// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Geodesic Bezier curves on a triangle mesh: de Casteljau's algorithm with the linear interpolation between two
    /// points replaced by the point at the same fraction of the geodesic between them (Morera, Carvalho, Velho,
    /// "Modeling on Triangulations with Geodesic Curves", 2008). The geodesics are found by edge flips (FlipOut: Sharp
    /// and Crane, ACM TOG 2020, see MeshGeodesicPath); the interpolated points are inserted into one shared
    /// IntrinsicTriangulation of the mesh as vertices (SplitEdge), so they are exact surface points. The mesh is not
    /// modified.
    ///
    /// The control points are mesh vertices (two or more: degree = count - 1; two give the geodesic). Compute
    /// evaluates the curve at the given parameters in [0, 1] (Points) and joins consecutive points by geodesics
    /// (Path, a polyline on the surface). On a plane the curve is the ordinary Bezier curve. Like the geodesic between
    /// two points, the curve is not unique where a geodesic between two of the intermediate points is not (cut locus);
    /// the geodesic found is the locally shortest one in the homotopy class of the shortest edge path.
    /// Status is that of the first geodesic that was not found (NoPath, Stuck, NotConverged), InvalidInput, or
    /// TraceFailed if a point could not be inserted or the path not traced; Success otherwise. Each interpolation
    /// runs a Dijkstra search over the whole (growing) triangulation, O(N log N); there are degree (degree + 1) / 2 per
    /// parameter, so large meshes with many parameters are slow.
    /// </summary>
    public class MeshGeodesicBezier
    {
        public DMesh3 Mesh { get; private set; }

        /// <summary> see MeshGeodesicPath.AngleTolerance </summary>
        public double AngleTolerance = 1e-6;
        /// <summary>
        /// an interpolated point within this fraction of a segment's length of one of its ends is that end (instead of
        /// a vertex a few ulp away that makes needle triangles)
        /// </summary>
        public double SnapTolerance = 1e-9;

        /// <summary> the largest number of segments (and parameters) accepted </summary>
        public const int MaxSamples = 1 << 20;

        public GeodesicPathStatus Status { get; private set; }
        /// <summary> the parameters of the last computation </summary>
        public List<double> Parameters { get; private set; }
        /// <summary> the curve points on the surface, one per parameter (the first is the first control vertex at t = 0) </summary>
        public List<Vector3d> Points { get; private set; }
        /// <summary> the polyline through Points, with the points where the geodesics between them cross mesh edges </summary>
        public List<Vector3d> Path { get; private set; }
        /// <summary> length of Path </summary>
        public double Length { get; private set; }
        /// <summary> the triangulation of the last computation, with the inserted points as vertices </summary>
        public IntrinsicTriangulation Triangulation { get; private set; }

        public MeshGeodesicBezier(DMesh3 mesh)
        {
            Mesh = mesh;
            reset();
        }

        void reset()
        {
            Parameters = new List<double>();
            Points = new List<Vector3d>();
            Path = new List<Vector3d>();
            Length = 0;
        }

        GeodesicPathStatus fail(GeodesicPathStatus status)
        {
            Status = status;
            reset();
            return status;
        }

        /// <summary> the curve at segments + 1 equally spaced parameters 0, 1/segments, ..., 1 </summary>
        public GeodesicPathStatus Compute(IList<int> controlVertices, int segments)
        {
            if (segments < 1 || segments > MaxSamples) return fail(GeodesicPathStatus.InvalidInput);
            var t = new double[segments + 1];
            for (int i = 0; i <= segments; ++i) t[i] = (double)i / segments;
            return Compute(controlVertices, t);
        }

        /// <summary> the curve at the given parameters, which must lie in [0, 1] and not decrease </summary>
        public GeodesicPathStatus Compute(IList<int> controlVertices, IList<double> parameters)
        {
            reset();
            Triangulation = null;
            if (controlVertices == null || parameters == null || controlVertices.Count < 2 || parameters.Count < 1 || parameters.Count > MaxSamples + 1)
                return fail(GeodesicPathStatus.InvalidInput);
            for (int i = 0; i < parameters.Count; ++i)
                if (!(parameters[i] >= 0 && parameters[i] <= 1) || (i > 0 && parameters[i] < parameters[i - 1]))
                    return fail(GeodesicPathStatus.InvalidInput);
            IntrinsicTriangulation T;
            try {
                T = new IntrinsicTriangulation(Mesh);
            } catch (ArgumentException) {
                return fail(GeodesicPathStatus.InvalidInput);
            }
            Triangulation = T;
            var control = new int[controlVertices.Count];
            for (int i = 0; i < control.Length; ++i) {
                control[i] = T.VertexIndex(controlVertices[i]);
                if (control[i] < 0) return fail(GeodesicPathStatus.InvalidInput);
            }
            var leg = new MeshGeodesicPath(Mesh) { AngleTolerance = AngleTolerance };

            // the curve points as vertices of T (vertices never move, so earlier points stay valid while T is flipped)
            var curve = new List<int>();
            foreach (double t in parameters) {
                int v = evaluate(T, leg, control, t);
                if (v < 0) return fail(leg.Status == GeodesicPathStatus.Success ? GeodesicPathStatus.TraceFailed : leg.Status);
                curve.Add(v);
            }
            var points = new List<Vector3d>();
            foreach (int v in curve) points.Add(T.VertexPosition(v));

            // join consecutive different points by geodesics
            var path = new List<Vector3d>();
            double length = 0;
            for (int i = 0; i < curve.Count; ++i) {
                if (i == 0) { path.Add(points[0]); continue; }
                GeodesicPathStatus st = leg.ComputeIn(T, curve[i - 1], curve[i], true);
                if (st != GeodesicPathStatus.Success) return fail(st);
                length += leg.Length;
                for (int k = 1; k < leg.Path.Count; ++k) path.Add(leg.Path[k]);
            }
            Parameters = new List<double>(parameters);
            Points = points;
            Path = path;
            Length = length;
            Status = GeodesicPathStatus.Success;
            return Status;
        }

        // de Casteljau at t; the vertex of T, or -1 (leg.Status says why, if it is not Success)
        int evaluate(IntrinsicTriangulation T, MeshGeodesicPath leg, int[] control, double t)
        {
            var cur = (int[])control.Clone();
            for (int level = cur.Length - 1; level >= 1; --level) {
                for (int i = 0; i < level; ++i) {
                    int v = lerp(T, leg, cur[i], cur[i + 1], t);
                    if (v < 0) return -1;
                    cur[i] = v;
                }
            }
            return cur[0];
        }

        // the point at fraction t of the geodesic from a to b
        int lerp(IntrinsicTriangulation T, MeshGeodesicPath leg, int a, int b, double t)
        {
            if (a == b || t <= 0) return a;
            if (t >= 1) return b;
            if (leg.ComputeIn(T, a, b, false) != GeodesicPathStatus.Success) return -1;
            return leg.VertexAtFraction(t, SnapTolerance);
        }
    }
}
