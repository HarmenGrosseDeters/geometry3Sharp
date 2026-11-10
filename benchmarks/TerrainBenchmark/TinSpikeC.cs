// Spike, variant (c) of docs/design/TERRAIN_DGM_TO_TIN.md (owner 2026-11-09): TerrainGreedyTIN (+RemoveVertices) selects the
// nodes, QualityMesh2 triangulates them once as a constrained Delaunay triangulation with the locked breaklines (and a hole for
// gaps), then a correction pass enforces the SURFACE guarantee on that output: exact overlay check on the real mesh; where the
// surface error exceeds eps, a node of the cell is added, a breakline / hole-boundary segment is split, or the cell's diagonal is
// locked; repeated until clean. Benchmark only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;

namespace TerrainBenchmark
{
    public static class TinSpikeC
    {
        public class Result
        {
            public int Selected, Vertices, Triangles, Rounds, LockedDiagonals, SkippedLocks, BreaklineSegments, SplitsDone;
            public double GreedySeconds, MeshSeconds, ScanSeconds, TotalSeconds;
            public double Round0Surface, Round0Node, SurfaceError, NodeError;
            public bool Ok = true;
            public string Note = "";
        }

        sealed class Req { public int Kind; public int Node = -1; public int Cell = -1; public int Seg = -1; public double X, Y; public double Err; }
        // Kind: 0 none, 1 select node, 2 split segment at (X,Y), 3 lock the diagonal of Cell

        public static List<Vector2d[]> Circles(int n, double spacing)
        {
            double c = (n - 1) / 2.0, rpit = n * 0.30, period = rpit / 8.0;
            var list = new List<Vector2d[]>();
            foreach (int m in new[] { 1, 3, 5, 7 }) {
                double r = rpit - m * period;
                int k = Math.Max(8, (int)Math.Round(2 * Math.PI * r / spacing));
                var pts = new Vector2d[k + 1];
                for (int q = 0; q < k; ++q) pts[q] = new Vector2d(c + r * Math.Cos(2 * Math.PI * q / k), c + r * Math.Sin(2 * Math.PI * q / k));
                pts[k] = pts[0];
                list.Add(pts);
            }
            return list;
        }

        static double SurfaceZ(HeightGrid g, double x, double y, bool xIsLine, bool yIsLine)
        {
            if (xIsLine) { int i = (int)x; int j = Math.Min((int)Math.Floor(y), g.NY - 2); double f = y - j; return g[i, j] + f * (g[i, j + 1] - g[i, j]); }
            if (yIsLine) { int j = (int)y; int i = Math.Min((int)Math.Floor(x), g.NX - 2); double f = x - i; return g[i, j] + f * (g[i + 1, j] - g[i, j]); }
            return g.Height(x, y);
        }

