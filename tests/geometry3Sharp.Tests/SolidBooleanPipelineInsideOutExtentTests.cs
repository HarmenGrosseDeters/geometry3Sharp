using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using T = geometry3Sharp.Tests.SolidBooleanPipelineThinInsideOutTests;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1567: the size that the bound of a dropped inside-out component is relative to is its extent (SolidBooleanDroppedComponent.Extent):
    /// the largest distance from its first vertex to its other vertices, the same in every orientation up to rounding (the box
    /// diagonal of the first version varied by up to sqrt(3) with the orientation of a flat plate)
    /// </summary>
    public class SolidBooleanPipelineInsideOutExtentTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union;
        const SolidBooleanPipeline.PipelineStatus Refused = SolidBooleanPipeline.PipelineStatus.InsideOutInput;

        static DMesh3 Box(Vector3d c, Vector3d e, bool flip = false)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(c, e), NoSharedVertices = false };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            if (flip)
                m.ReverseOrientation();
            return m;
        }

        static DMesh3 Unit() { return Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)); }

        // an inside-out plate 10 x 10 x thickness beside a unit box (not touching), 7 units off in y
        static DMesh3 BoxAndPlate(double x, double thickness)
        {
            DMesh3 b = Box(new Vector3d(x, 0, 0), new Vector3d(0.5, 0.5, 0.5));
            MeshEditor.Append(b, Box(new Vector3d(x, 7, 0), new Vector3d(5, 5, thickness / 2), true));
            return b;
        }

        static DMesh3 BoxAndLens(double x)
        {
            DMesh3 b = Box(new Vector3d(x + 0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            T.AddLens(b, x + 2, 0.5, T.ThinLift);
            return b;
        }

        static SolidBooleanPipeline Run(DMesh3 a, DMesh3 b)
        {
            var p = new SolidBooleanPipeline(a, b, U);
            p.Compute();
            return p;
        }

        static string Decision(SolidBooleanPipeline p)
        {
            return p.Status + " " + p.Report.DroppedInsideOutComponents.Count;
        }

        static Similarity AboutZ(double degrees)
        {
            return new Similarity("z " + degrees, 1, Matrix3d.AxisAngleD(Vector3d.AxisZ, degrees), Vector3d.Zero);
        }

        [Fact]
        public void TheExtent_OfALens_IsTheLongestDistanceFromItsFirstVertex_AndTheBoundIsRelativeToIt()
        {
            // the lens of the fixture: 0.5 x 0.5 across the base, the first vertex a corner of the base, the apex lifts by 3e-7 only
            var p = Run(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), BoxAndLens(2000));
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(Math.Sqrt(0.5), d.Extent, 12);
            Assert.Equal(1e-5 * d.Extent, d.Bound);
        }

        [Fact]
        public void TheExtent_ScalesExactly_UnderThePowersOfTwo()
        {
            var plain = Run(Unit(), BoxAndLens(3));
            SolidBooleanDroppedComponent d0 = Assert.Single(plain.Report.DroppedInsideOutComponents);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var p = Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndLens(3), s));
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
                SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
                Assert.Equal(d0.Extent * s.Scale, d.Extent);
                Assert.Equal(d0.Bound * s.Scale, d.Bound);
            }
        }

        [Fact]
        public void TheExtent_OfAPlate_IsTheSameInEveryFrame_UpToRounding()
        {
            double e0 = 0;
            foreach (Similarity s in Similarity.All()) {
                var p = Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndPlate(3, 1.2e-4), s));
                Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, s.Name + ": " + p.Status + " " + p.Error);
                SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
                double e = d.Extent / s.Scale;
                if (e0 == 0)
                    e0 = e;
                Assert.True(Math.Abs(e - e0) <= 1e-6 * e0, s.Name + ": " + e + " " + e0);
                Assert.Equal(1e-5 * d.Extent, d.Bound);
                Assert.True(p.Report.Clean, s.Name + ": " + p.Report.NotCleanReason);
                Assert.True(p.Result.IsClosed());
                Assert.True(Invariance.AllFinite(p.Result));
            }
        }

        [Fact]
        public void TheThreshold_IsTheSame_InEveryFrame_RotationsIncluded()
        {
            // the plate 10 x 10 x t beside a unit box: thickness t / (1 + 0.2 t), extent sqrt(200 + t^2), so the bound is 1.414e-4: well
            // below it the plate is dropped, well above it refused, in every frame
            foreach (Similarity s in Similarity.All()) {
                foreach (double t in new[] { 1.2e-4, 1.0e-4 }) {
                    var p = Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndPlate(3, t), s));
                    Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, s.Name + " t " + t + ": " + p.Status + " " + p.Error);
                    Assert.Single(p.Report.DroppedInsideOutComponents);
                }
                foreach (double t in new[] { 1.7e-4, 1.0e-3 }) {
                    var p = Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndPlate(3, t), s));
                    Assert.True(p.Status == Refused, s.Name + " t " + t + ": " + p.Status + " " + p.Error);
                    Assert.Empty(p.Report.DroppedInsideOutComponents);
                }
            }
        }

        [Fact]
        public void AtTheThreshold_TheExactFrames_DecideBitForBit_AsTheOriginal()
        {
            // thickness 1.41417e-4 against a bound of 1.41421e-4: within 3e-5 of it, and the same in every scaling by a power of two
            const double t = 1.4142e-4;
            var plain = Run(Unit(), BoxAndPlate(3, t));
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, plain.Status);
            SolidBooleanDroppedComponent d0 = Assert.Single(plain.Report.DroppedInsideOutComponents);
            Assert.True(d0.Thickness < d0.Bound && d0.Thickness > 0.9999 * d0.Bound, d0.Thickness + " " + d0.Bound);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var p = Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndPlate(3, t), s));
                Assert.Equal(Decision(plain), Decision(p));
                SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
                Assert.Equal(d0.Bound * s.Scale, d.Bound);
                Assert.Equal(d0.Thickness * s.Scale, d.Thickness);
            }
        }

        [Fact]
        public void ARotatedPlate_NextToAUnitBox_IsDecidedAsTheUnrotatedOne()
        {
            // the probe of the review: 10 x 10 x 1.6e-4 (thickness 1.6e-4 against a bound of 1.414e-4) was refused unrotated and dropped
            // rotated by 45 degrees about z when the bound was relative to the diagonal of the axis-aligned box of the plate
            Similarity r = AboutZ(45);
            var plain = Run(Unit(), BoxAndPlate(3, 1.6e-4));
            var rotated = Run(Invariance.Transformed(Unit(), r), Invariance.Transformed(BoxAndPlate(3, 1.6e-4), r));
            Assert.Equal(Refused, plain.Status);
            Assert.Equal(Decision(plain), Decision(rotated));
            Assert.Null(rotated.Result);
        }

        [Fact]
        public void TheExtent_DoesNotDependOnTheOtherInput_OrAnotherComponent()
        {
            // a plate of 1.2e-4 is dropped next to a unit box and next to a huge box of its own input as well
            DMesh3 b = Box(new Vector3d(5000, 0, 0), new Vector3d(500, 500, 500));
            MeshEditor.Append(b, Box(new Vector3d(5000, 3000, 0), new Vector3d(5, 5, 6e-5), true));
            var p = Run(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), b);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(1e-5 * d.Extent, d.Bound);
            Assert.True(d.Extent < 15);
        }
    }
}
