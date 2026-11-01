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
    public class GeneratedPredicatesTests
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
                "ipredicate P(a, a) bound 255\n    sign a.x * a.y",                   // duplicate point
                "ipredicate P(a, b) bound 255\n    sign a.x",                         // the sign of an input
                "ipredicate P(a, b) bound 255\n    sign (a.x * b.x",                  // missing ')'
                "x = a.x\n",                                                // no predicate
                "ipredicate P(a, b)\n    sign a.x * b.x",                   // no bound
                "ipredicate P(a, b) bound 0\n    sign a.x * b.x",           // bound out of range
                "ipredicate P(a, b) bound -3\n    sign a.x * b.x",
            })
                Assert.Throws<FormatException>(() => PredicateCompiler.Analyze(bad));
        }
    }
}
