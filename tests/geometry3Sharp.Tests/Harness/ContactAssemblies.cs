using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Procedural contact assemblies: closed parts whose faces touch exactly, with an analytical ground truth (docs/design/EXACT_IMPRINT.md,
    /// tests in ExactImprintAssemblyTests). All coordinates are integers of a lattice of <see cref="LatticeSolid.Unit"/> = 1/8, so every
    /// vertex is an exact double and every transform by a power of two is exact. Parts are built from horizontal faces (triangulated by the
    /// library's exact constrained Delaunay triangulation, with optional jittered Steiner points) and vertical walls (quad strips with a chosen
    /// diagonal and height levels), so the two parts of a contact can have different triangulations of the same planar contact face.
    /// </summary>
    public struct Rat : IComparable<Rat>, IEquatable<Rat>
    {
        public readonly BigInteger N, D;     // D > 0, reduced
        public Rat(BigInteger n, BigInteger d)
        {
            if (d.IsZero) throw new DivideByZeroException();
            if (d.Sign < 0) { n = -n; d = -d; }
            BigInteger g = BigInteger.GreatestCommonDivisor(n, d);
            if (g.IsOne == false && g.IsZero == false) { n /= g; d /= g; }
            if (n.IsZero) d = BigInteger.One;
            N = n; D = d;
        }
        public static Rat Int(long v) => new Rat(v, 1);
        public static readonly Rat Zero = new Rat(0, 1);
        public static Rat operator +(Rat a, Rat b) => new Rat(a.N * b.D + b.N * a.D, a.D * b.D);
        public static Rat operator -(Rat a, Rat b) => new Rat(a.N * b.D - b.N * a.D, a.D * b.D);
        public static Rat operator *(Rat a, Rat b) => new Rat(a.N * b.N, a.D * b.D);
        public static Rat operator /(Rat a, Rat b) => new Rat(a.N * b.D, a.D * b.N);
        public static Rat operator -(Rat a) => new Rat(-a.N, a.D);
        public int Sign => N.Sign;
        public int CompareTo(Rat o) => (N * o.D).CompareTo(o.N * D);
        public bool Equals(Rat o) => N == o.N && D == o.D;
        public override bool Equals(object obj) => obj is Rat r && Equals(r);
        public override int GetHashCode() => N.GetHashCode() * 31 + D.GetHashCode();
        public static bool operator ==(Rat a, Rat b) => a.Equals(b);
        public static bool operator !=(Rat a, Rat b) => a.Equals(b) == false;
        public static bool operator <(Rat a, Rat b) => a.CompareTo(b) < 0;
        public static bool operator >(Rat a, Rat b) => a.CompareTo(b) > 0;
        public static bool operator <=(Rat a, Rat b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Rat a, Rat b) => a.CompareTo(b) >= 0;
        public override string ToString() => D.IsOne ? N.ToString() : N + "/" + D;
    }

    /// <summary> a point of the lattice (integers of the unit 1/8) or a rational point in the plane of a contact (u, v) </summary>
    public struct P2 : IEquatable<P2>
    {
        public readonly Rat U, V;
        public P2(Rat u, Rat v) { U = u; V = v; }
        public P2(long u, long v) { U = Rat.Int(u); V = Rat.Int(v); }
        public bool Equals(P2 o) => U == o.U && V == o.V;
        public override bool Equals(object obj) => obj is P2 p && Equals(p);
        public override int GetHashCode() => U.GetHashCode() * 17 + V.GetHashCode();
        public override string ToString() => "(" + U + ", " + V + ")";
    }

    /// <summary> a polygon with holes in the plane of a contact, exact: the outer loop and the hole loops (any orientation) </summary>
    public sealed class Region2
    {
        public readonly List<P2> Outer;
        public readonly List<List<P2>> Holes;
        public Region2(IEnumerable<P2> outer, IEnumerable<IEnumerable<P2>> holes = null)
        {
            Outer = outer.ToList();
            Holes = holes == null ? new List<List<P2>>() : holes.Select(h => h.ToList()).ToList();
        }
        public IEnumerable<List<P2>> Loops() { yield return Outer; foreach (var h in Holes) yield return h; }

        public enum Where { Outside = 0, Boundary = 1, Inside = 2 }

        static Rat Cross(P2 a, P2 b, P2 c) => (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);

        static bool OnSegment(P2 a, P2 b, P2 p)
        {
            if (Cross(a, b, p).Sign != 0) return false;
            Rat minU = a.U < b.U ? a.U : b.U, maxU = a.U < b.U ? b.U : a.U, minV = a.V < b.V ? a.V : b.V, maxV = a.V < b.V ? b.V : a.V;
            return p.U >= minU && p.U <= maxU && p.V >= minV && p.V <= maxV;
        }

        // 0 outside the loop, 1 on it, 2 inside (even-odd ray casting along +u, exact)
        static Where Locate(List<P2> loop, P2 p)
        {
            bool inside = false;
            for (int i = 0; i < loop.Count; ++i) {
                P2 a = loop[i], b = loop[(i + 1) % loop.Count];
                if (OnSegment(a, b, p)) return Where.Boundary;
                bool aAbove = a.V > p.V, bAbove = b.V > p.V;
                if (aAbove == bAbove) continue;
                // the crossing of the segment with the line v = p.V lies right of p
                Rat uCross = a.U + (p.V - a.V) * (b.U - a.U) / (b.V - a.V);
                if (uCross > p.U) inside = !inside;
            }
            return inside ? Where.Inside : Where.Outside;
        }

        public Where Locate(P2 p)
        {
            Where w = Locate(Outer, p);
            if (w != Where.Inside) return w;
            foreach (var h in Holes) {
                Where wh = Locate(h, p);
                if (wh == Where.Boundary) return Where.Boundary;
                if (wh == Where.Inside) return Where.Outside;
            }
            return Where.Inside;
        }

        /// <summary> twice the signed area of a loop (shoelace), counter-clockwise positive </summary>
        public static Rat Area2(List<P2> loop)
        {
            Rat s = Rat.Zero;
            for (int i = 0; i < loop.Count; ++i) {
                P2 a = loop[i], b = loop[(i + 1) % loop.Count];
                s = s + (a.U * b.V - b.U * a.V);
            }
            return s;
        }

        /// <summary> the area of the region: outer minus the holes (exact) </summary>
        public Rat Area()
        {
            Rat a = Rat.Zero;
            Rat o = Area2(Outer); a = a + (o.Sign < 0 ? -o : o);
            foreach (var h in Holes) { Rat x = Area2(h); a = a - (x.Sign < 0 ? -x : x); }
            return a / Rat.Int(2);
        }
    }

    /// <summary> what a contact between two parts is, with the analytical ground truth (planar contacts on the lattice; lattice units of 1/8) </summary>
    public sealed class Contact
    {
        public string Name;
        public int PartA, PartB;
        /// <summary>
        /// the plane of the contact as a frame on the lattice: a point on it and two perpendicular in-plane directions (lattice vectors);
        /// the plane coordinates (u, v) of a point are (P - Origin) . DirU / |DirU|^2 and the same with DirV, so areas in (u, v) are affine images of the
        /// true areas and rational
        /// </summary>
        public long[] Origin, DirU, DirV;
        /// <summary> the faces of the two parts that lie in the plane (all of them), in plane coordinates </summary>
        public Region2 FaceA, FaceB;
        /// <summary> the area of the contact region (face A intersected with face B) in plane coordinates, derived by hand in the fixture; 0 for a contact along lines only </summary>
        public Rat Area;
        /// <summary> the outline of the contact region (rational points in plane coordinates): these segments must be covered by curve edges on both sides; for a line contact the shared lines </summary>
        public List<(P2 a, P2 b)> Outline = new List<(P2, P2)>();

        long[] Normal => new[] { DirU[1] * DirV[2] - DirU[2] * DirV[1], DirU[2] * DirV[0] - DirU[0] * DirV[2], DirU[0] * DirV[1] - DirU[1] * DirV[0] };
        static long Dot(long[] a, long[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        /// <summary> the plane coordinates of an exact point of a mesh built by LatticeSolid (coordinates in units of 1/8); false if it is not on the plane </summary>
        public bool ToUV(ExactPoint3 p, out P2 uv)
        {
            uv = default(P2);
            // the lattice coordinates X * 8 / W
            Rat[] d = new Rat[3];
            for (int k = 0; k < 3; ++k) d[k] = new Rat(p[k] * 8, p.W) - Rat.Int(Origin[k]);
            long[] n = Normal;
            Rat off = d[0] * Rat.Int(n[0]) + d[1] * Rat.Int(n[1]) + d[2] * Rat.Int(n[2]);
            if (off.Sign != 0) return false;
            Rat u = (d[0] * Rat.Int(DirU[0]) + d[1] * Rat.Int(DirU[1]) + d[2] * Rat.Int(DirU[2])) / Rat.Int(Dot(DirU, DirU));
            Rat v = (d[0] * Rat.Int(DirV[0]) + d[1] * Rat.Int(DirV[1]) + d[2] * Rat.Int(DirV[2])) / Rat.Int(Dot(DirV, DirV));
            uv = new P2(u, v);
            return true;
        }

        public Region2.Where Locate(P2 p)
        {
            Region2.Where a = FaceA.Locate(p), b = FaceB.Locate(p);
            if (a == Region2.Where.Outside || b == Region2.Where.Outside) return Region2.Where.Outside;
            if (a == Region2.Where.Inside && b == Region2.Where.Inside) return Region2.Where.Inside;
            return Region2.Where.Boundary;
        }
        public override string ToString() => Name;
    }

    public sealed class Part
    {
        public string Name;
        public DMesh3 Mesh;
        /// <summary> the number of the part in the assembly </summary>
        public int Index;
        public override string ToString() => Name;
    }

    public sealed class ContactAssembly
    {
        public string Name;
        public List<Part> Parts = new List<Part>();
        public List<Contact> Contacts = new List<Contact>();
        /// <summary> the notes of the fixture: which contacts are exact and why (the cylinder walls are planar octagon faces here) </summary>
        public string Note;
        public override string ToString() => Name;

        /// <summary> the assembly mapped by a similarity (exact for the powers of two) </summary>
        public List<DMesh3> TransformedParts(Similarity s) => Parts.Select(p => Invariance.Transformed(p.Mesh, s)).ToList();
    }

    /// <summary> a closed solid built on the lattice from horizontal faces and vertical walls </summary>
    public sealed class LatticeSolid
    {
        public const double Unit = 0.125;
        readonly Dictionary<(int, int, int), int> ids = new Dictionary<(int, int, int), int>();
        readonly List<(int, int, int)> positions = new List<(int, int, int)>();
        readonly List<Index3i> tris = new List<Index3i>();

        public int V(int x, int y, int z)
        {
            if (ids.TryGetValue((x, y, z), out int id) == false) {
                id = positions.Count;
                ids[(x, y, z)] = id;
                positions.Add((x, y, z));
            }
            return id;
        }

        public void Tri(int a, int b, int c) { tris.Add(new Index3i(a, b, c)); }

        /// <summary> the loop with extra points: the midpoints of every edge (a part with a finer discretization of its boundary) </summary>
        public static List<(int x, int y)> WithMidpoints(List<(int x, int y)> loop)
        {
            var r = new List<(int, int)>();
            for (int i = 0; i < loop.Count; ++i) {
                var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                r.Add(a);
                if ((a.x + b.x) % 2 == 0 && (a.y + b.y) % 2 == 0) r.Add(((a.x + b.x) / 2, (a.y + b.y) / 2));
            }
            return r;
        }

        /// <summary>
        /// a horizontal face at z, facing up or down: the region inside outer (counter-clockwise) and outside the holes (clockwise), triangulated by the
        /// exact constrained Delaunay triangulation of the loop points and the Steiner points (so a jittered point set gives a different triangulation)
        /// </summary>
        public void HFace(int z, bool up, List<(int x, int y)> outer, List<List<(int x, int y)>> holes, List<(int x, int y)> steiner = null)
        {
            holes = holes ?? new List<List<(int x, int y)>>();
            var pts = new List<(long x, long y)>();
            var loops = new List<List<int>>();
            foreach (var loop in new[] { outer }.Concat(holes)) {
                var idx = new List<int>();
                foreach (var p in loop) { idx.Add(pts.Count); pts.Add((p.x, p.y)); }
                loops.Add(idx);
            }
            var st = new List<int>();
            if (steiner != null)
                foreach (var p in steiner) { st.Add(pts.Count); pts.Add((p.x, p.y)); }
            long M = 1 << 14;
            int c0 = pts.Count; pts.Add((-M, -M));
            int c1 = pts.Count; pts.Add((4 * M, -M));
            int c2 = pts.Count; pts.Add((-M, 4 * M));
            Func<int, int, int, int> orient = (a, b, c) => Math.Sign((pts[b].x - pts[a].x) * (pts[c].y - pts[a].y) - (pts[b].y - pts[a].y) * (pts[c].x - pts[a].x));
            Func<int, int, int, int, int> incircle = (a, b, c, d) => {
                BigInteger[] r = new[] { a, b, c }.Select(i => {
                    BigInteger x = pts[i].x - pts[d].x, y = pts[i].y - pts[d].y;
                    return new[] { x, y, x * x + y * y };
                }).SelectMany(v => v).ToArray();
                BigInteger det = r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6]);
                return det.Sign;
            };
            var tr = new ExactTriangulation2(c0, c1, c2, orient, incircle);
            foreach (var loop in loops) foreach (int i in loop) tr.InsertPoint(i);
            foreach (int i in st) tr.InsertPoint(i);
            foreach (var loop in loops)
                for (int k = 0; k < loop.Count; ++k)
                    tr.InsertConstraint(loop[k], loop[(k + 1) % loop.Count]);
            tr.MakeDelaunay();
            var outerPoly = outer.Select(p => new P2(p.x, p.y)).ToList();
            var holePolys = holes.Select(h => h.Select(p => new P2(p.x, p.y)).ToList()).ToList();
            var region = new Region2(outerPoly, holePolys);
            foreach (Index3i t in tr.Triangles()) {
                if (t.a >= c0 || t.b >= c0 || t.c >= c0) continue;
                P2 centroid = new P2(new Rat(pts[t.a].x + pts[t.b].x + pts[t.c].x, 3), new Rat(pts[t.a].y + pts[t.b].y + pts[t.c].y, 3));
                if (region.Locate(centroid) != Region2.Where.Inside) continue;
                int va = V((int)pts[t.a].x, (int)pts[t.a].y, z), vb = V((int)pts[t.b].x, (int)pts[t.b].y, z), vc = V((int)pts[t.c].x, (int)pts[t.c].y, z);
                if (up) Tri(va, vb, vc); else Tri(va, vc, vb);
            }
        }

        /// <summary>
        /// the wall over a loop (material on the left of its direction: the outer loop counter-clockwise, a hole clockwise) between z0 and z1, with extra
        /// height levels, the outward normal on the right of the direction; altDiagonal flips the diagonal of every quad
        /// </summary>
        public void Wall(List<(int x, int y)> loop, int z0, int z1, IEnumerable<int> midLevels = null, bool altDiagonal = false)
        {
            var levels = new List<int> { z0 };
            if (midLevels != null) levels.AddRange(midLevels);
            levels.Add(z1);
            for (int i = 0; i < loop.Count; ++i) {
                var p = loop[i]; var q = loop[(i + 1) % loop.Count];
                for (int k = 0; k + 1 < levels.Count; ++k) {
                    int p0 = V(p.x, p.y, levels[k]), q0 = V(q.x, q.y, levels[k]), q1 = V(q.x, q.y, levels[k + 1]), p1 = V(p.x, p.y, levels[k + 1]);
                    if (altDiagonal) { Tri(p0, q0, p1); Tri(q0, q1, p1); }
                    else { Tri(p0, q0, q1); Tri(p0, q1, p1); }
                }
            }
        }

        public DMesh3 ToMesh()
        {
            var mesh = new DMesh3();
            foreach (var p in positions) mesh.AppendVertex(new Vector3d(p.Item1 * Unit, p.Item2 * Unit, p.Item3 * Unit));
            foreach (var t in tris)
                if (mesh.AppendTriangle(t.a, t.b, t.c) < 0) throw new InvalidOperationException("the solid is not a manifold: " + t);
            return mesh;
        }
    }
}

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The ground truth of a planar contact computed from the input meshes alone (exact rational geometry, no imprint code): the overlay of the two
    /// parts' in-plane edges inside the closed contact region. Imprinting A with B cuts the triangles of A along the edges of B (and along the
    /// outline), and B along those of A, so after both imprints each side contains every piece of the overlay as a mesh edge and no other vertex
    /// in the contact region: this is "the contact boundaries match one to one".
    /// </summary>
    public static class ContactTruth
    {
        public static Rat Cross(P2 a, P2 b, P2 c) => (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);

        /// <summary> the parameter t of the point where the segments (p, q) and (r, s) meet on (p, q), if they cross or touch (t strictly inside (p, q); not parallel) </summary>
        public static bool Crossing(P2 p, P2 q, P2 r, P2 s, out Rat t)
        {
            t = Rat.Zero;
            Rat d = (q.U - p.U) * (s.V - r.V) - (q.V - p.V) * (s.U - r.U);
            if (d.Sign == 0) return false;
            t = ((r.U - p.U) * (s.V - r.V) - (r.V - p.V) * (s.U - r.U)) / d;
            Rat w = ((r.U - p.U) * (q.V - p.V) - (r.V - p.V) * (q.U - p.U)) / d;
            return t.Sign > 0 && t < Rat.Int(1) && w.Sign >= 0 && w <= Rat.Int(1);
        }

        public static string Key(P2 p) => p.U + "," + p.V;
        public static string Key(P2 a, P2 b) { string x = Key(a), y = Key(b); return string.CompareOrdinal(x, y) < 0 ? x + "|" + y : y + "|" + x; }

        public sealed class Overlay
        {
            /// <summary> the pieces of the overlay: key (normalized exact point pair) to the piece and its provenance: 1 lies on an edge of part A, 2 on an edge of part B (both if the edges coincide) </summary>
            public Dictionary<string, (P2 a, P2 b, int from)> Pieces = new Dictionary<string, (P2, P2, int)>();
            public Dictionary<string, P2> Vertices = new Dictionary<string, P2>();
        }

        /// <summary> the unique edges of in-plane triangles </summary>
        public static List<(P2 a, P2 b)> Edges(IEnumerable<List<P2>> tris)
        {
            var seen = new HashSet<string>();
            var r = new List<(P2, P2)>();
            foreach (var t in tris)
                for (int k = 0; k < 3; ++k)
                    if (seen.Add(Key(t[k], t[(k + 1) % 3]))) r.Add((t[k], t[(k + 1) % 3]));
            return r;
        }

        public static Overlay Compute(Contact c, List<(P2 a, P2 b)> edgesA, List<(P2 a, P2 b)> edgesB)
        {
            var o = new Overlay();
            var sets = new[] { edgesA, edgesB };
            for (int side = 0; side < 2; ++side) {
                var mine = sets[side]; var other = sets[1 - side];
                foreach (var e in mine) {
                    var ts = new List<Rat> { Rat.Zero, Rat.Int(1) };
                    Rat dd = (e.b.U - e.a.U) * (e.b.U - e.a.U) + (e.b.V - e.a.V) * (e.b.V - e.a.V);
                    foreach (var f in other) {
                        Rat t;
                        if (Crossing(e.a, e.b, f.a, f.b, out t)) ts.Add(t);
                        foreach (P2 v in new[] { f.a, f.b }) {
                            if (Cross(e.a, e.b, v).Sign != 0) continue;
                            Rat tv = ((v.U - e.a.U) * (e.b.U - e.a.U) + (v.V - e.a.V) * (e.b.V - e.a.V)) / dd;
                            if (tv.Sign > 0 && tv < Rat.Int(1)) ts.Add(tv);
                        }
                    }
                    ts = ts.Distinct().OrderBy(x => x).ToList();
                    for (int k = 0; k + 1 < ts.Count; ++k) {
                        P2 s0 = new P2(e.a.U + ts[k] * (e.b.U - e.a.U), e.a.V + ts[k] * (e.b.V - e.a.V));
                        P2 s1 = new P2(e.a.U + ts[k + 1] * (e.b.U - e.a.U), e.a.V + ts[k + 1] * (e.b.V - e.a.V));
                        Rat half = new Rat(1, 2);
                        P2 m = new P2((s0.U + s1.U) * half, (s0.V + s1.V) * half);
                        if (c.Locate(m) == Region2.Where.Outside) continue;
                        string key = Key(s0, s1);
                        o.Pieces.TryGetValue(key, out var old);
                        o.Pieces[key] = (s0, s1, old.from | (1 << side));
                        o.Vertices[Key(s0)] = s0; o.Vertices[Key(s1)] = s1;
                    }
                }
            }
            return o;
        }
    }
}
