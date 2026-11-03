using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's rules for what refinement may do to the chains of the input's own rounding (owner decision on Q17, 2026-11-03:
    /// refinement must not collapse what the input's rounding keeps), and for the snapped domain (the hull model, its area):
    /// RefinementCoincidence (two chains share a link from a vertex of refinement on), RefinementPinch (two chains meet in a vertex of
    /// refinement: a known violation, listed, not failing yet), ChainSkipsHotPixel (a chain misses a hot pixel of the input's rounding
    /// its segment contacts), and the mesh area against the snapped domain (a report)
    /// </summary>
    public class QualityMesh2OracleCoincidenceTests
    {
        static Mesh2Output WithoutTheChainRule(Mesh2Input input)
        {
            QualityMesh2.Refiner.KeepChainsApartForTests = false;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.KeepChainsApartForTests = null; }
        }

        /// <summary>
        /// the refinement before #360's chain rule rerouted a piece onto another chain's edge: two chains then share a link from a vertex of
        /// refinement on (three subsegments there), which the oracle used to accept (the chains exist). Rejected now; the mesher with
        /// the rule gives a mesh without it. Seeds of <see cref="StarPolygonFuzz"/> where the refinement without the rule does that
        /// </summary>
        [Theory]
        [InlineData(12)]
        [InlineData(15)]
        [InlineData(100)]
        public void SharedLinkFromARefinementVertex_IsRejected(int seed)
        {
            var input = StarPolygonFuzz.Make(seed);
            var before = Oracle.Check(input, WithoutTheChainRule(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.Contains(before.Violations, v => v.Kind == Violation.RefinementCoincidence);
            var now = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.DoesNotContain(now.Violations, v => v.Kind == Violation.RefinementCoincidence);
        }

        /// <summary>
        /// a known violation (owner decision on Q17; the mesher's fix comes next): the spike of seed 527 has two edges whose links in the
        /// input's rounding, (9, 8) - (13, 12) and (13, 12) - (10, 10), both cross pixel (12, 11); refinement puts a vertex there, and both
        /// chains run through it, the tip then hanging on that point. Listed as RefinementPinch, the verdict still VALID
        /// </summary>
        [Fact]
        public void PinchAtARefinementVertex_IsAKnownViolation()
        {
            var input = StarPolygonFuzz.Make(527);
            var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            var pinch = Assert.Single(report.KnownViolations);
            Assert.Equal(Violation.RefinementPinch, pinch.Kind);
            Assert.Contains("(12, 11)", pinch.Detail);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        static Mesh2Input Moved(Mesh2Input plain, Similarity s)
        {
            var moved = new Mesh2Input { X = (double[])plain.X.Clone(), Y = (double[])plain.Y.Clone(), SnapTolerance = plain.SnapTolerance * s.Scale,
                MinAngleDeg = plain.MinAngleDeg, SteinerLimit = plain.SteinerLimit };
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            moved.Rings.AddRange(plain.Rings);
            moved.Segments.AddRange(plain.Segments);
            return moved;
        }

        /// <summary>
        /// invariance: at the 2^k scales the pinch of seed 527 and the coincidence of seed 12 without the chain rule are judged the same,
        /// violations and known violations alike (scaled); in every frame the new rules do not fire on seed 527's mesh
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void Rules_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var pinch = Moved(StarPolygonFuzz.Make(527), s);
            var r527 = Oracle.Check(pinch, new QualityMesh2Candidate().Run(pinch), new OracleExpect { Milestone = Milestone.M3 });
            // (its free point outside the ring meshes outside the domain in some frames: a class of its own, see
            // QualityMesh2KeepsChainsApartTests; the rules here must not fire in any frame)
            Assert.DoesNotContain(r527.Violations, v => v.Kind == Violation.RefinementCoincidence || v.Kind == Violation.ChainSkipsHotPixel);
            if (!s.IsExact) return;
            Assert.True(r527.Ok, name + ": " + r527);
            var plain527 = StarPolygonFuzz.Make(527);
            var p527 = Oracle.Check(plain527, new QualityMesh2Candidate().Run(plain527), new OracleExpect { Milestone = Milestone.M3 });
            Assert.Equal(p527.KnownViolations.Count, r527.KnownViolations.Count);
            var plain12 = StarPolygonFuzz.Make(12);
            var moved12 = Moved(plain12, s);
            var p12 = Oracle.Check(plain12, WithoutTheChainRule(plain12), new OracleExpect { Milestone = Milestone.M3 });
            var m12 = Oracle.Check(moved12, WithoutTheChainRule(moved12), new OracleExpect { Milestone = Milestone.M3 });
            Assert.Equal(p12.Violations.Select(v => v.Kind), m12.Violations.Select(v => v.Kind));
            Assert.Equal(p12.KnownViolations.Count, m12.KnownViolations.Count);
        }

        /// <summary>
        /// the snapped-area report is opt-in (it builds both snapped images on every run): the verdict, the violations and the known
        /// violations do not depend on it, on a pinch case (seed 527) and a restore case (seed 62)
        /// </summary>
        [Theory]
        [InlineData(527)]
        [InlineData(62)]
        public void SnappedAreaReport_ChangesNoVerdict(int seed)
        {
            var input = StarPolygonFuzz.Make(seed);
            var o = new QualityMesh2Candidate().Run(input);
            var plain = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            var withArea = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3, ReportSnappedArea = true });
            Assert.Null(plain.MeshMinusSnappedArea);
            Assert.NotNull(withArea.MeshMinusSnappedArea);
            Assert.Equal(plain.ToString(), withArea.ToString());
            Assert.Equal(plain.KnownViolations.Select(v => v.ToString()), withArea.KnownViolations.Select(v => v.ToString()));
            Assert.Equal(plain.CollapsedSegments + "/" + plain.CollapsedPoints + "/" + plain.CollapsedViaRefinement,
                withArea.CollapsedSegments + "/" + withArea.CollapsedPoints + "/" + withArea.CollapsedViaRefinement);
        }

        /// <summary>
        /// ratchet (coordinator 2026-11-03): the refinement pinches on seeds 0 to 2999 of <see cref="StarPolygonFuzz"/> at M3, exactly.
        /// More fails; fewer means the limit goes down with the change that removes them
        /// </summary>
        [Fact]
        public void RefinementPinches_Ratchet()
        {
            int inputs = 0, pinches = 0;
            for (int seed = 0; seed < 3000; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
                int k = report.KnownViolations.Count(v => v.Kind == Violation.RefinementPinch);
                if (k > 0) { ++inputs; pinches += k; }
            }
            Assert.Equal((13, 12), (pinches, inputs));
        }

        /// <summary>
        /// ChainSkipsHotPixel's exception for collapsed links: seed 818's star ring crosses itself, and its thin second lobe collapses in
        /// the input's own rounding; edge 2 - 3 contacts the crossing's pixel (5, 8), but its links there collapse, so its chain need not
        /// run through it (the pixel itself keeps links of the big lobe). Accepted snapped alone and refined
        /// </summary>
        [Fact]
        public void HotPixelOnTheEdgesOwnCollapsedLinks_IsNotDemanded()
        {
            var input = StarPolygonFuzz.Make(818);
            var snapped = new Mesh2Input { X = input.X, Y = input.Y, SnapTolerance = 1 };
            snapped.Rings.AddRange(input.Rings);
            var m2 = Oracle.Check(snapped, new ConstrainedDelaunayMesh2Candidate().Run(snapped), new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(m2.Ok, m2.ToString());
            var m3 = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(m3.Ok, m3.ToString());
        }

        /// <summary>
        /// the snapped domain's area (it was left 0) and the hull model: snapped alone (M2), the mesh is exactly the snapped domain, for
        /// ring domains and, with the convex hull of the snapped vertices as the boundary, for hull domains (segment soups); with the
        /// exact hull's rounded image (the M3 model) the hull domains differ. Seeds of <see cref="StarPolygonFuzz"/> and soups of 3 to 8
        /// segments in [0, 6] at quarters
        /// </summary>
        [Fact]
        public void SnappedAlone_MeshAreaEqualsTheSnappedDomain()
        {
            var wrong = new List<string>();
            int hullRuns = 0;
            for (int seed = 0; seed < 300; ++seed) {
                var star = StarPolygonFuzz.Make(seed);
                var ring = new Mesh2Input { X = star.X, Y = star.Y, SnapTolerance = 1 };
                ring.Rings.AddRange(star.Rings);
                ring.Segments.AddRange(star.Segments);
                var rnd = new System.Random(seed);
                int n = 3 + rnd.Next(6);
                var xs = new List<double>(); var ys = new List<double>();
                var soup = new Mesh2Input { SnapTolerance = 1 };
                for (int i = 0; i < n; ++i) {
                    for (int k = 0; k < 2; ++k) { xs.Add(rnd.Next(25) * 0.25); ys.Add(rnd.Next(25) * 0.25); }
                    if (xs[2 * i] != xs[2 * i + 1] || ys[2 * i] != ys[2 * i + 1]) soup.Segments.Add(new SegmentInput(2 * i, 2 * i + 1));
                }
                soup.X = xs.ToArray(); soup.Y = ys.ToArray();
                foreach (var (name, input) in new[] { ("star", ring), ("soup", soup) }) {
                    var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
                    if (o.Status != Mesh2Status.Success) continue;
                    var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2, ReportSnappedArea = true });
                    if (!report.Ok) continue;
                    if (name == "soup") ++hullRuns;
                    if (report.MeshMinusSnappedArea == null || report.MeshMinusSnappedArea.Value.Sign != 0)
                        wrong.Add(name + " " + seed + ": " + report.MeshMinusSnappedArea);
                }
            }
            Assert.True(wrong.Count == 0, wrong.Count + " differ:\n" + string.Join("\n", wrong.Take(20)));
            Assert.True(hullRuns >= 250, "hull runs " + hullRuns);
        }
    }
}
