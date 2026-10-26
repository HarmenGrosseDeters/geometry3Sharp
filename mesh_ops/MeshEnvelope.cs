// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using g3;

namespace gs
{
    public enum EnvelopeStatus
    {
        /// <summary> the envelope is built </summary>
        Ready,
        /// <summary> epsilon is not a positive finite number </summary>
        InvalidEpsilon,
        /// <summary>
        /// epsilon is too small for the coordinates: rounding of the prisms would take more than a percent of
        /// it (eg epsilon below about 1e-7 of the coordinates, or near-degenerate triangles far from planar in
        /// floating point)
        /// </summary>
        TooThin,
        /// <summary> the mesh has no triangles; nothing is contained </summary>
        Empty
    }

    /// <summary>
    /// Exact containment in an envelope around a triangle mesh, after Wang, Schneider, Hu, Attene and Panozzo,
    /// "Exact and Efficient Polyhedral Envelope Containment Check" (ACM TOG 2020). Every triangle t gets a
    /// convex prism: the slab |n . (x - t)| &lt;= 0.8 epsilon along its normal, intersected with half-spaces
    /// 0.554 epsilon outside t in 14 directions perpendicular to the normal (8 uniform ones and both normals
    /// of each edge). The prism lies within t + ball(epsilon) (lateral 0.554 / cos(22.5 deg) = 0.6, and
    /// 0.6^2 + 0.8^2 = 1; the rounding of the half-spaces is taken off these offsets), and it contains t.
    ///
    /// Contains / ContainsSegment / ContainsTriangle decide exactly (BigInteger arithmetic on the double input,
    /// after floating-point filters) whether a point, segment or triangle lies in the union of the prisms. So
    /// true guarantees that every point of the query is within epsilon of the mesh, whatever the rounding; false
    /// means that some point is outside all prisms (it may still be within epsilon, as the prisms are thinner
    /// than the epsilon ball: at least 0.8 epsilon along the normal, 0.554 epsilon sideways).
    ///
    /// Triangles are tested by clipping: the query triangle, in its own (u, v) parameters, minus each nearby
    /// prism's convex cross-section, until nothing (contained) or a piece of positive area (not contained)
    /// remains. The mesh may be a soup; degenerate triangles get a prism around their longest edge (or point).
    /// The envelope is a snapshot: later changes to the mesh are not seen. Queries may run in parallel.
    /// </summary>
    public class MeshEnvelope
    {
        public DMesh3 Mesh { get; private set; }
        public double Epsilon { get; private set; }
        public EnvelopeStatus Status { get; private set; }

        /// <summary> number of queries that needed exact arithmetic (the filters could not decide) </summary>
        public int ExactQueries { get { return exactQueries; } }
        int exactQueries;

        /// <summary>
        /// (tests) after an exact triangle query that returned false: a point of the piece left uncovered (the
        /// centroid of its vertices, rounded to doubles), else null
        /// </summary>
        internal Vector3d? LastWitness;

        /// <summary> (tests) skip the floating-point filters: every query is decided exactly </summary>
        internal bool SkipFilters;

        /// <summary> slab half-thickness along the normal, as a fraction of epsilon (before rounding margins) </summary>
        public const double NormalFraction = 0.8;
        /// <summary> lateral offset of the side half-spaces, as a fraction of epsilon (0.6 cos(22.5 deg)) </summary>
        public static readonly double LateralFraction = 0.6 * Math.Cos(Math.PI / 8);

        const int MaxPlanes = 16;
        const double U = 1.1102230246251565e-16;       // unit roundoff 2^-53

        int prismCount;
        double[] planes;            // per prism MaxPlanes x (mx, my, mz, s): m . x <= s
        int[] planeCount;
        AxisAlignedBox3d[] boxes;
        TriangleHashGrid grid;

        public MeshEnvelope(DMesh3 mesh, double epsilon)
        {
            Mesh = mesh;
            Epsilon = epsilon;
            if (!(epsilon > 0) || double.IsInfinity(epsilon)) { Status = EnvelopeStatus.InvalidEpsilon; return; }
            if (mesh.TriangleCount == 0) { Status = EnvelopeStatus.Empty; return; }
            Status = build();
        }

        public int PrismCount { get { return prismCount; } }


