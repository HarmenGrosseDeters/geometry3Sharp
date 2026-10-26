using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Items the parallel fix rounds left as "needs a decision" (docs/Analyse-2026-10.md), plus the exact
    /// winding-number cache race found while fixing spatial/.
    /// </summary>
    public class DesignDecisionFixesTests
    {
        static DMesh3 Sphere(Vector3d center, int edgeVertices = 12)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        // --- MeshBoolean: the union was returned open and unwelded, with Compute() always true ---

        [Fact]
        public void MeshBoolean_Union_IsClosedAndValid()
        {
            var op = new MeshBoolean() {
                Target = Sphere(Vector3d.Zero),
                Tool = Sphere(new Vector3d(0.7, 0.13, 0.05))
            };
            Assert.True(op.Compute());
            Assert.True(op.Result.IsClosed(), $"{MeshIterators.BoundaryEdges(op.Result).Count()} boundary edges");
            Assert.True(op.Result.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(1, new MeshConnectedComponents(op.Result).Let(c => { c.FindConnectedT(); return c.Count; }));
        }

        // --- MergeCoincidentEdges: "growing" the point buffer kept its size, so > 1024 hits looped forever ---

        [Fact]
        public void MergeCoincidentEdges_ManyCoincidentEdges_Terminates()
        {
            var mesh = new DMesh3();
            for (int i = 0; i < 1100; ++i)
                mesh.AppendTriangle(mesh.AppendVertex(Vector3d.Zero), mesh.AppendVertex(Vector3d.AxisX), mesh.AppendVertex(Vector3d.AxisY));
            var merge = new MergeCoincidentEdges(mesh);
            Exception error = null;
            var worker = new Thread(() => { try { merge.Apply(); } catch (Exception e) { error = e; } }) { IsBackground = true };
            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(20)), "MergeCoincidentEdges.Apply did not terminate");
            // it must also finish without an exception and leave a valid mesh
            Assert.Null(error);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
        }

        // --- Colorf: new Colorf(1,0,0) silently takes the byte constructor ---

        [Fact]
        public void Colorf_IntConstructorIsObsolete_AndFromBytesExists()
        {
            ConstructorInfo intCtor = typeof(Colorf).GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
            Assert.NotNull(intCtor.GetCustomAttribute<ObsoleteAttribute>());
            MethodInfo fromBytes = typeof(Colorf).GetMethod("FromBytes", new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
            Assert.NotNull(fromBytes);
            var red = (Colorf)fromBytes.Invoke(null, new object[] { 255, 0, 0, 255 });
            Assert.Equal(new Colorf(1f, 0f, 0f, 1f), red);
        }

        [Fact]
        public void Colorf_PredefinedColorsUnchanged()
        {
            Assert.Equal(new Colorf(1f, 0f, 0f, 1f), Colorf.Red);
            Assert.Equal(new Colorf(1f, 1f, 1f, 0f), Colorf.TransparentWhite);
            Assert.Equal(16f / 255f, Colorf.VideoBlack.r, 6);
        }

        // --- DVector.Clear: zero-fills but keeps the length ---

        [Fact]
        public void DVector_ClearIsObsolete_AndFillExists()
        {
            Assert.NotNull(typeof(DVector<int>).GetMethod("Clear", Type.EmptyTypes).GetCustomAttribute<ObsoleteAttribute>());
            MethodInfo fill = typeof(DVector<int>).GetMethod("Fill", new[] { typeof(int) });
            Assert.NotNull(fill);
            var v = new DVector<int>();
            for (int i = 0; i < 5000; ++i) v.Add(i);
            fill.Invoke(v, new object[] { 7 });
            Assert.Equal(5000, v.Length);
            Assert.True(Enumerable.Range(0, 5000).All(i => v[i] == 7));
        }

        // --- MeshScalarSamplingGrid: filled cells must stay on their side of IsoValue ---

        [Fact]
        public void MeshScalarSamplingGrid_SignedFunction_FilledCellsKeepTheirSide()
        {
            // a signed distance (negative inside) with IsoValue 0: the old fill set every value below
            // IsoValue to 0, which is the iso value itself, so the whole interior lost its sign
            DMesh3 mesh = Sphere(Vector3d.Zero, 16);
            Func<Vector3d, double> sdf = (p) => p.Length - 1.0;
            var grid = new MeshScalarSamplingGrid(mesh, 0.1, sdf) { IsoValue = 0 };
            grid.Compute();
            int checkedCells = 0, wrong = 0;
            DenseGrid3f g = grid.Grid;
            for (int k = 0; k < g.nk; ++k)
                for (int j = 0; j < g.nj; ++j)
                    for (int i = 0; i < g.ni; ++i) {
                        Vector3d p = new Vector3d(i, j, k) * grid.CellSize + (Vector3d)grid.GridOrigin;
                        double f = sdf(p);
                        if (Math.Abs(f) < 0.3)
                            continue;
                        checkedCells++;
                        if (Math.Sign(g[i, j, k]) != Math.Sign(f))
                            wrong++;
                    }
            Assert.True(checkedCells > 1000);
            Assert.True(wrong == 0, $"{wrong} of {checkedCells} cells on the wrong side of the iso value");
        }

        [Fact]
        public void MeshScalarSamplingGrid_WindingNumber_StillWorks()
        {
            // the default use: winding number with IsoValue 0.5, inside ~1, outside ~0
            DMesh3 mesh = Sphere(Vector3d.Zero, 16);
            var tree = new DMeshAABBTree3(mesh, true);
            tree.FastWindingNumber(Vector3d.Zero);
            var grid = new MeshScalarSamplingGrid(mesh, 0.1, (p) => tree.FastWindingNumber(p));
            grid.Compute();
            DenseGrid3f g = grid.Grid;
            for (int k = 0; k < g.nk; ++k)
                for (int j = 0; j < g.nj; ++j)
                    for (int i = 0; i < g.ni; ++i) {
                        Vector3d p = new Vector3d(i, j, k) * grid.CellSize + (Vector3d)grid.GridOrigin;
                        double r = p.Length;
                        if (r < 0.7) Assert.True(g[i, j, k] > 0.5f, $"inside cell {i},{j},{k}: {g[i, j, k]}");
                        if (r > 1.3) Assert.True(g[i, j, k] < 0.5f, $"outside cell {i},{j},{k}: {g[i, j, k]}");
                    }
        }

        // --- DMeshAABBTree3.WindingNumber: the exact winding cache was built without a lock ---

        [Fact]
        public void DMeshAABBTree_WindingNumber_FirstCallsFromManyThreads()
        {
            DMesh3 mesh = Sphere(Vector3d.Zero, 24);
            var rand = new Random(31);
            Vector3d[] points = Enumerable.Range(0, 48).Select(i => new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 3 - 1.5 * Vector3d.One).ToArray();
            var serial = new DMeshAABBTree3(mesh, true);
            double[] expected = points.Select(p => serial.WindingNumber(p)).ToArray();

            int nThreads = Math.Max(4, Environment.ProcessorCount);
            var failures = new List<string>();
            for (int trial = 0; trial < 40; ++trial) {
                var tree = new DMeshAABBTree3(mesh, true);
                var wrong = new int[nThreads];
                var errors = new List<Exception>();
                var barrier = new Barrier(nThreads);
                var threads = Enumerable.Range(0, nThreads).Select(t => new Thread(() => {
                    try {
                        barrier.SignalAndWait();
                        for (int n = 0; n < points.Length; ++n) {
                            int i = (n + 5 * t) % points.Length;
                            if (Math.Abs(tree.WindingNumber(points[i]) - expected[i]) > 1e-9)
                                wrong[t]++;
                        }
                    } catch (Exception e) {
                        lock (errors) errors.Add(e);
                    }
                })).ToList();
                threads.ForEach(th => th.Start());
                threads.ForEach(th => th.Join());
                if (errors.Count > 0 || wrong.Sum() > 0)
                    failures.Add($"trial {trial}: {errors.Count} exceptions ({string.Join("; ", errors.Select(e => e.GetType().Name).Distinct())}), {wrong.Sum()} wrong values");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
    }

    /// <summary>
    /// ReadOptions.Defaults was one shared, mutable instance: setting a field on it changed the defaults of
    /// every later read. These tests mutate it, so they run alone (on the old code the change is global).
    /// </summary>
    [Collection("ReadOptionsDefaults")]
    public class ReadOptionsDefaultsTests
    {
        [Fact]
        public void Defaults_IsNotShared()
        {
            ReadOptions d = ReadOptions.Defaults;
            try {
                d.ReadMaterials = true;
                d.CustomFlags.Flags["flag"] = true;
                Assert.False(ReadOptions.Defaults.ReadMaterials);
                Assert.Empty(ReadOptions.Defaults.CustomFlags.Flags);
                Assert.NotSame(ReadOptions.Defaults, ReadOptions.Defaults);
            } finally {
                d.ReadMaterials = false;
                d.CustomFlags = new CommandArgumentSet();
            }
        }
    }

    [CollectionDefinition("ReadOptionsDefaults", DisableParallelization = true)]
    public class ReadOptionsDefaultsCollection { }

    static class TestFunctional
    {
        public static R Let<T, R>(this T x, Func<T, R> f) { return f(x); }
    }
}
