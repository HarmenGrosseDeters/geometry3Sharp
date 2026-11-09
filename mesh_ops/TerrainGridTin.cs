// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;

namespace gs
{
    /// <summary> where the heights of a <see cref="TerrainBreakline"/> in the TIN come from (decision D4 of docs/design/TERRAIN_DGM_TO_TIN.md) </summary>
    public enum TerrainBreaklineHeights
    {
        /// <summary> the grid surface (the grid is the truth); the surface error bound holds along the breakline; a z of the input is ignored </summary>
        FromGrid = 0,
        /// <summary>
        /// the z of the input points (a survey line), linearly between them; the TIN follows the line exactly, so the vertical distance of the
        /// line to the grid surface cannot be reduced by refinement: it is measured (<see cref="TerrainGridTin.BreaklineError"/>) and must be within
        /// <see cref="TerrainGridTin.Epsilon"/> for the guarantee to hold, else the status is <see cref="TerrainGridTinStatus.BreaklineHeightDeviates"/>
        /// </summary>
        FromInput = 1
    }

    /// <summary>
    /// A breakline for <see cref="TerrainGridTin"/>: a polyline in plan (x, y in the coordinates of the <see cref="HeightGrid"/>);
    /// the TIN contains it as a chain of edges. The heights come from the grid (the grid is the truth, <see cref="TerrainBreaklineHeights.FromGrid"/>,
    /// the z of a point is ignored) or, for a survey line, from the input (<see cref="TerrainBreaklineHeights.FromInput"/>).
    /// Where two <c>FromInput</c> breaklines cross or touch with different heights, the one given first wins at the common vertex.
    /// </summary>
    public sealed class TerrainBreakline
    {
        public IReadOnlyList<Vector2d> Points { get; }
        /// <summary> the heights of the points (same length as <see cref="Points"/>) for <see cref="TerrainBreaklineHeights.FromInput"/>, else null </summary>
        public IReadOnlyList<double> Z { get; }
        public TerrainBreaklineHeights Heights { get { return Z != null ? TerrainBreaklineHeights.FromInput : TerrainBreaklineHeights.FromGrid; } }
        public TerrainBreakline(IEnumerable<Vector2d> points) { Points = points.ToList(); }
        /// <summary> a breakline of 3D points; with <see cref="TerrainBreaklineHeights.FromGrid"/> the z is ignored </summary>
        public TerrainBreakline(IEnumerable<Vector3d> points, TerrainBreaklineHeights heights = TerrainBreaklineHeights.FromGrid)
        {
            List<Vector3d> list = points.ToList();
            Points = list.Select(p => new Vector2d(p.x, p.y)).ToList();
            if (heights == TerrainBreaklineHeights.FromInput) Z = list.Select(p => p.z).ToList();
        }
    }

    public enum TerrainGridTinStatus
    {
        Ok = 0,
        /// <summary> no grid, a grid with fewer than 2 x 2 nodes, a non-positive or non-finite Epsilon, a non-finite breakline point </summary>
        InvalidInput,
        /// <summary> no cell with data at all </summary>
        NoData,
        /// <summary> the constrained mesher failed (see Message) </summary>
        MesherFailed,
        /// <summary> MaxRounds reached or no progress; Mesh is the last valid TIN and SurfaceError says what it achieves </summary>
        NotConverged,
        Cancelled,
        /// <summary>
        /// the refinement converged away from the breaklines, but a <see cref="TerrainBreaklineHeights.FromInput"/> breakline is further than Epsilon from
        /// the grid surface somewhere (<see cref="TerrainGridTin.BreaklineError"/>); Result is that TIN (it follows the survey line), SurfaceError says what it achieves
        /// </summary>
        BreaklineHeightDeviates
    }

    /// <summary>
    /// TIN of a height grid (DGM) with a guaranteed vertical error and breaklines (docs/design/TERRAIN_DGM_TO_TIN.md, method B2).
    /// The guarantee is the **surface error**: the exact maximum of |TIN - S| over the whole TIN, where S is the grid TIN
    /// (<see cref="HeightGrid.Height"/>: fixed diagonal, no-data cells have no surface), at most <see cref="Epsilon"/>.
    /// The TIN is a height field (counter-clockwise from above), has an edge chain for every breakline, and covers exactly the cells with data.
    /// Breaklines take their heights from the grid or, for survey lines, from the input (<see cref="TerrainBreaklineHeights"/>): a survey line is
    /// followed exactly, its distance to the grid surface is <see cref="BreaklineError"/> and must be within <see cref="Epsilon"/> (else <see cref="TerrainGridTinStatus.BreaklineHeightDeviates"/>).
    /// <para>Method: a constrained Delaunay triangulation (<see cref="QualityMesh2"/>, no refinement) of the selected nodes and the breaklines in grid
    /// index coordinates (exact and independent of origin and cell size), then rounds of an exact error scan of the real triangulation: the
    /// overlay vertices of every triangle with S (grid nodes inside, its corners, the crossings of its edges with the lines x = i, y = j and the
    /// diagonals) are the only places where |TIN - S| can be maximal. A violation selects a node of the cell, splits a breakline or boundary
    /// segment at the crossing, or locks a crease of S (cell diagonal or side) as constraint, until the error is within Epsilon. Locking is what
    /// makes the TIN equal to S where needed, whatever diagonals the triangulation would choose at the cocircular grid points.</para>
    /// <para>The error is computed in double arithmetic in index space; <see cref="SurfaceError"/> includes a rounding margin, so it is an upper bound
    /// of the exact value. Results do not depend on the number of threads.</para>
    /// </summary>
    public class TerrainGridTin
    {
        public HeightGrid Grid;
        /// <summary> the guaranteed vertical error (the unit of the heights), positive </summary>
        public double Epsilon = 0.1;
        public List<TerrainBreakline> Breaklines = new List<TerrainBreakline>();
        public int MaxRounds = 200;
        public Func<bool> CancelF = () => false;

