// Procedural CAD-like shapes with exact creases and curvatures, for the varifold feature / curvature study
// (docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md). No mesh files: every shape is a set of parametric patches
// (planar quads, quarter cylinders, disc sectors, sphere octants), each tessellated on its own grid. The ground truth is
// analytic: each patch knows its outward normal at a point and its principal curvatures (K1 >= K2 >= 0 for these convex
// patches), and the creases are the patch borders where the two analytic normals differ.
using System;
using System.Collections.Generic;
using g3;

sealed class Patch
{
    public int Id;
    public Func<double, double, Vector3d> P;   // (u, v) in [0, 1]^2
    public Func<Vector3d, Vector3d> N;         // analytic outward unit normal at a point of the patch
    public double K1, K2;                      // analytic principal curvatures (unsigned)
    public double LenU, LenV;                  // analytic lengths of the u and v borders (shared borders give equal counts)
}

/// <summary> a triangle soup: three positions per triangle, with the clean position, patch and orientation of each corner </summary>
sealed class Soup
{
    public List<Vector3d> V = new List<Vector3d>();       // 3 per triangle
    public List<Vector3d> Clean = new List<Vector3d>();   // the unbroken positions (ground truth lookups)
    public List<int> PatchOf = new List<int>();           // per triangle
    public Dictionary<int, Vector3d> Shift = new Dictionary<int, Vector3d>();   // per patch: the gap breakage's move
    public int Count => PatchOf.Count;
    public Soup Copy()
    {
        var s = new Soup();
        s.V.AddRange(V); s.Clean.AddRange(Clean); s.PatchOf.AddRange(PatchOf);
        foreach (var kv in Shift) s.Shift[kv.Key] = kv.Value;
        return s;
    }
    public void Add(Vector3d a, Vector3d b, Vector3d c, Vector3d ca, Vector3d cb, Vector3d cc, int patch)
    {
        V.Add(a); V.Add(b); V.Add(c); Clean.Add(ca); Clean.Add(cb); Clean.Add(cc); PatchOf.Add(patch);
    }
    public DMesh3 ToMesh()
    {
        var m = new DMesh3();
        for (int t = 0; t < Count; ++t) {
            int a = m.AppendVertex(V[3 * t]), b = m.AppendVertex(V[3 * t + 1]), c = m.AppendVertex(V[3 * t + 2]);
            m.AppendTriangle(a, b, c);
        }
        return m;
    }
}

static class SoupShapes
{
    public static readonly string[] Names = { "box", "slot", "vnotch", "fillet", "rbox" };
    const double X = 2, Y = 1.5, Z = 1;
    /// <summary> the fillet radius of fillet and rbox (0.25 in the study; the gate probe sets it near the wrap's ball) </summary>
    public static double R = 0.25;

    public static List<Patch> Make(string name)
    {
        var ps = new List<Patch>();
        switch (name) {
            case "box": Box(ps); break;
            case "slot":
                Prism(ps, new[] { V2(0, 0), V2(1.5, 0), V2(1.5, 1), V2(1.0, 1), V2(1.0, 0.5), V2(0.5, 0.5), V2(0.5, 1), V2(0, 1) },
                    new[] { Q(0, 0, .5, 0, .5, .5, 0, .5), Q(.5, 0, 1, 0, 1, .5, .5, .5), Q(1, 0, 1.5, 0, 1.5, .5, 1, .5),
                            Q(0, .5, .5, .5, .5, 1, 0, 1), Q(1, .5, 1.5, .5, 1.5, 1, 1, 1) });
                break;
            case "vnotch":
                // a V groove 0.5 wide and 0.5 deep; the caps in vertical strips so that every shared border has one count
                // (the two notch borders take the count of their 0.25 wide strip: the notch faces are 2.2 : 1 anisotropic)
                Prism(ps, new[] { V2(0, 0), V2(1.5, 0), V2(1.5, 1), V2(1.0, 1), V2(0.75, 0.5), V2(0.5, 1), V2(0, 1) },
                    new[] { Q(0, 0, .5, 0, .5, 1, 0, 1), Q(1, 0, 1.5, 0, 1.5, 1, 1, 1),
                            Q(.5, 0, .75, 0, .75, .5, .5, 1, .25, 1), Q(.75, 0, 1, 0, 1, 1, .75, .5, .25, 1) },
                    seg => seg == 3 || seg == 4 ? .25 : 0);
                break;
            case "fillet": Fillet(ps); break;
            case "rbox": RoundedBox(ps); break;
            default: throw new ArgumentException("unknown shape " + name);
        }
        for (int i = 0; i < ps.Count; ++i) ps[i].Id = i;
        return ps;
    }

