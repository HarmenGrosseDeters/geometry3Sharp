// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The reference-to-mesh direction of RemeshQualityPass.TwoSided: every point of a reference triangle stays within
    /// eps of the mesh while the pass changes it. The invariant and the certificate are those of ReducerReverseCoverage
    /// (Klein, Liebich and Strasser 1996; the cascade of Sacht and Jacobson 2024): every covered reference triangle T keeps
    /// a set D(T) of current mesh triangle ids with
    ///   (I) every point of T is within eps of the union of the triangles of D(T), with their current geometry.
    /// Two differences: the start is not the identity (the pass starts from a remesh of the reference, with other
    /// triangles), so D(T) is found by certifying T against the mesh triangles near it, and T is left out (counted in
    /// Uncovered) if that fails; and an operation is any local change that removes a set R of triangles and adds new
    /// ones (a flip, split, collapse or vertex move), not only a collapse. Every other triangle keeps its id and its
    /// geometry, so (I) can only break for the T whose D(T) meets R; each is certified again against the new triangles
    /// plus its members outside R. Distance to a subset is at least the distance to the mesh, so (I) gives
    /// h(T, mesh) &lt;= eps for every covered T after every operation. Floating point, like IsWithin. Serial.
    /// </summary>
    internal sealed class RemeshQualityCoverage
    {
        internal readonly double Eps;
        readonly int maxDepth;
        readonly Vector3d[] corners;               // 3 per reference triangle id
        readonly int[][] deps;                     // per reference triangle id: D(T), null if T is not covered
        readonly List<HashSet<int>> dependents = new List<HashSet<int>>();   // per mesh triangle id: the T with it in D(T)

        /// <summary> reference triangles that could not be certified at the start (left out of the invariant) </summary>
        internal int Uncovered;
        /// <summary> sub-triangles per certificate at most (a cost bound; beyond it the certificate refuses) </summary>
        internal readonly int MaxPieces;
        /// <summary> (tests) certificates made and operations refused </summary>
        internal long Certified, Refused;
        /// <summary> (benchmarks) seconds in the start certificates and in Check </summary>
        internal double InitSeconds, CheckSeconds;

        // the pending operation of the last accepted Check
        readonly List<int> affected = new List<int>();
        readonly List<int[]> newDeps = new List<int[]>();
        readonly List<bool> usesNew = new List<bool>();
        HashSet<int> pendingRemoved;

        /// <summary>
        /// grid: the mesh's triangles by their boxes (the self-intersection grid of the pass); candidates of T are the
        /// mesh triangles whose box meets T's box grown by eps
        /// </summary>
        internal RemeshQualityCoverage(DMesh3 reference, DMesh3 mesh, TriangleHashGrid grid, double eps, int maxDepth, int maxPieces = 256)
        {
            MaxPieces = maxPieces;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Eps = eps;
            this.maxDepth = maxDepth;
            int n = reference.MaxTriangleID;
            corners = new Vector3d[3 * n];
            deps = new int[n][];
            var ids = new List<int>();
            var tris = new List<Triangle3d>();
            var used = new List<int>();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero, x = Vector3d.Zero, y = Vector3d.Zero, z = Vector3d.Zero;
            foreach (int t in reference.TriangleIndices()) {
                reference.GetTriVertices(t, ref a, ref b, ref c);
                corners[3 * t] = a; corners[3 * t + 1] = b; corners[3 * t + 2] = c;
                var bx = new AxisAlignedBox3d(a); bx.Contain(b); bx.Contain(c);
                bx.Expand(eps);
                ids.Clear(); tris.Clear(); used.Clear();
                grid.Query(bx, (m) => ids.Add(m));
                ids.Sort();     // (the grid's order is not part of the answer: sorted, deterministic)
                foreach (int m in ids) {
                    mesh.GetTriVertices(m, ref x, ref y, ref z);
                    tris.Add(new Triangle3d(x, y, z));
                }
                Certified++;
                if (ids.Count == 0 || !Within(a, b, c, tris, eps, maxDepth, MaxPieces, used)) {
                    Uncovered++;
                    continue;
                }
                deps[t] = to_ids(used, ids, null, out bool _);
                foreach (int m in deps[t])
                    dependents_of(m, true).Add(t);
            }
            InitSeconds = sw.Elapsed.TotalSeconds;
        }

        HashSet<int> dependents_of(int m, bool create)
        {
            while (create && dependents.Count <= m) dependents.Add(null);
            if (m >= dependents.Count) return null;
            if (create && dependents[m] == null) dependents[m] = new HashSet<int>();
            return dependents[m];
        }

        // the sorted distinct ids of `used` (indices into the candidates: old ids first, then the new triangles)
        static int[] to_ids(List<int> used, List<int> oldIds, List<Triangle3d> newTris, out bool anyNew)
        {
            anyNew = false;
            var s = new SortedSet<int>();
            foreach (int k in used) {
                if (k < oldIds.Count) s.Add(oldIds[k]);
                else anyNew = true;
            }
            var r = new int[s.Count];
            s.CopyTo(r);
            return r;
        }

        /// <summary> (tests) D(T), null if T is not covered </summary>
        internal int[] DependenciesOf(int refTri) { return (refTri >= 0 && refTri < deps.Length) ? deps[refTri] : null; }

        /// <summary>
        /// would every covered reference triangle stay within Eps of the mesh if the triangles `removed` were replaced by
        /// `newTris` (every other triangle unchanged)? Changes nothing but the pending state that Commit applies.
        /// </summary>
        internal bool Check(DMesh3 mesh, HashSet<int> removed, List<Triangle3d> newTris)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { return check(mesh, removed, newTris); } finally { CheckSeconds += sw.Elapsed.TotalSeconds; }
        }

        bool check(DMesh3 mesh, HashSet<int> removed, List<Triangle3d> newTris)
        {
            pendingRemoved = null;
            affected.Clear(); newDeps.Clear(); usesNew.Clear();
            var seen = new HashSet<int>();
            foreach (int r in removed) {
                HashSet<int> s = dependents_of(r, false);
                if (s == null) continue;
                foreach (int T in s)
                    if (seen.Add(T)) affected.Add(T);
            }
            affected.Sort();
            var oldIds = new List<int>();
            var tris = new List<Triangle3d>();
            var used = new List<int>();
            Vector3d x = Vector3d.Zero, y = Vector3d.Zero, z = Vector3d.Zero;
            foreach (int T in affected) {
                oldIds.Clear(); tris.Clear(); used.Clear();
                foreach (int d in deps[T]) {
                    if (removed.Contains(d)) continue;
                    mesh.GetTriVertices(d, ref x, ref y, ref z);
                    oldIds.Add(d); tris.Add(new Triangle3d(x, y, z));
                }
                tris.AddRange(newTris);
                Certified++;
                if (!Within(corners[3 * T], corners[3 * T + 1], corners[3 * T + 2], tris, Eps, maxDepth, MaxPieces, used)) {
                    Refused++;
                    affected.Clear(); newDeps.Clear(); usesNew.Clear();
                    return false;
                }
                newDeps.Add(to_ids(used, oldIds, newTris, out bool anyNew));
                usesNew.Add(anyNew);
            }
            pendingRemoved = removed;
            return true;
        }

        /// <summary>
        /// apply the last accepted Check, after the mesh made that operation: removed as given to Check, added the ids of
        /// the new triangles (with the geometry Check saw)
        /// </summary>
        internal void Commit(HashSet<int> removed, IEnumerable<int> added)
        {
            if (pendingRemoved == null || !ReferenceEquals(pendingRemoved, removed))
                throw new InvalidOperationException("RemeshQualityCoverage.Commit without a matching accepted Check");
            var addedIds = new List<int>(added);
            addedIds.Sort();
            for (int k = 0; k < affected.Count; ++k) {
                int T = affected[k];
                foreach (int d in deps[T]) {
                    HashSet<int> s = dependents_of(d, false);
                    if (s != null) s.Remove(T);
                }
            }
            // every T that depended on a triangle of R is in affected: the entries of R start empty (a reused id is new)
            foreach (int r in removed) {
                HashSet<int> s = dependents_of(r, false);
                if (s != null) s.Clear();
            }
            for (int k = 0; k < affected.Count; ++k) {
                int T = affected[k];
                int[] d = newDeps[k];
                if (usesNew[k]) {
                    // (the certificate used some of the new triangles: all of them join D(T), a superset keeps (I))
                    var s = new SortedSet<int>(d);
                    foreach (int a in addedIds) s.Add(a);
                    d = new int[s.Count];
                    s.CopyTo(d);
                }
                deps[T] = d;
                foreach (int m in d)
                    dependents_of(m, true).Add(T);
            }
            affected.Clear(); newDeps.Clear(); usesNew.Clear();
            pendingRemoved = null;
        }

        /// <summary>
        /// is every point of the triangle (p0, p1, p2) within eps of the union of tris? The certificate of
        /// ReducerReverseCoverage.Within (a triangle is accepted if one candidate holds all its corners within eps, the
        /// distance to one triangle being convex; or if its largest corner distance plus its covering radius is within eps;
        /// else it is split into 4, up to maxDepth levels; a corner farther than eps from every candidate refuses), visited
        /// breadth first: every sub-triangle of a level is tested before any of the next, so a corner outside the band is
        /// found at the coarsest level that has one, not after the covered parts were refined to the bottom. The answer is
        /// the same as the depth-first one (both accept exactly when every leaf of the same subdivision is accepted, and
        /// refuse otherwise); used gets the indices into tris the certificate relied on, possibly with repeats.
        /// </summary>
        internal static bool Within(Vector3d p0, Vector3d p1, Vector3d p2, List<Triangle3d> tris, double eps, int maxDepth, List<int> used)
        {
            return Within(p0, p1, p2, tris, eps, maxDepth, int.MaxValue, used);
        }

        /// <summary> Within with at most maxPieces sub-triangles in all (refused beyond: conservative, a cost bound) </summary>
        internal static bool Within(Vector3d p0, Vector3d p1, Vector3d p2, List<Triangle3d> tris, double eps, int maxDepth, int maxPieces, List<int> used)
        {
            int pieces = 1;
            int n = tris.Count;
            if (n == 0) return false;
            var level = new List<(Vector3d a, Vector3d b, Vector3d c, double[] da, double[] db, double[] dc)> {
                (p0, p1, p2, distances(p0, tris), distances(p1, tris), distances(p2, tris)) };
            var next = new List<(Vector3d a, Vector3d b, Vector3d c, double[] da, double[] db, double[] dc)>();
            var mids = new Dictionary<Vector3d, double[]>();
            for (int depth = 0; ; ++depth) {
                // the corners first: any one outside the band refuses
                foreach (var s in level)
                    if (!(min(s.da) <= eps) || !(min(s.db) <= eps) || !(min(s.dc) <= eps))
                        return false;
                next.Clear();
                foreach (var s in level) {
                    int k = one_holds_all(s.da, s.db, s.dc, eps);
                    if (k >= 0) { used.Add(k); continue; }
                    int i0 = argmin(s.da), i1 = argmin(s.db), i2 = argmin(s.dc);
                    double dmax = Math.Max(s.da[i0], Math.Max(s.db[i1], s.dc[i2]));
                    if (dmax + TriangleDeviationBounds.covering_radius(s.a.Distance(s.b), s.b.Distance(s.c), s.c.Distance(s.a)) <= eps) {
                        used.Add(i0); used.Add(i1); used.Add(i2);
                        continue;
                    }
                    if (depth >= maxDepth || (pieces += 4) > maxPieces) return false;
                    Vector3d m01 = 0.5 * (s.a + s.b), m12 = 0.5 * (s.b + s.c), m20 = 0.5 * (s.c + s.a);
                    double[] e0 = mid(m01, tris, mids), e1 = mid(m12, tris, mids), e2 = mid(m20, tris, mids);
                    next.Add((s.a, m01, m20, s.da, e0, e2));
                    next.Add((m01, s.b, m12, e0, s.db, e1));
                    next.Add((m20, m12, s.c, e2, e1, s.dc));
                    next.Add((m01, m12, m20, e0, e1, e2));
                }
                if (next.Count == 0) return true;
                var t = level; level = next; next = t;
                mids.Clear();
            }
        }

        // (an edge midpoint shared by two sub-triangles of one level: its distances once)
        static double[] mid(Vector3d p, List<Triangle3d> tris, Dictionary<Vector3d, double[]> cache)
        {
            double[] d;
            if (!cache.TryGetValue(p, out d)) cache[p] = d = distances(p, tris);
            return d;
        }

        static int one_holds_all(double[] d0, double[] d1, double[] d2, double eps)
        {
            for (int k = 0; k < d0.Length; ++k)
                if (Math.Max(d0[k], Math.Max(d1[k], d2[k])) <= eps) return k;
            return -1;
        }

        static double[] distances(Vector3d p, List<Triangle3d> tris)
        {
            var d = new double[tris.Count];
            for (int k = 0; k < d.Length; ++k) {
                Triangle3d t = tris[k];
                d[k] = Math.Sqrt(DistPoint3Triangle3.DistanceSqr(ref p, ref t, out Vector3d _, out Vector3d _));
            }
            return d;
        }

        static double min(double[] d)
        {
            double m = double.PositiveInfinity;
            for (int k = 0; k < d.Length; ++k) if (!(d[k] >= m)) m = d[k];
            return m;
        }

        static int argmin(double[] d)
        {
            int best = 0;
            for (int k = 1; k < d.Length; ++k)
                if (d[k] < d[best]) best = k;
            return best;
        }
    }
}
