using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using g3;

namespace Meshing2Acceptance
{
    /// <summary>
    /// Refinement on the grid on the corpus (MESHING2D_SNAP.md section 3g, item 6): every corpus input meshed by QualityMesh2 at an
    /// explicit tolerance relative to its extent, at the minimum angles of --angles, with the floor of two pixels and without it.
    /// Counts what refinement on the grid refused: triangles kept because their circumcentre's pixel is taken or crossed by a
    /// constraint (grid-blocked), subsegments with no split point on the grid, and the triangles left below the angle.
    /// </summary>
    static class GridCounts
    {
        sealed class Tally
        {
            public int Inputs, Failed, Timeouts, SteinerLimit, WithBlocked, WithUnsplittable, WithBelow;
            public long Blocked, Unsplittable, Below, Vertices, Triangles;
            public double Ms;
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            string dir = arg(args, "--dir") ?? Corpus.DefaultDir();
            if (!System.IO.Directory.Exists(dir)) {
                Console.WriteLine("no corpus at " + dir + ": run python3 benchmarks/Meshing2Benchmark/corpus/fetch.py first");
                return 2;
            }
            var angles = (arg(args, "--angles") ?? "20,30").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            // the tolerance as a fraction of the input's extent (its larger bounding-box side): a power of two and a decimal one, whose
            // grid doubles do not hold exactly
            var relatives = (arg(args, "--relative") ?? "0.0009765625,0.001").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            var floors = (arg(args, "--floors") ?? "2,0").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            int seconds = int.Parse(arg(args, "--seconds") ?? "60", CultureInfo.InvariantCulture);
            string filter = arg(args, "--filter");
            bool verbose = args.Contains("--verbose");
            var inputs = Corpus.Inputs(dir).Where(i => filter == null || i.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var tallies = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
            var pattern = new Regex(@"snap grid: (\d+) triangles kept .*?, (\d+) subsegments");
            foreach (var (name, input) in inputs) {
                if (input.X.Length < 3) continue;
                double extent = Math.Max(input.X.Max() - input.X.Min(), input.Y.Max() - input.Y.Min());
                if (!(extent > 0)) continue;
                foreach (double rel in relatives)
                    foreach (double angle in angles)
                        foreach (double floor in floors) {
                            string key = "rel " + rel.ToString("R", CultureInfo.InvariantCulture) + ", " + angle.ToString(CultureInfo.InvariantCulture) + " deg, floor " + floor.ToString(CultureInfo.InvariantCulture);
                            if (!tallies.TryGetValue(key, out var t)) tallies[key] = t = new Tally();
                            ++t.Inputs;
                            var mesher = new QualityMesh2 { MinAngle = angle, SnapTolerance = extent * rel, Conforming = true, SteinerLimit = 20 * input.X.Length + 20000 };
                            ConstrainedTriangulation2Result result = null; Exception error = null;
                            var sw = Stopwatch.StartNew();
                            var thread = new System.Threading.Thread(() => {
                                QualityMesh2.Refiner.FloorPixelsForBenchmarks = floor;
                                try { result = mesher.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input)); } catch (Exception e) { error = e; }
                            }, 256 << 20);
                            thread.IsBackground = true;
                            thread.Start();
                            if (!thread.Join(seconds * 1000)) { ++t.Timeouts; if (verbose) Console.WriteLine(name + " | " + key + ": TIMEOUT"); continue; }
                            t.Ms += sw.Elapsed.TotalMilliseconds;
                            if (error != null || result.Mesh == null) {
                                ++t.Failed;
                                if (verbose) Console.WriteLine(name + " | " + key + ": " + (error != null ? error.GetType().Name + " " + error.Message : result.Status + " " + result.FailureKind));
                                continue;
                            }
                            long blocked = 0, unsplittable = 0;
                            foreach (var d in result.Diagnostics) {
                                var m = pattern.Match(d);
                                if (m.Success) { blocked = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); unsplittable = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); }
                            }
                            int below = result.Mesh.BadTriangles.Count;
                            t.Blocked += blocked; t.Unsplittable += unsplittable; t.Below += below;
                            if (blocked > 0) ++t.WithBlocked;
                            if (unsplittable > 0) ++t.WithUnsplittable;
                            if (below > 0) ++t.WithBelow;
                            if (result.Mesh.SteinerLimitReached) ++t.SteinerLimit;
                            t.Vertices += result.Mesh.Vertices.Count; t.Triangles += result.Mesh.Triangles.Count;
                            if (verbose) Console.WriteLine(name + " | " + key + ": " + result.Mesh.Vertices.Count + " vertices, blocked " + blocked + ", unsplittable " + unsplittable + ", below " + below + (result.Mesh.SteinerLimitReached ? ", Steiner limit" : ""));
                        }
            }
            Console.WriteLine("| Tolerance / extent, angle, floor (pixels) | Inputs | Grid-blocked (inputs) | Unsplittable (inputs) | Below the angle (inputs) | Steiner limit | Failed | Timeouts | Vertices | Time |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var pair in tallies) {
                var t = pair.Value;
                Console.WriteLine("| " + pair.Key + " | " + t.Inputs + " | " + t.Blocked + " (" + t.WithBlocked + ") | " + t.Unsplittable + " (" + t.WithUnsplittable + ") | " + t.Below + " (" + t.WithBelow + ") | "
                    + t.SteinerLimit + " | " + t.Failed + " | " + t.Timeouts + " | " + t.Vertices + " | " + (t.Ms / 1000).ToString("F1", CultureInfo.InvariantCulture) + " s |");
            }
            return 0;
        }
    }
}
