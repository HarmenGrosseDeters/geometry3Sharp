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
            Analyze(tris, alpha, out int[] corner, out int[] comp, out double[] opening, out _, out _);
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

        /// <summary> the sizes the offset band of the wrap's estimate needs (see Terms); lengths and areas in the units of the input </summary>
        public struct EstimateTerms
        {
            /// <summary> area of the components with an opening (as OpenArea): counted twice, the wrap covers both sides </summary>
            public double OpenArea;
            /// <summary> length of the boundary edges of those components: the rim the wrap goes around at the radius of the offset </summary>
            public double OpenBoundary;
            /// <summary> area of the other components whose thickness 2 V / A is below ThinFactor * alpha: a closed plate is a sheet with a rim </summary>
            public double ThinArea;
            /// <summary>
            /// the rim length of those components: half the length of their sharp edges (top and bottom face each have a rim edge), but at least the
            /// perimeter of a disc of the sheet area (2 sqrt(pi S)): the faces of a plate thinner than the weld cell are welded into one sheet, whose
            /// rim edges are used by more than two triangles and not counted as sharp
            /// </summary>
            public double ThinRim;
            /// <summary> length of the sharp edges of all other components: where the empty ball touches the edge from outside, the wrap goes around it at the offset </summary>
            public double ThickSharp;
        }

        /// <summary> a component is thin (a plate) if its thickness 2 |V| / A, in alpha, is below this: measured (plates of thickness 1e-4 to 0.4 at alpha 0.059): 1e-2 and below 4 to 18 times the one-sided density, 0.02 and above (0.34 alpha) 2.4 to 3.8, like a cube </summary>
        public const double ThinFactor = 0.2;
        /// <summary> the cosine of the dihedral turn from which an edge is sharp (45 degrees) </summary>
        public const double SharpCos = 0.7071067811865476;

        /// <summary>
        /// The terms of the estimate's offset band, from the welded components of Analyze: the area and boundary length of the open
        /// components, the area and sharp edges of the thin closed ones (volume by the divergence theorem about a vertex of the
        /// component, an oriented closed surface: an unoriented soup has a volume near zero and counts as thin), the sharp edges of the rest.
        /// An estimate: folds, non-manifold edges and inverted parts are counted as they come.
        /// </summary>
        public static EstimateTerms Terms(List<Vector3d> tris, double alpha)
        {
            var terms = new EstimateTerms();
            if (tris.Count < 3 || !(alpha > 0))
                return terms;
            Analyze(tris, alpha, out int[] corner, out int[] comp, out double[] opening, out Vector3d[] pos, out Dictionary<(int, int), int> edgeUse);
            double width = OpeningFactor * alpha;
            int n = tris.Count / 3;
            // (per component root, in vertex ids: arrays, so that the sums come in a fixed order)
            int nv = pos.Length;
            var area = new double[nv];
            var volume = new double[nv];
            var hasReference = new bool[nv];
            var reference = new Vector3d[nv];
            var sharp = new double[nv];
            var firstNormal = new Dictionary<(int, int), Vector3d>();
            for (int t = 0; t < n; ++t) {
                int root = Find(comp, corner[3 * t]);
                Vector3d a = tris[3 * t], b = tris[3 * t + 1], c = tris[3 * t + 2];
                if (!hasReference[root]) { hasReference[root] = true; reference[root] = a; }
                Vector3d cross = (b - a).Cross(c - a);
                area[root] += 0.5 * cross.Length;
                Vector3d r = reference[root];
                volume[root] += (a - r).Dot((b - r).Cross(c - r)) / 6.0;
                double len = cross.Length;
                if (!(len > 0)) continue;
                Vector3d normal = cross / len;
                for (int k = 0; k < 3; ++k) {
                    int u = corner[3 * t + k], v = corner[3 * t + (k + 1) % 3];
                    if (u == v) continue;
                    var key = u < v ? (u, v) : (v, u);
                    if (edgeUse.TryGetValue(key, out int uses) && uses == 2) {
                        if (firstNormal.TryGetValue(key, out Vector3d other)) {
                            if (other.Dot(normal) < SharpCos) {
                                sharp[root] += (pos[u] - pos[v]).Length;
                            }
                            firstNormal.Remove(key);
                        } else {
                            firstNormal[key] = normal;
                        }
                    }
                }
            }
            foreach (var kv in edgeUse)
                if (kv.Value == 1) {
                    int root = Find(comp, kv.Key.Item1);
                    if (opening[root] > width)
                        terms.OpenBoundary += (pos[kv.Key.Item1] - pos[kv.Key.Item2]).Length;
                }
            for (int root = 0; root < nv; ++root) {
                if (!hasReference[root]) continue;
                if (opening[root] > width) {
                    terms.OpenArea += area[root];
                } else if (area[root] > 0 && 2 * Math.Abs(volume[root]) / area[root] < ThinFactor * alpha) {
                    terms.ThinArea += area[root];
                    terms.ThinRim += Math.Max(0.5 * sharp[root], 2 * Math.Sqrt(Math.PI * 0.5 * area[root]));
                } else {
                    terms.ThickSharp += sharp[root];
                }
            }
            return terms;
        }

        /// <summary> the largest opening of the input: the largest bounding box diagonal of a chain of boundary edges (0 if there are none), welded with the cells of alpha </summary>
        public static double LargestOpening(List<Vector3d> tris, double alpha)
        {
            if (tris.Count < 3 || !(alpha > 0))
                return 0;
            Analyze(tris, alpha, out _, out _, out double[] opening, out _, out _);
            return opening.Length == 0 ? 0 : opening.Max();
        }

        // corner: the welded vertex of each corner; comp: the union-find of the vertices by triangle; opening: by component root, the
        // largest diagonal of its boundary chains
        static void Analyze(List<Vector3d> tris, double alpha, out int[] corner, out int[] comp, out double[] opening, out Vector3d[] pos, out Dictionary<(int, int), int> edgeUse)
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
            edgeUse = new Dictionary<(int, int), int>(Math.Min(tris.Count, 1 << 20));
            for (int t = 0; t < n; ++t) {
                int a = corner[3 * t], b = corner[3 * t + 1], c = corner[3 * t + 2];
                Union(comp, a, b); Union(comp, b, c);
                CountEdge(edgeUse, a, b); CountEdge(edgeUse, b, c); CountEdge(edgeUse, c, a);
            }
            pos = new Vector3d[nv];
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
