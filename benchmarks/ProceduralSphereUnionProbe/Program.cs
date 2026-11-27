// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static DMesh3[] BuildBallMeshes(Ball[] balls, int edgeVerts)
{
    var parts = new DMesh3[balls.Length];
    for (int i = 0; i < balls.Length; ++i) {
        parts[i] = new Sphere3Generator_NormalizedCube {
            EdgeVertices = edgeVerts,
            Radius = balls[i].Radius
        }.Generate().MakeDMesh();
        MeshTransforms.Translate(parts[i], balls[i].Center);
    }
    return parts;
}

static DMesh3 AppendAll(DMesh3[] parts)
{
    var appended = new DMesh3();
    var editor = new MeshEditor(appended);
    foreach (var part in parts)
        editor.AppendMesh(part);
    return appended;
}

static (string Status, DMesh3 Result, long Ms) RunMultiBoolean(DMesh3[] parts)
{
    var sw = Stopwatch.StartNew();
    var op = new ExactMeshBoolean(parts, ExactMeshBoolean.Operations.Union) { StrictRounding = true };
    bool ok = op.Compute();
    sw.Stop();
    return (ok ? op.Status.ToString() : $"FAIL:{op.Status}", op.Result, sw.ElapsedMilliseconds);
}

static (string Status, DMesh3 Result, long Ms) RunTreeBoolean(DMesh3[] parts)
{
    var sw = Stopwatch.StartNew();
    var cur = new List<DMesh3>(parts);
    while (cur.Count > 1) {
        var next = new List<DMesh3>((cur.Count + 1) / 2);
        for (int i = 0; i < cur.Count; i += 2) {
            if (i + 1 == cur.Count) {
                next.Add(cur[i]);
                continue;
            }
            var op = new ExactMeshBoolean(cur[i], cur[i + 1], ExactMeshBoolean.Operations.Union) { StrictRounding = true };
            bool ok = op.Compute();
            if (!ok || op.Result == null) {
                sw.Stop();
                return ($"FAIL:{op.Status}", op.Result, sw.ElapsedMilliseconds);
            }
            next.Add(op.Result);
        }
        cur = next;
    }
    sw.Stop();
    return ("Success", cur[0], sw.ElapsedMilliseconds);
}

static (string Status, DMesh3 Result, string CleanNote, long Ms) RunPipeline(DMesh3[] parts)
{
    var sw = Stopwatch.StartNew();
    var pipe = new SolidBooleanPipeline(parts, ExactMeshBoolean.Operations.Union);
    bool ok = pipe.Compute();
    sw.Stop();
    string status = ok ? pipe.Status.ToString() : $"FAIL:{pipe.Status}";
    string clean = pipe.Report.Clean ? "clean"
        : (pipe.Report.OnlyContacts ? $"contacts({pipe.Report.ContactsResult})" : pipe.Report.NotCleanReason);
    return (status, pipe.Result, clean, sw.ElapsedMilliseconds);
}

static (string Status, DMesh3 Result, long Ms) RunMarchingCubes(Ball[] balls, double cellSize, Vector3d shift)
{
    var field = new UnionOfBallsImplicit(balls, pad: 2.0 * cellSize);
    var b = field.Bounds();
    var mc = new MarchingCubes {
        Implicit = field,
        CubeSize = cellSize,
        ParallelCompute = false,
        Bounds = new AxisAlignedBox3d(b.Min + shift, b.Max + shift),
        RootMode = MarchingCubes.RootfindingModes.Bisection,
        RootModeSteps = 10
    };
    var sw = Stopwatch.StartNew();
    mc.Generate();
    sw.Stop();
    return ("generated", mc.Mesh, sw.ElapsedMilliseconds);
}

static (string Status, DMesh3 Result, long Ms) RunAlphaWrap(DMesh3[] parts, double alpha, double offset)
{
    var appended = AppendAll(parts);
    var wrap = new MeshAlphaWrap(appended) { Alpha = alpha, Offset = offset };
    var sw = Stopwatch.StartNew();
    wrap.Compute();
    sw.Stop();
    return (wrap.Status.ToString(), wrap.Result, sw.ElapsedMilliseconds);
}

