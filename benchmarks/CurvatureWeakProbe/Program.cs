using System;
using g3;
using gs;

if (Array.IndexOf(args, "--heat-time") >= 0) {
    VectorHeatTransportProbe.RunTimeSweep();
    return;
}

Console.WriteLine("resolution h-max h-mean pairing-error max-nodal-error heat-rms heat-max intrinsic-flips");
foreach (int resolution in new[] { 5, 9, 17, 33, 65 }) {
    var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
    var curvature = MeshCurvature.Compute(mesh);
    if (curvature.ValidVertices != mesh.VertexCount) throw new InvalidOperationException("Invalid vertex");
    double pairing = 0, maxError = 0, h = 0, edgeLengthSum = 0;
    foreach (int v in mesh.VertexIndices()) {
        Vector3d x = mesh.GetVertex(v);
        var field = new Vector3d(x.x * x.x * x.x, x.y * x.y * x.y, x.z * x.z * x.z);
        pairing += curvature.MixedArea[v] * curvature.MeanCurvatureNormal[v].Dot(field);
        maxError = Math.Max(maxError, (curvature.MeanCurvatureNormal[v] - 2 * x).Length);
    }
    foreach (int e in mesh.EdgeIndices()) {
        Index2i ends = mesh.GetEdgeV(e);
        double length = mesh.GetVertex(ends.a).Distance(mesh.GetVertex(ends.b));
        h = Math.Max(h, length);
        edgeLengthSum += length;
    }

    int source = VectorHeatTransportProbe.FindNorthPole(mesh);
    var (distanceRms, distanceMax, flips) = VectorHeatTransportProbe.MeasureScalarDistance(mesh, source, 1);
    Console.WriteLine($"{resolution} {h:G8} {edgeLengthSum / mesh.EdgeCount:G8} {Math.Abs(pairing - 24 * Math.PI / 5):G8} {maxError:G8} " +
        $"{distanceRms:G8} {distanceMax:G8} {flips}");
}

TorusPrincipalCurvatureProbe.Run();
CotanLaplacianProbe.Run();
VectorHeatTransportProbe.Run();
