using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The far-field expansion of FastWindingNumber is used, counted by the test hook FastWindingExactTriangleCount (the
    /// triangles a query sums exactly; it shares the gate with the evaluation). Scenes of FastWindingScaleTests (#693):
    /// before, every query near the spheres summed all 8664 of their triangles in the unit frame (areas 0 by an absolute
    /// epsilon), and several thousand at 1e12 in the scene's own coordinates (the gate by the padded box).
    /// </summary>
    public class FastWindingExpansionUsedTests
    {
        static int NearTriangles => RobustRepairFarPartTests.Scene(0).TriangleCount;

        static double ExactPerQuery(DMeshAABBTree3 tree, Vector3d[] points, double scale, Func<Vector3d, Vector3d> map, double maxError, string label)
        {
            long exact = 0;
            foreach (Vector3d p0 in points) {
                Vector3d q = map(scale * p0);
                double fast = tree.FastWindingNumber(q);
                if (maxError > 0)
                    Assert.True(Math.Abs(fast - tree.WindingNumber(q)) < maxError, $"{label}, at {p0}: fast {fast} vs exact {tree.WindingNumber(q)}");
                exact += tree.FastWindingExactTriangleCount(q);
            }
            return (double)exact / points.Length;
        }

        [Theory]
        [InlineData(1e8)]
        [InlineData(1e12)]
        public void FarPart_UnitFrame_FewTrianglesExact(double far)
        {
            double scale;
            DMesh3 m = FastWindingScaleTests.UnitFrameScene(far, out scale);
            var tree = new DMeshAABBTree3(m, true);
            double perQuery = ExactPerQuery(tree, FastWindingScaleTests.Points(200), scale, q => q, 0.1, $"far {far}");
            Assert.True(perQuery < 0.05 * NearTriangles, $"far {far}: {perQuery} of {NearTriangles} triangles summed exactly per query");
        }

        [Theory]
        [InlineData(1e3)]
        [InlineData(1e12)]
        public void FarPart_Unscaled_FewTrianglesExact(double far)
        {
            // the gate alone: in the scene's own coordinates the areas are fine; at 1e12 the padding of the boxes is 1.8
            var tree = new DMeshAABBTree3(RobustRepairFarPartTests.Scene(far), true);
            double perQuery = ExactPerQuery(tree, FastWindingScaleTests.Points(200, 5), 1, q => q, 0.1, $"far {far}");
            Assert.True(perQuery < 0.05 * NearTriangles, $"far {far}: {perQuery} of {NearTriangles} triangles summed exactly per query");
        }

        [Fact]
        public void BetaBelowSqrt3_GateByThePaddedBox()
        {
            // below sqrt(3) the box test still matters and keeps the padded box of before (a point on a triangle stays in the
            // boxes above it under rounding): at 1e12 (padding 1.8) the boxes near the spheres contain the queries, so many
            // triangles are summed exactly, as on the old code; from sqrt(3) on the tight box (FarPart_Unscaled_FewTrianglesExact)
            DMesh3 m = RobustRepairFarPartTests.Scene(1e12);
            var low = new DMeshAABBTree3(m, true) { FWNBeta = 1.7 };
            var high = new DMeshAABBTree3(m, true) { FWNBeta = Math.Sqrt(3) };
            Vector3d[] points = FastWindingScaleTests.Points(100, 5);
            double perQueryLow = ExactPerQuery(low, points, 1, q => q, 0.1, "beta 1.7");
            double perQueryHigh = ExactPerQuery(high, points, 1, q => q, 0.1, "beta sqrt(3)");
            Assert.True(perQueryLow > 0.2 * NearTriangles, $"beta 1.7: {perQueryLow} of {NearTriangles} triangles summed exactly per query");
            Assert.True(perQueryHigh < 0.1 * NearTriangles, $"beta sqrt(3): {perQueryHigh} of {NearTriangles} triangles summed exactly per query");
        }

        // the similarities whose coordinates resolve the spheres of the unit-frame scene at 1e8 (size 4e-8): at UTM
        // coordinates (4e-11 after the millimetre scale) they round to a few units in the last place, degenerate triangles
        // that are summed exactly by right (FastWindingScaleTests.FastWindingNumber_Invariant covers those)
        public static System.Collections.Generic.IEnumerable<object[]> Resolved()
        {
            double scale;
            FastWindingScaleTests.UnitFrameScene(1e8, out scale);
            return Similarity.All().Where(s => s.Resolution(2) < 1e-4 * 2.5 * scale).Select(s => new object[] { s.Name });
        }

        [Fact]
        public void Resolved_CoversTheScalesAndRotations()
        {
            var names = Resolved().Select(o => (string)o[0]).ToList();
            foreach (Similarity s in Similarity.All().Where(s => s.Translation == Vector3d.Zero))
                Assert.Contains(s.Name, names);
        }

        [Theory]
        [MemberData(nameof(Resolved))]
        public void FarPart_UnitFrame_Transformed_FewTrianglesExact(string name)
        {
            Similarity s = Similarity.ByName(name);
            double scale;
            DMesh3 m = FastWindingScaleTests.UnitFrameScene(1e8, out scale);
            var tree = new DMeshAABBTree3(Invariance.Transformed(m, s), true);
            double perQuery = ExactPerQuery(tree, FastWindingScaleTests.Points(200, 9), scale, s.Apply, 0, name);
            Assert.True(perQuery < 0.05 * NearTriangles, $"{name}: {perQuery} of {NearTriangles} triangles summed exactly per query");
        }
    }
}
