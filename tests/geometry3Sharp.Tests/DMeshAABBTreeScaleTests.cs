using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DMeshAABBTree3's top-down build must not depend on the scale of the mesh. It decided between "split at the midpoint
    /// of the centroids" and "split half and half" by an absolute spread of 1e-8 (MathUtil.ZeroTolerance): a mesh whose
    /// centroids lay within 1e-8 along the axis (a small mesh, a thin plate) got another tree than the same mesh scaled by
    /// 2^k, and the queries that depend on the tree (tie-breaks of the nearest hit, the adaptive winding number contour of
    /// the solidify fallback) differed in the last bits. Found by the repair campaigns (RepairCampaignTests, F2); other
    /// absolute tolerances of the spatial queries remain (F3), so this test pins the tree, not the solidify result.
    /// </summary>
    public class DMeshAABBTreeScaleTests
    {
        // a thin plate: 2 x 2 x 2^-30 (centroid spread along z below 1e-8), with a grid of triangles on top and bottom
        internal static DMesh3 ThinPlate(int n, double thickness)
        {
            var m = new DMesh3();
            int[,] top = new int[n + 1, n + 1], bottom = new int[n + 1, n + 1];
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j) {
                    double x = -1 + 2.0 * i / n, y = -1 + 2.0 * j / n;
                    // (a little unevenness in x and y, in dyadic steps, so that the centroids are not a regular grid)
                    double w = ((i * 7 + j * 13) % 5) * Math.Pow(2, -9);
                    top[i, j] = m.AppendVertex(new Vector3d(x + w, y - w, thickness / 2));
                    bottom[i, j] = m.AppendVertex(new Vector3d(x + w, y - w, -thickness / 2));
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    m.AppendTriangle(top[i, j], top[i + 1, j], top[i + 1, j + 1]);
                    m.AppendTriangle(top[i, j], top[i + 1, j + 1], top[i, j + 1]);
                    m.AppendTriangle(bottom[i, j], bottom[i + 1, j + 1], bottom[i + 1, j]);
                    m.AppendTriangle(bottom[i, j], bottom[i, j + 1], bottom[i + 1, j + 1]);
                }
            return m;
        }

        static DMesh3 Scaled(DMesh3 m, double factor)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, factor * c.GetVertex(v));
            return c;
        }

        // the order in which a traversal reaches boxes (with their depth) and triangles: the structure of the tree
        static List<int> Structure(DMesh3 mesh, DMeshAABBTree3.BuildStrategy strategy)
        {
            var tree = new DMeshAABBTree3(mesh);
            tree.Build(strategy);
            var seq = new List<int>();
            tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                NextBoxF = (box, depth) => { seq.Add(-1 - depth); return true; },
                NextTriangleF = (t) => seq.Add(t)
            });
            return seq;
        }

        [Theory]
        [InlineData(DMeshAABBTree3.BuildStrategy.TopDownMidpoint)]
        [InlineData(DMeshAABBTree3.BuildStrategy.TopDownMedian)]
        public void ThinPlate_TreeStructure_IsTheSameAtEveryScale(DMeshAABBTree3.BuildStrategy strategy)
        {
            DMesh3 plate = ThinPlate(6, Math.Pow(2, -30));
            List<int> reference = Structure(Scaled(plate, Math.Pow(2, 10)), strategy);
            Assert.True(reference.Count > 100);
            foreach (int k in new[] { 0, -10, -20, -30 })
                Assert.True(reference.SequenceEqual(Structure(Scaled(plate, Math.Pow(2, 10 + k)), strategy)), $"scale 2^{10 + k}");
        }
    }
}
