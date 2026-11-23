// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

Console.WriteLine("case | route | setting | status | clean | tris | comp | bnd | bow | euler | genus | vol | ms");

var cases = new (string Name, SwcSkeleton Skel, int Slices, int JointEv, double McCell, double WrapAlpha, double WrapOffset, int ExpectedComp, int ExpectedEuler)[] {
    ("y-junction-60deg", MakeY(60.0), 12, 4, 0.06, 0.14, 0.015, 1, 2),
    ("acute-fork-12deg", MakeY(12.0), 12, 4, 0.06, 0.14, 0.015, 1, 2),
    ("acute-fork-4deg", MakeY(4.0), 12, 4, 0.06, 0.14, 0.015, 1, 2),
    // Odd EdgeVertices (5) puts a sphere vertex on each face centre, so the tip spheres' meshes reach exactly
    // +-r along x and the mesh gap equals the nominal gap (EdgeVertices = 4 reaches only about 0.945 r there).
    ("near-tangent-gap-1e-2", MakeNearTangentPair(1e-2), 12, 5, 0.05, 0.12, 0.015, 1, 2),
    ("near-tangent-gap-1e-6", MakeNearTangentPair(1e-6), 12, 5, 0.05, 0.12, 0.015, 1, 2),
    ("intentional-loop", MakeLoop(), 12, 4, 0.06, 0.14, 0.015, 1, 0),
    ("dendrite-tree-21", MakeDendriteTree(depth: 3, seed: 201, minAngleDeg: 28, maxAngleDeg: 58), 10, 4, 0.07, 0.15, 0.015, 1, 2),
    ("tight-bundle-25", MakeDendriteTree(depth: 4, seed: 202, minAngleDeg: 14, maxAngleDeg: 24, maxSegs: 25), 10, 4, 0.07, 0.15, 0.015, 1, 2),
    ("dendrite-tree-45", MakeDendriteTree(depth: 5, seed: 203, minAngleDeg: 25, maxAngleDeg: 55, maxSegs: 45), 8, 3, 0.09, 0.18, 0.02, 1, 2),
};

var gapLines = new List<string>();
foreach (var c in cases) {
    DMesh3[] parts = BuildSkeletonMeshes(c.Skel, c.Slices, c.JointEv);
    gapLines.Add(DescribeClearance(c.Name, c.Skel, parts));

    var bMulti = RunMultiBoolean(parts);
    PrintRow(c.Name, "bool-multi", $"s{c.Slices}/p{parts.Length}", bMulti.Status, bMulti.Result, "", c.ExpectedComp, c.ExpectedEuler, bMulti.Ms);

    var bTree = RunTreeBoolean(parts);
    PrintRow(c.Name, "bool-tree", $"s{c.Slices}/p{parts.Length}", bTree.Status, bTree.Result, "", c.ExpectedComp, c.ExpectedEuler, bTree.Ms);

    var pipe = RunPipeline(parts);
    PrintRow(c.Name, "pipeline", $"s{c.Slices}/p{parts.Length}", pipe.Status, pipe.Result, pipe.CleanNote, c.ExpectedComp, c.ExpectedEuler, pipe.Ms);

    var conv = RunConvolutionMarchingCubes(c.Skel, c.McCell);
    PrintRow(c.Name, "conv-mc", $"h={c.McCell:G3}", conv.Status, conv.Result, "", c.ExpectedComp, c.ExpectedEuler, conv.Ms);

    var wrap = RunAlphaWrap(parts, c.WrapAlpha, c.WrapOffset);
    PrintRow(c.Name, "wrap", $"{c.WrapAlpha}/{c.WrapOffset}", wrap.Status, wrap.Result, "", c.ExpectedComp, c.ExpectedEuler, wrap.Ms);
}

Console.WriteLine();
Console.WriteLine("case | segments | primitives | measured mesh clearance");
foreach (string line in gapLines)
    Console.WriteLine(line);

