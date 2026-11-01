using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CsCheck;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Rep = geometry3Sharp.Tests.BooleanOracle.Representation;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Property-based tests of ExactMeshBoolean with CsCheck (Apache-2.0, net10.0 only; the file name ends in
    /// .CsCheck.cs, which the project compiles only there). Cases are printable descriptions (boxes or tetrahedra
    /// with integer corners, a 2^k scale, a dyadic translation, a representation change), built into fresh meshes
    /// per run and checked against the oracles of BooleanOracle; the fixed corpus is ExactBooleanMetamorphicTests.
    ///
    /// Campaign: Samples cases, each from its own seed derived from a fixed campaign seed (CsCheck's own sampling
    /// draws random seeds after the first one, so it is used for shrinking only). A failing seed is shrunk by
    /// CsCheck (Sample from that seed, ShrinkIterations tries, one thread) and reported with the original and the
    /// shrunk case and seed. Opt-in:
    ///   G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~ExactBooleanPropertyTests
    ///     a larger campaign (any number of samples per property, from the same campaign seeds);
    ///   G3_PROPERTY_CAMPAIGN="Boxes(general)" G3_PROPERTY_REPLAY=&lt;seed&gt; dotnet test ... --filter "FullyQualifiedName~ExactBooleanPropertyTests.Boxes"
    ///     one case of the named campaign, from a seed a failure reported (original or shrunk); the failure message
    ///     prints this command. Other campaigns run as usual.
    /// </summary>
    public class ExactBooleanPropertyTests
    {
        readonly ITestOutputHelper log;
        public ExactBooleanPropertyTests(ITestOutputHelper log) { this.log = log; }

        const int ShrinkIterations = 400;

        // ---------------------------------------------------------------- generators

        // a box with integer corners in [0, range]^3: small ranges give many shared planes, edges and vertices
        static Gen<IntBox> GenBox(int range) =>
            Gen.Select(GenInterval(range), GenInterval(range), GenInterval(range),
                (x, y, z) => new IntBox(x.lo, y.lo, z.lo, x.hi, y.hi, z.hi));

        static Gen<(int lo, int hi)> GenInterval(int range) =>
            Gen.Int[0, range - 1].SelectMany(lo => Gen.Int[1, range - lo].Select(ext => (lo, lo + ext)));

        // 2^k with |k| <= 8, and a translation on a 1/16 grid up to 2^12 (or none): exact world coordinates
        static readonly Gen<(int k, Vector3d t)> GenFrame =
            Gen.Select(Gen.Int[-8, 8], Gen.Bool, Gen.Int[-65536, 65536], Gen.Int[-65536, 65536], Gen.Int[-65536, 65536],
                (k, move, x, y, z) => (k, move ? new Vector3d(x / 16.0, y / 16.0, z / 16.0) : Vector3d.Zero));

        static Gen<BoxCase> GenCase(Gen<(IntBox a, IntBox b)> pair) =>
            Gen.Select(pair, GenFrame, (p, f) => new BoxCase(p.a, p.b, f.k, f.t));

        /// <summary> the box families of docs/design/BOOLEAN_PROPERTY_TESTS.md section 1, each its own campaign </summary>
        static Gen<(IntBox, IntBox)> GenFamily(string family)
        {
            switch (family) {
                case "general":
                    return Gen.Select(GenBox(4), GenBox(4));
                case "coplanar":
                    // B starts or ends on one of A's planes
                    return Gen.Select(GenBox(4), GenBox(4), Gen.Int[0, 2], Gen.Bool, (a, b, axis, atEnd) => (a, Snap(a, b, axis, atEnd)));
                case "touching":
                    // B starts where A ends on 1 to 3 axes (face, edge or vertex contact, or disjoint)
                    return Gen.Select(GenBox(3), GenBox(3), Gen.Int[1, 7], (a, b, axes) => (a, Touch(a, b, axes)));
                case "nested":
                    // B inside A, possibly on its walls or identical
                    return GenBox(5).SelectMany(a => Gen.Select(GenInterval(a.X1 - a.X0), GenInterval(a.Y1 - a.Y0), GenInterval(a.Z1 - a.Z0),
                        (x, y, z) => (a, new IntBox(a.X0 + x.lo, a.Y0 + y.lo, a.Z0 + z.lo, a.X0 + x.hi, a.Y0 + y.hi, a.Z0 + z.hi))));
                default: throw new ArgumentException(family);
            }
        }

        static IntBox Snap(IntBox a, IntBox b, int axis, bool atEnd)
        {
            int[] lo = { b.X0, b.Y0, b.Z0 }, hi = { b.X1, b.Y1, b.Z1 };
            int ext = hi[axis] - lo[axis];
            if (atEnd) { hi[axis] = a.Hi(axis); lo[axis] = hi[axis] - ext; }
            else { lo[axis] = a.Lo(axis); hi[axis] = lo[axis] + ext; }
            return new IntBox(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]);
        }

        static IntBox Touch(IntBox a, IntBox b, int axes)
        {
            int[] lo = { b.X0, b.Y0, b.Z0 }, hi = { b.X1, b.Y1, b.Z1 };
            for (int axis = 0; axis < 3; ++axis)
                if ((axes >> axis & 1) != 0) { int ext = hi[axis] - lo[axis]; lo[axis] = a.Hi(axis); hi[axis] = lo[axis] + ext; }
            return new IntBox(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]);
        }

        static Gen<Vector3i> GenPoint(int range) => Gen.Select(Gen.Int[0, range], Gen.Int[0, range], Gen.Int[0, range], (x, y, z) => new Vector3i(x, y, z));

        static Gen<IntTet> GenTet(Gen<Vector3i> point) =>
            Gen.Select(point, point, point, point).Where(p => IntTet.Det(p.Item1, p.Item2, p.Item3, p.Item4) != 0)
                .Select(p => new IntTet(p.Item1, p.Item2, p.Item3, p.Item4));

        /// <summary>
        /// Two tetrahedra in [0,6]^3; each corner of B is either free or one of A's corners (shared vertices, edges
        /// and faces), and sliver tetrahedra come up whenever four corners are nearly coplanar. Translated by 0 or by
        /// UTM-like coordinates (dyadic, exact).
        /// </summary>
        static readonly Gen<(IntTet a, IntTet b, Vector3d shift)> GenTets =
            GenTet(GenPoint(6)).SelectMany(a => {
                Vector3i[] corners = { a.P0, a.P1, a.P2, a.P3 };
                Gen<Vector3i> corner = Gen.Int[0, 7].SelectMany(k => k < 4 ? Gen.Const(corners[k]) : GenPoint(6));
                return Gen.Select(GenTet(corner), Gen.Bool, (b, far) => (a, b, far ? new Vector3d(632117.25, 5712043.5, 113.75) : Vector3d.Zero));
            });

        // ---------------------------------------------------------------- campaign

        static int Samples(int normal)
        {
            string env = Environment.GetEnvironmentVariable("G3_PROPERTY_SAMPLES");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : normal;
        }

        /// <summary>
        /// Runs the property on `samples` cases, case i from seed (campaign, i): reproducible, and independent of
        /// the other cases. property returns "" or the reason it fails. A failure is shrunk and reported.
        /// </summary>
        void Campaign<T>(string name, Gen<T> gen, Func<T, string> property, int samples, uint campaign)
        {
            void Check(T t) { string r = property(t); if (r != "") throw new Exception(r); }
            var watch = Stopwatch.StartNew();
            string replay = Environment.GetEnvironmentVariable("G3_PROPERTY_CAMPAIGN") == name ? Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") : null;
            string[] seeds = string.IsNullOrEmpty(replay)
                ? Enumerable.Range(0, Samples(samples)).Select(i => new PCG(campaign, (ulong)i).ToString()).ToArray()
                : new[] { replay };
            log.WriteLine($"{name}: CsCheck campaign {campaign}, {seeds.Length} samples");
            foreach (string seed in seeds) {
                string failure = null;
                T original = default(T);
                try { gen.Sample(t => { original = t; Check(t); }, seed: seed, iter: 1, threads: 1, print: t => t.ToString()); }
                catch (CsCheckException e) { failure = e.InnerException?.Message ?? e.Message; }
                if (failure == null) continue;
                string shrunk;
                try {
                    gen.Sample(Check, seed: seed, iter: ShrinkIterations, threads: 1, print: t => t.ToString());
                    shrunk = "(shrinking found no failure)";
                }
                catch (CsCheckException e) { shrunk = e.Message; }
                throw new Xunit.Sdk.XunitException($"{name} fails for seed {seed} (campaign {campaign}):\n  {original}\n  {failure}\n" +
                    $"Shrunk ({ShrinkIterations} tries, not necessarily minimal): {shrunk}\n" +
                    $"Replay: G3_PROPERTY_CAMPAIGN=\"{name}\" G3_PROPERTY_REPLAY=<seed> dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter \"FullyQualifiedName~ExactBooleanPropertyTests.{name.Split('(')[0]}\"");
            }
            log.WriteLine($"{name}: passed in {watch.ElapsedMilliseconds} ms");
        }

        // ---------------------------------------------------------------- properties

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, ref string error, bool safeRounding = true)
        {
            return Run(a, b, op, ref error, out _, safeRounding);
        }

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, ref string error, out double maxDrift, bool safeRounding = true)
        {
            maxDrift = 0;
            if (error != "") return null;
            DMesh3 r = BooleanOracle.Run(a, b, op, out string e, out maxDrift, safeRounding);
            if (e != "") error = op + ": " + e;
            return r;
        }

        static string First(params Func<string>[] checks)
        {
            foreach (Func<string> c in checks) { string r = c(); if (r != "") return r; }
            return "";
        }

        /// <summary>
        /// For boxes: each operation matches the analytic reference; vol(AuB) + vol(AnB) = vol A + vol B exactly;
        /// (A-B) u (AnB) is A by the reference and by SameSolid; u and n commute.
        /// </summary>
        internal static string BoxProperties(BoxCase c)
        {
            DMesh3 a = c.MeshA(), b = c.MeshB();
            string error = "";
            var r = new Dictionary<Ops, DMesh3>();
            foreach (Ops op in BooleanOracle.AllOps) r[op] = Run(a, b, op, ref error);
            DMesh3 rebuilt = Run(r[Ops.Difference], r[Ops.Intersection], Ops.Union, ref error);
            DMesh3 ba = Run(b, a, Ops.Union, ref error), bi = Run(b, a, Ops.Intersection, ref error);
            if (error != "") return error;
            var reversed = new BoxCase(c.B, c.A, c.Exponent, c.Translation);
            double size = 8 * c.Scale;
            return First(
                () => Prefix("union", BooleanOracle.CheckBoxResult(r[Ops.Union], c, Ops.Union)),
                () => Prefix("intersection", BooleanOracle.CheckBoxResult(r[Ops.Intersection], c, Ops.Intersection)),
                () => Prefix("difference", BooleanOracle.CheckBoxResult(r[Ops.Difference], c, Ops.Difference)),
                () => (BooleanOracle.ExactSixVolume(r[Ops.Union]) + BooleanOracle.ExactSixVolume(r[Ops.Intersection]))
                      .Equals(BooleanOracle.ExactSixVolume(a) + BooleanOracle.ExactSixVolume(b)) ? "" : "inclusion-exclusion",
                () => Prefix("(A-B) u (AnB)", BooleanOracle.CheckBoxResult(rebuilt, new BoxCase(c.A, c.A, c.Exponent, c.Translation), Ops.Union)),
                () => Prefix("(A-B) u (AnB) vs A", BooleanOracle.SameSolid(rebuilt, a, size)),
                () => Prefix("union B,A", BooleanOracle.CheckBoxResult(ba, reversed, Ops.Union)),
                () => Prefix("intersection B,A", BooleanOracle.CheckBoxResult(bi, reversed, Ops.Intersection)),
                () => Prefix("union A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Union], ba, size)),
                () => Prefix("intersection A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Intersection], bi, size)));
        }

        static string Prefix(string what, string reason) => reason == "" ? "" : what + ": " + reason;

        [Theory]
        [InlineData("general", 1u, 400)]
        [InlineData("coplanar", 2u, 300)]
        [InlineData("touching", 3u, 300)]
        [InlineData("nested", 4u, 300)]
        public void Boxes(string family, uint campaign, int samples) =>
            Campaign("Boxes(" + family + ")", GenCase(GenFamily(family)), BoxProperties, samples, campaign);

        /// <summary>
        /// One representation change (triangle order, vertex ids, corner rotation, subdivision, slivers) of A, B or
        /// both: the result still matches the analytic reference, and the result without the change (SameSolid).
        /// </summary>
        [Fact]
        public void Boxes_RepresentationInvariance()
        {
            var gen = Gen.Select(GenCase(GenFamily("general")), Gen.Enum<Rep>(), Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[0, 1000],
                (c, rep, operand, op, seed) => new RepresentationCase(c, rep, operand, BooleanOracle.AllOps[op], seed));
            Campaign("Boxes_RepresentationInvariance", gen, x => {
                BoxCase c = x.Case;
                DMesh3 a = c.MeshA(), b = c.MeshB();
                string error = "";
                DMesh3 baseline = Run(a, b, x.Op, ref error);
                if (x.Operand != 1) a = BooleanOracle.Change(a, x.Rep, x.Seed);
                if (x.Operand != 0) b = BooleanOracle.Change(b, x.Rep, x.Seed + 1);
                DMesh3 result = Run(a, b, x.Op, ref error, out double drift);
                // slivers: the result with SafeRounding must be clean and the same solid up to its documented
                // displacement bound (it contracts pieces below its degenerate threshold even where they are exact,
                // ExactBooleanPropertyFindings); the exact geometry is checked without it
                DMesh3 plain = x.Rep == Rep.Slivers ? Run(a, b, x.Op, ref error, safeRounding: false) : result;
                if (error != "") return error;
                return First(
                    () => x.Operand != 1 ? Prefix("changed A", BooleanOracle.CheckRepresentation(c.MeshA(), a, 8 * c.Scale)) : "",
                    () => x.Operand != 0 ? Prefix("changed B", BooleanOracle.CheckRepresentation(c.MeshB(), b, 8 * c.Scale)) : "",
                    () => Prefix("SafeRounding", BooleanOracle.CleanReason(result)),
                    () => x.Rep == Rep.Slivers ? Prefix("SafeRounding vs baseline", BooleanOracle.WithinSafeRoundingBound(result, baseline, 8 * c.Scale, drift)) : "",
                    () => BooleanOracle.CheckBoxResult(plain, c, x.Op, x.Rep == Rep.Slivers),
                    () => Prefix("vs baseline", BooleanOracle.SameSolid(plain, baseline, 8 * c.Scale)));
            }, 400, 5u);
        }

        public sealed class RepresentationCase
        {
            public readonly BoxCase Case; public readonly Rep Rep; public readonly int Operand; public readonly Ops Op; public readonly int Seed;
            public RepresentationCase(BoxCase c, Rep rep, int operand, Ops op, int seed) { Case = c; Rep = rep; Operand = operand; Op = op; Seed = seed; }
            public override string ToString() => $"{Case} {Op} {Rep} of {"ABX"[Operand]} seed {Seed}";
        }

        /// <summary>
        /// Non-axis-aligned solids: results clean; inclusion-exclusion and vol(A-B) + vol(AnB) = vol A within the
        /// rounding tolerance; (A-B) u (AnB) is A as a regularized solid; u and n commute (SameSolid). A bounded
        /// numerical check, weaker than the analytic box reference.
        /// </summary>
        [Fact]
        public void Tetrahedra()
        {
            Campaign("Tetrahedra", GenTets.Select(x => new TetCase(x.a, x.b, x.shift)), x => {
                DMesh3 a = x.A.Mesh(x.Shift), b = x.B.Mesh(x.Shift);
                string error = "";
                var r = new Dictionary<Ops, DMesh3>();
                foreach (Ops op in BooleanOracle.AllOps) r[op] = Run(a, b, op, ref error);
                DMesh3 rebuilt = Run(r[Ops.Difference], r[Ops.Intersection], Ops.Union, ref error);
                DMesh3 ba = Run(b, a, Ops.Union, ref error), bi = Run(b, a, Ops.Intersection, ref error);
                if (error != "") return error;
                double tol = BooleanOracle.VolumeTolerance(r[Ops.Union], r[Ops.Intersection]) + BooleanOracle.VolumeTolerance(r[Ops.Difference], a);
                double va = x.A.SixVolume / 6.0, vb = x.B.SixVolume / 6.0;
                return First(
                    () => Prefix("union", BooleanOracle.CleanReason(r[Ops.Union])),
                    () => Prefix("intersection", BooleanOracle.CleanReason(r[Ops.Intersection])),
                    () => Prefix("difference", BooleanOracle.CleanReason(r[Ops.Difference])),
                    () => Math.Abs(BooleanOracle.Volume(r[Ops.Union]) + BooleanOracle.Volume(r[Ops.Intersection]) - va - vb) <= tol ? "" : "inclusion-exclusion",
                    () => Math.Abs(BooleanOracle.Volume(r[Ops.Difference]) + BooleanOracle.Volume(r[Ops.Intersection]) - va) <= tol ? "" : "vol(A-B) + vol(AnB) != vol A",
                    () => Prefix("(A-B) u (AnB)", BooleanOracle.CleanReason(rebuilt)),
                    () => Prefix("(A-B) u (AnB) vs A", BooleanOracle.SameRegularizedSolid(rebuilt, a, 6)),
                    () => Prefix("union A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Union], ba, 6)),
                    () => Prefix("intersection A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Intersection], bi, 6)));
            }, 600, 6u);
        }

        public sealed class TetCase
        {
            public readonly IntTet A, B; public readonly Vector3d Shift;
            public TetCase(IntTet a, IntTet b, Vector3d shift) { A = a; B = b; Shift = shift; }
            public override string ToString() => $"A={A} B={B} shift={(Shift == Vector3d.Zero ? "0" : "utm")}";
        }

        // ---------------------------------------------------------------- the campaign harness itself

        /// <summary>
        /// A controlled faulty result: the union with one triangle less whenever it has more than 12 triangles.
        /// The campaign must fail and report the original seed, which gives the first failing case again, and a
        /// shrunk seed after at least one shrink (CsCheck accepts a failing case as a shrink only if it is smaller by
        /// its own size measure), which gives a different case that still fails.
        /// </summary>
        [Fact]
        public void Campaign_FindsShrinksAndReplaysAFaultyResult()
        {
            var failing = new List<BoxCase>();
            string Faulty(BoxCase c)
            {
                DMesh3 union = BooleanOracle.Run(c.MeshA(), c.MeshB(), Ops.Union, out string error);
                if (error != "") return error;
                if (union.TriangleCount > 12) union.RemoveTriangle(union.TriangleIndices().Last(), true, false);
                string r = BooleanOracle.CheckBoxResult(union, c, Ops.Union);
                if (r != "") failing.Add(c);
                return r;
            }
            var gen = GenCase(GenFamily("general"));
            var e = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Campaign("faulty union", gen, Faulty, 40, 7u));
            log.WriteLine(e.Message);
            Assert.Contains("not closed", e.Message);
            // the shrunk seed is the one after "Shrunk" (the failure text before it holds the original seed too)
            string afterShrunk = e.Message.Substring(e.Message.IndexOf("Shrunk (", StringComparison.Ordinal));
            Match m = Regex.Match(afterShrunk, "Set seed: \"([^\"]+)\".*\\((\\d+) shrinks");
            Assert.True(m.Success, "no shrunk seed in: " + e.Message);
            Assert.True(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) > 0, "no shrink: " + afterShrunk);
            BoxCase first = failing[0];
            BoxCase shrunk = null;
            gen.Sample(c => { shrunk = c; }, seed: m.Groups[1].Value, iter: 1, threads: 1);
            Assert.NotEqual(first.ToString(), shrunk.ToString());
            Assert.Contains(shrunk.ToString(), afterShrunk);
            Assert.NotEqual("", Faulty(shrunk));
            // the reported original seed gives the first failing case again
            Match original = Regex.Match(e.Message, "fails for seed (\\S+)");
            BoxCase again = null;
            gen.Sample(c => { again = c; }, seed: original.Groups[1].Value, iter: 1, threads: 1);
            Assert.Equal(first.ToString(), again.ToString());
        }

        [Fact]
        public void Campaign_IsReproducible()
        {
            var gen = GenCase(GenFamily("touching"));
            var x = new List<string>();
            var y = new List<string>();
            Campaign("first", gen, c => { x.Add(c.ToString()); return ""; }, 20, 3u);
            Campaign("second", gen, c => { y.Add(c.ToString()); return ""; }, 20, 3u);
            Assert.Equal(x, y);
            Assert.True(x.Distinct().Count() > 10);
        }
    }
}
