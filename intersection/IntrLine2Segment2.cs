using System;
using System.Diagnostics;

namespace g3
{
    // ported from WildMagic5
    //
    //
    // double IntervalThreshold
    // 		The intersection testing uses the center-extent form for line segments.
    // 		If you start with endpoints (Vector2<Real>) and create Segment2<Real>
    // 		objects, the conversion to center-extent form can contain small
    // 		numerical round-off errors.  Testing for the intersection of two
    // 		segments that share an endpoint might lead to a failure due to the
    // 		round-off errors.  To allow for this, you may specify a small positive
    // 		threshold that slightly enlarges the intervals for the segments.  The
    // 		default value is zero.
    //
    // double DotThreshold
    // 		The computation for determining whether the linear components are
    // 		parallel might contain small floating-point round-off errors.  The
    // 		default threshold is MathUtil.ZeroTolerance.  If you set the value,
    // 		pass in a nonnegative number.
    //
    //
    // The intersection set.  Let q = Quantity.  The cases are
    //
    //   q = 0: The line/segment do not intersect.  Type is Empty
    //
    //   q = 1: The line/segment intersect in a single point. Type is Point
    //          Intersection point is Point0.
    //          
    //   q = int.MaxValue:  The line/segment are collinear.  Type is Segment.


    /// <summary>
    /// Intersection of a line and a segment (originally ported from WildMagic5). With the default
    /// <see cref="IntervalThreshold"/> of 0 the decision is exact: Point (the closed segment P0-P1 meets the line, exactly
    /// at a segment end if that end is on the line), Segment (the segment lies on the line, Quantity int.MaxValue) or
    /// Empty, with no tolerance, the same at every 2^k scale; the line is the line through Origin and its second point
    /// (Origin + 2^m Direction, rounded) and the segment ends are Segment2d.P0 and P1 (rounded from Center and Extent).
    /// <see cref="DotThreshold"/> is not used then. With an IntervalThreshold above 0 the old tolerance route runs
    /// (the segment is enlarged by the threshold, lines within DotThreshold of parallel count as parallel). Non-finite
    /// input is an InvalidQuery; a segment that collapsed to a point in the rounding of its ends takes the tolerance route.
    /// </summary>
    public class IntrLine2Segment2
    {
        Line2d line;
        public Line2d Line {
            get { return line; }
            set { line = value; Result = IntersectionResult.NotComputed; }
        }

        Segment2d segment;
        public Segment2d Segment {
            get { return segment; }
            set { segment = value; Result = IntersectionResult.NotComputed; }
        }

        double intervalThresh = 0;
        public double IntervalThreshold {
            get { return intervalThresh; }
            set { intervalThresh = Math.Max(value, 0); Result = IntersectionResult.NotComputed; }
        }

        double dotThresh = MathUtil.ZeroTolerance;
        public double DotThreshold {
            get { return dotThresh; }
            set { dotThresh = Math.Max(value, 0); Result = IntersectionResult.NotComputed; }
        }

        public int Quantity = 0;
        public IntersectionResult Result = IntersectionResult.NotComputed;
        public IntersectionType Type = IntersectionType.Empty;

        public bool IsSimpleIntersection {
            get { return Result == IntersectionResult.Intersects && Type == IntersectionType.Point; }
        }

        /// <summary> Point on line, only set if Quantity = 1    </summary>
        public Vector2d Point;

        /// <summary>Parameter along line, only set if Quanityt = 1    </summary>
        public double Parameter;

        public IntrLine2Segment2(Line2d line, Segment2d seg)
        {
            this.line = line; segment = seg;
        }

        public IntrLine2Segment2 Compute()
        {
            Find();
            return this;
        }


        public bool Find()
        {
            if (Result != IntersectionResult.NotComputed)
                return (Result == IntersectionResult.Intersects);

            // [RMS] if either segment direction is not a normalized vector, 
            //   results are garbage, so fail query
            if (line.Direction.IsNormalized == false || segment.Direction.IsNormalized == false) {
                Type = IntersectionType.Empty;
                Result = IntersectionResult.InvalidQuery;
                return false;
            }

            if (intervalThresh > 0) {
                find_tolerant();
            } else if (find_exact() == false) {
                Type = IntersectionType.Empty;
                Result = IntersectionResult.InvalidQuery;
                return false;
            }

            Result = (Type != IntersectionType.Empty) ?
                IntersectionResult.Intersects : IntersectionResult.NoIntersection;

            return (Result == IntersectionResult.Intersects);
        }

        // the old route with the tolerances (DotThreshold, IntervalThreshold)
        void find_tolerant()
        {
            Vector2d s = Vector2d.Zero;
            Type = IntrLine2Line2.Classify(line.Origin, line.Direction,
                                           segment.Center, segment.Direction,
                                           dotThresh, ref s);

            if (Type == IntersectionType.Point) {

                // Test whether the line-line intersection is on the segment.
                if (Math.Abs(s[1]) <= segment.Extent + intervalThresh) {
                    Quantity = 1;
                    Point = line.Origin + s[0] * line.Direction;
                    Parameter = s[0];
                } else {
                    Quantity = 0;
                    Type = IntersectionType.Empty;
                }

            } else if (Type == IntersectionType.Line) {
                Type = IntersectionType.Segment;
                Quantity = int.MaxValue;
            } else {
                Quantity = 0;
            }
        }


        // the exact decision (IntervalThreshold 0): the line through Origin and its second point against the closed segment
        // P0-P1. false for non-finite input or a segment that collapsed to a point.
        bool find_exact()
        {
            Vector2d o = line.Origin, a = segment.P0, b = segment.P1;
            if (IntrExact2d.IsFinite(o) == false || IntrExact2d.IsFinite(a) == false || IntrExact2d.IsFinite(b) == false)
                return false;
            Vector2d p = IntrExact2d.SecondPoint(o, line.Direction);
            if (p == o)
                return false;       // (the second point overflowed: no line)
            if (a == b) {
                // a segment that collapsed to a point in the rounding of its ends: the tolerance route
                find_tolerant();
                return true;
            }
            int sa = ExactPredicates.Orient2d(o, p, a), sb = ExactPredicates.Orient2d(o, p, b);
            if (sa == 0 && sb == 0) {
                Type = IntersectionType.Segment;        // (the segment lies on the line)
                Quantity = int.MaxValue;
            } else if (sa * sb > 0) {
                Type = IntersectionType.Empty;
                Quantity = 0;
            } else {
                Type = IntersectionType.Point;
                Quantity = 1;
                if (sa == 0) Point = a;
                else if (sb == 0) Point = b;
                else {
                    double da = line.Direction.DotPerp(a - o), db = line.Direction.DotPerp(b - o);
                    Point = a + IntrExact2d.CrossingFraction(da, db) * (b - a);
                }
                Parameter = (Point - o).Dot(line.Direction);
            }
            return true;
        }
    }
}
