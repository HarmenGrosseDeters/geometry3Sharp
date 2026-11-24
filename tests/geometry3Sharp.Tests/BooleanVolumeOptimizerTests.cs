using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using g3.Examples;
using Xunit;
using Xunit.Abstractions;
using Opt = g3.Examples.BooleanVolumeOptimizerExample;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The volume-fitting optimizer of docs/examples/BooleanVolumeOptimizerExample.cs (on BooleanVolumeGradient): it reaches the
    /// target with every Compute succeeding and a closed result, scales exactly under 2^k, repeats bit for bit, handles a flush
    /// contact on the realized branch, and stops on a failed Compute.
    /// </summary>
    public class BooleanVolumeOptimizerTests
    {
        readonly ITestOutputHelper output;
        public BooleanVolumeOptimizerTests(ITestOutputHelper output) { this.output = output; }

        void Log(Opt.Result r)
        {
            output.WriteLine($"{r.Reason} {r.Error}");
            for (int i = 0; i < r.Iterations.Count; ++i) {
                var s = r.Iterations[i];
                output.WriteLine($"{i}: V={s.Volume:R} bound={s.VolumeBound:E2} step={s.Step:E3}{(s.Capped ? " capped" : "")} contacts={s.Contacts}");
            }
        }

        static void AssertConverged(Opt.Result r, double target, double tolerance = 1e-9)
        {
            Assert.True(r.Reason == Opt.StopReason.Converged, $"{r.Reason}: {r.Error}");
            Assert.Null(r.Error);
            var last = r.Iterations.Last();
            Assert.True(Math.Abs(last.Volume - target) <= tolerance * target + last.VolumeBound, $"V = {last.Volume:R}");
            Assert.True(last.VolumeBound < 1e-12 * target);
            // the result itself: closed, valid, finite, and its (rounded) volume is the exact one up to the rounding
            Assert.True(r.Final.IsClosed());
            Assert.True(r.Final.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(r.Moved.IsClosed());
            foreach (Vector3d p in r.Final.Vertices())
                Assert.True(p.IsFinite);
            Assert.Equal(target, MeshMeasurements.VolumeArea(r.Final, r.Final.TriangleIndices(), r.Final.GetVertex).x, 6);
            // every step but the last moved the vertices; the last evaluation takes none
            Assert.Equal(0, last.Step);
            Assert.All(r.Iterations.Take(r.Iterations.Count - 1), s => Assert.True(s.Step > 0));
        }

        [Fact]
        public void GrowUnion_ReachesTheTarget_EveryComputeSucceeds_ResultClosed()
        {
            Opt.Result r = Opt.GrowUnion();
            Log(r);
            AssertConverged(r, 1.3);
            Assert.True(r.Iterations[0].Volume < 1.2);                   // a real fit, not the start
            Assert.Contains(r.Iterations, s => s.Capped);                  // the cap acts on the far start ...
            Assert.False(r.Iterations[r.Iterations.Count - 2].Capped);     // ... and the projected step takes the last part
            Assert.All(r.Iterations, s => Assert.Equal(0, s.Contacts));    // a sphere and a cube: no flush faces
            Assert.True(r.Iterations.Count < 20);
        }

        [Fact]
        public void Difference_ShrinksToTheTarget()
        {
            // the cube minus the sphere: moving the sphere's vertices along the gradient of V(a - b) cuts deeper
            DMesh3 a = Opt.Cube(), b = Opt.Sphere();
            Opt.Result r = Opt.FitVolume(a, b, Ops.Difference, 0.8);
            Log(r);
            AssertConverged(r, 0.8);
            Assert.True(r.Iterations[0].Volume > 0.85);
        }

        [Fact]
        public void FlushContact_UsesTheRealizedBranch_AndReportsTheContact()
        {
            // a box touching the cube's +x face: V of the union has a kink there (merged, 1/4 per unit of the face moving into
            // the cube, or separated, 0); the optimizer follows the realized (merged) branch and reports the contact
            Opt.Result r = Opt.FitVolume(Opt.Cube(), Opt.TouchingBox(), Ops.Union, 1.2);
            Log(r);
            AssertConverged(r, 1.2);
            Assert.Equal(1.125, r.Iterations[0].Volume, 12);
            Assert.Equal(1, r.Iterations[0].Contacts);
            Assert.Equal(0.25, r.Iterations[0].ContactArea, 12);
        }

        [Fact]
        public void ZeroTolerance_StopsWithinTheVolumeBound()
        {
            // the stop test also accepts a residual within VolumeBound: with Tolerance 0 that alone ends the iteration
            Opt.Result r = Opt.FitVolume(Opt.Cube(), Opt.Sphere(), Ops.Union, 1.3, new Opt.Options() { Tolerance = 0 });
            Log(r);
            Assert.Equal(Opt.StopReason.Converged, r.Reason);
            var last = r.Iterations.Last();
            Assert.True(last.Volume != 1.3 && Math.Abs(last.Volume - 1.3) <= last.VolumeBound, $"V = {last.Volume:R}");
        }

        [Fact]
        public void IterationLimit_AndFailedCompute_Stop()
        {
            Opt.Result r = Opt.FitVolume(Opt.Cube(), Opt.Sphere(), Ops.Union, 1.3, new Opt.Options() { MaxIterations = 2 });
            Assert.Equal(Opt.StopReason.MaxIterations, r.Reason);
            Assert.Equal(2, r.Iterations.Count);
            Assert.True(r.Iterations[0].Step > 0 && r.Iterations[1].Step == 0);

            // an open input: the boolean refuses it (AllowOpenMeshes is off), the optimizer stops with its error
            DMesh3 open = Opt.Sphere();
            open.RemoveTriangle(open.TriangleIndices().First());
            r = Opt.FitVolume(Opt.Cube(), open, Ops.Union, 1.3);
            Assert.Equal(Opt.StopReason.ComputeFailed, r.Reason);
            Assert.StartsWith("boolean:", r.Error);
            Assert.Empty(r.Iterations);
            Assert.Null(r.Final);
        }

        [Fact]
        public void Gradient_WithoutRetainClassification_Fails()
        {
            var boolean = new ExactMeshBoolean(Opt.Cube(), Opt.Sphere(), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            var g = new BooleanVolumeGradient(boolean);
            Assert.False(g.Compute());
            Assert.Contains("RetainClassification", g.Error);
        }

        static void AssertSame(Opt.Result a, Opt.Result b)
        {
            Assert.Equal(a.Reason, b.Reason);
            Assert.Equal(a.Iterations.Count, b.Iterations.Count);
            for (int i = 0; i < a.Iterations.Count; ++i) {
                Assert.Equal(a.Iterations[i].Volume, b.Iterations[i].Volume);
                Assert.Equal(a.Iterations[i].Step, b.Iterations[i].Step);
                Assert.Equal(a.Iterations[i].Vertices, b.Iterations[i].Vertices);
                Assert.Equal(a.Iterations[i].Triangles, b.Iterations[i].Triangles);
            }
        }

        [Fact]
        public void RepeatedRuns_AreBitIdentical()
        {
            Opt.Result first = Opt.GrowUnion(recordMeshes: true);
            AssertSame(first, Opt.GrowUnion(recordMeshes: true));
            AssertSame(first, Opt.GrowUnion(recordMeshes: true));
        }

        [Fact]
        public void Similarities_GiveTheSameIterations_BitForBitUnderPowerOfTwoScales()
        {
            // (a tolerance above the coordinate resolution of the UTM case: there one coordinate's ulp, 2^-33 at 6e5, is 1e-7 of the
            // input's size 1e-3, and a smaller residual needs steps that the rounding of the positions mostly eats)
            var options = new Opt.Options() { Tolerance = 1e-7 };
            DMesh3 a = Opt.Cube(), b = Opt.Sphere();
            Opt.Result r = Opt.FitVolume(a, b, Ops.Union, 1.3, options);
            AssertConverged(r, 1.3, options.Tolerance);
            foreach (Similarity s in Similarity.All()) {
                double s3 = s.Scale * s.Scale * s.Scale;
                Opt.Result rs = Opt.FitVolume(Invariance.Transformed(a, s), Invariance.Transformed(b, s), Ops.Union, 1.3 * s3, options);
                Assert.True(rs.Reason == Opt.StopReason.Converged, $"{s.Name}: {rs.Reason} {rs.Error}");
                Assert.Equal(r.Iterations.Count, rs.Iterations.Count);
                double worst = 0;
                for (int i = 0; i < r.Iterations.Count; ++i) {
                    if (s.IsExact) {
                        Assert.True(r.Iterations[i].Volume * s3 == rs.Iterations[i].Volume, $"{s.Name} {i}: {r.Iterations[i].Volume:R} {rs.Iterations[i].Volume:R}");
                        Assert.True(r.Iterations[i].VolumeBound * s3 == rs.Iterations[i].VolumeBound, $"{s.Name} {i} bound");
                        Assert.True(r.Iterations[i].Step * s.Scale == rs.Iterations[i].Step, $"{s.Name} {i} step");
                    }
                    worst = Math.Max(worst, Math.Abs(rs.Iterations[i].Volume / s3 - r.Iterations[i].Volume));
                }
                if (s.IsExact)
                    foreach (int v in r.Moved.VertexIndices())
                        Assert.True(r.Moved.GetVertex(v) * s.Scale == rs.Moved.GetVertex(v), $"{s.Name} vertex {v}");
                else
                    foreach (int v in r.Moved.VertexIndices())
                        Assert.True(s.Inverse(rs.Moved.GetVertex(v)).Distance(r.Moved.GetVertex(v)) < Math.Max(1e-6, 100 * s.Resolution(2)), $"{s.Name} vertex {v}: {s.Inverse(rs.Moved.GetVertex(v))} {r.Moved.GetVertex(v)}");
                output.WriteLine($"{s.Name}: exact={s.IsExact}, worst |V'/s^3 - V| = {worst:E3}");
                Assert.True(worst < 1e-6, s.Name);
            }
        }

        [Fact]
        public void Trajectory_IsWrittenAsJsonArrays()
        {
            // the trajectory for a viewer: set G3_VOLUME_GRADIENT_TRAJECTORY to a file path to keep it
            Opt.Result r = Opt.GrowUnion(recordMeshes: true);
            var w = new StringWriter();
            Opt.WriteTrajectory(r, w);
            string keep = Environment.GetEnvironmentVariable("G3_VOLUME_GRADIENT_TRAJECTORY");
            if (string.IsNullOrEmpty(keep) == false)
                File.WriteAllText(keep, w.ToString());

            using (JsonDocument doc = JsonDocument.Parse(w.ToString())) {
                Assert.Equal("Converged", doc.RootElement.GetProperty("reason").GetString());
                JsonElement its = doc.RootElement.GetProperty("iterations");
                Assert.Equal(r.Iterations.Count, its.GetArrayLength());
                for (int i = 0; i < r.Iterations.Count; ++i) {
                    JsonElement e = its[i];
                    Assert.Equal(r.Iterations[i].Volume, e.GetProperty("volume").GetDouble());
                    double[] vs = e.GetProperty("vertices").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                    int[] ts = e.GetProperty("triangles").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    Assert.Equal(r.Iterations[i].Vertices, vs);
                    Assert.Equal(r.Iterations[i].Triangles, ts);
                    Assert.True(ts.Length % 3 == 0 && vs.Length % 3 == 0 && ts.Length > 0);
                    Assert.All(ts, t => Assert.InRange(t, 0, vs.Length / 3 - 1));
                }
            }
            // the last iteration's arrays are the final result mesh
            Opt.ToArrays(r.Final, out double[] fv, out int[] ft);
            Assert.Equal(fv, r.Iterations.Last().Vertices);
            Assert.Equal(ft, r.Iterations.Last().Triangles);
            Assert.Equal(r.Final.TriangleCount * 3, ft.Length);
        }
    }
}