        public static Result Run(HeightGrid g, double eps, bool remove, List<Vector2d[]> breaklines, int[] hole, bool useGreedy = true, int maxRounds = 400)
        {
            var total = Stopwatch.StartNew();
            var res = new Result();
            int nx = g.NX, ny = g.NY;
            // 1. selection by greedy on a gap-filled copy
            var sw = Stopwatch.StartNew();
            var zf = (double[])g.Z.Clone();
            if (hole != null) {
                double fill = g[hole[0] - 1, hole[1] - 1];
                for (int j = hole[1]; j <= hole[3]; ++j) for (int i = hole[0]; i <= hole[2]; ++i) zf[i + nx * j] = fill;
            }
            var gt = new TerrainGreedyTIN(zf, nx, ny, 0, 0, 1) { MaxError = eps, RemoveVertices = remove };
            if (useGreedy) gt.Compute();
            res.GreedySeconds = sw.Elapsed.TotalSeconds;
            // hole polygon (valid nodes around the no-data nodes): cells with a no-data corner have no surface
            int hx0 = hole == null ? 0 : hole[0] - 1, hy0 = hole == null ? 0 : hole[1] - 1, hx1 = hole == null ? 0 : hole[2] + 1, hy1 = hole == null ? 0 : hole[3] + 1;
            bool InHole(int i, int j) => hole != null && i > hx0 && i < hx1 && j > hy0 && j < hy1;

            var pts = new List<Vector2d>();
            var nodeId = new Dictionary<int, int>();
            int AddNode(int i, int j) { int k = i + nx * j; if (nodeId.TryGetValue(k, out int id)) return id; id = pts.Count; nodeId[k] = id; pts.Add(new Vector2d(i, j)); return id; }
            if (useGreedy) foreach (int vid in gt.Result.VertexIndices()) {
                Vector3d v = gt.Result.GetVertex(vid);
                int i = (int)Math.Round(v.x), j = (int)Math.Round(v.y);
                if (!InHole(i, j)) AddNode(i, j);
            }
            AddNode(0, 0); AddNode(nx - 1, 0); AddNode(0, ny - 1); AddNode(nx - 1, ny - 1);
            res.Selected = pts.Count;
            var segs = new List<(int a, int b)>();                // constraints other than diagonals; marker = index + 1
            if (hole != null) {
                int c0 = AddNode(hx0, hy0), c1 = AddNode(hx1, hy0), c2 = AddNode(hx1, hy1), c3 = AddNode(hx0, hy1);
                segs.Add((c0, c1)); segs.Add((c1, c2)); segs.Add((c2, c3)); segs.Add((c3, c0));
            }
            if (breaklines != null)
                foreach (var line in breaklines) {
                    int prev = -1;
                    foreach (Vector2d p in line) {
                        if (p.x <= 0 || p.y <= 0 || p.x >= nx - 1 || p.y >= ny - 1 || InHole((int)Math.Round(p.x), (int)Math.Round(p.y))) { prev = -1; continue; }
                        int id = pts.Count; pts.Add(p);
                        if (prev >= 0) segs.Add((prev, id));
                        prev = id;
                    }
                }
            var lockedCells = new HashSet<int>();
            var chainCells = new HashSet<int>();
            var sel = new bool[nx * ny];
            for (int round = 0; round < maxRounds; ++round) {
                sw.Restart();
                var input = new ConstrainedMesh2Input();
                input.Points.AddRange(pts);
                for (int s = 0; s < segs.Count; ++s) input.Segments.Add(new Mesh2Segment(segs[s].a, segs[s].b, s + 1, true));
                foreach (int crease in lockedCells) {
                    var (ea, eb) = CreaseEnds(crease, nx);
                    input.Segments.Add(new Mesh2Segment(nodeId[ea.x + nx * ea.y], nodeId[eb.x + nx * eb.y], 0, true));
                }
                if (hole != null) input.HoleSeeds.Add(new Vector2d((hx0 + hx1) / 2.0, (hy0 + hy1) / 2.0));
                var qm = new QualityMesh2 { MinAngle = 0, ResolveCrossings = false };
                ConstrainedTriangulation2Result r = qm.Compute(input);
                res.MeshSeconds += sw.Elapsed.TotalSeconds;
                if (r.Mesh == null) { res.Ok = false; res.Note = "mesher: " + r.Status + " " + r.FailureDetail; break; }
                ConstrainedMesh2 m = r.Mesh;
                var edgeSeg = new Dictionary<long, int>();
                foreach (Mesh2Subsegment ss in m.Subsegments)
                    if (ss.Marker > 0) edgeSeg[Key(ss.A, ss.B)] = ss.Marker - 1;
                var inputOfVertex = new int[m.Vertices.Count];
                for (int q = 0; q < inputOfVertex.Length; ++q) inputOfVertex[q] = -1;
                for (int q = 0; q < m.InputToVertex.Count; ++q) if (m.InputToVertex[q] >= 0) inputOfVertex[m.InputToVertex[q]] = q;
                Array.Clear(sel, 0, sel.Length);
                foreach (var kv in nodeId) sel[kv.Key] = true;
                sw.Restart();
                int nt = m.Triangles.Count;
                var reqs = new Req[nt];
                double nodeMax = 0, surfMax = 0; object mx = new object();
                Parallel.For(0, nt, () => new double[2], (t, st, loc) => {
                    reqs[t] = Scan(g, m, t, sel, edgeSeg, lockedCells, chainCells, out double wn, out double ws);
                    loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws);
                    return loc;
                }, loc => { lock (mx) { nodeMax = Math.Max(nodeMax, loc[0]); surfMax = Math.Max(surfMax, loc[1]); } });
                res.ScanSeconds += sw.Elapsed.TotalSeconds;
                res.Rounds = round + 1; res.Vertices = m.Vertices.Count; res.Triangles = nt;
                res.NodeError = nodeMax; res.SurfaceError = surfMax;
                if (round == 0) { res.Round0Surface = surfMax; res.Round0Node = nodeMax; }
                if (surfMax <= eps) break;
                int added = 0; var splitSeg = new HashSet<int>();
                var segList = segs;
                for (int t = 0; t < nt; ++t) {
                    Req q = reqs[t];
                    if (q == null || !(q.Err > eps)) continue;
                    if (q.Kind == 1 && q.Node >= 0 && !sel[q.Node]) { AddNode(q.Node % nx, q.Node / nx); sel[q.Node] = true; ++added; }
                    else if (q.Kind == 2 && splitSeg.Add(q.Seg)) {
                        var (a, b) = segList[q.Seg];
                        int id = pts.Count; pts.Add(new Vector2d(q.X, q.Y));
                        segList[q.Seg] = (a, id); segList.Add((id, b)); ++added; ++res.SplitsDone;
                    } else if (q.Kind == 3 && !lockedCells.Contains(q.Cell) && !chainCells.Contains(q.Cell)) {
                        var (ea, eb) = CreaseEnds(q.Cell, nx);
                        var xs = CreaseCrossings(pts, segs, new Vector2d(ea.x, ea.y), new Vector2d(eb.x, eb.y));
                        if (xs.Count == 0) { AddNode(ea.x, ea.y); AddNode(eb.x, eb.y); lockedCells.Add(q.Cell); ++added; continue; }
                        // the crease crosses breaklines: split them at the crossings (or pass through their vertices) and add the crease as a chain of pieces
                        if (xs.Any(x => x.seg >= 0 && splitSeg.Contains(x.seg))) { ++res.SkippedLocks; continue; }
                        int prev = AddNode(ea.x, ea.y), last = AddNode(eb.x, eb.y);
                        foreach (var x in xs.OrderBy(x => x.s)) {
                            int id;
                            if (x.seg >= 0) {
                                splitSeg.Add(x.seg);
                                var (sa, sb) = segs[x.seg];
                                id = pts.Count; pts.Add(x.pt);
                                segs[x.seg] = (sa, id); segs.Add((id, sb)); ++res.SplitsDone;
                            } else id = x.vertex;
                            segs.Add((prev, id)); prev = id;
                        }
                        segs.Add((prev, last));
                        chainCells.Add(q.Cell); ++added;
                    }
                }
                if (added == 0) {
                    Req w = reqs.Where(x => x != null).OrderByDescending(x => x.Err).First();
                    int wt = Array.IndexOf(reqs, w);
                    Index3i wtr = m.Triangles[wt];
                    string tri = string.Join(" | ", new[] { wtr.a, wtr.b, wtr.c }.Select(v => m.Vertices[v].x.ToString("R") + "," + m.Vertices[v].y.ToString("R") + " z=" + g.Height(m.Vertices[v].x, m.Vertices[v].y).ToString("R")));
                    Console.Error.WriteLine("worst triangle: " + tri + " edge locked: " + string.Join(",", new[] { (wtr.a, wtr.b), (wtr.b, wtr.c), (wtr.c, wtr.a) }.Select(e => edgeSeg.ContainsKey(Key(e.Item1, e.Item2)))));
                    res.Ok = false;
                    res.Note = "stuck at round " + round + " with surface error " + surfMax + "; worst request kind " + w.Kind + " err " + w.Err + " node " + w.Node + " cell " + w.Cell + " seg " + w.Seg + " at " + w.X + "," + w.Y
                        + (w.Kind == 3 ? " cellLocked=" + lockedCells.Contains(w.Cell) + " chain=" + chainCells.Contains(w.Cell) : "") + " violating requests " + reqs.Count(x => x != null && x.Err > eps);
                    break;
                }
            }
            res.LockedDiagonals = lockedCells.Count + chainCells.Count;
            res.BreaklineSegments = segs.Count;
            res.TotalSeconds = total.Elapsed.TotalSeconds + 0;
            return res;
        }

