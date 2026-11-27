using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Ray-triangle intersection. Whether the ray hits is decided exactly and watertight (ExactRayTriangle): the ray
    /// is the half line Origin + t Direction, t &gt;= 0, exactly (ExactRayTriangle.LineDirection, #1672), the triangle is closed (a ray onto an edge or a vertex hits every triangle there, so it never
    /// misses all triangles of a shared edge or vertex), a ray in the plane of the triangle never hits it (as
    /// upstream), and a ray that starts on the triangle hits it at t = 0. No tolerance: upstream rejected rays within
    /// about 1e-8 of the plane as parallel and rounded edge tests on and near edges either way. RayParameter and TriangleBaryCoords are computed in doubles (the barycentrics clamped into the triangle),
    /// as upstream (with the direction and the edges scaled by powers of two into [1, 2), which leaves them the same doubles where no product
    /// underflows or overflows, and the same for every 2^k scaling of the input); where that D . N has no reliable relative accuracy (a ray within
    /// about 1e-8 of the triangle's plane, a sliver, |D . N| below 2^-900, a scaled component in the subnormal range) or a quotient is not finite,
    /// they are the exact quotients, each rounded once (#1686).
    /// </summary>
    public class IntrRay3Triangle3
    {
        Ray3d ray;
        public Ray3d Ray
        {
            get { return ray; }
            set { ray = value; Result = IntersectionResult.NotComputed; }
        }

        Triangle3d triangle;
        public Triangle3d Triangle
        {
            get { return triangle; }
            set { triangle = value; Result = IntersectionResult.NotComputed; }
        }

		public int Quantity = 0;
		public IntersectionResult Result = IntersectionResult.NotComputed;
		public IntersectionType Type = IntersectionType.Empty;

        public bool IsSimpleIntersection {
            get { return Result == IntersectionResult.Intersects && Type == IntersectionType.Point; }
        }


        public double RayParameter;
        public Vector3d TriangleBaryCoords;


		public IntrRay3Triangle3(Ray3d r, Triangle3d t)
		{
			ray = r; triangle = t;
		}


        public IntrRay3Triangle3 Compute()
        {
            Find();
            return this;
        }


        public bool Find()
        {
            if (Result != IntersectionResult.NotComputed)
                return (Result != g3.IntersectionResult.NoIntersection);
            Type = IntersectionType.Empty;
            Quantity = 0;

            double t, b1, b2;
            Vector3d d = ExactRayTriangle.LineDirection(ray.Origin, ray.Direction);
            if (hit(ref ray, ref d, ref triangle.V0, ref triangle.V1, ref triangle.V2, true, out t, out b1, out b2) == false) {
                Result = IntersectionResult.NoIntersection;
                return false;
            }
            RayParameter = t;
            TriangleBaryCoords = new Vector3d(Math.Max(0, 1 - b1 - b2), b1, b2);      // (clamped: b1 + b2 can round past 1 by an ulp)
            Type = IntersectionType.Point;
            Quantity = 1;
            Result = IntersectionResult.Intersects;
            return true;
        }



        /// <summary>
        /// minimal intersection test, computes ray-t. The same decision as Find(): see the class remarks below.
        /// </summary>
        public static bool Intersects(ref Ray3d ray, ref Vector3d V0, ref Vector3d V1, ref Vector3d V2, out double rayT)
        {
            Vector3d d = ExactRayTriangle.LineDirection(ray.Origin, ray.Direction);
            return Intersects(ref ray, ref d, ref V0, ref V1, ref V2, out rayT);
        }

        /// <summary> Intersects with the ray's exact line direction (ExactRayTriangle.LineDirection) computed once per ray </summary>
        internal static bool Intersects(ref Ray3d ray, ref Vector3d lineDirection, ref Vector3d V0, ref Vector3d V1, ref Vector3d V2, out double rayT)
        {
            double b1, b2;
            return hit(ref ray, ref lineDirection, ref V0, ref V1, ref V2, false, out rayT, out b1, out b2);
        }

        // Whether the ray hits is decided exactly (ExactRayTriangle.RayHitClosedDirection: the line of the origin and
        // the exact direction, lineDirection a positive power-of-two multiple of ray.Direction or ray.Direction itself, against the closed triangle, so that a ray onto a shared edge or vertex hits every triangle
        // there and never misses all of them, at any scale). The ray parameter and the barycentrics are computed in
        // doubles as before (identical to upstream for every ray that both hit; near-parallel rays within upstream's
        // tolerance now hit too, and a ray within rounding of the edge of a lone or boundary triangle can change
        // between hit and miss, as the exact decision says).
        static bool hit(ref Ray3d ray, ref Vector3d lineDirection, ref Vector3d V0, ref Vector3d V1, ref Vector3d V2, bool bary,
            out double rayT, out double b1, out double b2)
        {
            rayT = double.MaxValue;
            b1 = b2 = 0;
            int h = ExactRayTriangle.RayHitClosedDirection(ray.Origin, lineDirection, V0, V1, V2);
            if (h == ExactRayTriangle.Miss)
                return false;

            // Solve Q + t*D = b1*E1 + b2*E2 (Q = kDiff, D = ray direction,
            // E1 = kEdge1, E2 = kEdge2, N = Cross(E1,E2)) by
            //   |Dot(D,N)|*b1 = sign(Dot(D,N))*Dot(D,Cross(Q,E2))
            //   |Dot(D,N)|*b2 = sign(Dot(D,N))*Dot(D,Cross(E1,Q))
            //   |Dot(D,N)|*t = -sign(Dot(D,N))*Dot(Q,N)
            Vector3d diff = ray.Origin - V0;
            Vector3d edge1 = V1 - V0;
            Vector3d edge2 = V2 - V0;
            // (the products below are cubes of the coordinates: they underflow to 0 below about 1e-103 and overflow above
            // about 1e102, which made t 0 or infinite for a triangle of that size. Scaled by a power of two the rounding
            // is the same, and t is scaled back: t' = s t, as the numerator is cubic and the denominator quadratic in s)
            int scaleExp = ScaleExponent(ref edge1, ref edge1, ref edge2);   // (by the edges into [1, 2): a far origin against a small triangle keeps its range; a diff that overflows then gives a quotient that is not finite: the exact route)
            if (scaleExp != 0) {
                diff = ScaledBy(diff, scaleExp);
                edge1 = ScaledBy(edge1, scaleExp);
                edge2 = ScaledBy(edge2, scaleExp);
            }
            Vector3d normal = edge1.Cross(ref edge2);
            // (the direction likewise, by its own power of two 2^dirExp into [1, 2): a subnormal direction made 1 / |D . N|
            // infinite and t NaN, #1672. t is scaled back by 2^dirExp, the barycentrics are ratios of terms linear in D. The
            // scaling is exact unless a component falls into the subnormal range or to 0 (a direction whose components
            // span more than about 1000 binades, eg (2^150, 2^-930, 0)): then that component is rounded, which changes t
            // and the barycentrics by its rounding only, as any double computation of them; the hit decision does not
            // use it)
            Vector3d dir = ray.Direction;
            int dirExp = ScaleExponent(ref dir, ref dir, ref dir);
            if (dirExp != 0)
                dir = new Vector3d(ExactPoint3.ScaleB(dir.x, dirExp), ExactPoint3.ScaleB(dir.y, dirExp), ExactPoint3.ScaleB(dir.z, dirExp));
            // the hit is exact for the ray itself (the line of ray.Origin and ray.Direction, #1672), so the true t, b1,
            // b2 are >= 0 and a negative rounded value is tiny: their magnitudes (the same doubles as upstream's
            // sign-corrected values wherever upstream hit; a rounded D . N of the wrong sign, near parallel, cannot
            // make them negative)
            double DdN = Math.Abs(dir.Dot(ref normal));
            double inv = (1) / DdN;
            // The exact quotients instead of the double ones (#1686; the hit implies D . N != 0 exactly, so they exist):
            // - a shallow ray or a sliver: the rounded D . N has an error of at most about 5 u S, S = sum |d_i| (|e1_j e2_k| +
            //   |e1_k e2_j|) (the rounding of N and of the dot product), so where D . N < 2^-26 S it has a relative error above
            //   about 2^-24 (cancellation: a ray within about 1e-8 of the plane, the rays upstream rejected as parallel); on
            //   the double route D . N has a relative error of at most about 4e-8, and t's numerator rounds by about
            //   u |Q| |N|, so the hit point lies within about 2^-24 |Q| of the exact one along the ray;
            // - |D . N| below 2^-900: the products can be subnormal, without relative accuracy, and 1 / DdN can overflow;
            // - a scaled component in the subnormal range (an input whose components span more than about 1000 binades:
            //   the scaling rounded it);
            // - a double quotient that is not finite (below).
            // Everything is computed from the scaled doubles, the same for every 2^k scaling of the input, so is the route.
            double S = Math.Abs(dir.x) * (Math.Abs(edge1.y * edge2.z) + Math.Abs(edge1.z * edge2.y))
                + Math.Abs(dir.y) * (Math.Abs(edge1.z * edge2.x) + Math.Abs(edge1.x * edge2.z))
                + Math.Abs(dir.z) * (Math.Abs(edge1.x * edge2.y) + Math.Abs(edge1.y * edge2.x));
            if (!(DdN >= ReliableDdN) || !(DdN >= ShallowRatio * S) || has_subnormal(ref dir) || has_subnormal(ref edge1)
                || has_subnormal(ref edge2) || has_subnormal(ref diff)) {
                exact_parameters(ref ray, ref V0, ref V1, ref V2, h == ExactRayTriangle.AtOrigin, bary, out rayT, out b1, out b2);
                return true;
            }
            if (h == ExactRayTriangle.AtOrigin) {
                rayT = 0;
            } else {
                rayT = Math.Abs(diff.Dot(ref normal)) * inv;
                if (double.IsNaN(rayT) || double.IsInfinity(rayT)) {
                    exact_parameters(ref ray, ref V0, ref V1, ref V2, false, bary, out rayT, out b1, out b2);
                    return true;
                } else if (scaleExp != 0 || dirExp != 0) {
                    rayT = ExactPoint3.ScaleB(rayT, dirExp - scaleExp);      // (one rounding, also for a subnormal t)
                    if (double.IsInfinity(rayT))
                        rayT = double.MaxValue;      // (farther than the doubles reach)
                }
            }
            if (bary) {
                Vector3d cross = diff.Cross(ref edge2);
                double DdQxE2 = Math.Abs(dir.Dot(ref cross));
                cross = edge1.Cross(ref diff);
                double DdE1xQ = Math.Abs(dir.Dot(ref cross));
                if (DdQxE2 + DdE1xQ > DdN) {
                    // (rounded past the third edge, where upstream rejected: onto it)
                    double sum = DdQxE2 + DdE1xQ;
                    b1 = DdQxE2 / sum;
                    b2 = DdE1xQ / sum;
                } else {
                    b1 = DdQxE2 * inv;
                    b2 = DdE1xQ * inv;
                }
                if (double.IsNaN(b1) || double.IsInfinity(b1) || double.IsNaN(b2) || double.IsInfinity(b2)) {
                    double t;
                    exact_parameters(ref ray, ref V0, ref V1, ref V2, h == ExactRayTriangle.AtOrigin, true, out t, out b1, out b2);
                }
            }
            return true;
        }

        // 2^-900: a |D . N| (direction and edges in [1, 2)) at or above it is a normal double whose partial products lose at
        // most 2^-1075 each (a relative 2^-175), and 1 / DdN is finite
        const double ReliableDdN = 1.1830521861667747e-271;      // 2^-900
        // 2^-26: D . N at least this fraction of S (above), so its rounding error (at most about 5 u S) is a relative 2^-24 at most
        const double ShallowRatio = 1.4901161193847656e-08;      // 2^-26
        const double MinNormal = 2.2250738585072014e-308;        // 2^-1022

        static bool has_subnormal(ref Vector3d v)
        {
            return (v.x != 0 && Math.Abs(v.x) < MinNormal) || (v.y != 0 && Math.Abs(v.y) < MinNormal) || (v.z != 0 && Math.Abs(v.z) < MinNormal);
        }

        /// <summary>
        /// t, b1, b2 of the hit as the exact quotients, each rounded once (ExactPoint3.ToDouble): with Q = O - V0,
        /// E1 = V1 - V0, E2 = V2 - V0, N = E1 x E2 in integers (all coordinates at their common lowest exponent, which
        /// cancels in the quotients), t = -(Q . N) / (D . N), b1 = D . (Q x E2) / (D . N), b2 = D . (E1 x Q) / (D . N).
        /// D . N != 0 for every hit (a line with D . N = 0 is parallel to the plane or in it, and meets no closed triangle
        /// as ExactRayTriangle decides). t at the origin is 0; a t beyond the doubles is double.MaxValue.
        /// </summary>
        static void exact_parameters(ref Ray3d ray, ref Vector3d V0, ref Vector3d V1, ref Vector3d V2, bool atOrigin, bool bary,
            out double rayT, out double b1, out double b2)
        {
            var I = to_integers(ray.Origin, ray.Direction, V0, V1, V2);
            System.Numerics.BigInteger[] O = I[0], D = I[1], A = I[2], B = I[3], C = I[4];
            var Q = sub(O, A); var E1 = sub(B, A); var E2 = sub(C, A);
            var N = cross(E1, E2);
            var DN = dot(D, N);
            rayT = 0;
            b1 = b2 = 0;
            if (DN.IsZero) {
                // (not a hit; not reached)
                b1 = b2 = 1.0 / 3.0;
                return;
            }
            if (atOrigin == false) {
                rayT = Math.Max(0, ExactPoint3.ToDouble(-dot(Q, N), DN, 0));
                if (double.IsInfinity(rayT))
                    rayT = double.MaxValue;      // (farther than the doubles reach)
            }
            if (bary) {
                b1 = Math.Max(0, ExactPoint3.ToDouble(dot(D, cross(Q, E2)), DN, 0));
                b2 = Math.Max(0, ExactPoint3.ToDouble(dot(D, cross(E1, Q)), DN, 0));
                // (b1 + b2 <= 1 also rounded: each is within 2^-54 of the exact value, so their sum within the half ulp
                // above 1 of an exact sum <= 1; x = 1 - b1 - b2 can still be an ulp below 0: clamped in Find)
            }
        }

        static System.Numerics.BigInteger[] sub(System.Numerics.BigInteger[] a, System.Numerics.BigInteger[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        static System.Numerics.BigInteger[] cross(System.Numerics.BigInteger[] a, System.Numerics.BigInteger[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        static System.Numerics.BigInteger dot(System.Numerics.BigInteger[] a, System.Numerics.BigInteger[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        // the coordinates as integers at their common lowest exponent (finite values: the hit decision refused others)
        static System.Numerics.BigInteger[][] to_integers(params Vector3d[] pts)
        {
            int n = pts.Length * 3;
            var mant = new long[n];
            var exp = new int[n];
            int emin = int.MaxValue;
            for (int i = 0; i < n; ++i) {
                long bits = BitConverter.DoubleToInt64Bits(pts[i / 3][i % 3]);
                int e = (int)((bits >> 52) & 0x7FF);
                long frac = bits & 0xFFFFFFFFFFFFFL;
                if (e == 0) exp[i] = -1074; else { frac |= 1L << 52; exp[i] = e - 1075; }
                mant[i] = bits < 0 ? -frac : frac;
                if (mant[i] != 0) emin = Math.Min(emin, exp[i]);
            }
            var r = new System.Numerics.BigInteger[pts.Length][];
            for (int i = 0; i < pts.Length; ++i) {
                r[i] = new System.Numerics.BigInteger[3];
                for (int j = 0; j < 3; ++j) {
                    int k = 3 * i + j;
                    r[i][j] = mant[k] == 0 ? System.Numerics.BigInteger.Zero : new System.Numerics.BigInteger(mant[k]) << (exp[k] - emin);
                }
            }
            return r;
        }


        // the power of two 2^-e that brings the largest component of the three vectors to [1, 2) (0 if all zero or not
        // finite). Always, since #1686 (before: only outside [2^-100, 2^100]): the computation below then sees the same
        // doubles for every 2^k scaling of the input, so its route (the double quotients or the exact ones) and its
        // result are the same bit for bit; for an input whose products neither underflow nor overflow the power-of-two
        // scaling and its undoing are exact, so t and the barycentrics are the doubles they were
        static int ScaleExponent(ref Vector3d a, ref Vector3d b, ref Vector3d c)
        {
            double m = Math.Max(MaxAbs(ref a), Math.Max(MaxAbs(ref b), MaxAbs(ref c)));
            if (!(m > 0) || !(m <= double.MaxValue))
                return 0;
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            if (e == 0)
                e = (int)((BitConverter.DoubleToInt64Bits(m * 18446744073709551616.0) >> 52) & 0x7FF) - 64;      // subnormal
            return -(e - 1023);
        }

        // 2^e exactly, e in [-1022, 1023] (not Math.Pow: its exactness is the platform's)
        static double Pow2(int e)
        {
            return BitConverter.Int64BitsToDouble((long)(e + 1023) << 52);
        }

        static double MaxAbs(ref Vector3d v)
        {
            return Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
        }

        // x * 2^e, exactly unless the result is subnormal or overflows (in two steps: 2^e itself is out of range beyond +-1023)
        static double ScaledBy(double x, int e)
        {
            int half = e / 2;
            return x * Pow2(half) * Pow2(e - half);
        }

        static Vector3d ScaledBy(Vector3d v, int e)
        {
            return new Vector3d(ScaledBy(v.x, e), ScaledBy(v.y, e), ScaledBy(v.z, e));
        }
    }
}
