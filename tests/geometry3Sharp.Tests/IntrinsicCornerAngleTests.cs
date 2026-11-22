using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>The corner angles that weight the tangent normal (internal member: this file does not compile against a library without it; the public check is TangentBasis_OfAScaledMesh_IsTheSameBasis).</summary>
    public class IntrinsicCornerAngleTests
    {
        [Fact]
        public void InputCornerAngles_AreTheAnglesOfTheTriangle()
        {
            // the corner angles that weight the tangent normal: a 3-4-5 triangle (the dot product of the unnormalized edges is no angle)
            var mesh = new DMesh3();
            mesh.AppendVertex(Vector3d.Zero); mesh.AppendVertex(new Vector3d(3, 0, 0)); mesh.AppendVertex(new Vector3d(0, 4, 0));
            mesh.AppendTriangle(0, 1, 2);
            var T = new IntrinsicTriangulation(mesh);
            double[] want = { Math.PI / 2, Math.Atan2(4, 3), Math.Atan2(3, 4) };
            for (int h = 0; h < 3; ++h) Assert.True(Math.Abs(T.CornerAngle_input(h) - want[h]) < 1e-14, $"corner {h}: {T.CornerAngle_input(h)} vs {want[h]}");
        }
    }
}
