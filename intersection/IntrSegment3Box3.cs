using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
	// ported from WildMagic5 
	/// <summary>
	/// The segment between its rounded ends P0 and P1 against the closed oriented box: exact decision without a tolerance (see <see cref="IntrExactBox3"/>). With solidBox false only the surface counts: a segment in the open interior does not intersect.
	/// </summary>
	public class IntrSegment3Box3
	{
		Segment3d segment;
		public Segment3d Segment
		{
			get { return segment; }
			set { segment = value; Result = IntersectionResult.NotComputed; }
		}

		Box3d box;
		public Box3d Box
		{
			get { return box; }
			set { box = value; Result = IntersectionResult.NotComputed; }
		}

		bool solid = false;
		public bool Solid {
			get { return solid; }
			set { solid = value; Result = IntersectionResult.NotComputed; }
		}

		public int Quantity = 0;
		public IntersectionResult Result = IntersectionResult.NotComputed;
		public IntersectionType Type = IntersectionType.Empty;

		public bool IsSimpleIntersection {
			get { return Result == IntersectionResult.Intersects && Type == IntersectionType.Point; }
		}

		public double SegmentParam0, SegmentParam1;
		public Vector3d Point0 = Vector3d.Zero;
		public Vector3d Point1 = Vector3d.Zero;

		// solidBox == false means fully contained segment does not intersect
		public IntrSegment3Box3(Segment3d s, Box3d b, bool solidBox)
		{
			segment = s; box = b; this.solid = solidBox;
		}

		public IntrSegment3Box3 Compute()
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

			// the closed segment between its rounded ends P0 and P1 (a Segment3d is a center, a direction and an extent)
			Vector3d p0 = segment.P0, p1 = segment.P1;
			IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
			IntrExactBox3.Hit hit;
			if (IntrExactBox3.Find(ref spec, ref p0, ref p0, ref p1, true, 0, true, 1, solid, out hit) == false) {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}
			SegmentParam0 = -segment.Extent;
			SegmentParam1 = segment.Extent;
			if (hit.Kind == 0) {
				Quantity = 0;
				Type = IntersectionType.Empty;
			} else {
				Point0 = hit.P0;
				SegmentParam0 = param_of(hit.P0, hit.T0 <= 0, hit.T0 >= 1);
				if (hit.Kind == 2) {
					Type = IntersectionType.Segment; Quantity = 2;
					Point1 = hit.P1;
					SegmentParam1 = param_of(hit.P1, hit.T1 <= 0, hit.T1 >= 1);
				} else {
					Type = IntersectionType.Point; Quantity = 1;
					SegmentParam1 = SegmentParam0;
				}
			}

			Result = (Type != IntersectionType.Empty) ?
				IntersectionResult.Intersects : IntersectionResult.NoIntersection;
			return (Result == IntersectionResult.Intersects);
		}




		// the parameter along the segment of a point of it (its ends exactly)
		double param_of(Vector3d p, bool atStart, bool atEnd)
		{
			if (atStart) return -segment.Extent;
			if (atEnd) return segment.Extent;
			double t = segment.Direction.Dot(p - segment.Center);
			return Math.Max(-segment.Extent, Math.Min(segment.Extent, t));
		}

		public bool Test ()
		{
			Vector3d p0 = segment.P0, p1 = segment.P1;
			IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
			IntrExactBox3.Hit hit;
			return IntrExactBox3.Find(ref spec, ref p0, ref p0, ref p1, true, 0, true, 1, true, out hit) && hit.Kind != 0;
		}




		/// <summary>
		/// The component origin + t direction, t in [t0, t1] (a bound of +-double.MaxValue means none), against the closed solid box: exact,
		/// without a tolerance (<see cref="IntrExactBox3"/>). On return t0 and t1 are the ends of the contact. False if there is none or an
		/// input is not finite. (The box is the intersection of the three slabs |dot(x - Center, Axis_i)| &lt;= Extent_i.)
		/// </summary>
		static public bool DoClipping (ref double t0, ref double t1,
		                 Vector3d origin, Vector3d direction,
		                 Box3d box, bool solid, ref int quantity, 
                         ref Vector3d point0, ref Vector3d point1,
		                 ref IntersectionType  intrType)
		{
			IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
			return IntrExactBox3.DoClip(ref t0, ref t1, ref origin, ref direction, ref spec, solid, ref quantity, ref point0, ref point1, ref intrType);
		}




		/// <summary> The slab clip of the WildMagic5 code; no longer used by this library's intersection classes (they decide exactly, see <see cref="IntrExactBox3"/>). </summary>
		static public bool Clip (double denom, double numer, ref double t0, ref double t1)
		{
			// Return value is 'true' if line segment intersects the current test
			// plane.  Otherwise 'false' is returned in which case the line segment
			// is entirely clipped.

			if (denom > (double)0)
			{
				if (numer > denom*t1)
				{
					return false;
				}
				if (numer > denom*t0)
				{
					t0 = numer/denom;
				}
				return true;
			}
			else if (denom < (double)0)
			{
				if (numer > denom*t0)
				{
					return false;
				}
				if (numer > denom*t1)
				{
					t1 = numer/denom;
				}
				return true;
			}
			else
			{
				return numer <= (double)0;
			}
		}


	}
}
