using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

// Option D of docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md: MeshRobustRepair takes the alpha wrap where the
// solidify's contour intersects itself. For one input: run the repair as it is; if it stopped the solidify on
// self-intersections (MeshRobustRepair.Solidify.StoppedOnSelfIntersections, the raw contour in Solidify.Result), take that
// contour and try (a) the existing contact separation (MeshRobustRepair.SeparateTouchingVertices) and (b) the untangling
// prototype (signed), each judged by NotCleanReason (exact). Then finish the solidify (its cleanup and reduction,
// MeshWindingNumberSolidify.FinishPostProcess) on the raw, the separated and the untangled contour, and judge again: a clean
// finished contour is the solidify result the repair would go on with instead of the wrap (its checks after the solidify, eg for
// lost parts, are not run here). No library change: the contour and the finish go through public members. One tab-separated
// row per input. Caveat: Apply() works in a frame scaled by a power of two and maps its outputs back; the finish here runs on
// the mapped-back contour with the solidify's own scale, the same computation up to a power-of-two scale, not bit for bit.
// With clean parts passed through, the contour covers the other parts only, while the diagonal is the whole input's.
static class Contours
{
    static readonly string[] Columns = { "input", "triangles", "apply", "path", "passed_through_triangles", "repair_ms", "contour", "contour_triangles", "contour_defects", "pairs", "cell_per_diag",
        "separate_moved", "separate_reason", "untangle_reason", "untangle_no_self_intersections", "untangle_rounds", "untangle_pairs_left", "untangle_move_per_diag", "untangle_move_per_cell", "untangle_ms",
        "finish_raw", "finish_separated", "finish_untangled", "finish_untangled_triangles", "h_wrap_to_input", "h_finished_untangled_to_input", "h_not_converged" };

    public static int File(string path, string outFile)
    {
        var builder = new DMesh3Builder();
        IOReadResult rd = StandardMeshReader.ReadFile(path, new ReadOptions(), builder);
        var row = new Dictionary<string, string> { ["input"] = Path.GetFileName(path) };
        if (rd.code != IOCode.Ok || builder.Meshes.Count == 0)
            row["apply"] = "load failed";
        else {
            DMesh3 mesh = builder.Meshes[0];
            for (int i = 1; i < builder.Meshes.Count; ++i)
                MeshEditor.Append(mesh, builder.Meshes[i]);
            Measure(mesh, null, row);
        }
        Emit(row, outFile);
        return 0;
    }

