// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace FilletDetection
{
    /// <summary>
    /// The fillet identification of DeFillet (Jiang, Wang, Zhao, Yan, Chen, Xin, Tu, Wang, ACM TOG 44(4), 2025, section 4.2),
    /// written from the paper (the authors' code is AGPL and was not read):
    ///   - the model is scaled into the unit sphere, the samples are the triangle centroids;
    ///   - the Voronoi vertices of the samples (circumcentres of the Delaunay cells, here Delaunay3) are the candidates for
    ///     rolling-ball centres; a vertex v with radius r is related to the samples S(v) with r &lt;= |s - v| &lt;= r + Epsilon (eq. 1);
    ///   - false vertices are dropped: outside the bounding box, r &gt;= MaxRadius (rule 1), S(v) smaller than 0.1 r (rule 2),
    ///     S(v) not connected on the mesh (rule 3, without the test for holes);
    ///   - the density of the remaining vertices (eq. 4, K nearest neighbours, bandwidth Sigma) picks, per sample, the related
    ///     vertex of the highest density (the rolling-ball trajectory transform, eq. 3);
    ///   - the score of a sample is the variation rate of that radius to its three neighbours (eq. 6, 1 without a vertex);
    ///   - a graph cut with the edge lengths as smoothness (eq. 7 to 9, Lambda) gives the labels.
    /// Differences to the paper: one global Voronoi diagram instead of 1000 patches (the patches are a speed-up and help with
    /// occluding inner walls, which the test solids do not have); rule 2 uses the largest distance to the mean instead of the
    /// minimum enclosing sphere; no test for cylindrical holes (the paper's spherical Voronoi test). Duplicate vertices are
    /// merged, as in the paper.
    /// </summary>
    public class VoronoiDetector
    {
        /// <summary> in units of the bounding sphere radius, as in the paper (defaults 0.006 and 0.06) </summary>
        public double Epsilon = 0.006, MaxRadius = 0.06;
        public int K = 30;
        public double Sigma = 1.0, Lambda = 1.0;

        public Detection Run(DMesh3 mesh)
        {
            var sw = Stopwatch.StartNew();
            int n = mesh.MaxTriangleID;
            var result = new Detection(n);
            AxisAlignedBox3d bounds = mesh.CachedBounds;
            Vector3d mid = bounds.Center; double scale = Scale(mesh);
            var s = new Vector3d[n];
            foreach (int tid in mesh.TriangleIndices()) s[tid] = (mesh.GetTriCentroid(tid) - mid) * scale;
            Vector3d half = 0.5 * scale * bounds.Diagonal;

            // Delaunay triangulation of the samples, inserted along a space filling order
            var dt = new Delaunay3(new AxisAlignedBox3d(-4 * Vector3d.One, 4 * Vector3d.One));
            var order = new List<(ulong, int)>();
            foreach (int tid in mesh.TriangleIndices()) order.Add((Morton(s[tid]), tid));
            order.Sort();
            var sampleOf = new List<int>(); for (int i = 0; i < 8; ++i) sampleOf.Add(-1);
            int failed = 0;
            foreach (var (code, tid) in order) {
                if (dt.Insert(s[tid], -1, 0, out int v) == Delaunay3.InsertResult.Inserted) {
                    while (sampleOf.Count <= v) sampleOf.Add(-1);
                    sampleOf[v] = tid;
                } else ++failed;
            }

            // Voronoi vertices with their related samples, after the rules
            var grid = new PointGrid(s, Math.Max(MaxRadius / 3, Epsilon));
            var centers = new List<Vector3d>(); var radii = new List<double>(); var related = new List<int[]>();
            int cells = 0;
            var inSet = new int[n]; int stamp = 0;
            var known = new HashSet<(long, long, long)>();
            var members = new List<int>(); var stack = new Stack<int>();
            foreach (int c in dt.CellIndices()) {
                Index4i cv = dt.GetCell(c);
                if (cv.a < 8 || cv.b < 8 || cv.c < 8 || cv.d < 8) continue;
                ++cells;
                Vector3d v = dt.Circumcenter(c);
                if (!(Math.Abs(v.x) <= half.x && Math.Abs(v.y) <= half.y && Math.Abs(v.z) <= half.z)) continue;
                double r = v.Distance(dt.GetVertex(cv.a));
                if (!(r < MaxRadius)) continue;                                           // rule 1
                // cospherical samples (the centroids of regular strips) give the same vertex for many cells: once
                if (!known.Add(((long)Math.Round(v.x * 1e9), (long)Math.Round(v.y * 1e9), (long)Math.Round(v.z * 1e9)))) continue;
                members.Clear();
                Vector3d mean = Vector3d.Zero;
                double lo = r * (1 - 1e-9), hi = r + Epsilon;
                grid.InBox(v, hi, i => { double d = s[i].Distance(v); if (d >= lo && d <= hi) { members.Add(i); mean += s[i]; } });
                if (members.Count == 0) continue;
                mean /= members.Count;
                double extent = 0;
                foreach (int i in members) extent = Math.Max(extent, s[i].Distance(mean));
                if (extent < 0.1 * r) continue;                                           // rule 2
                ++stamp;
                foreach (int i in members) inSet[i] = stamp;
                int reached = 0;
                stack.Push(members[0]); inSet[members[0]] = -stamp;
                while (stack.Count > 0) {
                    int t = stack.Pop(); ++reached;
                    Index3i nb = mesh.GetTriNeighbourTris(t);
                    for (int j = 0; j < 3; ++j)
                        if (nb[j] >= 0 && inSet[nb[j]] == stamp) { inSet[nb[j]] = -stamp; stack.Push(nb[j]); }
                }
                if (reached != members.Count) continue;                                   // rule 3
                centers.Add(v); radii.Add(r); related.Add(members.ToArray());
            }

            // density (eq. 4) and the transform (eq. 3)
            var centerArray = centers.ToArray();
            var density = new double[centerArray.Length];
            if (centerArray.Length > 1) {
                var vgrid = new PointGrid(centerArray);
                gParallel.ForEach(Enumerable.Range(0, centerArray.Length), i => {
                    double num = 0, den = 0;
                    foreach (var (d, j) in vgrid.Nearest(centerArray[i], Math.Min(K + 1, centerArray.Length))) {
                        if (j == i) continue;
                        double w = Math.Exp(-d * d / (2 * Sigma * Sigma));
                        num += w; den += d * w;
                    }
                    density[i] = den > 0 ? num / den : double.MaxValue;
                });
            }
            var chosen = new int[n]; for (int i = 0; i < n; ++i) chosen[i] = -1;
            for (int v = 0; v < centerArray.Length; ++v)
                foreach (int i in related[v])
                    if (chosen[i] < 0 || density[v] > density[chosen[i]]) chosen[i] = v;

            // score (eq. 6) and graph cut (eq. 7 to 9)
            var rate = new double[n];
            foreach (int tid in mesh.TriangleIndices()) {
                rate[tid] = 1;
                if (chosen[tid] < 0) continue;
                Index3i nb = mesh.GetTriNeighbourTris(tid);
                double sum = 0; int count = 0;
                for (int j = 0; j < 3; ++j) {
                    if (nb[j] < 0 || chosen[nb[j]] < 0) continue;
                    sum += Math.Abs(radii[chosen[tid]] - radii[chosen[nb[j]]]) / s[tid].Distance(s[nb[j]]); ++count;
                }
                if (count > 0) rate[tid] = Math.Min(1, sum / count);
            }
            var flow = new MaxFlow(n + 2);
            foreach (int tid in mesh.TriangleIndices()) {
                flow.AddEdge(n, tid, 1 - rate[tid], 0);
                flow.AddEdge(tid, n + 1, rate[tid], 0);
            }
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                Index2i ev = mesh.GetEdgeV(eid);
                double len = Lambda * scale * mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                flow.AddEdge(et.a, et.b, len, len);
            }
            flow.Run(n, n + 1);
            foreach (int tid in mesh.TriangleIndices()) {
                if (!flow.SourceSide(tid)) continue;
                result.Fillet[tid] = true;
                if (chosen[tid] >= 0) result.Radius[tid] = radii[chosen[tid]] / scale;
            }
            result.Seconds = sw.Elapsed.TotalSeconds;
            result.Note = "cells " + cells + ", kept " + centerArray.Length + (failed > 0 ? ", " + failed + " samples not inserted" : "");
            return result;
        }

        /// <summary> factor that takes the mesh into the unit sphere around the centre of its bounding box (the same under rotation) </summary>
        public static double Scale(DMesh3 mesh)
        {
            Vector3d mid = mesh.CachedBounds.Center;
            double far = 0;
            foreach (int vid in mesh.VertexIndices()) far = Math.Max(far, mesh.GetVertex(vid).Distance(mid));
            return 1 / far;
        }

        static ulong Morton(Vector3d p)
        {
            ulong code = 0;
            uint x = (uint)((p.x + 2) / 4 * 1048575), y = (uint)((p.y + 2) / 4 * 1048575), z = (uint)((p.z + 2) / 4 * 1048575);
            for (int b = 19; b >= 0; --b)
                code = (code << 3) | (((x >> b) & 1) << 2) | (((y >> b) & 1) << 1) | ((z >> b) & 1);
            return code;
        }
    }
}
