// BENCH_REPAIR=candidates: measures the candidates that MeshRobustRepair rejects before its final result (measurement only,
// no library change). The real Apply() gives the final mesh and its stage. The rejected candidates are rebuilt here from the
// library's building blocks (a benchmark-only copy of the decision chain of MeshRobustRepair.apply_core):
//   R   the MeshAutoRepair result (PostRepairCleanup)               rejected: NotCleanReason, volume IoU, (CheckLostComponents) lost parts
//   E1  exact resolve of R (resolve_exactly by reflection)           rejected: unclean (no mesh then), volume IoU, lost parts
//   E1o exact resolve of R, oriented by the input                   (as E1)
//   E2  exact resolve of the input                                  (as E1)
//   S   the solidify result of the real run (rejected for SolidifyReason when the wrap is the final result)
// The resolves run on a second instance without volume checks (MinVolumeIoU = 0), so that a resolve the library rejects for its
// volume or a lost part is kept; the volume and part checks are then applied here with the library's own functions.
// Not reproduced: the working frame (power of two scale), the pass-through of clean parts and the far part tolerances of
// apply_core; "sim_stage" vs "real_stage" in the output shows where the copy disagrees with the real run.
// For every rejected candidate with a mesh: validity, volume IoU, certified Hausdorff, Chamfer against the input, the spatial
// finding of the rejection (defect points or the points where the candidate's solid and the input's differ) and the
// deviation near / far from it for the candidate and the final mesh; for the best rejected candidate of a mesh that ends in a
// fallback: the footprint of a patch (PatchFootprint).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static class CandidateBench
{
    class Cand
    {
        public string Kind;
        public DMesh3 Mesh;                 // null: the stage failed without a mesh
        public string Reason = "";          // "" accepted (as the library would), else why rejected
        public string Class = "";           // unclean | volume | parts | nomesh | accepted
        public double Iou = double.NaN;
        public bool Attempted = true;
    }

    public static void Run(DMesh3 mesh, Dictionary<string, object> r, bool absTol, double tolFactor, double absRepairTol, double absMinEdge)
    {
        var total = Stopwatch.StartNew();
        bool lost = Environment.GetEnvironmentVariable("BENCH_LOST_COMPONENTS") == "1";
        r["cand_lost_components"] = lost;
        DMesh3 orig = new DMesh3(mesh);
        double diag = MeshRepairMetrics.TriangleBounds(orig).DiagonalLength;
        r["cand_diag"] = diag;

        // ---- the real run
        var sw = Stopwatch.StartNew();
        var real = Program.MakeRobust(new DMesh3(orig), absTol, tolFactor, absRepairTol, absMinEdge, new Dictionary<string, object>());
        bool ok = real.Apply();
        DMesh3 final = real.Mesh;
        r["real_ms"] = sw.ElapsedMilliseconds;
        r["real_ok"] = ok;
        string realStage = real.UsedAlphaWrap ? "wrap" : real.UsedSolidify ? "solidify" : real.UsedExactResolve ? "exact" : real.PassedThroughTriangles > 0 && real.FallbackReason == "" ? "passthrough" : real.FallbackReason == "" ? "repair" : "none";
        r["real_stage"] = realStage;
        r["real_result_reason"] = real.ResultReason;
        r["real_fallback_reason"] = real.FallbackReason;
        r["real_exact_reason"] = real.ExactResolveReason;
        r["real_solidify_reason"] = real.SolidifyReason;
        r["real_wrap_reason"] = real.WrapReason;
        r["real_used_input"] = real.UsedInput;
        r["real_passed_through"] = real.PassedThroughTriangles;
        r["real_far_parts"] = real.FarPartsHandled;
        r["real_frame_scale"] = real.LastFrameScale;

        // ---- the candidates
        var cands = new List<Cand>();
        sw.Restart();
        var rep = new MeshAutoRepair(new DMesh3(orig)) { PostRepairCleanup = true };
        if (absTol) { rep.ToleranceMode = MeshAutoRepair.ToleranceModes.Absolute; rep.RepairTolerance = absRepairTol * tolFactor; rep.MinEdgeLengthTol = absMinEdge * tolFactor; }
        rep.Apply();
        DMesh3 R = rep.Mesh;
        r["cand_repair_ms"] = sw.ElapsedMilliseconds;

        var inputSolid = new MeshRepairMetrics.SolidReference(orig);
        double minIou = 0.99;
        Func<DMesh3, string> volume = (m) => {
            var rs = MeshRepairMetrics.SolidReference.Unoriented(m);
            double iou;
            if (MeshRepairMetrics.VolumeIoUAtLeast(inputSolid, rs, minIou, out iou) == false) { lastIou = iou; return "volume"; }
            lastIou = iou;
            if (lost && MeshRepairMetrics.LostComponents(inputSolid, rs.Inside) > 0) return "parts";
            return "";
        };

        // R
        var cR = new Cand { Kind = "R", Mesh = R };
        cR.Reason = MeshRobustRepair.NotCleanReason(R, true);
        cR.Class = cR.Reason == "" ? "" : "unclean";
        bool rEmpty = R.TriangleCount == 0;
        string rVol = "";
        if (!rEmpty) {
            rVol = volume(R);
            cR.Iou = lastIou;
            if (cR.Reason == "" && rVol != "") { cR.Reason = rVol == "volume" ? "volume differs" : "parts lost"; cR.Class = rVol; }
        }
        if (cR.Reason == "") cR.Class = "accepted";
        cands.Add(cR);
        bool solidifyFromInput = rEmpty || rVol != "";   // the library's solidifyFrom == input
        bool cleanButDiffers = cR.Class == "volume" || cR.Class == "parts";

        // E1, E1o, E2
        var resolve = typeof(MeshRobustRepair).GetMethod("resolve_exactly", BindingFlags.NonPublic | BindingFlags.Instance);
        Func<string, DMesh3, bool, Cand> exact = (kind, from, byInput) => {
            var h = Program.MakeRobust(new DMesh3(orig), absTol, tolFactor, absRepairTol, absMinEdge, new Dictionary<string, object>());
            h.MinVolumeIoU = 0; h.NoWorkingFrame = true; h.PassThroughCleanParts = false;
            var c = new Cand { Kind = kind };
            var t = Stopwatch.StartNew();
            try {
                c.Mesh = (DMesh3)resolve.Invoke(h, new object[] { new DMesh3(from), orig, byInput, false });
            } catch (Exception e) { c.Reason = "exception: " + (e.InnerException ?? e).GetType().Name; }
            r["cand_" + kind + "_ms"] = t.ElapsedMilliseconds;
            if (c.Mesh == null) {
                if (c.Reason == "") c.Reason = h.ExactResolveReason == "" ? "same orientation" : h.ExactResolveReason;
                c.Class = "nomesh";
            } else {
                string v = volume(c.Mesh);
                c.Iou = lastIou;
                c.Reason = v == "" ? "" : v == "volume" ? "volume differs" : "parts lost";
                c.Class = v == "" ? "accepted" : v;
            }
            return c;
        };
        if (!rEmpty && cR.Class != "accepted" && !cleanButDiffers) {
            var c = exact("E1", R, false);
            cands.Add(c);
            if (c.Class == "volume" || c.Class == "parts" || c.Class == "nomesh") {
                var c2 = exact("E1o", R, true);
                if (c2.Reason != "same orientation") cands.Add(c2);
            }
        } else if (!rEmpty && cleanButDiffers) {
            var c2 = exact("E1o", R, true);
            if (c2.Reason != "same orientation") cands.Add(c2);
        }
        if (cands.All(c => c.Class != "accepted") && solidifyFromInput && orig.TriangleCount > 0)
            cands.Add(exact("E2", orig, false));

        // simulated stage
        string simStage = cands.FirstOrDefault(c => c.Class == "accepted")?.Kind;
        simStage = simStage == null ? null : simStage == "R" ? "repair" : "exact";
        if (simStage == null) {
            // the solidify / wrap fallbacks of the real run decide
            simStage = real.UsedAlphaWrap ? "wrap" : real.UsedSolidify ? "solidify" : "none";
        }
        r["sim_stage"] = simStage;
        r["sim_matches_real"] = simStage == realStage || (simStage == "repair" && realStage == "passthrough");

        // S: the solidify of the real run, rejected when the wrap is the final result
        if (real.UsedAlphaWrap && real.Solidify?.Result != null && real.Solidify.Result.TriangleCount > 0) {
            cands.Add(new Cand { Kind = "S", Mesh = real.Solidify.Result, Reason = real.SolidifyReason, Class = "unclean" });
        }

        // ---- measures of the final mesh and of every rejected candidate that has a mesh and comes before it
        var rejected = cands.Where(c => c.Class != "accepted").ToList();
        r["cand_rejected"] = rejected.Select(c => c.Kind + ":" + c.Class + ":" + c.Reason).ToList();
        bool laterFinal = realStage == "exact" || realStage == "solidify" || realStage == "wrap";
        r["cand_later_final"] = laterFinal;

        if (final == null || final.TriangleCount == 0 || realStage == "none") {
            r["cand_note"] = "no final mesh";
            r["cand_total_ms"] = total.ElapsedMilliseconds;
            return;
        }
        var treeIn = new DMeshAABBTree3(orig, true);
        Vector3d[] inSamples = Sample(orig, 10000, 11);
        var treeFinal = new DMeshAABBTree3(final, true);
        Vector3d[] finalSamples = Sample(final, 10000, 12);
        var fm = new Dictionary<string, object>();
        Measure(orig, final, diag, fm);
        r["final_measures"] = fm;
        var outList = new List<object>();
        foreach (var c in rejected) {
            if (c.Mesh == null || c.Mesh.TriangleCount == 0) {
                outList.Add(new Dictionary<string, object> { ["kind"] = c.Kind, ["class"] = c.Class, ["reason"] = c.Reason, ["mesh"] = false });
                continue;
            }
            var d = new Dictionary<string, object> { ["kind"] = c.Kind, ["class"] = c.Class, ["reason"] = c.Reason, ["mesh"] = true };
            var cm = new Dictionary<string, object>();
            Measure(orig, c.Mesh, diag, cm);
            d["measures"] = cm;
            // the rejection's finding
            var defects = FindDefects(c.Mesh, diag, out var dstats);
            d["defects"] = dstats;
            List<Vector3d> findings;
            string findingKind;
            if (c.Class == "unclean" && defects.Tris.Count + defects.Points.Count > 0) {
                findings = defects.Points; findingKind = "defects";
            } else {
                findings = Mismatch(inputSolid.Inside, MeshRepairMetrics.SolidInside(c.Mesh), orig, c.Mesh, 30000);
                findingKind = "solid_mismatch";
            }
            d["finding_kind"] = findingKind;
            d["finding_points"] = findings.Count;
            if (findings.Count > 0) {
                double rad = 0.02 * diag;
                var near = new NearSet(findings, rad);
                var treeC = new DMeshAABBTree3(c.Mesh, true);
                Vector3d[] cSamples = Sample(c.Mesh, 10000, 13);
                d["local"] = new Dictionary<string, object> {
                    ["radius_rel"] = 0.02,
                    ["cand"] = Local(inSamples, cSamples, treeC, treeIn, near, diag),
                    ["final"] = Local(inSamples, finalSamples, treeFinal, treeIn, near, diag)
                };
                // does the final mesh still have a defect near the finding? (it is clean unless it is a scale-only result)
                d["final_defect_near"] = near.AnyNear(FindDefects(final, diag, out _).Points);
            }
            outList.Add(d);
        }
        r["cand_list"] = outList;

        // ---- (3) patch footprint of the best rejected candidate of a mesh that ends in a fallback
        if (realStage == "solidify" || realStage == "wrap") {
            // every rejected candidate with a mesh (the best one, with the fewest defect triangles, is chosen by the analysis)
            var patches = new List<object>();
            foreach (var c in rejected.Where(x => x.Kind != "S" && x.Mesh != null && x.Mesh.TriangleCount > 0)) {
                var ds = FindDefects(c.Mesh, diag, out var st);
                var pf = new Dictionary<string, object> { ["kind"] = c.Kind, ["class"] = c.Class, ["defects"] = st };
                if (st.ContainsKey("capped")) pf["note"] = "too many self-intersections (capped)";
                else if (ds.Tris.Count == 0) pf["note"] = "no defects (rejected by volume or lost parts)";
                else PatchFootprint(c.Mesh, ds.Tris, diag, pf);
                patches.Add(pf);
            }
            r["patches"] = patches;
        }
        r["cand_total_ms"] = total.ElapsedMilliseconds;
    }

    static double lastIou;

    // ------------------------------------------------------------------ measures
    static void Measure(DMesh3 input, DMesh3 m, double diag, Dictionary<string, object> d)
    {
        d["tris"] = m.TriangleCount;
        d["not_clean"] = MeshRobustRepair.NotCleanReasons(m, m.TriangleCount <= 300000);
        d["si"] = m.TriangleCount <= 300000 ? MeshRepairMetrics.SelfIntersectionsAtMost(m, 100000) : -1;
        d["boundary_edges"] = MeshIterators.BoundaryEdges(m).Count();
        d["bowties"] = m.VertexIndices().Count(v => m.IsBowtieVertex(v));
        double missing, extra;
        d["iou"] = MeshRepairMetrics.VolumeIoU(input, m, out missing, out extra, 50000);
        foreach (bool fwd in new[] { true, false }) {
            var h = new MeshHausdorffDistance(fwd ? m : input, fwd ? input : m) { Symmetric = false, RelativeTolerance = 5e-3, MaxSubdivisions = 500000 };
            if (h.Compute()) {
                d[fwd ? "hd_fwd_hi" : "hd_back_hi"] = h.UpperBound / diag;
                d[fwd ? "hd_fwd_lo" : "hd_back_lo"] = h.LowerBound / diag;
            }
        }
        // Chamfer (mean distance of the surface samples, both ways, relative to the diagonal)
        var f = new MeshFidelityMetrics(input, m) { SurfaceSamples = 20000, VolumeSamples = 1000, Curvature = false, OuterSurface = false, EdgeSamples = 100, RelativeThresholds = new[] { 0.01 } };
        if (f.Compute()) d["chamfer"] = f.Chamfer / diag;
    }

    // ------------------------------------------------------------------ sampling and local analysis
    static Vector3d[] Sample(DMesh3 m, int n, int seed)
    {
        var tris = m.TriangleIndices().ToArray();
        var cum = new double[tris.Length];
        double acc = 0;
        for (int i = 0; i < tris.Length; ++i) { acc += m.GetTriArea(tris[i]); cum[i] = acc; }
        var rnd = new Random(seed);
        var res = new Vector3d[acc > 0 ? n : 0];
        for (int k = 0; k < res.Length; ++k) {
            double x = rnd.NextDouble() * acc;
            int i = Array.BinarySearch(cum, x);
            if (i < 0) i = ~i;
            i = Math.Min(i, tris.Length - 1);
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(tris[i], ref a, ref b, ref c);
            double u = rnd.NextDouble(), v = rnd.NextDouble();
            if (u + v > 1) { u = 1 - u; v = 1 - v; }
            res[k] = a + u * (b - a) + v * (c - a);
        }
        return res;
    }

    class NearSet
    {
        readonly Dictionary<(long, long, long), List<Vector3d>> cells = new Dictionary<(long, long, long), List<Vector3d>>();
        readonly double r;
        public NearSet(List<Vector3d> pts, double radius)
        {
            r = radius;
            foreach (var p in pts) {
                var k = Key(p);
                if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<Vector3d>();
                l.Add(p);
            }
        }
        (long, long, long) Key(Vector3d p) => ((long)Math.Floor(p.x / r), (long)Math.Floor(p.y / r), (long)Math.Floor(p.z / r));
        public bool Near(Vector3d p)
        {
            var k = Key(p);
            for (long i = -1; i <= 1; ++i) for (long j = -1; j <= 1; ++j) for (long l = -1; l <= 1; ++l)
                if (cells.TryGetValue((k.Item1 + i, k.Item2 + j, k.Item3 + l), out var list))
                    foreach (var q in list) if (p.DistanceSquared(q) <= r * r) return true;
            return false;
        }
        public bool AnyNear(List<Vector3d> pts) => pts.Any(Near);
    }

    // max / mean distance, near and far from the finding, input samples to the mesh (in2m) and mesh samples to the input (m2in)
    static Dictionary<string, object> Local(Vector3d[] inSamples, Vector3d[] mSamples, DMeshAABBTree3 treeM, DMeshAABBTree3 treeIn, NearSet near, double diag)
    {
        var res = new Dictionary<string, object>();
        void one(string name, Vector3d[] pts, DMeshAABBTree3 tree, DMesh3 mesh)
        {
            double nMax = 0, nSum = 0, fMax = 0, fSum = 0; int nN = 0, nF = 0;
            foreach (var p in pts) {
                int t = tree.FindNearestTriangle(p, out double d2);
                double dist = Math.Sqrt(d2);
                if (near.Near(p)) { nMax = Math.Max(nMax, dist); nSum += dist; nN++; }
                else { fMax = Math.Max(fMax, dist); fSum += dist; nF++; }
            }
            res[name + "_near_n"] = nN; res[name + "_far_n"] = nF;
            res[name + "_near_max"] = nMax / diag; res[name + "_near_mean"] = nN > 0 ? nSum / nN / diag : double.NaN;
            res[name + "_far_max"] = fMax / diag; res[name + "_far_mean"] = nF > 0 ? fSum / nF / diag : double.NaN;
        }
        one("in2m", inSamples, treeM, null);
        one("m2in", mSamples, treeIn, null);
        return res;
    }

    // points where the solids differ (uniform samples in the common box)
    static List<Vector3d> Mismatch(Func<Vector3d, bool> a, Func<Vector3d, bool> b, DMesh3 ma, DMesh3 mb, int n)
    {
        var box = MeshRepairMetrics.TriangleBounds(ma);
        box.Contain(MeshRepairMetrics.TriangleBounds(mb));
        var rnd = new Random(5);
        var pts = new Vector3d[n];
        for (int i = 0; i < n; ++i)
            pts[i] = box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
        var diff = new bool[n];
        gParallel.ForEach(Interval1i.Range(n), (i) => { diff[i] = a(pts[i]) != b(pts[i]); });
        var res = new List<Vector3d>();
        for (int i = 0; i < n; ++i) if (diff[i]) res.Add(pts[i]);
        return res;
    }

    // ------------------------------------------------------------------ defects
    class DefectSet
    {
        public HashSet<int> Tris = new HashSet<int>();
        public List<Vector3d> Points = new List<Vector3d>();
    }

    const int MaxSiPairs = 200000;

    static DefectSet FindDefects(DMesh3 m, double diag, out Dictionary<string, object> stats)
    {
        var ds = new DefectSet();
        stats = new Dictionary<string, object>();
        int si = 0, bnd = 0, bow = 0, deg = 0;
        // open boundary
        foreach (int e in MeshIterators.BoundaryEdges(m)) {
            Index2i et = m.GetEdgeT(e);
            if (ds.Tris.Add(et.a)) bnd++;
            Index2i ev = m.GetEdgeV(e);
            ds.Points.Add(0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b)));
        }
        // bowtie vertices: all triangles around them
        foreach (int v in m.VertexIndices()) {
            if (!m.IsBowtieVertex(v)) continue;
            foreach (int t in m.VtxTrianglesItr(v)) if (ds.Tris.Add(t)) bow++;
            ds.Points.Add(m.GetVertex(v));
        }
        // degenerate triangles
        double minArea = 1e-12 * diag * diag;
        foreach (int t in m.TriangleIndices()) {
            if (m.GetTriArea(t) < minArea) {
                if (ds.Tris.Add(t)) deg++;
                ds.Points.Add(m.GetTriCentroid(t));
            }
        }
        // self-intersections
        if (m.TriangleCount <= 300000) {
            if (MeshRepairMetrics.SelfIntersectionsAtMost(m, MaxSiPairs) > MaxSiPairs) {
                stats["capped"] = true;
            } else {
                foreach (var p in MeshRepairMetrics.SelfIntersectionPairs(m)) {
                    if (ds.Tris.Add(p.a)) si++;
                    if (ds.Tris.Add(p.b)) si++;
                    ds.Points.Add(m.GetTriCentroid(p.a));
                    ds.Points.Add(m.GetTriCentroid(p.b));
                }
            }
        } else stats["si_skipped"] = true;
        stats["tris"] = m.TriangleCount;
        stats["defect_tris"] = ds.Tris.Count;
        stats["si_tris"] = si; stats["boundary_tris"] = bnd; stats["bowtie_tris"] = bow; stats["degenerate_tris"] = deg;
        // (the points of a thousand defects are enough for the local analysis)
        if (ds.Points.Count > 20000) {
            int step = ds.Points.Count / 20000 + 1;
            ds.Points = ds.Points.Where((p, i) => i % step == 0).ToList();
        }
        return ds;
    }

    // ------------------------------------------------------------------ patch footprint
    // The smallest region (by a greedy growth, not a proven minimum) of triangles that contains all defect triangles, and whose
    // boundary (the edges to triangles outside; the mesh's own open edges do not count) is one simple loop. Grown by edge adjacency:
    // the defect components are joined by shortest triangle paths (nearest first; above 300 components by rings of triangles), holes of
    // the region (complement components that are not the largest) are filled, pinched boundary vertices get their whole fan.
    // Also the disjoint variant: every defect cluster (vertex connected, with one ring) is closed on its own, patches that touch merge.
    static void PatchFootprint(DMesh3 m, HashSet<int> defect, double diag, Dictionary<string, object> pf)
    {
        int maxT = m.MaxTriangleID;
        var area = new double[maxT];
        double totalArea = 0;
        foreach (int t in m.TriangleIndices()) { area[t] = m.GetTriArea(t); totalArea += area[t]; }
        Func<IEnumerable<int>, double> areaOf = (ts) => ts.Sum(t => area[t]);
        pf["tris"] = m.TriangleCount;
        pf["defect_area_frac"] = areaOf(defect) / totalArea;

        // defect components by edge adjacency / vertex adjacency
        var comps = Components(m, defect, false);
        pf["defect_components_edge"] = comps.Count;
        var compsV = Components(m, defect, true);
        pf["defect_components_vertex"] = compsV.Count;

        foreach (int margin in new[] { 0, 1 }) {
            var seed = new HashSet<int>(defect);
            if (margin == 1) seed = Dilate(m, seed, true);
            var sw = Stopwatch.StartNew();
            var single = SinglePatch(m, seed, area);
            string tag = margin == 0 ? "single" : "single_m1";
            pf[tag + "_tris"] = single.Tris.Count;
            pf[tag + "_area_frac"] = areaOf(single.Tris) / totalArea;
            pf[tag + "_loops"] = single.Loops;
            pf[tag + "_loop_edges"] = single.LoopEdges;
            pf[tag + "_loop_len_rel"] = single.LoopLength / diag;
            pf[tag + "_ok"] = single.Ok;
            pf[tag + "_ms"] = sw.ElapsedMilliseconds;

            // disjoint patches
            var cl = Components(m, seed, true);
            var patches = cl.Select(c => Close(m, new HashSet<int>(c), area).Tris).ToList();
            // merge patches that overlap or touch (a shared vertex)
            bool merged = true;
            while (merged && patches.Count > 1) {
                merged = false;
                var vsets = patches.Select(p => VertexSet(m, p)).ToList();
                for (int i = 0; i < patches.Count && !merged; ++i)
                    for (int j = i + 1; j < patches.Count && !merged; ++j)
                        if (vsets[i].Overlaps(vsets[j])) {
                            var u = new HashSet<int>(patches[i]); u.UnionWith(patches[j]);
                            patches[i] = Close(m, u, area).Tris; patches.RemoveAt(j); merged = true;
                        }
            }
            double sum = patches.Sum(p => areaOf(p));
            pf[(margin == 0 ? "multi" : "multi_m1") + "_patches"] = patches.Count;
            pf[(margin == 0 ? "multi" : "multi_m1") + "_area_frac"] = sum / totalArea;
            pf[(margin == 0 ? "multi" : "multi_m1") + "_max_patch_frac"] = patches.Count > 0 ? patches.Max(p => areaOf(p)) / totalArea : 0;
        }
    }

    class PatchResult { public HashSet<int> Tris; public int Loops, LoopEdges; public double LoopLength; public bool Ok; }

    static HashSet<int> VertexSet(DMesh3 m, HashSet<int> tris)
    {
        var vs = new HashSet<int>();
        foreach (int t in tris) { Index3i tv = m.GetTriangle(t); vs.Add(tv.a); vs.Add(tv.b); vs.Add(tv.c); }
        return vs;
    }

    static List<List<int>> Components(DMesh3 m, HashSet<int> set, bool viaVertices)
    {
        var seen = new HashSet<int>();
        var res = new List<List<int>>();
        foreach (int s in set) {
            if (!seen.Add(s)) continue;
            var comp = new List<int> { s };
            var stack = new Stack<int>(); stack.Push(s);
            while (stack.Count > 0) {
                int t = stack.Pop();
                if (viaVertices) {
                    Index3i tv = m.GetTriangle(t);
                    for (int k = 0; k < 3; ++k)
                        foreach (int u in m.VtxTrianglesItr(tv[k]))
                            if (set.Contains(u) && seen.Add(u)) { comp.Add(u); stack.Push(u); }
                } else {
                    Index3i nb = m.GetTriNeighbourTris(t);
                    for (int k = 0; k < 3; ++k) {
                        int u = nb[k];
                        if (u >= 0 && set.Contains(u) && seen.Add(u)) { comp.Add(u); stack.Push(u); }
                    }
                }
            }
            res.Add(comp);
        }
        return res;
    }

    static HashSet<int> Dilate(DMesh3 m, HashSet<int> set, bool viaVertices)
    {
        var res = new HashSet<int>(set);
        foreach (int t in set) {
            if (viaVertices) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) foreach (int u in m.VtxTrianglesItr(tv[k])) res.Add(u);
            } else {
                Index3i nb = m.GetTriNeighbourTris(t);
                for (int k = 0; k < 3; ++k) if (nb[k] >= 0) res.Add(nb[k]);
            }
        }
        return res;
    }

    // the cut edges of a region (to triangles outside it) as directed vertex pairs of the region's triangles
    static List<Index2i> CutEdges(DMesh3 m, HashSet<int> region)
    {
        var cut = new List<Index2i>();
        foreach (int t in region) {
            Index3i nb = m.GetTriNeighbourTris(t), tv = m.GetTriangle(t);
            for (int k = 0; k < 3; ++k) {
                int u = nb[k];
                if (u >= 0 && !region.Contains(u))
                    cut.Add(new Index2i(tv[k], tv[(k + 1) % 3]));
            }
        }
        return cut;
    }

    // grows the region until its cut edges are one simple loop (or it is everything)
    static PatchResult Close(DMesh3 m, HashSet<int> region, double[] area)
    {
        region = new HashSet<int>(region);
        int all = m.TriangleCount;
        for (int iter = 0; iter < 400; ++iter) {
            if (region.Count >= all) return Finish(m, region, true);
            var cut = CutEdges(m, region);
            if (cut.Count == 0) return Finish(m, region, true);      // (no cut edge: the region is a whole component)
            // vertex degrees in the cut graph
            var deg = new Dictionary<int, int>();
            foreach (var e in cut) { deg[e.a] = deg.GetValueOrDefault(e.a) + 1; deg[e.b] = deg.GetValueOrDefault(e.b) + 1; }
            var pinched = deg.Where(kv => kv.Value > 2).Select(kv => kv.Key).ToList();
            if (pinched.Count > 0) {
                foreach (int v in pinched) foreach (int t in m.VtxTrianglesItr(v)) region.Add(t);
                continue;
            }
            // connected loops
            var adj = new Dictionary<int, List<int>>();
            foreach (var e in cut) {
                if (!adj.TryGetValue(e.a, out var la)) adj[e.a] = la = new List<int>();
                if (!adj.TryGetValue(e.b, out var lb)) adj[e.b] = lb = new List<int>();
                la.Add(e.b); lb.Add(e.a);
            }
            int loops = 0; var seenV = new HashSet<int>();
            foreach (int v in adj.Keys) {
                if (!seenV.Add(v)) continue;
                loops++;
                var st = new Stack<int>(); st.Push(v);
                while (st.Count > 0) { int x = st.Pop(); foreach (int y in adj[x]) if (seenV.Add(y)) st.Push(y); }
            }
            if (loops == 1) return Finish(m, region, true);
            // several loops: fill the complement components except the largest
            var comp = new Dictionary<int, int>(); var compArea = new List<double>(); var compTris = new List<List<int>>();
            foreach (int s in m.TriangleIndices()) {
                if (region.Contains(s) || comp.ContainsKey(s)) continue;
                int id = compArea.Count; compArea.Add(0); compTris.Add(new List<int>());
                var st = new Stack<int>(); st.Push(s); comp[s] = id;
                while (st.Count > 0) {
                    int t = st.Pop(); compArea[id] += area[t]; compTris[id].Add(t);
                    Index3i nb = m.GetTriNeighbourTris(t);
                    for (int k = 0; k < 3; ++k) { int u = nb[k]; if (u >= 0 && !region.Contains(u) && !comp.ContainsKey(u)) { comp[u] = id; st.Push(u); } }
                }
            }
            if (compArea.Count > 1) {
                int keep = compArea.IndexOf(compArea.Max());
                for (int i = 0; i < compTris.Count; ++i) if (i != keep) foreach (int t in compTris[i]) region.Add(t);
            } else {
                region = Dilate(m, region, false);       // (one complement over several loops: a handle; grow towards it)
            }
        }
        return Finish(m, region, false);
    }

    static PatchResult Finish(DMesh3 m, HashSet<int> region, bool ok)
    {
        var cut = CutEdges(m, region);
        double len = 0;
        foreach (var e in cut) len += m.GetVertex(e.a).Distance(m.GetVertex(e.b));
        var adj = new Dictionary<int, List<int>>();
        foreach (var e in cut) {
            if (!adj.TryGetValue(e.a, out var la)) adj[e.a] = la = new List<int>();
            if (!adj.TryGetValue(e.b, out var lb)) adj[e.b] = lb = new List<int>();
            la.Add(e.b); lb.Add(e.a);
        }
        int loops = 0; var seenV = new HashSet<int>();
        foreach (int v in adj.Keys) {
            if (!seenV.Add(v)) continue;
            loops++;
            var st = new Stack<int>(); st.Push(v);
            while (st.Count > 0) { int x = st.Pop(); foreach (int y in adj[x]) if (seenV.Add(y)) st.Push(y); }
        }
        return new PatchResult { Tris = region, Loops = loops, LoopEdges = cut.Count, LoopLength = len, Ok = ok && loops <= 1 && adj.Values.All(l => l.Count == 2) };
    }

    // all defects in one region: components joined by shortest triangle paths (nearest first), then closed
    static PatchResult SinglePatch(DMesh3 m, HashSet<int> seed, double[] area)
    {
        var comps = Components(m, seed, true);
        var region = new HashSet<int>(comps.OrderByDescending(c => c.Count).First());
        var pending = new HashSet<int>(seed); pending.ExceptWith(region);
        if (comps.Count > 300) {
            // rings of triangles around all defects until they are connected
            var grown = new HashSet<int>(seed);
            for (int i = 0; i < 400 && Components(m, grown, false).Count > 1; ++i) grown = Dilate(m, grown, false);
            return Close(m, grown, area);
        }
        while (pending.Count > 0) {
            // BFS from the region over triangles (edge adjacency) to the nearest pending defect triangle
            var prev = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int t in region) { prev[t] = -1; q.Enqueue(t); }
            int hit = -1;
            while (q.Count > 0 && hit < 0) {
                int t = q.Dequeue();
                Index3i nb = m.GetTriNeighbourTris(t);
                for (int k = 0; k < 3; ++k) {
                    int u = nb[k];
                    if (u < 0 || prev.ContainsKey(u)) continue;
                    prev[u] = t;
                    if (pending.Contains(u)) { hit = u; break; }
                    q.Enqueue(u);
                }
            }
            if (hit < 0) {
                // (another component of the surface: not reachable; the patch cannot be one region)
                var res = Close(m, region, area); res.Ok = false; return res;
            }
            for (int t = hit; t >= 0 && !region.Contains(t); t = prev[t]) region.Add(t);
            // the component of the hit joins
            foreach (var c in comps) if (c.Contains(hit)) { foreach (int t in c) { region.Add(t); pending.Remove(t); } break; }
            // (defect triangles on the path)
            pending.ExceptWith(region);
        }
        return Close(m, region, area);
    }
}
