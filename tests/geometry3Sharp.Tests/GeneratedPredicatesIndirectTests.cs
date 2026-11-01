using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using g3;
#if NET
using G3PredicateCompiler;
#endif
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The generated indirect predicates (math/Generated/IndirectPredicates.cs, from tools/PredicateCompiler/indirect.spec;
    /// docs/design/INDIRECT_PREDICATES.md): Orient3d of a line-plane point (LPI) and of a three-plane point (TPI)
    /// against an oracle that constructs the point as a rational and evaluates orient3d on it (its own formulas, no code
    /// shared with the generator), on the FixedArith path (components up to 2^64 - 1) and the BigInteger path beyond, at
    /// degenerate inputs, at the bound, and under the similarities of the harness.
    /// </summary>
    public class GeneratedPredicatesIndirectTests
    {
        static readonly BigInteger M64 = (BigInteger.One << 64) - 1;
#if NET
        static string Dir => Path.Combine(AppContext.BaseDirectory, "PredicateCompiler");
        static string Spec => File.ReadAllText(Path.Combine(Dir, "indirect.spec"));

        [Fact]
        public void GeneratedFile_IsUpToDate()
        {
            string expected = File.ReadAllText(Path.Combine(Dir, "IndirectPredicates.cs.txt")).Replace("\r\n", "\n");
            string generated = PredicateCompiler.Code(Spec, "tools/PredicateCompiler/indirect.spec", "math/Generated/IndirectPredicates.cs", "IndirectPredicates");
            Assert.True(expected == generated, "math/Generated/IndirectPredicates.cs is out of date: regenerate it (tools/PredicateCompiler/README.md)");
        }

        [Fact]
        public void DesignDocTable_IsTheDerivation()
        {
            string doc = File.ReadAllText(Path.Combine(Dir, "INDIRECT_PREDICATES.md")).Replace("\r\n", "\n");
            Assert.True(doc.Contains(PredicateCompiler.Table(Spec)),
                "the bound table of docs/design/INDIRECT_PREDICATES.md is out of date: regenerate it (tools/PredicateCompiler/README.md)");
        }

#endif
        // ---- the oracle: construct the point, then orient3d on it (sign of det(x - c, a - c, b - c)) ----

        static BigInteger[] V(ExactPoint3 p) => new[] { p.X, p.Y, p.Z };
        static BigInteger[] Sub(BigInteger[] a, BigInteger[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static BigInteger[] Cross(BigInteger[] u, BigInteger[] v) =>
            new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
        static BigInteger Dot(BigInteger[] u, BigInteger[] v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
        // det of the rows u, v, w, by the rule of Sarrus (not the generator's expansion)
        static BigInteger Det(BigInteger[] u, BigInteger[] v, BigInteger[] w) =>
            u[0] * v[1] * w[2] + u[1] * v[2] * w[0] + u[2] * v[0] * w[1] - u[2] * v[1] * w[0] - u[1] * v[0] * w[2] - u[0] * v[2] * w[1];

        // orient3d of the rational point N / den (den != 0) against integer points a, b, c
        static int OrientRational(BigInteger[] N, BigInteger den, ExactPoint3 a, ExactPoint3 b, ExactPoint3 c)
        {
            if (den.Sign < 0) { N = N.Select(x => -x).ToArray(); den = -den; }
            var x = ExactPoint3.Create(N[0], N[1], N[2], den);      // reduced: the oracle's own point
            var row = new[] { x.X - c.X * x.W, x.Y - c.Y * x.W, x.Z - c.Z * x.W };      // (x - c) W, W > 0
            return Det(row, Sub(V(a), V(c)), Sub(V(b), V(c))).Sign;
        }

        // the line p-q meets the plane r, s, t at p + t (q - p), t = n.(r - p) / n.(q - p)
        static int OracleLPI(ExactPoint3[] P)
        {
            ExactPoint3 p = P[0], q = P[1], r = P[2], s = P[3], t = P[4];
            var n = Cross(Sub(V(s), V(r)), Sub(V(t), V(r)));
            BigInteger num = Dot(n, Sub(V(r), V(p))), den = Dot(n, Sub(V(q), V(p)));
            if (den.IsZero) return 0;
            var qp = Sub(V(q), V(p));
            var N = new[] { p.X * den + num * qp[0], p.Y * den + num * qp[1], p.Z * den + num * qp[2] };
            return OrientRational(N, den, P[5], P[6], P[7]);
        }

        // the point of three planes by Cramer's rule with replaced columns
        static int OracleTPI(ExactPoint3[] P)
        {
            var n = new BigInteger[3][];
            var d = new BigInteger[3];
            for (int k = 0; k < 3; ++k) {
                n[k] = Cross(Sub(V(P[3 * k + 1]), V(P[3 * k])), Sub(V(P[3 * k + 2]), V(P[3 * k])));
                d[k] = Dot(n[k], V(P[3 * k]));
            }
            BigInteger det = Det(n[0], n[1], n[2]);
            if (det.IsZero) return 0;
            var N = new BigInteger[3];
            for (int col = 0; col < 3; ++col) {
                var rows = n.Select(r => (BigInteger[])r.Clone()).ToArray();
                for (int k = 0; k < 3; ++k) rows[k][col] = d[k];
                N[col] = Det(rows[0], rows[1], rows[2]);
            }
            return OrientRational(N, det, P[9], P[10], P[11]);
        }

        // ---- inputs ----

        static BigInteger Random(Random rand, int bits)
        {
            if (bits <= 0) return 0;
            var bytes = new byte[bits / 8 + 2];
            rand.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;
            var v = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
            return rand.Next(2) == 0 ? v : -v;
        }

        // a component: at the bound 2^64 - 1, beyond it (2^64 .. 2^100), small, or random up to the given bits
        static BigInteger Component(Random rand, int bits, int kind)
        {
            Func<BigInteger, BigInteger> sg = v => rand.Next(2) == 0 ? v : -v;
            switch (kind) {
                case 0: return sg(M64);
                case 1: return sg(M64 - BigInteger.Abs(Random(rand, 8)));       // just below the bound
                case 2: return sg((BigInteger.One << 64) + BigInteger.Abs(Random(rand, 36)));     // beyond: BigInteger path
                default: return Random(rand, bits);
            }
        }

        static ExactPoint3 Point(Random rand, int bits, int kind) =>
            ExactPoint3.FromInteger(Component(rand, bits, kind), Component(rand, bits, kind), Component(rand, bits, kind));

        static FixedPoint3 Fixed(ExactPoint3 p)
        {
            Assert.True(FixedPoint3.TryCreate(p, out var f));
            return f;
        }

        static int LPI(ExactPoint3[] P)
        {
            var f = P.Select(Fixed).ToArray();
            int r = IndirectPredicates.Orient3dLPI(ref f[0], ref f[1], ref f[2], ref f[3], ref f[4], ref f[5], ref f[6], ref f[7]);
            Assert.Equal(r, IndirectPredicates.Orient3dLPIBig(P[0], P[1], P[2], P[3], P[4], P[5], P[6], P[7]));
            return r;
        }

        static int TPI(ExactPoint3[] P)
        {
            var f = P.Select(Fixed).ToArray();
            int r = IndirectPredicates.Orient3dTPI(ref f[0], ref f[1], ref f[2], ref f[3], ref f[4], ref f[5], ref f[6], ref f[7],
                ref f[8], ref f[9], ref f[10], ref f[11]);
            Assert.Equal(r, IndirectPredicates.Orient3dTPIBig(P[0], P[1], P[2], P[3], P[4], P[5], P[6], P[7], P[8], P[9], P[10], P[11]));
            return r;
        }

        /// <summary> both predicates equal the oracle at every size: small, random up to 64 bits, at the bound, beyond it </summary>
        [Fact]
        public void Predicates_MatchTheOracle_AtAllSizes()
        {
            var rand = new Random(246);
            int[] count = new int[3];
            for (int k = 0; k < 4000; ++k) {
                int bits = new[] { 4, 20, 40, 63, 64 }[k % 5];
                int kind = k % 7 == 0 ? 0 : k % 7 == 1 ? 1 : k % 11 == 2 ? 2 : 3;
                var P = Enumerable.Range(0, 12).Select(_ => Point(rand, bits, kind)).ToArray();
                if (kind == 3 && k % 13 == 0) P[rand.Next(12)] = Point(rand, 64, 2);       // one point beyond the bound
                int o = LPI(P.Take(8).ToArray());
                Assert.Equal(OracleLPI(P), o);
                count[o + 1]++;
                Assert.Equal(OracleTPI(P), TPI(P));
            }
            Assert.True(count[0] > 1000 && count[2] > 1000);
        }

        /// <summary>
        /// exact zeros: the LPI point lies on its own plane and on its own line; the TPI point on each of its planes;
        /// a line parallel to the plane and dependent planes give 0 (d = 0)
        /// </summary>
        [Fact]
        public void Predicates_ExactZeros()
        {
            var rand = new Random(7);
            for (int k = 0; k < 600; ++k) {
                int bits = new[] { 10, 40, 64 }[k % 3];
                var P = Enumerable.Range(0, 12).Select(_ => Point(rand, bits, 3)).ToArray();
                // LPI against the plane r, s, t itself, and against the line p, q and any c
                Assert.Equal(0, LPI(new[] { P[0], P[1], P[2], P[3], P[4], P[2], P[3], P[4] }));
                Assert.Equal(0, LPI(new[] { P[0], P[1], P[2], P[3], P[4], P[0], P[1], P[7] }));
                // TPI against each of its planes
                for (int j = 0; j < 3; ++j) {
                    var Q = P.Take(9).Concat(new[] { P[3 * j], P[3 * j + 1], P[3 * j + 2] }).ToArray();
                    Assert.Equal(0, TPI(Q));
                    Assert.Equal(0, OracleTPI(Q));
                }
                // parallel: q - p in the plane's direction (q = p + (s - r)); dependent: plane 3 = plane 1
                var q = ExactPoint3.FromInteger(P[0].X + P[3].X - P[2].X, P[0].Y + P[3].Y - P[2].Y, P[0].Z + P[3].Z - P[2].Z);
                Assert.Equal(0, LPI(new[] { P[0], q, P[2], P[3], P[4], P[5], P[6], P[7] }));
                Assert.Equal(0, TPI(new[] { P[0], P[1], P[2], P[3], P[4], P[5], P[0], P[1], P[2], P[9], P[10], P[11] }));
            }
        }

#if NET
        /// <summary> the bound is a limb boundary: 2^64 - 1 takes the FixedArith path, 2^64 the BigInteger path; both exact </summary>
        [Fact]
        public void BoundOfTheFixedPath_IsWholeLimbs()
        {
            var p = PredicateCompiler.Analyze(Spec);
            Assert.All(p, x => Assert.Equal(64, x.InputBits));
            var rand = new Random(64);
            foreach (var big in new[] { M64, M64 + 1 })
                for (int k = 0; k < 200; ++k) {
                    var P = Enumerable.Range(0, 12).Select(_ => Point(rand, 64, 3)).ToArray();
                    int j = rand.Next(12);
                    P[j] = ExactPoint3.FromInteger(rand.Next(2) == 0 ? big : -big, P[j].Y, P[j].Z);
                    Assert.Equal(OracleLPI(P), LPI(P.Take(8).ToArray()));
                    Assert.Equal(OracleTPI(P), TPI(P));
                }
        }

#endif
#if NET
        /// <summary> every value stays within its derived bound on inputs at +-(2^64 - 1) and random 64-bit ones </summary>
        [Fact]
        public void Bounds_Hold_AtTheBound()
        {
            var rand = new Random(3);
            foreach (var p in PredicateCompiler.Analyze(Spec))
                for (int k = 0; k < 300; ++k) {
                    var pts = new Dictionary<string, (BigInteger, BigInteger, BigInteger)>();
                    foreach (var name in p.Points)
                        pts[name] = (Component(rand, 64, k % 2), Component(rand, 64, k % 3 == 0 ? 0 : 1), Component(rand, 64, 0));
                    var v = PredicateCompiler.Evaluate(p, pts);
                    foreach (var n in p.Nodes)
                        Assert.True(BigInteger.Abs(v[n]) <= n.Max, $"{p.Name}: {n.Name ?? n.Id.ToString()} beyond its bound");
                }
        }

#endif
        [Fact]
        public void Predicates_RejectRationalInputs()
        {
            var a = ExactPoint3.FromInteger(1, 2, 3);
            var h = ExactPoint3.Create(1, 2, 3, 2);
            var f = Fixed(a);
            var g = Fixed(h);
            Assert.Throws<ArgumentException>(() => IndirectPredicates.Orient3dLPI(ref g, ref f, ref f, ref f, ref f, ref f, ref f, ref f));
            Assert.Throws<ArgumentException>(() => IndirectPredicates.Orient3dLPIBig(a, a, a, a, a, a, a, h));
        }

        /// <summary>
        /// invariance (Similarity.All()): the predicates equal the oracle on every transformed configuration (coordinates
        /// on a 2^-20 grid of the transformed points, as integers), and the 2^k scales give the untransformed signs bit
        /// for bit
        /// </summary>
        [Fact]
        public void Invariance_UnderTheHarnessSimilarities()
        {
            var rand = new Random(11);
            for (int k = 0; k < 150; ++k) {
                var basePts = Enumerable.Range(0, 12).Select(_ => new Vector3d(rand.Next(-1000, 1000), rand.Next(-1000, 1000), rand.Next(-1000, 1000))).ToArray();
                if (k % 5 == 0)         // zeros that every transformation keeps: the LPI point against its own plane
                    for (int j = 0; j < 3; ++j) basePts[5 + j] = basePts[2 + j];
                int[] reference = null;
                foreach (var sim in Similarity.All()) {
                    // integer coordinates: the transformed point times 2^20 / scale (exact for the 2^k scales)
                    var P = basePts.Select(b => sim.Apply(b) * (Math.Pow(2, 20) / sim.Scale)).Select(x =>
                        ExactPoint3.FromInteger(new BigInteger(Math.Round(x.x)), new BigInteger(Math.Round(x.y)), new BigInteger(Math.Round(x.z)))).ToArray();
                    int[] r = { LPI(P.Take(8).ToArray()), TPI(P) };
                    Assert.Equal(OracleLPI(P), r[0]);
                    Assert.Equal(OracleTPI(P), r[1]);
                    if (sim.IsExact) {
                        if (reference == null) reference = r;
                        else Assert.Equal(reference, r);
                    }
                }
            }
        }
    }
}
