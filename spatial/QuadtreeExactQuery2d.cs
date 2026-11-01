using System;
using System.Numerics;

namespace g3
{
    // Invoked only for ambiguous clipping or distances outside the finite double range.
    internal interface IQuadtreeOverflowDistance2d
    {
        int CompareOverflow(int a, int b);
    }

    internal static class QuadtreeExactQuery2d
    {
        internal struct Fraction
        {
            internal BigInteger Numerator, Denominator;
            internal Fraction(BigInteger numerator, BigInteger denominator)
            {
                Numerator = numerator;
                Denominator = denominator;
            }
            internal int CompareTo(Fraction other)
            {
                return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
            }
        }

        static BigInteger Integer(double value) { return QuadtreeGeometry2d.Integer(value); }

        internal static Fraction PointSquared(Vector2d p, Vector2d a)
        {
            BigInteger x = Integer(p.x) - Integer(a.x), y = Integer(p.y) - Integer(a.y);
            return new Fraction(x * x + y * y, BigInteger.One);
        }

        internal static Fraction BoxSquared(Vector2d p, AxisAlignedBox2d box)
        {
            return PointSquared(p, new Vector2d(Math.Max(box.Min.x, Math.Min(box.Max.x, p.x)),
                Math.Max(box.Min.y, Math.Min(box.Max.y, p.y))));
        }

        internal static Fraction SegmentSquared(Vector2d p, Vector2d a, Vector2d b)
        {
            BigInteger dx = Integer(b.x) - Integer(a.x), dy = Integer(b.y) - Integer(a.y);
            BigInteger px = Integer(p.x) - Integer(a.x), py = Integer(p.y) - Integer(a.y);
            BigInteger length = dx * dx + dy * dy, dot = px * dx + py * dy;
            if (dot <= 0) return PointSquared(p, a);
            if (dot >= length) return PointSquared(p, b);
            BigInteger cross = px * dy - py * dx;
            return new Fraction(cross * cross, length);
        }

        internal static Fraction TriangleSquared(Vector2d p, Triangle3d triangle)
        {
            if (QuadtreeGeometry2d.Contains(triangle, p)) return new Fraction(BigInteger.Zero, BigInteger.One);
            Vector2d a = QuadtreeGeometry2d.XY(triangle.V0), b = QuadtreeGeometry2d.XY(triangle.V1),
                c = QuadtreeGeometry2d.XY(triangle.V2);
            Fraction first = SegmentSquared(p, a, b), second = SegmentSquared(p, b, c), third = SegmentSquared(p, c, a);
            if (second.CompareTo(first) < 0) first = second;
            return third.CompareTo(first) < 0 ? third : first;
        }

        internal static bool SegmentIntersects(Vector2d a, Vector2d b, AxisAlignedBox2d box)
        {
            var enter = new Fraction(BigInteger.Zero, BigInteger.One);
            var exit = new Fraction(BigInteger.One, BigInteger.One);
            return Clip(a.x, b.x, box.Min.x, box.Max.x, ref enter, ref exit) &&
                Clip(a.y, b.y, box.Min.y, box.Max.y, ref enter, ref exit);
        }

        static bool Clip(double a, double b, double min, double max, ref Fraction enter, ref Fraction exit)
        {
            if (a == b) return a >= min && a <= max;
            BigInteger origin = Integer(a), direction = Integer(b) - origin;
            BigInteger start = Integer(min) - origin, end = Integer(max) - origin;
            if (direction.Sign < 0) {
                direction = -direction;
                BigInteger swap = start; start = -end; end = -swap;
            }
            var lower = new Fraction(start, direction);
            var upper = new Fraction(end, direction);
            if (lower.CompareTo(enter) > 0) enter = lower;
            if (upper.CompareTo(exit) < 0) exit = upper;
            return enter.CompareTo(exit) <= 0;
        }
    }
}
