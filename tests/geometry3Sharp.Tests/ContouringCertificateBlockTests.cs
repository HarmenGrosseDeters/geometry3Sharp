using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using static geometry3Sharp.Tests.ContouringTopologyCertificateTests;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The block test of the contouring's topology certificate (Theorem 6 of docs/design/CONTOURING_TOPOLOGY.md, section
    /// 7.10): uncertified cells merged into blocks, certified where the level set and the contour both meet a block in
    /// disjoint disks. The two counts and the ball test on their own, then end to end through MeshWindingNumberSolidify.
    /// </summary>
    public class ContouringCertificateBlockTests
    {
        static readonly Matrix3d Tilt = Matrix3d.AxisAngleD(Vector3d.AxisZ, 23) * Matrix3d.AxisAngleD(Vector3d.AxisX, 11);
        static readonly Vector3d P0 = new Vector3d(-0.6113, -0.5871, -0.4129);
        // a generic grid origin (no grid plane through a vertex of the test meshes)
        static readonly Vector3d O = new Vector3d(-1.0137, -1.0271, -1.0389);

        // ---- the level-set side (Lemma 6.3) ----

        static ContouringTopologyCertificate LevelSet(DMesh3 m, double h)
        {
            DMesh3 clean = ContouringLevelSet.CleanCopy(m);
            Assert.NotNull(clean);
            int comps, lcomps;
            bool[] inL = ContouringLevelSet.Classify(clean, out comps, out lcomps);
            return new ContouringTopologyCertificate(clean, inL, Grid(clean, O, h));
        }

        // the cells (min nodes, size 1) of the box of cells lo .. hi around the point p of a grid with spacing h at O
        static List<Vector3i> BlockAround(Vector3d p, double h, int lo, int hi)
        {
            Vector3i c = new Vector3i((int)Math.Floor((p.x - O.x) / h), (int)Math.Floor((p.y - O.y) / h), (int)Math.Floor((p.z - O.z) / h));
            var cells = new List<Vector3i>();
            for (int z = lo; z <= hi; ++z) for (int y = lo; y <= hi; ++y) for (int x = lo; x <= hi; ++x) cells.Add(c + new Vector3i(x, y, z));
            return cells;
        }

        [Fact]
        public void LevelSet_DiskThroughTheBlock_Passes()
        {
            // a face of a large tilted box crosses the 3 x 3 x 3 block around a point in the face's middle: one disk
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(0.6, 0.5, 0.4), Tilt);
            Vector3d p = Tilt * new Vector3d(0.01, 0.02, 0.4);
            Assert.True(LevelSet(box, 0.05).Block(BlockAround(p, 0.05, -1, 1), 1));
        }

        [Fact]
        public void LevelSet_ClosedPartInsideTheBlock_Fails()
        {
            // a small ball inside the block: a component that does not reach the block's boundary (chi 2)
            Vector3d c = new Vector3d(0.0123, 0.0217, 0.0311);
            DMesh3 ball = Sphere(c, 0.02, 8);
            Assert.False(LevelSet(ball, 0.05).Block(BlockAround(c, 0.05, -1, 1), 1));
            // a larger block around it fails the same way; the disk of a face next to it is still fine alone
            Assert.False(LevelSet(ball, 0.05).Block(BlockAround(c, 0.05, -2, 1), 1));
        }

        [Fact]
        public void LevelSet_TubeThroughTheBlock_Fails()
        {
            // the tube of a thin torus runs through the block: an annulus (one component, chi 0), not a disk
            DMesh3 t = Torus(Vector3d.Zero, 0.6, 0.03, 96, 12, Tilt);
            Vector3d p = Tilt * new Vector3d(0.6, 0, 0);
            Assert.False(LevelSet(t, 0.05).Block(BlockAround(p, 0.05, -1, 1), 1));
            // two tubes side by side cross it as two annuli: also not disks (the count of components alone does not decide)
            DMesh3 two = Union(t, Torus(new Vector3d(0, 0, 0.0817), 0.6, 0.03, 96, 12, Tilt));
            Assert.False(LevelSet(two, 0.05).Block(BlockAround(p, 0.05, -1, 1), 1));
        }

        [Fact]
        public void LevelSet_DegeneracyInsideTheBlock_Fails()
        {
            // a face of an axis-aligned box in a grid plane inside the block: the first version refuses the block
            double h = 0.05, z = O.z + 21 * h;
            DMesh3 box = PolyCube(4, 4, 1, (x, y, zz) => true, 0.2, new Vector3d(-0.4013, -0.3987, z - 0.2));
            Vector3d p = new Vector3d(0.0117, 0.0123, z);
            Assert.False(LevelSet(box, h).Block(BlockAround(p, h, -1, 1), 1));
        }

        [Fact]
        public void LevelSet_ClosedPartBesideAnAnnulus_Fails()
        {
            // an annulus (chi 0) and a small ball (chi 2) in one block: chi equals the number of components (2), so only the
            // rule that every component reaches the block's boundary refuses it
            DMesh3 t = Torus(Vector3d.Zero, 0.6, 0.03, 96, 12, Tilt);
            Vector3d p = Tilt * new Vector3d(0.6, 0, 0);
            DMesh3 m = Union(t, Sphere(p + Tilt * new Vector3d(0, 0, 0.055), 0.01, 8));
            var cells = BlockAround(p, 0.05, -2, 2);
            Assert.False(LevelSet(m, 0.05).Block(cells, 1));
        }

        [Fact]
        public void LevelSet_VertexOnAnInsideFace_Fails()
        {
            // a corner of a tetrahedron exactly on a grid plane, inside a face between two cells of the block: (G) fails
            // inside the block, which the first version refuses (the same corner off the plane: a disk, certified)
            double h = 0.05;
            Func<double, double, double, Vector3d> node = (i, j, k) => new Vector3d(O.x + h * i, O.y + h * j, O.z + h * k);
            foreach (bool onPlane in new[] { true, false }) {
                Vector3d a = new Vector3d(O.x + h * 20 + (onPlane ? 0 : 0.0013), O.y + h * 20.37, O.z + h * 20.41);
                DMesh3 tet = Tetrahedron(a, a + new Vector3d(-0.61, 0.27, 0.13), a + new Vector3d(-0.53, -0.31, 0.21), a + new Vector3d(-0.57, 0.03, -0.37));
                DMesh3 clean = ContouringLevelSet.CleanCopy(tet) ?? ContouringLevelSet.CleanCopy(Reversed(tet));
                Assert.NotNull(clean);
                int comps, lcomps;
                bool[] inL = ContouringLevelSet.Classify(clean, out comps, out lcomps);
                var cert = new ContouringTopologyCertificate(clean, inL, Grid(clean, O, h));
                var cells = new List<Vector3i>();
                for (int z = 19; z <= 21; ++z) for (int y = 19; y <= 21; ++y) for (int x = 19; x <= 20; ++x) cells.Add(new Vector3i(x, y, z));
                Assert.Equal(!onPlane, cert.Block(cells, 1));
            }
        }

        [Fact]
        public void LevelSet_SurfaceThroughAVertexOnAnInsideFace_Fails()
        {
            // an octahedron's equator vertex exactly on a grid plane, inside a face between two cells of the block, with its
            // star on both sides of the plane (the surface passes through the plane at the vertex): (G) fails inside the
            // block; off the plane by a little, the block is a disk and passes
            double h = 0.05;
            foreach (bool onPlane in new[] { true, false }) {
                Vector3d v0 = new Vector3d(O.x + h * 20 + (onPlane ? 0 : 0.0007), O.y + h * 20.37, O.z + h * 20.41);
                Vector3d q = v0 - new Vector3d(0, 0.2, 0.013);
                var m = new DMesh3();
                int N = m.AppendVertex(q + new Vector3d(0.3, 0.011, 0.007)), Sp = m.AppendVertex(q - new Vector3d(0.3, 0.017, 0.005));
                int[] e = { m.AppendVertex(v0), m.AppendVertex(q + new Vector3d(0.0013, 0.007, 0.2)),
                            m.AppendVertex(q + new Vector3d(0.0029, -0.2, 0.011)), m.AppendVertex(q + new Vector3d(0.0039, 0.013, -0.2)) };
                for (int i = 0; i < 4; ++i) { m.AppendTriangle(N, e[i], e[(i + 1) % 4]); m.AppendTriangle(Sp, e[(i + 1) % 4], e[i]); }
                DMesh3 clean = ContouringLevelSet.CleanCopy(m) ?? ContouringLevelSet.CleanCopy(Reversed(m));
                Assert.NotNull(clean);
                int comps, lcomps;
                bool[] inL = ContouringLevelSet.Classify(clean, out comps, out lcomps);
                if (lcomps == 0) { clean = ContouringLevelSet.CleanCopy(Reversed(clean)); inL = ContouringLevelSet.Classify(clean, out comps, out lcomps); }
                Assert.Equal(1, lcomps);
                var cert = new ContouringTopologyCertificate(clean, inL, Grid(clean, O, h));
                var cells = new List<Vector3i>();
                for (int z = 19; z <= 21; ++z) for (int y = 19; y <= 21; ++y) for (int x = 19; x <= 20; ++x) cells.Add(new Vector3i(x, y, z));
                Assert.Equal(!onPlane, cert.Block(cells, 1));
            }
        }

        [Fact]
        public void LevelSet_EdgeInAnInsideFacePlane_Fails()
        {
            // a mesh edge (v0, v1) in the plane x = O.x + 20 h, over the face between the cells 19 and 20 of the block (#1016,
            // M16): segment_face answers Degenerate for it in block_face. Without that, the edge is in no T(f) or M(f), the
            // counts of the block miss the triangles that meet the face only along it, and the block is certified. The case
            // was found by a search over tetrahedra with an edge in a grid plane (about one in ten of them certified with the guard removed)
            double h = 0.05, X = O.x + 20 * h;
            Vector3d v0 = new Vector3d(X, 0.028634054001855924, 0.03973650663226702), v1 = new Vector3d(X, 0.2516285005109054, -0.0596758542247002);
            Vector3d v2 = new Vector3d(-0.2911530609033318, 0.09456837901886034, 0.07353282138017625), v3 = new Vector3d(0.2692313150993228, -0.02399654130344095, 0.044168626878349604);
            DMesh3 tet = Tetrahedron(v0, v1, v2, v3);
            DMesh3 clean = ContouringLevelSet.CleanCopy(tet) ?? ContouringLevelSet.CleanCopy(Reversed(tet));
            Assert.NotNull(clean);
            int comps, lcomps;
            bool[] inL = ContouringLevelSet.Classify(clean, out comps, out lcomps);
            var cert = new ContouringTopologyCertificate(clean, inL, Grid(clean, O, h));
            var cells = new List<Vector3i>();
            for (int z = 19; z <= 20; ++z) for (int y = 20; y <= 21; ++y) for (int x = 18; x <= 20; ++x) cells.Add(new Vector3i(x, y, z));
            Assert.False(cert.Block(cells, 1));
        }

        // ---- the contour side (Lemma 6.4), on node signs ----

        sealed class SignGrid : CertificateGrid
        {
            public Func<Vector3i, bool> Inside;
            public Func<GridSegment, bool> CrossedF;
            public override double Coord(int axis, int i) { return i; }
            public override int BinSize { get { return 1; } }
            public override void CellFaces(Vector3i min, int size, List<GridFace> faces) { throw new NotSupportedException(); }
            public override void FaceEdges(GridFace f, List<GridSegment> edges) { throw new NotSupportedException(); }
            public override bool NodeInside(Vector3i n) { return Inside(n); }
            public override bool Crossed(GridSegment e) { return CrossedF != null ? CrossedF(e) : Inside(e.Start) != Inside(e.End); }
        }

        static List<Vector3i> Cells(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            var c = new List<Vector3i>();
            for (int z = z0; z <= z1; ++z) for (int y = y0; y <= y1; ++y) for (int x = x0; x <= x1; ++x) c.Add(new Vector3i(x, y, z));
            return c;
        }

        [Fact]
        public void Contour_Plane_Passes_SphereAndTube_Fail()
        {
            // a plane through the block: one disk
            Assert.True(ContouringTopologyCertificate.ContourBlock(new SignGrid() { Inside = n => n.z <= 0 }, Cells(-1, 0, -1, 0, -1, 0), 1));
            // a tilted plane over a larger block, with ambiguous faces (the face rule joins the outside corners)
            Assert.True(ContouringTopologyCertificate.ContourBlock(new SignGrid() { Inside = n => n.x + 2 * n.y + 3 * n.z <= 1 }, Cells(-2, 2, -2, 2, -2, 2), 1));
            // one inside node in the middle: a small sphere that does not reach the block's boundary
            Assert.False(ContouringTopologyCertificate.ContourBlock(new SignGrid() { Inside = n => n.x == 0 && n.y == 0 && n.z == 0 }, Cells(-1, 0, -1, 0, -1, 0), 1));
            // a line of inside nodes along x through the block: a tube (an annulus)
            Assert.False(ContouringTopologyCertificate.ContourBlock(new SignGrid() { Inside = n => n.y == 0 && n.z == 0 }, Cells(-1, 1, -1, 0, -1, 0), 1));
            // the same block around one end of a half line of inside nodes: a disk again (a cap)
            Assert.True(ContouringTopologyCertificate.ContourBlock(new SignGrid() { Inside = n => n.y == 0 && n.z == 0 && n.x >= 0 }, Cells(-1, 0, -1, 0, -1, 0), 1));
        }

        [Fact]
        public void Contour_SphereBesideATube_Fails()
        {
            // a tube along x (chi 0) and a small sphere around one inside node (chi 2) in one block: chi equals the number of
            // components, so only the rule that every component reaches the boundary refuses it
            Func<Vector3i, bool> inside = n => (n.y == 0 && n.z == 0) || (n.x == 0 && n.y == 2 && n.z == 0);
            Assert.False(ContouringTopologyCertificate.ContourBlock(new SignGrid() { Inside = inside }, Cells(-1, 0, -1, 2, -1, 0), 1));
        }

        [Fact]
        public void Contour_Patches_MatchTheContoursCellVertices()
        {
            // the patches the block test reads from the pattern are the cell vertices DualContouring makes, cell by cell
            // (an oracle for the face rule: ambiguous faces must be among the cells compared)
            WindingContouring.CheckPatchesForTests = true;
            WindingContouring.PatchCellsChecked = WindingContouring.AmbiguousCellsChecked = WindingContouring.CoarsePatchCellsChecked = 0;
            try {
                const double cell = 0.06;
                Vector3d c = new Vector3d(0.0123, 0.0217, 0.0311);
                // thin tubes (diagonal staircases of inside nodes: ambiguous faces) and a gap
                foreach (DMesh3 m in new[] { Torus(c, 0.6, 0.4 * cell, 96, 8, Tilt), Torus(c, 0.6, 0.55 * cell, 96, 8, Tilt),
                    Union(Sphere(c - new Vector3d(0.465, 0, 0), 0.45, 24), Sphere(c + new Vector3d(0.465, 0, 0), 0.45, 24)) })
                    Solidify(m, 0, true, 0, cell);
                // and a two-level grid (slabs with a hidden gap, refined around it): coarse and fine cells away from transitions
                MeshWindingNumberSolidify r = Solidify(Union(PolyCube(4, 4, 1, (x, y, z) => true, 0.25, P0), PolyCube(4, 4, 1, (x, y, z) => true, 0.25, P0 + new Vector3d(0.0031, 0.0047, 0.2617))), 40, true);
                Assert.True(r.RefineFactor > 1);
            } finally { WindingContouring.CheckPatchesForTests = false; }
            Assert.True(WindingContouring.CoarsePatchCellsChecked > 100, "on the two-level grid " + WindingContouring.CoarsePatchCellsChecked);
            Assert.True(WindingContouring.PatchCellsChecked > 1000, "checked " + WindingContouring.PatchCellsChecked);
            Assert.True(WindingContouring.AmbiguousCellsChecked > 0, "ambiguous " + WindingContouring.AmbiguousCellsChecked + " of " + WindingContouring.PatchCellsChecked);
        }

        [Fact]
        public void Contour_FaceRule_OnAnAlternatingCell()
        {
            // the inside corners of a cell alternate (every face ambiguous): the face rule keeps the four inside corners
            // apart and joins the outside ones across the faces, so four patches (corner disks), as DualContouring builds
            // them; without the rule the outside corners would stay apart too (twelve)
            bool ambiguous;
            var g = new SignGrid() { Inside = n => ((n.x + n.y + n.z) & 1) == 0 };
            Assert.Equal(4, ContouringTopologyCertificate.CellPatchCount(g, Vector3i.Zero, 1, out ambiguous));
            Assert.True(ambiguous);
            // one ambiguous face whose outside corners meet again on the far side: one patch per inside corner group
            var h = new SignGrid() { Inside = n => (n.x == 0 && n.y == 0 && n.z == 0) || (n.x == 1 && n.y == 1 && n.z == 0) };
            Assert.Equal(2, ContouringTopologyCertificate.CellPatchCount(h, Vector3i.Zero, 1, out ambiguous));
            Assert.True(ambiguous);
        }

        [Fact]
        public void Contour_PatternNotFromTheSigns_Fails()
        {
            // an edge marked crossed where its node signs agree: not the pattern Lemma 6.4 reads
            var g = new SignGrid() { Inside = n => n.z <= 0 };
            g.CrossedF = e => (g.Inside(e.Start) != g.Inside(e.End)) || (e.Start == new Vector3i(-1, -1, -1) && e.Axis == 0);
            Assert.False(ContouringTopologyCertificate.ContourBlock(g, Cells(-1, 0, -1, 0, -1, 0), 1));
        }

        // ---- the ball test (remark 2) ----

        [Fact]
        public void IsBall_Cases()
        {
            Assert.True(ContouringTopologyCertificate.IsBall(new[] { Vector3i.Zero }));
            Assert.True(ContouringTopologyCertificate.IsBall(Cells(0, 1, 0, 1, 0, 1)));
            Assert.True(ContouringTopologyCertificate.IsBall(new[] { new Vector3i(0, 0, 0), new Vector3i(1, 0, 0), new Vector3i(1, 1, 0) }));
            // two cubes that share only an edge, or only a node: chi 1, but pinched
            Assert.False(ContouringTopologyCertificate.IsBall(new[] { new Vector3i(0, 0, 0), new Vector3i(1, 1, 0) }));
            Assert.False(ContouringTopologyCertificate.IsBall(new[] { new Vector3i(0, 0, 0), new Vector3i(1, 1, 1) }));
            // an L of three cubes with a fourth touching it only along an edge: chi 1, pinched at that edge
            Assert.False(ContouringTopologyCertificate.IsBall(new[] { new Vector3i(0, 0, 0), new Vector3i(1, 0, 0), new Vector3i(0, 1, 0), new Vector3i(1, 1, 1) }));
            // a ring (a solid torus, chi 0) and a shell (a ball with a cavity: two boundary spheres)
            Assert.False(ContouringTopologyCertificate.IsBall(Cells(0, 2, 0, 2, 0, 0).Where(c => c != new Vector3i(1, 1, 0)).ToList()));
            Assert.False(ContouringTopologyCertificate.IsBall(Cells(0, 2, 0, 2, 0, 2).Where(c => c != new Vector3i(1, 1, 1)).ToList()));
            Assert.False(ContouringTopologyCertificate.IsBall(new Vector3i[0]));
        }

        [Fact]
        public void IsBall_BallWithATorusCavity_IsNot()
        {
            // a 5 x 5 x 3 block with a ring of cells removed inside: chi 1 and every node link a disk or a sphere, but the
            // boundary has two components (a sphere and a torus)
            var cubes = Cells(0, 4, 0, 4, 0, 2).Where(c => !(c.z == 1 && c.x >= 1 && c.x <= 3 && c.y >= 1 && c.y <= 3 && !(c.x == 2 && c.y == 2))).ToList();
            Assert.False(ContouringTopologyCertificate.IsBall(cubes));
        }

        [Fact]
        public void BlockCandidate_MeshSideCells_AreNotTried()
        {
            // a cluster with a cell the mesh side refused is not a block (the contour side reads Theorem 2's patches)
            var cluster = new List<Vector3i> { new Vector3i(3, 4, 5), new Vector3i(4, 4, 5) };
            var d = new Dictionary<Vector3i, int> { { cluster[0], 0 }, { cluster[1], 1 } };
            Assert.True(WindingContouring.block_candidate(null, cluster, 1, 1, new HashSet<(Vector3i, int)>()));
            Assert.False(WindingContouring.block_candidate(null, cluster, 1, 1, new HashSet<(Vector3i, int)> { (new Vector3i(4, 4, 5), 1) }));
        }

        [Fact]
        public void BlockCandidate_TwoLevelGrid_OneLatticeOnly()
        {
            // bins of 4 fine cells; refined bins x >= 10. A coarse cluster needs unrefined bins all around it, a fine cluster
            // refined ones; cells of other sizes are never blocks
            const int B = 4;
            Func<Vector3i, bool> refined = bin => bin.x >= 10;
            var none = new HashSet<(Vector3i, int)>();
            Assert.True(WindingContouring.block_candidate(refined, new List<Vector3i> { new Vector3i(5, 5, 5) }, B, B, none));
            Assert.False(WindingContouring.block_candidate(refined, new List<Vector3i> { new Vector3i(9, 5, 5) }, B, B, none));      // a refined bin next to it
            Assert.True(WindingContouring.block_candidate(refined, new List<Vector3i> { new Vector3i(4 * 12, 21, 21) }, 1, B, none));  // fine, inside refined bins
            Assert.False(WindingContouring.block_candidate(refined, new List<Vector3i> { new Vector3i(4 * 10, 21, 21) }, 1, B, none)); // fine, next to an unrefined bin
            Assert.False(WindingContouring.block_candidate(refined, new List<Vector3i> { new Vector3i(5, 5, 5) }, 2, B, none));
        }

        [Fact]
        public void IsBall_LargeCluster_IsFast()
        {
            // a flat layer of 60 x 60 cells (a refused region along a face in a grid plane): linear, not quadratic in its faces
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(ContouringTopologyCertificate.IsBall(Cells(0, 59, 0, 59, 0, 0)));
            Assert.True(sw.ElapsedMilliseconds < 2000, sw.ElapsedMilliseconds + " ms");
        }

        // ---- end to end ----

        static MeshWindingNumberSolidify Solidify(DMesh3 mesh, int res, bool blocks, int refineLevels = 2, double cellSize = 0, bool parallel = true)
        {
            var s = new MeshWindingNumberSolidify(mesh) {
                Adaptive = true, GridResolution = res, CellSize = cellSize, ComputeTopologyCertificate = true, CleanupResult = false,
                ReduceTolerance = 0, MaxRefineLevels = refineLevels, CertificateBlocks = blocks, CertificateParallel = parallel,
                CertificateMinChunk = 64
            };
            Assert.True(s.Compute());
            return s;
        }

        internal static void AssertClosedManifold(DMesh3 m)
        {
            Assert.True(m.IsClosed());
            foreach (int v in m.VertexIndices()) Assert.False(m.IsBowtieVertex(v));
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z)); }
        }

        static IEnumerable<(string name, DMesh3 mesh, int res)> Tilted()
        {
            yield return ("box", Box(new Vector3d(0.01, 0.02, 0.03), new Vector3d(0.6, 0.4, 0.3), Tilt), 32);
            yield return ("torus", Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.25, 48, 24, Tilt), 32);
            yield return ("sphere", Sphere(new Vector3d(0.01, 0.02, 0.03), 0.6, 24), 32);
        }

        [Fact]
        public void Blocks_CertifyTheSilhouetteDoubleCrossings()
        {
            // curved and tilted surfaces: grid lines nearly tangent to them cross them twice within a cell, which stage 1
            // refuses at every resolution (section 4.8); as blocks these cells are certified, and the result is right
            foreach (var (name, m, res) in Tilted()) {
                MeshWindingNumberSolidify off = Solidify(m, res, false);
                Assert.Equal(ContouringCertificateStatus.CellsUncertified, off.TopologyCertificateStatus);
                Assert.Contains(off.UncertifiedCells, c => c.Reason == ContouringCertificateReason.Edge);
                MeshWindingNumberSolidify on = Solidify(m, res, true);
                Assert.True(on.CertificateBlocksCertified > 0, name);
                Assert.Equal(on.CertificateBlockCells, off.UncertifiedCells.Count);
                Assert.Equal(ContouringCertificateStatus.Certified, on.TopologyCertificateStatus);
                Assert.True(on.ContourTopologyCertified && on.TopologyCertified);
                Assert.Empty(on.UncertifiedCells);
                Assert.Equal(WindingContouringCertificateTests.Truth(m), WindingContouringCertificateTests.Topology(on.Result));
                AssertClosedManifold(on.Result);
                // the blocks only certify: the contour is the same
                Assert.Equal(off.Result.TriangleCount, on.Result.TriangleCount);
                foreach (int v in off.Result.VertexIndices()) Assert.Equal(off.Result.GetVertex(v), on.Result.GetVertex(v));
            }
        }

        // tilted boxes and a thin tilted torus whose refused cells form clusters that are not balls as they are (cells that
        // meet only along an edge or at a node, along the silhouettes); grown by a layer of certified cells they are
        internal static IEnumerable<(string name, DMesh3 mesh, int res)> NotBallClusters()
        {
            Vector3d c = new Vector3d(0.01, 0.02, 0.03);
            Matrix3d T23 = Matrix3d.AxisAngleD(Vector3d.AxisZ, 23) * Matrix3d.AxisAngleD(Vector3d.AxisX, 14.5);
            Matrix3d T37 = Matrix3d.AxisAngleD(Vector3d.AxisZ, 37) * Matrix3d.AxisAngleD(Vector3d.AxisX, 21.5);
            yield return ("box37", Box(c, new Vector3d(0.6, 0.4, 0.3), T37), 16);
            yield return ("box23", Box(c, new Vector3d(0.6, 0.4, 0.3), T23), 20);
            yield return ("thin torus", Torus(c, 0.6, 0.12, 48, 16, T23), 32);
        }

        [Fact]
        public void Blocks_CertifyClustersThatAreNotBalls()
        {
            // stage 2 tried only the clusters that are balls as they are and left these runs uncertified; grown (remark 3
            // of Theorem 6) they are certified, with the right topology, and the contour is the same as without blocks
            foreach (var (name, m, res) in NotBallClusters()) {
                MeshWindingNumberSolidify off = Solidify(m, res, false);
                Assert.Equal(ContouringCertificateStatus.CellsUncertified, off.TopologyCertificateStatus);
                MeshWindingNumberSolidify on = Solidify(m, res, true);
                Assert.True(on.ContourTopologyCertified && on.TopologyCertified, name);
                Assert.Equal(ContouringCertificateStatus.Certified, on.TopologyCertificateStatus);
                Assert.Empty(on.UncertifiedCells);
                Assert.Equal(on.CertificateBlockCells, off.UncertifiedCells.Count);
                Assert.Equal(WindingContouringCertificateTests.Truth(m), WindingContouringCertificateTests.Topology(on.Result));
                AssertClosedManifold(on.Result);
                Assert.Equal(off.Result.TriangleCount, on.Result.TriangleCount);
                foreach (int v in off.Result.VertexIndices()) Assert.Equal(off.Result.GetVertex(v), on.Result.GetVertex(v));
            }
        }

        [Fact]
        public void Blocks_NeverCertifyAWrongContour()
        {
            // thin features (half a cell) that the contour gets wrong, on curved surfaces where blocks are tried: never
            // certified; and blocks are tried in these runs (else the test would not exercise them)
            const double cell = 0.06;
            double w = 0.5 * cell;
            Vector3d c = new Vector3d(0.0123, 0.0217, 0.0311);
            var cases = new (string, DMesh3)[] {
                ("torus", Torus(c, 0.6, w / 2, 96, 8, Tilt)),
                ("gap", Union(Sphere(c - new Vector3d(0.45 + w / 2, 0, 0), 0.45, 24), Sphere(c + new Vector3d(0.45 + w / 2, 0, 0), 0.45, 24))),
                ("shell", Union(Sphere(c, 0.8, 32), Reversed(Sphere(c, 0.8 - w, 32)))),
                ("tunnel", TunnelBall(c, 0.8, w)),
            };
            int wrong = 0, tried = 0;
            foreach (var (name, m) in cases) {
                MeshWindingNumberSolidify s = Solidify(m, 0, true, 0, cell);
                string truth = WindingContouringCertificateTests.Truth(m), got = WindingContouringCertificateTests.Topology(s.Result);
                if (got != truth) {
                    wrong++;
                    Assert.False(s.ContourTopologyCertified, name + ": certified, but " + got + " != " + truth);
                    Assert.NotEmpty(s.UncertifiedCells);
                    tried += s.CertificateBlocksTried;      // (blocks tried in the wrong runs themselves)
                }
            }
            Assert.True(wrong >= 3, "too few wrong results: " + wrong);
            Assert.True(tried > 0);
        }

        // a ball with a straight tunnel of diameter w along z (a polyhedral ball: a sphere mesh with a hole drilled, as a
        // torus of square cross-section is enough for the topology: genus 1)
        static DMesh3 TunnelBall(Vector3d c, double r, double w)
        {
            // a thick torus with a tiny hole: major radius r / 2 + w / 4, minor radius r / 2 - w / 4, so the hole is w wide
            return Torus(c, 0.5 * r + 0.25 * w, 0.5 * r - 0.25 * w, 64, 32, Tilt);
        }

        [Fact]
        public void WhollyInsideOutInput_IsNotCertified()
        {
            // a polycube with its normals inward: winding number -1 inside, the input's level set at 1/2 is empty; the contour
            // (of the flipped copy) is the box, so nothing may be certified
            DMesh3 m = ContouringTopologyCertificateTests.Reversed(PolyCube(3, 3, 2, (x, y, z) => true, 0.25, P0));
            MeshWindingNumberSolidify s = Solidify(m, 40, true);
            Assert.False(s.ContourTopologyCertified);
            Assert.Equal(ContouringCertificateStatus.CellsUncertified, s.TopologyCertificateStatus);
        }

        [Fact]
        public void Invariance_Similarities()
        {
            // the 2^k scales: the same status, blocks and cells, bit for bit; the others: certified implies the right topology
            // (at / 24, where all three are certified with blocks: 3, 3 and 2 of them)
            foreach (var (name, m, r32) in Tilted()) {
                const int res = 24;
                MeshWindingNumberSolidify b = Solidify(m, res, true);
                string truth = WindingContouringCertificateTests.Truth(m);
                foreach (Similarity sim in Similarity.All()) {
                    DMesh3 mm = new DMesh3(m);
                    MeshTransforms.PerVertexTransform(mm, sim.Apply);
                    MeshWindingNumberSolidify s = Solidify(mm, res, true);
                    if (s.ContourTopologyCertified) Assert.Equal(truth, WindingContouringCertificateTests.Topology(s.Result));
                    if (!sim.IsExact) continue;
                    Assert.Equal(b.TopologyCertificateStatus, s.TopologyCertificateStatus);
                    Assert.Equal(b.CertificateBlocksTried, s.CertificateBlocksTried);
                    Assert.Equal(b.CertificateBlocksCertified, s.CertificateBlocksCertified);
                    Assert.Equal(b.CertificateBlockCells, s.CertificateBlockCells);
                    Assert.Equal(b.UncertifiedCells.Count, s.UncertifiedCells.Count);
                    for (int i = 0; i < b.UncertifiedCells.Count; ++i)
                        Assert.Equal(sim.Scale * b.UncertifiedCells[i].Box.Min, s.UncertifiedCells[i].Box.Min);
                }
            }
        }

        [Fact]
        public void Compute_Twice_GivesTheSameBlocks()
        {
            DMesh3 t = Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.25, 48, 24, Tilt);
            var s = new MeshWindingNumberSolidify(t) { Adaptive = true, GridResolution = 32, ComputeTopologyCertificate = true, CleanupResult = false, ReduceTolerance = 0 };
            Assert.True(s.Compute());
            int tried = s.CertificateBlocksTried, certified = s.CertificateBlocksCertified, cells = s.CertificateBlockCells;
            Assert.True(certified > 0);
            Assert.True(s.Compute());
            Assert.Equal(tried, s.CertificateBlocksTried);
            Assert.Equal(certified, s.CertificateBlocksCertified);
            Assert.Equal(cells, s.CertificateBlockCells);
            Assert.Equal(ContouringCertificateStatus.Certified, s.TopologyCertificateStatus);
            // switched off: no stale counts
            s.ComputeTopologyCertificate = false;
            Assert.True(s.Compute());
            Assert.Equal(0, s.CertificateBlocksTried + s.CertificateBlocksCertified + s.CertificateBlockCells);
        }

        [Fact]
        public void Parallel_EqualsSerial_Repeated()
        {
            // the cell tests run in parallel, the blocks serially on their result: the same blocks and cells in every run
            foreach (var (name, m, res) in Tilted()) {
                MeshWindingNumberSolidify reference = null;
                for (int rep = 0; rep < 2; ++rep)
                    foreach (bool parallel in new[] { false, true }) {
                        MeshWindingNumberSolidify s = Solidify(m, res, true, 2, 0, parallel);
                        if (reference == null) { reference = s; continue; }
                        Assert.Equal(reference.TopologyCertificateStatus, s.TopologyCertificateStatus);
                        Assert.Equal(reference.CertificateBlocksTried, s.CertificateBlocksTried);
                        Assert.Equal(reference.CertificateBlocksCertified, s.CertificateBlocksCertified);
                        Assert.Equal(reference.CertificateBlockCells, s.CertificateBlockCells);
                        Assert.Equal(reference.UncertifiedCells.Count, s.UncertifiedCells.Count);
                    }
            }
        }
    }
}
