// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The reverse direction of Reducer.MaxDeviationTwoSided: every point of the input surface stays within eps of the
    /// reduced mesh (docs/design/BOUNDED_SIMPLIFICATION.md, option O1).
    ///
    /// Every input triangle T keeps a small set D(T) of current output triangle ids with the invariant
    ///   (I) every point of T is within eps of the union of the triangles D(T), with their current geometry.
    /// At the start the output is the input (same ids), D(T) = {T}. An edge collapse (a, b) changes only the triangles of
    /// the one-rings of a and b (R): two are removed, the others keep their ids and get the new vertex. So (I) can only
    /// break for the inputs that depend on a triangle of R; each of them is certified again against the new ring plus
    /// the members of its set outside R (all current output triangles after the collapse), and the collapse is refused if
    /// one cannot be. Since the distance to a subset of the output is at least the distance to the output, (I) gives
    /// h(input, output) &lt;= eps after every collapse (induction over the collapses; Klein, Liebich and Strasser,
    /// "Mesh reduction with error control", IEEE Visualization 1996, keep the same kind of correspondence).
    ///
    /// The certificate of one triangle against a candidate list follows the cascading bounds of Sacht and Jacobson
    /// (SGP 2024), as TriangleDeviationBounds does against a tree: the distance to one triangle is convex, so its maximum
    /// over a triangle is at a corner; otherwise the largest corner distance plus the covering radius; otherwise split
    /// into 4, up to maxDepth levels; a corner farther than eps from every candidate refuses. Floating point, like
    /// IsWithin (no explicit rounding term).
    ///
    /// Use: Check() before a collapse (no state is changed), Commit() right after the collapse that Check() accepted,
    /// with the same arguments and nothing else changed in between. Not thread-safe; Check() runs the per-input
    /// certificates through gParallel (each one independent and deterministic, so parallel equals serial).
    /// </summary>
    internal sealed class ReducerReverseCoverage
    {
        readonly DMesh3 input;
        internal readonly double Eps;
        readonly int maxDepth;
        readonly Vector3d[] inputCorners;          // 3 per input triangle id
        readonly int[][] deps;                     // per input triangle id: the output triangle ids of D(T), sorted
        readonly HashSet<int>[] dependents;        // per output triangle id: the input triangles whose D(T) holds it

        /// <summary> the mesh's ShapeTimestamp after the last change this coverage saw (start or Commit) </summary>
        internal int Stamp;
        /// <summary> (tests, benchmarks) input triangles certified, collapses refused, collapses committed </summary>
        internal long Certified, Refused, Committed;

        // the pending collapse of the last accepted Check()
        int pendA = -1, pendB = -1;
        readonly List<int> ring = new List<int>();             // R: the triangles at a or b, in iteration order
        readonly HashSet<int> ringSet = new HashSet<int>();
        readonly List<int> newIds = new List<int>();           // R without the two removed triangles
        readonly List<Triangle3d> newTris = new List<Triangle3d>();
        readonly List<int> affected = new List<int>();
        int[][] newDeps = new int[0][];

        /// <summary>
        /// reference: a copy of mesh with the same triangle ids and positions (the deviation reference taken from the
        /// mesh at its first reduction). False if they differ: the start D(T) = {T} needs them equal.
        /// </summary>
        internal static bool TryCreate(DMesh3 reference, DMesh3 mesh, double eps, int maxDepth, out ReducerReverseCoverage coverage)
        {
            coverage = null;
            if (!(eps > 0) || reference.MaxTriangleID != mesh.MaxTriangleID || reference.TriangleCount != mesh.TriangleCount)
                return false;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero, x = Vector3d.Zero, y = Vector3d.Zero, z = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                if (!reference.IsTriangle(t))
                    return false;
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                reference.GetTriVertices(t, ref x, ref y, ref z);
                if (a != x || b != y || c != z)
                    return false;
            }
            coverage = new ReducerReverseCoverage(reference, mesh, eps, maxDepth);
            return true;
        }

        ReducerReverseCoverage(DMesh3 reference, DMesh3 mesh, double eps, int maxDepth)
        {
            input = reference;
            Eps = eps;
            this.maxDepth = maxDepth;
            int n = reference.MaxTriangleID;
            inputCorners = new Vector3d[3 * n];
            deps = new int[n][];
            dependents = new HashSet<int>[n];
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in reference.TriangleIndices()) {
                reference.GetTriVertices(t, ref a, ref b, ref c);
                inputCorners[3 * t] = a; inputCorners[3 * t + 1] = b; inputCorners[3 * t + 2] = c;
                deps[t] = new int[] { t };
                dependents[t] = new HashSet<int> { t };
            }
            Stamp = mesh.ShapeTimestamp;
        }

        /// <summary> (tests) D(T) of an input triangle (null for an id that is not one) </summary>
        internal int[] DependenciesOf(int inputTri) { return (inputTri >= 0 && inputTri < deps.Length) ? deps[inputTri] : null; }

        /// <summary>
        /// would every input triangle stay within Eps of the mesh after collapsing edge (a, b) with the triangles t0, t1
        /// (t1 may be InvalidID) to newPos? Changes nothing but the pending state that Commit() applies.
        /// </summary>
        internal bool Check(DMesh3 mesh, int a, int b, int t0, int t1, Vector3d newPos)
        {
            pendA = pendB = -1;
            if (mesh.ShapeTimestamp != Stamp)
                throw new InvalidOperationException("Reducer.MaxDeviationTwoSided: the mesh was changed outside the reduction's collapses");
            ring.Clear(); ringSet.Clear(); newIds.Clear(); newTris.Clear(); affected.Clear();
            foreach (int v in new int[] { a, b })
                foreach (int t in mesh.VtxTrianglesItr(v))
                    if (ringSet.Add(t)) ring.Add(t);
            foreach (int t in ring) {
                if (t == t0 || t == t1)
                    continue;
                Index3i tv = mesh.GetTriangle(t);
                newIds.Add(t);
                newTris.Add(new Triangle3d(corner(mesh, tv.a, a, b, newPos), corner(mesh, tv.b, a, b, newPos), corner(mesh, tv.c, a, b, newPos)));
            }
            var seen = new HashSet<int>();
            foreach (int t in ring) {
                HashSet<int> s = (t < dependents.Length) ? dependents[t] : null;
                if (s == null) continue;
                foreach (int T in s)
                    if (seen.Add(T)) affected.Add(T);
            }
            affected.Sort();
            int count = affected.Count;
            if (newDeps.Length < count)
                newDeps = new int[Math.Max(count, 2 * newDeps.Length)][];
            bool ok = true;
            Action<int> certify = (k) => {
                if (!ok) return;
                int T = affected[k];
                // the candidates: the new ring, then the members of D(T) outside R (current, unchanged by the collapse)
                var ids = new List<int>(newIds.Count + deps[T].Length);
                var tris = new List<Triangle3d>(newIds.Count + deps[T].Length);
                ids.AddRange(newIds); tris.AddRange(newTris);
                Vector3d x = Vector3d.Zero, y = Vector3d.Zero, z = Vector3d.Zero;
                foreach (int d in deps[T]) {
                    if (ringSet.Contains(d)) continue;
                    mesh.GetTriVertices(d, ref x, ref y, ref z);
                    ids.Add(d); tris.Add(new Triangle3d(x, y, z));
                }
                var used = new List<int>(4);
                if (Within(inputCorners[3 * T], inputCorners[3 * T + 1], inputCorners[3 * T + 2], tris, Eps, maxDepth, used) == false) {
                    ok = false;
                    return;
                }
                var u = new int[used.Count];
                for (int i = 0; i < u.Length; ++i) u[i] = ids[used[i]];
                Array.Sort(u);
                newDeps[k] = dedup(u);
            };
            if (count >= 8)
                gParallel.ForRange(count, (k) => certify(k));
            else
                for (int k = 0; k < count && ok; ++k) certify(k);
            Certified += count;
            if (!ok) {
                Refused++;
                return false;
            }
            pendA = a; pendB = b;
            return true;
        }

        /// <summary> apply the sets of the last accepted Check(a, b, ...), after the mesh made that collapse </summary>
        internal void Commit(DMesh3 mesh, int a, int b)
        {
            if (pendA != a || pendB != b)
                throw new InvalidOperationException("ReducerReverseCoverage.Commit without a matching accepted Check");
            for (int k = 0; k < affected.Count; ++k) {
                int T = affected[k];
                foreach (int d in deps[T])
                    if (!ringSet.Contains(d)) dependents[d].Remove(T);
            }
            // every input that depended on a triangle of R is in affected: the entries of R are rebuilt from scratch
            foreach (int t in ring)
                if (t < dependents.Length) dependents[t] = null;
            for (int k = 0; k < affected.Count; ++k) {
                int T = affected[k];
                deps[T] = newDeps[k];
                newDeps[k] = null;
                foreach (int d in deps[T]) {
                    if (dependents[d] == null) dependents[d] = new HashSet<int>();
                    dependents[d].Add(T);
                }
            }
            pendA = pendB = -1;
            Committed++;
            Stamp = mesh.ShapeTimestamp;
        }

        static Vector3d corner(DMesh3 mesh, int v, int a, int b, Vector3d newPos)
        {
            return (v == a || v == b) ? newPos : mesh.GetVertex(v);
        }

        static int[] dedup(int[] sorted)
        {
            int n = 0;
            for (int i = 0; i < sorted.Length; ++i)
                if (i == 0 || sorted[i] != sorted[i - 1]) sorted[n++] = sorted[i];
            if (n == sorted.Length) return sorted;
            var r = new int[n];
            Array.Copy(sorted, r, n);
            return r;
        }

        /// <summary>
        /// is every point of the triangle (p0, p1, p2) within eps of the union of tris? Conservative (false if that cannot
        /// be shown with maxDepth levels of subdivision). used gets the indices into tris of the triangles the
        /// certificate relied on (the triangle is within eps of their union), possibly with repeats.
        /// </summary>
        internal static bool Within(Vector3d p0, Vector3d p1, Vector3d p2, List<Triangle3d> tris, double eps, int maxDepth, List<int> used)
        {
            int n = tris.Count;
            if (n == 0) return false;
            double[] d0 = distances(p0, tris), d1 = distances(p1, tris), d2 = distances(p2, tris);
            return within(p0, p1, p2, d0, d1, d2, tris, eps, maxDepth, used);
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

        static bool within(Vector3d p0, Vector3d p1, Vector3d p2, double[] d0, double[] d1, double[] d2,
            List<Triangle3d> tris, double eps, int depthLeft, List<int> used)
        {
            int n = tris.Count;
            int i0 = argmin(d0), i1 = argmin(d1), i2 = argmin(d2);
            // (a NaN distance is never accepted: every test below is false for it)
            if (!(d0[i0] <= eps) || !(d1[i1] <= eps) || !(d2[i2] <= eps))
                return false;
            // the distance to one triangle is convex: its maximum over the triangle is at a corner
            for (int k = 0; k < n; ++k) {
                if (Math.Max(d0[k], Math.Max(d1[k], d2[k])) <= eps) {
                    used.Add(k);
                    return true;
                }
            }
            // every point is within the covering radius of a corner, and each corner within its distance of its nearest
            double e01 = p0.Distance(p1), e12 = p1.Distance(p2), e20 = p2.Distance(p0);
            double dmax = Math.Max(d0[i0], Math.Max(d1[i1], d2[i2]));
            if (dmax + TriangleDeviationBounds.covering_radius(e01, e12, e20) <= eps) {
                used.Add(i0); used.Add(i1); used.Add(i2);
                return true;
            }
            if (depthLeft <= 0)
                return false;
            Vector3d m01 = 0.5 * (p0 + p1), m12 = 0.5 * (p1 + p2), m20 = 0.5 * (p2 + p0);
            double[] e0 = distances(m01, tris), e1 = distances(m12, tris), e2 = distances(m20, tris);
            return within(p0, m01, m20, d0, e0, e2, tris, eps, depthLeft - 1, used)
                && within(m01, p1, m12, e0, d1, e1, tris, eps, depthLeft - 1, used)
                && within(m20, m12, p2, e2, e1, d2, tris, eps, depthLeft - 1, used)
                && within(m01, m12, m20, e0, e1, e2, tris, eps, depthLeft - 1, used);
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
