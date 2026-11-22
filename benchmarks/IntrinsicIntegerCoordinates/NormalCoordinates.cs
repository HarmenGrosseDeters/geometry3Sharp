// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Prototype (study only, not library code): integer normal coordinates and roundabouts (Gillespie, Sharp and
// Crane, "Integer Coordinates for Intrinsic Geometry Processing", 2021; implemented from the paper) as a
// layer on top of gs.IntrinsicTriangulation. The library triangulation supplies the connectivity and the
// edge lengths and executes the flips and insertions (with its signposts); this layer keeps, per halfedge,
//   n: how often the input edges cross the intrinsic edge (-1: the edge lies on an input edge), and
//   r: at input vertices, the index (counter-clockwise around the tail) of the first input edge at or
//      after the halfedge,
// and extracts the common subdivision from the integers alone. Positions come afterwards, from the lengths.
using System;
using System.Collections.Generic;
using g3;
using gs;

class NormalCoordinates
{
    public readonly IntrinsicTriangulation T;
    public int[] n;                 // per halfedge slot, equal on twins
    public int[] r;                 // per halfedge slot (meaningful if the tail is an input vertex)
    readonly int N0;
    readonly int[][] inFan;         // per input vertex: heads of its input edges, counter-clockwise
    readonly bool[] inBoundary;
    public readonly int InputEdgeCount;
    public long MaxN;

    static int Next(int h) { return IntrinsicTriangulation.Next(h); }
    static int Prev(int h) { return IntrinsicTriangulation.Prev(h); }
    static int Pos(int x) { return x > 0 ? x : 0; }
    static int Neg(int x) { return x < 0 ? -x : 0; }

    /// <summary> T must be fresh (no flips yet): every intrinsic edge is an input edge </summary>
    public NormalCoordinates(IntrinsicTriangulation t)
    {
        T = t;
        N0 = T.InputVertexCount;
        int H = T.HalfedgeCount;
        n = new int[H];
        r = new int[H];
        inFan = new int[N0][];
        inBoundary = new bool[N0];
        int edges = 0;
        for (int h = 0; h < H; ++h) { n[h] = -1; if (T.Twin(h) < 0 || T.Twin(h) > h) edges++; }
        InputEdgeCount = edges;
        var fan = new List<int>();
        for (int v = 0; v < N0; ++v) {
            fan.Clear();
            int h0 = T.VertexHalfedge(v), h = h0;
            while (true) {
                r[h] = fan.Count;
                fan.Add(T.Head(h));
                int nh = T.NextAroundVertex(h);
                if (nh < 0) { fan.Add(T.Tail(Prev(h))); inBoundary[v] = true; break; }     // the other boundary edge
                if (nh == h0) break;
                h = nh;
            }
            inFan[v] = fan.ToArray();
        }
    }

    void Grow()
    {
        int H = T.HalfedgeCount;
        if (H > n.Length) { Array.Resize(ref n, Math.Max(H, 2 * n.Length)); Array.Resize(ref r, n.Length); }
    }

    // arcs that start at the tail of h and cross the opposite edge of its triangle (paper eq. 1)
    int Emanating(int h)
    {
        return Pos(Pos(n[Next(h)]) - Pos(n[h]) - Pos(n[Prev(h)]));
    }

    // arcs that cut off the corner at the tail of h (paper eq. 2)
    int Corner(int h)
    {
        int a = Pos(n[h]), b = Pos(n[Next(h)]), c = Pos(n[Prev(h)]);
        // emanating counts of the other two corners: head of h (opposite Prev(h)), and tail of Prev(h) (opposite h)
        int eHead = Pos(c - a - b), eApex = Pos(a - b - c);
        return (Pos(a + c - b) - eHead - eApex) / 2;
    }

    int WrapR(int v, int x)
    {
        if (v >= N0) return 0;
        if (inBoundary[v]) return x;
        int d = inFan[v].Length;
        x %= d;
        return x < 0 ? x + d : x;
    }

