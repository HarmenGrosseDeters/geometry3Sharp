using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Issue #628: the shoelace sum in absolute coordinates cancelled catastrophically for a small loop far from the origin.
    public class PolygonSignedAreaTests
    {
        static Polygon2d Circle(Vector2d center, double radius, int n)
        {
            var p = new Polygon2d();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                p.AppendVertex(center + radius * new Vector2d(Math.Cos(a), Math.Sin(a)));
            }
            return p;
        }

        public static System.Collections.Generic.IEnumerable<object[]> Names() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Names))]
        public void SignedArea_IsTranslationAndScaleInvariant(string name)
        {
            Similarity s = Similarity.ByName(name);
            const int n = 64;
            double exact = 0.5 * n * Math.Sin(2 * Math.PI / n);      // area of the unit n-gon
            Vector3d o = s.Apply(Vector3d.Zero);
            Polygon2d p = Circle(new Vector2d(o.x, o.y), s.Scale, n);

            double area = p.SignedArea / (s.Scale * s.Scale);
            Assert.True(Math.Abs(area / exact - 1) < 1e-5, $"{name}: normalized area {area:G17} instead of {exact:G17}");
            Assert.Equal(exact > 0, p.SignedArea > 0);
            Assert.False(p.IsClockwise);
            p.Reverse();
            Assert.True(p.IsClockwise);
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
