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
	// The intersection set:  Let q = Quantity.  The cases are
	//
	//   q = 0: The segments do not intersect.  Type is Empty
	//
	//   q = 1: The segments intersect in a single point.  Type is Point
	//          Intersection point is Point0.
	//          
	//   q = 2: The segments are collinear and intersect in a segment.
	//			Type is Segment. Points are Point0 and Point1


	/// <summary>
	/// Intersection of two segments (originally ported from WildMagic5). With the default
	/// <see cref="IntervalThreshold"/> of 0 the decision is exact: the closed segments between Segment2d.P0 and P1 (the
	/// rounded Center -/+ Extent Direction) are decided with exact orientation tests, with no tolerance and the same at
	/// every 2^k scale. Type Point is a single common point (a crossing, a touching end, or collinear segments that
	/// share only an end), Segment a collinear overlap of positive length, Empty none. Points that are ends of a segment
	/// are returned exactly, a proper crossing in floating point. Two segments that share an end given as input can still
	/// miss each other when the rounding of P0/P1 separates them: use an IntervalThreshold above 0 for that, which
	/// selects the old tolerance route (the segments are enlarged by the threshold; DotThreshold is used there only).
	/// Non-finite input is an InvalidQuery. A segment that collapsed to a point in the rounding of P0/P1 takes the
	/// tolerance route (not scale invariant).
	/// </summary>
	public class IntrSegment2Segment2
    {
        Segment2d segment1;
        public Segment2d Segment1
        {
			get { return segment1; }
			set { segment1 = value; Result = IntersectionResult.NotComputed; }
        }

        Segment2d segment2;
        public Segment2d Segment2
        {
			get { return segment2; }
			set { segment2 = value; Result = IntersectionResult.NotComputed; }
        }

		double intervalThresh = 0;
		public double IntervalThreshold
		{
			get { return intervalThresh; }
			set { intervalThresh = Math.Max(value, 0); Result = IntersectionResult.NotComputed; }
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

		// these values are all on segment 1, unlike many other tests!!

		public Vector2d Point0;
		public Vector2d Point1;     // only set if Quantity == 2, ie segment overlap

		public double Parameter0;
		public double Parameter1;     // only set if Quantity == 2, ie segment overlap

		public IntrSegment2Segment2(Segment2d seg1, Segment2d seg2)
		{
			segment1 = seg1; segment2 = seg2;
		}

		public IntrSegment2Segment2 Compute()
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
			if ( segment1.Direction.IsNormalized == false || segment2.Direction.IsNormalized == false )  {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}


			if (intervalThresh > 0) {
				// with an IntervalThreshold the segments are enlarged: the tolerance route (DotThreshold and IntervalThreshold)
				find_tolerant();
			} else if (find_exact() == false) {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}

			Result = (Type != IntersectionType.Empty) ?
				IntersectionResult.Intersects : IntersectionResult.NoIntersection;

			// [RMS] for debugging...
			//sanity_check();

			return (Result == IntersectionResult.Intersects);
        }



		void find_tolerant()
		{
			Vector2d s = Vector2d.Zero;
			Type = IntrLine2Line2.Classify(segment1.Center, segment1.Direction, 
			                               segment2.Center, segment2.Direction,
			                               dotThresh, ref s);

			if (Type == IntersectionType.Point) {
				// Test whether the line-line intersection is on the segments.
				if (Math.Abs(s[0]) <= segment1.Extent + intervalThresh
				    &&  Math.Abs(s[1]) <= segment2.Extent + intervalThresh)
				{
					Quantity = 1;
					Point0 = segment1.Center + s[0]*segment1.Direction;
					Parameter0 = s[0];
				}
				else
				{
					Quantity = 0;
					Type = IntersectionType.Empty;
				}
			}
			else if (Type == IntersectionType.Line)
			{
				// Compute the location of segment1 endpoints relative to segment0.
				Vector2d diff = segment2.Center - segment1.Center;
				double t1 = segment1.Direction.Dot(diff);
				double tmin = t1 - segment2.Extent;
				double tmax = t1 + segment2.Extent;
				Intersector1 calc = new Intersector1(-segment1.Extent, segment1.Extent, tmin, tmax);
				calc.Find();
				Quantity = calc.NumIntersections;
				if (Quantity == 2) {
					Type = IntersectionType.Segment;
					Parameter0 = calc.GetIntersection(0);
					Point0 = segment1.Center +
						Parameter0*segment1.Direction;
					Parameter1 = calc.GetIntersection(1);					
					Point1 = segment1.Center +
						Parameter1*segment1.Direction;
				}
				else if (Quantity == 1)
				{
					Type = IntersectionType.Point;
					Parameter0 = calc.GetIntersection(0);
					Point0 = segment1.Center +
						Parameter0*segment1.Direction;
				} else {
					Type = IntersectionType.Empty;
				}
			} else {
				Quantity = 0;
			}

		}


		// the exact decision (IntervalThreshold 0): the closed segments P0-P1 of both. false for non-finite input.
		bool find_exact()
		{
			Vector2d a = segment1.P0, b = segment1.P1, c = segment2.P0, d = segment2.P1;
			if (IntrExact2d.IsFinite(a) == false || IntrExact2d.IsFinite(b) == false || IntrExact2d.IsFinite(c) == false || IntrExact2d.IsFinite(d) == false)
				return false;
			if (a == b || c == d) {
				// a segment that collapsed to a point in the rounding of its ends: the tolerance route
				find_tolerant();
				return true;
			}
			int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
			int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
			Quantity = 0;
			if (o1 == 0 && o2 == 0) {
				// collinear: the overlap of [a, b] and [c, d] along the line (comparisons of coordinates)
				Vector2d cLo = IntrExact2d.Before(a, b, c, d) ? c : d, cHi = IntrExact2d.Before(a, b, c, d) ? d : c;
				Vector2d lo = IntrExact2d.Before(a, b, a, cLo) ? cLo : a;
				Vector2d hi = IntrExact2d.Before(a, b, cHi, b) ? cHi : b;
				if (IntrExact2d.Before(a, b, hi, lo)) {
					Type = IntersectionType.Empty;
				} else if (lo == hi) {
					set_point(lo, ref Point0, ref Parameter0);
					Type = IntersectionType.Point;
					Quantity = 1;
				} else {
					set_point(lo, ref Point0, ref Parameter0);
					set_point(hi, ref Point1, ref Parameter1);
					Type = IntersectionType.Segment;
					Quantity = 2;
				}
			} else if (o1 * o2 <= 0 && o3 * o4 <= 0) {
				Type = IntersectionType.Point;
				Quantity = 1;
				if (o1 == 0) set_point(c, ref Point0, ref Parameter0);
				else if (o2 == 0) set_point(d, ref Point0, ref Parameter0);
				else if (o3 == 0) set_point(a, ref Point0, ref Parameter0);
				else if (o4 == 0) set_point(b, ref Point0, ref Parameter0);
				else {
					// a proper crossing: on segment 1, from the distances of its ends to the line of segment 2 (in floating
					// point, on the segment: the fraction is where the rounded distances change sign)
					double da = segment2.Direction.DotPerp(a - segment2.Center), db = segment2.Direction.DotPerp(b - segment2.Center);
					Point0 = a + IntrExact2d.CrossingFraction(da, db) * (b - a);
					Parameter0 = (Point0 - segment1.Center).Dot(segment1.Direction);
				}
			} else {
				Type = IntersectionType.Empty;
			}
			return true;
		}

		// an exact point of segment 1 (an end of either segment) with its parameter
		void set_point(Vector2d p, ref Vector2d point, ref double parameter)
		{
			point = p;
			parameter = (p - segment1.Center).Dot(segment1.Direction);
		}


		void sanity_check() {
			if ( Quantity == 0 ) {
				Util.gDevAssert(Type == IntersectionType.Empty);
				Util.gDevAssert(Result == IntersectionResult.NoIntersection);
			} else if (Quantity == 1) {
				Util.gDevAssert(Type == IntersectionType.Point);
				Util.gDevAssert( segment1.DistanceSquared(Point0) < MathUtil.ZeroTolerance );
				Util.gDevAssert( segment2.DistanceSquared(Point0) < MathUtil.ZeroTolerance);
			} else if ( Quantity == 2 ) {
				Util.gDevAssert(Type == IntersectionType.Segment);
				Util.gDevAssert( segment1.DistanceSquared(Point0) < MathUtil.ZeroTolerance );
				Util.gDevAssert( segment1.DistanceSquared(Point1) < MathUtil.ZeroTolerance );
				Util.gDevAssert( segment2.DistanceSquared(Point0) < MathUtil.ZeroTolerance);
				Util.gDevAssert( segment2.DistanceSquared(Point1) < MathUtil.ZeroTolerance);
			}
		}
    }
}
