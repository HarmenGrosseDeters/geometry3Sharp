using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Op = geometry3Sharp.Tests.RobustnessSweepTests.Op;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Relation C1 / C2 of docs/design/METAMORPHIC_RELATIONS.md (owner 2026-11-13): the work of an operation does not depend on the
    /// frame of its input. C1 compares the size of the result (vertices, triangles; for the mesher the Steiner points it had to add)
    /// on a transformed copy with the original: equal under the exact transformations (the 2^k scales, which are bit for bit), at most
    /// a factor of 2 more otherwise (a rotation or a scale by 1e-3 may change ties, never the order of magnitude). C2 (nightly)
    /// compares the wall time: at most 20 times and 50 ms more. Cases: #664 (a mesher that ran to its Steiner limit of 400 000 vertices
    /// on a rotated copy), #712 and #636 (a repair that took 5 to 52 s on 4 to 20 vertices at the magnitudes 2^-100 .. 2^97), which a
    /// bound on the output size alone does not see, hence C2.
    /// </summary>
    public class CostInvarianceTests
    {
        readonly ITestOutputHelper output;
        public CostInvarianceTests(ITestOutputHelper output) { this.output = output; }

        public const double Factor = 2;
        public const double TimeFactor = 20;
        public static readonly TimeSpan TimeSlack = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// "" if the cost of the transformed run is within the bound of the original's: equal if exact, else at most a factor of 2 more.
        /// One-sided: a transformation that rounds the input (a UTM translation of features below the coordinates' last bit) may turn the
        /// problem into a much easier one (the fan below: 5046 vertices, 32 after the translation); only a growth is a cost explosion.
        /// </summary>
        public static string Bound(string what, long original, long transformed, bool exact)
        {
            if (exact) return original == transformed ? "" : $"{what}: {original} vs {transformed} (must be equal under an exact transformation)";
            double hi = Math.Ceiling(original * Factor);
            return transformed <= hi ? "" : $"{what}: {original} vs {transformed} (more than a factor of {Factor})";
        }

        /// <summary> "" if the time of the transformed run is at most TimeFactor times the original's plus the slack </summary>
        public static string TimeBound(string what, TimeSpan original, TimeSpan transformed)
        {
            TimeSpan limit = TimeSpan.FromTicks((long)(original.Ticks * TimeFactor)) + TimeSlack;
            return transformed <= limit ? "" : $"{what}: {original.TotalMilliseconds:F0} ms vs {transformed.TotalMilliseconds:F0} ms (limit {limit.TotalMilliseconds:F0} ms)";
        }

        // ---------------------------------------------------------------- the harness itself

        [Fact]
        public void Bound_AcceptsAFactorOfTwo_RejectsMore_AndDemandsEqualityWhenExact()
        {
            Assert.Equal("", Bound("n", 100, 200, false));
            Assert.Equal("", Bound("n", 100, 50, false));   // fewer is no explosion
            Assert.NotEqual("", Bound("n", 100, 201, false));
            Assert.NotEqual("", Bound("n", 100, 100000, false));
            Assert.Equal("", Bound("n", 100, 100, true));
            Assert.NotEqual("", Bound("n", 100, 101, true));
            Assert.Equal("", Bound("n", 0, 0, false));
        }

        [Fact]
        public void TimeBound_AcceptsNoise_AndRejectsAnExplosion()
        {
            Assert.Equal("", TimeBound("t", TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(60)));
            Assert.Equal("", TimeBound("t", TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(2000)));
            Assert.NotEqual("", TimeBound("t", TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(2100)));
            Assert.NotEqual("", TimeBound("t", TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(5)));
        }

        // ---------------------------------------------------------------- C1: the 2D quality mesher (#664)

        /// <summary> the near-contact fan of #542 (as QualityMesh2NearTJunctionTests.Fan): the unit square, A along y = 0.5, four B_i at a small angle to A </summary>
        static Mesh2Input Fan(int k, double scale, int steinerLimit)
        {
            var x = new List<double> { 0, 1, 1, 0 };
            var y = new List<double> { 0, 0, 1, 1 };
            var input = new Mesh2Input { MinAngleDeg = 30, ConstrainedQuality = true, SteinerLimit = steinerLimit };
            int Add(double px, double py) { x.Add(px); y.Add(py); return x.Count - 1; }
            input.Segments.Add(new SegmentInput(Add(0.125, 0.5), Add(0.875, 0.5)));
            for (int i = 0; i < 4; ++i) input.Segments.Add(new SegmentInput(Add(0.25 + 0.1 * i, 0.5), Add(0.85, 0.5 + (i + 1) * Math.Pow(2, -k) * scale)));
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            return input;
        }

        static Mesh2Input Moved(Mesh2Input input, Similarity s)
        {
            var m = new Mesh2Input {
                X = (double[])input.X.Clone(), Y = (double[])input.Y.Clone(), MinAngleDeg = input.MinAngleDeg, MaxArea = input.MaxArea, SteinerLimit = input.SteinerLimit,
                SnapTolerance = input.SnapTolerance, ResolveCrossings = input.ResolveCrossings, Conforming = input.Conforming, ConstrainedQuality = input.ConstrainedQuality,
            };
            if (input.SnapTolerance != 0) m.SnapTolerance = s.Scale * input.SnapTolerance;
            for (int i = 0; i < m.X.Length; ++i) { Vector2d q = s.Apply2d(new Vector2d(input.X[i], input.Y[i])); m.X[i] = q.x; m.Y[i] = q.y; }
            m.Segments.AddRange(input.Segments);
            m.Rings.AddRange(input.Rings);
            return m;
        }

        public static IEnumerable<object[]> MesherCases() =>
            Enumerable.Range(0, 3).SelectMany(c => Similarity.All2D().Where(s => s.Name != "identity").Select(s => new object[] { c, s.Name }));

        /// <summary> case 0 and 1: the fan at k = 26 and 28; case 2: a star polygon of the fuzz </summary>
        static Mesh2Input Case(int c) => c == 0 ? Fan(26, 1.13, 20000) : c == 1 ? Fan(28, 1.13, 20000) : StarPolygonFuzz.Make(4);

        [Theory, MemberData(nameof(MesherCases))]
        public void QualityMesh2_VertexCount_DoesNotDependOnTheFrame(int c, string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            Mesh2Input input = Case(c);
            var mesher = new QualityMesh2Candidate();
            Mesh2Output a = mesher.Run(input), b = mesher.Run(Moved(input, s));
            Assert.True(a.Status == Mesh2Status.Success, "the original: " + a.Status + " " + a.FailureKind);
            Assert.True(b.Status == Mesh2Status.Success, $"{similarity}: {b.Status} {b.FailureKind}");
            Assert.False(b.SteinerLimitReached, $"{similarity}: the Steiner limit was reached ({b.VertexCount} vertices, the original {a.VertexCount})");
            Assert.Equal("", Bound("vertices", a.VertexCount, b.VertexCount, s.IsExact));
            Assert.Equal("", Bound("triangles", a.Triangles.Length, b.Triangles.Length, s.IsExact));
        }

        // ---------------------------------------------------------------- C1: ExactMeshBoolean

        public static IEnumerable<object[]> BooleanCases() =>
            ExactBooleanMetamorphicTests.TetFamilies.Keys.SelectMany(f => Similarity.All().Where(s => s.Name != "identity").Select(s => new object[] { f, s.Name }));

        [Theory, MemberData(nameof(BooleanCases))]
        public void ExactMeshBoolean_ResultSize_DoesNotDependOnTheFrame(string family, string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies[family];
            DMesh3 a = ta.Mesh(Vector3d.Zero), b = tb.Mesh(Vector3d.Zero);
            DMesh3 am = Invariance.Transformed(a, s), bm = Invariance.Transformed(b, s);
            foreach (Ops op in BooleanOracle.AllOps) {
                DMesh3 r = BooleanOracle.Run(a, b, op, out string e1), rm = BooleanOracle.Run(am, bm, op, out string e2);
                Assert.True(e1 == "" && e2 == "", $"{op}: {e1} {e2}");
                Assert.Equal("", Bound($"{family} {op} triangles", r.TriangleCount, rm.TriangleCount, false));
                if (s.IsExact) Assert.Equal(r.TriangleCount, rm.TriangleCount);
            }
        }

        // ---------------------------------------------------------------- C2 (nightly): wall time of every sweep operation

        public static IEnumerable<object[]> TimeCases() =>
            RobustnessSweepTests.Ops.Where(o => !SymmetryInvarianceTests.Excluded.ContainsKey(o.Name)).Select(o => new object[] { o.Name });

        /// <summary> the best of several runs of run on a fresh input; only run is timed, not the construction or the transformation of the input </summary>
        static TimeSpan Fastest(Func<DMesh3> input, Func<DMesh3, RobustnessSweepTests.Sig> run, int repeats)
        {
            TimeSpan best = TimeSpan.MaxValue;
            for (int i = 0; i < repeats; ++i) {
                DMesh3 m = input();
                var watch = Stopwatch.StartNew();
                run(m);
                if (watch.Elapsed < best) best = watch.Elapsed;
            }
            return best;
        }

        /// <summary>
        /// The time of an operation on a copy under every similarity of the sweep (and two extreme power-of-two scales) against the
        /// original's: the best of 3 runs each, after a warm-up; at most 20 times and 50 ms more. Nightly only: it is a wall-clock check,
        /// so it is loose and runs where a busy machine does not decide a PR.
        /// </summary>
        [Theory, MemberData(nameof(TimeCases)), Trait("Category", "Nightly")]
        public void SweepOperation_Time_DoesNotDependOnTheFrame(string opName)
        {
            Op op = RobustnessSweepTests.Ops.First(o => o.Name == opName);
            Similarity identity = Similarity.Identity;
            op.Run(op.Input(), identity, op.Param);   // warm-up (JIT)
            TimeSpan baseline = Fastest(() => op.Input(), x => op.Run(x, identity, op.Param), 3);
            var failures = new List<string>();
            foreach (Similarity s in RobustnessSweepTests.Similarities().Where(x => x.Name != "identity")) {
                if (RobustnessSweepTests.KnownFailures.ContainsKey(opName + "|" + s.Name)) continue;   // a documented defect of the sweep (R..)
                if (op.PlanarInput && (s.Translation.z != 0 || s.IsRotated)) continue;
                double param = double.IsNaN(op.Param) ? double.NaN : s.ApplyLength(op.Param);
                TimeSpan t = Fastest(() => Invariance.Transformed(op.Input(), s), x => op.Run(x, s, param), 3);
                string r = TimeBound($"{opName} under {s.Name}", baseline, t);
                if (r != "") failures.Add(r);
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
    }
}
