using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The flip test of an edge collapse (MeshRefinerBase.edge_flip_metric) compared the dot product of two unnormalised face normals (of
    /// the size of an edge length squared) with 0: below an edge length of 2^-269 the dot product (the size of the edge length to the
    /// 4th) underflowed to 0 and every collapse was refused as a flip (ReduceToTriangleCount did nothing), and above 2^256 it overflowed
    /// (inf - inf is NaN, which accepted a real flip)
    /// </summary>
    public class ReducerFlipScaleTests
    {
        sealed class Probe : MeshRefinerBase
        {
            public Probe(double tol) : base(new DMesh3()) { EdgeFlipTolerance = tol; }
            public double Metric(Vector3d a, Vector3d b) { return edge_flip_metric(ref a, ref b); }
        }

        public static System.Collections.Generic.IEnumerable<object[]> Exponents()
        {
            foreach (int e in new[] { -500, -400, -300, -270, -100, 0, 100, 260, 300, 400, 500 })
                foreach (double tol in new[] { 0.0, 0.5 })
                    yield return new object[] { e, tol };
        }

        [Theory]
        [MemberData(nameof(Exponents))]
        public void FlipMetric_HasTheRightSign_AtEveryScale(int e, double tol)
        {
            double s = Math.Pow(2, e);          // (the normals are of the size of an edge length squared: edge 2^(e/2))
            var p = new Probe(tol);
            var n = new Vector3d(0.3 * s, -0.4 * s, 0.9 * s);
            Assert.True(p.Metric(n, n) > tol, "parallel normals are not a flip");
            Assert.True(p.Metric(n, -n) <= tol, "opposite normals are a flip");
            Assert.True(p.Metric(n, new Vector3d(-0.9 * s, 0, 0.3 * s)) <= tol + 1e-12 || tol == 0, "(perpendicular)");
            Assert.True(p.Metric(n, Vector3d.Zero) <= tol, "a zero normal is a flip, as before");
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        public void FlipMetric_OfAPower2Scaling_IsTheSameBitForBit_OutsideTheOrdinaryRange(double tol)
        {
            var p = new Probe(tol);
            Vector3d a = new Vector3d(0.3, -0.4, 0.9), b = new Vector3d(0.35, -0.3, 0.8);
            // (outside the range [1e-145, 1e145] of the largest component, ie 2^482, both normals take the scaled path)
            double high = p.Metric(a * Math.Pow(2, 500), b * Math.Pow(2, 500)), low = p.Metric(a * Math.Pow(2, -500), b * Math.Pow(2, -500));
            foreach (int e in new[] { 490, 600, 700, 900 })
                Assert.Equal(high, p.Metric(a * Math.Pow(2, e), b * Math.Pow(2, e)));
            foreach (int e in new[] { -490, -600, -700 })
                Assert.Equal(low, p.Metric(a * Math.Pow(2, e), b * Math.Pow(2, e)));
            Assert.Equal(high, low);
        }

        static DMesh3 Grid(int n, double s)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                    m.AppendVertex(new Vector3d(i * s, j * s, 0.1 * s * Math.Sin(i + j)));
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, c, b); m.AppendTriangle(b, c, d);
                }
            return m;
        }

        [Theory]
        [InlineData(-270)]
        [InlineData(-300)]
        [InlineData(-400)]
        [InlineData(-480)]
        public void ReduceToTriangleCount_OfASmallMesh_Reduces(int e)
        {
            DMesh3 m = Grid(7, Math.Pow(2, e));
            Assert.Equal(98, m.TriangleCount);
            new Reducer(m).ReduceToTriangleCount(30);
            Assert.True(m.TriangleCount <= 40, "triangles left: " + m.TriangleCount);
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
        }
    }
}

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The quadric errors of the Reducer (area weighted: the extent to the 4th) left the range of a double above an extent of about 1e77
    /// and below about 2^-268: all priorities were equal and the order was that of the ids, so the reduction of a 2^k scaled copy was not
    /// the scaled reduction (T13 rank 6: an octahedron of radius 2e146)
    /// </summary>
    public class ReducerQuadricFrameTests
    {
        static DMesh3 Grid(int n, double s)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                    m.AppendVertex(new Vector3d(i * s, j * s, 0.1 * s * Math.Sin(i * 1.7 + j * 0.9) + 0.03 * s * i * j / 7.0));
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, c, b); m.AppendTriangle(b, c, d);
                }
            return m;
        }

        static string Signature(DMesh3 m, double inverseScale)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
            }
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v) * inverseScale;
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            return sb.ToString();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReduceToTriangleCount_OfA2kScaledCopy_IsTheScaledReduction(bool areaWeighted)
        {
            DMesh3 reference = Grid(9, 1);
            new Reducer(reference) { AreaWeightedQuadrics = areaWeighted }.ReduceToTriangleCount(40);
            string expected = Signature(reference, 1);
            Assert.True(reference.TriangleCount <= 40);
            foreach (int e in new[] { -400, -300, -270, -100, -40, 40, 100, 200, 260, 300, 400, 480 }) {
                double s = Math.Pow(2, e);
                DMesh3 m = Grid(9, s);
                new Reducer(m) { AreaWeightedQuadrics = areaWeighted }.ReduceToTriangleCount(40);
                Assert.Equal(expected, Signature(m, 1.0 / s));
            }
        }

        [Fact]
        public void ReduceToMaxQuadricError_ScalesItsLimitWithTheMesh()
        {
            // the limit is in the units of the mesh: length^4 (area weighted)
            DMesh3 reference = Grid(9, 1);
            var rr = new Reducer(reference);
            rr.ReduceToMaxQuadricError(1e-2);
            Assert.True(reference.TriangleCount < 162);
            string expected = Signature(reference, 1);
            foreach (int e in new[] { -100, -60, 60, 100 }) {
                double s = Math.Pow(2, e);
                DMesh3 m = Grid(9, s);
                new Reducer(m).ReduceToMaxQuadricError(1e-2 * Math.Pow(s, 4));
                Assert.Equal(expected, Signature(m, 1.0 / s));
            }
        }
    }
}
