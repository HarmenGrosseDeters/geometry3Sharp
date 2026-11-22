using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Meshing2Acceptance;

namespace Meshing2HardProblems
{
    /// <summary> HP 7: the oracle's time by check against the mesher's and against the prototype checker of a certifying design. </summary>
    public static class Hp7
    {
        /// <summary> the mesher's result as the oracle reads it, from the library's public interface only </summary>
        public static Mesh2Output Output(ConstrainedTriangulation2Result result)
        {
            var output = new Mesh2Output();
            if (result.Status == Triangulation2Status.Failed) { output.Status = Mesh2Status.Failed; output.FailureKind = result.FailureKind.ToString(); return output; }
            var mesh = result.Mesh;
            output.Status = result.Status == Triangulation2Status.Empty ? Mesh2Status.Empty : Mesh2Status.Success;
            output.VX = mesh.Vertices.Select(p => p.x).ToArray();
            output.VY = mesh.Vertices.Select(p => p.y).ToArray();
            output.InputToVertex = mesh.InputToVertex.ToArray();
            output.Triangles = mesh.Triangles.Select(t => new[] { t.a, t.b, t.c }).ToArray();
            output.Neighbours = mesh.Neighbours.Select(t => new[] { t.a, t.b, t.c }).ToArray();
            output.Subsegments = mesh.Subsegments.Select(s => new SubsegmentOutput(s.A, s.B, s.Marker)).ToList();
            output.TriangleLabels = mesh.TriangleLabels.ToArray();
            output.BadTriangles = mesh.BadTriangles.ToArray();
            output.SteinerLimitReached = mesh.SteinerLimitReached;
            return output;
        }

        public static ConstrainedMesh2Input Convert(Mesh2Input input)
        {
            var result = new ConstrainedMesh2Input();
            for (int i = 0; i < input.Count; ++i) result.Points.Add(new Vector2d(input.X[i], input.Y[i]));
            foreach (var s in input.Segments) result.Segments.Add(new Mesh2Segment(s.A, s.B, s.Marker, s.Locked));
            foreach (var r in input.Rings) result.Rings.Add(new Mesh2Ring(r.Vertices, r.Hole, r.Marker));
            return result;
        }

        public static ConstrainedTriangulation2Result Mesh(Mesh2Input input) =>
            new QualityMesh2 { MinAngle = input.MinAngleDeg ?? 0, Conforming = !input.ConstrainedQuality, SnapTolerance = input.SnapTolerance, SteinerLimit = 3000000 }.Compute(Convert(input));

        // ---- inputs ----

