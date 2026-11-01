using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace Meshing2Acceptance
{
    /// <summary> The production M2 mesher, <see cref="ConstrainedDelaunayMesh2"/>, as seen by the gates. </summary>
    public sealed class ConstrainedDelaunayMesh2Candidate : IMeshing2Candidate
    {
        public string Name => "g3-constrained";

        internal static ConstrainedMesh2Input Convert(Mesh2Input input)
        {
            var result = new ConstrainedMesh2Input();
            for (int i = 0; i < input.Count; ++i) result.Points.Add(new Vector2d(input.X[i], input.Y[i]));
            foreach (var s in input.Segments) result.Segments.Add(new Mesh2Segment(s.A, s.B, s.Marker, s.Locked));
            foreach (var r in input.Rings) result.Rings.Add(new Mesh2Ring(r.Vertices, r.Hole, r.Marker));
            foreach (var h in input.HoleSeeds) result.HoleSeeds.Add(new Vector2d(h[0], h[1]));
            foreach (var g in input.Regions)
                result.Regions.Add(g.Ring != null ? Mesh2Region.ByRing(g.Ring, g.Label, g.MaxArea) : Mesh2Region.BySeed(new Vector2d(g.Seed[0], g.Seed[1]), g.Label, g.MaxArea));
            return result;
        }

        internal static Mesh2Output Output(ConstrainedTriangulation2Result result)
        {
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
            output.Subsegments = mesh.Subsegments.Select(s => new SubsegmentOutput(s.A, s.B, s.Marker)).ToList();
            output.TriangleLabels = mesh.TriangleLabels.ToArray();
            output.BadTriangles = new int[0];
            output.Locate = (x, y) => mesh.TryLocate(new Vector2d(x, y), out int t) ? t : -1;
            return output;
        }

        public Mesh2Output Run(Mesh2Input input)
        {
            var mesher = new ConstrainedDelaunayMesh2 { ResolveCrossings = input.ResolveCrossings };
            return Output(mesher.Compute(Convert(input)));
        }
    }
}
