// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
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
            return box.Min.IsFinite && box.Max.IsFinite;
        }

        internal static double norm_inf(Vector3d v) { return Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))); }

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
                foreach (Vector3d q in v) if (q.IsFinite == false) return EnvelopeStatus.OutOfRange;
                Vector3d[] e = { v1 - v0, v2 - v1, v0 - v2 };
                foreach (Vector3d q in e) if (q.IsFinite == false) return EnvelopeStatus.OutOfRange;
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
                double off = delta;
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
                double corr = 4 * U * (Math.Abs(h) + off);
                if (!(margin + corr <= off / 10)) { status = EnvelopeStatus.TooThin; return; }
                double s = h + (off - margin - corr);

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
                if (!(1.7320508075688772 * U * qinf <= R / 200) || q1.IsFinite == false || q2.IsFinite == false) {
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
            if (Status != EnvelopeStatus.Ready || q.IsFinite == false) return false;
            var cand = new List<int>();
            tree.Query(new AxisAlignedBox3d(q), cand);
            foreach (int p in cand)
                if (inside(p, q)) return true;
            return false;
        }

        /// <summary> is every point of the segment strictly inside some polyhedron? </summary>
        public bool ContainsSegment(Vector3d a, Vector3d b)
        {
            if (Status != EnvelopeStatus.Ready || a.IsFinite == false || b.IsFinite == false) return false;
            // C1: check endpoint a (and single-polyhedron convex early exit), then endpoint b
            var ptCand = new List<int>();
            tree.Query(new AxisAlignedBox3d(a), ptCand);
            bool ia = false;
            foreach (int p in ptCand) {
                if (inside(p, a) == false) continue;
                if (inside(p, b)) return true;
                ia = true;
            }
            if (ia == false || Contains(b) == false) return false;
            List<int> cand = candidates(a, b);
            int n = cand.Count;
            var active = new int[n];
            var cross = new byte[n];
            var exit = new byte[n];
            int m = 0;
            // Classify facets: drop disjoint polyhedra (sa >= 0 && sb >= 0),
            // keeping crossing (sa * sb < 0) and exit (sa < 0 < sb) facet bitmasks.
            for (int ci = 0; ci < n; ++ci) {
                int p = cand[ci];
                bool disjoint = false;
                byte cm = 0, em = 0;
                int nf = facetCount[p], o = p * MaxFacets * 3;
                for (int f = 0; f < nf; ++f, o += 3) {
                    int sa = ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], a);
                    int sb = ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], b);
                    if (sa >= 0) {
                        if (sb >= 0) { disjoint = true; break; }
                        if (sa > 0) cm |= (byte)(1 << f);
                    } else if (sb > 0) {
                        byte bit = (byte)(1 << f);
                        cm |= bit;
                        em |= bit;
                    }
                }
                if (disjoint) continue;
                active[m] = p; cross[m] = cm; exit[m] = em; m++;
            }
            return crossings_covered(active, cross, exit, m, a, b);
        }

        /// <summary>
        /// is every point of the triangle strictly inside some polyhedron? (Thm. 3.2: the vertices inside, every crossing
        /// of an edge with a facet plane inside another polyhedron, and every point where two facet planes meet the
        /// triangle's plane strictly inside the triangle inside some polyhedron; a degenerate triangle: its edges)
        /// </summary>
        public bool ContainsTriangle(Vector3d a, Vector3d b, Vector3d c)
        {
            if (Status != EnvelopeStatus.Ready || a.IsFinite == false || b.IsFinite == false || c.IsFinite == false) return false;
            // C1: check vertex a first (and single-polyhedron convex early exit), then b and c
            var ptCand = new List<int>();
            tree.Query(new AxisAlignedBox3d(a), ptCand);
            bool ia = false;
            foreach (int p in ptCand) {
                if (inside(p, a) == false) continue;
                if (inside(p, b) && inside(p, c)) return true;
                ia = true;
            }
            if (ia == false || Contains(b) == false || Contains(c) == false) return false;

            List<int> cand = candidates_box(a, b, c);
            int n = cand.Count;
            var tbox = new AxisAlignedBox3d(a); tbox.Contain(b); tbox.Contain(c);
            byte tSigns = normal_signs(a, b, c);
            var active = new int[n];
            var triMask = new byte[n];
            var abP = new int[n]; var abC = new byte[n]; var abE = new byte[n]; int mAB = 0;
            var bcP = new int[n]; var bcC = new byte[n]; var bcE = new byte[n]; int mBC = 0;
            var caP = new int[n]; var caC = new byte[n]; var caE = new byte[n]; int mCA = 0;
            int m = 0;
            // single-pass facet classification for the triangle and its three edges
            for (int ci = 0; ci < n; ++ci) {
                int p = cand[ci];
                AxisAlignedBox3d bp = boxes[p];
                if (plane_misses_box(a, b, c, tSigns, bp.Min.x, bp.Min.y, bp.Min.z, bp.Max.x, bp.Max.y, bp.Max.z)) continue;
                bool disjoint = false, dAB = false, dBC = false, dCA = false;
                byte tm = 0, cAB = 0, eAB = 0, cBC = 0, eBC = 0, cCA = 0, eCA = 0;
                int nf = facetCount[p], o = p * MaxFacets * 3;
                for (int f = 0; f < nf; ++f, o += 3) {
                    Vector3d r = facets[o], s = facets[o + 1], t = facets[o + 2];
                    int sa = ExactPredicates.Orient3d(r, s, t, a);
                    int sb = ExactPredicates.Orient3d(r, s, t, b);
                    int sc = ExactPredicates.Orient3d(r, s, t, c);
                    if (sa >= 0 && sb >= 0 && sc >= 0) { disjoint = true; break; }
                    byte bit = (byte)(1 << f);
                    if (sa > 0 || sb > 0 || sc > 0) tm |= bit;
                    if (sa >= 0 && sb >= 0) dAB = true;
                    else if (sa > 0) cAB |= bit;
                    else if (sb > 0) { cAB |= bit; eAB |= bit; }
                    if (sb >= 0 && sc >= 0) dBC = true;
                    else if (sb > 0) cBC |= bit;
                    else if (sc > 0) { cBC |= bit; eBC |= bit; }
                    if (sc >= 0 && sa >= 0) dCA = true;
                    else if (sc > 0) cCA |= bit;
                    else if (sa > 0) { cCA |= bit; eCA |= bit; }
                }
                if (disjoint) continue;
                active[m] = p; triMask[m] = tm; m++;
                if (dAB == false) { abP[mAB] = p; abC[mAB] = cAB; abE[mAB] = eAB; mAB++; }
                if (dBC == false) { bcP[mBC] = p; bcC[mBC] = cBC; bcE[mBC] = eBC; mBC++; }
                if (dCA == false) { caP[mCA] = p; caC[mCA] = cCA; caE[mCA] = eCA; mCA++; }
            }
            // C2: the three edges (reusing the signs classified above)
            if (crossings_covered(abP, abC, abE, mAB, a, b) == false
                || crossings_covered(bcP, bcC, bcE, mBC, b, c) == false
                || crossings_covered(caP, caC, caE, mCA, c, a) == false)
                return false;
            // degenerate triangles are already decided by C1 and C2; exact_normal is only needed for C3's apex
            Vector3d N;
            if (exact_normal(a, b, c, out N) == false) return true;
            // a point off the triangle's plane: the planes through each edge and it bound the triangle
            Vector3d top = apex(a, b, c, N);
            int ea = ExactPredicates.Orient3d(b, c, top, a), eb = ExactPredicates.Orient3d(c, a, top, b), ec = ExactPredicates.Orient3d(a, b, top, c);
            // exact normal component signs of each active facet in triMask, for exact box-extrema pruning below
            var fSigns = new byte[m * MaxFacets];
            for (int i = 0; i < m; ++i) {
                int p = active[i];
                for (int f = 0, mm = triMask[i]; mm != 0; ++f, mm >>= 1) {
                    if ((mm & 1) == 0) continue;
                    int o = (p * MaxFacets + f) * 3;
                    fSigns[i * MaxFacets + f] = normal_signs(facets[o], facets[o + 1], facets[o + 2]);
                }
            }
            // C3: pairs of crossing facets of two different active polyhedra whose closures can meet in the triangle;
            // the cover set (the paper's section 3.2.4) tries polyhedra that already covered a point first
            var cover = new List<int>();
            for (int i1 = 0; i1 < m; ++i1) {
                int tm1 = triMask[i1];
                int p1 = active[i1];
                AxisAlignedBox3d b1 = boxes[p1];
                for (int i2 = i1 + 1; i2 < m; ++i2) {
                    int tm2 = triMask[i2];
                    int p2 = active[i2];
                    AxisAlignedBox3d b2 = boxes[p2];
                    // every point of closure(p1) n closure(p2) n T lies in the closed intersection box B
                    if (pair_box_meets_plane(tbox, b1, b2, a, b, c, tSigns,
                        out double x0, out double y0, out double z0, out double x1, out double y1, out double z1) == false) continue;
                    int pm1 = filter_pair_facets(p1, tm1, fSigns, i1 * MaxFacets, x0, y0, z0, x1, y1, z1);
                    if (pm1 == 0) continue;
                    int pm2 = filter_pair_facets(p2, tm2, fSigns, i2 * MaxFacets, x0, y0, z0, x1, y1, z1);
                    if (pm2 == 0) continue;
                    var interBox = new AxisAlignedBox3d(x0, y0, z0, x1, y1, z1);
                    for (int f1 = 0, m1 = pm1; m1 != 0; ++f1, m1 >>= 1) {
                        if ((m1 & 1) == 0) continue;
                        int o1 = (p1 * MaxFacets + f1) * 3;
                        for (int f2 = 0, m2 = pm2; m2 != 0; ++f2, m2 >>= 1) {
                            if ((m2 & 1) == 0) continue;
                            int o2 = (p2 * MaxFacets + f2) * 3;
                            // (Dependent planes, no single point, need no test of their own: Orient3dTPI is 0 then,
                            // and the strict inside test below drops the pair.)
                            IndirectPredicates.Orient3dTPIPrepared tpi;
                            IndirectPredicates.Orient3dTPIPrepare(a, b, c, facets[o1], facets[o1 + 1], facets[o1 + 2],
                                facets[o2], facets[o2 + 1], facets[o2 + 2], out tpi);
                            // strictly inside the triangle?
                            if (IndirectPredicates.Orient3dTPI(ref tpi, b, c, top) != ea || IndirectPredicates.Orient3dTPI(ref tpi, c, a, top) != eb
                                || IndirectPredicates.Orient3dTPI(ref tpi, a, b, top) != ec) continue;
                            // only a vertex of the arrangement of the polyhedra's boundaries in the triangle matters:
                            // the point on the closure of both polyhedra (ENVELOPE.md 4.5)
                            if (in_closure_tpi(p1, tm1 & ~(1 << f1), ref tpi) == false
                                || in_closure_tpi(p2, tm2 & ~(1 << f2), ref tpi) == false) continue;
                            // (the point is on a facet plane of p1 and of p2, so in neither); the cover set first
                            bool covered = false;
                            foreach (int j in cover) {
                                int p = active[j];
                                if (p != p1 && p != p2 && boxes_meet(boxes[p], interBox)
                                    && inside_tpi(p, triMask[j], ref tpi)) { covered = true; break; }
                            }
                            if (covered == false) {
                                for (int j = 0; j < m; ++j) {
                                    int p = active[j];
                                    if (p != p1 && p != p2 && boxes_meet(boxes[p], interBox)
                                        && inside_tpi(p, triMask[j], ref tpi)) { covered = true; cover.Add(j); break; }
                                }
                            }
                            if (covered == false) return false;
                        }
                    }
                }
            }
            return true;
        }

        // closed intersection box of tbox, b1 and b2; returns false if empty or strictly on one side of plane (a, b, c)
        internal static bool pair_box_meets_plane(AxisAlignedBox3d tbox, AxisAlignedBox3d b1, AxisAlignedBox3d b2,
            Vector3d a, Vector3d b, Vector3d c, byte tSigns,
            out double x0, out double y0, out double z0, out double x1, out double y1, out double z1)
        {
            x0 = Math.Max(tbox.Min.x, Math.Max(b1.Min.x, b2.Min.x));
            x1 = Math.Min(tbox.Max.x, Math.Min(b1.Max.x, b2.Max.x));
            if (x0 > x1) { y0 = y1 = z0 = z1 = 0; return false; }
            y0 = Math.Max(tbox.Min.y, Math.Max(b1.Min.y, b2.Min.y));
            y1 = Math.Min(tbox.Max.y, Math.Min(b1.Max.y, b2.Max.y));
            if (y0 > y1) { z0 = z1 = 0; return false; }
            z0 = Math.Max(tbox.Min.z, Math.Max(b1.Min.z, b2.Min.z));
            z1 = Math.Min(tbox.Max.z, Math.Min(b1.Max.z, b2.Max.z));
            if (z0 > z1) return false;
            return plane_misses_box(a, b, c, tSigns, x0, y0, z0, x1, y1, z1) == false;
        }

        // exact signs of the three components of (b - a) x (c - a), the gradient of Orient3d(a, b, c, x) in x:
        // bit 0/1/2 set iff the x/y/z component is strictly positive
        internal static byte normal_signs(Vector3d a, Vector3d b, Vector3d c)
        {
            byte m = 0;
            if (ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) > 0) m |= 1;
            if (ExactPredicates.Orient2d(new Vector2d(a.z, a.x), new Vector2d(b.z, b.x), new Vector2d(c.z, c.x)) > 0) m |= 2;
            if (ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) > 0) m |= 4;
            return m;
        }

        // exact corners of [x0, x1] x [y0, y1] x [z0, z1] minimizing and maximizing Orient3d(a, b, c, x)
        internal static void box_extrema(byte signs, double x0, double y0, double z0, double x1, double y1, double z1,
            out Vector3d cmin, out Vector3d cmax)
        {
            if ((signs & 1) != 0) { cmin.x = x0; cmax.x = x1; } else { cmin.x = x1; cmax.x = x0; }
            if ((signs & 2) != 0) { cmin.y = y0; cmax.y = y1; } else { cmin.y = y1; cmax.y = y0; }
            if ((signs & 4) != 0) { cmin.z = z0; cmax.z = z1; } else { cmin.z = z1; cmax.z = z0; }
        }

        // does the closed box lie strictly on one side of the plane (a, b, c)?
        internal static bool plane_misses_box(Vector3d a, Vector3d b, Vector3d c, byte signs,
            double x0, double y0, double z0, double x1, double y1, double z1)
        {
            box_extrema(signs, x0, y0, z0, x1, y1, z1, out Vector3d cmin, out Vector3d cmax);
            return ExactPredicates.Orient3d(a, b, c, cmin) > 0 || ExactPredicates.Orient3d(a, b, c, cmax) < 0;
        }

        // subset of facets in mask whose planes meet the closed box B; returns 0 if B lies strictly outside any facet
        // of polyhedron p (so closure(p) n B is empty) or if no facet plane in mask touches B
        internal int filter_pair_facets(int p, int mask, byte[] fSigns, int signBase,
            double x0, double y0, double z0, double x1, double y1, double z1)
        {
            int kept = 0;
            for (int f = 0, mm = mask; mm != 0; ++f, mm >>= 1) {
                if ((mm & 1) == 0) continue;
                int o = (p * MaxFacets + f) * 3;
                box_extrema(fSigns[signBase + f], x0, y0, z0, x1, y1, z1, out Vector3d cmin, out Vector3d cmax);
                if (ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], cmin) > 0) return 0;
                if (ExactPredicates.Orient3d(facets[o], facets[o + 1], facets[o + 2], cmax) >= 0) kept |= 1 << f;
            }
            return kept;
        }

        internal static bool boxes_meet(AxisAlignedBox3d x, AxisAlignedBox3d y)
        {
            return x.Min.x <= y.Max.x && y.Min.x <= x.Max.x && x.Min.y <= y.Max.y && y.Min.y <= x.Max.y
                && x.Min.z <= y.Max.z && y.Min.z <= x.Max.z;
        }

        // a point strictly off the plane of the (non-degenerate) triangle. (a + k L n stays finite: C1 passed, so the
        // vertices lie in polyhedra, which the construction keeps well inside the double range)
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

        // is the prepared point in the closure of active polyhedron p? Facets absent from mask are strictly negative on
        // the open triangle (or equal to the defining facet plane), so only facets in mask need checking.
        bool in_closure_tpi(int p, int mask, ref IndirectPredicates.Orient3dTPIPrepared tpi)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; mask != 0; ++f, o += 3, mask >>= 1)
                if ((mask & 1) != 0 && IndirectPredicates.Orient3dTPI(ref tpi, facets[o], facets[o + 1], facets[o + 2]) > 0) return false;
            return true;
        }

        bool inside_tpi(int p, int mask, ref IndirectPredicates.Orient3dTPIPrepared tpi)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; mask != 0; ++f, o += 3, mask >>= 1)
                if ((mask & 1) != 0 && IndirectPredicates.Orient3dTPI(ref tpi, facets[o], facets[o + 1], facets[o + 2]) >= 0) return false;
            return true;
        }

        // the polyhedra whose search box meets the triangle's box
        List<int> candidates_box(Vector3d a, Vector3d b, Vector3d c)
        {
            var box = new AxisAlignedBox3d(a); box.Contain(b); box.Contain(c);
            var list = new List<int>();
            tree.Query(box, list);
            list.Sort();        // a deterministic order of the work
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

        // C2: every exit crossing of a facet plane of an active polyhedron (in its closure, strictly between a and b)
        // lies strictly inside another active polyhedron. A polyhedron with a facet >= 0 at both endpoints has empty
        // intersection with the segment and is already dropped; if the segment leaves the union, its first uncovered
        // point is an exit crossing (sa < 0 < sb) in the closure of the polyhedron it leaves.
        bool crossings_covered(int[] active, byte[] cross, byte[] exit, int m, Vector3d a, Vector3d b)
        {
            var cover = new List<int>();
            for (int i = 0; i < m; ++i) {
                int em = exit[i];
                if (em == 0) continue;
                int p = active[i], cm = cross[i];
                for (int f = 0; em != 0; ++f, em >>= 1) {
                    if ((em & 1) == 0) continue;
                    int o = (p * MaxFacets + f) * 3;
                    IndirectPredicates.Orient3dLPIPrepared lpi;
                    IndirectPredicates.Orient3dLPIPrepare(a, b, facets[o], facets[o + 1], facets[o + 2], out lpi);
                    if (in_closure_lpi(p, cm & ~(1 << f), ref lpi) == false) continue;
                    bool covered = false;
                    foreach (int j in cover) {
                        int p2 = active[j];
                        if (p2 != p && boxes_meet(boxes[p], boxes[p2]) && inside_lpi(p2, cross[j], ref lpi)) { covered = true; break; }
                    }
                    if (covered == false) {
                        for (int j = 0; j < m; ++j) {
                            int p2 = active[j];
                            if (p2 != p && boxes_meet(boxes[p], boxes[p2]) && inside_lpi(p2, cross[j], ref lpi)) { covered = true; cover.Add(j); break; }
                        }
                    }
                    if (covered == false) return false;
                }
            }
            return true;
        }

        // is the prepared point (line ab and plane rst) in the closure of active polyhedron p?
        bool in_closure_lpi(int p, int mask, ref IndirectPredicates.Orient3dLPIPrepared lpi)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; mask != 0; ++f, o += 3, mask >>= 1)
                if ((mask & 1) != 0 && IndirectPredicates.Orient3dLPI(ref lpi, facets[o], facets[o + 1], facets[o + 2]) > 0) return false;
            return true;
        }

        // is the prepared point (line ab and plane rst) strictly inside active polyhedron p?
        bool inside_lpi(int p, int mask, ref IndirectPredicates.Orient3dLPIPrepared lpi)
        {
            int o = p * MaxFacets * 3;
            for (int f = 0; mask != 0; ++f, o += 3, mask >>= 1)
                if ((mask & 1) != 0 && IndirectPredicates.Orient3dLPI(ref lpi, facets[o], facets[o + 1], facets[o + 2]) >= 0) return false;
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
