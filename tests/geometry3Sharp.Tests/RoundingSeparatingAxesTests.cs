using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding's full pair search with the separating axes of TriangleSeparatingAxes in front of the pair test
    /// (MeshRoundingRepair.intersecting_pairs_all): the pairs it reports, and so the result, are those of the search
    /// that tests every box pair (MeshRoundingRepair.NoSeparatingAxesOnThisThread, the reference). With
    /// MeshRoundingRepair.CheckOnThisThread every pair the axes drop is tested exactly (it throws if it intersects) and
    /// the search is compared with the full one. The filter itself: TriangleSeparatingAxesTests.
    /// </summary>
    public class RoundingSeparatingAxesTests
    {
        // a prism over a gear outline (4 corners per tooth: long thin side triangles) and its copy, rotated and shifted:
        // the rounded result has thin triangles on tilted faces, hundreds of box neighbours per query
        static (DMesh3 a, DMesh3 b) Gears(int teeth)
        {
            int n = 4 * teeth;
            var a = new DMesh3();
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < n; ++i) {
                    double t = 2 * Math.PI * i / n, r = (i % 4) < 2 ? 0.8 : 1.0;
                    a.AppendVertex(new Vector3d(r * Math.Cos(t), r * Math.Sin(t), 2 * k));
                }
            int c0 = a.AppendVertex(new Vector3d(0, 0, 0)), c1 = a.AppendVertex(new Vector3d(0, 0, 2));
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                a.AppendTriangle(c0, j, i); a.AppendTriangle(c1, n + i, n + j);
                a.AppendTriangle(i, j, n + j); a.AppendTriangle(i, n + j, n + i);
            }
            var b = new DMesh3(a);
            MeshTransforms.Rotate(b, new Vector3d(0, 0, 1), Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(b, new Vector3d(0.5, 0.5, 0.5));
            return (a, b);
        }

        static Func<ExactMeshBoolean> Case(string name)
        {
            string[] w = name.Split(' ');
            if (w[0] == "gear") {
                var (a, b) = Gears(int.Parse(w[1]));
                Ops op = (Ops)Enum.Parse(typeof(Ops), w[2]);
                return () => new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            }
            if (w[0] == "plate")
                return RoundingDirtyTreeTests.PlateWithHoles(int.Parse(w[1]), (Ops)Enum.Parse(typeof(Ops), w[2]));
            if (w[0] == "slivers") {
                DMesh3 m = RoundingDirtyTreeTests.SliverBox(int.Parse(w[1]), int.Parse(w[2]));
                return () => new ExactMeshBoolean(new DMesh3(m), new DMesh3(), Ops.Union);
            }
            return MeshRoundingRepairIncrementalTests.Case(name);
        }

        static ExactMeshBoolean Run(Func<ExactMeshBoolean> make, bool axes = true, bool check = false, bool serial = false)
        {
            MeshRoundingRepair.NoSeparatingAxesOnThisThread = axes == false;
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                ExactMeshBoolean b = make();
                Assert.True(b.Compute(), b.Error);
                return b;
            } finally {
                MeshRoundingRepair.NoSeparatingAxesOnThisThread = false;
                MeshRoundingRepair.CheckOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
        }

        static string Print(DMesh3 m) => RoundingDirtyTreeTests.Print(m);

        [Theory]
        [InlineData("gear 40 Union")]
        [InlineData("gear 40 Intersection")]
        [InlineData("gear 40 Difference")]
        [InlineData("plate 8 Union")]
        [InlineData("plate 8 Difference")]
        [InlineData("slivers 16 7")]
        [InlineData("utm Cluster 0 4 0")]
        [InlineData("Cluster")]
        [InlineData("FoldedCluster")]
        public void FullSearch_SameResultAsTestingEveryBoxPair(string name)
        {
            Func<ExactMeshBoolean> make = Case(name);
            ExactMeshBoolean with = Run(make), reference = Run(make, axes: false);
            Assert.Equal(0, reference.Rounding.AxisApartPairs);
            Assert.True(Print(with.Result) == Print(reference.Result), $"{name}: the result differs from the one without the axes");
            Assert.Equal(reference.RoundingClean, with.RoundingClean);
            Assert.Equal((reference.Rounding.InitialIntersections, reference.Rounding.InitialDegenerate, reference.Rounding.RemainingIntersections),
                (with.Rounding.InitialIntersections, with.Rounding.InitialDegenerate, with.Rounding.RemainingIntersections));
            Assert.Equal((reference.Rounding.Collapses, reference.Rounding.Flips, reference.Rounding.Moves, reference.Rounding.Zips, reference.Rounding.MaxDrift),
                (with.Rounding.Collapses, with.Rounding.Flips, with.Rounding.Moves, with.Rounding.Zips, with.Rounding.MaxDrift));
            Assert.True(with.Result.TriangleCount > 0 && Invariance.AllFinite(with.Result), name);
            // (closed, and what the independent check says is what it says without the axes; "plate 8 Difference" is
            // RoundingClean with touching positions, which NotCleanReason counts as self-intersections, on both paths)
            Assert.True(with.Result.IsClosed(), name);
            Assert.Equal(MeshRobustRepair.NotCleanReason(reference.Result), MeshRobustRepair.NotCleanReason(with.Result));
            if (name.StartsWith("gear"))
                Assert.Equal("", MeshRobustRepair.NotCleanReason(with.Result));
        }

        [Theory]
        [InlineData("gear 40 Union", 20000)]
        [InlineData("gear 40 Intersection", 20000)]
        [InlineData("plate 8 Union", 100)]
        public void ThinTriangles_TheAxesTakeMostBoxPairsFromThePairTest(string name, long atLeast)
        {
            ExactMeshBoolean with = Run(Case(name));
            Assert.True(with.Rounding.AxisApartPairs >= atLeast, $"{name}: {with.Rounding.AxisApartPairs} pairs apart");
        }

        [Theory]
        [InlineData("gear 24 Union")]
        [InlineData("gear 24 Difference")]
        [InlineData("plate 6 Union")]
        public void CheckMode_EveryDroppedPairIsTestedExactly_CountsAreThoseWithoutTheAxes(string name)
        {
            Func<ExactMeshBoolean> make = Case(name);
            // (the check throws if a dropped pair intersects, or if the search differs from the one over every pair)
            ExactMeshBoolean with = Run(make, check: true), reference = Run(make, axes: false, check: true), plain = Run(make);
            Assert.True(with.Rounding.AxisApartPairs > 0, $"{name}: no pair apart");
            Assert.Equal(plain.Rounding.AxisApartPairs, with.Rounding.AxisApartPairs);
            Assert.Equal(reference.Rounding.PairTestsChecked, with.Rounding.PairTestsChecked);
            Assert.Equal(reference.Rounding.FilterDecisions, with.Rounding.FilterDecisions);
            Assert.True(Print(with.Result) == Print(reference.Result) && Print(with.Result) == Print(plain.Result), name);
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Difference)]
        public void ApartPairs_AreExactlyTheBoxPairsApartByTheAxesOfEitherTriangle(Ops op)
        {
            // the rounded, unrepaired result of a boolean, repaired directly (no exact vertices: every box pair of two
            // triangles is a candidate once, from the triangle with the smaller id)
            var (a, b) = Gears(20);
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = false };
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 m = boolean.Result;
            // counted here over all pairs, with the axes of each triangle on its own
            int[] tids = m.TriangleIndices().ToArray();
            var tri = new Triangle3d[tids.Length]; var box = new AxisAlignedBox3d[tids.Length]; var degenerate = new bool[tids.Length];
            var data = new double[TriangleSeparatingAxes.Stride * tids.Length];
            for (int i = 0; i < tids.Length; ++i) {
                Index3i tv = m.GetTriangle(tids[i]);
                tri[i] = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
                box[i] = new AxisAlignedBox3d(tri[i].V0); box[i].Contain(tri[i].V1); box[i].Contain(tri[i].V2);
                degenerate[i] = ExactTriangleIntersection.IsDegenerate(ref tri[i]);
                TriangleSeparatingAxes.Compute(tri[i].V0, tri[i].V1, tri[i].V2, data, TriangleSeparatingAxes.Stride * i);
            }
            long expected = 0, byOwnOnly = 0, byOtherOnly = 0;
            for (int i = 0; i < tids.Length; ++i) {
                if (degenerate[i]) continue;
                for (int j = i + 1; j < tids.Length; ++j) {
                    AxisAlignedBox3d x = box[i], y = box[j];
                    if (x.Max.x < y.Min.x || x.Min.x > y.Max.x || x.Max.y < y.Min.y || x.Min.y > y.Max.y || x.Max.z < y.Min.z || x.Min.z > y.Max.z)
                        continue;
                    bool own = TriangleSeparatingAxes.Apart(data, TriangleSeparatingAxes.Stride * i, tri[j].V0, tri[j].V1, tri[j].V2);
                    bool other = TriangleSeparatingAxes.Apart(data, TriangleSeparatingAxes.Stride * j, tri[i].V0, tri[i].V1, tri[i].V2);
                    if (own || other) expected++;
                    if (own && other == false) byOwnOnly++;
                    if (other && own == false) byOtherOnly++;
                }
            }
            // (both triangles' axes are needed: each drops pairs the other does not)
            Assert.True(byOwnOnly > 100 && byOtherOnly > 100, $"{byOwnOnly} pairs apart by the first triangle's axes only, {byOtherOnly} by the second's");
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
            r.Apply();
            Assert.Equal(1, r.FullPairSearches);
            Assert.Equal(expected, r.AxisApartPairs);
        }

        [Fact]
        public void SerialEqualsParallel_Repeated()
        {
            Func<ExactMeshBoolean> make = Case("gear 40 Union");
            ExactMeshBoolean serial = Run(make, serial: true);
            string s = Print(serial.Result);
            Assert.True(serial.Rounding.AxisApartPairs > 0);
            for (int run = 0; run < 3; ++run) {
                ExactMeshBoolean p = Run(make);
                Assert.True(s == Print(p.Result), $"parallel run {run} differs from the serial run");
                Assert.Equal(serial.Rounding.AxisApartPairs, p.Rounding.AxisApartPairs);
            }
        }

        [Fact]
        public void SecondApplyOnOneInstance_SameAsAFreshInstance()
        {
            // the rounded, unrepaired results of two booleans, repaired by one instance in turn
            Func<Ops, DMesh3> rounded = (op) => {
                var (a, b) = Gears(24);
                var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = false };
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean.Result;
            };
            DMesh3 first = rounded(Ops.Union), second = rounded(Ops.Difference), fresh = new DMesh3(second);
            var r = new MeshRoundingRepair(first) { SharedPositionsTouch = true };
            r.Apply();
            long apartFirst = r.AxisApartPairs;
            Assert.True(apartFirst > 0);
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                r.Mesh = second;
                r.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            var f = new MeshRoundingRepair(fresh) { SharedPositionsTouch = true };
            f.Apply();
            Assert.Equal(f.AxisApartPairs, r.AxisApartPairs);
            Assert.True(r.AxisApartPairs > 0 && r.AxisApartPairs != apartFirst, "the count is that of the second Apply");
            Assert.Equal(Print(fresh), Print(second));
        }

        // the frames in 4 rows, as RoundingDirtyTreeTests
        static readonly int[][] FrameRows = { new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };

        public static IEnumerable<object[]> FrameRowIndexes() => Enumerable.Range(0, FrameRows.Length).Select(r => new object[] { r });

        [Fact]
        public void FrameRows_AreAllTheFramesOnce() =>
            Assert.Equal(Enumerable.Range(0, Similarity.All().Count()), FrameRows.SelectMany(r => r).OrderBy(i => i));

        [Theory]
        [MemberData(nameof(FrameRowIndexes))]
        public void Gears_Similarities_Pow2BitIdentical(int row)
        {
            var (a, b) = Gears(24);
            ExactMeshBoolean b0 = Run(() => new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Union));
            Assert.True(b0.Rounding.AxisApartPairs > 0);
            Similarity[] all = Similarity.All().ToArray();
            foreach (Similarity sim in FrameRows[row].Select(i => all[i])) {
                DMesh3 sa = Invariance.Transformed(a, sim), sb = Invariance.Transformed(b, sim);
                ExactMeshBoolean with = Run(() => new ExactMeshBoolean(new DMesh3(sa), new DMesh3(sb), Ops.Union), check: sim.IsExact == false);
                ExactMeshBoolean reference = Run(() => new ExactMeshBoolean(new DMesh3(sa), new DMesh3(sb), Ops.Union), axes: false);
                // in every frame: the same result as without the axes, and the axes at work
                Assert.True(Print(with.Result) == Print(reference.Result), sim.ToString());
                Assert.True(with.Rounding.AxisApartPairs > 0, sim.ToString());
                Assert.True(with.Result.IsClosed() && Invariance.AllFinite(with.Result), sim.ToString());
                // a power-of-two scale: the same result and the same pairs apart, bit for bit
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(with.Result, sim)) == "", sim.ToString());
                    Assert.Equal(b0.Rounding.AxisApartPairs, with.Rounding.AxisApartPairs);
                }
            }
        }
    }
}
