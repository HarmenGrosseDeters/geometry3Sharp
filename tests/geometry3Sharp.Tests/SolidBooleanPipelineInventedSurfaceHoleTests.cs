using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using V = gs.SolidBooleanPipeline.VerificationModes;
using Status = gs.SolidBooleanPipeline.PipelineStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A repaired hole counts as invented surface (docs/guides/booleans-and-imprint.md, NoInventedSurface): a body with a
    /// pinhole (one missing triangle) whose repair is the obvious one is refused by the default limit MaxInventedSurfaceShare = 0,
    /// although the result is the right Boolean of the repaired body; a limit above the share of the cap accepts it.
    /// The fixture: the unit box without one triangle of its face x = 1, intersected with [0.5, 2] x [-1, 2]^2. The right
    /// result is the half box [0.5, 1] x [0, 1]^2 (volume 0.5, area 4); the cap is the missing triangle (area 0.5), all of it
    /// in the result, so the share is 0.5 / 4 = 0.125.
    /// </summary>
    public class SolidBooleanPipelineInventedSurfaceHoleTests
    {
        const ExactMeshBoolean.Operations I = ExactMeshBoolean.Operations.Intersection;
        const double CapShare = 0.125, CapArea = 0.5;

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        // the unit box without one of the two triangles of its face x = 1
        static DMesh3 BoxWithPinhole()
        {
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            int hole = m.TriangleIndices().First(t => {
                Index3i tv = m.GetTriangle(t);
                return m.GetVertex(tv.a).x == 1 && m.GetVertex(tv.b).x == 1 && m.GetVertex(tv.c).x == 1;
            });
            m.RemoveTriangle(hole);
            Assert.False(m.IsClosed());
            Assert.Equal(11, m.TriangleCount);
            return m;
        }

        static SolidBooleanPipeline Pipeline(V verification, double limit)
        {
            return new SolidBooleanPipeline(new List<DMesh3> { BoxWithPinhole(), Box(0.5, -1, -1, 2, 2, 2) }, I) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, Verification = verification, MaxInventedSurfaceShare = limit
            };
        }

        static void AssertCapMeasured(SolidBooleanPipeline p)
        {
            Assert.Single(p.Report.RepairedInputs);
            Assert.Equal(0, p.Report.RepairedInputs[0].Index);
            Assert.True(Math.Abs(p.Report.InventedSurfaceShare - CapShare) <= 1e-12, p.Report.InventedSurfaceShare.ToString("R"));
            Assert.True(Math.Abs(p.Report.InventedSurfaceArea - CapArea) <= 1e-12, p.Report.InventedSurfaceArea.ToString("R"));
            Assert.True(p.Report.InventedTriangles >= 1);
            Assert.True(p.Report.InventedSurfaceMaxDistance > p.Report.InventedSurfaceTolerance);
        }

        static void AssertHalfBox(SolidBooleanPipeline p)
        {
            Assert.Equal(Status.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.Equal(0.5, Invariance.Volume(r), 12);
        }

        [Fact]
        public void APinhole_AtTheDefaultLimit_IsRefused_WithTheCapsShare()
        {
            SolidBooleanPipeline p = Pipeline(V.NoInventedSurface, 0);
            Assert.Equal(0, p.MaxInventedSurfaceShare);
            Assert.False(p.Compute());
            Assert.Equal(Status.VerificationFailed, p.Status);
            Assert.Null(p.Result);
            Assert.Contains("NoInventedSurface", p.Error);
            AssertCapMeasured(p);
        }

        [Fact]
        public void APinhole_BelowTheCapsShare_IsRefused()
        {
            SolidBooleanPipeline p = Pipeline(V.NoInventedSurface, 0.1);
            Assert.False(p.Compute());
            Assert.Equal(Status.VerificationFailed, p.Status);
            AssertCapMeasured(p);
        }

        [Fact]
        public void APinhole_AboveTheCapsShare_IsTheRightBooleanOfTheRepairedBody()
        {
            SolidBooleanPipeline p = Pipeline(V.NoInventedSurface, 0.2);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertHalfBox(p);
            AssertCapMeasured(p);
        }

        [Fact]
        public void APinhole_WithoutVerification_IsTheRightBoolean_AndNothingIsMeasured()
        {
            SolidBooleanPipeline p = Pipeline(V.None, 0);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertHalfBox(p);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.Equal(0, p.Report.InventedTriangles);
        }

        [Fact]
        public void TheClosedBox_AtTheDefaultLimit_IsNotFlagged()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 1, 1, 1), Box(0.5, -1, -1, 2, 2, 2) }, I) { Verification = V.NoInventedSurface };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertHalfBox(p);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.Equal(0, p.Report.InventedTriangles);
        }
    }
}