        // ---------------------------------------------------------------- construction

        EnvelopeStatus build()
        {
            int n = Mesh.TriangleCount;
            planes = new double[n * MaxPlanes * 4];
            planeCount = new int[n];
            boxes = new AxisAlignedBox3d[n];
            double sumEdge = 0;
            int p = 0;
            Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
            foreach (int tid in Mesh.TriangleIndices()) {
                Mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
                if (build_prism(p, v0, v1, v2) == false) return EnvelopeStatus.TooThin;
                var box = new AxisAlignedBox3d(v0); box.Contain(v1); box.Contain(v2);
                // the prism is within epsilon of the triangle
                box.Expand(1.01 * Epsilon);
                boxes[p] = box;
                sumEdge += v0.Distance(v1) + v1.Distance(v2) + v2.Distance(v0);
                p++;
            }
            prismCount = p;
            double cell = Math.Max(sumEdge / (3 * p) + 2 * Epsilon, Epsilon);
            grid = new TriangleHashGrid(Mesh.CachedBounds.Min, cell);
            for (int k = 0; k < p; ++k) grid.Insert(k, boxes[k]);
            return EnvelopeStatus.Ready;
        }

        bool build_prism(int p, Vector3d v0, Vector3d v1, Vector3d v2)
        {
            Vector3d[] v = { v0, v1, v2 };
            Vector3d[] e = { v1 - v0, v2 - v1, v0 - v2 };
            int L = 0;
            for (int k = 1; k < 3; ++k) if (e[k].LengthSquared > e[L].LengthSquared) L = k;
            Vector3d u1 = e[L].LengthSquared > 0 ? e[L].Normalized : Vector3d.AxisX;
            Vector3d nrm = e[0].Cross(v2 - v0);
            Vector3d nh;
            if (nrm.LengthSquared > 0 && nrm.IsFinite) nh = nrm.Normalized;
            else {
                // a segment or a point: any plane through it
                Vector3d axis = Math.Abs(u1.x) <= Math.Abs(u1.y) && Math.Abs(u1.x) <= Math.Abs(u1.z) ? Vector3d.AxisX
                    : Math.Abs(u1.y) <= Math.Abs(u1.z) ? Vector3d.AxisY : Vector3d.AxisZ;
                nh = u1.Cross(axis).Normalized;
            }
            Vector3d u2 = nh.Cross(u1).Normalized;
            u1 = u2.Cross(nh).Normalized;

            // rounding margin: errors of the dot products and offsets, relative to the coordinates, and the
            // spread of the triangle along the (rounded) normal
            double M = 0;
            foreach (Vector3d q in v) M = Math.Max(M, Math.Max(Math.Abs(q.x), Math.Max(Math.Abs(q.y), Math.Abs(q.z))));
            M = Math.Max(M, Epsilon);
            double margin = 64 * U * M;
            double h0 = nh.Dot(v0), h1 = nh.Dot(v1), h2 = nh.Dot(v2);
            double spread = Math.Max(h0, Math.Max(h1, h2)) - Math.Min(h0, Math.Min(h1, h2));
            double normalOffset = NormalFraction * Epsilon - spread - margin;
            double lateralOffset = LateralFraction * Epsilon - margin;
            if (!(normalOffset > 0.5 * NormalFraction * Epsilon) || !(lateralOffset > 0.99 * LateralFraction * Epsilon))
                return false;

            int c = 0;
            add_plane(p, ref c, nh, v, normalOffset);
            add_plane(p, ref c, -nh, v, normalOffset);
            for (int k = 0; k < 8; ++k) {
                double a = k * Math.PI / 4;
                add_plane(p, ref c, Math.Cos(a) * u1 + Math.Sin(a) * u2, v, lateralOffset);
            }
            for (int k = 0; k < 3; ++k) {
                Vector3d w = nh.Cross(e[k]);
                if (!(w.LengthSquared > 0)) continue;
                w = w.Normalized;
                add_plane(p, ref c, w, v, lateralOffset);
                add_plane(p, ref c, -w, v, lateralOffset);
            }
            planeCount[p] = c;
            return true;
        }

