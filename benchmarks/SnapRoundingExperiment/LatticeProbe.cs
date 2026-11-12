// Falsification probe for idea 3 of docs/research/CROSS_FIELD_GEOMETRY.md (docs/design/SNAP_ROUNDING_3D.md, section 6):
// is there, for what SafeRounding leaves, a clean mesh with the same triangles (fixed combinatorics, the vertices away
// from the defects fixed) at representable positions within the drift bound that is left?
//
//   lattice <case> [seconds, default 60] [seed, default 1]
//
// It is a random search, not an algorithm: it moves one to three vertices of a defect at once (what the repair's own
// moves, one vertex at a time and each improving on its own, cannot do) by a random vector of a random size between an
// ulp and the drift left, and keeps a move if the number of defects of the whole mesh (degenerate triangles and
// intersecting pairs, as MeshRobustRepair.NotCleanReasons counts them) does not grow; every vertex stays within the
// drift left of where SafeRounding put it (so the total stays within the bound), and no triangle above 100 times the
// degenerate area turns over. Above 2000 triangles (or with LATTICE_LOCAL=1) the defects are counted at the moved
// vertices only. Ablations: LATTICE_MAX_MOVED=1 (one vertex at a time), LATTICE_PLATEAU=0 (only moves that
// remove a defect). A clean end shows that such a configuration exists; no clean end shows nothing beyond
// this search.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

