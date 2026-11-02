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

        /// <summary>
        /// where segment ab meets Hobby's half-open pixel around centre c (half-width h): the parameters of the piece of ab in the closed
        /// square, by clipping (Liang and Barsky), or null if there is none or it lies only on the open top or right side. An independent
        /// formulation of the routine's contact test (which uses separating axes).
        /// </summary>
        static (Q t0, Q t1)? HalfOpenContact(Pt a, Pt b, Pt c, Q h)
        {
            Q t0 = Q.Zero, t1 = Q.One;
            Q x0 = c.Qx - h, x1 = c.Qx + h, y0 = c.Qy - h, y1 = c.Qy + h;
            foreach (var (p, d, lo, hi) in new[] { (a.Qx, b.Qx - a.Qx, x0, x1), (a.Qy, b.Qy - a.Qy, y0, y1) }) {
                if (d.Sign == 0) { if (p < lo || p > hi) return null; continue; }
                Q u = (lo - p) / d, v = (hi - p) / d;
                if (u > v) (u, v) = (v, u);
                if (u > t0) t0 = u;
                if (v < t1) t1 = v;
            }
            if (t0 > t1) return null;
            Pt P(Q t) => new Pt(a.Qx + (b.Qx - a.Qx) * t, a.Qy + (b.Qy - a.Qy) * t);
            Pt p0 = P(t0), p1 = P(t1);
            if (p0.Qx == x1 && p1.Qx == x1) return null;
            if (p0.Qy == y1 && p1.Qy == y1) return null;
            return (t0, t1);
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
            // every chain is snap rounding on the final hot pixels (SRR's Proposition 4.14): the centres of exactly the hot pixels its
            // input segment touches, in order along it (entry, then exit); every output point is a hot pixel centre
            Q hw = Q.FromDouble(w) / 2;
            foreach (var g in input.Segments) {
                if (g.A == g.B) continue;
                Pt a = Pt.FromDoubles(input.Points[g.A].x, input.Points[g.A].y), b = Pt.FromDoubles(input.Points[g.B].x, input.Points[g.B].y);
                var touched = new List<(Q t0, Q t1, Pt c)>();
                foreach (var c in distinct) {
                    var exactCentre = new Pt(PixelCentre(r.Points[Array.IndexOf(pts, c)].x, w), PixelCentre(r.Points[Array.IndexOf(pts, c)].y, w));
                    var hit = HalfOpenContact(a, b, exactCentre, hw);
                    if (hit != null) touched.Add((hit.Value.t0, hit.Value.t1, c));
                }
                touched.Sort((u, v) => u.t0 != v.t0 ? u.t0.CompareTo(v.t0) : u.t1 != v.t1 ? u.t1.CompareTo(v.t1) : 0);
                var expected = touched.Select(x => x.c).ToList();
                var chain = snap.ChainPoints(g.A, g.B).Select(p => Pt.FromDoubles(p.x, p.y)).ToList();
                Assert.True(expected.SequenceEqual(chain), "the chain of " + a + " - " + b + " is " + string.Join(" ", chain) + ", the touched hot pixels " + string.Join(" ", expected));
            }
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
                if (iter % 5 == 0) { xy.AddRange(xy.Take(4).ToList()); xy.Add(xy[0]); xy.Add(xy[1]); xy.Add((xy[0] + xy[2]) / 2); xy.Add((xy[1] + xy[3]) / 2); }   // (Take copied first: net48 enumerates the list while it grows)
                // pixels that are powers of two, and some that are not (their centres and corners are no exact doubles)
                var snap = Check(Segments(xy.ToArray()), new[] { 1.0, 2, 4, 0.3, 1.0 / 3, 2.3 }[iter % 6]);
                restored += snap.Spread + snap.Widened + snap.ForcedSpread;
                if (snap.Spread + snap.Widened + snap.ForcedSpread > 0) ++runs;
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

        [Fact]
        public void Tolerance_AtTheLimit_OfTwoToThe40Pixels()
        {
            // review of #277: the pixel index must stay below 2^40; a point at 2^20 is pixel 2^40 at 2^-20 (refused) and 2^39 at 2^-19
            var input = Segments(0, 0, 1 << 20, 1, 0, 1, 1, 0);
            Assert.Throws<ArgumentException>(() => SnapRounding2.Apply(input, Math.Pow(2, -20)));
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { SnapTolerance = Math.Pow(2, -20) }.Compute(input));
            var snap = SnapRounding2.Apply(input, Math.Pow(2, -19));
            Assert.Equal(1 << 20, snap.Result.Points[1].x);
            Assert.Equal(Triangulation2Status.Success, new ConstrainedDelaunayMesh2 { SnapTolerance = Math.Pow(2, -19) }.Compute(input).Status);
        }

        /// <summary> random segments in a square ring on a 1/64 lattice (the grid fuzz of the gate tests, smaller) </summary>
        static Mesh2Input RingWithSegments(int seed, int count)
        {
            var random = new Random(seed);
            var x = new List<double> { 0, 16, 16, 0 }; var y = new List<double> { 0, 0, 16, 16 };
            var input = new Mesh2Input();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            for (int i = 0; i < count; ++i) {
                for (int k = 0; k < 2; ++k) { x.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); y.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); }
                input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }

        public static IEnumerable<object[]> MeshInvarianceCases()
        {
            foreach (var s in Similarity.All()) { yield return new object[] { s.Name, Milestone.M2 }; yield return new object[] { s.Name, Milestone.M3 }; }
        }

        /// <summary>
        /// The meshes with a tolerance, not only the snapped input (review of #277): 2^k scales with the tolerance scaled alike give the
        /// same mesh bit for bit, refinement on the grid included; the other similarities move the grid by design (O11) and keep the
        /// properties the oracle judges.
        /// </summary>
        [Theory]
        [MemberData(nameof(MeshInvarianceCases))]
        public void SnappedMesh_Invariance_ScalesBitForBit_OthersPassTheOracle(string name, Milestone milestone)
        {
            var s = Similarity.ByName(name);
            var input = RingWithSegments(17, 12);
            input.SnapTolerance = 0.3;
            if (milestone == Milestone.M3) { input.MinAngleDeg = 25; input.SteinerLimit = 50000; }
            var moved = new Mesh2Input { Segments = input.Segments, Rings = input.Rings, SnapTolerance = input.SnapTolerance * s.Scale,
                MinAngleDeg = input.MinAngleDeg, SteinerLimit = input.SteinerLimit, X = new double[input.Count], Y = new double[input.Count] };
            for (int i = 0; i < input.Count; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var candidate = CandidateRegistry.For(milestone);
            var output = candidate.Run(moved);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(moved, output, new OracleExpect { Milestone = milestone });
            Assert.True(report.Ok, report.ToString());
            if (s.IsExact) {
                var plain = candidate.Run(input);
                Assert.Equal(plain.VX.Select(v => v * s.Scale), output.VX);
                Assert.Equal(plain.VY.Select(v => v * s.Scale), output.VY);
                Assert.Equal(plain.Triangles.SelectMany(t => t), output.Triangles.SelectMany(t => t));
            }
        }

        [Fact]
        public void QualityMesher_WithTolerance_Reuse_GivesTheSameMesh()
        {
            // review of #277: one QualityMesh2 instance, two different inputs in turn, then the first again
            var a = Segments(0.3, 0.3, 4.3, 4.3, 0.3, 4.3, 4.3, 0.3, 0, 0, 0.001, 4);
            var b = Segments(0, 0, 8, 1, 8, 1, 2, 7, 2, 7, 0, 0, 1, 3, 5, 2.1);
            var mesher = new QualityMesh2 { SnapTolerance = 1.0 / 4, MinAngle = 25 };
            var first = mesher.Compute(a);
            var other = mesher.Compute(b);
            var again = mesher.Compute(a);
            var fresh = new QualityMesh2 { SnapTolerance = 1.0 / 4, MinAngle = 25 }.Compute(b);
            Assert.Equal(Triangulation2Status.Success, first.Status);
            Assert.Equal(first.Mesh.Vertices, again.Mesh.Vertices);
            Assert.Equal(first.Mesh.Triangles, again.Mesh.Triangles);
            Assert.Equal(fresh.Mesh.Vertices, other.Mesh.Vertices);
            Assert.Equal(fresh.Mesh.Triangles, other.Mesh.Triangles);
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
        public void EndpointOnTheTopOrRightSide_DoesNotTouch_TheBottomLeftCornerDoes()
        {
            // review of #277: the contact rules for a segment that only touches the pixel at an end point, or passes through a corner
            var cases = new[] {
                (-3.0, 3.0, 0.0, 0.5, false),   // ends on the top side
                (3.0, 3.0, 0.5, 0.0, false),    // ends on the right side
                (-3.0, -3.0, 0.0, -0.5, true),  // ends on the bottom side
                (-3.0, 3.0, -0.5, 0.0, true),   // ends on the left side
            };
            foreach (var (ax, ay, bx, by, through) in cases) {
                var input = Segments(ax, ay, bx, by);
                input.Points.Add(new Vector2d(0, 0));
                var snap = SnapRounding2.Apply(input, 1);
                bool routed = snap.ChainPoints(0, 1).Contains(new Vector2d(0, 0));
                Assert.True(routed == through, "(" + ax + ", " + ay + ") - (" + bx + ", " + by + ")");
            }
            // a line through a pixel's corners only: x + y = -1 touches the bottom-left corner (-0.5, -0.5) of pixel (0, 0); x + y = 1 its
            // top-right corner (0.5, 0.5)
            foreach (var (c, through) in new[] { (-1.0, true), (1.0, false) }) {
                var input = Segments(-4, c + 4, 4, c - 4);
                input.Points.Add(new Vector2d(0, 0));
                var snap = SnapRounding2.Apply(input, 1);
                Assert.True(snap.ChainPoints(0, 1).Contains(new Vector2d(0, 0)) == through, "x + y = " + c);
            }
        }

        [Fact]
        public void HoleCollapsingToALine_DoesNotEmptyTheDomain()
        {
            // review of #277: a narrow hole of four segments with its seed; snapped at 0.5 its long sides merge onto one line
            var input = new ConstrainedMesh2Input();
            foreach (var (x, y) in new[] { (0.0, 0.0), (10, 0), (10, 10), (0, 10), (2, 5), (8, 5), (8, 5.1), (2, 5.1) }) input.Points.Add(new Vector2d(x, y));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            for (int i = 0; i < 4; ++i) input.Segments.Add(new Mesh2Segment(4 + i, 4 + (i + 1) % 4));
            input.HoleSeeds.Add(new Vector2d(5, 5.05));
            var result = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.5 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            double area = result.Mesh.Triangles.Sum(t => { var a = result.Mesh.Vertices[t.a]; var b = result.Mesh.Vertices[t.b]; var c = result.Mesh.Vertices[t.c]; return 0.5 * ((b - a).x * (c - a).y - (b - a).y * (c - a).x); });
            Assert.Equal(100, area, 9);
            Assert.Contains(result.Diagnostics, d => d.Contains("hole seed") && d.Contains("dropped"));
        }

        [Fact]
        public void SeedSweptBySnapping_IsDropped_OthersKept()
        {
            // review of #277: the hole's left side moves from x = 2.26 to 2.5, past the seed at 2.3; a seed in a hole on the grid stays,
            // and so does a seed at 2.6, within a pixel of the moved side but not swept by it (second review of #277)
            foreach (var (left, seedX, dropped, area0) in new[] { (2.26, 2.3, true, 100.0), (2.0, 2.3, false, 96.0), (2.26, 2.6, false, 97.0) }) {
                var input = new ConstrainedMesh2Input();
                foreach (var (x, y) in new[] { (0.0, 0.0), (10, 0), (10, 10), (0, 10), (left, 2), (4, 2), (4, 4), (left, 4) }) input.Points.Add(new Vector2d(x, y));
                input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
                for (int i = 0; i < 4; ++i) input.Segments.Add(new Mesh2Segment(4 + i, 4 + (i + 1) % 4));
                input.HoleSeeds.Add(new Vector2d(seedX, 3));
                var snap = SnapRounding2.Apply(input, 0.5);
                Assert.Equal(dropped ? 1 : 0, snap.DroppedSeeds);
                var result = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.5 }.Compute(input);
                double area = result.Mesh.Triangles.Sum(t => { var a = result.Mesh.Vertices[t.a]; var b = result.Mesh.Vertices[t.b]; var c = result.Mesh.Vertices[t.c]; return 0.5 * ((b - a).x * (c - a).y - (b - a).y * (c - a).x); });
                Assert.Equal(area0, area, 9);
            }
        }

        [Fact]
        public void BowtieRing_IsNotCollapsed()
        {
            // review of #277: a self-intersecting ring with zero signed area still encloses its two triangles (even-odd)
            var input = new ConstrainedMesh2Input();
            foreach (var (x, y) in new[] { (-10.0, -10.0), (10, -10), (10, 10), (-10, 10), (0, 0), (4, 4), (4, 0), (0, 4) }) input.Points.Add(new Vector2d(x, y));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            input.Rings.Add(new Mesh2Ring(new[] { 4, 5, 6, 7 }, true));
            var snap = SnapRounding2.Apply(input, 1);
            Assert.Equal(0, snap.CollapsedRings);
            Assert.Equal(2, snap.Result.Rings.Count);
        }

        [Fact]
        public void Heights_Regions_AndPointSegments_Survive()
        {
            var input = Segments(0.3, 0.3, 4.3, 4.3, 0.3, 4.3, 4.3, 0.3, 2, 1, 2, 1);
            input.Heights = new List<double> { 0, 4, 10, 30, 7, 7 };
            input.Points.Add(new Vector2d(-1, -1)); input.Points.Add(new Vector2d(6, -1)); input.Points.Add(new Vector2d(6, 6)); input.Points.Add(new Vector2d(-1, 6));
            input.Heights.AddRange(new[] { 0.0, 0, 0, 0 });
            input.Regions.Add(Mesh2Region.ByRing(new[] { 6, 7, 8, 9 }, 3));
            var snap = SnapRounding2.Apply(input, 1);
            Assert.Equal(snap.Result.Points.Count, snap.Result.Heights.Count);
            // the crossing at (2, 2): heights interpolated along its first segment, from 0 at (0.3, 0.3) to 4 at (4.3, 4.3)
            int crossing = snap.Result.Points.IndexOf(new Vector2d(2, 2));
            Assert.True(crossing >= 10);
            Assert.Equal(1.7 / 4 * 4, snap.Result.Heights[crossing], 9);
            Assert.Single(snap.Result.Regions);
            // the segment between two copies of (2, 1) has length zero: it collapses and is reported, its points stay
            Assert.Equal(1, snap.CollapsedSegments);
            Assert.Equal(new Vector2d(2, 1), snap.Result.Points[4]);
            var result = new ConstrainedDelaunayMesh2 { SnapTolerance = 1 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.Contains(result.Mesh.TriangleLabels, l => l == 3);
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
