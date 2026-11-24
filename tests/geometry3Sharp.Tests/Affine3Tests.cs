using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class Affine3Tests
    {
        static bool Contains(ExactInterval iv, ExactDyadic v)
        {
            if (double.IsNegativeInfinity(iv.Lo) && double.IsPositiveInfinity(iv.Hi))
                return true;
            if (!double.IsNegativeInfinity(iv.Lo) && ExactDyadic.Compare(ExactDyadic.Of(iv.Lo), v) > 0)
                return false;
            if (!double.IsPositiveInfinity(iv.Hi) && ExactDyadic.Compare(v, ExactDyadic.Of(iv.Hi)) > 0)
                return false;
            return true;
        }

        static ExactDyadic EvalLinear(Affine3 a, ExactDyadic ux, ExactDyadic uy, ExactDyadic uz)
        {
            return ExactDyadic.Of(a.C0)
                 + ExactDyadic.Of(a.Cx) * ux
                 + ExactDyadic.Of(a.Cy) * uy
                 + ExactDyadic.Of(a.Cz) * uz;
        }

        static bool EnclosesPoint(Affine3 a, ExactDyadic ux, ExactDyadic uy, ExactDyadic uz, ExactDyadic exactVal)
        {
            if (!a.IsFinite) return true;
            ExactDyadic center = EvalLinear(a, ux, uy, uz);
            ExactDyadic diff = exactVal - center;
            ExactDyadic absDiff = diff.Sign < 0 ? -diff : diff;
            return ExactDyadic.Compare(absDiff, ExactDyadic.Of(a.Rad)) <= 0;
        }

        [Fact]
        public void Affine3_AlgebraicOperations_SoundAgainstExactDyadic()
        {
            var rng = new Random(42);
            double[] scales = { 1.0, 1e-12, 1e12, 1e-140, 1e70, 1e-300 };
            for (int trial = 0; trial < 200; ++trial) {
                double sc = scales[trial % scales.Length];
                double x0 = (rng.NextDouble() * 4 - 2) * sc;
                double y0 = (rng.NextDouble() * 4 - 2) * sc;
                double z0 = (rng.NextDouble() * 4 - 2) * sc;
                double wx = rng.NextDouble() * 0.25 * sc;
                double wy = rng.NextDouble() * 0.25 * sc;
                double wz = rng.NextDouble() * 0.25 * sc;

                ExactInterval ix = new ExactInterval(x0, x0 + wx);
                ExactInterval iy = new ExactInterval(y0, y0 + wy);
                ExactInterval iz = new ExactInterval(z0, z0 + wz);

                Affine3 ax = Affine3.FromAxis(ix, 0);
                Affine3 ay = Affine3.FromAxis(iy, 1);
                Affine3 az = Affine3.FromAxis(iz, 2);

                double k = rng.NextDouble() * 5 - 2.5;
                double d = (rng.NextDouble() * 4 - 2) * sc;

                Affine3 sum = ax + ay;
                Affine3 diff = ax - az;
                Affine3 neg = -ax;
                Affine3 shifted = ax.AddConst(d);
                Affine3 scaled = (ax + ay).Scale(k);
                Affine3 prod = (ax + ay) * (ay - az);
                Affine3 sq = Affine3.Square(ax - ay + az);

                ExactInterval sumIv = sum.ToInterval();
                ExactInterval diffIv = diff.ToInterval();
                ExactInterval negIv = neg.ToInterval();
                ExactInterval shiftedIv = shifted.ToInterval();
                ExactInterval scaledIv = scaled.ToInterval();
                ExactInterval prodIv = prod.ToInterval();
                ExactInterval sqIv = sq.ToInterval();

                // Sample corners and interior points in [-1, 1]^3 using exact dyadic parameters u in {-1, -0.5, 0, 0.5, 1}
                double[] us = { -1.0, -0.5, 0.0, 0.5, 1.0 };
                for (int s = 0; s < 5; ++s) {
                    ExactDyadic ux = ExactDyadic.Of(us[(trial + s) % 5]);
                    ExactDyadic uy = ExactDyadic.Of(us[(trial * 3 + s + 1) % 5]);
                    ExactDyadic uz = ExactDyadic.Of(us[(trial * 7 + s + 2) % 5]);

                    ExactDyadic vx = ExactDyadic.Of(ax.C0) + ExactDyadic.Of(ax.Cx) * ux;
                    ExactDyadic vy = ExactDyadic.Of(ay.C0) + ExactDyadic.Of(ay.Cy) * uy;
                    ExactDyadic vz = ExactDyadic.Of(az.C0) + ExactDyadic.Of(az.Cz) * uz;

                    ExactDyadic eSum = vx + vy;
                    ExactDyadic eDiff = vx - vz;
                    ExactDyadic eNeg = -vx;
                    ExactDyadic eShifted = vx + ExactDyadic.Of(d);
                    ExactDyadic eScaled = (vx + vy) * ExactDyadic.Of(k);
                    ExactDyadic eProd = (vx + vy) * (vy - vz);
                    ExactDyadic eSq = (vx - vy + vz) * (vx - vy + vz);

                    Assert.True(EnclosesPoint(sum, ux, uy, uz, eSum));
                    Assert.True(EnclosesPoint(diff, ux, uy, uz, eDiff));
                    Assert.True(EnclosesPoint(neg, ux, uy, uz, eNeg));
                    Assert.True(EnclosesPoint(shifted, ux, uy, uz, eShifted));
                    Assert.True(EnclosesPoint(scaled, ux, uy, uz, eScaled));
                    Assert.True(EnclosesPoint(prod, ux, uy, uz, eProd));
                    Assert.True(EnclosesPoint(sq, ux, uy, uz, eSq));

                    Assert.True(Contains(sumIv, eSum));
                    Assert.True(Contains(diffIv, eDiff));
                    Assert.True(Contains(negIv, eNeg));
                    Assert.True(Contains(shiftedIv, eShifted));
                    Assert.True(Contains(scaledIv, eScaled));
                    Assert.True(Contains(prodIv, eProd));
                    Assert.True(Contains(sqIv, eSq));
                }
            }
        }

        [Fact]
        public void Affine3_SubnormalZeroOverflowAndNaN_AreAlwaysSound()
        {
            // Exact constant & cancellation x - x
            Affine3 x = Affine3.FromAxis(new ExactInterval(1.0, 1.5), 0);
            Affine3 cancel = x - x;
            Assert.Equal(0.0, cancel.Cx);
            ExactInterval cancelIv = cancel.ToInterval();
            Assert.True(cancelIv.Lo <= 0 && cancelIv.Hi >= 0);
            Assert.True(cancelIv.Hi - cancelIv.Lo < 1e-13);

            // Subnormal Square: excursion = double.Epsilon must not underflow halfL2 to 0
            Affine3 sub = new Affine3(0, double.Epsilon, 0, 0, 0);
            Affine3 subSq = Affine3.Square(sub);
            Assert.True(subSq.IsFinite);
            ExactInterval subSqIv = subSq.ToInterval();
            ExactDyadic exactSubSq = ExactDyadic.Of(double.Epsilon) * ExactDyadic.Of(double.Epsilon);
            Assert.True(Contains(subSqIv, exactSubSq));
            Assert.True(Contains(subSqIv, ExactDyadic.Zero));

            // Point interval at subnormal double.Epsilon must preserve value
            Affine3 pt = Affine3.FromAxis(ExactInterval.Of(double.Epsilon), 1);
            Assert.Equal(double.Epsilon, pt.C0);
            Assert.Equal(0.0, pt.Cy);
            Assert.Equal(0.0, pt.Rad);
            Assert.Equal(double.Epsilon, pt.ToInterval().Lo);
            Assert.Equal(double.Epsilon, pt.ToInterval().Hi);

            // Non-finite inputs produce Unbounded -> Whole interval
            Affine3[] bad = {
                Affine3.Of(double.NaN),
                Affine3.Of(double.PositiveInfinity),
                Affine3.Of(double.NegativeInfinity),
                Affine3.FromInterval(new ExactInterval(1.0, 0.0)),
                Affine3.FromAxis(new ExactInterval(double.NaN, 1.0), 0),
                Affine3.Of(1e200) * Affine3.Of(1e200),
                Affine3.Square(Affine3.Of(1e200)),
                Affine3.Of(1e308) + Affine3.Of(1e308),
                Affine3.Of(1e308).Scale(10.0),
                Affine3.Of(1e308).AddConst(1e308)
            };
            foreach (Affine3 a in bad) {
                Assert.False(a.IsFinite);
                ExactInterval iv = a.ToInterval();
                Assert.True(double.IsNegativeInfinity(iv.Lo) && double.IsPositiveInfinity(iv.Hi));
            }

            // Affine1 ConstantOnly & SinCos edge cases
            double mid, half;
            Affine1 s, c;
            Affine1.SinCos(new ExactInterval(double.NaN, 1.0), out s, out c, out mid, out half);
            Assert.True(s.ToInterval().Lo <= -1 && s.ToInterval().Hi >= 1);
            Assert.True(c.ToInterval().Lo <= -1 && c.ToInterval().Hi >= 1);

            Affine1.SinCos(new ExactInterval(0.0, 4.0), out s, out c, out mid, out half);
            Assert.Equal(0.0, s.C1); // wide box (> MaxTaylorHalf) falls back to constant interval
            Assert.Equal(0.0, c.C1);
            Assert.True(s.ToInterval().Lo <= -0.756 && s.ToInterval().Hi >= 1.0);
            Assert.True(c.ToInterval().Lo <= -1.0 && c.ToInterval().Hi >= 1.0);

            Affine1.SinCos(ExactInterval.Of(double.Epsilon), out s, out c, out mid, out half);
            Assert.Equal(double.Epsilon, mid);
            Assert.Equal(0.0, half);
            Assert.True(s.ToInterval().Lo <= double.Epsilon && s.ToInterval().Hi >= double.Epsilon);
            Assert.True(c.ToInterval().Lo <= 1.0 && c.ToInterval().Hi >= 1.0);

            // ConstantOnly preserves range of any affine1
            Affine1.SinCos(new ExactInterval(0.3, 0.7), out s, out c, out mid, out half);
            Affine1 sConst = s.ConstantOnly();
            Assert.Equal(0.0, sConst.C1);
            Assert.True(sConst.ToInterval().Lo <= s.ToInterval().Lo && sConst.ToInterval().Hi >= s.ToInterval().Hi);
        }

        [Fact]
        public void Affine1_SinCos_EnclosesPointSinCosAcrossWidthsAndRanges()
        {
            var rng = new Random(123);
            double[] widths = { 0.0, 1e-12, 1e-4, 0.05, 0.3, 1.0, 2.39, 2.41, 5.0, 10.0 };
            foreach (double w in widths) {
                for (int trial = 0; trial < 30; ++trial) {
                    double lo = (rng.NextDouble() * 40 - 20);
                    ExactInterval p = new ExactInterval(lo, lo + w);
                    Affine1 s, c;
                    double mid, half;
                    Affine1.SinCos(p, out s, out c, out mid, out half);
                    ExactInterval sHull = s.ToInterval(), cHull = c.ToInterval();
                    for (int k = 0; k <= 8; ++k) {
                        double theta = lo + w * (k / 8.0);
                        ExactInterval sp, cp;
                        IntervalMath.SinCos(theta, out sp, out cp);
                        Assert.True(sHull.Lo <= sp.Hi && sp.Lo <= sHull.Hi);
                        Assert.True(cHull.Lo <= cp.Hi && cp.Lo <= cHull.Hi);
                        if (half > 0 && half <= Affine1.MaxTaylorHalf && Math.Abs(mid) <= 1073741824.0) {
                            ExactInterval u = (ExactInterval.Of(theta) - ExactInterval.Of(mid)) / ExactInterval.Of(half);
                            ExactInterval sRange = ExactInterval.Of(s.C0) + ExactInterval.Of(s.C1) * u + new ExactInterval(-s.Rad, s.Rad);
                            ExactInterval cRange = ExactInterval.Of(c.C0) + ExactInterval.Of(c.C1) * u + new ExactInterval(-c.Rad, c.Rad);
                            Assert.True(sRange.Lo <= sp.Hi && sp.Lo <= sRange.Hi);
                            Assert.True(cRange.Lo <= cp.Hi && cp.Lo <= cRange.Hi);
                        }
                    }
                }
            }
        }

        [Fact]
        public void Affine3_NonZeroInitialRadAndMixedExponents_SoundAgainstExactDyadic()
        {
            // Exercises rA, rB, rScaled, -0.0, and mixed-exponent operands (1e70 combined with 1e-200)
            Affine3 a = new Affine3(1e70, -0.0, 1e68, 0.0, 1e65);
            Affine3 b = new Affine3(1e-200, 1e-202, 0.0, -1e-202, 1e-204);
            Affine3 prod = a * b;
            Affine3 scaled = prod.Scale(-3.25);
            Affine3 nested = prod * (a.Scale(1e-130) + b.Scale(1e140));
            Affine3 sq = Affine3.Square(a.Scale(1e-69) + b.Scale(1e200));

            ExactDyadic ux = ExactDyadic.Of(0.5), uy = ExactDyadic.Of(-1.0), uz = ExactDyadic.Of(1.0);
            // Sample corners of the initial Rad perturbations (+/- a.Rad, +/- b.Rad)
            foreach (int sa in new[] { -1, 1 })
                foreach (int sb in new[] { -1, 1 }) {
                    ExactDyadic va = EvalLinear(a, ux, uy, uz) + ExactDyadic.Of(sa * a.Rad);
                    ExactDyadic vb = EvalLinear(b, ux, uy, uz) + ExactDyadic.Of(sb * b.Rad);
                    ExactDyadic eProd = va * vb;
                    Assert.True(EnclosesPoint(prod, ux, uy, uz, eProd));
                    Assert.True(EnclosesPoint(scaled, ux, uy, uz, eProd * ExactDyadic.Of(-3.25)));
                    Assert.True(Contains(nested.ToInterval(), eProd * (va * ExactDyadic.Of(1e-130) + vb * ExactDyadic.Of(1e140))));
                    ExactDyadic combo = va * ExactDyadic.Of(1e-69) + vb * ExactDyadic.Of(1e200);
                    Assert.True(Contains(sq.ToInterval(), combo * combo));
                }
        }

        [Fact]
        public void IntervalTpms_SubnormalDoubleAngle_EnclosesValueAndGradient()
        {
            // Exercises sinCosDoubleAffine when 2*mid or 2*half diverges from mid(2*p) / half(2*p) in subnormal range.
            ExactInterval tiny = new ExactInterval(0.0, 3.0 * double.Epsilon);
            ExactInterval normal = new ExactInterval(0.2, 0.25);
            foreach (TpmsType type in new[] { TpmsType.IWP, TpmsType.Lidinoid }) {
                var f = new IntervalTpms(type, 1.0, 0.0);
                IntervalEnclosure e = f.Enclose(tiny, normal, normal);
                Assert.True(e.Smooth);
                Vector3d p = new Vector3d(double.Epsilon, 0.225, 0.225);
                double v = TpmsLattice.Evaluate(type, p.x, p.y, p.z);
                Assert.True(e.F.Lo - 1e-12 <= v && v <= e.F.Hi + 1e-12);
                const double d = 1e-6;
                for (int ax = 0; ax < 3; ++ax) {
                    Vector3d q0 = p, q1 = p; q0[ax] -= d; q1[ax] += d;
                    double fd = (TpmsLattice.Evaluate(type, q1.x, q1.y, q1.z) - TpmsLattice.Evaluate(type, q0.x, q0.y, q0.z)) / (2 * d);
                    ExactInterval g = ax == 0 ? e.Gx : ax == 1 ? e.Gy : e.Gz;
                    Assert.True(g.Lo - 1e-6 <= fd && fd <= g.Hi + 1e-6);
                }
            }
        }

        [Fact]
        public void IntervalTpms_AffineCache_IsBoundedAndBitIdenticalOnEviction()
        {
            var f = new IntervalTpms(TpmsType.Gyroid, 1.0, 0.0);
            object cache = f.NewEncloseCache();
            var own = (IntervalTpms.SinCosCache)cache;
            ExactInterval p = ExactInterval.Of(0.25);
            int n = IntervalTpms.SinCosCache.MaxEntries + 10;
            for (int i = 0; i < n; ++i) {
                ExactInterval x = new ExactInterval(1e-3 * i, 1e-3 * i + 1e-4);
                IntervalEnclosure e = f.Enclose(x, p, p, cache);
                if (i % 4093 == 0 || i == n - 1) {
                    IntervalEnclosure u = f.Enclose(x, p, p);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(u.F.Lo), BitConverter.DoubleToInt64Bits(e.F.Lo));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(u.F.Hi), BitConverter.DoubleToInt64Bits(e.F.Hi));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(u.Gx.Lo), BitConverter.DoubleToInt64Bits(e.Gx.Lo));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(u.Gx.Hi), BitConverter.DoubleToInt64Bits(e.Gx.Hi));
                }
                Assert.True(own.Map.Count <= IntervalTpms.SinCosCache.MaxEntries);
            }
            // p misses once on i = 0 and once again after Clear() at i = MaxEntries - 1 (none of the non-point x intervals equal p)
            Assert.Equal(n + 2, own.Misses);
            Assert.Equal(n + 2 - IntervalTpms.SinCosCache.MaxEntries, own.Map.Count);
        }

        // Plain ExactInterval reference for SchwarzD to verify Affine3 achieves strictly tighter enclosures
        // (fails if IntervalTpms reverts to plain interval arithmetic).
        static IntervalEnclosure EncloseSchwarzDPlainInterval(double k, ExactInterval x, ExactInterval y, ExactInterval z)
        {
            ExactInterval K = ExactInterval.Of(k);
            ExactInterval sx, cx, sy, cy, sz, cz;
            IntervalMath.SinCos(K * x, out sx, out cx);
            IntervalMath.SinCos(K * y, out sy, out cy);
            IntervalMath.SinCos(K * z, out sz, out cz);
            ExactInterval g = sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
            ExactInterval gx = cx * sy * sz + cx * cy * cz - sx * sy * cz - sx * cy * sz;
            ExactInterval gy = sx * cy * sz - sx * sy * cz + cx * cy * cz - cx * sy * sz;
            ExactInterval gz = sx * sy * cz - sx * cy * sz - cx * sy * sz + cx * cy * cz;
            return new IntervalEnclosure { F = g, Gx = K * gx, Gy = K * gy, Gz = K * gz, Smooth = true };
        }

        [Fact]
        public void IntervalTpms_Affine3_StrictlyTighterThanPlainIntervalOnSchwarzD()
        {
            double k = 2.0 * Math.PI / 1.1;
            var f = new IntervalTpms(TpmsType.SchwarzD, k, 0.0);
            double h = 2.2 / 32.0;
            double sumPlainF = 0, sumAffineF = 0, sumPlainG = 0, sumAffineG = 0;
            int affinePassesGradWherePlainFails = 0;

            for (int iz = 0; iz < 6; ++iz)
                for (int iy = 0; iy < 6; ++iy)
                    for (int ix = 0; ix < 6; ++ix) {
                        var bx = new ExactInterval(-0.6 + ix * h, -0.6 + (ix + 1) * h);
                        var by = new ExactInterval(-0.5 + iy * h, -0.5 + (iy + 1) * h);
                        var bz = new ExactInterval(-0.4 + iz * h, -0.4 + (iz + 1) * h);
                        IntervalEnclosure plain = EncloseSchwarzDPlainInterval(k, bx, by, bz);
                        IntervalEnclosure aff = f.Enclose(bx, by, bz);
                        sumPlainF += plain.F.Hi - plain.F.Lo;
                        sumAffineF += aff.F.Hi - aff.F.Lo;
                        sumPlainG += (plain.Gx.Hi - plain.Gx.Lo) + (plain.Gy.Hi - plain.Gy.Lo) + (plain.Gz.Hi - plain.Gz.Lo);
                        sumAffineG += (aff.Gx.Hi - aff.Gx.Lo) + (aff.Gy.Hi - aff.Gy.Lo) + (aff.Gz.Hi - aff.Gz.Lo);
                        if (ContouringSmoothCertificate.GradientTest(aff.Gx, aff.Gy, aff.Gz, 1.0)
                            && !ContouringSmoothCertificate.GradientTest(plain.Gx, plain.Gy, plain.Gz, 1.0))
                            ++affinePassesGradWherePlainFails;
                    }

            // On coarse n=32 cells (phase half-width ~0.2), Affine3 reduces width and passes gradient tests where plain interval fails
            Assert.True(sumAffineF < 0.9 * sumPlainF, $"F width ratio {sumAffineF / sumPlainF:F3}");
            Assert.True(sumAffineG < 0.85 * sumPlainG, $"G width ratio {sumAffineG / sumPlainG:F3}");
            Assert.True(affinePassesGradWherePlainFails > 0);

            // On finer n=64 cells (phase half-width ~0.1), O(h^2) vs O(h) gives >2x tighter enclosures
            double hf = 2.2 / 64.0;
            double finePlainF = 0, fineAffineF = 0, finePlainG = 0, fineAffineG = 0;
            for (int iz = 0; iz < 6; ++iz)
                for (int iy = 0; iy < 6; ++iy)
                    for (int ix = 0; ix < 6; ++ix) {
                        var bx = new ExactInterval(-0.6 + ix * hf, -0.6 + (ix + 1) * hf);
                        var by = new ExactInterval(-0.5 + iy * hf, -0.5 + (iy + 1) * hf);
                        var bz = new ExactInterval(-0.4 + iz * hf, -0.4 + (iz + 1) * hf);
                        IntervalEnclosure plain = EncloseSchwarzDPlainInterval(k, bx, by, bz);
                        IntervalEnclosure aff = f.Enclose(bx, by, bz);
                        finePlainF += plain.F.Hi - plain.F.Lo;
                        fineAffineF += aff.F.Hi - aff.F.Lo;
                        finePlainG += (plain.Gx.Hi - plain.Gx.Lo) + (plain.Gy.Hi - plain.Gy.Lo) + (plain.Gz.Hi - plain.Gz.Lo);
                        fineAffineG += (aff.Gx.Hi - aff.Gx.Lo) + (aff.Gy.Hi - aff.Gy.Lo) + (aff.Gz.Hi - aff.Gz.Lo);
                    }
            Assert.True(fineAffineF < 0.75 * finePlainF, $"fine F width ratio {fineAffineF / finePlainF:F3}");
            Assert.True(fineAffineG < 0.75 * finePlainG, $"fine G width ratio {fineAffineG / finePlainG:F3}");
        }

        sealed class CountingCachedField : IntervalImplicit3dCached, ImplicitFunction3d
        {
            readonly IntervalTpms inner;
            readonly double radius, cap;
            readonly bool returnNullCache;
            public int NewCacheCalls;
            public int CachedEncloseCalls;
            public int UncachedEncloseCalls;

            public CountingCachedField(TpmsType type, double k, double radius, double cap, bool returnNullCache = false)
            {
                inner = new IntervalTpms(type, k, 0.0);
                this.radius = radius;
                this.cap = cap;
                this.returnNullCache = returnNullCache;
            }

            public object NewEncloseCache()
            {
                System.Threading.Interlocked.Increment(ref NewCacheCalls);
                return returnNullCache ? null : inner.NewEncloseCache();
            }

            public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                System.Threading.Interlocked.Increment(ref UncachedEncloseCalls);
                return addCap(inner.Enclose(x, y, z), x, y, z);
            }

            public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z, object cache)
            {
                System.Threading.Interlocked.Increment(ref CachedEncloseCalls);
                return addCap(inner.Enclose(x, y, z, cache), x, y, z);
            }

            IntervalEnclosure addCap(IntervalEnclosure e, ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval r2 = ExactInterval.Square(ExactInterval.Of(radius));
                ExactInterval u = (ExactInterval.Square(x) + ExactInterval.Square(y) + ExactInterval.Square(z)) / r2;
                ExactInterval u2 = ExactInterval.Square(u);
                ExactInterval d = ExactInterval.Of(6) * ExactInterval.Of(cap) * u2 / r2;
                e.F = e.F + ExactInterval.Of(cap) * (u2 * u);
                e.Gx = e.Gx + d * x; e.Gy = e.Gy + d * y; e.Gz = e.Gz + d * z;
                return e;
            }

            public double Value(ref Vector3d p)
            {
                double u = p.LengthSquared / (radius * radius);
                return TpmsLattice.Evaluate(inner.Type, inner.K * p.x, inner.K * p.y, inner.K * p.z) + cap * u * u * u;
            }
        }

        [Fact]
        public void ContouringSmoothCertificate_UsesCacheAndMatchesSerialParallelAndReuse()
        {
            var origin = new Vector3d(-1.113, -1.107, -1.103);
            const double size = 2.2;
            double k = 2.0 * Math.PI / size;
            var field = new CountingCachedField(TpmsType.SchwarzD, k, 0.9, 4.0);
            var dc = new DualContouring {
                Implicit = field, InsideIsLess = true, CellSize = size / 24, Simplify = false,
                Bounds = new AxisAlignedBox3d(origin, origin + size * Vector3d.One), RecordCertificateData = true
            };
            dc.Generate();

            var cert = new ContouringSmoothCertificate { Field = field, Contour = dc, ParallelCompute = false };
            cert.Compute();
            Assert.Equal(1, field.NewCacheCalls);
            Assert.True(field.CachedEncloseCalls > 0);
            Assert.Equal(0, field.UncachedEncloseCalls);
            bool serialCertified = cert.Certified;
            int serialSurf = cert.SurfaceCells;
            int[] serialPassed = (int[])cert.PassedAtTightening.Clone();
            var serialUncert = new List<SmoothUncertifiedCell>(cert.UncertifiedCells);

            // Reuse same certificate instance with ParallelCompute = true
            cert.ParallelCompute = true;
            cert.Compute();
            Assert.True(field.NewCacheCalls >= 2);
            Assert.Equal(0, field.UncachedEncloseCalls);
            Assert.Equal(serialCertified, cert.Certified);
            Assert.Equal(serialSurf, cert.SurfaceCells);
            Assert.Equal(serialPassed, cert.PassedAtTightening);
            Assert.Equal(serialUncert.Count, cert.UncertifiedCells.Count);
            for (int i = 0; i < serialUncert.Count; ++i) {
                Assert.Equal(serialUncert[i].Cell, cert.UncertifiedCells[i].Cell);
                Assert.Equal(serialUncert[i].Reason, cert.UncertifiedCells[i].Reason);
            }

            // Null cache returned by NewEncloseCache gracefully falls back to uncached Enclose with identical result
            var nullCacheField = new CountingCachedField(TpmsType.SchwarzD, k, 0.9, 4.0, returnNullCache: true);
            var certNull = new ContouringSmoothCertificate { Field = nullCacheField, Contour = dc, ParallelCompute = false };
            certNull.Compute();
            Assert.Equal(1, nullCacheField.NewCacheCalls);
            Assert.Equal(0, nullCacheField.CachedEncloseCalls);
            Assert.True(nullCacheField.UncachedEncloseCalls > 0);
            Assert.Equal(serialCertified, certNull.Certified);
            Assert.Equal(serialSurf, certNull.SurfaceCells);
            Assert.Equal(serialPassed, certNull.PassedAtTightening);
            Assert.Equal(serialUncert.Count, certNull.UncertifiedCells.Count);
        }

        [Theory]
        [InlineData(TpmsType.Gyroid)]
        [InlineData(TpmsType.SchwarzP)]
        [InlineData(TpmsType.SchwarzD)]
        [InlineData(TpmsType.Neovius)]
        [InlineData(TpmsType.IWP)]
        [InlineData(TpmsType.Lidinoid)]
        public void IntervalTpms_CyclicAxisPermutation_PreservesEnclosureMidpointAndContainment(TpmsType type)
        {
            // All 6 TPMS functions are symmetric under cyclic permutation (x, y, z) -> (y, z, x).
            var f = new IntervalTpms(type, 2.3, 0.15, negate: false);
            var bx = new ExactInterval(-0.31, -0.24);
            var by = new ExactInterval(0.12, 0.19);
            var bz = new ExactInterval(0.45, 0.52);

            IntervalEnclosure e1 = f.Enclose(bx, by, bz);
            IntervalEnclosure e2 = f.Enclose(by, bz, bx);

            // Cyclic permutation preserves center values of F and permuted (Gx, Gy, Gz) -> (Gy, Gz, Gx)
            // (quadratic remainder widths can differ slightly when triple products associate (x, y)*z vs (y, z)*x).
            Func<ExactInterval, double> mid = iv => 0.5 * (iv.Lo + iv.Hi);
            Assert.True(Math.Abs(mid(e1.F) - mid(e2.F)) < 1e-12);
            Assert.True(Math.Abs(mid(e1.Gx) - mid(e2.Gz)) < 1e-12);
            Assert.True(Math.Abs(mid(e1.Gy) - mid(e2.Gx)) < 1e-12);
            Assert.True(Math.Abs(mid(e1.Gz) - mid(e2.Gy)) < 1e-12);

            // Both enclosures contain the true TPMS value at the box corners under the corresponding permutation.
            double[] xs = { bx.Lo, bx.Hi }, ys = { by.Lo, by.Hi }, zs = { bz.Lo, bz.Hi };
            foreach (double x in xs)
                foreach (double y in ys)
                    foreach (double z in zs) {
                        double v = TpmsLattice.Evaluate(type, 2.3 * x, 2.3 * y, 2.3 * z) - 0.15;
                        Assert.True(e1.F.Lo - 1e-12 <= v && v <= e1.F.Hi + 1e-12);
                        Assert.True(e2.F.Lo - 1e-12 <= v && v <= e2.F.Hi + 1e-12);
                    }
        }
    }
}
