// Reference computations for the checks, independent of the library code under test: shortest paths on
// the grid TIN, horizontal distances to polylines, distances to the grid TIN.
using System;
using System.Collections.Generic;
using g3;

namespace TerrainBenchmark
{
    public static class Checks
    {
        /// <summary>
        /// Shortest surface paths on the grid TIN (Dijkstra). Each node is linked to the nodes at offsets
        /// (a, b) with |a|, |b| &lt;= 3 and gcd(a, b) = 1 (32 directions, at most 1.0% longer than the straight
        /// line in the plane); a link is a straight line in XY whose length is measured exactly along the
        /// TIN. Every path found runs on the surface, so the distances are upper bounds of the geodesic
        /// distance up to about 1%. The sources are points (eg densified polylines); the nodes within
        /// 1.5 cells of a source start with their straight 3D distance to it.
        /// Returns the distances as a grid (NaN where not reached).
        /// </summary>
        public static Grid SurfaceDijkstra(Grid g, IList<Vector3d> sources)
        {
            int N = g.NX * g.NY;
            var dist = new double[N];
            for (int k = 0; k < N; ++k) dist[k] = double.PositiveInfinity;
            var pq = new PriorityQueue<int, double>();
            double r = 1.5 * g.Dx;
            foreach (Vector3d s in sources) {
                int i0 = (int)Math.Floor((s.x - r - g.X0) / g.Dx), i1 = (int)Math.Ceiling((s.x + r - g.X0) / g.Dx);
                int j0 = (int)Math.Floor((s.y - r - g.Y0) / g.Dx), j1 = (int)Math.Ceiling((s.y + r - g.Y0) / g.Dx);
                for (int j = Math.Max(0, j0); j <= Math.Min(g.NY - 1, j1); ++j)
                    for (int i = Math.Max(0, i0); i <= Math.Min(g.NX - 1, i1); ++i) {
                        if (double.IsNaN(g[i, j])) continue;
                        double d = new Vector3d(g.X(i), g.Y(j), g[i, j]).Distance(s);
                        int k = i + g.NX * j;
                        if (d < dist[k]) { dist[k] = d; pq.Enqueue(k, d); }
                    }
            }
            var offsets = new List<Index2i>();
            for (int a = -3; a <= 3; ++a)
                for (int b = -3; b <= 3; ++b)
                    if ((a != 0 || b != 0) && gcd(Math.Abs(a), Math.Abs(b)) == 1)
                        offsets.Add(new Index2i(a, b));
            var done = new bool[N];
            while (pq.Count > 0) {
                int k; double d;
                pq.TryDequeue(out k, out d);
                if (done[k] || d > dist[k]) continue;
                done[k] = true;
                int i = k % g.NX, j = k / g.NX;
                foreach (Index2i o in offsets) {
                    int ni = i + o.a, nj = j + o.b;
                    if (ni < 0 || nj < 0 || ni >= g.NX || nj >= g.NY) continue;
                    int nk = ni + g.NX * nj;
                    if (done[nk]) continue;
                    double len = SurfaceLength(g, i, j, ni, nj);
                    if (double.IsNaN(len)) continue;
                    if (d + len < dist[nk]) { dist[nk] = d + len; pq.Enqueue(nk, d + len); }
                }
            }
            var result = new Grid() { X0 = g.X0, Y0 = g.Y0, Dx = g.Dx, NX = g.NX, NY = g.NY, Z = new float[N], Name = "dijkstra" };
            for (int k = 0; k < N; ++k)
                result.Z[k] = double.IsInfinity(dist[k]) ? float.NaN : (float)dist[k];
            return result;
        }

        /// <summary>
        /// Upper bound of the surface distance at (x, y) from the Dijkstra distances: through each corner of
        /// the cell, plus the length along the TIN of the straight XY segment from the corner (a surface
        /// path). Linear interpolation of the node values is no bound: it is too small at ridges of the
        /// distance field (between two sources).
        /// </summary>
        public static double SurfaceBound(Grid g, Grid dist, double x, double y)
        {
            double fx = (x - g.X0) / g.Dx, fy = (y - g.Y0) / g.Dx;
            int i = Math.Min(g.NX - 2, Math.Max(0, (int)Math.Floor(fx))), j = Math.Min(g.NY - 2, Math.Max(0, (int)Math.Floor(fy)));
            double best = double.NaN;
            for (int k = 0; k < 4; ++k) {
                int ci = i + (k & 1), cj = j + (k >> 1);
                double u = dist[ci, cj];
                if (double.IsNaN(u)) continue;
                double len = surface_length_to(g, ci, cj, fx, fy);
                if (double.IsNaN(len)) continue;
                if (double.IsNaN(best) || u + len < best) best = u + len;
            }
            return best;
        }