// Near-tangent pairs: the measured minimum distance between the two branch meshes (node spheres 1, 3 and the distal
// segment on the left; 2, 4 and the distal segment on the right; the soma and the soma segments join both sides).
// Other cases: the minimum distance between the meshes of two skeleton segments that share no node (a small value
// marks where non-adjacent branches come close; it does not show that a route fuses them there).
static string DescribeClearance(string name, SwcSkeleton skel, DMesh3[] parts)
{
    int nodeCount = skel.Nodes.Count;
    if (name.StartsWith("near-tangent", StringComparison.Ordinal)) {
        int leftSeg = skel.Edges.IndexOf((1, 3)), rightSeg = skel.Edges.IndexOf((2, 4));
        double gap = MeshDistance(Append(parts[1], parts[3], parts[nodeCount + leftSeg]),
            Append(parts[2], parts[4], parts[nodeCount + rightSeg]));
        return $"{name} | {skel.Edges.Count} | {parts.Length} | min gap between the two branch meshes = {gap:G6}";
    }
    double best = double.MaxValue;
    (int, int) bestA = default, bestB = default;
    for (int i = 0; i < skel.Edges.Count; ++i) {
        for (int j = i + 1; j < skel.Edges.Count; ++j) {
            var (a0, a1) = skel.Edges[i];
            var (b0, b1) = skel.Edges[j];
            if (a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1)
                continue;
            double d = MeshDistance(parts[nodeCount + i], parts[nodeCount + j]);
            if (d < best) {
                best = d;
                bestA = skel.Edges[i];
                bestB = skel.Edges[j];
            }
        }
    }
    string where = best == double.MaxValue ? "none (all segments share a node)"
        : $"{best:G4} between segments {bestA.Item1}-{bestA.Item2} and {bestB.Item1}-{bestB.Item2}";
    return $"{name} | {skel.Edges.Count} | {parts.Length} | min distance of non-adjacent segment meshes = {where}";
}

static DMesh3 Append(params DMesh3[] meshes)
{
    var appended = new DMesh3();
    var editor = new MeshEditor(appended);
    foreach (var m in meshes)
        editor.AppendMesh(m);
    return appended;
}

static double MeshDistance(DMesh3 a, DMesh3 b)
{
    var ta = new DMeshAABBTree3(a, autoBuild: true);
    var tb = new DMeshAABBTree3(b, autoBuild: true);
    ta.FindNearestTriangles(tb, null, out double distance);
    return distance;
}

static SwcSkeleton MakeY(double angleDeg)
{
    var skel = new SwcSkeleton();
    int soma = skel.AddNode(-1, new Vector3d(0, 0, 0), 0.45);
    int fork = skel.AddNode(soma, new Vector3d(0, 0, 1.4), 0.25);
    double halfRad = 0.5 * angleDeg * Math.PI / 180.0;
    double len = 2.0;
    skel.AddNode(fork, new Vector3d(len * Math.Sin(halfRad), 0, 1.4 + len * Math.Cos(halfRad)), 0.12);
    skel.AddNode(fork, new Vector3d(-len * Math.Sin(halfRad), 0, 1.4 + len * Math.Cos(halfRad)), 0.12);
    return skel;
}

static SwcSkeleton MakeNearTangentPair(double gap)
{
    // Two parallel distal branches coming out of a U-fork whose outer surfaces pass within `gap` (must stay genus 0).
    var skel = new SwcSkeleton();
    double r = 0.18;
    int soma = skel.AddNode(-1, new Vector3d(0, 0, 0), 0.45);
    int left1 = skel.AddNode(soma, new Vector3d(-0.9, 0, 0.7), 0.22);
    int right1 = skel.AddNode(soma, new Vector3d(0.9, 0, 0.7), 0.22);
    // Curve both tips inward so the two tip spheres of radius r are separated by `gap`.
    double tipX = r + 0.5 * gap;
    skel.AddNode(left1, new Vector3d(-tipX, 0, 2.0), r);
    skel.AddNode(right1, new Vector3d(tipX, 0, 2.0), r);
    return skel;
}

static SwcSkeleton MakeLoop()
{
    // Two branches diverge from a soma and rejoin cleanly at a distal node (intentional genus 1, chi = 0).
    var skel = new SwcSkeleton();
    int soma = skel.AddNode(-1, new Vector3d(0, 0, 0), 0.42);
    int l1 = skel.AddNode(soma, new Vector3d(-0.9, 0, 1.0), 0.22);
    int r1 = skel.AddNode(soma, new Vector3d(0.9, 0, 1.0), 0.22);
    int top = skel.AddNode(l1, new Vector3d(0, 0, 2.0), 0.22);
    skel.AddExtraEdge(r1, top);
    return skel;
}

