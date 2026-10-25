// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace g3cli
{
    /// <summary>
    /// Command line front end: g3cli &lt;command&gt; [args], one JSON object on stdout, exit code from
    /// ResultCodes.ExitCode. "g3cli mcp" runs the MCP server instead.
    /// </summary>
    public static class Cli
    {
        // relaxed escaping: paths and messages stay readable (no \u0027 for quotes); the output is never embedded in HTML
        public static readonly JsonSerializerOptions Indented = new JsonSerializerOptions() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        public static readonly JsonSerializerOptions Compact = new JsonSerializerOptions() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static int Run(string[] argv, TextWriter stdout, TextReader stdin = null)
        {
            var tokens = argv.ToList();
            bool compact = tokens.Remove("--compact");
            JsonSerializerOptions json = compact ? Compact : Indented;

            JsonObject result = Execute(tokens.ToArray(), stdout, stdin, out bool ranServer);
            if (ranServer)
                return 0;
            stdout.WriteLine(result.ToJsonString(json));
            stdout.Flush();
            return ResultCodes.ExitCode((string)result["code"]);
        }

        static JsonObject Execute(string[] tokens, TextWriter stdout, TextReader stdin, out bool ranServer)
        {
            ranServer = false;
            string name = tokens.Length > 0 ? tokens[0] : "help";
            var rest = tokens.Skip(1).ToList();

            if (name == "help" || name == "--help" || name == "-h") {
                if (rest.Count == 0)
                    return Help();
                Command c = Commands.Find(rest[0]);
                return c != null ? CommandHelp(c)
                    : Commands.Error("help", ResultCodes.UnknownCommand, $"unknown command '{rest[0]}'");
            }
            if (name == "version" || name == "--version")
                return Commands.Ok("version", new JsonObject() { ["version"] = Commands.Version });
            if (name == "mcp") {
                ranServer = true;
                new McpServer(stdin ?? Console.In, stdout).Run();
                return null;
            }

            Command cmd = Commands.Find(name);
            if (cmd == null)
                return Commands.Error(name, ResultCodes.UnknownCommand, $"unknown command '{name}', see 'g3cli help'");
            if (rest.Contains("--help") || rest.Contains("-h"))
                return CommandHelp(cmd);
            Args args;
            try {
                args = cmd.ParseCommandLine(rest);
            } catch (CommandException e) {
                return Commands.Error(cmd.Name, e.Code, e.Message);
            }
            return Commands.Run(cmd, args);
        }

        static JsonObject Help()
        {
            var cmds = new JsonArray();
            foreach (Command c in Commands.All)
                cmds.Add(new JsonObject() { ["name"] = c.Name, ["description"] = c.Description, ["usage"] = Usage(c) });
            cmds.Add(new JsonObject() { ["name"] = "mcp", ["description"] = "Run as MCP server (JSON-RPC over stdio) with the commands above as tools.", ["usage"] = "g3cli mcp" });
            return Commands.Ok("help", new JsonObject() {
                ["version"] = Commands.Version,
                ["usage"] = "g3cli <command> [args] [--compact]; g3cli help <command> for the arguments",
                ["commands"] = cmds,
                ["result_codes"] = new JsonObject(ResultCodes.All.Select(c =>
                    new System.Collections.Generic.KeyValuePair<string, JsonNode>(c, ResultCodes.ExitCode(c)))),
                ["read_formats"] = new JsonArray(MeshFiles.ReadFormats.Select(f => (JsonNode)f).ToArray()),
                ["write_formats"] = new JsonArray(MeshFiles.WriteFormats.Select(f => (JsonNode)f).ToArray()),
            });
        }

        static JsonObject CommandHelp(Command c)
        {
            var args = new JsonObject();
            foreach (Param p in c.Params) {
                JsonObject s = p.Schema();
                if (p.Required) s["required"] = true;
                if (p.Positional) s["positional"] = true;
                args[p.Name] = s;
            }
            return Commands.Ok("help", new JsonObject() {
                ["name"] = c.Name, ["description"] = c.Description, ["usage"] = Usage(c), ["arguments"] = args
            });
        }

        static string Usage(Command c)
        {
            var parts = c.Params.Where(p => p.Positional).Select(p => "<" + p.Name + ">")
                .Concat(c.Params.Where(p => p.Positional == false && p.Required).Select(p => "--" + p.Name.Replace('_', '-') + " <" + p.Name + ">"));
            return "g3cli " + c.Name + " " + string.Join(" ", parts) + (c.Params.Any(p => p.Required == false) ? " [options]" : "");
        }
    }
}
