using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The symbolic perturbation of the site weights (math/ExactBisectorSoS.cs; docs/design/BISECTOR_PREDICATES.md section
    /// 3): every perturbed sign against an oracle that evaluates the weighted construction itself with explicit rational
    /// weights (not the coefficient cascade), at exact ties and on random inputs; the consistency of Lemma S2; the perturbed
    /// in-sphere test against the lifted 5 x 5 determinant; and Delaunay3 with SymbolicPerturbation.
    /// </summary>
    public class ExactBisectorSoSTests
    {
        // ---- the weighted oracle: integer coordinates (a common power of two), weights W_s = 2^(E (2^n - 2^rank(s))) over the
        // common denominator W = 2^(E 2^n), rank 0 for the smallest id (the dominant weight); E is far above the bit length of
        // every value of these small inputs, so the weights are infinitesimal in the dominance order ----

        const int E = 400;

        static void Dec(double x, out BigInteger m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long mm = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) e = -1074; else { mm |= 1L << 52; e = be - 1075; }
            while (mm != 0 && (mm & 1) == 0) { mm >>= 1; e++; }
            m = x < 0 ? -(BigInteger)mm : mm;
        }

        static BigInteger[][] Ints(params Vector3d[] P)
        {
            int emin = int.MaxValue;
            foreach (var p in P)
                foreach (double x in new[] { p.x, p.y, p.z })
                    if (x != 0) { Dec(x, out _, out int e); emin = Math.Min(emin, e); }
            var R = P.Select(p => new[] { p.x, p.y, p.z }.Select(x => {
                if (x == 0) return BigInteger.Zero;
                Dec(x, out var m, out int e);
                return m << (e - emin);
            }).ToArray()).ToArray();
            // the weights are infinitesimal only against values far below 2^E: small inputs (degree <= 6 here)
            Assert.True(R.All(r => r.All(v => v.GetBitLength() <= E / 8)), "the oracle's weights need small inputs");
            return R;
        }

        // W and the weights of the sites with the given ids
        static BigInteger[] Weights(int[] ids, out BigInteger W)
        {
            int n = ids.Length;
            W = BigInteger.One << (E << n);
            var w = new BigInteger[n];
            for (int i = 0; i < n; ++i) {
                int rank = ids.Count(j => j < ids[i]);
                w[i] = BigInteger.One << (E * ((1 << n) - (1 << rank)));
            }
            return w;
        }

        static BigInteger[] Sub(BigInteger[] a, BigInteger[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static BigInteger[] Mul(BigInteger[] a, BigInteger s) => new[] { a[0] * s, a[1] * s, a[2] * s };
        static BigInteger[] Add(BigInteger[] a, BigInteger[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        static BigInteger Dot(BigInteger[] u, BigInteger[] v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
        static BigInteger N2(BigInteger[] u) => Dot(u, u);
        static BigInteger[] Cross(BigInteger[] u, BigInteger[] v) =>
            new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
        static BigInteger Det3(BigInteger[] u, BigInteger[] v, BigInteger[] w) =>
            u[0] * v[1] * w[2] + u[1] * v[2] * w[0] + u[2] * v[0] * w[1] - u[2] * v[1] * w[0] - u[1] * v[0] * w[2] - u[0] * v[2] * w[1];

        // the power difference pi_a(v) - pi_e(v), times W: < 0 closer to a
        static int OSide1(Vector3d v, Vector3d a, int ia, Vector3d e, int ie)
        {
            var P = Ints(v, a, e);
            var w = Weights(new[] { ia, ie }, out var W);
            return ((N2(Sub(P[0], P[1])) - N2(Sub(P[0], P[2]))) * W - w[0] + w[1]).Sign;
        }

        // the crossing y of q0 q1 with the power bisector of a, b (a parameter on the segment), then pi_a(y) - pi_c(y)
        static int OSide2(Vector3d q0, Vector3d q1, Vector3d a, int ia, Vector3d b, int ib, Vector3d c, int ic)
        {
            var P = Ints(q0, q1, a, b, c);
            var w = Weights(new[] { ia, ib, ic }, out var W);
            BigInteger[] Q0 = P[0], Q1 = P[1], A = P[2], B = P[3], C = P[4];
            BigInteger G0 = (N2(Sub(Q0, A)) - N2(Sub(Q0, B))) * W - w[0] + w[1];
            BigInteger G1 = (N2(Sub(Q1, A)) - N2(Sub(Q1, B))) * W - w[0] + w[1];
            BigInteger D = G0 - G1;
            if (D.IsZero) return 0;
            var Y = Add(Mul(Q0, D), Mul(Sub(Q1, Q0), G0));         // y = Y / D
            // (pi_a - pi_c)(y) D W = 2 Y.(c - a) W + (|a|^2 - |c|^2) D W + (W_c - W_a) D
            BigInteger v = 2 * Dot(Y, Sub(C, A)) * W + (N2(A) - N2(C)) * D * W + (w[2] - w[0]) * D;
            return v.Sign * D.Sign;
        }

        // the point of the power bisectors (a, b), (a, c) and the plane of t by Cramer's rule, then pi_a(x) - pi_e(x)
        static int OSide3(Vector3d a, int ia, Vector3d b, int ib, Vector3d c, int ic, Vector3d t0, Vector3d t1, Vector3d t2, Vector3d e, int ie)
        {
            var P = Ints(a, b, c, t0, t1, t2, e);
            var w = Weights(new[] { ia, ib, ic, ie }, out var W);
            BigInteger[] A = P[0], B = P[1], C = P[2], T0 = P[3], T1 = P[4], T2 = P[5], Ee = P[6];
            var n = Cross(Sub(T1, T0), Sub(T2, T0));
            var rows = new[] { Mul(Sub(B, A), 2), Mul(Sub(C, A), 2), n };
            var rhs = new[] { (N2(B) - N2(A)) * W - (w[1] - w[0]), (N2(C) - N2(A)) * W - (w[2] - w[0]), Dot(n, T0) * W };
            BigInteger D = Det3(rows[0], rows[1], rows[2]);
            if (D.IsZero) return 0;
            var X = new BigInteger[3];
            for (int col = 0; col < 3; ++col) {
                var r = rows.Select(x => (BigInteger[])x.Clone()).ToArray();
                for (int k = 0; k < 3; ++k) r[k][col] = rhs[k];
                X[col] = Det3(r[0], r[1], r[2]);
            }
            // x = X / (D W): (pi_a - pi_e)(x) D W = 2 X.(e - a) + (|a|^2 - |e|^2) D W + (W_e - W_a) D
            BigInteger v = 2 * Dot(X, Sub(Ee, A)) + (N2(A) - N2(Ee)) * D * W + (w[3] - w[0]) * D;
            return v.Sign * D.Sign;
        }

        static BigInteger Det(BigInteger[][] M)
        {
            int n = M.Length;
            if (n == 1) return M[0][0];
            BigInteger s = 0;
            for (int j = 0; j < n; ++j) {
                if (M[0][j].IsZero) continue;
                var minor = M.Skip(1).Select(r => r.Where((_, k) => k != j).ToArray()).ToArray();
                BigInteger t = M[0][j] * Det(minor);
                s += (j % 2 == 0) ? t : -t;
            }
            return s;
        }

        // the lifted determinant of the rows (p, (|p|^2 - w_p) W, 1), with the sign that matches ExactPredicates.InSphere
        static int OInSphereRaw(Vector3d[] pts, int[] ids)
        {
            var P = Ints(pts);
            var w = Weights(ids, out var W);
            var M = P.Select((p, i) => new[] { p[0], p[1], p[2], N2(p) * W - w[i], BigInteger.One }).ToArray();
            return Det(M).Sign;
        }

        static int liftSign;

        static int OInSphere(Vector3d[] pts, int[] ids)
        {
            if (liftSign == 0) {
                // the orientation of the lifted determinant against ExactPredicates.InSphere, on non-degenerate points
                var rand = new Random(99);
                while (liftSign == 0) {
                    var q = Enumerable.Range(0, 5).Select(_ => new Vector3d(rand.Next(-20, 20), rand.Next(-20, 20), rand.Next(-20, 20))).ToArray();
                    int s = ExactPredicates.InSphere(q[0], q[1], q[2], q[3], q[4]);
                    if (s != 0 && ExactPredicates.Orient3d(q[0], q[1], q[2], q[3]) != 0)
                        liftSign = s * OInSphereRaw(q, new[] { 0, 1, 2, 3, 4 });
                }
            }
            return liftSign * OInSphereRaw(pts, ids);
        }

        // ---- inputs: lattice points and sites at equal distance from them (the ties), with random distinct ids ----

        static readonly Vector3d[] Norm3 = new[] { new Vector3d(1, 2, 2), new Vector3d(2, -1, 2), new Vector3d(-2, 2, 1), new Vector3d(3, 0, 0),
            new Vector3d(0, -3, 0), new Vector3d(0, 0, 3), new Vector3d(-1, -2, 2), new Vector3d(2, 2, -1), new Vector3d(-2, -1, -2),
            new Vector3d(-3, 0, 0), new Vector3d(0, 3, 0), new Vector3d(0, 0, -3), new Vector3d(2, 1, -2), new Vector3d(1, -2, -2) };

        static int[] Ids(Random rand, int n)
        {
            var ids = new List<int>();
            while (ids.Count < n) { int i = rand.Next(0, 40); if (!ids.Contains(i)) ids.Add(i); }
            return ids.ToArray();
        }

        static Vector3d SmallDir(Random rand, int r) => new Vector3d(rand.Next(-r, r + 1), rand.Next(-r, r + 1), rand.Next(-r, r + 1));

        /// <summary>
        /// the perturbed sides equal the weighted oracle at ties (sites equidistant from a vertex, an edge crossing, a
        /// Voronoi vertex in a triangle's plane, coplanar sites that zero the leading coefficients) and on random inputs;
        /// never 0 where the point exists; every depth of the cascade is reached
        /// </summary>
        [Fact]
        public void Sides_MatchTheWeightedOracle()
        {
            var rand = new Random(1);
            var depth2 = new int[5];
            var depth3 = new int[5];
            int ties1 = 0, ties2 = 0, ties3 = 0;
            for (int k = 0; k < 3000; ++k) {
                double s = Math.ScaleB(1.0, rand.Next(-60, 60));
                var x = SmallDir(rand, 20);
                bool tie = k % 4 != 3;
                // sites around x: at distance 3 (ties), or moved off the sphere
                var S = Norm3.OrderBy(_ => rand.Next()).Take(4).Select(d => (x + (tie ? d : d + SmallDir(rand, 1))) * s).ToArray();
                // a few with collinear or coplanar sites, so that the first coefficients vanish
                if (k % 5 == 0) S[2] = x * s * 2 - S[1];
                if (k % 7 == 0) S[3] = x * s * 2 - S[0];
                int[] id = Ids(rand, 4);
                Vector3d X = x * s, a = S[0], b = S[1], c = S[2], e = S[3];
                // Side1 at the lattice point
                int s1 = ExactBisectorSoS.Side1(X, a, id[0], e, id[3]);
                Assert.Equal(OSide1(X, a, id[0], e, id[3]), s1);
                Assert.NotEqual(0, s1);
                if (BisectorPredicates.BisectorSide1(X, a, e) == 0) ties1++;
                // Side2 on an edge through x (or near it)
                var dir = SmallDir(rand, 3);
                if (k % 11 == 0) dir = new Vector3d(c.y - b.y, b.x - c.x, 0) / s;         // (c - b).dq = 0
                Vector3d q0 = X - dir * s, q1 = X + dir * s * (k % 3 == 0 ? 2 : 1);
                int s2 = ExactBisectorSoS.Side2(q0, q1, a, id[0], b, id[1], c, id[2]);
                Assert.Equal(OSide2(q0, q1, a, id[0], b, id[1], c, id[2]), s2);
                if (BisectorPredicates.DotSites(a, b, q0, q1) != 0) Assert.NotEqual(0, s2); else Assert.Equal(0, s2);
                if (BisectorPredicates.BisectorSide2(q0, q1, a, b, c) == 0 && s2 != 0) {
                    ties2++;
                    depth2[Depth(new[] { id[0], id[1], id[2] }, new[] { BisectorPredicates.DotSites(b, c, q0, q1), BisectorPredicates.DotSites(a, c, q0, q1), 1 })]++;
                }
                // Side3 on a triangle through x
                Vector3d u = SmallDir(rand, 4) * s, w = SmallDir(rand, 4) * s;
                Vector3d t0 = X + u, t1 = X + w, t2 = X - u - w;
                int s3 = ExactBisectorSoS.Side3(a, id[0], b, id[1], c, id[2], t0, t1, t2, e, id[3]);
                Assert.Equal(OSide3(a, id[0], b, id[1], c, id[2], t0, t1, t2, e, id[3]), s3);
                if (BisectorPredicates.DetSitesNormal(a, b, c, t0, t1, t2) != 0) Assert.NotEqual(0, s3); else Assert.Equal(0, s3);
                if (BisectorPredicates.BisectorSide3(a, b, c, t0, t1, t2, e) == 0 && s3 != 0) {
                    ties3++;
                    depth3[Depth(id, new[] { BisectorPredicates.DetSitesNormal(e, b, c, t0, t1, t2), BisectorPredicates.DetSitesNormal(a, e, c, t0, t1, t2),
                        BisectorPredicates.DetSitesNormal(a, b, e, t0, t1, t2), 1 })]++;
                }
            }
            Assert.True(ties1 > 1000 && ties2 > 300 && ties3 > 1000, $"ties {ties1} {ties2} {ties3}");
            // the coefficients sum to 0, so with d != 0 at most one of the three of Side2 is 0 before the decisive one, and
            // at most two of the four of Side3
            Assert.True(depth2[0] > 0 && depth2[1] > 0 && depth2[2] == 0 && depth2[3] == 0, "Side2 depths " + string.Join(" ", depth2));
            Assert.True(depth3[0] > 0 && depth3[1] > 0 && depth3[2] > 0 && depth3[3] == 0 && depth3[4] == 0, "Side3 depths " + string.Join(" ", depth3));
        }

        // the position in id order of the first nonzero coefficient
        static int Depth(int[] ids, int[] coefs)
        {
            var order = Enumerable.Range(0, ids.Length).OrderBy(i => ids[i]).ToArray();
            for (int k = 0; k < order.Length; ++k) if (coefs[order[k]] != 0) return k;
            return order.Length;
        }

        /// <summary>
        /// Lemma S2: one point, one answer: Side1 is antisymmetric; Side2 is the same for either site of its bisector;
        /// Side3 is the same for every anchor of the three sites of its Voronoi edge
        /// </summary>
        [Fact]
        public void Consistency_AntisymmetryAndAnchors()
        {
            var rand = new Random(2);
            for (int k = 0; k < 2000; ++k) {
                var x = SmallDir(rand, 20);
                var S = Norm3.OrderBy(_ => rand.Next()).Take(4).Select(d => x + d).ToArray();
                int[] id = Ids(rand, 4);
                Assert.Equal(ExactBisectorSoS.Side1(x, S[0], id[0], S[3], id[3]), -ExactBisectorSoS.Side1(x, S[3], id[3], S[0], id[0]));
                var dir = SmallDir(rand, 3);
                Vector3d q0 = x - dir, q1 = x + 2 * dir;
                Assert.Equal(ExactBisectorSoS.Side2(q0, q1, S[0], id[0], S[1], id[1], S[2], id[2]),
                    ExactBisectorSoS.Side2(q0, q1, S[1], id[1], S[0], id[0], S[2], id[2]));
                // the reversed edge: the same point
                Assert.Equal(ExactBisectorSoS.Side2(q0, q1, S[0], id[0], S[1], id[1], S[2], id[2]),
                    ExactBisectorSoS.Side2(q1, q0, S[0], id[0], S[1], id[1], S[2], id[2]));
                Vector3d u = SmallDir(rand, 4), w = SmallDir(rand, 4);
                Vector3d t0 = x + u, t1 = x + w, t2 = x - u - w;
                int r = ExactBisectorSoS.Side3(S[0], id[0], S[1], id[1], S[2], id[2], t0, t1, t2, S[3], id[3]);
                Assert.Equal(r, ExactBisectorSoS.Side3(S[1], id[1], S[0], id[0], S[2], id[2], t0, t1, t2, S[3], id[3]));
                Assert.Equal(r, ExactBisectorSoS.Side3(S[2], id[2], S[1], id[1], S[0], id[0], t0, t1, t2, S[3], id[3]));
                Assert.Equal(r, ExactBisectorSoS.Side3(S[0], id[0], S[2], id[2], S[1], id[1], t0, t1, t2, S[3], id[3]));
                // the triangle's orientation and its vertex order do not matter
                Assert.Equal(r, ExactBisectorSoS.Side3(S[0], id[0], S[1], id[1], S[2], id[2], t1, t0, t2, S[3], id[3]));
                Assert.Equal(r, ExactBisectorSoS.Side3(S[0], id[0], S[1], id[1], S[2], id[2], t1, t2, t0, S[3], id[3]));
            }
        }

        /// <summary> the perturbed in-sphere test equals the lifted determinant with the same weights, at cospherical points and off them </summary>
        [Fact]
        public void InSphere_MatchesTheLiftedDeterminant()
        {
            var rand = new Random(3);
            int ties = 0;
            for (int k = 0; k < 2000; ++k) {
                var x = SmallDir(rand, 20);
                var P = Norm3.OrderBy(_ => rand.Next()).Take(5).Select(d => x + d).ToArray();
                if (k % 3 == 0) P[rand.Next(5)] += SmallDir(rand, 1);
                if (ExactPredicates.Orient3d(P[0], P[1], P[2], P[3]) == 0) continue;
                int[] id = Ids(rand, 5);
                int s = ExactBisectorSoS.InSphere(P[0], id[0], P[1], id[1], P[2], id[2], P[3], id[3], P[4], id[4]);
                Assert.Equal(OInSphere(P, id), s);
                Assert.NotEqual(0, s);
                if (ExactPredicates.InSphere(P[0], P[1], P[2], P[3], P[4]) == 0) ties++;
            }
            Assert.True(ties > 1000);
        }

        [Fact]
        public void DistinctIds_AreRequired()
        {
            var z = Vector3d.Zero;
            Assert.Throws<ArgumentException>(() => ExactBisectorSoS.Side1(z, z, 1, z, 1));
            Assert.Throws<ArgumentException>(() => ExactBisectorSoS.Side2(z, z, z, 1, z, 2, z, 1));
            Assert.Throws<ArgumentException>(() => ExactBisectorSoS.Side3(z, 1, z, 2, z, 3, z, z, z, z, 2));
            Assert.Throws<ArgumentException>(() => ExactBisectorSoS.InSphere(z, 1, z, 2, z, 3, z, 4, z, 4));
        }

        // ---- Delaunay3 with SymbolicPerturbation ----

        // the perturbed check of Validate: every vertex outside every cell's weighted sphere, every cell positive
        static void CheckRegular(Delaunay3 dt)
        {
            Assert.Null(dt.Validate(true));
            foreach (int c in dt.CellIndices()) {
                var t = dt.GetCell(c);
                Assert.True(ExactPredicates.Orient3d(dt.GetVertex(t.a), dt.GetVertex(t.b), dt.GetVertex(t.c), dt.GetVertex(t.d)) > 0);
            }
        }

        /// <summary> the initial six tetrahedra of the box are the regular triangulation of the weighted corners (the pulling one from corner 0) </summary>
        [Fact]
        public void Delaunay3_InitialCellsAreRegular()
        {
            var dt = new Delaunay3(new AxisAlignedBox3d(new Vector3d(-1, -2, -3), new Vector3d(4, 5, 6))) { SymbolicPerturbation = true };
            foreach (int c in dt.CellIndices())
                for (int v = 0; v < 8; ++v)
                    if (!new[] { dt.GetCell(c).a, dt.GetCell(c).b, dt.GetCell(c).c, dt.GetCell(c).d }.Contains(v))
                        Assert.Equal(-1, dt.InCircumsphere(c, dt.GetVertex(v), v));
            CheckRegular(dt);
            Assert.Throws<InvalidOperationException>(() => {
                var d2 = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
                d2.Insert(new Vector3d(0.5, 0.5, 0.5), -1, 0, out _);
                d2.SymbolicPerturbation = true;
            });
            Assert.Throws<InvalidOperationException>(() => dt.PlanInsert(new Vector3d(0.5, 0.5, 0.5), -1, 0, new Delaunay3.InsertPlan(), 1));
        }

        /// <summary>
        /// cospherical lattices (the worst case for ties) inserted in several orders: always the regular triangulation of its
        /// ids (unique: every other vertex outside every cell's weighted sphere), every cell positive
        /// </summary>
        [Fact]
        public void Delaunay3_Lattice_IsRegularInEveryOrder()
        {
            var lattice = new List<Vector3d>();
            for (int i = 1; i <= 4; ++i) for (int j = 1; j <= 4; ++j) for (int k = 1; k <= 4; ++k) lattice.Add(new Vector3d(i, j, k));
            var rand = new Random(4);
            for (int round = 0; round < 6; ++round) {
                var order = lattice.OrderBy(_ => rand.Next()).ToList();
                var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(5, 5, 5))) { SymbolicPerturbation = true };
                foreach (var p in order)
                    Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(p, -1, 0, out _));
                CheckRegular(dt);
                // the regular triangulation is unique: every vertex is outside every cell's weighted sphere
                foreach (int c in dt.CellIndices()) {
                    var t = dt.GetCell(c);
                    for (int v = 0; v < dt.VertexCount; ++v)
                        if (v != t.a && v != t.b && v != t.c && v != t.d)
                            Assert.Equal(-1, dt.InCircumsphere(c, dt.GetVertex(v), v));
                }
            }
        }

        /// <summary> the same points with the same ids (the same order) give the same cells, also after Clone (the flag is copied) </summary>
        [Fact]
        public void Delaunay3_Perturbed_Deterministic_AndCloned()
        {
            var rand = new Random(5);
            var pts = Enumerable.Range(0, 300).Select(_ => new Vector3d(rand.Next(1, 8), rand.Next(1, 8), rand.Next(1, 8)) + new Vector3d(0.5 * rand.Next(2), 0, 0)).Distinct().ToList();
            Func<Delaunay3, string> cells = d => string.Join(";", d.CellIndices().Select(c => d.GetCell(c)).Select(t => string.Join(",", new[] { t.a, t.b, t.c, t.d }.OrderBy(v => v))).OrderBy(x => x));
            Delaunay3 Build(int split, out Delaunay3 clone)
            {
                var d = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(9, 9, 9))) { SymbolicPerturbation = true };
                clone = null;
                for (int i = 0; i < pts.Count; ++i) {
                    if (i == split) { clone = d; d = d.Clone(); }
                    d.Insert(pts[i], -1, 0, out _);
                }
                return d;
            }
            var a = Build(-1, out _);
            var b = Build(pts.Count / 2, out var half);
            Assert.Equal(cells(a), cells(b));
            Assert.Throws<InvalidOperationException>(() => half.Clone().PlanInsert(Vector3d.One, -1, 0, new Delaunay3.InsertPlan(), 1));
            CheckRegular(a);
        }
    }
}
