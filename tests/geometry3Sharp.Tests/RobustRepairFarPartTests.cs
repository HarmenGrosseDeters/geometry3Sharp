using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A small part far from the rest must not make MeshRobustRepair report a blob as repaired. Two intersecting unit
    /// spheres and a unit tetrahedron 3e4 .. 1e12 away: by the diagonal of all triangles (many of) the sphere triangles
    /// were degenerate, the exact resolve's cleanups collapsed the spheres, the solidify lost them and the alpha wrap
    /// (alpha relative to that diagonal) wrapped everything into 8 or 10 triangles: Apply() true, IoU 0.04 .. 1 (by
    /// samples). The spheres' union and the tetrahedron must be kept. Closer (1e3 .. 2e4), where the exact resolve
    /// succeeds, the repair stays as it was: clean.
    /// </summary>
    public class RobustRepairFarPartTests
    {
        internal static DMesh3 Scene(double far, int edgeVertices = 20)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, new Vector3d(0.5, 0.3, 0.2));
            new MeshEditor(m).AppendMesh(s);
            var p = new Vector3d(far, far, far);
            int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + Vector3d.AxisX), v2 = m.AppendVertex(p + Vector3d.AxisY), v3 = m.AppendVertex(p + Vector3d.AxisZ);
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
            return m;
        }

        // (per component, relative to a point of it: far from the origin the plain sum cancels)
        internal static double Volume(DMesh3 m)
        {
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            double volume = 0;
            foreach (var comp in comps.Components) {
                Vector3d o = m.GetVertex(m.GetTriangle(comp.Indices[0]).a);
                foreach (int t in comp.Indices) {
                    Index3i tv = m.GetTriangle(t);
                    volume += (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o)) / 6;
                }
            }
            return volume;
        }

        [Theory]
        [InlineData(3e4)]
        [InlineData(1e5)]
        [InlineData(1e8)]
        [InlineData(1e12)]
        public void FarPart_NotABlob_TheSpheresKept(double far)
        {
            // the reference: the spheres alone, repaired
            DMesh3 near = Scene(0);
            near.RemoveTriangle(near.MaxTriangleID - 1, true, false);
            for (int k = 2; k <= 4; ++k)
                near.RemoveTriangle(near.MaxTriangleID - k, true, false);
            var r0 = new MeshRobustRepair(new DMesh3(near, true));
            Assert.True(r0.Apply());
            double expected = Volume(r0.Mesh) + 1.0 / 6;
            var r = new MeshRobustRepair(Scene(far));
            bool ok = r.Apply();
            DMesh3 m = r.Mesh;
            Assert.False(r.UsedAlphaWrap, $"far {far}: the wrap ({m.TriangleCount} triangles)");
            Assert.True(m.TriangleCount >= 0.9 * r0.Mesh.TriangleCount, $"far {far}: {m.TriangleCount} triangles, ok {ok}");
            Assert.True(m.IsClosed());
            // (the safe rounding may move the surface by up to 1e-5 of the largest component's diagonal: at 3e4 the
            // coordinates' rounding makes it use part of that)
            Assert.True(Math.Abs(Volume(m) - expected) <= 1e-4 * expected, $"far {far}: volume {Volume(m)}, expected {expected}");
            // reported as not repaired: NotCleanReason, by the diagonal of all triangles, rejects it
            Assert.False(ok);
            Assert.NotEqual("", MeshRobustRepair.NotCleanReason(m, false));
        }

        [Theory]
        [InlineData(1e3)]
        [InlineData(1e4)]
        [InlineData(2e4)]
        public void NearerPart_RepairedAndClean(double far)
        {
            var r = new MeshRobustRepair(Scene(far));
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.False(r.UsedAlphaWrap);
        }
    }
}
