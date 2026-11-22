// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact ray-plus-boundary generalized winding number cache for <see cref="DMeshAABBTree3"/>
    /// (C. Martens, P. Trettner, M. Bessmeltsev, "The Antipodal Method: Fast, Accurate, and Robust 3D Generalized
    /// Winding Numbers", ACM TOG 45(4), Art. 43, 2026; P. Xie, C. Hafner, C. Wojtan, "Fast and Exact Winding Numbers
    /// for Triangle Meshes", ACM TOG 45(4), Art. 41, 2026; implemented from the published papers, no external code
    /// copied).
    ///
    /// For any triangle mesh M with exterior edges ∂M (directed edges with nonzero net oriented count) and a query
    /// point p, the generalized winding number decomposes as
    ///     w_M(p) = chi(p, +x) + (1 / (2*pi)) * sum_{(a,b) in ∂M} weight(a,b) * atan2(num, den),
    /// where chi(p, +x) is the exact signed crossing count of the symbolically perturbed +x ray from
    /// p_eps = (p.x + eps3, p.y + eps1, p.z + eps2) (0 &lt; eps3 &lt;&lt; eps2 &lt;&lt; eps1) with M, and each exterior edge
    /// contributes half the signed solid angle of the triangle (a - p, b - p, (-1, 0, 0) at infinity).
    ///
    /// Coupling: the 2D edge determinant Orient2d((p.y,p.z), (a.y,a.z), (b.y,b.z)) with its exact symbolic
    /// perturbation is shared between chi and the branch cut (num = 0, den &lt;= 0) of atan2, so ray-edge and
    /// ray-vertex degeneracies cancel exactly without ±1 jumps.
    ///
    /// On closed meshes (|∂M| = 0, including unwelded closed soups whose coincident opposite edges cancel),
    /// the boundary term is empty and w_M(p) is an exact integer computed from a single O(log F) axis ray.
    /// On regular +x grid rows (MeshWindingNumberGrid), one BVH traversal along the (y, z) line supplies chi
    /// for every sample on the row. On true disconnected soups (|∂M| &gt;= F and F &gt; 64), evaluation falls back
    /// to the direct exact solid-angle sum over the packed triangles (fewer atan2 evaluations than both the
    /// boundary sum and hierarchical box fans).
    /// </summary>
    internal sealed class ExactWindingCache
    {
        struct FlatBox
        {
            public double MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
            // Internal node: Child1 >= 0, Child2 >= 0 (2 children) or Child2 == -1 (1 child).
            // Leaf node:     Child2 <= -2, triCount = -(Child2 + 2), triStart = Child1.
            public int Child1, Child2;
        }

        struct FlatTri
        {
            public Vector3d A, B, C;
        }

        internal struct BoundaryEdge
        {
            public Vector3d A, B;
            public int Weight;
        }

        internal struct LineHit
        {
            public int TriIndex;
            public double MinX, MaxX;
            public int Sign;
        }

        readonly FlatBox[] boxes;
        readonly FlatTri[] tris;
        readonly BoundaryEdge[] boundary;
        readonly int rootIndex;
        readonly int triCount;
        readonly bool useDirectSum;

        internal int BoundaryCount { get { return boundary.Length; } }
        internal int TriangleCount { get { return triCount; } }
        internal bool UseDirectSum { get { return useDirectSum; } }

        ExactWindingCache(FlatBox[] boxes, FlatTri[] tris, BoundaryEdge[] boundary, int rootIndex, int triCount, bool useDirectSum)
        {
            this.boxes = boxes;
            this.tris = tris;
            this.boundary = boundary;
            this.rootIndex = rootIndex;
            this.triCount = triCount;
            this.useDirectSum = useDirectSum;
        }

        /// <summary>
        /// Build the exact winding cache from the mesh and the already-built BVH arrays of DMeshAABBTree3.
        /// </summary>
        internal static ExactWindingCache Build(DMesh3 mesh, DVector<int> boxToIndex, DVector<int> indexList, int trianglesEnd, int rootIndex, int boxCount)
        {
            int meshTriCount = mesh.TriangleCount;
            if (meshTriCount == 0 || boxCount == 0 || rootIndex < 0)
                return new ExactWindingCache(Array.Empty<FlatBox>(), Array.Empty<FlatTri>(), Array.Empty<BoundaryEdge>(), -1, 0, false);

            // 1. Pack finite leaf triangles in BVH order and compute exact bottom-up double bounds per node.
            //    Also accumulate oriented exterior edges keyed by canonicalized 3D vertex coordinates so
            //    unwelded closed soups cancel opposite coincident edges to |∂M| = 0.
            var flatBoxes = new FlatBox[boxCount];
            var flatTris = new List<FlatTri>(meshTriCount);
            var edgeWeights = new Dictionary<EdgeKey, int>(meshTriCount);

            build_node(rootIndex, mesh, boxToIndex, indexList, trianglesEnd, flatBoxes, flatTris, edgeWeights);

            // 2. Collect nonzero exterior edges and sort deterministically by coordinates
            //    (strictly preserved under positive 2^k scaling).
            var edges = new List<BoundaryEdge>();
            foreach (var kv in edgeWeights) {
                int w = kv.Value;
                if (w == 0)
                    continue;
                EdgeKey k = kv.Key;
                if (w > 0)
                    edges.Add(new BoundaryEdge { A = k.Lo.ToVector(), B = k.Hi.ToVector(), Weight = w });
                else
                    edges.Add(new BoundaryEdge { A = k.Hi.ToVector(), B = k.Lo.ToVector(), Weight = -w });
            }
            if (edges.Count > 1)
                edges.Sort(compare_boundary_edges);

            FlatTri[] packedTris = flatTris.ToArray();
            BoundaryEdge[] packedEdges = edges.ToArray();
            bool direct = packedEdges.Length >= packedTris.Length && packedTris.Length > 64;
            return new ExactWindingCache(flatBoxes, packedTris, packedEdges, rootIndex, packedTris.Length, direct);
        }

        static void build_node(
            int iBox, DMesh3 mesh, DVector<int> boxToIndex, DVector<int> indexList, int trianglesEnd,
            FlatBox[] flatBoxes, List<FlatTri> flatTris, Dictionary<EdgeKey, int> edgeWeights)
        {
            int idx = boxToIndex[iBox];
            if (idx < trianglesEnd) {
                int n = indexList[idx];
                int start = flatTris.Count;
                double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
                double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                for (int i = 1; i <= n; ++i) {
                    int tid = indexList[idx + i];
                    mesh.GetTriVertices(tid, ref a, ref b, ref c);
                    if (!is_finite(a) || !is_finite(b) || !is_finite(c))
                        continue;
                    flatTris.Add(new FlatTri { A = a, B = b, C = c });
                    double tMinX = Math.Min(a.x, Math.Min(b.x, c.x));
                    double tMaxX = Math.Max(a.x, Math.Max(b.x, c.x));
                    double tMinY = Math.Min(a.y, Math.Min(b.y, c.y));
                    double tMaxY = Math.Max(a.y, Math.Max(b.y, c.y));
                    double tMinZ = Math.Min(a.z, Math.Min(b.z, c.z));
                    double tMaxZ = Math.Max(a.z, Math.Max(b.z, c.z));
                    if (tMinX < minX) minX = tMinX;
                    if (tMaxX > maxX) maxX = tMaxX;
                    if (tMinY < minY) minY = tMinY;
                    if (tMaxY > maxY) maxY = tMaxY;
                    if (tMinZ < minZ) minZ = tMinZ;
                    if (tMaxZ > maxZ) maxZ = tMaxZ;

                    add_edge(edgeWeights, a, b);
                    add_edge(edgeWeights, b, c);
                    add_edge(edgeWeights, c, a);
                }
                int count = flatTris.Count - start;
                flatBoxes[iBox] = new FlatBox {
                    MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, MinZ = minZ, MaxZ = maxZ,
                    Child1 = start,
                    Child2 = -(count + 2)
                };
                return;
            }

            int iChild1 = indexList[idx];
            if (iChild1 < 0) {
                iChild1 = (-iChild1) - 1;
                build_node(iChild1, mesh, boxToIndex, indexList, trianglesEnd, flatBoxes, flatTris, edgeWeights);
                FlatBox b1 = flatBoxes[iChild1];
                flatBoxes[iBox] = new FlatBox {
                    MinX = b1.MinX, MaxX = b1.MaxX, MinY = b1.MinY, MaxY = b1.MaxY, MinZ = b1.MinZ, MaxZ = b1.MaxZ,
                    Child1 = iChild1, Child2 = -1
                };
            } else {
                iChild1 = iChild1 - 1;
                int iChild2 = indexList[idx + 1] - 1;
                build_node(iChild1, mesh, boxToIndex, indexList, trianglesEnd, flatBoxes, flatTris, edgeWeights);
                build_node(iChild2, mesh, boxToIndex, indexList, trianglesEnd, flatBoxes, flatTris, edgeWeights);
                FlatBox b1 = flatBoxes[iChild1], b2 = flatBoxes[iChild2];
                flatBoxes[iBox] = new FlatBox {
                    MinX = Math.Min(b1.MinX, b2.MinX),
                    MaxX = Math.Max(b1.MaxX, b2.MaxX),
                    MinY = Math.Min(b1.MinY, b2.MinY),
                    MaxY = Math.Max(b1.MaxY, b2.MaxY),
                    MinZ = Math.Min(b1.MinZ, b2.MinZ),
                    MaxZ = Math.Max(b1.MaxZ, b2.MaxZ),
                    Child1 = iChild1, Child2 = iChild2
                };
            }
        }

        // Canonical exact point key (finite coordinates with -0.0 normalized to +0.0)
        readonly struct PointKey : IEquatable<PointKey>
        {
            public readonly double X, Y, Z;

            public PointKey(Vector3d v)
            {
                X = v.x == 0.0 ? 0.0 : v.x;
                Y = v.y == 0.0 ? 0.0 : v.y;
                Z = v.z == 0.0 ? 0.0 : v.z;
            }

            public Vector3d ToVector() { return new Vector3d(X, Y, Z); }

            public bool Equals(PointKey other) { return X == other.X && Y == other.Y && Z == other.Z; }
            public override bool Equals(object obj) { return obj is PointKey o && Equals(o); }
            public override int GetHashCode()
            {
                unchecked {
                    ulong bx = (ulong)BitConverter.DoubleToInt64Bits(X);
                    ulong by = (ulong)BitConverter.DoubleToInt64Bits(Y);
                    ulong bz = (ulong)BitConverter.DoubleToInt64Bits(Z);
                    ulong h = bx * 0x9E3779B97F4A7C15UL ^ by * 0xBF58476D1CE4E5B9UL ^ bz * 0x94D049BB133111EBUL;
                    h ^= h >> 30;
                    h *= 0xBF58476D1CE4E5B9UL;
                    h ^= h >> 27;
                    return (int)(h ^ (h >> 32));
                }
            }

            public static int Compare(PointKey a, PointKey b)
            {
                if (a.X != b.X) return a.X < b.X ? -1 : 1;
                if (a.Y != b.Y) return a.Y < b.Y ? -1 : 1;
                if (a.Z != b.Z) return a.Z < b.Z ? -1 : 1;
                return 0;
            }
        }

        readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly PointKey Lo, Hi;
            public EdgeKey(PointKey lo, PointKey hi) { Lo = lo; Hi = hi; }
            public bool Equals(EdgeKey other) { return Lo.Equals(other.Lo) && Hi.Equals(other.Hi); }
            public override bool Equals(object obj) { return obj is EdgeKey o && Equals(o); }
            public override int GetHashCode()
            {
                unchecked {
                    return (Lo.GetHashCode() * 397) ^ Hi.GetHashCode();
                }
            }
        }

        static void add_edge(Dictionary<EdgeKey, int> edgeWeights, Vector3d a, Vector3d b)
        {
            var ka = new PointKey(a);
            var kb = new PointKey(b);
            int cmp = PointKey.Compare(ka, kb);
            if (cmp == 0)
                return;
            EdgeKey key = cmp < 0 ? new EdgeKey(ka, kb) : new EdgeKey(kb, ka);
            int delta = cmp < 0 ? 1 : -1;
            int prev;
            if (edgeWeights.TryGetValue(key, out prev)) {
                int next = prev + delta;
                if (next == 0)
                    edgeWeights.Remove(key);
                else
                    edgeWeights[key] = next;
            } else {
                edgeWeights[key] = delta;
            }
        }

        static int compare_boundary_edges(BoundaryEdge u, BoundaryEdge v)
        {
            int c = u.A.x.CompareTo(v.A.x); if (c != 0) return c;
            c = u.A.y.CompareTo(v.A.y); if (c != 0) return c;
            c = u.A.z.CompareTo(v.A.z); if (c != 0) return c;
            c = u.B.x.CompareTo(v.B.x); if (c != 0) return c;
            c = u.B.y.CompareTo(v.B.y); if (c != 0) return c;
            return u.B.z.CompareTo(v.B.z);
        }

        // ---- Single-point evaluation ----

        /// <summary> Exact generalized winding number at point p. </summary>
        [MethodImpl(JitHints.Optimize)]
        internal double Evaluate(Vector3d p)
        {
            if (triCount == 0 || !is_finite(p))
                return 0.0;
            if (useDirectSum)
                return direct_sum(p);

            double surfaceCorrection = 0.0;
            int chi = signed_crossings_x(rootIndex, p.x, p.y, p.z, ref surfaceCorrection);
            double w = (double)chi;
            if (boundary.Length > 0)
                w += boundary_sum_x(p);
            if (surfaceCorrection != 0.0)
                w -= surfaceCorrection;
            return w;
        }

        double direct_sum(Vector3d p)
        {
            double sum = 0.0;
            FlatTri[] t = tris;
            for (int i = 0; i < t.Length; ++i)
                sum += safe_tri_solid_angle(t[i].A, t[i].B, t[i].C, p);
            return sum / MathUtil.FourPI;
        }

        // ---- Row-ray evaluation along +x (MeshWindingNumberGrid) ----

        /// <summary>
        /// Collect all exact perturbed +x line-triangle crossings and overlapping leaf box indices along the
        /// line (y, z) in one BVH pass. The caller supplies reusable lists to avoid per-row heap allocations.
        /// </summary>
        internal void CollectLineCrossingsX(double y, double z, List<LineHit> hits, List<int> rowLeaves)
        {
            hits.Clear();
            rowLeaves.Clear();
            if (triCount == 0 || useDirectSum || !is_finite(y) || !is_finite(z))
                return;
            collect_line_crossings_x(rootIndex, y, z, hits, rowLeaves);
        }

        /// <summary>
        /// Evaluate the exact winding number for every sample i in [0, ni) along the grid row
        /// x_i = originX + i * dx, (y, z) fixed, using a single BVH line traversal.
        /// Bit-for-bit identical to calling Evaluate(new Vector3d(i * dx + originX, y, z)) per sample.
        /// </summary>
        internal void EvaluateFullRowX(double originX, double dx, int ni, double y, double z, List<LineHit> hits, List<int> rowLeaves, int[] diffBuf, float[] outRow)
        {
            if (ni <= 0)
                return;
            if (triCount == 0 || !is_finite(y) || !is_finite(z)) {
                for (int i = 0; i < ni; ++i) outRow[i] = 0.0f;
                return;
            }
            if (!is_finite(originX) || !is_finite(dx)) {
                for (int i = 0; i < ni; ++i)
                    outRow[i] = (float)Evaluate(new Vector3d((double)i * dx + originX, y, z));
                return;
            }
            if (useDirectSum) {
                for (int i = 0; i < ni; ++i) {
                    double xi = (double)i * dx + originX;
                    outRow[i] = is_finite(xi) ? (float)direct_sum(new Vector3d(xi, y, z)) : 0.0f;
                }
                return;
            }

            CollectLineCrossingsX(y, z, hits, rowLeaves);

            double x0 = originX;
            double xLast = (double)(ni - 1) * dx + originX;
            if (dx <= 0.0 || !is_finite(xLast)) {
                for (int i = 0; i < ni; ++i)
                    outRow[i] = (float)EvaluateOnRowX((double)i * dx + originX, y, z, hits, rowLeaves);
                return;
            }

            Array.Clear(diffBuf, 0, ni);

            int hCount = hits.Count;
            for (int h = 0; h < hCount; ++h) {
                LineHit hit = hits[h];
                int s = hit.Sign;
                if (xLast < hit.MinX) {
                    // Every sample on the row lies strictly before the triangle: +s for all i in [0, ni).
                    diffBuf[0] += s;
                    continue;
                }
                if (x0 > hit.MaxX)
                    continue;

                // Find first index iMin with x_i >= hit.MinX, and last index iMax with x_i <= hit.MaxX.
                int iMin = first_index_ge(originX, dx, ni, hit.MinX);
                if (iMin > 0) {
                    diffBuf[0] += s;
                    if (iMin < ni)
                        diffBuf[iMin] -= s;
                }
                int iMax = last_index_le(originX, dx, ni, hit.MaxX);
                FlatTri tri = tris[hit.TriIndex];
                for (int i = iMin; i <= iMax; ++i) {
                    double xi = (double)i * dx + originX;
                    if (is_ahead_x(xi, y, z, tri.A, tri.B, tri.C, s)) {
                        diffBuf[i] += s;
                        if (i + 1 < ni)
                            diffBuf[i + 1] -= s;
                    }
                }
            }

            int chi = 0;
            bool hasBoundary = boundary.Length > 0;
            bool hasRowLeaves = rowLeaves.Count > 0;
            for (int i = 0; i < ni; ++i) {
                chi += diffBuf[i];
                double w = (double)chi;
                if (hasBoundary || hasRowLeaves) {
                    double xi = (double)i * dx + originX;
                    if (hasBoundary)
                        w += boundary_sum_x(new Vector3d(xi, y, z));
                    if (hasRowLeaves) {
                        double sc = row_surface_correction(xi, y, z, rowLeaves);
                        if (sc != 0.0)
                            w -= sc;
                    }
                }
                outRow[i] = (float)w;
            }
        }

        /// <summary>
        /// Evaluate the exact winding number at a single sample (x, y, z) on a row whose line hits
        /// and overlapping leaves were already collected with <see cref="CollectLineCrossingsX"/>.
        /// Bit-for-bit identical to Evaluate(new Vector3d(x, y, z)).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal double EvaluateOnRowX(double x, double y, double z, List<LineHit> hits, List<int> rowLeaves)
        {
            if (triCount == 0 || !is_finite(x) || !is_finite(y) || !is_finite(z))
                return 0.0;
            if (useDirectSum)
                return direct_sum(new Vector3d(x, y, z));

            int chi = 0;
            int hCount = hits.Count;
            for (int h = 0; h < hCount; ++h) {
                LineHit hit = hits[h];
                if (x < hit.MinX) {
                    chi += hit.Sign;
                } else if (x <= hit.MaxX) {
                    FlatTri tri = tris[hit.TriIndex];
                    if (is_ahead_x(x, y, z, tri.A, tri.B, tri.C, hit.Sign))
                        chi += hit.Sign;
                }
            }
            double w = (double)chi;
            if (boundary.Length > 0)
                w += boundary_sum_x(new Vector3d(x, y, z));
            if (rowLeaves.Count > 0) {
                double sc = row_surface_correction(x, y, z, rowLeaves);
                if (sc != 0.0)
                    w -= sc;
            }
            return w;
        }

        [MethodImpl(JitHints.Optimize)]
        double row_surface_correction(double px, double py, double pz, List<int> rowLeaves)
        {
            double sc = 0.0;
            int count = rowLeaves.Count;
            for (int r = 0; r < count; ++r) {
                ref FlatBox box = ref boxes[rowLeaves[r]];
                if (px < box.MinX || px > box.MaxX)
                    continue;
                int start = box.Child1;
                int end = start - (box.Child2 + 2);
                for (int i = start; i < end; ++i) {
                    ref FlatTri t = ref tris[i];
                    sc += tri_on_surface_correction(px, py, pz, t.A, t.B, t.C);
                }
            }
            return sc;
        }

        static int first_index_ge(double originX, double dx, int ni, double target)
        {
            int lo = 0, hi = ni;
            while (lo < hi) {
                int mid = lo + ((hi - lo) >> 1);
                if ((double)mid * dx + originX < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        static int last_index_le(double originX, double dx, int ni, double target)
        {
            int lo = -1, hi = ni - 1;
            while (lo < hi) {
                int mid = lo + ((hi - lo + 1) >> 1);
                if ((double)mid * dx + originX <= target)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        // ---- Exact +x BVH traversals ----

        [MethodImpl(JitHints.Optimize)]
        int signed_crossings_x(int iBox, double px, double py, double pz, ref double surfaceCorrection)
        {
            ref FlatBox box = ref boxes[iBox];
            if (py < box.MinY || py > box.MaxY || pz < box.MinZ || pz > box.MaxZ || px > box.MaxX)
                return 0;

            if (box.Child2 <= -2) {
                int start = box.Child1;
                int end = start - (box.Child2 + 2);
                bool mayContainP = px >= box.MinX;
                int sum = 0;
                for (int i = start; i < end; ++i) {
                    ref FlatTri t = ref tris[i];
                    int s = line_crossing_x(py, pz, t.A, t.B, t.C);
                    if (s != 0 && is_ahead_x(px, py, pz, t.A, t.B, t.C, s))
                        sum += s;
                    if (mayContainP)
                        surfaceCorrection += tri_on_surface_correction(px, py, pz, t.A, t.B, t.C);
                }
                return sum;
            }

            int total = signed_crossings_x(box.Child1, px, py, pz, ref surfaceCorrection);
            if (box.Child2 >= 0)
                total += signed_crossings_x(box.Child2, px, py, pz, ref surfaceCorrection);
            return total;
        }

        [MethodImpl(JitHints.Optimize)]
        void collect_line_crossings_x(int iBox, double py, double pz, List<LineHit> hits, List<int> rowLeaves)
        {
            ref FlatBox box = ref boxes[iBox];
            if (py < box.MinY || py > box.MaxY || pz < box.MinZ || pz > box.MaxZ)
                return;

            if (box.Child2 <= -2) {
                rowLeaves.Add(iBox);
                int start = box.Child1;
                int end = start - (box.Child2 + 2);
                for (int i = start; i < end; ++i) {
                    ref FlatTri t = ref tris[i];
                    int s = line_crossing_x(py, pz, t.A, t.B, t.C);
                    if (s != 0) {
                        double minX = Math.Min(t.A.x, Math.Min(t.B.x, t.C.x));
                        double maxX = Math.Max(t.A.x, Math.Max(t.B.x, t.C.x));
                        hits.Add(new LineHit { TriIndex = i, MinX = minX, MaxX = maxX, Sign = s });
                    }
                }
                return;
            }

            collect_line_crossings_x(box.Child1, py, pz, hits, rowLeaves);
            if (box.Child2 >= 0)
                collect_line_crossings_x(box.Child2, py, pz, hits, rowLeaves);
        }

        // ---- Exact on-surface correction ----
        // For any point p lying in a closed non-degenerate triangle T (vertex, edge, or face interior), the perturbed
        // ray-plus-boundary evaluation computes w_M(p_eps), which includes w_T(p_eps). Replacing w_T(p_eps) with
        // safe_tri_solid_angle(a, b, c, p) / 4pi matches the DMesh3.WindingNumber / FastWindingNumber solid-angle-sum
        // convention on vertices (e.g. 0.125 on cube corners), edges, and coplanar faces while keeping off-surface
        // queries decided purely by exact predicates.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double tri_on_surface_correction(double px, double py, double pz, Vector3d a, Vector3d b, Vector3d c)
        {
            if (px < a.x && px < b.x && px < c.x) return 0.0;
            if (px > a.x && px > b.x && px > c.x) return 0.0;
            if (py < a.y && py < b.y && py < c.y) return 0.0;
            if (py > a.y && py > b.y && py > c.y) return 0.0;
            if (pz < a.z && pz < b.z && pz < c.z) return 0.0;
            if (pz > a.z && pz > b.z && pz > c.z) return 0.0;

            Vector3d p = new Vector3d(px, py, pz);
            if (ExactPredicates.Orient3d(a, b, c, p) != 0)
                return 0.0;

            int ny = ExactPredicates.Orient2d(new Vector2d(a.z, a.x), new Vector2d(b.z, b.x), new Vector2d(c.z, c.x));
            int nz = ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y));
            int nx = ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z));

            int cls;
            if (nx != 0)
                cls = classify_point_in_tri_2d(py, pz, a.y, a.z, b.y, b.z, c.y, c.z, nx);
            else if (ny != 0)
                cls = classify_point_in_tri_2d(pz, px, a.z, a.x, b.z, b.x, c.z, c.x, ny);
            else if (nz != 0)
                cls = classify_point_in_tri_2d(px, py, a.x, a.y, b.x, b.y, c.x, c.y, nz);
            else
                return 0.0; // degenerate collinear triangle

            if (cls == 0)
                return 0.0;

            double own = safe_tri_solid_angle(a, b, c, p) / MathUtil.FourPI;
            if (cls == 2) {
                // Strictly inside triangle: p_eps lies infinitesimally on one side of the plane,
                // so T subtends an exact hemisphere (+0.5 on -N side, -0.5 on +N side).
                int side = ny != 0 ? ny : (nz != 0 ? nz : nx);
                return (side < 0 ? 0.5 : -0.5) - own;
            }

            // On an edge or vertex of T: exact perturbed winding of T at p_eps.
            int s = line_crossing_x(py, pz, a, b, c);
            int chiT = (s != 0 && is_ahead_x(px, py, pz, a, b, c, s)) ? s : 0;
            double bsum = boundary_edge_angle_x(p, a, b)
                        + boundary_edge_angle_x(p, b, c)
                        + boundary_edge_angle_x(p, c, a);
            return ((double)chiT + bsum / MathUtil.TwoPI) - own;
        }

        // Overflow-safe wrapper around MathUtil.TriSolidAngle: halves all four points by exact 0.5 when a
        // coordinate difference would overflow double.MaxValue (e.g. +1e308 - (-1e308)).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double safe_tri_solid_angle(Vector3d a, Vector3d b, Vector3d c, Vector3d p)
        {
            if (!is_finite(a - p) || !is_finite(b - p) || !is_finite(c - p)) {
                a *= 0.5; b *= 0.5; c *= 0.5; p *= 0.5;
            }
            return MathUtil.TriSolidAngle(a, b, c, ref p);
        }

        // Returns 0 = outside, 1 = on boundary (edge or vertex), 2 = strictly interior.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int classify_point_in_tri_2d(double pu, double pv, double au, double av, double bu, double bv, double cu, double cv, int areaSign)
        {
            Vector2d P = new Vector2d(pu, pv), A = new Vector2d(au, av), B = new Vector2d(bu, bv), C = new Vector2d(cu, cv);
            int s0 = ExactPredicates.Orient2d(A, B, P) * areaSign;
            if (s0 < 0) return 0;
            int s1 = ExactPredicates.Orient2d(B, C, P) * areaSign;
            if (s1 < 0) return 0;
            int s2 = ExactPredicates.Orient2d(C, A, P) * areaSign;
            if (s2 < 0) return 0;
            return (s0 > 0 && s1 > 0 && s2 > 0) ? 2 : 1;
        }

        // ---- Exact +x symbolic-perturbation predicates ----
        // Perturbation: p_eps = (p.x + eps3, p.y + eps1, p.z + eps2) with 0 < eps3 << eps2 << eps1.

        /// <summary>
        /// Exact perturbed sign of Orient2d((py + eps1, pz + eps2), (a.y, a.z), (b.y, b.z)).
        /// Strictly antisymmetric: EdgeSignX(py, pz, a, b) == -EdgeSignX(py, pz, b, a).
        /// Returns 0 iff a.y == b.y and a.z == b.z.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int EdgeSignX(double py, double pz, Vector3d a, Vector3d b)
        {
            int s = ExactPredicates.Orient2d(new Vector2d(py, pz), new Vector2d(a.y, a.z), new Vector2d(b.y, b.z));
            if (s != 0)
                return s;
            // Expansion of (a.y - (py + eps1))(b.z - (pz + eps2)) - (a.z - (pz + eps2))(b.y - (py + eps1)):
            //   coeff of eps1 = a.z - b.z
            //   coeff of eps2 = b.y - a.y
            if (a.z != b.z) return a.z > b.z ? 1 : -1;
            if (b.y != a.y) return b.y > a.y ? 1 : -1;
            return 0;
        }

        /// <summary>
        /// Exact signed crossing of the perturbed +x line through (py + eps1, pz + eps2) with triangle (a, b, c):
        /// +1 if it crosses with N.x &gt; 0, -1 if with N.x &lt; 0, 0 if it misses (or N.x == 0).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int line_crossing_x(double py, double pz, Vector3d a, Vector3d b, Vector3d c)
        {
            // Conservative exact reject: if py < minY then py + eps1 < minY; if py > maxY then py + eps1 > maxY.
            if (py < a.y && py < b.y && py < c.y) return 0;
            if (py > a.y && py > b.y && py > c.y) return 0;
            if (pz < a.z && pz < b.z && pz < c.z) return 0;
            if (pz > a.z && pz > b.z && pz > c.z) return 0;

            int s0 = EdgeSignX(py, pz, a, b);
            if (s0 == 0) return 0;
            if (EdgeSignX(py, pz, b, c) != s0) return 0;
            if (EdgeSignX(py, pz, c, a) != s0) return 0;
            return s0;
        }

        /// <summary>
        /// Exact test whether the crossing of the perturbed +x ray from p_eps = (px + eps3, py + eps1, pz + eps2)
        /// with triangle (a, b, c) (already known to have line crossing sign s = sign(N.x) != 0) lies ahead (t &gt; 0).
        /// Ahead iff Orient3d(a, b, c, p_eps) == -s.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool is_ahead_x(double px, double py, double pz, Vector3d a, Vector3d b, Vector3d c, int s)
        {
            // Fast exact half-space rejects before Orient3d:
            if (px < a.x && px < b.x && px < c.x) return true;
            if (px > a.x && px > b.x && px > c.x) return false;

            int so = ExactPredicates.Orient3d(a, b, c, new Vector3d(px, py, pz));
            if (so != 0)
                return so == -s;

            // p lies exactly in the plane of (a, b, c):
            // g(p_eps) = eps1 * N.y + eps2 * N.z + eps3 * N.x.
            int ny = ExactPredicates.Orient2d(new Vector2d(a.z, a.x), new Vector2d(b.z, b.x), new Vector2d(c.z, c.x));
            if (ny != 0) return ny == -s;
            int nz = ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y));
            if (nz != 0) return nz == -s;
            // N.y == 0 and N.z == 0, so sign(g(p_eps)) = sign(eps3 * N.x) = s != -s (behind p_eps).
            return false;
        }

        // ---- Coupled Antipodal boundary term for x1 = (-1, 0, 0) ----

        [MethodImpl(JitHints.Optimize)]
        double boundary_sum_x(Vector3d p)
        {
            double sum = 0.0;
            BoundaryEdge[] edges = boundary;
            for (int i = 0; i < edges.Length; ++i) {
                ref BoundaryEdge e = ref edges[i];
                sum += e.Weight * boundary_edge_angle_x(p, e.A, e.B);
            }
            return sum / MathUtil.TwoPI;
        }

        /// <summary>
        /// Half the signed solid angle of the spherical triangle (a - p, b - p, (-1, 0, 0)), coupled with
        /// EdgeSignX(p.y, p.z, a, b) at the branch cut (num = 0, den &lt;= 0) and exact limits at p_eps when p lies
        /// on vertex a, vertex b, the open segment (a, b), or behind a vertex along +x.
        /// Bi-homogeneous of degree (1, 1) in (A, B); handles extreme scales and mixed exponents across the full
        /// double range without overflow or underflow, and is bit-for-bit invariant under positive 2^k scaling.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double boundary_edge_angle_x(Vector3d p, Vector3d a, Vector3d b)
        {
            // Exact raw-coordinate checks BEFORE any rescaling so tiny nonzero y/z cannot underflow to 0.
            bool atA = a.x == p.x && a.y == p.y && a.z == p.z;
            bool atB = b.x == p.x && b.y == p.y && b.z == p.z;
            if (atA || atB) {
                if (atA && atB)
                    return 0.0;
                return atA ? on_vertex_angle_x(p, b, 1.0) : on_vertex_angle_x(p, a, -1.0);
            }

            bool aOnX = a.y == p.y && a.z == p.z;
            bool bOnX = b.y == p.y && b.z == p.z;
            if (aOnX || bOnX) {
                if (aOnX && bOnX)
                    return 0.0;
                if (aOnX) {
                    // A along -x coincides with apex (-1,0,0) -> zero solid angle.
                    if (a.x < p.x)
                        return 0.0;
                    // Ray passes directly through boundary vertex a (A along +x):
                    if (b.z == p.z && b.y > p.y)
                        return -Math.PI;
                    rescale_diff_2d(b.y, p.y, b.z, p.z, out double by2, out double bz2, out _);
                    return Math.Atan2(bz2, -by2);
                } else {
                    if (b.x < p.x)
                        return 0.0;
                    if (a.z == p.z && a.y > p.y)
                        return Math.PI;
                    rescale_diff_2d(a.y, p.y, a.z, p.z, out double ay2, out double az2, out _);
                    return Math.Atan2(-az2, -ay2);
                }
            }

            // Fast ordinary-range path: zero overflow and zero underflow whenever both vectors lie in
            // [2^-100, 2^100] and neither has extreme |yz| / |x| exponent disparity (< 2^-100).
            double ax = a.x - p.x, ay = a.y - p.y, az = a.z - p.z;
            double bx = b.x - p.x, by = b.y - p.y, bz = b.z - p.z;
            double mA = Math.Max(Math.Abs(ax), Math.Max(Math.Abs(ay), Math.Abs(az)));
            double mB = Math.Max(Math.Abs(bx), Math.Max(Math.Abs(by), Math.Abs(bz)));
            double yzA = Math.Max(Math.Abs(ay), Math.Abs(az));
            double yzB = Math.Max(Math.Abs(by), Math.Abs(bz));

            const double LowThresh = 7.8886090522101181e-31;   // 2^-100
            const double HighThresh = 1.2676506002282294e30;   // 2^100
            if (mA >= LowThresh && mA <= HighThresh && mB >= LowThresh && mB <= HighThresh &&
                yzA >= LowThresh * mA && yzB >= LowThresh * mB) {
                double yzA2 = ay * ay + az * az;
                double yzB2 = by * by + bz * bz;
                double la = Math.Sqrt(ax * ax + yzA2);
                double lb = Math.Sqrt(bx * bx + yzB2);
                double da = ax > 0.0 ? yzA2 / (la + ax) : la - ax;
                double db = bx > 0.0 ? yzB2 / (lb + bx) : lb - bx;
                double p1 = ay * bz;
                double p2 = az * by;
                double num = p2 - p1;
                double dotYZ = ay * by + az * bz;
                double den = da * db + dotYZ;
                double numErr = 1e-12 * (Math.Abs(p1) + Math.Abs(p2));
                double denErr = 1e-12 * (da * db + Math.Abs(ay * by) + Math.Abs(az * bz));
                return finish_edge_angle(p, a, b, num, den, numErr, denErr, dotYZ);
            }

            return boundary_edge_angle_x_extreme(p, a, b);
        }

        // Exact limit of boundary_edge_angle_x(p_eps, a, b) when p == a (sign = +1) or p == b (sign = -1).
        // Uses 2D yz-rescaling + exponent-tracked d-factor for mixed-exponent safety and the cancellation-free
        // identity lb - (x + y) = (z^2 - 2*x*y) / (lb + x + y) for x + y > 0.
        static double on_vertex_angle_x(Vector3d p, Vector3d other, double sign)
        {
            if (other.y == p.y && other.z == p.z)
                return 0.0;
            if (other.z == p.z && other.y > p.y) {
                if (other.x > p.x) return -sign * Math.PI;
                if (other.x == p.x) return -sign * Math.PI * 0.5;
                return 0.0;
            }
            rescale_diff_2d(other.y, p.y, other.z, p.z, out double ry, out double rz, out int shiftYZ);
            scaled_d_factor(other.x, p.x, ry, rz, shiftYZ, out double dVal, out int dExp);
            if (dExp > 100)
                return 0.0;
            double den0;
            if (dExp < -100) {
                den0 = -ry;
            } else if (dExp == 0) {
                // Ordinary |x| relative to |yz|: compute exact cancellation-free den0 = lr - (rx + ry).
                double dx = other.x - p.x;
                int extraX = 0;
                if (!is_finite(dx)) {
                    dx = other.x * 0.5 - p.x * 0.5;
                    extraX = 1;
                }
                double rx = 0.0;
                if (dx != 0.0) {
                    int rawE = (int)((BitConverter.DoubleToInt64Bits(Math.Abs(dx)) >> 52) & 0x7FF);
                    int ex;
                    double mx;
                    if (rawE == 0) {
                        dx *= 18446744073709551616.0;
                        rawE = (int)((BitConverter.DoubleToInt64Bits(Math.Abs(dx)) >> 52) & 0x7FF);
                        ex = (rawE - 1023) - 64 + extraX;
                        mx = dx * pow2(-(rawE - 1023));
                    } else {
                        ex = (rawE - 1023) + extraX;
                        int s = -(rawE - 1023);
                        mx = dx * pow2(s / 2) * pow2(s - s / 2);
                    }
                    int relExp = ex + shiftYZ;
                    if (relExp < -1020) {
                        // |dx / max(|dy|, |dz|)| < 2^-1020: when ry > 0 and |rz| <= 2^-500, both |dx| and |dz|
                        // are tiny relative to dy > 0, so atan2(sign * dz, -dx) is exact to full double precision.
                        if (ry > 0.0 && Math.Abs(rz) <= 3.0549363634996047e-151) {
                            rescale_diff_2d(other.z, p.z, p.x, other.x, out double uz, out double unx, out _);
                            if (uz == 0.0 && other.z != p.z) {
                                double zSign = other.z > p.z ? sign : -sign;
                                return unx < 0.0 ? zSign * Math.PI : 0.0;
                            }
                            return Math.Atan2(sign * uz, unx);
                        }
                        rx = 0.0;
                    } else {
                        rx = mx * pow2(relExp / 2) * pow2(relExp - relExp / 2);
                    }
                }
                double lr = Math.Sqrt(rx * rx + (ry * ry + rz * rz));
                double sxy = rx + ry;
                den0 = sxy > 0.0 ? (rz * rz - 2.0 * rx * ry) / (lr + sxy) : lr - sxy;
            } else {
                den0 = dVal * pow2(dExp) - ry;
            }
            if (rz == 0.0 && other.z != p.z) {
                double zSign = other.z > p.z ? sign : -sign;
                if (den0 < 0.0) return zSign * Math.PI;
                if (den0 > 0.0) return 0.0;
                return zSign * Math.PI * 0.5;
            }
            return Math.Atan2(sign * rz, den0);
        }

        // Exponent-tracked 2D-rescaled evaluation for extreme scales or mixed exponents (|yz| << |x|).
        // Precondition: (a.y-p.y, a.z-p.z) != (0,0) and (b.y-p.y, b.z-p.z) != (0,0).
        static double boundary_edge_angle_x_extreme(Vector3d p, Vector3d a, Vector3d b)
        {
            rescale_diff_2d(a.y, p.y, a.z, p.z, out double ay, out double az, out int sA);
            rescale_diff_2d(b.y, p.y, b.z, p.z, out double by, out double bz, out int sB);
            scaled_d_factor(a.x, p.x, ay, az, sA, out double daVal, out int daExp);
            scaled_d_factor(b.x, p.x, by, bz, sB, out double dbVal, out int dbExp);

            double p1 = ay * bz;
            double p2 = az * by;
            double num = p2 - p1;
            double dotYZ = ay * by + az * bz;

            int prodExp = daExp + dbExp;
            double prod;
            if (prodExp < -180) {
                prod = 0.0;
            } else if (prodExp > 180) {
                // daVal*dbVal >= 2^-64, so da'*db' >= 2^117 strictly dominates O(1) |num|, |dotYZ| -> angle = 0.
                return 0.0;
            } else {
                prod = daVal * dbVal * pow2(prodExp);
            }

            double den = prod + dotYZ;
            double numErr = 1e-12 * (Math.Abs(p1) + Math.Abs(p2));
            double denErr = 1e-12 * (prod + Math.Abs(ay * by) + Math.Abs(az * bz));
            return finish_edge_angle(p, a, b, num, den, numErr, denErr, dotYZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double finish_edge_angle(Vector3d p, Vector3d a, Vector3d b, double num, double den, double numErr, double denErr, double dotYZ)
        {
            // Couple the branch cut (oyz == 0, den <= 0) and open-edge limit (oyz == 0, ox == 0) with exact predicates.
            if (den <= denErr && Math.Abs(num) <= numErr) {
                int oyz = ExactPredicates.Orient2d(new Vector2d(p.y, p.z), new Vector2d(a.y, a.z), new Vector2d(b.y, b.z));
                if (oyz == 0) {
                    if (dotYZ >= 0.0)
                        return 0.0;
                    Vector3d ab = safe_diff(b, a);
                    int ox;
                    if (a.y != b.y && (a.z == b.z || Math.Abs(ab.y) >= Math.Abs(ab.z))) {
                        ox = ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(p.x, p.y));
                        if (b.y < a.y) ox = -ox;
                    } else {
                        ox = ExactPredicates.Orient2d(new Vector2d(a.x, a.z), new Vector2d(b.x, b.z), new Vector2d(p.x, p.z));
                        if (b.z < a.z) ox = -ox;
                    }
                    if (ox == 0)
                        return on_open_edge_angle_x(a, b);
                    if (ox < 0)
                        return 0.0;
                    int es = EdgeSignX(p.y, p.z, a, b);
                    return es == 0 ? 0.0 : (es < 0 ? Math.PI : -Math.PI);
                }
                // oyz != 0: exact sign(num) = -oyz. Never override den via 2D ox; only guard num's sign on den <= 0.
                if (den <= 0.0 && (num == 0.0 || (num > 0.0) != (oyz < 0)))
                    return oyz < 0 ? Math.PI : -Math.PI;
            }
            return Math.Atan2(num, den);
        }

        // Exact limit of boundary_edge_angle_x(p_eps, a, b) when p lies strictly on open segment (a, b).
        // Strictly antisymmetric under swapping (a, b) and bit-for-bit 2^k scale invariant.
        static double on_open_edge_angle_x(Vector3d a, Vector3d b)
        {
            if (a.z == b.z) {
                if (a.y == b.y)
                    return 0.0;
                if (a.x == b.x)
                    return b.y > a.y ? -Math.PI * 0.5 : Math.PI * 0.5;
                if ((b.x > a.x) == (b.y > a.y))
                    return b.y > a.y ? -Math.PI : Math.PI;
                return 0.0;
            }
            Vector3d u = rescale_pow2(safe_diff(b, a));
            double len = Math.Sqrt(u.x * u.x + u.y * u.y + u.z * u.z);
            if (len == 0.0)
                return 0.0;
            double ux = u.x / len, uy = u.y / len, uz = u.z / len;
            if (uz == 0.0) {
                // b.z != a.z, but |b.z - a.z| underflowed relative to max(|dx|, |dy|):
                double den = -ux * uy;
                if (den < 0.0) return b.z > a.z ? Math.PI : -Math.PI;
                if (den > 0.0) return 0.0;
                return b.z > a.z ? Math.PI * 0.5 : -Math.PI * 0.5;
            }
            return Math.Atan2(uz, -ux * uy);
        }

        // Rescale nonzero 2D difference (u1 - v1, u2 - v2) by exact 2^shift so max(|r1|, |r2|) lies in [1, 2).
        static void rescale_diff_2d(double u1, double v1, double u2, double v2, out double r1, out double r2, out int shift)
        {
            double d1 = u1 - v1, d2 = u2 - v2;
            int extra = 0;
            if (!is_finite(d1) || !is_finite(d2)) {
                d1 = u1 * 0.5 - v1 * 0.5;
                d2 = u2 * 0.5 - v2 * 0.5;
                extra = -1;
            }
            double m = Math.Max(Math.Abs(d1), Math.Abs(d2));
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            if (e == 0) {
                d1 *= 18446744073709551616.0;
                d2 *= 18446744073709551616.0;
                m = Math.Max(Math.Abs(d1), Math.Abs(d2));
                e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
                int k = -(e - 1023);
                double sc = pow2(k);
                r1 = d1 * sc;
                r2 = d2 * sc;
                shift = k + 64 + extra;
            } else {
                int k = -(e - 1023);
                double half = pow2(k / 2);
                double rest = pow2(k - k / 2);
                r1 = d1 * half * rest;
                r2 = d2 * half * rest;
                shift = k + extra;
            }
        }

        // Compute 2^shiftYZ * (la_raw - (ux - vx)) as dVal * 2^dExp without overflow or underflow.
        static void scaled_d_factor(double ux, double vx, double ry, double rz, int shiftYZ, out double dVal, out int dExp)
        {
            double dx = ux - vx;
            int extraX = 0;
            if (!is_finite(dx)) {
                dx = ux * 0.5 - vx * 0.5;
                extraX = 1;
            }
            double yz2 = ry * ry + rz * rz;
            if (dx == 0.0) {
                dVal = Math.Sqrt(yz2);
                dExp = 0;
                return;
            }
            double adx = Math.Abs(dx);
            int rawE = (int)((BitConverter.DoubleToInt64Bits(adx) >> 52) & 0x7FF);
            int ex;
            double mx;
            if (rawE == 0) {
                dx *= 18446744073709551616.0;
                rawE = (int)((BitConverter.DoubleToInt64Bits(Math.Abs(dx)) >> 52) & 0x7FF);
                ex = (rawE - 1023) - 64 + extraX;
                mx = dx * pow2(-(rawE - 1023));
            } else {
                ex = (rawE - 1023) + extraX;
                int s = -(rawE - 1023);
                mx = dx * pow2(s / 2) * pow2(s - s / 2);
            }
            int relExp = ex + shiftYZ;
            if (relExp > 60) {
                if (mx > 0.0) {
                    dVal = yz2 / (2.0 * mx);
                    dExp = -relExp;
                } else {
                    dVal = -2.0 * mx;
                    dExp = relExp;
                }
            } else if (relExp < -60) {
                dVal = Math.Sqrt(yz2);
                dExp = 0;
            } else {
                double rx = mx * pow2(relExp);
                double lr = Math.Sqrt(rx * rx + yz2);
                dVal = rx > 0.0 ? yz2 / (lr + rx) : lr - rx;
                dExp = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double pow2(int k) { return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector3d safe_diff(Vector3d a, Vector3d p)
        {
            double dx = a.x - p.x, dy = a.y - p.y, dz = a.z - p.z;
            if (is_finite(dx) && is_finite(dy) && is_finite(dz))
                return new Vector3d(dx, dy, dz);
            return new Vector3d(a.x * 0.5 - p.x * 0.5, a.y * 0.5 - p.y * 0.5, a.z * 0.5 - p.z * 0.5);
        }

        // Exact power-of-two rescaling into [1, 2) only when max |component| leaves [2^-100, 2^100].
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector3d rescale_pow2(Vector3d v)
        {
            double m = Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
            if (m == 0.0 || (m >= 7.8886090522101181e-31 && m <= 1.2676506002282294e30))
                return v;
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            if (e == 0)
                e = (int)((BitConverter.DoubleToInt64Bits(m * 18446744073709551616.0) >> 52) & 0x7FF) - 64;
            int shift = -(e - 1023);
            double half = pow2(shift / 2);
            double rest = pow2(shift - shift / 2);
            return new Vector3d(v.x * half * rest, v.y * half * rest, v.z * half * rest);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool is_finite(double x) { return (x - x) == 0.0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool is_finite(Vector3d v) { return (v.x - v.x) == 0.0 && (v.y - v.y) == 0.0 && (v.z - v.z) == 0.0; }
    }
}
