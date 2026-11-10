using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The order of TriangleBoxTree is a heuristic: one far box must not spoil it (the Morton coordinates are ranks), and
    /// no result may depend on it (a random order of the boxes, PermutationSeedForTests, gives the same queries, pairs,
    /// segment queries, Boolean results and a repair's result). Runs alone: the seed is process-wide.
    /// </summary>
    [Collection("BoxTreeOrder")]
    public class BoxTreeOrderTests
    {
        static List<AxisAlignedBox3d> SphereBoxes(int edgeVertices, int seed)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            var boxes = new List<AxisAlignedBox3d>();
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                var box = new AxisAlignedBox3d(a, a);
                box.Contain(b); box.Contain(c);
                boxes.Add(box);
            }
            // (shuffled: the input order must not be what makes the tree good)
            var rand = new Random(seed);
            for (int i = boxes.Count - 1; i > 0; --i) {
                int k = rand.Next(i + 1);
                var t = boxes[i]; boxes[i] = boxes[k]; boxes[k] = t;
            }
            return boxes;
        }

        static double MeanVisits(TriangleBoxTree tree, List<AxisAlignedBox3d> queries)
        {
            long sum = 0;
            foreach (var q in queries) sum += tree.VisitedNodes(q);
            return (double)sum / queries.Count;
        }

        [Theory]
        [InlineData(1e7)]
        [InlineData(1e12)]
        [InlineData(double.PositiveInfinity)]
        public void OneFarBox_TreeStaysLocal(double far)
        {
            // a sphere's shuffled triangle boxes and one point box far away: the queries of the sphere's boxes visit
            // about as many nodes as without the far box (with coordinates over the extent of the centres, all codes
            // but one tie, the tree takes the shuffled order, and a query visits a large part of it)
            List<AxisAlignedBox3d> boxes = SphereBoxes(40, 3);
            double plain = MeanVisits(new TriangleBoxTree(boxes), boxes.Take(2000).ToList());
            var withFar = new List<AxisAlignedBox3d>(boxes);
            var p = new Vector3d(far, far, far);
            withFar.Add(new AxisAlignedBox3d(p, p));
            double outlier = MeanVisits(new TriangleBoxTree(withFar), boxes.Take(2000).ToList());
            Assert.True(outlier <= 2 * plain + 10, $"far {far}: {outlier:F1} nodes per query, without the far box {plain:F1}");
            Assert.True(plain < 200, $"{plain:F1} nodes per query");
        }

        static void WithSeed(int seed, Action a)
        {
            long before = TriangleBoxTree.PermutedTrees;
            TriangleBoxTree.PermutationSeedForTests = seed;
            try { a(); } finally { TriangleBoxTree.PermutationSeedForTests = 0; }
            // (the hook ran: the trees were built in a random order)
            Assert.True(TriangleBoxTree.PermutedTrees > before, "no tree was permuted");
        }

        [Trait("Category", "Slow")]
        [Theory]
        [InlineData(16)]    // below MedianSplitBelow: the median split
        [InlineData(40)]    // above: the Morton order
        public void RandomOrders_SameQueriesPairsAndSegments(int edgeVertices)
        {
            List<AxisAlignedBox3d> boxes = SphereBoxes(edgeVertices, 5);
            var rand = new Random(9);
            var segments = Enumerable.Range(0, 200).Select(_ => (new Vector3d(rand.NextDouble() * 3 - 1.5, rand.NextDouble() * 3 - 1.5, rand.NextDouble() * 3 - 1.5),
                new Vector3d(rand.NextDouble() * 3 - 1.5, rand.NextDouble() * 3 - 1.5, rand.NextDouble() * 3 - 1.5))).ToList();
            Func<(List<List<int>>, List<(int, int)>, List<List<int>>)> run = () => {
                var tree = new TriangleBoxTree(boxes);
                var queries = boxes.Take(300).Select(q => { var l = new List<int>(); tree.Query(q, l); l.Sort(); return l; }).ToList();
                var pairs = new List<(int, int)>();
                foreach (Index2i t in tree.SelfPairTasks(8)) {
                    var list = new List<Index2i>();
                    tree.SelfPairs(t, list);
                    pairs.AddRange(list.Select(pr => (Math.Min(pr.a, pr.b), Math.Max(pr.a, pr.b))));
                }
                pairs.Sort();
                var segs = segments.Select(sg => { var l = new List<int>(); tree.QuerySegment(sg.Item1, sg.Item2, 1e-3, l.Add); l.Sort(); return l; }).ToList();
                return (queries, pairs, segs);
            };
            var reference = run();
            Assert.True(reference.Item3.Any(l => l.Count > 0), "no segment hits a box");
            foreach (int seed in new[] { 1, 2, 3 }) {
                (List<List<int>>, List<(int, int)>, List<List<int>>) got = default;
                WithSeed(seed, () => got = run());
                for (int i = 0; i < reference.Item1.Count; ++i) Assert.Equal(reference.Item1[i], got.Item1[i]);
                Assert.Equal(reference.Item2, got.Item2);
                for (int i = 0; i < reference.Item3.Count; ++i) Assert.Equal(reference.Item3[i], got.Item3[i]);
            }
        }

        static string Print(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices())
                sb.Append(t).Append(':').Append(m.GetTriangle(t)).Append(';');
            return sb.ToString();
        }

        [Fact]
        public void RandomOrders_SameRepairResult()
        {
            // SafeRounding's trees (the main one and that of the changed triangles) on a random order
            DMesh3 input = SafeRoundingDegenerateInputTests.Make(SafeRoundingDegenerateInputTests.Family.Cluster, 36, 1);
            Func<string> run = () => {
                DMesh3 m = new DMesh3(input);
                new MeshRoundingRepair(m) { SharedPositionsTouch = true }.Apply();
                return Print(m);
            };
            string reference = run();
            foreach (int seed in new[] { 1, 2 }) {
                string got = null;
                WithSeed(seed, () => got = run());
                Assert.True(reference == got, $"seed {seed}: the repair depends on the tree's order");
            }
        }

        [Theory]
        [InlineData(g3.ExactMeshBoolean.Operations.Union)]
        [InlineData(g3.ExactMeshBoolean.Operations.Difference)]
        public void RandomOrders_SameBooleanResult(g3.ExactMeshBoolean.Operations op)
        {
            // the arrangement's pair search, the closed-mesh tree and SafeRounding's searches all on a random order
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 14 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 17));
            MeshTransforms.Translate(b, new Vector3d(0.6, 0.3, 0.2));
            Func<string> run = () => {
                var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                Assert.True(boolean.Compute(), boolean.Error);
                return Print(boolean.Result);
            };
            string reference = run();
            foreach (int seed in new[] { 1, 2 }) {
                string got = null;
                WithSeed(seed, () => got = run());
                Assert.True(reference == got, $"seed {seed}: the result depends on the tree's order");
            }
        }
    }

    [CollectionDefinition("BoxTreeOrder", DisableParallelization = true)]
    public class BoxTreeOrderCollection { }
}
