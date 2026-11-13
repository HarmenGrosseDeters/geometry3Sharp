// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Functions composed from the correctly rounded ones of this port with + - * / and Math.Sqrt (all exactly defined by
// IEEE 754): the same bits on every platform, but not correctly rounded themselves. Not part of CORE-MATH.
using System;

namespace g3.CoreMathPort
{
    internal static partial class CoreMath
    {
        /// <summary>
        /// The arc cosine as Atan2(sqrt((1 - c) (1 + c)), c): the same double on every platform (Atan2 is correctly
        /// rounded, the rest is IEEE arithmetic and Sqrt), within 1 ulp of Math.Acos on glibc x64 over the sweeps of
        /// CoreMathComposedTests (816000 arguments, uniform and at distances 2^-k from -1, 0 and 1; not correctly
        /// rounded: 60501 of them, 7.4 %, differ from Math.Acos by one ulp; the test allows 2 for other platforms). The domain is that of Math.Acos: NaN for NaN and
        /// for |c| &gt; 1, 0 for 1, pi (Math.PI) for -1, pi / 2 for 0. The factor that is small near an end, 1 - c near 1 and 1 + c near -1, is exact
        /// there (Sterbenz, |c| &gt;= 1/2), so the result keeps its relative accuracy where 1 - c * c would cancel. A correctly rounded Acos (a port from CORE-MATH) can replace it later; the
        /// tests bound what would change.
        /// </summary>
        internal static double AcosByAtan2(double c)
        {
            if (!(c >= -1 && c <= 1))
                return double.NaN;
            return Atan2(Math.Sqrt((1 - c) * (1 + c)), c);
        }
    }
}
