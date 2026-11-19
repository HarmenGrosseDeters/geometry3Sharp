using System;
using System.Collections.Generic;
using System.Linq;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gs.MinCostFlow (successive shortest paths per source with early termination; Ahuja, Magnanti, Orlin 9.7): hand networks,
    /// the optimum against an exhaustive search over all flows of random small networks and against an independent
    /// Bellman-Ford solver on larger ones, the infeasible case, and repeatability.
    /// </summary>
    public class MinCostFlowTests
    {
        static long Solve(int n, (int u, int v, int cap, int cost)[] arcs, int[] excess, out int[] flows, out int[] left, out long cost)
        {
            var net = new MinCostFlow(n);
            var ids = arcs.Select(a => net.AddArc(a.u, a.v, a.cap, a.cost)).ToArray();
            left = (int[])excess.Clone();
            long f = net.Solve(left, out cost);
            flows = ids.Select(net.ArcFlow).ToArray();
            return f;
        }

        [Fact]
        public void TwoPaths_TheCheaperIsFilledFirst()
        {
            // 0 -> 1 -> 3 (cost 1 + 1, capacity 2) and 0 -> 2 -> 3 (cost 3 + 3, capacity 5); 4 units from 0 to 3
            var arcs = new[] { (0, 1, 2, 1), (1, 3, 2, 1), (0, 2, 5, 3), (2, 3, 5, 3) };
            long f = Solve(4, arcs, new[] { 4, 0, 0, -4 }, out int[] flows, out int[] left, out long cost);
            Assert.Equal(4, f);
            Assert.Equal(new[] { 2, 2, 2, 2 }, flows);
            Assert.Equal(2 * 2 + 2 * 6, cost);
            Assert.All(left, x => Assert.Equal(0, x));
        }

        [Fact]
        public void AugmentationReroutesAnEarlierPath()
        {
            // the classic diamond with a cross arc: the second unit needs the residual (reverse) arc of the first
            var arcs = new[] { (0, 1, 1, 1), (0, 2, 1, 4), (1, 2, 1, 1), (1, 3, 1, 4), (2, 3, 1, 1) };
            long f = Solve(4, arcs, new[] { 2, 0, 0, -2 }, out int[] flows, out int[] left, out long cost);
            Assert.Equal(2, f);
            Assert.Equal(1 + 4 + 4 + 1, cost);                 // 0-1-3 and 0-2-3; the cross arc 1-2 is not used
            Assert.Equal(new[] { 1, 1, 0, 1, 1 }, flows);
        }

        [Fact]
        public void MoreSourcesAndSinks_AndZeroCostArcs()
        {
            // two sources, two sinks, a zero cost arc
            var arcs = new[] { (0, 2, 3, 0), (1, 2, 3, 2), (2, 3, 2, 1), (2, 4, 3, 5) };
            long f = Solve(5, arcs, new[] { 2, 3, 0, -2, -3 }, out int[] flows, out int[] left, out long cost);
            Assert.Equal(5, f);
            Assert.Equal(2 * 0 + 3 * 2 + 2 * 1 + 3 * 5, cost);
            Assert.All(left, x => Assert.Equal(0, x));
        }

        [Fact]
        public void Infeasible_ReturnsTheFlowThatFits()
        {
            var arcs = new[] { (0, 1, 2, 1), (1, 2, 1, 1) };
            long f = Solve(3, arcs, new[] { 3, 0, -3 }, out int[] flows, out int[] left, out long cost);
            Assert.Equal(1, f);                               // the arc 1 -> 2 carries one unit
            Assert.Equal(2, left[0]); Assert.Equal(-2, left[2]);
            // no path at all
            f = Solve(3, new[] { (0, 1, 1, 1) }, new[] { 1, 0, -1 }, out flows, out left, out cost);
            Assert.Equal(0, f); Assert.Equal(0, cost);
        }

        [Fact]
        public void NoExcess_NoFlow_AndBadArguments()
        {
            var net = new MinCostFlow(2);
            net.AddArc(0, 1, 5, 1);
            Assert.Equal(0, net.Solve(new int[2], out long cost));
            Assert.Equal(0, cost);
            Assert.Throws<ArgumentOutOfRangeException>(() => net.AddArc(0, 2, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => net.AddArc(0, 1, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => net.AddArc(0, 1, 1, -1));
            Assert.Throws<ArgumentException>(() => net.Solve(new int[1], out cost));
        }

        // the cheapest flow of the excesses by trying every flow (arcs with capacity up to 2): cost, or -1 if none is feasible
        static long BruteForce(int n, (int u, int v, int cap, int cost)[] arcs, int[] excess)
        {
            long best = -1; int m = arcs.Length; var x = new int[m];
            void Rec(int i)
            {
                if (i == m) {
                    var bal = (int[])excess.Clone();
                    long c = 0;
                    for (int k = 0; k < m; ++k) { bal[arcs[k].u] -= x[k]; bal[arcs[k].v] += x[k]; c += (long)x[k] * arcs[k].cost; }
                    if (bal.All(b => b == 0) && (best < 0 || c < best)) best = c;
                    return;
                }
                for (int a = 0; a <= arcs[i].cap; ++a) { x[i] = a; Rec(i + 1); }
            }
            Rec(0);
            return best;
        }

        [Fact]
        public void RandomSmallNetworks_MatchTheExhaustiveSearch()
        {
            var rnd = new Random(11); int feasible = 0, infeasible = 0, zeroCost = 0;
            for (int it = 0; it < 400; ++it) {
                int n = 3 + rnd.Next(3), m = 3 + rnd.Next(5);
                var arcs = new (int, int, int, int)[m];
                for (int k = 0; k < m; ++k) {
                    int u = rnd.Next(n), v = rnd.Next(n - 1); if (v >= u) v++;
                    arcs[k] = (u, v, 1 + rnd.Next(2), rnd.Next(4));
                    if (arcs[k].Item4 == 0) zeroCost++;
                }
                var excess = new int[n];
                int units = 1 + rnd.Next(3);
                for (int u = 0; u < units; ++u) { int a = rnd.Next(n), b = rnd.Next(n); excess[a]++; excess[b]--; }
                long want = BruteForce(n, arcs, excess);
                long supply = excess.Where(e => e > 0).Sum();
                long f = Solve(n, arcs, excess, out int[] flows, out int[] left, out long cost);
                if (want >= 0) {
                    feasible++;
                    Assert.Equal(supply, f);
                    Assert.Equal(want, cost);
                    Assert.All(left, e => Assert.Equal(0, e));
                } else {
                    infeasible++;
                    Assert.True(f < supply, "iteration " + it);
                }
                // the flow is a valid flow of the arcs
                var bal = (int[])excess.Clone();
                for (int k = 0; k < m; ++k) { Assert.InRange(flows[k], 0, arcs[k].Item3); bal[arcs[k].Item1] -= flows[k]; bal[arcs[k].Item2] += flows[k]; }
                Assert.Equal(left, bal);
            }
            Assert.True(feasible > 150 && infeasible > 20 && zeroCost > 100, feasible + " feasible, " + infeasible + " infeasible, " + zeroCost + " zero cost arcs");
        }

        // an independent min-cost flow: successive shortest paths by Bellman-Ford from a super source to a super sink
        static (long flow, long cost) Reference(int n, (int u, int v, int cap, int cost)[] arcs, int[] excess)
        {
            int S = n, T = n + 1; var to = new List<int>(); var cap = new List<int>(); var cst = new List<int>(); var adj = new List<int>[n + 2];
            for (int i = 0; i < n + 2; ++i) adj[i] = new List<int>();
            void Add(int u, int v, int c, int w) { adj[u].Add(to.Count); to.Add(v); cap.Add(c); cst.Add(w); adj[v].Add(to.Count); to.Add(u); cap.Add(0); cst.Add(-w); }
            foreach (var a in arcs) Add(a.u, a.v, a.cap, a.cost);
            for (int i = 0; i < n; ++i) { if (excess[i] > 0) Add(S, i, excess[i], 0); else if (excess[i] < 0) Add(i, T, -excess[i], 0); }
            long flow = 0, cost = 0;
            while (true) {
                var d = new long[n + 2]; var pa = new int[n + 2];
                for (int i = 0; i < n + 2; ++i) { d[i] = long.MaxValue; pa[i] = -1; }
                d[S] = 0;
                for (int round = 0; round < n + 2; ++round) {
                    bool ch = false;
                    for (int u = 0; u < n + 2; ++u) {
                        if (d[u] == long.MaxValue) continue;
                        foreach (int a in adj[u]) if (cap[a] > 0 && d[u] + cst[a] < d[to[a]]) { d[to[a]] = d[u] + cst[a]; pa[to[a]] = a; ch = true; }
                    }
                    if (!ch) break;
                }
                if (d[T] == long.MaxValue) break;
                int b = int.MaxValue;
                for (int v = T; v != S; v = to[pa[v] ^ 1]) b = Math.Min(b, cap[pa[v]]);
                for (int v = T; v != S; v = to[pa[v] ^ 1]) { cap[pa[v]] -= b; cap[pa[v] ^ 1] += b; }
                flow += b; cost += (long)b * d[T];
            }
            return (flow, cost);
        }

        [Fact]
        public void LargerRandomNetworks_MatchTheBellmanFordSolver()
        {
            var rnd = new Random(23); int full = 0, partial = 0;
            for (int it = 0; it < 120; ++it) {
                int n = 12 + rnd.Next(20), m = 3 * n + rnd.Next(2 * n);
                var arcs = new (int, int, int, int)[m];
                for (int k = 0; k < m; ++k) { int u = rnd.Next(n), v = rnd.Next(n - 1); if (v >= u) v++; arcs[k] = (u, v, 2 + rnd.Next(6), rnd.Next(8)); }
                var excess = new int[n];
                for (int u = 0; u < 2 + rnd.Next(6); ++u) { int a = rnd.Next(n), b = rnd.Next(n); int q = 1 + rnd.Next(3); excess[a] += q; excess[b] -= q; }
                long supply = excess.Where(e => e > 0).Sum();
                var (rf, rc) = Reference(n, arcs, excess);
                long f = Solve(n, arcs, excess, out _, out _, out long cost);
                if (rf == supply) { full++; Assert.Equal(supply, f); Assert.Equal(rc, cost); } else partial++;
                // a partial flow (the supply is not feasible) is still routed as far as the network allows, in the order of the nodes
                Assert.True(f <= rf || rf == supply, "iteration " + it);
            }
            Assert.True(full > 60, full + " full flows, " + partial + " partial");
        }

        [Fact]
        public void Solve_IsRepeatable()
        {
            var rnd = new Random(5); int n = 40, m = 160;
            var arcs = new (int, int, int, int)[m];
            for (int k = 0; k < m; ++k) { int u = rnd.Next(n), v = rnd.Next(n - 1); if (v >= u) v++; arcs[k] = (u, v, 1 + rnd.Next(3), rnd.Next(3)); }   // many equal costs: ties
            var excess = new int[n]; for (int u = 0; u < 6; ++u) { excess[rnd.Next(n)] += 2; excess[rnd.Next(n)] -= 2; }
            Solve(n, arcs, excess, out int[] f1, out int[] l1, out long c1);
            for (int rep = 0; rep < 3; ++rep) {
                Solve(n, arcs, excess, out int[] f2, out int[] l2, out long c2);
                Assert.Equal(f1, f2); Assert.Equal(l1, l2); Assert.Equal(c1, c2);
            }
        }
    }
}
