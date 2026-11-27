using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The TPMS field in the determinism proof scope (#1682; mesh_generators/TpmsLatticeField.cs, DeterministicApis.txt): the argument
    /// forms its site lines name, the error bound of the logistic weight, and pinned cross-platform hashes of the field and of the default
    /// lattices (every libm call of the field is a correctly rounded CoreMath one, so the same bits on every runtime).
    /// </summary>
    public class TpmsDeterminismTests
    {
        const double U = 1.1102230246251565e-16;   // 2^-53

        // ---- the weight of SigmoidBlend against a 256-bit reference ----

        const int Bits = 256;
        static readonly BigInteger One = BigInteger.One << Bits;

        // x = m 2^e exactly
        static void Decompose(double x, out BigInteger m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else frac |= 1L << 52;
            m = bits < 0 ? -(BigInteger)frac : frac;
            e = exp - 1075;
        }

        // round(x * 2^Bits) for x = m 2^e
        static BigInteger Fixed(BigInteger m, int e)
        {
            int sh = Bits + e;
            return sh >= 0 ? m << sh : DivRound(m, BigInteger.One << -sh);
        }

        static BigInteger DivRound(BigInteger a, BigInteger b)
        {
            BigInteger q = BigInteger.DivRem(a, b, out BigInteger r);
            if (2 * BigInteger.Abs(r) >= BigInteger.Abs(b)) q += (a.Sign * b.Sign);
            return q;
        }

        // exp(y) in fixed point (scale 2^Bits) for y given in fixed point, |y| <= 64: y / 2^12, a Taylor series, then 12 squarings
        static BigInteger ExpFixed(BigInteger y)
        {
            const int k = 12;
            BigInteger r = DivRound(y, BigInteger.One << k);
            BigInteger sum = One, term = One;
            for (int n = 1; n < 40; ++n) {
                term = DivRound(term * r, One * n);
                if (term.IsZero) break;
                sum += term;
            }
            for (int i = 0; i < k; ++i) sum = DivRound(sum * sum, One);
            return sum;
        }

        // |w - 1 / (1 + exp(-4 (p - offset) / width))| in units of u, with the exact rational eta = p - offset
        static double WeightError(double p, double offset, double width, double w)
        {
            Decompose(p, out BigInteger mp, out int ep);
            Decompose(offset, out BigInteger mo, out int eo);
            Decompose(width, out BigInteger mw, out int ew);
            int e0 = Math.Min(ep, eo);
            BigInteger eta = (mp << (ep - e0)) - (mo << (eo - e0));     // eta = eta' 2^e0 exactly
            // y = -4 eta / width = -4 eta' 2^(e0 - ew) / mw, in fixed point
            int sh = Bits + e0 - ew + 2;
            BigInteger num = -eta;
            BigInteger y = sh >= 0 ? DivRound(num << sh, mw) : DivRound(num, mw << -sh);
            BigInteger expY = ExpFixed(y);
            BigInteger wRef = DivRound(One * One, One + expY);      // 1 / (1 + exp(y)) in fixed point
            Decompose(w, out BigInteger mwv, out int ewv);
            BigInteger diff = BigInteger.Abs(Fixed(mwv, ewv) - wRef);
            return (double)diff / (double)One / U;
        }

        [Fact]
        public void SigmoidBlend_WeightError_IsWithinTheDerivedBound()
        {
            // EnclosureErrorBlend derives dw <= 0.448 u + 0.25 u + 2 u = 2.7 u for the logistic 1 / (1 + exp(-4 eta / width)) with the correctly
            // rounded exp; the error term of the Enclosure assumes dw <= 8 u. Measured against a 256-bit reference with the exact eta = p - offset,
            // over offsets and widths of many magnitudes (also |offset| much larger than the width) and |4 eta / width| up to 40.
            var rng = new Random(1682);
            double worst = 0;
            int n = 0;
            for (int i = 0; i < 4000; ++i) {
                double width = Math.Pow(2, rng.Next(-40, 41)) * (0.5 + rng.NextDouble());
                double offset = (rng.NextDouble() - 0.5) * Math.Pow(2, rng.Next(-30, 60));
                double z = (rng.NextDouble() - 0.5) * 20;          // 4 z in [-40, 40]
                double p = offset + z * width;
                if (double.IsInfinity(p) || !(Math.Abs(4 * (p - offset) / width) <= 44)) continue;   // (p rounded far from the asked z: skip, the reference exp is for |y| <= 64)
                TpmsBlendWeight blend = TpmsLattice.SigmoidBlend(1, offset, width);
                double w = blend.Weight(new Vector3d(0, p, 0));
                double err = WeightError(p, offset, width, w);
                Assert.True(err <= 2.7, $"offset {offset:R} width {width:R} p {p:R}: w {w:R}, error {err} u");
                worst = Math.Max(worst, err);
                ++n;
            }
            Assert.True(n > 3500);
            Assert.True(worst <= 2.7 && worst < 8);      // (measured: 1.46 u)
            // the ends: exp overflows to +inf (w = 0 exactly) or underflows to 0 (w = 1), never NaN
            TpmsBlendWeight b = TpmsLattice.SigmoidBlend(0, 0, 1);
            Assert.Equal(0.0, b.Weight(new Vector3d(-1e300, 0, 0)));
            Assert.Equal(1.0, b.Weight(new Vector3d(1e300, 0, 0)));
            Assert.Equal(0.5, b.Weight(Vector3d.Zero));
            Assert.Equal(0.0, b.Weight(new Vector3d(double.NegativeInfinity, 0, 0)));
            Assert.Equal(1.0, b.Weight(new Vector3d(double.PositiveInfinity, 0, 0)));
        }

        [Fact]
        public void LinearGradePhase_MatchesTheExactIntegral()
        {
            // the phase of LinearGrade inside the graded range is (top - z0) / period0 * log(1 + x) / x (Log1pOverX, now CoreMath.Log):
            // within a few ulps of the integral of 1 / period, for steep and gentle grades
            foreach (double p1 in new[] { 1.0000001, 1.001, 1.5, 3.0, 0.25 }) {
                TpmsPhaseGrade g = TpmsLattice.LinearGrade(2, 0, 1, 2, p1, 1);
                for (int i = 1; i <= 16; ++i) {
                    double z = 2.0 * i / 16;
                    double slope = (p1 - 1) / 2, x = slope * z;
                    // z log(1 + x) / x: by its series for small x (no cancellation), else Math.Log (log of 1 + x >= 1.25 or <= 0.75, well conditioned)
                    double r;
                    if (Math.Abs(x) < 0.25) {
                        r = 0; double term = 1;
                        for (int n = 0; n < 40; ++n) { r += term / (n + 1); term *= -x; }
                    } else r = Math.Log(1 + x) / x;
                    double exact = z * r;
                    double phase = g.Map(new Vector3d(0, 0, z)).z;
                    Assert.True(Math.Abs(phase - exact) <= 1e-14 * Math.Max(1, Math.Abs(exact)), $"period1 {p1}, z {z}: {phase:R} vs {exact:R}");
                }
            }
            Assert.Equal(1.0, TpmsLattice.Log1pOverX(0));
            Assert.Equal(1 - 1e-17 / 2, TpmsLattice.Log1pOverX(1e-17));
        }

        // ---- the forms of the site lines ----

        [Fact]
        public void QuantileTable_DoesNotDependOnTheOrderOfTheSort()
        {
            // P3: BuildTable sorts the sample values with the unstable Array.Sort. The premise: equal keys have equal bits (no two zeros of
            // opposite sign, no NaN), so any order of a sort is the same array; the table is then a function of the multiset. Checked here
            // with shuffles of the samples sorted by a different (stable) sort, and the table rebuilt from each.
            var rng = new Random(7);
            foreach (TpmsType type in Enum.GetValues(typeof(TpmsType)))
                foreach (TpmsStyle style in Enum.GetValues(typeof(TpmsStyle))) {
                    const int n = 16;
                    double step = 2 * Math.PI / n;
                    var values = new List<double>();
                    for (int i = 0; i < n; ++i)
                        for (int j = 0; j < n; ++j)
                            for (int k = 0; k < n; ++k) {
                                double f = TpmsLattice.Evaluate(type, (i + 0.5) * step, (j + 0.5) * step, (k + 0.5) * step);
                                double g = style == TpmsStyle.Sheet ? Math.Abs(f) : (style == TpmsStyle.SkeletalLow ? f : -f);
                                values.Add(g + 0.0);
                            }
                    Assert.DoesNotContain(values, v => double.IsNaN(v) || double.IsInfinity(v) || BitConverter.DoubleToInt64Bits(v) == long.MinValue);
                    double[] table = TpmsLattice.BuildTable(type, style, n);
                    for (int s = 0; s < 3; ++s) {
                        double[] shuffled = values.OrderBy(_ => rng.Next()).ToArray();
                        double[] sorted = shuffled.OrderBy(v => v).ToArray();      // a stable sort of another order
                        Assert.Equal(table.Select(BitConverter.DoubleToInt64Bits), Table(sorted).Select(BitConverter.DoubleToInt64Bits));
                    }
                }
        }

        // the interpolation of BuildTable over a sorted array (1024 knots)
        static double[] Table(double[] values)
        {
            int total = values.Length;
            var table = new double[1025];
            for (int m = 0; m <= 1024; ++m) {
                double pos = (double)m / 1024 * total - 0.5;
                if (pos <= 0) { table[m] = values[0]; continue; }
                if (pos >= total - 1) { table[m] = values[total - 1]; continue; }
                int lo = (int)pos;
                double t = pos - lo;
                table[m] = values[lo] * (1 - t) + values[lo + 1] * t;
            }
            return table;
        }

        [Fact]
        public void Caches_ConcurrentFirstCalls_GiveTheSerialValues()
        {
            // P4: the factories of the caches are serial functions of their key, so a value built on any thread (or twice, in a race) is the
            // value of a serial build: the cached tables equal fresh builds made on eight threads at once, and the gradient ratios are the
            // pinned values (the same on every runtime, see FieldHash_IsPinned)
            var keys = new List<(TpmsType, TpmsStyle)>();
            foreach (TpmsType type in Enum.GetValues(typeof(TpmsType)))
                foreach (TpmsStyle style in Enum.GetValues(typeof(TpmsStyle))) keys.Add((type, style));
            var fresh = new double[keys.Count][];
            Parallel.For(0, keys.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => fresh[i] = TpmsLattice.BuildTable(keys[i].Item1, keys[i].Item2, 64));
            var cached = new double[keys.Count][];
            Parallel.For(0, keys.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => cached[i] = TpmsLattice.QuantileTable(keys[i].Item1, keys[i].Item2));
            for (int i = 0; i < keys.Count; ++i)
                Assert.Equal(fresh[i].Select(BitConverter.DoubleToInt64Bits), cached[i].Select(BitConverter.DoubleToInt64Bits));
            var ratios = new double[6];
            Parallel.For(0, 6, i => ratios[i] = TpmsLattice.GradientRatio((TpmsType)i));
            for (int i = 0; i < 6; ++i)
                Assert.Equal(ratios[i], TpmsLattice.GradientRatio((TpmsType)i));
        }

        // ---- invariance and determinism ----

        [Fact]
        public void Field_InvarianceOverSimilarities()
        {
            // The field lives in the lattice frame: of a similarity only the scale reaches it (offset, width, periods and positions are lengths).
            // Under the power-of-two scales the weights of SigmoidBlend and the phases of LinearGrade are the same bits; under the other scales
            // they agree to a few ulps (the scaled inputs are rounded).
            var rng = new Random(11);
            var points = Enumerable.Range(0, 200).Select(_ => new Vector3d(rng.NextDouble() * 3 - 1, rng.NextDouble() * 3 - 1, rng.NextDouble() * 3 - 1)).ToArray();
            TpmsBlendWeight w0 = TpmsLattice.SigmoidBlend(0, 0.75, 0.5);
            TpmsPhaseGrade g0 = TpmsLattice.LinearGrade(2, 0, 1, 2, 1.25, 1.5);
            int exact = 0;
            foreach (Similarity s in Similarity.All()) {
                double k = s.Scale;
                TpmsBlendWeight w = TpmsLattice.SigmoidBlend(0, 0.75 * k, 0.5 * k);
                TpmsPhaseGrade g = TpmsLattice.LinearGrade(2, 0, 1 * k, 2 * k, 1.25 * k, 1.5 * k);
                if (s.IsExact) {
                    Assert.Equal(g0.JacobianBound / k, g.JacobianBound);
                    ++exact;
                }
                foreach (Vector3d p in points) {
                    double a = w0.Weight(p), b = w.Weight(k * p);
                    Vector3d pa = g0.Map(p), pb = g.Map(k * p);
                    if (s.IsExact) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(b));
                        Assert.Equal(pa, pb);
                    } else {
                        Assert.True(Math.Abs(a - b) <= 1e-14, s.Name + ": weight " + a + " vs " + b);
                        Assert.True((pa - pb).MaxAbs <= 1e-13 * Math.Max(1, pa.MaxAbs), s.Name + ": phase " + pa + " vs " + pb);
                    }
                }
            }
            Assert.True(exact >= 2);
        }

        static TpmsLattice Hybrid(bool parallel) => new TpmsLattice {
            Type = TpmsType.Gyroid, SecondType = TpmsType.IWP, Blend = TpmsLattice.SigmoidBlend(0, 1.0, 0.5), Style = TpmsStyle.SkeletalLow,
            CellSize = 1, Divisions = 12, VolumeFraction = 0.4, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 1, 1)), ParallelCompute = parallel
        };

        static TpmsLattice Graded(bool parallel) => new TpmsLattice {
            Type = TpmsType.SchwarzD, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 12, VolumeFraction = 0.3,
            Box = new AxisAlignedBox3d(new Vector3d(-0.75, -0.75, 0), new Vector3d(0.75, 0.75, 2)), Grade = TpmsLattice.LinearGrade(2, 0, 1, 2, 1.5, 1.25),
            ParallelCompute = parallel
        };

        [Fact]
        public void Lattice_ParallelEqualsSerial_AndRepeatedRuns()
        {
            // the node values (GridTrig, Volatile) and the delegate calls of the field: parallel equals serial, and a second Compute on one
            // instance and repeated parallel runs give the same mesh, for a hybrid (SigmoidBlend) and a graded lattice (LinearGrade)
            foreach (Func<bool, TpmsLattice> make in new Func<bool, TpmsLattice>[] { Hybrid, Graded }) {
                TpmsLattice serial = make(false);
                serial.Compute();
                Assert.True(serial.Status == TpmsStatus.Ok, serial.Status + ": " + serial.Message);
                Assert.Equal("", MeshRobustRepair.NotCleanReasons(serial.Mesh));
                string h = MeshHash(serial.Mesh);
                serial.Compute();
                Assert.Equal(h, MeshHash(serial.Mesh));
                for (int run = 0; run < 2; ++run) {
                    TpmsLattice par = make(true);
                    par.Compute();
                    Assert.Equal(TpmsStatus.Ok, par.Status);
                    Assert.Equal(h, MeshHash(par.Mesh));
                }
            }
        }

        // ---- pinned cross-platform hashes ----

        static string Sha(List<byte> bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
        }

        // the vertex bits and the triangles, in index order
        static string MeshHash(DMesh3 mesh)
        {
            var bytes = new List<byte>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                bytes.AddRange(BitConverter.GetBytes(vid));
                for (int k = 0; k < 3; ++k) bytes.AddRange(BitConverter.GetBytes(v[k]));
            }
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                bytes.AddRange(BitConverter.GetBytes(t.a)); bytes.AddRange(BitConverter.GetBytes(t.b)); bytes.AddRange(BitConverter.GetBytes(t.c));
            }
            return Sha(bytes);
        }

        [Fact]
        public void FieldHash_IsPinned()
        {
            // the field of every type at 12^3 phases, the quantile tables, the gradient ratios, the logistic weights and the graded phases:
            // pinned bits, the same on every runtime (CoreMath sin / cos / exp / log and IEEE + - * / sqrt only)
            var bytes = new List<byte>();
            foreach (TpmsType type in Enum.GetValues(typeof(TpmsType))) {
                for (int i = 0; i < 12; ++i)
                    for (int j = 0; j < 12; ++j)
                        for (int k = 0; k < 12; ++k)
                            bytes.AddRange(BitConverter.GetBytes(TpmsLattice.Evaluate(type, 0.37 * i - 1.3, 0.61 * j + 0.2, 0.83 * k - 4.1)));
                foreach (TpmsStyle style in Enum.GetValues(typeof(TpmsStyle)))
                    foreach (double v in TpmsLattice.QuantileTable(type, style)) bytes.AddRange(BitConverter.GetBytes(v));
                bytes.AddRange(BitConverter.GetBytes(TpmsLattice.GradientRatio(type)));
            }
            TpmsBlendWeight w = TpmsLattice.SigmoidBlend(0, 0.75, 0.5);
            TpmsPhaseGrade g = TpmsLattice.LinearGrade(2, 0, 1, 2, 1.25, 1.5);
            for (int i = -50; i <= 50; ++i) {
                var p = new Vector3d(0.0371 * i, 0.013 * i, 0.041 * i + 1);
                bytes.AddRange(BitConverter.GetBytes(w.Weight(p)));
                Vector3d ph = g.Map(p);
                for (int k = 0; k < 3; ++k) bytes.AddRange(BitConverter.GetBytes(ph[k]));
            }
            bytes.AddRange(BitConverter.GetBytes(g.JacobianBound));
            Assert.Equal("A5A4001AD0115189", Sha(bytes));
        }

        [Theory]
        [InlineData(TpmsType.Gyroid, "4F00EDB94B145E10")]
        [InlineData(TpmsType.SchwarzP, "FD3763207E060B85")]
        [InlineData(TpmsType.SchwarzD, "12C313F076B9CDAE")]
        [InlineData(TpmsType.Neovius, "08C8D681C2DECF9D")]
        [InlineData(TpmsType.IWP, "E0A81B927D1B7F03")]
        [InlineData(TpmsType.Lidinoid, "C907FE9AA8FAD0A6")]
        public void DefaultLattice_MeshHash_IsPinned(TpmsType type, string expected)
        {
            // the default lattice of each type (unit box, CellSize 1, Divisions 16, Sheet, VolumeFraction 0.3): the field is the same bits on every
            // runtime, and so is the mesh where the contouring is (a pinned hash; a failure on one platform names a platform-dependent step)
            var t = new TpmsLattice { Type = type };
            t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(t.Mesh));
            Assert.Equal(expected, MeshHash(t.Mesh));
        }

        [Fact]
        public void HybridAndGradedLattice_MeshHash_IsPinned()
        {
            var h = Hybrid(true); h.Compute();
            var g = Graded(true); g.Compute();
            Assert.Equal(TpmsStatus.Ok, h.Status);
            Assert.Equal(TpmsStatus.Ok, g.Status);
            Assert.Equal("AE82CC85598BB708", MeshHash(h.Mesh));
            Assert.Equal("F654A21511AE0B67", MeshHash(g.Mesh));
        }
    }
}
