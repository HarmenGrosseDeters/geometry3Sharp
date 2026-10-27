using System;

namespace g3
{
    /// <summary>
    /// The size of the sampling grids (MeshSignedDistanceGrid, CachingMeshSDF, MeshWindingNumberGrid,
    /// MeshScalarSamplingGrid), with the checks that upstream lacked: a NaN, infinite, zero or negative cell size,
    /// non-finite bounds, or more samples than an array holds gave an OverflowException, an IndexOutOfRange, an
    /// out-of-memory or a 1-sample grid (docs/ROBUSTNESS_SWEEP.md R9).
    /// </summary>
    internal static class SamplingGridSize
    {
        /// <summary> the largest number of elements of a .NET array </summary>
        public const long MaxArrayLength = 0x7FFFFFC7;

        /// <summary>
        /// The number of samples along each axis that cover origin..max with spacing dx, or (-1,-1,-1) if the bounds or
        /// the spacing are not finite or positive, or the grid would have more than maxSamples samples (the array limit
        /// by default).
        /// </summary>
        public static Vector3i Dimensions(Vector3d origin, Vector3d max, double dx, long maxSamples = MaxArrayLength)
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
