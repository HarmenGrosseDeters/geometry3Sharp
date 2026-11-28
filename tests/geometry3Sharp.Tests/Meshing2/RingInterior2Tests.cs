using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class RingInterior2Tests
    {
        static void AssertStrictlyInside(IReadOnlyList<Vector2d> ring, Vector2d p)
        {
            Assert.False(double.IsNaN(p.x) || double.IsInfinity(p.x));
            Assert.False(double.IsNaN(p.y) || double.IsInfinity(p.y));
            Assert.True(IndependentStrictlyInside(ring, p),
                $"Point ({p.x:R}, {p.y:R}) is not strictly inside the ring under independent exact even-odd parity.");
        }

        // Independent exact even-odd point-in-ring test (casts vertical ray upward, independent of RingInterior2's horizontal ray).
        static bool IndependentStrictlyInside(IReadOnlyList<Vector2d> ring, Vector2d p)
        {
            int n = ring.Count;
            if (n < 3) return false;
            bool inside = false;
            for (int i = 0; i < n; ++i) {
                var a = ring[i];
                var b = ring[(i + 1) % n];
                if (a == b) continue;
                if (p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x)
                    && p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y)
                    && ExactPredicates.Orient2d(a, b, p) == 0)
                    return false;
                var left = a.x <= b.x ? a : b;
                var right = a.x <= b.x ? b : a;
                if (left.x <= p.x && p.x < right.x && ExactPredicates.Orient2d(left, right, p) < 0)
                    inside = !inside;
            }
            return inside;
        }

        static List<Vector2d> MakeStar(int spikes, double rInner, double rOuter, double cx, double cy, double scale, int seed)
        {
            var rng = new Random(seed);
            var ring = new List<Vector2d>(2 * spikes);
            for (int i = 0; i < 2 * spikes; ++i) {
                double angle = (2.0 * Math.PI * i) / (2 * spikes) + (rng.NextDouble() - 0.5) * (0.3 / spikes);
                double baseR = (i & 1) == 0 ? rOuter : rInner;
                double r = baseR * (0.7 + 0.6 * rng.NextDouble()) * scale;
                ring.Add(new Vector2d(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
            }
            return ring;
        }

        [Fact]
        public void F50_ThinObtuseAndStarContours_FindStrictInterior()
        {
            // 1. Very thin rectangles (axis-aligned & rotated, width orders of magnitude below length)
            double[] widths = { 1e-4, 1e-8, 1e-12 };
            foreach (double w in widths) {
                var horiz = new[] { new Vector2d(0, 0), new Vector2d(1000, 0), new Vector2d(1000, w), new Vector2d(0, w) };
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(horiz, out var ph));
                AssertStrictlyInside(horiz, ph);

                var vert = new[] { new Vector2d(0, 0), new Vector2d(w, 0), new Vector2d(w, 1000), new Vector2d(0, 1000) };
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(vert, out var pv));
                AssertStrictlyInside(vert, pv);

                // 37-degree rotated thin rectangle
                double ca = Math.Cos(0.65), sa = Math.Sin(0.65);
                var rot = horiz.Select(p => new Vector2d(ca * p.x - sa * p.y, sa * p.x + ca * p.y)).ToArray();
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(rot, out var pr));
                AssertStrictlyInside(rot, pr);
            }

            // 2. Valid obtuse triangles at scales 1e-3 and 1e6 (plus UTM offset)
            double[] triScales = { 1e-3, 1.0, 1e6 };
            foreach (double s in triScales) {
                var obtuse = new[] {
                    new Vector2d(0, 0),
                    new Vector2d(s, 0),
                    new Vector2d(100.0 * s, 1e-4 * s)
                };
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(obtuse, out var pt));
                AssertStrictlyInside(obtuse, pt);

                // Shifted to UTM offset
                var utm = obtuse.Select(p => new Vector2d(p.x + 500000.0, p.y + 5700000.0)).ToArray();
                if (s >= 1.0) {
                    Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(utm, out var putm));
                    AssertStrictlyInside(utm, putm);
                }
            }

            // 3. Random star polygons across multiple scales
            double[] starScales = { 1e-6, 1e-3, 1.0, 1e3, 1e6 };
            foreach (double s in starScales) {
                for (int seed = 1; seed <= 8; ++seed) {
                    var star = MakeStar(12, 0.05, 1.0, 0, 0, s, seed);
                    Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(star, out var ps));
                    AssertStrictlyInside(star, ps);
                }
            }
        }

        [Fact]
        public void F49_RandomTriangularHolesAtUtmScale_SeedsAreStrictlyInsideAndCutExactHole()
        {
            var rng = new Random(49);
            const double ox = 500000.0, oy = 5700000.0;
            for (int i = 0; i < 154; ++i) {
                var a = new Vector2d(ox + 1 + rng.NextDouble() * 60, oy + 1 + rng.NextDouble() * 60);
                var b = new Vector2d(ox + 1 + rng.NextDouble() * 60, oy + 1 + rng.NextDouble() * 60);
                var c = new Vector2d(ox + 1 + rng.NextDouble() * 60, oy + 1 + rng.NextDouble() * 60);
                if (ExactPredicates.Orient2d(a, b, c) == 0) continue;
                var tri = new[] { a, b, c };
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(tri, out var seed));
                AssertStrictlyInside(tri, seed);

                // Verify seed works as a hole seed in ConstrainedDelaunayMesh2 on a subset to keep runtime fast
                if (i < 15) {
                    var input = new ConstrainedMesh2Input();
                    input.Points.AddRange(new[] {
                        new Vector2d(ox, oy), new Vector2d(ox + 64, oy),
                        new Vector2d(ox + 64, oy + 64), new Vector2d(ox, oy + 64),
                        a, b, c
                    });
                    input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false));
                    input.Segments.Add(new Mesh2Segment(4, 5));
                    input.Segments.Add(new Mesh2Segment(5, 6));
                    input.Segments.Add(new Mesh2Segment(6, 4));
                    input.HoleSeeds.Add(seed);
                    var res = new ConstrainedDelaunayMesh2().Compute(input);
                    Assert.Equal(Triangulation2Status.Success, res.Status);
                    double meshArea = 0;
                    for (int t = 0; t < res.Mesh.TriangleCount; ++t) {
                        var idx = res.Mesh.Triangles[t];
                        var p0 = res.Mesh.Vertices[idx.a];
                        var p1 = res.Mesh.Vertices[idx.b];
                        var p2 = res.Mesh.Vertices[idx.c];
                        meshArea += 0.5 * ((p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x));
                    }
                    double triArea = 0.5 * Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                    double expected = 64.0 * 64.0 - triArea;
                    Assert.True(Math.Abs(meshArea - expected) < 1e-6,
                        $"Seed {i} produced wrong area {meshArea} vs expected {expected}");
                }
            }
        }

        [Fact]
        public void UltraNarrowExactFallback_FindsPointWhenOneExists_AndReturnsFalseWhenNoneExists()
        {
            double ulp = Math.Pow(2, -52); // 1 ULP at 1.0

            // 3-ULP right triangle: (1 + ulp, 1 + ulp) is strictly inside (only interior double!)
            var threeUlp = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + 3.0 * ulp, 1.0),
                new Vector2d(1.0, 1.0 + 3.0 * ulp)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(threeUlp, out var p3));
            AssertStrictlyInside(threeUlp, p3);
            Assert.Equal(1.0 + ulp, p3.x);
            Assert.Equal(1.0 + ulp, p3.y);

            // Same 3-ULP triangle with a 4th vertex on the bottom edge (skips triangle fast path and misses Phase 1 midpoints,
            // requiring Phase 2 exact rational scanline fallback with near-endpoint probe).
            var threeUlpQuad = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + ulp, 1.0),
                new Vector2d(1.0 + 3.0 * ulp, 1.0),
                new Vector2d(1.0, 1.0 + 3.0 * ulp)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(threeUlpQuad, out var p3q));
            AssertStrictlyInside(threeUlpQuad, p3q);
            Assert.Equal(1.0 + ulp, p3q.x);
            Assert.Equal(1.0 + ulp, p3q.y);

            // 2-ULP right triangle: (1 + ulp, 1 + ulp) lies ON the hypotenuse; NO binary64 point is strictly inside!
            var twoUlp = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + 2.0 * ulp, 1.0),
                new Vector2d(1.0, 1.0 + 2.0 * ulp)
            };
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(twoUlp, out _));

            // 1-ULP right triangle: no binary64 coordinate even between vertices
            var oneUlp = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + ulp, 1.0),
                new Vector2d(1.0, 1.0 + ulp)
            };
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(oneUlp, out _));

            // Diagonal sliver at UTM scale with width 4 ULPs (around 5.7e6, ULP is 2^-30)
            double utmUlp = Math.Pow(2, -30);
            double bx = 500000.0, by = 5700000.0;
            var utmSliver = new[] {
                new Vector2d(bx, by),
                new Vector2d(bx + 100.0, by + 100.0),
                new Vector2d(bx + 100.0 + 4.0 * utmUlp, by + 100.0)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(utmSliver, out var pUtm));
            AssertStrictlyInside(utmSliver, pUtm);

            // Nearly-vertical 2-ULP-wide sliver: X spans only 2 ULPs (1.0 -> 1.0 + 2*ulp), Y spans 100;
            // forces exact horizontal scanline fallback (vertical slabs have xMid on/outside boundary).
            var narrowVertical = new[] {
                new Vector2d(1.0, 0.0),
                new Vector2d(1.0 + 2.0 * ulp, 100.0),
                new Vector2d(1.0, 100.0),
                new Vector2d(1.0, 50.0) // 4 vertices so triangle fast path does not run
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(narrowVertical, out var pVert));
            AssertStrictlyInside(narrowVertical, pVert);

            // Symmetric nearly-horizontal 2-ULP-tall sliver: forces exact vertical scanline fallback.
            var narrowHorizontal = new[] {
                new Vector2d(0.0, 1.0),
                new Vector2d(100.0, 1.0),
                new Vector2d(100.0, 1.0 + 2.0 * ulp),
                new Vector2d(50.0, 1.0)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(narrowHorizontal, out var pHoriz));
            AssertStrictlyInside(narrowHorizontal, pHoriz);

            // Reflex vertex at exact same Y as candidate midpoint: tests half-open ray parity (no vertex double-count).
            var chevron = new[] {
                new Vector2d(0, 0), new Vector2d(10, 5), new Vector2d(0, 10), new Vector2d(4, 5)
            };
            Assert.True(RingInterior2.IsStrictlyInside(chevron, new Vector2d(5, 5)));
            Assert.False(RingInterior2.IsStrictlyInside(chevron, new Vector2d(2, 5)));
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(chevron, out var pChev));
            AssertStrictlyInside(chevron, pChev);
        }

        [Fact]
        public void ManyVerticesWithSingleTinyLobe_SweepFindsInterior()
        {
            // 100 vertices forming a folded staircase where 96 edges retrace identically and ONE tiny non-quantile
            // narrow triangle sits at slab index 3 (between quantiles 0 and 12, and narrower than the 8 widest slabs).
            var bump = new List<Vector2d>();
            double y = 0;
            for (int i = 0; i < 100; ++i) {
                bump.Add(new Vector2d(i * 10.0, y));
                if (i == 3) {
                    // Tiny horizontal ear completely inside slab [y, y + 1e-4], avoiding quantile/widest slabs
                    bump.Add(new Vector2d(i * 10.0 + 1.0, y + 5e-5));
                    bump.Add(new Vector2d(i * 10.0, y + 1e-4));
                }
                y += i >= 90 ? 100.0 : (i == 3 ? 1e-4 : 1.0);
            }
            // Retrace the main spine back to the start (without retracing the tiny ear)
            double yr = y;
            for (int i = 99; i >= 0; --i) {
                yr -= i >= 90 ? 100.0 : (i == 3 ? 1e-4 : 1.0);
                if (i != 3) bump.Add(new Vector2d(i * 10.0, yr));
            }

            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(bump, out var pt));
            AssertStrictlyInside(bump, pt);
        }

        [Fact]
        public void RingConventions_ClosedOpenOrientationDuplicatesBowtieSpike()
        {
            // GIS closed ring vs open ring -> identical result
            var open = new[] { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 4), new Vector2d(3, 4), new Vector2d(3, 10), new Vector2d(0, 10) };
            var closed = open.Concat(new[] { open[0] }).ToArray();
            var reversed = Enumerable.Reverse(open).ToArray();

            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(open, out var pOpen));
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(closed, out var pClosed));
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(reversed, out var pRev));
            AssertStrictlyInside(open, pOpen);
            AssertStrictlyInside(open, pRev);
            Assert.Equal(pOpen, pClosed);

            // Consecutive duplicate vertices
            var withDups = new List<Vector2d> { open[0], open[0], open[1], open[2], open[2], open[3], open[4], open[5], open[0] };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(withDups, out var pDups));
            Assert.Equal(pOpen, pDups);

            // Indexed overload matches direct overload bit-for-bit
            var indices = new[] { 0, 1, 2, 3, 4, 5, 0 };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(open, indices, out var pIdx));
            Assert.Equal(pOpen, pIdx);

            // Self-intersecting bowtie (even-odd interior is two triangles)
            var bowtie = new[] { new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(2, 0), new Vector2d(0, 2) };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(bowtie, out var pBow));
            AssertStrictlyInside(bowtie, pBow);

            // Square with retraced spike: interior point must lie in square, NOT on spike
            var withSpike = new[] {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 5),
                new Vector2d(20, 5), new Vector2d(10, 5), // spike
                new Vector2d(10, 10), new Vector2d(0, 10)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(withSpike, out var pSpike));
            AssertStrictlyInside(withSpike, pSpike);
            Assert.InRange(pSpike.x, 0.0, 10.0);
        }

        [Fact]
        public void DegenerateRings_ReturnFalse()
        {
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(Array.Empty<Vector2d>(), out _));
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(1, 2) }, out _));
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(1, 2), new Vector2d(3, 4) }, out _));
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(1, 1), new Vector2d(1, 1), new Vector2d(1, 1) }, out _));
            // F-48/a,b,c degenerate rings
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(2, 2), new Vector2d(5, 5), new Vector2d(8, 8) }, out _));
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(2, 2), new Vector2d(8, 8), new Vector2d(2, 2) }, out _));
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(2, 2), new Vector2d(8, 8), new Vector2d(5, 5), new Vector2d(2, 2) }, out _));
            // Non-collinear folded zig-zag that retraces itself back to start
            var folded = new[] {
                new Vector2d(0, 0), new Vector2d(4, 2), new Vector2d(2, 7),
                new Vector2d(4, 2), new Vector2d(0, 0)
            };
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(folded, out _));
        }

        [Fact]
        public void ExtremeScales_AndNegativeZero_AreExact()
        {
            foreach (double s in new[] { 1e-160, 1e-60, 1e60 }) {
                var poly = new[] {
                    new Vector2d(0, 0), new Vector2d(10 * s, 0),
                    new Vector2d(10 * s, 2 * s), new Vector2d(2 * s, 2 * s),
                    new Vector2d(2 * s, 10 * s), new Vector2d(0, 10 * s)
                };
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(poly, out var p));
                AssertStrictlyInside(poly, p);
            }

            // Negative zero coordinates normalize cleanly
            var nz = new[] { new Vector2d(-0.0, -0.0), new Vector2d(2.0, -0.0), new Vector2d(-0.0, 2.0) };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(nz, out var pnz));
            AssertStrictlyInside(nz, pnz);
            Assert.False(BitConverter.DoubleToInt64Bits(pnz.x) == long.MinValue);
            Assert.False(BitConverter.DoubleToInt64Bits(pnz.y) == long.MinValue);
        }

        [Fact]
        public void InvalidInputs_ThrowArgumentErrors()
        {
            Assert.Throws<ArgumentNullException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(null, out _));
            Assert.Throws<ArgumentNullException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(null, new[] { 0, 1, 2 }, out _));
            Assert.Throws<ArgumentNullException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { Vector2d.Zero }, null, out _));

            var pts = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0, 1) };
            Assert.Throws<ArgumentOutOfRangeException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(pts, new[] { 0, 1, 3 }, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(pts, new[] { -1, 1, 2 }, out _));

            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(double.NaN, 0), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(0, double.PositiveInfinity), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(1e71, 0), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
        }

        [Fact]
        public void Similarity_All2D_IsStrictInterior_AndPow2ScalesAreBitIdentical()
        {
            var baseRing = new[] {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 3),
                new Vector2d(4, 3), new Vector2d(4, 8), new Vector2d(0, 8)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(baseRing, out var basePt));
            AssertStrictlyInside(baseRing, basePt);

            foreach (var sim in Similarity.All2D()) {
                var transformed = baseRing.Select(p => sim.Apply2d(p)).ToArray();
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(transformed, out var pt),
                    $"Failed for similarity {sim.Name}");
                AssertStrictlyInside(transformed, pt);

                if (sim.IsExact) {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(basePt.x * sim.Scale), BitConverter.DoubleToInt64Bits(pt.x));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(basePt.y * sim.Scale), BitConverter.DoubleToInt64Bits(pt.y));
                }
            }
        }

        [Fact]
        public void Determinism_RepeatedAndParallelRuns_AreBitIdentical()
        {
            var rings = Enumerable.Range(1, 64)
                .Select(seed => MakeStar(10, 0.1, 1.0, 500000.0, 5700000.0, 25.0, seed))
                .ToArray();

            var serial = new Vector2d[rings.Length];
            for (int i = 0; i < rings.Length; ++i) {
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(rings[i], out serial[i]));
                AssertStrictlyInside(rings[i], serial[i]);
            }

            var parallel = new Vector2d[rings.Length];
            Parallel.For(0, rings.Length, i => {
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(rings[i], out parallel[i]));
            });

            for (int i = 0; i < rings.Length; ++i) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(serial[i].x), BitConverter.DoubleToInt64Bits(parallel[i].x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(serial[i].y), BitConverter.DoubleToInt64Bits(parallel[i].y));
            }
        }
    }
}
