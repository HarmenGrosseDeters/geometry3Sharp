using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The advice of SolidBooleanPipeline's InsideOutInput refusal: Orientation = ByNesting derives the enclosed solid only for an
    /// embedded input, so for a closed input that self-intersects (it keeps its given orientation under ByNesting) the message names
    /// the self-intersections instead of recommending ByNesting, and a closed copy made by the input repair as what the repair produced.
    /// Only the message: Status, Report and Result are as before.
    /// </summary>
    public class InsideOutAdviceTests
    {
        internal static readonly ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.OrientationModes Nesting = ExactMeshBoolean.OrientationModes.ByNesting;

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        // The unit cube, outward, whose top face is a fan of four triangles to an apex pulled down and out through the box to
        // (-3, -3, -0.5): closed, consistently oriented, self-intersecting (the fan crosses the walls and the bottom), volume
        // 1 - 1.5 / 3 = 0.5 > 0 (the sheared pyramid of the dent keeps its volume). Inside the dent's pyramid outside the box the
        // winding number is -1. The apex is the smallest point by the order of ExactMeshArrangement.ComparePoints (key x + 0.75 y +
        // 0.57 z), so count_inside_out reads a triangle of the fan (#1464), and the centroid of every fan triangle lies outside the
        // box (x < 0), in front of it the dent's -1: the one component is inside-out in part, InsideOutCells is 1 in every mode.
        // (The small square [0.45, 0.55]^2 of the bottom gives RepairSelfIntersectionTests a hole to open.)
        internal static DMesh3 DentedThroughBox()
        {
            const double a = 0.45, b = 0.55;
            var m = new DMesh3();
            var o = new[] { m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(1, 1, 0)), m.AppendVertex(new Vector3d(0, 1, 0)) };
            var t = new[] { m.AppendVertex(new Vector3d(0, 0, 1)), m.AppendVertex(new Vector3d(1, 0, 1)), m.AppendVertex(new Vector3d(1, 1, 1)), m.AppendVertex(new Vector3d(0, 1, 1)) };
            var n = new[] { m.AppendVertex(new Vector3d(a, a, 0)), m.AppendVertex(new Vector3d(b, a, 0)), m.AppendVertex(new Vector3d(b, b, 0)), m.AppendVertex(new Vector3d(a, b, 0)) };
            int apex = m.AppendVertex(new Vector3d(-3, -3, -0.5));
            // the bottom (normal -z): the inner square first, then the ring
            m.AppendTriangle(n[0], n[2], n[1]);
            m.AppendTriangle(n[0], n[3], n[2]);
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(o[i], n[j], o[j]);
                m.AppendTriangle(o[i], n[i], n[j]);
            }
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(o[i], o[j], t[j]);
                m.AppendTriangle(o[i], t[j], t[i]);
                m.AppendTriangle(t[i], t[j], apex);
            }
            return m;
        }

        internal static List<DMesh3> Parts(DMesh3 tool) { return new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), tool }; }

        [Fact]
        public void TheFixture_IsClosed_SelfIntersecting_PositiveVolume_AndInsideOutInPart()
        {
            DMesh3 m = DentedThroughBox();
            Assert.True(ExactMeshBoolean.IsWindingClosed(m));
            Assert.Equal(0.5, Invariance.Volume(m), 12);
            var arr = new ExactMeshArrangement(new List<DMesh3> { m }) { SelfIntersections = true };
            arr.Compute();
            Assert.True(arr.SelfIntersectingPairs > 0);
            foreach (var mode in new[] { ExactMeshBoolean.OrientationModes.Refuse, Nesting }) {
                var op = new ExactMeshBoolean(Parts(m), D) { CountInsideOutCells = true, Orientation = mode };
                Assert.True(op.Compute(), op.Error);
                Assert.Equal(1, op.InsideOutCells);
                Assert.Equal(0, op.ReorientedTriangles);
            }
        }

        [Fact]
        public void ByNesting_ASelfIntersectingInputWithAnInsideOutPart_TheMessageNamesTheSelfIntersections_NotByNesting()
        {
            var p = new SolidBooleanPipeline(Parts(DentedThroughBox()), D) { Orientation = Nesting };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Null(p.Result);
            Assert.Equal(1, p.Report.InsideOutCells);
            Assert.DoesNotContain("Orientation = ByNesting derives", p.Error);
            Assert.Contains("input 1 self-intersects", p.Error);
            Assert.Contains("ByNesting cannot orient it", p.Error);
            Assert.Contains("fix the self-intersections", p.Error);
            // (only that advice: "orient them" would not help)
            Assert.EndsWith("; fix the self-intersections or set AllowInsideOutInputs", p.Error);
            Assert.Contains("AllowInsideOutInputs", p.Error);
            Assert.DoesNotContain("repair", p.Error);
            // (the status and result on request are unchanged: taken as given)
            var allowed = new SolidBooleanPipeline(Parts(DentedThroughBox()), D) { Orientation = Nesting, AllowInsideOutInputs = true };
            Assert.True(allowed.Compute(), allowed.Error);
            Assert.Equal(1, allowed.Report.InsideOutCells);
        }

        [Fact]
        public void WithoutByNesting_TheMessageRecommendsIt_WithTheSelfIntersectionsAsItsCondition()
        {
            var p = new SolidBooleanPipeline(Parts(DentedThroughBox()), D);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Contains("Orientation = ByNesting derives the enclosed solid of an input without self-intersections", p.Error);
            Assert.Contains("self-intersecting pairs", p.Error);
            // an embedded inside-out input: the plain recommendation, which helps there
            var cube = Box(0, 0, 0, 1, 1, 1);
            foreach (int t in cube.TriangleIndices().ToList())
                cube.ReverseTriOrientation(t);
            var q = new SolidBooleanPipeline(Parts(cube), D);
            Assert.False(q.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, q.Status);
            Assert.Contains("orient them (Orientation = ByNesting derives the enclosed solid) or set AllowInsideOutInputs", q.Error);
            var nested = new SolidBooleanPipeline(Parts(cube), D) { Orientation = Nesting };
            Assert.True(nested.Compute(), nested.Error);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void TheRefusalAndItsMessage_AreInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var reference = new SolidBooleanPipeline(Parts(DentedThroughBox()), D) { Orientation = Nesting };
            Assert.False(reference.Compute());
            var p = new SolidBooleanPipeline(Parts(DentedThroughBox()).Select(m => Invariance.Transformed(m, s)).ToList(), D) { Orientation = Nesting };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Equal(reference.Report.InsideOutCells, p.Report.InsideOutCells);
            if (s.IsExact)
                Assert.Equal(reference.Error, p.Error);
            else
                Assert.Contains("input 1 self-intersects", p.Error);
        }

        [Fact]
        public void Reuse_ComputeTwice_AndAcrossModes_OnOnePipeline_TheMessageIsThatOfTheMode()
        {
            var p = new SolidBooleanPipeline(Parts(DentedThroughBox()), D) { Orientation = Nesting };
            Assert.False(p.Compute());
            string first = p.Error;
            Assert.False(p.Compute());
            Assert.Equal(first, p.Error);
            p.Orientation = ExactMeshBoolean.OrientationModes.Refuse;
            Assert.False(p.Compute());
            Assert.Contains("Orientation = ByNesting derives", p.Error);
        }

        // An open, non-planar sheet without self-intersections (n x n grid over the unit square, normal +z): z = 0.1 sin(2 pi u)
        // sin(2 pi v) + c sin(pi u) sin(pi v), so that it lies above and below the plane of its boundary (z = 0); c = -0.02: more below.
        internal static DMesh3 WavySheet(int n = 8, double c = -0.02)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double u = i / (double)n, v = j / (double)n;
                    m.AppendVertex(new Vector3d(u, v, 0.1 * Math.Sin(2 * Math.PI * u) * Math.Sin(2 * Math.PI * v) + c * Math.Sin(Math.PI * u) * Math.Sin(Math.PI * v)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i;
                    m.AppendTriangle(a, a + 1, a + n + 2);
                    m.AppendTriangle(a, a + n + 2, a + n + 1);
                }
            return m;
        }

        // The diagnosis of the self-intersecting repaired copy of an open sheet. MeshAutoRepair closes a sheet by filling its one
        // boundary loop (fill_any_holes, MinimalHoleFill): a second surface that spans the same loop, here the flat square z = 0.
        // The sheet crosses it, so the closed copy self-intersects along the crossing curves and consists of lenses between sheet
        // and fill, one component; the global orientation (repair_orientation(true)) makes its total volume positive, which
        // leaves the lenses on the other side of the fill negative: inverted in part. ByNesting cannot orient it (not embedded)
        // and the deciding triangle of the component lies in a negative lens (InsideOutCells 1). MeshAutoRepair itself is not
        // changed (pinned here); the pipeline refuses such a copy as OpenInput before the boolean (RepairSelfIntersectionTests).
        [Fact]
        public void Diagnosis_AnOpenWavySheet_ClosedByMeshAutoRepair_SelfIntersects_AndIsInsideOutInPart()
        {
            DMesh3 sheet = WavySheet();
            Assert.False(ExactMeshBoolean.IsWindingClosed(sheet));
            var own = new ExactMeshArrangement(new List<DMesh3> { sheet }) { SelfIntersections = true };
            own.Compute();
            Assert.Equal(0, own.SelfIntersectingPairs);
            var repair = new MeshAutoRepair(new DMesh3(sheet));
            Assert.True(repair.Apply());
            DMesh3 closed = repair.Mesh;
            Assert.True(ExactMeshBoolean.IsWindingClosed(closed));
            Assert.True(closed.TriangleCount > sheet.TriangleCount);
            var arr = new ExactMeshArrangement(new List<DMesh3> { closed }) { SelfIntersections = true };
            arr.Compute();
            Assert.True(arr.SelfIntersectingPairs > 0);
            Assert.True(Invariance.Volume(closed) > 1e-3);
            var components = new MeshConnectedComponents(closed);
            components.FindConnectedT();
            Assert.Equal(1, components.Count);
            var op = new ExactMeshBoolean(Parts(closed), D) { CountInsideOutCells = true, Orientation = Nesting };
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(1, op.InsideOutCells);
            // the closed copy given as it is: the refusal names its self-intersections, not ByNesting
            var p = new SolidBooleanPipeline(Parts(closed), D) { Orientation = Nesting };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Contains("input 1 self-intersects", p.Error);
            Assert.DoesNotContain("Orientation = ByNesting derives", p.Error);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
