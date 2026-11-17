using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> MeshRobustRepairLostParts on its own: the padded box rule and the per-part distance test (RobustRepairLostPartTests has the scenes) </summary>
    public class RobustRepairLostPartsUnitTests
    {
        static (List<Vector3d> v, List<Index3i> f) icosphere(int n, double r, Vector3d c) => RobustRepairLostPartTests.icosphere(n, r, c);
        static (List<Vector3d> v, List<Index3i> f) cube(double h, Vector3d c) => RobustRepairLostPartTests.cube(h, c);
        static void append(DMesh3 m, (List<Vector3d> v, List<Index3i> f) part, Func<Vector3d, Vector3d> map) => RobustRepairLostPartTests.append(m, part, map);
        static DMesh3 Scene(string kind, int r, int f) => RobustRepairLostPartTests.Scene(kind, r, f);

        [Fact]
        public void Parts_PaddedBoxRule()
        {
            // the unit sphere and the small box: two parts; two overlapping boxes count as one part
            Assert.Equal(2, MeshRobustRepairLostParts.Parts(Scene("box", 20, 2)).Count);
            Assert.Equal(2, MeshRobustRepairLostParts.Parts(Scene("boxes", 16, 2)).Count);
            // one part: not checked
            var sphere = new DMesh3();
            append(sphere, icosphere(1, 1, Vector3d.Zero), p => p);
            Assert.Null(MeshRobustRepairLostParts.Parts(sphere));
            // boxes whose padded boxes meet (a gap below 1e-3 of the diagonal) are one part
            var near = new DMesh3();
            append(near, cube(1, Vector3d.Zero), p => p);
            append(near, cube(1, new Vector3d(2 + 1e-4, 0, 0)), p => p);
            Assert.Null(MeshRobustRepairLostParts.Parts(near));
            var apart = new DMesh3();
            append(apart, cube(1, Vector3d.Zero), p => p);
            append(apart, cube(1, new Vector3d(2.1, 0, 0)), p => p);
            Assert.Equal(2, MeshRobustRepairLostParts.Parts(apart).Count);
        }

        [Fact]
        public void AnyLost_JudgesEachPartAgainstItsOwnSize()
        {
            DMesh3 input = Scene("box", 20, 2);
            List<MeshRobustRepairLostParts.Part> parts = MeshRobustRepairLostParts.Parts(input);
            // the input itself: nothing lost
            Assert.False(MeshRobustRepairLostParts.AnyLost(parts, input));
            // the large sphere alone: the box is lost
            var sphere = Scene("box", 20, 2);
            foreach (int t in sphere.TriangleIndices().ToArray())
                if (sphere.GetTriCentroid(t).x > 2) sphere.RemoveTriangle(t);
            Assert.True(MeshRobustRepairLostParts.AnyLost(parts, sphere));
            // the box moved by 0.3 of its own diagonal (beyond a quarter): lost; by 0.2: present
            double d = 2 * Math.Sqrt(3) * Math.Pow(2, -20);
            foreach (double shift in new[] { 0.3, 0.2 }) {
                var moved = Scene("box", 20, 2);
                foreach (int v in moved.VertexIndices().ToArray())
                    if (moved.GetVertex(v).x > 2) moved.SetVertex(v, moved.GetVertex(v) + new Vector3d(0, 0, shift * d + 2 * Math.Pow(2, -20)));
                Assert.Equal(shift > 0.25, MeshRobustRepairLostParts.AnyLost(parts, moved));
            }
            // an empty result loses everything
            Assert.True(MeshRobustRepairLostParts.AnyLost(parts, new DMesh3()));
            Assert.False(MeshRobustRepairLostParts.AnyLost(null, new DMesh3()));
        }
    }
}
