using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Snap rounding with restore at an explicit tolerance (D14, comp_geom/SnapRounding2.cs). The properties are checked exactly, with the
    /// oracle's rationals: every input point on the centre of its half-open pixel of a grid anchored at the origin; no output vertex in the
    /// open pixel of an edge not incident to it (SRR's minimum distance); no two output edges crossing; every chain within half a pixel
    /// per axis of its input segment (SR's geometric similarity).
    /// </summary>
    public class SnapRounding2Tests
    {
        static ConstrainedMesh2Input Segments(params double[] xy)
        {
            var input = new ConstrainedMesh2Input();
            for (int i = 0; i + 3 < xy.Length; i += 4) {
                input.Points.Add(new Vector2d(xy[i], xy[i + 1])); input.Points.Add(new Vector2d(xy[i + 2], xy[i + 3]));
                input.Segments.Add(new Mesh2Segment(input.Points.Count - 2, input.Points.Count - 1));
            }
            return input;
        }

        static Q PixelCentre(double v, double w)
        {
            // floor(v / w + 1/2) * w, exactly
            Q t = Q.FromDouble(v) / Q.FromDouble(w) + new Q(1, 2);
            var f = System.Numerics.BigInteger.Divide(t.N, t.D);
            if (t.N.Sign < 0 && f * t.D != t.N) f -= 1;
            return new Q(f, 1) * Q.FromDouble(w);
        }

        /// <summary> does segment ab meet the open square of half-width h around c </summary>
        static bool CrossesOpenSquare(Pt a, Pt b, Pt c, Q h)
        {
            Q x0 = c.Qx - h, x1 = c.Qx + h, y0 = c.Qy - h, y1 = c.Qy + h;
            if (Q.Max(a.Qx, b.Qx) <= x0 || Q.Min(a.Qx, b.Qx) >= x1 || Q.Max(a.Qy, b.Qy) <= y0 || Q.Min(a.Qy, b.Qy) >= y1) return false;
            int pos = 0, neg = 0;
            foreach (var p in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                int o = Exact.Orient(a, b, p);
                if (o > 0) ++pos; else if (o < 0) ++neg;
            }
            return pos > 0 && neg > 0;
        }

        /// <summary> the four properties of the class summary; returns the restore counts for the callers' statistics </summary>
        static SnapRounding2 Check(ConstrainedMesh2Input input, double w)
        {
            var snap = SnapRounding2.Apply(input, w);
            var r = snap.Result;
            // exactly half a pixel on the exact centres; the centres are rounded to doubles (D1), which for a pixel width that is no
            // power of two moves an edge by a few units in the last place of the coordinates, so the check on the doubles allows that much
            double big = r.Points.Max(p => Math.Max(Math.Abs(p.x), Math.Abs(p.y)));
            Q h = Q.FromDouble(w) / 2 - Q.FromDouble(big) * new Q(4, 1L << 52);
            var pts = r.Points.Select(p => Pt.FromDoubles(p.x, p.y)).ToArray();
            for (int i = 0; i < input.Points.Count; ++i) {
                // exactly the centre up to its rounding to doubles
                Assert.True(Q.Abs(pts[i].Qx - PixelCentre(input.Points[i].x, w)) <= Q.Abs(PixelCentre(input.Points[i].x, w)) * new Q(1, 1L << 52) + new Q(1, 1L << 60),
                    "input " + i + " is not at its pixel centre");
                Assert.True(Q.Abs(pts[i].Qy - PixelCentre(input.Points[i].y, w)) <= Q.Abs(PixelCentre(input.Points[i].y, w)) * new Q(1, 1L << 52) + new Q(1, 1L << 60),
                    "input " + i + " is not at its pixel centre");
            }
            var edges = r.Segments.Select(s => (s.A, s.B)).Concat(r.Rings.SelectMany(g => g.Vertices.Select((v, i) => (v, g.Vertices[(i + 1) % g.Vertices.Length]))))
                .Where(e => !pts[e.Item1].Equals(pts[e.Item2])).ToList();
            var distinct = pts.Distinct().ToList();
            foreach (var (a, b) in edges) {
                foreach (var p in distinct)
                    if (!p.Equals(pts[a]) && !p.Equals(pts[b]))
                        Assert.False(CrossesOpenSquare(pts[a], pts[b], p, h), "vertex " + p + " lies in the pixel of edge " + pts[a] + " - " + pts[b]);
            }
            for (int i = 0; i < edges.Count; ++i)
                for (int j = i + 1; j < edges.Count; ++j)
                    Assert.False(Exact.ProperCross(pts[edges[i].Item1], pts[edges[i].Item2], pts[edges[j].Item1], pts[edges[j].Item2]), "two output edges cross");
            return snap;
        }

        [Fact]
        public void Contact_IsHalfOpen_BottomAndLeftCount_TopAndRightDoNot()
        {
            // a hot pixel at (0, 0) from point 0; a segment along its bottom side is routed through it, one along its top side is not
            foreach (var (y, through) in new[] { (-0.5, true), (0.5, false) }) {
                var input = Segments(-3, y, 3, y);
                input.Points.Add(new Vector2d(0, 0));
                var r = SnapRounding2.Apply(input, 1).Result;
                bool routed = r.Segments.Any(s => s.A == 2 || s.B == 2);
                Assert.True(routed == through, "a segment at y = " + y + (through ? " must" : " must not") + " pass through the pixel of (0, 0)");
            }
            foreach (var (x, through) in new[] { (-0.5, true), (0.5, false) }) {
                var input = Segments(x, -3, x, 3);
                input.Points.Add(new Vector2d(0, 0));
                var r = SnapRounding2.Apply(input, 1).Result;
                Assert.True(r.Segments.Any(s => s.A == 2 || s.B == 2) == through, "a segment at x = " + x);
            }
        }

        [Fact]
        public void Crossing_GetsTheCentreOfItsPixel()
        {
            // the crossing of the two diagonals is (2.3, 2.3) after the shift: pixel (2, 2) with w = 1
            var input = Segments(0.3, 0.3, 4.3, 4.3, 0.3, 4.3, 4.3, 0.3);
            var snap = Check(input, 1);
            Assert.Equal(1, snap.Crossings);
            int extra = snap.Result.Points.Count - 1;
            Assert.Equal(4, extra);
            Assert.Equal(new Vector2d(2, 2), snap.Result.Points[extra]);
            Assert.Equal(4, snap.Result.Segments.Count);
            Assert.Equal(4, snap.Result.Segments.Count(s => s.A == extra || s.B == extra));
        }

        [Fact]
        public void Fuzz_SeparationAndSimilarity_WithRestore()
        {
            // random segments on a grid of 1/64 in [0, 16]^2 (so many end on pixel sides), pixels 1, 2 and 4, some segments repeated
            // or overlapping: the restore step must run on many of them
            var random = new Random(77);
            int restored = 0, runs = 0;
            for (int iter = 0; iter < 400; ++iter) {
                var xy = new List<double>();
                int m = 6 + random.Next(20);
                for (int i = 0; i < 4 * m; ++i) xy.Add(Math.Round(random.NextDouble() * 16 * 64) / 64);
                if (iter % 5 == 0) { xy.AddRange(xy.Take(4)); xy.Add(xy[0]); xy.Add(xy[1]); xy.Add((xy[0] + xy[2]) / 2); xy.Add((xy[1] + xy[3]) / 2); }
                var snap = Check(Segments(xy.ToArray()), new[] { 1.0, 2, 4 }[iter % 3]);
                restored += snap.Spread + snap.Snapped + snap.ForcedSpread;
                if (snap.Spread + snap.Snapped + snap.ForcedSpread > 0) ++runs;
            }
            Assert.True(runs >= 20, "the restore step ran on only " + runs + " inputs");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance_ScalesBitForBit_OthersKeepTheProperties(string name)
        {
            // the grid is anchored at the origin and its pixels are half-open, so rotations and translations change the result by
            // design (O11); 2^k scales with the tolerance scaled alike give the same result bit for bit
            var s = Similarity.ByName(name);
            var random = new Random(5);
            var xy = new List<double>();
            for (int i = 0; i < 4 * 24; ++i) xy.Add(Math.Round(random.NextDouble() * 16 * 64) / 64);
            var input = Segments(xy.ToArray());
            var moved = new ConstrainedMesh2Input { Segments = input.Segments };
            foreach (var p in input.Points) { var q = s.Apply(new Vector3d(p.x, p.y, 0)); moved.Points.Add(new Vector2d(q.x, q.y)); }
            double w = s.Scale;
            var snap = Check(moved, w);
            if (s.IsExact) {
                var plain = SnapRounding2.Apply(input, 1).Result;
                Assert.Equal(plain.Points.Select(p => p * s.Scale), snap.Result.Points);
                Assert.Equal(plain.Segments.Select(g => (g.A, g.B)), snap.Result.Segments.Select(g => (g.A, g.B)));
            }
        }

        [Fact]
        public void Mesher_Reuse_GivesTheSameMesh()
        {
            var input = Segments(0.3, 0.3, 4.3, 4.3, 0.3, 4.3, 4.3, 0.3, 0, 0, 0.001, 4);
            var mesher = new ConstrainedDelaunayMesh2 { SnapTolerance = 1.0 / 4 };
            var a = mesher.Compute(input); var b = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, a.Status);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            Assert.Equal(input.Points.Count, a.Mesh.InputCount);
            Assert.Equal(input.Points.Count, a.Mesh.InputToVertex.Count);
            Assert.Contains(a.Diagnostics, d => d.StartsWith("snap: tolerance"));
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(1e-300)]
        public void InvalidTolerance_IsRefused(double tolerance)
        {
            var input = Segments(0, 0, 1e6, 1, 0, 1, 1, 0);
            Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2 { SnapTolerance = tolerance }.Compute(input));
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { SnapTolerance = tolerance }.Compute(input));
        }

        [Fact]
        public void CollapsedRing_IsReported_AndItsSeedDropped()
        {
            var input = new ConstrainedMesh2Input();
            foreach (var (x, y) in new[] { (0.0, 0.0), (10, 0), (10, 10), (0, 10), (5, 5), (5.01, 5), (5.01, 5.01), (5, 5.01) }) input.Points.Add(new Vector2d(x, y));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            input.Rings.Add(new Mesh2Ring(new[] { 4, 5, 6, 7 }, true));
            input.HoleSeeds.Add(new Vector2d(5.005, 5.005));
            var snap = SnapRounding2.Apply(input, 0.25);
            Assert.Equal(1, snap.CollapsedRings);
            Assert.Equal(1, snap.DroppedSeeds);
            Assert.Single(snap.Result.Rings);
            Assert.Empty(snap.Result.HoleSeeds);
            var result = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.25 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Contains("collapsed"));
        }
    }
}
