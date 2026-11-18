using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The parallel walk of TriangleDeviationBounds.IsWithin (the certification of a collapse's new triangles, #1109): after a
    /// warm-up of the serial walk the open pieces are checked on several threads. The answer must be the one of the serial walk,
    /// whatever the thread schedule, so reductions with and without it are the same mesh bit for bit.
    /// </summary>
    public class ReducerParallelDeviationCheckTests
    {
        static DMesh3 Bumpy(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        static DMesh3 Transformed(DMesh3 m, Similarity s)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices().ToList()) c.SetVertex(v, s.Apply(c.GetVertex(v)));
            return c;
        }

        static void AssertSame(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            foreach (int v in a.VertexIndices()) {
                Assert.True(b.IsVertex(v));
                Assert.Equal(a.GetVertex(v), b.GetVertex(v));
            }
            foreach (int t in a.TriangleIndices()) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        // the triangles of a coarse reduction of the reference: large against the reference's triangles, near it or not
        static List<Triangle3d> CoarseTriangles(DMesh3 reference, int count)
        {
            var coarse = new DMesh3(reference);
            new Reducer(coarse).ReduceToTriangleCount(count);
            var tris = new List<Triangle3d>();
            foreach (int t in coarse.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                coarse.GetTriVertices(t, ref a, ref b, ref c);
                tris.Add(new Triangle3d(a, b, c));
            }
            return tris;
        }

        /// <summary>
        /// IsWithin with the parallel walk equals the serial walk, with and without a cache, over tolerances from "no triangle passes"
        /// to "all pass" (so some answers are decided only after the warm-up pieces), and again on a repeat
        /// </summary>
        [Fact]
        public void IsWithin_ParallelEqualsSerial()
        {
            DMesh3 reference = Bumpy(16);
            var tree = new DMeshAABBTree3(reference, true);
            var bounds = new TriangleDeviationBounds(reference);
            var tris = CoarseTriangles(reference, 40);
            int yes = 0, no = 0, noAfterHandoff = 0;
            foreach (double eps in new[] { 0.02, 0.05, 0.08, 0.12, 0.2 }) {
                foreach (Triangle3d t in tris) {
                    bool serial = bounds.IsWithin(tree, t.V0, t.V1, t.V2, eps, 10, null, false);
                    long before = bounds.ParallelHandoffs;
                    bool parallel = bounds.IsWithin(tree, t.V0, t.V1, t.V2, eps, 10, null, true);
                    if (!parallel && bounds.ParallelHandoffs > before) noAfterHandoff++;
                    var cache = new TriangleDeviationBounds.QueryCache(tree, eps);
                    bool parallelCached = bounds.IsWithin(tree, t.V0, t.V1, t.V2, eps, 10, cache, true);
                    Assert.Equal(serial, parallel);
                    Assert.Equal(serial, parallelCached);
                    Assert.Equal(serial, bounds.IsWithin(tree, t.V0, t.V1, t.V2, eps, 10, null, true));
                    if (serial) yes++; else no++;
                }
            }
            Assert.True(yes > 20 && no > 20, $"both answers occur: {yes} within, {no} not");
            Assert.True(noAfterHandoff > 0, "some failures are found by the parallel phase");
            Assert.True(bounds.ParallelHandoffs > 20, $"the walks went past the warm-up: {bounds.ParallelHandoffs}");
        }

        /// <summary>
        /// a piece that fails late (deep in a triangle that is mostly within the tolerance) is found by the parallel walk: a triangle
        /// that is within eps of the reference except for one dent, with the dent in every position of the triangle
        /// </summary>
        [Fact]
        public void IsWithin_FailureDeepInATriangleIsFound()
        {
            // reference: a flat grid of 40 x 40 cells at z = 0; the triangle lies at z = 0 above it, and the grid has a hole-free dent: one vertex moved down
            int n = 40;
            int failures = 0, failuresAfterHandoff = 0;
            for (int dent = 0; dent < 25; ++dent) {
                var m = new DMesh3();
                int[,] ids = new int[n + 1, n + 1];
                for (int j = 0; j <= n; ++j)
                    for (int i = 0; i <= n; ++i)
                        ids[i, j] = m.AppendVertex(new Vector3d(i, j, 0));
                for (int j = 0; j < n; ++j)
                    for (int i = 0; i < n; ++i) {
                        m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                        m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                    }
                int di = 6 + (dent % 5) * 7, dj = 6 + (dent / 5) * 6;
                m.SetVertex(ids[di, dj], new Vector3d(di, dj, -3));
                var tree = new DMeshAABBTree3(m, true);
                var bounds = new TriangleDeviationBounds(m);
                // a big triangle over the grid, 0.3 above the plane: within eps = 0.5 of the flat part only
                Vector3d a = new Vector3d(1, 1, 0.3), b = new Vector3d(38, 2, 0.3), c = new Vector3d(20, 37, 0.3);
                bool serial = bounds.IsWithin(tree, a, b, c, 0.5, 10, null, false);
                bool parallel = bounds.IsWithin(tree, a, b, c, 0.5, 10, null, true);
                Assert.Equal(serial, parallel);
                if (!serial) failures++;
                if (!parallel && bounds.ParallelHandoffs > 0) failuresAfterHandoff++;
            }
            Assert.True(failures > 0, "some dents are under the triangle");
            Assert.True(failuresAfterHandoff > 0, "some failures are found by the parallel phase");
        }

        static DMesh3 Reduce(DMesh3 input, double tol, bool parallel, out Reducer reducer)
        {
            var m = new DMesh3(input);
            reducer = new Reducer(m) { MaxDeviation = tol, ParallelDeviationCheck = parallel };
            reducer.ReduceToTriangleCount(4);
            return m;
        }

        /// <summary>
        /// the same reduction with and without the parallel walk gives the same mesh bit for bit, twice, and on the similarity images of
        /// the input (those of a power of two scale also equal the scaled result)
        /// </summary>
        [Fact]
        public void Reduction_SameWithAndWithoutParallelWalk_UnderSimilarities()
        {
            DMesh3 input = Bumpy(8);
            double diag = input.CachedBounds.DiagonalLength;
            DMesh3 baseline = Reduce(input, 1e-2 * diag, false, out Reducer _);
            Assert.True(baseline.TriangleCount < 0.9 * input.TriangleCount && baseline.TriangleCount > 4, $"{baseline.TriangleCount} of {input.TriangleCount}");
            foreach (Similarity s in Similarity.All()) {
                DMesh3 image = Transformed(input, s);
                double tol = s.ApplyLength(1e-2 * diag);
                DMesh3 serial = Reduce(image, tol, false, out Reducer r0), parallel = Reduce(image, tol, true, out Reducer r1);
                AssertSame(serial, parallel);
                Assert.Equal(0, r0.DeviationParallelHandoffs);
                Assert.True(r1.DeviationParallelHandoffs > 0, "the check went past the warm-up");
                AssertSame(parallel, Reduce(image, tol, true, out Reducer _));     // repeated: deterministic
                if (s.IsExact) {
                    // (a power of two scale: the scaled baseline, bit for bit)
                    foreach (int v in baseline.VertexIndices()) Assert.Equal(s.Apply(baseline.GetVertex(v)), parallel.GetVertex(v));
                    Assert.Equal(baseline.TriangleCount, parallel.TriangleCount);
                }
            }
        }
    }
}