        // outputs
        public TerrainGridTinStatus Status { get; private set; }
        public string Message { get; private set; } = "";
        /// <summary> the TIN in the coordinates of the grid (x = X0 + i CellSize, ...), heights of the grid surface </summary>
        public DMesh3 Result { get; private set; }
        /// <summary> upper bound of max |TIN - S| over the whole TIN </summary>
        public double SurfaceError { get; private set; }
        /// <summary> max |TIN - z| over the grid nodes inside the TIN (never above SurfaceError) </summary>
        public double NodeError { get; private set; }
        public int Rounds { get; private set; }
        /// <summary>
        /// max |TIN - S| over the edges of <see cref="TerrainBreaklineHeights.FromInput"/> breaklines and their vertices (0 if there are none): the distance of the survey
        /// lines to the grid surface, which refinement cannot reduce. The guarantee <see cref="SurfaceError"/> &lt;= <see cref="Epsilon"/> holds with status Ok only
        /// when it is within Epsilon (an upper bound, with the rounding margin).
        /// </summary>
        public double BreaklineError { get; private set; }
        /// <summary> the edges of Result (pairs of vertex ids) that belong to breaklines </summary>
        public IReadOnlyList<Index2i> BreaklineEdges { get; private set; } = new Index2i[0];
        /// <summary> creases of the grid surface locked as constraints (cell sides and diagonals), the cost of the surface guarantee </summary>
        public int LockedCreases { get; private set; }
        /// <summary> breakline segments dropped because an end point or the middle is outside the cells with data </summary>
        public int DroppedBreaklineSegments { get; private set; }
        /// <summary> number of crease requests deferred to the next round because a segment next to the crease was split in the same round (diagnostics, tests) </summary>
        internal int CreaseConflicts { get; private set; }

        sealed class Req { public int Kind; public int Node = -1; public int Crease = -1; public int Seg = -1; public double X, Y, Err, Px, Py; }
        // Kind: 0 nothing to do, 1 select a node, 2 split a segment at (X, Y), 3 lock a crease

        const int KindBreakline = 0, KindBoundary = 1, KindCrease = 2;

