using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.SeparateSelfContacts: where one solid of the result touches itself at coincident vertices (an
    /// outer shell with its cavities), every vertex there moves back into its own fan. All inputs are procedural: boxes,
    /// octahedra and prisms that touch at an edge or a vertex, in a body that they are cut from or joined to.
    /// </summary>
    public class SolidBooleanPipelineSelfContactTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, D = ExactMeshBoolean.Operations.Difference;
        static readonly Vector3d Utm = new Vector3d(632117.25, 5712043.5, 113.75);

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) => BooleanFuzzShapes.Box(x0, y0, z0, x1, y1, z1);

        static DMesh3 Moved(DMesh3 m, Vector3d d)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices())
                c.SetVertex(v, c.GetVertex(v) + d);
            return c;
        }

        static List<DMesh3> Moved(List<DMesh3> l, Vector3d d) => l.Select(m => Moved(m, d)).ToList();

        // an outward-oriented octahedron
        static DMesh3 Octahedron(double cx, double cy, double cz, double r)
        {
            var c = new Vector3d(cx, cy, cz);
            var m = new DMesh3();
            Vector3d[] p = { new Vector3d(r, 0, 0), new Vector3d(-r, 0, 0), new Vector3d(0, r, 0), new Vector3d(0, -r, 0), new Vector3d(0, 0, r), new Vector3d(0, 0, -r) };
            foreach (Vector3d q in p)
                m.AppendVertex(c + q);
            int[][] t = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            foreach (int[] q in t)
                m.AppendTriangle(q[0], q[1], q[2]);
            return m;
        }

        // an outward-oriented prism over a counter-clockwise triangle
        static DMesh3 Prism(Vector2d a, Vector2d b, Vector2d c, double z0, double z1)
        {
            var m = new DMesh3();
            Vector2d[] p = { a, b, c };
            for (int i = 0; i < 3; ++i)
                m.AppendVertex(new Vector3d(p[i].x, p[i].y, z0));
            for (int i = 0; i < 3; ++i)
                m.AppendVertex(new Vector3d(p[i].x, p[i].y, z1));
            m.AppendTriangle(0, 2, 1);
            m.AppendTriangle(3, 4, 5);
            for (int i = 0; i < 3; ++i) {
                int j = (i + 1) % 3;
                m.AppendTriangle(i, j, 3 + j);
                m.AppendTriangle(i, 3 + j, 3 + i);
            }
            return m;
        }

        static DMesh3 Slot(double x, double y, double w, double h) => Box(x, y, -1, x + w, y + h, 9);

        // ---- the inputs: a body (the first input) minus tools, or a union

        // two square through-holes of a cube that touch along the vertical edge x = y = 4 (the same corner value in both tools)
        static List<DMesh3> EdgePinch() => new List<DMesh3> { Box(0, 0, 0, 8, 8, 8), Box(2, 2, -1, 4, 4, 9), Box(4, 4, -1, 6, 6, 9) };

        // two octahedra that touch at the vertex (1, 0, 0) and a bar over them that joins them: one solid, a handle that touches itself
        static List<DMesh3> Handle() => new List<DMesh3> { Octahedron(0, 0, 0, 1), Octahedron(2, 0, 0, 1), Box(-0.5, -0.2, 0.5, 2.5, 0.2, 0.9) };

        // two wedge-shaped through-holes (apex angle 148 degrees, open to the left and right) whose apex edges touch: the body above and below the touch is a fold of 32 degrees
        static List<DMesh3> Fold() => new List<DMesh3> {
            Box(0, 0, 0, 8, 8, 8),
            Prism(new Vector2d(4, 4), new Vector2d(3, 7.5), new Vector2d(3, 0.5), -1, 9),
            Prism(new Vector2d(4, 4), new Vector2d(5, 0.5), new Vector2d(5, 7.5), -1, 9) };

        // two box cavities that touch at the corner (3, 3, 3), inside a cube
        static List<DMesh3> CavitiesAtACorner() => new List<DMesh3> { Box(0, 0, 0, 8, 8, 8), Box(1, 1, 1, 3, 3, 3), Box(3, 3, 3, 5, 5, 5) };

        // two octahedral cavities that touch at the vertex (4, 4, 4)
        static List<DMesh3> CavitiesAtAVertex() => new List<DMesh3> { Box(0, 0, 0, 8, 8, 8), Octahedron(3, 4, 4, 1), Octahedron(5, 4, 4, 1) };

        // an octahedral cavity whose vertex (0, 4, 4) lies on the wall of the cube (a cavity that touches its outer shell at a point)
        static List<DMesh3> CavityAtTheShell() => new List<DMesh3> { Box(0, 0, 0, 8, 8, 8), Octahedron(1, 4, 4, 1) };

        // a move that is put back because a triangle then crosses another one (found by a search over such bodies, then fixed)
        static List<DMesh3> CrossingCase() => new List<DMesh3> { Box(0, 0, 0, 8, 8, 8), Slot(6, 3, 0.25, 2), Octahedron(5, 4, 4, 1), Slot(5, 1, 0.015625, 3) };

        // moves that are put back because a triangle then turns over; the contact stays in the first body
        static List<DMesh3> FlipLeavingAContact() => new List<DMesh3> {
            Box(0, 0, 0, 8, 8, 8), Octahedron(4, 3, 2, 1), Octahedron(5, 2, 2, 1), Slot(6, 6, 0.125, 3), Slot(4, 1, 0.03125, 1) };

        static List<DMesh3> FlipSeparating() => new List<DMesh3> {
            Box(0, 0, 0, 8, 8, 8), Octahedron(5, 3, 2, 2), Slot(2, 4, 1, 1), Slot(5, 2, 1, 2), Slot(6, 4, 0.015625, 1) };

        // ---- helpers

        static SolidBooleanPipeline Run(List<DMesh3> inputs, ExactMeshBoolean.Operations op, double separate)
        {
            var p = new SolidBooleanPipeline(inputs, op) { SeparateSelfContacts = separate };
            Assert.True(p.Compute(), p.Error);
            return p;
        }

        // the number of positions of the mesh with two or more vertices (independent of the pipeline)
        static int PositionsWithSeveralVertices(DMesh3 m)
        {
            return m.VertexIndices().GroupBy(v => { Vector3d p = m.GetVertex(v); return new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0); }).Count(g => g.Count() > 1);
        }

        static int MeshComponents(DMesh3 m)
        {
            var cc = new MeshConnectedComponents(m);
            cc.FindConnectedT();
            return cc.Count;
        }

        static double UlpOf(DMesh3 m)
        {
            AxisAlignedBox3d b = m.GetBounds();
            return Math.Max(b.Min.Abs.MaxAbs, b.Max.Abs.MaxAbs) * 2.220446049250313e-16;
        }

        static double EdgeLength(DMesh3 m, int e)
        {
            Index2i ev = m.GetEdgeV(e);
            return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b));
        }

        // a result that is a solid: finite, closed, valid, clean by the independent check
        static void AssertSolid(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(MeshRobustRepair.NotCleanReason(r) == "", Describe(p));
        }

        static string Describe(SolidBooleanPipeline p)
        {
            SolidBooleanReport r = p.Report;
            return $"{r.ResultKind} '{r.NotCleanReason}' contacts {r.ContactsResult} crossing {r.SelfIntersectingTriangles}; found {r.SelfContactsFound} left {r.SelfContactsLeft} moved {r.SelfContactVerticesMoved} skipped {r.SelfContactVerticesSkipped} undone {r.SelfContactMovesUndoneCrossing}/{r.SelfContactMovesUndoneFlipped}";
        }

        // the separation against the same run without it: the same triangles, only vertices of self-contacts moved, within the distance
        static void AssertOnlyContactsMoved(SolidBooleanPipeline p, SolidBooleanPipeline plain, double delta)
        {
            Assert.Equal(plain.Result.TriangleCount, p.Result.TriangleCount);
            Assert.Equal(plain.Result.MaxVertexID, p.Result.MaxVertexID);
            double tol = delta + 4 * UlpOf(p.Result);
            int moved = 0;
            double max = 0;
            var atContact = new HashSet<int>(SolidBooleanPipeline.SelfContacts(plain.Result).SelectMany(l => l));
            foreach (int v in plain.Result.VertexIndices()) {
                Vector3d a = plain.Result.GetVertex(v), b = p.Result.GetVertex(v);
                if (a == b)
                    continue;
                moved++;
                Assert.Contains(v, atContact);
                max = Math.Max(max, a.Distance(b));
            }
            Assert.Equal(p.Report.SelfContactVerticesMoved, moved);
            Assert.True(max <= tol, $"a move of {max:R} beyond {tol:R}");
            Assert.Equal(max, p.Report.SelfContactMaxMove, 12);
            for (int t = 0; t < plain.Result.MaxTriangleID; ++t)
                Assert.Equal(plain.Result.GetTriangle(t), p.Result.GetTriangle(t));
        }

        static void AssertVolumeWithin(SolidBooleanPipeline p, SolidBooleanPipeline plain, double delta)
        {
            double tol = delta * Invariance.Area(plain.Result) + 1e-12 * Math.Abs(Invariance.Volume(plain.Result));
            Assert.True(Math.Abs(Invariance.Volume(p.Result) - Invariance.Volume(plain.Result)) <= tol,
                $"volume {Invariance.Volume(p.Result):R} against {Invariance.Volume(plain.Result):R}");
        }

        // ---- the option and its defaults

        [Fact]
        public void Default_IsOff_AndTheResultKeepsItsContacts()
        {
            var p = new SolidBooleanPipeline(EdgePinch(), D);
            Assert.Equal(0, p.SeparateSelfContacts);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, p.Report.ResultKind);
            Assert.True(p.Report.OnlyContacts);
            Assert.Equal(2, p.Report.ContactsResult);
            Assert.Equal(2, PositionsWithSeveralVertices(p.Result));
            Assert.Equal(0, p.Report.SelfContactsFound + p.Report.SelfContactsLeft + p.Report.SelfContactVerticesMoved + p.Report.SelfContactVerticesSkipped
                + p.Report.SelfContactMovesUndoneCrossing + p.Report.SelfContactMovesUndoneFlipped);
            Assert.Equal(0, p.Report.SelfContactMaxMove);

            var zero = Run(EdgePinch(), D, 0);
            Assert.Equal("", Invariance.Difference(p.Result, zero.Result));
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void InvalidDistance_IsInvalidInput(double bad)
        {
            var p = new SolidBooleanPipeline(EdgePinch(), D) { SeparateSelfContacts = bad };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
            Assert.Contains("SeparateSelfContacts", p.Error);
            Assert.Null(p.Result);
            Assert.Equal(SolidBooleanResultKind.None, p.Report.ResultKind);
        }

        // ---- the separation

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EdgePinch_IsSeparated_NearAndFarFromTheOrigin(bool utm)
        {
            List<DMesh3> inputs = utm ? Moved(EdgePinch(), Utm) : EdgePinch();
            const double delta = 0.01;
            var plain = Run(inputs, D, 0);
            Assert.Equal(2, PositionsWithSeveralVertices(plain.Result));
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, plain.Report.ResultKind);

            var p = Run(inputs, D, delta);
            AssertSolid(p);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.True(p.Report.Clean);
            Assert.False(p.Report.OnlyContacts);
            Assert.Equal(0, p.Report.ContactsResult);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            Assert.Equal(2, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.Equal(4, p.Report.SelfContactVerticesMoved);
            Assert.Equal(0, p.Report.SelfContactVerticesSkipped);
            Assert.Equal(0, p.Report.SelfContactMovesUndoneCrossing + p.Report.SelfContactMovesUndoneFlipped);
            Assert.Equal(delta, p.Report.SelfContactMaxMove, 8);
            AssertOnlyContactsMoved(p, plain, delta);
            AssertVolumeWithin(p, plain, delta);
            // the cube minus two holes of 2 x 2 x 8
            Assert.Equal(512 - 2 * 32, Invariance.Volume(plain.Result), 6);
        }

        [Fact]
        public void PointContact_OfTwoFacetedSolids_JoinedByABar_IsSeparated()
        {
            const double delta = 0.01;
            var plain = Run(Handle(), U, 0);
            Assert.Equal(1, PositionsWithSeveralVertices(plain.Result));
            var p = Run(Handle(), U, delta);
            AssertSolid(p);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Equal(1, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.Equal(2, p.Report.SelfContactVerticesMoved);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            AssertOnlyContactsMoved(p, plain, delta);
            AssertVolumeWithin(p, plain, delta);
        }

        [Fact]
        public void TwoSolidsTouchingAtAPoint_AreNotSelfContacts_AndStayAsTheyWere()
        {
            // the same octahedra without the bar: two solids that touch (a contact between solids, not of one solid)
            var inputs = new List<DMesh3> { Octahedron(0, 0, 0, 1), Octahedron(2, 0, 0, 1) };
            var plain = Run(inputs, U, 0);
            var p = Run(inputs, U, 0.05);
            Assert.Equal(1, p.Report.ContactsResult);
            Assert.Equal(0, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.Equal(0, p.Report.SelfContactVerticesMoved);
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, p.Report.ResultKind);
            Assert.Equal("", Invariance.Difference(plain.Result, p.Result));
            Assert.Equal(plain.Report.NotCleanReason, p.Report.NotCleanReason);
        }

        [Fact]
        public void TwoBoxesTouchingAtAnEdge_AreTwoSolids_AndStayAsTheyWere()
        {
            var inputs = new List<DMesh3> { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1) };
            var plain = Run(inputs, U, 0);
            var p = Run(inputs, U, 0.05);
            Assert.True(p.Report.ContactsResult > 0);
            Assert.Equal(0, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactVerticesMoved);
            Assert.Equal("", Invariance.Difference(plain.Result, p.Result));
            Assert.Equal(plain.Report.ContactsResult, p.Report.ContactsResult);
        }

        [Fact]
        public void ACleanResult_IsTheOneWithoutTheOption_BitForBit()
        {
            var inputs = new List<DMesh3> { Box(0, 0, 0, 2, 2, 2), Box(1, 1, 1, 3, 3, 3), Box(2.5, 0.5, 0.5, 4, 1.5, 1.5) };
            var plain = Run(inputs, U, 0);
            var p = Run(inputs, U, 0.1);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Equal(0, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactVerticesMoved);
            Assert.Equal("", Invariance.Difference(plain.Result, p.Result));
            Assert.Equal(plain.Report.NotCleanReason, p.Report.NotCleanReason);
            Assert.Equal(plain.Report.ResultTriangles, p.Report.ResultTriangles);
        }

        [Fact]
        public void Fold_OfTheBodyBetweenTwoWedgeHoles_IsSeparated()
        {
            const double delta = 0.02;
            var plain = Run(Fold(), D, 0);
            Assert.True(PositionsWithSeveralVertices(plain.Result) > 0);
            var p = Run(Fold(), D, delta);
            AssertSolid(p);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.True(p.Report.SelfContactsFound > 0);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.True(p.Report.SelfContactVerticesMoved >= 2 * p.Report.SelfContactsFound);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            AssertOnlyContactsMoved(p, plain, delta);
            AssertVolumeWithin(p, plain, delta);
        }

        [Fact]
        public void TwoCavitiesTouchingAtACorner_AreOneSolidsContact_AndSeparated()
        {
            const double delta = 0.05;
            foreach (List<DMesh3> inputs in new[] { CavitiesAtACorner(), CavitiesAtAVertex() }) {
                var plain = Run(inputs, D, 0);
                Assert.Equal(1, PositionsWithSeveralVertices(plain.Result));
                Assert.Equal(3, MeshComponents(plain.Result));          // the cube's shell and two cavities
                var p = Run(inputs, D, delta);
                AssertSolid(p);
                Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
                Assert.Equal(1, p.Report.SelfContactsFound);
                Assert.Equal(0, p.Report.SelfContactsLeft);
                Assert.Equal(2, p.Report.SelfContactVerticesMoved);
                Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
                AssertOnlyContactsMoved(p, plain, delta);
                AssertVolumeWithin(p, plain, delta);
            }
        }

        [Fact]
        public void ACavityTouchingItsOuterShellAtAPoint_IsSeparated()
        {
            const double delta = 0.05;
            var plain = Run(CavityAtTheShell(), D, 0);
            Assert.Equal(1, PositionsWithSeveralVertices(plain.Result));
            var p = Run(CavityAtTheShell(), D, delta);
            AssertSolid(p);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Equal(1, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.True(p.Report.SelfContactVerticesMoved >= 1);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            AssertOnlyContactsMoved(p, plain, delta);
            AssertVolumeWithin(p, plain, delta);
        }

        [Fact]
        public void SelfContacts_AreThoseOfOneSolid_NotOfDifferentSolids()
        {
            // a cube with two cavities that touch (one solid), and a second cube whose corner touches the first one's: only the first counts
            var m = new DMesh3();
            var ed = new MeshEditor(m);
            ed.AppendMesh(Run(CavitiesAtACorner(), D, 0).Result);
            ed.AppendMesh(Box(8, 8, 8, 10, 10, 10));
            Assert.Equal(2, PositionsWithSeveralVertices(m));
            List<int> only = Assert.Single(SolidBooleanPipeline.SelfContacts(m));
            Assert.Equal(2, only.Count);
            Vector3d at = m.GetVertex(only[0]);
            Assert.Equal(new Vector3d(3, 3, 3), at);
            Assert.Equal(at, m.GetVertex(only[1]));

            // a cube inside the hollow of another one, touching the hollow's corner: another solid, so no self-contact
            var other = new DMesh3();
            var ed2 = new MeshEditor(other);
            ed2.AppendMesh(Run(new List<DMesh3> { Box(0, 0, 0, 8, 8, 8), Box(2, 2, 2, 6, 6, 6) }, D, 0).Result);
            ed2.AppendMesh(Box(6, 6, 6, 7, 7, 7));
            Assert.Equal(1, PositionsWithSeveralVertices(other));
            Assert.Empty(SolidBooleanPipeline.SelfContacts(other));
        }

        [Fact]
        public void AVertexMovesTowardsTheAreaWeightedCentroidsOfItsFan()
        {
            // an independent computation of the direction and the step from the result without the option
            foreach (var c in new[] { (Fold(), D), (EdgePinch(), D), (Handle(), U), (CavityAtTheShell(), D) }) {
                const double delta = 0.02;
                var plain = Run(c.Item1, c.Item2, 0);
                var p = Run(c.Item1, c.Item2, delta);
                DMesh3 m = plain.Result;
                int checkedMoves = 0;
                foreach (int v in SolidBooleanPipeline.SelfContacts(m).SelectMany(l => l)) {
                    if (m.GetVertex(v) == p.Result.GetVertex(v))
                        continue;
                    Vector3d at = m.GetVertex(v), dir = Vector3d.Zero;
                    foreach (int t in m.VtxTrianglesItr(v)) {
                        Index3i tv = m.GetTriangle(t);
                        Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), cc = m.GetVertex(tv.c);
                        dir += ((a + b + cc) / 3 - at) * m.GetTriArea(t);
                    }
                    double shortest = m.VtxVerticesItr(v).Min(w => at.Distance(m.GetVertex(w)));
                    double step = Math.Min(delta, 0.25 * shortest);
                    Vector3d expected = at + dir.Normalized * step;
                    Assert.True(expected.Distance(p.Result.GetVertex(v)) <= 8 * UlpOf(m) + 1e-9 * step, $"vertex {v}: {expected} against {p.Result.GetVertex(v)}");
                    checkedMoves++;
                }
                Assert.True(checkedMoves > 0);
            }
        }

        [Fact]
        public void PositiveAndNegativeZero_AreOnePosition()
        {
            // the tools touch along the edge x = y = 0, one with the corner written -0.0, the other with 0.0
            var inputs = new List<DMesh3> { Box(-4, -4, 0, 4, 4, 8), Box(-2, -2, -1, -0.0, -0.0, 9), Box(0.0, 0.0, -1, 2, 2, 9) };
            var plain = Run(inputs, D, 0);
            Assert.Equal(2, plain.Report.ContactsResult);
            var p = Run(inputs, D, 0.01);
            AssertSolid(p);
            Assert.Equal(2, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            // the same for a mesh whose coincident vertices differ in the sign of a zero
            var m = new DMesh3(plain.Result);
            foreach (int v in m.VertexIndices()) {
                Vector3d q = m.GetVertex(v);
                if (q.x == 0 && q.y == 0)
                    m.SetVertex(v, new Vector3d(v % 2 == 0 ? -0.0 : 0.0, q.y, q.z));
            }
            Assert.Equal(2, SolidBooleanPipeline.SelfContacts(m).Count);
        }

        [Fact]
        public void VerticesOfNoTriangle_AreNoContact()
        {
            DMesh3 m = Run(EdgePinch(), D, 0).Result;
            int before = SolidBooleanPipeline.SelfContacts(m).Count;
            m.AppendVertex(new Vector3d(100, 100, 100));
            m.AppendVertex(new Vector3d(100, 100, 100));
            Assert.Equal(before, SolidBooleanPipeline.SelfContacts(m).Count);
        }

        [Fact]
        public void AContactOfAnotherSolidAtTheSamePosition_IsSeparatedAsWell_ContactsElsewhereAreNot()
        {
            // the pinch body with an octahedron whose bottom vertex touches the pinch point (4, 4, 8): the body's own vertices
            // move away, so the contact of the two solids there is gone too
            DMesh3 body = Run(EdgePinch(), D, 0).Result;
            var inputs = new List<DMesh3> { body, Octahedron(4, 4, 9, 1) };
            var plain = Run(inputs, U, 0);
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, plain.Report.ResultKind);
            Assert.Equal(2, plain.Report.ContactsResult);
            var p = Run(inputs, U, 0.01);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Equal(0, p.Report.ContactsResult);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            AssertSolid(p);
        }

        [Fact]
        public void TheStaleTree_NeedsThePad()
        {
            // triangle A in the plane z = 0; B far above it when the tree was built, then a vertex of B moved down through A
            var m = new DMesh3();
            int a0 = m.AppendVertex(new Vector3d(0, 0, 0)), a1 = m.AppendVertex(new Vector3d(10, 0, 0)), a2 = m.AppendVertex(new Vector3d(0, 10, 0));
            int b0 = m.AppendVertex(new Vector3d(2, 2, 1000)), b1 = m.AppendVertex(new Vector3d(1000, 2, 1000)), b2 = m.AppendVertex(new Vector3d(2, 1000, 1000));
            int ta = m.AppendTriangle(a0, a1, a2);
            m.AppendTriangle(b0, b1, b2);
            // (small triangles far to the side, at z = 0 and z = 100, so that the tree splits the two layers into boxes of their own)
            for (int i = 0; i < 24; ++i)
                foreach (double z in new[] { 0.0, 1000.0 }) {
                    double x = 20 + 2 * i;
                    m.AppendTriangle(m.AppendVertex(new Vector3d(x, 0, z)), m.AppendVertex(new Vector3d(x + 1, 0, z)), m.AppendVertex(new Vector3d(x, 1, z)));
                }
            var tree = new DMeshAABBTree3(new DMesh3(m), true);
            const double d = 1001;
            m.SetVertex(b0, new Vector3d(2, 2, 1000 - d));
            Assert.Empty(SolidBooleanPipeline.CrossingTriangles(m, new[] { a0 }, tree, 0));
            Assert.Contains(ta, SolidBooleanPipeline.CrossingTriangles(m, new[] { a0 }, tree, SolidBooleanPipeline.TreePad(d)));
            Assert.Contains(ta, SolidBooleanPipeline.CrossingTriangles(m, new[] { a0 }));
            Assert.Equal(2 * d, SolidBooleanPipeline.TreePad(d));
        }

        [Fact]
        public void ACancelledRun_IsCancelled_AndTheStepLeavesNoUncheckedMove()
        {
            var source = new System.Threading.CancellationTokenSource();
            source.Cancel();
            var p = new SolidBooleanPipeline(EdgePinch(), D) { SeparateSelfContacts = 0.01, Cancellation = source.Token };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            // the step itself: cancelled before its first check, every move is put back
            DMesh3 m = Run(EdgePinch(), D, 0).Result;
            DMesh3 before = new DMesh3(m);
            var report = new SolidBooleanReport();
            SolidBooleanPipeline.SeparateSelfContactsOf(m, 0.01, report, source.Token);
            Assert.Equal("", Invariance.Difference(before, m));
            Assert.Equal(0, report.SelfContactVerticesMoved);
            Assert.Equal(2, report.SelfContactsLeft);
        }

        // ---- the bounds of a move

        [Fact]
        public void ADistanceBelowTheCoordinateResolution_IsSkipped_AndTheResultIsUnchanged()
        {
            foreach (bool utm in new[] { false, true }) {
                List<DMesh3> inputs = utm ? Moved(EdgePinch(), Utm) : EdgePinch();
                var plain = Run(inputs, D, 0);
                var p = Run(inputs, D, 1e-15);
                Assert.Equal(2, p.Report.SelfContactsFound);
                Assert.Equal(2, p.Report.SelfContactsLeft);
                Assert.Equal(0, p.Report.SelfContactVerticesMoved);
                Assert.Equal(4, p.Report.SelfContactVerticesSkipped);
                Assert.Equal(0, p.Report.SelfContactMaxMove);
                Assert.Equal("", Invariance.Difference(plain.Result, p.Result));
                Assert.Equal(SolidBooleanResultKind.SolidWithContacts, p.Report.ResultKind);
                Assert.Equal(plain.Report.NotCleanReason, p.Report.NotCleanReason);
            }
        }

        [Fact]
        public void TheSixteenUlpBound_SeparatesWhatIsSkippedFromWhatIsMoved()
        {
            // a vertex stays if its step is not above 16 units in the last place of its largest coordinate (4 at the bottom of the pinch edge, 8 at the top)
            const double eps = 2.220446049250313e-16;
            var none = Run(EdgePinch(), D, 60 * eps);
            Assert.Equal(0, none.Report.SelfContactVerticesMoved);
            Assert.Equal(4, none.Report.SelfContactVerticesSkipped);
            Assert.Equal(2, none.Report.SelfContactsLeft);
            var bottom = Run(EdgePinch(), D, 100 * eps);
            Assert.Equal(2, bottom.Report.SelfContactVerticesMoved);
            Assert.Equal(2, bottom.Report.SelfContactVerticesSkipped);
            Assert.Equal(1, bottom.Report.SelfContactsLeft);
            var all = Run(EdgePinch(), D, 136 * eps);
            Assert.Equal(4, all.Report.SelfContactVerticesMoved);
            Assert.Equal(0, all.Report.SelfContactVerticesSkipped);
            Assert.Equal(0, all.Report.SelfContactsLeft);
        }

        [Fact]
        public void ADistanceLargerThanTheEdges_MovesEachVertexByAQuarterOfItsShortestEdge()
        {
            var cases = new List<(List<DMesh3> inputs, ExactMeshBoolean.Operations op)> {
                (EdgePinch(), D), (Handle(), U), (Fold(), D), (CavitiesAtACorner(), D), (CavityAtTheShell(), D) };
            foreach (var c in cases) {
                var plain = Run(c.inputs, c.op, 0);
                var p = Run(c.inputs, c.op, 1e9);
                Assert.True(p.Report.SelfContactVerticesMoved > 0, $"{c.op} {c.inputs.Count}: found {p.Report.SelfContactsFound} skipped {p.Report.SelfContactVerticesSkipped} undone {p.Report.SelfContactMovesUndoneCrossing} {p.Report.SelfContactMovesUndoneFlipped}");
                Assert.True(Invariance.AllFinite(p.Result));
                Assert.True(p.Result.IsClosed());
                double expect = 0;
                foreach (int v in SolidBooleanPipeline.SelfContacts(plain.Result).SelectMany(l => l)) {
                    double shortest = plain.Result.VtxVerticesItr(v).Min(w => plain.Result.GetVertex(v).Distance(plain.Result.GetVertex(w)));
                    expect = Math.Max(expect, 0.25 * shortest);
                }
                // (a move that is put back lowers the largest one)
                Assert.True(p.Report.SelfContactMaxMove <= expect + 4 * UlpOf(p.Result), $"move {p.Report.SelfContactMaxMove:R}, bound {expect:R}");
                Assert.True(p.Report.SelfContactMaxMove > 0);
            }
            // where nothing is put back the bound is reached
            var edge = Run(EdgePinch(), D, 1e9);
            Assert.Equal(0, edge.Report.SelfContactMovesUndoneCrossing + edge.Report.SelfContactMovesUndoneFlipped);
            var edgePlain = Run(EdgePinch(), D, 0);
            double bound = 0;
            foreach (int v in SolidBooleanPipeline.SelfContacts(edgePlain.Result).SelectMany(l => l))
                bound = Math.Max(bound, 0.25 * edgePlain.Result.VtxVerticesItr(v).Min(w => edgePlain.Result.GetVertex(v).Distance(edgePlain.Result.GetVertex(w))));
            Assert.Equal(bound, edge.Report.SelfContactMaxMove, 9);
        }

        // ---- the moves that are put back

        [Fact]
        public void AMoveThatMakesATriangleCrossAnother_IsPutBack()
        {
            var plain = Run(CrossingCase(), D, 0);
            var p = Run(CrossingCase(), D, 1);
            Assert.True(p.Report.SelfContactMovesUndoneCrossing > 0);
            Assert.Equal(1, p.Report.SelfContactsFound);
            // whatever stays is checked: no triangle crosses another beyond what they share, and the result is a clean solid
            AssertSolid(p);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Empty(SolidBooleanPipeline.CrossingTriangles(p.Result, p.Result.VertexIndices()));
            AssertOnlyContactsMoved(p, plain, 1);
        }

        [Fact]
        public void AMoveThatTurnsATriangleOver_IsPutBack_AndTheContactCanStay()
        {
            var plain = Run(FlipLeavingAContact(), D, 0);
            var p = Run(FlipLeavingAContact(), D, 0.3);
            Assert.True(p.Report.SelfContactMovesUndoneFlipped > 0);
            Assert.Equal(0, p.Report.SelfContactMovesUndoneCrossing);
            Assert.Equal(1, p.Report.SelfContactsFound);
            Assert.Equal(1, p.Report.SelfContactsLeft);
            Assert.Equal(0, p.Report.SelfContactVerticesMoved);
            // the contact stays, so the result is the one without the option, and a result with contacts left stays SolidWithContacts
            Assert.Equal("", Invariance.Difference(plain.Result, p.Result));
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, p.Report.ResultKind);
            Assert.True(p.Report.OnlyContacts);
            Assert.Equal(1, p.Report.ContactsResult);
            Assert.False(p.Report.Clean);
        }

        [Fact]
        public void AMoveThatTurnsATriangleOver_IsPutBack_WhileTheOthersStay()
        {
            var plain = Run(FlipSeparating(), D, 0);
            var p = Run(FlipSeparating(), D, 0.3);
            Assert.True(p.Report.SelfContactMovesUndoneFlipped > 0);
            Assert.True(p.Report.SelfContactVerticesMoved > 0);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            AssertSolid(p);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            AssertOnlyContactsMoved(p, plain, 0.3);
            // no triangle of the result is turned against its old normal
            foreach (int t in plain.Result.TriangleIndices())
                Assert.True(plain.Result.GetTriNormal(t).Dot(p.Result.GetTriNormal(t)) > 0, $"triangle {t}: {plain.Result.GetTriNormal(t)} {p.Result.GetTriNormal(t)}");
        }

        // ---- reuse, determinism, invariance

        [Fact]
        public void ComputeTwice_OnOneInstance_GivesTheSameResultAndReport()
        {
            var p = new SolidBooleanPipeline(EdgePinch(), D) { SeparateSelfContacts = 0.01 };
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            string r1 = p.Report.ResultKind + " " + p.Report.SelfContactsFound + " " + p.Report.SelfContactVerticesMoved + " " + p.Report.SelfContactMaxMove.ToString("R");
            Assert.True(p.Compute(), p.Error);
            string r2 = p.Report.ResultKind + " " + p.Report.SelfContactsFound + " " + p.Report.SelfContactVerticesMoved + " " + p.Report.SelfContactMaxMove.ToString("R");
            Assert.Equal(r1, r2);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            // the inputs are not changed by the separation
            Assert.Equal("", Invariance.Difference(EdgePinch()[1], p.Inputs[1]));
            // the option is read on each run
            p.SeparateSelfContacts = 0;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0, p.Report.SelfContactsFound);
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, p.Report.ResultKind);
        }

        [Fact]
        public void RepeatedRuns_AreBitIdentical()
        {
            DMesh3 first = null;
            for (int i = 0; i < 4; ++i) {
                var p = Run(Fold(), D, 0.02);
                if (first == null)
                    first = p.Result;
                else
                    Assert.Equal("", Invariance.Difference(first, p.Result));
            }
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void EdgePinch_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            const double delta = 0.01;
            var reference = Run(EdgePinch(), D, delta);
            List<DMesh3> inputs = EdgePinch().Select(m => Invariance.Transformed(m, s)).ToList();
            var p = Run(inputs, D, delta * s.Scale);
            Assert.True(Invariance.AllFinite(p.Result), s.Name);
            Assert.True(p.Result.IsClosed(), s.Name);
            if (s.IsRotated && PositionsWithSeveralVertices(Run(inputs, D, 0).Result) == 0) {
                // (the rotated faces of the tools are not planar: they overlap or leave a gap, so no contact forms and nothing is
                // changed; the rotation with a contact is covered by "rotated, scale 1e-3, utm" and by Handle_IsInvariantUnderSimilarities)
                Assert.Equal(0, p.Report.SelfContactsFound);
                Assert.Equal(0, p.Report.SelfContactVerticesMoved);
                return;
            }
            Assert.Equal(reference.Report.SelfContactsFound, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.Equal(reference.Report.SelfContactVerticesMoved, p.Report.SelfContactVerticesMoved);
            Assert.Equal(0, PositionsWithSeveralVertices(p.Result));
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Equal(reference.Result.TriangleCount, p.Result.TriangleCount);
            if (s.IsExact) {
                // a power of two: the same result, scaled, bit for bit (and the report scaled)
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                Assert.Equal(reference.Report.SelfContactMaxMove * s.Scale, p.Report.SelfContactMaxMove);
            } else {
                // (the move is the distance plus the rounding of the new coordinates)
                Assert.InRange(p.Report.SelfContactMaxMove / s.Scale, delta - 8 * s.Resolution(8), delta + 8 * s.Resolution(8));
            }
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Handle_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            const double delta = 0.01;
            var reference = Run(Handle(), U, delta);
            var p = Run(Handle().Select(m => Invariance.Transformed(m, s)).ToList(), U, delta * s.Scale);
            Assert.Equal(reference.Report.SelfContactsFound, p.Report.SelfContactsFound);
            Assert.Equal(0, p.Report.SelfContactsLeft);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