static void PrintRow(string caseName, string route, string setting, string status, DMesh3 mesh,
    string cleanNote, double? analyticVol, double? analyticArea, long ms)
{
    if (mesh == null || mesh.TriangleCount == 0) {
        Console.WriteLine($"{caseName} | {route} | {setting} | {status} | {cleanNote} | 0 | - | - | - | - | - | {ms}");
        return;
    }
    var topo = MeshTopologySummary.Compute(mesh);
    string notClean = cleanNote.Length > 0 ? cleanNote : MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true);
    if (notClean.Length == 0) notClean = "clean";
    Vector2d va = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
    string volStr = analyticVol.HasValue ? $"{(va.x - analyticVol.Value) / analyticVol.Value:P2}" : $"{va.x:F3}";
    string areaStr = analyticArea.HasValue ? $"{(va.y - analyticArea.Value) / analyticArea.Value:P2}" : $"{va.y:F3}";
    Console.WriteLine($"{caseName} | {route} | {setting} | {status} | {notClean} | {mesh.TriangleCount} | {topo.Components} | {topo.BoundaryEdges} | {topo.BowtieVertices} | {topo.EulerCharacteristic} | {volStr} | {areaStr} | {ms}");
}

static Ball[] MakeHelix(int count)
{
    // Alpha-helix-like overlapping chain (3.6 residues/turn, pitch 2.5, helix radius 1.0, atomic radii 1.05-1.22).
    // Adjacent center distance ~ 1.67 < 2.10 so consecutive balls overlap and i, i+3/i+4 contacts form helical grooves.
    double[] radii = { 1.15, 1.08, 1.18, 1.05, 1.22 };
    var balls = new Ball[count];
    for (int i = 0; i < count; ++i) {
        double theta = i * (2.0 * Math.PI / 3.6);
        double z = i * (2.5 / 3.6);
        balls[i] = new Ball(new Vector3d(1.0 * Math.Cos(theta), 1.0 * Math.Sin(theta), z), radii[i % radii.Length]);
    }
    return balls;
}

static Ball[] MakeRandomPacking(int count, int seed)
{
    // Realistic atomic van der Waals radii (H=1.20, O=1.52, N=1.55, C=1.70, S=1.80) scaled by 0.5.
    double[] palette = { 0.60, 0.76, 0.775, 0.85, 0.90 };
    var rng = new Random(seed);
    var balls = new List<Ball>(count) { new Ball(Vector3d.Zero, 0.85) };
    while (balls.Count < count) {
        double r = palette[rng.Next(palette.Length)];
        Ball anchor = balls[rng.Next(balls.Count)];
        // Random unit direction
        double u = 2.0 * rng.NextDouble() - 1.0;
        double phi = 2.0 * Math.PI * rng.NextDouble();
        double s = Math.Sqrt(1.0 - u * u);
        var dir = new Vector3d(s * Math.Cos(phi), s * Math.Sin(phi), u);
        // Place at 0.55..0.85 of sum of radii (guaranteed overlap, some near-tangent pairs with other balls)
        double frac = 0.55 + 0.30 * rng.NextDouble();
        Vector3d c = anchor.Center + dir * (frac * (anchor.Radius + r));
        // Reject exact center coincidence
        if (balls.All(b => b.Center.Distance(c) > 0.35 * Math.Min(b.Radius, r)))
            balls.Add(new Ball(c, r));
    }
    return balls.ToArray();
}

static Ball[] MakeBenzeneRing()
{
    // 6 coplanar equal unit balls around a circle of radius 1.4 (chord = 1.4 < 2 => torus-like ring, chi = 0).
    var balls = new Ball[6];
    for (int i = 0; i < 6; ++i) {
        double a = 2.0 * Math.PI * i / 6.0;
        balls[i] = new Ball(new Vector3d(1.4 * Math.Cos(a), 1.4 * Math.Sin(a), 0), 1.0);
    }
    return balls;
}

static Ball[] MakeTetraCavity()
{
    // 4 unit balls at regular tetrahedron with edge length d = 1.68:
    // circumradius of each face = d/sqrt(3) = 0.970 < 1 (sealed), distance to centroid = d*sqrt(3/8) = 1.029 > 1 (cavity!).
    double d = 1.68;
    double s = d / (2.0 * Math.Sqrt(2.0));
    return new[] {
        new Ball(new Vector3d(s, s, s), 1.0),
        new Ball(new Vector3d(s, -s, -s), 1.0),
        new Ball(new Vector3d(-s, s, -s), 1.0),
        new Ball(new Vector3d(-s, -s, s), 1.0),
    };
}

