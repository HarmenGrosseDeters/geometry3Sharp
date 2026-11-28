using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for the hot-pixel spatial index, critical-edge deduplication, X-sweep crossings, and exact 64-bit/BigInteger
    /// contact paths in SnapRounding2 (#1402): oracle validity, large-span BigInteger fallback, Similarity.All2D, and
    /// reuse/parallel bit-identical determinism.
    /// </summary>
    public class SnapRounding2IndexTests
    {
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = 0x9E3779B97F4A7C15UL ^ seed; }
            public double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
        }

        static ConstrainedMesh2Input RandomSoup(int n, ulong seed = 1402)
        {
            var input = new ConstrainedMesh2Input();
            var rng = new Rng(seed);
            for (int i = 0; i < n; ++i) {
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(rng.Next(), rng.Next()));
                input.Points.Add(new Vector2d(rng.Next(), rng.Next()));
                input.Segments.Add(new Mesh2Segment(a, a + 1, (i % 7) + 1));
            }
            return input;
        }

        static ConstrainedMesh2Input TChain(int n)
        {
            var input = new ConstrainedMesh2Input();
            double ax = 0.0, ay = 0.1, bx = 1.0, by = 0.87;
            input.Points.Add(new Vector2d(ax, ay));
            input.Points.Add(new Vector2d(bx, by));
            input.Segments.Add(new Mesh2Segment(0, 1, 1));
            for (int i = 0; i < n; ++i) {
                double t = (i + 0.5) / n, px = ax + t * (bx - ax), py = ay + t * (by - ay), side = i % 2 == 0 ? 1 : -1;
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(px, py));
                input.Points.Add(new Vector2d(px - side * 0.0077, py + side * 0.01));
                input.Segments.Add(new Mesh2Segment(a, a + 1, 2));
            }
            return input;
        }

        static Mesh2Input ToOracleInput(ConstrainedMesh2Input src, double snapTol)
        {
            var pts = new double[2 * src.Points.Count];
            for (int i = 0; i < src.Points.Count; ++i) { pts[2 * i] = src.Points[i].x; pts[2 * i + 1] = src.Points[i].y; }
            var mi = Generators.Literal(pts);
            mi.SnapTolerance = snapTol;
            foreach (var s in src.Segments) mi.Segments.Add(new SegmentInput(s.A, s.B, s.Marker, s.Locked));
            return mi;
        }

        [Theory]
        [InlineData(1.0 / 1024.0)]
        [InlineData(1.0 / 1048576.0)]
        public void LongSegmentSoup_Over64Segments_PassesOracleAndMultiRoundRestore(double tol)
        {
            // n = 100 > 64 exercises the X-sweep AddCrossings path, k-d tree RouteAll, deduplicated Critical, and EdgeBVH Restore
            var input = RandomSoup(100);
            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void TChain250_Snapped_PassesOracle()
        {
            double tol = 1.0 / 1024.0;
            var input = TChain(250);
            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void LargePixelSpan_ExercisesBigIntegerFallbackPaths_AndPassesOracle()
        {
            // Span > 2^31 pixels (> SafeSpan = 2^29) exercises the BigInteger fallback in both Meets and MeetsPixelCentreInterior
            double tol = 1.0 / (1L << 32);
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0.0, 0.0));
            input.Points.Add(new Vector2d(1.0, 1.0));
            input.Points.Add(new Vector2d(0.0, 1.0));
            input.Points.Add(new Vector2d(1.0, 0.0));
            // near-collinear point half a pixel off the diagonal to trigger critical-pixel detection across > 2^31 pixels
            input.Points.Add(new Vector2d(0.5, 0.5 + 0.6 * tol));
            input.Points.Add(new Vector2d(0.25, 0.25 - 0.6 * tol));
            input.Segments.Add(new Mesh2Segment(0, 1, 1));
            input.Segments.Add(new Mesh2Segment(2, 3, 2));
            input.Segments.Add(new Mesh2Segment(4, 5, 3));

            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void LongSegmentSoup_Snapped_Similarity_All2D(string name)
        {
            var s = Similarity.All2D().First(x => x.Name == name);
            var ex = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(ex.x * ex.x + ex.y * ex.y), cos = ex.x / norm, sin = ex.y / norm;
            var raw = RandomSoup(70, seed: 2026);
            var transformed = new ConstrainedMesh2Input();
            foreach (var p in raw.Points)
                transformed.Points.Add(new Vector2d(
                    s.Scale * (cos * p.x - sin * p.y) + s.Translation.x,
                    s.Scale * (sin * p.x + cos * p.y) + s.Translation.y));
            foreach (var seg in raw.Segments) transformed.Segments.Add(seg);
            // keep pixel count within 2^40 even at UTM offsets (~5e6) and scale 1e-3
            double tol = Math.Max(s.Scale / 512.0, 1e-3);
            var mi = ToOracleInput(transformed, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, name + ": " + report);
        }

        [Fact]
        public void LongSegmentSoup_Snapped_ReuseAndParallel_AreBitIdentical()
        {
            double tol = 1.0 / 1024.0;
            var input = RandomSoup(80);
            var mesher = new ConstrainedDelaunayMesh2 { SnapTolerance = tol };
            var r1 = mesher.Compute(input);
            var r2 = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, r1.Status);
            Assert.Equal(Triangulation2Status.Success, r2.Status);
            AssertBitIdentical(r1.Mesh, r2.Mesh);

            var parallel = new ConstrainedTriangulation2Result[4];
            Parallel.For(0, 4, i => parallel[i] = new ConstrainedDelaunayMesh2 { SnapTolerance = tol }.Compute(input));
            foreach (var rp in parallel) AssertBitIdentical(r1.Mesh, rp.Mesh);
        }

        static void AssertBitIdentical(ConstrainedMesh2 a, ConstrainedMesh2 b)
        {
            Assert.Equal(a.Vertices.Count, b.Vertices.Count);
            for (int i = 0; i < a.Vertices.Count; ++i) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Vertices[i].x), BitConverter.DoubleToInt64Bits(b.Vertices[i].x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Vertices[i].y), BitConverter.DoubleToInt64Bits(b.Vertices[i].y));
            }
            Assert.Equal(a.Triangles.Count, b.Triangles.Count);
            for (int i = 0; i < a.Triangles.Count; ++i) Assert.Equal(a.Triangles[i], b.Triangles[i]);
            Assert.Equal(a.Subsegments.Count, b.Subsegments.Count);
            for (int i = 0; i < a.Subsegments.Count; ++i) {
                Assert.Equal(a.Subsegments[i].A, b.Subsegments[i].A);
                Assert.Equal(a.Subsegments[i].B, b.Subsegments[i].B);
                Assert.Equal(a.Subsegments[i].Marker, b.Subsegments[i].Marker);
            }
        }

        [Fact]
        public void HorizontalBundle_Over64Segments_TriggersYSweepAndPassesOracle()
        {
            // 100 nearly horizontal long segments spanning x in [0, 10] with narrow y-extents (> 50% wider in X than Y)
            // triggers the Y-sweep branch in SnapRounding2.AddCrossings.
            var input = new ConstrainedMesh2Input();
            var rng = new Rng(140299);
            int n = 100;
            for (int i = 0; i < n; ++i) {
                double y0 = i * 0.05;
                double y1 = y0 + (rng.Next() - 0.5) * 0.12;
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(0.0, y0));
                input.Points.Add(new Vector2d(10.0, y1));
                input.Segments.Add(new Mesh2Segment(a, a + 1, 1));
            }
            // Add a few steep crossers to produce crossings across the horizontal bundle.
            for (int k = 0; k < 4; ++k) {
                double x = 2.0 + 2.0 * k;
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(x, -0.1));
                input.Points.Add(new Vector2d(x + 0.3, n * 0.05 + 0.1));
                input.Segments.Add(new Mesh2Segment(a, a + 1, 2));
            }

            double tol = 1.0 / 512.0;
            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void OverflowSpan_1518500250Pixels_EndToEnd_DoesNotTriggerSpuriousCriticalPixel()
        {
            // End-to-end regression for the 2^29 guard in SnapRounding2.MeetsPixelCentreInterior:
            // At M = 1_518_500_250 (exact in double, pixel width = 1.0), 4*M^2 > long.MaxValue while 4*M^2 - 4*M < long.MaxValue.
            // Without the 2^29 guard, Critical() falsely flags (0, M) as crossing the interior of (0,0)-(M,M) and Restore() throws.
            const double M = 1_518_500_250.0;
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0.0, 0.0));
            input.Points.Add(new Vector2d(M, M));
            input.Points.Add(new Vector2d(0.0, M));
            input.Points.Add(new Vector2d(M, 0.0));
            input.Segments.Add(new Mesh2Segment(0, 1, 1));

            var sr = SnapRounding2.Apply(input, 1.0);
            Assert.Equal(1, sr.Rounds);
            Assert.Equal(0, sr.Crossings);
            Assert.Equal(0, sr.Spread);
            Assert.Equal(0, sr.ForcedSpread);
            Assert.Single(sr.Result.Segments);

            var res = new ConstrainedDelaunayMesh2 { SnapTolerance = 1.0 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            Assert.Equal(2, res.Mesh.Triangles.Count);
        }

        static ulong HashSnapResult(ulong h, SnapRounding2 sr)
        {
            static ulong Mix(ulong state, ulong v) => (state ^ v) * 1099511628211UL;
            static ulong MixLong(ulong state, long v) => Mix(state, unchecked((ulong)v));
            static ulong MixDouble(ulong state, double v) => MixLong(state, BitConverter.DoubleToInt64Bits(v));

            h = MixLong(h, sr.Rounds);
            h = MixLong(h, sr.Crossings);
            h = MixLong(h, sr.Spread);
            h = MixLong(h, sr.Widened);
            h = MixLong(h, sr.ForcedSpread);
            h = MixLong(h, sr.CollapsedSegments);
            h = MixLong(h, sr.CollapsedRings);
            h = MixLong(h, sr.DroppedSeeds);
            h = MixLong(h, sr.OuterRingsCollapsed ? 1L : 0L);
            h = MixDouble(h, sr.MaxDisplacement);

            var r = sr.Result;
            h = MixLong(h, r.Points.Count);
            foreach (var p in r.Points) { h = MixDouble(h, p.x); h = MixDouble(h, p.y); }
            h = MixLong(h, r.Segments.Count);
            foreach (var s in r.Segments) {
                h = MixLong(h, s.A);
                h = MixLong(h, s.B);
                h = MixLong(h, s.Marker);
                h = MixLong(h, s.Locked ? 1L : 0L);
            }
            h = MixLong(h, r.Rings.Count);
            foreach (var ring in r.Rings) {
                h = MixLong(h, ring.Hole ? 1L : 0L);
                h = MixLong(h, ring.Vertices.Length);
                foreach (int v in ring.Vertices) h = MixLong(h, v);
            }
            h = MixLong(h, sr.Diagnostics.Count);
            foreach (var d in sr.Diagnostics) {
                h = MixLong(h, d.Length);
                foreach (char ch in d) h = MixLong(h, ch);
            }
            return h;
        }

        [Fact]
        public void SnapRounding2_ProceduralCorpus_MatchesGoldenBitIdenticalHash()
        {
            // Pins exact bit-for-bit output (points, segments, rings, counters, diagnostics) across:
            // 1) Random soup at two tolerances (X-sweep + multi-round Restore/Free choices),
            // 2) Horizontal bundle (Y-sweep),
            // 3) T-chain (dense collinear contacts + Restore),
            // 4) Near-collinear crossing where double Orient has the WRONG nonzero sign (kills OrientFilter bypass),
            // 5) Rings + repeated segments at a non-power-of-two tolerance (0.003).
            ulong h = 14695981039346656037UL;

            // 1. Random soup at 2^-10 and 2^-20 (multi-round Restore/Free).
            var soup = RandomSoup(100, seed: 1402);
            var srSoupCoarse = SnapRounding2.Apply(soup, 1.0 / 1024.0);
            Assert.True(srSoupCoarse.Rounds >= 2 && srSoupCoarse.Spread > 0, "Expected multi-round Restore on RandomSoup(100)");
            h = HashSnapResult(h, srSoupCoarse);
            h = HashSnapResult(h, SnapRounding2.Apply(soup, 1.0 / 1048576.0));

            // 2. Horizontal bundle (triggers Y-sweep).
            var hBundle = new ConstrainedMesh2Input();
            var rng = new Rng(140299);
            for (int i = 0; i < 80; ++i) {
                double y0 = i * 0.05, y1 = y0 + (rng.Next() - 0.5) * 0.12;
                int a = hBundle.Points.Count;
                hBundle.Points.Add(new Vector2d(0.0, y0));
                hBundle.Points.Add(new Vector2d(10.0, y1));
                hBundle.Segments.Add(new Mesh2Segment(a, a + 1, (i % 5) + 1));
            }
            for (int k = 0; k < 4; ++k) {
                double x = 2.0 + 2.0 * k;
                int a = hBundle.Points.Count;
                hBundle.Points.Add(new Vector2d(x, -0.1));
                hBundle.Points.Add(new Vector2d(x + 0.3, 4.1));
                hBundle.Segments.Add(new Mesh2Segment(a, a + 1, 9));
            }
            h = HashSnapResult(h, SnapRounding2.Apply(hBundle, 1.0 / 512.0));

            // 3. T-chain (dense hot pixels + Restore).
            h = HashSnapResult(h, SnapRounding2.Apply(TChain(120), 1.0 / 1024.0));

            // 4. Near-collinear crossing where double (b-a)x(c-a) has sign -1 while exact BigInteger Orient is +1.
            // Both segments properly cross ~9 pixels away from c at tol = 1/1024.
            var nearCollinear = new ConstrainedMesh2Input();
            nearCollinear.Points.Add(new Vector2d(0.1, 0.3));
            nearCollinear.Points.Add(new Vector2d(1.7, 2.6));
            nearCollinear.Points.Add(new Vector2d(
                BitConverter.Int64BitsToDouble(0x3fe6666666666668L),
                BitConverter.Int64BitsToDouble(0x3ff299999999999bL)));
            nearCollinear.Points.Add(new Vector2d(
                BitConverter.Int64BitsToDouble(0x3ff4cccccccccccdL),
                BitConverter.Int64BitsToDouble(0x4000333333333332L)));
            nearCollinear.Segments.Add(new Mesh2Segment(0, 1, 1));
            nearCollinear.Segments.Add(new Mesh2Segment(2, 3, 2));
            var srNear = SnapRounding2.Apply(nearCollinear, 1.0 / 1024.0);
            Assert.Equal(1, srNear.Crossings);
            h = HashSnapResult(h, srNear);

            // 5. Outer ring + hole + repeated & crossing segments at non-power-of-two tolerance 0.003.
            var ringed = new ConstrainedMesh2Input();
            ringed.Points.Add(new Vector2d(0.0, 0.0));
            ringed.Points.Add(new Vector2d(1.0, 0.0));
            ringed.Points.Add(new Vector2d(1.0, 1.0));
            ringed.Points.Add(new Vector2d(0.0, 1.0));
            ringed.Points.Add(new Vector2d(0.25, 0.25));
            ringed.Points.Add(new Vector2d(0.75, 0.25));
            ringed.Points.Add(new Vector2d(0.75, 0.75));
            ringed.Points.Add(new Vector2d(0.25, 0.75));
            ringed.Points.Add(new Vector2d(0.1, 0.501));
            ringed.Points.Add(new Vector2d(0.9, 0.499));
            ringed.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, false));
            ringed.Rings.Add(new Mesh2Ring(new[] { 4, 5, 6, 7 }, true));
            ringed.Segments.Add(new Mesh2Segment(8, 9, 3));
            ringed.Segments.Add(new Mesh2Segment(9, 8, 3)); // repeated reversed segment
            ringed.Segments.Add(new Mesh2Segment(0, 2, 4));
            ringed.Segments.Add(new Mesh2Segment(1, 3, 5));
            h = HashSnapResult(h, SnapRounding2.Apply(ringed, 0.003));

            // 6. Vertical/steep bundle + coarse random soup (dense multi-round Restore where steep/vertical chain edges
            // hit px == eb.MinX in Free(), pinning the exact < vs <= bounding-box prune).
            var vBundle = new ConstrainedMesh2Input();
            var rngV = new Rng(140277);
            for (int i = 0; i < 80; ++i) {
                double x0 = i * 0.05, x1 = x0 + (rngV.Next() - 0.5) * 0.12;
                int a = vBundle.Points.Count;
                vBundle.Points.Add(new Vector2d(x0, 0.0));
                vBundle.Points.Add(new Vector2d(x1, 10.0));
                vBundle.Segments.Add(new Mesh2Segment(a, a + 1, (i % 5) + 1));
            }
            for (int k = 0; k < 6; ++k) {
                double y = 1.0 + 1.5 * k;
                int a = vBundle.Points.Count;
                vBundle.Points.Add(new Vector2d(-0.1, y));
                vBundle.Points.Add(new Vector2d(4.1, y + 0.35));
                vBundle.Segments.Add(new Mesh2Segment(a, a + 1, 9));
            }
            h = HashSnapResult(h, SnapRounding2.Apply(vBundle, 1.0 / 64.0));
            h = HashSnapResult(h, SnapRounding2.Apply(RandomSoup(100, seed: 42), 1.0 / 64.0));

            // Pinned bit-for-bit golden hash (verified identical to pre-#1402 SnapRounding2 on master).
            Assert.Equal(4633619528224767930UL, h);
        }

        [Fact]
        public void Build_OriginDeduplication_UniqueAndDuplicateCoords_PreserveOrderAndSet()
        {
            // Case A: uniqueOriginCoords == true (20 distinct near-parallel segments sharing snapped chain edges).
            var uniq = new ConstrainedMesh2Input();
            for (int i = 0; i < 20; ++i) {
                int a = uniq.Points.Count;
                uniq.Points.Add(new Vector2d(0.0, 0.5 + i * 1e-6));
                uniq.Points.Add(new Vector2d(1.0, 0.5 + (19 - i) * 1e-6));
                uniq.Segments.Add(new Mesh2Segment(a, a + 1, i + 1));
            }
            var srUniq = SnapRounding2.Apply(uniq, 1.0 / 32.0);
            Assert.True(srUniq.Result.Segments.Count > 0);
            // Every output edge shared by all 20 chains must list all 20 unique origins in chain order, without duplicates.
            var midEdge = srUniq.Result.Segments[0];
            var pMid = srUniq.Result.Points[midEdge.A];
            var qMid = srUniq.Result.Points[midEdge.B];
            var uniqOrigins = srUniq.Origins(pMid, qMid);
            Assert.Equal(20, uniqOrigins.Count);
            for (int i = 0; i < 20; ++i)
                Assert.Equal((uniq.Points[2 * i], uniq.Points[2 * i + 1]), uniqOrigins[i]);

            // Case B: uniqueOriginCoords == false with BOTH < 8 and >= 8 origins per edge, including non-consecutive
            // duplicate coordinate pairs across distinct vertex indices (exercises list.Contains and originSets HashSet).
            var dup = new ConstrainedMesh2Input();
            var expectedOrder = new List<(Vector2d, Vector2d)>();
            // 12 distinct coordinate pairs, then repeat index 0, 5, 11 at non-consecutive later chain positions,
            // plus an immediate consecutive duplicate of index 3.
            var pairs = new List<(Vector2d a, Vector2d b)>();
            for (int i = 0; i < 12; ++i)
                pairs.Add((new Vector2d(0.0, 0.5 + i * 1e-6), new Vector2d(1.0, 0.5 + i * 1e-6)));
            // Build sequence with duplicates at < 8 and >= 8 positions:
            int[] seq = { 0, 1, 2, 3, 3, 0, 4, 5, 6, 7, 8, 9, 10, 11, 0, 5, 11, 2 };
            foreach (int idx in seq) {
                int a = dup.Points.Count;
                dup.Points.Add(pairs[idx].a);
                dup.Points.Add(pairs[idx].b);
                dup.Segments.Add(new Mesh2Segment(a, a + 1, idx + 1));
                if (!expectedOrder.Contains(pairs[idx])) expectedOrder.Add(pairs[idx]);
            }
            // Add a vertical crosser so the horizontal chain splits into multiple snapped edges.
            int ca = dup.Points.Count;
            dup.Points.Add(new Vector2d(0.5, 0.0));
            dup.Points.Add(new Vector2d(0.5, 1.0));
            dup.Segments.Add(new Mesh2Segment(ca, ca + 1, 99));

            var srDup = SnapRounding2.Apply(dup, 1.0 / 32.0);
            Assert.True(srDup.Crossings >= 1);
            var firstSeg = srDup.Result.Segments[0];
            var dupOrigins = srDup.Origins(srDup.Result.Points[firstSeg.A], srDup.Result.Points[firstSeg.B]);
            Assert.Equal(expectedOrder.Count, dupOrigins.Count);
            for (int i = 0; i < expectedOrder.Count; ++i)
                Assert.Equal(expectedOrder[i], dupOrigins[i]);
            // Also verify TryOrigin returns the first origin in chain order.
            Assert.True(srDup.TryOrigin(srDup.Result.Points[firstSeg.A], srDup.Result.Points[firstSeg.B], out var firstOrigin));
            Assert.Equal(expectedOrder[0], firstOrigin);
        }

        [Fact]
        public void Restore_MultiRound_SingleAndManyCriticalPixels_PassesOracleAndMatchesSeparation()
        {
            // Dense soup at coarse tolerance produces multi-round Restore with Spread >= 10 (> 16 unique Restore query points,
            // exercising multi-level k-d tree traversal in Restore).
            var soup = RandomSoup(70, seed: 31415);
            double tol = 1.0 / 64.0;
            var sr = SnapRounding2.Apply(soup, tol);
            Assert.True(sr.Rounds >= 2 && sr.Spread >= 10, $"Expected multi-round Restore with Spread >= 10 (got rounds={sr.Rounds}, spread={sr.Spread})");
            var mi = ToOracleInput(soup, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }
    }
}
