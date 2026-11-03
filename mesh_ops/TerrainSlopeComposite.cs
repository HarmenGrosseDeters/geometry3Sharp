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
    /// rounding put them on it. In a mixed run (Partner) the cut part and the fill part meet at a toe crossing at one XY
    /// with different heights (λ_cut below the toe, λ_fill above): a step there too, along the first catch segment of
    /// one of them, which the terrain part beside it does not share.
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
        // (shared with the partner of a mixed run, which writes into the same meshes)
        Dictionary<Vector3d, int> regionVerts = new Dictionary<Vector3d, int>(), compVerts = new Dictionary<Vector3d, int>();
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
                    // a catch point on a boundary edge of the slope surface (the toe, a lateral limit, where a catch line
                    // ends) on the slope side of the edge's line, by ulps where rounding put it off: the slope part's
                    // boundary from the edge's end to it then stays on the slope side, which the terrain nodes on the
                    // edge leave (nudge)
                    if (SlopeTerrainOverlay.ekind(k) == 0 && A.IsBoundaryEdge(SlopeTerrainOverlay.eedge(k)))
                        { Vector2d q = onto_slope_side(SlopeTerrainOverlay.eedge(k), p.xy, +1); p = new Vector3d(q.x, q.y, p.z); }
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
            RegionFailed = false; Builds = 0; RegionFailures = 0;
            if (Partner != null) {
                Partner.partnerNoSnap.Clear();
                if (Partner.B != B) throw new ArgumentException("SlopeComposite: the partner overlays another terrain");
                Partner.facesOfA.Clear(); Partner.facesOfB.Clear(); Partner.noSnap.Clear(); Partner.catchOwn.Clear(); Partner.nudged.Clear();
            }
            try {
                return build();
            } catch (InvalidOperationException e) {
                Problem = e.Message;
                return false;
            }
        }

        bool build()
        {
            foreach (SlopeComposite c in Partner != null ? new[] { this, Partner } : new[] { this })
                for (int i = 0; i < c.O.Faces.Count; ++i) {
                    add(c.facesOfA, c.O.Faces[i].TA, i);
                    add(c.facesOfB, c.O.Faces[i].TB, i);
                }
            while (true) {
                ++Builds;
                if (build_once() && no_overlaps()) return true;
                // a snapped or merged catch point can cross another part of the boundary where the features are below
                // MinFeatureSize: those points keep their own positions, and the composite is built again
                Func<int> held = () => noSnap.Count + (Partner != null ? Partner.noSnap.Count + Partner.partnerNoSnap.Count : 0);
                int before = held();
                foreach (long k in movedInFailure) noSnap.Add(k);
                // (the partner's points moved onto this one's: not again; moved by their own snapping: not that again)
                if (Partner != null)
                    foreach (long k in Partner.movedInFailure) (Partner.partnerSnapped.Contains(k) ? Partner.partnerNoSnap : Partner.noSnap).Add(k);
                if (held() == before) return false;
            }
        }

        // the composite is a height field (exact); overlapping triangles name the moved catch points at their vertices.
        // Only the triangles made here (the slope and the cut terrain triangles) are checked, against each other and
        // the copied terrain triangles within their box: two copied ones are the terrain as given (within the slope's
        // box the overlay rejects their overlaps, NotHeightField; outside it they are not this operation's concern).
        bool no_overlaps()
        {
            DMesh3 near;
            List<Index2i> pairs = OverlapsOfMade(composite_triangles(constructed), kept_terrain_within, out near);
            if (pairs.Count == 0) return true;
            Problem = $"{pairs.Count} pairs of composite triangles overlap in XY";
            foreach (Index2i pr in pairs)
                foreach (int t in new[] { pr.a, pr.b }) {
                    Index3i tv = near.GetTriangle(t);
                    foreach (int v in new[] { tv.a, tv.b, tv.c })
                        foreach (SlopeComposite c in Partner != null ? new[] { this, Partner } : new[] { this }) {
                            List<long> ks;
                            if (c.catchOfVertex.TryGetValue(near.GetVertex(v), out ks))
                                foreach (long k in ks)
                                    if (c.catchAt[k] != c.catchOwn[k]) c.movedInFailure.Add(k);
                        }
                }
            return false;
        }

        // the composite triangles made here (not copied from the terrain), for the overlap check
        List<int> constructed = new List<int>();

        /// <summary>
        /// the XY overlaps among the triangles 'made' and of them with the 'others' within their box, as pairs of
        /// triangles of 'near' (the made ones in order, then those others in order); overlaps of two others are not
        /// looked for
        /// </summary>
        internal static List<Index2i> OverlapsOfMade(IEnumerable<Vector3d[]> made, Func<AxisAlignedBox2d, IEnumerable<Vector3d[]>> othersWithin, out DMesh3 near)
        {
            // (not welded: the overlap test is geometric, and a triangle that welding would make non-manifold, eg at
            // terrain vertices with the same position, must not drop out of the check)
            near = new DMesh3(false, false, false, false);
            var box = AxisAlignedBox2d.Empty;
            foreach (Vector3d[] t in made) {
                box.Contain(t[0].xy); box.Contain(t[1].xy); box.Contain(t[2].xy);
                if (near.AppendTriangle(near.AppendVertex(t[0]), near.AppendVertex(t[1]), near.AppendVertex(t[2])) < 0)
                    throw new InvalidOperationException("SlopeComposite: overlap check");
            }
            if (near.TriangleCount > 0)
                foreach (Vector3d[] t in othersWithin(box))
                    if (near.AppendTriangle(near.AppendVertex(t[0]), near.AppendVertex(t[1]), near.AppendVertex(t[2])) < 0)
                        throw new InvalidOperationException("SlopeComposite: overlap check");
            return TerrainVolumeDifference.XYOverlapPairs(near);
        }

        // the triangles of 'all' whose XY box meets 'box' (inclusive)
        internal static IEnumerable<Vector3d[]> Within(IEnumerable<Vector3d[]> all, AxisAlignedBox2d box)
        {
            foreach (Vector3d[] t in all) {
                var tb = new AxisAlignedBox2d(t[0].xy); tb.Contain(t[1].xy); tb.Contain(t[2].xy);
                if (tb.Intersects(box)) yield return t;
            }
        }

        IEnumerable<Vector3d[]> composite_triangles(IEnumerable<int> tids)
        {
            foreach (int t in tids) {
                Index3i tv = Composite.GetTriangle(t);
                yield return new[] { Composite.GetVertex(tv.a), Composite.GetVertex(tv.b), Composite.GetVertex(tv.c) };
            }
        }

        // the terrain triangles not replaced, as given (copied into a full composite), whose XY box meets 'box'
        IEnumerable<Vector3d[]> kept_terrain_within(AxisAlignedBox2d box)
        {
            // (those of the overlay's near list if the box lies in its box: every triangle that meets the box is in it)
            bool inNear = O.NearBox.Contains(box.Min) && O.NearBox.Contains(box.Max);
            foreach (int tb in inNear ? (IEnumerable<int>)O.NearTerrain : B.TriangleIndices()) {
                Index3i v = B.GetTriangle(tb);
                Vector3d a = B.GetVertex(v.a), b = B.GetVertex(v.b), c = B.GetVertex(v.c);
                if (Math.Max(a.x, Math.Max(b.x, c.x)) < box.Min.x || Math.Min(a.x, Math.Min(b.x, c.x)) > box.Max.x ||
                    Math.Max(a.y, Math.Max(b.y, c.y)) < box.Min.y || Math.Min(a.y, Math.Min(b.y, c.y)) > box.Max.y) continue;
                if (replacedSet.Contains(tb)) continue;
                yield return new[] { a, b, c };
            }
        }

        // the region triangles of the slope surface, whole or in part
        bool slope_part()
        {
            foreach (int ta in A.TriangleIndices()) {
                if (cancel != null && cancel()) throw new OperationCanceledException();
                List<int> fl;
                if (facesOfA.TryGetValue(ta, out fl) == false || fl.Exists(f => O.Faces[f].Region) == false) continue;
                if (slope_whole(ta, fl)) {
                    Index3i t = A.GetTriangle(ta);
                    emit(true, A.GetTriangleGroup(ta) + GroupOffset, A.GetVertex(t.a), A.GetVertex(t.b), A.GetVertex(t.c));
                    continue;
                }
                if (partial(true, ta, fl) == false) return false;
            }
            return true;
        }

        /// <summary>
        /// a mixed run (TERRAIN_1C_PLAN.md, section 10): the composite of the fill overlay of the same terrain, built into
        /// the same meshes; the two regions are disjoint (S_fill &lt;= S_cut) and meet at the toe crossings
        /// </summary>
        public SlopeComposite Partner;
        /// <summary>
        /// a mixed run: per slope group (piece) its toe segment (toe index × 2^20 + segment), of this composite and of the
        /// partner (their groups offset), and the radius within which a toe crossing's two catch points are one
        /// </summary>
        public long[] SegmentOfGroup;
        public double PartnerSnapRadius;
        // (of the partner: its catch points moved onto this one's in this build, and those that keep their position)
        readonly HashSet<long> partnerSnapped = new HashSet<long>(), partnerNoSnap = new HashSet<long>();

        // the partner's catch points on the boundary of its slope surface (the toe, a lateral limit) take the XY of the
        // nearest of this one's there, within minFeature: at a toe crossing both catch lines end at the point where the
        // terrain meets the toe, realized λ below it (cut) and λ above (fill), a few ulps apart in XY; their regions would
        // overlap between the two points, and at one XY they meet there
        void snap_partner()
        {
            // (on the same toe segment; the two points are (λ_cut + λ_fill) / (the terrain's slope along the toe) apart)
            Func<SlopeComposite, long, long> segment = (c, k) =>
                SegmentOfGroup[c.A.GetTriangleGroup(c.A.GetEdgeT(SlopeTerrainOverlay.eedge(k)).a) + c.GroupOffset];
            var mineAt = new List<KeyValuePair<long, Vector3d>>();
            foreach (var kv in catchAt)
                if (SlopeTerrainOverlay.ekind(kv.Key) == 0 && A.IsBoundaryEdge(SlopeTerrainOverlay.eedge(kv.Key)))
                    mineAt.Add(new KeyValuePair<long, Vector3d>(segment(this, kv.Key), kv.Value));
            mineAt.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.x != y.Value.x ? x.Value.x.CompareTo(y.Value.x) :
                                  x.Value.y != y.Value.y ? x.Value.y.CompareTo(y.Value.y) : x.Value.z.CompareTo(y.Value.z));
            var keys = new List<long>(Partner.catchAt.Keys);
            keys.Sort();
            Partner.partnerSnapped.Clear();
            foreach (long k in keys) {
                if (Partner.partnerNoSnap.Contains(k)) continue;
                if (SlopeTerrainOverlay.ekind(k) != 0 || Partner.A.IsBoundaryEdge(SlopeTerrainOverlay.eedge(k)) == false) continue;
                Vector3d p = Partner.catchAt[k], best = p;
                long seg = segment(Partner, k);
                double bestD = Math.Max(minFeature, PartnerSnapRadius);
                foreach (var q in mineAt) {
                    if (q.Key != seg) continue;
                    double dd = q.Value.xy.Distance(p.xy);
                    if (dd < bestD) { bestD = dd; best = q.Value; }
                }
                if (best == p) continue;
                // (and the nearest of the partner's on that segment to it is this one: a pair, not two crossings near each other)
                bool mutual = true;
                foreach (long k2 in keys) {
                    if (k2 == k || SlopeTerrainOverlay.ekind(k2) != 0 || Partner.A.IsBoundaryEdge(SlopeTerrainOverlay.eedge(k2)) == false) continue;
                    if (segment(Partner, k2) != seg) continue;
                    // (a key merged with this one, at its position or already moved with it, is no other crossing)
                    if (Partner.catchAt[k2] == p || Partner.catchAt[k2].xy == best.xy) continue;
                    if (Partner.catchAt[k2].xy.Distance(best.xy) < bestD) { mutual = false; break; }
                }
                if (mutual == false) continue;
                // (the XY only: the cut point is realized λ below the toe, the fill point λ above, each on its own slope's
                // planes; the exact check of each region needs its own height there, so the two parts meet at one XY
                // with a step, as the terrain and the slope do along the toe)
                // (with the catch points merged with it, MergeNear's group at its position: each on its own planes)
                List<long> group;
                if (Partner.catchOfVertex.TryGetValue(p, out group) == false) group = new List<long>() { k };
                Partner.catchOfVertex.Remove(p);
                foreach (long g in group) {
                    double z = SlopeTerrainOverlay.ekind(g) == 0 ? Partner.edge_z(SlopeTerrainOverlay.eedge(g), best.xy) : Partner.tri_z(SlopeTerrainOverlay.etri(g), best.xy);
                    var q = new Vector3d(best.x, best.y, z);
                    Partner.catchAt[g] = q;
                    Partner.partnerSnapped.Add(g);
                    List<long> l;
                    if (Partner.catchOfVertex.TryGetValue(q, out l) == false) { l = new List<long>(); Partner.catchOfVertex[q] = l; }
                    l.Add(g);
                }
            }
        }

        // a terrain triangle that both overlays of a mixed run cut: one part, the triangle minus both regions. Their
        // boundaries inside (catch segments, slope boundaries) are constraints, and the pieces of its edges in both
        // regions or in neither (the two overlays' nodes along an edge merged, each overlay's in its own order)
        bool joint(int tb)
        {
            PartInput a = collect(false, tb, facesOfB[tb], false);
            if (a == null) return false;
            PartInput b = Partner.collect(false, tb, Partner.facesOfB[tb], false);
            if (b == null) { Problem = Partner.Problem; return false; }
            var inp = new PartInput();
            foreach (PartInput x in new[] { a, b }) {
                int off = inp.Pos.Count;
                inp.Pos.AddRange(x.Pos); inp.IsCatch.AddRange(x.IsCatch); inp.Owner.AddRange(x.Owner); inp.Key.AddRange(x.Key);
                foreach (Index2i c in x.Constraints) inp.Constraints.Add(new Index2i(c.a + off, c.b + off));
            }
            int offB = a.Pos.Count;
            // the edges: the terrain part's boundary is the triangle's boundary plus both regions' boundaries, mod 2,
            // where the regions do not overlap (an assumption here: regions that overlap are found by no_overlaps, the
            // exact check of the composite). Along an edge, a piece is a constraint where it is in both regions or in
            // neither; in both happens where one region is a sliver of zero width along the edge (the toe meets the
            // terrain on its crossing: the catch point of the cut part is the toe point, rounded), and its other side, a
            // constraint inside with the same ends, cancels it (with other ends, a sliver is left, which no_overlaps finds). The two node sequences are merged keeping each one's own order, with the
            // corners at the ends, so a piece's flags are those of the gaps that hold it in each sequence: the parity is
            // consistent at every node by construction, whatever the order between the two (t, by rounded positions,
            // only orders them; a node snapped off the edge can have the parameter of a corner, or of another node. A tie
            // between the two at the same XY does not matter; at different XY a wrong order is a piece there and back,
            // which check_graph or no_overlaps finds)
            for (int j = 0; j < 3; ++j) {
                List<int> na = a.EdgeNodes[j], nb = b.EdgeNodes[j].ConvertAll(i => i + offB);
                List<bool> fa = a.EdgeFlags[j], fb = b.EdgeFlags[j];
                Vector2d p0 = inp.Pos[na[0]].xy, p1 = inp.Pos[na[na.Count - 1]].xy;
                Func<int, double> t = (i) => (inp.Pos[i].xy - p0).Dot(p1 - p0);
                // (both start and end at the triangle's corners; between them the inner nodes of both, merged)
                int ia = 1, ib = 1, la = na.Count - 1, lb = nb.Count - 1, prev = na[0];
                while (ia < la || ib < lb) {
                    // (the piece from prev to the next node is in A's gap before na[ia] and B's gap before nb[ib])
                    bool inA = fa[ia - 1], inB = fb[ib - 1];
                    int next = ib >= lb || (ia < la && t(na[ia]) <= t(nb[ib])) ? na[ia++] : nb[ib++];
                    if (inA == inB) inp.Constraints.Add(new Index2i(prev, next));
                    prev = next;
                }
                if (fa[la - 1] == fb[lb - 1]) inp.Constraints.Add(new Index2i(prev, na[la]));
            }
            return triangulate(false, tb, terrainGroup, inp);
        }

        // the catch points at each realized position (to find the moved ones)
        readonly Dictionary<Vector3d, List<long>> catchOfVertex = new Dictionary<Vector3d, List<long>>();

        bool build_once()
        {
            foreach (SlopeComposite c in Partner != null ? new[] { this, Partner } : new[] { this }) {
                c.catchOfVertex.Clear();
                c.Problem = "";
                c.movedInFailure = new List<long>();
                c.catchAt.Clear();
                c.snap_catch_points();
            }
            if (Partner != null) snap_partner();
            regionVerts.Clear(); compVerts.Clear();
            Region = new DMesh3(false, false, false, true);
            Composite = new DMesh3(false, false, false, true);
            constructed.Clear();
            if (Partner != null) {
                Partner.Region = Region; Partner.Composite = Composite;
                Partner.regionVerts = regionVerts; Partner.compVerts = compVerts; Partner.constructed = constructed;
            }
            // the slope part (of both surfaces of a mixed run)
            if (slope_part() == false) return false;
            if (Partner != null && Partner.slope_part() == false) { Problem = Partner.Problem; return false; }
            // (the region is complete here. If it fails the caller's check, this build cannot be the result: the rest
            // of the terrain is copied only where it could change the outcome. The made triangles still are, so the
            // overlap check and a rebuild decide as they would. A copied triangle can only meet a made one, or make
            // the composite non-manifold, at an edge whose ends are both cut terrain vertices (the copied vertices
            // that are not cut are its own, and the terrain is manifold): those triangles are still copied, in the
            // same order, so the same exceptions are thrown. The outcome is the same, with most of the copy saved)
            RegionFailed = RegionCheck != null && RegionCheck(Region) == false;
            bool copy = CopyTerrain && RegionFailed == false;
            CopiedTerrain = copy;
            if (RegionFailed) ++RegionFailures;
            // the terrain part. The vertices of the copied triangles are found by position (compVerts) only where a cut
            // triangle meets them; elsewhere by terrain vertex id (no slope vertex can be there: a position in the region
            // lies in a cut terrain triangle, and at a terrain vertex in one of its triangles)
            var cut = new bool[B.MaxVertexID];
            Replaced.Clear(); replacedSet.Clear();
            var cutVerts = new List<int>();
            // (a mixed run: the triangles that either overlay replaces, those that both replace got one part)
            var mine = new HashSet<int>();
            var theirs = new HashSet<int>();
            foreach (SlopeComposite c in Partner != null ? new[] { this, Partner } : new[] { this })
                foreach (var kv in c.facesOfB)
                    if (kv.Value.Exists(f => c.O.Faces[f].Region)) {
                        Index3i v = B.GetTriangle(kv.Key);
                        foreach (int x in new[] { v.a, v.b, v.c })
                            if (cut[x] == false) { cut[x] = true; cutVerts.Add(x); }
                        replacedSet.Add(kv.Key);
                        (c == this ? mine : theirs).Add(kv.Key);
                    }
            Replaced.AddRange(replacedSet);
            Replaced.Sort();
            var outOf = new int[CopyTerrain ? B.MaxVertexID : 0];
            for (int i = 0; i < outOf.Length; ++i) outOf[i] = -1;
            // (as the cut triangles take a terrain vertex: -0 as +0)
            Func<int, Vector3d> cutPos = (bv) => { Vector3d p = B.GetVertex(bv); p.x += 0.0; p.y += 0.0; p.z += 0.0; return p; };
            Func<int, int> copied = (bv) => {
                if (cut[bv]) return vertex(Composite, compVerts, cutPos(bv));
                if (outOf[bv] < 0) outOf[bv] = Composite.AppendVertex(B.GetVertex(bv));
                return outOf[bv];
            };
            int checkAt = 0;
            // (only the replaced ones without the copy, in the same order)
            foreach (int tb in CopyTerrain ? B.TriangleIndices() : Replaced) {
                if ((++checkAt & 1023) == 0 && cancel != null && cancel()) throw new OperationCanceledException();
                if (replacedSet.Contains(tb) == false) {
                    if (CopyTerrain == false) continue;
                    Index3i v = B.GetTriangle(tb);
                    if (copy == false && (cut[v.a] ? 1 : 0) + (cut[v.b] ? 1 : 0) + (cut[v.c] ? 1 : 0) < 2) continue;
                    int ca = copied(v.a), cb = copied(v.b), cc = copied(v.c);
                    if (ExactPredicates.Orient2d(Composite.GetVertex(ca).xy, Composite.GetVertex(cb).xy, Composite.GetVertex(cc).xy) < 0) { int x = cb; cb = cc; cc = x; }
                    if (Composite.AppendTriangle(ca, cb, cc, terrainGroup) < 0)
                        throw new InvalidOperationException("SlopeComposite: non-manifold composite");
                    continue;
                }
                if (mine.Contains(tb) && theirs.Contains(tb)) {
                    if (joint(tb) == false) return false;
                    continue;
                }
                SlopeComposite own = mine.Contains(tb) ? this : Partner;
                if (own.terrain_whole(tb, own.facesOfB[tb])) continue;
                if (own.partial(false, tb, own.facesOfB[tb]) == false) { Problem = own.Problem; return false; }
            }
            // the terrain vertex at each composite vertex (copied, or a corner of a cut terrain triangle that stayed)
            TerrainVertexOf = new int[Composite.MaxVertexID];
            for (int i = 0; i < TerrainVertexOf.Length; ++i) TerrainVertexOf[i] = -1;
            // and the other way round, per terrain vertex (several terrain vertices at one position, an unwelded TIN, all
            // name the one composite vertex there)
            CompositeVertexOf = new int[B.MaxVertexID];
            for (int i = 0; i < CompositeVertexOf.Length; ++i) CompositeVertexOf[i] = -1;
            if (CopyTerrain)
                for (int bv = 0; bv < outOf.Length; ++bv)
                    if (outOf[bv] >= 0) { TerrainVertexOf[outOf[bv]] = bv; CompositeVertexOf[bv] = outOf[bv]; }
            foreach (int bv in cutVerts) {
                int cv;
                if (compVerts.TryGetValue(cutPos(bv), out cv)) { TerrainVertexOf[cv] = bv; CompositeVertexOf[bv] = cv; }
            }
            return true;
        }

        /// <summary> added to the slope surface's group ids (the second overlay of a mixed run: its pieces after the first's) </summary>
        public int GroupOffset;
        /// <summary> build the whole terrain with the region replaced (else only the replacement: Replaced are not in it) </summary>
        public bool CopyTerrain = true;
        /// <summary>
        /// a check of each build's region, made as soon as the region is complete (null: none). If it fails, the build
        /// does not copy the rest of the terrain (the composite is then not the whole terrain) and RegionFailed is set;
        /// the overlap check and the rebuilds go on as without it, so the final build is the same
        /// </summary>
        internal Func<DMesh3, bool> RegionCheck;
        /// <summary> the final build's region failed RegionCheck (its Composite lacks the copied terrain) </summary>
        internal bool RegionFailed;
        /// <summary> diagnostics: the builds made by the last Build (more than one: rebuilds with catch points held) </summary>
        internal int Builds;
        /// <summary> diagnostics: the builds of the last Build whose region failed RegionCheck </summary>
        internal int RegionFailures;
        /// <summary> diagnostics: the final build copied the whole rest of the terrain (else only the triangles at cut edges) </summary>
        internal bool CopiedTerrain;
        /// <summary> the terrain triangles replaced (cut by the region), sorted </summary>
        public readonly List<int> Replaced = new List<int>();
        readonly HashSet<int> replacedSet = new HashSet<int>();
        /// <summary> per composite vertex: the terrain vertex at its position, or -1 </summary>
        public int[] TerrainVertexOf;
        /// <summary> per terrain vertex: the composite vertex at its position (copied, or a corner of a cut triangle), or -1 </summary>
        public int[] CompositeVertexOf;

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

        // a terrain triangle wholly in the region: all its faces in it, no catch point, no boundary of the slope surface
        // inside, and its corners on the slope surface; nothing of it stays (the slope replaces all of it)
        // (the clauses overlap: a catch point needs a positive sign and the reverse; a corner off the slope surface needs a
        // boundary of it inside; and only triangles with a region face are tested, where a face outside the region with
        // D < 0 is cut off by the toe, a boundary. Tests: TerrainTriangleAroundThePit_IsNotWhollyReplaced for the boundary,
        // the pairs sign + catch point and boundary + corners by the composite area checks of the stress tests)
        bool terrain_whole(int tb, List<int> fl)
        {
            foreach (int fi in fl) {
                var f = O.Faces[fi];
                if (f.Region == false || f.C0 != -1) return false;
                foreach (var oe in f.Edges) {
                    if (O.Sign[oe.P] > 0 || O.Sign[oe.Q] > 0) return false;
                    if (SlopeTerrainOverlay.ekind(oe.Key) == 0 && A.IsBoundaryEdge(SlopeTerrainOverlay.eedge(oe.Key))) return false;
                }
            }
            Index3i t = B.GetTriangle(tb);
            foreach (int v in new[] { t.a, t.b, t.c })
                if (O.Container(SlopeTerrainOverlay.vkeyB(v)) < 0) return false;
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
            PartInput inp = collect(slope, tri, fl, true);
            if (inp == null) return false;
            return triangulate(slope, tri, slope ? A.GetTriangleGroup(tri) + GroupOffset : terrainGroup, inp);
        }

        // the input of a partial triangle: its nodes (realized positions, and for a catch point its key and the composite
        // it belongs to), its constraints as node pairs, and per edge of the triangle its nodes in order with the flag
        // of each gap (in this overlay's region)
        sealed class PartInput
        {
            public readonly List<Vector3d> Pos = new List<Vector3d>();
            public readonly List<bool> IsCatch = new List<bool>();
            public readonly List<SlopeComposite> Owner = new List<SlopeComposite>();
            public readonly List<long> Key = new List<long>();
            public readonly List<Index2i> Constraints = new List<Index2i>();
            public readonly List<int>[] EdgeNodes = { new List<int>(), new List<int>(), new List<int>() };
            public readonly List<bool>[] EdgeFlags = { new List<bool>(), new List<bool>(), new List<bool>() };
        }

        // edgeConstraints: the sub-pieces of the triangle's edges (in the region for the slope, not in it for the terrain)
        // as constraints; else only in EdgeNodes / EdgeFlags (a terrain triangle that both overlays of a mixed run cut)
        PartInput collect(bool slope, int tri, List<int> fl, bool edgeConstraints)
        {
            int[] tv, te;
            if (slope) {
                Index3i t = A.GetTriangle(tri), e = A.GetTriEdges(tri);
                tv = new[] { t.a, t.b, t.c }; te = new[] { e.a, e.b, e.c };
            } else { tv = O.bTriV[tri]; te = O.bTriE[tri]; }
            var inp = new PartInput();
            var nodeIndex = new Dictionary<Node, int>();
            Func<Node, int> idx = (n) => {
                int i;
                if (nodeIndex.TryGetValue(n, out i)) return i;
                i = inp.Pos.Count; nodeIndex[n] = i;
                Vector3d p = position(slope, n);
                p.x += 0.0; p.y += 0.0;     // (-0 is +0, also as a dictionary key)
                inp.Pos.Add(p); inp.IsCatch.Add(n.Catch); inp.Owner.Add(this); inp.Key.Add(n.Key);
                return i;
            };
            var constraints = inp.Constraints;
            int edgeKind = slope ? 0 : 1;
            // the edges of the triangle: the sub-pieces in the region (slope) or not in it (terrain) are constraints
            for (int j = 0; j < 3; ++j) {
                long c0 = slope ? SlopeTerrainOverlay.vkeyA(tv[j]) : SlopeTerrainOverlay.vkeyB(tv[j]);
                long c1 = slope ? SlopeTerrainOverlay.vkeyA(tv[(j + 1) % 3]) : SlopeTerrainOverlay.vkeyB(tv[(j + 1) % 3]);
                var seq = new List<Node>();
                var flags = new List<bool>();
                if (edge_sequence(edgeKind, te[j], fl, c0, c1, seq, flags) == false) return null;
                for (int k = 0; k + 1 < seq.Count; ++k)
                    if (edgeConstraints && flags[k] == slope) constraints.Add(new Index2i(idx(seq[k]), idx(seq[k + 1])));
                foreach (Node n in seq) inp.EdgeNodes[j].Add(idx(n));
                inp.EdgeFlags[j].AddRange(flags);
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
            return inp;
        }

        // the moved catch points of a failed partial triangle keep their own positions in the next build
        static void moved_in_failure(PartInput inp)
        {
            for (int i = 0; i < inp.Pos.Count; ++i) {
                if (inp.IsCatch[i] == false) continue;
                SlopeComposite c = inp.Owner[i];
                long k = inp.Key[i];
                if (c.catchAt[k] != c.catchOwn[k]) c.movedInFailure.Add(k);
            }
        }

        // the part of the triangle: an exact CDT of the nodes with the constraints, its part by parity from outside
        bool triangulate(bool slope, int tri, int group, PartInput inp)
        {
            // positions; nodes at the same XY are one point
            var pts = new List<Vector2d>();
            var zs = new List<double>();
            var pointOf = new int[inp.Pos.Count];
            var at = new Dictionary<Vector2d, int>();
            for (int i = 0; i < inp.Pos.Count; ++i) {
                Vector3d p = inp.Pos[i];
                int pi;
                if (at.TryGetValue(p.xy, out pi) == false) { pi = pts.Count; pts.Add(p.xy); zs.Add(p.z); at[p.xy] = pi; }
                else if (inp.IsCatch[i]) zs[pi] = p.z;      // (a catch point is shared with the other part: its z wins)
                pointOf[i] = pi;
            }
            // the constraints mod 2 (a segment twice is no boundary), without zero-length ones
            var cset = new HashSet<long>();
            foreach (Index2i c in inp.Constraints) {
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
                moved_in_failure(inp);
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
                            moved_in_failure(inp);
                            return false;
                        }
                    }
                }
            }
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
            // (runs that start at one point, eg at a terrain vertex where several slope edges meet it: the one that
            // ends there first, so that the run that goes on along the edge comes after it)
            Func<List<KeyValuePair<Node, bool>>, double> paramEnd = (run) => (O.VertexXY(run[run.Count - 1].Key.Key) - p0).Dot(p1 - p0);
            runs.Sort((x, y) => {
                int c = param(x).CompareTo(param(y));
                return c != 0 ? c : paramEnd(x).CompareTo(paramEnd(y));
            });
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
                // a crossing on a boundary edge of the slope surface, rounded, can lie off the edge's line on the
                // outside; the slope part's edges to it then leave the slope side, where the terrain nodes on that
                // edge are moved to (nudge), and the parts overlap. It is moved by ulps onto the slope side, the
                // mirror of the nudge (as the catch points on these edges are)
                if (kind == 2 && O.BoundaryKind(SlopeTerrainOverlay.vfirst(k)) != 0)
                    xy = onto_slope_side(SlopeTerrainOverlay.vfirst(k), xy, +1);
                // (on S̃: exactly rounded from the planes of the slope triangles, as 1a realizes its vertices)
                zs = kind == 2 ? edge_z(SlopeTerrainOverlay.vfirst(k), xy) : tri_z(O.Container(k), xy);
            }
            if (kind == 2 && slope == false && O.BoundaryKind(SlopeTerrainOverlay.vfirst(k)) != 0) {
                xy = nudge(k, xy);
                Vector3d end;
                if (escaped(k, xy, out end)) return end;
            }
            return new Vector3d(xy.x, xy.y, slope ? zs : zt);
        }

        // a nudged terrain node (the crossing of a slope boundary edge with terrain edge eb) that left both terrain
        // triangles at eb across another terrain edge at an end of eb (it lies in a triangle at that end): it takes the
        // position of that end if the end is not on the slope side. Where a boundary edge of the slope runs along
        // terrain edges within ulps (a toe edge through terrain vertices, rounded in a rotated UTM frame), the terrain
        // left between them is a sliver narrower than an ulp, and the nudge off the slope side can carry the crossing
        // across the next terrain edge at that end: the part of its triangle then overlaps the triangle beyond. At the
        // end the sliver has no area, and the end is a terrain vertex the triangles beyond share. (The test is
        // structural, not a distance: the same in every 2^k scaling.)
        bool escaped(long k, Vector2d x, out Vector3d end)
        {
            end = Vector3d.Zero;
            int eb = SlopeTerrainOverlay.vsecond(k);
            if (in_terrain_triangle(B.GetEdgeT(eb), x)) return false;
            // (not near an end, eg out of the terrain across a boundary edge: it stays)
            Index2i ev = B.GetEdgeV(eb);
            int ve = in_terrain_triangle(B.VtxTrianglesItr(ev.a), x) ? ev.a : in_terrain_triangle(B.VtxTrianglesItr(ev.b), x) ? ev.b : -1;
            if (ve < 0) return false;
            Vector3d e = B.GetVertex(ve);
            int ea = SlopeTerrainOverlay.vfirst(k);
            Vector2d e0, e1;
            slope_on_left(ea, out e0, out e1);
            if (ExactPredicates.Orient2d(e0, e1, e.xy) > 0) return false;
            // (as the cut triangles take a terrain vertex: -0 as +0)
            end = new Vector3d(e.x + 0.0, e.y + 0.0, e.z + 0.0);
            return true;
        }

        bool in_terrain_triangle(Index2i et, Vector2d x) { return in_terrain_triangle(new[] { et.a, et.b }, x); }

        // in one of the (closed, not degenerate) terrain triangles 'tris' (-1 for none)
        bool in_terrain_triangle(IEnumerable<int> tris, Vector2d x)
        {
            foreach (int t in tris) {
                if (t < 0) continue;
                Index3i tv = B.GetTriangle(t);
                Vector2d a = B.GetVertex(tv.a).xy, b = B.GetVertex(tv.b).xy, c = B.GetVertex(tv.c).xy;
                int o = ExactPredicates.Orient2d(a, b, c);
                if (o == 0) continue;
                if (ExactPredicates.Orient2d(a, b, x) * o >= 0 && ExactPredicates.Orient2d(b, c, x) * o >= 0 && ExactPredicates.Orient2d(c, a, x) * o >= 0)
                    return true;
            }
            return false;
        }

        // the ends of slope edge ea in the counter-clockwise order of its triangle: the slope is on its left
        void slope_on_left(int ea, out Vector2d e0, out Vector2d e1)
        {
            Index2i ev = A.GetEdgeV(ea);
            Index3i t = A.GetTriangle(A.GetEdgeT(ea).a);
            e0 = A.GetVertex(ev.a).xy; e1 = A.GetVertex(ev.b).xy;
            if (!((t.a == ev.a && t.b == ev.b) || (t.b == ev.a && t.c == ev.b) || (t.c == ev.a && t.a == ev.b))) { Vector2d s = e0; e0 = e1; e1 = s; }
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
            r = onto_slope_side(SlopeTerrainOverlay.vfirst(k), x, -1);
            nudged[k] = r;
            return r;
        }

        // x moved by ulps until it is not on the given side of the line of boundary edge ea of the slope surface:
        // side -1 off the slope side (on the line or outside), +1 off the outside (on the line or on the slope side)
        Vector2d onto_slope_side(int ea, Vector2d x, int side)
        {
            Vector2d e0, e1;
            slope_on_left(ea, out e0, out e1);
            // (the outward normal; moved along it for side -1, against it for +1)
            Vector2d nrm = new Vector2d(e1.y - e0.y, e0.x - e1.x) * side * -1;
            Vector2d r = x;
            // (from a coordinate of exactly 0 the first step is the ulp of the edge's coordinate scale, not the subnormal
            // 4.9e-324: that one is the same in every frame, so 2^k would not scale the result; a power of two times the
            // scale does. Elsewhere one ulp of the coordinate, as everywhere in a frame without subnormals)
            double u = UlpOfScale(Math.Max(Math.Max(Math.Max(Math.Abs(e0.x), Math.Abs(e0.y)), Math.Max(Math.Abs(e1.x), Math.Abs(e1.y))),
                                             Math.Max(Math.Abs(x.x), Math.Abs(x.y))));
            for (int it = 0; it < 64 && ExactPredicates.Orient2d(e0, e1, r) * side < 0; ++it) {
                if (nrm.x != 0) r.x = Step(r.x, nrm.x > 0, u);
                if (nrm.y != 0) r.y = Step(r.y, nrm.y > 0, u);
            }
            return r;
        }

        // one step up or down: the next double, from exactly 0 (or -0) the scale's ulp u
        internal static double Step(double v, bool up, double u)
        {
            if (v == 0) return up ? u : -u;
            return up ? ExactInterval.Up(v) : ExactInterval.Down(v);
        }

        // the ulp of a positive scale (a power of two; scales with 2^k). (A scale of 0 does not occur: the edge's ends and
        // the point would all be at the origin, Orient2d 0, and nothing moves)
        internal static double UlpOfScale(double m)
        {
            return ExactInterval.Up(m) - m;
        }
    }
}