static Ball[] MakeOctaCavity()
{
    // 6 unit balls at +/- (a, 0, 0) with a = 1.15: neighbor dist 1.626 < 2, face circumradius 0.939 < 1, center dist 1.15 > 1.
    double a = 1.15;
    return new[] {
        new Ball(new Vector3d(a, 0, 0), 1.0), new Ball(new Vector3d(-a, 0, 0), 1.0),
        new Ball(new Vector3d(0, a, 0), 1.0), new Ball(new Vector3d(0, -a, 0), 1.0),
        new Ball(new Vector3d(0, 0, a), 1.0), new Ball(new Vector3d(0, 0, -a), 1.0),
    };
}

// Scaling mode (#1466): `scaling [reps] [family:n,n,...]...` runs the multi-mesh and the tree route of each family alternately
// `reps` times and prints one RAW line per run with the public PhaseMilliseconds and Counts (tree: summed over its unions).
if (args.Length > 0 && args[0] == "scaling") {
    RunScaling(args);
    return;
}
// Hybrid mode (#1466): `hybrid [reps] [order] family:n:g,g,...`: bodies in groups of g (order idx = input order, morton = by the
// Morton code of the ball centres), one ExactMeshBoolean arrangement per group, then a balanced pairwise tree of the group results
// (g = 1 is the pairwise tree, g >= n the single arrangement).
if (args.Length > 0 && args[0] == "hybrid") {
    RunHybrid(args);
    return;
}

void RunHybrid(string[] a)
{
    int reps = int.Parse(a[1]);
    string order = a[2];
    for (int i = 3; i < a.Length; ++i) {
        var f = a[i].Split(':');
        string name = f[0];
        int n = int.Parse(f[1]);
        var groups = f[2].Split(',').Select(int.Parse).ToArray();
        var balls = MakeFamily(name, n);
        if (order == "morton") {
            var lo = new Vector3d(balls.Min(b => b.Center.x), balls.Min(b => b.Center.y), balls.Min(b => b.Center.z));
            var hi = new Vector3d(balls.Max(b => b.Center.x), balls.Max(b => b.Center.y), balls.Max(b => b.Center.z));
            ulong Code(Vector3d c)
            {
                ulong code = 0;
                uint[] q = new uint[3];
                for (int k = 0; k < 3; ++k) {
                    double span = Math.Max(hi[k] - lo[k], 1e-12);
                    q[k] = (uint)Math.Min(1023, Math.Max(0, (c[k] - lo[k]) / span * 1023.0));
                }
                for (int bit = 9; bit >= 0; --bit)
                    for (int k = 0; k < 3; ++k)
                        code = (code << 1) | ((q[k] >> bit) & 1u);
                return code;
            }
            balls = balls.OrderBy(b => Code(b.Center)).ToArray();
        }
        var parts = BuildBallMeshes(balls, 5);
        Console.WriteLine($"INFO {name} {n} order={order}");
        for (int r = 0; r < reps; ++r)
            foreach (int g in groups) {
                var sw = Stopwatch.StartNew();
                var phases = new Dictionary<string, long>();
                var counts = new Dictionary<string, long>();
                var cur = new List<DMesh3>();
                string status = "Success";
                for (int s0 = 0; s0 < parts.Length; s0 += g) {
                    var grp = parts.Skip(s0).Take(g).ToArray();
                    if (grp.Length == 1) { cur.Add(grp[0]); continue; }
                    var res = RunMultiBooleanStats(grp);
                    if (res.Result == null) { status = res.Status; break; }
                    foreach (var kv in res.Phases) phases[kv.Key] = phases.GetValueOrDefault(kv.Key) + kv.Value;
                    foreach (var kv in res.Counts) if (kv.Key != "result_hash") counts[kv.Key] = counts.GetValueOrDefault(kv.Key) + kv.Value;
                    cur.Add(res.Result);
                }
                while (status == "Success" && cur.Count > 1) {
                    var next = new List<DMesh3>();
                    for (int i2 = 0; i2 < cur.Count; i2 += 2) {
                        if (i2 + 1 == cur.Count) { next.Add(cur[i2]); continue; }
                        var res = RunMultiBooleanStats(new[] { cur[i2], cur[i2 + 1] });
                        if (res.Result == null) { status = res.Status; break; }
                        foreach (var kv in res.Phases) phases[kv.Key] = phases.GetValueOrDefault(kv.Key) + kv.Value;
                        foreach (var kv in res.Counts) if (kv.Key != "result_hash") counts[kv.Key] = counts.GetValueOrDefault(kv.Key) + kv.Value;
                        next.Add(res.Result);
                    }
                    cur = next;
                }
                sw.Stop();
                var fin = status == "Success" ? cur[0] : null;
                counts["result_hash"] = ResultHash(fin);
                if (fin != null) {
                    Vector2d va = MeshMeasurements.VolumeArea(fin, fin.TriangleIndices(), fin.GetVertex);
                    counts["volume_e9"] = (long)Math.Round(va.x * 1e9);
                    counts["area_e9"] = (long)Math.Round(va.y * 1e9);
                    counts["clean"] = ReportClean(fin) ? 1 : 0;
                }
                Console.WriteLine($"RAW {name} {n} hyb{g}{(order == "morton" ? "m" : "")} {r} {sw.ElapsedMilliseconds} {fin?.TriangleCount ?? 0} {status} {FormatStats(phases, counts)}");
            }
    }
}

