// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Threading;

namespace g3
{
    /// <summary> The cost a <see cref="ContourLoft"/> band minimizes, summed over its triangles. </summary>
    public enum ContourLoftCost
    {
        /// <summary> The band's surface area (Fuchs, Kedem &amp; Uselton 1977). </summary>
        MinimumArea,
        /// <summary>
        /// The volume enclosed by the band and the two planar caps, maximized (Keppel 1975). Meant for convex contours, as in the
        /// paper; for non-convex ones the largest volume is not always the plausible surface.
        /// </summary>
        MaximumVolume,
        /// <summary> <see cref="ContourLoft.CustomCost"/> per triangle, minimized. </summary>
        Custom
    }

    /// <summary> Outcome of <see cref="ContourLoft.Compute"/>. </summary>
    public enum ContourLoftStatus
    {
        /// <summary> <see cref="ContourLoftResult.Mesh"/> is the band (or the closed solid, with caps), checked. </summary>
        Success,
        /// <summary>
        /// Every band contains a triangle that <see cref="ContourLoft.IsAdmissible"/> rejects (decided exactly, given an exact test);
        /// <see cref="ContourLoftResult.InadmissibleTriangles"/> is the smallest number of such triangles in a band.
        /// </summary>
        NoAdmissibleBand,
        /// <summary> The optimal admissible band (or the solid with its caps) intersects itself (exact check). </summary>
        SelfIntersecting,
        /// <summary> A cap could not be meshed, or its mesh did not keep the contour's vertices and edges. </summary>
        CapFailed,
        /// <summary> Fewer than 3 vertices, a non-finite or repeated consecutive vertex, zero area, or the planes not separated. </summary>
        InvalidInput,
        /// <summary> A cost was not finite, or the costs are too large to be summed along a band. </summary>
        InvalidCost,
        /// <summary> The product of the vertex counts is above <see cref="ContourLoft.MaxCells"/>. </summary>
        TooLarge
    }

    /// <summary>
    /// A candidate triangle of a band: an edge of one contour and its apex on the other. Indices refer to the contours as given
    /// to <see cref="ContourLoft.Compute"/>.
    /// </summary>
    public struct ContourLoftTriangle
    {
        /// <summary>
        /// true: the edge (First[I], First[I + 1]) with the apex Second[J]; false: the edge (Second[J], Second[J + 1]) with the apex
        /// First[I] (indices modulo the counts).
        /// </summary>
        public bool EdgeOnFirst;
        public int I, J;
        /// <summary> A, B: the edge's end points in the order of its contour as given; C: the apex. </summary>
        public Vector3d A, B, C;
    }

    /// <summary> Result of <see cref="ContourLoft.Compute"/>. </summary>
    public sealed class ContourLoftResult
    {
        public ContourLoftStatus Status { get; internal set; }
        /// <summary> Why the status is not Success (empty on Success). </summary>
        public string Reason { get; internal set; } = "";
        /// <summary>
        /// The band, only on Success (null otherwise): vertex i is First[i], vertex m + j is Second[j], then the caps' Steiner points.
        /// Triangles face out of the solid that the band bounds together with the planar caps.
        /// </summary>
        public DMesh3 Mesh { get; internal set; }
        /// <summary> The band's total cost, the sum the search minimized (for MaximumVolume: minus the band's share of the volume). </summary>
        public double TotalCost { get; internal set; }
        /// <summary> The J of the spanning edge (First[0], Second[J]) of the band. </summary>
        public int StartOffset { get; internal set; }
        /// <summary> 0 on Success; on NoAdmissibleBand the smallest number of inadmissible triangles in any band (exact). </summary>
        public int InadmissibleTriangles { get; internal set; }
        /// <summary> starts whose bounded region held no valid path and were solved in the full region (tests: 0 on ordinary input) </summary>
        internal int FullRegionFallbacks;
    }

