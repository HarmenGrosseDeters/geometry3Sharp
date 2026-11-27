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
        // (the quad pipeline fixtures use it too: QuadRemesherClosedOutputTests, QuadCreasePreservationTests, CrossFieldShapes.OpenPatch and
        // the strips of the quad tests; QuadFixtureInputTests pins their inputs)
        internal static double Sin(double x) { return CoreMath.Sin(x); }
        internal static double Cos(double x) { return CoreMath.Cos(x); }

        /// <summary> Matrix3d.AxisAngleD with correctly rounded sine and cosine (the same steps; #1660) </summary>
        internal static Matrix3d AxisAngleMatrixD(Vector3d axis, double angleDeg)
        {
            double angle = angleDeg * MathUtil.Deg2Rad;
            double cs = Cos(angle), sn = Sin(angle);
            double oneMinusCos = 1.0 - cs;
            double x2 = axis[0] * axis[0], y2 = axis[1] * axis[1], z2 = axis[2] * axis[2];
            double xym = axis[0] * axis[1] * oneMinusCos, xzm = axis[0] * axis[2] * oneMinusCos, yzm = axis[1] * axis[2] * oneMinusCos;
            double xSin = axis[0] * sn, ySin = axis[1] * sn, zSin = axis[2] * sn;
            return new Matrix3d(
                x2 * oneMinusCos + cs, xym - zSin, xzm + ySin,
                xym + zSin, y2 * oneMinusCos + cs, yzm - xSin,
                xzm - ySin, yzm + xSin, z2 * oneMinusCos + cs);
        }

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

        /// <summary> a hash of the vertex bits (in id order) and the triangles of a mesh: the identity of a test input. Normals, colours, uvs and groups
        /// are left out (the quad pipeline recomputes its normals from the positions), so two meshes that differ only there have the same hash </summary>
        internal static string Hash(DMesh3 m)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                bytes.AddRange(BitConverter.GetBytes(v));
                bytes.AddRange(BitConverter.GetBytes(p.x)); bytes.AddRange(BitConverter.GetBytes(p.y)); bytes.AddRange(BitConverter.GetBytes(p.z));
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                bytes.AddRange(BitConverter.GetBytes(tri.a)); bytes.AddRange(BitConverter.GetBytes(tri.b)); bytes.AddRange(BitConverter.GetBytes(tri.c));
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
        }

        /// <summary>
        /// A test input whose results are topology-sensitive (the gear fixture of the quad tests, which failed on Windows net10.0: where a 1-ulp change of one outline
        /// point decides whether the extraction drops a face) is pinned: a platform-dependent input then fails here, at the fixture, and not
        /// as a changed result deep in the operation
        /// </summary>
        internal static DMesh3 Pinned(string fixture, DMesh3 m, string expected)
        {
            string h = Hash(m);
            if (h != expected)
                throw new Xunit.Sdk.XunitException("the test input " + fixture + " is not its pinned mesh (hash " + h + ", pinned " + expected + "): it was built with a platform-dependent step; build it with PortableMath");
            return m;
        }
    }
}