void RunScaling(string[] a)
{
    int reps = a.Length > 1 ? int.Parse(a[1]) : 5;
    var families = new List<(string Name, int[] Sizes)>();
    for (int i = 2; i < a.Length; ++i) {
        var parts = a[i].Split(':');
        families.Add((parts[0], parts[1].Split(',').Select(int.Parse).ToArray()));
    }
    if (families.Count == 0)
        families.Add(("dense104", new[] { 12, 24, 48, 96 }));
    // (a run to warm up the JIT, not printed)
    RunMultiBooleanStats(BuildBallMeshes(MakeFamily("dense104", 12), 5));
    foreach (var (name, sizes) in families) {
        foreach (int n in sizes) {
            var balls = MakeFamily(name, n);
            var parts = BuildBallMeshes(balls, 5);
            long inTris = parts.Sum(p => (long)p.TriangleCount);
            // overlap degree: other balls whose sphere overlaps each ball (centre distance below the radii's sum)
            var deg = new int[balls.Length];
            for (int i = 0; i < balls.Length; ++i)
                for (int j = i + 1; j < balls.Length; ++j)
                    if (balls[i].Center.Distance(balls[j].Center) < balls[i].Radius + balls[j].Radius) { deg[i]++; deg[j]++; }
            Console.WriteLine($"INFO {name} {n} input_tris={inTris} degree_mean={deg.Average():F2} degree_max={deg.Max()}");
            for (int r = 0; r < reps; ++r) {
                var m = RunMultiBooleanStats(parts);
                AddMeasures(m.Result, m.Counts);
                Console.WriteLine($"RAW {name} {n} multi {r} {m.Ms} {m.Tris} {m.Status} {FormatStats(m.Phases, m.Counts)}");
                if (Environment.GetEnvironmentVariable("G3_ROUTES") == "multi")
                    continue;
                var t = RunTreeBooleanStats(parts);
                AddMeasures(t.Result, t.Counts);
                Console.WriteLine($"RAW {name} {n} tree {r} {t.Ms} {t.Tris} {t.Status} {FormatStats(t.Phases, t.Counts)}");
            }
        }
    }
}

static void AddMeasures(DMesh3 mesh, Dictionary<string, long> counts)
{
    if (mesh == null) return;
    Vector2d va = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
    counts["volume_e9"] = (long)Math.Round(va.x * 1e9);
    counts["area_e9"] = (long)Math.Round(va.y * 1e9);
    counts["clean"] = ReportClean(mesh) ? 1 : 0;
}

// clean = closed and MeshRobustRepair.NotCleanReason (with the self-intersection check) is empty; a reason is printed as a CLEAN line
static bool ReportClean(DMesh3 mesh)
{
    string reason = MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true);
    if (!mesh.IsClosed() && reason.Length == 0) reason = "not closed";
    if (reason.Length > 0) Console.WriteLine($"CLEAN NOT {reason}");
    return reason.Length == 0;
}