    /// <summary>
    /// Flips the edge of h in the library triangulation and updates the integers (paper eqs. 3 and 4).
    /// Returns false (nothing changed) if the library refuses the flip.
    /// </summary>
    public bool Flip(int h)
    {
        int ta = T.Twin(h);
        if (ta < 0) return false;
        int hb = Next(h), hc = Prev(h), tb = Next(ta), tc = Prev(ta);
        // before: (h, hb, hc) = (i->j, j->k, k->i), (ta, tb, tc) = (j->i, i->l, l->j)
        int nij = n[h], njk = n[hb], nki = n[hc], nil = n[tb], nlj = n[tc];
        int rjk = r[hb], rki = r[hc], ril = r[tb], rlj = r[tc];
        // corner and emanating counts of both triangles before the flip
        int cK = Corner(hc), cIup = Corner(h), cJup = Corner(hb);
        int cL = Corner(tc), cJlo = Corner(ta), cIlo = Corner(tb);
        int eK = Emanating(hc), eIup = Emanating(h), eJup = Emanating(hb);
        int eL = Emanating(tc), eJlo = Emanating(ta), eIlo = Emanating(tb);
        if (T.FlipEdge(h) == false) return false;
        // all terms doubled, so that the halves stay integers
        long twice = 2L * cL + 2L * cK + Math.Abs((long)cJlo - cJup) + Math.Abs((long)cIlo - cIup) - eL - eK
            + 2L * ((long)eIlo + eIup + eJlo + eJup) + 2 * Neg(nij);
        if ((twice & 1) != 0 || twice / 2 > int.MaxValue) throw new InvalidOperationException("normal coordinates: odd flip count or overflow");
        int nkl = (int)(twice / 2);
        // after: (h, hb, hc) = (k->l, l->j, j->k), (ta, tb, tc) = (l->k, k->i, i->l)
        n[h] = nkl; n[ta] = nkl;
        n[hb] = nlj; r[hb] = rlj;
        n[hc] = njk; r[hc] = rjk;
        n[tb] = nki; r[tb] = rki;
        n[tc] = nil; r[tc] = ril;
        // k->l follows k->i counter-clockwise around k, l->k follows l->j around l
        r[h] = WrapR(T.Tail(h), rki + Emanating(tb) + Neg(nki));
        r[ta] = WrapR(T.Tail(ta), rlj + Emanating(hb) + Neg(nlj));
        if (nkl > MaxN) MaxN = nkl;
        return true;
    }

    /// <summary> Lawson flips in the order of the library's FlipToDelaunay; returns the number of flips </summary>
    public int FlipToDelaunay(bool integers = true)
    {
        var queue = new Queue<int>();
        var queued = new HashSet<int>();
        int H = T.HalfedgeCount;
        for (int h = 0; h < H; ++h)
            if (T.Twin(h) > h) { queued.Add(h); queue.Enqueue(h); }
        int flips = 0;
        long maxFlips = 100L * H + 1000;
        while (queue.Count > 0 && flips < maxFlips) {
            int h = queue.Dequeue();
            queued.Remove(h);
            int t = T.Twin(h);
            if (t < 0) continue;
            if (T.IsDelaunay(h)) continue;
            if ((integers ? Flip(h) : T.FlipEdge(h)) == false) continue;
            flips++;
            for (int k = 0; k < 4; ++k) {
                int o = k == 0 ? Next(h) : k == 1 ? Prev(h) : k == 2 ? Next(t) : Prev(t);
                int ot = T.Twin(o);
                if (ot < 0) continue;
                int rep = Math.Min(o, ot);
                if (queued.Add(rep)) queue.Enqueue(rep);
            }
        }
        return flips;
    }


    // ------------------------------------------------------------------ tracing (integers only)

    public struct Crossing
    {
        public int H;       // the halfedge the curve leaves a triangle through
        public int P;       // index of the crossing on H, counted from its tail
    }

