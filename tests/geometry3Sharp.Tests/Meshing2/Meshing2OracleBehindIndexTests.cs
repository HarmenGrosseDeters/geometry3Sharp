using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's test whether a triangle's circumcentre (or its pixel centre on the grid) lies behind an unsplittable subsegment uses
    /// the subsegment grid for the constructed point too (by its interval of doubles), not only for a vertex. An unrefined mesh (M2) of
    /// many short segments and points, judged at M3, sends almost every triangle through the whole exemption chain; with
    /// Oracle.CheckFastPathForTests every grid query is checked to return a superset of the boxes that meet the query, and every answer
    /// against the scan of all subsegments, so a query box that misses the constructed point throws.
    /// </summary>
    public class Meshing2OracleBehindIndexTests
    {
        static string H(double x) => "0x" + BitConverter.DoubleToInt64Bits(x).ToString("x16", CultureInfo.InvariantCulture);

        /// <summary> a square of side 64 with a grid of short disjoint segments and random points (fixed seed) </summary>
        static Mesh2Input Input(int seed, double snap, double length)
        {
            var rnd = new Random(seed);
            var pts = new List<(double, double)> { (0, 0), (64, 0), (64, 64), (0, 64) };
            var segs = new List<(int, int)>();
            for (int i = 0; i < 12; ++i)
                for (int j = 0; j < 12; ++j) {
                    double x = 2 + i * 5, y = 2 + j * 5;
                    segs.Add((pts.Count, pts.Count + 1));
                    pts.Add((x, y));
                    pts.Add((x + length * (0.5 + rnd.NextDouble()), y + length * (rnd.NextDouble() - 0.5)));
                }
            for (int k = 0; k < 1200; ++k) pts.Add((1 + 62 * rnd.NextDouble(), 1 + 62 * rnd.NextDouble()));
            var sb = new StringBuilder("mesh2-input 1\npoints " + pts.Count + "\n");
            foreach (var (x, y) in pts) sb.Append(H(x)).Append(' ').Append(H(y)).Append('\n');
            sb.Append("segments " + segs.Count + "\n");
            foreach (var (a, b) in segs) sb.Append(a).Append(' ').Append(b).Append(" 0 0\n");
            sb.Append("rings 1\nouter 0 4 0 1 2 3\nholeseeds 0\nregions 0\nmin_angle_deg ").Append(H(20)).Append("\nmax_area -\nsteiner_limit -\n");
            sb.Append("snap_tolerance ").Append(H(snap)).Append("\nsize_constant -\nresolve_crossings 1\nconforming 0\nend\n");
            return TextFormat.ReadInput(sb.ToString());
        }

        [Theory]
        [InlineData(5, 0.0, 2.0)]
        [InlineData(6, 0.0, 2.0)]
        [InlineData(7, 0.25, 0.4)]
        [InlineData(8, 0.25, 0.4)]
        [InlineData(9, 0.125, 0.2)]
        public void UnrefinedMesh_AtM3_CircumcentreQueriesAreSupersets(int seed, double snap, double length)
        {
            var input = Input(seed, snap, length);
            var output = CandidateRegistry.For(Milestone.M2).Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Oracle.CheckFastPathForTests = true;
            OracleReport report;
            try { report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }); }
            finally { Oracle.CheckFastPathForTests = false; }
            // the unrefined mesh has triangles below the bound, so the exemption chain ran on them
            Assert.False(report.Ok);
            Assert.True(report.Has(Violation.AngleBelowBound) || report.Violations.Count == 200, report.ToString());
        }
    }
}
