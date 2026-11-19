using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// R15 (docs/ROBUSTNESS_SWEEP.md): MeshIsoCurves with extreme field values (the interpolation denominator overflowed,
    /// sign products underflowed) .
    /// </summary>
    public class MeshIsoCurvesExtremeFieldTests
    {
        public static readonly MeshIsoCurves.RootfindingModes[] Modes =
            { MeshIsoCurves.RootfindingModes.SingleLerp, MeshIsoCurves.RootfindingModes.LerpSteps, MeshIsoCurves.RootfindingModes.Bisection };

        public static IEnumerable<object[]> ModeData() => Modes.Select(m => new object[] { m });

        // triangle (0,0,0) (1,0,0) (0,1,0) with the linear field a (1 - 2x): values (+a, -a, +a); the curve is the
        // edge between the midpoints (0.5, 0, 0) and (0.5, 0.5, 0)
        public static DMesh3 TriangleFor(Similarity s) => Triangle(s);

        static DMesh3 Triangle(Similarity s)
        {
            var m = new DMesh3();
            m.AppendVertex(s.Apply(new Vector3d(0, 0, 0))); m.AppendVertex(s.Apply(new Vector3d(1, 0, 0))); m.AppendVertex(s.Apply(new Vector3d(0, 1, 0)));
            m.AppendTriangle(0, 1, 2);
            return m;
        }

        static MeshIsoCurves Run(double a, MeshIsoCurves.RootfindingModes mode, Similarity s = null)
        {
            s = s ?? Similarity.Identity;
            var iso = new MeshIsoCurves(Triangle(s), p => a * (1 - 2 * s.Inverse(p).x)) { RootMode = mode };
            iso.Compute();
            return iso;
        }

        static Vector3d[] Points(MeshIsoCurves iso, Similarity s)
        {
            var pts = iso.Graph.VertexIndices().Select(v => s.Inverse(iso.Graph.GetVertex(v))).ToList();
            pts.Sort((p, q) => p.y.CompareTo(q.y));
            return pts.ToArray();
        }

        static void AssertMidpointEdge(MeshIsoCurves iso, string what, bool exactMidpoints = true)
        {
            Assert.True(iso.Graph.VertexCount == 2 && iso.Graph.EdgeCount == 1, $"{what}: {iso.Graph.VertexCount} vertices, {iso.Graph.EdgeCount} edges");
            var pts = Points(iso, Similarity.Identity);
            // (Bisection stops after RootModeSteps halvings: the same points for every value, not the midpoints)
            Vector3d[] expected = exactMidpoints ? new[] { new Vector3d(0.5, 0, 0), new Vector3d(0.5, 0.5, 0) } : Points(Run(1, iso.RootMode), Similarity.Identity);
            Assert.True(pts[0] == expected[0], $"{what}: {pts[0]}");
            Assert.True(pts[1] == expected[1], $"{what}: {pts[1]}");
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void ExtremeValues_GiveTheTopologyOfValueOne(MeshIsoCurves.RootfindingModes mode)
        {
            foreach (double a in new[] { 1.0, 1e308, 1.7976931348623157e308, 1e-200, 1e-160, 1e-300, 1e150 })
                AssertMidpointEdge(Run(a, mode), $"{mode} a = {a}", mode != MeshIsoCurves.RootfindingModes.Bisection);
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void ExtremeValues_AreInvariantUnderPowerOfTwoFieldScaling(MeshIsoCurves.RootfindingModes mode)
        {
            Vector3d[] reference = Points(Run(1, mode), Similarity.Identity);
            // from far below the underflow of a product (2^-540) to the overflow of a difference (2^1023)
            for (int k = -540; k <= 1023; k += 7) {
                double a = Math.Pow(2, k);
                var iso = Run(a, mode);
                Vector3d[] pts = Points(iso, Similarity.Identity);
                Assert.True(pts.Length == reference.Length, $"2^{k}: {pts.Length} vertices");
                for (int i = 0; i < pts.Length; ++i)
                    Assert.True(pts[i] == reference[i], $"2^{k} point {i}: {pts[i]}");
            }
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void AllSimilarities_GiveTheSameCurve(MeshIsoCurves.RootfindingModes mode)
        {
            foreach (double a in new[] { 1.0, 1e308, 1e-200 }) {
                Vector3d[] reference = Points(Run(a, mode), Similarity.Identity);
                foreach (Similarity s in Similarity.All()) {
                    var iso = Run(a, mode, s);
                    Assert.True(iso.Graph.VertexCount == 2 && iso.Graph.EdgeCount == 1, $"{s.Name}, a = {a}: topology");
                    Vector3d[] pts = Points(iso, s);
                    for (int i = 0; i < pts.Length; ++i) {
                        if (s.IsExact)
                            Assert.True(pts[i] == reference[i], $"{s.Name}, a = {a}: bits of point {i}");
                        else
                            Assert.True(pts[i].Distance(reference[i]) < 1e3 * s.Resolution(1), $"{s.Name}, a = {a}: point {i}");
                    }
                }
            }
        }

        [Fact]
        public void OnEdgeAndEdgeVertexCases_WithTinyValues_KeepTheirTopology()
        {
            // values (0, -tiny, +tiny): the product of the three underflows to 0 and sent every triangle to the vertex case
            foreach (double a in new[] { 1.0, 1e-200 }) {
                var m = new DMesh3();
                m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
                m.AppendTriangle(0, 1, 2);
                double[] f = { 0, -a, a };
                var iso = new MeshIsoCurves(m, p => 0) { VertexValueF = v => f[v] };
                iso.Compute();
                Assert.True(iso.Graph.EdgeCount == 1 && iso.Graph.VertexCount == 2, $"a = {a}");
                Assert.True(iso.Graph.VertexIndices().Select(v => iso.Graph.GetVertex(v)).Any(p => p == new Vector3d(0, 0, 0)));
                Assert.True(iso.Graph.VertexIndices().Select(v => iso.Graph.GetVertex(v)).Any(p => p == new Vector3d(0.5, 0.5, 0)), "a = " + a);
            }
        }

        [Theory]
        [InlineData(MeshIsoCurves.RootfindingModes.SingleLerp)]
        [InlineData(MeshIsoCurves.RootfindingModes.LerpSteps)]
        public void AsymmetricOverflowingBracket_KeepsTheCrossingPosition(MeshIsoCurves.RootfindingModes mode)
        {
            // values (-a, +3a, ...) along x: the crossing is at 0.25, also when 4a overflows (a = 4e307 and 1.7e308 / 3)
            Vector3d Cross(double a)
            {
                var iso = new MeshIsoCurves(Triangle(Similarity.Identity), p => a * (4 * p.x - 1)) { RootMode = mode };
                iso.Compute();
                return iso.Graph.VertexIndices().Select(v => iso.Graph.GetVertex(v)).Where(q => q.y == 0 && q.x < 1).Single();
            }
            Vector3d reference = Cross(1);
            Assert.True(reference.x == 0.25);
            foreach (double a in new[] { 4e307, 5.5e307, Math.Pow(2, 1021), 1e-310, Math.Pow(2, -1070) })
                Assert.True(Cross(a) == reference, "a = " + a);
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void ZeroVertexWithTinyAndHugeValues_KeepsItsTopologyInEveryMode(MeshIsoCurves.RootfindingModes mode)
        {
            // linear field a (y - x): values (0, -a, +a), a vertex on the curve and one crossing on the opposite edge
            foreach (double a in new[] { 1.0, 1e-200, 1e308 }) {
                var iso = new MeshIsoCurves(Triangle(Similarity.Identity), p => a * (p.y - p.x)) { RootMode = mode };
                iso.Compute();
                Assert.True(iso.Graph.EdgeCount == 1 && iso.Graph.VertexCount == 2, $"{mode}, a = {a}");
                }
        }
    }
}