        // length along the TIN from node (i, j) straight (in XY) to the point (fx, fy) in cell units
        static double surface_length_to(Grid g, int i, int j, double fx, double fy)
        {
            double a = fx - i, b = fy - j;
            var ts = new List<double>() { 0, 1 };
            // crossings with x - y = const (the diagonals) inside the segment, and the cell borders
            double c0 = i - j, c1 = fx - fy;
            for (double c = Math.Ceiling(Math.Min(c0, c1)); c <= Math.Floor(Math.Max(c0, c1)); c += 1)
                if (Math.Abs(c1 - c0) > 1e-12) ts.Add((c - c0) / (c1 - c0));
            for (double c = Math.Ceiling(Math.Min(i, fx)); c <= Math.Floor(Math.Max(i, fx)); c += 1)
                if (Math.Abs(a) > 1e-12) ts.Add((c - i) / a);
            for (double c = Math.Ceiling(Math.Min(j, fy)); c <= Math.Floor(Math.Max(j, fy)); c += 1)
                if (Math.Abs(b) > 1e-12) ts.Add((c - j) / b);
            ts.RemoveAll(t => t < 0 || t > 1);
            ts.Sort();
            double len = 0;
            Vector3d prev = Vector3d.Zero;
            for (int k = 0; k < ts.Count; ++k) {
                double x = i + ts[k] * a, y = j + ts[k] * b;
                double z = height_cells(g, x, y);
                if (double.IsNaN(z)) return double.NaN;
                Vector3d p = new Vector3d(x * g.Dx, y * g.Dx, z);
                if (k > 0) len += p.Distance(prev);
                prev = p;
            }
            return len;
        }

        static int gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

        /// <summary> length along the TIN of the straight XY segment between nodes (i,j) and (ni,nj) </summary>
        public static double SurfaceLength(Grid g, int i, int j, int ni, int nj)
        {
            // break points: crossings with the grid lines x = const, y = const and the diagonals x - y = const
            int a = ni - i, b = nj - j;
            var ts = new List<double>() { 0, 1 };
            for (int s = 1; s < Math.Abs(a); ++s) ts.Add((double)s / Math.Abs(a));
            for (int s = 1; s < Math.Abs(b); ++s) ts.Add((double)s / Math.Abs(b));
            if (a != b)
                for (int s = 1; s < Math.Abs(a - b); ++s) ts.Add((double)s / Math.Abs(a - b));
            ts.Sort();
            double len = 0;
            Vector3d prev = Vector3d.Zero;
            for (int k = 0; k < ts.Count; ++k) {
                // the point relative to node (i, j), in cell units; heights by the TIN of the cell
                double x = i + ts[k] * a, y = j + ts[k] * b;
                double z = height_cells(g, x, y);
                if (double.IsNaN(z)) return double.NaN;
                Vector3d p = new Vector3d(x * g.Dx, y * g.Dx, z);
                if (k > 0) len += p.Distance(prev);
                prev = p;
            }
            return len;
        }

        // TIN height at (x, y) in cell units (exact at nodes and on cell borders)
        static double height_cells(Grid g, double x, double y)
        {
            int i = (int)Math.Floor(x + 1e-12), j = (int)Math.Floor(y + 1e-12);
            if (i >= g.NX - 1) i = g.NX - 2;
            if (j >= g.NY - 1) j = g.NY - 2;
            double fx = x - i, fy = y - j;
            if (Math.Abs(fx) < 1e-12) fx = 0;
            if (Math.Abs(fy) < 1e-12) fy = 0;
            double z00 = g[i, j], z10 = g[i + 1, j], z01 = g[i, j + 1], z11 = g[i + 1, j + 1];
            if (fx >= fy)
                return z00 + fx * (z10 - z00) + fy * (z11 - z10);
            return z00 + fy * (z01 - z00) + fx * (z11 - z01);
        }


        /// <summary> horizontal distance from points to a set of polylines (segments binned in a hash grid) </summary>
        public class HorizontalDistance
        {
            List<Segment2d> segs = new List<Segment2d>();
            Dictionary<long, List<int>> bins = new Dictionary<long, List<int>>();
            double cell;

            public HorizontalDistance(IEnumerable<List<Vector3d>> lines, double cellSize)
            {
                cell = cellSize;
                foreach (var l in lines) {
                    if (l.Count == 1) add(new Segment2d(l[0].xy, l[0].xy));
                    for (int k = 0; k + 1 < l.Count; ++k) add(new Segment2d(l[k].xy, l[k + 1].xy));
                }
            }

            long key(int i, int j) { return ((long)i << 32) ^ (uint)j; }

