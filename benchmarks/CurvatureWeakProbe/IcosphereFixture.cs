using System;
using System.Collections.Generic;
using g3;

internal static class IcosphereFixture
{
    // The midpoint projection fixture from MeshCurvatureTests.
    internal static DMesh3 Make(int subdivisions)
    {
        double goldenRatio = (1 + Math.Sqrt(5)) / 2;
        var mesh = new DMesh3();
        Vector3d[] vertices = {
            new(-1, goldenRatio, 0), new(1, goldenRatio, 0), new(-1, -goldenRatio, 0), new(1, -goldenRatio, 0),
            new(0, -1, goldenRatio), new(0, 1, goldenRatio), new(0, -1, -goldenRatio), new(0, 1, -goldenRatio),
            new(goldenRatio, 0, -1), new(goldenRatio, 0, 1), new(-goldenRatio, 0, -1), new(-goldenRatio, 0, 1)
        };
        foreach (Vector3d vertex in vertices)
            mesh.AppendVertex(vertex.Normalized);

        int[] faces = {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };
        for (int i = 0; i < faces.Length; i += 3)
            mesh.AppendTriangle(faces[i], faces[i + 1], faces[i + 2]);

        for (int level = 0; level < subdivisions; ++level) {
            var next = new DMesh3();
            foreach (Vector3d vertex in mesh.Vertices())
                next.AppendVertex(vertex);

            var midpoints = new Dictionary<(int, int), int>();
            int Midpoint(int a, int b)
            {
                var edge = a < b ? (a, b) : (b, a);
                if (!midpoints.TryGetValue(edge, out int id)) {
                    id = next.AppendVertex((mesh.GetVertex(a) + mesh.GetVertex(b)).Normalized);
                    midpoints.Add(edge, id);
                }
                return id;
            }

            foreach (int tid in mesh.TriangleIndices()) {
                Index3i triangle = mesh.GetTriangle(tid);
                int ab = Midpoint(triangle.a, triangle.b);
                int bc = Midpoint(triangle.b, triangle.c);
                int ca = Midpoint(triangle.c, triangle.a);
                next.AppendTriangle(triangle.a, ab, ca);
                next.AppendTriangle(ab, triangle.b, bc);
                next.AppendTriangle(ca, bc, triangle.c);
                next.AppendTriangle(ab, bc, ca);
            }
            mesh = next;
        }
        return mesh;
    }
}