        // half-space m . x <= max_j m . v_j + offset, with the offset raised by the rounding error of the dot
        // products, so that the triangle is inside exactly
        void add_plane(int p, ref int c, Vector3d m, Vector3d[] v, double offset)
        {
            double maxd = double.MinValue, mag = 0;
            foreach (Vector3d q in v) {
                maxd = Math.Max(maxd, m.Dot(q));
                mag = Math.Max(mag, Math.Abs(m.x * q.x) + Math.Abs(m.y * q.y) + Math.Abs(m.z * q.z));
            }
            double s = maxd + 8 * U * mag + offset;
            s += 4 * U * Math.Abs(s);
            int o = (p * MaxPlanes + c) * 4;
            planes[o] = m.x; planes[o + 1] = m.y; planes[o + 2] = m.z; planes[o + 3] = s;
            c++;
        }


        // ---------------------------------------------------------------- queries

        /// <summary> is the point in the envelope? </summary>
        public bool Contains(Vector3d q)
        {
            if (Status != EnvelopeStatus.Ready) return false;
            List<int> cand = candidates(new AxisAlignedBox3d(q));
            bool uncertain = SkipFilters;
            if (SkipFilters == false) foreach (int p in cand) {
                int r = classify_fast(p, q);
                if (r < 0) return true;
                if (r == 0) uncertain = true;
            }
            if (uncertain == false) return false;
            System.Threading.Interlocked.Increment(ref exactQueries);
            foreach (int p in cand)
                if (classify_exact(p, q) <= 0) return true;
            return false;
        }

        /// <summary> is the whole segment in the envelope? </summary>
        public bool ContainsSegment(Vector3d a, Vector3d b)
        {
            if (Status != EnvelopeStatus.Ready) return false;
            var box = new AxisAlignedBox3d(a); box.Contain(b);
            List<int> cand = candidates(box);
            Vector3d[] q = { a, b };
            bool decided = false;
            bool r = SkipFilters ? false : fast_filters(cand, q, out decided);
            if (decided) return r;
            System.Threading.Interlocked.Increment(ref exactQueries);
            return segment_exact(cand, a, b);
        }

        /// <summary> is the whole triangle in the envelope? (also for degenerate triangles) </summary>
        public bool ContainsTriangle(Vector3d a, Vector3d b, Vector3d c)
        {
            if (Status != EnvelopeStatus.Ready) return false;
            var box = new AxisAlignedBox3d(a); box.Contain(b); box.Contain(c);
            List<int> cand = candidates(box);
            Vector3d[] q = { a, b, c };
            bool decided = false;
            bool r = SkipFilters ? false : fast_filters(cand, q, out decided);
            if (decided) return r;
            System.Threading.Interlocked.Increment(ref exactQueries);
            LastWitness = null;
            // (also for degenerate triangles: the parameter triangle in (u, v) is never degenerate, and the
            // query is contained if and only if the preimages of the prisms cover it)
            return triangle_exact(cand, a, b, c);
        }

        List<int> candidates(AxisAlignedBox3d box)
        {
            var list = new List<int>();
            grid.Query(box, (k) => { if (boxes[k].Intersects(box)) list.Add(k); });
            list.Sort();        // deterministic order, independent of the grid's hashing
            return list;
        }

        // -1: certainly inside prism p, 1: certainly outside, 0: undecided by floating point
        int classify_fast(int p, Vector3d q)
        {
            bool inside = true;
            int o = p * MaxPlanes * 4;
            for (int k = 0; k < planeCount[p]; ++k, o += 4) {
                double mx = planes[o], my = planes[o + 1], mz = planes[o + 2], s = planes[o + 3];
                double val = mx * q.x + my * q.y + mz * q.z - s;
                double err = 8 * U * (Math.Abs(mx * q.x) + Math.Abs(my * q.y) + Math.Abs(mz * q.z) + Math.Abs(s));
                if (val > err) return 1;
                if (!(val < -err)) inside = false;
            }
            return inside ? -1 : 0;
        }

