using System;
using g3;

Console.WriteLine("height edge-step distance-to-edge-witness midplane-radius tree-midpoint-distance");
foreach (double height in new[] { 1.0, Math.ScaleB(1.0, -27), Math.ScaleB(1.0, -44), Math.ScaleB(1.0, -50) }) {
    var box = new Box3d(new Vector3d(0.5, 0.5, height / 2), new Vector3d(0.5, 0.5, height / 2));
    var mesh = new TrivialBox3Generator { Box = box }.Generate().MakeDMesh();
    if (!mesh.IsClosed() || mesh.TriangleCount != 12)
        throw new InvalidOperationException("Expected a closed triangulated box.");

    var midpoint = new Vector3d(0.5, 0.5, height / 2);
    CheckContact(mesh, midpoint, new Vector3d(0.5, 0.5, 0), axis: 2, radius: height / 2);
    CheckContact(mesh, midpoint, new Vector3d(0.5, 0.5, height), axis: 2, radius: height / 2);
    var tree = new DMeshAABBTree3(mesh, true);
    if (tree.FindNearestTriangle(midpoint, out double midpointDistanceSquared) < 0)
        throw new InvalidOperationException("Expected a nearest triangle for the midpoint diagnostic.");
    double treeMidpointDistance = Math.Sqrt(midpointDistanceSquared);
    foreach (int exponent in new[] { -2, -10, -20 }) {
        double step = Math.ScaleB(height, exponent);
        var center = new Vector3d(step, 0.5, step);
        CheckContact(mesh, center, new Vector3d(0, 0.5, step), axis: 0, radius: step);
        CheckContact(mesh, center, new Vector3d(step, 0.5, 0), axis: 2, radius: step);
        double distanceToEdge = center.Distance(new Vector3d(0, 0.5, 0));
        Console.WriteLine($"{height:G9} {step:G9} {distanceToEdge:G9} {height / 2:G9} {treeMidpointDistance:G9}");
    }
}

static void CheckContact(DMesh3 mesh, Vector3d center, Vector3d point, int axis, double radius)
{
    if (center.DistanceSquared(point) != radius * radius)
        throw new InvalidOperationException("Unexpected witness radius.");

    int u = (axis + 1) % 3, v = (axis + 2) % 3;
    var projected = new Vector2d(point[u], point[v]);
    foreach (int tid in mesh.TriangleIndices()) {
        Index3i tri = mesh.GetTriangle(tid);
        Vector3d a = mesh.GetVertex(tri.a), b = mesh.GetVertex(tri.b), c = mesh.GetVertex(tri.c);
        if (a[axis] != point[axis] || b[axis] != point[axis] || c[axis] != point[axis])
            continue;

        var pa = new Vector2d(a[u], a[v]);
        var pb = new Vector2d(b[u], b[v]);
        var pc = new Vector2d(c[u], c[v]);
        int winding = ExactPredicates.Orient2d(pa, pb, pc);
        if (winding != 0 &&
            winding * ExactPredicates.Orient2d(pa, pb, projected) >= 0 &&
            winding * ExactPredicates.Orient2d(pb, pc, projected) >= 0 &&
            winding * ExactPredicates.Orient2d(pc, pa, projected) >= 0)
            return;
    }
    throw new InvalidOperationException($"Face contact {point} was not on the mesh.");
}