    /// <summary>
    /// A triangulated band between two closed polygons in distinct parallel planes (contour lofting, stage 1 of
    /// docs/design/CONTOUR_LOFT.md): every triangle has an edge on one contour and its apex on the other, and the band is the
    /// optimal one over all such bands and all start correspondences (Fuchs, Kedem &amp; Uselton 1977: shortest monotone paths in
    /// the toroidal grid graph, divide and conquer over the starts). Optionally both caps through <see cref="QualityMesh2"/>, for a
    /// closed solid.
    /// </summary>
    /// <remarks>
    /// <para>Admissibility: a triangle that <see cref="IsAdmissible"/> rejects counts as one violation; bands are compared by their
    /// number of violations first and by their cost second. So <see cref="ContourLoftStatus.NoAdmissibleBand"/> is exact (no band
    /// without violations exists) whenever the test is exact, as <see cref="NotSteeperThan"/> is. The cost is optimal up to the
    /// rounding of the cost sums.</para>
    /// <para>Cost: O(m n) evaluations of the cost and the admissibility test (each candidate triangle once), O(m n log min(m, n))
    /// steps of the search, about 36 bytes of memory per pair of vertices (m n at most <see cref="MaxCells"/>). Serial.</para>
    /// <para>Bands whose triangles on one contour's edges form a single block (one vertex fanned over the whole other contour)
    /// repeat a spanning edge and are not manifold; they are excluded (the search tracks the turns of its path).</para>
    /// <para>Limits: one contour per plane, no branching. The planes are not checked for exact parallelism: a vertex of each contour
    /// must lie strictly on the same side of the other contour's (Newell) plane. Contours running in opposite directions are
    /// matched (the second one is traversed backwards; ids still follow the input). Dissimilar or offset contours can make the
    /// optimal band intersect itself; that is checked exactly and reported (<see cref="ContourLoftStatus.SelfIntersecting"/>).</para>
    /// </remarks>
    public sealed class ContourLoft
    {
        public ContourLoftCost Cost = ContourLoftCost.MinimumArea;
        /// <summary> The cost of a triangle for <see cref="ContourLoftCost.Custom"/>: finite, may be negative; deterministic. </summary>
        public Func<ContourLoftTriangle, double> CustomCost;
        /// <summary> Whether a triangle may be used (null: every triangle); deterministic. </summary>
        public Func<ContourLoftTriangle, bool> IsAdmissible;
        /// <summary>
        /// Evaluate the built-in costs on normalized copies of the contours (Christiansen &amp; Sederberg 1978): each translated to its
        /// vertex centroid and scaled by 1 / sqrt(area), the two at unit distance. For offset or differently sized contours. The
        /// admissibility test and the output always use the input positions; a custom cost is not affected.
        /// </summary>
        public bool NormalizeForCost;
        /// <summary> Add both planar caps: the result is a closed solid. </summary>
        public bool Caps;
        /// <summary>
        /// Options of the cap meshes (not changed by the loft); null: a constrained Delaunay triangulation without refinement. The
        /// contour edges are locked, so refinement adds interior points only.
        /// </summary>
        public QualityMesh2 CapMesher;
        /// <summary> Check the band (and the solid) for self-intersections, exactly. </summary>
        public bool CheckSelfIntersections = true;
        /// <summary> Largest m * n accepted (memory, about 36 bytes each); above it the status is TooLarge. </summary>
        public long MaxCells = 1L << 22;

        /// <summary> An admissibility test for terrain: the triangle is not steeper than the spec (exact, <c>gs.Slope.IsNotSteeper</c>). </summary>
        public static Func<ContourLoftTriangle, bool> NotSteeperThan(gs.SlopeSpec spec)
        {
            if (!spec.IsValid) throw new ArgumentException("the slope spec is not valid");
            return t => gs.Slope.IsNotSteeper(t.A, t.B, t.C, spec);
        }

        /// <exception cref="ArgumentNullException">A contour is null.</exception>
        /// <exception cref="ArgumentException">Custom cost without <see cref="CustomCost"/>, or MaxCells not positive.</exception>
        public ContourLoftResult Compute(IList<Vector3d> first, IList<Vector3d> second, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (Cost == ContourLoftCost.Custom && CustomCost == null) throw new ArgumentException("Cost is Custom but CustomCost is null");
            if (MaxCells <= 0) throw new ArgumentException("MaxCells must be positive");
            return new Run(this, first, second, cancellationToken).Execute();
        }

        sealed class Run
        {
            readonly ContourLoft o;
            readonly IList<Vector3d> first, second;
            readonly CancellationToken ct;
            readonly ContourLoftResult result = new ContourLoftResult();

