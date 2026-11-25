using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
	// adapted from IntrLine3Box3
	/// <summary>
	/// The infinite line against the closed axis-aligned box [Min, Max]: exact decision without a tolerance (see <see cref="IntrExactBox3"/>).
	/// </summary>
	public class IntrLine3AxisAlignedBox3
	{
		Line3d line;
		public Line3d Line
		{
			get { return line; }
			set { line = value; Result = IntersectionResult.NotComputed; }
		}

		AxisAlignedBox3d box;
		public AxisAlignedBox3d Box
		{
			get { return box; }
			set { box = value; Result = IntersectionResult.NotComputed; }
		}

		public int Quantity = 0;
		public IntersectionResult Result = IntersectionResult.NotComputed;
		public IntersectionType Type = IntersectionType.Empty;

		public bool IsSimpleIntersection {
			get { return Result == IntersectionResult.Intersects && Type == IntersectionType.Point; }
		}

		public double LineParam0, LineParam1;
		public Vector3d Point0 = Vector3d.Zero;
		public Vector3d Point1 = Vector3d.Zero;

		public IntrLine3AxisAlignedBox3(Line3d l, AxisAlignedBox3d b)
		{
			line = l; box = b;
		}

		public IntrLine3AxisAlignedBox3 Compute()
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
			if ( line.Direction.IsNormalized == false )  {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}

			if (IntrExactBox3.IsFinite(ref line.Origin, ref line.Direction) == false) {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}
			LineParam0 = -double.MaxValue;
			LineParam1 = double.MaxValue;
			DoClipping(ref LineParam0, ref LineParam1, ref line.Origin, ref line.Direction, ref box,
			          true, ref Quantity, ref Point0, ref Point1, ref Type);

			Result = (Type != IntersectionType.Empty) ?
				IntersectionResult.Intersects : IntersectionResult.NoIntersection;
			return (Result == IntersectionResult.Intersects);
		}



        // [RMS TODO: lots of useless dot products below!! left over from obox conversion]
		public bool Test ()
		{
			IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
			return IntrExactBox3.Touches(ref spec, ref line.Origin, ref line.Direction, false, 0, false, 0);
		}




		/// <summary>
		/// The component origin + t direction, t in [t0, t1] (a bound of +-double.MaxValue means none), against the closed box: exact, without
		/// a tolerance (<see cref="IntrExactBox3"/>). On return t0 and t1 are the ends of the contact. False if there is none or an input is not finite.
		/// </summary>
		static public bool DoClipping (ref double t0, ref double t1,
		                 ref Vector3d origin, ref Vector3d direction,
		                 ref AxisAlignedBox3d box, bool solid, ref int quantity, 
                         ref Vector3d point0, ref Vector3d point1,
		                 ref IntersectionType  intrType)
		{
			IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
			return IntrExactBox3.DoClip(ref t0, ref t1, ref origin, ref direction, ref spec, solid, ref quantity, ref point0, ref point1, ref intrType);
		}




		static public bool Clip (double denom, double numer, ref double t0, ref double t1)
		{
			// Return value is 'true' if line segment intersects the current test
			// plane.  Otherwise 'false' is returned in which case the line segment
			// is entirely clipped.

			if (denom > (double)0)
			{
                if (numer - denom*t1 > MathUtil.ZeroTolerance)
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
                if (numer - denom*t0 > MathUtil.ZeroTolerance)
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
