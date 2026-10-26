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
    ///     nearest neighbors and the normal field" (The Visual Computer 2012), on the exact surface: start
    ///     with the ball whose diameter is the inward ray, then while a surface point q lies inside the
    ///     ball, replace the ball by the one tangent at p through q (radius |q-p|^2 / (2 (p-q).n)). Only
    ///     contacts seen from the ball center at least SeparationAngle away from p count (the angle
    ///     condition of Dey and Zhao, "Approximate medial axis as a Voronoi subcomplex", CAD 2004): the
    ///     flat facets of a tessellated curved surface fold into any ball tangent to one of them, but only
    ///     close to p, while an opposite wall or a crease is seen at a large angle. Independent of the
    ///     vertex density (large triangles are fine). Creases that fold by less than SeparationAngle count
    ///     as smooth. The thickness in the sense of a gauge ("does a ball of diameter t fit"), never larger
    ///     than Ray. A ball tangent at a polyhedral vertex is not defined at creases (it cuts both faces),
    ///     so vertex values are the area weighted mean of the values of the vertex's triangles.
    /// The rays use the watertight ray-triangle test of Woop, Benthin and Wald, "Watertight ray/triangle
    /// intersection" (JCGT 2013): a ray through a shared edge or vertex hits at least one of the triangles,
    /// so no ray slips through the surface. A ray that leaves the solid through a front face (normal facing
    /// the ray) hit an inconsistently oriented or open part; it is ignored. Points without any valid ray
    /// get NaN, and so do degenerate triangles (area below 1e-12 times the squared bounding box diagonal),
    /// which also do not count as walls. Closed components that are inside out for their nesting, and a mesh that is inside out as
    /// a whole, are taken with flipped normals (OrientInvertedParts; the mesh is not changed).
    /// </summary>
    public class MeshWallThickness
    {
        public DMesh3 Mesh;
        /// <summary>
        /// optional spatial data structure. If null it is built (and rebuilt when Mesh changes); for a mesh far
        /// from the origin (FarFromOrigin) the computation then runs on a copy moved to the origin, with its
        /// own tree, and this stays null. A tree set here is used as it is (also far from the origin).
        /// </summary>
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
        /// <summary> InscribedSphere: smallest angle (degrees) at the ball center between p and a contact that limits the ball </summary>
        public double SeparationAngle = 30;

        /// <summary> treat inside-out closed components (MeshPostRepairCleanup.FindInvertedComponents) and an inside-out mesh as flipped </summary>
        public bool OrientInvertedParts = true;

        public bool ComputeVertices = true;
        public bool ComputeTriangles = true;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> thickness per vertex ID (NaN for unused IDs and vertices without a valid ray) </summary>
        public double[] VertexThickness;
        /// <summary> thickness per triangle ID, at the centroid (NaN for unused IDs and triangles without a valid ray) </summary>
        public double[] TriangleThickness;
        /// <summary> statistics over the triangle values (area weighted), or over the vertex values if ComputeTriangles is false </summary>
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
            MeshWallThickness local = local_instance();
            if (local != null) {
                bool ok = local.Compute();
                VertexThickness = local.VertexThickness; TriangleThickness = local.TriangleThickness;
                Statistics = local.Statistics;
                return ok;
            }
            ensure_spatial();
            double maxDist = (MaxDistance > 0) ? MaxDistance : Mesh.CachedBounds.DiagonalLength;
            maxDist = Math.Max(maxDist, MathUtil.Epsilonf);
            set_tolerances();
            Vector3d[] cone = cone_directions();

            bool fromTriangles = ComputeVertices && Method == Methods.InscribedSphere;
            if ((ComputeTriangles || fromTriangles) && CancelF() == false) {
                TriangleThickness = new double[Mesh.MaxTriangleID];
                for (int i = 0; i < TriangleThickness.Length; ++i)
                    TriangleThickness[i] = double.NaN;
                gParallel.ForEach(Mesh.TriangleIndices(), (tid) => {
                    if (CancelF()) return;
                    Vector3d n = tri_normal(tid);
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
                    if (fromTriangles) {
                        VertexThickness[vid] = triangle_average(vid);
                        return;
                    }
                    Vector3d n = vertex_normal(vid);
                    if (n == Vector3d.Zero) return;
                    VertexThickness[vid] = thickness(Mesh.GetVertex(vid), n, vid, -1, cone, maxDist);
                });
            }
            if (CancelF())
                return false;

            var values = new List<double>();
            var weights = new List<double>();
            if (TriangleThickness != null && ComputeTriangles) {
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


        /// <summary>
        /// thickness at a surface point p with outward unit normal n (see Method); NaN if no valid ray.
        /// May be called from several threads at once.
        /// </summary>
        public double ThicknessAt(Vector3d p, Vector3d n)
        {
            MeshWallThickness local = local_instance();
            if (local != null)
                return local.ThicknessAt(p - localOffset, n);
            lock (toleranceLock)
                ensure_spatial();
            double maxDist = (MaxDistance > 0) ? MaxDistance : Mesh.CachedBounds.DiagonalLength;
            set_tolerances();
            return thickness(p, MeshAnalysisNormals.Unit(n), -1, -1, cone_directions(), Math.Max(maxDist, MathUtil.Epsilonf));
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


        // smallest ray parameter of a hit (relative to the mesh size, so that the result scales exactly with
        // the mesh), and the padding of the (float) tree boxes
        double eps, boxPad, tieTol, degenerateArea, coordRounding;

        // triangles taken with reversed orientation (OrientInvertedParts)
        bool[] flipped;

        /// <summary>
        /// true if the mesh lies so far from the origin (more than 10 bounding box diagonals) that the float
        /// boxes of DMeshAABBTree3, which round by 2^-24 of the coordinates (0.3 at 5e6), become coarse
        /// against the mesh size: queries then run on a copy moved to the origin
        /// </summary>
        public static bool FarFromOrigin(DMesh3 mesh)
        {
            AxisAlignedBox3d b = mesh.CachedBounds;
            return Math.Max(b.Min.MaxAbs, b.Max.MaxAbs) > 10 * b.DiagonalLength;
        }

        // for a mesh far from the origin and no given Spatial: the same computation on a copy moved to the
        // origin (same IDs; thickness does not change under a translation). Null otherwise.
        MeshWallThickness localCopy;
        Vector3d localOffset;
        int localStamp = -1;

        DMesh3 localSource;

        // the tree this instance built itself (as opposed to one the caller set), the mesh and edit stamp
        // it was built for: rebuilt when Mesh changes (another object, or edited)
        DMeshAABBTree3 ownSpatial;
        DMesh3 ownSpatialSource;
        int ownSpatialStamp = -1;

        bool spatial_given_by_caller()
        {
            return Spatial != null && Spatial != ownSpatial;
        }

        void ensure_spatial()
        {
            if (spatial_given_by_caller())
                return;
            if (ownSpatial == null || ownSpatialSource != Mesh || ownSpatialStamp != Mesh.ShapeTimestamp) {
                ownSpatial = new DMeshAABBTree3(Mesh, true);
                ownSpatialSource = Mesh; ownSpatialStamp = Mesh.ShapeTimestamp;
            }
            Spatial = ownSpatial;
        }

        MeshWallThickness local_instance()
        {
            if (spatial_given_by_caller() || FarFromOrigin(Mesh) == false)
                return null;
            lock (toleranceLock) {
                if (localCopy == null || localSource != Mesh || localStamp != Mesh.ShapeTimestamp) {
                    localOffset = Mesh.CachedBounds.Center;
                    DMesh3 m = new DMesh3(Mesh, false, MeshComponents.None);
                    MeshTransforms.Translate(m, -localOffset);
                    localCopy = new MeshWallThickness(m);
                    localSource = Mesh; localStamp = Mesh.ShapeTimestamp;
                    localCopy.ensure_spatial();     // here, under the lock, not in concurrent ThicknessAt calls
                }
                // current settings
                localCopy.Method = Method; localCopy.ConeAngle = ConeAngle; localCopy.RayCount = RayCount;
                localCopy.MaxDistance = MaxDistance; localCopy.MaxBallIterations = MaxBallIterations;
                localCopy.SeparationAngle = SeparationAngle; localCopy.OrientInvertedParts = OrientInvertedParts;
                localCopy.ComputeVertices = ComputeVertices; localCopy.ComputeTriangles = ComputeTriangles;
                localCopy.CancelF = CancelF;
                return localCopy;
            }
        }

        // zero for degenerate triangles (area below 1e-12 diag^2): their normal is rounding noise, which
        // depends on the unit; they get no value and do not count as walls
        Vector3d tri_normal(int tid)
        {
            double area;
            Vector3d n = MeshAnalysisNormals.TriNormal(Mesh, tid, out area);
            if (!(area >= degenerateArea))
                return Vector3d.Zero;
            return (flipped != null && flipped[tid]) ? -n : n;
        }

        /// <summary>
        /// Triangles of mesh to reverse so that it is outward oriented: those of closed components that are
        /// inside out for their nesting (MeshPostRepairCleanup.FindInvertedComponents); then, only if the mesh
        /// has no boundary edges at all, all of them if the signed volume is still negative (a fallback for
        /// when FindInvertedComponents does not decide, eg more than 500 components). For an open mesh the
        /// sign of the volume says nothing about its orientation (an open terrain with one tall peak has a
        /// negative one), so open parts are never flipped. The mesh and its IDs are not changed. Null if none.
        /// </summary>
        public static bool[] InvertedTriangles(DMesh3 mesh)
        {
            bool[] flip = new bool[mesh.MaxTriangleID];
            bool any = false;
            foreach (int[] comp in MeshPostRepairCleanup.FindInvertedComponents(mesh))
                foreach (int tid in comp) { flip[tid] = true; any = true; }
            bool closed = true;
            foreach (int eid in mesh.EdgeIndices())
                if (mesh.IsBoundaryEdge(eid)) { closed = false; break; }
            if (closed) {
                Vector3d c = mesh.CachedBounds.Center;
                double vol = 0;
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    Vector3d a = mesh.GetVertex(t.a) - c, b = mesh.GetVertex(t.b) - c, d = mesh.GetVertex(t.c) - c;
                    double v = a.Dot(b.Cross(d));
                    vol += flip[tid] ? -v : v;
                }
                if (vol < 0) {
                    foreach (int tid in mesh.TriangleIndices()) flip[tid] = !flip[tid];
                    any = true;
                }
            }
            return any ? flip : null;
        }

        int flippedStamp = -1;
        DMesh3 flippedSource;
        bool flippedMode;

        readonly object toleranceLock = new object();

        // also called by ThicknessAt, possibly from several threads: under a lock
        void set_tolerances()
        {
            lock (toleranceLock)
                set_tolerances_locked();
        }

        void set_tolerances_locked()
        {
            if (flippedSource != Mesh || flippedStamp != Mesh.ShapeTimestamp || flippedMode != OrientInvertedParts) {
                flipped = OrientInvertedParts ? InvertedTriangles(Mesh) : null;
                flippedSource = Mesh; flippedStamp = Mesh.ShapeTimestamp; flippedMode = OrientInvertedParts;
            }
            // relative to the mesh size, plus the rounding of the coordinates where they are large (eg geo
            // coordinates far from the origin): doubles for the geometry, floats for the tree boxes
            AxisAlignedBox3d b = Mesh.CachedBounds;
            double maxAbs = Math.Max(b.Min.MaxAbs, b.Max.MaxAbs);
            coordRounding = 64 * MathUtil.Epsilon * maxAbs;
            eps = 1e-9 * b.DiagonalLength + coordRounding;
            tieTol = 1e-9 * b.DiagonalLength + coordRounding;
            boxPad = 1e-6 * b.DiagonalLength + 2.5e-7 * maxAbs;
            degenerateArea = 1e-12 * b.DiagonalLength * b.DiagonalLength;
        }

        Vector3d vertex_normal(int vid)
        {
            Vector3d n = Vector3d.Zero;
            foreach (int tid in Mesh.VtxTrianglesItr(vid)) {
                double area;
                Vector3d tn = MeshAnalysisNormals.TriNormal(Mesh, tid, out area);
                if (flipped != null && flipped[tid]) tn = -tn;
                n += area * tn;
            }
            return MeshAnalysisNormals.Unit(n);
        }


        double triangle_average(int vid)
        {
            double sum = 0, wsum = 0;
            foreach (int tid in Mesh.VtxTrianglesItr(vid)) {
                double t = TriangleThickness[tid];
                if (double.IsNaN(t)) continue;
                double a = Mesh.GetTriArea(tid);
                sum += a * t; wsum += a;
            }
            return (wsum > 0) ? sum / wsum : double.NaN;
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
        // leaves through a front face
        double ray_length(Vector3d p, Vector3d dir, int vid, int tid, double maxDist)
        {
            WatertightRay ray = new WatertightRay(p, dir);
            double best = maxDist;
            int bestTri = DMesh3.InvalidID;
            double bestDot = 0;
            Spatial.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                NextBoxF = (box, depth) => ray.HitsBox(box, boxPad, best),
                NextTriangleF = (ti) => {
                    if (ti == tid) return;
                    Index3i tv = Mesh.GetTriangle(ti);
                    if (vid >= 0 && (tv.a == vid || tv.b == vid || tv.c == vid)) return;
                    double t;
                    if (ray.Intersect(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c), out t) == false) return;
                    if (t <= eps || t > maxDist || t > best + tieTol) return;
                    Vector3d tn = tri_normal(ti);
                    if (tn == Vector3d.Zero) return;        // degenerate: no wall
                    double dot = tn.Dot(dir);
                    // coincident faces (touching parts, duplicates) are hit at the same t up to rounding:
                    // prefer the one the ray leaves through, whatever the rounding (it depends on the unit)
                    if (bestTri != DMesh3.InvalidID && Math.Abs(t - best) <= tieTol) {
                        if (dot > bestDot) { bestTri = ti; bestDot = dot; }
                        best = Math.Min(best, t);
                        return;
                    }
                    best = t; bestTri = ti; bestDot = dot;
                }
            });
            if (bestTri == DMesh3.InvalidID || !(bestDot > 0))
                return double.NaN;      // no wall, or entered through a front face: open or flipped part
            return best;
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
            if (double.IsNaN(t))
                return double.NaN;  // no valid opposite wall (open or inconsistently oriented part)
            double r = 0.5 * t;
            double tol = 1e-12 * maxDist + coordRounding;
            double cosSep = Math.Cos(MathUtil.Clamp(SeparationAngle, 0, 180) * MathUtil.Deg2Rad);
            for (int iter = 0; iter < MaxBallIterations; ++iter) {
                Vector3d c = p - r * n;
                double rr = r;
                // the closest surface point inside the ball, among those far enough from p as seen from c
                double bestSqr = (r - tol) * (r - tol);
                Vector3d q = Vector3d.Zero;
                bool found = false;
                Spatial.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxF = (box, depth) => box_distance_sqr(box, c, boxPad) < bestSqr,
                    NextTriangleF = (ti) => {
                        Index3i tv = Mesh.GetTriangle(ti);
                        Triangle3d tri = new Triangle3d(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c));
                        Vector3d closest, bary;
                        double dsqr = DistPoint3Triangle3.DistanceSqr(ref c, ref tri, out closest, out bary);
                        if (!(dsqr < bestSqr)) return;
                        Vector3d w = closest - c;
                        double wl = w.Length;
                        // p lies in direction +n from c
                        if (wl > 0 && n.Dot(w) > cosSep * wl)
                            return;         // seen close to p: the folded neighborhood, not a wall
                        bestSqr = dsqr; q = closest; found = true;
                    }
                });
                if (found == false)
                    break;          // empty ball
                Vector3d d = q - p;
                double dn = d.Dot(n);
                if (dn >= -tol)
                    break;
                double rnew = -d.LengthSquared / (2 * dn);
                if (!(rnew < rr * (1 - 1e-12)))
                    break;
                r = rnew;
            }
            return 2 * r;
        }

        static double box_distance_sqr(AxisAlignedBox3f box, Vector3d p, double pad)
        {
            double dx = Math.Max(0, Math.Max(box.Min.x - pad - p.x, p.x - box.Max.x - pad));
            double dy = Math.Max(0, Math.Max(box.Min.y - pad - p.y, p.y - box.Max.y - pad));
            double dz = Math.Max(0, Math.Max(box.Min.z - pad - p.z, p.z - box.Max.z - pad));
            return dx * dx + dy * dy + dz * dz;
        }


        // watertight ray-triangle test (Woop, Benthin and Wald 2013): the vertices are sheared into the
        // ray's frame (z along the ray) once per vertex, and the edge functions of a shared edge are the
        // same products with opposite signs, so the two triangles never both reject a ray through it.
        // Edge functions that are exactly 0 count as inside.
        struct WatertightRay
        {
            Vector3d o, d, invD;
            int kx, ky, kz;
            double Sx, Sy, Sz;

            public WatertightRay(Vector3d origin, Vector3d dir)
            {
                o = origin; d = dir;
                invD = new Vector3d(1.0 / dir.x, 1.0 / dir.y, 1.0 / dir.z);
                kz = (Math.Abs(dir.x) >= Math.Abs(dir.y)) ? (Math.Abs(dir.x) >= Math.Abs(dir.z) ? 0 : 2) : (Math.Abs(dir.y) >= Math.Abs(dir.z) ? 1 : 2);
                kx = (kz + 1) % 3; ky = (kx + 1) % 3;
                if (dir[kz] < 0) { int tmp = kx; kx = ky; ky = tmp; }
                Sx = dir[kx] / dir[kz]; Sy = dir[ky] / dir[kz]; Sz = 1.0 / dir[kz];
            }

            public bool Intersect(Vector3d v0, Vector3d v1, Vector3d v2, out double t)
            {
                t = double.NaN;
                Vector3d A = v0 - o, B = v1 - o, C = v2 - o;
                double Ax = A[kx] - Sx * A[kz], Ay = A[ky] - Sy * A[kz];
                double Bx = B[kx] - Sx * B[kz], By = B[ky] - Sy * B[kz];
                double Cx = C[kx] - Sx * C[kz], Cy = C[ky] - Sy * C[kz];
                double U = Cx * By - Cy * Bx;
                double V = Ax * Cy - Ay * Cx;
                double W = Bx * Ay - By * Ax;
                if ((U < 0 || V < 0 || W < 0) && (U > 0 || V > 0 || W > 0))
                    return false;
                double det = U + V + W;
                if (det == 0)
                    return false;
                double T = U * (Sz * A[kz]) + V * (Sz * B[kz]) + W * (Sz * C[kz]);
                t = T / det;
                return t > 0;
            }

            // does the ray meet the box (padded) before maxT
            public bool HitsBox(AxisAlignedBox3f box, double pad, double maxT)
            {
                double t0 = 0, t1 = maxT;
                for (int k = 0; k < 3; ++k) {
                    double lo = box.Min[k] - pad, hi = box.Max[k] + pad;
                    if (d[k] == 0) {
                        if (o[k] < lo || o[k] > hi) return false;
                        continue;
                    }
                    double ta = (lo - o[k]) * invD[k], tb = (hi - o[k]) * invD[k];
                    if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                    t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
                    if (t0 > t1) return false;
                }
                return true;
            }
        }
    }
}