        /// <summary> side x side square rings that share their borders, inner corners jittered (the generator of Meshing2Benchmark's perf --quality) </summary>
        public static Mesh2Input Parcels(int side, double angle, bool constrained)
        {
            var rnd = new Random(7);
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i <= side; ++i)
                for (int j = 0; j <= side; ++j) {
                    bool edge = i == 0 || j == 0 || i == side || j == side;
                    xs.Add(i + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5)); ys.Add(j + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5));
                }
            var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), MinAngleDeg = angle, ConstrainedQuality = constrained };
            for (int i = 0; i < side; ++i)
                for (int j = 0; j < side; ++j) { int a = i * (side + 1) + j; input.Rings.Add(new RingInput(new[] { a, a + side + 1, a + side + 2, a + 1 }, false)); }
            return input;
        }

        /// <summary> one ring of n points around the origin with a random radius: a coastline </summary>
        public static Mesh2Input Coast(int n, double angle, bool constrained)
        {
            var rnd = new Random(11);
            var input = new Mesh2Input { X = new double[n], Y = new double[n], MinAngleDeg = angle, ConstrainedQuality = constrained };
            for (int i = 0; i < n; ++i) { double r = 1 + 0.35 * rnd.NextDouble(), a = 2 * Math.PI * i / n; input.X[i] = r * Math.Cos(a); input.Y[i] = r * Math.Sin(a); }
            input.Rings.Add(new RingInput(Enumerable.Range(0, n).ToArray(), false));
            return input;
        }

        /// <summary> a square with a hole whose corner is off the outer corner by 2^-k in both coordinates times very different
        /// powers of two: coordinates at very different binary exponents, the class of #947 </summary>
        public static Mesh2Input WideExponents(double angle, bool constrained)
        {
            var g = new Pslg { Name = "wide" };
            g.Ring.Add(g.Add(0, 0)); g.Ring.Add(g.Add(1, 0)); g.Ring.Add(g.Add(1, 1)); g.Ring.Add(g.Add(0, 1));
            var rnd = new Random(5);
            int centre = g.Add(0.5 + 3e-250, 0.5 + 7e-262);
            for (int i = 0; i < 12; ++i) {
                double a = 2 * Math.PI * i / 12 + 0.1, r = 0.2 + 0.2 * rnd.NextDouble();
                g.Interior.Add((centre, g.Add(0.5 + r * Math.Cos(a) * (i % 3 == 0 ? 1e-200 : 1), 0.5 + r * Math.Sin(a) * (i % 3 == 0 ? 1e-200 : 1))));
            }
            return g.ToInput(angle, constrained);
        }

        static IEnumerable<(string name, Mesh2Input input)> Cases(string size)
        {
            bool large = size == "large";
            foreach (bool cq in new[] { false, true }) {
                string mode = cq ? "cq" : "default";
                foreach (int side in large ? new[] { 30, 60, 120 } : new[] { 10, 30 }) {
                    yield return ("parcels" + side + " q20 " + mode, Parcels(side, 20, cq));
                    yield return ("parcels" + side + " q30 " + mode, Parcels(side, 30, cq));
                }
                foreach (int n in large ? new[] { 2000, 20000 } : new[] { 2000 }) yield return ("coast" + n + " q20 " + mode, Coast(n, 20, cq));
                yield return ("comb40 phi1 q20 " + mode, Hp6Inputs.Comb(1, 40).ToInput(20, cq));
                yield return ("fan12 phi2 q20 " + mode, Hp6Inputs.Fan(2, 12).ToInput(20, cq));
            }
            { var snapped = Parcels(large ? 60 : 30, 20, false); snapped.SnapTolerance = 1.0 / 64; yield return ("parcels" + (large ? 60 : 30) + " q20 snap 1/64", snapped); }
            yield return ("wide exponents q20 default", WideExponents(20, false));
        }

        static double Tube(Mesh2Input input)
        {
            double extent = Math.Max(input.X.Max() - input.X.Min(), input.Y.Max() - input.Y.Min());
            return Math.Max(input.SnapTolerance * 2, extent * Math.Pow(2, -40));   // the mesher's automatic tolerance is 2^-41 of the extent
        }

        static T Quiet<T>(Func<T> f, out string err)
        {
            var old = Console.Error; var writer = new StringWriter();
            Console.SetError(writer);
            try { return f(); } finally { Console.SetError(old); err = writer.ToString(); }
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            string size = arg(args, "--size") ?? "small", filter = arg(args, "--filter");
            Console.WriteLine("runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", " + Environment.ProcessorCount + " cores");
            Console.WriteLine("case,triangles,mesher_ms,oracle_ms,oracle/mesher,checker_ms,checker/mesher,oracle_verdict,checker_verdict,exact_orient,exact_incircle,below_angle,conforming_subsegment_hits");
            var detail = new List<string>();
            foreach (var (name, input) in Cases(size)) {
                if (filter != null && !name.Contains(filter)) continue;
                ConstrainedTriangulation2Result result = null;
                var times = new List<double>();
                for (int r = 0; r < 3; ++r) { var sw = Stopwatch.StartNew(); result = Mesh(input); sw.Stop(); times.Add(sw.Elapsed.TotalMilliseconds); }
                times.Sort();
                double mesherMs = times[1];
                if (result.Mesh == null) { Console.WriteLine(name + ",0," + mesherMs.ToString("F0", CultureInfo.InvariantCulture) + ",,,,," + result.Status + " " + result.FailureKind + ",,,,,"); continue; }
                var output = Output(result);
                var expect = new OracleExpect { Milestone = Milestone.M3 };
                // the oracle, fast path, twice (the second run is timed), with its own laps
                Oracle.Check(input, output, expect);
                Environment.SetEnvironmentVariable("G3_ORACLE_TIMING", "1");
                var osw = Stopwatch.StartNew();
                var report = Quiet(() => Oracle.Check(input, output, expect), out string laps);
                osw.Stop();
                Environment.SetEnvironmentVariable("G3_ORACLE_TIMING", null);
                // the checker: the certificate derived outside the timing, the best of five
                double tube = Tube(input);
                var cert = Hp7Checker.Derive(input, output, tube);
                Hp7Checker best = null; double bestMs = double.MaxValue; bool accepted = false;
                for (int r = 0; r < 5; ++r) {
                    var checker = new Hp7Checker(input, output, cert, tube, !input.ConstrainedQuality);
                    var sw = Stopwatch.StartNew(); accepted = checker.Run(); sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < bestMs) { bestMs = sw.Elapsed.TotalMilliseconds; best = checker; }
                }
                string F(double v) => v.ToString(v < 10 ? "F2" : "F0", CultureInfo.InvariantCulture);
                Console.WriteLine(string.Join(",", name, output.Triangles.Length, F(mesherMs), F(osw.Elapsed.TotalMilliseconds), F(osw.Elapsed.TotalMilliseconds / mesherMs),
                    F(bestMs), (bestMs / mesherMs).ToString("F3", CultureInfo.InvariantCulture), report.Ok ? "ok" : report.Violations[0].Kind.ToString(), accepted ? "accepted" : "REJECTED: " + best.Reject.Replace(',', ';'),
                    best.ExactOrient, best.ExactInCircle, best.BelowBound, best.ConformingSubsegmentHits));
                var oracleLaps = laps.Split('\n').Where(l => l.StartsWith("ORACLE ")).Select(l => l.Substring(7).Trim()).Select(l => { int cut = l.LastIndexOf(' ', l.Length - 4); return (l.Substring(0, cut), double.Parse(l.Substring(cut + 1, l.Length - cut - 4), CultureInfo.InvariantCulture)); }).ToList();
                double total = oracleLaps.Sum(l => l.Item2);
                detail.Add(name + " | oracle: " + string.Join("; ", oracleLaps.OrderByDescending(l => l.Item2).Take(6).Select(l => l.Item1 + " " + F(l.Item2) + " ms (" + (100 * l.Item2 / total).ToString("F0", CultureInfo.InvariantCulture) + " %)"))
                    + " | checker: " + string.Join("; ", best.Laps.Select(l => l.step + " " + F(l.ms) + " ms")));
            }
            Console.WriteLine();
            Console.WriteLine("the oracle's six largest checks and the checker's passes, per case:");
            foreach (string line in detail) Console.WriteLine(line);
            return 0;
        }

        // ---- fault injection: every corrupted mesh or certificate must be rejected ----

        static Mesh2Output Copy(Mesh2Output o) => new Mesh2Output {
            Status = o.Status, VX = (double[])o.VX.Clone(), VY = (double[])o.VY.Clone(), Triangles = o.Triangles.Select(t => (int[])t.Clone()).ToArray(),
            InputToVertex = (int[])o.InputToVertex.Clone(), Subsegments = o.Subsegments.Select(s => new SubsegmentOutput(s.A, s.B, s.Marker)).ToList(), BadTriangles = (int[])o.BadTriangles.Clone() };

        public static int SelfCheck()
        {
            int failed = 0;
            void Expect(string name, bool wantAccept, Mesh2Input input, Mesh2Output o, Certificate cert, bool conforming, string mustMention = null)
            {
                var checker = new Hp7Checker(input, o, cert, Tube(input), conforming);
                bool accepted = checker.Run();
                bool pass = accepted == wantAccept && (mustMention == null || (checker.Reject ?? "").Contains(mustMention));
                Console.WriteLine((pass ? "pass  " : "FAIL  ") + name + ": " + (accepted ? "accepted" : "rejected (" + checker.Reject + ")"));
                if (!pass) ++failed;
            }
            foreach (bool cq in new[] { false, true })
            foreach (bool fan in new[] { false, true }) {
                var input = fan ? Hp6Inputs.Fan(10).ToInput(25, cq) : Parcels(6, 33, cq);
                var good = Output(Mesh(input));
                var cert = Hp7Checker.Derive(input, good, Tube(input));
                string tag = (fan ? " (fan, " : " (parcels, ") + (cq ? "cq)" : "default)");
                Expect("the mesher's result" + tag, true, input, good, cert, !cq);
                var sub = new HashSet<long>(good.Subsegments.Select(s => Key(s.A, s.B)));
                var edgeTris = new Dictionary<long, List<int>>();
                for (int i = 0; i < good.Triangles.Length; ++i) for (int k = 0; k < 3; ++k) { long key = Key(good.Triangles[i][k], good.Triangles[i][(k + 1) % 3]); if (!edgeTris.TryGetValue(key, out var l)) edgeTris[key] = l = new List<int>(); l.Add(i); }
                int interior = Enumerable.Range(0, good.Triangles.Length).First(i => Enumerable.Range(0, 3).All(k => edgeTris[Key(good.Triangles[i][k], good.Triangles[i][(k + 1) % 3])].Count == 2));
                int atBoundary = Enumerable.Range(0, good.Triangles.Length).First(i => Enumerable.Range(0, 3).Any(k => edgeTris[Key(good.Triangles[i][k], good.Triangles[i][(k + 1) % 3])].Count == 1));

                { var m = Copy(good); var t = m.Triangles[interior]; (t[0], t[1]) = (t[1], t[0]); Expect("a triangle reversed" + tag, false, input, m, cert, !cq, "not positive"); }
                { var m = Copy(good); m.Triangles = m.Triangles.Where((t, i) => i != interior).ToArray(); m.BadTriangles = new int[0]; Expect("an interior triangle missing" + tag, false, input, m, cert, !cq); }
                { var m = Copy(good); m.Triangles = m.Triangles.Where((t, i) => i != atBoundary).ToArray(); m.BadTriangles = new int[0]; Expect("a boundary triangle missing" + tag, false, input, m, cert, !cq); }
                { var m = Copy(good); m.Triangles = m.Triangles.Concat(new[] { (int[])m.Triangles[interior].Clone() }).ToArray(); Expect("a triangle twice" + tag, false, input, m, cert, !cq, "two triangles"); }
                {
                    // the hole of a missing triangle declared as boundary: its edges added as subsegments of the nearest ring edges
                    var m = Copy(good); var t = m.Triangles[interior];
                    m.Triangles = m.Triangles.Where((x, i) => i != interior).ToArray(); m.BadTriangles = new int[0];
                    var sources = cert.SubsegmentSources.ToList();
                    for (int k = 0; k < 3; ++k) if (!sub.Contains(Key(t[k], t[(k + 1) % 3]))) { m.Subsegments.Add(new SubsegmentOutput(t[k], t[(k + 1) % 3])); sources.Add(new[] { input.Segments.Count }); }
                    Expect("a missing triangle declared a hole in the certificate" + tag, false, input, m, new Certificate { SubsegmentSources = sources.ToArray() }, !cq);
                }
                {
                    // an edge flip that keeps both triangles positive but breaks the empty circle
                    var m = Copy(good); bool done = false;
                    foreach (var kv in edgeTris) {
                        if (kv.Value.Count != 2 || sub.Contains(kv.Key)) continue;
                        int a = (int)(kv.Key >> 32), b = (int)(uint)kv.Key; int[] t1 = m.Triangles[kv.Value[0]], t2 = m.Triangles[kv.Value[1]];
                        int c = t1.First(v => v != a && v != b), d = t2.First(v => v != a && v != b);
                        double O(int p, int q, int r) => (m.VX[q] - m.VX[p]) * (m.VY[r] - m.VY[p]) - (m.VY[q] - m.VY[p]) * (m.VX[r] - m.VX[p]);
                        // t1 = (a, b, c) up to rotation or (b, a, c): orient so that a-b-c is positive
                        if (O(a, b, c) < 0) (a, b) = (b, a);
                        if (O(c, d, b) <= 1e-6 || O(d, c, a) <= 1e-6) continue;
                        m.Triangles[kv.Value[0]] = new[] { c, d, b }; m.Triangles[kv.Value[1]] = new[] { d, c, a }; done = true; break;
                    }
                    m.BadTriangles = Enumerable.Range(0, m.Triangles.Length).ToArray();
                    if (!done) { Console.WriteLine("FAIL  no flippable edge found" + tag); ++failed; }
                    else Expect("an edge flipped" + tag, false, input, m, cert, !cq, "locally Delaunay");
                }
                { var m = Copy(good); int steiner = m.VX.Length - 1; m.VX[steiner] += 50; Expect("a vertex moved far away" + tag, false, input, m, cert, !cq); }
                { var c2 = new Certificate { SubsegmentSources = cert.SubsegmentSources.Select(z => (int[])z.Clone()).ToArray() }; int i = Array.FindIndex(c2.SubsegmentSources, z => z.Length > 0); c2.SubsegmentSources[i][0] = (c2.SubsegmentSources[i][0] + 3) % (input.Segments.Count + input.Rings.Sum(r => r.Vertices.Length)); Expect("a subsegment named for another source" + tag, false, input, good, c2, !cq); }
                { var c2 = new Certificate { SubsegmentSources = cert.SubsegmentSources.Select(z => (int[])z.Clone()).ToArray() }; int i = Array.FindIndex(c2.SubsegmentSources, z => z.Length > 0); c2.SubsegmentSources[i] = new int[0]; Expect("a subsegment with its sources left out" + tag, false, input, good, c2, !cq); }
                { var m = Copy(good); m.Subsegments.RemoveAt(0); Expect("a subsegment missing" + tag, false, input, m, new Certificate { SubsegmentSources = cert.SubsegmentSources.Skip(1).ToArray() }, !cq); }
                if (good.BadTriangles.Length > 0) { var m = Copy(good); m.BadTriangles = m.BadTriangles.Skip(1).ToArray(); Expect("a triangle below the angle not listed" + tag, false, input, m, cert, !cq, "not listed"); }
                else if (fan) { Console.WriteLine("FAIL  the fan has no triangle below the angle" + tag); ++failed; }
            }
            {
                // a double cover of a square annulus: every triangle positive, every interior edge in two triangles, every boundary edge
                // on a ring edge; only the chains (one chain per source, from end to end) and the winding argument reject it
                var input = new Mesh2Input { X = new double[] { -2, 2, 2, -2, -1, 1, 1, -1 }, Y = new double[] { -2, -2, 2, 2, -1, -1, 1, 1 } };
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
                Mesh2Output Cover(int sheets)
                {
                    var o = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[8 * sheets], VY = new double[8 * sheets], InputToVertex = Enumerable.Range(0, 8).ToArray(), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
                    for (int s = 0; s < sheets; ++s) for (int i = 0; i < 8; ++i) { o.VX[8 * s + i] = input.X[i]; o.VY[8 * s + i] = input.Y[i]; }
                    var tris = new List<int[]>(); int steps = 4 * sheets;
                    for (int i = 0; i < steps; ++i) {
                        int j = (i + 1) % steps, outer0 = 8 * (i / 4) + i % 4, outer1 = 8 * (j / 4) + j % 4, inner0 = outer0 + 4, inner1 = outer1 + 4;
                        tris.Add(new[] { outer0, outer1, inner1 }); tris.Add(new[] { outer0, inner1, inner0 });
                        o.Subsegments.Add(new SubsegmentOutput(outer0, outer1)); o.Subsegments.Add(new SubsegmentOutput(inner0, inner1));
                    }
                    o.Triangles = tris.ToArray();
                    return o;
                }
                var single = Cover(1); var twice = Cover(2);
                Expect("an annulus, one sheet", true, input, single, Hp7Checker.Derive(input, single, Tube(input)), false);
                Expect("an annulus covered twice", false, input, twice, Hp7Checker.Derive(input, twice, Tube(input)), false, "chain");
            }
            Console.WriteLine(failed == 0 ? "checker self-check: all passed" : "checker self-check: " + failed + " FAILED");
            return failed == 0 ? 0 : 1;
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }
}
