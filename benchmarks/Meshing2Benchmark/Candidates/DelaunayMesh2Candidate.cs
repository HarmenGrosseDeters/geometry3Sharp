using System;
using System.Linq;
using g3;

namespace Meshing2Acceptance
{
    /// <summary> The production M1 mesher, <see cref="DelaunayMesh2"/>, as seen by the gates. </summary>
    public sealed class DelaunayMesh2Candidate : IMeshing2Candidate
    {
        public string Name => "g3-delaunay";

        public Mesh2Output Run(Mesh2Input input)
        {
            var points = new Vector2d[input.Count];
            for (int i = 0; i < points.Length; ++i) points[i] = new Vector2d(input.X[i], input.Y[i]);
            var result = new DelaunayMesh2().Compute(points);
            var output = new Mesh2Output();
            if (result.Status == Triangulation2Status.Failed) {
                output.Status = Mesh2Status.Failed;
                output.FailureKind = result.FailureKind.ToString();
                return output;
            }
            var mesh = result.Mesh;
            output.Status = result.Status == Triangulation2Status.Empty ? Mesh2Status.Empty : Mesh2Status.Success;
            output.VX = mesh.Vertices.Select(p => p.x).ToArray();
            output.VY = mesh.Vertices.Select(p => p.y).ToArray();
            output.InputToVertex = mesh.InputToVertex.ToArray();
            output.Triangles = mesh.Triangles.Select(t => new[] { t.a, t.b, t.c }).ToArray();
            output.Neighbours = mesh.Neighbours.Select(t => new[] { t.a, t.b, t.c }).ToArray();
            output.Locate = (x, y) => mesh.TryLocate(new Vector2d(x, y), out int t) ? t : -1;
            return output;
        }
    }
}
