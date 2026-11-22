// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;

namespace FilletDetection
{
    /// <summary>
    /// Spike: how well do fillet detectors find the fillets of procedural CAD solids with a known answer (see README.md).
    ///   crease                  what the dihedral crease test of the quad and tet code sees on the filleted solids
    ///   detect [filters...]     precision / recall / radius error of the detectors (filters: solid names, cad, fine, n=8, fast = without the Voronoi detector)
    ///   defillet [filters...]   sharp-edge reconstruction on the detected fillets against the solid before filleting
    /// </summary>
    public static class Program
    {
        static readonly string[] Names = { "box", "plate", "lprofile", "gear", "rod" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = Inv;
            string cmd = args.Length > 0 ? args[0] : "detect";
            var filters = args.Skip(1).ToArray();
            foreach (string f in filters) if (f.StartsWith("bound=")) RadiusBound = double.Parse(f.Substring(6), Inv);
            if (cmd == "crease") return Crease();
            if (cmd == "detect") return Detect(filters);
            if (cmd == "defillet") return Defillet.Run(filters);
            Console.Error.WriteLine("usage: FilletDetection crease | detect [filters] | defillet [filters]");
            return 2;
        }

        static string F(double v, string fmt = "0.00") { return double.IsNaN(v) ? "-" : v.ToString(fmt, Inv); }

        static int Crease()
        {
            Console.WriteLine("| solid | segments per 90 deg | crease length on fillets (30 deg) | elsewhere | design creases (solid before filleting) | on fillets at 20 deg | at 10 deg |");
            Console.WriteLine("|---|---|---|---|---|---|---|");
            foreach (string name in Names) {
                if (name == "rod") continue;
                foreach (int seg in new[] { 2, 4, 8, 16, 32 }) {
                    Solid s = Solid.Make(name, seg);
                    Baselines.CreaseLengths(s, 30, out double on30, out double off30);
                    Baselines.CreaseLengths(s, 20, out double on20, out double off20);
                    Baselines.CreaseLengths(s, 10, out double on10, out double off10);
                    Console.WriteLine($"| {name} | {seg} | {F(on30)} | {F(off30)} | {F(Baselines.CreaseLength(s.Sharp, 30))} | {F(on20)} | {F(on10)} |");
                }
            }
            return 0;
        }

        /// <summary> the test matrix: every solid as CAD strips with 4 to 32 segments, and uniformly subdivided with 4 to 16 </summary>
        public static IEnumerable<Solid> Cases(string[] filters)
        {
            bool Has(string f) => filters.Contains(f);
            var names = Names.Where(n => filters.Length == 0 || !filters.Any(f => Names.Contains(f)) || Has(n)).ToArray();
            int only = 0;
            foreach (string f in filters) if (f.StartsWith("n=")) only = int.Parse(f.Substring(2), Inv);
            bool cad = Has("cad") || !Has("fine"), fine = Has("fine") || !Has("cad");
            foreach (string name in names) {
                foreach (int seg in new[] { 2, 4, 8, 16, 32 }) {
                    if (only != 0 && seg != only) continue;
                    Solid s = Solid.Make(name, seg);
                    if (cad) {
                        yield return s.With(s.Mesh, " cad n=" + seg);
                        if (seg == 8) yield return s.With(s.Mesh, " cad n=" + seg).Rotated();
                    }
                    if (fine && seg >= 4 && seg <= 16) {
                        // edges at most 1.5 chords of the smallest fillet
                        double chord = 2 * s.Radii.Take(s.Name == "rod" ? 1 : s.Radii.Length).Min() * Math.Sin(Math.PI / (4 * seg));
                        if (s.Name == "gear") chord = 2 * 0.03 * Math.Sin(Math.PI / (4 * seg));
                        Solid fineSolid = s.With(Solid.Refine(s.Mesh, 1.5 * chord), " fine n=" + seg);
                        if (fineSolid.Mesh.TriangleCount > 150000) continue;
                        yield return fineSolid;
                        if (seg == 8) yield return fineSolid.Rotated();
                    }
                }
            }
        }

