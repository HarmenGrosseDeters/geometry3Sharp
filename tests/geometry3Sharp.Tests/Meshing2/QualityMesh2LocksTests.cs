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
    /// Quality and area refinement near locked segments and locked rings (spec O8, O9; catalogue F-25, F-31/a, F-36; feature m3.locks).
    /// </summary>
    public class QualityMesh2LocksTests
    {
        static OracleReport Judge(Mesh2Input input, out Mesh2Output output, OracleExpect expect = null)
        {
            output = new QualityMesh2Candidate().Run(input);
            return Oracle.Check(input, output, expect ?? new OracleExpect { Milestone = Milestone.M3 });
        }

        static Mesh2Input LockedRectangle(double w, double h, double maxArea, double? minAngle = null, bool useRing = false)
        {
            var input = new Mesh2Input {
                X = new[] { 0, w, w, 0 },
                Y = new[] { 0, 0, h, h },
                MaxArea = maxArea,
                MinAngleDeg = minAngle
            };
            if (useRing) input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1, locked: true));
            else {
                for (int i = 0; i < 4; ++i) input.Segments.Add(new SegmentInput(i, (i + 1) % 4, 1, locked: true));
            }
            return input;
        }

        [Theory]
        [InlineData(10, 20, 115, false)]
        [InlineData(10, 20, 115, true)]
        [InlineData(10, 30, 172, false)]
        [InlineData(10, 30, 172, true)]
        public void F25_LockedRectangle_AreaOnly_MeetsMinTrianglesAndOracle(double w, double h, int minTriangles, bool constrained)
        {
            var input = LockedRectangle(w, h, 1.75);
            input.ConstrainedQuality = constrained;
            var expect = new OracleExpect { Milestone = Milestone.M3, StatedArea = new Q((long)(w * h), 1), MinTriangles = minTriangles };
            var report = Judge(input, out var output, expect);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.True(output.Triangles.Length >= minTriangles, output.Triangles.Length + " < " + minTriangles);
            Assert.Equal(4, output.Subsegments.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void F31_LockedOuterRing_MinAngleAndArea_PassesOracle(bool constrained)
        {
            var row = Catalogue.Rows().First(r => r.Id == "F-31/a");
            var input = TextFormat.ReadInput(TextFormat.WriteInput(row.Case.Input));
            input.ConstrainedQuality = constrained;
            var report = Judge(input, out var output, row.Case.Expect);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            // all 5 locked outer ring edges stay unsplit
            Assert.Equal(5, output.Subsegments.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void F36_CrossingLockedBreaklines_NoVertexOutside_PassesOracle(bool constrained)
        {
            var row = Catalogue.Rows().First(r => r.Id == "F-36");
            var input = TextFormat.ReadInput(TextFormat.WriteInput(row.Case.Input));
            input.ConstrainedQuality = constrained;
            var report = Judge(input, out var output, row.Case.Expect);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            for (int v = 0; v < output.VX.Length; ++v) {
                Assert.InRange(output.VX[v], -1e-9, 96 + 1e-9);
                Assert.InRange(output.VY[v], -1e-9, 96 + 1e-9);
            }
        }

        [Fact]
        public void LockedHoleRing_IsNotSplit_AndInteriorMeetsBounds()
        {
            var input = new Mesh2Input {
                X = new double[] { 0, 20, 20, 0, 7, 13, 13, 7 },
                Y = new double[] { 0, 0, 20, 20, 7, 7, 13, 13 },
                MinAngleDeg = 25,
                MaxArea = 2
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1, locked: false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true, 2, locked: true));
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 364 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            var holeSubs = output.Subsegments.Where(s => s.Marker == 2).ToList();
            Assert.Equal(4, holeSubs.Count);
        }

        [Fact]
        public void AcuteSkinnyTriangleOnLockedEdge_IsRefined()
        {
            // Triangle (0,1,4) has locked base (0,1) of length 10 and apex (5,30): acute, angle at apex ~19 deg < 20 deg,
            // circumcentre strictly inside the domain -> must be refined, not skipped.
            var input = new Mesh2Input {
                X = new double[] { 0, 10, 10, 0, 5 },
                Y = new double[] { 0, 0, 30, 30, 30 },
                MinAngleDeg = 20
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 4, 3 }, false, 1, locked: true));
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 300 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.True(output.VX.Length > 5, "expected interior Steiner points to refine the acute skinny triangle");
            Assert.Equal(5, output.Subsegments.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TrappedInteriorTriangleNearLockedEdge_TerminatesWithoutSteinerLimit(bool constrained)
        {
            // Points (50, 0.5), (51, 0.5), (50.5, 0.45) form a flat obtuse interior triangle trapped 0.45 above a locked
            // bottom edge of length 100: no point on its side of the locked edge has insertion radius >= |pq| = 1.
            // Must terminate cleanly without creeping to the Steiner limit, and pass the oracle.
            var input = new Mesh2Input {
                X = new double[] { 0, 100, 100, 0, 50, 51, 50.5 },
                Y = new double[] { 0, 0, 40, 40, 0.5, 0.5, 0.45 },
                MinAngleDeg = 20,
                SteinerLimit = 2000,
                ConstrainedQuality = constrained
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1, locked: true));
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 4000 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached, "must not creep into the locked edge");
            Assert.True(report.Ok, report.ToString());
            Assert.True(output.VX.Length < 1000, "unexpectedly many vertices: " + output.VX.Length);
        }

        [Fact]
        public void RightTriangleOnLockedHypotenuse_DoesNotSplitLockedEdge()
        {
            // Right triangle (0,0)-(10,0)-(2,4) has angle 90 deg at (2,4) since (2-0)*(2-10) + 4*4 = 0,
            // so its circumcentre (5,0) lies strictly on the locked hypotenuse (0,0)-(10,0).
            // With MinAngleDeg = 0 and MaxArea = 10 (area = 20), SplitTriangle's very first candidate is (5,0).
            var input = new Mesh2Input {
                X = new double[] { 0, 10, 2 },
                Y = new double[] { 0, 0, 4 },
                MinAngleDeg = 0,
                MaxArea = 10
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false, 1, locked: true));
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 20 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(3, output.Subsegments.Count);
        }

        [Fact]
        public void SizeFunction_LockedSquare_LongestEdgeIsLocked_StopsCleanlyWithoutSplitting()
        {
            // A 16x16 locked square splits its diagonal (length 16*sqrt(2) > 16) at (8,8), leaving 4 triangles whose longest
            // edge is their own locked edge of length 16: must stop cleanly at 4 triangles with zero locked splits.
            var input = LockedRectangle(16, 16, 0, useRing: true);
            input.MaxArea = null;
            input.SizeConstant = 2.0;
            input.SizeFunction = (x, y) => new Q(2, 1);
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 256 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(4, output.Subsegments.Count);
            Assert.Equal(4, output.Triangles.Length);
        }

        [Fact]
        public void SizeFunction_NearLockedBoundary_RefinesInteriorAndPassesOracle()
        {
            // 16x16 locked ring with 32 edges of length 2 and constant SizeFunction = 2: interior must refine to > 100
            // triangles with all 32 locked boundary edges intact and zero oracle violations.
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i < 8; ++i) { xs.Add(2 * i); ys.Add(0); }
            for (int i = 0; i < 8; ++i) { xs.Add(16); ys.Add(2 * i); }
            for (int i = 0; i < 8; ++i) { xs.Add(16 - 2 * i); ys.Add(16); }
            for (int i = 0; i < 8; ++i) { xs.Add(0); ys.Add(16 - 2 * i); }
            var input = new Mesh2Input {
                X = xs.ToArray(), Y = ys.ToArray(),
                MinAngleDeg = 20,
                SizeConstant = 2.0,
                SizeFunction = (x, y) => new Q(2, 1)
            };
            input.Rings.Add(new RingInput(Enumerable.Range(0, 32).ToArray(), false, 1, locked: true));
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 256 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(32, output.Subsegments.Count);
            Assert.True(output.Triangles.Length > 100, "expected > 100 triangles, got " + output.Triangles.Length);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LockedRing_Invariance_SimilarityAll(bool areaOnly)
        {
            var baseIn = areaOnly
                ? LockedRectangle(10, 20, 1.75, null, useRing: true)
                : LockedRectangle(10, 20, 4.0, 20, useRing: true);
            if (!areaOnly) {
                var xs = baseIn.X.ToList(); var ys = baseIn.Y.ToList();
                xs.AddRange(new[] { 3.0, 7.0, 5.0 }); ys.AddRange(new[] { 6.0, 14.0, 10.0 });
                baseIn.X = xs.ToArray(); baseIn.Y = ys.ToArray();
            }
            var baseReport = Judge(baseIn, out var baseOut);
            Assert.True(baseReport.Ok, baseReport.ToString());

            foreach (var s in Similarity.All2D()) {
                var moved = new Mesh2Input {
                    X = new double[baseIn.Count], Y = new double[baseIn.Count],
                    Rings = baseIn.Rings, Segments = baseIn.Segments,
                    MinAngleDeg = baseIn.MinAngleDeg,
                    MaxArea = baseIn.MaxArea == null ? (double?)null : baseIn.MaxArea.Value * s.Scale * s.Scale
                };
                for (int i = 0; i < baseIn.Count; ++i) {
                    var p = s.Apply(new Vector3d(baseIn.X[i], baseIn.Y[i], 0));
                    moved.X[i] = p.x; moved.Y[i] = p.y;
                }
                var r = Judge(moved, out var o);
                Assert.Equal(Mesh2Status.Success, o.Status);
                Assert.False(o.SteinerLimitReached, s.Name);
                Assert.True(r.Ok, s.Name + ": " + r);
                Assert.Equal(4, o.Subsegments.Count);
                if (s.IsExact) {
                    Assert.Equal(baseOut.VX.Select(v => v * s.Scale), o.VX);
                    Assert.Equal(baseOut.VY.Select(v => v * s.Scale), o.VY);
                    Assert.Equal(baseOut.Triangles.SelectMany(t => t), o.Triangles.SelectMany(t => t));
                }
            }
        }

        [Fact]
        public void LockedRing_Reuse_AndDeterminism_ParallelEqualsSerial()
        {
            var input = new ConstrainedMesh2Input {
                Points = { new Vector2d(0, 0), new Vector2d(16, 1), new Vector2d(19, 9), new Vector2d(13, 17), new Vector2d(2, 15), new Vector2d(8, 8), new Vector2d(12, 6) }
            };
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4 }, false, 1, locked: true));
            input.Segments.Add(new Mesh2Segment(5, 6, 2, locked: true));

            var mesher = new QualityMesh2 { MinAngle = 20, MaxArea = 3 };
            var first = mesher.Compute(input);
            var second = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, first.Status);
            Assert.Equal(first.Mesh.Vertices, second.Mesh.Vertices);
            Assert.Equal(first.Mesh.Triangles, second.Mesh.Triangles);
            Assert.Equal(first.Mesh.Subsegments.Count, second.Mesh.Subsegments.Count);

            var parallel = new ConstrainedTriangulation2Result[8];
            Parallel.For(0, parallel.Length, i => {
                parallel[i] = new QualityMesh2 { MinAngle = 20, MaxArea = 3 }.Compute(input);
            });
            for (int i = 0; i < parallel.Length; ++i) {
                Assert.Equal(Triangulation2Status.Success, parallel[i].Status);
                Assert.Equal(first.Mesh.Vertices, parallel[i].Mesh.Vertices);
                Assert.Equal(first.Mesh.Triangles, parallel[i].Mesh.Triangles);
            }
        }

        [Fact]
        public void LockedRing_WithHeights_ToDMesh3_InterpolatesPlane()
        {
            var input = new ConstrainedMesh2Input {
                Points = { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 20), new Vector2d(0, 20) },
                Heights = new List<double> { 1, 11, 51, 41 } // z = 1 + x + 2y
            };
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, false, 1, locked: true));
            var res = new QualityMesh2 { MinAngle = 0, MaxArea = 1.75 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            Assert.True(res.Mesh.Triangles.Count >= 115);
            for (int v = 0; v < res.Mesh.Vertices.Count; ++v) {
                var p = res.Mesh.Vertices[v];
                Assert.Equal(1 + p.x + 2 * p.y, res.Mesh.Heights[v], 9);
            }
            var dm = res.Mesh.ToDMesh3();
            Assert.Equal(res.Mesh.Triangles.Count, dm.TriangleCount);
            Assert.Equal(res.Mesh.Vertices.Count, dm.VertexCount);
        }

        [Fact]
        public void LockedRing_OnSnapGrid_PassesOracle()
        {
            var input = LockedRectangle(12, 16, 4.0, 20, useRing: true);
            input.SnapTolerance = 0.25;
            var report = Judge(input, out var output, new OracleExpect { Milestone = Milestone.M3, StatedArea = 192 });
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(4, output.Subsegments.Count);
        }

        [Fact]
        public void TextFormat_LockedRings_RoundTrip_AndUnlockedHashUnchanged()
        {
            var unlocked = new Mesh2Input { X = new double[] { 0, 4, 4, 0 }, Y = new double[] { 0, 0, 4, 4 } };
            unlocked.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 0));
            string unlockedText = TextFormat.WriteInput(unlocked);
            Assert.Contains("\nouter 0 4 0 1 2 3\n", unlockedText);

            var locked = new Mesh2Input { X = new double[] { 0, 10, 10, 0, 3, 7, 7, 3 }, Y = new double[] { 0, 0, 10, 10, 3, 3, 7, 7 } };
            locked.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1, locked: true));
            locked.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true, 2, locked: true));
            string text = TextFormat.WriteInput(locked);
            Assert.Contains("\nouter-locked 1 4 0 1 2 3\n", text);
            Assert.Contains("\nhole-locked 2 4 4 5 6 7\n", text);
            var parsed = TextFormat.ReadInput(text);
            Assert.Equal(2, parsed.Rings.Count);
            Assert.True(parsed.Rings[0].Locked && !parsed.Rings[0].Hole);
            Assert.True(parsed.Rings[1].Locked && parsed.Rings[1].Hole);
            Assert.Equal(TextFormat.InputSha256(locked), TextFormat.InputSha256(parsed));
        }
    }
}