    public static double Area(string name) { var m = Tessellate(Make(name), 0.05, null).ToMesh(); return MeshMeasurements.AreaT(m, m.TriangleIndices()); }

    static Vector2d V2(double a, double b) => new Vector2d(a, b);
    // a cap quad p00, p10, p11, p01 of a profile, optionally with the lengths its counts are taken from
    static Vector2d[] Q(double a, double b, double c, double d, double e, double f, double g, double h, double lu = 0, double lv = 0) => new[] { V2(a, b), V2(c, d), V2(e, f), V2(g, h), V2(lu, lv) };

    // planar quad p00, p10, p11, p01 (bilinear; collapsed borders allowed)
    static Patch Quad(Vector3d p00, Vector3d p10, Vector3d p11, Vector3d p01, Vector3d n, double lu = 0, double lv = 0)
    {
        return new Patch {
            P = (u, v) => (1 - u) * (1 - v) * p00 + u * (1 - v) * p10 + u * v * p11 + (1 - u) * v * p01,
            N = _ => n, LenU = lu > 0 ? lu : Math.Max(p00.Distance(p10), p01.Distance(p11)), LenV = lv > 0 ? lv : Math.Max(p00.Distance(p01), p10.Distance(p11))
        };
    }

    static void Box(List<Patch> ps)
    {
        var c = new Vector3d[8];
        for (int i = 0; i < 8; ++i) c[i] = new Vector3d((i & 1) != 0 ? X : 0, (i & 2) != 0 ? Y : 0, (i & 4) != 0 ? Z : 0);
        ps.Add(Quad(c[0], c[1], c[3], c[2], -Vector3d.AxisZ)); ps.Add(Quad(c[4], c[5], c[7], c[6], Vector3d.AxisZ));
        ps.Add(Quad(c[0], c[1], c[5], c[4], -Vector3d.AxisY)); ps.Add(Quad(c[2], c[3], c[7], c[6], Vector3d.AxisY));
        ps.Add(Quad(c[0], c[2], c[6], c[4], -Vector3d.AxisX)); ps.Add(Quad(c[1], c[3], c[7], c[5], Vector3d.AxisX));
    }

    // profile (y, z), counter-clockwise, extruded along x over [0, X]; caps given as quads of the profile
    static void Prism(List<Patch> ps, Vector2d[] prof, Vector2d[][] caps, Func<int, double> segLen = null)
    {
        Func<Vector2d, double, Vector3d> P3 = (p, x) => new Vector3d(x, p.x, p.y);
        for (int i = 0; i < prof.Length; ++i) {
            Vector2d a = prof[i], b = prof[(i + 1) % prof.Length], d = (b - a).Normalized;
            var n = new Vector3d(0, d.y, -d.x);   // outward of a counter-clockwise profile
            ps.Add(Quad(P3(a, 0), P3(b, 0), P3(b, X), P3(a, X), n, segLen == null ? 0 : segLen(i)));
        }
        foreach (var q in caps) {
            ps.Add(Quad(P3(q[0], 0), P3(q[1], 0), P3(q[2], 0), P3(q[3], 0), -Vector3d.AxisX, q[4].x, q[4].y));
            ps.Add(Quad(P3(q[0], X), P3(q[1], X), P3(q[2], X), P3(q[3], X), Vector3d.AxisX, q[4].x, q[4].y));
        }
    }

    // quarter cylinder: axis from a to b, angle from d1 (u = 0) to d2 (u = 1)
    static Patch Cyl(Vector3d a, Vector3d b, Vector3d d1, Vector3d d2, double r)
    {
        Vector3d ax = (b - a).Normalized;
        return new Patch {
            P = (u, v) => a + v * (b - a) + r * (Math.Cos(u * Math.PI / 2) * d1 + Math.Sin(u * Math.PI / 2) * d2),
            N = p => { Vector3d w = p - a; w -= w.Dot(ax) * ax; return w.Normalized; },
            K1 = 1 / r, K2 = 0, LenU = r * Math.PI / 2, LenV = a.Distance(b)
        };
    }

    // flat quarter disc: centre c, radius s r, angle from d1 (v = 0) to d2 (v = 1); the border u = 0 is collapsed
    static Patch Sector(Vector3d c, Vector3d d1, Vector3d d2, double r, Vector3d n)
    {
        return new Patch {
            P = (u, v) => c + u * r * (Math.Cos(v * Math.PI / 2) * d1 + Math.Sin(v * Math.PI / 2) * d2),
            N = _ => n, LenU = r, LenV = r * Math.PI / 2
        };
    }

