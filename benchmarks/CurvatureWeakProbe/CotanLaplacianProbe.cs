using System;
using g3;
using gs;

internal static class CotanLaplacianProbe
{
    public static void Run()
    {
        Console.WriteLine("family resolution energy-error interior-laplacian-max worst-x worst-y worst-z");
        foreach (int n in new[] { 8, 16, 32, 64 }) {
            Measure("square-fixed", n, MakeSquare(n, false), false);
            Measure("square-checkerboard", n, MakeSquare(n, true), false);
            var sphere = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = n + 1 }.Generate().MakeDMesh();
            Measure("cube-sphere", n, sphere, true);
        }
    }

    private static void Measure(string family, int resolution, DMesh3 mesh, bool sphere)
    {
        var triangulation = new IntrinsicTriangulation(mesh, mollifyFactor: 0);
        var laplacian = triangulation.CotanLaplacian();
        double[] areas = triangulation.VertexAreas();
        var values = new double[triangulation.VertexCount];
        var applied = new double[values.Length];
        for (int i = 0; i < values.Length; ++i) {
            Vector3d p = mesh.GetVertex(triangulation.VertexID(i));
            double coordinate = sphere ? p.z : p.x;
            values[i] = coordinate * coordinate;
        }
        laplacian.Multiply(values, applied);

        double energy = 0, maxError = -1;
        Vector3d worst = Vector3d.Zero;
        for (int i = 0; i < values.Length; ++i) {
            if (!(areas[i] > 0) || !double.IsFinite(areas[i]) || !double.IsFinite(applied[i]))
                throw new InvalidOperationException($"Invalid cotangent row or area at vertex {i}.");
            energy += values[i] * applied[i];
            if (!triangulation.IsBoundaryVertex(i)) {
                double reference = sphere ? 6 * values[i] - 2 : -2;
                double error = Math.Abs(applied[i] / areas[i] - reference);
                if (error > maxError) {
                    maxError = error;
                    worst = mesh.GetVertex(triangulation.VertexID(i));
                }
            }
        }
        double exactEnergy = sphere ? 32 * Math.PI / 15 : 4.0 / 3;
        Console.WriteLine($"{family} {resolution} {Math.Abs(energy - exactEnergy):G8} {maxError:G8} {worst.x:G8} {worst.y:G8} {worst.z:G8}");
    }

    private static DMesh3 MakeSquare(int n, bool checkerboard)
    {
        var mesh = new DMesh3();
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i)
                mesh.AppendVertex(new Vector3d((double)i / n, (double)j / n, 0));

        int stride = n + 1;
        for (int j = 0; j < n; ++j) {
            for (int i = 0; i < n; ++i) {
                int a = j * stride + i, b = a + 1, d = a + stride, c = d + 1;
                if (checkerboard && (i + j) % 2 != 0) {
                    mesh.AppendTriangle(a, b, d);
                    mesh.AppendTriangle(b, c, d);
                } else {
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            }
        }
        if (mesh.TriangleCount != 2 * n * n)
            throw new InvalidOperationException("Expected two triangles per square.");
        return mesh;
    }
}
