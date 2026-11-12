using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Issue #628: the shoelace sum in absolute coordinates cancelled catastrophically for a small loop far from the origin.
    public class PolygonSignedAreaTests
    {
        // counter-clockwise, asymmetric (an L shape with a notch), area 4.25
        static readonly Vector2d[] Notched = {
            new Vector2d(0, 0), new Vector2d(3, 0), new Vector2d(3, 1), new Vector2d(1.5, 1),
            new Vector2d(1.5, 1.5), new Vector2d(1, 1.5), new Vector2d(1, 2), new Vector2d(0, 2) };

        static Vector2d[] Circle(int n)
        {
            var v = new Vector2d[n];
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                v[i] = new Vector2d(Math.Cos(a), Math.Sin(a));
            }
            return v;
        }

        // the loop in the orthonormal 2D frame of the transformed plane (as PlanarComplexNestingProbeTests does)
        static Polygon2d Transform(Vector2d[] loop, Similarity s)
        {
            Vector3d ex = s.ApplyDirection(Vector3d.AxisX), ey = s.ApplyDirection(Vector3d.AxisY);
            var p = new Polygon2d();
            foreach (Vector2d v in loop) {
                Vector3d q = s.Apply(new Vector3d(v.x, v.y, 0));
                p.AppendVertex(new Vector2d(q.Dot(ex), q.Dot(ey)));
            }
            return p;
        }

        public static IEnumerable<object[]> Names() => Similarity.Names();

        [Fact]
        public void Notched_AreaIsKnown()
        {
            Assert.Equal(4.25, new Polygon2d(Notched).SignedArea);
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void SignedArea_IsSimilarityInvariant(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (Vector2d[] loop in new[] { Notched, Circle(64) }) {
                double reference = new Polygon2d(loop).SignedArea;
                Polygon2d p = Transform(loop, s);
                double scaled = reference * s.Scale * s.Scale;
                if (s.IsExact)
                    Assert.Equal(scaled, p.SignedArea);
                else
                    Assert.True(Math.Abs(p.SignedArea / scaled - 1) < 1e-5, $"{name}: area {p.SignedArea:G17} instead of {scaled:G17}");
                Assert.False(p.IsClockwise);
                p.Reverse();
                Assert.True(p.IsClockwise);
                Assert.True(Math.Abs(p.SignedArea / -scaled - 1) < 1e-5);
            }
        }

        [Fact]
        public void SignedArea_DegenerateInputs()
        {
            Assert.Equal(0, new Polygon2d().SignedArea);
            var one = new Polygon2d(); one.AppendVertex(new Vector2d(3, 4));
            Assert.Equal(0, one.SignedArea);
            var two = new Polygon2d(); two.AppendVertex(new Vector2d(3, 4)); two.AppendVertex(new Vector2d(5, 9));
            Assert.Equal(0, two.SignedArea);
            var tri = new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(0, 2) });
            Assert.Equal(2, tri.SignedArea);
        }
    }
}
