using System;
using System.Collections.Generic;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public partial class SolidBooleanPipelineTests
    {
        static DMesh3 OpenSheetPlate(double x0, double x1, double y0, double y1, double z)
        {
            var m = new DMesh3();
            int v0 = m.AppendVertex(new Vector3d(x0, y0, z));
            int v1 = m.AppendVertex(new Vector3d(x1, y0, z));
            int v2 = m.AppendVertex(new Vector3d(x1, y1, z));
            int v3 = m.AppendVertex(new Vector3d(x0, y1, z));
            m.AppendTriangle(v0, v1, v2);
            m.AppendTriangle(v0, v2, v3);
            return m;
        }

        static DMesh3 OpenSheetLBracket(double lenX, double lenY, double lenZ)
        {
            var m = new DMesh3();
            int a0 = m.AppendVertex(new Vector3d(0, 0, 0));
            int a1 = m.AppendVertex(new Vector3d(lenX, 0, 0));
            int a2 = m.AppendVertex(new Vector3d(lenX, lenY, 0));
            int a3 = m.AppendVertex(new Vector3d(0, lenY, 0));
            int b1 = m.AppendVertex(new Vector3d(0, 0, lenZ));
            int b2 = m.AppendVertex(new Vector3d(0, lenY, lenZ));
            m.AppendTriangle(a0, a1, a2);
            m.AppendTriangle(a0, a2, a3);
            m.AppendTriangle(a0, a3, b2);
            m.AppendTriangle(a0, b2, b1);
            return m;
        }

        static DMesh3 OpenSheetAcuteFold(double halfAngleDeg)
        {
            double rad = halfAngleDeg * Math.PI / 180.0;
            double x = Math.Cos(rad);
            double y = Math.Sin(rad);
            var m = new DMesh3();
            int e0 = m.AppendVertex(new Vector3d(0, 0, 0));
            int e1 = m.AppendVertex(new Vector3d(0, 0, 2));
            int a0 = m.AppendVertex(new Vector3d(x, y, 0));
            int a1 = m.AppendVertex(new Vector3d(x, y, 2));
            int b0 = m.AppendVertex(new Vector3d(x, -y, 0));
            int b1 = m.AppendVertex(new Vector3d(x, -y, 2));
            m.AppendTriangle(e0, a0, a1);
            m.AppendTriangle(e0, a1, e1);
            m.AppendTriangle(e0, e1, b1);
            m.AppendTriangle(e0, b1, b0);
            return m;
        }

        [Fact]
        public void ThickenOpenSheet_Union_Intersection_Difference_And_ReportFields()
        {
            DMesh3 solid = new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, -1.0), new Vector3d(1.5, 1.5, 0.0)))
            }.Generate().MakeDMesh();
            DMesh3 sheet = OpenSheetLBracket(2.0, 1.0, 2.0);
            double d = 0.05;

            // Union: closed solid (2x2x1 = 4) + thickened bracket (d=0.05 => total vol 4.3245? Compute exact analytic):
            // Bracket outside solid: horizontal plate [0,2]x[0,1]x[-0.05,0.05] overlaps solid on [0,1.5]x[0,1]x[-0.05,0]
            // (vol 1.5*1*0.05 = 0.075), vertical plate [-0.05,0.05]x[0,1]x[0,2] overlaps solid nowhere (z >= 0).
            // Thickened bracket vol = 2*1*0.1 + 2*1*0.1 - 0.1*1*0.05? Wait: [0,2]x[0,1]x[-0.05,0.05] U [-0.05,0.05]x[0,1]x[-0.05,2.05]
            // = 0.2 + 0.21 - 0.005 = 0.405? Actually outer rim extends to x=-0.05, z=-0.05, z=2.0, x=2.0:
            // vol = 0.400000 (verified). Overlap with [-0.5,1.5]x[-0.5,1.5]x[-1,0] is [-0.05,1.5]x[0,1]x[-0.05,0] = 1.55 * 1 * 0.05 = 0.0775.
            // Union volume = 4.0 + 0.400 - 0.0775 = 4.3225; Intersection volume = 0.0775; Difference volume = 4.0 - 0.0775 = 3.9225.
            var uPipe = new SolidBooleanPipeline(solid, sheet, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = d
            };
            Assert.True(uPipe.Compute(), uPipe.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, uPipe.Status);
            Assert.True(uPipe.Report.Clean, uPipe.Report.NotCleanReason);
            Assert.True(uPipe.Report.InputClosed[0]);
            Assert.False(uPipe.Report.InputClosed[1]);
            Assert.Single(uPipe.Report.RepairedInputs);
            var rep = uPipe.Report.RepairedInputs[0];
            Assert.Equal(1, rep.Index);
            Assert.Equal("MeshMiteredOffset", rep.Method);
            Assert.Equal(sheet.TriangleCount, rep.TrianglesBefore);
            Assert.True(rep.TrianglesAfter > 0);
            Assert.True(rep.Milliseconds >= 0);
            Assert.Equal(4.3225, MeshMeasurements.VolumeArea(uPipe.Result, uPipe.Result.TriangleIndices(), uPipe.Result.GetVertex).x, 4);

            // Intersection
            var iPipe = new SolidBooleanPipeline(solid, sheet, I) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = d
            };
            Assert.True(iPipe.Compute(), iPipe.Error);
            Assert.True(iPipe.Report.Clean, iPipe.Report.NotCleanReason);
            Assert.Equal(0.0775, MeshMeasurements.VolumeArea(iPipe.Result, iPipe.Result.TriangleIndices(), iPipe.Result.GetVertex).x, 4);

            // Difference: must thicken the open sheet rather than auto-routing to the sheet-cut route
            var dPipe = new SolidBooleanPipeline(solid, sheet, D) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = d
            };
            Assert.True(dPipe.Compute(), dPipe.Error);
            Assert.True(dPipe.Report.Clean, dPipe.Report.NotCleanReason);
            Assert.Single(dPipe.Report.RepairedInputs);
            Assert.Equal("MeshMiteredOffset", dPipe.Report.RepairedInputs[0].Method);
            Assert.Equal(3.9225, MeshMeasurements.VolumeArea(dPipe.Result, dPipe.Result.TriangleIndices(), dPipe.Result.GetVertex).x, 4);
        }

        [Fact]
        public void ThickenOpenSheet_RelativeThickness_MiterLimit_And_StrayCoincidentTriangles()
        {
            DMesh3 solid = new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, -0.5), new Vector3d(1, 1, 0)))
            }.Generate().MakeDMesh();

            // 1. RelativeOpenSheetHalfThickness = 0.05 on a [0,1]x[0,1] plate (diag = sqrt(2)) => halfThick = 0.05 * sqrt(2)
            //    Add a stray coincident-vertex triangle far away at (1e10, 0, 0) AND test all three coincident pairs (a==b, b==c, c==a)
            DMesh3 plate = OpenSheetPlate(0, 1, 0, 1, 0);
            int p0 = plate.AppendVertex(new Vector3d(1e10, 0, 0));
            int p1 = plate.AppendVertex(new Vector3d(1e10, 0, 0));
            int p2 = plate.AppendVertex(new Vector3d(1e10, 1, 0));
            plate.AppendTriangle(p0, p1, p2); // va == vb
            plate.AppendTriangle(p2, p0, p1); // vb == vc
            plate.AppendTriangle(p0, p2, p1); // vc == va

            var relPipe = new SolidBooleanPipeline(solid, plate, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.0,
                RelativeOpenSheetHalfThickness = 0.05
            };
            Assert.True(relPipe.Compute(), relPipe.Error);
            Assert.True(relPipe.Report.Clean, relPipe.Report.NotCleanReason);
            double expectedHalf = 0.05 * Math.Sqrt(2.0);
            Assert.Equal(expectedHalf, relPipe.Result.CachedBounds.Max.z, 5);
            Assert.Equal(0.5 + expectedHalf, MeshMeasurements.VolumeArea(relPipe.Result, relPipe.Result.TriangleIndices(), relPipe.Result.GetVertex).x, 4);

            // 1b. 3D open sheet with unique z-dominant extent (2 x 1 x 3 => diag = sqrt(14)) exercises wx, wy, wz diagonal calculation
            DMesh3 tallBracket = OpenSheetLBracket(2.0, 1.0, 3.0);
            var rel3dPipe = new SolidBooleanPipeline(solid, tallBracket, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.0,
                RelativeOpenSheetHalfThickness = 0.02
            };
            Assert.True(rel3dPipe.Compute(), rel3dPipe.Error);
            Assert.True(rel3dPipe.Report.Clean, rel3dPipe.Report.NotCleanReason);
            double expectedHalf3d = 0.02 * Math.Sqrt(14.0);
            Assert.Equal(expectedHalf3d, -rel3dPipe.Result.CachedBounds.Min.x, 4);

            // 2. OpenSheetMiterLimit is forwarded to MeshMiteredOffset (smaller MiterLimit clamps acute fold crest further)
            DMesh3 fold = OpenSheetAcuteFold(10.0); // 20-deg fold => unclipped miter ~ 5.76 * d
            DMesh3 anchor = new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0.4, -0.5, 0.2), new Vector3d(1.2, 0.5, 1.8)))
            }.Generate().MakeDMesh();
            var clamp2 = new SolidBooleanPipeline(anchor, fold, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.05,
                OpenSheetMiterLimit = 2.0
            };
            var clamp4 = new SolidBooleanPipeline(anchor, fold, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.05,
                OpenSheetMiterLimit = 4.0
            };
            Assert.True(clamp2.Compute(), clamp2.Error);
            Assert.True(clamp4.Compute(), clamp4.Error);
            Assert.Equal(-2.0 * 0.05, clamp2.Result.CachedBounds.Min.x, 4);
            Assert.Equal(-4.0 * 0.05, clamp4.Result.CachedBounds.Min.x, 4);
        }

        [Fact]
        public void ThickenOpenSheet_Validation_NonFinite_Degenerate_And_RefusalPaths()
        {
            DMesh3 solid = new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One))
            }.Generate().MakeDMesh();
            DMesh3 plate = OpenSheetPlate(0, 1, 0, 1, 0.5);

            // 1. Out-of-range InputRepair enum (< Refuse and > ThickenOpenSheet)
            foreach (int badVal in new[] { -1, 99 }) {
                var badEnum = new SolidBooleanPipeline(solid, plate, U) {
                    InputRepair = (SolidBooleanPipeline.InputRepairModes)badVal
                };
                Assert.False(badEnum.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, badEnum.Status);
            }

            // 2. Negative / NaN / Infinity OpenSheetHalfThickness
            foreach (double bad in new[] { -0.1, double.NaN, double.PositiveInfinity }) {
                var p = new SolidBooleanPipeline(solid, plate, U) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                    OpenSheetHalfThickness = bad
                };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
                Assert.Contains("OpenSheetHalfThickness", p.Error);
            }

            // 3. Non-positive / NaN / Infinity RelativeOpenSheetHalfThickness when OpenSheetHalfThickness == 0
            foreach (double bad in new[] { 0.0, -0.01, double.NaN, double.PositiveInfinity }) {
                var p = new SolidBooleanPipeline(solid, plate, U) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                    OpenSheetHalfThickness = 0.0,
                    RelativeOpenSheetHalfThickness = bad
                };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
                Assert.Contains("RelativeOpenSheetHalfThickness", p.Error);
            }

            // 3b. Explicit OpenSheetHalfThickness > 0 ignores an invalid RelativeOpenSheetHalfThickness
            var explicitOverridesRel = new SolidBooleanPipeline(solid, plate, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.05,
                RelativeOpenSheetHalfThickness = -1.0
            };
            Assert.True(explicitOverridesRel.Compute(), explicitOverridesRel.Error);

            // 4. Out-of-range OpenSheetMiterLimit (< 1, > 1e4, NaN, Inf) — both boundaries 1.0 and 1e4 are accepted
            foreach (double bad in new[] { 0.99, 1e4 + 1.0, double.NaN, double.PositiveInfinity }) {
                var p = new SolidBooleanPipeline(solid, plate, U) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                    OpenSheetHalfThickness = 0.05,
                    OpenSheetMiterLimit = bad
                };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
                Assert.Contains("OpenSheetMiterLimit", p.Error);
            }
            foreach (double okLimit in new[] { 1.0, 1e4 }) {
                var boundaryMiter = new SolidBooleanPipeline(solid, plate, U) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                    OpenSheetHalfThickness = 0.05,
                    OpenSheetMiterLimit = okLimit
                };
                Assert.True(boundaryMiter.Compute(), $"MiterLimit={okLimit}: {boundaryMiter.Error}");
            }

            // 5. Open sheet with non-finite vertex coordinate fails with InvalidInput upfront
            DMesh3 nanSheet = OpenSheetPlate(0, 1, 0, 1, 0.5);
            nanSheet.SetVertex(0, new Vector3d(double.NaN, 0, 0.5));
            var nanPipe = new SolidBooleanPipeline(solid, nanSheet, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.0,
                RelativeOpenSheetHalfThickness = 0.05
            };
            Assert.False(nanPipe.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, nanPipe.Status);

            // 6. Genuinely open collinear sliver fails MeshMiteredOffset with InvalidInput -> OpenInput naming MeshMiteredOffset (not MeshRobustRepair)
            var sliver = new DMesh3();
            int da = sliver.AppendVertex(new Vector3d(0, 0, 0));
            int db = sliver.AppendVertex(new Vector3d(0.5, 0, 0));
            int dc = sliver.AppendVertex(new Vector3d(1, 0, 0));
            sliver.AppendTriangle(da, db, dc);
            var sliverPipe = new SolidBooleanPipeline(solid, sliver, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.0,
                RelativeOpenSheetHalfThickness = 0.05
            };
            Assert.False(sliverPipe.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, sliverPipe.Status);
            Assert.Contains("MeshMiteredOffset", sliverPipe.Error);
            Assert.DoesNotContain("MeshRobustRepair", sliverPipe.Error);

            // 6b. Open sheet with sub-tolerance explicit half-thickness (1e-20) also refuses via MeshMiteredOffset
            var tinyPipe = new SolidBooleanPipeline(solid, plate, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 1e-20
            };
            Assert.False(tinyPipe.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, tinyPipe.Status);
            Assert.Contains("MeshMiteredOffset", tinyPipe.Error);
            Assert.DoesNotContain("MeshRobustRepair", tinyPipe.Error);

            // 7. Closed inputs bypass ThickenOpenSheet (even with valid settings, zero repairs run)
            DMesh3 solid2 = new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)))
            }.Generate().MakeDMesh();
            var closedPipe = new SolidBooleanPipeline(solid, solid2, U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1
            };
            Assert.True(closedPipe.Compute(), closedPipe.Error);
            Assert.Empty(closedPipe.Report.RepairedInputs);
            Assert.Equal(1.5, MeshMeasurements.VolumeArea(closedPipe.Result, closedPipe.Result.TriangleIndices(), closedPipe.Result.GetVertex).x, 6);
        }
    }
}
