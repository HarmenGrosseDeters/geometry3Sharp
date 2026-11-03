using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class RobustnessSweepRemainingTests
    {
        [Fact]
        public void OracleCostCheck_DoesNotCompeteWithOtherTestCollections()
        {
            var collection = Assert.Single(typeof(QualityMesh2CollapsedPartsReportTests).GetCustomAttributesData()
                .Where(a => a.AttributeType == typeof(CollectionAttribute)));
            var definition = Assert.Single(typeof(QualityMesh2CollapsedPartsReportTests).Assembly.GetTypes()
                .SelectMany(t => t.GetCustomAttributesData())
                .Where(a => a.AttributeType == typeof(CollectionDefinitionAttribute)
                    && Equals(a.ConstructorArguments[0].Value, collection.ConstructorArguments[0].Value)));
            Assert.Contains(definition.NamedArguments,
                a => a.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization) && Equals(a.TypedValue.Value, true));
        }

        [Theory]
        [InlineData(1e308, true, false)]
        [InlineData(1.7e308, true, false)]
        [InlineData(1e308, false, false)]
        [InlineData(1e308, true, true)]
        public void Reducer_ExtremeMidpointsRemainFinite(double scale, bool optimal, bool fast)
        {
            var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Scale(mesh, scale);
            AssertFiniteCoordinates(mesh);
            int before = mesh.TriangleCount;
            var reducer = new Reducer(mesh) { MinimizeQuadricPositionError = optimal };
            if (fast)
                reducer.FastCollapsePass(double.MaxValue, 1);
            else
                reducer.ReduceToTriangleCount(before / 2);
            Assert.True(mesh.TriangleCount < before);
            AssertFiniteCoordinates(mesh);
            Assert.True(mesh.IsClosed());
            // CheckValidity squares coordinates; normalize before checking topology at this scale.
            MeshTransforms.Scale(mesh, 1 / scale);
            Assert.True(mesh.CheckValidity());
        }

        static void AssertFiniteCoordinates(DMesh3 mesh)
        {
            foreach (Vector3d v in mesh.Vertices())
                for (int d = 0; d < 3; ++d)
                    Assert.True(!double.IsNaN(v[d]) && !double.IsInfinity(v[d]));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1e-300)]
        public void Sampling_RejectsInvalidSizesBeforeWork(double size)
        {
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, 1);
            Assert.Throws<ArgumentException>(() => new ImplicitFieldSampler3d(bounds, size));
            foreach (bool parallel in new[] { false, true }) {
                var mc = new MarchingCubes { Bounds = bounds, CubeSize = size, ParallelCompute = parallel };
                Assert.Throws<ArgumentException>(() => mc.Generate());
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Sampling_RejectsNonFiniteBounds(double value)
        {
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, 1);
            bounds.Max.x = value;
            Assert.Throws<ArgumentException>(() => new ImplicitFieldSampler3d(bounds, 0.25));
            Assert.Throws<ArgumentException>(() => new MarchingCubes { Bounds = bounds }.Generate());
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Submeshes_KeepKeyOrderAcrossSchedules(string name)
        {
            var mesh = Invariance.Transformed(new Sphere3Generator_NormalizedCube { EdgeVertices = 5 }.Generate().MakeDMesh(), Similarity.ByName(name));
            object[] keys = Enumerable.Range(0, 16).Select(i => (object)i).ToArray();
            DSubmesh3Set serial = null;
            try {
                gParallel.SerialOnThisThread = true;
                serial = new DSubmesh3Set(mesh, keys, key => new[] { (int)key });
            } finally { gParallel.SerialOnThisThread = false; }
            for (int run = 0; run < 3; ++run) {
                var result = new DSubmesh3Set(mesh, keys, key => {
                    System.Threading.Thread.Sleep((16 - (int)key) % 4);
                    return new[] { (int)key };
                });
                for (int i = 0; i < keys.Length; ++i) {
                    Assert.Same(result.KeyToMesh[keys[i]], result.Submeshes[i]);
                    Assert.Equal("", Invariance.Difference(serial.Submeshes[i].SubMesh, result.Submeshes[i].SubMesh));
                    Assert.True(result.Submeshes[i].SubMesh.CheckValidity());
                }
            }
        }

        [Fact]
        public void Sampler_ValidSizesPreserveSamplingAndReuse()
        {
            float[] reference = null;
            foreach (Similarity s in Similarity.All()) {
                Vector3d center = s.Apply(Vector3d.Zero);
                var sampler = new ImplicitFieldSampler3d(new AxisAlignedBox3d(center, s.Scale), 0.25 * s.Scale);
                var sphere = new ImplicitSphere3d { Origin = center, Radius = s.Scale };
                sampler.Sample(sphere);
                float[] values = (float[])sampler.Grid.Buffer.Clone();
                sampler.Sample(sphere);
                Assert.Equal(values, sampler.Grid.Buffer);
                Assert.All(values, v => Assert.True(!float.IsNaN(v) && !float.IsInfinity(v)));
                Assert.Contains(values, v => v < 0);
                if (reference == null) reference = values;
                if (s.IsExact)
                    Assert.Equal(reference, values.Select(v => (float)(v / s.Scale)).ToArray());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MarchingCubes_CancelThenReuse(bool parallel)
        {
            var mc = new MarchingCubes { ParallelCompute = parallel, CancelF = () => true,
                Implicit = new ImplicitSphere3d { Radius = 1 }, Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.5) };
            mc.Generate();
            Assert.Equal(0, mc.Mesh.TriangleCount);
            mc.CancelF = () => false;
            mc.Generate();
            Assert.True(mc.Mesh.IsClosed());
            Assert.True(mc.Mesh.CheckValidity());
            Assert.True(mc.Mesh.TriangleCount > 0);
        }

        static MeshSpatialSort NestedSolids(Similarity s)
        {
            var sort = new MeshSpatialSort();
            foreach (double radius in new[] { 4.0, 1.0, 3.0, 2.0 }) {
                var mesh = new Sphere3Generator_NormalizedCube { Radius = radius, EdgeVertices = 4 }.Generate().MakeDMesh();
                sort.AddMesh(Invariance.Transformed(mesh, s), radius);
            }
            return sort;
        }

        static string SortSignature(MeshSpatialSort sort)
        {
            return string.Join(";", sort.Solids.Select(s => s.Outer.Identifier + ":" + string.Join(",", s.Cavities.Select(c => c.Identifier))))
                + "|" + string.Join(";", sort.Components.Select(c => string.Join(",", c.InsideOf.Select(p => p.Identifier))
                    + ":" + string.Join(",", c.InsideSet.Select(p => p.Identifier))));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SpatialSort_StableOrderAndReuse(string name)
        {
            Similarity s = Similarity.ByName(name);
            var serial = NestedSolids(s);
            try {
                gParallel.SerialOnThisThread = true;
                serial.Sort();
            } finally { gParallel.SerialOnThisThread = false; }
            Assert.Equal(new[] { 4.0, 2.0 }, serial.Solids.Select(x => (double)x.Outer.Identifier));
            Assert.Equal(new[] { 3.0, 1.0 }, serial.Solids.Select(x => (double)Assert.Single(x.Cavities).Identifier));
            var parallel = NestedSolids(s);
            for (int run = 0; run < 3; ++run) {
                parallel.Sort();
                Assert.Equal(SortSignature(serial), SortSignature(parallel));
                Assert.All(parallel.Components, c => {
                    Assert.True(c.Mesh.IsClosed());
                    Assert.True(c.Mesh.CheckValidity());
                });
            }
        }

        [Theory]
        [InlineData(MarchingCubes.RootfindingModes.SingleLerp)]
        [InlineData(MarchingCubes.RootfindingModes.LerpSteps)]
        [InlineData(MarchingCubes.RootfindingModes.Bisection)]
        public void MarchingCubes_ParallelIdsMatchSerialAndReuse(MarchingCubes.RootfindingModes mode)
        {
            DMesh3 reference = null;
            foreach (Similarity s in Similarity.All()) {
                Vector3d center = s.Apply(Vector3d.Zero);
                var mc = new MarchingCubes {
                    Implicit = new ImplicitSphere3d { Origin = center, Radius = s.Scale },
                    Bounds = new AxisAlignedBox3d(center, 1.5 * s.Scale), CubeSize = 0.25 * s.Scale,
                    RootMode = mode, ParallelCompute = false
                };
                mc.Generate();
                DMesh3 serial = mc.Mesh;
                Assert.True(serial.TriangleCount > 0);
                Assert.True(serial.IsClosed());
                Assert.True(serial.CheckValidity());
                if (reference == null) reference = serial;
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(serial, s)));
                Assert.All(serial.Vertices(), v => Assert.InRange((v - center).Length / s.Scale, 0.97, 1.03));
                mc.ParallelCompute = true;
                for (int run = 0; run < 3; ++run) {
                    mc.Generate();
                    Assert.Equal("", Invariance.Difference(serial, mc.Mesh));
                    Assert.Equal(mc.Mesh.VertexCount, mc.VertexHash.Count);
                    foreach (var entry in mc.VertexHash)
                        Assert.Equal(entry.Key, mc.Mesh.GetVertex(entry.Value));
                }
            }
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void WindingRegion_DoesNotWrapAtIntegerLimits(int edge)
        {
            var contour = new WindingContouring(Array.Empty<DMesh3>(), v => 0, 2) { RefineMargin = 1 };
            var region = new HashSet<Vector3i>();
            var method = typeof(WindingContouring).GetMethod("add_to_region", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(HashSet<Vector3i>), typeof(WindingNumberOctree), typeof(Vector3i) }, null);
            method.Invoke(contour, new object[] { region, null, new Vector3i(edge, 0, 0) });
            Assert.Equal(18, region.Count);
            Assert.All(region, c => Assert.True(Math.Abs((long)c.x - edge) <= 1));
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void WindingRegion_EdgeRangeDoesNotWrap(int edge)
        {
            var contour = new WindingContouring(Array.Empty<DMesh3>(), v => 0, 2) { RefineMargin = 1 };
            var region = new HashSet<Vector3i>();
            var method = typeof(WindingContouring).GetMethod("add_to_region", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(HashSet<Vector3i>), typeof(WindingNumberOctree), typeof(Vector3d), typeof(Vector3d) }, null);
            var octree = new WindingNumberOctree(new DMesh3(), null) {
                Origin = Vector3d.Zero, LeafCellSize = 1
            };
            Vector3d p = new Vector3d((double)edge + 0.5, 0.5, 0.5);
            method.Invoke(contour, new object[] { region, octree, p, p });
            Assert.Equal(18, region.Count);
            Assert.All(region, c => Assert.True(Math.Abs((long)c.x - edge) <= 1));
        }
    }
}
