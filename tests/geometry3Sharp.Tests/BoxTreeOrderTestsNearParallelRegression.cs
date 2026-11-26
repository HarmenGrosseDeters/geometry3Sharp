using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class BoxTreeOrderTestsNearParallelRegression
    {
        [Theory]
        [InlineData(0)]
        [InlineData(600)]
        public void SegmentWithEndpointInsideReturnsItsBox(int exponent)
        {
            // #1673. The segment starts outside the box and ends on or inside its boundary.
            double s = Math.Pow(2, exponent);
            double face = 1e-310 * s;
            foreach (int sign in new[] { -1, 1 })
            {
                double lo = sign < 0 ? -s : face;
                double hi = sign < 0 ? -face : s;
                var box = new AxisAlignedBox3d(new Vector3d(lo, -s, -s), new Vector3d(hi, s, s));
                var tree = new TriangleBoxTree(new List<AxisAlignedBox3d> { box });
                var p = Vector3d.Zero;
                var hits = new List<int>();

                foreach (double x in new[] { face, 2 * face })
                {
                    var q = new Vector3d(sign * x, 0, 0);
                    tree.QuerySegment(p, q, 0, hits.Add);
                    Assert.Equal(0, Assert.Single(hits));
                    hits.Clear();
                    tree.QuerySegment(q, p, 0, hits.Add);
                    Assert.Equal(0, Assert.Single(hits));
                    hits.Clear();
                }

                // For the tiny-step branch only, both endpoints on the same outside side exclude the box.
                if (exponent == 0) {
                    tree.QuerySegment(p, new Vector3d(sign * face / 2, 0, 0), 0, hits.Add);
                    Assert.Empty(hits);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void InteriorEndpointRemainsACandidateUnderSimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var box = AxisAlignedBox3d.Empty;
            foreach (int x in new[] { 1, 2 })
                foreach (int y in new[] { -1, 1 })
                    foreach (int z in new[] { -1, 1 })
                        box.Contain(s.Apply(new Vector3d(x, y, z)));
            var tree = new TriangleBoxTree(new List<AxisAlignedBox3d> { box });
            var hits = new List<int>();

            tree.QuerySegment(s.Apply(Vector3d.Zero), s.Apply(new Vector3d(1.5, 0, 0)), 0, hits.Add);
            Assert.Equal(0, Assert.Single(hits));
        }
    }
}
