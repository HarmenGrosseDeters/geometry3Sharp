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
            public int FewerNewCells, FreedIdReused, OuterTwice, SharedVertex, SharedEdge, Cospherical, Rejected, Rounds, Kept;
        }

        static List<Vector3d> Points(int seed, int n, bool lattice)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            for (int k = 0; k < n; ++k) {
                if (lattice) {
                    // cospherical and coplanar groups (integer lattice points, also on the box's mid planes): the star-shape
                    // growth, cavities with interior edges, repeated points
                    pts.Add(new Vector3d(rnd.Next(1, 10), rnd.Next(1, 10), rnd.Next(1, 10)));
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
                    // (cospherical: a cell next to the cavity with the point on its circumsphere. The star-shape growth of
                    // Insert cannot occur in an exact Delaunay triangulation: a point in the plane of a boundary facet is inside
                    // both circumspheres of the facet's cells or inside neither, as both meet that plane in the facet's
                    // circumcircle; it is not counted here)
                    var cavitySet = new HashSet<int>(pl.Cavity);
                    if (pl.Reads.Any(c => !cavitySet.Contains(c) && dt.InCircumsphere(c, pl.P) == 0)) cov.Cospherical++;
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
                Assert.True(cov.Cospherical > 0, "a cospherical neighbour of a kept cavity");
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

        // only a plan whose Result is Inserted can be given ids: an outside point or a repeated one throws and changes nothing
        [Fact]
        public void AssignInsertIds_RefusesAPlanThatIsNotAnInsertion()
        {
            var dt = new Delaunay3(Box());
            var plan = new Delaunay3.InsertPlan();
            dt.PlanInsert(new Vector3d(5, 5, 5), -1, 1e-12, plan, 3);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            dt.AssignInsertIds(plan);
            dt.ApplyInsert(plan);
            int vertices = dt.VertexCount;
            dt.PlanInsert(new Vector3d(5, 5, 5), -1, 1e-12, plan, 3);
            Assert.Equal(Delaunay3.InsertResult.TooClose, plan.Result);
            Assert.Throws<InvalidOperationException>(() => dt.AssignInsertIds(plan));
            dt.PlanInsert(new Vector3d(-100, 5, 5), -1, 1e-12, plan, 3);
            Assert.Equal(Delaunay3.InsertResult.Outside, plan.Result);
            Assert.Throws<InvalidOperationException>(() => dt.AssignInsertIds(plan));
            Assert.Equal(vertices, dt.VertexCount);
            Assert.Null(dt.Validate(true));
        }

        // the round insertion does not keep RefinementSupport's bookkeeping (labels, incident cells, undo): refused with it on;
        // a Clone after round insertions is the same triangulation and accepts serial insertions with the support on
        [Fact]
        public void RoundInsert_RefusedWithRefinementSupport_CloneAfterwardsWorks()
        {
            var dt = new Delaunay3(Box());
            var plan = new Delaunay3.InsertPlan();
            foreach (Vector3d p in Points(6, 200, false)) {
                dt.PlanInsert(p, -1, 1e-12, plan, 3);
                if (plan.Result != Delaunay3.InsertResult.Inserted) continue;
                dt.AssignInsertIds(plan);
                dt.ApplyInsert(plan);
            }
            Assert.Null(dt.Validate(true));
            var copy = dt.Clone();
            Assert.Equal(Cells(dt), Cells(copy));
            copy.RefinementSupport = true;
            Assert.Equal(Delaunay3.InsertResult.Inserted, copy.Insert(new Vector3d(5.01, 4.99, 5.02), -1, 1e-12, out _));
            Assert.Null(copy.Validate(true));
            dt.RefinementSupport = true;
            dt.PlanInsert(new Vector3d(4.01, 3.99, 4.02), -1, 1e-12, plan, 3);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            Assert.Throws<InvalidOperationException>(() => dt.AssignInsertIds(plan));
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

        [Fact]
        public void Circumcenter_And_CellVolume_ExtremeScales_StaysFiniteAndScaled()
        {
            // Regular unit-corner tetrahedron: circumcenter is (0.5, 0.5, 0.5)
            Vector3d a0 = Vector3d.Zero, b0 = new Vector3d(1, 0, 0), c0 = new Vector3d(0, 1, 0), d0 = new Vector3d(0, 0, 1);
            Vector3d cc0 = Delaunay3.Circumcenter(a0, b0, c0, d0);
            Assert.Equal(0.5, cc0.x, 12);
            Assert.Equal(0.5, cc0.y, 12);
            Assert.Equal(0.5, cc0.z, 12);

            // At 2^+-270 and 2^+-600, u.LengthSquared * vw has degree 4 (2^+-1080 / 2^+-2400) and overflows/underflows to Inf/0/NaN on master
            foreach (int exp in new[] { 270, -270, 600, -600 }) {
                double s = ExactPoint3.ScaleB(1.0, exp);
                Vector3d a = Vector3d.Zero, b = new Vector3d(s, 0, 0), c = new Vector3d(0, s, 0), d = new Vector3d(0, 0, s);
                Vector3d cc = Delaunay3.Circumcenter(a, b, c, d);
                Vector3d cc2 = Delaunay3.Circumcenter(a, b, c, d);
                Assert.Equal(cc, cc2); // determinism
                double expected = ExactPoint3.ScaleB(0.5, exp);
                double tol = ExactPoint3.ScaleB(1e-12, exp);
                Assert.True(cc.IsFinite, $"exp {exp}: non-finite circumcenter {cc}");
                Assert.True(Math.Abs(cc.x - expected) <= tol && Math.Abs(cc.y - expected) <= tol && Math.Abs(cc.z - expected) <= tol,
                    $"exp {exp}: cc={cc}, expected={expected}");
            }

            // Mixed-exponent anisotropic tetrahedra: single-scale normalization underflows/overflows the short/long axis terms
            foreach (var (ex, ey, ez) in new[] { (0, -600, -600), (0, -400, -400), (520, 520, -1000) }) {
                double sx = ExactPoint3.ScaleB(1.0, ex), sy = ExactPoint3.ScaleB(1.0, ey), sz = ExactPoint3.ScaleB(1.0, ez);
                Vector3d cc = Delaunay3.Circumcenter(Vector3d.Zero, new Vector3d(sx, 0, 0), new Vector3d(0, sy, 0), new Vector3d(0, 0, sz));
                double exx = ExactPoint3.ScaleB(0.5, ex), exy = ExactPoint3.ScaleB(0.5, ey), exz = ExactPoint3.ScaleB(0.5, ez);
                Assert.True(cc.IsFinite, $"mixed ({ex},{ey},{ez}): non-finite {cc}");
                Assert.True(Math.Abs(cc.x - exx) <= Math.Abs(exx) * 1e-12
                    && Math.Abs(cc.y - exy) <= Math.Abs(exy) * 1e-12
                    && Math.Abs(cc.z - exz) <= Math.Abs(exz) * 1e-12,
                    $"mixed ({ex},{ey},{ez}): cc={cc}, expected=({exx},{exy},{exz})");
            }

            // pre == 1 branch: coordinate differences overflow double ([-9e307, +9e307]), circumcenter is (0, 0, 0)
            double big = 9e307;
            Vector3d ccPre1 = Delaunay3.Circumcenter(
                new Vector3d(-big, -big, -big),
                new Vector3d(big, big, -big),
                new Vector3d(big, -big, big),
                new Vector3d(-big, big, big));
            Assert.True(ccPre1.IsFinite && ccPre1.MaxAbs <= 1e295, $"pre==1 cc={ccPre1}");

            // |cc - a| > double.MaxValue while |cc| <= double.MaxValue: rescaled frame avoids intermediate overflow
            Vector3d ccLargeOff1 = Delaunay3.Circumcenter(
                new Vector3d(-1.6e308, 0, 0),
                new Vector3d(1.6e308, 1.4e308, 0),
                new Vector3d(1.6e308, -1.4e308, 0),
                new Vector3d(1.6e308, 0, 1.4e308));
            Assert.True(ccLargeOff1.IsFinite, $"large offset 1: {ccLargeOff1}");
            Assert.True(Math.Abs(ccLargeOff1.x - 3.0625e307) <= 1e295 && Math.Abs(ccLargeOff1.y) <= 1e295 && Math.Abs(ccLargeOff1.z) <= 1e295,
                $"large offset 1: cc={ccLargeOff1}");

            Vector3d ccLargeOff2 = Delaunay3.Circumcenter(
                new Vector3d(-1e308, 0, 0),
                new Vector3d(0, 1.7e308, 0),
                new Vector3d(0, -1.7e308, 0),
                new Vector3d(0, 0, 1.7e308));
            Assert.True(ccLargeOff2.IsFinite, $"large offset 2: {ccLargeOff2}");
            Assert.True(Math.Abs(ccLargeOff2.x - 9.45e307) <= 1e295 && Math.Abs(ccLargeOff2.y) <= 1e295 && Math.Abs(ccLargeOff2.z) <= 1e295,
                $"large offset 2: cc={ccLargeOff2}");

            // CellVolume on anisotropic box [0, 2^520] x [0, 2^520] x [0, 2^-1000]: each of the 6 cells has volume 2^40 / 6
            double s520 = ExactPoint3.ScaleB(1.0, 520), sm1000 = ExactPoint3.ScaleB(1.0, -1000);
            var aniso = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(s520, s520, sm1000)));
            double expectedCellVol = ExactPoint3.ScaleB(1.0 / 6.0, 40);
            int cellCount = 0;
            foreach (int c in aniso.CellIndices()) {
                double vol = aniso.CellVolume(c);
                Assert.True(double.IsFinite(vol), $"cell {c}: non-finite volume {vol}");
                Assert.True(Math.Abs(vol - expectedCellVol) <= expectedCellVol * 1e-12,
                    $"cell {c}: vol={vol}, expected={expectedCellVol}");
                cellCount++;
            }
            Assert.Equal(6, cellCount);

            // Non-finite / zero-edge inputs return NaN cleanly
            Vector3d ccNan = Delaunay3.Circumcenter(new Vector3d(double.NaN, 0, 0), b0, c0, d0);
            Assert.True(double.IsNaN(ccNan.x));
            Vector3d ccZero = Delaunay3.Circumcenter(a0, a0, a0, a0);
            Assert.True(double.IsNaN(ccZero.x));
        }

        [Fact]
        public void Insert_And_PlanInsert_TinyMinSeparation_And_CosphericalDomeCollapse()
        {
            // 1. Tiny minSeparation (2^-545) with point at distance sqrt(3) * 2^-540 > minSeparation from box corner (0,0,0):
            //    on master, minSqr (2^-1090) and DistanceSquared (3 * 2^-1080) both underflow to 0.0, so 0.0 <= 0.0 falsely returns TooClose!
            double coord = ExactPoint3.ScaleB(1.0, -540);
            double minSep = ExactPoint3.ScaleB(1.0, -545);
            Vector3d pNear = new Vector3d(coord, coord, coord);
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            var plan = new Delaunay3.InsertPlan();
            dt.PlanInsert(pNear, -1, minSep, plan, 7u);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(pNear, -1, minSep, out _));
            Assert.Null(dt.Validate(true));

            // Point strictly closer than minSeparation must still be rejected as TooClose
            double closer = ExactPoint3.ScaleB(1.0, -547);
            Vector3d pTooClose = new Vector3d(closer, closer, closer);
            dt.PlanInsert(pTooClose, -1, minSep, plan, 11u);
            Assert.Equal(Delaunay3.InsertResult.TooClose, plan.Result);
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(pTooClose, -1, minSep, out _));

            // Separation edge cases: minSeparation == 0, < 0, NaN, +Inf, and overflowing minSeparation^2 (2^520)
            Vector3d pMid = new Vector3d(0.5, 0.5, 0.5);
            dt.PlanInsert(pMid, -1, 0.0, plan, 13u);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            dt.PlanInsert(pMid, -1, -1.0, plan, 17u);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            dt.PlanInsert(pMid, -1, double.NaN, plan, 19u);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            dt.PlanInsert(pMid, -1, double.PositiveInfinity, plan, 23u);
            Assert.Equal(Delaunay3.InsertResult.TooClose, plan.Result);
            dt.PlanInsert(pMid, -1, ExactPoint3.ScaleB(1.0, 520), plan, 29u);
            Assert.Equal(Delaunay3.InsertResult.TooClose, plan.Result);
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(pMid, -1, ExactPoint3.ScaleB(1.0, 520), out _));
            Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(pMid, -1, 0.0, out _));
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(pMid, -1, 0.0, out _));

            // Huge-scale separation (2^520 vs 2^515): DistanceSquared and minSeparation^2 both overflow to +Inf on master -> false TooClose
            double s521 = ExactPoint3.ScaleB(1.0, 521), s520 = ExactPoint3.ScaleB(1.0, 520), s515 = ExactPoint3.ScaleB(1.0, 515);
            var dtHuge = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(s521, s521, s521)));
            Vector3d pHuge = new Vector3d(s520, s520, s520);
            dtHuge.PlanInsert(pHuge, -1, s515, plan, 31u);
            Assert.Equal(Delaunay3.InsertResult.Inserted, plan.Result);
            Assert.Equal(Delaunay3.InsertResult.Inserted, dtHuge.Insert(pHuge, -1, s515, out _));
            Assert.Null(dtHuge.Validate(true));

            // 2. Cospherical sphere (14 exact integer-lattice points on sphere of radius sqrt(9) = 3 around (5,5,5)):
            //    inserting the ball center (5,5,5) collapses the cospherical cavity cleanly with Validate(true) == null.
            var dome = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(10, 10, 10)));
            var cospherical = new List<Vector3d> {
                new Vector3d(8, 5, 5), new Vector3d(2, 5, 5),
                new Vector3d(5, 8, 5), new Vector3d(5, 2, 5),
                new Vector3d(5, 5, 8), new Vector3d(5, 5, 2),
                new Vector3d(7, 7, 6), new Vector3d(7, 7, 4),
                new Vector3d(7, 3, 6), new Vector3d(7, 3, 4),
                new Vector3d(3, 7, 6), new Vector3d(3, 7, 4),
                new Vector3d(3, 3, 6), new Vector3d(3, 3, 4),
            };
            foreach (Vector3d q in cospherical)
                Assert.Equal(Delaunay3.InsertResult.Inserted, dome.Insert(q, -1, 1e-12, out _));
            Assert.Null(dome.Validate(true));
            var removed = new List<int>();
            var added = new List<int>();
            Assert.Equal(Delaunay3.InsertResult.Inserted, dome.Insert(new Vector3d(5, 5, 5), -1, 1e-12, out _, removed, added));
            Assert.True(removed.Count >= 14, $"expected large cospherical cavity, got {removed.Count}");
            Assert.Equal(2 * cospherical.Count - 4, added.Count); // F = 2*V_b - 4 = 24 radial tetrahedra
            Assert.Null(dome.Validate(true));
        }
    }
}
