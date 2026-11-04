using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Meshing2Acceptance
{
    public static partial class Oracle
    {
        /// <summary> tests only (per thread): the witness is built but never certified (to show that the certificate is what the reduction
        /// rests on: a mutant of it must change verdicts or fail the check mode) </summary>
        [ThreadStatic] public static bool SkipWitnessCertificateForTests;
        /// <summary> tests only (per thread): every witness counts as failed, so the global pass runs in full (the fallback) </summary>
        [ThreadStatic] public static bool FailWitnessForTests;
        /// <summary> tests only (per thread): changes the witness after it is built and before it is certified (a wrong witness must be
        /// refused by the certificate, and the pass then runs in full) </summary>
        [ThreadStatic] public static Func<List<int[]>, List<int[]>> CorruptWitnessForTests;
        /// <summary> tests only: how the last global pass ran on this thread (null: no pass; "witness", "fallback: ...") </summary>
        [ThreadStatic] public static string LastGlobalPassForTests;

        sealed partial class Run
        {
            /// <summary>
            /// The witness of the global Delaunay pass (MESHING2D_FAST_ORACLE.md section 6): the oracle's own Delaunay triangulation W of the
            /// used vertices, built by incremental insertion (Bowyer and Watson; ghost triangles for the outside of the hull, as in
            /// Cheng, Dey and Shewchuk 2012, ch. 3) with the oracle's filtered exact predicates, then certified independently of how it was
            /// built: (a) a triangulation of the convex hull of U and (b) every interior edge locally Delaunay. Returns the indices of the
            /// output triangles that are not triangles of W, in order, or null when W cannot be built or certified (the caller then runs
            /// the full pass). Nothing of the output's triangles is used, only its vertices.
            /// </summary>
            List<int> OutsideDelaunayWitness(int[] used, out string why)
            {
                why = null;
                if (FailWitnessForTests) { why = "failed for tests"; return null; }
                // (a defect in building or certifying W costs time, never a verdict: an exception there is a failed witness too)
                List<int[]> w;
                try {
                    w = BuildWitness(used, out why);
                    if (w == null) return null;
                    if (CorruptWitnessForTests != null) w = CorruptWitnessForTests(w);
                    if (!SkipWitnessCertificateForTests && !CertifyWitness(w, used, out why)) return null;
                } catch (Exception ex) when (!(ex is OutOfMemoryException)) {
                    why = "the witness threw " + ex.GetType().Name;
                    return null;
                }
                var triples = new HashSet<(int, int, int)>();
                foreach (var t in w) triples.Add(Sorted(t[0], t[1], t[2]));
                var outside = new List<int>();
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    if (!triples.Contains(Sorted(t[0], t[1], t[2]))) outside.Add(i);
                }
                if (CheckFastPathForTests) {
                    // the lemma's conclusion directly: no used vertex strictly inside a circumcircle of W
                    foreach (var t in w)
                        foreach (int id in used)
                            if (id != t[0] && id != t[1] && id != t[2] && InCircle(t[0], t[1], t[2], id) > 0)
                                FastPathDiffers("vertex " + id + " inside the circumcircle of the witness triangle " + t[0] + " " + t[1] + " " + t[2]);
                }
                return outside;
            }

            static (int, int, int) Sorted(int a, int b, int c)
            {
                if (a > b) (a, b) = (b, a);
                if (b > c) (b, c) = (c, b);
                if (a > b) (a, b) = (b, a);
                return (a, b, c);
            }

            // ------------------------------------------------------------ building W

            /// <summary> half-edge 3t + k runs from V[3t + k] to V[3t + (k + 1) % 3]; Twin[3t + k] is the opposite half-edge; -1 is the vertex
            /// at infinity (a triangle with it is a ghost: its finite edge is a hull edge, the outside on its left) </summary>
            sealed class Tri
            {
                public readonly List<int> V = new List<int>(), Twin = new List<int>();
                public readonly List<bool> Dead = new List<bool>();
                public readonly Stack<int> Free = new Stack<int>();

                public int New(int a, int b, int c)
                {
                    int t;
                    if (Free.Count > 0) { t = Free.Pop(); Dead[t] = false; V[3 * t] = a; V[3 * t + 1] = b; V[3 * t + 2] = c; }
                    else { t = Dead.Count; Dead.Add(false); V.Add(a); V.Add(b); V.Add(c); Twin.Add(-1); Twin.Add(-1); Twin.Add(-1); }
                    return t;
                }

                public void Link(int e, int f) { Twin[e] = f; Twin[f] = e; }
                public bool Ghost(int t) => V[3 * t] < 0 || V[3 * t + 1] < 0 || V[3 * t + 2] < 0;
            }

            const int Infinite = -1;

            /// <summary> p strictly in the conflict region of triangle t: inside the circumcircle of a real triangle; for a ghost, strictly
            /// left of its finite edge, or on that edge's open segment </summary>
            bool Conflict(Tri tr, int t, int p)
            {
                int a = tr.V[3 * t], b = tr.V[3 * t + 1], c = tr.V[3 * t + 2];
                if (a >= 0 && b >= 0 && c >= 0) return InCircle(a, b, c, p) > 0;
                // the finite edge, in the triangle's direction
                int x, y;
                if (a < 0) { x = b; y = c; } else if (b < 0) { x = c; y = a; } else { x = a; y = b; }
                int s = OrientV(x, y, p);
                if (s != 0) return s > 0;
                return StrictlyBetween(x, y, p);
            }

            /// <summary> p on the open segment x-y, given that the three are collinear (doubles compared exactly) </summary>
            bool StrictlyBetween(int x, int y, int p)
            {
                double px = o.VX[p], py = o.VY[p];
                bool inX = o.VX[x] != o.VX[y] ? (Math.Min(o.VX[x], o.VX[y]) < px && px < Math.Max(o.VX[x], o.VX[y])) : px == o.VX[x];
                bool inY = o.VY[x] != o.VY[y] ? (Math.Min(o.VY[x], o.VY[y]) < py && py < Math.Max(o.VY[x], o.VY[y])) : py == o.VY[x];
                return inX && inY && !(px == o.VX[x] && py == o.VY[x]) && !(px == o.VX[y] && py == o.VY[y]);
            }

            List<int[]> BuildWitness(int[] used, out string why)
            {
                why = null;
                int n = used.Length;
                if (n < 3) { why = "fewer than three vertices"; return null; }
                var order = InsertionOrder(used);
                // the first triangle: two distinct points and a third off their line
                int a = order[0], b = -1, c = -1;
                foreach (int id in order) if (o.VX[id] != o.VX[a] || o.VY[id] != o.VY[a]) { b = id; break; }
                if (b < 0) { why = "all vertices coincide"; return null; }
                foreach (int id in order) if (OrientV(a, b, id) != 0) { c = id; break; }
                if (c < 0) { why = "all vertices on a line"; return null; }
                if (OrientV(a, b, c) < 0) (b, c) = (c, b);
                var tr = new Tri();
                int t0 = tr.New(a, b, c);
                int g0 = tr.New(b, a, Infinite), g1 = tr.New(c, b, Infinite), g2 = tr.New(a, c, Infinite);
                tr.Link(3 * t0, 3 * g0); tr.Link(3 * t0 + 1, 3 * g1); tr.Link(3 * t0 + 2, 3 * g2);
                // ghosts around the hull: (b, a, inf): edges b->a, a->inf, inf->b
                tr.Link(3 * g0 + 1, 3 * g2 + 2);   // a->inf with inf->a
                tr.Link(3 * g0 + 2, 3 * g1 + 1);   // inf->b with b->inf
                tr.Link(3 * g1 + 2, 3 * g2 + 1);   // inf->c with c->inf
                int last = t0;
                long guard = 0, budget = 400L * n + 10000;
                var cavity = new List<int>(); var inCavity = new HashSet<int>(); var stack = new Stack<int>();
                var startAt = new Dictionary<int, int>();
                foreach (int p in order) {
                    if (p == a || p == b || p == c) continue;
                    // locate: a visibility walk with a rotating first edge; a ghost reached is in conflict (p outside the hull)
                    int t = last, step = 0;
                    while (true) {
                        if (++guard > budget) { why = "walk budget exceeded"; return null; }
                        if (tr.Ghost(t)) break;
                        int moved = -1;
                        for (int j = 0; j < 3; ++j) {
                            int k = (step + j) % 3;
                            int x = tr.V[3 * t + k], y = tr.V[3 * t + (k + 1) % 3];
                            if (OrientV(x, y, p) < 0) { moved = 3 * t + k; break; }
                        }
                        ++step;
                        if (moved < 0) break;
                        t = tr.Twin[moved] / 3;
                    }
                    if (!tr.Ghost(t)) {
                        for (int k = 0; k < 3; ++k) {
                            int q = tr.V[3 * t + k];
                            if (o.VX[q] == o.VX[p] && o.VY[q] == o.VY[p]) { why = "two vertices coincide"; return null; }
                        }
                    }
                    if (!Conflict(tr, t, p)) { why = "located triangle not in conflict"; return null; }
                    // the cavity: the triangles in conflict, connected to t
                    cavity.Clear(); inCavity.Clear(); stack.Clear();
                    cavity.Add(t); inCavity.Add(t); stack.Push(t);
                    while (stack.Count > 0) {
                        int u = stack.Pop();
                        for (int k = 0; k < 3; ++k) {
                            int w = tr.Twin[3 * u + k] / 3;
                            if (inCavity.Contains(w) || !Conflict(tr, w, p)) continue;
                            inCavity.Add(w); cavity.Add(w); stack.Push(w);
                            if (++guard > budget) { why = "cavity budget exceeded"; return null; }
                        }
                    }
                    // the new triangles: one per boundary half-edge x->y of the cavity, (x, y, p)
                    startAt.Clear();
                    var created = new List<(int tri, int x, int y)>();
                    foreach (int u in cavity)
                        for (int k = 0; k < 3; ++k) {
                            int e = 3 * u + k, f = tr.Twin[e];
                            if (inCavity.Contains(f / 3)) continue;
                            int x = tr.V[e], y = tr.V[3 * u + (k + 1) % 3];
                            created.Add((-1, x, y));
                        }
                    foreach (int u in cavity) { tr.Dead[u] = true; }
                    var outerTwin = new List<int>();
                    foreach (int u in cavity)
                        for (int k = 0; k < 3; ++k) { int f = tr.Twin[3 * u + k]; if (!inCavity.Contains(f / 3)) outerTwin.Add(f); }
                    foreach (int u in cavity) tr.Free.Push(u);
                    for (int i = 0; i < created.Count; ++i) {
                        var (_, x, y) = created[i];
                        int nt = tr.New(x, y, p);
                        created[i] = (nt, x, y);
                        tr.Link(3 * nt, outerTwin[i]);
                        if (startAt.ContainsKey(x)) { why = "cavity boundary not a simple cycle"; return null; }
                        startAt[x] = nt;
                    }
                    // y->p of (x, y, p) pairs with p->y of the triangle starting at y
                    foreach (var (nt, x, y) in created) {
                        if (!startAt.TryGetValue(y, out int other)) { why = "cavity boundary not closed"; return null; }
                        tr.Link(3 * nt + 1, 3 * other + 2);
                    }
                    last = -1;
                    foreach (var (nt, x, y) in created) if (!tr.Ghost(nt)) { last = nt; break; }
                    if (last < 0) { why = "no real triangle after an insertion"; return null; }
                }
                var result = new List<int[]>();
                for (int t = 0; t < tr.Dead.Count; ++t)
                    if (!tr.Dead[t] && !tr.Ghost(t)) result.Add(new[] { tr.V[3 * t], tr.V[3 * t + 1], tr.V[3 * t + 2] });
                return result;
            }

            /// <summary>
            /// The insertion order: rounds of doubling size over a fixed pseudo-random permutation (so each round is a random sample, as
            /// in randomized incremental construction), each round sorted along a Morton curve of the coordinates' ranks (so the walk
            /// from the last insertion is short). Deterministic
            /// </summary>
            int[] InsertionOrder(int[] used)
            {
                int n = used.Length;
                var perm = (int[])used.Clone();
                Array.Sort(perm);
                var rnd = new Random(20261104);
                for (int i = n - 1; i > 0; --i) { int j = rnd.Next(i + 1); (perm[i], perm[j]) = (perm[j], perm[i]); }
                // ranks of the coordinates (doubles compare exactly), 16 bits each
                var byX = (int[])perm.Clone(); Array.Sort(byX, (p, q) => o.VX[p].CompareTo(o.VX[q]));
                var byY = (int[])perm.Clone(); Array.Sort(byY, (p, q) => o.VY[p].CompareTo(o.VY[q]));
                var rx = new Dictionary<int, int>(n); var ry = new Dictionary<int, int>(n);
                for (int i = 0; i < n; ++i) { rx[byX[i]] = (int)((long)i * 65535 / Math.Max(1, n - 1)); ry[byY[i]] = (int)((long)i * 65535 / Math.Max(1, n - 1)); }
                ulong Morton(int id)
                {
                    ulong code = 0;
                    for (int bit = 0; bit < 16; ++bit) code |= (ulong)((rx[id] >> bit) & 1) << (2 * bit) | (ulong)((ry[id] >> bit) & 1) << (2 * bit + 1);
                    return code;
                }
                var order = new int[n];
                int start = 0, size = 1;
                while (start < n) {
                    int end = Math.Min(n, start + size);
                    var round = perm.Skip(start).Take(end - start).OrderBy(Morton).ThenBy(id => id).ToArray();
                    Array.Copy(round, 0, order, start, round.Length);
                    start = end; size *= 2;
                }
                return order;
            }

            // ------------------------------------------------------------ the certificate

            /// <summary> a hash of the edge key (x &lt;&lt; 32) | y that mixes all bits: long's own hash is x ^ y, which collides for every
            /// pair with the same xor (on the Herne tiles the certificate took 220 s with it, against well under a second). The SplitMix64
            /// finalizer (Steele, Lea and Flood 2014) </summary>
            sealed class MixedLong : IEqualityComparer<long>
            {
                public static readonly MixedLong Instance = new MixedLong();
                public bool Equals(long a, long b) => a == b;
                public int GetHashCode(long key)
                {
                    ulong z = (ulong)key + 0x9E3779B97F4A7C15UL;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return (int)(z ^ (z >> 31));
                }
            }

            /// <summary>
            /// (a) W is a triangulation of the convex hull of U and (b) every interior edge of W is locally Delaunay, checked from W's
            /// triangles alone (MESHING2D_FAST_ORACLE.md 6.2): positive triangles; every edge in at most two triangles, in opposite
            /// directions; the edges in one triangle, followed from a hull corner, run once around the hull's boundary, each on a hull edge
            /// and forward, and are all reached (the boundary is the hull's, once, so W covers the hull once; linear in the boundary);
            /// every vertex of U in W; then the in-circle sign of every interior edge's fourth vertex at most 0
            /// </summary>
            bool CertifyWitness(List<int[]> w, int[] used, out string why)
            {
                why = null;
                var directed = new Dictionary<long, int>(3 * w.Count, MixedLong.Instance);
                long D(int x, int y) => ((long)x << 32) | (uint)y;
                var vertices = new HashSet<int>();
                for (int i = 0; i < w.Count; ++i) {
                    var t = w[i];
                    if (OrientV(t[0], t[1], t[2]) <= 0) { why = "a witness triangle is not positive"; return false; }
                    for (int k = 0; k < 3; ++k) {
                        vertices.Add(t[k]);
                        long key = D(t[k], t[(k + 1) % 3]);
                        if (directed.ContainsKey(key)) { why = "a directed edge in two witness triangles"; return false; }
                        directed[key] = 3 * i + k;
                    }
                }
                if (vertices.Count != used.Length || used.Any(id => !vertices.Contains(id))) { why = "a used vertex is not in the witness"; return false; }
                var hull = HullOf(used);
                if (hull == null) { why = "no hull"; return false; }
                // the boundary edges, by their start (a vertex starting two runs the boundary through it twice)
                var next = new Dictionary<int, int>();
                foreach (var pair in directed) {
                    int x = (int)(pair.Key >> 32), y = (int)(uint)pair.Key;
                    if (directed.ContainsKey(D(y, x))) {
                        // an interior edge, once: locally Delaunay against the other triangle's fourth vertex
                        if (x > y) continue;
                        int e = pair.Value, f = directed[D(y, x)];
                        int c = w[e / 3][(e % 3 + 2) % 3], d = w[f / 3][(f % 3 + 2) % 3];
                        if (InCircle(x, y, c, d) > 0) { why = "a witness edge is not locally Delaunay"; return false; }
                        continue;
                    }
                    if (next.ContainsKey(x)) { why = "the witness boundary does not wind once around the hull"; return false; }
                    next[x] = y;
                }
                // followed from the first hull corner, the boundary runs along the hull's edges in order, each boundary edge on the current
                // hull edge, forward, until it is back at the start after the last hull edge; and that walk takes every boundary edge.
                // The boundary is then the hull's boundary, once (linear in the boundary; exact: orientations and coordinates compared)
                int start = hull[0], at = start, side = 0, walked = 0;
                while (true) {
                    if (!next.TryGetValue(at, out int to) || walked >= next.Count) { why = "a witness boundary edge is not on the hull"; return false; }
                    int a = hull[side], b = hull[(side + 1) % hull.Count];
                    if (OrientV(a, b, to) != 0 || !(Along(a, b, at) < Along(a, b, to) && Along(a, b, to) <= Along(a, b, b))) { why = "a witness boundary edge is not on the hull"; return false; }
                    ++walked;
                    if (Along(a, b, to) == Along(a, b, b)) {
                        if (to != b) { why = "a witness boundary edge is not on the hull"; return false; }
                        ++side;
                    }
                    at = to;
                    if (side == hull.Count) break;
                }
                if (at != start || walked != next.Count) { why = "a witness boundary edge is not on the hull"; return false; }
                return true;
            }

            /// <summary> the position of vertex v along the hull edge a-b, v on its line: the coordinate in which a and b differ, signed so
            /// that it grows from a to b (doubles, exact) </summary>
            double Along(int a, int b, int v) => o.VX[a] != o.VX[b] ? (o.VX[b] > o.VX[a] ? o.VX[v] : -o.VX[v]) : (o.VY[b] > o.VY[a] ? o.VY[v] : -o.VY[v]);

            /// <summary> the convex hull of the used vertices by the monotone chain (doubles sorted exactly, the filtered exact orientation),
            /// counter-clockwise, strictly convex corners only; null when it has fewer than three corners </summary>
            List<int> HullOf(int[] used)
            {
                var sorted = used.OrderBy(id => o.VX[id]).ThenBy(id => o.VY[id]).ToList();
                var unique = new List<int>();
                foreach (int id in sorted)
                    if (unique.Count == 0 || o.VX[unique[unique.Count - 1]] != o.VX[id] || o.VY[unique[unique.Count - 1]] != o.VY[id]) unique.Add(id);
                if (unique.Count < 3) return null;
                var hull = new List<int>();
                for (int pass = 0; pass < 2; ++pass) {
                    int start = hull.Count;
                    for (int i = 0; i < unique.Count; ++i) {
                        int p = unique[pass == 0 ? i : unique.Count - 1 - i];
                        while (hull.Count >= start + 2 && OrientV(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                        hull.Add(p);
                    }
                    hull.RemoveAt(hull.Count - 1);
                }
                return hull.Count >= 3 ? hull : null;
            }
        }
    }
}
