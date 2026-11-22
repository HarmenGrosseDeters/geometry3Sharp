using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline checks every MeshAutoRepair copy of an open input exactly for self-intersections (its own
    /// ExactMeshArrangement) and fails with OpenInput where the copy self-intersects and the input as given does not (owner
    /// 2026-11-22: check + refuse). The case: an open, non-planar sheet as a Difference tool, whose boundary fill the sheet
    /// crosses (InsideOutAdviceTests.Diagnosis_...): before, it was InsideOutInput or, depending on which lens decided, a wrong
    /// Success. Clean closures (open box, tube, bowl, deep U) are unchanged.
    /// </summary>
    public class RepairSelfIntersectionTests
    {
        static readonly ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.OrientationModes Nesting = ExactMeshBoolean.OrientationModes.ByNesting;
        const string Refusal = "MeshAutoRepair closed it into a self-intersecting mesh";

        internal static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        internal static List<DMesh3> Parts(DMesh3 tool) { return new List<DMesh3> { Box(-3, -3, -3, 3, 3, 3), tool }; }

        static DMesh3 Grid(int n, Func<double, double, Vector3d> f)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(f(i / (double)n, j / (double)n));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i;
                    m.AppendTriangle(a, a + 1, a + n + 2);
                    m.AppendTriangle(a, a + n + 2, a + n + 1);
                }
            return m;
        }

        // the unit cube (a 4 x 4 grid per face) without the faces whose normal is picked
        static DMesh3 OpenBox(Func<Vector3d, bool> remove)
        {
            DMesh3 b = new GridBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)), EdgeVertices = 4 }.Generate().MakeDMesh();
            foreach (int t in b.TriangleIndices().ToList())
                if (remove(b.GetTriNormal(t)))
                    b.RemoveTriangle(t);
            new MergeCoincidentEdges(b).Apply();
            return new DMesh3(b, true);
        }

        // a tube of radius 1 (n around, h rings), its top rim at height top(angle): open at both ends
        static DMesh3 Tube(int n, int h, Func<double, double> top)
        {
            var m = new DMesh3();
            for (int j = 0; j <= h; ++j)
                for (int i = 0; i < n; ++i) {
                    double th = 2 * Math.PI * i / n;
                    m.AppendVertex(new Vector3d(Math.Cos(th), Math.Sin(th), j / (double)h * top(th)));
                }
            for (int j = 0; j < h; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * n + i, b = j * n + (i + 1) % n;
                    m.AppendTriangle(a, b, b + n);
                    m.AppendTriangle(a, b + n, a + n);
                }
            return m;
        }

        public static IEnumerable<object[]> CleanClosures()
        {
            yield return new object[] { "open box, no top" };
            yield return new object[] { "open box, no top and bottom" };
            yield return new object[] { "tube, planar rims" };
            yield return new object[] { "tube, wavy rim" };
            yield return new object[] { "deep U" };
        }

        internal static DMesh3 Clean(string name)
        {
            switch (name) {
                case "open box, no top": return OpenBox(nv => nv.z > 0.5);
                case "open box, no top and bottom": return OpenBox(nv => Math.Abs(nv.z) > 0.5);
                case "tube, planar rims": return Tube(24, 4, th => 1);
                case "tube, wavy rim": return Tube(24, 4, th => 1 + 0.4 * Math.Sin(2 * th));
                case "deep U": return Grid(16, (u, v) => new Vector3d(0.3 * Math.Cos(Math.PI * u), v, -2 * Math.Sin(Math.PI * u)));
            }
            throw new ArgumentException(name);
        }

        static SolidBooleanPipeline Run(List<DMesh3> parts, SolidBooleanPipeline.InputRepairModes repair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust,
            ExactMeshBoolean.OrientationModes orientation = Nesting)
        {
            var p = new SolidBooleanPipeline(parts, D) { Orientation = orientation, InputRepair = repair };
            p.Compute();
            return p;
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.02)]
        [InlineData(-0.05)]
        [InlineData(0.02)]
        [InlineData(0.05)]
        [InlineData(0.1)]
        public void AWavySheetTool_WhoseRepairedCopySelfIntersects_IsRefusedAsOpen(double c)
        {
            // (c <= 0: was InsideOutInput; c > 0: was Success with a wrong solid, the lenses of sheet and fill)
            foreach (var mode in new[] { SolidBooleanPipeline.InputRepairModes.AutoRepair, SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust })
                foreach (var orientation in new[] { Nesting, ExactMeshBoolean.OrientationModes.Refuse }) {
                    SolidBooleanPipeline p = Run(Parts(InsideOutAdviceTests.WavySheet(8, c)), mode, orientation);
                    Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                    Assert.Null(p.Result);
                    Assert.Contains("input 1 is not closed, and " + Refusal, p.Error);
                    Assert.Contains("the input as given has none", p.Error);
                    Assert.Contains("An open cutting surface is not a solid", p.Error);
                    Assert.Contains("the repair (eg its fill of a hole) crosses the input", p.Error);
                    Assert.Equal("MeshAutoRepair", Assert.Single(p.Report.RepairedInputs).Method);
                }
        }

        [Theory]
        [MemberData(nameof(CleanClosures))]
        public void CleanClosures_StillSucceed_WithTheResultOfTheirClosedCopy(string name)
        {
            DMesh3 open = Clean(name);
            Assert.False(ExactMeshBoolean.IsWindingClosed(open));
            SolidBooleanPipeline p = Run(Parts(open));
            Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, p.Error);
            Assert.Equal("MeshAutoRepair", Assert.Single(p.Report.RepairedInputs).Method);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            // the same result as the pipeline gets for the closed copy given closed (the check changes nothing here)
            var repair = new MeshAutoRepair(new DMesh3(open));
            repair.Apply();
            SolidBooleanPipeline closed = Run(Parts(repair.Mesh), SolidBooleanPipeline.InputRepairModes.Refuse);
            Assert.True(closed.Status == SolidBooleanPipeline.PipelineStatus.Success, closed.Error);
            Assert.Equal("", Invariance.Difference(closed.Result, p.Result));
            Assert.True(Invariance.Volume(p.Result) > 200);
        }

        // an open input that self-intersects already (the dented box of InsideOutAdviceTests without its inner bottom square):
        // its repaired copy self-intersects too, and the input is used as before: the boolean of the copy runs (here Success: the
        // removed square held the triangle that read the copy as inside-out)
        [Fact]
        public void AnInputThatSelfIntersectsAsGiven_IsUsedAsBefore()
        {
            DMesh3 dented = InsideOutAdviceTests.DentedThroughBox();
            dented.RemoveTriangle(0);
            dented.RemoveTriangle(1);
            DMesh3 open = new DMesh3(dented, true);
            Assert.False(ExactMeshBoolean.IsWindingClosed(open));
            var own = new ExactMeshArrangement(new List<DMesh3> { open }) { SelfIntersections = true };
            own.Compute();
            Assert.True(own.SelfIntersectingPairs > 0);
            SolidBooleanPipeline p = Run(Parts(open));
            Assert.Equal("MeshAutoRepair", Assert.Single(p.Report.RepairedInputs).Method);
            Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, p.Error);
            Assert.NotNull(p.Result);
            Assert.True(ExactMeshBoolean.IsWindingClosed(p.Result));
            // (the copy self-intersects as well: the input's own self-intersections are what keeps it)
            var repair = new MeshAutoRepair(new DMesh3(open));
            repair.Apply();
            var copy = new ExactMeshArrangement(new List<DMesh3> { repair.Mesh }) { SelfIntersections = true };
            copy.Compute();
            Assert.True(copy.SelfIntersectingPairs > 0);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void TheRefusalAndTheCleanClosure_AreInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            foreach (double c in new[] { -0.02, 0.05 }) {
                SolidBooleanPipeline p = Run(Parts(InsideOutAdviceTests.WavySheet(8, c)).Select(m => Invariance.Transformed(m, s)).ToList());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Contains(Refusal, p.Error);
            }
            SolidBooleanPipeline reference = Run(Parts(Clean("open box, no top")));
            SolidBooleanPipeline q = Run(Parts(Clean("open box, no top")).Select(m => Invariance.Transformed(m, s)).ToList());
            Assert.True(q.Status == SolidBooleanPipeline.PipelineStatus.Success, q.Error);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(q.Result, s)));
            else {
                double v = Invariance.Volume(reference.Result);
                Assert.InRange(Invariance.Volume(q.Result) / (s.Scale * s.Scale * s.Scale), v - 1e-6 * v, v + 1e-6 * v);
            }
        }

        // the stage time on the refusal: the repair and its check (RepairMilliseconds was set only after the repair loop, so a
        // refusal inside it left 0); a 64 x 64 sheet, so that the repair takes measurable milliseconds
        [Fact]
        public void TheRefusal_ReportsTheRepairTime()
        {
            SolidBooleanPipeline p = Run(Parts(InsideOutAdviceTests.WavySheet(64)), SolidBooleanPipeline.InputRepairModes.AutoRepair);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Contains(Refusal, p.Error);
            SolidBooleanInputRepair r = Assert.Single(p.Report.RepairedInputs);
            Assert.True(p.Report.RepairMilliseconds > 0);
            Assert.True(p.Report.RepairMilliseconds >= r.Milliseconds);
            Assert.True(p.Report.TotalMilliseconds >= p.Report.RepairMilliseconds);
        }

        [Fact]
        public void Reuse_ComputeTwice_OnOnePipeline()
        {
            var p = new SolidBooleanPipeline(Parts(InsideOutAdviceTests.WavySheet()), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.False(p.Compute());
            string first = p.Error;
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Equal(first, p.Error);
            Assert.Single(p.Report.RepairedInputs);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
