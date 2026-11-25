using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
	// ported from WildMagic5 
	/// <summary>
	/// The ray against the closed solid oriented box: exact decision without a tolerance (see <see cref="IntrExactBox3"/>). <see cref="Intersects"/> takes an optional expansion of the extents.
	/// </summary>
	public class IntrRay3Box3
	{
		Ray3d ray;
		public Ray3d Ray
		{
			get { return ray; }
			set { ray = value; Result = IntersectionResult.NotComputed; }
		}

		Box3d box;
		public Box3d Box
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

		public double RayParam0, RayParam1;
		public Vector3d Point0 = Vector3d.Zero;
		public Vector3d Point1 = Vector3d.Zero;

		public IntrRay3Box3(Ray3d r, Box3d b)
		{
			ray = r; box = b;
		}

		public IntrRay3Box3 Compute()
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
			if ( ray.Direction.IsNormalized == false )  {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}

			if (IntrExactBox3.IsFinite(ref ray.Origin, ref ray.Direction) == false) {
				Type = IntersectionType.Empty;
				Result = IntersectionResult.InvalidQuery;
				return false;
			}
			RayParam0 = 0.0;
			RayParam1 = double.MaxValue;
			IntrLine3Box3.DoClipping(ref RayParam0, ref RayParam1, ray.Origin, ray.Direction, box,
			          true, ref Quantity, ref Point0, ref Point1, ref Type);

			Result = (Type != IntersectionType.Empty) ?
				IntersectionResult.Intersects : IntersectionResult.NoIntersection;
			return (Result == IntersectionResult.Intersects);
		}




		public bool Test ()
		{
            return Intersects(ref ray, ref box);
		}



        /// <summary>
        /// test if ray intersects box.
        /// expandExtents allows you to scale box for hit-testing purposes.
        /// </summary>
        public static bool Intersects(ref Ray3d ray, ref Box3d box, double expandExtents = 0)
        {
            // exact, closed, without a tolerance (IntrExactBox3); expandExtents is added to each extent (one rounding)
            return IntrExactBox3.RayTouchesBox3(ref ray.Origin, ref ray.Direction, ref box, expandExtents);
        }

    }
}