            // internal contours: A (m vertices, the rows) and B (n <= m vertices, the starts); B runs the same way as A
            Vector3d[] A, B;
            int m, n;
            bool swapped;          // A is the second contour
            bool reversedB;        // B is its input contour backwards
            int sgn;               // +1: the internal orientation faces out of the solid; -1: flip on output
            double[] costDown, costRight;
            bool[] badDown, badRight;

            public Run(ContourLoft options, IList<Vector3d> first, IList<Vector3d> second, CancellationToken ct)
            {
                o = options; this.first = first; this.second = second; this.ct = ct;
            }

            ContourLoftResult Fail(ContourLoftStatus status, string reason)
            {
                result.Status = status; result.Reason = reason; result.Mesh = null;
                return result;
            }

            public ContourLoftResult Execute()
            {
                string why = Validate(first, "first") ?? Validate(second, "second");
                if (why != null) return Fail(ContourLoftStatus.InvalidInput, why);
                if ((long)first.Count * second.Count > o.MaxCells || (long)(first.Count + 1) * (second.Count + 1) > int.MaxValue / 2)
                    return Fail(ContourLoftStatus.TooLarge, "m * n = " + ((long)first.Count * second.Count) + " is above MaxCells = " + o.MaxCells);

                Vector3d c1 = Centroid(first), c2 = Centroid(second);
                Vector3d n1 = Newell(first, c1), n2 = Newell(second, c2);
                if (n1.LengthSquared == 0 || !n1.IsFinite) return Fail(ContourLoftStatus.InvalidInput, "the first contour has zero area");
                if (n2.LengthSquared == 0 || !n2.IsFinite) return Fail(ContourLoftStatus.InvalidInput, "the second contour has zero area");
                if (!StrictlyOneSide(second, c1, n1) || !StrictlyOneSide(first, c2, n2))
                    return Fail(ContourLoftStatus.InvalidInput, "the contours' planes are not separated (a vertex lies on or across the other contour's plane)");

                swapped = second.Count > first.Count;
                IList<Vector3d> a = swapped ? second : first, b = swapped ? first : second;
                Vector3d na = swapped ? n2 : n1, nb = swapped ? n1 : n2, ca = swapped ? c2 : c1, cb = swapped ? c1 : c2;
                m = a.Count; n = b.Count;
                A = new Vector3d[m]; B = new Vector3d[n];
                for (int i = 0; i < m; ++i) A[i] = a[i];
                reversedB = na.Dot(nb) < 0;
                for (int j = 0; j < n; ++j) B[j] = b[reversedB ? (n - j) % n : j];

                // The band's internal triangles are down (A_i, A_i+1, B_j) and right (A_i, B_j+1, B_j): A forwards, B backwards. Closed by
                // cap A (A backwards, normal -na) and cap B, the solid is outward iff cap A's normal points away from B.
                sgn = na.Dot(cb - ca) > 0 ? 1 : -1;

                string costWhy = Evaluate(ca, cb, na);
                if (costWhy != null) return Fail(ContourLoftStatus.InvalidCost, costWhy);

                Search(out int best, out int count, out double cost, out byte[] steps);
                result.StartOffset = InputIndexB(best);
                result.TotalCost = cost;
                result.InadmissibleTriangles = count;
                if (count > 0)
                    return Fail(ContourLoftStatus.NoAdmissibleBand, "every band has an inadmissible triangle (at least " + count + ")");
                return Build(best, steps);
            }

            static string Validate(IList<Vector3d> c, string name)
            {
                if (c.Count < 3) return "the " + name + " contour has fewer than 3 vertices";
                for (int i = 0; i < c.Count; ++i) {
                    if (!c[i].IsFinite) return "the " + name + " contour has a vertex that is not finite (" + i + ")";
                    if (c[i] == c[(i + 1) % c.Count]) return "the " + name + " contour repeats a vertex (" + i + "; a closing vertex equal to the first is not allowed)";
                }
                return null;
            }

            static Vector3d Centroid(IList<Vector3d> c)
            {
                Vector3d s = Vector3d.Zero;
                for (int i = 0; i < c.Count; ++i) s += c[i];
                return s / c.Count;
            }

            // twice the area vector, relative to the centroid
            static Vector3d Newell(IList<Vector3d> c, Vector3d o)
            {
                Vector3d s = Vector3d.Zero;
                for (int i = 0; i < c.Count; ++i) s += (c[i] - o).Cross(c[(i + 1) % c.Count] - o);
                return s;
            }