        static long Key(int a, int b) { return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; }

        /// <summary> crease id = 3 (i + nx j) + type; type 0: diagonal of cell (i, j), 1: horizontal edge (i, j)-(i+1, j), 2: vertical edge (i, j)-(i, j+1) </summary>
        static ((int x, int y) a, (int x, int y) b) CreaseEnds(int id, int nx)
        {
            int type = id % 3, k = id / 3, i = k % nx, j = k / nx;
            return type == 0 ? ((i, j), (i + 1, j + 1)) : type == 1 ? ((i, j), (i + 1, j)) : ((i, j), (i, j + 1));
        }

        /// <summary> breakline segments crossing the crease properly (to split), and breakline vertices on it (to pass through) </summary>
        static List<(double s, int seg, int vertex, Vector2d pt)> CreaseCrossings(List<Vector2d> pts, List<(int a, int b)> segs, Vector2d p, Vector2d q)
        {
            var list = new List<(double, int, int, Vector2d)>();
            var seenVertex = new HashSet<int>();
            double len2 = (q.x - p.x) * (q.x - p.x) + (q.y - p.y) * (q.y - p.y), len = Math.Sqrt(len2);
            double minx = Math.Min(p.x, q.x), maxx = Math.Max(p.x, q.x), miny = Math.Min(p.y, q.y), maxy = Math.Max(p.y, q.y);
            for (int si = 0; si < segs.Count; ++si) {
                Vector2d u = pts[segs[si].a], v = pts[segs[si].b];
                if (Math.Max(u.x, v.x) < minx - 1e-9 || Math.Min(u.x, v.x) > maxx + 1e-9 || Math.Max(u.y, v.y) < miny - 1e-9 || Math.Min(u.y, v.y) > maxy + 1e-9) continue;
                foreach (int vi in new[] { segs[si].a, segs[si].b }) {
                    Vector2d w = pts[vi];
                    double t = ((w.x - p.x) * (q.x - p.x) + (w.y - p.y) * (q.y - p.y)) / len2;
                    if (t > 1e-9 && t < 1 - 1e-9 && Math.Abs(Orient(p, q, w)) < 1e-9 * len && seenVertex.Add(vi))
                        list.Add((t, -1, vi, w));
                }
                double d1 = Orient(p, q, u), d2 = Orient(p, q, v), d3 = Orient(u, v, p), d4 = Orient(u, v, q);
                if (Math.Abs(d1) < 1e-9 * len || Math.Abs(d2) < 1e-9 * len) continue;   // an end point on the crease: handled as a vertex
                if (d1 * d2 < 0 && d3 * d4 < 0) {
                    double s = d3 / (d3 - d4);
                    list.Add((s, si, -1, new Vector2d(p.x + s * (q.x - p.x), p.y + s * (q.y - p.y))));
                }
            }
            return list.Select(x => (x.Item1, x.Item2, x.Item3, x.Item4)).ToList();
        }