    // sphere octant: centre c, pole e3 (u = 0, collapsed), meridians to e1 (v = 0) and e2 (v = 1), equator at u = 1
    static Patch Octant(Vector3d c, Vector3d e1, Vector3d e2, Vector3d e3, double r)
    {
        return new Patch {
            P = (u, v) => c + r * (Math.Sin(u * Math.PI / 2) * (Math.Cos(v * Math.PI / 2) * e1 + Math.Sin(v * Math.PI / 2) * e2) + Math.Cos(u * Math.PI / 2) * e3),
            N = p => (p - c).Normalized, K1 = 1 / r, K2 = 1 / r, LenU = r * Math.PI / 2, LenV = r * Math.PI / 2
        };
    }

    // box X x Y x Z with its four vertical edges filleted (radius R): creases only on the top and bottom rims (lines and arcs)
    static void Fillet(List<Patch> ps)
    {
        Vector3d ex = Vector3d.AxisX, ey = Vector3d.AxisY, ez = Vector3d.AxisZ;
        ps.Add(Quad(new Vector3d(R, 0, 0), new Vector3d(X - R, 0, 0), new Vector3d(X - R, 0, Z), new Vector3d(R, 0, Z), -ey));
        ps.Add(Quad(new Vector3d(R, Y, 0), new Vector3d(X - R, Y, 0), new Vector3d(X - R, Y, Z), new Vector3d(R, Y, Z), ey));
        ps.Add(Quad(new Vector3d(0, R, 0), new Vector3d(0, Y - R, 0), new Vector3d(0, Y - R, Z), new Vector3d(0, R, Z), -ex));
        ps.Add(Quad(new Vector3d(X, R, 0), new Vector3d(X, Y - R, 0), new Vector3d(X, Y - R, Z), new Vector3d(X, R, Z), ex));
        var corners = new[] { (R, R, -ex, -ey), (X - R, R, ex, -ey), (X - R, Y - R, ex, ey), (R, Y - R, -ex, ey) };
        foreach (var (cx, cy, d1, d2) in corners) ps.Add(Cyl(new Vector3d(cx, cy, 0), new Vector3d(cx, cy, Z), d1, d2, R));
        foreach (double z in new[] { 0.0, Z }) {
            Vector3d n = z == 0 ? -ez : ez;
            Func<double, double, Vector3d> P = (a, b) => new Vector3d(a, b, z);
            ps.Add(Quad(P(R, R), P(X - R, R), P(X - R, Y - R), P(R, Y - R), n));
            ps.Add(Quad(P(R, 0), P(X - R, 0), P(X - R, R), P(R, R), n));
            ps.Add(Quad(P(R, Y - R), P(X - R, Y - R), P(X - R, Y), P(R, Y), n));
            ps.Add(Quad(P(0, R), P(R, R), P(R, Y - R), P(0, Y - R), n));
            ps.Add(Quad(P(X - R, R), P(X, R), P(X, Y - R), P(X - R, Y - R), n));
            foreach (var (cx, cy, d1, d2) in corners) ps.Add(Sector(P(cx, cy), d1, d2, R, n));
        }
    }

    // box X x Y x Z with every edge rounded (radius R): no crease; flats, quarter cylinders and sphere octants
    static void RoundedBox(List<Patch> ps)
    {
        double[] D = { X, Y, Z };
        Vector3d E(int k) => k == 0 ? Vector3d.AxisX : k == 1 ? Vector3d.AxisY : Vector3d.AxisZ;
        double Inner(int k, int s) => s > 0 ? D[k] - R : R;
        for (int k = 0; k < 3; ++k)
            for (int s = -1; s <= 1; s += 2) {
                int i = (k + 1) % 3, j = (k + 2) % 3;
                Vector3d Pt(double a, double b) { var p = new double[3]; p[k] = s > 0 ? D[k] : 0; p[i] = a; p[j] = b; return new Vector3d(p[0], p[1], p[2]); }
                ps.Add(Quad(Pt(R, R), Pt(D[i] - R, R), Pt(D[i] - R, D[j] - R), Pt(R, D[j] - R), s * E(k)));
            }
        for (int k = 0; k < 3; ++k) {
            int i = (k + 1) % 3, j = (k + 2) % 3;
            for (int si = -1; si <= 1; si += 2)
                for (int sj = -1; sj <= 1; sj += 2) {
                    var a = new double[3]; a[i] = Inner(i, si); a[j] = Inner(j, sj); a[k] = R;
                    var b = (double[])a.Clone(); b[k] = D[k] - R;
                    ps.Add(Cyl(new Vector3d(a[0], a[1], a[2]), new Vector3d(b[0], b[1], b[2]), si * E(i), sj * E(j), R));
                }
        }
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    ps.Add(Octant(new Vector3d(Inner(0, sx), Inner(1, sy), Inner(2, sz)), sx * E(0), sy * E(1), sz * E(2), R));
    }

