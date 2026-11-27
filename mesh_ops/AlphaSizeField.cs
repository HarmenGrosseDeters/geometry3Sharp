// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The local alpha of MeshTetrahedralization.MaxSurfaceDeviation (docs/design/TET_MESHING.md section 17.13): a graded size function
    /// alpha(x) = min(alpha0, min_j (alpha_j + L |x - p_j|)) over input surface samples p_j with a finer alpha_j (L = the gradation,
    /// the slope of the field). A size function bounded by its slope is the gradient limiting of Persson, "Mesh size functions for implicit
    /// geometries and PDE-based gradient limiting", Engineering with Computers 22 (2006); the alpha_j come from the distance of the
    /// previous wrap to the input (an a posteriori error indicator, as in adaptive refinement: refine where the measured error is above the
    /// tolerance), see MeshTetrahedralization.
    /// Read only after construction (a uniform hash grid of the samples), so thread-safe; no floating-point reduction across threads;
    /// scaling the input and the tolerance by 2^k scales every value by 2^k exactly.
    /// </summary>
    internal sealed class AlphaSizeField
    {
        const double MinNormal = 2.2250738585072014e-308; // 2^-1022
        const double ThinIndexLimit = 9e18;
        const double QueryIndexLimit = 4e15; // < 2^52: exact in double and safe for x0..x1 iteration

        readonly Vector3d[] points;
        readonly double[] alphas;
        readonly double alpha0, slope, radius, cell;
        readonly int[] wideItems;
        readonly double wideMinAlpha;
        readonly Vector3d wideMin, wideMax;
        sealed class Cell { public int[] Items; public double MinAlpha; public Vector3d Min, Max; }
        readonly Dictionary<(long, long, long), Cell> cells = new Dictionary<(long, long, long), Cell>();

        public int FineSamples => points.Length;

        /// <param name="pos">the samples</param>
        /// <param name="alphaJ">their alpha; only those below alpha0 are kept</param>
        /// <param name="alpha0">the outer (background) alpha value</param>
        /// <param name="alphaMin">the smallest alpha of the field (the reach of a sample is (alpha0 - alphaMin) / slope)</param>
        /// <param name="slope">Lipschitz growth rate of alpha with distance from a fine sample</param>
        /// <remarks>
        /// The cost of a query is bounded: the kept samples are thinned to one per cube of alphaMin / 2 (the smallest alpha_j in it, the first by
        /// index on a tie; the field changes by at most slope * alphaMin * sqrt(3) / 2), so the samples within the reach of a point, which lie on a
        /// surface, number at most about (2 * reach / (alphaMin / 2))^2 whatever the input (20000 samples of a gear on a long axle in one corner included),
        /// and a grid cell whose smallest alpha plus slope times its distance from the point is not below the best so far is skipped without a visit.
        /// </remarks>
        public AlphaSizeField(Vector3d[] pos, double[] alphaJ, double alpha0, double alphaMin, double slope)
        {
            this.alpha0 = alpha0; this.slope = slope;
            radius = (alpha0 > alphaMin && slope > 0) ? (alpha0 - alphaMin) / slope : 0;
            cell = radius / 2;
            double h = alphaMin / 2;
            Dictionary<(long, long, long), int> best = null;
            Dictionary<(long, long, long, byte), int> wideBest = null;
            for (int i = 0; i < pos.Length; ++i) {
                if (!(alphaJ[i] < alpha0)) continue;
                Vector3d p = pos[i];
                double gx = Math.Floor(p.x / h), gy = Math.Floor(p.y / h), gz = Math.Floor(p.z / h);
                if (FitsIndex(gx, ThinIndexLimit) && FitsIndex(gy, ThinIndexLimit) && FitsIndex(gz, ThinIndexLimit)) {
                    var key = ((long)gx, (long)gy, (long)gz);
                    best ??= new Dictionary<(long, long, long), int>();
                    if (!best.TryGetValue(key, out int b) || alphaJ[i] < alphaJ[b] || (alphaJ[i] == alphaJ[b] && i < b)) best[key] = i;
                } else {
                    byte exactAxes = 0;
                    long kx = WideThinKey(p.x, gx, 1, ref exactAxes);
                    long ky = WideThinKey(p.y, gy, 2, ref exactAxes);
                    long kz = WideThinKey(p.z, gz, 4, ref exactAxes);
                    var key = (kx, ky, kz, exactAxes);
                    wideBest ??= new Dictionary<(long, long, long, byte), int>();
                    if (!wideBest.TryGetValue(key, out int b) || alphaJ[i] < alphaJ[b] || (alphaJ[i] == alphaJ[b] && i < b)) wideBest[key] = i;
                }
            }
            var keep = new List<int>((best?.Count ?? 0) + (wideBest?.Count ?? 0));
            if (best != null) keep.AddRange(best.Values);
            if (wideBest != null) keep.AddRange(wideBest.Values);
            keep.Sort();
            points = new Vector3d[keep.Count]; alphas = new double[keep.Count];
            var lists = new Dictionary<(long, long, long), List<int>>();
            List<int> wideList = null;
            bool validCell = cell > 0 && !double.IsInfinity(cell);
            for (int k = 0; k < keep.Count; ++k) {
                points[k] = pos[keep[k]]; alphas[k] = alphaJ[keep[k]];
                if (validCell && TryQueryKey(points[k], out var key)) {
                    if (!lists.TryGetValue(key, out List<int> l)) lists[key] = l = new List<int>();
                    l.Add(k);
                } else {
                    wideList ??= new List<int>();
                    wideList.Add(k);
                }
            }
            foreach (var kv in lists) {
                BuildBounds(kv.Value, out double m, out Vector3d bmin, out Vector3d bmax);
                cells[kv.Key] = new Cell { Items = kv.Value.ToArray(), MinAlpha = m, Min = bmin, Max = bmax };
            }
            if (wideList != null) {
                BuildBounds(wideList, out wideMinAlpha, out wideMin, out wideMax);
                wideItems = wideList.ToArray();
            } else {
                wideItems = new int[0];
                wideMinAlpha = double.MaxValue;
            }
        }

        void BuildBounds(List<int> items, out double minAlpha, out Vector3d bmin, out Vector3d bmax)
        {
            minAlpha = double.MaxValue;
            bmin = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue);
            bmax = new Vector3d(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            foreach (int k in items) {
                if (alphas[k] < minAlpha) minAlpha = alphas[k];
                Vector3d p = points[k];
                if (p.x < bmin.x) bmin.x = p.x; if (p.x > bmax.x) bmax.x = p.x;
                if (p.y < bmin.y) bmin.y = p.y; if (p.y > bmax.y) bmax.y = p.y;
                if (p.z < bmin.z) bmin.z = p.z; if (p.z > bmax.z) bmax.z = p.z;
            }
        }

        static bool FitsIndex(double index, double limit) => Math.Abs(index) <= limit;

        static long WideThinKey(double coordinate, double index, byte axis, ref byte exactAxes)
        {
            if (FitsIndex(index, ThinIndexLimit))
                return (long)index;
            exactAxes |= axis;
            return coordinate == 0 ? 0 : BitConverter.DoubleToInt64Bits(coordinate);
        }

        bool TryQueryKey(Vector3d p, out (long, long, long) key)
        {
            double gx = Math.Floor(p.x / cell), gy = Math.Floor(p.y / cell), gz = Math.Floor(p.z / cell);
            if (FitsIndex(gx, QueryIndexLimit) && FitsIndex(gy, QueryIndexLimit) && FitsIndex(gz, QueryIndexLimit)) {
                key = ((long)gx, (long)gy, (long)gz);
                return true;
            }
            key = default;
            return false;
        }

        static double SafeDistance(Vector3d d)
        {
            double d2 = d.LengthSquared;
            if (d2 >= MinNormal && !double.IsInfinity(d2))
                return Math.Sqrt(d2);
            double m = Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z)));
            if (!(m > 0))
                return 0;
            if (double.IsInfinity(m))
                return double.PositiveInfinity;
            double sx = d.x / m, sy = d.y / m, sz = d.z / m;
            return m * Math.Sqrt(sx * sx + sy * sy + sz * sz);
        }

        static double BoxDistanceLower(Vector3d x, Vector3d bmin, Vector3d bmax)
        {
            double dx = Math.Max(0, ExactInterval.Down(Math.Max(bmin.x - x.x, x.x - bmax.x)));
            double dy = Math.Max(0, ExactInterval.Down(Math.Max(bmin.y - x.y, x.y - bmax.y)));
            double dz = Math.Max(0, ExactInterval.Down(Math.Max(bmin.z - x.z, x.z - bmax.z)));
            double d = SafeDistance(new Vector3d(dx, dy, dz));
            return d > 0 ? Math.Max(0, ExactInterval.Down(d * (1.0 - 8 * MathUtil.Epsilon))) : 0;
        }

        /// <summary> the alpha at x: alpha0 away from every fine sample, else the smallest alpha_j + L |x - p_j| within reach </summary>
        public double At(Vector3d x) => At(x, out _);

        /// <summary> as At, and the number of samples visited (tests: the cost of a query) </summary>
        public double At(Vector3d x, out int visited)
        {
            visited = 0;
            if (points.Length == 0 || !(radius > 0)) return alpha0;
            double best = alpha0, r2 = radius * radius;
            bool normalR2 = r2 >= MinNormal && !double.IsInfinity(r2);
            if (cells.Count > 0 && cell > 0 && !double.IsInfinity(cell)) {
                double fx0 = Math.Floor(ExactInterval.Down(ExactInterval.Down(x.x - radius) / cell));
                double fx1 = Math.Floor(ExactInterval.Up(ExactInterval.Up(x.x + radius) / cell));
                double fy0 = Math.Floor(ExactInterval.Down(ExactInterval.Down(x.y - radius) / cell));
                double fy1 = Math.Floor(ExactInterval.Up(ExactInterval.Up(x.y + radius) / cell));
                double fz0 = Math.Floor(ExactInterval.Down(ExactInterval.Down(x.z - radius) / cell));
                double fz1 = Math.Floor(ExactInterval.Up(ExactInterval.Up(x.z + radius) / cell));
                // Clamp the query window to the representable grid range; anything outside has no cells in the grid.
                long lim = (long)QueryIndexLimit;
                if (!(fx1 < -lim || fx0 > lim || fy1 < -lim || fy0 > lim || fz1 < -lim || fz0 > lim)
                    && !double.IsNaN(fx0) && !double.IsNaN(fx1) && !double.IsNaN(fy0) && !double.IsNaN(fy1) && !double.IsNaN(fz0) && !double.IsNaN(fz1)) {
                    long x0 = fx0 <= -lim ? -lim : (long)fx0, x1 = fx1 >= lim ? lim : (long)fx1;
                    long y0 = fy0 <= -lim ? -lim : (long)fy0, y1 = fy1 >= lim ? lim : (long)fy1;
                    long z0 = fz0 <= -lim ? -lim : (long)fz0, z1 = fz1 >= lim ? lim : (long)fz1;
                    if (x1 - x0 <= 16 && y1 - y0 <= 16 && z1 - z0 <= 16) {
                        for (long i = x0; i <= x1; ++i)
                            for (long j = y0; j <= y1; ++j)
                                for (long k = z0; k <= z1; ++k) {
                                    if (!cells.TryGetValue((i, j, k), out Cell c)) continue;
                                    VisitItems(x, c.Items, c.MinAlpha, c.Min, c.Max, normalR2, r2, ref best, ref visited);
                                }
                    } else {
                        for (int m = 0; m < points.Length; ++m)
                            VisitOne(x, m, normalR2, r2, ref best, ref visited);
                        return best;
                    }
                }
            }
            if (wideItems.Length > 0)
                VisitItems(x, wideItems, wideMinAlpha, wideMin, wideMax, normalR2, r2, ref best, ref visited);
            return best;
        }

        void VisitItems(Vector3d x, int[] items, double minAlpha, Vector3d bmin, Vector3d bmax, bool normalR2, double r2, ref double best, ref int visited)
        {
            double boxDist = BoxDistanceLower(x, bmin, bmax);
            double lower = ExactInterval.Down(minAlpha + ExactInterval.Down(slope * boxDist));
            if (lower >= best) return;
            foreach (int m in items)
                VisitOne(x, m, normalR2, r2, ref best, ref visited);
        }

        void VisitOne(Vector3d x, int m, bool normalR2, double r2, ref double best, ref int visited)
        {
            visited++;
            Vector3d diff = points[m] - x;
            double dist;
            if (normalR2) {
                double d2 = diff.LengthSquared;
                if (d2 > r2) return;
                dist = d2 >= MinNormal ? Math.Sqrt(d2) : SafeDistance(diff);
            } else {
                dist = SafeDistance(diff);
                if (dist > radius) return;
            }
            double v = alphas[m] + slope * dist;
            if (v < best) best = v;
        }

        /// <summary>
        /// n area-weighted samples of the input's triangles, deterministic (a fixed xorshift sequence; the choices depend on ratios of areas only,
        /// so the samples of a 2^k scaled input are the scaled samples bit for bit). Empty if the input has no area.
        /// </summary>
        public static Vector3d[] Samples(DMesh3 m, int n)
        {
            var tris = new List<int>();
            var cum = new List<double>();
            double acc = 0;
            foreach (int t in m.TriangleIndices()) {
                double a = m.GetTriArea(t);
                if (!(a > 0) || double.IsInfinity(a)) continue;
                acc += a; tris.Add(t); cum.Add(acc);
            }
            if (tris.Count == 0 || double.IsInfinity(acc)) return new Vector3d[0];
            var pos = new Vector3d[n];
            ulong s = 0x9E3779B97F4A7C15UL;
            Func<double> rnd = () => { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); };
            for (int i = 0; i < n; ++i) {
                double u = rnd() * acc;
                int lo = 0, hi = tris.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < u) lo = mid + 1; else hi = mid; }
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tris[lo], ref a, ref b, ref c);
                double r1 = Math.Sqrt(rnd()), r2 = rnd();
                pos[i] = (1 - r1) * a + r1 * (1 - r2) * b + r1 * r2 * c;
            }
            return pos;
        }
    }
}
