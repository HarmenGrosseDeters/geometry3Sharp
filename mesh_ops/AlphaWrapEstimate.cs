using System;
using System.Collections.Generic;
using System.Linq;

namespace g3
{
    /// <summary>
    /// The two-sidedness of an alpha wrap input, for the size estimate of MeshAlphaWrap (issue #1113): the area of the input triangles
    /// whose component is open, an opening wide enough for the carving ball to enter, so that the wrap covers both sides of it.
    /// </summary>
    static class AlphaWrapEstimate
    {
        /// <summary>
        /// the largest diagonal of the bounding box of an opening's boundary chain, in alpha, from which the wrap goes through it to the other
        /// side. Measured on a sphere with a circular hole of grid-ragged boundary (n = 16 and 24 quads per cube edge, relative alpha 40): the
        /// wrap is one-sided up to chain diagonals of 3.0 alpha (4.63 in one case) and covers both sides from 4.63 (5.03) alpha on; the
        /// diagonal is about 1.5 times the width of the hole, so this is a width of about 2.7 to 3.3 alpha (a ball of radius alpha passes
        /// an opening of about 2 alpha, plus the offset layer and the rounding of the boundary)
        /// </summary>
        public const double OpeningFactor = 4.0;

        /// <summary> the grid cells per alpha of the weld </summary>
        public const double WeldCells = 8.0;

        /// <summary>
        /// The total area (summed in triangle order) of the triangles in components with an opening. tris holds three corners per
        /// triangle. Vertices are welded by the cells of a grid of alpha / WeldCells (a soup of triangles with equal corners is welded, and so
        /// are corners that differ by rounding; a crack wider than a cell is an opening of its own: a corner pair that straddles a cell
        /// border makes a small local chain, not an opening), components are the vertex
        /// connectivity of the triangles, an edge used by exactly one triangle is a boundary edge, and the boundary edges' vertex
        /// chains are the openings: one whose bounding box diagonal is above OpeningFactor * alpha (a smaller hole is wrapped over
        /// from the outside only) makes its component open. An estimate, not a decision: folds and
        /// non-manifold edges are counted as they come. The width is an axis-aligned box diagonal, so it is not rotation invariant
        /// (the same hole turned by 45 degrees changes it by up to 1.4), and a long thin slit counts as wide: near the threshold the
        /// estimate steps by a factor of 2.
        /// </summary>
        public static double OpenArea(List<Vector3d> tris, double alpha)
        {
            if (tris.Count < 3 || !(alpha > 0))
                return 0;
            Analyze(tris, alpha, out int[] corner, out int[] comp, out double[] opening);
            double width = OpeningFactor * alpha;
            bool any = false;
            for (int v = 0; v < opening.Length; ++v)
                if (opening[v] > width) { any = true; break; }
            if (!any)
                return 0;
            double area = 0;
            for (int t = 0; t < tris.Count / 3; ++t)
                if (opening[Find(comp, corner[3 * t])] > width)
                    area += 0.5 * (tris[3 * t + 1] - tris[3 * t]).Cross(tris[3 * t + 2] - tris[3 * t]).Length;
            return area;
        }

        /// <summary> the largest opening of the input: the largest bounding box diagonal of a chain of boundary edges (0 if there are none), welded with the cells of alpha </summary>
        public static double LargestOpening(List<Vector3d> tris, double alpha)
        {
            if (tris.Count < 3 || !(alpha > 0))
                return 0;
            Analyze(tris, alpha, out _, out _, out double[] opening);
            return opening.Length == 0 ? 0 : opening.Max();
        }

        // corner: the welded vertex of each corner; comp: the union-find of the vertices by triangle; opening: by component root, the
        // largest diagonal of its boundary chains
        static void Analyze(List<Vector3d> tris, double alpha, out int[] corner, out int[] comp, out double[] opening)
        {
            int n = tris.Count / 3;
            double cell = alpha / WeldCells;
            var ids = new Dictionary<(long, long, long), int>(Math.Min(tris.Count, 1 << 20));
            corner = new int[tris.Count];
            for (int i = 0; i < tris.Count; ++i) {
                Vector3d p = tris[i];
                var key = (Cell(p.x, cell), Cell(p.y, cell), Cell(p.z, cell));
                if (!ids.TryGetValue(key, out int id)) { id = ids.Count; ids.Add(key, id); }
                corner[i] = id;
            }
            int nv = ids.Count;
            comp = new int[nv];
            for (int i = 0; i < nv; ++i) comp[i] = i;
            var edgeUse = new Dictionary<(int, int), int>(Math.Min(tris.Count, 1 << 20));
            for (int t = 0; t < n; ++t) {
                int a = corner[3 * t], b = corner[3 * t + 1], c = corner[3 * t + 2];
                Union(comp, a, b); Union(comp, b, c);
                CountEdge(edgeUse, a, b); CountEdge(edgeUse, b, c); CountEdge(edgeUse, c, a);
            }
            var pos = new Vector3d[nv];
            for (int i = 0; i < tris.Count; ++i) pos[corner[i]] = tris[i];
            var chain = new int[nv];
            for (int i = 0; i < nv; ++i) chain[i] = i;
            var onBoundary = new bool[nv];
            foreach (var kv in edgeUse)
                if (kv.Value == 1) {
                    Union(chain, kv.Key.Item1, kv.Key.Item2);
                    onBoundary[kv.Key.Item1] = onBoundary[kv.Key.Item2] = true;
                }
            var lo = new Vector3d[nv];
            var hi = new Vector3d[nv];
            var seen = new bool[nv];
            for (int v = 0; v < nv; ++v) {
                if (!onBoundary[v]) continue;
                int r = Find(chain, v);
                if (!seen[r]) { seen[r] = true; lo[r] = pos[v]; hi[r] = pos[v]; continue; }
                for (int k = 0; k < 3; ++k) {
                    if (pos[v][k] < lo[r][k]) lo[r][k] = pos[v][k];
                    if (pos[v][k] > hi[r][k]) hi[r][k] = pos[v][k];
                }
            }
            opening = new double[nv];
            for (int v = 0; v < nv; ++v)
                if (onBoundary[v] && Find(chain, v) == v) {
                    int c = Find(comp, v);
                    opening[c] = Math.Max(opening[c], (hi[v] - lo[v]).Length);
                }
        }

        // (beyond the range of a long, a tiny alpha against large coordinates, the cells merge: the estimate is then infinite anyway)
        static long Cell(double v, double cell) => (long)Math.Max(-9e18, Math.Min(9e18, Math.Floor(v / cell)));

        static void CountEdge(Dictionary<(int, int), int> uses, int a, int b)
        {
            if (a == b) return;
            var key = a < b ? (a, b) : (b, a);
            uses.TryGetValue(key, out int c);
            uses[key] = c + 1;
        }

        static int Find(int[] p, int x)
        {
            while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
            return x;
        }

        static void Union(int[] p, int a, int b)
        {
            a = Find(p, a); b = Find(p, b);
            if (a != b) { if (a < b) p[b] = a; else p[a] = b; }
        }
    }
}
