using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Meshing2Acceptance
{
    /// <summary>
    /// Just enough JSON for the golden files: objects, arrays, strings, integers, booleans, null. Written by hand because this
    /// project must build for net48 without extra packages. Objects keep their key order, so a diff of a golden file stays small.
    /// </summary>
    public static class MiniJson
    {
        public sealed class Obj : List<KeyValuePair<string, object>>
        {
            public object this[string key] {
                get { foreach (var p in this) if (p.Key == key) return p.Value; return null; }
            }
            public void Put(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
        }

        public static object Parse(string text)
        {
            int i = 0;
            var value = ParseValue(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("trailing text at " + i);
            return value;
        }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) ++i; }

        static object ParseValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') {
                var o = new Obj(); ++i; Skip(s, ref i);
                if (s[i] == '}') { ++i; return o; }
                while (true) {
                    Skip(s, ref i);
                    string key = ParseString(s, ref i);
                    Skip(s, ref i);
                    if (s[i++] != ':') throw new FormatException("expected : at " + i);
                    o.Put(key, ParseValue(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') { ++i; continue; }
                    if (s[i] == '}') { ++i; return o; }
                    throw new FormatException("expected , or } at " + i);
                }
            }
            if (c == '[') {
                var a = new List<object>(); ++i; Skip(s, ref i);
                if (s[i] == ']') { ++i; return a; }
                while (true) {
                    a.Add(ParseValue(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') { ++i; continue; }
                    if (s[i] == ']') { ++i; return a; }
                    throw new FormatException("expected , or ] at " + i);
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-')) ++i;
            if (start == i) throw new FormatException("unexpected character at " + i);
            return long.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected a string at " + i);
            var sb = new StringBuilder();
            for (++i; ; ++i) {
                char c = s[i];
                if (c == '"') { ++i; return sb.ToString(); }
                if (c != '\\') { sb.Append(c); continue; }
                c = s[++i];
                switch (c) {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(c); break;
                }
            }
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value, int depth)
        {
            switch (value) {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case Obj o:
                    // a row is one object per line in arrays of rows: short objects stay readable and diffs stay one row long
                    sb.Append('{');
                    for (int i = 0; i < o.Count; ++i) {
                        sb.Append(i == 0 ? "" : ", ");
                        WriteString(sb, o[i].Key);
                        sb.Append(": ");
                        WriteValue(sb, o[i].Value, depth + 1);
                    }
                    sb.Append('}');
                    break;
                case List<object> a:
                    sb.Append('[');
                    for (int i = 0; i < a.Count; ++i) {
                        sb.Append(i == 0 ? "\n  " : ",\n  ");
                        WriteValue(sb, a[i], depth + 1);
                    }
                    sb.Append(a.Count == 0 ? "]" : "\n]");
                    break;
                default: throw new ArgumentException("not JSON: " + value.GetType());
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
