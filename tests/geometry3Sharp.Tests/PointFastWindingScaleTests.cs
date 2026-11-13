using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PointAABBTree3.FastWindingNumber has no absolute constants (#745, as DMeshAABBTree3 in #744): the tree splits every
    /// set with a spread (before: only above an absolute 1e-8, so a cloud below about 1e-8 got another tree than the same
    /// cloud scaled up), and the far-field expansion is gated by the centre and radius with the tight box (before: a box
    /// padded by a fixed 1.1e-14, which contained every query near a cloud below about 1e-13).
    /// </summary>
    public class PointFastWindingScaleTests
    {
        // the vertices of a sphere of radius 1 as an oriented point cloud (the generator's normals), with a third of the
        // one-ring area per point
        internal static DMesh3 Cloud(out double[] area)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 30 }.Generate().MakeDMesh();
            area = new double[m.MaxVertexID];
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                double a = m.GetTriArea(t) / 3;
                area[tv.a] += a; area[tv.b] += a; area[tv.c] += a;
            }
            return m;
        }

        internal static PointAABBTree3 Tree(DMesh3 cloud, double[] area, double scale)
        {
            return new PointAABBTree3(cloud, true) { FWNAreaEstimateF = v => area[v] * scale * scale };
        }

        // the sum of the dipoles of all points (no expansion)
        static double Direct(DMesh3 cloud, double[] area, double scale, Vector3d q)
        {
            double sum = 0;
            foreach (int v in cloud.VertexIndices()) {
                Vector3d x = cloud.GetVertex(v), n = cloud.GetVertexNormal(v);
                sum += FastPointWinding.ExactEval(ref x, ref n, area[v] * scale * scale, ref q);
            }
            return sum;
        }

        internal static Vector3d[] Queries(int n, int seed)
        {
            var rnd = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => new Vector3d(2.6 * rnd.NextDouble() - 1.3, 2.6 * rnd.NextDouble() - 1.3, 2.6 * rnd.NextDouble() - 1.3)).ToArray();
        }

        [Theory]
        [InlineData(-30)]
        [InlineData(-40)]
        [InlineData(-50)]
        [InlineData(-100)]
        [InlineData(40)]
        public void PowerOfTwoScale_SameValuesBitForBit(int exponent)
        {
            // (2^-30: the tree's absolute split tolerance; 2^-50: the fixed padding too)
            double s = BitConverter.Int64BitsToDouble((long)(1023 + exponent) << 52);
            double[] area;
            DMesh3 unit = Cloud(out area);
            DMesh3 m = new DMesh3(unit);
            MeshTransforms.Scale(m, s);
            PointAABBTree3 unitTree = Tree(unit, area, 1), tree = Tree(m, area, s);
            foreach (Vector3d q in Queries(300, 3)) {
                double expected = unitTree.FastWindingNumber(q), fast = tree.FastWindingNumber(s * q);
                Assert.True(BitConverter.DoubleToInt64Bits(fast) == BitConverter.DoubleToInt64Bits(expected), $"2^{exponent}, at {q}: {fast:R} vs {expected:R} at unit scale");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void FastWindingNumber_Invariant(string name)
        {
            Similarity s = Similarity.ByName(name);
            double[] area;
            DMesh3 unit = Cloud(out area);
            DMesh3 m = Invariance.Transformed(unit, s);
            PointAABBTree3 unitTree = Tree(unit, area, 1), tree = Tree(m, area, s.Scale);
            foreach (Vector3d q in Queries(200, 5)) {
                Vector3d tq = s.Apply(q);
                double expected = unitTree.FastWindingNumber(q), fast = tree.FastWindingNumber(tq);
                if (s.IsExact)
                    Assert.True(BitConverter.DoubleToInt64Bits(fast) == BitConverter.DoubleToInt64Bits(expected), $"{name}, at {q}: {fast:R} vs {expected:R}");
                else {
                    // (another tree: other expansions, each within the expansion's error of the direct sum of the
                    // transformed cloud, so within twice that of each other)
                    double direct = Direct(m, area, s.Scale, tq);
                    Assert.True(Math.Abs(fast - direct) < 0.1, $"{name}, at {q}: fast {fast} vs direct {direct}");
                    Assert.True(Math.Abs(fast - expected) < 0.2, $"{name}, at {q}: fast {fast} vs {expected} untransformed");
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-50)]
        [InlineData(-210)]
        [InlineData(210)]
        public void Values_CloseToTheDirectSum(int exponent)
        {
            // (2^+-210: beyond the range where 4 pi d^5 is a normal double, the direct sum is taken)
            double s = BitConverter.Int64BitsToDouble((long)(1023 + exponent) << 52);
            double[] area;
            DMesh3 m = Cloud(out area);
            MeshTransforms.Scale(m, s);
            PointAABBTree3 tree = Tree(m, area, s);
            foreach (Vector3d q0 in Queries(100, 7)) {
                Vector3d q = s * q0;
                double fast = tree.FastWindingNumber(q), direct = Direct(m, area, s, q);
                Assert.True(Math.Abs(fast - direct) < 0.1, $"2^{exponent}, at {q0}: fast {fast} vs direct {direct}");
            }
        }

        [Fact]
        public void SplitAxisSpreadOfOneUlp_FarFromTheOrigin_TreeCoversEveryPoint()
        {
            // points whose x is 1.5e8 or the next double (spread 3e-8, above the old absolute 1e-8): the midpoint rounds
            // onto an end, every x is <= it, and the old scan ran past the set (an exception or a corrupt tree); now the
            // scan is bounded and a side left empty splits half and half
            // (x0 with an odd last mantissa bit: x0 + x1 is then a tie that rounds to even upwards, so the midpoint is x1)
            long bits = BitConverter.DoubleToInt64Bits(1.5e8) | 1;
            double x0 = BitConverter.Int64BitsToDouble(bits), x1 = BitConverter.Int64BitsToDouble(bits + 1);
            Assert.Equal(x1, 0.5 * (x0 + x1));
            var m = new DMesh3();
            var rnd = new Random(11);
            for (int i = 0; i < 40; ++i)
                m.AppendVertex(new Vector3d(i % 2 == 0 ? x0 : x1, rnd.NextDouble(), rnd.NextDouble()));
            var tree = new PointAABBTree3(m, true);
            tree.TestCoverage();
            for (int k = 0; k < 50; ++k) {
                Vector3d q = new Vector3d(x0 + rnd.NextDouble() - 0.5, rnd.NextDouble(), rnd.NextDouble());
                int found = tree.FindNearestPoint(q);
                double best = m.VertexIndices().Min(v => m.GetVertex(v).DistanceSquared(q));
                Assert.Equal(best, m.GetVertex(found).DistanceSquared(q));
            }
        }

        [Fact]
        public void TinyCloud_TreeSplitsLikeAtUnitScale()
        {
            // the tree itself: at 2^-30 every spread is below the old absolute 1e-8, so every set was split half and half;
            // now the boxes are those of unit scale, scaled
            double s = BitConverter.Int64BitsToDouble((long)(1023 - 30) << 52);
            double[] area;
            DMesh3 unit = Cloud(out area);
            DMesh3 m = new DMesh3(unit);
            MeshTransforms.Scale(m, s);
            var a = new PointAABBTree3(unit, true);
            var b = new PointAABBTree3(m, true);
            Assert.Equal(a.TotalVolume() * s * s * s, b.TotalVolume());
        }
    }
}
