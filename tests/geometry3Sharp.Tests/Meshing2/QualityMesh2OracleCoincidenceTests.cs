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
        /// <summary> the refinement without the pinch rule (a split may route a chain through another chain's vertex of refinement) </summary>
        static Mesh2Output WithoutThePinchRule(Mesh2Input input)
        {
            QualityMesh2.Refiner.KeepPinchFreeForTests = false;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.KeepPinchFreeForTests = null; }
        }

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
        /// the owner's strict decision on Q17: the spike of seed 527 has two edges whose links in the input's rounding, (9, 8) - (13, 12)
        /// and (13, 12) - (10, 10), both cross pixel (12, 11); refinement without the pinch rule puts a vertex there, and both chains run
        /// through it, the tip then hanging on that point. Rejected as RefinementPinch (a known violation, listed only, until the mesher's
        /// pinch rule; the mesher with the rule: QualityMesh2PinchFreeTests)
        /// </summary>
        [Fact]
        public void PinchAtARefinementVertex_IsRejected()
        {
            var input = StarPolygonFuzz.Make(527);
            var report = Oracle.Check(input, WithoutThePinchRule(input), new OracleExpect { Milestone = Milestone.M3 });
            var pinch = Assert.Single(report.Violations);
            Assert.Equal(Violation.RefinementPinch, pinch.Kind);
            Assert.Contains("(12, 11)", pinch.Detail);
            Assert.Empty(report.KnownViolations);
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
        /// invariance: at the 2^k scales the pinch of seed 527 without the pinch rule and the coincidence of seed 12 without the chain rule
        /// are judged the same,
        /// violations and known violations alike (scaled); in every frame the new rules do not fire on seed 527's mesh
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void Rules_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var pinch = Moved(StarPolygonFuzz.Make(527), s);
            var r527 = Oracle.Check(pinch, WithoutThePinchRule(pinch), new OracleExpect { Milestone = Milestone.M3 });
            // (its free point outside the ring meshes outside the domain in some frames: a class of its own, see
            // QualityMesh2KeepsChainsApartTests; the rules here must not fire in any frame)
            Assert.DoesNotContain(r527.Violations, v => v.Kind == Violation.RefinementCoincidence || v.Kind == Violation.ChainSkipsHotPixel);
            if (!s.IsExact) return;
            var plain527 = StarPolygonFuzz.Make(527);
            var p527 = Oracle.Check(plain527, WithoutThePinchRule(plain527), new OracleExpect { Milestone = Milestone.M3 });
            Assert.Equal(new[] { Violation.RefinementPinch }, p527.Violations.Select(v => v.Kind));
            Assert.Equal(p527.Violations.Select(v => v.Kind), r527.Violations.Select(v => v.Kind));
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
            // (seed 527 meshed without the pinch rule: a pinch, a failing violation, to carry through)
            var o = seed == 527 ? WithoutThePinchRule(input) : new QualityMesh2Candidate().Run(input);
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
        /// More fails; fewer means the limit goes down with the change that removes them (13 in 12 inputs at the rule's introduction; 12 in
        /// 11 since an input whose outer rings all collapse has an empty domain; 0 since the mesher's pinch rule, when they became a
        /// failing violation)
        /// </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void RefinementPinches_Ratchet()
        {
            int inputs = 0, pinches = 0;
            for (int seed = 0; seed < 3000; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
                int k = report.Violations.Count(v => v.Kind == Violation.RefinementPinch) + report.KnownViolations.Count(v => v.Kind == Violation.RefinementPinch);
                if (k > 0) { ++inputs; pinches += k; }
            }
            Assert.Equal((0, 0), (pinches, inputs));
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
        /// a contacted hot pixel that collapses in both images needs no chain (O-H5; it lies on the edge's own image, whose links there
        /// collapse in both). Seeds of <see cref="StarPolygonFuzz"/> at M3 whose input points on collapsed parts lie in pixels their
        /// neighbours' edges contact
        /// </summary>
        [Theory]
        [InlineData(493)]
        [InlineData(1762)]
        public void HotPixelCollapsedInBothImages_IsNotDemanded(int seed)
        {
            var input = StarPolygonFuzz.Make(seed);
            var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.CollapsedPoints > 0, report.ExcusedSummary());
        }

        /// <summary>
        /// at UTM coordinates and a pixel of 0.01 the output vertices are pixel centres rounded to doubles: the segment of Herne 373600
        /// (373662.135, 5709174.775) - (373662.129, 5709174.025), whose start lies exactly on the corner of its pixel, contacts the hot
        /// pixel of the point (373662.13, 5709174.77); its chain through that pixel is judged at the neighbours' exact pixel centres (the
        /// rounded start vertex misses the envelope by an ulp)
        /// </summary>
        [Fact]
        public void ChainAtUtmScale_IsJudgedAtExactPixelCentres()
        {
            var input = new Mesh2Input { X = new[] { 373662.135, 373662.129, 373662.13, 373661.9 }, Y = new[] { 5709174.775, 5709174.025, 5709174.77, 5709174.4 }, SnapTolerance = 0.01 };
            input.Segments.Add(new SegmentInput(0, 1));
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary>
        /// option C only where the mesher refines (review of #36, S3): hull-domain soups at M3 in the constrained-quality mode at angle
        /// 0 are not refined, so their hull is not rounded and gets no restore pixels; judged as M3 they get the same verdict as the
        /// identical mesh judged as M2
        /// </summary>
        [Fact]
        public void HullNotRefined_IsNotRounded()
        {
            var wrong = new List<string>();
            for (int seed = 0; seed < 300; ++seed) {
                var input = Soup(seed);
                input.ConstrainedQuality = true; input.MinAngleDeg = 0;
                var o = new QualityMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) continue;
                var m3 = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                var m2 = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
                if (m3.ToString() != m2.ToString()) wrong.Add(seed + ": M3 " + m3 + " | M2 " + m2);
            }
            Assert.True(wrong.Count == 0, wrong.Count + " differ:\n" + string.Join("\n", wrong.Take(10)));
        }

        /// <summary>
        /// each clause of the oracle's refinement test that can change the result (Refining, review of #36): the hull of (0, 0), (10, 1.4), (10, -3), (0, -3) with
        /// (5, 0.4) inside has the restore pixel (4, 1) on its top hull edge, which the oracle computes only where the mesher rounds the hull
        /// (M3 and refining). Each option set is judged on the candidate's mesh, which must be accepted at M3 (M2 judges the unrefined mesh with the angle set for the oracle only)
        /// </summary>
        [Theory]
        [InlineData("angle 20", true)]
        [InlineData("angle 0", true)]
        [InlineData("area 50", true)]
        [InlineData("size 5", true)]
        [InlineData("size function", true)]
        [InlineData("conforming", true)]
        [InlineData("cq angle 20", true)]
        [InlineData("cq area 50", true)]
        [InlineData("cq region area 50", true)]
        [InlineData("cq angle 0", false)]
        [InlineData("cq conforming", false)]
        [InlineData("Steiner limit", false)]
        [InlineData("none", false)]
        [InlineData("M2 angle 20", false)]
        public void Refining_DecidesTheHullModel(string options, bool rounded)
        {
            var input = new Mesh2Input { SnapTolerance = 1, X = new[] { 0.0, 10, 10, 0, 5 }, Y = new[] { 0.0, 1.4, -3, -3, 0.4 } };
            var milestone = Milestone.M3;
            switch (options) {
                case "angle 20": input.MinAngleDeg = 20; break;
                case "angle 0": input.MinAngleDeg = 0; break;
                case "area 50": input.MaxArea = 50; break;
                case "size 5": input.SizeConstant = 5; break;
                case "size function": input.SizeFunction = (x, y) => Q.FromDouble(5); break;
                case "conforming": input.Conforming = true; break;
                case "cq angle 20": input.ConstrainedQuality = true; input.MinAngleDeg = 20; break;
                case "cq area 50": input.ConstrainedQuality = true; input.MaxArea = 50; break;
                case "cq region area 50": input.ConstrainedQuality = true; input.Regions.Add(new RegionInput { Seed = new[] { 5.0, -1 }, Label = 1, MaxArea = 50 }); break;
                case "cq angle 0": input.ConstrainedQuality = true; input.MinAngleDeg = 0; break;
                case "cq conforming": input.ConstrainedQuality = true; input.Conforming = true; break;
                case "Steiner limit": input.SteinerLimit = 5; break;
                case "none": break;
                case "M2 angle 20": milestone = Milestone.M2; break;
            }
            var o = new QualityMesh2Candidate().Run(input);
            if (options == "M2 angle 20") input.MinAngleDeg = 20;
            Assert.Equal(Mesh2Status.Success, o.Status);
            var r = Oracle.Check(input, o, new OracleExpect { Milestone = milestone });
            // (at M2 the oracle also holds the unrefined mesh to the angle: only the hull model is judged there)
            if (milestone == Milestone.M3) Assert.True(r.Ok, options + ": " + r);
            Assert.Equal(rounded, r.RestorePixels.Contains((4, 1)));
        }

        static Mesh2Input Soup(int seed)
        {
            var rnd = new System.Random(seed);
            int n = 3 + rnd.Next(6);
            var xs = new List<double>(); var ys = new List<double>();
            var soup = new Mesh2Input { SnapTolerance = 1 };
            for (int i = 0; i < n; ++i) {
                for (int k = 0; k < 2; ++k) { xs.Add(rnd.Next(25) * 0.25); ys.Add(rnd.Next(25) * 0.25); }
                if (xs[2 * i] != xs[2 * i + 1] || ys[2 * i] != ys[2 * i + 1]) soup.Segments.Add(new SegmentInput(2 * i, 2 * i + 1));
            }
            soup.X = xs.ToArray(); soup.Y = ys.ToArray();
            return soup;
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
            int hullRuns = 0, starRuns = 0;
            for (int seed = 0; seed < 300; ++seed) {
                var star = StarPolygonFuzz.Make(seed);
                var ring = new Mesh2Input { X = star.X, Y = star.Y, SnapTolerance = 1 };
                ring.Rings.AddRange(star.Rings);
                ring.Segments.AddRange(star.Segments);
                var soup = Soup(seed);
                foreach (var (name, input) in new[] { ("star", ring), ("soup", soup) }) {
                    var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
                    if (o.Status != Mesh2Status.Success) continue;
                    var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2, ReportSnappedArea = true });
                    if (!report.Ok) continue;
                    if (name == "soup") ++hullRuns; else ++starRuns;
                    if (report.MeshMinusSnappedArea == null || report.MeshMinusSnappedArea.Value.Sign != 0)
                        wrong.Add(name + " " + seed + ": " + report.MeshMinusSnappedArea);
                }
            }
            Assert.True(wrong.Count == 0, wrong.Count + " differ:\n" + string.Join("\n", wrong.Take(20)));
            Assert.True(hullRuns >= 250 && starRuns >= 250, "hull runs " + hullRuns + ", star runs " + starRuns);
        }
    }
}
