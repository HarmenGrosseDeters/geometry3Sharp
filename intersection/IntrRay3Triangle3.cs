using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Ray-triangle intersection. Whether the ray hits is decided exactly and watertight (ExactRayTriangle): the ray
    /// is the half line from Origin through Origin + 2^m Direction (rounded; 2^m from the exponents, so 2^k scaling
    /// is exact), the triangle is closed (a ray onto an edge or a vertex hits every triangle there, so it never
    /// misses all triangles of a shared edge or vertex), a ray in the plane of the triangle never hits it (as
    /// upstream), and a ray that starts on the triangle hits it at t = 0. No tolerance: upstream rejected rays within
    /// about 1e-8 of the plane as parallel and rounded edge tests on and near edges either way. RayParameter and TriangleBaryCoords are computed in doubles (the barycentrics clamped into the triangle).
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
            TriangleBaryCoords = new Vector3d(1 - b1 - b2, b1, b2);
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
            int scaleExp = ScaleExponent(ref edge1, ref edge1, ref edge2);   // (by the edges: a far origin against a small triangle keeps its range; a ratio of diff to the edges beyond about 1e300 is not handled)
            if (scaleExp != 0) {
                diff = ScaledBy(diff, scaleExp);
                edge1 = ScaledBy(edge1, scaleExp);
                edge2 = ScaledBy(edge2, scaleExp);
            }
            Vector3d normal = edge1.Cross(ref edge2);
            // (the direction likewise, by its own power of two 2^dirExp, if its largest component is outside [2^-100,
            // 2^100]: a subnormal direction made 1 / |D . N| infinite and t NaN, #1672. t is scaled back by 2^dirExp,
            // the barycentrics are ratios of terms linear in D; a direction within that range keeps its bits. The
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
            if (h == ExactRayTriangle.AtOrigin) {
                rayT = 0;
            } else {
                rayT = Math.Abs(diff.Dot(ref normal)) * inv;
                if (double.IsNaN(rayT) || double.IsInfinity(rayT)) {
                    // D . N rounded to 0 (a ray within about 1e-16 of the plane): the projection of the centroid
                    Vector3d c = (V0 + V1 + V2) / 3.0;
                    rayT = Math.Max(0, (c - ray.Origin).Dot(dir) / dir.LengthSquared);
                    if (dirExp != 0) {
                        rayT = ExactPoint3.ScaleB(rayT, dirExp);
                        if (double.IsInfinity(rayT))
                            rayT = double.MaxValue;      // (farther than the doubles reach)
                    }
                } else if (scaleExp != 0 || dirExp != 0) {
                    rayT = dirExp == 0 ? ScaledBy(rayT, -scaleExp) : ExactPoint3.ScaleB(rayT, dirExp - scaleExp);
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
                if (double.IsNaN(b1) || double.IsInfinity(b1) || double.IsNaN(b2) || double.IsInfinity(b2))
                    b1 = b2 = 1.0 / 3.0;
            }
            return true;
        }


        // the power of two 2^-e that brings the largest component of the three vectors to [1, 2), or 0 if it is within
        // [2^-100, 2^100] (or all zero or not finite): cubes of those components neither underflow nor overflow
        static int ScaleExponent(ref Vector3d a, ref Vector3d b, ref Vector3d c)
        {
            double m = Math.Max(MaxAbs(ref a), Math.Max(MaxAbs(ref b), MaxAbs(ref c)));
            if (!(m > 0) || !(m <= double.MaxValue) || (m >= 7.8886090522101181e-31 && m <= 1.2676506002282294e30))
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
