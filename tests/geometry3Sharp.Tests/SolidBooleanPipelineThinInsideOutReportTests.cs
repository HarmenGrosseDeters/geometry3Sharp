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
    /// #1555: the report entry of a thin inside-out component that SolidBooleanPipeline dropped before the boolean
    /// (Report.DroppedInsideOutComponents), and the inputs it is not made for (fixtures of SolidBooleanPipelineThinInsideOutTests).
    /// </summary>
    public class SolidBooleanPipelineThinInsideOutReportTests
    {
        const ExactMeshBoolean.Operations I = ExactMeshBoolean.Operations.Intersection;

        [Theory]
        [InlineData(T.Outside)]
        [InlineData(T.Across)]
        public void TheDroppedLens_IsNamedInTheReport(double x0)
        {
            List<DMesh3> inputs = T.Inputs(x0, T.ThinLift);
            int before = inputs[0].TriangleCount;
            var p = new SolidBooleanPipeline(inputs, I);
            Assert.True(p.Compute(), p.Error);
            SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(0, d.Input);
            Assert.Equal(6, d.Triangles);
            Assert.True(Math.Abs(d.Volume + 0.25 * T.ThinLift / 3) <= 1e-9 * 0.25 * T.ThinLift / 3, d.Volume.ToString("R"));
            Assert.True(d.Area > 0.5 && d.Area < 0.5 + 1e-9);
            Assert.Equal(2 * -d.Volume / d.Area, d.Thickness);
            // the bound (#1567): 1e-5 of the box diagonal of the lens itself, not of the larger components around it
            Assert.Equal(new Vector3d(0.5, 0.5, T.ThinLift).Length, d.Diagonal);
            Assert.Equal(1e-5 * d.Diagonal, d.Bound);
            Assert.True(d.Thickness < d.Bound);
            Assert.Equal(new Vector3d(x0 + 0.25, 0.5, 0.5 + T.ThinLift / 2), d.Center);
            // (the input as given is not changed: the lens is dropped from a copy)
            Assert.Equal(before, inputs[0].TriangleCount);
            Assert.Equal(before, p.Report.InputTriangles[0]);
        }

        [Fact]
        public void NothingIsDropped_WhereTheLensIsNotThin_OrACavity_OrAllowed()
        {
            var notThin = new SolidBooleanPipeline(T.Inputs(T.Outside, T.NotThinLift), I);
            Assert.False(notThin.Compute());
            Assert.Empty(notThin.Report.DroppedInsideOutComponents);
            var cavity = new SolidBooleanPipeline(new List<DMesh3> { T.BoxWithLens(0.375, T.ThinLift), T.Other() }, I);
            Assert.True(cavity.Compute(), cavity.Error);
            Assert.Empty(cavity.Report.DroppedInsideOutComponents);
            var allowed = new SolidBooleanPipeline(T.Inputs(T.Outside, T.ThinLift), I) { AllowInsideOutInputs = true };
            Assert.True(allowed.Compute(), allowed.Error);
            Assert.Empty(allowed.Report.DroppedInsideOutComponents);
            var nesting = new SolidBooleanPipeline(T.Inputs(T.Outside, T.ThinLift), I) { Orientation = ExactMeshBoolean.OrientationModes.ByNesting };
            Assert.True(nesting.Compute(), nesting.Error);
            Assert.Empty(nesting.Report.DroppedInsideOutComponents);
            var lens = new DMesh3();
            T.AddLens(lens, 0.75, 0.5, T.ThinLift);
            var only = new SolidBooleanPipeline(new List<DMesh3> { lens, T.Other() }, I);
            Assert.False(only.Compute());
            Assert.Empty(only.Report.DroppedInsideOutComponents);
        }

        [Fact]
        public void APositiveThinSlab_IsNotDropped()
        {
            // the same lens with the apex below the quad: a thin solid of positive volume, not inside-out, kept
            DMesh3 a = T.Box(0, 0, 0, 1, 1, 1);
            T.AddLens(a, T.Outside, 0.5, -T.ThinLift);
            var p = new SolidBooleanPipeline(new List<DMesh3> { a, T.Other() }, I);
            Assert.True(p.Compute(), p.Error);
            Assert.Empty(p.Report.DroppedInsideOutComponents);
            Assert.True(Invariance.Volume(p.Result) > 0.5);
        }

        [Fact]
        public void TheReport_IsTheSame_UnderAScaleByAPowerOfTwo()
        {
            var plain = new SolidBooleanPipeline(T.Inputs(T.Across, T.ThinLift), I);
            Assert.True(plain.Compute(), plain.Error);
            SolidBooleanDroppedComponent d0 = Assert.Single(plain.Report.DroppedInsideOutComponents);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var p = new SolidBooleanPipeline(T.Inputs(T.Across, T.ThinLift).Select(m => Invariance.Transformed(m, s)).ToList(), I);
                Assert.True(p.Compute(), s.Name + ": " + p.Error);
                SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
                double k = s.Scale;
                Assert.Equal(d0.Volume * k * k * k, d.Volume);
                Assert.Equal(d0.Area * k * k, d.Area);
                Assert.Equal(d0.Thickness * k, d.Thickness);
                Assert.Equal(d0.Bound * k, d.Bound);
                Assert.Equal(d0.Diagonal * k, d.Diagonal);
                Assert.Equal(d0.Center * k, d.Center);
            }
        }

        [Fact]
        public void TheBound_IsThatOfTheComponentItself_InEveryOrderOfTheInputs()
        {
            // the lens's input last: its last component is the lens, the bound still that of the lens itself
            var p = new SolidBooleanPipeline(new List<DMesh3> { T.Other(), T.BoxWithLens(T.Across, T.ThinLift) }, I);
            Assert.True(p.Compute(), p.Error);
            SolidBooleanDroppedComponent d = Assert.Single(p.Report.DroppedInsideOutComponents);
            Assert.Equal(1, d.Input);
            Assert.Equal(1e-5 * new Vector3d(0.5, 0.5, T.ThinLift).Length, d.Bound);
            Assert.Equal(0.5, Invariance.Volume(p.Result), 14);
        }

        [Fact]
        public void APartOfTheLensThatIsOpenByItsEdges_IsNotDropped()
        {
            // the box and the lens, with two of the fan's triangles on their own copies of the vertices: the input is closed by
            // position (winding-closed), the lens is two components that are open by their edges. Removing one would open the
            // input: nothing is dropped
            DMesh3 m = T.Box(0, 0, 0, 1, 1, 1);
            double x0 = T.Outside, x1 = x0 + 0.5, y0 = 0.25, y1 = 0.75, z = 0.5;
            Vector3d pa = new Vector3d(x0, y0, z), pb = new Vector3d(x1, y0, z), pc = new Vector3d(x1, y1, z), pd = new Vector3d(x0, y1, z);
            Vector3d pe = new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, z + T.ThinLift);
            int a = m.AppendVertex(pa), b = m.AppendVertex(pb), c = m.AppendVertex(pc), d = m.AppendVertex(pd), e = m.AppendVertex(pe);
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            m.AppendTriangle(e, b, a); m.AppendTriangle(e, c, b);
            int c2 = m.AppendVertex(pc), d2 = m.AppendVertex(pd), a2 = m.AppendVertex(pa), e2 = m.AppendVertex(pe);
            m.AppendTriangle(e2, d2, c2); m.AppendTriangle(e2, a2, d2);
            Assert.True(ExactMeshBoolean.IsWindingClosed(m));
            Assert.False(m.IsClosed());
            var p = new SolidBooleanPipeline(new List<DMesh3> { m, T.Other() }, I);
            // (as before the change: the boolean decides; here the lens's open parts make an inside-out cell)
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Empty(p.Report.DroppedInsideOutComponents);
        }

        [Theory]
        [InlineData(1.0 / (1 << 20))]
        [InlineData(1.0 / (1L << 36))]
        public void AThinCavityFlushUnderTheTopFace_IsNotDropped_UnderEverySimilarity(double lift)
        {
            // the lens's quad lies in the box's top face z = 1, from inside, the apex at 1 - lift; flipped, so its volume is
            // negative: a thin cavity that touches the wall. Its quad vertices lie on the rest (the top face), and after a
            // rotation far from the origin within the touch distance of it: they are skipped (ShellIndex touch), the apex decides
            // (inside). Without the skip a rounded quad vertex can fall outside the face and drop the cavity
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = T.Box(0, 0, 0, 1, 1, 1);
                T.AddLens(a, 0.375, 1, -lift, true);
                Assert.True(Invariance.Volume(a) < 1);
                var inputs = new List<DMesh3> { a, T.Other() }.Select(m => Invariance.Transformed(m, s)).ToList();
                var p = new SolidBooleanPipeline(inputs, I);
                bool ok = p.Compute();
                Assert.True(p.Report.DroppedInsideOutComponents.Count == 0, s.Name);
                // (the boolean decides as before; where it succeeds, the result is a closed solid that keeps the cavity)
                if (ok) {
                    Assert.True(p.Result.IsClosed(), s.Name);
                    Assert.True(p.Result.CheckValidity(false, FailMode.ReturnOnly), s.Name);
                    double v = Invariance.Volume(Invariance.MappedBack(p.Result, s));
                    // (the cavity is some 1e-7 of the volume: under a rotation it is below the rounding of the result, so only bounds)
                    Assert.True(v < 0.5 + 1e-7 && v > 0.5 - 1e-6, s.Name + ": " + v);
                    if (s.IsExact)
                        Assert.True(v < 0.5, s.Name + ": " + v);
                } else
                    Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            }
        }

        [Fact]
        public void ASheetInput_IsNotExamined_ForTheBound()
        {
            // a Difference with a stated sheet of 100 x 100 (diagonal 141) that cuts the box at z = 0.25: were the sheet counted,
            // the bound would be 1.4e-3 and the lens of NotThinLift (thickness 8.1e-5) would be dropped; it is not thin by its
            // own size (bound 7.1e-6) and is refused as before
            DMesh3 a = T.BoxWithLens(T.Outside, T.NotThinLift);
            var sheet = new DMesh3();
            int v0 = sheet.AppendVertex(new Vector3d(-50, -50, 0.25)), v1 = sheet.AppendVertex(new Vector3d(50, -50, 0.25));
            int v2 = sheet.AppendVertex(new Vector3d(50, 50, 0.25)), v3 = sheet.AppendVertex(new Vector3d(-50, 50, 0.25));
            sheet.AppendTriangle(v0, v1, v2); sheet.AppendTriangle(v0, v2, v3);
            var p = new SolidBooleanPipeline(new List<DMesh3> { a, sheet }, ExactMeshBoolean.Operations.Difference) {
                InputRoles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(p.Compute());
            Assert.Empty(p.Report.DroppedInsideOutComponents);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            // the same with the thin lens: dropped, and the bound is that of the lens
            DMesh3 b = T.BoxWithLens(T.Outside, T.ThinLift);
            var q = new SolidBooleanPipeline(new List<DMesh3> { b, sheet }, ExactMeshBoolean.Operations.Difference) {
                InputRoles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.True(q.Compute(), q.Error);
            SolidBooleanDroppedComponent d = Assert.Single(q.Report.DroppedInsideOutComponents);
            Assert.Equal(new Vector3d(0.5, 0.5, T.ThinLift).Length, d.Diagonal);
            Assert.Equal(1e-5 * d.Diagonal, d.Bound);
            Assert.True(q.Result.IsClosed());
        }
    }
}
