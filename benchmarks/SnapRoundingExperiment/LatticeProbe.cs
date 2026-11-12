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
// degenerate area turns over. Ablations: LATTICE_MAX_MOVED=1 (one vertex at a time), LATTICE_PLATEAU=0 (only moves that
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
            if (ok) {
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
            } else
                for (int i = 0; i < moved.Count; ++i)
                    m.SetVertex(moved[i], saved[i]);
        }
        string reasons = gs.MeshRobustRepair.NotCleanReasons(m);
        return "{\"case\":\"" + name + "\",\"ok\":true,\"tris\":" + m.TriangleCount + ",\"drift_used\":" + (r.MaxDrift / r.MaxLength).ToString("F3", ci)
            + ",\"defects_before\":" + before + ",\"defects_after\":" + defects + ",\"clean\":" + (reasons == "" ? "true" : "false")
            + ",\"reasons\":\"" + reasons + "\",\"evaluations\":" + evaluations + ",\"accepted\":" + accepted + ",\"improving\":" + improving
            + ",\"moved_vertices\":" + start.Count + ",\"farthest_of_left\":" + (left > 0 ? farthest / left : 0).ToString("G3", ci)
            + ",\"max_moved\":" + maxMoved + ",\"plateau\":" + (plateau ? "true" : "false") + ",\"seconds\":" + clock.Elapsed.TotalSeconds.ToString("F1", ci) + "}";
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
