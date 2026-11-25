using System;
using System.Linq;
using g3;
using gs;

if (Array.IndexOf(args, "--heat-equator") >= 0) {
    VectorHeatTransportProbe.RunEquatorSweep();
    return;
}

if (Array.IndexOf(args, "--heat-time") >= 0) {
    VectorHeatTransportProbe.RunTimeSweep();
    return;
}

if (Array.IndexOf(args, "--heat-icosphere") >= 0) {
    VectorHeatTransportProbe.RunTimeSweep(icosphere: true);
    return;
}

if (Array.IndexOf(args, "--heat-sources") >= 0) {
    VectorHeatTransportProbe.RunSourceSweep();
    return;
}

if (Array.IndexOf(args, "--heat-frames") >= 0) {
    VectorHeatTransportProbe.RunSourceSweep(frameControl: true);
    return;
}

if (Array.IndexOf(args, "--heat-edges") >= 0) {
    VectorHeatEdgeProbe.Run();
    return;
}

if (Array.IndexOf(args, "--heat-paths") >= 0) {
    VectorHeatPathProbe.Run();
    return;
}

if (Array.IndexOf(args, "--heat-latitude") >= 0) {
    VectorHeatProfileProbe.Run(latitude: true);
    return;
}

if (Array.IndexOf(args, "--heat-azimuth") >= 0) {
    VectorHeatProfileProbe.Run(azimuth: true);
    return;
}

if (Array.IndexOf(args, "--heat-profile") >= 0) {
    VectorHeatProfileProbe.Run();
    return;
}

Console.WriteLine("resolution h-max h-mean pairing-error max-nodal-error heat-rms heat-max intrinsic-flips");
var localization = new System.Collections.Generic.List<string>();
foreach (int resolution in new[] { 5, 9, 17, 33, 65 }) {
    var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
    var curvature = MeshCurvature.Compute(mesh);
    if (curvature.ValidVertices != mesh.VertexCount) throw new InvalidOperationException("Invalid vertex");
    double pairing = 0, maxError = 0, h = 0, edgeLengthSum = 0;
    // the maximum nodal error per vertex class of the normalized cube: corners (all three |coordinates| equal) by valence,
    // seams (the two largest |coordinates| equal), face interiors; and the class and valence of the overall maximum
    var classMax = new System.Collections.Generic.SortedDictionary<string, double>();
    string argClass = "";
    foreach (int v in mesh.VertexIndices()) {
        Vector3d x = mesh.GetVertex(v);
        var field = new Vector3d(x.x * x.x * x.x, x.y * x.y * x.y, x.z * x.z * x.z);
        pairing += curvature.MixedArea[v] * curvature.MeanCurvatureNormal[v].Dot(field);
        double error = (curvature.MeanCurvatureNormal[v] - 2 * x).Length;
        double[] a = { Math.Abs(x.x), Math.Abs(x.y), Math.Abs(x.z) };
        Array.Sort(a);
        int valence = mesh.GetVtxEdgeCount(v);
        string vertexClass = a[2] - a[0] < 1e-9 ? $"corner-valence-{valence}" : a[2] - a[1] < 1e-9 ? "seam" : "face";
        classMax[vertexClass] = Math.Max(classMax.TryGetValue(vertexClass, out double m) ? m : 0, error);
        if (error > maxError) { maxError = error; argClass = $"{vertexClass} (valence {valence})"; }
    }
    localization.Add($"{resolution} max-at {argClass} " + string.Join(" ", classMax.Select(kv => $"{kv.Key}={kv.Value:G6}")));
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

Console.WriteLine("resolution localization of the maximum nodal error (class maxima)");
foreach (string line in localization) Console.WriteLine(line);

TorusPrincipalCurvatureProbe.Run();
CotanLaplacianProbe.Run();
VectorHeatTransportProbe.Run();