        /// <summary> the radius bound a user gives, as a multiple of the largest fillet radius (bound=4 on the command line) </summary>
        public static double RadiusBound = 2;

        public static Dictionary<string, Func<Solid, Detection>> Detectors()
        {
            return new Dictionary<string, Func<Solid, Detection>> {
                { "curvature", s => Baselines.Curvature(s.Mesh, RadiusBound * s.MaxFilletRadius) },
                { "curvature (oracle band)", s => Baselines.CurvatureOracle(s) },
                { "DeFillet (paper defaults)", s => new VoronoiDetector().Run(s.Mesh) },
                { "DeFillet (radius bound set)", s => new VoronoiDetector { MaxRadius = RadiusBound * s.MaxFilletRadius * VoronoiDetector.Scale(s.Mesh) }.Run(s.Mesh) },
                { "medial ball", s => new MedialDetector { MaxRadius = RadiusBound * s.MaxFilletRadius }.Run(s.Mesh) },
            };
        }

        public class Score
        {
            public double Precision, Recall, F1, CornerRecall, CylinderFalse, RadiusError;
        }

        /// <summary> area weighted scores against the known answer; the radius error is the area weighted median over the true positives </summary>
        public static Score Evaluate(Solid s, Detection d)
        {
            double tp = 0, fp = 0, fn = 0, corner = 0, cornerHit = 0, cyl = 0, cylHit = 0;
            var errors = new List<(double, double)>(); double errorArea = 0;
            foreach (int tid in s.Mesh.TriangleIndices()) {
                double a = s.Mesh.GetTriArea(tid);
                bool truth = s.IsFillet(tid), hit = d.Fillet[tid];
                if (truth && hit) { tp += a; if (!double.IsNaN(d.Radius[tid])) { errors.Add((Math.Abs(d.Radius[tid] - s.Radius(tid)) / s.Radius(tid), a)); errorArea += a; } }
                else if (truth) fn += a;
                else if (hit) fp += a;
                if (s.Class(tid) == Solid.Corner) { corner += a; if (hit) cornerHit += a; }
                if (s.Class(tid) == Solid.Cylinder) { cyl += a; if (hit) cylHit += a; }
            }
            errors.Sort();
            double median = double.NaN, run = 0;
            foreach (var (e, a) in errors) { run += a; if (run >= 0.5 * errorArea) { median = e; break; } }
            return new Score {
                Precision = tp + fp > 0 ? tp / (tp + fp) : double.NaN, Recall = tp + fn > 0 ? tp / (tp + fn) : double.NaN,
                F1 = tp + fn > 0 ? 2 * tp / (2 * tp + fp + fn) : double.NaN,
                CornerRecall = corner > 0 ? cornerHit / corner : double.NaN, CylinderFalse = cyl > 0 ? cylHit / cyl : double.NaN,
                RadiusError = median
            };
        }

        static int Detect(string[] filters)
        {
            Console.WriteLine("| solid | triangles | detector | precision | recall | F1 | corner recall | cylinder marked | false area / total | radius error (median) | s | note |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            var detectors = Detectors();
            foreach (Solid s in Cases(filters)) {
                foreach (var kv in detectors) {
                    if (filters.Contains("fast") && kv.Key.StartsWith("DeFillet")) continue;
                    Detection d = kv.Value(s);
                    Score sc = Evaluate(s, d);
                    double total = 0, wrong = 0;
                    foreach (int tid in s.Mesh.TriangleIndices()) { double a = s.Mesh.GetTriArea(tid); total += a; if (d.Fillet[tid] && !s.IsFillet(tid)) wrong += a; }
                    Console.WriteLine($"| {s.Name} | {s.Mesh.TriangleCount} | {kv.Key} | {F(sc.Precision)} | {F(sc.Recall)} | {F(sc.F1)} | {F(sc.CornerRecall)} | {F(sc.CylinderFalse)} | {F(wrong / total, "0.000")} | {F(sc.RadiusError, "0.000")} | {F(d.Seconds, "0.0")} | {d.Note} |");
                }
            }
            return 0;
        }
    }
}