// FNV-1a over the result's vertex coordinates (bit patterns) and triangles in index order: equal hashes mean the same mesh bit for bit
static long ResultHash(DMesh3 m)
{
    if (m == null) return 0;
    ulong h = 14695981039346656037UL;
    foreach (int v in m.VertexIndices()) {
        var p = m.GetVertex(v);
        foreach (double d in new[] { p.x, p.y, p.z }) { h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(d)) * 1099511628211UL; }
    }
    foreach (int t in m.TriangleIndices()) {
        var tr = m.GetTriangle(t);
        h = (h ^ (ulong)tr.a) * 1099511628211UL; h = (h ^ (ulong)tr.b) * 1099511628211UL; h = (h ^ (ulong)tr.c) * 1099511628211UL;
    }
    return (long)(h & 0x7FFFFFFFFFFFFFFFUL);
}

static string FormatStats(Dictionary<string, long> phases, Dictionary<string, long> counts) =>
    string.Join(";", phases.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => "t." + kv.Key + "=" + kv.Value)
        .Concat(counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => "c." + kv.Key + "=" + kv.Value)));

static Ball[] MakeFamily(string name, int n)
{
    // dense<seed>: the random packing (prefixes of one seed are nested); helix: the 1D chain; lattice: a cubic lattice of
    // equal balls at 0.9 of the diameter (each ball meets its 6 face neighbours); sparse<seed>: the packing at 0.95..1.15 of the radii's sum plus a drift
    if (name.StartsWith("dense"))
        return MakeRandomPacking(n, int.Parse(name.Substring(5)));
    if (name == "helix")
        return MakeHelix(n);
    if (name == "lattice") {
        int side = (int)Math.Ceiling(Math.Pow(n, 1.0 / 3.0));
        var l = new List<Ball>();
        for (int k = 0; k < side && l.Count < n; ++k)
            for (int j = 0; j < side && l.Count < n; ++j)
                for (int i = 0; i < side && l.Count < n; ++i)
                    l.Add(new Ball(new Vector3d(i, j, k) * 1.8, 1.0));
        return l.ToArray();
    }
    throw new ArgumentException(name);
}

static (string Status, DMesh3 Result, long Ms, int Tris, Dictionary<string, long> Phases, Dictionary<string, long> Counts) RunMultiBooleanStats(DMesh3[] parts)
{
    var sw = Stopwatch.StartNew();
    var op = new ExactMeshBoolean(parts, ExactMeshBoolean.Operations.Union) { StrictRounding = true };
    bool ok = op.Compute();
    sw.Stop();
    var counts = new Dictionary<string, long>(op.Counts) { ["patches"] = op.Patches, ["result_hash"] = ResultHash(op.Result) };
    return (ok ? op.Status.ToString() : $"FAIL:{op.Status}", op.Result, sw.ElapsedMilliseconds, op.Result?.TriangleCount ?? 0, op.PhaseMilliseconds, counts);
}

static (string Status, DMesh3 Result, long Ms, int Tris, Dictionary<string, long> Phases, Dictionary<string, long> Counts) RunTreeBooleanStats(DMesh3[] parts)
{
    var sw = Stopwatch.StartNew();
    var phases = new Dictionary<string, long>();
    var counts = new Dictionary<string, long>();
    counts["unions"] = 0;
    var cur = new List<DMesh3>(parts);
    while (cur.Count > 1) {
        var next = new List<DMesh3>((cur.Count + 1) / 2);
        for (int i = 0; i < cur.Count; i += 2) {
            if (i + 1 == cur.Count) {
                next.Add(cur[i]);
                continue;
            }
            var op = new ExactMeshBoolean(cur[i], cur[i + 1], ExactMeshBoolean.Operations.Union) { StrictRounding = true };
            bool ok = op.Compute();
            if (!ok || op.Result == null) {
                sw.Stop();
                return ($"FAIL:{op.Status}", op.Result, sw.ElapsedMilliseconds, 0, phases, counts);
            }
            foreach (var kv in op.PhaseMilliseconds) phases[kv.Key] = phases.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in op.Counts) counts[kv.Key] = counts.GetValueOrDefault(kv.Key) + kv.Value;
            counts["patches"] = counts.GetValueOrDefault("patches") + op.Patches;
            counts["unions"]++;
            next.Add(op.Result);
        }
        cur = next;
    }
    sw.Stop();
    counts["result_hash"] = ResultHash(cur[0]);
    return ("Success", cur[0], sw.ElapsedMilliseconds, cur[0].TriangleCount, phases, counts);
}

