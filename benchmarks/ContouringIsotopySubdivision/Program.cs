// Spike: a subdivision isotopy certificate (Plantinga and Vegter 2004 / 2007; Burr and Byrd, arXiv 2302.04908) for the
// DualContouring output of an implicit function, against the cell test of Theorem 5 (class S) and the built class P
// certificate. Public API only; inputs are procedural. See README.md and
// docs/research/CONTOURING_ISOTOPY_SUBDIVISION_2026-11.md.
//
//   dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- all > results/all.csv
//   dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- run <case> <N> [seed] [classP]
//   dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- selftest
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class Program
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    sealed class Case
    {
        public string Name; public double K; public int Seed = -1;
        public Field Field; public string Truth;          // null: from the finest certified run of the same case
        public Vector3d Origin = new Vector3d(-1.113, -1.107, -1.103); public double Size = 2.2;
    }

    static Case Make(string name, double k, int N, int seed)
    {
        var c = new Case { Name = name, K = k, Seed = seed };
        double h = c.Size / N, w = k * h;
        switch (name) {
            // --- the feature-width families of benchmarks/ContouringTopology (CONTOURING_TOPOLOGY.md 2.1), k = width in cells
            case "torus": c.Field = Fields.Torus(0.6, w / 2); c.Truth = "c=1;g=1"; break;
            case "gap": c.Field = Fields.Min(Fields.Sphere(new Vector3d(0.45 + w / 2, 0, 0), 0.45), Fields.Sphere(new Vector3d(-0.45 - w / 2, 0, 0), 0.45)); c.Truth = "c=2;g=0+0"; break;
            case "shell": c.Field = Fields.Max(Fields.Sphere(Vector3d.Zero, 0.8), Fields.Neg(Fields.Sphere(Vector3d.Zero, 0.8 - w))); c.Truth = "c=2;g=0+0"; break;
            case "tunnel": c.Field = Fields.Max(Fields.Sphere(Vector3d.Zero, 0.8), Fields.Neg(Fields.Cylinder(w / 2))); c.Truth = "c=1;g=1"; break;
            case "rod": c.Field = Fields.Min(Fields.Min(Fields.Sphere(new Vector3d(0.6, 0, 0), 0.3), Fields.Sphere(new Vector3d(-0.6, 0, 0), 0.3)), Fields.Capsule(0.6, w / 2)); c.Truth = "c=1;g=0"; break;
            // --- smooth references
            case "sphere": c.Field = Fields.Sphere(Vector3d.Zero, 0.7); c.Truth = "c=1;g=0"; break;
            // --- TPMS with a smooth cap, k = level, two periods across the box
            case "gyroid": c.Field = Fields.CappedTpms(Fields.Tpms.Gyroid, 2 * Math.PI * 2 / 2.2, k, 0.9, 4); break;
            case "schwarzp": c.Field = Fields.CappedTpms(Fields.Tpms.SchwarzP, 2 * Math.PI * 2 / 2.2, k, 0.9, 4); break;
            case "diamond": c.Field = Fields.CappedTpms(Fields.Tpms.Diamond, 2 * Math.PI * 2 / 2.2, k, 0.9, 4); break;
            // --- near-singular: Cassini surfaces, k = b / a (1 is the singular one)
            case "cassini": c.Field = Fields.Cassini(0.45, 0.45 * k); c.Truth = k > 1 ? "c=1;g=0" : k < 1 ? "c=2;g=0+0" : "singular"; break;
            // --- degenerate position: a sphere through grid nodes (dyadic grid, f = 0 exactly at six nodes)
            case "nodesphere": c.Origin = new Vector3d(-1.125, -1.125, -1.125); c.Size = 2.25; c.Field = Fields.SpherePoly(0.5625); c.Truth = "c=1;g=0"; break;
            default: throw new ArgumentException(name);
        }
        if (seed >= 0) {
            var rnd = new Random(seed);
            Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisY, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisZ, rnd.NextDouble() * 360);
            c.Field = Fields.Moved(c.Field, R, new Vector3d(0.03 * rnd.NextDouble(), 0.03 * rnd.NextDouble(), 0.03 * rnd.NextDouble()));
        }
        return c;
    }

    static string Sig(DMesh3 mesh)
    {
        var t = MeshTopologySummary.Compute(mesh);
        if (t.Components > 4) return "c=" + t.Components + ";gsum=" + t.ComponentList.Sum(x => x.Genus).ToString(Inv);
        return "c=" + t.Components + ";g=" + string.Join("+", t.ComponentList.Select(x => x.Genus).OrderBy(g => g).Select(g => g.ToString(Inv)));
    }

    sealed class Row
    {
        public string Name; public double K; public int Seed, N;
        public string Truth, DcSig; public bool DcClean;
        public GridCertificate G; public Subdivision Sub;
        public double CertMs, SMs, SubMs, DcMs; public long PvEvaluations;
        public string ClassP = "-", ClassPSig = "-"; public int ClassPUncertified = -1; public double ClassPMs;
    }

    static Row Run(string name, double k, int N, int seed, bool classP, bool subdivide = true, int maxT = 4, int maxTS = 3)
    {
        Case c = Make(name, k, N, seed);
        double h = c.Size / N;
        var row = new Row { Name = name, K = k, Seed = seed, N = N, Truth = c.Truth };

        var sw = Stopwatch.StartNew();
        var pvOnly = new GridCertificate { F = c.Field, Origin = c.Origin, H = h, N = N, MaxT = maxT, DoS = false };
        pvOnly.Run();
        row.CertMs = sw.Elapsed.TotalMilliseconds; row.PvEvaluations = pvOnly.Evaluations;
        sw.Restart();
        var g = new GridCertificate { F = c.Field, Origin = c.Origin, H = h, N = N, MaxT = maxT, MaxTS = maxTS };
        g.Run();
        row.SMs = Math.Max(0, sw.Elapsed.TotalMilliseconds - row.CertMs); row.G = g;

        if (subdivide) {
            sw.Restart();
            var s = new Subdivision { F = c.Field, Origin = c.Origin, H = h, N = N, MaxFiner = 4 };
            s.Run();
            row.SubMs = sw.Elapsed.TotalMilliseconds; row.Sub = s;
        }

        sw.Restart();
        var dc = new DualContouring { Simplify = false, Implicit = c.Field, InsideIsLess = true, IsoValue = 0, CellSize = h, Bounds = new AxisAlignedBox3d(c.Origin, c.Origin + c.Size * Vector3d.One) };
        dc.Generate();
        row.DcMs = sw.Elapsed.TotalMilliseconds;
        row.DcSig = dc.Mesh.TriangleCount == 0 ? "empty" : Sig(dc.Mesh);
        // the mesh side of the certificate (Theorem 2), as far as the public API shows it
        row.DcClean = dc.Mesh.TriangleCount > 0 && dc.Mesh.IsClosed() && dc.SelfIntersectionFree && dc.NonManifoldTriangles == 0;

        if (classP) {
            // the built certificate (class P, blocks and growth) on a fine marching-cubes mesh of the same field, same cell size
            var mc = new MarchingCubes { Implicit = c.Field, IsoValue = 0, CubeSize = c.Size / 160, Bounds = new AxisAlignedBox3d(c.Origin, c.Origin + c.Size * Vector3d.One) };
            mc.Generate();
            DMesh3 m = mc.Mesh;
            if (m.TriangleCount == 0 || !m.IsClosed() || (c.Truth != null && Sig(m) != c.Truth)) row.ClassP = "no-input";
            else {
                sw.Restart();
                var sol = new MeshWindingNumberSolidify(m) { Adaptive = true, CellSize = h, CleanupResult = false, ReduceTolerance = 0, ComputeTopologyCertificate = true };       // blocks and growth (2 rounds) are its defaults
                sol.Compute();
                row.ClassPMs = sw.Elapsed.TotalMilliseconds;
                row.ClassP = sol.TopologyCertified ? "yes" : "no";
                row.ClassPUncertified = sol.UncertifiedCells.Count;
                row.ClassPSig = sol.Result == null || sol.Result.TriangleCount == 0 ? "empty" : Sig(sol.Result);
            }
        }
        return row;
    }

    const string Header = "case,k,seed,N,truth,dc_sig,dc_right,dc_clean,surface_cells,pv_cells,s_cells,both,neither,pv_share,s_share,pv_certified,s_certified," +
        "pv_at_t0,pv_at_t1,pv_at_t2,pv_at_t3,pv_at_t4,pv_refuted,not_smooth_cells,uncertain_node_cells,ambiguous_faces_in_pv,multi_patch_in_pv,s_fail_s0,s_fail_s1,s_fail_s2,s_fail_s3_graph,s_fail_s3_circles," +
        "sub_leaves,sub_c1,sub_unresolved,sub_unresolved_not_smooth,sub_deepest_c1,sub_modal_c1,pv_ms,s_ms,sub_ms,dc_ms,pv_evaluations,class_p,class_p_uncertified,class_p_sig,class_p_ms";

    static string F(double v, string fmt = "0.###") { return v.ToString(fmt, Inv); }

    static string Line(Row r)
    {
        GridCertificate g = r.G; Subdivision s = r.Sub;
        string right = r.Truth == null || r.Truth == "singular" ? "?" : (r.DcSig == r.Truth && r.DcClean ? "yes" : "no");
        int modal = 0;
        if (s != null) for (int d = 0; d < 64; ++d) if (s.C1ByDepth[d] > s.C1ByDepth[modal]) modal = d;
        return string.Join(",", new string[] {
            r.Name, F(r.K, "0.####"), r.Seed.ToString(Inv), r.N.ToString(Inv), r.Truth ?? "?", r.DcSig, right, r.DcClean ? "yes" : "no",
            g.SurfaceCells.ToString(Inv), g.PvCells.ToString(Inv), g.SCells.ToString(Inv), g.BothCells.ToString(Inv), g.NeitherCells.ToString(Inv),
            g.SurfaceCells == 0 ? "-" : F(100.0 * g.PvCells / g.SurfaceCells, "0.00"), g.SurfaceCells == 0 ? "-" : F(100.0 * g.SCells / g.SurfaceCells, "0.00"),
            g.PvCertified ? "yes" : "no", g.SCertified ? "yes" : "no",
            g.PvAtT[0].ToString(Inv), g.PvAtT[1].ToString(Inv), g.PvAtT[2].ToString(Inv), g.PvAtT[3].ToString(Inv), g.PvAtT[4].ToString(Inv), g.PvRefuted.ToString(Inv), g.NotSmoothCells.ToString(Inv), g.UncertainNodeCells.ToString(Inv),
            g.AmbiguousFacesInPv.ToString(Inv), g.MultiPatchInPv.ToString(Inv), g.SFailBy[0].ToString(Inv), g.SFailBy[1].ToString(Inv), g.SFailBy[2].ToString(Inv), g.SFailBy[3].ToString(Inv), g.SFailBy[4].ToString(Inv),
            s == null ? "-" : s.Leaves.ToString(Inv) + (s.Aborted ? "+" : ""), s == null ? "-" : s.ByC1.ToString(Inv), s == null ? "-" : s.Unresolved.ToString(Inv), s == null ? "-" : s.UnresolvedNotSmooth.ToString(Inv),
            s == null || s.ByC1 == 0 ? "-" : s.DeepestC1.ToString(Inv), s == null || s.ByC1 == 0 ? "-" : (modal - 32).ToString(Inv),
            F(r.CertMs, "0"), F(r.SMs, "0"), s == null ? "-" : F(r.SubMs, "0"), F(r.DcMs, "0"), r.PvEvaluations.ToString(Inv),
            r.ClassP, r.ClassPUncertified.ToString(Inv), r.ClassPSig, F(r.ClassPMs, "0") });
    }

    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "selftest") return SelfTest();
        if (args.Length >= 4 && args[0] == "debug") {
            string[] p = args[1].Split(':'); int N = int.Parse(args[2]); int seed = int.Parse(args[3]);
            Case c = Make(p[0], p.Length > 1 ? double.Parse(p[1], Inv) : 0, N, seed);
            double h = c.Size / N;
            var g = new GridCertificate { F = c.Field, Origin = c.Origin, H = h, N = N, MaxT = 4, DoS = false };
            g.Run();
            var rnd = new Random(5); int shown = 0;
            for (int idx = 0; idx < g.Cell.Length && shown < 12; ++idx) {
                if (g.Cell[idx] != 1) continue;
                int i = idx % N, j = (idx / N) % N, k = idx / (N * N);
                double minDot = 2, minF = 1e9, maxF = -1e9;
                var gs = new List<Vector3d>();
                for (int q = 0; q < 400; ++q) {
                    Vector3d pt = c.Origin + h * new Vector3d(i + rnd.NextDouble(), j + rnd.NextDouble(), k + rnd.NextDouble());
                    Enc e = c.Field.At(pt); minF = Math.Min(minF, e.F.Mid); maxF = Math.Max(maxF, e.F.Mid);
                    gs.Add(new Vector3d(e.Gx.Mid, e.Gy.Mid, e.Gz.Mid).Normalized);
                }
                foreach (var a in gs) foreach (var b in gs) minDot = Math.Min(minDot, a.Dot(b));
                Enc box = c.Field.Eval(new Iv(c.Origin.x + h * i, c.Origin.x + h * (i + 1)), new Iv(c.Origin.y + h * j, c.Origin.y + h * (j + 1)), new Iv(c.Origin.z + h * k, c.Origin.z + h * (k + 1)));
                Console.WriteLine(i + " " + j + " " + k + " sampled f in [" + F(minF / h) + ", " + F(maxF / h) + "] h, min cos " + F(minDot) + "  box F [" + F(box.F.Lo / h) + "," + F(box.F.Hi / h) + "] G " + F(box.Gx.Lo) + ".." + F(box.Gx.Hi) + " " + F(box.Gy.Lo) + ".." + F(box.Gy.Hi) + " " + F(box.Gz.Lo) + ".." + F(box.Gz.Hi));
                ++shown;
                if (shown == 1) for (int t = 2; t <= 4; ++t) {
                    int m = 1 << t; double w = h / m; int bad = 0, nz = 0; double worst = 1e9, worstCone = 1e9; Vector3d d = Vector3d.Zero; var encs = new List<Enc>();
                    for (int a = 0; a < m; ++a) for (int b = 0; b < m; ++b) for (int cc = 0; cc < m; ++cc) {
                        Enc e = c.Field.Eval(new Iv(c.Origin.x + w * (i * m + a), c.Origin.x + w * (i * m + a + 1)), new Iv(c.Origin.y + w * (j * m + b), c.Origin.y + w * (j * m + b + 1)), new Iv(c.Origin.z + w * (k * m + cc), c.Origin.z + w * (k * m + cc + 1)));
                        encs.Add(e); d += new Vector3d(e.Gx.Mid, e.Gy.Mid, e.Gz.Mid); if (!e.Smooth) ++bad; if (e.F.HasZero) ++nz;
                    }
                    d = d.Normalized;
                    foreach (Enc e in encs) { Iv dot = d.x * e.Gx + d.y * e.Gy + d.z * e.Gz; Iv g2 = Iv.Sqr(e.Gx) + Iv.Sqr(e.Gy) + Iv.Sqr(e.Gz); worst = Math.Min(worst, dot.Lo); worstCone = Math.Min(worstCone, (Iv.Sqr(dot) - Iv.Of(0.5) * g2).Lo); }
                    Console.WriteLine("  t=" + t + " notsmooth " + bad + " withzero " + nz + " d " + d + " worst dot.Lo " + F(worst) + " worst cone " + F(worstCone));
                }
            }
            return 0;
        }
        if (args.Length >= 3 && args[0] == "run") {
            string[] p = args[1].Split(':');
            Console.WriteLine(Header);
            Console.WriteLine(Line(Run(p[0], p.Length > 1 ? double.Parse(p[1], Inv) : 0, int.Parse(args[2], Inv), args.Length > 3 ? int.Parse(args[3], Inv) : -1, args.Length > 4 && args[4] == "classP", true, args.Length > 5 ? int.Parse(args[5], Inv) : 4, args.Length > 6 ? int.Parse(args[6], Inv) : 3)));
            return 0;
        }
        if (args.Length >= 1 && args[0] == "all") {
            Console.WriteLine(Header);
            var specs = new List<(string name, double k)>();
            foreach (string fam in new[] { "torus", "gap", "shell", "tunnel", "rod" }) foreach (double k in new[] { 0.5, 1, 2, 4 }) if (k < 4 || fam == "torus") specs.Add((fam, k));
            specs.Add(("sphere", 0));
            foreach (string t in new[] { "gyroid", "schwarzp", "diamond" }) specs.Add((t, 0));
            foreach (double level in new[] { 0.6, 1.0, 1.2, 1.35 }) specs.Add(("gyroid", level));
            foreach (double q in new[] { 1.2, 1.05, 1.01, 1.001, 1, 0.999, 0.99, 0.95 }) specs.Add(("cassini", q));
            specs.Add(("nodesphere", 0));
            foreach (var sp in specs) {
                bool tpms = sp.name == "gyroid" || sp.name == "schwarzp" || sp.name == "diamond";
                int[] seeds = tpms || sp.name == "nodesphere" ? new[] { -1 } : new[] { -1, 0 };
                foreach (int seed in seeds) {
                    var rows = new List<Row>();
                    foreach (int N in new[] { 16, 32, 64 }) rows.Add(Run(sp.name, sp.k, N, seed, N <= 32 && !tpms));
                    if (tpms) {
                        // the truth of a capped TPMS: the contour of the finest grid whose certificate passes (128 if none of the three does)
                        Row reference = rows.LastOrDefault(r => r.G.PvCertified && r.DcClean);
                        if (reference == null) { Row fine = Run(sp.name, sp.k, 128, seed, false, false); rows.Add(fine); if (fine.G.PvCertified && fine.DcClean) reference = fine; }
                        if (reference != null) foreach (Row r in rows) r.Truth = reference.DcSig;
                    }
                    foreach (Row r in rows) Console.WriteLine(Line(r));
                    Console.Out.Flush();
                }
            }
            return 0;
        }
        Console.Error.WriteLine("usage: all | run <case[:k]> <N> [seed] [classP] | selftest");
        return 2;
    }

    // Checks of the prototype itself: the enclosures hold at random points, and broken tests are caught (hand mutants).
    static int SelfTest()
    {
        int fails = 0;
        var rnd = new Random(1);
        // 1. sin / cos enclosures against Math.Sin / Math.Cos (which are accurate to far better than 1e-12)
        for (int i = 0; i < 200000; ++i) {
            double x = (rnd.NextDouble() - 0.5) * 60;
            Iv s, c; Iv.SinCosPoint(x, out s, out c);
            if (!(s.Lo - 1e-12 <= Math.Sin(x) && Math.Sin(x) <= s.Hi + 1e-12 && s.Hi - s.Lo < 1e-13)) ++fails;
            if (!(c.Lo - 1e-12 <= Math.Cos(x) && Math.Cos(x) <= c.Hi + 1e-12 && c.Hi - c.Lo < 1e-13)) ++fails;
            double w = rnd.NextDouble() * 3, t = x + w * rnd.NextDouble();
            Iv.SinCos(new Iv(x, x + w), out s, out c);
            if (!(s.Lo <= Math.Sin(t) + 1e-12 && Math.Sin(t) - 1e-12 <= s.Hi && c.Lo <= Math.Cos(t) + 1e-12 && Math.Cos(t) - 1e-12 <= c.Hi)) ++fails;
        }
        Console.WriteLine("sin/cos enclosure failures: " + fails);
        // 2. every field: value and finite-difference gradient at random points of random boxes lie in the enclosure
        int fieldFails = 0, checkedBoxes = 0;
        foreach (string name in new[] { "torus", "gap", "shell", "tunnel", "rod", "sphere", "gyroid", "schwarzp", "diamond", "cassini", "nodesphere" })
            foreach (int seed in new[] { -1, 0 }) {
                Field f = Make(name, name == "cassini" ? 1.05 : name == "gyroid" ? 0.6 : 2, 32, seed).Field;
                for (int i = 0; i < 4000; ++i) {
                    Vector3d a = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
                    double w = Math.Pow(2, -1 - 6 * rnd.NextDouble());
                    Enc e = f.Eval(new Iv(a.x, a.x + w), new Iv(a.y, a.y + w), new Iv(a.z, a.z + w));
                    ++checkedBoxes;
                    for (int j = 0; j < 4; ++j) {
                        Vector3d p = a + w * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                        double v = f.Value(ref p);
                        if (!(e.F.Lo - 1e-9 <= v && v <= e.F.Hi + 1e-9)) ++fieldFails;
                        if (!e.Smooth) continue;
                        const double d = 1e-6;
                        for (int ax = 0; ax < 3; ++ax) {
                            Vector3d q0 = p, q1 = p; q0[ax] -= d; q1[ax] += d;
                            double fd = (f.Value(ref q1) - f.Value(ref q0)) / (2 * d);
                            Iv gi = ax == 0 ? e.Gx : ax == 1 ? e.Gy : e.Gz;
                            double tol = 1e-4 * (1 + Math.Abs(fd));
                            if (!(gi.Lo - tol <= fd && fd <= gi.Hi + tol)) ++fieldFails;
                        }
                    }
                }
            }
        Console.WriteLine("field enclosure failures: " + fieldFails + " in " + checkedBoxes + " boxes");
        return fails + fieldFails == 0 ? 0 : 1;
    }
}