    // The curve enters the triangle of h through crossing p of h: appends the crossings it leaves triangles
    // through until it ends at a vertex (paper algorithm 1). Returns the halfedge whose tail is that vertex,
    // in the last triangle, and the index of the arc among those emanating there.
    int TraceInto(int h, int p, List<Crossing> list, out int fanIndex)
    {
        while (true) {
            int ca = Corner(h);
            if (p < ca) {
                // cuts the corner at the tail: out through Prev(h), same index counted from that corner
                int g = Prev(h);
                int q = n[g] - 1 - p;
                list.Add(new Crossing() { H = g, P = q });
                h = T.Twin(g); p = n[g] - 1 - q;
                continue;
            }
            int cb = Corner(Next(h));
            if (p >= n[h] - cb) {
                int g = Next(h);
                int q = n[h] - 1 - p;
                list.Add(new Crossing() { H = g, P = q });
                h = T.Twin(g); p = n[g] - 1 - q;
                continue;
            }
            fanIndex = p - ca;
            return Prev(h);
        }
    }

    // the whole input edge through crossing p of halfedge h, from one end vertex to the other; index: where
    // (Twin(h), n - 1 - p), the crossing as the curve leaves the other triangle, is in the list
    void CurveThrough(int h, int p, List<Crossing> list, out int index, out int startCorner, out int endCorner)
    {
        var back = new List<Crossing>();
        int tw = T.Twin(h), fi;
        startCorner = TraceInto(tw, n[h] - 1 - p, back, out fi);
        list.Clear();
        for (int k = back.Count - 1; k >= 0; --k) {
            int g = T.Twin(back[k].H);
            list.Add(new Crossing() { H = g, P = n[g] - 1 - back[k].P });
        }
        index = list.Count;
        list.Add(new Crossing() { H = tw, P = n[h] - 1 - p });
        endCorner = TraceInto(h, p, list, out fi);
    }

    // Unfolds the triangles a curve crosses into the plane and intersects the straight segment between its end
    // vertices with the crossed edges: per crossing the parameter along its halfedge (from the tail) and along
    // the curve. Floating point; the order of the crossings is known from the integers.
    Vector2d[] ufP = new Vector2d[16], ufQ = new Vector2d[16];
    public void Unfold(List<Crossing> list, double[] tEdge, double[] sCurve)
    {
        int m = list.Count;
        if (ufP.Length < m) { ufP = new Vector2d[2 * m]; ufQ = new Vector2d[2 * m]; }
        int g = list[0].H;
        Vector2d P = Vector2d.Zero, Q = new Vector2d(T.Length(g), 0);
        Vector2d A = Third(P, Q, T.Length(Prev(g)), T.Length(Next(g)), +1);
        Vector2d B = A;
        for (int k = 0; k < m; ++k) {
            g = list[k].H;
            ufP[k] = P; ufQ[k] = Q;
            int tw = T.Twin(g);
            Vector2d R = Third(P, Q, T.Length(Next(tw)), T.Length(Prev(tw)), -1);
            if (k + 1 < m) {
                int g2 = list[k + 1].H;
                if (g2 == Next(tw)) Q = R; else P = R;
            } else
                B = R;
        }
        Vector2d d = B - A;
        for (int k = 0; k < m; ++k) {
            Vector2d e = ufQ[k] - ufP[k], ap = A - ufP[k];
            double den = e.x * d.y - e.y * d.x;
            tEdge[k] = (ap.x * d.y - ap.y * d.x) / den;
            sCurve[k] = (ap.x * e.y - ap.y * e.x) / den;
        }
    }

    // third corner of a triangle on the edge P -> Q, at distance lp from P and lq from Q, left (+1) or right (-1)
    static Vector2d Third(Vector2d P, Vector2d Q, double lp, double lq, int side)
    {
        Vector2d e = Q - P;
        double d = e.Length;
        Vector2d u = e / d, nrm = new Vector2d(-u.y, u.x);
        double x = (lp * lp - lq * lq + d * d) / (2 * d);
        double y2 = lp * lp - x * x;
        double y = y2 > 0 ? Math.Sqrt(y2) : 0;
        return P + x * u + (side * y) * nrm;
    }


