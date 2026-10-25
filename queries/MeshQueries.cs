using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Static helper queries on a DMesh3: point-triangle and triangle-triangle distances and
    /// intersections for individual mesh triangles, surface frames at a point or ray hit,
    /// brute-force (linear search) nearest-vertex / nearest-triangle / ray-hit queries, and edge length statistics.
    /// The functions taking an ISpatial (eg a built <see cref="DMeshAABBTree3"/>) use it to find the triangle
    /// and then compute the exact result on that triangle. The _LinearSearch functions are O(n) and
    /// mainly useful for testing or for meshes that are queried only once.
    /// </summary>
    public static class MeshQueries
    {

        /// <summary>
        /// Construct a DistPoint3Triangle3 object for a mesh triangle, with the distance already computed
        /// (see TriangleClosest, TriangleBaryCoords, Get()). Returns null if ti is not a valid triangle.
        /// </summary>
        public static DistPoint3Triangle3 TriangleDistance(DMesh3 mesh, int ti, Vector3d point)
        {
            if (!mesh.IsTriangle(ti))
                return null;
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(ti, ref tri.V0, ref tri.V1, ref tri.V2);
            DistPoint3Triangle3 q = new DistPoint3Triangle3(point, tri);
            q.GetSquared();
            return q;
        }

        /// <summary>
        /// Find point-normal(Z) frame at closest point to queryPoint on mesh, using spatial to find the nearest triangle.
        /// Returns interpolated vertex-normal frame if available (and bForceFaceNormal is false), otherwise tri-normal frame.
        /// Throws if no nearest triangle is found (eg empty mesh).
        /// </summary>
        public static Frame3f NearestPointFrame(DMesh3 mesh, ISpatial spatial, Vector3d queryPoint, bool bForceFaceNormal = false)
        {
            int tid = spatial.FindNearestTriangle(queryPoint);
            Vector3d surfPt = TriangleDistance(mesh, tid, queryPoint).TriangleClosest;
            if (mesh.HasVertexNormals && bForceFaceNormal == false)
                return SurfaceFrame(mesh, tid, surfPt);
            else
                return new Frame3f(surfPt, mesh.GetTriNormal(tid));
        }


        /// <summary>
        /// Find unsigned distance from queryPoint to the mesh surface, using spatial to find the nearest triangle.
        /// Returns double.MaxValue if no triangle is found within maxDist.
        /// </summary>
        public static double NearestPointDistance(DMesh3 mesh, ISpatial spatial, Vector3d queryPoint, double maxDist = double.MaxValue)
        {
            int tid = spatial.FindNearestTriangle(queryPoint, maxDist);
            if (tid == DMesh3.InvalidID)
                return double.MaxValue;
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            Vector3d closest, bary;
            double dist_sqr = DistPoint3Triangle3.DistanceSqr(ref queryPoint, ref tri, out closest, out bary);
            return Math.Sqrt(dist_sqr);
        }



        /// <summary>
        /// Find distance between two triangles, with optional
        /// transform on second triangle. Returns the computed DistTriangle3Triangle3
        /// (closest points and distance), or null if either triangle ID is invalid.
        /// </summary>
        public static DistTriangle3Triangle3 TriangleTriangleDistance(DMesh3 mesh1, int ti, DMesh3 mesh2, int tj, Func<Vector3d, Vector3d> TransformF = null)
        {
            if (mesh1.IsTriangle(ti) == false || mesh2.IsTriangle(tj) == false)
                return null;
            Triangle3d tri1 = new Triangle3d(), tri2 = new Triangle3d();
            mesh1.GetTriVertices(ti, ref tri1.V0, ref tri1.V1, ref tri1.V2);
            mesh2.GetTriVertices(tj, ref tri2.V0, ref tri2.V1, ref tri2.V2);
            if (TransformF != null) {
                tri2.V0 = TransformF(tri2.V0);
                tri2.V1 = TransformF(tri2.V1);
                tri2.V2 = TransformF(tri2.V2);
            }
            DistTriangle3Triangle3 dist = new DistTriangle3Triangle3(tri1, tri2);
            dist.Compute();
            return dist;
        }



        /// <summary>
        /// Convenience function to construct a IntrRay3Triangle3 object for a mesh triangle, with Find() already called.
        /// Check Result == IntersectionResult.Intersects and RayParameter. Returns null if ti is not a valid triangle.
        /// </summary>
        public static IntrRay3Triangle3 TriangleIntersection(DMesh3 mesh, int ti, Ray3d ray)
        {
            if (!mesh.IsTriangle(ti))
                return null;
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(ti, ref tri.V0, ref tri.V1, ref tri.V2);
            IntrRay3Triangle3 q = new IntrRay3Triangle3(ray, tri);
            q.Find();
            return q;
        }


        /// <summary>
        /// Convenience function to construct a IntrTriangle3Triangle3 object for two mesh triangles, with Find() already called,
        /// with optional transform on the second triangle. Returns null if either triangle ID is invalid.
        /// </summary>
        public static IntrTriangle3Triangle3 TrianglesIntersection(DMesh3 mesh1, int ti, DMesh3 mesh2, int tj, Func<Vector3d, Vector3d> TransformF = null)
        {
            if (mesh1.IsTriangle(ti) == false || mesh2.IsTriangle(tj) == false)
                return null;
            Triangle3d tri1 = new Triangle3d(), tri2 = new Triangle3d();
            mesh1.GetTriVertices(ti, ref tri1.V0, ref tri1.V1, ref tri1.V2);
            mesh2.GetTriVertices(tj, ref tri2.V0, ref tri2.V1, ref tri2.V2);
            if (TransformF != null) {
                tri2.V0 = TransformF(tri2.V0);
                tri2.V1 = TransformF(tri2.V1);
                tri2.V2 = TransformF(tri2.V2);
            }
            IntrTriangle3Triangle3 intr = new IntrTriangle3Triangle3(tri1, tri2);
            intr.Find();
            return intr;
        }


        /// <summary>
        /// Convenience function to construct a DistTriangle3Triangle3 object for two mesh triangles, with the distance
        /// already computed, with optional transform on the second triangle. Returns null if either triangle ID is invalid.
        /// </summary>
        public static DistTriangle3Triangle3 TrianglesDistance(DMesh3 mesh1, int ti, DMesh3 mesh2, int tj, Func<Vector3d,Vector3d> TransformF = null)
        {
            if (mesh1.IsTriangle(ti) == false || mesh2.IsTriangle(tj) == false)
                return null;
            Triangle3d tri1 = new Triangle3d(), tri2 = new Triangle3d();
            mesh1.GetTriVertices(ti, ref tri1.V0, ref tri1.V1, ref tri1.V2);
            mesh2.GetTriVertices(tj, ref tri2.V0, ref tri2.V1, ref tri2.V2);
            if (TransformF != null) {
                tri2.V0 = TransformF(tri2.V0);
                tri2.V1 = TransformF(tri2.V1);
                tri2.V2 = TransformF(tri2.V2);
            }
            DistTriangle3Triangle3 dist = new DistTriangle3Triangle3(tri1, tri2);
            dist.GetSquared();
            return dist;
        }




        /// <summary>
        /// Find point-normal frame at the nearest ray-intersection point on mesh, using spatial to find the hit triangle,
        /// or return false if no hit. Returns interpolated vertex-normal frame if available (and bForceFaceNormal is false),
        /// otherwise tri-normal frame.
        /// </summary>
        public static bool RayHitPointFrame(DMesh3 mesh, ISpatial spatial, Ray3d ray, out Frame3f hitPosFrame, bool bForceFaceNormal = false)
        {
            hitPosFrame = new Frame3f();
            int tid = spatial.FindNearestHitTriangle(ray);
            if (tid == DMesh3.InvalidID)
                return false;
            var isect = TriangleIntersection(mesh, tid, ray);
            if (isect.Result != IntersectionResult.Intersects)
                return false;
            Vector3d surfPt = ray.PointAt(isect.RayParameter);
            if (mesh.HasVertexNormals && bForceFaceNormal == false)
                hitPosFrame = SurfaceFrame(mesh, tid, surfPt);      // TODO isect has bary-coords already!!
            else
                hitPosFrame = new Frame3f(surfPt, mesh.GetTriNormal(tid));
            return true;
        }


        /// <summary>
        /// Get point-normal frame on surface of mesh. Assumption is that point lies in tID
        /// (the point is replaced by the point with the same barycentric coordinates in the triangle plane).
        /// Returns interpolated vertex-normal frame if available, otherwise tri-normal frame.
        /// Throws if tID is not a valid triangle.
        /// </summary>
        public static Frame3f SurfaceFrame(DMesh3 mesh, int tID, Vector3d point, bool bForceFaceNormal = false)
        {
            if (!mesh.IsTriangle(tID))
                throw new Exception("MeshQueries.SurfaceFrame: triangle " + tID + " does not exist!");
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tID, ref tri.V0, ref tri.V1, ref tri.V2);
            Vector3d bary = tri.BarycentricCoords(point);
            point = tri.PointAt(bary);
            if (mesh.HasVertexNormals && bForceFaceNormal == false) {
                Vector3d normal = mesh.GetTriBaryNormal(tID, bary.x, bary.y, bary.z);
                return new Frame3f(point, normal);
            } else
                return new Frame3f(point, mesh.GetTriNormal(tID));
        }


        /// <summary>
        /// Get barycentric coords of point in triangle. Throws if tID is not a valid triangle.
        /// </summary>
        public static Vector3d BaryCoords(DMesh3 mesh, int tID, Vector3d point)
        {
            if (!mesh.IsTriangle(tID))
                throw new Exception("MeshQueries.SurfaceFrame: triangle " + tID + " does not exist!");
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tID, ref tri.V0, ref tri.V1, ref tri.V2);
            return tri.BarycentricCoords(point);
        }


        /// <summary>
        /// Compute squared distance from point to triangle ti in mesh, with minimal extra objects/etc.
        /// Degenerate triangles are handled. Does not check that ti is valid.
        /// </summary>
        public static double TriDistanceSqr(DMesh3 mesh, int ti, Vector3d point)
        {
            Vector3d V0 = Vector3d.Zero, V1 = Vector3d.Zero, V2 = Vector3d.Zero;
            mesh.GetTriVertices(ti, ref V0, ref V1, ref V2);

            Vector3d diff = V0 - point;
            Vector3d edge0 = V1 - V0;
            Vector3d edge1 = V2 - V0;
            double a00 = edge0.LengthSquared;
            double a01 = edge0.Dot(ref edge1);
            double a11 = edge1.LengthSquared;
            double b0 = diff.Dot(ref edge0);
            double b1 = diff.Dot(ref edge1);
            double c = diff.LengthSquared;
            double det = Math.Abs(a00 * a11 - a01 * a01);
            double s = a01 * b1 - a11 * b0;
            double t = a01 * b0 - a00 * b1;
            double sqrDistance;
            // degenerate (zero area, or nearly): the nearest of the three edges, instead of NaN
            if (!(det > 1e-12 * a00 * a11) || a00 <= 0 || a11 <= 0) {
                Triangle3d tri = new Triangle3d(V0, V1, V2);
                return DistPoint3Triangle3.DistanceSqr(ref point, ref tri, out Vector3d cp, out Vector3d bc);
            }

            if (s + t <= det) {
                if (s < 0) {
                    if (t < 0) { // region 4
                        if (b0 < 0) {
                            t = 0;
                            if (-b0 >= a00) {
                                s = 1;
                                sqrDistance = a00 + (2) * b0 + c;
                            } else {
                                s = -b0 / a00;
                                sqrDistance = b0 * s + c;
                            }
                        } else {
                            s = 0;
                            if (b1 >= 0) {
                                t = 0;
                                sqrDistance = c;
                            } else if (-b1 >= a11) {
                                t = 1;
                                sqrDistance = a11 + (2) * b1 + c;
                            } else {
                                t = -b1 / a11;
                                sqrDistance = b1 * t + c;
                            }
                        }
                    } else { // region 3
                        s = 0;
                        if (b1 >= 0) {
                            t = 0;
                            sqrDistance = c;
                        } else if (-b1 >= a11) {
                            t = 1;
                            sqrDistance = a11 + (2) * b1 + c;
                        } else {
                            t = -b1 / a11;
                            sqrDistance = b1 * t + c;
                        }
                    }
                } else if (t < 0) { // region 5
                    t = 0;
                    if (b0 >= 0) {
                        s = 0;
                        sqrDistance = c;
                    } else if (-b0 >= a00) {
                        s = 1;
                        sqrDistance = a00 + (2) * b0 + c;
                    } else {
                        s = -b0 / a00;
                        sqrDistance = b0 * s + c;
                    }
                } else { // region 0
                    // minimum at interior point
                    double invDet = (1) / det;
                    s *= invDet;
                    t *= invDet;
                    sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                        t * (a01 * s + a11 * t + (2) * b1) + c;
                }
            } else {
                double tmp0, tmp1, numer, denom;
                if (s < 0) { // region 2
                    tmp0 = a01 + b0;
                    tmp1 = a11 + b1;
                    if (tmp1 > tmp0) {
                        numer = tmp1 - tmp0;
                        denom = a00 - (2) * a01 + a11;
                        if (numer >= denom) {
                            s = 1;
                            t = 0;
                            sqrDistance = a00 + (2) * b0 + c;
                        } else {
                            s = numer / denom;
                            t = 1 - s;
                            sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                                t * (a01 * s + a11 * t + (2) * b1) + c;
                        }
                    } else {
                        s = 0;
                        if (tmp1 <= 0) {
                            t = 1;
                            sqrDistance = a11 + (2) * b1 + c;
                        } else if (b1 >= 0) {
                            t = 0;
                            sqrDistance = c;
                        } else {
                            t = -b1 / a11;
                            sqrDistance = b1 * t + c;
                        }
                    }
                } else if (t < 0) {  // region 6
                    tmp0 = a01 + b1;
                    tmp1 = a00 + b0;
                    if (tmp1 > tmp0) {
                        numer = tmp1 - tmp0;
                        denom = a00 - (2) * a01 + a11;
                        if (numer >= denom) {
                            t = 1;
                            s = 0;
                            sqrDistance = a11 + (2) * b1 + c;
                        } else {
                            t = numer / denom;
                            s = 1 - t;
                            sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                                t * (a01 * s + a11 * t + (2) * b1) + c;
                        }
                    } else {
                        t = 0;
                        if (tmp1 <= 0) {
                            s = 1;
                            sqrDistance = a00 + (2) * b0 + c;
                        } else if (b0 >= 0) {
                            s = 0;
                            sqrDistance = c;
                        } else {
                            s = -b0 / a00;
                            sqrDistance = b0 * s + c;
                        }
                    }
                } else {  // region 1
                    numer = a11 + b1 - a01 - b0;
                    if (numer <= 0) {
                        s = 0;
                        t = 1;
                        sqrDistance = a11 + (2) * b1 + c;
                    } else {
                        denom = a00 - (2) * a01 + a11;
                        if (numer >= denom) {
                            s = 1;
                            t = 0;
                            sqrDistance = a00 + (2) * b0 + c;
                        } else {
                            s = numer / denom;
                            t = 1 - s;
                            sqrDistance = s * (a00 * s + a01 * t + (2) * b0) +
                                t * (a01 * s + a11 * t + (2) * b1) + c;
                        }
                    }
                }
            }

            if (sqrDistance < 0) 
                sqrDistance = 0;
            return sqrDistance;
        }




        /// <summary>
        /// Brute force O(n) search for nearest vertex to point. Returns DMesh3.InvalidID if the mesh has no vertices.
        /// </summary>
        public static int FindNearestVertex_LinearSearch(DMesh3 mesh, Vector3d p)
        {
            int vNearest = DMesh3.InvalidID;
            double fNearestSqr = double.MaxValue;
            foreach ( int vid in mesh.VertexIndices() ) {
                double distSqr = mesh.GetVertex(vid).DistanceSquared(p);
                if (distSqr < fNearestSqr) {
                    fNearestSqr = distSqr;
                    vNearest = vid;
                }
            }
            return vNearest;
        }



        /// <summary>
        /// Brute force O(n) search for nearest triangle to point. Returns DMesh3.InvalidID if the mesh has no triangles.
        /// </summary>
        public static int FindNearestTriangle_LinearSearch(DMesh3 mesh, Vector3d p)
        {
            int tNearest = DMesh3.InvalidID;
            double fNearestSqr = double.MaxValue;
            foreach ( int ti in mesh.TriangleIndices() ) {
                double distSqr = TriDistanceSqr(mesh, ti, p);
                if (distSqr < fNearestSqr) {
                    fNearestSqr = distSqr;
                    tNearest = ti;
                }
            }
            return tNearest;
        }

        /// <summary>
        /// Brute force O(n) search for the triangle with the nearest ray hit (smallest ray parameter).
        /// Returns DMesh3.InvalidID if the ray misses the mesh.
        /// </summary>
        public static int FindHitTriangle_LinearSearch(DMesh3 mesh, Ray3d ray)
        {
            int tNearestID = DMesh3.InvalidID;
            double fNearestT = double.MaxValue;
            Triangle3d tri = new Triangle3d();
            foreach ( int ti in mesh.TriangleIndices() ) {

                // [TODO] optimize this
                mesh.GetTriVertices(ti, ref tri.V0, ref tri.V1, ref tri.V2);
                IntrRay3Triangle3 ray_tri_hit = new IntrRay3Triangle3(ray, tri);
                if ( ray_tri_hit.Find() ) {
                    if ( ray_tri_hit.RayParameter < fNearestT ) {
                        fNearestT = ray_tri_hit.RayParameter;
                        tNearestID = ti;
                    }
                }
            }

            return tNearestID;
        }



        /// <summary>
        /// Brute force O(n*m) search for any intersecting triangles on two meshes. Returns the first
        /// pair (triangle of mesh1, triangle of mesh2) found, or Index2i.Max if there is none.
        /// </summary>
        public static Index2i FindIntersectingTriangles_LinearSearch(DMesh3 mesh1, DMesh3 mesh2)
        {
            foreach (int ti in mesh1.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh1.GetTriVertices(ti, ref a, ref b, ref c);
                foreach (int tj in mesh2.TriangleIndices()) {
                    Vector3d e = Vector3d.Zero, f = Vector3d.Zero, g = Vector3d.Zero;
                    mesh2.GetTriVertices(tj, ref e, ref f, ref g);
                    IntrTriangle3Triangle3 intr = new IntrTriangle3Triangle3(new Triangle3d(a, b, c), new Triangle3d(e, f, g));
                    if (intr.Test()) {
                        return new Index2i(ti, tj);
                    }
                }
            }
            return Index2i.Max;
        }


        /// <summary>
        /// Brute-force O(n*m) search for closest pair of triangles on two meshes. Returns (triangle of mesh1, triangle of mesh2)
        /// and the squared distance in fNearestSqr; Index2i.Max if either mesh is empty.
        /// </summary>
        public static Index2i FindNearestTriangles_LinearSearch(DMesh3 mesh1, DMesh3 mesh2, out double fNearestSqr)
        {
            Index2i nearPair = Index2i.Max;
            fNearestSqr = double.MaxValue;
            foreach (int ti in mesh1.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh1.GetTriVertices(ti, ref a, ref b, ref c);
                foreach ( int tj in mesh2.TriangleIndices() ) {
                    Vector3d e = Vector3d.Zero, f = Vector3d.Zero, g = Vector3d.Zero;
                    mesh2.GetTriVertices(tj, ref e, ref f, ref g);

                    DistTriangle3Triangle3 dist = new DistTriangle3Triangle3(new Triangle3d(a, b, c), new Triangle3d(e, f, g));
                    if ( dist.GetSquared() < fNearestSqr ) {
                        fNearestSqr = dist.GetSquared();
                        nearPair = new Index2i(ti, tj);
                    }
                }
            }
            return nearPair;
        }



        /// <summary>
        /// Compute min/max/average edge length of mesh. If samples &gt; 0, only about that many edges are visited
        /// (in a deterministic stride order) to estimate the statistics. All values are 0 if the mesh has no edges.
        /// </summary>
        public static void EdgeLengthStats(DMesh3 mesh, out double minEdgeLen, out double maxEdgeLen, out double avgEdgeLen, int samples = 0)
        {
            minEdgeLen = double.MaxValue;
            maxEdgeLen = double.MinValue;
            avgEdgeLen = 0;
            int avg_count = 0;
            int MaxID = mesh.MaxEdgeID;
            if (MaxID == 0) {       // no edges
                minEdgeLen = maxEdgeLen = 0;
                return;
            }

            // if we are only taking some samples, use a prime-modulo-loop instead of random
            int nPrime = (samples == 0 ) ? 1 : MathUtil.ModuloIterationStep(MaxID);
            int max_count = (samples == 0) ? MaxID : samples;

            Vector3d a = Vector3d.Zero, b = Vector3d.Zero;
            int eid = 0;
            int count = 0;
            do {
                if (mesh.IsEdge(eid)) {
                    mesh.GetEdgeV(eid, ref a, ref b);
                    double len = a.Distance(b);
                    if (len < minEdgeLen) minEdgeLen = len;
                    if (len > maxEdgeLen) maxEdgeLen = len;
                    avgEdgeLen += len;
                    avg_count++;
                }
                eid = (eid + nPrime) % MaxID;
            } while (eid != 0 && count++ < max_count);

            avgEdgeLen /= (double)avg_count;
        }



        /// <summary>
        /// Compute min/max/average edge length over the given edges (invalid edge IDs are skipped).
        /// The samples parameter is currently ignored.
        /// </summary>
        public static void EdgeLengthStatsFromEdges(DMesh3 mesh, IEnumerable<int> EdgeItr, out double minEdgeLen, out double maxEdgeLen, out double avgEdgeLen, int samples = 0)
        {
            minEdgeLen = double.MaxValue;
            maxEdgeLen = double.MinValue;
            avgEdgeLen = 0;
            int avg_count = 0;
            int MaxID = mesh.MaxEdgeID;

            Vector3d a = Vector3d.Zero, b = Vector3d.Zero;
            foreach ( int eid in EdgeItr ) { 
                if (mesh.IsEdge(eid)) {
                    mesh.GetEdgeV(eid, ref a, ref b);
                    double len = a.Distance(b);
                    if (len < minEdgeLen) minEdgeLen = len;
                    if (len > maxEdgeLen) maxEdgeLen = len;
                    avgEdgeLen += len;
                    avg_count++;
                }
            };
            avgEdgeLen /= (double)avg_count;
        }



    }
}
