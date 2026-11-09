// Spike of docs/design/TERRAIN_DGM_TO_TIN.md (method B2): error-driven rounds around QualityMesh2 (constrained Delaunay,
// MinAngle 0) in grid index space, with the exact surface error as the guarantee (D1) or the node error only (for the cost
// comparison). Benchmark only; no breaklines, no gaps yet. Compared with TerrainGreedyTIN on the same grid.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;

namespace TerrainBenchmark
{
    public static class TinSpike
    {
        public class Result
        {
            public int Vertices, Triangles, Rounds, LockedDiagonals;
            public double Seconds, MeshSeconds, ScanSeconds;
            public double SurfaceError, NodeError;
            public bool Ok = true;
            public string Note = "";
        }

        /// <summary> hilly terrain with an open pit with benches (index space = metres at cell 1) </summary>
        public static HeightGrid Pit(int n, double amp, int seed)
        {
            var z = new double[n * n];
            var rand = new Random(seed);
            double ph1 = rand.NextDouble() * 6, ph2 = rand.NextDouble() * 6;
            double c = (n - 1) / 2.0, rpit = n * 0.30, bench = amp / 8;
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = i - c, y = j - c, r = Math.Sqrt(x * x + y * y);
                    double hills = 0.15 * amp * (Math.Sin(i * 0.011 + ph1) + Math.Cos(j * 0.013 + ph2)) + 0.5 * amp * (1.0 - Math.Min(1, r / (n * 0.7)));
                    double depth = r < rpit ? Math.Floor((rpit - r) / (rpit / 8.0)) * bench * 1.0 + bench * 0.5 * Math.Min(1, ((rpit - r) % (rpit / 8.0)) / (rpit / 40.0)) : 0;
                    z[i + n * j] = hills - depth + 0.03 * Math.Sin(i * 0.9) * Math.Cos(j * 1.1) + 0.02 * Math.Sin(0.37 * (i + 2 * j));
                }
            return new HeightGrid(z, n, n, 0, 0, 1);
        }

        public static Result Run(HeightGrid g, double eps, bool surface, double frac = 0.5, int maxRounds = 400)
        {
            var total = Stopwatch.StartNew();
            var res = new Result();
            int nx = g.NX, ny = g.NY;
            var sel = new bool[nx * ny];
            var lockedCell = new HashSet<int>();
            var order = new List<int>();
            void Select(int i, int j) { int k = i + nx * j; if (!sel[k]) { sel[k] = true; order.Add(k); } }
            Select(0, 0); Select(nx - 1, 0); Select(0, ny - 1); Select(nx - 1, ny - 1);
            for (int round = 0; round < maxRounds; ++round) {
                var sw = Stopwatch.StartNew();
                var input = new ConstrainedMesh2Input();
                var idOfNode = new Dictionary<int, int>();
                foreach (int k in order) { idOfNode[k] = input.Points.Count; input.Points.Add(new Vector2d(k % nx, k / nx)); }
                foreach (int cell in lockedCell) {
                    int ci = cell % (nx - 1), cj = cell / (nx - 1);
                    input.Segments.Add(new Mesh2Segment(idOfNode[ci + nx * cj], idOfNode[ci + 1 + nx * (cj + 1)], 0, true));
                }
                var qm = new QualityMesh2 { MinAngle = 0, ResolveCrossings = false };
                ConstrainedTriangulation2Result r = qm.Compute(input);
                res.MeshSeconds += sw.Elapsed.TotalSeconds;
                if (r.Mesh == null) { res.Ok = false; res.Note = "mesher: " + r.Status + " " + r.FailureDetail; break; }
                ConstrainedMesh2 m = r.Mesh;
                sw.Restart();
                // scan: per triangle the worst violation (error, candidate node, locked-cell request)
                int nt = m.Triangles.Count;
                var worst = new double[nt]; var cand = new int[nt]; var lockReq = new int[nt];
                double nodeMax = 0, surfMax = 0;
                object mx = new object();
                Parallel.For(0, nt, () => new double[2], (t, st, loc) => {
                    Scan(g, m, t, surface, sel, out double wn, out double ws, out worst[t], out cand[t], out lockReq[t]);
                    loc[0] = Math.Max(loc[0], wn); loc[1] = Math.Max(loc[1], ws);
                    return loc;
                }, loc => { lock (mx) { nodeMax = Math.Max(nodeMax, loc[0]); surfMax = Math.Max(surfMax, loc[1]); } });
                res.ScanSeconds += sw.Elapsed.TotalSeconds;
                res.Rounds = round + 1; res.Vertices = m.Vertices.Count; res.Triangles = nt;
                res.NodeError = nodeMax; res.SurfaceError = surface ? surfMax : double.NaN;
                double guard = surface ? surfMax : nodeMax;
                if (guard <= eps) break;
                double thr = Math.Max(eps, frac * guard);
                int added = 0;
                for (int t = 0; t < nt; ++t) {
                    if (!(worst[t] > thr)) continue;
                    if (cand[t] >= 0) { if (!sel[cand[t]]) { Select(cand[t] % nx, cand[t] / nx); ++added; } }
                    else if (lockReq[t] >= 0 && lockedCell.Add(lockReq[t])) {
                        int ci = lockReq[t] % (nx - 1), cj = lockReq[t] / (nx - 1);
                        Select(ci, cj); Select(ci + 1, cj + 1); ++added;
                    }
                }
                if (added == 0 && thr > eps) { // nothing above the threshold could be inserted: lower it
                    for (int t = 0; t < nt && added == 0; ++t)
                        if (worst[t] > eps) {
                            if (cand[t] >= 0 && !sel[cand[t]]) { Select(cand[t] % nx, cand[t] / nx); ++added; }
                            else if (lockReq[t] >= 0 && lockedCell.Add(lockReq[t])) { int ci = lockReq[t] % (nx - 1), cj = lockReq[t] / (nx - 1); Select(ci, cj); Select(ci + 1, cj + 1); ++added; }
                        }
                }
                if (added == 0) { res.Ok = false; res.Note = "no progress at round " + round; break; }
            }
            res.LockedDiagonals = lockedCell.Count;
            res.Seconds = total.Elapsed.TotalSeconds;
            return res;
        }