Console.WriteLine("case | route | setting | status | clean | tris | comp | bnd | bow | euler | vol(err) | area(err) | ms");

// --- Group A: Analytic baselines (2-3 collinear spheres) ---
foreach (var (name, count, spacing) in new[] {
    ("overlap-2", 2, 1.5),
    ("thin-neck-2", 2, 2.0 - 1.0 / 128.0),
    ("small-gap-2", 2, 2.0 + 1.0 / 128.0),
    ("chain-3", 3, 1.5),
}) {
    double h = Math.Max(0, 1.0 - spacing / 2.0);
    double vol = count * (4.0 * Math.PI / 3.0) - (count - 1) * 2.0 * Math.PI * h * h * (1.0 - h / 3.0);
    double area = count * 4.0 * Math.PI - (count - 1) * 4.0 * Math.PI * h;
    var balls = Enumerable.Range(0, count)
        .Select(i => new Ball(new Vector3d((i - (count - 1) / 2.0) * spacing, 0, 0), 1.0))
        .ToArray();
    var parts = BuildBallMeshes(balls, edgeVerts: 9);

    var bMulti = RunMultiBoolean(parts);
    PrintRow(name, "bool-multi", "ev9", bMulti.Status, bMulti.Result, "", vol, area, bMulti.Ms);
    var pipe = RunPipeline(parts);
    PrintRow(name, "pipeline", "ev9", pipe.Status, pipe.Result, pipe.CleanNote, vol, area, pipe.Ms);
    var mc0 = RunMarchingCubes(balls, 1.0 / 16.0, Vector3d.Zero);
    PrintRow(name, "mc", "16-p0", mc0.Status, mc0.Result, "", vol, area, mc0.Ms);
    var mc1 = RunMarchingCubes(balls, 1.0 / 16.0, new Vector3d(1.0 / 32.0, 0, 0));
    PrintRow(name, "mc", "16-p1", mc1.Status, mc1.Result, "", vol, area, mc1.Ms);
    var wrap = RunAlphaWrap(parts, alpha: 0.20, offset: 0.02);
    PrintRow(name, "wrap", "0.20/0.02", wrap.Status, wrap.Result, "", vol, area, wrap.Ms);
}

