using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshSweepCollision (docs/design/CCD.md): approaching and missing meshes with known contact times, the broad phase
    /// against a brute-force pair loop, the self sweep, reuse, determinism (parallel equals serial) and invariance.
    /// </summary>
    public class MeshSweepCollisionTests
    {
        static DMesh3 box(Vector3d center, double size)
            => new TrivialBox3Generator() { Box = new Box3d(center, size * 0.5 * Vector3d.One) }.Generate().MakeDMesh();

        static DMesh3 sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        static Vector3d[] moved(DMesh3 m, Func<Vector3d, Vector3d> f)
        {
            var e = new Vector3d[m.MaxVertexID];
            foreach (int vid in m.VertexIndices()) e[vid] = f(m.GetVertex(vid));
            return e;
        }

        static DMesh3 map(DMesh3 m, Func<Vector3d, Vector3d> f)
        {
            var c = new DMesh3(m);
            foreach (int vid in c.VertexIndices()) c.SetVertex(vid, f(c.GetVertex(vid)));
            return c;
        }

        // unit box A at x in [-1, 0] moving by dx towards unit box B at x in [0.25, 1.25]: faces meet at t = 0.25 / dx
        static MeshSweepCollision approach(double dx, CcdOptions o = null, Func<Vector3d, Vector3d> f = null)
        {
            f = f ?? (x => x);
            DMesh3 a = box(new Vector3d(-0.5, 0, 0), 1), b = box(new Vector3d(0.75, 0.25, 0.125), 1);
            var sweep = new MeshSweepCollision(map(a, f), moved(a, x => f(x + new Vector3d(dx, 0, 0))), map(b, f), moved(b, f));
            if (o != null) sweep.Options = o;
            return sweep;
        }

        [Fact]
        public void Approach_ContactAtTheExpectedTime()
        {
            var sweep = approach(0.5);
            Assert.Equal(MeshSweepStatus.Collision, sweep.Compute());
            Assert.True(sweep.EarliestTimeOfImpact <= 0.5 && sweep.EarliestTimeOfImpact >= 0.5 - 1e-5, $"{sweep.EarliestTimeOfImpact:R}");
            Assert.True(sweep.EarliestNearContactTime >= sweep.EarliestTimeOfImpact && sweep.EarliestNearContactTime <= 0.5 + 1e-5);
            Assert.NotEmpty(sweep.Contacts);
            Assert.True(sweep.CandidatePairs > sweep.Contacts.Count);
            foreach (SweepContact c in sweep.Contacts) {
                Assert.True(c.Result.Status == CcdStatus.Collision);
                Assert.True(c.Result.TimeOfImpact <= sweep.EarliestNearContactTime);
                Assert.True(c.First >= 0 && c.Second >= 0);
            }
            // sorted
            for (int i = 1; i < sweep.Contacts.Count; ++i)
                Assert.True(sweep.Contacts[i - 1].Result.TimeOfImpact <= sweep.Contacts[i].Result.TimeOfImpact);
            // with a minimum separation the contact comes earlier: the faces are 1/8 apart at t = (0.25 - 0.125) / 0.5
            var sep = approach(0.5, new CcdOptions { MinSeparation = 0.125 });
            Assert.Equal(MeshSweepStatus.Collision, sep.Compute());
            Assert.True(sep.EarliestTimeOfImpact <= 0.25 && sep.EarliestTimeOfImpact >= 0.25 - 1e-5, $"{sep.EarliestTimeOfImpact:R}");
        }

        // pairs that hit the iteration limit are kept (they may be the first contact) and make the status IterationLimit
        [Fact]
        public void IterationLimit_PairsAreKept()
        {
            var sweep = approach(0.5, new CcdOptions { MaxIterations = 2 });
            sweep.BlockSize = 3;
            Assert.Equal(MeshSweepStatus.IterationLimit, sweep.Compute());
            Assert.Contains(sweep.Contacts, c => c.Result.Status == CcdStatus.IterationLimit);
            Assert.True(sweep.EarliestTimeOfImpact <= 0.5);
            foreach (SweepContact c in sweep.Contacts)
                Assert.True(c.Result.Status == CcdStatus.Collision || c.Result.Status == CcdStatus.IterationLimit);
        }

        [Fact]
        public void Miss_IsNoCollision()
        {
            var sweep = approach(0.2);
            Assert.Equal(MeshSweepStatus.NoCollision, sweep.Compute());
            Assert.Empty(sweep.Contacts);
            Assert.True(double.IsPositiveInfinity(sweep.EarliestTimeOfImpact));
            // the minimum separation turns the near miss (0.05 apart at the end) into a contact
            var sep = approach(0.2, new CcdOptions { MinSeparation = 0.0625 });
            Assert.Equal(MeshSweepStatus.Collision, sep.Compute());
        }

        // every pair the broad phase drops is a NoCollision, and every candidate's result equals the single query's
        [Fact]
        public void BroadPhase_AgainstBruteForce()
        {
            var rnd = new Random(2);
            for (int round = 0; round < 6; ++round) {
                DMesh3 a = sphere(new Vector3d(0, 0, 0), 1, 3), b = sphere(new Vector3d(2.2, 0.3 * rnd.NextDouble(), 0), 1, 3);
                Vector3d move = new Vector3d(0.3 + 0.3 * round, 0.1 * rnd.NextDouble(), 0);
                Vector3d[] endA = moved(a, x => x + move + 0.05 * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()));
                Vector3d[] endB = moved(b, x => x);
                var o = new CcdOptions { MinSeparation = (round % 2) * 0.01 };
                var sweep = new MeshSweepCollision(a, endA, b, endB) { Options = o, CollectAll = true };
                sweep.Compute();
                var got = new Dictionary<(SweepContactKind, bool, int, int), CcdResult>();
                foreach (SweepContact c in sweep.Contacts) got[(c.Kind, c.FirstOfMeshA, c.First, c.Second)] = c.Result;
                Assert.Equal(sweep.CandidatePairs, got.Count);
                double earliest = double.PositiveInfinity;
                void check(SweepContactKind kind, bool ofA, int first, int second, CcdResult r)
                {
                    if (got.TryGetValue((kind, ofA, first, second), out CcdResult g))
                        Assert.Equal(r, g);
                    else
                        Assert.Equal(CcdStatus.NoCollision, r.Status);
                    if (r.Status != CcdStatus.NoCollision) earliest = Math.Min(earliest, r.TimeOfImpact);
                }
                foreach (var (vm, ve, tm, te, ofA) in new[] { (a, endA, b, endB, true), (b, endB, a, endA, false) })
                    foreach (int vid in vm.VertexIndices())
                        foreach (int tid in tm.TriangleIndices()) {
                            Index3i t = tm.GetTriangle(tid);
                            CcdResult r = ContinuousCollision.VertexTriangle(vm.GetVertex(vid), tm.GetVertex(t.a), tm.GetVertex(t.b), tm.GetVertex(t.c),
                                ve[vid], te[t.a], te[t.b], te[t.c], o);
                            check(SweepContactKind.VertexTriangle, ofA, vid, tid, r);
                        }
                foreach (int ea in a.EdgeIndices())
                    foreach (int eb in b.EdgeIndices()) {
                        Index2i x = a.GetEdgeV(ea), y = b.GetEdgeV(eb);
                        CcdResult r = ContinuousCollision.EdgeEdge(a.GetVertex(x.a), a.GetVertex(x.b), b.GetVertex(y.a), b.GetVertex(y.b),
                            endA[x.a], endA[x.b], endB[y.a], endB[y.b], o);
                        check(SweepContactKind.EdgeEdge, true, ea, eb, r);
                    }
                Assert.Equal(earliest, sweep.EarliestTimeOfImpact);
            }
        }

        [Fact]
        public void SelfSweep()
        {
            // a rigid translation of one closed mesh: no self contact (incident pairs are skipped)
            DMesh3 s = sphere(Vector3d.Zero, 1, 4);
            var rigid = new MeshSweepCollision(s, moved(s, x => x + new Vector3d(0.3, 0.1, 0)));
            Assert.Equal(MeshSweepStatus.NoCollision, rigid.Compute());
            Assert.True(rigid.CandidatePairs > 0);
            // two components in one mesh, one moving into the other: the same contact time as A against B
            DMesh3 a = box(new Vector3d(-0.5, 0, 0), 1), b = box(new Vector3d(0.75, 0.25, 0.125), 1);
            var both = new DMesh3(a);
            int n = both.MaxVertexID;
            new MeshEditor(both).AppendMesh(b);
            var end = new Vector3d[both.MaxVertexID];
            foreach (int vid in both.VertexIndices()) end[vid] = both.GetVertex(vid) + (vid < n ? new Vector3d(0.5, 0, 0) : Vector3d.Zero);
            var sweep = new MeshSweepCollision(both, end);
            Assert.Equal(MeshSweepStatus.Collision, sweep.Compute());
            var ab = approach(0.5);
            ab.Compute();
            Assert.Equal(ab.EarliestTimeOfImpact, sweep.EarliestTimeOfImpact);
            foreach (SweepContact c in sweep.Contacts) {
                // no pair shares a vertex
                if (c.Kind == SweepContactKind.VertexTriangle) { Index3i t = both.GetTriangle(c.Second); Assert.True(t.a != c.First && t.b != c.First && t.c != c.First); }
                else {
                    Index2i x = both.GetEdgeV(c.First), y = both.GetEdgeV(c.Second);
                    Assert.True(x.a != y.a && x.a != y.b && x.b != y.a && x.b != y.b);
                }
            }
        }

        [Fact]
        public void Reuse_AndNewEndPositions()
        {
            DMesh3 a = box(new Vector3d(-0.5, 0, 0), 1), b = box(new Vector3d(0.75, 0.25, 0.125), 1);
            var sweep = new MeshSweepCollision(a, moved(a, x => x + new Vector3d(0.5, 0, 0)), b, moved(b, x => x));
            sweep.Compute();
            double t1 = sweep.EarliestTimeOfImpact;
            var c1 = sweep.Contacts.ToList();
            sweep.Compute();
            Assert.Equal(t1, sweep.EarliestTimeOfImpact);
            Assert.Equal(c1, sweep.Contacts);
            Assert.True(sweep.CandidatePairCount > 0);
            sweep.SetEndPositions(moved(a, x => new Vector3d(double.NaN, 0, 0)), moved(b, x => x));
            Assert.Equal(MeshSweepStatus.InvalidInput, sweep.Compute());
            Assert.Equal(0, sweep.CandidatePairCount);
            Assert.Equal(0, sweep.CandidatePairs);
            sweep.SetEndPositions(moved(a, x => x + new Vector3d(0.2, 0, 0)), moved(b, x => x));
            Assert.Equal(MeshSweepStatus.NoCollision, sweep.Compute());
            foreach (var bad in new Action<MeshSweepCollision>[] { x => x.BlockSize = 0, x => x.BlockSize = int.MaxValue, x => x.SelfTasks = 0, x => x.BatchPairs = 0 }) {
                var t = new MeshSweepCollision(a, moved(a, x => x), b, moved(b, x => x));
                bad(t);
                Assert.Equal(MeshSweepStatus.InvalidInput, t.Compute());
            }
            sweep.SetEndPositions(moved(a, x => x + new Vector3d(0.5, 0, 0)), moved(b, x => x));
            sweep.Compute();
            Assert.Equal(t1, sweep.EarliestTimeOfImpact);
            Assert.Equal(c1, sweep.Contacts);
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial()
        {
            DMesh3 a = sphere(Vector3d.Zero, 1, 6), b = sphere(new Vector3d(2.1, 0.2, 0.1), 1, 6);
            var endA = moved(a, x => x + new Vector3d(0.6, 0.05 * x.y, 0.02 * x.z));
            var endB = moved(b, x => x - new Vector3d(0.3, 0, 0));
            List<SweepContact> run(bool parallel, bool serialThread, bool all)
            {
                var s = new MeshSweepCollision(a, endA, b, endB) { Parallel = parallel, CollectAll = all, Options = new CcdOptions { MinSeparation = 0.01 } };
                gParallel.SerialOnThisThread = serialThread;
                try { s.Compute(); } finally { gParallel.SerialOnThisThread = false; }
                Assert.Equal(MeshSweepStatus.Collision, s.Status);
                return s.Contacts;
            }
            var serial = run(false, false, true);
            Assert.Equal(serial, run(true, false, true));
            Assert.Equal(serial, run(true, true, true));
            Assert.Equal(serial, run(true, false, true));
            Assert.Equal(run(false, false, false), run(true, false, false));
        }

        // the jobs (blocks of query primitives, self edge-pair tasks): parallel equals serial and repeated runs, with
        // many small jobs and with the default ones, for A against B and the self sweep; the contacts equal those of a
        // single job (one block holding everything)
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Jobs_ParallelEqualsSerial(bool self)
        {
            DMesh3 a = sphere(Vector3d.Zero, 1, 6), b = sphere(new Vector3d(2.05, 0.2, 0.1), 1, 6);
            DMesh3 m = a;
            if (self) { m = new DMesh3(a); new MeshEditor(m).AppendMesh(b); }
            int na = a.MaxVertexID;
            Vector3d[] endM = moved(m, x => x.x < 1.02 ? x + new Vector3d(0.3, 0.04 * x.y, 0.02 * x.z) : x - new Vector3d(0.1, 0, 0));
            Vector3d[] endA = moved(a, x => x + new Vector3d(0.3, 0.04 * x.y, 0.02 * x.z)), endB = moved(b, x => x - new Vector3d(0.1, 0, 0));
            // order: null runs the loops in parallel (parallel = true) or on the calling thread (false); a value runs
            // them one after the other in gParallel's serial order (0 as given, -1 backwards, else a seeded permutation)
            (List<SweepContact>, double, long) run(bool parallel, int? order, bool all, int block, int tasks)
            {
                var s = self ? new MeshSweepCollision(m, endM) : new MeshSweepCollision(a, endA, b, endB);
                s.Parallel = parallel; s.CollectAll = all; s.BlockSize = block; s.SelfTasks = tasks;
                // small blocks also get small batches of candidate pairs (many batches)
                if (block < 100) s.BatchPairs = 37;
                s.Options = new CcdOptions { MinSeparation = 0.005 };
                gParallel.SerialOnThisThread = order.HasValue;
                gParallel.SerialOrderOnThisThread = order ?? 0;
                try { s.Compute(); } finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
                Assert.Equal(MeshSweepStatus.Collision, s.Status);
                return (s.Contacts, s.EarliestTimeOfImpact, s.CandidatePairCount);
            }
            void same((List<SweepContact>, double, long) x, (List<SweepContact>, double, long) y)
            {
                Assert.Equal(x.Item1, y.Item1);
                Assert.Equal(x.Item2, y.Item2);
                Assert.Equal(x.Item3, y.Item3);
            }
            foreach (bool all in new[] { true, false }) {
                // one job holding everything is the reference
                var single = run(false, null, all, 1 << 20, 1);
                Assert.NotEmpty(single.Item1);
                // small jobs and batches: serial, parallel (repeated), backwards and permuted orders
                same(single, run(false, null, all, 7, 9));
                same(single, run(true, null, all, 7, 9));
                same(single, run(true, null, all, 7, 9));
                same(single, run(true, -1, all, 7, 9));
                same(single, run(true, 12345, all, 7, 9));
                if (all) {
                    // the default jobs
                    same(single, run(false, null, all, 128, 4096));
                    same(single, run(true, null, all, 128, 4096));
                }
            }
        }

        // the self sweep over Similarity.All(): 2^k scales give the same contacts and times bit for bit; the other frames
        // the same earliest time within the frame's resolution
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SelfSweep_Invariance(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (int subSteps in new[] { 1, 4 })
                self_sweep_invariance(sim, subSteps);
        }

        void self_sweep_invariance(Similarity sim, int subSteps)
        {
            string name = sim.Name;
            DMesh3 a = box(new Vector3d(-0.5, 0, 0), 1), b = box(new Vector3d(0.75, 0.25, 0.125), 1);
            var both = new DMesh3(a);
            int n = both.MaxVertexID;
            new MeshEditor(both).AppendMesh(b);
            MeshSweepCollision sweep(Func<Vector3d, Vector3d> f)
            {
                DMesh3 m = map(both, f);
                var end = new Vector3d[both.MaxVertexID];
                foreach (int vid in both.VertexIndices()) end[vid] = f(both.GetVertex(vid) + (vid < n ? new Vector3d(0.5, 0, 0) : Vector3d.Zero));
                var s = new MeshSweepCollision(m, end) { BlockSize = 3, SelfTasks = 5, SubSteps = subSteps };
                Assert.Equal(MeshSweepStatus.Collision, s.Compute());
                return s;
            }
            MeshSweepCollision r0 = sweep(x => x), r = sweep(sim.Apply);
            if (sim.IsExact) {
                Assert.Equal(r0.EarliestTimeOfImpact, r.EarliestTimeOfImpact);
                Assert.Equal(r0.Contacts.Select(c => (c.Kind, c.First, c.Second, c.Result.TimeOfImpact, c.Result.Iterations)),
                             r.Contacts.Select(c => (c.Kind, c.First, c.Second, c.Result.TimeOfImpact, c.Result.Iterations)));
            } else {
                double slack = 1e-5 + 256 * sim.Resolution(2) / 0.5;
                Assert.True(r.EarliestTimeOfImpact <= 0.5 + 1e-12 && r.EarliestTimeOfImpact >= 0.5 - slack, $"{name}: {r.EarliestTimeOfImpact:R}");
            }
        }

        // #866: with sub-steps the candidates are a subset of those of SubSteps = 1 (with the same results, the pairs are
        // solved over the whole step), on random scenes the contacts and times are identical, and large motions give
        // fewer candidates
        [Fact]
        public void SubSteps_SubsetOfOneSubStep()
        {
            var rnd = new Random(21);
            for (int round = 0; round < 8; ++round) {
                bool self = round % 2 == 1;
                DMesh3 a = sphere(Vector3d.Zero, 1, 4), b = sphere(new Vector3d(2.1, 0.3 * rnd.NextDouble(), 0.1), 1, 4);
                Vector3d move = new Vector3d(0.4 + 0.5 * rnd.NextDouble(), 0.3 * rnd.NextDouble(), 0.2 * rnd.NextDouble());
                double twist = rnd.NextDouble();
                Func<Vector3d, Vector3d> fa = x => x + move + twist * new Vector3d(0, 0.3 * x.z, -0.3 * x.y);
                var o = new CcdOptions { MinSeparation = (round % 3) * 0.004 };
                MeshSweepCollision make(int k, bool all, long maxHull = 1L << 27)
                {
                    MeshSweepCollision sw;
                    if (self) {
                        var m = new DMesh3(a);
                        int n = m.MaxVertexID;
                        new MeshEditor(m).AppendMesh(b);
                        var end = new Vector3d[m.MaxVertexID];
                        foreach (int v in m.VertexIndices()) end[v] = v < n ? fa(m.GetVertex(v)) : m.GetVertex(v);
                        sw = new MeshSweepCollision(m, end);
                    } else
                        sw = new MeshSweepCollision(a, moved(a, fa), b, moved(b, x => x));
                    sw.SubSteps = k; sw.CollectAll = all; sw.Options = o; sw.MaxHullDoubles = maxHull;
                    sw.Compute();
                    return sw;
                }
                var one = make(1, true);
                var a1 = make(1, false);
                var key = new Dictionary<(SweepContactKind, bool, int, int), CcdResult>();
                foreach (SweepContact c in one.Contacts) key[(c.Kind, c.FirstOfMeshA, c.First, c.Second)] = c.Result;
                foreach (int k in new[] { 2, 4, 8 }) {
                    var sub = make(k, true);
                    Assert.True(sub.CandidatePairCount <= one.CandidatePairCount);
                    foreach (SweepContact c in sub.Contacts) {
                        Assert.True(key.TryGetValue((c.Kind, c.FirstOfMeshA, c.First, c.Second), out CcdResult r), $"round {round} k {k}: a candidate SubSteps = 1 does not have");
                        Assert.Equal(r, c.Result);
                    }
                    // (the dropped pairs are farther apart than the minimum separation in every sub-step: no contact)
                    var ak = make(k, false);
                    Assert.Equal(a1.Status, ak.Status);
                    Assert.Equal(a1.EarliestTimeOfImpact, ak.EarliestTimeOfImpact);
                    Assert.Equal(a1.EarliestNearContactTime, ak.EarliestNearContactTime);
                    Assert.Equal(a1.Contacts, ak.Contacts);
                    if (k == 4 && move.x > 0.6) Assert.True(sub.CandidatePairCount < one.CandidatePairCount, $"round {round}: no fewer candidates");
                }
                // hulls above MaxHullDoubles: no filter, the candidates of SubSteps = 1
                var big = make(4, true, 0);
                Assert.Equal(one.CandidatePairCount, big.CandidatePairCount);
                Assert.Equal(one.Contacts, big.Contacts);
            }
            foreach (int bad in new[] { 0, 3, 6, 128, -4 }) {
                DMesh3 a = box(Vector3d.Zero, 1);
                Assert.Equal(MeshSweepStatus.InvalidInput, new MeshSweepCollision(a, moved(a, x => x)) { SubSteps = bad }.Compute());
            }
        }

        // review #872: SubSteps changed between two Computes on one instance gives the result of a fresh instance (the
        // hulls are rebuilt for the new SubSteps and released after each Compute)
        [Fact]
        public void SubSteps_ChangedBetweenComputes()
        {
            DMesh3 a = sphere(Vector3d.Zero, 1, 4), b = sphere(new Vector3d(2.1, 0.2, 0.1), 1, 4);
            Vector3d[] ea = moved(a, x => x + new Vector3d(1.6, 0.4, 0)), eb = moved(b, x => x);
            MeshSweepCollision fresh(int k)
            {
                var f = new MeshSweepCollision(a, ea, b, eb) { SubSteps = k, CollectAll = true };
                f.Compute();
                return f;
            }
            var sweep = new MeshSweepCollision(a, ea, b, eb) { CollectAll = true };
            foreach (int k in new[] { 4, 1, 8, 4, 2 }) {
                sweep.SubSteps = k;
                sweep.Compute();
                var f = fresh(k);
                Assert.Equal(f.CandidatePairCount, sweep.CandidatePairCount);
                Assert.Equal(f.Status, sweep.Status);
                Assert.Equal(f.EarliestTimeOfImpact, sweep.EarliestTimeOfImpact);
                Assert.Equal(f.Contacts, sweep.Contacts);
            }
            Assert.True(fresh(4).CandidatePairCount < fresh(1).CandidatePairCount);
        }

        static DMesh3 triangle(Vector3d p0, Vector3d p1, Vector3d p2)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(p0), m.AppendVertex(p1), m.AppendVertex(p2));
            return m;
        }

        // #866 sub-step filter, per sub-step: both of its ends count (a vertex and a triangle that meet exactly at t = 1,
        // from either side of each axis, so the last sub-step's boxes only touch), the overlap test is
        // closed, and the sub-step times are i / SubSteps (a vertex passing diagonally by a triangle overlaps it in the
        // whole step and in [0, 1/2], but in none of the four quarters)
        [Fact]
        public void SubSteps_TouchAtTheEndAndDiagonalPassBy()
        {
            var o = new CcdOptions { MinSeparation = 0 };
            for (int axis = 0; axis < 3; ++axis)
                for (int sign = -1; sign <= 1; sign += 2)
                    foreach (bool movingA in new[] { true, false }) {
                        // the target triangle from the plane x_axis = -sign to 0, the moving triangle's tip from x_axis =
                        // sign to 0: they meet at t = 1, where the last sub-step's boxes only touch (the inner hulls are
                        // widened by a rounding unit, the ends are exact)
                        Vector3d at(double c, double u, double v)
                        {
                            var q = Vector3d.Zero;
                            q[axis] = c; q[(axis + 1) % 3] = u; q[(axis + 2) % 3] = v;
                            return q;
                        }
                        DMesh3 target = triangle(at(-sign, -1, -1), at(-sign, 2, -1), at(-sign, -1, 2));
                        DMesh3 mover = triangle(at(sign, 0.25, 0.25), at(1.5 * sign, 0.5, 0.25), at(1.5 * sign, 0.25, 0.5));
                        Vector3d[] me = moved(mover, x => { var y = x; y[axis] -= sign; return y; });
                        Vector3d[] te = moved(target, x => { var y = x; y[axis] += sign; return y; });
                        MeshSweepCollision make(int k)
                        {
                            var sw = movingA ? new MeshSweepCollision(mover, me, target, te)
                                             : new MeshSweepCollision(target, te, mover, me);
                            sw.SubSteps = k; sw.Options = o;
                            sw.Compute();
                            return sw;
                        }
                        MeshSweepCollision one = make(1), four = make(4);
                        string name = $"axis {axis} sign {sign} A {movingA}";
                        Assert.True(one.Status == MeshSweepStatus.Collision, name);
                        Assert.Equal(one.Status, four.Status);
                        Assert.Equal(one.EarliestNearContactTime, four.EarliestNearContactTime);
                        Assert.Equal(one.Contacts, four.Contacts);
                    }

            foreach (bool movingA in new[] { true, false }) {
                // the moving vertices go from about (-10, 0, 0) to (10, 20, 0); the target is at (-7, 7, 0), 2.8 from the
                // path
                DMesh3 mover = triangle(new Vector3d(-10, 0, 0), new Vector3d(-9.75, 0, 0), new Vector3d(-10, 0.25, 0));
                DMesh3 target = triangle(new Vector3d(-7, 7, 0), new Vector3d(-6.75, 7, 0), new Vector3d(-7, 7.25, 0));
                Vector3d[] me = moved(mover, x => x + new Vector3d(20, 20, 0));
                MeshSweepCollision make(int k, long maxHull = 1L << 27)
                {
                    var sw = movingA ? new MeshSweepCollision(mover, me, target, moved(target, x => x))
                                     : new MeshSweepCollision(target, moved(target, x => x), mover, me);
                    sw.SubSteps = k; sw.MaxHullDoubles = maxHull;
                    sw.Compute();
                    return sw;
                }
                MeshSweepCollision one = make(1), four = make(4);
                Assert.Equal(MeshSweepStatus.NoCollision, one.Status);
                Assert.True(one.CandidatePairCount > 0);
                Assert.Equal(0, four.CandidatePairCount);
                Assert.Equal(MeshSweepStatus.NoCollision, four.Status);
                // the hulls take (3 + 3) vertices * 6 * (4 + 1) doubles: the filter runs at that bound, not below it
                Assert.Equal(0, make(4, 180).CandidatePairCount);
                Assert.Equal(one.CandidatePairCount, make(4, 179).CandidatePairCount);
            }
        }

        // review #793: the smallest minimum separation still widens the broad phase's boxes
        [Fact]
        public void TinyMinSeparation_ReachesTheNarrowPhase()
        {
            var a = new DMesh3();
            a.AppendVertex(new Vector3d(0.25, 0.25, double.Epsilon));
            var b = new DMesh3();
            int v0 = b.AppendVertex(Vector3d.Zero), v1 = b.AppendVertex(new Vector3d(1, 0, 0)), v2 = b.AppendVertex(new Vector3d(0, 1, 0));
            b.AppendTriangle(v0, v1, v2);
            var sweep = new MeshSweepCollision(a, moved(a, x => x), b, moved(b, x => x)) { Options = new CcdOptions { MinSeparation = double.Epsilon } };
            Assert.Equal(MeshSweepStatus.Collision, sweep.Compute());
            var none = new MeshSweepCollision(a, moved(a, x => x), b, moved(b, x => x));
            Assert.Equal(MeshSweepStatus.NoCollision, none.Compute());
        }

        // bound review of #793, S1: a subnormal minimum separation (0.5 * 5 2^-1074 rounds to 2 2^-1074): a static vertex at
        // x = 0 and a triangle in the plane x = 5 2^-1074, exactly at the minimum separation, touch
        [Fact]
        public void SubnormalMinSeparation_ExactlyAtTheDistance()
        {
            double ms = 5 * double.Epsilon;
            var a = new DMesh3();
            a.AppendVertex(new Vector3d(0, 0.25, 0.25));
            var b = new DMesh3();
            int v0 = b.AppendVertex(new Vector3d(ms, 0, 0)), v1 = b.AppendVertex(new Vector3d(ms, 1, 0)), v2 = b.AppendVertex(new Vector3d(ms, 0, 1));
            b.AppendTriangle(v0, v1, v2);
            var o = new CcdOptions { MinSeparation = ms };
            var sweep = new MeshSweepCollision(a, moved(a, x => x), b, moved(b, x => x)) { Options = o };
            Assert.Equal(MeshSweepStatus.Collision, sweep.Compute());
            Assert.Equal(CcdStatus.Collision, ContinuousCollision.VertexTriangle(a.GetVertex(0), b.GetVertex(v0), b.GetVertex(v1), b.GetVertex(v2),
                a.GetVertex(0), b.GetVertex(v0), b.GetVertex(v1), b.GetVertex(v2), o).Status);
        }

        // bound review of #793, S2 / S3: positions beyond the range are InvalidInput; a time limit bounds the earliest time
        [Fact]
        public void HugePositions_AndTimeLimit()
        {
            DMesh3 a = box(new Vector3d(-0.5, 0, 0), 1), b = box(new Vector3d(0.75, 0.25, 0.125), 1);
            var huge = moved(a, x => x);
            huge[0] = new Vector3d(-Math.Pow(2, 1020), 0, 0);    // away from B: no candidate pair, only the sweep's own check sees it
            Assert.Equal(MeshSweepStatus.InvalidInput, new MeshSweepCollision(a, huge, b, moved(b, x => x)).Compute());
            var early = approach(0.5, new CcdOptions { TimeLimit = 0.4 });
            Assert.Equal(MeshSweepStatus.NoCollision, early.Compute());
            Assert.Equal(0.4, early.EarliestTimeOfImpact);
            var late = approach(0.5, new CcdOptions { TimeLimit = 0.6 });
            Assert.Equal(MeshSweepStatus.Collision, late.Compute());
            Assert.True(late.EarliestTimeOfImpact <= 0.5 && late.EarliestTimeOfImpact >= 0.5 - 1e-5);
        }

        // review #793: an uncomputed sweep proves nothing; SetEndPositions without endB keeps B's
        [Fact]
        public void NotComputed_AndSetEndPositionsKeepsB()
        {
            DMesh3 a = box(new Vector3d(-0.5, 0, 0), 1), b = box(new Vector3d(0.75, 0.25, 0.125), 1);
            var sweep = new MeshSweepCollision(a, moved(a, x => x), b, moved(b, x => x));
            Assert.Equal(MeshSweepStatus.NotComputed, sweep.Status);
            Assert.Equal(MeshSweepStatus.NoCollision, sweep.Compute());
            sweep.SetEndPositions(moved(a, x => x + new Vector3d(0.5, 0, 0)));
            Assert.Equal(MeshSweepStatus.Collision, sweep.Compute());
        }

        [Fact]
        public void InvalidInput()
        {
            DMesh3 a = box(Vector3d.Zero, 1);
            Assert.Equal(MeshSweepStatus.InvalidInput, new MeshSweepCollision(a, new Vector3d[2]).Compute());
            Assert.Equal(MeshSweepStatus.InvalidInput, new MeshSweepCollision(null, new Vector3d[2]).Compute());
            var nan = moved(a, x => x);
            nan[0] = new Vector3d(double.NaN, 0, 0);
            var s = new MeshSweepCollision(a, nan);
            Assert.Equal(MeshSweepStatus.InvalidInput, s.Compute());
            Assert.Empty(s.Contacts);
            Assert.Equal(MeshSweepStatus.InvalidInput, new MeshSweepCollision(a, moved(a, x => x)) { Options = new CcdOptions { MinSeparation = -1 } }.Compute());
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r0 = approach(0.5);
            r0.Compute();
            var r = approach(0.5, new CcdOptions { MinSeparation = 0 }, s.Apply);
            Assert.Equal(MeshSweepStatus.Collision, r.Compute());
            if (s.IsExact) {
                Assert.Equal(r0.EarliestTimeOfImpact, r.EarliestTimeOfImpact);
                Assert.Equal(r0.EarliestNearContactTime, r.EarliestNearContactTime);
                Assert.Equal(r0.Contacts.Select(c => (c.Kind, c.First, c.Second, c.Result.TimeOfImpact)), r.Contacts.Select(c => (c.Kind, c.First, c.Second, c.Result.TimeOfImpact)));
            } else {
                // the filter's bound is absolute (64 M 2^-53 = 32 rounding units of the largest coordinate): at UTM
                // coordinates of a millimetre scene it moves the time of impact by up to e / speed
                double slack = 1e-5 + 256 * s.Resolution(2) / 0.5;
                Assert.True(r.EarliestTimeOfImpact <= 0.5 + 1e-12 && r.EarliestTimeOfImpact >= 0.5 - slack, $"{name}: {r.EarliestTimeOfImpact:R}");
            }
            // a clear miss stays a miss
            Assert.Equal(MeshSweepStatus.NoCollision, approach(0.2, null, s.Apply).Compute());
        }
    }
}