        static double Orient(Vector2d a, Vector2d b, Vector2d c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

        /// <summary> The errors of triangle t and the worst violation to act on. </summary>
        static Req Scan(HeightGrid g, ConstrainedMesh2 m, int t, bool[] sel, Dictionary<long, int> edgeSeg, HashSet<int> lockedSet, HashSet<int> chainSet, out double nodeErr, out double surfErr)
        {
            Index3i tr = m.Triangles[t];
            return ScanTriangle(g, m.Vertices[tr.a], m.Vertices[tr.b], m.Vertices[tr.c], new[] { tr.a, tr.b, tr.c }, sel, edgeSeg, lockedSet, chainSet, out nodeErr, out surfErr);
        }

        /// <summary> the exact node and surface error of a TIN (a DMesh3 in grid index space), eg of TerrainGreedyTIN's result </summary>
        public static void ErrorsOfMesh(HeightGrid g, DMesh3 mesh, out double nodeErr, out double surfErr)
        {
            var tris = mesh.TriangleIndices().ToArray();
            double nm = 0, sm = 0; object mx = new object();
            var none = new Dictionary<long, int>(); var empty = new HashSet<int>(); var sel = new bool[g.NX * g.NY];
            Parallel.For(0, tris.Length, () => new double[2], (k, st, loc) => {
                Index3i tv = mesh.GetTriangle(tris[k]);
                Vector3d p0 = mesh.GetVertex(tv.a), p1 = mesh.GetVertex(tv.b), p2 = mesh.GetVertex(tv.c);
                ScanTriangle(g, new Vector2d(p0.x, p0.y), new Vector2d(p1.x, p1.y), new Vector2d(p2.x, p2.y), new[] { tv.a, tv.b, tv.c }, sel, none, empty, empty, out double wn, out double ws);
                loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws);
                return loc;
            }, loc => { lock (mx) { nm = Math.Max(nm, loc[0]); sm = Math.Max(sm, loc[1]); } });
            nodeErr = nm; surfErr = sm;
        }

