using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class LaplacianDeformerTests
    {
        // pull the centre of a grid patch up, keep its border fixed
        static DMesh3 Deform(DMesh3 mesh)
        {
            var deformer = new LaplacianMeshDeformer(mesh);
            AxisAlignedBox3d bounds = mesh.CachedBounds;
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                if (mesh.IsBoundaryVertex(vid))
                    deformer.SetConstraint(vid, v, 10, true);
                else if (v.Distance(bounds.Center) < 0.08 * bounds.Width)
                    deformer.SetConstraint(vid, v + 0.3 * bounds.Width * Vector3d.AxisZ, 10);
            }
            Assert.True(deformer.SolveAndUpdateMesh());
            return mesh;
        }

        [Fact]
        public void NonCompactMesh_SameResultAsCompacted()
        {
            DMesh3 mesh = Plane();
            // remove a corner patch so vertex ids get gaps
            foreach (int tid in mesh.TriangleIndices().ToArray())
                if (mesh.GetTriCentroid(tid).x < -0.35 && mesh.GetTriCentroid(tid).y < -0.35)
                    mesh.RemoveTriangle(tid, true, false);
            Assert.False(mesh.IsCompact);
            DMesh3 compact = new DMesh3(mesh, true);
            Assert.True(compact.IsCompact);

            Deform(mesh);
            Deform(compact);
            // compact copy keeps the vertex order, so compare in order
            var a = mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToArray();
            var b = compact.VertexIndices().Select(v => compact.GetVertex(v)).ToArray();
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i)
                Assert.True(a[i].Distance(b[i]) < 1e-6, $"vertex {i}: {a[i]} vs {b[i]}");
            Assert.True(a.Max(p => p.z) > 0.1);
        }

        static DMesh3 Plane()
        {
            var gen = new TrivialRectGenerator() { Width = 1, Height = 1, IndicesMap = new Index2i(1, 2) };
            DMesh3 m = gen.Generate().MakeDMesh();
            // refine to a grid
            for (int k = 0; k < 4; ++k) {
                foreach (int eid in m.EdgeIndices().ToArray()) {
                    DMesh3.EdgeSplitInfo info;
                    m.SplitEdge(eid, out info);
                }
            }
            return m;
        }
    }

}
