// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Repairs what rounding exact vertex coordinates to double leaves behind (eg the output of
    /// ExactMeshBoolean, whose intersection points are rational): self-intersections, and degenerate
    /// triangles (needles and caps where intersection points nearly coincide), by local edits whose result
    /// is checked with exact predicates on the doubles. Only edits that create neither a new intersection nor
    /// a new degenerate triangle in their neighbourhood and remove at least one are applied, so the counts
    /// never grow and the repair terminates.
    ///
    /// The edits follow the modification stage of Milenkovic and Sacks ("Geometric rounding and feature
    /// separation in meshes", CAD 2019), applied after rounding: contract a short edge (length at most
    /// MaxDisplacement times the diagonal; the kept vertex at one of the two ends, which keeps the
    /// coordinates as they are, or at the midpoint), flip an edge (caps), and, beyond the paper, move a single
    /// vertex by a tiny amount (along the normal of the triangle it intersects or away from the base of a
    /// cap), which separates intersections that no contraction can (sheets that come within a rounding unit
    /// of each other), and zip two caps on both sides of an edge whose apexes lie on it (a flip would give two
    /// caps again: both are removed and the neighbouring edges along the segment split at the apexes). The
    /// contractions keep the mesh manifold (link condition, DMesh3.CollapseEdge) and leave boundary vertices
    /// alone. No triangle may turn over in an edit (a fold, which is not an intersection), except slivers
    /// (below 100 times the degenerate area) and triangles not higher than twice the distance a vertex moves
    /// (a move cannot turn over a triangle higher than the move, so the rule matters for flips and zips: tiny
    /// triangles may turn over, they are only checked for intersections and degeneracy). Components that keep a degenerate triangle
    /// or were edited, and are tiny (below 1e4 times the degenerate area, eg a flat tetrahedron between
    /// intersection points that nearly coincide) or closed and thinner than MaxDisplacement times the diagonal
    /// (eg the slivers that are the difference of two nearly coincident solids), are removed if
    /// RemoveTinyComponents, as Milenkovic and Sacks do.
    ///
    /// The degenerate test is the one of MeshRobustRepair.NotCleanReason (area below DegenerateArea times the
    /// squared diagonal, or exactly collinear corners); intersections are those of
    /// MeshRepairMetrics.SelfIntersections (exact, also between neighbours that overlap). With
    /// SharedPositionsTouch, vertices at the same position count as shared, so that sheets which touch at
    /// duplicated vertices (ExactMeshBoolean splits where solids touch) do not count as intersecting there; then
    /// no edit moves a vertex onto a position where other vertices are (which would make a new, uncounted
    /// contact), and a contraction keeps the contact points as they are (the kept vertex at its own position).
    ///
    /// Usage: construct with the mesh (modified in place), optionally set FixedVertexF to the vertices whose
    /// position should be kept where possible (eg input vertices), call Apply(). Apply() returns IsClean:
    /// RemainingDegenerate is counted again at the end, RemainingIntersections too if there were any (no
    /// edit adds one: each is checked exactly against all triangles near it), so a clean result is free of
    /// self-intersections and degenerate triangles (Milenkovic and Sacks get the same guarantee by separating
    /// the features before rounding; this works on the rounded mesh and has no guarantee that every case can
    /// be repaired). The vertex positions move by at most about MaxDisplacement times the diagonal per edit.
    /// Cost: one global exact self-intersection check (two or more if there are intersections), plus local
    /// work per defect.
    /// Implemented from the paper, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class MeshRoundingRepair
    {
        /// <summary> the mesh, repaired in place </summary>
        public DMesh3 Mesh;
        /// <summary> a triangle is degenerate below this area, relative to the squared bounding box diagonal (as MeshRobustRepair.NotCleanReason) </summary>
        public double DegenerateArea = 1e-12;
        /// <summary> longest edge contracted and farthest vertex move, relative to the bounding box diagonal </summary>
        public double MaxDisplacement = 1e-5;
        /// <summary> vertices at the same position count as shared vertices (touching sheets, see class comment) </summary>
        public bool SharedPositionsTouch = false;
        /// <summary> vertices whose position is kept where possible (tried last as the moved vertex); null = none </summary>
        public Func<int, bool> FixedVertexF = null;
        /// <summary> passes over the defects </summary>
        public int MaxPasses = 4;
        /// <summary> remove tiny or thin components that keep a degenerate triangle or were edited (see class comment) </summary>
        public bool RemoveTinyComponents = true;

        /// <summary> intersecting triangle pairs and degenerate triangles before the repair </summary>
        public int InitialIntersections, InitialDegenerate;
        /// <summary> intersecting triangle pairs and degenerate triangles after the repair (exact, see class comment) </summary>
        public int RemainingIntersections, RemainingDegenerate;
        /// <summary> edits applied (see class comment) </summary>
        public int Collapses, Flips, Moves, Zips;
        /// <summary> components, and their triangles, removed as tiny or thin (RemoveTinyComponents) </summary>
        public int RemovedComponents, RemovedTriangles;
        /// <summary> no self-intersections and no degenerate triangles remain </summary>
        public bool IsClean { get { return RemainingIntersections == 0 && RemainingDegenerate == 0; } }

        public MeshRoundingRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        double areaEps, maxLength;
        // triangles above this many times the degenerate area must not turn over in an edit
        const double OrientedArea = 100;
        // vertices per position (SharedPositionsTouch)
        Dictionary<Vector3d, List<int>> atPosition;
        // spatial index over the triangles at the last build; triangles changed since are in dirty
        TriangleBoxTree tree;
        List<int> treeTids;
        HashSet<int> dirty = new HashSet<int>();
        // vertices of applied edits
        HashSet<int> touched = new HashSet<int>();

        /// <summary> repair (see class comment); returns IsClean </summary>
        public bool Apply()
        {
            Collapses = Flips = Moves = Zips = RemovedComponents = RemovedTriangles = 0;
            touched.Clear();
            if (Mesh.TriangleCount == 0) {
                InitialIntersections = InitialDegenerate = RemainingIntersections = RemainingDegenerate = 0;
                return true;
            }
            double diag = Mesh.CachedBounds.DiagonalLength;
            areaEps = DegenerateArea * diag * diag;
            maxLength = MaxDisplacement * diag;
            if (SharedPositionsTouch) {
                atPosition = new Dictionary<Vector3d, List<int>>();
                foreach (int vid in Mesh.VertexIndices())
                    position_add(vid, Mesh.GetVertex(vid));
            }
            rebuild_tree();

            List<Index2i> pairs = intersecting_pairs();
            List<int> bad = degenerate_triangles();
            InitialIntersections = pairs.Count;
            InitialDegenerate = bad.Count;
            for (int pass = 0; pass < MaxPasses && (pairs.Count > 0 || bad.Count > 0); ++pass) {
                int edits = Collapses + Flips + Moves + Zips;
                // smallest first: their contraction often fixes the larger neighbours too
                bad.Sort((x, y) => area(x).CompareTo(area(y)));
                foreach (int t in bad)
                    if (Mesh.IsTriangle(t) && is_bad(t))
                        fix_degenerate(t);
                // (no edit adds an intersecting pair: without pairs there are none to find)
                if (pairs.Count > 0) {
                    if (Collapses + Flips + Moves + Zips > edits)
                        pairs = intersecting_pairs();
                    foreach (Index2i p in pairs)
                        if (Mesh.IsTriangle(p.a) && Mesh.IsTriangle(p.b) && intersects(p.a, p.b))
                            fix_intersection(p.a, p.b);
                    pairs = intersecting_pairs();
                }
                bad = degenerate_triangles();
                if (Collapses + Flips + Moves + Zips == edits)
                    break;
            }
            if (RemoveTinyComponents && (bad.Count > 0 || touched.Count > 0) && remove_tiny_components(bad) > 0) {
                bad = degenerate_triangles();
                if (pairs.Count > 0)
                    pairs = intersecting_pairs();
            }
            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            return IsClean;
        }


        // ---- geometry of the current mesh, or of a proposed edit (one vertex at another position) ----

        int movedV = -1;
        Vector3d movedPos;
        // the vertex a proposed contraction removes, or -1
        int vanishing = -1;

        Vector3d pos(int v)
        {
            return (v == movedV) ? movedPos : Mesh.GetVertex(v);
        }

        Triangle3d triangle(Index3i t)
        {
            return new Triangle3d(pos(t.a), pos(t.b), pos(t.c));
        }

        AxisAlignedBox3d box(Index3i t)
        {
            var b = new AxisAlignedBox3d(pos(t.a));
            b.Contain(pos(t.b)); b.Contain(pos(t.c));
            return b;
        }

        double area(int tid)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return area(t);
        }

        double area(Index3i t)
        {
            Vector3d a = pos(t.a);
            return 0.5 * (pos(t.b) - a).Cross(pos(t.c) - a).Length;
        }

        bool is_bad(int tid)
        {
            return is_bad(Mesh.GetTriangle(tid));
        }

        bool is_bad(Index3i t)
        {
            if (area(t) < areaEps)
                return true;
            Triangle3d tri = triangle(t);
            return ExactTriangleIntersection.IsDegenerate(ref tri);
        }

        static Vector3d key(Vector3d p)
        {
            // -0.0 and 0.0 are the same position
            return new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0);
        }

        void position_add(int v, Vector3d p)
        {
            List<int> list;
            if (atPosition.TryGetValue(key(p), out list) == false)
                atPosition[key(p)] = list = new List<int>();
            list.Add(v);
        }

        void position_remove(int v, Vector3d p)
        {
            List<int> list;
            if (atPosition.TryGetValue(key(p), out list)) {
                list.Remove(v);
                if (list.Count == 0)
                    atPosition.Remove(key(p));
            }
        }

        // the vertex id that stands for all vertices at v's position (SharedPositionsTouch), else v
        int canonical(int v)
        {
            if (atPosition == null)
                return v;
            // the smallest id at v's position: the vertices there, the moved one at its proposed position
            Vector3d p = key(pos(v));
            int best = v;
            List<int> list;
            if (atPosition.TryGetValue(p, out list))
                foreach (int u in list)
                    if (u != movedV && u < best)
                        best = u;
            if (movedV >= 0 && movedV < best && key(movedPos) == p)
                best = movedV;
            return best;
        }

        Index3i canonical(Index3i t)
        {
            return new Index3i(canonical(t.a), canonical(t.b), canonical(t.c));
        }

        // do the non-degenerate triangles t and u intersect beyond what they share (MeshRepairMetrics)
        bool intersects(Index3i t, ref Triangle3d T, Index3i u, ref Triangle3d U)
        {
            return ExactTriangleIntersection.IntersectsBeyondShared(canonical(t), ref T, canonical(u), ref U);
        }

        bool intersects(int ta, int tb)
        {
            Index3i a = Mesh.GetTriangle(ta), b = Mesh.GetTriangle(tb);
            Triangle3d A = triangle(a), B = triangle(b);
            if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                return false;
            return intersects(a, ref A, b, ref B);
        }


        // ---- spatial queries ----

        void rebuild_tree()
        {
            var boxes = new List<AxisAlignedBox3d>();
            treeTids = new List<int>();
            foreach (int tid in Mesh.TriangleIndices()) {
                treeTids.Add(tid);
                boxes.Add(box(Mesh.GetTriangle(tid)));
            }
            tree = new TriangleBoxTree(boxes);
            dirty.Clear();
        }

        void mark_dirty(int tid)
        {
            dirty.Add(tid);
            // (the changed triangles are searched linearly)
            if (dirty.Count > Math.Min(Math.Max(256, Mesh.TriangleCount / 64), 2048))
                rebuild_tree();
        }

        static bool overlaps(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return !(a.Max.x < b.Min.x || a.Min.x > b.Max.x || a.Max.y < b.Min.y || a.Min.y > b.Max.y || a.Max.z < b.Min.z || a.Min.z > b.Max.z);
        }

        // the current triangles whose box overlaps query (each once)
        void near(AxisAlignedBox3d query, List<int> result)
        {
            result.Clear();
            tree.Query(query, (i) => {
                int t = treeTids[i];
                if (dirty.Contains(t) == false && Mesh.IsTriangle(t))
                    result.Add(t);
            });
            foreach (int t in dirty)
                if (Mesh.IsTriangle(t) && overlaps(query, box(Mesh.GetTriangle(t))))
                    result.Add(t);
        }

        List<Index2i> intersecting_pairs()
        {
            if (dirty.Count > 0)
                rebuild_tree();
            int[] tris = treeTids.ToArray();
            var pairs = new List<Index2i>();
            object lockObj = new object();
            gParallel.ForEach(Interval1i.Range(tris.Length), (k) => {
                int ta = tris[k];
                Index3i ia = Mesh.GetTriangle(ta);
                Triangle3d A = triangle(ia);
                if (ExactTriangleIntersection.IsDegenerate(ref A))
                    return;
                List<Index2i> local = null;
                tree.Query(box(ia), (i) => {
                    int tb = tris[i];
                    if (tb <= ta) return;
                    Index3i ib = Mesh.GetTriangle(tb);
                    Triangle3d B = triangle(ib);
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
                    if (intersects(ia, ref A, ib, ref B)) {
                        if (local == null) local = new List<Index2i>();
                        local.Add(new Index2i(ta, tb));
                    }
                });
                if (local != null)
                    lock (lockObj) pairs.AddRange(local);
            });
            pairs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return pairs;
        }

        List<int> degenerate_triangles()
        {
            var list = new List<int>();
            foreach (int tid in Mesh.TriangleIndices())
                if (is_bad(tid))
                    list.Add(tid);
            return list;
        }


        // ---- edits: proposed as triangles removed and triangles added (and one moved vertex) ----

        readonly List<int> removed = new List<int>();
        readonly List<Index3i> added = new List<Index3i>();
        readonly List<int> addedFrom = new List<int>();     // the removed triangle an added one replaces, or -1
        readonly List<int> scratch = new List<int>();

        void begin_edit()
        {
            removed.Clear(); added.Clear(); addedFrom.Clear();
            movedV = -1;
            vanishing = -1;
        }

        // intersecting pairs and degenerate triangles among the removed triangles and their neighbours, with
        // the current geometry, against the same for the added triangles with the proposed geometry: true if
        // neither count grows and their sum drops, and no added triangle turns over
        bool improves(double displacement = 0)
        {
            int saveV = movedV;
            Vector3d savePos = movedPos;
            // (SharedPositionsTouch: a vertex moved onto other vertices would touch them uncounted)
            List<int> there;
            if (atPosition != null && saveV >= 0 && savePos != Mesh.GetVertex(saveV) && atPosition.TryGetValue(key(savePos), out there))
                foreach (int u in there)
                    if (u != saveV && u != vanishing)
                        return false;
            movedV = -1;
            int oldI = 0, oldB = 0;
            var removedSet = new HashSet<int>(removed);
            var oldCross = new Vector3d[removed.Count];
            Vector3d crossSum = Vector3d.Zero;
            for (int i = 0; i < removed.Count; ++i) {
                int r = removed[i];
                Index3i t = Mesh.GetTriangle(r);
                Triangle3d T = triangle(t);
                oldCross[i] = (T.V1 - T.V0).Cross(T.V2 - T.V0);
                if (is_bad(t))
                    oldB++;
                // (slivers just above the degenerate area, and triangles not higher than twice the displacement
                // of the edit, may turn over: tiny folds, intersections are checked)
                double longest = Math.Max(T.V0.Distance(T.V1), Math.Max(T.V1.Distance(T.V2), T.V2.Distance(T.V0)));
                if (oldCross[i].Length < 2 * OrientedArea * areaEps || oldCross[i].Length <= 2 * displacement * longest)
                    oldCross[i] = Vector3d.Zero;
                else
                    crossSum += oldCross[i];
                if (ExactTriangleIntersection.IsDegenerate(ref T))
                    continue;
                near(box(t), scratch);
                foreach (int x in scratch) {
                    if (x == r || (removedSet.Contains(x) && x < r))
                        continue;
                    Index3i u = Mesh.GetTriangle(x);
                    Triangle3d U = triangle(u);
                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && intersects(t, ref T, u, ref U))
                        oldI++;
                }
            }
            movedV = saveV; movedPos = savePos;

            int newI = 0, newB = 0;
            var tris = new Triangle3d[added.Count];
            var degen = new bool[added.Count];
            for (int i = 0; i < added.Count; ++i) {
                Index3i t = added[i];
                tris[i] = triangle(t);
                Vector3d cross = (tris[i].V1 - tris[i].V0).Cross(tris[i].V2 - tris[i].V0);
                // the replaced triangle's orientation (unless it is a sliver), for flips that of both
                Vector3d reference = (addedFrom[i] >= 0) ? oldCross[addedFrom[i]] : crossSum;
                if (cross.Dot(reference) <= 0 && reference != Vector3d.Zero)
                    return false;
                if (is_bad(t))
                    newB++;
                degen[i] = ExactTriangleIntersection.IsDegenerate(ref tris[i]);
                if (newB > oldB)
                    return false;
            }
            for (int i = 0; i < added.Count; ++i) {
                if (degen[i])
                    continue;
                Index3i t = added[i];
                near(box(t), scratch);
                foreach (int x in scratch) {
                    if (removedSet.Contains(x))
                        continue;
                    Index3i u = Mesh.GetTriangle(x);
                    // (the moved vertex's triangles are all removed: u has the current geometry)
                    Triangle3d U = triangle(u);
                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && intersects(t, ref tris[i], u, ref U))
                        if (++newI > oldI) return false;
                }
                for (int j = i + 1; j < added.Count; ++j)
                    if (degen[j] == false && intersects(t, ref tris[i], added[j], ref tris[j]))
                        if (++newI > oldI) return false;
            }
            return newI + newB < oldI + oldB;
        }

        bool is_fixed(int v)
        {
            return FixedVertexF != null && FixedVertexF(v);
        }

        bool interior_vertex(int v)
        {
            return Mesh.IsBoundaryVertex(v) == false && Mesh.IsBowtieVertex(v) == false;
        }

        // contract edge (a, b): b is kept, at position p
        bool try_collapse(int a, int b, Vector3d p)
        {
            int eid = Mesh.FindEdge(a, b);
            if (eid < 0 || Mesh.IsBoundaryEdge(eid) || interior_vertex(a) == false || interior_vertex(b) == false)
                return false;
            begin_edit();
            foreach (int t in Mesh.VtxTrianglesItr(a)) {
                removed.Add(t);
                Index3i tv = Mesh.GetTriangle(t);
                if (tv.a == b || tv.b == b || tv.c == b)
                    continue;
                added.Add(new Index3i(tv.a == a ? b : tv.a, tv.b == a ? b : tv.b, tv.c == a ? b : tv.c));
                addedFrom.Add(removed.Count - 1);
            }
            if (p != Mesh.GetVertex(b)) {
                foreach (int t in Mesh.VtxTrianglesItr(b)) {
                    Index3i tv = Mesh.GetTriangle(t);
                    if (tv.a == a || tv.b == a || tv.c == a)
                        continue;
                    removed.Add(t);
                    added.Add(tv);
                    addedFrom.Add(removed.Count - 1);
                }
            }
            movedV = b; movedPos = p; vanishing = a;
            bool ok = improves(Math.Max(p.Distance(Mesh.GetVertex(a)), p.Distance(Mesh.GetVertex(b))));
            movedV = -1;
            if (ok == false)
                return false;
            Vector3d pa = Mesh.GetVertex(a);
            DMesh3.EdgeCollapseInfo info;
            if (Mesh.CollapseEdge(b, a, out info) != MeshResult.Ok)
                return false;
            if (atPosition != null)
                position_remove(a, pa);
            set_vertex(b, p);
            foreach (int t in Mesh.VtxTrianglesItr(b))
                mark_dirty(t);
            touched.Add(b);
            Collapses++;
            return true;
        }

        bool try_flip(int eid)
        {
            if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid))
                return false;
            // as DMesh3.FlipEdge: edge a -> b in t0 = (a, b, c), t1 has d; afterwards t0 = (c, d, b), t1 = (d, c, a)
            Index2i ev = Mesh.GetEdgeV(eid), et = Mesh.GetEdgeT(eid);
            int a = ev.a, b = ev.b;
            int[] t0v = Mesh.GetTriangle(et.a).array, t1v = Mesh.GetTriangle(et.b).array;
            int c = IndexUtil.orient_tri_edge_and_find_other_vtx(ref a, ref b, t0v);
            int d = IndexUtil.find_tri_other_vtx(a, b, t1v);
            if (c < 0 || d < 0 || Mesh.FindEdge(c, d) >= 0)
                return false;
            begin_edit();
            removed.Add(et.a); removed.Add(et.b);
            added.Add(new Index3i(c, d, b)); addedFrom.Add(-1);
            added.Add(new Index3i(d, c, a)); addedFrom.Add(-1);
            if (improves() == false)
                return false;
            DMesh3.EdgeFlipInfo info;
            if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok)
                return false;
            mark_dirty(et.a); mark_dirty(et.b);
            touched.Add(a); touched.Add(b);
            Flips++;
            return true;
        }

        // the triangle across edge (u, v) from t, or -1
        int across(int t, int u, int v)
        {
            int eid = Mesh.FindEdge(u, v);
            if (eid < 0) return -1;
            Index2i et = Mesh.GetEdgeT(eid);
            return (et.a == t) ? et.b : (et.b == t) ? et.a : -1;
        }

        // two caps on both sides of edge (a, b), T = (a, b, c) and U = (b, a, d), whose apexes lie between a and b
        // (a flip gives two caps again): T and U removed, and the edges of their neighbours that run along
        // the segment split at the other apex, so that the two sides are zipped together. With the order a, c,
        // d, b along the edge: N1 = (c, b, x) across (b, c) becomes (c, d, x), (d, b, x); N2 = (d, a, y) across
        // (a, d) becomes (d, c, y), (c, a, y).
        bool try_zip(int eid)
        {
            if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid))
                return false;
            Index2i ev = Mesh.GetEdgeV(eid), et = Mesh.GetEdgeT(eid);
            int a = ev.a, b = ev.b;
            int tT = et.a, tU = et.b;
            int c = IndexUtil.orient_tri_edge_and_find_other_vtx(ref a, ref b, Mesh.GetTriangle(tT).array);
            int d = IndexUtil.find_tri_other_vtx(a, b, Mesh.GetTriangle(tU).array);
            if (c < 0 || d < 0 || c == d || Mesh.FindEdge(c, d) >= 0)
                return false;
            Vector3d pa = pos(a), ab = pos(b) - pa;
            double sc = (pos(c) - pa).Dot(ab) / ab.LengthSquared, sd = (pos(d) - pa).Dot(ab) / ab.LengthSquared;
            if (!(sc > 0 && sc < 1 && sd > 0 && sd < 1) || sc == sd)
                return false;
            int n1, n2, x, y;
            begin_edit();
            removed.Add(tT); removed.Add(tU);
            if (sc < sd) {
                n1 = across(tT, b, c); n2 = across(tU, a, d);
                if (n1 < 0 || n2 < 0) return false;
                x = IndexUtil.find_tri_other_vtx(b, c, Mesh.GetTriangle(n1).array);
                y = IndexUtil.find_tri_other_vtx(a, d, Mesh.GetTriangle(n2).array);
                added.Add(new Index3i(c, d, x)); added.Add(new Index3i(d, b, x));
                added.Add(new Index3i(d, c, y)); added.Add(new Index3i(c, a, y));
            } else {
                // order a, d, c, b: N1 = (a, c, x) across (c, a) becomes (a, d, x), (d, c, x); N2 = (b, d, y) across
                // (d, b) becomes (b, c, y), (c, d, y)
                n1 = across(tT, c, a); n2 = across(tU, d, b);
                if (n1 < 0 || n2 < 0) return false;
                x = IndexUtil.find_tri_other_vtx(c, a, Mesh.GetTriangle(n1).array);
                y = IndexUtil.find_tri_other_vtx(d, b, Mesh.GetTriangle(n2).array);
                added.Add(new Index3i(a, d, x)); added.Add(new Index3i(d, c, x));
                added.Add(new Index3i(b, c, y)); added.Add(new Index3i(c, d, y));
            }
            if (n1 == n2 || n1 == tU || n2 == tT || x < 0 || y < 0 || x == d || y == c || x == y)
                return false;
            removed.Add(n1); removed.Add(n2);
            for (int k = 0; k < 4; ++k)
                addedFrom.Add(k < 2 ? 2 : 3);
            // (the new edges must not exist yet: (d, x), (c, y))
            if (Mesh.FindEdge(d, x) >= 0 || Mesh.FindEdge(c, y) >= 0)
                return false;
            if (improves() == false)
                return false;
            var old = new List<Index3i>();
            foreach (int t in removed) {
                old.Add(Mesh.GetTriangle(t));
                Mesh.RemoveTriangle(t, false, false);
            }
            var tids = new List<int>();
            foreach (Index3i t in added) {
                int tid = Mesh.AppendTriangle(t.a, t.b, t.c);
                if (tid < 0) {
                    // (not expected: the checks above keep the mesh manifold) undone
                    foreach (int u in tids)
                        Mesh.RemoveTriangle(u, false, false);
                    foreach (Index3i u in old)
                        mark_dirty(Mesh.AppendTriangle(u.a, u.b, u.c));
                    return false;
                }
                tids.Add(tid);
                mark_dirty(tid);
            }
            touched.Add(c); touched.Add(d);
            Zips++;
            return true;
        }

        bool try_move(int v, Vector3d p)
        {
            if (p == Mesh.GetVertex(v) || double.IsNaN(p.x + p.y + p.z))
                return false;
            begin_edit();
            foreach (int t in Mesh.VtxTrianglesItr(v)) {
                removed.Add(t);
                added.Add(Mesh.GetTriangle(t));
                addedFrom.Add(removed.Count - 1);
            }
            movedV = v; movedPos = p;
            bool ok = improves(p.Distance(Mesh.GetVertex(v)));
            movedV = -1;
            if (ok == false)
                return false;
            set_vertex(v, p);
            foreach (int t in Mesh.VtxTrianglesItr(v))
                mark_dirty(t);
            touched.Add(v);
            Moves++;
            return true;
        }

        void set_vertex(int v, Vector3d p)
        {
            if (atPosition != null) {
                position_remove(v, Mesh.GetVertex(v));
                position_add(v, p);
            }
            Mesh.SetVertex(v, p);
        }

        // contract one of the edges of the triangles (shortest first, at most maxLength), the kept vertex at
        // a fixed end, else at either end or the midpoint
        bool collapse_any(params int[] tids)
        {
            var edges = new List<KeyValuePair<double, int>>();
            foreach (int t in tids) {
                Index3i te = Mesh.GetTriEdges(t);
                for (int k = 0; k < 3; ++k) {
                    Index2i ev = Mesh.GetEdgeV(te[k]);
                    double len = Mesh.GetVertex(ev.a).Distance(Mesh.GetVertex(ev.b));
                    if (len <= maxLength)
                        edges.Add(new KeyValuePair<double, int>(len, te[k]));
                }
            }
            edges.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (var e in edges) {
                if (Mesh.IsEdge(e.Value) == false)
                    continue;
                Index2i ev = Mesh.GetEdgeV(e.Value);
                int a = ev.a, b = ev.b;
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b);
                bool fa = is_fixed(a), fb = is_fixed(b);
                // (keep b at p)
                if (fa && fb == false) {
                    if (try_collapse(b, a, pa)) return true;
                } else if (fb && fa == false) {
                    if (try_collapse(a, b, pb)) return true;
                } else {
                    if (try_collapse(a, b, pb) || try_collapse(b, a, pa)) return true;
                    if (fa == false && try_collapse(a, b, 0.5 * (pa + pb))) return true;
                }
            }
            return false;
        }

        void fix_degenerate(int t)
        {
            if (collapse_any(t))
                return;
            // caps: flip the longest edge first; else move the corner opposite the longest edge away from it
            Index3i tv = Mesh.GetTriangle(t), te = Mesh.GetTriEdges(t);
            int[] order = { 0, 1, 2 };
            double[] len = new double[3];
            for (int k = 0; k < 3; ++k)
                len[k] = -pos(tv[k]).Distance(pos(tv[(k + 1) % 3]));
            Array.Sort(len, order);
            foreach (int k in order)
                if (try_flip(te[k]) || try_zip(te[k]))
                    return;
            int kl = order[0];
            int apex = tv[(kl + 2) % 3];
            Vector3d e0 = pos(tv[kl]), e1 = pos(tv[(kl + 1) % 3]), c = pos(apex);
            Vector3d dir = (e1 - e0).Normalized;
            Vector3d off = (c - e0) - (c - e0).Dot(dir) * dir;
            if (off.Length < 1e-300) {
                // on the line: towards the centroid of the apex's other neighbours
                Vector3d ctr = Vector3d.Zero;
                int n = 0;
                foreach (int u in Mesh.VtxVerticesItr(apex)) { ctr += pos(u); n++; }
                off = ctr / Math.Max(n, 1) - c;
                off -= off.Dot(dir) * dir;
            }
            if (off.Length < 1e-300 || -len[0] <= 0)
                return;
            off = off.Normalized;
            // height for the degenerate area, and more
            double h = 2 * areaEps / -len[0];
            for (double s = 2 * h; s <= maxLength; s *= 4)
                if (try_move(apex, c + s * off))
                    return;
        }

        void fix_intersection(int ta, int tb)
        {
            if (collapse_any(ta, tb))
                return;
            Index3i a = Mesh.GetTriangle(ta), b = Mesh.GetTriangle(tb);
            Vector3d na = Mesh.GetTriNormal(ta), nb = Mesh.GetTriNormal(tb);
            // movable vertices first; each along the other triangle's normal (both ways) or towards the centroid
            // of its neighbours, from a few units in the last place up to maxLength
            var verts = new List<KeyValuePair<int, Vector3d>>();
            for (int pass = 0; pass < 2; ++pass)
                for (int k = 0; k < 6; ++k) {
                    int v = (k < 3) ? a[k] : b[k - 3];
                    if (is_fixed(v) == (pass == 0))
                        continue;
                    verts.Add(new KeyValuePair<int, Vector3d>(v, (k < 3) ? nb : na));
                }
            foreach (var vn in verts) {
                int v = vn.Key;
                if (Mesh.IsVertex(v) == false)
                    continue;
                Vector3d p = Mesh.GetVertex(v);
                Vector3d ctr = Vector3d.Zero;
                int n = 0;
                foreach (int u in Mesh.VtxVerticesItr(v)) { ctr += Mesh.GetVertex(u); n++; }
                Vector3d toCtr = (n > 0) ? (ctr / n - p).Normalized : Vector3d.Zero;
                double ulp = 8 * 2.220446049250313e-16 * Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))) + 1e-300;
                for (double s = ulp; s <= maxLength; s *= 4) {
                    if (vn.Value.LengthSquared > 0.5 && (try_move(v, p + s * vn.Value) || try_move(v, p - s * vn.Value)))
                        return;
                    if (toCtr.LengthSquared > 0.5 && try_move(v, p + s * toCtr))
                        return;
                }
            }
            Index3i ea = Mesh.GetTriEdges(ta), eb = Mesh.GetTriEdges(tb);
            for (int k = 0; k < 3; ++k)
                if (try_flip(ea[k]) || (Mesh.IsTriangle(tb) && try_flip(eb[k])))
                    return;
        }

        // components with a degenerate triangle or an edited vertex that are tiny or thin
        int remove_tiny_components(List<int> bad)
        {
            var comps = new MeshConnectedComponents(Mesh);
            comps.FindConnectedT();
            var badSet = new HashSet<int>(bad);
            int count = 0;
            foreach (var comp in comps.Components) {
                bool hasBad = false;
                double sum = 0, volume = 0;
                // (the volume relative to a point of the component, against cancellation far from the origin)
                Vector3d o = pos(Mesh.GetTriangle(comp.Indices[0]).a);
                foreach (int t in comp.Indices) {
                    Index3i tv = Mesh.GetTriangle(t);
                    hasBad |= badSet.Contains(t) || touched.Contains(tv.a) || touched.Contains(tv.b) || touched.Contains(tv.c);
                    sum += area(t);
                    volume += (pos(tv.a) - o).Dot((pos(tv.b) - o).Cross(pos(tv.c) - o)) / 6;
                }
                if (hasBad == false)
                    continue;
                // tiny, or (closed) thinner than maxLength: a slab of thickness h has about volume = h area / 2
                bool closed = true;
                foreach (int t in comp.Indices) {
                    Index3i te = Mesh.GetTriEdges(t);
                    closed &= Mesh.IsBoundaryEdge(te.a) == false && Mesh.IsBoundaryEdge(te.b) == false && Mesh.IsBoundaryEdge(te.c) == false;
                }
                if (sum >= 1e4 * areaEps && (closed == false || 2 * Math.Abs(volume) >= maxLength * sum))
                    continue;
                foreach (int t in comp.Indices)
                    Mesh.RemoveTriangle(t, true, false);
                count += comp.Indices.Length;
                RemovedComponents++;
            }
            if (count > 0) {
                RemovedTriangles += count;
                if (atPosition != null) {
                    atPosition.Clear();
                    foreach (int vid in Mesh.VertexIndices())
                        position_add(vid, Mesh.GetVertex(vid));
                }
                rebuild_tree();
            }
            return count;
        }
    }
}
