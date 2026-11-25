using System;
using System.Collections.Generic;
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

    public static void RunTimeSweep(bool icosphere = false)
    {
        Console.WriteLine("schedule resolution h-mean factor vector-cap-rms-deg vector-far-rms-deg scalar-rms scalar-max vector-actual-factor");
        foreach (int resolution in icosphere ? new[] { 2, 3, 4, 5 } : new[] { 9, 17, 33, 65 }) {
            DMesh3 mesh = icosphere ? IcosphereFixture.Make(resolution)
                : new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
            int source = icosphere ? 0 : FindNorthPole(mesh);
            double h = new IntrinsicTriangulation(mesh).MeanEdgeLength;
            foreach (var (schedule, factor) in new[] { ("default", 1.0), ("fourfold", 4.0), ("linear", 0.5 / h) })
                Measure(mesh, resolution, true, factor, schedule, source);
        }
    }

    public static void RunEquatorSweep()
    {
        Console.WriteLine("resolution schedule actual-factor axis-fraction seam-fraction log-axis-over-seam axis-over-y");
        foreach (int resolution in new[] { 9, 17, 33, 65 }) {
            var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
            int pole = FindNorthPole(mesh);
            int axis = FindSphereVertex(mesh, Vector3d.AxisX);
            int seam = FindSphereVertex(mesh, (Vector3d.AxisX + Vector3d.AxisY).Normalized);
            int yAxis = FindSphereVertex(mesh, Vector3d.AxisY);
            double h = new IntrinsicTriangulation(mesh).MeanEdgeLength;
            foreach (var (schedule, factor) in new[] { ("default", 1.0), ("linear", 0.5 / h) }) {
                var heat = new VectorHeatMethod(mesh) { IntrinsicDelaunay = true, TimeFactor = factor };
                double axisShare = KernelShare(heat, pole, axis, seam);
                double seamShare = KernelShare(heat, pole, seam, axis);
                double control = KernelShare(heat, pole, axis, yAxis);
                double mean = heat.Triangulation.MeanEdgeLength;
                double actual = heat.DiffusionTime / (mean * mean);
                if (Math.Abs(axisShare + seamShare - 1) > 1e-10)
                    throw new InvalidOperationException("The complementary source shares must sum to one.");
                Console.WriteLine($"{resolution} {schedule} {actual:G8} {axisShare:G8} {seamShare:G8} " +
                    $"{Math.Log(axisShare / seamShare):G8} {control / (1 - control):G8}");
            }
        }
    }

    private static double KernelShare(VectorHeatMethod heat, int target, int unitSource, int zeroSource)
    {
        var values = new Dictionary<int, double> { [unitSource] = 1, [zeroSource] = 0 };
        double share = heat.ExtendScalar(values)[target];
        double h = heat.Triangulation.MeanEdgeLength;
        if (Math.Abs(heat.DiffusionTime / (h * h * heat.TimeFactor) - 1) > 1e-10)
            throw new InvalidOperationException("The solver changed the diffusion time.");
        if (!(share > 0 && share < 1))
            throw new InvalidOperationException("Expected two positive source contributions.");
        return share;
    }

    public static void RunSourceSweep(bool frameControl = false)
    {
        Console.WriteLine(frameControl
            ? "source resolution vector-cap-rms-deg vector-far-rms-deg frame-cap-rms-deg frame-far-rms-deg frame-max-deg conversion-max-deg"
            : "source resolution h-mean factor vector-cap-rms-deg vector-far-rms-deg scalar-rms scalar-max vector-actual-factor");
        foreach (int resolution in new[] { 9, 17, 33, 65 }) {
            var mesh = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = resolution }.Generate().MakeDMesh();
            Measure(mesh, resolution, true, 1, "cube-pole", FindNorthPole(mesh), frameControl);
            int corner = FindSphereVertex(mesh, Vector3d.One.Normalized);
            Measure(mesh, resolution, true, 1, "cube-corner", corner, frameControl);
        }
        foreach (int subdivisions in new[] { 2, 3, 4, 5 }) {
            DMesh3 mesh = IcosphereFixture.Make(subdivisions);
            // Vertices 0 and 11 bound the first edge split in the fixture, at every subdivision.
            Vector3d midpoint = (mesh.GetVertex(0) + mesh.GetVertex(11)).Normalized;
            Measure(mesh, subdivisions, true, 1, "ico-corner", 0, frameControl);
            Measure(mesh, subdivisions, true, 1, "ico-midpoint", FindSphereVertex(mesh, midpoint), frameControl);
        }
    }

    private static void Measure(DMesh3 mesh, int resolution, bool intrinsicDelaunay,
        double timeFactor = 1, string schedule = null, int? sourceVertex = null, bool frameControl = false)
    {
        int source = sourceVertex ?? FindNorthPole(mesh);
        Vector3d north = mesh.GetVertex(source).Normalized;
        Vector3d initial = (Vector3d.AxisX - north.x * north).Normalized;

        var heat = new VectorHeatMethod(mesh) { IntrinsicDelaunay = intrinsicDelaunay, TimeFactor = timeFactor };
        var triangulation = heat.Triangulation;
        Vector2d[] moved = heat.TransportVector(source, triangulation.FromWorld(source, initial).Normalized);

        int capCount = 0, farCount = 0;
        double capSquared = 0, capMax = 0, farSquared = 0, farMax = 0, lengthMax = 0;
        double frameCapSquared = 0, frameFarSquared = 0, frameMax = 0, conversionMax = 0;
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
            double error = AngleDegrees(expected, actual);
            if (frameControl) {
                Vector3d decoded = triangulation.ToWorld(v, triangulation.FromWorld(v, expected));
                triangulation.GetTangentBasis(v, out _, out _, out Vector3d meshNormal);
                Vector3d directProjection = expected - expected.Dot(meshNormal) * meshNormal;
                conversionMax = Math.Max(conversionMax, AngleDegrees(directProjection, decoded));
                Vector3d roundTrip = decoded - decoded.Dot(p) * p;
                if (!(roundTrip.Length > 0) || !roundTrip.IsFinite)
                    throw new InvalidOperationException($"Invalid frame control at vertex {v}.");
                double frameError = AngleDegrees(expected, roundTrip);
                frameMax = Math.Max(frameMax, frameError);
                if (colatitude <= 2.4)
                    frameCapSquared += frameError * frameError;
                else
                    frameFarSquared += frameError * frameError;
            }
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

        if (frameControl) {
            Console.WriteLine($"{schedule} {resolution} {Math.Sqrt(capSquared / capCount):G8} {Math.Sqrt(farSquared / farCount):G8} " +
                $"{Math.Sqrt(frameCapSquared / capCount):G8} {Math.Sqrt(frameFarSquared / farCount):G8} {frameMax:G8} {conversionMax:G8}");
            return;
        }

        double edgeLengthSum = 0;
        foreach (int e in mesh.EdgeIndices()) {
            Index2i ends = mesh.GetEdgeV(e);
            edgeLengthSum += mesh.GetVertex(ends.a).Distance(mesh.GetVertex(ends.b));
        }
        double heatEdge = triangulation.MeanEdgeLength;
        double diffusionRatio = heat.DiffusionTime / (heatEdge * heatEdge);
        if (schedule != null) {
            var (distanceRms, distanceMax, _) = MeasureScalarDistance(mesh, source, timeFactor);
            Console.WriteLine($"{schedule} {resolution} {edgeLengthSum / mesh.EdgeCount:G8} {timeFactor:G8} " +
                $"{Math.Sqrt(capSquared / capCount):G8} {Math.Sqrt(farSquared / farCount):G8} " +
                $"{distanceRms:G8} {distanceMax:G8} {diffusionRatio:G8}");
            return;
        }

        string mode = intrinsicDelaunay ? "delaunay" : "input";
        Console.WriteLine($"{mode} {resolution} {edgeLengthSum / mesh.EdgeCount:G8} " +
            $"{capCount} {Math.Sqrt(capSquared / capCount):G8} {capMax:G8} " +
            $"{farCount} {Math.Sqrt(farSquared / farCount):G8} {farMax:G8} {lengthMax:G8} {diffusionRatio:G8}");
    }

    private static double AngleDegrees(Vector3d first, Vector3d second)
    {
        return Math.Atan2(first.Cross(second).Length, first.Dot(second)) * 180 / Math.PI;
    }

    internal static int FindNorthPole(DMesh3 mesh)
    {
        return FindSphereVertex(mesh, Vector3d.AxisZ);
    }

    private static int FindSphereVertex(DMesh3 mesh, Vector3d point)
    {
        foreach (int v in mesh.VertexIndices()) {
            if (mesh.GetVertex(v).Distance(point) < 1e-12)
                return v;
        }
        throw new InvalidOperationException($"The sphere has no vertex at {point}.");
    }

    internal static (double Rms, double Maximum, int Flips) MeasureScalarDistance(
        DMesh3 mesh, int source, double timeFactor)
    {
        var heat = new MeshGeodesicDistance(mesh) { TimeFactor = timeFactor };
        if (!heat.Compute(new[] { source }) || heat.Distance[source] != 0)
            throw new InvalidOperationException("Heat distance failed at the source.");

        double squaredError = 0, maxError = 0;
        Vector3d north = mesh.GetVertex(source);
        foreach (int v in mesh.VertexIndices()) {
            Vector3d point = mesh.GetVertex(v);
            double reference = Math.Atan2(north.Cross(point).Length, north.Dot(point));
            double actual = heat.Distance[v];
            if (!double.IsFinite(actual) || actual < 0)
                throw new InvalidOperationException($"Invalid heat distance at vertex {v}.");
            double error = Math.Abs(actual - reference);
            squaredError += error * error;
            maxError = Math.Max(maxError, error);
        }
        return (Math.Sqrt(squaredError / mesh.VertexCount), maxError, heat.IntrinsicFlips);
    }
}
