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

        // The unit cube, outward, whose top face is a fan of four triangles to an apex pushed down through the bottom face to
        // z = -0.5: closed, consistently oriented, self-intersecting (the fan crosses the bottom), volume 1 - 1.5 / 3 = 0.5 > 0.
        // Below the bottom, inside the tip of the dent (|x - 0.5|, |y - 0.5| < 1/6 at z = 0), the winding number is -1: the bottom
        // has a small square [0.45, 0.55]^2 there whose triangles come first (the component's deciding triangle), so the one
        // component is inside-out in part, and InsideOutCells is 1 in every orientation mode.
        internal static DMesh3 DentedThroughBox()
        {
            const double a = 0.45, b = 0.55;
            var m = new DMesh3();
            var o = new[] { m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(1, 1, 0)), m.AppendVertex(new Vector3d(0, 1, 0)) };
            var t = new[] { m.AppendVertex(new Vector3d(0, 0, 1)), m.AppendVertex(new Vector3d(1, 0, 1)), m.AppendVertex(new Vector3d(1, 1, 1)), m.AppendVertex(new Vector3d(0, 1, 1)) };
            var n = new[] { m.AppendVertex(new Vector3d(a, a, 0)), m.AppendVertex(new Vector3d(b, a, 0)), m.AppendVertex(new Vector3d(b, b, 0)), m.AppendVertex(new Vector3d(a, b, 0)) };
            int apex = m.AppendVertex(new Vector3d(0.5, 0.5, -0.5));
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
        public void Reuse_ComputeTwice_AndAcrossModes_TheAdviceFollowsTheLastRun()
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

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
