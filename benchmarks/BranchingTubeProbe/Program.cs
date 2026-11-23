using System;
using System.Diagnostics;
using g3;
using gs;

Console.WriteLine("gap route setting status components boundary bowties euler genus ms");
foreach (double gap in new[] { 1.0 / 8, 1.0 / 128 }) {
    double armX = 0.25 + gap / 2;
    var tubes = new[] {
        new Tube(true, 0, -0.5, 1, 0.1875, 0.1875),             // crossbar
        new Tube(false, 0, -1.25, 1.125, 0.125, 0.09375),         // stem
        new Tube(false, -armX, 0, 2, 0.125, 0.25),
        new Tube(false, armX, 0, 2, 0.125, 0.25)
    };
    BoundedImplicitFunction3d field = tubes[0];
    for (int i = 1; i < tubes.Length; ++i)
        field = new ImplicitUnion3d { A = field, B = tubes[i] };

    foreach (double cell in new[] { 1.0 / 16, 1.0 / 32 }) {
        foreach (int phase in new[] { 0, 1 }) {
            var shift = new Vector3d(phase * cell / 2, cell / 4, 0);
            var mc = new MarchingCubes {
                Implicit = field, CubeSize = cell, ParallelCompute = false,
                Bounds = new AxisAlignedBox3d(new Vector3d(-1, -1.5, -0.5) + shift, new Vector3d(1, 2.5, 0.5) + shift),
                RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 12
            };
            var clock = Stopwatch.StartNew();
            mc.Generate();
            Report(gap, "mc", $"{1 / cell:G}-phase{phase}", "generated", mc.Mesh, clock.ElapsedMilliseconds);
        }
    }

    var parts = Array.ConvertAll(tubes, tube => tube.MakeMesh());
    DMesh3 result = parts[0];
    string status = "Success";
    var timer = Stopwatch.StartNew();
    for (int i = 1; i < parts.Length; ++i) {
        var op = new ExactMeshBoolean(result, parts[i], ExactMeshBoolean.Operations.Union) { StrictRounding = true };
        bool ok = op.Compute();
        status = op.Status.ToString();
        result = op.Result;
        if (!ok)
            break;
    }
    Report(gap, "boolean", "16", status, result, timer.ElapsedMilliseconds);

    var combined = new DMesh3();
    var editor = new MeshEditor(combined);
    foreach (var part in parts)
        editor.AppendMesh(part);
    var wrap = new MeshAlphaWrap(combined) { Alpha = 0.06, Offset = 0.012 };
    timer.Restart();
    wrap.Compute();
    Report(gap, "wrap", "0.06/0.012", wrap.Status.ToString(), wrap.Result, timer.ElapsedMilliseconds);
}

static void Report(double gap, string route, string setting, string status, DMesh3 mesh, long milliseconds)
{
    if (mesh == null || mesh.TriangleCount == 0) {
        Console.WriteLine($"{gap:G} {route} {setting} {status} - - - - - {milliseconds}");
        return;
    }
    MeshTopologySummary t = MeshTopologySummary.Compute(mesh);
    Console.WriteLine($"{gap:G} {route} {setting} {status} {t.Components} {t.BoundaryEdges} {t.BowtieVertices} " +
        $"{t.EulerCharacteristic} {t.Genus} {milliseconds}");
}

internal sealed class Tube(bool alongX, double lateral, double start, double height, double baseRadius, double tipRadius)
    : BoundedImplicitFunction3d
{
    public double Value(ref Vector3d point)
    {
        double axial = (alongX ? point.x : point.y) - start;
        double side = (alongX ? point.y : point.x) - lateral;
        double radius = baseRadius + (tipRadius - baseRadius) * Math.Clamp(axial / height, 0, 1);
        double radial = Math.Sqrt(side * side + point.z * point.z);
        return Math.Max(Math.Max(-axial, axial - height), radial - radius);
    }

    public AxisAlignedBox3d Bounds()
    {
        double r = Math.Max(baseRadius, tipRadius);
        return alongX
            ? new AxisAlignedBox3d(new Vector3d(start, lateral - r, -r), new Vector3d(start + height, lateral + r, r))
            : new AxisAlignedBox3d(new Vector3d(lateral - r, start, -r), new Vector3d(lateral + r, start + height, r));
    }

    public DMesh3 MakeMesh()
    {
        var mesh = new CappedCylinderGenerator {
            BaseRadius = (float)baseRadius, TopRadius = (float)tipRadius, Height = (float)height, Slices = 16
        }.Generate().MakeDMesh();
        MeshTransforms.PerVertexTransform(mesh, point => alongX
            ? new Vector3d(point.y + start, lateral - point.x, point.z)
            : new Vector3d(point.x + lateral, point.y + start, point.z));
        return mesh;
    }
}
