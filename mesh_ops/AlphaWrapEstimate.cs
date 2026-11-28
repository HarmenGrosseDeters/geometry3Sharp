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
        const double MinNormal = 2.2250738585072014e-308; // 2^-1022
        const double FastScaleMin = 7.888609052210118e-31; // 2^-100
        const double FastScaleMax = 1.2676506002282294e30; // 2^100
        const double SafeGridLimit = 4503599627370496.0;   // 2^52: exact binary64 integer range (ulp(v / cell) <= 1)

        static int Pow2Exp(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int biased = (int)((bits >> 52) & 0x7FFL);
            if (biased != 0)
                return biased - 1023;
            long sbits = BitConverter.DoubleToInt64Bits(v * 18014398509481984.0); // 2^54
            return (int)((sbits >> 52) & 0x7FFL) - 1023 - 54;
        }

        static Vector3d ScaleVec(Vector3d v, int n)
        {
            return new Vector3d(ExactPoint3.ScaleB(v.x, n), ExactPoint3.ScaleB(v.y, n), ExactPoint3.ScaleB(v.z, n));
        }

        static double SafeLength(Vector3d d)
        {
            double d2 = d.LengthSquared;
            if (d2 >= 4 * MinNormal && !double.IsInfinity(d2))
                return Math.Sqrt(d2);
            double m = d.MaxAbs;
            if (double.IsNaN(m)) return double.NaN;
            if (m == 0) return 0;
            if (double.IsInfinity(m)) return double.PositiveInfinity;
            int e = Pow2Exp(m);
            Vector3d ds = ScaleVec(d, -e);
            return ExactPoint3.ScaleB(Math.Sqrt(ds.LengthSquared), e);
        }

        static bool SafeUnit(Vector3d v, out Vector3d u)
        {
            double m = v.MaxAbs;
            if (!(m > 0) || double.IsInfinity(m)) { u = Vector3d.Zero; return false; }
            if (m >= FastScaleMin && m <= FastScaleMax) {
                double l = v.Length;
                if (!(l > 0)) { u = Vector3d.Zero; return false; }
                u = v / l;
                return true;
            }
            Vector3d vs = ScaleVec(v, -Pow2Exp(m));
            u = vs / Math.Sqrt(vs.LengthSquared);
            return true;
        }

        static long NormBits(double d) => BitConverter.DoubleToInt64Bits(d == 0.0 ? 0.0 : d);

        static bool IsOpenWidth(double openingWork, double workAlpha, double alpha, int scaleExp)
        {
            double width = OpeningFactor * workAlpha;
            if (scaleExp == 0 || width > 0)
                return openingWork > width;
            // workAlpha underflowed to 0 at extreme span / alpha (> 2^1072): compare in input units.
            return openingWork > 0 && ExactPoint3.ScaleB(openingWork, scaleExp) > OpeningFactor * alpha;
        }

        static bool IsThinThickness(double absVolume, double area, double workAlpha, double alpha, int scaleExp)
        {
            if (!(area > 0)) return false;
            double limit = ThinFactor * workAlpha;
            if (limit > 0)
                return 2 * absVolume / area < limit;
            // ThinFactor * workAlpha underflowed to 0 while alpha > 0: a zero-volume plate is still thin.
            if (absVolume == 0) return true;
            double rawLimit = ThinFactor * alpha;
            if (rawLimit > 0)
                return ExactPoint3.ScaleB(2 * absVolume / area, scaleExp) < rawLimit;
            // alpha <= 2^-1073: shift both sides up by 2^64 so ThinFactor * alpha stays normal.
            return ExactPoint3.ScaleB(2 * absVolume / area, scaleExp + 64) < ThinFactor * ExactPoint3.ScaleB(alpha, 64);
        }

        // Prepares either the original list (ordinary scale, bit-identical to master) or a shifted, power-of-two
        // globally span-normalized copy where coordinates lie in [0, 2) so area, cubic volume and cross products stay O(1).
        // Normalization is single-scale per mesh (components ~1e-120x smaller than the mesh span are outside this single-alpha estimator).
        // Returns false if any coordinate is non-finite or all points coincide.
        static bool PrepareFrame(List<Vector3d> tris, double alpha, out List<Vector3d> work, out double workAlpha, out int scaleExp, out bool exactKeys)
        {
            work = tris;
            workAlpha = alpha;
            scaleExp = 0;
            exactKeys = false;
            if (tris.Count < 3 || !(alpha > 0) || double.IsInfinity(alpha))
                return false;
            Vector3d lo = tris[0], hi = tris[0];
            double maxAbs = 0;
            for (int i = 0; i < tris.Count; ++i) {
                Vector3d p = tris[i];
                if (!p.IsFinite) return false;
                double ma = p.MaxAbs;
                if (ma > maxAbs) maxAbs = ma;
                if (p.x < lo.x) lo.x = p.x;
                if (p.y < lo.y) lo.y = p.y;
                if (p.z < lo.z) lo.z = p.z;
                if (p.x > hi.x) hi.x = p.x;
                if (p.y > hi.y) hi.y = p.y;
                if (p.z > hi.z) hi.z = p.z;
            }
            Vector3d span = hi - lo;
            int pre = 0;
            if (!span.IsFinite) {
                span = 0.5 * hi - 0.5 * lo;
                pre = 1;
            }
            double spanMax = span.MaxAbs;
            if (!(spanMax > 0))
                return false;
            double cell = alpha / WeldCells;
            if (pre == 0
                && alpha >= FastScaleMin && alpha <= FastScaleMax
                && maxAbs <= FastScaleMax
                && spanMax >= FastScaleMin && spanMax <= FastScaleMax
                && cell > 0 && maxAbs / cell <= SafeGridLimit) {
                return true;
            }
            int exp = Pow2Exp(spanMax);
            scaleExp = exp + pre;
            workAlpha = ExactPoint3.ScaleB(alpha, -scaleExp);
            double workCell = workAlpha / WeldCells;
            // In the normalized frame coordinates lie in [0, 2); when 2 / workCell > 2^52, v / workCell can exceed binary64's
            // exact integer range (ulp > 1) and grid-cell division loses resolution. Fall back to exact-bit welding: once
            // workCell < 2^-52 (2 / workCell > 2^53; sub-half-ULP at span / cell > 2^54) it agrees with infinite-precision cells
            // on [1, 2), and in the narrow band workCell in [2^-52, 2^-51) it acts as a conservative non-collapsing fallback
            // without claiming exact equivalence near lo.
            exactKeys = !(workCell > 0) || (2.0 / workCell > SafeGridLimit);
            work = new List<Vector3d>(tris.Count);
            if (pre == 0) {
                for (int i = 0; i < tris.Count; ++i)
                    work.Add(ScaleVec(tris[i] - lo, -exp));
            } else {
                Vector3d halfLo = 0.5 * lo;
                for (int i = 0; i < tris.Count; ++i)
                    work.Add(ScaleVec(0.5 * tris[i] - halfLo, -exp));
            }
            return true;
        }

        public static double OpenArea(List<Vector3d> tris, double alpha)
        {
            if (!PrepareFrame(tris, alpha, out List<Vector3d> work, out double workAlpha, out int scaleExp, out bool exactKeys))
                return 0;
            Analyze(work, workAlpha, exactKeys, out int[] corner, out int[] comp, out double[] opening, out _, out _);
            bool any = false;
            for (int v = 0; v < opening.Length; ++v)
                if (IsOpenWidth(opening[v], workAlpha, alpha, scaleExp)) { any = true; break; }
            if (!any)
                return 0;
            double area = 0;
            for (int t = 0; t < work.Count / 3; ++t)
                if (IsOpenWidth(opening[Find(comp, corner[3 * t])], workAlpha, alpha, scaleExp))
                    area += 0.5 * SafeLength((work[3 * t + 1] - work[3 * t]).Cross(work[3 * t + 2] - work[3 * t]));
            return scaleExp == 0 ? area : ExactPoint3.ScaleB(area, 2 * scaleExp);
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
        public static readonly double ThinFactor = 0.2;
        /// <summary> the cosine of the dihedral turn from which an edge is sharp (45 degrees) </summary>
        public static readonly double SharpCos = 0.7071067811865476;

        /// <summary>
        /// The terms of the estimate's offset band, from the welded components of Analyze: the area and boundary length of the open
        /// components, the area and sharp edges of the thin closed ones (volume by the divergence theorem about a vertex of the
        /// component; only for a consistently oriented component: where two triangles share an edge in the same direction the volume is not one and
        /// the component is not thin, and the sharp test is on |dot| of the normals so that it does not depend on the orientation either), the sharp edges of the rest.
        /// An estimate: folds, non-manifold edges and inverted parts are counted as they come.
        /// </summary>
        public static EstimateTerms Terms(List<Vector3d> tris, double alpha)
        {
            var terms = new EstimateTerms();
            if (!PrepareFrame(tris, alpha, out List<Vector3d> work, out double workAlpha, out int scaleExp, out bool exactKeys))
                return terms;
            Analyze(work, workAlpha, exactKeys, out int[] corner, out int[] comp, out double[] opening, out Vector3d[] pos, out Dictionary<(int, int), int> edgeUse);
            int n = work.Count / 3;
            // (per component root, in vertex ids: arrays, so that the sums come in a fixed order)
            int nv = pos.Length;
            var area = new double[nv];
            var volume = new double[nv];
            var hasReference = new bool[nv];
            var reference = new Vector3d[nv];
            var sharp = new double[nv];
            var firstUse = new Dictionary<(int, int), (Vector3d normal, bool forward)>();
            var inconsistent = new bool[nv];
            for (int t = 0; t < n; ++t) {
                int root = Find(comp, corner[3 * t]);
                Vector3d a = work[3 * t], b = work[3 * t + 1], c = work[3 * t + 2];
                if (!hasReference[root]) { hasReference[root] = true; reference[root] = a; }
                Vector3d cross = (b - a).Cross(c - a);
                double len = SafeLength(cross);
                area[root] += 0.5 * len;
                Vector3d r = reference[root];
                volume[root] += (a - r).Dot((b - r).Cross(c - r)) / 6.0;
                if (!SafeUnit(cross, out Vector3d normal)) continue;
                for (int k = 0; k < 3; ++k) {
                    int u = corner[3 * t + k], v = corner[3 * t + (k + 1) % 3];
                    if (u == v) continue;
                    var key = u < v ? (u, v) : (v, u);
                    if (edgeUse.TryGetValue(key, out int uses) && uses == 2) {
                        bool forward = u < v;
                        if (firstUse.TryGetValue(key, out var first)) {
                            // a consistently oriented surface traverses a shared edge in opposite directions; the sharp test is on the angle
                            // between the planes (|dot|), whatever the orientation of the two triangles
                            if (first.forward == forward) inconsistent[root] = true;
                            if (Math.Abs(first.normal.Dot(normal)) < SharpCos)
                                sharp[root] += SafeLength(pos[u] - pos[v]);
                            firstUse.Remove(key);
                        } else {
                            firstUse[key] = (normal, forward);
                        }
                    }
                }
            }
            foreach (var kv in edgeUse)
                if (kv.Value == 1) {
                    int root = Find(comp, kv.Key.Item1);
                    if (IsOpenWidth(opening[root], workAlpha, alpha, scaleExp))
                        terms.OpenBoundary += SafeLength(pos[kv.Key.Item1] - pos[kv.Key.Item2]);
                }
            for (int root = 0; root < nv; ++root) {
                if (!hasReference[root]) continue;
                if (IsOpenWidth(opening[root], workAlpha, alpha, scaleExp)) {
                    terms.OpenArea += area[root];
                } else if (!inconsistent[root] && IsThinThickness(Math.Abs(volume[root]), area[root], workAlpha, alpha, scaleExp)) {
                    terms.ThinArea += area[root];
                    terms.ThinRim += Math.Max(0.5 * sharp[root], 2 * Math.Sqrt(Math.PI * 0.5 * area[root]));
                } else {
                    terms.ThickSharp += sharp[root];
                }
            }
            if (scaleExp != 0) {
                terms.OpenArea = ExactPoint3.ScaleB(terms.OpenArea, 2 * scaleExp);
                terms.ThinArea = ExactPoint3.ScaleB(terms.ThinArea, 2 * scaleExp);
                terms.OpenBoundary = ExactPoint3.ScaleB(terms.OpenBoundary, scaleExp);
                terms.ThinRim = ExactPoint3.ScaleB(terms.ThinRim, scaleExp);
                terms.ThickSharp = ExactPoint3.ScaleB(terms.ThickSharp, scaleExp);
            }
            return terms;
        }

        /// <summary> the largest opening of the input: the largest bounding box diagonal of a chain of boundary edges (0 if there are none), welded with the cells of alpha </summary>
        public static double LargestOpening(List<Vector3d> tris, double alpha)
        {
            if (!PrepareFrame(tris, alpha, out List<Vector3d> work, out double workAlpha, out int scaleExp, out bool exactKeys))
                return 0;
            Analyze(work, workAlpha, exactKeys, out _, out _, out double[] opening, out _, out _);
            double best = opening.Length == 0 ? 0 : opening.Max();
            return scaleExp == 0 ? best : ExactPoint3.ScaleB(best, scaleExp);
        }

        // corner: the welded vertex of each corner; comp: the union-find of the vertices by triangle; opening: by component root, the
        // largest diagonal of its boundary chains
        static void Analyze(List<Vector3d> tris, double alpha, bool exactKeys, out int[] corner, out int[] comp, out double[] opening, out Vector3d[] pos, out Dictionary<(int, int), int> edgeUse)
        {
            int n = tris.Count / 3;
            double cell = alpha / WeldCells;
            var ids = new Dictionary<(long, long, long), int>(Math.Min(tris.Count, 1 << 20));
            corner = new int[tris.Count];
            if (exactKeys) {
                for (int i = 0; i < tris.Count; ++i) {
                    Vector3d p = tris[i];
                    var key = (NormBits(p.x), NormBits(p.y), NormBits(p.z));
                    if (!ids.TryGetValue(key, out int id)) { id = ids.Count; ids.Add(key, id); }
                    corner[i] = id;
                }
            } else {
                for (int i = 0; i < tris.Count; ++i) {
                    Vector3d p = tris[i];
                    var key = (Cell(p.x, cell), Cell(p.y, cell), Cell(p.z, cell));
                    if (!ids.TryGetValue(key, out int id)) { id = ids.Count; ids.Add(key, id); }
                    corner[i] = id;
                }
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
                    opening[c] = Math.Max(opening[c], SafeLength(hi[v] - lo[v]));
                }
        }

        // (called only when coordinates / cell stay within SafeGridLimit = 2^52; saturates cleanly otherwise)
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
