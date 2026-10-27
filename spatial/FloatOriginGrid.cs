using System;

namespace g3
{
    /// <summary>
    /// Helpers for the sampling grids whose public origin is a Vector3f (MeshSignedDistanceGrid, CachingMeshSDF,
    /// MeshWindingNumberGrid, MeshScalarSamplingGrid). The origin is rounded down to a float, so GridOrigin holds it
    /// exactly, and the sample positions are computed in double from it: GridOrigin + CellSize*(i,j,k) is then the
    /// position of a sample also far from the coordinate origin (at UTM coordinates a float position is off by up to
    /// 0.25, docs/ROBUSTNESS_SWEEP.md R3).
    /// </summary>
    internal static class FloatOriginGrid
    {
        /// <summary> the largest number of elements of a .NET array </summary>
        public const long MaxArrayLength = 0x7FFFFFC7;

        /// <summary> the largest float that is not greater than x (x itself if not finite) </summary>
        public static float FloorToFloat(double x)
        {
            float f = (float)x;
            if (float.IsNaN(f) || float.IsInfinity(f)) return f;
            while ((double)f > x) f = NextDown(f);
            return f;
        }

        public static Vector3f FloorToFloat(Vector3d v) => new Vector3f(FloorToFloat(v.x), FloorToFloat(v.y), FloorToFloat(v.z));

        static float NextDown(float f)
        {
            if (f == 0) return -float.Epsilon;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            bits += (f > 0) ? -1 : 1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        /// <summary>
        /// The number of samples along each axis that cover origin..max with spacing dx, or (-1,-1,-1) if the bounds or
        /// the spacing are not finite or positive, or the grid would have more than maxSamples samples (the array limit by default).
        /// </summary>
        public static Vector3i Dimensions(Vector3f origin, Vector3d max, double dx, long maxSamples = MaxArrayLength)
        {
            var invalid = new Vector3i(-1, -1, -1);
            if (!(dx > 0) || double.IsInfinity(dx)) return invalid;
            long[] n = new long[3];
            for (int k = 0; k < 3; ++k) {
                double cells = (max[k] - origin[k]) / dx;
                if (!(cells >= 0) || cells >= maxSamples) return invalid;      // (also NaN and infinity)
                n[k] = (long)cells + 1;
            }
            if ((double)n[0] * n[1] * n[2] > maxSamples) return invalid;
            return new Vector3i((int)n[0], (int)n[1], (int)n[2]);
        }
    }
}
