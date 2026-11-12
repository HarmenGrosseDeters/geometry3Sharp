using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Family = geometry3Sharp.Tests.SafeRoundingDegenerateInputTests.Family;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRoundingRepair keeps the smallest vertex id per position (canonical ids, SharedPositionsTouch) and searches
    /// the intersecting pairs again only for the triangles changed since the last search. With
    /// MeshRoundingRepair.CheckOnThisThread every search is compared with a full one and every canonical id with the
    /// dictionary of positions (it throws if they differ); the results are the same bit for bit, serial or parallel.
    /// </summary>
    public class MeshRoundingRepairIncrementalTests
    {
        readonly Xunit.Abstractions.ITestOutputHelper output;
        public MeshRoundingRepairIncrementalTests(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }

        // rotated, scaled by 1e-3 and moved to UTM coordinates (as SafeRoundingBoundTests.UtmCases): the rounding
        // moves vertices, flips, contracts edges and removes vertices, with intersecting pairs among the edited triangles
        static DMesh3 Utm(Family f, int k, int count, int seed) =>
            Invariance.Transformed(SafeRoundingDegenerateInputTests.Make(f, k, seed, count), Similarity.ByName("rotated, scale 1e-3, utm"));

        // the sphere with collinear caps (flips) and a flat tetrahedron through its surface (its apex on the middle of
        // a base edge: zero volume, a degenerate face, faces overlapping the base): removed as a tiny component, its
        // intersecting pairs with the sphere go with it
        static DMesh3 SphereWithFlatTetrahedron()
        {
            DMesh3 m = SafeRoundingDegenerateInputTests.Make(Family.Collinear, 30, 1);
            Vector3d a = new Vector3d(0.6, -0.3, -0.2), b = new Vector3d(1.3, 0.1, 0.05), c = new Vector3d(0.7, 0.35, 0.3);
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), id = m.AppendVertex((a + b) / 2);
            m.AppendTriangle(ia, ic, ib); m.AppendTriangle(ia, ib, id); m.AppendTriangle(ib, ic, id); m.AppendTriangle(ic, ia, id);
            return m;
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "utm Cluster 0 4 0" };
            yield return new object[] { "utm Cluster 36 4 0" };
            yield return new object[] { "utm FoldedCluster 30 4 0" };
            yield return new object[] { "utm Cluster 0 2 0" };
            // (in AtTheLimit_Stops_SameMeshAsProposingOn, with MaxPairTests 5000: without the stop its SmallestRejectedDrift
            // comes from a move past the limit)
            yield return new object[] { "utm Cluster 0 9 1" };
            foreach (Family f in new[] { Family.Sliver, Family.ShortEdge, Family.Coincident, Family.Collinear, Family.Cluster, Family.FoldedCluster })
                yield return new object[] { f.ToString() };
        }

        internal static Func<ExactMeshBoolean> Case(string name)
        {
            DMesh3 m;
            if (name.StartsWith("utm ")) {
                string[] w = name.Split(' ');
                m = Utm((Family)Enum.Parse(typeof(Family), w[1]), int.Parse(w[2]), int.Parse(w[3]), int.Parse(w[4]));
            } else {
                Family f = (Family)Enum.Parse(typeof(Family), name);
                m = SafeRoundingDegenerateInputTests.Make(f, f == Family.Cluster || f == Family.FoldedCluster ? 36 : 30, 1);
            }
            return () => new ExactMeshBoolean(new DMesh3(m), new DMesh3(), Ops.Union);
        }

        static ExactMeshBoolean Run(Func<ExactMeshBoolean> make, bool check = false, bool serial = false)
        {
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                ExactMeshBoolean b = make();
                Assert.True(b.Compute(), b.Error);
                return b;
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
        }

        // (the checked run of a case, once for the class: Checked_SameResultAsUnchecked and Searches_IncrementalAfterEdits read the
        // same run, which costs up to 3 s for the heavy cases; the result is only read)
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<ExactMeshBoolean>> CheckedRuns =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<ExactMeshBoolean>>();
        static ExactMeshBoolean CheckedRun(string name) =>
            CheckedRuns.GetOrAdd(name, n => new Lazy<ExactMeshBoolean>(() => Run(Case(n), check: true))).Value;

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

        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Cases))]
        public void Checked_SameResultAsUnchecked(string name)
        {
            // the checks throw if an incremental search or a canonical id differs; the result is the one without them
            ExactMeshBoolean plain = Run(Case(name));
            ExactMeshBoolean checkedRun = CheckedRun(name);
            Assert.Equal(Print(plain.Result), Print(checkedRun.Result));
            Assert.Equal(plain.RoundingClean, checkedRun.RoundingClean);
            MeshRoundingRepair r = checkedRun.Rounding;
            Assert.NotNull(r);
            Assert.True(r.FullPairSearches >= 1);
        }

        [Trait("Category", "Slow")]
        [Fact]
        public void Searches_IncrementalAfterEdits()
        {
            // the repairs edit (and search again): over the cases, searches of the changed triangles and searches
            // skipped because nothing changed both happen, and every result is clean
            int incremental = 0, unchanged = 0;
            foreach (object[] c in Cases()) {
                ExactMeshBoolean b = CheckedRun((string)c[0]);
                MeshRoundingRepair r = b.Rounding;
                incremental += r.IncrementalPairSearches;
                unchanged += r.UnchangedPairSearches;
                // (folded clusters are left unclean, see SafeRoundingDegenerateInputTests, and so is the UTM cluster with
                // every 9th triangle)
                if (((string)c[0]).Contains("FoldedCluster") == false && (string)c[0] != "utm Cluster 0 9 1") {
                    Assert.True(r.IsClean, (string)c[0]);
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
                }
            }
            Assert.True(incremental > 0, "no incremental search");
            Assert.True(unchanged > 0, "no search skipped");
        }

        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Cases))]
        public void SerialEqualsParallel_BitIdentical(string name)
        {
            string serial = Print(Run(Case(name), serial: true).Result);
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == Print(Run(Case(name)).Result), $"parallel run {run} differs from the serial run");
        }

        [Fact]
        public void ReusedInstance_MeshEditedBetween_EqualsFreshInstance()
        {
            // Apply, then a vertex pushed through the sphere to beyond its far side (new intersecting pairs, no edit of the repair marks
            // them), then Apply again on the same instance: the pairs of the last Apply must not be reused
            DMesh3 m = SafeRoundingDegenerateInputTests.Make(Family.Sliver, 30, 1);
            var reused = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
            Assert.True(reused.Apply());
            int v = m.VertexIndices().First();
            m.SetVertex(v, -1.5 * m.GetVertex(v));
            DMesh3 copy = new DMesh3(m);
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                reused.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            var fresh = new MeshRoundingRepair(copy) { SharedPositionsTouch = true };
            fresh.Apply();
            Assert.True(fresh.InitialIntersections > 0);
            Assert.Equal(fresh.InitialIntersections, reused.InitialIntersections);
            Assert.Equal(fresh.RemainingIntersections, reused.RemainingIntersections);
            Assert.Equal(Print(copy), Print(m));
        }

        [Fact]
        public void TinyComponentRemoved_ItsPairsGone()
        {
            DMesh3 m = SphereWithFlatTetrahedron();
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
                Assert.True(r.Apply());
                Assert.True(r.InitialIntersections > 0);
                Assert.Equal(1, r.RemovedComponents);
                Assert.Equal(4, r.RemovedTriangles);
                Assert.Equal(0, r.RemainingIntersections);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
        }

        [Fact]
        public void Utm_PowerOfTwoScales_SameResultScaled()
        {
            // the incremental searches under the exact similarities (2^k scales): the same mesh, mapped back, bit for bit
            DMesh3 input = Utm(Family.Cluster, 0, 4, 0);
            ExactMeshBoolean b0 = Run(() => new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union), check: true);
            Assert.True(b0.Rounding.IncrementalPairSearches > 0);
            foreach (Similarity sim in Similarity.All().Where(s => s.IsExact)) {
                DMesh3 scaled = Invariance.Transformed(input, sim);
                ExactMeshBoolean b = Run(() => new ExactMeshBoolean(scaled, new DMesh3(), Ops.Union), check: true);
                Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(b.Result, sim)) == "", sim.ToString());
                Assert.Equal(b0.Rounding.MaxDrift * sim.Scale, b.Rounding.MaxDrift);
                Assert.Equal(b0.Rounding.IncrementalPairSearches, b.Rounding.IncrementalPairSearches);
            }
        }

        static DMesh3 Input(string name)
        {
            if (name.StartsWith("utm ")) {
                string[] w = name.Split(' ');
                return Utm((Family)Enum.Parse(typeof(Family), w[1]), int.Parse(w[2]), int.Parse(w[3]), int.Parse(w[4]));
            }
            Family f = (Family)Enum.Parse(typeof(Family), name);
            return SafeRoundingDegenerateInputTests.Make(f, f == Family.Cluster || f == Family.FoldedCluster ? 36 : 30, 1);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void AtTheLimit_Stops_SameMeshAsProposingOn(string name)
        {
            // at the effort limit the passes stop proposing edits (every one would be rejected at the limit): the mesh, the
            // counts and the status are those of a repair that goes on proposing, as before the stop (ProposePastTheLimit);
            // only SmallestRejectedDrift may be larger, the smallest over the edits proposed before the limit
            int reached = 0;
            foreach (long limit in new long[] { 200, 2000, 5000, 20000 }) {
                DMesh3 stop = Input(name), on = new DMesh3(stop), checkedMesh = new DMesh3(stop);
                var rs = new MeshRoundingRepair(stop) { SharedPositionsTouch = true, MaxPairTests = limit };
                rs.Apply();
                var ro = new MeshRoundingRepair(on) { SharedPositionsTouch = true, MaxPairTests = limit, ProposePastTheLimit = true };
                ro.Apply();
                var rc = new MeshRoundingRepair(checkedMesh) { SharedPositionsTouch = true, MaxPairTests = limit };
                MeshRoundingRepair.CheckOnThisThread = true;
                try {
                    rc.Apply();
                } finally {
                    MeshRoundingRepair.CheckOnThisThread = false;
                }
                Assert.Equal(ro.EvaluationLimitReached, rs.EvaluationLimitReached);
                Assert.Equal(Print(on), Print(stop));
                Assert.Equal(Print(checkedMesh), Print(stop));
                Assert.Equal(ro.Moves + ro.Flips + ro.Collapses + ro.Zips + ro.VertexRemovals, rs.Moves + rs.Flips + rs.Collapses + rs.Zips + rs.VertexRemovals);
                Assert.Equal(ro.RemainingIntersections, rs.RemainingIntersections);
                Assert.Equal(ro.RemainingDegenerate, rs.RemainingDegenerate);
                Assert.Equal(ro.RemainingFalseContacts, rs.RemainingFalseContacts);
                Assert.Equal(ro.IsClean, rs.IsClean);
                Assert.Equal(BitConverter.DoubleToInt64Bits(ro.MaxDrift), BitConverter.DoubleToInt64Bits(rs.MaxDrift));
                // no proposal after the limit with the stop; the same value where the limit is not reached
                Assert.Equal(0, rs.ProposalsPastTheLimit);
                Assert.True(rs.SmallestRejectedDrift >= ro.SmallestRejectedDrift);
                Assert.Equal(BitConverter.DoubleToInt64Bits(rc.SmallestRejectedDrift), BitConverter.DoubleToInt64Bits(rs.SmallestRejectedDrift));
                if (rs.EvaluationLimitReached)
                    reached++;
                else
                    Assert.Equal(BitConverter.DoubleToInt64Bits(ro.SmallestRejectedDrift), BitConverter.DoubleToInt64Bits(rs.SmallestRejectedDrift));
                output.WriteLine($"{name} limit {limit}: reached {rs.EvaluationLimitReached}, proposals past it without the stop {ro.ProposalsPastTheLimit}, smallest rejected {rs.SmallestRejectedDrift:R} (proposing on: {ro.SmallestRejectedDrift:R}), bound {rs.MaxLength:R}");
                if (name == "utm Cluster 0 9 1" && limit == 5000) {
                    // the documented narrowing: here the smallest rejected drift of a repair that proposes on comes from a
                    // move after the limit, so with the stop the value is larger; and the stop saves those proposals
                    Assert.True(rs.EvaluationLimitReached && ro.ProposalsPastTheLimit > 0);
                    Assert.True(rs.SmallestRejectedDrift > ro.SmallestRejectedDrift);
                }
            }
            if (name.Contains("Cluster"))
                Assert.True(reached > 0, "the limit is never reached");
        }

        [Fact]
        public void AtTheLimit_Invariant_Deterministic()
        {
            // a repair that stops at the limit: the 2^k scales give the same mesh bit for bit (and the same scaled
            // SmallestRejectedDrift), every similarity the same counts of what is left; serial equals parallel, repeated
            DMesh3 m = SafeRoundingDegenerateInputTests.Make(Family.Cluster, 36, 1);
            Func<DMesh3, bool, MeshRoundingRepair> repair = (mesh, serial) => {
                var r = new MeshRoundingRepair(mesh) { SharedPositionsTouch = true, MaxPairTests = 2000 };
                bool was = gParallel.SerialOnThisThread;
                gParallel.SerialOnThisThread = serial;
                try { r.Apply(); } finally { gParallel.SerialOnThisThread = was; }
                return r;
            };
            DMesh3 m0 = new DMesh3(m);
            MeshRoundingRepair r0 = repair(m0, false);
            Assert.True(r0.EvaluationLimitReached);
            Assert.Equal(0, r0.ProposalsPastTheLimit);
            Assert.True(Invariance.AllFinite(m0));
            foreach (Similarity sim in Similarity.All()) {
                if (sim.IsExact == false)
                    continue;
                DMesh3 ms = Invariance.Transformed(m, sim);
                MeshRoundingRepair rs = repair(ms, false);
                Assert.True(rs.EvaluationLimitReached, sim.ToString());
                Assert.True(Invariance.Difference(m0, Invariance.MappedBack(ms, sim)) == "", sim.ToString());
                Assert.Equal(r0.RemainingDegenerate, rs.RemainingDegenerate);
                Assert.Equal(r0.RemainingIntersections, rs.RemainingIntersections);
                Assert.Equal(r0.SmallestRejectedDrift / r0.MaxLength, rs.SmallestRejectedDrift / rs.MaxLength);
            }
            string expected = Print(m0);
            for (int run = 0; run < 3; ++run) {
                DMesh3 again = new DMesh3(m);
                MeshRoundingRepair ra = repair(again, run == 0);
                Assert.True(expected == Print(again), $"run {run} differs");
                Assert.Equal(BitConverter.DoubleToInt64Bits(r0.SmallestRejectedDrift), BitConverter.DoubleToInt64Bits(ra.SmallestRejectedDrift));
            }
        }

        [Fact]
        public void AtTheLimit_StrictRounding_SaysWhatTheSmallestRejectedEditIs()
        {
            // StrictRounding reports the smallest rejected edit: where the repair stopped at its limit, the message says
            // that it is the smallest of the edits proposed before it; without the limit it does not
            // (a dense cluster that the repair leaves unclean also without the limit: both runs report an error)
            DMesh3 m = SafeRoundingDegenerateInputTests.Make(Family.Cluster, 36, 3, 8);
            var limited = new ExactMeshBoolean(new DMesh3(m), new DMesh3(), ExactMeshBoolean.Operations.Union) { StrictRounding = true, RoundingMaxPairTests = 2000 };
            Assert.False(limited.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, limited.Status);
            Assert.True(limited.Rounding.EvaluationLimitReached);
            Assert.Contains("stopped at its effort limit", limited.Error);
            var full = new ExactMeshBoolean(new DMesh3(m), new DMesh3(), ExactMeshBoolean.Operations.Union) { StrictRounding = true };
            Assert.False(full.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, full.Status);
            Assert.False(full.Rounding.EvaluationLimitReached);
            Assert.DoesNotContain("effort limit", full.Error);
        }

        [Fact]
        public void PastTheLimit_ReusedInstance_EqualsFreshInstance()
        {
            // a reused instance that reached the limit before starts again: the same result as a fresh one
            var reused = new MeshRoundingRepair(Input("utm Cluster 0 4 0")) { SharedPositionsTouch = true, MaxPairTests = 5000 };
            reused.Apply();
            Assert.True(reused.EvaluationLimitReached);
            DMesh3 m = Input("utm Cluster 0 9 1"), copy = new DMesh3(m);
            reused.Mesh = m;
            reused.Apply();
            var fresh = new MeshRoundingRepair(copy) { SharedPositionsTouch = true, MaxPairTests = 5000 };
            fresh.Apply();
            Assert.True(fresh.EvaluationLimitReached);
            // (no edit was rejected by the bound before the limit here: infinity for both, see AtTheLimit_Stops_SameMeshAsProposingOn)
            Assert.Equal(BitConverter.DoubleToInt64Bits(fresh.SmallestRejectedDrift), BitConverter.DoubleToInt64Bits(reused.SmallestRejectedDrift));
            Assert.Equal(Print(copy), Print(m));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SharedByIdOnly_SameResultChecked(string name)
        {
            // without SharedPositionsTouch (shared vertices by id, no canonical ids): the incremental searches checked
            DMesh3 plain = Input(name), checkedMesh = new DMesh3(plain);
            var rp = new MeshRoundingRepair(plain);
            rp.Apply();
            var rc = new MeshRoundingRepair(checkedMesh);
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                rc.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            Assert.Equal(Print(plain), Print(checkedMesh));
            Assert.Equal(rp.RemainingIntersections, rc.RemainingIntersections);
        }
    }
}