    // the thin procedural inputs of the solidify probe (Program.ThinInputs) through the repair at coarse resolutions, the repair stage
    // and the exact resolve switched off so that the repair reaches its solidify, as it does for the open real meshes: the rows
    // of the runs whose solidify the repair stopped on self-intersections
    public static int Probe()
    {
        var inputs = Program.ThinInputs();
        Func<DMesh3, DMesh3> noTop = m => Corpus.Without(m, (t, c) => c.z > m.CachedBounds.Max.z - 1e-9);
        Func<double, DMesh3> plate = t => Corpus.FineBox(Vector3d.Zero, new Vector3d(1, 1, 0.5 * t), 9);
        // (seeded per call: the same name is the same mesh in every run and frame)
        Func<int, double, DMesh3> shards = (n, t) => { var rnd = new Random(7 + n); return Corpus.Combine(Enumerable.Range(0, n).Select(i => {
            Vector3d a = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized, b = a.Cross(new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble() + 0.1)).Normalized, c = a.Cross(b);
            Vector3d o = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
            return Program.Map(Corpus.Box(-0.4, -0.3, -0.5 * t, 0.4, 0.3, 0.5 * t), p => o + p.x * a + p.y * b + p.z * c);
        }).ToArray()); };
        inputs.Add(("open box with a plate 0.03 through its wall", () => Corpus.Combine(noTop(Corpus.FineBox(Vector3d.Zero, Vector3d.One, 9)), Program.Map(plate(0.03), p => new Vector3d(p.x + 0.9, p.y * 0.5, p.z + 0.1)))));
        inputs.Add(("two open boxes through each other", () => Corpus.Combine(noTop(Corpus.FineBox(Vector3d.Zero, Vector3d.One, 9)), noTop(Corpus.FineBox(new Vector3d(0.8, 0.7, 0.3), Vector3d.One, 9)))));
        inputs.Add(("sphere with a hole and a fin 0.02", () => Corpus.Combine(Corpus.Without(Corpus.Sphere(Vector3d.Zero, 1, 12), (t, c) => c.z > 0.9), Corpus.Box(-0.01, -0.3, 0.5, 0.01, 0.3, 1.6))));
        inputs.Add(("12 thin shards 0.02 (closed)", () => shards(12, 0.02)));
        inputs.Add(("12 thin shards 0.02, one triangle missing in each", () => { DMesh3 m = shards(12, 0.02); return Corpus.Without(m, (t, c) => t % 12 == 0); }));
        inputs.Add(("30 thin shards 0.01 (closed)", () => shards(30, 0.01)));
        inputs.Add(("comb of 5 fins as a soup with a triangle missing", () => Corpus.Without(Corpus.Soup(Corpus.Combine(Enumerable.Range(0, 5).Select(i => Corpus.Box(0, 0.06 * i, 0, 1, 0.06 * i + 0.02, 1)).ToArray())), (t, c) => t == 3)));
        int found = 0, runs = 0;
        foreach (var inp in inputs)
            foreach (bool rot in new[] { false, true })
                foreach (int res in new[] { 24, 48, 96 }) {
                    DMesh3 g = rot ? Program.Map(inp.make(), Corpus.Rotate) : inp.make();
                    runs++;
                    // (the repair's solidify alone first: only a contour it stops on is worth the full row)
                    var probe = new MeshWindingNumberSolidify(new DMesh3(g, false, MeshComponents.None)) { GridResolution = res, Adaptive = true, StopIfSelfIntersecting = true };
                    if (!probe.Compute() || !probe.StoppedOnSelfIntersections) continue;
                    var row = new Dictionary<string, string> { ["input"] = inp.name + (rot ? ", rot" : ", id") + ", resolution " + res };
                    Measure(g, rr => { rr.ExactResolve = false; rr.PassThroughCleanParts = false; rr.SolidifyResolution = res; rr.RepairF = m => m; }, row);
                    if (row["contour"] != "self-intersecting") continue;    // (the repair did not stop where the solidify alone did)
                    found++;
                    Emit(row, null);
                    // (to try the file mode by hand: UNTANGLE_PROBE_EXPORT=<dir> writes these inputs as OBJ files; never committed)
                    string export = Environment.GetEnvironmentVariable("UNTANGLE_PROBE_EXPORT");
                    if (!string.IsNullOrEmpty(export))
                        StandardMeshWriter.WriteMesh(Path.Combine(export, "probe" + found + ".obj"), g, WriteOptions.Defaults);
                }
        Console.WriteLine("# contours the repair stops its solidify on (self-intersecting): " + found + " of " + runs + " runs");
        return 0;
    }

    static bool headerPrinted;

    static void Emit(Dictionary<string, string> row, string outFile)
    {
        string header = string.Join("\t", Columns), line = string.Join("\t", Columns.Select(c => row.TryGetValue(c, out string v) ? v : "-"));
        if (outFile == null) { if (!headerPrinted) Console.WriteLine(header); headerPrinted = true; Console.WriteLine(line); return; }
        if (!System.IO.File.Exists(outFile)) System.IO.File.WriteAllText(outFile, header + "\n");
        System.IO.File.AppendAllText(outFile, line + "\n");
        Console.WriteLine(line);
    }

    static string E(double v) { return double.IsNaN(v) ? "-" : v.ToString("0.00e+0", CultureInfo.InvariantCulture); }
    static string Reason(DMesh3 m) { string r = m == null || m.TriangleCount == 0 ? "no result" : MeshRobustRepair.NotCleanReason(m); return r == "" ? "clean" : r; }

    // the solidify's skipped cleanup and reduction on a contour (a copy): what the repair returns if it does not wrap
    static DMesh3 Finish(MeshWindingNumberSolidify solidify, DMesh3 contour)
    {
        solidify.Result = new DMesh3(contour, false, MeshComponents.None);
        solidify.StoppedOnSelfIntersections = true;
        solidify.FinishPostProcess();
        return solidify.Result;
    }

    static void Measure(DMesh3 input, Action<MeshRobustRepair> configure, Dictionary<string, string> row)
    {
        row["triangles"] = input.TriangleCount.ToString();
        double diag = MeshRepairMetrics.TriangleBounds(input).DiagonalLength;
        var rr = new MeshRobustRepair(new DMesh3(input, false, MeshComponents.None));
        if (configure != null) configure(rr);
        var sw = Stopwatch.StartNew();
        bool ok = rr.Apply();
        row["repair_ms"] = sw.ElapsedMilliseconds.ToString();
        row["apply"] = ok.ToString(); row["path"] = rr.ResultPath.ToString(); row["passed_through_triangles"] = rr.PassedThroughTriangles.ToString();
        MeshWindingNumberSolidify so = rr.Solidify;
        if (so == null || !so.StoppedOnSelfIntersections || so.Result == null) {
            // (a wrap that failed or was refused: the repair finished the stopped solidify itself, and the raw contour is gone)
            row["contour"] = so == null ? "no solidify"
                : rr.PhaseMilliseconds.ContainsKey("solidify.finish") ? "stopped on self-intersections, but the wrap was not taken (" + rr.WrapReason + "): finished by the repair, no raw contour"
                : "not stopped on self-intersections (" + rr.SolidifyReason + ")";
            return;
        }
        var contour = new DMesh3(so.Result, false, MeshComponents.None);
        row["contour"] = "self-intersecting"; row["contour_triangles"] = contour.TriangleCount.ToString();
        row["contour_defects"] = MeshRobustRepair.DescribeDefects(MeshRobustRepair.FindDefects(contour));
        int pairs = MeshRepairMetrics.SelfIntersectionsAtMost(contour, 100000);
        row["pairs"] = pairs > 100000 ? "> 100000" : pairs.ToString();
        row["cell_per_diag"] = E(so.UsedCellSize / diag);

        var separated = new DMesh3(contour, false, MeshComponents.None);
        row["separate_moved"] = MeshRobustRepair.SeparateTouchingVertices(separated, rr.SeparateFraction).ToString();
        row["separate_reason"] = Reason(separated);

        // (the untangler judges an open contour by its self-intersections only: the row's verdict is the full NotCleanReason, as for the separation)
        UntangleResult u = Untangler.Run(contour, new UntangleOptions { Signed = true, MaxRounds = 60, StallRounds = 15, MaxMilliseconds = 150000 });
        bool untangled = Reason(u.Mesh) == "clean";
        row["untangle_reason"] = Reason(u.Mesh); row["untangle_no_self_intersections"] = (u.PairsEnd == 0).ToString(); row["untangle_rounds"] = u.Rounds.ToString(); row["untangle_pairs_left"] = u.PairsEnd.ToString();
        row["untangle_move_per_diag"] = E(u.MaxMove / diag); row["untangle_move_per_cell"] = E(u.MaxMove / so.UsedCellSize); row["untangle_ms"] = u.Milliseconds.ToString();

        row["finish_raw"] = Reason(Finish(so, contour));
        if (row["separate_reason"] == "clean") row["finish_separated"] = Reason(Finish(so, separated));
        int before = Program.NotConverged;
        if (rr.Mesh != null && rr.UsedAlphaWrap) row["h_wrap_to_input"] = E(Program.Hausdorff(rr.Mesh, input, false, diag) / diag);
        if (untangled) {
            DMesh3 fin = Finish(so, u.Mesh);
            row["finish_untangled"] = Reason(fin);
            if (fin != null) row["finish_untangled_triangles"] = fin.TriangleCount.ToString();
            if (row["finish_untangled"] == "clean") row["h_finished_untangled_to_input"] = E(Program.Hausdorff(fin, input, false, diag) / diag);
        }
        row["h_not_converged"] = (Program.NotConverged - before).ToString();
    }
}