    // ------------------------------------------------------------------ common subdivision

    public class Subdivision
    {
        public bool Valid;
        public string Error;
        public int Crossings;               // subdivision vertices that are not triangulation vertices
        public int Faces;
        public int Curves, SharedEdges;
        public int LongestCurve;
        public int OutOfOrder;              // crossings whose computed position contradicts the integer order (clamped)
        public List<Vector3d> Points;       // triangulation vertices, then the crossings
        public List<int[]> Polygons;        // corners counter-clockwise, ids into Points
        public List<int> PolygonTriangle;   // intrinsic triangle of each polygon
    }

    /// <summary>
    /// The common subdivision from the integers: every input edge is traced through the intrinsic triangles
    /// (connectivity; checked: each ends at the vertex its roundabout names, and every crossing and every input
    /// edge is used exactly once), then positioned on its input edge by unfolding, and every intrinsic triangle
    /// is cut along its arcs into polygons. positions == false: connectivity and counts only.
    /// </summary>
    public Subdivision Extract(bool positions = true)
    {
        var S = new Subdivision();
        int H = T.HalfedgeCount, N = T.VertexCount;
        // ids of the crossings: per edge (the halfedge with the smaller index) a block of n ids
        int[] baseId = new int[H];
        int total = 0;
        for (int h = 0; h < H; ++h) {
            int tw = T.Twin(h);
            if (n[h] < -1) { S.Error = "normal coordinate below -1 at " + h; return S; }
            if (tw >= 0 && n[tw] != n[h]) { S.Error = "normal coordinates of twins differ at " + h; return S; }
            if (((Pos(Pos(n[h]) + Pos(n[Prev(h)]) - Pos(n[Next(h)])) - Emanating(Next(h)) - Emanating(Prev(h))) & 1) != 0) { S.Error = "odd corner count at " + h; return S; }
            if (tw >= 0 && tw < h) continue;
            if (tw < 0 && n[h] > 0) { S.Error = "boundary edge " + h + " is crossed"; return S; }
            baseId[h] = N + total;
            total += Pos(n[h]);
        }
        for (int h = 0; h < H; ++h) { int tw = T.Twin(h); if (tw >= 0 && tw < h) baseId[h] = baseId[tw]; }
        S.Crossings = total;
        if (positions) {
            S.Points = new List<Vector3d>(N + total);
            for (int v = 0; v < N; ++v) S.Points.Add(T.VertexPosition(v));
            for (int k = 0; k < total; ++k) S.Points.Add(Vector3d.Zero);
        }
        // fans: arcs that start at corners
        int[] fanBase = new int[H + 1];
        for (int h = 0; h < H; ++h) fanBase[h + 1] = fanBase[h] + Emanating(h);
        bool[] done = new bool[fanBase[H]];
        bool[] used = new bool[total];
        var list = new List<Crossing>();
        double[] tE = new double[16], sC = new double[16];
        for (int h = 0; h < H; ++h) {
            int v = T.Tail(h);
            if (n[h] == -1) {
                if (v >= N0) { S.Error = "shared edge at inserted vertex"; return S; }
                if (FanHead(v, r[h]) != T.Head(h)) { S.Error = "roundabout of shared halfedge " + h; return S; }
                if (T.Twin(h) < 0 || T.Twin(h) > h) S.SharedEdges++;
            }
            int e = fanBase[h + 1] - fanBase[h];
            if (e > 0 && v >= N0) { S.Error = "input edge starts at inserted vertex " + v; return S; }
            for (int m = 0; m < e; ++m) {
                if (done[fanBase[h] + m]) continue;
                done[fanBase[h] + m] = true;
                int o = Next(h);
                int p = Corner(o) + m;
                list.Clear();
                list.Add(new Crossing() { H = o, P = p });
                int fi;
                int end = TraceInto(T.Twin(o), n[o] - 1 - p, list, out fi);
                if (done[fanBase[end] + fi]) { S.Error = "input edge traced twice"; return S; }
                done[fanBase[end] + fi] = true;
                int w = T.Tail(end);
                // the roundabouts name the input edge at both ends
                int ra = r[h] + Neg(n[h]) + m, rb = w < N0 ? r[end] + Neg(n[end]) + fi : -1;
                if (w >= N0 || FanHead(v, ra) != w || FanHead(w, rb) != v) {
                    S.Error = "input edge from vertex " + v + " ends at the wrong vertex " + w; return S;
                }
                S.Curves++;
                if (list.Count > S.LongestCurve) S.LongestCurve = list.Count;
                foreach (Crossing c in list) {
                    int id = Id(baseId, c) - N;
                    if (used[id]) { S.Error = "crossing used twice"; return S; }
                    used[id] = true;
                }
                if (positions) {
                    if (tE.Length < list.Count) { tE = new double[2 * list.Count]; sC = new double[2 * list.Count]; }
                    Unfold(list, tE, sC);
                    Vector3d a = T.VertexPosition(v), b = T.VertexPosition(w);
                    double lo = 0;
                    for (int k = 0; k < list.Count; ++k) {
                        double s = sC[k];
                        if (!(s > lo && s < 1) || !(tE[k] > 0 && tE[k] < 1)) S.OutOfOrder++;
                        if (!(s >= lo)) s = lo;
                        if (s > 1) s = 1;
                        lo = s;
                        S.Points[Id(baseId, list[k])] = a + s * (b - a);
                    }
                }
            }
        }
        for (int k = 0; k < total; ++k) if (used[k] == false) { S.Error = "crossing on no input edge"; return S; }
        if (S.Curves + S.SharedEdges != InputEdgeCount) { S.Error = (S.Curves + S.SharedEdges) + " of " + InputEdgeCount + " input edges found"; return S; }

        // polygons: every arc cuts one region of its triangle in two
        if (positions) { S.Polygons = new List<int[]>(); S.PolygonTriangle = new List<int>(); }
        var ring = new List<int>();
        var apexSplit = new List<int>();
        for (int t = 0; t < H / 3; ++t) {
            int arcs = 0;
            for (int k = 0; k < 3; ++k) arcs += Corner(3 * t + k) + Emanating(3 * t + k);
            S.Faces += 1 + arcs;
            if (positions == false) continue;
            // corner bands, and the ring of the middle region
            ring.Clear(); apexSplit.Clear();
            int fanCorner = -1;
            for (int k = 0; k < 3; ++k) {
                int h = 3 * t + k, g = Prev(h);
                int c = Corner(h);
                // arc q of this corner: crossing q of h (from the tail) and crossing n - 1 - q of g
                for (int q = 0; q < c; ++q) {
                    int x1 = Id(baseId, new Crossing() { H = h, P = q }), y1 = Id(baseId, new Crossing() { H = g, P = n[g] - 1 - q });
                    if (q == 0) S.Polygons.Add(new[] { T.Tail(h), x1, y1 });
                    else {
                        int x0 = Id(baseId, new Crossing() { H = h, P = q - 1 }), y0 = Id(baseId, new Crossing() { H = g, P = n[g] - q });
                        S.Polygons.Add(new[] { x0, x1, y1, y0 });
                    }
                    S.PolygonTriangle.Add(t);
                }
                if (Emanating(h) > 0) fanCorner = k;
            }
            for (int k = 0; k < 3; ++k) {
                int h = 3 * t + k, g = Prev(h);
                int c = Corner(h);
                if (c == 0) { if (k == fanCorner) apexSplit.Add(ring.Count); ring.Add(T.Tail(h)); }
                else {
                    ring.Add(Id(baseId, new Crossing() { H = g, P = n[g] - c }));
                    ring.Add(Id(baseId, new Crossing() { H = h, P = c - 1 }));
                }
                // the fan of the corner opposite h crosses h between the corner arcs of its ends
                int e = Emanating(g);
                for (int m = 0; m < e; ++m) ring.Add(Id(baseId, new Crossing() { H = h, P = c + m }));
            }
            if (fanCorner < 0) { S.Polygons.Add(ring.ToArray()); S.PolygonTriangle.Add(t); }
            else {
                // ring, rotated to start at the apex A: A, (rest of the boundary) with the fan ends f_1 .. f_e in it
                int a0 = apexSplit[0], cnt = ring.Count;
                int e = Emanating(3 * t + fanCorner);
                // the fan ends are the e ring entries after the entries of the next corner
                int nextCorner = 3 * t + (fanCorner + 1) % 3;
                int skip = 1 + (Corner(nextCorner) == 0 ? 1 : 2);
                int apex = ring[a0];
                int start = 1;
                for (int m = 0; m <= e; ++m) {
                    int stop = (m < e) ? skip + m : cnt - 1;          // ring offset (from the apex) of the last corner of this piece
                    var poly = new List<int>() { apex };
                    for (int o = start; o <= stop; ++o) poly.Add(ring[(a0 + o) % cnt]);
                    S.Polygons.Add(poly.ToArray()); S.PolygonTriangle.Add(t);
                    start = stop;
                }
            }
        }
        if (positions && S.Polygons.Count != S.Faces) { S.Error = "polygon count " + S.Polygons.Count + " != " + S.Faces; return S; }
        S.Valid = true;
        return S;
    }

