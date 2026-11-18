// Throwaway spike (round 5, speed): min-cost flow by successive shortest paths with potentials (Ahuja, Magnanti, Orlin, Network
// Flows, 9.7) with the textbook early termination of Dijkstra (stop when the sink is permanently labelled; the unlabelled nodes get
// the distance of the sink, kept as a global offset so one augmentation costs only the labelled nodes), arrays instead of lists,
// ties of the heap broken by node index (deterministic).
using System;

class McfFast
{
    public int N;
    int[] head, nxt, to, cap, cost; int m;
    long[] pot; long G;
    long[] dist; int[] prev; int[] settledEp; int ep;
    long[] hk; int[] hv; int hn;
    public int Augmentations; public long Settled;

    public McfFast(int n, int arcHint)
    {
        N = n; head = new int[n]; for (int i = 0; i < n; ++i) head[i] = -1;
        int c = Math.Max(16, arcHint * 2);
        nxt = new int[c]; to = new int[c]; cap = new int[c]; cost = new int[c];
    }

    public int Add(int u, int v, int c, int w)
    {
        if (m + 2 > to.Length) { int nc = to.Length * 2; Array.Resize(ref nxt, nc); Array.Resize(ref to, nc); Array.Resize(ref cap, nc); Array.Resize(ref cost, nc); }
        int id = m; m += 2;
        to[id] = v; cap[id] = c; cost[id] = w; nxt[id] = head[u]; head[u] = id;
        to[id + 1] = u; cap[id + 1] = 0; cost[id + 1] = -w; nxt[id + 1] = head[v]; head[v] = id + 1;
        return id;
    }
    public int ArcCount { get { return m / 2; } }
    public int Flow(int arc) { return cap[arc ^ 1]; }

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

    public int Solve(int s, int t, out long totalCost)
    {
        int n = N;
        pot = new long[n]; dist = new long[n]; prev = new int[n]; settledEp = new int[n]; ep = 0; G = 0;
        for (int i = 0; i < n; ++i) dist[i] = long.MaxValue;
        hk = new long[1024]; hv = new int[1024]; hn = 0;
        var touched = new System.Collections.Generic.List<int>(); var settled = new System.Collections.Generic.List<int>();
        int flow = 0; totalCost = 0; Augmentations = 0;
        while (true) {
            ++ep; touched.Clear(); settled.Clear(); hn = 0;
            dist[s] = 0; touched.Add(s); Push(0, s);
            bool reached = false;
            while (hn > 0) {
                Pop(out long du, out int u);
                if (settledEp[u] == ep || du > dist[u]) continue;
                settledEp[u] = ep; settled.Add(u);
                if (u == t) { reached = true; break; }
                long pu = pot[u];
                for (int a = head[u]; a >= 0; a = nxt[a]) {
                    if (cap[a] <= 0) continue;
                    int v = to[a];
                    if (settledEp[v] == ep) continue;
                    long nd = du + cost[a] + pu - pot[v];
                    if (nd < dist[v]) { if (dist[v] == long.MaxValue) touched.Add(v); dist[v] = nd; prev[v] = a; Push(nd, v); }
                }
            }
            Settled += settled.Count;
            if (!reached) { foreach (int u in touched) dist[u] = long.MaxValue; break; }
            long D = dist[t];
            foreach (int u in settled) pot[u] += dist[u] - D;
            G += D;
            int b = int.MaxValue;
            for (int v = t; v != s; v = to[prev[v] ^ 1]) b = Math.Min(b, cap[prev[v]]);
            long pc = 0;
            for (int v = t; v != s; v = to[prev[v] ^ 1]) { int a = prev[v]; cap[a] -= b; cap[a ^ 1] += b; pc += cost[a]; }
            totalCost += pc * b; flow += b; Augmentations++;
            foreach (int u in touched) dist[u] = long.MaxValue;
        }
        return flow;
    }
}
