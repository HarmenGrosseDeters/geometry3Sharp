using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The mesh side of the contouring's topology certificate (Theorem 2 of docs/design/CONTOURING_TOPOLOGY.md, section
    /// 7.3): what DualContouring records with RecordCertificateData (H1-H4 and option B's facts (i)-(iii)), and its grid
    /// for the level-set side (ContourGrid: the two-level grid, the pattern, the face rule).
    /// </summary>
    public class DualContouringCertificateTests
    {
        class Fn : ImplicitFunction3d
        {
            public Func<Vector3d, double> F;
            public double Value(ref Vector3d p) { return F(p); }
        }

        static double Box(Vector3d p, Vector3d hs)
        {
            Vector3d q = new Vector3d(Math.Abs(p.x) - hs.x, Math.Abs(p.y) - hs.y, Math.Abs(p.z) - hs.z);
            Vector3d m = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return m.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        }

        // two turned plates 0.03 apart (as DualContouringFaceVertexTests): many ambiguous faces between single-patch cells
        static Func<Vector3d, double> Plates(int seed)
        {
            var rnd = new Random(seed);
            Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisY, rnd.NextDouble() * 360)
                * Matrix3d.AxisAngleD(Vector3d.AxisZ, rnd.NextDouble() * 360);
            return p => {
                Vector3d q = R * p;
                return Math.Min(Box(q - new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025)), Box(q + new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025)));
            };
        }

        static DualContouring Contour(Func<Vector3d, double> f, int res, bool record = true)
        {
            double cell = 2.2 / res;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * new Vector3d(0.1371, 0.2713, 0.3891);
            var dc = new DualContouring() {
                Implicit = new Fn() { F = f }, InsideIsLess = true, CellSize = cell, Bounds = new AxisAlignedBox3d(o, o + 2.2 * Vector3d.One),
                RecordCertificateData = record
            };
            dc.Generate();
            return dc;
        }

        static DualContouring Transitions(bool record = true)
        {
            return LocalRefinementTests.ContourMixed(q => q.Length - 1, 0.15, 1.3, 2, LocalRefinementTests.RandomRegion(1, 0.3), record);
        }

        // Option B's facts (section 7.3), asserted on the contour itself:
        //   (i)   no polygon crosses a face that has face vertices along a straight side: no mesh edge between two cell
        //         vertices crosses the (fine) face around a face vertex;
        //   (ii)  at a transition every polygon is a fan: no mesh edge joins the vertices of two cells of different size;
        //   (iii) a face vertex belongs to one segment: it is in exactly the two fans of its segment's edges (4 triangles,
        //         2 edge vertices and 2 cell vertices around it).
        static void AssertOptionBFacts(DualContouring dc)
        {
            DMesh3 m = dc.Mesh;
            CertificateGrid g = dc.ContourGrid();
            double hf = g.Coord(0, 1) - g.Coord(0, 0);
            Func<int, bool> cellVertex = v => dc.genPatches[v] >= 1;
            // the grid planes a point lies on (fine node index per axis, or int.MinValue)
            Func<Vector3d, int, int> plane = (p, k) => {
                int i = (int)Math.Round((p[k] - g.Coord(k, 0)) / hf);
                return g.Coord(k, i) == p[k] ? i : int.MinValue;
            };
            int checkedFaces = 0;
            foreach (int fv in dc.faceVertexIds) {
                if (!m.IsVertex(fv)) continue;
                // (iii)
                Assert.Equal(4, m.VtxTrianglesItr(fv).Count());
                var nbrs = m.VtxVerticesItr(fv).ToList();
                Assert.Equal(4, nbrs.Count);
                Assert.Equal(2, nbrs.Count(v => dc.edgeVertexIds.Contains(v)));
                Assert.Equal(2, nbrs.Count(v => cellVertex(v)));
                // (i): the fine face around it
                Vector3d p = m.GetVertex(fv);
                int k = -1;
                for (int a = 0; a < 3; ++a) if (plane(p, a) != int.MinValue) { Assert.Equal(-1, k); k = a; }
                Assert.True(k >= 0);
                int u = (k + 1) % 3, w = (k + 2) % 3;
                int iu = (int)Math.Floor((p[u] - g.Coord(u, 0)) / hf), iw = (int)Math.Floor((p[w] - g.Coord(w, 0)) / hf);
                double P = p[k], u0 = g.Coord(u, iu), u1 = g.Coord(u, iu + 1), w0 = g.Coord(w, iw), w1 = g.Coord(w, iw + 1);
                foreach (int eid in m.EdgeIndices()) {
                    Index2i ev = m.GetEdgeV(eid);
                    if (!cellVertex(ev.a) || !cellVertex(ev.b)) continue;
                    Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                    if ((a[k] - P) * (b[k] - P) >= 0) continue;
                    double t = (P - a[k]) / (b[k] - a[k]);
                    Vector3d x = a + t * (b - a);
                    Assert.False(x[u] > u0 && x[u] < u1 && x[w] > w0 && x[w] < w1, "a dual side crosses a face with a face vertex");
                }
                checkedFaces++;
            }
            Assert.True(checkedFaces > 0);
            // (ii)
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                if (cellVertex(ev.a) && cellVertex(ev.b))
                    Assert.Equal(dc.VertexCellSize(ev.a), dc.VertexCellSize(ev.b));
            }
        }

        [Fact]
        public void OptionB_Facts_HoldOnTheContours()
        {
            AssertOptionBFacts(Contour(Plates(4), 48));
            AssertOptionBFacts(Transitions());
        }

        [Fact]
        public void MeshSide_Sphere_NothingRefused()
        {
            DualContouring dc = Contour(p => p.Length - 0.7, 32);
            Assert.True(dc.ContourCells().Count > 100);
            Assert.False(dc.CertificateMeshFails);
            Assert.Empty(dc.CertificateMeshCells);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(9)]
        public void MeshSide_FaceVertices_FactsHold(int seed)
        {
            // ambiguous faces between single-patch cells: fans through face vertices, the facts (i)-(iii) hold
            DualContouring dc = Contour(Plates(seed), 48);
            Assert.True(dc.FaceVertices > 0);
            Assert.False(dc.CertificateMeshFails);
            Assert.Empty(dc.CertificateMeshCells);
        }

        [Fact]
        public void MeshSide_Transitions_FactsHold()
        {
            // refinement transitions: every polygon there is a fan with a face vertex on every face between cells of two sizes
            DualContouring dc = Transitions();
            Assert.True(dc.FaceVertices > 0);
            Assert.True(dc.ContourCells().Any(c => c.Value > 1) && dc.ContourCells().Any(c => c.Value == 1));
            Assert.False(dc.CertificateMeshFails);
            Assert.Empty(dc.CertificateMeshCells);
        }

        [Fact]
        public void MeshSide_FailedFaceVertices_AreRefused()
        {
            // a face vertex that cannot be placed (forced here): the polygons that needed it are not covered by the proof
            // (on the plates the polygons that needed one become dual quads, at the transitions fans with a dual side)
            for (int run = 0; run < 2; ++run) {
                int calls = 0;
                DualContouring.FailFaceVertexForTests = () => (calls++ % 3) == 0;
                DualContouring dc;
                try { dc = run == 0 ? Contour(Plates(4), 48) : Transitions(); } finally { DualContouring.FailFaceVertexForTests = null; }
                Assert.True(calls > 0);
                Assert.NotEmpty(dc.CertificateMeshCells);
            }
            // every face vertex failing at the transitions: the polygons there are no fans at all (fact (ii))
            DualContouring.FailFaceVertexForTests = () => true;
            DualContouring t;
            try { t = Transitions(); } finally { DualContouring.FailFaceVertexForTests = null; }
            Assert.Equal(0, t.FaceVertices);
            Assert.NotEmpty(t.CertificateMeshCells);
            Assert.Contains(t.CertificateMeshCells, c => c.Value > 1);
        }

        [Fact]
        public void MeshSide_PolygonsLeftOutAtTheBounds_AreRefused()
        {
            // a sphere larger than the grid's box: the polygons of the edges at the border have no cells on one side
            DualContouring dc = Contour(p => p.Length - 1.2, 24);
            Assert.NotEmpty(dc.CertificateMeshCells);
        }

        [Fact]
        public void MeshSide_VertexOnItsCellBoundary_FailsTheContour()
        {
            DualContouring dc = Contour(p => p.Length - 0.7, 32);
            Assert.False(dc.CertificateMeshFails);
            int v = dc.Mesh.VertexIndices().First(x => dc.genPatches[x] >= 1);
            Vector3d p = dc.Mesh.GetVertex(v);
            double h = 2.2 / 32, o = -1.1 + h * 0.1371;
            p.x = o + h * Math.Floor((p.x - o) / h);     // onto the cell's lower x face
            dc.Mesh.SetVertex(v, p);
            dc.CheckCertificateMeshSideForTests();
            Assert.Equal(DualContouring.MeshFailures.VertexPlacement, dc.CertificateMeshFailures);
        }

        [Fact]
        public void MeshSide_TriangleOfNoArea_FailsTheContour()
        {
            // a cell vertex moved onto the line of its triangle's other two vertices (wherever that is: no area)
            DualContouring dc = Contour(p => p.Length - 0.7, 32);
            int t = dc.Mesh.TriangleIndices().First();
            Index3i tv = dc.Mesh.GetTriangle(t);
            Vector3d a = dc.Mesh.GetVertex(tv.a), b = dc.Mesh.GetVertex(tv.b);
            dc.Mesh.SetVertex(tv.c, a + 2 * (b - a));
            dc.CheckCertificateMeshSideForTests();
            Assert.True((dc.CertificateMeshFailures & DualContouring.MeshFailures.NoArea) != 0);
        }

        [Fact]
        public void MeshSide_UncheckedOrLeftOut_FailsTheContour()
        {
            // the self-intersections not checked (ResolveSelfIntersections off): not known to be free of them
            double cell = 2.2 / 32;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * new Vector3d(0.1371, 0.2713, 0.3891);
            var dc = new DualContouring() {
                Implicit = new Fn() { F = p => p.Length - 0.7 }, InsideIsLess = true, CellSize = cell, Bounds = new AxisAlignedBox3d(o, o + 2.2 * Vector3d.One),
                RecordCertificateData = true, ResolveSelfIntersections = false
            };
            dc.Generate();
            Assert.Equal(DualContouring.MeshFailures.SelfIntersections, dc.CertificateMeshFailures);
            // every face vertex failing on the plates: polygons left out (non-manifold edges)
            DualContouring.FailFaceVertexForTests = () => true;
            DualContouring d2;
            try { d2 = Contour(Plates(4), 48); } finally { DualContouring.FailFaceVertexForTests = null; }
            Assert.True(d2.NonManifoldTriangles > 0);
            Assert.True((d2.CertificateMeshFailures & DualContouring.MeshFailures.LeftOut) != 0);
        }

        [Fact]
        public void MeshSide_EdgeOrFaceVertexOffItsPlace_IsRefused()
        {
            DualContouring dc = Contour(Plates(4), 48);
            Assert.True(dc.edgeVertexIds.Count > 0 && dc.faceVertexIds.Count > 0);
            // an edge vertex off its grid edge: the whole contour
            int ev = dc.edgeVertexIds.First(x => dc.Mesh.IsVertex(x));
            Vector3d pe = dc.Mesh.GetVertex(ev);
            dc.Mesh.SetVertex(ev, pe + new Vector3d(1e-6, 1e-6, 1e-6));
            dc.CheckCertificateMeshSideForTests();
            Assert.Equal(DualContouring.MeshFailures.VertexPlacement, dc.CertificateMeshFailures);
            dc.Mesh.SetVertex(ev, pe);
            dc.CheckCertificateMeshSideForTests();
            Assert.False(dc.CertificateMeshFails);
            Assert.Empty(dc.CertificateMeshCells);
            // a face vertex off its face: the cells around the face's edges
            int fv = dc.faceVertexIds.First(x => dc.Mesh.IsVertex(x));
            Vector3d pf = dc.Mesh.GetVertex(fv);
            dc.Mesh.SetVertex(fv, pf + new Vector3d(1e-6, 1e-6, 1e-6));
            dc.CheckCertificateMeshSideForTests();
            Assert.False(dc.CertificateMeshFails);
            Assert.InRange(dc.CertificateMeshCells.Count, 2, 12);
        }

        [Fact]
        public void ContourGrid_PatternIsTheCrossings()
        {
            // every crossed grid edge is an edge of a face of a contour cell, and the grid's pattern there is exactly the
            // crossings (uniform and two-level grids)
            foreach (DualContouring dc in new[] { Contour(Plates(2), 48), Transitions() }) {
                CertificateGrid g = dc.ContourGrid();
                var crossed = new HashSet<GridSegment>();
                var faces = new List<GridFace>();
                var edges = new List<GridSegment>();
                foreach (var c in dc.ContourCells()) {
                    faces.Clear();
                    g.CellFaces(c.Key, c.Value, faces);
                    Assert.True(faces.Count == 6 || (c.Value > 1 && faces.Count > 6));
                    foreach (GridFace f in faces) {
                        edges.Clear();
                        g.FaceEdges(f, edges);
                        Assert.True(edges.Count >= 4);
                        foreach (GridSegment e in edges) if (g.Crossed(e)) crossed.Add(e);
                    }
                }
                Assert.Equal(dc.CrossingCount, crossed.Count);
            }
            // a coarse cell next to a refined one: its side there is the fine faces; the node signs are the contour's
            DualContouring t = Transitions();
            CertificateGrid tg = t.ContourGrid();
            var fs = new List<GridFace>();
            int split = 0;
            foreach (var c in t.ContourCells().Where(c => c.Value > 1)) {
                fs.Clear();
                tg.CellFaces(c.Key, c.Value, fs);
                // (factor 2: a side next to a refined cell is 4 faces, 3 more)
                if (fs.Count > 6) { split++; Assert.Equal(0, (fs.Count - 6) % 3); Assert.All(fs, f => Assert.True(f.Size == 1 || f.Size == 2)); }
            }
            Assert.True(split > 0);
            Assert.True(tg.NodeInside(new Vector3i(17, 17, 17)));     // the centre of the sphere (cell 0.15 / 2 from -1.3)
            Assert.False(tg.NodeInside(new Vector3i(0, 0, 0)));
        }

        [Fact]
        public void Record_ChangesNothing()
        {
            // the recording does not change the contour (bit for bit)
            foreach (bool transitions in new[] { false, true }) {
                DualContouring a = transitions ? Transitions(false) : Contour(Plates(9), 48, false);
                DualContouring b = transitions ? Transitions(true) : Contour(Plates(9), 48, true);
                Assert.Equal(a.Mesh.VertexCount, b.Mesh.VertexCount);
                Assert.Equal(a.Mesh.TriangleCount, b.Mesh.TriangleCount);
                foreach (int v in a.Mesh.VertexIndices()) Assert.Equal(a.Mesh.GetVertex(v), b.Mesh.GetVertex(v));
                foreach (int t in a.Mesh.TriangleIndices()) Assert.Equal(a.Mesh.GetTriangle(t), b.Mesh.GetTriangle(t));
            }
        }
    }
}
