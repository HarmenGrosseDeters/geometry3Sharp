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
    /// SafeRounding's full self-intersection search skips the pairs of two triangles that are exact triangles of the
    /// arrangement (three input vertices, unchanged since the start: docs/design/ROUNDING_EXACT_PAIRS.md). With
    /// MeshRoundingRepair.CheckOnThisThread every search, with the skip, is compared with a full search that tests every
    /// pair (it throws if they differ), so the cases here check the lemma: boxes with cut and uncut faces, a turned box
    /// (rounded crossings), and the degenerate families whose repairs edit many triangles (contractions free triangle
    /// ids that later edits reuse, and searches run again in full after many changes).
    /// </summary>
    public class RoundingExactPairsTests
    {
        static DMesh3 Grid(Vector3d center, Vector3d half, int n = 12)
        {
            return new GridBox3Generator() { Box = new Box3d(center, half), EdgeVertices = n }.Generate().MakeDMesh();
        }

        static DMesh3 Turned(DMesh3 m, Vector3d axis, double degrees)
        {
            MeshTransforms.Rotate(m, Vector3d.Zero, Quaterniond.AxisAngleD(axis.Normalized, degrees));
            return m;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string name in new[] { "boxes", "turned", "Sliver", "Collinear", "Cluster", "FoldedCluster" })
                yield return new object[] { name };
        }

        static (DMesh3, DMesh3, Ops) Inputs(string name)
        {
            switch (name) {
                case "boxes":
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Grid(new Vector3d(0.9, 0.3, 0.1), new Vector3d(0.6, 0.7, 0.4)), Ops.Union);
                case "turned":
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Turned(Grid(new Vector3d(0.6, 0.2, 0.1), new Vector3d(0.6, 0.5, 0.35)), new Vector3d(1, 1, 0), 20), Ops.Difference);
            }
            Family f = (Family)Enum.Parse(typeof(Family), name);
            return (SafeRoundingDegenerateInputTests.Make(f, f == Family.Cluster || f == Family.FoldedCluster ? 36 : 30, 1), new DMesh3(), Ops.Union);
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool check = false, bool serial = false)
        {
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean;
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
        public void SkippingSearch_SameAsFull_AndSkips(string name)
        {
            // the check throws if a search with the skip finds other pairs than one testing every pair; the result is
            // the one without the check
            (DMesh3 a, DMesh3 b, Ops op) = Inputs(name);
            ExactMeshBoolean plain = Run(a, b, op);
            ExactMeshBoolean checkedRun = Run(a, b, op, check: true);
            Assert.Equal(Print(plain.Result), Print(checkedRun.Result));
            Assert.True(checkedRun.Rounding.SkippedExactQueries > 0, $"{name}: nothing skipped");
            Assert.True(checkedRun.Result.IsClosed() && Invariance.AllFinite(checkedRun.Result), name);
            if (name != "FoldedCluster") {
                // (folded clusters are left unclean, see SafeRoundingDegenerateInputTests)
                Assert.True(checkedRun.RoundingClean, name);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(checkedRun.Result));
            }
        }

        [Fact]
        public void Edits_ReuseIdsAndSearchAgain_StillTheSame()
        {
            // the families repair by contractions, flips and zips: ids are freed and reused, and over all of them searches
            // run again in full after many changes; all checked against a full search
            int full = 0, edits = 0;
            foreach (string name in new[] { "Sliver", "Collinear", "Cluster", "FoldedCluster" })
                foreach (int seed in new[] { 1, 2, 3 }) {
                    Family f = (Family)Enum.Parse(typeof(Family), name);
                    DMesh3 m = SafeRoundingDegenerateInputTests.Make(f, f == Family.Cluster || f == Family.FoldedCluster ? 36 : 30, seed);
                    ExactMeshBoolean b = Run(m, new DMesh3(), Ops.Union, check: true);
                    full += b.Rounding.FullPairSearches;
                    edits += b.Rounding.Collapses + b.Rounding.Flips + b.Rounding.Zips;
                }
            Assert.True(edits > 0, "no edits");
            Assert.True(full > 12, $"no search ran again in full ({full} full searches in 12 runs)");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Similarities_SameResult_Pow2BitIdentical(string name)
        {
            (DMesh3 a, DMesh3 b, Ops op) = Inputs(name);
            ExactMeshBoolean b0 = Run(a, b, op, check: true);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), op, check: true);
                Assert.True(bs.Result.IsClosed() && Invariance.AllFinite(bs.Result), $"{name} {sim}");
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", $"{name} {sim}");
                    Assert.Equal(b0.Rounding.SkippedExactQueries, bs.Rounding.SkippedExactQueries);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SerialEqualsParallel_ComputeTwice(string name)
        {
            (DMesh3 a, DMesh3 b, Ops op) = Inputs(name);
            string serial = Print(Run(a, b, op, serial: true).Result);
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == Print(Run(a, b, op).Result), $"{name}: parallel run {run} differs from the serial run");
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(serial, Print(boolean.Result));
        }
    }
}
