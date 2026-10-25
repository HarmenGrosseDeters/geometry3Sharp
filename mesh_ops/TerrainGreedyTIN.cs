// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// TIN with as few vertices as needed for a guaranteed maximum vertical error at every node of a
    /// height grid (eg a DGM / DTM raster): greedy insertion after Garland and Heckbert, "Fast Polygonal
    /// Approximation of Terrains and Height Fields" (1995). Starting from the two triangles of the grid's
    /// rectangle, the grid node farthest (vertically) from the current TIN is inserted, until no node is
    /// farther than MaxError. The triangulation is Delaunay in XY, so the result is a height field (no
    /// overlaps in XY) by construction.
    ///
    /// Node (i, j) lies at (X0 + i CellSize, Y0 + j CellSize), height Z[i + NX j]; the TIN uses the
    /// integer node coordinates, so the orientation tests are exact. No-data nodes (NaN) are not supported.
    /// </summary>
    public class TerrainGreedyTIN
    {
        public int NX, NY;
        public double X0, Y0, CellSize;
        public double[] Z;

        /// <summary> largest vertical distance of any grid node from the TIN </summary>
        public double MaxError = 0.1;
        /// <summary> stop earlier at this many vertices (the error is then larger, see AchievedError) </summary>
        public int MaxVertices = int.MaxValue;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Result;
        /// <summary> the largest vertical distance of a grid node from Result </summary>
        public double AchievedError;


        public TerrainGreedyTIN(double[] z, int nx, int ny, double x0, double y0, double cellSize)
        {
            Z = z; NX = nx; NY = ny; X0 = x0; Y0 = y0; CellSize = cellSize;
        }


        // triangulation: vertices are grid node ids; triangle t has corners tv[3t..3t+2] (counter-clockwise)
        // and neighbours tn[3t+k] across the edge tv[3t+k] -> tv[3t+(k+1)%3] (-1 at the boundary)
        List<int> tv, tn;
        List<bool> alive;
        List<int> version;
        List<int> candidate;        // node with the largest error in the triangle, -1 if none
        List<double> candErr;
        HeapMax heap;
        int vertexCount;

        long gx(int node) { return node % NX; }
        long gy(int node) { return node / NX; }

        // exact orientation of the grid nodes a, b and the node coordinates (px, py)
        long orient(int a, int b, long px, long py)
        {
            long ax = gx(a), ay = gy(a), bx = gx(b), by = gy(b);
            return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        }

        public bool Compute()
        {
            Result = null;
            if (Z == null || NX < 2 || NY < 2 || Z.Length < NX * NY || !(CellSize > 0) || MaxError < 0)
                return false;
            for (int k = 0; k < NX * NY; ++k)
                if (double.IsNaN(Z[k]) || double.IsInfinity(Z[k])) return false;
            tv = new List<int>(); tn = new List<int>(); alive = new List<bool>(); version = new List<int>();
            candidate = new List<int>(); candErr = new List<double>();
            heap = new HeapMax();
            int c00 = 0, c10 = NX - 1, c11 = NX * NY - 1, c01 = NX * (NY - 1);
            int t0 = add_triangle(c00, c10, c11), t1 = add_triangle(c00, c11, c01);
            tn[3 * t0 + 2] = t1;     // edge c11 -> c00
            tn[3 * t1 + 0] = t0;     // edge c00 -> c11
            vertexCount = 4;
            rescan(t0); rescan(t1);

            var changed = new List<int>();
            while (heap.Count > 0) {
                if (CancelF()) return false;
                double err; int t, ver;
                heap.Peek(out err, out t, out ver);
                if (alive[t] == false || ver != version[t]) { heap.Pop(); continue; }
                if (!(err > MaxError) || vertexCount >= MaxVertices) break;
                heap.Pop();
                changed.Clear();
                insert(t, candidate[t], changed);
                vertexCount++;
                foreach (int c in changed)
                    if (alive[c]) rescan(c);
            }
            build_result();
            return true;
        }

        int add_triangle(int a, int b, int c)
        {
            int t = alive.Count;
            tv.Add(a); tv.Add(b); tv.Add(c);
            tn.Add(-1); tn.Add(-1); tn.Add(-1);
            alive.Add(true); version.Add(0); candidate.Add(-1); candErr.Add(0);
            return t;
        }

        void set_triangle(int t, int a, int b, int c, int na, int nb, int nc)
        {
            tv[3 * t] = a; tv[3 * t + 1] = b; tv[3 * t + 2] = c;
            tn[3 * t] = na; tn[3 * t + 1] = nb; tn[3 * t + 2] = nc;
        }

        // in neighbour n, replace the link to old by nu
        void relink(int n, int old, int nu)
        {
            if (n < 0) return;
            for (int k = 0; k < 3; ++k)
                if (tn[3 * n + k] == old) { tn[3 * n + k] = nu; return; }
        }

        // the edge index of triangle t whose neighbour is n
        int edge_to(int t, int n)
        {
            for (int k = 0; k < 3; ++k)
                if (tn[3 * t + k] == n) return k;
            return -1;
        }

        void insert(int t, int p, List<int> changed)
        {
            long px = gx(p), py = gy(p);
            int a = tv[3 * t], b = tv[3 * t + 1], c = tv[3 * t + 2];
            long oa = orient(a, b, px, py), ob = orient(b, c, px, py), oc = orient(c, a, px, py);
            var stack = new Stack<int>();       // triangles whose edge 0 is opposite p (to legalize)
            if (oa > 0 && ob > 0 && oc > 0) {
                // inside: t -> (a, b, p), t1 = (b, c, p), t2 = (c, a, p); edge 0 of each is the old edge
                int na = tn[3 * t], nb = tn[3 * t + 1], nc = tn[3 * t + 2];
                int t1 = add_triangle(b, c, p), t2 = add_triangle(c, a, p);
                set_triangle(t, a, b, p, na, t1, t2);
                set_triangle(t1, b, c, p, nb, t2, t);
                set_triangle(t2, c, a, p, nc, t, t1);
                relink(nb, t, t1); relink(nc, t, t2);
                stack.Push(t); stack.Push(t1); stack.Push(t2);
                changed.Add(t); changed.Add(t1); changed.Add(t2);
            } else {
                // on an edge: rotate t so that the edge is k = 0 (a -> b)
                int k = (oa == 0) ? 0 : (ob == 0) ? 1 : 2;
                a = tv[3 * t + k]; b = tv[3 * t + (k + 1) % 3]; c = tv[3 * t + (k + 2) % 3];
                int nab = tn[3 * t + k], nbc = tn[3 * t + (k + 1) % 3], nca = tn[3 * t + (k + 2) % 3];
                // t -> (c, a, p) and t1 = (b, c, p)
                int t1 = add_triangle(b, c, p);
                set_triangle(t, c, a, p, nca, -1, t1);
                set_triangle(t1, b, c, p, nbc, t, -1);
                relink(nbc, t, t1);
                stack.Push(t); stack.Push(t1);
                changed.Add(t); changed.Add(t1);
                if (nab >= 0) {
                    // the neighbour (b, a, d) -> (a, d, p) and (d, b, p)
                    int n = nab, kn = edge_to(n, t);
                    int d = tv[3 * n + (kn + 2) % 3];
                    int nad = tn[3 * n + (kn + 1) % 3], ndb = tn[3 * n + (kn + 2) % 3];
                    int n1 = add_triangle(d, b, p);
                    set_triangle(n, a, d, p, nad, n1, t);
                    set_triangle(n1, d, b, p, ndb, t1, n);
                    relink(ndb, n, n1);
                    tn[3 * t + 1] = n;          // edge a -> p of (c, a, p)
                    tn[3 * t1 + 2] = n1;        // edge p -> b of (b, c, p)
                    stack.Push(n); stack.Push(n1);
                    changed.Add(n); changed.Add(n1);
                }
            }
            // Lawson flips: edge 0 of each triangle on the stack is opposite p
            while (stack.Count > 0) {
                int u = stack.Pop();
                int v = tn[3 * u];
                if (v < 0) continue;
                int e0 = tv[3 * u], e1 = tv[3 * u + 1];
                int kv = edge_to(v, u);
                int d = tv[3 * v + (kv + 2) % 3];
                // flip if d lies inside the circumcircle of u = (e0, e1, p)
                if (ExactPredicates.InCircle(pt(e0), pt(e1), pt(p), pt(d)) <= 0)
                    continue;
                // v = (e1, e0, d) from edge kv: its other edges e0 -> d and d -> e1
                int n_e0d = tn[3 * v + (kv + 1) % 3], n_de1 = tn[3 * v + (kv + 2) % 3];
                int n_e1p = tn[3 * u + 1], n_pe0 = tn[3 * u + 2];
                // u = (e0, d, p), v = (d, e1, p); edge 0 (opposite p) of each is an old edge of v
                set_triangle(u, e0, d, p, n_e0d, v, n_pe0);
                set_triangle(v, d, e1, p, n_de1, n_e1p, u);
                relink(n_e0d, v, u); relink(n_e1p, u, v);
                stack.Push(u); stack.Push(v);
                if (!changed.Contains(v)) changed.Add(v);
            }
        }

        Vector2d pt(int node) { return new Vector2d(gx(node), gy(node)); }

        // the node with the largest vertical distance from the plane of triangle t (nodes on its edges
        // included; the corners have distance 0)
        void rescan(int t)
        {
            version[t]++;
            int a = tv[3 * t], b = tv[3 * t + 1], c = tv[3 * t + 2];
            long ax = gx(a), ay = gy(a), bx = gx(b), by = gy(b), cx = gx(c), cy = gy(c);
            long det = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            double za = Z[a], zb = Z[b], zc = Z[c];
            long ymin = Math.Min(ay, Math.Min(by, cy)), ymax = Math.Max(ay, Math.Max(by, cy));
            double best = -1; int bestNode = -1;
            for (long y = ymin; y <= ymax; ++y) {
                // x range of the triangle on this row (exact membership below)
                double xlo = double.MaxValue, xhi = double.MinValue;
                row_range(ax, ay, bx, by, y, ref xlo, ref xhi);
                row_range(bx, by, cx, cy, y, ref xlo, ref xhi);
                row_range(cx, cy, ax, ay, y, ref xlo, ref xhi);
                if (xlo > xhi) continue;
                long x0 = (long)Math.Floor(xlo) - 1, x1 = (long)Math.Ceiling(xhi) + 1;
                for (long x = Math.Max(0, x0); x <= Math.Min(NX - 1, x1); ++x) {
                    // barycentric coordinates (exact integer numerators)
                    long w0 = (bx - x) * (cy - y) - (by - y) * (cx - x);
                    long w1 = (cx - x) * (ay - y) - (cy - y) * (ax - x);
                    long w2 = det - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    int node = (int)(x + NX * y);
                    double plane = (w0 * za + w1 * zb + w2 * zc) / det;
                    double e = Math.Abs(Z[node] - plane);
                    if (e > best) { best = e; bestNode = node; }
                }
            }
            candidate[t] = bestNode;
            candErr[t] = best;
            if (bestNode >= 0)
                heap.Push(best, t, version[t]);
        }

        static void row_range(long x0, long y0, long x1, long y1, long y, ref double lo, ref double hi)
        {
            if ((y < Math.Min(y0, y1)) || (y > Math.Max(y0, y1))) return;
            if (y0 == y1) { lo = Math.Min(lo, Math.Min(x0, x1)); hi = Math.Max(hi, Math.Max(x0, x1)); return; }
            double x = x0 + (double)(x1 - x0) * (y - y0) / (y1 - y0);
            lo = Math.Min(lo, x); hi = Math.Max(hi, x);
        }

        void build_result()
        {
            var mesh = new DMesh3(MeshComponents.None);
            var map = new Dictionary<int, int>();
            AchievedError = 0;
            for (int t = 0; t < alive.Count; ++t) {
                if (alive[t] == false) continue;
                if (candErr[t] > AchievedError) AchievedError = candErr[t];
                int[] v = new int[3];
                for (int k = 0; k < 3; ++k) {
                    int node = tv[3 * t + k];
                    if (map.TryGetValue(node, out v[k]) == false) {
                        v[k] = mesh.AppendVertex(new Vector3d(X0 + gx(node) * CellSize, Y0 + gy(node) * CellSize, Z[node]));
                        map[node] = v[k];
                    }
                }
                mesh.AppendTriangle(v[0], v[1], v[2]);
            }
            Result = mesh;
        }

        // binary max-heap of (error, triangle, version) with lazy deletion
        class HeapMax
        {
            List<double> key = new List<double>();
            List<int> tri = new List<int>(), ver = new List<int>();
            public int Count { get { return key.Count; } }
            public void Push(double k, int t, int v)
            {
                key.Add(k); tri.Add(t); ver.Add(v);
                int i = key.Count - 1;
                while (i > 0) {
                    int p = (i - 1) / 2;
                    if (key[p] >= key[i]) break;
                    swap(i, p); i = p;
                }
            }
            public void Peek(out double k, out int t, out int v) { k = key[0]; t = tri[0]; v = ver[0]; }
            public void Pop()
            {
                int last = key.Count - 1;
                swap(0, last);
                key.RemoveAt(last); tri.RemoveAt(last); ver.RemoveAt(last);
                int i = 0;
                while (true) {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < key.Count && key[l] > key[m]) m = l;
                    if (r < key.Count && key[r] > key[m]) m = r;
                    if (m == i) break;
                    swap(i, m); i = m;
                }
            }
            void swap(int i, int j)
            {
                double k = key[i]; key[i] = key[j]; key[j] = k;
                int t = tri[i]; tri[i] = tri[j]; tri[j] = t;
                int v = ver[i]; ver[i] = ver[j]; ver[j] = v;
            }
        }
    }
}
