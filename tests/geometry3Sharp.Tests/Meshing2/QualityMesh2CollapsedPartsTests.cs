using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Q17: a domain narrower than a pixel, which snap rounding collapses. A zigzag polyline 7 mm wide and 10 m long on a grid of 1 cm, its convex hull the domain.
    /// Snapping puts every vertex at a pixel centre, the hull chains and the polyline's chains share them, and parts of the domain
    /// collapse: corners in no triangle, segments with no chain there. Owner decisions (spec Q17): a part that snap rounding of the
    /// input collapses may collapse, and the oracle accepts what is missing only there (O-H5, 2026-11-02); the refinement must not
    /// collapse what the rounding of the input keeps (rule (ii'), 2026-11-03). Snapped alone and refined, the zigzags are accepted: the
    /// refinement puts no vertex where two chains of the input's image pass apart (the mesher's bugfix; before it the refined cases were
    /// pinned as known INVALID, and they fail against the library without it).
    /// </summary>
    public class QualityMesh2CollapsedPartsTests
    {
        /// <summary> the zigzag at slope 0.3 (six points) or 1.1 (eight points), offsets of 3.5 mm to either side, steps of 2 m </summary>
        internal static Mesh2Input Zigzag(int which, double? angle)
        {
            var input = which == 0
                ? new Mesh2Input {
                    X = new[] { 1000.0010343207233, 1001.9096386575279, 1003.8223802772258, 1005.7309846140304, 1007.6437262337281, 1009.5523305705327 },
                    Y = new[] { 499.99665632228806, 500.5943840910346, 501.1787371489334, 501.77646491767996, 502.36081797557875, 502.95854574432536 } }
                : new Mesh2Input {
                    X = new[] { 1000.0031192257602, 1000.9040730170909, 1001.8175037114626, 1002.7184575027933, 1003.6318881971648, 1004.5328419884955, 1005.4462726828672, 1006.3472264741979 },
                    Y = new[] { 499.998412413575, 501.78400230654785, 503.5632418538207, 505.3488317467936, 507.1280712940665, 508.91366118703934, 510.6929007343122, 512.478490627285 } };
            for (int i = 0; i + 1 < input.Count; ++i) input.Segments.Add(new SegmentInput(i, i + 1));
            input.SnapTolerance = 0.01; input.MinAngleDeg = angle; input.SteinerLimit = 50000;
            return input;
        }

        /// <summary> snapping alone (M2): the collapsed parts are accepted (O-H5 on the snap-rounded images) </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void DomainNarrowerThanAPixel_SnappedCollapseIsAccepted(int which)
        {
            var input = Zigzag(which, null);
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary>
        /// with refinement (M3) the mesher keeps what the rounding of the input keeps (owner decision 2026-11-03): no grid point of
        /// refinement brings together two chains of the input's image that are apart there. Before the fix it pinched 23.5 px² (z0) and
        /// 169 px² (z1) of the band 0.7 pixel wide that the input's rounding keeps (z0: 3 SegmentMissing and 1 VertexUnused, z1: 5
        /// SegmentMissing). Accepted, with exactly the excuses of the input's own collapse
        /// </summary>
        [Theory]
        [InlineData(0, 0.0, 2, 1)]
        [InlineData(0, 20.0, 2, 1)]
        [InlineData(1, 0.0, 1, 0)]
        [InlineData(1, 20.0, 1, 0)]
        public void DomainNarrowerThanAPixel_RefinementKeepsWhatRoundingKeeps(int which, double angle, int segmentsExcused, int pointsExcused)
        {
            var input = Zigzag(which, angle);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(segmentsExcused, report.CollapsedSegments);
            Assert.Equal(pointsExcused, report.CollapsedPoints);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary>
        /// the same under every similarity: snapped alone and refined, the oracle accepts the mesh in every frame; refined, the 2^k scales
        /// give the same mesh bit for bit and the same excuses
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void DomainNarrowerThanAPixel_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            Mesh2Input Moved(Mesh2Input plain)
            {
                var moved = Zigzag(0, plain.MinAngleDeg);
                for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
                moved.SnapTolerance = plain.SnapTolerance * s.Scale;
                return moved;
            }
            var snapped = Moved(Zigzag(0, null));
            var m2 = new ConstrainedDelaunayMesh2Candidate().Run(snapped);
            Assert.Equal(Mesh2Status.Success, m2.Status);
            var report = Oracle.Check(snapped, m2, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, name + ": " + report);
            var refined = Moved(Zigzag(0, 20));
            var m3 = new QualityMesh2Candidate().Run(refined);
            Assert.Equal(Mesh2Status.Success, m3.Status);
            var refinedOk = Oracle.Check(refined, m3, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(refinedOk.Ok, name + " (refined): " + refinedOk);
            if (s.IsExact) {
                var plain = Zigzag(0, 20);
                var a = new QualityMesh2Candidate().Run(plain);
                var b = m3;
                // the oracle's verdict too: the same violations and excuses at every 2^k scale
                var plainReport = Oracle.Check(plain, a, new OracleExpect { Milestone = Milestone.M3 });
                var refinedReport = Oracle.Check(refined, m3, new OracleExpect { Milestone = Milestone.M3 });
                Assert.Equal(plainReport.Violations.Select(v => v.Kind), refinedReport.Violations.Select(v => v.Kind));
                Assert.Equal(plainReport.CollapsedSegments, refinedReport.CollapsedSegments);
                Assert.Equal(plainReport.CollapsedPoints, refinedReport.CollapsedPoints);
                Assert.Equal(plainReport.CollapsedViaRefinement, refinedReport.CollapsedViaRefinement);
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes: the same mesh </summary>
        [Fact]
        public void DomainNarrowerThanAPixel_Reuse()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Zigzag(0, 20));
            var mesher = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.01, SteinerLimit = 50000, Conforming = true };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            // the collapse diagnostics too (a snapshot left over from the first compute would change the counts)
            Assert.Equal(a.Diagnostics ?? new string[0], b.Diagnostics ?? new string[0]);
            Assert.Contains(a.Diagnostics ?? new string[0], l => l.Contains("parts that snapping collapsed"));
        }
    }
}
