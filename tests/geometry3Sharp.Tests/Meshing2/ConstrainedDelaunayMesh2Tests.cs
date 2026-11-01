using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The M2 mesher <see cref="ConstrainedDelaunayMesh2"/>. The catalogue rows of m2.cdt, m2.domain, m2.regions and m2.noding run
    /// through the oracle in Meshing2CatalogueTests; these tests add fuzzing, the CLAUDE.md requirements and the API.
    /// </summary>
    public class ConstrainedDelaunayMesh2Tests
    {
        static readonly OracleExpect M2 = new OracleExpect { Milestone = Milestone.M2 };

        static OracleReport Judge(Mesh2Input input, out Mesh2Output output)
        {
            output = new ConstrainedDelaunayMesh2Candidate().Run(input);
            return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M2 });
        }

        static Mesh2Input Square(double size = 10)
        {
            var i = new Mesh2Input { X = new[] { 0, size, size, 0 }, Y = new[] { 0, 0, size, size } };
            i.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            return i;
        }

        static int AddPoint(Mesh2Input input, double x, double y)
        {
            input.X = input.X.Concat(new[] { x }).ToArray();
            input.Y = input.Y.Concat(new[] { y }).ToArray();
            return input.Count - 1;
        }

        static void AddSegment(Mesh2Input input, double ax, double ay, double bx, double by, int marker = 0)
            => input.Segments.Add(new SegmentInput(AddPoint(input, ax, ay), AddPoint(input, bx, by), marker));

        /// <summary> random constraints of many kinds: integer crossings (F-1), near-parallel lines, UTM offsets, holes, regions </summary>
        static Mesh2Input Fuzz(int seed)
        {
            var r = new Random(seed);
            int style = seed % 5;
            double offset = style == 3 ? 5.7e6 : 0, unit = style == 3 ? 1.0 / 64 : 1;
            var input = new Mesh2Input { X = new[] { offset, offset + 10, offset + 10, offset }, Y = new[] { offset, offset, offset + 10, offset + 10 } };
            if (style != 4) input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            int m = 3 + r.Next(14);
            for (int i = 0; i < m; ++i) {
                switch (style) {
                    case 0: case 4:
                        AddSegment(input, r.Next(11), r.Next(11), r.Next(11), r.Next(11), r.Next(3)); break;
                    case 1: {   // nearly parallel lines with tiny angles
                        double y0 = 1 + r.Next(8) + r.Next(1 << 10) * Math.Pow(2, -40), y1 = y0 + (r.Next(5) - 2) * Math.Pow(2, -30);
                        AddSegment(input, r.NextDouble(), y0, 9 + r.NextDouble(), y1, i); break;
                    }
                    case 2:
                        AddSegment(input, r.NextDouble() * 10, r.NextDouble() * 10, r.NextDouble() * 10, r.NextDouble() * 10); break;
                    default:
                        AddSegment(input, offset + r.Next(640) * unit, offset + r.Next(640) * unit, offset + r.Next(640) * unit, offset + r.Next(640) * unit); break;
                }
            }
            if (r.Next(3) == 0) {   // a hole, maybe crossed by segments
                double cx = offset + 2 + r.Next(6), cy = offset + 2 + r.Next(6);
                int a = AddPoint(input, cx, cy), b = AddPoint(input, cx + 1, cy), c = AddPoint(input, cx + 1, cy + 1), d = AddPoint(input, cx, cy + 1);
                input.Rings.Add(new RingInput(new[] { a, b, c, d }, true, 2));
            }
            if (r.Next(3) == 0) input.Regions.Add(new RegionInput { Seed = new[] { offset + 0.5, offset + 0.25 }, Label = 7 });
            return input;
        }

        [Fact]
        public void Fuzz_EveryResultPassesTheOracle()
        {
            for (int seed = 0; seed < 400; ++seed) {
                var input = Fuzz(seed);
                var report = Judge(input, out var output);
                Assert.True(output.Status != Mesh2Status.Failed, "seed " + seed + ": " + output.FailureKind);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
            }
        }

        static Mesh2Input Sample()
        {
            var input = Square();
            AddSegment(input, 2, 4, 4, 8, 3); AddSegment(input, 1, 5, 6, 6, 4); AddSegment(input, 1, 7, 5, 3, 5);   // F-1/a
            int a = AddPoint(input, 6, 1), b = AddPoint(input, 9, 1), c = AddPoint(input, 9, 3), d = AddPoint(input, 6, 3);
            input.Rings.Add(new RingInput(new[] { a, b, c, d }, true, 2));
            AddSegment(input, 7, 0.5, 8, 3.5);   // crosses the hole
            input.Regions.Add(new RegionInput { Seed = new[] { 0.5, 9.5 }, Label = 4 });
            return input;
        }

        static string[] Canonical(Mesh2Output o) =>
            o.Triangles.Select(t => string.Join(" ", t)).OrderBy(s => s, StringComparer.Ordinal)
            .Concat(o.Subsegments.Select(s => "S " + Math.Min(s.A, s.B) + " " + Math.Max(s.A, s.B) + " " + s.Marker).OrderBy(s => s, StringComparer.Ordinal)).ToArray();

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_KeepsTheResult(string name)
        {
            var s = Similarity.All().First(x => x.Name == name);
            var input = Sample();
            var e = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(e.x * e.x + e.y * e.y), cos = e.x / norm, sin = e.y / norm;
            var moved = Sample();
            for (int i = 0; i < input.Count; ++i) {
                moved.X[i] = s.Scale * (cos * input.X[i] - sin * input.Y[i]) + s.Translation.x;
                moved.Y[i] = s.Scale * (sin * input.X[i] + cos * input.Y[i]) + s.Translation.y;
            }
            moved.Regions[0].Seed = new[] { s.Scale * (cos * 0.5 - sin * 9.5) + s.Translation.x, s.Scale * (sin * 0.5 + cos * 9.5) + s.Translation.y };
            Assert.True(Judge(input, out var a).Ok);
            var report = Judge(moved, out var b);
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                // 2^k: the same mesh bit for bit, and the crossings scaled exactly
                Assert.Equal(Canonical(a), Canonical(b));
                for (int i = 0; i < a.VX.Length; ++i) { Assert.Equal(a.VX[i] * s.Scale, b.VX[i]); Assert.Equal(a.VY[i] * s.Scale, b.VY[i]); }
                Assert.Equal(a.TriangleLabels, b.TriangleLabels);
            } else Assert.Equal(a.Triangles.Length, b.Triangles.Length);
        }

        [Fact]
        public void Reuse_And_Parallel_RunsAreIdentical()
        {
            var mesher = new ConstrainedDelaunayMesh2();
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(10));
            var first = mesher.Compute(input).Mesh;
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, results.Length, i => results[i] = mesher.Compute(input).Mesh);
            foreach (var m in results.Concat(new[] { mesher.Compute(input).Mesh })) {
                Assert.Equal(first.Vertices, m.Vertices);
                Assert.Equal(first.Triangles, m.Triangles);
                Assert.Equal(first.Neighbours, m.Neighbours);
                Assert.Equal(first.Subsegments.Select(s => (s.A, s.B, s.Marker)), m.Subsegments.Select(s => (s.A, s.B, s.Marker)));
                Assert.Equal(first.TriangleLabels, m.TriangleLabels);
            }
            // the input is not changed
            Assert.Equal(ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(10)).Points, input.Points);
        }

        [Fact]
        public void InvalidInput_IsAnArgumentError_NamingIt()
        {
            var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0, 1) } };
            input.Segments.Add(new Mesh2Segment(0, 7));
            var e = Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2().Compute(input));
            Assert.Contains("point 7", e.Message);
            input.Segments.Clear();
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, -1 }));
            Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2().Compute(input));
            input.Rings.Clear();
            input.Points[1] = new Vector2d(double.NaN, 0);
            Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2().Compute(input));
            input.Points[1] = new Vector2d(1, 0);
            input.Heights = new List<double> { 1, 2 };
            Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2().Compute(input));
            Assert.Throws<ArgumentNullException>(() => new ConstrainedDelaunayMesh2().Compute(null));
        }

        [Fact]
        public void Crossings_WithNodingOff_FailWithTheCoordinate()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Sample());
            var result = new ConstrainedDelaunayMesh2 { ResolveCrossings = false }.Compute(input);
            Assert.Equal(Triangulation2Status.Failed, result.Status);
            Assert.Equal(Triangulation2FailureKind.CrossingConstraints, result.FailureKind);
            Assert.Null(result.Mesh);
            Assert.True(result.ProblemCoordinate.HasValue);
            var p = result.ProblemCoordinate.Value;
            Assert.True(p.x > 0 && p.x < 10 && p.y > 0 && p.y < 10);
        }

        static BigInteger Int(double v, int emin)
        {
            ExactPoint3.Decompose(v, out var m, out int e);
            return m.IsZero ? BigInteger.Zero : m << (e - emin);
        }

        [Fact]
        public void Intersection_IsTheExactCrossingRounded()
        {
            var r = new Random(4);
            for (int i = 0; i < 2000; ++i) {
                double s = Math.Pow(2, r.Next(-600, 200));
                var a = new Vector2d(r.NextDouble() * s, r.NextDouble() * s);
                var b = new Vector2d(r.NextDouble() * s, r.NextDouble() * s);
                var c = new Vector2d(r.NextDouble() * s, r.NextDouble() * s);
                var d = new Vector2d(r.NextDouble() * s, r.NextDouble() * s);
                if (ExactPredicates.Orient2d(a, b, c) * ExactPredicates.Orient2d(a, b, d) >= 0) continue;
                var x = ConstrainedDelaunayMesh2.IntersectionForTests(a, b, c, d);
                // the exact crossing x* = a + t (b - a): |x - x*| at most one unit in the last place in each coordinate
                const int emin = -1200;
                BigInteger ax = Int(a.x, emin), ay = Int(a.y, emin), bx = Int(b.x, emin), by = Int(b.y, emin), cx = Int(c.x, emin), cy = Int(c.y, emin), dx = Int(d.x, emin), dy = Int(d.y, emin);
                BigInteger den = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx), num = (cx - ax) * (dy - cy) - (cy - ay) * (dx - cx);
                if (den.Sign < 0) { den = -den; num = -num; }
                foreach (var (got, start, delta) in new[] { (x.x, ax, bx - ax), (x.y, ay, by - ay) }) {
                    BigInteger exact = start * den + num * delta;   // x* times den
                    double ulp = Math.Abs(got) * Math.Pow(2, -52) + double.Epsilon;
                    Assert.True(BigInteger.Abs(Int(got, emin) * den - exact) <= Int(ulp, emin) * den, "case " + i);
                }
            }
        }

        [Fact]
        public void Heights_ArePassedOn_AndInterpolatedAtCrossings()
        {
            var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(4, 4), new Vector2d(0, 4), new Vector2d(4, 0), new Vector2d(4, 4) } };
            input.Heights = new List<double> { 0, 8, 1, 3, 9 };
            input.Segments.Add(new Mesh2Segment(0, 1)); input.Segments.Add(new Mesh2Segment(2, 3));
            var result = new ConstrainedDelaunayMesh2().Compute(input);
            var m = result.Mesh;
            Assert.Equal(6, m.Vertices.Count);
            Assert.Equal(new Vector2d(2, 2), m.Vertices[5]);
            Assert.Equal(new double[] { 0, 8, 1, 3, 8, 4 }, m.Heights);   // duplicate 4: the lower index wins; the crossing: along the segment given first, 0-1
            Assert.Contains(result.Diagnostics, d => d.Contains("another height"));
        }

        [Fact]
        public void TryLocate_RespectsANonConvexDomain()
        {
            // an L: the notch [1,2]x[1,2] is outside
            var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(2, 1), new Vector2d(1, 1), new Vector2d(1, 2), new Vector2d(0, 2) } };
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4, 5 }));
            var m = new ConstrainedDelaunayMesh2().Compute(input).Mesh;
            Assert.Equal(4, m.TriangleCount);
            Assert.False(m.TryLocate(new Vector2d(1.5, 1.5), out int t)); Assert.Equal(-1, t);
            Assert.True(m.TryLocate(new Vector2d(1.5, 1), out t));      // on the boundary
            Assert.True(m.TryLocate(new Vector2d(1, 1), out t));        // the reflex vertex
            Assert.True(m.TryLocate(new Vector2d(0.25, 0.25), out t));
            Assert.False(m.TryLocate(new Vector2d(3, 3), out _));
            for (int i = 0; i < m.TriangleCount; ++i) {
                var tri = m.Triangles[i];
                var c = (m.Vertices[tri.a] + m.Vertices[tri.b] + m.Vertices[tri.c]) / 3;
                Assert.True(m.TryLocate(c, out int got)); Assert.Equal(i, got);
            }
        }

        [Fact]
        public void Seeds_AndNestedRings_AreReported()
        {
            var input = new ConstrainedMesh2Input();
            foreach (var p in new[] { (0, 0), (10, 0), (10, 10), (0, 10), (2, 2), (8, 2), (8, 8), (2, 8), (4, 4), (6, 4), (6, 6), (4, 6) })
                input.Points.Add(new Vector2d(p.Item1, p.Item2));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            input.Rings.Add(new Mesh2Ring(new[] { 4, 5, 6, 7 }, hole: true));
            input.Rings.Add(new Mesh2Ring(new[] { 8, 9, 10, 11 }));            // an island in the hole: removed (D5), with a diagnostic
            input.HoleSeeds.Add(new Vector2d(20, 20));                          // outside
            input.HoleSeeds.Add(new Vector2d(5, 2));                            // on a constraint
            input.Regions.Add(Mesh2Region.BySeed(new Vector2d(1, 1), 5));
            input.Regions.Add(Mesh2Region.BySeed(new Vector2d(5, 5), 6));       // in the hole
            var result = new ConstrainedDelaunayMesh2().Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            var m = result.Mesh;
            Assert.All(m.TriangleLabels, l => Assert.Equal(5, l));
            double area = m.Triangles.Sum(t => 0.5 * ((m.Vertices[t.b].x - m.Vertices[t.a].x) * (m.Vertices[t.c].y - m.Vertices[t.a].y) - (m.Vertices[t.b].y - m.Vertices[t.a].y) * (m.Vertices[t.c].x - m.Vertices[t.a].x)));
            Assert.Equal(64, area, 12);
            Assert.Contains(result.Diagnostics, d => d.Contains("outer ring 2") && d.Contains("inside a hole"));
            Assert.Contains(result.Diagnostics, d => d.Contains("hole seed 0") && d.Contains("outside"));
            Assert.Contains(result.Diagnostics, d => d.Contains("hole seed 1") && d.Contains("constraint"));
            Assert.Contains(result.Diagnostics, d => d.Contains("region seed 1"));
        }

        [Fact]
        public void Regions_LastWins_AndRingsLabel()
        {
            var input = Square();
            int a = AddPoint(input, 2, 2), b = AddPoint(input, 5, 2), c = AddPoint(input, 5, 5), d = AddPoint(input, 2, 5);
            input.Regions.Add(new RegionInput { Seed = new[] { 1.0, 1.0 }, Label = 1 });
            input.Regions.Add(new RegionInput { Ring = new[] { a, b, c, d }, Label = 2 });
            input.Regions.Add(new RegionInput { Seed = new[] { 9.0, 9.0 }, Label = 3 });   // the same cell as the first: wins outside the ring
            var report = Judge(input, out var o);
            Assert.True(report.Ok, report.ToString());
            Assert.Contains(2, o.TriangleLabels);
            Assert.Contains(3, o.TriangleLabels);
            Assert.DoesNotContain(1, o.TriangleLabels);
        }

        [Fact]
        public void Cancellation_Throws()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Sample());
            Assert.Throws<OperationCanceledException>(() => new ConstrainedDelaunayMesh2().Compute(input, new CancellationToken(true)));
        }

        [Fact]
        public void ManyCrossings_StayLinearish()
        {
            // a grid of 60 x 60 crossing lines: 3600 crossings, each found by a walk, no quadratic search
            var input = new Mesh2Input { X = new double[0], Y = new double[0] };
            for (int i = 0; i < 60; ++i) { AddSegment(input, 0.5 + i, 0.25, 0.5 + i, 60.25); AddSegment(input, 0.25, 0.5 + i, 60.25, 0.5 + i); }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.Equal(240 + 3600, o.VX.Length);
            Assert.True(watch.Elapsed.TotalSeconds < 20, watch.Elapsed.ToString());
        }

        [Fact]
        public void ManyRandomCrossings_MakeOneVertexPerCrossing()
        {
            // 750 random segments, about 50 000 crossings: once failed at the step limit, which grew with the input and not the output
            var r = new Random(12);
            var input = new ConstrainedMesh2Input();
            for (int i = 0; i < 750; ++i) {
                input.Points.Add(new Vector2d(r.NextDouble(), r.NextDouble())); input.Points.Add(new Vector2d(r.NextDouble(), r.NextDouble()));
                input.Segments.Add(new Mesh2Segment(2 * i, 2 * i + 1));
            }
            long crossings = 0;
            var p = input.Points;
            for (int i = 0; i < 750; ++i)
                for (int j = i + 1; j < 750; ++j) {
                    Vector2d a = p[2 * i], b = p[2 * i + 1], c = p[2 * j], d = p[2 * j + 1];
                    if (ExactPredicates.Orient2d(a, b, c) * ExactPredicates.Orient2d(a, b, d) < 0 && ExactPredicates.Orient2d(c, d, a) * ExactPredicates.Orient2d(c, d, b) < 0) ++crossings;
                }
            var result = new ConstrainedDelaunayMesh2().Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.True(crossings > 30000, crossings.ToString());
            Assert.Equal(1500 + crossings, result.Mesh.Vertices.Count);
            var m = result.Mesh;
            foreach (var t in m.Triangles)
                Assert.True(ExactPredicates.Orient2d(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c]) > 0);
        }

        // ------------------------------------------------------------ regressions of the M2 review

        [Fact]
        public void Crossing_LeavesAConstrainedDelaunayTriangulation()
        {
            // the constraint removed before a rounded crossing was inserted left a non-Delaunay edge (17-15 against 18)
            var i = new Mesh2Input { X = new[] { 0, 10, 10, 0, 0.5000000001506211, 9.5, 0.500000000631652, 9.5, 0.5000000005322374, 9.5, 6.342704406167709, 8.145161852308158, 0.5000000009008339 },
                Y = new[] { 0, 0, 10, 10, 5.000000000004121, 4.999999999995936, 4.999999999967287, 5.00000000003277, 5.000000000000192, 4.999999999999808, 8.262465707148642, 8.874103468318518, 5.000000000002075 } };
            i.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            foreach (var (a, b, m) in new[] { (4, 5, 0), (6, 7, 1), (8, 9, 2), (1, 10, 3), (1, 11, 4) }) i.Segments.Add(new SegmentInput(a, b, m));
            var report = Judge(i, out _);
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void Markers_SeeSourcesHiddenBehindAFreePoint()
        {
            var i = Square();
            foreach (var (x, y) in new[] { (0.5000000004453223, 4.999999999999801), (9.5, 5.000000000000313), (0.5000000004932587, 4.999999999999773), (0.5000000008699738, 5.000000000000384), (0.5000000002750816, 5.0000000000081855), (9.5, 4.9999999999918145) })
                AddPoint(i, x, y);
            i.Segments.Add(new SegmentInput(4, 5, 5)); i.Segments.Add(new SegmentInput(8, 9, 8));
            var report = Judge(i, out _);
            Assert.True(report.Ok, report.ToString());
        }

        [Theory]
        [InlineData(-560)]
        [InlineData(-1000)]
        [InlineData(500)]
        public void ExtremeScales_GiveTheSameMesh(int exponent)
        {
            // tolerance tests in doubles underflowed: a failure at 2^-560, other meshes and markers below
            double s = Math.Pow(2, exponent);
            for (int seed = 0; seed < 60; ++seed) {
                var input = Fuzz(seed);
                if (input.X.Max(Math.Abs) * s > 1e70) continue;
                var scaled = Fuzz(seed);
                for (int k = 0; k < input.Count; ++k) { scaled.X[k] = input.X[k] * s; scaled.Y[k] = input.Y[k] * s; }
                foreach (var r in scaled.Regions) r.Seed = new[] { r.Seed[0] * s, r.Seed[1] * s };
                Assert.True(Judge(input, out var a).Ok);
                var report = Judge(scaled, out var b);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
                Assert.Equal(Canonical(a), Canonical(b));
            }
        }

        [Fact]
        public void CrossingHeights_DoNotDependOnTheOrder()
        {
            ConstrainedMesh2 Run(bool reversed)
            {
                var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) }, Heights = new List<double> { 0, 100, 0, 100 } };
                var s = new[] { new Mesh2Segment(0, 2), new Mesh2Segment(1, 3) };
                if (reversed) Array.Reverse(s);
                input.Segments.AddRange(s);
                return new ConstrainedDelaunayMesh2().Compute(input).Mesh;
            }
            var a = Run(false); var b = Run(true);
            Assert.Equal(5, a.Vertices.Count);
            // segment 0-2 has heights 0..0, segment 1-3 100..100: the one given first wins, whatever the insertion order
            Assert.Equal(0, a.Heights[4]);
            Assert.Equal(100, b.Heights[4]);
        }

        [Fact]
        public void RatioToDouble_RoundsToNearestEven_InTheNormalRange()
        {
            var r = new Random(8);
            for (int i = 0; i < 20000; ++i) {
                var num = new BigInteger(r.Next()) * new BigInteger(r.Next()) * new BigInteger(r.Next()) + r.Next();
                var den = new BigInteger(r.Next(1, int.MaxValue)) * new BigInteger(r.Next(1, 1000));
                int scale = r.Next(-900, 900);
                double x = ConstrainedDelaunayMesh2.RatioToDoubleForTests(num, den, scale);
                // x = M 2^E with a 53-bit M; |x - num / den 2^scale| <= 2^(E-1), and at exactly half, M is even
                long bits = BitConverter.DoubleToInt64Bits(x);
                BigInteger M = (bits & 0xFFFFFFFFFFFFFL) | (1L << 52);
                int E = (int)(((bits >> 52) & 0x7FF) - 1075);
                int low = Math.Min(E - 1, scale);
                BigInteger lhs = BigInteger.Abs(((M * den) << (E - low)) - (num << (scale - low)));
                BigInteger half = den << (E - 1 - low);
                Assert.True(lhs <= half, "case " + i);
                if (lhs == half) Assert.True(M.IsEven, "tie not to even, case " + i);
            }
            // a constructed tie: (2^53 + 1) / 2 = 2^52 + 0.5 rounds to the even 2^52
            Assert.Equal(Math.Pow(2, 52), ConstrainedDelaunayMesh2.RatioToDoubleForTests((BigInteger.One << 53) + 1, 2, 0));
            Assert.Equal(Math.Pow(2, 52) + 2, ConstrainedDelaunayMesh2.RatioToDoubleForTests((BigInteger.One << 53) + 3, 2, 0));
        }
    }
}