static class LatticeProbe
{
    public static string Run(string name, DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, double seconds, int seed)
    {
        var ci = CultureInfo.InvariantCulture;
        var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
        if (!boolean.Compute())
            return "{\"case\":\"" + name + "\",\"ok\":false}";
        DMesh3 m = boolean.Result;
        MeshRoundingRepair r = boolean.Rounding;
        double left = r.MaxLength - r.MaxDrift;
        double diag = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
        double eps = 1e-12 * diag * diag;
        var start = new Dictionary<int, Vector3d>();
        var rng = new Random(seed);
        // ablations: LATTICE_MAX_MOVED=1 moves one vertex at a time, LATTICE_PLATEAU=0 keeps only moves that remove a defect
        int maxMoved = int.Parse(Environment.GetEnvironmentVariable("LATTICE_MAX_MOVED") ?? "3");
        bool plateau = Environment.GetEnvironmentVariable("LATTICE_PLATEAU") != "0";
        int before = Defects(m, eps, out List<int> defectTris), defects = before;
        int evaluations = 0, accepted = 0, improving = 0;
        double farthest = 0;
        var clock = Stopwatch.StartNew();
        double ulp = Math.Pow(2, Math.Floor(Math.Log(Math.Max(gs.MeshRepairMetrics.TriangleBounds(m).Min.MaxAbs, gs.MeshRepairMetrics.TriangleBounds(m).Max.MaxAbs), 2)) - 52);
        double span = Math.Log(Math.Max(left / ulp, 2), 2);
        // large meshes: the defects are counted at the moved vertices only, against a box tree of the triangles' boxes
        // at the start grown by the drift left (no vertex moves farther, so the boxes stay supersets)
        bool local = m.TriangleCount > 2000 || Environment.GetEnvironmentVariable("LATTICE_LOCAL") == "1";
        TriangleBoxTree tree = null;
        var treeTids = new List<int>();
        if (local) {
            var boxes = new List<AxisAlignedBox3d>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                var bx = new AxisAlignedBox3d(m.GetVertex(tv.a));
                bx.Contain(m.GetVertex(tv.b)); bx.Contain(m.GetVertex(tv.c));
                bx.Expand(left);
                boxes.Add(bx); treeTids.Add(t);
            }
            tree = new TriangleBoxTree(boxes);
        }
        while (defects > 0 && clock.Elapsed.TotalSeconds < seconds && left > 0) {
            // one to three vertices of a defect triangle, or of it and a neighbour
            int t = defectTris[rng.Next(defectTris.Count)];
            if (m.IsTriangle(t) == false)
                break;
            Index3i tv = m.GetTriangle(t);
            var moved = new List<int>();
            int count = 1 + rng.Next(maxMoved);
            var pool = new List<int> { tv.a, tv.b, tv.c };
            if (rng.Next(3) == 0)
                foreach (int v in new[] { tv.a, tv.b, tv.c })
                    foreach (int u in m.VtxVerticesItr(v))
                        if (pool.Contains(u) == false) pool.Add(u);
            while (moved.Count < count) {
                int v = pool[rng.Next(pool.Count)];
                if (moved.Contains(v) == false) moved.Add(v);
            }
            var saved = moved.Select(v => m.GetVertex(v)).ToArray();
            // the orientation of the triangles at the moved vertices (those above 100 times the degenerate area)
            var tris = new HashSet<int>();
            foreach (int v in moved)
                foreach (int u in m.VtxTrianglesItr(v)) tris.Add(u);
            var normals = tris.ToDictionary(u => u, u => Cross(m, u));
            bool ok = true;
            for (int i = 0; i < moved.Count && ok; ++i) {
                int v = moved[i];
                double size = ulp * Math.Pow(2, rng.NextDouble() * span);
                var dir = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                if (dir.Length == 0) { ok = false; break; }
                Vector3d p = saved[i] + dir * (size / dir.Length);
                if (start.TryGetValue(v, out Vector3d origin) == false)
                    origin = saved[i];
                if (p.Distance(origin) > left) { ok = false; break; }
                m.SetVertex(v, p);
            }
            if (ok)
                foreach (KeyValuePair<int, Vector3d> e in normals)
                    if (e.Value.Length > 200 * eps && Cross(m, e.Key).Dot(e.Value) <= 0) { ok = false; break; }
            int now = defects + 1;
            List<int> nowTris = null;
            if (ok && local) {
                // only the triangles at the moved vertices change: their defects before (positions put back) and after
                var after = moved.Select(v => m.GetVertex(v)).ToArray();
                for (int i = 0; i < moved.Count; ++i) m.SetVertex(moved[i], saved[i]);
                var was = LocalDefects(m, eps, tris, tree, treeTids);
                for (int i = 0; i < moved.Count; ++i) m.SetVertex(moved[i], after[i]);
                var isNow = LocalDefects(m, eps, tris, tree, treeTids);
                now = defects - was.Count + isNow.Count;
                var set = new HashSet<int>(defectTris);
                foreach (long k in was) { if (k < 0) set.Remove((int)~k); else { set.Remove((int)(k >> 32)); set.Remove((int)(k & 0xffffffff)); } }
                // (a triangle of a removed defect may still have another one: the list is rebuilt from the whole mesh when
                // it runs empty, below)
                foreach (long k in isNow) { if (k < 0) set.Add((int)~k); else { set.Add((int)(k >> 32)); set.Add((int)(k & 0xffffffff)); } }
                nowTris = set.ToList();
                nowTris.Sort();
                evaluations++;
            } else if (ok) {
                now = Defects(m, eps, out nowTris);
                evaluations++;
            }
            if (ok && (now < defects || (plateau && now == defects))) {
                for (int i = 0; i < moved.Count; ++i) {
                    if (start.ContainsKey(moved[i]) == false) start[moved[i]] = saved[i];
                    farthest = Math.Max(farthest, m.GetVertex(moved[i]).Distance(start[moved[i]]));
                }
                accepted++;
                if (now < defects) improving++;
                defects = now; defectTris = nowTris;
                // (local counts: the whole mesh again when the list runs empty or the count reaches 0)
                if (local && (defects <= 0 || defectTris.Count == 0))
                    defects = Defects(m, eps, out defectTris);
            } else
                for (int i = 0; i < moved.Count; ++i)
                    m.SetVertex(moved[i], saved[i]);
        }
        string reasons = gs.MeshRobustRepair.NotCleanReasons(m);
        return "{\"case\":\"" + name + "\",\"ok\":true,\"tris\":" + m.TriangleCount + ",\"drift_used\":" + (r.MaxDrift / r.MaxLength).ToString("F3", ci)
            + ",\"defects_before\":" + before + ",\"defects_after\":" + defects + ",\"clean\":" + (reasons == "" ? "true" : "false")
            + ",\"reasons\":\"" + reasons + "\",\"evaluations\":" + evaluations + ",\"accepted\":" + accepted + ",\"improving\":" + improving
            + ",\"moved_vertices\":" + start.Count + ",\"farthest_of_left\":" + (left > 0 ? farthest / left : 0).ToString("G3", ci)
            + ",\"local\":" + (local ? "true" : "false") + ",\"max_moved\":" + maxMoved + ",\"plateau\":" + (plateau ? "true" : "false") + ",\"seconds\":" + clock.Elapsed.TotalSeconds.ToString("F1", ci) + "}";
    }

    // the defects that involve the triangles of `tris`: ~t for a degenerate triangle t, (a << 32) | b for an intersecting
    // pair a < b (as MeshRepairMetrics.SelfIntersectionPairs: by vertex ids, degenerate triangles left out)
    static HashSet<long> LocalDefects(DMesh3 m, double eps, HashSet<int> tris, TriangleBoxTree tree, List<int> treeTids)
    {
        var found = new HashSet<long>();
        var hits = new List<int>();
        foreach (int t in tris) {
            Index3i tv = m.GetTriangle(t);
            var T = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
            bool degenerate = ExactTriangleIntersection.IsDegenerate(ref T);
            if (m.GetTriArea(t) < eps || degenerate) found.Add(~(long)t);
            if (degenerate) continue;
            var bx = new AxisAlignedBox3d(T.V0);
            bx.Contain(T.V1); bx.Contain(T.V2);
            hits.Clear();
            tree.Query(bx, hits);
            foreach (int i in hits) {
                int u = treeTids[i];
                if (u == t) continue;
                Index3i uv = m.GetTriangle(u);
                var U = new Triangle3d(m.GetVertex(uv.a), m.GetVertex(uv.b), m.GetVertex(uv.c));
                if (ExactTriangleIntersection.IsDegenerate(ref U)) continue;
                if (ExactTriangleIntersection.IntersectsBeyondShared(tv, ref T, uv, ref U))
                    found.Add(((long)Math.Min(t, u) << 32) | (uint)Math.Max(t, u));
            }
        }
        return found;
    }

    static Vector3d Cross(DMesh3 m, int t)
    {
        Index3i tv = m.GetTriangle(t);
        Vector3d p = m.GetVertex(tv.a);
        return (m.GetVertex(tv.b) - p).Cross(m.GetVertex(tv.c) - p);
    }

    // degenerate triangles plus intersecting pairs, and the triangles of both (each once)
    static int Defects(DMesh3 m, double eps, out List<int> tris)
    {
        var set = new HashSet<int>();
        int n = 0;
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            var T = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
            if (m.GetTriArea(t) < eps || ExactTriangleIntersection.IsDegenerate(ref T)) { set.Add(t); n++; }
        }
        foreach (Index2i p in gs.MeshRepairMetrics.SelfIntersectionPairs(m)) { set.Add(p.a); set.Add(p.b); n++; }
        tris = set.ToList();
        tris.Sort();
        return n;
    }
}
