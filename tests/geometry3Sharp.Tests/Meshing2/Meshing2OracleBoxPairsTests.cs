using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The conformity check's candidate pairs (<see cref="BoxPairs.Overlaps"/>): a sweep over x with two segment trees over y,
    /// in place of the plain sweep whose active list long boxes kept long. The pairs must be exactly those of a brute-force test,
    /// each once. The conformity report must not depend on the order in which the pairs come: it names the first 8 violations in
    /// index order.
    /// </summary>
    public class Meshing2OracleBoxPairsTests
    {
        static int[][] RandomBoxes(Random random, int n, int range, int maxSize)
        {
            var boxes = new int[n][];
            for (int i = 0; i < n; ++i) {
                int x = random.Next(range), y = random.Next(range);
                boxes[i] = new[] { x, Math.Min(range - 1, x + random.Next(maxSize + 1)), y, Math.Min(range - 1, y + random.Next(maxSize + 1)) };
            }
            return boxes;
        }

        static bool Overlap(int[] p, int[] q) => p[0] <= q[1] && q[0] <= p[1] && p[2] <= q[3] && q[2] <= p[3];

        static HashSet<(int, int)> BruteForce(int[][] a, int[][] b, bool same)
        {
            var pairs = new HashSet<(int, int)>();
            if (same) {
                for (int i = 0; i < a.Length; ++i) for (int j = i + 1; j < a.Length; ++j) if (Overlap(a[i], a[j])) pairs.Add((i, j));
            } else {
                for (int i = 0; i < a.Length; ++i) for (int j = 0; j < b.Length; ++j) if (Overlap(a[i], b[j])) pairs.Add((i, j));
            }
            return pairs;
        }

        static List<(int, int)> Found(Action<int[][], int[][], bool, Action<int, int>> pairsOf, int[][] a, int[][] b, bool same)
        {
            var found = new List<(int, int)>();
            pairsOf(a, b, same, (i, j) => found.Add(same ? (Math.Min(i, j), Math.Max(i, j)) : (i, j)));
            return found;
        }

        /// <summary> random boxes, from many ties on a small range (points, shared ends) to long boxes in either direction: the pairs are
        /// those of the brute-force test, each once, and the same as the plain sweep's </summary>
        [Theory]
        [InlineData(1, 8, 2, false)]
        [InlineData(2, 8, 2, true)]
        [InlineData(3, 40, 10, false)]
        [InlineData(4, 40, 10, true)]
        [InlineData(5, 1000, 3, false)]
        [InlineData(6, 1000, 3, true)]
        [InlineData(7, 1000, 1000, false)]
        [InlineData(8, 1000, 1000, true)]
        [InlineData(9, 3, 0, false)]
        [InlineData(10, 1, 0, true)]
        public void Pairs_AreThoseOfTheBruteForceTest_EachOnce(int seed, int range, int maxSize, bool same)
        {
            var random = new Random(seed);
            for (int round = 0; round < 20; ++round) {
                var a = RandomBoxes(random, random.Next(0, 120), range, maxSize);
                var b = same ? a : RandomBoxes(random, random.Next(0, 120), range, maxSize);
                var expected = BruteForce(a, b, same);
                var found = Found(BoxPairs.Overlaps, a, b, same);
                Assert.Equal(found.Count, found.Distinct().Count());
                Assert.True(expected.SetEquals(found), $"round {round}: {expected.Count} expected, {found.Count} found");
                Assert.True(expected.SetEquals(Found(BoxPairs.OverlapsBySweep, a, b, same)));
            }
        }

        /// <summary> long thin boxes in x, as Delaunay triangles between points on a few vertical lines give them (the case that kept the
        /// plain sweep's active list long), crossed by short boxes </summary>
        [Fact]
        public void LongBoxes_InX_AndShortOnes()
        {
            var boxes = new List<int[]>();
            for (int i = 0; i < 300; ++i) boxes.Add(new[] { 0, 999, i, i + 1 });
            for (int i = 0; i < 300; ++i) boxes.Add(new[] { 3 * i, 3 * i, (7 * i) % 301, (7 * i) % 301 });
            var a = boxes.ToArray();
            var expected = BruteForce(a, a, true);
            var found = Found(BoxPairs.Overlaps, a, a, true);
            Assert.Equal(found.Count, found.Distinct().Count());
            Assert.True(expected.SetEquals(found));
            var points = boxes.Skip(300).ToArray();
            var lines = boxes.Take(300).ToArray();
            Assert.True(BruteForce(lines, points, false).SetEquals(Found(BoxPairs.Overlaps, lines, points, false)));
        }

        /// <summary>
        /// Two triangulations of an n x n grid laid over each other, one with the diagonals / and one with \: the two diagonals of every
        /// cell cross. The report names the first 8 crossings by their edges in index order, whatever order the pairs came in.
        /// </summary>
        [Fact]
        public void Crossings_AreReportedInIndexOrder()
        {
            const int n = 4;
            int V(int i, int j) => i * (n + 1) + j;
            var vx = new List<double>(); var vy = new List<double>();
            for (int i = 0; i <= n; ++i) for (int j = 0; j <= n; ++j) { vx.Add(i); vy.Add(j); }
            var tris = new List<int[]>();
            var expected = new List<(long, long)>();
            static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) {
                int v00 = V(i, j), v10 = V(i + 1, j), v01 = V(i, j + 1), v11 = V(i + 1, j + 1);
                tris.Add(new[] { v00, v10, v11 }); tris.Add(new[] { v00, v11, v01 });
                tris.Add(new[] { v00, v10, v01 }); tris.Add(new[] { v10, v11, v01 });
                long k1 = Key(v00, v11), k2 = Key(v10, v01);
                expected.Add((Math.Min(k1, k2), Math.Max(k1, k2)));
            }
            expected.Sort();
            var input = new Mesh2Input { X = vx.ToArray(), Y = vy.ToArray() };
            var output = new Mesh2Output {
                Status = Mesh2Status.Success, VX = vx.ToArray(), VY = vy.ToArray(), InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                Triangles = tris.ToArray(), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
            foreach (bool check in new[] { false, true }) {
                Oracle.CheckFastPathForTests = check;
                OracleReport report;
                try { report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M1 }); }
                finally { Oracle.CheckFastPathForTests = false; }
                var crossings = report.Violations.Where(x => x.Kind == Violation.EdgesCross).Select(x => x.Detail).ToList();
                var names = expected.Take(8).Select(p => "edges " + (int)(p.Item1 >> 32) + "-" + (int)(uint)p.Item1 + " and "
                    + (int)(p.Item2 >> 32) + "-" + (int)(uint)p.Item2 + " cross").ToList();
                Assert.Equal(names, crossings);
            }
        }
    }
}