        /// <summary> The errors of triangle t: worst node error, worst surface error (if requested), and the worst violation to act on. </summary>
        static void Scan(HeightGrid g, ConstrainedMesh2 m, int t, bool surface, bool[] sel, out double nodeErr, out double surfErr, out double worstViol, out int candNode, out int lockCell)
        {
            Index3i tr = m.Triangles[t];
            Vector2d a = m.Vertices[tr.a], b = m.Vertices[tr.b], c = m.Vertices[tr.c];
            int nx = g.NX;
            double za = g[(int)a.x, (int)a.y], zb = g[(int)b.x, (int)b.y], zc = g[(int)c.x, (int)c.y];
            nodeErr = 0; surfErr = 0; worstViol = 0; candNode = -1; lockCell = -1;
            double det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (det == 0) return;
            // plane z = za + gx (x - ax) + gy (y - ay)
            double gx = ((zb - za) * (c.y - a.y) - (zc - za) * (b.y - a.y)) / det;
            double gy = ((zc - za) * (b.x - a.x) - (zb - za) * (c.x - a.x)) / det;
            int x0 = (int)Math.Min(a.x, Math.Min(b.x, c.x)), x1 = (int)Math.Max(a.x, Math.Max(b.x, c.x));
            int y0 = (int)Math.Min(a.y, Math.Min(b.y, c.y)), y1 = (int)Math.Max(a.y, Math.Max(b.y, c.y));
            // nodes inside (exact: integer coordinates, the orientation signs in doubles of small integers are exact)
            for (int j = y0; j <= y1; ++j) {
                // x range of the row from the edges
                for (int i = x0; i <= x1; ++i) {
                    double o1 = (b.x - a.x) * (j - a.y) - (b.y - a.y) * (i - a.x);
                    double o2 = (c.x - b.x) * (j - b.y) - (c.y - b.y) * (i - b.x);
                    double o3 = (a.x - c.x) * (j - c.y) - (a.y - c.y) * (i - c.x);
                    if (!((o1 >= 0 && o2 >= 0 && o3 >= 0) || (o1 <= 0 && o2 <= 0 && o3 <= 0))) continue;
                    double e = Math.Abs(za + gx * (i - a.x) + gy * (j - a.y) - g[i, j]);
                    if (e > nodeErr) nodeErr = e;
                    if (e > worstViol) { worstViol = e; candNode = i + nx * j; lockCell = -1; }
                }
            }
            surfErr = nodeErr;
            if (!surface) return;
            // crossings of the three edges with x = k, y = k, x - y = k
            for (int ed = 0; ed < 3; ++ed) {
                Vector2d p = ed == 0 ? a : ed == 1 ? b : c, q = ed == 0 ? b : ed == 1 ? c : a;
                double zp = ed == 0 ? za : ed == 1 ? zb : zc, zq = ed == 0 ? zb : ed == 1 ? zc : za;
                double dx = q.x - p.x, dy = q.y - p.y;
                for (int kind = 0; kind < 3; ++kind) {
                    double f0 = kind == 0 ? p.x : kind == 1 ? p.y : p.x - p.y;
                    double df = kind == 0 ? dx : kind == 1 ? dy : dx - dy;
                    if (df == 0) continue;
                    double f1 = f0 + df;
                    double lo = Math.Min(f0, f1), hi = Math.Max(f0, f1);
                    for (double k = Math.Floor(lo) + 1; k < hi; ++k) {
                        double s = (k - f0) / df;
                        if (!(s > 0 && s < 1)) continue;
                        double x = p.x + s * dx, y = p.y + s * dy;
                        double tz = zp + s * (zq - zp);
                        double sz = g.Height(x, y);
                        double e = Math.Abs(tz - sz);
                        if (e > surfErr) surfErr = e;
                        if (e > worstViol) {
                            worstViol = e;
                            int ci = Math.Min((int)Math.Floor(x), g.NX - 2), cj = Math.Min((int)Math.Floor(y), g.NY - 2);
                            // nearest corner of the cell that is not a vertex yet
                            candNode = -1; double bd = double.MaxValue;
                            for (int dj = 0; dj <= 1; ++dj)
                                for (int di = 0; di <= 1; ++di) {
                                    int kk = (ci + di) + nx * (cj + dj);
                                    if (sel[kk]) continue;
                                    double d2 = (ci + di - x) * (ci + di - x) + (cj + dj - y) * (cj + dj - y);
                                    if (d2 < bd) { bd = d2; candNode = kk; }
                                }
                            lockCell = candNode < 0 ? ci + (g.NX - 1) * cj : -1;
                        }
                    }
                }
            }
        }

        /// <summary> exact surface error of a finished selection, for the node-only run (same scan, all overlay vertices) </summary>
        public static double SurfaceErrorOf(HeightGrid g, IReadOnlyList<Vector2d> pts, IReadOnlyList<int[]> tris)
        {
            return double.NaN;
        }
    }
}