        static Req ScanTriangle(HeightGrid g, Vector2d a, Vector2d b, Vector2d c, int[] vid, bool[] sel, Dictionary<long, int> edgeSeg, HashSet<int> lockedSet, HashSet<int> chainSet, out double nodeErr, out double surfErr)
        {
            int nx = g.NX;
            double za = g.Height(a.x, a.y), zb = g.Height(b.x, b.y), zc = g.Height(c.x, c.y);
            if (double.IsNaN(za)) za = Node(g, a); if (double.IsNaN(zb)) zb = Node(g, b); if (double.IsNaN(zc)) zc = Node(g, c);
            nodeErr = 0; surfErr = 0;
            var req = new Req();
            double det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (det == 0) return req;
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
                    if (double.IsNaN(zg)) continue;
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
                edgeSeg.TryGetValue(Key(vid[ed], vid[(ed + 1) % 3]), out int segOfEdge);
                bool locked = edgeSeg.ContainsKey(Key(vid[ed], vid[(ed + 1) % 3]));
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
                            if (locked) { req.Kind = 2; req.Seg = segOfEdge; req.X = x; req.Y = y; continue; }
                            int ci = Math.Min((int)Math.Floor(x), g.NX - 2), cj = Math.Min((int)Math.Floor(y), g.NY - 2);
                            // a point on a grid line belongs to the cells on both sides
                            var cells = new List<(int, int)> { (ci, cj) };
                            if (kind == 0 && ci >= 1) cells.Add((ci - 1, cj));
                            if (kind == 1 && cj >= 1) cells.Add((ci, cj - 1));
                            int cand = -1; double bd = double.MaxValue;
                            foreach (var (cx, cy) in cells)
                                for (int dj = 0; dj <= 1; ++dj)
                                    for (int di = 0; di <= 1; ++di) {
                                        int kk = (cx + di) + nx * (cy + dj);
                                        if (sel[kk] || double.IsNaN(g[cx + di, cy + dj])) continue;
                                        double d2 = (cx + di - x) * (cx + di - x) + (cy + dj - y) * (cy + dj - y);
                                        if (d2 < bd) { bd = d2; cand = kk; }
                                    }
                            if (cand >= 0) { req.Kind = 1; req.Node = cand; }
                            else {
                                req.Kind = 0;
                                int crease = -1;
                                if (kind == 2) crease = 3 * (ci + nx * cj);
                                else if (kind == 0) { int jj = (int)Math.Floor(y); if (jj >= 0 && jj < g.NY - 1 && y != jj) crease = 3 * (ci + nx * jj) + 2; }
                                else { int ii = (int)Math.Floor(x); if (ii >= 0 && ii < g.NX - 1 && x != ii) crease = 3 * (ii + nx * cj) + 1; }
                                if (crease >= 0 && !lockedSet.Contains(crease) && !chainSet.Contains(crease)) { req.Kind = 3; req.Cell = crease; }
                            }
                        }
                    }
                }
            }
            return req;
        }

        static double Node(HeightGrid g, Vector2d v) { return g[(int)Math.Round(v.x), (int)Math.Round(v.y)]; }
    }
}
