// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// The supported input range of the terrain operations that work on a height field mesh (docs/QA.md 2.5a): the used
    /// vertices are finite, their coordinates have a magnitude of at most 2^200, and the extent (the largest side of the
    /// bounds) is in [2^-8, 2^200]. Below, absolute constants of the operations (a height of 1 above the terrain, a
    /// zero tolerance of at least 1e-10) decide and the results are wrong without a failure; above, squares of
    /// lengths approach the double range. Measured with a bumpy 8 x 8 cell terrain scaled by 2^e: correct from 2^-20 to 2^250.
    /// </summary>
    static class TerrainInputRange
    {
        public const double MaxMagnitude = 1.6069380442589903e60;       // 2^200
        public const double MinExtent = 0.00390625;                     // 2^-8

        /// <summary> null if the mesh is in the range, otherwise why not </summary>
        public static string Check(DMesh3 mesh, string name)
        {
            Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    Vector3d p = mesh.GetVertex(t[k]);
                    for (int c = 0; c < 3; ++c) {
                        // (written so that NaN fails)
                        if (!(Math.Abs(p[c]) <= MaxMagnitude))
                            return name + " is outside the supported range: a coordinate is not finite or its magnitude is above 2^200";
                        lo[c] = Math.Min(lo[c], p[c]);
                        hi[c] = Math.Max(hi[c], p[c]);
                    }
                }
            }
            double extent = Math.Max(hi.x - lo.x, Math.Max(hi.y - lo.y, hi.z - lo.z));
            if (extent < MinExtent)
                return name + " is outside the supported range: its extent is below 2^-8";
            return null;
        }
    }
}
