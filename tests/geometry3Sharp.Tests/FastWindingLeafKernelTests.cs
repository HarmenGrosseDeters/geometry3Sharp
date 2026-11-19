using System;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact terms of DMeshAABBTree3.FastWindingNumber (#1009) are summed four triangles at a time in the lanes of a
    /// vector (FastWindingLeafKernel) and the tree is walked on flat arrays; both are the same arithmetic as before, so the
    /// value of a query is the same bit for bit as on the reference path (FastWindingForceScalar: the DVector traversal and
    /// MathUtil.TriSolidAngle per triangle). Fused multiply-adds or another order of a sum would show in the last bits.
    /// </summary>
    [Collection("FastWindingParallelDepth")]
    public class FastWindingLeafKernelTests
    {
        // a stack of overlapping, self-intersecting shells: spheres and cubes shifted into each other, one of them reversed
        internal static DMesh3 Stack(int shells = 5, int edge = 10)
        {
            var result = new DMesh3();
            var editor = new MeshEditor(result);
            for (int i = 0; i < shells; ++i) {
                DMesh3 part = (i % 2 == 0)
                    ? new Sphere3Generator_NormalizedCube() { Radius = 1 - 0.07 * i, EdgeVertices = edge + i }.Generate().MakeDMesh()
                    : new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, (0.55 + 0.05 * i) * Vector3d.One) }.Generate().MakeDMesh();
                MeshTransforms.Translate(part, new Vector3d(0.11 * i, -0.07 * i, 0.05 * i));
                if (i == 3) part.ReverseOrientation(true);
                editor.AppendMesh(part);
            }
            return result;
        }

        // points on shells (just off them), inside, and far away
        internal static Vector3d[] Points(DMesh3 m, int n, int seed = 5)
        {
            var rnd = new Random(seed);
            int[] tids = m.TriangleIndices().ToArray();
            var pts = new Vector3d[n];
            for (int i = 0; i < n; ++i) {
                if (i % 4 == 0) {
                    pts[i] = new Vector3d(3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5);
                } else {
                    int t = tids[rnd.Next(tids.Length)];
                    pts[i] = m.GetTriCentroid(t) + (i % 4 == 1 ? 1e-6 : i % 4 == 2 ? 1e-3 : 0.05) * m.GetTriNormal(t);
                }
            }
            return pts;
        }

        static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        [Fact]
        public void Stack_IsSelfIntersectingAndSumsManyExactTerms()
        {
            DMesh3 m = Stack();
            var tree = new DMeshAABBTree3(m, true);
            tree.FastWindingNumber(Vector3d.Zero);
            Vector3d[] pts = Points(m, 200);
            long exact = pts.Sum(p => (long)tree.FastWindingExactTriangleCount(p));
            Assert.True(exact / 200.0 > 50, $"{exact / 200.0} exact terms per query: too few to test the leaf sums");
        }

        [Fact]
        public void VectorPath_EqualsReferencePath_TermForTerm()
        {
            DMesh3 m = Stack();
            var tree = new DMeshAABBTree3(m, true);
            tree.FastWindingNumber(Vector3d.Zero);
            foreach (Vector3d p in Points(m, 3000)) {
                tree.FastWindingForceScalar = false;
                double v = tree.FastWindingNumber(p);
                tree.FastWindingForceScalar = true;
                double r = tree.FastWindingNumber(p);
                Assert.True(Same(v, r), $"at {p}: {v:R} vs reference path {r:R}");
            }
        }

