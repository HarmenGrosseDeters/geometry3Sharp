using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Delaunay3's round insertion (Alpha Wrap 3.0 lever (B), docs/design/ALPHA_WRAP_ROUNDS.md): PlanInsert (read only),
    /// AssignInsertIds (serial), ApplyInsert (parallel for plans with disjoint read sets). A round of plans made against one
    /// triangulation, kept greedily in order while their read sets are disjoint, gives the tetrahedra of inserting the kept
    /// points one after the other with Insert; the cases of the note's review are counted, so each test shows it covered them.
    /// </summary>
    public class Delaunay3RoundInsertTests
    {
        // the tetrahedra as sorted vertex quadruples, with their neighbours as quadruples (adjacency, independent of cell ids)
        static List<string> Cells(Delaunay3 dt)
        {
            var key = new Dictionary<int, string>();
            foreach (int c in dt.CellIndices()) {
                Index4i t = dt.GetCell(c);
                var v = new[] { t.a, t.b, t.c, t.d };
                Array.Sort(v);
                key[c] = string.Join(",", v);
            }
            var list = new List<string>();
            foreach (int c in dt.CellIndices()) {
                var nb = new List<string>();
                for (int i = 0; i < 4; ++i) {
                    int n = dt.Neighbour(c, i);
                    // (oriented: the vertex opposite the facet, so a swapped neighbour shows)
                    nb.Add(dt.CellVertex(c, i) + ">" + (n < 0 ? "box" : key[n]));
                }
                nb.Sort(StringComparer.Ordinal);
                list.Add(key[c] + "|" + string.Join(";", nb));
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        sealed class Coverage
        {
            public int FewerNewCells, FreedIdReused, OuterTwice, SharedVertex, SharedEdge, Grown, Rejected, Rounds, Kept;
        }

        static List<Vector3d> Points(int seed, int n, bool lattice)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            for (int k = 0; k < n; ++k) {
                if (lattice) {
                    // cospherical and coplanar groups (lattice points): the star-shape growth and cavities with interior edges
                    pts.Add(new Vector3d(0.5 + rnd.Next(1, 12), 0.5 + rnd.Next(1, 12), 0.5 + rnd.Next(1, 12)) * 0.75);
                } else {
                    pts.Add(new Vector3d(0.5 + 9 * rnd.NextDouble(), 0.5 + 9 * rnd.NextDouble(), 0.5 + 9 * rnd.NextDouble()));
                }
            }
            return pts;
        }

        static AxisAlignedBox3d Box() { return new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(10, 10, 10)); }

        // rounds of up to `window` points against `round` (each plan against the triangulation at the start of the round);
        // the same kept points, in the same order, go one at a time into `serial` with Insert. Returns the coverage.
        static Coverage RunRounds(List<Vector3d> pts, int window, int threads, out Delaunay3 round, out Delaunay3 serial)
        {
            round = new Delaunay3(Box());
            serial = new Delaunay3(Box());
            var cov = new Coverage();
            var queue = new Queue<Vector3d>(pts);
            var plans = new List<Delaunay3.InsertPlan>();
            var rnd = new Random(5);
            while (queue.Count > 0) {
                var batch = new List<Vector3d>();
                while (batch.Count < window && queue.Count > 0) batch.Add(queue.Dequeue());
                while (plans.Count < batch.Count) plans.Add(new Delaunay3.InsertPlan());
                Delaunay3 dt = round;
                // (hints: random live cells, so the walks differ from Insert's)
                var hints = batch.Select(_ => rnd.Next(dt.MaxCellID)).ToArray();
                Action<int> plan = k => dt.PlanInsert(batch[k], dt.IsCell(hints[k]) ? hints[k] : -1, 1e-12, plans[k], (uint)(k + 1) * 2654435761u);
                if (threads == 1) for (int k = 0; k < batch.Count; ++k) plan(k);
                else Parallel.For(0, batch.Count, new ParallelOptions() { MaxDegreeOfParallelism = threads }, plan);
                // select in order: a plan is kept if none of its reads is a write (cavity or outer neighbour) of a kept plan
                var written = new HashSet<int>();
                var keptVertices = new List<HashSet<int>>();
                var keptEdges = new HashSet<long>();
                var kept = new List<int>();
                for (int k = 0; k < batch.Count; ++k) {
                    var pl = plans[k];
                    if (pl.Result != Delaunay3.InsertResult.Inserted) {
                        cov.Rejected++;
                        // (a rejected point is rejected by Insert too, in the state of the round, if nothing it read was written)
                        if (!pl.Reads.Any(written.Contains)) {
                            Assert.NotEqual(Delaunay3.InsertResult.Inserted, serial.Insert(batch[k], -1, 1e-12, out _));
                        } else queue.Enqueue(batch[k]);
                        continue;
                    }
                    if (pl.Reads.Any(written.Contains)) { queue.Enqueue(batch[k]); continue; }
                    foreach (int q in pl.Reads) written.Add(q);
                    kept.Add(k);
                }
                // coverage: the cases of the review
                foreach (int k in kept) {
                    var pl = plans[k];
                    if (pl.Cells.Count < pl.Cavity.Count) cov.FewerNewCells++;
                    if (pl.Cells.Where(x => x.Outer >= 0).GroupBy(x => x.Outer).Any(gr => gr.Count() > 1)) cov.OuterTwice++;
                    var vs = new HashSet<int>(pl.Cells.SelectMany(x => new[] { x.A, x.B, x.C }));
                    foreach (var other in keptVertices)
                        if (vs.Overlaps(other)) { cov.SharedVertex++; break; }
                    keptVertices.Add(vs);
                    var es = new HashSet<long>();
                    foreach (var x in pl.Cells)
                        foreach (var (u, w) in new[] { (x.A, x.B), (x.B, x.C), (x.C, x.A) })
                            es.Add(u < w ? ((long)u << 32) | (uint)w : ((long)w << 32) | (uint)u);
                    if (es.Overlaps(keptEdges)) cov.SharedEdge++;
                    keptEdges.UnionWith(es);
                    // (the conflict region grown beyond the cells whose circumsphere contains the point strictly)
                    if (pl.Cavity.Any(c => dt.InCircumsphere(c, pl.P) <= 0)) cov.Grown++;
                }
                // ids serially in order, then the writes in parallel
                var freed = new HashSet<int>();
                foreach (int k in kept) {
                    var pl = plans[k];
                    round.AssignInsertIds(pl);
                    if (pl.NewIds.Any(freed.Contains)) cov.FreedIdReused++;
                    for (int j = pl.Cells.Count; j < pl.Cavity.Count; ++j) freed.Add(pl.Cavity[j]);
                }
                if (threads == 1) foreach (int k in kept) round.ApplyInsert(plans[k]);
                else Parallel.ForEach(kept, new ParallelOptions() { MaxDegreeOfParallelism = threads }, k => dt.ApplyInsert(plans[k]));
                foreach (int k in kept) {
                    var res = serial.Insert(batch[k], -1, 1e-12, out int v);
                    Assert.Equal(Delaunay3.InsertResult.Inserted, res);
                    Assert.Equal(plans[k].Vertex, v);
                }
                cov.Rounds++;
                cov.Kept += kept.Count;
            }
            return cov;
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void Rounds_GiveTheTetrahedraOfSerialInsertion(int seed, bool lattice)
        {
            var pts = Points(seed, 700, lattice);
            var cov = RunRounds(pts, 48, 4, out Delaunay3 round, out Delaunay3 serial);
            Assert.Null(round.Validate(true));
            Assert.Null(serial.Validate(true));
            Assert.Equal(serial.CellCount, round.CellCount);
            Assert.Equal(Cells(serial), Cells(round));
            Assert.True(cov.Rounds < cov.Kept, $"{cov.Rounds} rounds for {cov.Kept} points: the rounds must keep several points");
            Assert.True(cov.SharedVertex > 0 && cov.SharedEdge > 0, $"kept cavities sharing a vertex {cov.SharedVertex}, an edge {cov.SharedEdge}");
            Assert.True(cov.OuterTwice > 0, "an outer neighbour adjacent to two cavity cells");
            if (lattice) {
                Assert.True(cov.Grown > 0, "star-shape growth in a round");
                Assert.True(cov.Rejected > 0, "points on vertices (rejected)");
            }
        }

        // a cavity with fewer new cells than removed ones frees ids in AssignInsertIds; a later plan of the same round takes
        // them from the free stack, and only that plan writes them
        [Fact]
        public void Rounds_FreedIdsReusedInTheSameRound()
        {
            int fewer = 0, reused = 0;
            for (int seed = 10; seed < 40 && (fewer == 0 || reused == 0); ++seed) {
                var cov = RunRounds(Points(seed, 500, true), 64, 4, out Delaunay3 round, out Delaunay3 serial);
                Assert.Null(round.Validate(false));
                Assert.Equal(Cells(serial), Cells(round));
                fewer += cov.FewerNewCells;
                reused += cov.FreedIdReused;
            }
            Assert.True(fewer > 0, "no cavity with fewer new cells than removed ones");
            Assert.True(reused > 0, "no freed id reused in its round");
        }

        // the plans of a round do not depend on the thread count (read only), and neither does the result
        [Fact]
        public void Rounds_SameResultForEveryThreadCount()
        {
            var pts = Points(7, 600, true);
            RunRounds(pts, 64, 1, out Delaunay3 r1, out _);
            string reference = string.Join("\n", Cells(r1));
            var ids = Enumerable.Range(0, r1.MaxCellID).Where(r1.IsCell).Select(c => c + ":" + r1.GetCell(c)).ToList();
            foreach (int threads in new[] { 2, 4, 8 }) {
                RunRounds(pts, 64, threads, out Delaunay3 rt, out _);
                Assert.Equal(reference, string.Join("\n", Cells(rt)));
                // (also the cell ids: AssignInsertIds runs in the order of the round)
                Assert.Equal(ids, Enumerable.Range(0, rt.MaxCellID).Where(rt.IsCell).Select(c => c + ":" + rt.GetCell(c)).ToList());
            }
        }

        // PlanInsert writes nothing: the triangulation, its walk state and the result of a later Insert are unchanged
        [Fact]
        public void PlanInsert_ChangesNothing()
        {
            var a = new Delaunay3(Box());
            var b = new Delaunay3(Box());
            var pts = Points(3, 300, false);
            var plan = new Delaunay3.InsertPlan();
            foreach (Vector3d p in pts) {
                a.PlanInsert(p, -1, 1e-12, plan, 12345);
                a.PlanInsert(p + new Vector3d(1e-3, 0, 0), 0, 1e-12, plan, 777);
                a.Insert(p, -1, 1e-12, out int va);
                b.Insert(p, -1, 1e-12, out int vb);
                Assert.Equal(vb, va);
                Assert.Equal(b.LastWalkSteps, a.LastWalkSteps);
            }
            Assert.Equal(Enumerable.Range(0, b.MaxCellID).Where(b.IsCell).Select(c => c + ":" + b.GetCell(c)),
                Enumerable.Range(0, a.MaxCellID).Where(a.IsCell).Select(c => c + ":" + a.GetCell(c)));
        }

        // the plan's result is Insert's, for the rejections too (outside the box, on a vertex, closer than the separation)
        [Fact]
        public void PlanInsert_ResultIsInsertsResult()
        {
            var dt = new Delaunay3(Box());
            foreach (Vector3d p in Points(4, 200, false))
                dt.Insert(p, -1, 1e-12, out _);
            var plan = new Delaunay3.InsertPlan();
            var probes = new List<(Vector3d p, double sep)> {
                (new Vector3d(-1, 5, 5), 1e-12), (new Vector3d(10, 5, 5), 1e-12), (dt.GetVertex(20), 1e-12),
                (dt.GetVertex(30) + new Vector3d(1e-4, 0, 0), 1e-3), (new Vector3d(5, 5, 5), 1e-12), (new Vector3d(1, 2, 3), 0.5) };
            foreach (var (p, sep) in probes) {
                dt.PlanInsert(p, -1, sep, plan, 99);
                var expected = dt.Insert(p, -1, sep, out _);
                Assert.Equal(expected, plan.Result);
                if (expected == Delaunay3.InsertResult.Inserted) {
                    // (undo is not available: compare with a fresh plan of the next probe only)
                    Assert.Null(dt.Validate(false));
                }
            }
        }
    }
}
