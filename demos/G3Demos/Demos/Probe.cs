using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G3Demos.Demos
{
    /// <summary>
    /// Runs one demo case from JSON parameters and returns a one-line summary: the same code path as the UI, callable
    /// from the browser check (e2e/smoke.mjs, through JS interop) and from a native console host, so that WebAssembly
    /// and native results can be compared line by line (e2e/cases.json).
    /// </summary>
    public static class Probe
    {
        static readonly JsonSerializerOptions options = new JsonSerializerOptions {
            IncludeFields = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() }
        };

        public static string RunCase(string demo, string json)
        {
            DemoResult r;
            try {
                switch (demo) {
                    case "boolean": r = BooleanDemo.Compute(JsonSerializer.Deserialize<BooleanParams>(json, options)); break;
                    case "repair": r = RepairDemo.Compute(JsonSerializer.Deserialize<RepairParams>(json, options)); break;
                    case "remesh": r = RemeshDemo.Compute(JsonSerializer.Deserialize<RemeshParams>(json, options)); break;
                    default: return "unknown demo " + demo;
                }
            } catch (Exception e) {
                return "EXCEPTION " + e.GetType().Name + ": " + e.Message;
            }
            var sb = new StringBuilder();
            sb.Append(r.HeadlineState).Append(" | ").Append(r.Headline).Append(" | input=").Append(r.InputHash).Append(" | output=").Append(r.OutputHash);
            foreach (StatusGroup g in r.Groups)
                foreach (StatusRow row in g.Rows)
                    if (row.State == RowState.Bad || row.State == RowState.Warn || row.Name.StartsWith("V(") || row.Name == "ResultPath"
                        || row.Name == "triangles / vertices" || row.Name == "volume / area" || row.Name == "Status")
                        sb.Append(" | ").Append(g.Title.Split(' ')[0]).Append('.').Append(row.Name).Append('=').Append(row.Value);
            return sb.ToString();
        }
    }
}
