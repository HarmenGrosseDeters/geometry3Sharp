// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
{
    var center = new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
    var extent = new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2);
    return new TrivialBox3Generator { Box = new Box3d(center, extent) }.Generate().MakeDMesh();
}

static DMesh3 Difference(DMesh3 outer, DMesh3 tool)
{
    var boolean = new ExactMeshBoolean(outer, tool, ExactMeshBoolean.Operations.Difference) {
        SafeRounding = true
    };
    if (!boolean.Compute() || !boolean.RoundingClean)
        throw new InvalidOperationException($"Difference: {boolean.Error}, rounding clean: {boolean.RoundingClean}");
    return boolean.Result;
}

static DMesh3 Repair(DMesh3 input, bool passThrough)
{
    var repair = new MeshRobustRepair(new DMesh3(input)) { PassThroughCleanParts = passThrough };
    if (!repair.Apply() || repair.ResultReason != "")
        throw new InvalidOperationException($"Repair: {repair.ResultReason}");
    var expectedPath = passThrough ? MeshRobustRepair.ResultPaths.PassThrough : MeshRobustRepair.ResultPaths.Repair;
    Require(repair.ResultPath == expectedPath, $"Repair: expected {expectedPath}, got {repair.ResultPath}");
    Console.WriteLine($"repair pass-through enabled={passThrough}: {repair.ResultPath}");
    return repair.Mesh;
}

static double Volume(DMesh3 mesh) =>
    MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;

// Precondition: a clean boundary of a bounded solid, made of pairwise disjoint shells.
// A vertex of a connected shell is off every other shell and suffices for containment.
static (int Parts, int Tunnels, int Cavities) SolidBetti(DMesh3 mesh, MeshTopologySummary summary)
{
    DMesh3[] shells = MeshConnectedComponents.Separate(mesh);
    var trees = shells.Select(shell => new DMeshAABBTree3(shell, true)).ToArray();
    int parts = 0;
    for (int i = 0; i < shells.Length; ++i) {
        Vector3d witness = shells[i].GetVertex(shells[i].VertexIndices().First());
        int depth = 0;
        for (int j = 0; j < shells.Length; ++j) {
            if (i != j && trees[j].WindingNumber(witness) != 0)
                ++depth;
        }
        if (depth % 2 == 0)
            ++parts;
    }
    return (parts, checked((int)summary.Genus), shells.Length - parts);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static double MedianMicroseconds(Action action)
{
    const int iterations = 1000;
    for (int i = 0; i < iterations; ++i)
        action();
    var samples = new double[7];
    for (int batch = 0; batch < samples.Length; ++batch) {
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; ++i)
            action();
        samples[batch] = watch.Elapsed.TotalMicroseconds / iterations;
    }
    Array.Sort(samples);
    return samples[samples.Length / 2];
}

var cavity = Difference(Box(0, 0, 0, 3, 3, 3), Box(1, 1, 1, 2, 2, 2));
var repaired = Repair(cavity, true);
var forcedRepair = Repair(cavity, false);

// The replacement has the same total signed volume as the cavity (24 + 2 = 26).
var separate = Box(0, 0, 0, 3, 4, 2);
new MeshEditor(separate).AppendMesh(Box(6, 0, 0, 8, 1, 1));
var filled = Box(0, 0, 0, 3, 3, 3);
var tunnel = Difference(Box(0, 0, 0, 3, 3, 3), Box(1, 1, -1, 2, 2, 4));

// Independent expected values from the box constructions. Cleanliness is checked on each output.
var cases = new[] {
    ("Boolean cavity", cavity, (1, 0, 1)),
    ("Repair pass-through", repaired, (1, 0, 1)),
    ("Repair forced", forcedRepair, (1, 0, 1)),
    ("Filled cavity", filled, (1, 0, 0)),
    ("Disjoint solids", separate, (2, 0, 0)),
    ("Through tunnel", tunnel, (1, 1, 0)),
};
Console.WriteLine("case | triangles | surface Betti | solid Betti | total volume | signed shell volumes | surface matches cavity | solid matches cavity");
foreach (var (name, mesh, expected) in cases) {
    Require(mesh.CheckValidity(false, FailMode.ReturnOnly) && mesh.Vertices().All(v => v.IsFinite),
        $"{name}: invalid mesh or non-finite vertices");
    string reason = MeshRobustRepair.NotCleanReason(mesh);
    Require(reason == "", $"{name}: {reason}");
    var summary = MeshTopologySummary.Compute(mesh);
    var surface = (summary.Components, 2 * summary.Genus, summary.Components);
    double volume = Volume(mesh);
    DMesh3[] shells = MeshConnectedComponents.Separate(mesh);
    var trees = shells.Select(shell => new DMeshAABBTree3(shell, true)).ToArray();
    for (int i = 0; i < trees.Length; ++i)
        for (int j = i + 1; j < trees.Length; ++j)
            Require(!trees[i].TestIntersection(trees[j]), $"{name}: shells meet");
    var solid = SolidBetti(mesh, summary);
    double[] shellVolumes = shells.Select(Volume).OrderBy(v => v).ToArray();
    Console.WriteLine($"{name} | {mesh.TriangleCount} | {surface} | {solid} | {volume:G12} | [{string.Join(", ", shellVolumes.Select(v => v.ToString("G12")))}] | {surface == (2, 0, 2)} | {solid == (1, 0, 1)}");
    Require(solid == expected, $"{name}: expected solid Betti {expected}, got {solid}");
    if (mesh == separate) {
        Require(mesh.TriangleCount == cavity.TriangleCount, "Disjoint control must match cavity triangle count.");
        Require(surface == (2, 0, 2), "Disjoint control must match cavity surface topology.");
        Require(Math.Abs(volume - Volume(cavity)) < 1e-12, "Disjoint control must match cavity volume.");
    }
}

// Two box boundaries glued at exactly one mesh vertex. Each boundary is a sphere,
// so the union has Betti (1, 0, 2) by the wedge construction.
var pointContact = Box(0, 0, 0, 1, 1, 1);
var secondBox = Box(1, 1, 1, 2, 2, 2);
var shared = new Vector3d(1, 1, 1);
int sharedId = pointContact.VertexIndices().Single(id => pointContact.GetVertex(id) == shared);
var vertexMap = new int[secondBox.MaxVertexID];
foreach (int id in secondBox.VertexIndices()) {
    Vector3d position = secondBox.GetVertex(id);
    vertexMap[id] = position == shared ? sharedId : pointContact.AppendVertex(position);
}
foreach (int id in secondBox.TriangleIndices()) {
    Index3i t = secondBox.GetTriangle(id);
    Require(pointContact.AppendTriangle(vertexMap[t.a], vertexMap[t.b], vertexMap[t.c]) >= 0,
        "Point contact: triangle append failed");
}
var pointSummary = MeshTopologySummary.Compute(pointContact);
string pointReason = MeshRobustRepair.NotCleanReason(pointContact);
Console.WriteLine($"Point contact | edge components={pointSummary.Components} | bowties={pointSummary.BowtieVertices} | clean check={pointReason} | reference surface Betti=(1, 0, 2)");
Require(pointSummary.Components == 2 && pointSummary.BowtieVertices == 1 && pointReason == "bowties",
    "Point contact: expected two edge components and a rejected bowtie.");

double baseline = MedianMicroseconds(() => MeshTopologySummary.Compute(cavity));
double extended = MedianMicroseconds(() => SolidBetti(cavity, MeshTopologySummary.Compute(cavity)));
Console.WriteLine($"Boolean cavity median us/check: summary={baseline:F2}, summary+nesting={extended:F2}");
