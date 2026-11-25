// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;

// --orient3d-stagea <box.json>: the bound of Orient3d's stage A expression over a box (tools/FPTaylorPilot/compare_orient3d.py
// writes the box, {"names": [...], "lo": [...], "hi": [...]} with round-trip doubles, and reads the JSON printed here)
if (args.Length == 2 && args[0] == "--orient3d-stagea") {
    var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(args[1])).RootElement;
    var names = doc.GetProperty("names").EnumerateArray().Select(e => e.GetString()).ToList();
    var los = doc.GetProperty("lo").EnumerateArray().Select(e => double.Parse(e.GetString(), System.Globalization.CultureInfo.InvariantCulture)).ToList();
    var his = doc.GetProperty("hi").EnumerateArray().Select(e => double.Parse(e.GetString(), System.Globalization.CultureInfo.InvariantCulture)).ToList();
    var dag = PredicateModels.BuildOrient3dStageA();
    var box = new Interval64[dag.InputIds.Count];
    for (int i = 0; i < box.Length; ++i) {
        int k = names.IndexOf(dag.Nodes[dag.InputIds[i]].Name);
        box[i] = new Interval64(los[k], his[k]);
    }
    var res = new Dictionary<string, object>();
    foreach (bool sign in new[] { true, false }) {
        var r = BoxSymbolicTaylor.Bound(dag, box, maxBoxes: 256, relTol: 0.005, signFilter: sign);
        res[sign ? "sign_filter" : "with_final_rounding"] = new Dictionary<string, string> {
            ["refined_bound"] = r.RefinedBound.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            ["first_order"] = r.FirstOrderBound.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            ["second_order"] = r.SecondOrderBound.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            ["sampled_lower"] = r.SampledLowerBound.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            ["boxes"] = r.BoxesEvaluated.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(res));
    return;
}

Console.WriteLine("=== Part 1: Offline Bounded-Box Symbolic Taylor vs OCaml FPTaylorPilot & PredicateCompiler ===");
Console.WriteLine("expr | ops | deg | compilerBound | ocamlFPTaylor | csSymTaylor(1box) | csSymTaylor(bnb) | sampledLower | ratio(bnb/comp) | bnb/ocaml | boxes | ms");

static Interval64[] MakeBoxForDag(ExprDag dag, Func<string, Interval64> rangeFor)
{
    var box = new Interval64[dag.InputIds.Count];
    for (int i = 0; i < dag.InputIds.Count; ++i)
        box[i] = rangeFor(dag.Nodes[dag.InputIds[i]].Name);
    return box;
}

static Interval64 LpiRange(string name) =>
    name.StartsWith("r.") ? new Interval64(0.0, 0.5) : new Interval64(1.0, 2.0);

static Interval64 TpiRange(string name) => new Interval64(1.0, 2.0);

// Exact reference generator bounds & OCaml FPTaylor bounds from tools/FPTaylorPilot/README.md.
var boxBenchmarks = new (ExprDag Dag, Func<string, Interval64> Range, double MagOverDomain, double? OcamlFPTaylor, int MaxBoxes)[] {
    (PredicateModels.BuildLpiPrx(), LpiRange, 2.0, 1.1102230246251565e-16, 64),
    (PredicateModels.BuildLpiNy(), LpiRange, 8.0, 1.779821241033454e-15, 256),
    (PredicateModels.BuildLpiNz(), LpiRange, 8.0, 1.779821241033454e-15, 256),
    (PredicateModels.BuildLpiD(), LpiRange, 96.0, 2.512295922526476e-14, 1024),
    (PredicateModels.BuildLpiNx(), LpiRange, 96.0, 3.099124653780570e-14, 1024),
    (PredicateModels.BuildOrient3dLpiDet(), LpiRange, 1152.0, null, 1024),
    (PredicateModels.BuildTpiU1x(), TpiRange, 1.0, 5.551115123125783e-17, 64),
    (PredicateModels.BuildTpiN1x(), TpiRange, 2.0, 4.458239333260394e-16, 256),
    (PredicateModels.BuildTpiD1(), TpiRange, 6.0, 2.109423746787797e-15, 512),
    (PredicateModels.BuildOrient3dTpiD(), TpiRange, 144.0, null, 1024),
    (PredicateModels.BuildOrient3dTpiDet(), TpiRange, 2592.0, null, 1024),
};

