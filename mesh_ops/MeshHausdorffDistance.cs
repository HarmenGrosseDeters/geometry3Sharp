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
        /// <summary> stop (Converged = false) after this many triangle subdivisions (checked per batch of 256: up to 255 more per direction).
        /// For the symmetric distance the first direction may use half of it (rounded up), the second the rest: what the first left
        /// unused is passed on, so the second gets at least half less the first one's last batch (up to 255), and a first direction
        /// which does not converge leaves the second one its share (#740) </summary>
        public int MaxSubdivisions = 20000000;

        /// <summary>
        /// also use the patch bound of TriangleDeviationBounds (target triangles under a triangle of A, along its
        /// normal): much fewer subdivisions when large triangles of A lie over many small, nearly flat triangles of B,
        /// eg a reduced mesh against the mesh it came from. Off by default (other uses keep their bounds)
        /// </summary>
        internal bool PatchBound = false;

        /// <summary>
        /// certify UpperBound too, rounding included (#742): the distances of the subdivision points and the bounds of the triangles are
        /// evaluated with outward rounding (ExactInterval), so UpperBound is never below the exact distance. Without it UpperBound is
        /// certified only up to the floating-point evaluation of the distance queries (see Compute), which can put it below the exact
        /// distance where a point of one mesh lies very near the other (a distance d much below the size E of the nearest triangle).
        /// Takes about twice the time (+16 to +115%, benchmarks/HausdorffBenchmark); the internal patch bound is not used then. Every
        /// certified distance is at least about 2^-537 (the outward rounding), so an absolute Tolerance below that cannot converge for
        /// meshes that touch.
        /// Off by default (the results are then as before, bit for bit). docs/design/HAUSDORFF_CERTIFIED_UPPER.md
        /// </summary>
        public bool CertifiedUpper = false;

        /// <summary> tests only: measure the meshes as given, without the scaled or shifted copies (to test the bounds' own handling of
        /// rounded subdivision points, #738) </summary>
        internal bool AsGivenForTests = false;

        /// <summary> if this function returns true, the computation stops and Compute() returns false </summary>
        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> output: interval that contains the exact Hausdorff distance (NaN if Compute() failed on its inputs): LowerBound
        /// certified, rounding included; UpperBound certified with CertifiedUpper, otherwise up to the floating-point evaluation of the
        /// distance queries, which can put it below the exact distance near the surface (see Compute) </summary>
        public double LowerBound, UpperBound;
        /// <summary> output: true if UpperBound - LowerBound is within the tolerance (false if stopped by MaxSubdivisions or CancelF, or if
        /// the subdivision points' rounding keeps the gap open); the upper bound, and so the gap, up to the floating-point evaluation of
        /// the distance queries unless CertifiedUpper </summary>
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
        // coordinates up to 2^24 times the extent are measured as given (eg UTM coordinates, 2^16 times a tile of 100 m): a midpoint
        // rounds there by at most 2^-29 of the extent, which the bounds account for (OneSided.off); beyond, on copies shifted near 0
        const int MaxPlainOffsetExponent = 24;

        /// <summary>
        /// Compute LowerBound / UpperBound (and WorstPoint). Returns false, with LowerBound = UpperBound = NaN (the other outputs
        /// are then not defined),
        /// if either mesh has no triangles or a non-finite vertex, or if the distance exceeds double.MaxValue (meshes near
        /// opposite ends of the double range); also returns false if cancelled. Returns true otherwise, even if not Converged
        /// (check Converged). Vertices no triangle uses are ignored (also for the size of the meshes).
        /// Meshes with an extent outside [2^-32, 2^65) are measured on copies scaled by an exact power of two (the squared lengths
        /// of the distance queries would overflow or underflow at their own scale), so the result at 2^k times the meshes is
        /// 2^k times the result, bit for bit; meshes whose coordinates exceed 2^24 times their extent on copies moved near 0 by an
        /// exact shift (subdivision points would round off the triangles). SpatialA / SpatialB are then not used (nor set).
        /// LowerBound is certified, rounding included: never above the exact distance (the worst point's distance is re-evaluated
        /// exactly, less the rounding of the point; docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md). UpperBound is certified up to the
        /// floating-point evaluation of the distance queries, about 2^-53 E^2 / d for a distance d to a triangle of size E, at most about
        /// 2^-26 E (E: the nearest target triangle's size; the rounding of subdivision points is included): near the surface (d much below
        /// E) it can be below the exact distance. CertifiedUpper certifies it fully (#742).
        /// </summary>
        public bool Compute()
        {
            LowerBound = UpperBound = double.NaN; Converged = false; Subdivisions = 0;
            if (MeshA.TriangleCount == 0 || MeshB.TriangleCount == 0 || finite(MeshA) == false || finite(MeshB) == false)
                return false;
            // (the vertices used by triangles: a far isolated vertex would set the scale, and the triangles, scaled by it, underflowed)
            AxisAlignedBox3d bounds = used_bounds(MeshA);
            bounds.Contain(used_bounds(MeshB));
            // the distance queries square lengths of the meshes' extent: beyond about 2^500 they overflowed (the nearest-triangle
            // search found no triangle and threw; from about 1e120 the upper bound was double.MaxValue), below about 2^-500 they
            // underflowed (a distance of 1e-202 was measured as 0). Such meshes are measured on copies scaled by 2^-e (#731)
            // (the extent is the largest span Max - Min; where that overflows, twice the half span, which is exact there. Halving
            // every span would round a span of 2^-1074 to 0; review of #735)
            int e = int.MinValue;
            for (int k = 0; k < 3; ++k) {
                double span = bounds.Max[k] - bounds.Min[k];
                if (double.IsInfinity(span)) e = Math.Max(e, exponent(0.5 * bounds.Max[k] - 0.5 * bounds.Min[k]) + 1);
                else if (span > 0) e = Math.Max(e, exponent(span));
            }
            if (e == int.MinValue) e = 0;      // (the extent is in [2^e, 2^(e+1)); a single point stays as it is)
            // each axis whose coordinates are far from 0 compared with their span is moved near 0 by a shift c, exactly (Sterbenz: all
            // coordinates lie in [c, 2c], or [2c, c] below 0; an axis along which both meshes are flat is the case of span 0). At
            // coordinates far beyond the extent, a midpoint of two corners is not representable and rounds: at z = 1.3 2^52 an ulp is 1,
            // the midpoint of z = -1 and 0 rounded onto a corner, a child triangle was its parent and refinement never ended, and the
            // rounded points, off the triangle by up to 0.5, gave a lower bound 18 times the distance (#738). After the shift every
            // coordinate is at most twice the span of its axis; along an axis that cannot be shifted it already is
            Vector3d shift = Vector3d.Zero;
            double big = 0;
            for (int k = 0; k < 3; ++k) {
                double lo = bounds.Min[k], hi = bounds.Max[k];
                if (lo > 0 && hi <= 2 * lo) shift[k] = lo;
                else if (hi < 0 && lo >= 2 * hi) shift[k] = hi;
                big = Math.Max(big, Math.Max(Math.Abs(lo), Math.Abs(hi)));
            }
            bool offset = big > 0 && exponent(big) - e > MaxPlainOffsetExponent;
            if ((e >= MinPlainExponent && e <= MaxPlainExponent && !offset) || AsGivenForTests)
                return compute(MeshA, MeshB, SpatialA, SpatialB, (Tolerance > 0) ? Tolerance : RelativeTolerance * bounds.DiagonalLength,
                    0, Vector3d.Zero, true);

            DMesh3 a = scaled(MeshA, shift, -e), b = scaled(MeshB, shift, -e);
            // (an absolute tolerance scaled with the meshes; below the subnormal range it is 0, as RelativeTolerance would be)
            double tol = (Tolerance > 0) ? pow2(Tolerance, -e) : RelativeTolerance * new AxisAlignedBox3d(pow2(bounds.Min - shift, -e), pow2(bounds.Max - shift, -e)).DiagonalLength;
            return compute(a, b, Symmetric ? new DMeshAABBTree3(a, true) : null, new DMeshAABBTree3(b, true), tol, e, shift, false);
        }

        bool compute(DMesh3 meshA, DMesh3 meshB, DMeshAABBTree3 spatialA, DMeshAABBTree3 spatialB, double tol, int e, Vector3d shift, bool keepSpatial)
        {
            if (spatialA == null && Symmetric) spatialA = new DMeshAABBTree3(meshA, true);
            if (spatialB == null) spatialB = new DMeshAABBTree3(meshB, true);
            if (keepSpatial) { SpatialA = spatialA; SpatialB = spatialB; }

            Subdivisions = 0;
            var ab = new OneSided(meshA, spatialB, tol, 0, this, Symmetric ? MaxSubdivisions - MaxSubdivisions / 2 : MaxSubdivisions);
            ab.Run();
            // (the certified lower bounds; the second direction is pruned against the first one's computed Lower, which the upper
            // bound of the first direction is never below, so the pruned triangles stay bounded)
            double lower = ab.CertifiedLower, upper = ab.Upper, computed = ab.Lower;
            Converged = ab.Converged;
            Vector3d worst = ab.CertifiedWorst, worstClosest = ab.CertifiedWorstClosest; WorstPointOnB = false;
            if (Symmetric && CancelF() == false) {
                var ba = new OneSided(meshB, spatialA, tol, ab.Lower, this, MaxSubdivisions);
                ba.Run();
                Converged = Converged && ba.Converged;
                computed = Math.Max(computed, ba.Lower);
                if (ba.CertifiedLower > lower) {
                    lower = ba.CertifiedLower;
                    worst = ba.CertifiedWorst; worstClosest = ba.CertifiedWorstClosest; WorstPointOnB = true;
                }
                upper = Math.Max(upper, ba.Upper);
            }
            upper = Math.Max(upper, lower);
            // (CertifiedUpper on copies scaled down: their coordinates below 2^(e - 1022) round into the subnormal range, so the copies'
            // distance may be below the meshes' (scaled) by less than 2^-1072; every certified triangle bound is at least 2^-537 and
            // ends with a rounding up, at least about 2^-590 above its exact value, which covers it; HAUSDORFF_CERTIFIED_UPPER.md
            // section 8)
            // where the certified lower bound is below the computed one (a rounded point off the triangles, #738), the gap to the upper
            // bound may exceed the tolerance
            if (lower < computed && !(upper - lower <= tol))
                Converged = false;
            // back to the meshes' scale (exact; an unresolved bound, double.MaxValue, stays MaxValue). A distance beyond MaxValue
            // (meshes near opposite ends of the double range) has no bounds in doubles: refused, the outputs NaN
            double lo = pow2(lower, e), up = (upper == double.MaxValue) ? double.MaxValue : pow2(upper, e);
            // (a subnormal result may round: outward then, the lower bound down, the upper bound up; review of #735)
            if (pow2(lo, -e) != lower) lo = Math.Max(0, ExactInterval.Down(lo));
            if (up != double.MaxValue && !double.IsInfinity(up) && pow2(up, -e) != upper) up = ExactInterval.Up(up);
            if (double.IsInfinity(lo) || double.IsInfinity(up)) {
                Converged = false;
                return false;
            }
            LowerBound = lo; UpperBound = up;
            WorstPoint = pow2(worst, e); WorstPointClosest = pow2(worstClosest, e);
            // (rounded to doubles; exact along a flat axis, whose scaled coordinates are 0)
            for (int k = 0; k < 3; ++k)
                if (shift[k] != 0) { WorstPoint[k] += shift[k]; WorstPointClosest[k] += shift[k]; }
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

        // x 2^n, exact unless the result leaves the normal range (in two factors: 2^n alone may not be representable; a subnormal
        // result may round twice, for values below about 2^-485 of the extent, which do not decide a bound)
        static double pow2(double x, int n)
        {
            int h = n / 2;
            return x * BitConverter.Int64BitsToDouble((long)(h + 1023) << 52) * BitConverter.Int64BitsToDouble((long)(n - h + 1023) << 52);
        }
        static Vector3d pow2(Vector3d v, int n) => new Vector3d(pow2(v.x, n), pow2(v.y, n), pow2(v.z, n));

        // (v - shift) 2^n; vertices no triangle uses at 0 (they may be far away or not finite)
        static DMesh3 scaled(DMesh3 mesh, Vector3d shift, int n)
        {
            var copy = new DMesh3(mesh, false, MeshComponents.None);
            foreach (int vid in copy.VertexIndices())
                copy.SetVertex(vid, (copy.GetVtxEdgeCount(vid) > 0) ? pow2(copy.GetVertex(vid) - shift, n) : Vector3d.Zero);
            return copy;
        }

        // bounds of the vertices used by triangles
        static AxisAlignedBox3d used_bounds(DMesh3 mesh)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            foreach (int vid in mesh.VertexIndices())
                if (mesh.GetVtxEdgeCount(vid) > 0) box.Contain(mesh.GetVertex(vid));
            return box;
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
            // how far a point may be from the triangle of From it stands for (0 for the input vertices): a subdivision point is the
            // midpoint of two points rounded to doubles, so it may lie off the triangle. A triangle's bound plus the largest of its
            // corners' off bounds the distances of the points it stands for; the certified lower bound subtracts the worst point's
            // (#738)
            List<double> off = new List<double>();
            // the largest bound of a triangle that is not refined further, kept in Upper (#738): one that cannot be split (in doubles
            // a child is the triangle itself, every midpoint rounding onto a corner), or one whose corners may be off by more than
            // the tolerance (the gap of its bounds cannot close below that; it was refined until the points' last bits)
            double stuck = 0;

            public double Lower, Upper;
            public bool Converged;
            public Vector3d Worst, WorstClosest;
            // the off of Worst; the input vertex farthest from To (its off is 0) and its distance
            double worstOff;
            int vertexWorst = -1;
            // certified: a lower bound that is never above the exact distance (Lower is the largest computed distance of a point,
            // which may be above it by the rounding of the distance query and, for a subdivision point, by its off); the point and
            // its closest point
            public double CertifiedLower;
            public Vector3d CertifiedWorst, CertifiedWorstClosest;

            // (owner.Subdivisions, counted over both directions, at which this direction stops)
            readonly int limit;

            public OneSided(DMesh3 from, DMeshAABBTree3 spatialTo, double tol, double prune, MeshHausdorffDistance owner, int limit)
            {
                this.from = from; this.to = spatialTo.Mesh; spatial = spatialTo;
                certified = owner.CertifiedUpper;
                // (the patch bound's rounding slack is estimated, not derived: not used for a certified bound)
                bounds = new TriangleDeviationBounds(to) { PatchBound = owner.PatchBound && !certified, Certified = certified };
                this.tol = tol; this.prune = prune; this.owner = owner; this.limit = limit;
            }

            // CertifiedUpper: per point, a certified upper bound of its distance to its nearest triangle (B1; the triangles' bounds use
            // these instead of dist)
            readonly bool certified;
            List<double> up = new List<double>();

            void query(Vector3d p, out double d, out int t, out Vector3d c, out double u)
            {
                t = spatial.FindNearestTriangle(p);
                DistPoint3Triangle3 q = MeshQueries.TriangleDistance(to, t, p);
                d = Math.Sqrt(q.DistanceSquared);
                c = q.TriangleClosest;
                u = certified ? CertifiedTriangleBounds.PointTriangleUpper(p, q.Triangle.V0, q.Triangle.V1, q.Triangle.V2,
                    q.TriangleBaryCoords.y, q.TriangleBaryCoords.z) : d;
            }

            int add_point(Vector3d p)
            {
                double d, u; int t; Vector3d c;
                query(p, out d, out t, out c, out u);
                return add_point(p, d, t, c, 0, u);
            }
            int add_point(Vector3d p, double d, int t, Vector3d c, double o, double u)
            {
                pos.Add(p); dist.Add(d); up.Add(u); near.Add(t); closest.Add(c); off.Add(o);
                int i = pos.Count - 1;
                if (d > Lower) {
                    Lower = d; Worst = p; WorstClosest = c; worstOff = o;
                }
                return i;
            }



            // the triangles have the same corners (as a multiset of points): splitting made no progress
            bool same(Index3i c, Index3i p)
            {
                Vector3d c0 = pos[c.a], c1 = pos[c.b], c2 = pos[c.c], p0 = pos[p.a], p1 = pos[p.b], p2 = pos[p.c];
                return (c0 == p0 && ((c1 == p1 && c2 == p2) || (c1 == p2 && c2 == p1)))
                    || (c0 == p1 && ((c1 == p0 && c2 == p2) || (c1 == p2 && c2 == p0)))
                    || (c0 == p2 && ((c1 == p0 && c2 == p1) || (c1 == p1 && c2 == p0)));
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
                double[] vu = new double[nv];
                foreach (int vid in from.VertexIndices())
                    if (vmap[vid] >= 0) vp[vmap[vid]] = from.GetVertex(vid);
                gParallel.ForEach(Interval1i.Range(nv), (i) => query(vp[i], out vd[i], out vt[i], out vc[i], out vu[i]));
                Lower = 0;
                Worst = (nv > 0) ? vp[0] : Vector3d.Zero;
                WorstClosest = (nv > 0) ? vc[0] : Vector3d.Zero;
                for (int i = 0; i < nv; ++i) {
                    add_point(vp[i], vd[i], vt[i], vc[i], 0, vu[i]);
                    if (vertexWorst < 0 || vd[i] > vd[vertexWorst]) vertexWorst = i;
                }

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
                    if (owner.Subdivisions >= limit || owner.CancelF()) {
                        Converged = false;
                        break;
                    }
                    batch.Clear();
                    while (batch.Count < BatchSize && heap.Count > 0 && heap.TopKey > threshold() + tol)
                        batch.Add(heap.Pop());
                    owner.Subdivisions += batch.Count;

                    int nb = batch.Count;
                    Vector3d[] mp = new Vector3d[3 * nb], mc = new Vector3d[3 * nb];
                    double[] md = new double[3 * nb], mo = new double[3 * nb], mu = new double[3 * nb];
                    int[] mt = new int[3 * nb];
                    for (int b = 0; b < nb; ++b) {
                        Index3i t = batch[b];
                        double e0, e1, e2;
                        mp[3 * b] = HausdorffRounding.Midpoint(pos[t.a], pos[t.b], out e0);
                        mp[3 * b + 1] = HausdorffRounding.Midpoint(pos[t.b], pos[t.c], out e1);
                        mp[3 * b + 2] = HausdorffRounding.Midpoint(pos[t.c], pos[t.a], out e2);
                        // (the exact midpoint of two points within off of the triangle is within their mean of it)
                        mo[3 * b] = HausdorffRounding.MidOff(off[t.a], off[t.b], e0);
                        mo[3 * b + 1] = HausdorffRounding.MidOff(off[t.b], off[t.c], e1);
                        mo[3 * b + 2] = HausdorffRounding.MidOff(off[t.c], off[t.a], e2);
                    }
                    gParallel.ForEach(Interval1i.Range(3 * nb), (k) => query(mp[k], out md[k], out mt[k], out mc[k], out mu[k]));

                    Index3i[] children = new Index3i[4 * nb];
                    for (int b = 0; b < nb; ++b) {
                        Index3i t = batch[b];
                        int m01 = add_point(mp[3 * b], md[3 * b], mt[3 * b], mc[3 * b], mo[3 * b], mu[3 * b]);
                        int m12 = add_point(mp[3 * b + 1], md[3 * b + 1], mt[3 * b + 1], mc[3 * b + 1], mo[3 * b + 1], mu[3 * b + 1]);
                        int m20 = add_point(mp[3 * b + 2], md[3 * b + 2], mt[3 * b + 2], mc[3 * b + 2], mo[3 * b + 2], mu[3 * b + 2]);
                        children[4 * b] = new Index3i(t.a, m01, m20);
                        children[4 * b + 1] = new Index3i(m01, t.b, m12);
                        children[4 * b + 2] = new Index3i(m20, m12, t.c);
                        children[4 * b + 3] = new Index3i(m01, m12, m20);
                    }
                    double[] cu = new double[4 * nb];
                    gParallel.ForEach(Interval1i.Range(4 * nb), (k) => cu[k] = upper_bound(children[k]));
                    for (int k = 0; k < 4 * nb; ++k) {
                        if (cu[k] <= threshold())
                            continue;
                        Index3i c = children[k];
                        if (same(c, batch[k / 4]) || Math.Max(off[c.a], Math.Max(off[c.b], off[c.c])) > tol)
                            stuck = Math.Max(stuck, double.IsNaN(cu[k]) ? double.MaxValue : cu[k]);
                        else
                            heap.Push(double.IsNaN(cu[k]) ? double.MaxValue : cu[k], children[k]);
                    }
                }
                if (stuck > threshold() + tol)
                    Converged = false;
                // dropped triangles were below max(Lower, prune), so for the pruned (second) direction this is
                // only an upper bound where it is above prune, which is all the symmetric distance needs
                Upper = Math.Max(Math.Max(Lower, stuck), (heap.Count > 0) ? heap.TopKey : 0);
                certify();
            }

            // the certified lower bound: the exact distance of the worst point, minus its off, and of the farthest input vertex,
            // rounded down (CertifiedPointMeshDistance); never above Lower, so where Lower holds it is Lower, bit for bit
            void certify()
            {
                CertifiedLower = 0; CertifiedWorst = Worst; CertifiedWorstClosest = WorstClosest;
                if (pos.Count == 0)
                    return;
                double w = CertifiedPointMeshDistance.LowerBound(to, Worst);
                double cw = (worstOff > 0) ? HausdorffRounding.Down(ExactInterval.Down(w - worstOff)) : w;
                if (!(cw > 0)) cw = 0;
                double cv = 0;
                if (vertexWorst >= 0 && pos[vertexWorst] != Worst)
                    cv = CertifiedPointMeshDistance.LowerBound(to, pos[vertexWorst]);
                if (cv > cw) {
                    CertifiedLower = Math.Min(Lower, cv); CertifiedWorst = pos[vertexWorst]; CertifiedWorstClosest = closest[vertexWorst];
                } else
                    CertifiedLower = Math.Min(Lower, cw);
            }

            double threshold() { return Math.Max(Lower, prune); }


            // upper bound of the distance to To over the triangle (i, j, k), and over the part of From's triangle it stands for
            double upper_bound(Index3i t)
            {
                return HausdorffRounding.TriangleUpper(bounds, pos[t.a], pos[t.b], pos[t.c], up[t.a], up[t.b], up[t.c],
                    near[t.a], near[t.b], near[t.c], off[t.a], off[t.b], off[t.c], threshold());
            }
        }
    }

    /// <summary>
    /// The rounding allowance of MeshHausdorffDistance's subdivision points (#738; derivations in
    /// docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md, section c)
    /// </summary>
    internal static class HausdorffRounding
    {
        // below x by more than the rounding error of up to two operations whose exact result is x (as Up); 0 for x <= 0
        internal static double Down(double x) => (x > 0) ? Math.Max(0, Math.Min(x * (1 - 4.5e-16), ExactInterval.Down(x))) : 0;
        // above x by more than the rounding error of up to two operations whose exact result is x: relatively for a normal x (the
        // factor is the double 1 + 2^-51, the rounded product at least x (1 + 3.33e-16)), by one ulp for a subnormal one (whose
        // roundings are absolute, at most half the smallest subnormal each)
        internal static double Up(double x) => (x > 0) ? Math.Max(x * (1 + 4.5e-16), ExactInterval.Up(x)) : x;

        // the midpoint of a and b in doubles, and an upper bound of its distance from the exact midpoint (TwoSum per component:
        // a + b = s + r exactly; halving s is exact unless the result is subnormal; docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md c1)
        internal static Vector3d Midpoint(Vector3d a, Vector3d b, out double err)
        {
            Vector3d m = Vector3d.Zero;
            // (the sum of the components' errors bounds the length of the error vector, without squares, which underflow)
            double e1 = 0;
            const double MinNormal = 2.2250738585072014E-308, Tiny = 4.9406564584124654E-324;
            for (int k = 0; k < 3; ++k) {
                double x = a[k], y = b[k], sum = x + y, bb = sum - x, r = (x - (sum - bb)) + (y - bb);
                m[k] = 0.5 * sum;
                // (an overflowing sum, only measured as given at coordinates near MaxValue: no bound, the point is unusable)
                if (double.IsInfinity(sum) || double.IsNaN(r)) { err = double.PositiveInfinity; return m; }
                // |r| / 2, plus one smallest subnormal where halving may round (a subnormal r or a subnormal midpoint: by at most
                // half of it each)
                double ek = 0.5 * Math.Abs(r);
                if (r != 0 && Math.Abs(r) < 2 * MinNormal) ek += Tiny;
                // (the halving of s rounds only if 0 < |s| < 2^-1021: tested on the sum, not on the midpoint, since the half of the
                // largest double below 2^-1021 is a tie that rounds up to the normal 2^-1022; numerics review of #819, F1)
                if (sum != 0 && Math.Abs(sum) < 2 * MinNormal) ek += Tiny;
                e1 += ek;
            }
            err = (e1 > 0) ? Up(e1) : 0;
            return m;
        }

        /// <summary> an upper bound of the distance to the target over the exact triangle that the rounded triangle (p0, p1, p2) stands
        /// for, its corners off by at most o0, o1, o2 (docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md c3): the rounded triangle's bound plus
        /// the largest of them, rounded up </summary>
        internal static double TriangleUpper(TriangleDeviationBounds bounds, Vector3d p0, Vector3d p1, Vector3d p2, double d0, double d1, double d2,
            int n0, int n1, int n2, double o0, double o1, double o2, double threshold)
        {
            double o = Math.Max(o0, Math.Max(o1, o2));
            double u = bounds.UpperBound(p0, p1, p2, d0, d1, d2, n0, n1, n2, threshold);
            return (o > 0) ? Up(u + o) : u;
        }

        internal static double MidOff(double oa, double ob, double err)
        {
            double o = 0.5 * (oa + ob) + err;
            // (a positive input whose half rounds to 0, eg 2^-1074: the smallest subnormal instead; not reached from Midpoint, whose
            // allowances are 0 or at least 2^-1073; numerics review N3)
            if (o > 0) return Up(o);
            return (oa > 0 || ob > 0 || err > 0) ? 4.9406564584124654E-324 : 0;
        }
    }
}
