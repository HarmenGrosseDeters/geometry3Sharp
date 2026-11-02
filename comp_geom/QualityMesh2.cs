// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Quality mesh of a domain: the constrained Delaunay triangulation of <see cref="ConstrainedDelaunayMesh2"/>, refined by
    /// Delaunay refinement until every triangle meets the minimum angle, the maximum area and the size function, or the Steiner
    /// limit is reached. Conforming: every output triangle has an empty circumcircle, also across segments, except across
    /// locked segments, which are never split (spec O2, O9).
    /// </summary>
    /// <remarks>
    /// <para>Ruppert's algorithm (1995) with Shewchuk's refinements (2002): encroached subsegments are split first, a bad
    /// triangle gets a vertex at its circumcentre unless that would encroach a subsegment, which is then split instead; a
    /// subsegment at an input vertex is split at a power of two from it (concentric shells), so that small input angles do not
    /// make refinement run away. Triangles whose small angle lies between two constrained edges are not refined for their angle.
    /// Split points are computed on the original input segment, so they stay within rounding of it however often a piece is
    /// split (spec O12).</para>
    /// <para>Terminates: angles up to about 33 degrees converge on ordinary input; any input stops at the Steiner limit
    /// (default 10 x (points + segments) + 100 000, spec Q13). Pieces that are too short to split in doubles stay as they are.</para>
    /// <para>Locked segments (spec O9) keep their vertices and the mesh stays valid, but the bounds are not guaranteed near them
    /// yet: a triangle whose circumcentre lies behind a locked segment is left as it is, so its neighbourhood may keep triangles
    /// below the angle or above the area (listed in <see cref="ConstrainedMesh2.BadTriangles"/> when below the angle). Meeting
    /// the bounds there is the open work item m3.locks.</para>
    /// </remarks>
    public sealed class QualityMesh2
    {
        /// <summary> Minimum angle in degrees, at least 0 and below 60 (0: no angle bound). Default 20 (spec O3). </summary>
        public double MinAngle = 20;
        /// <summary> Maximum triangle area, or null. </summary>
        public double? MaxArea;
        /// <summary>
        /// Target size at a point: the longest edge of a triangle may be at most h(x, y) at its centroid (decision D9), or null. Must
        /// be deterministic and thread safe; it is combined with the other bounds by taking the stricter one.
        /// </summary>
        public Func<double, double, double> SizeFunction;
        /// <summary> Most Steiner points to insert, or null for 10 x (points + segments) + 100 000. </summary>
        public int? SteinerLimit;
        /// <summary> Conforming Delaunay even without bounds: split subsegments until none is encroached (spec O2). </summary>
        public bool Conforming;
        /// <summary> Split crossing constraints (default); off: a crossing is a failure. </summary>
        public bool ResolveCrossings = true;
        /// <summary>
        /// Snap tolerance in coordinate units (spec O11, decision D14), as <see cref="ConstrainedDelaunayMesh2.SnapTolerance"/>; 0: automatic
        /// only. Every vertex refinement adds is on the grid too (owner decision 2026-11-01): Steiner and split points are rounded to pixel
        /// centres when inserted; a triangle whose shortest edge is at most two pixels is not refined; a triangle whose rounded point is
        /// taken or within half a pixel of a constraint stays, and a subsegment without a valid split point on the grid stays; both are
        /// counted in the diagnostics.
        /// </summary>
        public double SnapTolerance;

        /// <exception cref="ArgumentException">An option or the input is invalid (an angle outside [0, 60), a non-positive area).</exception>
        public ConstrainedTriangulation2Result Compute(ConstrainedMesh2Input input, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (double.IsNaN(MinAngle) || MinAngle < 0 || MinAngle >= 60) throw new ArgumentException("MinAngle must be at least 0 and below 60 degrees (decision D12)");
            if (MaxArea != null && !(MaxArea.Value > 0) || MaxArea != null && double.IsInfinity(MaxArea.Value)) throw new ArgumentException("MaxArea must be positive and finite");
            if (SteinerLimit != null && SteinerLimit.Value < 0) throw new ArgumentException("SteinerLimit must not be negative");
            if (input.Regions != null)
                foreach (var r in input.Regions)
                    if (r != null && r.MaxArea != null && !(r.MaxArea.Value > 0)) throw new ArgumentException("a region's MaxArea must be positive");
            return ConstrainedDelaunayMesh2.Snapped(input, SnapTolerance, cancellationToken, (snapped, snap) => {
                var builder = new ConstrainedDelaunayMesh2.Builder(snapped, ResolveCrossings, cancellationToken);
                var refiner = new Refiner(this, builder, snap);
                builder.refine = refiner.Run;
                builder.IsBad = refiner.BelowAngle;
                return builder.Run();
            });
        }

        internal sealed class Refiner
        {
            readonly QualityMesh2 options;
            readonly ConstrainedDelaunayMesh2.Builder b;
            DelaunayKernel2 k;
            readonly double sin2Min;
            readonly Queue<long> segments = new Queue<long>();
            readonly Queue<int> triangles = new Queue<int>();
            readonly HashSet<long> unsplittable = new HashSet<long>();
            readonly HashSet<long> exempt = new HashSet<long>();
            long limit, inserted, splits;
            /// <summary> vertices below this id existed before refinement: input points and crossings, the apexes of input angles </summary>
            int firstSteiner;

            /// <summary> with a snap tolerance: the snapped input's grid, on which every vertex refinement adds must lie (owner decision
            /// 2026-11-01, MESHING2D_SNAP.md section 3g); null otherwise </summary>
            readonly SnapRounding2 snap;
            readonly SnapGrid grid;
            /// <summary> triangles whose rounded circumcentre was refused (its pixel holds a vertex or is crossed by a constraint) </summary>
            internal long GridBlocked;
            /// <summary> subsegments with no valid split point on the grid </summary>
            internal long GridUnsplittable;

            internal Refiner(QualityMesh2 options, ConstrainedDelaunayMesh2.Builder builder, SnapRounding2 snap = null)
            {
                this.options = options; b = builder; this.snap = snap; grid = snap?.Grid;
                double s = Math.Sin(options.MinAngle * Math.PI / 180);
                sin2Min = s * s;
            }

            static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;
            static long Key(int a, int c) => a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;

            bool InDomain(int t) => k.Tag[t] >= 0 && !k.IsGhost(t);

            internal ConstrainedTriangulation2Result Run(ConstrainedDelaunayMesh2.Builder builder)
            {
                k = b.k;
                int segmentCount = (b.input.Segments?.Count ?? 0);
                foreach (var r in b.input.Rings ?? new List<Mesh2Ring>()) segmentCount += r.Vertices.Length;
                limit = options.SteinerLimit ?? 10L * (b.n + segmentCount) + 100000;
                bool any = options.MinAngle > 0 || options.MaxArea != null || options.SizeFunction != null || HasRegionArea() || options.Conforming;
                if (!any) return null;
                var hullFailure = ConstrainHull();
                if (hullFailure != null) return hullFailure;
                firstSteiner = k.VertexCount;
                k.Dirty = new List<int>();
                for (int t = 0; t < k.TriangleSlots; ++t)
                    if (InDomain(t)) { triangles.Enqueue(t); QueueEdges(t); }
                long steps = 0;
                int sweepStart = k.VertexCount;
                while (true) {
                    if (segments.Count == 0 && triangles.Count == 0) {
                        // On the grid a refusal is not final: a split point or circumcentre refused because a constraint crossed its pixel
                        // becomes valid when that constraint is split and its pieces bend away (found by the grid fuzz test). When the
                        // queues drain after a sweep that inserted vertices, the refusals are dropped and everything is looked at again;
                        // a sweep without insertions ends refinement, so the counts reported are those of the final sweep. Each further
                        // sweep needs an insertion in the one before, and insertions are bounded by the grid and the Steiner limit.
                        if (grid == null || k.VertexCount == sweepStart || (unsplittable.Count == 0 && exempt.Count == 0)) break;
                        sweepStart = k.VertexCount;
                        unsplittable.Clear(); exempt.Clear();
                        GridBlocked = 0; GridUnsplittable = 0;
                        for (int t = 0; t < k.TriangleSlots; ++t)
                            if (InDomain(t)) { triangles.Enqueue(t); QueueEdges(t); }
                    }
                    if ((++steps & 255) == 0) b.cancel.ThrowIfCancellationRequested();
                    // every vertex made since the input counts, crossings included (O7: the limit counts every Steiner point of the run)
                    // (at the limit an insertion is refused and reports the limit; the queues then drain without inserting, so the flag
                    // is set only when a bound was left unmet, not on a mesh that needs nothing; review of M3)
                    inserted = k.VertexCount - (k.N + 1);
                    // every step either inserts, splits or retires an entry; a cap far above that only stops a defect, which is
                    // reported as one, not as the Steiner limit (review of M3)
                    if (steps > 64 * (limit + k.TriangleSlots) + 100000)
                        return b.Fail(Triangulation2FailureKind.RefinementLimit, null, "refinement did not settle within " + steps + " steps");
                    if (segments.Count > 0) {
                        long key = segments.Dequeue();
                        int e = k.FindEdge((int)(key >> 32), (int)(key & 0xffffffff));
                        if (e < 0 || k.Seg[e] < 0 || !Encroached(e)) continue;
                        var failure = SplitSegment(e);
                        if (failure != null) return failure;
                        continue;
                    }
                    int t0 = triangles.Dequeue();
                    if (!InDomain(t0) || !Bad(t0)) continue;
                    var fail = SplitTriangle(t0);
                    if (fail != null) return fail;
                }
                k.Dirty = null;
                if (grid != null && (GridBlocked > 0 || GridUnsplittable > 0))
                    b.diagnostics.Add("snap grid: " + GridBlocked + " triangles kept because their circumcentre's pixel is taken or crossed by a constraint, "
                        + GridUnsplittable + " subsegments with no split point on the grid");
                // the result is a constrained Delaunay triangulation in any case, also when the limit stopped refinement
                var bad = k.NonDelaunayEdges();
                if (bad.Count > 0) k.RestoreDelaunay(bad);
                return null;
            }

            bool HasRegionArea()
            {
                foreach (var r in b.input.Regions ?? new List<Mesh2Region>()) if (r.MaxArea != null) return true;
                return false;
            }

            /// <summary> without outer rings the hull is the boundary (D13): its edges become constraints, so it does not grow </summary>
            ConstrainedTriangulation2Result ConstrainHull()
            {
                if (b.outerRings > 0) return null;
                var pieces = new List<ConstrainedDelaunayMesh2.Builder.Piece>();
                for (int t = 0; t < k.TriangleSlots; ++t) {
                    if (!k.IsGhost(t)) continue;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.V[e] == k.Infinite || k.V[Next(e)] == k.Infinite || k.Seg[e] >= 0) continue;
                        // each hull edge its own source with its coordinates, so that splits are computed on the original edge
                        // e runs along the ghost, the domain on its right: stored reversed, the domain is on the left of A -> B
                        int a0 = k.V[Next(e)], b0 = k.V[e];
                        b.sources.Add(new ConstrainedDelaunayMesh2.Source { Ring = -1, Hull = true, AX = k.X[a0], AY = k.Y[a0], BX = k.X[b0], BY = k.Y[b0], VA = a0, VB = b0 });
                        pieces.Add(new ConstrainedDelaunayMesh2.Builder.Piece { U = a0, W = b0, Sources = new List<int> { b.sources.Count - 1 } });
                    }
                }
                // through the builder's piece insertion: a vertex within the tolerance of a hull edge becomes a vertex of the boundary
                // (O11; a point a few ulps inside a hull edge would otherwise encroach it for ever)
                var pending = new Stack<ConstrainedDelaunayMesh2.Builder.Piece>();
                long guard = 0;
                foreach (var piece in pieces) {
                    pending.Push(piece);
                    while (pending.Count > 0) {
                        if (++guard > 100000 + 16L * pieces.Count) return b.Fail(Triangulation2FailureKind.NodingLimit, null, "the hull edges did not settle");
                        var failure = b.InsertPiece(pending.Pop(), pending);
                        if (failure != null) return failure;
                    }
                }
                // what lies between such a boundary chain and the old hull edge is outside: reachable from a ghost without crossing a constraint
                var queue = new Queue<int>();
                for (int t = 0; t < k.TriangleSlots; ++t) if (k.IsGhost(t)) queue.Enqueue(t);
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || k.Tag[t2] < 0) continue;
                        k.Tag[t2] = -1;
                        queue.Enqueue(t2);
                    }
                }
                return null;
            }

            void QueueEdges(int t)
            {
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (k.Seg[e] >= 0) segments.Enqueue(Key(k.V[e], k.V[Next(e)]));
                }
            }

            /// <summary> the piece a split lifted: its sources, its ends, and the tags on its left and right before the split </summary>
            struct LiftedPiece
            {
                internal List<int> Sources;
                internal Vector2d U, W;
                internal int Left, Right;
            }

            int TagOf(int t) => k.IsGhost(t) ? -1 : k.Tag[t];

            /// <summary> the tag the triangle of half-edge h takes from the lifted piece, if h lies on it; int.MinValue otherwise </summary>
            int TagFromPiece(int h, LiftedPiece? lifted)
            {
                if (lifted == null || k.Seg[h] < 0) return int.MinValue;
                var piece = lifted.Value;
                // only an edge with exactly the piece's sources: one that also carries another constraint (a split point that rounded
                // onto a constraint through the same end, at a tiny angle) closed the wedge between them, and its sides need not have
                // the tags the piece's sides had (found with the corpus run, earcut's water-huge)
                var own = k.Sources[k.Seg[h]];
                if (own.Count != piece.Sources.Count) return int.MinValue;
                foreach (int s in own) if (!piece.Sources.Contains(s)) return int.MinValue;
                // on the lifted piece: both ends between its ends (the pieces of a split lie on it, the other pieces of its source do not)
                // (in a scale by a power of two: the squares do not underflow at small magnitudes)
                var d0 = piece.W - piece.U;
                int ex = ScaleOf(d0.x, d0.y, 0, 0);
                var d = ScaleB(d0, -ex); double l2 = d.LengthSquared;
                var a = ScaleB(k.Point(k.V[h]) - piece.U, -ex); var c = ScaleB(k.Point(k.V[Next(h)]) - piece.U, -ex);
                double ta = a.Dot(d), tc = c.Dot(d);
                double slack = l2 * 1e-9;
                if (ta < -slack || tc < -slack || ta > l2 + slack || tc > l2 + slack) return int.MinValue;
                return (c - a).Dot(d) > 0 ? piece.Left : piece.Right;
            }

            /// <summary>
            /// tests only: after every operation compare the tags with domain membership computed from scratch (slow). Per thread, so
            /// that a test switching it on does not reach meshers that other tests run in parallel (refinement runs on the calling thread)
            /// </summary>
            [ThreadStatic] internal static bool CheckTagsForTests;
            string lastOperation;
            long operations;

            void CheckTags()
            {
                ++operations;
                if (!CheckTagsForTests || (b.input.HoleSeeds?.Count ?? 0) > 0) return;
                var inside = b.InsideFromScratch();
                for (int t = 0; t < inside.Length; ++t)
                    if (!k.IsGhost(t) && (k.Tag[t] >= 0) != inside[t])
                        throw new InvalidOperationException("tag of triangle " + t + " (" + string.Join(" ", new[] { k.V[3 * t], k.V[3 * t + 1], k.V[3 * t + 2] }.Select(q => q + "=(" + k.X[q].ToString("R") + ", " + k.Y[q].ToString("R") + ")")) + ") is " + k.Tag[t]
                            + ", inside " + inside[t] + ", edges constrained " + k.Seg[3 * t] + " " + k.Seg[3 * t + 1] + " " + k.Seg[3 * t + 2]
                            + ", neighbour tags " + TagOf(k.H[3 * t] / 3) + " " + TagOf(k.H[3 * t + 1] / 3) + " " + TagOf(k.H[3 * t + 2] / 3)
                            + ", after operation " + operations + ": " + lastOperation);
            }

            /// <summary> after an insertion: the rewritten triangles may be bad, their constrained edges encroached </summary>
            void Collect(LiftedPiece? lifted = null)
            {
                // Tags change only across constraints. A split briefly lifts the constraint of the piece it splits, and the flips around
                // it may then cross the boundary, so the rewritten triangles take their tags anew: from the unchanged triangles they are
                // joined to by unconstrained edges (a breadth-first search into the rewritten region), and from the sides of the
                // pieces of the lifted piece, which keep the tags its sides had (a small hole or region enclosed by constraints has no
                // unchanged neighbour through an unconstrained edge; found by the review of M3). Any other rewritten triangle keeps its tag.
                var previous = new Dictionary<int, int>();
                foreach (int t in k.Dirty) if (!k.IsGhost(t) && !previous.ContainsKey(t)) { previous.Add(t, k.Tag[t]); k.Tag[t] = int.MinValue; }
                var queue = new Queue<int>();
                foreach (int t in previous.Keys)
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        int fromPiece = TagFromPiece(e, lifted);
                        if (fromPiece != int.MinValue) {
                            if (k.Tag[t] == int.MinValue) { k.Tag[t] = fromPiece; queue.Enqueue(t); }
                            continue;
                        }
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.Tag[t2] == int.MinValue) continue;
                        // a ghost counts as an unchanged neighbour outside: a sliver left between a rerouted boundary piece and the old,
                        // now unconstrained hull edge is outside the domain (found with M3, F-39)
                        int tag = k.IsGhost(t2) ? -1 : k.Tag[t2];
                        if (k.Tag[t] == int.MinValue) { k.Tag[t] = tag; queue.Enqueue(t); }
                    }
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || k.Tag[t2] != int.MinValue) continue;
                        k.Tag[t2] = k.Tag[t];
                        queue.Enqueue(t2);
                    }
                }
                foreach (var pair in previous) if (k.Tag[pair.Key] == int.MinValue) k.Tag[pair.Key] = pair.Value;
                CheckTags();
                foreach (int t in k.Dirty) {
                    if (k.IsGhost(t)) continue;
                    triangles.Enqueue(t);
                    QueueEdges(t);
                }
                k.Dirty.Clear();
            }

            bool Locked(int e)
            {
                foreach (int s in k.Sources[k.Seg[e]]) if (b.sources[s].Locked) return true;
                return false;
            }

            /// <summary> a vertex of a triangle in the domain at the edge lies strictly inside its diametral circle (exact) </summary>
            bool Encroached(int e)
            {
                if (Locked(e) || unsplittable.Contains(Key(k.V[e], k.V[Next(e)]))) return false;
                foreach (int h in new[] { e, k.H[e] }) {
                    int t = h / 3;
                    // conforming (O2) asks for empty circumcircles against all vertices, so a vertex of a hole or of the outside
                    // encroaches too
                    if (options.Conforming ? k.IsGhost(t) : !InDomain(t)) continue;
                    int apex = k.V[Next(Next(h))];
                    if (DotSign(k.Point(k.V[h]), k.Point(k.V[Next(h)]), k.Point(apex)) < 0) return true;
                }
                return false;
            }

            /// <summary> sign of (a - p) . (b - p), exact: a floating-point filter, then integers </summary>
            internal static int DotSign(Vector2d a, Vector2d c, Vector2d p)
            {
                double ax = a.x - p.x, ay = a.y - p.y, cx = c.x - p.x, cy = c.y - p.y;
                double l = ax * cx, r = ay * cy, d = l + r;
                double bound = (Math.Abs(l) + Math.Abs(r)) * 4.5e-16;   // 2 roundings of the differences, 3 of the products and the sum
                if (d > bound && Math.Abs(d) > 1e-290) return 1;
                if (-d > bound && Math.Abs(d) > 1e-290) return -1;
                double[] v = { a.x, a.y, c.x, c.y, p.x, p.y };
                var m = new BigInteger[6];
                var ex = new int[6];
                int emin = int.MaxValue;
                for (int i = 0; i < 6; ++i) { ExactPoint3.Decompose(v[i], out m[i], out ex[i]); if (!m[i].IsZero) emin = Math.Min(emin, ex[i]); }
                if (emin == int.MaxValue) return 0;
                var q = new BigInteger[6];
                for (int i = 0; i < 6; ++i) q[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - emin);
                return ((q[0] - q[4]) * (q[2] - q[4]) + (q[1] - q[5]) * (q[3] - q[5])).Sign;
            }

            // ------------------------------------------------------------ bad triangles

            /// <summary> floor(log2 |x|) for finite x != 0, from the bits </summary>
            internal static int Exponent(double x)
            {
                long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
                int e = (int)((bits >> 52) & 0x7FF);
                if (e != 0) return e - 1023;
                bits = BitConverter.DoubleToInt64Bits(Math.Abs(x) * 18446744073709551616.0);   // subnormal: times 2^64 (exact)
                return (int)((bits >> 52) & 0x7FF) - 1023 - 64;
            }

            /// <summary> the exponent that brings the largest of the values to [1, 2), 0 if all are zero </summary>
            static int ScaleOf(double a, double b, double c, double d)
            {
                double m = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
                return m > 0 ? Exponent(m) : 0;
            }

            static Vector2d ScaleB(Vector2d v, int n) => new Vector2d(ExactPoint3.ScaleB(v.x, n), ExactPoint3.ScaleB(v.y, n));

            /// <summary>
            /// The triangle's edges from its first vertex, scaled by a power of two to unit size: the products of the angle and
            /// circumcentre computations then neither underflow nor overflow, and an input scaled by 2^k gives the same values bit
            /// for bit (review of M3: at 2^-500 the squared lengths underflowed and no triangle counted as bad)
            /// </summary>
            Vector2d Local(int t, out Vector2d q, out Vector2d r, out int ex)
            {
                var a = k.Point(k.V[3 * t]);
                var d1 = k.Point(k.V[3 * t + 1]) - a; var d2 = k.Point(k.V[3 * t + 2]) - a;
                ex = ScaleOf(d1.x, d1.y, d2.x, d2.y);
                q = ScaleB(d1, -ex); r = ScaleB(d2, -ex);
                return a;
            }

            /// <summary> the area of the scaled triangle of <see cref="Local"/> </summary>
            static double LocalArea(Vector2d q, Vector2d r) => 0.5 * (q.x * r.y - q.y * r.x);

            double Area(int t)
            {
                Local(t, out var q, out var r, out int ex);
                return ExactPoint3.ScaleB(LocalArea(q, r), 2 * ex);
            }

            /// <summary> sin^2 of the smallest angle, and the corner it is at </summary>
            double MinSin2(int t, out int corner)
            {
                double best = double.MaxValue; corner = 0;
                Local(t, out var q0, out var r0, out _);
                var corners = new[] { Vector2d.Zero, q0, r0 };
                double area2 = 2 * LocalArea(q0, r0);
                for (int j = 0; j < 3; ++j) {
                    var p = corners[j]; var q = corners[(j + 1) % 3]; var r = corners[(j + 2) % 3];
                    double l1 = (q - p).LengthSquared, l2 = (r - p).LengthSquared;
                    double s2 = area2 * area2 / (l1 * l2);
                    if (s2 < best) { best = s2; corner = j; }
                }
                return best;
            }

            /// <summary> for the output's list of bad triangles: below the minimum angle </summary>
            internal bool BelowAngle(int t) => options.MinAngle > 0 && MinSin2(t, out _) < sin2Min;

            /// <summary> the squared length of the longest locked edge of t, in the scale of <see cref="Local"/> </summary>
            double LongestLocked2(int t, Vector2d q, Vector2d r)
            {
                double best = 0;
                var corners = new[] { Vector2d.Zero, q, r };
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (k.Seg[e] >= 0 && Locked(e)) best = Math.Max(best, (corners[(j + 1) % 3] - corners[j]).LengthSquared);
                }
                return best;
            }

            bool Bad(int t)
            {
                if (exempt.Contains(TriangleKey(t))) return false;
                // on the grid: at two pixels or less no better point can be placed, so neither angle nor size is refined (section 3g)
                if (grid != null && BelowFloor(t)) return false;
                var a0 = Local(t, out var q0, out var r0, out int ex);
                double localArea = LocalArea(q0, r0), area = ExactPoint3.ScaleB(localArea, 2 * ex);
                double localLocked2 = LongestLocked2(t, q0, r0), locked2 = ExactPoint3.ScaleB(localLocked2, 2 * ex);
                // a triangle over an edge that may not be split need not shrink below a needle (O5, O9)
                bool areaExempt = localLocked2 > 4 * localArea;
                double? maxArea = options.MaxArea;
                if (k.Tag[t] > 0) {
                    var ra = b.input.Regions[k.Tag[t] - 1].MaxArea;
                    if (ra != null) maxArea = maxArea == null ? ra : Math.Min(maxArea.Value, ra.Value);
                }
                // (a margin of 2^-40 below the bound: the area in doubles may round down below it while the exact area is above)
                if (maxArea != null && area > maxArea.Value * (1 - 1.0 / (1L << 40)) && !areaExempt) return true;
                if (options.SizeFunction != null) {
                    var a = k.Point(k.V[3 * t]); var c = k.Point(k.V[3 * t + 1]); var d = k.Point(k.V[3 * t + 2]);
                    var g = (a + c + d) / 3;
                    double h = options.SizeFunction(g.x, g.y);
                    double longest = ExactPoint3.ScaleB(Math.Max(q0.LengthSquared, Math.Max((r0 - q0).LengthSquared, r0.LengthSquared)), 2 * ex);
                    if (longest > Math.Max(h * h, locked2) * (1 - 1.0 / (1L << 40))) return true;
                }
                if (options.MinAngle <= 0) return false;
                if (MinSin2(t, out int corner) >= sin2Min) return false;
                // the small angle lies between two constrained edges: forced by the input (O3)
                int ea = 3 * t + corner, eb = 3 * t + (corner + 2) % 3;
                if (k.Seg[ea] >= 0 && k.Seg[eb] >= 0) return false;
                // the shortest edge spans a small input angle: its ends lie on two input segments that meet at an angle below the
                // bound, at about the same distance from where they meet. Such a triangle cannot be improved without refining towards
                // the apex for ever (Shewchuk 2002, small input angles)
                if (SpansSmallAngle(k.V[3 * t + (corner + 1) % 3], k.V[3 * t + (corner + 2) % 3])) return false;
                return true;
            }

            /// <summary>
            /// benchmarks only (Meshing2Benchmark gridcounts): the floor in pixels instead of two, 0 for none, to measure what the floor
            /// saves (MESHING2D_SNAP.md section 3g, item 6). Per thread, like the tag check switch
            /// </summary>
            [ThreadStatic] internal static double? FloorPixelsForBenchmarks;

            /// <summary> the shortest edge of t is at most two pixels (the angle floor on the grid, MESHING2D_SNAP.md section 3g) </summary>
            bool BelowFloor(int t)
            {
                Local(t, out var q, out var r, out int ex);
                double shortest = Math.Min(q.LengthSquared, Math.Min((r - q).LengthSquared, r.LengthSquared));
                double floor = ExactPoint3.ScaleB((FloorPixelsForBenchmarks ?? 2) * grid.W, -ex);
                return shortest <= floor * floor;
            }

            /// <summary> a constrained edge crosses the open pixel of grid point c (c lies in triangle at): a vertex there would sit within half a
            /// pixel of a constraint (D14) </summary>
            bool ConstraintInPixel(int at, Vector2d c)
            {
                long kx = grid.Pixel(c.x), ky = grid.Pixel(c.y);
                double h = grid.W / 2;
                foreach (int t in TrianglesNear(at, c.x - h, c.y - h, c.x + h, c.y + h))
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0 && grid.CrossesInterior(k.Point(k.V[e]), k.Point(k.V[Next(e)]), kx, ky)) return true;
                    }
                return false;
            }

            /// <summary> the real triangles whose bounding box meets the box, found from triangle t0 across all edges </summary>
            List<int> TrianglesNear(int t0, double x0, double y0, double x1, double y1)
            {
                var result = new List<int>();
                var seen = new HashSet<int> { t0 };
                var stack = new Stack<int>();
                stack.Push(t0);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    if (k.IsGhost(t)) continue;
                    double lx = double.MaxValue, ly = double.MaxValue, hx = double.MinValue, hy = double.MinValue;
                    for (int j = 0; j < 3; ++j) { var p = k.Point(k.V[3 * t + j]); lx = Math.Min(lx, p.x); ly = Math.Min(ly, p.y); hx = Math.Max(hx, p.x); hy = Math.Max(hy, p.y); }
                    if (hx < x0 || lx > x1 || hy < y0 || ly > y1) continue;
                    result.Add(t);
                    for (int j = 0; j < 3; ++j) { int t2 = k.H[3 * t + j] / 3; if (seen.Add(t2)) stack.Push(t2); }
                }
                return result;
            }

            /// <summary>
            /// The split point of constrained edge u-w on the grid (MESHING2D_SNAP.md section 3g): the point at the given fraction between
            /// u and w is taken on the input segment the edge comes from (O12), and its pixel's centre is used if it is valid; else the
            /// neighbouring pixels of the input segment's envelope between u and w, nearest first. Valid: no constraint crosses its pixel,
            /// and the new edges u-q, q-w cross no vertex's pixel and no other constraint. A vertex already at q is fine: the chain is
            /// then routed through it, as snap rounding routes a segment through every hot pixel of its envelope (a Steiner point there
            /// would otherwise block the split and leave the subsegment encroached; found by the fuzz test).
            /// </summary>
            bool GridSplitPoint(int e, Vector2d pu, Vector2d pw, List<int> src, double fraction, out Vector2d q)
            {
                q = default(Vector2d);
                // the input segments of all sources of the piece (snapped chains of several input segments can share it): the split point
                // must lie in every one's envelope, so that no chain leaves its own (found by the fuzz test)
                var origins = new List<(Vector2d a, Vector2d b)>();
                foreach (int si in src) {
                    var ss = b.sources[si];
                    var all = snap.Origins(new Vector2d(ss.AX, ss.AY), new Vector2d(ss.BX, ss.BY));
                    if (all.Count == 0) all = new List<(Vector2d a, Vector2d b)> { (new Vector2d(ss.AX, ss.AY), new Vector2d(ss.BX, ss.BY)) };
                    foreach (var o in all) if (!origins.Contains(o)) origins.Add(o);
                }
                Vector2d A = origins[0].a, B = origins[0].b;
                bool InAllEnvelopes(long px, long py, bool own)
                {
                    for (int i = own ? 0 : 1; i < origins.Count; ++i) if (!grid.CrossesInterior(origins[i].a, origins[i].b, px, py) && !OnPixelCentre(origins[i], px, py)) return false;
                    return true;
                }
                var dir = B - A;
                int es = ScaleOf(dir.x, dir.y, 0, 0);
                var dirs = ScaleB(dir, -es); double l2 = dirs.LengthSquared;
                if (!(l2 > 0)) return false;
                double Along(Vector2d p) => ScaleB(p - A, -es).Dot(dirs) / l2;
                double tu = Along(pu), tw = Along(pw), lo = Math.Min(tu, tw), hi = Math.Max(tu, tw);
                var target = A + dir * (tu + (tw - tu) * fraction);
                long kx = grid.Pixel(target.x), ky = grid.Pixel(target.y);
                var candidates = new List<(double d, Vector2d c)>();
                for (long dx = -1; dx <= 1; ++dx)
                    for (long dy = -1; dy <= 1; ++dy) {
                        var c = new Vector2d(grid.Centre(kx + dx), grid.Centre(ky + dy));
                        double tc = Along(c);
                        if (!(tc > lo && tc < hi)) continue;
                        if ((dx != 0 || dy != 0) && !grid.CrossesInterior(A, B, kx + dx, ky + dy)) continue;
                        if (!InAllEnvelopes(kx + dx, ky + dy, false)) continue;
                        candidates.Add((ScaleB(c - target, -es).LengthSquared, c));
                    }
                candidates.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.c.x != c.c.x ? a.c.x.CompareTo(c.c.x) : a.c.y.CompareTo(c.c.y));
                foreach (var (_, c) in candidates)
                    if (c != pu && c != pw && ValidGridSplit(e, pu, pw, c)) { q = c; return true; }
                // none next to the target (vertices crowd it): the other pixels of the envelope between u and w, nearest to the target
                // first, sampled every half pixel along the input segment (at most 256 samples; found by the fuzz test on a long subsegment)
                var seenPixels = new HashSet<(long, long)>();
                for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) seenPixels.Add((kx + dx, ky + dy));
                var wider = new List<(double d, Vector2d c)>();
                double span = Math.Abs(tw - tu) * Math.Sqrt(dir.LengthSquared);
                int samples = (int)Math.Min(256, Math.Max(2, Math.Ceiling(2 * span / grid.W)));
                for (int i = 1; i < samples; ++i) {
                    var sp = A + dir * (tu + (tw - tu) * i / samples);
                    long sx = grid.Pixel(sp.x), sy = grid.Pixel(sp.y);
                    for (long dx = -1; dx <= 1; ++dx)
                        for (long dy = -1; dy <= 1; ++dy) {
                            if (!seenPixels.Add((sx + dx, sy + dy))) continue;
                            var c = new Vector2d(grid.Centre(sx + dx), grid.Centre(sy + dy));
                            double tc = Along(c);
                            if (!(tc > lo && tc < hi) || !grid.CrossesInterior(A, B, sx + dx, sy + dy) || !InAllEnvelopes(sx + dx, sy + dy, false)) continue;
                            wider.Add((ScaleB(c - target, -es).LengthSquared, c));
                        }
                }
                wider.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.c.x != c.c.x ? a.c.x.CompareTo(c.c.x) : a.c.y.CompareTo(c.c.y));
                foreach (var (_, c) in wider)
                    if (c != pu && c != pw && ValidGridSplit(e, pu, pw, c)) { q = c; return true; }
                return false;
            }

            /// <summary> the input segment passes through the centre of pixel (px, py) (a degenerate envelope contact, eg along a grid line) </summary>
            bool OnPixelCentre((Vector2d a, Vector2d b) o, long px, long py)
            {
                var c = new Vector2d(grid.Centre(px), grid.Centre(py));
                return ExactPredicates.Orient2d(o.a, o.b, c) == 0 && c.x >= Math.Min(o.a.x, o.b.x) && c.x <= Math.Max(o.a.x, o.b.x) && c.y >= Math.Min(o.a.y, o.b.y) && c.y <= Math.Max(o.a.y, o.b.y);
            }

            bool ValidGridSplit(int e, Vector2d pu, Vector2d pw, Vector2d q)
            {
                double h = grid.W;
                double x0 = Math.Min(q.x, Math.Min(pu.x, pw.x)) - h, y0 = Math.Min(q.y, Math.Min(pu.y, pw.y)) - h;
                double x1 = Math.Max(q.x, Math.Max(pu.x, pw.x)) + h, y1 = Math.Max(q.y, Math.Max(pu.y, pw.y)) + h;
                long qx = grid.Pixel(q.x), qy = grid.Pixel(q.y);
                var near = TrianglesNear(e / 3, x0, y0, x1, y1);
                var seen = new HashSet<int>();
                foreach (int t in near)
                    for (int j = 0; j < 3; ++j) {
                        int f = 3 * t + j, v = k.V[f];
                        var pv = k.Point(v);
                        if (seen.Add(v) && pv != pu && pv != pw && pv != q) {
                            long vx = grid.Pixel(pv.x), vy = grid.Pixel(pv.y);
                            if (grid.CrossesInterior(pu, q, vx, vy) || grid.CrossesInterior(q, pw, vx, vy)) return false;
                        }
                        if (k.Seg[f] < 0 || f == e || f == k.H[e]) continue;
                        var a = pv; var c = k.Point(k.V[Next(f)]);
                        // (a constraint ending at a vertex already at q leaves its pixel there: not a crossing)
                        if (a != q && c != q && grid.CrossesInterior(a, c, qx, qy)) return false;
                        if (ProperCross(pu, q, a, c) || ProperCross(q, pw, a, c)) return false;
                    }
                return true;
            }

            static bool ProperCross(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                if (a == c || a == d || b == c || b == d) return false;
                int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
                int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
                return o1 * o2 < 0 && o3 * o4 < 0;
            }

            bool SpansSmallAngle(int p, int q)
            {
                var sp = SourcesAt(p); var sq = SourcesAt(q);
                if (sp.Count == 0 || sq.Count == 0) return false;
                var P = k.Point(p); var Qv = k.Point(q);
                foreach (int a in sp)
                    foreach (int c in sq) {
                        if (a == c) continue;
                        var s1 = b.sources[a]; var s2 = b.sources[c];
                        // on the grid the input angles are those of the input segments, not of their snapped chains (the oracle judges the
                        // input; a chain bends by up to half a pixel)
                        if (grid != null) {
                            s1 = OriginOf(s1); s2 = OriginOf(s2);
                            if (s1.AX == s2.AX && s1.AY == s2.AY && s1.BX == s2.BX && s1.BY == s2.BY) continue;
                        }
                        // all in a common scale by a power of two (no underflow at small magnitudes, the same bits at any 2^k)
                        double d1x = s1.BX - s1.AX, d1y = s1.BY - s1.AY, d2x = s2.BX - s2.AX, d2y = s2.BY - s2.AY, ox = s2.AX - s1.AX, oy = s2.AY - s1.AY;
                        int ex = Math.Max(ScaleOf(d1x, d1y, d2x, d2y), ScaleOf(ox, oy, 0, 0));
                        d1x = ExactPoint3.ScaleB(d1x, -ex); d1y = ExactPoint3.ScaleB(d1y, -ex); d2x = ExactPoint3.ScaleB(d2x, -ex); d2y = ExactPoint3.ScaleB(d2y, -ex);
                        ox = ExactPoint3.ScaleB(ox, -ex); oy = ExactPoint3.ScaleB(oy, -ex);
                        double cross = d1x * d2y - d1y * d2x, l1 = Math.Sqrt(d1x * d1x + d1y * d1y), l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                        if (!(Math.Abs(cross) > 0) || !(l1 > 0) || !(l2 > 0)) continue;
                        double sin = Math.Abs(cross) / (l1 * l2);
                        if (sin * sin >= sin2Min * (1 - 1e-9)) continue;
                        // where the lines meet
                        double t1 = (ox * d2y - oy * d2x) / cross;
                        double t2 = (ox * d1y - oy * d1x) / cross;
                        // the apex is on both segments: an input angle, not two lines that would meet far away
                        // (slightly stricter than the oracle's exact test, so that rounding never exempts what it would not)
                        if (t1 < 1e-12 && t1 != 0 || t1 > 1 || t2 < 1e-12 && t2 != 0 || t2 > 1 || t1 < 0 || t2 < 0) continue;
                        // p, q and the apex relative to s1's start, in the common scale
                        var A = new Vector2d(t1 * d1x, t1 * d1y);
                        var s1A = new Vector2d(s1.AX, s1.AY);
                        var Pl = ScaleB(P - s1A, -ex) - A; var Ql = ScaleB(Qv - s1A, -ex) - A;
                        double rp = Pl.LengthSquared, rq = Ql.LengthSquared;
                        if (!(rp > 0) || !(rq > 0) || rp > 2.24 * rq || rq > 2.24 * rp) continue;
                        // and p, q lie on the same side of the apex along their lines
                        if (Pl.Dot(Ql) <= 0) continue;
                        return true;
                    }
                return false;
            }

            /// <summary> the input segment a snapped source comes from, as a source with its coordinates (itself if unknown) </summary>
            ConstrainedDelaunayMesh2.Source OriginOf(ConstrainedDelaunayMesh2.Source s)
            {
                if (!snap.TryOrigin(new Vector2d(s.AX, s.AY), new Vector2d(s.BX, s.BY), out var o)) return s;
                s.AX = o.a.x; s.AY = o.a.y; s.BX = o.b.x; s.BY = o.b.y;
                return s;
            }

            List<int> SourcesAt(int v)
            {
                var result = new List<int>();
                foreach (int e in k.Star(v)) {
                    int c = k.Seg[e];
                    if (c < 0) continue;
                    foreach (int src in k.Sources[c]) if (!b.sources[src].Hull && !result.Contains(src)) result.Add(src);
                }
                return result;
            }

            long TriangleKey(int t)
            {
                int a = k.V[3 * t], c = k.V[3 * t + 1], d = k.V[3 * t + 2];
                // the triangle's vertex set, order-free, in 64 bits (collisions only make a triangle exempt that is not)
                ulong h = (ulong)Math.Min(a, Math.Min(c, d)) * 0x9E3779B97F4A7C15UL ^ (ulong)Math.Max(a, Math.Max(c, d)) * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(a + c + d);
                return (long)h;
            }

            // ------------------------------------------------------------ splitting

            /// <summary> the circumcentre of t, or false if it cannot be computed in doubles </summary>
            bool Circumcentre(int t, out Vector2d c)
            {
                var a = Local(t, out var p, out var q, out int ex);
                double bx = p.x, by = p.y, cx = q.x, cy = q.y;
                double d = 2 * (bx * cy - by * cx);
                double b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                double ux = (cy * b2 - by * c2) / d, uy = (bx * c2 - cx * b2) / d;
                c = new Vector2d(a.x + ExactPoint3.ScaleB(ux, ex), a.y + ExactPoint3.ScaleB(uy, ex));
                return d != 0 && !double.IsNaN(c.x) && !double.IsNaN(c.y) && !double.IsInfinity(c.x) && !double.IsInfinity(c.y)
                    && Math.Abs(c.x) <= DelaunayMesh2.CoordinateLimit && Math.Abs(c.y) <= DelaunayMesh2.CoordinateLimit;
            }

            ConstrainedTriangulation2Result SplitTriangle(int t)
            {
                if (inserted >= limit) { b.SteinerLimitReached = true; return null; }
                if (!Circumcentre(t, out var c)) { exempt.Add(TriangleKey(t)); return null; }
                // on the grid the point is the centre of the circumcentre's pixel, decided exactly (rounded at insertion, never moved later)
                if (grid != null && !grid.CircumcentrePixelCentre(k.Point(k.V[3 * t]), k.Point(k.V[3 * t + 1]), k.Point(k.V[3 * t + 2]), out c)) { exempt.Add(TriangleKey(t)); return null; }
                // walk from t towards c; a constrained edge on the way is split instead (it is encroached, or c is outside)
                int at = Walk(t, c, out int blocked);
                if (blocked >= 0) {
                    if (k.Seg[blocked] < 0) { exempt.Add(TriangleKey(t)); return null; }   // (the domain is bounded by constraints: not expected)
                    if (Locked(blocked) || unsplittable.Contains(Key(k.V[blocked], k.V[Next(blocked)]))) { exempt.Add(TriangleKey(t)); return null; }
                    return SplitSegment(blocked);
                }
                // c would encroach a subsegment of its cavity: split those instead (Ruppert; Shewchuk 2002)
                var encroached = EncroachedByPoint(at, c);
                if (encroached.Count > 0) {
                    // split one now (the others go to the queue, to be split if still encroached), then try t again
                    int first = encroached[0];
                    for (int i = 1; i < encroached.Count; ++i) segments.Enqueue(Key(k.V[encroached[i]], k.V[Next(encroached[i])]));
                    long key = Key(k.V[first], k.V[Next(first)]);
                    triangles.Enqueue(t);
                    var failure = SplitSegment(first);
                    if (failure != null) return failure;
                    // a piece too short to split stays: then t cannot be helped this way. On the grid t is tried again (it is queued): the
                    // encroachment of an unsplittable subsegment no longer stops its circumcentre, which may still fit on the grid
                    if (unsplittable.Contains(key) && grid == null) exempt.Add(TriangleKey(t));
                    return null;
                }
                if (grid != null && ConstraintInPixel(at, c)) { exempt.Add(TriangleKey(t)); ++GridBlocked; return null; }
                int before = k.VertexCount;
                if (CheckTagsForTests) lastOperation = "circumcentre of triangle " + t + " at (" + c.x.ToString("R") + ", " + c.y.ToString("R") + ")";
                k.InsertPoint(c.x, c.y);
                if (k.VertexCount == before) { exempt.Add(TriangleKey(t)); k.Dirty.Clear(); if (grid != null) ++GridBlocked; return null; }
                ++inserted;
                if (b.heights != null) b.SetHeightOnReference(k.VertexCount - 1);
                Collect();
                return null;
            }

            /// <summary> visibility walk from t to the triangle containing c; blocked is the first constrained edge crossed, or -1 </summary>
            int Walk(int t, Vector2d c, out int blocked)
            {
                blocked = -1;
                int entry = -1;
                long guard = 4L * k.TriangleSlots + 16;
                for (long step = 0; step < guard; ++step) {
                    int moved = -1;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + (int)((step + j) % 3);
                        if (e == entry) continue;
                        if (ExactPredicates.Orient2d(k.Point(k.V[e]), k.Point(k.V[Next(e)]), c) < 0) { moved = e; break; }
                    }
                    if (moved < 0) return t;
                    if (k.Seg[moved] >= 0) { blocked = moved; return t; }
                    entry = k.H[moved];
                    t = entry / 3;
                    if (k.IsGhost(t) || !InDomain(t)) { blocked = moved; return t; }   // (not reached: the domain is bounded by constraints)
                }
                blocked = -1;
                return t;
            }

            /// <summary> the unlocked constrained edges on the boundary of c's cavity whose diametral circle contains c </summary>
            List<int> EncroachedByPoint(int t0, Vector2d c)
            {
                var result = new List<int>();
                var seen = new HashSet<int> { t0 };
                var stack = new Stack<int>();
                stack.Push(t0);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) {
                            if (!Locked(e) && !unsplittable.Contains(Key(k.V[e], k.V[Next(e)])) && DotSign(k.Point(k.V[e]), k.Point(k.V[Next(e)]), c) < 0) result.Add(e);
                            continue;
                        }
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || seen.Contains(t2)) continue;
                        var p = k.Point(k.V[3 * t2]); var q = k.Point(k.V[3 * t2 + 1]); var r = k.Point(k.V[3 * t2 + 2]);
                        if (ExactPredicates.InCircle(p, q, r, c) > 0) { seen.Add(t2); stack.Push(t2); }
                    }
                }
                return result;
            }

            /// <summary>
            /// Splits the constrained edge e: at its midpoint, or, next to an input vertex, at the power of two from it closest to the
            /// middle (concentric shells). The point is computed on the first input segment of the piece, so repeated splits do not drift.
            /// </summary>
            ConstrainedTriangulation2Result SplitSegment(int e)
            {
                int u = k.V[e], w = k.V[Next(e)];
                var src = k.Sources[k.Seg[e]];
                var pu = k.Point(u); var pw = k.Point(w);
                // lengths and projections in a scale by a power of two (no underflow; the same bits at any 2^k)
                var duw = pw - pu;
                int ex = ScaleOf(duw.x, duw.y, 0, 0);
                double length = ScaleB(duw, -ex).Length;
                double fraction = 0.5;
                // concentric shells around the vertices that existed before refinement (input points and crossings): where segments
                // meet at a small angle, their splits then lie on common circles and do not encroach each other (Shewchuk 2002)
                bool uInput = u < firstSteiner, wInput = w < firstSteiner;
                if (uInput != wInput && length > 0) {
                    // the power of two nearest to half the length, from the bits (no Log or Pow: their last bit may differ by runtime)
                    ExactPoint3.Decompose(length / 2, out var mant, out int em);
                    double lower = ExactPoint3.ScaleB(1, (int)(em + ExactPoint3.BitLength(mant) - 1)), d = length / 2 - lower < 2 * lower - length / 2 ? lower : 2 * lower;
                    if (d > length / 3 && d < 2 * length / 3) fraction = uInput ? d / length : 1 - d / length;
                }
                Vector2d p;
                var s = b.sources[src[0]];
                if (grid != null) {
                    if (!GridSplitPoint(e, pu, pw, src, fraction, out p)) { unsplittable.Add(Key(u, w)); ++GridUnsplittable; return null; }
                }
                else {
                    var A = new Vector2d(s.AX, s.AY); var B = new Vector2d(s.BX, s.BY);
                    var dir = B - A;
                    int es = ScaleOf(dir.x, dir.y, 0, 0);
                    var dirs = ScaleB(dir, -es); double l2 = dirs.LengthSquared;
                    double tu = l2 > 0 ? ScaleB(pu - A, -es).Dot(dirs) / l2 : 0, tw = l2 > 0 ? ScaleB(pw - A, -es).Dot(dirs) / l2 : 0;
                    double tt = tu + (tw - tu) * fraction;
                    p = A + dir * tt;
                }
                // (On the hull a point that rounds to the outside extends the triangulation's hull by a sliver, and Lawson's flips remove
                // the old hull edge; a point moved inwards instead would leave a sliver at an unconstrained hull edge.)
                if ((p.x == pu.x && p.y == pu.y) || (p.x == pw.x && p.y == pw.y) || double.IsNaN(p.x) || double.IsNaN(p.y)) {
                    unsplittable.Add(Key(u, w));
                    return null;
                }
                // a split point within the tolerance of another constraint would merge the two chains there (O11: nearly coincident
                // lines count as one), cutting off the region between them, eg the tip of a narrow wedge at a small input angle with its
                // input vertex (found with the corpus run, earcut's water): the piece stays as it is
                // (on the grid the exact checks of ValidGridSplit take its place: no other constraint in q's pixel, no crossing, and a
                // vertex already at q, possibly the end of another constraint, is a vertex the chains may share, as hot pixels are)
                if (grid == null && b.NearOtherSource(p.x, p.y, src)) { unsplittable.Add(Key(u, w)); return null; }
                if (inserted >= limit) { b.SteinerLimitReached = true; return null; }
                ++splits;
                if (CheckTagsForTests) lastOperation = "split of " + u + "=(" + pu.x.ToString("R") + ", " + pu.y.ToString("R") + ")-" + w + "=(" + pw.x.ToString("R") + ", " + pw.y.ToString("R") + ") (sources " + string.Join(",", src.Select(z => z + ":(" + b.sources[z].AX + "," + b.sources[z].AY + ")-(" + b.sources[z].BX + "," + b.sources[z].BY + ") ring " + b.sources[z].Ring)) + ") at (" + p.x.ToString("R") + ", " + p.y.ToString("R") + ") left " + TagOf(e / 3) + " right " + TagOf(k.H[e] / 3);
                var lifted = new LiftedPiece { Sources = new List<int>(src), U = pu, W = pw, Left = TagOf(e / 3), Right = TagOf(k.H[e] / 3) };
                var sources = k.Unconstrain(e);
                // without its constraint the edge may not be Delaunay: restore that first, Lawson's insertion assumes it (as at crossings)
                k.RestoreDelaunay(new[] { ((long)u << 32) | (uint)w });
                int before = k.VertexCount;
                int q = k.InsertPoint(p.x, p.y);
                if (k.VertexCount > before) {
                    ++inserted;
                    if (b.heights != null) b.SetHeight(q, u, w);
                } else if (grid == null || q == u || q == w) unsplittable.Add(Key(u, w));   // no new vertex: no progress from splitting this piece
                var pending = new Stack<ConstrainedDelaunayMesh2.Builder.Piece>();
                pending.Push(new ConstrainedDelaunayMesh2.Builder.Piece { U = q, W = w, Sources = sources });
                pending.Push(new ConstrainedDelaunayMesh2.Builder.Piece { U = u, W = q, Sources = new List<int>(sources) });
                long guard = 0;
                while (pending.Count > 0) {
                    if (++guard > 100000) return b.Fail(Triangulation2FailureKind.NodingLimit, p, "a segment split did not settle");
                    var failure = b.InsertPiece(pending.Pop(), pending);
                    if (failure != null) return failure;
                }
                Collect(lifted);
                return null;
            }
        }
    }
}
