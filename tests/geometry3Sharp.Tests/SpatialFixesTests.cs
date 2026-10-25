using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs in spatial/, distance/, queries/ and containment/ from the class-by-class review
    /// (docs/Analyse-2026-10.md, section 2.4).
    /// </summary>
    public class SpatialFixesTests
    {
        static DMesh3 UnitSphere()
        {
            Sphere3Generator_NormalizedCube gen = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 };
            return gen.Generate().MakeDMesh();
        }

        static DMesh3 Triangle(Vector3d a, Vector3d b, Vector3d c)
        {
            DMesh3 mesh = new DMesh3();
            int va = mesh.AppendVertex(a), vb = mesh.AppendVertex(b), vc = mesh.AppendVertex(c);
            mesh.AppendTriangle(va, vb, vc);
            return mesh;
        }

        /// <summary>
        /// Runs action on a worker thread; fails if it does not finish (old code loops forever).
        /// </summary>
        static void AssertTerminates(Action action, string what)
        {
            Task t = Task.Run(action);
            Assert.True(t.Wait(TimeSpan.FromSeconds(5)), $"{what} did not terminate");
        }


        // ---- MeshWindingNumberGrid / MeshScalarSamplingGrid: BufferCells != 1 ----

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void MeshWindingNumberGrid_BufferCells_MatchesFullGrid(int bufferCells)
        {
            DMesh3 mesh = UnitSphere();
            DMeshAABBTree3 spatial = new DMeshAABBTree3(mesh, true);

            MeshWindingNumberGrid narrow = new MeshWindingNumberGrid(mesh, spatial, 0.2) { BufferCells = bufferCells };
            narrow.Compute();
            MeshWindingNumberGrid full = new MeshWindingNumberGrid(mesh, spatial, 0.2) {
                BufferCells = bufferCells, ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid };
            full.Compute();

            Assert.Equal(full.Dimensions, narrow.Dimensions);
            foreach (Vector3i ijk in full.Grid.Indices())
                Assert.True(Math.Abs(full.Grid[ijk] - narrow.Grid[ijk]) < 0.1, $"cell {ijk}: full {full.Grid[ijk]}, narrow band {narrow.Grid[ijk]}");
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void MeshScalarSamplingGrid_BufferCells_MatchesFullGrid(int bufferCells)
        {
            DMesh3 mesh = UnitSphere();
            DMeshAABBTree3 spatial = new DMeshAABBTree3(mesh, true);
            // ScalarF is called in parallel; build the lazy winding cache up front
            // (as MeshWindingNumberGrid does), it is not thread-safe to build
            spatial.WindingNumber(Vector3d.Zero);
            Func<Vector3d, double> f = (p) => spatial.WindingNumber(p);

            MeshScalarSamplingGrid narrow = new MeshScalarSamplingGrid(mesh, 0.2, f) { BufferCells = bufferCells };
            narrow.Compute();
            MeshScalarSamplingGrid full = new MeshScalarSamplingGrid(mesh, 0.2, f) {
                BufferCells = bufferCells, ComputeMode = MeshScalarSamplingGrid.ComputeModes.FullGrid };
            full.Compute();

            Assert.Equal(full.Dimensions, narrow.Dimensions);
            foreach (Vector3i ijk in full.Grid.Indices())
                Assert.True(Math.Abs(full.Grid[ijk] - narrow.Grid[ijk]) < 0.1, $"cell {ijk}: full {full.Grid[ijk]}, narrow band {narrow.Grid[ijk]}");
        }


        // ---- GridIndexing: MultigridIndexer floor, ToBlockLocal shifts, ToGrid floor ----

        [Fact]
        public void MultigridIndexer3_NegativeExactMultiple_FloorsCorrectly()
        {
            MultigridIndexer3 idx = new MultigridIndexer3(new Vector3i(4, 4, 4));
            Assert.Equal(new Vector3i(-1, -1, -1), idx.ToBlockIndex(new Vector3i(-4, -4, -4)));
            GridLevelIndex li = idx.ToBlock(new Vector3i(-4, -8, -5));
            Assert.Equal(new Vector3i(-1, -2, -2), li.block_index);
            Assert.Equal(new Vector3i(0, 0, 3), li.local_index);
        }

        [Fact]
        public void MultigridIndexer3_RoundTripsWithShifts()
        {
            MultigridIndexer3 idx = new MultigridIndexer3(new Vector3i(4, 3, 5)) {
                OuterShift = new Vector3i(1, -2, 3), BlockShift = new Vector3i(2, 1, -1) };
            for (int x = -13; x <= 13; ++x) {
                Vector3i outer = new Vector3i(x, x + 1, -x);
                Vector3i block = idx.ToBlockIndex(outer);
                Vector3i local = idx.ToBlockLocal(outer);
                GridLevelIndex li = idx.ToBlock(outer);
                Assert.Equal(block, li.block_index);
                Assert.Equal(local, li.local_index);
                Assert.True(local.x >= 0 && local.x < 4 && local.y >= 0 && local.y < 3 && local.z >= 0 && local.z < 5, $"outer {outer}: local {local} out of block");
                Assert.Equal(outer, idx.FromBlock(block) + local);
            }
        }

        [Fact]
        public void MultigridIndexer2_NegativeExactMultiple_AndShifts()
        {
            MultigridIndexer2 plain = new MultigridIndexer2(new Vector2i(4, 4));
            Assert.Equal(new Vector2i(-1, -2), plain.ToBlockIndex(new Vector2i(-4, -8)));

            MultigridIndexer2 idx = new MultigridIndexer2(new Vector2i(4, 3)) {
                OuterShift = new Vector2i(1, -2), BlockShift = new Vector2i(2, 1) };
            for (int x = -13; x <= 13; ++x) {
                Vector2i outer = new Vector2i(x, -x);
                Vector2i block = idx.ToBlockIndex(outer);
                Vector2i local = idx.ToBlockLocal(outer);
                GridLevelIndex2 li = idx.ToBlock(outer);
                Assert.Equal(block, li.block_index);
                Assert.Equal(local, li.local_index);
                Assert.True(local.x >= 0 && local.x < 4 && local.y >= 0 && local.y < 3, $"outer {outer}: local {local} out of block");
                Assert.Equal(outer, idx.FromBlock(block) + local);
            }
        }

        [Fact]
        public void BiGrid3_UpdateAtNegativeBlockBoundary_WritesRightCell()
        {
            BiGrid3<Bitmap3> grid = new BiGrid3<Bitmap3>(new Bitmap3(new Vector3i(4, 4, 4)));
            grid.Update(new Index3i(-4, 0, 0), (block, local) => block.Set(local, true));

            Bitmap3 b = grid.BlockGrid.Get(new Vector3i(-1, 0, 0), false);
            Assert.NotNull(b);
            Assert.True(b.Get(new Vector3i(0, 0, 0)));
        }

        [Fact]
        public void GridIndexers_ToGrid_FloorNegativeCoordinates()
        {
            Assert.Equal(new Vector3i(-1, -1, 0), new ScaleGridIndexer3(1.0).ToGrid(new Vector3d(-0.5, -0.5, 0.5)));
            Assert.Equal(new Vector3i(-1, 0, -2), new ShiftGridIndexer3(new Vector3d(1, 1, 1), 1.0).ToGrid(new Vector3d(0.5, 1.5, -0.5)));
            Assert.Equal(new Vector3i(-1, -1, 0), new FrameGridIndexer3(Frame3f.Identity, Vector3f.One).ToGrid(new Vector3d(-0.5, -0.5, 0.5)));
            Assert.Equal(new Vector2i(-1, 0), new ScaleGridIndexer2(1.0).ToGrid(new Vector2d(-0.5, 0.5)));
            Assert.Equal(new Vector2i(-1, 0), new ShiftGridIndexer2(new Vector2d(1, 1), 1.0).ToGrid(new Vector2d(0.5, 1.5)));
        }


        // ---- MeshSignedDistanceGrid.point_triangle_distance with degenerate triangles ----

        [Fact]
        public void PointTriangleDistance_CollinearTriangle()
        {
            Vector3d x0 = new Vector3d(0, 1, 0);
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 0, 0), c = new Vector3d(2, 0, 0);
            Assert.Equal(1.0, MeshSignedDistanceGrid.point_triangle_distance(ref x0, ref a, ref b, ref c), 6);

            Vector3f x0f = (Vector3f)x0, af = (Vector3f)a, bf = (Vector3f)b, cf = (Vector3f)c;
            Assert.Equal(1.0f, MeshSignedDistanceGrid.point_triangle_distance(ref x0f, ref af, ref bf, ref cf), 5);
        }

        [Fact]
        public void PointTriangleDistance_CoincidentVertices_NoNaN()
        {
            Vector3d x0 = new Vector3d(3, 4, 0);
            Vector3d a = Vector3d.Zero;
            Assert.Equal(5.0, MeshSignedDistanceGrid.point_triangle_distance(ref x0, ref a, ref a, ref a), 6);
            Assert.Equal(5.0, MeshSignedDistanceGrid.point_segment_distance(ref x0, ref a, ref a), 6);

            Vector3f x0f = (Vector3f)x0, af = Vector3f.Zero;
            Assert.Equal(5.0f, MeshSignedDistanceGrid.point_triangle_distance(ref x0f, ref af, ref af, ref af), 5);
            Assert.Equal(5.0f, MeshSignedDistanceGrid.point_segment_distance(ref x0f, ref af, ref af), 5);
        }


        // ---- DenseGrid2.assign_border, DenseGrid2f/3f copy constructors ----

        [Fact]
        public void DenseGrid2_AssignBorder_SetsAllBorderCells()
        {
            for (int rings = 1; rings <= 2; ++rings) {
                DenseGrid2f grid = new DenseGrid2f(6, 7, 0);
                grid.assign_border(1, rings);
                for (int j = 0; j < 7; ++j) {
                    for (int i = 0; i < 6; ++i) {
                        bool border = i < rings || j < rings || i >= 6 - rings || j >= 7 - rings;
                        Assert.True(grid[i, j] == (border ? 1 : 0), $"rings {rings}: cell ({i},{j}) = {grid[i, j]}");
                    }
                }
            }
        }

        [Fact]
        public void DenseGrid_CopyConstructor_EmptySource()
        {
            DenseGrid2f g2 = new DenseGrid2f(new DenseGrid2f());
            Assert.Equal(0, g2.size);
            DenseGrid3f g3 = new DenseGrid3f(new DenseGrid3f());
            Assert.Equal(0, g3.size);
        }


        // ---- Bitmap3.Filter ----

        [Fact]
        public void Bitmap3_Filter_CountsNeighboursBeforeRemoving()
        {
            Bitmap3 bmp = new Bitmap3(new Vector3i(5, 3, 3));
            for (int x = 1; x <= 3; ++x)
                bmp.Set(new Vector3i(x, 1, 1), true);
            bmp.Filter(1);
            Assert.False(bmp.Get(new Vector3i(1, 1, 1)));
            Assert.True(bmp.Get(new Vector3i(2, 1, 1)), "middle voxel has 2 neighbours in the input and must survive");
            Assert.False(bmp.Get(new Vector3i(3, 1, 1)));
        }


        // ---- DCurve3BoxTree ----

        static DCurve3 Polyline(int n)
        {
            List<Vector3d> v = new List<Vector3d>();
            for (int i = 0; i < n; ++i)
                v.Add(new Vector3d(i, 0, 0));
            return new DCurve3(v, false);
        }

        [Fact]
        public void DCurve3BoxTree_RayDistance_IsSquared()
        {
            DCurve3BoxTree tree = new DCurve3BoxTree(Polyline(5));
            Ray3d ray = new Ray3d(new Vector3d(1.5, 2, -1), Vector3d.AxisZ);
            int seg; double segT, rayT;
            double d = tree.SquaredDistance(ray, out seg, out segT, out rayT);
            Assert.Equal(4.0, d, 9);
        }

        [Fact]
        public void DCurve3BoxTree_MaxDist_IsUnsquared()
        {
            DCurve3BoxTree tree = new DCurve3BoxTree(Polyline(5));
            int seg; double segT;
            double d = tree.SquaredDistance(new Vector3d(1.5, 3, 0), out seg, out segT, 4.0);
            Assert.Equal(9.0, d, 9);
            d = tree.SquaredDistance(new Vector3d(1.5, 3, 0), out seg, out segT, 2.0);
            Assert.Equal(double.MaxValue, d);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void DCurve3BoxTree_TinyCurve_BuildTerminates(int n)
        {
            DCurve3BoxTree tree = null;
            AssertTerminates(() => tree = new DCurve3BoxTree(Polyline(n)), $"DCurve3BoxTree with {n} vertices");
            int seg; double segT;
            Assert.Equal(double.MaxValue, tree.SquaredDistance(Vector3d.Zero, out seg, out segT));
            Assert.Equal(-1, seg);
        }


        // ---- Polygon2dBoxTree ----

        [Fact]
        public void Polygon2dBoxTree_TwoVertices_Builds()
        {
            Polygon2d poly = new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(4, 0) });
            Polygon2dBoxTree tree = new Polygon2dBoxTree(poly);
            Assert.Equal(1.0, tree.DistanceSquared(new Vector2d(2, 1)), 9);
        }

        [Fact]
        public void Polygon2dBoxTree_MaxDist_IsUnsquared()
        {
            Polygon2d square = Polygon2d.MakeRectangle(Vector2d.Zero, 2, 2);
            Polygon2dBoxTree tree = new Polygon2dBoxTree(square);
            int seg; double segT;
            Assert.Equal(4.0, tree.SquaredDistance(new Vector2d(3, 0), out seg, out segT, 3.0), 9);
            Assert.Equal(double.MaxValue, tree.SquaredDistance(new Vector2d(3, 0), out seg, out segT, 1.5));
        }

        [Fact]
        public void GeneralPolygon2dBoxTree_HoleDistance_Guard()
        {
            // guard: holes are queried with the outer distance as bound; must keep working
            GeneralPolygon2d gp = new GeneralPolygon2d(Polygon2d.MakeRectangle(Vector2d.Zero, 20, 20));
            Polygon2d hole = Polygon2d.MakeRectangle(Vector2d.Zero, 2, 2); hole.Reverse();
            gp.AddHole(hole, false);
            GeneralPolygon2dBoxTree tree = new GeneralPolygon2dBoxTree(gp);
            Assert.Equal(1.0, tree.DistanceSquared(new Vector2d(2, 0)), 9);
            Assert.Equal(4.0, tree.DistanceSquared(new Vector2d(8, 0)), 9);
        }


        // ---- BasicIntersectionTargets ----

        [Fact]
        public void TransformedIntersectionTarget_UsesBaseTargetAndNormalMap()
        {
            DMesh3 mesh = Triangle(new Vector3d(-10, -10, 0), new Vector3d(10, -10, 0), new Vector3d(0, 10, 0));
            DMeshIntersectionTarget baseTarget = new DMeshIntersectionTarget(mesh, new DMeshAABBTree3(mesh, true));
            TransformedIntersectionTarget target = new TransformedIntersectionTarget() {
                BaseTarget = baseTarget,
                MapToBaseF = (r) => new Ray3d(r.Origin - new Vector3d(0, 0, 5), r.Direction),
                MapFromBasePosF = (p) => p + new Vector3d(0, 0, 5),
                MapFromBaseNormalF = (n) => n
            };
            Assert.True(target.HasNormal);
            Vector3d hit, normal;
            Assert.True(target.RayIntersect(new Ray3d(new Vector3d(0, 0, 10), -Vector3d.AxisZ), out hit, out normal));
            Assert.True(hit.Distance(new Vector3d(0, 0, 5)) < 1e-9, $"hit {hit}");
            Assert.True(normal.Distance(Vector3d.AxisZ) < 1e-9, $"normal {normal}");
        }

        [Fact]
        public void PlaneIntersectionTarget_NormalFollowsFrame()
        {
            PlaneIntersectionTarget target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 2)), NormalAxis = 2 };
            Vector3d hit, normal;
            Assert.True(target.RayIntersect(new Ray3d(Vector3d.Zero, Vector3d.AxisZ), out hit, out normal));
            Assert.True(hit.Distance(new Vector3d(0, 0, 2)) < 1e-6, $"hit {hit}");
            Assert.True(normal.Distance(Vector3d.AxisZ) < 1e-6, $"normal {normal}");
        }


        // ---- distance ----

        [Fact]
        public void DistPoint2Circle2_Issue215_Guard()
        {
            // guard: upstream #215 is already fixed here
            Circle2d circle = new Circle2d(Vector2d.Zero, 5);
            Assert.Equal(5.0, new DistPoint2Circle2(Vector2d.Zero, circle).Get(), 9);
            Assert.Equal(3.0, new DistPoint2Circle2(new Vector2d(0, 2), circle).Get(), 9);
            Assert.Equal(5.0, new DistPoint2Circle2(new Vector2d(6, 8), circle).Get(), 9);
        }

        [Fact]
        public void DistPoint3Cylinder3_PointOnAxis_NoNaN()
        {
            Cylinder3d finite = new Cylinder3d(Vector3d.Zero, Vector3d.AxisZ, 2, 10);
            DistPoint3Cylinder3 d = new DistPoint3Cylinder3(new Vector3d(0, 0, 1), finite);
            Assert.Equal(4.0, d.GetSquared(), 9);
            Assert.Equal(-2.0, d.SignedDistance, 9);
            Assert.Equal(2.0, d.CylinderClosest.Distance(new Vector3d(0, 0, 1)), 9);

            Cylinder3d infinite = new Cylinder3d(Vector3d.Zero, Vector3d.AxisZ, 2, double.MaxValue);
            d = new DistPoint3Cylinder3(new Vector3d(0, 0, 1), infinite);
            Assert.Equal(4.0, d.GetSquared(), 9);
            Assert.Equal(2.0, d.CylinderClosest.Distance(new Vector3d(0, 0, 1)), 9);
        }


        // ---- TilingUtil ----

        [Fact]
        public void BoundedRegularTiling2_FitsAllTiles()
        {
            AxisAlignedBox2d element = new AxisAlignedBox2d(0, 0, 1, 1);
            AxisAlignedBox2d bounds = new AxisAlignedBox2d(0, 0, 10, 10);
            List<Vector2d> t = TilingUtil.BoundedRegularTiling2(element, bounds, 1.0);
            Assert.Equal(25, t.Count);
            foreach (Vector2d v in t)
                Assert.True(v.x >= 0 && v.y >= 0 && v.x + 1 <= 10 + 1e-9 && v.y + 1 <= 10 + 1e-9, $"tile at {v} outside bounds");
        }

        [Fact]
        public void BoundedCircleTiling2_UsesSpacingPerCount()
        {
            AxisAlignedBox2d element = new AxisAlignedBox2d(0, 0, 1, 1);
            AxisAlignedBox2d bounds = new AxisAlignedBox2d(0, 0, 10, 10);
            List<Vector2d> t = TilingUtil.BoundedCircleTiling2(element, bounds, 1.0);
            Assert.True(t.Count > 1, $"only {t.Count} tile(s)");
            foreach (Vector2d v in t)
                Assert.True(v.x >= 0 && v.y >= 0 && v.x + 1 <= 10 + 1e-9, $"tile at {v} outside bounds");
        }


        // ---- queries ----

        [Fact]
        public void IntersectionUtil_RaySphereOverload_IgnoresHitsBehindRay()
        {
            Vector3d o = new Vector3d(0, 0, 5), d = Vector3d.AxisZ, c = Vector3d.Zero;
            LinearIntersection r = IntersectionUtil.RaySphere(ref o, ref d, ref c, 1.0);
            Assert.False(r.intersects);

            o = Vector3d.Zero;
            r = IntersectionUtil.RaySphere(ref o, ref d, ref c, 1.0);
            Assert.True(r.intersects);
            Assert.Equal(1, r.numIntersections);
            Assert.Equal(1.0, r.parameter.a, 9);
        }

        [Fact]
        public void RayIntersection_Sphere_OriginInside()
        {
            float t;
            Assert.True(RayIntersection.Sphere(new Vector3f(0.5f, 0, 0), Vector3f.AxisX, Vector3f.Zero, 1, out t));
            Assert.Equal(0.5f, t, 5);
            // outside: unchanged
            Assert.True(RayIntersection.Sphere(new Vector3f(-3, 0, 0), Vector3f.AxisX, Vector3f.Zero, 1, out t));
            Assert.Equal(2.0f, t, 5);
        }

        [Fact]
        public void RayIntersection_InfiniteCylinder_OriginInside()
        {
            float t;
            Assert.True(RayIntersection.InfiniteCylinder(new Vector3f(0.5f, 0, 0), Vector3f.AxisX, Vector3f.Zero, Vector3f.AxisZ, 1, out t));
            Assert.Equal(0.5f, t, 5);
        }

        [Fact]
        public void FindNearestTriangles_LinearSearch_ReturnsSquaredDistance()
        {
            DMesh3 m1 = Triangle(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            DMesh3 m2 = Triangle(new Vector3d(0, 0, 3), new Vector3d(1, 0, 3), new Vector3d(0, 1, 3));
            double fNearestSqr;
            MeshQueries.FindNearestTriangles_LinearSearch(m1, m2, out fNearestSqr);
            Assert.Equal(9.0, fNearestSqr, 9);
        }

        [Fact]
        public void EdgeLengthStats_EmptyMesh_DoesNotThrow()
        {
            double mn, mx, avg;
            MeshQueries.EdgeLengthStats(new DMesh3(), out mn, out mx, out avg);
            Assert.Equal(0, avg);
            MeshQueries.EdgeLengthStats(new DMesh3(), out mn, out mx, out avg, 10);
        }

        [Fact]
        public void EditMeshSpatial_FindNearestHitTriangle_RespectsMaxDist()
        {
            DMesh3 mesh = Triangle(new Vector3d(-10, -10, 5), new Vector3d(10, -10, 5), new Vector3d(0, 10, 5));
            DMeshAABBTree3 tree = new DMeshAABBTree3(mesh, true);
            DMesh3 edit = new DMesh3(mesh);
            int va = edit.AppendVertex(new Vector3d(-10, -10, 3)), vb = edit.AppendVertex(new Vector3d(10, -10, 3)), vc = edit.AppendVertex(new Vector3d(0, 10, 3));
            int added = edit.AppendTriangle(va, vb, vc);

            EditMeshSpatial spatial = new EditMeshSpatial() { SourceMesh = mesh, SourceSpatial = tree, EditMesh = edit };
            Ray3d ray = new Ray3d(Vector3d.Zero, Vector3d.AxisZ);
            Assert.Equal(0, spatial.FindNearestHitTriangle(ray));
            Assert.Equal(DMesh3.InvalidID, spatial.FindNearestHitTriangle(ray, 4.0));

            spatial.AddTriangle(added);
            Assert.Equal(added, spatial.FindNearestHitTriangle(ray, 4.0));
            Assert.Equal(DMesh3.InvalidID, spatial.FindNearestHitTriangle(ray, 2.0));
        }


        // ---- containment ----

        [Fact]
        public void ContMinBox2_CollinearPoints()
        {
            List<Vector2d> pts = new List<Vector2d>() { new Vector2d(0, 0), new Vector2d(1, 1), new Vector2d(3, 3), new Vector2d(2, 2) };
            ContMinBox2 cont = new ContMinBox2(pts, 1e-9, QueryNumberType.QT_DOUBLE, false);
            Box2d box = cont.MinBox;
            Assert.True(box.Center.Distance(new Vector2d(1.5, 1.5)) < 1e-9, $"center {box.Center}");
            Assert.Equal(1.5 * Math.Sqrt(2), box.Extent.x, 9);
            Assert.Equal(0.0, box.Extent.y, 9);
            Assert.Equal(1.0, box.AxisX.Length, 9);
            Assert.Equal(0.0, box.AxisX.Dot(box.AxisY), 9);
            foreach (Vector2d p in pts) {
                Vector2d d = p - box.Center;
                Assert.True(Math.Abs(d.Dot(box.AxisX)) <= box.Extent.x + 1e-9 && Math.Abs(d.Dot(box.AxisY)) <= box.Extent.y + 1e-9, $"{p} not contained");
            }
        }


        // ---- DSparseGrid3 ----

        [Fact]
        public void DSparseGrid3_Density_Finite()
        {
            DSparseGrid3<Bitmap3> grid = new DSparseGrid3<Bitmap3>(new Bitmap3(new Vector3i(2, 2, 2)));
            Assert.Equal(0.0, grid.Density);
            grid.Get(new Vector3i(3, 3, 3), true);
            Assert.Equal(1.0, grid.Density, 9);
            grid.Get(new Vector3i(4, 3, 3), true);
            Assert.Equal(1.0, grid.Density, 9);
            grid.Get(new Vector3i(4, 4, 3), true);
            Assert.Equal(0.75, grid.Density, 9);
        }
    }
}