            static bool StrictlyOneSide(IList<Vector3d> c, Vector3d o, Vector3d normal)
            {
                int side = 0;
                for (int i = 0; i < c.Count; ++i) {
                    double d = normal.Dot(c[i] - o);
                    int s = d > 0 ? 1 : d < 0 ? -1 : 0;
                    if (s == 0 || side != 0 && s != side) return false;
                    side = s;
                }
                return true;
            }

            int InputIndexB(int j) { j %= n; return reversedB ? (n - j) % n : j; }

            // the candidate triangle in input terms: down (A_i, A_i+1, B_j) or right (A_i, B_j+1, B_j)
            ContourLoftTriangle Candidate(bool down, int i, int j)
            {
                int ia = i % m, ib = j % n;
                var t = new ContourLoftTriangle();
                if (down) {
                    // edge of A, apex B_j
                    t.EdgeOnFirst = !swapped;
                    int ja = InputIndexB(ib);
                    if (swapped) { t.I = ja; t.J = ia; } else { t.I = ia; t.J = ja; }
                    t.A = A[ia]; t.B = A[(ia + 1) % m]; t.C = B[ib];
                } else {
                    // edge (B_j, B_j+1) of B, apex A_i; in input order the edge starts at the smaller index along the input
                    int u = InputIndexB(ib), v = InputIndexB(ib + 1);
                    int start = reversedB ? v : u;
                    t.EdgeOnFirst = swapped;
                    if (swapped) { t.I = start; t.J = ia; } else { t.I = ia; t.J = start; }
                    IList<Vector3d> cb = swapped ? first : second;
                    t.A = cb[start]; t.B = cb[(start + 1) % n]; t.C = A[ia];
                }
                return t;
            }

            string Evaluate(Vector3d ca, Vector3d cb, Vector3d na)
            {
                int cells = m * n;
                costDown = new double[cells]; costRight = new double[cells];
                badDown = new bool[cells]; badRight = new bool[cells];
                // the points the built-in costs see
                Vector3d[] PA = new Vector3d[m], PB = new Vector3d[n];
                if (o.NormalizeForCost) {
                    double sa = Math.Sqrt(Newell(A, ca).Length / 2), sb = Math.Sqrt(Newell(B, cb).Length / 2);
                    Vector3d up = na.Normalized * sgn;
                    for (int i = 0; i < m; ++i) PA[i] = (A[i] - ca) / sa;
                    for (int j = 0; j < n; ++j) PB[j] = (B[j] - cb) / sb + up;
                } else {
                    Vector3d c = Vector3d.Zero;
                    for (int i = 0; i < m; ++i) c += A[i];
                    for (int j = 0; j < n; ++j) c += B[j];
                    c /= (m + n);
                    for (int i = 0; i < m; ++i) PA[i] = A[i] - c;
                    for (int j = 0; j < n; ++j) PB[j] = B[j] - c;
                }
                double largest = 0;
                for (int i = 0; i < m; ++i) {
                    ct.ThrowIfCancellationRequested();
                    for (int j = 0; j < n; ++j) {
                        int k = i * n + j;
                        double cd, cr;
                        switch (o.Cost) {
                            case ContourLoftCost.MinimumArea:
                                cd = Area(PA[i], PA[(i + 1) % m], PB[j]);
                                cr = Area(PA[i], PB[(j + 1) % n], PB[j]);
                                break;
                            case ContourLoftCost.MaximumVolume:
                                cd = -sgn * Volume(PA[i], PA[(i + 1) % m], PB[j]);
                                cr = -sgn * Volume(PA[i], PB[(j + 1) % n], PB[j]);
                                break;
                            default:
                                cd = o.CustomCost(Candidate(true, i, j));
                                cr = o.CustomCost(Candidate(false, i, j));
                                break;
                        }
                        if (!MathUtil.IsFinite(cd) || !MathUtil.IsFinite(cr))
                            return "the cost of a triangle is not finite (row " + i + ", column " + j + ")";
                        costDown[k] = cd; costRight[k] = cr;
                        largest = Math.Max(largest, Math.Max(Math.Abs(cd), Math.Abs(cr)));
                        if (o.IsAdmissible != null) {
                            badDown[k] = !o.IsAdmissible(Candidate(true, i, j));
                            badRight[k] = !o.IsAdmissible(Candidate(false, i, j));
                        }
                    }
                }
                // a band sums m + n costs: bound every partial sum
                if (!MathUtil.IsFinite(largest * (m + n) * 2))
                    return "the costs are too large to be summed along a band";
                return null;
            }

