using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Solid angles and the winding number at every binary scale (found by tools/MeshFuzzPilot, the tree-winding target):
    /// MathUtil.TriSolidAngle is a quotient of cubes of the coordinates, which underflow below 1e-103 (0) and overflow
    /// above 1e102 (NaN).
    /// </summary>
    public class WindingNumberScaleTests
    {
        static double Pow2(int e) => Math.Pow(2, e);

        [Theory]
        [InlineData(-1000)]
        [InlineData(-700)]
        [InlineData(-400)]
        [InlineData(-340)]
        [InlineData(-90)]
        [InlineData(90)]
        [InlineData(340)]
        [InlineData(400)]
        [InlineData(700)]
        [InlineData(1000)]
        public void TriSolidAngle_IsTheSameDoubleAtEveryPowerOfTwo(int e)
        {
            Vector3d a = new Vector3d(1, 0, 0), b = new Vector3d(0, 1.5, 0), c = new Vector3d(0, 0, 2.25), p = new Vector3d(0.125, 0.0625, 0.03125);
            double expected = MathUtil.TriSolidAngle(a, b, c, ref p);
            Assert.InRange(expected, 0.1, 6);
            double s = Pow2(e);
            Vector3d sp = s * p;
            Assert.Equal(expected, MathUtil.TriSolidAngle(s * a, s * b, s * c, ref sp));
        }

        static DMesh3 Octahedron(double scale)
        {
            var m = new DMesh3();
            var v = new[] { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1) };
            foreach (var p in v) m.AppendVertex(scale * p);
            int[] t = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            for (int i = 0; i < t.Length; i += 3) m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
            return m;
        }

        [Theory]
        [InlineData(1e-300)]
        [InlineData(1e-150)]
        [InlineData(1e-110)]
        [InlineData(1)]
        [InlineData(1e110)]
        [InlineData(1e150)]
        [InlineData(1e300)]
        public void WindingNumber_OfAClosedMesh_IsOneInsideAndZeroOutside(double scale)
        {
            DMesh3 m = Octahedron(scale);
            var tree = new DMeshAABBTree3(m, true);
            Assert.Equal(1.0, tree.WindingNumber(new Vector3d(0.1, 0.05, 0.02) * scale), 9);
            Assert.Equal(0.0, tree.WindingNumber(new Vector3d(2, 0.5, 0.25) * scale), 9);
            Assert.Equal(1.0, m.WindingNumber(new Vector3d(0.1, 0.05, 0.02) * scale), 9);
        }
    }
}
