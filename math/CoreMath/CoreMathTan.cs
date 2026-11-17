// MIT License (the sources of this file are not under the Boost license of the rest of the repository).
//
// Copyright (c) 2022-2026 Alexei Sibidanov, Paul Zimmermann, INRIA and CERN (CORE-MATH, https://core-math.gitlabpages.inria.fr/)
// Copyright (c) 2026 Andante (CoreMathSharp, https://github.com/andanteyk/CoreMathSharp)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of
// the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Correctly rounded binary64 tan: vendored from CoreMathSharp (https://github.com/andanteyk/CoreMathSharp, commit
// 77505b1bdb34c73655740b7d08947654d8b7eb63, MIT, Copyright (c) 2026 Andante), a C# port of the CORE-MATH project
// (https://core-math.gitlabpages.inria.fr/, MIT, Copyright (c) 2022-2026 Alexei Sibidanov, Paul Zimmermann, INRIA and
// CERN; commit 018fa97 of core-math). Changes: namespace, internal visibility, tables as static arrays instead of
// ReadOnlySpan (netstandard2.0), no hardware intrinsics. See docs/SOURCES.md for the full notices.
using System;
using System.Runtime.CompilerServices;

namespace g3.CoreMathPort;

internal static partial class CoreMath
{
    // 1 / a for a normalized Dint, by a table start and three Newton steps (CoreMathSharp's Dint.Inv, from CORE-MATH's tan.c)
    static Dint DintInv(in Dint a)
    {
        ulong[] Tinv = Tbl_Cos_0;
        ulong h = a.hi;

        int i = (int)(h >> 55) & 0xff;
        ulong t = Tinv[i];

        Uint128 e = (Uint128.One << 127) - (Uint128)h * t;
        e = t * (e >> 55);
        t += (e >> 72).lo;

        e = (Uint128.One << 127) - (Uint128)h * t;
        e = t * (e >> 47);
        t += (e >> 80).lo;

        e = (Uint128.One << 127) - (Uint128)h * t;
        e = t * (e >> 31);
        t += (e >> 96).lo;

        Dint q, r;
        r = new Dint(0, t, 1 - a.ex, 1);
        q = Dint.Mul21(a, r);
        r = r with { sgn = 0 };
        q = Dint.Add(Dint.One with { ex = 1 }, q);
        q = Dint.Mul(r, q);
        r = Dint.Add(r, q);

        return r;
    }

    static Dint DintDiv(in Dint b, in Dint a)
    {
        Dint r = DintInv(a);
        r = Dint.Mul(r, b);
        return r;
    }

