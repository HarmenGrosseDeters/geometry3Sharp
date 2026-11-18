// Throwaway spike (QUAD_REMESHING.md 4.3, second measurement). Min-cost flow by successive shortest paths with potentials
// (Ahuja, Magnanti, Orlin, Network Flows, section 9.7), written for this spike.
using System;
using System.Collections.Generic;

class Mcf
{
    public int N;
    public List<int> To = new List<int>(), Cap = new List<int>(), Cost = new List<int>();
    public List<int>[] Adj;
    public Mcf(int n) { N = n; Adj = new List<int>[n]; for (int i = 0; i < n; ++i) Adj[i] = new List<int>(); }

    public int Add(int u, int v, int cap, int cost)
    {
        int id = To.Count;
        To.Add(v); Cap.Add(cap); Cost.Add(cost); Adj[u].Add(id);
        To.Add(u); Cap.Add(0); Cost.Add(-cost); Adj[v].Add(id + 1);
        return id;
    }

    public int Flow(int arc) { return Cap[arc ^ 1]; }
    public int Augmentations;

    // returns the flow value; total cost in cost
    public int Solve(int s, int t, out long cost)
    {
        int n = N;
        var pot = new long[n]; var dist = new long[n]; var prevArc = new int[n];
        int flow = 0; cost = 0; Augmentations = 0;
        var pq = new PriorityQueue<int, long>();
        while (true) {
            for (int i = 0; i < n; ++i) { dist[i] = long.MaxValue; prevArc[i] = -1; }
            dist[s] = 0; pq.Clear(); pq.Enqueue(s, 0);
            while (pq.Count > 0) {
                pq.TryDequeue(out int u, out long du);
                if (du > dist[u]) continue;
                foreach (int a in Adj[u]) {
                    if (Cap[a] <= 0) continue;
                    int v = To[a];
                    long nd = du + Cost[a] + pot[u] - pot[v];
                    if (nd < dist[v]) { dist[v] = nd; prevArc[v] = a; pq.Enqueue(v, nd); }
                }
            }
            if (dist[t] == long.MaxValue) break;
            for (int i = 0; i < n; ++i) if (dist[i] != long.MaxValue) pot[i] += dist[i];
            int b = int.MaxValue;
            for (int v = t; v != s; v = To[prevArc[v] ^ 1]) b = Math.Min(b, Cap[prevArc[v]]);
            for (int v = t; v != s; v = To[prevArc[v] ^ 1]) { Cap[prevArc[v]] -= b; Cap[prevArc[v] ^ 1] += b; cost += (long)b * Cost[prevArc[v]]; }
            flow += b; Augmentations++;
        }
        return flow;
    }
}
