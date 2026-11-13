using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.SimplifyWithin retries a reduction that is not certified within maxDistance with
    /// Reducer.MaxDeviation (issue 747): the quadric error bounds the vertices' distance to the planes they absorbed,
    /// not the triangles' distance to the mesh, so the reduction could deviate by more and was then thrown away (on the
    /// alpha wrap: the raw wrap returned, AlphaWrapSimplified false). Only the old API here, so this fails on the old code.
    /// </summary>
    public class SimplifyWithinRetryTests
    {
        internal static DMesh3 BumpySphere(int edgeVertices)
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int vid in sphere.VertexIndices()) {
                Vector3d p = sphere.GetVertex(vid);
                sphere.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5)));
            }
            return sphere;
        }

        internal static void AssertCertified(DMesh3 simp, DMesh3 mesh, double maxDistance, string what, double maxShare = 0.99)
        {
            Assert.True(simp != null, what + ": not simplified");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(simp));
            Assert.True(simp.TriangleCount < mesh.TriangleCount * maxShare, $"{what}: {simp.TriangleCount} of {mesh.TriangleCount}");
            var hd = new MeshHausdorffDistance(simp, mesh) { Symmetric = false, Tolerance = 0.05 * maxDistance };
            hd.Compute();
            Assert.True(hd.UpperBound <= maxDistance, $"{what}: hausdorff {hd.UpperBound} > {maxDistance}");
        }

        // a quadric budget 10 times the distance: the plain reduction is not certified; the retry is, and still reduces
        [Fact]
        public void NotCertified_RetriedWithTheDeviationBound()
        {
            DMesh3 sphere = BumpySphere(16);
            DMesh3 simp = MeshRobustRepair.SimplifyWithin(sphere, 0.01, 1e-3, true, () => false);
            AssertCertified(simp, sphere, 1e-3, "serial");
            // the slabs of the wrap simplification (parallel): the same bound
            int sub;
            DMesh3 slabs = MeshRobustRepair.SimplifyWithin(sphere, 0.01, 1e-3, true, () => false, out sub, 8, 0);
            AssertCertified(slabs, sphere, 1e-3, "slabs");
            // cancelled: null
            Assert.Null(MeshRobustRepair.SimplifyWithin(sphere, 0.01, 1e-3, true, () => true));
        }
    }
}
