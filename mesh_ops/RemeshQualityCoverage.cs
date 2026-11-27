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
        /// <summary> (tests) certificates made and operations refused </summary>
        internal long Certified, Refused;

        // the pending operation of the last accepted Check
        readonly List<int> affected = new List<int>();
        readonly List<int[]> newDeps = new List<int[]>();
        readonly List<bool> usesNew = new List<bool>();
        HashSet<int> pendingRemoved;

        /// <summary>
        /// grid: the mesh's triangles by their boxes (the self-intersection grid of the pass); candidates of T are the
        /// mesh triangles whose box meets T's box grown by eps
        /// </summary>
        internal RemeshQualityCoverage(DMesh3 reference, DMesh3 mesh, TriangleHashGrid grid, double eps, int maxDepth)
        {
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
                if (ids.Count == 0 || !ReducerReverseCoverage.Within(a, b, c, tris, eps, maxDepth, used)) {
                    Uncovered++;
                    continue;
                }
                deps[t] = to_ids(used, ids, null, out bool _);
                foreach (int m in deps[t])
                    dependents_of(m, true).Add(t);
            }
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
                if (!ReducerReverseCoverage.Within(corners[3 * T], corners[3 * T + 1], corners[3 * T + 2], tris, Eps, maxDepth, used)) {
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
    }
}
