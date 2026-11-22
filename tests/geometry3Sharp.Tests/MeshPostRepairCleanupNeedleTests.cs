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
        static double Volume(DMesh3 mesh) => NeedleMeshes.Volume(mesh);

        static HashSet<Vector3d> Vertices(DMesh3 m) => NeedleMeshes.Vertices(m);

        static DMesh3 BoxWithSplitEdge(double t) => NeedleMeshes.BoxWithSplitEdge(t);

        static DMesh3 SphereWithBump(double t, double h, out DMesh3 original, out int vNear) => NeedleMeshes.SphereWithBump(t, h, out original, out vNear);

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
            var c = Run(box);       // (the flips of the fallback keep the vertices)
            Assert.Equal(before.TriangleCount, box.TriangleCount);
            Assert.True(Vertices(before).SetEquals(Vertices(box)));
            Assert.Equal(8, Volume(box), 12);
            // a second Apply() on the same instance changes nothing more (the needle stays, it is not collapsed in a later pass either)
            c.Apply();
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
