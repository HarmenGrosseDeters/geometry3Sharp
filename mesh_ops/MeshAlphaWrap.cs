// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

namespace gs
{
    /// <summary>
    /// Alpha wrapping with an offset (Portaneri, Rouxel-Labbé, Hemmer, Cohen-Steiner, Alliez, "Alpha Wrapping
    /// with an Offset", ACM TOG 2022): a closed, oriented, 2-manifold surface without self-intersections that
    /// strictly encloses any input: triangle soups, open or non-manifold meshes, degenerate triangles, point
    /// clouds. The result is computed, not repaired, so it does not depend on the input being valid.
    ///
    /// The method refines and carves a 3D Delaunay triangulation (Delaunay3), starting from a box around the
    /// input. A ball of radius Alpha "flows" in from outside through Delaunay facets it fits through (facet
    /// circumradius above Alpha); a cell it reaches is carved (marked outside), unless
    ///   1. the Voronoi edge dual to the facet crosses the offset surface (the points at distance Offset from
    ///      the input): its first crossing, coming from outside, is inserted as a Steiner point;
    ///   2. the cell intersects the input: the projection of its circumcenter onto the offset surface is
    ///      inserted.
    /// The result is the boundary between the outside and the remaining cells, made manifold by moving
    /// outside cells around non-manifold edges and vertices to the inside.
    ///
    /// Guarantees (with the exact predicates of Delaunay3; the result vertices are the triangulation vertices,
    /// no rounding):
    ///   - closed, consistently oriented (outwards), 2-manifold, no self-intersections;
    ///   - the input is strictly inside: no outside cell comes closer than 1e-6 * Offset to it (the margin of
    ///     the floating-point distance tests);
    ///   - the vertices lie on the offset surface (up to rounding), so between them the result can come closer
    ///     to the input than Offset (by the sagitta of the triangles); it stays within about Offset + Alpha.
    /// Alpha controls the detail: cavities and gaps narrower than about 2 * Alpha are not entered (they are
    /// wrapped over), and the result has triangles with a circumradius of at most about Alpha. Smaller Alpha =
    /// more detail, more triangles, more time (about 1 / Alpha^2). Offset keeps the result away from the input.
    /// Typical values: Alpha 1/100 to 1/20 of the bounding box diagonal, Offset 1/30 of Alpha.
    ///
    /// Not covered by the guarantees: with MaxSteinerPoints reached (PointLimitReached) the box corners can be
    /// vertices of the result.
    ///
    /// Implemented from the paper, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class MeshAlphaWrap
    {
        public enum WrapStatus
        {
            Success = 0,
            /// <summary> no triangles and no points </summary>
            EmptyInput = 1,
            /// <summary> non-finite coordinates </summary>
            InvalidInput = 2,
            /// <summary> Alpha / Offset not positive (eg relative values for an input of zero size) </summary>
            InvalidParameters = 3,
            Cancelled = 4,
            /// <summary>
            /// MaxSteinerPoints reached: the refinement stopped early. Result is set and has all the guarantees
            /// (closed, manifold, encloses the input), but has less detail than requested.
            /// </summary>
            PointLimitReached = 5,
            /// <summary> internal failure (should not happen), see Error </summary>
            Failed = 6
        }

        /// <summary> input triangles (any mesh: soup, open, non-manifold); vertices without triangles are used as points </summary>
        public DMesh3 Mesh;
        /// <summary> additional input points (point cloud) </summary>
        public List<Vector3d> Points = new List<Vector3d>();

        /// <summary> ball radius (detail). If not positive: bounding box diagonal / RelativeAlpha </summary>
        public double Alpha = 0;
        public double RelativeAlpha = 20;
        /// <summary> distance of the result to the input. If not positive: bounding box diagonal / RelativeOffset </summary>
        public double Offset = 0;
        public double RelativeOffset = 600;

        /// <summary> stop refining after this many Steiner points (status PointLimitReached) </summary>
        public int MaxSteinerPoints = 2000000;
        /// <summary> make the result 2-manifold (otherwise it can have non-manifold edges and vertices, which DMesh3 rejects; for tests) </summary>
        internal bool MakeManifold = true;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Result;
        public WrapStatus Status;
        /// <summary> why Compute() failed, otherwise null </summary>
        public string Error;
        /// <summary> the alpha and offset used (absolute) </summary>
        public double UsedAlpha, UsedOffset;
        /// <summary> time per phase, for diagnostics </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> Steiner points, carved cells etc, for diagnostics </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();
        /// <summary> the triangulation of the last Compute() (for tests) </summary>
        internal Delaunay3 Triangulation { get { return dt; } }

        public MeshAlphaWrap(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public MeshAlphaWrap(IEnumerable<Vector3d> points)
        {
            Points = new List<Vector3d>(points);
        }


        const byte Inside = 0, Outside = 1;
        // cells closer than ContactTolerance * Offset to the input count as intersecting it (margin for the
        // floating-point distances): the result keeps at least that distance
        const double ContactTolerance = 1e-6;

        AlphaWrapOracle oracle;
        Delaunay3 dt;
        byte[] label = new byte[0];
        double alpha, offset, contact;
        double diag;
        // distance of each triangulation vertex to the input (NaN = not computed)
        List<double> vertexDistance = new List<double>();
        GateQueue gates = new GateQueue();
        Stopwatch phaseTimer = new Stopwatch();

        void end_phase(string name)
        {
            PhaseMilliseconds[name] = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();
        }

        /// <summary> computes Result; true on Success (see Status; Result is also set for PointLimitReached) </summary>
        public bool Compute()
        {
            Result = null;
            Error = null;
            PhaseMilliseconds.Clear();
            Counts.Clear();
            phaseTimer.Restart();
            Status = setup();
            if (Status != WrapStatus.Success)
                return false;
            end_phase("setup");

            Status = refine_and_carve();
            end_phase("wrap");
            if (Status != WrapStatus.Success && Status != WrapStatus.PointLimitReached)
                return false;

            remove_pockets();
            if (MakeManifold && make_manifold() == false) {
                Status = WrapStatus.Failed;
                Error = "could not make the result manifold";
                return false;
            }
            end_phase("manifold");
            Result = extract();
            end_phase("extract");
            if (Result == null) {
                Status = WrapStatus.Failed;
                return false;
            }
            Counts["result_triangles"] = Result.TriangleCount;
            return Status == WrapStatus.Success;
        }


        WrapStatus setup()
        {
            var tris = new List<Vector3d>();
            var pts = new List<Vector3d>();
            if (Mesh != null) {
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    tris.Add(Mesh.GetVertex(t.a)); tris.Add(Mesh.GetVertex(t.b)); tris.Add(Mesh.GetVertex(t.c));
                }
                foreach (int vid in Mesh.VertexIndices())
                    if (Mesh.GetVtxEdgeCount(vid) == 0) pts.Add(Mesh.GetVertex(vid));
            }
            if (Points != null)
                pts.AddRange(Points);
            if (tris.Count == 0 && pts.Count == 0) {
                Error = "input has no triangles and no points";
                return WrapStatus.EmptyInput;
            }
            foreach (var list in new[] { tris, pts })
                foreach (Vector3d v in list)
                    if (double.IsNaN(v.x + v.y + v.z) || double.IsInfinity(v.x + v.y + v.z)) {
                        Error = "input has non-finite coordinates";
                        return WrapStatus.InvalidInput;
                    }
            oracle = new AlphaWrapOracle(tris, pts);
            Counts["input_triangles"] = oracle.Triangles;
            Counts["input_segments"] = oracle.Segments;
            Counts["input_points"] = oracle.Points;

            AxisAlignedBox3d bounds = oracle.Bounds;
            diag = bounds.DiagonalLength;
            alpha = Alpha > 0 ? Alpha : (RelativeAlpha > 0 ? diag / RelativeAlpha : 0);
            offset = Offset > 0 ? Offset : (RelativeOffset > 0 ? diag / RelativeOffset : 0);
            UsedAlpha = alpha; UsedOffset = offset;
            if (!(alpha > 0) || !(offset > 0) || double.IsInfinity(alpha) || double.IsInfinity(offset)) {
                Error = "alpha and offset must be positive (relative values need an input of nonzero size)";
                return WrapStatus.InvalidParameters;
            }
            contact = ContactTolerance * offset;

            // the box keeps the offset surface strictly inside, with room for alpha balls around it
            AxisAlignedBox3d box = bounds;
            box.Expand(2 * (offset + alpha) + 1e-3 * Math.Max(diag, offset));
            dt = new Delaunay3(box);
            vertexDistance.Clear();
            for (int v = 0; v < dt.VertexCount; ++v)
                vertexDistance.Add(double.NaN);
            ensure_labels();
            foreach (int c in dt.CellIndices())
                label[c] = Inside;
            gates.Clear();
            foreach (int c in dt.CellIndices())
                for (int i = 0; i < 4; ++i)
                    if (dt.Neighbour(c, i) < 0) push_gate(c, i);
            return WrapStatus.Success;
        }

        // per cell (valid while the cell's stamp matches): circumcenter, and whether it touches the input
        Vector3d[] centerCache = new Vector3d[0];
        int[] centerStamp = new int[0];
        sbyte[] touchCache = new sbyte[0];
        int[] touchStamp = new int[0];

        void ensure_labels()
        {
            if (label.Length < dt.MaxCellID) {
                int n = Math.Max(dt.MaxCellID, 2 * label.Length);
                Array.Resize(ref label, n);
                Array.Resize(ref centerCache, n); Array.Resize(ref centerStamp, n);
                Array.Resize(ref touchCache, n); Array.Resize(ref touchStamp, n);
            }
        }

        Vector3d circumcenter(int c)
        {
            int st = dt.CellStamp(c);
            if (centerStamp[c] != st) {
                centerCache[c] = dt.Circumcenter(c);
                centerStamp[c] = st;
            }
            return centerCache[c];
        }

        byte label_of(int c) { return c < 0 ? Outside : label[c]; }


        // ---- refinement and carving ----

        long ticksIntersection, ticksTouch, ticksInsert;

        WrapStatus refine_and_carve()
        {
            ticksIntersection = ticksTouch = ticksInsert = 0;
            var removed = new List<int>();
            var added = new List<int>();
            long rule1 = 0, rule2 = 0, carved = 0, rejected = 0, popped = 0;
            int steiner = 0;
            WrapStatus status = WrapStatus.Success;
            while (gates.Count > 0) {
                if ((++popped & 1023) == 0 && CancelF())
                    return WrapStatus.Cancelled;
                gates.Pop(out int c, out int i, out int stampC);
                if (!dt.IsCell(c) || dt.CellStamp(c) != stampC || label[c] != Inside)
                    continue;
                int n = dt.Neighbour(c, i);
                if (label_of(n) != Outside)
                    continue;
                if (!(facet_radius(c, i) > alpha))
                    continue;
                Vector3d from, to;
                dual_edge(c, i, out from, out to);

                Vector3d steinerPoint;
                bool insert = false;
                int which = 0;
                long t0 = Stopwatch.GetTimestamp();
                bool crossing = rule_intersection(from, to, out steinerPoint);
                long t1 = Stopwatch.GetTimestamp();
                ticksIntersection += t1 - t0;
                bool touching = !crossing && cell_touches_input(c);
                ticksTouch += Stopwatch.GetTimestamp() - t1;
                if (crossing) {
                    insert = true; which = 1;
                } else if (touching) {
                    insert = rule_projection(c, i, to, out steinerPoint);
                    which = 2;
                    if (!insert) {
                        rejected++;
                        continue;       // keep the cell: the facet stays on the result
                    }
                }
                if (insert) {
                    if (steiner >= MaxSteinerPoints) {
                        status = WrapStatus.PointLimitReached;
                        break;
                    }
                    removed.Clear(); added.Clear();
                    // (no separation needed for correctness; tiny separations would only make slivers)
                    long t2 = Stopwatch.GetTimestamp();
                    var res = dt.Insert(steinerPoint, c, 1e-9 * alpha, out int vid, removed, added);
                    ticksInsert += Stopwatch.GetTimestamp() - t2;
                    if (res != Delaunay3.InsertResult.Inserted) {
                        rejected++;
                        continue;
                    }
                    steiner++;
                    if (which == 1) rule1++; else rule2++;
                    vertexDistance.Add(offset);
                    ensure_labels();
                    foreach (int nc in added)
                        label[nc] = Inside;
                    foreach (int nc in added)
                        for (int j = 0; j < 4; ++j)
                            if (label_of(dt.Neighbour(nc, j)) == Outside) push_gate(nc, j);
                    continue;
                }
                // carve
                label[c] = Outside;
                carved++;
                for (int j = 0; j < 4; ++j) {
                    int m = dt.Neighbour(c, j);
                    if (m >= 0 && label[m] == Inside)
                        push_gate(m, dt.FacetTo(m, c));
                }
            }
            Counts["steiner_points"] = steiner;
            Counts["steiner_intersection"] = rule1;
            Counts["steiner_projection"] = rule2;
            Counts["rejected_points"] = rejected;
            Counts["carved_cells"] = carved;
            Counts["gates_popped"] = popped;
            Counts["cells"] = dt.CellCount;
            PhaseMilliseconds["wrap.intersection"] = ticksIntersection * 1000 / Stopwatch.Frequency;
            PhaseMilliseconds["wrap.touch"] = ticksTouch * 1000 / Stopwatch.Frequency;
            PhaseMilliseconds["wrap.insert"] = ticksInsert * 1000 / Stopwatch.Frequency;
            return status;
        }

        void push_gate(int c, int i)
        {
            double r = facet_radius(c, i);
            if (r > alpha)
                gates.Push(r, c, i, dt.CellStamp(c));
        }

        /// <summary>
        /// Circumradius of facet i of cell c: an alpha ball can pass through the facet if it is larger than alpha
        /// </summary>
        double facet_radius(int c, int i)
        {
            Index3i f = dt.FacetVertices(c, i);
            Vector3d a = dt.GetVertex(f.a), b = dt.GetVertex(f.b), d = dt.GetVertex(f.c);
            Vector3d u = b - a, v = d - a, w = u.Cross(v);
            Vector3d x = (u.LengthSquared * v.Cross(w) + v.LengthSquared * w.Cross(u)) / (2 * w.LengthSquared);
            return x.Length;
        }

        /// <summary>
        /// The Voronoi edge dual to facet i of the inside cell c, from the circumcenter of the outside cell to
        /// that of c (centers of the empty balls through the facet; a ray for a facet on the box)
        /// </summary>
        void dual_edge(int c, int i, out Vector3d from, out Vector3d to)
        {
            Index3i f = dt.FacetVertices(c, i);
            Vector3d a = dt.GetVertex(f.a), b = dt.GetVertex(f.b), d = dt.GetVertex(f.c);
            Vector3d u = b - a, v = d - a, w = u.Cross(v);
            double w2 = w.LengthSquared;
            Vector3d nrm = w / Math.Sqrt(w2);       // outwards of c
            Vector3d fc = a + (u.LengthSquared * v.Cross(w) + v.LengthSquared * w.Cross(u)) / (2 * w2);
            to = clamp_center(circumcenter(c), fc, nrm, -1);
            int n = dt.Neighbour(c, i);
            if (n < 0)
                from = fc + (Math.Max((to - fc).Dot(nrm), 0) + 4 * dt.Box.DiagonalLength) * nrm;
            else
                from = clamp_center(circumcenter(n), fc, nrm, 1);
        }

        // circumcenters of flat cells can be far away or not finite: keep them on the facet's normal line,
        // within a few box diagonals (beyond that, they are outside the box either way)
        Vector3d clamp_center(Vector3d cc, Vector3d fc, Vector3d nrm, int side)
        {
            double lim = 4 * dt.Box.DiagonalLength;
            if (double.IsNaN(cc.x + cc.y + cc.z) || double.IsInfinity(cc.x + cc.y + cc.z))
                return fc + side * lim * nrm;
            double t = (cc - fc).Dot(nrm);
            if (Math.Abs(t) > lim)
                return fc + Math.Sign(t) * lim * nrm;
            return cc;
        }

        // Rule 1: first crossing of the Voronoi edge from -> to with the offset surface
        bool rule_intersection(Vector3d from, Vector3d to, out Vector3d p)
        {
            p = Vector3d.Zero;
            double t = oracle.FirstEntry(from, to, offset);
            if (t < 0)
                return false;
            if (t > 0) {
                p = from + t * (to - from);
                return true;
            }
            // from is already within the offset: the edge crosses the surface if it leaves (bisection)
            double dto = oracle.Distance(to) - offset;
            if (!(dto > 0))
                return false;
            double lo = 0, hi = 1;
            for (int k = 0; k < 60; ++k) {
                double mid = 0.5 * (lo + hi);
                if (oracle.Distance(from + mid * (to - from)) > offset) hi = mid; else lo = mid;
            }
            p = from + hi * (to - from);
            return true;
        }

        double vertex_distance(int v)
        {
            double d = vertexDistance[v];
            if (double.IsNaN(d)) {
                d = oracle.Distance(dt.GetVertex(v));
                vertexDistance[v] = d;
            }
            return d;
        }

        // does cell c intersect the input (come closer than the contact tolerance)?
        bool cell_touches_input(int c)
        {
            int st = dt.CellStamp(c);
            if (touchStamp[c] != st) {
                touchCache[c] = (sbyte)(touches_input(c) ? 1 : 0);
                touchStamp[c] = st;
            }
            return touchCache[c] == 1;
        }

        bool touches_input(int c)
        {
            Index4i t = dt.GetCell(c);
            for (int j = 0; j < 4; ++j)
                if (vertex_distance(t[j]) < contact) return true;
            Vector3d a = dt.GetVertex(t.a), b = dt.GetVertex(t.b), d = dt.GetVertex(t.c), e = dt.GetVertex(t.d);
            // the cell is within (longest edge at v) of each vertex v: far enough from the input if a vertex is
            // farther from it than that (distance is 1-Lipschitz)
            double ab = a.Distance(b), ad = a.Distance(d), ae = a.Distance(e), bd = b.Distance(d), be = b.Distance(e), de = d.Distance(e);
            if (vertex_distance(t.a) - Math.Max(ab, Math.Max(ad, ae)) > contact || vertex_distance(t.b) - Math.Max(ab, Math.Max(bd, be)) > contact
                || vertex_distance(t.c) - Math.Max(ad, Math.Max(bd, de)) > contact || vertex_distance(t.d) - Math.Max(ae, Math.Max(be, de)) > contact)
                return false;
            Vector3d g = 0.25 * (a + b + d + e);
            double rho = Math.Sqrt(Math.Max(Math.Max(g.DistanceSquared(a), g.DistanceSquared(b)), Math.Max(g.DistanceSquared(d), g.DistanceSquared(e))));
            double dg = oracle.Distance(g);
            if (dg < contact) return true;
            if (dg >= rho + contact) return false;
            return oracle.TetrahedronTouches(a, b, d, e, contact);
        }

        // Rule 2: projection of the circumcenter of c onto the offset surface
        bool rule_projection(int c, int i, Vector3d cc, out Vector3d p)
        {
            Vector3d q;
            double d2 = oracle.DistanceSquared(cc, out q);
            double dist = Math.Sqrt(d2);
            Vector3d dir;
            if (dist > 1e-12 * diag) {
                dir = (cc - q) / dist;
            } else {
                // circumcenter on the input: go towards the outside cell
                Index3i f = dt.FacetVertices(c, i);
                dir = (dt.GetVertex(f.b) - dt.GetVertex(f.a)).Cross(dt.GetVertex(f.c) - dt.GetVertex(f.a)).Normalized;
            }
            p = q + offset * dir;
            return double.IsNaN(p.x + p.y + p.z) == false;
        }


        // ---- post-processing ----

        // outside cells not connected to the box boundary (enclosed by an insertion) become inside
        int remove_pockets()
        {
            var reached = new bool[dt.MaxCellID];
            var stack = new Stack<int>();
            foreach (int c in dt.CellIndices()) {
                if (label[c] != Outside) continue;
                for (int i = 0; i < 4; ++i)
                    if (dt.Neighbour(c, i) < 0) { reached[c] = true; stack.Push(c); break; }
            }
            while (stack.Count > 0) {
                int c = stack.Pop();
                for (int i = 0; i < 4; ++i) {
                    int n = dt.Neighbour(c, i);
                    if (n >= 0 && !reached[n] && label[n] == Outside) { reached[n] = true; stack.Push(n); }
                }
            }
            int flipped = 0;
            foreach (int c in dt.CellIndices())
                if (label[c] == Outside && !reached[c]) { label[c] = Inside; flipped++; }
            Counts["pocket_cells"] = (Counts.TryGetValue("pocket_cells", out long pc) ? pc : 0) + flipped;
            return flipped;
        }

        // cells incident to each vertex (CSR)
        int[] starStart, starCells;
        void build_stars()
        {
            int nv = dt.VertexCount;
            starStart = new int[nv + 1];
            foreach (int c in dt.CellIndices())
                for (int j = 0; j < 4; ++j) starStart[dt.CellVertex(c, j) + 1]++;
            for (int v = 0; v < nv; ++v) starStart[v + 1] += starStart[v];
            starCells = new int[starStart[nv]];
            int[] fill = new int[nv];
            foreach (int c in dt.CellIndices())
                for (int j = 0; j < 4; ++j) {
                    int v = dt.CellVertex(c, j);
                    starCells[starStart[v] + fill[v]++] = c;
                }
        }

        /// <summary>
        /// Moves outside cells to the inside until every edge has at most two result facets and the result
        /// facets around every vertex form one disk: around a non-manifold edge or vertex, the outside cells are
        /// grouped into connected runs (through facets containing the edge / vertex); the run connected to the
        /// box boundary, otherwise the largest one, stays outside. If the inside cells around a vertex are not
        /// connected, all its outside cells move inside. Only inside is added, so the input stays enclosed;
        /// every pass moves at least one cell, so this terminates.
        /// </summary>
        bool make_manifold()
        {
            long flips = 0;
            build_stars();      // (the triangulation does not change here, only labels)
            var edgeCount = new Dictionary<long, int>();
            var surfaceVertices = new HashSet<int>();
            for (int pass = 0; pass < 1000; ++pass) {
                int changed = 0;
                // edges
                edgeCount.Clear();
                surfaceVertices.Clear();
                foreach (int c in dt.CellIndices()) {
                    if (label[c] != Inside) continue;
                    for (int i = 0; i < 4; ++i) {
                        if (label_of(dt.Neighbour(c, i)) != Outside) continue;
                        Index3i f = dt.FacetVertices(c, i);
                        for (int j = 0; j < 3; ++j) {
                            surfaceVertices.Add(f[j]);
                            long key = edge_key(f[j], f[(j + 1) % 3]);
                            edgeCount.TryGetValue(key, out int cnt);
                            edgeCount[key] = cnt + 1;
                        }
                    }
                }
                foreach (var kv in edgeCount) {
                    if (kv.Value <= 2) continue;
                    int u = (int)(kv.Key >> 32), w = (int)(kv.Key & 0xFFFFFFFF);
                    changed += fix_star(u, w);
                }
                if (changed == 0) {
                    // vertices (after the edges are manifold)
                    foreach (int v in surfaceVertices)
                        changed += fix_star(v, -1);
                }
                if (changed > 0) {
                    flips += changed;
                    remove_pockets();
                    continue;
                }
                Counts["manifold_flips"] = flips;
                Counts["manifold_passes"] = pass + 1;
                return true;
            }
            return false;
        }

        static long edge_key(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        // star of vertex v (w < 0) or of edge v-w: cells containing it, connected through facets containing it
        readonly List<int> local = new List<int>();
        readonly Dictionary<int, int> localIndex = new Dictionary<int, int>();
        readonly List<int> comp = new List<int>();
        readonly List<byte> compLabel = new List<byte>();
        readonly List<bool> compExterior = new List<bool>();
        readonly List<double> compVolume = new List<double>();
        readonly Stack<int> compStack = new Stack<int>();

        int fix_star(int v, int w)
        {
            local.Clear(); localIndex.Clear();
            for (int k = starStart[v]; k < starStart[v + 1]; ++k) {
                int c = starCells[k];
                if (w >= 0 && dt.VertexIndexIn(c, w) < 0) continue;
                localIndex[c] = local.Count;
                local.Add(c);
            }
            int n = local.Count;
            if (n == 0) return 0;
            // components of equal labels; component -> touches the box boundary (through a facet in the star)
            comp.Clear();
            for (int k = 0; k < n; ++k) comp.Add(-1);
            compLabel.Clear(); compExterior.Clear(); compVolume.Clear();
            var stack = compStack;
            for (int k = 0; k < n; ++k) {
                if (comp[k] >= 0) continue;
                int id = compLabel.Count;
                byte lab = label[local[k]];
                compLabel.Add(lab); compExterior.Add(false); compVolume.Add(0);
                comp[k] = id;
                stack.Push(k);
                while (stack.Count > 0) {
                    int kk = stack.Pop();
                    int c = local[kk];
                    compVolume[id] += dt.CellVolume(c);
                    for (int i = 0; i < 4; ++i) {
                        int cvx = dt.CellVertex(c, i);
                        if (cvx == v || cvx == w) continue;     // facet i does not contain the vertex / edge
                        int m = dt.Neighbour(c, i);
                        if (m < 0) { compExterior[id] = true; continue; }
                        if (localIndex.TryGetValue(m, out int mk) && comp[mk] < 0 && label[m] == lab) {
                            comp[mk] = id;
                            stack.Push(mk);
                        }
                    }
                }
            }
            // the box boundary is outside: outside components touching it are one component (with it), and
            // an inside component touching it has the box boundary as outside neighbour
            int nIn = 0, nOut = 0;
            bool exterior = false;
            for (int k = 0; k < compLabel.Count; ++k) {
                if (compExterior[k]) exterior = true;
                if (compLabel[k] == Inside) nIn++;
                else if (!compExterior[k]) nOut++;
            }
            if (exterior) nOut++;
            if (nIn == 0 || nOut == 0 || (nIn == 1 && nOut == 1))
                return 0;       // not on the result, or manifold
            int flipped = 0;
            if (nOut > 1) {
                // keep the outside connected to the box boundary, otherwise the largest outside component
                int keep = -1;
                for (int k = 0; k < compLabel.Count; ++k) {
                    if (compLabel[k] != Outside || exterior) continue;
                    if (keep < 0 || compVolume[k] > compVolume[keep]) keep = k;
                }
                for (int k = 0; k < n; ++k) {
                    int ck = comp[k];
                    if (compLabel[ck] != Outside) continue;
                    bool stays = exterior ? compExterior[ck] : ck == keep;
                    if (!stays) { label[local[k]] = Inside; flipped++; }
                }
                return flipped;
            }
            // several inside components, one outside: fill the outside
            for (int k = 0; k < n; ++k)
                if (label[local[k]] == Outside) { label[local[k]] = Inside; flipped++; }
            return flipped;
        }

        DMesh3 extract()
        {
            DMesh3 mesh = new DMesh3();
            var map = new Dictionary<int, int>();
            int failed = 0;
            foreach (int c in dt.CellIndices()) {
                if (label[c] != Inside) continue;
                for (int i = 0; i < 4; ++i) {
                    if (label_of(dt.Neighbour(c, i)) != Outside) continue;
                    Index3i f = dt.FacetVertices(c, i);
                    Index3i t = Index3i.Zero;
                    for (int j = 0; j < 3; ++j) {
                        if (!map.TryGetValue(f[j], out int vid)) {
                            vid = mesh.AppendVertex(dt.GetVertex(f[j]));
                            map[f[j]] = vid;
                        }
                        t[j] = vid;
                    }
                    if (mesh.AppendTriangle(t) < 0) failed++;
                }
            }
            if (failed > 0 && MakeManifold) {
                Error = failed + " result triangles could not be added (non-manifold)";
                return null;
            }
            return mesh;
        }


        /// <summary> max-heap of gates (facet i of inside cell c, with the cell's stamp), by facet radius </summary>
        class GateQueue
        {
            double[] key = new double[256];
            int[] cell = new int[256], facet = new int[256], stamp = new int[256];
            int count = 0;
            public int Count { get { return count; } }
            public void Clear() { count = 0; }

            public void Push(double k, int c, int f, int s)
            {
                if (count == key.Length) {
                    int n = 2 * count;
                    Array.Resize(ref key, n); Array.Resize(ref cell, n); Array.Resize(ref facet, n); Array.Resize(ref stamp, n);
                }
                int i = count++;
                while (i > 0) {
                    int p = (i - 1) / 2;
                    if (key[p] >= k) break;
                    move(p, i);
                    i = p;
                }
                key[i] = k; cell[i] = c; facet[i] = f; stamp[i] = s;
            }

            public void Pop(out int c, out int f, out int s)
            {
                c = cell[0]; f = facet[0]; s = stamp[0];
                count--;
                if (count == 0) return;
                double k = key[count];
                int lc = cell[count], lf = facet[count], ls = stamp[count];
                int i = 0;
                while (true) {
                    int l = 2 * i + 1;
                    if (l >= count) break;
                    int r = l + 1;
                    int m = (r < count && key[r] > key[l]) ? r : l;
                    if (key[m] <= k) break;
                    move(m, i);
                    i = m;
                }
                key[i] = k; cell[i] = lc; facet[i] = lf; stamp[i] = ls;
            }

            void move(int from, int to)
            {
                key[to] = key[from]; cell[to] = cell[from]; facet[to] = facet[from]; stamp[to] = stamp[from];
            }
        }
    }
}
