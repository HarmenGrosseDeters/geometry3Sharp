using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The needle collapse of MeshPostRepairCleanup (#1363): between two interior vertices onto the end that is a corner or a crease, else the one that keeps the volume
    /// best, and not if the discarded vertex moves by more than MaxCollapseDisplacement of the diagonal. (It collapsed to the midpoint, with no bound, and moved
    /// corners by half the edge.)
    /// </summary>
    public class MeshPostRepairCleanupNeedleTests
    {
        static double Volume(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;

        static HashSet<Vector3d> Vertices(DMesh3 m) => new HashSet<Vector3d>(m.VertexIndices().Select(v => m.GetVertex(v)));

        // the box [-1, 1]^3 with one edge split at the fraction t from a corner: the same solid, 14 triangles, one needle
        static DMesh3 BoxWithSplitEdge(double t)
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
        static DMesh3 SphereWithBump(double t, double h, out DMesh3 original, out int vNear)
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

        static MeshPostRepairCleanup Run(DMesh3 mesh, double maxDisplacement = 1e-4)
        {
            var c = new MeshPostRepairCleanup(mesh) { MaxCollapseDisplacement = maxDisplacement };
            c.Apply();
            return c;
        }

        // a needle at a corner of a box (the new vertex lies on the box edge): the corner stays, the solid is the box
        [Theory]
        [InlineData(1e-5)]
        [InlineData(1e-6)]
        public void NeedleAtACorner_KeepsTheCorner(double t)
        {
            DMesh3 box = BoxWithSplitEdge(t);
            DMesh3 plain = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            var c = Run(box);
            Assert.Equal(1, c.FixedDegenerate);
            Assert.Equal(plain.TriangleCount, box.TriangleCount);
            Assert.True(Vertices(plain).SetEquals(Vertices(box)));
            Assert.Equal(8, Volume(box), 12);
            ResultChecks.AssertClean(box);
        }

        // the collapse is not bounded for a needle that lies on a straight edge, but the corner is kept: also with the bound off
        [Fact]
        public void NeedleAtACorner_BoundOff_StillKeepsTheCorner()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            Run(box, 0);
            Assert.Equal(12, box.TriangleCount);
            Assert.Equal(8, Volume(box), 12);
            Assert.True(Vertices(box).Count == 8 && Vertices(box).All(p => Math.Abs(p.x) == 1 && Math.Abs(p.y) == 1 && Math.Abs(p.z) == 1));
        }

        // a needle longer than the bound (edge 2e-3, diagonal 3.46: 5.8e-4 of it) is left as it is: nothing moves
        [Fact]
        public void NeedleLongerThanTheBound_IsSkipped()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            DMesh3 before = new DMesh3(box);
            Run(box);       // (the flips of the fallback keep the vertices)
            Assert.Equal(before.TriangleCount, box.TriangleCount);
            Assert.True(Vertices(before).SetEquals(Vertices(box)));
            Assert.Equal(8, Volume(box), 12);
        }

        // a bump on a smooth surface: neither end is a corner, the collapse keeps the end that keeps the volume (the sphere as it was), not the midpoint
        [Theory]
        [InlineData(1e-4)]
        [InlineData(1 - 1e-4)]       // (the new vertex near the other end of the edge)
        public void NeedleBumpOnASmoothSurface_KeepsTheVolume(double t)
        {
            DMesh3 m = SphereWithBump(t, 1e-7, out DMesh3 original, out int vNear);
            Vector3d kept = m.GetVertex(vNear);
            double v0 = Volume(original);
            Assert.NotEqual(v0, Volume(m));
            var c = Run(m);
            Assert.Equal(1, c.FixedDegenerate);
            Assert.Equal(original.TriangleCount, m.TriangleCount);
            Assert.True(Vertices(original).SetEquals(Vertices(m)));
            Assert.Equal(v0, Volume(m), 12);
            ResultChecks.AssertClean(m);
        }

        // under every similarity the same collapse (the kept end and the result: the vertices of the transformed sphere), 2^k scales bit for bit
        [Theory]
        [InlineData(1e-4)]
        [InlineData(1 - 1e-4)]
        public void NeedleBump_InvariantUnderSimilarities(double t)
        {
            DMesh3 reference = SphereWithBump(t, 1e-7, out DMesh3 original, out int _);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(reference, sim);
                var c = Run(m);
                Assert.Equal(1, c.FixedDegenerate);
                Assert.Equal(original.TriangleCount, m.TriangleCount);
                Assert.True(Vertices(Invariance.Transformed(original, sim)).SetEquals(Vertices(m)), sim.Name);
            }
        }

        // one instance, two meshes: the second result is that of a new instance
        [Fact]
        public void Reuse_OneInstanceTwiceGivesTheResultOfANewOne()
        {
            DMesh3 a = SphereWithBump(1e-4, 1e-7, out DMesh3 original, out int _);
            var c = new MeshPostRepairCleanup(a);
            c.Apply();
            DMesh3 b = BoxWithSplitEdge(1e-5);
            c.Mesh = b;
            c.Apply();
            DMesh3 expected = BoxWithSplitEdge(1e-5);
            Run(expected);
            Assert.Equal(1, c.FixedDegenerate);
            Assert.Equal(expected.TriangleCount, b.TriangleCount);
            Assert.True(Vertices(expected).SetEquals(Vertices(b)));
            // and again on the result: nothing left to do
            c.Apply();
            Assert.Equal(0, c.FixedDegenerate);
            Assert.True(Vertices(expected).SetEquals(Vertices(b)));
        }
    }
}
