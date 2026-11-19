// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace gs
{
    /// <summary>
    /// Min-cost flow on a network with node excesses, by successive shortest paths with node potentials (Ahuja, Magnanti, Orlin,
    /// Network Flows, 1993, section 9.7), written from the book. No super source or sink: the nodes are taken in index order, and for
    /// a node with excess one Dijkstra search (reduced costs) runs from it and stops at the first node with a deficit that it
    /// settles (the early termination of the textbook: the potentials of the nodes that are not settled are left as they are, relative
    /// to the distance of that node); the flow is augmented along the path. Integer capacities and costs, costs of the arcs added
    /// by AddArc must not be negative. Deterministic: the heap orders by (distance, node index), the arcs of a node are
    /// scanned in the reverse order of AddArc, and nothing depends on a hash or on the thread.
    /// Internal: used by the pure-quad repair (QuadFlowRepair).
    /// </summary>
    internal sealed class MinCostFlow
    {
        public readonly int N;
        int[] head, nxt, to, cap, cost; int m;
        long[] pot, dist;
        int[] prev, settledEp; int ep;
        long[] hk; int[] hv; int hn;
        /// <summary> augmentations, Dijkstra searches and settled nodes of the last Solve </summary>
        public int Augmentations, Searches; public long Settled;

        public MinCostFlow(int n, int arcHint = 0)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            N = n; head = new int[n]; for (int i = 0; i < n; ++i) head[i] = -1;
            int c = Math.Max(16, arcHint * 2);
            nxt = new int[c]; to = new int[c]; cap = new int[c]; cost = new int[c];
        }

        /// <summary> adds the arc u -> v (capacity c >= 0, cost w >= 0) and its residual arc; returns the arc id (even) for ArcFlow </summary>
        public int AddArc(int u, int v, int c, int w)
        {
            if (u < 0 || u >= N || v < 0 || v >= N) throw new ArgumentOutOfRangeException(nameof(u));
            if (c < 0 || w < 0) throw new ArgumentOutOfRangeException(nameof(c), "capacity and cost must not be negative");
            if (m + 2 > to.Length) {
                int nc = to.Length * 2;
                Array.Resize(ref nxt, nc); Array.Resize(ref to, nc); Array.Resize(ref cap, nc); Array.Resize(ref cost, nc);
            }
            int id = m; m += 2;
            to[id] = v; cap[id] = c; cost[id] = w; nxt[id] = head[u]; head[u] = id;
            to[id + 1] = u; cap[id + 1] = 0; cost[id + 1] = -w; nxt[id + 1] = head[v]; head[v] = id + 1;
            return id;
        }

        public int ArcCount { get { return m / 2; } }

        /// <summary> the flow on the arc after Solve </summary>
        public int ArcFlow(int arc) { return cap[arc ^ 1]; }

        void Push(long k, int v)
        {
            if (hn == hk.Length) { Array.Resize(ref hk, hn * 2); Array.Resize(ref hv, hn * 2); }
            int i = hn++;
            while (i > 0) {
                int p = (i - 1) >> 1;
                if (hk[p] < k || (hk[p] == k && hv[p] <= v)) break;
                hk[i] = hk[p]; hv[i] = hv[p]; i = p;
            }
            hk[i] = k; hv[i] = v;
        }

        void Pop(out long k, out int v)
        {
            k = hk[0]; v = hv[0];
            long lk = hk[--hn]; int lv = hv[hn];
            int i = 0;
            while (true) {
                int c = 2 * i + 1; if (c >= hn) break;
                if (c + 1 < hn && (hk[c + 1] < hk[c] || (hk[c + 1] == hk[c] && hv[c + 1] < hv[c]))) c++;
                if (hk[c] > lk || (hk[c] == lk && hv[c] >= lv)) break;
                hk[i] = hk[c]; hv[i] = hv[c]; i = c;
            }
            hk[i] = lk; hv[i] = lv;
        }

        /// <summary>
        /// routes the excesses: a node with excess[k] &gt; 0 sends it to nodes with a deficit (excess &lt; 0), at minimum total cost.
        /// Returns the flow value (the total of the excesses that were routed); less than the sum of the positive excesses if the
        /// network cannot carry all of it (then the flow is a min-cost flow of the value routed so far, in the order of the nodes).
        /// The excess array is updated (what is left); totalCost is the cost of the flow. Solve can be called once per network.
        /// </summary>
        public long Solve(int[] excess, out long totalCost)
        {
            int n = N;
            if (excess == null || excess.Length < n) throw new ArgumentException("excess needs one entry per node", nameof(excess));
            pot = new long[n]; dist = new long[n]; prev = new int[n]; settledEp = new int[n]; ep = 0;
            for (int i = 0; i < n; ++i) dist[i] = long.MaxValue;
            hk = new long[1024]; hv = new int[1024]; hn = 0;
            var touched = new System.Collections.Generic.List<int>(); var settled = new System.Collections.Generic.List<int>();
            long flow = 0; totalCost = 0; Augmentations = 0; Searches = 0; Settled = 0;
            for (int k = 0; k < n; ++k) {
                while (excess[k] > 0) {
                    ++ep; touched.Clear(); settled.Clear(); hn = 0;
                    dist[k] = 0; touched.Add(k); Push(0, k);
                    int l = -1;
                    while (hn > 0) {
                        Pop(out long du, out int u);
                        if (settledEp[u] == ep || du > dist[u]) continue;
                        settledEp[u] = ep; settled.Add(u);
                        if (excess[u] < 0) { l = u; break; }
                        long pu = pot[u];
                        for (int a = head[u]; a >= 0; a = nxt[a]) {
                            if (cap[a] <= 0) continue;
                            int v = to[a];
                            if (settledEp[v] == ep) continue;
                            long nd = du + cost[a] + pu - pot[v];
                            if (nd < dist[v]) { if (dist[v] == long.MaxValue) touched.Add(v); dist[v] = nd; prev[v] = a; Push(nd, v); }
                        }
                    }
                    Settled += settled.Count; Searches++;
                    if (l < 0) { foreach (int u in touched) dist[u] = long.MaxValue; return flow; }
                    long D = dist[l];
                    foreach (int u in settled) pot[u] += dist[u] - D;
                    int b = Math.Min(excess[k], -excess[l]);
                    for (int v = l; v != k; v = to[prev[v] ^ 1]) b = Math.Min(b, cap[prev[v]]);
                    long pc = 0;
                    for (int v = l; v != k; v = to[prev[v] ^ 1]) { int a = prev[v]; cap[a] -= b; cap[a ^ 1] += b; pc += cost[a]; }
                    totalCost += pc * b; flow += b; excess[k] -= b; excess[l] += b; Augmentations++;
                    foreach (int u in touched) dist[u] = long.MaxValue;
                }
            }
            return flow;
        }
    }
}