static SwcSkeleton MakeDendriteTree(int depth, int seed, double minAngleDeg, double maxAngleDeg, int maxSegs = 100)
{
    var rng = new Random(seed);
    var skel = new SwcSkeleton();
    int soma = skel.AddNode(-1, Vector3d.Zero, 0.50);

    // 3 primary dendritic roots emanating from the soma.
    var queue = new Queue<(int Node, Vector3d Dir, double Radius, double Length, int Level)>();
    for (int k = 0; k < 3; ++k) {
        double az = 2.0 * Math.PI * k / 3.0 + 0.15 * (rng.NextDouble() - 0.5);
        double el = 0.25 + 0.35 * rng.NextDouble();
        var dir = new Vector3d(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el)).Normalized;
        int n1 = skel.AddNode(soma, dir * 1.4, 0.26);
        queue.Enqueue((n1, dir, 0.26, 1.25, 1));
    }

    while (queue.Count > 0 && skel.Edges.Count < maxSegs) {
        var (parent, dir, rad, len, level) = queue.Dequeue();
        if (level >= depth)
            continue;

        // Build orthonormal frame around parent direction.
        Vector3d refAxis = Math.Abs(dir.z) < 0.9 ? Vector3d.AxisZ : Vector3d.AxisX;
        Vector3d u = dir.Cross(refAxis).Normalized;
        Vector3d v = dir.Cross(u).Normalized;
        double planePhi = 2.0 * Math.PI * rng.NextDouble();
        Vector3d splitAxis = Math.Cos(planePhi) * u + Math.Sin(planePhi) * v;

        double halfAngle = 0.5 * (minAngleDeg + (maxAngleDeg - minAngleDeg) * rng.NextDouble()) * Math.PI / 180.0;
        double childRad = Math.Max(0.06, rad * 0.76);
        double childLen = len * (0.78 + 0.15 * rng.NextDouble());

        for (int side = -1; side <= 1; side += 2) {
            if (skel.Edges.Count >= maxSegs)
                break;
            Vector3d cDir = (Math.Cos(halfAngle) * dir + side * Math.Sin(halfAngle) * splitAxis).Normalized;
            Vector3d pos = skel.Nodes[parent].Pos + cDir * childLen;
            int child = skel.AddNode(parent, pos, childRad);
            queue.Enqueue((child, cDir, childRad, childLen, level + 1));
        }
    }
    return skel;
}

static DMesh3 MakeTaperedSegment(Vector3d a, double ra, Vector3d b, double rb, int slices)
{
    Vector3d axis = b - a;
    double len = axis.Length;
    var gen = new CappedCylinderGenerator {
        BaseRadius = (float)ra,
        TopRadius = (float)rb,
        Height = (float)len,
        Slices = slices,
        NoSharedVertices = false,
    };
    DMesh3 cyl = gen.Generate().MakeDMesh();
    // CappedCylinderGenerator builds along +Y from y=0 to y=Height.
    Vector3d dir = axis / len;
    var rot = new Quaterniond(Vector3d.AxisY, dir);
    MeshTransforms.Rotate(cyl, Vector3d.Zero, rot);
    MeshTransforms.Translate(cyl, a);
    return cyl;
}

