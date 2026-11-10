// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The MCP request parser of the command line tool (g3cli.McpServer.Handle) as a fuzz target. Compiled where g3cli is referenced
// (the fuzz harness and the g3cli tests).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReaderFuzz
{
    public static class McpTarget
    {
        public static readonly FuzzTarget Target = new FuzzTarget { Name = "mcp", Text = true, MakeSeeds = Seeds, Check = Check };

        static List<byte[]> Seeds()
        {
            return new List<byte[]> {
                Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}"),
                Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":\"a\",\"method\":\"tools/list\"}"),
                Encoding.UTF8.GetBytes("[{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"},{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}]")
            };
        }

        // Tool calls read and write files named in their arguments and run operations: they are not parsed here, only the
        // protocol layer (messages, batches, methods, parameters) is. An input that names a tool call is not passed on.
        static string Check(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);
            // conservative: any JSON encoding of the method "tools/call" holds either the literal "call" or a backslash escape; resources/* likewise
            if (text.IndexOf("call", StringComparison.Ordinal) >= 0 || text.IndexOf("resource", StringComparison.Ordinal) >= 0 || text.IndexOf('\\') >= 0) return "";
            try {
                new g3cli.McpServer(new System.IO.StringReader(""), new System.IO.StringWriter()).Handle(text);
                return "";
            } catch (Exception e) {
                // Handle answers malformed JSON with a JSON-RPC error: it must not throw
                return "threw " + e.GetType().Name + ": " + e.Message + " at " + string.Join(" < ", (e.StackTrace ?? "").Split('\n').Select(s => s.Trim()).Take(4).ToArray());
            }
        }
    }
}
