using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Prepare / Solve (factorize once, solve many) and the optional initial positions of ArapMeshDeformer.
    public partial class ArapMeshDeformerTests
    {
        static DMesh3 FramedCylinder() { return Moved(Cylinder(16, 7), Place(Frame, FrameShift)); }

        static Dictionary<int, Vector3d> Bend(DMesh3 mesh, double degrees)
        {
            Vector3d center = Frame * new Vector3d(0, 0, 1.8) + FrameShift;
            return RingHandles(mesh, 16, 7, BendMotion(degrees, center), p => p);
        }

        static void AssertSameResult(DMesh3 mesh, ArapMeshDeformer.ResultInfo expected, ArapMeshDeformer.ResultInfo actual)
        {
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Iterations, actual.Iterations);
            Assert.Equal(expected.Residual, actual.Residual);
            Assert.Equal(expected.Energy, actual.Energy);
            foreach (int v in mesh.VertexIndices()) Assert.Equal(expected.Mesh.GetVertex(v), actual.Mesh.GetVertex(v));
        }

        [Fact]
        public void PrepareSolve_EqualsAFreshCompute_BitForBit_ForEveryHandlePosition()
        {
            DMesh3 mesh = FramedCylinder();
            var d = new ArapMeshDeformer(mesh);
            var preparation = d.Prepare(Bend(mesh, 20).Keys);
            Assert.Equal(ArapMeshDeformer.Status.Success, preparation.Status);
            Assert.Equal(32, preparation.HandleCount);
            // Several handle positions of the same handle ids on one preparation, in a different order than a drag would take.
            foreach (double degrees in new[] { 20.0, 55.0, -35.0, 5.0, 20.0 }) {
                var handles = Bend(mesh, degrees);
                var solved = d.Solve(preparation, handles);
                var fresh = new ArapMeshDeformer(mesh).Compute(handles);
                AssertSuccessPath(mesh, solved, handles);
                AssertSameResult(mesh, fresh, solved);
            }
        }

        [Fact]
        public void PrepareSolve_ReportsTheStatusOfCompute_WhenTheHandlePositionsFail()
        {
            var mesh = Patch();
            var ids = new[] { 0, 1, 3 };
            var d = new ArapMeshDeformer(mesh);
            var preparation = d.Prepare(ids);
            Assert.Equal(ArapMeshDeformer.Status.Success, preparation.Status);
            var huge = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = new Vector3d(0, 1e160, 0) };
            var solved = d.Solve(preparation, huge);
            Assert.Equal(ArapMeshDeformer.Status.NumericalRange, solved.Status);
            Assert.Null(solved.Mesh);
            Assert.Equal(new ArapMeshDeformer(mesh).Compute(huge).Status, solved.Status);
            // The failure does not poison the preparation.
            var fine = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3) + new Vector3d(0, 0, 0.2) };
            AssertSameResult(mesh, new ArapMeshDeformer(mesh).Compute(fine), d.Solve(preparation, fine));
            var nonFinite = new Dictionary<int, Vector3d>(fine) { [3] = new Vector3d(double.NaN, 0, 0) };
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(preparation, nonFinite).Status);
        }

        [Fact]
        public void PrepareSolve_AllHandles_ReturnsTheTargets()
        {
            var mesh = Patch();
            var d = new ArapMeshDeformer(mesh);
            var preparation = d.Prepare(new[] { 0, 1, 2, 3 });
            Assert.Equal(ArapMeshDeformer.Status.Success, preparation.Status);
            var targets = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) targets[v] = mesh.GetVertex(v) + new Vector3d(v, 1, 2);
            var solved = d.Solve(preparation, targets);
            Assert.Equal(ArapMeshDeformer.Status.Success, solved.Status);
            foreach (var t in targets) Assert.Equal(t.Value, solved.Mesh.GetVertex(t.Key));
            AssertSameResult(mesh, new ArapMeshDeformer(mesh).Compute(targets), solved);
        }

        [Fact]
        public void Prepare_ReportsInputFailuresAtOnce_AndSolveRefusesAMismatch()
        {
            DMesh3 mesh = FramedCylinder();
            var d = new ArapMeshDeformer(mesh);
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Prepare(null).Status);
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Prepare(new[] { 0, 99999 }).Status);
            Assert.Equal(ArapMeshDeformer.Status.UnanchoredComponent, d.Prepare(new int[0]).Status);
            var bend = Bend(mesh, 20);
            var preparation = d.Prepare(bend.Keys);
            Assert.Null(d.Solve(null, bend).Mesh);
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(null, bend).Status);
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(d.Prepare(new int[0]), bend).Status);
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(preparation, null).Status);
            // Other handle ids than those prepared (one fewer, one swapped, one more) are refused.
            var fewer = new Dictionary<int, Vector3d>(bend); fewer.Remove(0);
            var swapped = new Dictionary<int, Vector3d>(bend); swapped.Remove(0); swapped[20] = mesh.GetVertex(20);
            var more = new Dictionary<int, Vector3d>(bend) { [20] = mesh.GetVertex(20) };
            foreach (var other in new[] { fewer, swapped, more }) {
                var refused = d.Solve(preparation, other);
                Assert.Equal(ArapMeshDeformer.Status.InvalidInput, refused.Status);
                Assert.Null(refused.Mesh);
            }
            Assert.Equal(ArapMeshDeformer.Status.Success, d.Solve(preparation, bend).Status);
            // A preparation made for another mesh is refused, and so is one whose mesh was edited or whose options are invalid.
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, new ArapMeshDeformer(new DMesh3(mesh)).Solve(preparation, bend).Status);
            d.Options.MaxIterations = 0;
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(preparation, bend).Status);
            d.Options.MaxIterations = 50;
            mesh.SetVertex(40, mesh.GetVertex(40) + new Vector3d(0.01, 0, 0));
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(preparation, bend).Status);
            Assert.Equal(ArapMeshDeformer.Status.Success, d.Solve(d.Prepare(bend.Keys), bend).Status);
        }

        [Fact]
        public void PrepareSolve_ChangedOptions_ApplyToTheSolve()
        {
            DMesh3 mesh = FramedCylinder();
            var d = new ArapMeshDeformer(mesh);
            var bend = Bend(mesh, 35);
            var preparation = d.Prepare(bend.Keys);
            foreach (var settings in new[] { new[] { 1, 0 }, new[] { 400, 0 }, new[] { 400, 5 }, new[] { 3, 5 } }) {
                d.Options.MaxIterations = settings[0]; d.Options.AccelerationDepth = settings[1];
                var fresh = new ArapMeshDeformer(mesh) { Options = { MaxIterations = settings[0], AccelerationDepth = settings[1] } }.Compute(bend);
                AssertSameResult(mesh, fresh, d.Solve(preparation, bend));
            }
        }

        [Fact]
        public void PrepareSolve_Invariance_OverSimilarityAll()
        {
            DMesh3 mesh = Cylinder(12, 6);
            var handles = RingHandles(mesh, 12, 6, BendMotion(30, new Vector3d(0, 0, 1.5)), p => p);
            var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = 400;
            var reference = d.Solve(d.Prepare(handles.Keys), handles);
            AssertSuccessPath(mesh, reference, handles);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 moved = Moved(mesh, s.Apply);
                var mapped = new Dictionary<int, Vector3d>();
                foreach (var h in handles) mapped.Add(h.Key, s.Apply(h.Value));
                var next = new ArapMeshDeformer(moved); next.Options.MaxIterations = 400;
                var preparation = next.Prepare(mapped.Keys);
                var output = next.Solve(preparation, mapped);
                AssertSuccessPath(moved, output, mapped);
                Assert.Equal(reference.Iterations, output.Iterations);
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d back = s.Inverse(output.Mesh.GetVertex(v));
                    if (s.IsExact) Assert.Equal(reference.Mesh.GetVertex(v), back);
                    else Assert.True(reference.Mesh.GetVertex(v).Distance(back) <= Math.Max(1e-6, 64 * s.Resolution(4)), s.Name);
                }
                if (s.IsExact) Assert.Equal(reference.Energy, output.Energy);
                else Assert.True(Math.Abs(reference.Energy - output.Energy) <= 1e-6 * reference.Energy, s.Name);
            }
        }

        [Fact]
        public void InitialPositions_Invariance_OverSimilarityAll()
        {
            DMesh3 mesh = Cylinder(12, 6);
            var handles = RingHandles(mesh, 12, 6, BendMotion(30, new Vector3d(0, 0, 1.5)), p => p);
            var start = RingHandles(mesh, 12, 6, BendMotion(15, new Vector3d(0, 0, 1.5)), p => p);
            foreach (int v in mesh.VertexIndices()) if (!start.ContainsKey(v)) start[v] = mesh.GetVertex(v) + new Vector3d(0.02, -0.01, 0.03 * v % 0.5);
            var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = 400;
            var reference = d.Compute(handles, start);
            AssertSuccessPath(mesh, reference, handles);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 moved = Moved(mesh, s.Apply);
                var mapped = new Dictionary<int, Vector3d>(); var mappedStart = new Dictionary<int, Vector3d>();
                foreach (var h in handles) mapped.Add(h.Key, s.Apply(h.Value));
                foreach (var q in start) mappedStart.Add(q.Key, s.Apply(q.Value));
                var next = new ArapMeshDeformer(moved); next.Options.MaxIterations = 400;
                var output = next.Compute(mapped, mappedStart);
                AssertSuccessPath(moved, output, mapped);
                if (s.IsExact) Assert.Equal(reference.Iterations, output.Iterations);
                else Assert.True(Math.Abs(reference.Iterations - output.Iterations) <= 3, s.Name);
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d back = s.Inverse(output.Mesh.GetVertex(v));
                    if (s.IsExact) Assert.Equal(reference.Mesh.GetVertex(v), back);
                    else Assert.True(reference.Mesh.GetVertex(v).Distance(back) <= Math.Max(1e-5, 64 * s.Resolution(4)), s.Name);
                }
            }
        }

        [Fact]
        public void PrepareSolve_ConcurrentSolvesOnOnePreparation_EqualTheSerialOnes()
        {
            DMesh3 mesh = FramedCylinder();
            var d = new ArapMeshDeformer(mesh);
            var preparation = d.Prepare(Bend(mesh, 20).Keys);
            var angles = new[] { 10.0, 25.0, 40.0, 55.0, 70.0, 85.0 };
            var serial = new ArapMeshDeformer.ResultInfo[angles.Length]; var parallel = new ArapMeshDeformer.ResultInfo[angles.Length];
            for (int i = 0; i < angles.Length; ++i) serial[i] = d.Solve(preparation, Bend(mesh, angles[i]));
            for (int round = 0; round < 3; ++round) {
                Parallel.For(0, angles.Length, i => parallel[i] = new ArapMeshDeformer(mesh).Solve(preparation, Bend(mesh, angles[i])));
                for (int i = 0; i < angles.Length; ++i) AssertSameResult(mesh, serial[i], parallel[i]);
            }
        }

        [Fact]
        public void InitialPositions_Null_IsTheDefaultStartBitForBit()
        {
            DMesh3 mesh = FramedCylinder();
            var handles = Bend(mesh, 35);
            AssertSameResult(mesh, new ArapMeshDeformer(mesh).Compute(handles), new ArapMeshDeformer(mesh).Compute(handles, null));
        }

        static Dictionary<int, Vector3d> Positions(DMesh3 mesh)
        {
            var map = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) map[v] = mesh.GetVertex(v);
            return map;
        }

        [Fact]
        public void InitialPositions_ConvergedResult_StartsAtTheMinimum_AndHandleEntriesAreIgnored()
        {
            DMesh3 mesh = FramedCylinder();
            var handles = Bend(mesh, 35);
            var d = new ArapMeshDeformer(mesh);
            d.Options.MaxIterations = 400;
            var cold = d.Compute(handles);
            AssertSuccessPath(mesh, cold, handles);
            Assert.True(cold.Iterations > 5);
            // The whole previous result, handle vertices included (their entries must not move the handles).
            var warm = d.Compute(handles, Positions(cold.Mesh));
            AssertSuccessPath(mesh, warm, handles);
            Assert.True(warm.Iterations <= 2, "a converged start needs " + warm.Iterations + " iterations");
            Assert.True(Math.Abs(warm.Energy - cold.Energy) <= 1e-9 * cold.Energy);
            foreach (int v in mesh.VertexIndices()) Assert.True(warm.Mesh.GetVertex(v).Distance(cold.Mesh.GetVertex(v)) < 1e-6);
            // Handle entries at arbitrary finite places change nothing.
            var junk = Positions(cold.Mesh);
            foreach (int h in handles.Keys) junk[h] = new Vector3d(1e3, -2e3, 3e3);
            AssertSameResult(mesh, warm, d.Compute(handles, junk));
            // Only the free vertices are needed.
            var onlyFree = Positions(cold.Mesh);
            foreach (int h in handles.Keys) onlyFree.Remove(h);
            AssertSameResult(mesh, warm, d.Compute(handles, onlyFree));
        }

        [Fact]
        public void InitialPositions_AreAStart_NotAConstraint_AndTheEnergyNeverIncreases()
        {
            DMesh3 mesh = FramedCylinder();
            var handles = Bend(mesh, 35);
            var start = Positions(mesh);   // the rest pose: a poor, but valid start
            var reference = new ArapMeshDeformer(mesh) { Options = { MaxIterations = 400 } }.Compute(handles, start);
            AssertSuccessPath(mesh, reference, handles);
            double previous = double.PositiveInfinity;
            for (int budget = 1; budget <= 30; ++budget) {
                var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = budget;
                var r = d.Compute(handles, start);
                Assert.NotNull(r.Mesh);
                Assert.True(r.Energy <= previous, "budget " + budget);
                previous = r.Energy;
            }
            // A different start that reaches the same minimum.
            var cold = new ArapMeshDeformer(mesh) { Options = { MaxIterations = 400 } }.Compute(handles);
            Assert.True(Math.Abs(reference.Energy - cold.Energy) <= 1e-6 * cold.Energy);
        }

        [Fact]
        public void InitialPositions_AreValidated()
        {
            DMesh3 mesh = FramedCylinder();
            var handles = Bend(mesh, 35);
            var good = Positions(mesh);
            var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = 400;
            Assert.Equal(ArapMeshDeformer.Status.Success, d.Compute(handles, good).Status);
            int free = -1;
            foreach (int v in mesh.VertexIndices()) if (!handles.ContainsKey(v)) { free = v; break; }
            var missing = new Dictionary<int, Vector3d>(good); missing.Remove(free);
            var unknown = new Dictionary<int, Vector3d>(good) { [123456] = Vector3d.Zero };
            var nan = new Dictionary<int, Vector3d>(good) { [free] = new Vector3d(0, double.NaN, 0) };
            var infinite = new Dictionary<int, Vector3d>(good) { [free] = new Vector3d(double.PositiveInfinity, 0, 0) };
            var nanOnHandle = new Dictionary<int, Vector3d>(good) { [0] = new Vector3d(double.NaN, 0, 0) };
            foreach (var bad in new[] { missing, unknown, nan, infinite, nanOnHandle, new Dictionary<int, Vector3d>() }) {
                var r = d.Compute(handles, bad);
                Assert.Equal(ArapMeshDeformer.Status.InvalidInput, r.Status);
                Assert.Null(r.Mesh);
                var p = d.Prepare(handles.Keys);
                Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Solve(p, handles, bad).Status);
            }
            // A start whose normalized coordinates overflow is a numerical range failure, not a crash.
            var far = new Dictionary<int, Vector3d>(good) { [free] = new Vector3d(1.7e308, 0, 0) };
            var farResult = d.Compute(handles, far);
            Assert.Contains(farResult.Status, new[] { ArapMeshDeformer.Status.NumericalRange, ArapMeshDeformer.Status.NonFiniteResult });
            Assert.Null(farResult.Mesh);
        }

        [Fact]
        public void InitialPositions_ForAllHandles_AreValidatedButNotNeeded()
        {
            var mesh = Patch();
            var targets = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) targets[v] = mesh.GetVertex(v) + new Vector3d(0, 0, 1);
            var d = new ArapMeshDeformer(mesh);
            Assert.Equal(ArapMeshDeformer.Status.Success, d.Compute(targets, new Dictionary<int, Vector3d>()).Status);
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, d.Compute(targets, new Dictionary<int, Vector3d> { [0] = new Vector3d(double.NaN, 0, 0) }).Status);
        }

        // The point of both features: a drag in small steps, each solved from the previous result on one factorization.
        // ARAP is nonconvex: the path of the drag and a cold solve can end in different local minima (here at 50 deg the
        // drag is lower, at 60 deg higher), so the test checks what holds: every step is a valid stationary result.
        [Fact]
        public void IncrementalDrag_OnOnePreparation_GivesStationaryValidSteps()
        {
            DMesh3 mesh = FramedCylinder();
            var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = 400;
            var preparation = d.Prepare(Bend(mesh, 10).Keys);
            Dictionary<int, Vector3d> previous = null;
            double previousEnergy = 0;
            for (int degrees = 10; degrees <= 60; degrees += 10) {
                var handles = Bend(mesh, degrees);
                var step = d.Solve(preparation, handles, previous);
                AssertSuccessPath(mesh, step, handles);
                Assert.True(step.Energy > previousEnergy, degrees + " deg");
                previousEnergy = step.Energy;
                previous = Positions(step.Mesh);
                // Restarting from the result is stationary.
                var again = d.Solve(preparation, handles, previous);
                Assert.True(again.Iterations <= 2, degrees + " deg: " + again.Iterations);
                Assert.True(Math.Abs(again.Energy - step.Energy) <= 1e-9 * step.Energy, degrees + " deg");
            }
        }
    }
}