        /// <summary> Computes the TIN. Returns true on Ok. </summary>
        public bool Compute()
        {
            Result = null; BreaklineEdges = new Index2i[0]; Rounds = 0; SurfaceError = double.NaN; NodeError = double.NaN; LockedCreases = 0; DroppedBreaklineSegments = 0; CreaseConflicts = 0; BreaklineError = 0;
            Message = "";
            HeightGrid g = Grid;
            if (g == null || g.Z == null || g.NX < 2 || g.NY < 2 || g.Z.Length != (long)g.NX * g.NY || !(g.CellSize > 0))
                return Fail(TerrainGridTinStatus.InvalidInput, "the grid needs at least 2 x 2 nodes and a positive cell size");
            if (!(Epsilon > 0) || double.IsInfinity(Epsilon))
                return Fail(TerrainGridTinStatus.InvalidInput, "Epsilon must be positive and finite");
            if (MaxRounds < 1)
                return Fail(TerrainGridTinStatus.InvalidInput, "MaxRounds must be at least 1");
            if (!(Math.Abs(g.X0) < double.PositiveInfinity) || !(Math.Abs(g.Y0) < double.PositiveInfinity))
                return Fail(TerrainGridTinStatus.InvalidInput, "the grid origin is not finite");
            int nx = g.NX, ny = g.NY;
            if ((long)nx * ny > (1L << 26))
                return Fail(TerrainGridTinStatus.InvalidInput, "the grid is too large (more than 2^26 nodes)");
            foreach (TerrainBreakline bl in Breaklines ?? new List<TerrainBreakline>())
                if (bl == null || bl.Points.Any(p => double.IsNaN(p.x) || double.IsNaN(p.y) || double.IsInfinity(p.x) || double.IsInfinity(p.y))
                    || (bl.Z != null && (bl.Z.Count != bl.Points.Count || bl.Z.Any(z => double.IsNaN(z) || double.IsInfinity(z)))))
                    return Fail(TerrainGridTinStatus.InvalidInput, "a breakline is null or has a non-finite point or height");

            // cells with data
            var cellOk = new bool[(nx - 1) * (ny - 1)];
            int okCount = 0;
            for (int cj = 0; cj < ny - 1; ++cj)
                for (int ci = 0; ci < nx - 1; ++ci)
                    if (HeightGrid.IsData(g[ci, cj]) && HeightGrid.IsData(g[ci + 1, cj]) && HeightGrid.IsData(g[ci, cj + 1]) && HeightGrid.IsData(g[ci + 1, cj + 1])) {
                        cellOk[ci + (nx - 1) * cj] = true; ++okCount;
                    }
            if (okCount == 0) return Fail(TerrainGridTinStatus.NoData, "no cell has data at all");
            bool Ok(int ci, int cj) { return ci >= 0 && cj >= 0 && ci < nx - 1 && cj < ny - 1 && cellOk[ci + (nx - 1) * cj]; }

            var pts = new List<Vector2d>();
            var ptsZ = new List<double>();            // survey height of a point of a FromInput breakline, else NaN (the grid surface)
            var nodeId = new Dictionary<int, int>();
            int AddNode(int i, int j) { int k = i + nx * j; int id; if (nodeId.TryGetValue(k, out id)) return id; id = pts.Count; nodeId[k] = id; pts.Add(new Vector2d(i, j)); ptsZ.Add(double.NaN); return id; }
            var segs = new List<(int a, int b)>();        // constraints other than plain creases; marker = index + 1
            var segKind = new List<int>();
            void AddSeg(int a, int b, int kind) { segs.Add((a, b)); segKind.Add(kind); }

            // boundary of the cells with data: maximal straight runs along grid lines
            for (int j = 0; j <= ny - 1; ++j) {
                int run = -1;
                for (int i = 0; i <= nx - 1; ++i) {
                    bool edge = i < nx - 1 && (Ok(i, j) != Ok(i, j - 1));
                    if (edge && run < 0) run = i;
                    if (!edge && run >= 0) { AddSeg(AddNode(run, j), AddNode(i, j), KindBoundary); run = -1; }
                }
            }
            for (int i = 0; i <= nx - 1; ++i) {
                int run = -1;
                for (int j = 0; j <= ny - 1; ++j) {
                    bool edge = j < ny - 1 && (Ok(i, j) != Ok(i - 1, j));
                    if (edge && run < 0) run = j;
                    if (!edge && run >= 0) { AddSeg(AddNode(i, run), AddNode(i, j), KindBoundary); run = -1; }
                }
            }
            // breaklines in index coordinates; segments with an end point or the middle outside the cells with data are dropped
            foreach (TerrainBreakline bl in Breaklines ?? new List<TerrainBreakline>()) {
                int prev = -1; Vector2d prevP = default(Vector2d); bool hasPrev = false;
                for (int bi = 0; bi < bl.Points.Count; ++bi) {
                    Vector2d w = bl.Points[bi];
                    double bz = bl.Z != null ? bl.Z[bi] : double.NaN;
                    Vector2d p = new Vector2d((w.x - g.X0) / g.CellSize, (w.y - g.Y0) / g.CellSize);
                    bool inside = InCell(p, nx, ny, Ok);
                    bool joined = false;
                    if (inside && !(prev >= 0 && p == prevP)) {
                        int id = pts.Count;
                        pts.Add(p); ptsZ.Add(bz);
                        if (prev >= 0 && SegmentInData(prevP, p, nx, ny, Ok)) { AddSeg(prev, id, KindBreakline); joined = true; }
                        if (prev >= 0 && !joined) ++DroppedBreaklineSegments;
                        else if (prev < 0 && hasPrev) ++DroppedBreaklineSegments;
                        prev = id; prevP = p;
                    } else if (!inside) {
                        if (hasPrev) ++DroppedBreaklineSegments;
                        prev = -1;
                    }
                    hasPrev = true;
                }
            }

            var lockedCreases = new HashSet<int>();      // plain creases (constraint segment between two nodes)
            var chainCreases = new HashSet<int>();       // creases crossing breaklines: a chain of pieces in segs
            var sel = new bool[nx * ny];
            double zmax = 0;
            foreach (double z in g.Z) if (HeightGrid.IsData(z)) zmax = Math.Max(zmax, Math.Abs(z));
            double margin = 64 * 2.220446049250313e-16 * (1 + zmax) * Math.Max(nx, ny);      // rounding of the scan in index space
            margin += WorldRoundingMargin(g);
            if (Epsilon <= 2 * margin)
                return Fail(TerrainGridTinStatus.InvalidInput, "Epsilon " + Epsilon + " is below the rounding margin of the computation (" + 2 * margin + ")");
            double act = Epsilon - margin;               // a violation is an error above this
            List<int> keptTris = null; ConstrainedMesh2 lastMesh = null; double lastSurf = double.NaN, lastNode = double.NaN;
            List<int> lastKinds = null; double[] lastVz = null; double lastDev = 0; bool converged = false;
            bool hasInput = Breaklines != null && Breaklines.Any(b => b.Z != null);

            // a point on a segment: a lattice node, or a new point; on a FromInput breakline segment always a new point with the interpolated survey height
            int AddPoint(double x, double y, int segA = -1, int segB = -1)
            {
                double z = segA >= 0 ? SplitZ(pts, ptsZ, segA, segB, new Vector2d(x, y)) : double.NaN;
                if (double.IsNaN(z) && x == Math.Floor(x) && y == Math.Floor(y) && x >= 0 && y >= 0 && x <= nx - 1 && y <= ny - 1) return AddNode((int)x, (int)y);
                pts.Add(new Vector2d(x, y)); ptsZ.Add(z); return pts.Count - 1;
            }

            for (int round = 0; round < MaxRounds; ++round) {
                if (CancelF()) return Fail(TerrainGridTinStatus.Cancelled, "cancelled");
                var input = new ConstrainedMesh2Input();
                input.Points.AddRange(pts);
                for (int s = 0; s < segs.Count; ++s) input.Segments.Add(new Mesh2Segment(segs[s].a, segs[s].b, s + 1, true));
                foreach (int crease in lockedCreases) {
                    var e = CreaseEnds(crease, nx);
                    input.Segments.Add(new Mesh2Segment(nodeId[e.a.x + nx * e.a.y], nodeId[e.b.x + nx * e.b.y], 0, true));
                }
                ConstrainedTriangulation2Result r = new QualityMesh2 { MinAngle = 0, ResolveCrossings = true }.Compute(input);
                if (r.Mesh == null) return Fail(TerrainGridTinStatus.MesherFailed, r.Status + " " + r.FailureDetail);
                ConstrainedMesh2 m = r.Mesh;
                var edgeSeg = new Dictionary<long, int>();
                foreach (Mesh2Subsegment ss in m.Subsegments)
                    if (ss.Markers != null && ss.Markers.Length > 0) edgeSeg[Key(ss.A, ss.B)] = ss.Markers[0] - 1;      // (overlapping sources: any one is split)
                    else if (ss.Marker > 0) edgeSeg[Key(ss.A, ss.B)] = ss.Marker - 1;
                Array.Clear(sel, 0, sel.Length);
                foreach (var kv in nodeId) sel[kv.Key] = true;
                double[] vz = null; HashSet<long> fixedEdges = null;
                if (hasInput) SurveyHeights(m, pts, ptsZ, segs, segKind, out vz, out fixedEdges);

                // the triangles over cells with data (the boundary of the data is made of constraint edges: a triangle is on one side)
                var kept = new List<int>();
                double area = 0;
                for (int t = 0; t < m.Triangles.Count; ++t) {
                    Index3i tr = m.Triangles[t];
                    Vector2d a = m.Vertices[tr.a], b = m.Vertices[tr.b], c = m.Vertices[tr.c];
                    double cx = (a.x + b.x + c.x) / 3, cy = (a.y + b.y + c.y) / 3;
                    int ci = Math.Min((int)Math.Floor(cx), nx - 2), cj = Math.Min((int)Math.Floor(cy), ny - 2);
                    if (!Ok(ci, cj)) continue;
                    kept.Add(t);
                    area += 0.5 * ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
                }
                if (Math.Abs(area - okCount) > 1e-9 * okCount + 1e-6)
                    return Fail(TerrainGridTinStatus.MesherFailed, "the triangles cover " + area + " cells, the data has " + okCount + " (a breakline or constraint was not handled)");

                int nt = kept.Count;
                var reqs = new List<Req>[nt];
                double nodeMax = 0, surfMax = 0, loopMax = 0, devMax = 0; object mx = new object();
                Parallel.For(0, nt, () => new double[4], (k, st, loc) => {
                    Index3i tr = m.Triangles[kept[k]];
                    double[] zs = null;
                    if (vz != null && (!double.IsNaN(vz[tr.a]) || !double.IsNaN(vz[tr.b]) || !double.IsNaN(vz[tr.c]))) {
                        zs = new double[3];
                        for (int c = 0; c < 3; ++c) {
                            int v = c == 0 ? tr.a : c == 1 ? tr.b : tr.c;
                            zs[c] = double.IsNaN(vz[v]) ? VertexZ(g, m.Vertices[v].x, m.Vertices[v].y) : vz[v];
                        }
                    }
                    reqs[k] = ScanTriangle(g, m.Vertices[tr.a], m.Vertices[tr.b], m.Vertices[tr.c], new[] { tr.a, tr.b, tr.c }, sel, edgeSeg, lockedCreases, chainCreases, act, out double wn, out double ws, out double wl, out double wd, zs, round < SingleRequestRounds ? 1 : MaxRequestsPerTriangle, fixedEdges);
                    loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws); loc[2] = Math.Max(loc[2], wl); loc[3] = Math.Max(loc[3], wd);
                    return loc;
                }, loc => { lock (mx) { nodeMax = Math.Max(nodeMax, loc[0]); surfMax = Math.Max(surfMax, loc[1]); loopMax = Math.Max(loopMax, loc[2]); devMax = Math.Max(devMax, loc[3]); } });
                Rounds = round + 1;
                lastMesh = m; keptTris = kept; lastSurf = surfMax + margin; lastNode = nodeMax + margin; lastVz = vz; lastDev = devMax + margin;
                lastKinds = segKind.ToList();
                if (loopMax <= act) { converged = true; break; }

                // act on the violations, in triangle order (deterministic)
                int added = 0;
                var splitDone = new HashSet<int>();                       // segments split by a crease chain in this round
                var splitReqs = new SortedDictionary<int, List<Vector2d>>();
                var blocked = new HashSet<int>();                         // segments split or to be split in this round
                var buckets = SegmentBuckets(pts, segs, segKind, nx, ny);
                for (int k = 0; k < nt; ++k) {
                    foreach (Req q in reqs[k]) {
                        if (q.Kind == 1 && q.Node >= 0) { if (!sel[q.Node]) { AddNode(q.Node % nx, q.Node / nx); sel[q.Node] = true; ++added; } }
                        else if (q.Kind == 2 && q.Seg >= 0 && q.Seg < segs.Count) {
                            if (splitDone.Contains(q.Seg)) continue;
                            if (!splitReqs.TryGetValue(q.Seg, out List<Vector2d> list)) { splitReqs[q.Seg] = list = new List<Vector2d>(); blocked.Add(q.Seg); }
                            list.Add(new Vector2d(q.X, q.Y));
                        } else if (q.Kind == 3 && !lockedCreases.Contains(q.Crease) && !chainCreases.Contains(q.Crease)) {
                            var e = CreaseEnds(q.Crease, nx);
                            var xs = CreaseCrossings(pts, segs, buckets, nx, blocked, new Vector2d(e.a.x, e.a.y), new Vector2d(e.b.x, e.b.y), out bool conflict);
                            if (conflict) { ++CreaseConflicts; continue; }      // a segment near it is split in this round: retried in the next round
                            if (xs.Count == 0) { AddNode(e.a.x, e.a.y); AddNode(e.b.x, e.b.y); lockedCreases.Add(q.Crease); ++added; continue; }
                            int prev = AddNode(e.a.x, e.a.y), last = AddNode(e.b.x, e.b.y);
                            foreach (var x in xs.OrderBy(x => x.s)) {
                                int id;
                                if (x.seg >= 0) {
                                    splitDone.Add(x.seg); blocked.Add(x.seg);
                                    var (sa, sb) = segs[x.seg];
                                    id = AddPoint(x.pt.x, x.pt.y, segs[x.seg].a, segs[x.seg].b);
                                    int kind = segKind[x.seg];
                                    segs[x.seg] = (sa, id); AddSeg(id, sb, kind);
                                } else id = x.vertex;
                                AddSeg(prev, id, KindCrease); prev = id;
                            }
                            AddSeg(prev, last, KindCrease);
                            chainCreases.Add(q.Crease); ++added;
                        }
                    }
                }
                // the splits of segments, several per segment (all the violations on it), except those a crease chain has split
                foreach (var kv in splitReqs) {
                    if (splitDone.Contains(kv.Key)) continue;
                    var (sa, sb) = segs[kv.Key];
                    Vector2d A = pts[sa], B = pts[sb], d = B - A;
                    double len2 = d.LengthSquared;
                    var cuts = kv.Value.Select(pt => (t: (pt - A).Dot(d) / len2, pt)).Where(c => c.t > 1e-12 && c.t < 1 - 1e-12).OrderBy(c => c.t).ToList();
                    int kind = segKind[kv.Key], prevId = sa; double prevT = 0;
                    bool first = true;
                    foreach (var c in cuts) {
                        if (c.t - prevT < 1e-12 && !first) continue;
                        int id = AddPoint(c.pt.x, c.pt.y, sa, sb);
                        if (id == prevId || id == sb) continue;
                        if (first) { segs[kv.Key] = (prevId, id); first = false; } else AddSeg(prevId, id, kind);
                        prevId = id; prevT = c.t; ++added;
                    }
                    if (!first) AddSeg(prevId, sb, kind);
                }
                if (added == 0) break;       // no progress: reported below as NotConverged
            }

