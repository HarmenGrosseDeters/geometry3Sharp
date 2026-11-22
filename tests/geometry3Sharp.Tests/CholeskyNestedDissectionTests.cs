// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for opt-in <see cref="CholeskyOrdering.NestedDissection"/> in <see cref="SparseCholesky"/> and
    /// <see cref="SparseHermitianCholesky"/>: residual accuracy, permutation validity, bit-identical determinism across
    /// thread schedules, reuse/switching, adversarial topologies, scaling/order invariance, and fill reduction.
    /// </summary>
    public class CholeskyNestedDissectionTests
    {
        static ulong Next(ref ulong s) { s = s * 6364136223846793005UL + 1442695040888963407UL; return s >> 11; }

        static double[] MakeRhs(int n, ulong seed)
        {
            ulong s = seed;
            var b = new double[n];
            for (int i = 0; i < n; ++i) b[i] = ((double)(Next(ref s) % 2001) - 1000.0) / 64.0;
            return b;
        }

        static double RelativeResidualReal(int n, List<(int r, int c, double v)> entries, double[] b, double[] x)
        {
            var ax = new double[n];
            for (int e = 0; e < entries.Count; ++e) {
                var (r, c, v) = entries[e];
                ax[r] += v * x[c];
                if (r != c) ax[c] += v * x[r];
            }
            double rn = 0, bn = 0;
            for (int i = 0; i < n; ++i) {
                Assert.False(double.IsNaN(x[i]) || double.IsInfinity(x[i]));
                double d = ax[i] - b[i];
                rn += d * d;
                bn += b[i] * b[i];
            }
            return bn == 0 ? Math.Sqrt(rn) : Math.Sqrt(rn / bn);
        }

        static double RelativeResidualHerm(int n, List<(int r, int c, double re, double im)> entries, double[] b, double[] x)
        {
            var ax = new double[2 * n];
            for (int e = 0; e < entries.Count; ++e) {
                var (r, c, vr, vi) = entries[e];
                if (r < c) { int t = r; r = c; c = t; vi = -vi; }
                double xcr = x[2 * c], xci = x[2 * c + 1];
                ax[2 * r] += vr * xcr - vi * xci;
                ax[2 * r + 1] += vr * xci + vi * xcr;
                if (r != c) {
                    double xrr = x[2 * r], xri = x[2 * r + 1];
                    ax[2 * c] += vr * xrr + vi * xri;
                    ax[2 * c + 1] += vr * xri - vi * xrr;
                }
            }
            double rn = 0, bn = 0;
            for (int i = 0; i < 2 * n; ++i) {
                Assert.False(double.IsNaN(x[i]) || double.IsInfinity(x[i]));
                double d = ax[i] - b[i];
                rn += d * d;
                bn += b[i] * b[i];
            }
            return bn == 0 ? Math.Sqrt(rn) : Math.Sqrt(rn / bn);
        }

        static List<(int r, int c, double v)> RealGridEntries(int nx, int ny, int bs, ulong seed)
        {
            ulong s = seed;
            var list = new List<(int, int, double)>();
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = (y * nx + x) * bs;
                    for (int u = 0; u < bs; ++u) {
                        list.Add((a + u, a + u, 8.0 + (Next(ref s) % 64) / 64.0));
                        for (int v = 0; v < u; ++v) list.Add((a + u, a + v, -((Next(ref s) % 32) / 64.0)));
                    }
                    int[] dx = { 1, 0, 1 };
                    int[] dy = { 0, 1, 1 };
                    for (int d = 0; d < 3; ++d) {
                        int qx = x + dx[d], qy = y + dy[d];
                        if (qx >= nx || qy >= ny) continue;
                        int b = (qy * nx + qx) * bs;
                        for (int u = 0; u < bs; ++u)
                            for (int v = 0; v < bs; ++v)
                                list.Add((b + v, a + u, -((Next(ref s) % 128) / 128.0 + 0.25) / bs));
                    }
                }
            return list;
        }

        static List<(int r, int c, double v)> RealGrid3Entries(int k, ulong seed)
        {
            ulong s = seed;
            var list = new List<(int, int, double)>();
            for (int z = 0; z < k; ++z)
                for (int y = 0; y < k; ++y)
                    for (int x = 0; x < k; ++x) {
                        int a = (z * k + y) * k + x;
                        list.Add((a, a, 9.0 + (Next(ref s) % 16) / 16.0));
                        if (x + 1 < k) list.Add((a + 1, a, -(double)(1 + Next(ref s) % 8) / 8.0));
                        if (y + 1 < k) list.Add((a + k, a, -(double)(1 + Next(ref s) % 8) / 8.0));
                        if (z + 1 < k) list.Add((a + k * k, a, -(double)(1 + Next(ref s) % 8) / 8.0));
                    }
            return list;
        }

        static List<(int r, int c, double re, double im)> ComplexGridEntries(int nx, int ny, ulong seed)
        {
            ulong s = seed;
            var list = new List<(int, int, double, double)>();
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = y * nx + x;
                    list.Add((a, a, 8.0 + (Next(ref s) % 64) / 64.0, 0.0));
                    int[] dx = { 1, 0, 1 };
                    int[] dy = { 0, 1, 1 };
                    for (int d = 0; d < 3; ++d) {
                        int qx = x + dx[d], qy = y + dy[d];
                        if (qx >= nx || qy >= ny) continue;
                        int b = qy * nx + qx;
                        double re = -((Next(ref s) % 128) / 128.0 + 0.25);
                        double im = ((Next(ref s) % 64) / 64.0 - 0.5);
                        if (Next(ref s) % 2 == 0) list.Add((b, a, re, im));
                        else list.Add((a, b, re, -im));
                    }
                }
            return list;
        }

        static void AssertValidPermutation(int[] perm, int n)
        {
            Assert.Equal(n, perm.Length);
            bool[] seen = new bool[n];
            for (int i = 0; i < n; ++i) {
                int v = perm[i];
                Assert.InRange(v, 0, n - 1);
                Assert.False(seen[v], "duplicate entry in Perm: " + v);
                seen[v] = true;
            }
        }

        static void AssertSameBits(double[] a, double[] b, string label)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i)
                Assert.True(BitConverter.DoubleToInt64Bits(a[i]) == BitConverter.DoubleToInt64Bits(b[i]),
                    $"{label}: index {i} differs ({a[i]:R} vs {b[i]:R})");
        }

        [Theory]
        [InlineData(24, 24, 1)]
        [InlineData(24, 24, 2)]
        [InlineData(18, 18, 3)]
        [InlineData(10, 8, 1)]
        public void RealGrid_NestedDissection_AccurateResidualAndValidPermutation(int nx, int ny, int bs)
        {
            int n = nx * ny * bs;
            var entries = RealGridEntries(nx, ny, bs, (ulong)(nx * 31 + bs));
            var c = new SparseCholesky(n, bs) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in entries) c.Add(r, col, v);
            Assert.True(c.Factorize(), c.Error);
            Assert.True(c.IsValid);
            AssertValidPermutation(c.AnalysisArrays[0], n);

            double[] b = MakeRhs(n, 42);
            double[] x = new double[n];
            c.Solve(b, x);
            double res = RelativeResidualReal(n, entries, b, x);
            Assert.True(res <= 1e-12, $"residual too large: {res:E3}");
        }

        [Fact]
        public void Real3DGrid_NestedDissection_ReducesFillAndFlopsOverMinimumDegree()
        {
            // 18^3 = 5832 unknowns (past the small-graph crossover): nested dissection must beat minimum degree
            // on both nnz(L) and numeric flops while preserving machine-precision accuracy.
            int k = 18, n = k * k * k;
            var entries = RealGrid3Entries(k, 17);

            var amd = new SparseCholesky(n);
            var nd = new SparseCholesky(n) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in entries) { amd.Add(r, col, v); nd.Add(r, col, v); }

            Assert.True(amd.Factorize(), amd.Error);
            Assert.True(nd.Factorize(), nd.Error);

            long flopsAmd = ComputeFlops(amd.AnalysisArrays[1]);
            long flopsNd = ComputeFlops(nd.AnalysisArrays[1]);
            Assert.True(nd.FactorNonZeros < amd.FactorNonZeros,
                $"expected ND nnz ({nd.FactorNonZeros}) < AMD nnz ({amd.FactorNonZeros})");
            Assert.True(flopsNd < flopsAmd,
                $"expected ND flops ({flopsNd}) < AMD flops ({flopsAmd})");

            double[] b = MakeRhs(n, 99);
            double[] x = new double[n];
            nd.Solve(b, x);
            Assert.True(RelativeResidualReal(n, entries, b, x) <= 1e-12);
        }

        static long ComputeFlops(int[] lp)
        {
            long flops = 0;
            for (int j = 0; j < lp.Length - 1; ++j) {
                long c = lp[j + 1] - lp[j];
                flops += c * c;
            }
            return flops;
        }

        [Fact]
        public void HermitianGrid_NestedDissection_AccurateResidual()
        {
            int nx = 26, ny = 24, n = nx * ny;
            var entries = ComplexGridEntries(nx, ny, 77);
            var c = new SparseHermitianCholesky(n) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, re, im) in entries) c.Add(r, col, re, im);
            Assert.True(c.Factorize(), c.Error);
            AssertValidPermutation(c.AnalysisArrays[0], n);

            double[] b = MakeRhs(2 * n, 123);
            double[] x = new double[2 * n];
            c.Solve(b, x);
            double res = RelativeResidualHerm(n, entries, b, x);
            Assert.True(res <= 1e-12, $"Hermitian residual too large: {res:E3}");
        }

        [Fact]
        public void ClosedSurfaceMeshLaplacian_And_BiharmonicStencil_AccurateResidual()
        {
            // Procedural closed sphere mesh (~600 vertices)
            var gen = new Sphere3Generator_NormalizedCube { Radius = 1.0, EdgeVertices = 10 };
            gen.Generate();
            DMesh3 mesh = gen.MakeDMesh();
            int nv = mesh.MaxVertexID;
            var meshEntries = new List<(int, int, double)>();
            var deg = new int[nv];
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                meshEntries.Add((Math.Max(ev.a, ev.b), Math.Min(ev.a, ev.b), -1.0));
                deg[ev.a]++; deg[ev.b]++;
            }
            for (int i = 0; i < nv; ++i) meshEntries.Add((i, i, deg[i] + 1.0));

            var cs = new SparseCholesky(nv) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in meshEntries) cs.Add(r, col, v);
            Assert.True(cs.Factorize(), cs.Error);
            double[] bm = MakeRhs(nv, 5);
            double[] xm = new double[nv];
            cs.Solve(bm, xm);
            Assert.True(RelativeResidualReal(nv, meshEntries, bm, xm) <= 1e-12);

            // 2-ring biharmonic stencil on 22x22 grid
            int nx = 22, ny = 22, nb = nx * ny;
            var bihEntries = new List<(int, int, double)>();
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = y * nx + x;
                    bihEntries.Add((a, a, 25.0));
                    for (int dy = -2; dy <= 2; ++dy)
                        for (int dx = -2; dx <= 2; ++dx) {
                            if (dx == 0 && dy == 0) continue;
                            if (Math.Abs(dx) + Math.Abs(dy) > 3) continue;
                            int qx = x + dx, qy = y + dy;
                            if (qx < 0 || qx >= nx || qy < 0 || qy >= ny) continue;
                            int b = qy * nx + qx;
                            if (b <= a) continue;
                            bihEntries.Add((b, a, (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) ? -1.0 : 0.25));
                        }
                }
            var cb = new SparseCholesky(nb) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in bihEntries) cb.Add(r, col, v);
            Assert.True(cb.Factorize(), cb.Error);
            double[] bb = MakeRhs(nb, 9);
            double[] xb = new double[nb];
            cb.Solve(bb, xb);
            Assert.True(RelativeResidualReal(nb, bihEntries, bb, xb) <= 1e-12);
        }

        [Fact]
        public void Determinism_AcrossSerialParallelAndAdversarialSchedules_BitIdentical()
        {
            // Grid large enough (28x28 = 784 blocks, bs=2 -> n=1568) to exercise patch quotient graph,
            // parallel ND levels, and parallel leaf Halo-AMD.
            int nx = 28, ny = 28, bs = 2, n = nx * ny * bs;
            var entries = RealGridEntries(nx, ny, bs, 314);
            double[] b = MakeRhs(n, 271);

            SparseCholesky RunWith(bool serialAll, int serialOrder)
            {
                gParallel.SerialOnThisThread = serialAll;
                gParallel.SerialOrderOnThisThread = serialOrder;
                try {
                    var c = new SparseCholesky(n, bs) {
                        Ordering = CholeskyOrdering.NestedDissection,
                        ForceSerialNumeric = serialAll,
                        ForceSerialSolve = serialAll,
                        NumericMinWork = 0,
                        SolveMinWork = 0
                    };
                    foreach (var (r, col, v) in entries) c.Add(r, col, v);
                    Assert.True(c.Factorize(), c.Error);
                    return c;
                } finally {
                    gParallel.SerialOnThisThread = false;
                    gParallel.SerialOrderOnThisThread = 0;
                }
            }

            var refSerial = RunWith(true, 0);
            var refX = new double[n];
            refSerial.Solve(b, refX);

            // Adversarial schedule orders under SerialOnThisThread (-1 reversed, 7 and 99 permuted)
            foreach (int order in new[] { -1, 7, 99 }) {
                var adv = RunWith(true, order);
                for (int k = 0; k < refSerial.AnalysisArrays.Length; ++k)
                    Assert.Equal(refSerial.AnalysisArrays[k], adv.AnalysisArrays[k]);
                AssertSameBits(refSerial.FactorValues, adv.FactorValues, "adv factor order=" + order);
                var x = new double[n];
                adv.Solve(b, x);
                AssertSameBits(refX, x, "adv solve order=" + order);
            }

            // Repeated real multi-threaded runs
            for (int rep = 0; rep < 3; ++rep) {
                var par = RunWith(false, 0);
                for (int k = 0; k < refSerial.AnalysisArrays.Length; ++k)
                    Assert.Equal(refSerial.AnalysisArrays[k], par.AnalysisArrays[k]);
                AssertSameBits(refSerial.FactorValues, par.FactorValues, "par factor rep=" + rep);
                var x = new double[n];
                par.Solve(b, x);
                AssertSameBits(refX, x, "par solve rep=" + rep);
            }

            // Hermitian determinism under parallel vs serial
            int nh = 28 * 28;
            var hentries = ComplexGridEntries(28, 28, 55);
            double[] bh = MakeRhs(2 * nh, 88);

            gParallel.SerialOnThisThread = true;
            SparseHermitianCholesky hSer;
            double[] hSerX = new double[2 * nh];
            try {
                hSer = new SparseHermitianCholesky(nh) {
                    Ordering = CholeskyOrdering.NestedDissection,
                    ForceSerialNumeric = true,
                    ForceSerialSolve = true
                };
                foreach (var (r, col, re, im) in hentries) hSer.Add(r, col, re, im);
                Assert.True(hSer.Factorize(), hSer.Error);
                hSer.Solve(bh, hSerX);
            } finally {
                gParallel.SerialOnThisThread = false;
            }

            var hPar = new SparseHermitianCholesky(nh) {
                Ordering = CholeskyOrdering.NestedDissection,
                NumericMinWork = 0,
                SolveMinWork = 0
            };
            foreach (var (r, col, re, im) in hentries) hPar.Add(r, col, re, im);
            Assert.True(hPar.Factorize(), hPar.Error);
            var hParX = new double[2 * nh];
            hPar.Solve(bh, hParX);

            for (int k = 0; k < hSer.AnalysisArrays.Length; ++k)
                Assert.Equal(hSer.AnalysisArrays[k], hPar.AnalysisArrays[k]);
            AssertSameBits(hSer.FactorValues[0], hPar.FactorValues[0], "herm re");
            AssertSameBits(hSer.FactorValues[1], hPar.FactorValues[1], "herm im");
            AssertSameBits(hSerX, hParX, "herm solve");
        }

        [Fact]
        public void ReuseAndOrderingSwitch_ReanalyzesOnOrderingChange_ReusesWhenUnchanged()
        {
            int nx = 22, ny = 22, bs = 2, n = nx * ny * bs;
            var entries = RealGridEntries(nx, ny, bs, 19);
            double[] b = MakeRhs(n, 23);

            var c = new SparseCholesky(n, bs);
            Assert.Equal(CholeskyOrdering.MinimumDegree, c.Ordering);
            foreach (var (r, col, v) in entries) c.Add(r, col, v);
            Assert.True(c.Factorize());
            int[] amdPerm = (int[])c.AnalysisArrays[0].Clone();
            double[] amdFactor = (double[])c.FactorValues.Clone();
            double[] amdX = new double[n];
            c.Solve(b, amdX);

            // Switch Ordering on same pattern -> must re-analyze (Perm changes, solution accurate).
            c.Ordering = CholeskyOrdering.NestedDissection;
            Assert.True(c.Factorize());
            int[] ndLpRef = c.AnalysisArrays[1];
            double[] ndFactor = (double[])c.FactorValues.Clone();
            Assert.NotEqual(amdPerm, c.AnalysisArrays[0]);
            double[] ndX = new double[n];
            c.Solve(b, ndX);
            Assert.True(RelativeResidualReal(n, entries, b, ndX) <= 1e-12);

            // Refill with same pattern and same Ordering -> must reuse analysis arrays and match bits.
            c.Clear();
            foreach (var (r, col, v) in entries) c.Add(r, col, v);
            Assert.True(c.Factorize());
            Assert.Same(ndLpRef, c.AnalysisArrays[1]);
            AssertSameBits(ndFactor, c.FactorValues, "reused ND factor");

            // Switch back to MinimumDegree -> must restore exact bit-identical AMD factor and solution.
            c.Ordering = CholeskyOrdering.MinimumDegree;
            Assert.True(c.Factorize());
            Assert.Equal(amdPerm, c.AnalysisArrays[0]);
            AssertSameBits(amdFactor, c.FactorValues, "restored AMD factor");
            double[] amdX2 = new double[n];
            c.Solve(b, amdX2);
            AssertSameBits(amdX, amdX2, "restored AMD solve");

            // Same toggle on SparseHermitianCholesky: exercises schedule/plan/colCopy invalidation across ordering switches.
            int nh = 20 * 20;
            var hEntries = ComplexGridEntries(20, 20, 31);
            double[] hb = MakeRhs(2 * nh, 37);
            var hc = new SparseHermitianCholesky(nh);
            Assert.Equal(CholeskyOrdering.MinimumDegree, hc.Ordering);
            foreach (var (r, col, re, im) in hEntries) hc.Add(r, col, re, im);
            Assert.True(hc.Factorize());
            int[] hAmdPerm = (int[])hc.AnalysisArrays[0].Clone();
            double[] hAmdX = new double[2 * nh];
            hc.Solve(hb, hAmdX);

            hc.Ordering = CholeskyOrdering.NestedDissection;
            Assert.True(hc.Factorize());
            Assert.NotEqual(hAmdPerm, hc.AnalysisArrays[0]);
            double[] hNdX = new double[2 * nh];
            hc.Solve(hb, hNdX);
            Assert.True(RelativeResidualHerm(nh, hEntries, hb, hNdX) <= 1e-12);

            hc.Ordering = CholeskyOrdering.MinimumDegree;
            Assert.True(hc.Factorize());
            Assert.Equal(hAmdPerm, hc.AnalysisArrays[0]);
            double[] hAmdX2 = new double[2 * nh];
            hc.Solve(hb, hAmdX2);
            AssertSameBits(hAmdX, hAmdX2, "restored Hermitian AMD solve");
        }

        [Fact]
        public void EdgeTopologies_TinyDiagonalDisconnectedStarCliquePath_And_NonSpdFailure()
        {
            // N = 0 and N = 1
            var empty = new SparseCholesky(0) { Ordering = CholeskyOrdering.NestedDissection };
            Assert.True(empty.Factorize());
            var one = new SparseCholesky(1) { Ordering = CholeskyOrdering.NestedDissection };
            one.Add(0, 0, 9.0);
            Assert.True(one.Factorize());
            var x1 = new double[1]; one.Solve(new[] { 18.0 }, x1);
            Assert.Equal(2.0, x1[0], 12);

            var hEmpty = new SparseHermitianCholesky(0) { Ordering = CholeskyOrdering.NestedDissection };
            Assert.True(hEmpty.Factorize());

            // Pure diagonal matrix larger than PatchUseThreshold
            int nDiag = 400;
            var diag = new SparseCholesky(nDiag) { Ordering = CholeskyOrdering.NestedDissection };
            for (int i = 0; i < nDiag; ++i) diag.Add(i, i, 2.0 + i);
            Assert.True(diag.Factorize());
            Assert.Equal(nDiag, diag.FactorNonZeros);
            var bDiag = new double[nDiag]; var xDiag = new double[nDiag];
            for (int i = 0; i < nDiag; ++i) bDiag[i] = (2.0 + i) * 3.0;
            diag.Solve(bDiag, xDiag);
            for (int i = 0; i < nDiag; ++i) Assert.Equal(3.0, xDiag[i], 12);

            // Disconnected components: 2 isolated + 25-node chain + two 16x16 grids
            int off1 = 2, off2 = off1 + 25, gSize = 16 * 16, off3 = off2 + gSize, nDisc = off3 + gSize;
            var discEntries = new List<(int, int, double)>();
            discEntries.Add((0, 0, 5.0));
            discEntries.Add((1, 1, 7.0));
            for (int i = 0; i < 25; ++i) {
                discEntries.Add((off1 + i, off1 + i, 4.0));
                if (i + 1 < 25) discEntries.Add((off1 + i + 1, off1 + i, -1.0));
            }
            foreach (int off in new[] { off2, off3 }) {
                foreach (var (r, col, v) in RealGridEntries(16, 16, 1, (ulong)off))
                    discEntries.Add((off + r, off + col, v));
            }
            var disc = new SparseCholesky(nDisc) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in discEntries) disc.Add(r, col, v);
            Assert.True(disc.Factorize(), disc.Error);
            double[] bDisc = MakeRhs(nDisc, 11); double[] xDisc = new double[nDisc];
            disc.Solve(bDisc, xDisc);
            Assert.True(RelativeResidualReal(nDisc, discEntries, bDisc, xDisc) <= 1e-12);

            // Star graph (hub 0 connected to 299 leaves): exact 1-node separator -> O(n) nonzeros
            int nStar = 300;
            var starEntries = new List<(int, int, double)>();
            starEntries.Add((0, 0, nStar + 10.0));
            for (int i = 1; i < nStar; ++i) {
                starEntries.Add((i, i, 4.0));
                starEntries.Add((i, 0, -0.5));
            }
            var star = new SparseCholesky(nStar) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in starEntries) star.Add(r, col, v);
            Assert.True(star.Factorize(), star.Error);
            Assert.True(star.FactorNonZeros <= 3 * nStar, "star graph should have near-zero fill: " + star.FactorNonZeros);
            double[] bStar = MakeRhs(nStar, 33); double[] xStar = new double[nStar];
            star.Solve(bStar, xStar);
            Assert.True(RelativeResidualReal(nStar, starEntries, bStar, xStar) <= 1e-12);

            // Complete clique K_180 (> LeafThreshold): triggers dense fallback cleanly
            int nClique = 180;
            var cliqueEntries = new List<(int, int, double)>();
            for (int i = 0; i < nClique; ++i) {
                cliqueEntries.Add((i, i, nClique + 5.0));
                for (int j = 0; j < i; ++j) cliqueEntries.Add((i, j, 0.25));
            }
            var clique = new SparseCholesky(nClique) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in cliqueEntries) clique.Add(r, col, v);
            Assert.True(clique.Factorize(), clique.Error);
            double[] bClique = MakeRhs(nClique, 44); double[] xClique = new double[nClique];
            clique.Solve(bClique, xClique);
            Assert.True(RelativeResidualReal(nClique, cliqueEntries, bClique, xClique) <= 1e-12);

            // 1D path graph P_400: thin 1-node separators
            int nPath = 400;
            var pathEntries = new List<(int, int, double)>();
            for (int i = 0; i < nPath; ++i) {
                pathEntries.Add((i, i, 4.0));
                if (i + 1 < nPath) pathEntries.Add((i + 1, i, -1.0));
            }
            var path = new SparseCholesky(nPath) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in pathEntries) path.Add(r, col, v);
            Assert.True(path.Factorize(), path.Error);
            Assert.True(path.FactorNonZeros <= 4 * nPath);
            double[] bPath = MakeRhs(nPath, 55); double[] xPath = new double[nPath];
            path.Solve(bPath, xPath);
            Assert.True(RelativeResidualReal(nPath, pathEntries, bPath, xPath) <= 1e-12);

            // Non-SPD indefinite matrix must fail cleanly with IsValid == false and Error != null
            var badReal = new SparseCholesky(200) { Ordering = CholeskyOrdering.NestedDissection };
            for (int i = 0; i < 200; ++i) {
                badReal.Add(i, i, i == 100 ? -4.0 : 4.0);
                if (i + 1 < 200) badReal.Add(i + 1, i, -1.0);
            }
            Assert.False(badReal.Factorize());
            Assert.False(badReal.IsValid);
            Assert.False(string.IsNullOrEmpty(badReal.Error));

            var badHerm = new SparseHermitianCholesky(200) { Ordering = CholeskyOrdering.NestedDissection };
            for (int i = 0; i < 200; ++i) {
                badHerm.Add(i, i, i == 50 ? -2.0 : 4.0, 0.0);
                if (i + 1 < 200) badHerm.Add(i + 1, i, -1.0, 0.1);
            }
            Assert.False(badHerm.Factorize());
            Assert.False(badHerm.IsValid);
            Assert.False(string.IsNullOrEmpty(badHerm.Error));
        }

        [Fact]
        public void InputOrderAndPowerOfTwoScale_Invariance_And_SolveLogCompatibility()
        {
            int nx = 20, ny = 20, bs = 2, n = nx * ny * bs;
            var entries = RealGridEntries(nx, ny, bs, 101);
            double[] b = MakeRhs(n, 202);

            // 1) Insertion order invariance: adding entries in reverse order produces bit-identical Perm, Lp, Li.
            var fwd = new SparseCholesky(n, bs) { Ordering = CholeskyOrdering.NestedDissection };
            foreach (var (r, col, v) in entries) fwd.Add(r, col, v);
            Assert.True(fwd.Factorize());

            var rev = new SparseCholesky(n, bs) { Ordering = CholeskyOrdering.NestedDissection };
            for (int i = entries.Count - 1; i >= 0; --i) {
                var (r, col, v) = entries[i];
                rev.Add(col, r, v); // also swap (r, c)
            }
            Assert.True(rev.Factorize());
            Assert.Equal(fwd.AnalysisArrays[0], rev.AnalysisArrays[0]);
            Assert.Equal(fwd.AnalysisArrays[1], rev.AnalysisArrays[1]);
            Assert.Equal(fwd.AnalysisArrays[2], rev.AnalysisArrays[2]);

            // 2) Power-of-two scaling invariance: (2^k A) x = (2^k b) yields bit-identical x and symbolic structure.
            double[] xRef = new double[n];
            fwd.Solve(b, xRef);
            foreach (int exp in new[] { -12, 12 }) {
                double scale = Math.Pow(2.0, exp);
                var scaled = new SparseCholesky(n, bs) { Ordering = CholeskyOrdering.NestedDissection };
                foreach (var (r, col, v) in entries) scaled.Add(r, col, v * scale);
                Assert.True(scaled.Factorize());
                Assert.Equal(fwd.AnalysisArrays[0], scaled.AnalysisArrays[0]);
                double[] bScaled = new double[n];
                for (int i = 0; i < n; ++i) bScaled[i] = b[i] * scale;
                double[] xScaled = new double[n];
                scaled.Solve(bScaled, xScaled);
                AssertSameBits(xRef, xScaled, "2^" + exp + " scaling");
            }

            // 3) SolveLog compatibility on an M-matrix (bs=1, positive diagonal, non-positive off-diagonals)
            int nm = 20 * 20;
            var mSolver = new SparseCholesky(nm) { Ordering = CholeskyOrdering.NestedDissection };
            for (int y = 0; y < 20; ++y)
                for (int x = 0; x < 20; ++x) {
                    int a = y * 20 + x;
                    mSolver.Add(a, a, 6.0);
                    if (x + 1 < 20) mSolver.Add(a + 1, a, -1.0);
                    if (y + 1 < 20) mSolver.Add(a + 20, a, -1.0);
                }
            Assert.True(mSolver.Factorize());
            var bPos = new double[nm]; var logB = new double[nm];
            for (int i = 0; i < nm; ++i) { bPos[i] = 1.0 + (i % 5); logB[i] = Math.Log(bPos[i]); }
            var xLin = new double[nm]; var logX = new double[nm];
            mSolver.Solve(bPos, xLin);
            Assert.True(mSolver.SolveLog(logB, logX));
            for (int i = 0; i < nm; ++i)
                Assert.True(Math.Abs(Math.Exp(logX[i]) - xLin[i]) <= 1e-12 * Math.Abs(xLin[i]));
        }
    }
}
