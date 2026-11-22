using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Metamorphic bug hunt (docs/research/METAMORPHIC_BUG_HUNT_2026-11.md): relations and operation chains on procedural inputs,
    /// every Success checked independently (Checks.cs). One line per check: PASS / FAIL / SLOW / INFO, the stage, the case, the detail.
    /// Usage: MetamorphicBugHunt [-v] [stage ...]   (no stage: all; -v also prints the PASS lines)
    /// Stages: boolean chain deep repair wrap contour mesh2d findings (docs/research/METAMORPHIC_BUG_HUNT_2026-11.md), and of the second
    /// hunt (docs/research/METAMORPHIC_BUG_HUNT_2_2026-11.md): io loft contour2 geodesic intrinsic quad wrap2 mesh2d2 tet robust findings2
    /// </summary>
    public static class Program
    {
        public static int Fails, Passes, Slows;
        public const long SlowMs = 20000;

        public static void Report(string stage, string name, string problem, long ms = -1)
        {
            if (ms >= SlowMs) { ++Slows; Console.WriteLine($"SLOW {stage} | {name} | {ms} ms"); }
            if (problem == "") { ++Passes; if (Verbose) Console.WriteLine($"PASS {stage} | {name}"); }
            else { ++Fails; Console.WriteLine($"FAIL {stage} | {name} | {problem}"); }
            Console.Out.Flush();
        }

        public static void Info(string stage, string name, string text) { Console.WriteLine($"INFO {stage} | {name} | {text}"); Console.Out.Flush(); }

        public static bool Verbose;

        public static T Timed<T>(Func<T> f, out long ms)
        {
            var sw = Stopwatch.StartNew();
            T r = f();
            ms = sw.ElapsedMilliseconds;
            return r;
        }

        public static string Rel(string what, double x, double y, double tol)
        {
            double s = Math.Max(Math.Abs(x), Math.Abs(y));
            if (double.IsNaN(x) || double.IsNaN(y)) return $"{what}: NaN";
            return Math.Abs(x - y) <= tol * Math.Max(s, 1e-300) ? "" : $"{what}: {x.ToString("R", CultureInfo.InvariantCulture)} vs {y.ToString("R", CultureInfo.InvariantCulture)} (relative {Math.Abs(x - y) / s:E2}, tolerance {tol:E1})";
        }

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var want = new HashSet<string>(args.Where(a => !a.StartsWith("-")));
            Verbose = args.Contains("-v");
            var stages = new (string, Action)[] {
                ("boolean", BooleanStage.Run),
                ("chain", ChainStage.Run),
                ("deep", DeepChainStage.Run),
                ("repair", RepairStage.Run),
                ("wrap", WrapStage.Run),
                ("contour", ContourStage.Run),
                ("mesh2d", Mesh2Stage.Run),
                ("findings", FindingsStage.Run),
                ("io", IoStage.Run),
                ("loft", LoftStage.Run),
                ("contour2", Contour2Stage.Run),
                ("geodesic", GeodesicStage.Run),
                ("intrinsic", IntrinsicStage.Run),
                ("quad", QuadStage.Run),
                ("wrap2", Wrap2Stage.Run),
                ("mesh2d2", Mesh2OptionsStage.Run),
                ("tet", TetStage.Run),
                ("robust", RobustStage.Run),
                ("findings2", RobustStage.Findings),
            };
            foreach (var (name, run) in stages) {
                if (want.Count > 0 && !want.Contains(name)) continue;
                var sw = Stopwatch.StartNew();
                int pass0 = Passes, fail0 = Fails, slow0 = Slows;
                try { run(); }
                catch (Exception e) { Report(name, "stage", "threw " + e); }
                Console.WriteLine($"STAGE {name} | pass {Passes - pass0}, fail {Fails - fail0}, slow {Slows - slow0} | {sw.ElapsedMilliseconds} ms");
            }
            Console.WriteLine($"TOTAL pass {Passes}, fail {Fails}, slow {Slows}");
            return 0;
        }
    }
}
