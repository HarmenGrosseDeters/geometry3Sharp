using System;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class WrapFidelityHypothesisTests
    {
        static DMesh3 Patch(bool crease, bool oppositeDiagonal, bool reverseIds)
        {
            var points = new[] {
                new Vector3d(-1, 0, 0), new Vector3d(0, 0, 0), new Vector3d(1, 0, crease ? 1 : 0),
                new Vector3d(-1, 1, 0), new Vector3d(0, 1, 0), new Vector3d(1, 1, crease ? 1 : 0)
            };
            var mesh = new DMesh3();
            var ids = new int[points.Length];
            for (int i = 0; i < points.Length; ++i) {
                int j = reverseIds ? points.Length - 1 - i : i;
                ids[j] = mesh.AppendVertex(points[j]);
            }
            for (int k = 0; k < 2; ++k) {
                int a = k, b = k + 1, c = k + 3, d = k + 4;
                if (oppositeDiagonal) {
                    Assert.True(mesh.AppendTriangle(ids[a], ids[b], ids[c]) >= 0);
                    Assert.True(mesh.AppendTriangle(ids[b], ids[d], ids[c]) >= 0);
                } else {
                    Assert.True(mesh.AppendTriangle(ids[a], ids[b], ids[d]) >= 0);
                    Assert.True(mesh.AppendTriangle(ids[a], ids[d], ids[c]) >= 0);
                }
            }
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            return mesh;
        }

        // Exercise the actual private query adapter without changing the public API or copying its normal logic.
        static Vector3d ClosestNormal(DMesh3 mesh, Vector3d point, out double distance)
        {
            Type surfaceType = typeof(MeshFidelityMetrics).GetNestedType("Surface", BindingFlags.NonPublic);
            Assert.NotNull(surfaceType);
            object surface = Activator.CreateInstance(surfaceType, new object[] { mesh, 0.0 });
            object[] args = { point, Vector3d.Zero, DMesh3.InvalidID, Vector3d.Zero };
            distance = (double)surfaceType.GetMethod("Distance").Invoke(surface, args);
            Assert.True((int)args[2] >= 0);
            return (Vector3d)args[1];
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FixedFaceEdgeVertexQueries_PreserveAnalyticNormalsUnderRetriangulation(bool crease)
        {
            Vector3d left = Vector3d.AxisZ;
            Vector3d right = crease ? new Vector3d(-1, 0, 1).Normalized : left;
            Vector3d seam = (left + right).Normalized;
            var points = new[] { new Vector3d(-0.75, 0.25, 0), new Vector3d(0.75, 0.25, crease ? 0.75 : 0),
                new Vector3d(0, 0.5, 0), Vector3d.Zero, new Vector3d(0, 1, 0) };
            var normals = new[] { left, right, seam, seam, seam };
            foreach (bool diagonal in new[] { false, true })
                foreach (bool reindex in new[] { false, true }) {
                    DMesh3 mesh = Patch(crease, diagonal, reindex);
                    for (int i = 0; i < points.Length; ++i) {
                        double distance;
                        Vector3d normal = ClosestNormal(mesh, points[i], out distance);
                        Assert.InRange(distance, 0, 1e-14);
                        Assert.True((normal - normals[i]).Length < 1e-12,
                            $"crease={crease}, diagonal={diagonal}, reindex={reindex}, point={i}: {normal} vs {normals[i]}");
                    }
                }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SameSurfaceRetriangulation_HasNoDistanceOrReversalRegression(bool crease)
        {
            foreach (int seed in new[] { 1, 7, 31 }) {
                var f = new MeshFidelityMetrics(Patch(crease, false, false), Patch(crease, true, true)) {
                    SurfaceSamples = 1024, EdgeSamples = 128, VolumeSamples = 0,
                    OuterSurface = false, Curvature = false, Seed = seed, Parallel = false
                };
                Assert.True(f.Compute());
                // Squared-distance cancellation can leave sqrt(epsilon)-scale noise on identical surfaces.
                Assert.InRange(f.MaxDistanceMeshToReference, 0, 1e-7);
                Assert.InRange(f.MaxDistanceReferenceToMesh, 0, 1e-7);
                Assert.All(f.FScore, score => Assert.Equal(1.0, score));
                Assert.Equal(0.0, f.NormalsMeshToReference.ReversedFraction);
                Assert.Equal(0.0, f.NormalsReferenceToMesh.ReversedFraction);
                Assert.InRange(f.NormalConsistency, 1 - 1e-12, 1);
            }
        }
    }
}
