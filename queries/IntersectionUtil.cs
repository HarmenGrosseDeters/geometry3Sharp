using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    public enum IntersectionResult
    {
        NotComputed,
        Intersects,
        NoIntersection,
		InvalidQuery
    }

    public enum IntersectionType
    {
        Empty, Point, Segment, Line, Polygon, Plane, Unknown
    }

    /// <summary>
    /// returned by linear-primitive intersection functions
    /// </summary>
    public struct LinearIntersection
    {
        public bool intersects;
        public int numIntersections;       // 0, 1, or 2
        public Interval1d parameter;       // t-values along ray
    }



    public static class IntersectionUtil
    {



        /// <summary>
        /// Does the ray hit the triangle: the exact, watertight decision of IntrRay3Triangle3 (ExactRayTriangle), without
        /// constructing additional data structures. rayDirection need not be normalized.
        /// </summary>
        public static bool Intersects(Vector3d rayOrigin, Vector3d rayDirection, Vector3d V0, Vector3d V1, Vector3d V2)
        {
            Vector3d p = ExactRayTriangle.SecondPoint(rayOrigin, rayDirection);
            return ExactRayTriangle.RayHitClosed(rayOrigin, p, V0, V1, V2) != ExactRayTriangle.Miss;
        }



        /// <summary>
        /// Test if line intersects sphere
        /// </summary>
        public static bool LineSphereTest(ref Vector3d lineOrigin, ref Vector3d lineDirection, ref Vector3d sphereCenter, double sphereRadius)
        {
            // [RMS] adapted from GeometricTools GTEngine
            // https://www.geometrictools.com/GTEngine/Include/Mathematics/GteIntrLine3Sphere3.h

            // The sphere is (X-C)^T*(X-C)-1 = 0 and the line is X = P+t*D.
            // Substitute the line equation into the sphere equation to obtain a
            // quadratic equation Q(t) = t^2 + 2*a1*t + a0 = 0, where a1 = D^T*(P-C),
            // and a0 = (P-C)^T*(P-C)-1.

            Vector3d diff = lineOrigin - sphereCenter;
            double a0 = diff.LengthSquared - sphereRadius * sphereRadius;
            double a1 = lineDirection.Dot(diff);

            // Intersection occurs when Q(t) has real roots.
            double discr = a1 * a1 - a0;
            return (discr >= 0);
        }


        /// <summary>
        /// Intersect ray with sphere and return intersection info (# hits, ray parameters)
        /// </summary>
        public static bool LineSphere(ref Vector3d lineOrigin, ref Vector3d lineDirection, ref Vector3d sphereCenter, double sphereRadius, ref LinearIntersection result)
        {
            // [RMS] adapted from GeometricTools GTEngine
            // https://www.geometrictools.com/GTEngine/Include/Mathematics/GteIntrLine3Sphere3.h

            // The sphere is (X-C)^T*(X-C)-1 = 0 and the line is X = P+t*D.
            // Substitute the line equation into the sphere equation to obtain a
            // quadratic equation Q(t) = t^2 + 2*a1*t + a0 = 0, where a1 = D^T*(P-C),
            // and a0 = (P-C)^T*(P-C)-1.
            Vector3d diff = lineOrigin - sphereCenter;
            double a0 = diff.LengthSquared - sphereRadius * sphereRadius;
            double a1 = lineDirection.Dot(diff);

            // Intersection occurs when Q(t) has real roots.
            double discr = a1 * a1 - a0;
            if (discr > 0) {
                result.intersects = true;
                result.numIntersections = 2;
                double root = Math.Sqrt(discr);
                result.parameter.a = -a1 - root;
                result.parameter.b = -a1 + root;
            } else if (discr < 0) {
                result.intersects = false;
                result.numIntersections = 0;
            } else {
                result.intersects = true;
                result.numIntersections = 1;
                result.parameter.a = -a1;
                result.parameter.b = -a1;
            }
            return result.intersects;
        }
        public static LinearIntersection LineSphere(ref Vector3d lineOrigin, ref Vector3d lineDirection, ref Vector3d sphereCenter, double sphereRadius)
        {
            LinearIntersection result = new LinearIntersection();
            LineSphere(ref lineOrigin, ref lineDirection, ref sphereCenter, sphereRadius, ref result);
            return result;
        }



        /// <summary>
        /// Test if ray intersects sphere
        /// </summary>
        public static bool RaySphereTest(ref Vector3d rayOrigin, ref Vector3d rayDirection, ref Vector3d sphereCenter, double sphereRadius)
        {
            // [RMS] adapted from GeometricTools GTEngine
            // https://www.geometrictools.com/GTEngine/Include/Mathematics/GteIntrRay3Sphere3.h

            // The sphere is (X-C)^T*(X-C)-1 = 0 and the line is X = P+t*D.
            // Substitute the line equation into the sphere equation to obtain a
            // quadratic equation Q(t) = t^2 + 2*a1*t + a0 = 0, where a1 = D^T*(P-C),
            // and a0 = (P-C)^T*(P-C)-1.

            Vector3d diff = rayOrigin - sphereCenter;
            double a0 = diff.LengthSquared - sphereRadius * sphereRadius;
            if (a0 <= 0)
                return true;  // P is inside the sphere.
            // else: P is outside the sphere
            double a1 = rayDirection.Dot(diff);
            if (a1 >= 0)
                return false;

            // Intersection occurs when Q(t) has double roots.
            double discr = a1 * a1 - a0;
            return (discr >= 0);
        }


        /// <summary>
        /// Intersect ray with sphere and return intersection info (# hits, ray parameters)
        /// </summary>
        public static bool RaySphere(ref Vector3d rayOrigin, ref Vector3d rayDirection, ref Vector3d sphereCenter, double sphereRadius, ref LinearIntersection result)
        {
            // [RMS] adapted from GeometricTools GTEngine
            // https://www.geometrictools.com/GTEngine/Include/Mathematics/GteIntrRay3Sphere3.h

            LineSphere(ref rayOrigin, ref rayDirection, ref sphereCenter, sphereRadius, ref result);
            if ( result.intersects ) {
                // The line containing the ray intersects the sphere; the t-interval
                // is [t0,t1].  The ray intersects the sphere as long as [t0,t1]
                // overlaps the ray t-interval [0,+infinity).
                if ( result.parameter.b < 0 ) {
                    result.intersects = false;
                    result.numIntersections = 0;
                } else if ( result.parameter.a < 0 ) {
                    result.numIntersections--;
                    result.parameter.a = result.parameter.b;
                }
            }
            return result.intersects;
        }
        public static LinearIntersection RaySphere(ref Vector3d rayOrigin, ref Vector3d rayDirection, ref Vector3d sphereCenter, double sphereRadius)
        {
            LinearIntersection result = new LinearIntersection();
            RaySphere(ref rayOrigin, ref rayDirection, ref sphereCenter, sphereRadius, ref result);
            return result;
        }

    }



}
