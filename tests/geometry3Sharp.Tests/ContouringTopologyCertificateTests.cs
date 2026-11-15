using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The level-set side of the contouring's topology certificate (ContouringTopologyCertificate, Theorem 3 of
    /// docs/design/CONTOURING_TOPOLOGY.md, section 7.4) on a uniform test grid whose pattern is the exact inside / outside
    /// of the nodes, and the exact classification of the level set's components (ContouringLevelSet). Each case is built
    /// to fail exactly one test, so that a broken count or predicate changes its reason.
    /// </summary>
    public class ContouringTopologyCertificateTests
    {
        // ---- procedural closed meshes (outward) ----

        internal static DMesh3 Box(Vector3d center, Vector3d half, Matrix3d rot)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(center + rot * new Vector3d((i & 1) != 0 ? half.x : -half.x, (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z));
            int[][] q = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] f in q) { m.AppendTriangle(f[0], f[1], f[2]); m.AppendTriangle(f[0], f[2], f[3]); }
            return m;
        }

        // torus around the axis rot * z: major radius R, minor r
        internal static DMesh3 Torus(Vector3d center, double R, double r, int nu, int nv, Matrix3d rot)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(center + rot * new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        internal static DMesh3 Sphere(Vector3d center, double r, int n)
        {
            var m = new DMesh3();
            int top = m.AppendVertex(center + new Vector3d(0, 0, r));
            for (int i = 1; i < n; ++i)
                for (int j = 0; j < 2 * n; ++j) {
                    double th = Math.PI * i / n, ph = Math.PI * j / n;
                    m.AppendVertex(center + r * new Vector3d(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th)));
                }
            int bottom = m.AppendVertex(center - new Vector3d(0, 0, r));
            Func<int, int, int> V = (i, j) => 1 + (i - 1) * 2 * n + ((j % (2 * n)) + 2 * n) % (2 * n);
            for (int j = 0; j < 2 * n; ++j) {
                m.AppendTriangle(top, V(1, j), V(1, j + 1));
                m.AppendTriangle(bottom, V(n - 1, j + 1), V(n - 1, j));
            }
            for (int i = 1; i < n - 1; ++i)
                for (int j = 0; j < 2 * n; ++j) {
                    m.AppendTriangle(V(i, j), V(i + 1, j), V(i + 1, j + 1));
                    m.AppendTriangle(V(i, j), V(i + 1, j + 1), V(i, j + 1));
                }
            return m;
        }

        // the boundary of a set of unit voxels (x, y, z in [0, n)), scaled by s and moved to o; outward
        internal static DMesh3 PolyCube(int nx, int ny, int nz, Func<int, int, int, bool> solid, double s, Vector3d o)
        {
            Func<int, int, int, bool> S = (x, y, z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && solid(x, y, z);
            var m = new DMesh3();
            var ids = new Dictionary<Vector3i, int>();
            Func<Vector3i, int> vid = (p) => {
                int id;
                if (!ids.TryGetValue(p, out id)) { id = m.AppendVertex(o + s * new Vector3d(p.x, p.y, p.z)); ids[p] = id; }
                return id;
            };
            for (int x = 0; x < nx; ++x) for (int y = 0; y < ny; ++y) for (int z = 0; z < nz; ++z) {
                if (!S(x, y, z)) continue;
                for (int a = 0; a < 3; ++a)
                    for (int side = 0; side < 2; ++side) {
                        Vector3i nb = new Vector3i(x, y, z); nb[a] += side == 0 ? -1 : 1;
                        if (S(nb.x, nb.y, nb.z)) continue;
                        int u = (a + 1) % 3, v = (a + 2) % 3;
                        Vector3i c = new Vector3i(x, y, z); c[a] += side;
                        Vector3i c1 = c, c2 = c, c3 = c; c1[u] += 1; c2[u] += 1; c2[v] += 1; c3[v] += 1;
                        // outward: (u, v, normal) is right-handed, so (c, c1, c2) faces +a; reverse for -a
                        if (side == 1) { m.AppendTriangle(vid(c), vid(c1), vid(c2)); m.AppendTriangle(vid(c), vid(c2), vid(c3)); }
                        else { m.AppendTriangle(vid(c), vid(c2), vid(c1)); m.AppendTriangle(vid(c), vid(c3), vid(c2)); }
                    }
            }
            return m;
        }

        internal static DMesh3 Union(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 p in parts) new MeshEditor(m).AppendMesh(p);
            return m;
        }

        // the tetrahedron (a, b, c, d), outward if (b - a, c - a, d - a) is right-handed
        internal static DMesh3 Tetrahedron(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            var m = new DMesh3();
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), id = m.AppendVertex(d);
            if (ExactPredicates.Orient3d(a, b, c, d) > 0) { int t = ib; ib = ic; ic = t; }
            m.AppendTriangle(ia, ib, ic); m.AppendTriangle(ia, id, ib); m.AppendTriangle(ib, id, ic); m.AppendTriangle(ic, id, ia);
            return m;
        }

        internal static DMesh3 Reversed(DMesh3 m) { var c = new DMesh3(m); c.ReverseOrientation(false); return c; }

        static readonly Matrix3d Tilt = Matrix3d.AxisAngleD(Vector3d.AxisZ, 23) * Matrix3d.AxisAngleD(Vector3d.AxisX, 11);

        // ---- the test grid: uniform, the pattern from the exact winding number of the mesh at the nodes ----

        internal sealed class TestGrid : CertificateGrid
        {
            public Vector3d Origin; public double H;
            public DMesh3 Mesh; public DMeshAABBTree3 Tree; public AxisAlignedBox3d Root;
            public HashSet<Vector3i> Flipped = new HashSet<Vector3i>();
            readonly System.Collections.Concurrent.ConcurrentDictionary<Vector3i, bool> signs = new System.Collections.Concurrent.ConcurrentDictionary<Vector3i, bool>();
            public override double Coord(int axis, int i) { return Origin[axis] + H * i; }
            public override int BinSize { get { return 1; } }
            public override void CellFaces(Vector3i min, int size, List<GridFace> faces)
            {
                for (int a = 0; a < 3; ++a)
                    for (int s = 0; s < 2; ++s) { Vector3i m = min; m[a] += s * size; faces.Add(new GridFace(m, a, size)); }
            }
            public override void FaceEdges(GridFace f, List<GridSegment> edges)
            {
                for (int k = 0; k < 4; ++k) {
                    Vector3i c0 = f.Corner(k), c1 = f.Corner((k + 1) % 4), d = c1 - c0;
                    int axis = d.x != 0 ? 0 : d.y != 0 ? 1 : 2;
                    edges.Add(new GridSegment(d[axis] > 0 ? c0 : c1, axis, f.Size));
                }
            }
            public override bool NodeInside(Vector3i n)
            {
                bool s;
                if (!signs.TryGetValue(n, out s)) {
                    int w;
                    VolumeSampleCells.ExactWinding(Mesh, Tree, Node(n), Root, out w);
                    s = w > 0;
                    signs[n] = s;
                }
                return s != Flipped.Contains(n);
            }
            public override bool Crossed(GridSegment e) { return NodeInside(e.Start) != NodeInside(e.End); }
        }

        internal static TestGrid Grid(DMesh3 mesh, Vector3d origin, double h)
        {
            return new TestGrid() { Origin = origin, H = h, Mesh = mesh, Tree = new DMeshAABBTree3(mesh, true), Root = mesh.CachedBounds };
        }

        // the reason of every cell the mesh's triangles or the contour touch (cells within the mesh's box, padded)
        internal static Dictionary<Vector3i, ContouringCertificateReason> Certify(DMesh3 mesh, TestGrid grid, out ContouringTopologyCertificate cert)
        {
            DMesh3 clean = ContouringLevelSet.CleanCopy(mesh);
            Assert.NotNull(clean);
            int comps, lcomps;
            bool[] inL = ContouringLevelSet.Classify(clean, out comps, out lcomps);
            Assert.NotNull(inL);
            cert = new ContouringTopologyCertificate(clean, inL, grid);
            var cells = new HashSet<Vector3i>(cert.Bins);
            AxisAlignedBox3d b = mesh.CachedBounds;
            Vector3i lo = Vector3i.Zero, hi = Vector3i.Zero;
            for (int k = 0; k < 3; ++k) { lo[k] = (int)Math.Floor((b.Min[k] - grid.Origin[k]) / grid.H) - 2; hi[k] = (int)Math.Ceiling((b.Max[k] - grid.Origin[k]) / grid.H) + 2; }
            var contour = new HashSet<Vector3i>();
            for (int z = lo.z; z <= hi.z; ++z)
                for (int y = lo.y; y <= hi.y; ++y)
                    for (int x = lo.x; x <= hi.x; ++x)
                        for (int a = 0; a < 3; ++a) {
                            Vector3i n0 = new Vector3i(x, y, z), n1 = n0; n1[a] += 1;
                            if (!grid.Crossed(new GridSegment(n0, a, 1))) continue;
                            for (int q = 0; q < 4; ++q) {
                                Vector3i c = n0; c[(a + 1) % 3] -= (q & 1); c[(a + 2) % 3] -= (q >> 1);
                                contour.Add(c);
                            }
                        }
            cells.UnionWith(contour);
            var result = new Dictionary<Vector3i, ContouringCertificateReason>();
            foreach (Vector3i c in cells.OrderBy(c => c.z).ThenBy(c => c.y).ThenBy(c => c.x)) {
                bool meets;
                result[c] = cert.Cell(c, 1, contour.Contains(c), out meets);
            }
            return result;
        }

        static Dictionary<Vector3i, ContouringCertificateReason> Certify(DMesh3 mesh, TestGrid grid)
        {
            ContouringTopologyCertificate cert;
            return Certify(mesh, grid, out cert);
        }

        static List<ContouringCertificateReason> Refused(Dictionary<Vector3i, ContouringCertificateReason> r)
        {
            return r.Values.Where(x => x != ContouringCertificateReason.None).ToList();
        }

        // a generic grid origin: no grid plane through a vertex of the test meshes
        static readonly Vector3d O = new Vector3d(-1.0137, -1.0271, -1.0389);

        // ---- certified ----

        // a generic offset of the polycubes (no vertex on a grid plane of the grid at O with h = 0.1)
        static readonly Vector3d P0 = new Vector3d(-0.6113, -0.5871, -0.4129);

        [Fact]
        public void Certified_Polycubes_AllCells()
        {
            // faces parallel to the grid planes at a generic offset: every grid edge crosses the surface at most once
            DMesh3 box = PolyCube(4, 3, 2, (x, y, z) => true, 0.25, P0);
            DMesh3 ring = PolyCube(5, 5, 2, (x, y, z) => !(x >= 1 && x <= 3 && y >= 1 && y <= 3), 0.25, P0);
            DMesh3 lshape = PolyCube(5, 5, 3, (x, y, z) => !(x >= 2 && y >= 2), 0.25, P0);
            foreach (DMesh3 m in new[] { box, ring, lshape }) {
                var r = Certify(m, Grid(m, O, 0.1));
                Assert.True(r.Count > 100);
                Assert.Empty(Refused(r));
            }
        }

        [Fact]
        public void Certified_HollowBox_BothShells()
        {
            // the outer shell outward, the inner one inward: both are the level set (w steps 0 -> 1 across each)
            DMesh3 m = Union(PolyCube(6, 6, 6, (x, y, z) => true, 0.25, P0),
                Reversed(PolyCube(4, 4, 4, (x, y, z) => true, 0.25, P0 + new Vector3d(0.25, 0.25, 0.25))));
            var r = Certify(m, Grid(m, O, 0.1));
            Assert.Empty(Refused(r));
            int comps, lcomps;
            ContouringLevelSet.Classify(ContouringLevelSet.CleanCopy(m), out comps, out lcomps);
            Assert.Equal(2, comps);
            Assert.Equal(2, lcomps);
        }

        [Fact]
        public void CurvedSurface_TangentGridEdges_AreRefusedAsEdges()
        {
            // a smooth surface nearly tangent to a grid line crosses it twice within one cell: refused as Edge, and only so
            DMesh3 t = Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.25, 48, 24, Tilt);
            var refused = Refused(Certify(t, Grid(t, O, 0.1)));
            Assert.NotEmpty(refused);
            Assert.DoesNotContain(ContouringCertificateReason.GeneralPosition, refused);
            Assert.DoesNotContain(ContouringCertificateReason.Cell, refused);
            Assert.DoesNotContain(ContouringCertificateReason.Face, refused);
        }

        // ---- each test refuses ----

        [Fact]
        public void GeneralPosition_FaceOnGridPlane_IsRefused()
        {
            // the box's top face lies in a grid plane z = O.z + 12 h
            double h = 0.1, top = O.z + 12 * h;
            DMesh3 box = Box(new Vector3d(0.003, 0.007, top - 0.4), new Vector3d(0.43, 0.37, 0.4), Matrix3d.Identity);
            Assert.Equal(top, box.CachedBounds.Max.z);
            var refused = Refused(Certify(box, Grid(box, O, h)));
            Assert.NotEmpty(refused);
            Assert.All(refused, x => Assert.Equal(ContouringCertificateReason.GeneralPosition, x));
        }

        [Fact]
        public void GeneralPosition_VertexOnGridEdge_IsRefused()
        {
            // one vertex exactly on a grid edge (two coordinates on grid planes, the third between nodes)
            double h = 0.1;
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(0.6, 0.45, 0.35), Tilt);
            Vector3d p = box.GetVertex(0);
            Vector3d q = new Vector3d(O.x + h * Math.Round((p.x - O.x) / h), O.y + h * Math.Round((p.y - O.y) / h), p.z);
            box.SetVertex(0, q);
            var refused = Refused(Certify(box, Grid(box, O, h)));
            Assert.NotEmpty(refused);
            Assert.Contains(ContouringCertificateReason.GeneralPosition, refused);
        }

        [Fact]
        public void GeneralPosition_VertexInsideAFace_IsRefused()
        {
            // the box's lowest corner moved down onto the grid plane z = O.z + 6 h, away from the grid edges: only the face
            // test sees it (its edges have an end on the face)
            double h = 0.1;
            DMesh3 box = PolyCube(4, 3, 2, (x, y, z) => true, 0.25, P0);
            int corner = -1;
            foreach (int v in box.VertexIndices()) if (box.GetVertex(v) == P0) corner = v;
            box.SetVertex(corner, new Vector3d(P0.x, P0.y, O.z + 6 * h));
            Assert.True(box.GetVertex(corner).z < P0.z);
            var r = Certify(box, Grid(box, O, h));
            var refused = r.Where(kv => kv.Value != ContouringCertificateReason.None).ToList();
            Assert.NotEmpty(refused);
            Assert.All(refused, kv => Assert.Equal(ContouringCertificateReason.GeneralPosition, kv.Value));
            // the two cells across the face that holds the vertex
            Assert.Equal(2, refused.Count);
        }

        [Fact]
        public void GeneralPosition_NodeInsideATriangle_IsRefused()
        {
            // a tetrahedron whose face a, b, c (dyadic, in the plane x + y + z = 3) holds the grid node (1, 1, 1) in its
            // interior, no vertex or edge on a grid plane or edge: only the edge test (a node on the triangle) sees it
            Vector3d a = new Vector3d(1.25, 1.25, 0.5), b = new Vector3d(0.5, 1.375, 1.125), c = new Vector3d(1.125, 0.5, 1.375);
            Vector3d d = new Vector3d(0.6875, 0.5625, 0.3125);
            var m = new DMesh3();
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), id = m.AppendVertex(d);
            m.AppendTriangle(ia, ib, ic); m.AppendTriangle(ia, id, ib); m.AppendTriangle(ib, id, ic); m.AppendTriangle(ic, id, ia);
            Assert.Equal(0, ExactPredicates.Orient3d(a, b, c, Vector3d.One));
            var r = Certify(m, Grid(m, Vector3d.Zero, 1));
            var refused = r.Where(kv => kv.Value != ContouringCertificateReason.None).ToList();
            Assert.NotEmpty(refused);
            Assert.All(refused, kv => Assert.Equal(ContouringCertificateReason.GeneralPosition, kv.Value));
            // the cells around the node's edges that the triangle meets: all contain the node
            Assert.All(refused, kv => Assert.True(kv.Key.x <= 1 && kv.Key.x >= 0 && kv.Key.y <= 1 && kv.Key.y >= 0 && kv.Key.z <= 1 && kv.Key.z >= 0));
        }

        [Fact]
        public void Edge_WallThinnerThanACell_IsRefused()
        {
            // a slab 0.03 thick between grid planes z: the z-edges through it cross it twice, both ends outside
            double h = 0.1;
            DMesh3 slab = Box(new Vector3d(0.004, 0.006, O.z + 10.5 * h), new Vector3d(0.5, 0.4, 0.015), Matrix3d.AxisAngleD(Vector3d.AxisZ, 7));
            var refused = Refused(Certify(slab, Grid(slab, O, h)));
            Assert.NotEmpty(refused);
            Assert.Contains(ContouringCertificateReason.Edge, refused);
            Assert.DoesNotContain(ContouringCertificateReason.GeneralPosition, refused);
        }

        [Fact]
        public void Edge_WrongNodeSign_IsRefused()
        {
            // the contour's sign at one node is wrong (the fast winding number's error): its edges do not match
            DMesh3 box = PolyCube(4, 3, 2, (x, y, z) => true, 0.25, P0);
            TestGrid g = Grid(box, O, 0.1);
            Vector3i inside = new Vector3i(10, 10, 10);
            Assert.True(g.NodeInside(inside));
            g.Flipped.Add(inside);
            var r = Certify(box, g);
            var refused = r.Where(kv => kv.Value != ContouringCertificateReason.None).ToList();
            // exactly the 8 cells around the node
            Assert.Equal(8, refused.Count);
            Assert.All(refused, kv => Assert.Equal(ContouringCertificateReason.Edge, kv.Value));
            Assert.All(refused, kv => Assert.True(Math.Abs(kv.Key.x - 9.5) <= 0.5 && Math.Abs(kv.Key.y - 9.5) <= 0.5 && Math.Abs(kv.Key.z - 9.5) <= 0.5));
        }

        [Fact]
        public void Face_RodThroughAFace_IsRefused()
        {
            // a thin rod along z through the middle of a column of faces: on each face a closed curve, no edge crossed
            double h = 0.1;
            Vector3d c = new Vector3d(O.x + 10.5 * h + 0.003, O.y + 10.5 * h - 0.002, 0.004);
            DMesh3 rod = Box(c, new Vector3d(0.02, 0.02, 0.33), Matrix3d.AxisAngleD(Vector3d.AxisZ, 13));
            var r = Certify(rod, Grid(rod, O, h));
            var refused = Refused(r);
            Assert.NotEmpty(refused);
            Assert.Contains(ContouringCertificateReason.Face, refused);
            Assert.DoesNotContain(ContouringCertificateReason.GeneralPosition, refused);
            Assert.DoesNotContain(ContouringCertificateReason.Edge, refused);
        }

        [Fact]
        public void Cell_BubbleInsideACell_IsRefused()
        {
            // a closed sphere inside one cell (Euler characteristic 2), and a torus inside one cell (0)
            double h = 0.2;
            Vector3d c = new Vector3d(O.x + 5.5 * h + 0.001, O.y + 5.5 * h - 0.002, O.z + 5.5 * h + 0.003);
            foreach (DMesh3 m in new[] { Sphere(c, 0.06, 8), Torus(c, 0.05, 0.02, 12, 8, Tilt) }) {
                var r = Certify(m, Grid(m, O, h));
                var refused = r.Where(kv => kv.Value != ContouringCertificateReason.None).ToList();
                Assert.Single(refused);
                Assert.Equal(new Vector3i(5, 5, 5), refused[0].Key);
                Assert.Equal(ContouringCertificateReason.Cell, refused[0].Value);
            }
        }

        [Fact]
        public void Cell_HandleInsideACell_IsRefused()
        {
            // a ball crossing several cells with a torus-shaped handle fused into... (a sphere and a disjoint small torus
            // inside the same cell the sphere passes through: the cell holds a disk and a closed part)
            double h = 0.2;
            Vector3d c = new Vector3d(O.x + 5.5 * h + 0.001, O.y + 5.5 * h - 0.002, O.z + 5.5 * h + 0.003);
            DMesh3 m = Union(Sphere(c + new Vector3d(0.31, 0, 0), 0.3, 24), Torus(c - new Vector3d(0.05, 0, 0), 0.03, 0.01, 12, 8, Tilt));
            var r = Certify(m, Grid(m, O, h));
            Assert.Equal(ContouringCertificateReason.Cell, r[new Vector3i(5, 5, 5)]);
        }

        [Fact]
        public void Pairing_BandAlongTheDiagonal_IsRefused()
        {
            // a long thin box along the diagonal of the face z = O.z + 10 h through the nodes (10, 10) and (11, 11): the
            // face's four edges are crossed once each, but L joins the crossings around the outside corners, the face
            // rule around the inside ones
            double h = 0.1;
            Vector3d c = new Vector3d(O.x + 10.5 * h + 0.0013, O.y + 10.5 * h - 0.0011, O.z + 10 * h + 0.0017);
            DMesh3 band = Box(c, new Vector3d(0.15, 0.02, 0.03), Matrix3d.AxisAngleD(Vector3d.AxisZ, 45.3));
            var r = Certify(band, Grid(band, O, h));
            var refused = Refused(r);
            Assert.Contains(ContouringCertificateReason.Pairing, refused);
            Assert.DoesNotContain(ContouringCertificateReason.GeneralPosition, refused);
        }

        // ---- the predicates on their own ----

        [Fact]
        public void SegmentTriangle_Cases()
        {
            var cert = new ContouringTopologyCertificate(Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity), null, Grid(Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity), O, 1));
            Vector3d a = new Vector3d(0, 0, 0.5), b = new Vector3d(1, 0, 0.5), c = new Vector3d(0, 1, 0.5);
            Func<Vector3d, Vector3d, int, int> st = (A, B, ax) => cert.segment_triangle(A, B, ax, a, b, c);
            // through the interior
            Assert.Equal(1, st(new Vector3d(0.2, 0.2, 0), new Vector3d(0.2, 0.2, 1), 2));
            // ends short of the plane, beyond it
            Assert.Equal(0, st(new Vector3d(0.2, 0.2, 0), new Vector3d(0.2, 0.2, 0.4), 2));
            Assert.Equal(0, st(new Vector3d(0.2, 0.2, 0.6), new Vector3d(0.2, 0.2, 1), 2));
            // an end on the triangle's interior
            Assert.Equal(-1, st(new Vector3d(0.2, 0.2, 0), new Vector3d(0.2, 0.2, 0.5), 2));
            Assert.Equal(-1, st(new Vector3d(0.2, 0.2, 0.5), new Vector3d(0.2, 0.2, 1), 2));
            // through an edge, through a vertex
            Assert.Equal(-1, st(new Vector3d(0.5, 0, 0), new Vector3d(0.5, 0, 1), 2));
            Assert.Equal(-1, st(new Vector3d(0, 0, 0), new Vector3d(0, 0, 1), 2));
            // past the triangle (outside, next to the hypotenuse)
            Assert.Equal(0, st(new Vector3d(0.6, 0.6, 0), new Vector3d(0.6, 0.6, 1), 2));
            // in the triangle's plane, along it
            Assert.Equal(-1, st(new Vector3d(-1, 0.2, 0.5), new Vector3d(2, 0.2, 0.5), 0));
            // in the plane, beside it
            Assert.Equal(0, st(new Vector3d(-1, 1.5, 0.5), new Vector3d(2, 1.5, 0.5), 0));
            // an end on the plane outside the triangle
            Assert.Equal(0, st(new Vector3d(0.9, 0.9, 0), new Vector3d(0.9, 0.9, 0.5), 2));
            // an end on a triangle edge (the projection on the edge: the general test)
            Assert.Equal(-1, st(new Vector3d(0.5, 0, 0.5), new Vector3d(0.5, 0, 1), 2));
            Assert.Equal(-1, st(new Vector3d(0.5, 0, 0), new Vector3d(0.5, 0, 0.5), 2));
            // the projection on the line of an edge, past the triangle
            Assert.Equal(0, st(new Vector3d(1.5, 0, 0), new Vector3d(1.5, 0, 1), 2));
            // a triangle parallel to the axis (projected: no area) -> the general test
            Vector3d d = new Vector3d(0.3, 0, 0), e = new Vector3d(0.3, 1, 0), f = new Vector3d(0.3, 0, 1);
            Assert.Equal(-1, cert.segment_triangle(new Vector3d(0.3, 0.2, 0.1), new Vector3d(0.3, 0.2, 0.9), 2, d, e, f));
            Assert.Equal(0, cert.segment_triangle(new Vector3d(0.4, 0.2, 0.1), new Vector3d(0.4, 0.2, 0.9), 2, d, e, f));
            // the general test across: a slanted segment
            Assert.Equal(1, ContouringTopologyCertificate.segment_triangle_general(new Vector3d(0.1, 0.1, 0), new Vector3d(0.3, 0.2, 1), a, b, c));
            Assert.Equal(0, ContouringTopologyCertificate.segment_triangle_general(new Vector3d(0.6, 0.6, 0), new Vector3d(0.7, 0.6, 1), a, b, c));
            // the general test with an end on the plane: inside the closed triangle (refused), outside it (none)
            Assert.Equal(-1, ContouringTopologyCertificate.segment_triangle_general(new Vector3d(0.25, 0.25, 0.5), new Vector3d(0.3, 0.2, 1), a, b, c));
            Assert.Equal(0, ContouringTopologyCertificate.segment_triangle_general(new Vector3d(0.75, 0.75, 0.5), new Vector3d(0.3, 0.2, 1), a, b, c));
            Assert.Equal(0, ContouringTopologyCertificate.segment_triangle_general(new Vector3d(0.3, 0.2, 0), new Vector3d(-0.25, 0.5, 0.5), a, b, c));
            Assert.Equal(0, ContouringTopologyCertificate.segment_triangle_general(new Vector3d(0.3, 0.2, 0), new Vector3d(0.5, -0.25, 0.5), a, b, c));
        }

        [Fact]
        public void SegmentFace_Cases()
        {
            var cert = new ContouringTopologyCertificate(Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity), null, Grid(Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity), O, 1));
            // the face x = 0, y in [0, 1], z in [0, 1]
            Func<Vector3d, Vector3d, int> sf = (p, q) => cert.segment_face(p, q, 0, 0, 0, 1, 0, 1);
            Assert.Equal(1, sf(new Vector3d(-1, 0.3, 0.4), new Vector3d(1, 0.5, 0.6)));
            Assert.Equal(1, sf(new Vector3d(1, 0.5, 0.6), new Vector3d(-1, 0.3, 0.4)));
            Assert.Equal(0, sf(new Vector3d(-1, 1.3, 0.4), new Vector3d(1, 1.5, 0.6)));
            Assert.Equal(0, sf(new Vector3d(-1, 0.3, -0.4), new Vector3d(1, 0.5, -0.6)));
            Assert.Equal(0, sf(new Vector3d(-1, 0.3, 1.4), new Vector3d(1, 0.5, 1.2)));
            Assert.Equal(0, sf(new Vector3d(-1, -0.3, 0.4), new Vector3d(1, -0.5, 0.6)));
            // through the face's boundary (dyadic coordinates: exactly at y = 1, z = 0, y = 0, z = 1 at x = 0)
            Assert.Equal(-1, sf(new Vector3d(-1, 0.75, 0.375), new Vector3d(1, 1.25, 0.625)));
            Assert.Equal(-1, sf(new Vector3d(-1, 0.375, -0.125), new Vector3d(1, 0.5, 0.125)));
            Assert.Equal(-1, sf(new Vector3d(-1, -0.125, 0.375), new Vector3d(1, 0.125, 0.625)));
            Assert.Equal(-1, sf(new Vector3d(-1, 0.375, 1.125), new Vector3d(1, 0.5, 0.875)));
            // just inside each of them: a crossing
            Assert.Equal(1, sf(new Vector3d(-1, 0.75, 0.375), new Vector3d(1, 1.25 - 1e-9, 0.625)));
            Assert.Equal(1, sf(new Vector3d(-1, 0.375, -0.125), new Vector3d(1, 0.5, 0.125 + 1e-9)));
            Assert.Equal(1, sf(new Vector3d(-1, -0.125, 0.375), new Vector3d(1, 0.125 + 1e-9, 0.625)));
            Assert.Equal(1, sf(new Vector3d(-1, 0.375, 1.125), new Vector3d(1, 0.5, 0.875 - 1e-9)));
            // just outside: none
            Assert.Equal(0, sf(new Vector3d(-1, 0.75, 0.375), new Vector3d(1, 1.25 + 1e-9, 0.625)));
            Assert.Equal(0, sf(new Vector3d(-1, 0.375, -0.125), new Vector3d(1, 0.5, 0.125 - 1e-9)));
            Assert.Equal(0, sf(new Vector3d(-1, -0.125, 0.375), new Vector3d(1, 0.125 - 1e-9, 0.625)));
            Assert.Equal(0, sf(new Vector3d(-1, 0.375, 1.125), new Vector3d(1, 0.5, 0.875 + 1e-9)));
            // an end on the face, an end on the plane outside it, both on one side, in the plane
            Assert.Equal(-1, sf(new Vector3d(0, 0.3, 0.4), new Vector3d(1, 0.5, 0.6)));
            Assert.Equal(-1, sf(new Vector3d(1, 0.5, 0.6), new Vector3d(0, 0.3, 0.4)));
            Assert.Equal(0, sf(new Vector3d(0, 1.3, 0.4), new Vector3d(1, 0.5, 0.6)));
            Assert.Equal(0, sf(new Vector3d(1, 0.5, 0.6), new Vector3d(0, 0.3, 1.4)));
            Assert.Equal(0, sf(new Vector3d(0.5, 0.3, 0.4), new Vector3d(1, 0.5, 0.6)));
            Assert.Equal(-1, sf(new Vector3d(0, -0.3, 0.4), new Vector3d(0, 0.5, 0.6)));
            Assert.Equal(0, sf(new Vector3d(0, 1.3, 0.4), new Vector3d(0, 1.5, 0.6)));
        }

        // x as an exact integer times 2^-1100 (every finite double is one)
        static System.Numerics.BigInteger Fixed(double x)
        {
            if (x == 0) return System.Numerics.BigInteger.Zero;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            var r = new System.Numerics.BigInteger(m) << (e - 1075 + 1100);
            return x < 0 ? -r : r;
        }
        static System.Numerics.BigInteger ExactDot(Vector3d n, Vector3d x)
        {
            return Fixed(n.x) * Fixed(x.x) + Fixed(n.y) * Fixed(x.y) + Fixed(n.z) * Fixed(x.z);
        }

        [Fact]
        public void MayMeetBox_DropsOnlySeparatedTriangles()
        {
            // triangles nearly through a box corner (a third vertex in the plane of two random ones and a corner, up to
            // rounding): may_meet_box may keep a triangle that misses the box, and must drop only those its normal (as
            // computed in double) separates from the box exactly, or whose boxes are apart
            var rnd = new Random(5);
            Vector3d lo = new Vector3d(0, 0, 0), hi = new Vector3d(1, 1, 1);
            int dropped = 0, near = 0;
            for (int i = 0; i < 20000; ++i) {
                Vector3d a = new Vector3d(rnd.NextDouble() * 3 - 1, rnd.NextDouble() * 3 - 1, rnd.NextDouble() * 3 - 1);
                Vector3d b = new Vector3d(rnd.NextDouble() * 3 - 1, rnd.NextDouble() * 3 - 1, rnd.NextDouble() * 3 - 1);
                Vector3d corner = new Vector3d(rnd.Next(2), rnd.Next(2), rnd.Next(2));
                Vector3d c = corner + (rnd.NextDouble() - 0.5) * (a - corner) + (rnd.NextDouble() - 0.5) * (b - corner);
                if (i % 2 == 1) c += 1e-14 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                if (ContouringTopologyCertificate.may_meet_box(a, b, c, lo, hi)) continue;
                dropped++;
                bool boxesApart = false;
                for (int k = 0; k < 3; ++k)
                    boxesApart |= Math.Max(a[k], Math.Max(b[k], c[k])) < lo[k] || Math.Min(a[k], Math.Min(b[k], c[k])) > hi[k];
                if (boxesApart) continue;
                Vector3d n = (b - a).Cross(c - a);
                var ta = ExactDot(n, a); var tb = ExactDot(n, b); var tc = ExactDot(n, c);
                var tmin = System.Numerics.BigInteger.Min(ta, System.Numerics.BigInteger.Min(tb, tc));
                var tmax = System.Numerics.BigInteger.Max(ta, System.Numerics.BigInteger.Max(tb, tc));
                bool allBelow = true, allAbove = true;
                for (int k = 0; k < 8; ++k) {
                    var d = ExactDot(n, new Vector3d(k & 1, (k >> 1) & 1, (k >> 2) & 1));
                    allBelow &= d < tmin;
                    allAbove &= d > tmax;
                }
                Assert.True(allBelow || allAbove);
                if (ExactPredicates.Orient3d(a, b, c, corner) != 0) near++;
            }
            Assert.True(dropped > 1000);
            // a triangle above the box and one below it (the plane separates on either side): dropped
            Assert.False(ContouringTopologyCertificate.may_meet_box(new Vector3d(-1, -1, 1.5), new Vector3d(3, -1, 1.5), new Vector3d(-1, 3, 3.6), lo, hi));
            Assert.False(ContouringTopologyCertificate.may_meet_box(new Vector3d(-1, -1, -1.6), new Vector3d(3, -1, -1.6), new Vector3d(-1, 3, 0.4), lo, hi));
            // touching a box corner: kept
            Assert.True(ContouringTopologyCertificate.may_meet_box(new Vector3d(1, 1, 1), new Vector3d(3, 1, 1.5), new Vector3d(1, 3, 1.5), lo, hi));
        }

        [Fact]
        public void MayMeetBox_TouchingAtExtremeScales_IsKept()
        {
            // exactly touching triangles (at a box corner, along a face), scaled to about 2^500 (the projections overflow
            // while the products do not: an infinite projection must keep the triangle) and to the subnormal range (the
            // normal underflows to 0); a separated triangle is still dropped where the projections are finite
            foreach (double sc in new[] { 1.0, Math.Pow(2, 340), Math.Pow(2, 500), Math.Pow(2, -500), Math.Pow(2, -1040) }) {
                Vector3d lo = Vector3d.Zero, hi = sc * Vector3d.One;
                Func<double, double, double, Vector3d> P = (x, y, z) => sc * new Vector3d(x, y, z);
                // a corner
                Assert.True(ContouringTopologyCertificate.may_meet_box(P(1, 1, 1), P(3, 1, 1.5), P(1, 3, 1.5), lo, hi), "corner at " + sc);
                Assert.True(ContouringTopologyCertificate.may_meet_box(P(1, 1, 1), P(3, 2, 1), P(2, 3, 1), lo, hi), "corner in the top plane at " + sc);
                // the top face, from above, tilted: touching along the edge x = 0.5, z = 1
                Assert.True(ContouringTopologyCertificate.may_meet_box(P(0.5, -1, 1), P(0.5, 3, 1), P(2, 1, 3), lo, hi), "face at " + sc);
            }
            foreach (double sc in new[] { 1.0, Math.Pow(2, 340), Math.Pow(2, -500) }) {
                Vector3d lo = Vector3d.Zero, hi = sc * Vector3d.One;
                Assert.False(ContouringTopologyCertificate.may_meet_box(sc * new Vector3d(-1, -1, 1.5), sc * new Vector3d(3, -1, 1.5), sc * new Vector3d(-1, 3, 3.6), lo, hi), "separated at " + sc);
            }
            // the scales where every product n_k x_k is finite but a three-term projection overflows (the review's case M1):
            // the touching triangle is kept there too
            int overflowing = 0;
            for (double e = 330; e <= 345; e += 0.125) {
                double sc = Math.Pow(2, e);
                Vector3d lo = Vector3d.Zero, hi = sc * Vector3d.One;
                // (in the plane x + y + z = 3, which meets the box only at its corner: a normal of three equal parts, so
                // three equal products add up to an overflow)
                Vector3d a = sc * new Vector3d(1, 1, 1), b = sc * new Vector3d(2, 1, 0), c = sc * new Vector3d(1, 0, 2);
                Vector3d n = (b - a).Cross(c - a);
                bool productsFinite = true, sumInfinite = false;
                foreach (Vector3d x in new[] { a, b, c, lo, hi }) {
                    double t1 = n.x * x.x, t2 = n.y * x.y, t3 = n.z * x.z;
                    productsFinite &= !double.IsInfinity(t1) && !double.IsInfinity(t2) && !double.IsInfinity(t3);
                    sumInfinite |= double.IsInfinity(t1 + t2 + t3);
                }
                if (productsFinite && sumInfinite) overflowing++;
                Assert.True(ContouringTopologyCertificate.may_meet_box(a, b, c, lo, hi), "corner at 2^" + e);
            }
            Assert.True(overflowing > 0, "no scale reaches a finite product with an infinite projection");
        }

        // ---- the level set ----

        [Fact]
        public void LevelSet_NestedOutwardBall_IsNotOnTheLevelSet()
        {
            // a ball inside a ball, both outward: w = 2 inside the inner one, so it is not part of { w > 1/2 }'s boundary
            DMesh3 m = ContouringLevelSet.CleanCopy(Union(Sphere(new Vector3d(0.01, 0.02, 0.03), 0.8, 16), Sphere(new Vector3d(0.01, 0.02, 0.03), 0.4, 12)));
            int comps, lcomps;
            bool[] inL = ContouringLevelSet.Classify(m, out comps, out lcomps);
            Assert.Equal(2, comps);
            Assert.Equal(1, lcomps);
            // the outer sphere's triangles come first
            Assert.True(inL[0]);
            Assert.False(inL[m.MaxTriangleID - 1]);
        }

        [Fact]
        public void LevelSet_InsideOutBall_IsNotOnTheLevelSet()
        {
            // an inside-out ball alone: w = -1 inside, 0 outside; { w > 1/2 } is empty
            DMesh3 m = ContouringLevelSet.CleanCopy(Reversed(Sphere(new Vector3d(0.01, 0.02, 0.03), 0.8, 16)));
            int comps, lcomps;
            ContouringLevelSet.Classify(m, out comps, out lcomps);
            Assert.Equal(1, comps);
            Assert.Equal(0, lcomps);
        }

        [Fact]
        public void LevelSet_TwoDisjointBalls_BothOnTheLevelSet()
        {
            DMesh3 m = ContouringLevelSet.CleanCopy(Union(Sphere(new Vector3d(-1, 0.02, 0.03), 0.5, 12), Torus(new Vector3d(1, 0, 0), 0.4, 0.15, 24, 12, Tilt)));
            int comps, lcomps;
            ContouringLevelSet.Classify(m, out comps, out lcomps);
            Assert.Equal(2, comps);
            Assert.Equal(2, lcomps);
        }

        [Fact]
        public void FrontWinding_IsTheWindingNumberInFrontOfTheVertex()
        {
            // every vertex of a ball inside a ball (both outward): front of the outer one 0, of the inner one 1
            DMesh3 m = ContouringLevelSet.CleanCopy(Union(Sphere(Vector3d.Zero, 0.8, 10), Sphere(new Vector3d(0.01, 0.02, 0.03), 0.4, 8)));
            var tree = new DMeshAABBTree3(m, true);
            int outerVerts = 1 + 9 * 20 + 1;
            foreach (int v in m.VertexIndices()) {
                int front;
                Assert.True(ContouringLevelSet.FrontWinding(m, tree, v, m.CachedBounds, out front));
                Assert.Equal(v < outerVerts ? 0 : 1, front);
            }
        }

        [Fact]
        public void CleanCopy_RejectsBrokenInputs()
        {
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One, Tilt);
            Assert.NotNull(ContouringLevelSet.CleanCopy(box));
            // open
            var open = new DMesh3(box); open.RemoveTriangle(0, false, false);
            Assert.Null(ContouringLevelSet.CleanCopy(open));
            // self-intersecting (two overlapping boxes)
            Assert.Null(ContouringLevelSet.CleanCopy(Union(box, Box(new Vector3d(0.5, 0.1, 0.2), Vector3d.One, Tilt))));
            // inconsistently oriented (one triangle flipped)
            var flipped = new DMesh3(box); flipped.ReverseTriOrientation(3);
            Assert.Null(ContouringLevelSet.CleanCopy(flipped));
            // unwelded: the same box with every triangle's vertices of its own is welded and accepted
            var soup = new DMesh3();
            foreach (int t in box.TriangleIndices()) {
                Index3i tv = box.GetTriangle(t);
                soup.AppendTriangle(soup.AppendVertex(box.GetVertex(tv.a)), soup.AppendVertex(box.GetVertex(tv.b)), soup.AppendVertex(box.GetVertex(tv.c)));
            }
            DMesh3 welded = ContouringLevelSet.CleanCopy(soup);
            Assert.NotNull(welded);
            Assert.Equal(8, welded.VertexCount);
            // a vertex where two parts touch (a bowtie: every edge regular, the vertex not), welded or shared
            DMesh3 tetA = Tetrahedron(Vector3d.Zero, new Vector3d(1, 0.1, 0.2), new Vector3d(0.2, 1, 0.1), new Vector3d(0.1, 0.2, 1));
            DMesh3 tetB = Tetrahedron(Vector3d.Zero, new Vector3d(-1, -0.2, -0.1), new Vector3d(-0.1, -1, -0.2), new Vector3d(-0.2, -0.1, -1));
            DMesh3 touching = Union(tetA, tetB);
            Assert.Equal(8, touching.VertexCount);
            Assert.NotNull(ContouringLevelSet.CleanCopy(tetA));
            Assert.Null(ContouringLevelSet.CleanCopy(touching));
            // a triangle of no area (a vertex moved onto the line of the other two of a triangle): rejected
            var flat = new DMesh3(box);
            Index3i t0 = flat.GetTriangle(0);
            flat.SetVertex(t0.c, 0.5 * (flat.GetVertex(t0.a) + flat.GetVertex(t0.b)));
            Assert.Null(ContouringLevelSet.CleanCopy(flat));
        }

        // ---- invariance, reuse ----

        [Fact]
        public void Invariance_Similarities()
        {
            // the certificate is combinatorial: under a power-of-two scale of mesh and grid the same reason per cell, bit for
            // bit; under translations and other scales the axis-aligned ring stays certified
            DMesh3 t = PolyCube(5, 5, 2, (x, y, z) => !(x >= 1 && x <= 3 && y >= 1 && y <= 3), 0.25, P0);
            DMesh3 slab = Box(new Vector3d(0.004, 0.006, O.z + 10.5 * 0.1), new Vector3d(0.5, 0.4, 0.015), Matrix3d.AxisAngleD(Vector3d.AxisZ, 7));
            var baseT = Certify(t, Grid(t, O, 0.1));
            var baseS = Certify(slab, Grid(slab, O, 0.1));
            Assert.Empty(Refused(baseT));
            Assert.NotEmpty(Refused(baseS));
            foreach (Similarity s in Similarity.All()) {
                DMesh3 tt = new DMesh3(t), ss = new DMesh3(slab);
                MeshTransforms.PerVertexTransform(tt, s.Apply);
                MeshTransforms.PerVertexTransform(ss, s.Apply);
                Vector3d o = s.IsExact ? s.Scale * O : s.Apply(O);
                var rt = Certify(tt, Grid(tt, o, s.ApplyLength(0.1)));
                if (s.Rotation.EpsilonEqual(Matrix3d.Identity, 0))
                    Assert.Empty(Refused(rt));
                else    // (tilted, the ring's edges are wedges a grid edge can cross twice: refused as Edge, nothing else)
                    Assert.All(Refused(rt), x => Assert.Equal(ContouringCertificateReason.Edge, x));
                if (s.IsExact) {
                    Assert.Equal(baseT, rt);
                    Assert.Equal(baseS, Certify(ss, Grid(ss, o, s.ApplyLength(0.1))));
                }
            }
        }

        [Fact]
        public void Cells_ParallelEqualsSerial_Repeated()
        {
            // the parallel path (chunks of 16 cells here: many borders between chunks) gives the serial reasons, bit for
            // bit, in repeated runs; on scenes with refusals of every kind
            double h = 0.1;
            Vector3d c = new Vector3d(O.x + 10.5 * h + 0.003, O.y + 10.5 * h - 0.002, 0.004);
            var scenes = new[] {
                Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.25, 48, 24, Tilt),
                Union(Box(new Vector3d(0.004, 0.006, O.z + 10.5 * h), new Vector3d(0.5, 0.4, 0.015), Matrix3d.AxisAngleD(Vector3d.AxisZ, 7)),
                      Box(c + new Vector3d(0, 0, 0.5), new Vector3d(0.02, 0.02, 0.12), Matrix3d.AxisAngleD(Vector3d.AxisZ, 13))),
                Box(new Vector3d(O.x + 10.5 * h + 0.0013, O.y + 10.5 * h - 0.0011, O.z + 10 * h + 0.0017), new Vector3d(0.15, 0.02, 0.03), Matrix3d.AxisAngleD(Vector3d.AxisZ, 45.3)),
            };
            foreach (DMesh3 m in scenes) {
                ContouringTopologyCertificate cert;
                var serial = Certify(m, Grid(m, O, h), out cert);
                Assert.NotEmpty(Refused(serial));
                var cells = serial.Keys.Select(k => new KeyValuePair<Vector3i, int>(k, 1)).ToList();
                var flags = cells.Select(k => true).ToList();
                for (int rep = 0; rep < 3; ++rep) {
                    DMesh3 clean = ContouringLevelSet.CleanCopy(m);
                    int comps, lcomps;
                    var p = new ContouringTopologyCertificate(clean, ContouringLevelSet.Classify(clean, out comps, out lcomps), Grid(m, O, h));
                    ContouringCertificateReason[] par = p.Cells(cells, flags, true, 16);
                    var q = new ContouringTopologyCertificate(clean, ContouringLevelSet.Classify(clean, out comps, out lcomps), Grid(m, O, h));
                    ContouringCertificateReason[] ser = q.Cells(cells, flags, false);
                    for (int i = 0; i < cells.Count; ++i) {
                        Assert.Equal(ser[i], par[i]);
                        Assert.Equal(serial[cells[i].Key], ser[i]);
                    }
                }
            }
        }

        [Fact]
        public void Cells_TestedTwice_GiveTheSameReasons()
        {
            DMesh3 slab = Box(new Vector3d(0.004, 0.006, O.z + 10.5 * 0.1), new Vector3d(0.5, 0.4, 0.015), Matrix3d.AxisAngleD(Vector3d.AxisZ, 7));
            ContouringTopologyCertificate cert;
            TestGrid g = Grid(slab, O, 0.1);
            var first = Certify(slab, g, out cert);
            foreach (var kv in first) {
                bool meets;
                Assert.Equal(kv.Value, cert.Cell(kv.Key, 1, true, out meets));
            }
        }
    }
}
