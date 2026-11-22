using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;

namespace Meshing2HardProblems
{
    /// <summary> A noded planar straight line graph: one outer ring and interior segments that meet only at shared end points. </summary>
    public sealed class Pslg
    {
        public string Name;
        public double PhiDeg;
        public List<Vector2d> P = new List<Vector2d>();
        public List<int> Ring = new List<int>();
        public List<(int a, int b)> Interior = new List<(int, int)>();

        public int Add(double x, double y) { P.Add(new Vector2d(x, y)); return P.Count - 1; }
        /// <summary> the ring's edges first, then the interior segments: the ids of the input segments everywhere in this benchmark </summary>
        public List<(int a, int b)> Segments()
        {
            var all = new List<(int, int)>();
            for (int i = 0; i < Ring.Count; ++i) all.Add((Ring[i], Ring[(i + 1) % Ring.Count]));
            all.AddRange(Interior);
            return all;
        }

        public bool Inside(Vector2d q)
        {
            bool inside = false;
            for (int i = 0, j = Ring.Count - 1; i < Ring.Count; j = i++) {
                var a = P[Ring[i]]; var b = P[Ring[j]];
                if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public Mesh2Input ToInput(double minAngle, bool constrainedQuality, double snap = 0)
        {
            var input = new Mesh2Input { X = P.Select(p => p.x).ToArray(), Y = P.Select(p => p.y).ToArray(), MinAngleDeg = minAngle, ConstrainedQuality = constrainedQuality, SnapTolerance = snap };
            input.Rings.Add(new RingInput(Ring.ToArray(), false));
            foreach (var s in Interior) input.Segments.Add(new SegmentInput(s.a, s.b));
            return input;
        }
    }

    /// <summary> An input angle below 60 degrees between two input segments that are neighbours around their common vertex, inside the domain. </summary>
    public sealed class Wedge
    {
        public int Apex, Seg1, Seg2;
        public double Phi;          // radians
        public Vector2d Dir1, Dir2; // unit, Dir2 counter-clockwise of Dir1 by Phi
    }

    public static class Wedges
    {
        public const double Sixty = Math.PI / 3;

        public static List<Wedge> Of(Pslg g)
        {
            var segs = g.Segments();
            var at = new Dictionary<int, List<int>>();
            for (int s = 0; s < segs.Count; ++s) {
                if (!at.TryGetValue(segs[s].a, out var la)) at[segs[s].a] = la = new List<int>();
                if (!at.TryGetValue(segs[s].b, out var lb)) at[segs[s].b] = lb = new List<int>();
                la.Add(s); lb.Add(s);
            }
            var result = new List<Wedge>();
            foreach (var kv in at.OrderBy(k => k.Key)) {
                int x = kv.Key; var list = kv.Value;
                if (list.Count < 2) continue;
                Vector2d Dir(int s) { int o = segs[s].a == x ? segs[s].b : segs[s].a; var d = g.P[o] - g.P[x]; return d / d.Length; }
                var sorted = list.OrderBy(s => Math.Atan2(Dir(s).y, Dir(s).x)).ToList();
                for (int i = 0; i < sorted.Count; ++i) {
                    int s1 = sorted[i], s2 = sorted[(i + 1) % sorted.Count];
                    if (s1 == s2) continue;
                    Vector2d d1 = Dir(s1), d2 = Dir(s2);
                    double phi = Math.Atan2(d1.x * d2.y - d1.y * d2.x, d1.x * d2.x + d1.y * d2.y);
                    if (phi <= 0) phi += 2 * Math.PI;
                    if (phi >= Sixty) continue;
                    double len = Math.Min((g.P[segs[s1].a] - g.P[segs[s1].b]).Length, (g.P[segs[s2].a] - g.P[segs[s2].b]).Length);
                    var mid = d1 + d2; mid /= mid.Length;
                    if (!g.Inside(g.P[x] + mid * (len * 1e-6))) continue;
                    result.Add(new Wedge { Apex = x, Seg1 = s1, Seg2 = s2, Phi = phi, Dir1 = d1, Dir2 = d2 });
                }
            }
            return result;
        }
    }

    /// <summary> The procedural adversarial inputs of the small-angle study (HP 6). Every family takes the small angle in degrees. </summary>
    public static class Hp6Inputs
    {
        static double Rad(double deg) => deg * Math.PI / 180;

        static Pslg Box(string name, double phi)
        {
            var g = new Pslg { Name = name, PhiDeg = phi };
            g.Ring.Add(g.Add(-1, -1)); g.Ring.Add(g.Add(2, -1)); g.Ring.Add(g.Add(2, 2)); g.Ring.Add(g.Add(-1, 2));
            return g;
        }

        /// <summary> a triangle domain with the angle at the origin (the catalogue's F-38/a shape) </summary>
        public static Pslg Wedge(double phi)
        {
            var g = new Pslg { Name = "wedge", PhiDeg = phi };
            g.Ring.Add(g.Add(0, 0)); g.Ring.Add(g.Add(1, 0)); g.Ring.Add(g.Add(Math.Cos(Rad(phi)), Math.Sin(Rad(phi))));
            return g;
        }

        /// <summary> two dangling segments from the origin inside a box </summary>
        public static Pslg BoxWedge(double phi)
        {
            var g = Box("boxwedge", phi);
            int o = g.Add(0, 0);
            g.Interior.Add((o, g.Add(1, 0))); g.Interior.Add((o, g.Add(Math.Cos(Rad(phi)), Math.Sin(Rad(phi)))));
            return g;
        }

        /// <summary> k segments from the origin, neighbours phi apart: several small angles at one vertex </summary>
        public static Pslg Fan(double phi, int k = 5)
        {
            var g = Box("fan" + k, phi);
            int o = g.Add(0, 0);
            for (int i = 0; i < k; ++i) g.Interior.Add((o, g.Add(Math.Cos(Rad(phi * i)), Math.Sin(Rad(phi * i)))));
            return g;
        }

        /// <summary> a saw: n teeth with tip angle phi side by side, so the apexes are close to each other compared with their segments </summary>
        public static Pslg Comb(double phi, int n = 6)
        {
            var g = new Pslg { Name = "comb" + n, PhiDeg = phi };
            double w = 2 * Math.Tan(Rad(phi) / 2);
            g.Ring.Add(g.Add(0, 0)); g.Ring.Add(g.Add(0, -1)); g.Ring.Add(g.Add(n * w, -1));
            for (int i = n; i >= 1; --i) { g.Ring.Add(g.Add(i * w, 0)); g.Ring.Add(g.Add((i - 0.5) * w, 1)); }
            return g;
        }

        /// <summary> a segment that ends on the inside of another at the angle phi (noded: the vertex is given) </summary>
        public static Pslg TJunction(double phi)
        {
            var g = Box("tjunction", phi);
            int o = g.Add(0, 0);
            g.Interior.Add((g.Add(-0.75, 0), o)); g.Interior.Add((o, g.Add(1, 0))); g.Interior.Add((o, g.Add(Math.Cos(Rad(phi)), Math.Sin(Rad(phi)))));
            return g;
        }

        /// <summary> two segments that cross at the angle phi (noded) </summary>
        public static Pslg Crossing(double phi)
        {
            var g = Box("crossing", phi);
            int o = g.Add(0, 0);
            foreach (double a in new[] { 0, phi, 180, 180 + phi }) g.Interior.Add((o, g.Add(0.75 * Math.Cos(Rad(a)), 0.75 * Math.Sin(Rad(a)))));
            return g;
        }

        /// <summary> a rhombus domain: two corners of angle phi facing each other </summary>
        public static Pslg Rhombus(double phi)
        {
            var g = new Pslg { Name = "rhombus", PhiDeg = phi };
            double c = Math.Cos(Rad(phi) / 2), s = Math.Sin(Rad(phi) / 2);
            g.Ring.Add(g.Add(0, 0)); g.Ring.Add(g.Add(c, -s)); g.Ring.Add(g.Add(2 * c, 0)); g.Ring.Add(g.Add(c, s));
            return g;
        }

        /// <summary> two apexes 1/64 apart on one segment, their wedges on opposite sides: clusters that interact at two scales </summary>
        public static Pslg Twin(double phi)
        {
            var g = Box("twin", phi);
            const double eps = 1.0 / 64;
            int o = g.Add(0, 0), e = g.Add(eps, 0);
            g.Interior.Add((o, e)); g.Interior.Add((e, g.Add(1, 0)));
            g.Interior.Add((o, g.Add(Math.Cos(Rad(phi)), Math.Sin(Rad(phi)))));
            g.Interior.Add((e, g.Add(eps + Math.Cos(Rad(phi)), -Math.Sin(Rad(phi)))));
            return g;
        }

        public static readonly (string name, Func<double, Pslg> make)[] Families = {
            ("wedge", Wedge), ("boxwedge", BoxWedge), ("fan5", p => Fan(p)), ("comb6", p => Comb(p)), ("tjunction", TJunction),
            ("crossing", Crossing), ("rhombus", Rhombus), ("twin", Twin),
        };
    }
}
