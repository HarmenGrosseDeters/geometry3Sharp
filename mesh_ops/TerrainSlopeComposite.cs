// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The composite terrain from the overlay (TERRAIN_1B_PLAN.md, scope 5): the slope surface clipped to the region
    /// (SlopeRegion) and the terrain minus the region. A triangle wholly inside (outside) is kept as it is; a partial
    /// one is triangulated by an exact CDT with the boundary of the region in it as constraints, and its part found by
    /// parity from outside its triangle. The nodes on the boundary are overlay identities, so neighbouring triangles
    /// split their shared edges at the same nodes: along the catch line both parts share their vertices (z on S̃), along
    /// the toe and the lateral limits the terrain has its own vertices (a step), moved off the slope side by ulps where
    /// rounding put them on it.
    /// </summary>
    internal sealed class SlopeComposite
    {
        readonly SlopeTerrainOverlay O;
        readonly DMesh3 A, B;
        readonly int terrainGroup;
        readonly Func<bool> cancel;

        public DMesh3 Region, Composite;
        public string Problem = "";

        readonly Dictionary<int, List<int>> facesOfA = new Dictionary<int, List<int>>(), facesOfB = new Dictionary<int, List<int>>();
        readonly Dictionary<Vector3d, int> regionVerts = new Dictionary<Vector3d, int>(), compVerts = new Dictionary<Vector3d, int>();
        readonly Dictionary<long, Vector2d> nudged = new Dictionary<long, Vector2d>();

        readonly double minFeature;
        // the realized position of each catch point: snapped to a slope vertex nearer than minFeature, or merged with
        // the catch points nearer than minFeature (TERRAIN.md 5.5: the terrain side absorbs the move)
        readonly Dictionary<long, Vector3d> catchAt = new Dictionary<long, Vector3d>();

        public SlopeComposite(SlopeTerrainOverlay overlay, int terrainGroup, double minFeature, Func<bool> cancel)
        {
            O = overlay; A = overlay.Slope; B = overlay.Terrain;
            this.terrainGroup = terrainGroup;
            this.minFeature = minFeature;
            this.cancel = cancel;
        }

        // two catch points may share a position if one of them can take the other's without leaving its slope planes:
        // both on the same slope edge, or one inside a slope triangle and the other inside it or on one of its edges
        bool mergeable(long a, long b)
        {
            int ka = SlopeTerrainOverlay.ekind(a), kb = SlopeTerrainOverlay.ekind(b);
            if (ka == 0 && kb == 0) return SlopeTerrainOverlay.eedge(a) == SlopeTerrainOverlay.eedge(b);
            if (ka == 1 && kb == 1) return SlopeTerrainOverlay.etri(a) == SlopeTerrainOverlay.etri(b);
            long e = ka == 0 ? a : b, t = ka == 0 ? b : a;
            Index3i te = A.GetTriEdges(SlopeTerrainOverlay.etri(t));
            int ea = SlopeTerrainOverlay.eedge(e);
            return te.a == ea || te.b == ea || te.c == ea;
        }

        void snap_catch_points()
        {
            var keys = new List<long>();
            var seen = new HashSet<long>();
            foreach (var f in O.Faces)
                if (f.Region && f.C0 != -1)
                    foreach (long k in new[] { f.C0, f.C1 })
                        if (seen.Add(k)) keys.Add(k);
            keys.Sort();
            var snapped = new HashSet<long>();
            foreach (long k in keys) {
                Vector3d p;
                if (catchOwn.TryGetValue(k, out p) == false) {
                    p = O.CatchPoint(k);
                    p.x += 0.0; p.y += 0.0;
                    p.z = SlopeTerrainOverlay.ekind(k) == 0 ? edge_z(SlopeTerrainOverlay.eedge(k), p.xy) : tri_z(SlopeTerrainOverlay.etri(k), p.xy);
                    catchOwn[k] = p;
                }
                catchAt[k] = p;
                if (noSnap.Contains(k)) continue;
                // the slope vertices of its edge (on an edge of A) or of its triangle (inside one)
                int[] vs;
                if (SlopeTerrainOverlay.ekind(k) == 0) { Index2i ev = A.GetEdgeV(SlopeTerrainOverlay.eedge(k)); vs = new[] { ev.a, ev.b }; }
                else { Index3i t = A.GetTriangle(SlopeTerrainOverlay.etri(k)); vs = new[] { t.a, t.b, t.c }; }
                double best = minFeature;
                foreach (int v in vs) {
                    Vector3d q = A.GetVertex(v);
                    double dd = q.xy.Distance(p.xy);
                    if (dd < best) { best = dd; catchAt[k] = q; snapped.Add(k); }
                }
            }
            // catch points nearer than minFeature: one position. Greedy, so a point moves by less than minFeature from
            // its own (snapped) position: in the order snapped, then on a slope edge, then inside a slope triangle (the
            // position must lie on the planes of all slope triangles that use it: a vertex, else a point on an edge, else
            // a point inside the one triangle), then by key, each point joins the nearest representative it may share a
            // position with, else it becomes one. (Pairs from a grid of cells of size minFeature, so a line along y is
            // not quadratic.)
            Func<long, int> priority = (x) => snapped.Contains(x) ? 0 : SlopeTerrainOverlay.ekind(x) == 0 ? 1 : 2;
            Dictionary<long, long> repOf = MergeNear(keys, catchAt, priority, mergeable, noSnap, minFeature);
            foreach (long k in keys) {
                catchAt[k] = catchAt[repOf[k]];
                List<long> l;
                if (catchOfVertex.TryGetValue(catchAt[k], out l) == false) { l = new List<long>(); catchOfVertex[catchAt[k]] = l; }
                l.Add(k);
            }
        }

        /// <summary>
        /// the representative of each point: in the order of priority, then key, each point (not in fixed) joins the
        /// nearest representative nearer than radius that it may share a position with, else it becomes one; so every
        /// point is less than radius from its representative (no chains)
        /// </summary>
        internal static Dictionary<long, long> MergeNear(List<long> keys, Dictionary<long, Vector3d> at, Func<long, int> priority,
                                                         Func<long, long, bool> mayShare, HashSet<long> fixedPoints, double radius)
        {
            var order = new List<long>(keys);
            order.Sort((x, y) => priority(x) != priority(y) ? priority(x).CompareTo(priority(y)) : x.CompareTo(y));
            var grid = new Dictionary<long, List<long>>();
            Func<double, long> cell = (x) => (long)Math.Floor(x / radius);
            var repOf = new Dictionary<long, long>();
            foreach (long k in order) {
                repOf[k] = k;
                if (fixedPoints.Contains(k)) continue;
                Vector2d p = at[k].xy;
                long cx = cell(p.x), cy = cell(p.y), best = -1;
                double bestD = radius;
                for (long gx = cx - 1; gx <= cx + 1; ++gx)
                    for (long gy = cy - 1; gy <= cy + 1; ++gy) {
                        List<long> l;
                        if (grid.TryGetValue(gx * 0x100000007L + gy, out l) == false) continue;
                        foreach (long r in l) {
                            double dd = at[r].xy.Distance(p);
                            if (dd < bestD && mayShare(k, r)) { bestD = dd; best = r; }
                            else if (dd == bestD && best >= 0 && r < best && mayShare(k, r)) best = r;
                        }
                    }
                if (best >= 0) { repOf[k] = best; continue; }
                long ck = cx * 0x100000007L + cy;
                List<long> cl;
                if (grid.TryGetValue(ck, out cl) == false) { cl = new List<long>(); grid[ck] = cl; }
                cl.Add(k);
            }
            return repOf;
        }

        // a node of a partial triangle: an overlay vertex (Catch = false) or a catch point (Catch = true, Key an edge key)
        struct Node : IEquatable<Node>
        {
            public bool Catch;
            public long Key;
            public bool Equals(Node o) { return Catch == o.Catch && Key == o.Key; }
            public override int GetHashCode() { return Key.GetHashCode() * 2 + (Catch ? 1 : 0); }
        }

        static Node V(long k) { return new Node() { Catch = false, Key = k }; }
        static Node C(long k) { return new Node() { Catch = true, Key = k }; }

        // catch points that keep their own position: a move made the region boundary of a triangle not simple
        readonly HashSet<long> noSnap = new HashSet<long>();
        readonly Dictionary<long, Vector3d> catchOwn = new Dictionary<long, Vector3d>();
        List<long> movedInFailure = new List<long>();

        public bool Build()
        {
            // (a second call starts from scratch)
            facesOfA.Clear(); facesOfB.Clear(); noSnap.Clear(); catchOwn.Clear(); nudged.Clear();
            try {
                return build();
            } catch (InvalidOperationException e) {
                Problem = e.Message;
                return false;
            }
        }

        bool build()
        {
            for (int i = 0; i < O.Faces.Count; ++i) {
                add(facesOfA, O.Faces[i].TA, i);
                add(facesOfB, O.Faces[i].TB, i);
            }
            while (true) {
                if (build_once() && no_overlaps()) return true;
                // a snapped or merged catch point can cross another part of the boundary where the features are below
                // MinFeatureSize: those points keep their own positions, and the composite is built again
                int before = noSnap.Count;
                foreach (long k in movedInFailure) noSnap.Add(k);
                if (noSnap.Count == before) return false;
            }
        }

        // the composite is a height field (exact); overlapping triangles name the moved catch points at their vertices.
        // Only the triangles made here (the slope and the cut terrain triangles) are checked, against each other and
        // the copied terrain triangles within their box: two copied ones are the terrain as given (within the slope's
        // box the overlay rejects their overlaps, NotHeightField; outside it they are not this operation's concern).
        bool no_overlaps()
        {
            DMesh3 near;
            List<Index2i> pairs = OverlapsOfConstructed(Composite, constructed, out near);
            if (pairs.Count == 0) return true;
            Problem = $"{pairs.Count} pairs of composite triangles overlap in XY";
            foreach (Index2i pr in pairs)
                foreach (int t in new[] { pr.a, pr.b }) {
                    Index3i tv = near.GetTriangle(t);
                    foreach (int v in new[] { tv.a, tv.b, tv.c }) {
                        List<long> ks;
                        if (catchOfVertex.TryGetValue(near.GetVertex(v), out ks))
                            foreach (long k in ks)
                                if (catchAt[k] != catchOwn[k]) movedInFailure.Add(k);
                    }
                }
            return false;
        }

        // the composite triangles made here (not copied from the terrain), for the overlap check
        readonly List<int> constructed = new List<int>();

        /// <summary>
        /// the XY overlaps among the triangles 'constructed' of the mesh and the others within their box, as pairs of
        /// triangles of 'near' (those triangles, in mesh order); overlaps of two others are not looked for
        /// </summary>
        internal static List<Index2i> OverlapsOfConstructed(DMesh3 mesh, List<int> constructed, out DMesh3 near)
        {
            var box = AxisAlignedBox2d.Empty;
            foreach (int t in constructed) {
                Index3i tv = mesh.GetTriangle(t);
                box.Contain(mesh.GetVertex(tv.a).xy); box.Contain(mesh.GetVertex(tv.b).xy); box.Contain(mesh.GetVertex(tv.c).xy);
            }
            // (not welded: the overlap test is geometric, and a triangle that welding would make non-manifold, eg at
            // terrain vertices with the same position, must not drop out of the check)
            near = new DMesh3(false, false, false, false);
            var isConstructed = new HashSet<int>(constructed);
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
                if (isConstructed.Contains(t) == false) {
                    var tb = new AxisAlignedBox2d(a.xy); tb.Contain(b.xy); tb.Contain(c.xy);
                    if (tb.Intersects(box) == false) continue;
                }
                if (near.AppendTriangle(near.AppendVertex(a), near.AppendVertex(b), near.AppendVertex(c)) < 0)
                    throw new InvalidOperationException("SlopeComposite: overlap check");
            }
            return TerrainVolumeDifference.XYOverlapPairs(near);
        }

        // the catch points at each realized position (to find the moved ones)
        readonly Dictionary<Vector3d, List<long>> catchOfVertex = new Dictionary<Vector3d, List<long>>();

        bool build_once()
        {
            catchOfVertex.Clear();
            Problem = "";
            movedInFailure = new List<long>();
            regionVerts.Clear(); compVerts.Clear(); catchAt.Clear();
            snap_catch_points();
            Region = new DMesh3(false, false, false, true);
            Composite = new DMesh3(false, false, false, true);
            constructed.Clear();
            // the slope part
            foreach (int ta in A.TriangleIndices()) {
                if (cancel != null && cancel()) throw new OperationCanceledException();
                List<int> fl;
                if (facesOfA.TryGetValue(ta, out fl) == false || fl.Exists(f => O.Faces[f].Region) == false) continue;
                if (slope_whole(ta, fl)) {
                    Index3i t = A.GetTriangle(ta);
                    emit(true, A.GetTriangleGroup(ta), A.GetVertex(t.a), A.GetVertex(t.b), A.GetVertex(t.c));
                    continue;
                }
                if (partial(true, ta, fl) == false) return false;
            }
            // the terrain part. The vertices of the copied triangles are found by position (compVerts) only where a cut
            // triangle meets them; elsewhere by terrain vertex id (no slope vertex can be there: a position in the region
            // lies in a cut terrain triangle, and at a terrain vertex in one of its triangles)
            var cut = new bool[B.MaxVertexID];
            foreach (var kv in facesOfB)
                if (kv.Value.Exists(f => O.Faces[f].Region)) {
                    Index3i v = B.GetTriangle(kv.Key);
                    cut[v.a] = cut[v.b] = cut[v.c] = true;
                }
            var outOf = new int[B.MaxVertexID];
            for (int i = 0; i < outOf.Length; ++i) outOf[i] = -1;
            Func<int, int> copied = (bv) => {
                if (cut[bv]) {
                    // (as the cut triangles take it: -0 as +0)
                    Vector3d p = B.GetVertex(bv);
                    p.x += 0.0; p.y += 0.0; p.z += 0.0;
                    return vertex(Composite, compVerts, p);
                }
                if (outOf[bv] < 0) outOf[bv] = Composite.AppendVertex(B.GetVertex(bv));
                return outOf[bv];
            };
            int checkAt = 0;
            foreach (int tb in B.TriangleIndices()) {
                if ((++checkAt & 1023) == 0 && cancel != null && cancel()) throw new OperationCanceledException();
                List<int> fl;
                if (facesOfB.TryGetValue(tb, out fl) == false || fl.Exists(f => O.Faces[f].Region) == false) {
                    Index3i v = B.GetTriangle(tb);
                    int ca = copied(v.a), cb = copied(v.b), cc = copied(v.c);
                    if (ExactPredicates.Orient2d(Composite.GetVertex(ca).xy, Composite.GetVertex(cb).xy, Composite.GetVertex(cc).xy) < 0) { int x = cb; cb = cc; cc = x; }
                    if (Composite.AppendTriangle(ca, cb, cc, terrainGroup) < 0)
                        throw new InvalidOperationException("SlopeComposite: non-manifold composite");
                    continue;
                }
                if (partial(false, tb, fl) == false) return false;
            }
            return true;
        }

        static void add(Dictionary<int, List<int>> d, int k, int v)
        {
            List<int> l;
            if (d.TryGetValue(k, out l) == false) { l = new List<int>(); d[k] = l; }
            l.Add(v);
        }

        // all faces in the region, no catch point, no terrain boundary inside, and covered by the terrain
        bool slope_whole(int ta, List<int> fl)
        {
            foreach (int fi in fl) {
                var f = O.Faces[fi];
                if (f.Region == false || f.C0 != -1) return false;
                foreach (var oe in f.Edges) {
                    if (O.Sign[oe.P] > 0 || O.Sign[oe.Q] > 0) return false;
                    if (SlopeTerrainOverlay.ekind(oe.Key) == 1 && O.EdgeFaces[oe.Key].b == -1) return false;
                }
            }
            Index3i t = A.GetTriangle(ta);
            foreach (int v in new[] { t.a, t.b, t.c })
                if (O.Container(SlopeTerrainOverlay.vkeyA(v)) < 0) return false;
            return true;
        }

        void emit(bool slope, int group, Vector3d a, Vector3d b, Vector3d c)
        {
            if (ExactPredicates.Orient2d(a.xy, b.xy, c.xy) < 0) { Vector3d x = b; b = c; c = x; }
            int ca = vertex(Composite, compVerts, a), cb = vertex(Composite, compVerts, b), cc = vertex(Composite, compVerts, c);
            int tid = Composite.AppendTriangle(ca, cb, cc, group);
            if (tid < 0)
                throw new InvalidOperationException("SlopeComposite: non-manifold composite");
            constructed.Add(tid);
            if (slope) {
                int ra = vertex(Region, regionVerts, a), rb = vertex(Region, regionVerts, b), rc = vertex(Region, regionVerts, c);
                if (Region.AppendTriangle(ra, rb, rc, group) < 0)
                    throw new InvalidOperationException("SlopeComposite: non-manifold slope region");
            }
        }

        static int vertex(DMesh3 m, Dictionary<Vector3d, int> map, Vector3d p)
        {
            int id;
            if (map.TryGetValue(p, out id)) return id;
            id = m.AppendVertex(p);
            map[p] = id;
            return id;
        }

        // ---------------------------------------------------------------- a partial triangle

        bool partial(bool slope, int tri, List<int> fl)
        {
            int[] tv, te;
            if (slope) {
                Index3i t = A.GetTriangle(tri), e = A.GetTriEdges(tri);
                tv = new[] { t.a, t.b, t.c }; te = new[] { e.a, e.b, e.c };
            } else { tv = O.bTriV[tri]; te = O.bTriE[tri]; }
            var nodes = new List<Node>();
            var constraints = new List<Index2i>();
            var nodeIndex = new Dictionary<Node, int>();
            Func<Node, int> idx = (n) => {
                int i;
                if (nodeIndex.TryGetValue(n, out i)) return i;
                i = nodes.Count; nodes.Add(n); nodeIndex[n] = i;
                return i;
            };
            int edgeKind = slope ? 0 : 1;
            // the edges of the triangle: the sub-pieces in the region (slope) or not in it (terrain) are constraints
            for (int j = 0; j < 3; ++j) {
                long c0 = slope ? SlopeTerrainOverlay.vkeyA(tv[j]) : SlopeTerrainOverlay.vkeyB(tv[j]);
                long c1 = slope ? SlopeTerrainOverlay.vkeyA(tv[(j + 1) % 3]) : SlopeTerrainOverlay.vkeyB(tv[(j + 1) % 3]);
                var seq = new List<Node>();
                var flags = new List<bool>();
                if (edge_sequence(edgeKind, te[j], fl, c0, c1, seq, flags) == false) return false;
                for (int k = 0; k + 1 < seq.Count; ++k)
                    if (flags[k] == slope) constraints.Add(new Index2i(idx(seq[k]), idx(seq[k + 1])));
                foreach (Node n in seq) idx(n);
            }
            // inside: the catch segments, and the boundary of the other surface next to the region
            foreach (int fi in fl) {
                var f = O.Faces[fi];
                if (f.Region == false) continue;
                if (f.C0 != -1) constraints.Add(new Index2i(idx(C(f.C0)), idx(C(f.C1))));
                foreach (var oe in f.Edges) {
                    if (SlopeTerrainOverlay.ekind(oe.Key) == edgeKind) continue;
                    bool onBoundary = slope ? O.EdgeFaces[oe.Key].b == -1 : O.BoundaryKind(SlopeTerrainOverlay.eedge(oe.Key)) != 0;
                    if (onBoundary == false) continue;
                    int sp = O.Sign[oe.P], sq = O.Sign[oe.Q];
                    if (sp < 0 && sq < 0) constraints.Add(new Index2i(idx(V(oe.P)), idx(V(oe.Q))));
                    else if (sp < 0) constraints.Add(new Index2i(idx(V(oe.P)), idx(C(oe.Key))));
                    else if (sq < 0) constraints.Add(new Index2i(idx(C(oe.Key)), idx(V(oe.Q))));
                }
            }

            // positions; nodes at the same XY are one point
            var pts = new List<Vector2d>();
            var zs = new List<double>();
            var pointOf = new int[nodes.Count];
            var at = new Dictionary<Vector2d, int>();
            for (int i = 0; i < nodes.Count; ++i) {
                Vector3d p = position(slope, nodes[i]);
                p.x += 0.0; p.y += 0.0;     // (-0 is +0, also as a dictionary key)
                int pi;
                if (at.TryGetValue(p.xy, out pi) == false) { pi = pts.Count; pts.Add(p.xy); zs.Add(p.z); at[p.xy] = pi; }
                else if (nodes[i].Catch) zs[pi] = p.z;      // (a catch point is shared with the other part: its z wins)
                pointOf[i] = pi;
            }
            // the constraints mod 2 (a segment twice is no boundary), without zero-length ones
            var cset = new HashSet<long>();
            foreach (Index2i c in constraints) {
                int a = pointOf[c.a], b = pointOf[c.b];
                if (a == b) continue;
                long k = ukey(a, b);
                if (cset.Remove(k) == false) cset.Add(k);
            }
            var segs0 = new List<Index2i>();
            foreach (long k in cset) segs0.Add(new Index2i((int)(k >> 32), (int)(k & 0xFFFFFFFF)));
            segs0.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            // points exactly on a constraint split it (degenerate input that SoS decided: a terrain edge on a slope edge,
            // a vertex of one surface on an edge of the other; rounding puts the realized points on the line)
            var segs = new List<Index2i>();
            foreach (Index2i sg in segs0) {
                Vector2d a = pts[sg.a], b = pts[sg.b];
                var on = new List<int>();
                for (int k = 0; k < pts.Count; ++k)
                    if (k != sg.a && k != sg.b && ExactPredicates.Orient2d(a, b, pts[k]) == 0 && (pts[k] - a).Dot(b - a) > 0 && (pts[k] - b).Dot(a - b) > 0)
                        on.Add(k);
                if (on.Count == 0) { segs.Add(sg); continue; }
                on.Sort((x, y) => (pts[x] - a).Dot(b - a).CompareTo((pts[y] - a).Dot(b - a)));
                int prev = sg.a;
                foreach (int k in on) { segs.Add(new Index2i(prev, k)); prev = k; }
                segs.Add(new Index2i(prev, sg.b));
            }
            // (the parity of a split segment is that of the segment; cset keeps the keys of the pieces)
            cset.Clear();
            foreach (Index2i sg in segs) {
                long k = ukey(sg.a, sg.b);
                if (cset.Remove(k) == false) cset.Add(k);
            }
            segs.Clear();
            foreach (long k in cset) segs.Add(new Index2i((int)(k >> 32), (int)(k & 0xFFFFFFFF)));
            segs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            string why;
            if (check_graph(pts, segs, out why) == false) {
                Problem = $"the region boundary in {(slope ? "slope" : "terrain")} triangle {tri} is not simple after rounding ({why})";
                foreach (Node n in nodes)
                    if (n.Catch && catchAt[n.Key] != catchOwn[n.Key]) movedInFailure.Add(n.Key);
                return false;
            }

            // CDT in a large outer triangle; the part is found by parity from outside
            var box = new AxisAlignedBox2d(pts[0]);
            foreach (var p in pts) box.Contain(p);
            int n0 = pts.Count;
            Vector2d cen = box.Center;
            double S = Math.Max(box.MaxDim, 1e-300) + Math.Max(Math.Abs(cen.x), Math.Abs(cen.y)) * 1e-6;
            pts.Add(new Vector2d(cen.x - 10 * S, cen.y - 10 * S)); pts.Add(new Vector2d(cen.x + 10 * S, cen.y - 10 * S)); pts.Add(new Vector2d(cen.x, cen.y + 10 * S));
            Func<int, int, int, int> orient = (a, b, c) => ExactPredicates.Orient2d(pts[a], pts[b], pts[c]);
            Func<int, int, int, int, int> incircle = (a, b, c, d) => ExactPredicates.InCircle(pts[a], pts[b], pts[c], pts[d]);
            var cdt = new ExactTriangulation2(n0, n0 + 1, n0 + 2, orient, incircle);
            for (int i = 0; i < n0; ++i) cdt.InsertPoint(i);
            foreach (Index2i s in segs) cdt.InsertConstraint(s.a, s.b);
            cdt.MakeDelaunay();
            var tris = new List<Index3i>(cdt.Triangles());
            var label = new int[tris.Count];
            var edgeTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < tris.Count; ++t) {
                label[t] = -1;
                Index3i x = tris[t];
                foreach (long k in new[] { ukey(x.a, x.b), ukey(x.b, x.c), ukey(x.c, x.a) }) {
                    List<int> l;
                    if (edgeTris.TryGetValue(k, out l) == false) { l = new List<int>(); edgeTris[k] = l; }
                    l.Add(t);
                }
            }
            var queue = new Queue<int>();
            for (int t = 0; t < tris.Count; ++t)
                if (tris[t].a >= n0 || tris[t].b >= n0 || tris[t].c >= n0) { label[t] = 0; queue.Enqueue(t); }
            while (queue.Count > 0) {
                int t = queue.Dequeue();
                Index3i x = tris[t];
                foreach (long k in new[] { ukey(x.a, x.b), ukey(x.b, x.c), ukey(x.c, x.a) }) {
                    int want = cset.Contains(k) ? 1 - label[t] : label[t];
                    foreach (int u in edgeTris[k]) {
                        if (u == t) continue;
                        if (label[u] == -1) { label[u] = want; queue.Enqueue(u); }
                        else if (label[u] != want) {
                            Problem = $"inconsistent parity in {(slope ? "slope" : "terrain")} triangle {tri}";
                            foreach (Node n in nodes)
                                if (n.Catch && catchAt[n.Key] != catchOwn[n.Key]) movedInFailure.Add(n.Key);
                            return false;
                        }
                    }
                }
            }
            int group = slope ? A.GetTriangleGroup(tri) : terrainGroup;
            for (int t = 0; t < tris.Count; ++t) {
                if (label[t] != 1) continue;
                Index3i x = tris[t];
                if (x.a >= n0 || x.b >= n0 || x.c >= n0) { Problem = "the part reaches the outer triangle"; return false; }
                emit(slope, group, new Vector3d(pts[x.a].x, pts[x.a].y, zs[x.a]), new Vector3d(pts[x.b].x, pts[x.b].y, zs[x.b]),
                     new Vector3d(pts[x.c].x, pts[x.c].y, zs[x.c]));
            }
            return true;
        }

        static long ukey(int a, int b) { return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; }

        // no constraint passes through a point or crosses another (exact)
        static bool check_graph(List<Vector2d> pts, List<Index2i> segs, out string why)
        {
            why = "";
            for (int i = 0; i < segs.Count; ++i) {
                Vector2d a = pts[segs[i].a], b = pts[segs[i].b];
                for (int k = 0; k < pts.Count; ++k) {
                    if (k == segs[i].a || k == segs[i].b) continue;
                    if (ExactPredicates.Orient2d(a, b, pts[k]) == 0 && (pts[k] - a).Dot(b - a) > 0 && (pts[k] - b).Dot(a - b) > 0) { why = $"point {pts[k]} on segment {a} {b}"; return false; }
                }
                for (int j = i + 1; j < segs.Count; ++j) {
                    Index2i s = segs[i], t = segs[j];
                    if (s.a == t.a || s.a == t.b || s.b == t.a || s.b == t.b) continue;
                    Vector2d c = pts[t.a], d = pts[t.b];
                    int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
                    int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
                    if (o1 * o2 < 0 && o3 * o4 < 0) { why = $"segments {a} {b} and {c} {d} cross"; return false; }
                }
            }
            return true;
        }

        // the nodes along an edge of the owner triangle (from corner c0 to c1) and, per sub-piece, whether it is in the
        // region; nodes inside a run of equal flags that are not catch points or corners are dropped
        bool edge_sequence(int edgeKind, int edge, List<int> fl, long c0, long c1, List<Node> seq, List<bool> flags)
        {
            // the overlay edges of this edge, each split at its catch point
            var byStart = new Dictionary<long, SlopeTerrainOverlay.OEdge>();
            var ends = new HashSet<long>();
            var region = new Dictionary<long, bool>();
            foreach (int fi in fl) {
                var f = O.Faces[fi];
                foreach (var oe in f.Edges) {
                    if (SlopeTerrainOverlay.ekind(oe.Key) != edgeKind || SlopeTerrainOverlay.eedge(oe.Key) != edge) continue;
                    byStart[oe.P] = oe;
                    ends.Add(oe.Q);
                    region[oe.Key] = f.Region;
                }
            }
            var runs = new List<List<KeyValuePair<Node, bool>>>();
            foreach (var kv in byStart) {
                if (ends.Contains(kv.Key)) continue;
                var run = new List<KeyValuePair<Node, bool>>();
                long cur = kv.Key;
                int guard = 0;
                SlopeTerrainOverlay.OEdge oe;
                while (byStart.TryGetValue(cur, out oe)) {
                    if (++guard > byStart.Count) { Problem = "a cycle along an edge"; return false; }
                    int sp = O.Sign[oe.P], sq = O.Sign[oe.Q];
                    bool r = region[oe.Key];
                    if (sp != sq && r) {
                        run.Add(new KeyValuePair<Node, bool>(V(oe.P), r && sp < 0));
                        run.Add(new KeyValuePair<Node, bool>(C(oe.Key), r && sq < 0));
                    } else run.Add(new KeyValuePair<Node, bool>(V(oe.P), r && sp < 0));
                    cur = oe.Q;
                }
                run.Add(new KeyValuePair<Node, bool>(V(cur), false));
                runs.Add(run);
            }
            Vector2d p0 = O.VertexXY(c0), p1 = O.VertexXY(c1);
            Func<List<KeyValuePair<Node, bool>>, double> param = (run) => (O.VertexXY(run[0].Key.Key) - p0).Dot(p1 - p0);
            runs.Sort((x, y) => param(x).CompareTo(param(y)));
            var raw = new List<KeyValuePair<Node, bool>>();
            if (runs.Count == 0 || runs[0][0].Key.Key != c0) raw.Add(new KeyValuePair<Node, bool>(V(c0), false));
            foreach (var run in runs) raw.AddRange(run);
            if (raw[raw.Count - 1].Key.Key != c1 || raw[raw.Count - 1].Key.Catch) raw.Add(new KeyValuePair<Node, bool>(V(c1), false));
            // (the flag of the last node is the gap after it, so false at the end)
            for (int i = 0; i < raw.Count; ++i) {
                Node n = raw[i].Key;
                bool keep = i == 0 || i == raw.Count - 1 || n.Catch || raw[i - 1].Value != raw[i].Value;
                if (keep == false) continue;
                if (seq.Count > 0 && seq[seq.Count - 1].Equals(n)) continue;
                seq.Add(n);
                flags.Add(raw[i].Value);
            }
            return true;
        }

        // the position of a node on the slope side (z on S̃) or the terrain side (z on the terrain; catch points on S̃)
        Vector3d position(bool slope, Node n)
        {
            if (n.Catch) return catchAt[n.Key];
            long k = n.Key;
            int kind = SlopeTerrainOverlay.vkind(k);
            double zs, zt;
            if (kind == 0 && slope) return A.GetVertex(SlopeTerrainOverlay.vfirst(k));
            if (kind == 1 && slope == false) return B.GetVertex(SlopeTerrainOverlay.vfirst(k));
            O.Heights(k, out zs, out zt);
            Vector2d xy = O.VertexXY(k);
            if (slope) {
                // (on S̃: exactly rounded from the planes of the slope triangles, as 1a realizes its vertices)
                zs = kind == 2 ? edge_z(SlopeTerrainOverlay.vfirst(k), xy) : tri_z(O.Container(k), xy);
            }
            if (kind == 2 && slope == false && O.BoundaryKind(SlopeTerrainOverlay.vfirst(k)) != 0)
                xy = nudge(k, xy);
            return new Vector3d(xy.x, xy.y, slope ? zs : zt);
        }

        // the height of the plane through a, b, c at p, rounded to nearest from the exact value
        static double exact_plane_z(Vector3d a, Vector3d b, Vector3d c, Vector2d p)
        {
            ExactDyadic ax = ExactDyadic.Of(a.x), ay = ExactDyadic.Of(a.y), az = ExactDyadic.Of(a.z);
            ExactDyadic ux = ExactDyadic.Of(b.x) - ax, uy = ExactDyadic.Of(b.y) - ay, uz = ExactDyadic.Of(b.z) - az;
            ExactDyadic wx = ExactDyadic.Of(c.x) - ax, wy = ExactDyadic.Of(c.y) - ay, wz = ExactDyadic.Of(c.z) - az;
            ExactDyadic nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            ExactDyadic num = az * nz - nx * (ExactDyadic.Of(p.x) - ax) - ny * (ExactDyadic.Of(p.y) - ay);
            return ExactDyadic.Quotient(num, nz);
        }

        double tri_z(int ta, Vector2d p)
        {
            Index3i t = A.GetTriangle(ta);
            return exact_plane_z(A.GetVertex(t.a), A.GetVertex(t.b), A.GetVertex(t.c), p);
        }

        // a point on a slope edge: the lower of its triangles' planes there (cut; the higher for fill), as 1a takes
        // the lowest incident plane, so the vertex is on or below (above) both
        double edge_z(int ea, Vector2d p)
        {
            Index2i et = A.GetEdgeT(ea);
            double z = tri_z(et.a, p);
            if (et.b >= 0) {
                double z2 = tri_z(et.b, p);
                z = O.Dir > 0 ? Math.Min(z, z2) : Math.Max(z, z2);
            }
            return z;
        }

        // a terrain node on a boundary edge of the slope surface, moved by ulps off the slope side if it is on it (a node
        // exactly on the line stays: it does not overlap the slope, and it may be a vertex both share, eg a toe vertex on
        // a terrain edge)
        Vector2d nudge(long k, Vector2d x)
        {
            Vector2d r;
            if (nudged.TryGetValue(k, out r)) return r;
            int ea = SlopeTerrainOverlay.vfirst(k);
            Index2i ev = A.GetEdgeV(ea);
            int ta = A.GetEdgeT(ea).a;
            Index3i t = A.GetTriangle(ta);
            // the edge in the counter-clockwise order of its triangle: the slope is on its left
            Vector2d e0 = A.GetVertex(ev.a).xy, e1 = A.GetVertex(ev.b).xy;
            if (!((t.a == ev.a && t.b == ev.b) || (t.b == ev.a && t.c == ev.b) || (t.c == ev.a && t.a == ev.b))) { Vector2d s = e0; e0 = e1; e1 = s; }
            Vector2d nrm = new Vector2d(e1.y - e0.y, e0.x - e1.x);
            r = x;
            for (int it = 0; it < 64 && ExactPredicates.Orient2d(e0, e1, r) > 0; ++it) {
                if (nrm.x != 0) r.x = nrm.x > 0 ? ExactInterval.Up(r.x) : ExactInterval.Down(r.x);
                if (nrm.y != 0) r.y = nrm.y > 0 ? ExactInterval.Up(r.y) : ExactInterval.Down(r.y);
            }
            nudged[k] = r;
            return r;
        }
    }
}
