using System;
using g3;
using gs;

foreach (int resolution in new[] {5, 9, 17, 33, 65}) {
    var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
    var curvature = MeshCurvature.Compute(mesh);
    if (curvature.ValidVertices != mesh.VertexCount) throw new InvalidOperationException("Invalid vertex");
    double pairing = 0, maxError = 0, h = 0;
    foreach (int v in mesh.VertexIndices()) {
        Vector3d x = mesh.GetVertex(v);
        var field = new Vector3d(x.x*x.x*x.x, x.y*x.y*x.y, x.z*x.z*x.z);
        pairing += curvature.MixedArea[v] * curvature.MeanCurvatureNormal[v].Dot(field);
        maxError = Math.Max(maxError, (curvature.MeanCurvatureNormal[v] - 2*x).Length);
    }
    foreach (int e in mesh.EdgeIndices()) {
        Index2i ends = mesh.GetEdgeV(e);
        h = Math.Max(h, mesh.GetVertex(ends.a).Distance(mesh.GetVertex(ends.b)));
    }
    Console.WriteLine($"{resolution} {h:G8} {Math.Abs(pairing - 24*Math.PI/5):G8} {maxError:G8}");
}
