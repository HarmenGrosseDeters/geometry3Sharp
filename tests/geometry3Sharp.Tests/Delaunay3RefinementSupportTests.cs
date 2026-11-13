using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Delaunay3.RefinementSupport (docs/design/TET_MESHING.md section 14.6 step 1): the undo of the last insertion restores the
    /// triangulation exactly (cells, ids, neighbours, stamps, labels, the id allocation), labels pass to the new cells from the
    /// cells they replace, and IsEdge / IsFacet / IncidentCells agree with a brute-force scan of all cells.
    /// </summary>
    public class Delaunay3RefinementSupportTests
    {
        // the full observable state, by cell id
        static string State(Delaunay3 dt, bool withStamps = true)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(dt.VertexCount).Append('|').Append(dt.CellCount).Append('|').Append(dt.MaxCellID).Append('\n');
            for (int v = 0; v < dt.VertexCount; ++v) sb.Append(dt.GetVertex(v).ToString()).Append(';');
            for (int c = 0; c < dt.MaxCellID; ++c) {
                if (!dt.IsCell(c)) { sb.Append(c).Append(":-\n"); continue; }
                sb.Append(c).Append(':').Append(dt.GetCell(c).ToString());
                for (int i = 0; i < 4; ++i) sb.Append(',').Append(dt.Neighbour(c, i));
                sb.Append(" L").Append(dt.CellLabel(c));
                if (withStamps) sb.Append(" S").Append(dt.CellStamp(c));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // null if IncidentCells of every vertex equals the brute-force star
        static string StarsReason(Delaunay3 dt)
        {
            var star = new Dictionary<int, HashSet<int>>();
            foreach (int c in dt.CellIndices())
                for (int i = 0; i < 4; ++i) {
                    int v = dt.CellVertex(c, i);
                    if (!star.TryGetValue(v, out var s)) star[v] = s = new HashSet<int>();
                    s.Add(c);
                }
            var cells = new List<int>();
            for (int v = 0; v < dt.VertexCount; ++v) {
                dt.IncidentCells(v, cells);
                if (!star[v].SetEquals(cells) || cells.Count != cells.Distinct().Count()) return "star of vertex " + v;
            }
            return null;
        }

        static Delaunay3 Labelled(AxisAlignedBox3d box)
        {
            var dt = new Delaunay3(box) { RefinementSupport = true };
            return dt;
        }

        // label every cell by the octant of its centroid
        static void LabelByCentroid(Delaunay3 dt, Vector3d centre)
        {
            foreach (int c in dt.CellIndices().ToList()) {
                Index4i t = dt.GetCell(c);
                Vector3d m = (dt.GetVertex(t.a) + dt.GetVertex(t.b) + dt.GetVertex(t.c) + dt.GetVertex(t.d)) / 4;
                dt.SetCellLabel(c, (m.x > centre.x ? 1 : 0) + (m.y > centre.y ? 2 : 0) + (m.z > centre.z ? 4 : 0) + 1);
            }
        }

        static IEnumerable<Vector3d> Points(int seed, int n, bool grid)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < n; ++i)
                yield return grid ? new Vector3d(1 + rnd.Next(4), 1 + rnd.Next(4), 1 + rnd.Next(4)) * 0.2
                                  : new Vector3d(0.05 + 0.9 * rnd.NextDouble(), 0.05 + 0.9 * rnd.NextDouble(), 0.05 + 0.9 * rnd.NextDouble());
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, true)]      // cospherical grid points: the grown cavities, cells with several cavity neighbours
        public void Undo_RestoresTheTriangulationExactly(int seed, bool grid)
        {
            var dt = Labelled(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            var twin = Labelled(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            var rnd = new Random(seed + 100);
            int undone = 0, inserted = 0;
            foreach (Vector3d p in Points(seed, 300, grid)) {
                LabelByCentroid(dt, new Vector3d(0.5, 0.5, 0.5));
                LabelByCentroid(twin, new Vector3d(0.5, 0.5, 0.5));
                string before = State(dt);
                var r = dt.Insert(p, -1, 0, out int v);
                if (r != Delaunay3.InsertResult.Inserted) {
                    Assert.False(dt.UndoLastInsert());                 // a failed insertion leaves nothing to undo
                    Assert.Equal(before, State(dt));
                    Assert.Equal(r, twin.Insert(p, -1, 0, out _));      // the twin's walk takes the same random steps
                    continue;
                }
                if (rnd.Next(3) == 0) {
                    Assert.True(dt.UndoLastInsert());
                    Assert.False(dt.UndoLastInsert());                 // only once
                    Assert.Equal(before, State(dt));
                    Assert.Null(dt.Validate(false));
                    Assert.Null(StarsReason(dt));                      // the incident cells point at live cells again
                    undone++;
                    // inserting it again gives the twin's triangulation, ids included (stamps differ by the undone attempt)
                    Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(p, -1, 0, out v));
                }
                Assert.Equal(Delaunay3.InsertResult.Inserted, twin.Insert(p, -1, 0, out int tv));
                Assert.Equal(tv, v);
                Assert.Equal(State(twin, false), State(dt, false));
                inserted++;
            }
            Assert.True(undone > (grid ? 15 : 40) && inserted > (grid ? 60 : 250), undone + " / " + inserted);   // the grid has 64 distinct points
            Assert.Null(dt.Validate(true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Clone_IsIndependentAndEvolvesIdentically(bool grid)
        {
            var dt = Labelled(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            var pts = Points(21, 200, grid).ToList();
            for (int k = 0; k < 100; ++k) dt.Insert(pts[k], -1, 0, out _);
            LabelByCentroid(dt, new Vector3d(0.4, 0.5, 0.6));
            Delaunay3 copy = dt.Clone();
            Assert.Equal(State(dt), State(copy));
            Assert.False(copy.UndoLastInsert());                       // the undo record is not copied
            for (int k = 100; k < 200; ++k) {
                var r1 = dt.Insert(pts[k], -1, 0, out int v1);
                var r2 = copy.Insert(pts[k], -1, 0, out int v2);
                Assert.Equal(r1, r2); Assert.Equal(v1, v2);
            }
            Assert.Equal(State(dt), State(copy));
            copy.Insert(new Vector3d(0.31, 0.52, 0.73), -1, 0, out _);
            Assert.NotEqual(State(dt), State(copy));
            Assert.Null(StarsReason(copy));
            var plain = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            for (int k = 0; k < 50; ++k) plain.Insert(pts[k], -1, 0, out _);
            Delaunay3 plainCopy = plain.Clone();
            Assert.False(plainCopy.RefinementSupport);
            Assert.Null(plainCopy.Validate(true));
        }

        [Fact]
        public void NewCells_TakeTheLabelOfTheCellTheyReplace()
        {
            var dt = Labelled(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            foreach (Vector3d p in Points(5, 60, false)) dt.Insert(p, -1, 0, out _);
            int checkedCells = 0;
            foreach (Vector3d p in Points(6, 100, false)) {
                LabelByCentroid(dt, p);
                var removed = new List<int>(); var added = new List<int>();
                var removedFacets = new Dictionary<(int, int, int), int>();
                foreach (int c in dt.CellIndices())
                    if (dt.InCircumsphere(c, p) > 0)
                        for (int i = 0; i < 4; ++i) { Index3i f = dt.FacetVertices(c, i); removedFacets[Sorted(f.a, f.b, f.c)] = dt.CellLabel(c); }
                if (dt.Insert(p, -1, 0, out int v, removed, added) != Delaunay3.InsertResult.Inserted) continue;
                foreach (int nc in added) {
                    Index3i baseFacet = dt.FacetVertices(nc, dt.VertexIndexIn(nc, v));
                    Assert.Equal(removedFacets[Sorted(baseFacet.a, baseFacet.b, baseFacet.c)], dt.CellLabel(nc));
                    checkedCells++;
                }
            }
            Assert.True(checkedCells > 1000, checkedCells.ToString());
        }

        static (int, int, int) Sorted(int a, int b, int c)
        {
            var s = new[] { a, b, c }; Array.Sort(s); return (s[0], s[1], s[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IsEdgeIsFacetIncidentCells_MatchBruteForce(bool enableLate)
        {
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)) { RefinementSupport = !enableLate };
            var pts = Points(7, 200, false).ToList();
            for (int k = 0; k < pts.Count; ++k) {
                if (enableLate && k == 120) dt.RefinementSupport = true;    // the scan when turned on
                dt.Insert(pts[k], -1, 0, out _);
            }
            var facets = new HashSet<(int, int, int)>(); var edges = new HashSet<(int, int)>();
            var star = new Dictionary<int, HashSet<int>>();
            foreach (int c in dt.CellIndices()) {
                Index4i t = dt.GetCell(c);
                for (int i = 0; i < 4; ++i) {
                    Index3i f = dt.FacetVertices(c, i); facets.Add(Sorted(f.a, f.b, f.c));
                    if (!star.TryGetValue(t[i], out var s)) star[t[i]] = s = new HashSet<int>();
                    s.Add(c);
                    for (int j = i + 1; j < 4; ++j) edges.Add((Math.Min(t[i], t[j]), Math.Max(t[i], t[j])));
                }
            }
            var cells = new List<int>();
            for (int v = 0; v < dt.VertexCount; ++v) {
                dt.IncidentCells(v, cells);
                Assert.True(star[v].SetEquals(cells), "star of " + v);
                Assert.Equal(cells.Count, cells.Distinct().Count());
            }
            var rnd = new Random(8);
            int hitsF = 0, hitsE = 0;
            for (int k = 0; k < 20000; ++k) {
                int a = rnd.Next(dt.VertexCount), b = rnd.Next(dt.VertexCount), c = rnd.Next(dt.VertexCount);
                if (k % 2 == 0) {                                 // a known facet in a random order of its vertices
                    var f = facets.ElementAt(rnd.Next(facets.Count));
                    var perm = new[] { f.Item1, f.Item2, f.Item3 }.OrderBy(_ => rnd.Next()).ToArray();
                    a = perm[0]; b = perm[1]; c = perm[2];
                }
                if (a == b || b == c || a == c) continue;
                bool isF = dt.IsFacet(a, b, c, out int fc, out int fi);
                Assert.Equal(facets.Contains(Sorted(a, b, c)), isF);
                if (isF) {
                    Index3i f = dt.FacetVertices(fc, fi);
                    Assert.Equal(Sorted(a, b, c), Sorted(f.a, f.b, f.c));
                    hitsF++;
                }
                bool isE = dt.IsEdge(a, b, out int ec);
                Assert.Equal(edges.Contains((Math.Min(a, b), Math.Max(a, b))), isE);
                if (isE) { Assert.True(dt.VertexIndexIn(ec, a) >= 0 && dt.VertexIndexIn(ec, b) >= 0); hitsE++; }
            }
            Assert.True(hitsF > 5000 && hitsE > 5000, hitsF + " / " + hitsE);
        }

        [Fact]
        public void Undo_NeedsSupportAndASuccessfulInsertion()
        {
            var plain = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            plain.Insert(new Vector3d(0.3, 0.4, 0.5), -1, 0, out _);
            Assert.False(plain.UndoLastInsert());
            var dt = Labelled(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            Assert.False(dt.UndoLastInsert());
            dt.Insert(new Vector3d(0.3, 0.4, 0.5), -1, 0, out _);
            string afterFirst = State(dt);
            dt.Insert(new Vector3d(0.6, 0.4, 0.5), -1, 0, out _);
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(new Vector3d(0.6, 0.4, 0.5), -1, 0, out _));
            Assert.False(dt.UndoLastInsert());                         // the failed one cleared the record of the second
            Assert.Equal(Delaunay3.InsertResult.Outside, dt.Insert(new Vector3d(2, 0.4, 0.5), -1, 0, out _));
            Assert.False(dt.UndoLastInsert());
            dt.Insert(new Vector3d(0.7, 0.2, 0.5), -1, 0, out _);
            dt.RefinementSupport = false;
            Assert.False(dt.UndoLastInsert());
            Assert.NotEqual("", afterFirst);
        }

        /// <summary> scale invariance: the same insertions and undos on scaled points give the same combinatorics; 2^k bit for bit </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void UndoAndQueries_AreInvariantUnderSimilarities(string name)
        {
            Similarity s = Similarity.All().First(x => x.Name == name);
            string Run(Func<Vector3d, Vector3d> map)
            {
                var corners = new[] { map(new Vector3d(-0.1, -0.1, -0.1)), map(new Vector3d(1.1, 1.1, 1.1)), map(new Vector3d(-0.1, 1.1, -0.1)), map(new Vector3d(1.1, -0.1, 1.1)),
                                      map(new Vector3d(-0.1, -0.1, 1.1)), map(new Vector3d(1.1, 1.1, -0.1)), map(new Vector3d(-0.1, 1.1, 1.1)), map(new Vector3d(1.1, -0.1, -0.1)) };
                var box = new AxisAlignedBox3d(corners[0], corners[0]);
                foreach (var c in corners) box.Contain(c);
                var dt = Labelled(box);
                var log = new System.Text.StringBuilder();
                int k = 0;
                foreach (Vector3d p in Points(11, 120, false)) {
                    var r = dt.Insert(map(p), -1, 0, out int v);
                    log.Append((int)r);
                    if (r == Delaunay3.InsertResult.Inserted && (k++ % 3) == 0) { dt.UndoLastInsert(); dt.Insert(map(p), -1, 0, out v); }
                }
                // combinatorics without the 8 box corners' cells (their positions depend on the rotated box): facets among input points
                var fs = new SortedSet<string>();
                foreach (int c in dt.CellIndices()) {
                    Index4i t = dt.GetCell(c);
                    if (t.a < 8 || t.b < 8 || t.c < 8 || t.d < 8) continue;
                    var q = new[] { t.a, t.b, t.c, t.d }; Array.Sort(q);
                    fs.Add(string.Join(",", q));
                }
                Assert.Null(dt.Validate(false));
                return log + "\n" + string.Join(";", fs);
            }
            string reference = Run(p => p);
            string mapped = Run(s.Apply);
            if (s.IsExact) Assert.Equal(reference, mapped);
            else {
                // rotations and translations round the points: the interior cells away from near-degenerate configurations agree
                var a = new HashSet<string>(reference.Split('\n')[1].Split(';'));
                var b = new HashSet<string>(mapped.Split('\n')[1].Split(';'));
                Assert.True(a.Intersect(b).Count() >= 0.9 * a.Count, a.Intersect(b).Count() + " of " + a.Count);
            }
        }
    }
}
