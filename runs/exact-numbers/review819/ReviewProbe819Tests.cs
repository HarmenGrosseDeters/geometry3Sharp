// LOCAL REVIEW PROBE (numerics review of #819); not part of the PR, never committed.
using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class ReviewProbe819Tests
    {
        readonly ITestOutputHelper output;
        public ReviewProbe819Tests(ITestOutputHelper output) { this.output = output; }

        // a double as m 2^e exactly
        static void Dec(double v, out BigInteger m, out int e) { ExactPoint3.Decompose(v, out m, out e); }

        // sign of (m1 2^e1) - (m2 2^e2)
        static int Cmp(BigInteger m1, int e1, BigInteger m2, int e2)
        {
            int e = Math.Min(e1, e2);
            return ((m1 << (e1 - e)) - (m2 << (e2 - e))).Sign;
        }

        static double RandDouble(Random r, int kind)
        {
            switch (kind % 8) {
                case 0: return BitConverter.Int64BitsToDouble((long)(r.NextDouble() * (1L << 52)) | ((long)r.Next(0, 3) << 52)) * (r.Next(2) == 0 ? 1 : -1);   // subnormal and the two lowest binades
                case 1: return BitConverter.Int64BitsToDouble(((long)r.Next(1, 2046) << 52) | (long)(r.NextDouble() * (1L << 52))) * (r.Next(2) == 0 ? 1 : -1);   // any exponent
                case 2: return (r.Next(2) == 0 ? 1 : -1) * Math.Pow(2, r.Next(-1074, -1010)) * r.Next(1, 8);
                case 3: return (r.Next(2) == 0 ? 1 : -1) * (1L << r.Next(0, 53)) * (1 + r.Next(0, 4) * 2.220446049250313e-16);
                case 4: return double.Epsilon * r.Next(-5, 6);
                case 5: return (r.NextDouble() - 0.5) * Math.Pow(2, r.Next(-60, 60));
                case 6: return BitConverter.Int64BitsToDouble(((long)r.Next(1, 4) << 52) | (r.Next(2) == 0 ? 0 : 0xFFFFFFFFFFFFFL)) * (r.Next(2) == 0 ? 1 : -1);   // 2^-1022 .. 2^-1020, extreme mantissas
                default: return 0;
            }
        }

        // c1: err >= |m - (a + b) / 2| (even in L1), exactly, for every pair
        [Fact]
        public void Midpoint_ErrBoundsTheExactError()
        {
            var r = new Random(819);
            int nonzero = 0, sub = 0, edgeCases = 0, edgeFailures = 0; double worstRatio = 0;
            for (int n = 0; n < 400000; ++n) {
                int ka = r.Next(8), kb = (n % 3 == 0) ? ka : r.Next(8);
                var a = new Vector3d(RandDouble(r, ka), RandDouble(r, ka), RandDouble(r, ka));
                var b = new Vector3d(RandDouble(r, kb), RandDouble(r, kb), RandDouble(r, kb));
                if (n % 5 == 0) b = new Vector3d(a.x + ExactInterval.Up(Math.Abs(a.x)) - Math.Abs(a.x), -a.y, ExactInterval.Up(a.z));   // neighbours, opposite, ...
                if (double.IsInfinity(a.x + b.x) || double.IsInfinity(a.y + b.y) || double.IsInfinity(a.z + b.z)) continue;
                double err;
                Vector3d m = HausdorffRounding.Midpoint(a, b, out err);
                // exact L1 error: sum_k |2 m_k - a_k - b_k| / 2, as integer times 2^e
                BigInteger total = BigInteger.Zero; int e0 = -1080;
                for (int k = 0; k < 3; ++k) {
                    Dec(m[k], out BigInteger mm, out int me); Dec(a[k], out BigInteger am, out int ae); Dec(b[k], out BigInteger bm, out int be);
                    BigInteger t = (mm << (me + 1 - e0)) - (am << (ae - e0)) - (bm << (be - e0));     // (2 m - a - b) / 2^e0
                    total += BigInteger.Abs(t);
                }
                // err >= total 2^(e0 - 1)
                Dec(err, out BigInteger em, out int ee);
                // the one value of the sum whose half rounds up to the smallest normal number (review finding): its 2^-1075 is
                // not in err; counted here, and allowed for, to see whether anything else fails
                const double Edge = 4.4501477170144023E-308;       // 2^-1021 - 2^-1074
                int edge = 0;
                for (int k = 0; k < 3; ++k) if (Math.Abs(a[k] + b[k]) == Edge) edge++;
                if (edge > 0) {
                    edgeCases++;
                    if (Cmp(em, ee, total, e0 - 1) < 0) edgeFailures++;
                    // with 2^-1075 per such component the bound holds: (err 2 + edge 2^-1074) / 2 >= total 2^(e0 - 1)
                    Assert.True(Cmp((em << (ee + 1 + 1080)) + edge * (BigInteger.One << 6), -1080, total, e0) >= 0, $"case {n}: err {err:R} below the exact L1 error even with the edge allowance");
                } else
                    Assert.True(Cmp(em, ee, total, e0 - 1) >= 0, $"case {n}: err {err:R} below the exact L1 error; a {a.x:R} {a.y:R} {a.z:R} b {b.x:R} {b.y:R} {b.z:R} m {m.x:R} {m.y:R} {m.z:R}");
                if (!total.IsZero) {
                    nonzero++;
                    double exact = Math.Exp(BigInteger.Log(total) + (e0 - 1) * Math.Log(2));
                    if (exact > 0 && err / exact > worstRatio && exact > 1e-300) worstRatio = err / exact;
                    if (err < 1e-300) sub++;
                }
            }
            output.WriteLine($"{nonzero} pairs with a rounding error, {sub} with a subnormal allowance, largest err / exact (normal range) {worstRatio}; {edgeCases} pairs with a sum of 2^-1021 - 2^-1074 in a component, {edgeFailures} of them with err below the exact error");
            Assert.True(nonzero > 50000 && sub > 5000);
        }

        // c2: MidOff >= (oa + ob) / 2 + err exactly
        [Fact]
        public void MidOff_BoundsTheExactSum()
        {
            var r = new Random(820);
            for (int n = 0; n < 400000; ++n) {
                double oa = Math.Abs(RandDouble(r, r.Next(8))), ob = Math.Abs(RandDouble(r, r.Next(8))), err = Math.Abs(RandDouble(r, r.Next(8)));
                if (n % 4 == 0) { ob = oa; }
                if (n % 7 == 0) { oa = 2.2250738585072014E-308 * (1 + r.Next(3)); ob = double.Epsilon * r.Next(0, 4); err = double.Epsilon * r.Next(0, 4); }
                if (double.IsInfinity(oa + ob) || double.IsInfinity(0.5 * (oa + ob) + err)) continue;
                // (Midpoint and MidOff return 0 or at least 2^-1073: Up of a positive value)
                if ((oa > 0 && oa < 2 * double.Epsilon) || (ob > 0 && ob < 2 * double.Epsilon) || (err > 0 && err < 2 * double.Epsilon)) continue;
                double o = HausdorffRounding.MidOff(oa, ob, err);
                Dec(oa, out BigInteger am, out int ae); Dec(ob, out BigInteger bm, out int be); Dec(err, out BigInteger em, out int ee); Dec(o, out BigInteger om, out int oe);
                int e0 = -1080;
                BigInteger exact2 = (am << (ae - e0)) + (bm << (be - e0)) + (em << (ee + 1 - e0));      // 2 x exact, / 2^e0
                Assert.True(Cmp(om, oe + 1, exact2, e0) >= 0, $"case {n}: MidOff({oa:R}, {ob:R}, {err:R}) = {o:R} below the exact value");
            }
        }

        // U0: the error of the float distance query (as OneSided.query evaluates it) against the certified lower bound, for a
        // point at height h over the interior of a triangle of size about 1 (coordinates about 1, and with an offset)
        [Fact]
        public void FloatQuery_ErrorAgainstTheExactDistance()
        {
            var r = new Random(822);
            foreach (double offset in new[] { 0.0, 1000.0, 5.7e6 })
                foreach (double h in new[] { 1e-1, 1e-3, 1e-6, 1e-9, 1e-12 }) {
                    double worstAbs = 0, worstBelow = 0, worstAbove = 0; int zeros = 0;
                    for (int n = 0; n < 2000; ++n) {
                        Func<Vector3d> v = () => new Vector3d(r.NextDouble() + offset, r.NextDouble() + offset, r.NextDouble() + offset);
                        var mesh = new DMesh3();
                        Vector3d a = v(), b = v(), c = v();
                        mesh.AppendTriangle(mesh.AppendVertex(a), mesh.AppendVertex(b), mesh.AppendVertex(c));
                        Vector3d nrm = (b - a).Cross(c - a).Normalized;
                        Vector3d p = 0.3 * a + 0.3 * b + 0.4 * c + h * nrm;
                        double f = Math.Sqrt(MeshQueries.TriangleDistance(mesh, 0, p).DistanceSquared);
                        double lb = CertifiedPointMeshDistance.LowerBound(mesh, p);        // the exact distance, rounded down by 3 ulps at most
                        double diff = f - lb;
                        worstAbs = Math.Max(worstAbs, Math.Abs(diff));
                        if (diff < 0) worstBelow = Math.Max(worstBelow, -diff); else worstAbove = Math.Max(worstAbove, diff);
                        if (f == 0) zeros++;
                    }
                    output.WriteLine($"offset {offset:G3}, height {h:G1}: |float - exact| up to {worstAbs:G3} (float below by up to {worstBelow:G3}, above by up to {worstAbove:G3}; {zeros} of 2000 float distances are 0); 2^-50 of the coordinates = {Math.Pow(2, -50) * (1 + offset):G3}");
                }
        }

        static BigInteger[] Exact(Vector3d v, int e0)
        {
            var x = new BigInteger[3];
            for (int k = 0; k < 3; ++k) { Dec(v[k], out BigInteger m, out int e); x[k] = m.IsZero ? BigInteger.Zero : m << (e - e0); }
            return x;
        }

        // section 3: LowerBound(mesh, p)^2 <= the exact squared distance from p to sampled points of every triangle (points
        // with dyadic barycentric weights, evaluated exactly), and it is not far below the float distance
        [Fact]
        public void CertifiedLowerBound_IsBelowSampledExactDistances()
        {
            var r = new Random(821);
            int positive = 0, zero = 0; double worstGap = 0;
            for (int n = 0; n < 3000; ++n) {
                int scale = (n % 4 == 0) ? r.Next(-300, 300) : (n % 4 == 1) ? r.Next(-1000, -900) : 0;
                Func<double> c = () => {
                    double v = (r.NextDouble() - 0.5) * Math.Pow(2, r.Next(-3, 4));
                    if (n % 6 == 3) v = Math.Round(v * 8) / 8;          // lattice: degenerate and touching cases
                    if (n % 6 == 5) v += 1048576.0 * r.Next(1, 3);      // an offset
                    return v * Math.Pow(2, scale);
                };
                var mesh = new DMesh3();
                int nt = (n % 3 == 0) ? 1 : r.Next(2, 30);
                for (int t = 0; t < nt; ++t) {
                    int a = mesh.AppendVertex(new Vector3d(c(), c(), c())), b = mesh.AppendVertex(new Vector3d(c(), c(), c())), d = mesh.AppendVertex(new Vector3d(c(), c(), c()));
                    if (n % 10 == 7) mesh.SetVertex(d, 0.5 * (mesh.GetVertex(a) + mesh.GetVertex(b)));       // (nearly) degenerate
                    mesh.AppendTriangle(a, b, d);
                }
                Vector3d p = new Vector3d(c(), c(), c());
                if (n % 9 == 4) p = mesh.GetVertex(r.Next(mesh.VertexCount));
                double lb = CertifiedPointMeshDistance.LowerBound(mesh, p);
                Assert.True(lb >= 0 && !double.IsNaN(lb));
                // lb^2 as an exact integer times 2^(2 le)
                Dec(lb, out BigInteger lm, out int le);
                BigInteger lb2 = lm * lm; int lb2e = 2 * le;
                double nearest = double.MaxValue;
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    Vector3d va = mesh.GetVertex(t.a), vb = mesh.GetVertex(t.b), vc = mesh.GetVertex(t.c);
                    nearest = Math.Min(nearest, Math.Sqrt(MeshQueries.TriangleDistance(mesh, tid, p).DistanceSquared));
                    int e0 = -1100;
                    BigInteger[] A = Exact(va, e0), B = Exact(vb, e0), C = Exact(vc, e0), P = Exact(p, e0);
                    // sample points (i A + j B + (16 - i - j) C) / 16: squared distance times 256, / 2^(2 e0)
                    for (int i = 0; i <= 16; i += 2)
                        for (int j = 0; i + j <= 16; j += 2) {
                            BigInteger s2 = BigInteger.Zero;
                            for (int k = 0; k < 3; ++k) {
                                BigInteger q = i * A[k] + j * B[k] + (16 - i - j) * C[k] - 16 * P[k];
                                s2 += q * q;
                            }
                            // lb2 2^lb2e <= s2 / 256 2^(2 e0)
                            Assert.True(Cmp(lb2 * 256, lb2e, s2, 2 * e0) <= 0, $"case {n}: the lower bound {lb:R} is above the distance to a point of triangle {tid}");
                        }
                }
                if (lb > 0) positive++; else zero++;
                if (nearest > 0 && nearest < double.MaxValue && lb > 0) worstGap = Math.Max(worstGap, (nearest - lb) / nearest);
                // (not a soundness check: the bound is the distance up to rounding; the float distance has its own error)
                if (scale > -900 && n % 6 != 5 && nearest > 1e-3 * Math.Pow(2, scale)) Assert.True(lb >= nearest * (1 - 1e-6) - 1e-300, $"case {n}: lower bound {lb:R} far below the float distance {nearest:R}");
            }
            output.WriteLine($"{positive} positive bounds, {zero} zero, largest (float distance - bound) / distance {worstGap}");
            Assert.True(positive > 1500);
        }
    }
}
