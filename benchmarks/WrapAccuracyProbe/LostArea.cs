// Mode "lost" (issue #1479): what does MeshRobustRepair lose of an open input, and why?
//   per mesh: structure of the input (components, boundary loops, self-intersections, winding number field, skin / interior / outside
//   surface by the winding number on both sides), the result of MeshRobustRepair (stage, reasons), the area shares lost and added at
//   several tolerances for the robust result and for each stage on its own, and the lost input triangles clustered into regions (whole
//   component?, skin / interior wall / outside sheet, inside or outside the result's solid, visible from outside).
// Public API only. The meshes are read from a local copy; the output holds ids and numbers only.
//   lost <out.jsonl> <mesh file or @list>... [--taus 1e-4,4e-4,1e-3,4e-3] [--no-stages] [--no-wrap]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using g3;
using gs;

static class LostArea
{
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        string outFile = args[1];
        var inputs = new List<string>();
        double[] taus = { 1e-4, 4e-4, 1e-3, 4e-3 };
        bool stages = true, wrap = true, flip = false;
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--taus") taus = args[++i].Split(',').Select(s => double.Parse(s, Inv)).ToArray();
            else if (args[i] == "--no-stages") stages = false;
            else if (args[i] == "--no-wrap") wrap = false;
            else if (args[i] == "--flip") flip = true;          // reverse every triangle first (a test of the orientation hypothesis)
            else if (args[i].StartsWith("@")) inputs.AddRange(File.ReadAllLines(args[i].Substring(1)).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")));
            else inputs.Add(args[i]);
        }
        foreach (string spec in inputs) {
            string name = Inputs.NameOf(spec);
            DMesh3 input = Inputs.Load(spec);
            if (input == null) { Console.WriteLine(name + ": cannot read"); continue; }
            if (flip) { input.ReverseOrientation(); name += "_flipped"; }
            long rawFacets = -1;
            if (!spec.StartsWith("proc:") && File.Exists(spec)) { long len = new FileInfo(spec).Length; rawFacets = (len - 84) / 50; if (len != 84 + 50 * rawFacets) { rawFacets = 0; foreach (string l in File.ReadLines(spec)) if (l.TrimStart().StartsWith("facet normal")) rawFacets++; } }
            var row = Analyse(name, input, taus, stages, wrap, rawFacets);
            File.AppendAllText(outFile, JsonSerializer.Serialize(row, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }) + "\n");
        }
        return 0;
    }

    // ---- sampling helpers ----
    static Vector3d[] AreaSamples(DMesh3 m, int n, int seed, out int[] tidOf)
    {
        var tids = new List<int>(); var cum = new List<double>(); double total = 0;
        foreach (int tid in m.TriangleIndices()) { total += m.GetTriArea(tid); tids.Add(tid); cum.Add(total); }
        var pts = new Vector3d[n]; tidOf = new int[n];
        if (tids.Count == 0 || !(total > 0)) return new Vector3d[0];
        var rnd = new Random(seed); int ti = 0;
        for (int k = 0; k < n; ++k) {
            double target = (k + 0.5) / n * total;
            while (ti < cum.Count - 1 && cum[ti] < target) ti++;
            Index3i t = m.GetTriangle(tids[ti]);
            double u = rnd.NextDouble(), v = rnd.NextDouble();
            if (u + v > 1) { u = 1 - u; v = 1 - v; }
            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
            pts[k] = a + u * (b - a) + v * (c - a); tidOf[k] = tids[ti];
        }
        return pts;
    }

    // sorted distances of n area-stratified sample points of `from` to `to`
    static double[] Dists(DMesh3 from, DMeshAABBTree3 toTree, int n)
    {
        Vector3d[] pts = AreaSamples(from, n, 1, out int[] _);
        var d = new double[pts.Length];
        Parallel.For(0, pts.Length, k => { toTree.FindNearestTriangle(pts[k], out double d2); d[k] = Math.Sqrt(d2); });
        Array.Sort(d);
        return d;
    }

    static double ShareAbove(double[] sorted, double tau)
    {
        if (sorted.Length == 0) return 0;
        int lo = 0, hi = sorted.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (sorted[mid] > tau) hi = mid; else lo = mid + 1; }
        return (double)(sorted.Length - lo) / sorted.Length;
    }

    static Dictionary<string, object> Describe(string stage, DMesh3 res, DMesh3 input, DMeshAABBTree3 inTree, DMeshAABBTree3 resTree, double diag, double[] taus)
    {
        var r = new Dictionary<string, object> { ["stage"] = stage };
        if (res == null || res.TriangleCount == 0) { r["tris"] = 0; return r; }
        r["tris"] = res.TriangleCount;
        r["closed"] = res.IsClosed();
        r["verdict"] = Metrics.Verdict(res);
        r["area_ratio"] = Metrics.Area(res) / Metrics.Area(input);
        try { MeshRepairMetrics.VolumeIoUAtLeast(input, res, 0.99, out double iou); r["iou_vs_input_solid"] = iou; } catch (Exception) { }
        double[] lost = Dists(input, resTree, 60000), added = Dists(res, inTree, 60000);
        foreach (double t in taus) {
            string k = t.ToString("G", Inv);
            r["lost@" + k] = ShareAbove(lost, t * diag);
            r["added@" + k] = ShareAbove(added, t * diag);
        }
        r["solid_share_of_input_box"] = SolidShare(resTree, input.GetBounds());
        r["lost_p99"] = lost[(int)(0.99 * (lost.Length - 1))] / diag;
        r["lost_max"] = lost[lost.Length - 1] / diag;
        return r;
    }

    // share of uniform points of the box with winding number > 0.5 (the solid's share of the box); 4000 points, fixed seed
    static double SolidShare(DMeshAABBTree3 tree, AxisAlignedBox3d bb)
    {
        var rnd = new Random(21); int n = 4000; var pts = new Vector3d[n];
        for (int k = 0; k < n; ++k) pts[k] = new Vector3d(bb.Min.x + rnd.NextDouble() * bb.Width, bb.Min.y + rnd.NextDouble() * bb.Height, bb.Min.z + rnd.NextDouble() * bb.Depth);
        var ins = new bool[n];
        tree.WindingNumber(pts[0]);
        Parallel.For(0, n, k => { ins[k] = tree.WindingNumber(pts[k]) > 0.5; });
        return ins.Count(x => x) / (double)n;
    }

    // ---- structure ----
    static Dictionary<string, object> Structure(DMesh3 m, DMeshAABBTree3 tree, double diag, double tau, long rawFacets)
    {
        var s = new Dictionary<string, object>();
        s["tris"] = m.TriangleCount; s["verts"] = m.VertexCount; s["raw_stl_facets"] = rawFacets;
        s["area"] = Metrics.Area(m); s["diag"] = diag;
        int be = 0, ne = 0;
        foreach (int eid in m.EdgeIndices()) { ne++; if (m.IsBoundaryEdge(eid)) be++; }
        s["edges"] = ne; s["boundary_edges"] = be;
        try {
            var bl = new MeshBoundaryLoops(m, true);
            s["boundary_loops"] = bl.Count; s["boundary_spans"] = bl.SpanCount;
            var sizes = bl.Loops.Select(l => l.VertexCount).OrderByDescending(x => x).Take(8).ToList();
            s["loop_sizes_top"] = string.Join(",", sizes);
        } catch (Exception e) { s["boundary_loops"] = -1; s["loops_error"] = e.GetType().Name; }
        // edges whose two triangles traverse it in the same direction (inconsistent orientation; DMesh3 does not forbid them after edits)
        int inconsistent = 0, interior = 0;
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid); if (et.b == DMesh3.InvalidID) continue;
            interior++;
            Index2i ev = m.GetEdgeV(eid);
            bool Dir(int t) { Index3i tv = m.GetTriangle(t); for (int j = 0; j < 3; ++j) { int a = tv[j], b = tv[(j + 1) % 3]; if (a == ev.a && b == ev.b) return true; if (a == ev.b && b == ev.a) return false; } return true; }
            if (Dir(et.a) == Dir(et.b)) inconsistent++;
        }
        s["edges_inconsistently_oriented_share"] = interior == 0 ? 0.0 : inconsistent / (double)interior;
        s["bowties"] = m.VertexIndices().Count(v => m.IsBowtieVertex(v));
        var comps = new MeshConnectedComponents(m); comps.FindConnectedT();
        s["components"] = comps.Count;
        var cinfo = new List<string>(); int closedComps = 0;
        double total = (double)s["area"];
        foreach (var c in comps.Components.OrderByDescending(c => c.Indices.Length).Take(8)) {
            double a = c.Indices.Sum(t => m.GetTriArea(t));
            cinfo.Add(c.Indices.Length + "t/" + (a / total).ToString("0.###", Inv));
        }
        foreach (var c in comps.Components) {
            bool closed = true;
            foreach (int t in c.Indices) { Index3i te = m.GetTriEdges(t); for (int j = 0; j < 3 && closed; ++j) if (m.IsBoundaryEdge(te[j])) closed = false; if (!closed) break; }
            if (closed) closedComps++;
        }
        s["components_closed"] = closedComps;
        s["components_top"] = string.Join(" ", cinfo);
        s["selfint_pairs_at_most_1e6"] = MeshRepairMetrics.SelfIntersectionsAtMost(m, 1000000);
        // the largest components one by one: box, boundary, orientation (winding number of the component alone at random points of its box)
        var cd = new List<string>();
        foreach (var c in comps.Components.OrderByDescending(c => c.Indices.Length).Take(4)) {
            if (c.Indices.Length < 4) continue;
            DMesh3 sub = new DSubmesh3(m, c.Indices).SubMesh;
            AxisAlignedBox3d cb = sub.GetBounds();
            var bbd = new AxisAlignedBox3d(true); int bedges = 0;
            foreach (int eid in sub.BoundaryEdgeIndices()) { bedges++; Index2i ev = sub.GetEdgeV(eid); bbd.Contain(sub.GetVertex(ev.a)); bbd.Contain(sub.GetVertex(ev.b)); }
            var st2 = new DMeshAABBTree3(sub, true); var r2 = new Random(3); int neg = 0, amb = 0, one = 0, zero = 0, nn = 400;
            for (int k = 0; k < nn; ++k) {
                var q = new Vector3d(cb.Min.x + r2.NextDouble() * cb.Width, cb.Min.y + r2.NextDouble() * cb.Height, cb.Min.z + r2.NextDouble() * cb.Depth);
                double wv = st2.WindingNumber(q);
                if (wv < -0.1) neg++; else if (wv <= 0.1) zero++; else if (wv < 0.9) amb++; else one++;
            }
            cd.Add("[" + c.Indices.Length + "t box " + (cb.Width / diag).ToString("0.##", Inv) + "x" + (cb.Height / diag).ToString("0.##", Inv) + "x" + (cb.Depth / diag).ToString("0.##", Inv)
                + " diag, boundary edges " + bedges + (bedges > 0 ? " spanning " + (bbd.DiagonalLength / cb.DiagonalLength).ToString("0.##", Inv) + " of its box diagonal" : "")
                + ", own winding at random box points neg/0/between/1: " + (neg / (double)nn).ToString("0.##", Inv) + "/" + (zero / (double)nn).ToString("0.##", Inv) + "/" + (amb / (double)nn).ToString("0.##", Inv) + "/" + (one / (double)nn).ToString("0.##", Inv) + "]");
        }
        s["components_detail"] = string.Join(" ", cd);
        // winding number field: uniform points in the box
        var rnd = new Random(11); AxisAlignedBox3d bb = m.GetBounds();
        var hist = new int[7]; int n = 6000;
        var pts = new Vector3d[n];
        for (int k = 0; k < n; ++k) pts[k] = new Vector3d(bb.Min.x + rnd.NextDouble() * bb.Width, bb.Min.y + rnd.NextDouble() * bb.Height, bb.Min.z + rnd.NextDouble() * bb.Depth);
        var w = new double[n];
        tree.WindingNumber(pts[0]);
        Parallel.For(0, n, k => { w[k] = tree.WindingNumber(pts[k]); });
        foreach (double x in w) hist[x < -0.1 ? 0 : x <= 0.1 ? 1 : x < 0.9 ? 2 : x <= 1.1 ? 3 : x < 1.9 ? 4 : x <= 2.1 ? 5 : 6]++;
        s["wn_box_hist(<-.1,~0,(.1-.9),~1,(1.1-1.9),~2,>2.1)"] = string.Join(",", hist.Select(h => (h / (double)n).ToString("0.###", Inv)));
        s["wn_box_ambiguous_share"] = hist[2] / (double)n;
        s["input_solid_share_of_box_w>0.5"] = SolidShare(tree, bb);
        // surface classes by the winding number on both sides (at distance tau): skin, interior wall, outside sheet, ambiguous
        var sc = SurfaceClasses(m, tree, tau);
        s["surface_skin_share"] = sc[0]; s["surface_interior_share"] = sc[1]; s["surface_outside_share"] = sc[2]; s["surface_ambiguous_share"] = sc[3];
        // doubled sheets: another sheet of the input within 2 tau along the normal line (not the own triangle, not a neighbour)
        s["doubled_share_within_2tau"] = DoubledShare(m, tree, 2 * tau);
        s["doubled_share_within_20tau"] = DoubledShare(m, tree, 20 * tau);
        return s;
    }

    // [skin, interior, outside, ambiguous] area shares (area-stratified samples)
    static double[] SurfaceClasses(DMesh3 m, DMeshAABBTree3 tree, double eps)
    {
        Vector3d[] pts = AreaSamples(m, 12000, 5, out int[] tid);
        var cls = new int[pts.Length];
        tree.WindingNumber(pts[0]);
        Parallel.For(0, pts.Length, k => {
            cls[k] = ClassOf(m, tree, tid[k], pts[k], eps);
        });
        var r = new double[4];
        foreach (int c in cls) r[c] += 1.0 / pts.Length;
        return r;
    }

    // 0 skin (one side inside, the other outside), 1 interior (both sides inside), 2 outside sheet (both outside), 3 ambiguous (a value within 0.15 of 0.5)
    static int ClassOf(DMesh3 m, DMeshAABBTree3 tree, int tid, Vector3d p, double eps)
    {
        Vector3d nrm = m.GetTriNormal(tid);
        double wf = tree.WindingNumber(p + eps * nrm), wb = tree.WindingNumber(p - eps * nrm);
        if (Math.Abs(wf - 0.5) < 0.15 || Math.Abs(wb - 0.5) < 0.15) return 3;
        bool f = wf > 0.5, b = wb > 0.5;
        return f != b ? 0 : (f ? 1 : 2);
    }

    static double DoubledShare(DMesh3 m, DMeshAABBTree3 tree, double maxDist)
    {
        Vector3d[] pts = AreaSamples(m, 6000, 9, out int[] tid);
        var dbl = new bool[pts.Length];
        Parallel.For(0, pts.Length, k => {
            Vector3d nrm = m.GetTriNormal(tid[k]);
            var own = new HashSet<int> { tid[k] };
            Index3i tv = m.GetTriangle(tid[k]);
            foreach (int v in new[] { tv.a, tv.b, tv.c }) foreach (int t in m.VtxTrianglesItr(v)) own.Add(t);
            foreach (double sgn in new[] { 1.0, -1.0 }) {
                var hits = new List<int>();
                tree.FindAllHitTriangles(new Ray3d(pts[k], sgn * nrm, true), hits, maxDist);
                foreach (int h in hits) if (!own.Contains(h)) { dbl[k] = true; return; }
            }
        });
        return dbl.Count(x => x) / (double)pts.Length;
    }

    // ---- visibility ----
    static Vector3d[] Directions(int n)
    {
        var d = new Vector3d[n]; double ga = Math.PI * (3 - Math.Sqrt(5));
        for (int i = 0; i < n; ++i) {
            double z = 1 - 2.0 * (i + 0.5) / n, r = Math.Sqrt(1 - z * z), a = ga * i;
            d[i] = new Vector3d(r * Math.Cos(a), r * Math.Sin(a), z);
        }
        return d;
    }

    // share of the rays from just off the triangle's centroid (both sides, directions on the side) that leave the input without a hit
    static double Escape(DMesh3 m, DMeshAABBTree3 tree, int tid, Vector3d[] dirs, double eps)
    {
        Vector3d c = m.GetTriCentroid(tid), nrm = m.GetTriNormal(tid);
        int esc = 0, tot = 0;
        foreach (double sgn in new[] { 1.0, -1.0 }) {
            Vector3d o = c + sgn * eps * nrm;
            foreach (Vector3d d in dirs) {
                if (d.Dot(nrm) * sgn <= 0.05) continue;
                tot++;
                if (tree.FindNearestHitTriangle(new Ray3d(o, d, true)) == DMesh3.InvalidID) esc++;
            }
        }
        return tot == 0 ? 0 : (double)esc / tot;
    }

    // ---- main analysis ----
    public static Dictionary<string, object> Analyse(string name, DMesh3 input, double[] taus, bool stages, bool wrap, long rawFacets)
    {
        var row = new Dictionary<string, object> { ["input"] = name };
        double diag = Inputs.Diag(input), tau0 = 4e-4;
        var inTree = new DMeshAABBTree3(input, true);
        Console.WriteLine("== " + name);
        var st = Structure(input, inTree, diag, tau0 * diag, rawFacets);
        row["structure"] = st;
        foreach (var kv in st) Console.WriteLine("  " + kv.Key + " = " + (kv.Value is double d ? d.ToString("G5", Inv) : kv.Value));

        // robust
        var sw = Stopwatch.StartNew();
        var rr = new MeshRobustRepair(new DMesh3(input, true));
        bool ok = rr.Apply();
        var rob = new Dictionary<string, object> {
            ["ok"] = ok, ["path"] = rr.ResultPath.ToString(), ["ms"] = sw.Elapsed.TotalMilliseconds, ["fallback_reason"] = rr.FallbackReason,
            ["exact_resolve_reason"] = rr.ExactResolveReason, ["solidify_reason"] = rr.SolidifyReason, ["wrap_reason"] = rr.WrapReason, ["result_reason"] = rr.ResultReason,
            ["used_input"] = rr.UsedInput, ["repair_volume_iou"] = rr.RepairVolumeIoU, ["topology_changed"] = rr.TopologyChanged
        };
        row["robust"] = rob;
        Console.WriteLine("  robust: ok=" + ok + " path=" + rr.ResultPath + " usedInput=" + rr.UsedInput + " repairIoU=" + rr.RepairVolumeIoU.ToString("G4", Inv)
            + " fallback='" + rr.FallbackReason + "' resolve='" + rr.ExactResolveReason + "' solidify='" + rr.SolidifyReason + "'");
        DMesh3 res = rr.Mesh;
        var resTree = new DMeshAABBTree3(res, true);
        var stageRows = new List<Dictionary<string, object>>();
        var rd = Describe("robust", res, input, inTree, resTree, diag, taus);
        stageRows.Add(rd);

        if (stages) {
            Action<string, Func<DMesh3>> stage = (label, f) => {
                var t = Stopwatch.StartNew(); DMesh3 m = null; string err = "";
                try { m = f(); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                if (m == null || m.TriangleCount == 0) { stageRows.Add(new Dictionary<string, object> { ["stage"] = label, ["tris"] = 0, ["error"] = err, ["ms"] = t.Elapsed.TotalMilliseconds }); return; }
                var d = Describe(label, m, input, inTree, new DMeshAABBTree3(m, true), diag, taus); d["ms"] = t.Elapsed.TotalMilliseconds; stageRows.Add(d);
            };
            // MeshRobustRepair with a check switched off (settings of the library, no change of it)
            stage("robust_MinVolumeIoU0", () => { var q = new MeshRobustRepair(new DMesh3(input, true)) { MinVolumeIoU = 0 }; q.Apply(); Console.WriteLine("  robust_MinVolumeIoU0: path=" + q.ResultPath + " fallback='" + q.FallbackReason + "' resolve='" + q.ExactResolveReason + "'"); return q.Mesh; });
            stage("robust_NoExactResolve", () => { var q = new MeshRobustRepair(new DMesh3(input, true)) { ExactResolve = false }; q.Apply(); Console.WriteLine("  robust_NoExactResolve: path=" + q.ResultPath + " fallback='" + q.FallbackReason + "'"); return q.Mesh; });
            DMesh3 autoRes = null;
            stage("auto", () => { var a = new MeshAutoRepair(new DMesh3(input, true)) { PostRepairCleanup = true }; a.Apply(); autoRes = a.Mesh; return a.Mesh; });
            stage("resolve_input", () => ExactMeshBoolean.Resolve(new DMesh3(input, true)));
            if (autoRes != null) stage("resolve_auto", () => ExactMeshBoolean.Resolve(new DMesh3(autoRes, true)));
            stage("solidify_input", () => { var s = new MeshWindingNumberSolidify(new DMesh3(input, true)) { GridResolution = 128, Adaptive = true }; s.Compute(); return s.Result; });
            if (autoRes != null) stage("solidify_auto", () => { var s = new MeshWindingNumberSolidify(new DMesh3(autoRes, true)) { GridResolution = 128, Adaptive = true }; s.Compute(); return s.Result; });
            if (wrap) {
                stage("wrap_robust_defaults", () => SpikeA.Wrap(new DMesh3(input, true), diag / 300, diag / 3000, 120, 1).Result);
                stage("wrap_d100_1e-4", () => SpikeA.Wrap(new DMesh3(input, true), diag / 100, diag * 1e-4, 120, 1).Result);
            }
        }
        row["stages"] = stageRows;
        foreach (var r in stageRows) {
            var parts = new List<string>();
            foreach (var kv in r) parts.Add(kv.Key + "=" + (kv.Value is double d ? d.ToString("G4", Inv) : kv.Value));
            Console.WriteLine("  stage: " + string.Join(" ", parts));
        }

        row["regions"] = Regions(input, inTree, res, resTree, diag, tau0, rr);
        return row;
    }

    // lost input triangles (>= 2 of 4 points farther than tau from the result) clustered by edge adjacency
    static Dictionary<string, object> Regions(DMesh3 input, DMeshAABBTree3 inTree, DMesh3 res, DMeshAABBTree3 resTree, double diag, double tauRel, MeshRobustRepair rr)
    {
        double tau = tauRel * diag, eps = tau;
        var o = new Dictionary<string, object>();
        var lost = new HashSet<int>();
        var tris = input.TriangleIndices().ToList();
        var isLost = new bool[input.MaxTriangleID];
        Parallel.ForEach(tris, tid => {
            Index3i tv = input.GetTriangle(tid);
            Vector3d a = input.GetVertex(tv.a), b = input.GetVertex(tv.b), c = input.GetVertex(tv.c);
            int nfar = 0;
            foreach (Vector3d q in new[] { (a + b + c) / 3, (4 * a + b + c) / 6, (a + 4 * b + c) / 6, (a + b + 4 * c) / 6 }) {
                resTree.FindNearestTriangle(q, out double d2);
                if (Math.Sqrt(d2) > tau) nfar++;
            }
            if (nfar >= 2) isLost[tid] = true;
        });
        foreach (int t in tris) if (isLost[t]) lost.Add(t);
        double total = Metrics.Area(input), lostArea = lost.Sum(t => input.GetTriArea(t));
        o["lost_tris"] = lost.Count; o["lost_area_share_by_triangles"] = lostArea / total;
        var comps = new MeshConnectedComponents(input); comps.FindConnectedT();
        var compOf = new int[input.MaxTriangleID];
        for (int i = 0; i < comps.Count; ++i) foreach (int t in comps[i].Indices) compOf[t] = i;
        // clusters
        var seen = new HashSet<int>(); var clusters = new List<List<int>>();
        foreach (int s in lost) {
            if (!seen.Add(s)) continue;
            var list = new List<int>(); var stack = new Stack<int>(); stack.Push(s);
            while (stack.Count > 0) {
                int t = stack.Pop(); list.Add(t);
                Index3i nb = input.GetTriNeighbourTris(t);
                for (int j = 0; j < 3; ++j) { int n = nb[j]; if (n >= 0 && lost.Contains(n) && seen.Add(n)) stack.Push(n); }
            }
            clusters.Add(list);
        }
        o["regions"] = clusters.Count;
        // per-triangle classes: input winding both sides, result inside / outside, visibility
        Vector3d[] dirs = Directions(64);
                        var clsA = new int[input.MaxTriangleID]; var inResA = new bool[input.MaxTriangleID]; var visA = new double[input.MaxTriangleID];
        inTree.WindingNumber(input.GetTriCentroid(tris[0]));
        bool resClosed = res.IsClosed();
        if (resClosed) resTree.WindingNumber(res.GetVertex(res.VertexIndices().First()));
        Parallel.ForEach(tris, t => {
            Vector3d c = input.GetTriCentroid(t);
            clsA[t] = ClassOf(input, inTree, t, c, eps);
            visA[t] = Escape(input, inTree, t, dirs, eps);
            if (isLost[t]) inResA[t] = resClosed ? resTree.WindingNumber(c) > 0.5 : resTree.FastWindingNumber(c) > 0.5;
        });
        double[] byClass = new double[4]; double lostInRes = 0, lostVisible = 0, lostVisible10 = 0;
        double visArea = 0, visLostArea = 0, skinVisArea = 0, skinVisLost = 0, skinArea = 0, skinLost = 0;
        foreach (int t in tris) {
            double a = input.GetTriArea(t);
            bool isVis = visA[t] > 0, isVis10 = visA[t] >= 0.1;
            if (isVis10) { visArea += a; if (isLost[t]) visLostArea += a; }
            if (clsA[t] == 0) { skinArea += a; if (isLost[t]) skinLost += a; if (isVis10) { skinVisArea += a; if (isLost[t]) skinVisLost += a; } }
            if (!isLost[t]) continue;
            byClass[clsA[t]] += a;
            if (inResA[t]) lostInRes += a;
            if (isVis) lostVisible += a;
            if (isVis10) lostVisible10 += a;
        }
        {
            double lv = 0; var lvc = new double[4]; int lvn = 0;
            foreach (int t in tris) if (isLost[t] && visA[t] >= 0.1) { double a = input.GetTriArea(t); lv += a; lvc[clsA[t]] += a; lvn++; }
            var lvComps = new Dictionary<int, int>(); double lvWhole = 0; int lvWholeComps = 0;
            foreach (int t in tris) if (isLost[t] && visA[t] >= 0.1) { lvComps.TryGetValue(compOf[t], out int c0); lvComps[compOf[t]] = c0 + 1; }
            var lostPerComp = new Dictionary<int, int>(); foreach (int t in lost) { lostPerComp.TryGetValue(compOf[t], out int c0); lostPerComp[compOf[t]] = c0 + 1; }
            foreach (var kv in lvComps) if (lostPerComp[kv.Key] == comps[kv.Key].Indices.Length) { lvWholeComps++; foreach (int t in comps[kv.Key].Indices) if (visA[t] >= 0.1) lvWhole += input.GetTriArea(t); }
            o["lost_visible_components"] = lvComps.Count; o["lost_visible_whole_components"] = lvWholeComps; o["lost_visible_in_whole_components_share_of_input"] = lvWhole / total;
            o["lost_visible_component_sizes"] = string.Join(",", lvComps.Select(kv => comps[kv.Key].Indices.Length + (lostPerComp[kv.Key] == comps[kv.Key].Indices.Length ? "w" : "")).Take(12));
            o["lost_visible_tris"] = lvn; o["lost_visible_area_share_of_input"] = lv / total;
            o["lost_visible_by_class(skin,interior,outside,ambiguous)_share_of_input"] = string.Join(",", lvc.Select(x => (x / total).ToString("0.####", Inv)));
        }
        // shares of the lost area
        o["lost_skin_share_of_lost"] = byClass[0] / lostArea; o["lost_interior_share_of_lost"] = byClass[1] / lostArea;
        o["lost_outside_sheet_share_of_lost"] = byClass[2] / lostArea; o["lost_ambiguous_share_of_lost"] = byClass[3] / lostArea;
        o["lost_inside_result_solid_share_of_lost"] = lostInRes / lostArea;
        o["lost_visible_any_share_of_lost"] = lostVisible / lostArea; o["lost_visible_ge10pct_share_of_lost"] = lostVisible10 / lostArea;
        o["input_visible_ge10pct_area_share"] = visArea / total;
        o["lost_share_of_visible_surface"] = visArea > 0 ? visLostArea / visArea : 0;
        o["input_skin_area_share"] = skinArea / total; o["lost_share_of_skin"] = skinArea > 0 ? skinLost / skinArea : 0;
        o["lost_share_of_visible_skin"] = skinVisArea > 0 ? skinVisLost / skinVisArea : 0;
        o["lost_area_share_of_input"] = lostArea / total;
        // top clusters
        var infos = new List<string>(); int wholeComps = 0; double wholeCompArea = 0;
        var clusterByComp = clusters.GroupBy(c => compOf[c[0]]).ToList();
        foreach (var g in clusterByComp) {
            int total_t = comps[g.Key].Indices.Length; int lost_t = g.Sum(c => c.Count);
            if (lost_t == total_t) { wholeComps++; wholeCompArea += comps[g.Key].Indices.Sum(t => input.GetTriArea(t)); }
        }
        o["lost_whole_components"] = wholeComps; o["lost_whole_components_area_share_of_lost"] = wholeCompArea / lostArea;
        o["components_touched"] = clusterByComp.Count;
        int shown = 0;
        foreach (var c in clusters.OrderByDescending(c => c.Sum(t => input.GetTriArea(t)))) {
            if (shown++ >= 8) break;
            double a = c.Sum(t => input.GetTriArea(t));
            var bb = new AxisAlignedBox3d(true);
            foreach (int t in c) { Index3i tv = input.GetTriangle(t); bb.Contain(input.GetVertex(tv.a)); bb.Contain(input.GetVertex(tv.b)); bb.Contain(input.GetVertex(tv.c)); }
            int comp = compOf[c[0]];
            double skin = c.Where(t => clsA[t] == 0).Sum(t => input.GetTriArea(t)) / a, inter = c.Where(t => clsA[t] == 1).Sum(t => input.GetTriArea(t)) / a, outs = c.Where(t => clsA[t] == 2).Sum(t => input.GetTriArea(t)) / a;
            double vis = c.Where(t => visA[t] >= 0.1).Sum(t => input.GetTriArea(t)) / a, inr = c.Where(t => inResA[t]).Sum(t => input.GetTriArea(t)) / a;
            infos.Add("[" + c.Count + "t, " + (a / lostArea).ToString("0.###", Inv) + " of lost, box " + (bb.DiagonalLength / diag).ToString("0.###", Inv)
                + " diag, comp#" + comp + (c.Count == comps[comp].Indices.Length ? " WHOLE" : " part of " + comps[comp].Indices.Length + "t")
                + ", skin/interior/outside " + skin.ToString("0.##", Inv) + "/" + inter.ToString("0.##", Inv) + "/" + outs.ToString("0.##", Inv)
                + ", in-result " + inr.ToString("0.##", Inv) + ", visible " + vis.ToString("0.##", Inv) + "]");
        }
        o["top_regions"] = string.Join(" ", infos);
        foreach (var kv in o) Console.WriteLine("  region: " + kv.Key + " = " + (kv.Value is double d ? d.ToString("G4", Inv) : kv.Value));
        return o;
    }
}
