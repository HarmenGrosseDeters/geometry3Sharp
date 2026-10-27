using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Absolute thresholds in triangle queries (docs/ROBUSTNESS_SWEEP.md S1, S15): DistTriangle3Triangle3 reported
    /// squared distances up to 1e-8 (distances up to 1e-4) as 0, and IntersectionUtil.Intersects counted every triangle
    /// with edges below about 1e-4 as parallel to the ray. Both are relative to the triangles' size now.
    /// </summary>
    public class QueryThresholdScaleTests
    {
        static Triangle3d Tri(Similarity s, Vector3d a, Vector3d b, Vector3d c) => new Triangle3d(s.Apply(a), s.Apply(b), s.Apply(c));

        // two triangles 1e-3 apart (along z), the second one shifted sideways
        static readonly Vector3d[] A = { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
        static readonly Vector3d[] B = { new Vector3d(0.2, 0.2, 1e-3), new Vector3d(1.2, 0.3, 1e-3), new Vector3d(0.3, 1.1, 1e-3) };

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void TriangleTriangleDistance_ScalesWithTheInput(string name)
        {
            Similarity s = Similarity.ByName(name);
            var d0 = new DistTriangle3Triangle3(Tri(Similarity.Identity, A[0], A[1], A[2]), Tri(Similarity.Identity, B[0], B[1], B[2]));
            var d1 = new DistTriangle3Triangle3(Tri(s, A[0], A[1], A[2]), Tri(s, B[0], B[1], B[2]));
            double a = d0.Get(), b = d1.Get() / s.Scale;
            Assert.True(Math.Abs(a - 1e-3) < 1e-12, $"identity: {a}");
            if (s.IsExact)
                Assert.Equal(a, b);
            else
                Assert.True(Math.Abs(a - b) < 1e-9 + 100 * s.Resolution(2), $"{name}: {a} vs {b}");
        }

        [Theory]
        [InlineData(-30)]
        [InlineData(-10)]
        [InlineData(0)]
        [InlineData(20)]
        public void TriangleTriangleDistance_SmallGaps_NotZero(int exponent)
        {
            // gaps of 1e-6 of the size: upstream returned 0 below 1e-4 absolute
            var s = new Similarity("2^" + exponent, Math.Pow(2, exponent), Matrix3d.Identity, Vector3d.Zero);
            var q = new DistTriangle3Triangle3(Tri(s, A[0], A[1], A[2]),
                Tri(s, new Vector3d(0.2, 0.2, 1e-6), new Vector3d(1.2, 0.3, 1e-6), new Vector3d(0.3, 1.1, 1e-6)));
            // (the squared-distance formulation leaves an error of about 1e-8 of the size: 2.8e-12 here)
            double d = q.Get() / s.Scale;
            Assert.True(Math.Abs(d - 1e-6) < 1e-11, $"distance {d}");
        }

        [Theory]
        [InlineData(-30)]
        [InlineData(0)]
        [InlineData(20)]
        public void TriangleTriangleDistance_Intersecting_Zero(int exponent)
        {
            var s = new Similarity("2^" + exponent, Math.Pow(2, exponent), Matrix3d.Identity, Vector3d.Zero);
            var q = new DistTriangle3Triangle3(Tri(s, A[0], A[1], A[2]),
                Tri(s, new Vector3d(0.2, 0.2, -0.5), new Vector3d(0.3, 0.2, 0.5), new Vector3d(0.2, 0.3, 0.5)));
            Assert.Equal(0, q.Get());
        }

        public static TheoryData<string, int> TouchingCases()
        {
            var d = new TheoryData<string, int>();
            foreach (object[] n in Similarity.Names())
                for (int c = 0; c < 3; ++c) d.Add((string)n[0], c);
            return d;
        }

        [Theory]
        [MemberData(nameof(TouchingCases))]
        public void TriangleTriangleDistance_Touching_ExactlyZero(string name, int touching)
        {
            // a shared vertex, a shared edge (coplanar neighbours), a vertex on the other's face: the edge and point
            // queries leave rounding noise, which must still count as touching in every placement
            Similarity s = Similarity.ByName(name);
            Vector3d[] b;
            if (touching == 0) b = new[] { A[1], new Vector3d(2, 0.3, 0.4), new Vector3d(1.5, 1, -0.2) };
            else if (touching == 1) b = new[] { A[1], A[2], new Vector3d(1, 1, 0) };
            else b = new[] { new Vector3d(0.3, 0.3, 0), new Vector3d(0.6, 0.4, 1), new Vector3d(0.2, 0.7, 1) };
            var q = new DistTriangle3Triangle3(Tri(s, A[0], A[1], A[2]), Tri(s, b[0], b[1], b[2]));
            Assert.Equal(0, q.Get());
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void IntersectionUtil_SmallTriangles_HitUnderSimilarity(string name)
        {
            // a triangle of 2^-20 (edges about 1e-6) in every placement
            Similarity s = Similarity.ByName(name);
            double k = Math.Pow(2, -20);
            Vector3d v0 = s.Apply(k * A[0]), v1 = s.Apply(k * A[1]), v2 = s.Apply(k * A[2]);
            Vector3d o = s.Apply(k * new Vector3d(0.25, 0.25, 1)), down = s.ApplyDirection(-Vector3d.AxisZ);
            Assert.True(IntersectionUtil.Intersects(o, down, v0, v1, v2), name);
            Assert.False(IntersectionUtil.Intersects(o, -down, v0, v1, v2), name);
        }

        [Theory]
        [InlineData(-30)]
        [InlineData(-10)]
        [InlineData(0)]
        public void LineTriangleDistance_ThroughSmallTriangle_Zero(int exponent)
        {
            // UnitCross zeroed the normal of triangles with edges below about 1e-8: the line counted as parallel and the
            // distance came from the edges
            double k = Math.Pow(2, exponent);
            var tri = new Triangle3d(k * A[0], k * A[1], k * A[2]);
            var line = new Line3d(k * new Vector3d(0.25, 0.25, 1), new Vector3d(0.1, 0.05, -1).Normalized);
            var q = new DistLine3Triangle3(line, tri);
            Assert.Equal(0, q.GetSquared());
        }

        [Fact]
        public void FindNearestTriangles_MillimetreModels_ReportTheGap()
        {
            // two millimetre boxes 1e-5 apart (1% of their size): upstream reported distance 0
            DMesh3 a = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, 0.5e-3 * Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1e-3 + 1e-5, 0, 0), 0.5e-3 * Vector3d.One) }.Generate().MakeDMesh();
            var ta = new DMeshAABBTree3(a, true);
            var tb = new DMeshAABBTree3(b, true);
            ta.FindNearestTriangles(tb, null, out double distance);
            Assert.True(Math.Abs(distance - 1e-5) < 1e-12, $"distance {distance}");
        }

        [Theory]
        [InlineData(-30)]
        [InlineData(-20)]
        [InlineData(-10)]
        [InlineData(0)]
        [InlineData(20)]
        public void IntersectionUtil_SmallTriangles_Hit(int exponent)
        {
            // a ray straight through the triangle: upstream missed it once Dot(D, E1 x E2) fell below 1e-8
            double k = Math.Pow(2, exponent);
            Vector3d v0 = k * A[0], v1 = k * A[1], v2 = k * A[2];
            Vector3d o = k * new Vector3d(0.25, 0.25, 1);
            Assert.True(IntersectionUtil.Intersects(o, -Vector3d.AxisZ, v0, v1, v2));
            Assert.False(IntersectionUtil.Intersects(o, Vector3d.AxisZ, v0, v1, v2));
            // a ray parallel to the triangle still misses
            Assert.False(IntersectionUtil.Intersects(k * new Vector3d(-1, 0.25, 0), Vector3d.AxisX, v0, v1, v2));
        }
    }
}