            static double Area(Vector3d a, Vector3d b, Vector3d c) => 0.5 * (b - a).Cross(c - a).Length;
            static double Volume(Vector3d a, Vector3d b, Vector3d c) => a.Dot(b.Cross(c)) / 6;

            // ---- the search ----
            // Node (i, j): i in [0, m], j unrolled in [0, 2n]; start s: (0, s) -> (m, s + n), a down step (i, j) -> (i + 1, j) and a
            // right step (i, j) -> (i, j + 1). A path whose down steps (or right steps) form a single block repeats a spanning edge,
            // a fan of one vertex over the whole other contour: not a manifold band. Valid paths have at least 3 turns, tracked as
            // 10 states per node: the first step's direction f and the turns t, where 3 and 4 stand for any odd and even count
            // of at least 3 (the parity gives the last step's direction; st = 5 f + t).
            const int States = 10, FromStart = 10, None = 255;
            int[] spanLo, spanHi;            // (n + 1) x (m + 1): the row spans of the path of start s
            int[] boundLo, boundHi;          // per start: the starts whose paths bound its region (-1: the full region)
            byte[] pred;                      // (m + 1) x (n + 1) x 10: the predecessor's state, FromStart or None
            int[] cntPrev, cntCur;           // 10 x (n + 1), per state and column - s
            double[] dPrev, dCur;
            int[] regionL, regionR;

            static readonly int[] EndStates = { 3, 4, 8, 9 };
            static bool Less(int c1, double d1, int c2, double d2) => c1 < c2 || c1 == c2 && d1 < d2;
            static int LastDir(int st) { int f = st / 5, t = st % 5; return (t & 1) == 0 ? f : 1 - f; }
            static int Next(int st, int dir)
            {
                if (dir == LastDir(st)) return st;
                int f = st / 5, t = st % 5;
                return 5 * f + (t == 4 ? 3 : t + 1);
            }

            void Search(out int best, out int bestCount, out double bestCost, out byte[] steps)
            {
                spanLo = new int[(n + 1) * (m + 1)]; spanHi = new int[(n + 1) * (m + 1)];
                boundLo = new int[n + 1]; boundHi = new int[n + 1];
                pred = new byte[(m + 1) * (n + 1) * States];
                cntPrev = new int[States * (n + 1)]; cntCur = new int[States * (n + 1)];
                dPrev = new double[States * (n + 1)]; dCur = new double[States * (n + 1)];
                regionL = new int[m + 1]; regionR = new int[m + 1];
                for (int k = 0; k < cntPrev.Length; ++k) cntPrev[k] = cntCur[k] = int.MaxValue;
                var counts = new int[n]; var costs = new double[n];

                boundLo[0] = boundHi[0] = -1;
                Solve(0, out counts[0], out costs[0]);
                for (int i = 0; i <= m; ++i) {
                    spanLo[n * (m + 1) + i] = spanLo[i] + n;
                    spanHi[n * (m + 1) + i] = spanHi[i] + n;
                }
                // divide and conquer over the starts (an explicit stack, depth log n)
                var stack = new Stack<Index2i>();
                stack.Push(new Index2i(0, n));
                while (stack.Count > 0) {
                    Index2i r = stack.Pop();
                    if (r.b - r.a <= 1) continue;
                    int mid = (r.a + r.b) / 2;
                    boundLo[mid] = r.a; boundHi[mid] = r.b;
                    Solve(mid, out counts[mid], out costs[mid]);
                    stack.Push(new Index2i(mid, r.b));
                    stack.Push(new Index2i(r.a, mid));
                }
                best = 0;
                for (int s = 1; s < n; ++s)
                    if (Less(counts[s], costs[s], counts[best], costs[best])) best = s;
                bestCount = counts[best]; bestCost = costs[best];
                // the steps of the best band: its start solved again in the same region
                int endState = Solve(best, out _, out _);
                steps = Backtrack(best, endState);
            }

