// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The exact building blocks of the determinism proof stage 1 (docs/design/DETERMINISM_PROOF.md section 5.4): SnapRounding2 (hash
// containers keyed by value tuples, whose hashes are seeded per process on .NET), ExactTriangulation2 and TriangleBoxTree (the
// parallel build above its threshold), on procedural inputs from a splitmix64 stream. ExactPlanarSimplify runs inside the
// boolean.* cases. A hash seed or a schedule that reached a result would show as a different trace between two processes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;

namespace DeterminismHarness
{
    static class Stage1
    {
        public static void AddTo(List<Case> list)
        {
            foreach (int seed in new[] { 1, 2, 3 })
                foreach (double tol in new[] { 0.5, 0.125 }) {
                    int s = seed; double t = tol;
                    list.Add(new Case("stage1.snaprounding." + seed + "." + tol, serial => SnapRounding(s, t)));
                }
            foreach (int seed in new[] { 1, 2 })
                foreach (int n in new[] { 20, 400 }) {
                    int s = seed, c = n;
                    list.Add(new Case("stage1.triangulation2." + seed + "." + n, serial => Triangulation(s, c)));
                }
            list.Add(new Case("stage1.boxtree.parallel", serial => BoxTree(1, 6000, false)));
            list.Add(new Case("stage1.boxtree.ties", serial => BoxTree(2, 6000, true)));
        }

        static void AddSnap(Trace t, SnapRounding2 s)
        {
            t.Section("points");
            t.Add(s.InputCount);
            foreach (Vector2d p in s.Result.Points) t.Add(p);
            t.Section("segments");
            foreach (Mesh2Segment e in s.Result.Segments) { t.Add(e.A); t.Add(e.B); }
            t.Section("counts");
            t.Add(s.Rounds); t.Add(s.Crossings); t.Add(s.Spread); t.Add(s.Widened); t.Add(s.ForcedSpread); t.Add(s.CollapsedSegments); t.Add(s.MaxDisplacement);
        }

        static Trace SnapRounding(int seed, double tol)
        {
            var rng = new Rng((ulong)seed);
            var input = new ConstrainedMesh2Input();
            int n = 60;
            for (int i = 0; i < n; ++i)
                input.Points.Add(new Vector2d(Math.Floor(rng.Range(0, 64)) / 8.0, Math.Floor(rng.Range(0, 64)) / 8.0));
            for (int k = 0; k < 45; ++k) {
                int a = (int)(rng.Next() % (ulong)n), b = (int)(rng.Next() % (ulong)n);
                if (a != b) input.Segments.Add(new Mesh2Segment(a, b));
            }
            var t = new Trace();
            AddSnap(t, SnapRounding2.Apply(input, tol));
            // the same input with the segments in the opposite order: another insertion order into every hash container
            var rev = new ConstrainedMesh2Input();
            rev.Points.AddRange(input.Points);
            for (int k = input.Segments.Count - 1; k >= 0; --k) rev.Segments.Add(input.Segments[k]);
            AddSnap(t, SnapRounding2.Apply(rev, tol));
            return t;
        }

        static Trace Triangulation(int seed, int count)
        {
            var rng = new Rng((ulong)(100 + seed));
            int n = count > 50 ? 1000 : 12;
            var pts = new List<(long x, long y)> { (0, 0), (n, 0), (0, n) };
            var seen = new HashSet<(long, long)>(pts);
            var inner = new List<int>();
            while (inner.Count < count) {
                long x = (long)(rng.Next() % (ulong)(n + 1)), y = (long)(rng.Next() % (ulong)(n + 1 - x));
                if (seen.Add((x, y))) { pts.Add((x, y)); inner.Add(pts.Count - 1); }
            }
            Func<int, int, int, int> orient = (a, b, c) => Math.Sign((pts[b].x - pts[a].x) * (pts[c].y - pts[a].y) - (pts[b].y - pts[a].y) * (pts[c].x - pts[a].x));
            Func<int, int, int, int, int> incircle = (a, b, c, d) => {
                BigInteger[] r = new[] { a, b, c }.SelectMany(i => {
                    BigInteger x = pts[i].x - pts[d].x, y = pts[i].y - pts[d].y;
                    return new[] { x, y, x * x + y * y };
                }).ToArray();
                return (r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6])).Sign;
            };
            var tr = new ExactTriangulation2(0, 1, 2, orient, incircle);
            foreach (int p in inner) tr.InsertPoint(p);
            for (int k = 0; k + 1 < inner.Count; k += 2) {
                try { tr.InsertConstraint(inner[k], inner[k + 1]); } catch (InvalidOperationException) { }
            }
            tr.MakeDelaunay();
            var t = new Trace();
            t.Section("triangles");
            foreach (Index3i f in tr.Triangles()) { t.Add(f.a); t.Add(f.b); t.Add(f.c); }
            return t;
        }

        static Trace BoxTree(int seed, int n, bool ties)
        {
            var rng = new Rng((ulong)(200 + seed));
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                // ties: boxes on an 8 x 8 x 8 lattice (many equal Morton codes and equal centres)
                Vector3d lo = ties ? new Vector3d((int)(rng.Next() % 8), (int)(rng.Next() % 8), (int)(rng.Next() % 8))
                    : new Vector3d(rng.Double(), rng.Double(), rng.Double());
                Vector3d size = ties ? new Vector3d(1, 1, 1) : new Vector3d(1e-2, 1e-2, 1e-2) * rng.Double();
                boxes.Add(new AxisAlignedBox3d(lo, lo + size));
            }
            int saved = TriangleBoxTree.ParallelBuildFrom;
            TriangleBoxTree.ParallelBuildFrom = 4500;      // above the median split's limit: the Morton order, and the parallel build
            try {
                var tree = new TriangleBoxTree(boxes);
                var t = new Trace();
                t.Section("queries");
                var hits = new List<int>();
                for (int q = 0; q < 200; ++q) {
                    Vector3d c = ties ? new Vector3d(rng.Range(0, 9), rng.Range(0, 9), rng.Range(0, 9)) : new Vector3d(rng.Double(), rng.Double(), rng.Double());
                    hits.Clear();
                    tree.Query(new AxisAlignedBox3d(c, c + new Vector3d(0.05, 0.05, 0.05) * (ties ? 10 : 1)), hits);
                    t.Add(hits.Count);
                    foreach (int h in hits) t.Add(h);
                }
                t.Section("pairs");
                if (!ties) {
                    // (the lattice has about n^2 / 512 overlapping pairs: too many)
                    var pairs = new List<Index2i>();
                    foreach (Index2i task in tree.SelfPairTasks(16)) tree.SelfPairs(task, pairs);
                    t.Add(pairs.Count);
                    foreach (Index2i p in pairs) { t.Add(p.a); t.Add(p.b); }
                }
                return t;
            } finally { TriangleBoxTree.ParallelBuildFrom = saved; }
        }
    }
}
