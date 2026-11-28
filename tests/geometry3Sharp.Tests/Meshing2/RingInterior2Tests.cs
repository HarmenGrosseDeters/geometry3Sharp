using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            // 2. Valid obtuse triangles at scales 1e-3, 1.0, and 1e6 (plus UTM offset including s = 1e-3)
            double[] triScales = { 1e-3, 1.0, 1e6 };
            foreach (double s in triScales) {
                var obtuse = new[] {
                    new Vector2d(0, 0),
                    new Vector2d(s, 0),
                    new Vector2d(100.0 * s, 1e-4 * s)
                };
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(obtuse, out var pt));
                AssertStrictlyInside(obtuse, pt);

                var utm = obtuse.Select(p => new Vector2d(p.x + 500000.0, p.y + 5700000.0)).ToArray();
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(utm, out var putm));
                AssertStrictlyInside(utm, putm);
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
        public void UltraNarrowExactFallback_AndBowtieCrossingAtMidpoint_WorkExact()
        {
            double ulp = Math.Pow(2, -52); // 1 ULP at 1.0

            // 3-ULP right triangle: (1 + ulp, 1 + ulp) is strictly inside (only interior double)
            var threeUlp = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + 3.0 * ulp, 1.0),
                new Vector2d(1.0, 1.0 + 3.0 * ulp)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(threeUlp, out var p3));
            AssertStrictlyInside(threeUlp, p3);
            Assert.Equal(1.0 + ulp, p3.x);
            Assert.Equal(1.0 + ulp, p3.y);

            // 4-vertex 3-ULP triangle: bypasses triangle fast path and misses Phase 1 midpoints,
            // exercising Phase 2a exact horizontal scanline fallback with near-endpoint probe.
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

            // 2-ULP right triangle: (1 + ulp, 1 + ulp) lies ON hypotenuse; NO binary64 point is strictly inside.
            var twoUlp = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + 2.0 * ulp, 1.0),
                new Vector2d(1.0, 1.0 + 2.0 * ulp)
            };
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(twoUlp, out _));

            // 1-ULP right triangle: no binary64 coordinate even between vertices.
            var oneUlp = new[] {
                new Vector2d(1.0, 1.0),
                new Vector2d(1.0 + ulp, 1.0),
                new Vector2d(1.0, 1.0 + ulp)
            };
            Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(oneUlp, out _));

            // Bowtie whose two diagonals cross exactly at (12, 12) = (xMid, yMid) of its only interior-width slabs:
            // must NOT early-exit on zero-width span at the crossing!
            double u49 = Math.Pow(2, -49); // 1 ULP in [8, 16)
            var crossingBowtie = new[] {
                new Vector2d(9.0, 9.0),
                new Vector2d(15.0, 15.0),
                new Vector2d(15.0 - u49, 15.0 + u49),
                new Vector2d(9.0 + u49, 9.0 - u49)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(crossingBowtie, out var pBowCross));
            AssertStrictlyInside(crossingBowtie, pBowCross);

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
        public void Phase2aVerticalAndPhase2bSweep_AreGenuinelyExercised()
        {
            double ulp = Math.Pow(2, -52);

            // Helper: build a ring with 32 non-cancelling split-edge collinear retraces on Y (so > 16 horizontal slabs
            // survive CancelDuplicateEdgesMod2) and ONE 3-ULP lobe at horizontal slab index 3 (unsampled by horizontal
            // Phase 1/2a: quantiles are 0,4,8,12,16,20,24,28 and widest slabs are indices >= 24), while X has only 2 slabs
            // so Phase 2a vertical exact scanline (ProbeExactVerticalSlab) hits it directly!
            var vertExactRing = new List<Vector2d>();
            double[] ySpine = new double[33];
            for (int i = 0; i <= 32; ++i)
                ySpine[i] = i < 24 ? 1.0 + i * 3.0 * ulp : 1.0 + 72.0 * ulp + (i - 23) * 1.0;

            // Forward along x = 1.0 through even+odd ySpine points, inserting the 3-ULP lobe at slab 3
            for (int i = 0; i < 32; ++i) {
                vertExactRing.Add(new Vector2d(1.0, ySpine[i]));
                if (i == 3) {
                    // 3-ULP right triangle in slab [ySpine[3], ySpine[4]] = [1 + 9*ulp, 1 + 12*ulp]
                    vertExactRing.Add(new Vector2d(1.0 + 3.0 * ulp, ySpine[3]));
                    vertExactRing.Add(new Vector2d(1.0, ySpine[4]));
                    continue;
                }
            }
            vertExactRing.Add(new Vector2d(1.0, ySpine[32]));
            // Return along x = 1.0 using DIFFERENT intermediate vertices (split at 1-step-shifted positions) so
            // CancelDuplicateEdgesMod2 does NOT cancel the vertical spine edges!
            for (int i = 31; i >= 1; i -= 2)
                vertExactRing.Add(new Vector2d(1.0, ySpine[i]));

            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(vertExactRing, out var pVertExact));
            AssertStrictlyInside(vertExactRing, pVertExact);

            // Now hide a sole tiny interior lobe in BOTH unsampled horizontal AND unsampled vertical slabs
            // (32 non-cancelling horizontal slabs AND 32 non-cancelling vertical slabs, exact integer coordinates
            // with zero FP drift!), so Phase 1 and Phase 2a both miss and Phase 2b SweepAllHorizontal finds it!
            var sweepRing = new List<Vector2d>();
            // Build an exact L-shaped retraced spine along x=0 (32 y-steps) and y=0 (32 x-steps), with the
            // return path skipping every other vertex so edges don't cancel mod 2.
            double[] coord = new double[33];
            for (int i = 0; i <= 32; ++i)
                coord[i] = i < 24 ? i * 4.0 : 96.0 + (i - 24) * 100.0;

            // Go up along x = 0 from y = 0 to y = coord[32], inserting a tiny lobe strictly inside
            // x in [coord[2], coord[3]] = [8, 12] and y in [coord[2], coord[3]] = [8, 12]
            // (indices 2 is neither a quantile 0,4,8,12,16,20,24,28 nor among the 8 widest slabs 24..31!).
            for (int i = 0; i <= 32; ++i) sweepRing.Add(new Vector2d(0, coord[i]));
            for (int i = 31; i >= 1; i -= 2) sweepRing.Add(new Vector2d(0, coord[i]));
            // Go right along y = 0 from x = 0 to x = coord[32], detouring via a retraced bridge to the tiny lobe at [8, 12]^2
            for (int i = 0; i <= 32; ++i) {
                sweepRing.Add(new Vector2d(coord[i], 0));
                if (i == 2) {
                    // Exact retraced bridge up to (8, 8), tiny square [8, 12] x [8, 12], exact retraced bridge back down
                    sweepRing.Add(new Vector2d(8, 8));
                    sweepRing.Add(new Vector2d(12, 8));
                    sweepRing.Add(new Vector2d(12, 12));
                    sweepRing.Add(new Vector2d(8, 12));
                    sweepRing.Add(new Vector2d(8, 8));
                    sweepRing.Add(new Vector2d(8, 0));
                }
            }
            for (int i = 31; i >= 1; i -= 2) sweepRing.Add(new Vector2d(coord[i], 0));

            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(sweepRing, out var pSweep));
            AssertStrictlyInside(sweepRing, pSweep);
            Assert.InRange(pSweep.x, 8.0, 12.0);
            Assert.InRange(pSweep.y, 8.0, 12.0);
        }

        [Fact]
        public void RingConventions_ClosedOpenOrientationDuplicatesBowtieSlitAndOverlaps()
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
                new Vector2d(20, 5), new Vector2d(10, 5),
                new Vector2d(10, 10), new Vector2d(0, 10)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(withSpike, out var pSpike));
            AssertStrictlyInside(withSpike, pSpike);
            Assert.InRange(pSpike.x, 0.0, 10.0);

            // Square [0,10]^2 with an internal retraced diagonal slit through (5,5):
            // quarter-point probing should give a well-centered point (distance from diagonal > 1.0, not 1 ULP).
            var withSlit = new[] {
                new Vector2d(0, 0), new Vector2d(10, 10), new Vector2d(0, 0),
                new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(withSlit, out var pSlit));
            AssertStrictlyInside(withSlit, pSlit);
            double distToDiag = Math.Abs(pSlit.x - pSlit.y) / Math.Sqrt(2.0);
            Assert.True(distToDiag > 1.0, $"Expected high clearance from diagonal slit, got {distToDiag}");

            // Partially overlapping collinear edges (split-edge overlap not cancelled by CancelDuplicateEdgesMod2)
            var partialOverlap = new[] {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10),
                new Vector2d(0, 10), new Vector2d(0, 6), new Vector2d(0, 3), new Vector2d(0, 8), new Vector2d(0, 0)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(partialOverlap, out var pOverlap));
            AssertStrictlyInside(partialOverlap, pOverlap);

            // Odd-multiplicity duplicate edge (3 copies of bottom edge)
            var oddDup = new[] {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(0, 0), new Vector2d(10, 0),
                new Vector2d(10, 10), new Vector2d(0, 10)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(oddDup, out var pOdd));
            AssertStrictlyInside(oddDup, pOdd);
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
        public void ExtremeScales_MixedMagnitudes_Subnormals_AndNegativeZero_AreExact()
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

            // Mixed magnitudes in one ring (1e-160 combined with 1e50)
            var mixed = new[] {
                new Vector2d(0, 0), new Vector2d(1e50, 0), new Vector2d(1e-160, 1e-160)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(mixed, out var pMix));
            AssertStrictlyInside(mixed, pMix);

            // Subnormal triangle and 4-vertex quad
            double sub = 1e-315;
            var subTri = new[] { new Vector2d(0, 0), new Vector2d(10 * sub, 0), new Vector2d(0, 10 * sub) };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(subTri, out var pSubTri));
            AssertStrictlyInside(subTri, pSubTri);

            var subQuad = new[] {
                new Vector2d(0, 0), new Vector2d(4 * double.Epsilon, 0),
                new Vector2d(4 * double.Epsilon, 4 * double.Epsilon), new Vector2d(0, 4 * double.Epsilon)
            };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(subQuad, out var pSubQuad));
            AssertStrictlyInside(subQuad, pSubQuad);

            // CoordinateLimit boundary (±1e70 accepted)
            double lim = DelaunayMesh2.CoordinateLimit;
            var atLimit = new[] { new Vector2d(-lim, -lim), new Vector2d(lim, -lim), new Vector2d(0, lim) };
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(atLimit, out var pLim));
            AssertStrictlyInside(atLimit, pLim);

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

            var pts = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0, 1), new Vector2d(double.NaN, double.PositiveInfinity) };
            Assert.Throws<ArgumentOutOfRangeException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(pts, new[] { 0, 1, 4 }, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(pts, new[] { -1, 1, 2 }, out _));
            // Indexed overload ignores unreferenced invalid vertices and rejects referenced ones
            Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(pts, new[] { 0, 1, 2 }, out var pValid));
            AssertStrictlyInside(new[] { pts[0], pts[1], pts[2] }, pValid);
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(pts, new[] { 0, 1, 3 }, out _));

            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(double.NaN, 0), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(0, double.NaN), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(0, double.PositiveInfinity), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(double.NegativeInfinity, 0), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            double overLimit = RingInterior2.NextUp(DelaunayMesh2.CoordinateLimit);
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(overLimit, 0), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
            Assert.Throws<ArgumentException>(() => ConstrainedDelaunayMesh2.TryGetInteriorPoint(new[] { new Vector2d(0, -overLimit), new Vector2d(1, 0), new Vector2d(0, 1) }, out _));
        }

        [Fact]
        public void Similarity_All2D_IsStrictInterior_AndPow2ScalesAreBitIdenticalAcrossAllPhases()
        {
            double ulp = Math.Pow(2, -52);
            var rings = new Vector2d[][] {
                // Phase 1 (L-shape)
                new[] { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 3), new Vector2d(4, 3), new Vector2d(4, 8), new Vector2d(0, 8) },
                // Triangle fast path (obtuse triangle)
                new[] { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(15, 0.5) },
                // Phase 2a exact fallback (4-vertex 3-ULP right triangle)
                new[] { new Vector2d(1.0, 1.0), new Vector2d(1.0 + ulp, 1.0), new Vector2d(1.0 + 3.0 * ulp, 1.0), new Vector2d(1.0, 1.0 + 3.0 * ulp) }
            };

            for (int r = 0; r < rings.Length; ++r) {
                var baseRing = rings[r];
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(baseRing, out var basePt));
                AssertStrictlyInside(baseRing, basePt);

                foreach (var sim in Similarity.All2D()) {
                    var transformed = baseRing.Select(p => sim.Apply2d(p)).ToArray();
                    // At 3-ULP resolution, non-exact transforms (rotation/translation) can collapse the 3-ULP triangle to <= 2 ULPs
                    if (r == 2 && !sim.IsExact) continue;
                    Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(transformed, out var pt),
                        $"Ring {r} failed for similarity {sim.Name}");
                    AssertStrictlyInside(transformed, pt);

                    if (sim.IsExact) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(basePt.x * sim.Scale), BitConverter.DoubleToInt64Bits(pt.x));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(basePt.y * sim.Scale), BitConverter.DoubleToInt64Bits(pt.y));
                    }
                }

                // Extreme power-of-two scales (2^-500 and 2^200) must also be bit-identical
                foreach (int exp in new[] { -500, 200 }) {
                    var scaled = baseRing.Select(p => new Vector2d(ExactPoint3.ScaleB(p.x, exp), ExactPoint3.ScaleB(p.y, exp))).ToArray();
                    Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(scaled, out var pt));
                    AssertStrictlyInside(scaled, pt);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(ExactPoint3.ScaleB(basePt.x, exp)), BitConverter.DoubleToInt64Bits(pt.x));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(ExactPoint3.ScaleB(basePt.y, exp)), BitConverter.DoubleToInt64Bits(pt.y));
                }
            }
        }

        [Fact]
        public void AdversarialCombRings_ScaleSubquadratically_WithinTimeBudget()
        {
            // 1. Comb ring with interior (many teeth crossing every horizontal scanline): n = 500 and n = 2000
            foreach (int teeth in new[] { 250, 1000 }) {
                var comb = new List<Vector2d>(2 * teeth + 2);
                comb.Add(new Vector2d(0, 0));
                comb.Add(new Vector2d(2 * teeth + 1, 0));
                for (int i = teeth - 1; i >= 0; --i) {
                    comb.Add(new Vector2d(2 * i + 2, 10.0));
                    comb.Add(new Vector2d(2 * i + 1, 1.0));
                }
                var sw = Stopwatch.StartNew();
                Assert.True(ConstrainedDelaunayMesh2.TryGetInteriorPoint(comb, out var pt));
                sw.Stop();
                AssertStrictlyInside(comb, pt);
                Assert.True(sw.ElapsedMilliseconds < 2000, $"Comb (teeth={teeth}) took {sw.ElapsedMilliseconds} ms");
            }

            // 2. Genuinely zero-interior comb of 1-ULP-wide right-triangle teeth (no binary64 point strictly inside any tooth)
            double ulp = Math.Pow(2, -52);
            foreach (int teeth in new[] { 200, 800 }) {
                var zeroComb = new List<Vector2d>(3 * teeth + 2);
                double baseX = 1.0;
                zeroComb.Add(new Vector2d(baseX, 0.0));
                zeroComb.Add(new Vector2d(baseX + (2 * teeth) * ulp, 0.0));
                for (int i = teeth - 1; i >= 0; --i) {
                    // Each tooth spans x in [baseX + 2*i*ulp, baseX + (2*i + 1)*ulp] (width = 1 ULP: zero interior doubles!)
                    zeroComb.Add(new Vector2d(baseX + (2 * i + 1) * ulp, 0.0));
                    zeroComb.Add(new Vector2d(baseX + (2 * i + 1) * ulp, 10.0));
                    zeroComb.Add(new Vector2d(baseX + (2 * i) * ulp, 0.0));
                }
                var sw = Stopwatch.StartNew();
                Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(zeroComb, out _));
                sw.Stop();
                Assert.True(sw.ElapsedMilliseconds < 2000, $"Zero-interior comb (teeth={teeth}) took {sw.ElapsedMilliseconds} ms");
            }

            // 3. Sparse 1-ULP-wide vertical-walled staircase (1000 horizontal slabs, active.Count == 2 per slab,
            // every slab is [1 + k*ulp, 1 + (k+1)*ulp] x [10*k, 10*(k+1)] with provably 0 binary64 interior points):
            // must stay O(n log n) via O(1) ExactRat.IsStrictlyBetween without calling O(n) Consider on every slab.
            {
                const int steps = 1000;
                var stair = new List<Vector2d>(4 * steps);
                for (int k = 0; k < steps; ++k) {
                    stair.Add(new Vector2d(1.0 + k * ulp, 10.0 * k));
                    stair.Add(new Vector2d(1.0 + k * ulp, 10.0 * (k + 1)));
                }
                stair.Add(new Vector2d(1.0 + steps * ulp, 10.0 * steps));
                for (int k = steps - 1; k >= 0; --k) {
                    stair.Add(new Vector2d(1.0 + (k + 1) * ulp, 10.0 * k));
                    if (k > 0) stair.Add(new Vector2d(1.0 + k * ulp, 10.0 * k));
                }
                var sw = Stopwatch.StartNew();
                Assert.False(ConstrainedDelaunayMesh2.TryGetInteriorPoint(stair, out _));
                sw.Stop();
                Assert.True(sw.ElapsedMilliseconds < 2000, $"1-ULP staircase (n={stair.Count}) took {sw.ElapsedMilliseconds} ms");
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
