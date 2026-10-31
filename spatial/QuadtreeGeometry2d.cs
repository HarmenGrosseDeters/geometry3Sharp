using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    // Geometry is evaluated in XY; snapshot Z coordinates are validated but only heights use them.
    internal static class QuadtreeGeometry2d
    {
        internal static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        internal static Vector2d XY(Vector3d p) { return new Vector2d(p.x, p.y); }
        internal static void Validate(Vector2d p)
        {
            if (!Finite(p.x) || !Finite(p.y)) throw new ArgumentException("Coordinates must be finite.");
        }
        internal static void Validate(Vector3d p)
        {
            Validate(XY(p));
            if (!Finite(p.z)) throw new ArgumentException("Coordinates must be finite.");
        }
        internal static void ValidateTolerance(double tolerance)
        {
            if (!Finite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        }
        internal static void ValidateQuery<T>(Vector2d p, double tolerance, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            Validate(p);
            ValidateTolerance(tolerance);
        }
        internal static AxisAlignedBox2d Bounds(Vector3d point)
        {
            Validate(point);
            return new AxisAlignedBox2d(XY(point));
        }
        internal static AxisAlignedBox2d Bounds(Segment3d segment)
        {
            Validate(segment.Center);
            Validate(segment.Direction);
            if (!Finite(segment.Extent) || segment.Extent < 0) throw new ArgumentException("Invalid segment extent.");
            Validate(segment.P0);
            Validate(segment.P1);
            return new AxisAlignedBox2d(XY(segment.P0), XY(segment.P1));
        }
        internal static AxisAlignedBox2d Bounds(Vector3d a, Vector3d b)
        {
            Validate(a);
            Validate(b);
            return new AxisAlignedBox2d(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }
        internal static AxisAlignedBox2d Bounds(Triangle3d triangle)
        {
            Validate(triangle.V0);
            Validate(triangle.V1);
            Validate(triangle.V2);
            return new AxisAlignedBox2d(
                Math.Min(triangle.V0.x, Math.Min(triangle.V1.x, triangle.V2.x)),
                Math.Min(triangle.V0.y, Math.Min(triangle.V1.y, triangle.V2.y)),
                Math.Max(triangle.V0.x, Math.Max(triangle.V1.x, triangle.V2.x)),
                Math.Max(triangle.V0.y, Math.Max(triangle.V1.y, triangle.V2.y)));
        }
        internal static bool Same(Segment3d a, Segment3d b)
        {
            return a.Center == b.Center && a.Direction == b.Direction && a.Extent == b.Extent;
        }
        internal static bool Same(Triangle3d a, Triangle3d b)
        {
            return a.V0 == b.V0 && a.V1 == b.V1 && a.V2 == b.V2;
        }
        internal static double Hypot(double x, double y)
        {
            double squared = x * x + y * y;
            if (squared >= 2.2250738585072014e-308 && Finite(squared)) return Math.Sqrt(squared);
            x = Math.Abs(x); y = Math.Abs(y);
            double m = Math.Max(x, y);
            if (m == 0 || double.IsInfinity(m)) return m;
            x /= m; y /= m;
            return m * Math.Sqrt(x * x + y * y);
        }
        internal static double PointDistance(Vector2d a, Vector2d b)
        {
            return Hypot(a.x - b.x, a.y - b.y);
        }
        // Inclusive tolerance tests. A rounded distance within its error of the tolerance decides nothing: exact squares do.
        // Besides its relative part, the error of a distance to a segment follows the size of the terms it cancels.
        static bool Undecided(double distance, double tolerance, double terms)
        {
            return Math.Abs(distance - tolerance) <= 1e-14 * tolerance + 4e-15 * terms;
        }
        static QuadtreeExactQuery2d.Fraction Squared(double tolerance)
        {
            BigInteger value = Integer(tolerance);
            return new QuadtreeExactQuery2d.Fraction(value * value, BigInteger.One);
        }
        internal static bool WithinPoint(Vector2d p, Vector2d a, double tolerance)
        {
            double distance = PointDistance(p, a);
            if (tolerance == 0 || !Undecided(distance, tolerance, 0)) return distance <= tolerance;
            return QuadtreeExactQuery2d.PointSquared(p, a).CompareTo(Squared(tolerance)) <= 0;
        }
        internal static bool WithinSegment(Vector2d p, Vector2d a, Vector2d b, double tolerance)
        {
            double distance = SegmentDistance(p, a, b);
            double terms = Math.Abs(b.x - a.x) + Math.Abs(b.y - a.y) + Math.Abs(p.x - a.x) + Math.Abs(p.y - a.y);
            if (!Undecided(distance, tolerance, terms)) return distance <= tolerance;
            return QuadtreeExactQuery2d.SegmentSquared(p, a, b).CompareTo(Squared(tolerance)) <= 0;
        }
        internal static bool WithinTriangle(Triangle3d triangle, Vector2d p, double tolerance)
        {
            Vector2d a = XY(triangle.V0), b = XY(triangle.V1), c = XY(triangle.V2);
            return Contains(triangle, p) || WithinSegment(p, a, b, tolerance) ||
                WithinSegment(p, b, c, tolerance) || WithinSegment(p, c, a, tolerance);
        }
        internal static bool NearRegion(AxisAlignedBox2d box, Vector2d p, double tolerance)
        {
            // Axis tests are conservative, avoid squared-distance overflow, and include rounded boundaries.
            return (p.x >= box.Min.x || box.Min.x - p.x <= tolerance) &&
                (p.x <= box.Max.x || p.x - box.Max.x <= tolerance) &&
                (p.y >= box.Min.y || box.Min.y - p.y <= tolerance) &&
                (p.y <= box.Max.y || p.y - box.Max.y <= tolerance);
        }
        static bool LostRange(double source, double result)
        {
            return source != 0 && (!Finite(result) || Math.Abs(result) < 2.2250738585072014e-308);
        }
        static bool LostProduct(double a, double b, double result)
        {
            return a != 0 && b != 0 && (!Finite(result) || Math.Abs(result) < 2.2250738585072014e-308);
        }
        static bool UncertainDifference(double left, double right)
        {
            return (left != 0 || right != 0) &&
                Math.Abs(left - right) <= 1e-12 * (Math.Abs(left) + Math.Abs(right));
        }
        // A barycentric numerator near zero, as for a point on a diagonal of a grid TIN. Its rounding error matters only
        // if the terms are large against the determinant: otherwise it moves the height by a few ulps of the largest z.
        static bool UncertainCoordinate(double left, double right, double determinant)
        {
            return UncertainDifference(left, right) && Math.Abs(left) + Math.Abs(right) > 4 * Math.Abs(determinant);
        }
        // ... unless the z difference it multiplies dwarfs the height, as next to a spike: then the error is the whole result.
        static bool Swamped(double left, double right, double zDifference, double height)
        {
            return UncertainDifference(left, right) && Math.Abs(zDifference) > 1e6 * Math.Abs(height);
        }
        internal static double SegmentDistance(Vector2d p, Vector2d a, Vector2d b)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            double qx = p.x - a.x, qy = p.y - a.y;
            double scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (scale == 0) return PointDistance(p, a);
            double qs = Math.Max(Math.Abs(qx), Math.Abs(qy));
            if (qs == 0) return 0;
            if (!Finite(scale) || !Finite(qs)) return ExactSegmentDistance(p, a, b);
            double ux = dx / scale, uy = dy / scale, vx = qx / qs, vy = qy / qs;
            if (LostRange(dx, ux) || LostRange(dy, uy) || LostRange(qx, vx) || LostRange(qy, vy))
                return ExactSegmentDistance(p, a, b);
            double dotX = vx * ux, dotY = vy * uy, normX = ux * ux, normY = uy * uy;
            double dot = dotX + dotY, norm = normX + normY;
            double projection = dot / norm, relativeScale = qs / scale;
            double t = projection * relativeScale;
            if (LostProduct(vx, ux, dotX) || LostProduct(vy, uy, dotY) ||
                LostProduct(ux, ux, normX) || LostProduct(uy, uy, normY) ||
                UncertainDifference(dotX, -dotY) || LostRange(dot, projection) ||
                LostRange(qs, relativeScale) || LostProduct(projection, relativeScale, t) || !Finite(t))
                return ExactSegmentDistance(p, a, b);
            if (t <= 0) return PointDistance(p, a);
            if (t >= 1) return PointDistance(p, b);
            double left = vx * uy, right = vy * ux, cross = left - right;
            if (LostProduct(vx, uy, left) || LostProduct(vy, ux, right) ||
                Math.Abs(cross) <= 1e-14 * (Math.Abs(left) + Math.Abs(right)) || cross == 0)
                return ExactSegmentDistance(p, a, b);
            return (Math.Abs(cross) / Math.Sqrt(norm)) * qs;
        }
        internal static bool OnSegment(Vector2d p, Vector2d a, Vector2d b)
        {
            return p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x) &&
                p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y) && ExactPredicates.Orient2d(a, b, p) == 0;
        }
        internal static bool Intersects(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int abC = ExactPredicates.Orient2d(a, b, c), abD = ExactPredicates.Orient2d(a, b, d);
            int cdA = ExactPredicates.Orient2d(c, d, a), cdB = ExactPredicates.Orient2d(c, d, b);
            if (abC == 0 && OnSegment(c, a, b) || abD == 0 && OnSegment(d, a, b) ||
                cdA == 0 && OnSegment(a, c, d) || cdB == 0 && OnSegment(b, c, d)) return true;
            return abC * abD < 0 && cdA * cdB < 0;
        }
        internal static bool Contains(Triangle3d triangle, Vector2d p)
        {
            Vector2d a = XY(triangle.V0), b = XY(triangle.V1), c = XY(triangle.V2);
            int orientation = ExactPredicates.Orient2d(a, b, c);
            if (orientation == 0) return false;
            return ExactPredicates.Orient2d(a, b, p) * orientation >= 0 &&
                ExactPredicates.Orient2d(b, c, p) * orientation >= 0 &&
                ExactPredicates.Orient2d(c, a, p) * orientation >= 0;
        }
        internal static double TriangleDistance(Triangle3d triangle, Vector2d p)
        {
            if (Contains(triangle, p)) return 0;
            Vector2d a = XY(triangle.V0), b = XY(triangle.V1), c = XY(triangle.V2);
            return Math.Min(SegmentDistance(p, a, b), Math.Min(SegmentDistance(p, b, c), SegmentDistance(p, c, a)));
        }
        internal static double Height(Triangle3d t, Vector2d p)
        {
            if (p == XY(t.V0)) return t.V0.z;
            if (p == XY(t.V1)) return t.V1.z;
            if (p == XY(t.V2)) return t.V2.z;
            if (t.V0.z == t.V1.z && t.V0.z == t.V2.z) return t.V0.z;
            double ax = t.V1.x - t.V0.x, ay = t.V1.y - t.V0.y;
            double bx = t.V2.x - t.V0.x, by = t.V2.y - t.V0.y;
            double px = p.x - t.V0.x, py = p.y - t.V0.y;
            double scale = Math.Max(Math.Max(Math.Abs(ax), Math.Abs(ay)), Math.Max(Math.Abs(bx), Math.Abs(by)));
            double sax = ax / scale, say = ay / scale, sbx = bx / scale, sby = by / scale,
                spx = px / scale, spy = py / scale;
            if (LostRange(ax, sax) || LostRange(ay, say) || LostRange(bx, sbx) ||
                LostRange(by, sby) || LostRange(px, spx) || LostRange(py, spy)) return ExactHeight(t, p);
            ax = sax; ay = say; bx = sbx; by = sby; px = spx; py = spy;
            double left = ax * by, right = ay * bx, determinant = left - right;
            double uLeft = px * by, uRight = py * bx, vLeft = ax * py, vRight = ay * px;
            if (!Finite(determinant) || determinant == 0 || LostProduct(ax, by, left) ||
                LostProduct(ay, bx, right) || UncertainDifference(left, right) ||
                LostProduct(px, by, uLeft) || LostProduct(py, bx, uRight) ||
                LostProduct(ax, py, vLeft) || LostProduct(ay, px, vRight) ||
                UncertainCoordinate(uLeft, uRight, determinant) || UncertainCoordinate(vLeft, vRight, determinant)) return ExactHeight(t, p);
            double u = (uLeft - uRight) / determinant;
            double v = (vLeft - vRight) / determinant;
            if (LostRange(uLeft - uRight, u) || LostRange(vLeft - vRight, v)) return ExactHeight(t, p);
            double directFirst = u * (t.V1.z - t.V0.z), directSecond = v * (t.V2.z - t.V0.z);
            double directHeight = t.V0.z + directFirst + directSecond;
            if (LostProduct(u, t.V1.z - t.V0.z, directFirst) ||
                LostProduct(v, t.V2.z - t.V0.z, directSecond) ||
                Swamped(uLeft, uRight, t.V1.z - t.V0.z, directHeight) ||
                Swamped(vLeft, vRight, t.V2.z - t.V0.z, directHeight)) return ExactHeight(t, p);
            if (Finite(directHeight) && Math.Abs(directHeight) >
                1e-12 * (Math.Abs(t.V0.z) + Math.Abs(directFirst) + Math.Abs(directSecond))) return directHeight;
            double zScale = Math.Max(Math.Abs(t.V0.z), Math.Max(Math.Abs(t.V1.z), Math.Abs(t.V2.z)));
            if (zScale == 0) return 0;
            double z0 = t.V0.z / zScale, z1 = t.V1.z / zScale, z2 = t.V2.z / zScale;
            if (LostRange(t.V0.z, z0) || LostRange(t.V1.z, z1) || LostRange(t.V2.z, z2)) return ExactHeight(t, p);
            double first = u * (z1 - z0), second = v * (z2 - z0);
            double sum = z0 + first + second, height = sum * zScale;
            if (LostProduct(u, z1 - z0, first) || LostProduct(v, z2 - z0, second) ||
                LostProduct(sum, zScale, height) || !Finite(height) || Math.Abs(sum) <= 1e-12 * (Math.Abs(z0) + Math.Abs(first) + Math.Abs(second)))
                return ExactHeight(t, p);
            return height;
        }

        // Rare numeric fallback: doubles are integers in units of 2^-1074. Integer determinants
        // prevent lost thin-triangle areas and intermediate overflow; returned constructions are rounded doubles.
        internal static BigInteger Integer(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 2047);
            long fraction = bits & 0xfffffffffffffL;
            BigInteger result = exponent == 0 ? new BigInteger(fraction) :
                new BigInteger(fraction | 0x10000000000000L) << (exponent - 1);
            return bits < 0 ? -result : result;
        }
        static BigInteger Cross(BigInteger ax, BigInteger ay, BigInteger bx, BigInteger by)
        {
            return ax * by - ay * bx;
        }
        static double ExactHeight(Triangle3d t, Vector2d p)
        {
            BigInteger x = Integer(t.V0.x), y = Integer(t.V0.y);
            BigInteger ax = Integer(t.V1.x) - x, ay = Integer(t.V1.y) - y;
            BigInteger bx = Integer(t.V2.x) - x, by = Integer(t.V2.y) - y;
            BigInteger px = Integer(p.x) - x, py = Integer(p.y) - y;
            BigInteger det = Cross(ax, ay, bx, by), u = Cross(px, py, bx, by), v = Cross(ax, ay, px, py);
            BigInteger z0 = Integer(t.V0.z), z1 = Integer(t.V1.z), z2 = Integer(t.V2.z);
            return Ratio(det * z0 + u * (z1 - z0) + v * (z2 - z0), det << 1074);
        }
        static double ExactSegmentDistance(Vector2d p, Vector2d a, Vector2d b)
        {
            BigInteger dx = Integer(b.x) - Integer(a.x), dy = Integer(b.y) - Integer(a.y);
            BigInteger px = Integer(p.x) - Integer(a.x), py = Integer(p.y) - Integer(a.y);
            BigInteger dot = px * dx + py * dy, length = dx * dx + dy * dy;
            if (dot <= 0) return PointDistance(p, a);
            if (dot >= length) return PointDistance(p, b);
            BigInteger cross = Cross(px, py, dx, dy);
            return SqrtRatio(cross * cross, length << 2148);
        }
        static int Bits(BigInteger positive)
        {
            byte[] bytes = positive.ToByteArray();
            int last = bytes.Length - 1;
            while (last > 0 && bytes[last] == 0) --last;
            int bits = last * 8;
            for (int b = bytes[last]; b != 0; b >>= 1) ++bits;
            return bits;
        }
        static int Exponent(BigInteger n, BigInteger d)
        {
            int exponent = Bits(n) - Bits(d);
            if (exponent >= 0 ? n < (d << exponent) : (n << -exponent) < d) --exponent;
            return exponent;
        }
        static double SqrtRatio(BigInteger n, BigInteger d)
        {
            if (n.IsZero) return 0;
            int e = Exponent(n, d);
            if ((e & 1) != 0) --e;
            int half = e / 2;
            if (half < -1022) {
                // Round in subnormal units using exact squared midpoints, avoiding double rounding.
                BigInteger scaled = n << 2148, quotient = scaled / d;
                BigInteger q = BigInteger.One << ((Bits(quotient) + 1) / 2);
                while (true) {
                    BigInteger next = (q + quotient / q) >> 1;
                    if (next >= q) break;
                    q = next;
                    if (q.IsZero) break;
                }
                BigInteger midpoint = (q << 1) + 1;
                int compare = (scaled << 2).CompareTo(d * midpoint * midpoint);
                if (compare > 0 || compare == 0 && !q.IsEven) ++q;
                return BitConverter.Int64BitsToDouble((long)q);
            }
            if (half > 1023) return double.PositiveInfinity;
            double normalized = e >= 0 ? Ratio(n, d << e) : Ratio(n << -e, d);
            return Math.Sqrt(normalized) * Math.Pow(2, half);
        }
        static double Ratio(BigInteger n, BigInteger d)
        {
            if (n.IsZero) return 0;
            bool negative = n.Sign != d.Sign;
            n = BigInteger.Abs(n); d = BigInteger.Abs(d);
            int e = Exponent(n, d);
            if (e > 1023) return negative ? double.NegativeInfinity : double.PositiveInfinity;
            int shift = e < -1022 ? 1074 : 52 - e;
            BigInteger remainder;
            BigInteger divisor = shift >= 0 ? d : d << -shift;
            BigInteger q = BigInteger.DivRem(shift >= 0 ? n << shift : n, divisor, out remainder);
            int compare = (remainder << 1).CompareTo(divisor);
            if (compare > 0 || compare == 0 && !q.IsEven) ++q;
            if (q >= (BigInteger.One << 53)) { q >>= 1; ++e; }
            long raw;
            if (e > 1023) raw = 0x7ff0000000000000L;
            else if (e < -1022) raw = (long)q;
            else raw = ((long)(e + 1023) << 52) | ((long)q & 0xfffffffffffffL);
            if (negative) raw |= long.MinValue;
            return BitConverter.Int64BitsToDouble(raw);
        }
    }
}
