// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using g3;

namespace gs
{
    /// <summary>
    /// Exact containment in the polyhedral envelope of Wang, Schneider, Hu, Attene and Panozzo, "Exact and Efficient
    /// Polyhedral Envelope Containment Check" (ACM TOG 2020), docs/design/ENVELOPE.md. Every triangle gets the open
    /// convex polyhedron of the paper's Prop. 3.1 (offset delta = epsilon / sqrt 3: floor and ceiling, three side
    /// planes, caps at the acute and right vertices; a box around the longest edge or the point for a degenerate
    /// triangle). Each facet is an oriented plane through three double points.
    ///
    /// The rounding of those points is taken off the offsets with a proven bound (ENVELOPE.md section 4.4): every
    /// point of every polyhedron is within epsilon of its triangle. That the triangle is strictly inside its
    /// polyhedron is checked exactly at build. Queries decide exactly whether a point or a segment lies in the union
    /// of the polyhedra (Thm. 3.2: the endpoints are inside, and every point where the segment crosses a facet plane
    /// of a polyhedron lies in another one), with ExactPredicates.Orient3d and IndirectPredicates.Orient3dLPI. The
    /// boundary counts as outside. True guarantees that every point of the query is within epsilon of the mesh.
    ///
    /// The polyhedra are a snapshot of the mesh (Mesh is the given instance; later changes to it are not seen); queries
    /// may run in parallel. One triangle that cannot get a polyhedron (TooThin, OutOfRange) makes the status of the
    /// whole envelope that, as in MeshEnvelope: a partial envelope would answer false for its region.
    /// </summary>
    internal sealed class PolyhedralEnvelope
    {
        public DMesh3 Mesh { get; private set; }
        public double Epsilon { get; private set; }
        public EnvelopeStatus Status { get; private set; }
        public int PolyhedronCount { get { return count; } }

        internal const int MaxFacets = 8;
        const double U = 1.1102230246251565e-16;       // unit roundoff 2^-53
        const double Lambda = 2;                        // ENVELOPE.md 4.4, Lemma 2
        const double Tiny = 1e-300;

        // direction errors (ENVELOPE.md 4.4), in units of U, each one more than derived for the O(u^2) terms
        internal const double ThetaNormal = 9, ThetaSide = 29, ThetaCap = 11, ThetaAlong = 7, ThetaPerp1 = 20,
            ThetaPerp2 = 61, ThetaAxis = 0;

        int count;
        Vector3d[] facets;          // per polyhedron MaxFacets x 3 points: strictly inside means Orient3d < 0
        int[] facetCount;
        TriangleBoxTree tree;
        AxisAlignedBox3d[] boxes;

        public PolyhedralEnvelope(DMesh3 mesh, double epsilon)
        {
            Mesh = mesh;
            Epsilon = epsilon;
            if (!(epsilon > 0) || double.IsInfinity(epsilon)) { Status = EnvelopeStatus.InvalidEpsilon; return; }
            if (mesh.TriangleCount == 0) { Status = EnvelopeStatus.Empty; return; }
            if (epsilon < 1e-280) { Status = EnvelopeStatus.TooThin; return; }
            if (mesh.TriangleCount > int.MaxValue / (MaxFacets * 3)) { Status = EnvelopeStatus.TooLarge; return; }
            Status = build();
        }


        // ---------------------------------------------------------------- construction

        EnvelopeStatus build()
        {
            int n = Mesh.TriangleCount;
            facets = new Vector3d[n * MaxFacets * 3];
            facetCount = new int[n];
            var boxes = new List<AxisAlignedBox3d>(n);
            Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
            foreach (int tid in Mesh.TriangleIndices()) {
                Mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
                var b = new Builder(Epsilon, v0, v1, v2);
                EnvelopeStatus st = b.Build(facets, count * MaxFacets * 3, out facetCount[count]);
                if (st != EnvelopeStatus.Ready) return st;
                AxisAlignedBox3d box;
                if (search_box(v0, v1, v2, out box) == false) return EnvelopeStatus.OutOfRange;
                boxes.Add(box);
                count++;
            }
            tree = new TriangleBoxTree(boxes);
            this.boxes = boxes.ToArray();
            return EnvelopeStatus.Ready;
        }

