using System;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The ordering, the structure of L and the supernodes of SparseCholesky and SparseHermitianCholesky (one shared analysis)
    /// are the same arrays, bit for bit, as before the analysis was shared and made cheaper: golden hashes taken on the
    /// previous code, for the analysis arrays, the values of L and a solution.
    /// </summary>
    public class CholeskyAnalysisTests
    {
        readonly ITestOutputHelper output;
        public CholeskyAnalysisTests(ITestOutputHelper output) { this.output = output; }

        static ulong Next(ref ulong s) { s = s * 6364136223846793005UL + 1442695040888963407UL; return s >> 11; }

        static ulong Fnv(ulong h, long v) { h ^= (ulong)v; return h * 1099511628211UL; }
        static ulong HashInts(int[][] arrays)
        {
            ulong h = 1469598103934665603UL;
            foreach (var a in arrays) { h = Fnv(h, a.Length); foreach (int v in a) h = Fnv(h, v); }
            return h;
        }
        static ulong HashDoubles(params double[][] arrays)
        {
            ulong h = 1469598103934665603UL;
            foreach (var a in arrays) { h = Fnv(h, a.Length); foreach (double v in a) h = Fnv(h, BitConverter.DoubleToInt64Bits(v)); }
            return h;
        }

        // ---- matrices (rational entries only, so the hashes hold on every platform)

        // a triangulated grid, bs unknowns per node (real symmetric)
        static SparseCholesky RealGrid(int nx, int ny, int bs, ulong seed)
        {
            ulong s = seed; var c = new SparseCholesky(nx * ny * bs, bs);
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = (y * nx + x) * bs;
                    for (int u = 0; u < bs; ++u) {
                        c.Add(a + u, a + u, 8.0 + (Next(ref s) % 64) / 64.0);
                        for (int v = 0; v < u; ++v) c.Add(a + u, a + v, -((Next(ref s) % 32) / 64.0));
                    }
                    foreach (var q in new[] { new[] { x + 1, y }, new[] { x, y + 1 }, new[] { x + 1, y + 1 } }) {
                        if (q[0] >= nx || q[1] >= ny) continue;
                        int b = (q[1] * nx + q[0]) * bs;
                        for (int u = 0; u < bs; ++u)
                            for (int v = 0; v < bs; ++v) c.Add(b + v, a + u, -((Next(ref s) % 128) / 128.0 + 0.25) / bs);
                    }
                }
            return c;
        }
        static SparseCholesky RealGrid3(int k, ulong seed)
        {
            ulong s = seed; var c = new SparseCholesky(k * k * k);
            for (int z = 0; z < k; ++z) for (int y = 0; y < k; ++y) for (int x = 0; x < k; ++x) {
                int a = (z * k + y) * k + x; c.Add(a, a, 9.0 + (Next(ref s) % 16) / 16.0);
                if (x + 1 < k) c.Add(a + 1, a, -(double)(1 + Next(ref s) % 8) / 8.0);
                if (y + 1 < k) c.Add(a + k, a, -(double)(1 + Next(ref s) % 8) / 8.0);
                if (z + 1 < k) c.Add(a + k * k, a, -(double)(1 + Next(ref s) % 8) / 8.0);
            }
            return c;
        }
        static SparseCholesky RealRandom(int n, int perRow, ulong seed)
        {
            ulong s = seed; var c = new SparseCholesky(n);
            for (int i = 0; i < n; ++i) {
                c.Add(i, i, perRow * 2 + 1 + (Next(ref s) % 16) / 16.0);
                for (int t = 0; t < perRow; ++t) { int j = (int)(Next(ref s) % (ulong)n); if (j != i) c.Add(i, j, -(double)(1 + Next(ref s) % 8) / 8.0); }
            }
            return c;
        }
        // a matrix with several connected components and an isolated unknown (several roots of the elimination tree)
        static SparseCholesky RealComponents()
        {
            ulong s = 5; var c = new SparseCholesky(130);
            for (int i = 0; i < 130; ++i) c.Add(i, i, 6.0 + (Next(ref s) % 8) / 8.0);
            for (int i = 0; i + 1 < 60; ++i) c.Add(i + 1, i, -1.0);
            for (int i = 60; i + 2 < 120; ++i) { c.Add(i + 2, i, -0.5); c.Add(i + 1, i, -0.25); }
            return c;
        }
        // complex Hermitian: the grid of RealGrid with complex off-diagonal entries
        internal static SparseHermitianCholesky ComplexGrid(int nx, int ny, ulong seed)
        {
            ulong s = seed; var c = new SparseHermitianCholesky(nx * ny);
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = y * nx + x; c.Add(a, a, 8.0 + (Next(ref s) % 64) / 64.0, 0);
                    foreach (var q in new[] { new[] { x + 1, y }, new[] { x, y + 1 }, new[] { x + 1, y + 1 } }) {
                        if (q[0] >= nx || q[1] >= ny) continue;
                        int b = q[1] * nx + q[0];
                        // both orders of the pair, so that the conjugate flag of the permuted entries is exercised
                        double re = -((Next(ref s) % 128) / 128.0 + 0.25), im = ((Next(ref s) % 64) / 64.0 - 0.5);
                        if (Next(ref s) % 2 == 0) c.Add(b, a, re, im); else c.Add(a, b, re, -im);
                    }
                }
            return c;
        }
        internal static SparseHermitianCholesky ComplexRandom(int n, int perRow, ulong seed)
        {
            ulong s = seed; var c = new SparseHermitianCholesky(n);
            for (int i = 0; i < n; ++i) {
                c.Add(i, i, perRow * 2 + 1 + (Next(ref s) % 16) / 16.0, 0);
                for (int t = 0; t < perRow; ++t) { int j = (int)(Next(ref s) % (ulong)n); if (j != i) c.Add(i, j, -(double)(1 + Next(ref s) % 8) / 8.0, ((double)(Next(ref s) % 8) - 4) / 16.0); }
            }
            return c;
        }

        static double[] Rhs(int n, ulong seed) { ulong s = seed; var b = new double[n]; for (int i = 0; i < n; ++i) b[i] = ((double)(Next(ref s) % 2001) - 1000) / 64.0; return b; }

        struct Hashes { public ulong Analysis, Factor, Solution; public override string ToString() { return Analysis + "UL, " + Factor + "UL, " + Solution + "UL"; } }

        static Hashes Run(SparseCholesky c)
        {
            Assert.True(c.Factorize(), c.Error);
            var x = new double[c.N]; c.Solve(Rhs(c.N, 3), x);
            return new Hashes { Analysis = HashInts(c.AnalysisArrays), Factor = HashDoubles(c.FactorValues), Solution = HashDoubles(x) };
        }
        static Hashes Run(SparseHermitianCholesky c)
        {
            Assert.True(c.Factorize(), c.Error);
            var x = new double[2 * c.N]; c.Solve(Rhs(2 * c.N, 3), x);
            return new Hashes { Analysis = HashInts(c.AnalysisArrays), Factor = HashDoubles(c.FactorValues), Solution = HashDoubles(x) };
        }

        static readonly string[] Names = { "real grid 40x40", "real grid 24x24 bs2", "real grid 17x13 bs3", "real 3d 12^3", "real random 900", "real components", "complex grid 40x40", "complex grid 33x21", "complex random 700" };
        static Hashes Case(int i)
        {
            switch (i) {
                case 0: return Run(RealGrid(40, 40, 1, 1));
                case 1: return Run(RealGrid(24, 24, 2, 2));
                case 2: return Run(RealGrid(17, 13, 3, 3));
                case 3: return Run(RealGrid3(12, 4));
                case 4: return Run(RealRandom(900, 4, 5));
                case 5: return Run(RealComponents());
                case 6: return Run(ComplexGrid(40, 40, 6));
                case 7: return Run(ComplexGrid(33, 21, 7));
                default: return Run(ComplexRandom(700, 4, 8));
            }
        }

        // hashes taken with the code before the analysis was shared (master at 92654526): analysis arrays, values of L, a solution
        static readonly ulong[][] Golden = {
            new ulong[] { 11214081012657704556UL, 13582808965648679454UL, 8194271590920427217UL },
            new ulong[] { 15143858315145624653UL, 4209744824681651760UL, 12311591901017858934UL },
            new ulong[] { 2146382567492016101UL, 10950510485985180180UL, 5043962356874442377UL },
            new ulong[] { 16599961039041603106UL, 13897134108444290500UL, 14357801532516837928UL },
            new ulong[] { 12916233383112508390UL, 11393826566449560625UL, 7253421344690539072UL },
            new ulong[] { 2018545021747793930UL, 13777582485456262976UL, 16502908655883178337UL },
            new ulong[] { 203531034621410940UL, 5138671580511745281UL, 16384738400946779578UL },
            new ulong[] { 2824921013006895121UL, 4021844954555359435UL, 17993860210476440836UL },
            new ulong[] { 10277375782302424802UL, 16764946156797365360UL, 11671204412522918497UL }
        };

        [Fact]
        public void Analysis_FactorAndSolution_MatchTheCodeBeforeTheSharedAnalysis()
        {
            for (int i = 0; i < Names.Length; ++i) {
                Hashes h = Case(i);
                Assert.True(Golden[i][0] == h.Analysis, Names[i] + ": ordering, structure of L, supernodes or permuted pattern differ");
                Assert.True(Golden[i][1] == h.Factor, Names[i] + ": values of L differ");
                Assert.True(Golden[i][2] == h.Solution, Names[i] + ": solution differs");
            }
        }

        [Fact]
        public void Refactorization_WithTheSamePattern_ReusesTheAnalysis_AndGivesTheSameBits()
        {
            // SparseCholesky keeps its analysis when the pattern is the same: factorize again, and after Clear and the same entries
            var c = RealGrid(30, 30, 2, 9);
            Assert.True(c.Factorize());
            ulong h1 = HashDoubles(c.FactorValues), a1 = HashInts(c.AnalysisArrays);
            int[] structure = c.AnalysisArrays[1];
            Assert.True(c.Factorize());
            Assert.Equal(h1, HashDoubles(c.FactorValues)); Assert.Equal(a1, HashInts(c.AnalysisArrays));
            Assert.Same(structure, c.AnalysisArrays[1]);
            c.Clear();
            var d = RealGrid(30, 30, 2, 9);          // the same entries, added again to the cleared instance
            ulong s = 9; int nx = 30, ny = 30, bs = 2;
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = (y * nx + x) * bs;
                    for (int u = 0; u < bs; ++u) {
                        c.Add(a + u, a + u, 8.0 + (Next(ref s) % 64) / 64.0);
                        for (int v = 0; v < u; ++v) c.Add(a + u, a + v, -((Next(ref s) % 32) / 64.0));
                    }
                    foreach (var q in new[] { new[] { x + 1, y }, new[] { x, y + 1 }, new[] { x + 1, y + 1 } }) {
                        if (q[0] >= nx || q[1] >= ny) continue;
                        int b = (q[1] * nx + q[0]) * bs;
                        for (int u = 0; u < bs; ++u)
                            for (int v = 0; v < bs; ++v) c.Add(b + v, a + u, -((Next(ref s) % 128) / 128.0 + 0.25) / bs);
                    }
                }
            Assert.True(c.Factorize());
            Assert.Same(structure, c.AnalysisArrays[1]);
            Assert.Equal(h1, HashDoubles(c.FactorValues));
        }

        [Fact]
        public void TinyAndDiagonalMatrices_GiveTheExpectedFactor()
        {
            // one unknown, and a diagonal matrix (every unknown is a root of the elimination tree): L = sqrt(diagonal)
            var one = new SparseCholesky(1); one.Add(0, 0, 4.0);
            Assert.True(one.Factorize()); var x1 = new double[1]; one.Solve(new[] { 8.0 }, x1); Assert.Equal(2.0, x1[0], 12);
            var diag = new SparseCholesky(6, 2);
            for (int i = 0; i < 6; ++i) diag.Add(i, i, 4.0 * (i + 1));
            Assert.True(diag.Factorize());
            var b = new double[] { 4, 8, 12, 16, 20, 24 }; var x = new double[6]; diag.Solve(b, x);
            for (int i = 0; i < 6; ++i) Assert.Equal(1.0, x[i], 12);
            Assert.Equal(6, diag.FactorNonZeros);
            var hd = new SparseHermitianCholesky(3);
            for (int i = 0; i < 3; ++i) hd.Add(i, i, 9.0, 0);
            Assert.True(hd.Factorize()); var xh = new double[6]; hd.Solve(new double[] { 9, 18, 27, 36, 45, 54 }, xh);
            for (int i = 0; i < 6; ++i) Assert.Equal(i + 1.0, xh[i], 12);
        }
    }
}
