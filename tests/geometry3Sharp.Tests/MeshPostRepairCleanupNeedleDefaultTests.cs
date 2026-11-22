using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> procedural meshes with an interior needle (#1363) </summary>
    internal static class NeedleMeshes
    {
        internal static double Volume(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;

        internal static HashSet<Vector3d> Vertices(DMesh3 m) => new HashSet<Vector3d>(m.VertexIndices().Select(v => m.GetVertex(v)));

        // the box [-1, 1]^3 with one edge split at the fraction t from a corner: the same solid, 14 triangles, one needle
        internal static DMesh3 BoxWithSplitEdge(double t)
        {
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            int eid = b.EdgeIndices().First(x => { Index2i ev = b.GetEdgeV(x); return Math.Abs((b.GetVertex(ev.a) - b.GetVertex(ev.b)).Length - 2) < 1e-12; });
            Index2i evs = b.GetEdgeV(eid);
            b.SplitEdge(eid, out DMesh3.EdgeSplitInfo info);
            b.SetVertex(info.vNew, Vector3d.Lerp(b.GetVertex(evs.a), b.GetVertex(evs.b), t));
            return b;
        }

        // a sphere with the edge at its vertex split at the fraction t, the new vertex lifted by h along the normal: a needle bump on a smooth surface. Returns the
        // sphere as it was, the mesh with the bump and the vertex that is the end of the edge nearer to the new one
        internal static DMesh3 SphereWithBump(double t, double h, out DMesh3 original, out int vNear)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { EdgeVertices = 12, Radius = 1 }.Generate().MakeDMesh();
            original = new DMesh3(m);
            int eid = m.EdgeIndices().First(e => m.GetEdgeT(e).b != DMesh3.InvalidID);
            Index2i ev = m.GetEdgeV(eid);
            vNear = ev.a;
            m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info);
            Vector3d p = Vector3d.Lerp(m.GetVertex(ev.a), m.GetVertex(ev.b), t);
            m.SetVertex(info.vNew, p + h * p.Normalized);
            return m;
        }
    }

    /// <summary>
    /// The needle collapse of the default MeshPostRepairCleanup (#1363): onto a corner or the end that keeps the volume, not the midpoint. These tests set none of the new
    /// options (MeshPostRepairCleanupNeedleTests does), so they build against the library as it was and fail there: the midpoint collapse moved the corner.
    /// </summary>
    public class MeshPostRepairCleanupNeedleDefaultTests
    {
        // the box with a needle at a corner (the split vertex lies on the box edge, 2e-5 from the corner): the default cleanup keeps the corner and the solid
        [Fact]
        public void Default_NeedleAtACorner_KeepsTheCornerAndTheSolid()
        {
            DMesh3 box = NeedleMeshes.BoxWithSplitEdge(1e-5);
            new MeshPostRepairCleanup(box).Apply();
            Assert.Equal(12, box.TriangleCount);
            Assert.Equal(8, box.VertexCount);
            Assert.All(NeedleMeshes.Vertices(box), p => Assert.True(Math.Abs(p.x) == 1 && Math.Abs(p.y) == 1 && Math.Abs(p.z) == 1, p.ToString()));
            Assert.Equal(8, NeedleMeshes.Volume(box), 12);
            ResultChecks.AssertClean(box);
        }

        // a needle bump on a smooth sphere: the default cleanup gives back the sphere as it was (the volume-best end), closed, manifold and finite
        [Theory]
        [InlineData(1e-4)]
        [InlineData(1 - 1e-4)]
        public void Default_NeedleBumpOnASphere_RestoresTheSphere(double t)
        {
            DMesh3 m = NeedleMeshes.SphereWithBump(t, 1e-7, out DMesh3 original, out int _);
            new MeshPostRepairCleanup(m).Apply();
            Assert.Equal(original.TriangleCount, m.TriangleCount);
            Assert.True(NeedleMeshes.Vertices(original).SetEquals(NeedleMeshes.Vertices(m)));
            Assert.Equal(NeedleMeshes.Volume(original), NeedleMeshes.Volume(m), 12);
            Assert.True(m.IsClosed());
            Assert.All(m.VertexIndices(), v => Assert.True(m.GetVertex(v).IsFinite));
            ResultChecks.AssertClean(m);
        }

        // MeshAutoRepair with the cleanup on (its default for PostRepairCleanup is set here): a needle on a closed sphere keeps the volume
        [Fact]
        public void AutoRepairWithCleanup_NeedleBump_KeepsTheVolume()
        {
            DMesh3 m = NeedleMeshes.SphereWithBump(1e-4, 1e-7, out DMesh3 original, out int _);
            var repair = new MeshAutoRepair(m) { PostRepairCleanup = true };
            Assert.True(repair.Apply());
            Assert.Equal(NeedleMeshes.Volume(original), NeedleMeshes.Volume(repair.Mesh), 12);
            ResultChecks.AssertClean(repair.Mesh);
        }
    }
}
