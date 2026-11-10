using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T1 finding R32: MeshWindingNumberSolidify.Compute threw an ArgumentException from the grid (a float cell
    /// size) for a mesh of size 1e160, 1e300 or 1e-160; at 2^k scales the result must be that at scale 1, scaled
    /// </summary>
    public class WindingSolidifyExtremeScaleTests
    {
        static DMesh3 Holed(double s)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            int top = m.VertexIndices().OrderByDescending(v => m.GetVertex(v).z).ThenBy(v => v).First();
            foreach (int t in m.VtxTrianglesItr(top).ToList()) m.RemoveTriangle(t, true, false);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, s * m.GetVertex(v));
            return m;
        }

        [Theory]
        [InlineData(MeshWindingNumberSolidify.Methods.DualContouring, -600)]
        [InlineData(MeshWindingNumberSolidify.Methods.DualContouring, 530)]
        [InlineData(MeshWindingNumberSolidify.Methods.DualContouring, 600)]
        [InlineData(MeshWindingNumberSolidify.Methods.MarchingCubes, -530)]
        [InlineData(MeshWindingNumberSolidify.Methods.MarchingCubes, 600)]
        public void Result_IsTheUnitScaleResultTimesTheScale(MeshWindingNumberSolidify.Methods method, int k)
        {
            double s = Math.Pow(2, k);
            var unit = new MeshWindingNumberSolidify(Holed(1)) { Method = method, GridResolution = 24 };
            var big = new MeshWindingNumberSolidify(Holed(s)) { Method = method, GridResolution = 24 };
            Assert.True(unit.Compute());
            Assert.True(big.Compute());
            Assert.Equal(unit.Result.TriangleCount, big.Result.TriangleCount);
            Assert.Equal(unit.Result.VertexCount, big.Result.VertexCount);
            foreach (int v in unit.Result.VertexIndices())
                Assert.True(unit.Result.GetVertex(v) * s == big.Result.GetVertex(v), "vertex " + v);
            Assert.Equal(unit.UsedCellSize * s, big.UsedCellSize);
        }
    }
}
