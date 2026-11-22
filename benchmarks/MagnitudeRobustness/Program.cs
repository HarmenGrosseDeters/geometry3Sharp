using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

namespace MagnitudeRobustness
{
    /// <summary>
    /// Probes of the magnitude study (docs/research/MAGNITUDE_ROBUSTNESS_2026-11.md): what the geodesic and intrinsic classes
    /// and DualContouring return for an input scaled by 2^e, which expression leaves the double range first, and what a
    /// power-of-two prescaling of the input (option a of the note) gives and costs. No library change: the prescaling is a
    /// wrapper in this project.
    /// Stages: roots, sweep, prescale, dc, dcbig, cost, formula. Run: dotnet run -c Release -- [stage ...] (all without arguments).
    /// </summary>
    public static class Program
    {
        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var stages = new Dictionary<string, Action> {
                { "roots", Roots }, { "sweep", () => Sweep(false) }, { "prescale", () => Sweep(true) }, { "dc", DcStage.Run }, { "dcbig", DcStage.Big },
                { "cost", Cost }, { "formula", Formula },
            };
            foreach (string s in args.Length == 0 ? stages.Keys.ToArray() : args) {
                if (!stages.TryGetValue(s, out Action run)) { Console.WriteLine("unknown stage " + s + " (stages: " + string.Join(", ", stages.Keys) + ")"); continue; }
                var w = Stopwatch.StartNew();
                Console.WriteLine("STAGE " + s);
                run();
                Console.WriteLine($"STAGE {s} done in {w.Elapsed.TotalSeconds:F1} s");
            }
        }

        // ---------------------------------------------------------------- comparison of a scaled run with the unit run

