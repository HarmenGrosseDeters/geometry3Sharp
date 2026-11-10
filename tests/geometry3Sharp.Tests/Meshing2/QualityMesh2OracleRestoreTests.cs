using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's own restore step of snap rounding with restore (Belussi, Migliorini, Negri and Pelagatti 2016; Q17, item (a)): the
    /// oracle recomputes SRR's restore pixels from the specification (MESHING2D_SNAP.md section 3b) and counts them in the rounding of
    /// the input (its input-only state of rule (ii')). These tests hold it against the library's snap rounding, which it shares no code
    /// with, and check that the restore pixels are no longer taken as refinement's
    /// </summary>
    public class QualityMesh2OracleRestoreTests
    {
        /// <summary>
        /// the pixels of the proper crossings of the input's edges that hold no input point, exactly (the pixel of x is
        /// floor(x / tol + 1/2)); the generator's star polygons can cross themselves (an angular gap above 180 degrees)
        /// </summary>
        static HashSet<(BigInteger x, BigInteger y)> CrossingPixels(Mesh2Input input)
        {
            var edges = new List<(int a, int b)>();
            foreach (var r in input.Rings) for (int i = 0; i < r.Vertices.Length; ++i) edges.Add((r.Vertices[i], r.Vertices[(i + 1) % r.Vertices.Length]));
            foreach (var s in input.Segments) edges.Add((s.A, s.B));
            var pts = Enumerable.Range(0, input.Count).Select(i => Pt.FromDoubles(input.X[i], input.Y[i])).ToArray();
            Q tol = Q.FromDouble(input.SnapTolerance), half = new Q(1, 2);
            BigInteger Floor(Q v) { var q = BigInteger.DivRem(v.N, v.D, out var rem); return rem.Sign < 0 ? q - 1 : q; }
            (BigInteger, BigInteger) PixelOf(Q x, Q y) => (Floor(x / tol + half), Floor(y / tol + half));
            var inputPixels = new HashSet<(BigInteger, BigInteger)>(pts.Select(p => PixelOf(p.Qx, p.Qy)));
            var result = new HashSet<(BigInteger, BigInteger)>();
            for (int i = 0; i < edges.Count; ++i)
                for (int j = i + 1; j < edges.Count; ++j) {
                    Pt a = pts[edges[i].a], b = pts[edges[i].b], c = pts[edges[j].a], d = pts[edges[j].b];
                    if (Exact.Orient(a, b, c) * Exact.Orient(a, b, d) >= 0 || Exact.Orient(c, d, a) * Exact.Orient(c, d, b) >= 0) continue;
                    Q den = (b.Qx - a.Qx) * (d.Qy - c.Qy) - (b.Qy - a.Qy) * (d.Qx - c.Qx), t = ((c.Qx - a.Qx) * (d.Qy - c.Qy) - (c.Qy - a.Qy) * (d.Qx - c.Qx)) / den;
                    var px = PixelOf(a.Qx + t * (b.Qx - a.Qx), a.Qy + t * (b.Qy - a.Qy));
                    if (!inputPixels.Contains(px)) result.Add(px);
                }
            return result;
        }

        /// <summary> the hot pixels the library's snap rounding adds beyond the input points' (crossings and restore), as indices </summary>
        static List<(BigInteger x, BigInteger y)> LibraryExtraPixels(Mesh2Input input)
        {
            var snap = SnapRounding2.Apply(ConstrainedDelaunayMesh2Candidate.Convert(input), input.SnapTolerance);
            return snap.Result.Points.Skip(snap.InputCount)
                .Select(p => (new BigInteger(Math.Round(p.x / input.SnapTolerance)), new BigInteger(Math.Round(p.y / input.SnapTolerance)))).Distinct().OrderBy(p => p).ToList();
        }

        static List<(BigInteger x, BigInteger y)> OracleExtraPixels(Mesh2Input input, OracleReport report)
            => CrossingPixels(input).Concat(report.RestorePixels).Distinct().OrderBy(p => p).ToList();

        /// <summary>
        /// on seeds 0 to 2999 of <see cref="StarPolygonFuzz"/>, the hot pixels the library's snap rounding adds beyond the input points'
        /// are exactly the crossings' and the oracle's restore pixels; at least the 10 seeds that need them have some
        /// </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void RestorePixels_MatchTheLibrarys_OnTheFuzz()
        {
            var wrong = new List<string>();
            int withRestore = 0;
            for (int seed = 0; seed < 3000; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                var library = LibraryExtraPixels(input);
                var o = new QualityMesh2Candidate().Run(input);
                // (an input whose ring collapses entirely has an empty domain, and the oracle computes no snapped domain for an empty
                // output: those seeds are skipped here)
                bool empty = QualityMesh2KeepsChainsApartTests.EmptyDomain.Contains(seed);
                Assert.Equal(empty ? Mesh2Status.Empty : Mesh2Status.Success, o.Status);
                if (empty) continue;
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                Assert.NotNull(report.RestorePixels);
                var expected = OracleExtraPixels(input, report);
                if (report.RestorePixels.Count > 0) ++withRestore;
                if (!expected.SequenceEqual(library))
                    wrong.Add(seed + ": library " + string.Join(" ", library) + ", oracle " + string.Join(" ", expected));
            }
            Assert.True(wrong.Count == 0, wrong.Count + " seeds differ:\n" + string.Join("\n", wrong.Take(20)));
            Assert.True(withRestore >= 10, "seeds with restore pixels: " + withRestore);
        }

        /// <summary> a soup of 3 to 8 segments with ends at quarters in [0, 6]², pixel 1: dense, with ties in the nearest pixel </summary>
        static Mesh2Input Soup(int seed)
        {
            var rnd = new Random(seed);
            int n = 3 + rnd.Next(6);
            var xs = new List<double>(); var ys = new List<double>();
            var input = new Mesh2Input { SnapTolerance = 1 };
            for (int i = 0; i < n; ++i) {
                for (int k = 0; k < 2; ++k) { xs.Add(rnd.Next(25) * 0.25); ys.Add(rnd.Next(25) * 0.25); }
                input.Segments.Add(new SegmentInput(2 * i, 2 * i + 1));
            }
            input.X = xs.ToArray(); input.Y = ys.ToArray();
            return input;
        }

        /// <summary>
        /// the same comparison on dense segment soups snapped alone (M2), where the restore step meets ties in the nearest pixel; the
        /// soups of seeds 0 to 1999 use ChoosePixel's rules R1 and R3 (its widened search, the fallback when no neighbour is useful,
        /// was met in none of them, nor in 280 000 other random inputs, by the library or the oracle)
        /// </summary>
        [Fact]
        public void RestorePixels_MatchTheLibrarys_OnSegmentSoups()
        {
            var wrong = new List<string>();
            int spread = 0, forced = 0;
            for (int seed = 0; seed < 2000; ++seed) {
                var input = Soup(seed);
                if (Enumerable.Range(0, input.Segments.Count).Any(i => input.X[2 * i] == input.X[2 * i + 1] && input.Y[2 * i] == input.Y[2 * i + 1])) continue;
                var snap = SnapRounding2.Apply(ConstrainedDelaunayMesh2Candidate.Convert(input), 1);
                spread += snap.Spread; forced += snap.ForcedSpread;
                var library = LibraryExtraPixels(input);
                var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) { wrong.Add(seed + ": status " + o.Status); continue; }
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
                var oracle = OracleExtraPixels(input, report);
                if (!oracle.SequenceEqual(library)) wrong.Add(seed + ": library " + string.Join(" ", library) + ", oracle " + string.Join(" ", oracle));
            }
            Assert.True(wrong.Count == 0, wrong.Count + " seeds differ:\n" + string.Join("\n", wrong.Take(20)));
            Assert.True(spread > 0 && forced > 0, "R1 " + spread + ", R3 " + forced);
        }

        /// <summary>
        /// a hull domain's hull edges are snap-rounded only with refinement (option C), and so restored only then: the top hull edge
        /// (0, 0) - (10, 1.4) is routed (0, 0) - (10, 1), whose link crosses the pixel of the input point (5, 0.4), which the edge
        /// does not contact; the restore step makes (4, 1) hot at M3, as the mesher does (its first vertex beyond the input), and
        /// nothing at M2
        /// </summary>
        [Fact]
        public void HullEdgesAreRestoredOnlyWithRefinement()
        {
            var x = new[] { 0.0, 10, 10, 0, 5 };
            var y = new[] { 0.0, 1.4, -3, -3, 0.4 };
            var snapped = new Mesh2Input { X = x, Y = y, SnapTolerance = 1 };
            var m2 = Oracle.Check(snapped, new ConstrainedDelaunayMesh2Candidate().Run(snapped), new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(m2.Ok, m2.ToString());
            Assert.Empty(m2.RestorePixels);
            var refined = new Mesh2Input { X = x, Y = y, SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 5000 };
            var o = new QualityMesh2Candidate().Run(refined);
            var m3 = Oracle.Check(refined, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(m3.Ok, m3.ToString());
            Assert.Equal(new[] { (new BigInteger(4), new BigInteger(1)) }, m3.RestorePixels.ToArray());
            Assert.Equal((4.0, 1.0), (o.VX[refined.Count], o.VY[refined.Count]));
        }

        /// <summary>
        /// a tie in ChoosePixel's nearest pixel: four segments of slope 0 and -1 through half pixels and a free point (found by a search
        /// over such inputs); the candidates (5, 0) and (6, 0) lie equally near the segment, and the tie goes to the smaller x, as the
        /// library breaks it
        /// </summary>
        [Fact]
        public void RestoreTie_GoesToTheSmallerX()
        {
            var input = new Mesh2Input { X = new[] { 0, 5.5, 1.5, 6, 2.5, 8.5, 2, 8, 1 }, Y = new[] { 5, 5, 4.5, 0.5, 2.5, -2.5, 3, -3, 5.25 }, SnapTolerance = 1 };
            for (int i = 0; i < 4; ++i) input.Segments.Add(new SegmentInput(2 * i, 2 * i + 1));
            var report = Oracle.Check(input, new ConstrainedDelaunayMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M2 });
            Assert.Contains((new BigInteger(5), new BigInteger(0)), report.RestorePixels);
            Assert.DoesNotContain((new BigInteger(6), new BigInteger(0)), report.RestorePixels);
            Assert.Equal(LibraryExtraPixels(input), OracleExtraPixels(input, report));
        }

        /// <summary>
        /// the same comparison where the critical search walks along each link (more hot pixels than a link has pixels): 100 to 200 free
        /// points and 5 to 15 segments in [0, 30]², pixel 1, snapped alone (M2) (review S1: the soups and the fuzz have too few hot
        /// pixels for that mode)
        /// </summary>
        [Fact]
        public void RestorePixels_MatchTheLibrarys_WithManyHotPixels()
        {
            var wrong = new List<string>();
            int withRestore = 0;
            for (int seed = 0; seed < 60; ++seed) {
                var rnd = new Random(seed);
                int points = 100 + rnd.Next(101), segments = 5 + rnd.Next(11);
                var xs = new List<double>(); var ys = new List<double>();
                for (int i = 0; i < 2 * segments + points; ++i) { xs.Add(Math.Round(rnd.NextDouble() * 300) / 10); ys.Add(Math.Round(rnd.NextDouble() * 300) / 10); }
                var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), SnapTolerance = 1 };
                for (int i = 0; i < segments; ++i) if (xs[2 * i] != xs[2 * i + 1] || ys[2 * i] != ys[2 * i + 1]) input.Segments.Add(new SegmentInput(2 * i, 2 * i + 1));
                var library = LibraryExtraPixels(input);
                var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) { wrong.Add(seed + ": status " + o.Status); continue; }
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
                if (report.RestorePixels.Count > 0) ++withRestore;
                var oracle = OracleExtraPixels(input, report);
                if (!oracle.SequenceEqual(library)) wrong.Add(seed + ": library " + string.Join(" ", library) + ", oracle " + string.Join(" ", oracle));
            }
            Assert.True(wrong.Count == 0, wrong.Count + " seeds differ:\n" + string.Join("\n", wrong.Take(20)));
            Assert.True(withRestore >= 30, "seeds with restore pixels: " + withRestore);
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
        /// the restore cases of the fuzz in every similarity frame: the library's extra hot pixels are the crossings' and the oracle's
        /// restore pixels, and the mesh is accepted; at the 2^k scales the restore pixels, the mesh and the excuses are the same bit for bit
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void RestoreCases_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            foreach (int seed in new[] { 62, 161, 787, 818, 963, 1455, 2396, 2459, 2471, 2923 }) {
                var plain = StarPolygonFuzz.Make(seed);
                var input = Moved(plain, s);
                var library = LibraryExtraPixels(input);
                var o = new QualityMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                var oracle = OracleExtraPixels(input, report);
                Assert.True(library.SequenceEqual(oracle), name + ", seed " + seed + ": library " + string.Join(" ", library) + ", oracle " + string.Join(" ", oracle));
                Assert.True(report.Ok, name + ", seed " + seed + ": " + report);
                if (s.IsExact) {
                    var a = new QualityMesh2Candidate().Run(plain);
                    var plainReport = Oracle.Check(plain, a, new OracleExpect { Milestone = Milestone.M3 });
                    Assert.Equal(plainReport.RestorePixels, report.RestorePixels);
                    Assert.Equal(a.VX.Select(x => x * s.Scale), o.VX);
                    Assert.Equal(a.VY.Select(y => y * s.Scale), o.VY);
                    Assert.Equal(plainReport.ExcusedSummary(), report.ExcusedSummary());
                }
            }
        }

        /// <summary> reuse: one oracle check twice on the same input and output gives the same restore pixels and verdict </summary>
        [Fact]
        public void RestorePixels_SameOnASecondCheck()
        {
            var input = StarPolygonFuzz.Make(62);
            var o = new QualityMesh2Candidate().Run(input);
            var first = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            var second = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.NotEmpty(first.RestorePixels);
            Assert.Equal(first.RestorePixels, second.RestorePixels);
            Assert.Equal(first.ToString(), second.ToString());
            Assert.Equal(first.ExcusedSummary(), second.ExcusedSummary());
        }
    }
}
