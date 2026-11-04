// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Constrained Delaunay triangulation of points, segments, rings, holes and regions, with exact predicates; the output
    /// coordinates are doubles and the mesh is valid on them (decision D1).
    /// </summary>
    /// <remarks>
    /// <para>Crossing, touching and overlapping constraints are split at their intersections (noding, spec O10). A crossing is
    /// computed exactly and rounded to the nearest double; the pieces then run through it, at most a few units in the last
    /// place off their input segment. A vertex within the automatic tolerance (2^-41 of the larger of the largest coordinate
    /// and the extent, about 2.6 um at UTM northings; spec O11, Q14) of a segment becomes a vertex of it, so every piece stays
    /// within that tolerance of each of its input segments. Input points within that tolerance of each other become one vertex
    /// (the first of them; <see cref="ConstrainedMesh2.InputToVertex"/> maps the others to it), so no ulp-wide triangle is made
    /// between them. With <see cref="ResolveCrossings"/> off, a crossing gives
    /// <see cref="Triangulation2FailureKind.CrossingConstraints"/>.</para>
    /// <para>The domain: a point belongs to it iff the innermost ring containing it (each ring by the even-odd rule) is an outer
    /// ring, or, without outer rings, iff it is in the convex hull and in no hole (decision D5, owner 2026-11-01). So an island in
    /// a lake is meshed, a lake in it removed again, a hole in a hole stays removed; a hole removes the free segments inside it,
    /// not the rings whose role is outer (Q7). Rings that overlap without nesting: the latest decides, with a diagnostic.
    /// Hole seeds remove the bounded cell of the constraints around them (D6). Regions by ring or seed label the triangles,
    /// the last one wins (I6, D16).</para>
    /// <para>Vertex ids: input point i is vertex i; crossings get ids from the input count on (Q8). Deterministic; independent
    /// instances may run concurrently.</para>
    /// </remarks>
    public sealed class ConstrainedDelaunayMesh2
    {
        /// <summary> Split crossing and overlapping constraints (default). Off: a crossing is a failure, never an invalid mesh. </summary>
        public bool ResolveCrossings = true;

        /// <summary> The automatic tolerance as a fraction of the larger of the largest coordinate and the extent. </summary>
        public const double AutoToleranceFactor = 1.0 / (1L << 41);

        /// <summary>
        /// Snap tolerance in coordinate units (spec O11); 0 (default): only the automatic tolerance. With a tolerance the input is snap
        /// rounded first (decision D14): every input point moves to the centre of its pixel on a grid of this width anchored at the origin
        /// (so input on that grid stays put), constraints are routed through the hot pixels, and every vertex ends at least half a pixel
        /// from every constraint edge not incident to it. With quality refinement every vertex it adds is on the grid as well. A point on
        /// the grid and a segment closer than half the tolerance (per axis) merge, features more than twice the tolerance apart never do,
        /// and in between the grid decides; two points off the grid merge if they fall into one pixel, so close points on either side of a
        /// pixel boundary stay apart. Collapsed rings and segments and dropped seeds are reported in the diagnostics. At least 2^-40 of the
        /// largest coordinate.
        /// </summary>
        public double SnapTolerance;

        /// <exception cref="ArgumentException">The input is invalid, or SnapTolerance is negative, not finite, or too small for the coordinates.</exception>
        public ConstrainedTriangulation2Result Compute(ConstrainedMesh2Input input, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Snapped(input, SnapTolerance, cancellationToken, (snapped, snap) => new Builder(snapped, ResolveCrossings, cancellationToken) { OuterRingsCollapsed = snap?.OuterRingsCollapsed ?? false }.Run());
        }

        /// <summary> runs the mesher on the snap-rounded input (D14) if a snap tolerance is set, and maps the result back to the input points </summary>
        internal static ConstrainedTriangulation2Result Snapped(ConstrainedMesh2Input input, double tolerance, CancellationToken cancel,
            Func<ConstrainedMesh2Input, SnapRounding2, ConstrainedTriangulation2Result> run)
        {
            if (tolerance == 0) return run(input, null);
            if (!(tolerance > 0) || double.IsInfinity(tolerance)) throw new ArgumentException("SnapTolerance must be zero or positive and finite (spec O11)");
            new Builder(input, true, cancel).Validate();
            SnapRounding2 snap;
            try {
                snap = SnapRounding2.Apply(input, tolerance, cancel);
            } catch (InvalidOperationException e) {
                // a round limit or no pixel to restore: never seen in the fuzz tests; a failure status, not an exception
                return new ConstrainedTriangulation2Result(Triangulation2Status.Failed, Triangulation2FailureKind.InvalidTopology, null, new string[0], null, e.Message);
            }
            cancel.ThrowIfCancellationRequested();
            var result = run(snap.Result, snap);
            var diagnostics = snap.Diagnostics.Concat(result.Diagnostics ?? new string[0]).ToList();
            diagnostics.Add("snap: tolerance " + tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", input moved by at most "
                + snap.MaxDisplacement.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " per axis, " + snap.Crossings + " crossings, "
                + snap.Rounds + " rounds, restore " + snap.Spread + " spread, " + snap.ForcedSpread + " forced, " + snap.Widened + " widened");
            return new ConstrainedTriangulation2Result(result.Status, result.FailureKind, result.Mesh?.WithInputCount(input.Points.Count), diagnostics,
                result.ProblemCoordinate, result.FailureDetail);
        }

        internal static Vector2d IntersectionForTests(Vector2d a, Vector2d b, Vector2d c, Vector2d d) => Builder.Intersection(a, b, c, d);
        internal static double RatioToDoubleForTests(BigInteger num, BigInteger den, int scale) => Builder.RatioToDouble(num, den, scale);

        /// <summary> an input piece of a constraint and where it came from </summary>
        internal struct Source
        {
            internal int Ring;        // ring id (outer and hole rings, then region rings), or -1
            internal int Marker;
            internal bool Locked;
            /// <summary> an edge of the convex hull made a constraint by refinement (no input segment) </summary>
            internal bool Hull;
            internal double AX, AY, BX, BY;
            /// <summary> kernel vertices of the input end points </summary>
            internal int VA, VB;
        }

        /// <summary> The constrained triangulation in the making; the refinement of <see cref="QualityMesh2"/> works on it. </summary>
        internal sealed class Builder
        {
            /// <summary>
            /// the input segments in [HullSegmentsFrom, HullSegmentsTo) are the snapped chains of the hull edges (QualityMesh2 on a
            /// grid, owner decision of 2026-11-02, MESHING2D_HULL_GRID.md section 7): they become hull sources, and ConstrainHull
            /// adds no hull edges of its own then
            /// </summary>
            internal int HullSegmentsFrom = int.MaxValue, HullSegmentsTo = int.MaxValue;
            internal bool HullFromSegments => HullSegmentsFrom != int.MaxValue;
            internal readonly ConstrainedMesh2Input input;
            readonly bool resolve;
            internal readonly CancellationToken cancel;
            internal readonly List<string> diagnostics = new List<string>();
            internal readonly List<Source> sources = new List<Source>();
            internal DelaunayKernel2 k;
            internal int n;
            int[] order, rank;
            internal double tolerance;
            internal double[] heights;     // per kernel vertex (ranks, then new vertices), if any
            internal int outerRings, holeRings, regionRings;
            /// <summary> the input's outer rings all collapsed in snapping (SnapRounding2.OuterRingsCollapsed): no part of the plane is
            /// in the domain, though no outer ring is left (without this the domain would be the hull, D13, and cover input outside
            /// the rings; found with the star polygon fuzz, seeds 37 and 41) </summary>
            internal bool OuterRingsCollapsed;
            bool[] ringIsHole;
            /// <summary> runs after the domain and the tags are known, before the output; a failure result stops it </summary>
            internal Func<Builder, ConstrainedTriangulation2Result> refine;
            /// <summary> filled by the refinement for the output </summary>
            internal bool SteinerLimitReached;
            internal Func<int, bool> IsBad;

            internal Builder(ConstrainedMesh2Input input, bool resolve, CancellationToken cancel)
            {
                this.input = input; this.resolve = resolve; this.cancel = cancel;
            }

            internal ConstrainedTriangulation2Result Fail(Triangulation2FailureKind kind, Vector2d? where, string detail)
                => new ConstrainedTriangulation2Result(Triangulation2Status.Failed, kind, null, diagnostics, where, detail);

            internal ConstrainedTriangulation2Result Run()
            {
                Validate();
                n = input.Points.Count;
                var x = new double[n];
                var y = new double[n];
                var vertices = new Vector2d[n];
                double maxAbs = 0, minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
                for (int i = 0; i < n; ++i) {
                    var p = input.Points[i];
                    x[i] = p.x == 0 ? 0.0 : p.x; y[i] = p.y == 0 ? 0.0 : p.y;
                    vertices[i] = new Vector2d(x[i], y[i]);
                    maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(x[i]), Math.Abs(y[i])));
                    minX = Math.Min(minX, x[i]); maxX = Math.Max(maxX, x[i]); minY = Math.Min(minY, y[i]); maxY = Math.Max(maxY, y[i]);
                }
                double magnitude = n == 0 ? 0 : Math.Max(maxAbs, Math.Max(maxX - minX, maxY - minY));
                tolerance = magnitude * AutoToleranceFactor;
                // tolerance tests run on differences scaled by a power of two to about 1, so that no square underflows at tiny scales
                // (review of M2: at 2^-560 a dot product was 0 and the walk found no wedge)
                if (magnitude > 0) {
                    ExactPoint3.Decompose(magnitude, out var mant, out int exp);
                    shift = -(int)(exp + ExactPoint3.BitLength(mant) - 1);
                    scaledTolerance = ExactPoint3.ScaleB(magnitude, shift) * AutoToleranceFactor;
                }
                cancel.ThrowIfCancellationRequested();
                MergeNearPoints(x, y);
                order = DelaunayKernel2.InsertionOrder(x, y);
                rank = new int[n];
                var xs = new double[n]; var ys = new double[n];
                for (int r = 0; r < n; ++r) { xs[r] = x[order[r]]; ys[r] = y[order[r]]; rank[order[r]] = r; }
                k = new DelaunayKernel2(xs, ys, order);
                bool area;
                try {
                    area = k.Build(cancel);
                } catch (InvalidOperationException e) {
                    return Fail(Triangulation2FailureKind.InvalidTopology, null, e.Message);
                }
                var mapping = new int[n];
                if (!area) {
                    var first = new Dictionary<Vector2d, int>();
                    for (int i = 0; i < n; ++i) {
                        if (!first.TryGetValue(vertices[i], out int f)) first.Add(vertices[i], f = i);
                        mapping[i] = f;
                    }
                    var empty = new ConstrainedMesh2(vertices, n, mapping, new Index3i[0], new Index3i[0], new int[0], new Mesh2Subsegment[0],
                        input.Heights == null ? null : input.Heights.ToArray(), new int[0], new int[0], new int[0], new Vector2d[0], 0);
                    return new ConstrainedTriangulation2Result(Triangulation2Status.Empty, Triangulation2FailureKind.None, empty, diagnostics);
                }
                for (int i = 0; i < n; ++i) mapping[i] = k.Representative[k.Vertex[rank[i]]];
                k.BeginConstraints();
                InitHeights();
                try {
                    var failure = InsertConstraints();
                    if (failure != null) return failure;
                    // a last linear pass: every unconstrained edge locally Delaunay, so the result is a constrained Delaunay triangulation
                    var bad = k.NonDelaunayEdges();
                    if (bad.Count > 0) {
                        k.RestoreDelaunay(bad);
                        if (k.NonDelaunayEdges().Count > 0) return Fail(Triangulation2FailureKind.InvalidTopology, null, "the constrained Delaunay property could not be restored");
                    }
                } catch (InvalidOperationException e) {
                    return Fail(Triangulation2FailureKind.InvalidTopology, null, e.Message);
                }
                cancel.ThrowIfCancellationRequested();
                return Output(vertices, mapping);
            }

            /// <summary>
            /// Points within the automatic tolerance of an earlier point are merged into it (spec O11, Q14): they get its coordinates
            /// for the triangulation, so the kernel maps them to it as exact duplicates; their slots keep their own coordinates
            /// (D17). A point merges only into a point that merged into nothing, so no chain moves anything farther than the tolerance.
            /// </summary>
            void MergeNearPoints(double[] x, double[] y)
            {
                if (!(scaledTolerance > 0) || n < 2) return;
                double cell = 2 * scaledTolerance, tol2 = scaledTolerance * scaledTolerance;
                double ox = x[0], oy = y[0];
                var cells = new Dictionary<long, List<int>>();
                long Key(long cx, long cy) => (cx << 32) ^ (cy & 0xffffffff);
                int merged = 0;
                for (int i = 0; i < n; ++i) {
                    double sx = Scaled(x[i] - ox), sy = Scaled(y[i] - oy);
                    long cx = (long)Math.Floor(sx / cell), cy = (long)Math.Floor(sy / cell);
                    int into = -1;
                    for (long dx = -1; dx <= 1 && into < 0; ++dx)
                        for (long dy = -1; dy <= 1 && into < 0; ++dy) {
                            if (!cells.TryGetValue(Key(cx + dx, cy + dy), out var list)) continue;
                            foreach (int j in list) {
                                if (x[j] == x[i] && y[j] == y[i]) { into = -2; break; }   // an exact duplicate: the kernel maps it
                                double ex = Scaled(x[i] - x[j]), ey = Scaled(y[i] - y[j]);
                                if (ex * ex + ey * ey <= tol2) { into = j; break; }
                            }
                        }
                    if (into >= 0) { x[i] = x[into]; y[i] = y[into]; ++merged; continue; }
                    if (into == -2) continue;
                    if (!cells.TryGetValue(Key(cx, cy), out var own)) cells.Add(Key(cx, cy), own = new List<int>());
                    own.Add(i);
                }
                if (merged > 0) diagnostics.Add(merged + " points within the automatic tolerance of another point were merged into it");
            }

            internal void Validate()
            {
                var pts = input.Points ?? throw new ArgumentException("input.Points is null");
                for (int i = 0; i < pts.Count; ++i) DelaunayMesh2.CheckCoordinate(pts[i].x, pts[i].y, "points[" + i + "]");
                void Index(int v, string what)
                {
                    if (v < 0 || v >= pts.Count) throw new ArgumentException(what + " refers to point " + v + ", which does not exist (" + pts.Count + " points)");
                }
                if (input.Segments != null)
                    for (int i = 0; i < input.Segments.Count; ++i) {
                        var s = input.Segments[i] ?? throw new ArgumentException("segments[" + i + "] is null");
                        Index(s.A, "segments[" + i + "].A"); Index(s.B, "segments[" + i + "].B");
                    }
                if (input.Rings != null)
                    for (int i = 0; i < input.Rings.Count; ++i) {
                        var r = input.Rings[i] ?? throw new ArgumentException("rings[" + i + "] is null");
                        if (r.Vertices == null) throw new ArgumentException("rings[" + i + "] has no vertices");
                        foreach (int v in r.Vertices) Index(v, "rings[" + i + "]");
                    }
                if (input.HoleSeeds != null)
                    for (int i = 0; i < input.HoleSeeds.Count; ++i) DelaunayMesh2.CheckCoordinate(input.HoleSeeds[i].x, input.HoleSeeds[i].y, "holeSeeds[" + i + "]");
                if (input.Regions != null)
                    for (int i = 0; i < input.Regions.Count; ++i) {
                        var r = input.Regions[i] ?? throw new ArgumentException("regions[" + i + "] is null");
                        if (r.Ring != null) foreach (int v in r.Ring) Index(v, "regions[" + i + "]");
                        else DelaunayMesh2.CheckCoordinate(r.Seed.x, r.Seed.y, "regions[" + i + "].Seed");
                    }
                if (input.Heights != null) {
                    if (input.Heights.Count != pts.Count) throw new ArgumentException("heights: one per point is needed");
                    for (int i = 0; i < input.Heights.Count; ++i)
                        if (double.IsNaN(input.Heights[i]) || double.IsInfinity(input.Heights[i])) throw new ArgumentException("heights[" + i + "] is not finite");
                }
            }

            int KV(int inputIndex) => k.Vertex[rank[inputIndex]];

            void InitHeights()
            {
                if (input.Heights == null) return;
                heights = new double[Math.Max(16, n + 1)];
                var set = new bool[n];
                for (int i = 0; i < n; ++i) {
                    int v = KV(i);
                    double z = input.Heights[i];
                    // the lowest index wins (decision D11): Representative is the lowest index of the vertex
                    if (k.Representative[v] == i) { heights[v] = z; set[v] = true; }
                }
                for (int i = 0; i < n; ++i) {
                    int v = KV(i);
                    if (k.Representative[v] != i && input.Heights[i] != heights[v])
                        diagnostics.Add("point " + i + " coincides with point " + k.Representative[v] + " but has another height; the lower index wins");
                }
            }

            /// <summary>
            /// The height of a crossing (Q9, D11): linear along the input segment with the lower index of the two that cross, between
            /// the heights of its input end points; independent of the insertion order. A diagnostic if the other segment disagrees.
            /// </summary>
            void SetCrossingHeight(int v, List<int> a, List<int> c)
            {
                if (heights == null) return;
                if (v >= heights.Length) Array.Resize(ref heights, Math.Max(v + 1, 2 * heights.Length));
                int sa = a.Min(), sc = c.Min();
                double za = Along(v, sources[sa]), zc = Along(v, sources[sc]);
                heights[v] = sa <= sc ? za : zc;
                if (za != zc) diagnostics.Add("a crossing at (" + k.X[v].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                    + k.Y[v].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ") lies on two constraints with different heights; the one given first wins");
            }

            double Along(int v, Source q)
            {
                double dx = q.BX - q.AX, dy = q.BY - q.AY, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? ((k.X[v] - q.AX) * dx + (k.Y[v] - q.AY) * dy) / l2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                return heights[q.VA] + t * (heights[q.VB] - heights[q.VA]);
            }

            // the constrained triangulation before refinement, for the heights of Steiner points (Q9)
            int[] refV, refH;
            double[] refX, refY, refZ;

            int refHint = -1;

            internal void SnapshotReference()
            {
                if (heights == null) return;
                refHint = -1;
                int slots = k.TriangleSlots;
                refV = new int[3 * slots]; Array.Copy(k.V, refV, refV.Length);
                refH = new int[3 * slots]; Array.Copy(k.H, refH, refH.Length);
                refX = new double[k.VertexCount]; refY = new double[k.VertexCount]; refZ = new double[k.VertexCount];
                for (int v = 0; v < k.VertexCount; ++v) {
                    if (v == k.Infinite) continue;
                    refX[v] = k.X[v]; refY[v] = k.Y[v]; refZ[v] = v < heights.Length ? heights[v] : 0;
                }
            }

            /// <summary> the height of vertex v by linear interpolation on the reference triangulation (Q9) </summary>
            internal void SetHeightOnReference(int v)
            {
                if (heights == null || refV == null) return;
                if (v >= heights.Length) Array.Resize(ref heights, Math.Max(v + 1, 2 * heights.Length));
                var p = k.Point(v);
                int slots = refV.Length / 3, t = -1;
                bool Solid(int s0) => refV[3 * s0] != k.Infinite && refV[3 * s0 + 1] != k.Infinite && refV[3 * s0 + 2] != k.Infinite;
                Vector2d R(int i) => new Vector2d(refX[refV[i]], refY[refV[i]]);
                // from the triangle the last point was found in (refinement inserts near where it inserted before)
                if (refHint >= 0 && refHint < slots && Solid(refHint)) t = refHint;
                for (int s0 = 0; s0 < slots && t < 0; ++s0) if (Solid(s0)) t = s0;
                if (t < 0) return;
                // a visibility walk on a constrained triangulation that need not be Delaunay: the first edge tried is chosen by a
                // fixed pseudo-random sequence, so the walk cannot cycle for ever (a stochastic walk; review of M3); deterministic
                uint state = 2463534242u ^ (uint)v * 2654435761u;
                long guard = 8L * slots + 64;
                bool found = false;
                for (long step = 0; step < guard; ++step) {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    int start = (int)(state % 3), moved = -1;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + (start + j) % 3, f = 3 * t + (start + j + 1) % 3;
                        if (ExactPredicates.Orient2d(R(e), R(f), p) < 0) { moved = e; break; }
                    }
                    if (moved < 0) { found = true; break; }
                    int next = refH[moved] / 3;
                    if (!Solid(next)) { found = true; break; }   // outside the reference hull (a hull split rounded outwards): extrapolate
                    t = next;
                }
                if (!found) {
                    // (not expected) a scan for the containing triangle
                    for (int s0 = 0; s0 < slots; ++s0) {
                        if (!Solid(s0)) continue;
                        if (ExactPredicates.Orient2d(R(3 * s0), R(3 * s0 + 1), p) >= 0 && ExactPredicates.Orient2d(R(3 * s0 + 1), R(3 * s0 + 2), p) >= 0
                            && ExactPredicates.Orient2d(R(3 * s0 + 2), R(3 * s0), p) >= 0) { t = s0; break; }
                    }
                }
                refHint = t;
                int a = refV[3 * t], bb = refV[3 * t + 1], c = refV[3 * t + 2];
                double x0 = refX[a], y0 = refY[a], x1 = refX[bb] - x0, y1 = refY[bb] - y0, x2 = refX[c] - x0, y2 = refY[c] - y0;
                double det = x1 * y2 - y1 * x2, px = p.x - x0, py = p.y - y0;
                double l1 = (px * y2 - py * x2) / det, l2 = (x1 * py - y1 * px) / det;
                heights[v] = refZ[a] + l1 * (refZ[bb] - refZ[a]) + l2 * (refZ[c] - refZ[a]);
            }

            internal void SetHeight(int v, int u, int w)
            {
                if (heights == null) return;
                if (v >= heights.Length) Array.Resize(ref heights, Math.Max(v + 1, 2 * heights.Length));
                // linear along the piece u-w that v splits
                double dx = k.X[w] - k.X[u], dy = k.Y[w] - k.Y[u], l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? ((k.X[v] - k.X[u]) * dx + (k.Y[v] - k.Y[u]) * dy) / l2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                heights[v] = heights[u] + t * (heights[w] - heights[u]);
            }

            internal struct Piece
            {
                internal int U, W;
                internal List<int> Sources;
            }

            ConstrainedTriangulation2Result InsertConstraints()
            {
                var work = new Stack<Piece>();
                void Add(int a, int b, Source s)
                {
                    int u = KV(a), w = KV(b);
                    sources.Add(s);
                    if (u != w) work.Push(new Piece { U = u, W = w, Sources = new List<int> { sources.Count - 1 } });
                }
                Source Make(int a, int b, int ring, int marker, bool locked)
                    => new Source { Ring = ring, Marker = marker, Locked = locked, AX = k.X[KV(a)], AY = k.Y[KV(a)], BX = k.X[KV(b)], BY = k.Y[KV(b)], VA = KV(a), VB = KV(b) };
                // rings: outer and hole rings, then region rings, each with an id
                var rings = input.Rings ?? new List<Mesh2Ring>();
                var regions = input.Regions ?? new List<Mesh2Region>();
                ringIsHole = new bool[rings.Count + regions.Count];
                for (int r = 0; r < rings.Count; ++r) {
                    var ring = rings[r];
                    if (ring.Hole) { ringIsHole[r] = true; ++holeRings; } else ++outerRings;
                    var vs = ring.Vertices;
                    for (int i = 0; i < vs.Length; ++i) {
                        int a = vs[i], b = vs[(i + 1) % vs.Length];
                        Add(a, b, Make(a, b, r, ring.Marker, false));
                    }
                }
                for (int g = 0; g < regions.Count; ++g) {
                    var vs = regions[g].Ring;
                    if (vs == null) continue;
                    ++regionRings;
                    for (int i = 0; i < vs.Length; ++i) {
                        int a = vs[i], b = vs[(i + 1) % vs.Length];
                        Add(a, b, Make(a, b, rings.Count + g, 0, false));
                    }
                }
                if (input.Segments != null)
                    for (int si = 0; si < input.Segments.Count; ++si) {
                        var s = input.Segments[si];
                        var source = Make(s.A, s.B, -1, s.Marker, s.Locked);
                        // the snapped chains of the hull edges of a hull domain (QualityMesh2 on a grid): hull sources
                        source.Hull = si >= HullSegmentsFrom && si < HullSegmentsTo;
                        Add(s.A, s.B, source);
                    }
                // in input order: reverse the stack once
                var pending = new Stack<Piece>(work);
                // Each crossing of two input segments makes at most one vertex, so more new vertices than pairs of sources mean the
                // rounded crossings feed on each other; and the steps per vertex are bounded. Both limits only stop a runaway.
                long m = sources.Count, vertexLimit = m * (m - 1) / 2 + 2 * m + 1000, steps = 0;
                while (pending.Count > 0) {
                    if ((++steps & 255) == 0) cancel.ThrowIfCancellationRequested();
                    long made = k.VertexCount - (k.N + 1);
                    if (made > vertexLimit || steps > 64L * (m + n + made) + 100000)
                        return Fail(Triangulation2FailureKind.NodingLimit, null, "noding did not settle: " + made + " vertices made in " + steps + " steps");
                    var piece = pending.Pop();
                    var failure = InsertPiece(piece, pending);
                    if (failure != null) return failure;
                }
                return null;
            }

            /// <summary> v is within the tolerance of the open segment u-w and of every source of the piece </summary>
            bool Near(int v, int u, int w, List<int> src)
            {
                if (!(scaledTolerance > 0)) return false;
                double px = k.X[v], py = k.Y[v];
                if (!WithinTolerance(px, py, k.X[u], k.Y[u], k.X[w], k.Y[w])) return false;
                foreach (int s in src) {
                    var q = sources[s];
                    if (!WithinTolerance(px, py, q.AX, q.AY, q.BX, q.BY)) return false;
                }
                return true;
            }

            /// <summary> vertices v and u are within the tolerance of each other </summary>
            bool NearPoint(int v, int u)
            {
                double dx = Scaled(k.X[v] - k.X[u]), dy = Scaled(k.Y[v] - k.Y[u]);
                return dx * dx + dy * dy <= scaledTolerance * scaledTolerance;
            }

            int shift;
            double scaledTolerance;
            double Scaled(double d) => ExactPoint3.ScaleB(d, shift);

            /// <summary> squared distance of p to segment a-b in scaled units; inside: the projection falls in the open segment </summary>
            double ScaledDist2(double px, double py, double ax, double ay, double bx, double by, out bool inside)
            {
                double dx = Scaled(bx - ax), dy = Scaled(by - ay), qx = Scaled(px - ax), qy = Scaled(py - ay);
                double l2 = dx * dx + dy * dy;
                inside = false;
                if (!(l2 > 0)) return qx * qx + qy * qy;
                double t = (qx * dx + qy * dy) / l2;
                inside = t > 0 && t < 1;
                t = Math.Max(0, Math.Min(1, t));
                double cx = t * dx - qx, cy = t * dy - qy;
                return cx * cx + cy * cy;
            }

            bool WithinTolerance(double px, double py, double ax, double ay, double bx, double by)
            {
                double d2 = ScaledDist2(px, py, ax, ay, bx, by, out bool inside);
                return inside && d2 <= scaledTolerance * scaledTolerance;
            }

            void Split(int v, Piece piece, Stack<Piece> pending)
            {
                pending.Push(new Piece { U = v, W = piece.W, Sources = piece.Sources });
                pending.Push(new Piece { U = piece.U, W = v, Sources = new List<int>(piece.Sources) });
            }

            internal ConstrainedTriangulation2Result InsertPiece(Piece piece, Stack<Piece> pending)
            {
                int u = piece.U, w = piece.W;
                if (u == w) return null;
                int existing = k.FindEdge(u, w);
                if (existing >= 0) {
                    if (k.Seg[existing] < 0) {
                        // a vertex within the tolerance of the piece becomes a vertex of it (O11) also when the edge exists already: such
                        // a vertex is the apex of a triangle at the edge (anything that close to the edge is in that triangle). Found with
                        // the corpus run (delaunator's issue43: points 5e-15 inside a hull edge, which refinement then split towards
                        // them down to the last bit)
                        foreach (int side in new[] { existing, k.H[existing] }) {
                            int apex = k.V[Next(Next(side))];
                            // (not one within the tolerance of an end: in a cluster of such points the order along the piece is not
                            // reliable in doubles, and splits could go back and forth)
                            if (apex != k.Infinite && Near(apex, u, w, piece.Sources) && !NearPoint(apex, u) && !NearPoint(apex, w)) { Split(apex, piece, pending); return null; }
                        }
                        k.SetConstraint(existing, k.NewConstraint(piece.Sources));
                    }
                    else {
                        // a union: one input segment covers each piece once, so a source already there is the same piece again
                        // (re-inserted by a split), not a second cover; appending it made the lists grow without bound (found with M3)
                        var list = k.Sources[k.Seg[existing]];
                        foreach (int src in piece.Sources) if (!list.Contains(src)) list.Add(src);
                    }
                    return null;
                }
                // the triangle at u whose wedge holds the direction to w
                int h = -1;
                foreach (int e in k.Star(u)) {
                    if (k.IsGhost(e / 3)) continue;
                    int a = k.V[Next(e)], b = k.V[Next(Next(e))];
                    int oa = k.OrientV(u, w, a), ob = k.OrientV(u, w, b);
                    if (oa == 0 && Ahead(u, w, a)) { Split(a, piece, pending); return null; }
                    if (ob == 0 && Ahead(u, w, b)) { Split(b, piece, pending); return null; }
                    if (oa < 0 && ob > 0) { h = Next(e); break; }
                }
                if (h < 0) throw new InvalidOperationException("ConstrainedDelaunayMesh2: no triangle at a segment's end point");
                {
                    int a = k.V[h], b = k.V[Next(h)];
                    if (Near(a, u, w, piece.Sources)) { Split(a, piece, pending); return null; }
                    if (Near(b, u, w, piece.Sources)) { Split(b, piece, pending); return null; }
                }
                var crossed = new List<long>();
                while (true) {
                    int right = k.V[h], left = k.V[Next(h)];
                    if (k.Seg[h] >= 0) return Cross(piece, h, pending);
                    crossed.Add(((long)right << 32) | (uint)left);
                    int g = k.H[h];
                    int v = k.V[Next(Next(g))];
                    if (v == w) break;
                    int ov = k.OrientV(u, w, v);
                    if (ov == 0 || Near(v, u, w, piece.Sources)) { Split(v, piece, pending); return null; }
                    h = ov > 0 ? Next(g) : Next(Next(g));
                }
                int edge = k.RecoverEdge(u, w, crossed);
                k.SetConstraint(edge, k.NewConstraint(piece.Sources));
                return null;
            }

            static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;

            /// <summary> a, collinear with u-w, lies on the side of u towards w (exact: coordinate comparisons) </summary>
            bool Ahead(int u, int w, int a)
            {
                if (k.X[w] != k.X[u]) return (k.X[w] > k.X[u]) == (k.X[a] > k.X[u]) && k.X[a] != k.X[u];
                return (k.Y[w] > k.Y[u]) == (k.Y[a] > k.Y[u]) && k.Y[a] != k.Y[u];
            }

            /// <summary> the piece crosses the constrained edge of half-edge h: split both at the rounded crossing </summary>
            ConstrainedTriangulation2Result Cross(Piece piece, int h, Stack<Piece> pending)
            {
                int u = piece.U, w = piece.W, r = k.V[h], l = k.V[Next(h)];
                var x = Intersection(k.Point(u), k.Point(w), k.Point(r), k.Point(l));
                if (!resolve) return Fail(Triangulation2FailureKind.CrossingConstraints, x, "constraints cross and ResolveCrossings is off");
                var other = k.Unconstrain(h);
                // without its constraint the edge r-l may not be Delaunay: restore that before inserting, since Lawson's insertion
                // assumes a constrained Delaunay triangulation (review of M2: a non-Delaunay edge survived otherwise)
                k.RestoreDelaunay(new[] { ((long)r << 32) | (uint)l });
                int before = k.VertexCount;
                // a vertex within the tolerance of the crossing and of every source of both pieces takes its place (O11): three segments
                // through one point that is not a double would otherwise leave a triangle a few ulps wide (found with M3)
                int p = NearVertex(x, piece.Sources, other);
                if (p < 0) p = k.InsertPoint(x.x, x.y);
                if (k.VertexCount > before) SetCrossingHeight(p, piece.Sources, other);
                pending.Push(new Piece { U = r, W = p, Sources = other });
                pending.Push(new Piece { U = p, W = l, Sources = new List<int>(other) });
                pending.Push(new Piece { U = p, W = w, Sources = piece.Sources });
                pending.Push(new Piece { U = u, W = p, Sources = new List<int>(piece.Sources) });
                return null;
            }

            int NearVertex(Vector2d x, List<int> a, List<int> c)
            {
                if (!(scaledTolerance > 0)) return -1;
                int t = k.LocatePoint(x.x, x.y, out int exact);
                if (exact >= 0) return exact;
                double tol2 = scaledTolerance * scaledTolerance;
                int best = -1; double bestD = double.MaxValue;
                // the corners of the containing triangle and of its neighbours
                for (int j = 0; j < 3; ++j) {
                    foreach (int s0 in new[] { t, k.H[3 * t + j] / 3 })
                        for (int i = 0; i < 3; ++i) {
                            int v = k.V[3 * s0 + i];
                            if (v == k.Infinite) continue;
                            double dx = Scaled(k.X[v] - x.x), dy = Scaled(k.Y[v] - x.y), d = dx * dx + dy * dy;
                            if (d > tol2 || d >= bestD) continue;
                            bool all = true;
                            foreach (int src in a.Concat(c)) {
                                var q = sources[src];
                                if (ScaledDist2(k.X[v], k.Y[v], q.AX, q.AY, q.BX, q.BY, out _) > tol2) { all = false; break; }
                            }
                            if (all) { best = v; bestD = d; }
                        }
                }
                return best;
            }

            /// <summary>
            /// The crossing of segments a-b and c-d (not parallel), computed exactly and rounded to a double point: every double is
            /// an integer times a power of two, so with a common exponent the crossing is a ratio of integers.
            /// </summary>
            internal static Vector2d Intersection(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                var m = new BigInteger[8];
                var ex = new int[8];
                double[] v = { a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y };
                int emin = int.MaxValue;
                for (int i = 0; i < 8; ++i) {
                    ExactPoint3.Decompose(v[i], out m[i], out ex[i]);
                    if (!m[i].IsZero) emin = Math.Min(emin, ex[i]);
                }
                if (emin == int.MaxValue) emin = 0;
                var q = new BigInteger[8];
                for (int i = 0; i < 8; ++i) q[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - emin);
                BigInteger ax = q[0], ay = q[1], bx = q[2], by = q[3], cx = q[4], cy = q[5], dx = q[6], dy = q[7];
                // t = cross(c - a, d - c) / cross(b - a, d - c)
                BigInteger den = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
                BigInteger num = (cx - ax) * (dy - cy) - (cy - ay) * (dx - cx);
                if (den.Sign < 0) { den = -den; num = -num; }
                BigInteger xn = ax * den + num * (bx - ax), yn = ay * den + num * (by - ay);
                return new Vector2d(RatioToDouble(xn, den, emin), RatioToDouble(yn, den, emin));
            }

            /// <summary>
            /// num / den * 2^scale (den > 0) rounded to nearest, ties to even, in integer arithmetic only: no conversion of a large
            /// BigInteger to double, whose rounding may differ between runtimes. Below the normal range a second rounding to the
            /// subnormal grid follows: the result is then within one unit in the last place, not always the nearest.
            /// </summary>
            internal static double RatioToDouble(BigInteger num, BigInteger den, int scale)
            {
                if (num.IsZero) return 0;
                bool negative = num.Sign < 0;
                if (negative) num = -num;
                long shift = 55 - (ExactPoint3.BitLength(num) - ExactPoint3.BitLength(den));
                BigInteger q = shift >= 0 ? BigInteger.DivRem(num << (int)shift, den, out BigInteger rem) : BigInteger.DivRem(num, den << (int)-shift, out rem);
                // q has 55 or 56 bits; keep 53, round with the dropped bits and the remainder as sticky
                int drop = (int)ExactPoint3.BitLength(q) - 53;
                BigInteger kept = q >> drop, dropped = q - (kept << drop), half = BigInteger.One << (drop - 1);
                int c = dropped.CompareTo(half);
                if (c > 0 || (c == 0 && !rem.IsZero) || (c == 0 && !kept.IsEven)) kept += 1;
                double mantissa = (double)(long)kept;   // exact: at most 2^53
                double r = ExactPoint3.ScaleB(mantissa, (int)(scale - shift + drop));
                return negative ? -r : r;
            }

            // ------------------------------------------------------------ domain, labels, output

            /// <summary>
            /// Domain membership from scratch, by ring parity (D5): per triangle slot the interned set of rings it lies inside of
            /// (each ring by the even-odd rule), and per set whether it is in the domain: the innermost ring of the set decides by its
            /// role (owner decision of 2026-11-01: islands in lakes are meshed, a hole in a hole stays removed). Ring r is inside ring
            /// q when every triangle inside r is inside q, read off the sets of the real triangles; the innermost ring of a set is
            /// inside all the others. Rings that overlap without nesting have none: the latest ring of the set decides, reported in
            /// overlaps.
            /// </summary>
            internal void RingParity(out int[] state, out List<int[]> sets, out bool[] inside) => RingParity(out state, out sets, out inside, out _);

            internal void RingParity(out int[] state, out List<int[]> sets, out bool[] inside, out List<int[]> overlaps)
            {
                int slots = k.TriangleSlots;
                // per constraint: the rings it toggles (odd multiplicity), as a sorted array
                var toggles = new int[k.Sources.Count][];
                var count = new Dictionary<int, int>();
                for (int c = 0; c < toggles.Length; ++c) {
                    count.Clear();
                    foreach (int s in k.Sources[c]) {
                        int ring = sources[s].Ring;
                        if (ring < 0) continue;
                        count.TryGetValue(ring, out int m);
                        count[ring] = m + 1;
                    }
                    toggles[c] = count.Where(p => (p.Value & 1) == 1).Select(p => p.Key).OrderBy(z => z).ToArray();
                }
                // the rings each triangle lies inside of (odd parity), by a breadth-first search from the outside; setList are interned
                var setList = new List<int[]> { new int[0] };
                var index = new Dictionary<string, int> { { "", 0 } };
                int Intern(int[] set)
                {
                    string key = string.Join(",", set);
                    if (!index.TryGetValue(key, out int id)) { id = setList.Count; setList.Add(set); index.Add(key, id); }
                    return id;
                }
                state = new int[slots];
                for (int t = 0; t < slots; ++t) state[t] = -1;
                var queue = new Queue<int>();
                for (int t = 0; t < slots; ++t) if (k.IsGhost(t)) { state[t] = 0; queue.Enqueue(t); }
                var xorCache = new Dictionary<long, int>();
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j, t2 = k.H[e] / 3;
                        if (state[t2] >= 0) continue;
                        int c = k.Seg[e];
                        if (c < 0 || toggles[c].Length == 0) state[t2] = state[t];
                        else {
                            long key = ((long)state[t] << 32) | (uint)c;
                            if (!xorCache.TryGetValue(key, out int s2)) xorCache.Add(key, s2 = Intern(Xor(setList[state[t]], toggles[c])));
                            state[t2] = s2;
                        }
                        queue.Enqueue(t2);
                    }
                }
                int baseRings = (input.Rings ?? new List<Mesh2Ring>()).Count;
                sets = setList;
                // the domain rings of each set (region rings are labels, not domain); the sets that occur in real triangles
                var domainRings = setList.Select(set => set.Where(r => r < baseRings).ToArray()).ToArray();
                var used = new bool[setList.Count];
                for (int t = 0; t < slots; ++t) if (!k.IsGhost(t)) used[state[t]] = true;
                // per ring: the rings that contain everything inside it (itself included), the intersection of the sets it is in
                var containers = new HashSet<int>[baseRings];
                for (int si = 0; si < setList.Count; ++si) {
                    if (!used[si]) continue;
                    foreach (int r in domainRings[si]) {
                        if (containers[r] == null) containers[r] = new HashSet<int>(domainRings[si]);
                        else containers[r].IntersectWith(domainRings[si]);
                    }
                }
                inside = new bool[setList.Count];
                overlaps = new List<int[]>();
                for (int si = 0; si < setList.Count; ++si) {
                    var rings = domainRings[si];
                    if (rings.Length == 0) { inside[si] = outerRings == 0 && !OuterRingsCollapsed; continue; }
                    int pick = -1;
                    foreach (int r in rings)
                        if (containers[r] != null && rings.All(q => containers[r].Contains(q))) pick = Math.Max(pick, r);
                    if (pick < 0) {
                        pick = rings.Max();
                        if (used[si]) overlaps.Add(rings);
                    }
                    inside[si] = !ringIsHole[pick];
                }
            }

            /// <summary> per triangle slot: in the domain by ring parity (hole seeds not applied); for the refinement's tag check, after the
            /// refinement constrained the hull of a hull domain </summary>
            internal bool[] InsideFromScratch()
            {
                RingParity(out var state, out _, out var inside);
                var result = new bool[state.Length];
                for (int t = 0; t < state.Length; ++t) result[t] = !k.IsGhost(t) && inside[state[t]];
                // a hull domain (D13) whose hull refinement constrained (ConstrainHull; a hull edge on an input segment keeps that
                // segment as its constraint): the domain ends at the constraints, so what the ghosts reach without crossing one is
                // outside, eg the sliver between the old, now unconstrained hull edge and the two pieces of a hull edge whose split
                // point rounded inwards, as the tags have it (found by the tag check on F-35 and F-3/c). All input lies inside the hull,
                // so no other constraint is met from outside. (The check then follows ConstrainHull's own rule: a hull edge that
                // ConstrainHull left unconstrained would leak in both; the oracle's area and coverage checks catch that.)
                // (with OuterRingsCollapsed nothing is inside already, and the flood changes nothing)
                if (outerRings == 0) {
                    var seen = new bool[state.Length];
                    var queue = new Queue<int>();
                    for (int t = 0; t < state.Length; ++t) if (k.IsGhost(t)) { seen[t] = true; queue.Enqueue(t); }
                    while (queue.Count > 0) {
                        int t = queue.Dequeue();
                        for (int j = 0; j < 3; ++j) {
                            int e = 3 * t + j, t2 = k.H[e] / 3;
                            if (seen[t2] || k.Seg[e] >= 0) continue;
                            seen[t2] = true; result[t2] = false; queue.Enqueue(t2);
                        }
                    }
                }
                return result;
            }

            ConstrainedTriangulation2Result Output(Vector2d[] inputVertices, int[] mapping)
            {
                int slots = k.TriangleSlots;
                RingParity(out var state, out var sets, out var inside, out var overlaps);
                int baseRings = (input.Rings ?? new List<Mesh2Ring>()).Count;
                var keep = new bool[slots];
                for (int t = 0; t < slots; ++t) keep[t] = !k.IsGhost(t) && inside[state[t]];
                // D5: where rings overlap without nesting, the latest of them decides; say so
                foreach (var rings in overlaps)
                    diagnostics.Add("rings " + string.Join(", ", rings) + " overlap without nesting; ring " + rings.Max() + " (the latest) decides where they overlap");
                // hole seeds (D6): the bounded cell of the constraints around the seed
                var holeSeeds = input.HoleSeeds ?? new List<Vector2d>();
                for (int i = 0; i < holeSeeds.Count; ++i) {
                    var cell = SeedCell(holeSeeds[i], keep, "hole seed " + i, requireBounded: true);
                    if (cell != null) foreach (int t in cell) keep[t] = false;
                }
                // regions, the last wins: per triangle the index of its region, -1 for none
                var regionOf = new int[slots];
                for (int t = 0; t < slots; ++t) regionOf[t] = -1;
                var seedRegionOf = new int[slots];   // per triangle: the index of the last seed region that labelled it, or -1
                for (int t = 0; t < slots; ++t) seedRegionOf[t] = -1;
                var regions = input.Regions ?? new List<Mesh2Region>();
                for (int g = 0; g < regions.Count; ++g) {
                    var region = regions[g];
                    if (region.Ring != null) {
                        // by ring: the latest region ring of each set, found once per set below
                        continue;
                    } else {
                        var cell = SeedCell(region.Seed, keep, "region seed " + g, requireBounded: false);
                        if (cell != null) foreach (int t in cell) if (keep[t]) { regionOf[t] = g; seedRegionOf[t] = g; }
                    }
                }
                // region rings: per distinct set, the region ring with the highest index wins over seeds of lower index
                var regionOfSet = new int[sets.Count];
                for (int si = 0; si < sets.Count; ++si) {
                    regionOfSet[si] = -1;
                    foreach (int ring in sets[si]) if (ring >= baseRings) regionOfSet[si] = Math.Max(regionOfSet[si], ring - baseRings);
                }
                if (regionRings > 0) {
                    // seeds were applied in order above; a ring region overrides a seed region of a lower index only
                    for (int t = 0; t < slots; ++t) {
                        if (!keep[t]) continue;
                        int g = regionOfSet[state[t]];
                        if (g >= 0 && g >= seedRegionOf[t]) regionOf[t] = g;
                    }
                }
                // the tag of a triangle: -1 outside the domain, else 1 + its region (0: none); splits and flips pass it on
                for (int t = 0; t < slots; ++t) k.Tag[t] = keep[t] ? regionOf[t] + 1 : -1;
                if (refine != null) {
                    SnapshotReference();
                    var failure = refine(this);
                    if (failure != null) return failure;
                }
                return Build(inputVertices, mapping);
            }

            static int[] Xor(int[] a, int[] b)
            {
                var r = new List<int>(a.Length + b.Length);
                int i = 0, j = 0;
                while (i < a.Length || j < b.Length) {
                    if (j == b.Length || (i < a.Length && a[i] < b[j])) r.Add(a[i++]);
                    else if (i == a.Length || b[j] < a[i]) r.Add(b[j++]);
                    else { ++i; ++j; }
                }
                return r.ToArray();
            }

            /// <summary>
            /// The triangles of the cell of the constraints that contains the seed strictly inside a kept triangle; null (with a
            /// diagnostic) if the seed is outside, on a constraint or vertex, in a removed area, or (for holes) the cell is unbounded.
            /// </summary>
            List<int> SeedCell(Vector2d seed, bool[] keep, string what, bool requireBounded)
            {
                int t = k.LocatePoint(seed.x, seed.y, out int vertex);
                if (k.IsGhost(t) || !keep[t]) { diagnostics.Add(what + " is outside the domain or in a removed area and is ignored"); return null; }
                if (vertex >= 0) { diagnostics.Add(what + " is on a vertex and is ignored"); return null; }
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (ExactPredicates.Orient2d(k.Point(k.V[e]), k.Point(k.V[Next(e)]), seed) == 0) {
                        if (k.Seg[e] >= 0) { diagnostics.Add(what + " is on a constraint and is ignored"); return null; }
                    }
                }
                var cell = new List<int> { t };
                var seen = new HashSet<int> { t };
                for (int i = 0; i < cell.Count; ++i) {
                    int c = cell[i];
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * c + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2)) {
                            if (requireBounded) { diagnostics.Add(what + " lies in an unbounded cell and removes nothing"); return null; }
                            continue;
                        }
                        if (seen.Add(t2)) cell.Add(t2);
                    }
                }
                return cell;
            }

            ConstrainedTriangulation2Result Build(Vector2d[] inputVertices, int[] mapping)
            {
                int slots = k.TriangleSlots;
                var regions = input.Regions ?? new List<Mesh2Region>();
                var keep = new bool[slots];
                for (int t = 0; t < slots; ++t) keep[t] = k.Tag[t] >= 0 && !k.IsGhost(t);
                // vertex ids: input vertices by representative; new vertices numbered from n in order of first use
                var outId = new int[k.VertexCount];
                for (int v = 0; v < outId.Length; ++v) outId[v] = -1;
                for (int r = 0; r < n; ++r) if (k.Vertex[r] == r) outId[r] = k.Representative[r];
                var extra = new List<Vector2d>();
                var extraHeights = new List<double>();
                var id = new int[slots];
                int count = 0;
                for (int t = 0; t < slots; ++t) {
                    id[t] = keep[t] ? count++ : -1;
                    if (!keep[t]) continue;
                    for (int j = 0; j < 3; ++j) {
                        int v = k.V[3 * t + j];
                        if (outId[v] >= 0) continue;
                        outId[v] = n + extra.Count;
                        extra.Add(k.Point(v));
                        if (heights != null) extraHeights.Add(heights[v]);
                    }
                }
                var triangles = new Index3i[count];
                var neighbours = new Index3i[count];
                var labels = new int[count];
                var subsegments = new List<Mesh2Subsegment>();
                for (int t = 0; t < slots; ++t) {
                    int i = id[t];
                    if (i < 0) continue;
                    int a = outId[k.V[3 * t]], b = outId[k.V[3 * t + 1]], c = outId[k.V[3 * t + 2]];
                    int rot = a < b && a < c ? 0 : b < c ? 1 : 2;
                    int e0 = 3 * t + rot, e1 = 3 * t + (rot + 1) % 3, e2 = 3 * t + (rot + 2) % 3;
                    triangles[i] = new Index3i(outId[k.V[e0]], outId[k.V[e1]], outId[k.V[e2]]);
                    neighbours[i] = new Index3i(id[k.H[e1] / 3], id[k.H[e2] / 3], id[k.H[e0] / 3]);
                    labels[i] = k.Tag[t] > 0 ? regions[k.Tag[t] - 1].Label : 0;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j, c2 = k.Seg[e];
                        if (c2 < 0) continue;
                        int nb = id[k.H[e] / 3];
                        if (nb >= 0 && nb < i) continue;
                        subsegments.Add(Subsegment(outId[k.V[e]], outId[k.V[Next(e)]], Covering(k.V[e], k.V[Next(e)], k.Sources[c2])));
                    }
                }
                var vertices = inputVertices.Concat(extra).ToArray();
                double[] outHeights = null;
                if (heights != null) {
                    outHeights = new double[vertices.Length];
                    for (int i = 0; i < n; ++i) outHeights[i] = input.Heights[i];
                    for (int i = 0; i < n; ++i) outHeights[i] = outHeights[mapping[i]];
                    for (int j = 0; j < extra.Count; ++j) outHeights[n + j] = extraHeights[j];
                }
                // the full triangulation behind the mesh, for point location
                var fullP = new Vector2d[k.VertexCount];
                for (int v = 0; v < fullP.Length; ++v) if (v != k.Infinite) fullP[v] = k.Point(v);
                var fullV = new int[3 * slots]; Array.Copy(k.V, fullV, fullV.Length);
                var fullH = new int[3 * slots]; Array.Copy(k.H, fullH, fullH.Length);
                var mesh = new ConstrainedMesh2(vertices, n, mapping, triangles, neighbours, labels, subsegments.ToArray(), outHeights, fullV, fullH, id, fullP, k.Infinite);
                var bad = new List<int>();
                if (IsBad != null)
                    for (int t = 0; t < slots; ++t) if (id[t] >= 0 && IsBad(t)) bad.Add(id[t]);
                mesh.BadTriangles = bad.AsReadOnly();
                mesh.SteinerLimitReached = SteinerLimitReached;
                var status = count == 0 ? Triangulation2Status.Empty : Triangulation2Status.Success;
                return new ConstrainedTriangulation2Result(status, Triangulation2FailureKind.None, mesh, diagnostics);
            }

            /// <summary>
            /// The sources that cover the edge u-w: its own, and those of constraints at its end points whose input segment passes
            /// within the automatic tolerance (twice the snap tolerance, 2^-40 relative) of both ends. Nearly coincident lines count as
            /// one there (spec O11, R8), so a short piece between two crossings of nearly parallel lines carries both markers.
            /// </summary>
            List<int> Covering(int u, int w, List<int> own)
            {
                var result = new List<int>(own);
                double tube = 2 * scaledTolerance;
                if (!(tube > 0)) return result;
                if (cellKeys == null) BuildGrid();
                if (cellKeys.Length == 0) return result;
                double ux = Scaled(k.X[u] - gridX), uy = Scaled(k.Y[u] - gridY), wx = Scaled(k.X[w] - gridX), wy = Scaled(k.Y[w] - gridY);
                double tube2 = tube * tube;
                void Consider(int s)
                {
                    if (sax[s] != sax[s] || result.Contains(s)) return;   // NaN: a hull source
                    if (SegDist2(ux, uy, s) <= tube2 && SegDist2(wx, wy, s) <= tube2) result.Add(s);
                }
                // every source is in the cells it passes through; one that passes within the tube of u is in u's cell or in a cell
                // across a border within the tube (review of M2: the star of u can hide a source)
                double x = ux / gridCell, y = uy / gridCell, r = tube / gridCell;
                long x0 = (long)Math.Floor(x - r), x1 = (long)Math.Floor(x + r), y0 = (long)Math.Floor(y - r), y1 = (long)Math.Floor(y + r);
                for (long cx = x0; cx <= x1; ++cx)
                    for (long cy = y0; cy <= y1; ++cy) {
                        int at = Array.BinarySearch(cellKeys, (cx << 32) ^ (cy & 0xffffffff));
                        if (at < 0) continue;
                        for (int q = cellStart[at]; q < cellStart[at + 1]; ++q) Consider(cellSources[q]);
                    }
                // and the constraints of the triangles at both ends (in case rounding in the traversal skipped a corner cell)
                foreach (int v in new[] { u, w })
                    foreach (int e0 in k.Star(v))
                        for (int j = 0; j < 3; ++j) {
                            int c = k.Seg[3 * (e0 / 3) + j];
                            if (c >= 0) foreach (int s in k.Sources[c]) Consider(s);
                        }
                return result;
            }

            /// <summary>
            /// The point lies within the automatic tolerance of a source other than the given ones (the closed segment). For the
            /// refinement: a split point there would merge the two constraints' chains (O11), which refinement must not cause.
            /// </summary>
            internal bool NearOtherSource(double px, double py, List<int> own)
            {
                if (!(scaledTolerance > 0)) return false;
                if (cellKeys == null) BuildGrid();
                if (cellKeys.Length == 0) return false;
                double x = Scaled(px - gridX) / gridCell, y = Scaled(py - gridY) / gridCell, r = scaledTolerance / gridCell;
                long x0 = (long)Math.Floor(x - r), x1 = (long)Math.Floor(x + r), y0 = (long)Math.Floor(y - r), y1 = (long)Math.Floor(y + r);
                double tol2 = scaledTolerance * scaledTolerance;
                for (long cx = x0; cx <= x1; ++cx)
                    for (long cy = y0; cy <= y1; ++cy) {
                        int at = Array.BinarySearch(cellKeys, (cx << 32) ^ (cy & 0xffffffff));
                        if (at < 0) continue;
                        for (int q = cellStart[at]; q < cellStart[at + 1]; ++q) {
                            int src = cellSources[q];
                            if (own.Contains(src)) continue;
                            var o = sources[src];
                            if (ScaledDist2(px, py, o.AX, o.AY, o.BX, o.BY, out _) <= tol2) return true;
                        }
                    }
                return false;
            }

            /// <summary> squared distance of a point (scaled, relative to the grid origin) to source s </summary>
            double SegDist2(double px, double py, int s)
            {
                double ax = sax[s], ay = say[s], dx = sbx[s] - ax, dy = sby[s] - ay, qx = px - ax, qy = py - ay;
                double l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, (qx * dx + qy * dy) / l2)) : 0;
                double cx = t * dx - qx, cy = t * dy - qy;
                return cx * cx + cy * cy;
            }

            // a uniform grid over the sources' bounding box, as sorted cell keys with the sources of each cell (CSR); the sources'
            // end points scaled and relative to the grid origin
            long[] cellKeys;
            int[] cellStart, cellSources;
            double[] sax, say, sbx, sby;
            double gridX, gridY, gridCell;

            void BuildGrid()
            {
                int m = sources.Count;
                sax = new double[m]; say = new double[m]; sbx = new double[m]; sby = new double[m];
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var q in sources) {
                    if (q.Hull) continue;
                    minX = Math.Min(minX, Math.Min(q.AX, q.BX)); maxX = Math.Max(maxX, Math.Max(q.AX, q.BX));
                    minY = Math.Min(minY, Math.Min(q.AY, q.BY)); maxY = Math.Max(maxY, Math.Max(q.AY, q.BY));
                }
                if (minX > maxX) { cellKeys = new long[0]; return; }
                gridX = minX; gridY = minY;
                double extent = Math.Max(Scaled(maxX - minX), Scaled(maxY - minY));
                gridCell = Math.Max(extent / Math.Max(1, Math.Sqrt(m)), 64 * scaledTolerance);
                var keys = new List<long>(4 * m);
                var values = new List<int>(4 * m);
                for (int s = 0; s < m; ++s) {
                    var q = sources[s];
                    if (q.Hull) { sax[s] = double.NaN; continue; }
                    sax[s] = Scaled(q.AX - gridX); say[s] = Scaled(q.AY - gridY); sbx[s] = Scaled(q.BX - gridX); sby[s] = Scaled(q.BY - gridY);
                    int source = s;
                    Traverse(sax[s] / gridCell, say[s] / gridCell, sbx[s] / gridCell, sby[s] / gridCell, key => { keys.Add(key); values.Add(source); });
                }
                var ka = keys.ToArray(); var va = values.ToArray();
                Array.Sort(ka, va);
                var unique = new List<long>(); var start = new List<int>();
                for (int i = 0; i < ka.Length; ++i)
                    if (i == 0 || ka[i] != ka[i - 1]) { unique.Add(ka[i]); start.Add(i); }
                start.Add(ka.Length);
                cellKeys = unique.ToArray(); cellStart = start.ToArray(); cellSources = va;
            }

            /// <summary> the unit cells a segment passes through, in order (Amanatides and Woo 1987), each once </summary>
            static void Traverse(double ax, double ay, double bx, double by, Action<long> visit)
            {
                long cx = (long)Math.Floor(ax), cy = (long)Math.Floor(ay), ex = (long)Math.Floor(bx), ey = (long)Math.Floor(by);
                double dx = bx - ax, dy = by - ay;
                int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1;
                double tdx = dx != 0 ? Math.Abs(1 / dx) : double.PositiveInfinity, tdy = dy != 0 ? Math.Abs(1 / dy) : double.PositiveInfinity;
                double tx = dx != 0 ? ((sx > 0 ? cx + 1 - ax : ax - cx) * tdx) : double.PositiveInfinity;
                double ty = dy != 0 ? ((sy > 0 ? cy + 1 - ay : ay - cy) * tdy) : double.PositiveInfinity;
                long guard = Math.Abs(ex - cx) + Math.Abs(ey - cy) + 2;
                for (long i = 0; i <= guard; ++i) {
                    visit((cx << 32) ^ (cy & 0xffffffff));
                    if (cx == ex && cy == ey) break;
                    if (tx < ty) { tx += tdx; cx += sx; } else { ty += tdy; cy += sy; }
                }
            }

            double Dist2(double px, double py, Source q) => ScaledDist2(px, py, q.AX, q.AY, q.BX, q.BY, out _);

            Mesh2Subsegment Subsegment(int a, int b, List<int> src)
            {
                var markers = new SortedSet<int>();
                bool locked = false;
                foreach (int s in src) {
                    if (sources[s].Marker != 0) markers.Add(sources[s].Marker);
                    locked |= sources[s].Locked;
                }
                return new Mesh2Subsegment { A = a, B = b, Markers = markers.ToArray(), Marker = markers.Count == 1 ? markers.Min : 0, Locked = locked };
            }
        }
    }
}
