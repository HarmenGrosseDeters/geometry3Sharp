// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// The planar map of the slope envelope (docs/design/TERRAIN_1A_PLAN.md): convex faces in a half-edge structure,
    /// each labelled with a plane (the cap, or a plane of a toe piece). Faces are split along lines of the exact kernel
    /// only, so neighbouring faces share their vertices. All decisions are exact (ExactSlopeKernel); approximations
    /// serve the bounding boxes and a conservative prefilter only.
    /// </summary>
    internal sealed class SlopeMap
    {
        readonly ExactSlopeKernel K;

        // half-edges
        readonly List<int> hOrig = new List<int>(), hTwin = new List<int>(), hNext = new List<int>(), hPrev = new List<int>(),
            hFace = new List<int>(), hLine = new List<int>(), hBar = new List<int>(), hBarSide = new List<int>();
        // faces
        readonly List<int> fEdge = new List<int>(), fLabel = new List<int>(), fQt = new List<int>();
        readonly List<bool> fDomain = new List<bool>(), fAlive = new List<bool>();
        readonly List<bool> hAlive = new List<bool>();
        LooseQuadtree2d qt;
        readonly List<int> qtFace = new List<int>();
        // barriers: the toe segments in order (barrier id = piece index), then the lateral rays
        readonly List<int> barToe = new List<int>(), barSeg = new List<int>(), barDomainSign = new List<int>();
        int toeBarriers;
        // plane -> piece (-1: the cap)
        readonly Dictionary<int, int> owner = new Dictionary<int, int>();

        public SlopeMap(ExactSlopeKernel kernel) { K = kernel; }
        public int FaceCount { get { return fEdge.Count; } }

        // ---------------------------------------------------------------- structure

        int new_half_edge(int orig, int face, int line)
        {
            hAlive.Add(true);
            hOrig.Add(orig); hTwin.Add(-1); hNext.Add(-1); hPrev.Add(-1); hFace.Add(face); hLine.Add(line); hBar.Add(-1); hBarSide.Add(0);
            return hOrig.Count - 1;
        }

        int dest(int h) { return hOrig[hTwin[h]]; }

        List<int> cycle(int f)
        {
            var c = new List<int>();
            int h0 = fEdge[f], h = h0;
            do { c.Add(h); h = hNext[h]; if (c.Count > 100000) throw new InvalidOperationException("SlopeMap: broken cycle"); } while (h != h0);
            return c;
        }

        AxisAlignedBox2d face_box(int f)
        {
            var b = AxisAlignedBox2d.Empty;
            foreach (int h in cycle(f)) b.Contain(K.Approx(hOrig[h]));
            b.Expand(1e-9 * (b.MaxDim + Math.Max(Math.Max(Math.Abs(b.Min.x), Math.Abs(b.Max.x)), Math.Max(Math.Abs(b.Min.y), Math.Abs(b.Max.y)))));
            return b;
        }

        void qt_update(int f)
        {
            if (fQt[f] >= 0) qt.Remove(fQt[f]);
            int id = qt.Insert(face_box(f));
            while (qtFace.Count <= id) qtFace.Add(-1);
            qtFace[id] = f;
            fQt[f] = id;
        }

        // split the edge h (and its twin) at vertex v, which lies on its line strictly between its ends
        void split_edge(int h, int v)
        {
            int t = hTwin[h];
            int h2 = new_half_edge(v, hFace[h], hLine[h]);
            int t2 = new_half_edge(v, hFace[t], hLine[t]);
            hBar[h2] = hBar[h]; hBarSide[h2] = hBarSide[h];
            hBar[t2] = hBar[t]; hBarSide[t2] = hBarSide[t];
            // h: u -> v, h2: v -> w; t: w -> v, t2: v -> u
            int hn = hNext[h], tn = hNext[t];
            hNext[h] = h2; hPrev[h2] = h; hNext[h2] = hn; if (hn >= 0) hPrev[hn] = h2;
            hNext[t] = t2; hPrev[t2] = t; hNext[t2] = tn; if (tn >= 0) hPrev[tn] = t2;
            hTwin[h] = t2; hTwin[t2] = h;
            hTwin[h2] = t; hTwin[t] = h2;
        }

        /// <summary>
        /// split face f by the line: neg is the part where the line's sign is negative, pos the rest (either -1 if
        /// the line does not cut f; all zero counts as pos). chord: the new half-edge in neg (-1 if no cut).
        /// </summary>
        bool split_face(int f, int line, out int neg, out int pos, out int chord)
        {
            neg = -1; pos = -1; chord = -1;
            List<int> c = cycle(f);
            int n = c.Count;
            var s = new int[n];
            bool anyNeg = false, anyPos = false;
            for (int i = 0; i < n; ++i) {
                s[i] = K.Side(hOrig[c[i]], line);
                if (s[i] < 0) anyNeg = true;
                if (s[i] > 0) anyPos = true;
            }
            if (anyNeg == false) { pos = f; return false; }
            if (anyPos == false) { neg = f; return false; }
            // split the edges whose ends are strictly on both sides
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                if (s[i] * s[j] < 0) {
                    int v = K.Crossing(hLine[c[i]], line);
                    if (v < 0) throw new InvalidOperationException("SlopeMap: parallel crossing");
                    split_edge(c[i], v);
                }
            }
            c = cycle(f);
            n = c.Count;
            s = new int[n];
            for (int i = 0; i < n; ++i) s[i] = K.Side(hOrig[c[i]], line);
            // the two zero vertices: z1 between the negative and the positive run, z2 after the positive run
            int i1 = -1, i2 = -1;
            for (int i = 0; i < n; ++i) {
                if (s[i] != 0) continue;
                int prev = s[(i + n - 1) % n], next = s[(i + 1) % n];
                if (prev < 0 && next > 0) i1 = i;
                else if (prev > 0 && next < 0) i2 = i;
            }
            if (i1 < 0 || i2 < 0)
                throw new InvalidOperationException("SlopeMap: face is not convex");
            int z1out = c[i1], z1in = c[(i1 + n - 1) % n], z2out = c[i2], z2in = c[(i2 + n - 1) % n];
            int g = fEdge.Count;
            fEdge.Add(-1); fLabel.Add(fLabel[f]); fDomain.Add(fDomain[f]); fQt.Add(-1); fAlive.Add(true);
            int n1 = new_half_edge(hOrig[z1out], f, line);      // z1 -> z2, in neg (f)
            int n2 = new_half_edge(hOrig[z2out], g, line);      // z2 -> z1, in pos (g)
            hTwin[n1] = n2; hTwin[n2] = n1;
            hNext[z1in] = n1; hPrev[n1] = z1in; hNext[n1] = z2out; hPrev[z2out] = n1;
            hNext[z2in] = n2; hPrev[n2] = z2in; hNext[n2] = z1out; hPrev[z1out] = n2;
            fEdge[f] = n1;
            fEdge[g] = n2;
            int h = n2;
            do { hFace[h] = g; h = hNext[h]; } while (h != n2);
            qt_update(f);
            qt_update(g);
            neg = f; pos = g; chord = n1;
            return true;
        }

        // ---------------------------------------------------------------- the domain

        /// <summary>
        /// The box minus the design side of the toes, cut at the lateral limits of open toes (the normal rays at
        /// their ends); false with a reason if the sides of the toes do not agree
        /// </summary>
        public bool BuildDomain(List<List<Vector3d>> toes, List<bool> closed, List<SlopeToe.Sides> sides, AxisAlignedBox2d box,
                                out string why, out Index2i where)
        {
            why = ""; where = new Index2i(-1, -1);
            var pts = new List<Vector2d>();
            pts.Add(box.Min); pts.Add(new Vector2d(box.Max.x, box.Min.y)); pts.Add(box.Max); pts.Add(new Vector2d(box.Min.x, box.Max.y));
            var toeIndex = new List<int>();
            foreach (var t in toes) { toeIndex.Add(pts.Count); foreach (var p in t) pts.Add(p.xy); }
            int nIn = pts.Count;
            Vector2d c = box.Center;
            double S = box.MaxDim;
            pts.Add(new Vector2d(c.x - 10 * S, c.y - 10 * S)); pts.Add(new Vector2d(c.x + 10 * S, c.y - 10 * S)); pts.Add(new Vector2d(c.x, c.y + 10 * S));
            Func<int, int, int, int> orient = (a, b, d) => ExactPredicates.Orient2d(pts[a], pts[b], pts[d]);
            Func<int, int, int, int, int> incircle = (a, b, d, e) => ExactPredicates.InCircle(pts[a], pts[b], pts[d], pts[e]);
            var cdt = new ExactTriangulation2(nIn, nIn + 1, nIn + 2, orient, incircle);
            for (int i = 0; i < nIn; ++i) cdt.InsertPoint(i);
            for (int i = 0; i < 4; ++i) cdt.InsertConstraint(i, (i + 1) % 4);
            var segOf = new Dictionary<long, int>();
            var domainSign = new List<int>();
            for (int t = 0; t < toes.Count; ++t) {
                int n = toes[t].Count, m = closed[t] ? n : n - 1;
                int sign;
                if (closed[t]) {
                    bool ccw = polygon_ccw(toes[t]);
                    bool inside = sides[t] == SlopeToe.Sides.Inside;
                    sign = (inside == ccw) ? 1 : -1;
                } else sign = (sides[t] == SlopeToe.Sides.Left) ? 1 : -1;
                domainSign.Add(sign);
                for (int i = 0; i < m; ++i) {
                    int a = toeIndex[t] + i, b = toeIndex[t] + (i + 1) % n;
                    cdt.InsertConstraint(a, b);
                    segOf[key(a, b)] = barToe.Count;
                    barToe.Add(t); barSeg.Add(i); barDomainSign.Add(sign);
                }
            }
            toeBarriers = barToe.Count;
            cdt.MakeDelaunay();

            // faces from the triangles inside the box
            var kv = new int[nIn];
            for (int i = 0; i < nIn; ++i) kv[i] = K.PointVertex(pts[i]);
            var he = new Dictionary<long, int>();
            var lineOf = new Dictionary<long, int>();
            foreach (Index3i tri in cdt.Triangles()) {
                if (tri.a >= nIn || tri.b >= nIn || tri.c >= nIn) continue;
                int[] v = { tri.a, tri.b, tri.c };
                if (ExactPredicates.Orient2d(pts[v[0]], pts[v[1]], pts[v[2]]) < 0) { int x = v[1]; v[1] = v[2]; v[2] = x; }
                int f = fEdge.Count;
                fEdge.Add(-1); fLabel.Add(-1); fDomain.Add(false); fQt.Add(-1); fAlive.Add(true);
                var hs = new int[3];
                for (int k = 0; k < 3; ++k) {
                    int a = v[k], b = v[(k + 1) % 3];
                    long uk = a < b ? key(a, b) : key(b, a);
                    int line;
                    if (lineOf.TryGetValue(uk, out line) == false) {
                        line = K.PointLine(pts[Math.Min(a, b)], pts[Math.Max(a, b)]);
                        lineOf[uk] = line;
                    }
                    hs[k] = new_half_edge(kv[a], f, line);
                    he[key(a, b)] = hs[k];
                    int bar;
                    if (segOf.TryGetValue(key(a, b), out bar)) { hBar[hs[k]] = bar; hBarSide[hs[k]] = 1; }
                    else if (segOf.TryGetValue(key(b, a), out bar)) { hBar[hs[k]] = bar; hBarSide[hs[k]] = -1; }
                }
                for (int k = 0; k < 3; ++k) { hNext[hs[k]] = hs[(k + 1) % 3]; hPrev[hs[(k + 1) % 3]] = hs[k]; }
                fEdge[f] = hs[0];
            }
            // twins; box edges get twins outside (face -1)
            foreach (var e in he.ToList()) {
                int a = (int)(e.Key >> 32), b = (int)(e.Key & 0xFFFFFFFF);
                int t;
                if (he.TryGetValue(key(b, a), out t)) { hTwin[e.Value] = t; continue; }
                int o = new_half_edge(kv[b], -1, hLine[e.Value]);
                hTwin[e.Value] = o; hTwin[o] = e.Value;
            }
            qt = new LooseQuadtree2d(new AxisAlignedBox2d[] { box }, false);
            qt.Remove(0);
            qtFace.Add(-1);
            for (int f = 0; f < fEdge.Count; ++f) qt_update(f);

            // lateral limits: the normal rays at the ends of open toes
            for (int t = 0; t < toes.Count; ++t) {
                if (closed[t]) continue;
                var p = toes[t];
                int n = p.Count;
                Vector2d u0 = (p[1].xy - p[0].xy).Normalized, u1 = (p[n - 1].xy - p[n - 2].xy).Normalized;
                Func<Vector2d, Vector2d> normal = (u) => domainSign[t] > 0 ? new Vector2d(-u.y, u.x) : new Vector2d(u.y, -u.x);
                int l0, l1;
                Vector2d n0 = normal(u0), n1 = normal(u1);
                int b0 = add_ray(p[0].xy, n0, u0, 4 * S, t, 0, out l0);
                int b1 = add_ray(p[n - 1].xy, n1, -u1, 4 * S, t, n - 2, out l1);
                // each ray ends where it first meets a toe or the other ray (normals that converge on the slope side)
                trim_ray(b0, p[0].xy, n0, first_hit(p[0].xy, n0, p[n - 1].xy, n1, toes, closed));
                trim_ray(b1, p[n - 1].xy, n1, first_hit(p[n - 1].xy, n1, p[0].xy, n0, toes, closed));
            }

            // components of the faces between the barriers; each must lie on the domain side of all its barriers
            var comp = new int[fEdge.Count];
            for (int f = 0; f < comp.Length; ++f) comp[f] = -1;
            for (int f0 = 0; f0 < comp.Length; ++f0) {
                if (comp[f0] >= 0) continue;
                var stack = new Stack<int>();
                var members = new List<int>();
                stack.Push(f0); comp[f0] = f0;
                int verdict = 0;     // 0 unknown, 1 domain, -1 not
                while (stack.Count > 0) {
                    int f = stack.Pop();
                    members.Add(f);
                    foreach (int h in cycle(f)) {
                        if (hBar[h] >= 0) {
                            int v = (hBarSide[h] == barDomainSign[hBar[h]]) ? 1 : -1;
                            if (verdict == 0) verdict = v;
                            else if (verdict != v) {
                                int b = hBar[h];
                                why = $"the sides of the toes do not agree (toe {barToe[b]}): a region lies on the slope side of one toe and the design side of another";
                                where = new Index2i(barToe[b], barSeg[b]);
                                return false;
                            }
                            continue;
                        }
                        int g = hFace[hTwin[h]];
                        if (g >= 0 && comp[g] < 0) { comp[g] = f0; stack.Push(g); }
                    }
                }
                if (verdict > 0)
                    foreach (int f in members) fDomain[f] = true;
            }
            if (fDomain.Any(d => d) == false) {
                why = "no slope region on the chosen sides";
                return false;
            }
            return true;
        }

        // the distance along the ray from u0 (direction d, unit) to its first crossing with a toe segment that does not
        // end at u0, or with the other ray (from w, direction e); infinity if none (approximate: it only decides where a
        // lateral limit ends)
        static double first_hit(Vector2d u0, Vector2d d, Vector2d w, Vector2d e, List<List<Vector3d>> toes, List<bool> closed)
        {
            double best = double.PositiveInfinity;
            Func<Vector2d, Vector2d, bool, double> hit = (a, b, isRay) => {
                Vector2d r = b - a;
                double den = d.x * r.y - d.y * r.x;
                if (den == 0) return double.PositiveInfinity;
                Vector2d q = a - u0;
                double t = (q.x * r.y - q.y * r.x) / den, s = (q.x * d.y - q.y * d.x) / den;
                if (t <= 0 || s < 0 || (isRay == false && s > 1)) return double.PositiveInfinity;
                return t;
            };
            for (int k = 0; k < toes.Count; ++k) {
                int n = toes[k].Count, m = closed[k] ? n : n - 1;
                for (int i = 0; i < m; ++i) {
                    Vector2d a = toes[k][i].xy, b = toes[k][(i + 1) % n].xy;
                    if (a == u0 || b == u0) continue;
                    best = Math.Min(best, hit(a, b, false));
                }
            }
            best = Math.Min(best, hit(w, w + e, true));
            return best;
        }

        // the barrier marks of ray 'bar' farther than tStop from u0 are removed
        void trim_ray(int bar, Vector2d u0, Vector2d d, double tStop)
        {
            if (double.IsInfinity(tStop)) return;
            double eps = 1e-9 * (tStop + u0.Length);
            for (int h = 0; h < hOrig.Count; ++h) {
                if (hAlive[h] == false || hBar[h] != bar) continue;
                double ta = (K.Approx(hOrig[h]) - u0).Dot(d), tb = (K.Approx(hOrig[hTwin[h]]) - u0).Dot(d);
                if (Math.Max(ta, tb) > tStop + eps) { hBar[h] = -1; hBarSide[h] = 0; }
            }
        }

        int add_ray(Vector2d u0, Vector2d dir, Vector2d domainDir, double R, int toe, int seg, out int line)
        {
            int bar = barToe.Count;
            barToe.Add(toe); barSeg.Add(seg);
            Vector2d far = u0 + R * dir;
            line = K.PointLine(u0, far);
            barDomainSign.Add(ExactPredicates.Orient2d(u0, far, u0 + domainDir));
            // ahead of u0: negative side of the line through u0 and u0 + perp(dir)
            int ahead = K.PointLine(u0, u0 + new Vector2d(-dir.y, dir.x));
            int nf = fEdge.Count;
            for (int f = 0; f < nf; ++f) {
                bool anyAhead = false;
                foreach (int h in cycle(f)) if (K.Side(hOrig[h], ahead) < 0) { anyAhead = true; break; }
                if (anyAhead == false) continue;
                int neg, pos, chord;
                if (split_face(f, line, out neg, out pos, out chord) == false) continue;
                int a = hOrig[chord], b = hOrig[hTwin[chord]];
                if (K.Side(a, ahead) > 0 || K.Side(b, ahead) > 0) continue;
                // neg (the chord's face) is on the negative side of the line, pos on the positive
                hBar[chord] = bar; hBarSide[chord] = -1;
                hBar[hTwin[chord]] = bar; hBarSide[hTwin[chord]] = 1;
            }
            return bar;
        }

        static bool polygon_ccw(List<Vector3d> p)
        {
            int best = 0;
            for (int i = 1; i < p.Count; ++i)
                if (p[i].x < p[best].x || (p[i].x == p[best].x && p[i].y < p[best].y)) best = i;
            int n = p.Count;
            return ExactPredicates.Orient2d(p[(best + n - 1) % n].xy, p[best].xy, p[(best + 1) % n].xy) > 0;
        }

        static long key(int a, int b) { return ((long)a << 32) | (uint)b; }

        public void LabelDomain(int cap)
        {
            owner[cap] = -1;
            for (int f = 0; f < fEdge.Count; ++f) fLabel[f] = fDomain[f] ? cap : -1;
        }

        // ---------------------------------------------------------------- inserting a piece

        /// <summary>
        /// the faces where F_k = max of the piece's planes is lower than their label get the piece's planes
        /// (ties keep the earlier label). A, B: the piece's (lowered) end points; zmin, slb: F_k &gt;= zmin + slb * distance
        /// to the segment (the prefilter); reach: no effect beyond it.
        /// </summary>
        public void InsertPiece(int k, List<int> strip, List<int> fanA, List<double> angA, List<int> fanB, List<double> angB,
                                Vector3d A, Vector3d B, double zmin, double slb, AxisAlignedBox2d reach, double theta)
        {
            foreach (int p in strip) owner[p] = k;
            foreach (int p in fanA) owner[p] = k;
            foreach (int p in fanB) owner[p] = k;
            var hits = new List<int>();
            qt.QueryRectangle(reach, hits);
            var faces = hits.Select(id => qtFace[id]).Where(f => f >= 0 && fAlive[f] && fDomain[f]).Distinct().OrderBy(f => f).ToList();
            var relevant = new List<int>();
            foreach (int f in faces) {
                int label = fLabel[f];
                List<int> c = cycle(f);
                var pos = c.Select(h => K.Approx(hOrig[h])).ToList();
                // prefilter (conservative): F_k over the face is at least zmin + slb * dmin
                double upper = double.MinValue;
                foreach (var q in pos) upper = Math.Max(upper, K.ApproxHeight(label, q.x, q.y));
                double dmin = poly_segment_distance(pos, A.xy, B.xy);
                double lowerF = zmin + slb * dmin;
                double size = 0;
                foreach (var q in pos) size = Math.Max(size, q.Distance(pos[0]));
                if (lowerF > upper + 1e-9 * (Math.Abs(upper) + Math.Abs(lowerF) + slb * (size + dmin)))
                    continue;
                relevant.Clear();
                relevant.AddRange(strip);
                add_sector(relevant, fanB, angB, B.xy, pos, theta);
                add_sector(relevant, fanA, angA, A.xy, pos, theta);
                insert_into(f, label, relevant);
            }
        }

        static void add_sector(List<int> relevant, List<int> fan, List<double> ang, Vector2d apex, List<Vector2d> pos, double theta)
        {
            if (fan.Count == 0) return;
            double refAngle = double.NaN, lo = 0, hi = 0;
            foreach (var q in pos) {
                Vector2d d = q - apex;
                if (d.LengthSquared < 1e-30 * (apex.LengthSquared + 1)) continue;
                double a = Math.Atan2(d.y, d.x);
                if (double.IsNaN(refAngle)) { refAngle = a; continue; }
                double r = wrap(a - refAngle);
                lo = Math.Min(lo, r); hi = Math.Max(hi, r);
            }
            if (double.IsNaN(refAngle)) { relevant.AddRange(fan); return; }
            if (hi - lo > Math.PI - 4 * theta) { relevant.AddRange(fan); return; }
            // centred on the sector, so that the window never wraps (its half width is below pi / 2 + 2 theta)
            double mid = refAngle + 0.5 * (lo + hi), half = 0.5 * (hi - lo) + 2 * theta;
            for (int i = 0; i < fan.Count; ++i)
                if (Math.Abs(wrap(ang[i] - mid)) <= half) relevant.Add(fan[i]);
        }

        static double wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static double poly_segment_distance(List<Vector2d> poly, Vector2d a, Vector2d b)
        {
            // inside (convex, counter-clockwise): 0
            bool inside = true;
            for (int i = 0; i < poly.Count && inside; ++i) {
                Vector2d p = poly[i], q = poly[(i + 1) % poly.Count];
                if (cross(q - p, a - p) < 0) inside = false;
            }
            if (inside) return 0;
            double d = double.MaxValue;
            var s = new Segment2d(a, b);
            for (int i = 0; i < poly.Count; ++i) {
                var e = new Segment2d(poly[i], poly[(i + 1) % poly.Count]);
                d = Math.Min(d, Math.Sqrt(s.DistanceSquared(poly[i])));
                d = Math.Min(d, Math.Sqrt(e.DistanceSquared(a)));
                d = Math.Min(d, Math.Sqrt(e.DistanceSquared(b)));
                if (segments_cross(a, b, poly[i], poly[(i + 1) % poly.Count])) return 0;
            }
            return d;
        }

        static double cross(Vector2d u, Vector2d v) { return u.x * v.y - u.y * v.x; }

        static bool segments_cross(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            double o1 = cross(b - a, c - a), o2 = cross(b - a, d - a), o3 = cross(d - c, a - c), o4 = cross(d - c, b - c);
            return o1 * o2 <= 0 && o3 * o4 <= 0;
        }

        void insert_into(int f, int label, List<int> planes)
        {
            // W = f ∩ {P_j < label for all j}, clipped virtually first, so that f is only split if W is not empty
            List<int> c = cycle(f);
            var verts = c.Select(h => hOrig[h]).ToList();
            var elines = c.Select(h => hLine[h]).ToList();
            var applied = new List<int>();
            foreach (int p in planes) {
                int line = K.PlaneLine(p, label);
                if (clip(ref verts, ref elines, line) == false)
                    return;     // empty
                applied.Add(line);
            }
            // replay the lines that bound W, in order
            var bounding = new HashSet<int>(elines);
            int cur = f;
            foreach (int line in applied) {
                if (bounding.Contains(line) == false) continue;
                int neg, pos, chord;
                split_face(cur, line, out neg, out pos, out chord);
                if (neg < 0) throw new InvalidOperationException("SlopeMap: replay");
                cur = neg;
            }
            // W gets the maximum of the planes (incremental: the parts where the next plane is higher)
            var parts = new List<int>() { cur };
            fLabel[cur] = planes[0];
            for (int i = 1; i < planes.Count; ++i) {
                int p = planes[i];
                var next = new List<int>();
                foreach (int g in parts) {
                    int neg, pos, chord;
                    split_face(g, K.PlaneLine(p, fLabel[g]), out neg, out pos, out chord);
                    if (neg >= 0) next.Add(neg);
                    if (pos >= 0) {
                        // all zero: the same height everywhere, keep the earlier plane
                        if (neg >= 0 || all_positive(pos, K.PlaneLine(p, fLabel[pos])))
                            fLabel[pos] = p;
                        next.Add(pos);
                    }
                }
                parts = next;
            }
        }

        bool all_positive(int f, int line)
        {
            bool any = false;
            foreach (int h in cycle(f)) {
                int s = K.Side(hOrig[h], line);
                if (s < 0) return false;
                if (s > 0) any = true;
            }
            return any;
        }

        // keep the part of the polygon where the line is negative; false if nothing is left
        bool clip(ref List<int> verts, ref List<int> elines, int line)
        {
            int n = verts.Count;
            var s = new int[n];
            bool anyNeg = false, anyPos = false;
            for (int i = 0; i < n; ++i) {
                s[i] = K.Side(verts[i], line);
                if (s[i] < 0) anyNeg = true; else if (s[i] > 0) anyPos = true;
            }
            if (anyNeg == false) return false;
            if (anyPos == false) return true;
            // (vertex, line of the edge from it) of the part where the line is negative or zero
            var nv = new List<int>(); var nl = new List<int>();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                if (s[i] <= 0) {
                    nv.Add(verts[i]);
                    nl.Add((s[j] > 0 && s[i] == 0) ? line : elines[i]);    // leaving at a vertex on the line: along it
                    if (s[i] < 0 && s[j] > 0) {
                        nv.Add(K.Crossing(elines[i], line));               // leaving inside the edge
                        nl.Add(line);
                    }
                } else if (s[j] < 0) {
                    nv.Add(K.Crossing(elines[i], line));                   // entering inside the edge
                    nl.Add(elines[i]);
                }
            }
            verts = nv; elines = nl;
            return true;
        }

        // ---------------------------------------------------------------- simplification

        /// <summary>
        /// Merge neighbouring faces with the same label where their union is convex, and remove the vertices that are
        /// then left in the middle of a straight edge between two faces. This removes the splits along lines of planes
        /// that were not the minimum in the end (they cross true creases at arbitrary, sometimes nearly coincident,
        /// points). Exact: convexity by Orient.
        /// </summary>
        public void Simplify()
        {
            // greedy in face order, rounds until nothing changes; a pair that failed is tried again only after one of
            // its faces changed (the convexity of the union depends on the two faces only)
            var version = new int[fEdge.Count];
            var failed = new HashSet<(int, int, int, int)>();
            bool changed = true;
            while (changed) {
                changed = false;
                for (int f = 0; f < fEdge.Count; ++f) {
                    if (fAlive[f] == false || fDomain[f] == false) continue;
                    for (bool again = true; again;) {
                        again = false;
                        foreach (int h in cycle(f)) {
                            int g = hFace[hTwin[h]];
                            if (g < 0 || g == f || fAlive[g] == false || fDomain[g] == false || fLabel[g] != fLabel[f] || hBar[h] >= 0) continue;
                            var key = (f, g, version[f], version[g]);
                            if (failed.Contains(key)) continue;
                            if (try_merge(f, g)) { version[f]++; again = true; changed = true; break; }
                            failed.Add(key);
                        }
                    }
                }
                for (int f = 0; f < fEdge.Count; ++f) {
                    if (fAlive[f] == false) continue;
                    foreach (int h in cycle(f)) {
                        int g = hFace[hTwin[h]];
                        if (try_remove_vertex(h)) {
                            version[f]++;
                            if (g >= 0) version[g]++;
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        bool try_merge(int f, int g)
        {
            List<int> cf = cycle(f);
            int n = cf.Count;
            // the run of f's edges shared with g (contiguous: two convex faces meet in one segment)
            int start = -1;
            for (int i = 0; i < n; ++i) {
                bool shared = hFace[hTwin[cf[i]]] == g, prevShared = hFace[hTwin[cf[(i + n - 1) % n]]] == g;
                if (shared && prevShared == false) { start = i; break; }
            }
            if (start < 0) return false;    // all edges shared: not possible for faces with area
            int r = 0;
            while (hFace[hTwin[cf[(start + r) % n]]] == g) r++;
            for (int i = r; i < n; ++i) if (hFace[hTwin[cf[(start + i) % n]]] == g) return false;    // not contiguous
            int h1 = cf[start], hr = cf[(start + r - 1) % n];
            int fPrev = hPrev[h1], fNext = hNext[hr];               // f's edges ending at a, starting at b
            int t1 = hTwin[h1], tr = hTwin[hr];
            int gNext = hNext[t1], gPrev = hPrev[tr];               // g's edges starting at a, ending at b
            if (hBar[h1] >= 0) return false;
            for (int i = 0; i < r; ++i) if (hBar[cf[(start + i) % n]] >= 0) return false;
            int a = hOrig[h1], b = hOrig[fNext];
            // convex at a: (origin of fPrev, a, dest of gNext); at b: (origin of gPrev, b, dest of fNext)
            if (K.Orient(hOrig[fPrev], a, hOrig[hTwin[gNext]]) < 0) return false;
            if (K.Orient(hOrig[gPrev], b, hOrig[hTwin[fNext]]) < 0) return false;
            // the union must not be degenerate (both faces have area, so it has)
            hNext[fPrev] = gNext; hPrev[gNext] = fPrev;
            hNext[gPrev] = fNext; hPrev[fNext] = gPrev;
            for (int i = 0; i < r; ++i) { int h = cf[(start + i) % n]; hAlive[h] = false; hAlive[hTwin[h]] = false; }
            int e = gNext;
            do { hFace[e] = f; e = hNext[e]; } while (e != gNext);
            fEdge[f] = fPrev;
            fAlive[g] = false;
            if (fQt[g] >= 0) { qt.Remove(fQt[g]); fQt[g] = -1; }
            qt_update(f);
            return true;
        }

        // h: v -> w in a face; v of degree 2 (its two edges are h and the twin of hPrev[h]) and straight: remove it
        bool try_remove_vertex(int h)
        {
            int p = hPrev[h];
            int v = hOrig[h];
            int t = hTwin[h], tp = hTwin[p];        // t: w -> v, tp: v -> u
            if (hFace[t] < 0 || hFace[h] < 0) return false;    // the box boundary stays
            if (hNext[t] != tp) return false;       // another edge at v
            if (hBar[h] != hBar[p]) return false;
            int u = hOrig[p], w = hOrig[t];
            if (K.Orient(u, v, w) != 0) return false;
            int f = hFace[h], g = hFace[t];
            // keep p (u -> v becomes u -> w) and its twin tp (v -> u becomes w -> u)
            int hn = hNext[h], tn = hPrev[t];
            hNext[p] = hn; hPrev[hn] = p;
            hOrig[tp] = w;
            hNext[tn] = tp; hPrev[tp] = tn;
            hAlive[h] = false; hAlive[t] = false;
            if (f >= 0 && fEdge[f] == h) fEdge[f] = p;
            if (g >= 0 && fEdge[g] == t) fEdge[g] = tp;
            return true;
        }

        // ---------------------------------------------------------------- results

        /// <summary> pieces whose toe segment has a face of another piece on it, lower than the toe by more than tol </summary>
        public List<int> Undercuts(List<Vector3d[]> pieceEnds, double tol)
        {
            var result = new SortedSet<int>();
            for (int h = 0; h < hOrig.Count; ++h) {
                if (hAlive[h] == false) continue;
                int b = hBar[h], f = hFace[h];
                if (b < 0 || b >= toeBarriers || f < 0 || fDomain[f] == false) continue;
                int o;
                if (owner.TryGetValue(fLabel[f], out o) == false || o == b || o < 0) continue;
                Vector3d A = pieceEnds[b][0], B = pieceEnds[b][1];
                foreach (int v in new[] { hOrig[h], hOrig[hTwin[h]] }) {
                    Vector2d q = K.Rounded(v);
                    double t = (q - A.xy).Dot(B.xy - A.xy) / (B.xy - A.xy).LengthSquared;
                    double z = A.z + t * (B.z - A.z);
                    if (z - K.ApproxHeight(fLabel[f], q.x, q.y) > tol) result.Add(b);
                }
            }
            return result.ToList();
        }

        /// <summary>
        /// the slope faces as a TIN: vertices rounded to the nearest doubles, z the lowest incident plane there (rounded
        /// to nearest), faces triangulated at their exact corners; group id = piece; z negated for fill
        /// </summary>
        public DMesh3 Realize(int cap, bool fill, double mergeRadius = 0)
        {
            var mesh = new DMesh3(false, false, false, true);
            // the planes incident to each rounded position (all domain faces, the cap too)
            var labelsAt = new Dictionary<Vector2d, List<int>>();
            var faces = new List<int>();
            for (int f = 0; f < fEdge.Count; ++f) {
                if (fAlive[f] == false || fDomain[f] == false) continue;
                foreach (int h in cycle(f)) {
                    Vector2d r = K.Rounded(hOrig[h]);
                    List<int> ls;
                    if (labelsAt.TryGetValue(r, out ls) == false) { ls = new List<int>(); labelsAt[r] = ls; }
                    if (ls.Contains(fLabel[f]) == false) ls.Add(fLabel[f]);
                }
                if (fLabel[f] != cap) faces.Add(f);
            }
            var outId = new Dictionary<Vector2d, int>();
            var repOf = new Dictionary<Vector2d, Vector2d>();
            Func<int, int> outVertex = (v) => {
                Vector2d r = K.Rounded(v);
                Vector2d rep;
                if (repOf.TryGetValue(r, out rep)) r = rep;
                int o;
                if (outId.TryGetValue(r, out o)) return o;
                double z = double.MaxValue;
                foreach (int l in labelsAt[r]) z = Math.Min(z, K.Height(l, r.x, r.y));
                o = mesh.AppendVertex(new Vector3d(r.x, r.y, fill ? -z : z));
                outId[r] = o;
                OutLabels.Add(labelsAt[r]);
                return o;
            };
            OutLabels.Clear();
            OnBarrier.Clear();
            var barrierPos = new HashSet<Vector2d>();
            BarriersAt.Clear();
            Action<Vector2d, int> at = (p, bar) => {
                List<int> l;
                if (BarriersAt.TryGetValue(p, out l) == false) { l = new List<int>(); BarriersAt[p] = l; }
                if (l.Contains(bar) == false) l.Add(bar);
            };
            for (int h = 0; h < hOrig.Count; ++h)
                if (hAlive[h] && hBar[h] >= 0 && hFace[h] >= 0 && fDomain[hFace[h]]) {
                    Vector2d p = K.Rounded(hOrig[h]), q = K.Rounded(hOrig[hTwin[h]]);
                    barrierPos.Add(p); barrierPos.Add(q);
                    at(p, hBar[h]); at(q, hBar[h]);
                }
            merge_close_positions(labelsAt, barrierPos, BarriersAt, mergeRadius, repOf);
            foreach (int f in faces) {
                int group = owner[fLabel[f]];
                var verts = cycle(f).Select(h => hOrig[h]).ToList();
                foreach (Index3i t in triangulate(verts)) {
                    int a = outVertex(t.a), b = outVertex(t.b), c = outVertex(t.c);
                    if (a == b || b == c || c == a) continue;
                    int tid = mesh.AppendTriangle(a, b, c, group);
                    if (tid < 0) throw new InvalidOperationException("SlopeMap: non-manifold realization");
                }
            }
            foreach (int v in mesh.VertexIndices()) OnBarrier.Add(barrierPos.Contains(mesh.GetVertex(v).xy));
            return mesh;
        }

        // Positions nearer than the radius become one vertex (z the lowest of the planes of all of them; the exact check
        // of every triangle decides), and a position on a barrier only goes to another one on a barrier. Near-degenerate
        // inputs need it: on a regular polygon the line where the strips of two pieces are equal passes within a few
        // ulps of the opposite vertex, which leaves a cluster of map vertices there; collapsing their edges one by one
        // can fail the link condition and leave a degenerate triangle.
        static void merge_close_positions(Dictionary<Vector2d, List<int>> labelsAt, HashSet<Vector2d> barrierPos,
                                          Dictionary<Vector2d, List<int>> barriersAt, double radius,
                                          Dictionary<Vector2d, Vector2d> repOf)
        {
            if (!(radius > 0)) return;
            var order = labelsAt.Keys.ToList();
            order.Sort((a, b) => {
                bool ba = barrierPos.Contains(a), bb = barrierPos.Contains(b);
                if (ba != bb) return ba ? -1 : 1;
                int ca = labelsAt[a].Count, cb = labelsAt[b].Count;
                if (ca != cb) return cb.CompareTo(ca);
                return a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
            });
            var grid = new Dictionary<long, List<Vector2d>>();
            Func<double, long> cell = (x) => (long)Math.Floor(x / radius);
            foreach (Vector2d p in order) {
                long cx = cell(p.x), cy = cell(p.y);
                Vector2d best = p;
                double bestD = radius;
                for (long i = cx - 1; i <= cx + 1; ++i)
                    for (long j = cy - 1; j <= cy + 1; ++j) {
                        List<Vector2d> reps;
                        if (grid.TryGetValue(i * 0x100000007L + j, out reps) == false) continue;
                        foreach (Vector2d q in reps) {
                            double d = p.Distance(q);
                            if (d >= bestD) continue;
                            if (barrierPos.Contains(p) && barrierPos.Contains(q) == false) continue;
                            best = q; bestD = d;
                        }
                    }
                if (best != p) {
                    repOf[p] = best;
                    foreach (int l in labelsAt[p]) if (labelsAt[best].Contains(l) == false) labelsAt[best].Add(l);
                    // (and its barriers: the vertex is on the barriers of both, which classifies its boundary edges)
                    List<int> bp, bq;
                    if (barriersAt.TryGetValue(p, out bp) && barriersAt.TryGetValue(best, out bq))
                        foreach (int bar in bp) if (bq.Contains(bar) == false) bq.Add(bar);
                    continue;
                }
                long k = cx * 0x100000007L + cy;
                List<Vector2d> list;
                if (grid.TryGetValue(k, out list) == false) { list = new List<Vector2d>(); grid[k] = list; }
                list.Add(p);
            }
        }

        /// <summary> Realize: per vertex of the mesh, the planes of its faces, and whether it lies on a toe or a lateral limit </summary>
        public readonly List<List<int>> OutLabels = new List<List<int>>();
        public readonly List<bool> OnBarrier = new List<bool>();
        /// <summary> Realize: the barriers (toe segments first, then the lateral limits) through each rounded position on one </summary>
        public readonly Dictionary<Vector2d, List<int>> BarriersAt = new Dictionary<Vector2d, List<int>>();
        public int ToeBarrierCount { get { return toeBarriers; } }

        /// <summary>
        /// Repair (TERRAIN.md 5.4): collapse the edges shorter than minLength, shortest first, onto the end whose planes
        /// contain the other end's planes, so the triangles stay in their cells; vertices on the toe or a lateral limit
        /// are only removed onto another such vertex. Returns the number of collapses.
        /// </summary>
        public static int CollapseShortEdges(DMesh3 mesh, List<List<int>> labels, List<bool> onBarrier, double minLength)
        {
            int count = 0;
            for (bool again = true; again;) {
                again = false;
                var edges = mesh.EdgeIndices().Select(e => new KeyValuePair<double, int>(edge_length(mesh, e), e))
                    .Where(kv => kv.Key < minLength).OrderBy(kv => kv.Key).ThenBy(kv => kv.Value).ToList();
                foreach (var kv in edges) {
                    int e = kv.Value;
                    if (mesh.IsEdge(e) == false) continue;
                    Index2i ev = mesh.GetEdgeV(e);
                    foreach (var pair in new[] { new Index2i(ev.a, ev.b), new Index2i(ev.b, ev.a) }) {
                        int keep = pair.a, remove = pair.b;
                        if (labels[remove].All(l => labels[keep].Contains(l)) == false) continue;
                        if (onBarrier[remove] && onBarrier[keep] == false) continue;
                        DMesh3.EdgeCollapseInfo info;
                        Vector3d kp = mesh.GetVertex(keep);
                        if (mesh.CollapseEdge(keep, remove, out info) == MeshResult.Ok) {
                            mesh.SetVertex(keep, kp);
                            count++;
                            again = true;
                            break;
                        }
                    }
                }
            }
            return count;
        }

        static double edge_length(DMesh3 m, int e)
        {
            Index2i ev = m.GetEdgeV(e);
            return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b));
        }

        // A triangulation of a convex polygon (with collinear points) without triangles across collinear points,
        // maximizing the smallest quality (altitude / longest edge) by dynamic programming: no needles at nearly flat
        // corners where another triangulation exists. Collinearity is exact (Orient); quality is approximate.
        List<Index3i> triangulate(List<int> v)
        {
            int n = v.Count;
            var tris = new List<Index3i>();
            if (n == 3) {
                if (K.Orient(v[0], v[1], v[2]) != 0) tris.Add(new Index3i(v[0], v[1], v[2]));
                return tris;
            }
            if (n > 64)
                return triangulate_ears(v);
            var pos = v.Select(K.Approx).ToList();
            // best[i, j]: the best smallest quality of the sub-polygon i..j (j > i + 1), -1 if none
            var best = new double[n, n];
            var choice = new int[n, n];
            for (int len = 2; len < n; ++len)
                for (int i = 0; i + len < n; ++i) {
                    int j = i + len;
                    best[i, j] = -1; choice[i, j] = -1;
                    for (int k = i + 1; k < j; ++k) {
                        double left = (k - i >= 2) ? best[i, k] : double.MaxValue, right = (j - k >= 2) ? best[k, j] : double.MaxValue;
                        if (left < 0 || right < 0) continue;
                        double q = tri_quality(pos[i], pos[k], pos[j]);
                        if (q <= 0 && K.Orient(v[i], v[k], v[j]) == 0) continue;
                        if (q <= 0) q = 1e-300;
                        double m = Math.Min(q, Math.Min(left, right));
                        if (m > best[i, j]) { best[i, j] = m; choice[i, j] = k; }
                    }
                }
            if (choice[0, n - 1] < 0)
                return triangulate_ears(v);
            var stack = new Stack<Index2i>();
            stack.Push(new Index2i(0, n - 1));
            while (stack.Count > 0) {
                Index2i ij = stack.Pop();
                if (ij.b - ij.a < 2) continue;
                int k = choice[ij.a, ij.b];
                tris.Add(new Index3i(v[ij.a], v[k], v[ij.b]));
                stack.Push(new Index2i(ij.a, k));
                stack.Push(new Index2i(k, ij.b));
            }
            return tris;
        }

        // altitude / longest edge (0 for collinear points, approximate)
        static double tri_quality(Vector2d a, Vector2d b, Vector2d c)
        {
            double area2 = Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            double l = Math.Max(a.DistanceSquared(b), Math.Max(b.DistanceSquared(c), c.DistanceSquared(a)));
            return l > 0 ? area2 / l : 0;
        }

        // ears at exact corners of a convex polygon with collinear points: no triangle across collinear points
        List<Index3i> triangulate_ears(List<int> v)
        {
            var tris = new List<Index3i>();
            var poly = new List<int>(v);
            Func<int, bool> corner = (i) => K.Orient(poly[(i + poly.Count - 1) % poly.Count], poly[i], poly[(i + 1) % poly.Count]) != 0;
            while (poly.Count > 3) {
                int n = poly.Count;
                var isCorner = new bool[n];
                bool anyFlat = false;
                for (int i = 0; i < n; ++i) { isCorner[i] = corner(i); if (isCorner[i] == false) anyFlat = true; }
                int best = -1;
                double bestQ = -1;
                for (int i = 0; i < n; ++i) {
                    if (isCorner[i] == false) continue;
                    if (anyFlat && isCorner[(i + n - 1) % n] && isCorner[(i + 1) % n]) continue;
                    double q = quality(poly[(i + n - 1) % n], poly[i], poly[(i + 1) % n]);
                    if (q > bestQ) { bestQ = q; best = i; }
                }
                if (best < 0) throw new InvalidOperationException("SlopeMap: no ear");
                tris.Add(new Index3i(poly[(best + n - 1) % n], poly[best], poly[(best + 1) % n]));
                poly.RemoveAt(best);
            }
            if (poly.Count == 3 && K.Orient(poly[0], poly[1], poly[2]) != 0)
                tris.Add(new Index3i(poly[0], poly[1], poly[2]));
            return tris;
        }

        // smallest angle (approximate), for choosing ears
        double quality(int a, int b, int c)
        {
            Vector2d p = K.Approx(a), q = K.Approx(b), r = K.Approx(c);
            Func<Vector2d, Vector2d, Vector2d, double> angle = (x, y, z) => {
                Vector2d u = y - x, w = z - x;
                return Math.Abs(Math.Atan2(u.x * w.y - u.y * w.x, u.Dot(w)));
            };
            return Math.Min(angle(p, q, r), Math.Min(angle(q, r, p), angle(r, p, q)));
        }
    }
}
