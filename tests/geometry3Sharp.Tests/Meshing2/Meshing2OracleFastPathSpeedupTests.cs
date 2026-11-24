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
    /// Regression and equivalence tests for #1319 fast-path Oracle optimizations:
    /// laminar multi-ring shoelace area, RingGrid, RingCellCache, X-sorted BucketSlice,
    /// flat 2D BVHs in StrtreePointSearch/StrtreeEdgeSearch, fast dyadic Q/Pt unpacking,
    /// and double filters in Coverage, Segments, and Quality.
    /// </summary>
    public class Meshing2OracleFastPathSpeedupTests
    {
        static Mesh2Input Parcels(int side)
        {
            int stride = side + 1;
            var xs = new double[stride * stride];
            var ys = new double[stride * stride];
            var rnd = new Random(19);
            for (int j = 0; j < stride; ++j)
                for (int i = 0; i < stride; ++i) {
                    int id = j * stride + i;
                    double jx = (i > 0 && i < side) ? (rnd.NextDouble() - 0.5) * 0.25 : 0;
                    double jy = (j > 0 && j < side) ? (rnd.NextDouble() - 0.5) * 0.25 : 0;
                    xs[id] = (i + jx) / side;
                    ys[id] = (j + jy) / side;
                }
            var input = new Mesh2Input { X = xs, Y = ys };
            for (int j = 0; j < side; ++j)
                for (int i = 0; i < side; ++i) {
                    int a = j * stride + i, b = a + 1, c = a + stride + 1, d = a + stride;
                    input.Rings.Add(new RingInput(new[] { a, b, c, d }, false));
                }
            return input;
        }

        static Mesh2Input StarCoast(int n, double minAngleDeg = 0)
        {
            var rnd = new Random(11);
            var input = new Mesh2Input {
                X = new double[n],
                Y = new double[n],
                MinAngleDeg = minAngleDeg > 0 ? (double?)minAngleDeg : null
            };
            for (int i = 0; i < n; ++i) {
                double r = 1.0 + 0.35 * rnd.NextDouble();
                double a = 2.0 * Math.PI * i / n;
                input.X[i] = r * Math.Cos(a);
                input.Y[i] = r * Math.Sin(a);
            }
            input.Rings.Add(new RingInput(Enumerable.Range(0, n).ToArray(), false));
            return input;
        }

        [Fact]
        public void Domain_Parcels5x5_FastMatchesReferenceAreaPerimeterAndClassification()
        {
            // 25 touching outer rings (> 16 threshold): exercises RingGrid, BoxPairs Nested, and laminar shoelace.
            var input = Parcels(5);
            var fast = new Domain(input, filter: true);
            var reference = new Domain(input, filter: false);

            Assert.Equal(reference.Area, fast.Area);
            Assert.Equal(reference.Perimeter2Bound, fast.Perimeter2Bound);

            // Check interior, shared-border, vertex, and exterior probes.
            for (int gx = -1; gx <= 11; ++gx)
                for (int gy = -1; gy <= 11; ++gy) {
                    var p = Pt.FromDoubles(gx / 10.0, gy / 10.0);
                    Assert.Equal(reference.Classify(p), fast.Classify(p));
                }
            for (int i = 0; i < input.Count; ++i) {
                var p = Pt.FromDoubles(input.X[i], input.Y[i]);
                Assert.Equal(reference.Classify(p), fast.Classify(p));
            }
        }

        [Fact]
        public void Domain_NestedTouchingOverlappingEqualAndBowtie_FastMatchesReferenceAreaAndClassification()
        {
            var cases = new List<(string name, Mesh2Input input)>();

            // 1. Outer -> hole -> island -> inner hole + redundant outer inside outer + hole inside hole.
            {
                var inp = new Mesh2Input {
                    X = new double[] {
                        0, 10, 10, 0,       // 0..3 outer
                        1, 9, 9, 1,         // 4..7 hole
                        2, 8, 8, 2,         // 8..11 island (outer)
                        3, 7, 7, 3,         // 12..15 inner hole
                        4, 6, 6, 4,         // 16..19 hole inside inner hole (no effect)
                        2.5, 3.5, 3.5, 2.5  // 20..23 redundant outer? intersects inner hole -> keep inside island instead:
                    },
                    Y = new double[] {
                        0, 0, 10, 10,
                        1, 1, 9, 9,
                        2, 2, 8, 8,
                        3, 3, 7, 7,
                        4, 4, 6, 6,
                        2.2, 2.2, 2.8, 2.8
                    }
                };
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                inp.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
                inp.Rings.Add(new RingInput(new[] { 8, 9, 10, 11 }, false));
                inp.Rings.Add(new RingInput(new[] { 12, 13, 14, 15 }, true));
                inp.Rings.Add(new RingInput(new[] { 16, 17, 18, 19 }, true));
                inp.Rings.Add(new RingInput(new[] { 20, 21, 22, 23 }, false));
                cases.Add(("deep-nested", inp));
            }

            // 2. Two holes touching along a shared edge inside an outer ring.
            {
                var inp = new Mesh2Input {
                    X = new double[] { 0, 10, 10, 0, 2, 5, 5, 2, 8, 8 },
                    Y = new double[] { 0, 0, 10, 10, 2, 2, 8, 8, 2, 8 }
                };
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                inp.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
                inp.Rings.Add(new RingInput(new[] { 5, 8, 9, 6 }, true));
                cases.Add(("touching-holes", inp));
            }

            // 3. Equal duplicate rings (outer then hole -> must fall back to slabs and yield area 0).
            {
                var inp = new Mesh2Input {
                    X = new double[] { 0, 4, 4, 0 },
                    Y = new double[] { 0, 0, 4, 4 }
                };
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, true));
                cases.Add(("equal-rings", inp));
            }

            // 4. Partially overlapping outer and hole (must fall back to slabs).
            {
                var inp = new Mesh2Input {
                    X = new double[] { 0, 6, 6, 0, 3, 9, 9, 3 },
                    Y = new double[] { 0, 0, 6, 6, 2, 2, 8, 8 }
                };
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                inp.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
                cases.Add(("partial-overlap", inp));
            }

            // 5. Ring vertex on another ring's open edge (T-touching squares; must fall back to slabs cleanly).
            {
                var inp = new Mesh2Input {
                    X = new double[] { 0, 4, 4, 0, 4, 8, 8, 4 },
                    Y = new double[] { 0, 0, 4, 4, 1, 1, 3, 3 }
                };
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                inp.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, false));
                cases.Add(("t-touch-open-edge", inp));
            }

            // 6. Self-intersecting bowtie ring (must fall back to slabs).
            {
                var inp = new Mesh2Input {
                    X = new double[] { 0, 4, 0, 4 },
                    Y = new double[] { 0, 4, 4, 0 }
                };
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                cases.Add(("bowtie", inp));
            }

            foreach (var (name, inp) in cases) {
                var fast = new Domain(inp, filter: true);
                var reference = new Domain(inp, filter: false);
                Assert.True(reference.Area == fast.Area, name + ": area fast=" + fast.Area + " ref=" + reference.Area);
                Assert.True(reference.Perimeter2Bound == fast.Perimeter2Bound, name + ": perimeter mismatch");
                for (int gx = -1; gx <= 11; ++gx)
                    for (int gy = -1; gy <= 11; ++gy) {
                        var p = Pt.FromDoubles(gx, gy);
                        Assert.True(reference.Classify(p) == fast.Classify(p), name + " at (" + gx + "," + gy + ")");
                    }
            }
        }

        [Fact]
        public void Domain_LargeStarCoastRing_RingCellCacheAndBucketSliceMatchReference()
        {
            // 160 vertices (> 64 threshold): exercises RingCellCache and BucketSlice (Spanning + Ending).
            var input = StarCoast(160);
            var fast = new Domain(input, filter: true);
            var reference = new Domain(input, filter: false);

            Assert.Equal(reference.Area, fast.Area);
            Assert.Equal(reference.Perimeter2Bound, fast.Perimeter2Bound);

            // Grid probes spanning interior disk, jagged annulus, and exterior.
            for (int ix = -15; ix <= 15; ++ix)
                for (int iy = -15; iy <= 15; ++iy) {
                    var p = Pt.FromDoubles(ix * 0.1, iy * 0.1);
                    Assert.Equal(reference.Classify(p), fast.Classify(p));
                }
            // All ring vertices and edge midpoints.
            var ring = fast.Rings[0].ring;
            for (int i = 0; i < ring.Length; ++i) {
                Assert.Equal(Where.Boundary, fast.Classify(ring[i]));
                Pt a = ring[i], b = ring[(i + 1) % ring.Length];
                var mid = new Pt((a.Qx + b.Qx) * new Q(1, 2), (a.Qy + b.Qy) * new Q(1, 2));
                Assert.Equal(reference.Classify(mid), fast.Classify(mid));
            }
        }

        [Fact]
        public void Oracle_FastAndCheckModeMatchReference_OnParcelsAndCoastQuality()
        {
            // 1. 4x4 touching parcels with quality refinement.
            var parcels = Parcels(4);
            parcels.MinAngleDeg = 20;
            var parcelsOut = new QualityMesh2Candidate().Run(parcels);
            Assert.Equal(Mesh2Status.Success, parcelsOut.Status);

            // 2. 72-vertex jagged coast with quality refinement (triggers BelowAngle, SpansSmallInputAngle, MPW floor, RingCellCache, BucketSlice).
            var coast = StarCoast(72, 20);
            var coastOut = new QualityMesh2Candidate().Run(coast);
            Assert.Equal(Mesh2Status.Success, coastOut.Status);

            foreach (var (name, inp, outp) in new[] { ("parcels4", parcels, parcelsOut), ("coast72", coast, coastOut) }) {
                var expectFast = new OracleExpect { Milestone = Milestone.M3 };
                var expectRef = new OracleExpect { Milestone = Milestone.M3, Reference = true };

                OracleReport fast;
                bool prevCheck = Oracle.CheckFastPathForTests;
                Oracle.CheckFastPathForTests = true;
                try {
                    fast = Oracle.Check(inp, outp, expectFast);
                } finally {
                    Oracle.CheckFastPathForTests = prevCheck;
                }
                var reference = Oracle.Check(inp, outp, expectRef);

                Assert.True(fast.Ok, name + " fast: " + fast);
                Assert.True(reference.Ok, name + " ref: " + reference);
                Assert.Equal(reference.Area2, fast.Area2);
                Assert.Equal(reference.BelowAngle, fast.BelowAngle);
                Assert.Equal(reference.BelowMpwFloorExcused, fast.BelowMpwFloorExcused);
                Assert.Equal(reference.MinSin2, fast.MinSin2);
            }
        }

        [Fact]
        public void Oracle_FlatBvhsAndClassifyLazy_RejectCorruptedMeshesSameAsReference()
        {
            // Base valid mesh of a square.
            var input = new Mesh2Input {
                X = new double[] { 0, 2, 2, 0 },
                Y = new double[] { 0, 0, 2, 2 }
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));

            // 1. Crossing edges (two overlapping diagonals 0-2 and 1-3).
            var crossingOut = new Mesh2Output {
                Status = Mesh2Status.Success,
                IdentityRules = false,
                VX = new double[] { 0, 2, 2, 0 },
                VY = new double[] { 0, 0, 2, 2 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 1, 3 }, new[] { 1, 2, 3 } }
            };
            var fastCross = Oracle.Check(input, crossingOut, new OracleExpect { Milestone = Milestone.M2 });
            var refCross = Oracle.Check(input, crossingOut, new OracleExpect { Milestone = Milestone.M2, Reference = true });
            Assert.True(fastCross.Has(Violation.EdgesCross), "fast should flag EdgesCross: " + fastCross);
            Assert.True(refCross.Has(Violation.EdgesCross), "ref should flag EdgesCross: " + refCross);

            // 2. Vertex strictly inside a triangle.
            var touchOut = new Mesh2Output {
                Status = Mesh2Status.Success,
                IdentityRules = false,
                VX = new double[] { 0, 2, 2, 0, 1, 1.5 },
                VY = new double[] { 0, 0, 2, 2, 0.5, 0.5 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 5, 4 } }
            };
            var fastTouch = Oracle.Check(input, touchOut, new OracleExpect { Milestone = Milestone.M2 });
            var refTouch = Oracle.Check(input, touchOut, new OracleExpect { Milestone = Milestone.M2, Reference = true });
            Assert.True(fastTouch.Has(Violation.VertexInTriangle), "fast should flag VertexInTriangle: " + fastTouch);
            Assert.True(refTouch.Has(Violation.VertexInTriangle), "ref should flag VertexInTriangle: " + refTouch);

            // 3. Triangle outside domain.
            var outsideOut = new Mesh2Output {
                Status = Mesh2Status.Success,
                IdentityRules = false,
                VX = new double[] { 0, 2, 2, 0, 3 },
                VY = new double[] { 0, 0, 2, 2, 1 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 1, 4, 2 } }
            };
            var fastOut = Oracle.Check(input, outsideOut, new OracleExpect { Milestone = Milestone.M2 });
            var refOut = Oracle.Check(input, outsideOut, new OracleExpect { Milestone = Milestone.M2, Reference = true });
            Assert.True(fastOut.Has(Violation.TriangleOutsideDomain), "fast should flag TriangleOutsideDomain: " + fastOut);
            Assert.True(refOut.Has(Violation.TriangleOutsideDomain), "ref should flag TriangleOutsideDomain: " + refOut);
        }

        [Fact]
        public void Exact_FromDoubleAndPtFromDoubles_ExactAndReducedAcrossExponents()
        {
            double[] samples = {
                0.0, -0.0, 1.0, -1.0, 0.5, -0.5, 1.5, 3.25, 1e-12, -1e-12,
                double.Epsilon, -double.Epsilon,
                BitConverter.Int64BitsToDouble(0x0010000000000000L), // smallest normal 2^-1022
                BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFFL), // largest subnormal
                1e-250, -1e-250, 1e100, -1e100, 1e250, -1e250,
                Math.PI, Math.E
            };

            static Q ReferenceFromDouble(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                bool neg = bits < 0;
                int exp = (int)((bits >> 52) & 0x7ff);
                long mant = bits & 0xfffffffffffffL;
                if (exp == 0) exp = 1; else mant |= 1L << 52;
                exp -= 1075;
                BigInteger num = neg ? -mant : mant;
                return exp >= 0 ? new Q(num << exp, 1) : new Q(num, BigInteger.One << -exp);
            }

            foreach (double x in samples) {
                Q qx = Q.FromDouble(x);
                Q rx = ReferenceFromDouble(x);
                Assert.Equal(rx, qx);
                Assert.True(qx.D > 0);
                Assert.Equal(BigInteger.One, BigInteger.GreatestCommonDivisor(BigInteger.Abs(qx.N), qx.D));
            }
            for (int i = 0; i < samples.Length; ++i) {
                double x = samples[i];
                double y = samples[(i * 7 + 3) % samples.Length];
                Pt p = Pt.FromDoubles(x, y);
                Pt refP = new Pt(ReferenceFromDouble(x), ReferenceFromDouble(y));
                Assert.Equal(refP.X, p.X);
                Assert.Equal(refP.Y, p.Y);
                Assert.Equal(refP.W, p.W);
                Assert.Equal(Q.FromDouble(x), p.Qx);
                Assert.Equal(Q.FromDouble(y), p.Qy);
            }
        }
    }
}
