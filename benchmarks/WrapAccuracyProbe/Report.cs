// Markdown tables from the JSONL files:  report a|b <file.jsonl>...
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

static class Report
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    static double D(Dictionary<string, JsonElement> r, string k, double def = double.NaN)
    {
        if (!r.TryGetValue(k, out var e)) return def;
        if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        if (e.ValueKind == JsonValueKind.String) { var s = e.GetString(); if (s == "NaN") return double.NaN; if (s == "Infinity") return double.PositiveInfinity; }
        return def;
    }
    static string S(Dictionary<string, JsonElement> r, string k) { return r.TryGetValue(k, out var e) ? (e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()) : ""; }
    static string F(double v, string fmt = "G3") { return double.IsNaN(v) ? "-" : v.ToString(fmt, CI); }
    static string Pct(double v) { return double.IsNaN(v) ? "-" : (100 * v).ToString("0.##", CI) + "%"; }

    public static int Run(string[] args)
    {
        string kind = args[1];
        var rows = new List<Dictionary<string, JsonElement>>();
        for (int i = 2; i < args.Length; ++i)
            foreach (string line in File.ReadLines(args[i])) {
                using (var doc = JsonDocument.Parse(line)) {
                    var d = new Dictionary<string, JsonElement>();
                    foreach (var p in doc.RootElement.EnumerateObject()) d[p.Name] = p.Value.Clone();
                    rows.Add(d);
                }
            }
        var sb = new StringBuilder();
        if (kind == "a") A(rows, sb); else B(rows, sb);
        Console.Write(sb.ToString());
        return 0;
    }

    static string Cfg(Dictionary<string, JsonElement> r) { return "d/" + F(D(r, "alpha_den")) + " | " + F(D(r, "offset_rel")); }

    static void A(List<Dictionary<string, JsonElement>> rows, StringBuilder sb)
    {
        var groups = rows.GroupBy(r => S(r, "input") + "|" + D(r, "alpha_den").ToString("R", CI) + "|" + D(r, "offset_rel").ToString("R", CI)).ToList();
        sb.AppendLine("Accuracy: two-sided Hausdorff upper bound max(h(result, input), h(input, result)) in units of the diagonal; `*` = not converged (the upper bound is not within the tolerance).");
        sb.AppendLine();
        sb.AppendLine("| input | alpha | offset | wrap tris | wrap s | wrap H | naive H | safe H | wrap vol err | naive vol err | safe vol err |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in groups) {
            var w = g.FirstOrDefault(r => S(r, "variant") == "wrap"); var n = g.FirstOrDefault(r => S(r, "variant") == "naive"); var s = g.FirstOrDefault(r => S(r, "variant") == "safe");
            if (w == null) continue;
            if (n == null) { sb.AppendLine("| " + S(w, "input") + " | " + Cfg(w) + " | - | " + F(D(w, "ms") / 1000) + " | " + S(w, "status") + " |  |  |  |  |  |"); continue; }
            Func<Dictionary<string, JsonElement>, string> H = r => {
                double d = Math.Max(D(r, "h_res_in"), D(r, "h_in_res")) / D(r, "diag");
                bool conv = (!r.ContainsKey("h_res_in_conv") || r["h_res_in_conv"].GetBoolean()) && (!r.ContainsKey("h_in_res_conv") || r["h_in_res_conv"].GetBoolean());
                return F(d) + (conv ? "" : "*");
            };
            sb.AppendLine("| " + S(w, "input") + " | " + Cfg(w) + " | " + F(D(w, "tris"), "0") + " | " + F(D(w, "ms") / 1000) + " | " + H(w) + " | " + H(n) + " | " + H(s) + " | " +
                F(D(w, "vol_err")) + " | " + F(D(n, "vol_err")) + " | " + F(D(s, "vol_err")) + " |");
        }
        sb.AppendLine();
        if (rows.Any(r => r.ContainsKey("res_in_p50"))) {
            sb.AppendLine("Typical error: p50 / p99 of the sampled distances (area-stratified, 20000 samples each way, the larger of the two directions), in units of the diagonal.");
            sb.AppendLine();
            sb.AppendLine("| input | alpha | offset | wrap p50 | wrap p99 | naive p50 | naive p99 | safe p50 | safe p99 |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (var g in groups) {
                var w = g.FirstOrDefault(r => S(r, "variant") == "wrap"); var n = g.FirstOrDefault(r => S(r, "variant") == "naive"); var s = g.FirstOrDefault(r => S(r, "variant") == "safe");
                if (w == null || n == null || s == null || !w.ContainsKey("res_in_p50")) continue;
                Func<Dictionary<string, JsonElement>, string, string> P = (r, k) => F(Math.Max(D(r, "res_in_" + k), D(r, "in_res_" + k)) / D(r, "diag"));
                sb.AppendLine("| " + S(w, "input") + " | " + Cfg(w) + " | " + P(w, "p50") + " | " + P(w, "p99") + " | " + P(n, "p50") + " | " + P(n, "p99") + " | " + P(s, "p50") + " | " + P(s, "p99") + " |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("Validity and cost of the snap back (naive = every vertex to its nearest input point at once; safe = with exact ring checks; verdict = `MeshRobustRepair.NotCleanReason`; self-int = independent `MeshRepairMetrics` count).");
        sb.AppendLine();
        sb.AppendLine("| input | alpha | offset | naive verdict | naive flipped / collapsed tris | naive bad area | safe verdict | safe self-int | safe moved full / partial / none | safe rejections flip / degenerate / self | safe snap s |");
        sb.AppendLine("|---|---|---|---|---:|---:|---|---:|---|---|---:|");
        foreach (var g in groups) {
            var w = g.FirstOrDefault(r => S(r, "variant") == "wrap"); var n = g.FirstOrDefault(r => S(r, "variant") == "naive"); var s = g.FirstOrDefault(r => S(r, "variant") == "safe");
            if (w == null || n == null || s == null) continue;
            sb.AppendLine("| " + S(w, "input") + " | " + Cfg(w) + " | " + S(n, "verdict") + " | " + F(D(n, "flipped"), "0") + " / " + F(D(n, "collapsed"), "0") + " | " + Pct(D(n, "bad_area_share")) + " | " +
                S(s, "verdict") + " | " + F(D(s, "selfint"), "0") + " | " + Pct(D(s, "moved_full")) + " / " + Pct(D(s, "moved_partial")) + " / " + Pct(D(s, "moved_none")) + " | " +
                F(D(s, "rej_flip"), "0") + " / " + F(D(s, "rej_degenerate"), "0") + " / " + F(D(s, "rej_self"), "0") + " | " + F(D(s, "snap_ms") / 1000) + " |");
        }
    }

    static void B(List<Dictionary<string, JsonElement>> rows, StringBuilder sb)
    {
        sb.AppendLine("Added share = area share of the result farther than 4 offsets from the input; lost share = area share of the input farther than 4 offsets from the result; patches = connected far parts of the result, matched = within 2 alpha + 4 offsets of an input boundary vertex; patch/hole = far area over the removed area (the 4 variants use the same tau).");
        sb.AppendLine();
        sb.AppendLine("| input | alpha | offset | variant | verdict | vol / solid | added | lost | patches (matched) | unmatched patch area | patch / hole area | deep volume | s |");
        sb.AppendLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows.OrderBy(r => S(r, "input")).ThenByDescending(r => D(r, "alpha_den")).ThenByDescending(r => D(r, "offset_rel"))) {
            string v = S(r, "variant");
            sb.AppendLine("| " + S(r, "input") + " | " + Cfg(r) + " | " + v + (r.ContainsKey("result_path") ? " (" + S(r, "result_path") + ")" : "") + " | " + (r.ContainsKey("verdict") ? S(r, "verdict") : S(r, "status")) + " | " + F(D(r, "vol_ratio")) + " | " +
                Pct(D(r, "added_share")) + " | " + Pct(D(r, "lost_share")) + " | " + F(D(r, "patches"), "0") + " (" + F(D(r, "patches_matched"), "0") + ") | " + (D(r, "patch_area") > 0 ? Pct(D(r, "patch_area_unmatched") / D(r, "patch_area")) : "-") + " | " + F(D(r, "patch_over_hole_area")) + " | " + Pct(D(r, "deep_volume_share")) + " | " + F(D(r, "ms") / 1000) + " |");
        }
    }
}
