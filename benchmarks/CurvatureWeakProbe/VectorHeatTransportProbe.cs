using System;
using g3;
using gs;

internal static class VectorHeatTransportProbe
{
    public static void Run()
    {
        Console.WriteLine("triangulation resolution h-mean cap-count cap-rms-deg cap-max-deg far-count far-rms-deg far-max-deg length-max diffusion-ratio");
        foreach (int resolution in new[] { 5, 9, 17, 33, 65 }) {
            var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
            Measure(mesh, resolution, true);
            Measure(mesh, resolution, false);
        }
    }

    private static void Measure(DMesh3 mesh, int resolution, bool intrinsicDelaunay)
    {
        Vector3d north = Vector3d.AxisZ, initial = Vector3d.AxisX;
        int source = -1;
        foreach (int v in mesh.VertexIndices()) {
            if (mesh.GetVertex(v).Distance(north) < 1e-12) {
                source = v;
                break;
            }
        }
        if (source < 0)
            throw new InvalidOperationException("The cube-sphere has no north pole vertex.");

        var heat = new VectorHeatMethod(mesh) { IntrinsicDelaunay = intrinsicDelaunay };
        var triangulation = heat.Triangulation;
        Vector2d[] moved = heat.TransportVector(source, triangulation.FromWorld(source, initial).Normalized);

        int capCount = 0, farCount = 0;
        double capSquared = 0, capMax = 0, farSquared = 0, farMax = 0, lengthMax = 0;
        foreach (int v in mesh.VertexIndices()) {
            Vector3d p = mesh.GetVertex(v).Normalized;
            double colatitude = Math.Atan2(north.Cross(p).Length, north.Dot(p));
            if (colatitude > 3.0)
                continue;

            double length = moved[v].Length;
            Vector3d actual = triangulation.ToWorld(v, moved[v]);
            actual -= actual.Dot(p) * p;
            if (!double.IsFinite(length) || !(length > 0) || !(actual.Length > 0) || !actual.IsFinite)
                throw new InvalidOperationException($"Invalid transported vector at vertex {v}.");

            // Rotation along the unique short great-circle arc, expressed without a rotation axis at the source.
            Vector3d expected = initial - initial.Dot(p) / (1 + north.Dot(p)) * (north + p);
            double error = Math.Atan2(expected.Cross(actual).Length, expected.Dot(actual)) * 180 / Math.PI;
            lengthMax = Math.Max(lengthMax, Math.Abs(length - 1));
            if (colatitude <= 2.4) {
                capCount++;
                capSquared += error * error;
                capMax = Math.Max(capMax, error);
            } else {
                farCount++;
                farSquared += error * error;
                farMax = Math.Max(farMax, error);
            }
        }
        if (capCount == 0 || farCount == 0)
            throw new InvalidOperationException("Expected vertices in both transport regions.");

        double edgeLengthSum = 0;
        foreach (int e in mesh.EdgeIndices()) {
            Index2i ends = mesh.GetEdgeV(e);
            edgeLengthSum += mesh.GetVertex(ends.a).Distance(mesh.GetVertex(ends.b));
        }
        double heatEdge = triangulation.MeanEdgeLength;
        double diffusionRatio = heat.DiffusionTime / (heatEdge * heatEdge);
        string mode = intrinsicDelaunay ? "delaunay" : "input";
        Console.WriteLine($"{mode} {resolution} {edgeLengthSum / mesh.EdgeCount:G8} " +
            $"{capCount} {Math.Sqrt(capSquared / capCount):G8} {capMax:G8} " +
            $"{farCount} {Math.Sqrt(farSquared / farCount):G8} {farMax:G8} {lengthMax:G8} {diffusionRatio:G8}");
    }
}
