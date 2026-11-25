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
// degenerate area turns over. LATTICE_GRID=<n> draws the steps from a fixed set (26 axis directions, the drift left
// over 2^j, j < n). Above 2000 triangles (or with LATTICE_LOCAL=1) the defects are counted at the moved
// vertices only. Ablations: LATTICE_MAX_MOVED=1 (one vertex at a time), LATTICE_PLATEAU=0 (only moves that
// remove a defect). A clean end shows that such a configuration exists; no clean end shows nothing beyond
// this search.
//
// The plateau rule of the note's section 10 (a prototype of its acceptance rule, not of its order of candidates):
// LATTICE_STRICT=1 keeps a step only if it adds no defect (the defects at the moved vertices after the step are a
// subset of those before; local counts only), so a step with an equal count leaves the set of defects as it was;
// LATTICE_STALL=<B> ends the search after B evaluations without a step that removes a defect and puts the steps since
// the last such step back (the chain is rolled back: the result differs from the start only by chains that removed a
// defect). LATTICE_TARGET=degenerate (with LATTICE_STRICT=1) searches the degenerate triangles only, the defects that the
// rounding leaves on the Thingi10K self-unions: the count that must fall is theirs, and no step may add a defect of
// any kind (contacts of the exact result, which the probe counts as intersecting pairs, are neither searched nor
// counted as removed).
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
        var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { AllowOpenMeshes = true };
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
        // LATTICE_GRID=<n>: candidates from a fixed set (26 axis directions, sizes the drift left / 2^j, j < n) instead of random vectors
        int gridSizes = int.Parse(Environment.GetEnvironmentVariable("LATTICE_GRID") ?? "0");
        bool grid = gridSizes > 0;
        bool strict = Environment.GetEnvironmentVariable("LATTICE_STRICT") == "1";
        bool degenerateOnly = Environment.GetEnvironmentVariable("LATTICE_TARGET") == "degenerate";
        if (degenerateOnly && strict == false)
            throw new ArgumentException("LATTICE_TARGET=degenerate needs LATTICE_STRICT=1");
        int stall = int.Parse(Environment.GetEnvironmentVariable("LATTICE_STALL") ?? "0");
        int sinceImproving = 0, rolledBack = 0, longestChain = 0, chain = 0;
        bool stalled = false;
        var journal = new List<KeyValuePair<int, Vector3d>>();   // the steps since the last one that removed a defect
        int allBefore = Defects(m, eps, out List<int> defectTris, false);
        int before = degenerateOnly ? Defects(m, eps, out defectTris, true) : allBefore, defects = before;
        int evaluations = 0, accepted = 0, improving = 0;
        double farthest = 0;
        var clock = Stopwatch.StartNew();
        double ulp = Math.Pow(2, Math.Floor(Math.Log(Math.Max(gs.MeshRepairMetrics.TriangleBounds(m).Min.MaxAbs, gs.MeshRepairMetrics.TriangleBounds(m).Max.MaxAbs), 2)) - 52);
        double span = Math.Log(Math.Max(left / ulp, 2), 2);
        // large meshes: the defects are counted at the moved vertices only, against a box tree of the triangles' boxes
        // at the start grown by the drift left (no vertex moves farther, so the boxes stay supersets)
        bool local = m.TriangleCount > 2000 || strict || Environment.GetEnvironmentVariable("LATTICE_LOCAL") == "1";
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
                if (grid) {
                    // a fixed candidate set: the 26 directions with components -1, 0, 1, the size the drift left over a power of two
                    dir = new Vector3d(rng.Next(3) - 1, rng.Next(3) - 1, rng.Next(3) - 1);
                    size = left * Math.Pow(2, -rng.Next(gridSizes));
                }
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
            bool addsNone = true;
            if (ok && local) {
                // only the triangles at the moved vertices change: their defects before (positions put back) and after
                var after = moved.Select(v => m.GetVertex(v)).ToArray();
                for (int i = 0; i < moved.Count; ++i) m.SetVertex(moved[i], saved[i]);
                var was = LocalDefects(m, eps, tris, tree, treeTids);
                for (int i = 0; i < moved.Count; ++i) m.SetVertex(moved[i], after[i]);
                var isNow = LocalDefects(m, eps, tris, tree, treeTids);
                addsNone = isNow.IsSubsetOf(was);
                if (degenerateOnly) {
                    was.RemoveWhere(k => k >= 0);
                    isNow.RemoveWhere(k => k >= 0);
                }
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
                now = Defects(m, eps, out nowTris, false);
                evaluations++;
            }
            if (ok) sinceImproving++;
            if (ok && (strict == false || addsNone) && (now < defects || (plateau && now == defects))) {
                for (int i = 0; i < moved.Count; ++i) {
                    if (start.ContainsKey(moved[i]) == false) start[moved[i]] = saved[i];
                    farthest = Math.Max(farthest, m.GetVertex(moved[i]).Distance(start[moved[i]]));
                }
                accepted++;
                if (now < defects) {
                    improving++;
                    longestChain = Math.Max(longestChain, chain);
                    sinceImproving = 0; chain = 0; journal.Clear();
                } else {
                    chain++;
                    for (int i = 0; i < moved.Count; ++i) journal.Add(new KeyValuePair<int, Vector3d>(moved[i], saved[i]));
                }
                defects = now; defectTris = nowTris;
                // (local counts: the whole mesh again when the list runs empty or the count reaches 0)
                if (local && (defects <= 0 || defectTris.Count == 0))
                    defects = Defects(m, eps, out defectTris, degenerateOnly);
            } else
                for (int i = 0; i < moved.Count; ++i)
                    m.SetVertex(moved[i], saved[i]);
            if (stall > 0 && sinceImproving >= stall) { stalled = true; break; }
        }
        // the steps of an unfinished chain are put back, the last first (always with LATTICE_STALL, at a stall or at
        // the time limit)
        if (stall > 0 && defects > 0) {
            for (int i = journal.Count - 1; i >= 0; --i) m.SetVertex(journal[i].Key, journal[i].Value);
            rolledBack = journal.Count;
            defects = Defects(m, eps, out defectTris, degenerateOnly);
        }
        int allAfter = Defects(m, eps, out List<int> unused, false);
        string reasons = gs.MeshRobustRepair.NotCleanReasons(m);
        return "{\"case\":\"" + name + "\",\"ok\":true,\"tris\":" + m.TriangleCount + ",\"drift_used\":" + (r.MaxDrift / r.MaxLength).ToString("F3", ci)
            + ",\"defects_before\":" + before + ",\"defects_after\":" + defects + ",\"clean\":" + (reasons == "" ? "true" : "false")
            + ",\"reasons\":\"" + reasons + "\",\"evaluations\":" + evaluations + ",\"accepted\":" + accepted + ",\"improving\":" + improving
            + ",\"moved_vertices\":" + start.Count + ",\"farthest_of_left\":" + (left > 0 ? farthest / left : 0).ToString("G3", ci)
            + ",\"grid\":" + gridSizes + ",\"local\":" + (local ? "true" : "false") + ",\"max_moved\":" + maxMoved + ",\"plateau\":" + (plateau ? "true" : "false")
            + ",\"limit\":" + (r.EvaluationLimitReached ? "true" : "false") + ",\"left_degenerate\":" + r.RemainingDegenerate + ",\"left_pairs\":" + r.RemainingIntersections + ",\"search_moves\":" + r.PositionSearchMoves
            + ",\"target\":\"" + (degenerateOnly ? "degenerate" : "all") + "\",\"all_defects_before\":" + allBefore + ",\"all_defects_after\":" + allAfter
            + ",\"seed\":" + seed + ",\"strict\":" + (strict ? "true" : "false") + ",\"stall\":" + stall + ",\"stalled\":" + (stalled ? "true" : "false") + ",\"rolled_back\":" + rolledBack + ",\"longest_chain\":" + longestChain + ",\"seconds\":" + clock.Elapsed.TotalSeconds.ToString("F1", ci) + "}";
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
    static int Defects(DMesh3 m, double eps, out List<int> tris, bool degenerateOnly)
    {
        var set = new HashSet<int>();
        int n = 0;
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            var T = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
            if (m.GetTriArea(t) < eps || ExactTriangleIntersection.IsDegenerate(ref T)) { set.Add(t); n++; }
        }
        if (degenerateOnly == false)
            foreach (Index2i p in gs.MeshRepairMetrics.SelfIntersectionPairs(m)) { set.Add(p.a); set.Add(p.b); n++; }
        tris = set.ToList();
        tris.Sort();
        return n;
    }
}
