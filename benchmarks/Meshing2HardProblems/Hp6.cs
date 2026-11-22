using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using Meshing2Acceptance;

namespace Meshing2HardProblems
{
    /// <summary>
    /// HP 6: what the library's quality mesher and two reference rules from the literature leave below the angle bound on
    /// procedural inputs with small input angles, sorted by how necessary each triangle is.
    /// </summary>
    public static class Hp6
    {
        public sealed class Tally
        {
            public int Vertices, Triangles, Below;
            /// <summary> the small corner is an input vertex and the triangle lies in a wedge of two input segments narrower than the
            /// bound: every triangulation has a corner of at most that angle there </summary>
            public int ApexForced;
            /// <summary> the edge opposite the small corner joins two input segments that meet below 60 degrees, its ends at the
            /// same distance from the apex (MPW's exempt triangles), within a factor 1.5 (the oracle's excuse), or further apart </summary>
            public int AcrossShell, Across15, AcrossFar;
            /// <summary> at the apex of a wedge that is wider than the bound (below 60 degrees) </summary>
            public int ApexUnforced;
            public int Other;
            /// <summary> across triangles whose small angle is below MPW's floor arctan(sin phi / (2 - cos phi)) of their wedge </summary>
            public int BelowFloor;
            public double MinAngleDeg = 180, MinNotForcedDeg = 180, MaxAngleDeg;
            /// <summary> the largest distance of an unforced bad triangle from its nearest small apex, in units of that apex's shortest input segment </summary>
            public double FarthestUnforced;
        }

        static double Deg(double rad) => rad * 180 / Math.PI;

        public static Tally Classify(Pslg g, IReadOnlyList<Vector2d> v, IReadOnlyList<Index3i> tris, double boundDeg)
        {
            var segs = g.Segments(); var wedges = Wedges.Of(g);
            double bound = boundDeg * Math.PI / 180 - 1e-9, extent = g.P.Max(p => Math.Max(Math.Abs(p.x), Math.Abs(p.y)));
            var tally = new Tally { Vertices = v.Count, Triangles = tris.Count };
            double Along(Wedge w, bool second, Vector2d p)
            {
                // the distance of p from the apex along a side of the wedge, or -1 if p is not on that side
                var d = second ? w.Dir2 : w.Dir1; var rel = p - g.P[w.Apex];
                int s = second ? w.Seg2 : w.Seg1;
                double t = rel.Dot(d), off = Math.Abs(rel.x * d.y - rel.y * d.x), len = (g.P[segs[s].a] - g.P[segs[s].b]).Length;
                return off <= 1e-9 * extent && t > 1e-12 * extent && t <= len * (1 + 1e-9) ? t : -1;
            }
            bool InWedge(Wedge w, Vector2d p)
            {
                var rel = p - g.P[w.Apex];
                double c1 = w.Dir1.x * rel.y - w.Dir1.y * rel.x, c2 = rel.x * w.Dir2.y - rel.y * w.Dir2.x, tol = 1e-9 * extent;
                return c1 >= -tol && c2 >= -tol && rel.Dot(w.Dir1) > 0;
            }
            double ApexScale(Wedge w) => segs.Where(s => s.a == w.Apex || s.b == w.Apex).Min(s => (g.P[s.a] - g.P[s.b]).Length);
            foreach (var t in tris) {
                double angle = Hp6Refiner.MinAngle(v[t.a], v[t.b], v[t.c], out int corner);
                tally.MinAngleDeg = Math.Min(tally.MinAngleDeg, Deg(angle));
                double largest = Math.PI - angle - MiddleAngle(v[t.a], v[t.b], v[t.c]);
                tally.MaxAngleDeg = Math.Max(tally.MaxAngleDeg, Deg(largest));
                if (angle >= bound) continue;
                ++tally.Below;
                int m = t[corner]; Vector2d p = v[t[(corner + 1) % 3]], q = v[t[(corner + 2) % 3]];
                var atApex = m < g.P.Count ? wedges.FirstOrDefault(w => w.Apex == m && InWedge(w, p) && InWedge(w, q)) : null;
                if (atApex != null && atApex.Phi < bound) { ++tally.ApexForced; continue; }
                tally.MinNotForcedDeg = Math.Min(tally.MinNotForcedDeg, Deg(angle));
                if (wedges.Count > 0) {
                    var centre = (v[t.a] + v[t.b] + v[t.c]) / 3;
                    tally.FarthestUnforced = Math.Max(tally.FarthestUnforced, wedges.Min(w => (centre - g.P[w.Apex]).Length / ApexScale(w)));
                }
                if (atApex != null) { ++tally.ApexUnforced; continue; }
                bool across = false;
                foreach (var w in wedges) {
                    double rp = Along(w, false, p), rq = Along(w, true, q);
                    if (rp < 0 || rq < 0) { rp = Along(w, true, p); rq = Along(w, false, q); }
                    if (rp < 0 || rq < 0) continue;
                    double ratio = Math.Max(rp, rq) / Math.Min(rp, rq);
                    if (ratio <= 1 + 1e-9) ++tally.AcrossShell; else if (ratio <= 1.5) ++tally.Across15; else ++tally.AcrossFar;
                    if (angle < Math.Atan(Math.Sin(w.Phi) / (2 - Math.Cos(w.Phi))) - 1e-9) ++tally.BelowFloor;
                    across = true; break;
                }
                if (!across) ++tally.Other;
            }
            return tally;
        }

