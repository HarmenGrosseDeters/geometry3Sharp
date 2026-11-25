using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
	// adapted from IntrRay3Box3
	/// <summary>
	/// The ray against the closed axis-aligned box [Min, Max]: exact decision without a tolerance (see <see cref="IntrExactBox3"/>); the entry parameter of <see cref="FindRayIntersectT"/> is within a few ulp of the exact one.
	/// </summary>
	public class IntrRay3AxisAlignedBox3
	{
		Ray3d ray;
		public Ray3d Ray
		{
			get { return ray; }
			set { ray = value; Result = IntersectionResult.NotComputed; }
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

		public double RayParam0, RayParam1;
		public Vector3d Point0 = Vector3d.Zero;
		public Vector3d Point1 = Vector3d.Zero;

		public IntrRay3AxisAlignedBox3(Ray3d r, AxisAlignedBox3d b)
		{
			ray = r; box = b;
		}

		public IntrRay3AxisAlignedBox3 Compute()
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
			IntrLine3AxisAlignedBox3.DoClipping(ref RayParam0, ref RayParam1, ref ray.Origin, ref ray.Direction, ref box,
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
        public static bool Intersects(ref Ray3d ray, ref AxisAlignedBox3d box, double expandExtents = 0)
        {
            // exact, closed, without a tolerance (IntrExactBox3); expandExtents moves each face outward by that amount (one rounding of the face)
            IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, expandExtents);
            return IntrExactBox3.Touches(ref spec, ref ray.Origin, ref ray.Direction, true, 0, false, 0);
        }

        /// <summary>
        /// Find intersection of ray with AABB, without having to construct any new classes.
        /// Returns ray T-value of first intersection (or double.MaxVlaue on miss)
        /// </summary>
        public static bool FindRayIntersectT(ref Ray3d ray, ref AxisAlignedBox3d box, out double RayParam)
        {
            // exact decision, closed box (IntrExactBox3); the entry parameter is within a few ulp of the exact one
            IntrExactBox3.BoxSpec spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
            if (IntrExactBox3.RayEntry(ref spec, ref ray.Origin, ref ray.Direction, out RayParam))
                return true;
            RayParam = double.MaxValue;
            return false;
        }
    }
}
