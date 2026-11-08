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
    /// (rounded crossings), coplanar stacks, duplicated input triangles (also at rounded coordinates), A and B touching
    /// at a corner and along a face (uncut triangles of both meshes at shared positions), and the degenerate families whose repairs edit many triangles (contractions free triangle
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
            foreach (string name in new[] { "boxes", "turned", "coplanar", "duplicate", "duplicateTurned", "corner", "face", "Sliver", "Collinear", "Cluster", "FoldedCluster" })
                yield return new object[] { name };
        }

        static (DMesh3, DMesh3, Ops) Inputs(string name)
        {
            switch (name) {
                case "boxes":
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Grid(new Vector3d(0.9, 0.3, 0.1), new Vector3d(0.6, 0.7, 0.4)), Ops.Union);
                case "turned":
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Turned(Grid(new Vector3d(0.6, 0.2, 0.1), new Vector3d(0.6, 0.5, 0.35)), new Vector3d(1, 1, 0), 20), Ops.Difference);
                case "coplanar":
                    // a coplanar stack: the top and bottom faces of both boxes lie in z = +-0.4 and overlap (output once,
                    // from one stack), cut by each other's edges
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Grid(new Vector3d(0.9, 0.3, 0), new Vector3d(0.6, 0.7, 0.4), 9), Ops.Union);
                case "duplicate":
                    // every input triangle twice: B is A, so every face is a stack of two equal triangles
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Ops.Union);
                case "duplicateTurned":
                    // duplicated triangles at turned (rounded) coordinates, and a box that cuts them
                    {
                        DMesh3 a = Turned(Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), new Vector3d(1, 2, 3), 17);
                        var both = new DMesh3(a);
                        MeshEditor.Append(both, a);
                        return (both, Grid(new Vector3d(0.8, 0.2, 0.1), new Vector3d(0.5, 0.6, 0.3)), Ops.Difference);
                    }
                case "corner":
                    // A and B touch at one corner: uncut triangles of A and of B share a position (one exact point), the
                    // other triangles are uncut too
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Grid(new Vector3d(2, 1.4, 0.8), new Vector3d(1, 0.7, 0.4)), Ops.Union);
                case "face":
                    // A and B touch along a face from outside (opposite orientations there), shifted: the touching faces
                    // cut each other, the rest is uncut, and A's and B's triangles meet at shared positions
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), Grid(new Vector3d(2, 0.3, 0.1), new Vector3d(1, 0.7, 0.4), 10), Ops.Union);
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
                // (where the solid touches itself, corner and face, its vertices share positions, which the
                // self-intersection check counts as intersecting: see ExactBooleanRoundingContactsTests)
                bool touches = checkedRun.Result.VertexIndices().GroupBy(v => checkedRun.Result.GetVertex(v)).Any(g => g.Count() > 1);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(checkedRun.Result, checkSelfIntersections: touches == false));
            }
        }

        [Fact]
        public void ApplyAgain_AfterTheResultChanged_SearchesEveryPair()
        {
            // the promise (exact vertices at their exact points) holds for the first Apply only: a second Apply on the
            // same instance, after the result was changed, must find the intersections a fresh repair finds
            var box = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), EdgeVertices = 4 }.Generate().MakeDMesh();
            var boolean = new ExactMeshBoolean(box, new DMesh3(), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Rounding.SkippedExactQueries > 0);
            DMesh3 r = boolean.Result;
            // an inner vertex of the top face, pushed through the bottom
            int top = r.VertexIndices().First(v => r.GetVertex(v).z == 0.4 && Math.Abs(r.GetVertex(v).x) < 1 && Math.Abs(r.GetVertex(v).y) < 0.7);
            Vector3d p = r.GetVertex(top);
            r.SetVertex(top, new Vector3d(p.x, p.y, -0.8));
            var fresh = new MeshRoundingRepair(new DMesh3(r)) { SharedPositionsTouch = true };
            fresh.Apply();
            int expected = fresh.InitialIntersections;
            Assert.True(expected > 0);
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                boolean.Rounding.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            Assert.Equal(expected, boolean.Rounding.InitialIntersections);
            Assert.Equal(0, boolean.Rounding.SkippedExactQueries);
        }

        [Fact]
        public void WithoutSharedPositionsTouch_NoSkip()
        {
            // the boxes touch at a corner: the result keeps two vertices of that exact point at one position. Without
            // SharedPositionsTouch the full search reports their triangles as intersecting, which the lemma does not
            // cover: the exact flags are not used then (in check mode a skip would throw)
            var boolean = new ExactMeshBoolean(Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4), 4), Grid(new Vector3d(2, 1.4, 0.8), new Vector3d(1, 0.7, 0.4), 4), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 r = boolean.Result;
            Assert.True(r.VertexIndices().GroupBy(v => r.GetVertex(v)).Any(g => g.Count() > 1), "no contact");
            var plain = new MeshRoundingRepair(new DMesh3(r)) { MaxPasses = 0 };
            plain.Apply();
            Assert.True(plain.InitialIntersections > 0, "the corner is not reported");
            var repair = new MeshRoundingRepair(new DMesh3(r)) { MaxPasses = 0, ExactVertexF = (v) => true };
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                repair.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            Assert.Equal(plain.InitialIntersections, repair.InitialIntersections);
            Assert.Equal(0, repair.SkippedExactQueries);
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
