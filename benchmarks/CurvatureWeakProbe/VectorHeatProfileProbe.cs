using System;
using g3;
using gs;

internal static class VectorHeatProfileProbe
{
    public static void Run(bool azimuth = false, bool latitude = false)
    {
        const int intervals = 8;
        // CubeMapping of (1/2, 1/4, -1), the target of the last-edge control.
        const double x = 0.5, y = 0.25;
        var target = new Vector3d(x * Math.Sqrt(0.5 - y * y / 6), y * Math.Sqrt(0.5 - x * x / 6),
            -Math.Sqrt(1 - (x * x + y * y) / 2 + x * x * y * y / 3));
        Vector3d radial = new Vector3d(target.x, target.y, 0).Normalized;
        double arc = Math.Atan2(Vector3d.AxisZ.Cross(target).Length, target.z);
        var coarse = MakeMesh(17);
        var samples = new Vector3d[intervals + 1];
        Console.WriteLine(latitude ? "resolution schedule sample longitude-deg interpolated-error-deg" : azimuth
            ? "resolution schedule ring vertex azimuth-deg signed-error-deg"
            : "resolution schedule station colatitude-deg station-offset-deg signed-error-deg");
        for (int i = 0; i <= intervals; ++i) {
            double theta = arc * i / intervals;
            samples[i] = Math.Cos(theta) * Vector3d.AxisZ + Math.Sin(theta) * radial;
        }

        // Snap once on the coarsest mesh; all refinements contain these physical vertices.
        var stations = new Vector3d[samples.Length];
        for (int i = 0; i < samples.Length; ++i)
            stations[i] = coarse.GetVertex(MeshQueries.FindNearestVertex_LinearSearch(coarse, samples[i]));
        foreach (int resolution in new[] { 17, 33, 65 }) {
            var mesh = resolution == 17 ? coarse : MakeMesh(resolution);
            int source = VectorHeatTransportProbe.FindNorthPole(mesh);
            double h = new IntrinsicTriangulation(mesh).MeanEdgeLength;
            foreach (var (schedule, factor) in new[] { ("default", 1.0), ("linear", 0.5 / h) }) {
                var heat = new VectorHeatMethod(mesh) { IntrinsicDelaunay = true, TimeFactor = factor };
                var tri = heat.Triangulation;
                var moved = heat.TransportVector(source, tri.FromWorld(source, Vector3d.AxisX).Normalized);
                if (Math.Abs(heat.DiffusionTime / (tri.MeanEdgeLength * tri.MeanEdgeLength * factor) - 1) > 1e-10)
                    throw new InvalidOperationException("The solver changed the requested time.");
                if (latitude) {
                    SampleLatitude(mesh, tri, moved, target, resolution, schedule);
                    continue;
                }
                if (azimuth) {
                    foreach (int vertex in mesh.VertexIndices()) {
                        Vector3d p = mesh.GetVertex(vertex).Normalized;
                        bool equator = Math.Abs(p.z) < 1e-12;
                        if (!equator && Math.Abs(p.z - target.z) > 1e-12) continue;
                        double longitude = Math.Atan2(p.y, p.x) * MathUtil.Rad2Deg;
                        double error = SignedError(tri, vertex, p, moved[vertex]);
                        Console.WriteLine($"{resolution} {schedule} {(equator ? "equator" : "target")} " +
                            $"{vertex} {longitude:G9} {error:G9}");
                    }
                    continue;
                }
                for (int i = 0; i < stations.Length; ++i) {
                    int vertex = MeshQueries.FindNearestVertex_LinearSearch(mesh, stations[i]);
                    if (mesh.GetVertex(vertex).Distance(stations[i]) > 1e-12)
                        throw new InvalidOperationException("Missing nested station.");
                    Vector3d p = stations[i].Normalized;
                    double error = SignedError(tri, vertex, p, moved[vertex]);
                    double offset = Math.Atan2(p.Cross(samples[i]).Length, p.Dot(samples[i]));
                    double theta = Math.Atan2(Vector3d.AxisZ.Cross(p).Length, p.z);
                    Console.WriteLine($"{resolution} {schedule} {i} {theta * MathUtil.Rad2Deg:G9} " +
                        $"{offset * MathUtil.Rad2Deg:G9} {error:G9}");
                }
            }
        }
    }

    private static void SampleLatitude(DMesh3 mesh, IntrinsicTriangulation tri, Vector2d[] moved,
        Vector3d target, int resolution, string schedule)
    {
        var tree = new DMeshAABBTree3(mesh, true);
        double radius = Math.Sqrt(1 - target.z * target.z);
        double Sample(Vector3d direction) {
            var ray = new Ray3d(Vector3d.Zero, direction.Normalized);
            int tid = tree.FindNearestHitTriangle(ray);
            var hit = MeshQueries.TriangleIntersection(mesh, tid, ray);
            if (hit == null || hit.Result != IntersectionResult.Intersects)
                throw new InvalidOperationException("Missing latitude ray intersection.");
            Index3i vertices = mesh.GetTriangle(tid);
            Vector3d weights = hit.TriangleBaryCoords;
            double error = 0;
            for (int j = 0; j < 3; ++j) {
                int v = vertices[j];
                double angle = SignedError(tri, v, mesh.GetVertex(v).Normalized, moved[v]);
                if (!(Math.Abs(angle) < 90))
                    throw new InvalidOperationException("Signed errors must lie in one angular chart.");
                error += weights[j] * angle;
            }
            return error;
        }
        int anchors = 0;
        foreach (int v in mesh.VertexIndices()) {
            Vector3d p = mesh.GetVertex(v).Normalized;
            if (Math.Abs(p.z - target.z) > 1e-12) continue;
            if (Math.Abs(Sample(p) - SignedError(tri, v, p, moved[v])) > 1e-8)
                throw new InvalidOperationException("Latitude interpolation missed a measured vertex.");
            ++anchors;
        }
        if (anchors != 8) throw new InvalidOperationException("Expected eight target-latitude vertices.");
        const int count = 256;
        for (int i = 0; i < count; ++i) {
            double longitude = 2 * Math.PI * i / count;
            double error = Sample(new Vector3d(radius * Math.Cos(longitude), radius * Math.Sin(longitude), target.z));
            Console.WriteLine($"{resolution} {schedule} {i} {360.0 * i / count:G12} {error:G12}");
        }
    }

    private static double SignedError(IntrinsicTriangulation tri, int vertex, Vector3d p, Vector2d direction)
    {
        Vector3d actual = tri.ToWorld(vertex, direction);
        actual -= actual.Dot(p) * p;
        if (!(actual.Length > 0) || !actual.IsFinite)
            throw new InvalidOperationException("Invalid transported direction.");
        Vector3d exact = VectorHeatEdgeProbe.GreatCircleTransport(Vector3d.AxisZ, p, Vector3d.AxisX);
        return Math.Atan2(p.Dot(exact.Cross(actual)), exact.Dot(actual)) * MathUtil.Rad2Deg;
    }

    private static DMesh3 MakeMesh(int resolution)
    {
        return new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
    }
}
