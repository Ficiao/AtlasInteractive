using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public sealed class RuntimeMcpClient : MonoBehaviour
{
	[SerializeField] private string _endpoint = "http://127.0.0.1:8765/mcp";
	[SerializeField] private int _timeoutSeconds = 60;

	private int _requestId;

	public async Task<JArray> ListToolsAsync()
	{
		JObject request = new JObject
		{
			["jsonrpc"] = "2.0",
			["id"] = ++_requestId,
			["method"] = "tools/list",
			["params"] = new JObject
			{
				["_meta"] = CreateMeta()
			}
		};

		JObject response = await SendAsync(request, "tools/list", null);

		return response["result"]?["tools"] as JArray ?? new JArray();
	}

	public async Task<JObject> CallToolAsync(string toolName, JObject arguments)
	{
		JObject request = new JObject
		{
			["jsonrpc"] = "2.0",
			["id"] = ++_requestId,
			["method"] = "tools/call",
			["params"] = new JObject
			{
				["name"] = toolName,
				["arguments"] = arguments ?? new JObject(),
				["_meta"] = CreateMeta()
			}
		};

		JObject response = await SendAsync(request, "tools/call", toolName);

		return response["result"] as JObject ?? throw new InvalidOperationException("MCP tools/call returned no result.");
	}

	private async Task<JObject> SendAsync(JObject body, string method, string toolName)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(body.ToString());

		UnityWebRequest request = new UnityWebRequest(_endpoint, UnityWebRequest.kHttpVerbPOST);
		request.uploadHandler = new UploadHandlerRaw(bytes);
		request.downloadHandler = new DownloadHandlerBuffer();
		request.timeout = _timeoutSeconds;

		request.SetRequestHeader("Content-Type", "application/json");
		request.SetRequestHeader("MCP-Protocol-Version", "2026-07-28");
		request.SetRequestHeader("Mcp-Method", method);

		if (!string.IsNullOrWhiteSpace(toolName))
		{
			request.SetRequestHeader("Mcp-Name", toolName);
		}

		string responseText = await SendWebRequestAsync(request);

		JObject response = JObject.Parse(responseText);

		if (response["error"] != null)
		{
			throw new InvalidOperationException($"MCP error: {response["error"]}");
		}

		return response;
	}

	private static JObject CreateMeta()
	{
		return new JObject
		{
			["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
			["io.modelcontextprotocol/clientCapabilities"] = new JObject()
		};
	}

	private static Task<string> SendWebRequestAsync(UnityWebRequest request)
	{
		TaskCompletionSource<string> completion = new TaskCompletionSource<string>();

		UnityWebRequestAsyncOperation operation = request.SendWebRequest();

		operation.completed += _ =>
		{
			try
			{
				string response = request.downloadHandler?.text ?? "";

				if (request.result != UnityWebRequest.Result.Success)
				{
					completion.TrySetException(new InvalidOperationException(
						$"HTTP {request.responseCode}: {request.error}\n{response}"
					));
				}
				else
				{
					completion.TrySetResult(response);
				}
			}
			finally
			{
				request.Dispose();
			}
		};

		return completion.Task;
	}
}