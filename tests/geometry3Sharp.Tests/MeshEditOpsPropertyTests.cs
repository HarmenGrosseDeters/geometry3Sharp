using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 gap list (thin tests): seeded properties of VoxelSurfaceGenerator (area and volume against the exposed faces
    /// of random voxel sets), MeshInsertPolygon (a polygon, with and without a hole, inserted into a flat grid mesh: the
    /// interior triangles cover exactly the polygon, nothing else changes), RegionOperator (a round trip, an edit inside,
    /// an edge split inside: the rest of the mesh and its area stay) and MeshTrimLoop (a cap cut from a sphere). Plain
    /// System.Random with fixed seeds, so the file also runs on net48.
    /// </summary>
    public class MeshEditOpsPropertyTests
    {
        static double Pow2(int e) => Math.Pow(2, e);

        static double TriArea(DMesh3 m, int t)
        {
            Index3i tv = m.GetTriangle(t);
            return 0.5 * (m.GetVertex(tv.b) - m.GetVertex(tv.a)).Cross(m.GetVertex(tv.c) - m.GetVertex(tv.a)).Length;
        }

        static double Area(DMesh3 m) => m.TriangleIndices().Sum(t => TriArea(m, t));

        static void Close(double expected, double actual, double relative, string what = "")
        {
            Assert.True(Math.Abs(expected - actual) <= relative * Math.Max(Math.Abs(expected), Math.Abs(actual)), $"{what} expected {expected}, actual {actual}");
        }

        static double SignedVolume(DMesh3 m)
        {
            double v = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                v += m.GetVertex(tv.a).Dot(m.GetVertex(tv.b).Cross(m.GetVertex(tv.c))) / 6;
            }
            return v;
        }

        // ---- VoxelSurfaceGenerator --------------------------------------------------------------------------------

        public static IEnumerable<object[]> VoxelCases()
        {
            foreach (int seed in new[] { 1, 2, 3, 4 })
                foreach (bool clockwise in new[] { false, true })
                    yield return new object[] { seed, clockwise };
        }

        [Theory]
        [MemberData(nameof(VoxelCases))]
        public void VoxelSurface_AreaAndVolumeAreThoseOfTheExposedFaces(int seed, bool clockwise)
        {
            var rng = new Random(2026111700 + seed);
            var dims = new Vector3i(6, 5, 7);
            var grid = new Bitmap3(dims);
            int filled = 0;
            for (int z = 0; z < dims.z; ++z) for (int y = 0; y < dims.y; ++y) for (int x = 0; x < dims.x; ++x)
                if (rng.NextDouble() < 0.4 + 0.1 * seed) { grid.Set(new Vector3i(x, y, z), true); ++filled; }
            // exposed faces, the grid boundary counts as empty (CapAtBoundary)
            int exposed = 0;
            foreach (Vector3i v in grid.NonZeros())
                foreach (Vector3i d in new[] { new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0), new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1) }) {
                    Vector3i n = v + d;
                    bool inside = n.x >= 0 && n.y >= 0 && n.z >= 0 && n.x < dims.x && n.y < dims.y && n.z < dims.z;
                    if (!inside || !grid.Get(n)) ++exposed;
                }
            var gen = new VoxelSurfaceGenerator { Voxels = grid, Clockwise = clockwise };
            gen.Generate();
            Assert.Single(gen.Meshes);
            DMesh3 m = gen.Meshes[0];
            Assert.Equal(2 * exposed, m.TriangleCount);
            Close(exposed, Area(m), 1e-12, "area");
            Close(clockwise ? -filled : filled, SignedVolume(m), 1e-9, "volume");
            // without the cap at the boundary, exactly the exposed faces on the grid boundary are missing
            int exposedInside = 0;
            foreach (Vector3i v in grid.NonZeros())
                foreach (Vector3i d in new[] { new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0), new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1) }) {
                    Vector3i n = v + d;
                    if (n.x >= 0 && n.y >= 0 && n.z >= 0 && n.x < dims.x && n.y < dims.y && n.z < dims.z && !grid.Get(n)) ++exposedInside;
                }
            Assert.True(exposedInside < exposed, "vacuous: no voxel on the grid boundary");
            var open = new VoxelSurfaceGenerator { Voxels = grid, CapAtBoundary = false };
            open.Generate();
            Assert.Equal(2 * exposedInside, open.Meshes[0].TriangleCount);
            // a voxel set is its interior faces too when SkipInteriorFaces is off: 6 faces per voxel
            var all = new VoxelSurfaceGenerator { Voxels = grid, SkipInteriorFaces = false };
            all.Generate();
            Assert.Equal(12 * filled, all.Meshes[0].TriangleCount);
        }

        static Bitmap3 RandomGrid()
        {
            var grid = new Bitmap3(new Vector3i(5, 5, 5));
            var rng = new Random(2026111710);
            for (int i = 0; i < 60; ++i) grid.Set(new Vector3i(rng.Next(5), rng.Next(5), rng.Next(5)), true);
            return grid;
        }

        [Fact]
        public void VoxelSurface_MaxMeshElementCount_SplitsWithoutLosingFaces()
        {
            Bitmap3 grid = RandomGrid();
            var one = new VoxelSurfaceGenerator { Voxels = grid };
            one.Generate();
            var split = new VoxelSurfaceGenerator { Voxels = grid, MaxMeshElementCount = 100 };
            split.Generate();
            Assert.True(split.Meshes.Count > 1, "vacuous: no split");
            Assert.Equal(one.Meshes[0].TriangleCount, split.Meshes.Sum(m => m.TriangleCount));
            Close(Area(one.Meshes[0]), split.Meshes.Sum(Area), 1e-12);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(37)]
        [InlineData(24)]
        public void VoxelSurface_MaxMeshElementCount_IsAHardBound(int limit)
        {
            var split = new VoxelSurfaceGenerator { Voxels = RandomGrid(), MaxMeshElementCount = limit };
            split.Generate();
            Assert.All(split.Meshes, m => Assert.True(m.MaxVertexID <= limit && m.MaxTriangleID <= limit, $"{m.MaxVertexID} vertices, {m.MaxTriangleID} triangles"));
            var one = new VoxelSurfaceGenerator { Voxels = RandomGrid() };
            one.Generate();
            Assert.Equal(one.Meshes[0].TriangleCount, split.Meshes.Sum(m => m.TriangleCount));
            Close(Area(one.Meshes[0]), split.Meshes.Sum(Area), 1e-12);
        }

        [Fact]
        public void VoxelSurface_LimitBelowOneVoxel_GivesOneVoxelPerMeshAndEnds()
        {
            var grid = new Bitmap3(new Vector3i(3, 3, 3));
            grid.Set(new Vector3i(0, 0, 0), true); grid.Set(new Vector3i(2, 2, 2), true); grid.Set(new Vector3i(1, 1, 1), true);
            var gen = new VoxelSurfaceGenerator { Voxels = grid, MaxMeshElementCount = 5 };
            gen.Generate();
            Assert.Equal(3, gen.Meshes.Count);
            Assert.All(gen.Meshes, m => Assert.Equal(12, m.TriangleCount));
        }

        [Fact]
        public void VoxelSurface_VoxelWithoutExposedFaces_OpensNoEmptyMesh()
        {
            // A full 3x3x3 grid without the cap at the boundary, minus (1,1,2) and (1,2,1): the last voxel with faces is (1,2,2)
            // (two faces toward the holes: 8 vertices, above the limit of 5, so the mesh is full), and the very last voxel (2,2,2)
            // has no face to add (its three other neighbours are filled, the rest is outside). It must not start an empty mesh.
            var grid = new Bitmap3(new Vector3i(3, 3, 3));
            for (int z = 0; z < 3; ++z) for (int y = 0; y < 3; ++y) for (int x = 0; x < 3; ++x) grid.Set(new Vector3i(x, y, z), true);
            grid.Set(new Vector3i(1, 1, 2), false); grid.Set(new Vector3i(1, 2, 1), false);
            var gen = new VoxelSurfaceGenerator { Voxels = grid, CapAtBoundary = false, MaxMeshElementCount = 5 };
            gen.Generate();
            Assert.NotEmpty(gen.Meshes);
            Assert.All(gen.Meshes, m => Assert.True(m.TriangleCount > 0, "an empty mesh"));
            var unlimited = new VoxelSurfaceGenerator { Voxels = grid, CapAtBoundary = false };
            unlimited.Generate();
            Assert.True(unlimited.Meshes[0].TriangleCount > 0);
            Assert.Equal(unlimited.Meshes[0].TriangleCount, gen.Meshes.Sum(m => m.TriangleCount));
        }

        [Theory]
        [InlineData(48, 1)]        // two isolated voxels of 24 vertices each fit exactly
        [InlineData(47, 2)]
        public void VoxelSurface_LimitIsInclusive(int limit, int meshes)
        {
            var grid = new Bitmap3(new Vector3i(5, 5, 5));
            grid.Set(new Vector3i(1, 1, 1), true); grid.Set(new Vector3i(3, 3, 3), true);
            var gen = new VoxelSurfaceGenerator { Voxels = grid, MaxMeshElementCount = limit };
            gen.Generate();
            Assert.Equal(meshes, gen.Meshes.Count);
        }

        [Fact]
        public void VoxelSurface_GenerateTwiceOnOneInstance_GivesTheSameMeshes()
        {
            var gen = new VoxelSurfaceGenerator { Voxels = RandomGrid(), MaxMeshElementCount = 100 };
            gen.Generate();
            var first = gen.Meshes.Select(m => (m.VertexCount, m.TriangleCount, Area(m), SignedVolume(m))).ToList();
            gen.Generate();
            var second = gen.Meshes.Select(m => (m.VertexCount, m.TriangleCount, Area(m), SignedVolume(m))).ToList();
            Assert.Equal(first, second);
        }

        // ---- MeshInsertPolygon ------------------------------------------------------------------------------------

        static DMesh3 GridMesh(int n, double h)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i * h, j * h, 0));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
            return m;
        }

        public static IEnumerable<object[]> InsertCases()
        {
            foreach (int e in new[] { -10, 0, 10 })
                foreach (bool hole in new[] { false, true })
                    yield return new object[] { e, hole };
        }

        [Theory]
        [MemberData(nameof(InsertCases))]
        public void InsertPolygon_InteriorTrianglesCoverExactlyThePolygon(int e, bool hole)
        {
            double s = Pow2(e);
            var rng = new Random(2026111720 + e + (hole ? 1 : 0));
            for (int round = 0; round < 3; ++round) {
                DMesh3 m = GridMesh(8, s);
                double before = Area(m);
                Vector2d c = new Vector2d((3.8 + rng.NextDouble() * 0.4) * s, (3.8 + rng.NextDouble() * 0.4) * s);
                Polygon2d outer = Polygon2d.MakeCircle(2.3 * s, 17, rng.NextDouble());
                outer.Translate(c);
                var poly = new GeneralPolygon2d(outer);
                if (hole) {
                    Polygon2d inner = Polygon2d.MakeCircle(0.9 * s, 11, rng.NextDouble());
                    inner.Reverse();
                    inner.Translate(c + new Vector2d(0.2 * s, -0.1 * s));
                    poly.AddHole(inner);
                }
                var insert = new MeshInsertPolygon { Mesh = m, Polygon = poly };
                Assert.True(insert.Insert(), $"2^{e} hole {hole} round {round}: Insert failed");
                m.CheckValidity(true, FailMode.Throw);
                Close(before, Area(m), 1e-12, "area");
                double interior = insert.InteriorTriangles.Sum(t => TriArea(m, t));
                Assert.True(Math.Abs(interior - poly.Area) <= 1e-9 * before, $"2^{e} hole {hole} round {round}: interior area {interior}, polygon {poly.Area}");
                var inside = new HashSet<int>(insert.InteriorTriangles);
                foreach (int t in m.TriangleIndices()) {
                    bool contained = poly.Contains(m.GetTriCentroid(t).xy);
                    Assert.True(contained == inside.Contains(t), $"2^{e} hole {hole} round {round}: triangle {t} is {(contained ? "inside" : "outside")} the polygon but {(inside.Contains(t) ? "selected" : "not selected")}");
                }
            }
        }

        // ---- RegionOperator ---------------------------------------------------------------------------------------

        static DMesh3 SphereMesh(int e) { DMesh3 m = Defects.Sphere(Vector3d.Zero, Pow2(e), 6); return m; }

        static int[] Cap(DMesh3 m) => m.TriangleIndices().Where(t => m.GetTriCentroid(t).y > 0.2 * m.GetBounds().Extents.y).ToArray();

        [Theory]
        [InlineData(-10)]
        [InlineData(0)]
        [InlineData(20)]
        public void RegionOperator_RoundTripAndInteriorEdits_KeepTheRestOfTheMesh(int e)
        {
            DMesh3 m = SphereMesh(e);
            int[] tris = Cap(m);
            Assert.InRange(tris.Length, 10, m.TriangleCount - 10);
            double area = Area(m);
            int triCount = m.TriangleCount, vertCount = m.VertexCount;

            // the plain round trip changes nothing
            var op = new RegionOperator(m, tris);
            Assert.True(op.BackPropropagate());
            m.CheckValidity(false, FailMode.Throw);
            Assert.Equal(triCount, m.TriangleCount);
            Assert.Equal(vertCount, m.VertexCount);
            Close(area, Area(m), 1e-12, "area");
            Assert.True(m.IsClosed());

            // an edit of the interior vertices of the submesh: every interior vertex ends exactly `shift` along x from where it was,
            // every vertex on the region border and outside the region where it was (ids of the region's vertices are new after
            // the reinsertion: the submesh vertex maps give the base vertex of each)
            op = new RegionOperator(m, tris);
            var interior = op.CurrentBaseInteriorVertices();
            Assert.NotEmpty(interior);
            double shift = 1e-3 * Pow2(e);
            var expected = new Dictionary<int, Vector3d>();       // submesh vertex -> its position after the reinsertion
            int shifted = 0;
            foreach (int sv in op.Region.SubMesh.VertexIndices()) {
                int bv = op.Region.MapVertexToBaseMesh(sv);
                Vector3d p = op.Region.SubMesh.GetVertex(sv);
                if (interior.Contains(bv) && op.Region.BaseBorderV.Contains(bv) == false) {
                    p = p + shift * Vector3d.AxisX;
                    op.Region.SubMesh.SetVertex(sv, p);
                    ++shifted;
                }
                expected[sv] = p;
            }
            var outside = m.VertexIndices().Where(v => !op.Region.BaseBorderV.Contains(v) && !interior.Contains(v)).ToDictionary(v => v, v => m.GetVertex(v));
            Assert.True(op.BackPropropagate());
            m.CheckValidity(false, FailMode.Throw);
            foreach (var kv in expected)
                Assert.True(m.GetVertex(op.ReinsertSubToBaseMapV[kv.Key]) == kv.Value, $"submesh vertex {kv.Key} is not where the edit put it");
            Assert.True(shifted > 10, "vacuous: " + shifted + " vertices shifted");
            foreach (var kv in outside)
                Assert.True(m.IsVertex(kv.Key) && m.GetVertex(kv.Key) == kv.Value, $"vertex {kv.Key} outside the region moved");
            Assert.Equal(triCount, m.TriangleCount);
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(0)]
        [InlineData(20)]
        public void RegionOperator_SplittingAnInteriorEdge_AddsTwoTrianglesAndKeepsTheSurface(int e)
        {
            DMesh3 m = SphereMesh(e);
            int[] tris = Cap(m);
            double area = Area(m);
            int triCount = m.TriangleCount;
            var op = new RegionOperator(m, tris);
            // an edge of the submesh whose four vertices are all interior to the region
            int edge = -1;
            foreach (int eid in op.Region.SubMesh.EdgeIndices()) {
                Index2i ev = op.Region.SubMesh.GetEdgeV(eid);
                if (op.Region.SubMesh.IsBoundaryVertex(ev.a) || op.Region.SubMesh.IsBoundaryVertex(ev.b) || op.Region.SubMesh.IsBoundaryEdge(eid)) continue;
                edge = eid; break;
            }
            Assert.True(edge >= 0);
            Assert.Equal(MeshResult.Ok, op.Region.SubMesh.SplitEdge(edge, out DMesh3.EdgeSplitInfo info));
            Assert.True(op.BackPropropagate());
            m.CheckValidity(false, FailMode.Throw);
            // the maps from the submesh to the base mesh: every submesh vertex and triangle is where its image is
            foreach (int sv in op.Region.SubMesh.VertexIndices())
                Assert.True(op.Region.SubMesh.GetVertex(sv) == m.GetVertex(op.ReinsertSubToBaseMapV[sv]), $"vertex {sv}: wrong image");
            foreach (int st in op.Region.SubMesh.TriangleIndices())
                Assert.True(op.Region.SubMesh.GetTriCentroid(st).DistanceSquared(m.GetTriCentroid(op.ReinsertSubToBaseMapT[st])) <= 1e-24 * Pow2(2 * e), $"triangle {st}: wrong image");
            Assert.Equal(triCount + 2, m.TriangleCount);
            Close(area, Area(m), 1e-12, "area");
            Assert.True(m.IsClosed());
        }

        // ---- MeshTrimLoop -----------------------------------------------------------------------------------------

        [Theory]
        [InlineData(-10, true)]
        [InlineData(0, true)]
        [InlineData(10, true)]
        [InlineData(-10, false)]
        [InlineData(0, false)]
        [InlineData(10, false)]
        public void TrimLoop_RemovesTheSideOfTheSeed(int e, bool seedInCap)
        {
            double s = Pow2(e);
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = s, EdgeVertices = 24 }.Generate().MakeDMesh();
            double area = Area(m);
            double y0 = 0.5 * s, ring = Math.Sqrt(0.75) * s;
            var pts = new List<Vector3d>();
            for (int i = 0; i < 48; ++i) {
                double a = 2 * Math.PI * i / 48;
                pts.Add(new Vector3d(ring * Math.Cos(a), y0, ring * Math.Sin(a)));
            }
            var loop = new DCurve3(pts, true);
            var trim = new MeshTrimLoop(m, loop, new Vector3d(0, seedInCap ? s : -s, 0));
            Assert.True(trim.Trim(), "Trim failed");
            m.CheckValidity(false, FailMode.Throw);
            // the sphere area is 4 pi s^2, the cap above y = 0.5 s is 2 pi (1 - 0.5) s^2 = a quarter of it: removing the cap
            // leaves three quarters, removing the rest one quarter (the cut moves the border by a ring of triangles)
            double left = Area(m) / area;
            if (seedInCap) Assert.InRange(left, 0.70, 0.80); else Assert.InRange(left, 0.20, 0.30);
            var loops = new MeshBoundaryLoops(m);
            Assert.True(loops.Compute());
            Assert.Equal(1, loops.Count);
            foreach (int v in m.VertexIndices())
                Assert.True(seedInCap ? m.GetVertex(v).y <= y0 + 0.08 * s : m.GetVertex(v).y >= y0 - 0.08 * s, "a vertex on the removed side of the cut");
        }
    }
}
