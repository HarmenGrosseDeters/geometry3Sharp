using System;
using System.Diagnostics;
using g3;
using gs;

Console.WriteLine("case route setting status expected-components components boundary-edges bowties euler volume-relative-error area-relative-error ms");
foreach (var (name, count, spacing) in new[] {
    ("overlap", 2, 1.5), ("thin-neck", 2, 2 - 1.0 / 128),
    ("small-gap", 2, 2 + 1.0 / 128), ("chain", 3, 1.5)
}) {
    // Only adjacent pairs can overlap, and there is no triple intersection.
    double capHeight = Math.Max(0, 1 - spacing / 2);
    double capVolume = Math.PI * capHeight * capHeight * (1 - capHeight / 3);
    double volume = count * (4 * Math.PI / 3) - (count - 1) * 2 * capVolume;
    double area = count * 4 * Math.PI - (count - 1) * 4 * Math.PI * capHeight;
    int expectedComponents = spacing < 2 ? 1 : count;
    Vector3d Center(int i) => new Vector3d((i - (count - 1) / 2.0) * spacing, 0, 0);

    BoundedImplicitFunction3d field = new ImplicitSphere3d { Origin = Center(0), Radius = 1 };
    for (int i = 1; i < count; ++i)
        field = new ImplicitUnion3d { A = field, B = new ImplicitSphere3d { Origin = Center(i), Radius = 1 } };

    foreach (double cell in new[] { 1.0 / 16, 1.0 / 32 }) {
        foreach (int phase in new[] { 0, 1 }) {
            var shift = new Vector3d(phase * cell / 2, 0, 0);
            var mc = new MarchingCubes {
                Implicit = field, CubeSize = cell, ParallelCompute = false,
                Bounds = new AxisAlignedBox3d(new Vector3d(-4, -2, -2) + shift, new Vector3d(4, 2, 2) + shift),
                RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 10
            };
            var clock = Stopwatch.StartNew();
            mc.Generate();
            Report(name, "mc", $"{1 / cell:G}-phase{phase}", "generated", mc.Mesh,
                expectedComponents, volume, area, clock.ElapsedMilliseconds);
        }
    }

    var parts = new DMesh3[count];
    for (int i = 0; i < count; ++i) {
        parts[i] = new Sphere3Generator_NormalizedCube { EdgeVertices = 9, Radius = 1 }.Generate().MakeDMesh();
        MeshTransforms.Translate(parts[i], Center(i));
    }

    DMesh3 result = parts[0];
    string status = "Success";
    var timer = Stopwatch.StartNew();
    for (int i = 1; i < count; ++i) {
        var op = new ExactMeshBoolean(result, parts[i], ExactMeshBoolean.Operations.Union) { StrictRounding = true };
        bool ok = op.Compute();
        status = op.Status.ToString();
        result = op.Result;
        if (!ok)
            break;
    }
    Report(name, "boolean", "9", status, result, expectedComponents, volume, area, timer.ElapsedMilliseconds);

    var appended = new DMesh3();
    var editor = new MeshEditor(appended);
    foreach (var part in parts)
        editor.AppendMesh(part);
    var wrap = new MeshAlphaWrap(appended) { Alpha = 0.20, Offset = 0.02 };
    timer.Restart();
    wrap.Compute();
    Report(name, "wrap", "0.20/0.02", wrap.Status.ToString(), wrap.Result, expectedComponents, volume, area, timer.ElapsedMilliseconds);
}

static void Report(string name, string route, string setting, string status, DMesh3 mesh,
    int expectedComponents, double volume, double area, long milliseconds)
{
    if (mesh == null || mesh.TriangleCount == 0) {
        Console.WriteLine($"{name} {route} {setting} {status} {expectedComponents} - - - - - - {milliseconds}");
        return;
    }

    var topology = MeshTopologySummary.Compute(mesh);
    Vector2d measured = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
    Console.WriteLine($"{name} {route} {setting} {status} {expectedComponents} {topology.Components} " +
        $"{topology.BoundaryEdges} {topology.BowtieVertices} {topology.EulerCharacteristic} " +
        $"{(measured.x - volume) / volume:G6} {(measured.y - area) / area:G6} {milliseconds}");
}
