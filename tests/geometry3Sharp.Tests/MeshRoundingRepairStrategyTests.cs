using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Each edit of MeshRoundingRepair on its own, on hand-built meshes where only that edit can fix the defect: the
    /// counts, the resulting topology and positions, not only a clean result. Found with the mutation tests
    /// (tests/mutation, stryker-mesh-rounding.json): the tests of the whole repair passed with the flip, the zip or the
    /// move of a cap's apex broken, because another edit or a later pass fixed the mesh as well.
    /// </summary>
    public class MeshRoundingRepairStrategyTests
    {
        static double AreaEps(DMesh3 m)
        {
            double diag = MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
            return 1e-12 * diag * diag;
        }

        static int Degenerate(DMesh3 m)
        {
            double eps = AreaEps(m);
            return m.TriangleIndices().Count(t => m.GetTriArea(t) < eps);
        }

        static void AssertCounts(MeshRoundingRepair r, int collapses, int flips, int moves, int zips)
        {
            Assert.Equal(collapses, r.Collapses);
            Assert.Equal(flips, r.Flips);
            Assert.Equal(moves, r.Moves);
            Assert.Equal(zips, r.Zips);
            Assert.Equal(0, r.RemovedComponents);
        }

        [Fact]
        public void Cap_FlippedAcrossItsLongestEdge()
        {
            // T = (a, b, c) with c on the segment (a, b), U = (b, a, d) across it; all edges far longer than the
            // contraction limit, so the cap is fixed by flipping (a, b): (c, d, b) and (d, c, a)
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(2, 0, 0));
            int c = m.AppendVertex(new Vector3d(1, 0, 0)), d = m.AppendVertex(new Vector3d(1, -1, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, a, d);
            Assert.Equal(1, Degenerate(m));
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            AssertCounts(r, 0, 1, 0, 0);
            Assert.Equal(1, r.InitialDegenerate);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.True(m.FindEdge(a, b) < 0);
            Assert.True(m.FindEdge(c, d) >= 0);
            Assert.Equal(2, m.TriangleCount);
            Assert.Equal(0, Degenerate(m));
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            // the vertices stay where they were
            Assert.Equal(new Vector3d(1, 0, 0), m.GetVertex(c));
        }

        // Two caps on both sides of (a, b), T = (a, b, c) and U = (b, a, d), apexes at 1/4 and 3/4 of the edge (in
        // either order): a flip gives two caps again, so the two sides are zipped together. N1 is the neighbour of T
        // and N2 that of U along the edge, x and y their third corners.
        static DMesh3 TwoCaps(bool cFirst, out int a, out int b, out int c, out int d, out int x, out int y)
        {
            var m = new DMesh3();
            a = m.AppendVertex(new Vector3d(0, 0, 0));
            b = m.AppendVertex(new Vector3d(4, 0, 0));
            c = m.AppendVertex(new Vector3d(cFirst ? 1 : 3, 0, 0));
            d = m.AppendVertex(new Vector3d(cFirst ? 3 : 1, 0, 0));
            x = m.AppendVertex(new Vector3d(2, 1, 0));
            y = m.AppendVertex(new Vector3d(2, -1, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, a, d);
            if (cFirst) {
                // order a, c, d, b: N1 = (c, b, x) across (b, c), N2 = (d, a, y) across (a, d)
                m.AppendTriangle(c, b, x);
                m.AppendTriangle(d, a, y);
            } else {
                // order a, d, c, b: N1 = (a, c, x) across (c, a), N2 = (b, d, y) across (d, b)
                m.AppendTriangle(a, c, x);
                m.AppendTriangle(b, d, y);
            }
            return m;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TwoCaps_Zipped(bool cFirst)
        {
            int a, b, c, d, x, y;
            DMesh3 m = TwoCaps(cFirst, out a, out b, out c, out d, out x, out y);
            Assert.Equal(2, Degenerate(m));
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            AssertCounts(r, 0, 0, 0, 1);
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(0, r.RemainingDegenerate);
            // the caps and their neighbours are replaced by four triangles: (a, b) is gone, the apexes are joined,
            // and x and y are joined to the apex they were not joined to (d and c)
            Assert.Equal(4, m.TriangleCount);
            Assert.True(m.FindEdge(a, b) < 0);
            Assert.True(m.FindEdge(c, d) >= 0);
            Assert.True(m.FindEdge(d, x) >= 0 && m.FindEdge(c, y) >= 0);
            Assert.True(m.FindEdge(c, x) >= 0 && m.FindEdge(d, y) >= 0);
            Assert.Equal(0, Degenerate(m));
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            // all triangles keep the orientation of the input (normal +z), and they cover what N1 and N2 covered
            foreach (int t in m.TriangleIndices())
                Assert.True(m.GetTriNormal(t).z > 0.999, $"triangle {t} turned over");
            Assert.Equal(3.0, m.TriangleIndices().Sum(t => m.GetTriArea(t)), 12);
        }

        // A cap T = (a, b, c) on the boundary edge (a, b) of a fan around its apex c, so that neither a flip nor a
        // zip can fix it: the apex is moved off the edge, by twice the height that gives the degenerate area. The
        // edge does not pass through the origin, and the other neighbour is off-centre, so that the direction of the
        // move depends on every term.
        static DMesh3 FanWithCap(double apexOffset, out int a, out int b, out int c, out int x)
        {
            var m = new DMesh3();
            Vector3d o = new Vector3d(0.5, 3, -2);
            a = m.AppendVertex(o + new Vector3d(0, 0, 0));
            b = m.AppendVertex(o + new Vector3d(2, 0, 0));
            c = m.AppendVertex(o + new Vector3d(1, apexOffset, 0));
            x = m.AppendVertex(o + new Vector3d(1.5, 1, 0.5));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(c, b, x);
            m.AppendTriangle(a, c, x);
            return m;
        }

        [Fact]
        public void Cap_ApexOnTheEdge_MovedTowardsItsNeighbours()
        {
            int a, b, c, x;
            DMesh3 m = FanWithCap(0, out a, out b, out c, out x);
            Assert.False(m.IsBoundaryVertex(c));
            Assert.Equal(1, Degenerate(m));
            Vector3d pa = m.GetVertex(a), pb = m.GetVertex(b), pc = m.GetVertex(c), px = m.GetVertex(x);
            double areaEps = AreaEps(m);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            AssertCounts(r, 0, 0, 1, 0);
            Assert.Equal(pa, m.GetVertex(a));
            Assert.Equal(pb, m.GetVertex(b));
            Assert.Equal(px, m.GetVertex(x));
            // towards the centroid of a, b and x, perpendicular to (a, b), by twice the height h of the degenerate area
            Vector3d dir = (pb - pa).Normalized;
            Vector3d off = (pa + pb + px) / 3 - pc;
            off = (off - off.Dot(dir) * dir).Normalized;
            Vector3d moved = m.GetVertex(c) - pc;
            double h = 2 * areaEps / pa.Distance(pb);
            Assert.Equal(2 * h, moved.Length, 2 * h * 1e-3);
            Assert.True(moved.Normalized.Distance(off) < 1e-6, $"moved along {moved.Normalized}, expected {off}");
            Assert.Equal(0, Degenerate(m));
        }

        [Fact]
        public void Cap_ApexOffTheEdge_MovedAwayFromIt()
        {
            // the apex 1e-13 above the edge (area 1e-13, below the degenerate area of about 5e-12): moved further
            // in the same direction, perpendicular to the edge
            int a, b, c, x;
            DMesh3 m = FanWithCap(1e-13, out a, out b, out c, out x);
            Assert.Equal(1, Degenerate(m));
            Vector3d pa = m.GetVertex(a), pb = m.GetVertex(b), pc = m.GetVertex(c);
            double areaEps = AreaEps(m);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            AssertCounts(r, 0, 0, 1, 0);
            Vector3d q = m.GetVertex(c);
            Assert.Equal(pc.x, q.x);
            Assert.Equal(pc.z, q.z);
            double h = 2 * areaEps / pa.Distance(pb);
            Assert.Equal(pc.y + 2 * h, q.y, 2 * h * 1e-3);
            Assert.Equal(0, Degenerate(m));
        }

        // A closed box of size 1 x 1 x t with a needle (an edge split 1e-12 from its end), far from a unit sphere. Once
        // the needle is contracted the box is an edited component; it is removed if it is thinner than the
        // displacement limit (2 V / A below MaxDisplacement times the diagonal), else kept. open removes one of its
        // triangles (away from the needle): an open component is never removed.
        static DMesh3 SphereAndSlab(double t, bool open, out int sphereTriangles, out double maxLength)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, new Vector3d(3, 0, 0));
            sphereTriangles = m.TriangleCount;
            DMesh3 slab = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, t / 2), new Vector3d(0.5, 0.5, t / 2)) }.Generate().MakeDMesh();
            int eid = slab.EdgeIndices().First(e => {
                Index2i v = slab.GetEdgeV(e);
                return Math.Abs(slab.GetVertex(v.a).z - slab.GetVertex(v.b).z) < 1e-30;
            });
            Index2i ev = slab.GetEdgeV(eid);
            Vector3d pa = slab.GetVertex(ev.a), pb = slab.GetVertex(ev.b);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, slab.SplitEdge(eid, out split));
            slab.SetVertex(split.vNew, pa + 1e-12 * (pb - pa).Normalized);
            if (open) {
                Vector3d pn = slab.GetVertex(split.vNew);
                int far = slab.TriangleIndices().OrderByDescending(tid => slab.GetTriCentroid(tid).Distance(pn)).First();
                slab.RemoveTriangle(far, true, false);
            }
            new MeshEditor(m).AppendMesh(slab);
            maxLength = 1e-5 * MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
            return m;
        }

        [Theory]
        [InlineData(2e-5, false, true)]     // thinner than the limit (about 4.9e-5): removed
        [InlineData(1e-3, false, false)]    // thicker: kept
        [InlineData(2e-5, true, false)]     // thin but open: kept
        public void ThinComponent_RemovedOnlyIfClosedAndThin(double t, bool open, bool removed)
        {
            int n;
            double maxLength;
            DMesh3 m = SphereAndSlab(t, open, out n, out maxLength);
            // thickness 2 V / A of the closed slab: t / (1 + 2 t), against the limit
            Assert.True((t < maxLength) == removed || open);
            int before = m.TriangleCount;
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(1, r.Collapses);
            Assert.Equal(0, r.RemainingDegenerate);
            if (removed) {
                Assert.Equal(1, r.RemovedComponents);
                Assert.Equal(n, m.TriangleCount);
                Assert.Equal(before - 2 - n, r.RemovedTriangles);
                Assert.True(m.IsClosed());
            } else {
                Assert.Equal(0, r.RemovedComponents);
                Assert.Equal(0, r.RemovedTriangles);
                Assert.Equal(before - 2, m.TriangleCount);
            }
        }

        [Fact]
        public void Apply_Twice_CountsOnlyTheSecondRun()
        {
            int a, b, c, d, x, y;
            DMesh3 m = TwoCaps(true, out a, out b, out c, out d, out x, out y);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.Equal(1, r.Zips);
            // nothing left to do: all counts are reset
            Assert.True(r.Apply());
            AssertCounts(r, 0, 0, 0, 0);
            Assert.Equal(0, r.InitialDegenerate);
            Assert.Equal(4, m.TriangleCount);
        }
    }
}