static DMesh3[] BuildSkeletonMeshes(SwcSkeleton skel, int slices, int jointEv)
{
    var parts = new List<DMesh3>(skel.Nodes.Count + skel.Edges.Count);
    // Node spheres (soma + joint caps so tapered tubes connect smoothly without gaps).
    for (int i = 0; i < skel.Nodes.Count; ++i) {
        int ev = i == 0 ? Math.Max(jointEv + 2, 6) : jointEv;
        DMesh3 s = new Sphere3Generator_NormalizedCube {
            EdgeVertices = ev,
            Radius = skel.Nodes[i].Radius
        }.Generate().MakeDMesh();
        MeshTransforms.Translate(s, skel.Nodes[i].Pos);
        parts.Add(s);
    }
    foreach (var (u, v) in skel.Edges) {
        parts.Add(MakeTaperedSegment(skel.Nodes[u].Pos, skel.Nodes[u].Radius, skel.Nodes[v].Pos, skel.Nodes[v].Radius, slices));
    }
    return parts.ToArray();
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

static (string Status, DMesh3 Result, long Ms) RunConvolutionMarchingCubes(SwcSkeleton skel, double cellSize)
{
    var field = new CauchyConvolutionImplicit(skel, pad: 3.0 * cellSize);
    var mc = new MarchingCubes {
        Implicit = field,
        CubeSize = cellSize,
        ParallelCompute = false,
        Bounds = field.Bounds(),
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
    var appended = new DMesh3();
    var editor = new MeshEditor(appended);
    foreach (var part in parts)
        editor.AppendMesh(part);
    var wrap = new MeshAlphaWrap(appended) { Alpha = alpha, Offset = offset };
    var sw = Stopwatch.StartNew();
    wrap.Compute();
    sw.Stop();
    return (wrap.Status.ToString(), wrap.Result, sw.ElapsedMilliseconds);
}

static void PrintRow(string caseName, string route, string setting, string status, DMesh3 mesh,
    string cleanNote, int expComp, int expEuler, long ms)
{
    if (mesh == null || mesh.TriangleCount == 0) {
        Console.WriteLine($"{caseName} | {route} | {setting} | {status} | {cleanNote} | 0 | - | - | - | - | - | - | {ms}");
        return;
    }
    var topo = MeshTopologySummary.Compute(mesh);
    string notClean = cleanNote.Length > 0 ? cleanNote : MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true);
    if (notClean.Length == 0) notClean = "clean";
    Vector2d va = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
    string topoFlag = (topo.Components == expComp && topo.EulerCharacteristic == expEuler) ? "" : "*";
    Console.WriteLine($"{caseName} | {route} | {setting} | {status} | {notClean} | {mesh.TriangleCount} | {topo.Components}{topoFlag} | {topo.BoundaryEdges} | {topo.BowtieVertices} | {topo.EulerCharacteristic}{topoFlag} | {topo.Genus:G3} | {va.x:F3} | {ms}");
}

readonly record struct SwcNode(int Parent, Vector3d Pos, double Radius);

sealed class SwcSkeleton
{
    public readonly List<SwcNode> Nodes = new List<SwcNode>();
    public readonly List<(int A, int B)> Edges = new List<(int, int)>();

    public int AddNode(int parent, Vector3d pos, double radius)
    {
        int id = Nodes.Count;
        Nodes.Add(new SwcNode(parent, pos, radius));
        if (parent >= 0)
            Edges.Add((parent, id));
        return id;
    }

    public void AddExtraEdge(int a, int b) => Edges.Add((a, b));
}

/// <summary>
/// Closed-form Cauchy-kernel line-segment convolution surface (McCormack &amp; Sherstyuk 1998; convolution
/// surfaces after Bloomenthal &amp; Shoemake 1991)
/// combined with a soma point kernel so straight tubes of any radius r hit the iso-level at
/// exact distance r and branch junctions blend smoothly without bulges.
/// </summary>
sealed class CauchyConvolutionImplicit : BoundedImplicitFunction3d
{
    private const double IsoThreshold = 0.25 / 1.4142135623730951; // 1 / (4*sqrt(2))
    private readonly SwcSkeleton _skel;
    private readonly AxisAlignedBox3d _bounds;

    public CauchyConvolutionImplicit(SwcSkeleton skel, double pad)
    {
        _skel = skel;
        var b = AxisAlignedBox3d.Empty;
        foreach (var n in skel.Nodes) {
            double ext = n.Radius * 1.5 + pad;
            b.Contain(n.Pos - new Vector3d(ext));
            b.Contain(n.Pos + new Vector3d(ext));
        }
        _bounds = b;
    }

    public AxisAlignedBox3d Bounds() => _bounds;

    public double Value(ref Vector3d pt)
    {
        double field = 0;

        // Soma point kernel (node 0): equals IsoThreshold at distance = Radius.
        var soma = _skel.Nodes[0];
        double rSoma = soma.Radius;
        double d2Soma = pt.DistanceSquared(soma.Pos);
        double qSoma = (2.0 * rSoma * rSoma) / (rSoma * rSoma + d2Soma);
        field += IsoThreshold * qSoma * Math.Sqrt(qSoma);

        // Analytic Cauchy line-segment convolution integrals along each skeleton edge.
        foreach (var (ia, ib) in _skel.Edges) {
            var na = _skel.Nodes[ia];
            var nb = _skel.Nodes[ib];
            Vector3d ab = nb.Pos - na.Pos;
            double len = ab.Length;
            if (len < 1e-12)
                continue;
            Vector3d u = ab / len;
            Vector3d ap = pt - na.Pos;
            double t0 = ap.Dot(u);
            double tClamp = Math.Clamp(t0 / len, 0.0, 1.0);
            double r = na.Radius + tClamp * (nb.Radius - na.Radius);
            double h2 = Math.Max(0.0, ap.LengthSquared - t0 * t0);

            double a2 = r * r + h2;
            double a = Math.Sqrt(a2);
            double x0 = -t0;
            double x1 = len - t0;
            double integral =
                (x1 / (2.0 * a2 * (a2 + x1 * x1)) + Math.Atan(x1 / a) / (2.0 * a2 * a))
                - (x0 / (2.0 * a2 * (a2 + x0 * x0)) + Math.Atan(x0 / a) / (2.0 * a2 * a));
            field += (r * r * r / Math.PI) * integral;
        }

        // Negative inside, positive outside for MarchingCubes.
        return IsoThreshold - field;
    }
}
