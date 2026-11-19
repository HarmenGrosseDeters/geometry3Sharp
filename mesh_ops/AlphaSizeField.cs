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
        readonly Vector3d[] points;
        readonly double[] alphas;
        readonly double alpha0, slope, radius, cell;
        readonly Dictionary<(long, long, long), int[]> cells = new Dictionary<(long, long, long), int[]>();

        public int FineSamples => points.Length;

        /// <param name="pos">the samples</param>
        /// <param name="alphaJ">their alpha; only those below alpha0 are kept</param>
        /// <param name="alphaMin">the smallest alpha of the field (the reach of a sample is (alpha0 - alphaMin) / slope)</param>
        public AlphaSizeField(Vector3d[] pos, double[] alphaJ, double alpha0, double alphaMin, double slope)
        {
            this.alpha0 = alpha0; this.slope = slope;
            radius = (alpha0 - alphaMin) / slope;
            cell = radius;
            var keep = new List<int>();
            for (int i = 0; i < pos.Length; ++i)
                if (alphaJ[i] < alpha0)
                    keep.Add(i);
            points = new Vector3d[keep.Count]; alphas = new double[keep.Count];
            var lists = new Dictionary<(long, long, long), List<int>>();
            for (int k = 0; k < keep.Count; ++k) {
                points[k] = pos[keep[k]]; alphas[k] = alphaJ[keep[k]];
                var key = Key(points[k]);
                if (!lists.TryGetValue(key, out List<int> l)) lists[key] = l = new List<int>();
                l.Add(k);
            }
            foreach (var kv in lists) cells[kv.Key] = kv.Value.ToArray();
        }

        (long, long, long) Key(Vector3d p) => ((long)Math.Floor(p.x / cell), (long)Math.Floor(p.y / cell), (long)Math.Floor(p.z / cell));

        /// <summary> the alpha at x: alpha0 away from every fine sample, else the smallest alpha_j + L |x - p_j| within reach </summary>
        public double At(Vector3d x)
        {
            if (points.Length == 0) return alpha0;
            double best = alpha0, r2 = radius * radius;
            long x0 = (long)Math.Floor((x.x - radius) / cell), x1 = (long)Math.Floor((x.x + radius) / cell);
            long y0 = (long)Math.Floor((x.y - radius) / cell), y1 = (long)Math.Floor((x.y + radius) / cell);
            long z0 = (long)Math.Floor((x.z - radius) / cell), z1 = (long)Math.Floor((x.z + radius) / cell);
            for (long i = x0; i <= x1; ++i)
                for (long j = y0; j <= y1; ++j)
                    for (long k = z0; k <= z1; ++k) {
                        if (!cells.TryGetValue((i, j, k), out int[] l)) continue;
                        foreach (int m in l) {
                            double d2 = (points[m] - x).LengthSquared;
                            if (d2 > r2) continue;
                            double v = alphas[m] + slope * Math.Sqrt(d2);
                            if (v < best) best = v;
                        }
                    }
            return best;
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
