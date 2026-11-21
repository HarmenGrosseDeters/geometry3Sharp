using System;

namespace g3
{
    /// <summary>
    /// Double-precision 3D plane defined by <c>Dot(Normal, P) - Constant = 0</c> (ported from WildMagic5 Wm5Plane3).
    /// </summary>
    /// <remarks>
    /// Note the sign convention: <see cref="Constant"/> equals <c>+Dot(Normal, P0)</c> (opposite sign to
    /// <c>ax + by + cz + d = 0</c>). <see cref="DistanceTo"/> returns signed Euclidean distance only when
    /// <see cref="Normal"/> has unit length.
    /// </remarks>
    public struct Plane3d
    {
        public Vector3d Normal;
        public double Constant;

        public Plane3d(Vector3d normal, double constant)
        {
            Normal = normal;
            Constant = constant;
        }

         // N is specified, c = Dot(N,P) where P is a point on the plane.
        public Plane3d(Vector3d normal, Vector3d point)
        {
            Normal = normal;
            Constant = Normal.Dot(point);
        }

        // N = Cross(P1-P0,P2-P0)/Length(Cross(P1-P0,P2-P0)), c = Dot(N,P0) where
        // P0, P1, P2 are points on the plane.
        public Plane3d(Vector3d p0, Vector3d p1, Vector3d p2)
        {
            Vector3d edge1 = p1 - p0;
            Vector3d edge2 = p2 - p0;
            Normal = edge1.UnitCross(edge2);
            Constant = Normal.Dot(p0);
        }


        /// <summary>
        /// Signed distance <c>Dot(Normal, p) - Constant</c> (Euclidean when <see cref="Normal"/> is unit-length):
        /// positive on the normal side, negative on the opposite side, zero on the plane.
        /// </summary>
        public double DistanceTo(Vector3d p)
        {
            return Normal.Dot(p) - Constant;
        }

        /// <summary>
        /// Returns <c>+1</c> if <paramref name="p"/> is on the positive (<see cref="Normal"/>) side,
        /// <c>-1</c> on the negative side, or <c>0</c> on the plane.
        /// </summary>
        public int WhichSide (Vector3d p)
        {
            double distance = DistanceTo(p);
            if (distance < 0)
                return -1;
            else if (distance > 0)
                return +1;
            else
                return 0;
        }

    }




    /// <summary>
    /// Single-precision 3D plane defined by <c>Dot(Normal, P) - Constant = 0</c> (ported from WildMagic5 Wm5Plane3).
    /// </summary>
    /// <remarks>
    /// Note the sign convention: <see cref="Constant"/> equals <c>+Dot(Normal, P0)</c> (opposite sign to
    /// <c>ax + by + cz + d = 0</c>). <see cref="DistanceTo"/> returns signed Euclidean distance only when
    /// <see cref="Normal"/> has unit length.
    /// </remarks>
    public struct Plane3f
    {
        public Vector3f Normal;
        public float Constant;

        public Plane3f(Vector3f normal, float constant)
        {
            Normal = normal;
            Constant = constant;
        }

         // N is specified, c = Dot(N,P) where P is a point on the plane.
        public Plane3f(Vector3f normal, Vector3f point)
        {
            Normal = normal;
            Constant = Normal.Dot(point);
        }

        // N = Cross(P1-P0,P2-P0)/Length(Cross(P1-P0,P2-P0)), c = Dot(N,P0) where
        // P0, P1, P2 are points on the plane.
        public Plane3f(Vector3f p0, Vector3f p1, Vector3f p2)
        {
            Vector3f edge1 = p1 - p0;
            Vector3f edge2 = p2 - p0;
            Normal = edge1.UnitCross(edge2);
            Constant = Normal.Dot(p0);
        }


        /// <summary>
        /// Signed distance <c>Dot(Normal, p) - Constant</c> (Euclidean when <see cref="Normal"/> is unit-length):
        /// positive on the normal side, negative on the opposite side, zero on the plane.
        /// </summary>
        public float DistanceTo(Vector3f p)
        {
            return Normal.Dot(p) - Constant;
        }

        /// <summary>
        /// Returns <c>+1</c> if <paramref name="p"/> is on the positive (<see cref="Normal"/>) side,
        /// <c>-1</c> on the negative side, or <c>0</c> on the plane.
        /// </summary>
        public int WhichSide (Vector3f p)
        {
            float distance = DistanceTo(p);
            if (distance < 0)
                return -1;
            else if (distance > 0)
                return +1;
            else
                return 0;
        }

    }


}
