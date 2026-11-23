using System;
using g3;
using gs;

internal static class VectorHeatEdgeProbe
{
    public static void Run()
    {
        Console.WriteLine("family level connectivity edges rms-deg max-deg inserted-edges inserted-rms-deg");
        foreach (int level in new[] { 9, 17, 33, 65 }) {
            var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = level }.Generate().MakeDMesh();
            MeasureBoth(mesh, "cube", level);
        }
        foreach (int level in new[] { 2, 3, 4, 5 })
            MeasureBoth(IcosphereFixture.Make(level), "ico", level);
    }

    private static void MeasureBoth(DMesh3 mesh, string family, int level)
    {
        Measure(mesh, new IntrinsicTriangulation(mesh), family, level, "input");
        Measure(mesh, IntrinsicTriangulation.BuildDelaunay(mesh, false, out _), family, level, "delaunay");
    }

    private static void Measure(DMesh3 mesh, IntrinsicTriangulation triangulation, string family, int level, string connectivity)
    {
        int edgeCount = 0, insertedCount = 0;
        double squaredError = 0, maxError = 0, insertedSquaredError = 0;
        for (int h = 0; h < triangulation.HalfedgeCount; ++h) {
            // The heat operator clamps nonpositive cotangent weights to zero.
            if (triangulation.Twin(h) < h || !(triangulation.CotanWeight(h) > 0))
                continue;

            int from = triangulation.VertexID(triangulation.Head(h));
            int to = triangulation.VertexID(triangulation.Tail(h));
            Vector3d start = mesh.GetVertex(from).Normalized, finish = mesh.GetVertex(to).Normalized;
            Vector3d across = start.Cross(finish).Normalized;
            Vector3d forward = across.Cross(start);
            double angle = triangulation.Signpost(h) - triangulation.ReverseSignpost(h) + Math.PI;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);

            double tangentError = MeasureDirection(triangulation, from, to, start, finish, forward, cos, sin);
            double mixedError = MeasureDirection(triangulation, from, to, start, finish, forward + across, cos, sin);
            double edgeSquaredError = tangentError * tangentError + mixedError * mixedError;
            squaredError += edgeSquaredError;
            maxError = Math.Max(maxError, Math.Max(tangentError, mixedError));
            ++edgeCount;
            if (mesh.FindEdge(from, to) < 0) {
                ++insertedCount;
                insertedSquaredError += edgeSquaredError;
            }
        }

        string insertedRms = insertedCount == 0 ? "-" : $"{Math.Sqrt(insertedSquaredError / (2 * insertedCount)):G8}";
        Console.WriteLine($"{family} {level} {connectivity} {edgeCount} " +
            $"{Math.Sqrt(squaredError / (2 * edgeCount)):G8} {maxError:G8} {insertedCount} {insertedRms}");
    }

    private static double MeasureDirection(IntrinsicTriangulation triangulation, int from, int to,
        Vector3d start, Vector3d finish, Vector3d initial, double cos, double sin)
    {
        Vector2d local = triangulation.FromWorld(from, initial);
        var rotated = new Vector2d(cos * local.x - sin * local.y, sin * local.x + cos * local.y);
        Vector3d actual = triangulation.ToWorld(to, rotated);
        actual -= actual.Dot(finish) * finish;
        Vector3d expected = initial - initial.Dot(finish) / (1 + start.Dot(finish)) * (start + finish);
        if (!(actual.Length > 0) || !actual.IsFinite)
            throw new InvalidOperationException($"Invalid edge transport from vertex {from} to {to}.");
        return Math.Atan2(expected.Cross(actual).Length, expected.Dot(actual)) * 180 / Math.PI;
    }
}