#if NET8_0_OR_GREATER   // (the vector kernel exists on the net8.0 and later builds of the library only)
        [Fact]
        public void Kernel_GroupsOfOneToFour_EqualTriSolidAngleBitForBit()
        {
            if (FastWindingLeafKernel.Available == false)
                return;     // (no 256-bit vectors on this machine: the reference path is the only one)
            var rnd = new Random(11);
            var tris = new Vector3d[12];
            var lanes = new double[FastWindingLeafKernel.GroupSize];
            for (int round = 0; round < 3000; ++round) {
                // sizes over most of the range of the kernel (2^-90 .. 2^90 around the query), also far and near triangles
                double scale = Math.Pow(2, rnd.Next(-90, 90));
                var p = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * scale;
                for (int k = 0; k < tris.Length; ++k)
                    tris[k] = p + scale * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * (k % 3 == 0 ? 3 : 0.2);
                for (int n = 1; n <= 4; ++n) {
                    FastWindingLeafKernel.StoreGroup(lanes, 0, tris, 0, n);
                    double expected = 0.125, sum = 0.125;       // (a leaf's sum starts at 0; another start shows an addition order)
                    for (int t = 0; t < n; ++t)
                        expected += MathUtil.TriSolidAngle(tris[3 * t], tris[3 * t + 1], tris[3 * t + 2], ref p) / MathUtil.FourPI;
                    bool done = FastWindingLeafKernel.TrySum(lanes, 0, ref p, n, ref sum);
                    Assert.True(done, $"round {round}, n {n}, scale {scale}");
                    Assert.True(Same(sum, expected), $"round {round}, n {n}: {sum:R} vs {expected:R}");
                }
            }
        }

        [Theory]
        [InlineData(-120)]     // largest component below 2^-100: TriSolidAngle rescales
        [InlineData(120)]      // above 2^100
        public void Kernel_BeyondTheUnscaledRange_LeavesTheSumToTheScalarPath(int exponent)
        {
            if (FastWindingLeafKernel.Available == false)
                return;
            double scale = Math.Pow(2, exponent);
            var tris = new[] { new Vector3d(1, 0, 0) * scale, new Vector3d(0, 1, 0) * scale, new Vector3d(0, 0, 1) * scale,
                new Vector3d(1, 1, 0) * scale, new Vector3d(0, 1, 1) * scale, new Vector3d(1, 0, 1) * scale };
            var lanes = new double[FastWindingLeafKernel.GroupSize];
            FastWindingLeafKernel.StoreGroup(lanes, 0, tris, 0, 2);
            var p = new Vector3d(0.1, 0.2, 0.3) * scale;
            double sum = 0.5;
            Assert.False(FastWindingLeafKernel.TrySum(lanes, 0, ref p, 2, ref sum));
            Assert.True(Same(sum, 0.5), "the sum is untouched where the kernel declines");
        }

        [Fact]
        public void Kernel_NonFiniteAndDegenerate_LeaveTheSumToTheScalarPath()
        {
            if (FastWindingLeafKernel.Available == false)
                return;
            var lanes = new double[FastWindingLeafKernel.GroupSize];
            var tris = new[] { new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) };
            double sum = 0;
            // the query on a corner of the triangle and all three corners at the query: a zero largest component
            var p = new Vector3d(1, 0, 0);
            FastWindingLeafKernel.StoreGroup(lanes, 0, new[] { p, p, p }, 0, 1);
            Assert.False(FastWindingLeafKernel.TrySum(lanes, 0, ref p, 1, ref sum));
            var nan = new Vector3d(double.NaN, 0, 0);
            FastWindingLeafKernel.StoreGroup(lanes, 0, new[] { nan, tris[1], tris[2] }, 0, 1);
            var q = new Vector3d(0.2, 0.2, 0.2);
            Assert.False(FastWindingLeafKernel.TrySum(lanes, 0, ref q, 1, ref sum));
            Assert.Equal(0, sum);
        }

