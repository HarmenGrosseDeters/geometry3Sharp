// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Marching tetrahedra for a wall around a level set: the solid {|g| &lt;= w} of a field g, optionally cut by a domain
    /// {d &lt;= 0}, on a uniform grid whose cubes are split into five tetrahedra. Made for walls thinner than a grid cell
    /// (sheet lattices, shells), which marching cubes on the single field |g| - w loses or breaks up.
    ///
    /// The two sides of the wall are contoured as two levels of g, g = +w and g = -w, not as one level of |g| - w. With g, w
    /// and d interpolated linearly in each tetrahedron, the result is the exact boundary of the solid
    /// {g - w &lt;= 0, -g - w &lt;= 0, d &lt;= 0} of those piecewise linear functions: in a tetrahedron the two sides are two
    /// planes that cannot meet (their functions add up to -2 w &lt; 0), so the wall keeps both sides at any thickness above
    /// zero, and the pieces of different tetrahedra have disjoint interiors, so the mesh has no self-intersections by
    /// construction. Where the solid stays inside Bounds the mesh is closed. It uses the values at the grid nodes only: one
    /// evaluation of Field (and of HalfWidthF and Domain) per node, as marching cubes with linear edges.
    ///
    /// What it does not do: it sees only what the grid resolves of g itself (a wall of g thinner than a cell is fine, a
    /// feature of g smaller than a cell is not); it gives no topology guarantee against the true level sets; and the
    /// triangles of a wall cut close to a grid node or to another plane can be slivers (see SliverGuard; check the result
    /// with MeshRobustRepair.NotCleanReason where a clean mesh is required). About 2.5 times the triangles of marching
    /// cubes on the same grid (five tetrahedra per cube).
    ///
    /// From Baktash, Gillespie, Crane, "Subgrid Marching Tetrahedra" (2026): several crossings per grid edge; here
    /// restricted to the crossings of linear interpolants, which need no case analysis beyond clipping convex polygons.
    /// See docs/guides/contouring-and-offsets.md and docs/design/TPMS_LATTICE.md section 10. Reusable: Generate recomputes everything.
    /// </summary>
    public class TwoLevelMarchingTetrahedra
    {
        /// <summary> the field g; the wall is around its zero level set </summary>
        public ImplicitFunction3d Field;
        /// <summary> w: the solid is {|g| &lt;= w}, in units of g (for a distance field, half the wall thickness). Finite and positive. Not used if HalfWidthF is set. </summary>
        public double HalfWidth = 0;
        /// <summary> optional w as a function of the position (positive, finite); evaluated once per grid node. Must be thread-safe if ParallelCompute. </summary>
        public Func<Vector3d, double> HalfWidthF = null;
        /// <summary> optional domain: the solid is cut to {d &lt;= 0} (eg a signed distance, negative inside) </summary>
        public ImplicitFunction3d Domain = null;

        /// <summary> region to contour; the mesh is open where the solid reaches its border, so keep a margin of a cell or cut with Domain </summary>
        public AxisAlignedBox3d Bounds;
        /// <summary> edge length of the grid cubes; the grid nodes are Bounds.Min + CellSize * (i, j, k) </summary>
        public double CellSize = 0.1;

        /// <summary>
        /// A node value within this fraction of the mean change of its function over a crossing grid edge counts as inside
        /// by that margin (one-sided, the solid only grows), so that a plane does not pass through a grid node and make
        /// zero-area triangles. It also thickens a wall thinner than that margin at such nodes. 0 switches it off.
        /// </summary>
        public double SliverGuard = 1.0 / 256;

        /// <summary>
        /// With a Domain: where the rim (a side of the wall meeting the cut of the domain) passes next to a grid edge, the
        /// boundary gets edges far shorter than a cell and triangles of almost no area. Where the crossings of the domain and
        /// of a side of the wall on a grid edge are closer than this fraction of the edge, the value of the Domain function at
        /// one end of the edge is changed so that they are 1.5 times this fraction apart. The cut of the domain moves by about
        /// that fraction of a cell there; the wall is not touched, and the result is still the exact boundary of a piecewise
        /// linear solid. 0 switches it off.
        /// </summary>
        public double RimGuard = 1.0 / 512;

        /// <summary> evaluate the fields in parallel (slabs of the grid); the result is the same as serial, bit for bit </summary>
        public bool ParallelCompute = true;
        public Func<bool> CancelF = () => { return false; };
        /// <summary> largest grid (nodes); a larger one throws an ArgumentException before any allocation </summary>
        public long MaxGridNodes = 1L << 28;

        // outputs
        /// <summary> the result; null after a cancelled Generate </summary>
        public DMesh3 Mesh;
        /// <summary> cubes per axis of the last Generate </summary>
        public Vector3i CellDimensions;
        /// <summary> triangles left out because they would have made a non-manifold edge (0 except for degenerate node values with SliverGuard 0) </summary>
        public int NonManifoldTriangles;
        /// <summary> node values of the Domain function that RimGuard changed </summary>
        public int RimNodesMoved;

        // the three constraint functions at the grid nodes, negative inside: g - w, -g - w, d
        double[][] f;
        int nc;                     // 2, or 3 with a domain
        int n1x, n1y, n1z;
        Vector3d origin; double h;
        double[] floor;             // per function: the smallest magnitude SliverGuard leaves at a node
        Dictionary<ValueTuple<int, int, int>, int> edgeVertices;
        Dictionary<ValueTuple<int, int, int, int>, int> faceVertices;

        static readonly int[][] EvenTets = { new[] { 0, 3, 5, 6 }, new[] { 1, 0, 3, 5 }, new[] { 2, 0, 3, 6 }, new[] { 4, 0, 5, 6 }, new[] { 7, 3, 5, 6 } };
        static readonly int[][] OddTets = { new[] { 1, 2, 4, 7 }, new[] { 0, 1, 2, 4 }, new[] { 3, 1, 2, 7 }, new[] { 5, 1, 4, 7 }, new[] { 6, 2, 4, 7 } };

        public void Generate()
        {
            Mesh = null; NonManifoldTriangles = 0; RimNodesMoved = 0; CellDimensions = Vector3i.Zero;
            if (Field == null) throw new ArgumentException("TwoLevelMarchingTetrahedra: Field is null");
            if (!(CellSize > 0) || double.IsInfinity(CellSize)) throw new ArgumentException("TwoLevelMarchingTetrahedra: CellSize must be finite and positive");
            if (HalfWidthF == null && (!(HalfWidth > 0) || double.IsInfinity(HalfWidth))) throw new ArgumentException("TwoLevelMarchingTetrahedra: HalfWidth must be finite and positive");
            if (!(RimGuard >= 0 && RimGuard < 0.1)) throw new ArgumentException("TwoLevelMarchingTetrahedra: RimGuard must be in [0, 0.1)");
            if (!(SliverGuard >= 0 && SliverGuard < 0.25)) throw new ArgumentException("TwoLevelMarchingTetrahedra: SliverGuard must be in [0, 0.25)");
            Vector3d size = Bounds.Max - Bounds.Min;
            if (!(size.x >= 0 && size.y >= 0 && size.z >= 0) || double.IsInfinity(size.x + size.y + size.z) || double.IsNaN(Bounds.Min.x + Bounds.Min.y + Bounds.Min.z)
                || double.IsInfinity(Bounds.Min.x + Bounds.Min.y + Bounds.Min.z))
                throw new ArgumentException("TwoLevelMarchingTetrahedra: Bounds must be finite and not empty");
            h = CellSize; origin = Bounds.Min;
            double cx = Math.Max(1, Math.Ceiling(size.x / h)), cy = Math.Max(1, Math.Ceiling(size.y / h)), cz = Math.Max(1, Math.Ceiling(size.z / h));
            if (!((cx + 1) * (cy + 1) * (cz + 1) <= Math.Min(MaxGridNodes, int.MaxValue)))
                throw new ArgumentException("TwoLevelMarchingTetrahedra: grid of " + (cx + 1) + " x " + (cy + 1) + " x " + (cz + 1) + " nodes is too large");
            CellDimensions = new Vector3i((int)cx, (int)cy, (int)cz);
            n1x = CellDimensions.x + 1; n1y = CellDimensions.y + 1; n1z = CellDimensions.z + 1;

            if (!Sample()) return;
            floor = new double[nc];
            for (int c = 0; c < nc; ++c) floor[c] = Guard(f[c]);
            if (nc == 3) SeparateRim();

            var mesh = new DMesh3();
            edgeVertices = new Dictionary<ValueTuple<int, int, int>, int>();
            faceVertices = new Dictionary<ValueTuple<int, int, int, int>, int>();
            var corner = new int[8]; var tet = new int[4];
            for (int k = 0; k < CellDimensions.z; ++k) {
                if (CancelF()) { Release(); return; }
                for (int j = 0; j < CellDimensions.y; ++j)
                    for (int i = 0; i < CellDimensions.x; ++i) {
                        int id = i + n1x * (j + n1y * k);
                        corner[0] = id; corner[1] = id + 1; corner[2] = id + n1x; corner[3] = id + n1x + 1;
                        for (int q = 0; q < 4; ++q) corner[4 + q] = corner[q] + n1x * n1y;
                        if (Uniform(corner)) continue;
                        int[][] split = ((i + j + k) & 1) == 0 ? EvenTets : OddTets;
                        for (int t = 0; t < 5; ++t) {
                            for (int q = 0; q < 4; ++q) tet[q] = corner[split[t][q]];
                            MeshTet(mesh, tet);
                        }
                    }
            }
            Release();
            Mesh = mesh;
        }

        void Release() { f = null; edgeVertices = null; faceVertices = null; }

        Vector3d NodePos(int id)
        {
            int i = id % n1x, j = id / n1x % n1y, k = id / (n1x * n1y);
            return new Vector3d(origin.x + h * i, origin.y + h * j, origin.z + h * k);
        }

        // the node values of g - w, -g - w and d; false if cancelled. Throws on a value that is not finite.
        bool Sample()
        {
            nc = Domain != null ? 3 : 2;
            int nodes = n1x * n1y * n1z;
            f = new double[nc][];
            for (int c = 0; c < nc; ++c) f[c] = new double[nodes];
            bool bad = false, cancelled = false;
            Action<int> slab = k => {
                if (cancelled || bad) return;
                if (CancelF()) { cancelled = true; return; }
                for (int j = 0; j < n1y; ++j)
                    for (int i = 0; i < n1x; ++i) {
                        int id = i + n1x * (j + n1y * k);
                        Vector3d p = new Vector3d(origin.x + h * i, origin.y + h * j, origin.z + h * k);
                        double g = Field.Value(ref p);
                        double w = HalfWidthF != null ? HalfWidthF(p) : HalfWidth;
                        double d = nc == 3 ? Domain.Value(ref p) : -1;
                        if (double.IsNaN(g) || double.IsInfinity(g) || !(w > 0) || double.IsInfinity(w) || double.IsNaN(d) || double.IsInfinity(d)) { bad = true; return; }
                        f[0][id] = g - w; f[1][id] = -g - w;
                        if (nc == 3) f[2][id] = d;
                    }
            };
            if (ParallelCompute) gParallel.ForEach(Interval1i.Range(n1z), slab);
            else for (int k = 0; k < n1z; ++k) slab(k);
            if (bad) { Release(); throw new InvalidOperationException("TwoLevelMarchingTetrahedra: a field value is not finite, or the half width is not positive, at a grid node"); }
            if (cancelled) { Release(); return false; }
            return true;
        }

        // SliverGuard: serial and in a fixed order, so the same with and without ParallelCompute
        double Guard(double[] v)
        {
            if (SliverGuard <= 0) return 0;
            double sum = 0; long count = 0;
            int[] step = { 1, n1x, n1x * n1y };
            for (int k = 0; k < n1z; ++k)
                for (int j = 0; j < n1y; ++j)
                    for (int i = 0; i < n1x; ++i) {
                        int id = i + n1x * (j + n1y * k);
                        for (int a = 0; a < 3; ++a) {
                            if ((a == 0 ? i + 1 : a == 1 ? j + 1 : k + 1) >= (a == 0 ? n1x : a == 1 ? n1y : n1z)) continue;
                            double x = v[id], y = v[id + step[a]];
                            if ((x < 0) != (y < 0)) { sum += Math.Abs(x - y); ++count; }
                        }
                    }
            if (count == 0) return 0;
            double snap = SliverGuard * (sum / count);
            for (int id = 0; id < v.Length; ++id)
                if (Math.Abs(v[id]) < snap) v[id] = -snap;
            return snap;
        }

        static readonly int[][] TetEdges = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 3 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 2, 3 } };

        // RimGuard: serial, in the order of the cubes, a few rounds (a changed node moves the crossings of its other edges a little)
        void SeparateRim()
        {
            if (RimGuard <= 0) return;
            double tau = RimGuard;
            double[] d = f[2];
            var corner = new int[8];
            for (int round = 0; round < 8; ++round) {
                int moved = 0;
                for (int k = 0; k < CellDimensions.z; ++k)
                    for (int j = 0; j < CellDimensions.y; ++j)
                        for (int i = 0; i < CellDimensions.x; ++i) {
                            int id = i + n1x * (j + n1y * k);
                            corner[0] = id; corner[1] = id + 1; corner[2] = id + n1x; corner[3] = id + n1x + 1;
                            for (int q = 0; q < 4; ++q) corner[4 + q] = corner[q] + n1x * n1y;
                            if (Uniform(corner)) continue;
                            int[][] split = ((i + j + k) & 1) == 0 ? EvenTets : OddTets;
                            for (int t = 0; t < 5; ++t)
                                foreach (int[] e in TetEdges) {
                                    int a = Math.Min(corner[split[t][e[0]]], corner[split[t][e[1]]]), b = Math.Max(corner[split[t][e[0]]], corner[split[t][e[1]]]);
                                    if ((d[a] < 0) == (d[b] < 0)) continue;
                                    for (int c = 0; c < 2; ++c) {
                                        if ((f[c][a] < 0) == (f[c][b] < 0)) continue;
                                        double tc = Crossing(c, a, b), td = Crossing(2, a, b);
                                        if (Math.Abs(tc - td) >= tau) continue;
                                        if (MoveCrossing(a, b, tc + (td >= tc ? 1.5 : -1.5) * tau) || MoveCrossing(a, b, tc - (td >= tc ? 1.5 : -1.5) * tau)) ++moved;
                                    }
                                }
                        }
                RimNodesMoved += moved;
                if (moved == 0) break;
            }
        }

        // Change the Domain value at one end of the edge (a, b) so that its crossing is at the parameter t: the end that changes
        // less, if its value keeps its sign and stays above the floor of the SliverGuard. False if neither end can.
        bool MoveCrossing(int a, int b, double t)
        {
            if (!(t > RimGuard && t < 1 - RimGuard)) return false;
            double[] d = f[2];
            double newA = t * d[b] / (t - 1), newB = d[a] * (t - 1) / t;
            bool okA = (newA < 0) == (d[a] < 0) && Math.Abs(newA) >= floor[2] && newA != 0, okB = (newB < 0) == (d[b] < 0) && Math.Abs(newB) >= floor[2] && newB != 0;
            if (okA && (!okB || Math.Abs(newA - d[a]) <= Math.Abs(newB - d[b]))) { d[a] = newA; return true; }
            if (okB) { d[b] = newB; return true; }
            return false;
        }

        // no function changes sign on the cube, or one is outside at all its corners: nothing to do
        bool Uniform(int[] corner)
        {
            bool crossing = false;
            for (int c = 0; c < nc; ++c) {
                double[] v = f[c];
                int inside = 0;
                for (int q = 0; q < 8; ++q) if (v[corner[q]] < 0) ++inside;
                if (inside == 0) return true;
                if (inside != 8) crossing = true;
            }
            return !crossing;
        }

        // ---- one tetrahedron: per function the polygon of its zero plane, cut by the other functions ----

        readonly int[] polyA = new int[4], polyB = new int[4];      // the tetrahedron edges (node ids, A < B) of the polygon's vertices
        readonly bool[,] negative = new bool[4, 3];
        readonly List<int> emitted = new List<int>(10);

        void MeshTet(DMesh3 mesh, int[] tet)
        {
            // the tetrahedron is outside if a function is not negative at all four nodes
            for (int c = 0; c < nc; ++c) {
                double[] v = f[c];
                if (!(v[tet[0]] < 0) && !(v[tet[1]] < 0) && !(v[tet[2]] < 0) && !(v[tet[3]] < 0)) return;
            }
            for (int c = 0; c < nc; ++c) {
                double[] v = f[c];
                int mask = (v[tet[0]] < 0 ? 1 : 0) | (v[tet[1]] < 0 ? 2 : 0) | (v[tet[2]] < 0 ? 4 : 0) | (v[tet[3]] < 0 ? 8 : 0);
                if (mask == 15) continue;
                int m = Polygon(tet, mask);
                // which side of the other functions each vertex of the polygon is on
                bool[] inside = new bool[m];
                for (int p = 0; p < m; ++p) {
                    inside[p] = true;
                    for (int o = 0; o < nc; ++o) {
                        if (o == c) continue;
                        negative[p, o] = NegativeAt(o, c, polyA[p], polyB[p]);
                        if (!negative[p, o]) inside[p] = false;
                    }
                }
                emitted.Clear();
                for (int p = 0; p < m; ++p) {
                    int q = (p + 1) % m;
                    if (inside[p]) emitted.Add(EdgeVertex(mesh, c, polyA[p], polyB[p]));
                    int enter = -1, exit = -1; bool valid = true;
                    for (int o = 0; o < nc; ++o) {
                        if (o == c) continue;
                        if (negative[p, o] == negative[q, o]) { if (!negative[p, o]) valid = false; continue; }
                        if (negative[p, o]) { if (exit >= 0) valid = false; exit = o; }
                        else { if (enter >= 0) valid = false; enter = o; }
                    }
                    if (inside[p]) { if (exit >= 0) emitted.Add(FaceVertex(mesh, c, exit, p, q)); continue; }
                    if (!valid || enter < 0) continue;
                    emitted.Add(FaceVertex(mesh, c, enter, p, q));
                    if (exit >= 0) emitted.Add(FaceVertex(mesh, c, exit, p, q));
                }
                if (emitted.Count < 3) continue;
                // orientation: the normal points to where the function grows (out of the solid)
                Vector3d p0 = EdgePoint(c, polyA[0], polyB[0]), p1 = EdgePoint(c, polyA[1], polyB[1]), p2 = EdgePoint(c, polyA[2], polyB[2]);
                int lo = -1, hi = -1;
                for (int q = 0; q < 4; ++q) { if (f[c][tet[q]] < 0) lo = tet[q]; else hi = tet[q]; }
                bool flip = (p1 - p0).Cross(p2 - p0).Dot(NodePos(hi) - NodePos(lo)) < 0;
                for (int t = 1; t + 1 < emitted.Count; ++t) {
                    int a = emitted[0], b = emitted[flip ? t + 1 : t], d = emitted[flip ? t : t + 1];
                    if (a == b || b == d || a == d) continue;
                    if (mesh.AppendTriangle(a, b, d) < 0) ++NonManifoldTriangles;
                }
            }
        }

        // The polygon of a zero plane in the tetrahedron: its vertices are on the edges between a negative and a non-negative
        // node, in an order in which neighbours lie in one face of the tetrahedron. Returns 3 or 4.
        int Polygon(int[] tet, int mask)
        {
            int count = (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1) + ((mask >> 3) & 1);
            if (count == 2) {
                int a = -1, b = -1, c = -1, d = -1;
                for (int q = 0; q < 4; ++q) {
                    if (((mask >> q) & 1) != 0) { if (a < 0) a = q; else b = q; }
                    else { if (c < 0) c = q; else d = q; }
                }
                SetEdge(0, tet[a], tet[c]); SetEdge(1, tet[a], tet[d]); SetEdge(2, tet[b], tet[d]); SetEdge(3, tet[b], tet[c]);
                return 4;
            }
            int alone = 0;
            for (int q = 0; q < 4; ++q) if ((((mask >> q) & 1) != 0) == (count == 1)) alone = q;
            int n = 0;
            for (int q = 0; q < 4; ++q) if (q != alone) SetEdge(n++, tet[alone], tet[q]);
            return 3;
        }

        void SetEdge(int slot, int x, int y) { polyA[slot] = Math.Min(x, y); polyB[slot] = Math.Max(x, y); }

        // the crossing of function c on the edge (a, b), a < b, as a parameter from a
        double Crossing(int c, int a, int b) { double x = f[c][a], y = f[c][b]; return x / (x - y); }

        // Is function o negative at the crossing of function c on the edge (a, b)? Decided by the order of the two crossings
        // on the edge only, with a fixed tie rule, so that every tetrahedron and every polygon at this edge sees one arrangement.
        bool NegativeAt(int o, int c, int a, int b)
        {
            bool na = f[o][a] < 0, nb = f[o][b] < 0;
            if (na == nb) return na;
            double tc = Crossing(c, a, b), to = Crossing(o, a, b);
            bool cFirst;
            if (tc != to) cFirst = tc < to;
            else {
                // a tie: a function that becomes negative along the edge comes before one that stops being negative; then by index
                bool cEnters = !(f[c][a] < 0), oEnters = !na;
                cFirst = cEnters != oEnters ? cEnters : c < o;
            }
            return cFirst ? na : nb;
        }

        Vector3d EdgePoint(int c, int a, int b)
        {
            Vector3d pa = NodePos(a), pb = NodePos(b);
            return pa + Crossing(c, a, b) * (pb - pa);
        }

        int EdgeVertex(DMesh3 mesh, int c, int a, int b)
        {
            var key = new ValueTuple<int, int, int>(a, b, c);
            int vid;
            if (edgeVertices.TryGetValue(key, out vid)) return vid;
            vid = mesh.AppendVertex(EdgePoint(c, a, b));
            edgeVertices[key] = vid;
            return vid;
        }

        // the point of the face through the polygon vertices p and q where the functions c and o are both zero
        int FaceVertex(DMesh3 mesh, int c, int o, int p, int q)
        {
            int n0 = polyA[p], n1 = polyB[p], n2 = polyA[q] != n0 && polyA[q] != n1 ? polyA[q] : polyB[q];
            if (n2 < n1) { int s = n1; n1 = n2; n2 = s; }
            if (n1 < n0) { int s = n0; n0 = n1; n1 = s; }
            int lowF = Math.Min(c, o), highF = Math.Max(c, o);
            var key = new ValueTuple<int, int, int, int>(n0, n1, n2, lowF * 3 + highF);
            int vid;
            if (faceVertices.TryGetValue(key, out vid)) return vid;
            // barycentric coordinates: orthogonal to the values of both functions at the three nodes
            var u = new Vector3d(f[lowF][n0], f[lowF][n1], f[lowF][n2]);
            var v = new Vector3d(f[highF][n0], f[highF][n1], f[highF][n2]);
            Vector3d w = u.Cross(v);
            double sum = w.x + w.y + w.z;
            if (sum < 0) { w = -w; sum = -sum; }
            w.x = Math.Max(w.x, 0); w.y = Math.Max(w.y, 0); w.z = Math.Max(w.z, 0);
            sum = w.x + w.y + w.z;
            Vector3d pos;
            if (!(sum > 0)) pos = (NodePos(n0) + NodePos(n1) + NodePos(n2)) / 3;
            else {
                Vector3d a = NodePos(n0);
                pos = a + (w.y / sum) * (NodePos(n1) - a) + (w.z / sum) * (NodePos(n2) - a);
            }
            vid = mesh.AppendVertex(pos);
            faceVertices[key] = vid;
            return vid;
        }
    }
}
