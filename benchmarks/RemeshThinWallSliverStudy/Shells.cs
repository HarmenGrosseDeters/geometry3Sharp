// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Procedural thin-walled solids with known wall thickness t and known radius of curvature r of the wall surface.
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

static class Shells
{
    // a closed solid from a mid-surface S(u, v) (u, v in [0, 1]) with unit normal n: outer = S + t/2 n, inner = S - t/2 n,
    // rim strips along the open boundary. periodicU: u wraps (index 0 .. nu-1), else 0 .. nu.
    public static DMesh3 Shell(int nu, int nv, bool periodicU, Func<double, double, (Vector3d p, Vector3d n)> S, double t)
    {
        int cu = periodicU ? nu : nu + 1, cv = nv + 1;
        var m = new DMesh3();
        int[,] o = new int[cu, cv], inn = new int[cu, cv];
        for (int i = 0; i < cu; ++i)
            for (int j = 0; j < cv; ++j) {
                var (p, n) = S((double)i / nu, (double)j / nv);
                o[i, j] = m.AppendVertex(p + 0.5 * t * n);
                inn[i, j] = m.AppendVertex(p - 0.5 * t * n);
            }
        var tris = new List<Index3i>();
        for (int i = 0; i < nu; ++i) {
            int i1 = periodicU ? (i + 1) % nu : i + 1;
            for (int j = 0; j < nv; ++j) {
                tris.Add(new Index3i(o[i, j], o[i1, j], o[i1, j + 1])); tris.Add(new Index3i(o[i, j], o[i1, j + 1], o[i, j + 1]));
                tris.Add(new Index3i(inn[i, j], inn[i1, j + 1], inn[i1, j])); tris.Add(new Index3i(inn[i, j], inn[i, j + 1], inn[i1, j + 1]));
            }
        }
        // rims: for every directed boundary edge a -> b of the outer surface, the quad (b, a, a', b') (a' = the inner vertex below a)
        var outerSet = new HashSet<long>();
        var dir = new HashSet<(int, int)>();
        var innerOf = new Dictionary<int, int>();
        for (int i = 0; i < cu; ++i) for (int j = 0; j < cv; ++j) innerOf[o[i, j]] = inn[i, j];
        foreach (var tr in tris) {
            if (!innerOf.ContainsKey(tr.a)) continue;       // outer triangles only
            for (int k = 0; k < 3; ++k) dir.Add((tr[k], tr[(k + 1) % 3]));
        }
        foreach (var (a, b) in dir.ToList())
            if (!dir.Contains((b, a))) {
                int a2 = innerOf[a], b2 = innerOf[b];
                tris.Add(new Index3i(b, a, a2)); tris.Add(new Index3i(b, a2, b2));
            }
        foreach (var tr in tris) m.AppendTriangle(tr.a, tr.b, tr.c);
        if (Volume(m) < 0) m.ReverseOrientation();
        return m;
    }

    public static double Volume(DMesh3 m)
    {
        double v = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            v += a.Dot(b.Cross(c));
        }
        return v / 6;
    }

    public class Body
    {
        public string Name; public DMesh3 Mesh; public double T;
        public Func<Vector3d, double> R;        // radius of curvature of the wall surface at a point (the smaller principal one); +inf if flat
    }

    public static Body Plate(double a, double t, int n) => new Body() {
        Name = $"plate_t{t}", T = t, R = p => double.PositiveInfinity,
        Mesh = Shell(n, n, false, (u, v) => (new Vector3d(a * (u - 0.5), a * (v - 0.5), 0), new Vector3d(0, 0, 1)), t) };

    public static Body Tube(double R, double H, double t, int slices, int rings) => new Body() {
        Name = $"tube_t{t}", T = t, R = p => R,
        Mesh = Shell(slices, rings, true, (u, v) => {
            double th = 2 * Math.PI * u; return (new Vector3d(R * Math.Cos(th), R * Math.Sin(th), H * v), new Vector3d(Math.Cos(th), Math.Sin(th), 0)); }, t) };

    // a quarter torus: centre circle radius Rb, wall mid-surface a circle of radius ro (so the wall bends with radius ro across
    // and (Rb + ro cos(phi)) / cos(phi) along the bend: the inside of a tight bend is the smaller one)
    public static Body Bend(double Rb, double ro, double t, int slices, int arcs) => new Body() {
        Name = $"bend_t{t}", T = t,
        R = p => {
            double rho = Math.Sqrt(p.x * p.x + p.y * p.y), phi = Math.Atan2(p.z, rho - Rb), c = Math.Cos(phi);
            double major = Math.Abs(c) < 1e-9 ? double.PositiveInfinity : Math.Abs((Rb + ro * c) / c);
            return Math.Min(ro, major);
        },
        Mesh = Shell(slices, arcs, true, (u, v) => {
            double ph = 2 * Math.PI * u, ps = 0.5 * Math.PI * v;
            double cp = Math.Cos(ph), sp = Math.Sin(ph), cs = Math.Cos(ps), ss = Math.Sin(ps);
            return (new Vector3d((Rb + ro * cp) * cs, (Rb + ro * cp) * ss, ro * sp), new Vector3d(cp * cs, cp * ss, sp)); }, t) };
}