        static double MiddleAngle(Vector2d a, Vector2d b, Vector2d c)
        {
            double A = Ang(a, b, c), B = Ang(b, c, a), C = Ang(c, a, b);
            return A + B + C - Math.Min(A, Math.Min(B, C)) - Math.Max(A, Math.Max(B, C));
        }
        static double Ang(Vector2d at, Vector2d p, Vector2d q) { var u = p - at; var w = q - at; return Math.Atan2(Math.Abs(u.x * w.y - u.y * w.x), u.Dot(w)); }

        const string Header = "family,phi_deg,bound_deg,mesher,status,ms,vertices,triangles,min_deg,max_deg,below,apex_forced,across_shell,across_1.5,across_far,apex_unforced,other,below_mpw_floor,min_not_forced_deg,farthest_unforced";

        static string Row(Pslg g, double bound, string mesher, string status, double ms, Tally t) => string.Join(",", new[] {
            g.Name, g.PhiDeg.ToString(CultureInfo.InvariantCulture), bound.ToString(CultureInfo.InvariantCulture), mesher, status.Replace(',', ';'), ms.ToString("F0", CultureInfo.InvariantCulture),
            t.Vertices.ToString(), t.Triangles.ToString(), t.MinAngleDeg.ToString("F3", CultureInfo.InvariantCulture), t.MaxAngleDeg.ToString("F1", CultureInfo.InvariantCulture), t.Below.ToString(),
            t.ApexForced.ToString(), t.AcrossShell.ToString(), t.Across15.ToString(), t.AcrossFar.ToString(), t.ApexUnforced.ToString(), t.Other.ToString(), t.BelowFloor.ToString(),
            t.Below == t.ApexForced ? "-" : t.MinNotForcedDeg.ToString("F3", CultureInfo.InvariantCulture), t.FarthestUnforced.ToString("F3", CultureInfo.InvariantCulture) });

        /// <summary> the library's mesher through its public interface, judged by the oracle </summary>
        static (string status, double ms, Tally tally) Library(Pslg g, double bound, bool constrained)
        {
            var input = new ConstrainedMesh2Input();
            input.Points.AddRange(g.P);
            input.Rings.Add(new Mesh2Ring(g.Ring.ToArray(), false));
            foreach (var s in g.Interior) input.Segments.Add(new Mesh2Segment(s.a, s.b));
            var mesher = new QualityMesh2 { MinAngle = bound, Conforming = !constrained, SteinerLimit = 400000 };
            var sw = Stopwatch.StartNew();
            var result = mesher.Compute(input);
            sw.Stop();
            if (result.Mesh == null) return (result.Status + " " + result.FailureKind, sw.Elapsed.TotalMilliseconds, new Tally());
            var tally = Classify(g, result.Mesh.Vertices, result.Mesh.Triangles, bound);
            var report = Oracle.Check(g.ToInput(bound, constrained), Hp7.Output(result), new OracleExpect { Milestone = Milestone.M3 });
            string status = (report.Ok ? "oracle ok" : "oracle: " + report.Violations[0].Kind) + (result.Mesh.SteinerLimitReached ? " steiner limit" : "");
            return (status, sw.Elapsed.TotalMilliseconds, tally);
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            var phis = (arg(args, "--phi") ?? "40,20,10,5,1,0.1").Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            var bounds = (arg(args, "--bounds") ?? "20,25").Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            string filter = arg(args, "--filter"), meshers = arg(args, "--meshers") ?? "g3-default,g3-cq,ruppert,mpw,collar";
            int limit = int.Parse(arg(args, "--vertex-limit") ?? "60000", CultureInfo.InvariantCulture);
            Console.WriteLine(Header);
            foreach (var (name, make) in Hp6Inputs.Families) {
                if (filter != null && !name.Contains(filter)) continue;
                foreach (double phi in phis)
                    foreach (double bound in bounds) {
                        var g = make(phi);
                        foreach (string mesher in meshers.Split(',')) {
                            if (mesher.StartsWith("g3")) {
                                var (status, ms, tally) = Library(g, bound, mesher == "g3-cq");
                                Console.WriteLine(Row(g, bound, mesher, status, ms, tally));
                                continue;
                            }
                            var mode = mesher == "ruppert" ? Hp6Refiner.Mode.Ruppert : mesher == "mpw" ? Hp6Refiner.Mode.Mpw : Hp6Refiner.Mode.Collar;
                            var refiner = new Hp6Refiner(g, mode, bound, mode == Hp6Refiner.Mode.Ruppert ? Math.Min(limit, 20000) : limit);
                            var sw = Stopwatch.StartNew();
                            refiner.Run();
                            sw.Stop();
                            string note = refiner.Status + (mode == Hp6Refiner.Mode.Collar ? " (shrinks " + refiner.Shrinks + "; left at a collar " + refiner.LeftAtCollar + ")" : "");
                            Console.WriteLine(Row(g, bound, mesher, note, sw.Elapsed.TotalMilliseconds, Classify(g, refiner.Vertices, refiner.Triangles, bound)));
                        }
                    }
            }
            return 0;
        }
    }
}
