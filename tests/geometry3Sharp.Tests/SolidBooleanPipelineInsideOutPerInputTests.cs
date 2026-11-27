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
    /// #1567: whether a closed inside-out component is dropped as a rounding residue (#1555) depends on the component itself (thin
    /// by 1e-5 of its own box diagonal: two nearly coincident sheets), not on the size of the other components or inputs. A
    /// real 10 x 10 x 0.01 plate is refused (InsideOutInput) next to a large input as next to a small one.
    /// </summary>
    public class SolidBooleanPipelineInsideOutPerInputTests
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

        // a unit box at (x, 0, 0) and an inside-out plate 10 x 10 x thickness beside it (not touching), 7 units off in y
        static DMesh3 BoxAndPlate(double x, double thickness)
        {
            DMesh3 b = Box(new Vector3d(x, 0, 0), new Vector3d(0.5, 0.5, 0.5));
            MeshEditor.Append(b, Box(new Vector3d(x, 7, 0), new Vector3d(5, 5, thickness / 2), true));
            return b;
        }

        // a unit box with an inside-out lens (the fixture of #1555, thickness 3e-7) beside it, at x
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

        static void AssertRefused(SolidBooleanPipeline p, string what)
        {
            Assert.True(p.Status == Refused, what + ": " + p.Status + " " + p.Error);
            Assert.Empty(p.Report.DroppedInsideOutComponents);
            Assert.Null(p.Result);
        }

        static DMesh3 Unit() { return Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)); }

        [Theory]
        [InlineData(500)]
        [InlineData(5)]
        public void ARealPlate_IsRefused_NextToALargeOrASmallInput(double half)
        {
            // the repro of the issue: on master the plate is dropped (Success, Clean) next to the large input
            AssertRefused(Run(Box(Vector3d.Zero, new Vector3d(half, half, half)), BoxAndPlate(2000, 0.01)), "A of half-size " + half);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(5)]
        public void ARealPlate_IsRefused_NextToALargeComponentOfItsOwnInput(double half)
        {
            // the plate (1e-3 of its own diagonal) next to a large box of the same input: a bound of that input would take it
            DMesh3 b = Box(new Vector3d(5000, 0, 0), new Vector3d(half, half, half));
            MeshEditor.Append(b, Box(new Vector3d(5000, 3 * half, 0), new Vector3d(5, 5, 0.005), true));
            AssertRefused(Run(Unit(), b), "B with a box of half-size " + half);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(5)]
        [InlineData(0.5)]
        public void AResidue_IsDropped_NextToALargeOrASmallInput(double half)
        {
            var p = Run(Box(Vector3d.Zero, new Vector3d(half, half, half)), BoxAndLens(2000));
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(1, d.Input);
            Assert.Equal(1e-5 * d.Diagonal, d.Bound);
            Assert.Equal(8 * half * half * half + 1, Invariance.Volume(p.Result), 6);
            Assert.True(p.Result.IsClosed());
        }

        [Fact]
        public void AResidue_NextToALargeComponentOfItsOwnInput_IsDropped()
        {
            // the bound is that of the residue's own size: a large box in the same input does not matter
            DMesh3 b = Box(new Vector3d(5000, 0, 0), new Vector3d(500, 500, 500));
            var lens = new DMesh3();
            T.AddLens(lens, 5600, 0.5, T.ThinLift);
            MeshEditor.Append(b, lens);
            var p = Run(Unit(), b);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(1e9 + 1, Invariance.Volume(p.Result), 3);
        }

        [Fact]
        public void TheThreshold_IsOneE5OfTheOwnDiagonal()
        {
            // the plate 10 x 10 x t: thickness 2 V / A = t / (1 + 0.2 t), diagonal sqrt(200 + t^2): the bound is 1.414e-4
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                foreach (double t in new[] { 1.2e-4, 1.4e-4 }) {
                    var p = Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndPlate(3, t), s));
                    Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, s.Name + " t " + t + ": " + p.Status + " " + p.Error);
                    SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
                    Assert.True(d.Thickness < d.Bound && d.Thickness > 0.8 * d.Bound, s.Name + ": " + d.Thickness + " " + d.Bound);
                }
                foreach (double t in new[] { 1.45e-4, 1.7e-4 })
                    AssertRefused(Run(Invariance.Transformed(Unit(), s), Invariance.Transformed(BoxAndPlate(3, t), s)), s.Name + " t " + t);
            }
        }

        [Fact]
        public void TheDecision_IsTheSame_UnderEverySimilarity()
        {
            var plain = Run(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), BoxAndLens(2000));
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, plain.Status);
            foreach (Similarity s in Similarity.All()) {
                AssertRefused(Run(Invariance.Transformed(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), s), Invariance.Transformed(BoxAndPlate(2000, 0.01), s)), s.Name + " plate");
                AssertRefused(Run(Invariance.Transformed(Box(Vector3d.Zero, new Vector3d(5, 5, 5)), s), Invariance.Transformed(BoxAndPlate(2000, 0.01), s)), s.Name + " plate, small A");
                var p = Run(Invariance.Transformed(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), s), Invariance.Transformed(BoxAndLens(2000), s));
                Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, s.Name + ": " + p.Status + " " + p.Error);
                SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
                if (s.IsExact) {
                    SolidBooleanDroppedComponent d0 = plain.Report.DroppedInsideOutComponents[0];
                    double k = s.Scale;
                    Assert.Equal(d0.Bound * k, d.Bound);
                    Assert.Equal(d0.Diagonal * k, d.Diagonal);
                    Assert.Equal(d0.Thickness * k, d.Thickness);
                    Assert.Equal("", Invariance.GeometricDifference(Invariance.MappedBack(p.Result, s), plain.Result));
                }
            }
        }

        [Fact]
        public void AThinLens_InTheVoidOfAThinCavity_IsDropped_TheCavityStays()
        {
            // a box of 1000 (diagonal 1732) with a cavity 10 x 10 x 0.01 and, in the void of the cavity, an inside-out lens (thickness
            // 3e-7). By the old bound (1.7e-2) both were candidates; the cavity (every vertex inside the box) stayed, and the lens, tested
            // against the box alone, was inside it and stayed too. Now the cavity (thickness 1e-3 of its diagonal) is no candidate and
            // belongs to the rest: the lens has winding number 0 against it and is dropped
            DMesh3 b = Box(new Vector3d(5000, 0, 0), new Vector3d(500, 500, 500));
            MeshEditor.Append(b, Box(new Vector3d(5000, 0, 0), new Vector3d(5, 5, 0.005), true));
            var lens = new DMesh3();
            T.AddLens(lens, 4999.75, 0, T.ThinLift);
            MeshTransforms.Translate(lens, new Vector3d(0, -0.5, 0));
            MeshEditor.Append(b, lens);
            var p = Run(Unit(), b);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(1, d.Input);
            Assert.Equal(6, d.Triangles);
            Assert.True(p.Result.IsClosed());
            Assert.True(Invariance.AllFinite(p.Result));
            // the solid of the box minus the cavity (1), plus the unit box
            Assert.Equal(1e9 - 1 + 1, Invariance.Volume(p.Result), 3);
        }

        [Fact]
        public void Compute_Twice_GivesTheSameDecision()
        {
            var refused = new SolidBooleanPipeline(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), BoxAndPlate(2000, 0.01), U);
            for (int i = 0; i < 2; ++i) {
                Assert.False(refused.Compute());
                AssertRefused(refused, "run " + i);
            }
            var dropped = new SolidBooleanPipeline(Box(Vector3d.Zero, new Vector3d(500, 500, 500)), BoxAndLens(2000), U);
            Assert.True(dropped.Compute(), dropped.Error);
            double v = Invariance.Volume(dropped.Result);
            Assert.True(dropped.Compute(), dropped.Error);
            Assert.Equal(v, Invariance.Volume(dropped.Result));
            Assert.Single(dropped.Report.DroppedInsideOutComponents);
        }
    }
}