    public static int Count(double len, double h, double mult) => Math.Max(1, (int)Math.Round(len / h * mult));

    /// <summary>
    /// every patch on its own grid of spacing about h (mult[patch] scales it: a non-conforming seam where neighbours differ);
    /// triangles oriented by the analytic normal; degenerate triangles (collapsed borders) skipped
    /// </summary>
    public static Soup Tessellate(List<Patch> ps, double h, Func<int, double> mult)
    {
        var s = new Soup();
        foreach (Patch p in ps) {
            double f = mult == null ? 1 : mult(p.Id);
            int nu = Count(p.LenU, h, f), nv = Count(p.LenV, h, f);
            var g = new Vector3d[(nu + 1) * (nv + 1)];
            for (int j = 0; j <= nv; ++j) for (int i = 0; i <= nu; ++i) g[j * (nu + 1) + i] = p.P((double)i / nu, (double)j / nv);
            for (int j = 0; j < nv; ++j)
                for (int i = 0; i < nu; ++i) {
                    Vector3d a = g[j * (nu + 1) + i], b = g[j * (nu + 1) + i + 1], c = g[(j + 1) * (nu + 1) + i + 1], d = g[(j + 1) * (nu + 1) + i];
                    // the diagonal alternates (a checkerboard), so no direction is preferred
                    if (((i + j) & 1) == 0) { Tri(s, p, a, b, c); Tri(s, p, a, c, d); } else { Tri(s, p, a, b, d); Tri(s, p, b, c, d); }
                }
        }
        return s;
    }

    static void Tri(Soup s, Patch p, Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d cr = (b - a).Cross(c - a);
        double size = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a)));
        if (!(cr.Length > 1e-12 * size * size)) return;
        if (cr.Dot(p.N((a + b + c) / 3)) < 0) { var t = b; b = c; c = t; }
        s.Add(a, b, c, a, b, c, p.Id);
    }

    /// <summary>
    /// the ground truth from the conforming clean tessellation: crease segments (patch borders whose analytic normals differ by
    /// more than 5 degrees, with that dihedral) and discontinuity segments (creases, and borders where the curvature jumps)
    /// </summary>
    public static (List<(Vector3d a, Vector3d b, double dih)> creases, List<(Vector3d a, Vector3d b)> jumps) Truth(List<Patch> ps, Soup clean, double h)
    {
        var (cr, jumps, _) = TruthWithPatches(ps, clean, h);
        return (cr, jumps);
    }

    /// <summary> Truth, with the two patches of every crease segment </summary>
    public static (List<(Vector3d a, Vector3d b, double dih)> creases, List<(Vector3d a, Vector3d b)> jumps, List<(int pa, int pb)> pairs) TruthWithPatches(List<Patch> ps, Soup clean, double h)
    {
        var pairs = new List<(int, int)>();
        var mesh = SoupWeld.Weld(clean, 1e-9 * h, out int[] triOf);
        var tp = new int[mesh.MaxTriangleID];
        for (int t = 0; t < clean.Count; ++t) if (triOf[t] >= 0) tp[triOf[t]] = clean.PatchOf[t];
        var cr = new List<(Vector3d, Vector3d, double)>(); var jumps = new List<(Vector3d, Vector3d)>();
        foreach (int eid in mesh.EdgeIndices()) {
            Index2i et = mesh.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Patch pa = ps[tp[et.a]], pb = ps[tp[et.b]];
            if (pa.Id == pb.Id) continue;
            Index2i ev = mesh.GetEdgeV(eid);
            Vector3d a = mesh.GetVertex(ev.a), b = mesh.GetVertex(ev.b), m = 0.5 * (a + b);
            double dih = Vector3d.AngleD(pa.N(m), pb.N(m));
            if (dih > 5) { cr.Add((a, b, dih)); jumps.Add((a, b)); pairs.Add((pa.Id, pb.Id)); }
            else if (pa.K1 != pb.K1 || pa.K2 != pb.K2) jumps.Add((a, b));
        }
        return (cr, jumps, pairs);
    }
}
