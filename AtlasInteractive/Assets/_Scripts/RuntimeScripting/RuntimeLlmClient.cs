using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class RuntimeLlmClient : MonoBehaviour
{
    [SerializeField] private RuntimeMcpClient _mcpClient;
    [SerializeField] private int _maxToolRounds = 12;
    [SerializeField] private int _timeoutSeconds = 120;

    private readonly JArray _messages = new JArray();
    private readonly Dictionary<string, string> _modelToolToMcpTool = new Dictionary<string, string>();
    private readonly Dictionary<string, bool> _modelToolHasParameters = new Dictionary<string, bool>();

    private RuntimeLlmConfig _config;
    private JArray _modelTools;
    private HttpClient _httpClient;
    private SynchronizationContext _unityContext;

    private void Awake()
    {
        if (_mcpClient == null) throw new InvalidOperationException("RuntimeMcpClient is required.");

        _unityContext = SynchronizationContext.Current;

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

        LoadConfig();

        _messages.Add(new JObject
        {
            ["role"] = "system",
            ["content"] = _config.systemPrompt
        });
    }

    private void OnDestroy()
    {
        _httpClient?.Dispose();
    }

    public async Task<string> SendAsync(string userMessage, Action<string> onTextDelta = null)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return "";

        await EnsureToolsLoaded();

        _messages.Add(new JObject
        {
            ["role"] = "user",
            ["content"] = userMessage
        });

        for (int round = 0; round < _maxToolRounds; round++)
        {
            JObject request = new JObject
            {
                ["model"] = _config.model,
                ["messages"] = _messages.DeepClone(),
                ["tools"] = _modelTools,
                ["tool_choice"] = "auto",
                ["stream"] = true
            };

            StreamingRoundResult result = await SendStreamingRequest(request, onTextDelta);

            _messages.Add(result.AssistantMessage);

            if (result.ToolCalls.Count == 0)
            {
                Debug.Log($"LLM final response: {result.Content}");
                return result.Content;
            }

            foreach (JObject toolCall in result.ToolCalls)
            {
                await ExecuteToolCall(toolCall);
            }
        }

        throw new InvalidOperationException($"LLM exceeded maximum tool rounds ({_maxToolRounds}).");
    }

    public void ClearConversation()
    {
        _messages.Clear();

        _messages.Add(new JObject
        {
            ["role"] = "system",
            ["content"] = _config.systemPrompt
        });
    }

    private async Task<StreamingRoundResult> SendStreamingRequest(JObject body, Action<string> onTextDelta)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _config.endpoint);

        request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(_config.apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.apiKey);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}\n{error}"
            );
        }

        using Stream stream = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new StreamReader(stream);

        StringBuilder content = new StringBuilder();
        SortedDictionary<int, StreamingToolCall> toolCalls = new SortedDictionary<int, StreamingToolCall>();

        while (!reader.EndOfStream)
        {
            string line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string payload = line.Substring(5).Trim();

            if (payload == "[DONE]") break;

            JObject chunk;

            try
            {
                chunk = JObject.Parse(payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not parse LLM stream chunk:\n{payload}\n{e.Message}");
                continue;
            }

            JObject delta = chunk["choices"]?[0]?["delta"] as JObject;

            if (delta == null) continue;

            JToken contentToken = delta["content"];

            if (contentToken != null && contentToken.Type == JTokenType.String)
            {
                string text = contentToken.Value<string>();

                if (!string.IsNullOrEmpty(text))
                {
                    content.Append(text);
                    EmitTextDelta(onTextDelta, text);
                }
            }

            if (delta["tool_calls"] is not JArray calls) continue;

            foreach (JObject call in calls)
            {
                int index = call.Value<int?>("index") ?? 0;

                if (!toolCalls.TryGetValue(index, out StreamingToolCall accumulated))
                {
                    accumulated = new StreamingToolCall();
                    toolCalls.Add(index, accumulated);
                }

                string id = call.Value<string>("id");

                if (!string.IsNullOrWhiteSpace(id)) accumulated.Id = id;

                JObject function = call["function"] as JObject;

                if (function == null) continue;

                string namePart = function.Value<string>("name");
                string argumentsPart = function.Value<string>("arguments");

                if (!string.IsNullOrEmpty(namePart)) accumulated.Name.Append(namePart);
                if (!string.IsNullOrEmpty(argumentsPart)) accumulated.Arguments.Append(argumentsPart);
            }
        }

        JArray finalToolCalls = new JArray();

        foreach (KeyValuePair<int, StreamingToolCall> pair in toolCalls)
        {
            StreamingToolCall toolCall = pair.Value;

            string id = string.IsNullOrWhiteSpace(toolCall.Id)
                ? $"call_{Guid.NewGuid():N}"
                : toolCall.Id;

            string name = toolCall.Name.ToString();
            string arguments = toolCall.Arguments.ToString();

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("LLM returned a tool call without a function name.");
            }

            if (string.IsNullOrWhiteSpace(arguments)) arguments = "{}";

            finalToolCalls.Add(new JObject
            {
                ["id"] = id,
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = name,
                    ["arguments"] = arguments
                }
            });
        }

        Debug.Log($"LLM stream finished. Text chars: {content.Length}, tool calls: {finalToolCalls.Count}");

        JObject assistantMessage = new JObject
        {
            ["role"] = "assistant",
            ["content"] = content.Length > 0 ? content.ToString() : JValue.CreateNull()
        };

        if (finalToolCalls.Count > 0) assistantMessage["tool_calls"] = finalToolCalls.DeepClone();

        return new StreamingRoundResult
        {
            Content = content.ToString(),
            AssistantMessage = assistantMessage,
            ToolCalls = finalToolCalls
        };
    }

    private async Task ExecuteToolCall(JObject toolCall)
    {
        string callId = toolCall.Value<string>("id");
        JObject function = toolCall["function"] as JObject;

        if (string.IsNullOrWhiteSpace(callId) || function == null)
        {
            throw new InvalidOperationException($"Invalid tool call: {toolCall}");
        }

        string modelToolName = function.Value<string>("name");

        if (string.IsNullOrWhiteSpace(modelToolName))
        {
            AddToolResult(callId, ErrorPayload("Tool call is missing a function name."));
            return;
        }

        if (!_modelToolToMcpTool.TryGetValue(modelToolName, out string mcpToolName))
        {
            AddToolResult(callId, ErrorPayload($"Unknown tool: {modelToolName}"));
            return;
        }

        string argumentsJson = function.Value<string>("arguments");
        JObject arguments;

        try
        {
            arguments = string.IsNullOrWhiteSpace(argumentsJson)
                ? new JObject()
                : JObject.Parse(argumentsJson);
        }
        catch (Exception e)
        {
            bool parameterless = _modelToolHasParameters.TryGetValue(modelToolName, out bool hasParameters) && !hasParameters;

            RepairStoredToolArguments(callId, "{}");

            if (parameterless)
            {
                Debug.LogWarning($"LLM returned malformed arguments for parameterless tool {modelToolName}: '{argumentsJson}'. Using {{}} instead.");
                arguments = new JObject();
            }
            else
            {
                string error = $"Invalid JSON arguments for {modelToolName}: {argumentsJson}. {e.Message}";
                Debug.LogWarning(error);
                AddToolResult(callId, ErrorPayload(error));
                return;
            }
        }

        RepairStoredToolArguments(callId, arguments.ToString(Formatting.None));

        Debug.Log($"LLM tool call: {mcpToolName}\n{arguments.ToString(Formatting.Indented)}");

        try
        {
            JObject mcpResult = await _mcpClient.CallToolAsync(mcpToolName, arguments);
            string cleanResult = ExtractToolResult(mcpResult);

            Debug.Log($"LLM tool result: {mcpToolName}\n{cleanResult}");
            AddToolResult(callId, cleanResult);
        }
        catch (Exception e)
        {
            string error = ErrorPayload($"Tool execution failed for {mcpToolName}: {e.Message}");
            Debug.LogWarning(error);
            AddToolResult(callId, error);
        }
    }

    private void AddToolResult(string callId, string content)
    {
        _messages.Add(new JObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = callId,
            ["content"] = content
        });
    }

    private void RepairStoredToolArguments(string callId, string validArgumentsJson)
    {
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i] is not JObject message) continue;
            if (message["tool_calls"] is not JArray calls) continue;

            foreach (JObject call in calls)
            {
                if (call.Value<string>("id") != callId) continue;
                if (call["function"] is not JObject function) return;

                function["arguments"] = validArgumentsJson;
                return;
            }
        }
    }

    private static string ErrorPayload(string error)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = error
        }.ToString(Formatting.None);
    }

    private static string ExtractToolResult(JObject mcpResult)
    {
        JArray content = mcpResult["content"] as JArray;

        if (content == null || content.Count == 0) return mcpResult.ToString(Formatting.None);

        StringBuilder result = new StringBuilder();

        foreach (JObject item in content)
        {
            if (item.Value<string>("type") != "text") continue;

            string text = item.Value<string>("text");
            if (string.IsNullOrEmpty(text)) continue;

            if (result.Length > 0) result.Append('\n');
            result.Append(text);
        }

        return result.Length > 0 ? result.ToString() : mcpResult.ToString(Formatting.None);
    }

    private async Task EnsureToolsLoaded()
    {
        if (_modelTools != null) return;

        JArray mcpTools = await _mcpClient.ListToolsAsync();

        _modelTools = new JArray();
        _modelToolToMcpTool.Clear();
        _modelToolHasParameters.Clear();

        foreach (JObject mcpTool in mcpTools)
        {
            string mcpName = mcpTool.Value<string>("name");
            if (string.IsNullOrWhiteSpace(mcpName)) continue;

            string modelName = ToModelToolName(mcpName);
            JObject schema = mcpTool["inputSchema"]?.DeepClone() as JObject ?? new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            };

            JObject properties = schema["properties"] as JObject;

            _modelToolToMcpTool.Add(modelName, mcpName);
            _modelToolHasParameters.Add(modelName, properties != null && properties.Count > 0);

            JObject function = new JObject
            {
                ["name"] = modelName,
                ["description"] = mcpTool.Value<string>("description") ?? "",
                ["parameters"] = schema
            };

            _modelTools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = function
            });
        }

        Debug.Log($"Loaded {_modelTools.Count} MCP tools for LLM.");
    }

    private void LoadConfig()
    {
        string directory = Path.Combine(Application.persistentDataPath, "Private");
        string path = Path.Combine(directory, "llm.json");

        Directory.CreateDirectory(directory);

        if (!File.Exists(path))
        {
            RuntimeLlmConfig defaultConfig = new RuntimeLlmConfig
            {
                endpoint = "http://127.0.0.1:1234/v1/chat/completions",
                apiKey = "",
                model = "qwen2.5-7b-instruct-1m",
                systemPrompt = "You are an execution agent controlling a Unity runtime scene through tools. " +
                "Complete every requested action before answering. Tool results are authoritative. Runtime objects" +
                " may be referenced by stable objectId or exact unique name. Never guess asset paths: use asset__list_models, " +
                "asset__list_textures or asset__list_audio when the exact path is unknown. Parameterless tools use {}. " +
                "For Rigidbody operations ALWAYS use component__configure_rigidbody directly. It creates the Rigidbody automatically. " +
                "For continuous rotation ALWAYS use script__attach_rotation. Do not write custom Lua for a normal continuous " +
                "rotation request and do not add a Rigidbody for rotation. For other custom runtime behaviours use " +
                "script__create_and_attach. Lua uses a restricted API: move(x,y,z), setPosition(x,y,z), rotate(x,y,z), log(message). " +
                "Never use gameObject, transform, self, Rigidbody, UnityEngine, Time, Vector3, position, GetComponent, component:get " +
                "or other Unity APIs from Lua. scene__load restores hierarchy, supported components, assets and Lua scripts itself. " +
                "Never claim success unless the relevant tool returns success=true. Do not repeatedly retry the same failed call without " +
                "changing the cause. Keep final answers concise."
            };

            File.WriteAllText(path, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
        }

        _config = JsonConvert.DeserializeObject<RuntimeLlmConfig>(File.ReadAllText(path));

        if (_config == null) throw new InvalidDataException($"Invalid LLM config: {path}");
        if (string.IsNullOrWhiteSpace(_config.endpoint)) throw new InvalidDataException("LLM endpoint is missing.");
        if (string.IsNullOrWhiteSpace(_config.model)) throw new InvalidDataException("LLM model is missing.");

        Debug.Log($"LLM config loaded: {_config.endpoint} / {_config.model}");
    }

    private void EmitTextDelta(Action<string> callback, string text)
    {
        if (callback == null) return;

        if (_unityContext != null && SynchronizationContext.Current != _unityContext)
        {
            _unityContext.Post(_ => callback(text), null);
        }
        else
        {
            callback(text);
        }
    }

    private static string ToModelToolName(string mcpName)
    {
        return mcpName.Replace(".", "__");
    }

    private sealed class StreamingToolCall
    {
        public string Id;
        public readonly StringBuilder Name = new StringBuilder();
        public readonly StringBuilder Arguments = new StringBuilder();
    }

    private sealed class StreamingRoundResult
    {
        public string Content;
        public JObject AssistantMessage;
        public JArray ToolCalls;
    }
}

[Serializable]
public sealed class RuntimeLlmConfig
{
    public string endpoint;
    public string apiKey;
    public string model;
    public string systemPrompt;
}