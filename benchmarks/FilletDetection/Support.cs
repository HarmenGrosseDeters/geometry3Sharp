// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace FilletDetection
{
    /// <summary> per triangle answer of a detector: fillet or not, and the fillet radius it found (NaN: none) </summary>
    public class Detection
    {
        public bool[] Fillet;
        public double[] Radius;
        public double Seconds;
        public string Note = "";
        public Detection(int n) { Fillet = new bool[n]; Radius = new double[n]; for (int i = 0; i < n; ++i) Radius[i] = double.NaN; }
    }

    /// <summary> uniform hash grid over points: ball queries and k nearest neighbours </summary>
    public class PointGrid
    {
        readonly Vector3d[] pts;
        readonly double h;
        readonly Dictionary<(int, int, int), List<int>> cells = new Dictionary<(int, int, int), List<int>>();

        /// <summary> cellSize 0: chosen from the extent and the number of points </summary>
        public PointGrid(Vector3d[] points, double cellSize = 0)
        {
            pts = points;
            if (cellSize <= 0) {
                var box = AxisAlignedBox3d.Empty;
                foreach (Vector3d p in points) box.Contain(p);
                cellSize = Math.Max(1e-9, box.DiagonalLength / (2 * Math.Pow(Math.Max(1, points.Length), 1.0 / 3)));
            }
            h = cellSize;
            for (int i = 0; i < pts.Length; ++i) {
                var key = Key(pts[i]);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                list.Add(i);
            }
        }
        (int, int, int) Key(Vector3d p) { return ((int)Math.Floor(p.x / h), (int)Math.Floor(p.y / h), (int)Math.Floor(p.z / h)); }

        /// <summary> calls f for every point within the box of half size radius around c (a superset of the ball) </summary>
        public void InBox(Vector3d c, double radius, Action<int> f)
        {
            var lo = Key(c - radius * Vector3d.One); var hi = Key(c + radius * Vector3d.One);
            if ((double)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4.0 * cells.Count) {
                // fewer cells than the box has: visit them all, in a fixed order
                for (int i = 0; i < pts.Length; ++i) {
                    var k = Key(pts[i]);
                    if (k.Item1 >= lo.Item1 && k.Item1 <= hi.Item1 && k.Item2 >= lo.Item2 && k.Item2 <= hi.Item2 && k.Item3 >= lo.Item3 && k.Item3 <= hi.Item3) f(i);
                }
                return;
            }
            for (int x = lo.Item1; x <= hi.Item1; ++x)
                for (int y = lo.Item2; y <= hi.Item2; ++y)
                    for (int z = lo.Item3; z <= hi.Item3; ++z)
                        if (cells.TryGetValue((x, y, z), out var list))
                            foreach (int i in list) f(i);
        }

        /// <summary> the k nearest points to c (c itself included if it is one of them), as (distance, index), nearest first </summary>
        public List<(double, int)> Nearest(Vector3d c, int k)
        {
            var found = new List<(double, int)>();
            for (int ring = 1; ring < 1 << 20; ring *= 2) {
                found.Clear();
                InBox(c, ring * h, i => found.Add((pts[i].Distance(c), i)));
                found.Sort();
                if ((found.Count >= k && found[k - 1].Item1 <= ring * h) || found.Count == pts.Length) break;
            }
            if (found.Count > k) found.RemoveRange(k, found.Count - k);
            return found;
        }
    }

    /// <summary> maximum flow / minimum cut (Dinic), for the two-label graph cut </summary>
    public class MaxFlow
    {
        readonly List<int> to = new List<int>(), next = new List<int>();
        readonly List<double> cap = new List<double>();
        readonly int[] head, level, it;
        const double Eps = 1e-12;

        public MaxFlow(int nodes) { head = new int[nodes]; level = new int[nodes]; it = new int[nodes]; for (int i = 0; i < nodes; ++i) head[i] = -1; }

        public void AddEdge(int u, int v, double c, double reverse)
        {
            to.Add(v); cap.Add(c); next.Add(head[u]); head[u] = to.Count - 1;
            to.Add(u); cap.Add(reverse); next.Add(head[v]); head[v] = to.Count - 1;
        }

        bool Levels(int s, int t)
        {
            for (int i = 0; i < level.Length; ++i) level[i] = -1;
            var queue = new Queue<int>(); queue.Enqueue(s); level[s] = 0;
            while (queue.Count > 0) {
                int u = queue.Dequeue();
                for (int e = head[u]; e >= 0; e = next[e])
                    if (cap[e] > Eps && level[to[e]] < 0) { level[to[e]] = level[u] + 1; queue.Enqueue(to[e]); }
            }
            return level[t] >= 0;
        }

        public double Run(int s, int t)
        {
            double flow = 0;
            var path = new List<int>();
            while (Levels(s, t)) {
                Array.Copy(head, it, head.Length);
                path.Clear();
                int u = s;
                while (true) {
                    if (u == t) {
                        double f = double.MaxValue;
                        foreach (int e in path) f = Math.Min(f, cap[e]);
                        foreach (int e in path) { cap[e] -= f; cap[e ^ 1] += f; }
                        flow += f; path.Clear(); u = s;
                        continue;
                    }
                    int edge = it[u];
                    while (edge >= 0 && !(cap[edge] > Eps && level[to[edge]] == level[u] + 1)) edge = next[edge];
                    it[u] = edge;
                    if (edge >= 0) { path.Add(edge); u = to[edge]; continue; }
                    if (u == s) break;
                    level[u] = -1;      // dead end in this phase
                    int back = path[path.Count - 1]; path.RemoveAt(path.Count - 1);
                    u = to[back ^ 1];
                }
            }
            return flow;
        }

        /// <summary> after Run: the node is on the source side of the minimum cut </summary>
        public bool SourceSide(int node) { return level[node] >= 0; }
    }
}
