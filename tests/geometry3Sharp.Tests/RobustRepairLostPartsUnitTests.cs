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
            // open: a part with a boundary edge (the small sphere with 6 triangles removed), not the closed unit sphere
            var open = MeshRobustRepairLostParts.Parts(Scene("open", 4, 16));
            Assert.Equal(2, open.Count);
            Assert.False(open[0].Open);
            Assert.True(open[1].Open);
            Assert.All(MeshRobustRepairLostParts.Parts(Scene("boxes", 16, 2)), p => Assert.False(p.Open));
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
            Assert.Same(parts[1], MeshRobustRepairLostParts.FirstLost(parts, sphere));
            Assert.Null(MeshRobustRepairLostParts.FirstLost(parts, input));
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

        static DMesh3 Boxes(params (double h, Vector3d c)[] boxes)
        {
            var m = new DMesh3();
            foreach (var b in boxes) append(m, cube(b.h, b.c), p => p);
            return m;
        }

        [Fact]
        public void Parts_SeparatedAlongYOrZ_AreTwo_TouchingBoxesAreOne()
        {
            // the sweep is along x: boxes that overlap in x but lie apart in y or z are two parts; boxes that touch exactly are one
            Assert.Equal(2, MeshRobustRepairLostParts.Parts(Boxes((1, Vector3d.Zero), (1, new Vector3d(0.5, 5, 0)))).Count);
            Assert.Equal(2, MeshRobustRepairLostParts.Parts(Boxes((1, Vector3d.Zero), (1, new Vector3d(0.5, 0, 5)))).Count);
            Assert.Null(MeshRobustRepairLostParts.Parts(Boxes((1, Vector3d.Zero), (1, new Vector3d(2, 0, 0)))));
            // each box padded by 1e-3 of its diagonal (2 sqrt 3: 0.0035 each, 0.0069 together): a gap of 0.005 is one part, a gap of 0.06
            // (below the 0.069 of a padding of 1e-2) two
            Assert.Null(MeshRobustRepairLostParts.Parts(Boxes((1, Vector3d.Zero), (1, new Vector3d(2.005, 0, 0)))));
            Assert.Equal(2, MeshRobustRepairLostParts.Parts(Boxes((1, Vector3d.Zero), (1, new Vector3d(2.06, 0, 0)))).Count);
        }

        [Fact]
        public void Parts_MinTrianglesAndExtent_Judged()
        {
            DMesh3 sphereOnly = new DMesh3();
            append(sphereOnly, icosphere(1, 1, Vector3d.Zero), p => p);
            // a part of 3 triangles is not judged, one of 4 is
            foreach (int n in new[] { 3, 4 }) {
                DMesh3 m = new DMesh3(sphereOnly);
                var c = new Vector3d(10, 0, 0);
                int[] v = { m.AppendVertex(c), m.AppendVertex(c + Vector3d.AxisX), m.AppendVertex(c + Vector3d.AxisY), m.AppendVertex(c + Vector3d.AxisZ), m.AppendVertex(c - Vector3d.AxisY) };
                var fan = new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3), new Index3i(0, 3, 4), new Index3i(0, 4, 1) };
                for (int k = 0; k < n; ++k) m.AppendTriangle(v[fan[k].a], v[fan[k].b], v[fan[k].c]);
                var parts = MeshRobustRepairLostParts.Parts(m);
                Assert.Equal(n == 4, MeshRobustRepairLostParts.AnyLost(parts, sphereOnly));
            }
            // a part of 4 triangles without extent (all corners at one point) is not judged
            DMesh3 point = new DMesh3(sphereOnly);
            var q = new Vector3d(10, 0, 0);
            for (int k = 0; k < 4; ++k) point.AppendTriangle(point.AppendVertex(q), point.AppendVertex(q), point.AppendVertex(q));
            Assert.False(MeshRobustRepairLostParts.AnyLost(MeshRobustRepairLostParts.Parts(point), sphereOnly));
        }

        [Fact]
        public void Parts_TooManyComponents_NotChecked_AndSaysSo()
        {
            // MaxComponents components are judged, one more is a soup: no parts, the reason given
            foreach (int extra in new[] { 0, 1 }) {
                DMesh3 m = new DMesh3();
                append(m, icosphere(1, 1, Vector3d.Zero), p => p);
                for (int k = 0; k < MeshRobustRepairLostParts.MaxComponents - 1 + extra; ++k) {
                    var c = new Vector3d(10 + 2 * (k % 64), 2 * (k / 64), 0);
                    m.AppendTriangle(m.AppendVertex(c), m.AppendVertex(c + Vector3d.AxisX), m.AppendVertex(c + Vector3d.AxisY));
                }
                string skipped;
                var parts = MeshRobustRepairLostParts.Parts(m, out skipped);
                if (extra == 0) {
                    Assert.NotNull(parts);
                    Assert.Equal("", skipped);
                } else {
                    Assert.Null(parts);
                    Assert.Equal("too many components", skipped);
                }
            }
        }

        [Fact]
        public void AnyLost_TinyPart_ItsSquaredReachWouldUnderflow_IsPresent()
        {
            // a part of extent 2^-600 (its reach squared is 0 in doubles) that the result contains unchanged is present; at the origin, so
            // that its coordinates are representable
            double h = Math.Pow(2, -600);
            DMesh3 m = Boxes((1, new Vector3d(4, 0, 0)), (h, Vector3d.Zero));
            var parts = MeshRobustRepairLostParts.Parts(m);
            Assert.Equal(2, parts.Count);
            Assert.True(parts[1].Diagonal > 0 && parts[1].Diagonal < 1e-170);
            Assert.False(MeshRobustRepairLostParts.AnyLost(parts, m));
            // and lost without it
            Assert.True(MeshRobustRepairLostParts.AnyLost(parts, Boxes((1, new Vector3d(4, 0, 0)))));
            // the diagonal itself, without underflow: 2^-600 sqrt 12 for that box
            Assert.Equal(h * Math.Sqrt(12), parts[1].Diagonal, h * 1e-12);
        }

        [Fact]
        public void Apply_SaysWhyTheCheckDidNotJudge()
        {
            var one = new MeshRobustRepair(Boxes((1, Vector3d.Zero)));
            Assert.True(one.Apply());
            Assert.Equal("one part", one.LostPartCheckSkipped);
            var two = new MeshRobustRepair(Boxes((1, Vector3d.Zero), (1, new Vector3d(5, 0, 0))));
            Assert.True(two.Apply());
            Assert.Equal("", two.LostPartCheckSkipped);
            var noVolume = new MeshRobustRepair(Boxes((1, Vector3d.Zero), (1, new Vector3d(5, 0, 0)))) { MinVolumeIoU = 0 };
            Assert.True(noVolume.Apply());
            Assert.Equal("no volume checks", noVolume.LostPartCheckSkipped);
            var off = new MeshRobustRepair(Boxes((1, Vector3d.Zero), (1, new Vector3d(5, 0, 0)))) { NoLostPartCheck = true };
            Assert.True(off.Apply());
            Assert.Equal("off", off.LostPartCheckSkipped);
        }
    }
}
