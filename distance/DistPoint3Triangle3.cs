using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    // ported from WildMagic 5 
    // https://www.geometrictools.com/Downloads/Downloads.html

    public class DistPoint3Triangle3
    {
        Vector3d point;
        public Vector3d Point
        {
            get { return point; }
            set { point = value; DistanceSquared = -1.0; }
        }

        Triangle3d triangle;
        public Triangle3d Triangle
        {
            get { return triangle; }
            set { triangle = value; DistanceSquared = -1.0; }
        }

        public double DistanceSquared = -1.0;

        public Vector3d TriangleClosest;
        public Vector3d TriangleBaryCoords;


        public DistPoint3Triangle3(Vector3d PointIn, Triangle3d TriangleIn )
        {
            point = PointIn; triangle = TriangleIn;
        }

        public DistPoint3Triangle3 Compute()
        {
            GetSquared();
            return this;
        }

        public double Get()
        {
            return Math.Sqrt(GetSquared());
        }


        public double GetSquared()
        {
            if (DistanceSquared >= 0)
                return DistanceSquared;

            DistanceSquared = DistanceSqr(ref point, ref triangle, out TriangleClosest, out TriangleBaryCoords);
            return DistanceSquared;
        }


        static double degenerate_distance_sqr(ref Vector3d point, ref Triangle3d tri, out Vector3d closestPoint, out Vector3d baryCoords)
        {
            double best = double.MaxValue;
            closestPoint = tri.V0; baryCoords = new Vector3d(1, 0, 0);
            for (int k = 0; k < 3; ++k) {
                Vector3d a = tri[k], b = tri[(k + 1) % 3], ab = b - a;
                double l2 = ab.LengthSquared;
                double t = (l2 > 0) ? MathUtil.Clamp((point - a).Dot(ab) / l2, 0, 1) : 0;
                Vector3d q = a + t * ab;
                double d = q.DistanceSquared(point);
                if (d < best) {
                    best = d; closestPoint = q;
                    baryCoords = Vector3d.Zero; baryCoords[k] = 1 - t; baryCoords[(k + 1) % 3] = t;
                }
            }
            return best;
        }

        // Range: squared edge lengths (and squared distances) must be representable, so edges and distances below about 1e150 (the
        // result may be +Infinity or double.MaxValue beyond, and noise of the algorithm is scaled up with the triangle).
        // The products a00, a11 and det of the algorithm below must be normal doubles with a finite reciprocal: for edges shorter than
        // about 1e-68 or longer than about 1e75 (or a very short edge beside a long one) they under- or overflow, and the division by a
        // subnormal det gave NaN. Outside this range the triangle is scaled by a power of two (exact) first.
        internal const double SafeProductMin = 1e-135, SafeProductMax = 1e150;

        /// <summary> the products of the algorithm are in the range where it is accurate (see SafeProductMin) </summary>
        internal static bool ProductsAreSafe(double a00, double a11)
        {
            return a00 >= SafeProductMin && a00 <= SafeProductMax && a11 >= SafeProductMin && a11 <= SafeProductMax;
        }

        public static double DistanceSqr(ref Vector3d point, ref Triangle3d triangle, out Vector3d closestPoint, out Vector3d baryCoords )
        {
            Vector3d e0 = triangle.V1 - triangle.V0, e1 = triangle.V2 - triangle.V0;
            if (!ProductsAreSafe(e0.LengthSquared, e1.LengthSquared)) {
                double m0 = Math.Max(Math.Abs(e0.x), Math.Max(Math.Abs(e0.y), Math.Abs(e0.z)));
                double m1 = Math.Max(Math.Abs(e1.x), Math.Max(Math.Abs(e1.y), Math.Abs(e1.z)));
                double big = Math.Max(m0, m1), small = Math.Min(m0, m1);
                // the range limits of the scaling (edges beyond 2^+-1000, or none): the nearest of the three edges, unscaled; squared
                // edge lengths and distances do not exist as doubles there, the result may be double.MaxValue
                if (!(big > 9.332636185032189e-302) || !(big < 1.0715086071862673e+301))
                    return degenerate_distance_sqr(ref point, ref triangle, out closestPoint, out baryCoords);
                // relative to V0 (the vertices themselves may be huge beside the edges), scaled so that the longest edge is about 1
                int e = ExponentOf(big);
                double down = PowerOfTwo(-e), up = PowerOfTwo(e);
                var scaled = new Triangle3d(Vector3d.Zero, e0 * down, e1 * down);
                Vector3d p = (point - triangle.V0) * down;
                double d2; Vector3d cp;
                if (!(small > 1e-60 * big)) {
                    // an edge 1e60 times shorter than the other (or none): a segment to the precision of a double
                    d2 = degenerate_distance_sqr(ref p, ref scaled, out cp, out baryCoords);
                } else {
                    d2 = DistanceSqrCore(ref p, ref scaled, out cp, out baryCoords);
                    // a point so far (more than about 1e154 edge lengths) that a square overflows: the three edges are as good as the
                    // triangle there (the triangle is a point to the precision of a double)
                    if (!(d2 < double.MaxValue))
                        d2 = degenerate_distance_sqr(ref p, ref scaled, out cp, out baryCoords);
                }
                // (the scaled square of a point that far overflows in the nearest-edge answer too: unscaled, the square of the distance is a double)
                if (!(d2 < double.MaxValue))
                    return degenerate_distance_sqr(ref point, ref triangle, out closestPoint, out baryCoords);
                closestPoint = triangle.V0 + cp * up;
                return d2 * up * up;
            }
            return DistanceSqrCore(ref point, ref triangle, out closestPoint, out baryCoords);
        }

        // the exponent e of a normal positive double m = 1.f * 2^e
        static int ExponentOf(double m) { return (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF) - 1023; }
        // 2^e for e in [-1022, 1023], exactly
        static double PowerOfTwo(int e) { return BitConverter.Int64BitsToDouble((long)(e + 1023) << 52); }

        static double DistanceSqrCore(ref Vector3d point, ref Triangle3d triangle, out Vector3d closestPoint, out Vector3d baryCoords )
        {
            Vector3d diff = triangle.V0 - point;
            Vector3d edge0 = triangle.V1 - triangle.V0;
            Vector3d edge1 = triangle.V2 - triangle.V0;
            double a00 = edge0.LengthSquared;
            double a01 = edge0.Dot(ref edge1);
            double a11 = edge1.LengthSquared;
            double b0 = diff.Dot(ref edge0);
            double b1 = diff.Dot(ref edge1);
            double c = diff.LengthSquared;
            double det = Math.Abs(a00 * a11 - a01 * a01);
            double s = a01 * b1 - a11 * b0;
            double t = a01 * b0 - a00 * b1;
            double sqrDistance;
            // degenerate (zero area, or a sliver with angles below ~1e-6: the divisions below would give NaN or
            // garbage, the edges are within 1e-6 x their length of it): the
            // nearest of the three edges
            if (!(det > 1e-12 * a00 * a11) || a00 <= 0 || a11 <= 0)
                return degenerate_distance_sqr(ref point, ref triangle, out closestPoint, out baryCoords);

            if (s + t <= det) {
                if (s < 0) {
                    if (t < 0)  // region 4
                    {
                        if (b0 < 0) {
                            t = 0;
                            if (-b0 >= a00) {
                                s = 1;
                                sqrDistance = a00 + (2) * b0 + c;
                            } else {
                                s = -b0 / a00;
                                sqrDistance = b0 * s + c;
                            }
                        } else {
                            s = 0;
                            if (b1 >= 0) {
                                t = 0;
                                sqrDistance = c;
                            } else if (-b1 >= a11) {
                                t = 1;
                                sqrDistance = a11 + (2) * b1 + c;
                            } else {
                                t = -b1 / a11;
                                sqrDistance = b1 * t + c;
                            }
                        }
                    } else  // region 3
                      {
                        s = 0;
                        if (b1 >= 0) {
                            t = 0;
                            sqrDistance = c;
                        } else if (-b1 >= a11) {
                            t = 1;
                            sqrDistance = a11 + (2) * b1 + c;
                        } else {
                            t = -b1 / a11;
                            sqrDistance = b1 * t + c;
                        }
                    }
                } else if (t < 0)  // region 5
                  {
                    t = 0;
                    if (b0 >= 0) {
                        s = 0;
                        sqrDistance = c;
                    } else if (-b0 >= a00) {
                        s = 1;
                        sqrDistance = a00 + (2) * b0 + c;
                    } else {
                        s = -b0 / a00;
                        sqrDistance = b0 * s + c;
                    }
                } else  // region 0
                  {
                    // minimum at interior point
                    double invDet = (1) / det;
                    s *= invDet;
                    t *= invDet;
                    sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                        t * (a01 * s + a11 * t + (2) * b1) + c;
                }
            } else {
                double tmp0, tmp1, numer, denom;

                if (s < 0)  // region 2
                {
                    tmp0 = a01 + b0;
                    tmp1 = a11 + b1;
                    if (tmp1 > tmp0) {
                        numer = tmp1 - tmp0;
                        denom = a00 - (2) * a01 + a11;
                        if (numer >= denom) {
                            s = 1;
                            t = 0;
                            sqrDistance = a00 + (2) * b0 + c;
                        } else {
                            s = numer / denom;
                            t = 1 - s;
                            sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                                t * (a01 * s + a11 * t + (2) * b1) + c;
                        }
                    } else {
                        s = 0;
                        if (tmp1 <= 0) {
                            t = 1;
                            sqrDistance = a11 + (2) * b1 + c;
                        } else if (b1 >= 0) {
                            t = 0;
                            sqrDistance = c;
                        } else {
                            t = -b1 / a11;
                            sqrDistance = b1 * t + c;
                        }
                    }
                } else if (t < 0)  // region 6
                  {
                    tmp0 = a01 + b1;
                    tmp1 = a00 + b0;
                    if (tmp1 > tmp0) {
                        numer = tmp1 - tmp0;
                        denom = a00 - (2) * a01 + a11;
                        if (numer >= denom) {
                            t = 1;
                            s = 0;
                            sqrDistance = a11 + (2) * b1 + c;
                        } else {
                            t = numer / denom;
                            s = 1 - t;
                            sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                                t * (a01 * s + a11 * t + (2) * b1) + c;
                        }
                    } else {
                        t = 0;
                        if (tmp1 <= 0) {
                            s = 1;
                            sqrDistance = a00 + (2) * b0 + c;
                        } else if (b0 >= 0) {
                            s = 0;
                            sqrDistance = c;
                        } else {
                            s = -b0 / a00;
                            sqrDistance = b0 * s + c;
                        }
                    }
                } else  // region 1
                  {
                    numer = a11 + b1 - a01 - b0;
                    if (numer <= 0) {
                        s = 0;
                        t = 1;
                        sqrDistance = a11 + (2) * b1 + c;
                    } else {
                        denom = a00 - (2) * a01 + a11;
                        if (numer >= denom) {
                            s = 1;
                            t = 0;
                            sqrDistance = a00 + (2) * b0 + c;
                        } else {
                            s = numer / denom;
                            t = 1 - s;
                            sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                                t * (a01 * s + a11 * t + (2) * b1) + c;
                        }
                    }
                }
            }
            closestPoint = triangle.V0 + s * edge0 + t * edge1;
            baryCoords = new Vector3d(1 - s - t, s, t);

            // Account for numerical round-off error.
            return Math.Max(sqrDistance, 0);
        }






    }
}
