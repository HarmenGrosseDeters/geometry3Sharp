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
        // the box of each face in the quadtree (as of its last qt_update)
        readonly List<AxisAlignedBox2d> fBox = new List<AxisAlignedBox2d>();
        readonly List<bool> fDomain = new List<bool>(), fAlive = new List<bool>();
        readonly List<bool> hAlive = new List<bool>();
        LooseQuadtree2d qt;
        readonly List<int> qtFace = new List<int>();
        // barriers: the toe segments in order (barrier id = piece index), then the lateral rays
        readonly List<int> barToe = new List<int>(), barSeg = new List<int>(), barDomainSign = new List<int>();
        int toeBarriers;
        AxisAlignedBox2d domainBox;
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
            var c = new List<int>(8);
            int h0 = fEdge[f], h = h0;
            do { c.Add(h); h = hNext[h]; if (c.Count > 100000) throw new InvalidOperationException("SlopeMap: broken cycle"); } while (h != h0);
            return c;
        }

        AxisAlignedBox2d face_box(int f)
        {
            var b = AxisAlignedBox2d.Empty;
            int h0 = fEdge[f], h = h0, guard = 0;
            do { b.Contain(K.Approx(hOrig[h])); h = hNext[h]; if (++guard > 100000) throw new InvalidOperationException("SlopeMap: broken cycle"); } while (h != h0);
            b.Expand(1e-9 * (b.MaxDim + Math.Max(Math.Max(Math.Abs(b.Min.x), Math.Abs(b.Max.x)), Math.Max(Math.Abs(b.Min.y), Math.Abs(b.Max.y)))));
            return b;
        }

        void qt_update(int f) { qt_set(f, face_box(f)); }

        void qt_set(int f, AxisAlignedBox2d box)
        {
            if (fQt[f] >= 0) qt.Remove(fQt[f]);
            fBox[f] = box;
            int id = qt.Insert(box);
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
            // (Simplify: a ray's chord split by a later ray stays a cut)
            if (cut != null) {
                if (cut.Contains(h)) cut.Add(h2);
                if (cut.Contains(t)) cut.Add(t2);
            }
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
        readonly List<int> splitCycle = new List<int>();
        int[] splitSigns = new int[64], splitSigns2 = new int[64];

        bool split_face(int f, int line, out int neg, out int pos, out int chord)
        {
            neg = -1; pos = -1; chord = -1;
            // (most calls do not cut: the signs first, into reused buffers, the same predicates as before)
            List<int> c = splitCycle;
            c.Clear();
            { int h0 = fEdge[f], x = h0; do { c.Add(x); x = hNext[x]; if (c.Count > 100000) throw new InvalidOperationException("SlopeMap: broken cycle"); } while (x != h0); }
            int n = c.Count;
            if (splitSigns.Length < n) splitSigns = new int[2 * n];
            int[] s = splitSigns;
            bool anyNeg = false, anyPos = false;
            for (int i = 0; i < n; ++i) {
                s[i] = K.Side(hOrig[c[i]], line);
                if (s[i] < 0) anyNeg = true;
                if (s[i] > 0) anyPos = true;
            }
            if (anyNeg == false) { pos = f; return false; }
            if (anyPos == false) { neg = f; return false; }
            // (c is reused buffers' content: copy the cycle, which split_edge below changes; the signs s stay as they are, the buffer is not
            // touched before the loop below ends. A clone of s copied the whole buffer, which grows with the largest face ever seen)
            c = new List<int>(c);
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
            if (splitSigns2.Length < n) splitSigns2 = new int[2 * n];
            s = splitSigns2;
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
            fEdge.Add(-1); fLabel.Add(fLabel[f]); fDomain.Add(fDomain[f]); fQt.Add(-1); fAlive.Add(true); fBox.Add(AxisAlignedBox2d.Empty);
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
            var segOf = new Dictionary<long, int>(EdgeKeyComparer.Instance);
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
            var he = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            var lineOf = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            foreach (Index3i tri in cdt.Triangles()) {
                if (tri.a >= nIn || tri.b >= nIn || tri.c >= nIn) continue;
                int[] v = { tri.a, tri.b, tri.c };
                if (ExactPredicates.Orient2d(pts[v[0]], pts[v[1]], pts[v[2]]) < 0) { int x = v[1]; v[1] = v[2]; v[2] = x; }
                int f = fEdge.Count;
                fEdge.Add(-1); fLabel.Add(-1); fDomain.Add(false); fQt.Add(-1); fAlive.Add(true); fBox.Add(AxisAlignedBox2d.Empty);
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
            domainBox = box;
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
            ordered = null;
            foreach (int p in strip) owner[p] = k;
            foreach (int p in fanA) owner[p] = k;
            foreach (int p in fanB) owner[p] = k;
            var hits = hitBuffer;
            hits.Clear();
            qt.QueryRectangle(reach, hits);
            // (the live domain faces of the hits, each once, in ascending id: a stamp per face instead of Distinct and OrderBy)
            if (faceStamp.Length < fEdge.Count) Array.Resize(ref faceStamp, 2 * fEdge.Count);
            ++stampNow;
            var faces = faceBuffer;
            faces.Clear();
            foreach (int id in hits) {
                int f = qtFace[id];
                if (f >= 0 && fAlive[f] && fDomain[f] && faceStamp[f] != stampNow) { faceStamp[f] = stampNow; faces.Add(f); }
            }
            faces.Sort();
            var relevant = new List<int>();
            foreach (int f in faces) {
                int label = fLabel[f];
                // the same prefilter on the face's box first (no cycle): the box's highest corner bounds the plane over
                // the face from above, its distance to the segment bounds the face's from below, so it skips no face
                // the test below keeps; except a face narrower than the rounding, whose approximate corners are one
                // point: the polygon test below takes its distance as 0 (a degenerate polygon counts as containing the
                // segment's end), the box (which contains the face, its expansion exceeds the error of Approx) does not
                {
                    AxisAlignedBox2d bb = fBox[f];
                    double upperB = double.MinValue;
                    for (int i = 0; i < 4; ++i) { Vector2d q = bb.GetCorner(i); upperB = Math.Max(upperB, K.ApproxHeight(label, q.x, q.y)); }
                    double dminB = box_segment_distance(bb, A.xy, B.xy);
                    double lowerB = zmin + slb * dminB;
                    double sizeB = bb.Diagonal.Length;
                    if (lowerB > upperB + 1e-9 * (Math.Abs(upperB) + Math.Abs(lowerB) + slb * (sizeB + dminB)))
                        continue;
                }
                var pos = prefilterPos;
                pos.Clear(); prefilterVerts.Clear();
                { int h0 = fEdge[f], h = h0; do { pos.Add(K.Approx(hOrig[h])); prefilterVerts.Add(hOrig[h]); h = hNext[h]; } while (h != h0); }
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

        readonly List<int> hitBuffer = new List<int>(), faceBuffer = new List<int>();
        int[] faceStamp = new int[0];
        int stampNow;
        readonly List<Vector2d> prefilterPos = new List<Vector2d>();
        readonly List<int> prefilterVerts = new List<int>();

        void add_sector(List<int> relevant, List<int> fan, List<double> ang, Vector2d apex, List<Vector2d> pos, double theta)
        {
            if (fan.Count == 0) return;
            double refAngle = double.NaN, lo = 0, hi = 0;
            for (int i = 0; i < pos.Count; ++i) {
                Vector2d q = pos[i];
                Vector2d d = q - apex;
                // (a corner at the apex (exact) has no direction: the corners next to it bound the face's. A corner near
                // it: its approximate position (within 1e-12 of its coordinates, Approx) gives its direction within
                // 0.1 theta only from a distance of 1e-11 / theta of the coordinates; nearer, all of the fan)
                if (d.x == 0 && d.y == 0 && K.IsAt(prefilterVerts[i], apex)) continue;
                double near = 1e-11 * (Math.Abs(q.x) + Math.Abs(q.y) + Math.Abs(apex.x) + Math.Abs(apex.y)) / theta;
                if (d.Length < near) { relevant.AddRange(fan); return; }
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

        // the distance of the box to the segment ab (0 if they meet), approximate as poly_segment_distance
        static double box_segment_distance(AxisAlignedBox2d box, Vector2d a, Vector2d b)
        {
            if (box.Contains(a) || box.Contains(b)) return 0;
            double d = Math.Min(point_box_distance(box, a), point_box_distance(box, b));
            var s = new Segment2d(a, b);
            for (int i = 0; i < 4; ++i) {
                Vector2d p = box.GetCorner(i), q = box.GetCorner((i + 1) % 4);
                if (segments_cross(a, b, p, q)) return 0;
                d = Math.Min(d, Math.Sqrt(s.DistanceSquared(p)));
            }
            return d;
        }

        static double point_box_distance(AxisAlignedBox2d box, Vector2d p)
        {
            double dx = Math.Max(Math.Max(box.Min.x - p.x, p.x - box.Max.x), 0), dy = Math.Max(Math.Max(box.Min.y - p.y, p.y - box.Max.y), 0);
            return Math.Sqrt(dx * dx + dy * dy);
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

        void insert_into(int f, int label, List<int> planes, List<(int line, int flip)> region = null)
        {
            // W = f ∩ {P_j < label for all j}, clipped virtually first, so that f is only split if W is not empty
            // (the corners and the lines of the edges from them, walked in place into lists of the right size)
            int n = 0;
            { int h0 = fEdge[f], h = h0; do { ++n; h = hNext[h]; if (n > 100000) throw new InvalidOperationException("SlopeMap: broken cycle"); } while (h != h0); }
            var verts = new List<int>(n + 2);
            var elines = new List<int>(n + 2);
            { int h0 = fEdge[f], h = h0; do { verts.Add(hOrig[h]); elines.Add(hLine[h]); h = hNext[h]; } while (h != h0); }
            // (a plane the same as the label's, a line that is zero everywhere: F_k equals the label where it is the
            // maximum; the lower plane id wins the tie, so that the labels do not depend on the order of the pieces)
            var applied = new List<(int line, int flip)>();
            int tied = int.MaxValue;
            // (the region of a face of another map, divide and conquer: the half-planes of its edges, kept where flip * side is not positive)
            if (region != null)
                foreach (var rc in region) {
                    int rr = clip(ref verts, ref elines, rc.line, rc.flip);
                    if (rr < 0) return;
                    if (rr == 0) continue;
                    applied.Add(rc);
                }
            foreach (int p in planes) {
                int line = K.PlaneLine(p, label);
                int r = clip(ref verts, ref elines, line);
                if (r < 0)
                    return;     // empty
                if (r == 0) { tied = Math.Min(tied, p); continue; }
                applied.Add((line, 1));
            }
            if (tied < int.MaxValue && tied > label)
                return;
            // replay the lines that bound W, in order
            var bounding = new HashSet<int>(elines);
            int cur = f;
            foreach (var ap in applied) {
                if (bounding.Contains(ap.line) == false) continue;
                int neg, pos, chord;
                split_face(cur, ap.line, out neg, out pos, out chord);
                int keep = ap.flip > 0 ? neg : pos;
                if (keep < 0) throw new InvalidOperationException("SlopeMap: replay");
                cur = keep;
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
                        // all zero: the same plane, the lower id
                        if (neg >= 0 || all_positive(pos, K.PlaneLine(p, fLabel[pos])))
                            fLabel[pos] = p;
                        else
                            fLabel[pos] = Math.Min(p, fLabel[pos]);
                        next.Add(pos);
                    }
                }
                parts = next;
            }
        }

        // ---------------------------------------------------------------- divide and conquer (prototype)

        /// <summary> a copy of the map (the kernel is shared) </summary>
        public SlopeMap Clone()
        {
            var m = new SlopeMap(K);
            m.hOrig.AddRange(hOrig); m.hTwin.AddRange(hTwin); m.hNext.AddRange(hNext); m.hPrev.AddRange(hPrev); m.hFace.AddRange(hFace);
            m.hLine.AddRange(hLine); m.hBar.AddRange(hBar); m.hBarSide.AddRange(hBarSide); m.hAlive.AddRange(hAlive);
            m.fEdge.AddRange(fEdge); m.fLabel.AddRange(fLabel); m.fDomain.AddRange(fDomain); m.fAlive.AddRange(fAlive); m.fBox.AddRange(fBox);
            for (int f = 0; f < fEdge.Count; ++f) m.fQt.Add(-1);
            m.barToe.AddRange(barToe); m.barSeg.AddRange(barSeg); m.barDomainSign.AddRange(barDomainSign);
            m.toeBarriers = toeBarriers; m.RayCount = RayCount; m.domainBox = domainBox;
            foreach (var kv in owner) m.owner[kv.Key] = kv.Value;
            m.qt = new LooseQuadtree2d(new AxisAlignedBox2d[] { domainBox }, false);
            m.qt.Remove(0);
            m.qtFace.Add(-1);
            for (int f = 0; f < fEdge.Count; ++f) if (fAlive[f]) m.qt_set(f, fBox[f]);
            return m;
        }

        public int CarveCount, LiveFacesMax;

        /// <summary>
        /// this &lt;- min(this, other): for every face r of other (label q, not the cap), the part of every face of this that lies in r and where the plane q is
        /// lower than the label is cut out and relabelled q (the tie rule of insert_into)
        /// </summary>
        public void MergeMin(SlopeMap other, int cap)
        {
            ordered = null;
            foreach (var kv in other.owner) owner[kv.Key] = kv.Value;
            var hits = new List<int>();
            var region = new List<(int line, int flip)>();
            for (int r = 0; r < other.fEdge.Count; ++r) {
                if (other.in_domain(r) == false) continue;
                int q = other.fLabel[r];
                if (q == cap || q < 0) continue;
                // the half-planes of r: for every edge the side its other corners are on
                region.Clear();
                var corners = other.cycle(r);
                bool degenerate = false;
                foreach (int e in corners) {
                    int line = other.hLine[e];
                    if (line < 0) continue;
                    int sgn = 0;
                    foreach (int e2 in corners) {
                        int sd = K.Side(other.hOrig[e2], line);
                        if (sd != 0) { sgn = sd; break; }
                    }
                    if (sgn == 0) { degenerate = true; break; }
                    region.Add((line, -sgn));
                }
                if (degenerate) continue;
                hits.Clear();
                qt.QueryRectangle(other.fBox[r], hits);
                var faces = hits.Select(id => qtFace[id]).Where(f => f >= 0 && fAlive[f] && fDomain[f]).Distinct().OrderBy(f => f).ToList();
                var rpos = new List<Vector2d>();
                foreach (int e in corners) rpos.Add(K.Approx(other.hOrig[e]));
                foreach (int f in faces) {
                    // (cheap conservative prefilters on the approximate corners, with a wide margin: the polygons are clearly apart, or the plane q is
                    // clearly higher than the label at every corner of f, so that W is empty; else the exact carve)
                    var fpos = new List<Vector2d>();
                    var fcorner = new List<int>();
                    { int h0 = fEdge[f], h = h0; do { fpos.Add(K.Approx(hOrig[h])); fcorner.Add(hOrig[h]); h = hNext[h]; } while (h != h0); }
                    if (clearly_apart(rpos, fpos) || clearly_apart(fpos, rpos)) continue;
                    if (classify(q, fLabel[f], fcorner) > 0) continue;
                    if (higher_on_overlap(rpos, fpos, q, fLabel[f])) continue;
                    if (interior_disjoint(region, fcorner, f, other, corners)) continue;
                    ++CarveCount;
                    insert_into(f, fLabel[f], new List<int> { q }, region);
                }
            }
        }

        /// <summary>
        /// exact: f and r have no interior in common (touching or apart). Two convex polygons are interior-disjoint iff one of the lines of their edges has the
        /// other on its closed outer side (separating axis): the signs of the corners against the lines of r's edges (region: kept where flip * side is not positive) and
        /// against the lines of f's edges.
        /// </summary>
        bool interior_disjoint(List<(int line, int flip)> region, List<int> fcorner, int f, SlopeMap other, List<int> rEdges)
        {
            foreach (var rc in region) {
                bool anyInside = false;
                foreach (int v in fcorner) if (rc.flip * K.Side(v, rc.line) < 0) { anyInside = true; break; }
                if (!anyInside) return true;
            }
            int h0 = fEdge[f], h = h0;
            do {
                int line = hLine[h];
                if (line >= 0) {
                    int sgn = 0;
                    foreach (int v in fcorner) { int sd = K.Side(v, line); if (sd != 0) { sgn = sd; break; } }
                    if (sgn != 0) {
                        bool anyInside = false;
                        foreach (int e in rEdges) if (sgn * K.Side(other.hOrig[e], line) > 0) { anyInside = true; break; }
                        if (!anyInside) return true;
                    }
                }
                h = hNext[h];
            } while (h != h0);
            return false;
        }

        readonly List<Vector2d> clipA = new List<Vector2d>(), clipB = new List<Vector2d>();

        /// <summary>
        /// true if the plane q is clearly higher than the label's plane everywhere on f ∩ r, so that no part of f is carved: the approximate polygon f clipped to
        /// the edges of r, each pushed outwards by the error margin (a superset of the true overlap), and the heights at its corners (linear, so the minimum
        /// is at a corner) against the margin of classify. A sliver of the true overlap is inside the expanded polygon, so it is never skipped wrongly.
        /// </summary>
        bool higher_on_overlap(List<Vector2d> rpos, List<Vector2d> fpos, int q, int label)
        {
            double scale = 0, ext = 0;
            foreach (var c in rpos) scale = Math.Max(scale, Math.Abs(c.x) + Math.Abs(c.y));
            foreach (var c in fpos) { scale = Math.Max(scale, Math.Abs(c.x) + Math.Abs(c.y)); ext = Math.Max(ext, Math.Abs(c.x - fpos[0].x) + Math.Abs(c.y - fpos[0].y)); }
            var cur = clipA; cur.Clear(); cur.AddRange(fpos);
            var nxt = clipB;
            for (int i = 0; i < rpos.Count && cur.Count > 0; ++i) {
                Vector2d p = rpos[i], qq = rpos[(i + 1) % rpos.Count];
                double ex = qq.x - p.x, ey = qq.y - p.y, len = Math.Abs(ex) + Math.Abs(ey);
                if (len == 0) continue;
                double tol = 1e-10 * scale * (len + ext + Math.Abs(p.x - fpos[0].x) + Math.Abs(p.y - fpos[0].y));
                nxt.Clear();
                for (int j = 0; j < cur.Count; ++j) {
                    Vector2d a = cur[j], b = cur[(j + 1) % cur.Count];
                    double sa = ex * (a.y - p.y) - ey * (a.x - p.x) + tol, sb = ex * (b.y - p.y) - ey * (b.x - p.x) + tol;
                    if (sa >= 0) nxt.Add(a);
                    if ((sa > 0 && sb < 0) || (sa < 0 && sb > 0)) nxt.Add(a + (sa / (sa - sb)) * (b - a));
                }
                var tmp = cur; cur = nxt; nxt = tmp;
            }
            if (cur.Count == 0) return true;
            double minD = double.MaxValue, sc = 0, span = 0;
            foreach (var c in cur) {
                double hp = K.ApproxHeight(q, c.x, c.y), hl = K.ApproxHeight(label, c.x, c.y);
                minD = Math.Min(minD, hp - hl);
                sc = Math.Max(sc, Math.Max(Math.Abs(hp), Math.Abs(hl)));
                span = Math.Max(span, Math.Abs(c.x) + Math.Abs(c.y));
            }
            return minD > (1e-9 * sc + 3e-10 * span + 1e-12);
        }

        // all corners of b strictly outside one edge of the counter-clockwise convex polygon a, by a margin
        static bool clearly_apart(List<Vector2d> a, List<Vector2d> b)
        {
            double scale = 0;
            foreach (var p in a) scale = Math.Max(scale, Math.Abs(p.x) + Math.Abs(p.y));
            foreach (var p in b) scale = Math.Max(scale, Math.Abs(p.x) + Math.Abs(p.y));
            for (int i = 0; i < a.Count; ++i) {
                Vector2d p = a[i], q = a[(i + 1) % a.Count];
                double ex = q.x - p.x, ey = q.y - p.y, len = Math.Abs(ex) + Math.Abs(ey);
                bool outside = true;
                foreach (var c in b) {
                    // (the positions are within 1e-12 of the coordinates; the product's error is that times (length + distance); a factor 100 on top)
                    double dist = Math.Abs(c.x - p.x) + Math.Abs(c.y - p.y);
                    double tol = 1e-10 * scale * (len + dist);
                    if (ex * (c.y - p.y) - ey * (c.x - p.x) >= -tol) { outside = false; break; }
                }
                if (outside) return true;
            }
            return false;
        }

        /// <summary>
        /// +1 if the plane p is higher than the label's plane at every one of the corners, -1 if lower at every one, 0 if that is not clear: by the
        /// approximate corners and the interval midpoints of the planes with a wide margin (both planes are linear, so the sign holds on the polygon)
        /// </summary>
        int classify(int p, int label, IList<int> corners)
        {
            double minD = double.MaxValue, maxD = double.MinValue, scale = 0, span = 0;
            for (int i = 0; i < corners.Count; ++i) {
                Vector2d q = K.Approx(corners[i]);
                double hp = K.ApproxHeight(p, q.x, q.y), hl = K.ApproxHeight(label, q.x, q.y);
                double d = hp - hl;
                minD = Math.Min(minD, d); maxD = Math.Max(maxD, d);
                scale = Math.Max(scale, Math.Max(Math.Abs(hp), Math.Abs(hl)));
                span = Math.Max(span, Math.Abs(q.x) + Math.Abs(q.y));
            }
            double tol = (1e-9 * scale + 3e-10 * span + 1e-12);
            if (minD > tol) return 1;
            if (maxD < -tol) return -1;
            return 0;
        }

        /// <summary> merge neighbours of one label while the union stays convex (all faces); returns the number of live domain faces </summary>
        public int Compact()
        {
            var cand = new List<int>();
            for (int f = 0; f < fEdge.Count; ++f) if (in_domain(f)) cand.Add(f);
            foreach (int f in cand) {
                if (in_domain(f) == false) continue;
                bool again = true;
                while (again) {
                    again = false;
                    int h0 = fEdge[f], h = h0;
                    do {
                        int t = hTwin[h], g = t >= 0 ? hFace[t] : -1;
                        if (g >= 0 && g != f && hBar[h] < 0 && hBar[t] < 0 && in_domain(g) && fLabel[g] == fLabel[f] && try_merge(f, g, h)) { again = true; break; }
                        h = hNext[h];
                    } while (h != h0);
                }
            }
            int live = 0;
            for (int f = 0; f < fEdge.Count; ++f) if (in_domain(f)) ++live;
            LiveFacesMax = Math.Max(LiveFacesMax, live);
            return live;
        }

        // p -> u -> x does not clearly turn left? By the approximate positions with a wide margin (Approx is within 1e-12 of the coordinates: the margin
        // below is 1e-9 of the products); a straight end on another line counts as a turn: not merging is always safe
        bool turns_right(int p, int u, int x)
        {
            Vector2d P = K.Approx(p), U = K.Approx(u), X = K.Approx(x);
            double ax = U.x - P.x, ay = U.y - P.y, bx = X.x - U.x, by = X.y - U.y;
            double cross = ax * by - ay * bx;
            double scale = Math.Abs(P.x) + Math.Abs(P.y) + Math.Abs(U.x) + Math.Abs(U.y) + Math.Abs(X.x) + Math.Abs(X.y);
            double tol = 1e-9 * (Math.Abs(ax) + Math.Abs(ay) + Math.Abs(bx) + Math.Abs(by) + 1e-300) * (scale + 1e-300);
            return cross <= tol;
        }

        // merge face g into f across the run of their shared edges that contains h (f's half-edge with g behind it)
        bool try_merge(int f, int g, int h)
        {
            int s = h, guard = 0;
            while (hFace[hTwin[hPrev[s]]] == g && hPrev[s] != h) { s = hPrev[s]; if (++guard > 100000) return false; }
            int e = s, m = 1;
            while (hFace[hTwin[hNext[e]]] == g && hNext[e] != s) { e = hNext[e]; ++m; if (m > 100000) return false; }
            int shared = 0, nf = 0, ng = 0;
            { int x = fEdge[f]; do { ++nf; if (hFace[hTwin[x]] == g) ++shared; x = hNext[x]; } while (x != fEdge[f]); }
            { int x = fEdge[g]; do { ++ng; x = hNext[x]; } while (x != fEdge[g]); }
            if (shared != m || m >= nf || m >= ng) return false;
            for (int x = s, i = 0; i < m; x = hNext[x], ++i)
                if (hBar[x] >= 0 || hBar[hTwin[x]] >= 0) return false;
            int ts = hTwin[s], te = hTwin[e];
            for (int x = s, i = 0; i + 1 < m; x = hNext[x], ++i)
                if (hNext[hTwin[hNext[x]]] != hTwin[x]) return false;
            int a = hPrev[s], b = hNext[e], c = hPrev[te], d = hNext[ts];
            if (on_one_line(a, d) == false && turns_right(hOrig[a], hOrig[s], hOrig[hNext[d]])) return false;
            if (on_one_line(c, b) == false && turns_right(hOrig[c], hOrig[te], hOrig[hNext[b]])) return false;
            for (int x = s, i = 0; i < m; x = hNext[x], ++i) { hAlive[x] = false; hAlive[hTwin[x]] = false; }
            hNext[a] = d; hPrev[d] = a; hNext[c] = b; hPrev[b] = c;
            fEdge[f] = a;
            int y = a;
            do { hFace[y] = f; y = hNext[y]; } while (y != a);
            fAlive[g] = false;
            if (fQt[g] >= 0) { qt.Remove(fQt[g]); fQt[g] = -1; }
            fEdge[g] = -1;
            if (a != b && c != d && d != b && a != c) { try_remove_vertex(d); try_remove_vertex(b); }
            qt_update(f);
            return true;
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

        // keep the part of the polygon where the line is negative or zero: -1 if no corner is negative (nothing is
        // left, or a line zero at every corner, of two planes that are the same, which leaves the polygon as it is: 0)
        int clip(ref List<int> verts, ref List<int> elines, int line, int flip = 1)
        {
            int n = verts.Count;
            var s = new int[n];
            bool anyNeg = false, anyPos = false;
            for (int i = 0; i < n; ++i) {
                s[i] = flip * K.Side(verts[i], line);
                if (s[i] < 0) anyNeg = true; else if (s[i] > 0) anyPos = true;
            }
            if (anyNeg == false) return anyPos ? -1 : 0;
            if (anyPos == false) return 1;
            // (vertex, line of the edge from it) of the part where the line is negative or zero
            var nv = new List<int>(n + 2); var nl = new List<int>(n + 2);
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
            return 1;
        }

        // ---------------------------------------------------------------- simplification

        /// <summary>
        /// Replace the faces by the canonical partition of the labelled domain, a function of the labels only and not of
        /// the order in which the pieces split the faces (docs/design/TERRAIN_1C_PLAN.md, section 9):
        /// a component (the faces of one label, connected across edges that are no barrier) is cut along a ray from
        /// each of its reflex corners, the extension of the edge into the corner up to the first point of the
        /// component's boundary; each part of a component between the rays is one convex face, and a vertex of degree
        /// two between collinear edges of one barrier (or none) is removed. A convex component is one face. Exact:
        /// Orient and Side only. The faces are then listed in a canonical order (OrderedFaces).
        /// </summary>
        public void Simplify()
        {
            int nf = fEdge.Count;
            // the components
            var uf = new int[nf];
            for (int i = 0; i < nf; ++i) uf[i] = i;
            for (int f = 0; f < nf; ++f) {
                if (in_domain(f) == false) continue;
                int h0 = fEdge[f], h = h0;
                do {
                    int g = hFace[hTwin[h]];
                    if (hBar[h] < 0 && g >= 0 && in_domain(g) && fLabel[g] == fLabel[f]) union(uf, f, g);
                    h = hNext[h];
                } while (h != h0);
            }
            comp = new List<int>(nf);
            for (int f = 0; f < nf; ++f) comp.Add(in_domain(f) ? find(uf, f) : -1);
            cut = new HashSet<int>();
            // the reflex corners of their boundaries: (the boundary edge out of the corner, the line of the edge into
            // it, the start of that edge)
            var rays = new List<Index3i>();
            for (int f = 0; f < nf; ++f) {
                if (comp[f] < 0) continue;
                int h0 = fEdge[f], h = h0;
                do {
                    if (separates(h)) {
                        int x = next_boundary(h);
                        if (reflex(h, x)) rays.Add(new Index3i(x, hLine[h], hOrig[h]));
                    }
                    h = hNext[h];
                } while (h != h0);
            }
            foreach (Index3i r in rays) cut_ray(r.a, r.b, r.c);
            RayCount = rays.Count;
            // the parts between the rays: one face each
            nf = fEdge.Count;
            uf = new int[nf];
            for (int i = 0; i < nf; ++i) uf[i] = i;
            for (int f = 0; f < nf; ++f) {
                if (fAlive[f] == false || comp[f] < 0) continue;
                int h0 = fEdge[f], h = h0;
                do {
                    if (separates(h) == false && cut.Contains(h) == false) union(uf, f, hFace[hTwin[h]]);
                    h = hNext[h];
                } while (h != h0);
            }
            var members = new Dictionary<int, List<int>>();
            for (int f = 0; f < nf; ++f) {
                if (fAlive[f] == false || comp[f] < 0) continue;
                int r = find(uf, f);
                List<int> l;
                if (members.TryGetValue(r, out l) == false) { l = new List<int>(); members[r] = l; }
                l.Add(f);
            }
            foreach (var kv in members)
                if (kv.Value.Count > 1) merge_part(kv.Key, kv.Value);
            // the vertices left in the middle of a straight edge
            for (int f = 0; f < nf; ++f) {
                if (fAlive[f] == false || comp[f] < 0) continue;
                for (bool again = true; again;) {
                    again = false;
                    foreach (int h in cycle(f))
                        if (hAlive[h] && hFace[h] == f && try_remove_vertex(h)) again = true;
                }
            }
            comp = null; cut = null;
            ordered = null;
            OrderedFaces();
        }

        /// <summary> Simplify: the number of rays cut (the reflex corners of the components) </summary>
        internal int RayCount;

        List<int> comp;
        HashSet<int> cut;
        List<int> ordered;

        bool in_domain(int f) { return f >= 0 && fAlive[f] && fDomain[f]; }

        static int find(int[] uf, int x)
        {
            while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; }
            return x;
        }

        // (the smaller root becomes the root: the roots do not depend on the order of the unions)
        static void union(int[] uf, int a, int b)
        {
            a = find(uf, a); b = find(uf, b);
            if (a < b) uf[b] = a; else if (b < a) uf[a] = b;
        }

        int comp_of(int f) { return (f < 0 || f >= comp.Count) ? -1 : comp[f]; }

        // the half-edge lies between two components (a barrier, or another label, or outside the domain)
        bool separates(int h) { return hBar[h] >= 0 || comp_of(hFace[h]) != comp_of(hFace[hTwin[h]]); }

        // the next boundary half-edge of the component after h (around the end of h, through the component's faces)
        int next_boundary(int h)
        {
            int x = hNext[h];
            for (int guard = 0; separates(x) == false; ++guard) {
                if (guard > 100000) throw new InvalidOperationException("SlopeMap: broken boundary");
                x = hNext[hTwin[x]];
            }
            return x;
        }

        // the component's corner between its boundary edges h (u -> v) and x (v -> w) is reflex (the component lies to
        // the left of its boundary: the faces are counter-clockwise); a slit's tip (w = u) is a full turn
        bool reflex(int h, int x)
        {
            int u = hOrig[h], v = hOrig[x], w = hOrig[hTwin[x]];
            if (u == w) return true;
            if (on_one_line(h, x)) return false;
            return K.Orient(u, v, w) < 0;
        }

        // the half-edges out of the origin of e (all of them: around the box boundary from one outside half-edge to
        // the other)
        readonly List<int> outBuffer = new List<int>();
        List<int> outgoing(int e0)
        {
            var r = outBuffer;
            r.Clear();
            int e = e0;
            bool open = false;
            do {
                r.Add(e);
                if (r.Count > 100000) throw new InvalidOperationException("SlopeMap: broken vertex");
                int t = hTwin[e];
                if (hFace[t] < 0) { open = true; break; }
                e = hNext[t];
            } while (e != e0);
            if (open) {
                e = e0;
                while (hFace[e] >= 0) {
                    e = hTwin[hPrev[e]];
                    if (e == e0) break;
                    r.Add(e);
                    if (r.Count > 100000) throw new InvalidOperationException("SlopeMap: broken vertex");
                }
            }
            return r;
        }

        // the line crosses the face's interior (corners on both sides)
        bool crosses(int f, int line)
        {
            bool neg = false, pos = false;
            int h0 = fEdge[f], h = h0;
            do {
                int s = K.Side(hOrig[h], line);
                if (s < 0) neg = true; else if (s > 0) pos = true;
                if (neg && pos) return true;
                h = hNext[h];
            } while (h != h0);
            return false;
        }

        // Cut the component along the ray from the corner (the origin of x) on the line, away from u, up to the first
        // point of the component's boundary. The faces are convex, so the ray crosses each face it enters along one
        // chord that a split by the whole line makes; at a vertex it goes on in the face that the line crosses there
        // (the face behind holds the chord, and no face has the line through its interior behind the corner: the
        // edge into the corner is there), or along an edge on the line.
        void cut_ray(int x, int line, int u)
        {
            int c = comp_of(hFace[x]);
            int e = x, at = hOrig[x];
            // the points of a line are in the order of CompareXY along it: the ray's direction in that order
            int dir = K.CompareXY(at, u);
            for (int guard = 0; ; ++guard) {
                if (guard > hOrig.Count) throw new InvalidOperationException("SlopeMap: ray");
                List<int> outs = outgoing(e);
                if (guard > 0)
                    foreach (int o in outs) if (separates(o)) return;
                int ahead = -1, along = -1;
                foreach (int o in outs) {
                    int f = hFace[o];
                    if (comp_of(f) != c) continue;
                    if (crosses(f, line)) { ahead = f; break; }
                    int w = hOrig[hTwin[o]];
                    if (along < 0 && K.Side(w, line) == 0 && K.CompareXY(w, at) == dir) along = o;
                }
                if (ahead >= 0) {
                    int neg, pos, chord;
                    split_face(ahead, line, out neg, out pos, out chord);
                    while (comp.Count < fEdge.Count) comp.Add(c);
                    cut.Add(chord); cut.Add(hTwin[chord]);
                    int a0 = hOrig[chord], a1 = hOrig[hTwin[chord]];
                    if (a0 == at) { at = a1; e = hTwin[chord]; }
                    else if (a1 == at) { at = a0; e = chord; }
                    else throw new InvalidOperationException("SlopeMap: ray chord");
                    continue;
                }
                if (along < 0 || separates(along)) throw new InvalidOperationException("SlopeMap: ray");
                cut.Add(along); cut.Add(hTwin[along]);
                at = hOrig[hTwin[along]];
                e = hTwin[along];
            }
        }

        // the faces of a part (convex, simply connected) become the face 'rep': its boundary half-edges linked in one
        // cycle, the half-edges inside removed
        void merge_part(int rep, List<int> faces)
        {
            Func<int, bool> bound = (h) => separates(h) || cut.Contains(h);
            var boundary = new List<int>();
            foreach (int f in faces)
                foreach (int h in cycle(f)) {
                    if (bound(h)) boundary.Add(h);
                    else hAlive[h] = false;
                }
            var next = new Dictionary<int, int>(boundary.Count);
            foreach (int h in boundary) {
                int x = hNext[h];
                for (int guard = 0; bound(x) == false; ++guard) {
                    if (guard > 100000) throw new InvalidOperationException("SlopeMap: broken part");
                    x = hNext[hTwin[x]];
                }
                next[h] = x;
            }
            int h0 = boundary[0], y = h0, count = 0;
            do {
                int z = next[y];
                hNext[y] = z; hPrev[z] = y; hFace[y] = rep;
                ++count;
                y = z;
            } while (y != h0 && count <= boundary.Count);
            if (count != boundary.Count)
                throw new InvalidOperationException("SlopeMap: a part is not simply connected");
            foreach (int f in faces) if (f != rep) fAlive[f] = false;
            fEdge[rep] = h0;
        }

        /// <summary>
        /// the domain faces in a canonical order (their corners from the lowest in CompareXY, compared in turn, then the
        /// label), each face's cycle starting at its lowest corner: after Simplify, a function of the partition only
        /// </summary>
        public List<int> OrderedFaces()
        {
            if (ordered != null) return ordered;
            var faces = new List<int>();
            for (int f = 0; f < fEdge.Count; ++f) {
                if (in_domain(f) == false) continue;
                int best = fEdge[f];
                foreach (int h in cycle(f))
                    if (K.CompareXY(hOrig[h], hOrig[best]) < 0) best = h;
                fEdge[f] = best;
                faces.Add(f);
            }
            faces.Sort((a, b) => {
                if (a == b) return 0;
                int ha = fEdge[a], hb = fEdge[b];
                do {
                    int c = K.CompareXY(hOrig[ha], hOrig[hb]);
                    if (c != 0) return c;
                    ha = hNext[ha]; hb = hNext[hb];
                } while (ha != fEdge[a] && hb != fEdge[b]);
                if (ha == fEdge[a] && hb != fEdge[b]) return -1;
                if (hb == fEdge[b] && ha != fEdge[a]) return 1;
                if (fLabel[a] != fLabel[b]) return fLabel[a].CompareTo(fLabel[b]);
                return a.CompareTo(b);
            });
            ordered = faces;
            return ordered;
        }

        // the half-edges x and y lie on the same line of the kernel (both ends of every half-edge lie exactly on its line:
        // the crossings of a split are on both lines, a chord is on the splitting line, and a merge or a vertex removal
        // keeps the lines of the edges it keeps), so their ends are exactly collinear
        bool on_one_line(int x, int y) { return hLine[x] >= 0 && hLine[x] == hLine[y]; }

        // h: v -> w in a face; v of degree 2 (its two edges are h and the twin of hPrev[h]) and straight: remove it
        bool try_remove_vertex(int h)
        {
            if (removable(h) == false) return false;
            int p = hPrev[h];
            int t = hTwin[h], tp = hTwin[p];        // t: w -> v, tp: v -> u
            int w = hOrig[t];
            int f = hFace[h], g = hFace[t];
            // keep p (u -> v becomes u -> w) and its twin tp (v -> u becomes w -> u)
            int hn = hNext[h], tn = hPrev[t];
            hNext[p] = hn; hPrev[hn] = p;
            hOrig[tp] = w;
            if (tn >= 0) { hNext[tn] = tp; hPrev[tp] = tn; }
            hAlive[h] = false; hAlive[t] = false;
            if (f >= 0 && fEdge[f] == h) fEdge[f] = p;
            if (g >= 0 && fEdge[g] == t) fEdge[g] = tp;
            return true;
        }

        bool removable(int h)
        {
            int p = hPrev[h];
            int v = hOrig[h];
            int t = hTwin[h], tp = hTwin[p];        // t: w -> v, tp: v -> u
            if (hFace[h] < 0) return false;
            if (hFace[t] < 0 || hFace[tp] < 0) {
                // on the box boundary: both edges on it (nothing is outside)
                if (hFace[t] >= 0 || hFace[tp] >= 0) return false;
            } else if (hNext[t] != tp) return false;       // another edge at v
            if (hBar[h] != hBar[p]) return false;
            int u = hOrig[p], w = hOrig[t];
            if (on_one_line(p, h) == false && K.Orient(u, v, w) != 0) return false;
            return true;
        }

        /// <summary>
        /// tests: what makes the faces no canonical partition after Simplify ("" if nothing): a domain face that is not
        /// strictly convex at a corner (exact), or a vertex that Simplify removes
        /// </summary>
        internal string CanonicalProblem()
        {
            // the links: every live half-edge of a domain face in one cycle of that face, twins paired
            var inCycle = new HashSet<int>();
            foreach (int f in OrderedFaces()) {
                foreach (int h in cycle(f)) {
                    if (hAlive[h] == false) return $"face {f}: a dead half-edge in its cycle";
                    if (hFace[h] != f) return $"face {f}: a half-edge of face {hFace[h]} in its cycle";
                    if (hPrev[hNext[h]] != h) return $"face {f}: next and prev disagree";
                    if (hTwin[hTwin[h]] != h || hOrig[hTwin[h]] != hOrig[hNext[h]]) return $"face {f}: a twin does not match";
                    if (inCycle.Add(h) == false) return $"face {f}: a half-edge twice";
                }
            }
            for (int h = 0; h < hOrig.Count; ++h)
                if (hAlive[h] && in_domain(hFace[h]) && inCycle.Contains(h) == false) return $"half-edge {h} of face {hFace[h]} is in no cycle";
            foreach (int f in OrderedFaces()) {
                var c = cycle(f);
                bool turns = false;
                for (int i = 0; i < c.Count; ++i) {
                    int o = K.Orient(hOrig[c[i]], hOrig[c[(i + 1) % c.Count]], hOrig[c[(i + 2) % c.Count]]);
                    if (o < 0) return $"face {f} is not convex";
                    if (o > 0) turns = true;
                    if (removable(c[i])) return $"face {f}: a vertex of degree 2 on a straight edge";
                }
                if (turns == false) return $"face {f} has no area";
            }
            return "";
        }

        // ---------------------------------------------------------------- results

        /// <summary> pieces whose toe segment has a face of another piece on it, lower than the toe by more than tol </summary>
        public List<int> Undercuts(List<Vector3d[]> pieceEnds, double tol)
        {
            List<UndercutCause> causes;
            return Undercuts(pieceEnds, tol, out causes);
        }

        /// <summary> an undercut toe segment (piece), the piece whose face lies on it, and a point of it where it does </summary>
        public struct UndercutCause { public int Piece, Cause; public Vector2d At; }

        /// <summary> Undercuts, with the first cause of each undercut segment (in the order of OrderedFaces) </summary>
        public List<int> Undercuts(List<Vector3d[]> pieceEnds, double tol, out List<UndercutCause> causes)
        {
            var result = new SortedSet<int>();
            var first = new Dictionary<int, UndercutCause>();
            foreach (int h in OrderedFaces().SelectMany(cycle)) {
                int b = hBar[h], f = hFace[h];
                if (b < 0 || b >= toeBarriers) continue;
                int o;
                if (owner.TryGetValue(fLabel[f], out o) == false || o == b || o < 0) continue;
                Vector3d A = pieceEnds[b][0], B = pieceEnds[b][1];
                foreach (int v in new[] { hOrig[h], hOrig[hTwin[h]] }) {
                    Vector2d q = K.Rounded(v);
                    double t = (q - A.xy).Dot(B.xy - A.xy) / (B.xy - A.xy).LengthSquared;
                    double z = A.z + t * (B.z - A.z);
                    if (z - K.ApproxHeight(fLabel[f], q.x, q.y) > tol) {
                        result.Add(b);
                        if (first.ContainsKey(b) == false) first[b] = new UndercutCause() { Piece = b, Cause = o, At = q };
                    }
                }
            }
            causes = result.Select(b => first[b]).ToList();
            return result.ToList();
        }

        /// <summary>
        /// the slope faces as a TIN: vertices rounded to the nearest doubles, z the lowest incident plane there minus
        /// 'lower' (rounded to nearest from the exact value), faces triangulated at their exact corners; group id = piece;
        /// z negated for fill
        /// </summary>
        public DMesh3 Realize(int cap, bool fill, double mergeRadius = 0, double lower = 0)
        {
            var mesh = new DMesh3(false, false, false, true);
            // the planes incident to each rounded position (all domain faces, the cap too)
            var labelsAt = new Dictionary<Vector2d, List<int>>();
            var faces = new List<int>();
            foreach (int f in OrderedFaces()) {
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
                foreach (int l in labelsAt[r]) z = Math.Min(z, K.Height(l, r.x, r.y, lower));
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
            foreach (var l in BarriersAt.Values) l.Sort();
            merge_close_positions(labelsAt, barrierPos, BarriersAt, mergeRadius, repOf);
            DroppedTriangles = append_faces(mesh,
                faces.Select(f => new KeyValuePair<int, List<int>>(owner[fLabel[f]], cycle(f).Select(h => hOrig[h]).ToList())),
                triangulate, outVertex);
            foreach (int v in mesh.VertexIndices()) OnBarrier.Add(barrierPos.Contains(mesh.GetVertex(v).xy));
            return mesh;
        }

        // Positions nearer than the radius become one vertex (z the lowest of the planes of all of them; the exact check
        // of every triangle decides), and a position on a barrier only goes to another one on a barrier. Near-degenerate
        // inputs need it: on a regular polygon the line where the strips of two pieces are equal passes within a few
        // ulps of the opposite vertex, which leaves a cluster of map vertices there; collapsing their edges one by one
        // can fail the link condition and leave a degenerate triangle.
        void merge_close_positions(Dictionary<Vector2d, List<int>> labelsAt, HashSet<Vector2d> barrierPos,
                                          Dictionary<Vector2d, List<int>> barriersAt, double radius,
                                          Dictionary<Vector2d, Vector2d> repOf)
        {
            MergedCount = 0;
            if (!(radius > 0)) return;
            var order = labelsAt.Keys.ToList();
            order.Sort((a, b) => {
                bool ba = barrierPos.Contains(a), bb = barrierPos.Contains(b);
                if (ba != bb) return ba ? -1 : 1;
                int ca = labelsAt[a].Count, cb = labelsAt[b].Count;
                if (ca != cb) return cb.CompareTo(ca);
                return a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
            });
            var grid = new Dictionary<long, List<Vector2d>>(EdgeKeyComparer.Instance);
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
                    MergedCount++;
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
        /// <summary> Realize: a triangle would make the mesh non-manifold (a merge can; caught to realize without it) </summary>
        public sealed class NonManifoldRealization : InvalidOperationException
        {
            public NonManifoldRealization() : base("SlopeMap: non-manifold realization") { }
        }

        // The faces (group, corners as kernel vertices) triangulated at their exact corners and appended with their
        // rounded vertices (outVertex). A face narrower than the rounding, eg between two creases a few ulps apart, has
        // no area once rounded: its triangles are dropped (kept, they would be vertical walls over their neighbours) and
        // the neighbours stitched, which leaves no gap. Returns the number dropped. (Static: tests feed it faces.)
        internal static int append_faces(DMesh3 mesh, IEnumerable<KeyValuePair<int, List<int>>> faces,
                                         Func<List<int>, List<Index3i>> triangulate, Func<int, int> outVertex)
        {
            var dropped = new List<Index3i>();
            foreach (var face in faces) {
                foreach (Index3i t in triangulate(face.Value)) {
                    int a = outVertex(t.a), b = outVertex(t.b), c = outVertex(t.c);
                    if (a == b || b == c || c == a) continue;
                    if (ExactPredicates.Orient2d(mesh.GetVertex(a).xy, mesh.GetVertex(b).xy, mesh.GetVertex(c).xy) == 0) {
                        dropped.Add(new Index3i(a, b, c));
                        continue;
                    }
                    int tid = mesh.AppendTriangle(a, b, c, face.Key);
                    if (tid < 0) throw new NonManifoldRealization();
                }
            }
            stitch(mesh, dropped);
            return dropped.Count;
        }

        // A dropped triangle (rounded corners exactly collinear) leaves its middle corner m on the long edge (e0, e1) of
        // the triangle on the other side: that triangle is split at m, which lies exactly on the edge, so the mesh stays
        // conforming (no T-junction, no crack). Repeated until nothing changes (slivers next to each other).
        internal static void stitch(DMesh3 mesh, List<Index3i> dropped)
        {
            for (bool again = dropped.Count > 0; again;) {
                again = false;
                foreach (Index3i d in dropped) {
                    int[] v = { d.a, d.b, d.c };
                    // the middle corner: between the other two along their line
                    for (int k = 0; k < 3; ++k) {
                        int m = v[k], e0 = v[(k + 1) % 3], e1 = v[(k + 2) % 3];
                        Vector2d pm = mesh.GetVertex(m).xy, p0 = mesh.GetVertex(e0).xy, p1 = mesh.GetVertex(e1).xy;
                        if ((pm - p0).Dot(p1 - p0) <= 0 || (pm - p1).Dot(p0 - p1) <= 0) continue;
                        int e = mesh.FindEdge(e0, e1);
                        if (e < 0) break;
                        Index2i et = mesh.GetEdgeT(e);
                        if (et.b >= 0) break;
                        int t = et.a;
                        Index3i tv = mesh.GetTriangle(t);
                        // the triangle runs e0 -> e1 or e1 -> e0; x is its third vertex
                        int x = tv.a != e0 && tv.a != e1 ? tv.a : (tv.b != e0 && tv.b != e1 ? tv.b : tv.c);
                        bool forward = (tv.a == e0 && tv.b == e1) || (tv.b == e0 && tv.c == e1) || (tv.c == e0 && tv.a == e1);
                        int g = mesh.GetTriangleGroup(t);
                        mesh.RemoveTriangle(t, false, false);
                        // (an edge m x that exists already would leave a hole: non-manifold, as for any other triangle)
                        bool ok = forward ? mesh.AppendTriangle(e0, m, x, g) >= 0 && mesh.AppendTriangle(m, e1, x, g) >= 0
                                          : mesh.AppendTriangle(e1, m, x, g) >= 0 && mesh.AppendTriangle(m, e0, x, g) >= 0;
                        if (ok == false) throw new NonManifoldRealization();
                        again = true;
                        break;
                    }
                }
            }
        }

        /// <summary> Realize: triangles dropped because their rounded corners are collinear </summary>
        public int DroppedTriangles;

        /// <summary> Realize: the positions merged into another one (0 without a merge radius) </summary>
        public int MergedCount;

        // the triangles of 'remove' that stay (those without 'keep') with 'keep' in its place: counter-clockwise (exact)
        // and not thinner than half the collapse threshold minLength unless they were before (a collapse next to
        // collinear vertices can
        // otherwise leave a needle whose slope no margin covers)
        static bool collapse_keeps_shapes(DMesh3 mesh, int keep, int remove, double minLength)
        {
            Vector2d k = mesh.GetVertex(keep).xy;
            foreach (int t in mesh.VtxTrianglesItr(remove)) {
                Index3i tv = mesh.GetTriangle(t);
                if (tv.a == keep || tv.b == keep || tv.c == keep) continue;
                Vector2d a = mesh.GetVertex(tv.a).xy, b = mesh.GetVertex(tv.b).xy, c = mesh.GetVertex(tv.c).xy;
                double before = altitude(a, b, c);
                if (tv.a == remove) a = k; else if (tv.b == remove) b = k; else c = k;
                if (ExactPredicates.Orient2d(a, b, c) <= 0) return false;
                double after = altitude(a, b, c);
                if (after < 0.5 * minLength && after < before) return false;
            }
            return true;
        }

        static double altitude(Vector2d a, Vector2d b, Vector2d c)
        {
            double area2 = Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            double l = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a)));
            return l > 0 ? area2 / l : 0;
        }

        /// <summary>
        /// Repair of needles: a triangle thinner than minAltitude has its longest edge flipped if the two new triangles
        /// are proper (the old diagonal's ends strictly on both sides of the new one, exact; counter-clockwise when the
        /// old ones are, and the exact check of every triangle afterwards catches any other) and the thinner of them is thicker than the thinner of the two old ones; both
        /// take the piece (group) of the thicker old triangle. A face of the map narrower than the rounding (two creases
        /// that meet at an angle of 1e-12, from nearly coplanar tangent planes of neighbouring apexes) can only be
        /// triangulated into needles; across its sides the flips remove them. Vertices do not move; the exact check of
        /// every triangle decides afterwards. Each triangle carries a bound of its deviation from the realization (0 for
        /// the realized ones): a flip changes the surface most at the crossing X of the two diagonals, by
        /// |z_ab(X) - z_cd(X)|, so the new triangles get the larger bound of the old two plus that (and the rounding of
        /// its evaluation); a flip whose bound would exceed maxDeviation is not made (the share reserved for it,
        /// TerrainSlopeDesign.MergeShare and RepairLambda). The new
        /// triangles are checked against the thicker old triangle's piece; reaching less than w into a neighbouring
        /// piece, they may be steeper than that piece's spec allows only within the reserved shares. Returns the number
        /// of flips.
        /// </summary>
        public static int FlipNeedles(DMesh3 mesh, double minAltitude, double maxDeviation)
        {
            int count = 0;
            var dev = new Dictionary<int, double>();
            Func<int, double> devOf = (x) => { double v; return dev.TryGetValue(x, out v) ? v : 0; };
            for (int pass = 0; pass < 64; ++pass) {
                int flips = 0;
                var needles = mesh.TriangleIndices().Where(t => tri_altitude(mesh, t) < minAltitude).ToList();
                needles.Sort();
                foreach (int t in needles) {
                    if (mesh.IsTriangle(t) == false || tri_altitude(mesh, t) >= minAltitude) continue;
                    Index3i tv = mesh.GetTriangle(t);
                    Index3i te = mesh.GetTriEdges(t);
                    // the longest edge
                    int best = -1;
                    double bl = -1;
                    for (int j = 0; j < 3; ++j) {
                        double l = mesh.GetVertex(tv[j]).xy.Distance(mesh.GetVertex(tv[(j + 1) % 3]).xy);
                        if (l > bl) { bl = l; best = j; }
                    }
                    int e = te[best];
                    if (mesh.IsBoundaryEdge(e)) continue;
                    Index2i et = mesh.GetEdgeT(e);
                    int t2 = et.a == t ? et.b : et.a;
                    Index2i ev = mesh.GetEdgeV(e);
                    Index2i ov = mesh.GetEdgeOpposingV(e);
                    Vector2d a = mesh.GetVertex(ev.a).xy, b = mesh.GetVertex(ev.b).xy, c = mesh.GetVertex(ov.a).xy, d = mesh.GetVertex(ov.b).xy;
                    // the new triangles (c, d, b) and (d, c, a) in some orientation: both must be strictly on one side
                    int o1 = ExactPredicates.Orient2d(c, d, a), o2 = ExactPredicates.Orient2d(c, d, b);
                    if (o1 == 0 || o2 == 0 || o1 == o2) continue;
                    double before = Math.Min(tri_altitude(mesh, t), tri_altitude(mesh, t2));
                    double after = Math.Min(altitude(c, d, a), altitude(c, d, b));
                    if (!(after > before)) continue;
                    double change = diagonal_change(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), mesh.GetVertex(ov.a), mesh.GetVertex(ov.b));
                    double bound = ExactInterval.Up(Math.Max(devOf(t), devOf(t2)) + change);    // (rounded up: an upper bound)
                    if (!(bound <= maxDeviation)) continue;
                    int group = tri_altitude(mesh, t) >= tri_altitude(mesh, t2) ? mesh.GetTriangleGroup(t) : mesh.GetTriangleGroup(t2);
                    DMesh3.EdgeFlipInfo info;
                    if (mesh.FlipEdge(e, out info) != MeshResult.Ok) continue;
                    mesh.SetTriangleGroup(info.t0, group);
                    mesh.SetTriangleGroup(info.t1, group);
                    dev[info.t0] = bound; dev[info.t1] = bound;
                    flips++;
                }
                count += flips;
                if (flips == 0) break;
            }
            return count;
        }

        // |z on ab - z on cd| at the crossing of the diagonals ab and cd of a convex quad (an upper bound of the change
        // of the surface by the flip), exact and rounded up: in doubles the crossing of nearly parallel diagonals (the
        // case of two needles on one long edge) is off by eps / sin of their angle, far more than ulps of z.
        // z1 - z2 = N / den with den = u x w, N = (a.z - c.z) den + (r x w)(b.z - a.z) - (r x u)(d.z - c.z)
        internal static double diagonal_change(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            Func<double, ExactDyadic> E = ExactDyadic.Of;
            if (MathUtil.IsFinite(a.x + a.y + a.z + b.x + b.y + b.z + c.x + c.y + c.z + d.x + d.y + d.z) == false)
                return double.MaxValue;
            ExactDyadic ux = E(b.x) - E(a.x), uy = E(b.y) - E(a.y), wx = E(d.x) - E(c.x), wy = E(d.y) - E(c.y);
            ExactDyadic rx = E(c.x) - E(a.x), ry = E(c.y) - E(a.y);
            ExactDyadic den = ux * wy - uy * wx;
            if (den.Sign == 0) return double.MaxValue;
            ExactDyadic n = (E(a.z) - E(c.z)) * den + (rx * wy - ry * wx) * (E(b.z) - E(a.z)) - (rx * uy - ry * ux) * (E(d.z) - E(c.z));
            if (n.Sign == 0) return 0;
            double q = Math.Abs(ExactDyadic.Quotient(n, den));
            // (rounded to nearest: one step up is above the exact value)
            double ch = ExactInterval.Up(q);
            return MathUtil.IsFinite(ch) ? ch : double.MaxValue;
        }

        /// <summary> the XY altitude of a triangle (twice its area over its longest edge; 0 if degenerate) </summary>
        internal static double TriangleAltitude(DMesh3 mesh, int t) { return tri_altitude(mesh, t); }

        static double tri_altitude(DMesh3 mesh, int t)
        {
            Index3i tv = mesh.GetTriangle(t);
            return altitude(mesh.GetVertex(tv.a).xy, mesh.GetVertex(tv.b).xy, mesh.GetVertex(tv.c).xy);
        }

        /// <summary>
        /// Repair (TERRAIN.md 5.4): collapse the edges shorter than minLength, shortest first, onto the end whose planes
        /// contain the other end's planes, so the triangles stay in their cells; vertices on the toe or a lateral limit
        /// are only removed onto another such vertex. Returns the number of collapses.
        /// </summary>
        public static int CollapseShortEdges(DMesh3 mesh, List<List<int>> labels, List<bool> onBarrier, double minLength)
        {
            return CollapseShortEdges(mesh, labels, onBarrier, minLength, null);
        }

        /// <summary>
        /// CollapseShortEdges, appending the collapses made to 'made' (the kept and the removed end in XY, in order), for
        /// ReplayCollapses on a realization with the same vertices in XY (another lowering λ: the lengths are in 3D, so
        /// they move with z, and the decisions are made once per build, TERRAIN.md 5.4)
        /// </summary>
        internal static int CollapseShortEdges(DMesh3 mesh, List<List<int>> labels, List<bool> onBarrier, double minLength, List<Vector2d[]> made)
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
                        if (collapse_keeps_shapes(mesh, keep, remove, minLength) == false) continue;
                        DMesh3.EdgeCollapseInfo info;
                        Vector3d kp = mesh.GetVertex(keep);
                        Vector2d rp = mesh.GetVertex(remove).xy;
                        if (mesh.CollapseEdge(keep, remove, out info) == MeshResult.Ok) {
                            mesh.SetVertex(keep, kp);
                            if (made != null) made.Add(new[] { kp.xy, rp });
                            count++;
                            again = true;
                            break;
                        }
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// The collapses of CollapseShortEdges again, on a realization with the same vertices in XY: each the edge
        /// between the vertices at the recorded ends, in order, onto the kept one (its position kept). Returns the
        /// number made (fewer if an edge is not there; the exact check of every triangle decides).
        /// </summary>
        internal static int ReplayCollapses(DMesh3 mesh, List<Vector2d[]> made)
        {
            var at = new Dictionary<Vector2d, int>();
            foreach (int v in mesh.VertexIndices()) at[mesh.GetVertex(v).xy] = v;
            int count = 0;
            foreach (Vector2d[] c in made) {
                int keep, remove;
                if (at.TryGetValue(c[0], out keep) == false || at.TryGetValue(c[1], out remove) == false) continue;
                if (mesh.FindEdge(keep, remove) < 0) continue;
                Vector3d kp = mesh.GetVertex(keep);
                DMesh3.EdgeCollapseInfo info;
                if (mesh.CollapseEdge(keep, remove, out info) != MeshResult.Ok) continue;
                mesh.SetVertex(keep, kp);
                at.Remove(c[1]);
                count++;
            }
            return count;
        }

        static double edge_length(DMesh3 m, int e)
        {
            Index2i ev = m.GetEdgeV(e);
            return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b));
        }

        // (in XY: the band merge's decisions do not depend on the lowering λ, which moves z only)
        // an upper bound of |a - b| (each step rounded up: the difference, its square, the sum, the root; exactly 0 for
        // a = b). Scales bitwise with 2^k away from subnormals
        internal static double distance_up(Vector2d a, Vector2d b)
        {
            double dx = a.x - b.x, dy = a.y - b.y;
            dx = dx == 0 ? 0 : ExactInterval.Up(Math.Abs(dx));
            dy = dy == 0 ? 0 : ExactInterval.Up(Math.Abs(dy));
            double sx = dx == 0 ? 0 : ExactInterval.Up(dx * dx), sy = dy == 0 ? 0 : ExactInterval.Up(dy * dy);
            double sum = sx + sy;
            if (sum == 0) return 0;
            return ExactInterval.Up(Math.Sqrt(ExactInterval.Up(sum)));
        }

        static double edge_length_xy(DMesh3 m, int e)
        {
            Index2i ev = m.GetEdgeV(e);
            return m.GetVertex(ev.a).xy.Distance(m.GetVertex(ev.b).xy);
        }

        /// <summary>
        /// The targeted merge of the band (TERRAIN.md 5.4, "Repair of the band") on a realization: see the static
        /// overload; the heights are the planes' at 'lower' (as Realize).
        /// </summary>
        internal int BandMerge(DMesh3 mesh, double maxLength, double lower, bool fill, Func<int, bool> fails, out double merged)
        {
            return BandMerge(mesh, OutLabels, OnBarrier, (l, x, y) => K.Height(l, x, y, lower), maxLength, fill, fails, out merged);
        }

        /// <summary>
        /// The targeted merge of the band: an edge with XY length at most maxLength (at most MinFeatureSize, so both its
        /// triangles are needles: a triangle's altitude is at most its shortest edge), whose ends are on no barrier and
        /// have different plane sets (a near-quadruple point, which CollapseShortEdges may not collapse, or nested sets
        /// that it left: it refuses a collapse that shrinks a thin altitude, and it measures in 3D), is collapsed onto one end (more labels first, then x,
        /// then y). The kept vertex takes the union of the plane sets and the lowest of those planes at its XY
        /// ('height', mirrored for fill). Made only if every other triangle of the removed vertex stays counter-clockwise,
        /// the link condition holds, and no original position merged into the removed vertex (its own and those merged
        /// into it before) moves by more than maxLength in XY (an upper bound of the distance, rounded up): chains (clusters of triple points) are merged, the
        /// cumulative move of every position capped by maxLength. Passes until one merges nothing (each merge removes a
        /// vertex, so finitely many); in a pass shortest edges first, then by id. Only where a triangle of the edge fails
        /// ('fails', the exact checks on the current mesh; null: all qualify), so a needle that passes is left as it is,
        /// and not at the mesh's boundary (the cap's level line: a collapse there would move it). Decisions in XY and by
        /// those checks, so they depend on the realization through the checks only. Returns the number of merges;
        /// 'merged' is an upper bound of the largest move of an original position (0 if none).
        /// </summary>
        internal static int BandMerge(DMesh3 mesh, List<List<int>> labels, List<bool> onBarrier, Func<int, double, double, double> height,
                                      double maxLength, bool fill, Func<int, bool> fails, out double merged)
        {
            merged = 0;
            int count = 0;
            if (!(maxLength > 0)) return 0;
            // the original XY positions merged into a vertex before (absent: only its own); a vertex keeps its XY
            var origins = new Dictionary<int, List<Vector2d>>();
            for (bool more = true; more; ) {
                more = false;
                var candidates = mesh.EdgeIndices().Select(e => new KeyValuePair<double, int>(edge_length_xy(mesh, e), e))
                    .Where(kv => kv.Key <= maxLength).OrderBy(kv => kv.Key).ThenBy(kv => kv.Value).ToList();
                foreach (var kv in candidates) {
                    int e = kv.Value;
                    // (an edge of a removed vertex goes to the kept one in a collapse, and its length changes: the cap on
                    // the move below covers it, as the removed end moves by the edge's length)
                    if (mesh.IsEdge(e) == false) continue;
                    Index2i ev = mesh.GetEdgeV(e);
                    if (onBarrier[ev.a] || onBarrier[ev.b]) continue;
                    if (mesh.IsBoundaryVertex(ev.a) || mesh.IsBoundaryVertex(ev.b)) continue;
                    List<int> la = labels[ev.a], lb = labels[ev.b];
                    if (la.Count == lb.Count && la.All(l => lb.Contains(l))) continue;
                    if (fails != null) {
                        Index2i et = mesh.GetEdgeT(e);
                        if (fails(et.a) == false && (et.b < 0 || fails(et.b) == false)) continue;
                    }
                    Vector3d pa = mesh.GetVertex(ev.a), pb = mesh.GetVertex(ev.b);
                    bool keepA = la.Count != lb.Count ? la.Count > lb.Count : (pa.x != pb.x ? pa.x < pb.x : pa.y < pb.y);
                    int keep = keepA ? ev.a : ev.b, remove = keepA ? ev.b : ev.a;
                    Vector3d kp = mesh.GetVertex(keep);
                    // (the moves of the positions merged into 'remove', to the kept XY)
                    List<Vector2d> moved;
                    if (origins.TryGetValue(remove, out moved) == false) moved = new List<Vector2d> { mesh.GetVertex(remove).xy };
                    double move = moved.Max(o => distance_up(o, kp.xy));
                    if (move > maxLength) continue;
                    if (stays_counter_clockwise(mesh, keep, remove) == false) continue;
                    var union = new List<int>(labels[keep]);
                    foreach (int l in labels[remove]) if (union.Contains(l) == false) union.Add(l);
                    DMesh3.EdgeCollapseInfo info;
                    if (mesh.CollapseEdge(keep, remove, out info) != MeshResult.Ok) continue;
                    double z = double.MaxValue;
                    foreach (int l in union) z = Math.Min(z, height(l, kp.x, kp.y));
                    mesh.SetVertex(keep, new Vector3d(kp.x, kp.y, fill ? -z : z));
                    labels[keep] = union;
                    List<Vector2d> kept;
                    if (origins.TryGetValue(keep, out kept) == false) origins[keep] = kept = new List<Vector2d> { kp.xy };
                    kept.AddRange(moved);
                    origins.Remove(remove);
                    merged = Math.Max(merged, move);
                    count++;
                    more = true;
                }
            }
            return count;
        }

        // every triangle of 'remove' that does not contain 'keep' stays counter-clockwise with 'remove' at 'keep' (exact)
        static bool stays_counter_clockwise(DMesh3 mesh, int keep, int remove)
        {
            Vector2d k = mesh.GetVertex(keep).xy;
            foreach (int t in mesh.VtxTrianglesItr(remove)) {
                Index3i tv = mesh.GetTriangle(t);
                if (tv.a == keep || tv.b == keep || tv.c == keep) continue;
                Vector2d a = mesh.GetVertex(tv.a).xy, b = mesh.GetVertex(tv.b).xy, c = mesh.GetVertex(tv.c).xy;
                if (tv.a == remove) a = k; else if (tv.b == remove) b = k; else c = k;
                if (ExactPredicates.Orient2d(a, b, c) <= 0) return false;
            }
            return true;
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
