using System;
using g3;
using gs;

internal static class TorusPrincipalCurvatureProbe
{
    private const double MajorRadius = 1;
    private const double MinorRadius = 0.35;

    public static void Run()
    {
        Console.WriteLine("diagonals nu nv h-mean k1-rms k1-max k2-rms k2-max direction-max-deg normal-max-deg");
        foreach (bool checkerboard in new[] { false, true }) {
            foreach (int nu in new[] { 16, 32, 64, 128, 256 }) {
                int nv = nu / 2;
                var mesh = MakeTorus(nu, nv, checkerboard);
                var curvature = MeshPrincipalCurvature.Compute(mesh);
                double sum1 = 0, sum2 = 0, max1 = 0, max2 = 0, maxDirection = 0, maxNormal = 0;
                foreach (int v in mesh.VertexIndices()) {
                    if (!curvature.Valid[v] || !double.IsFinite(curvature.K1[v]) || !double.IsFinite(curvature.K2[v]))
                        throw new InvalidOperationException($"Invalid principal curvature at vertex {v}.");

                    Vector3d point = mesh.GetVertex(v);
                    double rho = Math.Sqrt(point.x * point.x + point.y * point.y);
                    double error1 = Math.Abs(curvature.K1[v] - 1 / MinorRadius);
                    double error2 = Math.Abs(curvature.K2[v] - (rho - MajorRadius) / (MinorRadius * rho));
                    sum1 += error1 * error1;
                    sum2 += error2 * error2;
                    max1 = Math.Max(max1, error1);
                    max2 = Math.Max(max2, error2);

                    var azimuth = new Vector3d(-point.y / rho, point.x / rho, 0);
                    Vector3d direction = curvature.Dir2[v];
                    double angle = Math.Atan2(direction.Cross(azimuth).Length, Math.Abs(direction.Dot(azimuth)));
                    if (!double.IsFinite(angle) || !(direction.LengthSquared > 0))
                        throw new InvalidOperationException($"Invalid principal direction at vertex {v}.");
                    maxDirection = Math.Max(maxDirection, angle);

                    var exactNormal = new Vector3d((rho - MajorRadius) * point.x / rho,
                        (rho - MajorRadius) * point.y / rho, point.z) / MinorRadius;
                    Vector3d normal = curvature.Normal[v];
                    double normalAngle = Math.Atan2(normal.Cross(exactNormal).Length, normal.Dot(exactNormal));
                    if (!double.IsFinite(normalAngle) || !(normal.LengthSquared > 0))
                        throw new InvalidOperationException($"Invalid vertex normal at vertex {v}.");
                    maxNormal = Math.Max(maxNormal, normalAngle);
                }

                double edgeLengthSum = 0;
                foreach (int e in mesh.EdgeIndices()) {
                    Index2i ends = mesh.GetEdgeV(e);
                    edgeLengthSum += mesh.GetVertex(ends.a).Distance(mesh.GetVertex(ends.b));
                }
                string layout = checkerboard ? "checkerboard" : "fixed";
                Console.WriteLine($"{layout} {nu} {nv} {edgeLengthSum / mesh.EdgeCount:G8} " +
                    $"{Math.Sqrt(sum1 / mesh.VertexCount):G8} {max1:G8} " +
                    $"{Math.Sqrt(sum2 / mesh.VertexCount):G8} {max2:G8} {maxDirection * 180 / Math.PI:G8} {maxNormal * 180 / Math.PI:G8}");
            }
        }
    }

    private static DMesh3 MakeTorus(int nu, int nv, bool checkerboard)
    {
        var mesh = new DMesh3();
        var ids = new int[nu, nv];
        for (int i = 0; i < nu; ++i) {
            for (int j = 0; j < nv; ++j) {
                double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                double rho = MajorRadius + MinorRadius * Math.Cos(v);
                ids[i, j] = mesh.AppendVertex(new Vector3d(rho * Math.Cos(u), rho * Math.Sin(u), MinorRadius * Math.Sin(v)));
            }
        }
        for (int i = 0; i < nu; ++i) {
            int nextI = (i + 1) % nu;
            for (int j = 0; j < nv; ++j) {
                int nextJ = (j + 1) % nv;
                int a = ids[i, j], b = ids[nextI, j], c = ids[nextI, nextJ], d = ids[i, nextJ];
                if (checkerboard && (i + j) % 2 != 0) {
                    mesh.AppendTriangle(a, b, d);
                    mesh.AppendTriangle(b, c, d);
                } else {
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            }
        }
        if (mesh.TriangleCount != 2 * nu * nv || !mesh.IsClosed())
            throw new InvalidOperationException("Expected a closed triangulated torus.");
        return mesh;
    }
}
