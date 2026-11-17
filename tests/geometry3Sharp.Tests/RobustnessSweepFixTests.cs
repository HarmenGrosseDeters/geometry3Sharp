using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Fixes of three findings of the robustness sweep (docs/ROBUSTNESS_SWEEP.md): R35 (MeshDeviation statistics at
    /// extreme sizes), R13 (MeshIterativeSmooth with a non-finite vertex) and R15 (MeshIsoCurves, an absolute tolerance
    /// on the field difference).
    /// </summary>
    public class RobustnessSweepFixTests
    {
        static DMesh3 Sphere(double r = 1, int n = 6) => Defects.Sphere(Vector3d.Zero, r, n);

        static DMesh3 Mapped(DMesh3 m, Similarity s, Vector3d offset)
        {
            DMesh3 c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, s.Apply(c.GetVertex(v) + offset));
            return c;
        }

        static Similarity Pow2(int k) => new Similarity("2^" + k, Math.Pow(2, k), Matrix3d.Identity, Vector3d.Zero);

        // ---- R35: DeviationStatistics and MeshDeviation ----

        [Fact]
        public void Statistics_ExtremeValuesAndWeights_ArePowerOfTwoInvariant()
        {
            double[] v = { -3, -1.5, 0.25, 2, 4.5, 7 };
            double[] w = { 1, 2, 0.5, 3, 1, 0.25 };
            var r = DeviationStatistics.Compute(v, w);
            foreach (int kv in new[] { 600, -1000, 1000, -1070 })
                foreach (int kw in new[] { 600, -1000, 1000 }) {
                    double sv = Math.Pow(2, kv), sw = Math.Pow(2, kw);
                    var s = DeviationStatistics.Compute(v.Select(x => x * sv).ToList(), w.Select(x => x * sw).ToList());
                    string c = $"values 2^{kv}, weights 2^{kw}";
                    Assert.True(s.Count == r.Count, c);
                    Assert.True(r.Mean * sv == s.Mean, c + " mean");
                    Assert.True(r.MeanAbsolute * sv == s.MeanAbsolute, c + " mean abs");
                    Assert.True(r.RMS * sv == s.RMS, c + " rms");
                    Assert.True(r.StdDev * sv == s.StdDev, c + " stddev");
                    Assert.True(r.Min * sv == s.Min && r.Max * sv == s.Max && r.MaxAbsolute * sv == s.MaxAbsolute, c + " range");
                    Assert.True(r.AbsolutePercentile(0.9) * sv == s.AbsolutePercentile(0.9), c + " percentile");
                }
        }

        [Fact]
        public void Statistics_NormalRange_KeepsTheTotalWeightAndTheMoments()
        {
            var s = DeviationStatistics.Compute(new double[] { -2, -1, 0, 1, 2, double.NaN }, new double[] { 1, 1, 1, 1, 4, 1 });
            Assert.Equal(8, s.TotalWeight);       // the sum of the weights, not of the scaled ones
            Assert.Equal(0.75, s.Mean, 12);       // (-2 - 1 + 0 + 1 + 8) / 8
            Assert.Equal(Math.Sqrt((4 + 1 + 0 + 1 + 16) / 8.0), s.RMS, 12);
            Assert.Equal(Math.Sqrt(2.1875), s.StdDev, 12);
        }

        // the reference sphere is shifted by offset before the map: Mapped applies the offset before the scale
        static MeshDeviation Deviation(Similarity s, bool bounds = false, double tolerance = 0)
        {
            DMesh3 m = Mapped(Sphere(), s, Vector3d.Zero);
            DMesh3 reference = Mapped(Sphere(1.05, 5), s, new Vector3d(0.02, 0, 0));
            var d = new MeshDeviation(m, reference) { ComputeCertifiedBound = bounds, ComputeTriangleBounds = bounds, CertifiedTolerance = tolerance * s.Scale };
            Assert.True(d.Compute());
            return d;
        }

        [Fact]
        public void Deviation_AllSimilarities_GiveTheSameStatistics()
        {
            MeshDeviation r = Deviation(Similarity.Identity);
            DMesh3 input = Sphere();
            foreach (Similarity s in Similarity.All()) {
                MeshDeviation d = Deviation(s);
                double tol = Math.Max(1e-5 * r.Statistics.MaxAbsolute, s.DistanceResolution(1) / 1000);
                Assert.Equal(r.Statistics.Count, d.Statistics.Count);
                Assert.True(Math.Abs(d.Statistics.MaxAbsolute / s.Scale - r.Statistics.MaxAbsolute) < tol, s.Name + " max");
                Assert.True(Math.Abs(d.Statistics.Mean / s.Scale - r.Statistics.Mean) < tol, s.Name + " mean");
                Assert.True(Math.Abs(d.Statistics.RMS / s.Scale - r.Statistics.RMS) < tol, s.Name + " rms");
                foreach (int v in input.VertexIndices())
                    Assert.True(Math.Abs(d.VertexDeviation[v] / s.Scale - r.VertexDeviation[v]) < tol, s.Name + " vertex " + v);
                if (s.IsExact) {     // the 2^k scales: bit for bit
                    Assert.True(r.Statistics.Mean * s.Scale == d.Statistics.Mean, s.Name + " mean bits");
                    Assert.True(r.Statistics.StdDev * s.Scale == d.Statistics.StdDev, s.Name + " stddev bits");
                    Assert.True(r.Statistics.MaxAbsolute * s.Scale == d.Statistics.MaxAbsolute, s.Name + " max bits");
                    foreach (int v in input.VertexIndices())
                        Assert.True(r.VertexDeviation[v] * s.Scale == d.VertexDeviation[v], s.Name + " vertex bits " + v);
                }
            }
        }

        [Theory]
        [InlineData(530)]       // 1e160: the squared distances overflow
        [InlineData(-530)]      // 1e-160: the weights (areas) underflow
        [InlineData(995)]       // 1e300
        [InlineData(-1010)]     // 1e-304 (1e-310 is in the sweep: subnormal coordinates are not exact)
        public void Deviation_ExtremePowerOfTwoScale_IsTheUnitResultBitForBit(int k)
        {
            Similarity s = Pow2(k);
            MeshDeviation r = Deviation(Similarity.Identity, true, 1e-2);
            MeshDeviation d = Deviation(s, true, 1e-2);
            Assert.True(d.Statistics.Count == r.Statistics.Count && r.Statistics.Count > 0, $"count {d.Statistics.Count} vs {r.Statistics.Count}");
            Assert.True(r.Statistics.TotalWeight * s.Scale * s.Scale == d.Statistics.TotalWeight || double.IsInfinity(d.Statistics.TotalWeight) || d.Statistics.TotalWeight == 0, "total weight (area units of the mesh)");
            Assert.True(Math.Abs(r.Statistics.TotalWeight * s.Scale * s.Scale / d.Statistics.TotalWeight - 1) < 1e-12 || double.IsInfinity(d.Statistics.TotalWeight) || d.Statistics.TotalWeight == 0, "total weight ratio");
            Assert.True(r.Statistics.Mean * s.Scale == d.Statistics.Mean, "mean");
            Assert.True(r.Statistics.StdDev * s.Scale == d.Statistics.StdDev, "stddev");
            Assert.True(r.Statistics.RMS * s.Scale == d.Statistics.RMS, "rms");
            Assert.True(r.Statistics.MaxAbsolute * s.Scale == d.Statistics.MaxAbsolute, "max");
            Assert.True(r.VertexStatistics.Mean * s.Scale == d.VertexStatistics.Mean, "vertex mean");
            Assert.True(r.Statistics.AbsolutePercentile(0.9) * s.Scale == d.Statistics.AbsolutePercentile(0.9), "p90");
            foreach (int v in r.Mesh.VertexIndices()) {
                Assert.True(r.VertexDeviation[v] * s.Scale == d.VertexDeviation[v], "deviation " + v);
                Assert.True(r.ClosestTriangle[v] == d.ClosestTriangle[v], "triangle " + v);
                Assert.True(r.ClosestPoint[v].x * s.Scale == d.ClosestPoint[v].x, "closest " + v);
            }
            Assert.True(r.HausdorffUpperBound * s.Scale == d.HausdorffUpperBound, "certified bound");
            Assert.True(r.HausdorffLowerBound * s.Scale == d.HausdorffLowerBound, "certified lower bound");
            foreach (int t in r.Mesh.TriangleIndices()) {
                Assert.True(r.TriangleDeviationUpper[t] * s.Scale == d.TriangleDeviationUpper[t], "triangle bound " + t);
                Assert.True(r.TriangleDeviationLower[t] * s.Scale == d.TriangleDeviationLower[t], "triangle lower " + t);
            }
            Assert.True(r.TriangleUpperStatistics.Mean * s.Scale == d.TriangleUpperStatistics.Mean, "triangle mean");
        }

        [Theory]
        [InlineData(MeshDeviation.Signs.Unsigned)]
        [InlineData(MeshDeviation.Signs.WindingNumber)]
        public void Deviation_ExtremeScale_OtherSigns_AreTheUnitResultBitForBit(MeshDeviation.Signs sign)
        {
            MeshDeviation[] pair = new MeshDeviation[2];
            Similarity[] sims = { Similarity.Identity, Pow2(530) };
            for (int i = 0; i < 2; ++i) {
                DMesh3 m = Mapped(Sphere(), sims[i], Vector3d.Zero);
                m.RemoveTriangle(m.TriangleIndices().First());      // a gap in the ids
                DMesh3 reference = Mapped(Sphere(1.05, 5), sims[i], new Vector3d(0.02, 0, 0));
                pair[i] = new MeshDeviation(m, reference) { Sign = sign };
                Assert.True(pair[i].Compute());
            }
            Assert.True(pair[0].Statistics.Count > 0 && pair[0].Statistics.Count == pair[1].Statistics.Count);
            foreach (int v in pair[0].Mesh.VertexIndices())
                Assert.True(pair[0].VertexDeviation[v] * sims[1].Scale == pair[1].VertexDeviation[v] || (double.IsNaN(pair[0].VertexDeviation[v]) && double.IsNaN(pair[1].VertexDeviation[v])), "deviation " + v);
            Assert.True(pair[0].Statistics.Mean * sims[1].Scale == pair[1].Statistics.Mean);
        }

        [Fact]
        public void Deviation_ExtremeScale_ReuseAndRepeatedRunsAgree()
        {
            Similarity s = Pow2(530);
            DMesh3 m = Mapped(Sphere(), s, Vector3d.Zero);
            DMesh3 reference = Mapped(Sphere(1.05, 5), s, new Vector3d(0.02, 0, 0));
            var d = new MeshDeviation(m, reference);
            Assert.True(d.Compute());
            double[] first = d.VertexDeviation.ToArray();
            double mean = d.Statistics.Mean;
            for (int run = 0; run < 3; ++run) {
                Assert.True(d.Compute());      // one instance, computed again
                Assert.True(first.SequenceEqual(d.VertexDeviation), "deviations run " + run);
                Assert.True(mean == d.Statistics.Mean, $"{mean} vs {d.Statistics.Mean}");
            }
        }

        // ---- R13: MeshIterativeSmooth with a non-finite vertex ----

        public static IEnumerable<object[]> SmoothCases()
        {
            foreach (var type in new[] { MeshIterativeSmooth.SmoothTypes.Uniform, MeshIterativeSmooth.SmoothTypes.Cotan, MeshIterativeSmooth.SmoothTypes.MeanValue })
                foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                    yield return new object[] { type, bad };
        }

        [Theory]
        [MemberData(nameof(SmoothCases))]
        public void IterativeSmooth_NonFiniteVertex_StaysAndIsNotSpread(MeshIterativeSmooth.SmoothTypes type, double bad)
        {
            DMesh3 m = Sphere();
            int badV = m.VertexIndices().OrderBy(v => v).ElementAt(m.VertexCount / 3);
            // the same problem with a finite value: the one-ring of the bad vertex (and the vertex) is not listed
            DMesh3 reference = new DMesh3(m);
            var ring = new HashSet<int>(m.VtxVerticesItr(badV)) { badV };
            int[] listed = m.VertexIndices().Where(v => ring.Contains(v) == false).ToArray();
            new MeshIterativeSmooth(reference, listed) { Alpha = 0.5, Rounds = 4, SmoothType = type }.Smooth();

            m.SetVertex(badV, new Vector3d(bad, 0, 0));
            Vector3d[] before = m.VertexIndices().Select(v => m.GetVertex(v)).ToArray();
            var smooth = new MeshIterativeSmooth(m, m.VertexIndices().ToArray()) { Alpha = 0.5, Rounds = 4, SmoothType = type };
            Assert.True(smooth.Smooth());

            Assert.True(m.GetVertex(badV).x.Equals(bad) || (double.IsNaN(bad) && double.IsNaN(m.GetVertex(badV).x)), "the bad vertex stays");
            foreach (int v in m.VertexIndices()) {
                if (v == badV) continue;
                Vector3d p = m.GetVertex(v);
                Assert.True(p.IsFinite, "vertex " + v + " is finite");
                // (neighbours of the bad vertex stay where they were: the reference does not list them; the others smooth as without it)
                Assert.True(p == reference.GetVertex(v), ring.Contains(v) ? "a neighbour of the bad vertex stays" : "the others smooth as without the bad vertex");
                if (ring.Contains(v))
                    Assert.True(p == before[v], "a neighbour is unchanged");
            }
        }

        [Theory]
        [MemberData(nameof(SmoothCases))]
        public void IterativeSmooth_NonFiniteVertex_WithProjection_FrozenVerticesAreNotProjected(MeshIterativeSmooth.SmoothTypes type, double bad)
        {
            // ProjectF runs for the vertices that smooth, not for the frozen ones (the bad vertex and its neighbours): they keep
            // their position, the others get the projected one (here: a shift by +1 in z of the smoothed position)
            DMesh3 m = Sphere();
            int badV = m.VertexIndices().OrderBy(v => v).ElementAt(m.VertexCount / 3);
            var ring = new HashSet<int>(m.VtxVerticesItr(badV)) { badV };
            DMesh3 reference = new DMesh3(m);
            int[] listed = m.VertexIndices().Where(v => ring.Contains(v) == false).ToArray();
            var shift = new Vector3d(0, 0, 1);
            new MeshIterativeSmooth(reference, listed) { Alpha = 0.5, Rounds = 1, SmoothType = type, ProjectF = (p, n, v) => p + shift }.Smooth();

            m.SetVertex(badV, new Vector3d(bad, 0, 0));
            Vector3d[] before = m.VertexIndices().Select(v => m.GetVertex(v)).ToArray();
            var calls = new System.Collections.Concurrent.ConcurrentBag<int>();
            var smooth = new MeshIterativeSmooth(m, m.VertexIndices().ToArray()) {
                Alpha = 0.5, Rounds = 1, SmoothType = type,
                ProjectF = (p, n, v) => { calls.Add(v); return p + shift; }
            };
            Assert.True(smooth.Smooth());

            Assert.Equal(m.VertexCount - ring.Count, calls.Count);
            Assert.DoesNotContain(calls, v => ring.Contains(v));
            foreach (int v in m.VertexIndices()) {
                if (ring.Contains(v)) {
                    Assert.True(v == badV ? double.IsNaN(bad) ? double.IsNaN(m.GetVertex(v).x) : m.GetVertex(v).x == bad : m.GetVertex(v) == before[v], "frozen vertex " + v);
                } else {
                    Assert.True(m.GetVertex(v).IsFinite);
                    Assert.True(m.GetVertex(v) == reference.GetVertex(v), "projected like the reference without the bad vertex");
                }
            }
        }

        [Fact]
        public void IterativeSmooth_FiniteInput_MovesEveryListedVertex()
        {
            DMesh3 m = Sphere();
            DMesh3 before = new DMesh3(m);
            new MeshIterativeSmooth(m, m.VertexIndices().ToArray()) { Alpha = 0.5, Rounds = 1 }.Smooth();
            Assert.True(m.VertexIndices().Count(v => m.GetVertex(v) != before.GetVertex(v)) > m.VertexCount / 2);
            // one round, uniform: every vertex goes half way to the centroid of the previous positions
            foreach (int v in m.VertexIndices()) {
                Vector3d c = Vector3d.Zero; before.VtxOneRingCentroid(v, ref c);
                Vector3d e = 0.5 * before.GetVertex(v) + 0.5 * c;
                Assert.True(m.GetVertex(v).Distance(e) < 1e-12);
            }
        }

        [Fact]
        public void IterativeSmooth_AllSimilarities_GiveTheSameResult()
        {
            DMesh3 input = Sphere();
            DMesh3 r = new DMesh3(input);
            new MeshIterativeSmooth(r, r.VertexIndices().ToArray()) { Alpha = 0.5, Rounds = 3 }.Smooth();
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(input, s);
                new MeshIterativeSmooth(m, m.VertexIndices().ToArray()) { Alpha = 0.5, Rounds = 3 }.Smooth();
                foreach (int v in input.VertexIndices()) {
                    Vector3d back = s.Inverse(m.GetVertex(v));
                    Assert.True(back.Distance(r.GetVertex(v)) < 100 * s.Resolution(1), s.Name);
                    if (s.IsExact)
                        Assert.True(r.GetVertex(v) * s.Scale == m.GetVertex(v), s.Name + " bits");
                }
            }
        }

        // ---- R15: MeshIsoCurves, relative tolerance on the field difference ----

        static (int vertices, double length, Vector3d[] points) IsoCurve(Similarity s, MeshIsoCurves.RootfindingModes mode)
        {
            DMesh3 m = Invariance.Transformed(Sphere(), s);
            Vector3d o = s.Apply(Vector3d.Zero), n = s.ApplyDirection(new Vector3d(1, 2, 3).Normalized);
            double level = 0.1 * s.Scale;
            var iso = new MeshIsoCurves(m, x => (x - o).Dot(n) - level) { RootMode = mode };
            iso.Compute();
            double len = iso.Graph.Edges().Sum(e => iso.Graph.GetVertex(e.a).Distance(iso.Graph.GetVertex(e.b)));
            return (iso.Graph.VertexCount, len, iso.Graph.VertexIndices().Select(v => s.Inverse(iso.Graph.GetVertex(v))).ToArray());
        }

        [Theory]
        [InlineData(MeshIsoCurves.RootfindingModes.SingleLerp)]
        [InlineData(MeshIsoCurves.RootfindingModes.LerpSteps)]
        [InlineData(MeshIsoCurves.RootfindingModes.Bisection)]
        public void IsoCurves_AllSimilarities_GiveTheSameCurve(MeshIsoCurves.RootfindingModes mode)
        {
            var r = IsoCurve(Similarity.Identity, mode);
            Assert.True(r.vertices > 10);
            var scales = Similarity.All().Concat(new[] { Pow2(-24), Pow2(-40), Pow2(40) });
            foreach (Similarity s in scales) {
                var c = IsoCurve(s, mode);
                Assert.True(c.vertices == r.vertices, $"{s.Name}: {c.vertices} vertices, {r.vertices} expected");
                Assert.True(Math.Abs(c.length / s.Scale - r.length) < 1e-6 * r.length, s.Name + " length");
                if (s.IsExact || s.Name.StartsWith("2^"))
                    for (int i = 0; i < r.points.Length; ++i)
                        Assert.True(c.points[i].Distance(r.points[i]) < 1e-12, s.Name + " point " + i);
            }
        }

        [Fact]
        public void IsoCurves_SmallFieldDifference_StillInterpolates()
        {
            // one triangle, the field changes by 1e-9 along the x edge (below the old absolute 1e-8): the crossing is half way, not at an end
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2);
            foreach (var mode in new[] { MeshIsoCurves.RootfindingModes.SingleLerp, MeshIsoCurves.RootfindingModes.LerpSteps }) {
                var iso = new MeshIsoCurves(m, p => (p.x - 0.25) * 1e-9) { RootMode = mode };
                iso.Compute();
                Assert.True(iso.Graph.VertexCount == 2, "two crossings");
                double xs = iso.Graph.VertexIndices().Select(v => iso.Graph.GetVertex(v)).Where(p => p.y == 0).Select(p => p.x).Single();
                Assert.Equal(0.25, xs, 9);
            }
        }

        [Fact]
        public void IsoCurves_EqualFieldValuesAtTheEnds_DoNotDivideByZero()
        {
            // both values 0 on an edge that the curve touches: no NaN position
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2);
            var iso = new MeshIsoCurves(m, p => p.y);
            iso.Compute();
            foreach (int v in iso.Graph.VertexIndices())
                Assert.True(iso.Graph.GetVertex(v).IsFinite);
        }
    }
}
