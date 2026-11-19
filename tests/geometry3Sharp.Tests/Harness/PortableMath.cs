using System;
using g3;
using g3.CoreMathPort;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Test inputs that are the same bits on every platform (#1151). The platform's Math.Sin / Math.Cos are not correctly
    /// rounded everywhere (on Windows x64 about 3 % of the arguments differ by an ulp from the correctly rounded value; glibc
    /// on the Linux CI is correctly rounded for practically all), so a test input built with them differs between the
    /// platforms and a pinned hash or a tight bound taken on Linux fails on Windows. These use the library's correctly
    /// rounded CoreMath, which gives the Linux values everywhere. The library's own generators are not changed (owner
    /// decision on #1151: the library does not promise identical results across operating systems).
    /// </summary>
    internal static class PortableMath
    {
        internal static double Sin(double x) { return CoreMath.Sin(x); }
        internal static double Cos(double x) { return CoreMath.Cos(x); }

        /// <summary> Quaterniond.AxisAngleD with correctly rounded sine and cosine (the same steps as SetAxisAngleD) </summary>
        internal static Quaterniond AxisAngleD(Vector3d axis, double angleDeg)
        {
            double halfAngle = 0.5 * (MathUtil.Deg2Rad * angleDeg);
            double sn = Sin(halfAngle);
            return new Quaterniond(sn * axis.x, sn * axis.y, sn * axis.z, Cos(halfAngle));
        }

        /// <summary>
        /// CappedCylinderGenerator's mesh with its ring positions computed with correctly rounded sine and cosine: the same
        /// topology and the same float angles as the generator (a full circle), every vertex off the axis at
        /// (radius cos a, y, radius sin a) of its ring angle a
        /// </summary>
        internal static DMesh3 CappedCylinder(float baseRadius, float topRadius, float height, int slices, bool noSharedVertices = false)
        {
            var gen = new CappedCylinderGenerator() { BaseRadius = baseRadius, TopRadius = topRadius, Height = height, Slices = slices, NoSharedVertices = noSharedVertices };
            DMesh3 m = gen.Generate().MakeDMesh();
            // the generator's angles (CylinderGenerators.cs): float arithmetic from StartAngleDeg = 0 to EndAngleDeg = 360
            float fTotalRange = (gen.EndAngleDeg - gen.StartAngleDeg) * MathUtil.Deg2Radf;
            float fStartRad = gen.StartAngleDeg * MathUtil.Deg2Radf;
            float fDelta = fTotalRange / slices;
            var angles = new float[slices + 1];
            for (int k = 0; k <= slices; ++k)
                angles[k] = fStartRad + (float)k * fDelta;
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                if (p.x == 0 && p.z == 0)
                    continue;       // (a cap centre)
                // the ring angle of this vertex: the generator's angle nearest to its direction
                double dir = Math.Atan2(p.z, p.x);
                int best = 0;
                double bestDist = double.MaxValue;
                for (int k = 0; k <= slices; ++k) {
                    double d = Math.Abs(Math.IEEERemainder(dir - angles[k], 2 * Math.PI));
                    if (d < bestDist) { bestDist = d; best = k; }
                }
                double radius = p.y == 0 ? baseRadius : topRadius;
                m.SetVertex(v, new Vector3d(radius * Cos(angles[best]), p.y, radius * Sin(angles[best])));
            }
            return m;
        }
    }
}
