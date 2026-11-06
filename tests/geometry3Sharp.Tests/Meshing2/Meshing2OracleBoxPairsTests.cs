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

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static OracleReport Judge(List<double> vx, List<double> vy, List<int[]> tris, bool check)
        {
            var input = new Mesh2Input { X = vx.ToArray(), Y = vy.ToArray() };
            var output = new Mesh2Output {
                Status = Mesh2Status.Success, VX = vx.ToArray(), VY = vy.ToArray(), InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                Triangles = tris.ToArray(), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
            Oracle.CheckFastPathForTests = check;
            try { return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M1 }); }
            finally { Oracle.CheckFastPathForTests = false; }
        }

        /// <summary> the grid points (i, j) for 0 &lt;= i, j &lt;= n (shifted by dx, dy), numbered in a shuffled order: the index order
        /// then differs from the order in which a sweep meets them </summary>
        static int[,] ShuffledGrid(int n, double dx, double dy, Random random, List<double> vx, List<double> vy)
        {
            int start = vx.Count, count = (n + 1) * (n + 1);
            var slots = Enumerable.Range(start, count).OrderBy(_ => random.Next()).ToArray();
            var index = new int[n + 1, n + 1];
            for (int k = 0; k < count; ++k) { vx.Add(0); vy.Add(0); }
            int s = 0;
            for (int i = 0; i <= n; ++i) for (int j = 0; j <= n; ++j) {
                index[i, j] = slots[s++];
                vx[index[i, j]] = i + dx; vy[index[i, j]] = j + dy;
            }
            return index;
        }

        /// <summary>
        /// Two triangulations of an n x n grid laid over each other, one with the diagonals / and one with \: the two diagonals of every
        /// cell cross. Vertices and triangles are numbered in a shuffled order. The report names the first 8 crossings by their edges in
        /// index order, whatever order the pairs came in, with and without the check mode.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Crossings_AreReportedInIndexOrder(int seed)
        {
            const int n = 5;
            var random = new Random(seed);
            var vx = new List<double>(); var vy = new List<double>();
            var V = ShuffledGrid(n, 0, 0, random, vx, vy);
            var tris = new List<int[]>();
            var expected = new List<(long, long)>();
            for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) {
                int v00 = V[i, j], v10 = V[i + 1, j], v01 = V[i, j + 1], v11 = V[i + 1, j + 1];
                tris.Add(new[] { v00, v10, v11 }); tris.Add(new[] { v00, v11, v01 });
                tris.Add(new[] { v00, v10, v01 }); tris.Add(new[] { v10, v11, v01 });
                long k1 = Key(v00, v11), k2 = Key(v10, v01);
                expected.Add((Math.Min(k1, k2), Math.Max(k1, k2)));
            }
            tris = tris.OrderBy(_ => random.Next()).ToList();
            expected.Sort();
            var names = expected.Take(8).Select(p => "edges " + (int)(p.Item1 >> 32) + "-" + (int)(uint)p.Item1 + " and "
                + (int)(p.Item2 >> 32) + "-" + (int)(uint)p.Item2 + " cross").ToList();
            foreach (bool check in new[] { false, true }) {
                var report = Judge(vx, vy, tris, check);
                Assert.Equal(names, report.Violations.Where(x => x.Kind == Violation.EdgesCross).Select(x => x.Detail).ToList());
            }
        }

        /// <summary>
        /// Two grid triangulations, the second shifted by (0.5, 0.25) so that its vertices lie inside triangles of the first and the
        /// other way round, numbered and ordered in a shuffled way: the report names the first 8 (triangle, vertex) pairs in index
        /// order, found by a brute-force test here.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void TouchingVertices_AreReportedInIndexOrder(int seed)
        {
            const int n = 4;
            var random = new Random(seed);
            var vx = new List<double>(); var vy = new List<double>();
            var A = ShuffledGrid(n, 0, 0, random, vx, vy);
            var B = ShuffledGrid(n, 0.5, 0.25, random, vx, vy);
            var tris = new List<int[]>();
            foreach (var V in new[] { A, B })
                for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) {
                    tris.Add(new[] { V[i, j], V[i + 1, j], V[i + 1, j + 1] });
                    tris.Add(new[] { V[i, j], V[i + 1, j + 1], V[i, j + 1] });
                }
            tris = tris.OrderBy(_ => random.Next()).ToList();
            double Orient(int a, int b, int c) => (vx[b] - vx[a]) * (vy[c] - vy[a]) - (vy[b] - vy[a]) * (vx[c] - vx[a]);
            var expected = new List<(int, int)>();
            for (int ti = 0; ti < tris.Count; ++ti)
                for (int id = 0; id < vx.Count; ++id) {
                    var t = tris[ti];
                    if (id == t[0] || id == t[1] || id == t[2]) continue;
                    if (Orient(t[0], t[1], id) >= 0 && Orient(t[1], t[2], id) >= 0 && Orient(t[2], t[0], id) >= 0) expected.Add((ti, id));
                }
            Assert.True(expected.Count > 8);
            expected.Sort();
            var names = expected.Take(8).Select(p => "vertex " + p.Item2 + " / touches triangle " + string.Join(" ", tris[p.Item1])).ToList();
            foreach (bool check in new[] { false, true }) {
                var report = Judge(vx, vy, tris, check);
                var found = report.Violations.Where(x => x.Kind == Violation.VertexInTriangle && x.Detail.Contains(" touches triangle "))
                    .Select(x => {
                        var d = x.Detail;
                        string id = d.Substring(0, d.IndexOf(' ', "vertex ".Length));
                        return id + " / " + d.Substring(d.IndexOf("touches triangle "));
                    }).ToList();
                Assert.Equal(names, found);
            }
        }
    }
}
