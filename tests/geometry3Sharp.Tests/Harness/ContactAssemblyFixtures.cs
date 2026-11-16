using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The fixtures of ContactAssemblies: stacked plates with matching and with offset bolt holes, a stepped shaft in a bore, a through mortise with its
    /// tenon (flush and protruding), and a plate standing on a plate (inside and overhanging the edge). Each comes with the tessellation of its parts as
    /// plain (the same structured triangulation on both sides) or perturbed (a part with jittered interior points, midpoints on its boundary loops, the
    /// other diagonal and an extra height level on its walls), so the two sides of a contact have different triangulations of the same planar face.
    ///
    /// Which contacts are exact: every contact listed is between planar faces that lie exactly in one plane (horizontal faces and the walls of prisms over
    /// lattice loops, the octagon walls of the shaft and the bore included: an octagon is a prism over eight planar faces). A true cylinder (a polygon
    /// inscribed in a circle) has no exactly representable vertices and would touch its bore only where the two tessellations share the vertices; the
    /// octagon is the cylinder of this suite and its walls touch exactly for any tessellation. The loose shaft leaves a gap at its shank: there is no
    /// wall contact there (and no curve), only the shoulder.
    /// </summary>
    public static class ContactAssemblyFixtures
    {
        public sealed class Tess
        {
            public bool Perturbed;
            public int Seed;
            public static readonly Tess Plain = new Tess();
            public static Tess Jitter(int seed) { return new Tess() { Perturbed = true, Seed = seed }; }
        }

        // ---- loops ----

        static List<(int x, int y)> Rect(int x0, int y0, int x1, int y1) => new List<(int, int)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        static List<(int x, int y)> Rev(List<(int x, int y)> loop) { var r = new List<(int, int)>(loop); r.Reverse(); return r; }
        /// <summary> the counter-clockwise octagon around (cx, cy): |x| &lt;= a, |y| &lt;= a, |x| + |y| &lt;= a + b, vertices (+-a, +-b), (+-b, +-a) </summary>
        static List<(int x, int y)> Oct(int cx, int cy, int a, int b) => new List<(int, int)> {
            (cx + a, cy - b), (cx + a, cy + b), (cx + b, cy + a), (cx - b, cy + a), (cx - a, cy + b), (cx - a, cy - b), (cx - b, cy - a), (cx + b, cy - a) };
        static P2 P(long u, long v) => new P2(u, v);
        static List<P2> Poly(IEnumerable<(int x, int y)> loop) => loop.Select(p => new P2(p.x, p.y)).ToList();
        static Region2 RegionOf(List<(int x, int y)> outer, params List<(int x, int y)>[] holes) => new Region2(Poly(outer), holes.Select(h => (IEnumerable<P2>)Poly(h)));
        static IEnumerable<(P2, P2)> Edges(List<(int x, int y)> loop)
        {
            for (int i = 0; i < loop.Count; ++i) yield return (new P2(loop[i].x, loop[i].y), new P2(loop[(i + 1) % loop.Count].x, loop[(i + 1) % loop.Count].y));
        }
        static long OctArea(int a, int b) => 4L * a * a - 2L * (a - b) * (a - b);

        // ---- tessellation helpers ----

        static List<(int x, int y)> Prep(List<(int x, int y)> loop, Tess t) => t.Perturbed ? LatticeSolid.WithMidpoints(loop) : loop;

        // jittered points strictly inside the face (distinct, deterministic): a different Delaunay triangulation of the same planar face
        static List<(int x, int y)> Jitter(Tess t, List<(int x, int y)> outer, List<List<(int x, int y)>> holes, int count, int salt)
        {
            if (t.Perturbed == false) return null;
            var region = new Region2(Poly(outer), holes.Select(h => (IEnumerable<P2>)Poly(h)));
            var rnd = new Random(t.Seed * 7919 + salt);
            int minX = outer.Min(p => p.x), maxX = outer.Max(p => p.x), minY = outer.Min(p => p.y), maxY = outer.Max(p => p.y);
            var used = new HashSet<(int, int)>(outer);
            foreach (var h in holes) foreach (var p in h) used.Add(p);
            var r = new List<(int x, int y)>();
            for (int tries = 0; r.Count < count && tries < 5000; ++tries) {
                var p = (rnd.Next(minX, maxX + 1), rnd.Next(minY, maxY + 1));
                if (used.Contains(p) || region.Locate(new P2(p.Item1, p.Item2)) != Region2.Where.Inside) continue;
                used.Add(p); r.Add(p);
            }
            return r;
        }

        // a straight extrusion of the region (outer counter-clockwise, holes clockwise) between z0 and z1
        static DMesh3 Prism(List<(int x, int y)> outer, List<List<(int x, int y)>> holes, int z0, int z1, Tess t, int salt)
        {
            var o = Prep(outer, t);
            var hs = holes.Select(h => Prep(h, t)).ToList();
            var s = new LatticeSolid();
            s.HFace(z1, true, o, hs, Jitter(t, o, hs, 12, salt));
            s.HFace(z0, false, o, hs, Jitter(t, o, hs, 12, salt + 1));
            var mid = t.Perturbed ? new[] { (z0 + z1) / 2 } : null;
            s.Wall(o, z0, z1, mid, t.Perturbed);
            foreach (var h in hs) s.Wall(h, z0, z1, mid, t.Perturbed);
            return s.ToMesh();
        }

        // ---- contacts ----

        static Contact Horizontal(string name, int a, int b, int z, Region2 faceA, Region2 faceB, long area, IEnumerable<(P2, P2)> outline)
        {
            var c = new Contact() { Name = name, PartA = a, PartB = b, Origin = new long[] { 0, 0, z }, DirU = new long[] { 1, 0, 0 }, DirV = new long[] { 0, 1, 0 },
                FaceA = faceA, FaceB = faceB, Area = Rat.Int(area) };
            c.Outline.AddRange(outline);
            return c;
        }

        // the vertical wall over the edge p -> q: plane coordinates u along the edge (0 at p, 1 at q), v the height; each part's wall covers its z range
        static Contact Wall(string name, int a, int b, (int x, int y) p, (int x, int y) q, int za0, int za1, int zb0, int zb1)
        {
            Func<int, int, Region2> rect = (z0, z1) => new Region2(new[] { P(0, z0), P(1, z0), P(1, z1), P(0, z1) });
            int lo = Math.Max(za0, zb0), hi = Math.Min(za1, zb1);
            var c = new Contact() { Name = name, PartA = a, PartB = b, Origin = new long[] { p.x, p.y, 0 }, DirU = new long[] { q.x - p.x, q.y - p.y, 0 }, DirV = new long[] { 0, 0, 1 },
                FaceA = rect(za0, za1), FaceB = rect(zb0, zb1), Area = Rat.Int(hi - lo) };
            c.Outline.AddRange(new[] { (P(0, lo), P(1, lo)), (P(1, lo), P(1, hi)), (P(1, hi), P(0, hi)), (P(0, hi), P(0, lo)) });
            return c;
        }

        // ---- the boundary of the union of two convex polygons (exact; the outline of the contact of plates whose holes overlap) ----

        static List<(P2, P2)> UnionBoundary(List<(int x, int y)> a, List<(int x, int y)> b)
        {
            var pa = Poly(a); var pb = Poly(b);
            var ra = new Region2(pa); var rb = new Region2(pb);
            var r = new List<(P2, P2)>();
            Action<List<P2>, List<P2>, Region2, bool> add = (mine, other, otherRegion, first) => {
                for (int i = 0; i < mine.Count; ++i) {
                    P2 p = mine[i], q = mine[(i + 1) % mine.Count];
                    var ts = new List<Rat> { Rat.Zero, Rat.Int(1) };
                    for (int j = 0; j < other.Count; ++j) {
                        Rat t;
                        if (ContactTruth.Crossing(p, q, other[j], other[(j + 1) % other.Count], out t)) ts.Add(t);
                        // vertices of the other polygon on this edge (collinear overlaps)
                        P2 v = other[j];
                        if (ContactTruth.Cross(p, q, v).Sign == 0) {
                            Rat dd = (q.U - p.U) * (q.U - p.U) + (q.V - p.V) * (q.V - p.V);
                            Rat tv = ((v.U - p.U) * (q.U - p.U) + (v.V - p.V) * (q.V - p.V)) / dd;
                            if (tv.Sign > 0 && tv < Rat.Int(1)) ts.Add(tv);
                        }
                    }
                    ts = ts.Distinct().OrderBy(x => x).ToList();
                    for (int k = 0; k + 1 < ts.Count; ++k) {
                        P2 s0 = new P2(p.U + ts[k] * (q.U - p.U), p.V + ts[k] * (q.V - p.V));
                        P2 s1 = new P2(p.U + ts[k + 1] * (q.U - p.U), p.V + ts[k + 1] * (q.V - p.V));
                        Rat half = new Rat(1, 2);
                        P2 m = new P2((s0.U + s1.U) * half, (s0.V + s1.V) * half);
                        var w = otherRegion.Locate(m);
                        // outside the other polygon, or on its boundary (a shared piece of boundary: take it from the first polygon only)
                        if (w == Region2.Where.Outside || (w == Region2.Where.Boundary && first)) r.Add((s0, s1));
                    }
                }
            };
            add(pa, pb, rb, true);
            add(pb, pa, ra, false);
            return r;
        }

        // ---- the fixtures ----

        /// <summary> three stacked plates (8 x 8 x 1 each) with a bolt hole (an octagon): the holes match, or the middle plate's hole is shifted by one unit in x so that the rims overlap only partly </summary>
        public static ContactAssembly StackedPlates(bool offsetHoles, Tess t0, Tess t1, Tess t2)
        {
            var outer = Rect(0, 0, 64, 64);
            var hole = Oct(32, 32, 16, 8);
            var holeShifted = Oct(40, 32, 16, 8);
            var holes = new[] { hole, offsetHoles ? holeShifted : hole, hole };
            var tess = new[] { t0, t1, t2 };
            var asm = new ContactAssembly() { Name = "stacked-plates" + (offsetHoles ? "-offset-holes" : "-matching-holes") };
            for (int i = 0; i < 3; ++i)
                asm.Parts.Add(new Part() { Index = i, Name = "plate" + i, Mesh = Prism(outer, new List<List<(int x, int y)>> { Rev(holes[i]) }, 8 * i, 8 * i + 8, tess[i], 10 * i) });
            // the contact region: the plate minus the union of the two holes. Hand derivation of the area (lattice units squared): an octagon with
            // a = 16, b = 8 has area 4a^2 - 2(a-b)^2 = 896; shifted by (8, 0) the two octagons overlap in
            //   int_{-8}^{8} 24 dy + 2 int_8^16 (40 - 2y) dy = 384 + 256 = 640   (the width at height y is 2 w(y) - 8, w = min(16, 24 - |y|)),
            // so the union is 896 + 896 - 640 = 1152, and the contact region 4096 - 1152 = 2944; for equal holes 4096 - 896 = 3200
            long area = offsetHoles ? 4096 - (896 + 896 - 640) : 4096 - 896;
            for (int i = 0; i < 2; ++i) {
                var upper = holes[i + 1];
                var outline = new List<(P2, P2)>(Edges(outer));
                outline.AddRange(offsetHoles ? UnionBoundary(holes[i], upper) : Edges(hole).ToList());
                asm.Contacts.Add(Horizontal("plate" + i + "-plate" + (i + 1), i, i + 1, 8 * i + 8, RegionOf(outer, Rev(holes[i])), RegionOf(outer, Rev(upper)), area, outline));
            }
            asm.Note = "planar contacts at z = 1 and z = 2 between the faces with a hole; the hole walls do not touch";
            return asm;
        }

        /// <summary>
        /// a stepped shaft (head and shank, coaxial octagon prisms) in a stepped bore of a block: the head sits on the planar shoulder of the counterbore.
        /// Tight: the shank is the bore's octagon, so its walls touch the bore walls (exact planar contacts, for any tessellation); loose: the shank is a
        /// smaller octagon with a gap to the bore, and only the shoulder and the head's walls touch.
        /// </summary>
        public static ContactAssembly SteppedShaft(bool tight, Tess housing, Tess shaft)
        {
            var outer = Rect(0, 0, 64, 64);
            var bore = Oct(32, 32, 16, 8);
            var counterbore = Oct(32, 32, 24, 12);
            var shank = tight ? bore : Oct(32, 32, 12, 6);
            var ho = Prep(outer, housing); var hb = Prep(Rev(bore), housing); var hc = Prep(Rev(counterbore), housing);
            var bm = Rev(bore); var cm = Rev(counterbore);
            // housing: z 0..32, bore z 0..16, counterbore z 16..32
            var h = new LatticeSolid();
            h.HFace(0, false, ho, new List<List<(int x, int y)>> { hb }, Jitter(housing, ho, new List<List<(int x, int y)>> { hb }, 12, 1));
            h.HFace(32, true, ho, new List<List<(int x, int y)>> { hc }, Jitter(housing, ho, new List<List<(int x, int y)>> { hc }, 12, 2));
            var hcc = Prep(counterbore, housing);
            h.HFace(16, true, hcc, new List<List<(int x, int y)>> { hb }, Jitter(housing, hcc, new List<List<(int x, int y)>> { hb }, 10, 3));
            var mid = housing.Perturbed;
            h.Wall(ho, 0, 32, mid ? new[] { 8 } : null, mid);
            h.Wall(hb, 0, 16, mid ? new[] { 8 } : null, mid);
            h.Wall(hc, 16, 32, mid ? new[] { 24 } : null, mid);
            // shaft: shank z -8..16, head z 16..40
            var sk = Prep(shank, shaft); var hd = Prep(counterbore, shaft);
            var s = new LatticeSolid();
            s.HFace(-8, false, sk, null, Jitter(shaft, sk, new List<List<(int x, int y)>>(), 8, 1));
            s.HFace(40, true, hd, null, Jitter(shaft, hd, new List<List<(int x, int y)>>(), 12, 2));
            s.HFace(16, false, hd, new List<List<(int x, int y)>> { Prep(Rev(shank), shaft) }, Jitter(shaft, hd, new List<List<(int x, int y)>> { Prep(Rev(shank), shaft) }, 10, 3));
            var smid = shaft.Perturbed;
            s.Wall(sk, -8, 16, smid ? new[] { 4 } : null, smid);
            s.Wall(hd, 16, 40, smid ? new[] { 28 } : null, smid);
            var asm = new ContactAssembly() { Name = "stepped-shaft-" + (tight ? "tight" : "loose") };
            asm.Parts.Add(new Part() { Index = 0, Name = "housing", Mesh = h.ToMesh() });
            asm.Parts.Add(new Part() { Index = 1, Name = "shaft", Mesh = s.ToMesh() });
            // shoulder: the housing's ring between the bore and the counterbore against the shaft's underside ring between the shank and the head:
            // overlap = counterbore minus bore either way (the loose shank is inside the bore): 2016 - 896 (areas from 4a^2 - 2(a-b)^2)
            long ring = OctArea(24, 12) - OctArea(16, 8);
            var outline = new List<(P2, P2)>(Edges(counterbore));
            outline.AddRange(Edges(bore));
            asm.Contacts.Add(Horizontal("shoulder", 0, 1, 16, RegionOf(counterbore, bm), RegionOf(counterbore, Rev(shank)), ring, outline));
            // the head against the counterbore: eight planar walls, overlap z 16..32 of the head's 16..40
            for (int i = 0; i < 8; ++i)
                asm.Contacts.Add(Wall("head-wall" + i, 0, 1, counterbore[i], counterbore[(i + 1) % 8], 16, 32, 16, 40));
            // the shank against the bore (tight only): overlap z 0..16 of the shank's -8..16
            if (tight)
                for (int i = 0; i < 8; ++i)
                    asm.Contacts.Add(Wall("shank-wall" + i, 0, 1, bore[i], bore[(i + 1) % 8], 0, 16, -8, 16));
            asm.Note = tight ? "shoulder, head walls and shank walls touch (all planar); the shank sticks out below the housing"
                             : "shoulder and head walls touch; the shank has a gap to the bore (no contact, no curve)";
            return asm;
        }

        /// <summary>
        /// a block with a through mortise (16 x 32) and its tenon: flush (the tenon ends are in the planes of the block's top and bottom, contacts along lines
        /// there) or protruding (the tenon sticks out on both sides: the mortise rim is an edge on the tenon's walls)
        /// </summary>
        public static ContactAssembly MortiseAndTenon(bool flush, Tess block, Tess tenon)
        {
            var outer = Rect(0, 0, 64, 64);
            var slot = Rect(24, 16, 40, 48);
            int tz0 = flush ? 0 : -16, tz1 = flush ? 32 : 48;
            var asm = new ContactAssembly() { Name = "mortise-tenon-" + (flush ? "flush" : "protruding") };
            asm.Parts.Add(new Part() { Index = 0, Name = "block", Mesh = Prism(outer, new List<List<(int x, int y)>> { Rev(slot) }, 0, 32, block, 10) });
            asm.Parts.Add(new Part() { Index = 1, Name = "tenon", Mesh = Prism(slot, new List<List<(int x, int y)>>(), tz0, tz1, tenon, 20) });
            for (int i = 0; i < 4; ++i)
                asm.Contacts.Add(Wall("wall" + i, 0, 1, slot[i], slot[(i + 1) % 4], 0, 32, tz0, tz1));
            if (flush) {
                // the faces in the planes z = 32 and z = 0 touch along the rim of the mortise only (zero area)
                foreach (int z in new[] { 0, 32 }) {
                    var c = Horizontal("flush-z" + z, 0, 1, z, RegionOf(outer, Rev(slot)), RegionOf(slot), 0, Edges(slot));
                    asm.Contacts.Add(c);
                }
            }
            asm.Note = flush ? "four planar wall contacts, and line contacts along the rim at z = 0 and z = 32 (coplanar faces that share only the rim)"
                             : "four planar wall contacts; the rim of the mortise lies in the interior of the tenon's walls (z = 0 and z = 32 lines)";
            return asm;
        }

        /// <summary> a thin plate standing on a base plate: its bottom face (32 x 8 lattice units) on the base's top face, inside it, or overhanging its edge (partial overlap) </summary>
        public static ContactAssembly PlateOnPlate(bool overhang, Tess baseTess, Tess standing)
        {
            var asm = new ContactAssembly() { Name = "plate-on-plate-" + (overhang ? "overhang" : "inside") };
            var baseRect = Rect(0, 0, 64, 64);
            var footprint = overhang ? Rect(40, 28, 72, 36) : Rect(16, 28, 48, 36);
            asm.Parts.Add(new Part() { Index = 0, Name = "base", Mesh = Prism(baseRect, new List<List<(int x, int y)>>(), 0, 8, baseTess, 10) });
            asm.Parts.Add(new Part() { Index = 1, Name = "standing", Mesh = Prism(footprint, new List<List<(int x, int y)>>(), 8, 40, standing, 20) });
            int x0 = footprint[0].x, x1 = Math.Min(footprint[1].x, 64);
            var overlap = Rect(x0, 28, x1, 36);
            asm.Contacts.Add(Horizontal("footprint", 0, 1, 8, RegionOf(baseRect), RegionOf(footprint), (x1 - x0) * 8, Edges(overlap)));
            asm.Note = overhang ? "the footprint overlaps the base's top face only partly (its edge x = 64 crosses the footprint)" : "the footprint lies inside the base's top face: its edges lie in the interior of the base's face";
            return asm;
        }

        /// <summary> all fixtures, each with plain tessellations and with perturbed ones (one part, or each part with its own jitter) </summary>
        public static IEnumerable<ContactAssembly> All()
        {
            Tess p = Tess.Plain, j1 = Tess.Jitter(1), j2 = Tess.Jitter(2);
            foreach (bool offset in new[] { false, true }) {
                var a = StackedPlates(offset, p, p, p); a.Name += "/plain"; yield return a;
                a = StackedPlates(offset, p, j1, p); a.Name += "/perturbed-middle"; yield return a;
                a = StackedPlates(offset, j1, p, j2); a.Name += "/perturbed-outer"; yield return a;
            }
            foreach (bool tight in new[] { true, false }) {
                var a = SteppedShaft(tight, p, p); a.Name += "/plain"; yield return a;
                a = SteppedShaft(tight, p, j1); a.Name += "/perturbed-shaft"; yield return a;
                a = SteppedShaft(tight, j1, j2); a.Name += "/perturbed-both"; yield return a;
            }
            foreach (bool flush in new[] { true, false }) {
                var a = MortiseAndTenon(flush, p, p); a.Name += "/plain"; yield return a;
                a = MortiseAndTenon(flush, p, j1); a.Name += "/perturbed-tenon"; yield return a;
                a = MortiseAndTenon(flush, j1, j2); a.Name += "/perturbed-both"; yield return a;
            }
            foreach (bool overhang in new[] { false, true }) {
                var a = PlateOnPlate(overhang, p, p); a.Name += "/plain"; yield return a;
                a = PlateOnPlate(overhang, j1, p); a.Name += "/perturbed-base"; yield return a;
                a = PlateOnPlate(overhang, j1, j2); a.Name += "/perturbed-both"; yield return a;
            }
        }

        public static IEnumerable<object[]> Names() { foreach (var a in All()) yield return new object[] { a.Name }; }
        static Dictionary<string, ContactAssembly> cache;
        /// <summary> the fixture of that name (built once; the parts are not modified by any operation) </summary>
        public static ContactAssembly ByName(string name)
        {
            lock (typeof(ContactAssemblyFixtures)) {
                if (cache == null) cache = All().ToDictionary(a => a.Name);
                return cache[name];
            }
        }
    }
}
