using System;

namespace g3
{
	/// <summary>
	/// Intersection of two lines (originally ported from WildMagic5). The decision is exact: Point (the lines cross),
	/// Line (the same line) or Empty (parallel, apart), with no tolerance, the same at every 2^k scale. A line is the
	/// line through <see cref="Line2d.Origin"/> and its second point (<see cref="Line2d.Origin"/> + 2^m
	/// <see cref="Line2d.Direction"/>, rounded: the 2D counterpart of IntrRay3Triangle3's ray). <see cref="DotThreshold"/>
	/// is only used by the static <see cref="Classify"/>. Non-finite input is an InvalidQuery. The crossing point and
	/// parameters are computed in floating point from the exactly decided crossing (infinite for lines that are
	/// parallel to within rounding).
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
			Vector2d p1 = IntrExact2d.SecondPoint(o1, line1.Direction), p2 = IntrExact2d.SecondPoint(o2, line2.Direction);
			if (p1 == o1 || p2 == o2) {
				// (the second point overflowed: no line)
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}
			Type = IntrExact2d.LineLine(o1, p1, o2, p2);
			Vector2d s = Vector2d.Zero;
			if (Type == IntersectionType.Point) {
				double den = line1.Direction.DotPerp(line2.Direction);
				Vector2d diff = o2 - o1;
				double n1 = diff.DotPerp(line2.Direction), n2 = diff.DotPerp(line1.Direction);
				if (den != 0) {
					s.x = n1 / den;
					s.y = n2 / den;
				} else {
					// the rounded cross product of the directions is 0, the exact one is not (the lines cross very far away):
					// infinite parameters, with the sign of the exact one
					int e = IntrExact2d.CrossSign(o1, IntrExact2d.SecondPoint(o1, line1.Direction), o2, IntrExact2d.SecondPoint(o2, line2.Direction));
					s.x = n1 == 0 ? 0 : (n1 * e > 0 ? double.PositiveInfinity : double.NegativeInfinity);
					s.y = n2 == 0 ? 0 : (n2 * e > 0 ? double.PositiveInfinity : double.NegativeInfinity);
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
