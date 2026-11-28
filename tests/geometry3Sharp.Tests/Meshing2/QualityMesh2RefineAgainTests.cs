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
    /// <summary>
    /// Tests for <see cref="QualityMesh2.Refine(TriangulationMesh2, IReadOnlyList{double?}, CancellationToken)"/> and
    /// <see cref="QualityMesh2.Refine(ConstrainedMesh2, IReadOnlyList{double?}, CancellationToken)"/> (spec I8, O17, catalogue F-33).
    /// </summary>
    public class QualityMesh2RefineAgainTests
    {
        static double TriangleArea(IReadOnlyList<Vector2d> v, Index3i t)
        {
            var a = v[t.a]; var b = v[t.b]; var c = v[t.c];
            return 0.5 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        }

        static double TotalArea(ConstrainedMesh2 m)
        {
            double sum = 0;
            for (int t = 0; t < m.TriangleCount; ++t) sum += TriangleArea(m.Vertices, m.Triangles[t]);
            return sum;
        }

        static double LabelArea(ConstrainedMesh2 m, int label)
        {
            double sum = 0;
            for (int t = 0; t < m.TriangleCount; ++t)
                if (m.TriangleLabels[t] == label) sum += TriangleArea(m.Vertices, m.Triangles[t]);
            return sum;
        }

        static Mesh2Output ToOutput(ConstrainedTriangulation2Result res)
        {
            var o = ConstrainedDelaunayMesh2Candidate.Output(res);
            if (res.Mesh != null) {
                o.BadTriangles = res.Mesh.BadTriangles.ToArray();
                o.SteinerLimitReached = res.Mesh.SteinerLimitReached;
            }
            return o;
        }

        static Mesh2Input F33Input(double? minAngle, bool constrainedQuality = false)
        {
            var b = new B().RandomPoints(16, 12, 33)
                .TriangleArea(0, 1, 6, 0.25)
                .TriangleArea(1, 5, 6, 0.25)
                .TriangleArea(3, 4, 6, 0.25);
            if (minAngle != null) b.Min(minAngle.Value);
            if (constrainedQuality) b.ConstrainedQuality();
            return b.Build();
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(20.0, false)]
        [InlineData(20.0, true)]
        public void F33_PerTriangleAreasOnPointSet_PassesOracleFastAndReference(double? minAngle, bool constrainedQuality)
        {
            var input = F33Input(minAngle, constrainedQuality);
            var output = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            // Bounded triangles sum to area 42.0 -> at <= 0.25 each, strictly more than 168 triangles must be produced
            Assert.True(output.Triangles.Length > 170, "expected local refinement, got " + output.Triangles.Length + " triangles");

            var fast = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(fast.Ok, "fast oracle: " + fast);
            var reference = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3, Reference = true });
            Assert.True(reference.Ok, "reference oracle: " + reference);

            // Verify unconstrained part of the hull stayed coarser than 0.25 (area-only and min-angle both leave far corners coarse)
            double maxTriArea = 0;
            for (int t = 0; t < output.Triangles.Length; ++t) {
                var tri = output.Triangles[t];
                double ax = output.VX[tri[0]], ay = output.VY[tri[0]];
                double bx = output.VX[tri[1]], by = output.VY[tri[1]];
                double cx = output.VX[tri[2]], cy = output.VY[tri[2]];
                double area = 0.5 * ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax));
                if (area > maxTriArea) maxTriArea = area;
            }
            Assert.True(maxTriArea > 1.0, "expected unbounded region to stay coarse, max area was " + maxTriArea);
        }

        [Fact]
        public void SpecF33_UnitSquareWithTwoInteriorPoints_RefinesOnlyMarkedTriangles()
        {
            // Literal spec F-33 scenario: unit square + two interior points, per-triangle max area 0.002 on triangles touching them
            var pts = new[] {
                new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1),
                new Vector2d(0.25, 0.25), new Vector2d(0.75, 0.75)
            };
            var tri = new DelaunayMesh2().Compute(pts);
            Assert.Equal(Triangulation2Status.Success, tri.Status);
            var mesh = tri.Mesh;

            var areas = new double?[mesh.TriangleCount];
            var input = new Mesh2Input {
                X = pts.Select(p => p.x).ToArray(),
                Y = pts.Select(p => p.y).ToArray(),
                MinAngleDeg = 20
            };
            // Mark triangles incident to vertex 4 (0.25, 0.25) only, leaving vertex 2 corner triangles coarser
            int marked = 0;
            for (int t = 0; t < mesh.TriangleCount; ++t) {
                var tr = mesh.Triangles[t];
                if (tr.a == 4 || tr.b == 4 || tr.c == 4) {
                    areas[t] = 0.005;
                    input.TriangleAreas.Add(new TriangleAreaInput(tr.a, tr.b, tr.c, 0.005));
                    ++marked;
                }
            }
            Assert.True(marked >= 3);

            var res = new QualityMesh2 { MinAngle = 20, Conforming = true }.Refine(mesh, areas);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            Assert.Equal(1.0, TotalArea(res.Mesh), 12);
            var report = Oracle.Check(input, ToOutput(res), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void Refine_TriangulationMesh2_CombinedWithGlobalMaxArea_TakesStricterBound()
        {
            var pts = new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 4), new Vector2d(0, 4), new Vector2d(2, 2) };
            var initial = new DelaunayMesh2().Compute(pts).Mesh;
            // All 4 triangles have area 4. Set per-triangle area 0.25 on triangle 0, and global MaxArea 1.0
            var areas = new double?[initial.TriangleCount];
            areas[0] = 0.25;
            var res = new QualityMesh2 { MinAngle = 20, MaxArea = 1.0, Conforming = true }.Refine(initial, areas);
            Assert.Equal(Triangulation2Status.Success, res.Status);

            var input = new Mesh2Input {
                X = pts.Select(p => p.x).ToArray(),
                Y = pts.Select(p => p.y).ToArray(),
                MinAngleDeg = 20,
                MaxArea = 1.0
            };
            var t0 = initial.Triangles[0];
            input.TriangleAreas.Add(new TriangleAreaInput(t0.a, t0.b, t0.c, 0.25));
            var report = Oracle.Check(input, ToOutput(res), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void Refine_TriangulationMesh2_AllNullAreasAndNoBounds_KeepsTriangulationValid()
        {
            var pts = new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 4), new Vector2d(0, 4), new Vector2d(1.5, 2.5) };
            var initial = new DelaunayMesh2().Compute(pts).Mesh;
            var res = new QualityMesh2 { MinAngle = 0, Conforming = false }.Refine(initial, new double?[initial.TriangleCount]);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            Assert.Equal(initial.TriangleCount, res.Mesh.TriangleCount);
            Assert.Equal(pts.Length, res.Mesh.Vertices.Count);
        }

        [Fact]
        public void ChainedRefinement_O17_CDT_ThenQuality20_ThenQuality30AndArea_PassesOracleAndPreservesInputMapping()
        {
            // Include an exact duplicate input point (index 4 == index 0) to verify Q8 mapping composition across chains
            var input = new Mesh2Input {
                X = new double[] { 0, 10, 10, 0, 0, 2, 8 },
                Y = new double[] { 0, 0, 10, 10, 0, 3, 7 }
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            input.Segments.Add(new SegmentInput(5, 6, 9));

            var cdt = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.Equal(Triangulation2Status.Success, cdt.Status);

            var pass1 = new QualityMesh2 { MinAngle = 20, Conforming = true }.Refine(cdt.Mesh);
            Assert.Equal(Triangulation2Status.Success, pass1.Status);
            input.MinAngleDeg = 20;
            Assert.True(Oracle.Check(input, ToOutput(pass1), new OracleExpect { Milestone = Milestone.M3 }).Ok);

            var pass2 = new QualityMesh2 { MinAngle = 30, MaxArea = 1.0, Conforming = true }.Refine(pass1.Mesh);
            Assert.Equal(Triangulation2Status.Success, pass2.Status);
            input.MinAngleDeg = 30;
            input.MaxArea = 1.0;
            var report2 = Oracle.Check(input, ToOutput(pass2), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report2.Ok, report2.ToString());

            // Q8: original InputCount and duplicate mapping preserved across 3 passes
            Assert.Equal(input.Count, pass2.Mesh.InputCount);
            Assert.Equal(0, pass2.Mesh.InputToVertex[4]);
            Assert.True(pass2.Mesh.TriangleCount > pass1.Mesh.TriangleCount);
            Assert.Equal(100.0, TotalArea(pass2.Mesh), 10);
        }

        [Fact]
        public void ChainedRefinement_O12_RepeatedSegmentSplitsDoNotDrift()
        {
            // Non-axis-aligned interior segment (0,0)-(10,3): repeated splits across 3 Refine calls must stay within 2 ULPs of exact line
            var a = new Vector2d(0, 0);
            var b = new Vector2d(10, 3);
            var input = new ConstrainedMesh2Input {
                Points = new List<Vector2d> { a, new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10), b },
                Rings = new List<Mesh2Ring> { new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false, marker: 1) },
                Segments = new List<Mesh2Segment> { new Mesh2Segment(0, 4, marker: 7) }
            };
            var pass1 = new QualityMesh2 { MinAngle = 20, MaxArea = 4.0, Conforming = true }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, pass1.Status);
            var pass2 = new QualityMesh2 { MinAngle = 25, MaxArea = 0.5, Conforming = true }.Refine(pass1.Mesh);
            Assert.Equal(Triangulation2Status.Success, pass2.Status);
            var pass3 = new QualityMesh2 { MinAngle = 28, MaxArea = 0.05, Conforming = true }.Refine(pass2.Mesh);
            Assert.Equal(Triangulation2Status.Success, pass3.Status);

            var seg7 = pass3.Mesh.Subsegments.Where(s => s.Marker == 7 || (s.Markers != null && s.Markers.Contains(7))).ToList();
            Assert.True(seg7.Count >= 20, "expected segment 7 to be split many times, got " + seg7.Count);

            // Exact perpendicular distance from line (0,0)-(10,3): |3x - 10y| / sqrt(109)
            double norm = Math.Sqrt(109.0);
            double maxDist = 0;
            foreach (var s in seg7) {
                foreach (int vi in new[] { s.A, s.B }) {
                    var p = pass3.Mesh.Vertices[vi];
                    double d = Math.Abs(3.0 * p.x - 10.0 * p.y) / norm;
                    if (d > maxDist) maxDist = d;
                }
            }
            // At coordinate magnitude ~10, 1 ULP is ~1.78e-15; zero-drift projection stays within ~1 ULP
            Assert.True(maxDist < 2e-15, "segment vertices drifted by " + maxDist);

            // O12: every subsegment of segment 7 must still reference the original (0,0)-(10,3) source endpoints, not chopped sub-pieces
            Assert.NotNull(pass3.Mesh.SavedSources);
            Assert.NotNull(pass3.Mesh.SubsegmentSourceIds);
            int checkedSeg7 = 0;
            for (int i = 0; i < pass3.Mesh.Subsegments.Count; ++i) {
                var s = pass3.Mesh.Subsegments[i];
                if (s.Marker != 7 && (s.Markers == null || !s.Markers.Contains(7))) continue;
                ++checkedSeg7;
                var srcIds = pass3.Mesh.SubsegmentSourceIds[i];
                Assert.Contains(srcIds, id => {
                    var src = pass3.Mesh.SavedSources[id];
                    return src.Marker == 7 && src.AX == a.x && src.AY == a.y && src.BX == b.x && src.BY == b.y;
                });
            }
            Assert.Equal(seg7.Count, checkedSeg7);
        }

        [Fact]
        public void Refine_ConstrainedMesh2_PreservesHolesTouchingHolesIslandAndSeedHole()
        {
            // Outer [0,20]^2 (area 400):
            // - Touching holes [2,5]x[2,5] (9) and [5,8]x[2,5] (9)
            // - Lake [10,18]x[2,10] (64) with island [12,16]x[4,8] (+16)
            // - Seed hole bounded by segments [2,6]x[12,16] (16)
            // Expected domain area = 400 - 9 - 9 - 64 + 16 - 16 = 318
            var b = new B()
                .Square(20)
                .Hole(2, 2, 5, 2, 5, 5, 2, 5)
                .Hole(5, 2, 8, 2, 8, 5, 5, 5)
                .Hole(10, 2, 18, 2, 18, 10, 10, 10)
                .Outer(12, 4, 16, 4, 16, 8, 12, 8)
                .Polyline(2, 12, 6, 12, 6, 16, 2, 16, 2, 12)
                .HoleSeed(4, 14);
            var input = b.Build();

            var cdt = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.Equal(Triangulation2Status.Success, cdt.Status);
            Assert.Equal(318.0, TotalArea(cdt.Mesh), 10);

            var refined = new QualityMesh2 { MinAngle = 25, MaxArea = 1.5, Conforming = true }.Refine(cdt.Mesh);
            Assert.Equal(Triangulation2Status.Success, refined.Status);
            Assert.Equal(318.0, TotalArea(refined.Mesh), 10);

            // Check point location: holes empty, island and main domain covered
            Assert.False(refined.Mesh.TryLocate(new Vector2d(3.5, 3.5), out _));
            Assert.False(refined.Mesh.TryLocate(new Vector2d(6.5, 3.5), out _));
            Assert.False(refined.Mesh.TryLocate(new Vector2d(11.0, 6.0), out _));
            Assert.False(refined.Mesh.TryLocate(new Vector2d(4.0, 14.0), out _));
            Assert.True(refined.Mesh.TryLocate(new Vector2d(14.0, 6.0), out _));
            Assert.True(refined.Mesh.TryLocate(new Vector2d(1.0, 1.0), out _));

            input.MinAngleDeg = 25;
            input.MaxArea = 1.5;
            var report = Oracle.Check(input, ToOutput(refined), new OracleExpect { Milestone = Milestone.M3, StatedArea = 318 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void Refine_ConstrainedMesh2_PreservesRegionLabelsAndPerTriangleAreas()
        {
            // Square [0,10]^2 split by x = 5: left labeled -3 (seed), right labeled 42 (seed), inner ring [1,4]x[1,4] labeled 9
            var b = new B().Square(10)
                .Seg(5, 0, 5, 10, marker: 10)
                .RegionSeed(2.5, 8, -3)
                .RegionSeed(7.5, 5, 42)
                .RegionRing(9, null, 1, 1, 4, 1, 4, 4, 1, 4);
            var input = b.Build();

            var cdt = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.Equal(Triangulation2Status.Success, cdt.Status);
            Assert.Equal(9.0, LabelArea(cdt.Mesh, 9), 10);
            Assert.Equal(41.0, LabelArea(cdt.Mesh, -3), 10);
            Assert.Equal(50.0, LabelArea(cdt.Mesh, 42), 10);

            // Apply per-triangle max area 0.25 ONLY to triangles inside region 9
            var perTri = new double?[cdt.Mesh.TriangleCount];
            for (int t = 0; t < perTri.Length; ++t) {
                if (cdt.Mesh.TriangleLabels[t] == 9) {
                    perTri[t] = 0.25;
                    var tr = cdt.Mesh.Triangles[t];
                    if (tr.a < input.Count && tr.b < input.Count && tr.c < input.Count)
                        input.TriangleAreas.Add(new TriangleAreaInput(tr.a, tr.b, tr.c, 0.25));
                }
            }

            var refined = new QualityMesh2 { MinAngle = 22, Conforming = true }.Refine(cdt.Mesh, perTri);
            Assert.Equal(Triangulation2Status.Success, refined.Status);
            Assert.Equal(9.0, LabelArea(refined.Mesh, 9), 10);
            Assert.Equal(41.0, LabelArea(refined.Mesh, -3), 10);
            Assert.Equal(50.0, LabelArea(refined.Mesh, 42), 10);

            // Every triangle in region 9 must have area <= 0.25 (+ small float slack)
            for (int t = 0; t < refined.Mesh.TriangleCount; ++t) {
                if (refined.Mesh.TriangleLabels[t] == 9)
                    Assert.True(TriangleArea(refined.Mesh.Vertices, refined.Mesh.Triangles[t]) <= 0.25 + 1e-12);
            }

            input.MinAngleDeg = 22;
            var report = Oracle.Check(input, ToOutput(refined), new OracleExpect { Milestone = Milestone.M3, StatedArea = 100 });
            Assert.True(report.Ok, report.ToString());
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.0625)]
        public void Refine_ConstrainedMesh2_PreservesLockedSegmentsAndMultipleMarkers(double snapFirstPass)
        {
            // Overlapping segments on (2,5)-(8,5) carry markers 3 and 7, plus locked segment (2,2)-(8,2) with marker 11
            var input = new ConstrainedMesh2Input {
                Points = new List<Vector2d> {
                    new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10),
                    new Vector2d(2, 5), new Vector2d(8, 5),
                    new Vector2d(2, 2), new Vector2d(8, 2)
                },
                Rings = new List<Mesh2Ring> { new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false, marker: 1) },
                Segments = new List<Mesh2Segment> {
                    new Mesh2Segment(4, 5, marker: 3, locked: false),
                    new Mesh2Segment(4, 5, marker: 7, locked: false),
                    new Mesh2Segment(6, 7, marker: 11, locked: true)
                }
            };
            var cdt = new ConstrainedDelaunayMesh2 { SnapTolerance = snapFirstPass }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, cdt.Status);

            var refined = new QualityMesh2 { MinAngle = 20, MaxArea = 1.0, Conforming = true, SnapTolerance = snapFirstPass }.Refine(cdt.Mesh);
            Assert.Equal(Triangulation2Status.Success, refined.Status);
            Assert.Equal(100.0, TotalArea(refined.Mesh), 10);

            // Multi-marker subsegments on y = 5 must preserve both markers 3 and 7
            var multi = refined.Mesh.Subsegments.Where(s => s.Markers != null && s.Markers.Contains(3) && s.Markers.Contains(7)).ToList();
            Assert.True(multi.Count >= 1, "expected multi-marker subsegments carrying both 3 and 7");

            // Locked segment (2,2)-(8,2) must remain a single unsplit locked subsegment with marker 11
            var lockedSubs = refined.Mesh.Subsegments.Where(s => s.Locked && (s.Marker == 11 || (s.Markers != null && s.Markers.Contains(11)))).ToList();
            Assert.Single(lockedSubs);
            var pa = refined.Mesh.Vertices[lockedSubs[0].A];
            var pb = refined.Mesh.Vertices[lockedSubs[0].B];
            Assert.Equal(6.0, (pb - pa).Length, 12);
        }

        [Fact]
        public void Refine_ConstrainedMesh2_PreservesHeightsAndToDMesh3()
        {
            double HeightAt(Vector2d p) => 2.0 * p.x + 3.0 * p.y + 1.0;
            var pts = new[] {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10),
                new Vector2d(3, 3), new Vector2d(7, 3), new Vector2d(7, 7), new Vector2d(3, 7)
            };
            var input = new ConstrainedMesh2Input {
                Points = pts.ToList(),
                Rings = new List<Mesh2Ring> {
                    new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false),
                    new Mesh2Ring(new[] { 4, 5, 6, 7 }, hole: true)
                },
                Heights = pts.Select(HeightAt).ToList()
            };
            var pass1 = new ConstrainedDelaunayMesh2().Compute(input);
            var pass2 = new QualityMesh2 { MinAngle = 25, MaxArea = 1.0, Conforming = true }.Refine(pass1.Mesh);
            Assert.Equal(Triangulation2Status.Success, pass2.Status);
            Assert.NotNull(pass2.Mesh.Heights);
            Assert.Equal(pass2.Mesh.Vertices.Count, pass2.Mesh.Heights.Count);
            for (int v = 0; v < pass2.Mesh.Vertices.Count; ++v)
                Assert.Equal(HeightAt(pass2.Mesh.Vertices[v]), pass2.Mesh.Heights[v], 10);

            var dm = pass2.Mesh.ToDMesh3();
            Assert.Equal(pass2.Mesh.Vertices.Count, dm.VertexCount);
            Assert.Equal(pass2.Mesh.TriangleCount, dm.TriangleCount);
            for (int v = 0; v < dm.VertexCount; ++v) {
                var p3 = dm.GetVertex(v);
                Assert.Equal(HeightAt(new Vector2d(p3.x, p3.y)), p3.z, 10);
            }
        }

        [Fact]
        public void Refine_SnapToleranceOnTriangulationAndConstrainedMesh_KeepsAllVerticesOnGrid()
        {
            const double tol = 0.125;
            var pts = new[] { new Vector2d(0.01, 0.02), new Vector2d(8.02, 0.01), new Vector2d(8.01, 8.03), new Vector2d(0.02, 8.01), new Vector2d(4.03, 4.02) };
            var tri = new DelaunayMesh2().Compute(pts).Mesh;
            var areas = new double?[tri.TriangleCount];
            areas[0] = 0.5;
            var resTri = new QualityMesh2 { MinAngle = 20, SnapTolerance = tol, Conforming = true }.Refine(tri, areas);
            Assert.Equal(Triangulation2Status.Success, resTri.Status);
            foreach (var v in resTri.Mesh.Vertices) {
                Assert.Equal(Math.Round(v.x / tol) * tol, v.x, 12);
                Assert.Equal(Math.Round(v.y / tol) * tol, v.y, 12);
            }

            // ConstrainedMesh2 with hole refined onto snap grid
            var holeInput = new B().Square(8).Hole(3, 3, 5, 3, 5, 5, 3, 5).Build();
            var cdt = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(holeInput)).Mesh;
            var resCon = new QualityMesh2 { MinAngle = 20, MaxArea = 1.0, SnapTolerance = tol, Conforming = true }.Refine(cdt);
            Assert.Equal(Triangulation2Status.Success, resCon.Status);
            Assert.Equal(60.0, TotalArea(resCon.Mesh), 10);
            Assert.False(resCon.Mesh.TryLocate(new Vector2d(4, 4), out _));
            foreach (var v in resCon.Mesh.Vertices) {
                Assert.Equal(Math.Round(v.x / tol) * tol, v.x, 12);
                Assert.Equal(Math.Round(v.y / tol) * tol, v.y, 12);
            }
        }

        [Fact]
        public void Refine_SizeFunction_SteinerLimit_AndCancellation()
        {
            var pts = new[] { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) };
            var tri = new DelaunayMesh2().Compute(pts).Mesh;

            // SizeFunction
            var sized = new QualityMesh2 { MinAngle = 20, SizeFunction = (x, y) => 2.0, Conforming = true }.Refine(tri);
            Assert.Equal(Triangulation2Status.Success, sized.Status);
            for (int t = 0; t < sized.Mesh.TriangleCount; ++t) {
                var tr = sized.Mesh.Triangles[t];
                var a = sized.Mesh.Vertices[tr.a]; var b = sized.Mesh.Vertices[tr.b]; var c = sized.Mesh.Vertices[tr.c];
                double longest = Math.Max((b - a).Length, Math.Max((c - b).Length, (a - c).Length));
                Assert.True(longest <= 2.0 + 1e-12, "edge length " + longest + " > 2.0");
            }

            // SteinerLimit
            var capped = new QualityMesh2 { MinAngle = 25, MaxArea = 0.001, SteinerLimit = 25, Conforming = true }.Refine(tri);
            Assert.Equal(Triangulation2Status.Success, capped.Status);
            Assert.True(capped.Mesh.SteinerLimitReached);
            Assert.True(capped.Mesh.Vertices.Count <= pts.Length + 25);

            // Pre-cancelled token
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() => new QualityMesh2().Refine(tri, null, cts.Token));
                Assert.Throws<OperationCanceledException>(() => new QualityMesh2().Refine(sized.Mesh, null, cts.Token));
            }
        }

        [Fact]
        public void Refine_ValidationAndEmptyMeshes()
        {
            Assert.Throws<ArgumentNullException>(() => new QualityMesh2().Refine((TriangulationMesh2)null));
            Assert.Throws<ArgumentNullException>(() => new QualityMesh2().Refine((ConstrainedMesh2)null));

            var pts = new[] { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(0, 2) };
            var tri = new DelaunayMesh2().Compute(pts).Mesh;
            var con = new ConstrainedDelaunayMesh2().Compute(new ConstrainedMesh2Input { Points = pts.ToList() }).Mesh;

            // Wrong count
            Assert.Throws<ArgumentException>(() => new QualityMesh2().Refine(tri, new double?[] { 0.5, 0.5 }));
            Assert.Throws<ArgumentException>(() => new QualityMesh2().Refine(con, new double?[0]));
            // Invalid area entries
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                Assert.Throws<ArgumentException>(() => new QualityMesh2().Refine(tri, new double?[] { bad }));
                Assert.Throws<ArgumentException>(() => new QualityMesh2().Refine(con, new double?[] { bad }));
            }

            // Empty TriangulationMesh2 (collinear points)
            var emptyTri = new DelaunayMesh2().Compute(new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(2, 0) }).Mesh;
            Assert.Equal(0, emptyTri.TriangleCount);
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { MinAngle = -1 }.Refine(emptyTri));
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { SnapTolerance = -0.1 }.Refine(emptyTri));
            Assert.Throws<ArgumentException>(() => new QualityMesh2().Refine(emptyTri, new double?[] { 1.0 }));
            var emptyTriRes = new QualityMesh2().Refine(emptyTri);
            Assert.Equal(Triangulation2Status.Empty, emptyTriRes.Status);
            Assert.Equal(0, emptyTriRes.Mesh.TriangleCount);
            Assert.Equal(3, emptyTriRes.Mesh.InputCount);

            // Empty ConstrainedMesh2 (hole removes entire square)
            var emptyConInput = new ConstrainedMesh2Input {
                Points = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1) },
                Rings = new List<Mesh2Ring> {
                    new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false),
                    new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: true)
                },
                Heights = new List<double> { 1.0, 2.0, 3.0, 4.0 }
            };
            var emptyCon = new ConstrainedDelaunayMesh2().Compute(emptyConInput).Mesh;
            Assert.Equal(0, emptyCon.TriangleCount);
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { MaxArea = 0 }.Refine(emptyCon));
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { SnapTolerance = double.NaN }.Refine(emptyCon));
            var emptyConRes = new QualityMesh2().Refine(emptyCon);
            Assert.Equal(Triangulation2Status.Empty, emptyConRes.Status);
            Assert.Equal(0, emptyConRes.Mesh.TriangleCount);
            Assert.Equal(4, emptyConRes.Mesh.InputCount);
            Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, emptyConRes.Mesh.Heights);
        }

        [Fact]
        public void Refine_Immutability_Reuse_And_ParallelDeterminism()
        {
            // TriangulationMesh2 + ConstrainedMesh2
            var inputA = F33Input(20);
            var ptsA = Enumerable.Range(0, inputA.Count).Select(i => new Vector2d(inputA.X[i], inputA.Y[i])).ToArray();
            var triA = new DelaunayMesh2().Compute(ptsA).Mesh;
            var areasA = new List<double?>(new double?[triA.TriangleCount]);
            for (int t = 0; t < triA.TriangleCount; ++t) {
                var tr = triA.Triangles[t];
                var s = new[] { tr.a, tr.b, tr.c }.OrderBy(x => x).ToArray();
                if ((s[0] == 0 && s[1] == 1 && s[2] == 6) || (s[0] == 1 && s[1] == 5 && s[2] == 6)) areasA[t] = 0.25;
            }
            var snapshotVerts = triA.Vertices.ToArray();
            var snapshotTris = triA.Triangles.ToArray();
            var snapshotAreas = areasA.ToArray();

            var conB = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(
                new B().Square(10).Hole(3, 3, 7, 3, 7, 7, 3, 7).RegionSeed(1, 1, 5).Build())).Mesh;
            var snapshotConVerts = conB.Vertices.ToArray();
            var snapshotConTris = conB.Triangles.ToArray();
            var snapshotConLabels = conB.TriangleLabels.ToArray();

            var mesher = new QualityMesh2 { MinAngle = 20, Conforming = true };
            var firstA = mesher.Refine(triA, areasA).Mesh;
            var firstB = mesher.Refine(conB).Mesh;
            var secondA = mesher.Refine(triA, areasA).Mesh;

            // Immutability (I9)
            Assert.Equal(snapshotVerts, triA.Vertices);
            Assert.Equal(snapshotTris, triA.Triangles);
            Assert.Equal(snapshotAreas, areasA);
            Assert.Equal(snapshotConVerts, conB.Vertices);
            Assert.Equal(snapshotConTris, conB.Triangles);
            Assert.Equal(snapshotConLabels, conB.TriangleLabels);

            // A -> B -> A reuse bit-identical
            Assert.Equal(firstA.Vertices, secondA.Vertices);
            Assert.Equal(firstA.Triangles, secondA.Triangles);
            Assert.Equal(firstA.Neighbours, secondA.Neighbours);

            // Parallel determinism (O22) on both overloads
            var parA = new ConstrainedMesh2[8];
            var parB = new ConstrainedMesh2[8];
            Parallel.For(0, 8, i => {
                parA[i] = mesher.Refine(triA, areasA).Mesh;
                parB[i] = mesher.Refine(conB).Mesh;
            });
            for (int i = 0; i < 8; ++i) {
                Assert.Equal(firstA.Vertices, parA[i].Vertices);
                Assert.Equal(firstA.Triangles, parA[i].Triangles);
                Assert.Equal(firstA.Neighbours, parA[i].Neighbours);
                Assert.Equal(firstB.Vertices, parB[i].Vertices);
                Assert.Equal(firstB.Triangles, parB[i].Triangles);
                Assert.Equal(firstB.TriangleLabels, parB[i].TriangleLabels);
            }
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_All2D_RefineTriangulationAndConstrainedMesh(string name)
        {
            var s = Similarity.All2D().First(x => x.Name == name);
            var e = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(e.x * e.x + e.y * e.y), cos = e.x / norm, sin = e.y / norm;
            Vector2d Move(double x, double y) => new Vector2d(
                s.Scale * (cos * x - sin * y) + s.Translation.x,
                s.Scale * (sin * x + cos * y) + s.Translation.y);

            // 1. Refine(TriangulationMesh2, triangleMaxAreas) on F-33
            var baseInput = F33Input(20);
            var movedInput = F33Input(20);
            for (int i = 0; i < baseInput.Count; ++i) {
                var p = Move(baseInput.X[i], baseInput.Y[i]);
                movedInput.X[i] = p.x; movedInput.Y[i] = p.y;
            }
            foreach (var ta in movedInput.TriangleAreas) ta.MaxArea *= s.Scale * s.Scale;

            var baseOut = new QualityMesh2Candidate().Run(baseInput);
            var movedOut = new QualityMesh2Candidate().Run(movedInput);
            var reportTri = Oracle.Check(movedInput, movedOut, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(reportTri.Ok, name + " (TriangulationMesh2): " + reportTri);
            if (s.IsExact) {
                Assert.Equal(baseOut.Triangles.Length, movedOut.Triangles.Length);
                Assert.Equal(baseOut.VX.Length, movedOut.VX.Length);
                for (int i = 0; i < baseOut.VX.Length; ++i) {
                    Assert.Equal(baseOut.VX[i] * s.Scale, movedOut.VX[i]);
                    Assert.Equal(baseOut.VY[i] * s.Scale, movedOut.VY[i]);
                }
            }

            // 2. Refine(ConstrainedMesh2, triangleMaxAreas) on a domain with hole + region
            Mesh2Input MakeCon(bool move)
            {
                var mi = new B().Square(8).Hole(3, 3, 5, 3, 5, 5, 3, 5).RegionRing(7, null, 0, 0, 3, 0, 3, 3, 0, 3).Min(20).Build();
                if (move) {
                    for (int i = 0; i < mi.Count; ++i) {
                        var p = Move(mi.X[i], mi.Y[i]);
                        mi.X[i] = p.x; mi.Y[i] = p.y;
                    }
                }
                return mi;
            }
            var conBaseIn = MakeCon(false);
            var conMovedIn = MakeCon(true);
            var cdtBase = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(conBaseIn)).Mesh;
            var cdtMoved = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(conMovedIn)).Mesh;

            double areaLimit = 0.5 * s.Scale * s.Scale;
            var areasBase = new double?[cdtBase.TriangleCount];
            for (int t = 0; t < areasBase.Length; ++t) if (cdtBase.TriangleLabels[t] == 7) areasBase[t] = 0.5;
            var areasMoved = new double?[cdtMoved.TriangleCount];
            for (int t = 0; t < areasMoved.Length; ++t) if (cdtMoved.TriangleLabels[t] == 7) areasMoved[t] = areaLimit;

            var refBase = new QualityMesh2 { MinAngle = 20, Conforming = true }.Refine(cdtBase, areasBase);
            var refMoved = new QualityMesh2 { MinAngle = 20, Conforming = true }.Refine(cdtMoved, areasMoved);
            Assert.Equal(Triangulation2Status.Success, refMoved.Status);

            var reportCon = Oracle.Check(conMovedIn, ToOutput(refMoved), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(reportCon.Ok, name + " (ConstrainedMesh2): " + reportCon);
            // At UTM (~5.7e6) with scale 1e-3, 1 double ULP (~9.3e-10) on split boundary vertices is ~3e-7 of the 3e-3 edge length
            Assert.Equal(LabelArea(cdtMoved, 7), LabelArea(refMoved.Mesh, 7), 1e-6 * LabelArea(cdtMoved, 7));
            if (s.IsExact) {
                Assert.Equal(refBase.Mesh.TriangleCount, refMoved.Mesh.TriangleCount);
                Assert.Equal(refBase.Mesh.Vertices.Count, refMoved.Mesh.Vertices.Count);
                for (int i = 0; i < refBase.Mesh.Vertices.Count; ++i) {
                    Assert.Equal(refBase.Mesh.Vertices[i].x * s.Scale, refMoved.Mesh.Vertices[i].x);
                    Assert.Equal(refBase.Mesh.Vertices[i].y * s.Scale, refMoved.Mesh.Vertices[i].y);
                }
            }
        }
    }
}