        // decides with floating point if possible: true if one prism certainly contains all points (convex),
        // false if one point is certainly outside every candidate prism
        bool fast_filters(List<int> cand, Vector3d[] q, out bool decided)
        {
            decided = true;
            var outsideAll = new bool[q.Length];
            for (int i = 0; i < q.Length; ++i) outsideAll[i] = true;
            foreach (int p in cand) {
                bool all = true;
                for (int i = 0; i < q.Length; ++i) {
                    int r = classify_fast(p, q[i]);
                    if (r != -1) all = false;
                    if (r != 1) outsideAll[i] = false;
                }
                if (all) return true;
            }
            for (int i = 0; i < q.Length; ++i)
                if (outsideAll[i]) return false;
            decided = false;
            return false;
        }


        // ---------------------------------------------------------------- exact arithmetic
        //
        // Doubles are dyadic rationals m 2^e; sums of products of them are exact as such. A half-space in the
        // parameters of a query (a point, a segment a + t (b - a), a triangle q0 + u (q1 - q0) + v (q2 - q0))
        // becomes a linear inequality with integer coefficients after scaling all its terms to one exponent.

        struct Dy
        {
            public BigInteger M;
            public int E;
            public static Dy Of(double x)
            {
                BigInteger m; int e;
                ExactPoint3.Decompose(x, out m, out e);
                return new Dy() { M = m, E = e };
            }
            public static Dy operator *(Dy a, Dy b) { return new Dy() { M = a.M * b.M, E = a.E + b.E }; }
            public static Dy operator -(Dy a) { return new Dy() { M = -a.M, E = a.E }; }
            public static Dy operator +(Dy a, Dy b)
            {
                if (a.M.IsZero) return b;
                if (b.M.IsZero) return a;
                if (a.E <= b.E) return new Dy() { M = a.M + (b.M << (b.E - a.E)), E = a.E };
                return new Dy() { M = (a.M << (a.E - b.E)) + b.M, E = b.E };
            }
            public static Dy operator -(Dy a, Dy b) { return a + (-b); }
        }

        static Dy dot(Dy[] m, Vector3d q)
        {
            return m[0] * Dy.Of(q.x) + m[1] * Dy.Of(q.y) + m[2] * Dy.Of(q.z);
        }

        Dy[] plane_normal(int p, int k, out Dy s)
        {
            int o = (p * MaxPlanes + k) * 4;
            s = Dy.Of(planes[o + 3]);
            return new[] { Dy.Of(planes[o]), Dy.Of(planes[o + 1]), Dy.Of(planes[o + 2]) };
        }

        // the values as integers with a common power of two (only their ratios matter)
        static BigInteger[] common(params Dy[] d)
        {
            int e = int.MaxValue;
            foreach (Dy x in d) if (x.M.IsZero == false) e = Math.Min(e, x.E);
            var r = new BigInteger[d.Length];
            for (int i = 0; i < d.Length; ++i)
                r[i] = d[i].M.IsZero ? BigInteger.Zero : d[i].M << (d[i].E - e);
            return r;
        }

        // sign of m . q - s for the worst half-space of prism p: <= 0 inside (closed), > 0 outside
        int classify_exact(int p, Vector3d q)
        {
            int worst = -1;
            for (int k = 0; k < planeCount[p]; ++k) {
                Dy s;
                Dy[] m = plane_normal(p, k, out s);
                int sign = (dot(m, q) - s).M.Sign;
                if (sign > 0) return 1;
                worst = Math.Max(worst, sign);
            }
            return worst;
        }

        // ---- segments: pieces of the parameter interval [0, 1] as rationals lo = ln / ld, hi = hn / hd

        struct Interval { public BigInteger LN, LD, HN, HD; }

        static bool less(BigInteger an, BigInteger ad, BigInteger bn, BigInteger bd) { return an * bd < bn * ad; }

        // part of the interval with A t <= C (keep = true) or A t >= C (keep = false); null if of zero length
        static Interval? clip(Interval iv, BigInteger A, BigInteger C, bool keep)
        {
            if (keep == false) { A = -A; C = -C; }
            if (A.IsZero) return C.Sign >= 0 ? iv : (Interval?)null;
            BigInteger tn = C, td = A;
            if (td.Sign < 0) { tn = -tn; td = -td; }
            if (A.Sign > 0) {       // t <= tn / td
                if (less(tn, td, iv.HN, iv.HD)) { iv.HN = tn; iv.HD = td; }
            } else {                // t >= tn / td
                if (less(iv.LN, iv.LD, tn, td)) { iv.LN = tn; iv.LD = td; }
            }
            return less(iv.LN, iv.LD, iv.HN, iv.HD) ? iv : (Interval?)null;
        }