// --- Group B: Degenerate, near-tangent, coplanar-ring and cavity clusters ---
var stressB = new (string Name, Ball[] Balls, int Ev, double McCell, double WrapAlpha, double WrapOffset)[] {
    ("exact-tangent-2", new[] { new Ball(new Vector3d(-1, 0, 0), 1), new Ball(new Vector3d(1, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("gap-1e-6", new[] { new Ball(new Vector3d(-1 - 0.5e-6, 0, 0), 1), new Ball(new Vector3d(1 + 0.5e-6, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("gap-1e-9", new[] { new Ball(new Vector3d(-1 - 0.5e-9, 0, 0), 1), new Ball(new Vector3d(1 + 0.5e-9, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("gap-1e-12", new[] { new Ball(new Vector3d(-1 - 0.5e-12, 0, 0), 1), new Ball(new Vector3d(1 + 0.5e-12, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("overlap-1e-6", new[] { new Ball(new Vector3d(-1 + 0.5e-6, 0, 0), 1), new Ball(new Vector3d(1 - 0.5e-6, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("overlap-1e-9", new[] { new Ball(new Vector3d(-1 + 0.5e-9, 0, 0), 1), new Ball(new Vector3d(1 - 0.5e-9, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("overlap-1e-12", new[] { new Ball(new Vector3d(-1 + 0.5e-12, 0, 0), 1), new Ball(new Vector3d(1 - 0.5e-12, 0, 0), 1) }, 9, 1.0 / 16.0, 0.20, 0.02),
    ("coplanar-ring-6", MakeBenzeneRing(), 7, 1.0 / 12.0, 0.25, 0.02),
    ("tetra-cavity-4", MakeTetraCavity(), 9, 1.0 / 16.0, 0.20, 0.02),
    ("octa-cavity-6", MakeOctaCavity(), 7, 1.0 / 14.0, 0.20, 0.02),
};

foreach (var c in stressB) {
    var parts = BuildBallMeshes(c.Balls, c.Ev);
    var bMulti = RunMultiBoolean(parts);
    PrintRow(c.Name, "bool-multi", $"ev{c.Ev}", bMulti.Status, bMulti.Result, "", null, null, bMulti.Ms);
    var pipe = RunPipeline(parts);
    PrintRow(c.Name, "pipeline", $"ev{c.Ev}", pipe.Status, pipe.Result, pipe.CleanNote, null, null, pipe.Ms);
    var mc = RunMarchingCubes(c.Balls, c.McCell, Vector3d.Zero);
    PrintRow(c.Name, "mc", $"h={c.McCell:G3}", mc.Status, mc.Result, "", null, null, mc.Ms);
    var wrap = RunAlphaWrap(parts, c.WrapAlpha, c.WrapOffset);
    PrintRow(c.Name, "wrap", $"{c.WrapAlpha}/{c.WrapOffset}", wrap.Status, wrap.Result, "", null, null, wrap.Ms);
}

// --- Group C: Molecule-like helices and random multi-radii packings (scaling / cliffs) ---
var stressC = new (string Name, Ball[] Balls, int Ev, double McCell, double WrapAlpha, double WrapOffset)[] {
    ("helix-16", MakeHelix(16), 5, 0.14, 0.25, 0.02),
    ("helix-32", MakeHelix(32), 5, 0.16, 0.25, 0.02),
    ("packing-12", MakeRandomPacking(12, seed: 101), 5, 0.10, 0.22, 0.02),
    ("packing-24", MakeRandomPacking(24, seed: 102), 5, 0.12, 0.22, 0.02),
    ("packing-48", MakeRandomPacking(48, seed: 103), 5, 0.14, 0.25, 0.02),
    ("packing-96", MakeRandomPacking(96, seed: 104), 5, 0.16, 0.28, 0.02),
};

foreach (var c in stressC) {
    var parts = BuildBallMeshes(c.Balls, c.Ev);
    var bMulti = RunMultiBoolean(parts);
    PrintRow(c.Name, "bool-multi", $"ev{c.Ev}", bMulti.Status, bMulti.Result, "", null, null, bMulti.Ms);
    var bTree = RunTreeBoolean(parts);
    PrintRow(c.Name, "bool-tree", $"ev{c.Ev}", bTree.Status, bTree.Result, "", null, null, bTree.Ms);
    var pipe = RunPipeline(parts);
    PrintRow(c.Name, "pipeline", $"ev{c.Ev}", pipe.Status, pipe.Result, pipe.CleanNote, null, null, pipe.Ms);
    var mc = RunMarchingCubes(c.Balls, c.McCell, Vector3d.Zero);
    PrintRow(c.Name, "mc", $"h={c.McCell:G3}", mc.Status, mc.Result, "", null, null, mc.Ms);
    var wrap = RunAlphaWrap(parts, c.WrapAlpha, c.WrapOffset);
    PrintRow(c.Name, "wrap", $"{c.WrapAlpha}/{c.WrapOffset}", wrap.Status, wrap.Result, "", null, null, wrap.Ms);
}

readonly record struct Ball(Vector3d Center, double Radius);

sealed class UnionOfBallsImplicit : BoundedImplicitFunction3d
{
    private readonly Ball[] _balls;
    private readonly AxisAlignedBox3d _bounds;

    public UnionOfBallsImplicit(Ball[] balls, double pad = 0.5)
    {
        _balls = balls;
        var b = AxisAlignedBox3d.Empty;
        foreach (var ball in balls) {
            b.Contain(ball.Center - new Vector3d(ball.Radius + pad));
            b.Contain(ball.Center + new Vector3d(ball.Radius + pad));
        }
        _bounds = b;
    }

    public AxisAlignedBox3d Bounds() => _bounds;

    public double Value(ref Vector3d pt)
    {
        double minD = double.PositiveInfinity;
        for (int i = 0; i < _balls.Length; ++i) {
            double d = pt.Distance(_balls[i].Center) - _balls[i].Radius;
            if (d < minD)
                minD = d;
        }
        return minD;
    }
}
