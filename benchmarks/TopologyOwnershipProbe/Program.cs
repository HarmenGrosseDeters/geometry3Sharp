using System;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests;

var anchors = new[] { new Vector3d(1.5, 1.5, 1.5), new Vector3d(3.5, 1.5, 1.5),
    new Vector3d(11.5, 1.5, 1.5), new Vector3d(13.5, 1.5, 1.5) };
DMesh3 stock = Join(Box(0, 0, 0, 5), Box(10, 0, 0, 5));
DMesh3 reference = Subtract(1, 3);
var repair = new MeshRobustRepair(new DMesh3(reference)) { PassThroughCleanParts = false };
if (!repair.Apply()) throw new InvalidOperationException(repair.ResultReason);
Console.WriteLine($"forced repair path: {repair.ResultPath}");
Console.WriteLine("case | triangles | surface (shells, genus, euler) | solid Betti | signed volume | signed shell volumes | landmark material");
Report("expected Boolean", reference);
Report("forced repair", repair.Mesh);
Report("one cavity each", Subtract(1, 11));
Report("both cavities moved", Subtract(11, 13));

var tree = new DMeshAABBTree3(reference, true);
Timing("SolidBetti", () => SolidBetti.Compute(reference).Parts);
Timing("build + four winding queries", () => Count(new DMeshAABBTree3(reference, true)));
Timing("four winding queries on existing tree", () => Count(tree));

int Count(DMeshAABBTree3 t) => anchors.Count(p => t.WindingNumber(p) > 0.5);
DMesh3 Subtract(int firstX, int secondX)
{
    var op = new ExactMeshBoolean(stock, Join(Box(firstX, 1, 1, 1), Box(secondX, 1, 1, 1)),
        ExactMeshBoolean.Operations.Difference) { StrictRounding = true };
    if (!op.Compute()) throw new InvalidOperationException(op.Status + ": " + op.Error);
    return op.Result;
}

void Report(string name, DMesh3 mesh)
{
    string reason = MeshRobustRepair.NotCleanReason(mesh);
    if (reason != "") throw new InvalidOperationException(name + ": " + reason);
    var summary = MeshTopologySummary.Compute(mesh);
    var tree = new DMeshAABBTree3(mesh, true);
    string material = string.Concat(anchors.Select(p => tree.WindingNumber(p) > 0.5 ? "1" : "0"));
    double[] shells = MeshConnectedComponents.Separate(mesh).Select(Volume).OrderBy(v => v).ToArray();
    Console.WriteLine($"{name} | {mesh.TriangleCount} | ({summary.Components}, {summary.Genus}, {summary.EulerCharacteristic}) | " +
        $"{SolidBetti.Compute(mesh)} | {Volume(mesh):G12} | [{string.Join(", ", shells.Select(v => v.ToString("G12")))}] | {material}");
}

static DMesh3 Box(double x, double y, double z, double width)
{
    var center = new Vector3d(x, y, z) + (width / 2) * Vector3d.One;
    return new TrivialBox3Generator { Box = new Box3d(center, (width / 2) * Vector3d.One) }.Generate().MakeDMesh();
}

static DMesh3 Join(DMesh3 a, DMesh3 b)
{
    var result = new DMesh3(a);
    new MeshEditor(result).AppendMesh(b);
    return result;
}

static double Volume(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;

static void Timing(string name, Func<int> measure)
{
    const int count = 1000;
    int sum = 0;
    for (int i = 0; i < count; ++i) sum += measure();
    var times = new double[7];
    for (int batch = 0; batch < times.Length; ++batch) {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; ++i) sum += measure();
        times[batch] = sw.Elapsed.TotalMicroseconds / count;
    }
    Array.Sort(times);
    Console.WriteLine($"{name}: {times[3]:F2} us/check (checksum {sum})");
}
