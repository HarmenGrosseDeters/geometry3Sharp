using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace Meshing2HardProblems
{
    /// <summary>
    /// A reference refiner for the small-angle study (HP 6): Ruppert's algorithm with concentric shells, written from the papers
    /// (Ruppert 1995; Miller, Pav, Walkington 2005; Pav, Walkington 2005). It is a prototype with the expensive part idealised:
    /// the constrained Delaunay triangulation is rebuilt from scratch in every round (the library's ConstrainedDelaunayMesh2), the
    /// arithmetic is in doubles, and nothing here is meant for the library. Diametral circles and circumcentres only: no
    /// off-centres, no lenses, no grid, no area bound, which is what the papers' proofs cover.
    /// </summary>
    public sealed class Hp6Refiner
    {
        public enum Mode
        {
            /// <summary> shells, no exemption: the cascade towards the apex of a small input angle </summary>
            Ruppert,
            /// <summary> Miller, Pav, Walkington 2005 (section 3): a skinny triangle is not split when the edge opposite its small
            /// angle joins two midpoints on adjoining input segments that meet below 60 degrees </summary>
            Mpw,
            /// <summary> corner lopping after Pav, Walkington 2005 (algorithm 1), with the protected arcs their section 6 sketches:
            /// every segment at a vertex with a small angle is cut on one circle, the pieces inside are never split, the chord of
            /// a small wedge is a constraint that is split onto the circle, the circle shrinks when a vertex enters it, and a
            /// circumcentre inside the circle is not inserted </summary>
            Collar,
        }

        sealed class Sub { public int A, B, Seg, Kind, Apex; public double Radius; }   // Kind 0 piece of an input segment, 1 radial piece of a collar, 2 chord of a collar arc
        const int Normal = 0, Radial = 1, Arc = 2;

        readonly Pslg g; readonly Mode mode; readonly double minAngle; readonly int vertexLimit;
        readonly List<(int a, int b)> segs;
        readonly int ringEdges;
        readonly List<Vector2d> pts = new List<Vector2d>();
        readonly List<bool> inputLike = new List<bool>();   // an input vertex, a collar point, or a point of an unbalanced shell split (MPW section 10)
        readonly List<int[]> onSegs = new List<int[]>();
        readonly List<int> collarApex = new List<int>();
        readonly List<double> collarRadius = new List<double>();
        readonly Dictionary<int, double> collarOf = new Dictionary<int, double>();   // the current radius of a collar, by apex
        readonly List<Wedge> wedges;
        List<Sub> subs = new List<Sub>();
        readonly List<int> ring;

        public string Status = "ok";
        public int Rounds, Shrinks, LeftAtCollar;
        public IReadOnlyList<Vector2d> Vertices => pts;
        public List<Index3i> Triangles = new List<Index3i>();

        public Hp6Refiner(Pslg g, Mode mode, double minAngleDeg, int vertexLimit = 60000)
        {
            this.g = g; this.mode = mode; minAngle = minAngleDeg * Math.PI / 180; this.vertexLimit = vertexLimit;
            segs = g.Segments(); ringEdges = g.Ring.Count; ring = new List<int>(g.Ring);
            wedges = Wedges.Of(g);
            for (int i = 0; i < g.P.Count; ++i) {
                pts.Add(g.P[i]); inputLike.Add(true); collarApex.Add(-1); collarRadius.Add(0);
                onSegs.Add(Enumerable.Range(0, segs.Count).Where(s => segs[s].a == i || segs[s].b == i).ToArray());
            }
            for (int s = 0; s < segs.Count; ++s) subs.Add(new Sub { A = segs[s].a, B = segs[s].b, Seg = s, Kind = Normal, Apex = -1 });
            if (mode == Mode.Collar) BuildCollars();
        }

        int AddVertex(Vector2d p, bool likeInput, int[] on, int apex = -1, double radius = 0)
        {
            pts.Add(p); inputLike.Add(likeInput); onSegs.Add(on); collarApex.Add(apex); collarRadius.Add(radius);
            return pts.Count - 1;
        }

        void InsertOnRing(int a, int b, int m)
        {
            for (int i = 0; i < ring.Count; ++i) {
                int u = ring[i], v = ring[(i + 1) % ring.Count];
                if ((u == a && v == b) || (u == b && v == a)) { ring.Insert(i + 1, m); return; }
            }
            throw new InvalidOperationException("a boundary piece is not on the ring");
        }

        // ---- collars ----

        void BuildCollars()
        {
            foreach (int x in wedges.Select(w => w.Apex).Distinct().ToList()) {
                // the distance to the nearest feature that does not contain x (lfs0 of Pav and Walkington): other vertices, and segments not at x
                double d = double.MaxValue;
                for (int v = 0; v < g.P.Count; ++v) if (v != x) d = Math.Min(d, (g.P[v] - g.P[x]).Length);
                for (int s = 0; s < segs.Count; ++s) if (segs[s].a != x && segs[s].b != x) d = Math.Min(d, DistToSegment(g.P[x], g.P[segs[s].a], g.P[segs[s].b]));
                double r = Math.Pow(2, Math.Floor(Math.Log(d / 3, 2)));
                collarOf[x] = r;
                foreach (var sub in subs.Where(z => z.Kind == Normal && (z.A == x || z.B == x)).ToList()) CutRadial(sub, x, r);
                AddArcs(x, r);
            }
        }

        /// <summary> the piece of sub from x to radius r becomes the collar's radial piece </summary>
        void CutRadial(Sub sub, int x, double r)
        {
            int far = sub.A == x ? sub.B : sub.A;
            var dir = pts[far] - pts[x]; dir /= dir.Length;
            int m = AddVertex(pts[x] + dir * r, true, new[] { sub.Seg }, x, r);
            if (sub.Seg < ringEdges) InsertOnRing(x, far, m);
            subs.Remove(sub);
            subs.Add(new Sub { A = x, B = m, Seg = sub.Seg, Kind = Radial, Apex = x, Radius = r });
            subs.Add(new Sub { A = m, B = far, Seg = sub.Seg, Kind = Normal, Apex = -1 });
        }

        void AddArcs(int x, double r)
        {
            foreach (var w in wedges.Where(w => w.Apex == x)) {
                int a = subs.First(z => z.Kind == Radial && z.Apex == x && z.Seg == w.Seg1).B, b = subs.First(z => z.Kind == Radial && z.Apex == x && z.Seg == w.Seg2).B;
                subs.Add(new Sub { A = a, B = b, Seg = -1, Kind = Arc, Apex = x, Radius = r });
            }
        }

        void Shrink(int x)
        {
            double r = collarOf[x] / 2;
            collarOf[x] = r; ++Shrinks;
            foreach (var sub in subs.Where(z => z.Kind == Radial && z.Apex == x).ToList()) {
                int far = sub.B;
                var dir = pts[far] - pts[x]; dir /= dir.Length;
                int m = AddVertex(pts[x] + dir * r, true, new[] { sub.Seg }, x, r);
                if (sub.Seg < ringEdges) InsertOnRing(x, far, m);
                subs.Remove(sub);
                subs.Add(new Sub { A = x, B = m, Seg = sub.Seg, Kind = Radial, Apex = x, Radius = r });
                subs.Add(new Sub { A = m, B = far, Seg = sub.Seg, Kind = Normal, Apex = -1 });
            }
            AddArcs(x, r);
        }

        // ---- geometry ----

        static double DistToSegment(Vector2d p, Vector2d a, Vector2d b)
        {
            var ab = b - a; double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / ab.LengthSquared));
            return (p - (a + ab * t)).Length;
        }

        static bool InDiametral(Vector2d p, Vector2d a, Vector2d b) => (a - p).Dot(b - p) < -1e-13 * (a - b).LengthSquared;

        public static double MinAngle(Vector2d a, Vector2d b, Vector2d c, out int corner)
        {
            double A = Angle(a, b, c), B = Angle(b, c, a), C = Angle(c, a, b);
            corner = A <= B && A <= C ? 0 : B <= C ? 1 : 2;
            return Math.Min(A, Math.Min(B, C));
        }
        static double Angle(Vector2d at, Vector2d p, Vector2d q)
        {
            var u = p - at; var v = q - at;
            return Math.Atan2(Math.Abs(u.x * v.y - u.y * v.x), u.Dot(v));
        }

        static Vector2d Circumcentre(Vector2d a, Vector2d b, Vector2d c)
        {
            var ab = b - a; var ac = c - a;
            double d = 2 * (ab.x * ac.y - ab.y * ac.x);
            double ux = (ac.y * ab.LengthSquared - ab.y * ac.LengthSquared) / d, uy = (ab.x * ac.LengthSquared - ac.x * ab.LengthSquared) / d;
            return new Vector2d(a.x + ux, a.y + uy);
        }

        // ---- the exemptions ----

        bool Exempt(Index3i t, int corner)
        {
            int m = t[corner], p = t[(corner + 1) % 3], q = t[(corner + 2) % 3];   // the small angle is at m, opposite the edge p-q
            if (mode == Mode.Mpw) {
                if (inputLike[p] || inputLike[q]) return false;   // both ends are midpoints
                foreach (int s1 in onSegs[p]) foreach (int s2 in onSegs[q])
                    if (s1 != s2 && wedges.Any(w => (w.Seg1 == s1 && w.Seg2 == s2) || (w.Seg1 == s2 && w.Seg2 == s1))) return true;
                return false;
            }
            if (mode == Mode.Collar) {
                // the fan of a collar: the apex and two points on its current circle
                for (int k = 0; k < 3; ++k) {
                    int x = t[k], u = t[(k + 1) % 3], v = t[(k + 2) % 3];
                    if (collarOf.TryGetValue(x, out double r) && collarApex[u] == x && collarApex[v] == x && collarRadius[u] == r && collarRadius[v] == r) return true;
                }
            }
            return false;
        }

        // ---- the loop ----

        ConstrainedMesh2 Triangulate()
        {
            var input = new ConstrainedMesh2Input();
            input.Points.AddRange(pts);
            input.Rings.Add(new Mesh2Ring(ring.ToArray(), false));
            foreach (var s in subs) if (s.Seg < 0 || s.Seg >= ringEdges) input.Segments.Add(new Mesh2Segment(s.A, s.B));
            var result = new ConstrainedDelaunayMesh2().Compute(input);
            if (result.Mesh == null || result.Mesh.Vertices.Count != pts.Count) { Status = "triangulation: " + result.Status + " " + result.FailureKind + (result.Mesh != null ? " (vertices merged or added)" : ""); return null; }
            return result.Mesh;
        }

        void Split(Sub sub, List<Sub> add)
        {
            Vector2d a = pts[sub.A], b = pts[sub.B];
            if (sub.Kind == Arc) {
                var x = pts[sub.Apex]; var dir = (a - x) + (b - x); dir /= dir.Length;
                int m = AddVertex(x + dir * sub.Radius, false, new int[0], sub.Apex, sub.Radius);
                add.Add(new Sub { A = sub.A, B = m, Seg = -1, Kind = Arc, Apex = sub.Apex, Radius = sub.Radius });
                add.Add(new Sub { A = m, B = sub.B, Seg = -1, Kind = Arc, Apex = sub.Apex, Radius = sub.Radius });
                return;
            }
            double len = (b - a).Length;
            Vector2d p; bool balanced = true;
            if (inputLike[sub.A] != inputLike[sub.B]) {
                // a concentric shell around the input end: a power of two between a third and two thirds of the piece
                double r = Math.Pow(2, Math.Floor(Math.Log(2 * len / 3, 2)));
                p = inputLike[sub.A] ? a + (b - a) * (r / len) : b + (a - b) * (r / len);
                balanced = Math.Abs(r - len / 2) <= 1e-12 * len;
            } else p = (a + b) * 0.5;
            // (MPW section 10: the point of an unbalanced split counts as an input point, not as a midpoint)
            int v = AddVertex(p, !balanced, new[] { sub.Seg });
            if (sub.Seg < ringEdges) InsertOnRing(sub.A, sub.B, v);
            add.Add(new Sub { A = sub.A, B = v, Seg = sub.Seg, Kind = Normal, Apex = -1 });
            add.Add(new Sub { A = v, B = sub.B, Seg = sub.Seg, Kind = Normal, Apex = -1 });
        }

        void SplitAll(HashSet<Sub> which)
        {
            var next = new List<Sub>();
            foreach (var s in subs) if (which.Contains(s)) Split(s, next); else next.Add(s);
            subs = next;
        }

        public void Run()
        {
            var left = new HashSet<(int, int, int)>();
            while (true) {
                ++Rounds;
                var mesh = Triangulate();
                if (mesh == null) return;
                Triangles = mesh.Triangles.ToList();
                // the triangulator merges what is closer than its tolerance (2^-41 of the extent): a cascade that gets there loses the apex
                var used = new bool[pts.Count];
                foreach (var t in Triangles) { used[t.a] = true; used[t.b] = true; used[t.c] = true; }
                if (used.Contains(false)) { Status = "cascade down to the triangulator's tolerance (a vertex left unused)"; return; }
                if (pts.Count > vertexLimit) { Status = "vertex limit"; return; }
                var opposite = new Dictionary<long, List<int>>();
                foreach (var t in Triangles)
                    for (int k = 0; k < 3; ++k) {
                        long key = Key(t[(k + 1) % 3], t[(k + 2) % 3]);
                        if (!opposite.TryGetValue(key, out var l)) opposite[key] = l = new List<int>(2);
                        l.Add(t[k]);
                    }
                // 1. subsegments encroached by a vertex
                var toSplit = new HashSet<Sub>(); var toShrink = new SortedSet<int>();
                foreach (var s in subs) {
                    if (!opposite.TryGetValue(Key(s.A, s.B), out var opp)) continue;
                    if (!opp.Any(v => InDiametral(pts[v], pts[s.A], pts[s.B]))) continue;
                    if (s.Kind == Radial) toShrink.Add(s.Apex); else toSplit.Add(s);
                }
                if (toShrink.Count > 0) { foreach (int x in toShrink) Shrink(x); continue; }
                if (toSplit.Count > 0) { SplitAll(toSplit); continue; }
                // 2. skinny triangles, worst first
                var skinny = new List<(double angle, int index, int corner)>();
                for (int i = 0; i < Triangles.Count; ++i) {
                    var t = Triangles[i];
                    double angle = MinAngle(pts[t.a], pts[t.b], pts[t.c], out int corner);
                    if (angle < minAngle && !Exempt(t, corner) && !left.Contains(Sorted(t))) skinny.Add((angle, i, corner));
                }
                skinny.Sort();
                var accepted = new List<(Vector2d c, double r)>();
                foreach (var (angle, index, corner) in skinny) {
                    var t = Triangles[index];
                    var c = Circumcentre(pts[t.a], pts[t.b], pts[t.c]);
                    double r = (c - pts[t.a]).Length;
                    bool atCollar = false;
                    var hit = new List<Sub>();
                    foreach (var s in subs)
                        if (InDiametral(c, pts[s.A], pts[s.B])) { if (s.Kind == Radial) atCollar = true; else hit.Add(s); }
                    foreach (var kv in collarOf) if ((c - pts[kv.Key]).Length < kv.Value) atCollar = true;
                    // Pav and Walkington: a circumcentre inside the ball of a lopped corner is never inserted; the triangle stays
                    if (atCollar) { left.Add(Sorted(t)); ++LeftAtCollar; continue; }
                    bool rejected = hit.Count > 0;
                    foreach (var s in hit) toSplit.Add(s);
                    if (rejected) continue;
                    if (!mesh.TryLocate(c, out _)) { Status = "a circumcentre outside the domain with no encroached subsegment"; return; }
                    // the batch keeps every insertion radius: two accepted points are at least the larger circumradius apart
                    if (accepted.All(o => (o.c - c).Length >= Math.Max(o.r, r))) accepted.Add((c, r));
                }
                if (toSplit.Count > 0) { SplitAll(toSplit); continue; }
                if (accepted.Count == 0) return;
                foreach (var (c, r) in accepted) AddVertex(c, false, new int[0]);
            }
        }

        static (int, int, int) Sorted(Index3i t) { var a = new[] { t.a, t.b, t.c }; Array.Sort(a); return (a[0], a[1], a[2]); }
        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }
}
