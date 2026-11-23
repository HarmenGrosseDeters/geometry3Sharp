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
