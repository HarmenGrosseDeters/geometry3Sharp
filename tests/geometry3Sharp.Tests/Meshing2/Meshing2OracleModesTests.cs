using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's fast path (floating-point filters, spatial indexes, the indexed global Delaunay pass) against its reference path
    /// (every test exact, OracleExpect.Reference): the same verdicts on the catalogue, on corrupted meshes, and on the self-checks.
    /// </summary>
    public class Meshing2OracleModesTests
    {
        static readonly List<CatalogueRow> Rows = Catalogue.Rows()
            .Where(r => r.State == CaseState.Covered && r.Test == null && r.Case != null && !r.Slow && CandidateRegistry.For(r.Case.Milestone) != null).ToList();

        // The rows that take 5 s or more in CatalogueRow / CorruptedRow (measured on net10.0, 4 cores) run in classes of their own, tagged
        // Slow (the draft tier of CI skips those) and in three groups that run in parallel. RowIds() is every other row; the groups plus
        // RowIds() are exactly the old set (RowGroups_PartitionTheCoveredRows).
        static readonly string[][] SlowRowGroups = {
            new[] { "F-39", "F-4/b", "F-24/a" },
            new[] { "F-42", "F-38/d", "F-34" },
            new[] { "F-24/b", "F-37", "F-12/b", "F-4/a", "F-5" },
        };

        // The rows of these two tests that take more than 10 s (the exact reference path is the check: F-39 alone is 7.5 s per
        // Oracle.Check on 1188 triangles) run in the scheduled tier, Category=Nightly (CLAUDE.md, Test time; .github/workflows/nightly.yml),
        // and not in PR or master CI. The sets are the rows measured above 10 s; the other slow rows stay in the Slow classes.
        static readonly string[] NightlyCatalogueRows = { "F-39", "F-42", "F-24/b", "F-37" };
        static readonly string[] NightlyCorruptedRows = { "F-39", "F-42", "F-24/b", "F-37", "F-4/b", "F-38/d" };

        static string[] NightlyRows(bool corrupted) => corrupted ? NightlyCorruptedRows : NightlyCatalogueRows;

        public static IEnumerable<object[]> RowIds() => Rows.Where(r => SlowRowGroups.All(g => !g.Contains(r.Id))).Select(r => new object[] { r.Id });

        /// <summary> the Slow group's rows of the corrupted-rows (true) or the catalogue-rows (false) test, without its Nightly rows </summary>
        public static IEnumerable<object[]> SlowRowIds(int group, bool corrupted) =>
            SlowRowGroups[group].Where(id => !NightlyRows(corrupted).Contains(id)).Select(id => new object[] { id });

        public static IEnumerable<object[]> NightlyRowIds(bool corrupted) => NightlyRows(corrupted).Select(id => new object[] { id });

        [Fact]
        public void RowGroups_PartitionTheCoveredRows()
        {
            foreach (bool corrupted in new[] { false, true }) {
                var ids = RowIds().Concat(Enumerable.Range(0, SlowRowGroups.Length).SelectMany(g => SlowRowIds(g, corrupted))).Concat(NightlyRowIds(corrupted))
                    .Select(a => (string)a[0]).ToList();
                Assert.Equal(Rows.Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal), ids.OrderBy(x => x, StringComparer.Ordinal));
            }
        }

        /// <summary> what a verdict consists of: the kinds with their counts, and the measured values </summary>
        static string Verdict(OracleReport r) =>
            string.Join(",", r.Violations.GroupBy(v => v.Kind).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count()))
            + " | area2 " + r.Area2 + " | minsin2 " + r.MinSin2 + " | below " + r.BelowAngle + " | ties " + r.TieEdges + " | unique " + r.Unique
            + " | constructed " + r.ConstructedVertices + " | zone " + r.MergeZoneExcused
            + " | o11 " + r.O11LocalExcused + "/" + r.O11GlobalExcused + "/" + r.O11AngleExcused;

        static (OracleReport reference, OracleReport fast) Same(Mesh2Input input, Mesh2Output output, OracleExpect e, string what)
        {
            var reference = Oracle.Check(input, output, e.WithReference());
            // The reference path runs the global empty-circumcircle pass only up to GlobalDelaunayLimit triangle-vertex pairs, by design
            // (all pairs, exactly); above it the fast path is compared without that pass too, and the pass itself is compared on the rows
            // below the limit. (Found when F-4/a, F-4/b and F-5 left the slow rows: the fast path found the global violations of a
            // corrupted F-5 and counted the merge-zone excusals of its global pass, the limited reference path neither; with no limit the
            // reference path agrees with the fast one, in 200 s instead of 0.9 s.)
            var fast = Oracle.Check(input, output, reference.GlobalDelaunayChecked ? e : e.WithGlobalDelaunayLimit(0));
            Assert.Equal(reference.GlobalDelaunayChecked, fast.GlobalDelaunayChecked);
            Assert.True(Verdict(fast) == Verdict(reference), what + ": fast " + Verdict(fast) + " / reference " + Verdict(reference));
            return (reference, fast);
        }

        // The expensive theories sit in nested classes (ExtremeScales split in two): xUnit runs classes in parallel but the rows of one class
        // one after another, and in one class they took 14 minutes (841 s on net10.0, 4 cores), the second longest stretch of the suite.
        public class CatalogueRows
        {
            [Theory]
            [MemberData(nameof(RowIds), MemberType = typeof(Meshing2OracleModesTests))]
            public void CatalogueRow_SameVerdictOnBothPaths(string id) => CatalogueRow(id);
        }

        [Trait("Category", "Slow")]
        public class CatalogueRowsSlow1
        {
            [Theory]
            [MemberData(nameof(SlowRowIds), 0, false, MemberType = typeof(Meshing2OracleModesTests))]
            public void CatalogueRow_SameVerdictOnBothPaths(string id) => CatalogueRow(id);
        }

        [Trait("Category", "Slow")]
        public class CatalogueRowsSlow2
        {
            [Theory]
            [MemberData(nameof(SlowRowIds), 1, false, MemberType = typeof(Meshing2OracleModesTests))]
            public void CatalogueRow_SameVerdictOnBothPaths(string id) => CatalogueRow(id);
        }

        [Trait("Category", "Slow")]
        public class CatalogueRowsSlow3
        {
            [Theory]
            [MemberData(nameof(SlowRowIds), 2, false, MemberType = typeof(Meshing2OracleModesTests))]
            public void CatalogueRow_SameVerdictOnBothPaths(string id) => CatalogueRow(id);
        }

        [Trait("Category", "Nightly")]
        public class CatalogueRowsNightly
        {
            [Theory]
            [MemberData(nameof(NightlyRowIds), false, MemberType = typeof(Meshing2OracleModesTests))]
            public void CatalogueRow_SameVerdictOnBothPaths(string id) => CatalogueRow(id);
        }

        static void CatalogueRow(string id)
        {
            var row = Rows.First(r => r.Id == id);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            Same(row.Case.Input, output, row.Case.Expect, id);
        }

        /// <summary>
        /// The fast path's filters in doubles hold at extreme magnitudes: the catalogue rows with small angles, holes and seeds, scaled by
        /// 2^k (exact; inputs, seeds and the mesher's output alike), give the same verdict and the same reported details on both paths
        /// (review of the oracle speed-up: a distance overflowed at 2^512, a product of squared lengths underflowed at 2^-270)
        /// </summary>
        public class ExtremeScalesSmall
        {
            [Theory]
            [InlineData("F-38/a", -270)]
            [InlineData("F-44", -270)]
            [InlineData("spike-0.84", -300)]
            public void ExtremeScales_SameVerdictAndDetailsOnBothPaths(string id, int exponent) => ExtremeScales(id, exponent);
        }

        // F-38/d (22k triangles) is the long one, once at 2^-270 and once at 2^512: a class each, so that they run in parallel
        [Trait("Category", "Nightly")]
        public class ExtremeScalesSmallF38d
        {
            [Theory]
            [InlineData("F-38/d", -270)]
            public void ExtremeScales_SameVerdictAndDetailsOnBothPaths(string id, int exponent) => ExtremeScales(id, exponent);
        }

        [Trait("Category", "Nightly")]
        public class ExtremeScalesLargeF38d
        {
            [Theory]
            [InlineData("F-38/d", 512)]
            public void ExtremeScales_SameVerdictAndDetailsOnBothPaths(string id, int exponent) => ExtremeScales(id, exponent);
        }

        public class ExtremeScalesLarge
        {
            [Theory]
            [InlineData("F-38/a", 450)]
            [InlineData("F-38/a", 512)]
            [InlineData("F-44", 512)]
            [InlineData("spike-0.84", 512)]
            public void ExtremeScales_SameVerdictAndDetailsOnBothPaths(string id, int exponent) => ExtremeScales(id, exponent);
        }

        static void ExtremeScales(string id, int exponent)
        {
            // spike-0.84: earcut's water-huge reduced to five points (QualityMesh2Tests.SpikeOf084Degrees) at 25 degrees, whose merge zone and
            // O11-unsplittable piece the oracle excuses; at 2^-300 the leg pieces' directions are below 1e-81
            Mesh2Case c;
            if (id == "spike-0.84") {
                var spike = new Mesh2Input { X = new double[] { 4097, 4097, 4098, 4183, 4050 }, Y = new double[] { 2989, 3013, 2945, 4096, 3675 }, MinAngleDeg = 25 };
                spike.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
                c = new Mesh2Case { Id = id, Milestone = Milestone.M3, Input = spike, Expect = new OracleExpect { Milestone = Milestone.M3 } };
            } else c = Catalogue.Rows().First(r => r.Id == id).Case;
            var row = new { Case = c };
            var input = TextFormat.ReadInput(TextFormat.WriteInput(row.Case.Input));
            double f = Math.Pow(2, exponent);
            for (int i = 0; i < input.Count; ++i) { input.X[i] *= f; input.Y[i] *= f; }
            foreach (var seed in input.HoleSeeds) { seed[0] *= f; seed[1] *= f; }
            Assert.Null(input.MaxArea);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            for (int i = 0; i < output.VX.Length; ++i) { output.VX[i] *= f; output.VY[i] *= f; }
            output.ExactVertices = null; output.Locate = null;   // (the candidate's location belongs to the unscaled mesh)
            var e = new OracleExpect { Milestone = row.Case.Milestone };
            // (the reports of Same are the ones to compare in detail: the same two checks on the same arguments, once instead of twice)
            var (reference, fast) = Same(input, output, e, id + " at 2^" + exponent);
            string Details(OracleReport r) => string.Join("; ", r.Violations.Select(v => v.ToString()).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(Details(reference), Details(fast));
        }

        /// <summary>
        /// The evidence for the comparison above the limit: on F-4/b and F-5 (tens of millions of triangle-vertex pairs) the reference path
        /// without its limit gives the fast path's verdict on the mesh and on every corrupted copy, the merge-zone excusals and the
        /// global violations included. Takes about half an hour, so it runs with G3_MESH2_SLOW set, like the slow catalogue rows.
        /// </summary>
        [Theory]
        [InlineData("F-4/b")]
        [InlineData("F-5")]
        public void AboveTheLimit_ReferenceWithoutLimit_SameVerdictAsFast(string id)
        {
            if (Environment.GetEnvironmentVariable("G3_MESH2_SLOW") == null) return;
            var row = Catalogue.Rows().First(r => r.Id == id);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            Assert.True((long)output.Triangles.Length * output.VX.Length > row.Case.Expect.GlobalDelaunayLimit, id + " is no longer above the limit");
            foreach (var (name, mesh) in new[] { ("valid", output) }.Concat(Corrupted(output))) {
                var fast = Oracle.Check(row.Case.Input, mesh, row.Case.Expect);
                var reference = Oracle.Check(row.Case.Input, mesh, row.Case.Expect.WithReference().WithGlobalDelaunayLimit(long.MaxValue));
                // a corrupted copy may stop both paths before the global pass; the valid mesh must reach it
                Assert.True(fast.GlobalDelaunayChecked == reference.GlobalDelaunayChecked && (fast.GlobalDelaunayChecked || name != "valid"),
                    id + " " + name + ": global pass fast " + fast.GlobalDelaunayChecked + " / reference " + reference.GlobalDelaunayChecked);
                Assert.True(Verdict(fast) == Verdict(reference), id + " " + name + ": fast " + Verdict(fast) + " / reference " + Verdict(reference));
            }
        }

        /// <summary> corruptions a mesher could produce: a vertex moved, a triangle missing, a triangle turned over, two vertices swapped </summary>
        static IEnumerable<(string, Mesh2Output)> Corrupted(Mesh2Output o)
        {
            if (o.Triangles.Length < 4) yield break;
            Mesh2Output Copy() => new Mesh2Output {
                Status = o.Status, FailureKind = o.FailureKind, VX = (double[])o.VX.Clone(), VY = (double[])o.VY.Clone(), InputToVertex = o.InputToVertex,
                Triangles = o.Triangles.Select(t => (int[])t.Clone()).ToArray(), Neighbours = null, Subsegments = o.Subsegments, TriangleLabels = o.TriangleLabels,
                BadTriangles = o.BadTriangles, SteinerLimitReached = o.SteinerLimitReached };
            double extent = Math.Max(o.VX.Max() - o.VX.Min(), o.VY.Max() - o.VY.Min());
            int mid = o.Triangles.Length / 2, v = o.Triangles[mid][0];
            var moved = Copy(); moved.VX[v] += extent * 1e-3; moved.VY[v] -= extent * 7e-4;
            yield return ("vertex moved", moved);
            var missing = Copy(); missing.Triangles = missing.Triangles.Where((t, i) => i != mid).ToArray();
            if (missing.TriangleLabels != null) missing.TriangleLabels = missing.TriangleLabels.Where((l, i) => i != mid).ToArray();
            missing.BadTriangles = null;
            yield return ("triangle missing", missing);
            var flipped = Copy(); var f = flipped.Triangles[mid]; (f[1], f[2]) = (f[2], f[1]);
            yield return ("triangle turned over", flipped);
            var swapped = Copy(); int w = o.Triangles[0][1];
            (swapped.VX[v], swapped.VX[w]) = (swapped.VX[w], swapped.VX[v]); (swapped.VY[v], swapped.VY[w]) = (swapped.VY[w], swapped.VY[v]);
            yield return ("two vertices swapped", swapped);
        }

        [Trait("Category", "Slow")]
        public class CorruptedRowsSlow1
        {
            [Theory]
            [MemberData(nameof(SlowRowIds), 0, true, MemberType = typeof(Meshing2OracleModesTests))]
            public void CorruptedRow_RejectedOnBothPaths(string id) => CorruptedRow(id);
        }

        [Trait("Category", "Slow")]
        public class CorruptedRowsSlow2
        {
            [Theory]
            [MemberData(nameof(SlowRowIds), 1, true, MemberType = typeof(Meshing2OracleModesTests))]
            public void CorruptedRow_RejectedOnBothPaths(string id) => CorruptedRow(id);
        }

        [Trait("Category", "Slow")]
        public class CorruptedRowsSlow3
        {
            [Theory]
            [MemberData(nameof(SlowRowIds), 2, true, MemberType = typeof(Meshing2OracleModesTests))]
            public void CorruptedRow_RejectedOnBothPaths(string id) => CorruptedRow(id);
        }

        [Trait("Category", "Nightly")]
        public class CorruptedRowsNightly
        {
            [Theory]
            [MemberData(nameof(NightlyRowIds), true, MemberType = typeof(Meshing2OracleModesTests))]
            public void CorruptedRow_RejectedOnBothPaths(string id) => CorruptedRow(id);
        }

        public class CorruptedRows
        {
            [Theory]
            [MemberData(nameof(RowIds), MemberType = typeof(Meshing2OracleModesTests))]
            public void CorruptedRow_RejectedOnBothPaths(string id) => CorruptedRow(id);
        }

        static void CorruptedRow(string id)
        {
            var row = Rows.First(r => r.Id == id);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            if (output.Status != Mesh2Status.Success || row.Case.Expect.ExpectFailure) return;
            foreach (var (name, bad) in Corrupted(output)) {
                Same(row.Case.Input, bad, row.Case.Expect, id + " " + name);
                // a moved vertex can stay within what the case allows (a large tube, a free point); the others always break the mesh
                if (name != "vertex moved") Assert.False(Oracle.Check(row.Case.Input, bad, row.Case.Expect).Ok, id + " " + name + " was accepted");
            }
        }

        [Fact]
        public void RingBucket_PointOnTheTopEdge_IsOnTheBoundary()
        {
            // bounds review: a ring of more than 16 vertices spanning y in [-1, 0]; the rounded bucket index of a point on the top edge
            // ran past the last bucket and the filter said outside
            var pts = new List<Pt> { Pt.FromDoubles(0, 0), Pt.FromDoubles(1, 0) };
            for (int i = 1; i <= 10; ++i) pts.Add(Pt.FromDoubles(1, -0.1 * i));
            pts.Add(Pt.FromDoubles(0, -1));
            for (int i = 9; i >= 1; --i) pts.Add(Pt.FromDoubles(0, -0.1 * i));
            var ring = pts.ToArray();
            var p = Pt.FromDoubles(0.5, 0);
            Assert.Equal(Where.Boundary, Domain.InRing(ring, p, false));
            Assert.Equal(Where.Boundary, Domain.InRing(ring, p, true));
            var inside = Pt.FromDoubles(0.5, -0.5);
            Assert.Equal(Where.Inside, Domain.InRing(ring, inside, true));
        }

        [Theory]
        [InlineData(0, 0, 1.2750220907015703e77, -3.416411395353274e76, 1.2750220907015703e77, 3.416411395353274e76)]
        [InlineData(0, 0, 1e-5, 0, 5e-6, 1.2345e-155)]
        public void SmallestAngle_AtTheEndsOfTheRange_SameVerdict(double ax, double ay, double bx, double by, double cx, double cy)
        {
            // bounds review: an overflowing product made the filtered sine 0 (a 30-75-75 triangle judged below 20 degrees); a subnormal
            // cross product gave an interval that missed the exact value
            var input = new Mesh2Input { X = new[] { ax, bx, cx }, Y = new[] { ay, by, cy }, MinAngleDeg = 20 };
            var output = new Mesh2Output { Status = Mesh2Status.Success, VX = input.X, VY = input.Y, InputToVertex = new[] { 0, 1, 2 },
                Triangles = new[] { new[] { 0, 1, 2 } }, Neighbours = new[] { new[] { -1, -1, -1 } }, Subsegments = new List<SubsegmentOutput>(), BadTriangles = new int[0] };
            Same(input, output, new OracleExpect { Milestone = Milestone.M3 }, "triangle");
        }

        [Fact]
        public void SelfChecks_PassOnTheReferencePath()
        {
            Oracle.ForceReference = true;
            try {
                var failed = SelfCheck.Run().Where(i => !i.Passed).Select(i => i.Name + ": " + i.Detail).ToList();
                Assert.True(failed.Count == 0, string.Join("\n", failed));
            } finally { Oracle.ForceReference = false; }
        }
    }
}
