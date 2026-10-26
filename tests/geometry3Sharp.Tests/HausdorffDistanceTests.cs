using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Certified (Pompeiu-)Hausdorff distance between triangle meshes: the result is an interval
    /// [LowerBound, UpperBound] that contains the exact distance and is narrower than the tolerance.
    /// </summary>
    public class HausdorffDistanceTests
    {
        // grid over [x0,x1] x [y0,y1] with n x n vertices, z = h(x,y)
        static DMesh3 Grid(double x0, double y0, double x1, double y1, int n, Func<double, double, double> h = null)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = x0 + (x1 - x0) * i / (n - 1), y = y0 + (y1 - y0) * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, h == null ? 0 : h(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        static DMesh3 Triangle(Vector3d a, Vector3d b, Vector3d c)
        {
            DMesh3 m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
            return m;
        }

        static DMesh3 Soup(DMesh3 m)
        {
            DMesh3 soup = new DMesh3();
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                soup.AppendTriangle(soup.AppendVertex(m.GetVertex(t.a)), soup.AppendVertex(m.GetVertex(t.b)), soup.AppendVertex(m.GetVertex(t.c)));
            }
            return soup;
        }

        // grid plane z=0 over [-10,10]^2 with the square hole [-2,2]^2
        static DMesh3 PlaneWithHole()
        {
            DMesh3 b = Grid(-10, -10, 10, 10, 21);
            foreach (int tid in b.TriangleIndices().ToArray()) {
                Vector3d c = b.GetTriCentroid(tid);
                if (Math.Abs(c.x) < 2 && Math.Abs(c.y) < 2)
                    b.RemoveTriangle(tid, true, false);
            }
            return b;
        }

        static double DistanceToMesh(DMeshAABBTree3 spatial, Vector3d p)
        {
            int tid = spatial.FindNearestTriangle(p);
            return Math.Sqrt(MeshQueries.TriDistanceSqr(spatial.Mesh, tid, p));
        }

        // one-sided distance estimated by sampling (as MeshFunctions.Compare does: vertices and points on edges)
        static double SampledVerticesAndEdges(DMesh3 a, DMesh3 b)
        {
            var spatial = new DMeshAABBTree3(b, true);
            double max = 0;
            foreach (int vid in a.VertexIndices())
                max = Math.Max(max, DistanceToMesh(spatial, a.GetVertex(vid)));
            foreach (int eid in a.EdgeIndices()) {
                Index2i ev = a.GetEdgeV(eid);
                for (int k = 1; k <= 3; ++k)
                    max = Math.Max(max, DistanceToMesh(spatial, Vector3d.Lerp(a.GetVertex(ev.a), a.GetVertex(ev.b), k / 4.0)));
            }
            return max;
        }

        // dense barycentric sampling of every triangle of a: a lower bound of the one-sided distance
        static double SampledDense(DMesh3 a, DMesh3 b, int n)
        {
            var spatial = new DMeshAABBTree3(b, true);
            double max = 0;
            foreach (int tid in a.TriangleIndices()) {
                Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero, p2 = Vector3d.Zero;
                a.GetTriVertices(tid, ref p0, ref p1, ref p2);
                for (int i = 0; i <= n; ++i)
                    for (int j = 0; i + j <= n; ++j) {
                        double u = (double)i / n, v = (double)j / n;
                        max = Math.Max(max, DistanceToMesh(spatial, p0 + u * (p1 - p0) + v * (p2 - p0)));
                    }
            }
            return max;
        }

        static void AssertCertified(MeshHausdorffDistance h, double exact, double tol)
        {
            Assert.True(h.Converged, "did not converge");
            Assert.True(h.UpperBound - h.LowerBound <= tol + 1e-12, $"interval [{h.LowerBound}, {h.UpperBound}] wider than {tol}");
            // 1e-7: rounding of the point-triangle distances
            Assert.True(h.LowerBound <= exact + 1e-7 && exact <= h.UpperBound + 1e-7, $"{exact} not in [{h.LowerBound}, {h.UpperBound}]");
        }


        [Fact]
        public void IdenticalMeshes_Zero()
        {
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            var h = new MeshHausdorffDistance(a, new DMesh3(a)) { Tolerance = 1e-6 };
            Assert.True(h.Compute());
            AssertCertified(h, 0, 1e-6);
        }

        [Fact]
        public void IsolatedVertices_AreNotOnTheSurface()
        {
            DMesh3 a = Grid(0, 0, 10, 10, 11);
            DMesh3 b = new DMesh3(a);
            a.AppendVertex(new Vector3d(0, 0, 5));
            b.AppendVertex(new Vector3d(3, 3, -7));
            var h = new MeshHausdorffDistance(a, b) { Tolerance = 1e-6 };
            Assert.True(h.Compute());
            AssertCertified(h, 0, 1e-6);
        }

        [Fact]
        public void DegenerateTriangles_FiniteCertifiedBounds()
        {
            // zero-area triangles (collinear corners, a repeated corner) in both meshes, as in many repair inputs
            DMesh3 a = Grid(0, 0, 10, 10, 11);
            DMesh3 b = Grid(0, 0, 10, 10, 7, (x, y) => 0.25);
            foreach (DMesh3 m in new[] { a, b }) {
                int v0 = m.AppendVertex(new Vector3d(2, 2, 0.1)), v1 = m.AppendVertex(new Vector3d(4, 2, 0.1)), v2 = m.AppendVertex(new Vector3d(6, 2, 0.1));
                m.AppendTriangle(v0, v1, v2);
                int w0 = m.AppendVertex(new Vector3d(7, 7, 0.1)), w1 = m.AppendVertex(new Vector3d(8, 8, 0.1)), w2 = m.AppendVertex(new Vector3d(8, 8, 0.1));
                m.AppendTriangle(w0, w1, w2);
            }
            var h = new MeshHausdorffDistance(a, b) { Tolerance = 1e-4 };
            Assert.True(h.Compute());
            Assert.True(h.Converged);
            Assert.InRange(h.UpperBound, 0.25 - 1e-9, 0.25 + 1e-4);
        }

        [Fact]
        public void PointTriangleDistance_DegenerateTriangle()
        {
            var tri = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(2, 0, 0));
            var p = new Vector3d(1.5, 1, 0);
            double d = DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out Vector3d c, out Vector3d bary);
            Assert.Equal(1.0, d, 12);
            Assert.True(c.Distance(new Vector3d(1.5, 0, 0)) < 1e-12);
            DMesh3 m = new DMesh3();
            m.AppendVertex(tri.V0); m.AppendVertex(tri.V1); m.AppendVertex(tri.V2);
            m.AppendTriangle(0, 1, 2);
            Assert.Equal(1.0, MeshQueries.TriDistanceSqr(m, 0, p), 12);
            // repeated corner
            var t2 = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0), new Vector3d(0, 2, 0));
            var q = new Vector3d(1, 1, 0);
            Assert.Equal(1.0, DistPoint3Triangle3.DistanceSqr(ref q, ref t2, out c, out bary), 12);
        }

        [Fact]
        public void NonFiniteCoordinates_Fail_AndResetTheOutputs()
        {
            DMesh3 a = Grid(0, 0, 10, 10, 11);
            DMesh3 b = Grid(0, 0, 10, 10, 7, (x, y) => 1);
            var h = new MeshHausdorffDistance(a, b);
            Assert.True(h.Compute());
            a.VerticesBuffer[3 * 5] = double.NaN;      // eg from a broken import (SetVertex asserts finite)
            Assert.False(h.Compute());
            Assert.True(double.IsNaN(h.UpperBound));
            Assert.False(new MeshHausdorffDistance(b, a).Compute());
            Assert.False(new MeshHausdorffDistance(new DMesh3(), b).Compute());
        }

        [Fact]
        public void ParallelPlanes_Offset()
        {
            DMesh3 a = Grid(0, 0, 10, 10, 11);
            DMesh3 b = Grid(0, 0, 10, 10, 7, (x, y) => 0.37);   // different triangulation
            var h = new MeshHausdorffDistance(a, b) { Tolerance = 1e-6 };
            Assert.True(h.Compute());
            AssertCertified(h, 0.37, 1e-6);
        }

        [Fact]
        public void MaximumInsideATriangle_FoundAndCertified()
        {
            // A: one big triangle over a hole of B. The farthest point of A from B is the center of the hole,
            // 2 away from its rim; the vertices and edges of A lie on B, so sampling them gives 0.
            DMesh3 a = Triangle(new Vector3d(-9, -9, 0), new Vector3d(9, -8, 0), new Vector3d(0, 9, 0));
            DMesh3 b = PlaneWithHole();
            Assert.Equal(0, SampledVerticesAndEdges(a, b), 9);

            var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 1e-6 };
            Assert.True(h.Compute());
            AssertCertified(h, 2.0, 1e-6);
            Assert.True(h.WorstPoint.Distance(Vector3d.Zero) < 1e-3, $"worst point {h.WorstPoint}");
        }

        [Fact]
        public void Symmetric_IsMaxOfBothDirections()
        {
            // A: plane over [0,10]^2; B: the same plane over [0,12] x [0,10] -> B's extra strip is 2 away from A
            DMesh3 a = Grid(0, 0, 10, 10, 11);
            DMesh3 b = Grid(0, 0, 12, 10, 13);
            var h = new MeshHausdorffDistance(a, b) { Tolerance = 1e-6 };
            Assert.True(h.Compute());
            AssertCertified(h, 2.0, 1e-6);
        }

        [Fact]
        public void DifferentTriangulations_ContainsDenseSamplingEstimate()
        {
            // two different spheres approximations, rotated against each other
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 5 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1.02, EdgeVertices = 9 }.Generate().MakeDMesh();
            MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 23));
            double tol = 1e-5;
            var h = new MeshHausdorffDistance(a, b) { Tolerance = tol };
            Assert.True(h.Compute());
            Assert.True(h.Converged);
            Assert.True(h.UpperBound - h.LowerBound <= tol);
            double sampled = Math.Max(SampledDense(a, b, 12), SampledDense(b, a, 12));
            // the exact value is >= any sample and inside the interval
            Assert.True(sampled <= h.UpperBound + 1e-12, $"sampled {sampled} > upper {h.UpperBound}");
            Assert.True(h.LowerBound >= sampled - tol);
        }

        [Fact]
        public void TriangleSoupAndUtmCoordinates()
        {
            Vector3d offset = new Vector3d(632000, 5712000, 100);
            DMesh3 a = Triangle(new Vector3d(-9, -9, 0), new Vector3d(9, -8, 0), new Vector3d(0, 9, 0));
            DMesh3 b = Soup(PlaneWithHole());
            MeshTransforms.Translate(a, offset);
            MeshTransforms.Translate(b, offset);
            var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 1e-6 };
            Assert.True(h.Compute());
            AssertCertified(h, 2.0, 1e-6);
        }

        [Fact]
        public void ReducedMesh_DeviationCertifiedQuickly()
        {
            // typical use: how far does a simplified terrain deviate from the original?
            DMesh3 original = Grid(-50, -50, 50, 50, 101, (x, y) => 3 * Math.Sin(0.1 * x) * Math.Cos(0.13 * y));
            DMesh3 reduced = new DMesh3(original);
            var reducer = new Reducer(reduced);
            reducer.ReduceToTriangleCount(2000);
            var h = new MeshHausdorffDistance(original, reduced) { RelativeTolerance = 1e-4 };
            Assert.True(h.Compute());
            Assert.True(h.Converged);
            // "quickly" is checked by the amount of work (about 54k subdivisions), not by wall-clock time,
            // which is unreliable on loaded CI machines and on Mono
            Assert.True(h.Subdivisions < 200000, $"{h.Subdivisions} subdivisions");
            double sampled = Math.Max(SampledDense(original, reduced, 4), SampledDense(reduced, original, 4));
            Assert.True(sampled <= h.UpperBound + 1e-12);
            Assert.True(h.LowerBound >= sampled - (h.UpperBound - h.LowerBound) - 1e-12);
        }
    
        // sphere with random bumps (seeded), so that the nearest triangles change across most triangles
        static DMesh3 RandomBumpySphere(int seed, int n)
        {
            var rnd = new Random(seed);
            double a = 0.05 + 0.2 * rnd.NextDouble(), f = 2 + 6 * rnd.NextDouble(), g = 2 + 6 * rnd.NextDouble(), ph = 6 * rnd.NextDouble();
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + a * Math.Sin(f * p.x + ph) * Math.Cos(g * p.y - 0.5) + 0.01 * (rnd.NextDouble() - 0.5)));
            }
            return m;
        }

        [Fact]
        public void UpperBound_ValidWithCoarseToleranceAndWithoutConvergence()
        {
            // the upper bound is used as an acceptance test (MeshRobustRepair.SimplifyWithin): it must hold with a
            // coarse tolerance and also when the subdivision budget stops the refinement (Converged false)
            for (int seed = 0; seed < 6; ++seed) {
                // from a coarse mesh to a fine one of the same shape: the largest distance lies inside the coarse
                // triangles, not at their corners, so a bound that ignores the unrefined parts is caught
                DMesh3 a = RandomBumpySphere(seed, 6), b = RandomBumpySphere(seed, 16);
                if (seed % 2 == 1) {
                    // a simplified copy against the original, as in the wrap simplification
                    a = new DMesh3(b);
                    new Reducer(a).ReduceToTriangleCount(b.TriangleCount / 10);
                    a = new DMesh3(a, true);
                }
                double sampled = SampledDense(a, b, 8);
                Assert.True(sampled > 1e-3, $"seed {seed}: sampled {sampled}");
                foreach (int budget in new[] { 0, 30, 20000000 }) {
                    foreach (double tol in new[] { 0.3 * sampled, 1e-4 }) {
                        var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, MaxSubdivisions = budget };
                        Assert.True(h.Compute());
                        string what = $"seed {seed} budget {budget} tol {tol}: [{h.LowerBound}, {h.UpperBound}] converged {h.Converged} subdivisions {h.Subdivisions} sampled {sampled}";
                        Assert.True(sampled <= h.UpperBound + 1e-12, $"{what}: sampled {sampled} above upper {h.UpperBound}");
                        Assert.True(h.LowerBound <= h.UpperBound, what);
                        if (budget == 20000000) {
                            Assert.True(h.Converged, what);
                            Assert.True(h.UpperBound - h.LowerBound <= tol + 1e-12, what);
                        }
                        Assert.True(h.Subdivisions < budget + 256, what);     // (checked per batch of 256)
                        if (h.Converged)
                            Assert.True(h.UpperBound - h.LowerBound <= tol + 1e-12, what);
                    }
                }
            }
        }

        [Fact]
        public void Cancelled_ReturnsFalse()
        {
            DMesh3 a = RandomBumpySphere(1, 14), b = RandomBumpySphere(2, 9);
            var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 1e-6, CancelF = () => true };
            Assert.False(h.Compute());
            Assert.False(h.Converged);
        }

        [Fact]
        public void Reuse_And_PrebuiltSpatial_SameBounds()
        {
            DMesh3 a = RandomBumpySphere(3, 14), b = RandomBumpySphere(4, 9);
            var h = new MeshHausdorffDistance(a, b) { Symmetric = true, Tolerance = 1e-4 };
            Assert.True(h.Compute());
            double lo = h.LowerBound, up = h.UpperBound;
            Assert.True(h.Compute());       // computed twice on one instance
            Assert.Equal(lo, h.LowerBound);
            Assert.Equal(up, h.UpperBound);
            var pre = new MeshHausdorffDistance(a, b) { Symmetric = true, Tolerance = 1e-4,
                SpatialA = new DMeshAABBTree3(a, true), SpatialB = new DMeshAABBTree3(b, true) };
            Assert.True(pre.Compute());
            Assert.Equal(lo, pre.LowerBound);
            Assert.Equal(up, pre.UpperBound);
        }

        [Fact]
        public void Invariant_UnderSimilarities()
        {
            DMesh3 a = RandomBumpySphere(5, 12), b = RandomBumpySphere(6, 8);
            foreach (bool symmetric in new[] { false, true }) {
                var h0 = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };       // relative tolerance
                Assert.True(h0.Compute());
                foreach (Similarity s in Similarity.All()) {
                    DMesh3 ta = new DMesh3(a), tb = new DMesh3(b);
                    MeshTransforms.PerVertexTransform(ta, s.Apply);
                    MeshTransforms.PerVertexTransform(tb, s.Apply);
                    var h = new MeshHausdorffDistance(ta, tb) { Symmetric = symmetric };
                    Assert.True(h.Compute());
                    if (s.IsExact) {
                        Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                        Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    } else {
                        // both intervals contain the exact distance (up to the coordinates' rounding)
                        double eps = 100 * s.Resolution(2);
                        Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                    }
                }
            }
        }
}
}
