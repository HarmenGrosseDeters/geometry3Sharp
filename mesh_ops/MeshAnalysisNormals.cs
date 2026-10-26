// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// Triangle normals, vertex normals and triangle angles without absolute thresholds: a vector is zero
    /// only if its length is exactly zero (or not finite), so the results do not depend on the unit of the
    /// mesh. Code that compares lengths or areas with MathUtil.ZeroTolerance (1e-8) does: a vertex normal
    /// computed that way vanishes for a finely meshed small part in meters.
    /// </summary>
    public static class MeshAnalysisNormals
    {
        /// <summary> unit vector, or zero if v has no finite nonzero length </summary>
        public static Vector3d Unit(Vector3d v)
        {
            double len = v.Length;
            if (!(len > 0) || double.IsInfinity(len))
                return Vector3d.Zero;
            return v / len;
        }

        /// <summary> unit normal of the triangle (zero if degenerate), and its area </summary>
        public static Vector3d TriNormal(DMesh3 mesh, int tid, out double area)
        {
            Index3i t = mesh.GetTriangle(tid);
            Vector3d a = mesh.GetVertex(t.a);
            Vector3d cross = (mesh.GetVertex(t.b) - a).Cross(mesh.GetVertex(t.c) - a);
            area = 0.5 * cross.Length;
            return Unit(cross);
        }

        public static Vector3d TriNormal(DMesh3 mesh, int tid)
        {
            double area;
            return TriNormal(mesh, tid, out area);
        }

        /// <summary> area weighted vertex normal (the sum of the triangles' cross products), zero if none </summary>
        public static Vector3d VertexNormal(DMesh3 mesh, int vid)
        {
            Vector3d sum = Vector3d.Zero;
            foreach (int tid in mesh.VtxTrianglesItr(vid)) {
                Index3i t = mesh.GetTriangle(tid);
                Vector3d a = mesh.GetVertex(t.a);
                sum += (mesh.GetVertex(t.b) - a).Cross(mesh.GetVertex(t.c) - a);
            }
            return Unit(sum);
        }

        /// <summary> interior angle (radians) of the triangle at its corner j (0..2), from atan2 of sine and cosine </summary>
        public static double InternalAngle(DMesh3 mesh, int tid, int j)
        {
            Index3i t = mesh.GetTriangle(tid);
            Vector3d p = mesh.GetVertex(t[j]);
            Vector3d u = mesh.GetVertex(t[(j + 1) % 3]) - p, w = mesh.GetVertex(t[(j + 2) % 3]) - p;
            return Math.Atan2(u.Cross(w).Length, u.Dot(w));
        }
    }
}
