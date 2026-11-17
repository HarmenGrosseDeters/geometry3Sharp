// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// Principal curvatures and principal directions per vertex, after Rusinkiewicz, "Estimating Curvatures and Their
    /// Derivatives on Triangle Meshes" (3DPVT 2004), written from the paper:
    ///   - vertex normals are the angle weighted face normals (Max 1999);
    ///   - on each triangle the second fundamental form II = [a b; b c] in a frame (u, v) of the triangle plane is the
    ///     least squares solution of II (e . u, e . v)^T = (dn . u, dn . v)^T over its three edges e, where dn is the
    ///     difference of the vertex normals at the ends of e (6 equations, 3 unknowns);
    ///   - at a vertex, the tangent basis (u_i, v_i) is rotated into each incident triangle plane (the rotation about
    ///     n_i x n_f that takes n_i to the face normal n_f), II is evaluated on the rotated basis, and the three values
    ///     II(u,u), II(u,v), II(v,v) are averaged with the Voronoi corner areas of Meyer et al. 2003 (the corner area
    ///     of an obtuse triangle is half its area at the obtuse corner and a quarter at the others);
    ///   - the eigenvalues of the 2 x 2 matrix are the principal curvatures K1 &gt;= K2 and the eigenvector of K1 is
    ///     the direction Dir1 (Dir2 = n x Dir1).
    ///
    /// Sign: the curvature is positive where the surface bends away from the normal's side, so a sphere with outward
    /// normals has K1 = K2 = 1 / r (the sign of MeshCurvature.Mean). At a vertex with equal principal curvatures (an
    /// umbilic, eg anywhere on a sphere) the direction is arbitrary (but finite and deterministic): the weight
    /// K1 - K2 says how well defined it is. Boundary vertices use their incident triangles only. Per vertex id
    /// (MaxVertexID entries); a vertex that has no valid triangle (degenerate, or normals that differ by more than
    /// 120 degrees, a fold) gets zeros and Valid = false.
    ///
    /// Scale: K1, K2 scale with 1 / s; scaling by a power of two gives the scaled values bit for bit, a rotation or
    /// translation the same values up to rounding. Serial and deterministic, O(n).
    /// </summary>
    public class MeshPrincipalCurvature
    {
        /// <summary> principal curvatures per vertex id, K1 &gt;= K2 </summary>
        public double[] K1, K2;
        /// <summary> unit vertex normals, and the unit principal directions of K1 and K2 (tangent to the surface) </summary>
        public Vector3d[] Normal, Dir1, Dir2;
        /// <summary> the vertex has at least one usable incident triangle (and so a finite K1, K2, Dir1, Dir2) </summary>
        public bool[] Valid;

        /// <summary>
        /// the principal curvatures and directions of all vertices of mesh (not modified). smoothingRounds &gt; 0 averages the
        /// curvature tensors (3 x 3, in space) over the one-ring, weighted with the Voronoi areas, that many times before the
        /// eigen decomposition: an own choice against noise (a vertex noise of a tenth of the edge length makes the raw
        /// directions nearly random), which also blurs real curvature over that many rings.
        /// </summary>
        public static MeshPrincipalCurvature Compute(DMesh3 mesh, int smoothingRounds = 0)
        {
            var c = new MeshPrincipalCurvature();
            int nv = mesh.MaxVertexID;
            c.K1 = new double[nv]; c.K2 = new double[nv];
            c.Normal = new Vector3d[nv]; c.Dir1 = new Vector3d[nv]; c.Dir2 = new Vector3d[nv];
            c.Valid = new bool[nv];

            // angle weighted vertex normals
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Vector3d p0 = mesh.GetVertex(t.a), p1 = mesh.GetVertex(t.b), p2 = mesh.GetVertex(t.c);
                Vector3d n = (p1 - p0).Cross(p2 - p0);
                double nl = n.Length;
                if (!(nl > 0) || double.IsInfinity(nl)) continue;
                n /= nl;
                for (int k = 0; k < 3; ++k) {
                    Vector3d a = mesh.GetVertex(t[(k + 1) % 3]) - mesh.GetVertex(t[k]);
                    Vector3d b = mesh.GetVertex(t[(k + 2) % 3]) - mesh.GetVertex(t[k]);
                    double ang = Math.Atan2(a.Cross(b).Length, a.Dot(b));
                    c.Normal[t[k]] += ang * n;
                }
            }
            foreach (int v in mesh.VertexIndices()) {
                double l = c.Normal[v].Length;
                c.Normal[v] = l > 0 ? c.Normal[v] / l : Vector3d.Zero;
            }
            // a tangent basis per vertex: the axis least aligned with the normal, projected (deterministic)
            var bu = new Vector3d[nv]; var bv = new Vector3d[nv];
            foreach (int v in mesh.VertexIndices()) {
                Vector3d n = c.Normal[v];
                if (n.LengthSquared == 0) continue;
                Vector3d axis = Math.Abs(n.x) <= Math.Abs(n.y) && Math.Abs(n.x) <= Math.Abs(n.z) ? Vector3d.AxisX : (Math.Abs(n.y) <= Math.Abs(n.z) ? Vector3d.AxisY : Vector3d.AxisZ);
                Vector3d u = (axis - axis.Dot(n) * n).Normalized;
                bu[v] = u; bv[v] = n.Cross(u);
            }

            var kuu = new double[nv]; var kuv = new double[nv]; var kvv = new double[nv]; var wsum = new double[nv];
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Vector3d[] p = { mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c) };
                Vector3d[] n = { c.Normal[t.a], c.Normal[t.b], c.Normal[t.c] };
                Vector3d fn = (p[1] - p[0]).Cross(p[2] - p[0]);
                double area2 = fn.Length;
                double longest = Math.Max((p[1] - p[0]).LengthSquared, Math.Max((p[2] - p[1]).LengthSquared, (p[0] - p[2]).LengthSquared));
                if (!(area2 > 1e-12 * longest) || double.IsInfinity(area2)) continue;
                fn /= area2;
                Vector3d fu = (p[1] - p[0]).Normalized, fv = fn.Cross(fu);

                // least squares II over the three edges (edge k runs from corner k+1 to corner k+2)
                double s00 = 0, s01 = 0, s02 = 0, s11 = 0, s12 = 0, s22 = 0, r0 = 0, r1 = 0, r2 = 0;
                for (int k = 0; k < 3; ++k) {
                    int i1 = (k + 1) % 3, i2 = (k + 2) % 3;
                    Vector3d e = p[i2] - p[i1], dn = n[i2] - n[i1];
                    double eu = e.Dot(fu), ev = e.Dot(fv), du = dn.Dot(fu), dv = dn.Dot(fv);
                    // rows: (eu, ev, 0 | du) and (0, eu, ev | dv)
                    s00 += eu * eu; s01 += eu * ev; s11 += ev * ev + eu * eu; s12 += eu * ev; s22 += ev * ev;
                    r0 += eu * du; r1 += ev * du + eu * dv; r2 += ev * dv;
                }
                double a, b, cc;
                if (!Solve3(s00, s01, s02, s11, s12, s22, r0, r1, r2, out a, out b, out cc)) continue;

                // Voronoi corner areas
                double[] cornerArea = CornerAreas(p, area2 * 0.5);
                for (int k = 0; k < 3; ++k) {
                    int v = t[k];
                    Vector3d nv_ = c.Normal[v];
                    double cs = nv_.Dot(fn);
                    if (cs < -0.5 || cornerArea[k] <= 0) continue;
                    // rotation taking nv_ to fn
                    Vector3d ax = nv_.Cross(fn);
                    Vector3d u = Rotate(bu[v], nv_, ax, cs), w = Rotate(bv[v], nv_, ax, cs);
                    double uu = u.Dot(fu), uv = u.Dot(fv), wu = w.Dot(fu), wv = w.Dot(fv);
                    double iuu = a * uu * uu + 2 * b * uu * uv + cc * uv * uv;
                    double ivv = a * wu * wu + 2 * b * wu * wv + cc * wv * wv;
                    double iuv = a * uu * wu + b * (uu * wv + uv * wu) + cc * uv * wv;
                    double wt = cornerArea[k];
                    kuu[v] += wt * iuu; kuv[v] += wt * iuv; kvv[v] += wt * ivv; wsum[v] += wt;
                }
            }
            if (smoothingRounds > 0) {
                // S = a u u^T + b (u w^T + w u^T) + d w w^T per vertex (6 entries xx yy zz xy xz yz), weighted by wsum
                var S = new double[6 * nv]; var S2 = new double[6 * nv]; var W = new double[nv]; var W2 = new double[nv];
                foreach (int v in mesh.VertexIndices()) {
                    if (!(wsum[v] > 0)) continue;
                    double a = kuu[v] / wsum[v], b = kuv[v] / wsum[v], d = kvv[v] / wsum[v];
                    Vector3d u = bu[v], w = bv[v];
                    S[6 * v] = a * u.x * u.x + 2 * b * u.x * w.x + d * w.x * w.x;
                    S[6 * v + 1] = a * u.y * u.y + 2 * b * u.y * w.y + d * w.y * w.y;
                    S[6 * v + 2] = a * u.z * u.z + 2 * b * u.z * w.z + d * w.z * w.z;
                    S[6 * v + 3] = a * u.x * u.y + b * (u.x * w.y + u.y * w.x) + d * w.x * w.y;
                    S[6 * v + 4] = a * u.x * u.z + b * (u.x * w.z + u.z * w.x) + d * w.x * w.z;
                    S[6 * v + 5] = a * u.y * u.z + b * (u.y * w.z + u.z * w.y) + d * w.y * w.z;
                    W[v] = wsum[v];
                }
                for (int round = 0; round < smoothingRounds; ++round) {
                    foreach (int v in mesh.VertexIndices()) {
                        double ws = W[v];
                        for (int k = 0; k < 6; ++k) S2[6 * v + k] = ws * S[6 * v + k];
                        foreach (int nb in mesh.VtxVerticesItr(v)) {
                            ws += W[nb];
                            for (int k = 0; k < 6; ++k) S2[6 * v + k] += W[nb] * S[6 * nb + k];
                        }
                        W2[v] = ws;
                        if (ws > 0) for (int k = 0; k < 6; ++k) S2[6 * v + k] /= ws;
                    }
                    var tS = S; S = S2; S2 = tS;
                    // the weights stay those of the vertices (not the growing sums): the average is of the tensors
                }
                foreach (int v in mesh.VertexIndices()) {
                    if (!(wsum[v] > 0)) continue;
                    Vector3d u = bu[v], w = bv[v];
                    Func<Vector3d, Vector3d, double> q = (p, r) => p.x * (S[6 * v] * r.x + S[6 * v + 3] * r.y + S[6 * v + 4] * r.z)
                        + p.y * (S[6 * v + 3] * r.x + S[6 * v + 1] * r.y + S[6 * v + 5] * r.z)
                        + p.z * (S[6 * v + 4] * r.x + S[6 * v + 5] * r.y + S[6 * v + 2] * r.z);
                    kuu[v] = q(u, u) * wsum[v]; kuv[v] = q(u, w) * wsum[v]; kvv[v] = q(w, w) * wsum[v];
                }
            }
            foreach (int v in mesh.VertexIndices()) {
                if (!(wsum[v] > 0)) continue;
                double a = kuu[v] / wsum[v], b = kuv[v] / wsum[v], d = kvv[v] / wsum[v];
                double mean = 0.5 * (a + d), diff = 0.5 * (a - d);
                double root = Math.Sqrt(diff * diff + b * b);
                double psi = 0.5 * Math.Atan2(b, diff);       // angle of the K1 direction in (bu, bv)
                if (double.IsNaN(mean) || double.IsNaN(root) || double.IsNaN(psi)) continue;
                c.K1[v] = mean + root; c.K2[v] = mean - root;
                c.Dir1[v] = Math.Cos(psi) * bu[v] + Math.Sin(psi) * bv[v];
                c.Dir2[v] = c.Normal[v].Cross(c.Dir1[v]);
                c.Valid[v] = true;
            }
            return c;
        }

        // R x for the rotation that takes the unit vector n to the unit vector f (axis n x f = ax, cos = cs, cs > -1):
        // Rodrigues' formula x cs + ax x x + ax (ax . x) / (1 + cs)
        internal static Vector3d Rotate(Vector3d x, Vector3d n, Vector3d ax, double cs)
        {
            return x * cs + ax.Cross(x) + ax * (ax.Dot(x) / (1 + cs));
        }

        // the symmetric 3 x 3 system [s00 s01 s02; s01 s11 s12; s02 s12 s22] x = r by Cramer's rule
        static bool Solve3(double s00, double s01, double s02, double s11, double s12, double s22, double r0, double r1, double r2,
            out double a, out double b, out double c)
        {
            a = b = c = 0;
            double c00 = s11 * s22 - s12 * s12, c01 = s02 * s12 - s01 * s22, c02 = s01 * s12 - s02 * s11;
            double det = s00 * c00 + s01 * c01 + s02 * c02;
            double scale = (s00 + s11 + s22);
            if (!(Math.Abs(det) > 1e-12 * scale * scale * scale) || double.IsInfinity(det)) return false;
            double c11 = s00 * s22 - s02 * s02, c12 = s02 * s01 - s00 * s12, c22 = s00 * s11 - s01 * s01;
            a = (c00 * r0 + c01 * r1 + c02 * r2) / det;
            b = (c01 * r0 + c11 * r1 + c12 * r2) / det;
            c = (c02 * r0 + c12 * r1 + c22 * r2) / det;
            return true;
        }

        // Voronoi corner areas of a triangle (Meyer et al. 2003, mixed area): the Voronoi area at a corner of a
        // non-obtuse triangle, and half / a quarter of the area at the obtuse corner / the other corners
        static double[] CornerAreas(Vector3d[] p, double area)
        {
            double[] l2 = new double[3], cot = new double[3];
            for (int k = 0; k < 3; ++k) {
                Vector3d a = p[(k + 1) % 3] - p[k], b = p[(k + 2) % 3] - p[k];
                l2[k] = (p[(k + 2) % 3] - p[(k + 1) % 3]).LengthSquared;       // squared length of the edge opposite corner k
                cot[k] = a.Dot(b) / a.Cross(b).Length;
            }
            double[] r = new double[3];
            bool obtuse = cot[0] < 0 || cot[1] < 0 || cot[2] < 0;
            for (int k = 0; k < 3; ++k) {
                if (!obtuse)
                    r[k] = (l2[(k + 1) % 3] * cot[(k + 2) % 3] + l2[(k + 2) % 3] * cot[(k + 1) % 3]) / 8;
                else
                    r[k] = cot[k] < 0 ? area / 2 : area / 4;
            }
            return r;
        }
    }
}
