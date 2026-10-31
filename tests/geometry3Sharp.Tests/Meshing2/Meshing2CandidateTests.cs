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
    /// What CLAUDE.md asks of an operation, for whatever is registered as the M1 candidate: results checked by the oracle,
    /// invariance over Similarity.All(), reuse, and determinism. These tests stay when session 2 replaces the prototype.
    /// </summary>
    public class Meshing2CandidateTests
    {
        static IMeshing2Candidate Candidate => CandidateRegistry.For(Milestone.M1);

        public static IEnumerable<object[]> CiCases() => M1Cases.Ci().Select(c => new object[] { c.Id });

        static Mesh2Case Find(string id) => M1Cases.Ci().First(c => c.Id == id);

        [Theory]
        [MemberData(nameof(CiCases))]
        public void CiCase_PassesTheOracle(string id)
        {
            var c = Find(id);
            var output = Candidate.Run(c.Input);
            var report = Oracle.Check(c.Input, output, c.Expect);
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.GlobalDelaunayChecked || output.Triangles.Length == 0, "the global pass must run on the CI subset");
        }

        /// <summary>
        /// A similarity of the plane from the 3D similarity of the harness: its scale and the (x, y) part of its translation, and
        /// the in-plane rotation its matrix applies to the x axis. Rounding happens as in any transformed input.
        /// </summary>
        static Mesh2Input Apply(Similarity s, Mesh2Input input)
        {
            var e = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(e.x * e.x + e.y * e.y), cos = e.x / norm, sin = e.y / norm;
            var result = new Mesh2Input { X = new double[input.Count], Y = new double[input.Count] };
            for (int i = 0; i < input.Count; ++i) {
                result.X[i] = s.Scale * (cos * input.X[i] - sin * input.Y[i]) + s.Translation.x;
                result.Y[i] = s.Scale * (sin * input.X[i] + cos * input.Y[i]) + s.Translation.y;
            }
            return result;
        }

        static IEnumerable<int[]> Canonical(Mesh2Output o) => o.Triangles.Select(t => {
            int k = t[0] < t[1] && t[0] < t[2] ? 0 : t[1] < t[2] ? 1 : 2;
            return new[] { t[k], t[(k + 1) % 3], t[(k + 2) % 3] };
        }).OrderBy(t => t[0]).ThenBy(t => t[1]).ThenBy(t => t[2]);

        public static IEnumerable<object[]> InvarianceInputs()
        {
            // a set in general position with room to spare (integers on 20 bits), and a tied one
            foreach (var id in new[] { "m1/uniform-int-32", "m1/grid-8x8" })
                foreach (var s in Similarity.All()) yield return new object[] { id, s.Name };
        }

        [Theory]
        [MemberData(nameof(InvarianceInputs))]
        public void Similarity_KeepsTheResult(string id, string similarity)
        {
            var s = Similarity.All().First(x => x.Name == similarity);
            var c = Find(id);
            var transformed = Apply(s, c.Input);
            var baseOutput = Candidate.Run(c.Input);
            var output = Candidate.Run(transformed);
            var report = Oracle.Check(transformed, output, new OracleExpect { Milestone = Milestone.M1 });
            Assert.True(report.Ok, report.ToString());
            if (s.IsExact) {
                // 2^k: the same result bit for bit, ties included
                Assert.Equal(Canonical(baseOutput).Select(t => string.Join(" ", t)), Canonical(output).Select(t => string.Join(" ", t)));
                Assert.Equal(Oracle.Check(c.Input, baseOutput, c.Expect).MinSin2, report.MinSin2);
                return;
            }
            if (id == "m1/uniform-int-32") // general position with room to spare: rounding of the transform changes nothing
                Assert.Equal(Canonical(baseOutput).Select(t => string.Join(" ", t)), Canonical(output).Select(t => string.Join(" ", t)));
        }

        [Fact]
        public void Reuse_OneInstanceTwice_EqualsAFreshInstance()
        {
            var instance = Candidate;
            var a = Find("m1/uniform-int-128").Input;
            var b = Find("m1/grid-8x8").Input;
            instance.Run(a);
            var second = instance.Run(b);
            var fresh = Candidate.Run(b);
            Assert.Equal(Fingerprint.Identity(fresh), Fingerprint.Identity(second));
            Assert.Equal(Fingerprint.Triangles(fresh), Fingerprint.Triangles(second));
        }

        [Theory]
        [InlineData("m1/uniform-int-128")]
        [InlineData("m1/grid-8x8")]
        [InlineData("m1/lattice-circle-1105")]
        public void RepeatedRuns_AreBitIdentical(string id)
        {
            var input = Find(id).Input;
            var first = Candidate.Run(input);
            for (int i = 0; i < 3; ++i) {
                var again = Candidate.Run(input);
                Assert.Equal(first.Triangles.SelectMany(t => t), again.Triangles.SelectMany(t => t));
                Assert.Equal(first.VX, again.VX);
                Assert.Equal(first.VY, again.VY);
            }
        }

        [Fact]
        public void ParallelRuns_EqualSerial()
        {
            var cases = M1Cases.Ci().Where(c => c.Input.Count <= 128).ToList();
            var serial = cases.Select(c => Fingerprint.Identity(Candidate.Run(c.Input))).ToList();
            var parallel = new string[cases.Count];
            for (int round = 0; round < 3; ++round) {
                Parallel.For(0, cases.Count, i => parallel[i] = Fingerprint.Identity(Candidate.Run(cases[i].Input)));
                Assert.Equal(serial, parallel);
            }
        }

        [Fact]
        public void Results_AreIndependentOfTheInput()
        {
            var input = Generators.UniformInt(32, 20, 1);
            var before = Fingerprint.Triangles(Candidate.Run(input));
            var output = Candidate.Run(input);
            double[] xCopy = (double[])input.X.Clone();
            for (int i = 0; i < input.Count; ++i) input.X[i] += 1;
            Assert.Equal(before, Fingerprint.Triangles(output));
            Assert.Equal(xCopy, output.VX);
            // and a second mesh changes nothing of the first
            Candidate.Run(input);
            Assert.Equal(before, Fingerprint.Triangles(output));
        }

        [Theory]
        [InlineData(double.NaN, 0.0)]
        [InlineData(0.0, double.NaN)]
        [InlineData(double.PositiveInfinity, 0.0)]
        [InlineData(0.0, double.NegativeInfinity)]
        [InlineData(1.0000001e70, 0.0)]
        [InlineData(0.0, -1.0000001e70)]
        public void InvalidCoordinates_AreArgumentErrors(double x, double y)
        {
            var input = Generators.Literal(0, 0, 1, 0, x, y, 0, 1);
            var error = Assert.Throws<ArgumentException>(() => Candidate.Run(input));
            Assert.Contains("points[2]", error.Message);
        }

        [Fact]
        public void TheLimitOfTheRange_IsAccepted()
        {
            var input = Generators.Literal(-1e70, -1e70, 1e70, -1e70, 0, 1e70, 0, 0);
            var report = Oracle.Check(input, Candidate.Run(input), new OracleExpect { Milestone = Milestone.M1 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void GeneralPosition_MatchesTheDefinition()
        {
            // a third opinion besides the oracle and the differential: the triangles of the definition, by enumeration
            for (ulong seed = 100; seed < 130; ++seed) {
                var input = Generators.UniformInt(24, 16, seed);
                var report = Oracle.Check(input, Candidate.Run(input), new OracleExpect { Milestone = Milestone.M1 });
                Assert.True(report.Ok, report.ToString());
                if (!report.Unique) continue;
                var byDefinition = new Mesh2Output { VX = input.X, VY = input.Y, Triangles = SelfCheck.DelaunayByDefinition(input) };
                Assert.Equal(Fingerprint.Triangles(byDefinition), Fingerprint.Triangles(Candidate.Run(input)));
            }
        }

        [Fact]
        public void Fuzz_ManySmallInputsWithTiesAndDuplicates_PassTheOracle()
        {
            Parallel.For(0, 300, i => {
                var r = new SplitMix64((ulong)i * 7919 + 1);
                int n = 3 + (int)r.Bits(5);
                int bits = 1 + (int)r.Bits(3) % 6;
                var input = Generators.UniformInt(n, bits, (ulong)i + 1000);
                if (i % 4 == 1) input = Generators.NudgeOdd(input);
                if (i % 4 == 2) input = Generators.Transform(input, -1040);
                if (i % 4 == 3) input = Generators.Transform(input, 200);
                var report = Oracle.Check(input, Candidate.Run(input), new OracleExpect { Milestone = Milestone.M1 });
                Assert.True(report.Ok, "seed " + i + ": " + report);
            });
        }
    }

    /// <summary>The classification of the differential, with hand-made sides: which outcome counts as a failure decides what CI sees.</summary>
    public class Meshing2DifferentialTests
    {
        sealed class Fixed : IMeshing2Candidate
        {
            public Func<Mesh2Input, Mesh2Output> F;
            public string Name => "fixed";
            public Mesh2Output Run(Mesh2Input input) => F(input);
        }

        static Mesh2Case Case(params double[] xy) => new Mesh2Case { Id = "c", Milestone = Milestone.M1, Input = Generators.Literal(xy), Expect = new OracleExpect { Milestone = Milestone.M1 } };

        static Fixed Mesh(params int[][] triangles) => new Fixed { F = i => new Mesh2Output {
            Status = Mesh2Status.Success, VX = i.X, VY = i.Y, InputToVertex = Enumerable.Range(0, i.Count).ToArray(), Triangles = triangles } };

        static readonly int[][] A = { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, B = { new[] { 0, 1, 3 }, new[] { 1, 2, 3 } };

        [Fact]
        public void SameUniqueResult_IsSameTriangles_WhateverTheNumbering()
        {
            var c = Case(0, 0, 8, 0, 9, 8, 0, 7);
            var theirs = new Fixed { F = i => new Mesh2Output {
                Status = Mesh2Status.Success, VX = new[] { i.X[2], i.X[0], i.X[1], i.X[3] }, VY = new[] { i.Y[2], i.Y[0], i.Y[1], i.Y[3] },
                Triangles = new[] { new[] { 1, 2, 3 }, new[] { 0, 3, 2 } }, IdentityRules = false } };
            var result = Differential.Compare(c, Mesh(new[] { 0, 1, 3 }, new[] { 1, 2, 3 }), theirs);
            Assert.Equal(DiffOutcome.SameTriangles, result.Outcome);
            Assert.False(result.IsFailure);
        }

        [Fact]
        public void Ties_MayDiffer()
        {
            var c = Case(0, 0, 8, 0, 8, 8, 0, 8);
            var result = Differential.Compare(c, Mesh(A), Mesh(B));
            Assert.Equal(DiffOutcome.BothValid, result.Outcome);
            Assert.False(result.IsFailure);
        }

        [Fact]
        public void InvalidCandidate_IsAFailure_InvalidReferenceIsNot()
        {
            var c = Case(0, 0, 8, 0, 9, 8, 0, 7);
            var good = Mesh(new[] { 0, 1, 3 }, new[] { 1, 2, 3 });
            var bad = Mesh(new[] { 0, 1, 2 }, new[] { 0, 2, 3 }); // the other diagonal, not Delaunay here
            var ours = Differential.Compare(c, bad, good);
            Assert.Equal(DiffOutcome.CandidateInvalid, ours.Outcome);
            Assert.True(ours.IsFailure);
            var theirs = Differential.Compare(c, good, bad);
            Assert.Equal(DiffOutcome.ReferenceInvalid, theirs.Outcome);
            Assert.False(theirs.IsFailure);
        }

        [Fact]
        public void ThrowingReference_IsNoFailure_ButIsReported()
        {
            var c = Case(0, 0, 8, 0, 9, 8, 0, 7);
            var result = Differential.Compare(c, Mesh(new[] { 0, 1, 3 }, new[] { 1, 2, 3 }), new Fixed { F = i => throw new InvalidOperationException("no") });
            Assert.Equal(DiffOutcome.ReferenceThrew, result.Outcome);
            Assert.False(result.IsFailure);
            Assert.Contains("no", result.Detail);
        }

        // "Differ" needs two results that both pass the oracle on an input with a unique answer, which a sound oracle excludes,
        // so it cannot be provoked from here. It is the alarm for a hole in the oracle, and it must count as a failure.
        [Fact]
        public void Differ_CountsAsAFailure() => Assert.True(new DiffResult { Outcome = DiffOutcome.Differ }.IsFailure);
    }

    /// <summary>F-51, F-54, F-58: the library's own predicates against the oracle's. The exact code is judged, not changed.</summary>
    public class Meshing2PredicateTests
    {
        static (int orient, int circle, int gOrient, int gCircle) One(SplitMix64 r, int exponent, bool utm)
        {
            double s = Math.Pow(2, exponent), ox = utm ? 632117.25 : 0, oy = utm ? 5712043.5 : 0;
            double D() => (r.Bits(20) - (1L << 19)) * s;
            var v = new[] { D() + ox, D() + oy, D() + ox, D() + oy, D() + ox, D() + oy, D() + ox, D() + oy };
            for (int k = 0; k < 8; ++k) if (r.Bits(1) == 1) v[k] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v[k]) + r.Bits(2));
            Pt a = Pt.FromDoubles(v[0], v[1]), b = Pt.FromDoubles(v[2], v[3]), c = Pt.FromDoubles(v[4], v[5]), d = Pt.FromDoubles(v[6], v[7]);
            int orient = Exact.Orient(a, b, c);
            // the library's in-circle reverses its sign for clockwise input, as the oracle's does: both are the plain determinant
            return (orient, Exact.InCircle(a, b, c, d),
                ExactPredicates.Orient2d(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5])),
                ExactPredicates.InCircle(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]), new Vector2d(v[6], v[7])));
        }

        [Fact]
        public void PredicateSigns_MatchTheOracle()
        {
            int[] exponents = { -1040, -530, -300, -40, 0, 100, 200 };
            for (int i = 0; i < 7000; ++i) {
                var r = new SplitMix64((ulong)i + 5);
                var x = One(r, exponents[i % exponents.Length], false);
                Assert.Equal(x.orient, x.gOrient);
                Assert.Equal(x.circle, x.gCircle);
            }
        }

        // four points on a lattice circle, each moved a few units in the last place
        static (int, int) NearCocircular(ulong seed, double ox, double oy, int exponent)
        {
            var r = new SplitMix64(seed);
            var circle = Generators.LatticeCircle(1105, false);
            double s = Math.Pow(2, exponent);
            var v = new double[8];
            for (int k = 0; k < 4; ++k) {
                int i = (int)(r.Next() % (ulong)circle.Count);
                v[2 * k] = circle.X[i] * s + ox; v[2 * k + 1] = circle.Y[i] * s + oy;
                if (r.Bits(1) == 1) v[2 * k] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v[2 * k]) + r.Bits(2));
                if (r.Bits(1) == 1) v[2 * k + 1] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v[2 * k + 1]) + r.Bits(2));
            }
            return (Exact.InCircle(Pt.FromDoubles(v[0], v[1]), Pt.FromDoubles(v[2], v[3]), Pt.FromDoubles(v[4], v[5]), Pt.FromDoubles(v[6], v[7])),
                ExactPredicates.InCircle(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]), new Vector2d(v[6], v[7])));
        }

        [Fact]
        public void NearCocircular_MatchesTheOracle()
        {
            int zero = 0, plus = 0, minus = 0;
            for (ulong seed = 0; seed < 20000; ++seed) {
                bool utm = seed % 2 == 0;
                var (exact, library) = NearCocircular(seed, utm ? 632117.25 : 0, utm ? 5712043.5 : 0, utm ? -10 : -1000);
                Assert.Equal(exact, library);
                if (exact == 0) ++zero; else if (exact > 0) ++plus; else ++minus;
            }
            // the input must really be near-degenerate on all sides, or this test proves little
            Assert.True(zero > 100 && plus > 100 && minus > 100, zero + " " + plus + " " + minus);
        }

        [Fact]
        public void Predicates_InParallel_MatchTheOracle()
        {
            Parallel.For(0, 4, thread => {
                for (ulong seed = 0; seed < 2500; ++seed) {
                    var (exact, library) = NearCocircular(seed * 4 + (ulong)thread, 632117.25, 5712043.5, -10);
                    Assert.Equal(exact, library);
                }
            });
        }
    }
}
