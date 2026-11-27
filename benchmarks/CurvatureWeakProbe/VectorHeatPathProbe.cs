using System;
using System.Collections.Generic;
using g3;
using gs;

internal static class VectorHeatPathProbe
{
    public static void Run()
    {
        Console.WriteLine("family level cap-path-rms-deg far-path-rms-deg cap-rotation-rms-deg far-rotation-rms-deg");
        foreach (int level in new[] { 9, 17, 33, 65 }) {
            var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = level }.Generate().MakeDMesh();
            Measure(mesh, VectorHeatTransportProbe.FindNorthPole(mesh), "cube", level);
        }
        foreach (int level in new[] { 2, 3, 4, 5 })
            Measure(IcosphereFixture.Make(level), 0, "ico", level);
    }

    private static void Measure(DMesh3 mesh, int source, string family, int level)
    {
        var triangulation = IntrinsicTriangulation.BuildDelaunay(mesh, false, out _);
        int count = triangulation.VertexCount;
        var edges = new Dictionary<int, int>[count];
        var position = new Vector3d[count];
        for (int i = 0; i < count; ++i) {
            edges[i] = new Dictionary<int, int>();
            position[i] = mesh.GetVertex(triangulation.VertexID(i)).Normalized;
        }
        for (int h = 0; h < triangulation.HalfedgeCount; ++h) {
            if (triangulation.CotanWeight(h) > 0)
                edges[triangulation.Tail(h)].Add(triangulation.Head(h), h);
        }

        int seed = triangulation.VertexIndex(source);
        var paths = new DijkstraGraphDistance(count, false, _ => true,
            (a, b) => (float)triangulation.Length(edges[a][b]), a => edges[a].Keys) { TrackOrder = true };
        paths.AddSeed(seed, 0);
        paths.Compute();
        if (paths.GetOrder().Count != count)
            throw new InvalidOperationException("Expected a connected graph of positive-weight edges.");

        Vector3d north = position[seed];
        Vector3d initial = (Vector3d.AxisX - north.x * north).Normalized;
        var alongPath = new Vector3d[count];
        var local = new Vector2d[count];
        alongPath[seed] = initial;
        local[seed] = triangulation.FromWorld(source, initial).Normalized;

        var path = new List<int>();
        int capCount = 0, farCount = 0;
        double capPathError = 0, farPathError = 0, capRotationError = 0, farRotationError = 0;
        foreach (int v in paths.GetOrder()) {
            if (v != seed) {
                path.Clear();
                if (!paths.GetPathToSeed(v, path) || path.Count < 2)
                    throw new InvalidOperationException($"No predecessor for vertex {v}.");
                int parent = path[1];
                alongPath[v] = VectorHeatEdgeProbe.GreatCircleTransport(position[parent], position[v], alongPath[parent]);
                double angle = VectorHeatEdgeProbe.HeadToTailRotation(triangulation, edges[v][parent]);
                double c = Math.Cos(angle), s = Math.Sin(angle);
                Vector2d previous = local[parent];
                local[v] = new Vector2d(c * previous.x - s * previous.y, s * previous.x + c * previous.y);
            }

            Vector3d point = position[v];
            double colatitude = Math.Atan2(north.Cross(point).Length, north.Dot(point));
            if (colatitude > 3.0)
                continue;

            Vector3d direct = VectorHeatEdgeProbe.GreatCircleTransport(north, point, initial);
            Vector3d discrete = triangulation.ToWorld(triangulation.VertexID(v), local[v]);
            discrete -= discrete.Dot(point) * point;
            double pathError = AngleDegrees(direct, alongPath[v]);
            double rotationError = AngleDegrees(alongPath[v], discrete);
            if (colatitude <= 2.4) {
                ++capCount;
                capPathError += pathError * pathError;
                capRotationError += rotationError * rotationError;
            } else {
                ++farCount;
                farPathError += pathError * pathError;
                farRotationError += rotationError * rotationError;
            }
        }
        Console.WriteLine($"{family} {level} {Math.Sqrt(capPathError / capCount):G8} {Math.Sqrt(farPathError / farCount):G8} " +
            $"{Math.Sqrt(capRotationError / capCount):G8} {Math.Sqrt(farRotationError / farCount):G8}");
    }

    private static double AngleDegrees(Vector3d first, Vector3d second)
    {
        return Math.Atan2(first.Cross(second).Length, first.Dot(second)) * 180 / Math.PI;
    }
}
