// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Wall thickness per vertex and per triangle of a closed, consistently oriented (outward normals) mesh.
    /// Three measures, all evaluated at the vertices (with the area weighted vertex normal) and at the
    /// triangle centroids (with the face normal):
    ///   - Ray: length of the ray along the inward normal to the opposite wall. Exact for parallel walls
    ///     (slabs, concentric shells), but sensitive to noise and to a single unlucky ray.
    ///   - ShapeDiameter: shape diameter function after Shapira, Shamir and Cohen-Or, "Consistent mesh
    ///     partitioning and skeletonisation using the shape diameter function" (The Visual Computer 2008):
    ///     RayCount rays in a cone of ConeAngle (default 120 degrees) around the inward normal, rays whose
    ///     length is more than one standard deviation from the median are dropped, the rest averaged with
    ///     weights 1/(angle to the cone axis). Robust, but larger than the wall thickness (oblique rays are
    ///     longer); it measures the local "diameter" of the part.
    ///   - InscribedSphere: diameter of the largest ball that touches the surface at the point and lies
    ///     inside the solid, ie twice the distance to the medial axis along the normal. Computed by the
    ///     shrinking ball iteration of Ma, Bai, Hu and Kim, "3D medial axis point approximation using
    ///     nearest neighbors and the normal field" (The Visual Computer 2012), with the mesh vertices as the
    ///     samples of the (smooth) surface: start with the ball whose diameter is the inward ray, then while
    ///     the nearest vertex q to the center lies inside the ball, replace the ball by the one tangent at p
    ///     through q (radius |q-p|^2 / (2 (p-q).n)). The thickness in the sense of a gauge ("does a ball of
    ///     diameter t fit"), never larger than Ray. The vertices stand for the surface: on coarse meshes
    ///     (eg large flat triangles meeting at concave creases) the ball can reach between them, so refine
    ///     such meshes first; exact closest points on the triangles instead would shrink every ball at a
    ///     tessellated curved surface (the flat facets fold into any ball tangent to one of them).
    /// A ray that leaves the solid through a front face (normal facing the ray) hit an inconsistently
    /// oriented or open part; it is ignored. Points without any valid ray get NaN.
    /// </summary>
    public class MeshWallThickness
    {
        public DMesh3 Mesh;
        /// <summary> optional spatial data structure (built if null) </summary>
        public DMeshAABBTree3 Spatial;

        public enum Methods { Ray, ShapeDiameter, InscribedSphere }
        public Methods Method = Methods.ShapeDiameter;

        /// <summary> full opening angle in degrees of the ray cone for ShapeDiameter </summary>
        public double ConeAngle = 120;
        /// <summary> number of rays per point for ShapeDiameter </summary>
        public int RayCount = 30;
        /// <summary> rays longer than this count as missing; 0 = bounding box diagonal </summary>
        public double MaxDistance = 0;
        /// <summary> iteration limit of the shrinking ball (InscribedSphere) </summary>
        public int MaxBallIterations = 100;

        public bool ComputeVertices = true;
        public bool ComputeTriangles = true;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> thickness per vertex ID (NaN for unused IDs and vertices without a valid ray) </summary>
        public double[] VertexThickness;
        /// <summary> thickness per triangle ID, at the centroid (NaN for unused IDs and triangles without a valid ray) </summary>
        public double[] TriangleThickness;
        /// <summary> statistics over the computed triangle values (area weighted), or the vertex values if no triangles were computed </summary>
        public DeviationStatistics Statistics;


        public MeshWallThickness(DMesh3 mesh)
        {
            Mesh = mesh;
        }


        /// <summary> returns false if the mesh is empty or the computation was cancelled </summary>
        public bool Compute()
        {
            VertexThickness = null; TriangleThickness = null; Statistics = null;
            if (Mesh.TriangleCount == 0)
                return false;
            if (Spatial == null)
                Spatial = new DMeshAABBTree3(Mesh, true);
            double maxDist = (MaxDistance > 0) ? MaxDistance : Mesh.CachedBounds.DiagonalLength;
            maxDist = Math.Max(maxDist, MathUtil.Epsilonf);
            eps = 1e-9 * Mesh.CachedBounds.DiagonalLength;
            Vector3d[] cone = cone_directions();

            if (ComputeTriangles && CancelF() == false) {
                TriangleThickness = new double[Mesh.MaxTriangleID];
                for (int i = 0; i < TriangleThickness.Length; ++i)
                    TriangleThickness[i] = double.NaN;
                gParallel.ForEach(Mesh.TriangleIndices(), (tid) => {
                    if (CancelF()) return;
                    Vector3d n = Mesh.GetTriNormal(tid);
                    if (n == Vector3d.Zero || double.IsNaN(n.x)) return;
                    TriangleThickness[tid] = thickness(Mesh.GetTriCentroid(tid), n, -1, tid, cone, maxDist);
                });
            }
            if (ComputeVertices && CancelF() == false) {
                VertexThickness = new double[Mesh.MaxVertexID];
                for (int i = 0; i < VertexThickness.Length; ++i)
                    VertexThickness[i] = double.NaN;
                gParallel.ForEach(Mesh.VertexIndices(), (vid) => {
                    if (CancelF() || Mesh.GetVtxEdgeCount(vid) == 0) return;
                    Vector3d n = vertex_normal(vid);
                    if (n == Vector3d.Zero) return;
                    VertexThickness[vid] = thickness(Mesh.GetVertex(vid), n, vid, -1, cone, maxDist);
                });
            }
            if (CancelF())
                return false;

            var values = new List<double>();
            var weights = new List<double>();
            if (TriangleThickness != null) {
                foreach (int tid in Mesh.TriangleIndices()) {
                    values.Add(TriangleThickness[tid]); weights.Add(Mesh.GetTriArea(tid));
                }
            } else if (VertexThickness != null) {
                foreach (int vid in Mesh.VertexIndices()) {
                    values.Add(VertexThickness[vid]); weights.Add(1);
                }
            }
            Statistics = DeviationStatistics.Compute(values, weights);
            return true;
        }


        /// <summary> thickness at a surface point p with outward unit normal n (see Method); NaN if no valid ray </summary>
        public double ThicknessAt(Vector3d p, Vector3d n)
        {
            if (Spatial == null)
                Spatial = new DMeshAABBTree3(Mesh, true);
            double maxDist = (MaxDistance > 0) ? MaxDistance : Mesh.CachedBounds.DiagonalLength;
            eps = 1e-9 * Mesh.CachedBounds.DiagonalLength;
            return thickness(p, n.Normalized, -1, -1, cone_directions(), Math.Max(maxDist, MathUtil.Epsilonf));
        }


        /// <summary> vertices whose thickness is below minThickness </summary>
        public List<int> ThinVertices(double minThickness)
        {
            var result = new List<int>();
            if (VertexThickness != null)
                foreach (int vid in Mesh.VertexIndices())
                    if (VertexThickness[vid] < minThickness) result.Add(vid);
            return result;
        }

        /// <summary> triangles whose thickness is below minThickness </summary>
        public List<int> ThinTriangles(double minThickness)
        {
            var result = new List<int>();
            if (TriangleThickness != null)
                foreach (int tid in Mesh.TriangleIndices())
                    if (TriangleThickness[tid] < minThickness) result.Add(tid);
            return result;
        }


        // offset of the ray origins and beyond grazed source triangles (relative to the mesh size, so that
        // the result scales exactly with the mesh)
        double eps;

        Vector3d vertex_normal(int vid)
        {
            Vector3d n = Vector3d.Zero;
            foreach (int tid in Mesh.VtxTrianglesItr(vid)) {
                Vector3d tn, c; double area;
                Mesh.GetTriInfo(tid, out tn, out area, out c);
                if (double.IsNaN(tn.x)) continue;
                n += area * tn;
            }
            double len = n.Length;
            return (len > MathUtil.ZeroTolerance) ? n / len : Vector3d.Zero;
        }


        // unit directions of the ray cone around +Z: equal-area samples of the spherical cap (golden angle
        // spiral), none on the axis itself (so the 1/angle weights are finite)
        Vector3d[] cone_directions()
        {
            int n = Math.Max(1, RayCount);
            double cosMax = Math.Cos(0.5 * MathUtil.Clamp(ConeAngle, 0, 179) * MathUtil.Deg2Rad);
            double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            var dirs = new Vector3d[n];
            for (int i = 0; i < n; ++i) {
                double u = (i + 0.5) / n;
                double z = 1.0 - u * (1.0 - cosMax);
                double s = Math.Sqrt(Math.Max(0, 1 - z * z));
                double phi = i * golden;
                dirs[i] = new Vector3d(s * Math.Cos(phi), s * Math.Sin(phi), z);
            }
            return dirs;
        }


        double thickness(Vector3d p, Vector3d n, int vid, int tid, Vector3d[] cone, double maxDist)
        {
            switch (Method) {
                case Methods.Ray:
                    return ray_length(p, -n, vid, tid, maxDist);
                case Methods.InscribedSphere:
                    return inscribed_diameter(p, n, vid, tid, maxDist);
                default:
                    return shape_diameter(p, n, vid, tid, cone, maxDist);
            }
        }


        // distance from p along unit dir to the opposite wall: the first hit of a triangle other than the
        // source triangle or the triangles at the source vertex; NaN if none within maxDist or if the ray
        // leaves through a front face. A ray through a vertex or edge of the opposite wall can slip through
        // the crack between the floating point ray-triangle tests and hit the far side; then the ray is
        // repeated with slightly tilted directions.
        double ray_length(Vector3d p, Vector3d dir, int vid, int tid, double maxDist)
        {
            bool frontFace;
            double t = ray_cast(p, dir, vid, tid, maxDist, out frontFace);
            if (frontFace) {
                Vector3d b1, b2;
                Vector3d.MakePerpVectors(ref dir, out b1, out b2);
                for (int k = 0; k < 3 && frontFace; ++k) {
                    double a = 2 * Math.PI * k / 3;
                    Vector3d tilted = (dir + 1e-6 * (Math.Cos(a) * b1 + Math.Sin(a) * b2)).Normalized;
                    t = ray_cast(p, tilted, vid, tid, maxDist, out frontFace);
                }
            }
            return frontFace ? double.NaN : t;
        }

        double ray_cast(Vector3d p, Vector3d dir, int vid, int tid, double maxDist, out bool frontFace)
        {
            frontFace = false;
            double start = eps;
            for (int attempt = 0; attempt < 8; ++attempt) {
                Ray3d ray = new Ray3d(p + start * dir, dir);
                int hit = Spatial.FindNearestHitTriangle(ray, maxDist - start);
                if (hit == DMesh3.InvalidID)
                    return double.NaN;
                IntrRay3Triangle3 isect = MeshQueries.TriangleIntersection(Mesh, hit, ray);
                double t = start + isect.RayParameter;
                if (t > maxDist)
                    return double.NaN;
                if (hit == tid || (vid >= 0 && IndexUtil.find_tri_index(vid, Mesh.GetTriangle(hit)) >= 0)) {
                    start = t + eps;        // grazing the source: continue behind it
                    continue;
                }
                if (Mesh.GetTriNormal(hit).Dot(dir) <= 0) {
                    frontFace = true;       // entered through a front face: open or flipped part
                    return double.NaN;
                }
                return t;
            }
            return double.NaN;
        }


        double shape_diameter(Vector3d p, Vector3d n, int vid, int tid, Vector3d[] cone, double maxDist)
        {
            Vector3d axis = -n, b1, b2;
            Vector3d.MakePerpVectors(ref axis, out b1, out b2);
            var lengths = new List<double>(cone.Length);
            var angles = new List<double>(cone.Length);
            foreach (Vector3d d in cone) {
                Vector3d dir = (d.x * b1 + d.y * b2 + d.z * axis).Normalized;
                double t = ray_length(p, dir, vid, tid, maxDist);
                if (double.IsNaN(t)) continue;
                lengths.Add(t);
                angles.Add(Math.Acos(MathUtil.Clamp(d.z, -1, 1)));
            }
            if (lengths.Count == 0)
                return double.NaN;

            // drop rays more than one standard deviation from the median
            double[] sorted = lengths.ToArray();
            Array.Sort(sorted);
            int m = sorted.Length;
            double median = (m % 2 == 1) ? sorted[m / 2] : 0.5 * (sorted[m / 2 - 1] + sorted[m / 2]);
            double mean = 0;
            foreach (double t in lengths) mean += t;
            mean /= m;
            double var = 0;
            foreach (double t in lengths) var += (t - mean) * (t - mean);
            double std = Math.Sqrt(var / m);

            double sum = 0, wsum = 0;
            for (int i = 0; i < lengths.Count; ++i) {
                if (Math.Abs(lengths[i] - median) > std) continue;
                double w = 1.0 / Math.Max(angles[i], 1e-6);
                sum += w * lengths[i]; wsum += w;
            }
            return (wsum > 0) ? sum / wsum : median;
        }


        double inscribed_diameter(Vector3d p, Vector3d n, int vid, int tid, double maxDist)
        {
            // the inward ray bounds the ball's diameter: its hit point lies on the diameter through p
            double t = ray_length(p, -n, vid, tid, maxDist);
            double r = double.IsNaN(t) ? 0.5 * maxDist : 0.5 * t;
            double tol = 1e-12 * maxDist;
            for (int iter = 0; iter < MaxBallIterations; ++iter) {
                Vector3d c = p - r * n;
                int near = Spatial.FindNearestVertex(c, r);
                if (near == DMesh3.InvalidID || near == vid)
                    break;          // empty ball
                Vector3d d = Mesh.GetVertex(near) - p;
                double dn = d.Dot(n);
                if (dn >= -tol)
                    break;          // q is (numerically) on the tangent plane: the ball cannot shrink to exclude it
                double rnew = -d.LengthSquared / (2 * dn);
                if (!(rnew < r * (1 - 1e-12)))
                    break;
                r = rnew;
            }
            if (double.IsNaN(t) && r >= 0.5 * maxDist * (1 - 1e-12))
                return double.NaN;  // no opposite wall at all
            return 2 * r;
        }
    }
}
