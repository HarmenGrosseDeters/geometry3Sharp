using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The accuracy of the edge angle of ExactWindingCache.boundary_edge_angle_x near the line of the edge (docs/design/
    /// EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md, "The accuracy of the edge angle"): for every p, a, b the computed angle
    /// differs from the exact atan2(num, den) of the exact differences by at most 2e-13 (the rounded evaluation 1.2e-13 where
    /// |num| + |den| >= 2^-5 (S1 + S2), which implies hypot(num, den) >= 0.0220 (S1 + S2); the evaluation from exact num and den
    /// elsewhere). The oracle is that of
    /// ExactWindingCacheInternalTestsEdgeAngleOracle (integers over the grid 2^-1074, exact square roots), independent of
    /// the code under test. The fixtures are rational (no Sin / Cos): the exact angle is computed for the doubles that come
    /// out, so a rounded fixture changes the d of a family but not the validity of the check. The class name starts with
    /// ExactWindingCacheInternalTests so that the mutation shard's filter runs it.
    /// </summary>
    public class ExactWindingCacheInternalTestsNearOpenEdge
    {
        const double Bound = 2e-13;

        // The pre-fix formula of the fast path (the rounded atan2 of num and den), as it was before the accurate evaluation was added.
        static double PreFixFormula(Vector3d p, Vector3d a, Vector3d b, out double num, out double den, out double s1, out double s2)
        {
            double ax = a.x - p.x, ay = a.y - p.y, az = a.z - p.z;
            double bx = b.x - p.x, by = b.y - p.y, bz = b.z - p.z;
            double yzA2 = ay * ay + az * az;
            double yzB2 = by * by + bz * bz;
            double la = Math.Sqrt(ax * ax + yzA2);
            double lb = Math.Sqrt(bx * bx + yzB2);
            double da = ax > 0.0 ? yzA2 / (la + ax) : la - ax;
            double db = bx > 0.0 ? yzB2 / (lb + bx) : lb - bx;
            double p1 = ay * bz;
            double p2 = az * by;
            num = p2 - p1;
            double dotYZ = ay * by + az * bz;
            den = da * db + dotYZ;
            s1 = Math.Abs(p1) + Math.Abs(p2);
            s2 = da * db + Math.Abs(ay * by) + Math.Abs(az * bz);
            // (the correctly rounded atan2 as in the library since #1627: the platform's Math.Atan2 here differed from boundary_edge_angle_x by at
            // most 1 ulp in 109 of the 39,986 compared inputs of AwayFromTheCancellation..., so the reference takes the same atan2 and the test
            // checks again what it is for, that the fix changes nothing away from the cancellation)
            return g3.CoreMathPort.CoreMath.Atan2(num, den);
        }

        // exact num = az by - ay bz of the exact differences is 0: p is on the yz line of the edge exactly, and the angle is a
        // defined limit (0, +-pi or the open-edge value), not a value of atan2 of an exact den (the oracle cannot give it)
        internal static bool IsDegenerate(Vector3d p, Vector3d a, Vector3d b)
        {
            ExactDyadic ay = ExactDyadic.Of(a.y) - ExactDyadic.Of(p.y), az = ExactDyadic.Of(a.z) - ExactDyadic.Of(p.z);
            ExactDyadic by = ExactDyadic.Of(b.y) - ExactDyadic.Of(p.y), bz = ExactDyadic.Of(b.z) - ExactDyadic.Of(p.z);
            return (az * by - ay * bz).Sign == 0;
        }

        // the error against the exact angle of the oracle (a wrong sign near +-pi is an error of 2 pi); 0 for the exactly
        // degenerate inputs (num == 0), whose value must be one of the defined limits
        static double Error(Vector3d p, Vector3d a, Vector3d b, out double got, out double exact)
        {
            got = ExactWindingCache.boundary_edge_angle_x(p, a, b);
            exact = double.NaN;
            if (IsDegenerate(p, a, b)) {
                Assert.True(Math.Abs(got) <= Math.PI, $"degenerate input gives {got:R}");
                return 0.0;
            }
            ExactWindingCacheInternalTestsEdgeAngleOracle.ExactAngle(p, a, b, out exact, out _);
            return Math.Abs(got - exact);
        }

        // (1/3) [[2, 2, -1], [-1, 2, 2], [2, -1, 2]]: a rotation with rational entries
        static Vector3d Rot(Vector3d v)
        {
            return new Vector3d((2 * v.x + 2 * v.y - v.z) / 3.0, (-v.x + 2 * v.y + 2 * v.z) / 3.0, (2 * v.x - v.y + 2 * v.z) / 3.0);
        }

        static readonly double[] Ds = { 1e-2, 3e-6, 1e-7, 1e-8, 1e-10, 1e-12, 1e-13, 1e-14, 1e-15, 1e-16, 1e-17, 1e-18 };

        // the generic family of the report: a = (0, 0.8192508924825106, 0.8566354958959876), b = -1.3 a + d perp(a), p = origin,
        // with the 2D (y, z) vectors embedded in the yz plane (x = 0), the zx plane (y = 0) and the xy plane (z = 0)
        static (Vector3d p, Vector3d a, Vector3d b) Family(int plane, double d, double k, double scale)
        {
            double a1 = 0.8192508924825106, a2 = 0.8566354958959876;
            double b1 = -k * a1 - d * a2, b2 = -k * a2 + d * a1;
            Func<double, double, Vector3d> emb = (u, v) => plane == 0 ? new Vector3d(0, u, v) : plane == 1 ? new Vector3d(v, 0, u) : new Vector3d(u, v, 0);
            // (in the zx and xy planes the offset d is added in the third axis instead, so that the yz projection is not collinear)
            Vector3d a = emb(a1, a2), b = emb(b1, b2);
            if (plane != 0) {
                b = plane == 1 ? new Vector3d(-k * a2, d, -k * a1) : new Vector3d(-k * a1, -k * a2, d);
                a = plane == 1 ? new Vector3d(a2, 0, a1) : new Vector3d(a1, a2, 0);
            }
            return (Vector3d.Zero, scale * a, scale * b);
        }

        [Fact]
        public void SimpleReproducer_IsAccurateAtEveryD()
        {
            // p = (0,0,0), a = (0,1,0), b = (0,-1,d): 1.5e-11 at d = 3e-6 and 1.15e-9 at 1e-7 before the fix
            foreach (double d in Ds.Concat(new[] { 3e-3, 1e-4, 3e-5, 1e-5, 3e-6, 1e-9, 1e-11 }))
                foreach (double sgn in new[] { 1.0, -1.0 }) {
                    double err = Error(Vector3d.Zero, new Vector3d(0, 1, 0), new Vector3d(0, -1, sgn * d), out double got, out double exact);
                    Assert.True(err <= Bound, $"d {sgn * d}: got {got:R}, exact {exact:R}, error {err:E2}");
                }
        }

        [Fact]
        public void GenericFamily_IsAccurateAtEveryD_OnBothSidesAndInThreePlanes()
        {
            int tested = 0;
            foreach (int plane in new[] { 0, 1, 2 })
                foreach (double d in Ds)
                    foreach (double sgn in new[] { 1.0, -1.0 })
                        foreach (double k in new[] { 1.3, 1.0, 0.5, 1e-3 }) {
                            var (p, a, b) = Family(plane, sgn * d, k, 1.0);
                            double err = Error(p, a, b, out double got, out double exact);
                            Assert.True(err <= Bound, $"plane {plane}, d {sgn * d}, k {k}: got {got:R}, exact {exact:R}, error {err:E2}");
                            ++tested;
                        }
            Assert.Equal(3 * Ds.Length * 2 * 4, tested);
        }

        [Fact]
        public void GenericFamily_RotatedAndOffset_IsAccurate()
        {
            // rotated by a rational rotation and far from the origin (coordinates 1e6 with features of 1: the differences are rounded)
            var off = new Vector3d(1e6, -2e6, 3e6);
            foreach (double d in Ds)
                foreach (double sgn in new[] { 1.0, -1.0 }) {
                    var (p, a, b) = Family(0, sgn * d, 1.3, 1.0);
                    Assert_Accurate(Rot(p), Rot(a), Rot(b), $"rotated, d {sgn * d}");
                    Assert_Accurate(off + p, off + a, off + b, $"offset, d {sgn * d}");
                    Assert_Accurate(Rot(off + p), Rot(off + a), Rot(off + b), $"rotated offset, d {sgn * d}");
                    // p a little off the line, in the plane of the edge and off it
                    Assert_Accurate(p + new Vector3d(0.25, 0, 0), a, b, $"x offset, d {sgn * d}");
                    Assert_Accurate(p + new Vector3d(0.25, 0.1 * d, -0.2 * d), a, b, $"yz offset, d {sgn * d}");
                }
        }

        static void Assert_Accurate(Vector3d p, Vector3d a, Vector3d b, string what)
        {
            double err = Error(p, a, b, out double got, out double exact);
            Assert.True(err <= Bound, $"{what}: p {p} a {a} b {b}: got {got:R}, exact {exact:R}, error {err:E2}");
        }

        [Fact]
        public void GenericFamily_AtExtremeMagnitudes_TakesTheRescaledPath_AndIsAccurate()
        {
            foreach (int e in new[] { 300, -300, 500, -500, 150, -150 })
                foreach (double d in Ds)
                    foreach (double sgn in new[] { 1.0, -1.0 })
                        foreach (int plane in new[] { 0, 1, 2 }) {
                            double s = Math.Pow(2, e);
                            var (p, a, b) = Family(plane, sgn * d, 1.3, s);
                            double err = Error(p, a, b, out double got, out double exact);
                            Assert.True(err <= Bound, $"2^{e}, plane {plane}, d {sgn * d}: got {got:R}, exact {exact:R}, error {err:E2}");
                        }
        }

        [Fact]
        public void YzMuchSmallerThanX_NearAntiparallel_IsAccurate()
        {
            // the rescaled path with a large x extent: |yz| 1e-20 of |x|
            foreach (double ratio in new[] { 1e-6, 1e-12, 1e-20, 1e-30 })
                foreach (double d in new[] { 1e-3, 1e-8, 1e-13, 1e-17 })
                    foreach (double xa in new[] { 1.0, -1.0 })
                        foreach (double xb in new[] { 1.0, -1.0, 0.0 }) {
                            double a1 = 0.8192508924825106 * ratio, a2 = 0.8566354958959876 * ratio;
                            var a = new Vector3d(xa * 0.7, a1, a2);
                            var b = new Vector3d(xb * 1.9, -1.3 * a1 - d * ratio * a2, -1.3 * a2 + d * ratio * a1);
                            Assert_Accurate(Vector3d.Zero, a, b, $"ratio {ratio}, d {d}, xa {xa}, xb {xb}");
                        }
        }

        [Fact]
        public void LimitCases_KeepTheirDefinedValues()
        {
            var o = Vector3d.Zero;
            // p exactly on the edge: the open-edge limit (a.z == b.z, a.x == b.x: a half turn of the other sign)
            Assert.Equal(Math.PI * 0.5, ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(0, 1, 0), new Vector3d(0, -1, 0)));
            Assert.Equal(-Math.PI * 0.5, ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(0, -1, 0), new Vector3d(0, 1, 0)));
            // on the line of the edge, beyond an end: no angle
            Assert.Equal(0.0, ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(0, 1, 0), new Vector3d(0, 2, 0)));
            Assert.Equal(0.0, ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(0, 2, 0), new Vector3d(0, 1, 0)));
            // at a vertex: the limit from the symbolic perturbation
            Assert.Equal(ExactWindingCache.boundary_edge_angle_x(o, o, new Vector3d(0, 1, 1)), ExactWindingCache.boundary_edge_angle_x(o, o, new Vector3d(0, 1, 1)));
            Assert.Equal(0.0, ExactWindingCache.boundary_edge_angle_x(o, o, o));
            // the segment in front of p (x > 0) and the yz projections antiparallel: the branch cut, a full +-pi
            double front = ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(1, 1, 0), new Vector3d(1, -1, 0));
            double back = ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(-1, 1, 0), new Vector3d(-1, -1, 0));
            Assert.True(Math.Abs(front) == Math.PI, $"{front}");
            Assert.Equal(0.0, back);
            Assert.Equal(-front, ExactWindingCache.boundary_edge_angle_x(o, new Vector3d(1, -1, 0), new Vector3d(1, 1, 0)));
        }

        // ---- bit identity where nothing changes ----

        [Fact]
        public void AwayFromTheCancellation_ReturnsTheBitsOfThePreFixFormula()
        {
            var rnd = new Random(12345);
            int same = 0, skipped = 0;
            for (int i = 0; i < 40000; ++i) {
                double s = Math.Pow(2, rnd.Next(-30, 30));
                Func<Vector3d> V = () => new Vector3d((rnd.NextDouble() * 2 - 1) * s, (rnd.NextDouble() * 2 - 1) * s, (rnd.NextDouble() * 2 - 1) * s);
                Vector3d p = V(), a = V(), b = V();
                double old = PreFixFormula(p, a, b, out double num, out double den, out double s1, out double s2);
                // inputs the fix may treat differently (hypot(num, den) below 1/16 of the sizes of the terms, the zone of the sign
                // of num) are not compared
                if (Math.Abs(num) + Math.Abs(den) < 0.0625 * (s1 + s2) || Math.Abs(num) <= 1e-9 * s1) { ++skipped; continue; }
                double got = ExactWindingCache.boundary_edge_angle_x(p, a, b);
                Assert.True(BitConverter.DoubleToInt64Bits(old) == BitConverter.DoubleToInt64Bits(got), $"p {p} a {a} b {b}: {old:R} vs {got:R}");
                ++same;
            }
            Assert.True(same > 35000, $"{same} compared, {skipped} skipped");
        }

        [Fact]
        public void RatherCancellingInputs_TakeTheSlowPath_AndAreAccurate_WhileTheRestStayBitIdentical()
        {
            // p near the yz line of the edge: the pre-fix formula is wrong there, the new one is accurate; where the
            // pre-fix formula is accurate (no cancellation) the bits agree
            var rnd = new Random(77);
            int identical = 0, fixedUp = 0;
            for (int i = 0; i < 3000; ++i) {
                double s = Math.Pow(2, rnd.Next(-20, 20));
                double r() { return (rnd.NextDouble() * 2 - 1); }
                var a = new Vector3d(r() * s, r() * s, r() * s);
                double k = 0.2 + 2 * rnd.NextDouble();
                double d = Math.Pow(10, -rnd.Next(1, 15)) * (rnd.Next(2) == 0 ? 1 : -1);
                var b = new Vector3d(r() * s, -k * a.y - d * s * a.z, -k * a.z + d * s * a.y);
                var p = Vector3d.Zero;
                double old = PreFixFormula(p, a, b, out _, out _, out _, out _);
                double got = ExactWindingCache.boundary_edge_angle_x(p, a, b);
                double err = Error(p, a, b, out _, out double exact);
                Assert.True(err <= Bound, $"p {p} a {a} b {b}: {got:R} vs exact {exact:R}");
                if (BitConverter.DoubleToInt64Bits(old) == BitConverter.DoubleToInt64Bits(got)) ++identical; else ++fixedUp;
            }
            Assert.True(fixedUp > 100 && identical > 100, $"{identical} identical, {fixedUp} changed");
        }

        // ---- the public consequences ----

        // the edge a-b of the generic family (b = -1.3 a + d perp(a)) and a third vertex c above its line: for d > 0 the origin lies
        // just outside the triangle (in its plane), for d < 0 just inside (on its surface)
        static DMesh3 Triangle(double d, bool flip)
        {
            double ay = 0.8192508924825106, az = 0.8566354958959876;
            var mesh = new DMesh3();
            int v0 = mesh.AppendVertex(new Vector3d(0, ay, az));
            int v1 = mesh.AppendVertex(new Vector3d(0, -1.3 * ay - d * az, -1.3 * az + d * ay));
            int v2 = mesh.AppendVertex(new Vector3d(0, 0.3, 5));
            if (flip) mesh.AppendTriangle(v1, v0, v2); else mesh.AppendTriangle(v0, v1, v2);
            return mesh;
        }

        [Fact]
        public void SingleTriangle_WindingNumber_NextToTheEdge_IsExactToTheBound()
        {
            // p in the plane of the triangle just outside the edge a-b: the exact winding number is 0 (before the fix:
            // -2.5e-4 at d = 1e-13, -5e-3 at 1e-14, -0.15 at 3e-16); the oracle gives the exact value for the rounded vertices
            foreach (double d in new[] { 1e-6, 1e-9, 1e-12, 1e-13, 1e-14, 1e-15, 3e-16 }) {
                foreach (bool flip in new[] { false, true }) {
                    DMesh3 mesh = Triangle(d, flip);
                    var tree = new DMeshAABBTree3(mesh, true);
                    double w = tree.WindingNumber(Vector3d.Zero);
                    double exact = ExactWinding(mesh, Vector3d.Zero);
                    Assert.True(Math.Abs(Math.Abs(exact) - 0.5) < 1e-9 || Math.Abs(w - exact) <= 1e-11, $"d {d}, flip {flip}: winding number {w:R}, exact {exact:R}");
                    if (d >= 1e-15) Assert.True(Math.Abs(w) <= 1e-11, $"d {d}, flip {flip}: winding number {w:R}");
                    Assert.Equal(w, tree.GetExactWindingCache().Evaluate(Vector3d.Zero));
                }
            }
        }

        [Fact]
        public void PointOnTheSurfaceInThePlaneX0NextToTheEdge_ValueIsZeroForBothTriangleOrders()
        {
            // p inside the triangle on its surface, within d of the edge: the value on the surface is the principal value, the
            // mean of the two sides, and a single triangle subtends +0.5 on one side and -0.5 on the other: 0. The edge angles
            // are accurate to 2e-13 each, so the value is 0 within 1e-11 (before the change +-0.5 by the sign of a rounded
            // determinant). Also for the triangles in the plane x = 0 and for both orientations.
            foreach (double d in new[] { -1e-6, -1e-12, -1e-14, -1e-16, -1.3866900198112808E-17 }) {
                var values = new List<double>();
                foreach (bool flip in new[] { false, true }) {
                    var tree = new DMeshAABBTree3(Triangle(d, flip), true);
                    values.Add(tree.WindingNumber(Vector3d.Zero));
                }
                foreach (double w in values)
                    Assert.True(Math.Abs(w) <= 1e-11, $"d {d}: winding numbers {values[0]:R}, {values[1]:R}");
            }
        }

        [Fact]
        public void IsInsideBatch_OnTheSurfaceInThePlaneX0NextToTheEdge_IsOutsideForBothTriangleOrders()
        {
            // the reported case: p on the surface of the triangle, 1.4e-17 from the edge. Before the fix the first order gave
            // 0.676 (Inside) and the second 0.324 (Outside). The value on the surface is 0 (single triangle), so the decision
            // is Outside for both orders, and it is that of the value.
            foreach (double d in new[] { -1.3866900198112808E-17, -5.1570997421607445E-17, -4.623241721084523E-16, -5.710275727938796E-15, -2.794345575409625E-14 })
                foreach (bool flip in new[] { false, true }) {
                    var tree = new DMeshAABBTree3(Triangle(d, flip), true);
                    var r = new InsideStatus[1];
                    Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, new[] { Vector3d.Zero }, r, InsideMethod.ExactWindingNumber));
                    double w = tree.WindingNumber(Vector3d.Zero);
                    Assert.True(Math.Abs(w) <= 1e-12, $"d {d:R}, flip {flip}: winding number {w:R}");
                    Assert.Equal(InsideStatus.Outside, r[0]);
                }
        }

        // ---- open meshes sampled near their open edges against an exact oracle ----

        static BigInteger Grid(double x)
        {
            ExactDyadic d = ExactDyadic.Of(x);
            return d.M.IsZero ? BigInteger.Zero : d.M << (d.E + 1074);
        }

        static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign <= 0) return BigInteger.Zero;
            BigInteger x = BigInteger.One << (int)((ExactPoint3.BitLength(n) + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        // the solid angle / (4 pi) of the triangle seen from p (Van Oosterom and Strackee: 2 atan2(det, |a||b||c| + (a.b)|c| +
        // (b.c)|a| + (c.a)|b|)), det and the denominator from exact integers (fixed point with 2^200 for the square roots)
        static double ExactTriangleWinding(Vector3d p, Vector3d v0, Vector3d v1, Vector3d v2)
        {
            BigInteger[] a = { Grid(v0.x) - Grid(p.x), Grid(v0.y) - Grid(p.y), Grid(v0.z) - Grid(p.z) };
            BigInteger[] b = { Grid(v1.x) - Grid(p.x), Grid(v1.y) - Grid(p.y), Grid(v1.z) - Grid(p.z) };
            BigInteger[] c = { Grid(v2.x) - Grid(p.x), Grid(v2.y) - Grid(p.y), Grid(v2.z) - Grid(p.z) };
            Func<BigInteger[], BigInteger[], BigInteger> dot = (u, v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
            BigInteger det = a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0]);
            const int K = 200;
            BigInteger la = ISqrt(dot(a, a) << (2 * K)), lb = ISqrt(dot(b, b) << (2 * K)), lc = ISqrt(dot(c, c) << (2 * K));
            BigInteger den = la * lb * lc + (dot(a, b) << (2 * K)) * lc + (dot(b, c) << (2 * K)) * la + (dot(c, a) << (2 * K)) * lb;
            BigInteger num = det << (3 * K);
            if (num.IsZero)
                return den.Sign >= 0 ? 0.0 : 0.5;
            long bits = Math.Max(ExactPoint3.BitLength(BigInteger.Abs(num)), ExactPoint3.BitLength(BigInteger.Abs(den)));
            int sh = (int)Math.Max(0, bits - 900);
            double y = (double)(num >> sh), x = (double)(den >> sh);
            if (y == 0.0) y = num.Sign >= 0 ? double.Epsilon : -double.Epsilon;
            double half = ExactWindingCacheInternalTestsEdgeAngleOracle.ExactAtan2(y, x, out _);
            return 2.0 * half / (4.0 * Math.PI);
        }

        static double ExactWinding(DMesh3 mesh, Vector3d p)
        {
            double w = 0.0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                w += ExactTriangleWinding(p, mesh.GetVertex(tri.a), mesh.GetVertex(tri.b), mesh.GetVertex(tri.c));
            }
            return w;
        }

        // an open box [0,1]^3 without its top (the rim is the 4 edges at z = 1), outward oriented
        static DMesh3 OpenBox()
        {
            var m = new DMesh3();
            int[] v = new int[8];
            for (int i = 0; i < 8; ++i)
                v[i] = m.AppendVertex(new Vector3d(i & 1, (i >> 1) & 1, (i >> 2) & 1));
            Action<int, int, int, int> quad = (p0, p1, p2, p3) => { m.AppendTriangle(v[p0], v[p1], v[p2]); m.AppendTriangle(v[p0], v[p2], v[p3]); };
            quad(0, 2, 3, 1);       // bottom (z = 0), normal -z
            quad(0, 1, 5, 4);       // y = 0
            quad(1, 3, 7, 5);       // x = 1
            quad(3, 2, 6, 7);       // y = 1
            quad(2, 0, 4, 6);       // x = 0
            return m;
        }

        // a square annulus in the plane z = 0: outer [-2, 2]^2, hole [-1, 1]^2, 8 quads on the 4 x 4 lattice
        static DMesh3 AnnulusWithHole()
        {
            var m = new DMesh3();
            int[,] id = new int[4, 4];
            double[] c = { -2, -1, 1, 2 };
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    id[i, j] = m.AppendVertex(new Vector3d(c[i], c[j], 0));
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j) {
                    if (i == 1 && j == 1) continue;
                    m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]);
                    m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
                }
            return m;
        }

        // points next to the open edges at 10^-1 .. 10^-14 from them, in directions around the edge (rational direction
        // (1 - q^2, 2 q) / (1 + q^2): no Sin / Cos)
        static List<Vector3d> NearEdgePoints(DMesh3 mesh, int seed, int[] distances = null)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            foreach (int e in mesh.BoundaryEdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                Vector3d a = mesh.GetVertex(ev.a), b = mesh.GetVertex(ev.b);
                Vector3d dir = (b - a).Normalized;
                Vector3d u = Math.Abs(dir.x) < 0.9 ? Vector3d.AxisX.Cross(dir).Normalized : Vector3d.AxisY.Cross(dir).Normalized;
                Vector3d w = dir.Cross(u);
                for (int k = 0; k < 14; ++k) {
                    double t = k < 2 ? 0.001 + 0.998 * k : rnd.NextDouble();       // (k < 2: near the end points, so near a vertex)
                    double q = 0.2 + 4.0 * rnd.NextDouble();
                    if (distances != null && Array.IndexOf(distances, k) < 0) continue;     // (the random numbers are drawn either way)
                    double r = Math.Pow(10, -1 - k);
                    double cs = (1 - q * q) / (1 + q * q), sn = 2 * q / (1 + q * q);
                    pts.Add(a + t * (b - a) + r * (cs * u + sn * w));
                }
            }
            return pts;
        }

        [Fact]
        public void OpenBox_NearItsOpenEdges_IsWithinTheBoundOfTheExactWinding()
        {
            Assert_NearTheOpenEdges_WithinTheBound(OpenBox());
        }

        [Fact]
        public void AnnulusWithHole_NearItsOpenEdges_IsWithinTheBoundOfTheExactWinding()
        {
            Assert_NearTheOpenEdges_WithinTheBound(AnnulusWithHole());
        }

        // (8 of the 14 distances 10^-1 .. 10^-14 of NearEdgePoints, and one test per mesh, each about 1 s: the oracle of the whole mesh
        // per point costs most; the distances left out are covered by the families above and by the invariance test)
        static void Assert_NearTheOpenEdges_WithinTheBound(DMesh3 mesh)
        {
            int n = 0;
            var tree = new DMeshAABBTree3(mesh, true);
            ExactWindingCache cache = tree.GetExactWindingCache();
            Assert.True(cache.BoundaryCount > 0);
            foreach (Vector3d p in NearEdgePoints(mesh, 3, new[] { 0, 1, 3, 5, 7, 9, 11, 13 })) {
                double exact = ExactWinding(mesh, p);
                double w = cache.Evaluate(p);
                ++n;
                // (the exact winding number of an open mesh jumps across its surface: a point on it within rounding is a tie)
                if (Math.Abs(Math.Abs(exact) - 0.5) < 1e-9) continue;
                Assert.True(Math.Abs(w - exact) <= 1e-11, $"p {p.x:R} {p.y:R} {p.z:R}: {w:R} vs exact {exact:R}, triangles {mesh.TriangleCount}");
                Assert.Equal(w, tree.WindingNumber(p));
            }
            Assert.True(n > 30, $"{n} points");
        }

        // ---- the standard set ----

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WindingNumberNearTheOpenEdges_IsInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = OpenBox();
            var tree = new DMeshAABBTree3(mesh, true);
            var treeT = new DMeshAABBTree3(Invariance.Transformed(mesh, sim), true);
            int n = 0;
            foreach (Vector3d p in NearEdgePoints(mesh, 8)) {
                double w = tree.WindingNumber(p), wT = treeT.WindingNumber(sim.Apply(p));
                // the 2^k frames bit for bit (also at 1e-14 from an edge); a rounded frame moves p by 1e-16 of its size, which
                // matters only where p is within about 1e-6 of the edge (the UTM frame: 1e-9): compare the points farther than 1e-2 of it
                if (sim.IsExact) {
                    Assert.True(BitConverter.DoubleToInt64Bits(w) == BitConverter.DoubleToInt64Bits(wT), $"{name}: p {p}: {w:R} vs {wT:R}");
                    ++n;
                } else if (EdgeDistance(mesh, p) > 1e-2) {
                    Assert.True(Math.Abs(w - wT) <= 1e-5, $"{name}: p {p}: {w:R} vs {wT:R}");
                    ++n;
                }
            }
            Assert.True(n >= 4, $"{n} points");
        }

        static double EdgeDistance(DMesh3 mesh, Vector3d p)
        {
            double best = double.MaxValue;
            foreach (int e in mesh.BoundaryEdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                var seg = new Segment3d(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b));
                best = Math.Min(best, seg.DistanceSquared(p));
            }
            return Math.Sqrt(best);
        }

        [Fact]
        public void BatchDecisionsNearTheOpenEdges_AreDeterministic_ParallelEqualsSerial_AndTheCacheIsReusable()
        {
            DMesh3 mesh = OpenBox();
            Vector3d[] pts = NearEdgePoints(mesh, 21).ToArray();
            var tree = new DMeshAABBTree3(mesh, true);
            var serial = new InsideStatus[pts.Length];
            var parallel = new InsideStatus[pts.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts, serial, InsideMethod.ExactWindingNumber, new BatchOptions { Parallel = false }));
            for (int rep = 0; rep < 3; ++rep) {
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts, parallel, InsideMethod.ExactWindingNumber, new BatchOptions { Parallel = true }));
                Assert.Equal(serial, parallel);
            }
            Assert.Contains(InsideStatus.Inside, serial);
            Assert.Contains(InsideStatus.Outside, serial);
            // reuse: one cache instance, the same values the second time, and those of a fresh instance
            ExactWindingCache cache = tree.GetExactWindingCache();
            ExactWindingCache fresh = new DMeshAABBTree3(mesh, true).GetExactWindingCache();
            foreach (Vector3d p in pts) {
                double w1 = cache.Evaluate(p), w2 = cache.Evaluate(p), w3 = fresh.Evaluate(p);
                Assert.True(BitConverter.DoubleToInt64Bits(w1) == BitConverter.DoubleToInt64Bits(w2) && BitConverter.DoubleToInt64Bits(w1) == BitConverter.DoubleToInt64Bits(w3), $"p {p}");
            }
        }

        static Vector3d Scale(Vector3d v, double f) { return new Vector3d(v.x * f, v.y * f, v.z * f); }

        // (v * f) / f == v: the scaling by a power of two loses no bit (no underflow into the subnormal range, no overflow)
        static bool ScalesExactly(Vector3d v, double f)
        {
            Vector3d w = Scale(v, f);
            return w.x / f == v.x && w.y / f == v.y && w.z / f == v.z;
        }

        [Fact]
        public void SubnormalProducts_DoNotFlipTheSignOfNum_AtTheBranchCut()
        {
            // S1 = |ay bz| + |az by| below 2^-1000: the products are rounded to the subnormal grid (absolute error up to 2^-1075), and the
            // rounded num can have the wrong sign while den < 0: +pi instead of -pi, a winding number off by 1 (review of #1514).
            // Exact Orient2d of (p, a, b) in yz is 1. The input, its mirror images (a and b swapped: +pi; z negated: +pi) and the
            // 2^k scalings that lose no bit, from the subnormal products up (the rescaled path takes over below 2^-100 and above 2^100).
            var p = new Vector3d(0, 5.334489550943501e-77, 0);
            var a = new Vector3d(7.888609052210118e-31, 6.963444032852826e-61, 1.3734963702551047e-262);
            var b = new Vector3d(7.888609052210118e-31, -7.374133604637322e-61, -1.4545023542892055e-262);
            Assert.Equal(1, ExactPredicates.Orient2d(new Vector2d(p.y, p.z), new Vector2d(a.y, a.z), new Vector2d(b.y, b.z)));
            int n = 0;
            foreach (int k in new[] { 0, 1, -1, 7, -7, 50, -50, 100, -100, 300, -300, 500 }) {
                double f = Math.Pow(2, k);
                Vector3d ps = Scale(p, f), as_ = Scale(a, f), bs = Scale(b, f);
                if (!ScalesExactly(p, f) || !ScalesExactly(a, f) || !ScalesExactly(b, f)) continue;
                ++n;
                foreach (bool swap in new[] { false, true })
                    foreach (bool negZ in new[] { false, true }) {
                        Vector3d a2 = swap ? bs : as_, b2 = swap ? as_ : bs;
                        Vector3d p2 = ps;
                        if (negZ) { a2 = new Vector3d(a2.x, a2.y, -a2.z); b2 = new Vector3d(b2.x, b2.y, -b2.z); }
                        double err = Error(p2, a2, b2, out double got, out double exact);
                        Assert.True(err <= Bound, $"2^{k}, swap {swap}, negZ {negZ}: got {got:R}, exact {exact:R}, error {err:E2}");
                        // (mirrored once: the other side of the cut, +pi; mirrored twice, or not at all: -pi)
                        double want = (swap ^ negZ) ? Math.PI : -Math.PI;
                        Assert.True(Math.Abs(got - want) <= 1e-13, $"2^{k}, swap {swap}, negZ {negZ}: got {got:R}");
                    }
            }
            Assert.True(n >= 8, $"{n} scalings");
        }

        [Fact]
        public void SubnormalProducts_NeighbourhoodOfTheReviewerInput_IsAccurate()
        {
            // the products of num around the subnormal grid: a.z and b.z scaled so that S1 runs from 2^-1060 to 2^-900
            var p = new Vector3d(0, 5.334489550943501e-77, 0);
            var a = new Vector3d(7.888609052210118e-31, 6.963444032852826e-61, 1.3734963702551047e-262);
            var b = new Vector3d(7.888609052210118e-31, -7.374133604637322e-61, -1.4545023542892055e-262);
            int n = 0;
            for (int e = -40; e <= 60; e += 5)
                foreach (double r in new[] { 1.0, 1.0000000000000002, 0.9999999999999999, 0.5, 1.5, 3.0 }) {
                    double f = Math.Pow(2, e) * r;
                    var a2 = new Vector3d(a.x, a.y, a.z * f);
                    var b2 = new Vector3d(b.x, b.y, b.z * (1.0 / r) * Math.Pow(2, e));
                    double err = Error(p, a2, b2, out double got, out double exact);
                    Assert.True(err <= Bound, $"e {e}, r {r}: p {p} a {a2} b {b2}: got {got:R}, exact {exact:R}, error {err:E2}");
                    ++n;
                }
            Assert.Equal(21 * 6, n);
        }

        [Fact]
        public void LongEdges_NearTheOpenSegment_AreAccurate_AtThePrecisionOfTheExactEvaluation()
        {
            // an edge of length 2^60 .. 2^100 times its yz extent with p near it: the exact evaluation needs k = 70 + lenBits + 2 - bits(num)
            // bits (a fixed 70 gives 0.07710721005579022 instead of 0.077087507655067 at X = 1.2345678901234567 2^60 and 3.14159 instead of
            // 0.0771621882431914 at 2^100, review of #1514)
            var p = new Vector3d(0.1, 0.3, -0.2);
            foreach (int e in new[] { 40, 50, 60, 70, 80, 90, 100 })
                foreach (double xf in new[] { 1.2345678901234567, 1.0, 1.9999999999999998 }) {
                    double X = xf * Math.Pow(2, e);
                    var a = new Vector3d(p.x - X, p.y + 0.7071067811865476, p.z + 0.1);
                    var b = new Vector3d(p.x + 0.9 * X, p.y - 0.6364, p.z - 0.09);
                    double err = Error(p, a, b, out double got, out double exact);
                    Assert.True(err <= Bound, $"X = {xf:R} 2^{e}: got {got:R}, exact {exact:R}, error {err:E2}");
                }
        }

        [Fact]
        public void OracleIsExact_BelowTheGridOfTheInput()
        {
            // p = 0, a = (-2^100, 1, 0), b = (2^100, -1, 2^-1074): den = da db - 1 is far below 2^-1074 and num = -2^-1074; the angle is
            // -pi/2 (an oracle without extra bits below the grid says -pi), and so is the library's
            var a = new Vector3d(-Math.Pow(2, 100), 1, 0);
            var b = new Vector3d(Math.Pow(2, 100), -1, double.Epsilon);
            ExactWindingCacheInternalTestsEdgeAngleOracle.ExactAngle(Vector3d.Zero, a, b, out double exact, out _);
            Assert.True(Math.Abs(exact + Math.PI * 0.5) <= 1e-15, $"{exact:R}");
            double got = ExactWindingCache.boundary_edge_angle_x(Vector3d.Zero, a, b);
            Assert.True(Math.Abs(got - exact) <= Bound, $"{got:R}");
        }

        [Fact]
        public void TinyNumAgainstAHugeDen_KeepsTheSignOfNum()
        {
            // a = (0, 2^1020, +-e), b = (3 e, -e, 0) with e = 2^-1074 (the smallest subnormal): num = az by - ay bz = -+e^2 =
            // -+2^-2148, den = da db + ay by + az bz = 0.162 * 2^-54 - 2^-54 < 0. The exact angle is -+pi (within 1e-600), and the
            // double of num / den underflows: only the sign of num decides between -pi and +pi.
            double big = Math.Pow(2, 1020), e = double.Epsilon;
            foreach (double sgn in new[] { 1.0, -1.0 }) {
                var a = new Vector3d(0, big, sgn * e);
                var b = new Vector3d(3 * e, -e, 0.0);
                double got = ExactWindingCache.boundary_edge_angle_x(Vector3d.Zero, a, b);
                Assert.True(Math.Abs(got + sgn * Math.PI) <= 1e-13, $"sign {sgn}: {got:R}");
            }
        }
    }
}
