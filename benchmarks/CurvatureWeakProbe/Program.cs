using System;
using g3;
using gs;

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

    int source = -1;
    foreach (int v in mesh.VertexIndices()) {
        if (mesh.GetVertex(v).Distance(Vector3d.AxisZ) < 1e-12) {
            source = v;
            break;
        }
    }
    if (source < 0) throw new InvalidOperationException("The cube-sphere has no north pole vertex.");

    var heat = new MeshGeodesicDistance(mesh);
    if (!heat.Compute(new[] { source }) || heat.Distance[source] != 0)
        throw new InvalidOperationException("Heat distance failed at the source.");

    double sumSquaredError = 0, maxDistanceError = 0;
    Vector3d north = mesh.GetVertex(source);
    foreach (int v in mesh.VertexIndices()) {
        Vector3d point = mesh.GetVertex(v);
        double reference = Math.Atan2(north.Cross(point).Length, north.Dot(point));
        double distance = heat.Distance[v];
        if (!double.IsFinite(distance) || distance < 0)
            throw new InvalidOperationException($"Invalid heat distance at vertex {v}.");
        double error = Math.Abs(distance - reference);
        sumSquaredError += error * error;
        maxDistanceError = Math.Max(maxDistanceError, error);
    }
    Console.WriteLine($"{resolution} {h:G8} {edgeLengthSum / mesh.EdgeCount:G8} {Math.Abs(pairing - 24 * Math.PI / 5):G8} {maxError:G8} " +
        $"{Math.Sqrt(sumSquaredError / mesh.VertexCount):G8} {maxDistanceError:G8} {heat.IntrinsicFlips}");
}

TorusPrincipalCurvatureProbe.Run();
CotanLaplacianProbe.Run();
