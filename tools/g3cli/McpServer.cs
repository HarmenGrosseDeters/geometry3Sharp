// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace g3cli
{
    /// <summary>
    /// Model Context Protocol server over stdio: newline-delimited JSON-RPC 2.0 messages. Implements
    /// initialize, ping, tools/list and tools/call; each command of Commands is one tool. A failing
    /// operation is a tool result with isError = true (and the result code in structuredContent), not
    /// a JSON-RPC error; those are only for malformed requests, unknown methods and unknown tools.
    /// Requests are handled one after another.
    /// </summary>
    public class McpServer
    {
        public static readonly string[] ProtocolVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        readonly TextReader input;
        readonly TextWriter output;

        public McpServer(TextReader input, TextWriter output)
        {
            this.input = input; this.output = output;
        }

        /// <summary> Serves until stdin closes. </summary>
        public void Run()
        {
            string line;
            while ((line = input.ReadLine()) != null) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                JsonNode response = Handle(line);
                if (response != null) {
                    output.WriteLine(response.ToJsonString(Cli.Compact));
                    output.Flush();
                }
            }
        }

        /// <summary> Response to one message, null for notifications. </summary>
        public JsonNode Handle(string line)
        {
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(line);
            } catch (JsonException e) {
                return ErrorResponse(null, -32700, "parse error: " + e.Message);
            }
            using (doc) {
                JsonElement msg = doc.RootElement;
                if (msg.ValueKind == JsonValueKind.Array) {
                    // batches (2025-03-26): answer each request
                    var responses = new JsonArray();
                    foreach (JsonElement m in msg.EnumerateArray()) {
                        JsonNode r = HandleMessage(m);
                        if (r != null) responses.Add(r);
                    }
                    return responses.Count > 0 ? responses : null;
                }
                return HandleMessage(msg);
            }
        }

        JsonNode HandleMessage(JsonElement msg)
        {
            if (msg.ValueKind != JsonValueKind.Object || msg.TryGetProperty("method", out JsonElement methodEl) == false
                || methodEl.ValueKind != JsonValueKind.String) {
                // a response from the client (we send no requests) or garbage
                if (msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("id", out JsonElement rid) && (msg.TryGetProperty("result", out _) || msg.TryGetProperty("error", out _)))
                    return null;
                return ErrorResponse(msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("id", out JsonElement bad) ? JsonNode.Parse(bad.GetRawText()) : null,
                    -32600, "invalid request");
            }
            string method = methodEl.GetString();
            bool isNotification = msg.TryGetProperty("id", out JsonElement idEl) == false;
            JsonNode id = isNotification ? null : JsonNode.Parse(idEl.GetRawText());
            JsonElement? prms = msg.TryGetProperty("params", out JsonElement p) ? p : (JsonElement?)null;

            if (isNotification)
                return null;        // notifications/initialized, notifications/cancelled, ...

            try {
                switch (method) {
                    case "initialize": return Response(id, Initialize(prms));
                    case "ping": return Response(id, new JsonObject());
                    case "tools/list": return Response(id, ToolsList());
                    case "tools/call": return ToolsCall(id, prms);
                    default: return ErrorResponse(id, -32601, "method not found: " + method);
                }
            } catch (Exception e) {
                return ErrorResponse(id, -32603, "internal error: " + e.Message);
            }
        }

        static JsonObject Initialize(JsonElement? prms)
        {
            string requested = null;
            if (prms.HasValue && prms.Value.ValueKind == JsonValueKind.Object
                && prms.Value.TryGetProperty("protocolVersion", out JsonElement v) && v.ValueKind == JsonValueKind.String)
                requested = v.GetString();
            string version = ProtocolVersions.Contains(requested) ? requested : ProtocolVersions[0];
            return new JsonObject() {
                ["protocolVersion"] = version,
                ["capabilities"] = new JsonObject() { ["tools"] = new JsonObject() { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject() { ["name"] = "g3cli", ["title"] = "geometry3Sharp mesh tools", ["version"] = Commands.Version },
                ["instructions"] = "Mesh operations on files (paths relative to the server's working directory): measure, repair, boolean, " +
                    "resolve, remesh, simplify, convert. Every tool returns a JSON result with status and code; isError is set when code is not 'ok'.",
            };
        }

        static JsonObject ToolsList()
        {
            var tools = new JsonArray();
            foreach (Command c in Commands.All) {
                tools.Add(new JsonObject() {
                    ["name"] = c.Name,
                    ["description"] = c.Description,
                    ["inputSchema"] = c.InputSchema(),
                });
            }
            return new JsonObject() { ["tools"] = tools };
        }

        static JsonNode ToolsCall(JsonNode id, JsonElement? prms)
        {
            if (prms.HasValue == false || prms.Value.ValueKind != JsonValueKind.Object
                || prms.Value.TryGetProperty("name", out JsonElement nameEl) == false || nameEl.ValueKind != JsonValueKind.String)
                return ErrorResponse(id, -32602, "tools/call needs params.name");
            Command cmd = Commands.Find(nameEl.GetString());
            if (cmd == null)
                return ErrorResponse(id, -32602, "unknown tool: " + nameEl.GetString());

            JsonElement? arguments = prms.Value.TryGetProperty("arguments", out JsonElement a) ? a : (JsonElement?)null;
            JsonObject result;
            try {
                result = Commands.Run(cmd, cmd.ParseJson(arguments));
            } catch (CommandException e) {
                result = Commands.Error(cmd.Name, e.Code, e.Message);
            }
            bool isError = (string)result["code"] != ResultCodes.Ok;
            return Response(id, new JsonObject() {
                ["content"] = new JsonArray(new JsonObject() { ["type"] = "text", ["text"] = result.ToJsonString(Cli.Indented) }),
                ["structuredContent"] = result,
                ["isError"] = isError,
            });
        }

        static JsonObject Response(JsonNode id, JsonNode result)
        {
            return new JsonObject() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }

        static JsonObject ErrorResponse(JsonNode id, int code, string message)
        {
            return new JsonObject() {
                ["jsonrpc"] = "2.0", ["id"] = id,
                ["error"] = new JsonObject() { ["code"] = code, ["message"] = message },
            };
        }
    }
}
