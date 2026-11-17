using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The opt-in triangle target of the alpha wrap fallback (MeshRobustRepair.WrapTargetTriangles / WrapTargetFactor /
    /// WrapTargetMaxDistance, result AlphaWrapTarget / AlphaWrapTargetReached / AlphaWrapTargetLimit): the simplified wrap is reduced
    /// further with a certified deviation per collapse, the guarantees of the wrap kept; off (the default) changes nothing.
    /// </summary>
    public class AlphaWrapTargetTests
    {
        // a closed sphere of 260 triangles flattened onto z = 0 (zero volume: the solidify fails, the wrap is used)
        static DMesh3 Flat()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            foreach (int v in m.VertexIndices().ToList()) { Vector3d p = m.GetVertex(v); m.SetVertex(v, new Vector3d(p.x, p.y, 0)); }
            return m;
        }

        static MeshRobustRepair Repair(DMesh3 input, Action<MeshRobustRepair> set = null)
        {
            var r = new MeshRobustRepair(input) { SolidifyResolution = 16, WrapRelativeAlpha = 40, WrapRelativeOffset = 400 };
            set?.Invoke(r);
            Assert.True(r.Apply(), r.ResultReason);
            Assert.True(r.UsedAlphaWrap, r.ResultReason);
            return r;
        }

        // shapes: a closed solid, a thin plate, the zero-volume surface, an open sheet, a closed shell around a closed ball (the cavity
        // behind a hole), and the plates of AlphaWrapNoContactTests (convex creases of the input)
        public static IEnumerable<object[]> Shapes() => new[] { "sphere", "plate", "flat", "sheet", "neck", "plates" }.Select(s => new object[] { s });

        static DMesh3 Shape(string name)
        {
            DMesh3 Open(DMesh3 m, Func<Vector3d, bool> drop)
            {
                foreach (int t in m.TriangleIndices().ToList()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    m.GetTriVertices(t, ref a, ref b, ref c);
                    if (drop((a + b + c) / 3)) m.RemoveTriangle(t);
                }
                return m;
            }
            switch (name) {
                case "sphere": return Defects.Sphere(Vector3d.Zero, 1, 12);
                case "plate": return new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.01)), NoSharedVertices = false }.Generate().MakeDMesh();
                case "flat": {
                    DMesh3 f = Defects.Sphere(Vector3d.Zero, 1, 12);
                    foreach (int v in f.VertexIndices().ToList()) { Vector3d p = f.GetVertex(v); f.SetVertex(v, new Vector3d(p.x, p.y, 0)); }
                    return f;
                }
                case "sheet": return Open(Defects.Sphere(Vector3d.Zero, 1, 12), c => c.z > 0.1);
                case "neck": {
                    DMesh3 shell = Open(Defects.Sphere(Vector3d.Zero, 1, 12), c => c.z > 0.93);
                    MeshEditor.Append(shell, Defects.Sphere(Vector3d.Zero, 0.6, 12));
                    return shell;
                }
                default: return MeshRobustRepairTests.ThinPlates(6);
            }
        }

        // the wrap, its simplification and the reduction to the target, as MeshRobustRepair runs them (the stages without the solidify
        // stage, which does not take these shapes to the wrap)
        sealed class Run
        {
            public DMesh3 Input, Raw, Simplified, Reduced;
            public double Offset;
            public int Target;
        }

        static Run Reduce(DMesh3 input, double factor, double distance, int relativeAlpha = 40)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = relativeAlpha, RelativeOffset = 10 * relativeAlpha };
            Assert.True(w.Compute(), w.Error);
            var run = new Run() { Input = input, Raw = w.Result, Offset = w.UsedOffset, Target = (int)Math.Ceiling(factor * input.TriangleCount) };
            run.Simplified = MeshRobustRepair.SimplifyWithin(run.Raw, 0.5 * run.Offset, 0.5 * run.Offset, true, () => false, out int _,
                MeshRobustRepair.WrapSimplifyMinQuality, true, MeshRobustRepair.WrapSimplifySlabs, 0, null, input);
            Assert.NotNull(run.Simplified);
            run.Reduced = MeshRobustRepair.ReduceToTarget(run.Simplified, run.Raw, run.Target, distance * run.Offset, true, () => false, input);
            return run;
        }

        static double OneSided(DMesh3 a, DMesh3 b, double tolerance)
        {
            var hd = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tolerance };
            Assert.True(hd.Compute());
            return hd.UpperBound;
        }

        // the guarantees of a result: closed, manifold, no self-intersection, no contact with the input, every input vertex inside
        // (a point of the input outside the result would mean the surface passed the input), certified distance to the unreduced wrap
        static void AssertGuarantees(DMesh3 result, Run run, double distance)
        {
            Assert.Equal("", MeshRobustRepair.NotCleanReason(result, true));
            Assert.Equal(0, AlphaWrapNoContactTests.Crossings(result, run.Input));
            var solid = MeshRepairMetrics.SolidReference.Unoriented(result);
            foreach (int v in run.Input.VertexIndices())
                Assert.True(solid.Inside(run.Input.GetVertex(v)), $"input vertex {v} is not inside the result");
            Assert.True(OneSided(result, run.Raw, 0.01 * run.Offset) <= distance * run.Offset, "farther from the wrap than the certified distance");
        }

        // off is the default, and bit for bit the result of master (hash computed there): a zero or negative target does nothing
        [Fact]
        public void Default_Off_BitIdenticalToMaster()
        {
            var d = new MeshRobustRepair(Flat());
            Assert.Equal(0, d.WrapTargetTriangles);
            Assert.Equal(0.0, d.WrapTargetFactor);
            Assert.Equal(0.5, d.WrapTargetMaxDistance);
            foreach (var set in new Action<MeshRobustRepair>[] { null, r => { r.WrapTargetTriangles = -5; r.WrapTargetFactor = -1; r.WrapTargetMaxDistance = 3; } }) {
                MeshRobustRepair r = Repair(Flat(), set);
                Assert.Equal(856, r.Mesh.TriangleCount);
                Assert.Equal("FEE932283B9EF9AD", AlphaWrapManifoldStepTests.Hash(r.Mesh));
                Assert.Equal(0, r.AlphaWrapTarget);
                Assert.Equal(0, r.AlphaWrapTargetReached);
                Assert.Equal("", r.AlphaWrapTargetLimit);
            }
        }

        // a target above the simplified wrap's size changes nothing (the target is an upper bound): the same mesh as without
        [Fact]
        public void TargetAboveTheSimplifiedWrap_ChangesNothing()
        {
            MeshRobustRepair r = Repair(Flat(), x => x.WrapTargetTriangles = 900);
            Assert.Equal(856, r.Mesh.TriangleCount);
            Assert.Equal("FEE932283B9EF9AD", AlphaWrapManifoldStepTests.Hash(r.Mesh));
            Assert.Equal(900, r.AlphaWrapTarget);
            Assert.Equal(856, r.AlphaWrapTargetReached);
            Assert.Equal("", r.AlphaWrapTargetLimit);
        }

        // the reduction reaches the target (the input's count) where it can, and the result keeps every guarantee: on each shape the
        // result has at most the target, or stops with fewer triangles than the simplified wrap and the count says why
        [Theory]
        [MemberData(nameof(Shapes))]
        public void Reduces_TowardTheInputsCount_GuaranteesKept(string shape)
        {
            Run run = Reduce(Shape(shape), 1.0, 0.5);
            Assert.NotNull(run.Reduced);
            AssertGuarantees(run.Reduced, run, 0.5);
            Assert.True(run.Reduced.TriangleCount <= Math.Max(run.Target, run.Simplified.TriangleCount));
            // (the simplification stops at its quadric error: a collapse that the certified deviation allows is left on every shape
            // whose simplified wrap is above the target)
            if (run.Simplified.TriangleCount > run.Target)
                Assert.True(run.Reduced.TriangleCount < run.Simplified.TriangleCount, $"{shape}: {run.Reduced.TriangleCount} of {run.Simplified.TriangleCount}");
        }

        // a closed solid at the input's count: reached (the first lever), with half an offset of certified distance
        [Fact]
        public void ClosedSolid_ReachesTheInputsCount()
        {
            Run run = Reduce(Shape("sphere"), 1.0, 0.5);
            Assert.True(run.Simplified.TriangleCount > run.Target, $"{run.Simplified.TriangleCount} vs {run.Target}");
            Assert.True(run.Reduced.TriangleCount <= run.Target, $"{run.Reduced.TriangleCount} vs {run.Target}");
            AssertGuarantees(run.Reduced, run, 0.5);
        }

        // a larger certified distance reaches a smaller count (a closed solid at half the input's count: out of reach at half an offset,
        // reached at two), and the result is certified within that distance, not the smaller one
        [Fact]
        public void LargerDistance_ReachesMore_AndIsCertifiedWithinIt()
        {
            Run near = Reduce(Shape("sphere"), 0.5, 0.5), far = Reduce(Shape("sphere"), 0.5, 2.0);
            Assert.True(near.Reduced.TriangleCount > near.Target, $"{near.Reduced.TriangleCount} vs {near.Target}");
            Assert.True(far.Reduced.TriangleCount <= far.Target, $"{far.Reduced.TriangleCount} vs {far.Target}");
            Assert.True(far.Reduced.TriangleCount < near.Reduced.TriangleCount);
            AssertGuarantees(near.Reduced, near, 0.5);
            AssertGuarantees(far.Reduced, far, 2.0);
            Assert.True(OneSided(far.Reduced, far.Raw, 0.01 * far.Offset) > 0.5 * far.Offset, "the larger distance is used");
        }

        // MeshRobustRepair: the target and its limit are reported; the count reached is the result's; a larger distance reaches fewer
        [Fact]
        public void RobustRepair_ReportsTargetReachedAndLimit()
        {
            MeshRobustRepair near = Repair(Flat(), x => x.WrapTargetFactor = 1);
            Assert.Equal(Flat().TriangleCount, near.AlphaWrapTarget);
            Assert.Equal(near.Mesh.TriangleCount, near.AlphaWrapTargetReached);
            Assert.True(near.AlphaWrapTargetReached < 856 && near.AlphaWrapTargetReached > near.AlphaWrapTarget, $"{near.AlphaWrapTargetReached}");
            Assert.Equal(MeshRobustRepair.TargetLimitDistance, near.AlphaWrapTargetLimit);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(near.Mesh, true));
            Assert.Equal(0, AlphaWrapNoContactTests.Crossings(near.Mesh, Flat()));
            MeshRobustRepair far = Repair(Flat(), x => { x.WrapTargetFactor = 1; x.WrapTargetMaxDistance = 2; });
            Assert.True(far.AlphaWrapTargetReached < near.AlphaWrapTargetReached, $"{far.AlphaWrapTargetReached} vs {near.AlphaWrapTargetReached}");
            Assert.Equal(MeshRobustRepair.TargetLimitDistance, far.AlphaWrapTargetLimit);
        }

        // WrapTargetTriangles wins over WrapTargetFactor; the factor is of the input's count, rounded up
        [Fact]
        public void AbsoluteTarget_WinsOverTheFactor_FactorIsOfTheInputCount()
        {
            int n = Flat().TriangleCount;
            Assert.Equal(n, Repair(Flat(), x => x.WrapTargetFactor = 1).AlphaWrapTarget);
            Assert.Equal(n / 2 + 1, Repair(Flat(), x => x.WrapTargetFactor = 0.5 + 1e-9).AlphaWrapTarget);
            Assert.Equal(700, Repair(Flat(), x => { x.WrapTargetTriangles = 700; x.WrapTargetFactor = 1; }).AlphaWrapTarget);
        }

        // the result is certified as a whole, not only per collapse: a distance that the simplified wrap itself does not meet gives no
        // result (null; MeshRobustRepair keeps the simplified wrap and says TargetLimitNotCertified)
        [Fact]
        public void ReduceToTarget_NotCertified_GivesNull()
        {
            Run run = Reduce(Shape("sphere"), 1.0, 0.5);
            Assert.NotNull(run.Reduced);
            Assert.Null(MeshRobustRepair.ReduceToTarget(run.Simplified, run.Raw, run.Target, 0.01 * run.Offset, true, () => false, run.Input));
            Assert.Null(MeshRobustRepair.ReduceToTarget(run.Simplified, run.Raw, run.Target, 0.5 * run.Offset, true, () => true, run.Input));
        }

        // a distance below the simplification's own half offset counts as half an offset (the result is the same mesh)
        [Fact]
        public void DistanceBelowHalfAnOffset_CountsAsHalfAnOffset()
        {
            string h = AlphaWrapManifoldStepTests.Hash(Repair(Flat(), x => x.WrapTargetFactor = 1).Mesh);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Repair(Flat(), x => { x.WrapTargetFactor = 1; x.WrapTargetMaxDistance = 0.1; }).Mesh));
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Repair(Flat(), x => { x.WrapTargetFactor = 1; x.WrapTargetMaxDistance = -2; }).Mesh));
        }

        // the quality pass runs after the target: a pass that splits caps (a low threshold, for the test) can take the result above a
        // target that the reduction reached, and the limit says so
        [Fact]
        public void QualityPassAboveTheTarget_LimitSaysSo()
        {
            MeshRobustRepair plain = Repair(Flat(), x => x.WrapTargetTriangles = 700);
            Assert.Equal("", plain.AlphaWrapTargetLimit);
            Assert.True(plain.AlphaWrapTargetReached <= 700);
            MeshRobustRepair split = Repair(Flat(), x => {
                x.WrapTargetTriangles = plain.AlphaWrapTargetReached; x.AlphaWrapQualityPass = true;
                x.ConfigureWrapQuality = p => p.SplitCapsAboveDegrees = 60;
            });
            Assert.True(split.AlphaWrapQualityImproved);
            Assert.Equal(split.Mesh.TriangleCount, split.AlphaWrapTargetReached);
            Assert.True(split.AlphaWrapTargetReached > split.AlphaWrapTarget, $"{split.AlphaWrapTargetReached} vs {split.AlphaWrapTarget}");
            Assert.Equal(MeshRobustRepair.TargetLimitQualityPass, split.AlphaWrapTargetLimit);
        }

        // a target above the raw wrap without the simplification is reached: no limit is reported
        [Fact]
        public void WithoutSimplification_RawWrapWithinTarget_NoLimit()
        {
            MeshRobustRepair r = Repair(Flat(), x => { x.SimplifyAlphaWrap = false; x.WrapTargetTriangles = 1000000; });
            Assert.Equal("", r.AlphaWrapTargetLimit);
            Assert.Equal(r.Mesh.TriangleCount, r.AlphaWrapTargetReached);
        }

        // a NaN distance counts as half an offset, like a smaller one
        [Fact]
        public void NaNDistance_CountsAsHalfAnOffset()
        {
            string h = AlphaWrapManifoldStepTests.Hash(Repair(Flat(), x => x.WrapTargetFactor = 1).Mesh);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Repair(Flat(), x => { x.WrapTargetFactor = 1; x.WrapTargetMaxDistance = double.NaN; }).Mesh));
        }

        // a target without the simplification: nothing to continue, the limit says so, the raw wrap is the result
        [Fact]
        public void WithoutSimplification_LimitSaysSo()
        {
            MeshRobustRepair r = Repair(Flat(), x => { x.SimplifyAlphaWrap = false; x.WrapTargetFactor = 1; });
            Assert.Equal(MeshRobustRepair.TargetLimitNotSimplified, r.AlphaWrapTargetLimit);
            Assert.Equal(r.AlphaWrapTriangles, r.AlphaWrapTargetReached);
            Assert.Equal(r.Mesh.TriangleCount, r.AlphaWrapTargetReached);
        }

        // reuse: Apply() twice on one instance gives the same result, and the target fields follow the options of each call
        [Fact]
        public void Reuse_SameResult_FieldsFollowTheOptions()
        {
            var r = new MeshRobustRepair(Flat()) { SolidifyResolution = 16, WrapRelativeAlpha = 40, WrapRelativeOffset = 400, WrapTargetFactor = 1 };
            Assert.True(r.Apply());
            string h = AlphaWrapManifoldStepTests.Hash(r.Mesh);
            int reached = r.AlphaWrapTargetReached;
            r.Mesh = Flat();
            Assert.True(r.Apply());
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(r.Mesh));
            Assert.Equal(reached, r.AlphaWrapTargetReached);
            r.WrapTargetFactor = 0; r.Mesh = Flat();
            Assert.True(r.Apply());
            Assert.Equal("FEE932283B9EF9AD", AlphaWrapManifoldStepTests.Hash(r.Mesh));
            Assert.Equal(0, r.AlphaWrapTarget);
            Assert.Equal(0, r.AlphaWrapTargetReached);
            Assert.Equal("", r.AlphaWrapTargetLimit);
        }

        // invariance over Similarity.All(): the 2^k scales give the same reduction bit for bit (no absolute constants, serial reducer);
        // every other frame gives a result with the guarantees
        [Fact]
        public void Invariance_AllSimilarities()
        {
            DMesh3 input = Shape("sphere");
            Run r0 = Reduce(input, 0.8, 0.5, 25);
            foreach (Similarity s in Similarity.All()) {
                Run r = Reduce(Invariance.Transformed(input, s), 0.8, 0.5, 25);
                Assert.NotNull(r.Reduced);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Reduced, true));
                Assert.Equal(0, AlphaWrapNoContactTests.Crossings(r.Reduced, r.Input));
                if (s.IsExact) {
                    Assert.Equal(r0.Reduced.TriangleCount, r.Reduced.TriangleCount);
                    Assert.True(r0.Reduced.VertexIndices().All(v => r.Reduced.GetVertex(v) == s.Apply(r0.Reduced.GetVertex(v))), s.Name);
                }
            }
        }

        // repeated runs are the same mesh (the reduction is serial)
        [Fact]
        public void Deterministic_RepeatedRuns()
        {
            string h = AlphaWrapManifoldStepTests.Hash(Reduce(Shape("sphere"), 0.8, 0.5, 25).Reduced);
            for (int i = 0; i < 2; ++i)
                Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Reduce(Shape("sphere"), 0.8, 0.5, 25).Reduced));
        }

        // a cancel during the reduction to the target (after the simplification has ended): Apply() returns false, no result
        [Fact]
        public void Cancel_DuringTheTargetReduction()
        {
            var r = new MeshRobustRepair(Flat()) { SolidifyResolution = 16, WrapRelativeAlpha = 40, WrapRelativeOffset = 400, WrapTargetFactor = 1 };
            r.CancelF = () => r.AlphaWrapTarget > 0;
            Assert.False(r.Apply());
        }
    }
}
