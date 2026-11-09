// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;

namespace gs
{
    /// <summary>
    /// A breakline for <see cref="TerrainGridTin"/>: a polyline in plan (x, y in the coordinates of the <see cref="HeightGrid"/>);
    /// the TIN contains it as a chain of edges. The heights come from the grid (the grid is the truth), the z of a point is ignored.
    /// </summary>
    public sealed class TerrainBreakline
    {
        public IReadOnlyList<Vector2d> Points { get; }
        public TerrainBreakline(IEnumerable<Vector2d> points) { Points = points.ToList(); }
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
        Cancelled
    }

    /// <summary>
    /// TIN of a height grid (DGM) with a guaranteed vertical error and breaklines (docs/design/TERRAIN_DGM_TO_TIN.md, method B2).
    /// The guarantee is the **surface error**: the exact maximum of |TIN - S| over the whole TIN, where S is the grid TIN
    /// (<see cref="HeightGrid.Height"/>: fixed diagonal, no-data cells have no surface), at most <see cref="Epsilon"/>.
    /// The TIN is a height field (counter-clockwise from above), has an edge chain for every breakline, and covers exactly the cells with data.
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
        /// <summary> the edges of Result (pairs of vertex ids) that belong to breaklines </summary>
        public IReadOnlyList<Index2i> BreaklineEdges { get; private set; } = new Index2i[0];
        /// <summary> creases of the grid surface locked as constraints (cell sides and diagonals), the cost of the surface guarantee </summary>
        public int LockedCreases { get; private set; }
        /// <summary> breakline segments dropped because an end point or the middle is outside the cells with data </summary>
        public int DroppedBreaklineSegments { get; private set; }

        sealed class Req { public int Kind; public int Node = -1; public int Crease = -1; public int Seg = -1; public double X, Y, Err; }
        // Kind: 0 nothing to do, 1 select a node, 2 split a segment at (X, Y), 3 lock a crease

        const int KindBreakline = 0, KindBoundary = 1, KindCrease = 2;

