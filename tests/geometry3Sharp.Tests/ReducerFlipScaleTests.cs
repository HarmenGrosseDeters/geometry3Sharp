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
            if (tol != 0)
                Assert.True(Math.Abs(p.Metric(n, new Vector3d(-0.9 * s, 0, 0.3 * s))) < 1e-12, "perpendicular: the cosine is 0");
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


        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        public void FlipMetric_OfMixedMagnitudesAndInfinity(double tol)
        {
            var p = new Probe(tol);
            var n = new Vector3d(0.3, -0.4, 0.9);
            // one normal of an ordinary size, the other far outside the range: still parallel / opposite
            Assert.True(p.Metric(n, n * Math.Pow(2, 500)) > tol);
            Assert.True(p.Metric(n * Math.Pow(2, -500), n) > tol);
            Assert.True(p.Metric(n, -n * Math.Pow(2, 500)) <= tol);
            // an infinite component (a cross product that overflowed) is a flip, as a zero normal
            Assert.True(p.Metric(n, new Vector3d(double.PositiveInfinity, 0, 0)) <= tol);
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

        // The frame is used from an extent of 2^32 (and below 2^-32); the grid of 8 cells has an extent of 8 * 2^e. The scaled copies on
        // both sides of the edge of the band must give the same (scaled) reduction as the unscaled mesh: no seam
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reduction_HasNoSeamAtTheEdgeOfTheFrameBand(bool areaWeighted)
        {
            DMesh3 reference = Grid(8, 1);
            new Reducer(reference) { AreaWeightedQuadrics = areaWeighted }.ReduceToTriangleCount(30);
            string expected = Signature(reference, 1);
            var exponents = new System.Collections.Generic.List<int>();
            for (int e = 26; e <= 32; e++) { exponents.Add(e); exponents.Add(-e - 3); }     // extent 8 * 2^e: from 2^29 to 2^35, and 2^-32 to 2^-26
            exponents.Add(-36); exponents.Add(-37);                                          // and below the band: 2^-33, 2^-34
            foreach (int e in exponents) {
                double s = Math.Pow(2, e);
                DMesh3 m = Grid(8, s);
                new Reducer(m) { AreaWeightedQuadrics = areaWeighted }.ReduceToTriangleCount(30);
                Assert.Equal(expected, Signature(m, 1.0 / s));
            }
        }

        // the limits are in the units of the caller: the same relative threshold gives the same reduction at every scale, also across
        // the edge of the band
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReduceToMaxQuadricError_KeepsItsUnits_AtEveryScale(bool areaWeighted)
        {
            double Limit(double s) { return areaWeighted ? 1e-2 * Math.Pow(s, 4) : 1e-2 * s * s; }
            DMesh3 reference = Grid(8, 1);
            new Reducer(reference) { AreaWeightedQuadrics = areaWeighted }.ReduceToMaxQuadricError(Limit(1));
            Assert.True(reference.TriangleCount < 128);
            string expected = Signature(reference, 1);
            foreach (int e in new[] { -100, -40, -35, -29, 26, 29, 35, 40, 60, 100 }) {
                double s = Math.Pow(2, e);
                DMesh3 m = Grid(8, s);
                new Reducer(m) { AreaWeightedQuadrics = areaWeighted }.ReduceToMaxQuadricError(Limit(s));
                Assert.Equal(expected, Signature(m, 1.0 / s));
            }
        }

        [Fact]
        public void PartitionedReducer_KeepsItsErrorInTheUnitsOfTheCaller()
        {
            DMesh3 reference = Grid(12, 1);
            var pr = new gs.PartitionedReducer() { MaxThreads = 1, MinTriangles = 0, Configure = r => { r.AreaWeightedQuadrics = false; } };
            DMesh3 expectedMesh = pr.Reduce(reference, 1e-3);
            Assert.True(expectedMesh.TriangleCount < reference.TriangleCount);
            string expected = Signature(expectedMesh, 1);
            foreach (int e in new[] { -100, -40, -29, 29, 40, 100 }) {
                double s = Math.Pow(2, e);
                DMesh3 m = Grid(12, s);
                DMesh3 r = new gs.PartitionedReducer() { MaxThreads = 1, MinTriangles = 0, Configure = q => { q.AreaWeightedQuadrics = false; } }.Reduce(m, 1e-3 * s * s);
                Assert.Equal(expected, Signature(r, 1.0 / s));
            }
        }

        // the flip test is shared with the Remesher: its result for a 2^k scaled copy is the scaled result
        [Fact]
        public void Remesher_OfA2kScaledCopy_IsTheScaledRemesh()
        {
            DMesh3 reference = Grid(8, 1);
            RemeshOnce(reference, 1);
            string expected = Signature(reference, 1);
            foreach (int e in new[] { -400, -300, -100, -40, 40, 100, 260, 400 }) {
                double s = Math.Pow(2, e);
                DMesh3 m = Grid(8, s);
                RemeshOnce(m, s);
                Assert.Equal(expected, Signature(m, 1.0 / s));
            }
        }

        static void RemeshOnce(DMesh3 m, double s)
        {
            var rm = new Remesher(m) { SmoothSpeedT = 0.5 };
            rm.SetTargetEdgeLength(1.3 * s);
            for (int i = 0; i < 3; i++) rm.BasicRemeshPass();
        }

        // the scale of the frame must be a normal double and its reciprocal too: a mesh of a subnormal extent (which is not reduced, but must
        // not become NaN: the scale would be infinite) stays unscaled; the same clamp keeps an extent above 2^1000 unscaled
        [Theory]
        [InlineData(-1070)]
        [InlineData(-1030)]
        public void Reducer_OfASubnormalOrNearlySubnormalExtent_StaysFinite(int e)
        {
            DMesh3 m = Grid(8, Math.Pow(2, e));
            new Reducer(m).ReduceToTriangleCount(30);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Assert.True(!double.IsNaN(p.x) && !double.IsNaN(p.y) && !double.IsNaN(p.z), "NaN vertex " + v);
            }
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
        }

        // "no limit" stays no limit however small the frame scale of a huge mesh is
        [Theory]
        [InlineData(-400)]
        [InlineData(0)]
        [InlineData(400)]
        public void ReduceToMaxQuadricError_OfDoubleMaxValue_AcceptsEveryCollapse(int e)
        {
            DMesh3 reference = Grid(8, 1);
            new Reducer(reference).ReduceToMaxQuadricError(double.MaxValue);
            double s = Math.Pow(2, e);
            DMesh3 m = Grid(8, s);
            new Reducer(m).ReduceToMaxQuadricError(double.MaxValue);
            Assert.Equal(reference.TriangleCount, m.TriangleCount);
        }

        // an area weighted reduction on four threads at an extent outside the band
        [Fact]
        public void PartitionedReducer_AreaWeighted_FourThreads_KeepsItsErrorInTheUnitsOfTheCaller()
        {
            DMesh3 reference = Grid(12, 1);
            DMesh3 expectedMesh = new gs.PartitionedReducer() { MaxThreads = 4, MinTriangles = 0 }.Reduce(reference, 1e-3);
            Assert.True(expectedMesh.TriangleCount < reference.TriangleCount);
            string expected = Signature(expectedMesh, 1);
            foreach (int e in new[] { -60, -40, 40, 60 }) {
                double s = Math.Pow(2, e);
                DMesh3 r = new gs.PartitionedReducer() { MaxThreads = 4, MinTriangles = 0 }.Reduce(Grid(12, s), 1e-3 * Math.Pow(s, 4));
                Assert.Equal(expected, Signature(r, 1.0 / s));
            }
        }


        static DMesh3 Sphere(double scale)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, scale * mesh.GetVertex(v));
            return mesh;
        }

        // the top of the range: above an edge length of 1e154 the cross products of the flip test would overflow; the edges are scaled by one factor
        // there (no more NaN metric that accepted a flip blindly), and the result is closed, finite and on the sphere
        [Theory]
        [InlineData(1e150)]
        [InlineData(1e160)]
        [InlineData(1e200)]
        [InlineData(1e300)]
        public void Reducer_OfAHugeSphere_StaysClosedAndFinite(double scale)
        {
            DMesh3 m = Sphere(scale);
            new Reducer(m).ReduceToTriangleCount(486);
            Assert.True(m.TriangleCount <= 486);
            Assert.True(m.IsClosed());
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z));
                double r = (p / scale).Length;
                Assert.InRange(r, 0.9, 1.1);
            }
        }

        // the bottom of the range: the flip test scales the edges of a triangle by one factor outside [1e-70, 1e70] (#595 left a zero normal below
        // 1e-160 as a flip: nothing was collapsed there), so a tiny sphere reduces too, closed, finite and on the sphere
        [Theory]
        [InlineData(1e-250)]
        [InlineData(1e-300)]
        public void Reducer_OfATinySphere_Reduces(double scale)
        {
            DMesh3 m = Sphere(scale);
            new Reducer(m).ReduceToTriangleCount(486);
            Assert.True(m.TriangleCount <= 486);
            Assert.True(m.IsClosed());
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z));
                Assert.InRange((p / scale).Length, 0.9, 1.1);
            }
        }
    }
}
