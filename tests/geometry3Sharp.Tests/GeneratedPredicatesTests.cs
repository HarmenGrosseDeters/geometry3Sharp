using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using g3;
using G3PredicateCompiler;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The bounds of the exact predicates on fixed-width points, derived by tools/PredicateCompiler from
    /// tools/PredicateCompiler/predicates.spec (docs/design/FIXED_WIDTH_EXACT_POINTS.md sections 5 and 13): the
    /// design doc's table is the derivation's output, the derived bounds hold on inputs at the admission limit, the
    /// spec is the formula of the FixedArith kernels, and the kernels' scratch constants cover the derived scratch.
    /// </summary>
    public unsafe class GeneratedPredicatesTests
    {
        static readonly BigInteger Limit = (BigInteger.One << 255) - 1;

        static string Dir => Path.Combine(AppContext.BaseDirectory, "PredicateCompiler");
        static string Spec => File.ReadAllText(Path.Combine(Dir, "predicates.spec"));
        static Predicate Get(string name) => PredicateCompiler.Analyze(Spec).Single(p => p.Name == name);

        [Fact]
        public void DesignDocTable_IsTheDerivation()
        {
            string doc = File.ReadAllText(Path.Combine(Dir, "FIXED_WIDTH_EXACT_POINTS.md")).Replace("\r\n", "\n");
            Assert.True(doc.Contains(PredicateCompiler.Table(Spec)),
                "the bound table of docs/design/FIXED_WIDTH_EXACT_POINTS.md is out of date: regenerate it (tools/PredicateCompiler/README.md)");
        }

        [Fact]
        public void Bound_IsTheBoundOfFixedPoint3()
        {
            foreach (var p in PredicateCompiler.Analyze(Spec)) {
                Assert.Equal(Limit, p.InputBound);
                Assert.Equal(64 * FixedPoint3.Limbs - 1, p.InputBits);
                Assert.All(p.Nodes.Where(n => n.Op == Op.Input), n => Assert.Equal(FixedPoint3.Limbs, n.ArenaLimbs));
            }
            // admitted at m, rejected beyond it
            Assert.True(FixedPoint3.TryCreate(ExactPoint3.CreateUnreduced(Limit, -Limit, Limit, Limit), out _));
            Assert.False(FixedPoint3.TryCreate(ExactPoint3.CreateUnreduced(Limit + 1, 0, 0, 1), out _));
            Assert.False(FixedPoint3.TryCreate(ExactPoint3.CreateUnreduced(-Limit - 1, 0, 0, 1), out _));
        }

        /// <summary> the results of the derivation against the hand table it replaced (section 5 before the derivation) </summary>
        [Fact]
        public void Derivation_ReproducesTheHandTable()
        {
            Predicate o = Get("Orient"), c = Get("InCircle");
            // the determinants: 6 m^3 < 3 2^766 < 2^768; 192 m^12 < 3 2^3066 < 2^3068 (hand table: 13 / 48 limbs in
            // two's complement, 12 / 48 as magnitudes)
            Assert.Equal("6 m^3", o.Root.Bound.ToString());
            Assert.Equal(768, o.Root.Bits);
            Assert.Equal(12, o.Root.Limbs);
            Assert.Equal("192 m^12", c.Root.Bound.ToString());
            Assert.Equal(3068, c.Root.Bits);
            Assert.Equal(48, c.Root.Limbs);
            Assert.True(o.Root.Max < 3 * BigInteger.Pow(2, 766));
            Assert.True(c.Root.Max < 3 * BigInteger.Pow(2, 3066));
            var bits = c.Nodes.Where(n => n.Name != null).ToDictionary(n => n.Name, n => n.Bits);
            Assert.Equal(511, bits["aA"]);      // A = x w_d - x_d w < 2^511
            Assert.Equal(510, bits["as"]);      // s < 2^510
            Assert.Equal(1021, bits["r1"]);     // A s < 2^1021
            Assert.Equal(1023, bits["r3"]);     // l = A^2 + B^2 < 2^1023
            Assert.Equal(2045, bits["n1"]);     // mixed minor < 2^2045
            Assert.Equal(2043, bits["n3"]);     // pure minor < 2^2043
            Assert.Equal(3066, bits["t1"]);     // terms < 2^3066
            Assert.Equal(3066, bits["t3"]);
        }

        /// <summary>
        /// FixedArith's scratch constants cover the derived scratch. The lengths-only sums: Orient 143 as the comment
        /// in FixedArith counts; InCircle 1035, below the comment's 1078, which takes every determinant entry at the 19
        /// limbs of a lift (A s and B s have 17; its determinant part is 594, not 637, as the review of #223 found)
        /// </summary>
        [Fact]
        public void KernelScratch_CoversTheDerivation()
        {
            Predicate o = Get("Orient"), c = Get("InCircle");
            int orientScratch = Const("OrientScratch"), inCircleScratch = Const("InCircleScratch");
            Assert.True(orientScratch >= o.ArenaScratch, $"OrientScratch {orientScratch} < {o.ArenaScratch}");
            Assert.True(inCircleScratch >= c.ArenaScratch, $"InCircleScratch {inCircleScratch} < {c.ArenaScratch}");
            // Orient: 6 products of 8 limbs, 3 minors of 9, 3 terms of 4 + 8, two sums of 12 + 1
            Assert.Equal(137, o.ArenaScratch);
            Assert.Equal(950, c.ArenaScratch);
            Assert.Equal(143, o.LengthScratch);
            Assert.Equal(1035, c.LengthScratch);
            Assert.True(orientScratch >= o.LengthScratch && inCircleScratch >= c.LengthScratch);
        }

        static int Const(string name)
        {
            var f = typeof(FixedArith).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(f);
            return (int)f.GetRawConstantValue();
        }

        // a random component: the limit, 0, +-1, full limbs, or random bits up to 255; weights positive
        static BigInteger Component(Random rand, bool weight)
        {
            BigInteger v;
            switch (rand.Next(8)) {
                case 0: v = Limit; break;
                case 1: v = weight ? BigInteger.One : BigInteger.Zero; break;
                case 2: v = BigInteger.One; break;
                case 3: v = (BigInteger.One << (64 * rand.Next(1, 4))) - 1; break;
                default:
                    int bits = rand.Next(1, 256);
                    var bytes = new byte[bits / 8 + 2];
                    rand.NextBytes(bytes);
                    bytes[bytes.Length - 1] = 0;
                    v = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
                    if (weight && v.IsZero) v = BigInteger.One;
                    break;
            }
            return (weight || rand.Next(2) == 0) ? v : -v;
        }

        static ExactPoint3 RandomPoint(Random rand, int kind)
        {
            Func<BigInteger> c = () => (rand.Next(2) == 0) ? Limit : -Limit;
            if (kind == 0)      // every component at the limit: the largest values the bounds allow
                return ExactPoint3.CreateUnreduced(c(), c(), c(), Limit);
            if (kind == 3) {    // full 255-bit components of random signs (no cancellation as between +-m)
                Func<BigInteger> full = () => {
                    var bytes = new byte[33];
                    rand.NextBytes(bytes);
                    bytes[32] = 0;
                    return (new BigInteger(bytes) & Limit) | (BigInteger.One << 254);
                };
                Func<BigInteger> sf = () => rand.Next(2) == 0 ? full() : -full();
                return ExactPoint3.CreateUnreduced(sf(), sf(), sf(), full());
            }
            if (kind == 1)      // an integer point (the W = 1 rows of InCircle)
                return ExactPoint3.CreateUnreduced(Component(rand, false), Component(rand, false), Component(rand, false), BigInteger.One);
            return ExactPoint3.CreateUnreduced(Component(rand, false), Component(rand, false), Component(rand, false), Component(rand, true));
        }

        static Dictionary<string, (BigInteger x, BigInteger y, BigInteger w)> Inputs(Predicate p, ExactPoint3[] pts, int i, int j)
        {
            var d = new Dictionary<string, (BigInteger, BigInteger, BigInteger)>();
            for (int k = 0; k < p.Points.Count; ++k)
                d[p.Points[k]] = (pts[k][i], pts[k][j], pts[k].W);
            return d;
        }

        /// <summary>
        /// every value of both predicates stays within its derived bound, on points at the admission limit and on
        /// random ones; the root's sign is the FixedArith kernel's (the spec is the kernel's formula)
        /// </summary>
        [Fact]
        public void Bounds_Hold_AndTheSpecIsTheKernel()
        {
            var rand = new Random(221);
            Predicate o = Get("Orient"), c = Get("InCircle");
            var reached = new Dictionary<Node, int>();
            for (int k = 0; k < 3000; ++k) {
                int kind = k % 4 == 0 ? 0 : k % 4 == 1 ? 1 : 2;
                var pts = Enumerable.Range(0, 4).Select(_ => RandomPoint(rand, kind == 1 && rand.Next(3) == 0 ? 2 : kind)).ToArray();
                if (k % 13 == 0) pts[2] = pts[0];       // zero determinants
                int i = k % 3, j = (i + 1 + (k / 3) % 2) % 3;
                var f = pts.Select(q => { Assert.True(FixedPoint3.TryCreate(q, out var r)); return r; }).ToArray();
                foreach (var p in new[] { o, c }) {
                    var v = PredicateCompiler.Evaluate(p, Inputs(p, pts, i, j));
                    foreach (var n in p.Nodes) {
                        BigInteger a = BigInteger.Abs(v[n]);
                        Assert.True(a <= n.Max, $"{p.Name}: value {n.Name ?? n.Id.ToString()} beyond its bound");
                        int bits = a.IsZero ? 0 : (int)a.GetBitLength();
                        if (!reached.TryGetValue(n, out int r) || bits > r) reached[n] = bits;
                    }
                    int kernel = p == o ? FixedArith.Orient(ref f[0], ref f[1], ref f[2], i, j)
                        : FixedArith.InCircle(ref f[0], ref f[1], ref f[2], ref f[3], i, j);
                    Assert.Equal(v[p.Root].Sign, kernel);
                }
            }
            // the bounds are tight in limbs: every value reached its bound's limb count (the determinants not their
            // bound's bits: 3030 of 3068 for InCircle, as the terms cancel partly)
            foreach (var p in new[] { o, c })
                foreach (var n in p.Nodes)
                    Assert.True(n.Limbs == (reached[n] + 63) / 64, $"{p.Name}: value {n.Name ?? n.Id.ToString()} reached {reached[n]} of {n.Bits} bits");
        }

        // the limbs a FixedArena has handed out
        static long Used(FixedArena ar, ulong* start)
        {
            var cur = (ulong*)Pointer.Unbox(typeof(FixedArena).GetField("cur", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ar));
            return cur - start;
        }

        /// <summary>
        /// the length rule against the kernels' real arena use: the FixedArith operations of Orient (Det3Sign) and
        /// InCircle (incircle_row, Det3Sign) never take more than the derived scratch, and on points with full
        /// 255-bit components they take all of it but 1 / 3 limbs (the model is an upper bound, and close); FixedArena.Take throws beyond it
        /// </summary>
        [Fact]
        public void KernelArenaUse_IsTheDerivedScratch()
        {
            Predicate o = Get("Orient"), c = Get("InCircle");
            var row = typeof(FixedArith).GetMethod("incircle_row", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(row);
            var rand = new Random(223);
            long maxO = 0, maxC = 0;
            ulong* v = stackalloc ulong[48];
            ulong* s = stackalloc ulong[2048];
            for (int k = 0; k < 2000; ++k) {
                var pts = Enumerable.Range(0, 4).Select(_ => RandomPoint(rand, k % 2 == 0 ? 3 : k % 3)).ToArray();
                var f = pts.Select(q => { Assert.True(FixedPoint3.TryCreate(q, out var r)); return r; }).ToArray();
                int i = k % 3, j = (i + 1) % 3;
                var ar = new FixedArena(s, o.ArenaScratch);
                FixedArith.Det3Sign(f[0].Get(i, v), f[0].Get(j, v + 4), f[0].Get(3, v + 8), f[1].Get(i, v + 12), f[1].Get(j, v + 16),
                    f[1].Get(3, v + 20), f[2].Get(i, v + 24), f[2].Get(j, v + 28), f[2].Get(3, v + 32), ref ar);
                maxO = Math.Max(maxO, Used(ar, s));
                // InCircle as FixedArith.InCircle does it, on an arena of exactly the derived scratch (Take throws beyond)
                ar = new FixedArena(s, c.ArenaScratch);
                FixedInt dx = f[3].Get(i, v), dy = f[3].Get(j, v + 4), dw = f[3].Get(3, v + 8);
                var e = new FixedInt[9];
                for (int r = 0; r < 3; ++r) {
                    object[] args = { f[r], dx, dy, dw, f[3].WIsOne, i, j, Pointer.Box(v + 12 + 12 * r, typeof(ulong*)), ar, null, null, null };
                    row.Invoke(null, args);
                    ar = (FixedArena)args[8];
                    e[3 * r] = (FixedInt)args[9]; e[3 * r + 1] = (FixedInt)args[10]; e[3 * r + 2] = (FixedInt)args[11];
                }
                FixedArith.Det3Sign(e[0], e[1], e[2], e[3], e[4], e[5], e[6], e[7], e[8], ref ar);
                maxC = Math.Max(maxC, Used(ar, s));
            }
            // within a few limbs of the model: it lets every subtraction take a carry limb, which the signs do not allow
            // all at once (Orient: the three minors cannot all be additions of magnitudes; measured 136 / 947)
            string msg = $"arena use {maxO} / {maxC}, derived {o.ArenaScratch} / {c.ArenaScratch}";
            Assert.True(maxO <= o.ArenaScratch && maxO >= o.ArenaScratch - 1, msg);
            Assert.True(maxC <= c.ArenaScratch && maxC >= c.ArenaScratch - 3, msg);
        }

        /// <summary> the limbs at their boundaries: 2 m < 2^256 fits 4 limbs, 3 m needs 257 bits, 5 limbs </summary>
        [Fact]
        public void Limbs_AtTheLimbBoundaries()
        {
            var p = PredicateCompiler.Analyze("ipredicate P(a, b) bound 255\n    s2 = a.x + a.y\n    s3 = s2 + b.x\n    sign s3 * b.w").Single();
            var n = p.Nodes.Where(x => x.Name != null).ToDictionary(x => x.Name);
            Assert.Equal(256, n["s2"].Bits);
            Assert.Equal(4, n["s2"].Limbs);
            Assert.Equal(257, n["s3"].Bits);
            Assert.Equal(5, n["s3"].Limbs);
            Assert.Equal("3 m^2", p.Root.Bound.ToString());
            Assert.Equal(5, n["s2"].ArenaLimbs);    // FixedArith takes max + 1
            Assert.Equal(9, p.Root.ArenaLimbs);     // 5 + 4
            Assert.Equal(5 + 5 + 9, p.ArenaScratch);
        }

        /// <summary> the input bound is the spec's, not tied to FixedPoint3: 2^63 - 1 gives one-limb inputs </summary>
        [Fact]
        public void Bound_IsPerPredicate()
        {
            var p = PredicateCompiler.Analyze("ipredicate P(a, b) bound 63\n    sign a.x * b.y - a.y * b.x").Single();
            BigInteger m = (BigInteger.One << 63) - 1;
            Assert.Equal(m, p.InputBound);
            Assert.Equal("2 m^2", p.Root.Bound.ToString());
            Assert.Equal(2 * m * m, p.Root.Max);
            Assert.Equal(127, p.Root.Bits);
            Assert.Equal(2, p.Root.Limbs);
            Assert.All(p.Nodes.Where(n => n.Op == Op.Input), n => Assert.Equal(1, n.ArenaLimbs));
            Assert.Equal(2 + 2 + 3, p.ArenaScratch);
        }

        [Fact]
        public void Compiler_RejectsBadSpecs()
        {
            foreach (var bad in new[] {
                "ipredicate P(a, b) bound 255\n    x = a.x * c.y\n    sign x",          // unknown point
                "ipredicate P(a, b) bound 255\n    sign a.z * b.x",                   // unknown component
                "ipredicate P(a, b) bound 255\n    x = a.x * b.x",                    // no sign line
                "ipredicate P(a, b) bound 255\n    x = a.x * b.x\n    x = a.y\n    sign x",   // repeated name
                "ipredicate P(a, b) bound 255\n    x = a.x * b.x\n    y = b.x * a.x\n    sign x - y",   // the same value twice
                "ipredicate P(a, b) bound 255\n    sign a.x * b.x - b.x * a.x",       // the same value twice, unnamed
                "ipredicate P(a, b) bound 255 x\n    sign a.x * b.x",                 // text after the bound
                "ipredicate P(a, a) bound 255\n    sign a.x * a.y",                   // duplicate point
                "ipredicate P(a, b) bound 255\n    sign a.x",                         // the sign of an input
                "ipredicate P(a, b) bound 255\n    sign (a.x * b.x",                  // missing ')'
                "x = a.x\n",                                                // no predicate
                "ipredicate P(a, b)\n    sign a.x * b.x",                   // no bound
                "ipredicate P(a, b) bound 0\n    sign a.x * b.x",           // bound out of range
                "ipredicate P(a, b) bound -3\n    sign a.x * b.x",
                // 'prepare': only for inputs xyz, a proper subset of known distinct points, once
                "ipredicate P(a, b) bound 64\n    prepare a\n    sign a.x * b.x",
                "ipredicate P(a, b) bound 64 inputs xyz\n    prepare a, b\n    sign a.x * b.x",
                "ipredicate P(a, b) bound 64 inputs xyz\n    prepare c\n    sign a.x * b.x",
                "ipredicate P(a, b, c) bound 64 inputs xyz\n    prepare a, a\n    sign a.x * b.x",
                "ipredicate P(a, b, c) bound 64 inputs xyz\n    prepare a\n    prepare b\n    sign a.x * b.x",
            })
                Assert.Throws<FormatException>(() => PredicateCompiler.Analyze(bad));
        }

        [Fact]
        public void SymbolicTaylorVerify_IndirectSpec_AllFiltersPass()
        {
            string indirectSpec = File.ReadAllText(Path.Combine(Dir, "indirect.spec"));
            var report = SymbolicTaylorVerify.VerifySpec(indirectSpec);
            Assert.True(report.AllPassed, SymbolicTaylorVerify.FormatReport(report));
            Assert.Equal(8, report.Rows.Count);
            foreach (var row in report.Rows) {
                Assert.True(row.Dominates, $"{row.PredicateName} {row.NodeLabel} {row.DomainLabel}: ST={row.SymbolicTaylorBound:E4} > gen={row.GeneratorBound:E4}");
                Assert.True(row.SamplesValid, $"{row.PredicateName} {row.NodeLabel} {row.DomainLabel}: exact sample violation");
                Assert.True(row.Ratio > 1.5, $"{row.PredicateName} {row.NodeLabel} {row.DomainLabel}: unexpected ratio {row.Ratio}");
                Assert.True(row.MaxSampledExactErr <= row.SymbolicTaylorBound);
            }
        }

        [Fact]
        public void SymbolicTaylorVerify_MatchesFPTaylorPilotLeavesAndChains()
        {
            string indirectSpec = File.ReadAllText(Path.Combine(Dir, "indirect.spec"));
            var preds = PredicateCompiler.Analyze(indirectSpec).ToDictionary(p => p.Name);

            // Exact FPTaylorPilot LPI domain: r in [0, 0.5], all other points in [1, 2].
            var lpi = preds["Orient3dLPI"];
            var lpiRanges = new Dictionary<string, (double, double)> { ["r"] = (0.0, 0.5) };
            var lpiNodes = lpi.Nodes.Where(n => n.Name != null).ToDictionary(n => n.Name);

            // Leaf prx = p.x - r.x in [0.5, 2.0]: exact binary64 binade bound is 2^-53 = 1.1102230246251565e-16 (1.00x OCaml FPTaylor).
            var prxRow = SymbolicTaylorVerify.VerifyNode(lpi, lpiNodes["prx"], "prx", "FPTaylorPilot", (1.0, 2.0), lpiRanges);
            Assert.True(prxRow.Passed);
            Assert.Equal(Math.ScaleB(1.0, -53), prxRow.SymbolicTaylorBound);

            // LPI nx and d chains on FPTaylorPilot domain: Symbolic Taylor strictly below generator bound.
            var nxRow = SymbolicTaylorVerify.VerifyNode(lpi, lpiNodes["nx"], "nx", "FPTaylorPilot", (1.0, 2.0), lpiRanges);
            var dRow = SymbolicTaylorVerify.VerifyNode(lpi, lpiNodes["d"], "d", "FPTaylorPilot", (1.0, 2.0), lpiRanges);
            Assert.True(nxRow.Passed && nxRow.Ratio > 1.5);
            Assert.True(dRow.Passed && dRow.Ratio > 2.0);
            Assert.True(dRow.SymbolicTaylorBound < 2.6e-14, $"LPI d bound {dRow.SymbolicTaylorBound:E4} unexpectedly loose");

            // Exact FPTaylorPilot TPI domain: all points in [1, 2].
            // Leaf u1_x = b1.x - a1.x in [-1, 1]: exact binary64 binade bound is 2^-54 = 5.551115123125783e-17 (1.00x OCaml FPTaylor).
            var tpi = preds["Orient3dTPI"];
            var tpiNodes = tpi.Nodes.Where(n => n.Name != null).ToDictionary(n => n.Name);
            var u1xRow = SymbolicTaylorVerify.VerifyNode(tpi, tpiNodes["u1_x"], "u1_x", "FPTaylorPilot", (1.0, 2.0));
            Assert.True(u1xRow.Passed);
            Assert.Equal(Math.ScaleB(1.0, -54), u1xRow.SymbolicTaylorBound);

            var d1Row = SymbolicTaylorVerify.VerifyNode(tpi, tpiNodes["D1"], "D1", "FPTaylorPilot", (1.0, 2.0));
            Assert.True(d1Row.Passed && d1Row.Ratio > 2.0);
        }

        [Fact]
        public void SymbolicTaylorVerify_HalfUlpMaxAndDirectedRounding_AreSoundAtBoundaries()
        {
            Assert.Equal(0.0, SymbolicTaylorVerify.HalfUlpMax(0.0));
            Assert.Equal(double.Epsilon, SymbolicTaylorVerify.HalfUlpMax(double.Epsilon));
            Assert.Equal(double.Epsilon, SymbolicTaylorVerify.HalfUlpMax(Math.ScaleB(1.0, -1022)));
            Assert.Equal(double.Epsilon, SymbolicTaylorVerify.HalfUlpMax(Math.ScaleB(1.0, -1021)));
            Assert.Equal(Math.ScaleB(1.0, -54), SymbolicTaylorVerify.HalfUlpMax(Math.BitDecrement(1.0)));
            Assert.Equal(Math.ScaleB(1.0, -54), SymbolicTaylorVerify.HalfUlpMax(1.0));
            Assert.Equal(Math.ScaleB(1.0, -53), SymbolicTaylorVerify.HalfUlpMax(Math.BitIncrement(1.0)));
            Assert.Equal(Math.ScaleB(1.0, -53), SymbolicTaylorVerify.HalfUlpMax(1.5));
            Assert.Equal(Math.ScaleB(1.0, -53), SymbolicTaylorVerify.HalfUlpMax(2.0));
            Assert.Equal(Math.ScaleB(1.0, -52), SymbolicTaylorVerify.HalfUlpMax(Math.BitIncrement(2.0)));
            Assert.True(double.IsPositiveInfinity(SymbolicTaylorVerify.HalfUlpMax(double.PositiveInfinity)));
            Assert.True(double.IsPositiveInfinity(SymbolicTaylorVerify.HalfUlpMax(double.NaN)));

            // Exact directed rounding checks against Rational across normal, power-of-two, and extreme scales.
            var rng = new Random(2026);
            double[] special = { 0.0, 1.0, -1.0, 2.0, -2.0, 0.5, 1.0 / 3.0, Math.BitIncrement(1.0), Math.BitDecrement(2.0), 1e-150, 1e150, double.Epsilon };
            for (int i = 0; i < 2000; ++i) {
                double a = i < special.Length * special.Length
                    ? special[i / special.Length]
                    : (rng.NextDouble() * 2.0 - 1.0) * Math.ScaleB(1.0, rng.Next(-120, 120));
                double b = i < special.Length * special.Length
                    ? special[i % special.Length]
                    : (rng.NextDouble() * 2.0 - 1.0) * Math.ScaleB(1.0, rng.Next(-120, 120));
                var ra = Rational.FromDouble(a);
                var rb = Rational.FromDouble(b);

                double ad = Interval64.AddDown(a, b), au = Interval64.AddUp(a, b);
                if (double.IsFinite(ad)) Assert.True(Rational.FromDouble(ad) <= ra + rb);
                if (double.IsFinite(au)) Assert.True(ra + rb <= Rational.FromDouble(au));

                double md = Interval64.MulDown(a, b), mu = Interval64.MulUp(a, b);
                if (double.IsFinite(md)) Assert.True(Rational.FromDouble(md) <= ra * rb);
                if (double.IsFinite(mu)) Assert.True(ra * rb <= Rational.FromDouble(mu));
            }
        }

        [Fact]
        public void SymbolicTaylorVerify_RejectsCorruptedGeneratorBound()
        {
            string indirectSpec = File.ReadAllText(Path.Combine(Dir, "indirect.spec"));
            var preds = PredicateCompiler.Analyze(indirectSpec);
            var lpi = preds.Single(p => p.Name == "Orient3dLPI");

            // Corrupt FilterK and C on both sign factors below the proven Symbolic Taylor bound (1/4 of true value).
            for (int f = 0; f < lpi.Signs.Count; ++f) {
                lpi.Signs[f].C = lpi.Signs[f].C / Rational.Int(4);
                lpi.FilterK[f] *= 0.25;
            }
            var report = SymbolicTaylorVerify.VerifyPredicates(new[] { lpi }, maxBoxes: 128, exactSamples: 32);
            Assert.False(report.AllPassed);
            Assert.All(report.Rows, r => Assert.False(r.Dominates));

            // Corrupt a tight leaf C to 0.25 * u: both domination and exact sample checks must fail.
            var prx = lpi.Nodes.Single(n => n.Name == "prx");
            prx.C = Rational.U / Rational.Int(4);
            var leafRow = SymbolicTaylorVerify.VerifyNode(lpi, prx, "prx", "corrupted", (1.0, 2.0),
                new Dictionary<string, (double, double)> { ["r"] = (0.0, 0.5) }, maxBoxes: 32, exactSamples: 64);
            Assert.False(leafRow.Dominates);
            Assert.False(leafRow.SamplesValid);
            Assert.False(leafRow.Passed);
        }

        [Fact]
        public void SymbolicTaylorVerify_IsDeterministicAndReusable()
        {
            string indirectSpec = File.ReadAllText(Path.Combine(Dir, "indirect.spec"));
            var r1 = SymbolicTaylorVerify.VerifySpec(indirectSpec, maxBoxes: 64, exactSamples: 32);
            var r2 = SymbolicTaylorVerify.VerifySpec(indirectSpec, maxBoxes: 64, exactSamples: 32);
            Assert.Equal(r1.AllPassed, r2.AllPassed);
            Assert.Equal(r1.Rows.Count, r2.Rows.Count);
            for (int i = 0; i < r1.Rows.Count; ++i) {
                Assert.Equal(r1.Rows[i].PredicateName, r2.Rows[i].PredicateName);
                Assert.Equal(r1.Rows[i].NodeLabel, r2.Rows[i].NodeLabel);
                Assert.Equal(r1.Rows[i].DomainLabel, r2.Rows[i].DomainLabel);
                Assert.Equal(r1.Rows[i].SymbolicTaylorBound, r2.Rows[i].SymbolicTaylorBound);
                Assert.Equal(r1.Rows[i].GeneratorBound, r2.Rows[i].GeneratorBound);
                Assert.Equal(r1.Rows[i].MaxSampledExactErr, r2.Rows[i].MaxSampledExactErr);
                Assert.Equal(r1.Rows[i].BoxesEvaluated, r2.Rows[i].BoxesEvaluated);
            }
        }
    }
}
