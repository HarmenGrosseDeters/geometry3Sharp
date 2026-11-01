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

        public static IEnumerable<object[]> RowIds() => Rows.Select(r => new object[] { r.Id });

        /// <summary> what a verdict consists of: the kinds with their counts, and the measured values </summary>
        static string Verdict(OracleReport r) =>
            string.Join(",", r.Violations.GroupBy(v => v.Kind).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count()))
            + " | area2 " + r.Area2 + " | minsin2 " + r.MinSin2 + " | below " + r.BelowAngle + " | ties " + r.TieEdges + " | unique " + r.Unique
            + " | constructed " + r.ConstructedVertices;

        static void Same(Mesh2Input input, Mesh2Output output, OracleExpect e, string what)
        {
            var fast = Oracle.Check(input, output, e);
            var reference = Oracle.Check(input, output, e.WithReference());
            Assert.True(Verdict(fast) == Verdict(reference), what + ": fast " + Verdict(fast) + " / reference " + Verdict(reference));
        }

        [Theory]
        [MemberData(nameof(RowIds))]
        public void CatalogueRow_SameVerdictOnBothPaths(string id)
        {
            var row = Rows.First(r => r.Id == id);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            Same(row.Case.Input, output, row.Case.Expect, id);
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

        [Theory]
        [MemberData(nameof(RowIds))]
        public void CorruptedRow_RejectedOnBothPaths(string id)
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
