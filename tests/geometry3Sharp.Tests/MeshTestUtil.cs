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

        public static int BoundaryEdgeCount(DMesh3 mesh)
        {
            return MeshIterators.BoundaryEdges(mesh).Count();
        }
    }
}