            void Region(int s)
            {
                int lo = boundLo[s], hi = boundHi[s];
                for (int i = 0; i <= m; ++i) {
                    int L = s, R = s + n;
                    if (lo >= 0) {
                        L = Math.Max(L, Math.Min(spanLo[lo * (m + 1) + i], spanLo[hi * (m + 1) + i]));
                        R = Math.Min(R, Math.Max(spanHi[lo * (m + 1) + i], spanHi[hi * (m + 1) + i]));
                    }
                    regionL[i] = L; regionR[i] = R;
                }
            }

            // the best valid path from (0, s) to (m, s + n) in the region of s (the full region if the bounded one holds none);
            // records its spans and returns its end state
            int Solve(int s, out int count, out double cost)
            {
                ct.ThrowIfCancellationRequested();
                Region(s);
                int end = SolveInRegion(s, out count, out cost);
                if (end < 0 && boundLo[s] >= 0) {
                    result.FullRegionFallbacks++;
                    boundLo[s] = boundHi[s] = -1;
                    Region(s);
                    end = SolveInRegion(s, out count, out cost);
                }
                if (end < 0) throw new InvalidOperationException("ContourLoft: no valid path for start " + s + " (internal error)");
                // spans of the path
                int baseIdx = s * (m + 1);
                for (int i = 0; i <= m; ++i) { spanLo[baseIdx + i] = int.MaxValue; spanHi[baseIdx + i] = int.MinValue; }
                int row = m, col = s + n, st = end;
                while (true) {
                    spanLo[baseIdx + row] = Math.Min(spanLo[baseIdx + row], col);
                    spanHi[baseIdx + row] = Math.Max(spanHi[baseIdx + row], col);
                    if (row == 0 && col == s) break;
                    int p = pred[((row * (n + 1)) + (col - s)) * States + st];
                    if (LastDir(st) == 0) row--; else col--;
                    st = p;
                }
                return end;
            }

            int SolveInRegion(int s, out int count, out double cost)
            {
                int w = n + 1;
                for (int i = 0; i <= m; ++i) {
                    int L = regionL[i], R = regionR[i];
                    int ia = i % m;
                    for (int j = L; j <= R; ++j) {
                        int c = j - s, cell = (i * w + c) * States;
                        for (int st = 0; st < States; ++st) pred[cell + st] = None;
                        // from above: a down step, triangle (A_i-1, A_i, B_j)
                        if (i > 0 && j >= regionL[i - 1] && j <= regionR[i - 1]) {
                            int k = (i - 1) * n + j % n;
                            int bad = badDown[k] ? 1 : 0; double w2 = costDown[k];
                            if (i - 1 == 0 && j == s) Offer(c, 0, bad, w2, FromStart, cell);
                            else
                                for (int st = 0; st < States; ++st)
                                    if (cntPrev[st * w + c] != int.MaxValue)
                                        Offer(c, Next(st, 0), cntPrev[st * w + c] + bad, dPrev[st * w + c] + w2, st, cell);
                        }
                        // from the left: a right step, triangle (A_i, B_j, B_j-1)
                        if (j > L) {
                            int k = ia * n + (j - 1) % n;
                            int bad = badRight[k] ? 1 : 0; double w2 = costRight[k];
                            if (i == 0 && j - 1 == s) Offer(c, 5, bad, w2, FromStart, cell);
                            else
                                for (int st = 0; st < States; ++st)
                                    if (cntCur[st * w + c - 1] != int.MaxValue)
                                        Offer(c, Next(st, 1), cntCur[st * w + c - 1] + bad, dCur[st * w + c - 1] + w2, st, cell);
                        }
                    }
                    // the current row becomes the previous one; cells outside the region stay unreachable
                    var ti = cntPrev; cntPrev = cntCur; cntCur = ti;
                    var td = dPrev; dPrev = dCur; dCur = td;
                    for (int k = 0; k < cntCur.Length; ++k) cntCur[k] = int.MaxValue;
                }
                // at (m, s + n): the states with at least 3 turns (first step down, then first step right)
                int end = -1; count = int.MaxValue; cost = 0;
                foreach (int st in EndStates) {
                    int cc = cntPrev[st * w + n];
                    if (cc == int.MaxValue) continue;
                    if (end < 0 || Less(cc, dPrev[st * w + n], count, cost)) { end = st; count = cc; cost = dPrev[st * w + n]; }
                }
                for (int k = 0; k < cntPrev.Length; ++k) cntPrev[k] = int.MaxValue;
                return end;
            }

