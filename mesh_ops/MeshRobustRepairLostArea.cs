using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The lost-area report of <see cref="MeshRobustRepair"/> (#1479, MeasureLostArea): how much of the input's surface is not represented in
    /// the result. Every triangle of the input is sampled at the centroids of the k*k equal sub-triangles of its barycentric subdivision (so
    /// each sample stands for the same share of the triangle's area; k from the triangle count, SamplesTarget, and larger for a triangle whose
    /// area is above the mean: a rule that follows the area, so that one huge triangle among many small ones is not judged by 16 samples), the
    /// distance of each sample to the result is the nearest-point query of MeshBatchQueries (the batch runs the single DMeshAABBTree3 queries, so its
    /// results do not depend on the thread count), and a sample farther than the tolerance from the result is lost. LostArea is the sum of the
    /// lost samples' shares of their triangles' areas, taken serially in triangle id order (the sums are the same for a parallel and a serial batch,
    /// and for a power of two scaled input, bit for bit: every operation scales exactly). A report, no judgment: it does not know whether the input
    /// surface should be in the result (an interior wall of a solid is lost by design). Triangles without area are not sampled and never listed.
    /// </summary>
    internal static class MeshRobustRepairLostArea
    {
        /// <summary> about this many samples in all for triangles of the mean area (k*k per triangle, k in 1..MaxSubdivisions): the cost is that many nearest-triangle queries </summary>
        internal const int SamplesTarget = 1 << 19;
        internal const int MaxSubdivisions = 4;
        /// <summary>
        /// a triangle of more than the mean area gets k scaled by the square root of its area over the mean (so about the same area per sample),
        /// at most this: 1024 samples. The samples spent this way add up to about the base budget again (the areas sum to n times the mean). Beyond the
        /// cap a triangle of more than 1024 / k^2 times the mean area has samples of a larger share each
        /// </summary>
        internal const int MaxLargeSubdivisions = 32;
        // the points of a batch: bounded memory for any input (a batch ends at the first triangle that reaches this many samples)
        // (small enough that the cancel check between batches, about 25 ms of queries each, is prompt: the batch's own token is not used, the caller's cancel is a Func)
        const int ChunkSamples = 1 << 15;

        internal sealed class Report
        {
            /// <summary> area of the input (of the triangles that were sampled) </summary>
            public double InputArea;
            /// <summary> estimated area of the input farther than the tolerance from the result </summary>
            public double LostArea;
            /// <summary> input triangles with area and more than half of their samples lost (ids of the input) </summary>
            public List<int> LostTriangles = new List<int>();
            /// <summary> the largest distance of a sample to the result (also of a sample that is not lost); PositiveInfinity for an empty result </summary>
            public double MaxDistance;
            /// <summary> the subdivisions of a triangle of the mean area (SubdivisionsFor) </summary>
            public int Subdivisions;
            /// <summary> samples taken in all </summary>
            public long Samples;
            /// <summary> the most points one batch held (tests: the buffers follow the input, not a fixed size) </summary>
            public int PeakBatchSamples;
        }

        internal static int SubdivisionsFor(int triangleCount)
        {
            if (triangleCount <= 0)
                return 1;
            int k = (int)Math.Floor(Math.Sqrt((double)SamplesTarget / triangleCount));
            return Math.Max(1, Math.Min(MaxSubdivisions, k));
        }

        /// <summary> the subdivisions of a triangle of the given area when the base is k and the mean area is mean (0: no samples, a triangle without area) </summary>
        internal static int SubdivisionsOf(double area, double mean, int k)
        {
            if (!(area > 0))
                return 0;
            if (!(area > mean) || !(mean > 0))
                return k;
            double scaled = Math.Ceiling(k * Math.Sqrt(area / mean));
            return (int)Math.Min(MaxLargeSubdivisions, Math.Max(k, scaled));
        }

        /// <summary>
        /// Measure the input against the result. tolerance is an absolute length (a sample at a distance of more than it is lost). Returns null if
        /// cancelled. parallel: the batch queries on all cores (the report is the same either way).
        /// </summary>
        public static Report Measure(DMesh3 input, DMesh3 result, double tolerance, bool parallel = true, Func<bool> cancel = null)
        {
            if (double.IsNaN(tolerance) || tolerance < 0)
                throw new ArgumentOutOfRangeException("tolerance", "the tolerance of the lost-area report must be a number of at least 0");
            var report = new Report();
            int n = input.TriangleCount;
            int kBase = report.Subdivisions = SubdivisionsFor(n);
            var ids = new int[n];
            var areas = new double[n];
            double total = 0;
            int at = 0;
            foreach (int t in input.TriangleIndices()) {
                ids[at] = t;
                double a = input.GetTriArea(t);
                areas[at++] = a;
                if (a > 0)
                    total += a;
            }
            report.InputArea = total;
            // (not for an input whose areas overflow, coordinates of about 1e150 and more: Apply() works in a frame of extent 1 to 2 and never gets there)
            if (!(total > 0) || double.IsInfinity(total))
                return report;
            double mean = total / n;
            var ks = new int[n];
            for (int i = 0; i < n; ++i)
                ks[i] = SubdivisionsOf(areas[i], mean, kBase);
            DMeshAABBTree3 tree = result.TriangleCount > 0 ? new DMeshAABBTree3(result, true) : null;
            var options = new BatchOptions() { Parallel = parallel };
            double lost = 0, maxDist = 0;
            int first = 0;
            while (first < n) {
                // the triangles of one batch: up to ChunkSamples samples (at least one triangle)
                int count = 0, samples = 0;
                while (first + count < n && (count == 0 || samples < ChunkSamples)) {
                    samples += ks[first + count] * ks[first + count];
                    count++;
                }
                if (cancel != null && cancel())
                    return null;
                var points = new Vector3d[samples];
                int p = 0;
                for (int i = first; i < first + count; ++i) {
                    if (ks[i] == 0)
                        continue;
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    input.GetTriVertices(ids[i], ref a, ref b, ref c);
                    Samples(a, b, c, ks[i], points, p);
                    p += ks[i] * ks[i];
                }
                NearestPointResult[] found = null;
                if (tree != null && samples > 0) {
                    found = new NearestPointResult[samples];
                    MeshBatchQueries.NearestPointBatch(tree, points, found, null, options);
                }
                report.Samples += samples;
                report.PeakBatchSamples = Math.Max(report.PeakBatchSamples, samples);
                p = 0;
                for (int i = first; i < first + count; ++i) {
                    int perTri = ks[i] * ks[i];
                    if (perTri == 0)
                        continue;
                    int lostSamples = 0;
                    for (int j = 0; j < perTri; ++j) {
                        // The distance is that of the sample to the nearest point the query returns, not the query's own distance: that one is the square
                        // root of a quadratic form of the triangle's corners and the point whose terms cancel for a point on the triangle, so it is wrong by
                        // about sqrt(epsilon) times the triangle's size (4.6e-9 on a unit sphere against itself, at any distance from the origin), where the
                        // difference of two points near each other is exact to a few units in the last place of the coordinates
                        double d = double.PositiveInfinity;
                        if (found != null && found[p + j].Triangle != DMesh3.InvalidID)
                            d = (points[p + j] - found[p + j].Point).Length;
                        if (d > maxDist)
                            maxDist = d;
                        if (d > tolerance)
                            lostSamples++;
                    }
                    p += perTri;
                    if (lostSamples > 0)
                        lost += areas[i] * ((double)lostSamples / perTri);
                    if (2 * lostSamples > perTri)
                        report.LostTriangles.Add(ids[i]);
                }
                first += count;
            }
            report.LostArea = lost;
            report.MaxDistance = maxDist;
            return report;
        }

        /// <summary> the samples of (a, b, c) for k subdivisions: the centroids of the k*k equal sub-triangles </summary>
        internal static Vector3d[] SamplePoints(Vector3d a, Vector3d b, Vector3d c, int k)
        {
            var into = new Vector3d[k * k];
            Samples(a, b, c, k, into, 0);
            return into;
        }

        // the centroids of the k*k equal sub-triangles of (a, b, c): k(k+1)/2 pointing like the triangle, k(k-1)/2 pointing the other way
        static void Samples(Vector3d a, Vector3d b, Vector3d c, int k, Vector3d[] into, int at)
        {
            Vector3d e1 = b - a, e2 = c - a;
            for (int i = 0; i < k; ++i)
                for (int j = 0; i + j < k; ++j) {
                    into[at++] = a + (e1 * (i + 1.0 / 3) + e2 * (j + 1.0 / 3)) / k;
                    if (i + j < k - 1)
                        into[at++] = a + (e1 * (i + 2.0 / 3) + e2 * (j + 2.0 / 3)) / k;
                }
        }

        // The unit in the last place of x >= 0 (2^(e - 1075) for the exponent field e), the same value as the rounding's own
        // helper (MeshRoundingRepair.ulp, which is private in a file whose mutation shards are split by character
        // position, so it is repeated here instead of being opened up).
        internal static double Ulp(double x)
        {
            if (!(x < double.PositiveInfinity))
                return double.NaN;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            return e > 52 ? BitConverter.Int64BitsToDouble((long)(e - 52) << 52)
                : e > 0 ? BitConverter.Int64BitsToDouble(1L << (e - 1)) : double.Epsilon;
        }
    }
}