            // the result: the triangles over cells with data, the vertices they use
            var mesh = new DMesh3(MeshComponents.None);
            var vmap = new int[lastMesh.Vertices.Count];
            for (int v = 0; v < vmap.Length; ++v) vmap[v] = -1;
            int Map(int v)
            {
                if (vmap[v] < 0) {
                    Vector2d p = lastMesh.Vertices[v];
                    vmap[v] = mesh.AppendVertex(new Vector3d(g.X0 + p.x * g.CellSize, g.Y0 + p.y * g.CellSize, lastVz != null && !double.IsNaN(lastVz[v]) ? lastVz[v] : VertexZ(g, p.x, p.y)));
                }
                return vmap[v];
            }
            foreach (int t in keptTris) { Index3i tr = lastMesh.Triangles[t]; mesh.AppendTriangle(Map(tr.a), Map(tr.b), Map(tr.c)); }
            var edges = new List<Index2i>();
            foreach (Mesh2Subsegment ss in lastMesh.Subsegments) {
                int[] ms = ss.Markers != null && ss.Markers.Length > 0 ? ss.Markers : (ss.Marker > 0 ? new[] { ss.Marker } : new int[0]);
                if (vmap[ss.A] >= 0 && vmap[ss.B] >= 0 && ms.Any(mk => mk - 1 < lastKinds.Count && lastKinds[mk - 1] == KindBreakline)) edges.Add(new Index2i(vmap[ss.A], vmap[ss.B]));
            }
            Result = mesh; BreaklineEdges = edges;
            SurfaceError = lastSurf; NodeError = lastNode;
            LockedCreases = lockedCreases.Count + chainCreases.Count;
            BreaklineError = hasInput ? lastDev : 0;
            if (lastSurf > Epsilon && converged && hasInput && lastDev > Epsilon)
                return Fail(TerrainGridTinStatus.BreaklineHeightDeviates, "a breakline with heights from the input is up to " + lastDev + " from the grid surface, above Epsilon " + Epsilon, false);
            if (lastSurf > Epsilon)
                return Fail(TerrainGridTinStatus.NotConverged, "the surface error " + lastSurf + " is above Epsilon after " + Rounds + " rounds", false);
            Status = TerrainGridTinStatus.Ok;
            return true;
        }

