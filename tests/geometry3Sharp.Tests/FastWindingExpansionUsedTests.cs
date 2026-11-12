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

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void FarPart_UnitFrame_Transformed_FewTrianglesExact(string name)
        {
            Similarity s = Similarity.ByName(name);
            double scale;
            DMesh3 m = FastWindingScaleTests.UnitFrameScene(1e8, out scale);
            // the expansion is used wherever the coordinates resolve the spheres: at UTM coordinates their size 4e-8 (4e-11
            // after the millimetre scale) rounds to a few units in the last place, degenerate triangles that are summed
            // exactly by right
            if (!(s.Resolution(2) < 1e-4 * 2.5 * scale))
                return;
            var tree = new DMeshAABBTree3(Invariance.Transformed(m, s), true);
            double perQuery = ExactPerQuery(tree, FastWindingScaleTests.Points(200, 9), scale, s.Apply, 0, name);
            Assert.True(perQuery < 0.05 * NearTriangles, $"{name}: {perQuery} of {NearTriangles} triangles summed exactly per query");
        }
    }
}