double lpiDBoxBound = 0;
foreach (var b in boxBenchmarks) {
    var box = MakeBoxForDag(b.Dag, b.Range);
    double compBound = b.Dag.ForwardC[b.Dag.Root] * b.MagOverDomain;
    var res = BoxSymbolicTaylor.Bound(b.Dag, box, maxBoxes: b.MaxBoxes, relTol: 0.005, signFilter: false);
    if (b.Dag.Name == "LPI d") lpiDBoxBound = res.RefinedBound;
    int ops = b.Dag.Nodes.Count - b.Dag.InputIds.Count;
    string ocamlStr = b.OcamlFPTaylor.HasValue ? $"{b.OcamlFPTaylor.Value:E3}" : "-";
    string vsOcaml = b.OcamlFPTaylor.HasValue ? $"{res.RefinedBound / b.OcamlFPTaylor.Value:F3}x" : "-";
    Console.WriteLine(
        $"{b.Dag.Name} | {ops} | {b.Dag.Degree[b.Dag.Root]} | {compBound:E3} | {ocamlStr} | " +
        $"{res.SingleBoxBound:E3} | {res.RefinedBound:E3} | {res.SampledLowerBound:E3} | {res.RefinedBound / compBound:F3} | {vsOcaml} | " +
        $"{res.BoxesEvaluated} | {res.ElapsedMs:F1}");
}

// Empirical verification of LPI d box bound (2.359e-14 < OCaml FPTaylor's 2.512e-14) over 50,000 random & corner points.
{
    var dag = PredicateModels.BuildLpiD();
    var box = MakeBoxForDag(dag, LpiRange);
    var rng = new Random(999);
    var vBuf = new double[dag.Nodes.Count];
    var mBuf = new double[dag.Nodes.Count];
    double maxObsErr = 0.0;
    for (int s = 0; s < 50000; ++s) {
        var x = new double[box.Length];
        for (int i = 0; i < box.Length; ++i) {
            if (s < 25000) {
                // Perturbed corners (worst-case rounding near box boundaries)
                double corner = rng.Next(2) == 0 ? box[i].Lo : box[i].Hi;
                int ulps = rng.Next(-4, 5);
                if (corner == box[i].Lo && ulps < 0) ulps = -ulps;
                if (corner == box[i].Hi && ulps > 0) ulps = -ulps;
                x[i] = corner == 0 ? (ulps > 0 ? Math.ScaleB(ulps, -55) : 0.0) : Math.Clamp(corner + ulps * Math.ScaleB(1.0, -52), box[i].Lo, box[i].Hi);
            } else {
                x[i] = box[i].Lo + rng.NextDouble() * (box[i].Hi - box[i].Lo);
            }
        }
        double fl = dag.EvalStageAPermanent(x, vBuf, mBuf).Val;
        ExactBin ex = dag.EvalExact(x);
        double err = (ExactBin.FromDouble(fl) - ex).ToDoubleAbs();
        if (err > maxObsErr) maxObsErr = err;
    }
    Console.WriteLine($"LPI d 50k box check: maxObservedErr={maxObsErr:E3}, csSymTaylorBound={lpiDBoxBound:E3}, ratio={maxObsErr / lpiDBoxBound:F4}");
    if (maxObsErr > lpiDBoxBound)
        throw new InvalidOperationException("LPI d box bound exceeded!");
}

Console.WriteLine();
Console.WriteLine("=== Part 2: Adversarial Near-Degenerate Workloads (Fallback Reduction & Soundness vs ExactBin) ===");
Console.WriteLine("workload | N | stageA_undec | iv64_undec | symT_undec | symT_rescued_of_A | medRatio(symT/A) | medRatio(symT/iv64) | maxErr/symT | wrongSigns");

