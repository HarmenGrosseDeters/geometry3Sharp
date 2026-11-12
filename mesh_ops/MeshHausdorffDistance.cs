// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Certified (Pompeiu-)Hausdorff distance between two triangle meshes (or soups): computes an interval
    /// [LowerBound, UpperBound] that contains the exact distance, with UpperBound - LowerBound below the
    /// tolerance. Sampling vertices or edge points only gives a lower bound, which can be far too small
    /// when the farthest point lies inside a triangle (eg over a hole or dip of the other mesh).
    ///
    /// Branch and bound after Sacht and Jacobson, "Cascading upper bounds for triangle soup
    /// Pompeiu-Hausdorff distance" (SGP 2024):
    ///   - lower bound: distances of points of A (vertices, then subdivision points) to B;
    ///   - upper bound per triangle of A, the smallest of
    ///       * for any triangle t of B, max over the corners of d(corner, t) (the distance to one triangle
    ///         is convex, so its maximum over a triangle is at a corner). Tried for the nearest triangles
    ///         of the corners, it is exact when all corners project to the same triangle;
    ///       * d(corner) + longest edge at that corner (triangle inequality);
    ///       * max d(corner) + covering radius (every point is that close to some corner);
    ///   - the triangle with the largest upper bound is split into 4 until the largest upper bound is
    ///     within the tolerance of the lower bound; triangles whose upper bound is below the lower bound
    ///     are dropped.
    /// The symmetric distance is the max of both directions; the second direction is pruned against the
    /// first one's lower bound.
    ///
    /// Usage: <c>var hd = new MeshHausdorffDistance(meshA, meshB); if (hd.Compute()) use(hd.UpperBound);</c>
    /// The meshes are not modified. Distances are in mesh units; the meshes must be in the same coordinate space.
    /// The value is 0 for identical meshes and, for two concentric spheres of radii r1 &lt; r2, about r2 - r1.
    /// </summary>
    public class MeshHausdorffDistance
    {
        /// <summary> input meshes (not modified). The one-sided distance is from A to B. </summary>
        public DMesh3 MeshA, MeshB;
        /// <summary> optional built spatial data structures for MeshA / MeshB (built by Compute() if null; SpatialA only if Symmetric) </summary>
        public DMeshAABBTree3 SpatialA, SpatialB;

        /// <summary> symmetric distance max(h(A,B), h(B,A)); false = one-sided h(A,B) (A to B) </summary>
        public bool Symmetric = true;
        /// <summary> absolute tolerance for UpperBound - LowerBound; if 0, RelativeTolerance * bounding box diagonal </summary>
        public double Tolerance = 0;
        /// <summary> tolerance relative to the diagonal of the combined bounding box, used if Tolerance is 0 </summary>
        public double RelativeTolerance = 1e-4;
        /// <summary> stop (Converged = false) after this many triangle subdivisions (checked per batch of 256: up to 255 more per direction) </summary>
        public int MaxSubdivisions = 20000000;

        /// <summary>
        /// also use the patch bound of TriangleDeviationBounds (target triangles under a triangle of A, along its
        /// normal): much fewer subdivisions when large triangles of A lie over many small, nearly flat triangles of B,
        /// eg a reduced mesh against the mesh it came from. Off by default (other uses keep their bounds)
        /// </summary>
        internal bool PatchBound = false;

        /// <summary> if this function returns true, the computation stops and Compute() returns false </summary>
        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> output: interval that contains the exact Hausdorff distance (NaN if Compute() failed on its inputs) </summary>
        public double LowerBound, UpperBound;
        /// <summary> output: true if UpperBound - LowerBound is within the tolerance (false if stopped by MaxSubdivisions or CancelF) </summary>
        public bool Converged;
        /// <summary> point where the lower bound is attained (on A, or on B if WorstPointOnB), and its closest point on the other mesh </summary>
        public Vector3d WorstPoint, WorstPointClosest;
        /// <summary> output: true if WorstPoint lies on MeshB (only possible if Symmetric) </summary>
        public bool WorstPointOnB;
        /// <summary> output: number of triangle subdivisions performed </summary>
        public int Subdivisions;


        /// <summary> Set up the distance computation between meshes a and b. Call Compute() to run it. </summary>
        public MeshHausdorffDistance(DMesh3 a, DMesh3 b)
        {
            MeshA = a; MeshB = b;
        }


        // extents in [2^-32, 2^65) are measured as given; outside, on copies scaled by a power of two (exact) to an extent in [1, 2).
        // (below: the bounds' absolute thresholds, eg 1e-12 for two triangle centroids in TriangleDeviationBounds, changed the
        // subdivisions from extents of about 2^-40; above: squared lengths stay far from overflow)
        const int MinPlainExponent = -32, MaxPlainExponent = 64;

        /// <summary>
        /// Compute LowerBound / UpperBound (and WorstPoint). Returns false, with LowerBound = UpperBound = NaN,
        /// if either mesh has no triangles or a non-finite vertex, or if a coordinate of the meshes, scaled so that their extent
        /// lies in [1, 2), would exceed 2^1000 (a mesh flat along an axis at coordinates far larger than its extent); also returns
        /// false if cancelled. Returns true otherwise, even if not Converged (check Converged).
        /// Meshes with an extent outside [2^-32, 2^65) are measured on copies scaled by an exact power of two (the squared lengths
        /// of the distance queries would overflow or underflow at their own scale), so the result at 2^k times the meshes is
        /// 2^k times the result, bit for bit; SpatialA / SpatialB are then not used.
        /// </summary>
        public bool Compute()
        {
            LowerBound = UpperBound = double.NaN; Converged = false; Subdivisions = 0;
            if (MeshA.TriangleCount == 0 || MeshB.TriangleCount == 0 || finite(MeshA) == false || finite(MeshB) == false)
                return false;
            AxisAlignedBox3d bounds = MeshA.CachedBounds;
            bounds.Contain(MeshB.CachedBounds);
            // the distance queries square lengths of the meshes' extent: beyond about 2^500 they overflowed (the nearest-triangle
            // search found no triangle and threw; from about 1e120 the upper bound was double.MaxValue), below about 2^-500 they
            // underflowed (a distance of 1e-202 was measured as 0). Such meshes are measured on copies scaled by 2^-e (#731)
            // (half extents: Max - Min overflows for coordinates of opposite sign near MaxValue)
            double half = 0;
            for (int k = 0; k < 3; ++k) half = Math.Max(half, 0.5 * bounds.Max[k] - 0.5 * bounds.Min[k]);
            int e = (half > 0) ? exponent(half) + 1 : 0;      // extent = 2 half in [2^e, 2^(e+1))
            if (e >= MinPlainExponent && e <= MaxPlainExponent)
                return compute(MeshA, MeshB, SpatialA, SpatialB, (Tolerance > 0) ? Tolerance : RelativeTolerance * bounds.DiagonalLength, 0, true);

            double big = 0;
            for (int k = 0; k < 3; ++k) big = Math.Max(big, Math.Max(Math.Abs(bounds.Max[k]), Math.Abs(bounds.Min[k])));
            if (exponent(big) - e > 1000)
                return false;
            DMesh3 a = scaled(MeshA, -e), b = scaled(MeshB, -e);
            AxisAlignedBox3d sb = a.CachedBounds;
            sb.Contain(b.CachedBounds);
            // (an absolute tolerance scaled with the meshes; below the subnormal range it is 0, as RelativeTolerance would be)
            double tol = (Tolerance > 0) ? pow2(Tolerance, -e) : RelativeTolerance * sb.DiagonalLength;
            return compute(a, b, Symmetric ? new DMeshAABBTree3(a, true) : null, new DMeshAABBTree3(b, true), tol, e, false);
        }

        bool compute(DMesh3 meshA, DMesh3 meshB, DMeshAABBTree3 spatialA, DMeshAABBTree3 spatialB, double tol, int e, bool keepSpatial)
        {
            if (spatialA == null && Symmetric) spatialA = new DMeshAABBTree3(meshA, true);
            if (spatialB == null) spatialB = new DMeshAABBTree3(meshB, true);
            if (keepSpatial) { SpatialA = spatialA; SpatialB = spatialB; }

            Subdivisions = 0;
            var ab = new OneSided(meshA, spatialB, tol, 0, this);
            ab.Run();
            double lower = ab.Lower, upper = ab.Upper;
            Converged = ab.Converged;
            Vector3d worst = ab.Worst, worstClosest = ab.WorstClosest; WorstPointOnB = false;
            if (Symmetric && CancelF() == false) {
                var ba = new OneSided(meshB, spatialA, tol, ab.Lower, this);
                ba.Run();
                Converged = Converged && ba.Converged;
                if (ba.Lower > lower) {
                    lower = ba.Lower;
                    worst = ba.Worst; worstClosest = ba.WorstClosest; WorstPointOnB = true;
                }
                upper = Math.Max(upper, ba.Upper);
            }
            upper = Math.Max(upper, lower);
            // back to the meshes' scale (exact; an unresolved bound, double.MaxValue, stays MaxValue)
            LowerBound = pow2(lower, e);
            UpperBound = (upper == double.MaxValue) ? double.MaxValue : Math.Min(pow2(upper, e), double.MaxValue);
            WorstPoint = pow2(worst, e); WorstPointClosest = pow2(worstClosest, e);
            return CancelF() == false;
        }

        // floor(log2(x)) for a finite x > 0 (subnormal too)
        static int exponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int biased = (int)((bits >> 52) & 0x7FF);
            if (biased == 0) return exponent(x * 18014398509481984.0) - 54;     // (2^54)
            return biased - 1023;
        }

        // x 2^n, exact unless the result leaves the normal range (in two factors: 2^n alone may not be representable)
        static double pow2(double x, int n)
        {
            int h = n / 2;
            return x * BitConverter.Int64BitsToDouble((long)(h + 1023) << 52) * BitConverter.Int64BitsToDouble((long)(n - h + 1023) << 52);
        }
        static Vector3d pow2(Vector3d v, int n) => new Vector3d(pow2(v.x, n), pow2(v.y, n), pow2(v.z, n));

        static DMesh3 scaled(DMesh3 mesh, int n)
        {
            var copy = new DMesh3(mesh, false, MeshComponents.None);
            foreach (int vid in copy.VertexIndices()) copy.SetVertex(vid, pow2(copy.GetVertex(vid), n));
            return copy;
        }


        // all vertices used by triangles have finite coordinates
        static bool finite(DMesh3 mesh)
        {
            foreach (int vid in mesh.VertexIndices()) {
                if (mesh.GetVtxEdgeCount(vid) == 0) continue;
                Vector3d v = mesh.GetVertex(vid);
                if (!v.IsFinite) return false;   // (each component: their sum overflows for finite large values)
            }
            return true;
        }

        // one-sided h(From, To), pruned against an external lower bound (for the symmetric distance)
        class OneSided
        {
            DMesh3 from, to;
            DMeshAABBTree3 spatial;
            TriangleDeviationBounds bounds;
            double tol, prune;
            MeshHausdorffDistance owner;

            // points of From (input vertices, then subdivision points): position, distance to To, nearest triangle
            List<Vector3d> pos = new List<Vector3d>();
            List<double> dist = new List<double>();
            List<int> near = new List<int>();
            List<Vector3d> closest = new List<Vector3d>();

            public double Lower, Upper;
            public bool Converged;
            public Vector3d Worst, WorstClosest;

            public OneSided(DMesh3 from, DMeshAABBTree3 spatialTo, double tol, double prune, MeshHausdorffDistance owner)
            {
                this.from = from; this.to = spatialTo.Mesh; spatial = spatialTo;
                bounds = new TriangleDeviationBounds(to) { PatchBound = owner.PatchBound };
                this.tol = tol; this.prune = prune; this.owner = owner;
            }

            void query(Vector3d p, out double d, out int t, out Vector3d c)
            {
                t = spatial.FindNearestTriangle(p);
                DistPoint3Triangle3 q = MeshQueries.TriangleDistance(to, t, p);
                d = Math.Sqrt(q.DistanceSquared);
                c = q.TriangleClosest;
            }

            int add_point(Vector3d p)
            {
                double d; int t; Vector3d c;
                query(p, out d, out t, out c);
                return add_point(p, d, t, c);
            }
            int add_point(Vector3d p, double d, int t, Vector3d c)
            {
                pos.Add(p); dist.Add(d); near.Add(t); closest.Add(c);
                int i = pos.Count - 1;
                if (d > Lower) {
                    Lower = d; Worst = p; WorstClosest = c;
                }
                return i;
            }

            public void Run()
            {
                // input vertices of the triangles (isolated vertices are not on the surface), in parallel
                int[] vmap = new int[from.MaxVertexID];
                int nv = 0;
                foreach (int vid in from.VertexIndices())
                    vmap[vid] = (from.GetVtxEdgeCount(vid) > 0) ? nv++ : -1;
                Vector3d[] vp = new Vector3d[nv];
                double[] vd = new double[nv];
                int[] vt = new int[nv];
                Vector3d[] vc = new Vector3d[nv];
                foreach (int vid in from.VertexIndices())
                    if (vmap[vid] >= 0) vp[vmap[vid]] = from.GetVertex(vid);
                gParallel.ForEach(Interval1i.Range(nv), (i) => query(vp[i], out vd[i], out vt[i], out vc[i]));
                Lower = 0;
                Worst = (nv > 0) ? vp[0] : Vector3d.Zero;
                WorstClosest = (nv > 0) ? vc[0] : Vector3d.Zero;
                for (int i = 0; i < nv; ++i)
                    add_point(vp[i], vd[i], vt[i], vc[i]);

                // initial upper bounds (in parallel)
                List<Index3i> tris = new List<Index3i>();
                foreach (int tid in from.TriangleIndices()) {
                    Index3i t = from.GetTriangle(tid);
                    tris.Add(new Index3i(vmap[t.a], vmap[t.b], vmap[t.c]));
                }
                double[] ub = new double[tris.Count];
                gParallel.ForEach(Interval1i.Range(tris.Count), (k) => ub[k] = upper_bound(tris[k]));

                var heap = new DeviationMaxHeap<Index3i>();
                for (int k = 0; k < tris.Count; ++k)
                    if (!(ub[k] <= threshold()))       // NaN (rounding in degenerate triangles) is kept
                        heap.Push(double.IsNaN(ub[k]) ? double.MaxValue : ub[k], tris[k]);

                // refine the triangles with the largest upper bounds, in batches: the distance queries of
                // the new points and the bounds of the children are computed in parallel
                Converged = true;
                const int BatchSize = 256;
                List<Index3i> batch = new List<Index3i>(BatchSize);
                while (heap.Count > 0 && heap.TopKey > threshold() + tol) {
                    if (owner.Subdivisions >= owner.MaxSubdivisions || owner.CancelF()) {
                        Converged = false;
                        break;
                    }
                    batch.Clear();
                    while (batch.Count < BatchSize && heap.Count > 0 && heap.TopKey > threshold() + tol)
                        batch.Add(heap.Pop());
                    owner.Subdivisions += batch.Count;

                    int nb = batch.Count;
                    Vector3d[] mp = new Vector3d[3 * nb], mc = new Vector3d[3 * nb];
                    double[] md = new double[3 * nb];
                    int[] mt = new int[3 * nb];
                    for (int b = 0; b < nb; ++b) {
                        Index3i t = batch[b];
                        mp[3 * b] = 0.5 * (pos[t.a] + pos[t.b]);
                        mp[3 * b + 1] = 0.5 * (pos[t.b] + pos[t.c]);
                        mp[3 * b + 2] = 0.5 * (pos[t.c] + pos[t.a]);
                    }
                    gParallel.ForEach(Interval1i.Range(3 * nb), (k) => query(mp[k], out md[k], out mt[k], out mc[k]));

                    Index3i[] children = new Index3i[4 * nb];
                    for (int b = 0; b < nb; ++b) {
                        Index3i t = batch[b];
                        int m01 = add_point(mp[3 * b], md[3 * b], mt[3 * b], mc[3 * b]);
                        int m12 = add_point(mp[3 * b + 1], md[3 * b + 1], mt[3 * b + 1], mc[3 * b + 1]);
                        int m20 = add_point(mp[3 * b + 2], md[3 * b + 2], mt[3 * b + 2], mc[3 * b + 2]);
                        children[4 * b] = new Index3i(t.a, m01, m20);
                        children[4 * b + 1] = new Index3i(m01, t.b, m12);
                        children[4 * b + 2] = new Index3i(m20, m12, t.c);
                        children[4 * b + 3] = new Index3i(m01, m12, m20);
                    }
                    double[] cu = new double[4 * nb];
                    gParallel.ForEach(Interval1i.Range(4 * nb), (k) => cu[k] = upper_bound(children[k]));
                    for (int k = 0; k < 4 * nb; ++k)
                        if (!(cu[k] <= threshold()))
                            heap.Push(double.IsNaN(cu[k]) ? double.MaxValue : cu[k], children[k]);
                }
                // dropped triangles were below max(Lower, prune), so for the pruned (second) direction this is
                // only an upper bound where it is above prune, which is all the symmetric distance needs
                Upper = Math.Max(Lower, (heap.Count > 0) ? heap.TopKey : 0);
            }

            double threshold() { return Math.Max(Lower, prune); }

            // upper bound of the distance to To over the triangle (i, j, k)
            double upper_bound(Index3i t)
            {
                return bounds.UpperBound(pos[t.a], pos[t.b], pos[t.c], dist[t.a], dist[t.b], dist[t.c],
                    near[t.a], near[t.b], near[t.c], threshold());
            }
        }
    }
}
