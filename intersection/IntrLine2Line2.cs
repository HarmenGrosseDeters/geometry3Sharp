using System;

namespace g3
{
	/// <summary>
	/// Intersection of two lines (originally ported from WildMagic5). The decision is exact: Point (the lines cross),
	/// Line (the same line) or Empty (parallel, apart), with no tolerance, the same at every 2^k scale. A line is the
	/// line through <see cref="Line2d.Origin"/> along <see cref="Line2d.Direction"/> exactly as given (parallel is the exact sign of
	/// cross(d1, d2), the same line the exact sign of cross(o2 - o1, d1)). <see cref="DotThreshold"/> is only used by the static
	/// <see cref="Classify"/>. Non-finite input is an InvalidQuery, and so is a crossing that is not representable (lines that
	/// are parallel to within rounding, crossing beyond the range of a double). The crossing point and parameters are computed
	/// in floating point from the exactly decided crossing.
	/// </summary>
	public class IntrLine2Line2
    {
        Line2d line1;
        public Line2d Line1
        {
			get { return line1; }
			set { line1 = value; Result = IntersectionResult.NotComputed; }
        }

        Line2d line2;
        public Line2d Line2
        {
			get { return line2; }
			set { line2 = value; Result = IntersectionResult.NotComputed; }
        }

		double dotThresh = MathUtil.ZeroTolerance;
		public double DotThreshold
		{
			get { return dotThresh; }
			set { dotThresh = Math.Max(value, 0); Result = IntersectionResult.NotComputed; }
		}

		public int Quantity = 0;
		public IntersectionResult Result = IntersectionResult.NotComputed;
		public IntersectionType Type = IntersectionType.Empty;

        public bool IsSimpleIntersection {
            get { return Result == IntersectionResult.Intersects && Type == IntersectionType.Point; }
        }


		public Vector2d Point;
        public double Segment1Parameter;
		public double Segment2Parameter;


		public IntrLine2Line2(Line2d l1, Line2d l2)
		{
			line1 = l1; line2 = l2;
		}


		public IntrLine2Line2 Compute()
        {
            Find();
            return this;
        }


        public bool Find()
        {
            if (Result != IntersectionResult.NotComputed)
				return (Result == IntersectionResult.Intersects);

			// [RMS] if either line direction is not a normalized vector, 
			//   results are garbage, so fail query
			if ( line1.Direction.IsNormalized == false || line2.Direction.IsNormalized == false )  {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}

			Vector2d o1 = line1.Origin, o2 = line2.Origin;
			if (IntrExact2d.IsFinite(o1) == false || IntrExact2d.IsFinite(o2) == false) {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}
			Vector2d d1 = line1.Direction, d2 = line2.Direction;
			Type = IntrExact2d.LineLine(o1, d1, o2, d2);
			Vector2d s = Vector2d.Zero;
			if (Type == IntersectionType.Point) {
				// the parameters from the cross products (the denominator to about one rounding; the origins scaled by a power of
				// two first, so that their difference cannot overflow). A crossing that is not representable is an invalid query.
				double sc = IntrExact2d.ScaleFor(o1, o2, Vector2d.Zero);
				Vector2d diff = o2 * sc - o1 * sc;
				double den = IntrExact2d.AccurateCross(d1, d2) * sc;
				s.x = IntrExact2d.AccurateCross(diff, d2) / den;
				s.y = IntrExact2d.AccurateCross(diff, d1) / den;
				Vector2d x = o1 + s.x * d1;
				if (IntrExact2d.IsFinite(s) == false || IntrExact2d.IsFinite(x) == false) {
					Type = IntersectionType.Empty;
					Result = IntersectionResult.InvalidQuery;
					return false;
				}
			}

			if (Type == IntersectionType.Point) {
				Quantity = 1;
				Point = line1.Origin + s.x*line1.Direction;
				Segment1Parameter = s.x;
				Segment2Parameter = s.y;
			} else if (Type == IntersectionType.Line) {
				Quantity = int.MaxValue;
			} else {
				Quantity = 0;
			}

			Result = (Type != IntersectionType.Empty) ?
				IntersectionResult.Intersects : IntersectionResult.NoIntersection;
			return (Result == IntersectionResult.Intersects);
        }



		public static IntersectionType Classify( Vector2d P0, Vector2d D0,  Vector2d P1,  Vector2d D1,
		             double dotThreshold, ref Vector2d s)
		{
			// Ensure dotThreshold is nonnegative.
			dotThreshold = Math.Max(dotThreshold, (double)0);

			// The intersection of two lines is a solution to P0+s0*D0 = P1+s1*D1.
			// Rewrite this as s0*D0 - s1*D1 = P1 - P0 = Q.  If D0.Dot(Perp(D1)) = 0,
			// the lines are parallel.  Additionally, if Q.Dot(Perp(D1)) = 0, the
			// lines are the same.  If D0.Dot(Perp(D1)) is not zero, then
			//   s0 = Q.Dot(Perp(D1))/D0.Dot(Perp(D1))
			// produces the point of intersection.  Also,
			//   s1 = Q.Dot(Perp(D0))/D0.Dot(Perp(D1))

			Vector2d diff = P1 - P0;
			double D0DotPerpD1 = D0.DotPerp(D1);
			if ( Math.Abs(D0DotPerpD1) > dotThreshold) {
			// Lines intersect in a single point.
				double invD0DotPerpD1 = ((double)1)/D0DotPerpD1;
				double diffDotPerpD0 = diff.DotPerp(D0);
				double diffDotPerpD1 = diff.DotPerp(D1);
				s[0] = diffDotPerpD1*invD0DotPerpD1;
				s[1] = diffDotPerpD0*invD0DotPerpD1;
				return IntersectionType.Point;
			}

			// Lines are parallel.
			diff.Normalize();
			double diffNDotPerpD1 = diff.DotPerp(D1);
			if (Math.Abs(diffNDotPerpD1) <= dotThreshold) {
				// Lines are colinear.
				return IntersectionType.Line;
			}

			// Lines are parallel, but distinct.
			return IntersectionType.Empty;
		}

    }
}
