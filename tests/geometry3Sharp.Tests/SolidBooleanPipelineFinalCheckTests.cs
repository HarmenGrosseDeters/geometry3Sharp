using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The final check of SolidBooleanPipeline (FinalCheck): a result with "self-intersections" is told apart into contacts
    /// only (OnlyContacts) and real crossings (SelfIntersectingTriangles).
    /// </summary>
    public class SolidBooleanPipelineFinalCheckTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        static DMesh3 Together(params DMesh3[] parts)
        {
            var m = new DMesh3();
            var ed = new MeshEditor(m);
            foreach (DMesh3 p in parts)
                ed.AppendMesh(p);
            return m;
        }

        // two boxes that touch along the edge x = y = 1 (not welded: duplicated vertices there)
        static DMesh3 EdgeTouching() { return Together(Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1)); }

        // the same plus a box that passes through the top face of the first one, away from the contact
        static DMesh3 EdgeTouchingAndCrossing() { return Together(Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1), Box(0.25, 0.25, 0.5, 0.5, 0.5, 1.5)); }

        static SolidBooleanReport Check(DMesh3 m)
        {
            var r = new SolidBooleanReport();
            SolidBooleanPipeline.FinalCheck(m, r);
            return r;
        }

        [Fact]
        public void EdgeContactOnly_IsOnlyContacts()
        {
            SolidBooleanReport r = Check(EdgeTouching());
            Assert.Equal("self-intersections", r.NotCleanReason);
            Assert.True(r.ContactsResult > 0);
            Assert.Equal(0, r.SelfIntersectingTriangles);
            Assert.True(r.OnlyContacts);
        }

        [Fact]
        public void ContactAndRealCrossing_AreReportedSeparately()
        {
            SolidBooleanReport r = Check(EdgeTouchingAndCrossing());
            Assert.Equal("self-intersections", r.NotCleanReason);
            Assert.True(r.ContactsResult > 0);
            Assert.True(r.SelfIntersectingTriangles > 0);
            Assert.False(r.OnlyContacts);
            // the crossing box alone against the first box: no contact, the same crossing triangles
            SolidBooleanReport c = Check(Together(Box(0, 0, 0, 1, 1, 1), Box(0.25, 0.25, 0.5, 0.5, 0.5, 1.5)));
            Assert.Equal(0, c.ContactsResult);
            Assert.Equal(c.SelfIntersectingTriangles, r.SelfIntersectingTriangles);
            Assert.False(c.OnlyContacts);
        }

        [Fact]
        public void CleanMesh_ComputesNothing()
        {
            SolidBooleanReport r = Check(Box(0, 0, 0, 1, 1, 1));
            Assert.Equal("", r.NotCleanReason);
            Assert.True(r.Clean);
            Assert.Equal(0, r.SelfIntersectingTriangles);
            Assert.False(r.OnlyContacts);
        }

        [Fact]
        public void AnotherReasonThanSelfIntersections_WithContacts_ComputesNothing()
        {
            // the contact is there but the mesh is not closed: NotCleanReason names that first, OnlyContacts is not set
            DMesh3 m = EdgeTouching();
            m.RemoveTriangle(0);
            SolidBooleanReport r = Check(m);
            Assert.True(r.ContactsResult > 0);
            Assert.NotEqual("", r.NotCleanReason);
            Assert.NotEqual("self-intersections", r.NotCleanReason);
            Assert.Equal(0, r.SelfIntersectingTriangles);
            Assert.False(r.OnlyContacts);
        }

        [Fact]
        public void FinalCheck_ResetsEarlierValues()
        {
            var r = new SolidBooleanReport();
            SolidBooleanPipeline.FinalCheck(EdgeTouchingAndCrossing(), r);
            Assert.True(r.SelfIntersectingTriangles > 0);
            SolidBooleanPipeline.FinalCheck(Box(0, 0, 0, 1, 1, 1), r);
            Assert.Equal(0, r.SelfIntersectingTriangles);
            Assert.False(r.OnlyContacts);
            Assert.Equal(0, r.ContactsResult);
            // OnlyContacts is reset on its own: from a contact alone to a mesh with another reason
            SolidBooleanPipeline.FinalCheck(EdgeTouching(), r);
            Assert.True(r.OnlyContacts);
            DMesh3 open = EdgeTouching();
            open.RemoveTriangle(0);
            SolidBooleanPipeline.FinalCheck(open, r);
            Assert.NotEqual("self-intersections", r.NotCleanReason);
            Assert.False(r.OnlyContacts);
            Assert.Equal(0, r.SelfIntersectingTriangles);
        }

        [Fact]
        public void PipelineUnionOfBoxesTouchingAtAnEdge_ReportsOnlyContacts()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1) }, ExactMeshBoolean.Operations.Union);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("self-intersections", p.Report.NotCleanReason);
            Assert.True(p.Report.ContactsResult > 0);
            Assert.Equal(0, p.Report.SelfIntersectingTriangles);
            Assert.True(p.Report.OnlyContacts);
            Assert.False(p.Report.Clean);
        }

        [Fact]
        public void PipelineOfDisjointBoxes_IsCleanWithDefaults()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 1, 1, 1), Box(3, 0, 0, 4, 1, 1) }, ExactMeshBoolean.Operations.Union);
            Assert.True(p.Compute(), p.Error);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Equal(0, p.Report.SelfIntersectingTriangles);
            Assert.False(p.Report.OnlyContacts);
        }

        [Fact]
        public void Reuse_ComputeTwiceGivesTheSameReport()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1) }, ExactMeshBoolean.Operations.Union);
            Assert.True(p.Compute(), p.Error);
            var first = (p.Report.NotCleanReason, p.Report.ContactsResult, p.Report.SelfIntersectingTriangles, p.Report.OnlyContacts);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(first, (p.Report.NotCleanReason, p.Report.ContactsResult, p.Report.SelfIntersectingTriangles, p.Report.OnlyContacts));
            Assert.True(p.Report.OnlyContacts);
        }

        [Fact]
        public void CrossingTriangles_CountsADegenerateTriangle_AndATriangleThatSharesAVertexWithOne()
        {
            // vertex 0 has only a degenerate triangle (0, 3, 4) on a line, next to a proper one (1, 2, 10) it does not touch;
            // vertex 5 has a proper triangle (5, 6, 7) that shares vertex 6 with the degenerate (6, 8, 9); vertex 11 has a
            // proper triangle (11, 12, 13) alone. Nothing crosses anything.
            var m = new DMesh3();
            foreach (var p in new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), new Vector3d(-2, 0, 0),
                                      new Vector3d(10, 0, 0), new Vector3d(11, 0, 0), new Vector3d(10, 1, 0), new Vector3d(12, 0, 0), new Vector3d(13, 0, 0),
                                      new Vector3d(1, 1, 0), new Vector3d(20, 0, 0), new Vector3d(21, 0, 0), new Vector3d(20, 1, 0) })
                m.AppendVertex(p);
            int degenerate = m.AppendTriangle(0, 3, 4);
            m.AppendTriangle(1, 2, 10);
            int nextToDegenerate = m.AppendTriangle(5, 6, 7);
            m.AppendTriangle(6, 8, 9);
            m.AppendTriangle(11, 12, 13);
            Assert.Equal(new[] { degenerate }, SolidBooleanPipeline.CrossingTriangles(m, new[] { 0 }).ToArray());
            Assert.Equal(new[] { nextToDegenerate }, SolidBooleanPipeline.CrossingTriangles(m, new[] { 5 }).ToArray());
            Assert.Empty(SolidBooleanPipeline.CrossingTriangles(m, new[] { 11 }));
        }

        [Fact]
        public void CrossingTriangles_GrowsTheQueryBoxesByThePad()
        {
            // the tree was built while triangle U was 30 further along x; it now crosses T. A query box grown by 2 * 30 finds
            // it, one not grown does not (the boxes of the tree are those of the old positions; small triangles at x = 10 to 15
            // and 40 to 45 keep T alone in its leaf, so no box of the tree around U contains T)
            var m = new DMesh3();
            foreach (var p in new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0),
                                      new Vector3d(0.5, 0.5, -1), new Vector3d(1, 0.5, 1), new Vector3d(0.5, 1, 1) })
                m.AppendVertex(p);
            int T = m.AppendTriangle(0, 1, 2);
            m.AppendTriangle(3, 4, 5);
            for (int k = 0; k < 12; ++k) {
                double x = k < 6 ? 10 + k : 34 + k;
                int a = m.AppendVertex(new Vector3d(x, 0, 0)), b = m.AppendVertex(new Vector3d(x + 0.1, 0, 0)), c = m.AppendVertex(new Vector3d(x, 0.1, 0));
                m.AppendTriangle(a, b, c);
            }
            var before = new DMesh3(m, false, MeshComponents.None);
            for (int v = 3; v < 6; ++v)
                before.SetVertex(v, before.GetVertex(v) + new Vector3d(30, 0, 0));
            var tree = new DMeshAABBTree3(before, true);
            Assert.Empty(SolidBooleanPipeline.CrossingTriangles(m, new[] { 0 }, tree, 0));
            Assert.Equal(new[] { T }, SolidBooleanPipeline.CrossingTriangles(m, new[] { 0 }, tree, 60).ToArray());
            Assert.Equal(new[] { T }, SolidBooleanPipeline.CrossingTriangles(m, new[] { 0 }).ToArray());
        }

        [Fact]
        public void PairPass_CopiesTheCountsOfThePairedResult()
        {
            // the pair pass keeps the paired result: its report fields are those of a final check of the returned mesh
            var inputs = new List<DMesh3> { SoupBox(Vector3d.Zero), SoupBox(new Vector3d(2, 2, 0)) };
            var p = new SolidBooleanPipeline(inputs, ExactMeshBoolean.Operations.Union) { PairContactsMode = SolidBooleanPipeline.PairContactsModes.Auto, TargetTriangleCount = 4 };
            Assert.True(p.Compute(), p.Error);
            Assert.True(p.Report.PairPassRan);
            Assert.True(p.Report.PairPassKept);
            SolidBooleanReport r = Check(p.Result);
            Assert.Equal(r.NotCleanReason, p.Report.NotCleanReason);
            Assert.Equal(r.ContactsResult, p.Report.ContactsResult);
            Assert.Equal(r.SelfIntersectingTriangles, p.Report.SelfIntersectingTriangles);
            Assert.Equal(r.OnlyContacts, p.Report.OnlyContacts);
        }

        static DMesh3 SoupBox(Vector3d c) { return ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(c, 8)); }

        [Fact]
        public void SetFinalCheck_CopiesAllFourFields_IncludingACleanSourceOverANotCleanTarget()
        {
            SolidBooleanReport crossing = Check(EdgeTouchingAndCrossing()), contact = Check(EdgeTouching()), clean = Check(Box(0, 0, 0, 1, 1, 1));
            foreach (var pair in new[] { (crossing, contact), (contact, crossing), (clean, crossing), (clean, contact), (crossing, clean), (contact, clean) }) {
                var target = new SolidBooleanReport();
                target.SetFinalCheck(pair.Item2);
                target.SetFinalCheck(pair.Item1);
                Assert.Equal(pair.Item1.ContactsResult, target.ContactsResult);
                Assert.Equal(pair.Item1.NotCleanReason, target.NotCleanReason);
                Assert.Equal(pair.Item1.SelfIntersectingTriangles, target.SelfIntersectingTriangles);
                Assert.Equal(pair.Item1.OnlyContacts, target.OnlyContacts);
            }
            Assert.True(crossing.SelfIntersectingTriangles > 0 && crossing.ContactsResult > 0 && !crossing.OnlyContacts);
            Assert.True(contact.OnlyContacts && contact.ContactsResult > 0);
            Assert.True(clean.Clean);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Counts_AreInvariantOverSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (DMesh3 m in new[] { EdgeTouching(), EdgeTouchingAndCrossing() }) {
                SolidBooleanReport a = Check(m);
                SolidBooleanReport b = Check(Invariance.Transformed(m, s));
                Assert.Equal(a.NotCleanReason, b.NotCleanReason);
                Assert.Equal(a.ContactsResult, b.ContactsResult);
                Assert.Equal(a.SelfIntersectingTriangles, b.SelfIntersectingTriangles);
                Assert.Equal(a.OnlyContacts, b.OnlyContacts);
            }
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
