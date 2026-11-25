using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The documented contract of the point ids of ExactMeshArrangement: the exact points and the ids of the input
    /// vertices are the same in every run; the ids of new points are only labels (not asserted to differ).
    /// </summary>
    public class ArrangementIdContractTests
    {
        static DMesh3 Sphere(Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 4 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static string Key(ExactPoint3 p) { return p.X + "," + p.Y + "," + p.Z + "," + p.W; }

        [Fact]
        public void FreshComputes_SameExactPointsAndInputVertexIds()
        {
            var meshes = new List<DMesh3>() { Sphere(Vector3d.Zero), Sphere(new Vector3d(0.7, 0.2, 0.1)), Sphere(new Vector3d(0.3, 0.8, -0.2)) };
            List<string> firstPoints = null;
            int[][] firstVertexIds = null;
            for (int run = 0; run < 3; ++run) {
                var arr = new ExactMeshArrangement(meshes);
                arr.Compute();
                Assert.NotEmpty(arr.Output);
                // multiset of the exact points of the corners, compared through Exact(id), not by id
                var pts = new List<string>();
                foreach (var t in arr.Output) {
                    pts.Add(Key(arr.Exact(t.A)));
                    pts.Add(Key(arr.Exact(t.B)));
                    pts.Add(Key(arr.Exact(t.C)));
                }
                pts.Sort(StringComparer.Ordinal);
                // input vertices: the ids at the vertex slots, before merging picks a canonical id
                var ids = arr.VertexPoints.Select(v => (int[])v.Clone()).ToArray();
                if (run == 0) { firstPoints = pts; firstVertexIds = ids; continue; }
                Assert.Equal(firstPoints, pts);
                Assert.Equal(firstVertexIds.Length, ids.Length);
                for (int m = 0; m < ids.Length; ++m)
                    Assert.Equal(firstVertexIds[m], ids[m]);
            }
        }
    }
}
