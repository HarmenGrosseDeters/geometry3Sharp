using System;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DMeshAABBTree3.FastWindingNumber has no absolute constants (#693): the far-field expansion of a box is used by its
    /// centre and radius alone, at any scale and any distance from the origin. Before, (a) the areas of its cache came from
    /// DMesh3.GetTriInfo, whose Normalize has the absolute epsilon 2.2e-16: every triangle with edges below about 1e-8
    /// had area 0, the centres of all boxes of such a part were 0/0, and every query summed all of its triangles exactly;
    /// (b) the expansion was gated by the padded box (padding 2^-40 of the distance from the origin), so the boxes of a
    /// small part far from the origin contained the queries near them. MeshRobustRepair works in a unit frame: two unit
    /// spheres with a tetrahedron 1e8 or 1e12 away became spheres of size 4e-8 / 4e-12 there, and the volume checks
    /// took 60 s of a 62 s repair. (The number of triangles summed exactly: FastWindingExpansionUsedTests.)
    /// </summary>
    [Collection("FastWindingParallelDepth")]
    public class FastWindingScaleTests
    {
        // RobustRepairFarPartTests.Scene scaled into the unit frame of MeshRobustRepair (largest extent in [1, 2)):
        // the spheres of size about 2.5 / far
        internal static DMesh3 UnitFrameScene(double far, out double scale)
        {
            DMesh3 m = RobustRepairFarPartTests.Scene(far);
            scale = MeshRepairMetrics.unit_scale_for_mesh(m, true, 0, 0);
            MeshTransforms.Scale(m, scale);
            return m;
        }

        // points around the two spheres (of the unscaled scene)
        internal static Vector3d[] Points(int n, int seed = 3)
        {
            var rnd = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => new Vector3d(-1.2 + 2.9 * rnd.NextDouble(), -1.2 + 2.7 * rnd.NextDouble(), -1.2 + 2.6 * rnd.NextDouble())).ToArray();
        }

        [Theory]
        [InlineData(1e8)]
        [InlineData(1e12)]
        public void FarPart_UnitFrame_ValuesAsAtUnitScale(double far)
        {
            double scale;
            DMesh3 m = UnitFrameScene(far, out scale);
            Assert.True(scale < 1e-7, $"scale {scale}");
            var tree = new DMeshAABBTree3(m, true);
            DMesh3 unit = RobustRepairFarPartTests.Scene(far);
            var unitTree = new DMeshAABBTree3(unit, true);
            tree.FastWindingNumber(Vector3d.Zero);
            // (a) the cache of every box has a finite centre and radius
            int cached = 0;
            for (int b = 0; b < tree.BoxCountForTests; ++b) {
                Vector3d c, o1; double r; Matrix3d o2;
                if (tree.TryGetFastWindingCoefficients(b, out c, out r, out o1, out o2)) {
                    cached++;
                    Assert.True(c.IsFinite && !double.IsNaN(r) && o1.IsFinite, $"box {b}: centre {c}, radius {r}");
                }
            }
            Assert.True(cached > 100);
            Vector3d[] points = Points(400);
            foreach (Vector3d p in points) {
                Vector3d q = scale * p;
                double fast = tree.FastWindingNumber(q), reference = tree.WindingNumber(q);
                Assert.True(Math.Abs(fast - reference) < 0.1, $"far {far}, at {p}: fast {fast} vs exact {reference}");
                // what the scene gives unscaled (scale is a power of two): the expansion, not an exact sum
                double atUnit = unitTree.FastWindingNumber(p);
                Assert.True(BitConverter.DoubleToInt64Bits(fast) == BitConverter.DoubleToInt64Bits(atUnit), $"far {far}, at {p}: {fast:R} scaled vs {atUnit:R} unscaled");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void FastWindingNumber_Invariant(string name)
        {
            // the scene of MeshRobustRepair's unit frame at 1e8 (spheres of size 4e-8), transformed
            Similarity s = Similarity.ByName(name);
            double scale;
            DMesh3 m = UnitFrameScene(1e8, out scale);
            var reference = new DMeshAABBTree3(m, true);
            DMesh3 t = Invariance.Transformed(m, s);
            var tree = new DMeshAABBTree3(t, true);
            Vector3d[] points = Points(200, 9);
            foreach (Vector3d p0 in points) {
                Vector3d p = scale * p0, q = s.Apply(p);
                double expected = reference.FastWindingNumber(p), fast = tree.FastWindingNumber(q);
                if (s.IsExact)
                    Assert.True(BitConverter.DoubleToInt64Bits(fast) == BitConverter.DoubleToInt64Bits(expected), $"{name}, at {p0}: {fast:R} vs {expected:R}");
                else
                    // (rotated or translated: another tree, so other expansions; the reference is the exact winding number
                    // of the transformed mesh, whose coordinates at UTM round the spheres of size 4e-8 to 1e-10)
                    Assert.True(Math.Abs(fast - tree.WindingNumber(q)) < 0.1, $"{name}, at {p0}: fast {fast} vs exact {tree.WindingNumber(q)}");
            }
        }

        [Theory]
        [InlineData(-200)]
        [InlineData(-205)]
        [InlineData(-210)]
        [InlineData(-230)]
        [InlineData(-260)]
        [InlineData(200)]
        [InlineData(210)]
        [InlineData(250)]
        public void ExtremeScales_FiniteAndClose(int exponent)
        {
            // the expansion divides by the fifth power of the distance: where that leaves the normal range (sizes below
            // about 2^-204 or above 2^204) the exact sum must be taken, not a NaN (a sphere of size 2^-210 gave NaN for
            // every query once the areas were scale-free and the gate had no range check)
            double s = BitConverter.Int64BitsToDouble((long)(1023 + exponent) << 52);
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            MeshTransforms.Scale(m, s);
            var tree = new DMeshAABBTree3(m, true);
            var rnd = new Random(2);
            for (int i = 0; i < 200; ++i) {
                Vector3d q = s * new Vector3d(6 * rnd.NextDouble() - 3, 6 * rnd.NextDouble() - 3, 6 * rnd.NextDouble() - 3);
                double fast = tree.FastWindingNumber(q), reference = tree.WindingNumber(q);
                Assert.True(Math.Abs(fast - reference) < 0.1, $"2^{exponent}, query {i}: fast {fast} vs exact {reference}");
            }
        }

        [Theory]
        [InlineData(4.5)]
        [InlineData(5.0)]
        [InlineData(6.0)]
        public void AboveTheUpperRange_ExactSumNotAWrongExpansion(double distance)
        {
            // a unit sphere scaled by 2^202, queries on a shell at distance * 2^202 from its centre: every box is about
            // 2^204.1 .. 2^204.6 away, where 4 pi d^5 overflows. The gate (d^5 <= 2^1019) leaves the expansion out there and
            // the value is the exact sum; with d^5 <= MaxValue the second-order term lost its d (x) d part (finite, 18 %
            // off: about 5e-4 here; numerics review of #744). At unit scale the same queries use the expansion
            double s = BitConverter.Int64BitsToDouble((long)(1023 + 202) << 52);
            DMesh3 unit = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            DMesh3 m = new DMesh3(unit);
            MeshTransforms.Scale(m, s);
            var unitTree = new DMeshAABBTree3(unit, true);
            var tree = new DMeshAABBTree3(m, true);
            var rnd = new Random(4);
            double expandedAtUnit = 0;
            for (int i = 0; i < 300; ++i) {
                Vector3d dir = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                Vector3d q = distance * dir;
                double fast = tree.FastWindingNumber(s * q), reference = tree.WindingNumber(s * q);
                Assert.True(Math.Abs(fast - reference) < 1e-9, $"distance {distance}, query {i}: fast {fast:R} vs exact {reference:R}");
                expandedAtUnit = Math.Max(expandedAtUnit, Math.Abs(unitTree.FastWindingNumber(q) - unitTree.WindingNumber(q)));
            }
            // (the control: at unit scale the expansion is used and differs from the exact sum)
            Assert.True(expandedAtUnit > 1e-9, $"distance {distance}: unit scale {expandedAtUnit}");
        }

        [Fact]
        public void FastWindingNumber_Reuse_And_ParallelBuildEqualsSerial()
        {
            double scale;
            DMesh3 m = UnitFrameScene(1e8, out scale);
            Vector3d[] points = Points(200, 11).Select(p => scale * p).ToArray();
            // the reference: the plain recursion of the cache build, on this thread
            double[] serial = null;
            int depth = DMeshAABBTree3.FastWindingParallelDepth;
            DMeshAABBTree3.FastWindingParallelDepth = 0;
            gParallel.SerialOnThisThread = true;
            try {
                var t0 = new DMeshAABBTree3(m, true);
                serial = points.Select(p => t0.FastWindingNumber(p)).ToArray();
            } finally {
                gParallel.SerialOnThisThread = false;
                DMeshAABBTree3.FastWindingParallelDepth = depth;
            }
            for (int run = 0; run < 3; ++run) {
                var tree = new DMeshAABBTree3(m, true);
                // twice on one tree (the cache of the first pass reused), and the parallel cache build
                for (int pass = 0; pass < 2; ++pass) {
                    double[] w = new double[points.Length];
                    gParallel.ForEach(Enumerable.Range(0, points.Length), (i) => w[i] = tree.FastWindingNumber(points[i]));
                    for (int i = 0; i < points.Length; ++i)
                        Assert.True(BitConverter.DoubleToInt64Bits(serial[i]) == BitConverter.DoubleToInt64Bits(w[i]), $"run {run}, pass {pass}, point {i}: {w[i]:R} vs {serial[i]:R}");
                }
            }
        }

        [Fact]
        public void FarPart_UnitFrame_VolumeCheck_TimeBounded()
        {
            // #693: the volume check of MeshRobustRepair on the scene in its unit frame (MeshRepairMetrics.VolumeIoUAtLeast
            // of a mesh with itself: the samples of the input's and the result's solid, each by a FastWindingNumber). The
            // bound is far above the time after and below the time before (4 cores: 2.8 s before, 21 ms after)
            double scale;
            DMesh3 m = UnitFrameScene(1e8, out scale);
            var sw = Stopwatch.StartNew();
            double iou;
            bool atLeast = MeshRepairMetrics.VolumeIoUAtLeast(m, new DMesh3(m), 0.9, out iou);
            long ms = sw.ElapsedMilliseconds;
            Assert.True(atLeast, $"iou {iou}");
            Assert.True(iou > 0.99, $"iou {iou}");
            Assert.True(ms < BoundMs, $"{ms} ms");
        }
        const long BoundMs = 800;
    }
}