            void add(Segment2d s)
            {
                int id = segs.Count;
                segs.Add(s);
                Vector2d a = s.P0, b = s.P1;
                int i0 = (int)Math.Floor(Math.Min(a.x, b.x) / cell), i1 = (int)Math.Floor(Math.Max(a.x, b.x) / cell);
                int j0 = (int)Math.Floor(Math.Min(a.y, b.y) / cell), j1 = (int)Math.Floor(Math.Max(a.y, b.y) / cell);
                for (int i = i0; i <= i1; ++i)
                    for (int j = j0; j <= j1; ++j) {
                        List<int> l;
                        if (bins.TryGetValue(key(i, j), out l) == false) bins[key(i, j)] = l = new List<int>();
                        l.Add(id);
                    }
            }

            /// <summary> distance to the nearest segment if within maxDist (else +inf) </summary>
            public double Distance(Vector2d q, double maxDist)
            {
                int r = (int)Math.Ceiling(maxDist / cell);
                int ci = (int)Math.Floor(q.x / cell), cj = (int)Math.Floor(q.y / cell);
                double best = double.PositiveInfinity;
                for (int i = ci - r; i <= ci + r; ++i)
                    for (int j = cj - r; j <= cj + r; ++j) {
                        List<int> l;
                        if (bins.TryGetValue(key(i, j), out l) == false) continue;
                        foreach (int s in l) best = Math.Min(best, Math.Sqrt(segs[s].DistanceSquared(q)));
                    }
                return best <= maxDist ? best : double.PositiveInfinity;
            }
        }


        /// <summary> exact 3D distance from p to the grid TIN, searching the cells within maxDist horizontally </summary>
        public static double DistanceToTIN(Grid g, Vector3d p, double maxDist)
        {
            int i0 = Math.Max(0, (int)Math.Floor((p.x - maxDist - g.X0) / g.Dx)), i1 = Math.Min(g.NX - 2, (int)Math.Floor((p.x + maxDist - g.X0) / g.Dx));
            int j0 = Math.Max(0, (int)Math.Floor((p.y - maxDist - g.Y0) / g.Dx)), j1 = Math.Min(g.NY - 2, (int)Math.Floor((p.y + maxDist - g.Y0) / g.Dx));
            double best = double.PositiveInfinity;
            for (int j = j0; j <= j1; ++j)
                for (int i = i0; i <= i1; ++i) {
                    Vector3d a = new Vector3d(g.X(i), g.Y(j), g[i, j]), b = new Vector3d(g.X(i + 1), g.Y(j), g[i + 1, j]);
                    Vector3d c = new Vector3d(g.X(i + 1), g.Y(j + 1), g[i + 1, j + 1]), d = new Vector3d(g.X(i), g.Y(j + 1), g[i, j + 1]);
                    best = Math.Min(best, PointTriangleDistance(p, a, b, c));
                    best = Math.Min(best, PointTriangleDistance(p, a, c, d));
                }
            return best;
        }

        /// <summary> distance from p to the triangle abc (Ericson, Real-Time Collision Detection 5.1.5) </summary>
        public static double PointTriangleDistance(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            Vector3d ab = b - a, ac = c - a, ap = p - a;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            if (d1 <= 0 && d2 <= 0) return p.Distance(a);
            Vector3d bp = p - b;
            double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
            if (d3 >= 0 && d4 <= d3) return p.Distance(b);
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return p.Distance(a + (d1 / (d1 - d3)) * ab);
            Vector3d cp = p - c;
            double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
            if (d6 >= 0 && d5 <= d6) return p.Distance(c);
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return p.Distance(a + (d2 / (d2 - d6)) * ac);
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return p.Distance(b + ((d4 - d3) / ((d4 - d3) + (d5 - d6))) * (c - b));
            double denom = 1.0 / (va + vb + vc);
            return p.Distance(a + ab * (vb * denom) + ac * (vc * denom));
        }


        public static Dictionary<string, object> Stats(List<double> v, string prefix, Dictionary<string, object> r)
        {
            r[prefix + "_n"] = v.Count;
            if (v.Count == 0) return r;
            v.Sort();
            double sum = 0, sum2 = 0;
            foreach (double x in v) { sum += x; sum2 += x * x; }
            r[prefix + "_min"] = v[0];
            r[prefix + "_p01"] = v[(int)(0.01 * (v.Count - 1))];
            r[prefix + "_p50"] = v[(int)(0.5 * (v.Count - 1))];
            r[prefix + "_p99"] = v[(int)(0.99 * (v.Count - 1))];
            r[prefix + "_max"] = v[v.Count - 1];
            r[prefix + "_mean"] = sum / v.Count;
            r[prefix + "_rms"] = Math.Sqrt(sum2 / v.Count);
            return r;
        }
    }
}
