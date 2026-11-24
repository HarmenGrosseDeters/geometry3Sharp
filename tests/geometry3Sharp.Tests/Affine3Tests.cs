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

            // Reuse same certificate instance with ParallelCompute = true (repeated 3x to verify schedule independence)
            for (int run = 0; run < 3; ++run) {
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
            }
            cert.ParallelCompute = false;
            cert.Compute();
            Assert.Equal(serialCertified, cert.Certified);
            Assert.Equal(serialSurf, cert.SurfaceCells);
            Assert.Equal(serialPassed, cert.PassedAtTightening);

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

        [Fact]
        public void Affine3_ErrorTerms_CoverUnderflowInexactMulMidRadAndSafeSum()
        {
            // 1) SubErr: underflowing product 1e-200 * 1e-200 has fl(c0) = 0 while exact value is 1e-400 > 0
            Affine3 underflow = Affine3.Of(1e-200) * Affine3.Of(1e-200);
            ExactDyadic exactUnderflow = ExactDyadic.Of(1e-200) * ExactDyadic.Of(1e-200);
            Assert.True(Contains(underflow.ToInterval(), exactUnderflow));

            // 2) Multiplication / Square / Add / AddConst / Scale rounding-error terms on inexact binary64 operations
            Affine3 mulInexact = Affine3.Of(0.1) * Affine3.Of(0.1);
            ExactDyadic exactTenthSq = ExactDyadic.Of(0.1) * ExactDyadic.Of(0.1);
            Assert.True(Contains(mulInexact.ToInterval(), exactTenthSq));
            Assert.True(Contains(Affine3.Square(Affine3.Of(0.1)).ToInterval(), exactTenthSq));
            Assert.True(Contains(Affine3.Of(0.1).Scale(0.1).ToInterval(), exactTenthSq));
            Assert.True(Contains((Affine3.Of(1.0) + Affine3.Of(1e-16)).ToInterval(), ExactDyadic.Of(1.0) + ExactDyadic.Of(1e-16)));
            Assert.True(Contains(Affine3.Of(1.0).AddConst(1e-16).ToInterval(), ExactDyadic.Of(1.0) + ExactDyadic.Of(1e-16)));
            // Non-zero slope inexact multiplication with nl == 0: directly tests px1/px2/py1/py2/pz1/pz2 roundErr terms
            Affine3 c01 = Affine3.Of(0.1);
            ExactDyadic d01 = ExactDyadic.Of(0.1), d03 = ExactDyadic.Of(0.3), dProd = d01 * d03;
            foreach (Affine3 lin in new[] {
                new Affine3(0, 0.3, 0, 0, 0), new Affine3(0, 0, 0.3, 0, 0), new Affine3(0, 0, 0, 0.3, 0)
            }) {
                Assert.True(EnclosesPoint(c01 * lin, ExactDyadic.Of(1.0), ExactDyadic.Of(1.0), ExactDyadic.Of(1.0), dProd));
                Assert.True(EnclosesPoint(lin * c01, ExactDyadic.Of(1.0), ExactDyadic.Of(1.0), ExactDyadic.Of(1.0), dProd));
            }

            // 3) MidRad upward rounding: [0.1, 0.3], [-1e-17, 1.0] (left side rounds down), and [-1.0, 1e-17] (right side rounds down)
            foreach (ExactInterval iv in new[] { new ExactInterval(0.1, 0.3), new ExactInterval(-1e-17, 1.0), new ExactInterval(-1.0, 1e-17) }) {
                Affine3 fromIv = Affine3.FromInterval(iv);
                ExactDyadic dLo = ExactDyadic.Of(iv.Lo), dHi = ExactDyadic.Of(iv.Hi);
                ExactDyadic radIv = ExactDyadic.Of(fromIv.Rad), midIv = ExactDyadic.Of(fromIv.C0);
                Assert.True(ExactDyadic.Compare(dHi - midIv, radIv) <= 0);
                Assert.True(ExactDyadic.Compare(midIv - dLo, radIv) <= 0);
                Affine3 fromAx = Affine3.FromAxis(iv, 0);
                ExactDyadic hAx = ExactDyadic.Of(fromAx.Cx), midAx = ExactDyadic.Of(fromAx.C0);
                Assert.True(ExactDyadic.Compare(dHi - midAx, hAx) <= 0);
                Assert.True(ExactDyadic.Compare(midAx - dLo, hAx) <= 0);
            }

            // 4) SafeSum factors: sub-0.5-ulp terms absorbed by repeated additions must not drop below exact sum
            double u2 = Math.Pow(2, -52);
            double eps = 0.45 * u2;
            Affine3 exTest = new Affine3(0, 1.0, eps, eps, eps);
            ExactDyadic exactEx = ExactDyadic.Of(1.0) + ExactDyadic.Of(3.0) * ExactDyadic.Of(eps);
            Assert.True(ExactDyadic.Compare(ExactDyadic.Of(exTest.Excursion()), exactEx) >= 0);
            // SafeSum8 in operator +: a.Rad = 1.0 plus 5 terms each 0.45*u2 < 0.5*ulp(1.0) -> exact sum = 1.0 + 2.25*u2
            Affine3 addA = new Affine3(0.45, 0.45, 0.45, 0.45, 1.0);
            Affine3 addB = new Affine3(0, 0, 0, 0, eps);
            Affine3 addSum = addA + addB;
            ExactDyadic exactAddRad = ExactDyadic.Of(1.0) + ExactDyadic.Of(5.0) * ExactDyadic.Of(eps);
            Assert.True(ExactDyadic.Compare(ExactDyadic.Of(addSum.Rad), exactAddRad) >= 0);
            // SafeSum16 in operator *: verifies multiplication preserves SafeSum16 headroom above uninflated sum
            Affine3 mulA = new Affine3(0, 0, 0, 0, 1.0);
            Affine3 mulB = new Affine3(1.0, 0.3 * u2, 0.3 * u2, 0.3 * u2, 0.3 * u2);
            Affine3 mulRes = mulA * mulB;
            double rAOnly = ExactInterval.Up(1.0);
            // Without SafeSum16, nl (~1.2*u2) + rA (1+u2) + roundErr terms could lose sub-0.5-ulp contributions; Rad must strictly exceed rA + 1.5*u2
            Assert.True(mulRes.Rad > ExactInterval.Up(rAOnly + 1.5 * u2));

            // 5) SinCosCache WithAffine mismatch guard: passing a SchwarzP cache to Gyroid (or vice versa) throws
            var sp = new IntervalTpms(TpmsType.SchwarzP, 1.0, 0.0);
            var gy = new IntervalTpms(TpmsType.Gyroid, 1.0, 0.0);
            var box = new ExactInterval(0.1, 0.2);
            Assert.Throws<ArgumentException>(() => gy.Enclose(box, box, box, sp.NewEncloseCache()));
            Assert.Throws<ArgumentException>(() => sp.Enclose(box, box, box, gy.NewEncloseCache()));
        }

        [Fact]
        public void IntervalTpms_NeverWiderThanPlainInterval_AcrossCoarseAndFineBoxes()
        {
            var rng = new Random(99);
            const double kVal = 1.0;
            ExactInterval k = ExactInterval.Of(kVal), zero = ExactInterval.Of(0.0), two = ExactInterval.Of(2.0);
            var gyroid = new IntervalTpms(TpmsType.Gyroid, kVal, 0.0);
            var iwp = new IntervalTpms(TpmsType.IWP, kVal, 0.0);
            foreach (double half in new[] { 0.05, 0.5, 1.0 }) {
                for (int i = 0; i < 200; ++i) {
                    double x0 = rng.NextDouble() * 10 - 5, y0 = rng.NextDouble() * 10 - 5, z0 = rng.NextDouble() * 10 - 5;
                    var bx = new ExactInterval(x0 - half, x0 + half);
                    var by = new ExactInterval(y0 - half, y0 + half);
                    var bz = new ExactInterval(z0 - half, z0 + half);
                    ExactInterval px = k * bx, py = k * by, pz = k * bz;
                    ExactInterval sx, cx, sy, cy, sz, cz, s2x, c2x, s2y, c2y, s2z, c2z;
                    IntervalMath.SinCos(px, out sx, out cx);
                    IntervalMath.SinCos(py, out sy, out cy);
                    IntervalMath.SinCos(pz, out sz, out cz);
                    ExactInterval pf = (sx * cy + sy * cz + sz * cx) - zero;
                    ExactInterval pgx = k * (cx * cy - sz * sx), pgy = k * (cy * cz - sx * sy), pgz = k * (cz * cx - sy * sz);

                    IntervalEnclosure e = gyroid.Enclose(bx, by, bz);
                    Assert.True(e.F.Lo >= pf.Lo && e.F.Hi <= pf.Hi, $"Gyroid half {half}: F wider");
                    Assert.True(e.Gx.Lo >= pgx.Lo && e.Gx.Hi <= pgx.Hi, $"Gyroid half {half}: Gx wider");
                    Assert.True(e.Gy.Lo >= pgy.Lo && e.Gy.Hi <= pgy.Hi, $"Gyroid half {half}: Gy wider");
                    Assert.True(e.Gz.Lo >= pgz.Lo && e.Gz.Hi <= pgz.Hi, $"Gyroid half {half}: Gz wider");

                    IntervalMath.SinCos(new ExactInterval(2 * px.Lo, 2 * px.Hi), out s2x, out c2x);
                    IntervalMath.SinCos(new ExactInterval(2 * py.Lo, 2 * py.Hi), out s2y, out c2y);
                    IntervalMath.SinCos(new ExactInterval(2 * pz.Lo, 2 * pz.Hi), out s2z, out c2z);
                    ExactInterval ifF = (two * (cx * cy + cy * cz + cz * cx) - (c2x + c2y + c2z)) - zero;
                    ExactInterval igx = k * (two * s2x - two * (sx * (cy + cz)));
                    ExactInterval igy = k * (two * s2y - two * (sy * (cx + cz)));
                    ExactInterval igz = k * (two * s2z - two * (sz * (cx + cy)));
                    IntervalEnclosure ei = iwp.Enclose(bx, by, bz);
                    Assert.True(ei.F.Lo >= ifF.Lo && ei.F.Hi <= ifF.Hi, $"IWP half {half}: F wider");
                    Assert.True(ei.Gx.Lo >= igx.Lo && ei.Gx.Hi <= igx.Hi, $"IWP half {half}: Gx wider");
                    Assert.True(ei.Gy.Lo >= igy.Lo && ei.Gy.Hi <= igy.Hi, $"IWP half {half}: Gy wider");
                    Assert.True(ei.Gz.Lo >= igz.Lo && ei.Gz.Hi <= igz.Hi, $"IWP half {half}: Gz wider");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance_UnderSimilarities_Tpms(string name)
        {
            Similarity sim = System.Linq.Enumerable.First(Similarity.All(), s => s.Name == name);
            var origin = new Vector3d(-1.113, -1.107, -1.103);
            const double size = 2.2;
            const double baseK = 1.2;

            // Base run at Identity for direct comparison
            var baseField = new CountingCachedField(TpmsType.Gyroid, baseK, 0.85, 2.0);
            var dc0 = new DualContouring {
                Implicit = baseField, InsideIsLess = true, CellSize = size / 28, Simplify = false,
                Bounds = new AxisAlignedBox3d(origin, origin + size * Vector3d.One), RecordCertificateData = true
            };
            dc0.Generate();
            var cert0 = new ContouringSmoothCertificate { Field = baseField, Contour = dc0, ParallelCompute = false };
            cert0.Compute();
            Assert.True(cert0.Certified, $"base: uncertified={cert0.UncertifiedCells.Count}, runFailure={cert0.RunFailure}");
            Assert.True(cert0.SurfaceCells > 50);

            if (sim.IsExact) {
                // Exact power-of-two scaling: phases (K / s) * (s * x) are bit-identical across all 6 TPMS types (with & without cache)
                foreach (TpmsType type in new[] { TpmsType.Gyroid, TpmsType.SchwarzP, TpmsType.SchwarzD, TpmsType.Neovius, TpmsType.IWP, TpmsType.Lidinoid }) {
                    var f0 = new IntervalTpms(type, baseK, 0.1);
                    var fs = new IntervalTpms(type, baseK / sim.Scale, 0.1);
                    object c0 = f0.NewEncloseCache(), cs = fs.NewEncloseCache();
                    var bx = new ExactInterval(-0.31, -0.24);
                    var by = new ExactInterval(0.12, 0.19);
                    var bz = new ExactInterval(0.45, 0.52);
                    var sbx = new ExactInterval(sim.Scale * bx.Lo, sim.Scale * bx.Hi);
                    var sby = new ExactInterval(sim.Scale * by.Lo, sim.Scale * by.Hi);
                    var sbz = new ExactInterval(sim.Scale * bz.Lo, sim.Scale * bz.Hi);
                    foreach (bool cached in new[] { false, true }) {
                        IntervalEnclosure e0 = cached ? f0.Enclose(bx, by, bz, c0) : f0.Enclose(bx, by, bz);
                        IntervalEnclosure es = cached ? fs.Enclose(sbx, sby, sbz, cs) : fs.Enclose(sbx, sby, sbz);
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.F.Lo), BitConverter.DoubleToInt64Bits(es.F.Lo));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.F.Hi), BitConverter.DoubleToInt64Bits(es.F.Hi));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.Gx.Lo / sim.Scale), BitConverter.DoubleToInt64Bits(es.Gx.Lo));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.Gx.Hi / sim.Scale), BitConverter.DoubleToInt64Bits(es.Gx.Hi));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.Gy.Lo / sim.Scale), BitConverter.DoubleToInt64Bits(es.Gy.Lo));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.Gy.Hi / sim.Scale), BitConverter.DoubleToInt64Bits(es.Gy.Hi));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.Gz.Lo / sim.Scale), BitConverter.DoubleToInt64Bits(es.Gz.Lo));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(e0.Gz.Hi / sim.Scale), BitConverter.DoubleToInt64Bits(es.Gz.Hi));
                    }
                }

                // End-to-end ContouringSmoothCertificate on capped Gyroid (runs Affine3 + cache): bit-identical verdict & counts
                var scaledField = new CountingCachedField(TpmsType.Gyroid, baseK / sim.Scale, 0.85 * sim.Scale, 2.0);
                var dcs = new DualContouring {
                    Implicit = scaledField, InsideIsLess = true, CellSize = sim.Scale * size / 28, Simplify = false,
                    Bounds = new AxisAlignedBox3d(sim.Scale * origin, sim.Scale * (origin + size * Vector3d.One)), RecordCertificateData = true
                };
                dcs.Generate();
                var certs = new ContouringSmoothCertificate { Field = scaledField, Contour = dcs, ParallelCompute = false };
                certs.Compute();
                Assert.Equal(cert0.Certified, certs.Certified);
                Assert.Equal(cert0.SurfaceCells, certs.SurfaceCells);
                Assert.Equal(cert0.PassedAtTightening, certs.PassedAtTightening);
                Assert.Equal(cert0.UncertifiedCells.Count, certs.UncertifiedCells.Count);
                Assert.True(scaledField.CachedEncloseCalls > 0);
            } else {
                // Non-exact similarity (UTM, scale 1e-3 / 1e3, rotated): wrap capped Gyroid in moved frame and compare against base
                var inner = new CountingCachedField(TpmsType.Gyroid, baseK, 0.85, 2.0);
                var moved = new MovedCachedField(inner, sim);
                Vector3d mOrigin = sim.Apply(Vector3d.Zero) + sim.Scale * origin;
                var dc = new DualContouring {
                    Implicit = moved, InsideIsLess = true, CellSize = sim.Scale * size / 28, Simplify = false,
                    Bounds = new AxisAlignedBox3d(mOrigin, mOrigin + sim.Scale * size * Vector3d.One), RecordCertificateData = true
                };
                dc.Generate();
                var cert = new ContouringSmoothCertificate { Field = moved, Contour = dc, ParallelCompute = false };
                cert.Compute();
                Assert.True(cert.Certified, $"{name}: uncertified={cert.UncertifiedCells.Count}, runFailure={cert.RunFailure}");
                Assert.True(dc0.Mesh.IsClosed() && dc.Mesh.IsClosed());
                var t0 = gs.MeshTopologySummary.Compute(dc0.Mesh);
                var tm = gs.MeshTopologySummary.Compute(dc.Mesh);
                Assert.Equal(t0.Components, tm.Components);
                Assert.Equal(
                    System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Select(t0.ComponentList, c => c.Genus), g => g),
                    System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Select(tm.ComponentList, c => c.Genus), g => g));
                if (sim == Similarity.RotatedUtmMillimetre)
                    Assert.InRange(cert.SpacingRatioSquared, 1 + 1e-6, 1 + 1e-4);
                Assert.True(inner.CachedEncloseCalls > 0);
            }
        }

        // Adapts an IntervalImplicit3dCached + ImplicitFunction3d into a Similarity-transformed frame
        sealed class MovedCachedField : IntervalImplicit3dCached, ImplicitFunction3d
        {
            readonly CountingCachedField inner;
            readonly Similarity sim;
            public MovedCachedField(CountingCachedField inner, Similarity sim) { this.inner = inner; this.sim = sim; }
            public object NewEncloseCache() { return inner.NewEncloseCache(); }
            public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z) { return Enclose(x, y, z, null); }
            public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z, object cache)
            {
                ExactInterval sc = ExactInterval.Of(sim.Scale);
                ExactInterval ox = (x - ExactInterval.Of(sim.Translation.x)) / sc;
                ExactInterval oy = (y - ExactInterval.Of(sim.Translation.y)) / sc;
                ExactInterval oz = (z - ExactInterval.Of(sim.Translation.z)) / sc;
                Matrix3d rt = sim.Rotation.Transpose();
                ExactInterval lx = ExactInterval.Of(rt.Row0.x) * ox + ExactInterval.Of(rt.Row0.y) * oy + ExactInterval.Of(rt.Row0.z) * oz;
                ExactInterval ly = ExactInterval.Of(rt.Row1.x) * ox + ExactInterval.Of(rt.Row1.y) * oy + ExactInterval.Of(rt.Row1.z) * oz;
                ExactInterval lz = ExactInterval.Of(rt.Row2.x) * ox + ExactInterval.Of(rt.Row2.y) * oy + ExactInterval.Of(rt.Row2.z) * oz;
                IntervalEnclosure e = cache != null ? inner.Enclose(lx, ly, lz, cache) : inner.Enclose(lx, ly, lz);
                Matrix3d r = sim.Rotation;
                ExactInterval gx = (ExactInterval.Of(r.Row0.x) * e.Gx + ExactInterval.Of(r.Row0.y) * e.Gy + ExactInterval.Of(r.Row0.z) * e.Gz) / sc;
                ExactInterval gy = (ExactInterval.Of(r.Row1.x) * e.Gx + ExactInterval.Of(r.Row1.y) * e.Gy + ExactInterval.Of(r.Row1.z) * e.Gz) / sc;
                ExactInterval gz = (ExactInterval.Of(r.Row2.x) * e.Gx + ExactInterval.Of(r.Row2.y) * e.Gy + ExactInterval.Of(r.Row2.z) * e.Gz) / sc;
                return new IntervalEnclosure { F = e.F, Gx = gx, Gy = gy, Gz = gz, Smooth = e.Smooth };
            }
            public double Value(ref Vector3d p) { Vector3d q = sim.Inverse(p); return inner.Value(ref q); }
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