static void RunWorkload(
    string label,
    ExprDag dag,
    int samples,
    Func<Random, double[]> gen,
    Func<double[], double> customStageABound = null)
{
    var rng = new Random(20261124);
    int nNodes = dag.Nodes.Count;
    var vBuf = new double[nNodes];
    var mBuf = new double[nNodes];
    var vBar = new double[nNodes];
    var mBar = new double[nNodes];

    int stageAUndec = 0, ivUndec = 0, symTUndec = 0, symTRescued = 0, wrongSigns = 0;
    double maxErrOverSymT = 0.0;
    var ratiosVsA = new List<double>(samples);
    var ratiosVsIv = new List<double>(samples);

    for (int s = 0; s < samples; ++s) {
        double[] x = gen(rng);
        ExactBin exact = dag.EvalExact(x);
        int exactSign = exact.Sign;

        var (val, boundA, signA) = dag.EvalStageAPermanent(x, vBuf, mBuf);
        if (customStageABound != null) {
            boundA = customStageABound(x);
            signA = val > boundA ? 1 : (val < -boundA ? -1 : 2);
        }
        var iv = dag.EvalInterval(x);
        int signIv = iv.Sign();
        var (boundST, _, _, signST) = dag.EvalRuntimeSymbolicTaylor(vBuf, mBuf, vBar, mBar, signFilter: true);

        if (signA != 2 && signA != exactSign) ++wrongSigns;
        if (signIv != 2 && signIv != exactSign) ++wrongSigns;
        if (signST != 2 && signST != exactSign) ++wrongSigns;

        if (signA == 2) ++stageAUndec;
        if (signIv == 2) ++ivUndec;
        if (signST == 2) ++symTUndec;
        if (signA == 2 && signST != 2) ++symTRescued;

        if (boundA > 0 && double.IsFinite(boundST))
            ratiosVsA.Add(boundST / boundA);
        if (iv.Radius > 0 && double.IsFinite(boundST))
            ratiosVsIv.Add(boundST / iv.Radius);

        ExactBin flExact = ExactBin.FromDouble(val);
        double trueErr = (flExact - exact).ToDoubleAbs();
        double encBound = Interval64.Up(boundST + ExprDag.U * Math.Abs(val));
        if (encBound > 0 && double.IsFinite(encBound))
            maxErrOverSymT = Math.Max(maxErrOverSymT, trueErr / encBound);
    }

    ratiosVsA.Sort();
    ratiosVsIv.Sort();
    double medA = ratiosVsA.Count > 0 ? ratiosVsA[ratiosVsA.Count / 2] : double.NaN;
    double medIv = ratiosVsIv.Count > 0 ? ratiosVsIv[ratiosVsIv.Count / 2] : double.NaN;
    string rescuedPct = stageAUndec > 0 ? $"{100.0 * symTRescued / stageAUndec:F1}%" : "-";
    Console.WriteLine(
        $"{label} | {samples} | {stageAUndec} ({100.0 * stageAUndec / samples:F1}%) | " +
        $"{ivUndec} ({100.0 * ivUndec / samples:F1}%) | {symTUndec} ({100.0 * symTUndec / samples:F1}%) | " +
        $"{symTRescued}/{stageAUndec} ({rescuedPct}) | {medA:F4} | {medIv:F4} | {maxErrOverSymT:F4} | {wrongSigns}");
    if (wrongSigns != 0)
        throw new InvalidOperationException($"{label}: SOUNDNESS VIOLATION ({wrongSigns} wrong signs)!");
}

static double RandSigned(Random rng) => 2.0 * rng.NextDouble() - 1.0;

static double[] Pack(ExprDag dag, Dictionary<string, double> map)
{
    var x = new double[dag.InputIds.Count];
    for (int i = 0; i < x.Length; ++i)
        x[i] = map[dag.Nodes[dag.InputIds[i]].Name];
    return x;
}

static void SetPt3(Dictionary<string, double> m, string p, double x, double y, double z)
{
    m[p + ".x"] = x; m[p + ".y"] = y; m[p + ".z"] = z;
}