    /// <summary> The correctly rounded tangent (CORE-MATH tan, binary64). </summary>
    internal static double Tan(double x)
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (double hi, double lo) aMul(double a, double b)
        {
            double hi = a * b;
            double lo = FusedMultiplyAdd(a, b, -hi);
            return (hi, lo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (double hi, double lo) sMul(double a, double bh, double bl)
        {
            var (hi, lo) = aMul(a, bh);
            lo = FusedMultiplyAdd(a, bl, lo);
            return (hi, lo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (double hi, double lo) dMul(double ah, double al, double bh, double bl)
        {
            double s, t;
            (double hi, s) = aMul(ah, bh);
            t = FusedMultiplyAdd(al, bh, s);
            double lo = FusedMultiplyAdd(ah, bl, t);
            return (hi, lo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (double hi, double lo) fastTwoSum(double a, double b)
        {
            double e;
            double hi = a + b;
            e = hi - a;
            double lo = b - e;
            return (hi, lo);
        }

        static (double hi, double lo) evalPSFast(double xh, double xl, double uh, double ul)
        {
            double[] PSFast = Tbl_Cos_2;


            double t;
            double h = PSFast[4];
            h = FusedMultiplyAdd(h, uh, PSFast[3]);
            h = FusedMultiplyAdd(h, uh, PSFast[2]);
            (h, double l) = sMul(h, uh, ul);
            (h, t) = fastTwoSum(PSFast[0], h);
            l += PSFast[1] + t;
            (h, l) = dMul(h, l, xh, xl);
            return (h, l);
        }

        static (double hi, double lo) evalPCFast(double uh, double ul)
        {
            double[] PCFast = Tbl_Cos_3;

            double t, h, l;
            h = PCFast[4];
            h = FusedMultiplyAdd(h, uh, PCFast[3]);
            h = FusedMultiplyAdd(h, uh, PCFast[2]);
            (h, l) = sMul(h, uh, ul);
            (h, t) = fastTwoSum(PCFast[0], h);
            l += PCFast[1] + t;
            return (h, l);
        }

        static Dint evalPS(Dint x, Dint x2)
        {
            ulong[] PSHi = Tbl_Cos_4;
            ulong[] PSLo = Tbl_Cos_5;
            int[] PSEx = Tbl_Cos_6;
            byte[] PSSgn = Tbl_Cos_7;


            Dint y = Dint.Mul21(x2, new Dint(PSLo[5], PSHi[5], PSEx[5], PSSgn[5]));
            y = Dint.Add(y, new Dint(PSLo[4], PSHi[4], PSEx[4], PSSgn[4]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PSLo[3], PSHi[3], PSEx[3], PSSgn[3]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PSLo[2], PSHi[2], PSEx[2], PSSgn[2]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PSLo[1], PSHi[1], PSEx[1], PSSgn[1]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PSLo[0], PSHi[0], PSEx[0], PSSgn[0]));
            y = Dint.Mul(y, x);

            return y;
        }

        static Dint evalPC(Dint x2)
        {
            ulong[] PCHi = Tbl_Cos_8;
            ulong[] PCLo = Tbl_Cos_9;
            int[] PCEx = Tbl_Cos_10;
            byte[] PCSgn = Tbl_Cos_7;


            Dint y = Dint.Mul21(x2, new Dint(PCLo[5], PCHi[5], PCEx[5], PCSgn[5]));
            y = Dint.Add(y, new Dint(PCLo[4], PCHi[4], PCEx[4], PCSgn[4]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PCLo[3], PCHi[3], PCEx[3], PCSgn[3]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PCLo[2], PCHi[2], PCEx[2], PCSgn[2]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PCLo[1], PCHi[1], PCEx[1], PCSgn[1]));
            y = Dint.Mul(y, x2);
            y = Dint.Add(y, new Dint(PCLo[0], PCHi[0], PCEx[0], PCSgn[0]));

            return y;
        }

        static (double h, double l) setDd(ulong c1, ulong c0)
        {
            double h, l;

            if (c1 != 0)
            {
                int e = Polyfill.LeadingZeroCount(c1);
                if (e != 0)
                {
                    c1 = (c1 << e) | (c0 >> -e);
                    c0 = c0 << e;
                }

                int f = 0x3fe - e;
                ulong t = ((ulong)f << 52) | ((c1 << 1) >> 12);
                h = Polyfill.UInt64BitsToDouble(t);

                c0 = (c1 << 53) | (c0 >> 11);
                if (c0 != 0)
                {
                    int g = Polyfill.LeadingZeroCount(c0);
                    if (g != 0)
                    {
                        c0 <<= g;
                    }

                    t = ((ulong)(f - 53 - g) << 52) | ((c0 << 1) >> 12);
                    l = Polyfill.UInt64BitsToDouble(t);
                }
                else
                {
                    l = 0;
                }
            }
            else if (c0 != 0)
            {
                int e = Polyfill.LeadingZeroCount(c0);
                int f = 0x3fe - 64 - e;

                ulong t = ((ulong)f << 52) | (c0 >> 12);
                h = Polyfill.UInt64BitsToDouble(t);

                c0 <<= 52;
                if (c0 != 0)
                {
                    int g = Polyfill.LeadingZeroCount(c0);
                    c0 <<= g + 1;
                    t = ((ulong)(f - 64 - g) << 52) | (c0 >> 12);
                    l = Polyfill.UInt64BitsToDouble(t);
                }
                else
                {
                    l = 0;
                }
            }
            else
            {
                h = l = 0;
            }

            return (h, l);
        }


        static (int value, double h, double l) reduceFast(double x, ulong[] T)
        {
            double h, l;

            if (x <= 6.2831853071795853)
            {
                const double Ch = 0.15915494309189535, Cl = -9.8393383375912429e-18;

                (h, l) = aMul(Ch, x);
                l = FusedMultiplyAdd(Cl, x, l);
            }
            else
            {
                ulong t = Polyfill.DoubleToUInt64Bits(x);
                int e = (int)(t >> 52) & 0x7ff;

                ulong m = (1ul << 52) | (t & 0xffffffffffffful);
                var c = new ulong[3];
                Uint128 u;

                if (e <= 1074)
                {
                    u = (Uint128)m * T[1];
                    c[0] = u.lo;
                    c[1] = u.hi;

                    u = (Uint128)m * T[0];
                    c[1] += u.lo;
                    c[2] = u.hi + (c[1] < u.lo ? 1ul : 0ul);

                    e = 1075 - e;
                }
                else
                {
                    int i = (e - 1138 + 63) / 64;

                    u = (Uint128)m * T[i + 2];
                    c[0] = u.lo;
                    c[1] = u.hi;

                    u = (Uint128)m * T[i + 1];
                    c[1] += u.lo;
                    c[2] = u.hi + (c[1] < u.lo ? 1ul : 0ul);

                    u = (Uint128)m * T[i];
                    c[2] += u.lo;
                    e = 1139 + (i << 6) - e;
                }

                if (e == 64)
                {
                    c[0] = c[1];
                    c[1] = c[2];
                }
                else
                {
                    c[0] = (c[1] << -e) | (c[0] >> e);
                    c[1] = (c[2] << -e) | (c[1] >> e);
                }

                (h, l) = setDd(c[1], c[0]);
            }


            {
                double i = BuiltinFloor(h * 2048.0);
                h = FusedMultiplyAdd(i, -0.00048828125, h);
                return ((int)i, h, l);
            }
        }


        static (double h, double l) fastDiv(double bh, double bl, double ah, double al)
        {
            double h, l;

            double y = 1.0 / ah;
            h = bh * y;

            double eh = FusedMultiplyAdd(ah, -h, bh);
            double el = FusedMultiplyAdd(al, -h, bl);

            l = y * (eh + el);
            return (h, l);
        }

        static (double err, double h, double l) tanFast(double x, ulong[] T)
        {
            double[] SC = Tbl_Cos_12;


            int neg = x < 0.0 ? 1 : 0, isTan = 1;
            double absx = neg != 0 ? -x : x;

            (int i, double h, double l) = reduceFast(absx, T);
            i &= 0x3ff;

            isTan ^= i >> 9;
            neg ^= i >> 9;
            i &= 0x1ff;

            if ((i & 0x100) != 0)
            {
                isTan ^= 1;
                i = 0x1ff - i;

                h = 0.00048828125 - h;
                l = -l;
            }

            if (i == 0 && h < 7.2759576141834259e-12)
            {
                return (1.0, h, l);
            }

            double sh, sl, ch, cl;

            h -= SC[i * 3 + 0];
            (h, l) = fastTwoSum(h, l);

            double uh, ul;
            (uh, ul) = aMul(h, h);
            ul = FusedMultiplyAdd(h + h, l, ul);

            (sh, sl) = evalPSFast(h, l, uh, ul);
            (ch, cl) = evalPCFast(uh, ul);

            double sh0, sl0, ch0, cl0, h1, l1;
            (sh0, sl0) = sMul(SC[i * 3 + 2], sh, sl);
            (ch0, cl0) = sMul(SC[i * 3 + 1], ch, cl);
            (h1, l1) = fastTwoSum(ch0, sh0);
            l1 += sl0 + cl0;

            double h2, l2;
            (ch, cl) = sMul(SC[i * 3 + 2], ch, cl);
            (sh, sl) = sMul(SC[i * 3 + 1], sh, sl);
            (h2, l2) = fastTwoSum(ch, -sh);
            l2 += cl - sl;

            if (isTan != 0)
            {
                (h, l) = fastDiv(h1, l1, h2, l2);
            }
            else
            {
                (h, l) = fastDiv(h2, l2, h1, l1);
            }


            double[] sgn = Tbl_Cos_13;
            h *= sgn[neg];
            l *= sgn[neg];
            return (h * 1.4928955695357043e-20, h, l);
        }


        ulong[] T = Tbl_Cos_1;



        static double tanAccurate(double x, ulong[] T)
        {
            ulong[] SHi = Tbl_Cos_14;
            ulong[] SLo = Tbl_Cos_15;
            int[] SEx = Tbl_Cos_16;
            byte[] SSgn = Tbl_Cos_17;

            ulong[] CHi = Tbl_Cos_18;
            ulong[] CLo = Tbl_Cos_19;
            int[] CEx = Tbl_Cos_20;
            byte[] CSgn = Tbl_Cos_17;



            double absx = (x > 0.0) ? x : -x;

            Dint X = Dint.FromDouble(absx);

            X = X.Reduce(T);

            int isTan = 1, neg = x < 0.0 ? 1 : 0;

            (X, int i) = X.Reduce2();
            i &= 0x3ff;

            if ((i & 0x200) != 0)
            {
                isTan = 0;
                neg ^= 1;
                i &= 0x1ff;
            }

            if ((i & 0x100) != 0)
            {
                isTan ^= 1;
                X = X with { sgn = 1 };
                X = Dint.Add(Dint.Magic, X);
                i = 0x1ff - i;
            }

            Dint U, V, X2;
            X2 = Dint.Mul(X, X);
            U = evalPC(X2);
            V = evalPS(X, X2);

            Dint Sin, UU, VV;
            UU = Dint.Mul(new Dint(SLo[i], SHi[i], SEx[i], SSgn[i]), U);
            VV = Dint.Mul(new Dint(CLo[i], CHi[i], CEx[i], CSgn[i]), V);
            Sin = Dint.Add(UU, VV);

            Dint Cos;
            U = Dint.Mul(new Dint(CLo[i], CHi[i], CEx[i], CSgn[i]), U);
            V = Dint.Mul(new Dint(SLo[i], SHi[i], SEx[i], SSgn[i]), V);
            V = V with { sgn = 1 - V.sgn };
            Cos = Dint.Add(U, V);

            if (isTan != 0)
            {
                U = DintDiv(Sin, Cos);
            }
            else
            {
                U = DintDiv(Cos, Sin);
            }

            ulong err = 86;
            ulong hi0, hi1, lo0, lo1;
            lo0 = U.lo - err;
            hi0 = U.hi - (lo0 > U.lo ? 1ul : 0ul);
            lo1 = U.lo + err;
            hi1 = U.hi + (lo1 < U.lo ? 1ul : 0ul);

            if ((hi0 >> 10) != (hi1 >> 10))
            {
                double[] exceptions = Tbl_Tan_0;

                for (int j = 0; j < exceptions.Length / 3; j++)
                {
                    if (Abs(x) == exceptions[j * 3 + 0])
                    {
                        return x > 0.0 ? exceptions[j * 3 + 1] + exceptions[j * 3 + 2] :
                            -exceptions[j * 3 + 1] - exceptions[j * 3 + 2];
                    }
                }
            }

            if (neg != 0)
            {
                U = U with { sgn = 1 - U.sgn };
            }

            double y = U.ToDouble();
            return y;
        }





        ulong t = Polyfill.DoubleToUInt64Bits(x);
        int e = (int)(t >> 52) & 0x7ff;

        if (e == 0x7ff)
        {
            if (t << 1 == 0x7fful << 53)
            {
                return 0.0 / 0.0;       // tan(+-inf)
            }

            return x + x;               // NaN
        }

        ulong ux = t & 0x7fffffffffffffff;
        if (ux <= 0x3e4d12ed0af1a27eul)
        {
            if (x == 0.0)
            {
                return x;
            }

            return FusedMultiplyAdd(x, 5.5511151231257827e-17, x);
        }

        double h, l, err;
        (err, h, l) = tanFast(x, T);

        double left = h + (l - err), right = h + (l + err);
        if (left == right)
        {
            return left;
        }

        return tanAccurate(x, T);
    }

    private static readonly double[] Tbl_Tan_0 = new double[] { 4.4703483581541778e-07, 4.4703483581544753e-07, 2.6469779601696883e-23, 8.9406967163076408e-07, 8.9406967163100231e-07, -7.8285755638033317e-45 };
}
