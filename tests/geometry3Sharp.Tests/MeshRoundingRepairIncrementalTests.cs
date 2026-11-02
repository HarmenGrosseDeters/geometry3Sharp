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
            foreach (Family f in new[] { Family.Sliver, Family.ShortEdge, Family.Coincident, Family.Collinear, Family.Cluster, Family.FoldedCluster })
                yield return new object[] { f.ToString() };
        }

        static Func<ExactMeshBoolean> Case(string name)
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

        [Theory]
        [MemberData(nameof(Cases))]
        public void Checked_SameResultAsUnchecked(string name)
        {
            // the checks throw if an incremental search or a canonical id differs; the result is the one without them
            ExactMeshBoolean plain = Run(Case(name));
            ExactMeshBoolean checkedRun = Run(Case(name), check: true);
            Assert.Equal(Print(plain.Result), Print(checkedRun.Result));
            Assert.Equal(plain.RoundingClean, checkedRun.RoundingClean);
            MeshRoundingRepair r = checkedRun.Rounding;
            Assert.NotNull(r);
            Assert.True(r.FullPairSearches >= 1);
        }

        [Fact]
        public void Searches_IncrementalAfterEdits()
        {
            // the repairs edit (and search again): over the cases, searches of the changed triangles and searches
            // skipped because nothing changed both happen, and every result is clean
            int incremental = 0, unchanged = 0;
            foreach (object[] c in Cases()) {
                ExactMeshBoolean b = Run(Case((string)c[0]), check: true);
                MeshRoundingRepair r = b.Rounding;
                incremental += r.IncrementalPairSearches;
                unchanged += r.UnchangedPairSearches;
                // (folded clusters are left unclean, see SafeRoundingDegenerateInputTests)
                if (((string)c[0]).Contains("FoldedCluster") == false) {
                    Assert.True(r.IsClean, (string)c[0]);
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
                }
            }
            Assert.True(incremental > 0, "no incremental search");
            Assert.True(unchanged > 0, "no search skipped");
        }

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
    }
}
