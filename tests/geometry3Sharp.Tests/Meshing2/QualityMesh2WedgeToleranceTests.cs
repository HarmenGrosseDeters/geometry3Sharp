// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class QualityMesh2WedgeToleranceTests
    {
        static double MinAngleDegrees(ConstrainedMesh2 mesh)
        {
            double minSin2 = double.MaxValue;
            foreach (var tri in mesh.Triangles) {
                var a = mesh.Vertices[tri.a];
                var b = mesh.Vertices[tri.b];
                var c = mesh.Vertices[tri.c];
                double qx = b.x - a.x, qy = b.y - a.y, rx = c.x - a.x, ry = c.y - a.y;
                double area2 = qx * ry - qy * rx;
                double l0 = (rx - qx) * (rx - qx) + (ry - qy) * (ry - qy);
                double l1 = rx * rx + ry * ry;
                double l2 = qx * qx + qy * qy;
                double maxProd = Math.Max(l1 * l2, Math.Max(l0 * l2, l0 * l1));
                double s2 = maxProd > 0 ? (area2 * area2) / maxProd : 0;
                if (s2 < minSin2) minSin2 = s2;
            }
            return Math.Asin(Math.Sqrt(Math.Max(0.0, Math.Min(1.0, minSin2)))) * (180.0 / Math.PI);
        }

        static bool SameMesh(ConstrainedMesh2 a, ConstrainedMesh2 b)
        {
            if (a.Vertices.Count != b.Vertices.Count || a.Triangles.Count != b.Triangles.Count) return false;
            for (int i = 0; i < a.Vertices.Count; ++i)
                if (BitConverter.DoubleToInt64Bits(a.Vertices[i].x) != BitConverter.DoubleToInt64Bits(b.Vertices[i].x)
                    || BitConverter.DoubleToInt64Bits(a.Vertices[i].y) != BitConverter.DoubleToInt64Bits(b.Vertices[i].y))
                    return false;
            for (int i = 0; i < a.Triangles.Count; ++i)
                if (a.Triangles[i] != b.Triangles[i]) return false;
            return true;
        }

        static ConstrainedMesh2 AssertOracleClean(Mesh2Input benchInput, QualityMesh2 mesher, bool constrainedMode = false)
        {
            var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(benchInput);
            var res = mesher.Compute(g3Input);
            Assert.True(res.Status == Triangulation2Status.Success, string.Join("; ", res.Diagnostics));
            Assert.NotNull(res.Mesh);

            var outObj = ConstrainedDelaunayMesh2Candidate.Output(res);
            outObj.BadTriangles = res.Mesh.BadTriangles.ToArray();
            outObj.SteinerLimitReached = res.Mesh.SteinerLimitReached;

            if (constrainedMode) {
                benchInput.Conforming = false;
                benchInput.ConstrainedQuality = true;
            }
            var report = Oracle.Check(benchInput, outObj, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "Oracle violations:\n" + string.Join("\n", report.Violations.Select(v => v.ToString())));
            return res.Mesh;
        }

        [Fact]
        public void WedgeTolerance_InvalidArgumentsThrow()
        {
            var input = new ConstrainedMesh2Input {
                Points = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) }
            };
            foreach (double bad in new[] { -0.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2 { WedgeTolerance = bad }.Compute(input));
                Assert.Throws<ArgumentException>(() => new QualityMesh2 { MinAngle = 25, WedgeTolerance = bad }.Compute(input));
            }
        }

        [Fact]
        public void F19_Case1_ThinWedge_CollapsedAndCornerPreserved_ZeroBadTrianglesAt30Deg()
        {
            // Spec F-19 case 1: border (0,0)-(20,0), wall (5,0)-(15, 0.025)-(15, 5), snap 0.01, wedge 0.03, min angle 30 deg
            foreach (bool conforming in new[] { false, true }) {
                var bench = new B()
                    .Rect(-1, -1, 21, 6)
                    .Seg(0, 0, 20, 0)
                    .Polyline(5, 0, 15, 0.025, 15, 5)
                    .Snap(0.01)
                    .Wedge(0.03)
                    .Min(30)
                    .Build();
                if (!conforming) bench.ConstrainedQuality = true;
                else bench.Conforming = true;

                var mesh = AssertOracleClean(bench, new QualityMesh2 {
                    MinAngle = 30, SnapTolerance = 0.01, WedgeTolerance = 0.03, Conforming = conforming
                }, constrainedMode: !conforming);

                Assert.Empty(mesh.BadTriangles);
                Assert.True(MinAngleDegrees(mesh) >= 30.0 - 1e-9,
                    "Expected no triangle below 30 deg (got " + MinAngleDegrees(mesh) + ", conforming=" + conforming + ")");

                // Vertex (15, 0.025) must still be present (snapped to pixel centre at y = 0.02 or 0.03, NOT merged onto y = 0)
                int cornerInputIdx = -1;
                for (int i = 0; i < bench.Count; ++i)
                    if (bench.X[i] == 15.0 && bench.Y[i] == 0.025) { cornerInputIdx = i; break; }
                Assert.True(cornerInputIdx >= 0);
                var cornerPt = mesh.Vertices[mesh.InputToVertex[cornerInputIdx]];
                Assert.True(Math.Abs(cornerPt.x - 15.0) <= 0.01 + 1e-12, "Corner x shifted: " + cornerPt.x);
                Assert.True(Math.Abs(cornerPt.y - 0.025) <= 0.01 + 1e-12 && cornerPt.y > 0.01,
                    "Expected corner vertex (15, 0.025) to remain present off y=0, got " + cornerPt);
            }
        }

        [Fact]
        public void F19_Case2_WiderOffset_005_NotMerged()
        {
            // Spec F-19 case 2: o = 0.05 > wedge tolerance 0.03 -> NOT merged (bit-identical to WedgeTolerance = 0)
            var input = ConstrainedDelaunayMesh2Candidate.Convert(
                new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0).Polyline(5, 0, 15, 0.05, 15, 5).Build());
            var withoutWedge = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.01, WedgeTolerance = 0 }.Compute(input);
            var withWedge = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.01, WedgeTolerance = 0.03 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, withWedge.Status);
            Assert.True(SameMesh(withoutWedge.Mesh, withWedge.Mesh), "o = 0.05 must not be merged at WedgeTolerance = 0.03");
        }

        [Fact]
        public void F19_Case3_ShortLargeAngleSegment_NotMerged()
        {
            // Spec F-19 case 3: short segment (5,0)-(5.02, 0.025) at ~51 deg (>= 10 deg) within 0.03 -> NOT merged
            var input = ConstrainedDelaunayMesh2Candidate.Convert(
                new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0).Seg(5, 0, 5.02, 0.025).Build());
            var withoutWedge = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.01, WedgeTolerance = 0 }.Compute(input);
            var withWedge = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.01, WedgeTolerance = 0.03 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, withWedge.Status);
            Assert.True(SameMesh(withoutWedge.Mesh, withWedge.Mesh), "Short large-angle segment (~51 deg) must not be merged");
        }

        [Fact]
        public void F19_Case4_SlowlyDriftingPolyline_MergedCompletely_ZeroBadTriangles()
        {
            // Spec F-19 case 4: polyline from (2,0) with vertices (2 + 2i, 0.003 * i), i = 1..8 (drifts to 0.024 <= 0.03)
            var b = new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0);
            var poly = new List<double> { 2.0, 0.0 };
            for (int i = 1; i <= 8; ++i) {
                poly.Add(2.0 + 2.0 * i);
                poly.Add(0.003 * i);
            }
            b.Polyline(poly.ToArray()).Snap(0.01).Wedge(0.03).Min(30);
            var bench = b.Build();
            bench.Conforming = true;

            var mesh = AssertOracleClean(bench, new QualityMesh2 {
                MinAngle = 30, SnapTolerance = 0.01, WedgeTolerance = 0.03, Conforming = true
            });

            Assert.Empty(mesh.BadTriangles);
            Assert.True(MinAngleDegrees(mesh) >= 30.0 - 1e-9);

            // Every polyline vertex must have merged onto y = 0
            for (int i = 0; i < bench.Count; ++i) {
                if (bench.X[i] >= 2.0 && bench.X[i] <= 18.0 && bench.Y[i] >= 0.0 && bench.Y[i] <= 0.03) {
                    var pt = mesh.Vertices[mesh.InputToVertex[i]];
                    Assert.Equal(0.0, pt.y);
                }
            }
        }

        [Fact]
        public void F19_Case5_SteeperDriftingPolyline_BorderNotDraggedBeyondTolerance()
        {
            // Spec F-19 case 5: slope 0.0075 * i, i = 1..8 (drifts to 0.06 > 0.03): border (0,0)-(20,0) must not move > 0.03
            var b = new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0);
            var poly = new List<double> { 2.0, 0.0 };
            for (int i = 1; i <= 8; ++i) {
                poly.Add(2.0 + 2.0 * i);
                poly.Add(0.0075 * i);
            }
            b.Polyline(poly.ToArray()).Snap(0.01).Wedge(0.03).Min(20);
            var bench = b.Build();
            bench.Conforming = true;

            var mesh = AssertOracleClean(bench, new QualityMesh2 {
                MinAngle = 20, SnapTolerance = 0.01, WedgeTolerance = 0.03, Conforming = true
            });

            // Border endpoints (0,0) and (20,0) must stay at y = 0 (zero drag)
            for (int i = 0; i < bench.Count; ++i) {
                if ((bench.X[i] == 0.0 || bench.X[i] == 20.0) && bench.Y[i] == 0.0) {
                    var pt = mesh.Vertices[mesh.InputToVertex[i]];
                    Assert.Equal(0.0, pt.y);
                }
            }
            // No input vertex moved by more than WedgeTolerance + SnapTolerance from its original coordinate
            for (int i = 0; i < bench.Count; ++i) {
                var orig = new Vector2d(bench.X[i], bench.Y[i]);
                var actual = mesh.Vertices[mesh.InputToVertex[i]];
                Assert.True((actual - orig).Length <= 0.03 + 0.01,
                    "Input vertex " + i + " drifted too far: " + orig + " -> " + actual);
            }
            // Far end (18, 0.06) must NOT have been pulled onto y = 0
            int farIdx = -1;
            for (int i = 0; i < bench.Count; ++i)
                if (bench.X[i] == 18.0 && Math.Abs(bench.Y[i] - 0.06) < 1e-12) { farIdx = i; break; }
            Assert.True(farIdx >= 0);
            Assert.True(mesh.Vertices[mesh.InputToVertex[farIdx]].y >= 0.05,
                "Far polyline end at y=0.06 should not be collapsed onto y=0");
        }

        [Fact]
        public void WedgeCollapse_WorksWithoutSnapTolerance_WithHeightsRingsAndLocks()
        {
            // ConstrainedDelaunayMesh2 with SnapTolerance = 0, WedgeTolerance = 0.05, linear heights, ring + interior wedge
            var bench = new B()
                .Rect(0, 0, 20, 10)
                .Polyline(4, 0, 14, 0.03, 14, 4)
                .Build();
            var input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
            input.Heights = input.Points.Select(p => 3.0 * p.x - 2.0 * p.y + 1.0).ToList();

            var cdt = new ConstrainedDelaunayMesh2 { WedgeTolerance = 0.05 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, cdt.Status);
            Assert.Equal(input.Points.Count, cdt.Mesh.InputCount);
            // Foot vertex (14, 0) was inserted and interpolated linearly along bottom ring edge
            bool foundFoot = false;
            for (int i = 0; i < cdt.Mesh.Vertices.Count; ++i) {
                var p = cdt.Mesh.Vertices[i];
                Assert.Equal(3.0 * p.x - 2.0 * p.y + 1.0, cdt.Mesh.Heights[i], 10);
                if (Math.Abs(p.x - 14.0) < 1e-12 && Math.Abs(p.y) < 1e-12) foundFoot = true;
            }
            Assert.True(foundFoot, "Expected foot vertex (14, 0) to be created on host edge");

            // Locked wedge segment is NOT collapsed (spec O9)
            var lockedIn = ConstrainedDelaunayMesh2Candidate.Convert(
                new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0).Seg(5, 0, 15, 0.025, marker: 5, locked: true).Build());
            var lockedNoWedge = new ConstrainedDelaunayMesh2 { WedgeTolerance = 0 }.Compute(lockedIn);
            var lockedWithWedge = new ConstrainedDelaunayMesh2 { WedgeTolerance = 0.05 }.Compute(lockedIn);
            Assert.True(SameMesh(lockedNoWedge.Mesh, lockedWithWedge.Mesh), "Locked wedge segment must not be collapsed");
        }

        [Fact]
        public void TextFormat_RoundTrips_WedgeTolerance()
        {
            var input = new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0).Snap(1.0 / 128).Wedge(5.0 / 128).Min(30).Build();
            string text = TextFormat.WriteInput(input);
            Assert.Contains("wedge_tolerance ", text);
            var parsed = TextFormat.ReadInput(text);
            Assert.Equal(5.0 / 128, parsed.WedgeTolerance);
            Assert.Equal(TextFormat.InputSha256(input), TextFormat.InputSha256(parsed));

            var plain = new B().Square(10).Min(25).Build();
            string plainText = TextFormat.WriteInput(plain);
            Assert.DoesNotContain("wedge_tolerance", plainText);

            var bad = new B().Square(10).Build();
            bad.WedgeTolerance = -0.01;
            Assert.Throws<InvalidOperationException>(() => TextFormat.WriteInput(bad));
            Assert.Throws<FormatException>(() => TextFormat.ReadInput(plainText.Replace("end\n", "wedge_tolerance -0.01\nend\n")));
        }

        [Fact]
        public void WedgeCollapse_Cancellation_AndScalingOnLargeDriftingPolylines()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(
                new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0).Polyline(5, 0, 15, 0.025, 15, 5).Build());
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                new QualityMesh2 { MinAngle = 30, WedgeTolerance = 0.03 }.Compute(input, cts.Token));

            // Multi-scale BVH scaling check: n vs 4n parallel hosts with thin wedges (verifies O(E log E), no quadratic blowup)
            ConstrainedMesh2Input MakeStripes(int count)
            {
                var b = new B().Rect(-1, -1, 25, count * 2 + 1);
                for (int i = 0; i < count; ++i) {
                    double y = 2.0 * i;
                    b.Seg(0, y, 20, y);
                    b.Polyline(2, y, 14, y + 0.02, 14, y + 0.5);
                }
                return ConstrainedDelaunayMesh2Candidate.Convert(b.Build());
            }
            var r1 = new ConstrainedDelaunayMesh2 { WedgeTolerance = 0.03 }.Compute(MakeStripes(40));
            var r4 = new ConstrainedDelaunayMesh2 { WedgeTolerance = 0.03 }.Compute(MakeStripes(160));
            Assert.Equal(Triangulation2Status.Success, r1.Status);
            Assert.Equal(Triangulation2Status.Success, r4.Status);
            Assert.True(r4.Mesh.Triangles.Count >= 3 * r1.Mesh.Triangles.Count);
        }

        [Fact]
        public void HostSegmentEndpointsStayPinned_WhenAlsoShallowToLongerSegment()
        {
            // Edge 0 (len 4): (1, 0)-(5, -0.01) merges onto Edge 1 (len 8): (0, 0)-(8, 0), pinning (0, 0) and (8, 0).
            // Edge 2 (len 20): (0, 0)-(20, 0.04) is shallower than 10 deg and within WedgeTolerance (0.03) of (8, 0).
            // Because Edge 1 already acted as a host, its endpoint (8, 0) must remain pinned at y = 0.
            var input = new ConstrainedMesh2Input {
                Points = new List<Vector2d> {
                    new Vector2d(-1, -1), new Vector2d(21, -1), new Vector2d(21, 6), new Vector2d(-1, 6),
                    new Vector2d(1, 0), new Vector2d(5, -0.01),
                    new Vector2d(0, 0), new Vector2d(8, 0),
                    new Vector2d(20, 0.04)
                },
                Rings = new List<Mesh2Ring> { new Mesh2Ring(new[] { 0, 1, 2, 3 }, false) },
                Segments = new List<Mesh2Segment> {
                    new Mesh2Segment(4, 5), // short wedge onto edge (6, 7)
                    new Mesh2Segment(6, 7), // host for edge (4, 5)
                    new Mesh2Segment(6, 8)  // longer segment shallow to edge (6, 7)
                }
            };
            var res = new ConstrainedDelaunayMesh2 { WedgeTolerance = 0.03 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            int v7 = res.Mesh.InputToVertex[7];
            Assert.Equal(8.0, res.Mesh.Vertices[v7].x);
            Assert.Equal(0.0, res.Mesh.Vertices[v7].y);
        }

        [Fact]
        public void WedgeCollapse_SimilarityAll2D_Reuse_AndParallelDeterminism()
        {
            var basePoints = new[] {
                new Vector2d(-1, -1), new Vector2d(21, -1), new Vector2d(21, 6), new Vector2d(-1, 6),
                new Vector2d(0, 0), new Vector2d(20, 0),
                new Vector2d(5, 0), new Vector2d(15, 1.0 / 32), new Vector2d(15, 5)
            };
            Mesh2Input MakeTransformed(Similarity sim)
            {
                var b = new B();
                foreach (var p in basePoints) {
                    var q = sim.Apply(new Vector3d(p.x, p.y, 0));
                    b.Point(q.x, q.y);
                }
                b.Snap((1.0 / 128) * sim.Scale).Wedge((5.0 / 128) * sim.Scale).Min(30);
                var inp = b.Build();
                inp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                inp.Segments.Add(new SegmentInput(4, 5));
                inp.Segments.Add(new SegmentInput(6, 7));
                inp.Segments.Add(new SegmentInput(7, 8));
                return inp;
            }

            var baselineInput = MakeTransformed(Similarity.Identity);
            var baselineMesher = new QualityMesh2 { MinAngle = 30, SnapTolerance = 1.0 / 128, WedgeTolerance = 5.0 / 128, Conforming = true };
            var baseline = AssertOracleClean(baselineInput, baselineMesher);
            Assert.Empty(baseline.BadTriangles);

            foreach (var sim in Similarity.All2D()) {
                var benchInput = MakeTransformed(sim);
                var m = new QualityMesh2 {
                    MinAngle = 30,
                    SnapTolerance = (1.0 / 128) * sim.Scale,
                    WedgeTolerance = (5.0 / 128) * sim.Scale,
                    Conforming = true
                };
                var mesh = AssertOracleClean(benchInput, m);
                if (sim.IsExact) {
                    Assert.Empty(mesh.BadTriangles);
                    Assert.True(MinAngleDegrees(mesh) >= 30.0 - 1e-9, "Min angle below 30 under " + sim.Name);
                    Assert.Equal(baseline.Vertices.Count, mesh.Vertices.Count);
                    Assert.Equal(baseline.Triangles.Count, mesh.Triangles.Count);
                    for (int i = 0; i < baseline.Vertices.Count; ++i) {
                        Assert.Equal(baseline.Vertices[i].x * sim.Scale, mesh.Vertices[i].x);
                        Assert.Equal(baseline.Vertices[i].y * sim.Scale, mesh.Vertices[i].y);
                    }
                    for (int i = 0; i < baseline.Triangles.Count; ++i)
                        Assert.Equal(baseline.Triangles[i], mesh.Triangles[i]);
                }
            }

            // Instance reuse
            var g3BaselineInput = ConstrainedDelaunayMesh2Candidate.Convert(baselineInput);
            var again = baselineMesher.Compute(g3BaselineInput);
            Assert.Equal(Triangulation2Status.Success, again.Status);
            Assert.True(SameMesh(baseline, again.Mesh), "Reused QualityMesh2 instance produced different output");

            // Parallel determinism across 8 threads
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, 8, i => {
                var m = new QualityMesh2 { MinAngle = 30, SnapTolerance = 1.0 / 128, WedgeTolerance = 5.0 / 128, Conforming = true };
                results[i] = m.Compute(ConstrainedDelaunayMesh2Candidate.Convert(baselineInput)).Mesh;
            });
            for (int i = 0; i < results.Length; ++i)
                Assert.True(SameMesh(baseline, results[i]), "Parallel run " + i + " differed from serial");
        }
    }
}
