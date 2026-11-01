// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// Sizing fields for Remesher.TargetEdgeLengthF.
    /// </summary>
    public static class RemeshSizing
    {
        /// <summary>
        /// Target edge length from the local wall thickness of input (MeshWallThickness, InscribedSphere: the diameter
        /// of the largest ball inside the solid at the point): fraction times the thickness, but at least minEdgeLength
        /// (default: 1e-3 of the bounding box diagonal, so a sliver of zero thickness does not refine without bound).
        /// The thickness of a vertex of input is the smallest of its triangles' (a rim between two walls gets the
        /// thinner one), and the field at a point is interpolated over the input triangle nearest to it, from its
        /// vertices: continuous, so a point on an edge or vertex of input gets the same value whichever of its
        /// triangles is the nearest. Vertices without any thickness (NaN: open parts, degenerate triangles) are left
        /// out of the interpolation; NaN if none has one, which Remesher reads as the global target. Remesher caps the
        /// field by its global lengths, so with it the edges near walls thinner than the global target over fraction
        /// are shorter than the wall (the walls are not folded through each other), and elsewhere as before. Input is
        /// copied (the remesher may change the mesh it got). Relative values only: scale invariant. Null if the
        /// thickness cannot be computed (empty mesh).
        /// </summary>
        public static Func<Vector3d, double> FromWallThickness(DMesh3 input, double fraction = 1.0, double minEdgeLength = -1)
        {
            DMesh3 copy = new DMesh3(input);
            var wt = new MeshWallThickness(copy) { Method = MeshWallThickness.Methods.InscribedSphere, ComputeVertices = false };
            if (wt.Compute() == false)
                return null;
            double[] tt = wt.TriangleThickness;
            double[] vt = new double[copy.MaxVertexID];
            for (int v = 0; v < vt.Length; ++v)
                vt[v] = double.NaN;
            foreach (int tid in copy.TriangleIndices()) {
                double t = tt[tid];
                if (!(t > 0) || double.IsInfinity(t))
                    continue;
                Index3i tv = copy.GetTriangle(tid);
                for (int k = 0; k < 3; ++k)
                    if (!(vt[tv[k]] <= t)) vt[tv[k]] = t;
            }
            if (minEdgeLength < 0)
                minEdgeLength = 1e-3 * copy.CachedBounds.DiagonalLength;
            var tree = new DMeshAABBTree3(copy, true);
            return (p) => {
                int tid = tree.FindNearestTriangle(p);
                if (tid == DMesh3.InvalidID)
                    return double.NaN;
                Index3i tv = copy.GetTriangle(tid);
                var d = new DistPoint3Triangle3(p, new Triangle3d(copy.GetVertex(tv.a), copy.GetVertex(tv.b), copy.GetVertex(tv.c))).Compute();
                double sum = 0, wsum = 0;
                for (int k = 0; k < 3; ++k) {
                    double t = vt[tv[k]], w = d.TriangleBaryCoords[k];
                    if (double.IsNaN(t) || !(w > 0))
                        continue;
                    sum += w * t; wsum += w;
                }
                if (!(wsum > 0)) {
                    // the nearest point is on a corner or edge whose vertices have no value: the smallest of the others
                    double m = double.NaN;
                    for (int k = 0; k < 3; ++k)
                        if (double.IsNaN(vt[tv[k]]) == false && !(m <= vt[tv[k]])) m = vt[tv[k]];
                    if (double.IsNaN(m))
                        return double.NaN;
                    return Math.Max(fraction * m, minEdgeLength);
                }
                return Math.Max(fraction * sum / wsum, minEdgeLength);
            };
        }
    }
}
