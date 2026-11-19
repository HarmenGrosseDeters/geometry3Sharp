using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact degenerate-triangle guard of the band (ContourLoft.cs, "a band triangle is degenerate"), which the double-precision test of the
    /// separated planes does not make redundant: the second contour has a vertex exactly on the line of an edge of the first one, hence exactly in
    /// its plane, but the rounded plane test of the working frame sees it on the correct side of the plane.
    /// </summary>
    public class CoverageGate2BContourLoftTests
    {
        // integer coordinates (exactly representable); B[0] = A[0] + 2 * (A[1] - A[0]) lies on the line of the edge A[0]A[1] of A
        static List<Vector3d> A() => new List<Vector3d> { new Vector3d(-5, -3, -2), new Vector3d(-9, 2, 0), new Vector3d(-8, -5, -5) };
        static List<Vector3d> B() => new List<Vector3d> { new Vector3d(-13, 7, 2), new Vector3d(-15, 8, 6), new Vector3d(-13, -6, -4) };

        [Fact]
        public void ComputeBand_ReportsADegenerateBandTriangle_AsInvalidInput()
        {
            Assert.Equal(B()[0], A()[0] + 2 * (A()[1] - A()[0]));
            ContourLoftBandTriangles r = new ContourLoft().ComputeBand(A(), B());
            Assert.Equal(ContourLoftStatus.InvalidInput, r.Status);
            Assert.Contains("a band triangle is degenerate", r.Reason);
            Assert.Contains("not in separated planes", r.Reason);
            Assert.Null(r.Triangles);
        }

        [Fact]
        public void Compute_ReportsTheSameDegenerateBandTriangle_AsInvalidInput()
        {
            // the same input through the public operation (the mesh-building twin of the check)
            ContourLoftResult r = new ContourLoft().Compute(A(), B());
            Assert.Equal(ContourLoftStatus.InvalidInput, r.Status);
            Assert.Contains("a band triangle is degenerate", r.Reason);
            Assert.Null(r.Mesh);
        }

        [Fact]
        public void ComputeBand_OfNonDegenerateContours_Succeeds()
        {
            // control: moving the collinear vertex off the plane gives a band
            var b = B();
            b[0] = new Vector3d(-13, 7, 3);
            ContourLoftBandTriangles r = new ContourLoft().ComputeBand(A(), b);
            Assert.Equal(ContourLoftStatus.Success, r.Status);
            Assert.Equal(6, r.Triangles.Count);
        }
    }
}
