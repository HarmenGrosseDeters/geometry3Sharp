using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Paths of PointCloudNormals, WindingContouring and WindingNumberOctree that no other test reaches: a cloud with a few
    /// axes among many degenerate points, a wrap outside its supported range, a neighbourhood the eigen solver cannot
    /// handle, a mesh with nothing to fill, and an octree of one leaf cell.
    /// </summary>
    public class MiscCoverageTests
    {
        static double Pow2(int e) => Math.Pow(2.0, e);

        // ---------------------------------------------------------------- PointCloudNormals

        static List<Vector3d> PlanePatch(int n, double x, int seed)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            for (int i = 0; i < n; ++i) pts.Add(new Vector3d(x, 3 * rnd.NextDouble(), 3 * rnd.NextDouble()));
            return pts;
        }

        [Fact]
        public void PointCloudNormals_FewAxesAmongCollinearPoints_IsDegenerateInputWithSpacingAndNoiseOfTheAxes()
        {
            // 40 points of a plane patch and a long line 1000 away: the line points have no normal axis, only 40 of 640 points do
            // (below MinConfidentFraction), and Spacing / NoiseSigma are still estimated from the points that have one
            var plane = PlanePatch(40, 1000, 5);
            var cloud = new List<Vector3d>(plane);
            for (int i = 0; i < 600; ++i) cloud.Add(new Vector3d(i * 0.1, 0, 0));

            foreach (bool orient in new[] { false, true }) {
                var pn = new PointCloudNormals(cloud) { Orient = orient };
                Assert.False(pn.Compute());
                Assert.Equal(PointCloudNormals.NormalsStatus.DegenerateInput, pn.Status);
                Assert.Equal(40, pn.AxisCount);
                Assert.Equal("40 of 640 points have a normal axis", pn.Error);
                Assert.Equal(0, pn.ConfidentCount);
                Assert.Null(pn.Wrap);
                for (int i = 0; i < 40; ++i) {
                    Assert.Equal(1.0, pn.Normals[i].Length, 9);
                    Assert.True(Math.Abs(pn.Normals[i].x) > 0.999, "the axis of a patch point is the plane normal");
                    Assert.True(pn.Normals[i].x > 0, "canonical sign: the largest component is positive");
                }
                for (int i = 40; i < 640; ++i) Assert.Equal(Vector3d.Zero, pn.Normals[i]);

                // the estimates are those of the patch alone (its neighbourhoods do not reach the line)
                var alone = new PointCloudNormals(plane) { Orient = false };
                Assert.True(alone.Compute(), alone.Status + alone.Error);
                Assert.True(pn.Spacing > 0 && pn.NoiseSigma >= 0);
                Assert.InRange(Math.Abs(pn.Spacing - alone.Spacing), 0, 1e-9 * alone.Spacing);
                Assert.InRange(Math.Abs(pn.NoiseSigma - alone.NoiseSigma), 0, 1e-9 * Math.Max(alone.NoiseSigma, 1e-12));
            }
        }

        [Fact]
        public void PointCloudNormals_WrapOutsideItsSupportedRange_IsWrapFailedWithTheAxes()
        {
            // the axes work at any finite magnitude, the alpha wrap only within [2^-128, 2^128]
            var pts = PointClouds.Sphere(400, 3, Pow2(140), out _);
            var axes = new PointCloudNormals(pts) { Orient = false };
            Assert.True(axes.Compute(), axes.Status + axes.Error);

            var pn = new PointCloudNormals(pts);
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.WrapFailed, pn.Status);
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, pn.WrapStatus);
            Assert.StartsWith("alpha wrap: InvalidInput ", pn.Error);
            Assert.Null(pn.Wrap);
            Assert.Equal(0, pn.ConfidentCount);
            Assert.Equal(400, pn.Confident.Length);
            Assert.All(pn.Confident, c => Assert.False(c));
            Assert.Equal(400, pn.AxisCount);
            // the unoriented axes (canonical sign) are returned, bit for bit those of Orient = false
            Assert.Equal(axes.Normals, pn.Normals);
            Assert.All(pn.Normals, n => Assert.Equal(1.0, n.Length, 9));
            Assert.True(pn.Spacing > 0);

            // Compute() again on the same instance gives the same
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.WrapFailed, pn.Status);
            Assert.Equal(axes.Normals, pn.Normals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PointCloudNormals_ClusterTooTightForTheCovariance_HasNoNormal(bool threeDimensional)
        {
            // 40 distinct points within 4e-159 of the centre of the cloud (the box of the cloud is symmetric, so its centre is
            // exactly 0 and the cluster stays distinct in the frame of the neighbourhoods): the squares of their offsets are
            // subnormal, the trace is below 1/DBL_MAX, 1 / trace is infinite and the normalized covariance has infinite and NaN
            // entries. The cluster points get no axis, all others are not affected
            var pts = PointClouds.Sphere(600, 4, 1.0, out _);
            foreach (double s in new[] { -1.5, 1.5 }) {
                pts.Add(new Vector3d(s, 0, 0)); pts.Add(new Vector3d(0, s, 0)); pts.Add(new Vector3d(0, 0, s));
            }
            int far = pts.Count;
            var all = new List<Vector3d>(pts);
            for (int i = 1; i <= 40; ++i)
                all.Add(threeDimensional ? new Vector3d(i * 1e-160, ((i * 7) % 11 + 1) * 1e-160, ((i * 5) % 13 + 1) * 1e-160) : new Vector3d(i * 1e-160, 0, 0));
            var pn = new PointCloudNormals(all) { Orient = false };
            Assert.True(pn.Compute(), pn.Status + " " + pn.Error);
            Assert.Equal(PointCloudNormals.NormalsStatus.Success, pn.Status);
            Assert.Equal(far, pn.AxisCount);
            for (int i = 0; i < far; ++i) Assert.Equal(1.0, pn.Normals[i].Length, 9);
            for (int i = far; i < all.Count; ++i) Assert.Equal(Vector3d.Zero, pn.Normals[i]);
        }

        // ---------------------------------------------------------------- WindingContouring

        [Fact]
        public void FillSmallHolesChecked_ClosedMesh_ChangesNothingAndReportsNoIntersection()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            Assert.True(m.IsClosed());
            int triangles = m.TriangleCount, vertices = m.VertexCount;
            var tris = m.TriangleIndices().Select(t => m.GetTriangle(t)).ToList();
            Assert.Equal(0, WindingContouring.FillSmallHolesChecked(m));
            Assert.Equal(triangles, m.TriangleCount);
            Assert.Equal(vertices, m.VertexCount);
            Assert.Equal(tris, m.TriangleIndices().Select(t => m.GetTriangle(t)).ToList());
            // FinishHoles on a mesh that was reported to have left-out quads but has no hole: the contour's verdict
            int fill;
            Assert.True(WindingContouring.FinishHoles(m, 3, true, out fill));
            Assert.Equal(0, fill);
            Assert.False(WindingContouring.FinishHoles(m, 3, false, out fill));
            Assert.Equal(0, fill);
            Assert.Equal(triangles, m.TriangleCount);
        }

        // ---------------------------------------------------------------- WindingNumberOctree

        [Fact]
        public void Octree_OfOneLeafCell_IsTheCellOverTheMeshBounds()
        {
            // depth 0 without padding: the root cell is a leaf, refine() tests it against the mesh without a candidate list
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1, 2, 3), new Vector3d(0.5, 1.0, 1.5)) }.Generate().MakeDMesh();
            var octree = new WindingNumberOctree(box, new DMeshAABBTree3(box, true)) { MaxDepth = 0, PaddingCells = 0 };
            octree.Build();
            Assert.Equal(1, octree.LeafCount);
            Assert.Equal(3.0, octree.LeafCellSize);                        // the longest side of the box
            Assert.Equal(new Vector3d(0.5, 1, 1.5), octree.Origin);        // the lower corner of the box
            Assert.Single(octree.SurfaceCells);
            Assert.Equal(Vector3i.Zero, octree.SurfaceCells[0]);

            // a region that excludes the root leaves no cell
            var none = new WindingNumberOctree(box, new DMeshAABBTree3(box, true)) { MaxDepth = 0, PaddingCells = 0, RegionF = (min, size) => false };
            none.Build();
            Assert.Empty(none.SurfaceCells);
        }
    }
}
