using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace g3
{
    /// <summary>
    /// Minimal JSON parser and string escaping, used by the glTF reader/writer
    /// (netstandard2.0 has no built-in JSON library and g3 has no package dependencies).
    /// Parse() returns Dictionary&lt;string,object&gt;, List&lt;object&gt;, string, double, bool or null.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            Parser p = new Parser(json);
            p.SkipWhitespace();
            object result = p.ParseValue(0);
            p.SkipWhitespace();
            if (p.Pos != json.Length)
                throw new FormatException("MiniJson: unexpected data after JSON value at position " + p.Pos);
            return result;
        }


        /// <summary>
        /// Return s as a quoted JSON string literal
        /// </summary>
        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }


        class Parser
        {
            const int MaxDepth = 256;
            string s;
            public int Pos;

            public Parser(string s)
            {
                this.s = s;
            }

            public void SkipWhitespace()
            {
                while (Pos < s.Length && (s[Pos] == ' ' || s[Pos] == '\t' || s[Pos] == '\n' || s[Pos] == '\r'))
                    Pos++;
            }

            FormatException error(string msg)
            {
                return new FormatException("MiniJson: " + msg + " at position " + Pos);
            }

            public object ParseValue(int depth)
            {
                if (depth > MaxDepth)
                    throw error("nesting too deep");
                if (Pos >= s.Length)
                    throw error("unexpected end of data");
                char c = s[Pos];
                if (c == '{')
                    return parse_object(depth);
                if (c == '[')
                    return parse_array(depth);
                if (c == '"')
                    return parse_string();
                if (c == '-' || (c >= '0' && c <= '9'))
                    return parse_number();
                if (string.CompareOrdinal(s, Pos, "true", 0, 4) == 0) { Pos += 4; return true; }
                if (string.CompareOrdinal(s, Pos, "false", 0, 5) == 0) { Pos += 5; return false; }
                if (string.CompareOrdinal(s, Pos, "null", 0, 4) == 0) { Pos += 4; return null; }
                throw error("unexpected character '" + c + "'");
            }

            Dictionary<string, object> parse_object(int depth)
            {
                Dictionary<string, object> obj = new Dictionary<string, object>();
                Pos++;
                SkipWhitespace();
                if (Pos < s.Length && s[Pos] == '}') { Pos++; return obj; }
                while (true) {
                    SkipWhitespace();
                    if (Pos >= s.Length || s[Pos] != '"')
                        throw error("expected object key");
                    string key = parse_string();
                    SkipWhitespace();
                    if (Pos >= s.Length || s[Pos] != ':')
                        throw error("expected ':'");
                    Pos++;
                    SkipWhitespace();
                    obj[key] = ParseValue(depth + 1);
                    SkipWhitespace();
                    if (Pos >= s.Length)
                        throw error("unexpected end of object");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == '}') { Pos++; return obj; }
                    throw error("expected ',' or '}'");
                }
            }

            List<object> parse_array(int depth)
            {
                List<object> list = new List<object>();
                Pos++;
                SkipWhitespace();
                if (Pos < s.Length && s[Pos] == ']') { Pos++; return list; }
                while (true) {
                    SkipWhitespace();
                    list.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (Pos >= s.Length)
                        throw error("unexpected end of array");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == ']') { Pos++; return list; }
                    throw error("expected ',' or ']'");
                }
            }

            string parse_string()
            {
                Pos++;  // opening quote
                StringBuilder sb = null;
                int start = Pos;
                while (true) {
                    if (Pos >= s.Length)
                        throw error("unterminated string");
                    char c = s[Pos];
                    if (c == '"') {
                        string result = (sb == null) ? s.Substring(start, Pos - start) : sb.Append(s, start, Pos - start).ToString();
                        Pos++;
                        return result;
                    }
                    if (c == '\\') {
                        if (sb == null)
                            sb = new StringBuilder();
                        sb.Append(s, start, Pos - start);
                        Pos++;
                        if (Pos >= s.Length)
                            throw error("unterminated escape");
                        char e = s[Pos];
                        switch (e) {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (Pos + 4 >= s.Length)
                                    throw error("invalid unicode escape");
                                sb.Append((char)int.Parse(s.Substring(Pos + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                Pos += 4;
                                break;
                            default:
                                throw error("invalid escape '\\" + e + "'");
                        }
                        Pos++;
                        start = Pos;
                        continue;
                    }
                    Pos++;
                }
            }

            double parse_number()
            {
                int start = Pos;
                while (Pos < s.Length) {
                    char c = s[Pos];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                        Pos++;
                    else
                        break;
                }
                double d;
                if (double.TryParse(s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d) == false)
                    throw error("invalid number");
                return d;
            }
        }
    }
}
