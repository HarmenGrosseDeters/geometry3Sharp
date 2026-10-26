using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    static class MeshTestUtil
    {
        /// <summary>
        /// Two closed cones (n-gon pyramids with base cap) touching at a shared apex vertex 0,
        /// so vertex 0 is a bowtie. Triangles are appended in a random order given by seed.
        /// </summary>
        public static DMesh3 DoubleConeBowtie(int n, int seed)
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(Vector3d.Zero);
            var tris = new List<Index3i>();
            foreach (double z in new[] { 1.0, -1.0 }) {
                int center = mesh.AppendVertex(new Vector3d(0, 0, z));
                int first = mesh.VertexCount;
                for (int i = 0; i < n; ++i) {
                    double a = 2 * Math.PI * i / n;
                    mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), z));
                }
                for (int i = 0; i < n; ++i) {
                    int r0 = first + i, r1 = first + (i + 1) % n;
                    if (z > 0) {
                        tris.Add(new Index3i(0, r1, r0));
                        tris.Add(new Index3i(center, r0, r1));
                    } else {
                        tris.Add(new Index3i(0, r0, r1));
                        tris.Add(new Index3i(center, r1, r0));
                    }
                }
            }
            var rand = new Random(seed);
            foreach (Index3i t in tris.OrderBy(t => rand.Next()))
                mesh.AppendTriangle(t);
            return mesh;
        }

        /// <summary>
        /// The unit cube, closed except for one short crack: the top face has a square hole of the given size around
        /// its center, closed by a separate two-triangle patch (own vertices) lifted by gap. The only boundary is the
        /// two coincident square loops, so the boundary edge midpoints span just the hole size. 16 vertices,
        /// 20 triangles; 12 vertices once the crack is merged.
        /// </summary>
        public static DMesh3 CubeWithShortCrack(double size, double gap)
        {
            var mesh = new DMesh3();
            double[] cx = { 0, 1, 1, 0 }, cy = { 0, 0, 1, 1 };
            int[] B = new int[4], O = new int[4], I = new int[4], P = new int[4];
            for (int k = 0; k < 4; ++k) B[k] = mesh.AppendVertex(new Vector3d(cx[k], cy[k], 0));
            for (int k = 0; k < 4; ++k) O[k] = mesh.AppendVertex(new Vector3d(cx[k], cy[k], 1));
            for (int k = 0; k < 4; ++k) I[k] = mesh.AppendVertex(new Vector3d(0.5 + size * (cx[k] - 0.5), 0.5 + size * (cy[k] - 0.5), 1));
            for (int k = 0; k < 4; ++k) P[k] = mesh.AppendVertex(new Vector3d(0.5 + size * (cx[k] - 0.5), 0.5 + size * (cy[k] - 0.5), 1 + gap));
            mesh.AppendTriangle(B[0], B[2], B[1]);
            mesh.AppendTriangle(B[0], B[3], B[2]);
            for (int k = 0; k < 4; ++k) {
                int k1 = (k + 1) % 4;
                mesh.AppendTriangle(B[k], B[k1], O[k1]);
                mesh.AppendTriangle(B[k], O[k1], O[k]);
                mesh.AppendTriangle(O[k], O[k1], I[k1]);
                mesh.AppendTriangle(O[k], I[k1], I[k]);
            }
            mesh.AppendTriangle(P[0], P[1], P[2]);
            mesh.AppendTriangle(P[0], P[2], P[3]);
            return mesh;
        }

        /// <summary>
        /// One small triangle (edge about size, near the origin) in copies separate vertices, plus, if far is set,
        /// a unit triangle at x = 10 that makes the centroids span about 10.
        /// </summary>
        public static DMesh3 DuplicatedSmallTriangle(double size, int copies, bool far)
        {
            var mesh = new DMesh3();
            for (int k = 0; k < copies; ++k)
                mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(0, 0, 0)), mesh.AppendVertex(new Vector3d(size, 0, 0)),
                    mesh.AppendVertex(new Vector3d(0, size, 0)));
            if (far)
                mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(10, 0, 0)), mesh.AppendVertex(new Vector3d(11, 0, 0)),
                    mesh.AppendVertex(new Vector3d(10, 1, 0)));
            return mesh;
        }

        public static int BoundaryEdgeCount(DMesh3 mesh)
        {
            return MeshIterators.BoundaryEdges(mesh).Count();
        }
    }
}
