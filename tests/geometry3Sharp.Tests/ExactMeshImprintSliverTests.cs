using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshImprint at near contact (#1034, docs/design/IMPRINT_SLIVER_CLEANUP.md): a cutter that is the target shifted by a few
    /// ulps of its largest coordinate. The result is clean per NotCleanReason, closed, keeps the target's vertices bit for bit and
    /// carries the curve (every curve edge is an edge of Result). Through the public API only, so that the tests run on the base.
    /// </summary>
    public class ExactMeshImprintSliverTests
    {
        // the box (1, 0.7, 0.4) rotated by 29 degrees about z and 17 about x, as doubles: the rotation would use Math.Sin / Cos, which
        // differ in the last bit between runtimes (docs/design/DETERMINISM.md C1), so the corners are given as the literals that
        // Matrix3d.AxisAngleD gives on net10 (the box whose 1-ulp shifts left slivers before #1034)
        static readonly double[] BoxCorners = {
            -0.6067795312896138, -0.9680060882596397, -0.58718209569113,
            1.1424598829891777, 0.0016131522330344594, -0.58718209569113,
            0.4933838391796905, 1.1725773320679964, -0.1778617090792985,
            -1.255855575099101, 0.20295809157532227, -0.1778617090792985,
            -0.4933838391796905, -1.1725773320679964, 0.1778617090792985,
            1.255855575099101, -0.20295809157532227, 0.1778617090792985,
            0.6067795312896138, 0.9680060882596397, 0.58718209569113,
            -1.1424598829891777, -0.0016131522330344594, 0.58718209569113 };

        // a box rotated off the axes, and a capped cone frustum (fan caps: many target edges meet at the rim and the centres)
        internal static DMesh3 Shape(string kind)
        {
            if (kind == "box") {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)) }.Generate().MakeDMesh();
                Assert.Equal(8, m.MaxVertexID);
                foreach (int v in m.VertexIndices().ToArray())
                    m.SetVertex(v, new Vector3d(BoxCorners[3 * v], BoxCorners[3 * v + 1], BoxCorners[3 * v + 2]));
                return m;
            }
            return Snapped(new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.6f, Height = 2f, Slices = 12 }.Generate().MakeDMesh());
        }

        // the generator uses Math.Sin / Cos, which differ in the last bit between runtimes (docs/design/DETERMINISM.md C1): the
        // vertices are snapped to a grid of 2^-24, exactly, so that the input is the same on net48 and net10
        internal static DMesh3 Snapped(DMesh3 m)
        {
            const double grid = 1.0 / (1 << 24);
            foreach (int v in m.VertexIndices().ToArray()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, new Vector3d(Math.Round(p.x / grid) * grid, Math.Round(p.y / grid) * grid, Math.Round(p.z / grid) * grid));
            }
            return m;
        }

        static double NextUp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);

        // the mesh moved along the axis by ulps units in the last place of its largest coordinate on that axis (exact: checked per vertex)
        internal static DMesh3 Shifted(DMesh3 m, int axis, int ulps)
        {
            double maxAbs = m.Vertices().Max(p => Math.Abs(p[axis]));
            double step = ulps * (NextUp(maxAbs) - maxAbs);
            var b = new DMesh3(m);
            foreach (int v in b.VertexIndices().ToArray()) {
                Vector3d p = b.GetVertex(v);
                double x = p[axis] + step;
                Assert.Equal(step, x - p[axis]);       // the shift is exact
                p[axis] = x;
                b.SetVertex(v, p);
            }
            return b;
        }

        internal static ExactMeshImprint Run(DMesh3 target, DMesh3 cutter)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
            Assert.True(imprint.Compute(), imprint.Error);
            Assert.Equal(ExactMeshImprint.ImprintStatus.Success, imprint.Status);
            return imprint;
        }

        // clean, closed, the target's vertices kept, the curve carried
        internal static void AssertCleanAndCarried(DMesh3 target, ExactMeshImprint imprint)
        {
            DMesh3 r = imprint.Result;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.True(imprint.RoundingClean);
            Assert.True(r.IsClosed());
            Assert.True(r.Vertices().All(p => p.IsFinite));
            foreach (int v in target.VertexIndices()) {
                Assert.True(r.IsVertex(v));
                Assert.Equal(target.GetVertex(v), r.GetVertex(v));
            }
            Assert.NotEmpty(imprint.CurveEdges);
            Assert.Equal(imprint.CurveEdges.Count, imprint.Counts["curve_edges"]);
            foreach (long k in imprint.CurveEdges)
                Assert.True(r.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0, "a curve edge is not an edge of Result");
            Assert.True(imprint.Counts["new_vertices"] > 0);
        }

        // (kind, axis, ulps): each one left slivers before #1034 in both directions
        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "box", 2, 1 };
            yield return new object[] { "box", 2, -1 };
            yield return new object[] { "box", 2, 3 };
            yield return new object[] { "cylinder", 0, 1 };
            yield return new object[] { "cylinder", 0, -4 };
            yield return new object[] { "cylinder", 2, 1 };
            yield return new object[] { "cylinder", 2, -2 };
            yield return new object[] { "cylinder", 2, 5 };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void UlpShiftedCopy_BothDirections_AreCleanClosedAndCarryTheCurve(string kind, int axis, int ulps)
        {
            DMesh3 a = Shape(kind), b = Shifted(a, axis, ulps);
            AssertCleanAndCarried(a, Run(a, b));
            AssertCleanAndCarried(b, Run(b, a));
        }

        public static IEnumerable<object[]> Similarities() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Invariance_UlpShiftedCopy_IsCleanUnderEverySimilarity_AndBitIdenticalUnderPowersOfTwo(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (string kind in new[] { "box", "cylinder" }) {
                int axis = kind == "box" ? 2 : 0, ulps = 1;
                DMesh3 a = Invariance.Transformed(Shape(kind), s);
                DMesh3 b = Shifted(a, axis, ulps);
                ExactMeshImprint imprint = Run(a, b);
                AssertCleanAndCarried(a, imprint);
                // a scaling by 2^k is exact (the ulp scales with it): the same result bit for bit, scaled
                if (s.IsRotated == false && s.Translation == Vector3d.Zero && Math.Log(s.Scale, 2) % 1 == 0) {
                    DMesh3 a0 = Shape(kind);
                    ExactMeshImprint i0 = Run(a0, Shifted(a0, axis, ulps));
                    Assert.Equal(i0.Result.MaxVertexID, imprint.Result.MaxVertexID);
                    foreach (int v in i0.Result.VertexIndices())
                        Assert.Equal(s.Apply(i0.Result.GetVertex(v)), imprint.Result.GetVertex(v));
                    Assert.Equal(i0.Result.MaxTriangleID, imprint.Result.MaxTriangleID);
                    foreach (int t in i0.Result.TriangleIndices())
                        Assert.Equal(i0.Result.GetTriangle(t), imprint.Result.GetTriangle(t));
                    Assert.Equal(i0.CurveEdges.OrderBy(x => x), imprint.CurveEdges.OrderBy(x => x));
                    Assert.Equal(i0.SourceTriangle, imprint.SourceTriangle);
                }
            }
        }

        [Fact]
        public void FarFromTheOrigin_Reuse_AndSerialEqualsParallel()
        {
            DMesh3 a = Invariance.Transformed(Shape("box"), Similarity.Utm), b = Shifted(a, 2, 1);
            var imprint = new ExactMeshImprint() { Target = a, Cutter = b };
            Assert.True(imprint.Compute(), imprint.Error);
            AssertCleanAndCarried(a, imprint);
            Assert.True(imprint.Rounding.FollowStageRan);
            DMesh3 first = imprint.Result;
            Assert.True(imprint.Compute(), imprint.Error);
            Assert.True(Same(first, imprint.Result));
            gParallel.SerialOnThisThread = true;
            try {
                ExactMeshImprint serial = Run(a, b);
                Assert.True(Same(first, serial.Result));
                Assert.Equal(imprint.Counts["sliver_cleanup_neutral_contractions"], serial.Counts["sliver_cleanup_neutral_contractions"]);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        // the evaluation limit (#1489): cylinder along x, 6 ulps, in the translated frames, the cases in which the final stage used up the limit of 10000 plus
        // 50 per defect with 23 slivers left (the stage's position search proposes 26 x 8 moves per corner). The cut is the one that failed: the copy shifted by
        // -6 cut into the original, and the original cut into the copy shifted by +6. They need 25 evaluations more than the limit of 50 gave (15 826 against
        // 15 800) and nothing else: no neutral contraction is accepted in them (NeutralContractions 0), and they are clean without neutral contractions too
        public static IEnumerable<object[]> LimitCases()
        {
            foreach (string frame in new[] { "utm", "utm-negated", "utm-mixed" }) {
                yield return new object[] { frame, -6 };
                yield return new object[] { frame, 6 };
            }
        }

        static ExactMeshImprint RunLimitCase(DMesh3 a, int ulps)
        {
            DMesh3 b = Shifted(a, 0, ulps);
            return ulps < 0 ? Run(a, b) : Run(b, a);
        }

        [Theory]
        [MemberData(nameof(LimitCases))]
        public void StageEvaluationLimit_CylinderAlongX_SixUlps_IsCleanClosedManifoldAndKeepsTheVolume(string frame, int ulps)
        {
            DMesh3 a = Invariance.Transformed(Shape("cylinder"), ExactMeshImprintFarFrameTests.FarFrame(frame));
            ExactMeshImprint imprint = RunLimitCase(a, ulps);
            DMesh3 target = ulps < 0 ? a : Shifted(a, 0, ulps);
            AssertCleanAndCarried(target, imprint);
            ResultChecks.AssertClosed(imprint.Result, "result");
            Assert.False(imprint.Rounding.EvaluationLimitReached);
            Assert.True(imprint.Rounding.FollowStageRan);
            Assert.Equal(0, imprint.Rounding.RemainingDegenerate + imprint.Rounding.RemainingIntersections);
            Assert.Equal(ResultChecks.Volume(target), ResultChecks.Volume(imprint.Result), 6);      // (the volume sum carries the rounding of the coordinates far from the origin)
            Assert.Equal(0, imprint.Rounding.NeutralContractions);
            // the limit alone cleans them: the same without the neutral contractions
            DMesh3 b = Shifted(a, 0, ulps);
            var without = ulps < 0 ? new ExactMeshImprint() { Target = a, Cutter = b, NeutralContractions = false } : new ExactMeshImprint() { Target = b, Cutter = a, NeutralContractions = false };
            Assert.True(without.Compute(), without.Error);
            Assert.True(without.RoundingClean);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(without.Result));
            // the limit that the stage was given: 10000 plus 400 per initial defect
            Rounding_ExpectLimit(imprint, 400);
        }

        static void Rounding_ExpectLimit(ExactMeshImprint imprint, int perDefect)
        {
            MeshRoundingRepair r = imprint.Rounding;
            Assert.Equal(10000L + perDefect * (long)(r.InitialIntersections + r.InitialDegenerate + r.InitialFalseContacts), r.MaxEvaluations);
        }

        [Fact]
        public void StageEvaluationLimit_OnlyTheStageGetsIt_ACleanRunAndTheBooleanKeepTheLimitOf50()
        {
            // a run that is clean without the stage never raises it
            DMesh3 a = Shape("cylinder"), b = Shifted(a, 1, 1);
            ExactMeshImprint clean = Run(a, b);
            Assert.False(clean.Rounding.FollowStageRan);
            Rounding_ExpectLimit(clean, 50);
            // a run that needs the stage gets it from the stage's start; the setting is the imprint's, off it the limit is the repair's own
            DMesh3 far = Invariance.Transformed(a, Similarity.Utm);
            var off = new ExactMeshImprint() { Target = far, Cutter = Shifted(far, 2, 1), StageEvaluationsPerDefect = 0 };
            Assert.True(off.Compute(), off.Error);
            Assert.True(off.Rounding.FollowStageRan);      // (so that the case cannot become vacuous: the stage runs and still has the limit of 50)
            Rounding_ExpectLimit(off, 50);
            // the Boolean (two overlapping spheres: initial defects) and the repair's default
            DMesh3 s = ExactMeshImprintTests.Sphere(Vector3d.Zero, 1, 12), t = ExactMeshImprintTests.Sphere(new Vector3d(0.6, 0.2, 0.1), 0.8, 12);
            var boolean = new ExactMeshBoolean(s, t, ExactMeshBoolean.Operations.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(0, boolean.Rounding.StageMaxEvaluationsPerDefect);
            Assert.Equal(50, boolean.Rounding.MaxEvaluationsPerDefect);
            Assert.Equal(10000L + 50 * (long)(boolean.Rounding.InitialIntersections + boolean.Rounding.InitialDegenerate + boolean.Rounding.InitialFalseContacts), boolean.Rounding.MaxEvaluations);
            Assert.Equal(50, new MeshRoundingRepair(new DMesh3()).MaxEvaluationsPerDefect);
        }

        [Fact]
        public void StageEvaluationLimit_ARunThatReachesTheLimitBeforeTheStage_KeepsTheLimitOf50()
        {
            // a 48-slice cylinder (snapped like Shape), translated, along x, -30 ulps: 2318 initial defects, the 125 900 evaluations of 10000 + 50 per defect are used up by
            // the passes and the stage never starts. Nothing may raise the limit then (a stage that does not run has no limit of its own); it stops on evaluations,
            // not on the pair budget
            DMesh3 a0 = Snapped(new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.6f, Height = 2f, Slices = 48 }.Generate().MakeDMesh());
            DMesh3 a = Invariance.Transformed(a0, Similarity.Utm), b = Shifted(a, 0, -30);
            var imprint = new ExactMeshImprint() { Target = a, Cutter = b };
            Assert.True(imprint.Compute(), imprint.Error);
            MeshRoundingRepair r = imprint.Rounding;
            Assert.True(r.EvaluationLimitReached);
            Assert.False(r.StoppedByPairTests);
            Assert.False(r.FollowStageRan);
            Assert.Equal(0, r.NeutralContractions);
            Rounding_ExpectLimit(imprint, 50);
        }

        [Fact]
        public void StageEvaluationLimit_Reuse_SerialEqualsParallel_AndPowersOfTwoGiveTheSameBitsScaled()
        {
            DMesh3 a = Invariance.Transformed(Shape("cylinder"), ExactMeshImprintFarFrameTests.FarFrame("utm"));
            ExactMeshImprint first = RunLimitCase(a, -6);
            var again = new ExactMeshImprint() { Target = a, Cutter = Shifted(a, 0, -6) };
            Assert.True(again.Compute(), again.Error);
            Assert.True(again.Compute(), again.Error);
            Assert.True(Same(first.Result, again.Result));
            gParallel.SerialOnThisThread = true;
            try { Assert.True(Same(first.Result, RunLimitCase(a, -6).Result)); } finally { gParallel.SerialOnThisThread = false; }
            // a scale by 2^k of the frame (translation included, exact) scales the result: the same triangles, the vertices times 2^k
            foreach (int k in new[] { -3, 4 }) {
                double scale = Math.Pow(2, k);
                var sim = new Similarity("utm * 2^" + k, scale, Matrix3d.Identity, Similarity.Utm.Translation * scale);
                DMesh3 sa = Invariance.Transformed(Shape("cylinder"), sim);
                ExactMeshImprint scaled = RunLimitCase(sa, -6);
                AssertCleanAndCarried(sa, scaled);
                Assert.Equal(first.Result.MaxVertexID, scaled.Result.MaxVertexID);
                foreach (int v in first.Result.VertexIndices())
                    Assert.Equal(first.Result.GetVertex(v) * scale, scaled.Result.GetVertex(v));
                foreach (int tri in first.Result.TriangleIndices())
                    Assert.Equal(first.Result.GetTriangle(tri), scaled.Result.GetTriangle(tri));
            }
        }

        static bool Same(DMesh3 a, DMesh3 b) =>
            a.MaxVertexID == b.MaxVertexID && a.MaxTriangleID == b.MaxTriangleID
            && a.VertexIndices().All(v => b.IsVertex(v) && a.GetVertex(v) == b.GetVertex(v))
            && a.TriangleIndices().All(t => b.IsTriangle(t) && a.GetTriangle(t) == b.GetTriangle(t));

        [Fact]
        public void Reuse_ComputeTwiceOnOneInstance_SameCleanResult()
        {
            DMesh3 a = Shape("cylinder"), b = Shifted(a, 2, 1);
            var imprint = new ExactMeshImprint() { Target = a, Cutter = b };
            Assert.True(imprint.Compute(), imprint.Error);
            AssertCleanAndCarried(a, imprint);
            DMesh3 first = imprint.Result;
            var edges = new HashSet<long>(imprint.CurveEdges);
            var sources = (int[])imprint.SourceTriangle.Clone();
            Assert.True(imprint.Compute(), imprint.Error);
            AssertCleanAndCarried(a, imprint);
            Assert.True(Same(first, imprint.Result));
            Assert.Equal(edges, imprint.CurveEdges);
            Assert.Equal(sources, imprint.SourceTriangle);
        }

        [Fact]
        public void Determinism_SerialEqualsParallel_AndRepeatedRuns()
        {
            DMesh3 a = Shape("cylinder"), b = Shifted(a, 0, 1);
            ExactMeshImprint reference = Run(a, b);
            AssertCleanAndCarried(a, reference);
            for (int i = 0; i < 3; ++i) {
                ExactMeshImprint again = Run(a, b);
                Assert.True(Same(reference.Result, again.Result));
                Assert.Equal(reference.CurveEdges, again.CurveEdges);
                Assert.Equal(reference.SourceTriangle, again.SourceTriangle);
            }
            gParallel.SerialOnThisThread = true;
            try {
                ExactMeshImprint serial = Run(a, b);
                Assert.True(Same(reference.Result, serial.Result));
                Assert.Equal(reference.CurveEdges, serial.CurveEdges);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }
    }
}
