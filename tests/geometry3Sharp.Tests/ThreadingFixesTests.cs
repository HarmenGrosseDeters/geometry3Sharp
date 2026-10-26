using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Race conditions from the class-by-class review (docs/Analyse-2026-10.md) and upstream issue #72.
    /// A race only shows up when threads actually overlap, so every test starts its threads together
    /// on a Barrier and repeats the situation many times; the expected results come from a serial run.
    /// </summary>
    public class ThreadingFixesTests
    {
        static DMesh3 Sphere(int edgeVertices)
        {
            var gen = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices };
            return gen.Generate().MakeDMesh();
        }

        static Vector3d[] QueryPoints(int count, int seed)
        {
            var rand = new Random(seed);
            return Enumerable.Range(0, count).Select(i => new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 3 - 1.5 * Vector3d.One).ToArray();
        }

        /// <summary>
        /// Runs work(threadIndex) on nThreads threads that are released at the same moment.
        /// Returns the exceptions thrown on the threads.
        /// </summary>
        static List<Exception> RunTogether(int nThreads, Action<int> work)
        {
            var barrier = new Barrier(nThreads);
            var errors = new List<Exception>();
            var threads = Enumerable.Range(0, nThreads).Select(t => new Thread(() => {
                try {
                    barrier.SignalAndWait();
                    work(t);
                } catch (Exception e) {
                    lock (errors) errors.Add(e);
                }
            })).ToList();
            threads.ForEach(th => th.Start());
            threads.ForEach(th => th.Join());
            return errors;
        }

        // --- DMeshAABBTree3 / PointAABBTree3 fast winding number cache (upstream #72) ---

        [Fact]
        public void DMeshAABBTree_FastWindingNumber_FirstCallsFromManyThreads()
        {
            DMesh3 mesh = Sphere(24);
            Vector3d[] points = QueryPoints(64, 72);
            var serialTree = new DMeshAABBTree3(mesh, true);
            double[] expected = points.Select(p => serialTree.FastWindingNumber(p)).ToArray();

            int nThreads = Math.Max(4, Environment.ProcessorCount);
            var failures = new List<string>();
            for (int trial = 0; trial < 50; ++trial) {
                // a fresh tree each time, so the cache is built by the concurrent calls
                var tree = new DMeshAABBTree3(mesh, true);
                var wrong = new int[nThreads];
                List<Exception> errors = RunTogether(nThreads, (t) => {
                    for (int k = 0; k < points.Length; ++k) {
                        int i = (k + 7 * t) % points.Length;
                        if (Math.Abs(tree.FastWindingNumber(points[i]) - expected[i]) > 1e-9)
                            wrong[t]++;
                    }
                });
                if (errors.Count > 0 || wrong.Sum() > 0)
                    failures.Add($"trial {trial}: {errors.Count} exceptions ({string.Join("; ", errors.Select(e => e.GetType().Name).Distinct())}), {wrong.Sum()} wrong values");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void PointAABBTree_FastWindingNumber_FirstCallsFromManyThreads()
        {
            DMesh3 mesh = Sphere(24);
            MeshNormals.QuickCompute(mesh);
            Vector3d[] points = QueryPoints(64, 73);
            Func<int, double> areaF = (vid) => 4 * Math.PI / mesh.VertexCount;
            var serialTree = new PointAABBTree3(mesh, true) { FWNAreaEstimateF = areaF };
            double[] expected = points.Select(p => serialTree.FastWindingNumber(p)).ToArray();

            int nThreads = Math.Max(4, Environment.ProcessorCount);
            var failures = new List<string>();
            for (int trial = 0; trial < 50; ++trial) {
                var tree = new PointAABBTree3(mesh, true) { FWNAreaEstimateF = areaF };
                var wrong = new int[nThreads];
                List<Exception> errors = RunTogether(nThreads, (t) => {
                    for (int k = 0; k < points.Length; ++k) {
                        int i = (k + 7 * t) % points.Length;
                        if (Math.Abs(tree.FastWindingNumber(points[i]) - expected[i]) > 1e-9)
                            wrong[t]++;
                    }
                });
                if (errors.Count > 0 || wrong.Sum() > 0)
                    failures.Add($"trial {trial}: {errors.Count} exceptions ({string.Join("; ", errors.Select(e => e.GetType().Name).Distinct())}), {wrong.Sum()} wrong values");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
        // --- Remesher / RemesherPro: vModifiedV was a BitArray written from parallel loops ---

        /// <summary> sphere with every vertex moved radially by up to +-2%, so smoothing moves every vertex </summary>
        static DMesh3 NoisySphere(int edgeVertices, int seed)
        {
            DMesh3 mesh = Sphere(edgeVertices);
            var rand = new Random(seed);
            foreach (int vid in mesh.VertexIndices().ToList())
                mesh.SetVertex(vid, mesh.GetVertex(vid) * (1 + 0.02 * (rand.NextDouble() * 2 - 1)));
            return mesh;
        }

        static double MaxVertexDistance(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            return a.VertexIndices().Max(vid => a.GetVertex(vid).Distance(b.GetVertex(vid)));
        }

        static void SmoothOnly(Remesher r)
        {
            r.EnableFlips = r.EnableCollapses = r.EnableSplits = false;
            r.EnableSmoothing = true;
            r.SmoothSpeedT = 0.5;
        }

        [Fact]
        public void Remesher_ParallelSmoothPass_MatchesSerial()
        {
            DMesh3 input = NoisySphere(130, 1);     // ~100k vertices
            DMesh3 serial = new DMesh3(input), parallel = new DMesh3(input);
            var rs = new Remesher(serial) { EnableParallelSmooth = false };
            var rp = new Remesher(parallel) { EnableParallelSmooth = true };
            SmoothOnly(rs); SmoothOnly(rp);
            for (int pass = 0; pass < 5; ++pass) {
                rs.BasicRemeshPass();
                rp.BasicRemeshPass();
            }
            Assert.True(MaxVertexDistance(serial, input) > 1e-3, "smoothing did not move the vertices");
            Assert.True(MaxVertexDistance(serial, parallel) < 1e-12, $"parallel smoothing differs by {MaxVertexDistance(serial, parallel)}");
        }

        [Fact]
        public void RemesherPro_ParallelSmoothAndProjectionPasses_MatchSerial()
        {
            DMesh3 input = NoisySphere(130, 2);
            DMesh3 target = Sphere(40);
            DMesh3 serial = new DMesh3(input), parallel = new DMesh3(input);
            var rs = new RemesherPro(serial) { EnableParallelSmooth = false, EnableParallelProjection = false };
            var rp = new RemesherPro(parallel) { EnableParallelSmooth = true, EnableParallelProjection = true };
            foreach (RemesherPro r in new[] { rs, rp }) {
                SmoothOnly(r);
                r.SetProjectionTarget(MeshProjectionTarget.Auto(target));
            }
            for (int pass = 0; pass < 5; ++pass) {
                rs.RemeshIteration();
                rp.RemeshIteration();
            }
            Assert.True(MaxVertexDistance(serial, input) > 1e-3, "remeshing did not move the vertices");
            Assert.True(MaxVertexDistance(serial, parallel) < 1e-9, $"parallel passes differ by {MaxVertexDistance(serial, parallel)}");
        }

        [Fact]
        public void RemesherPro_FaceAlignedProjection_IsDeterministic()
        {
            // TrackedFaceProjectionPass has no serial variant, so run it repeatedly on the same input.
            // The results must be identical: the pass used to sum per-vertex contributions in thread
            // order, which changed the last bits every run and now and then flipped its 'vertex did
            // not move' threshold, leaving a vertex ~1e-6 off.
            DMesh3 input = NoisySphere(130, 3);
            DMesh3 target = Sphere(40);
            DMesh3 reference = null;
            var failures = new List<string>();
            for (int run = 0; run < 4; ++run) {
                DMesh3 mesh = new DMesh3(input);
                var r = new RemesherPro(mesh) { UseFaceAlignedProjection = true };
                SmoothOnly(r);
                r.SmoothSpeedT = 0;
                r.SetProjectionTarget(MeshProjectionTarget.Auto(target));
                for (int pass = 0; pass < 3; ++pass)
                    r.RemeshIteration();
                if (reference == null)
                    reference = mesh;
                else if (MaxVertexDistance(reference, mesh) > 0)
                    failures.Add($"run {run} differs from run 0 by {MaxVertexDistance(reference, mesh)}");
            }
            Assert.True(MaxVertexDistance(reference, input) > 1e-3, "projection did not move the vertices");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // --- RemoveOccludedTriangles.PerVertex: occlusion flags were a BitArray written in parallel ---

        [Fact]
        public void RemoveOccludedTriangles_PerVertex_RemovesEveryOccludedTriangle()
        {
            // a coarse sphere keeps each occlusion test cheap, so the parallel writes come close together
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { EdgeVertices = 3, Radius = 10 }.Generate().MakeDMesh();
            var sphereTree = new DMeshAABBTree3(sphere, true);
            const int N = 100000;
            var failures = new List<string>();
            for (int run = 0; run < 20; ++run) {
                // N triangles with one vertex inside the sphere and two outside: all are occluded
                DMesh3 mesh = new DMesh3(sphere);
                int sphereT = mesh.TriangleCount;
                var rand = new Random(run);
                for (int i = 0; i < N; ++i) {
                    int a = mesh.AppendVertex(new Vector3d(rand.NextDouble() * 5 - 2.5, 0, 0));
                    int b = mesh.AppendVertex(new Vector3d(20 + i * 1e-3, 0, 0));
                    int c = mesh.AppendVertex(new Vector3d(20 + i * 1e-3, 1, 0));
                    mesh.AppendTriangle(a, b, c);
                }
                var occ = new RemoveOccludedTriangles(mesh, sphereTree) {
                    PerVertex = true, InsideMode = RemoveOccludedTriangles.CalculationMode.FastWindingNumber };
                Assert.True(occ.Apply());
                int removed = occ.RemovedT.Count(t => t >= sphereT);
                if (removed != N)
                    failures.Add($"run {run}: removed {removed} of {N}");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void SignedDistanceGrid_ParallelEqualsSerial()
        {
            // a grid point at equal distance from two triangles (eg across a shared edge) kept the triangle whose thread
            // came first; the sweep then propagates distances from that triangle, so the grid varied between runs
            DMesh3 mesh = Sphere(14);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                mesh.SetVertex(vid, p * (1 + 0.15 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5)));
            }
            foreach (var mode in new[] { MeshSignedDistanceGrid.ComputeModes.NarrowBandOnly, MeshSignedDistanceGrid.ComputeModes.FullGrid }) {
                Func<bool, MeshSignedDistanceGrid> run = (parallel) => {
                    var sdf = new MeshSignedDistanceGrid(mesh, 0.12) { UseParallel = parallel, ComputeMode = mode, WantClosestTriGrid = true };
                    sdf.Compute();
                    return sdf;
                };
                MeshSignedDistanceGrid serial = run(false);
                for (int k = 0; k < 4; ++k) {
                    MeshSignedDistanceGrid parallel = run(true);
                    Assert.True(serial.Grid.Buffer.SequenceEqual(parallel.Grid.Buffer), mode + ", parallel run " + k);
                    // (in the narrow band only, a tie changes the closest triangle but not the distance)
                    Assert.True(serial.ClosestTriGrid.Buffer.SequenceEqual(parallel.ClosestTriGrid.Buffer), mode + ", closest triangles, parallel run " + k);
                }
            }
        }
    }
}