        static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b) || (a == 0 && b == 0);

        /// <summary> "exact" (the unit result times 2^e, bit for bit), "status X" (a failure status), "threw X", or "another ..." (a success with another result) </summary>
        public static string Compare(Sig unit, Sig got, int e)
        {
            if (got.Status.StartsWith("threw")) return got.Status;
            if (!got.Ok) return "status " + got.Status;
            if (got.Status != unit.Status) return "another (status " + got.Status + ")";
            if (!got.Ints.SequenceEqual(unit.Ints)) return "another (counts or indices)";
            if (got.Pure.Length != unit.Pure.Length || got.Lengths.Length != unit.Lengths.Length) return "another (sizes)";
            double worst = 0; bool nan = false;
            for (int i = 0; i < got.Pure.Length; ++i)
                if (!SameBits(got.Pure[i], unit.Pure[i])) { if (double.IsNaN(got.Pure[i])) nan = true; else worst = Math.Max(worst, Math.Abs(got.Pure[i] - unit.Pure[i]) / Math.Max(Math.Abs(unit.Pure[i]), 1e-300)); }
            for (int i = 0; i < got.Lengths.Length; ++i) {
                double want = Math.ScaleB(unit.Lengths[i], e);
                if (SameBits(got.Lengths[i], want)) continue;
                if (double.IsNaN(got.Lengths[i]) || double.IsInfinity(got.Lengths[i])) nan = true;
                else worst = Math.Max(worst, Math.Abs(Math.ScaleB(got.Lengths[i], -e) - unit.Lengths[i]) / Math.Max(Math.Abs(unit.Lengths[i]), 1e-300));
            }
            if (nan) return "another (NaN or infinite values)";
            if (worst > 0) return "another (" + Magnitude(worst) + " relative)";
            return "exact";
        }

        // one significant digit, so that neighbouring exponents with nearly the same deviation share a line
        static string Magnitude(double x) => x >= 0.1 ? "above 1e-1" : "1e" + Math.Ceiling(Math.Log10(x)).ToString("F0");

        static Sig Guarded(Func<Sig> run)
        {
            try { return run(); } catch (Exception ex) { return new Sig { Status = "threw " + ex.GetType().Name }; }
        }

        /// <summary> the mesh times 2^e, or null if a coordinate is not represented exactly (overflow, or a subnormal that lost bits) </summary>
        static DMesh3 ScaledExactly(DMesh3 mesh, int e)
        {
            if (mesh == null) return null;
            var c = new DMesh3(mesh);
            foreach (int v in c.VertexIndices()) {
                Vector3d p = c.GetVertex(v), q = new Vector3d(Math.ScaleB(p.x, e), Math.ScaleB(p.y, e), Math.ScaleB(p.z, e));
                for (int k = 0; k < 3; ++k) if (double.IsInfinity(q[k]) || Math.ScaleB(q[k], -e) != p[k]) return null;
                c.SetVertex(v, q);
            }
            return c;
        }

        /// <summary>
        /// Option (a) as a wrapper: scale the input by the power of two that brings its largest coordinate into [1, 2), run, and
        /// scale the lengths of the result back. Both scalings are exact (no rounding) unless a value becomes subnormal.
        /// </summary>
        public static Sig Prescaled(Op op, DMesh3 mesh, double unit)
        {
            double max = 0;
            foreach (int v in mesh.VertexIndices()) { Vector3d p = mesh.GetVertex(v); max = Math.Max(max, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z)))); }
            if (!(max > 0) || double.IsInfinity(max)) return op.Run(mesh, unit);
            int k = Math.ILogB(max);
            var c = new DMesh3(mesh);
            foreach (int v in c.VertexIndices()) { Vector3d p = c.GetVertex(v); c.SetVertex(v, new Vector3d(Math.ScaleB(p.x, -k), Math.ScaleB(p.y, -k), Math.ScaleB(p.z, -k))); }
            Sig g = op.Run(c, Math.ScaleB(unit, -k));
            for (int i = 0; i < g.Lengths.Length; ++i) g.Lengths[i] = Math.ScaleB(g.Lengths[i], k);
            return g;
        }

        public static IEnumerable<int> Exponents()
        {
            for (int e = -1070; e <= 1020; ++e)
                if (e % 10 == 0 || (Math.Abs(e) >= 25 && Math.Abs(e) <= 65) || (Math.Abs(e) >= 225 && Math.Abs(e) <= 300) || (Math.Abs(e) >= 500 && Math.Abs(e) <= 545)) yield return e;
        }

        /// <summary> the classification per tested exponent, as runs of equal answers </summary>
        public static string Runs(IEnumerable<(int e, string answer)> answers)
        {
            var parts = new List<string>();
            int first = 0, lastE = 0; string cur = null;
            foreach (var (e, a) in answers) {
                if (a != cur) { if (cur != null) parts.Add(Range(first, lastE) + ": " + cur); cur = a; first = e; }
                lastE = e;
            }
            if (cur != null) parts.Add(Range(first, lastE) + ": " + cur);
            return string.Join("\n    ", parts);
        }
        static string Range(int a, int b) => a == b ? $"2^{a}" : $"2^{a} .. 2^{b}";

        // ---------------------------------------------------------------- sweep: every operation at 2^e, plain or prescaled

        static void Sweep(bool prescale)
        {
            foreach (Op op in Ops.All()) {
                if (prescale && op.Mesh == null) continue;       // the field case has its own stage (dc)
                Sig unit = Guarded(() => op.Run(op.Mesh, 1));
                var answers = new List<(int, string)>();
                var w = Stopwatch.StartNew();
                foreach (int e in Exponents()) {
                    // (the quad remesher is the slowest: every fifth exponent of the dense bands is enough for it)
                    if (op.Name == "QuadRemesher" && e % 10 != 0 && !(Math.Abs(e) >= 245 && Math.Abs(e) <= 270)) continue;
                    DMesh3 m = op.Mesh == null ? null : ScaledExactly(op.Mesh, e);
                    if (op.Mesh != null && m == null) { answers.Add((e, "(the scaled input is not representable)")); continue; }
                    double s = Math.ScaleB(1.0, e);
                    if (s == 0 || double.IsInfinity(s)) { answers.Add((e, "(the scale is not representable)")); continue; }
                    Sig got = Guarded(() => prescale ? Prescaled(op, m, s) : op.Run(m, s));
                    answers.Add((e, Compare(unit, got, e)));
                }
                Console.WriteLine($"{(prescale ? "PRESCALED" : "PLAIN")} {op.Name} [{op.Issue}] (unit run: {unit.Status}; {w.Elapsed.TotalSeconds:F1} s)\n    {Runs(answers)}");
            }
        }

        // ---------------------------------------------------------------- roots: the expressions that leave the double range

        // the expressions as the library writes them (IntrinsicTriangulation.area_from_lengths, angle_from_lengths, cotan_from_lengths)
        static double KahanProduct(double a, double b, double c)
        {
            if (a < b) { double t = a; a = b; b = t; }
            if (b < c) { double t = b; b = c; c = t; }
            if (a < b) { double t = a; a = b; b = t; }
            return (a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c));
        }
        static double Area(double a, double b, double c) { double p = KahanProduct(a, b, c); return p > 0 ? 0.25 * Math.Sqrt(p) : 0; }
        static double Angle(double a, double b, double c) => Math.Atan2(4 * Area(a, b, c), b * b + c * c - a * a);
        static double Cotan(double a, double b, double c) { double A4 = 4 * Area(a, b, c), x = b * b + c * c - a * a; return A4 <= 0 ? (x >= 0 ? 1e30 : -1e30) : x / A4; }

        static string Kind(double x) => double.IsNaN(x) ? "NaN" : double.IsInfinity(x) ? "infinite" : x == 0 ? "zero" : Math.Abs(x) < 2.2250738585072014e-308 ? "subnormal" : "normal";

        static void Roots()
        {
            DMesh3 sphere = Ops.Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 6);
            double lmin = double.MaxValue, lmax = 0, pmin = double.MaxValue, pmax = 0, cmin = double.MaxValue, cmax = 0;
            foreach (int t in sphere.TriangleIndices()) {
                Index3i i = sphere.GetTriangle(t);
                Vector3d a = sphere.GetVertex(i.a), b = sphere.GetVertex(i.b), c = sphere.GetVertex(i.c);
                double la = (b - a).Length, lb = (c - b).Length, lc = (a - c).Length;
                lmin = Math.Min(lmin, Math.Min(la, Math.Min(lb, lc))); lmax = Math.Max(lmax, Math.Max(la, Math.Max(lb, lc)));
                double p = KahanProduct(la, lb, lc); pmin = Math.Min(pmin, p); pmax = Math.Max(pmax, p);
                Vector3d n = (b - a).Cross(c - a);
                foreach (double sq in new[] { n.x * n.x, n.y * n.y, n.z * n.z }) { if (sq > 0) cmin = Math.Min(cmin, sq); cmax = Math.Max(cmax, sq); }
            }
            Console.WriteLine($"sphere of {sphere.VertexCount} vertices: edge lengths {lmin:G4} .. {lmax:G4} (2^{Math.Log2(lmin):F2} .. 2^{Math.Log2(lmax):F2})");
            Console.WriteLine($"  Kahan product of four lengths (16 area^2): 2^{Math.Log2(pmin):F1} .. 2^{Math.Log2(pmax):F1}");
            Console.WriteLine($"    predicted at scale 2^e: some product subnormal for e <= {Math.Floor((-1022 - Math.Log2(pmin)) / 4)}, every product zero for e <= {Math.Floor((-1075 - Math.Log2(pmax)) / 4)}, some product infinite for e >= {Math.Ceiling((1024 - Math.Log2(pmax)) / 4)}, every product infinite for e >= {Math.Ceiling((1024 - Math.Log2(pmin)) / 4)}");
            Console.WriteLine($"  squared components of the cross product (PolyhedralGeodesicDistance, lines 128 and 192): 2^{Math.Log2(cmin):F1} .. 2^{Math.Log2(cmax):F1}");
            Console.WriteLine($"    predicted: the largest subnormal for e <= {Math.Floor((-1022 - Math.Log2(cmax)) / 4)}, the largest infinite for e >= {Math.Ceiling((1024 - Math.Log2(cmax)) / 4)}");
            // one triangle through the three formulas
            {
                Index3i i = sphere.GetTriangle(0);
                Vector3d a = sphere.GetVertex(i.a), b = sphere.GetVertex(i.b), c = sphere.GetVertex(i.c);
                double la = (b - a).Length, lb = (c - b).Length, lc = (a - c).Length;
                Console.WriteLine($"  triangle 0 (sides {la:G4}, {lb:G4}, {lc:G4}): angle {Angle(la, lb, lc) * 180 / Math.PI:F3} degrees, cotan {Cotan(la, lb, lc):G6}");
                foreach (int e in new[] { -280, -266, -260, -256, -250, 250, 256, 260, 300, 520 }) {
                    double s = Math.ScaleB(1.0, e), A = Area(la * s, lb * s, lc * s);
                    Console.WriteLine($"    2^{e}: product {Kind(KahanProduct(la * s, lb * s, lc * s))}, area {Kind(A)} ({Math.ScaleB(A, -2 * e):G6} x 4^e), angle {Angle(la * s, lb * s, lc * s) * 180 / Math.PI:F3}, cotan {Cotan(la * s, lb * s, lc * s):G6}, b^2 + c^2 - a^2 {Kind(lb * s * lb * s + lc * s * lc * s - la * s * la * s)}");
                }
            }
        }

        // ---------------------------------------------------------------- cost of the prescaling wrapper

        static double Median(Func<double> run, int n = 5) { var t = new List<double>(); for (int i = 0; i < n; ++i) t.Add(run()); t.Sort(); return t[n / 2]; }

        static void Cost()
        {
            // inputs of the size classes of benchmarks/IntrinsicBenchmark, GeodesicOracleBenchmark and QuadRemeshBenchmark (spheres here: closed, any size)
            foreach (int n in new[] { 24, 48, 96 }) {
                DMesh3 mesh = Ops.Sphere(new Vector3d(0.1, 0.2, 0.3), 1, n);
                int far = mesh.MaxVertexID / 2;
                var ops = new List<Op> {
                    new Op { Name = "MeshGeodesicDistance (build + one source)", Run = (m, u) => { var g = new MeshGeodesicDistance(m); g.Compute(new[] { 0 }); return new Sig { Ok = true, Lengths = g.Distance }; } },
                    new Op { Name = "PolyhedralGeodesicDistance", Run = (m, u) => { var g = new PolyhedralGeodesicDistance(m); g.Compute(new[] { 0 }); return new Sig { Ok = true, Lengths = g.Distance }; } },
                    new Op { Name = "MeshGeodesicPath", Run = (m, u) => { var f = new MeshGeodesicPath(m); f.Compute(0, far); return new Sig { Ok = true, Lengths = new[] { f.Length } }; } },
                    new Op { Name = "IntrinsicTriangulation + FlipToDelaunay", Run = (m, u) => { var t = new IntrinsicTriangulation(m); return new Sig { Ok = true, Ints = new[] { t.FlipToDelaunay() } }; } },
                    new Op { Name = "DelaunayRefine(25)", Run = (m, u) => { var t = new IntrinsicTriangulation(m); return new Sig { Ok = true, Ints = new[] { t.DelaunayRefine(25).InsertedVertices } }; } },
                    new Op { Name = "VectorHeatMethod.TransportVector", Run = (m, u) => { Vector2d[] v = new VectorHeatMethod(m).TransportVector(0, new Vector2d(1, 0)); return new Sig { Ok = true, Pure = new[] { v[0].x } }; } },
                };
                if (n <= 48) ops.Add(new Op { Name = "QuadRemesher", Run = (m, u) => { var q = new QuadRemesher(m) { TargetEdgeLength = 0.1 * u }; q.Compute(); return new Sig { Ok = q.Success, Lengths = q.Success ? q.Result.VertexCoordinates.ToArray() : Array.Empty<double>() }; } });
                Console.WriteLine($"sphere, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles (ms, median of 5, after one warm-up run)");
                foreach (Op op in ops) {
                    op.Run(mesh, 1); Prescaled(op, mesh, 1);
                    double plain = Median(() => { var w = Stopwatch.StartNew(); op.Run(mesh, 1); return w.Elapsed.TotalMilliseconds; });
                    double pre = Median(() => { var w = Stopwatch.StartNew(); Prescaled(op, mesh, 1); return w.Elapsed.TotalMilliseconds; });
                    double plain2 = Median(() => { var w = Stopwatch.StartNew(); op.Run(mesh, 1); return w.Elapsed.TotalMilliseconds; });
                    Console.WriteLine($"  {op.Name,-44} plain {plain,9:F2}  prescaled {pre,9:F2}  plain again {plain2,9:F2}  overhead {(pre - Math.Min(plain, plain2)) / Math.Min(plain, plain2) * 100,6:F1} %");
                }
                double copy = Median(() => { var w = Stopwatch.StartNew(); Prescaled(new Op { Run = (m, u) => new Sig() }, mesh, 1); return w.Elapsed.TotalMilliseconds; });
                Console.WriteLine($"  {"the scaled copy alone",-44} {copy,9:F3}");
            }
        }

        // ---------------------------------------------------------------- formula: cost of a power-of-two normalization inside the area formula (option b)

        // the three sides times the power of two that brings the largest into [1, 2): the product of four lengths stays normal
        // unless the triangle itself is degenerate beyond 2^-255 of its longest side
        static double AreaScaled(double a, double b, double c)
        {
            if (a < b) { double t = a; a = b; b = t; }
            if (b < c) { double t = b; b = c; c = t; }
            if (a < b) { double t = a; a = b; b = t; }
            int k = (int)((BitConverter.DoubleToInt64Bits(a) >> 52) & 0x7FF) - 1023;
            if (k < -250 || k > 250) {      // the rare path: only here the scaling is paid
                if (!(a > 0) || double.IsInfinity(a)) return 0;
                k = Math.ILogB(a);
                a = Math.ScaleB(a, -k); b = Math.ScaleB(b, -k); c = Math.ScaleB(c, -k);
                double q = (a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c));
                return q > 0 ? Math.ScaleB(0.25 * Math.Sqrt(q), 2 * k) : 0;
            }
            double p = (a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c));
            return p > 0 ? 0.25 * Math.Sqrt(p) : 0;
        }

        static void Formula()
        {
            var r = new Random(7);
            int n = 1 << 16;
            var a = new double[n]; var b = new double[n]; var c = new double[n];
            for (int i = 0; i < n; ++i) { a[i] = 0.5 + r.NextDouble(); b[i] = 0.5 + r.NextDouble(); c[i] = Math.Abs(a[i] - b[i]) + r.NextDouble() * (a[i] + b[i] - Math.Abs(a[i] - b[i])); }
            // the same bits inside today's range, and the scaled bits outside
            int diff = 0, outside = 0;
            foreach (int e in new[] { -500, -255, -200, -60, 0, 60, 200, 250, 500 })
                for (int i = 0; i < n; ++i) {
                    double s = Math.ScaleB(1.0, e);
                    double old = Area(a[i] * s, b[i] * s, c[i] * s), neu = AreaScaled(a[i] * s, b[i] * s, c[i] * s);
                    if (Math.Abs(e) <= 250) { if (!SameBits(old, neu)) ++diff; }
                    else if (!SameBits(neu, Math.ScaleB(Area(a[i], b[i], c[i]), 2 * e))) ++outside;
                }
            Console.WriteLine($"area with a power-of-two normalization on the rare path: {diff} of {7 * n} results differ from today's formula at 2^-255 .. 2^250; {outside} of {2 * n} are not the scaled unit result at 2^-500 and 2^500");
            double sum = 0;
            double T(Func<double, double, double, double> f) => Median(() => { var w = Stopwatch.StartNew(); for (int rep = 0; rep < 200; ++rep) for (int i = 0; i < n; ++i) sum += f(a[i], b[i], c[i]); return w.Elapsed.TotalMilliseconds * 1e6 / (200.0 * n); }, 7);
            T(Area); T(AreaScaled);
            double t0 = T(Area), t1 = T(AreaScaled), t2 = T(Area), t3 = T(AreaScaled);
            Console.WriteLine($"  ns per call: today's formula {t0:F2} / {t2:F2}, with the exponent test {t1:F2} / {t3:F2}  (checksum {sum:G3})");
        }
    }
}
