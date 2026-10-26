// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    public enum GeodesicPathStatus
    {
        /// <summary> the path is a geodesic: at every free vertex on it the angle is at least pi on both sides </summary>
        Success,
        /// <summary> a closed loop without fixed vertices contracted to a point (Length 0, Path and PathVertices one point) </summary>
        Collapsed,
        /// <summary> MaxIterations reached; the path is valid and shorter than the initial one, but not yet a geodesic </summary>
        NotConverged,
        /// <summary>
        /// some vertex with an angle below pi could not be shortened, because the edges that would have to be
        /// flipped carry other parts of the same (self-overlapping) path; the path is valid
        /// </summary>
        Stuck,
        /// <summary> two consecutive waypoints are not connected </summary>
        NoPath,
        /// <summary> invalid vertex ids, an edge path whose vertices are not adjacent, or a non-manifold mesh </summary>
        InvalidInput
    }

    /// <summary>
    /// Geodesic paths and loops on a triangle mesh by edge flips (FlipOut: Sharp and Crane, "You Can Find
    /// Geodesic Paths in Triangle Meshes by Just Flipping Edges", ACM TOG 2020). The path starts as a path
    /// along mesh edges (Dijkstra between the waypoints, or one given), on an IntrinsicTriangulation of the
    /// mesh. As long as the path turns at a free vertex by less than pi on one side, the edges inside that
    /// wedge are flipped until the outer boundary of the wedge is convex, and the path is rerouted along it,
    /// which shortens it. The result is a geodesic in the exact (intrinsic) geometry of the mesh: straight
    /// across triangles and edges, through a vertex only where the angle is at least pi on both sides
    /// (saddles, boundary corners), in the same homotopy class as the initial path. The mesh is not modified.
    ///
    /// Compute(a, b) gives a geodesic between two vertices that is locally shortest; from the Dijkstra path it
    /// is usually, but not always, the globally shortest one. ComputeThrough connects waypoints (kept fixed),
    /// optionally closed. Shorten tightens a given path along mesh edges; with closed = true and no fixed
    /// vertex it gives a closed geodesic (or Collapsed for a contractible loop).
    ///
    /// Vertices must be manifold (see IntrinsicTriangulation); boundaries are allowed, a path does not leave
    /// the surface. Lengths are intrinsic edge lengths (with IntrinsicTriangulation's mollification, 0 for
    /// meshes without degenerate triangles).
    /// </summary>
    public class MeshGeodesicPath
    {
        public DMesh3 Mesh { get; private set; }

        /// <summary> a free vertex is straight if the angle is at least pi - AngleTolerance on both sides (radians) </summary>
        public double AngleTolerance = 1e-6;
        /// <summary> maximum number of shortening steps (0: 100 * initial path edges + 1000) </summary>
        public int MaxIterations = 0;

        public GeodesicPathStatus Status { get; private set; }
        /// <summary> length of the result </summary>
        public double Length { get; private set; }
        /// <summary> length of the initial path along mesh edges </summary>
        public double InitialLength { get; private set; }
        /// <summary> number of shortening steps of the last computation </summary>
        public int Iterations { get; private set; }
        /// <summary> number of edge flips of the last computation </summary>
        public int Flips { get; private set; }
        /// <summary>
        /// the path as a polyline on the mesh surface: its vertices and the points where it crosses mesh edges
        /// (a closed path ends with its first point again); empty if the computation failed
        /// </summary>
        public List<Vector3d> Path { get; private set; }
        /// <summary>
        /// mesh vertex ids the path passes through, in order: start, waypoints, vertices where it bends (saddles,
        /// boundary corners) or runs straight through a vertex, end (a closed path does not repeat its first one)
        /// </summary>
        public List<int> PathVertices { get; private set; }
        /// <summary> the triangulation of the last computation, with the path's edges (flipped; the mesh is unchanged) </summary>
        public IntrinsicTriangulation Triangulation { get; private set; }

        public MeshGeodesicPath(DMesh3 mesh)
        {
            Mesh = mesh;
            Path = new List<Vector3d>();
            PathVertices = new List<int>();
        }


        /// <summary> geodesic between two mesh vertices </summary>
        public GeodesicPathStatus Compute(int source, int target)
        {
            return ComputeThrough(new[] { source, target }, false);
        }

        /// <summary>
        /// geodesic through the waypoints (mesh vertex ids) in this order, which stay fixed; closed: also back
        /// from the last to the first (needs at least two waypoints)
        /// </summary>
        public GeodesicPathStatus ComputeThrough(IList<int> waypoints, bool closed = false)
        {
            if (begin() == false) return Status;
            var pts = new List<int>();
            foreach (int vid in waypoints) {
                int i = T.VertexIndex(vid);
                if (i < 0) return fail(GeodesicPathStatus.InvalidInput);
                if (pts.Count == 0 || pts[pts.Count - 1] != i) pts.Add(i);
            }
            if (closed && pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
            if (pts.Count == 0 || (closed && pts.Count < 2)) return fail(GeodesicPathStatus.InvalidInput);
            segs = new List<int>();
            fixedTail = new List<bool>();
            int legs = closed ? pts.Count : pts.Count - 1;
            for (int k = 0; k < legs; ++k) {
                int n0 = segs.Count;
                if (dijkstra(pts[k], pts[(k + 1) % pts.Count], segs) == false) return fail(GeodesicPathStatus.NoPath);
                for (int j = n0; j < segs.Count; ++j) fixedTail.Add(j == n0);
            }
            if (segs.Count == 0) {      // a single vertex
                Status = GeodesicPathStatus.Success;
                PathVertices.Add(T.VertexID(pts[0]));
                Path.Add(T.VertexPosition(pts[0]));
                return Status;
            }
            this.closed = closed;
            return run();
        }

        /// <summary>
        /// tightens a path along mesh edges, given by its mesh vertex ids (consecutive ones must share an edge):
        /// open, it keeps its end points; closed (the last vertex connects back to the first, which need not be
        /// repeated) it has no fixed vertex. The vertices of the path that are in fixedVertices (mesh ids) stay
        /// fixed too.
        /// </summary>
        public GeodesicPathStatus Shorten(IList<int> edgePath, bool closed = false, ICollection<int> fixedVertices = null)
        {
            if (begin() == false) return Status;
            var pts = new List<int>();
            foreach (int vid in edgePath) {
                int i = T.VertexIndex(vid);
                if (i < 0) return fail(GeodesicPathStatus.InvalidInput);
                pts.Add(i);
            }
            if (closed && pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 2) return fail(GeodesicPathStatus.InvalidInput);
            segs = new List<int>();
            fixedTail = new List<bool>();
            int legs = closed ? pts.Count : pts.Count - 1;
            for (int k = 0; k < legs; ++k) {
                int s = find_segment(pts[k], pts[(k + 1) % pts.Count]);
                if (s == int.MinValue) return fail(GeodesicPathStatus.InvalidInput);
                segs.Add(s);
                fixedTail.Add((closed == false && k == 0) || (fixedVertices != null && fixedVertices.Contains(T.VertexID(pts[k]))));
            }
            this.closed = closed;
            return run();
        }


        // ---------------------------------------------------------------- path representation
        //
        // The path is a list of segments, each an intrinsic edge with a direction: s >= 0 is halfedge s from its
        // tail to its head; s < 0 is the halfedge ~s backwards, used only for boundary halfedges (an interior
        // edge backwards is its twin). fixedTail[k]: the tail of segment k is a fixed vertex (open paths: the
        // start; waypoints). Joint k is the tail of segment k, between segment k-1 and k (k >= 1 for open paths).

        IntrinsicTriangulation T;
        List<int> segs;
        List<bool> fixedTail;
        bool closed;
        int emptyVertex;        // the vertex a path without segments is at

        int seg_tail(int s) { return s >= 0 ? T.Tail(s) : T.Head(~s); }
        int seg_head(int s) { return s >= 0 ? T.Head(s) : T.Tail(~s); }
        double seg_length(int s) { return T.Length(s >= 0 ? s : ~s); }
        static int seg_halfedge(int s) { return s >= 0 ? s : ~s; }
        int seg_reverse(int s)
        {
            if (s < 0) return ~s;
            int tw = T.Twin(s);
            return tw >= 0 ? tw : ~s;
        }

        bool begin()
        {
            Path = new List<Vector3d>();
            PathVertices = new List<int>();
            Length = InitialLength = 0;
            Iterations = Flips = 0;
            segs = null; fixedTail = null; closed = false;
            try {
                T = new IntrinsicTriangulation(Mesh);
            } catch (ArgumentException) {
                T = null;
                Triangulation = null;
                Status = GeodesicPathStatus.InvalidInput;
                return false;
            }
            Triangulation = T;
            return true;
        }

        GeodesicPathStatus fail(GeodesicPathStatus status)
        {
            Status = status;
            Path = new List<Vector3d>();
            PathVertices = new List<int>();
            Length = 0;
            return status;
        }

        // the edges at vertex b counter-clockwise: the outgoing halfedges starting at VertexHalfedge(b), at a
        // boundary vertex followed by the incoming boundary halfedge (the last edge); cum[k] is the angle from
        // edge 0 to edge k (interior vertices: cum has one more entry, the angle sum)
        void build_fan(int b, List<int> fan, List<double> cum, out bool boundary)
        {
            fan.Clear(); cum.Clear();
            boundary = T.IsBoundaryVertex(b);
            int h0 = T.VertexHalfedge(b), h = h0;
            double a = 0;
            cum.Add(0);
            do {
                fan.Add(h);
                a += T.CornerAngle(h);
                cum.Add(a);
                h = T.NextAroundVertex(h);
            } while (h >= 0 && h != h0 && fan.Count <= T.HalfedgeCount);
            if (boundary) fan.Add(IntrinsicTriangulation.Prev(fan[fan.Count - 1]));
        }

        // index in the fan of b of the edge of a segment that starts at b
        static int fan_index(List<int> fan, int s)
        {
            int h = seg_halfedge(s);
            return fan.IndexOf(h);
        }

        // segment from vertex index u to v (an edge of the unflipped triangulation), int.MinValue if none
        int find_segment(int u, int v)
        {
            var fan = new List<int>(); var cum = new List<double>();
            bool boundary;
            build_fan(u, fan, cum, out boundary);
            int n = boundary ? fan.Count - 1 : fan.Count;
            for (int k = 0; k < n; ++k)
                if (T.Head(fan[k]) == v) return fan[k];
            if (boundary && T.Tail(fan[n]) == v) return ~fan[n];
            return int.MinValue;
        }

        // shortest path along the edges from vertex index a to b (ties by vertex index), appended to path
        bool dijkstra(int a, int b, List<int> path)
        {
            int N = T.VertexCount;
            double[] dist = new double[N];
            int[] pred = new int[N];
            for (int i = 0; i < N; ++i) { dist[i] = double.MaxValue; pred[i] = int.MinValue; }
            dist[a] = 0;
            var heap = new SortedSet<KeyValuePair<double, int>>(new DistComparer());
            heap.Add(new KeyValuePair<double, int>(0, a));
            var fan = new List<int>(); var cum = new List<double>();
            while (heap.Count > 0) {
                var top = heap.Min;
                heap.Remove(top);
                int u = top.Value;
                if (u == b) break;
                bool boundary;
                build_fan(u, fan, cum, out boundary);
                for (int k = 0; k < fan.Count; ++k) {
                    int s = (boundary && k == fan.Count - 1) ? ~fan[k] : fan[k];
                    int v = seg_head(s);
                    double d = dist[u] + seg_length(s);
                    if (d < dist[v]) {
                        if (dist[v] != double.MaxValue) heap.Remove(new KeyValuePair<double, int>(dist[v], v));
                        dist[v] = d; pred[v] = s;
                        heap.Add(new KeyValuePair<double, int>(d, v));
                    }
                }
            }
            if (dist[b] == double.MaxValue) return false;
            var rev = new List<int>();
            for (int v = b; v != a; v = seg_tail(pred[v])) rev.Add(pred[v]);
            for (int k = rev.Count - 1; k >= 0; --k) path.Add(rev[k]);
            return true;
        }

        class DistComparer : IComparer<KeyValuePair<double, int>>
        {
            public int Compare(KeyValuePair<double, int> x, KeyValuePair<double, int> y)
            {
                int c = x.Key.CompareTo(y.Key);
                return c != 0 ? c : x.Value.CompareTo(y.Value);
            }
        }


        // ---------------------------------------------------------------- FlipOut

        // the wedge at a joint: edges iFrom to iTo counter-clockwise in the fan of b, its angle, and whether it
        // runs from the outgoing to the incoming segment
        struct Wedge
        {
            public int From, To;
            public double Angle;
            public bool OutToIn;
            public bool Backtrack;      // the path returns along the edge it came from
        }

        // the smaller side of joint k (vertex b = tail of segs[k], incoming segs[k-1]); Angle = +inf if no side is finite
        Wedge joint_wedge(int k, List<int> fan, List<double> cum)
        {
            int m = segs.Count;
            int sIn = segs[(k - 1 + m) % m], sOut = segs[k];
            int b = seg_tail(sOut);
            bool boundary;
            build_fan(b, fan, cum, out boundary);
            int iOut = fan_index(fan, sOut), iIn = fan_index(fan, seg_reverse(sIn));
            var w = new Wedge() { Angle = double.PositiveInfinity };
            if (iOut < 0 || iIn < 0) return w;
            if (iIn == iOut) { w.Backtrack = true; w.Angle = 0; w.From = iOut; w.To = iIn; return w; }
            if (boundary) {
                // only the side without the gap is finite
                if (iOut < iIn) { w.From = iOut; w.To = iIn; w.OutToIn = true; } else { w.From = iIn; w.To = iOut; w.OutToIn = false; }
                w.Angle = cum[w.To] - cum[w.From];
                return w;
            }
            int n = fan.Count;
            double total = cum[n];
            double outToIn = iIn > iOut ? cum[iIn] - cum[iOut] : total - (cum[iOut] - cum[iIn]);
            double inToOut = total - outToIn;
            if (outToIn <= inToOut) { w.From = iOut; w.To = iIn; w.OutToIn = true; w.Angle = outToIn; } else { w.From = iIn; w.To = iOut; w.OutToIn = false; w.Angle = inToOut; }
            return w;
        }

        bool is_path_edge(int h)
        {
            int tw = T.Twin(h);
            foreach (int s in segs) {
                int x = seg_halfedge(s);
                if (x == h || x == tw) return true;
            }
            return false;
        }

        // flips the edge of h (and updates the path's halfedges, which the flip may move)
        bool flip(int h)
        {
            int ta = T.Twin(h);
            if (T.FlipEdge(h) == false) return false;
            Flips++;
            for (int j = 0; j < segs.Count; ++j) {
                int s = segs[j];
                int x = IntrinsicTriangulation.FlipRelocated(seg_halfedge(s), h, ta);
                segs[j] = s >= 0 ? x : ~x;
            }
            return true;
        }

        // FlipOut at joint k: flips the edges inside the wedge until its outer chain is convex, then replaces the
        // two segments at the joint by the chain if that is shorter. Returns false if the joint is stuck.
        bool flip_out(int k, List<int> fan, List<double> cum)
        {
            int m = segs.Count;
            int kIn = (k - 1 + m) % m;
            Wedge w = joint_wedge(k, fan, cum);
            if (w.Backtrack) {
                // a -> b -> a: drop both segments; the two joints at a merge (fixed if either was)
                int a = seg_tail(segs[kIn]);
                bool hasNext = closed || k + 1 < m;
                bool merged = fixedTail[kIn] || (hasNext && fixedTail[(k + 1) % m]);
                remove_pair(kIn, k);
                if (segs.Count == 0) { emptyVertex = a; return true; }
                if (hasNext) {
                    int at = (kIn > k || k + 1 == m) ? 0 : kIn;     // where the segment after the pair is now
                    fixedTail[at] = merged;
                }
                return true;
            }
            int n = fan.Count;
            // flip interior edges of the wedge (not carrying the path) until none can be flipped
            while (true) {
                bool flipped = false;
                for (int i = w.From + 1; ; ++i) {
                    int idx = i % n;
                    if (idx == w.To) break;
                    int e = fan[idx];
                    if (T.Twin(e) < 0 || is_path_edge(e)) continue;
                    if (flip(e)) { flipped = true; break; }
                }
                if (flipped == false) break;
                w = joint_wedge(k, fan, cum);
                n = fan.Count;
            }
            // the outer chain of the wedge: the edges opposite b, from the head of edge From to the head of edge To
            var chain = new List<int>();
            double chainLen = 0;
            for (int i = w.From; ; ++i) {
                int idx = i % n;
                if (idx == w.To) break;
                int o = IntrinsicTriangulation.Next(fan[idx]);
                chain.Add(o);
                chainLen += T.Length(o);
            }
            double oldLen = seg_length(segs[kIn]) + seg_length(segs[k]);
            if (!(chainLen < oldLen)) return false;
            if (w.OutToIn) {
                chain.Reverse();
                for (int j = 0; j < chain.Count; ++j) chain[j] = seg_reverse(chain[j]);
            }
            replace_pair(kIn, k, chain);
            return true;
        }

        void remove_pair(int kIn, int k)
        {
            if (kIn > k) {      // closed, kIn = m - 1, k = 0
                segs.RemoveAt(kIn); fixedTail.RemoveAt(kIn);
                segs.RemoveAt(0); fixedTail.RemoveAt(0);
            } else {
                segs.RemoveRange(kIn, 2); fixedTail.RemoveRange(kIn, 2);
            }
        }

        void replace_pair(int kIn, int k, List<int> chain)
        {
            bool fixedA = fixedTail[kIn];
            var flags = new List<bool>();
            for (int j = 0; j < chain.Count; ++j) flags.Add(j == 0 && fixedA);
            if (kIn > k) {      // closed, wrapped: the chain goes to the end, the first segment is dropped
                segs.RemoveAt(kIn); fixedTail.RemoveAt(kIn);
                segs.RemoveAt(0); fixedTail.RemoveAt(0);
                segs.AddRange(chain); fixedTail.AddRange(flags);
            } else {
                segs.RemoveRange(kIn, 2); fixedTail.RemoveRange(kIn, 2);
                segs.InsertRange(kIn, chain); fixedTail.InsertRange(kIn, flags);
            }
        }

        double path_length()
        {
            double l = 0;
            foreach (int s in segs) l += seg_length(s);
            return l;
        }

        GeodesicPathStatus run()
        {
            InitialLength = path_length();
            int maxIter = MaxIterations > 0 ? MaxIterations : 100 * segs.Count + 1000;
            double straight = Math.PI - AngleTolerance;
            var fan = new List<int>(); var cum = new List<double>();
            // joints that could not be shortened, by their two segments; cleared when the path changes
            var stuck = new HashSet<long>();
            GeodesicPathStatus status = GeodesicPathStatus.Success;
            while (true) {
                if (closed && segs.Count == 0) { status = GeodesicPathStatus.Collapsed; break; }
                // the free joint with the smallest angle below pi
                int m = segs.Count, best = -1;
                double bestAngle = straight;
                bool anyStuck = false;
                for (int k = closed ? 0 : 1; k < m; ++k) {
                    if (fixedTail[k]) continue;
                    Wedge w = joint_wedge(k, fan, cum);
                    if (!(w.Angle < straight)) continue;
                    if (stuck.Contains(joint_key(k))) { anyStuck = true; continue; }
                    if (w.Angle < bestAngle || best < 0) { best = k; bestAngle = w.Angle; }
                }
                if (best < 0) { status = anyStuck ? GeodesicPathStatus.Stuck : GeodesicPathStatus.Success; break; }
                if (Iterations >= maxIter) { status = GeodesicPathStatus.NotConverged; break; }
                Iterations++;
                if (flip_out(best, fan, cum)) stuck.Clear();
                else stuck.Add(joint_key(best));
            }
            Status = status;
            build_output();
            return Status;
        }

        long joint_key(int k)
        {
            int m = segs.Count;
            return ((long)segs[(k - 1 + m) % m] << 32) ^ (uint)segs[k];
        }

        void build_output()
        {
            Path = new List<Vector3d>();
            PathVertices = new List<int>();
            Length = path_length();
            if (segs.Count == 0) {
                // a loop contracted to a point, or an open path from a vertex back to it
                PathVertices.Add(T.VertexID(emptyVertex));
                Path.Add(T.VertexPosition(emptyVertex));
                return;
            }
            foreach (int s in segs) {
                List<Vector3d> pts = T.TraceEdge(seg_halfedge(s));
                if (s < 0) pts.Reverse();
                // the joints are vertices: exact positions instead of the ends of the walks
                pts[0] = T.VertexPosition(seg_tail(s));
                pts[pts.Count - 1] = T.VertexPosition(seg_head(s));
                int start = Path.Count == 0 ? 0 : 1;
                for (int j = start; j < pts.Count; ++j) Path.Add(pts[j]);
                PathVertices.Add(T.VertexID(seg_tail(s)));
            }
            if (closed == false) PathVertices.Add(T.VertexID(seg_head(segs[segs.Count - 1])));
        }
    }
}