        /// <summary>
        /// The effect of rounding the output vertices to world coordinates on |TIN - S|. The mesh is built in index space (exact); a vertex is written as
        /// x = X0 + p.x CellSize, which moves it by at most u (|X0| + 2 extent) per axis (u = 2^-53; the product and the sum are rounded once each), and the
        /// same for y. A point of a moved triangle has the same barycentric coordinates and so the same TIN height as before, and lies at most that far
        /// (hypot of the two axis bounds) from the point of the exact triangle, where S differs by at most its Lipschitz constant times the distance.
        /// S is piecewise linear and continuous on right isosceles triangles, so its gradient components are at most D / CellSize (D: the largest height
        /// difference of two neighbouring nodes with data) and the Lipschitz constant is at most sqrt(2) D / CellSize. A factor 2 covers the rounding of
        /// this computation itself.
        /// </summary>
        static double WorldRoundingMargin(HeightGrid g)
        {
            const double u = 1.1102230246251565e-16;
            double extX = (g.NX - 1) * g.CellSize, extY = (g.NY - 1) * g.CellSize;
            double dx = u * (Math.Abs(g.X0) + 2 * extX), dy = u * (Math.Abs(g.Y0) + 2 * extY);
            double delta = Math.Sqrt(dx * dx + dy * dy);
            double d = 0;
            int nx = g.NX, ny = g.NY;
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    double z = g[i, j];
                    if (!HeightGrid.IsData(z)) continue;
                    if (i + 1 < nx && HeightGrid.IsData(g[i + 1, j])) d = Math.Max(d, Math.Abs(g[i + 1, j] - z));
                    if (j + 1 < ny && HeightGrid.IsData(g[i, j + 1])) d = Math.Max(d, Math.Abs(g[i, j + 1] - z));
                }
            return 2 * Math.Sqrt(2) * d / g.CellSize * delta;
        }

        static IEnumerable<int> Marks(Mesh2Subsegment ss)
        {
            if (ss.Markers != null && ss.Markers.Length > 0) return ss.Markers;
            return ss.Marker > 0 ? new[] { ss.Marker } : new int[0];
        }

        /// <summary>
        /// The heights of the mesher's vertices that lie on FromInput breaklines (NaN elsewhere: the grid surface), and the edges that belong to such a
        /// breakline. A vertex at an input point (or a split point of the loop) takes its survey height; a vertex the mesher made at a crossing, or a node on the
        /// line, the height interpolated along the breakline segment with the lowest index.
        /// </summary>
        static void SurveyHeights(ConstrainedMesh2 m, List<Vector2d> pts, List<double> ptsZ, List<(int a, int b)> segs, List<int> segKind, out double[] vz, out HashSet<long> fixedEdges)
        {
            var posZ = new Dictionary<(double, double), double>();
            for (int i = 0; i < pts.Count; ++i)
                if (!double.IsNaN(ptsZ[i]) && !posZ.ContainsKey((pts[i].x, pts[i].y))) posZ[(pts[i].x, pts[i].y)] = ptsZ[i];
            vz = new double[m.Vertices.Count];
            for (int v = 0; v < vz.Length; ++v) vz[v] = posZ.TryGetValue((m.Vertices[v].x, m.Vertices[v].y), out double z) ? z : double.NaN;
            fixedEdges = new HashSet<long>();
            var best = new int[vz.Length];
            for (int v = 0; v < best.Length; ++v) best[v] = int.MaxValue;
            foreach (Mesh2Subsegment ss in m.Subsegments) {
                int low = int.MaxValue;
                foreach (int mk in Marks(ss)) {
                    int si = mk - 1;
                    if (si >= 0 && si < segs.Count && segKind[si] == KindBreakline && !double.IsNaN(ptsZ[segs[si].a]) && !double.IsNaN(ptsZ[segs[si].b])) low = Math.Min(low, si);
                }
                if (low == int.MaxValue) continue;
                fixedEdges.Add(Key(ss.A, ss.B));
                foreach (int v in new[] { ss.A, ss.B }) {
                    if (!double.IsNaN(vz[v]) && best[v] == int.MaxValue) continue;      // at an input point
                    if (low < best[v]) { best[v] = low; vz[v] = SplitZ(pts, ptsZ, segs[low].a, segs[low].b, m.Vertices[v]); }
                }
            }
        }

        /// <summary> the survey height at a point of the segment a-b if both its end points have one (a FromInput breakline), else NaN (the grid surface) </summary>
        static double SplitZ(List<Vector2d> pts, List<double> ptsZ, int a, int b, Vector2d p)
        {
            double za = ptsZ[a], zb = ptsZ[b];
            if (double.IsNaN(za) || double.IsNaN(zb)) return double.NaN;
            Vector2d d = pts[b] - pts[a];
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Min(1, Math.Max(0, (p - pts[a]).Dot(d) / len2)) : 0;
            return za + t * (zb - za);
        }

        bool Fail(TerrainGridTinStatus status, string message, bool clear = true)
        {
            Status = status; Message = message;
            if (clear) Result = null;
            return false;
        }

        static bool InCell(Vector2d p, int nx, int ny, Func<int, int, bool> ok)
        {
            if (!(p.x >= 0 && p.y >= 0 && p.x <= nx - 1 && p.y <= ny - 1)) return false;
            int ci = Math.Min((int)Math.Floor(p.x), nx - 2), cj = Math.Min((int)Math.Floor(p.y), ny - 2);
            if (ok(ci, cj)) return true;
            // on a grid line shared with a cell with data
            if (p.x == ci && ok(ci - 1, cj)) return true;
            if (p.y == cj && ok(ci, cj - 1)) return true;
            if (p.x == ci && p.y == cj && ok(ci - 1, cj - 1)) return true;
            return false;
        }

        /// <summary> true if the closed segment p-q lies in the cells with data (every piece between two grid line crossings is tested at its middle) </summary>
        static bool SegmentInData(Vector2d p, Vector2d q, int nx, int ny, Func<int, int, bool> ok)
        {
            double dx = q.x - p.x, dy = q.y - p.y;
            var ts = new List<double> { 0, 1 };
            if (dx != 0) for (double k = Math.Floor(Math.Min(p.x, q.x)) + 1; k < Math.Max(p.x, q.x); ++k) ts.Add((k - p.x) / dx);
            if (dy != 0) for (double k = Math.Floor(Math.Min(p.y, q.y)) + 1; k < Math.Max(p.y, q.y); ++k) ts.Add((k - p.y) / dy);
            ts.Sort();
            for (int k = 0; k + 1 < ts.Count; ++k) {
                if (!(ts[k + 1] > ts[k])) continue;
                double t = 0.5 * (ts[k] + ts[k + 1]);
                if (!InCell(new Vector2d(p.x + t * dx, p.y + t * dy), nx, ny, ok)) return false;
            }
            return true;
        }

        static long Key(int a, int b) { return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; }

        /// <summary> the surface of the grid at (x, y) in index coordinates, on a grid line along the line (so that cells without data next to it do not matter) </summary>
        static double VertexZ(HeightGrid g, double x, double y)
        {
            bool xl = x == Math.Floor(x), yl = y == Math.Floor(y);
            if (xl && yl) return g[(int)x, (int)y];
            return SurfaceZ(g, x, y, xl, yl);
        }

        static double SurfaceZ(HeightGrid g, double x, double y, bool xIsLine, bool yIsLine)
        {
            if (xIsLine) { int i = (int)x; int j = Math.Min((int)Math.Floor(y), g.NY - 2); double f = y - j; return g[i, j] + f * (g[i, j + 1] - g[i, j]); }
            if (yIsLine) { int j = (int)y; int i = Math.Min((int)Math.Floor(x), g.NX - 2); double f = x - i; return g[i, j] + f * (g[i + 1, j] - g[i, j]); }
            return IndexHeight(g, x, y);
        }

        /// <summary> HeightGrid.Height for a grid with CellSize 1 and origin 0 (index coordinates), without the scaling </summary>
        static double IndexHeight(HeightGrid g, double fx, double fy)
        {
            if (fx < 0 || fy < 0 || fx > g.NX - 1 || fy > g.NY - 1) return double.NaN;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
            if (i == g.NX - 1) i--;
            if (j == g.NY - 1) j--;
            fx -= i; fy -= j;
            double z00 = g[i, j], z10 = g[i + 1, j], z01 = g[i, j + 1], z11 = g[i + 1, j + 1];
            if (!HeightGrid.IsData(z00) || !HeightGrid.IsData(z10) || !HeightGrid.IsData(z01) || !HeightGrid.IsData(z11)) return double.NaN;
            if (fx >= fy) return z00 + fx * (z10 - z00) + fy * (z11 - z10);
            return z00 + fy * (z01 - z00) + fx * (z11 - z01);
        }

        /// <summary> crease id = 3 (i + nx j) + type; type 0: diagonal of cell (i, j), 1: horizontal side (i, j)-(i+1, j), 2: vertical side (i, j)-(i, j+1) </summary>
        static ((int x, int y) a, (int x, int y) b) CreaseEnds(int id, int nx)
        {
            int type = id % 3, k = id / 3, i = k % nx, j = k / nx;
            return type == 0 ? ((i, j), (i + 1, j + 1)) : type == 1 ? ((i, j), (i + 1, j)) : ((i, j), (i, j + 1));
        }

        static double Orient(Vector2d a, Vector2d b, Vector2d c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

        /// <summary> the segments (not crease pieces) by the grid cells their bounding boxes touch, for the crease queries of a round </summary>
        static Dictionary<int, List<int>> SegmentBuckets(List<Vector2d> pts, List<(int a, int b)> segs, List<int> segKind, int nx, int ny)
        {
            var d = new Dictionary<int, List<int>>();
            for (int si = 0; si < segs.Count; ++si) {
                if (segKind[si] == KindCrease) continue;
                Vector2d u = pts[segs[si].a], v = pts[segs[si].b];
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(u.x, v.x) - 1e-9)), x1 = Math.Min(nx - 1, (int)Math.Floor(Math.Max(u.x, v.x) + 1e-9));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(u.y, v.y) - 1e-9)), y1 = Math.Min(ny - 1, (int)Math.Floor(Math.Max(u.y, v.y) + 1e-9));
                for (int y = y0; y <= y1; ++y)
                    for (int x = x0; x <= x1; ++x) {
                        int key = x + nx * y;
                        if (!d.TryGetValue(key, out List<int> list)) d[key] = list = new List<int>();
                        list.Add(si);
                    }
            }
            return d;
        }

        /// <summary>
        /// Breakline / boundary segments crossing the crease properly (to split at the crossing), and their vertices on it (to pass through).
        /// <paramref name="conflict"/>: a candidate segment is in <paramref name="blocked"/> (split in this round already; its pieces are not in the buckets).
        /// </summary>
        static List<(double s, int seg, int vertex, Vector2d pt)> CreaseCrossings(List<Vector2d> pts, List<(int a, int b)> segs, Dictionary<int, List<int>> buckets, int nx, HashSet<int> blocked,
            Vector2d p, Vector2d q, out bool conflict)
        {
            conflict = false;
            var list = new List<(double s, int seg, int vertex, Vector2d pt)>();
            var seenVertex = new HashSet<int>(); var seenSeg = new HashSet<int>();
            double len2 = (q.x - p.x) * (q.x - p.x) + (q.y - p.y) * (q.y - p.y), len = Math.Sqrt(len2);
            double minx = Math.Min(p.x, q.x), maxx = Math.Max(p.x, q.x), miny = Math.Min(p.y, q.y), maxy = Math.Max(p.y, q.y);
            var cand = new List<int>();
            for (int cy = (int)Math.Floor(miny); cy <= (int)Math.Floor(maxy); ++cy)
                for (int cx = (int)Math.Floor(minx); cx <= (int)Math.Floor(maxx); ++cx)
                    if (buckets.TryGetValue(cx + nx * cy, out List<int> l))
                        foreach (int si in l) if (seenSeg.Add(si)) cand.Add(si);
            cand.Sort();
            foreach (int si in cand) {
                Vector2d u = pts[segs[si].a], v = pts[segs[si].b];
                if (blocked.Contains(si)) { conflict = true; continue; }
                if (Math.Max(u.x, v.x) < minx - 1e-9 || Math.Min(u.x, v.x) > maxx + 1e-9 || Math.Max(u.y, v.y) < miny - 1e-9 || Math.Min(u.y, v.y) > maxy + 1e-9) continue;
                foreach (int vi in new[] { segs[si].a, segs[si].b }) {
                    Vector2d w = pts[vi];
                    double t = ((w.x - p.x) * (q.x - p.x) + (w.y - p.y) * (q.y - p.y)) / len2;
                    if (t > 1e-9 && t < 1 - 1e-9 && Math.Abs(Orient(p, q, w)) < 1e-9 * len && seenVertex.Add(vi))
                        list.Add((t, -1, vi, w));
                }
                double d1 = Orient(p, q, u), d2 = Orient(p, q, v), d3 = Orient(u, v, p), d4 = Orient(u, v, q);
                if (Math.Abs(d1) < 1e-9 * len || Math.Abs(d2) < 1e-9 * len) continue;      // an end point on the crease: handled as a vertex
                if (d1 * d2 < 0 && d3 * d4 < 0) {
                    double s = d3 / (d3 - d4);
                    list.Add((s, si, -1, new Vector2d(p.x + s * (q.x - p.x), p.y + s * (q.y - p.y))));
                }
            }
            return list;
        }

        /// <summary>
        /// The exact vertical error of a TIN against the grid surface S (<see cref="HeightGrid.Height"/>): <paramref name="surfaceError"/> is the
        /// maximum of |TIN - S| over the whole TIN, <paramref name="nodeError"/> the maximum at the grid nodes inside it. The mesh is in the
        /// coordinates of the grid. Parts over cells without data have no surface and count as no error.
        /// </summary>
        /// <exception cref="ArgumentException">A vertex of a triangle is not finite, or lies outside the grid.</exception>
        public static void ErrorOfTin(HeightGrid grid, DMesh3 tin, out double nodeError, out double surfaceError)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (tin == null) throw new ArgumentNullException(nameof(tin));
            var tris = tin.TriangleIndices().ToArray();
            int[] ids = new int[tris.Length * 3];
            for (int k = 0; k < tris.Length; ++k) {
                Index3i tv = tin.GetTriangle(tris[k]);
                for (int c = 0; c < 3; ++c) {
                    Vector3d v = tin.GetVertex(tv[c]);
                    double rx = (v.x - grid.X0) / grid.CellSize, ry = (v.y - grid.Y0) / grid.CellSize;
                    if (!(rx >= -1e-6 && ry >= -1e-6 && rx <= grid.NX - 1 + 1e-6 && ry <= grid.NY - 1 + 1e-6) || double.IsNaN(v.z) || double.IsInfinity(v.z))
                        throw new ArgumentException("vertex " + tv[c] + " is not finite or lies outside the grid", nameof(tin));
                }
            }
            double nm = 0, sm = 0; object mx = new object();
            var none = new Dictionary<long, int>(); var empty = new HashSet<int>(); var sel = new bool[(long)grid.NX * grid.NY];
            Parallel.For(0, tris.Length, () => new double[2], (k, st, loc) => {
                Index3i tv = tin.GetTriangle(tris[k]);
                Vector3d p0 = tin.GetVertex(tv.a), p1 = tin.GetVertex(tv.b), p2 = tin.GetVertex(tv.c);
                Vector2d a = ToIndex(grid, p0), b = ToIndex(grid, p1), c = ToIndex(grid, p2);
                ScanTriangle(grid, a, b, c, new[] { tv.a, tv.b, tv.c }, sel, none, empty, empty, double.PositiveInfinity, out double wn, out double ws, out _, out _, new[] { p0.z, p1.z, p2.z });
                loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws);
                return loc;
            }, loc => { lock (mx) { nm = Math.Max(nm, loc[0]); sm = Math.Max(sm, loc[1]); } });
            nodeError = nm; surfaceError = sm;
        }

        /// <summary> a point in index coordinates, clamped into the grid (the conversion of a vertex on the border can be a few ulp outside) </summary>
        static Vector2d ToIndex(HeightGrid grid, Vector3d v)
        {
            double x = (v.x - grid.X0) / grid.CellSize, y = (v.y - grid.Y0) / grid.CellSize;
            return new Vector2d(Math.Min(Math.Max(x, 0), grid.NX - 1), Math.Min(Math.Max(y, 0), grid.NY - 1));
        }

        const int MaxRequestsPerTriangle = 16;
        const double NearCorner = 1e-12;

        /// <summary>
        /// The worst violations of a triangle, at most 16, at least <see cref="MinRequestDistance"/> grid cells apart (one insertion near a violation usually fixes its neighbours, so
        /// only separated places are acted on in the same round: a long step then needs rounds in proportion to its length divided by 16 x that distance, not to its length).
        /// </summary>
        sealed class Top
        {
            public readonly List<Req> List = new List<Req>();
            readonly int nx;
            readonly int max;
            public Top(int nx, int max) { this.nx = nx; this.max = max; }
            public void Offer(Req r)
            {
                if (r.Kind == 1) { r.Px = r.Node % nx; r.Py = r.Node / nx; }
                else if (r.Kind == 3) { int k = r.Crease / 3; r.Px = k % nx; r.Py = k / nx; }
                else { r.Px = r.X; r.Py = r.Y; }
                for (int k = 0; k < List.Count; ++k) {
                    Req o = List[k];
                    double dx = o.Px - r.Px, dy = o.Py - r.Py;
                    if (dx * dx + dy * dy < MinRequestDistance * MinRequestDistance) {
                        if (r.Err > o.Err) { List.RemoveAt(k); break; }
                        return;
                    }
                }
                int at = List.Count;
                while (at > 0 && List[at - 1].Err < r.Err) --at;
                if (at >= max) return;
                List.Insert(at, r);
            }
        }

        const double MinRequestDistance = 16;
        /// <summary> the first rounds act on the worst violation of a triangle only (most features are fixed by one insertion each); later rounds on several separated ones (long features) </summary>
        const int SingleRequestRounds = 6;

        /// <summary>
        /// The errors of one triangle (index coordinates) and its violations above <paramref name="act"/> to act on (worst first, at most 16);
        /// zs: the heights of the corners (null: the grid surface there).
        /// </summary>
        static List<Req> ScanTriangle(HeightGrid g, Vector2d a, Vector2d b, Vector2d c, int[] vid, bool[] sel, Dictionary<long, int> edgeSeg, HashSet<int> lockedSet, HashSet<int> chainSet,
            double act, out double nodeErr, out double surfErr, out double loopErr, out double devErr, double[] zs = null, int maxRequests = MaxRequestsPerTriangle, HashSet<long> fixedEdges = null)
        {
            bool devMode = fixedEdges != null;
            int nx = g.NX;
            double za = zs != null ? zs[0] : VertexZ(g, a.x, a.y), zb = zs != null ? zs[1] : VertexZ(g, b.x, b.y), zc = zs != null ? zs[2] : VertexZ(g, c.x, c.y);
            nodeErr = 0; surfErr = 0; loopErr = 0; devErr = 0;
            var top = new Top(nx, maxRequests);
            double det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (det == 0 || double.IsNaN(za) || double.IsNaN(zb) || double.IsNaN(zc)) return top.List;
            if (zs != null) {
                // heights given for the corners (a TIN that is not ours): the corners are overlay vertices, too
                for (int k = 0; k < 3; ++k) {
                    Vector2d v = k == 0 ? a : k == 1 ? b : c;
                    double sz = VertexZ(g, v.x, v.y);
                    if (double.IsNaN(sz)) continue;
                    double e = Math.Abs(zs[k] - sz);
                    if (devMode) devErr = Math.Max(devErr, e); else { surfErr = Math.Max(surfErr, e); loopErr = Math.Max(loopErr, e); }
                    if (v.x == Math.Floor(v.x) && v.y == Math.Floor(v.y)) nodeErr = Math.Max(nodeErr, e);
                }
            }
            double gx = ((zb - za) * (c.y - a.y) - (zc - za) * (b.y - a.y)) / det;
            double gy = ((zc - za) * (b.x - a.x) - (zb - za) * (c.x - a.x)) / det;
            int x0 = (int)Math.Ceiling(Math.Min(a.x, Math.Min(b.x, c.x))), x1 = (int)Math.Floor(Math.Max(a.x, Math.Max(b.x, c.x)));
            int y0 = (int)Math.Ceiling(Math.Min(a.y, Math.Min(b.y, c.y))), y1 = (int)Math.Floor(Math.Max(a.y, Math.Max(b.y, c.y)));
            for (int j = y0; j <= y1; ++j)
                for (int i = x0; i <= x1; ++i) {
                    double o1 = (b.x - a.x) * (j - a.y) - (b.y - a.y) * (i - a.x);
                    double o2 = (c.x - b.x) * (j - b.y) - (c.y - b.y) * (i - b.x);
                    double o3 = (a.x - c.x) * (j - c.y) - (a.y - c.y) * (i - c.x);
                    if (!((o1 >= 0 && o2 >= 0 && o3 >= 0) || (o1 <= 0 && o2 <= 0 && o3 <= 0))) continue;
                    if (devMode && ((i == a.x && j == a.y) || (i == b.x && j == b.y) || (i == c.x && j == c.y))) continue;      // a corner: counted with the corners
                    double zg = g[i, j];
                    if (!HeightGrid.IsData(zg)) continue;
                    double e = Math.Abs(za + gx * (i - a.x) + gy * (j - a.y) - zg);
                    if (e > nodeErr) nodeErr = e;
                    int onEdge = o1 == 0 ? 0 : o2 == 0 ? 1 : o3 == 0 ? 2 : -1;
                    if (devMode && onEdge >= 0 && fixedEdges.Contains(Key(vid[onEdge], vid[(onEdge + 1) % 3]))) { devErr = Math.Max(devErr, e); continue; }      // on a survey line: not reducible
                    if (e > loopErr) loopErr = e;
                    if (e > act) {
                        int seg = -1;
                        if (onEdge >= 0 && edgeSeg.TryGetValue(Key(vid[onEdge], vid[(onEdge + 1) % 3]), out int sg)) seg = sg;
                        if (seg >= 0) top.Offer(new Req { Kind = 2, Seg = seg, X = i, Y = j, Err = e });
                        else top.Offer(new Req { Kind = 1, Node = i + nx * j, Err = e });
                    }
                }
            surfErr = Math.Max(surfErr, nodeErr);
            for (int ed = 0; ed < 3; ++ed) {
                Vector2d p = ed == 0 ? a : ed == 1 ? b : c, q = ed == 0 ? b : ed == 1 ? c : a;
                double zp = ed == 0 ? za : ed == 1 ? zb : zc, zq = ed == 0 ? zb : ed == 1 ? zc : za;
                int segOfEdge = -1;
                bool isLocked = edgeSeg.TryGetValue(Key(vid[ed], vid[(ed + 1) % 3]), out segOfEdge);
                bool onSurvey = devMode && fixedEdges.Contains(Key(vid[ed], vid[(ed + 1) % 3]));
                double dx = q.x - p.x, dy = q.y - p.y;
                for (int kind = 0; kind < 3; ++kind) {
                    double f0 = kind == 0 ? p.x : kind == 1 ? p.y : p.x - p.y;
                    double df = kind == 0 ? dx : kind == 1 ? dy : dx - dy;
                    if (df == 0) continue;
                    double f1 = f0 + df, lo = Math.Min(f0, f1), hi = Math.Max(f0, f1);
                    for (double k = Math.Floor(lo) + 1; k < hi; ++k) {
                        double s = (k - f0) / df;
                        if (!(s > 0 && s < 1)) continue;
                        double x = kind == 0 ? k : p.x + s * dx, y = kind == 1 ? k : p.y + s * dy;
                        double tz = zp + s * (zq - zp);
                        double sz = SurfaceZ(g, x, y, kind == 0, kind == 1);
                        if (double.IsNaN(sz)) continue;
                        double e = Math.Abs(tz - sz);
                        // on a survey line, or at a corner (a crossing at the end of the edge within the rounding of the parameter is the corner, whose error is a
                        // survey deviation if the corner has a survey height): not reducible by refinement
                        if (onSurvey || (devMode && (s < NearCorner || s > 1 - NearCorner))) { if (e > devErr) devErr = e; continue; }
                        if (e > surfErr) surfErr = e;
                        if (e > loopErr) loopErr = e;
                        if (!(e > act)) continue;
                        if (isLocked) { top.Offer(new Req { Kind = 2, Seg = segOfEdge, X = x, Y = y, Err = e }); continue; }
                        int ci = Math.Min((int)Math.Floor(x), g.NX - 2), cj = Math.Min((int)Math.Floor(y), g.NY - 2);
                        // a point on a grid line belongs to the cells on both sides
                        int cellsN = 1; int c2x = 0, c2y = 0;
                        if (kind == 0 && ci >= 1) { cellsN = 2; c2x = ci - 1; c2y = cj; }
                        if (kind == 1 && cj >= 1) { cellsN = 2; c2x = ci; c2y = cj - 1; }
                        int cand = -1; double bd = double.MaxValue;
                        for (int cc = 0; cc < cellsN; ++cc) {
                            int cx = cc == 0 ? ci : c2x, cy = cc == 0 ? cj : c2y;
                            for (int dj = 0; dj <= 1; ++dj)
                                for (int di = 0; di <= 1; ++di) {
                                    int kk = (cx + di) + nx * (cy + dj);
                                    if (sel[kk] || !HeightGrid.IsData(g[cx + di, cy + dj])) continue;
                                    double d2 = (cx + di - x) * (cx + di - x) + (cy + dj - y) * (cy + dj - y);
                                    if (d2 < bd) { bd = d2; cand = kk; }
                                }
                        }
                        if (cand >= 0) { top.Offer(new Req { Kind = 1, Node = cand, Err = e }); continue; }
                        int crease = -1;
                        if (kind == 2) crease = 3 * (ci + nx * cj);
                        else if (kind == 0) { int jj = (int)Math.Floor(y); if (jj >= 0 && jj < g.NY - 1 && y != jj) crease = 3 * (ci + nx * jj) + 2; }
                        else { int ii = (int)Math.Floor(x); if (ii >= 0 && ii < g.NX - 1 && x != ii) crease = 3 * (ii + nx * cj) + 1; }
                        if (crease >= 0 && !lockedSet.Contains(crease) && !chainSet.Contains(crease)) top.Offer(new Req { Kind = 3, Crease = crease, Err = e });
                    }
                }
            }
            surfErr = Math.Max(surfErr, devErr);
            return top.List;
        }
    }
}