        /// <summary> Computes the TIN. Returns true on Ok. </summary>
        public bool Compute()
        {
            Result = null; BreaklineEdges = new Index2i[0]; Rounds = 0; SurfaceError = double.NaN; NodeError = double.NaN; LockedCreases = 0; DroppedBreaklineSegments = 0;
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
            if ((long)nx * 3 * ny > int.MaxValue / 2)
                return Fail(TerrainGridTinStatus.InvalidInput, "the grid is too large");
            foreach (TerrainBreakline bl in Breaklines ?? new List<TerrainBreakline>())
                if (bl == null || bl.Points.Any(p => double.IsNaN(p.x) || double.IsNaN(p.y) || double.IsInfinity(p.x) || double.IsInfinity(p.y)))
                    return Fail(TerrainGridTinStatus.InvalidInput, "a breakline is null or has a non-finite point");

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
            var nodeId = new Dictionary<int, int>();
            int AddNode(int i, int j) { int k = i + nx * j; int id; if (nodeId.TryGetValue(k, out id)) return id; id = pts.Count; nodeId[k] = id; pts.Add(new Vector2d(i, j)); return id; }
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
            // one seed in every 4-connected component of the cells without data (the face of the constraints around it is removed): a point of the
            // component that lies inside the convex hull of the boundary points (a seed outside the domain is ignored, and a component that touches the rim can have its
            // cell centres outside the hull)
            var hull = ConvexHull(pts);
            // the hull edges that are not boundary runs close the faces of the components of cells without data that touch the hull
            var runPairs = new HashSet<long>(segs.Select(sg => Key(sg.a, sg.b)));
            for (int k = 0; k < hull.Count; ++k) {
                int ha = hull[k], hb = hull[(k + 1) % hull.Count];
                if (!runPairs.Contains(Key(ha, hb))) AddSeg(ha, hb, KindBoundary);
            }
            var seeds = new List<Vector2d>();
            var seen = new bool[cellOk.Length];
            var stack = new Stack<int>();
            var samples = new[] { new Vector2d(0.5, 0.5), new Vector2d(0.1, 0.9), new Vector2d(0.9, 0.9), new Vector2d(0.1, 0.1), new Vector2d(0.9, 0.1), new Vector2d(0.5, 0.95), new Vector2d(0.95, 0.5), new Vector2d(0.5, 0.05), new Vector2d(0.05, 0.5) };
            for (int s0 = 0; s0 < cellOk.Length; ++s0) {
                if (cellOk[s0] || seen[s0]) continue;
                bool haveSeed = false;
                seen[s0] = true; stack.Push(s0);
                while (stack.Count > 0) {
                    int c = stack.Pop(); int ci = c % (nx - 1), cj = c / (nx - 1);
                    if (!haveSeed)
                        foreach (Vector2d sm in samples) {
                            var sp = new Vector2d(ci + sm.x, cj + sm.y);
                            if (StrictlyInside(pts, hull, sp)) { seeds.Add(sp); haveSeed = true; break; }
                        }
                    for (int d = 0; d < 4; ++d) {
                        int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (ni < 0 || nj < 0 || ni >= nx - 1 || nj >= ny - 1) continue;
                        int nc = ni + (nx - 1) * nj;
                        if (!cellOk[nc] && !seen[nc]) { seen[nc] = true; stack.Push(nc); }
                    }
                }
            }
            // breaklines in index coordinates; segments with an end point or the middle outside the cells with data are dropped
            foreach (TerrainBreakline bl in Breaklines ?? new List<TerrainBreakline>()) {
                int prev = -1; Vector2d prevP = default(Vector2d); bool hasPrev = false;
                foreach (Vector2d w in bl.Points) {
                    Vector2d p = new Vector2d((w.x - g.X0) / g.CellSize, (w.y - g.Y0) / g.CellSize);
                    bool inside = InCell(p, nx, ny, Ok);
                    bool joined = false;
                    if (inside && !(prev >= 0 && p == prevP)) {
                        int id = pts.Count;
                        if (prev >= 0 && InCell(new Vector2d((p.x + prevP.x) / 2, (p.y + prevP.y) / 2), nx, ny, Ok)) { pts.Add(p); AddSeg(prev, id, KindBreakline); joined = true; }
                        else if (prev < 0 || true) { pts.Add(p); }
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
            ConstrainedMesh2 lastMesh = null; double lastSurf = double.NaN, lastNode = double.NaN;
            List<int> lastMarkers = null;

            for (int round = 0; round < MaxRounds; ++round) {
                if (CancelF()) return Fail(TerrainGridTinStatus.Cancelled, "cancelled");
                var input = new ConstrainedMesh2Input();
                input.Points.AddRange(pts);
                for (int s = 0; s < segs.Count; ++s) input.Segments.Add(new Mesh2Segment(segs[s].a, segs[s].b, s + 1, true));
                foreach (int crease in lockedCreases) {
                    var e = CreaseEnds(crease, nx);
                    input.Segments.Add(new Mesh2Segment(nodeId[e.a.x + nx * e.a.y], nodeId[e.b.x + nx * e.b.y], 0, true));
                }
                input.HoleSeeds.AddRange(seeds);
                ConstrainedTriangulation2Result r = new QualityMesh2 { MinAngle = 0, ResolveCrossings = true }.Compute(input);
                if (r.Mesh == null) return Fail(TerrainGridTinStatus.MesherFailed, r.Status + " " + r.FailureDetail);
                ConstrainedMesh2 m = r.Mesh;
                var edgeSeg = new Dictionary<long, int>();
                foreach (Mesh2Subsegment ss in m.Subsegments)
                    if (ss.Markers != null && ss.Markers.Length > 0) edgeSeg[Key(ss.A, ss.B)] = ss.Markers[0] - 1;      // (overlapping sources: any one is split)
                    else if (ss.Marker > 0) edgeSeg[Key(ss.A, ss.B)] = ss.Marker - 1;
                Array.Clear(sel, 0, sel.Length);
                foreach (var kv in nodeId) sel[kv.Key] = true;

                int nt = m.Triangles.Count;
                var reqs = new Req[nt];
                double nodeMax = 0, surfMax = 0; object mx = new object();
                Parallel.For(0, nt, () => new double[2], (t, st, loc) => {
                    Index3i tr = m.Triangles[t];
                    reqs[t] = ScanTriangle(g, m.Vertices[tr.a], m.Vertices[tr.b], m.Vertices[tr.c], new[] { tr.a, tr.b, tr.c }, sel, edgeSeg, lockedCreases, chainCreases, out double wn, out double ws);
                    loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws);
                    return loc;
                }, loc => { lock (mx) { nodeMax = Math.Max(nodeMax, loc[0]); surfMax = Math.Max(surfMax, loc[1]); } });
                Rounds = round + 1;
                lastMesh = m; lastSurf = surfMax + margin; lastNode = nodeMax + margin;
                lastMarkers = segKind.ToList();
                if (surfMax + margin <= Epsilon) break;

                // act on the violations, in triangle order (deterministic)
                int added = 0; var splitSeg = new HashSet<int>();
                for (int t = 0; t < nt; ++t) {
                    Req q = reqs[t];
                    if (q == null || !(q.Err + margin > Epsilon)) continue;
                    if (q.Kind == 1 && q.Node >= 0 && !sel[q.Node]) { AddNode(q.Node % nx, q.Node / nx); sel[q.Node] = true; ++added; }
                    else if (q.Kind == 2 && q.Seg >= 0 && q.Seg < segs.Count && splitSeg.Add(q.Seg)) {
                        var (a, b) = segs[q.Seg];
                        int id = pts.Count; pts.Add(new Vector2d(q.X, q.Y));
                        int kind = segKind[q.Seg];
                        segs[q.Seg] = (a, id); AddSeg(id, b, kind); ++added;
                    } else if (q.Kind == 3 && !lockedCreases.Contains(q.Crease) && !chainCreases.Contains(q.Crease)) {
                        var e = CreaseEnds(q.Crease, nx);
                        var xs = CreaseCrossings(pts, segs, segKind, new Vector2d(e.a.x, e.a.y), new Vector2d(e.b.x, e.b.y));
                        if (xs.Count == 0) { AddNode(e.a.x, e.a.y); AddNode(e.b.x, e.b.y); lockedCreases.Add(q.Crease); ++added; continue; }
                        if (xs.Any(x => x.seg >= 0 && splitSeg.Contains(x.seg))) continue;      // retried in the next round
                        int prev = AddNode(e.a.x, e.a.y), last = AddNode(e.b.x, e.b.y);
                        foreach (var x in xs.OrderBy(x => x.s)) {
                            int id;
                            if (x.seg >= 0) {
                                splitSeg.Add(x.seg);
                                var (sa, sb) = segs[x.seg];
                                id = pts.Count; pts.Add(x.pt);
                                int kind = segKind[x.seg];
                                segs[x.seg] = (sa, id); AddSeg(id, sb, kind);
                            } else id = x.vertex;
                            AddSeg(prev, id, KindCrease); prev = id;
                        }
                        AddSeg(prev, last, KindCrease);
                        chainCreases.Add(q.Crease); ++added;
                    }
                }
                if (added == 0) break;       // no progress: reported below as NotConverged
            }

            // the result
            var mesh = new DMesh3(MeshComponents.None);
            for (int v = 0; v < lastMesh.Vertices.Count; ++v) {
                Vector2d p = lastMesh.Vertices[v];
                mesh.AppendVertex(new Vector3d(g.X0 + p.x * g.CellSize, g.Y0 + p.y * g.CellSize, VertexZ(g, p.x, p.y)));
            }
            foreach (Index3i tr in lastMesh.Triangles) mesh.AppendTriangle(tr.a, tr.b, tr.c);
            var edges = new List<Index2i>();
            foreach (Mesh2Subsegment ss in lastMesh.Subsegments) {
                int[] ms = ss.Markers != null && ss.Markers.Length > 0 ? ss.Markers : (ss.Marker > 0 ? new[] { ss.Marker } : new int[0]);
                if (ms.Any(mk => mk - 1 < lastMarkers.Count && lastMarkers[mk - 1] == KindBreakline)) edges.Add(new Index2i(ss.A, ss.B));
            }
            Result = mesh; BreaklineEdges = edges;
            SurfaceError = lastSurf; NodeError = lastNode;
            LockedCreases = lockedCreases.Count + chainCreases.Count;
            if (lastSurf > Epsilon)
                return Fail(TerrainGridTinStatus.NotConverged, "the surface error " + lastSurf + " is above Epsilon after " + Rounds + " rounds", false);
            Status = TerrainGridTinStatus.Ok;
            return true;
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

        /// <summary> indices of the corners of the convex hull of the points, counter-clockwise, collinear points left out </summary>
        static List<int> ConvexHull(List<Vector2d> points)
        {
            var p = Enumerable.Range(0, points.Count).OrderBy(i => points[i].x).ThenBy(i => points[i].y).ToList();
            var h = new List<int>();
            for (int pass = 0; pass < 2; ++pass) {
                int start = h.Count;
                foreach (int v in pass == 0 ? p : Enumerable.Reverse(p)) {
                    while (h.Count >= start + 2 && Orient(points[h[h.Count - 2]], points[h[h.Count - 1]], points[v]) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(v);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        static bool StrictlyInside(List<Vector2d> pts, List<int> hull, Vector2d p)
        {
            if (hull.Count < 3) return false;
            for (int k = 0; k < hull.Count; ++k)
                if (Orient(pts[hull[k]], pts[hull[(k + 1) % hull.Count]], p) <= 0) return false;
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

        /// <summary> breakline segments crossing the crease properly (to split at the crossing), and breakline vertices on it (to pass through) </summary>
        static List<(double s, int seg, int vertex, Vector2d pt)> CreaseCrossings(List<Vector2d> pts, List<(int a, int b)> segs, List<int> segKind, Vector2d p, Vector2d q)
        {
            var list = new List<(double s, int seg, int vertex, Vector2d pt)>();
            var seenVertex = new HashSet<int>();
            double len2 = (q.x - p.x) * (q.x - p.x) + (q.y - p.y) * (q.y - p.y), len = Math.Sqrt(len2);
            double minx = Math.Min(p.x, q.x), maxx = Math.Max(p.x, q.x), miny = Math.Min(p.y, q.y), maxy = Math.Max(p.y, q.y);
            for (int si = 0; si < segs.Count; ++si) {
                if (segKind[si] == KindCrease) continue;
                Vector2d u = pts[segs[si].a], v = pts[segs[si].b];
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
        /// coordinates of the grid; triangles outside the cells with data are not supported (their error is not counted).
        /// </summary>
        public static void ErrorOfTin(HeightGrid grid, DMesh3 tin, out double nodeError, out double surfaceError)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (tin == null) throw new ArgumentNullException(nameof(tin));
            var tris = tin.TriangleIndices().ToArray();
            double nm = 0, sm = 0; object mx = new object();
            var none = new Dictionary<long, int>(); var empty = new HashSet<int>(); var sel = new bool[grid.NX * grid.NY];
            Parallel.For(0, tris.Length, () => new double[2], (k, st, loc) => {
                Index3i tv = tin.GetTriangle(tris[k]);
                Vector3d p0 = tin.GetVertex(tv.a), p1 = tin.GetVertex(tv.b), p2 = tin.GetVertex(tv.c);
                Vector2d a = new Vector2d((p0.x - grid.X0) / grid.CellSize, (p0.y - grid.Y0) / grid.CellSize);
                Vector2d b = new Vector2d((p1.x - grid.X0) / grid.CellSize, (p1.y - grid.Y0) / grid.CellSize);
                Vector2d c = new Vector2d((p2.x - grid.X0) / grid.CellSize, (p2.y - grid.Y0) / grid.CellSize);
                ScanTriangle(grid, a, b, c, new[] { tv.a, tv.b, tv.c }, sel, none, empty, empty, out double wn, out double ws, new[] { p0.z, p1.z, p2.z });
                loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws);
                return loc;
            }, loc => { lock (mx) { nm = Math.Max(nm, loc[0]); sm = Math.Max(sm, loc[1]); } });
            nodeError = nm; surfaceError = sm;
        }

        /// <summary> The errors of one triangle (index coordinates) and the worst violation to act on; zs: the heights of the corners (null: the grid surface there). </summary>
        static Req ScanTriangle(HeightGrid g, Vector2d a, Vector2d b, Vector2d c, int[] vid, bool[] sel, Dictionary<long, int> edgeSeg, HashSet<int> lockedSet, HashSet<int> chainSet,
            out double nodeErr, out double surfErr, double[] zs = null)
        {
            int nx = g.NX;
            double za = zs != null ? zs[0] : VertexZ(g, a.x, a.y), zb = zs != null ? zs[1] : VertexZ(g, b.x, b.y), zc = zs != null ? zs[2] : VertexZ(g, c.x, c.y);
            nodeErr = 0; surfErr = 0;
            var req = new Req();
            double det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (det == 0 || double.IsNaN(za) || double.IsNaN(zb) || double.IsNaN(zc)) return req;
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
                    double zg = g[i, j];
                    if (!HeightGrid.IsData(zg)) continue;
                    double e = Math.Abs(za + gx * (i - a.x) + gy * (j - a.y) - zg);
                    if (e > nodeErr) nodeErr = e;
                    if (e > req.Err) {
                        req.Err = e;
                        int onEdge = o1 == 0 ? 0 : o2 == 0 ? 1 : o3 == 0 ? 2 : -1;
                        int seg = -1;
                        if (onEdge >= 0 && edgeSeg.TryGetValue(Key(vid[onEdge], vid[(onEdge + 1) % 3]), out int sg)) seg = sg;
                        if (seg >= 0) { req.Kind = 2; req.Seg = seg; req.X = i; req.Y = j; }
                        else { req.Kind = 1; req.Node = i + nx * j; }
                    }
                }
            surfErr = nodeErr;
            for (int ed = 0; ed < 3; ++ed) {
                Vector2d p = ed == 0 ? a : ed == 1 ? b : c, q = ed == 0 ? b : ed == 1 ? c : a;
                double zp = ed == 0 ? za : ed == 1 ? zb : zc, zq = ed == 0 ? zb : ed == 1 ? zc : za;
                int segOfEdge = -1;
                bool isLocked = edgeSeg.TryGetValue(Key(vid[ed], vid[(ed + 1) % 3]), out segOfEdge);
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
                        if (e > surfErr) surfErr = e;
                        if (e > req.Err) {
                            req.Err = e;
                            if (isLocked) { req.Kind = 2; req.Seg = segOfEdge; req.X = x; req.Y = y; continue; }
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
                            if (cand >= 0) { req.Kind = 1; req.Node = cand; }
                            else {
                                req.Kind = 0;
                                int crease = -1;
                                if (kind == 2) crease = 3 * (ci + nx * cj);
                                else if (kind == 0) { int jj = (int)Math.Floor(y); if (jj >= 0 && jj < g.NY - 1 && y != jj) crease = 3 * (ci + nx * jj) + 2; }
                                else { int ii = (int)Math.Floor(x); if (ii >= 0 && ii < g.NX - 1 && x != ii) crease = 3 * (ii + nx * cj) + 1; }
                                if (crease >= 0 && !lockedSet.Contains(crease) && !chainSet.Contains(crease)) { req.Kind = 3; req.Crease = crease; }
                            }
                        }
                    }
                }
            }
            return req;
        }
    }
}
