using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The lost-area report of <see cref="MeshRobustRepair"/> (#1479, MeasureLostArea): how much of the input's surface is not represented in
    /// the result. Every triangle of the input is sampled at the centroids of the k*k equal sub-triangles of its barycentric subdivision (so
    /// each sample stands for the same share of the triangle's area, k from the triangle count: SamplesTarget), the distance of each sample to
    /// the result is the nearest-point query of MeshBatchQueries (the batch runs the single DMeshAABBTree3 queries, so its results do not
    /// depend on the thread count), and a sample farther than the tolerance from the result is lost. LostArea is the sum of the lost samples'
    /// shares of their triangles' areas, taken serially in triangle id order (the sums are the same for a parallel and a serial batch, and for
    /// a power of two scaled input, bit for bit: every operation scales exactly). A report, no judgment: it does not know whether the input
    /// surface should be in the result (an interior wall of a solid is lost by design).
    /// </summary>
    internal static class MeshRobustRepairLostArea
    {
        /// <summary> about this many samples in all (k*k per triangle, k in 1..MaxSubdivisions): the cost is that many nearest-triangle queries </summary>
        internal const int SamplesTarget = 1 << 19;
        internal const int MaxSubdivisions = 4;
        // the points of a batch: bounded memory for any input
        const int ChunkTriangles = 1 << 14;

        internal sealed class Report
        {
            /// <summary> area of the input (of the triangles that were sampled) </summary>
            public double InputArea;
            /// <summary> estimated area of the input farther than the tolerance from the result </summary>
            public double LostArea;
            /// <summary> input triangles with more than half of their samples lost (ids of the input) </summary>
            public List<int> LostTriangles = new List<int>();
            /// <summary> the largest distance of a sample to the result (also of a sample that is not lost); PositiveInfinity for an empty result </summary>
            public double MaxDistance;
            public int Subdivisions;
        }

        internal static int SubdivisionsFor(int triangleCount)
        {
            if (triangleCount <= 0)
                return 1;
            int k = (int)Math.Floor(Math.Sqrt((double)SamplesTarget / triangleCount));
            return Math.Max(1, Math.Min(MaxSubdivisions, k));
        }

        /// <summary>
        /// Measure the input against the result. tolerance is an absolute length (a sample at a distance of more than it is lost). Returns null if
        /// cancelled. parallel: the batch queries on all cores (the report is the same either way).
        /// </summary>
        public static Report Measure(DMesh3 input, DMesh3 result, double tolerance, bool parallel = true, Func<bool> cancel = null)
        {
            var report = new Report();
            int k = report.Subdivisions = SubdivisionsFor(input.TriangleCount);
            int perTri = k * k;
            DMeshAABBTree3 tree = null;
            if (result.TriangleCount > 0) {
                tree = new DMeshAABBTree3(result, true);
            }
            var options = new BatchOptions() { Parallel = parallel };
            var points = new Vector3d[ChunkTriangles * perTri];
            var found = new NearestPointResult[points.Length];
            var ids = new int[ChunkTriangles];
            var areas = new double[ChunkTriangles];
            double lost = 0, total = 0, maxDist = 0;
            int count = 0;
            IEnumerator<int> en = input.TriangleIndices().GetEnumerator();
            bool more = true;
            while (more) {
                count = 0;
                while (count < ChunkTriangles) {
                    if (en.MoveNext() == false) { more = false; break; }
                    int t = en.Current;
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    input.GetTriVertices(t, ref a, ref b, ref c);
                    ids[count] = t;
                    areas[count] = input.GetTriArea(t);
                    Samples(a, b, c, k, points, count * perTri);
                    count++;
                }
                if (count == 0)
                    break;
                if (cancel != null && cancel())
                    return null;
                int n = count * perTri;
                if (tree != null) {
                    var chunk = n == points.Length ? points : Trim(points, n);
                    var res = n == found.Length ? found : new NearestPointResult[n];
                    MeshBatchQueries.NearestPointBatch(tree, chunk, res, null, options);
                    if (res != found)
                        Array.Copy(res, found, n);
                }
                for (int i = 0; i < count; ++i) {
                    int lostSamples = 0;
                    for (int j = 0; j < perTri; ++j) {
                        double d = tree == null ? double.PositiveInfinity : found[i * perTri + j].Distance;
                        if (d == double.MaxValue)
                            d = double.PositiveInfinity;
                        if (d > maxDist)
                            maxDist = d;
                        if (d > tolerance)
                            lostSamples++;
                    }
                    total += areas[i];
                    if (lostSamples > 0)
                        lost += areas[i] * ((double)lostSamples / perTri);
                    if (2 * lostSamples > perTri)
                        report.LostTriangles.Add(ids[i]);
                }
            }
            report.InputArea = total;
            report.LostArea = lost;
            report.MaxDistance = maxDist;
            return report;
        }

        static Vector3d[] Trim(Vector3d[] points, int n)
        {
            var r = new Vector3d[n];
            Array.Copy(points, r, n);
            return r;
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
    }
}