// 1. LPI d: short segment (len ~ 1e-3) far from triangle vertex r (~ 10), nearly parallel to triangle plane.
var dagLpiD = PredicateModels.BuildLpiD();
RunWorkload("LPI d (short seg far from r)", dagLpiD, 5000, rng => {
    var m = new Dictionary<string, double>();
    SetPt3(m, "r", -10.0 + RandSigned(rng), -10.0 + RandSigned(rng), 0.1 * RandSigned(rng));
    SetPt3(m, "s", 10.0 + RandSigned(rng), -10.0 + RandSigned(rng), 0.1 * RandSigned(rng));
    SetPt3(m, "t", 0.0 + RandSigned(rng), 10.0 + RandSigned(rng), 0.1 * RandSigned(rng));
    double px = RandSigned(rng), py = RandSigned(rng), pz = 0.5 + 0.5 * RandSigned(rng);
    double eps = Math.Pow(10.0, -14.0 - 2.5 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    double dx = 1e-3 * RandSigned(rng), dy = 1e-3 * RandSigned(rng);
    double ux = m["s.x"] - m["r.x"], uy = m["s.y"] - m["r.y"], uz = m["s.z"] - m["r.z"];
    double wx = m["t.x"] - m["r.x"], wy = m["t.y"] - m["r.y"], wz = m["t.z"] - m["r.z"];
    double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
    double dz = -(nx * dx + ny * dy) / nz + eps * 10.0;
    SetPt3(m, "p", px, py, pz);
    SetPt3(m, "q", px + dx, py + dy, pz + dz);
    return Pack(dagLpiD, m);
});

// 2. Orient3dLPI det: short segment far from r & c, piercing plane near test plane (a,b,c).
var dagLpiDet = PredicateModels.BuildOrient3dLpiDet();
RunWorkload("Orient3dLPI det (short seg)", dagLpiDet, 5000, rng => {
    var m = new Dictionary<string, double>();
    SetPt3(m, "r", -8.0 + RandSigned(rng), -8.0 + RandSigned(rng), 0.2 * RandSigned(rng));
    SetPt3(m, "s", 8.0 + RandSigned(rng), -8.0 + RandSigned(rng), 0.2 * RandSigned(rng));
    SetPt3(m, "t", 0.0 + RandSigned(rng), 8.0 + RandSigned(rng), 0.2 * RandSigned(rng));
    double ix = RandSigned(rng), iy = RandSigned(rng);
    double ux = m["s.x"] - m["r.x"], uy = m["s.y"] - m["r.y"], uz = m["s.z"] - m["r.z"];
    double wx = m["t.x"] - m["r.x"], wy = m["t.y"] - m["r.y"], wz = m["t.z"] - m["r.z"];
    double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
    double iz = m["r.z"] - (nx * (ix - m["r.x"]) + ny * (iy - m["r.y"])) / nz;
    double sx = 1e-2 * RandSigned(rng), sy = 1e-2 * RandSigned(rng), sz = 1e-2 * (0.5 + rng.NextDouble());
    SetPt3(m, "p", ix - sx, iy - sy, iz - sz);
    SetPt3(m, "q", ix + sx, iy + sy, iz + sz);
    double pert = Math.Pow(10.0, -13.0 - 3.0 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    SetPt3(m, "a", ix + 5.0 + RandSigned(rng), iy - 5.0 + RandSigned(rng), iz + pert);
    SetPt3(m, "b", ix - 5.0 + RandSigned(rng), iy + 1.0 + RandSigned(rng), iz + pert);
    SetPt3(m, "c", ix + 4.0 + RandSigned(rng), iy + 6.0 + RandSigned(rng), iz + pert);
    return Pack(dagLpiDet, m);
});

// 3. Orient3dLPI det: unit-scale general near-coplanar intersection.
RunWorkload("Orient3dLPI det (unit near-plane)", dagLpiDet, 5000, rng => {
    var m = new Dictionary<string, double>();
    SetPt3(m, "r", RandSigned(rng), RandSigned(rng), 0.1 * RandSigned(rng));
    SetPt3(m, "s", 1.0 + 0.2 * RandSigned(rng), RandSigned(rng), 0.1 * RandSigned(rng));
    SetPt3(m, "t", RandSigned(rng), 1.0 + 0.2 * RandSigned(rng), 0.1 * RandSigned(rng));
    double ux = m["s.x"] - m["r.x"], uy = m["s.y"] - m["r.y"], uz = m["s.z"] - m["r.z"];
    double wx = m["t.x"] - m["r.x"], wy = m["t.y"] - m["r.y"], wz = m["t.z"] - m["r.z"];
    double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
    double ix = 0.3 * RandSigned(rng), iy = 0.3 * RandSigned(rng);
    double iz = m["r.z"] - (nx * (ix - m["r.x"]) + ny * (iy - m["r.y"])) / nz;
    double dx = 0.4 * RandSigned(rng), dy = 0.4 * RandSigned(rng), dz = 0.8 + 0.4 * rng.NextDouble();
    SetPt3(m, "p", ix - dx, iy - dy, iz - dz);
    SetPt3(m, "q", ix + dx, iy + dy, iz + dz);
    double pert = Math.Pow(10.0, -13.0 - 3.0 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    SetPt3(m, "a", 1.0 + 0.2 * RandSigned(rng), -0.5 + 0.2 * RandSigned(rng), iz + pert);
    SetPt3(m, "b", -1.0 + 0.2 * RandSigned(rng), 0.5 + 0.2 * RandSigned(rng), iz + pert);
    SetPt3(m, "c", 0.2 * RandSigned(rng), 1.2 + 0.2 * RandSigned(rng), iz + pert);
    return Pack(dagLpiDet, m);
});

// 4. Orient3dTPI det: three planes meeting at (ix,iy,iz) with test plane (a,b,c) perturbed by 1e-13..1e-16!
var dagTpiDet = PredicateModels.BuildOrient3dTpiDet();
RunWorkload("Orient3dTPI det (near-coplanar)", dagTpiDet, 5000, rng => {
    var m = new Dictionary<string, double>();
    double ix = 0.2 * RandSigned(rng), iy = 0.2 * RandSigned(rng), iz = 0.2 * RandSigned(rng);
    // Construct 3 planes passing EXACTLY through (ix,iy,iz) with small perturbation in test plane z = iz + pert.
    // Plane 1: z = iz (horizontal, exact); Plane 2: x = ix + 0.1*(z - iz); Plane 3: y = iy + 0.1*(x - ix).
    void AddExactPlane(string k, Vector3d u, Vector3d v)
    {
        double s0 = 1.5 * RandSigned(rng), t0 = 1.5 * RandSigned(rng);
        Vector3d a0 = new Vector3d(ix, iy, iz) + s0 * u + t0 * v;
        Vector3d b0 = a0 + (0.8 + 0.4 * rng.NextDouble()) * u;
        Vector3d c0 = a0 + (0.8 + 0.4 * rng.NextDouble()) * v;
        SetPt3(m, "a" + k, a0.x, a0.y, a0.z);
        SetPt3(m, "b" + k, b0.x, b0.y, b0.z);
        SetPt3(m, "c" + k, c0.x, c0.y, c0.z);
    }
    AddExactPlane("1", new Vector3d(1, 0.2, 0.3).Normalized, new Vector3d(-0.2, 1, 0.25).Normalized);
    AddExactPlane("2", new Vector3d(0.25, 0.15, 1).Normalized, new Vector3d(0.1, 1, -0.2).Normalized);
    AddExactPlane("3", new Vector3d(1, -0.2, 0.15).Normalized, new Vector3d(0.15, 0.25, 1).Normalized);
    double pert = Math.Pow(10.0, -12.5 - 3.5 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    Vector3d tU = new Vector3d(1, 0.3, -0.2).Normalized;
    Vector3d tV = new Vector3d(-0.25, 1, 0.35).Normalized;
    Vector3d tN = tU.Cross(tV).Normalized;
    Vector3d basePt = new Vector3d(ix, iy, iz) + pert * tN;
    Vector3d pa = basePt + (1.0 + 0.3 * RandSigned(rng)) * tU + (-0.5 + 0.3 * RandSigned(rng)) * tV;
    Vector3d pb = basePt + (-1.0 + 0.3 * RandSigned(rng)) * tU + (0.6 + 0.3 * RandSigned(rng)) * tV;
    Vector3d pc = basePt + (0.2 + 0.3 * RandSigned(rng)) * tU + (1.2 + 0.3 * RandSigned(rng)) * tV;
    SetPt3(m, "a", pa.x, pa.y, pa.z);
    SetPt3(m, "b", pb.x, pb.y, pb.z);
    SetPt3(m, "c", pc.x, pc.y, pc.z);
    return Pack(dagTpiDet, m);
});

// 5. Orient3d: near-coplanar points (perturbation 1e-13.5..1e-16.5).
var dagO3d = PredicateModels.BuildOrient3d();
RunWorkload("Orient3d (near-coplanar)", dagO3d, 5000, rng => {
    var m = new Dictionary<string, double>();
    double ax = RandSigned(rng), ay = RandSigned(rng), az = RandSigned(rng);
    double bx = RandSigned(rng), by = RandSigned(rng), bz = RandSigned(rng);
    double cx = RandSigned(rng), cy = RandSigned(rng), cz = RandSigned(rng);
    double u = 0.2 + 0.6 * rng.NextDouble(), v = 0.2 + 0.6 * rng.NextDouble();
    double pert = Math.Pow(10.0, -13.5 - 3.0 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    double dx = ax + u * (bx - ax) + v * (cx - ax);
    double dy = ay + u * (by - ay) + v * (cy - ay);
    double dz = az + u * (bz - az) + v * (cz - az) + pert;
    SetPt3(m, "a", ax, ay, az); SetPt3(m, "b", bx, by, bz);
    SetPt3(m, "c", cx, cy, cz); SetPt3(m, "d", dx, dy, dz);
    return Pack(dagO3d, m);
});

// 6. InCircle & InSphere: near-cocircular / near-cospherical.
var dagIcc = PredicateModels.BuildInCircle();
RunWorkload("InCircle (near-cocircular)", dagIcc, 5000, rng => {
    var m = new Dictionary<string, double>();
    double cx = 5.0 * RandSigned(rng), cy = 5.0 * RandSigned(rng);
    double R = 1.0 + rng.NextDouble();
    string[] pts = { "a", "b", "c", "d" };
    for (int i = 0; i < 4; ++i) {
        double ang = 2.0 * Math.PI * (i * 0.25 + 0.1 * RandSigned(rng));
        double r = i == 3 ? R * (1.0 + Math.Pow(10.0, -13.0 - 3.0 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1)) : R;
        m[pts[i] + ".x"] = cx + r * Math.Cos(ang);
        m[pts[i] + ".y"] = cy + r * Math.Sin(ang);
    }
    return Pack(dagIcc, m);
});

var dagIsp = PredicateModels.BuildInSphere();
RunWorkload("InSphere (near-cospherical)", dagIsp, 5000, rng => {
    var m = new Dictionary<string, double>();
    double cx = 3.0 * RandSigned(rng), cy = 3.0 * RandSigned(rng), cz = 3.0 * RandSigned(rng);
    double R = 1.0 + rng.NextDouble();
    string[] pts = { "a", "b", "c", "d", "e" };
    for (int i = 0; i < 5; ++i) {
        double z = RandSigned(rng);
        double phi = 2.0 * Math.PI * rng.NextDouble();
        double s = Math.Sqrt(Math.Max(0, 1.0 - z * z));
        double r = i == 4 ? R * (1.0 + Math.Pow(10.0, -13.0 - 3.0 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1)) : R;
        SetPt3(m, pts[i], cx + r * s * Math.Cos(phi), cy + r * s * Math.Sin(phi), cz + r * z);
    }
    return Pack(dagIsp, m);
});

// 7. SN-5 CrossesInteriorFilter: shipped 1e-14 bound (30x margin) vs Interval64 vs Runtime Symbolic Taylor.
var dagSn5 = PredicateModels.BuildSnapCrossesInterior();
RunWorkload("SN-5 CrossesInterior (shipped 1e-14)", dagSn5, 5000, rng => {
    var m = new Dictionary<string, double>();
    double W = Math.Pow(2.0, -rng.Next(2, 10));
    long kx = rng.Next(-1000, 1000), ky = rng.Next(-1000, 1000);
    double kxh = kx + (rng.Next(2) == 0 ? -0.5 : 0.5);
    double kyh = ky + (rng.Next(2) == 0 ? -0.5 : 0.5);
    double cx = kxh * W, cy = kyh * W;
    double vx = RandSigned(rng), vy = RandSigned(rng);
    double pert = Math.Pow(10.0, -13.0 - 3.0 * rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    double ax = cx - 2.0 * vx + pert, ay = cy - 2.0 * vy;
    double bx = cx + 2.0 * vx + pert, by = cy + 2.0 * vy;
    m["a.x"] = ax; m["a.y"] = ay; m["b.x"] = bx; m["b.y"] = by;
    m["kx_half"] = kxh; m["ky_half"] = kyh; m["W"] = W;
    return Pack(dagSn5, m);
}, x => {
    double ax = x[0], ay = x[1], bx = x[2], by = x[3], kxh = x[4], kyh = x[5], w = x[6];
    double cx = kxh * w, cy = kyh * w;
    double dx = bx - ax, dy = by - ay, ex = cx - ax, ey = cy - ay;
    return 1e-14 * (Math.Abs(dx) * (Math.Abs(ey) + Math.Abs(cy)) + Math.Abs(dy) * (Math.Abs(ex) + Math.Abs(cx)));
});

Console.WriteLine();
Console.WriteLine("=== Part 3: Runtime Latency Comparison (ns/call, 50000 calls after JIT warmup) ===");
Console.WriteLine("predicate | ops | stageA_ns | iv64_ns | symTaylor_ns | exactBin_ns | symT/stageA | exact/symT");

static void BenchLatency(string name, ExprDag dag, int iters)
{
    var rng = new Random(77);
    int nIn = dag.InputIds.Count;
    var inputs = new double[256][];
    for (int i = 0; i < inputs.Length; ++i) {
        inputs[i] = new double[nIn];
        for (int j = 0; j < nIn; ++j) inputs[i][j] = 1.0 + rng.NextDouble();
    }
    int nNodes = dag.Nodes.Count;
    var vBuf = new double[nNodes];
    var mBuf = new double[nNodes];
    var vBar = new double[nNodes];
    var mBar = new double[nNodes];

    long sink = 0;
    for (int i = 0; i < 5000; ++i) {
        var x = inputs[i & 255];
        sink += dag.EvalStageAPermanent(x, vBuf, mBuf).Sign;
        sink += dag.EvalInterval(x).Sign();
        sink += dag.EvalRuntimeSymbolicTaylor(vBuf, mBuf, vBar, mBar).Sign;
    }
    for (int i = 0; i < 500; ++i)
        sink += dag.EvalExact(inputs[i & 255]).Sign;

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < iters; ++i)
        sink += dag.EvalStageAPermanent(inputs[i & 255], vBuf, mBuf).Sign;
    double nsA = sw.Elapsed.TotalNanoseconds / iters;

    sw.Restart();
    for (int i = 0; i < iters; ++i)
        sink += dag.EvalInterval(inputs[i & 255]).Sign();
    double nsIv = sw.Elapsed.TotalNanoseconds / iters;

    sw.Restart();
    for (int i = 0; i < iters; ++i) {
        dag.EvalStageAPermanent(inputs[i & 255], vBuf, mBuf);
        sink += dag.EvalRuntimeSymbolicTaylor(vBuf, mBuf, vBar, mBar).Sign;
    }
    double nsST = sw.Elapsed.TotalNanoseconds / iters;

    int exactIters = Math.Min(iters, 2000);
    sw.Restart();
    for (int i = 0; i < exactIters; ++i)
        sink += dag.EvalExact(inputs[i & 255]).Sign;
    double nsEx = sw.Elapsed.TotalNanoseconds / exactIters;

    int ops = nNodes - nIn;
    Console.WriteLine($"{name} | {ops} | {nsA:F0} | {nsIv:F0} | {nsST:F0} | {nsEx:F0} | {nsST / nsA:F2}x | {nsEx / nsST:F1}x");
    if (sink == long.MinValue) Console.WriteLine(sink);
}

BenchLatency("Orient3d", dagO3d, 50000);
BenchLatency("InCircle", dagIcc, 50000);
BenchLatency("InSphere", dagIsp, 50000);
BenchLatency("Orient3dLPI det", dagLpiDet, 50000);
BenchLatency("Orient3dTPI det", dagTpiDet, 50000);

// Part 3b: Production IndirectPredicates.Orient3dLPI (reflection) on near-degenerate Stage-A-undecided inputs
// to compare our Stage-1.5 filter cost directly against geometry3Sharp's real FixedArith fallback!
Console.WriteLine();
Console.WriteLine("=== Part 3b: Production IndirectPredicates.Orient3dLPI (double entry) fallback cost ===");
var indirectType = typeof(ExactPredicates).Assembly.GetType("g3.IndirectPredicates");
var lpiMethod = indirectType?.GetMethod("Orient3dLPI", BindingFlags.Static | BindingFlags.NonPublic,
    null, new[] { typeof(Vector3d), typeof(Vector3d), typeof(Vector3d), typeof(Vector3d), typeof(Vector3d), typeof(Vector3d), typeof(Vector3d), typeof(Vector3d) }, null);
if (lpiMethod != null) {
    var lpiDel = (Func<Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, int>)
        Delegate.CreateDelegate(typeof(Func<Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, Vector3d, int>), lpiMethod);

    // Collect 256 short-segment inputs where Stage A is UNDECIDED (forces FixedArith fallback) vs 256 easy inputs.
    var rng = new Random(42);
    var easy = new Vector3d[256][];
    var fallback = new Vector3d[256][];
    int eCount = 0, fCount = 0;
    var vBuf = new double[dagLpiDet.Nodes.Count];
    var mBuf = new double[dagLpiDet.Nodes.Count];
    while (eCount < 256 || fCount < 256) {
        var r = new Vector3d(-8 + RandSigned(rng), -8 + RandSigned(rng), 0.2 * RandSigned(rng));
        var s = new Vector3d(8 + RandSigned(rng), -8 + RandSigned(rng), 0.2 * RandSigned(rng));
        var t = new Vector3d(RandSigned(rng), 8 + RandSigned(rng), 0.2 * RandSigned(rng));
        double ix = RandSigned(rng), iy = RandSigned(rng);
        Vector3d n = (s - r).Cross(t - r);
        double iz = r.z - (n.x * (ix - r.x) + n.y * (iy - r.y)) / n.z;
        var delta = new Vector3d(1e-2 * RandSigned(rng), 1e-2 * RandSigned(rng), 1e-2 * (0.5 + rng.NextDouble()));
        var p = new Vector3d(ix, iy, iz) - delta;
        var q = new Vector3d(ix, iy, iz) + delta;
        double pert = (fCount < 256 ? Math.Pow(10.0, -14.5 - rng.NextDouble()) : 0.5) * (rng.Next(2) == 0 ? 1 : -1);
        var a = new Vector3d(ix + 5 + RandSigned(rng), iy - 5 + RandSigned(rng), iz + pert);
        var b = new Vector3d(ix - 5 + RandSigned(rng), iy + 1 + RandSigned(rng), iz + pert);
        var c = new Vector3d(ix + 4 + RandSigned(rng), iy + 6 + RandSigned(rng), iz + pert);

        var map = new Dictionary<string, double>();
        SetPt3(map, "p", p.x, p.y, p.z); SetPt3(map, "q", q.x, q.y, q.z);
        SetPt3(map, "r", r.x, r.y, r.z); SetPt3(map, "s", s.x, s.y, s.z); SetPt3(map, "t", t.x, t.y, t.z);
        SetPt3(map, "a", a.x, a.y, a.z); SetPt3(map, "b", b.x, b.y, b.z); SetPt3(map, "c", c.x, c.y, c.z);
        int sA = dagLpiDet.EvalStageAPermanent(Pack(dagLpiDet, map), vBuf, mBuf).Sign;
        var pts = new[] { p, q, r, s, t, a, b, c };
        if (sA != 2 && eCount < 256) easy[eCount++] = pts;
        else if (sA == 2 && fCount < 256) fallback[fCount++] = pts;
    }

    long sink = 0;
    for (int i = 0; i < 2000; ++i) {
        var pts = easy[i & 255];
        sink += lpiDel(pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7]);
        pts = fallback[i & 255];
        sink += lpiDel(pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7]);
    }
    const int N = 50000;
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < N; ++i) {
        var pts = easy[i & 255];
        sink += lpiDel(pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7]);
    }
    double easyNs = sw.Elapsed.TotalNanoseconds / N;

    sw.Restart();
    for (int i = 0; i < N; ++i) {
        var pts = fallback[i & 255];
        sink += lpiDel(pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7]);
    }
    double fallbackNs = sw.Elapsed.TotalNanoseconds / N;
    Console.WriteLine($"Production IndirectPredicates.Orient3dLPI: Stage-A fast path = {easyNs:F0} ns/call, FixedArith fallback = {fallbackNs:F0} ns/call (ratio {fallbackNs / easyNs:F1}x)");
    if (sink == long.MinValue) Console.WriteLine(sink);
}
