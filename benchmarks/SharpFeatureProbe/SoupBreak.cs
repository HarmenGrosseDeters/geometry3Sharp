// Breakage of a clean soup and the weld baseline (docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md).
using System;
using System.Collections.Generic;
using System.Globalization;
using g3;

static class SoupBreak
{
    /// <summary>
    /// the breakage spec, comma separated: jitter:a (every corner of every triangle moved by up to a h, independently: the
    /// copies of a vertex part), holes:p (a fraction p of the triangles dropped), dup:p (a fraction p duplicated, half of them
    /// reversed), gap:g (every patch moved by g h in its own random direction: CAD gaps and overlaps between faces), flip:p
    /// (a fraction p reversed). nonconf (the patch grids differ, T-junctions on every seam) is applied at tessellation.
    /// mix:s is all of them at level s: jitter 0.05 s, holes 0.05 s, dup 0.1 s, gap 0.3 s, flip 0.2 s, nonconf for s > 0.
    /// </summary>
    public static Soup Apply(Soup s0, string spec, double h, int seed)
    {
        var s = s0.Copy();
        if (string.IsNullOrEmpty(spec) || spec == "clean" || spec == "soup" || spec == "nonconf") return s;
        foreach (string part in Expand(spec)) {
            string[] kv = part.Split(':');
            double x = kv.Length > 1 ? double.Parse(kv[1], CultureInfo.InvariantCulture) : 0;

            var rnd = new Random(seed * 7919 + Fixed(kv[0]));
            switch (kv[0]) {
                case "nonconf": break;
                case "jitter":
                    for (int i = 0; i < s.V.Count; ++i) s.V[i] += x * h * Ball(rnd);
                    break;
                case "gap": {
                    var shift = new Dictionary<int, Vector3d>();
                    for (int t = 0; t < s.Count; ++t) {
                        if (!shift.TryGetValue(s.PatchOf[t], out Vector3d d)) { d = x * h * Ball(rnd).Normalized; shift[s.PatchOf[t]] = d; s.Shift[s.PatchOf[t]] = d; }
                        for (int k = 0; k < 3; ++k) s.V[3 * t + k] += d;
                    }
                    break;
                }
                case "holes": {
                    var r = new Soup();
                    for (int t = 0; t < s.Count; ++t) if (rnd.NextDouble() >= x) Copy(s, t, r, false);
                    s = r; break;
                }
                case "dup": {
                    int n = s.Count;
                    for (int t = 0; t < n; ++t) if (rnd.NextDouble() < x) Copy(s, t, s, rnd.NextDouble() < 0.5);
                    break;
                }
                case "flip":
                    for (int t = 0; t < s.Count; ++t) if (rnd.NextDouble() < x) {
                        var a = s.V[3 * t + 1]; s.V[3 * t + 1] = s.V[3 * t + 2]; s.V[3 * t + 2] = a;
                        var c = s.Clean[3 * t + 1]; s.Clean[3 * t + 1] = s.Clean[3 * t + 2]; s.Clean[3 * t + 2] = c;
                    }
                    break;
                default: throw new ArgumentException("unknown breakage " + part);
            }
        }
        return s;
    }

    public static bool NonConforming(string spec)
    {
        foreach (string p in Expand(spec)) if (p == "nonconf") return true;
        return false;
    }

    static IEnumerable<string> Expand(string spec)
    {
        foreach (string part in spec.Split(',')) {
            if (part.StartsWith("mix:") || part.StartsWith("mixnh:")) {
                // (mixnh: the same without the holes, which let a wrap at alpha below the hole size inside: issue #1542)
                bool holes = part.StartsWith("mix:");
                double m = double.Parse(part.Substring(part.IndexOf(':') + 1), CultureInfo.InvariantCulture);
                if (m <= 0) continue;
                string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
                yield return "nonconf"; yield return "gap:" + F(0.3 * m); yield return "jitter:" + F(0.05 * m);
                if (holes) yield return "holes:" + F(0.05 * m); yield return "dup:" + F(0.1 * m); yield return "flip:" + F(0.2 * m);
            } else if (part.Length > 0 && part != "clean" && part != "soup") yield return part;
        }
    }

    static int Fixed(string k) { int r = 17; foreach (char c in k) r = r * 31 + c; return r & 0xFFFF; }

    static Vector3d Ball(Random r)
    {
        while (true) {
            var v = new Vector3d(2 * r.NextDouble() - 1, 2 * r.NextDouble() - 1, 2 * r.NextDouble() - 1);
            if (v.LengthSquared <= 1 && v.LengthSquared > 1e-6) return v;
        }
    }

    static void Copy(Soup s, int t, Soup r, bool reverse)
    {
        int i0 = 3 * t, i1 = reverse ? 3 * t + 2 : 3 * t + 1, i2 = reverse ? 3 * t + 1 : 3 * t + 2;
        r.Add(s.V[i0], s.V[i1], s.V[i2], s.Clean[i0], s.Clean[i1], s.Clean[i2], s.PatchOf[t]);
    }

    /// <summary> the patch multipliers of a non-conforming soup: neighbouring patches get different grids </summary>
    public static double Mult(int patch) => new[] { 1.0, 1.37, 0.79, 1.18 }[patch % 4];
}

static class SoupWeld
{
    /// <summary>
    /// vertex weld within tol (the first corner in soup order is the representative; greedy, serial, deterministic), then the
    /// triangles appended in soup order; a triangle that is degenerate after the weld or would make an edge non-manifold is
    /// dropped (triOf = -1). triOf maps soup triangle to mesh triangle; vOf (optional) maps soup corner to mesh vertex.
    /// </summary>
    public static DMesh3 Weld(Soup s, double tol, out int[] triOf) => Weld(s, tol, out triOf, out _);

    public static DMesh3 Weld(Soup s, double tol, out int[] triOf, out int[] vOf)
    {
        var m = new DMesh3();
        var cells = new Dictionary<(long, long, long), List<int>>();
        vOf = new int[s.V.Count];
        double cell = tol > 0 ? tol : 1;
        for (int i = 0; i < s.V.Count; ++i) {
            Vector3d p = s.V[i];
            long cx = (long)Math.Floor(p.x / cell), cy = (long)Math.Floor(p.y / cell), cz = (long)Math.Floor(p.z / cell);
            int found = -1;
            for (long dx = -1; dx <= 1 && found < 0; ++dx)
                for (long dy = -1; dy <= 1 && found < 0; ++dy)
                    for (long dz = -1; dz <= 1 && found < 0; ++dz)
                        if (cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                            foreach (int v in list) if (m.GetVertex(v).Distance(p) <= tol) { found = v; break; }
            if (found < 0) {
                found = m.AppendVertex(p);
                if (!cells.TryGetValue((cx, cy, cz), out var l)) cells[(cx, cy, cz)] = l = new List<int>();
                l.Add(found);
            }
            vOf[i] = found;
        }
        triOf = new int[s.Count];
        for (int t = 0; t < s.Count; ++t) {
            int a = vOf[3 * t], b = vOf[3 * t + 1], c = vOf[3 * t + 2];
            triOf[t] = a == b || b == c || c == a ? -1 : m.AppendTriangle(a, b, c);
            if (triOf[t] < 0) triOf[t] = -1;
        }
        return m;
    }
}