    int FanHead(int v, int x) { int k = WrapShared(v, x); return k < 0 ? -1 : inFan[v][k]; }

    // index into inFan[v]; an out-of-range roundabout at a boundary vertex gives -1
    int WrapShared(int v, int x)
    {
        int d = inFan[v].Length;
        if (inBoundary[v]) return (x >= 0 && x < d) ? x : -1;
        x %= d;
        return x < 0 ? x + d : x;
    }

    int Id(int[] baseId, Crossing c)
    {
        int tw = T.Twin(c.H);
        return (tw >= 0 && tw < c.H) ? baseId[c.H] + n[c.H] - 1 - c.P : baseId[c.H] + c.P;
    }


    // ------------------------------------------------------------------ insertion

    // Chord of the arc through crossing p of halfedge h (entering its triangle) in the layout q of that
    // triangle: its two end points. Needs the whole curve (unfolding), as in the paper's algorithm 5.
    void ArcChord(int h, int p, Vector2d[] q, out Vector2d X, out Vector2d Y)
    {
        var list = new List<Crossing>();
        int idx, sc, ec;
        CurveThrough(h, p, list, out idx, out sc, out ec);
        double[] tE = new double[list.Count], sC = new double[list.Count];
        Unfold(list, tE, sC);
        int t = h / 3;
        X = OnEdge(q, h - 3 * t, 1 - Clamp01(tE[idx]));
        if (idx + 1 < list.Count) {
            int g = list[idx + 1].H;
            Y = OnEdge(q, g - 3 * t, Clamp01(tE[idx + 1]));
        } else
            Y = q[(Prev(h)) - 3 * t];       // ends at the apex
    }
    static double Clamp01(double x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
    static Vector2d OnEdge(Vector2d[] q, int k, double t) { return q[k] + t * (q[(k + 1) % 3] - q[k]); }
    static double Orient(Vector2d a, Vector2d b, Vector2d c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

    public int ChordQueries;

    /// <summary>
    /// Inserts a vertex into triangle t at point P of its layout (library InsertVertex) and gives the three
    /// new edges their normal coordinates: the region of P between the arcs of t is found with floating-point
    /// side tests (binary search over the nested arcs), the coordinates follow from the region. Any region is
    /// a valid choice, so rounding can only move the point across an arc, not break the integers.
    /// Returns the new vertex, -1 if the library refuses.
    /// </summary>
    public int Insert(int t, Vector2d P)
    {
        var q = new Vector2d[3];
        T.GetTriangleLayout(t, out q[0], out q[1], out q[2]);
        int[] c = new int[3], e = new int[3], nn = new int[3], rr = new int[3];
        for (int k = 0; k < 3; ++k) { c[k] = Corner(3 * t + k); e[k] = Emanating(3 * t + k); nn[k] = n[3 * t + k]; rr[k] = r[3 * t + k]; }
        // a[k]: how many of the corner arcs at corner k lie between the corner and P
        int[] a = new int[3];
        int inBand = -1;
        for (int k = 0; k < 3 && inBand < 0; ++k) {
            int h = 3 * t + k;
            int lo = 0, hi = c[k];                      // arcs [0, lo) separate, arcs [hi, c) do not
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                Vector2d X, Y;
                ArcChord(h, mid, q, out X, out Y); ChordQueries++;
                bool separates = Orient(X, Y, P) * Orient(X, Y, q[k]) < 0;
                if (separates) lo = mid + 1; else hi = mid;
            }
            a[k] = lo;
            if (lo < c[k]) inBand = k;
        }
        int[] np = new int[3];                          // crossings of the segment from P to corner k
        if (inBand >= 0) {
            int k = inBand, rest = c[k] - a[k];
            for (int j = 0; j < 3; ++j) np[j] = (j == k) ? a[k] : c[j] + rest + FanBetween(e, k, j);
        } else {
            int fan = e[0] > 0 ? 0 : e[1] > 0 ? 1 : e[2] > 0 ? 2 : -1;
            for (int j = 0; j < 3; ++j) np[j] = c[j];
            if (fan >= 0) {
                // the fan of corner 'fan' crosses the opposite halfedge o; m: fan arcs between corner fan+1 and P
                int o = 3 * t + (fan + 1) % 3, k1 = (fan + 1) % 3, k2 = (fan + 2) % 3;
                int lo = 0, hi = e[fan];
                while (lo < hi) {
                    int mid = (lo + hi) / 2;
                    Vector2d X, Y;
                    ArcChord(o, c[k1] + mid, q, out X, out Y); ChordQueries++;
                    bool separates = Orient(X, Y, P) * Orient(X, Y, q[k1]) < 0;
                    if (separates) lo = mid + 1; else hi = mid;
                }
                np[fan] = 0; np[k1] = c[k1] + lo; np[k2] = c[k2] + e[fan] - lo;
            }
        }
        int f1 = T.TriangleCount, f2 = f1 + 1;
        int v = T.InsertVertex(t, P);
        if (v < 0) return -1;
        Grow();
        // library layout after the insertion: t = (a, b, p), f1 = (b, c, p), f2 = (c, a, p)
        int[] outer = { 3 * t, 3 * f1, 3 * f2 };
        for (int k = 0; k < 3; ++k) {
            int h = outer[k];
            n[h] = nn[k]; r[h] = rr[k];
            int toP = Next(h), fromP = Prev(h);         // head(h) -> p, p -> tail(h)
            n[toP] = np[(k + 1) % 3]; n[fromP] = np[k];
            r[fromP] = 0;
            if (np[k] > MaxN) MaxN = np[k];
        }
        // tail(h) -> p follows h counter-clockwise around the tail; it is the twin of Prev(h)
        for (int k = 0; k < 3; ++k) {
            int h = outer[k];
            int tailToP = T.Twin(Prev(h));
            r[tailToP] = WrapR(T.Tail(h), r[h] + Emanating(h) + Neg(n[h]));
        }
        return v;
    }

    // fan arcs (of whichever corner has a fan) that separate a point in the band of corner k from corner j
    static int FanBetween(int[] e, int k, int j)
    {
        // the fan of corner f separates corner f+1 from corner f+2; a point in the band of f itself cannot exist
        for (int f = 0; f < 3; ++f)
            if (e[f] > 0 && f != k && f != j) return e[f];
        return 0;
    }
}
