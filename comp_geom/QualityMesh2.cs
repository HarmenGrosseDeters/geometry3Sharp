// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
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
            var builder = new ConstrainedDelaunayMesh2.Builder(input, ResolveCrossings, cancellationToken);
            var refiner = new Refiner(this, builder);
            builder.refine = refiner.Run;
            builder.IsBad = refiner.BelowAngle;
            return builder.Run();
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
            int hullSource = -1;
            long limit, inserted, splits;
            internal static bool DebugTrace = Environment.GetEnvironmentVariable("G3_REFINE_TRACE") != null;

            internal Refiner(QualityMesh2 options, ConstrainedDelaunayMesh2.Builder builder)
            {
                this.options = options; b = builder;
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
                ConstrainHull();
                k.Dirty = new List<int>();
                for (int t = 0; t < k.TriangleSlots; ++t)
                    if (InDomain(t)) { triangles.Enqueue(t); QueueEdges(t); }
                long steps = 0;
                while (segments.Count > 0 || triangles.Count > 0) {
                    if ((++steps & 255) == 0) b.cancel.ThrowIfCancellationRequested();
                    if (DebugTrace && (steps % 200000) == 0) Console.Error.WriteLine("REFINE steps " + steps + " inserted " + inserted + " segQ " + segments.Count + " triQ " + triangles.Count + " splits " + splits + " exempt " + exempt.Count + " unsplittable " + unsplittable.Count + " vertices " + k.VertexCount);
                    if (inserted >= limit) { b.SteinerLimitReached = true; break; }
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
                return null;
            }

            bool HasRegionArea()
            {
                foreach (var r in b.input.Regions ?? new List<Mesh2Region>()) if (r.MaxArea != null) return true;
                return false;
            }

            /// <summary> without outer rings the hull is the boundary (D13): its edges become constraints, so it does not grow </summary>
            void ConstrainHull()
            {
                if (b.outerRings > 0) return;
                for (int t = 0; t < k.TriangleSlots; ++t) {
                    if (!k.IsGhost(t)) continue;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.V[e] == k.Infinite || k.V[Next(e)] == k.Infinite || k.Seg[e] >= 0) continue;
                        if (hullSource < 0) {
                            b.sources.Add(new ConstrainedDelaunayMesh2.Source { Ring = -1, Hull = true });
                            hullSource = b.sources.Count - 1;
                        }
                        k.SetConstraint(e, k.NewConstraint(new List<int> { hullSource }));
                    }
                }
            }

            void QueueEdges(int t)
            {
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (k.Seg[e] >= 0) segments.Enqueue(Key(k.V[e], k.V[Next(e)]));
                }
            }

            /// <summary> after an insertion: the rewritten triangles may be bad, their constrained edges encroached </summary>
            void Collect()
            {
                // A split point on a boundary segment can round to just outside the domain: the sliver between the old edge and the
                // new pieces is then tagged outside, while the domain is bounded by constraints. Tags change only across constraints,
                // so a triangle joined to the domain by an unconstrained edge takes the domain's tag.
                var queue = new Queue<int>(k.Dirty);
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    if (k.IsGhost(t) || k.Tag[t] >= 0) continue;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || k.Tag[t2] < 0) continue;
                        k.Tag[t] = k.Tag[t2];
                        for (int i = 0; i < 3; ++i) if (k.Seg[3 * t + i] < 0) queue.Enqueue(k.H[3 * t + i] / 3);
                        k.Dirty.Add(t);
                        break;
                    }
                }
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
                    if (!InDomain(t)) continue;
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

            double Area(int t)
            {
                var a = k.Point(k.V[3 * t]); var c = k.Point(k.V[3 * t + 1]); var d = k.Point(k.V[3 * t + 2]);
                return 0.5 * ((c.x - a.x) * (d.y - a.y) - (c.y - a.y) * (d.x - a.x));
            }

            /// <summary> sin^2 of the smallest angle, and the corner it is at </summary>
            double MinSin2(int t, out int corner)
            {
                double best = double.MaxValue; corner = 0;
                double area2 = 2 * Area(t);
                for (int j = 0; j < 3; ++j) {
                    var p = k.Point(k.V[3 * t + j]); var q = k.Point(k.V[3 * t + (j + 1) % 3]); var r = k.Point(k.V[3 * t + (j + 2) % 3]);
                    double l1 = (q - p).LengthSquared, l2 = (r - p).LengthSquared;
                    double s2 = area2 * area2 / (l1 * l2);
                    if (s2 < best) { best = s2; corner = j; }
                }
                return best;
            }

            /// <summary> for the output's list of bad triangles: below the minimum angle </summary>
            internal bool BelowAngle(int t) => options.MinAngle > 0 && MinSin2(t, out _) < sin2Min;

            double LongestLocked2(int t)
            {
                double best = 0;
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (k.Seg[e] >= 0 && Locked(e)) best = Math.Max(best, (k.Point(k.V[Next(e)]) - k.Point(k.V[e])).LengthSquared);
                }
                return best;
            }

            bool Bad(int t)
            {
                if (exempt.Contains(TriangleKey(t))) return false;
                double area = Area(t);
                double locked2 = LongestLocked2(t);
                // a triangle over an edge that may not be split need not shrink below a needle (O5, O9)
                bool areaExempt = locked2 > 4 * area;
                double? maxArea = options.MaxArea;
                if (k.Tag[t] > 0) {
                    var ra = b.input.Regions[k.Tag[t] - 1].MaxArea;
                    if (ra != null) maxArea = maxArea == null ? ra : Math.Min(maxArea.Value, ra.Value);
                }
                if (maxArea != null && area > maxArea.Value && !areaExempt) return true;
                if (options.SizeFunction != null) {
                    var a = k.Point(k.V[3 * t]); var c = k.Point(k.V[3 * t + 1]); var d = k.Point(k.V[3 * t + 2]);
                    var g = (a + c + d) / 3;
                    double h = options.SizeFunction(g.x, g.y);
                    double longest = Math.Max((c - a).LengthSquared, Math.Max((d - c).LengthSquared, (a - d).LengthSquared));
                    if (longest > Math.Max(h * h, locked2)) return true;
                }
                if (options.MinAngle <= 0) return false;
                if (MinSin2(t, out int corner) >= sin2Min) return false;
                // the small angle lies between two constrained edges: forced by the input (O3)
                int ea = 3 * t + corner, eb = 3 * t + (corner + 2) % 3;
                if (k.Seg[ea] >= 0 && k.Seg[eb] >= 0) return false;
                return true;
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
                var a = k.Point(k.V[3 * t]); var p = k.Point(k.V[3 * t + 1]); var q = k.Point(k.V[3 * t + 2]);
                double bx = p.x - a.x, by = p.y - a.y, cx = q.x - a.x, cy = q.y - a.y;
                double d = 2 * (bx * cy - by * cx);
                double b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                double ux = (cy * b2 - by * c2) / d, uy = (bx * c2 - cx * b2) / d;
                c = new Vector2d(a.x + ux, a.y + uy);
                return d != 0 && !double.IsNaN(c.x) && !double.IsNaN(c.y) && !double.IsInfinity(c.x) && !double.IsInfinity(c.y)
                    && Math.Abs(c.x) <= DelaunayMesh2.CoordinateLimit && Math.Abs(c.y) <= DelaunayMesh2.CoordinateLimit;
            }

            ConstrainedTriangulation2Result SplitTriangle(int t)
            {
                if (!Circumcentre(t, out var c)) { exempt.Add(TriangleKey(t)); return null; }
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
                    // a piece too short to split stays: then t cannot be helped this way
                    if (unsplittable.Contains(key)) exempt.Add(TriangleKey(t));
                    return null;
                }
                int before = k.VertexCount;
                k.InsertPoint(c.x, c.y);
                if (k.VertexCount == before) { exempt.Add(TriangleKey(t)); k.Dirty.Clear(); return null; }
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
                double length = (pw - pu).Length;
                double fraction = 0.5;
                bool uInput = u < k.N, wInput = w < k.N;
                if (uInput != wInput && length > 0) {
                    double d = Math.Pow(2, Math.Round(Math.Log(length / 2, 2)));
                    if (d > length / 3 && d < 2 * length / 3) fraction = uInput ? d / length : 1 - d / length;
                }
                Vector2d p;
                var s = b.sources[src[0]];
                if (s.Hull) p = pu + (pw - pu) * fraction;
                else {
                    var A = new Vector2d(s.AX, s.AY); var B = new Vector2d(s.BX, s.BY);
                    var dir = B - A; double l2 = dir.LengthSquared;
                    double tu = l2 > 0 ? (pu - A).Dot(dir) / l2 : 0, tw = l2 > 0 ? (pw - A).Dot(dir) / l2 : 0;
                    double tt = tu + (tw - tu) * fraction;
                    p = A + dir * tt;
                }
                if ((p.x == pu.x && p.y == pu.y) || (p.x == pw.x && p.y == pw.y) || double.IsNaN(p.x) || double.IsNaN(p.y)) {
                    unsplittable.Add(Key(u, w));
                    return null;
                }
                ++splits;
                var sources = k.Unconstrain(e);
                int before = k.VertexCount;
                int q = k.InsertPoint(p.x, p.y);
                if (k.VertexCount > before) {
                    ++inserted;
                    if (b.heights != null) b.SetHeight(q, u, w);
                }
                var pending = new Stack<ConstrainedDelaunayMesh2.Builder.Piece>();
                pending.Push(new ConstrainedDelaunayMesh2.Builder.Piece { U = q, W = w, Sources = sources });
                pending.Push(new ConstrainedDelaunayMesh2.Builder.Piece { U = u, W = q, Sources = new List<int>(sources) });
                long guard = 0;
                while (pending.Count > 0) {
                    if (++guard > 100000) return b.Fail(Triangulation2FailureKind.NodingLimit, p, "a segment split did not settle");
                    var failure = b.InsertPiece(pending.Pop(), pending);
                    if (failure != null) return failure;
                }
                Collect();
                return null;
            }
        }
    }
}
