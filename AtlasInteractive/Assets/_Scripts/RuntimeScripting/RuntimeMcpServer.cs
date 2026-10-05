using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class RuntimeMcpServer : MonoBehaviour
{
    private const string ModernProtocolVersion = "2026-07-28";
    private const string LegacyProtocolVersion = "2025-11-25";

    [SerializeField] private RuntimeMcpToolRouter _toolRouter;
    [SerializeField] private int _port = 8765;

    private readonly ConcurrentQueue<MainThreadWorkItem> _mainThreadQueue = new();

    private TcpListener _listener;
    private bool _running;
    private bool _processingMainThreadWork;

    private void Awake()
    {
        if (_toolRouter == null) throw new InvalidOperationException("RuntimeMcpToolRouter is required.");
    }

    private void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        _running = true;

        Task.Run(AcceptLoop);

        Debug.Log($"MCP server listening: http://127.0.0.1:{_port}/mcp");
    }

    private void Update()
    {
        if (_processingMainThreadWork) return;
        if (!_mainThreadQueue.TryDequeue(out MainThreadWorkItem workItem)) return;

        _processingMainThreadWork = true;
        ExecuteMainThreadWork(workItem);
    }

    private void OnDestroy()
    {
        _running = false;

        try
        {
            _listener?.Stop();
        }
        catch
        {
        }

        _listener = null;
    }

    private async Task AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch
            {
                if (!_running) break;
                continue;
            }

            _ = Task.Run(() => HandleClient(client));
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                HttpRequest request = await ReadRequest(stream);

                if (request == null) return;

                if (request.Method == "OPTIONS")
                {
                    await WriteResponse(stream, 204, null);
                    return;
                }

                string path = request.Path.Split('?')[0];

                if (path != "/mcp" && path != "/mcp/")
                {
                    await WriteResponse(stream, 404, "{\"error\":\"Not found\"}");
                    return;
                }

                if (!IsOriginAllowed(request.Headers))
                {
                    await WriteResponse(stream, 403, "{\"error\":\"Origin not allowed\"}");
                    return;
                }

                if (request.Method != "POST")
                {
                    await WriteResponse(stream, 405, "{\"error\":\"Method not allowed\"}");
                    return;
                }

                JObject rpcRequest;

                try
                {
                    rpcRequest = JObject.Parse(request.Body);
                }
                catch
                {
                    JObject parseError = CreateErrorResponse(null, -32700, "Parse error");
                    await WriteResponse(stream, 400, parseError.ToString(Formatting.None));
                    return;
                }

                JObject rpcResponse = await HandleRpcRequest(rpcRequest, request.Headers);

                if (rpcResponse == null)
                {
                    await WriteResponse(stream, 202, null);
                    return;
                }

                await WriteResponse(stream, 200, rpcResponse.ToString(Formatting.None));
            }
            catch (Exception e)
            {
                JObject error = CreateErrorResponse(null, -32603, e.Message);

                try
                {
                    await WriteResponse(stream, 500, error.ToString(Formatting.None));
                }
                catch
                {
                }
            }
        }
    }

    private async Task<JObject> HandleRpcRequest(JObject request, Dictionary<string, string> headers)
    {
        JToken id = request["id"]?.DeepClone();

        if (request.Value<string>("jsonrpc") != "2.0")
        {
            return CreateErrorResponse(id, -32600, "Invalid Request");
        }

        string method = request.Value<string>("method");

        if (string.IsNullOrWhiteSpace(method))
        {
            return CreateErrorResponse(id, -32600, "Missing method");
        }

        switch (method)
        {
            case "server/discover":
                {
                    JObject result = new JObject
                    {
                        ["supportedVersions"] = new JArray(
                            ModernProtocolVersion,
                            LegacyProtocolVersion,
                            "2025-06-18",
                            "2025-03-26",
                            "2024-11-05"
                        ),
                        ["capabilities"] = new JObject
                        {
                            ["tools"] = new JObject()
                        },
                        ["instructions"] = "Runtime Unity scene manipulation server for ATLAS.",
                        ["ttlMs"] = 0,
                        ["cacheScope"] = "private"
                    };

                    StampModernResult(result);

                    return CreateResultResponse(id, result);
                }

            case "initialize":
                {
                    JObject parameters = request["params"] as JObject;
                    string requestedVersion = parameters?.Value<string>("protocolVersion");

                    string version = requestedVersion switch
                    {
                        "2025-11-25" => "2025-11-25",
                        "2025-06-18" => "2025-06-18",
                        "2025-03-26" => "2025-03-26",
                        "2024-11-05" => "2024-11-05",
                        _ => LegacyProtocolVersion
                    };

                    JObject result = new JObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JObject
                        {
                            ["tools"] = new JObject()
                        },
                        ["serverInfo"] = new JObject
                        {
                            ["name"] = "atlas-unity-runtime",
                            ["version"] = "0.1.0"
                        },
                        ["instructions"] = "Runtime Unity scene manipulation server for ATLAS."
                    };

                    return CreateResultResponse(id, result);
                }

            case "notifications/initialized":
            case "notifications/cancelled":
                return null;

            case "ping":
                return CreateResultResponse(id, new JObject());

            case "tools/list":
                {
                    JObject result = new JObject
                    {
                        ["tools"] = _toolRouter.GetToolDefinitions()
                    };

                    if (IsModernRequest(request, headers))
                    {
                        result["ttlMs"] = 0;
                        result["cacheScope"] = "private";
                        StampModernResult(result);
                    }

                    return CreateResultResponse(id, result);
                }

            case "tools/call":
                {
                    JObject parameters = request["params"] as JObject;

                    if (parameters == null)
                    {
                        return CreateErrorResponse(id, -32602, "Missing params");
                    }

                    string toolName = parameters.Value<string>("name");

                    if (string.IsNullOrWhiteSpace(toolName))
                    {
                        return CreateErrorResponse(id, -32602, "Missing tool name");
                    }

                    JObject arguments = parameters["arguments"] as JObject ?? new JObject();

                    JObject result = await RunOnMainThread(() => _toolRouter.CallAsync(toolName, arguments));

                    if (IsModernRequest(request, headers))
                    {
                        StampModernResult(result);
                    }

                    return CreateResultResponse(id, result);
                }

            default:
                return id == null ? null : CreateErrorResponse(id, -32601, $"Method not found: {method}");
        }
    }

    private Task<JObject> RunOnMainThread(Func<Task<JObject>> action)
    {
        TaskCompletionSource<JObject> completion = new TaskCompletionSource<JObject>();

        _mainThreadQueue.Enqueue(new MainThreadWorkItem
        {
            Action = action,
            Completion = completion
        });

        return completion.Task;
    }

    private async void ExecuteMainThreadWork(MainThreadWorkItem workItem)
    {
        try
        {
            JObject result = await workItem.Action();
            workItem.Completion.TrySetResult(result);
        }
        catch (Exception e)
        {
            workItem.Completion.TrySetException(e);
        }
        finally
        {
            _processingMainThreadWork = false;
        }
    }

    private static bool IsModernRequest(JObject request, Dictionary<string, string> headers)
    {
        if (headers.TryGetValue("MCP-Protocol-Version", out string headerVersion))
        {
            if (headerVersion == ModernProtocolVersion) return true;
        }

        JToken meta = request["params"]?["_meta"];
        string metaVersion = meta?["io.modelcontextprotocol/protocolVersion"]?.Value<string>();

        return metaVersion == ModernProtocolVersion;
    }

    private static void StampModernResult(JObject result)
    {
        result["resultType"] = "complete";

        JObject meta = result["_meta"] as JObject ?? new JObject();

        meta["io.modelcontextprotocol/serverInfo"] = new JObject
        {
            ["name"] = "atlas-unity-runtime",
            ["version"] = "0.1.0"
        };

        result["_meta"] = meta;
    }

    private static JObject CreateResultResponse(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id ?? JValue.CreateNull(),
            ["result"] = result
        };
    }

    private static JObject CreateErrorResponse(JToken id, int code, string message)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id ?? JValue.CreateNull(),
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private static bool IsOriginAllowed(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Origin", out string origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri uri)) return false;

        return uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "::1";
    }

    private static async Task<HttpRequest> ReadRequest(NetworkStream stream)
    {
        const int maxHeaderSize = 64 * 1024;
        const int maxBodySize = 4 * 1024 * 1024;

        using MemoryStream headerStream = new MemoryStream();

        byte[] singleByte = new byte[1];

        int state = 0;

        while (true)
        {
            int read = await stream.ReadAsync(singleByte, 0, 1);

            if (read == 0) return null;

            byte value = singleByte[0];
            headerStream.WriteByte(value);

            if (headerStream.Length > maxHeaderSize) throw new InvalidDataException("HTTP headers too large.");

            switch (state)
            {
                case 0:
                    state = value == '\r' ? 1 : 0;
                    break;

                case 1:
                    state = value == '\n' ? 2 : 0;
                    break;

                case 2:
                    state = value == '\r' ? 3 : 0;
                    break;

                case 3:
                    if (value == '\n') goto HeadersComplete;
                    state = 0;
                    break;
            }
        }

    HeadersComplete:

        byte[] headerBytes = headerStream.ToArray();
        string headerText = Encoding.ASCII.GetString(headerBytes, 0, headerBytes.Length - 4);

        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);

        if (lines.Length == 0) throw new InvalidDataException("Invalid HTTP request.");

        string[] requestLine = lines[0].Split(' ');

        if (requestLine.Length < 2) throw new InvalidDataException("Invalid HTTP request line.");

        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            int separator = lines[i].IndexOf(':');

            if (separator <= 0) continue;

            string name = lines[i].Substring(0, separator).Trim();
            string value = lines[i].Substring(separator + 1).Trim();

            headers[name] = value;
        }

        if (headers.TryGetValue("Transfer-Encoding", out string transferEncoding) &&
            transferEncoding.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new NotSupportedException("Chunked HTTP request bodies are not supported.");
        }

        int contentLength = 0;

        if (headers.TryGetValue("Content-Length", out string contentLengthString))
        {
            if (!int.TryParse(contentLengthString, out contentLength)) throw new InvalidDataException("Invalid Content-Length.");
        }

        if (contentLength < 0 || contentLength > maxBodySize) throw new InvalidDataException("HTTP body too large.");

        byte[] bodyBytes = new byte[contentLength];

        int offset = 0;

        while (offset < contentLength)
        {
            int read = await stream.ReadAsync(bodyBytes, offset, contentLength - offset);

            if (read == 0) throw new EndOfStreamException();

            offset += read;
        }

        return new HttpRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = requestLine[1],
            Headers = headers,
            Body = Encoding.UTF8.GetString(bodyBytes)
        };
    }

    private static async Task WriteResponse(NetworkStream stream, int statusCode, string body)
    {
        byte[] bodyBytes = body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);

        string reason = statusCode switch
        {
            200 => "OK",
            202 => "Accepted",
            204 => "No Content",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            _ => "OK"
        };

        StringBuilder headers = new StringBuilder();

        headers.Append($"HTTP/1.1 {statusCode} {reason}\r\n");

        if (bodyBytes.Length > 0) headers.Append("Content-Type: application/json; charset=utf-8\r\n");

        headers.Append($"Content-Length: {bodyBytes.Length}\r\n");
        headers.Append("Connection: close\r\n");
        headers.Append("\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers.ToString());

        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
        }

        await stream.FlushAsync();
    }

    private sealed class MainThreadWorkItem
    {
        public Func<Task<JObject>> Action;
        public TaskCompletionSource<JObject> Completion;
    }

    private sealed class HttpRequest
    {
        public string Method;
        public string Path;
        public Dictionary<string, string> Headers;
        public string Body;
    }
}