        // the box of the triangle expanded by epsilon and the rounding (the polyhedron is within epsilon of it)
        bool search_box(Vector3d v0, Vector3d v1, Vector3d v2, out AxisAlignedBox3d box)
        {
            box = new AxisAlignedBox3d(v0); box.Contain(v1); box.Contain(v2);
            double M = Math.Max(norm_inf(box.Min), norm_inf(box.Max));
            double pad = Epsilon * (1 + 4 * U) + 4 * U * M;
            box.Expand(pad);
            return finite(box.Min) && finite(box.Max);
        }

        internal static double norm_inf(Vector3d v) { return Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))); }

        // all components finite (Vector3d.IsFinite tests their sum, which overflows)
        internal static bool finite(Vector3d v)
        {
            return !(double.IsNaN(v.x) || double.IsInfinity(v.x) || double.IsNaN(v.y) || double.IsInfinity(v.y)
                || double.IsNaN(v.z) || double.IsInfinity(v.z));
        }

        /// <summary>
        /// the polyhedron of one triangle (ENVELOPE.md 4.4); internal for the tests, which check its bounds a
        /// posteriori
        /// </summary>
        internal struct Builder
        {
            internal readonly double eps, delta, rho, R, S, cinf;
            readonly Vector3d v0, v1, v2;
            Vector3d[] outp;
            int outBase, nf;
            EnvelopeStatus status;

            /// <summary> (tests) per facet: its direction, theta (absolute), offset, margin and mu </summary>
            internal List<Facet> Log;
            internal struct Facet { public Vector3d M; public double Theta, S, Margin, Mu; }

            public Builder(double epsilon, Vector3d a, Vector3d b, Vector3d c)
            {
                eps = epsilon; v0 = a; v1 = b; v2 = c;
                // delta < epsilon / sqrt 3 (three roundings: the constant, the two products; 1 - 4 u covers them)
                delta = epsilon * 0.57735026918962573 * (1 - 4 * U);
                double r = Math.Max(length(v1 - v0), length(v2 - v0));
                rho = r * (1 + 8 * U);
                R = (rho + 2 * epsilon) * (1 + 4 * U);
                S = 4 * R;
                cinf = norm_inf(v0);
                outp = null; outBase = 0; nf = 0; status = EnvelopeStatus.Ready; Log = null;
            }

            public EnvelopeStatus Build(Vector3d[] dst, int offset, out int facetCount)
            {
                outp = dst; outBase = offset; nf = 0;
                facetCount = 0;
                Vector3d[] v = { v0, v1, v2 };
                foreach (Vector3d q in v) if (finite(q) == false) return EnvelopeStatus.OutOfRange;
                Vector3d[] e = { v1 - v0, v2 - v1, v0 - v2 };
                foreach (Vector3d q in e) if (finite(q) == false) return EnvelopeStatus.OutOfRange;
                // every dot product of the facets' points and the vertices stays finite
                if (!(R < double.MaxValue / 64) || !(3 * (cinf + 8 * R) < double.MaxValue))
                    return EnvelopeStatus.OutOfRange;

                Vector3d N;
                if (exact_normal(v0, v1, v2, out N)) {
                    Vector3d n;
                    unit(N, out n);
                    add(n, ThetaNormal * U);
                    add(-n, ThetaNormal * U);
                    for (int k = 0; k < 3; ++k) {
                        Vector3d w;
                        if (unit(scaled(e[k]).Cross(n), out w) == false) return EnvelopeStatus.OutOfRange;
                        add(w, ThetaSide * U);
                    }
                    for (int i = 0; i < 3; ++i) {
                        // (scaled by one power of two: no overflow or underflow in the squares)
                        Vector3d a = v[(i + 1) % 3] - v[i], b = v[(i + 2) % 3] - v[i];
                        int se = exponent(Math.Max(norm_inf(a), norm_inf(b)));
                        a = scale(a, -se); b = scale(b, -se);
                        // every acute and right vertex, maybe some obtuse ones (ENVELOPE.md 4.4, caps)
                        if (!(a.Dot(b) >= -(a.LengthSquared + b.LengthSquared) / 4)) continue;
                        Vector3d m;
                        if (unit(-(a + b), out m) == false) return EnvelopeStatus.OutOfRange;
                        add(m, ThetaCap * U);
                    }
                } else {
                    // degenerate: all vertices on one line, or one point. Any edge of non-zero length gives the line
                    // (the offsets are T's support, so the box covers all three vertices); the longest one by the
                    // scaled length (squares of tiny edges underflow), the cube only if all edges are exactly zero
                    int L = 0;
                    for (int k = 1; k < 3; ++k) if (length(e[k]) > length(e[L])) L = k;
                    Vector3d d;
                    if (length(e[L]) > 0) {
                        unit(e[L], out d);
                        Vector3d axis = Math.Abs(d.x) <= Math.Abs(d.y) && Math.Abs(d.x) <= Math.Abs(d.z) ? Vector3d.AxisX
                            : Math.Abs(d.y) <= Math.Abs(d.z) ? Vector3d.AxisY : Vector3d.AxisZ;
                        Vector3d p1, p2;
                        unit(d.Cross(axis), out p1);
                        unit(d.Cross(p1), out p2);
                        add(d, ThetaAlong * U); add(-d, ThetaAlong * U);
                        add(p1, ThetaPerp1 * U); add(-p1, ThetaPerp1 * U);
                        add(p2, ThetaPerp2 * U); add(-p2, ThetaPerp2 * U);
                    } else {
                        add(Vector3d.AxisX, ThetaAxis); add(-Vector3d.AxisX, ThetaAxis);
                        add(Vector3d.AxisY, ThetaAxis); add(-Vector3d.AxisY, ThetaAxis);
                        add(Vector3d.AxisZ, ThetaAxis); add(-Vector3d.AxisZ, ThetaAxis);
                    }
                }
                if (status != EnvelopeStatus.Ready) return status;
                // the triangle strictly inside, decided exactly
                for (int f = 0; f < nf; ++f) {
                    int o = outBase + 3 * f;
                    foreach (Vector3d q in v)
                        if (ExactPredicates.Orient3d(outp[o], outp[o + 1], outp[o + 2], q) >= 0)
                            return EnvelopeStatus.TooThin;
                }
                facetCount = nf;
                return EnvelopeStatus.Ready;
            }

            // the facet {x : m . x < s} through three double points, s reduced by the margins of section 4.4
            void add(Vector3d m, double theta)
            {
                if (status != EnvelopeStatus.Ready) return;
                double h = double.NegativeInfinity, mag = 0;
                foreach (Vector3d q in new[] { v0, v1, v2 }) {
                    h = Math.Max(h, m.x * q.x + m.y * q.y + m.z * q.z);
                    mag = Math.Max(mag, Math.Abs(m.x * q.x) + Math.Abs(m.y * q.y) + Math.Abs(m.z * q.z));
                }
                // (Tiny: products that underflow, at most 2^-1074 each)
                double eta = 4 * U * mag + Tiny;
                double muPlan = 32 * U * (cinf + 4 * R);
                double margin = theta * (R + rho) + Lambda * muPlan + eta;
                double corr = 4 * U * (Math.Abs(h) + delta);
                if (!(margin + corr <= delta / 10)) { status = EnvelopeStatus.TooThin; return; }
                double s = h + (delta - margin - corr);

                // three points: o on the plane, o + S a, o + S b with a, b orthogonal to m
                Vector3d c = v0;
                double t = (s - m.Dot(c)) / m.LengthSquared;
                Vector3d o = c + t * m;
                Vector3d axis = Math.Abs(m.x) <= Math.Abs(m.y) && Math.Abs(m.x) <= Math.Abs(m.z) ? Vector3d.AxisX
                    : Math.Abs(m.y) <= Math.Abs(m.z) ? Vector3d.AxisY : Vector3d.AxisZ;
                Vector3d a, b;
                unit(m.Cross(axis), out a);
                b = m.Cross(a);
                Vector3d q0 = o, q1 = o + S * a, q2 = o + S * b;
                // Lemma 2's conditions: the rounding of the points small against R
                double qinf = Math.Max(norm_inf(q0), Math.Max(norm_inf(q1), norm_inf(q2)));
                if (!(1.7320508075688772 * U * qinf <= R / 200) || finite(q1) == false || finite(q2) == false) {
                    status = EnvelopeStatus.TooThin; return;
                }
                double mu = 0;
                foreach (Vector3d q in new[] { q0, q1, q2 }) {
                    double r = m.x * q.x + m.y * q.y + m.z * q.z - s;
                    double bound = 5 * U * (Math.Abs(m.x * q.x) + Math.Abs(m.y * q.y) + Math.Abs(m.z * q.z) + Math.Abs(s));
                    mu = Math.Max(mu, Math.Abs(r) + bound + Tiny);
                }
                if (!(mu <= muPlan)) { status = EnvelopeStatus.TooThin; return; }
                if (nf >= MaxFacets) throw new InvalidOperationException("PolyhedralEnvelope: too many facets");
                int k = outBase + 3 * nf;
                outp[k] = q0; outp[k + 1] = q1; outp[k + 2] = q2;
                nf++;
                if (Log != null) Log.Add(new Facet() { M = m, Theta = theta, S = s, Margin = margin + corr, Mu = mu });
            }
        }

        // length without overflow or underflow of the squares (scaled by a power of two)
        internal static double length(Vector3d v)
        {
            double m = norm_inf(v);
            if (!(m > 0) || double.IsInfinity(m)) return m;
            int e = exponent(m);
            Vector3d w = new Vector3d(ExactPoint3.ScaleB(v.x, -e), ExactPoint3.ScaleB(v.y, -e), ExactPoint3.ScaleB(v.z, -e));
            return ExactPoint3.ScaleB(w.Length, e);
        }

        static Vector3d scale(Vector3d v, int e)
        {
            return new Vector3d(ExactPoint3.ScaleB(v.x, e), ExactPoint3.ScaleB(v.y, e), ExactPoint3.ScaleB(v.z, e));
        }

        // v times a power of two with its largest component in [1, 2) (v != 0, finite)
        static Vector3d scaled(Vector3d v) { return scale(v, -exponent(norm_inf(v))); }

        // floor(log2 |x|) for finite x != 0
        static int exponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            if (be != 0) return be - 1023;
            return exponent(x * 18446744073709551616.0) - 64;     // subnormal
        }

        /// <summary>
        /// unit vector along v, false if v is zero or not finite: scaled by a power of two first (exact), then divided
        /// by its rounded length (|unit(v) - v / |v|| &lt;= 4 u, ENVELOPE.md 4.4)
        /// </summary>
        internal static bool unit(Vector3d v, out Vector3d u)
        {
            u = Vector3d.Zero;
            double m = norm_inf(v);
            if (!(m > 0) || double.IsInfinity(m)) return false;
            int e = exponent(m);
            Vector3d w = new Vector3d(ExactPoint3.ScaleB(v.x, -e), ExactPoint3.ScaleB(v.y, -e), ExactPoint3.ScaleB(v.z, -e));
            double l = Math.Sqrt(w.x * w.x + w.y * w.y + w.z * w.z);
            u = new Vector3d(w.x / l, w.y / l, w.z / l);
            return true;
        }

        /// <summary>
        /// N = (b - a) x (c - a) computed exactly and rounded to doubles (|N~ - N| &lt; 1.02 u |N| in norm; N up to a
        /// positive power of two), false if N = 0 exactly
        /// </summary>
        internal static bool exact_normal(Vector3d a, Vector3d b, Vector3d c, out Vector3d N)
        {
            N = Vector3d.Zero;
            BigInteger[] X = ExactCoords(a, b, c);
            BigInteger ux = X[3] - X[0], uy = X[4] - X[1], uz = X[5] - X[2];
            BigInteger vx = X[6] - X[0], vy = X[7] - X[1], vz = X[8] - X[2];
            BigInteger nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nx.IsZero && ny.IsZero && nz.IsZero) return false;
            long bits = Math.Max(bit_length(nx), Math.Max(bit_length(ny), bit_length(nz)));
            int shift = (int)Math.Max(0, bits - 62);
            // |N_k| < 2^62 after the shift: exact as long. The shift truncates by less than one unit (towards minus
            // infinity), below 2^-61 of the largest component, and the conversion to double rounds by u relative: in
            // norm |N~ - N| <= (u + sqrt 3 2^-61) |N| < 1.02 u |N| (a small component may be off relatively by more)
            N = new Vector3d((double)(long)(nx >> shift), (double)(long)(ny >> shift), (double)(long)(nz >> shift));
            return true;
        }

        static long bit_length(BigInteger x)
        {
            if (x.IsZero) return 0;
            if (x.Sign < 0) x = -x;
            byte[] b = x.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) top--;
            int hb = 0;
            for (int v = b[top]; v != 0; v >>= 1) hb++;
            return 8L * top + hb;
        }

        // the nine coordinates as integers times one common power of two
        static BigInteger[] ExactCoords(Vector3d a, Vector3d b, Vector3d c)
        {
            double[] x = { a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z };
            var m = new ulong[9]; var e = new int[9];
            int emin = int.MaxValue;
            for (int k = 0; k < 9; ++k) {
                if (x[k] == 0) continue;
                long bits = BitConverter.DoubleToInt64Bits(x[k]);
                int be = (int)((bits >> 52) & 0x7FF);
                ulong mm = (ulong)bits & 0xFFFFFFFFFFFFFUL;
                if (be == 0) e[k] = -1074; else { mm |= 1UL << 52; e[k] = be - 1075; }
                m[k] = mm;
                emin = Math.Min(emin, e[k]);
            }
            var X = new BigInteger[9];
            for (int k = 0; k < 9; ++k) {
                if (x[k] == 0) { X[k] = BigInteger.Zero; continue; }
                BigInteger v = new BigInteger(m[k]) << (e[k] - emin);
                X[k] = x[k] < 0 ? -v : v;
            }
            return X;
        }


        // ---------------------------------------------------------------- queries

        /// <summary> is the point strictly inside some polyhedron? </summary>
        public bool Contains(Vector3d q)
        {
            if (Status != EnvelopeStatus.Ready || finite(q) == false) return false;
            foreach (int p in candidates(q, q))
                if (inside(p, q)) return true;
            return false;
        }

        /// <summary> is every point of the segment strictly inside some polyhedron? </summary>
        public bool ContainsSegment(Vector3d a, Vector3d b)
        {
            if (Status != EnvelopeStatus.Ready || finite(a) == false || finite(b) == false) return false;
            List<int> cand = candidates(a, b);
            // C1: the endpoints
            bool ia = false, ib = false;
            foreach (int p in cand) {
                if (ia == false && inside(p, a)) ia = true;
                if (ib == false && inside(p, b)) ib = true;
            }
            if (ia == false || ib == false) return false;
            return a == b || crossings_covered(cand, a, b);
        }

        // C2: every crossing of a facet plane of a candidate, strictly between a and b, inside another candidate
        bool crossings_covered(List<int> cand, Vector3d a, Vector3d b)
        {
            foreach (int p in cand) {
                for (int f = 0; f < facetCount[p]; ++f) {
                    int o = (p * MaxFacets + f) * 3;
                    Vector3d r = facets[o], s = facets[o + 1], t = facets[o + 2];
                    int sa = ExactPredicates.Orient3d(r, s, t, a), sb = ExactPredicates.Orient3d(r, s, t, b);
                    if (sa * sb >= 0) continue;
                    bool covered = false;
                    // the crossing point's construction once, then against every facet of the others
                    IndirectPredicates.Orient3dLPIPrepared lpi;
                    IndirectPredicates.Orient3dLPIPrepare(a, b, r, s, t, out lpi);
                    foreach (int p2 in cand)
                        if (p2 != p && inside_lpi(p2, ref lpi)) { covered = true; break; }
                    if (covered == false) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// is every point of the triangle strictly inside some polyhedron? (Thm. 3.2: the vertices inside, every crossing
        /// of an edge with a facet plane inside another polyhedron, and every point where two facet planes meet the
        /// triangle's plane strictly inside the triangle inside some polyhedron; a degenerate triangle: its edges)
        /// </summary>
        public bool ContainsTriangle(Vector3d a, Vector3d b, Vector3d c)
        {
            if (Status != EnvelopeStatus.Ready || finite(a) == false || finite(b) == false || finite(c) == false) return false;
            Vector3d N;
            if (exact_normal(a, b, c, out N) == false)
                return ContainsSegment(a, b) && ContainsSegment(b, c) && ContainsSegment(c, a);
            List<int> cand = candidates_box(a, b, c);
            // C1, and one polyhedron with all three vertices contains the triangle (convex)
            bool ia = false, ib = false, ic = false;
            foreach (int p in cand) {
                bool xa = inside(p, a), xb = inside(p, b), xc = inside(p, c);
                if (xa && xb && xc) return true;
                ia |= xa; ib |= xb; ic |= xc;
            }
            if (ia == false || ib == false || ic == false) return false;
            // C2
            if (crossings_covered(cand, a, b) == false || crossings_covered(cand, b, c) == false || crossings_covered(cand, c, a) == false)
                return false;
            // C3: the facets whose plane crosses the open triangle (a vertex strictly on each side)
            var rel = new List<int>();
            foreach (int p in cand)
                for (int f = 0; f < facetCount[p]; ++f) {
                    int o = (p * MaxFacets + f) * 3;
                    int sa = ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], a);
                    int sb = ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], b);
                    int sc = ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], c);
                    if (Math.Max(sa, Math.Max(sb, sc)) > 0 && Math.Min(sa, Math.Min(sb, sc)) < 0) rel.Add(p * MaxFacets + f);
                }
            if (rel.Count < 2) return true;
            // a point off the triangle's plane: the planes through each edge and it bound the triangle
            Vector3d top = apex(a, b, c, N);
            int ea = ExactPredicates.Orient3d(b, c, top, a), eb = ExactPredicates.Orient3d(c, a, top, b), ec = ExactPredicates.Orient3d(a, b, top, c);
            for (int i = 0; i < rel.Count; ++i) {
                int o1 = rel[i] * 3, p1 = rel[i] / MaxFacets;
                for (int j = i + 1; j < rel.Count; ++j) {
                    int o2 = rel[j] * 3, p2 = rel[j] / MaxFacets;
                    // dependent planes: the two traces in the triangle's plane are parallel or equal, no single point
                    if (IndirectPredicates.TPIDenominator(a, b, c, facets[o1], facets[o1 + 1], facets[o1 + 2],
                            facets[o2], facets[o2 + 1], facets[o2 + 2]) == 0) continue;
                    IndirectPredicates.Orient3dTPIPrepared tpi;
                    IndirectPredicates.Orient3dTPIPrepare(a, b, c, facets[o1], facets[o1 + 1], facets[o1 + 2],
                        facets[o2], facets[o2 + 1], facets[o2 + 2], out tpi);
                    // strictly inside the triangle?
                    if (IndirectPredicates.Orient3dTPI(ref tpi, b, c, top) != ea || IndirectPredicates.Orient3dTPI(ref tpi, c, a, top) != eb
                        || IndirectPredicates.Orient3dTPI(ref tpi, a, b, top) != ec) continue;
                    // (the point is on a facet plane of p1 and of p2, so in neither: no need to skip them)
                    bool covered = false;
                    foreach (int p in cand)
                        if (p != p1 && p != p2 && inside_tpi(p, ref tpi)) { covered = true; break; }
                    if (covered == false) return false;
                }
            }
            return true;
        }

        // a point strictly off the plane of the (non-degenerate) triangle
        static Vector3d apex(Vector3d a, Vector3d b, Vector3d c, Vector3d N)
        {
            Vector3d n;
            unit(N, out n);
            double L = Math.Max(length(b - a), Math.Max(length(c - b), length(a - c)));
            for (double k = 1; ; k *= 2) {
                Vector3d top = a + (k * L) * n;
                if (ExactPredicates.Orient3d(a, b, c, top) != 0) return top;
                if (k > 1e300) throw new InvalidOperationException("PolyhedralEnvelope: no point off the triangle's plane");
            }
        }

        bool inside_tpi(int p, ref IndirectPredicates.Orient3dTPIPrepared tpi)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; f < facetCount[p]; ++f, o += 3)
                if (IndirectPredicates.Orient3dTPI(ref tpi, facets[o], facets[o + 1], facets[o + 2]) >= 0) return false;
            return true;
        }

        // the polyhedra whose search box meets the triangle's box (a linear scan)
        List<int> candidates_box(Vector3d a, Vector3d b, Vector3d c)
        {
            var box = new AxisAlignedBox3d(a); box.Contain(b); box.Contain(c);
            var list = new List<int>();
            for (int p = 0; p < count; ++p)
                if (boxes[p].Intersects(box)) list.Add(p);
            return list;
        }

        internal List<int> candidates(Vector3d a, Vector3d b)
        {
            var list = new List<int>();
            tree.QuerySegment(a, b, 0, list.Add);
            list.Sort();        // a deterministic order of the work
            return list;
        }

        bool inside(int p, Vector3d x)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; f < facetCount[p]; ++f, o += 3)
                if (ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], x) >= 0) return false;
            return true;
        }

        // is the prepared point (line ab and plane rst) strictly inside polyhedron p?
        bool inside_lpi(int p, ref IndirectPredicates.Orient3dLPIPrepared lpi)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; f < facetCount[p]; ++f, o += 3)
                if (IndirectPredicates.Orient3dLPI(ref lpi, facets[o], facets[o + 1], facets[o + 2]) >= 0) return false;
            return true;
        }

        /// <summary> (tests) the three points of facet f of polyhedron p </summary>
        internal void GetFacet(int p, int f, out Vector3d q0, out Vector3d q1, out Vector3d q2)
        {
            int o = (p * MaxFacets + f) * 3;
            q0 = facets[o]; q1 = facets[o + 1]; q2 = facets[o + 2];
        }
        internal int FacetCount(int p) { return facetCount[p]; }
    }
}