        bool segment_exact(List<int> cand, Vector3d a, Vector3d b)
        {
            if (a == b) {
                foreach (int p in cand) if (classify_exact(p, a) <= 0) return true;
                return false;
            }
            var pieces = new List<Interval>() { new Interval() { LN = 0, LD = 1, HN = 1, HD = 1 } };
            foreach (int p in cand) {
                // half-spaces m . (a + t (b - a)) <= s:  t m.(b - a) <= s - m.a
                var lines = new List<BigInteger[]>();
                bool empty = false;
                for (int k = 0; k < planeCount[p] && empty == false; ++k) {
                    Dy s;
                    Dy[] m = plane_normal(p, k, out s);
                    Dy ma = dot(m, a);
                    BigInteger[] AC = common(dot(m, b) - ma, s - ma);
                    if (AC[0].IsZero) { if (AC[1].Sign < 0) empty = true; continue; }
                    lines.Add(AC);
                }
                if (empty) continue;
                var next = new List<Interval>();
                foreach (Interval piece in pieces) {
                    Interval? cur = piece;
                    foreach (BigInteger[] AC in lines) {
                        Interval? outside = clip(cur.Value, AC[0], AC[1], false);
                        if (outside.HasValue) next.Add(outside.Value);
                        cur = clip(cur.Value, AC[0], AC[1], true);
                        if (cur.HasValue == false) break;
                    }
                }
                pieces = next;
                if (pieces.Count == 0) return true;
            }
            return false;
        }

        // ---- triangles: convex polygons in (u, v) as cyclic lists of lines A u + B v <= C (inside), counter-
        // clockwise; vertex i is the intersection of lines i - 1 and i

        // intersection of two lines as homogeneous (X, Y, W), W > 0; W = 0 if parallel
        static void meet(BigInteger[] l1, BigInteger[] l2, out BigInteger X, out BigInteger Y, out BigInteger W)
        {
            W = l1[0] * l2[1] - l2[0] * l1[1];
            X = l1[2] * l2[1] - l2[2] * l1[1];
            Y = l1[0] * l2[2] - l2[0] * l1[2];
            if (W.Sign < 0) { W = -W; X = -X; Y = -Y; }
        }

        // the part of a convex polygon of positive area with A u + B v <= C; null if that part has zero area
        // (then no vertex is strictly inside). The result has positive area if not null.
        static List<BigInteger[]> clip(List<BigInteger[]> poly, BigInteger[] h)
        {
            int n = poly.Count;
            var F = new int[n];
            bool anyNeg = false, anyPos = false;
            for (int i = 0; i < n; ++i) {
                BigInteger X, Y, W;
                meet(poly[(i + n - 1) % n], poly[i], out X, out Y, out W);
                if (W.IsZero) throw new InvalidOperationException("MeshEnvelope: degenerate clip polygon");
                F[i] = (h[0] * X + h[1] * Y - h[2] * W).Sign;
                if (F[i] < 0) anyNeg = true;
                if (F[i] > 0) anyPos = true;
            }
            if (anyPos == false) return poly;
            if (anyNeg == false) return null;
            // edge i runs from vertex i to vertex i + 1; it stays if one of its ends is strictly inside. h goes
            // in once, in place of the run of vertices outside, before the first kept edge after it.
            int start = 0;
            while (F[start] >= 0) start++;
            var res = new List<BigInteger[]>(n + 1);
            bool pending = false, inserted = false;
            for (int k = 0; k < n; ++k) {
                int i = (start + k) % n;
                bool kept = F[i] < 0 || F[(i + 1) % n] < 0;
                if (kept) {
                    if (pending && inserted == false) { res.Add(h); inserted = true; }
                    res.Add(poly[i]);
                }
                if (F[(i + 1) % n] > 0) pending = true;
            }
            if (pending && inserted == false) res.Add(h);
            return res;
        }

