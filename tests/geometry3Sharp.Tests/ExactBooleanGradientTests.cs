using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Research prototype checks for docs/research/EXACT_BOOLEAN_GRADIENTS_2026-11.md: the exact volume gradient of a
    /// Boolean result (Harness/BooleanGradient.cs) against exact central differences at steps 2^-k, its invariance under
    /// Similarity.All(), the one-sided behaviour at contact positions, and the area gradient against differences in double.
    /// </summary>
    public class ExactBooleanGradientTests
    {
        readonly ITestOutputHelper output;
        public ExactBooleanGradientTests(ITestOutputHelper output) { this.output = output; }

        // inputs on the 2^-24 grid, so x + h delta is exact in double for h = 2^-k, k <= 44
        static double Grid(double x) => Math.Round(x * (1 << 24)) / (1 << 24);

        static DMesh3 Unit() => BooleanFuzzShapes.Box(0, 0, 0, 1, 1, 1);

        // a box rotated about a generic axis, its corners rounded to the grid (the faces are then slightly bent: generic)
        static DMesh3 RotatedBox()
        {
            Matrix3d r = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27);
            var c = new Vector3d(0.6, 0.55, 0.5);
            return BooleanFuzzShapes.Map(BooleanFuzzShapes.Box(-0.4, -0.35, -0.3, 0.4, 0.35, 0.3), p => {
                Vector3d q = r * p + c;
                return new Vector3d(Grid(q.x), Grid(q.y), Grid(q.z));
            });
        }

        static DMesh3 ShiftedBox() => BooleanFuzzShapes.Box(0.5, 0.25, 0.375, 1.5, 1.25, 1.375);

        public static IEnumerable<object[]> GenericCases()
        {
            foreach (string shape in new[] { "axis", "rotated" })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    yield return new object[] { shape, op };
        }

        static DMesh3 Second(string shape) => shape == "axis" ? ShiftedBox() : RotatedBox();

        // a fixed direction: small integer vectors per vertex of both meshes
        static Vector3d[][] Direction(DMesh3 a, DMesh3 b, int seed)
        {
            var rand = new Random(seed);
            return new[] { a, b }.Select(m => Enumerable.Range(0, m.MaxVertexID)
                .Select(_ => new Vector3d(rand.Next(-3, 4), rand.Next(-3, 4), rand.Next(-3, 4))).ToArray()).ToArray();
        }

        static DMesh3 Moved(DMesh3 m, Vector3d[] d, double h)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) {
                Vector3d p = c.GetVertex(v), q = p + h * d[v];
                if (q - h * d[v] != p) throw new InvalidOperationException("the step is not exact");
                c.SetVertex(v, q);
            }
            return c;
        }

        static BigRational VolumeAt(DMesh3 a, DMesh3 b, Ops op, Vector3d[][] d, double h)
            => BooleanGradient.Compute(Moved(a, d[0], h), Moved(b, d[1], h), op, area: false).Volume;

        static double AreaAt(DMesh3 a, DMesh3 b, Ops op, Vector3d[][] d, double h)
            => BooleanGradient.Compute(Moved(a, d[0], h), Moved(b, d[1], h), op).Area;

        static double Rel(BigRational x, BigRational y) => Math.Abs(BooleanGradient.D(x - y)) / Math.Max(1e-300, Math.Abs(BooleanGradient.D(y)));

        [Theory]
        [MemberData(nameof(GenericCases))]
        public void VolumeGradient_MatchesExactCentralDifferences(string shape, Ops op)
        {
            DMesh3 a = Unit(), b = Second(shape);
            Vector3d[][] d = Direction(a, b, 7);
            BooleanGradient g = BooleanGradient.Compute(a, b, op, area: false);
            BigRational dir = g.VolumeDirectional(d);
            Assert.True(g.KeptPieces > 0);
            // the result's exact volume against the library's double volume of the rounded result
            var bo = new ExactMeshBoolean(a, b, op);
            Assert.True(bo.Compute());
            Assert.Equal(Invariance.Volume(bo.Result), BooleanGradient.D(g.Volume), 9);
            double prev = double.NaN;
            foreach (int k in new[] { 8, 12, 16, 20, 24, 28, 32, 36, 40 }) {
                double h = ExactPoint3.ScaleB(1, -k);
                BigRational fd = (VolumeAt(a, b, op, d, h) - VolumeAt(a, b, op, d, -h)) / (BooleanGradient.Pow2(1 - k));
                double err = Rel(fd, dir);
                output.WriteLine($"{shape} {op} k={k} dV={BooleanGradient.D(dir):R} fd={BooleanGradient.D(fd):R} rel.err={err:E3} ratio={(prev / err):F2}");
                // O(h^2): the central difference of a locally rational function; a wrong gradient leaves a constant error
                Assert.True(err <= 64 * h * h, $"k={k}: relative error {err:E3}");
                prev = err;
            }
        }

        [Fact]
        public void VolumeGradient_Similarities_FollowExactly()
        {
            DMesh3 a = Unit(), b = RotatedBox();
            BooleanGradient g = BooleanGradient.Compute(a, b, Ops.Union, area: false);
            foreach (Similarity s in Similarity.All()) {
                BooleanGradient gs = BooleanGradient.Compute(Invariance.Transformed(a, s), Invariance.Transformed(b, s), Ops.Union, area: false);
                BigRational s2 = BigRational.Of(s.Scale) * BigRational.Of(s.Scale), s3 = s2 * BigRational.Of(s.Scale);
                Assert.Equal(g.VolumeGradient.Count, gs.VolumeGradient.Count);
                double worst = 0;
                foreach (var kv in g.VolumeGradient) {
                    BigRational[] x = kv.Value, y = gs.VolumeGradient[kv.Key];
                    if (s.IsExact) {
                        // 2^k scales: the scaled gradient is s^2 times the gradient, as rationals
                        for (int k = 0; k < 3; ++k) Assert.Equal(0, (y[k] - x[k] * s2).Sign);
                    } else {
                        Vector3d back = s.Rotation.Transpose() * new Vector3d(BooleanGradient.D(y[0]), BooleanGradient.D(y[1]), BooleanGradient.D(y[2])) / (s.Scale * s.Scale);
                        Vector3d orig = new Vector3d(BooleanGradient.D(x[0]), BooleanGradient.D(x[1]), BooleanGradient.D(x[2]));
                        worst = Math.Max(worst, (back - orig).Length);
                    }
                }
                if (s.IsExact) Assert.Equal(0, (gs.Volume - g.Volume * s3).Sign);
                output.WriteLine($"{s.Name}: exact={s.IsExact} worst |R^T g'/s^2 - g| = {worst:E3}");
                // otherwise the transformed input is rounded; the gradient is Lipschitz in the input
                Assert.True(worst < 1e-6, s.Name);
            }
        }

        // translation of the second mesh along axis
        static Vector3d[][] Translate(DMesh3 a, DMesh3 b, Vector3d t)
            => new[] { new Vector3d[a.MaxVertexID], Enumerable.Repeat(t, b.MaxVertexID).ToArray() };

        static void OneSided(DMesh3 a, DMesh3 b, Ops op, Vector3d[][] d, int k, out BigRational left, out BigRational right, out BigRational grad)
        {
            double h = ExactPoint3.ScaleB(1, -k);
            BigRational v0 = VolumeAt(a, b, op, d, 0), ih = BooleanGradient.Pow2(k);
            left = (v0 - VolumeAt(a, b, op, d, -h)) * ih;
            right = (VolumeAt(a, b, op, d, h) - v0) * ih;
            grad = BooleanGradient.Compute(a, b, op, area: false).VolumeDirectional(d);
        }

        [Theory]
        // face contact of area 1/4 at x = 1 (B moves along +x, away from A): the result's own branch
        [InlineData(Ops.Union, 0.25, 0.0, 0.25)]          // merged: the contact faces are dropped, the overlapping side
        [InlineData(Ops.Intersection, -0.25, 0.0, 0.0)]   // empty: the separated side
        [InlineData(Ops.Difference, 0.25, 0.0, 0.0)]      // A unchanged: the separated side
        public void FaceContact_GradientIsTheResultsOneSidedDerivative(Ops op, double left, double right, double grad)
        {
            DMesh3 a = Unit(), b = BooleanFuzzShapes.Box(1, 0.25, 0.25, 2, 0.75, 0.75);
            Vector3d[][] d = Translate(a, b, Vector3d.AxisX);
            OneSided(a, b, op, d, 20, out BigRational l, out BigRational r, out BigRational g);
            output.WriteLine($"{op}: left {BooleanGradient.D(l)} right {BooleanGradient.D(r)} gradient {BooleanGradient.D(g)}");
            // V is piecewise linear in the translation: the one-sided quotients are exact
            Assert.Equal(0, (l - BigRational.Of(left)).Sign);
            Assert.Equal(0, (r - BigRational.Of(right)).Sign);
            Assert.Equal(0, (g - BigRational.Of(grad)).Sign);
        }

        [Theory]
        // B's top is flush with A's top on [0.5,1]x[0.25,0.75]; B's top moves up: right derivative 1/2 (all of B's top),
        // left 1/4 (its part outside A). The coplanar part is kept once, from the first triangle of the stack (input order).
        [InlineData(false, 0.25)]
        [InlineData(true, 0.5)]
        public void FlushFaces_TieBreakByInputOrderPicksOneSide(bool swapped, double grad)
        {
            DMesh3 a = Unit(), b = BooleanFuzzShapes.Box(0.5, 0.25, 0.5, 1.5, 0.75, 1);
            var top = b.VertexIndices().Select(v => b.GetVertex(v).z == 1 ? Vector3d.AxisZ : Vector3d.Zero).ToArray();
            Vector3d[][] d = { new Vector3d[a.MaxVertexID], top };
            DMesh3 first = swapped ? b : a, second = swapped ? a : b;
            Vector3d[][] ds = swapped ? new[] { d[1], d[0] } : d;
            OneSided(first, second, Ops.Union, ds, 20, out BigRational l, out BigRational r, out BigRational g);
            output.WriteLine($"swapped={swapped}: left {BooleanGradient.D(l)} right {BooleanGradient.D(r)} gradient {BooleanGradient.D(g)}");
            Assert.Equal(0, (l - BigRational.Of(0.25)).Sign);
            Assert.Equal(0, (r - BigRational.Of(0.5)).Sign);
            Assert.Equal(0, (g - BigRational.Of(grad)).Sign);
        }

        // a square prism (diamond cross section in xz) whose lowest edge lies on A's top face
        static DMesh3 EdgeOnTop() => BooleanFuzzShapes.Map(Unit(), p => new Vector3d(0.5 + 0.25 * (p.x - p.z), 0.25 + 0.5 * p.y, 1 + 0.25 * (p.x + p.z)));

        [Fact]
        public void EdgeContact_VolumeIsDifferentiable_AreaIsNot()
        {
            DMesh3 a = Unit(), b = EdgeOnTop();
            Vector3d[][] d = Translate(a, b, Vector3d.AxisZ);
            // volume: the overlap is a wedge of volume t^2 / 2, so both one-sided quotients go to the gradient (0) like h
            foreach (int k in new[] { 10, 20 }) {
                OneSided(a, b, Ops.Union, d, k, out BigRational l, out BigRational r, out BigRational g);
                output.WriteLine($"k={k}: volume left {BooleanGradient.D(l):E3} right {BooleanGradient.D(r):E3} gradient {BooleanGradient.D(g)}");
                Assert.Equal(0, g.Sign);
                Assert.Equal(0, r.Sign);
                Assert.Equal(0, (l - BigRational.Of(ExactPoint3.ScaleB(0.5, -k))).Sign);
            }
            // area: the union loses a strip of A's top (width 2t) and of two of B's faces (width t sqrt 2): left derivative
            // 1 + sqrt 2, right 0; the gradient is the right one (B rigid, A unchanged)
            double h = ExactPoint3.ScaleB(1, -20);
            double a0 = AreaAt(a, b, Ops.Union, d, 0);
            double al = (a0 - AreaAt(a, b, Ops.Union, d, -h)) / h, ar = (AreaAt(a, b, Ops.Union, d, h) - a0) / h;
            double ga = BooleanGradient.Compute(a, b, Ops.Union).AreaDirectional(d);
            output.WriteLine($"area left {al:R} right {ar:R} gradient {ga:R}");
            Assert.True(Math.Abs(al - (1 + Math.Sqrt(2))) < 4 * h, $"left {al}");   // (the strips' ends: an O(h) term)
            Assert.Equal(0, ar, 6);
            Assert.Equal(0, ga, 9);
        }

        [Theory]
        [MemberData(nameof(GenericCases))]
        public void AreaGradient_MatchesCentralDifferences(string shape, Ops op)
        {
            DMesh3 a = Unit(), b = Second(shape);
            Vector3d[][] d = Direction(a, b, 11);
            BooleanGradient g = BooleanGradient.Compute(a, b, op);
            double dir = g.AreaDirectional(d);
            Assert.True(g.SeamEdges > 0);
            var bo = new ExactMeshBoolean(a, b, op);
            Assert.True(bo.Compute());
            Assert.Equal(Invariance.Area(bo.Result), g.Area, 9);
            foreach (int k in new[] { 12, 16, 20, 24 }) {
                double h = ExactPoint3.ScaleB(1, -k);
                double fd = (AreaAt(a, b, op, d, h) - AreaAt(a, b, op, d, -h)) / (2 * h);
                double err = Math.Abs(fd - dir) / Math.Abs(dir);
                output.WriteLine($"{shape} {op} k={k} dA={dir:R} fd={fd:R} rel.err={err:E3}");
                if (k >= 16) Assert.True(err < 1e-6, $"k={k}: relative error {err:E3}");
            }
        }

        [Fact]
        public void AreaGradient_GrowsLikeOneOverSinTheta_AtASliverSeam()
        {
            // A n B where B's bottom is tilted by slope theta against A's top and cuts it along y = 0.5: the kept pieces
            // form a wedge of angle theta, and moving B down moves the seam by t / theta (no bound on dA as theta -> 0)
            double prev = 0;
            foreach (int j in new[] { 4, 8, 12 }) {
                double th = ExactPoint3.ScaleB(1, -j);
                DMesh3 a = Unit();
                DMesh3 b = BooleanFuzzShapes.Map(BooleanFuzzShapes.Box(0.25, -0.5, 0, 0.75, 1.5, 1), p => new Vector3d(p.x, p.y, p.z == 0 ? 1 + th * (p.y - 0.5) : 2));
                Vector3d[][] d = Translate(a, b, -Vector3d.AxisZ);
                double ga = BooleanGradient.Compute(a, b, Ops.Intersection).AreaDirectional(d);
                double h = th * ExactPoint3.ScaleB(1, -16);
                double fd = (AreaAt(a, b, Ops.Intersection, d, h) - AreaAt(a, b, Ops.Intersection, d, -h)) / (2 * h);
                output.WriteLine($"theta=2^-{j}: dA={ga:R} fd={fd:R} theta*dA={th * ga:R}");
                Assert.Equal(fd, ga, 4);
                Assert.True(Math.Abs(ga) > 0.9 * Math.Abs(prev) * 16);
                prev = ga;
            }
        }
    }
}
