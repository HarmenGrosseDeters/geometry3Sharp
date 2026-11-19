using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ContourLoftStack (docs/design/CONTOUR_LOFT.md section 10): several contours per slice, branching, merging, caps, holes. Procedural
    /// inputs; every Success is checked for closed, manifold, no self-intersection, the Euler characteristic of the expected topology and
    /// the volume.
    /// </summary>
    public class ContourLoftStackTests
    {
        // ---- inputs ----

        static List<Vector3d> Disc(int n, double r, double z, double cx = 0, double cy = 0, double phase = 0)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                double a = phase + 2 * Math.PI * i / n;
                c.Add(new Vector3d(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z));
            }
            return c;
        }

        // a rectangle x0..x1, y0..y1 with k points per side (counter-clockwise)
        static List<Vector3d> Rect(double x0, double x1, double y0, double y1, double z, int k = 3)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x0 + (x1 - x0) * i / k, y0, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x1, y0 + (y1 - y0) * i / k, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x1 - (x1 - x0) * i / k, y1, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x0, y1 - (y1 - y0) * i / k, z));
            return c;
        }

        static IList<IList<IList<Vector3d>>> Slices(params IList<Vector3d>[][] s) =>
            s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c).ToList()).ToList();

        static IList<Vector3d>[] Sl(params IList<Vector3d>[] contours) => contours;

        static ContourLoftStackResult Loft(IList<IList<IList<Vector3d>>> slices, bool caps = true)
        {
            return new ContourLoftStack { CapEnds = caps }.Compute(slices);
        }

        static int Euler(DMesh3 m) => m.VertexCount - m.EdgeCount + m.TriangleCount;

        static double Volume(DMesh3 mesh)
        {
            double vol = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tr = mesh.GetTriangle(t);
                vol += mesh.GetVertex(tr.a).Dot(mesh.GetVertex(tr.b).Cross(mesh.GetVertex(tr.c))) / 6;
            }
            return vol;
        }

        static void AssertClosedSolid(ContourLoftStackResult r, int euler)
        {
            Assert.True(r.Status == ContourLoftStackStatus.Success, r.Status + ": " + r.Reason);
            DMesh3 m = r.Mesh;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.False(MeshRepairMetrics.HasSelfIntersections(m));
            foreach (int v in m.VertexIndices()) Assert.False(m.IsBowtieVertex(v));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(m));
            foreach (int e in m.EdgeIndices()) Assert.False(m.IsBoundaryEdge(e));
            Assert.Equal(euler, Euler(m));
            Assert.True(Volume(m) > 0);
        }

        // ---- the inputs of the cases ----

        // two cylinders (16-gons at x = -2, 2) joined into one stadium-like block
        static IList<IList<IList<Vector3d>>> Merge() => Slices(
            Sl(Disc(16, 1, 0, -2), Disc(16, 1, 0, 2)),
            Sl(Disc(16, 1, 1, -2), Disc(16, 1, 1, 2)),
            Sl(Rect(-3.5, 3.5, -1.2, 1.2, 2, 6)));

        static IList<IList<IList<Vector3d>>> Reverse(IList<IList<IList<Vector3d>>> s)
        {
            double top = s.Count - 1;
            return s.Reverse().Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.Select(p => new Vector3d(p.x, p.y, top - p.z)).Reverse().ToList()).ToList()).ToList();
        }

        // a bar splitting into three legs, a Y-like branch with a bridge at the top plane
        static IList<IList<IList<Vector3d>>> Branch3() => Slices(
            Sl(Rect(-5, 5, -1.5, 1.5, 0, 8)),
            Sl(Disc(12, 1, 1, -3.5), Disc(12, 1, 1, 0), Disc(12, 1, 1, 3.5)),
            Sl(Disc(12, 1, 2, -3.5), Disc(12, 1, 2, 0), Disc(12, 1, 2, 3.5)));

        // a bar whose two ends sit beyond two discs that are cut by it: the pieces of the overlap are cut contours (intermediate plane)
        static IList<IList<IList<Vector3d>>> BranchCut() => Slices(
            Sl(Rect(-3, 3, -1, 1, 0, 6)),
            Sl(Disc(24, 1.5, 1, -3), Disc(24, 1.5, 1, 3)));

        // slices of a torus: a bar, two legs, a bar
        static IList<IList<IList<Vector3d>>> Torus() => Slices(
            Sl(Rect(-4, 4, -1, 1, 0, 6)),
            Sl(Rect(-4, -2, -1, 1, 1, 3), Rect(2, 4, -1, 1, 1, 3)),
            Sl(Rect(-4, 4, -1, 1, 2, 6)));

        // a washer: annuli (outer contour and hole) 1:1 through the stack, capped at both ends
        static IList<IList<IList<Vector3d>>> Washer() => Slices(
            Sl(Disc(20, 3, 0), Disc(20, 1, 0)),
            Sl(Disc(20, 3.2, 1), Disc(20, 1.2, 1)),
            Sl(Disc(20, 3, 2), Disc(20, 1, 2)));

        // two overlapping rows: 2 contours below, 2 above, overlapping in a zig-zag (one group with three pieces)
        static IList<IList<IList<Vector3d>>> Zigzag() => Slices(
            Sl(Rect(-4, -0.5, -1, 1, 0, 4), Rect(0.5, 4, -1, 1, 0, 4)),
            Sl(Rect(-3, 1, -1.2, 1.2, 1, 4), Rect(2, 5, -1.2, 1.2, 1, 3)));

        // ---- topology and closure ----

        [Fact]
        public void TwoCylindersMergeIntoOne_IsASphere()
        {
            var r = Loft(Merge());
            AssertClosedSolid(r, 2);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Band && g.Slab == 0);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Merge && g.Slab == 1 && g.Lower.Count == 2 && g.Upper.Count == 1);
        }

        [Fact]
        public void OneCylinderSplitsIntoTwo_IsASphere()
        {
            var r = Loft(Reverse(Merge()));
            AssertClosedSolid(r, 2);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Branch && g.Lower.Count == 1 && g.Upper.Count == 2);
        }

        [Fact]
        public void YBranch_OneIntoThree_BridgesLeaveNoNonManifoldEdgeOrVertex()
        {
            var r = Loft(Branch3());
            AssertClosedSolid(r, 2);
            var g = r.Groups.Single(x => x.Kind == ContourLoftGroupKind.Branch);
            Assert.Equal(1, g.Lower.Count); Assert.Equal(3, g.Upper.Count);
            AssertEdgeAndVertexManifold(r.Mesh);
        }

        [Fact]
        public void YBranch_ThreeIntoOne_Mirror()
        {
            var r = Loft(Reverse(Branch3()));
            AssertClosedSolid(r, 2);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Merge && g.Lower.Count == 3);
            AssertEdgeAndVertexManifold(r.Mesh);
        }

        static void AssertEdgeAndVertexManifold(DMesh3 m)
        {
            foreach (int e in m.EdgeIndices()) {
                Index4i et = m.GetEdge(e);
                Assert.True(et.d == DMesh3.InvalidID ? false : true, "an edge has fewer than two triangles");
                Assert.NotEqual(DMesh3.InvalidID, et.c);
            }
            foreach (int v in m.VertexIndices()) {
                Assert.False(m.IsBowtieVertex(v));
                // the fan of every vertex is one closed ring: the number of triangles equals the number of edges at the vertex
                Assert.Equal(m.GetVtxEdgeCount(v), m.GetVtxTriangleCount(v));
            }
        }

        [Fact]
        public void ContainedPieces_NeedNoIntermediatePlane_AtTheirOwnSlice()
        {
            // the pieces are the contours themselves: no vertex beyond the input's, and the bridge lies in the slice plane
            var cases = new[] { (Merge(), 2), (Reverse(Merge()), 2), (Branch3(), 2), (Reverse(Branch3()), 2), (Torus(), 0) };
            foreach (var (s, euler) in cases) {
                var r = Loft(s);
                AssertClosedSolid(r, euler);
                Assert.Equal(s.Sum(sl => sl.Sum(c => c.Count)), r.Mesh.VertexCount);
            }
        }

        [Fact]
        public void CutPieces_UseAnIntermediatePlane_AndStayASphere()
        {
            var r = Loft(BranchCut());
            AssertClosedSolid(r, 2);
            int inputVertices = BranchCut().Sum(sl => sl.Sum(c => c.Count));
            Assert.True(r.Mesh.VertexCount > inputVertices, "no intermediate vertices");
            // the intermediate plane is at the middle between the slices (z = 0.5)
            var mid = r.Mesh.VertexIndices().Where(v => v >= inputVertices).Select(v => r.Mesh.GetVertex(v).z).Where(z => Math.Abs(z - 0.5) < 1e-9);
            Assert.NotEmpty(mid);
        }

        [Fact]
        public void BranchFraction_MovesTheIntermediatePlane()
        {
            var r = new ContourLoftStack { CapEnds = true, BranchFraction = 0.25 }.Compute(BranchCut());
            AssertClosedSolid(r, 2);
            int inputVertices = BranchCut().Sum(sl => sl.Sum(c => c.Count));
            Assert.Contains(r.Mesh.VertexIndices().Where(v => v >= inputVertices), v => Math.Abs(r.Mesh.GetVertex(v).z - 0.25) < 1e-9);
        }

        [Fact]
        public void TorusBySlices_HasGenusOne()
        {
            var r = Loft(Torus());
            AssertClosedSolid(r, 0);
            AssertEdgeAndVertexManifold(r.Mesh);
        }

        [Fact]
        public void Washer_NestedContoursAreTunnels_AndTheCapsAreAnnuli()
        {
            var r = Loft(Washer());
            AssertClosedSolid(r, 0);
            // the volume of two frusta-like layers: between the bounds of the extreme and the middle annulus
            double vol = Volume(r.Mesh);
            double a0 = Math.PI * (9 - 1) * 0.9, a1 = Math.PI * (3.2 * 3.2 - 1.2 * 1.2) * 1.1;
            Assert.InRange(vol, 2 * a0, 2 * a1);
        }

        [Fact]
        public void HoleThatAppears_AndOneThatVanishes_AreCapped()
        {
            // a blind hole from the top: the hole appears at slice 1 and runs to the last slice
            var s = Slices(Sl(Disc(16, 3, 0)), Sl(Disc(16, 3, 1), Disc(12, 1, 1)), Sl(Disc(16, 3, 2), Disc(12, 1, 2)));
            var r = Loft(s);
            AssertClosedSolid(r, 2);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Appearing && g.Slab == 0);
            var r2 = Loft(Reverse(s));
            AssertClosedSolid(r2, 2);
            Assert.Contains(r2.Groups, g => g.Kind == ContourLoftGroupKind.Vanishing && g.Slab == 1);
        }

        [Fact]
        public void ContourThatEndsOrStartsBetweenSlices_GetsACap()
        {
            // two towers; the second one ends after slice 1
            var s = Slices(Sl(Disc(12, 1, 0, -3), Disc(12, 1, 0, 3)), Sl(Disc(12, 1, 1, -3), Disc(12, 1, 1, 3)), Sl(Disc(12, 1, 2, -3)));
            var r = Loft(s);
            AssertClosedSolid(r, 4);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Vanishing && g.Slab == 1);
            var r2 = Loft(Reverse(s));
            AssertClosedSolid(r2, 4);
            Assert.Contains(r2.Groups, g => g.Kind == ContourLoftGroupKind.Appearing);
        }

        [Fact]
        public void ZigzagOverlap_IsOneComplexGroup_AndASphere()
        {
            var r = Loft(Zigzag());
            AssertClosedSolid(r, 2);
            var g = r.Groups.Single();
            Assert.Equal(ContourLoftGroupKind.Complex, g.Kind);
        }

        [Fact]
        public void WithoutCapEnds_TheEndsStayOpen_AndTheRestIsManifold()
        {
            var r = Loft(Merge(), caps: false);
            Assert.Equal(ContourLoftStackStatus.Success, r.Status);
            Assert.Contains(r.Mesh.EdgeIndices(), e => r.Mesh.IsBoundaryEdge(e));
            Assert.False(MeshRepairMetrics.HasSelfIntersections(r.Mesh));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(r.Mesh));
        }

        // ---- stage 1 results stay the same, bit for bit ----

        [Fact]
        public void SingleContours_AreChainedStage1Bands()
        {
            var rand = new Random(5);
            IList<Vector3d> Star(int n, double z, double cx) => Enumerable.Range(0, n).Select(i => {
                double a = 2 * Math.PI * (i + 0.6 * rand.NextDouble()) / n, rr = 1.5 * (0.7 + 0.6 * rand.NextDouble());
                return new Vector3d(cx + rr * Math.Cos(a), rr * Math.Sin(a), z);
            }).ToList();
            var c0 = Star(11, 0, 0); var c1 = Star(7, 1, 0.1); var c2 = Star(13, 2.2, -0.1);
            var s = Slices(Sl(c0), Sl(c1), Sl(c2));
            var r = Loft(s, caps: false);
            Assert.Equal(ContourLoftStackStatus.Success, r.Status);
            int[] off = { 0, 11, 18 };
            var expected = new List<string>();
            var lists = new[] { c0, c1, c2 };
            for (int k = 0; k < 2; ++k) {
                var b = new ContourLoft { CheckSelfIntersections = false }.Compute(lists[k], lists[k + 1]);
                Assert.Equal(ContourLoftStatus.Success, b.Status);
                int m = lists[k].Count;
                foreach (int t in b.Mesh.TriangleIndices()) {
                    Index3i tr = b.Mesh.GetTriangle(t);
                    int Map(int v) => v < m ? off[k] + v : off[k + 1] + v - m;
                    expected.Add(Canon(new Index3i(Map(tr.a), Map(tr.b), Map(tr.c))));
                }
            }
            var got = r.Mesh.TriangleIndices().Select(t => Canon(r.Mesh.GetTriangle(t)));
            Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), got.OrderBy(x => x, StringComparer.Ordinal));
        }

        static string Canon(Index3i t)
        {
            int[] a = { t.a, t.b, t.c };
            int k = a[0] <= a[1] && a[0] <= a[2] ? 0 : a[1] <= a[2] ? 1 : 2;
            return a[k] + "," + a[(k + 1) % 3] + "," + a[(k + 2) % 3];
        }

        [Fact]
        public void ClockwiseContours_GiveTheSameSurface()
        {
            var s = Merge();
            var cw = s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.AsEnumerable().Reverse().ToList()).ToList()).ToList();
            var a = Loft(s); var b = Loft(cw);
            AssertClosedSolid(b, 2);
            Assert.Equal(PositionTriangles(a.Mesh), PositionTriangles(b.Mesh));
        }

        static List<string> PositionTriangles(DMesh3 m)
        {
            string P(int v) { var p = m.GetVertex(v); return p.x.ToString("R") + " " + p.y.ToString("R") + " " + p.z.ToString("R"); }
            return m.TriangleIndices().Select(t => {
                Index3i tr = m.GetTriangle(t);
                string[] a = { P(tr.a), P(tr.b), P(tr.c) };
                int k = string.CompareOrdinal(a[0], a[1]) <= 0 && string.CompareOrdinal(a[0], a[2]) <= 0 ? 0 : string.CompareOrdinal(a[1], a[2]) <= 0 ? 1 : 2;
                return a[k] + "|" + a[(k + 1) % 3] + "|" + a[(k + 2) % 3];
            }).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        [Fact]
        public void TwoSeparateCylinders_VolumeIsTwoPrisms()
        {
            var s = Slices(Sl(Disc(16, 1, 0, -3), Disc(16, 1, 0, 3)), Sl(Disc(16, 1, 1, -3), Disc(16, 1, 1, 3)));
            var r = Loft(s);
            AssertClosedSolid(r, 4);
            double area = 0.5 * 16 * Math.Sin(2 * Math.PI / 16);
            Assert.Equal(2 * area * 1.0, Volume(r.Mesh), 12);
        }

        // ---- every status once; never a mesh ----

        static void AssertFails(ContourLoftStackResult r, ContourLoftStackStatus status)
        {
            Assert.Equal(status, r.Status);
            Assert.Null(r.Mesh);
            Assert.NotEqual("", r.Reason);
        }

        [Fact]
        public void InvalidInput_EachWay()
        {
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 0)))), ContourLoftStackStatus.InvalidInput);
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 0)), Sl(new List<Vector3d> { new Vector3d(0, 0, 1), new Vector3d(1, 0, 1) }))), ContourLoftStackStatus.InvalidInput);
            var nan = Disc(8, 1, 1); nan[2] = new Vector3d(double.NaN, 0, 1);
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 0)), Sl(nan))), ContourLoftStackStatus.InvalidInput);
            var line = new List<Vector3d> { new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(2, 0, 1) };
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 0)), Sl(line))), ContourLoftStackStatus.InvalidInput);
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 1)), Sl(Disc(8, 1, 0)), Sl(Disc(8, 1, 0.5)))), ContourLoftStackStatus.InvalidInput);
            var rep = Disc(8, 1, 1); rep.Insert(1, rep[0]);
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 0)), Sl(rep))), ContourLoftStackStatus.InvalidInput);
        }

        [Fact]
        public void EmptySlices_EndTheSolidsAround_AndAnAllEmptyStackIsInvalid()
        {
            var s = Slices(Sl(Disc(10, 1, 0)), Sl(), Sl(Disc(10, 1, 2)), Sl(Disc(10, 1, 3)));
            var r = Loft(s);
            AssertClosedSolid(r, 4);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Vanishing && g.Slab == 0);
            Assert.Contains(r.Groups, g => g.Kind == ContourLoftGroupKind.Appearing && g.Slab == 1);
            AssertFails(Loft(Slices(Sl(), Sl(), Sl())), ContourLoftStackStatus.InvalidInput);
            AssertClosedSolid(Loft(Slices(Sl(), Sl(Disc(10, 1, 1)), Sl(Disc(10, 1, 2)))), 2);
        }

        [Fact]
        public void CrossingAndTouchingContours_AreReported()
        {
            AssertFails(Loft(Slices(Sl(Rect(0, 2, 0, 2, 0), Rect(1, 3, 1, 3, 0)), Sl(Rect(0, 2, 0, 2, 1)))), ContourLoftStackStatus.CrossingContours);
            // two squares sharing a corner vertex
            var a = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0) };
            var b = new List<Vector3d> { new Vector3d(1, 1, 0), new Vector3d(2, 1, 0), new Vector3d(2, 2, 0), new Vector3d(1, 2, 0) };
            AssertFails(Loft(Slices(Sl(a, b), Sl(Rect(0, 2, 0, 2, 1)))), ContourLoftStackStatus.CrossingContours);
            // a vertex of one square on the edge of the other
            var c = new List<Vector3d> { new Vector3d(1, 0.5, 0), new Vector3d(2, 0.2, 0), new Vector3d(2, 0.8, 0) };
            AssertFails(Loft(Slices(Sl(Rect(0, 1, 0, 1, 0, 1), c), Sl(Rect(0, 2, 0, 2, 1)))), ContourLoftStackStatus.CrossingContours);
        }

        [Fact]
        public void Unsupported_BranchWithHoles_AndEndingContourWithContinuingHole()
        {
            // one contour with a hole splitting into two
            var s = Slices(Sl(Rect(-3, 3, -1, 1, 0, 6), Disc(8, 0.3, 0)), Sl(Disc(12, 1.2, 1, -2), Disc(12, 1.2, 1, 2)));
            AssertFails(Loft(s), ContourLoftStackStatus.Unsupported);
            // a ring ends, but its hole continues inside a small disc with a hole of its own
            var t = Slices(Sl(Disc(16, 3, 0), Disc(16, 1, 0)), Sl(Disc(16, 0.9, 1), Disc(12, 0.5, 1)), Sl(Disc(16, 0.9, 2), Disc(12, 0.5, 2)));
            AssertFails(Loft(t), ContourLoftStackStatus.Unsupported);
        }

        [Fact]
        public void BridgeNotFound_WhenTheContainerHidesEveryPair()
        {
            // two discs in the two arms of a U: every segment between them crosses the notch
            var u = new List<Vector3d> {
                new Vector3d(-3, -1, 1), new Vector3d(3, -1, 1), new Vector3d(3, 3, 1), new Vector3d(1, 3, 1),
                new Vector3d(1, 0, 1), new Vector3d(-1, 0, 1), new Vector3d(-1, 3, 1), new Vector3d(-3, 3, 1) };
            var s = Slices(Sl(Disc(10, 0.6, 0, -2, 2), Disc(10, 0.6, 0, 2, 2)), Sl(u));
            AssertFails(Loft(s), ContourLoftStackStatus.BridgeNotFound);
        }

        [Fact]
        public void NoAdmissibleBand_InvalidCost_TooLarge_AreForwardedFromTheBands()
        {
            var s = Slices(Sl(Disc(8, 1, 0)), Sl(Disc(8, 1, 1)));
            AssertFails(new ContourLoftStack { IsAdmissible = t => false }.Compute(s), ContourLoftStackStatus.NoAdmissibleBand);
            AssertFails(new ContourLoftStack { Cost = ContourLoftCost.Custom, CustomCost = t => double.NaN }.Compute(s), ContourLoftStackStatus.InvalidCost);
            AssertFails(new ContourLoftStack { MaxCells = 4 }.Compute(s), ContourLoftStackStatus.TooLarge);
        }

        [Fact]
        public void BandFailed_WhenASliceIsNotSeparatedFromItsNeighbour()
        {
            // the second contour is tilted so that some of its vertices lie below the first one's plane, while its mean height is above it
            var tilted = Disc(8, 1, 1).Select(p => new Vector3d(p.x, p.y, 1 + 3 * p.x)).ToList();
            AssertFails(Loft(Slices(Sl(Disc(8, 1, 0)), Sl(tilted))), ContourLoftStackStatus.BandFailed);
        }

        [Fact]
        public void SelfIntersecting_IsReported_NotReturned()
        {
            // a cost that rewards long spanning edges twists the band of two coaxial squares (every edge runs through the axis)
            var loft = new ContourLoftStack { Cost = ContourLoftCost.Custom, CustomCost = t => -((t.A - t.C).Length + (t.B - t.C).Length) };
            var r = loft.Compute(Slices(Sl(Disc(4, 1, 0)), Sl(Disc(4, 1, 1))));
            AssertFails(r, ContourLoftStackStatus.SelfIntersecting);
        }

        [Fact]
        public void CapFailed_WhenTheCapMesherRejects()
        {
            var r = new ContourLoftStack { CapEnds = true, CapMesher = new QualityMesh2 { MinAngle = 75 } }.Compute(Slices(Sl(Disc(8, 1, 0)), Sl(Disc(8, 1, 1))));
            AssertFails(r, ContourLoftStackStatus.CapFailed);
        }

        [Fact]
        public void CapsWithRefinement_AddPointsInTheSlicePlane_AndStayClean()
        {
            var r = new ContourLoftStack { CapEnds = true, CapMesher = new QualityMesh2 { MinAngle = 20 } }.Compute(Merge());
            AssertClosedSolid(r, 2);
            int inputVertices = Merge().Sum(sl => sl.Sum(c => c.Count));
            var added = r.Mesh.VertexIndices().Where(v => v >= inputVertices).ToList();
            Assert.NotEmpty(added);
            Assert.All(added, v => Assert.True(Math.Abs(r.Mesh.GetVertex(v).z) < 1e-12 || Math.Abs(r.Mesh.GetVertex(v).z - 2) < 1e-12));
        }

        [Fact]
        public void ATallSolid_IsNotCleanAndReportedAsSuch()
        {
            // a height of 1e7 times the width fails the clean check's degenerate-triangle threshold: never a silent result
            var r = Loft(Slices(Sl(Disc(8, 1, 0)), Sl(Disc(8, 1, 1e7))));
            Assert.True(r.Status == ContourLoftStackStatus.CapFailed || r.Status == ContourLoftStackStatus.BandFailed, r.Status + " " + r.Reason);
            Assert.Null(r.Mesh);
        }

        [Fact]
        public void TwoGroupsInOneSlab_EachGetsItsOwnTreatment()
        {
            // left: a bar cut by two wider discs (intermediate plane); right, far away: a bar holding two small discs (no intermediate vertices)
            var s = Slices(
                Sl(Rect(-3, 3, -1, 1, 0, 6), Rect(37, 43, -1, 1, 0, 6)),
                Sl(Disc(24, 1.5, 1, -3), Disc(24, 1.5, 1, 3), Disc(12, 0.6, 1, 38.5), Disc(12, 0.6, 1, 41.5)));
            var r = Loft(s);
            AssertClosedSolid(r, 4);
            int inputVertices = s.Sum(sl => sl.Sum(c => c.Count));
            var inter = r.Mesh.VertexIndices().Where(v => v >= inputVertices && Math.Abs(r.Mesh.GetVertex(v).z - 0.5) < 1e-9).ToList();
            Assert.NotEmpty(inter);
            Assert.All(inter, v => Assert.True(r.Mesh.GetVertex(v).x < 20, "an intermediate vertex in the contained group"));
            Assert.Equal(2, r.Groups.Count(g => g.Kind == ContourLoftGroupKind.Branch));
        }

        [Fact]
        public void Arguments_AreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => new ContourLoftStack().Compute(null));
            Assert.Throws<ArgumentException>(() => new ContourLoftStack { Cost = ContourLoftCost.Custom }.Compute(Merge()));
            Assert.Throws<ArgumentException>(() => new ContourLoftStack { MaxCells = 0 }.Compute(Merge()));
            Assert.Throws<ArgumentException>(() => new ContourLoftStack { BranchFraction = 1 }.Compute(Merge()));
            Assert.Throws<ArgumentException>(() => new ContourLoftStack { BranchFraction = 0 }.Compute(Merge()));
        }

        // ---- invariance, reuse, determinism ----

        static IList<IList<IList<Vector3d>>> Jitter(IList<IList<IList<Vector3d>>> s, int seed, double amount)
        {
            var rand = new Random(seed);
            return s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.Select(p =>
                new Vector3d(p.x + amount * (rand.NextDouble() - 0.5), p.y + amount * (rand.NextDouble() - 0.5), p.z)).ToList()).ToList()).ToList();
        }

        static IEnumerable<(string name, IList<IList<IList<Vector3d>>> slices)> InvarianceCases()
        {
            yield return ("merge", Jitter(Merge(), 1, 0.12));
            yield return ("cut branch", Jitter(BranchCut(), 2, 0.12));
            yield return ("zigzag", Jitter(Zigzag(), 3, 0.12));
            yield return ("torus", Jitter(Torus(), 4, 0.12));
            yield return ("branch3", Jitter(Branch3(), 5, 0.12));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarity_GivesTheSameSurface(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (var (cname, slices) in InvarianceCases()) {
                foreach (bool caps in new[] { false, true }) {
                    var expected = Loft(slices, caps);
                    Assert.True(expected.Status == ContourLoftStackStatus.Success, cname + ": " + expected.Status + " " + expected.Reason);
                    var mapped = slices.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.Select(sim.Apply).ToList()).ToList()).ToList();
                    var got = Loft(mapped, caps);
                    Assert.True(got.Status == ContourLoftStackStatus.Success, cname + " " + sim.Name + ": " + got.Status + " " + got.Reason);
                    if (sim.IsExact)
                        Assert.Equal("", Invariance.Difference(expected.Mesh, Invariance.MappedBack(got.Mesh, sim)));
                    else {
                        // the pieces and their numbering are the same; where a piece of the overlap runs along a contour edge, the band has a
                        // planar quad whose diagonal is a tie of the search (rounding decides), so compare what the diagonal cannot change
                        Assert.Equal(expected.Mesh.VertexCount, got.Mesh.VertexCount);
                        Assert.Equal(expected.Mesh.TriangleCount, got.Mesh.TriangleCount);
                        Assert.Equal(Euler(expected.Mesh), Euler(got.Mesh));
                        // the numbering of the intermediate vertices is canonical: vertex i is at the same place
                        double tol = 100 * sim.Resolution(10) + 1e-9;
                        foreach (int v in expected.Mesh.VertexIndices())
                            Assert.True((sim.Inverse(got.Mesh.GetVertex(v)) - expected.Mesh.GetVertex(v)).Length <= tol, cname + " / " + sim.Name + ": vertex " + v + " moved");
                        double ea = Invariance.Area(expected.Mesh) * sim.Scale * sim.Scale, ga = Invariance.Area(got.Mesh);
                        Assert.True(Math.Abs(ea - ga) <= 1e-6 * ea, cname + " / " + sim.Name + ": area " + ea + " vs " + ga);
                        if (caps) {
                            Assert.Equal("", MeshRobustRepair.NotCleanReason(got.Mesh));
                            double ev = Invariance.Volume(expected.Mesh) * sim.Scale * sim.Scale * sim.Scale, gv = Invariance.Volume(got.Mesh);
                            Assert.True(Math.Abs(ev - gv) <= 1e-6 * Math.Abs(ev), cname + " / " + sim.Name + ": volume " + ev + " vs " + gv);
                        }
                    }
                    Assert.Equal(expected.Groups.Select(g => g.Kind), got.Groups.Select(g => g.Kind));
                }
            }
        }

        static string Fingerprint(ContourLoftStackResult r) =>
            r.Status + "|" + string.Join(";", r.Groups.Select(g => g.Slab + ":" + g.Kind + ":" + string.Join(",", g.Lower) + ">" + string.Join(",", g.Upper))) + "|" +
            (r.Mesh == null ? "" : string.Join(";", r.Mesh.VertexIndices().Select(v => r.Mesh.GetVertex(v).ToString())) + "|" +
             string.Join(";", r.Mesh.TriangleIndices().Select(t => r.Mesh.GetTriangle(t).ToString())));

        [Fact]
        public void Reuse_OneInstanceTwice_EqualsFreshInstances()
        {
            var loft = new ContourLoftStack { CapEnds = true };
            var a = Jitter(Merge(), 1, 0.1); var b = Jitter(BranchCut(), 2, 0.1);
            string fa = Fingerprint(loft.Compute(a)), fb = Fingerprint(loft.Compute(b)), fa2 = Fingerprint(loft.Compute(a));
            Assert.Equal(Fingerprint(new ContourLoftStack { CapEnds = true }.Compute(a)), fa);
            Assert.Equal(Fingerprint(new ContourLoftStack { CapEnds = true }.Compute(b)), fb);
            Assert.Equal(fa, fa2);
        }

        [Fact]
        public void Deterministic_RepeatedAndConcurrentRunsAreIdentical()
        {
            foreach (var (name, slices) in InvarianceCases()) {
                string first = Fingerprint(Loft(slices));
                for (int i = 0; i < 3; ++i) Assert.Equal(first, Fingerprint(Loft(slices)));
                var results = new string[4];
                System.Threading.Tasks.Parallel.For(0, 4, i => results[i] = Fingerprint(Loft(slices)));
                Assert.All(results, x => Assert.Equal(first, x));
            }
        }
    }
}
