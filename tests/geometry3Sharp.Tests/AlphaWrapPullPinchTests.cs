using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAlphaWrap.PullPinchPoints (opt-in, docs/design/ALPHA_WRAP_MANIFOLD_QUALITY.md): the refinement at non-manifold spots
    /// also uses the offset projection of a cell's circumcenter pulled back until it lies the separation inside the circumsphere,
    /// at most 2 offsets from the input. The spots that the manifold step resolved with facets below the angle bound of the class
    /// doc become manifold by carving.
    /// </summary>
    public class AlphaWrapPullPinchTests
    {
        // the smallest angle (degrees) of every result triangle and the number below the bound asin(min(offset, alpha - offset) / 2 alpha)
        // (less 1e-5 of the separation, as AlphaWrapTests.AssertSeparationBound)
        static (double min, int below, double bound) Angles(MeshAlphaWrap w)
        {
            double alpha = w.UsedAlpha, off = w.UsedOffset;
            double lo = Math.Asin((1 - 1e-5) * Math.Min(off, alpha - off) / (2 * alpha)) * 180 / Math.PI;
            double min = 180; int below = 0;
            foreach (int tid in w.Result.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                w.Result.GetTriVertices(tid, ref a, ref b, ref c);
                Func<Vector3d, Vector3d, double> ang = (u, v) => Math.Atan2(u.Cross(v).Length, u.Dot(v)) * 180 / Math.PI;
                double t = Math.Min(ang(b - a, c - a), Math.Min(ang(a - b, c - b), ang(a - c, b - c)));
                min = Math.Min(min, t);
                if (t < lo) below++;
            }
            return (min, below, lo);
        }

        static long Count(MeshAlphaWrap w, string key) { return w.Counts.TryGetValue(key, out long n) ? n : 0; }

        static MeshAlphaWrap Wrap(DMesh3 input, double relAlpha, double relOffset, bool pull, int roundWindow = 256, int threads = 0)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset, PullPinchPoints = pull, RoundWindow = roundWindow, MaxThreads = threads };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        // the #919 instance (AlphaWrapRoundsTests.Rounds_DefaultPath_Soup42_QualityPinned: 4 triangles below the bound, 2.12 degrees,
        // without the option): with it no triangle below the bound, the pinches resolved by carving
        [Fact]
        public void Soup42_RoundsPath_NoTriangleBelowTheAngleBound()
        {
            DMesh3 m = AlphaWrapTests.RandomSoup(42);
            MeshAlphaWrap w = Wrap(m, 20, 200, true);
            AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(m), vertexBand: MeshAlphaWrap.PullMaxOffsets);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AlphaWrapTests.AssertSeparationBound(w, "soup 42");
            var (min, below, lo) = Angles(w);
            Assert.True(below == 0, $"{below} triangles below {lo} degrees, min {min}");
            Assert.True(Count(w, "pinch_pulled_points") > 0);
            Assert.True(Count(w, "manifold_flips") <= 2, $"{Count(w, "manifold_flips")} manifold flips");
        }

        // the soups whose wrap has a triangle below the bound without the option (a sweep of soups 1..60 at three alphas, both
        // carvings, docs/design/ALPHA_WRAP_MANIFOLD_QUALITY.md): none with it; the result clean, the separation kept, the vertices
        // in the band [offset, 2 offset]
        public static IEnumerable<object[]> BelowOnMaster()
        {
            yield return new object[] { 26, 20.0, 200.0, 256 };
            yield return new object[] { 29, 20.0, 200.0, 0 };
            yield return new object[] { 51, 20.0, 200.0, 256 };
            yield return new object[] { 52, 20.0, 200.0, 0 };
            yield return new object[] { 46, 40.0, 400.0, 256 };
            yield return new object[] { 4, 60.0, 600.0, 0 };
        }

        [Theory]
        [MemberData(nameof(BelowOnMaster))]
        public void Soups_BelowTheBoundWithout_NoneWith(int seed, double relAlpha, double relOffset, int roundWindow)
        {
            DMesh3 m = AlphaWrapTests.RandomSoup(seed);
            var (_, belowOff, _) = Angles(Wrap(m, relAlpha, relOffset, false, roundWindow));
            Assert.True(belowOff > 0, $"soup {seed}: no triangle below the bound without the option (the case lost its power)");
            MeshAlphaWrap w = Wrap(m, relAlpha, relOffset, true, roundWindow);
            AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(m), vertexBand: MeshAlphaWrap.PullMaxOffsets);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AlphaWrapTests.AssertSeparationBound(w, "soup " + seed);
            var (min, below, lo) = Angles(w);
            Assert.True(below == 0, $"soup {seed}: {below} triangles below {lo} degrees, min {min}");
        }

        // the band of the relaxed promise: every vertex between the offset and 2 offsets from the input (an independent
        // brute-force distance), some of them strictly off the offset surface (the pulled points are on the result), none
        // without the option
        [Fact]
        public void Band_VerticesBetweenOffsetAndTwoOffsets()
        {
            // (soup 28: without the limit of 2 offsets, a pulled point 3.3 offsets out is on the result)
            DMesh3 m = AlphaWrapTests.RandomSoup(28);
            MeshAlphaWrap w = Wrap(m, 20, 200, true);
            double off = w.UsedOffset;
            Func<Vector3d, double> dist = p => {
                // (the soup's triangles are not degenerate; the brute-force minimum over them)
                double best = double.MaxValue;
                foreach (int tid in m.TriangleIndices())
                    best = Math.Min(best, Math.Sqrt(MeshQueries.TriDistanceSqr(m, tid, p)));
                return best;
            };
            int off1 = 0;
            double max = 0;
            foreach (Vector3d v in w.Result.Vertices()) {
                double d = dist(v);
                Assert.True(d >= off * (1 - 1e-6), $"vertex {d / off} offsets from the input");
                Assert.True(d <= MeshAlphaWrap.PullMaxOffsets * off * (1 + 1e-6), $"vertex {d / off} offsets from the input");
                if (d > 1.01 * off) off1++;
                max = Math.Max(max, d);
            }
            Assert.True(off1 > 0 && max > 1.5 * off, $"{off1} vertices off the offset surface, max {max / off} offsets");
            MeshAlphaWrap w0 = Wrap(m, 20, 200, false);
            foreach (Vector3d v in w0.Result.Vertices())
                Assert.Equal(off, dist(v), off * 1e-6);
        }

        // invariance over Similarity.All(): the 2^k scales give the same wrap, scaled, bit for bit; every transform a clean wrap
        // with the separation bound and pulled points; no triangle below the angle bound in the frames without a rotation
        // (the identity's wrap, computed once for the frames)
        static readonly Lazy<MeshAlphaWrap> Identity = new Lazy<MeshAlphaWrap>(() => Wrap(AlphaWrapTests.RandomSoup(42), 20, 200, true, threads: 4));

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance_AllSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = AlphaWrapTests.RandomSoup(42);
            MeshAlphaWrap w0 = Identity.Value;
            var w = new MeshAlphaWrap(Invariance.Transformed(input, s)) { RelativeAlpha = 20, RelativeOffset = 200, PullPinchPoints = true, MaxThreads = 4 };
            if (s.IsRotated) {
                w.Alpha = s.ApplyLength(w0.UsedAlpha);
                w.Offset = s.ApplyLength(w0.UsedOffset);
            }
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AlphaWrapTests.AssertSeparationBound(w, s.Name);
            Assert.True(Count(w, "pinch_pulled_points") > 0, s.Name);
            // (no angle guarantee on the facets of the flips the manifold step still makes: a rotated frame rounds to another
            // triangulation, and in the extreme frame (rotated, 1e-3, utm) 9 cells get no point within the band, 2 flips remain and
            // 1 triangle is below the bound, 0 without the option there; over the 8 frames 1 against 24. The frames of the 2^k
            // scales and the translation give the identity's triangulation: none below)
            if (!s.IsRotated) {
                var (min, below, lo) = Angles(w);
                Assert.True(below == 0, $"{s.Name}: {below} triangles below {lo} degrees, min {min}");
            }
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(w0.Result, Invariance.MappedBack(w.Result, s)));
        }

        // reuse (Compute twice on one instance) and determinism: one thread = several threads, repeated runs, bit for bit
        [Fact]
        public void Reuse_And_SameResultForEveryThreadCount()
        {
            DMesh3 input = AlphaWrapTests.RandomSoup(29);
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 20, RelativeOffset = 200, PullPinchPoints = true, MaxThreads = 4 };
            Assert.True(w.Compute(), w.Error);
            string first = AlphaWrapRoundsTests.Fingerprint(w);
            Assert.True(Count(w, "pinch_pulled_points") > 0);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(first, AlphaWrapRoundsTests.Fingerprint(w));
            foreach (int threads in new[] { 1, 2, 4 })
                Assert.Equal(first, AlphaWrapRoundsTests.Fingerprint(Wrap(input, 20, 200, true, threads: threads)));
        }

        // the flat point sets of AlphaWrapCornerPinchPointsTests, whose first box puts corners on the result without the option:
        // with RefineNonManifold off the retry refines at the pinches anyway, and the pulled candidate comes before the corner
        // exception there. A clean result with the vertices in the band, pulled points, the same for one thread and several
        [Theory]
        [InlineData(129, false)]
        [InlineData(104, true)]
        public void FlatInput_Retry_PulledPointsInTheBand(int seed, bool refine)
        {
            Func<int, MeshAlphaWrap> make = threads => {
                uint state = (uint)seed * 2654435761u + 977u;
                Func<double> unit = () => { state = state * 1664525u + 1013904223u; return ((state >> 8) & 0xFFFFFF) / (double)0x1000000; };
                var pts = new List<Vector3d>();
                for (int i = 0; i < 30; ++i) pts.Add(new Vector3d(2 * unit() - 1, 2 * unit() - 1, 0));
                return new MeshAlphaWrap(pts) { RelativeAlpha = 5, RelativeOffset = 600, RefineNonManifold = refine, PullPinchPoints = true, MaxThreads = threads };
            };
            MeshAlphaWrap w = make(1);
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            // (refine off: the first box puts corners on the result, the retry refines; refine on: without the option the first box
            // fails as well (AlphaWrapCornerPinchPointsTests), with it the pulled points resolve the pinches there: no retry)
            Assert.Equal(refine ? 0 : 1, Count(w, "box_retries"));
            Assert.True(Count(w, "pinch_pulled_points") > 0);
            Assert.Equal(w.Counts.TryGetValue("pinch_corner_points", out long k) ? k : 0, w.CornerPinchPoints);
            AlphaWrapTests.CheckWrap(w, w.Points, vertexBand: MeshAlphaWrap.PullMaxOffsets);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            MeshAlphaWrap w4 = make(4);
            Assert.True(w4.Compute(), w4.Error);
            Assert.Equal(AlphaWrapRoundsTests.Fingerprint(w), AlphaWrapRoundsTests.Fingerprint(w4));
        }
    }
}
