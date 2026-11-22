using System;

namespace g3 
{
	/// <summary>
	/// Intersection of a segment and a triangle (originally ported from WildMagic5). The decision is exact: the closed
	/// segment between Segment2d.P0 and P1 against the closed triangle, Empty, Point (one common point) or Segment (a
	/// positive length), with exact orientation tests and no tolerance, the same at every 2^k scale. Corners and segment
	/// ends are returned exactly, the crossing of the segment with an edge in floating point (its Quantity and Type are
	/// exact). Non-finite input is an InvalidQuery. A degenerate triangle (collinear corners) or a segment that collapsed
	/// to a point takes the old tolerance route (1e-8).
	/// </summary>
	public class IntrSegment2Triangle2
	{
		Segment2d segment;
		public Segment2d Segment
		{
			get { return segment; }
			set { segment = value; Result = IntersectionResult.NotComputed; }
		}

		Triangle2d triangle;
		public Triangle2d Triangle
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


		public Vector2d Point0;
		public Vector2d Point1;
		public double Param0;
		public double Param1;


		public IntrSegment2Triangle2(Segment2d s, Triangle2d t)
		{
			segment = s; triangle = t;
		}


		public IntrSegment2Triangle2 Compute()
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
			if ( segment.Direction.IsNormalized == false )  {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}

			if (find_exact() == false) {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}

			Result = (Type != IntersectionType.Empty) ?
				IntersectionResult.Intersects : IntersectionResult.NoIntersection;
			return (Result == IntersectionResult.Intersects);
		}



		// the exact decision: the closed segment P0-P1 against the closed triangle. false for non-finite input. A
		// degenerate triangle (collinear corners) or a segment collapsed to a point takes the old tolerance route.
		bool find_exact()
		{
			Vector2d a = segment.P0, b = segment.P1;
			if (IntrExact2d.IsFinite(a) == false || IntrExact2d.IsFinite(b) == false || IntrExact2d.IsFinite(triangle.V0) == false
			    || IntrExact2d.IsFinite(triangle.V1) == false || IntrExact2d.IsFinite(triangle.V2) == false)
				return false;
			IntrExact2d.Cand entry = default(IntrExact2d.Cand), exit = default(IntrExact2d.Cand);
			int r = (a == b) ? -1 : IntrExact2d.Interval(a, b, ref triangle, out entry, out exit);
			if (r < 0) {
				find_tolerant();
				return true;
			}
			Quantity = 0;
			Type = IntersectionType.Empty;
			if (r == 0)
				return true;
			// clip the interval [entry, exit] of the line to the segment [a, b]
			bool lowIsA = IntrExact2d.Compare(entry, ref triangle, a, b, false) <= 0;      // (the line enters at or before a: the segment starts at a)
			bool highIsB = IntrExact2d.Compare(exit, ref triangle, a, b, true) >= 0;       // (and leaves at or after b)
			bool single = false;
			if (lowIsA && highIsB == false) {
				int c = IntrExact2d.Compare(exit, ref triangle, a, b, false);
				if (c < 0) return true;
				single = (c == 0);
			} else if (lowIsA == false && highIsB) {
				int c = IntrExact2d.Compare(entry, ref triangle, a, b, true);
				if (c > 0) return true;
				single = (c == 0);
			} else if (lowIsA == false && entry.Crossing == exit.Crossing && entry.Index == exit.Index) {
				single = true;      // the line touches one corner of the triangle, within the segment
			}
			Vector2d lo = lowIsA ? a : IntrExact2d.PointOf(entry, ref triangle, a, b);
			Vector2d hi = highIsB ? b : IntrExact2d.PointOf(exit, ref triangle, a, b);
			Point0 = lo;
			Param0 = (lo - segment.Center).Dot(segment.Direction);
			if (single) {
				Quantity = 1;
				Type = IntersectionType.Point;
				// the point: a (the exit is at a), b (the entry is at b) or the corner the line touches
				Point0 = lowIsA ? a : (highIsB ? b : lo);
				Param0 = (Point0 - segment.Center).Dot(segment.Direction);
			} else {
				Point1 = hi;
				Param1 = (hi - segment.Center).Dot(segment.Direction);
				Quantity = 2;
				Type = IntersectionType.Segment;
			}
			return true;
		}

		// the old route with the 1e-8 tolerance, for a degenerate triangle
		void find_tolerant()
		{
			Vector3d dist = Vector3d.Zero;
			Vector3i sign = Vector3i.Zero;
			int positive = 0, negative = 0, zero = 0;
			IntrLine2Triangle2.TriangleLineRelations(segment.Center, segment.Direction, triangle,
			                      ref dist, ref sign, ref positive, ref negative, ref zero);

			if (positive == 3 || negative == 3)
			{
				// No intersections.
				Quantity = 0;
				Type = IntersectionType.Empty;
			} else {
				Vector2d param = Vector2d.Zero;
				IntrLine2Triangle2.GetInterval(segment.Center, segment.Direction, triangle, dist, sign, ref param);

				Intersector1 intr = new Intersector1(param[0], param[1], -segment.Extent, +segment.Extent);
				intr.Find();

				Quantity = intr.NumIntersections;
				if (Quantity == 2) {
					// Segment intersection.
					Type = IntersectionType.Segment;
					Param0 = intr.GetIntersection(0);
					Point0 = segment.Center + Param0*segment.Direction;
					Param1 = intr.GetIntersection(1);
					Point1 = segment.Center + Param1*segment.Direction;
				} else if (Quantity == 1) {
					// Point intersection.
					Type = IntersectionType.Point;
					Param0 = intr.GetIntersection(0);
					Point0 = segment.Center + Param0*segment.Direction;
				} else {
					// No intersections.
					Type = IntersectionType.Empty;
				}
			}

		}

	}
}
