using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The sampling grids (docs/ROBUSTNESS_SWEEP.md R3, R9, R10): the value of sample (i,j,k) is the value at
    /// GridOriginD + CellSize*(i,j,k), also at UTM coordinates where a float position is off by up to 0.25 (the samples
    /// were computed in float); the grid has the same size in any placement; invalid cell sizes or bounds are an
    /// ArgumentException from Compute instead of an overflow, an out-of-memory or an IndexOutOfRange deep inside.
    /// </summary>
    public class SamplingGridPrecisionTests
    {
        static DMesh3 Sphere(Similarity s)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            return Invariance.Transformed(m, s);
        }

        static Vector3d SamplePosition(Vector3d origin, double cellSize, int i, int j, int k) =>
            new Vector3d(origin.x + i * cellSize, origin.y + j * cellSize, origin.z + k * cellSize);

        /// <summary> the largest difference between |sample| and the distance from its position to the mesh, over the narrow band </summary>
        static double MaxSdfError(DMesh3 mesh, MeshSignedDistanceGrid sdf, double band)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            double max = 0;
            DenseGrid3f g = sdf.Grid;
            for (int k = 0; k < g.nk; ++k) for (int j = 0; j < g.nj; ++j) for (int i = 0; i < g.ni; ++i) {
                if (Math.Abs(g[i, j, k]) > band) continue;
                Vector3d p = SamplePosition(sdf.GridOriginD, sdf.CellSize, i, j, k);
                double d = Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, tree.FindNearestTriangle(p), p));
                max = Math.Max(max, Math.Abs(Math.Abs(g[i, j, k]) - d));
            }
            return max;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SignedDistanceGrid_SampleIsTheDistanceAtItsPosition(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 mesh = Sphere(s);
            double cell = s.ApplyLength(0.2);
            var sdf = new MeshSignedDistanceGrid(mesh, cell) { ExactBandWidth = 2 };
            sdf.Compute();
            // the float distance rounds by about 1e-7 of the band; a float sample position was off by 0.03 to 0.25 at UTM
            double err = MaxSdfError(mesh, sdf, 3 * cell) / s.Scale;
            Assert.True(err < 1e-5, $"{name}: sample value differs from the distance at GridOrigin + CellSize*ijk by {err:E2} (input units)");
            // the grid covers the bounds, with the same number of samples in any placement (an origin rounded down to a
            // float lay up to 0.5 below the bounds at UTM coordinates: 5014 samples instead of 28 with millimetre cells)
            Vector3d min = mesh.CachedBounds.Min;
            Assert.True(sdf.GridOriginD.x <= min.x && sdf.GridOriginD.y <= min.y && sdf.GridOriginD.z <= min.z);
            var sdfId = new MeshSignedDistanceGrid(Sphere(Similarity.Identity), 0.2) { ExactBandWidth = 2 };
            sdfId.Compute();
            for (int k = 0; k < 3; ++k)
                Assert.True(Math.Abs(sdf.Dimensions[k] - sdfId.Dimensions[k]) <= 1, $"{name}: dimensions {sdf.Dimensions} vs {sdfId.Dimensions}");
            if (s.IsExact) {
                // a power-of-two scale gives the same grid, scaled, bit for bit
                var sdf0 = new MeshSignedDistanceGrid(Sphere(Similarity.Identity), 0.2) { ExactBandWidth = 2 };
                sdf0.Compute();
                Assert.Equal(sdf0.Dimensions, sdf.Dimensions);
                Assert.True(sdf.GridOriginD == s.Scale * sdf0.GridOriginD, $"origin {sdf.GridOriginD} vs {sdf0.GridOriginD}");
                for (int q = 0; q < sdf.Grid.size; ++q)
                    Assert.True(sdf.Grid[q] == (float)(s.Scale * sdf0.Grid[q]), $"sample {q}: {sdf.Grid[q]} vs {sdf0.Grid[q]}");
            }
            Vector3d last = SamplePosition(sdf.GridOriginD, sdf.CellSize, sdf.Grid.ni - 1, sdf.Grid.nj - 1, sdf.Grid.nk - 1);
            Vector3d max = mesh.CachedBounds.Max;
            Assert.True(last.x >= max.x && last.y >= max.y && last.z >= max.z, "grid does not cover the bounds");
        }

        [Fact]
        public void SignedDistanceGrid_ParallelAndSpatialModes_SampleIsTheDistanceAtItsPosition()
        {
            DMesh3 mesh = Sphere(Similarity.Utm);
            double cell = 0.2;
            foreach (bool parallel in new[] { false, true }) {
                foreach (bool spatial in new[] { false, true }) {
                    var sdf = new MeshSignedDistanceGrid(mesh, cell, spatial ? new DMeshAABBTree3(mesh, true) : null) { ExactBandWidth = 2, UseParallel = parallel };
                    sdf.Compute();
                    double err = MaxSdfError(mesh, sdf, 3 * cell);
                    Assert.True(err < 1e-5, $"parallel={parallel} spatial={spatial}: {err:E2}");
                }
            }
            var flood = new MeshSignedDistanceGrid(mesh, cell, new DMeshAABBTree3(mesh, true)) {
                ComputeMode = MeshSignedDistanceGrid.ComputeModes.NarrowBand_SpatialFloodFill, NarrowBandMaxDistance = 2 * cell };
            flood.Compute();
            Assert.True(MaxSdfError(mesh, flood, cell) < 1e-5, "flood fill");
        }

        [Fact]
        public void SignedDistanceGrid_MarchingCubesVolume_SameAtUtm()
        {
            // the iso-surface of the trilinear grid at UTM is the one at the origin, moved (it was off by up to 0.25)
            double Volume(Similarity s)
            {
                DMesh3 mesh = Sphere(s);
                var sdf = new MeshSignedDistanceGrid(mesh, 0.1) { ExactBandWidth = 2 };
                sdf.Compute();
                // (the cubes on the grid samples, as in the documented usage: the rounding of the origin to a float
                // moves the grid against the mesh, and cubes between the samples would interpolate the distances)
                var iso = new DenseGridTrilinearImplicit(sdf);
                var mc = new MarchingCubes() { Implicit = iso, IsoValue = 0, Bounds = iso.Bounds(), CubeSize = sdf.CellSize };
                mc.Generate();
                return Invariance.Volume(mc.Mesh);
            }
            // (the float rounding of the origin moves the grid against the mesh by up to 0.03 at UTM: a discretization
            // difference of about 3e-4; the float samples gave 5e-3)
            double v0 = Volume(Similarity.Identity), v1 = Volume(Similarity.Utm);
            Assert.True(Math.Abs(v0 - v1) < 1e-3 * v0, $"volume {v0} at the origin, {v1} at UTM");
        }

        [Fact]
        public void WindingNumberAndScalarGrids_SampleIsTheValueAtItsPosition()
        {
            DMesh3 mesh = Sphere(Similarity.Utm);
            double cell = 0.25;
            Vector3d c = mesh.CachedBounds.Center, dir = new Vector3d(1, 2, 3);
            var scalar = new MeshScalarSamplingGrid(mesh, cell, p => (p - c).Dot(dir)) { ComputeMode = MeshScalarSamplingGrid.ComputeModes.FullGrid };
            scalar.Compute();
            double max = 0;
            for (int k = 0; k < scalar.Grid.nk; ++k) for (int j = 0; j < scalar.Grid.nj; ++j) for (int i = 0; i < scalar.Grid.ni; ++i) {
                Vector3d p = SamplePosition(scalar.GridOriginD, (float)scalar.CellSize, i, j, k);
                max = Math.Max(max, Math.Abs(scalar.Grid[i, j, k] - (p - c).Dot(dir)));
                Assert.True(scalar.CellCenter(i, j, k) == (Vector3f)p);
                Assert.True(scalar.GridOrigin == (Vector3f)scalar.GridOriginD);
            }
            Assert.True(max < 1e-5, $"scalar grid: {max:E2}");

            var tree = new DMeshAABBTree3(mesh, true);
            var winding = new MeshWindingNumberGrid(mesh, tree, cell) { ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid };
            winding.Compute();
            max = 0;
            for (int k = 0; k < winding.Grid.nk; k += 2) for (int j = 0; j < winding.Grid.nj; j += 2) for (int i = 0; i < winding.Grid.ni; i += 2) {
                Vector3d p = SamplePosition(winding.GridOriginD, (float)winding.CellSize, i, j, k);
                max = Math.Max(max, Math.Abs(winding.Grid[i, j, k] - tree.WindingNumber(p)));
            }
            Assert.True(max < 1e-4, $"winding grid: {max:E2}");
        }

        [Fact]
        public void CachingMeshSDF_SampleIsTheDistanceAtItsPosition()
        {
            DMesh3 mesh = Sphere(Similarity.Utm);
            var tree = new DMeshAABBTree3(mesh, true);
            var sdf = new CachingMeshSDF(mesh, 0.2, tree) { MaxOffsetDistance = 0.4f };
            sdf.Initialize();
            double max = 0;
            for (int k = 0; k < sdf.Grid.nk; ++k) for (int j = 0; j < sdf.Grid.nj; ++j) for (int i = 0; i < sdf.Grid.ni; ++i) {
                Vector3d p = SamplePosition(sdf.GridOriginD, sdf.CellSize, i, j, k);
                double d = Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, tree.FindNearestTriangle(p), p));
                if (d > 0.3) continue;
                max = Math.Max(max, Math.Abs(Math.Abs(sdf.GetValue(new Vector3i(i, j, k))) - d));
            }
            Assert.True(max < 1e-5, $"{max:E2}");
        }

        [Fact]
        public void SignedDistanceGrid_ReusedAndParallel_EqualSerialFresh()
        {
            // reuse (Compute twice on one instance) and determinism (parallel equals serial, bit for bit) at UTM
            DMesh3 mesh = Sphere(Similarity.Utm);
            var serial = new MeshSignedDistanceGrid(mesh, 0.2) { ExactBandWidth = 2, UseParallel = false };
            serial.Compute();
            var parallel = new MeshSignedDistanceGrid(mesh, 0.2) { ExactBandWidth = 2, UseParallel = true };
            parallel.Compute();
            parallel.Compute();
            Assert.Equal(serial.GridOriginD, parallel.GridOriginD);
            Assert.Equal(serial.Dimensions, parallel.Dimensions);
            for (int k = 0; k < serial.Grid.size; ++k)
                Assert.Equal(serial.Grid[k], parallel.Grid[k]);
        }

        static DMesh3 MarchSphere(bool pro, double scale, double radius, Vector3d center, double cube, double extent)
        {
            var sphere = new ImplicitSphere3d() { Origin = scale * center, Radius = scale * radius };
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, scale * extent);
            if (pro) {
                var mc = new MarchingCubesPro() { Implicit = sphere, Bounds = bounds, CubeSize = scale * cube, ParallelCompute = false };
                mc.Generate();
                return mc.Mesh;
            } else {
                var mc = new MarchingCubes() { Implicit = sphere, Bounds = bounds, CubeSize = scale * cube, ParallelCompute = false };
                mc.Generate();
                return mc.Mesh;
            }
        }

        [Theory]
        [InlineData(false, -20)]
        [InlineData(false, -10)]
        [InlineData(false, 20)]
        [InlineData(true, -20)]
        [InlineData(true, -10)]
        [InlineData(true, 20)]
        public void MarchingCubes_DistanceField_ScalesExactly(bool pro, int exponent)
        {
            // (R10) the root finder snapped crossings within an absolute 1e-5 of the iso value to the edge ends: a
            // distance field of a small object came out staircased. Now a power-of-two scale gives the same mesh, scaled.
            double scale = Math.Pow(2, exponent);
            Vector3d c = new Vector3d(0.013, 0.021, 0.007);
            DMesh3 a = MarchSphere(pro, 1, 1, c, 0.1, 1.3);
            DMesh3 b = MarchSphere(pro, scale, 1, c, 0.1, 1.3);
            MeshTransforms.Scale(b, 1 / scale);
            Assert.Equal("", Invariance.Difference(a, b));
            ResultChecks.AssertClosed(b);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MarchingCubes_CornersOnTheSurface_StayManifold(bool pro)
        {
            // grid corners exactly on the sphere (value == IsoValue): the vertex is pulled off the corner, as before
            DMesh3 m = MarchSphere(pro, 1, 1, Vector3d.Zero, 0.25, 1.5);
            ResultChecks.AssertClosed(m);
            Assert.True(Math.Abs(Invariance.Volume(m) - 4.0 / 3 * Math.PI) < 0.1 * 4.0 / 3 * Math.PI);
        }

        public static TheoryData<double> InvalidCellSizes => new TheoryData<double> { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -0.1, 1e-9 };

        [Theory]
        [MemberData(nameof(InvalidCellSizes))]
        public void InvalidCellSize_IsAnArgumentException(double cell)
        {
            // (1e-9 on a sphere of radius 1: 8e27 samples, which overflowed int to a negative or a wrong size)
            DMesh3 mesh = Sphere(Similarity.Identity);
            var tree = new DMeshAABBTree3(mesh, true);
            Assert.Throws<ArgumentException>(() => new MeshSignedDistanceGrid(mesh, cell).Compute());
            Assert.Throws<ArgumentException>(() => new CachingMeshSDF(mesh, cell, tree).Initialize());
            Assert.Throws<ArgumentException>(() => new MeshWindingNumberGrid(mesh, tree, cell).Compute());
            Assert.Throws<ArgumentException>(() => new MeshScalarSamplingGrid(mesh, cell, p => p.x).Compute());
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void InfiniteVertex_IsAnArgumentException(double bad)
        {
            DMesh3 mesh = Sphere(Similarity.Identity);
            mesh.SetVertex(mesh.VertexIndices().Skip(5).First(), new Vector3d(0.5, bad, 0.5));
            var tree = new DMeshAABBTree3(mesh, true);
            Assert.Throws<ArgumentException>(() => new MeshSignedDistanceGrid(mesh, 0.2).Compute());
            Assert.Throws<ArgumentException>(() => new MeshScalarSamplingGrid(mesh, 0.2, p => p.x).Compute());
            Assert.Throws<ArgumentException>(() => new MeshWindingNumberGrid(mesh, tree, 0.2).Compute());
            Assert.Throws<ArgumentException>(() => new CachingMeshSDF(mesh, 0.2, tree).Initialize());
        }

        [Fact]
        public void InvalidCellSize_OnAComputedInstance_KeepsItsGrid()
        {
            // the exception comes before any state changes: a reused instance keeps its grid and origin
            DMesh3 mesh = Sphere(Similarity.Identity);
            var sdf = new MeshSignedDistanceGrid(mesh, 0.2);
            sdf.Compute();
            Vector3d origin = sdf.GridOriginD;
            DenseGrid3f grid = sdf.Grid;
            sdf.CellSize = float.NaN;
            Assert.Throws<ArgumentException>(() => sdf.Compute());
            Assert.Equal(origin, sdf.GridOriginD);
            Assert.Same(grid, sdf.Grid);
        }
    }
}
