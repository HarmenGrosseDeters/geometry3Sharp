using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The size-optimality yardstick of the 2D grading gap (docs/research/GEOGRAM_MEASUREMENTS_2026-11.md, item 2): the integral
    /// I = ∫ 1/lfs(p)^2 dA over the domain, lfs the local feature size of the PSLG (Ruppert 1995: the radius of the smallest disc
    /// centred at p that meets two features, vertices or segments, that do not share a point). Ruppert's and Mitchell's results bound
    /// the triangle count of any mesh with a minimum angle alpha from below by c(alpha) * I and Ruppert's refinement from above by
    /// C(alpha) * I, with constants that are not sharp; so N / I is the yardstick (triangles per unit of I), not a bound in triangles.
    ///
    /// lfs by brute force over the features (the inputs are small); the integral by the centroid rule on the output triangles of
    /// QualityMesh2, each split uniformly into 4^L triangles (the output is graded by lfs already, so the cells are adaptive), at
    /// L = 0 .. --levels to show convergence. Usage: lfs [--cases f-11/a,f-11/b,f-40/angle-33.75] [--levels 6] [--reference 108,205,130]
    /// </summary>
    static class LfsYardstick
    {
        sealed class Pslg
        {
            public double[] X, Y;
            public int[] Unique;
            public List<(int a, int b)> Segments = new List<(int, int)>();
        }

        static Pslg Features(Mesh2Input input)
        {
            var p = new Pslg { X = input.X, Y = input.Y };
            // duplicate input points are one feature: segments refer to the first of equal points
            var first = new Dictionary<(double, double), int>();
            var canon = new int[p.X.Length];
            for (int v = 0; v < p.X.Length; ++v) {
                if (first.ContainsKey((p.X[v], p.Y[v]))) ++DuplicateCount; else first[(p.X[v], p.Y[v])] = v;   // (no TryAdd on net48)
                canon[v] = first[(p.X[v], p.Y[v])];
            }
            p.Unique = first.Values.ToArray();
            var seen = new HashSet<(int, int)>();
            void add(int a, int b)
            {
                a = canon[a]; b = canon[b];
                if (a == b) return;
                var k = a < b ? (a, b) : (b, a);
                if (seen.Add(k)) p.Segments.Add(k);
            }
            foreach (var s in input.Segments) add(s.A, s.B);
            foreach (var r in input.Rings) {
                int[] v = r.Vertices;
                int n = v.Length > 1 && v[0] == v[v.Length - 1] ? v.Length - 1 : v.Length;
                for (int i = 0; i < n; ++i) add(v[i], v[(i + 1) % n]);
            }
            // a vertex on a segment's interior (eg a random point on the outer square) splits it: the PSLG's features are the pieces
            // (otherwise lfs would be 0 at that vertex). Exact for the integer and dyadic inputs here
            for (bool split = true; split;) {
                split = false;
                for (int s = 0; s < p.Segments.Count && !split; ++s) {
                    var (a, b) = p.Segments[s];
                    foreach (int v in p.Unique) {
                        if (v == a || v == b) continue;
                        double cross = (p.X[b] - p.X[a]) * (p.Y[v] - p.Y[a]) - (p.Y[b] - p.Y[a]) * (p.X[v] - p.X[a]);
                        double dot = (p.X[v] - p.X[a]) * (p.X[b] - p.X[a]) + (p.Y[v] - p.Y[a]) * (p.Y[b] - p.Y[a]);
                        double l2 = (p.X[b] - p.X[a]) * (p.X[b] - p.X[a]) + (p.Y[b] - p.Y[a]) * (p.Y[b] - p.Y[a]);
                        if (cross == 0 && dot > 0 && dot < l2) { p.Segments[s] = (a, v); p.Segments.Add((v, b)); split = true; ++SplitCount; break; }
                    }
                }
            }
            return p;
        }

        static int SplitCount, DuplicateCount;

        static double DistSeg(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0;
            double qx = ax + t * dx - px, qy = ay + t * dy - py;
            return Math.Sqrt(qx * qx + qy * qy);
        }

        /// <summary> lfs(p): features sorted by distance; the first one that shares no point with a nearer one gives lfs </summary>
        static double Lfs(Pslg g, double px, double py, List<(double d, int kind, int id)> buffer)
        {
            buffer.Clear();
            foreach (int i in g.Unique) buffer.Add((Math.Sqrt((g.X[i] - px) * (g.X[i] - px) + (g.Y[i] - py) * (g.Y[i] - py)), 0, i));
            for (int s = 0; s < g.Segments.Count; ++s) {
                var (a, b) = g.Segments[s];
                buffer.Add((DistSeg(px, py, g.X[a], g.Y[a], g.X[b], g.Y[b]), 1, s));
            }
            buffer.Sort((u, v) => u.d.CompareTo(v.d));
            bool incident((double d, int kind, int id) f, (double d, int kind, int id) h)
            {
                if (f.kind == 0 && h.kind == 0) return f.id == h.id;
                if (f.kind == 1 && h.kind == 1) {
                    var (a, b) = g.Segments[f.id]; var (c, d) = g.Segments[h.id];
                    return a == c || a == d || b == c || b == d;
                }
                var (v, seg) = f.kind == 0 ? (f.id, h.id) : (h.id, f.id);
                return g.Segments[seg].a == v || g.Segments[seg].b == v;
            }
            for (int k = 1; k < buffer.Count; ++k)
                for (int j = 0; j < k; ++j)
                    if (!incident(buffer[k], buffer[j])) return buffer[k].d;
            return double.PositiveInfinity;
        }

        static double Integrate(Pslg g, Mesh2Output o, int level)
        {
            var buffer = new List<(double, int, int)>();
            double sum = 0;
            foreach (var t in o.Triangles) {
                double ax = o.VX[t[0]], ay = o.VY[t[0]], bx = o.VX[t[1]], by = o.VY[t[1]], cx = o.VX[t[2]], cy = o.VY[t[2]];
                double area = 0.5 * Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay));
                int k = 1 << level;
                double sub = area / (k * k);
                // the k^2 sub-triangles of the uniform split, in barycentric steps of 1/k: upward (i, j), (i+1, j), (i, j+1) and
                // downward (i+1, j), (i+1, j+1), (i, j+1); centroid of each
                for (int i = 0; i < k; ++i)
                    for (int j = 0; i + j < k; ++j) {
                        for (int down = 0; down < 2; ++down) {
                            if (down == 1 && i + j + 1 >= k) continue;
                            double u, v;
                            if (down == 0) { u = (3 * i + 1) / (3.0 * k); v = (3 * j + 1) / (3.0 * k); }
                            else { u = (3 * i + 2) / (3.0 * k); v = (3 * j + 2) / (3.0 * k); }
                            double px = ax + u * (bx - ax) + v * (cx - ax), py = ay + u * (by - ay) + v * (cy - ay);
                            double l = Lfs(g, px, py, buffer);
                            sum += sub / (l * l);
                        }
                    }
            }
            return sum;
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            var ids = (arg(args, "--cases") ?? "f-11/a,f-11/b,f-40/angle-33.75").Split(',');
            int levels = int.Parse(arg(args, "--levels") ?? "6", CultureInfo.InvariantCulture);
            // the reference mesher's triangle counts of docs/design/MESHING2D_GRADING.md (default mode, no constrained quality)
            var reference = (arg(args, "--reference") ?? "108,205,130").Split(',').Select(s => s.Length == 0 ? (double?)null : double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            string F(double x) => x.ToString("G4", CultureInfo.InvariantCulture);
            Console.WriteLine("| case | mode | triangles | I = ∫ 1/lfs² (L = 0 .. " + levels + ") | triangles / I | reference triangles | reference / I |");
            Console.WriteLine("|---|---|---:|---|---:|---:|---:|");
            for (int c = 0; c < ids.Length; ++c) {
                foreach (bool constrained in new[] { false, true }) {
                    var row = Catalogue.Rows().FirstOrDefault(r => r.Case != null && r.Case.Id == ids[c]);
                    if (row == null) { Console.WriteLine("no case " + ids[c]); return 2; }
                    Mesh2Input input = row.Case.Input;
                    input.ConstrainedQuality = constrained;
                    SplitCount = DuplicateCount = 0;
                    Pslg g = Features(input);
                    Mesh2Output o = new QualityMesh2Candidate().Run(input);
                    if (o.Status != Mesh2Status.Success) { Console.WriteLine(ids[c] + ": " + o.Status + " " + o.FailureKind); continue; }
                    var values = Enumerable.Range(0, levels + 1).Select(l => Integrate(g, o, l)).ToList();
                    double I = values[values.Count - 1];
                    double? refN = c < reference.Length && !constrained ? reference[c] : null;
                    Console.WriteLine("| " + ids[c] + " | " + (constrained ? "cq" : "default") + " | " + o.Triangles.Length + " | " + string.Join(", ", values.Select(F)) + " | "
                        + F(o.Triangles.Length / I) + (SplitCount + DuplicateCount > 0 ? " (" + SplitCount + " segment splits, " + DuplicateCount + " duplicates)" : "") + " | " + (refN?.ToString(CultureInfo.InvariantCulture) ?? "") + " | " + (refN != null ? F(refN.Value / I) : "") + " |");
                }
            }
            return 0;
        }
    }
}
