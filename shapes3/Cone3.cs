// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace g3
{
    /// <summary>
    /// Single (infinite, or truncated at Height) cone with apex Apex, unit axis direction Axis pointing from
    /// the apex into the cone, and half opening angle Angle (radians, between the axis and the surface).
    /// </summary>
    public class Cone3d
    {
        public Vector3d Apex;
        public Vector3d Axis;
        public double Angle;
        /// <summary> extent along the axis from the apex (double.MaxValue = infinite) </summary>
        public double Height = double.MaxValue;

        public Cone3d(Vector3d apex, Vector3d axis, double angle, double height = double.MaxValue)
        {
            Apex = apex;
            Axis = axis.Normalized;
            Angle = angle;
            Height = height;
        }

        /// <summary> radius of the cross section at distance h along the axis from the apex </summary>
        public double RadiusAt(double h)
        {
            return h * Math.Tan(Angle);
        }

        /// <summary>
        /// signed distance of p to the (infinite) cone surface, negative inside. Points whose nearest
        /// point is the apex get the distance to the apex.
        /// </summary>
        public double SignedDistance(Vector3d p)
        {
            Vector3d w = p - Apex;
            double h = w.Dot(Axis);
            double rho = (w - h * Axis).Length;
            double sin = Math.Sin(Angle), cos = Math.Cos(Angle);
            // coordinates in the half plane through the axis: along the surface line and across it
            double along = h * cos + rho * sin;
            double across = rho * cos - h * sin;
            if (along < 0)
                return w.Length;        // behind the apex (always outside)
            return across;
        }
    }
}
