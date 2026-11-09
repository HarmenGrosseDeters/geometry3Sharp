using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's fast path with Oracle.CheckFastPathForTests on: every index and filter that replaces a scan or an exact test is
    /// checked against it as it runs (the domain edges near a point; the sources near a triangle in the merge zone and those covering a
    /// subsegment; the vertices and subsegments of a pixel on the snap grid; the zone filters at their call sites), and a difference
    /// throws. Run over meshes that reach each of them: the catalogue, the grid fuzz in both modes, hull domains (no rings), and the star
    /// polygons on the snap grid. The verdicts themselves are compared with the reference path in Meshing2OracleModesTests.
    /// </summary>
    public class Meshing2OracleFastPathCheckTests
    {
        static OracleReport Checked(Mesh2Input input, Mesh2Output output, OracleExpect e)
        {
            Oracle.CheckFastPathForTests = true;
            try { return Oracle.Check(input, output, e); }
            finally { Oracle.CheckFastPathForTests = false; }
        }

        static readonly OracleExpect M3 = new OracleExpect { Milestone = Milestone.M3 };

        public static IEnumerable<object[]> RowIds() => Catalogue.Rows()
            .Where(r => r.State == CaseState.Covered && r.Test == null && r.Case != null && !r.Slow && CandidateRegistry.For(r.Case.Milestone) != null)
            .Select(r => new object[] { r.Id });

        public static IEnumerable<object[]> SelfCheckNames() => SelfCheck.Run().Select(i => new object[] { i.Name });

        /// <summary> the oracle's own hand-built cases (a grid pixel crossed by a constraint, a vertex at a circumcentre's pixel, merge
        /// zones, hull tips, ...) with the checks on: each still passes, and no index or filter differs from its scan </summary>
        [Theory]
        [MemberData(nameof(SelfCheckNames))]
        public void SelfCheckItem(string name)
        {
            Oracle.CheckFastPathForTests = true;
            try {
                var item = SelfCheck.Run().First(i => i.Name == name);
                Assert.True(item.Passed, item.Detail);
            } finally { Oracle.CheckFastPathForTests = false; }
        }

        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(RowIds))]
        public void CatalogueRow(string id)
        {
            var row = Catalogue.Rows().First(r => r.Id == id);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            Checked(row.Case.Input, output, row.Case.Expect);
        }

        static Mesh2Output Out(Mesh2Input input, double[] vx, double[] vy, int[][] tris) => new Mesh2Output {
            Status = Mesh2Status.Success, VX = vx, VY = vy, InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
            Triangles = tris, Neighbours = SelfCheck.NeighboursOf(tris), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };

        /// <summary>
        /// GridBlocked's subsegments: the skinny triangle of the oracle's self-check 5b on the grid (pixel 1/4), with a short subsegment
        /// across the upper right part of its circumcentre's pixel, from a quarter pixel left of the centre one pixel up to one pixel
        /// right and a quarter pixel down: it crosses the pixel's interior, and its box misses the pixel's lower left corner (a query of
        /// less than the pixel's box would miss it). The check compares the grid's answer with the scan of all subsegments
        /// </summary>
        [Fact]
        public void GridBlocked_SubsegmentAcrossACornerOfThePixel()
        {
            double w = 0.25;
            double[] pts = { 5.0, 5, 9, 5.5, 5.5, 5.25 };
            // the circumcentre, exactly, and its pixel's centre
            Pt a = Pt.FromDoubles(pts[0], pts[1]), b = Pt.FromDoubles(pts[2], pts[3]), c = Pt.FromDoubles(pts[4], pts[5]);
            Q bx = b.Qx - a.Qx, by = b.Qy - a.Qy, cx = c.Qx - a.Qx, cy = c.Qy - a.Qy, d = (bx * cy - by * cx) * 2, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            double ux = double.Parse((a.Qx + (cy * b2 - by * c2) / d).ToDecimal(20), System.Globalization.CultureInfo.InvariantCulture);
            double uy = double.Parse((a.Qy + (bx * c2 - cx * b2) / d).ToDecimal(20), System.Globalization.CultureInfo.InvariantCulture);
            double px = Math.Floor(ux / w + 0.5) * w, py = Math.Floor(uy / w + 0.5) * w;
            var input = new Mesh2Input {
                X = new[] { 0, 16, 16, 0, pts[0], pts[2], pts[4], px, px + w, px - 3 * w, px - 2 * w },
                Y = new[] { 0, 0, 16, 16, pts[1], pts[3], pts[5], py + 2 * w, py - 2 * w, py + 5 * w, py + 5 * w },
                SnapTolerance = w, MinAngleDeg = 20,
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Segments.Add(new SegmentInput(7, 8));
            var o = Out(input, (double[])input.X.Clone(), (double[])input.Y.Clone(), new[] { new[] { 4, 5, 6 } });
            o.BadTriangles = new[] { 0 };
            o.Subsegments.Add(new SubsegmentOutput(7, 8));
            // a short subsegment left of and above the pixel, many times over: the grid over the subsegments' boxes then has cells of
            // about a quarter pixel and starts left of the pixel (a grid of one cell, or one clamped to its border, returns every box)
            input.Segments.Add(new SegmentInput(9, 10));
            for (int i = 0; i < 900; ++i) o.Subsegments.Add(new SubsegmentOutput(9, 10));
            // (other violations do not matter here: the check of the fast path throws if the grid of subsegments misses the crossing)
            var report = Checked(input, o, M3);
            Assert.False(report.Has(Violation.AngleBelowBound), "the triangle should be grid-blocked: " + report);
            // the negative control: without the subsegment across the pixel the triangle is judged, so GridBlocked decided above
            o.Subsegments.RemoveAt(0);
            input.Segments.RemoveAt(0);
            Assert.True(Checked(input, o, M3).Has(Violation.AngleBelowBound), "without the subsegment the triangle should be judged");
        }

        /// <summary>
        /// The hull edge nearest to a boundary vertex on a hull domain (D13), where the grid over the hull edges is fine: 400 input points
        /// on a circle of radius 1/2 around (1/2, 1/2), no rings, and an output vertex 0.09 outside the hull edge at 45 degrees (within a
        /// stated tube of 0.1, so it counts as moved by its distance). Its box is several cells of the grid away from the edge's box: only
        /// the query widened by the tube finds the edge. The check compares the nearest edge found with the minimum over all hull edges;
        /// 0.11 outside (beyond the tube) is the control
        /// </summary>
        [Trait("Category", "Slow")]
        [Theory]
        [InlineData(0.09, true)]
        [InlineData(0.11, false)]
        public void HullDomain_BoundaryVertexOffTheHullEdge(double offset, bool withinTube)
        {
            const int n = 400;
            var x = new double[n]; var y = new double[n];
            for (int i = 0; i < n; ++i) { double a = 2 * Math.PI * i / n; x[i] = 0.5 + 0.5 * Math.Cos(a); y[i] = 0.5 + 0.5 * Math.Sin(a); }
            var input = new Mesh2Input { X = x, Y = y };
            input.Segments.Add(new SegmentInput(0, n / 2));
            // the output: the input points, the centre (n) and the vertex outside the edge from point 50 to 51 (n + 1); a fan from the centre
            int k = n / 8;
            double am = 2 * Math.PI * (k + 0.5) / n, r = 0.5 * Math.Cos(Math.PI / n) + offset;
            var vx = x.Concat(new[] { 0.5, 0.5 + r * Math.Cos(am) }).ToArray();
            var vy = y.Concat(new[] { 0.5, 0.5 + r * Math.Sin(am) }).ToArray();
            var tris = new List<int[]>();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                if (i == k) { tris.Add(new[] { n, i, n + 1 }); tris.Add(new[] { n, n + 1, j }); }
                else tris.Add(new[] { n, i, j });
            }
            var o = Out(input, vx, vy, tris.ToArray());
            o.Subsegments.Add(new SubsegmentOutput(0, n / 2));
            // (the check of the fast path throws if the index misses the nearest hull edge). The control: within the tube the edges to
            // the vertex count as on the hull (DomainEdgesNear found the edge); beyond it they are boundary edges on nothing
            var report = Checked(input, o, new OracleExpect { Milestone = Milestone.M2, Tube = Q.FromDouble(0.1) });
            Assert.Equal(!withinTube, report.Has(Violation.HullBoundary) || report.Has(Violation.BoundaryNotConstrained));
        }

        /// <summary>
        /// The one intended output difference of the fast path's indexes: a hull domain without any hull edge (all input points the same)
        /// whose output has triangles. The scan's minimum over no hull edges threw out of Oracle.Check; now the boundary vertices count as
        /// not moved, and the degenerate domain is reported as such (TrianglesOnDegenerate), on both paths and with the checks on
        /// </summary>
        [Fact]
        public void HullDomain_WithoutHullEdges_IsReportedNotThrown()
        {
            var input = new Mesh2Input { X = new double[] { 1, 1, 1 }, Y = new double[] { 1, 1, 1 } };
            var o = new Mesh2Output {
                Status = Mesh2Status.Success, VX = new double[] { 1, 0, 2, 1 }, VY = new double[] { 1, 0, 0, 2 }, InputToVertex = new[] { 0, 0, 0 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 1 } }, BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
            o.Neighbours = SelfCheck.NeighboursOf(o.Triangles);
            var e = new OracleExpect { Milestone = Milestone.M2 };
            Assert.True(Checked(input, o, e).Has(Violation.TrianglesOnDegenerate));
            Assert.True(Oracle.Check(input, o, e.WithReference()).Has(Violation.TrianglesOnDegenerate));
        }

        /// <summary> the lens tests' fuzz (a square, a hole, random segments) with and without the snap grid, both modes, 20 to 30 degrees:
        /// the merge zone (small angles between segments), GridBlocked and the marks </summary>
        [Fact]
        public void Fuzz()
        {
            for (int seed = 0; seed < 30; ++seed) {
                double snap = new[] { 0, 0.25, 0.125 }[seed % 3];
                var input = QualityMesh2LensDeletionTests.Fuzz(7000 + seed, new[] { 20.0, 25, 30 }[seed % 3], snap);
                input.ConstrainedQuality = seed % 2 == 0;
                var o = new QualityMesh2Candidate().Run(input);
                Assert.True(Checked(input, o, M3).Ok, "seed " + seed);
            }
        }

        /// <summary> hull domains (segments only, no rings: D13), where the boundary is the hull and the coverage checks ask for the domain
        /// edges near a point </summary>
        [Fact]
        public void HullDomains()
        {
            for (int seed = 0; seed < 20; ++seed) {
                var r = new Random(seed);
                var x = new List<double>(); var y = new List<double>();
                var input = new Mesh2Input();
                for (int i = 0; i < 5; ++i) {
                    x.Add(r.Next(0, 40) / 4.0); y.Add(r.Next(0, 40) / 4.0);
                    x.Add(r.Next(0, 40) / 4.0 + 0.01 * r.Next(3)); y.Add(r.Next(0, 40) / 4.0);
                    input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
                }
                input.X = x.ToArray(); input.Y = y.ToArray();
                input.MinAngleDeg = 20 + 5 * (seed % 3);
                if (seed % 2 == 1) input.SnapTolerance = 0.125;
                var o = new QualityMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) continue;
                Checked(input, o, M3);
            }
        }

        /// <summary> the star polygons of the Q17 fuzz on the snap grid (pixel 1): GridBlocked and the marks on chains </summary>
        [Fact]
        public void StarPolygons()
        {
            for (int seed = 0; seed < 60; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                input.ConstrainedQuality = seed % 3 == 0;
                var o = new QualityMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) continue;
                Checked(input, o, M3);
            }
        }
    }
}
