// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary> Outcome of a continuous collision query (ContinuousCollision) </summary>
    public enum CcdStatus
    {
        /// <summary>
        /// a coordinate that is not finite or above 2^1000 in magnitude, or an invalid option; nothing is proven (the
        /// zero value, so that a default CcdResult proves nothing)
        /// </summary>
        InvalidInput,
        /// <summary>
        /// proven: for every t in [0, TimeLimit] the primitives stay farther apart than MinSeparation in the L-infinity
        /// distance (the largest coordinate difference of any two of their points), which implies the same for the
        /// Euclidean distance
        /// </summary>
        NoCollision,
        /// <summary>
        /// possible contact: there is no contact (L-infinity distance &lt;= MinSeparation) on [0, TimeOfImpact), and at
        /// NearContactTime the primitives are within MinSeparation + 2 ErrorBound + ToleranceReached of each other in every
        /// coordinate, so within sqrt(3) times that in the Euclidean distance (a false positive is near)
        /// </summary>
        Collision,
        /// <summary>
        /// MaxIterations boxes were evaluated before a near contact was found: treat as a collision at TimeOfImpact
        /// (there is no contact on [0, TimeOfImpact)), but nothing is known about the distance after it
        /// </summary>
        IterationLimit
    }

    /// <summary> Options of a continuous collision query </summary>
    public sealed class CcdOptions
    {
        /// <summary> the distance below which the primitives count as in contact (absolute, &gt;= 0) </summary>
        public double MinSeparation = 0;
        /// <summary>
        /// the width (per coordinate) of the image of a parameter box below which the search stops and reports a
        /// collision (absolute). NaN: RelativeTolerance times the largest extent (per axis) of the query's points, but at
        /// least the evaluation error M 2^-47 (M the largest coordinate magnitude of an axis).
        /// </summary>
        public double Tolerance = double.NaN;
        /// <summary> see Tolerance </summary>
        public double RelativeTolerance = 1e-6;
        /// <summary> the number of parameter boxes evaluated before IterationLimit; &lt;= 0: no limit </summary>
        public int MaxIterations = 1000000;
        /// <summary>
        /// prove [0, TimeLimit] only (0 &lt;= TimeLimit &lt;= 1). The search still bisects [0, 1] (so that every box
        /// corner is exact) and stops at the first box that starts after TimeLimit.
        /// </summary>
        public double TimeLimit = 1;
        /// <summary>
        /// once a near contact is found, the search stops refining when every box it has not discarded starts at most
        /// this much before it (so NearContactTime - TimeOfImpact &lt;= TimeTolerance unless MaxTighteningIterations or
        /// MaxIterations stops it)
        /// </summary>
        public double TimeTolerance = 1e-6;
        /// <summary>
        /// the number of boxes evaluated after a near contact is found, to bring TimeOfImpact up to it (see
        /// TimeTolerance); when it runs out, TimeOfImpact stays the (correct, looser) smallest start of the boxes not
        /// discarded. Near contacts with a minimum separation whose front is not axis-aligned can need many boxes, since
        /// a box is discarded only when one coordinate separates it.
        /// </summary>
        public int MaxTighteningIterations = 1024;

        internal bool IsValid()
        {
            return MinSeparation >= 0 && MinSeparation < double.PositiveInfinity
                && (double.IsNaN(Tolerance) || (Tolerance >= 0 && Tolerance < double.PositiveInfinity))
                && RelativeTolerance >= 0 && RelativeTolerance < double.PositiveInfinity
                && TimeLimit >= 0 && TimeLimit <= 1
                && TimeTolerance >= 0 && TimeTolerance <= 1
                && MaxTighteningIterations >= 0;
        }
    }

    /// <summary> Result of a continuous collision query </summary>
    public struct CcdResult
    {
        /// <summary> see CcdStatus </summary>
        public CcdStatus Status;
        /// <summary>
        /// a lower bound of the time of the first contact: there is no contact on [0, TimeOfImpact). NoCollision:
        /// TimeLimit if it is below 1 (only [0, TimeLimit] is proven), +infinity for the whole step; InvalidInput:
        /// +infinity (nothing is proven).
        /// </summary>
        public double TimeOfImpact;
        /// <summary>
        /// Collision: a time at which the primitives are within sqrt(3) (MinSeparation + 2 ErrorBound +
        /// ToleranceReached) of each other (TimeOfImpact &lt;= NearContactTime); +infinity otherwise
        /// </summary>
        public double NearContactTime;
        /// <summary>
        /// an upper bound of the width (per coordinate) of the reported box's computed corner values; the width of the
        /// exact image is at most ToleranceReached + 2 ErrorBound
        /// </summary>
        public double ToleranceReached;
        /// <summary> the largest bound of the evaluation error that was used (0 on the interval path) </summary>
        public double ErrorBound;
        /// <summary> the number of parameter boxes evaluated </summary>
        public int Iterations;
    }

    /// <summary>
    /// Conservative continuous collision detection of a vertex against a triangle and of an edge against an edge, all
    /// points moving linearly from their positions at t = 0 to those at t = 1. NoCollision is a proof that the
    /// primitives stay farther apart than MinSeparation; a Collision may be a false positive, but only if the
    /// primitives came near (CcdStatus.Collision).
    ///
    /// Method (inclusion-based root finding, after B. Wang, Z. Ferguson, T. Schneider, X. Jiang, M. Attene, D. Panozzo,
    /// "A Large-scale Benchmark and an Inclusion-based Algorithm for Continuous Collision Detection", ACM TOG 40(5),
    /// 2021; implemented from the paper, bounds our own, docs/design/CCD.md): the difference F(t, u, v) of the two
    /// moving points of the primitives (u, v their barycentric or edge parameters) is affine in each of t, u, v
    /// separately, so the image of a parameter box lies in the convex hull of the values at its 8 corners. A box is
    /// discarded when on some axis all corner values are beyond MinSeparation (with the evaluation error), and split
    /// at the midpoint otherwise; boxes are taken in the order of their start time, so the first box whose image is
    /// narrower than the tolerance gives the time of impact.
    /// </summary>
    public static class ContinuousCollision
    {
        static readonly CcdOptions defaults = new CcdOptions();

        /// <summary>
        /// Vertex p against the triangle (a, b, c), with the positions at t = 0 (p0, a0, b0, c0) and at t = 1 (p1, a1,
        /// b1, c1) and linear motion in between.
        /// </summary>
        public static CcdResult VertexTriangle(Vector3d p0, Vector3d a0, Vector3d b0, Vector3d c0,
                                               Vector3d p1, Vector3d a1, Vector3d b1, Vector3d c1,
                                               CcdOptions options = null)
        {
            var s = new CcdSolver();
            s.Set(p0, a0, b0, c0, p1, a1, b1, c1);
            return s.Solve(true, options ?? defaults);
        }

        /// <summary>
        /// Edge (a, b) against the edge (c, d), with the positions at t = 0 (a0, b0, c0, d0) and at t = 1 (a1, b1, c1,
        /// d1) and linear motion in between.
        /// </summary>
        public static CcdResult EdgeEdge(Vector3d a0, Vector3d b0, Vector3d c0, Vector3d d0,
                                         Vector3d a1, Vector3d b1, Vector3d c1, Vector3d d1,
                                         CcdOptions options = null)
        {
            var s = new CcdSolver();
            s.Set(a0, b0, c0, d0, a1, b1, c1, d1);
            return s.Solve(false, options ?? defaults);
        }
    }


    /// <summary>
    /// The solver of ContinuousCollision, reusable for many queries (one per thread). The four points of a query are
    /// (p, a, b, c) for vertex-triangle and (a, b, c, d) for edge-edge, at t = 0 and t = 1.
    /// F(t, u, v) = p(t) - ((1 - u - v) a(t) + u b(t) + v c(t)) over u, v &gt;= 0, u + v &lt;= 1 (vertex-triangle) and
    /// F(t, u, v) = (a(t) + u (b(t) - a(t))) - (c(t) + v (d(t) - c(t))) over [0, 1]^2 (edge-edge), t in [0, 1].
    /// </summary>
    internal sealed class CcdSolver
    {
        // start[3 i + axis], end[3 i + axis] of the four points i
        readonly double[] start = new double[12], end = new double[12];

        // the queue of parameter boxes, a binary heap ordered by t0, then depth (deeper first), then seq: a total order,
        // so the search is deterministic
        struct Box
        {
            public double t0, t1, u0, u1, v0, v1;
            public int depth, seq;
        }
        Box[] heap = new Box[64];
        int count, seq;

        // corner values of the current box: corner k has t = bit 0, u = bit 1, v = bit 2 (lower or upper end); on the
        // interval path lo / hi of the interval, on the filter path both the computed value
        readonly double[] cornerLo = new double[24], cornerHi = new double[24];
        // vertex-triangle boxes that cross u + v = 1: the corners outside the triangle are left out of the inclusion
        // (they only steer the split), and the two points where the box boundary crosses u + v = 1 are added
        // (values at 4 axis + 2 kt + j); F is affine in (u, v) for fixed t and affine in t for fixed (u, v), so the
        // image of the box's part in the triangle lies in the hull of the values at the clipped polygon's vertices at t0
        // and t1
        readonly double[] extraLo = new double[12], extraHi = new double[12];
        readonly double[] extraU = new double[2], extraV = new double[2];
        int extraCount, insideMask;

        /// <summary> the smallest parameter interval that is still split: its halves' ends stay exact doubles
        /// (multiples of 2^-53 in [0, 1]) </summary>
        internal const double MinSplitWidth = 1.0 / (1L << 52);

        /// <summary> tests: take the outward-rounded interval path even where the filter applies </summary>
        internal bool ForceIntervals;

        public void Set(Vector3d s0, Vector3d s1, Vector3d s2, Vector3d s3, Vector3d e0, Vector3d e1, Vector3d e2, Vector3d e3)
        {
            put(start, 0, s0); put(start, 1, s1); put(start, 2, s2); put(start, 3, s3);
            put(end, 0, e0); put(end, 1, e1); put(end, 2, e2); put(end, 3, e3);
        }

        static void put(double[] a, int i, Vector3d v) { a[3 * i] = v.x; a[3 * i + 1] = v.y; a[3 * i + 2] = v.z; }

        [MethodImpl(JitHints.Optimize)]
        public CcdResult Solve(bool vertexTriangle, CcdOptions o)
        {
            var r = new CcdResult { Status = CcdStatus.InvalidInput, TimeOfImpact = double.PositiveInfinity, NearContactTime = double.PositiveInfinity };
            if (o.IsValid() == false)
                return r;
            // finite and at most InputRange in magnitude, so that no extent, difference or corner value overflows
            for (int i = 0; i < 12; ++i)
                if ((Math.Abs(start[i]) <= InputRange) == false || (Math.Abs(end[i]) <= InputRange) == false)
                    return r;
            vf = vertexTriangle;
            ms = o.MinSeparation;
            timeLimit = o.TimeLimit;
            maxIterations = o.MaxIterations;
            iterations = 0;
            prepare_filter();
            delta = o.Tolerance;
            if (double.IsNaN(delta)) {
                // relative to the extent, but not finer than the evaluation error M 2^-47 (the computed corner values
                // carry rounding of that order, so narrower images are met only by chance: a tiny scene at large
                // coordinates hit the iteration limit)
                delta = o.RelativeTolerance * extent();
                delta = Math.Max(delta, Math.Max(errors[0], Math.Max(errors[1], errors[2])));
            }
            bool haveNear = false;
            Box near = default(Box);
            double nearWidth = 0;

            // The search keeps a partition of the domain into discarded boxes, the boxes of the queue and (once found)
            // the near box: so every point of time before the smallest start of the queue's boxes and the near box is
            // contact-free (G2), whatever the order. The order only decides how tight that time is: boxes are taken by
            // their start time; the first box that is not discarded starts a dive (depth-first, earlier half first) for
            // a near box; once one is found, only the boxes that start more than TimeTolerance before it are refined.
            count = 0; seq = 0;
            push(new Box { t0 = 0, t1 = 1, u0 = 0, u1 = 1, v0 = 0, v1 = 1 });
            int nearAt = 0;
            while (count > 0) {
                Box b = heap[0];
                if (haveNear && (b.t0 >= near.t0 - o.TimeTolerance || iterations - nearAt >= o.MaxTighteningIterations))
                    break;
                if (haveNear == false && b.t0 > o.TimeLimit)
                    break;
                if (limit_reached())
                    return stop(ref r, b.t0, haveNear, ref near, nearWidth);
                pop();
                double width;
                if (evaluate(ref b, out width))
                    continue;
                if (width <= delta) {
                    // the earliest box of the queue: tight
                    near = b; nearWidth = width; haveNear = true;
                    return finish(ref r, near.t0, ref near, nearWidth);
                }
                int split = choose_split(ref b, haveNear ? near.t0 - o.TimeTolerance : double.NaN);
                if (split < 0) {
                    // no parameter can be split further (2^-52): the box counts as near, honestly with its width
                    return finish(ref r, b.t0, ref b, width);
                }
                Box c1, c2;
                halves(ref b, split, out c1, out c2);
                if (haveNear) {
                    push(c1); push(c2);
                    continue;
                }
                // dive: follow the earlier half that is not discarded, the other half goes into the queue
                while (true) {
                    if (limit_reached()) {
                        push(c1); push(c2);
                        return stop(ref r, heap[0].t0, false, ref near, 0);
                    }
                    double w1, w2 = 0;
                    bool d1 = evaluate(ref c1, out w1);
                    Box next;
                    double wNext;
                    if (d1 == false) {
                        push(c2);
                        next = c1; wNext = w1;
                    } else {
                        if (limit_reached()) {
                            push(c2);
                            return stop(ref r, heap[0].t0, false, ref near, 0);
                        }
                        if (evaluate(ref c2, out w2))
                            break;
                        next = c2; wNext = w2;
                    }
                    if (next.t0 > o.TimeLimit) {
                        push(next);     // evaluated again when it comes up (and then stops the search)
                        break;
                    }
                    if (wNext <= delta) {
                        near = next; nearWidth = wNext; haveNear = true; nearAt = iterations;
                        break;
                    }
                    split = choose_split(ref next, double.NaN);
                    if (split < 0) {
                        near = next; nearWidth = wNext; haveNear = true; nearAt = iterations;
                        break;
                    }
                    halves(ref next, split, out c1, out c2);
                }
            }
            if (haveNear)
                return finish(ref r, count > 0 ? Math.Min(heap[0].t0, near.t0) : near.t0, ref near, nearWidth);
            r.Status = CcdStatus.NoCollision;
            r.TimeOfImpact = no_collision_time();
            r.Iterations = iterations;
            r.ErrorBound = errorBound;
            return r;
        }

        bool vf;
        double delta, ms, errorBound, timeLimit;
        int maxIterations, iterations;

        // NoCollision proves [0, TimeLimit]: the time of impact is TimeLimit, or +infinity for the whole step
        double no_collision_time() { return timeLimit < 1 ? timeLimit : double.PositiveInfinity; }

        bool limit_reached() { return maxIterations > 0 && iterations >= maxIterations; }

        static void halves(ref Box b, int split, out Box c1, out Box c2)
        {
            c1 = b; c2 = b;
            c1.depth = c2.depth = b.depth + 1;
            if (split == 0) { double m = 0.5 * (b.t0 + b.t1); c1.t1 = m; c2.t0 = m; }
            else if (split == 1) { double m = 0.5 * (b.u0 + b.u1); c1.u1 = m; c2.u0 = m; }
            else { double m = 0.5 * (b.v0 + b.v1); c1.v1 = m; c2.v0 = m; }
        }

        // true if the box is discarded: outside the vertex-triangle domain, or on some axis every corner value is
        // beyond the minimum separation; else width is an upper bound of the largest per-axis width of the corner values
        [MethodImpl(JitHints.Optimize)]
        bool evaluate(ref Box b, out double width)
        {
            width = 0;
            // the vertex-triangle domain u + v <= 1: 1 - v0 is exact (v0 a multiple of 2^-53 in [0, 1])
            if (vf && b.u0 > 1 - b.v0)
                return true;
            ++iterations;
            clip(ref b);
            for (int axis = 0; axis < 3; ++axis) {
                double threshold;
                if (useFilter) {
                    corners_filter(axis, ref b);
                    threshold = thresholds[axis];
                } else {
                    corners_interval(vf, axis, ref b);
                    threshold = ms;
                }
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                for (int k = 0; k < 8; ++k)
                    if ((insideMask & (1 << (k >> 1))) != 0) {
                        lo = Math.Min(lo, cornerLo[8 * axis + k]);
                        hi = Math.Max(hi, cornerHi[8 * axis + k]);
                    }
                for (int j = 0; j < 2 * extraCount; ++j) {
                    lo = Math.Min(lo, extraLo[4 * axis + j]);
                    hi = Math.Max(hi, extraHi[4 * axis + j]);
                }
                // every exact corner value lies in [lo - e, hi + e] (filter; threshold >= ms + e) or in [lo, hi]
                // (intervals; threshold = ms); NaN bounds never discard: the comparisons are false
                if (lo > threshold || hi < -threshold)
                    return true;
                // (hi - lo is exact when 0, so a constant image has width 0 and meets a tolerance of 0)
                double w = hi - lo;
                width = Math.Max(width, w == 0 ? 0 : ExactInterval.Up(w));
            }
            return separated_by_normals(ref b);
        }

        // The axes separate the hull of the corner values from the cube [-ms, ms]^3 only when one coordinate does; an
        // approach that is not axis-aligned then needs many small boxes. So the box is also tested along the
        // classical separating directions: the triangle's normal (b - a) x (c - a) (vertex-triangle) or the edges'
        // cross product (b - a) x (d - c) (edge-edge), at t0 and at t1. For any vector n, n . y <= ms |n|_1 on the
        // cube, and n . y is linear, so if n . F > ms |n|_1 at every vertex of the hull (or < -ms |n|_1 at every
        // one), the hull misses the cube. n needs no accuracy (any vector is valid): the dot products are evaluated in
        // outward-rounded intervals over the corner values' enclosures ([F - e, F + e] on the filter path, the
        // intervals on the interval path), so the test needs no further error bound.
        bool separated_by_normals(ref Box b)
        {
            for (int kt = 0; kt < 2; ++kt) {
                double nx, ny, nz;
                normal(kt == 0 ? b.t0 : b.t1, out nx, out ny, out nz);
                if (separated_along(nx, ny, nz))
                    return true;
            }
            // Nearly parallel edges: the cross product vanishes and the image is a thin strip along the edges'
            // direction, which no axis separates until the boxes are tiny (a query of the ToI sample needed 775 689
            // boxes). The value of F near the box's centre (the mean of the corner values; any accuracy will do) with
            // its part along an edge removed is a direction across the strip; it separates the strip as soon as the
            // strip is narrower than the distance, and choose_split splits along it. Only for nearly parallel edges:
            // elsewhere such extra directions changed the search for the worse (a later near box, a looser time).
            splitDirValid = false;
            if (vf || ParallelEdgeDirections == false)
                return false;
            double tm = 0.5 * (b.t0 + b.t1);
            double ax, ay, az, bx, by, bz;
            edges_at(tm, out ax, out ay, out az, out bx, out by, out bz);
            // (each vector scaled by a power of two to a largest component in [1, 2): exact, so the decisions are the
            // same at every 2^k scale, and no product over- or underflows)
            if (pow2_normalize(ref ax, ref ay, ref az) == false || pow2_normalize(ref bx, ref by, ref bz) == false)
                return false;
            if (nearly_parallel(ax, ay, az, bx, by, bz) == false) {
                // the cross product separates eventually; split along it
                set_split_direction(ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx);
                return false;
            }
            double cx = 0, cy = 0, cz = 0;
            for (int k = 0; k < 8; ++k) {
                cx += cornerLo[k] + cornerHi[k];
                cy += cornerLo[8 + k] + cornerHi[8 + k];
                cz += cornerLo[16 + k] + cornerHi[16 + k];
            }
            if (pow2_normalize(ref cx, ref cy, ref cz) == false)
                return false;
            for (int edge = 0; edge < 2; ++edge) {
                double ex = edge == 0 ? ax : bx, ey = edge == 0 ? ay : by, ez = edge == 0 ? az : bz;
                double f = (cx * ex + cy * ey + cz * ez) / (ex * ex + ey * ey + ez * ez);
                double nx = cx - f * ex, ny = cy - f * ey, nz = cz - f * ez;
                if (separated_along(nx, ny, nz))
                    return true;
                if (splitDirValid == false)
                    set_split_direction(nx, ny, nz);
            }
            return false;
        }

        // the direction choose_split measures along (only steers the order of the search)
        bool splitDirValid;
        double sdx, sdy, sdz;

        // the edge directions (b - a, d - c) make an angle with a sine below 0.01; both scaled by pow2_normalize (only
        // steers the search: nearly parallel edges get the directions across their strip and the split along them)
        static bool nearly_parallel(double ax, double ay, double az, double bx, double by, double bz)
        {
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            double c2 = cx * cx + cy * cy + cz * cz, a2 = ax * ax + ay * ay + az * az, b2 = bx * bx + by * by + bz * bz;
            return c2 <= 1e-4 * a2 * b2;
        }

        /// <summary> tests: off switches the directions and the split of nearly parallel edges (separated_by_normals) </summary>
        internal bool ParallelEdgeDirections = true;

        // edge-edge: the edges' vectors b - a and d - c at time t
        void edges_at(double t, out double ax, out double ay, out double az, out double bx, out double by, out double bz)
        {
            ax = at(1, 0, t) - at(0, 0, t); ay = at(1, 1, t) - at(0, 1, t); az = at(1, 2, t) - at(0, 2, t);
            bx = at(3, 0, t) - at(2, 0, t); by = at(3, 1, t) - at(2, 1, t); bz = at(3, 2, t) - at(2, 2, t);
        }

        // scale (x, y, z) by a power of two (exact) so that its largest component lies in [1, 2); false for a zero or
        // non-finite vector
        static bool pow2_normalize(ref double x, ref double y, ref double z)
        {
            double m = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if ((m > 0) == false || double.IsInfinity(m))
                return false;
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            if (e == 0) {
                // subnormal: lift by 2^600 first
                double up = BitConverter.Int64BitsToDouble((long)(1023 + 600) << 52);
                x *= up; y *= up; z *= up;
                m = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
                e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            }
            // multiply by 2^(1023 - e), in two steps when that is outside the normal exponents
            int shift = 1023 - e;
            while (shift != 0) {
                int step = Math.Max(-1000, Math.Min(1000, shift));
                double f = BitConverter.Int64BitsToDouble((long)(1023 + step) << 52);
                x *= f; y *= f; z *= f;
                shift -= step;
            }
            return true;
        }

        // the spread of n . F over the box's corner values, n the split direction (|n|_1 = 1)
        double projected_spread()
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int k = 0; k < 8; ++k) {
                double d = sdx * (cornerLo[k] + cornerHi[k]) + sdy * (cornerLo[8 + k] + cornerHi[8 + k]) + sdz * (cornerLo[16 + k] + cornerHi[16 + k]);
                lo = Math.Min(lo, d); hi = Math.Max(hi, d);
            }
            return 0.5 * (hi - lo);
        }

        void set_split_direction(double x, double y, double z)
        {
            double len = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
            if ((len > 0) == false || double.IsInfinity(len))
                return;
            splitDirValid = true;
            sdx = x / len; sdy = y / len; sdz = z / len;
        }

        // true if n . F > ms |n|_1 at every vertex of the hull (or < -ms |n|_1 at every one): then the hull misses the
        // cube [-ms, ms]^3, for any n (dot products and the bound in outward-rounded intervals)
        bool separated_along(double nx, double ny, double nz)
        {
            if (nx == 0 && ny == 0 && nz == 0 || double.IsNaN(nx + ny + nz) || double.IsInfinity(nx + ny + nz))
                return false;
            // ms |n|_1, rounded up
            ExactInterval bound = ExactInterval.Of(ms) * (ExactInterval.Of(Math.Abs(nx)) + ExactInterval.Of(Math.Abs(ny)) + ExactInterval.Of(Math.Abs(nz)));
            double limit = bound.Hi;
            bool allAbove = true, allBelow = true;
            for (int k = 0; k < 8 + 2 * extraCount && (allAbove || allBelow); ++k) {
                ExactInterval d;
                if (k < 8) {
                    if ((insideMask & (1 << (k >> 1))) == 0)
                        continue;
                    d = dot(nx, ny, nz, k, false);
                } else
                    d = dot(nx, ny, nz, k - 8, true);
                if ((d.Lo > limit) == false) allAbove = false;
                if ((d.Hi < -limit) == false) allBelow = false;
            }
            return allAbove || allBelow;
        }

        // the exact n . F at a corner (or crossing point) lies in the result
        ExactInterval dot(double nx, double ny, double nz, int k, bool extra)
        {
            ExactInterval sum = ExactInterval.Of(0);
            for (int axis = 0; axis < 3; ++axis) {
                double lo = extra ? extraLo[4 * axis + k] : cornerLo[8 * axis + k];
                double hi = extra ? extraHi[4 * axis + k] : cornerHi[8 * axis + k];
                ExactInterval f = useFilter ? new ExactInterval(lo, hi) - new ExactInterval(-errors[axis], errors[axis])
                                            : new ExactInterval(lo, hi);
                sum = sum + ExactInterval.Of(axis == 0 ? nx : axis == 1 ? ny : nz) * f;
            }
            return sum;
        }

        // the point i at time t on the axis (for the separating directions only)
        double at(int i, int axis, double t) { return start[3 * i + axis] + t * (end[3 * i + axis] - start[3 * i + axis]); }

        // the separating direction at time t (any accuracy will do)
        void normal(double t, out double nx, out double ny, out double nz)
        {
            double ax, ay, az, bx, by, bz;
            if (vf) {
                ax = at(2, 0, t) - at(1, 0, t); ay = at(2, 1, t) - at(1, 1, t); az = at(2, 2, t) - at(1, 2, t);
                bx = at(3, 0, t) - at(1, 0, t); by = at(3, 1, t) - at(1, 1, t); bz = at(3, 2, t) - at(1, 2, t);
            } else
                edges_at(t, out ax, out ay, out az, out bx, out by, out bz);
            nx = ay * bz - az * by; ny = az * bx - ax * bz; nz = ax * by - ay * bx;
        }

        // the time of impact is a lower bound; the near box gives NearContactTime (its start: its corner (t0, u0, v0) is
        // in the domain and near, G3)
        CcdResult finish(ref CcdResult r, double toi, ref Box b, double width)
        {
            LastBox[0] = b.t0; LastBox[1] = b.t1; LastBox[2] = b.u0; LastBox[3] = b.u1; LastBox[4] = b.v0; LastBox[5] = b.v1;
            r.Status = CcdStatus.Collision;
            r.TimeOfImpact = toi;
            r.NearContactTime = b.t0;
            r.ToleranceReached = width;
            r.Iterations = iterations;
            r.ErrorBound = errorBound;
            return r;
        }

        // the iteration limit: with a near box still a Collision (with a looser time of impact), else IterationLimit
        CcdResult stop(ref CcdResult r, double toi, bool haveNear, ref Box near, double nearWidth)
        {
            if (haveNear)
                return finish(ref r, Math.Min(toi, near.t0), ref near, nearWidth);
            // every box left starts after TimeLimit: [0, TimeLimit] is proven
            if (toi > timeLimit) {
                r.Status = CcdStatus.NoCollision;
                r.TimeOfImpact = no_collision_time();
                r.Iterations = iterations;
                r.ErrorBound = errorBound;
                return r;
            }
            r.Status = CcdStatus.IterationLimit;
            r.TimeOfImpact = toi;
            r.Iterations = iterations;
            r.ErrorBound = errorBound;
            return r;
        }

        /// <summary> tests: the parameter box of the last Collision (t0, t1, u0, u1, v0, v1) </summary>
        internal readonly double[] LastBox = new double[6];

        // the largest extent (max - min) per axis of the eight points; only steers the default tolerance
        double extent()
        {
            double e = 0;
            for (int axis = 0; axis < 3; ++axis) {
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                for (int i = 0; i < 4; ++i) {
                    lo = Math.Min(lo, Math.Min(start[3 * i + axis], end[3 * i + axis]));
                    hi = Math.Max(hi, Math.Max(start[3 * i + axis], end[3 * i + axis]));
                }
                e = Math.Max(e, hi - lo);
            }
            return e;
        }

        /// <summary>
        /// The bound B1 of docs/design/CCD.md, section 6.2: the computed corner value of F on one axis (corners_filter's
        /// order of operations, no FMA) differs from the exact value at the same corner by at most
        /// 64 M 2^-53 + 16 2^-1074, M the largest magnitude of the axis' eight input coordinates. The count gives
        /// 62 M u + 21 2^-1075 (u = 2^-53, second-order terms included); 64 M u = M 2^-47 is a power-of-two multiple, so
        /// e scales exactly with 2^k scaling (above the subnormal range).
        /// </summary>
        internal const double FilterRelative = 1.0 / (1L << 47);
        internal const double FilterAbsolute = 16 * double.Epsilon;
        /// <summary> above this coordinate magnitude the interval path is taken (no intermediate can overflow below it) </summary>
        internal static readonly double FilterRange = BitConverter.Int64BitsToDouble((1023L + 500) << 52);   // 2^500
        /// <summary>
        /// larger coordinates are InvalidInput: up to 2^1000 the extent (&lt;= 2^1001), the differences and the corner values
        /// (&lt;= 6 * 2^1000, also with the interval path's widening) stay finite
        /// </summary>
        public static readonly double InputRange = BitConverter.Int64BitsToDouble((1023L + 1000) << 52);   // 2^1000

        readonly double[] diff = new double[12], thresholds = new double[3], errors = new double[3];
        bool useFilter;

        void prepare_filter()
        {
            errorBound = 0;
            useFilter = ForceIntervals == false;
            for (int axis = 0; axis < 3; ++axis) {
                double m = 0;
                for (int i = 0; i < 4; ++i)
                    m = Math.Max(m, Math.Max(Math.Abs(start[3 * i + axis]), Math.Abs(end[3 * i + axis])));
                if (m > FilterRange)
                    useFilter = false;
                double e = ExactInterval.Up(m * FilterRelative + FilterAbsolute);
                errors[axis] = e;
                thresholds[axis] = ExactInterval.Up(ms + e);
                for (int i = 0; i < 4; ++i)
                    diff[3 * i + axis] = end[3 * i + axis] - start[3 * i + axis];
            }
            if (useFilter)
                errorBound = Math.Max(errors[0], Math.Max(errors[1], errors[2]));
        }

        // the (u, v) corners inside the domain (bit kuv = u bit | v bit << 1) and, for a vertex-triangle box that
        // crosses u + v = 1, the two crossing points of its boundary. 1 - u and 1 - v are exact (multiples of 2^-53 in
        // [0, 1]), so the points are exact and the inside tests too. The box's corner (u0, v0) is inside (evaluate).
        void clip(ref Box b)
        {
            insideMask = 15;
            extraCount = 0;
            if (vf == false || b.u1 <= 1 - b.v1)
                return;
            bool in1 = b.u1 <= 1 - b.v0, in2 = b.v1 <= 1 - b.u0;   // (u1, v0) and (u0, v1); (u1, v1) is outside
            insideMask = 1 | (in1 ? 2 : 0) | (in2 ? 4 : 0);
            // where the line u + v = 1 leaves the box: through the right edge (u = u1) or the bottom edge (v = v0) ...
            if (in1) { extraU[0] = b.u1; extraV[0] = 1 - b.u1; }
            else { extraU[0] = 1 - b.v0; extraV[0] = b.v0; }
            // ... and through the top edge (v = v1) or the left edge (u = u0)
            if (in2) { extraU[1] = 1 - b.v1; extraV[1] = b.v1; }
            else { extraU[1] = b.u0; extraV[1] = 1 - b.u0; }
            extraCount = 2;
        }

        // corner values of F on one axis in doubles, in the order of operations bound B1 was derived for:
        // d_i = end_i - start_i (prepare_filter), x_i = start_i + t * d_i, e0, e1, e2 differences of the x_i, and
        // F = (e0 - u * e1) - v * e2 (vertex-triangle) or (e0 + u * e1) - v * e2 (edge-edge)
        [MethodImpl(JitHints.Optimize)]
        void corners_filter(int axis, ref Box b)
        {
            double s0 = start[axis], s1 = start[3 + axis], s2 = start[6 + axis], s3 = start[9 + axis];
            double d0 = diff[axis], d1 = diff[3 + axis], d2 = diff[6 + axis], d3 = diff[9 + axis];
            for (int kt = 0; kt < 2; ++kt) {
                double t = kt == 0 ? b.t0 : b.t1;
                double x0 = s0 + t * d0, x1 = s1 + t * d1, x2 = s2 + t * d2, x3 = s3 + t * d3;
                double e0, e1, e2;
                if (vf) { e0 = x0 - x1; e1 = x2 - x1; e2 = x3 - x1; }
                else { e0 = x0 - x2; e1 = x1 - x0; e2 = x3 - x2; }
                for (int kuv = 0; kuv < 4; ++kuv) {
                    double u = (kuv & 1) == 0 ? b.u0 : b.u1, v = (kuv & 2) == 0 ? b.v0 : b.v1;
                    double f = vf ? (e0 - u * e1) - v * e2 : (e0 + u * e1) - v * e2;
                    int k = kt | (kuv << 1);
                    cornerLo[8 * axis + k] = f;
                    cornerHi[8 * axis + k] = f;
                }
                for (int j = 0; j < extraCount; ++j) {
                    double u = extraU[j], v = extraV[j];
                    double f = (e0 - u * e1) - v * e2;
                    extraLo[4 * axis + 2 * kt + j] = f;
                    extraHi[4 * axis + 2 * kt + j] = f;
                }
            }
        }

        /// <summary>
        /// tests: the computed corner values (24: corner k of axis a at 8 a + k) of the box on the filter path, and the
        /// per-axis bounds e in errorsOut; options as in Solve (MinSeparation 0)
        /// </summary>
        internal double[] FilterCornersForTests(bool vertexTriangle, double t0, double t1, double u0, double u1, double v0, double v1, double[] errorsOut)
        {
            vf = vertexTriangle; ms = 0;
            prepare_filter();
            var b = new Box { t0 = t0, t1 = t1, u0 = u0, u1 = u1, v0 = v0, v1 = v1 };
            extraCount = 0;
            for (int axis = 0; axis < 3; ++axis) {
                corners_filter(axis, ref b);
                errorsOut[axis] = errors[axis];
            }
            return (double[])cornerLo.Clone();
        }

        // corner values of F on one axis in outward-rounded interval arithmetic: each contains the exact value at the
        // exact corner (t, u, v are doubles)
        [MethodImpl(JitHints.Optimize)]
        void corners_interval(bool vertexTriangle, int axis, ref Box b)
        {
            ExactInterval s0 = ExactInterval.Of(start[axis]), s1 = ExactInterval.Of(start[3 + axis]),
                s2 = ExactInterval.Of(start[6 + axis]), s3 = ExactInterval.Of(start[9 + axis]);
            ExactInterval d0 = ExactInterval.Of(end[axis]) - s0, d1 = ExactInterval.Of(end[3 + axis]) - s1,
                d2 = ExactInterval.Of(end[6 + axis]) - s2, d3 = ExactInterval.Of(end[9 + axis]) - s3;
            ExactInterval u0 = ExactInterval.Of(b.u0), u1 = ExactInterval.Of(b.u1), v0 = ExactInterval.Of(b.v0), v1 = ExactInterval.Of(b.v1);
            for (int kt = 0; kt < 2; ++kt) {
                ExactInterval t = ExactInterval.Of(kt == 0 ? b.t0 : b.t1);
                ExactInterval x0 = s0 + t * d0, x1 = s1 + t * d1, x2 = s2 + t * d2, x3 = s3 + t * d3;
                ExactInterval e0, e1, e2;
                if (vertexTriangle) { e0 = x0 - x1; e1 = x2 - x1; e2 = x3 - x1; }
                else { e0 = x0 - x2; e1 = x1 - x0; e2 = x3 - x2; }
                for (int kuv = 0; kuv < 4; ++kuv) {
                    ExactInterval u = (kuv & 1) == 0 ? u0 : u1, v = (kuv & 2) == 0 ? v0 : v1;
                    ExactInterval f = vertexTriangle ? (e0 - u * e1) - v * e2 : (e0 + u * e1) - v * e2;
                    int k = kt | (kuv << 1);
                    cornerLo[8 * axis + k] = f.Lo;
                    cornerHi[8 * axis + k] = f.Hi;
                }
                for (int j = 0; j < extraCount; ++j) {
                    ExactInterval u = ExactInterval.Of(extraU[j]), v = ExactInterval.Of(extraV[j]);
                    ExactInterval f = (e0 - u * e1) - v * e2;
                    extraLo[4 * axis + 2 * kt + j] = f.Lo;
                    extraHi[4 * axis + 2 * kt + j] = f.Hi;
                }
            }
        }

        // The parameter (0 t, 1 u, 2 v) to split, among those still wider than MinSplitWidth; -1 if none. Only steers
        // the order of the search. In this order: a box that reaches past target (the start of the near box minus
        // TimeTolerance) is split in t, so that its part before target can be discarded; for nearly parallel edges, the
        // parameter that changes F most along the split direction separated_by_normals left for this box (it is called
        // on every box that reaches here, in evaluate, and resets the direction first), while the spread along it
        // exceeds the tolerance and the minimum separation; else the parameter whose box edges change the corner values
        // most (ties: t, then u, then v).
        int choose_split(ref Box b, double target)
        {
            if (b.t1 > target && b.t1 - b.t0 >= MinSplitWidth)
                return 0;
            // the parameter that changes F most along the box's separating direction (the direction in which the box is
            // closest to being discarded: along the edges of nearly parallel edges F changes a lot, across them little)
            // Only while the image's spread along that direction exceeds the tolerance and the minimum separation (n is
            // scaled to |n|_1 = 1, so the band of the cube along n is [-ms, ms]): below that a split along the direction
            // cannot separate the box any more, and the widths that decide a near box must shrink instead.
            if (splitDirValid && projected_spread() > Math.Max(delta, ms)) {
                int bestDir = -1;
                double bestDirValue = 0;
                for (int p = 0; p < 3; ++p) {
                    double w = p == 0 ? b.t1 - b.t0 : (p == 1 ? b.u1 - b.u0 : b.v1 - b.v0);
                    if (w < MinSplitWidth)
                        continue;
                    int bit = 1 << p;
                    double change = 0;
                    for (int k = 0; k < 8; ++k)
                        if ((k & bit) == 0) {
                            int k2 = k | bit;
                            double d = sdx * ((cornerLo[k2] + cornerHi[k2]) - (cornerLo[k] + cornerHi[k]))
                                     + sdy * ((cornerLo[8 + k2] + cornerHi[8 + k2]) - (cornerLo[8 + k] + cornerHi[8 + k]))
                                     + sdz * ((cornerLo[16 + k2] + cornerHi[16 + k2]) - (cornerLo[16 + k] + cornerHi[16 + k]));
                            double ad = Math.Abs(d);
                            if (double.IsNaN(ad)) ad = double.PositiveInfinity;
                            change = Math.Max(change, ad);
                        }
                    if (change > bestDirValue) { bestDir = p; bestDirValue = change; }
                }
                if (bestDir >= 0)
                    return bestDir;
            }
            int best = -1;
            double bestValue = -1;
            for (int p = 0; p < 3; ++p) {
                double w = p == 0 ? b.t1 - b.t0 : (p == 1 ? b.u1 - b.u0 : b.v1 - b.v0);
                if (w < MinSplitWidth)
                    continue;
                int bit = 1 << p;
                double change = 0;
                for (int axis = 0; axis < 3; ++axis)
                    for (int k = 0; k < 8; ++k)
                        if ((k & bit) == 0) {
                            double a = cornerLo[8 * axis + k] + cornerHi[8 * axis + k], c = cornerLo[8 * axis + (k | bit)] + cornerHi[8 * axis + (k | bit)];
                            double diff = Math.Abs(c - a);
                            if (double.IsNaN(diff)) diff = double.PositiveInfinity;
                            change = Math.Max(change, diff);
                        }
                if (change > bestValue) { best = p; bestValue = change; }
            }
            return best;
        }

        void push(Box b)
        {
            b.seq = seq++;
            if (count == heap.Length)
                Array.Resize(ref heap, 2 * heap.Length);
            int i = count++;
            while (i > 0) {
                int parent = (i - 1) >> 1;
                if (less(ref heap[parent], ref b))
                    break;
                heap[i] = heap[parent];
                i = parent;
            }
            heap[i] = b;
        }

        Box pop()
        {
            Box top = heap[0];
            Box last = heap[--count];
            int i = 0;
            while (true) {
                int c = 2 * i + 1;
                if (c >= count)
                    break;
                if (c + 1 < count && less(ref heap[c + 1], ref heap[c]))
                    ++c;
                if (less(ref last, ref heap[c]))
                    break;
                heap[i] = heap[c];
                i = c;
            }
            if (count > 0)
                heap[i] = last;
            return top;
        }

        static bool less(ref Box a, ref Box b)
        {
            if (a.t0 != b.t0) return a.t0 < b.t0;
            if (a.depth != b.depth) return a.depth > b.depth;   // deeper first: a contact at a point of (u, v) is reached depth-first
            return a.seq < b.seq;
        }
    }
}