            // a candidate for state st at column c of the current row (strictly better only: ties keep the earlier offer)
            void Offer(int c, int st, int cnt, double d, int from, int cell)
            {
                int w = n + 1, idx = st * w + c;
                if (cntCur[idx] == int.MaxValue || Less(cnt, d, cntCur[idx], dCur[idx])) {
                    cntCur[idx] = cnt; dCur[idx] = d; pred[cell + st] = (byte)from;
                }
            }

            // the steps of start s from the pred table of its last solve: 1 = down, 2 = right, in band order
            byte[] Backtrack(int s, int end)
            {
                var steps = new byte[m + n];
                int row = m, col = s + n, k = m + n, st = end;
                while (row > 0 || col > s) {
                    int p = pred[((row * (n + 1)) + (col - s)) * States + st];
                    if (LastDir(st) == 0) { steps[--k] = 1; row--; } else { steps[--k] = 2; col--; }
                    st = p;
                }
                if (k != 0 || row != 0 || col != s) throw new InvalidOperationException("ContourLoft: broken path (internal error)");
                return steps;
            }

            // ---- the band and its checks ----
            int VertexOfA(int i) { i %= m; return swapped ? first.Count + i : i; }
            int VertexOfB(int j) { int input = InputIndexB(j); return swapped ? input : first.Count + input; }

            ContourLoftResult Build(int s, byte[] steps)
            {
                var mesh = new DMesh3();
                foreach (var p in first) mesh.AppendVertex(p);
                foreach (var p in second) mesh.AppendVertex(p);
                int i = 0, j = s;
                foreach (byte st in steps) {
                    Index3i t = st == 1 ? new Index3i(VertexOfA(i), VertexOfA(i + 1), VertexOfB(j))
                                         : new Index3i(VertexOfA(i), VertexOfB(j + 1), VertexOfB(j));
                    if (st == 1) i++; else j++;
                    if (sgn < 0) t = new Index3i(t.a, t.c, t.b);
                    var tri = new Triangle3d(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref tri))
                        return Fail(ContourLoftStatus.InvalidInput, "a band triangle is degenerate (the contours are not in separated planes)");
                    if (mesh.AppendTriangle(t) < 0)
                        return Fail(ContourLoftStatus.InvalidInput, "the band is not a manifold (internal error or repeated vertices across the contours)");
                }
                string why = CheckBand(mesh);
                if (why != null)
                    return Fail(ContourLoftStatus.InvalidInput, why);
                if (o.CheckSelfIntersections && gs.MeshRepairMetrics.HasSelfIntersections(mesh))
                    return Fail(ContourLoftStatus.SelfIntersecting, "the optimal band intersects itself (dissimilar or offset contours?)");
                if (o.Caps) {
                    string capWhy = AddCap(mesh, first, 0) ?? AddCap(mesh, second, first.Count);
                    if (capWhy != null) return Fail(ContourLoftStatus.CapFailed, capWhy);
                    string clean = gs.MeshRobustRepair.NotCleanReason(mesh, o.CheckSelfIntersections);
                    if (clean == "self-intersections")
                        return Fail(ContourLoftStatus.SelfIntersecting, "the solid with its caps intersects itself");
                    if (clean != "")
                        return Fail(ContourLoftStatus.CapFailed, "the solid with its caps is not clean: " + clean);
                }
                result.Status = ContourLoftStatus.Success; result.Reason = ""; result.Mesh = mesh;
                return result;
            }

            // an annulus whose two boundary loops are the contours, edge for edge
            string CheckBand(DMesh3 mesh)
            {
                int boundary = 0;
                foreach (int e in mesh.EdgeIndices()) if (mesh.IsBoundaryEdge(e)) boundary++;
                if (boundary != m + n) return "the band has " + boundary + " boundary edges, not " + (m + n);
                for (int k = 0; k < 2; ++k) {
                    IList<Vector3d> c = k == 0 ? first : second;
                    int off = k == 0 ? 0 : first.Count;
                    for (int v = 0; v < c.Count; ++v) {
                        int e = mesh.FindEdge(off + v, off + (v + 1) % c.Count);
                        if (e < 0 || !mesh.IsBoundaryEdge(e)) return "a contour edge is not a boundary edge of the band";
                    }
                }
                if (mesh.VertexCount - mesh.EdgeCount + mesh.TriangleCount != 0) return "the band is not an annulus";
                foreach (int v in mesh.VertexIndices())
                    if (mesh.IsBowtieVertex(v)) return "the band has a bowtie vertex";
                if (gs.MeshRepairMetrics.InconsistentlyOrientedEdges(mesh) > 0) return "the band is not consistently oriented";
                return null;
            }

