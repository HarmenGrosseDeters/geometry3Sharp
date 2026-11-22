using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.MaxDeviation on flat inputs (issue 1317): the new triangles of a collapse span many reference triangles of a flat
    /// face, and many touch its boundary or a crease. The check must certify them without splitting them down to the size of the
    /// reference triangles (the tree queries of a reduction are counted: a structural bound, not a wall-clock one), and the result
    /// must be the reduction it was.
    /// </summary>
    public class ReducerFlatInputTests
    {
        // n x n cells over the unit square in a tilted plane (z = 0.25 x): flat, with a boundary
        static DMesh3 Plate(int n)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    ids[i, j] = m.AppendVertex(new Vector3d((double)i / n, (double)j / n, 0.25 * i / n));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        // a welded cube [-1, 1]^3 with n vertices per edge: flat faces, creases
        static DMesh3 Cube(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p / Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
            }
            return m;
        }

        // certified upper bound of the one-sided distance
        static double Deviation(DMesh3 from, DMesh3 to, double eps)
        {
            var h = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = 0.01 * eps };
            h.Compute();
            return h.UpperBound;
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

        [Fact]
        public void FlatPlate_CertifiedWithFewQueries()
        {
            DMesh3 input = Plate(12);
            double eps = 1e-3 * input.CachedBounds.DiagonalLength;
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps };
            r.ReduceToTriangleCount(2);
            // (before the fix 232049 lookups, with it 25894: the pieces along the plate's boundary were split down to the cells)
            Assert.True(r.DeviationCacheLookups < 80000, "tree queries: " + r.DeviationCacheLookups);
            // the result: the plate, its boundary kept, within the tolerance both ways
            Assert.True(m.TriangleCount <= 8, "triangles: " + m.TriangleCount);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(Deviation(m, input, eps) <= eps);
            Assert.True(Deviation(input, m, eps) <= eps);
        }

        [Fact]
        public void Cube_CertifiedWithFewQueries()
        {
            DMesh3 input = Cube(9);
            double eps = 1e-3 * input.CachedBounds.DiagonalLength;
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps };
            r.ReduceToTriangleCount(4);
            // (before the fix 538853 lookups, with it 75792: the pieces along the cube's edges were split down to the cells)
            Assert.True(r.DeviationCacheLookups < 200000, "tree queries: " + r.DeviationCacheLookups);
            Assert.True(m.TriangleCount <= 20, "triangles: " + m.TriangleCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(Deviation(m, input, eps) <= eps);
            Assert.True(Deviation(input, m, eps) <= eps);
            Assert.Equal(8.0, MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x, 9);
        }

        /// <summary> repeated runs, the parallel walk of the check and a second reduction on the same instance give the same meshes </summary>
        [Fact]
        public void FlatInputs_Repeatable_ParallelEqualsSerial_Reuse()
        {
            foreach (DMesh3 input in new[] { Plate(8), Cube(6) }) {
                double eps = 1e-3 * input.CachedBounds.DiagonalLength;
                Func<bool, DMesh3> reduce = (parallel) => {
                    var m = new DMesh3(input);
                    new Reducer(m) { MaxDeviation = eps, ParallelDeviationCheck = parallel }.ReduceToTriangleCount(2);
                    return m;
                };
                DMesh3 serial = reduce(false);
                AssertSame(serial, reduce(false));
                AssertSame(serial, reduce(true));
                // in two steps on one Reducer: still within the tolerance of the input (the reference is the first mesh)
                var two = new DMesh3(input);
                var r = new Reducer(two) { MaxDeviation = eps };
                r.ReduceToTriangleCount(input.TriangleCount / 2);
                Assert.True(two.TriangleCount <= input.TriangleCount / 2);
                r.ReduceToTriangleCount(2);
                Assert.True(two.TriangleCount <= 3 * serial.TriangleCount, two.TriangleCount + " vs " + serial.TriangleCount);
                Assert.True(two.CheckValidity(true, FailMode.ReturnOnly));
                Assert.True(Deviation(two, input, eps) <= eps);
            }
        }

        /// <summary> under the similarities: bit for bit under the 2^k scales, the same guarantees under the others </summary>
        [Fact]
        public void FlatInputs_InvariantUnderSimilarities()
        {
            foreach (DMesh3 input in new[] { Plate(10), Cube(7) }) {
                double eps = 1e-3 * input.CachedBounds.DiagonalLength;
                var m0 = new DMesh3(input);
                new Reducer(m0) { MaxDeviation = eps }.ReduceToTriangleCount(2);
                foreach (Similarity s in Similarity.All()) {
                    DMesh3 ti = Invariance.Transformed(input, s);
                    var m = new DMesh3(ti);
                    new Reducer(m) { MaxDeviation = s.ApplyLength(eps) }.ReduceToTriangleCount(2);
                    if (s.IsExact) {
                        AssertSame(Invariance.Transformed(m0, s), m);
                        continue;
                    }
                    Assert.True(m.CheckValidity(true, FailMode.ReturnOnly), s.Name);
                    Assert.True(m.TriangleCount <= 3 * m0.TriangleCount + 8, s.Name + ": " + m.TriangleCount + " vs " + m0.TriangleCount);
                    Assert.True(Deviation(m, ti, s.ApplyLength(eps)) <= s.ApplyLength(eps), s.Name);
                }
            }
        }
    }
}
