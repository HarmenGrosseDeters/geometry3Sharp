using System.Collections.Generic;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Exact 2D Delaunay triangulation (DelaunayMesh2), constrained Delaunay (ConstrainedDelaunayMesh2),
    /// and Ruppert/Shewchuk quality refinement (QualityMesh2) with holes, regions, and 2.5D DMesh3 export.
    /// </summary>
    public static class Meshing2DExample
    {
        /// <summary> Plain 2D Delaunay triangulation of a point set. </summary>
        public static Triangulation2Result DelaunayPoints()
        {
            var pts = new List<Vector2d> {
                new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 3), new Vector2d(0, 3),
                new Vector2d(2, 1.5)
            };
            return new DelaunayMesh2().Compute(pts);
        }

        /// <summary>
        /// Quality-refined 2D mesh of a 10x6 rectangle with a 2x2 square hole, a dividing constraint segment,
        /// per-region area limits, and interpolated vertex heights converted to a 2.5D DMesh3.
        /// </summary>
        public static ConstrainedTriangulation2Result QualityDomainWithHole(out DMesh3 mesh3d)
        {
            var input = new ConstrainedMesh2Input {
                Points = {
                    new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(0, 6),
                    new Vector2d(4, 2), new Vector2d(6, 2), new Vector2d(6, 4), new Vector2d(4, 4),
                    new Vector2d(7, 0), new Vector2d(7, 6)
                },
                Heights = new List<double> { 0, 10, 16, 6, 6, 8, 10, 8, 7, 13 },
                Rings = {
                    new Mesh2Ring(new[] { 0, 1, 2, 3 }),
                    new Mesh2Ring(new[] { 4, 5, 6, 7 }, hole: true)
                },
                Segments = { new Mesh2Segment(8, 9, marker: 5) },
                Regions = {
                    Mesh2Region.BySeed(new Vector2d(1, 1), label: 1, maxArea: 2.0),
                    Mesh2Region.BySeed(new Vector2d(9, 1), label: 2, maxArea: 0.5)
                }
            };

            var qm = new QualityMesh2 { MinAngle = 25, MaxArea = 2.0 };
            ConstrainedTriangulation2Result res = qm.Compute(input);
            mesh3d = res.Status == Triangulation2Status.Success ? res.Mesh.ToDMesh3(labelsAsGroups: true) : null;
            return res;
        }
    }
}