            // the planar cap of contour c (mesh vertices off .. off + count - 1), oriented against the band
            string AddCap(DMesh3 mesh, IList<Vector3d> c, int off)
            {
                int k = c.Count;
                Vector3d nrm = Newell(c, Centroid(c));
                int axis = Math.Abs(nrm.x) >= Math.Abs(nrm.y) && Math.Abs(nrm.x) >= Math.Abs(nrm.z) ? 0 : Math.Abs(nrm.y) >= Math.Abs(nrm.z) ? 1 : 2;
                int ax = (axis + 1) % 3, ay = (axis + 2) % 3;
                var input = new ConstrainedMesh2Input { Heights = new List<double>() };
                for (int v = 0; v < k; ++v) {
                    input.Points.Add(new Vector2d(c[v][ax], c[v][ay]));
                    input.Heights.Add(c[v][axis]);
                }
                var ring = new int[k];
                for (int v = 0; v < k; ++v) { ring[v] = v; input.Segments.Add(new Mesh2Segment(v, (v + 1) % k, 0, true)); }
                input.Rings.Add(new Mesh2Ring(ring));
                ConstrainedTriangulation2Result r;
                try {
                    r = (o.CapMesher ?? new QualityMesh2 { MinAngle = 0 }).Compute(input, ct);
                } catch (ArgumentException e) {
                    return "the cap mesher rejected the contour: " + e.Message;
                }
                if (r.Status != Triangulation2Status.Success)
                    return "the cap mesher failed: " + r.Status + " " + r.FailureKind + " " + r.FailureDetail;
                ConstrainedMesh2 cm = r.Mesh;
                // every contour vertex kept, at its position, and distinct
                var map = new int[cm.Vertices.Count];
                for (int v = 0; v < map.Length; ++v) map[v] = -1;
                for (int v = 0; v < k; ++v) {
                    int q = cm.InputToVertex[v];
                    if (q < 0 || map[q] >= 0 || cm.Vertices[q] != input.Points[v] || cm.Heights[q] != input.Heights[v])
                        return "the cap moved or merged a contour vertex (" + v + ")";
                    map[q] = off + v;
                }
                for (int q = 0; q < map.Length; ++q) {
                    if (map[q] >= 0) continue;
                    var p = new Vector3d();
                    p[ax] = cm.Vertices[q].x; p[ay] = cm.Vertices[q].y; p[axis] = cm.Heights[q];
                    map[q] = mesh.AppendVertex(p);
                }
                // orientation: against the band at the edge (off, off + 1)
                int e0 = mesh.FindEdge(off, off + 1);
                Index3i bt = mesh.GetTriangle(mesh.GetEdgeT(e0).a);
                bool bandForward = IndexUtil.find_tri_ordered_edge(off, off + 1, ref bt) != DMesh3.InvalidID;
                bool flip = false, found = false;
                foreach (var t in cm.Triangles) {
                    var tt = new Index3i(map[t.a], map[t.b], map[t.c]);
                    if (IndexUtil.find_tri_ordered_edge(off, off + 1, ref tt) != DMesh3.InvalidID) { found = true; flip = bandForward; break; }
                    if (IndexUtil.find_tri_ordered_edge(off + 1, off, ref tt) != DMesh3.InvalidID) { found = true; flip = !bandForward; break; }
                }
                if (!found) return "the cap does not contain the contour edge (0, 1)";
                foreach (var t in cm.Triangles) {
                    var tt = flip ? new Index3i(map[t.a], map[t.c], map[t.b]) : new Index3i(map[t.a], map[t.b], map[t.c]);
                    if (mesh.AppendTriangle(tt) < 0) return "a cap triangle does not fit the band (non-manifold)";
                }
                return null;
            }
        }
    }
}
