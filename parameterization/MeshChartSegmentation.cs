// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Segments a mesh into charts: connected regions of triangles with similar normals, for a texture atlas.
    ///
    /// Region growing in the style of variational shape approximation (Cohen-Steiner, Alliez and Desbrun 2004,
    /// L2,1 metric) with the compactness term of D-Charts (Julius, Kraevoy and Sheffer 2005): each chart has a
    /// proxy (normal N, centroid C); the charts are grown from seed triangles in order of the cost
    /// area * (|n - N|^2 + Compactness * |c - C|^2 / D^2) (D the bounding box diagonal of the region), then
    /// proxies and seeds are updated (Lloyd iterations). While a triangle deviates from its chart's normal by
    /// more than MaxNormalAngle degrees and there are fewer than MaxCharts charts, a chart is seeded at the
    /// worst triangle (the farthest point heuristic) and the charts are regrown. Every chart is connected
    /// (grown through edges). Triangles of less than 5% of the mean area are ignored in the deviation (the
    /// normals of slivers are noise). Charts are not guaranteed to be discs; MeshDiscCut cuts them if needed.
    /// </summary>
    public class MeshChartSegmentation
    {
        public DMesh3 Mesh;
        /// <summary> largest angle in degrees between a triangle normal and the normal of its chart </summary>
        public double MaxNormalAngle = 40;
        public int MaxCharts = 64;
        /// <summary> weight of the distance to the chart centroid in the growing cost (compact, round charts) </summary>
        public double Compactness = 0.1;
        public int LloydIterations = 4;

        /// <summary> chart index by triangle id (-1 for unused ids) after Compute() </summary>
        public int[] TriangleChart;
        public int ChartCount;
        /// <summary> largest angle in degrees between a triangle normal and its chart normal after Compute() </summary>
        public double MaxAngle;

        public MeshChartSegmentation(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public bool Compute()
        {
            var tris = new List<int>(Mesh.TriangleIndices());
            TriangleChart = new int[Mesh.MaxTriangleID];
            for (int i = 0; i < TriangleChart.Length; ++i) TriangleChart[i] = -1;
            ChartCount = 0; MaxAngle = 0;
            if (tris.Count == 0)
                return false;
            int[] labels;
            double maxAngle;
            ChartCount = Segment(Mesh, tris, MaxNormalAngle, Math.Max(1, MaxCharts), Compactness, LloydIterations, 1, out labels, out maxAngle);
            for (int k = 0; k < tris.Count; ++k)
                TriangleChart[tris[k]] = labels[k];
            MaxAngle = maxAngle;
            return true;
        }


        /// <summary>
        /// Splits the connected region of triangles into exactly two connected charts (fewer only for a single
        /// triangle): the second proxy is seeded at the triangle whose normal deviates most, or, in a flat
        /// region, at the triangle farthest from the first seed. labels[k] is 0 or 1 for tris[k].
        /// </summary>
        public static int SplitInTwo(DMesh3 mesh, IList<int> tris, double compactness, out int[] labels)
        {
            double maxAngle;
            return Segment(mesh, tris, 0, 2, compactness, 4, 2, out labels, out maxAngle);
        }


        /// <summary>
        /// segments the triangles tris (any subset of the mesh) into at most maxCharts connected charts, with at
        /// least minCharts (if there are that many triangles per connected part); returns the chart count
        /// </summary>
        internal static int Segment(DMesh3 mesh, IList<int> tris, double maxNormalAngleDeg, int maxCharts, double compactness,
            int lloydIterations, int minCharts, out int[] labels, out double maxAngleDeg)
        {
            int n = tris.Count;
            var local = new Dictionary<int, int>();
            for (int k = 0; k < n; ++k) local[tris[k]] = k;
            Vector3d[] nrm = new Vector3d[n], cen = new Vector3d[n];
            double[] area = new double[n];
            int[][] nbr = new int[n][];
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            for (int k = 0; k < n; ++k) {
                Vector3d a = mesh.GetVertex(mesh.GetTriangle(tris[k]).a), b = mesh.GetVertex(mesh.GetTriangle(tris[k]).b), c = mesh.GetVertex(mesh.GetTriangle(tris[k]).c);
                Vector3d cr = (b - a).Cross(c - a);
                double len = cr.Length;
                area[k] = 0.5 * len;
                nrm[k] = len > 0 && MathUtil.IsFinite(len) ? cr / len : Vector3d.Zero;
                cen[k] = (a + b + c) / 3;
                box.Contain(a); box.Contain(b); box.Contain(c);
                Index3i nb = mesh.GetTriNeighbourTris(tris[k]);
                var list = new List<int>(3);
                for (int j = 0; j < 3; ++j) {
                    int l;
                    if (nb[j] != DMesh3.InvalidID && local.TryGetValue(nb[j], out l)) list.Add(l);
                }
                nbr[k] = list.ToArray();
            }
            double D2 = Math.Max(box.DiagonalLength * box.DiagonalLength, 1e-300);
            // tiny triangles still need a positive cost
            double meanArea = 0;
            foreach (double a in area) meanArea += a;
            meanArea /= Math.Max(n, 1);
            double minArea = 1e-12 * meanArea, sliverArea = 0.05 * meanArea;

            labels = new int[n];
            for (int k = 0; k < n; ++k) labels[k] = -1;

            // one seed per connected part
            var seeds = new List<int>();
            {
                int[] part = new int[n];
                for (int k = 0; k < n; ++k) part[k] = -1;
                for (int s = 0; s < n; ++s) {
                    if (part[s] >= 0) continue;
                    var stack = new Stack<int>();
                    stack.Push(s); part[s] = seeds.Count;
                    while (stack.Count > 0) {
                        int t = stack.Pop();
                        foreach (int m in nbr[t]) if (part[m] < 0) { part[m] = seeds.Count; stack.Push(m); }
                    }
                    seeds.Add(s);
                }
            }
            var pN = new List<Vector3d>();
            var pC = new List<Vector3d>();
            foreach (int s in seeds) { pN.Add(nrm[s]); pC.Add(cen[s]); }
            Func<int, int, double> cost = (t, k) =>
                Math.Max(area[t], minArea) * ((nrm[t] - pN[k]).LengthSquared + compactness * (cen[t] - pC[k]).LengthSquared / D2);

            // add charts one at a time with one Lloyd iteration each, then converge with the full iterations
            maxAngleDeg = 0;
            bool final = false;
            while (true) {
                for (int iter = 0; iter < (final ? Math.Max(1, lloydIterations) : 1); ++iter) {
                    grow(seeds, nbr, labels, cost);
                    // update the proxies, then the seeds (triangle of least cost in each chart)
                    int K = seeds.Count;
                    var sumN = new Vector3d[K]; var sumC = new Vector3d[K]; var sumA = new double[K];
                    for (int t = 0; t < n; ++t) {
                        int k = labels[t];
                        double w = Math.Max(area[t], minArea);
                        sumN[k] += w * nrm[t]; sumC[k] += w * cen[t]; sumA[k] += w;
                    }
                    for (int k = 0; k < K; ++k) {
                        double l = sumN[k].Length;
                        pN[k] = l > 0 ? sumN[k] / l : pN[k];
                        pC[k] = sumC[k] / sumA[k];
                    }
                    double[] bestCost = new double[K];
                    for (int k = 0; k < K; ++k) bestCost[k] = double.PositiveInfinity;
                    for (int t = 0; t < n; ++t) {
                        double c = cost(t, labels[t]) / Math.Max(area[t], minArea);
                        if (c < bestCost[labels[t]]) { bestCost[labels[t]] = c; seeds[labels[t]] = t; }
                    }
                }
                grow(seeds, nbr, labels, cost);

                // worst triangle: largest normal deviation (ties, eg in a flat chart: farthest from the seed);
                // slivers (eg from marching cubes) have arbitrary normals and do not count
                // (and are never seeds: a chart seeded at a cap keeps a zero normal and stays a single cap)
                int worst = -1; double worstAngle = -1, worstDist = -1;
                for (int pass = 0; pass < 2 && worst < 0; ++pass)
                    for (int t = 0; t < n; ++t) {
                        if (pass == 0 && area[t] < sliverArea) continue;
                        double ang = nrm[t].LengthSquared > 0 ? Vector3d.AngleD(nrm[t], pN[labels[t]]) : 0;
                        if (!MathUtil.IsFinite(ang)) ang = 0;
                        double dist = (cen[t] - cen[seeds[labels[t]]]).LengthSquared;
                        if (ang > worstAngle + 1e-9 || (Math.Abs(ang - worstAngle) <= 1e-9 && dist > worstDist)) {
                            worst = t; worstAngle = ang; worstDist = dist;
                        }
                    }
                maxAngleDeg = Math.Max(worstAngle, 0);
                bool needMore = seeds.Count < minCharts || worstAngle > maxNormalAngleDeg;
                bool canAdd = seeds.Count < maxCharts && !seeds.Contains(worst) && !(seeds.Count >= minCharts && worstAngle < 1e-6);
                if (!needMore || !canAdd) {
                    if (final) break;
                    final = true;     // converge, then check again
                    continue;
                }
                final = false;
                // in a flat region the deviation is about the same everywhere: seed at the farthest triangle
                if (worstAngle < 1e-3) {
                    double far = -1;
                    for (int pass = 0; pass < 2 && far < 0; ++pass)
                        for (int t = 0; t < n; ++t) {
                            if (pass == 0 && area[t] < sliverArea) continue;
                            double dist = (cen[t] - cen[seeds[labels[t]]]).LengthSquared;
                            if (dist > far && !seeds.Contains(t)) { far = dist; worst = t; }
                        }
                }
                seeds.Add(worst);
                pN.Add(nrm[worst]); pC.Add(cen[worst]);
            }
            return seeds.Count;
        }

        // flood from the seeds in order of cost; each triangle joins the chart that reaches it first
        static void grow(List<int> seeds, int[][] nbr, int[] labels, Func<int, int, double> cost)
        {
            int n = labels.Length;
            for (int t = 0; t < n; ++t) labels[t] = -1;
            var heap = new SortedSet<Tuple<double, int, int>>();
            for (int k = 0; k < seeds.Count; ++k) {
                labels[seeds[k]] = k;
            }
            for (int k = 0; k < seeds.Count; ++k)
                foreach (int m in nbr[seeds[k]])
                    if (labels[m] < 0) heap.Add(Tuple.Create(cost(m, k), m, k));
            while (heap.Count > 0) {
                var top = heap.Min;
                heap.Remove(top);
                int t = top.Item2, k = top.Item3;
                if (labels[t] >= 0) continue;
                labels[t] = k;
                foreach (int m in nbr[t])
                    if (labels[m] < 0) heap.Add(Tuple.Create(cost(m, k), m, k));
            }
        }
    }
}
