using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The flip test of an edge collapse above an edge length of 1e154: the cross products of the face normals overflowed; an infinite
    /// normal refused the collapse, but inf - inf is NaN and a NaN metric accepted a flip blindly (FINDINGS item 10). The edges are
    /// scaled by one factor now, so the test decides as at unit size
    /// </summary>
    public class FlipTestHugeScaleTests
    {
        sealed class Probe : MeshRefinerBase
        {
            public Probe(DMesh3 m) : base(m) { }
            public bool Flips(int vid, int vother, Vector3d newv) { return collapse_creates_flip_or_invalid(vid, vother, ref newv, DMesh3.InvalidID, DMesh3.InvalidID); }
        }

        // a triangle (v0, v1, v2) in the plane z = 0 and an isolated vertex v3 (the "other" of the collapse)
        static DMesh3 Triangle(double s)
        {
            var m = new DMesh3(false, false, false, false);
            m.AppendVertex(new Vector3d(0, 0, 0));
            m.AppendVertex(new Vector3d(s, 0, 0));
            m.AppendVertex(new Vector3d(0, s, 0));
            m.AppendVertex(new Vector3d(5 * s, 5 * s, 0));
            m.AppendTriangle(0, 1, 2);
            return m;
        }

        public static System.Collections.Generic.IEnumerable<object[]> Scales()
        {
            foreach (double s in new[] { 1e-310, 1e-305, 1e-3, 1.0, 1e60, 1e80, 1e120, 1e150, 1e154, 1e160, 1e200, 1e250, 1e300 })
                yield return new object[] { s };
        }

        [Theory]
        [MemberData(nameof(Scales))]
        public void AMoveWithinTheSameSide_IsNotAFlip_AtAnyScale(double s)
        {
            var p = new Probe(Triangle(s));
            Assert.False(p.Flips(0, 3, new Vector3d(0.1 * s, 0.1 * s, 0)));          // vertex 0 moves inside: same orientation
        }

        [Theory]
        [MemberData(nameof(Scales))]
        public void AMoveAcrossTheOppositeEdge_IsAFlip_AtAnyScale(double s)
        {
            var p = new Probe(Triangle(s));
            Assert.True(p.Flips(0, 3, new Vector3d(2 * s, 2 * s, 0)));               // vertex 0 jumps over the edge (v1, v2): the normal turns
            Assert.True(p.Flips(0, 3, new Vector3d(0.5 * s, 0.5 * s, 0)));           // onto the edge (v1, v2): a zero normal
        }

        // random triangles in space and random new positions of one corner: the decision at a 2^k scaled copy is the unit-size decision (the
        // scale is exact), also where the cross products overflow
        [Theory]
        [InlineData(600)]
        [InlineData(750)]
        [InlineData(900)]
        [InlineData(-600)]
        public void TheDecisionOfARandomCollapse_IsTheSameAtA2kScaledCopy(int e)
        {
            var rnd = new Random(2026);
            double k = Math.Pow(2, e);
            int flips = 0, noFlips = 0;
            for (int i = 0; i < 400; i++) {
                Vector3d a = Rand(rnd), b = Rand(rnd), c = Rand(rnd), n = Rand(rnd);
                bool unit = new Probe(Tri(a, b, c, 1)).Flips(0, 3, n);
                bool scaled = new Probe(Tri(a, b, c, k)).Flips(0, 3, n * k);
                Assert.Equal(unit, scaled);
                if (unit) flips++; else noFlips++;
            }
            Assert.True(flips > 40 && noFlips > 40, flips + " flips, " + noFlips + " no flips");
        }

        // the collapsed vertex is the b or c corner of its triangle (the other operand orders of the fused cross products)
        [Theory]
        [MemberData(nameof(Scales))]
        public void TheOtherCorners_DecideLikeTheFirst_AtAnyScale(double s)
        {
            var p = new Probe(Triangle(s));
            Assert.False(p.Flips(1, 3, new Vector3d(0.4 * s, 0.1 * s, 0)));
            Assert.True(p.Flips(1, 3, new Vector3d(-s, -s, 0)));
            Assert.False(p.Flips(2, 3, new Vector3d(0.1 * s, 0.4 * s, 0)));
            Assert.True(p.Flips(2, 3, new Vector3d(-s, -s, 0)));
        }

        static Vector3d Rand(Random r) { return new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5); }

        static DMesh3 Tri(Vector3d a, Vector3d b, Vector3d c, double k)
        {
            var m = new DMesh3(false, false, false, false);
            m.AppendVertex(a * k); m.AppendVertex(b * k); m.AppendVertex(c * k); m.AppendVertex(new Vector3d(7, 7, 7) * k);
            m.AppendTriangle(0, 1, 2);
            return m;
        }
    }
}
