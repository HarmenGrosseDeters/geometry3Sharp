using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #986 (owner decision 2026-11-17, option A): an input narrower than the automatic tolerance of the 2D meshers (2^-41 of the larger of
    /// its largest coordinate and its extent, spec O11) collapses when its points merge, and the result is Empty. That is the documented
    /// limit, and the result says so in a diagnostic. The repro of the issue: the unit pentagon with x scaled by 2^e is Empty from |e| = 41 on.
    /// </summary>
    public class QualityMesh2ToleranceLimitTests
    {
        const string Marker = "no triangle is left, though the input is not on one line";

        /// <summary> the pentagon of #986: x of every point multiplied by sx, y by sy (exact for powers of two) </summary>
        static ConstrainedMesh2Input Pentagon(double sx, double sy = 1, Func<Vector2d, Vector2d> map = null)
        {
            var input = new ConstrainedMesh2Input();
            double[] x = { 0, 1, 1, 0.5, 0 }, y = { 0, 0, 0.6, 1, 1 };
            for (int i = 0; i < 5; ++i) {
                var p = new Vector2d(x[i] * sx, y[i] * sy);
                input.Points.Add(map == null ? p : map(p));
            }
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4 }));
            return input;
        }

        static QualityMesh2 Mesher() => new QualityMesh2 { MinAngle = 20, SteinerLimit = 300 };

        static bool OnOneLine(IList<Vector2d> p) => p.All(q => ExactPredicates.Orient2d(p[0], p.First(z => z != p[0]), q) == 0);

        static bool HasMarker(ConstrainedTriangulation2Result r) => r.Diagnostics.Any(d => d.StartsWith(Marker, StringComparison.Ordinal));

        static void AssertDocumentedEmpty(ConstrainedTriangulation2Result r, string what)
        {
            Assert.True(r.Status == Triangulation2Status.Empty, what + ": " + r.Status + " " + r.FailureKind);
            Assert.Equal(0, r.Mesh.TriangleCount);
            Assert.True(HasMarker(r), what + ": no diagnostic of the collapse; diagnostics: " + string.Join(" / ", r.Diagnostics));
        }

        /// <summary> the issue's repro, both directions: Empty, with the diagnostic that names the automatic tolerance </summary>
        [Theory]
        [InlineData(-46)]
        [InlineData(46)]
        public void Pentagon_AtAspect2Pow46_IsEmptyWithTheToleranceDiagnostic(int e)
        {
            var r = Mesher().Compute(Pentagon(Math.Pow(2, e)));
            AssertDocumentedEmpty(r, "2^" + e);
            // the tolerance in the diagnostic is 2^-41 of the magnitude (1 for e < 0, 2^e for e > 0), exactly
            double magnitude = Math.Max(1, Math.Pow(2, e));
            string tol = (magnitude * ConstrainedDelaunayMesh2.AutoToleranceFactor).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains(r.Diagnostics, d => d.StartsWith(Marker, StringComparison.Ordinal) && d.Contains(" " + tol + " "));
        }

        /// <summary>
        /// the sweep of exponents around the limit, both meshers, both directions: a mesh (and no such diagnostic) up to 2^40, the documented
        /// Empty from 2^41 on (the merge is at a distance of at most the tolerance, so 2^-41 itself merges)
        /// </summary>
        [Fact]
        public void Pentagon_SweepOfExponents_MeshesUpTo2Pow40_AndIsTheDocumentedEmptyFrom2Pow41()
        {
            foreach (int sign in new[] { -1, 1 })
                for (int a = 36; a <= 52; ++a) {
                    int e = sign * a;
                    var input = Pentagon(Math.Pow(2, e));
                    var quality = Mesher().Compute(input);
                    var cdt = new ConstrainedDelaunayMesh2().Compute(input);
                    foreach (var (name, r) in new[] { ("QualityMesh2", quality), ("ConstrainedDelaunayMesh2", cdt) }) {
                        if (a <= 40) {
                            Assert.True(r.Status == Triangulation2Status.Success, $"{name} 2^{e}: {r.Status} {r.FailureKind}");
                            Assert.True(r.Mesh.TriangleCount > 0);
                            Assert.False(HasMarker(r), $"{name} 2^{e}: the collapse diagnostic on a mesh");
                        }
                        else AssertDocumentedEmpty(r, $"{name} 2^{e}");
                    }
                }
        }

        /// <summary>
        /// a sliver that collapses without any merge of two points: the apex of a triangle within the tolerance of its base merges onto the
        /// base (O11), and the domain is empty; the same diagnostic
        /// </summary>
        [Fact]
        public void Sliver_ApexWithinTheToleranceOfTheBase_IsTheDocumentedEmpty()
        {
            foreach (int e in new[] { 40, 41, 50 }) {
                var input = new ConstrainedMesh2Input();
                input.Points.Add(new Vector2d(0, 0)); input.Points.Add(new Vector2d(1, 0)); input.Points.Add(new Vector2d(0.5, Math.Pow(2, -e)));
                input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2 }));
                foreach (var r in new[] { Mesher().Compute(input), new ConstrainedDelaunayMesh2().Compute(input) }) {
                    Assert.DoesNotContain(r.Diagnostics, d => d.Contains("were merged into it"));
                    if (e == 40) { Assert.True(r.Status == Triangulation2Status.Success, "2^-40: " + r.Status); Assert.False(HasMarker(r)); }
                    else AssertDocumentedEmpty(r, "2^-" + e);
                }
            }
        }

        /// <summary> the diagnostic is about a collapse: an input on one line is Empty without it, and so is a domain removed by a hole only
        /// when it is not on one line (the wording covers both) </summary>
        [Fact]
        public void Diagnostic_OnlyForAnInputNotOnOneLine()
        {
            var line = new ConstrainedMesh2Input();
            for (int i = 0; i < 4; ++i) line.Points.Add(new Vector2d(i, 2 * i));
            line.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            foreach (var r in new[] { Mesher().Compute(line), new ConstrainedDelaunayMesh2().Compute(line) }) {
                Assert.Equal(Triangulation2Status.Empty, r.Status);
                Assert.False(HasMarker(r));
            }
            // an outer ring on one line and a free point off it: the triangulation has area, the domain is empty, the ring has none
            line.Points.Add(new Vector2d(5, 0));
            foreach (var r in new[] { Mesher().Compute(line), new ConstrainedDelaunayMesh2().Compute(line) }) {
                Assert.Equal(Triangulation2Status.Empty, r.Status);
                Assert.False(HasMarker(r), string.Join(" / ", r.Diagnostics));
            }
            // a hole on the outer ring: the domain is removed, the input has area
            var holed = Pentagon(1);
            holed.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4 }, hole: true));
            var h = new ConstrainedDelaunayMesh2().Compute(holed);
            Assert.Equal(Triangulation2Status.Empty, h.Status);
            Assert.True(HasMarker(h));
            // a mesh never carries it
            Assert.False(HasMarker(Mesher().Compute(Pentagon(1))));
        }

        /// <summary> with a snap tolerance the snap lines report the collapse; the automatic tolerance's diagnostic is not added </summary>
        [Fact]
        public void Diagnostic_NotOnTheSnapGrid()
        {
            var r = new QualityMesh2 { MinAngle = 20, SteinerLimit = 300, SnapTolerance = Math.Pow(2, -20) }.Compute(Pentagon(Math.Pow(2, -30)));
            Assert.Equal(Triangulation2Status.Empty, r.Status);
            Assert.False(HasMarker(r));
            // a domain that a hole removes, on the snap grid: the builder sees an input with area and an empty domain, but snapped
            var holed = Pentagon(1);
            holed.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4 }, hole: true));
            foreach (var h in new[] { new QualityMesh2 { MinAngle = 20, SteinerLimit = 300, SnapTolerance = 1.0 / 64 }.Compute(holed),
                                      new ConstrainedDelaunayMesh2 { SnapTolerance = 1.0 / 64 }.Compute(holed) }) {
                Assert.Equal(Triangulation2Status.Empty, h.Status);
                Assert.False(HasMarker(h), string.Join(" / ", h.Diagnostics));
            }
        }

        /// <summary>
        /// invariance over the similarities of the plane: at 2^-46 the pentagon is the documented Empty under every one (the tolerance is
        /// relative, so translation and rotation do not lift the limit), and at 2^-20 it is a mesh; the 2^k scales give the same mesh bit for
        /// bit (vertices scaled exactly, the same triangles)
        /// </summary>
        [Fact]
        public void Pentagon_AtTheLimit_IsInvariantOverTheSimilarities()
        {
            var reference = Mesher().Compute(Pentagon(Math.Pow(2, -20)));
            Assert.Equal(Triangulation2Status.Success, reference.Status);
            int marked = 0;
            foreach (Similarity s in Similarity.All2D()) {
                foreach (var flat in new[] { Pentagon(Math.Pow(2, -46), 1, s.Apply2d), Pentagon(1, Math.Pow(2, -46), s.Apply2d) }) {
                    var r0 = Mesher().Compute(flat);
                    // (far from the origin, the UTM translation rounds the 2^-46 offsets away: the mapped input is on one line, a plain Empty)
                    if (OnOneLine(flat.Points)) { Assert.Equal(Triangulation2Status.Empty, r0.Status); Assert.False(HasMarker(r0)); }
                    else { AssertDocumentedEmpty(r0, s.Name + ", 2^-46"); ++marked; }
                }
                if (!s.IsExact || s.Translation != Vector3d.Zero) continue;
                var r = Mesher().Compute(Pentagon(Math.Pow(2, -20), 1, s.Apply2d));
                Assert.Equal(Triangulation2Status.Success, r.Status);
                Assert.Equal(reference.Mesh.Vertices.Count, r.Mesh.Vertices.Count);
                Assert.Equal(reference.Mesh.TriangleCount, r.Mesh.TriangleCount);
                for (int v = 0; v < r.Mesh.Vertices.Count; ++v)
                    Assert.Equal(s.Apply2d(reference.Mesh.Vertices[v]), r.Mesh.Vertices[v]);
                for (int t = 0; t < r.Mesh.TriangleCount; ++t)
                    Assert.Equal(reference.Mesh.Triangles[t], r.Mesh.Triangles[t]);
            }
            Assert.True(marked >= 12, marked + " of the flattened inputs not on one line");
        }

        /// <summary> reuse: one instance computes the limit case and a mesh twice, with the same results </summary>
        [Fact]
        public void Mesher_Reused_GivesTheSameResults()
        {
            var mesher = Mesher();
            var inputs = new[] { Pentagon(Math.Pow(2, -46)), Pentagon(Math.Pow(2, -8)) };
            var first = inputs.Select(i => mesher.Compute(i)).ToArray();
            var second = inputs.Select(i => mesher.Compute(i)).ToArray();
            for (int i = 0; i < inputs.Length; ++i) {
                Assert.Equal(first[i].Status, second[i].Status);
                Assert.Equal(first[i].Diagnostics, second[i].Diagnostics);
                Assert.Equal(first[i].Mesh.TriangleCount, second[i].Mesh.TriangleCount);
            }
            AssertDocumentedEmpty(second[0], "reused");
        }

        /// <summary> the oracle of the 2D meshers accepts the documented Empty (O11: a domain narrower than the tube may vanish) </summary>
        [Fact]
        public void Oracle_AcceptsTheDocumentedEmpty()
        {
            foreach (int e in new[] { -46, -41, 41, 46 }) {
                double s = Math.Pow(2, e);
                var input = new Mesh2Input {
                    X = new[] { 0, s, s, s * 0.5, 0 }, Y = new[] { 0, 0, 0.6, 1, 1.0 },
                    MinAngleDeg = 20, ConstrainedQuality = true, SteinerLimit = 300, SnapTolerance = 0,
                };
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
                Mesh2Output o = new QualityMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Empty, o.Status);
                OracleReport report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, $"2^{e}: " + report);
            }
        }
    }
}