#endif

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Stack_Invariant_AndVectorPathEqualsReferencePathUnderEverySimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m = Stack();
            var reference = new DMeshAABBTree3(m, true);
            DMesh3 t = Invariance.Transformed(m, s);
            var tree = new DMeshAABBTree3(t, true);
            foreach (Vector3d p in Points(m, 400)) {
                Vector3d q = s.Apply(p);
                tree.FastWindingForceScalar = false;
                double v = tree.FastWindingNumber(q);
                tree.FastWindingForceScalar = true;
                double r = tree.FastWindingNumber(q);
                Assert.True(Same(v, r), $"{name} at {p}: {v:R} vs reference path {r:R}");
                if (s.IsExact)      // (2^k: the same value, scaled by nothing: the winding number has no unit)
                    Assert.True(Same(v, reference.FastWindingNumber(p)), $"{name} at {p}: {v:R} vs unscaled {reference.FastWindingNumber(p):R}");
            }
        }

        [Fact]
        public void Queries_ParallelEqualSerial_AndRepeatable()
        {
            DMesh3 m = Stack(6, 14);
            var tree = new DMeshAABBTree3(m, true);
            tree.FastWindingNumber(Vector3d.Zero);
            Vector3d[] pts = Points(m, 4000);
            double[] serial = pts.Select(p => tree.FastWindingNumber(p)).ToArray();
            var par = new double[pts.Length];
            Parallel.For(0, pts.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => par[i] = tree.FastWindingNumber(pts[i]));
            for (int i = 0; i < pts.Length; ++i) {
                Assert.True(Same(serial[i], par[i]), $"query {i}");
                Assert.True(Same(serial[i], tree.FastWindingNumber(pts[i])), $"query {i} again");      // (reuse of one tree)
            }
        }

        [Fact]
        public void CacheRebuiltAfterTheMeshChanged_StillEqualsReferencePath()
        {
            DMesh3 m = Stack();
            var tree = new DMeshAABBTree3(m, true);
            Vector3d[] pts = Points(m, 300);
            tree.FastWindingNumber(pts[0]);
            // a vertex moves: the tree's own timestamp check refuses (the tree must be rebuilt); a new tree on the changed mesh is the case
            m.SetVertex(0, m.GetVertex(0) + new Vector3d(0.01, 0.02, 0.03));
            var tree2 = new DMeshAABBTree3(m, true);
            foreach (Vector3d p in pts) {
                tree2.FastWindingForceScalar = false;
                double v = tree2.FastWindingNumber(p);
                tree2.FastWindingForceScalar = true;
                Assert.True(Same(v, tree2.FastWindingNumber(p)), $"at {p}");
            }
        }

        // the inside-test cache of MeshFidelityMetrics' two IoUs (#1009): shared sample points are not evaluated again
        [Fact]
        public void VolumeIoU_InsideCache_SameResultAndNoSecondEvaluationOfSharedPoints()
        {
            DMesh3 reference = Stack(), result = Stack(3);
            var region = MeshRepairMetrics.sample_region(reference, result);
            var tree = new DMeshAABBTree3(result, true);
            tree.FastWindingNumber(Vector3d.Zero);
            int calls = 0;
            Func<Vector3d, bool> inResult = p => { System.Threading.Interlocked.Increment(ref calls); return tree.FastWindingNumber(p) > 0.5; };
            Func<Vector3d, bool> inRef = p => p.LengthSquared < 0.7;
            double m0, e0, m1, e1, m2, e2;
            double plain = MeshRepairMetrics.volume_iou(inRef, region, inResult, out m0, out e0, 3000, 1, false);
            Assert.Equal(3000, calls);
            var cache = new MeshRepairMetrics.InsideCache();
            calls = 0;
            double first = MeshRepairMetrics.volume_iou(inRef, region, inResult, out m1, out e1, 3000, 1, false, cache);
            Assert.Equal(3000, calls);
            Assert.True(Same(plain, first) && Same(m0, m1) && Same(e0, e1));
            // another reference solid, the same region and seed: the same points, no evaluation of the result's test
            calls = 0;
            double second = MeshRepairMetrics.volume_iou(p => p.LengthSquared < 0.4, region, inResult, out m2, out e2, 3000, 1, false, cache);
            Assert.Equal(0, calls);
            double check = MeshRepairMetrics.volume_iou(p => p.LengthSquared < 0.4, region, inResult, out m0, out e0, 3000, 1, false);
            Assert.True(Same(second, check) && Same(m2, m0) && Same(e2, e0));
            // a cached point that differs in one coordinate only is not the point (stale answers there would show)
            for (int k = 0; k < cache.Points.Length; k += 2) {
                cache.Points[k] = new Vector3d(cache.Points[k].x, cache.Points[k].y + 1e-9, cache.Points[k].z);
                cache.Points[k + 1] = new Vector3d(cache.Points[k + 1].x, cache.Points[k + 1].y, cache.Points[k + 1].z - 1e-9);
                cache.Inside[k] = !cache.Inside[k]; cache.Inside[k + 1] = !cache.Inside[k + 1];
            }
            calls = 0;
            double third = MeshRepairMetrics.volume_iou(inRef, region, inResult, out m2, out e2, 3000, 1, false, cache);
            Assert.Equal(3000, calls);
            Assert.True(Same(third, first) && Same(m2, m1) && Same(e2, e1));
            // another seed: other points, all evaluated
            calls = 0;
            MeshRepairMetrics.volume_iou(inRef, region, inResult, out m2, out e2, 3000, 2, false, cache);
            Assert.True(calls > 2900, $"{calls} evaluations for other points");
            // another sample count: the cache does not apply
            calls = 0;
            MeshRepairMetrics.volume_iou(inRef, region, inResult, out m2, out e2, 1000, 2, false, cache);
            Assert.Equal(1000, calls);
        }
    }
}
