using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The floating-point filters of ExactMeshBoolean (orient3d_filtered, plane_side_filtered), checked against exact
    /// signs: where a filter decides, its sign must be the sign for every exact input within the documented error of
    /// the doubles (relative 2^-52 per coordinate and coefficient), at all corners of the error boxes (the values are
    /// affine in each coordinate, so the corners are the worst case); nearly degenerate inputs, offsets far from the
    /// origin, magnitudes from just above TinyCoordinate to 2^300. And the filters must decide for well separated
    /// input, else they would only cost time. Found with the mutation tests (tests/mutation, stryker-mesh-boolean-2):
    /// mutants of the bounds survived, because no test compared the filters with exact values.
    /// </summary>
    public class ExactBooleanFilterTests
    {
        const int S = 1100;
        const double U = 2.220446049250313e-16;

        static BigInteger Q(double v)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            return m.IsZero ? BigInteger.Zero : (m << (e + S));
        }

        // the two ends of the error box of c: lo, hi (scaled by 2^S)
        static BigInteger[] Box(double c)
        {
            BigInteger q = Q(c), e = Q(U * Math.Abs(c));
            return new[] { q - e, q + e };
        }

        static BigInteger Corner(BigInteger[] box, int mask, int bit) => box[(mask >> bit) & 1];

        static double Rnd(Random r, int k) => (r.NextDouble() - 0.5) * Math.Pow(2, k + 1);

        static int[] Exponents = { -295, -200, -40, -3, 0, 10, 52, 60, 300 };

        static void AssertOrient3dAtCorners(Vector3d a, Vector3d b, Vector3d c, Vector3d d, int f, string what)
        {
            Vector3d[] v = { a, b, c, d };
            var boxes = new BigInteger[4, 3][];
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 3; ++j)
                    boxes[i, j] = Box(v[i][j]);
            var X = new BigInteger[4, 3];
            for (int mask = 0; mask < 4096; ++mask) {
                for (int i = 0; i < 4; ++i)
                    for (int j = 0; j < 3; ++j)
                        X[i, j] = Corner(boxes[i, j], mask, 3 * i + j);
                BigInteger ux = X[1, 0] - X[0, 0], uy = X[1, 1] - X[0, 1], uz = X[1, 2] - X[0, 2];
                BigInteger vx = X[2, 0] - X[0, 0], vy = X[2, 1] - X[0, 1], vz = X[2, 2] - X[0, 2];
                BigInteger wx = X[3, 0] - X[0, 0], wy = X[3, 1] - X[0, 1], wz = X[3, 2] - X[0, 2];
                BigInteger det = wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
                if (-det.Sign != f)
                    Assert.Fail($"{what} mask={mask}: filter {f}, exact {-det.Sign}");
            }
        }

        // d moved off the plane of a, b, c by growing powers of two: the first offsets where the filter decides are
        // within a factor of about 2 of its error bound, where a bound that is too small gives wrong signs
        [Fact]
        public void Orient3dFiltered_SignHoldsAtAllCornersOfTheErrorBoxes()
        {
            var r = new Random(11);
            int cases = 0;
            foreach (int k in Exponents)
                foreach (int off in new[] { int.MinValue, k + 20, k + 45 })
                    for (int iter = 0; iter < 2; ++iter) {
                        double o = (off == int.MinValue) ? 0 : Math.Pow(2, off);
                        Func<Vector3d> p = () => new Vector3d(o + Rnd(r, k), o + Rnd(r, k), o + Rnd(r, k));
                        Vector3d a = p(), b = p(), c = p();
                        Vector3d n = (b - a).Cross(c - a).Normalized;
                        double s = r.NextDouble(), t = r.NextDouble();
                        Vector3d d0 = a + s * (b - a) + t * (c - a);
                        // (the unmoved point too: undecided, or decided correctly)
                        int f0 = ExactMeshBoolean.orient3d_filtered(ref a, ref b, ref c, ref d0);
                        if (f0 != 2)
                            AssertOrient3dAtCorners(a, b, c, d0, f0, $"orient3d k={k} off={off} iter={iter} unmoved");
                        int found = 0;
                        for (int j = -60; j <= 0 && found < 2; ++j) {
                            double sign = ((j + iter) % 2 == 0) ? 1 : -1;
                            Vector3d d = d0 + sign * Math.Pow(2, Math.Max(k, off == int.MinValue ? k : off) + j) * n;
                            int f = ExactMeshBoolean.orient3d_filtered(ref a, ref b, ref c, ref d);
                            if (f == 2)
                                continue;
                            AssertOrient3dAtCorners(a, b, c, d, f, $"orient3d k={k} off={off} iter={iter} j={j}");
                            found++;
                            cases++;
                        }
                    }
            Assert.True(cases > 40, $"{cases} decided cases");
        }

        [Fact]
        public void Orient3dFiltered_DecidesForSeparatedPoints_AndNotForTinyOnes()
        {
            var r = new Random(12);
            // (the determinant of 2^3k must stay above the permanent's underflow guard of 1e-280 and finite)
            foreach (int k in new[] { -90, -40, -3, 0, 10, 52, 60, 90 })
                for (int iter = 0; iter < 50; ++iter) {
                    double m = Math.Pow(2, k);
                    // (coordinates between 2^k and 2^(k+1) in magnitude: none below TinyCoordinate)
                    Vector3d a = new Vector3d(m * (1 + r.NextDouble()), -m * (1 + r.NextDouble()), m * (1 + r.NextDouble())), b = a + new Vector3d(Math.Pow(2, k), 0, 0),
                        c = a + new Vector3d(0, Math.Pow(2, k), 0), d = a + new Vector3d(0, 0, Math.Pow(2, k) * (iter % 2 == 0 ? 1 : -1));
                    // det3 [b-a; c-a; d-a] = +-2^3k: the sign of det4 is its negative
                    Assert.Equal(iter % 2 == 0 ? -1 : 1, ExactMeshBoolean.orient3d_filtered(ref a, ref b, ref c, ref d));
                }
            Vector3d z = Vector3d.Zero, x = Vector3d.AxisX, y = Vector3d.AxisY, t = new Vector3d(0, 0, 1e-200);
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered(ref z, ref x, ref y, ref t));
            Vector3d nan = new Vector3d(double.NaN, 0, 1), inf = new Vector3d(double.PositiveInfinity, 0, 1);
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered(ref z, ref x, ref y, ref nan));
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered(ref z, ref x, ref y, ref inf));
        }

        [Fact]
        public void PlaneSideFiltered_SignHoldsForRoundedCoefficientsAndPoints()
        {
            var r = new Random(13);
            int decided = 0, cases = 0;
            foreach (int bits in new[] { 10, 53, 54, 80, 200 })
                foreach (int k in new[] { -40, 0, 20, 60 })
                    for (int iter = 0; iter < 20; ++iter) {
                        BigInteger A = new BigInteger(Rnd(r, 50)) << Math.Max(0, bits - 50), B = new BigInteger(Rnd(r, 50)) << r.Next(0, Math.Max(1, bits - 50)),
                            C = new BigInteger(Rnd(r, 30)) + 1;
                        double x = Rnd(r, k), y = Rnd(r, k), z = Rnd(r, k);
                        // D so that the point is nearly on the plane: A x + B y + C z rounded down to an integer;
                        // then moved by growing powers of two until the filter decides (near its bound)
                        BigInteger D0 = (A * Q(x) + B * Q(y) + C * Q(z)) >> S;
                        int found = 0;
                        for (int j = -1; j < 400 && found < 2; ++j) {
                            BigInteger D = (j < 0) ? D0 : D0 + ((iter % 2 == 0 ? BigInteger.One : BigInteger.MinusOne) << j);
                            double[] pa = { (double)A, (double)B, (double)C, (double)D };
                            int f = ExactMeshBoolean.plane_side_filtered(pa, x, y, z);
                            cases++;
                            if (f == 2)
                                continue;
                            decided++;
                            found++;
                            // every exact point in the error box of (x, y, z) is on the side f of every plane whose
                            // coefficients are within 2^-52 of pa (here A, B and C are exact doubles, D is rounded;
                            // the boxes are taken around all four, as the contract allows)
                            BigInteger[][] cb = { Box(x), Box(y), Box(z) }, pb = { Box(pa[0]), Box(pa[1]), Box(pa[2]), Box(pa[3]) };
                            for (int mask = 0; mask < 128; ++mask) {
                                BigInteger v = -Corner(pb[3], mask, 6) * (BigInteger.One << S);
                                for (int i = 0; i < 3; ++i)
                                    v += Corner(pb[i], mask, i + 3) * Corner(cb[i], mask, i);
                                if (v.Sign != f)
                                    Assert.Fail($"plane side bits={bits} k={k} iter={iter} j={j} mask={mask}: filter {f}, exact {v.Sign}");
                            }
                        }
                    }
            Assert.True(decided > 300, $"decided {decided} of {cases}");
            // well off the plane: decides; tiny coordinates and overflow: does not
            Assert.Equal(1, ExactMeshBoolean.plane_side_filtered(new double[] { 1, 2, 3, 4 }, 1, 1, 1));
            Assert.Equal(-1, ExactMeshBoolean.plane_side_filtered(new double[] { 1, 2, 3, 7 }, 1, 1, 1));
            Assert.Equal(2, ExactMeshBoolean.plane_side_filtered(new double[] { 1, 2, 3, 6 }, 1, 1, 1));
            Assert.Equal(2, ExactMeshBoolean.plane_side_filtered(new double[] { 1, 0, 0, 0 }, 1e-200, 1, 1));
            Assert.Equal(2, ExactMeshBoolean.plane_side_filtered(new double[] { 1e300, 1e300, 0, 0 }, 1e300, 1e300, 1));
        }
    }
}
