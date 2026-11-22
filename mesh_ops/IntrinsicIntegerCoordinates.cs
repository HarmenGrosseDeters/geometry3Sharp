// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    // Integer (normal) coordinates of the intrinsic triangulation: Gillespie, Sharp and Crane, "Integer Coordinates
    // for Intrinsic Geometry Processing", ACM TOG 2021 (implemented from the paper, arXiv:2106.00220v1). An opt-in
    // layer beside the signposts (EnableIntegerCoordinates). Per halfedge it keeps
    //   hN: how many input edges cross the intrinsic edge; -1 if the edge lies on an input edge;
    //   hR: (tail an input vertex) the roundabout: the index, counter-clockwise in the fan of input edges of the
    //       tail, of the first input edge at or after the halfedge.
    // The input edges are curves on the intrinsic triangulation; the counts on the three edges of a triangle fix
    // how they run through it (paper eqs. 1 and 2), so the connectivity of the common subdivision follows from
    // integers alone. The layer covers edge flips (eqs. 3 and 4) and splits of boundary edges (eq. 6); any other
    // insertion switches it off (the extraction then traces, as without the layer).
    public partial class IntrinsicTriangulation
    {
        int[] hN, hR;           // null: the layer is off
        bool changed;           // a flip or an insertion has been made (also one that was undone later)
        int[] inFanStart;       // per input vertex: its range in inFan
        int[] inFan;            // the input halfedges leaving the vertex, counter-clockwise from vHe (the last
                                // boundary edge of a boundary vertex has none: no intrinsic halfedge leaves along it)

        /// <summary> are the integer coordinates on (EnableIntegerCoordinates) and still kept? </summary>
        public bool HasIntegerCoordinates { get { return hN != null; } }

        /// <summary>
        /// Switches on the integer (normal) coordinates (Gillespie, Sharp and Crane 2021): two integers per
        /// halfedge, updated by FlipEdge and by splits of boundary edges, from which ExtractCommonSubdivision gets
        /// the connectivity of the common subdivision without tracing (exact for the triangulation at hand, and
        /// faster). Call it before the first flip or insertion: returns false, and changes nothing, after one.
        /// InsertVertex, a split of an interior edge and the circumcenter insertions of DelaunayRefine are not
        /// covered: they switch the coordinates off for good (HasIntegerCoordinates), and the extraction traces
        /// again. So does a flip whose new edge would be crossed more than int.MaxValue times.
        /// </summary>
        public bool EnableIntegerCoordinates()
        {
            if (hN != null) return true;
            if (changed) return false;
            hN = new int[hV.Length];
            hR = new int[hV.Length];
            inFanStart = new int[N0 + 1];
            var fan = new List<int>();
            for (int v = 0; v < N0; ++v) {
                inFanStart[v] = fan.Count;
                int h0 = vHe[v], h = h0;
                while (true) {
                    hN[h] = -1;
                    hR[h] = fan.Count - inFanStart[v];
                    fan.Add(h);
                    int nh = NextAroundVertex(h);
                    if (nh < 0 || nh == h0) break;
                    h = nh;
                }
            }
            inFanStart[N0] = fan.Count;
            inFan = fan.ToArray();
            return true;
        }

        void drop_integer_coordinates() { hN = null; hR = null; }

        // (for tests of the consistency checks only) overwrites the integers of halfedge h and, for n, of its twin
        internal void set_integer_coordinates(int h, int n, int r)
        {
            hN[h] = n; hR[h] = r;
            if (hTwin[h] >= 0) hN[hTwin[h]] = n;
        }
        internal int normal_coordinate(int h) { return hN[h]; }
        internal int roundabout(int h) { return hR[h]; }

        static long pos(long x) { return x > 0 ? x : 0; }
        static long neg(long x) { return x < 0 ? -x : 0; }

        // arcs that start at the tail of h and cross the opposite edge of its triangle (paper eq. 1)
        long emanating(int h)
        {
            return pos(pos(hN[Next(h)]) - pos(hN[h]) - pos(hN[Prev(h)]));
        }

        // arcs that cut off the corner at the tail of h (paper eq. 2)
        long corner_arcs(int h)
        {
            long a = pos(hN[h]), b = pos(hN[Next(h)]), c = pos(hN[Prev(h)]);
            // minus the arcs that start at the head of h and at the apex and cross h or Prev(h)
            return (pos(a + c - b) - pos(c - a - b) - pos(a - b - c)) / 2;
        }

        // roundabout x at vertex v, as stored: not wrapped at boundary vertices (their fan is open)
        int wrap_roundabout(int v, long x)
        {
            if (v >= N0) return 0;
            if (vBoundary[v]) return (int)x;
            int d = inFanStart[v + 1] - inFanStart[v];
            return (int)(x % d);
        }

        // twice the normal coordinate of the edge k-l that replaces the edge of ha (i -> j in (i, j, k), twin ta
        // in (j, i, l)); paper eq. 3 with all terms doubled, so that the halves stay integers. Before the flip.
        long flipped_coordinate_twice(int ha, int ta)
        {
            int hb = Next(ha), hc = Prev(ha), tb = Next(ta), tc = Prev(ta);
            long cK = corner_arcs(hc), cIup = corner_arcs(ha), cJup = corner_arcs(hb);
            long cL = corner_arcs(tc), cJlo = corner_arcs(ta), cIlo = corner_arcs(tb);
            long eK = emanating(hc), eIup = emanating(ha), eJup = emanating(hb);
            long eL = emanating(tc), eJlo = emanating(ta), eIlo = emanating(tb);
            return 2 * cL + 2 * cK + Math.Abs(cJlo - cJup) + Math.Abs(cIlo - cIup) - eL - eK
                + 2 * (eIlo + eIup + eJlo + eJup) + 2 * neg(hN[ha]);
        }

        // after the flip of ha / ta (FlipEdge has moved the records of the four outer halfedges): the new edge
        // k -> l in ha, l -> k in ta
        void set_flipped_coordinates(int ha, int ta, long twice)
        {
            // (an odd count cannot happen; a count beyond int would be a subdivision too large to index)
            if ((twice & 1) != 0 || twice / 2 > int.MaxValue) { drop_integer_coordinates(); return; }
            hN[ha] = hN[ta] = (int)(twice / 2);
            // k -> l follows k -> i (now Next(ta)) counter-clockwise around k, l -> k follows l -> j (Next(ha))
            // around l (paper eq. 4)
            int ki = Next(ta), lj = Next(ha);
            hR[ha] = wrap_roundabout(hV[ha], hR[ki] + emanating(ki) + neg(hN[ki]));
            hR[ta] = wrap_roundabout(hV[ta], hR[lj] + emanating(lj) + neg(hN[lj]));
        }

        // after the split of the boundary edge of h = i -> j of triangle (i, j, k) at the new vertex m (split_edge:
        // h = i -> m, Next(h) = m -> k, Prev(h) = k -> i; mj = m -> j and km = k -> m in the new triangle). The
        // pieces i-m and m-j lie on the input edge, and m-k crosses crossed input edges (paper eq. 6): the
        // arcs that cut off k, and the arcs that start at i or j and cross the opposite edge (m lies between
        // them and the split edge), which is the larger of the counts of j-k and k-i.
        void set_boundary_split_coordinates(int h, int mj, int km, long crossed)
        {
            int mk = Next(h), ki = Prev(h);
            hN[h] = -1; hN[mj] = -1;
            hN[mk] = hN[km] = (int)crossed;
            hR[mk] = 0; hR[mj] = 0;
            hR[km] = wrap_roundabout(hV[km], hR[ki] + emanating(ki) + neg(hN[ki]));
        }


        // ------------------------------------------------------------------ common subdivision from the integers

        // entry x of the fan of input edges of input vertex v: false if there is none (boundary vertices)
        bool fan_entry(int v, long x, out int entry)
        {
            int d = inFanStart[v + 1] - inFanStart[v];
            entry = 0;
            if (vBoundary[v]) { if (x < 0 || x >= d) return false; }
            else x = ((x % d) + d) % d;
            entry = inFan[inFanStart[v] + (int)x];
            return true;
        }

        // The curve (an input edge) enters the triangle of h through crossing p of h, counted from the tail of h:
        // appends the halfedges it leaves triangles through, and the crossing indices on them (from their tails),
        // until it ends at a vertex (paper algorithm 1). Returns the halfedge whose tail is that vertex, in the
        // last triangle, and the index of the arc among those that start there; -1 if it has not ended after
        // maxCrossings crossings.
        int trace_curve(int h, int p, List<int> outH, List<int> outP, out int fanIndex, int maxCrossings)
        {
            fanIndex = -1;
            while (outH.Count <= maxCrossings) {
                int ca = (int)corner_arcs(h);
                int g, q;
                if (p < ca) {
                    // cuts off the corner at the tail: out through Prev(h), the same index counted from that corner
                    g = Prev(h); q = hN[g] - 1 - p;
                } else if (p >= hN[h] - (int)corner_arcs(Next(h))) {
                    g = Next(h); q = hN[h] - 1 - p;
                } else {
                    fanIndex = p - ca;
                    return Prev(h);
                }
                if (hTwin[g] < 0 || q < 0 || q >= hN[g]) return -1;
                outH.Add(g); outP.Add(q);
                h = hTwin[g]; p = hN[g] - 1 - q;
            }
            return -1;      // (inconsistent coordinates: the curve does not end, or leaves the surface)
        }

        // third corner of a triangle on the edge P -> Q, at distance lp from P and lq from Q, left (+1) or right (-1)
        static Vector2d third_corner(Vector2d P, Vector2d Q, double lp, double lq, int side)
        {
            Vector2d e = Q - P;
            double d = e.Length;
            Vector2d u = e / d, nrm = new Vector2d(-u.y, u.x);
            double x = (lp * lp - lq * lq + d * d) / (2 * d);
            double y2 = lp * lp - x * x;
            double y = y2 > 0 ? Math.Sqrt(y2) : 0;
            return P + x * u + (side * y) * nrm;
        }

        // parameter s of a crossing on its curve, after one at lo: a position that contradicts the order along the
        // curve (rounding; not a number in a triangle without area) is clamped to it
        internal static double clamp_to_order(double s, double lo)
        {
            if (!(s >= lo)) s = lo;
            return s > 1 ? 1 : s;
        }

        // Unfolds the triangles a curve crosses into the plane (from the edge lengths; + - * / and square roots
        // only) and intersects the straight segment between its end vertices with the crossed edges: per
        // crossing the parameter along the curve. Floating point; the order of the crossings is known.
        void unfold_curve(List<int> list, ref double[] sCurve, ref Vector2d[] ufP, ref Vector2d[] ufQ)
        {
            int m = list.Count;
            if (sCurve.Length < m) { sCurve = new double[2 * m]; ufP = new Vector2d[2 * m]; ufQ = new Vector2d[2 * m]; }
            int g = list[0];
            Vector2d P = Vector2d.Zero, Q = new Vector2d(hLen[g], 0);
            Vector2d A = third_corner(P, Q, hLen[Prev(g)], hLen[Next(g)], +1);
            Vector2d B = A;
            for (int k = 0; k < m; ++k) {
                g = list[k];
                ufP[k] = P; ufQ[k] = Q;
                int tw = hTwin[g];
                Vector2d C = third_corner(P, Q, hLen[Next(tw)], hLen[Prev(tw)], -1);
                if (k + 1 == m) B = C;
                else if (list[k + 1] == Next(tw)) Q = C;
                else P = C;
            }
            Vector2d d = B - A;
            for (int k = 0; k < m; ++k) {
                Vector2d e = ufQ[k] - ufP[k], ap = A - ufP[k];
                sCurve[k] = (ap.x * e.y - ap.y * e.x) / (e.x * d.y - e.y * d.x);
            }
        }

        // The common subdivision from the integer coordinates. Connectivity: every input edge that is not an
        // intrinsic edge is traced through the intrinsic triangles by integer comparisons (checked: it ends at the
        // vertex its roundabouts name at both ends, and every crossing and every input edge is used once), and
        // every intrinsic triangle is cut along its arcs into polygons. Positions: a crossing lies on its input
        // edge at the parameter the unfolded curve gives, clamped to the order of the crossings along the edge.
        // Each polygon lies in one input triangle: the one on its side of an arc on its border.
        IntrinsicCommonSubdivision extract_from_integers()
        {
            var R = new IntrinsicCommonSubdivision();
            Func<string, IntrinsicCommonSubdivision> fail = (msg) => {
                R.Status = CommonSubdivisionStatus.Inconsistent; R.Error = msg;
                R.Faces.Clear();
                return R;
            };
            int H = 3 * F;
            // ids of the crossings: per edge (at the halfedge with the smaller index) a block of hN ids
            int[] baseId = new int[H];
            long total = 0;
            int interiorShared = 0;
            for (int h = 0; h < H; ++h) {
                int tw = hTwin[h];
                if (hN[h] < -1) return fail("normal coordinate below -1 at halfedge " + h);
                if (tw < 0 ? hN[h] != -1 : hN[tw] != hN[h]) return fail("normal coordinates of halfedge " + h + " and its twin differ");
                long a = pos(hN[h]), b = pos(hN[Next(h)]), c = pos(hN[Prev(h)]);
                if (((pos(a + c - b) - pos(c - a - b) - pos(a - b - c)) & 1) != 0) return fail("odd corner count at halfedge " + h);
                if (tw >= 0 && tw < h) { baseId[h] = baseId[tw]; continue; }
                if (hN[h] == -1 && tw >= 0) interiorShared++;
                baseId[h] = (int)(N + total);
                total += pos(hN[h]);
                if (N + total > int.MaxValue) return fail("too many crossings");
            }
            int X = (int)total;
            Func<int, int, int> idOf = (h, p) => {
                int tw = hTwin[h];
                return (tw >= 0 && tw < h) ? baseId[h] + hN[h] - 1 - p : baseId[h] + p;
            };
            for (int v = 0; v < N; ++v) R.Points.Add(VertexPosition(v));
            for (int k = 0; k < X; ++k) R.Points.Add(Vector3d.Zero);

            // the curves: per crossing its curve, its place along it and the parameter; per curve the end
            // vertices and the input halfedges along it (left: from v to w, right: from w to v; -1 if none)
            int[] xCurve = new int[X], xOrd = new int[X];
            double[] xS = new double[X];
            for (int k = 0; k < X; ++k) xCurve[k] = -1;
            var cV = new List<int>(); var cW = new List<int>(); var cLeft = new List<int>(); var cRight = new List<int>();
            int[] fanBase = new int[H + 1];
            for (int h = 0; h < H; ++h) fanBase[h + 1] = fanBase[h] + (int)emanating(h);
            bool[] done = new bool[fanBase[H]];
            var listH = new List<int>(); var listP = new List<int>();
            double[] sC = new double[16];
            Vector2d[] ufP = new Vector2d[16], ufQ = new Vector2d[16];
            for (int h = 0; h < H; ++h) {
                int v = hV[h];
                if (hN[h] == -1 && hTwin[h] >= 0) {
                    // an input edge that is an intrinsic edge: its roundabout names it
                    int se;
                    if (v >= N0 || fan_entry(v, hR[h], out se) == false || input_head(se) != Head(h))
                        return fail("roundabout of halfedge " + h + " on an input edge");
                }
                int e = fanBase[h + 1] - fanBase[h];
                if (e > 0 && v >= N0) return fail("input edge starts at inserted vertex " + v);
                // the roundabouts around an input vertex count its input edges: the next halfedge comes after the
                // edge of this one, if it is one, and after the arcs that start in between
                int around = NextAroundVertex(h);
                if (v < N0 && (hR[h] < 0 || hR[h] >= inFanStart[v + 1] - inFanStart[v] + (vBoundary[v] ? 1 : 0)
                        || (around >= 0 && hR[around] != wrap_roundabout(v, hR[h] + e + neg(hN[h])))))
                    return fail("roundabout of halfedge " + h);
                for (int m = 0; m < e; ++m) {
                    if (done[fanBase[h] + m]) continue;
                    done[fanBase[h] + m] = true;
                    int o = Next(h);
                    int p = (int)corner_arcs(o) + m;
                    listH.Clear(); listP.Clear();
                    listH.Add(o); listP.Add(p);
                    int fi;
                    int end = trace_curve(hTwin[o], hN[o] - 1 - p, listH, listP, out fi, X);
                    if (end < 0 || fi >= fanBase[end + 1] - fanBase[end] || done[fanBase[end] + fi]) return fail("input edge from vertex " + v + " does not end, or is traced twice");
                    done[fanBase[end] + fi] = true;
                    int w = hV[end];
                    // the roundabouts name the input edge at both ends
                    int ea, eb;
                    if (w >= N0 || fan_entry(v, hR[h] + neg(hN[h]) + m, out ea) == false || input_head(ea) != w || inTwin[ea] < 0
                        || fan_entry(w, hR[end] + neg(hN[end]) + fi, out eb) == false || input_head(eb) != v)
                        return fail("input edge from vertex " + v + " ends at the wrong vertex " + w);
                    int curve = cV.Count;
                    cV.Add(v); cW.Add(w);
                    cLeft.Add(ea);
                    cRight.Add(inTwin[ea]);
                    unfold_curve(listH, ref sC, ref ufP, ref ufQ);
                    Vector3d pa = R.Points[v], pb = R.Points[w];
                    double lo = 0;
                    for (int k = 0; k < listH.Count; ++k) {
                        int id = idOf(listH[k], listP[k]);
                        if (xCurve[id - N] >= 0) return fail("crossing used twice");
                        double s = lo = clamp_to_order(sC[k], lo);
                        xCurve[id - N] = curve; xOrd[id - N] = k; xS[id - N] = s;
                        R.Points[id] = pa + s * (pb - pa);
                    }
                }
            }
            for (int k = 0; k < X; ++k) if (xCurve[k] < 0) return fail("crossing on no input edge");
            int inputInterior = 0;
            for (int ih = 0; ih < 3 * F0; ++ih) if (inTwin[ih] > ih) inputInterior++;
            if (cV.Count + interiorShared != inputInterior)
                return fail((cV.Count + interiorShared) + " of " + inputInterior + " interior input edges found");

            // input triangle left of the arc from corner x1 to corner x2 (consecutive on one curve)
            Func<int, int, int> leftOf = (x1, x2) => {
                int curve = xCurve[(x1 >= N ? x1 : x2) - N];
                bool forward;
                if (x1 < N) forward = (x1 == cV[curve]);
                else if (x2 < N) forward = (x2 == cW[curve]);
                else forward = xOrd[x2 - N] > xOrd[x1 - N];
                int ih = forward ? cLeft[curve] : cRight[curve];
                return ih / 3;
            };
            double[] itArea = new double[F], inArea = new double[F0];
            string faceError = null;
            Action<int, int[], int> addFace = (t, corners, it) => {
                if (faceError != null) return;
                if (it < 0) { faceError = "intrinsic triangle " + t + " lies in no input triangle"; return; }
                var bary = new Vector3d[corners.Length];
                for (int m = 0; m < corners.Length; ++m) {
                    int x = corners[m];
                    Vector3d b = Vector3d.Zero;
                    if (x >= N) {
                        int curve = xCurve[x - N];
                        double s = xS[x - N];
                        int il = cLeft[curve], ir = cRight[curve];
                        if (il / 3 == it) { b[il % 3] = 1 - s; b[(il + 1) % 3] = s; }
                        else if (ir / 3 == it) { b[ir % 3] = s; b[(ir + 1) % 3] = 1 - s; }
                        else faceError = "crossing " + x + " is not on input triangle " + inTid[it];
                    } else if (x >= N0) {
                        int ib = vInEdge[x];
                        if (ib >= 0 && ib / 3 == it) { b[ib % 3] = 1 - vInEdgeT[x]; b[(ib + 1) % 3] = vInEdgeT[x]; }
                        else faceError = "vertex " + x + " is not on input triangle " + inTid[it];
                    } else {
                        int k = 0;
                        while (k < 3 && input_tail(3 * it + k) != x) ++k;
                        if (k < 3) b[k] = 1;
                        else faceError = "vertex " + x + " is not a corner of input triangle " + inTid[it];
                    }
                    bary[m] = b;
                }
                double area = 0;
                Vector2d q0 = inLayout[3 * it], q1 = inLayout[3 * it + 1], q2 = inLayout[3 * it + 2];
                Vector2d first = bary[0].x * q0 + bary[0].y * q1 + bary[0].z * q2, prev = Vector2d.Zero;
                for (int m = 1; m < corners.Length; ++m) {
                    Vector2d cur = bary[m].x * q0 + bary[m].y * q1 + bary[m].z * q2 - first;
                    area += 0.5 * cross(prev, cur);
                    prev = cur;
                }
                // (convex in its input triangle; rounding can give a face without area a negative one)
                if (!(area >= 0)) area = area < 0 ? 0 : area;
                itArea[t] += area; inArea[it] += area;
                R.Faces.Add(new IntrinsicCommonSubdivision.Face() {
                    InputTriangle = inTid[it], IntrinsicTriangle = t, Corners = corners, Barycentric = bary, Area = area
                });
            };

            // polygons: every arc cuts one region of its triangle in two
            var ring = new List<int>();
            for (int t = 0; t < F; ++t) {
                int fanCorner = -1, apexAt = -1, ringArc = -1;
                ring.Clear();
                for (int k = 0; k < 3; ++k) {
                    int h = 3 * t + k, g = Prev(h);
                    int c = (int)corner_arcs(h);
                    // the bands of the corner: arc q joins crossing q of h (from the tail) and crossing n - 1 - q of g
                    for (int q = 0; q < c; ++q) {
                        int x1 = idOf(h, q), y1 = idOf(g, hN[g] - 1 - q);
                        addFace(t, q == 0 ? new[] { hV[h], x1, y1 } : new[] { idOf(h, q - 1), x1, y1, idOf(g, hN[g] - q) }, leftOf(x1, y1));
                    }
                    // the ring of the middle region
                    if (emanating(h) > 0) { fanCorner = k; apexAt = ring.Count; }
                    if (c == 0) ring.Add(hV[h]);
                    else {
                        ringArc = ring.Count;
                        ring.Add(idOf(g, hN[g] - c));
                        ring.Add(idOf(h, c - 1));
                    }
                    // the arcs that start at the corner opposite h cross h between the corner arcs of its ends
                    int e = (int)emanating(g);
                    for (int m = 0; m < e; ++m) ring.Add(idOf(h, c + m));
                }
                int cnt = ring.Count;
                if (fanCorner >= 0) {
                    // the arcs from the apex cut the middle region into a fan: they end at the e ring entries
                    // after those of the next corner
                    int e = (int)emanating(3 * t + fanCorner);
                    int skip = corner_arcs(3 * t + (fanCorner + 1) % 3) == 0 ? 2 : 3;
                    int apex = ring[apexAt], start = 1;
                    for (int m = 0; m <= e; ++m) {
                        int stop = m < e ? skip + m : cnt - 1;      // ring offset (from the apex) of the last corner of this piece
                        var poly = new int[stop - start + 2];
                        poly[0] = apex;
                        for (int o = start; o <= stop; ++o) poly[o - start + 1] = ring[(apexAt + o) % cnt];
                        addFace(t, poly, m < e ? leftOf(poly[poly.Length - 1], apex) : leftOf(apex, poly[1]));
                        start = stop;
                    }
                } else if (ringArc >= 0)
                    addFace(t, ring.ToArray(), leftOf(ring[ringArc], ring[ringArc + 1]));
                else {
                    // a triangle that no input edge enters lies in one input triangle: beside an input edge it
                    // lies on, or with a corner inserted on a boundary edge of it
                    int it = -1;
                    for (int k = 0; k < 3 && it < 0; ++k) {
                        int h = 3 * t + k, se, ib; double ti, tj;
                        if (hN[h] != -1) continue;
                        if (hTwin[h] < 0) { if (boundary_param(h, out ib, out ti, out tj)) it = ib / 3; }
                        else if (fan_entry(hV[h], hR[h], out se)) it = se / 3;
                    }
                    for (int k = 0; k < 3 && it < 0; ++k)
                        if (hV[3 * t + k] >= N0 && vInEdge[hV[3 * t + k]] >= 0) it = vInEdge[hV[3 * t + k]] / 3;
                    addFace(t, ring.ToArray(), it);
                }
                if (faceError != null) return fail(faceError);
            }

            // The faces in an input triangle are the cells of chords between ordered points on its border, so they
            // tile it up to rounding: checked. The intrinsic areas are reported only: there the rounding of the
            // lengths and of the positions shows (relative to the area of a sliver it can be large).
            for (int it = 0; it < F0; ++it) {
                double a = 0.5 * Math.Abs(cross(inLayout[3 * it + 1] - inLayout[3 * it], inLayout[3 * it + 2] - inLayout[3 * it]));
                double err = Math.Abs(inArea[it] - a) / Math.Max(a, 1e-300);
                if (!(err <= R.MaxInputAreaError)) R.MaxInputAreaError = err;
            }
            for (int t = 0; t < F; ++t) {
                double a = TriangleArea(t);
                double err = Math.Abs(itArea[t] - a) / Math.Max(a, 1e-300);
                if (!(err <= R.MaxIntrinsicAreaError)) R.MaxIntrinsicAreaError = err;
            }
            if (!(R.MaxInputAreaError <= 1e-6)) return fail("faces do not sum to the input triangle areas (relative error " + R.MaxInputAreaError + ")");
            // (lengths whose squares or areas leave the double range: the positions are not reliable)
            if (!(R.MaxIntrinsicAreaError < double.PositiveInfinity)) return fail("the intrinsic triangle areas are not finite");
            return R;
        }
    }
}
