using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's default conformity searches (certified STRtree pruning, StrtreePointSearch and StrtreeEdgeSearch): with
    /// Oracle.CheckFastPathForTests on, Conformity compares their answers with the sweep pair for pair, so a pruning that loses a
    /// touch or a crossing throws here, not only in the oracle-index-pilot workflow. And the edge search's work on a convex boundary
    /// grows like n log n (review of #411: the exact hull of an inner node held nearly every boundary point, O(E·h)).
    /// </summary>
    public class Meshing2OracleStrtreeTests
    {
        static OracleReport Checked(Mesh2Output o)
        {
            // a hull domain over the output's own points (no rings): the checks before Conformity pass for positive triangles
            var input = new Mesh2Input { X = (double[])o.VX.Clone(), Y = (double[])o.VY.Clone() };
            o.Status = Mesh2Status.Success;
            o.InputToVertex = Enumerable.Range(0, o.VX.Length).ToArray();
            o.Neighbours = SelfCheck.NeighboursOf(o.Triangles);
            o.BadTriangles = new int[0];
            o.Subsegments = new List<SubsegmentOutput>();
            Oracle.CheckFastPathForTests = true;
            try { return Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M1 }); }
            finally { Oracle.CheckFastPathForTests = false; }
        }

        static Mesh2Output Mesh(List<double[]> points, List<int[]> triangles) => new Mesh2Output {
            VX = points.Select(p => p[0]).ToArray(), VY = points.Select(p => p[1]).ToArray(), Triangles = triangles.ToArray() };

        /// <summary>
        /// A big triangle with its lower edge on y = 0 and, below it, rows of small triangles whose top corners lie on that edge: the
        /// STRtree has nodes whose closed box is outside the big triangle's edge except for points exactly on it. A pruning that
        /// treats orientation 0 as outside (`&lt; 0` to `&lt;= 0`) or picks the wrong box corner loses those touches; the sweep finds them
        /// </summary>
        [Fact]
        public void PointSearch_TouchesOnAnEdgeLine_EqualTheSweep()
        {
            var p = new List<double[]> { new[] { 0.0, 0 }, new[] { 100.0, 0 }, new[] { 0.0, 100 } };
            var t = new List<int[]> { new[] { 0, 1, 2 } };
            for (int i = 0; i < 12; ++i) {
                int s = p.Count; double x = 2 + 2 * i;
                p.Add(new[] { x, 0.0 }); p.Add(new[] { x, -1.0 }); p.Add(new[] { x + 0.5, -1.0 });
                t.Add(new[] { s, s + 1, s + 2 });
                // the mirror image at the left edge x = 0 (its outside is x < 0): a different corner of the node box is the maximum
                s = p.Count; double y = 2 + 2 * i;
                p.Add(new[] { 0.0, y }); p.Add(new[] { -1.0, y + 0.5 }); p.Add(new[] { -1.0, y });
                t.Add(new[] { s, s + 1, s + 2 });
                // and the hypotenuse x + y = 100 (outside up and right)
                s = p.Count; double h = 30 + 3 * i;
                p.Add(new[] { h, 100 - h }); p.Add(new[] { h + 1, 101 - h }); p.Add(new[] { h, 101 - h });
                t.Add(new[] { s, s + 1, s + 2 });
            }
            var report = Checked(Mesh(p, t));
            Assert.True(report.Has(Violation.VertexInTriangle), report.ToString());
        }

        /// <summary> random overlapping positive triangles on a small integer grid: many touches and crossings, collinear contacts
        /// and shared vertices; the STRtree answers must equal the sweep's (CheckFastPathForTests throws otherwise) </summary>
        [Fact]
        public void RandomOverlaps_EqualTheSweep()
        {
            int crossed = 0;
            for (int seed = 0; seed < 60; ++seed) {
                var r = new Random(seed);
                var p = new List<double[]>();
                var used = new HashSet<(int, int)>();
                while (p.Count < 30) {
                    var q = (r.Next(-10, 11), r.Next(-10, 11));
                    if (used.Add(q)) p.Add(new double[] { q.Item1, q.Item2 });
                }
                var t = new List<int[]>();
                for (int attempt = 0; attempt < 200 && t.Count < 40; ++attempt) {
                    int a = r.Next(p.Count), b = r.Next(p.Count), c = r.Next(p.Count);
                    double o = (p[b][0] - p[a][0]) * (p[c][1] - p[a][1]) - (p[b][1] - p[a][1]) * (p[c][0] - p[a][0]);
                    if (o > 0) t.Add(new[] { a, b, c }); else if (o < 0) t.Add(new[] { a, c, b });
                }
                var report = Checked(Mesh(p, t));
                if (report.Has(Violation.EdgesCross)) ++crossed;
            }
            Assert.True(crossed > 30, "the random meshes should mostly have crossing edges: " + crossed);
        }

        /// <summary> the edges of a zigzag strip between two parabolas y = ±(1 - x²)/4 (m points on each, sharing the tips): every
        /// point is on the convex hull and every chord splits it </summary>
        static (Mesh2Output mesh, long[] keys) Lens(int m)
        {
            var x = new double[2 * m - 2]; var y = new double[2 * m - 2];
            int U(int i) => i;
            int L(int i) => i == 0 ? 0 : i == m - 1 ? m - 1 : m - 1 + i;
            for (int i = 0; i < m; ++i) {
                double s = -1 + 2.0 * i / (m - 1);
                x[U(i)] = s; y[U(i)] = 0.25 * (1 - s * s);
                if (i > 0 && i < m - 1) { x[L(i)] = s; y[L(i)] = -0.25 * (1 - s * s); }
            }
            var tris = new List<int[]>();
            for (int i = 0; i < m - 1; ++i) {
                if (i > 0) tris.Add(new[] { L(i), L(i + 1), U(i) });
                if (i < m - 2) tris.Add(new[] { L(i + 1), U(i + 1), U(i) });
            }
            var mesh = new Mesh2Output { VX = x, VY = y, Triangles = tris.ToArray() };
            var keys = mesh.Triangles.SelectMany(t => new[] {
                StrtreeEdgeSearch.Key(t[0], t[1]), StrtreeEdgeSearch.Key(t[1], t[2]), StrtreeEdgeSearch.Key(t[2], t[0]) }).ToArray();
            return (mesh, keys);
        }

        /// <summary>
        /// Complexity (REVIEW_CHECKLIST §10): the edge search's work (nodes visited plus orientation tests of the pruning certificates)
        /// on the convex lens at n and 4n edges grows like n log n, not n²: an inner node tests at most HullLimit polygon vertices.
        /// With the exact hull at every node (before the fix) the work grew about 13-fold per 4n here. The answers equal the sweep's
        /// </summary>
        [Fact]
        public void EdgeSearch_ConvexLens_WorkGrowsLikeNLogN()
        {
            long Work(int m, out int hits)
            {
                var (mesh, keys) = Lens(m);
                var search = new StrtreeEdgeSearch(mesh, keys);
                var hulls = search.Run("hulls");
                Assert.Equal(search.Run("boxes").Fingerprint, hulls.Fingerprint);
                hits = hulls.Hits;
                return hulls.Nodes + hulls.PruneTests;
            }
            long small = Work(1000, out int h1), large = Work(4000, out int h2);
            Assert.Equal(0, h1); Assert.Equal(0, h2);
            double ratio = (double)large / small;
            // n log n from 3000 to 12000 edges: 4 × log(12000) / log(3000) ≈ 4.7; allow for the tree's depth changing by one level
            Assert.True(ratio < 6.5, "work " + small + " -> " + large + " (x" + ratio.ToString("F2") + ")");
        }

        /// <summary> the point search on the same lens: its node test is constant, so its work also grows like n log n </summary>
        [Fact]
        public void PointSearch_ConvexLens_WorkGrowsLikeNLogN()
        {
            long Work(int m)
            {
                var (mesh, _) = Lens(m);
                var search = new StrtreePointSearch(mesh);
                var pruned = search.Run("pruned");
                Assert.Equal(search.Run("boxes").Fingerprint, pruned.Fingerprint);
                Assert.Equal(0, pruned.Hits);
                return pruned.Nodes;
            }
            double ratio = (double)Work(4000) / Work(1000);
            Assert.True(ratio < 6.5, "x" + ratio.ToString("F2"));
        }
    }
}