        // is there a half-space with no vertex of the polygon strictly inside (so that the polygon and the
        // convex region of the half-spaces meet in zero area)?
        static bool separated(List<BigInteger[]> poly, List<BigInteger[]> lines)
        {
            int n = poly.Count;
            var X = new BigInteger[n]; var Y = new BigInteger[n]; var W = new BigInteger[n];
            for (int i = 0; i < n; ++i) meet(poly[(i + n - 1) % n], poly[i], out X[i], out Y[i], out W[i]);
            foreach (BigInteger[] h in lines) {
                bool anyInside = false;
                for (int i = 0; i < n && anyInside == false; ++i)
                    if ((h[0] * X[i] + h[1] * Y[i] - h[2] * W[i]).Sign < 0) anyInside = true;
                if (anyInside == false) return true;
            }
            return false;
        }

        static BigInteger[] negate(BigInteger[] l) { return new[] { -l[0], -l[1], -l[2] }; }

        bool triangle_exact(List<int> cand, Vector3d a, Vector3d b, Vector3d c)
        {
            // the triangle: v >= 0, u + v <= 1, u >= 0 (counter-clockwise in (u, v))
            var tri = new List<BigInteger[]>() {
                new BigInteger[] { 0, -1, 0 }, new BigInteger[] { 1, 1, 1 }, new BigInteger[] { -1, 0, 0 } };
            var pieces = new List<List<BigInteger[]>>() { tri };
            // prisms that contain more corners first: they remove more at once
            var order = new List<int>(cand);
            var score = new Dictionary<int, int>();
            foreach (int p in cand) {
                int sc = 0;
                foreach (Vector3d q in new[] { a, b, c }) if (classify_fast(p, q) <= 0) sc++;
                score[p] = sc;
            }
            order.Sort((x, y) => score[x] != score[y] ? score[y].CompareTo(score[x]) : x.CompareTo(y));
            foreach (int p in order) {
                // m . (a + u (b - a) + v (c - a)) <= s:  u m.(b - a) + v m.(c - a) <= s - m.a
                var lines = new List<BigInteger[]>();
                bool empty = false;
                for (int k = 0; k < planeCount[p] && empty == false; ++k) {
                    Dy s;
                    Dy[] m = plane_normal(p, k, out s);
                    Dy ma = dot(m, a);
                    BigInteger[] ABC = common(dot(m, b) - ma, dot(m, c) - ma, s - ma);
                    if (ABC[0].IsZero && ABC[1].IsZero) { if (ABC[2].Sign < 0) empty = true; continue; }
                    lines.Add(reduce(ABC));
                }
                if (empty) continue;
                var next = new List<List<BigInteger[]>>();
                foreach (var piece in pieces) {
                    // a piece the prism does not reach stays whole (instead of being cut up by the
                    // half-spaces before the separating one)
                    if (separated(piece, lines)) { next.Add(piece); continue; }
                    List<BigInteger[]> cur = piece;
                    foreach (BigInteger[] h in lines) {
                        List<BigInteger[]> outside = clip(cur, negate(h));
                        if (outside != null) next.Add(outside);
                        cur = clip(cur, h);
                        if (cur == null) break;
                    }
                }
                pieces = next;
                if (pieces.Count == 0) return true;
            }
            LastWitness = centroid(pieces[0], a, b, c);
            return false;
        }

        static Vector3d centroid(List<BigInteger[]> poly, Vector3d a, Vector3d b, Vector3d c)
        {
            double u = 0, v = 0;
            int n = poly.Count;
            for (int i = 0; i < n; ++i) {
                BigInteger X, Y, W;
                meet(poly[(i + n - 1) % n], poly[i], out X, out Y, out W);
                u += ratio(X, W); v += ratio(Y, W);
            }
            u /= n; v /= n;
            return a + u * (b - a) + v * (c - a);
        }

        static double ratio(BigInteger num, BigInteger den)
        {
            // scale both into the double range before dividing
            int shift = (int)Math.Max(0, Math.Max(ExactPoint3.BitLength(BigInteger.Abs(num)), ExactPoint3.BitLength(den)) - 1000);
            return (double)(num >> shift) / (double)(den >> shift);
        }

        static BigInteger[] reduce(BigInteger[] l)
        {
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(l[0], l[1]), l[2]);
            if (g.IsZero || g.IsOne) return l;
            return new[] { l[0] / g, l[1] / g, l[2] / g };
        }
    }
}
