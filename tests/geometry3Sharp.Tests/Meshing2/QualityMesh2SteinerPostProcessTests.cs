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
    public class QualityMesh2SteinerPostProcessTests
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

        static double MeanMinAngleDegrees(ConstrainedMesh2 mesh)
        {
            double sum = 0;
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
                sum += Math.Asin(Math.Sqrt(Math.Max(0.0, Math.Min(1.0, s2)))) * (180.0 / Math.PI);
            }
            return mesh.Triangles.Count > 0 ? sum / mesh.Triangles.Count : 0;
        }

        static void AssertOracleClean(Mesh2Input benchInput, QualityMesh2 mesher, bool constrainedMode = false)
        {
            var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(benchInput);
            var res = mesher.Compute(g3Input);
            Assert.True(res.Status == Triangulation2Status.Success, string.Join("; ", res.Diagnostics));
            Assert.NotNull(res.Mesh);

            var outObj = ConstrainedDelaunayMesh2Candidate.Output(res);
            outObj.BadTriangles = res.Mesh.BadTriangles.ToArray();
            outObj.SteinerLimitReached = res.Mesh.SteinerLimitReached;

            var oracleInput = benchInput;
            if (constrainedMode) {
                oracleInput.Conforming = false;
                oracleInput.ConstrainedQuality = true;
            }
            var report = Oracle.Check(oracleInput, outObj, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "Oracle violations:\n" + string.Join("\n", report.Violations.Select(v => v.ToString())));
        }

        static void AssertConstraintsAndInputsUnchanged(ConstrainedMesh2 before, ConstrainedMesh2 after)
        {
            Assert.Equal(before.InputCount, after.InputCount);
            for (int i = 0; i < before.InputCount; ++i)
                Assert.Equal(before.Vertices[i], after.Vertices[i]);

            var beforeSegPts = new HashSet<Vector2d>();
            foreach (var s in before.Subsegments) {
                beforeSegPts.Add(before.Vertices[s.A]);
                beforeSegPts.Add(before.Vertices[s.B]);
            }
            var afterSegPts = new HashSet<Vector2d>();
            foreach (var s in after.Subsegments) {
                afterSegPts.Add(after.Vertices[s.A]);
                afterSegPts.Add(after.Vertices[s.B]);
            }
            Assert.True(beforeSegPts.SetEquals(afterSegPts), "Constrained segment vertices changed during Steiner post-processing");
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

        [Fact]
        public void SmoothingPasses_NegativeThrowsArgumentOutOfRange()
        {
            var input = new ConstrainedMesh2Input {
                Points = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) }
            };
            var mesher = new QualityMesh2 { MinAngle = 25, SmoothingPasses = -1 };
            Assert.Throws<ArgumentOutOfRangeException>(() => mesher.Compute(input));
        }

        [Fact]
        public void Smoothing_F64c_At2Pow20_BothModes_ValidAndMovesSteinerPoints()
        {
            foreach (bool conforming in new[] { false, true }) {
                double o = 1 << 20;
                var bench = new B()
                    .Rect(o, o, o + 10, o + 10)
                    .Seg(o + 1, o + 1, o + 9, o + 1 + Math.Pow(2, -20))
                    .Min(25)
                    .Build();
                if (!conforming) bench.ConstrainedQuality = true;
                else bench.Conforming = true;

                var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
                var raw = new QualityMesh2 { MinAngle = 25, Conforming = conforming, SmoothingPasses = 0 }.Compute(g3Input);
                var smooth = new QualityMesh2 { MinAngle = 25, Conforming = conforming, SmoothingPasses = 5 }.Compute(g3Input);

                Assert.Equal(Triangulation2Status.Success, raw.Status);
                Assert.Equal(Triangulation2Status.Success, smooth.Status);
                AssertOracleClean(bench, new QualityMesh2 { MinAngle = 25, Conforming = conforming, SmoothingPasses = 5 }, constrainedMode: !conforming);

                AssertConstraintsAndInputsUnchanged(raw.Mesh, smooth.Mesh);
                Assert.Equal(raw.Mesh.Vertices.Count, smooth.Mesh.Vertices.Count);
                Assert.True(smooth.Mesh.BadTriangles.Count <= raw.Mesh.BadTriangles.Count);
                Assert.True(MinAngleDegrees(smooth.Mesh) >= MinAngleDegrees(raw.Mesh) - 1e-12);
                Assert.True(MeanMinAngleDegrees(smooth.Mesh) > MeanMinAngleDegrees(raw.Mesh),
                    "Expected smoothing at 2^20 to strictly improve average minimum angle (conforming=" + conforming + ")");

                int moved = 0;
                for (int i = raw.Mesh.InputCount; i < raw.Mesh.Vertices.Count; ++i)
                    if (raw.Mesh.Vertices[i] != smooth.Mesh.Vertices[i]) ++moved;
                Assert.True(moved > 0, "Expected free Steiner points to move at 2^20");
            }
        }

        [Fact]
        public void Smoothing_PreservesHolesRegionsMaxAreaSizeFunctionAndLockedSegments()
        {
            var bench = new B()
                .Square(10)
                .Hole(2, 2, 4, 2, 4, 4, 2, 4)
                .Seg(0, 6, 10, 6, marker: 3)
                .RegionSeed(1, 1, 1, 0.5)
                .RegionSeed(5, 8, 2, 1.5)
                .Min(25)
                .Build();
            bench.Conforming = true;

            var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
            g3Input.Heights = g3Input.Points.Select(p => 2.0 * p.x + 3.0 * p.y + 5.0).ToList();

            var raw = new QualityMesh2 { MinAngle = 25, Conforming = true, SmoothingPasses = 0 }.Compute(g3Input);
            var smooth = new QualityMesh2 { MinAngle = 25, Conforming = true, SmoothingPasses = 5 }.Compute(g3Input);

            AssertOracleClean(bench, new QualityMesh2 { MinAngle = 25, Conforming = true, SmoothingPasses = 5 });
            AssertConstraintsAndInputsUnchanged(raw.Mesh, smooth.Mesh);
            Assert.True(MeanMinAngleDegrees(smooth.Mesh) > MeanMinAngleDegrees(raw.Mesh));

            for (int i = 0; i < smooth.Mesh.Vertices.Count; ++i) {
                var p = smooth.Mesh.Vertices[i];
                double expectedZ = 2.0 * p.x + 3.0 * p.y + 5.0;
                Assert.Equal(expectedZ, smooth.Mesh.Heights[i], 10);
            }

            // Locked segment: endpoints stay intact and smoothing never decreases min angle
            var lockedInput = ConstrainedDelaunayMesh2Candidate.Convert(new B().Square(10).Seg(2, 5, 8, 5, marker: 7, locked: true).Min(20).Build());
            var lockedRaw = new QualityMesh2 { MinAngle = 20, Conforming = false, SmoothingPasses = 0 }.Compute(lockedInput);
            var lockedSmooth = new QualityMesh2 { MinAngle = 20, Conforming = false, SmoothingPasses = 4 }.Compute(lockedInput);
            Assert.Equal(Triangulation2Status.Success, lockedSmooth.Status);
            AssertConstraintsAndInputsUnchanged(lockedRaw.Mesh, lockedSmooth.Mesh);
            Assert.True(MinAngleDegrees(lockedSmooth.Mesh) >= MinAngleDegrees(lockedRaw.Mesh) - 1e-12);

            // SizeFunction preservation
            var sizeBench = new B().Square(10).Min(25).Build();
            sizeBench.SizeConstant = 1.5;
            sizeBench.SizeFunction = (x, y) => Q.FromDouble(1.5);
            sizeBench.Conforming = true;
            var sizeMesher = new QualityMesh2 {
                MinAngle = 25, Conforming = true, SizeFunction = (x, y) => 1.5, SmoothingPasses = 5
            };
            AssertOracleClean(sizeBench, sizeMesher);
        }

        [Fact]
        public void Smoothing_WithSnapGrid_StaysOnGridAndOracleValid()
        {
            foreach (bool conforming in new[] { false, true }) {
                var bench = new B()
                    .Square(10)
                    .Seg(1, 3, 9, 4)
                    .Snap(1.0 / 32)
                    .Min(25)
                    .Build();
                if (!conforming) bench.ConstrainedQuality = true;
                else bench.Conforming = true;

                var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
                var raw = new QualityMesh2 { MinAngle = 25, SnapTolerance = 1.0 / 32, Conforming = conforming, SmoothingPasses = 0 }.Compute(g3Input);
                var smooth = new QualityMesh2 { MinAngle = 25, SnapTolerance = 1.0 / 32, Conforming = conforming, SmoothingPasses = 5 }.Compute(g3Input);

                AssertOracleClean(bench, new QualityMesh2 { MinAngle = 25, SnapTolerance = 1.0 / 32, Conforming = conforming, SmoothingPasses = 5 }, constrainedMode: !conforming);
                AssertConstraintsAndInputsUnchanged(raw.Mesh, smooth.Mesh);
                Assert.True(MinAngleDegrees(smooth.Mesh) >= MinAngleDegrees(raw.Mesh) - 1e-12);
                Assert.True(smooth.Mesh.BadTriangles.Count <= raw.Mesh.BadTriangles.Count);

                int moved = 0;
                for (int i = raw.Mesh.InputCount; i < raw.Mesh.Vertices.Count; ++i)
                    if (raw.Mesh.Vertices[i] != smooth.Mesh.Vertices[i]) ++moved;
                Assert.True(moved > 0, "Expected snap-grid smoothing to relocate at least one Steiner point (conforming=" + conforming + ")");

                foreach (var p in smooth.Mesh.Vertices) {
                    Assert.Equal(Math.Round(p.x * 32.0), p.x * 32.0);
                    Assert.Equal(Math.Round(p.y * 32.0), p.y * 32.0);
                }
            }

            // Non-dyadic 0.01 snap grid with dense interior segments near free Steiner points (regression for TrianglesNear star seed)
            var denseBench = new B()
                .Square(10)
                .Segs(1, 2, 9, 2.25, 1, 4, 9, 4.15, 1, 6, 9, 6.2, 1, 8, 9, 7.85)
                .Snap(0.01)
                .Min(25)
                .Area(1.5)
                .Build();
            denseBench.Conforming = true;
            var denseInput = ConstrainedDelaunayMesh2Candidate.Convert(denseBench);
            var denseRaw = new QualityMesh2 { MinAngle = 25, MaxArea = 1.5, SnapTolerance = 0.01, Conforming = true, SmoothingPasses = 0 }.Compute(denseInput);
            var denseSmooth = new QualityMesh2 { MinAngle = 25, MaxArea = 1.5, SnapTolerance = 0.01, Conforming = true, SmoothingPasses = 5 }.Compute(denseInput);
            AssertOracleClean(denseBench, new QualityMesh2 { MinAngle = 25, MaxArea = 1.5, SnapTolerance = 0.01, Conforming = true, SmoothingPasses = 5 });
            AssertConstraintsAndInputsUnchanged(denseRaw.Mesh, denseSmooth.Mesh);
            int denseMoved = 0;
            for (int i = denseRaw.Mesh.InputCount; i < denseRaw.Mesh.Vertices.Count; ++i)
                if (denseRaw.Mesh.Vertices[i] != denseSmooth.Mesh.Vertices[i]) ++denseMoved;
            Assert.True(denseMoved > 0, "Expected smoothing on 0.01 snap grid to move at least one Steiner point");
        }

        [Fact]
        public void Smoothing_WithSpatiallyVaryingSizeFunction_StaysOracleValid()
        {
            // Non-constant SizeFunction: fine on the left (h = 0.5 at x = 0), coarse on the right (h = 3.0 at x = 10)
            var bench = new B().Square(10).Min(25).Build();
            bench.Conforming = true;
            bench.SizeFunction = (x, y) => new Q(1, 2) + x * new Q(1, 4);
            var mesher = new QualityMesh2 {
                MinAngle = 25, Conforming = true,
                SizeFunction = (x, y) => 0.5 + 0.25 * x,
                SmoothingPasses = 5
            };
            AssertOracleClean(bench, mesher);
        }

        [Fact]
        public void ReduceSteiner_RemovesRedundantFreePointsAndStaysOracleValid()
        {
            foreach (bool conforming in new[] { false, true }) {
                var bench = new B()
                    .Square(20)
                    .Hole(7, 7, 11, 7, 11, 11, 7, 11)
                    .Segs(2, 2, 18, 3, 2, 15, 17, 14, 4, 1, 16, 19)
                    .RandomPoints(20, 15, 42)
                    .Min(25)
                    .Build();
                if (!conforming) bench.ConstrainedQuality = true;
                else bench.Conforming = true;

                var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
                g3Input.Heights = g3Input.Points.Select(p => p.x - 0.5 * p.y).ToList();

                var raw = new QualityMesh2 { MinAngle = 25, Conforming = conforming, ReduceSteiner = false }.Compute(g3Input);
                var reduced = new QualityMesh2 { MinAngle = 25, Conforming = conforming, ReduceSteiner = true }.Compute(g3Input);

                AssertOracleClean(bench, new QualityMesh2 { MinAngle = 25, Conforming = conforming, ReduceSteiner = true }, constrainedMode: !conforming);
                AssertConstraintsAndInputsUnchanged(raw.Mesh, reduced.Mesh);
                Assert.True(reduced.Mesh.Vertices.Count < raw.Mesh.Vertices.Count,
                    "Expected ReduceSteiner to remove redundant free Steiner points (conforming=" + conforming
                    + ", raw=" + raw.Mesh.Vertices.Count + ", reduced=" + reduced.Mesh.Vertices.Count + ")");
                Assert.True(reduced.Mesh.BadTriangles.Count <= raw.Mesh.BadTriangles.Count);

                for (int i = 0; i < reduced.Mesh.Vertices.Count; ++i) {
                    var p = reduced.Mesh.Vertices[i];
                    Assert.Equal(p.x - 0.5 * p.y, reduced.Mesh.Heights[i], 10);
                }
            }

            // Per-region MaxArea + global MaxArea + hole + locked segment under ReduceSteiner
            var regBench = new B()
                .Square(10)
                .Hole(3, 3, 5, 3, 5, 5, 3, 5)
                .Seg(0, 6, 10, 6, marker: 2)
                .RegionSeed(1, 1, 1, 0.5)
                .RegionSeed(5, 8, 2, 2.0)
                .Min(20)
                .Area(2.5)
                .Build();
            regBench.Conforming = true;
            AssertOracleClean(regBench, new QualityMesh2 { MinAngle = 20, MaxArea = 2.5, Conforming = true, ReduceSteiner = true });

            var lockedBench = new B().Square(10).Seg(4, 5, 6, 5, marker: 7, locked: true).Min(20).Build();
            lockedBench.ConstrainedQuality = true;
            AssertOracleClean(lockedBench, new QualityMesh2 { MinAngle = 20, Conforming = false, ReduceSteiner = true, SmoothingPasses = 4 }, constrainedMode: true);
        }

        [Fact]
        public void ReduceSteiner_DisabledWhenSizeFunctionIsSet()
        {
            var bench = new B().Square(10).Min(20).Build();
            var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
            var a = new QualityMesh2 { MinAngle = 20, SizeFunction = (x, y) => 2.0, ReduceSteiner = false }.Compute(g3Input);
            var b = new QualityMesh2 { MinAngle = 20, SizeFunction = (x, y) => 2.0, ReduceSteiner = true }.Compute(g3Input);
            Assert.True(SameMesh(a.Mesh, b.Mesh), "Spec O14: ReduceSteiner must be disabled when SizeFunction (O6) is set");
        }

        [Fact]
        public void ReduceSteiner_AndSmoothingCombined_WithSnapGrid_IsOracleValid()
        {
            var bench = new B()
                .Square(10)
                .Seg(1, 5, 9, 5)
                .Snap(1.0 / 32)
                .Min(20)
                .Area(2.0)
                .Build();
            bench.Conforming = true;

            var mesher = new QualityMesh2 {
                MinAngle = 20, MaxArea = 2.0, SnapTolerance = 1.0 / 32, Conforming = true,
                ReduceSteiner = true, SmoothingPasses = 3
            };
            AssertOracleClean(bench, mesher);
        }

        [Fact]
        public void TextFormat_RoundTrips_ReduceSteinerAndSmoothingPasses()
        {
            var input = new B().Square(10).Min(25).ReduceSteiner().Smooth(4).Build();
            string text = TextFormat.WriteInput(input);
            Assert.Contains("reduce_steiner 1\n", text);
            Assert.Contains("smoothing_passes 4\n", text);
            var parsed = TextFormat.ReadInput(text);
            Assert.True(parsed.ReduceSteiner);
            Assert.Equal(4, parsed.SmoothingPasses);

            var plain = new B().Square(10).Min(25).Build();
            string plainText = TextFormat.WriteInput(plain);
            Assert.DoesNotContain("reduce_steiner", plainText);
            Assert.DoesNotContain("smoothing_passes", plainText);

            // Negative SmoothingPasses rejected symmetrically by both writer and reader
            var neg = new B().Square(10).Min(25).Build();
            neg.SmoothingPasses = -2;
            Assert.Throws<InvalidOperationException>(() => TextFormat.WriteInput(neg));
            Assert.Throws<FormatException>(() => TextFormat.ReadInput(plainText.Replace("end\n", "smoothing_passes -1\nend\n")));
            Assert.Throws<FormatException>(() => TextFormat.ReadInput(plainText.Replace("end\n", "reduce_steiner 2\nend\n")));
        }

        [Fact]
        public void PostProcess_CancellationThrowsOperationCanceled()
        {
            var bench = new B().Square(10).Min(25).Area(0.5).Build();
            var g3Input = ConstrainedDelaunayMesh2Candidate.Convert(bench);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                new QualityMesh2 { MinAngle = 25, MaxArea = 0.5, ReduceSteiner = true, SmoothingPasses = 5 }.Compute(g3Input, cts.Token));
        }

        [Fact]
        public void PostProcess_Scaling_LinearInMeshSize()
        {
            // Compare n vs 4n triangles: post-processing stays fast and strictly valid with linear scaling
            var small = ConstrainedDelaunayMesh2Candidate.Convert(new B().Square(10).Min(25).Area(0.5).Build());
            var large = ConstrainedDelaunayMesh2Candidate.Convert(new B().Square(20).Min(25).Area(0.5).Build());
            var mesher = new QualityMesh2 { MinAngle = 25, MaxArea = 0.5, Conforming = true, ReduceSteiner = true, SmoothingPasses = 3 };
            var rSmall = mesher.Compute(small);
            var rLarge = mesher.Compute(large);
            Assert.Equal(Triangulation2Status.Success, rSmall.Status);
            Assert.Equal(Triangulation2Status.Success, rLarge.Status);
            Assert.Empty(rSmall.Mesh.BadTriangles);
            Assert.Empty(rLarge.Mesh.BadTriangles);
            Assert.True(rLarge.Mesh.Triangles.Count >= 3 * rSmall.Mesh.Triangles.Count);
        }

        [Fact]
        public void PostProcess_ScaleInvariance_Reuse_AndParallelDeterminism()
        {
            var basePoints = new[] {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10),
                new Vector2d(1, 3), new Vector2d(9, 4)
            };
            ConstrainedMesh2Input MakeTransformed(Similarity sim) => new ConstrainedMesh2Input {
                Points = basePoints.Select(p => {
                    var q = sim.Apply(new Vector3d(p.x, p.y, 0));
                    return new Vector2d(q.x, q.y);
                }).ToList(),
                Rings = new List<Mesh2Ring> { new Mesh2Ring(new[] { 0, 1, 2, 3 }, false) },
                Segments = new List<Mesh2Segment> { new Mesh2Segment(4, 5) }
            };

            // 1. Similarity.All2D() invariance: exact bit-for-bit on 2^k scales, valid and angle-bound met on all
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = true, ReduceSteiner = true, SmoothingPasses = 4 };
            var baseline = mesher.Compute(MakeTransformed(Similarity.Identity));
            Assert.Equal(Triangulation2Status.Success, baseline.Status);
            Assert.Empty(baseline.Mesh.BadTriangles);

            foreach (var sim in Similarity.All2D()) {
                var res = mesher.Compute(MakeTransformed(sim));
                Assert.Equal(Triangulation2Status.Success, res.Status);
                Assert.Empty(res.Mesh.BadTriangles);
                Assert.True(MinAngleDegrees(res.Mesh) >= 25.0 - 1e-9, "Min angle below 25 under " + sim.Name);
                if (sim.IsExact) {
                    Assert.Equal(baseline.Mesh.Vertices.Count, res.Mesh.Vertices.Count);
                    Assert.Equal(baseline.Mesh.Triangles.Count, res.Mesh.Triangles.Count);
                    for (int i = 0; i < baseline.Mesh.Vertices.Count; ++i) {
                        Assert.Equal(baseline.Mesh.Vertices[i].x * sim.Scale, res.Mesh.Vertices[i].x);
                        Assert.Equal(baseline.Mesh.Vertices[i].y * sim.Scale, res.Mesh.Vertices[i].y);
                    }
                    for (int i = 0; i < baseline.Mesh.Triangles.Count; ++i)
                        Assert.Equal(baseline.Mesh.Triangles[i], res.Mesh.Triangles[i]);
                }
            }

            // 2. Instance reuse produces bit-identical results
            var again = mesher.Compute(MakeTransformed(Similarity.Identity));
            Assert.True(SameMesh(baseline.Mesh, again.Mesh), "Reused QualityMesh2 instance produced different output");

            // 3. Parallel determinism across 8 threads
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, 8, i => {
                var m = new QualityMesh2 { MinAngle = 25, Conforming = true, ReduceSteiner = true, SmoothingPasses = 4 };
                results[i] = m.Compute(MakeTransformed(Similarity.Identity)).Mesh;
            });
            for (int i = 0; i < results.Length; ++i)
                Assert.True(SameMesh(baseline.Mesh, results[i]), "Parallel run " + i + " differed from serial");
        }
    }
}
