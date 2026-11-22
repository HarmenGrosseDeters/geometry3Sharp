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
    /// The first pair search of SafeRounding's repair queries a tree of the new triangles only (intersecting_pairs_of_new), and the
    /// tree of all triangles is built at the first edit, not at the start (docs/research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md, 1a step 1).
    /// The result must be bit for bit the one with the tree of all triangles first (MeshRoundingRepair.TreeFirstSearchOnThisThread),
    /// with the same counts. The skipped exact triangles are counted per block, not by a counter the threads share (lever 3x).
    /// The tube pairs are the seam inputs of benchmarks/BooleanComparison/SeamLevers.cs, smaller.
    /// </summary>
    public class RoundingLazyFirstSearchTests
    {
        // (SeamLevers.Tube)
        static DMesh3 Tube(int k, double half, double radius)
        {
            double h = 2 * Math.PI * radius / k;
            int rings = Math.Max(2, (int)Math.Ceiling(2 * half / h));
            var m = new DMesh3();
            for (int i = 0; i <= rings; ++i) {
                double x = -half + 2 * half * i / rings;
                for (int j = 0; j < k; ++j) {
                    double phi = 2 * Math.PI * j / k;
                    double r = radius * (1 + 0.06 * Math.Sin(1.3 * x + 0.5) * Math.Cos(3 * phi + 0.2) + 0.04 * Math.Cos(0.7 * x + 2 * phi));
                    m.AppendVertex(new Vector3d(x, r * Math.Cos(phi), r * Math.Sin(phi)));
                }
            }
            for (int i = 0; i < rings; ++i)
                for (int j = 0; j < k; ++j) {
                    int a = i * k + j, b = (i + 1) * k + j, c = (i + 1) * k + (j + 1) % k, d = i * k + (j + 1) % k;
                    m.AppendTriangle(a, c, b);
                    m.AppendTriangle(a, d, c);
                }
            int c0 = m.AppendVertex(new Vector3d(-half, 0, 0)), c1 = m.AppendVertex(new Vector3d(half, 0, 0));
            for (int j = 0; j < k; ++j) {
                m.AppendTriangle(c0, (j + 1) % k, j);
                m.AppendTriangle(c1, rings * k + j, rings * k + (j + 1) % k);
            }
            return m;
        }

        // (SeamLevers.Pair)
        static (DMesh3, DMesh3) TubePair(int k, double half)
        {
            DMesh3 a = Tube(k, half, 1.0), b = Tube(k, half, 0.8);
            MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.8).Normalized, 73));
            MeshTransforms.Translate(b, new Vector3d(0.11, 0.23, 0.37));
            return (a, b);
        }

        public static IEnumerable<object[]> TubeCases()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Difference, Ops.Intersection })
                foreach (double half in new[] { 3.0, 8.0 })
                    yield return new object[] { op, half };
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool treeFirst = false, bool check = false, bool serial = false)
        {
            MeshRoundingRepair.TreeFirstSearchOnThisThread = treeFirst;
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean;
            } finally {
                MeshRoundingRepair.TreeFirstSearchOnThisThread = false;
                MeshRoundingRepair.CheckOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
        }

        // the result bit for bit (ids included) and the counts of the repair that do not depend on how the first search ran (the
        // numbers of full and incremental searches do: the tree of all triangles is built later, its size sets that choice)
        static string Fingerprint(ExactMeshBoolean b)
        {
            MeshRoundingRepair r = b.Rounding;
            return RoundingExactPairsTests.Print(b.Result) + $"|skipped {r.SkippedExactQueries} pairs {r.InitialIntersections} bad {r.InitialDegenerate} contacts {r.InitialFalseContacts}"
                + $" left {r.RemainingIntersections} {r.RemainingDegenerate} {r.RemainingFalseContacts} edits {r.Collapses} {r.Flips} {r.Zips} removed {r.RemovedTriangles}";
        }

        [Theory]
        [MemberData(nameof(TubeCases))]
        public void Tubes_SameAsTheTreeFirst_SameCounts(Ops op, double half)
        {
            (DMesh3 a, DMesh3 b) = TubePair(24, half);
            ExactMeshBoolean lazy = Run(a, b, op);
            ExactMeshBoolean tree = Run(a, b, op, treeFirst: true);
            Assert.Equal(Fingerprint(tree), Fingerprint(lazy));
            Assert.True(lazy.Rounding.SkippedExactQueries > 0, "nothing skipped");
            // (the result of an intersection is all new triangles: the tree of all is the way there)
            Assert.Equal(op == Ops.Intersection ? 0 : 1, lazy.Rounding.SearchesOfNew);
            Assert.Equal(0, tree.Rounding.SearchesOfNew);
            if (op != Ops.Intersection)
                Assert.True(lazy.Rounding.TreeBuilds < tree.Rounding.TreeBuilds || lazy.Rounding.TreeBuilds == 0, "the tree was built at the start");
            Assert.True(lazy.Result.IsClosed() && Invariance.AllFinite(lazy.Result));
        }

        [Fact]
        public void Union_WithoutEdits_BuildsNoTree()
        {
            (DMesh3 a, DMesh3 b) = TubePair(24, 8);
            ExactMeshBoolean lazy = Run(a, b, Ops.Union);
            Assert.True(lazy.Rounding.Collapses + lazy.Rounding.Flips + lazy.Rounding.Zips == 0, "the case has edits");
            Assert.Equal(0, lazy.Rounding.TreeBuilds);
            Assert.Equal(1, Run(a, b, Ops.Union, treeFirst: true).Rounding.TreeBuilds);
        }

        [Fact]
        public void Tubes_CheckedAgainstAFullSearch()
        {
            // (the check throws if the search of the new triangles finds other pairs than one that tests every pair)
            (DMesh3 a, DMesh3 b) = TubePair(24, 3);
            ExactMeshBoolean plain = Run(a, b, Ops.Union);
            ExactMeshBoolean checkedRun = Run(a, b, Ops.Union, check: true);
            Assert.Equal(Fingerprint(plain), Fingerprint(checkedRun));
            Assert.Equal(1, checkedRun.Rounding.SearchesOfNew);
        }

        public static IEnumerable<object[]> BoxCases() => RoundingExactPairsTests.CasesExceptFoldedCluster();

        [Theory]
        [MemberData(nameof(BoxCases))]
        public void TheCasesOfTheExactPairs_SameAsTheTreeFirst(string name)
        {
            // (boxes cut and uncut, coplanar, duplicated and touching, and the degenerate families whose repairs edit many triangles)
            (DMesh3 a, DMesh3 b, Ops op) = RoundingExactPairsTests.Inputs(name);
            ExactMeshBoolean lazy = Run(a, b, op);
            ExactMeshBoolean tree = Run(a, b, op, treeFirst: true);
            Assert.Equal(Fingerprint(tree), Fingerprint(lazy));
        }

        [Theory]
        [InlineData(Family.Collinear)]
        [InlineData(Family.Sliver)]
        [InlineData(Family.Coincident)]
        public void DegenerateExactTriangles_BesideTheCut_SameAsTheTreeFirst(Family family)
        {
            // (a degenerate triangle of the input stays an exact triangle of the result: the new triangles of the cut next to it
            // are not tested against it, as in the search with the tree)
            DMesh3 a = SafeRoundingDegenerateInputTests.Make(family, 30, 1);
            DMesh3 b = new GridBox3Generator() { Box = new Box3d(a.CachedBounds.Center + new Vector3d(0.3, 0.1, 0.0), new Vector3d(0.5, 0.5, 0.5) * a.CachedBounds.MaxDim * 0.5), EdgeVertices = 6 }.Generate().MakeDMesh();
            ExactMeshBoolean lazy = Run(a, b, Ops.Union);
            ExactMeshBoolean tree = Run(a, b, Ops.Union, treeFirst: true);
            Assert.Equal(Fingerprint(tree), Fingerprint(lazy));
            Assert.Equal(1, lazy.Rounding.SearchesOfNew);
        }

        [Fact]
        public void ManyNewTriangles_UseTheTree()
        {
            // a coarse box cut through by a turned one: most triangles of the result are new, the tree of all is the better way
            DMesh3 a = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), EdgeVertices = 3 }.Generate().MakeDMesh();
            DMesh3 b = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.3, 0.1, 0.05), new Vector3d(0.6, 0.5, 0.35)), EdgeVertices = 3 }.Generate().MakeDMesh();
            MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            ExactMeshBoolean lazy = Run(a, b, Ops.Difference);
            ExactMeshBoolean tree = Run(a, b, Ops.Difference, treeFirst: true);
            Assert.Equal(0, lazy.Rounding.SearchesOfNew);
            Assert.Equal(Fingerprint(tree), Fingerprint(lazy));
        }

        [Fact]
        public void Tubes_SerialEqualsParallel_RepeatedRuns_ComputeTwice()
        {
            (DMesh3 a, DMesh3 b) = TubePair(24, 3);
            string serial = Fingerprint(Run(a, b, Ops.Union, serial: true));
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == Fingerprint(Run(a, b, Ops.Union)), $"parallel run {run} differs from the serial run");
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(serial, Fingerprint(boolean));
        }

        [Fact]
        public void Tubes_SameResultUnderTheSimilarities_Pow2BitIdentical()
        {
            (DMesh3 a, DMesh3 b) = TubePair(16, 2);
            ExactMeshBoolean b0 = Run(a, b, Ops.Union);
            Assert.True(b0.Rounding.SkippedExactQueries > 0);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), Ops.Union);
                Assert.True(bs.Result.IsClosed() && Invariance.AllFinite(bs.Result), sim.ToString());
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
                    Assert.Equal(b0.Rounding.SkippedExactQueries, bs.Rounding.SkippedExactQueries);
                    Assert.Equal(b0.Rounding.InitialIntersections, bs.Rounding.InitialIntersections);
                }
            }
        }
    }
}
