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
    /// PartitionedReducer (Reducer / Remesher 2.0, T3), the public Reducer on the fixed slabs of PartitionedReduction (whose
    /// argument PartitionedReductionTests pins): its contract. The same result for every thread count and run, the 2^k
    /// similarities bit for bit, reuse of an instance, the input untouched, no attributes, the serial fallbacks, cancel,
    /// exceptions of the parts, and the guarantees of the self-intersection guard.
    /// </summary>
    public class PartitionedReducerTests
    {
        // (AreaWeightedQuadrics off: the error is a squared distance, which scales with the mesh)
        static void Si(Reducer r) { r.PreventSelfIntersections = true; r.AreaWeightedQuadrics = false; }

        // closed bumpy sphere, about 4 k triangles per 18 vertices along an edge of the cube (enough for 8 slabs)
        static DMesh3 Sphere(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        // open n x n heightfield (its boundary is not kept by the partitioned path)
        static DMesh3 Terrain(int n)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.08 * Math.Sin(6 * x) * Math.Cos(5 * y) + 0.01 * Math.Sin(40 * x + 13 * y)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        static double MaxError(DMesh3 m, double rel) { double d = rel * m.CachedBounds.DiagonalLength; return d * d; }

        static PartitionedReducer Make(int threads = 0) => new PartitionedReducer() { MaxThreads = threads, MinTriangles = 0, Configure = Si };

        [Fact]
        public void SameResult_AnyThreadCount_AndRepeated()
        {
            DMesh3 m = Sphere(30);
            double e = MaxError(m, 0.004);
            DMesh3 reference = null;
            foreach (int threads in new[] { 1, 2, 4, 8, 3, 1 }) {
                var pr = Make(threads);
                DMesh3 r = pr.Reduce(m, e);
                Assert.True(pr.UsedPartition);
                if (reference == null) reference = r;
                else Assert.Equal("", Invariance.Difference(reference, r));
            }
            Assert.True(reference.TriangleCount < m.TriangleCount / 3, $"{reference.TriangleCount} of {m.TriangleCount}");
        }

        // the public class is PartitionedReduction on 8 slabs (the argument and its tests are there)
        [Fact]
        public void IsPartitionedReductionOnEightSlabs()
        {
            DMesh3 m = Sphere(30);
            double e = MaxError(m, 0.004);
            Action<Reducer> configure = (r) => { Si(r); r.PreserveBoundaryShape = false; };
            DMesh3 expected = new PartitionedReduction() { Slabs = 8, Configure = configure }.Reduce(m, e);
            Assert.Equal("", Invariance.Difference(new DMesh3(expected, true, MeshComponents.None), Make().Reduce(m, e)));
            DMesh3 other = new PartitionedReduction() { Slabs = 5, Configure = configure }.Reduce(m, e);
            Assert.NotEqual("", Invariance.Difference(new DMesh3(other, true, MeshComponents.None), Make().Reduce(m, e)));
        }

        [Fact]
        public void Reuse_SameInstanceTwice_SameResult_InputUntouched()
        {
            DMesh3 m = Sphere(24);
            DMesh3 before = new DMesh3(m);
            double e = MaxError(m, 0.004);
            var pr = Make();
            DMesh3 a = pr.Reduce(m, e), b = pr.Reduce(m, e);
            Assert.Equal("", Invariance.Difference(a, b));
            Assert.Equal("", Invariance.Difference(before, m));
            Assert.NotSame(m, a);
        }

        [Fact]
        public void Guarantees_ClosedAndFreeOfSelfIntersections()
        {
            DMesh3 m = Sphere(30);
            DMesh3 r = Make().Reduce(m, MaxError(m, 0.004));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true));
            Assert.True(r.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
            // near the serial result in size and shape
            DMesh3 s = new DMesh3(m);
            var red = new Reducer(s) { PreventSelfIntersections = true, AreaWeightedQuadrics = false };
            red.ReduceToMaxQuadricError(MaxError(m, 0.004));
            Assert.InRange(r.TriangleCount, 0.8 * s.TriangleCount, 1.25 * s.TriangleCount);
            var hd = new MeshHausdorffDistance(m, r) { Symmetric = true, Tolerance = 0.0005 * m.CachedBounds.DiagonalLength };
            Assert.True(hd.Compute());
            Assert.True(hd.UpperBound < 0.02 * m.CachedBounds.DiagonalLength, hd.UpperBound.ToString());
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void InvariantUnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = Sphere(24);
            double e = MaxError(m, 0.004);
            DMesh3 reference = Make().Reduce(m, e);
            DMesh3 input = Invariance.Transformed(m, sim);
            double l = sim.ApplyLength(Math.Sqrt(e));
            DMesh3 r = Make().Reduce(input, l * l);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true));
            if (sim.IsExact)
                Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(r, sim)));
            else
                Assert.InRange(r.TriangleCount, 0.9 * reference.TriangleCount, 1.1 * reference.TriangleCount);
        }

        [Fact]
        public void SmallMeshes_FallBackToOneSerialRun_ByDefault()
        {
            DMesh3 m = Sphere(18);
            Assert.True(m.TriangleCount < 20000);
            double e = MaxError(m, 0.004);
            var pr = new PartitionedReducer() { Configure = Si };
            DMesh3 r = pr.Reduce(m, e);
            Assert.False(pr.UsedPartition);
            DMesh3 s = new DMesh3(m);
            var red = new Reducer(s) { PreventSelfIntersections = true, AreaWeightedQuadrics = false, PreserveBoundaryShape = false };
            red.ReduceToMaxQuadricError(e);
            Assert.Equal("", Invariance.Difference(new DMesh3(s, true, MeshComponents.None), r));
            // the threshold is the setting
            pr.MinTriangles = m.TriangleCount;
            pr.Reduce(m, e);
            Assert.True(pr.UsedPartition);
            pr.MinTriangles = m.TriangleCount + 1;
            pr.Reduce(m, e);
            Assert.False(pr.UsedPartition);
        }

        [Fact]
        public void SlabsThatCannotBeKeptApart_FallBackToOneSerialRun()
        {
            DMesh3 m = Sphere(24);
            double e = MaxError(m, 0.004);
            var pr = Make();
            pr.GapEdgeLengths = 0;
            DMesh3 r = pr.Reduce(m, e);
            Assert.False(pr.UsedPartition);
            DMesh3 s = new DMesh3(m);
            var red = new Reducer(s) { PreventSelfIntersections = true, AreaWeightedQuadrics = false, PreserveBoundaryShape = false };
            red.ReduceToMaxQuadricError(e);
            Assert.Equal("", Invariance.Difference(new DMesh3(s, true, MeshComponents.None), r));
            Assert.True(Make().Reduce(m, e).TriangleCount > 0);     // and the partition again on the next call of a fresh instance
        }

        [Fact]
        public void ResultHasNoAttributes_AlsoOnTheFallback()
        {
            DMesh3 m = Sphere(24);
            m.EnableVertexNormals(Vector3f.AxisY); m.EnableVertexColors(Vector3f.One); m.EnableVertexUVs(Vector2f.Zero); m.EnableTriangleGroups(3);
            foreach (int minTriangles in new[] { 0, 1000000 }) {
                DMesh3 r = new PartitionedReducer() { MinTriangles = minTriangles, Configure = Si }.Reduce(m, MaxError(m, 0.004));
                Assert.False(r.HasVertexNormals || r.HasVertexColors || r.HasVertexUVs || r.HasTriangleGroups, $"MinTriangles {minTriangles}");
                Assert.True(m.HasVertexNormals);       // the input keeps its own
            }
        }

        [Fact]
        public void OpenMesh_BoundaryIsNotPreserved_ButMeshIsValid()
        {
            DMesh3 m = Terrain(110);
            var pr = new PartitionedReducer() { MinTriangles = 0, Configure = Si };
            DMesh3 r = pr.Reduce(m, MaxError(m, 0.003));
            Assert.True(pr.UsedPartition);
            Assert.True(r.TriangleCount < m.TriangleCount / 2);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true).Replace("not closed", ""));      // an open mesh is open
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
            Assert.True(r.BoundaryEdgeIndices().Any());
        }

        [Fact]
        public void SerialFallback_OnAnOpenMesh_DoesNotPreserveTheBoundaryShape()
        {
            DMesh3 m = Terrain(60);
            double e = MaxError(m, 0.003);
            DMesh3 r = new PartitionedReducer() { MinTriangles = 1000000, Configure = Si }.Reduce(m, e);
            DMesh3 s = new DMesh3(m);
            new Reducer(s) { PreventSelfIntersections = true, AreaWeightedQuadrics = false, PreserveBoundaryShape = false }.ReduceToMaxQuadricError(e);
            Assert.Equal("", Invariance.Difference(new DMesh3(s, true, MeshComponents.None), r));
            DMesh3 kept = new DMesh3(m);
            new Reducer(kept) { PreventSelfIntersections = true, AreaWeightedQuadrics = false }.ReduceToMaxQuadricError(e);
            Assert.NotEqual(kept.TriangleCount, r.TriangleCount);       // the setting matters on this mesh
        }

        [Fact]
        public void Cancel_ReturnsTheMeshSoFar_AndCallsFromSeveralThreads()
        {
            DMesh3 m = Sphere(30);
            var threads = new HashSet<int>();
            int calls = 0;
            var pr = Make();
            pr.CancelF = () => { lock (threads) { threads.Add(Thread.CurrentThread.ManagedThreadId); calls++; } return true; };
            DMesh3 r = pr.Reduce(m, MaxError(m, 0.004));
            Assert.True(calls > 0);
            Assert.True(r.TriangleCount > 0 && r.TriangleCount <= m.TriangleCount);
            // cancelled before any collapse: Progress reaches the reducers of the parts, so almost nothing is reduced
            Assert.True(r.TriangleCount >= 0.9 * m.TriangleCount, $"{r.TriangleCount} of {m.TriangleCount}");
            Assert.True(r.IsCompact);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
            // not cancelled: the full reduction is much smaller
            Assert.True(Make().Reduce(m, MaxError(m, 0.004)).TriangleCount < 0.5 * r.TriangleCount);
        }

        [Fact]
        public void ExceptionInAPart_SurfacesAsAggregateException_SerialFallbackThrowsTheOriginal()
        {
            DMesh3 m = Sphere(24);
            int calls = 0;
            // (the 1st call is the scratch reducer of the option check, the 2nd the quadrics on the calling thread, then the parts)
            Action<Reducer> throwing = (r) => { if (Interlocked.Increment(ref calls) > 2) throw new InvalidOperationException("part"); };
            foreach (int threads in new[] { 0, 1, 4 }) {
                calls = 0;
                var pr = new PartitionedReducer() { MinTriangles = 0, MaxThreads = threads, Configure = throwing };
                var ex = Assert.Throws<AggregateException>(() => pr.Reduce(m, MaxError(m, 0.004)));
                Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
                Assert.False(pr.UsedPartition);
            }
            // thrown by Configure on the calling thread while the quadrics are computed: the same type on the partition path
            calls = 1;
            Assert.Throws<AggregateException>(() => new PartitionedReducer() { MinTriangles = 0, Configure = throwing }.Reduce(m, MaxError(m, 0.004)));
            calls = 0;
            var serial = new PartitionedReducer() { MinTriangles = 1000000, Configure = (r) => throw new InvalidOperationException("serial") };
            Assert.Throws<InvalidOperationException>(() => serial.Reduce(m, 1));
        }

        // options the partition cannot honour (each part would take its own mesh as the deviation reference; constraints and
        // projection targets refer to vertex ids of other meshes): refused up front, on every path
        [Theory]
        [InlineData(0)]
        [InlineData(1000000)]
        public void MaxDeviationAndConstraintsInConfigure_AreRefused(int minTriangles)
        {
            DMesh3 m = Sphere(24);
            var pr = new PartitionedReducer() { MinTriangles = minTriangles, Configure = r => r.MaxDeviation = 0.01 };
            Assert.Throws<ArgumentException>(() => pr.Reduce(m, 1));
            pr.Configure = r => r.SetExternalConstraints(new MeshConstraints());
            Assert.Throws<ArgumentException>(() => pr.Reduce(m, 1));
            pr.Configure = Si;
            Assert.True(pr.Reduce(m, MaxError(m, 0.004)).TriangleCount > 0);
        }

        // Configure cannot undo the contract: PreserveBoundaryShape and Progress are set after it
        [Fact]
        public void ConfigureCannotRestoreTheBoundaryShape_OrReplaceTheCancel()
        {
            DMesh3 m = Terrain(110);
            double e = MaxError(m, 0.003);
            DMesh3 a = new PartitionedReducer() { MinTriangles = 0, Configure = Si }.Reduce(m, e);
            DMesh3 b = new PartitionedReducer() { MinTriangles = 0, Configure = r => { Si(r); r.PreserveBoundaryShape = true; } }.Reduce(m, e);
            Assert.Equal("", Invariance.Difference(a, b));
            DMesh3 c = new PartitionedReducer() { MinTriangles = 0, CancelF = () => true, Configure = r => { Si(r); r.Progress = new ProgressCancel(() => false); } }.Reduce(m, e);
            Assert.True(c.TriangleCount >= 0.9 * m.TriangleCount, $"{c.TriangleCount} of {m.TriangleCount}");
        }

        // vertices that no triangle uses are not in the result, and do not steer the slabs (they were in the bounds and the cuts)
        [Theory]
        [InlineData(0)]
        [InlineData(1000000)]
        public void IsolatedVertices_AreDropped_AndDoNotChangeTheResult(int minTriangles)
        {
            DMesh3 m = Sphere(24);
            double e = MaxError(m, 0.004);
            DMesh3 withIsolated = new DMesh3(m);
            withIsolated.AppendVertex(new Vector3d(0.1, 100, 0.2));
            DMesh3 a = new PartitionedReducer() { MinTriangles = minTriangles, Configure = Si }.Reduce(m, e);
            DMesh3 b = new PartitionedReducer() { MinTriangles = minTriangles, Configure = Si }.Reduce(withIsolated, e);
            Assert.Equal("", Invariance.Difference(a, b));
            Assert.Equal(a.VertexCount, a.VertexIndices().Count(v => a.GetVtxEdgeCount(v) > 0));
        }

        [Fact]
        public void UsedPartition_IsResetByEveryCall()
        {
            DMesh3 m = Sphere(24);
            var pr = Make();
            pr.Reduce(m, MaxError(m, 0.004));
            Assert.True(pr.UsedPartition);
            pr.Reduce(new DMesh3(), 1);
            Assert.False(pr.UsedPartition);
            pr.Reduce(m, MaxError(m, 0.004));
            Assert.True(pr.UsedPartition);
            Assert.Throws<ArgumentOutOfRangeException>(() => pr.Reduce(m, -1));
            pr.Configure = r => { throw new InvalidOperationException(); };
            Assert.Throws<InvalidOperationException>(() => pr.Reduce(m, 1));
            Assert.False(pr.UsedPartition);
        }

        [Fact]
        public void Arguments_AreChecked_EmptyMeshAndZeroError()
        {
            var pr = Make();
            Assert.Throws<ArgumentNullException>(() => pr.Reduce(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pr.Reduce(new DMesh3(), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pr.Reduce(new DMesh3(), double.NaN));
            Assert.Equal(0, pr.Reduce(new DMesh3(), 1).TriangleCount);
            var onlyVertices = new DMesh3();
            onlyVertices.AppendVertex(new Vector3d(1, 2, 3));
            Assert.Equal(0, pr.Reduce(onlyVertices, 1).VertexCount);      // isolated vertices are dropped, also without triangles
            Assert.False(pr.UsedPartition);
            DMesh3 m = Sphere(24);
            DMesh3 r = pr.Reduce(m, 0);       // error 0: only collapses that change nothing; valid, not larger
            Assert.True(r.TriangleCount <= m.TriangleCount && r.TriangleCount > 0);
            Assert.Equal(8, PartitionedReducer.Slabs);
        }
    }
}
