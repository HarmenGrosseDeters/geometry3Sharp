using System;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A hull domain whose input points are collinear has no area and is empty, also with a snap tolerance: snapping moves a point by up
    /// to half a pixel and used to turn collinear points into a triangle (found by the QualityMesh2 CsCheck campaign, T5; owner decision
    /// 2026-11-10, option B: snapping never turns a zero-area input into a mesh).
    /// </summary>
    public class QualityMesh2SnapDegenerateTests
    {
        /// <summary> (2, 4), (3, 3), (0, 6) in units of 2^-8: on the line x + y = 6; pixel 2^-7, so (3, 3) is the pixel centre (4, 4) </summary>
        static Mesh2Input Collinear(int exponent, double pixelUnits, double? angle, bool constrained)
        {
            double unit = Math.Pow(2, -8 + exponent);
            var input = new Mesh2Input { X = new[] { 2 * unit, 3 * unit, 0 }, Y = new[] { 4 * unit, 3 * unit, 6 * unit }, SnapTolerance = pixelUnits * unit,
                MinAngleDeg = angle, ConstrainedQuality = constrained };
            input.Segments.Add(new SegmentInput(0, 1));
            input.Segments.Add(new SegmentInput(0, 2));
            return input;
        }

        [Theory]
        [InlineData(0, 0.0, true)]
        [InlineData(-100, 0.0, true)]
        [InlineData(60, 0.0, true)]
        [InlineData(0, null, false)]
        [InlineData(-100, null, false)]
        public void CollinearPoints_WithASnapTolerance_AreEmpty(int exponent, double? angle, bool constrained)
        {
            var input = Collinear(exponent, 2.0, angle, constrained);
            var output = angle == null ? new ConstrainedDelaunayMesh2Candidate().Run(input) : new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Empty, output.Status);
            Assert.Empty(output.Triangles);
            // the vertices are the snapped points: (3, 3) in units of 2^-8 moved to the pixel centre (4, 4), the others are pixel centres
            double unit = Math.Pow(2, -8 + exponent);
            Assert.Equal(new[] { 2 * unit, 4 * unit, 0 }, output.VX);
            Assert.Equal(new[] { 4 * unit, 4 * unit, 6 * unit }, output.VY);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = angle == null ? Milestone.M2 : Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary> an outer ring over collinear points has no area either, and the mesher can be reused </summary>
        [Fact]
        public void CollinearRing_WithASnapTolerance_IsEmpty_AndTheMesherIsReusable()
        {
            double unit = Math.Pow(2, -8);
            var ring = new g3.ConstrainedMesh2Input {
                Points = new System.Collections.Generic.List<g3.Vector2d> { new g3.Vector2d(2 * unit, 4 * unit), new g3.Vector2d(3 * unit, 3 * unit), new g3.Vector2d(0, 6 * unit) },
                Rings = new System.Collections.Generic.List<g3.Mesh2Ring> { new g3.Mesh2Ring(new[] { 0, 1, 2 }) } };
            var mesher = new g3.QualityMesh2 { MinAngle = 20, SnapTolerance = 2 * unit };
            Assert.Equal(g3.Triangulation2Status.Empty, mesher.Compute(ring).Status);
            Assert.Equal(g3.Triangulation2Status.Empty, mesher.Compute(ring).Status);
        }

        /// <summary> the same without a tolerance is empty too, and a hull with area keeps its mesh when snapped </summary>
        [Fact]
        public void WithoutATolerance_AndWithArea_NothingChanges()
        {
            var plain = Collinear(0, 0, 20, true);
            Assert.Equal(Mesh2Status.Empty, new QualityMesh2Candidate().Run(plain).Status);
            double unit = Math.Pow(2, -8);
            var area = new Mesh2Input { X = new[] { 16 * unit, 24 * unit, 0, 30 * unit }, Y = new[] { 32 * unit, 16 * unit, 48 * unit, 40 * unit }, SnapTolerance = unit, MinAngleDeg = 20, ConstrainedQuality = true };
            Assert.Equal(Mesh2Status.Success, new QualityMesh2Candidate().Run(area).Status);
        }
    }
}
