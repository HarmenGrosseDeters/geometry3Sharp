// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
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

        internal const int BoundaryTreeMinEdges = 500;
        internal int BoundaryTreeMinThreshold = BoundaryTreeMinEdges;

        readonly FlatBox[] boxes;
        readonly FlatTri[] tris;
        readonly BoundaryEdge[] boundary;
        readonly int rootIndex;
        readonly int triCount;
        readonly bool useDirectSum;

        volatile BoundaryTree boundaryTree;
        volatile bool boundaryTreeTried;
        readonly object boundaryTreeLock = new object();

        [ThreadStatic] static int[] boundaryTreeStack;
        // the start size of the traversal stack of a thread (the tree is balanced: depth <= 31, so 128 never grows); tests make it small
        internal static int BoundaryTreeStackStart = 128;
        internal static void ResetBoundaryTreeStack() { boundaryTreeStack = null; }
        internal static int BoundaryTreeStackLength { get { return boundaryTreeStack == null ? 0 : boundaryTreeStack.Length; } }
        internal static int[] BoundaryTreeStackArray { get { return boundaryTreeStack; } }

        internal int BoundaryCount { get { return boundary.Length; } }
        internal int TriangleCount { get { return triCount; } }
        internal bool UseDirectSum { get { return useDirectSum; } }
        internal bool HasBoundaryTree { get { return boundaryTree != null; } }

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

        /// <summary>
        /// Build the hierarchical boundary tree once (when |∂M| >= BoundaryTreeMinThreshold and not in direct-sum mode)
        /// before parallel threshold queries (MeshBatchQueries, MeshDeviation). Safe to call repeatedly.
        /// </summary>
        internal void PrepareThresholdQueries()
        {
            get_or_build_boundary_tree();
        }

        BoundaryTree get_or_build_boundary_tree()
        {
            if (useDirectSum || boundary.Length < BoundaryTreeMinThreshold)
                return null;
            if (boundaryTreeTried)
                return boundaryTree;
            lock (boundaryTreeLock) {
                if (!boundaryTreeTried) {
                    boundaryTree = BoundaryTree.Build(boundary);
                    boundaryTreeTried = true;
                }
                return boundaryTree;
            }
        }

        /// <summary>
        /// Exact threshold query matching Evaluate(p) &gt; iso.
        /// For open meshes with large boundaries (|∂M| >= BoundaryTreeMinThreshold), uses a certified
        /// hierarchical dipole sum over boundary chains (EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md §3) and
        /// falls back to the exact full boundary sum whenever |w_h - iso| &lt;= B_h + 1e-9 or p lies on the mesh.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal bool EvaluateAbove(Vector3d p, double iso)
        {
            if (triCount == 0 || !is_finite(p))
                return 0.0 > iso;
            if (useDirectSum)
                return direct_sum(p) > iso;
            if (!is_finite(iso))
                return Evaluate(p) > iso;

            BoundaryTree tree = get_or_build_boundary_tree();
            if (tree == null)
                return Evaluate(p) > iso;

            double surfaceCorrection = 0.0;
            int chi = signed_crossings_x(rootIndex, p.x, p.y, p.z, ref surfaceCorrection);
            if (surfaceCorrection == 0.0 && try_tree_boundary_sum_x(tree, p, out double bSum, out double bErr)) {
                double wH = (double)chi + bSum;
                int decided = decide_above(wH, iso, tree_margin(bErr, chi, bSum));
                if (decided != 0)
                    return decided > 0;
            }
            double w = (double)chi + boundary_sum_x(p) - surfaceCorrection;
            return w > iso;
        }

        /// <summary>
        /// The decision margin of a threshold query from the tree: the tree value wH = chi + bSum decides only when it is
        /// farther from iso than this, so that |wH - W| &lt;= margin where W is the value of the full sum (Evaluate), all in winding
        /// units (divided by 2 pi). Derivation in docs/design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md ("Which tree", the decision
        /// margin): bErr covers the summation order, the nodes' approximation and the evaluation error of the replaced edges;
        /// 4e-15 (|chi| + |bSum|) covers the roundings of the division by 2 pi and of chi + bSum (u relative each, about 18
        /// times too large); the factor 1.01 the rounding of the margin and of the accumulation of err.
        /// The 1e-9 is none of these terms and not part of the proof: it is a cushion on top of the proved bound A1 (every edge of
        /// an accepted node, kept on the fast path of boundary_edge_angle_x by fast_path_certificate, is within 1e-14 of its exact
        /// angle; the code charges 1e-12), and an absolute term because a winding number is dimensionless. A larger margin only
        /// sends more decisions to the full sum.
        /// </summary>
        internal static double tree_margin(double bErr, int chi, double bSum)
        {
            return 1.01 * (bErr + 1e-9 + 4e-15 * (Math.Abs((double)chi) + Math.Abs(bSum)));
        }

        /// <summary> +1: w &gt; iso and farther than the margin; -1: w &lt; iso and farther than the margin; 0: not decided by the tree </summary>
        internal static int decide_above(double w, double iso, double margin)
        {
            if (w > iso && (w - iso) > margin)
                return 1;
            if (w < iso && (iso - w) > margin)
                return -1;
            return 0;
        }

        /// <summary>
        /// Exact absolute-value threshold query matching Math.Abs(Evaluate(p)) &gt; iso.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal bool EvaluateAbsAbove(Vector3d p, double iso)
        {
            if (triCount == 0 || !is_finite(p))
                return 0.0 > iso;
            if (useDirectSum)
                return Math.Abs(direct_sum(p)) > iso;
            if (!is_finite(iso))
                return Math.Abs(Evaluate(p)) > iso;

            BoundaryTree tree = get_or_build_boundary_tree();
            if (tree == null)
                return Math.Abs(Evaluate(p)) > iso;

            double surfaceCorrection = 0.0;
            int chi = signed_crossings_x(rootIndex, p.x, p.y, p.z, ref surfaceCorrection);
            if (surfaceCorrection == 0.0 && try_tree_boundary_sum_x(tree, p, out double bSum, out double bErr)) {
                double wH = (double)chi + bSum;
                int decided = decide_above(Math.Abs(wH), iso, tree_margin(bErr, chi, bSum));
                if (decided != 0)
                    return decided > 0;
            }
            double w = (double)chi + boundary_sum_x(p) - surfaceCorrection;
            return Math.Abs(w) > iso;
        }

        /// <summary>
        /// Internal test/benchmark hook: evaluate the hierarchical boundary sum (in winding units) and its
        /// certified error bound at point p. Returns false if no tree is active or the query falls outside
        /// the certified range.
        /// </summary>
        internal bool TryEvaluateTreeBoundarySumX(Vector3d p, out double bSum, out double bErr)
        {
            bSum = 0.0;
            bErr = double.PositiveInfinity;
            if (triCount == 0 || !is_finite(p))
                return false;
            BoundaryTree tree = get_or_build_boundary_tree();
            if (tree == null)
                return false;
            return try_tree_boundary_sum_x(tree, p, out bSum, out bErr);
        }

        // Certified hierarchical Antipodal boundary sum (in winding units, i.e. divided by 2*pi).
        // For a chain node covering a_0 -> ... -> a_k inside ball B(c, r) with r < 0.25 * d and the +x ray
        // strictly outside the cap of B(c, r) seen from p by angular margin eps,
        //     sum_{j<k} edge(a_j, a_{j+1}) = edge(a_0, a_k) + 0.5 * va . (c - p) / d^3 + R,
        // with |R| <= A_abs * r / (d - r)^3 (docs/design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md §3).
        [MethodImpl(JitHints.Optimize)]
        bool try_tree_boundary_sum_x(BoundaryTree tree, Vector3d p, out double bSum, out double bErr)
        {
            bSum = 0.0;
            bErr = double.PositiveInfinity;
            double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
            if (!(pm <= 1e70))
                return false;

            double sum = 0.0;
            double err = 0.0;

            BoundaryEdge[] heavy = tree.Heavy;
            for (int i = 0; i < heavy.Length; ++i) {
                ref BoundaryEdge e = ref heavy[i];
                double wAbs = Math.Abs((double)e.Weight);
                sum += e.Weight * boundary_edge_angle_x(p, e.A, e.B);
                err += 1e-12 * wAbs;
            }

            int[] stack = boundaryTreeStack ?? (boundaryTreeStack = new int[BoundaryTreeStackStart]);
            int[] roots = tree.Roots;
            int[] loArr = tree.Lo, hiArr = tree.Hi, leftArr = tree.Left, rightArr = tree.Right;
            Vector3d[] verts = tree.Verts;

            for (int ri = 0; ri < roots.Length; ++ri) {
                int sp = 0;
                stack[sp++] = roots[ri];
                while (sp > 0) {
                    int k = stack[--sp];
                    int left = leftArr[k];
                    if (left >= 0) {
                        int st = node_query(tree, k, p, pm, null, out double nChord, out double nHalfOmega, out double nErr);
                        if (st < 0)
                            return false;
                        if (st > 0) {
                            sum += nChord + nHalfOmega;
                            err += nErr;
                            continue;
                        }
                        if (sp + 2 > stack.Length) {
                            Array.Resize(ref stack, stack.Length * 2);
                            boundaryTreeStack = stack;
                        }
                        stack[sp++] = rightArr[k];
                        stack[sp++] = left;
                    } else {
                        int lo = loArr[k], hi = hiArr[k];
                        for (int s = lo; s < hi; ++s) {
                            sum += boundary_edge_angle_x(p, verts[s], verts[s + 1]);
                            err += 1e-12;
                        }
                    }
                }
            }

            // Summation-order error between tree order and boundary_sum_x order:
            // each edge contributes <= pi * |Weight|, summed over N = boundary.Length edges -> 2 * N * u * pi * sum(|Weight|),
            // scaled by 1.01 to cover rounding of err accumulation and division by TwoPI.
            double sumOrderErr = 7.5e-16 * (double)boundary.Length * tree.TotalWeight;
            bSum = sum / MathUtil.TwoPI;
            bErr = 1.01 * (err + sumOrderErr) / MathUtil.TwoPI;
            return is_finite(bSum) && is_finite(bErr);
        }

        /// <summary> The terms of one tree node seen from p (the shared source of the query and of the test hook) </summary>
        internal sealed class NodeQuery
        {
            public bool Accepted;
            public double D, Dr, CoordEps, Eps, CosA, MinusX1U;                     // the cap test: Accepted needs R &lt; 0.25 D, MinusX1U &lt; CosA - Eps
            public double Chord, HalfOmega;
            public double Rem, VaErrTerm, DipScale, NodeErr;                          // the node's error and its clauses
        }

        /// <summary>
        /// One node of the tree for the query point p: 1 = accepted (Chord + HalfOmega replace the node's edges, NodeErr covers
        /// them), 0 = descend, -1 = the query is outside the certified range (the caller falls back on the full sum).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int node_query(BoundaryTree tree, int k, Vector3d p, double pm, NodeQuery dbg, out double chord, out double halfOmega, out double nodeErr)
        {
            // (dbg is null on the query path: the clause terms are written for the test hook only)
            chord = 0.0; halfOmega = 0.0; nodeErr = 0.0;
            double dx = tree.Cx[k] - p.x;
            double dy = tree.Cy[k] - p.y;
            double dz = tree.Cz[k] - p.z;
            double dd = dx * dx + dy * dy + dz * dz;
            int range = squared_distance_range(dd);
            if (range != 0)
                return range < 0 ? -1 : 0;
            double dn = Math.Sqrt(dd);
            double d = dn * (1.0 - 1e-15);
            double r = tree.R[k];
            if (dbg != null) dbg.D = d;
            if (!ball_is_small(r, d))
                return 0;
            double dr = d - r;
            double coordEps = 2.3e-16 * (pm + tree.M1[k]) / dr;
            if (dbg != null) { dbg.Dr = dr; dbg.CoordEps = coordEps; }
            if (!coord_eps_small(coordEps))
                return 0;
            // A1 (the evaluation error of every replaced edge is at most 1e-14, design note, "The decision margin") is proved for
            // edges that take the fast path of boundary_edge_angle_x; the certificate makes sure that every edge of an accepted
            // node does (no rescaling, no extreme path, no exact fallback). A node that fails it is descended into, and its leaf
            // edges are evaluated by the same function as the full sum, bit for bit.
            if (!fast_path_certificate(d, r, dy, dz))
                return 0;
            double rd = r / d;
            double cosA = Math.Sqrt(1.0 - rd * rd);
            double minusX1U = dx / dn;
            double eps = 1e-9 + 16.0 * coordEps;
            if (dbg != null) { dbg.CosA = cosA; dbg.MinusX1U = minusX1U; dbg.Eps = eps; }
            if (!ray_outside_cap(minusX1U, cosA, eps))
                return 0;
            Vector3d a0 = tree.Verts[tree.Lo[k]];
            Vector3d ak = tree.Verts[tree.Hi[k]];
            chord = (a0.x == ak.x && a0.y == ak.y && a0.z == ak.z)
                ? 0.0
                : boundary_edge_angle_x(p, a0, ak);
            double invD3 = 1.0 / (dd * dn);
            double vx = tree.Vx[k], vy = tree.Vy[k], vz = tree.Vz[k];
            halfOmega = 0.5 * (vx * dx + vy * dy + vz * dz) * invD3;
            double absDx = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
            double rem = tree.AAbs[k] * r / (dr * dr * dr);
            // (the soundness test checks vaErrTerm against the exact rounding error of va and finds a slack of about 2: a
            // factor 0.5 on it still passes; do not tighten it without a proof)
            double vaErrTerm = 0.5 * tree.VaErr[k] * absDx * invD3;
            double dipScale = 0.5 * (Math.Abs(vx) + Math.Abs(vy) + Math.Abs(vz)) * absDx * invD3;
            // The bound has to cover |chord + dipole - (the computed full sum over the node's edges)|, so it holds the
            // approximation error (rem, the rounding of va: vaErrTerm, of the dipole: dipScale, of dx: coordEps) and the
            // evaluation error of the full sum itself, which carries 1e-12 per edge (the leaf rule): the k = hi - lo edges
            // that the node replaces are charged the same. It grows with the chain, not with Len / dr: the vertices and p are
            // exact doubles here and the reference sum uses the same ones, so there is no displacement term as in the
            // grid-based tree of ExactMeshBoolean (design note 4b, which describes that tree).
            nodeErr = rem + vaErrTerm + (1e-12 + 16.0 * coordEps) * dipScale + 1e-11 + 16.0 * coordEps
                + 1e-12 * (double)(tree.Hi[k] - tree.Lo[k]);
            if (dbg != null) { dbg.Chord = chord; dbg.HalfOmega = halfOmega; dbg.Rem = rem; dbg.VaErrTerm = vaErrTerm; dbg.DipScale = dipScale; dbg.NodeErr = nodeErr; }
            if (!is_finite(chord) || !is_finite(halfOmega) || !is_finite(nodeErr))
                return -1;
            if (dbg != null) dbg.Accepted = true;
            return 1;
        }

        // The guards of node_query, each a function of its own so that its boundary is tested from both sides.

        /// <summary> the squared distance of p to the node centre: 0 = in range, -1 = too far for the certified range (the query falls back), +1 = too near (descend) </summary>
        internal static int squared_distance_range(double dd)
        {
            if (dd > 1e140)
                return -1;
            if (dd < 1e-140)
                return 1;
            return 0;
        }

        /// <summary> a node is used only where its ball is small against its distance: r &lt; d / 4 </summary>
        internal static bool ball_is_small(double r, double d) { return r < 0.25 * d; }

        /// <summary> and where the rounding of the coordinates stays small against the distance </summary>
        internal static bool coord_eps_small(double coordEps) { return coordEps < 1e-6; }

        /// <summary>
        /// Every edge of a node (all its points within r of the centre c, d the distance from p to c, (dy, dz) the yz part of c - p)
        /// takes the fast path of boundary_edge_angle_x: |a - p| lies in [0.75 d, 1.25 d], so its largest component is in
        /// [2^-100, 2^100] for d in [2^-97, 2^99]; the yz part of every edge point is at least 1e-9 d (the yz part of the centre
        /// minus r), far above the 2^-100 of the fast path's test; and the point is not on the +x axis (|yz| &gt; 0).
        /// </summary>
        internal static bool fast_path_certificate(double d, double r, double dy, double dz)
        {
            const double MinD = 6.310887241768095e-30;      // 2^-97
            const double MaxD = 6.3382530011411470e29;      // 2^99
            if (!(d >= MinD && d <= MaxD))
                return false;
            double yz = Math.Sqrt(dy * dy + dz * dz);
            return yz - r >= 1e-9 * d;
        }

        /// <summary> the +x ray from p misses the cap of the node's ball by the margin eps (the remainder bound needs the ray outside it) </summary>
        internal static bool ray_outside_cap(double minusX1U, double cosA, double eps) { return minusX1U < cosA - eps; }

        /// <summary> Test hook: the node k (0 = the root of the first chain) seen from p, with its stored data. Null without a tree. </summary>
        internal sealed class NodeInfo
        {
            public NodeQuery Query;
            public int Status;
            public Vector3d Center;
            public double R, VaErr, AAbs, M1;
            public int Left, Right, Lo, Hi;             // child nodes (-1 at a leaf) and the vertex range of the chain
            public Vector3d HalfVa;                     // the stored 0.5 * va
            public Vector3d[] Vertices;                 // the node's chain points, lo .. hi
        }

        /// <summary> Test hooks: the roots of the chains, the weighted (non-unit) edges and the total weight of the tree (null / 0 without a tree) </summary>
        internal int[] BoundaryTreeRoots { get { BoundaryTree t = get_or_build_boundary_tree(); return t == null ? null : (int[])t.Roots.Clone(); } }
        internal BoundaryEdge[] BoundaryTreeHeavy { get { BoundaryTree t = get_or_build_boundary_tree(); return t == null ? null : (BoundaryEdge[])t.Heavy.Clone(); } }
        internal double BoundaryTreeTotalWeight { get { BoundaryTree t = get_or_build_boundary_tree(); return t == null ? 0.0 : t.TotalWeight; } }

        /// <summary> Test hook: the number of tree nodes (0 without a tree) </summary>
        internal int BoundaryNodeCount { get { BoundaryTree t = get_or_build_boundary_tree(); return t == null ? 0 : t.Lo.Length; } }

        internal NodeInfo DescribeBoundaryNode(int k, Vector3d p)
        {
            BoundaryTree tree = get_or_build_boundary_tree();
            if (tree == null || k < 0 || k >= tree.Lo.Length)
                return null;
            var info = new NodeInfo();
            double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
            info.Query = new NodeQuery();
            info.Status = node_query(tree, k, p, pm, info.Query, out _, out _, out _);
            info.Center = new Vector3d(tree.Cx[k], tree.Cy[k], tree.Cz[k]);
            info.R = tree.R[k]; info.VaErr = tree.VaErr[k]; info.AAbs = tree.AAbs[k]; info.M1 = tree.M1[k];
            info.Left = tree.Left[k]; info.Right = tree.Right[k]; info.Lo = tree.Lo[k]; info.Hi = tree.Hi[k];
            info.HalfVa = new Vector3d(tree.Vx[k], tree.Vy[k], tree.Vz[k]);
            int lo = tree.Lo[k], hi = tree.Hi[k];
            info.Vertices = new Vector3d[hi - lo + 1];
            Array.Copy(tree.Verts, lo, info.Vertices, 0, info.Vertices.Length);
            return info;
        }

        // Balanced binary tree over head-to-tail directed unit-weight exterior boundary chains.
        // Built deterministically once per large-boundary open mesh (no hash-iteration order dependence).
        sealed class BoundaryTree
        {
            const int LeafSegments = 4;

            public readonly Vector3d[] Verts;
            public readonly BoundaryEdge[] Heavy;
            public readonly double TotalWeight;
            public readonly int[] Roots;
            public readonly int[] Lo, Hi, Left, Right;
            public readonly double[] Cx, Cy, Cz, R, Vx, Vy, Vz, AAbs, VaErr, M1;

            BoundaryTree(
                Vector3d[] verts, BoundaryEdge[] heavy, double totalWeight, int[] roots,
                int[] lo, int[] hi, int[] left, int[] right,
                double[] cx, double[] cy, double[] cz, double[] r,
                double[] vx, double[] vy, double[] vz,
                double[] aAbs, double[] vaErr, double[] m1)
            {
                Verts = verts; Heavy = heavy; TotalWeight = totalWeight; Roots = roots;
                Lo = lo; Hi = hi; Left = left; Right = right;
                Cx = cx; Cy = cy; Cz = cz; R = r;
                Vx = vx; Vy = vy; Vz = vz;
                AAbs = aAbs; VaErr = vaErr; M1 = m1;
            }

            public static BoundaryTree Build(BoundaryEdge[] edges)
            {
                int n = edges.Length;
                if (n < 16)
                    return null;

                var unit = new List<BoundaryEdge>(n);
                var heavy = new List<BoundaryEdge>();
                double mMax = 0.0;
                double totalWeight = 0.0;
                for (int i = 0; i < n; ++i) {
                    BoundaryEdge e = edges[i];
                    totalWeight += Math.Abs((double)e.Weight);
                    double ma = Math.Max(Math.Abs(e.A.x), Math.Max(Math.Abs(e.A.y), Math.Abs(e.A.z)));
                    double mb = Math.Max(Math.Abs(e.B.x), Math.Max(Math.Abs(e.B.y), Math.Abs(e.B.z)));
                    if (ma > mMax) mMax = ma;
                    if (mb > mMax) mMax = mb;
                    if (e.Weight == 1)
                        unit.Add(e);
                    else
                        heavy.Add(e);
                }
                // Keep all build and query intermediates (dv.Length, cr.Length, dd, dr^3) strictly in normal range.
                if (unit.Count < 16 || mMax > 1e65 || (mMax > 0.0 && mMax < 1e-65))
                    return null;

                int nu = unit.Count;
                var starts = new Dictionary<PointKey, List<int>>(nu);
                var indegree = new Dictionary<PointKey, int>(nu);
                for (int s = 0; s < nu; ++s) {
                    PointKey ka = new PointKey(unit[s].A);
                    PointKey kb = new PointKey(unit[s].B);
                    if (!starts.TryGetValue(ka, out List<int> list)) {
                        list = new List<int>(2);
                        starts[ka] = list;
                    }
                    list.Add(s);
                    indegree.TryGetValue(kb, out int deg);
                    indegree[kb] = deg + 1;
                }

                var used = new bool[nu];
                var verts = new List<Vector3d>(nu + 32);
                var chainLo = new List<int>();
                var chainHi = new List<int>();
                // Pass 0: open chains (start has indegree 0); Pass 1: closed boundary loops.
                for (int pass = 0; pass < 2; ++pass) {
                    for (int s0 = 0; s0 < nu; ++s0) {
                        if (used[s0])
                            continue;
                        if (pass == 0 && indegree.ContainsKey(new PointKey(unit[s0].A)))
                            continue;
                        int vStart = verts.Count;
                        verts.Add(unit[s0].A);
                        int s = s0;
                        while (s >= 0) {
                            used[s] = true;
                            verts.Add(unit[s].B);
                            int next = -1;
                            if (starts.TryGetValue(new PointKey(unit[s].B), out List<int> list)) {
                                for (int j = 0; j < list.Count; ++j) {
                                    int q = list[j];
                                    if (!used[q]) {
                                        next = q;
                                        break;
                                    }
                                }
                            }
                            s = next;
                        }
                        chainLo.Add(vStart);
                        chainHi.Add(verts.Count - 1);
                    }
                }

                // If boundary is mostly leaf-only chains (average chain <= LeafSegments edges), skip tree overhead.
                if (chainLo.Count == 0 || (long)chainLo.Count * (LeafSegments + 1) > nu)
                    return null;

                Vector3d[] vArr = verts.ToArray();
                int estNodes = Math.Max(chainLo.Count, (nu / LeafSegments) * 2 + chainLo.Count);
                var lo = new List<int>(estNodes);
                var hi = new List<int>(estNodes);
                var left = new List<int>(estNodes);
                var right = new List<int>(estNodes);
                var cx = new List<double>(estNodes);
                var cy = new List<double>(estNodes);
                var cz = new List<double>(estNodes);
                var rr = new List<double>(estNodes);
                var vx = new List<double>(estNodes);
                var vy = new List<double>(estNodes);
                var vz = new List<double>(estNodes);
                var aa = new List<double>(estNodes);
                var vaErr = new List<double>(estNodes);
                var m1 = new List<double>(estNodes);

                var roots = new int[chainLo.Count];
                for (int c = 0; c < chainLo.Count; ++c)
                    roots[c] = build_node(vArr, chainLo[c], chainHi[c], lo, hi, left, right, cx, cy, cz, rr, vx, vy, vz, aa, vaErr, m1);

                return new BoundaryTree(
                    vArr, heavy.ToArray(), totalWeight, roots,
                    lo.ToArray(), hi.ToArray(), left.ToArray(), right.ToArray(),
                    cx.ToArray(), cy.ToArray(), cz.ToArray(), rr.ToArray(),
                    vx.ToArray(), vy.ToArray(), vz.ToArray(),
                    aa.ToArray(), vaErr.ToArray(), m1.ToArray());
            }

            static int build_node(
                Vector3d[] verts, int a, int b,
                List<int> lo, List<int> hi, List<int> left, List<int> right,
                List<double> cx, List<double> cy, List<double> cz, List<double> rr,
                List<double> vx, List<double> vy, List<double> vz,
                List<double> aa, List<double> vaErr, List<double> m1)
            {
                int k = lo.Count;
                lo.Add(a); hi.Add(b); left.Add(-1); right.Add(-1);
                int count = b - a + 1;
                Vector3d sum = Vector3d.Zero;
                for (int q = a; q <= b; ++q)
                    sum += verts[q];
                Vector3d c = sum / count;

                double r = 0.0, l1 = 0.0, abs = 0.0, prods = 0.0;
                Vector3d va = Vector3d.Zero;
                for (int q = a; q <= b; ++q) {
                    Vector3d v = verts[q];
                    Vector3d w = (q < b) ? verts[q + 1] : verts[a];
                    Vector3d dv = v - c;
                    Vector3d dw = w - c;
                    double lv = dv.Length;
                    double lw = dw.Length;
                    if (lv > r) r = lv;
                    double vm1 = Math.Abs(v.x) + Math.Abs(v.y) + Math.Abs(v.z);
                    if (vm1 > l1) l1 = vm1;
                    Vector3d cr = dv.Cross(dw);
                    va += cr;
                    abs += cr.Length;
                    prods += lv * lw;
                }

                double rUp = r * (1.0 + 1e-14);
                double vaError = 0.5 * prods * (count + 8) * 4.5e-16;
                double aAbsVal = 0.5 * abs + vaError;
                if (!is_finite(c) || !is_finite(rUp) || !is_finite(va) || !is_finite(vaError) || !is_finite(aAbsVal) || !is_finite(l1))
                    rUp = double.PositiveInfinity;

                cx.Add(c.x); cy.Add(c.y); cz.Add(c.z);
                rr.Add(rUp);
                vx.Add(0.5 * va.x); vy.Add(0.5 * va.y); vz.Add(0.5 * va.z);
                aa.Add(aAbsVal);
                vaErr.Add(vaError);
                m1.Add(l1);

                if (b - a > LeafSegments) {
                    int mid = a + ((b - a) >> 1);
                    int l = build_node(verts, a, mid, lo, hi, left, right, cx, cy, cz, rr, vx, vy, vz, aa, vaErr, m1);
                    int rt = build_node(verts, mid, b, lo, hi, left, right, cx, cy, cz, rr, vx, vy, vz, aa, vaErr, m1);
                    left[k] = l;
                    right[k] = rt;
                }
                return k;
            }
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
        /// double range without overflow or underflow, and is bit-for-bit invariant under positive 2^k scaling (except for a scaling that moves S1 across 2^-1000, see below).
        /// Accuracy: for every p, a, b the result differs from the exact atan2(num, den) of the exact differences by at most
        /// 2e-13, and it is on the right side of the branch cut, den &lt;= 1e-12 S2: there the rounded num has the exact sign when
        /// |num| > 1e-12 S1 and S1 = |ay bz| + |az by| >= 2^-1000 (SubnormalS1), and accurate_edge_angle_x decides otherwise (below
        /// 2^-1000 the products carry an absolute rounding error of up to 2^-1075, which no longer is below 1e-12 S1; design note
        /// EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md, "The accuracy of the edge angle"). The rounded
        /// evaluation is used where |num| + |den| >= 2^-5 (S1 + S2), which implies hypot(num, den) >= 0.0220 (S1 + S2). Where den
        /// cancels, that is where a - p and b - p are nearly antiparallel in 3D (p near the open segment (a, b)), and where the sign
        /// of num is uncertain at the branch cut (p near the yz line of the edge, den &lt;= 1e-12 S2), accurate_edge_angle_x is
        /// used. The limit cases (p at a vertex, on the open edge, on the yz line of the edge) are as defined above and below.
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
                // Error bounds: finish_edge_angle decides. While the products are normal numbers (S1 >= 2^-1000; the fast path
                // has |yz| >= 2^-200) they have a relative error of 3u and the sign of num is certain where |num| > 1e-12 S1.
                // Where S1 < 2^-1000 the rounding to the subnormal grid (absolute error up to 2^-1075 per product, more than
                // 1e-12 S1) can flip the sign of a tiny num, and finish_edge_angle then lets the exact path decide wherever
                // den <= 1e-12 S2 (the branch cut).
                // Sizes of the terms of num and den (S1, S2 of the design note, "The accuracy of the edge angle").
                double s1 = Math.Abs(p1) + Math.Abs(p2);
                double s2 = da * db + Math.Abs(ay * by) + Math.Abs(az * bz);
                return finish_edge_angle(p, a, b, num, den, s1, s2);
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
            double s1 = Math.Abs(p1) + Math.Abs(p2);
            double s2 = prod + Math.Abs(ay * by) + Math.Abs(az * bz);
            return finish_edge_angle(p, a, b, num, den, s1, s2);
        }

        /// <summary>
        /// Decides between the rounded evaluation Atan2(num, den) and accurate_edge_angle_x. The rounded evaluation is kept
        /// when (1) |num| + |den| >= 2^-5 (S1 + S2), which makes hypot(num, den) >= 0.0220 (S1 + S2), and (2) the sign of
        /// num is certain or den is clearly positive (outside the zone of the branch cut: den &lt;= 1e-12 S2 and either
        /// |num| &lt;= 1e-12 S1 or S1 &lt; 2^-1000, where the products are subnormal and the sign of a tiny num is not certain).
        /// Then the angle is within 1.2e-13 of the exact one (design note, "The accuracy of the edge angle"); everywhere else
        /// it is computed from exact num and den. S1 = |p1| + |p2| and S2 = da db + |ay by| +
        /// |az bz| are the sizes of the terms of num and den. The comparisons are false for NaN, which stays on the rounded path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double finish_edge_angle(Vector3d p, Vector3d a, Vector3d b, double num, double den, double s1, double s2)
        {
            double an = Math.Abs(num);
            if (an + Math.Abs(den) < FastAngleRatio * (s1 + s2) || (den <= 1e-12 * s2 && (an <= 1e-12 * s1 || s1 < SubnormalS1))) {
                // p exactly on the yz line of the edge (exact num == 0): a defined limit, no integer arithmetic needed. The sign of
                // dotYZ is exact from the signs of the differences: the yz parts of a - p and b - p are collinear (exact Orient2d), so
                // ay by and az bz both have the sign of the same factor (or are 0), and the sign of a difference is that of the comparison.
                if (ExactPredicates.Orient2d(new Vector2d(p.y, p.z), new Vector2d(a.y, a.z), new Vector2d(b.y, b.z)) == 0)
                    return edge_angle_on_yz_line_x(p, a, b, collinear_yz_dot_is_nonnegative(p, a, b));
                return accurate_edge_angle_x(p, a, b);
            }
            return Math.Atan2(num, den);
        }

        // sign of (a - p).yz . (b - p).yz >= 0 when the yz parts of a - p and b - p are collinear (b - p = lambda (a - p), or one is 0):
        // both products have the sign of lambda, so the dot product is negative iff one of them is.
        static bool collinear_yz_dot_is_nonnegative(Vector3d p, Vector3d a, Vector3d b)
        {
            int sy = Math.Sign(a.y.CompareTo(p.y)) * Math.Sign(b.y.CompareTo(p.y));
            int sz = Math.Sign(a.z.CompareTo(p.z)) * Math.Sign(b.z.CompareTo(p.z));
            return sy >= 0 && sz >= 0;
        }

        // 2^-5. With it the rounded num and den (errors at most 4.01 u S1 and 22.1 u S2, u = 2^-53) give an angle within
        // asin(22.1 u / 0.0220) <= 1.12e-13 of the exact one.
        const double FastAngleRatio = 0.03125;

        // 2^-1000. Below it the products of num carry an absolute rounding error of up to 2^-1075 each (the subnormal grid), which is
        // no longer below 1e-12 S1; from it up, 2^-1074 <= 2^-74 S1, so the sign of num is certain where |num| > 1e-12 S1.
        const double SubnormalS1 = 9.3326361850321888e-302;

        [ThreadStatic] static int accurateEdgeAngleCalls;
        // the calls of accurate_edge_angle_x on this thread (tests: the inputs with exact num == 0 must not reach it)
        internal static int AccurateEdgeAngleCalls { get { return accurateEdgeAngleCalls; } }

        /// <summary>
        /// The edge angle atan2(num, den) from exact num and den, for the inputs the rounded evaluation cannot serve (p near the
        /// yz line of the edge, or the sign of num uncertain). num = az by - ay bz and dotYZ = ay by + az bz are evaluated
        /// exactly from the exact differences (integers over a common grid); den = da db + dotYZ with da = |A| - ax,
        /// db = |B| - bx is not a polynomial: da db comes from integer square roots of the exact squared lengths at k extra
        /// bits, k chosen so that the absolute error of den is at most 2^-62 |num|. The angle is then within 2^-61 of the
        /// exact one, plus the roundings of the two doubles (1.1e-16 each) and of Atan2. When num is exactly 0 (p on the yz
        /// line of the edge) the branch cut and the open-edge limit are decided by exact predicates as before.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double accurate_edge_angle_x(Vector3d p, Vector3d a, Vector3d b)
        {
            ++accurateEdgeAngleCalls;
            ExactDyadic[] ds = {
                ExactDyadic.Of(a.x) - ExactDyadic.Of(p.x), ExactDyadic.Of(a.y) - ExactDyadic.Of(p.y), ExactDyadic.Of(a.z) - ExactDyadic.Of(p.z),
                ExactDyadic.Of(b.x) - ExactDyadic.Of(p.x), ExactDyadic.Of(b.y) - ExactDyadic.Of(p.y), ExactDyadic.Of(b.z) - ExactDyadic.Of(p.z) };
            int e0 = int.MaxValue;
            for (int i = 0; i < ds.Length; ++i)
                if (!ds[i].M.IsZero && ds[i].E < e0) e0 = ds[i].E;
            BigInteger[] g = new BigInteger[6];
            for (int i = 0; i < g.Length; ++i)
                g[i] = ds[i].M.IsZero ? BigInteger.Zero : ds[i].M << (ds[i].E - e0);
            BigInteger AX = g[0], AY = g[1], AZ = g[2], BX = g[3], BY = g[4], BZ = g[5];

            BigInteger num = AZ * BY - AY * BZ;
            BigInteger dot = AY * BY + AZ * BZ;
            if (num.IsZero)
                return edge_angle_on_yz_line_x(p, a, b, dot.Sign >= 0);

            BigInteger yzA = AY * AY + AZ * AZ, yzB = BY * BY + BZ * BZ;
            BigInteger aa = AX * AX + yzA, bb = BX * BX + yzB;
            // bit length of floor(sqrt(n)) is (bit length of n + 1) / 2
            long lenBits = Math.Max((ExactPoint3.BitLength(aa) + 1) / 2, (ExactPoint3.BitLength(bb) + 1) / 2);
            // Precision: with L = max(|A|, |B|) < 2^lenBits, da, db <= 2 L, so daK + dbK <= 4 L 2^k + 4 and the error of denK
            // (2 (daK + dbK) + 4, below) is at most 8 L 2^k + 12 < 2^(lenBits + 3 + k) + 12. |num| >= 2^(bits(num) - 1) and
            // k >= 70 + lenBits + 2 - bits(num), so |numK| 2^-62 = |num| 2^(2k - 62) >= 2^(k + lenBits + 9), at least 2^6 times the
            // error (the 12 is below 2^(k + 6)): the absolute error of den is at most 2^-62 |num|, whatever the ratio of num to den.
            // (An earlier version checked this in a loop that raised k by 32 and never ran.)
            long k = 70 + Math.Max(0L, lenBits + 2 - ExactPoint3.BitLength(BigInteger.Abs(num)));
            int kk = (int)k;
            BigInteger laK = isqrt(aa << (2 * kk)), lbK = isqrt(bb << (2 * kk));
            BigInteger daK = AX.Sign > 0 ? (yzA << (2 * kk)) / (laK + (AX << kk)) : laK - (AX << kk);
            BigInteger dbK = BX.Sign > 0 ? (yzB << (2 * kk)) / (lbK + (BX << kk)) : lbK - (BX << kk);
            BigInteger numK = num << (2 * kk);
            BigInteger denK = daK * dbK + (dot << (2 * kk));
            // |error of denK| <= 2 (daK + dbK) + 4: daK, dbK are within 2 of da 2^k, db 2^k
            Debug.Assert((((daK + dbK) * 2 + 4) << 62) <= BigInteger.Abs(numK));

            long bl = Math.Max(ExactPoint3.BitLength(BigInteger.Abs(numK)), ExactPoint3.BitLength(BigInteger.Abs(denK)));
            int sh = (int)(bl - 1000);
            ExactDyadic one = new ExactDyadic(BigInteger.One, 0);
            double nd = ExactDyadic.Quotient(new ExactDyadic(numK, -sh), one);
            double dd = denK.IsZero ? 0.0 : ExactDyadic.Quotient(new ExactDyadic(denK, -sh), one);
            if (nd == 0.0)
                nd = num.Sign > 0 ? double.Epsilon : -double.Epsilon;   // underflow against a huge den: keep the sign of num
            return Math.Atan2(nd, dd);
        }

        // floor(sqrt(n)) for n >= 0, Newton's iteration from above.
        static BigInteger isqrt(BigInteger n)
        {
            if (n.Sign <= 0) return BigInteger.Zero;
            BigInteger x = BigInteger.One << (int)((ExactPoint3.BitLength(n) + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        // p lies exactly on the yz line of the edge (num == 0 exactly): the branch cut (dot &lt; 0) and the open-edge limit are
        // decided by exact predicates.
        static double edge_angle_on_yz_line_x(Vector3d p, Vector3d a, Vector3d b, bool dotNonNegative)
        {
            if (dotNonNegative)
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
