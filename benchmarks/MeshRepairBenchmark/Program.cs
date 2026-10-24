// Runs MeshAutoRepair on one mesh file and prints one JSON line with the result
// (mesh statistics and quality metrics before and after the repair).
// Usage: MeshRepairBenchmark <meshfile> [std|soup|manual] [none|meters|geo] [tolerance factor]
//   std    = StandardMeshReader defaults (STL vertex welding, DMesh3Builder)
//   soup   = STL triangle soup, no vertex welding
//   manual = weld by exact position ourselves and call DMesh3.AppendTriangle directly
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using g3;
using gs;
using MeshRepairBenchmark;

class Program
{
    static int Main(string[] args)
    {
        var r = new System.Collections.Generic.Dictionary<string, object>();
        r["file"] = System.IO.Path.GetFileName(args[0]);
        string stage = "load";
        var sw = Stopwatch.StartNew();
        try {
            string mode = args.Length > 1 ? args[1] : "std";
            r["mode"] = mode;
            ReadOptions opts = new ReadOptions();
            if (mode != "std")
                opts.CustomFlags.Integers[STLReader.StrategyFlag] = (int)STLReader.Strategy.NoProcessing;
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult rr = StandardMeshReader.ReadFile(args[0], opts, builder);
            if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) {
                r["status"] = "load_failed"; r["error"] = rr.message;
                Emit(r); return 0;
            }
            DMesh3 mesh = builder.Meshes[0];
            for (int i = 1; i < builder.Meshes.Count; ++i)
                MeshEditor.Append(mesh, builder.Meshes[i]);
            if (mode == "manual")
                mesh = ManualBuild(mesh, r);
            // optional transform: "meters" = scale 0.001, "geo" = translate far from origin
            string xform = args.Length > 2 ? args[2] : "none";
            r["xform"] = xform;
            if (xform == "meters")
                MeshTransforms.Scale(mesh, 0.001);
            else if (xform == "geo")
                MeshTransforms.Translate(mesh, new Vector3d(500000, 5000000, 0));
            r["load_ms"] = sw.ElapsedMilliseconds;
            Stats(mesh, r, "before");

            stage = "repair";
            sw.Restart();
            // BENCH_REPAIR: default = MeshAutoRepair, cleanup = MeshAutoRepair with PostRepairCleanup,
            // instr = InstrumentedAutoRepair, records cheap quality metrics after every pipeline step
            string repairMode = Environment.GetEnvironmentVariable("BENCH_REPAIR") ?? "default";
            r["repair_mode"] = repairMode;
            double tolFactor = 1;
            // optional 4th arg: multiply default tolerances (e.g. 0.001 when the data is in meters)
            if (args.Length > 3) {
                tolFactor = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
                r["tol_factor"] = tolFactor;
            }
            DMesh3 result;
            bool ok;
            if (repairMode == "instr") {
                var repair = new InstrumentedAutoRepair(mesh);
                repair.RepairTolerance *= tolFactor;
                repair.MinEdgeLengthTol *= tolFactor;
                var stages = new System.Collections.Generic.List<object>();
                repair.OnStage = (name, sm) => {
                    var q = new System.Collections.Generic.Dictionary<string, object>();
                    q["stage"] = name;
                    Quality(sm, q, "q", false);
                    q["tris"] = sm.TriangleCount;
                    q["boundary_edges"] = MeshIterators.BoundaryEdges(sm).Count();
                    q["bowties"] = sm.VertexIndices().Count(v => sm.IsBowtieVertex(v));
                    stages.Add(q);
                };
                ok = repair.Apply();
                result = repair.Mesh;
                r["stages"] = stages;
            } else {
                var repair = new MeshAutoRepair(mesh);
                repair.RepairTolerance *= tolFactor;
                repair.MinEdgeLengthTol *= tolFactor;
                repair.PostRepairCleanup = repairMode == "cleanup";
                ok = repair.Apply();
                result = repair.Mesh;
            }
            r["repair_ms"] = sw.ElapsedMilliseconds;
            r["apply_returned"] = ok;

            stage = "stats_after";
            Stats(result, r, "after");
            bool valid = true;
            try { result.CheckValidity(false, FailMode.ReturnOnly); } catch { valid = false; }
            r["after_valid"] = valid;
            r["status"] = result.TriangleCount == 0 ? "emptied"
                        : result.IsClosed() ? "closed" : "not_closed";
        } catch (Exception e) {
            r["status"] = "exception";
            r["stage"] = stage;
            r["ms"] = sw.ElapsedMilliseconds;
            r["error"] = e.GetType().Name + ": " + e.Message;
            r["trace"] = string.Join(" | ", (e.StackTrace ?? "").Split('\n')
                .Select(l => l.Trim()).Where(l => l.StartsWith("at g3.")).Take(4)
                .Select(l => l.Substring(3).Split(" in ")[0]));
        }
        Emit(r);
        return 0;
    }

    // naive wrapper-style construction: exact-position weld, AppendTriangle, skip failures
    static DMesh3 ManualBuild(DMesh3 soup, System.Collections.Generic.Dictionary<string, object> r)
    {
        var result = new DMesh3();
        var map = new System.Collections.Generic.Dictionary<Vector3d, int>();
        int nNonManifold = 0, nInvalid = 0;
        foreach (int tid in soup.TriangleIndices()) {
            Index3i t = soup.GetTriangle(tid);
            Index3i nt = Index3i.Zero;
            for (int j = 0; j < 3; ++j) {
                Vector3d v = soup.GetVertex(t[j]);
                if (!map.TryGetValue(v, out int vid)) { vid = result.AppendVertex(v); map[v] = vid; }
                nt[j] = vid;
            }
            int res = result.AppendTriangle(nt);
            if (res == DMesh3.NonManifoldID) nNonManifold++;
            else if (res < 0) nInvalid++;
        }
        r["build_skipped_nonmanifold"] = nNonManifold;
        r["build_skipped_invalid"] = nInvalid;
        return result;
    }

    // topological and geometric quality checks
    static void Quality(DMesh3 m, System.Collections.Generic.Dictionary<string, object> r, string p, bool bExpensive = true)
    {
        double diag = Math.Max(m.CachedBounds.DiagonalLength, MathUtil.Epsilon);
        double areaEps = 1e-12 * diag * diag;

        // degenerate (near-zero area) and sliver triangles
        int nDegen = 0, nSliver = 0;
        foreach (int tid in m.TriangleIndices()) {
            m.GetTriVertices(tid, ref vA, ref vB, ref vC);
            double area = MathUtil.Area(vA, vB, vC);
            if (area < areaEps) { nDegen++; continue; }
            double maxEdge = Math.Max(vA.Distance(vB), Math.Max(vB.Distance(vC), vC.Distance(vA)));
            // ratio of longest edge to height over it; equilateral ~ 1.15
            if (maxEdge * maxEdge / (2 * area) > 1000) nSliver++;
        }
        r[p + "_degenerate_tris"] = nDegen;
        r[p + "_sliver_tris"] = nSliver;

        // orientation consistency and fold-overs on interior edges
        int nFlippedEdges = 0, nFolds = 0;
        double foldCos = Math.Cos(175.0 * MathUtil.Deg2Rad);
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Index2i ev = m.GetEdgeV(eid);
            bool dirA = edge_dir(m.GetTriangle(et.a), ev.a, ev.b);
            bool dirB = edge_dir(m.GetTriangle(et.b), ev.a, ev.b);
            if (dirA == dirB) { nFlippedEdges++; continue; }
            if (m.GetTriArea(et.a) < areaEps || m.GetTriArea(et.b) < areaEps) continue;
            if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) < foldCos) nFolds++;
        }
        r[p + "_inconsistent_orient_edges"] = nFlippedEdges;
        r[p + "_folded_edges_gt175deg"] = nFolds;

        // duplicate triangles (same vertex set, any orientation)
        var seen = new System.Collections.Generic.HashSet<(int, int, int)>();
        int nDup = 0;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            int[] s = { t.a, t.b, t.c }; Array.Sort(s);
            if (!seen.Add((s[0], s[1], s[2]))) nDup++;
        }
        r[p + "_duplicate_tris"] = nDup;

        // self-intersections between triangles that share no vertex (coplanar overlaps not detected)
        int nSelf = 0;
        if (bExpensive && m.TriangleCount > 0 && m.TriangleCount <= 200000) {
            // g3's tri-tri intersection throws on (near-)degenerate triangles; those are counted
            // separately above, so test the remaining triangles only (vertex ids are preserved)
            var copy = new DMesh3(m);
            foreach (int tid in copy.TriangleIndices().ToArray()) {
                copy.GetTriVertices(tid, ref vA, ref vB, ref vC);
                double maxEdge = Math.Max(vA.Distance(vB), Math.Max(vB.Distance(vC), vC.Distance(vA)));
                double area = MathUtil.Area(vA, vB, vC);
                if (area < areaEps || maxEdge * maxEdge / (2 * area) > 1e6)
                    copy.RemoveTriangle(tid, false, false);
            }
            var tree = new DMeshAABBTree3(copy, true);
            var res = tree.FindAllIntersections(tree);
            var pairs = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var x in res.Points.Select(q => (q.t0, q.t1)).Concat(res.Segments.Select(q => (q.t0, q.t1)))) {
                if (x.t0 == x.t1) continue;
                Index3i ta = copy.GetTriangle(x.t0), tb = copy.GetTriangle(x.t1);
                if (ta.a == tb.a || ta.a == tb.b || ta.a == tb.c || ta.b == tb.a || ta.b == tb.b || ta.b == tb.c
                    || ta.c == tb.a || ta.c == tb.b || ta.c == tb.c) continue;
                pairs.Add((Math.Min(x.t0, x.t1), Math.Max(x.t0, x.t1)));
            }
            nSelf = pairs.Count;
        } else nSelf = -1;
        r[p + "_self_intersecting_pairs"] = nSelf;

        // inside-out closed components (negative volume and not a cavity inside another component)
        int nInverted = MeshPostRepairCleanup.FindInvertedComponents(m).Count;
        r[p + "_inverted_components"] = nInverted;
    }
    static Vector3d vA, vB, vC;

    // true if triangle t traverses a->b
    static bool edge_dir(Index3i t, int a, int b)
    {
        for (int j = 0; j < 3; ++j)
            if (t[j] == a) return t[(j + 1) % 3] == b;
        return false;
    }

    static void Stats(DMesh3 m, System.Collections.Generic.Dictionary<string, object> r, string p)
    {
        Quality(m, r, p);
        r[p + "_tris"] = m.TriangleCount;
        r[p + "_verts"] = m.VertexCount;
        r[p + "_boundary_edges"] = MeshIterators.BoundaryEdges(m).Count();
        r[p + "_bowties"] = m.VertexIndices().Count(v => m.IsBowtieVertex(v));
        r[p + "_components"] = new MeshConnectedComponents(m).Let(c => { c.FindConnectedT(); return c.Count; });
        r[p + "_closed"] = m.IsClosed();
        r[p + "_bbox_diag"] = Math.Round(m.CachedBounds.DiagonalLength, 4);
        r[p + "_edges_below_1e-4"] = m.EdgeIndices().Count(e => {
            Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) < 0.0001; });
    }

    static void Emit(object o) => Console.WriteLine(JsonSerializer.Serialize(o));
}

static class Ext
{
    public static R Let<T, R>(this T t, Func<T, R> f) => f(t);
}
