// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// Discrete mean and Gaussian curvature per vertex, after Meyer, Desbrun, Schröder and Barr, "Discrete
    /// Differential-Geometry Operators for Triangulated 2-Manifolds" (VisMath 2002, published 2003):
    ///   - mixed area A_M of each vertex (their Fig. 4): the Voronoi area of the vertex in each non-obtuse triangle,
    ///     (|x_i x_j|^2 cot(angle at k) + |x_i x_k|^2 cot(angle at j)) / 8, and in an obtuse triangle half its area
    ///     at the obtuse corner and a quarter at the other two; the mixed areas of a mesh sum to its area;
    ///   - mean curvature normal K(x_i) = (1 / (2 A_M)) sum_j (cot alpha_ij + cot beta_ij) (x_i - x_j) (their eq. 8),
    ///     computed in the equivalent area gradient form grad A / A_M (Desbrun et al. 1999; the gradient of a
    ///     triangle's area at a corner is n x e / 2 for its unit normal n and the opposite edge e), which needs no
    ///     cotangents and stays bounded at slivers; mean curvature H = K . n / 2 with n the angle weighted vertex
    ///     normal (positive on a sphere oriented outward: 1 / r);
    ///   - Gaussian curvature by the angle deficit (their eq. 9): (2 pi - sum of the angles at the vertex) / A_M.
    ///
    /// Robustness (all thresholds relative to the triangle, none absolute):
    ///   - a triangle is degenerate if twice its area is at most 1e-12 times its longest edge squared (collinear,
    ///     coincident corners, zero-length edges): it adds no area and no area gradient (its normal is not defined),
    ///     but its angles count in the angle sums, so the total angle deficit stays that of Gauss-Bonnet. The
    ///     angle at a corner with a zero-length edge is not defined: such corners share pi minus the other angles
    ///     equally (pi / 2 for two coincident corners, pi / 3 for three). The vertices of a degenerate triangle get
    ///     values (finite) but are not Valid: the part of their fan that the triangle stands for is missing from the
    ///     area gradient (eg the midpoint of a T-junction sliver gets an in-plane gradient and H = 0);
    ///   - a triangle with a non-finite corner adds nothing (no angles either), counts as degenerate, and its
    ///     vertices are not Valid;
    ///   - boundary vertices get one-sided values: the deficit against pi (the geodesic curvature of the boundary,
    ///     0 at a straight boundary of a plane) and the normal part of grad A / A_M over their fan; bowtie
    ///     vertices (several fans) the values over all fans. Both are computed, but not Valid;
    ///   - a vertex without area (no triangles, or only degenerate ones) gets 0 and is not Valid; no output is NaN
    ///     or infinite (a value that would overflow is set to 0 and the vertex is not Valid; the Voronoi areas are
    ///     formed from the cotangents, so they do not overflow before the area does).
    ///
    /// Scale: H scales with 1 / s and K with 1 / s^2 under a scale by s; scaling by a power of two gives the scaled
    /// values bit for bit, and a rotation or translation the same values up to rounding. Deterministic, O(n).
    /// The existing MeshUtil.DiscreteGaussCurvature (the negative angle deficit, not divided by an area) and
    /// MeshWeights.VoronoiArea (absolute thresholds, NaN at coincident vertices) are left as they are.
    /// </summary>
    public class MeshCurvature
    {
        /// <summary> per vertex id (MaxVertexID entries): mean curvature H, Gaussian curvature K, mixed area A_M </summary>
        public double[] Mean, Gaussian, MixedArea;
        /// <summary> per vertex id: mean curvature normal grad A / A_M (= 2 H n on a smooth surface; zero without area) </summary>
        public Vector3d[] MeanCurvatureNormal;
        /// <summary> per vertex id: the angle deficit (2 pi, or pi at the boundary, minus the angle sum) </summary>
        public double[] AngleDeficit;
        /// <summary> per vertex id: interior, manifold (one fan), with area and finite values, not at a degenerate triangle </summary>
        public bool[] Valid;
        /// <summary> number of Valid vertices, of boundary and bowtie vertices, and of degenerate triangles (see the class) </summary>
        public int ValidVertices, BoundaryVertices, BowtieVertices, DegenerateTriangles;

        /// <summary> a triangle is degenerate if twice its area is at most this times its longest edge squared </summary>
        public const double DegenerateRatio = 1e-12;

        /// <summary> the curvatures of all vertices of mesh (not modified) </summary>
        public static MeshCurvature Compute(DMesh3 mesh)
        {
            var c = new MeshCurvature();
            int nv = mesh.MaxVertexID;
            c.Mean = new double[nv]; c.Gaussian = new double[nv]; c.MixedArea = new double[nv];
            c.AngleDeficit = new double[nv];
            c.MeanCurvatureNormal = new Vector3d[nv];
            c.Valid = new bool[nv];
            var angleSum = new double[nv];
            var gradient = new Vector3d[nv];
            var normal = new Vector3d[nv];
            var atDegenerate = new bool[nv];
            var p = new Vector3d[3];
            var angle = new double[3];
            var dot = new double[3];
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                bool finite = true;
                for (int j = 0; j < 3; ++j) {
                    p[j] = mesh.GetVertex(t[j]);
                    finite = finite && p[j].IsFinite;
                }
                if (finite == false) {
                    c.DegenerateTriangles++;
                    for (int j = 0; j < 3; ++j) atDegenerate[t[j]] = true;
                    continue;
                }
                // angles from atan2 of sine and cosine; corners with a zero-length edge share what is left of pi
                double known = 0;
                int undefined = 0;
                double lmax2 = 0;
                for (int j = 0; j < 3; ++j) {
                    Vector3d u = p[(j + 1) % 3] - p[j], w = p[(j + 2) % 3] - p[j];
                    lmax2 = Math.Max(lmax2, u.LengthSquared);
                    dot[j] = u.Dot(w);
                    if (u == Vector3d.Zero || w == Vector3d.Zero) {
                        angle[j] = double.NaN;
                        undefined++;
                    } else {
                        angle[j] = Math.Atan2(u.Cross(w).Length, dot[j]);
                        known += angle[j];
                    }
                }
                for (int j = 0; j < 3; ++j) {
                    if (double.IsNaN(angle[j]))
                        angle[j] = Math.Max(0, Math.PI - known) / undefined;
                    angleSum[t[j]] += angle[j];
                }
                Vector3d cross = (p[1] - p[0]).Cross(p[2] - p[0]);
                double dblArea = cross.Length;
                if (!(dblArea > DegenerateRatio * lmax2) || double.IsInfinity(dblArea)) {
                    c.DegenerateTriangles++;
                    for (int j = 0; j < 3; ++j) atDegenerate[t[j]] = true;
                    continue;
                }
                Vector3d n = cross / dblArea;
                double area = 0.5 * dblArea;
                bool obtuse = dot[0] < 0 || dot[1] < 0 || dot[2] < 0;
                for (int j = 0; j < 3; ++j) {
                    int j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                    gradient[t[j]] += 0.5 * n.Cross(p[j2] - p[j1]);
                    normal[t[j]] += angle[j] * n;
                    double a;
                    if (obtuse == false) {
                        // cot(angle at k) = dot_k / (2 area), formed first (the products of lengths and dots overflow far sooner)
                        a = ((p[j1] - p[j]).LengthSquared * (dot[j2] / dblArea) + (p[j2] - p[j]).LengthSquared * (dot[j1] / dblArea)) / 8;
                    } else
                        a = (dot[j] < 0) ? 0.5 * area : 0.25 * area;
                    c.MixedArea[t[j]] += a;
                }
            }
            foreach (int vid in mesh.VertexIndices()) {
                bool boundary = mesh.IsBoundaryVertex(vid), bowtie = mesh.IsBowtieVertex(vid);
                if (boundary) c.BoundaryVertices++;
                if (bowtie) c.BowtieVertices++;
                double deficit = (boundary ? Math.PI : 2 * Math.PI) - angleSum[vid];
                if (mesh.GetVtxEdgeCount(vid) == 0)
                    deficit = 0;
                c.AngleDeficit[vid] = deficit;
                double am = c.MixedArea[vid];
                if (!(am > 0))
                    continue;
                Vector3d kn = gradient[vid] / am;
                Vector3d nrm = MeshAnalysisNormals.Unit(normal[vid]);
                double h = 0.5 * kn.Dot(nrm), k = deficit / am;
                if (kn.IsFinite == false || double.IsInfinity(h) || double.IsNaN(h) || double.IsInfinity(k) || double.IsNaN(k))
                    continue;
                c.MeanCurvatureNormal[vid] = kn;
                c.Mean[vid] = h;
                c.Gaussian[vid] = k;
                c.Valid[vid] = boundary == false && bowtie == false && nrm != Vector3d.Zero && atDegenerate[vid] == false;
                if (c.Valid[vid]) c.ValidVertices++;
            }
            return c;
        }
    }
}
