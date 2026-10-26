// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace g3cli
{
    /// <summary>
    /// Result codes of a command. Every command returns exactly one of these in its JSON result
    /// ("code"), the CLI also returns the matching process exit code.
    /// </summary>
    public static class ResultCodes
    {
        public const string Ok = "ok";
        /// <summary> the operation ran but could not produce a result (eg boolean of meshes with non-finite coordinates) </summary>
        public const string OperationFailed = "operation_failed";
        /// <summary> the input mesh has no triangles </summary>
        public const string EmptyMesh = "empty_mesh";
        /// <summary> missing, unknown or malformed arguments </summary>
        public const string InvalidArguments = "invalid_arguments";
        public const string UnknownCommand = "unknown_command";
        /// <summary> input file missing, unreadable or not parseable </summary>
        public const string ReadError = "read_error";
        /// <summary> file extension of an input or output is not a supported mesh format </summary>
        public const string UnknownFormat = "unknown_format";
        public const string WriteError = "write_error";
        /// <summary> unexpected exception (a bug) </summary>
        public const string InternalError = "internal_error";

        public static int ExitCode(string code)
        {
            switch (code) {
                case Ok: return 0;
                case OperationFailed: case EmptyMesh: return 1;
                case InvalidArguments: case UnknownCommand: return 2;
                case ReadError: case UnknownFormat: return 3;
                case WriteError: return 4;
                default: return 70;
            }
        }

        public static readonly string[] All = {
            Ok, OperationFailed, EmptyMesh, InvalidArguments, UnknownCommand, ReadError, UnknownFormat, WriteError, InternalError
        };
    }


    /// <summary> Thrown inside a command to end it with a result code; never leaves CommandRunner. </summary>
    public class CommandException : Exception
    {
        public string Code;
        public CommandException(string code, string message) : base(message) { Code = code; }
    }


    public enum ParamType { String, Number, Integer, Boolean }


    /// <summary> A parameter of a command: CLI option (--name, or positional) and MCP tool argument at once. </summary>
    public class Param
    {
        public string Name;
        public ParamType Type;
        public string Description;
        public bool Required;
        public object Default;
        public string[] Enum;
        /// <summary> may be given on the command line without --name, in this order </summary>
        public bool Positional;

        public Param(string name, ParamType type, string description)
        {
            Name = name; Type = type; Description = description;
        }

        public JsonObject Schema()
        {
            var o = new JsonObject();
            o["type"] = Type == ParamType.String ? "string" : Type == ParamType.Number ? "number"
                : Type == ParamType.Integer ? "integer" : "boolean";
            o["description"] = Description;
            if (Enum != null)
                o["enum"] = new JsonArray(Enum.Select(e => (JsonNode)e).ToArray());
            if (Default is bool b)
                o["default"] = b;
            else if (Default is int i)
                o["default"] = i;
            else if (Default is double d)
                o["default"] = d;
            else if (Default is string str)
                o["default"] = str;
            return o;
        }
    }


    public class Command
    {
        public string Name;
        public string Description;
        public List<Param> Params = new List<Param>();
        public Func<Args, JsonObject> Run;

        public Param Find(string name)
        {
            return Params.FirstOrDefault(p => p.Name == name);
        }

        /// <summary> JSON schema of the arguments (MCP inputSchema) </summary>
        public JsonObject InputSchema()
        {
            var props = new JsonObject();
            foreach (Param p in Params)
                props[p.Name] = p.Schema();
            var schema = new JsonObject() { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
            var req = Params.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray();
            if (req.Length > 0)
                schema["required"] = new JsonArray(req);
            return schema;
        }

        /// <summary> Validated arguments from command line tokens (after the command name). </summary>
        public Args ParseCommandLine(IList<string> tokens)
        {
            var raw = new Dictionary<string, string>();
            var positional = Params.Where(p => p.Positional).ToList();
            int nextPositional = 0;
            for (int i = 0; i < tokens.Count; ++i) {
                string t = tokens[i];
                if (t.StartsWith("--") && t.Length > 2) {
                    string name = t.Substring(2), value = null;
                    int eq = name.IndexOf('=');
                    if (eq >= 0) {
                        value = name.Substring(eq + 1);
                        name = name.Substring(0, eq);
                    }
                    name = name.Replace('-', '_');
                    Param p = Find(name);
                    if (p == null && name.StartsWith("no_") && Find(name.Substring(3))?.Type == ParamType.Boolean && value == null) {
                        p = Find(name.Substring(3));
                        value = "false";
                    }
                    if (p == null)
                        throw new CommandException(ResultCodes.InvalidArguments, $"unknown option --{name} for '{Name}'");
                    if (value == null) {
                        bool nextIsValue = i + 1 < tokens.Count && (tokens[i + 1].StartsWith("--") == false);
                        if (p.Type == ParamType.Boolean)
                            value = (nextIsValue && IsBoolText(tokens[i + 1])) ? tokens[++i] : "true";
                        else if (nextIsValue)
                            value = tokens[++i];
                        else
                            throw new CommandException(ResultCodes.InvalidArguments, $"option --{name} needs a value");
                    }
                    if (raw.ContainsKey(p.Name))
                        throw new CommandException(ResultCodes.InvalidArguments, $"option --{name} given twice");
                    raw[p.Name] = value;
                } else {
                    while (nextPositional < positional.Count && raw.ContainsKey(positional[nextPositional].Name))
                        nextPositional++;
                    if (nextPositional >= positional.Count)
                        throw new CommandException(ResultCodes.InvalidArguments, $"unexpected argument '{t}' for '{Name}'");
                    raw[positional[nextPositional++].Name] = t;
                }
            }
            var values = new Dictionary<string, object>();
            foreach (var kv in raw)
                values[kv.Key] = ConvertText(Find(kv.Key), kv.Value);
            return Validate(values);
        }

        /// <summary> Validated arguments from a JSON object (MCP tools/call arguments). </summary>
        public Args ParseJson(JsonElement? arguments)
        {
            var values = new Dictionary<string, object>();
            if (arguments.HasValue && arguments.Value.ValueKind != JsonValueKind.Null) {
                if (arguments.Value.ValueKind != JsonValueKind.Object)
                    throw new CommandException(ResultCodes.InvalidArguments, "arguments must be an object");
                foreach (JsonProperty prop in arguments.Value.EnumerateObject()) {
                    Param p = Find(prop.Name);
                    if (p == null)
                        throw new CommandException(ResultCodes.InvalidArguments, $"unknown argument '{prop.Name}' for '{Name}'");
                    if (prop.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    values[p.Name] = ConvertJson(p, prop.Value);
                }
            }
            return Validate(values);
        }

        Args Validate(Dictionary<string, object> values)
        {
            foreach (Param p in Params) {
                if (values.ContainsKey(p.Name) == false) {
                    if (p.Required)
                        throw new CommandException(ResultCodes.InvalidArguments, $"missing required argument '{p.Name}' for '{Name}'");
                    if (p.Default != null)
                        values[p.Name] = p.Default;
                } else if (p.Enum != null && p.Enum.Contains((string)values[p.Name]) == false) {
                    throw new CommandException(ResultCodes.InvalidArguments,
                        $"'{p.Name}' must be one of {string.Join(", ", p.Enum)}, got '{values[p.Name]}'");
                }
            }
            return new Args(values);
        }

        static bool IsBoolText(string s)
        {
            return s == "true" || s == "false" || s == "1" || s == "0";
        }

        static object ConvertText(Param p, string s)
        {
            switch (p.Type) {
                case ParamType.String:
                    return s;
                case ParamType.Number:
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
                        return d;
                    break;
                case ParamType.Integer:
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                        return i;
                    break;
                case ParamType.Boolean:
                    if (s == "true" || s == "1") return true;
                    if (s == "false" || s == "0") return false;
                    break;
            }
            throw new CommandException(ResultCodes.InvalidArguments, $"'{p.Name}' expects {TypeName(p.Type)}, got '{s}'");
        }

        static object ConvertJson(Param p, JsonElement e)
        {
            switch (p.Type) {
                case ParamType.String:
                    if (e.ValueKind == JsonValueKind.String) return e.GetString();
                    break;
                case ParamType.Number:
                    // as on the command line, numbers must be finite (1e400 would be infinity)
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v) && double.IsFinite(v)) return v;
                    break;
                case ParamType.Integer:
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int i)) return i;
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue)
                        return (int)d;
                    break;
                case ParamType.Boolean:
                    if (e.ValueKind == JsonValueKind.True) return true;
                    if (e.ValueKind == JsonValueKind.False) return false;
                    break;
            }
            throw new CommandException(ResultCodes.InvalidArguments, $"'{p.Name}' expects {TypeName(p.Type)}, got {e.GetRawText()}");
        }

        static string TypeName(ParamType t)
        {
            return t == ParamType.String ? "a string" : t == ParamType.Number ? "a number"
                : t == ParamType.Integer ? "an integer" : "true or false";
        }
    }


    /// <summary> Validated, typed arguments of one command call (defaults filled in). </summary>
    public class Args
    {
        readonly Dictionary<string, object> values;
        public Args(Dictionary<string, object> values) { this.values = values; }

        public bool Has(string name) { return values.ContainsKey(name); }
        public string String(string name) { return Has(name) ? (string)values[name] : null; }
        public double Number(string name) { return Convert.ToDouble(values[name], CultureInfo.InvariantCulture); }
        public int Integer(string name) { return (int)values[name]; }
        public bool Bool(string name) { return (bool)values[name]; }
    }
}
