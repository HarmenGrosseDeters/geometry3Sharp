// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Exact arrangement of the triangles of several meshes: every triangle is split along its intersections
    /// with the triangles of the other meshes, so that the pieces only meet along shared edges and vertices.
    ///
    /// All input coordinates are converted exactly to one integer grid (a double is m * 2^e). Every new point is
    /// the intersection of three planes with integer coefficients (the triangle planes, and for each triangle edge
    /// the plane through the edge perpendicular to the triangle), computed as a canonical rational point
    /// (ExactPoint3), so equal points are identical, also when they are computed from different triangles.
    /// Each split triangle is retriangulated with an exact constrained Delaunay triangulation in a coordinate
    /// projection. A point on an edge is always also computed by the triangles on the other side of the edge
    /// (it is the end of their intersection with the same triangle), so the pieces stay connected.
    ///
    /// Intersections between triangles of the same mesh are computed if SelfIntersections is set; then also
    /// T-junctions (a vertex on the edge of a neighbour, closed by a degenerate triangle) are split, since the
    /// triangles touch along a segment. Degenerate (zero-area) triangles are not part of the output.
    /// </summary>
    internal class ExactMeshArrangement
    {
        public struct Plane
        {
            // A x + B y + C z = D
            public BigInteger A, B, C, D;
            public Plane(BigInteger a, BigInteger b, BigInteger c, BigInteger d) { A = a; B = b; C = c; D = d; }
        }

        public struct Segment
        {
            public int P, Q;
            public Plane Line;     // a plane that intersects the triangle plane in the segment's line
            public Segment(int p, int q, Plane line) { P = p; Q = q; Line = line; }
        }

        public class InputTriangle
        {
            public int Mesh, Tid;
            public int[] Corners = new int[3];      // point ids
            public Vector3d V0, V1, V2;             // input coordinates of the corners
            public bool Degenerate;
            public List<Segment> Segments = new List<Segment>();
            public HashSet<int> Points = new HashSet<int>();
            public List<int> Coplanar = new List<int>();    // input triangles of other meshes in the same plane, intersecting

            // planes are computed when first needed (most triangles are far from all intersections)
            internal ChunkedList<ExactPoint3> pts;
            Plane p;
            Plane[] edgePlanes;
            int axis, sign;
            bool haveP;

            public Plane P { get { ensure(); return p; } }
            /// <summary> edge k: Corners[k] -> Corners[k+1], plane through the edge perpendicular to the triangle </summary>
            public Plane[] EdgePlanes { get { ensure(); return edgePlanes; } }
            /// <summary> unnormalized normal (b-a)x(c-a) </summary>
            public BigInteger NX { get { ensure(); return p.A; } }
            public BigInteger NY { get { ensure(); return p.B; } }
            public BigInteger NZ { get { ensure(); return p.C; } }
            /// <summary> projection: drop Axis; Sign = sign of the normal component on that axis </summary>
            public int Axis { get { ensure(); return axis; } }
            public int Sign { get { ensure(); return sign; } }

            void ensure()
            {
                if (haveP)
                    return;
                if (Degenerate)
                    throw new InvalidOperationException("ExactMeshArrangement: degenerate triangle has no plane");
                ExactPoint3 a = pts[Corners[0]], b = pts[Corners[1]], c = pts[Corners[2]];
                BigInteger ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                BigInteger vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                BigInteger nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                p = new Plane(nx, ny, nz, nx * a.X + ny * a.Y + nz * a.Z);
                BigInteger ax = BigInteger.Abs(nx), ay = BigInteger.Abs(ny), az = BigInteger.Abs(nz);
                axis = (ax >= ay && ax >= az) ? 0 : (ay >= az) ? 1 : 2;
                sign = ((axis == 0) ? nx : (axis == 1) ? ny : nz).Sign;
                edgePlanes = new Plane[3];
                for (int k = 0; k < 3; ++k)
                    edgePlanes[k] = edge_plane(nx, ny, nz, pts[Corners[k]], pts[Corners[(k + 1) % 3]]);
                haveP = true;
            }

            // plane through the integer points p, q, perpendicular to the triangle plane
            static Plane edge_plane(BigInteger NX, BigInteger NY, BigInteger NZ, ExactPoint3 p, ExactPoint3 q)
            {
                BigInteger dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                BigInteger nx = dy * NZ - dz * NY;
                BigInteger ny = dz * NX - dx * NZ;
                BigInteger nz = dx * NY - dy * NX;
                return new Plane(nx, ny, nz, nx * p.X + ny * p.Y + nz * p.Z);
            }
        }

        public class OutputTriangle
        {
            public int Mesh, Source;     // Source: index into Triangles
            public int A, B, C;
        }

        public List<DMesh3> Meshes;
        public int ScaleExp;        // input coordinate = integer coordinate * 2^ScaleExp
        public ChunkedList<ExactPoint3> Points = new ChunkedList<ExactPoint3>();
        public List<InputTriangle> Triangles = new List<InputTriangle>();
        public List<OutputTriangle> Output = new List<OutputTriangle>();
        public HashSet<long> ConstraintEdges = new HashSet<long>(EdgeKeyComparer.Instance);     // undirected point id pairs on intersection curves
        public int[][] VertexPoints;        // per mesh: vertex id -> point id
        public List<int>[] MeshTriangles;   // per mesh: indices into Triangles
        public int IntersectingPairs;
        public int SegmentCrossings;        // points where two intersection segments cross inside a triangle

        // point ids by value, in shards (by hash) so that threads adding points rarely wait for each other
        const int Shards = 64;
        readonly Dictionary<ExactPoint3, int>[] pointIndex = make_shards();
        int nextPoint;
        static Dictionary<ExactPoint3, int>[] make_shards()
        {
            var d = new Dictionary<ExactPoint3, int>[Shards];
            for (int i = 0; i < Shards; ++i)
                d[i] = new Dictionary<ExactPoint3, int>(1024);
            return d;
        }

        public ExactMeshArrangement(List<DMesh3> meshes)
        {
            Meshes = meshes;
        }

        public static long EdgeKey(int a, int b)
        {
            return (a < b) ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a);
        }

        /// <summary> integer grid offset: input coordinate = (point + Center) * 2^ScaleExp (keeps the filters' error bounds small) </summary>
        public BigInteger[] Center = { BigInteger.Zero, BigInteger.Zero, BigInteger.Zero };
        // double approximations of the points (grid units, relative error at most 4 * 2^-52), for the floating-point filters
        readonly ChunkedList<Vector3d> approx = new ChunkedList<Vector3d>();

        /// <summary> id of the point, a new one if it is not known yet (thread-safe) </summary>
        public int AddPoint(ExactPoint3 p)
        {
            // the approximation outside the lock (also when the point exists: cheap compared to waiting)
            Vector3d a = p.ApproxVector3d();
            var shard = pointIndex[(p.GetHashCode() & 0x7FFFFFFF) % Shards];
            lock (shard) {
                int id;
                if (shard.TryGetValue(p, out id))
                    return id;
                id = System.Threading.Interlocked.Increment(ref nextPoint) - 1;
                approx.Set(id, a);
                Points.Set(id, p);
                // published after the point is stored: other threads only see the id once Points[id] is valid
                shard[p] = id;
                return id;
            }
        }

        /// <summary> lexicographic order of the exact coordinates (x, then y, then z): deterministic, unlike point ids </summary>
        public int ComparePoints(int a, int b)
        {
            if (a == b)
                return 0;
            Vector3d fa = approx[a], fb = approx[b];
            ExactPoint3 pa = Points[a], pb = Points[b];
            for (int k = 0; k < 3; ++k) {
                double tol = 16 * U * (Math.Abs(fa[k]) + Math.Abs(fb[k]));
                if (fa[k] < fb[k] - tol) return -1;
                if (fa[k] > fb[k] + tol) return 1;
                int c = (pa[k] * pb.W).CompareTo(pb[k] * pa.W);
                if (c != 0) return c;
            }
            return 0;
        }

        /// <summary> input coordinates of a point (rounded to double) </summary>
        public Vector3d PointToVector(int id)
        {
            return ToInputVector(Points[id]);
        }

        /// <summary> input coordinates of a grid point (rounded to double) </summary>
        public Vector3d ToInputVector(ExactPoint3 p)
        {
            return new Vector3d(ExactPoint3.ToDouble(p.X + Center[0] * p.W, p.W, ScaleExp),
                ExactPoint3.ToDouble(p.Y + Center[1] * p.W, p.W, ScaleExp), ExactPoint3.ToDouble(p.Z + Center[2] * p.W, p.W, ScaleExp));
        }

        const double U = 2.220446049250313e-16;     // 2^-52
        // for tests: always evaluate the predicates exactly
        internal bool DisableFilters = false;


        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        public void Compute()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            build_points();
            build_triangles();
            PhaseMilliseconds["setup"] = sw.ElapsedMilliseconds; sw.Restart();
            find_intersections();
            PhaseMilliseconds["intersections"] = sw.ElapsedMilliseconds; sw.Restart();
            gParallel.ForEach(Interval1i.Range(Triangles.Count), (ti) => prepare_triangle(Triangles[ti]));
            var pieces = new List<OutputTriangle>[Triangles.Count];
            var constraints = new List<long>[Triangles.Count];
            gParallel.ForEach(Interval1i.Range(Triangles.Count), (ti) => triangulate(ti, out pieces[ti], out constraints[ti]));
            for (int ti = 0; ti < Triangles.Count; ++ti) {
                if (pieces[ti] != null)
                    Output.AddRange(pieces[ti]);
                if (constraints[ti] != null)
                    foreach (long key in constraints[ti])
                        ConstraintEdges.Add(key);
            }
            PhaseMilliseconds["triangulation"] = sw.ElapsedMilliseconds;
        }



        void build_points()
        {
            // common exponent: every coordinate is an integer multiple of 2^ScaleExp
            int eMin = int.MaxValue;
            foreach (DMesh3 mesh in Meshes) {
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    for (int k = 0; k < 3; ++k) {
                        BigInteger m; int e;
                        ExactPoint3.Decompose(v[k], out m, out e);
                        if (m.IsZero == false)
                            eMin = Math.Min(eMin, e);
                    }
                }
            }
            if (eMin == int.MaxValue)
                eMin = 0;
            ScaleExp = eMin;

            // integer coordinates, relative to the center of their box
            var coords = new List<BigInteger[]>[Meshes.Count];
            BigInteger[] min = new BigInteger[3], max = new BigInteger[3];
            bool first = true;
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                DMesh3 mesh = Meshes[mi];
                coords[mi] = new List<BigInteger[]>();
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    BigInteger[] c = new BigInteger[3];
                    for (int k = 0; k < 3; ++k) {
                        BigInteger m; int e;
                        ExactPoint3.Decompose(v[k], out m, out e);
                        c[k] = m.IsZero ? BigInteger.Zero : (m << (e - eMin));
                        if (first || c[k] < min[k]) min[k] = c[k];
                        if (first || c[k] > max[k]) max[k] = c[k];
                    }
                    first = false;
                    coords[mi].Add(c);
                }
            }
            if (first == false)
                for (int k = 0; k < 3; ++k)
                    Center[k] = (min[k] + max[k]) / 2;
            VertexPoints = new int[Meshes.Count][];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                DMesh3 mesh = Meshes[mi];
                VertexPoints[mi] = new int[mesh.MaxVertexID];
                int i = 0;
                foreach (int vid in mesh.VertexIndices()) {
                    BigInteger[] c = coords[mi][i++];
                    VertexPoints[mi][vid] = AddPoint(ExactPoint3.FromInteger(c[0] - Center[0], c[1] - Center[1], c[2] - Center[2]));
                }
            }
        }


        void build_triangles()
        {
            MeshTriangles = new List<int>[Meshes.Count];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                MeshTriangles[mi] = new List<int>();
                DMesh3 mesh = Meshes[mi];
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    InputTriangle it = new InputTriangle() { Mesh = mi, Tid = tid };
                    it.Corners[0] = VertexPoints[mi][t.a];
                    it.Corners[1] = VertexPoints[mi][t.b];
                    it.Corners[2] = VertexPoints[mi][t.c];
                    it.pts = Points;
                    // degenerate iff the corners are collinear, ie collinear in all three coordinate projections (exact)
                    Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
                    it.V0 = a; it.V1 = b; it.V2 = c;
                    it.Degenerate = ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) == 0
                        && ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) == 0
                        && ExactPredicates.Orient2d(new Vector2d(a.z, a.x), new Vector2d(b.z, b.x), new Vector2d(c.z, c.x)) == 0;
                    MeshTriangles[mi].Add(Triangles.Count);
                    Triangles.Add(it);
                }
            }
        }

        /// <summary>
        /// The point where the segment between the input points p, q crosses the plane (the endpoints are on
        /// opposite sides): (s_p q - s_q p) / (s_p - s_q) with s the plane values, cheaper than PlanesPoint.
        /// </summary>
        public int EdgePlanePoint(int p, int q, Plane plane)
        {
            ExactPoint3 P = Points[p], Q = Points[q];
            BigInteger sp = plane.A * P.X + plane.B * P.Y + plane.C * P.Z - plane.D * P.W;
            BigInteger sq = plane.A * Q.X + plane.B * Q.Y + plane.C * Q.Z - plane.D * Q.W;
            // homogeneous: P = (X, W), so the point is (sp Q - sq P) with weights sp Wq - sq Wp
            return AddPoint(ExactPoint3.Create(sp * Q.X - sq * P.X, sp * Q.Y - sq * P.Y, sp * Q.Z - sq * P.Z, sp * Q.W - sq * P.W));
        }

        /// <summary> the point common to three planes (they must be independent) </summary>
        public int PlanesPoint(Plane p1, Plane p2, Plane p3)
        {
            // Cramer: x = (d1 (n2 x n3) + d2 (n3 x n1) + d3 (n1 x n2)) / det(n1, n2, n3)
            BigInteger c23x = p2.B * p3.C - p2.C * p3.B, c23y = p2.C * p3.A - p2.A * p3.C, c23z = p2.A * p3.B - p2.B * p3.A;
            BigInteger c31x = p3.B * p1.C - p3.C * p1.B, c31y = p3.C * p1.A - p3.A * p1.C, c31z = p3.A * p1.B - p3.B * p1.A;
            BigInteger c12x = p1.B * p2.C - p1.C * p2.B, c12y = p1.C * p2.A - p1.A * p2.C, c12z = p1.A * p2.B - p1.B * p2.A;
            BigInteger det = p1.A * c23x + p1.B * c23y + p1.C * c23z;
            if (det.IsZero)
                throw new InvalidOperationException("ExactMeshArrangement: planes are not independent");
            return AddPoint(ExactPoint3.Create(
                p1.D * c23x + p2.D * c31x + p3.D * c12x,
                p1.D * c23y + p2.D * c31y + p3.D * c12y,
                p1.D * c23z + p2.D * c31z + p3.D * c12z, det));
        }


        /// <summary> orientation of three points in the projection of triangle t, +1 if counter-clockwise like t </summary>
        public int Orient(InputTriangle t, int p, int q, int r)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            // filter: the error of the determinant of the approximations is below 96 u m^2 (forward error analysis
            // with coordinate errors 4 u m); 128 u m^2 with a margin
            Vector3d fa = approx[p], fb = approx[q], fc = approx[r];
            double bx = fb[i] - fa[i], by = fb[j] - fa[j], cx = fc[i] - fa[i], cy = fc[j] - fa[j];
            double fdet = bx * cy - by * cx;
            double m = Math.Max(Math.Max(Math.Max(Math.Abs(fa[i]), Math.Abs(fa[j])), Math.Max(Math.Abs(fb[i]), Math.Abs(fb[j]))), Math.Max(Math.Abs(fc[i]), Math.Abs(fc[j])));
            double bound = DisableFilters ? double.MaxValue : 128 * U * m * m;
            if (fdet > bound) return t.Sign;
            if (-fdet > bound) return -t.Sign;
            ExactPoint3 a = Points[p], b = Points[q], c = Points[r];
            BigInteger det = ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W);
            return det.Sign * t.Sign;
        }

        /// <summary> +1 if s is inside the circle through p, q, r (counter-clockwise in the projection of t) </summary>
        public int InCircle(InputTriangle t, int p, int q, int r, int s)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            {
                // filter: entries up to 2m (differences) and 8m^2 (lifts), terms of degree 4, coordinate errors
                // 4 u m; the error of the determinant of the approximations is far below 16384 u m^4
                Vector3d fa = approx[p], fb = approx[q], fc = approx[r], fd = approx[s];
                double adx = fa[i] - fd[i], ady = fa[j] - fd[j], bdx = fb[i] - fd[i], bdy = fb[j] - fd[j], cdx = fc[i] - fd[i], cdy = fc[j] - fd[j];
                double fdet = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                    + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                    + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
                double mm = Math.Max(Math.Max(Math.Max(Math.Abs(fa[i]), Math.Abs(fa[j])), Math.Max(Math.Abs(fb[i]), Math.Abs(fb[j]))),
                    Math.Max(Math.Max(Math.Abs(fc[i]), Math.Abs(fc[j])), Math.Max(Math.Abs(fd[i]), Math.Abs(fd[j]))));
                double m2 = mm * mm;
                double bound = DisableFilters ? double.MaxValue : 16384 * U * m2 * m2;
                if (fdet > bound) return t.Sign;
                if (-fdet > bound) return -t.Sign;
            }
            ExactPoint3 a = Points[p], b = Points[q], c = Points[r], d = Points[s];
            // rows (x w, y w, x^2 + y^2, w^2) = w^2 (x/w, y/w, |.|^2, 1); with orientation flipped by t.Sign
            BigInteger[,] m = new BigInteger[4, 4];
            ExactPoint3[] pts = { a, b, c, d };
            for (int k = 0; k < 4; ++k) {
                ExactPoint3 v = pts[k];
                m[k, 0] = v[i] * v.W;
                m[k, 1] = v[j] * v.W;
                m[k, 2] = v[i] * v[i] + v[j] * v[j];
                m[k, 3] = v.W * v.W;
            }
            BigInteger det = Det4(m);
            // for counter-clockwise a, b, c this determinant is positive iff d is inside; the projection
            // may mirror the triangle, which flips the orientation of a, b, c and the sign
            return det.Sign * t.Sign;
        }

        public static BigInteger Det4(BigInteger[,] m)
        {
            BigInteger det = BigInteger.Zero;
            for (int c = 0; c < 4; ++c) {
                int c0 = (c == 0) ? 1 : 0, c1 = (c <= 1) ? 2 : 1, c2 = (c <= 2) ? 3 : 2;
                BigInteger minor = ExactPoint3.Det3(
                    m[1, c0], m[1, c1], m[1, c2],
                    m[2, c0], m[2, c1], m[2, c2],
                    m[3, c0], m[3, c1], m[3, c2]);
                det += ((c % 2 == 0) ? m[0, c] : -m[0, c]) * minor;
            }
            return det;
        }

        // closed triangle contains point (which lies in its plane)
        bool inside(InputTriangle t, int r)
        {
            return Orient(t, t.Corners[0], t.Corners[1], r) >= 0
                && Orient(t, t.Corners[1], t.Corners[2], r) >= 0
                && Orient(t, t.Corners[2], t.Corners[0], r) >= 0;
        }

        // position of point along a direction: compare (d . p1) / w1 with (d . p2) / w2
        internal int compare_along(BigInteger dx, BigInteger dy, BigInteger dz, int p1, int p2)
        {
            if (p1 == p2)
                return 0;
            {
                // filter: direction rounded to double (relative error 2u), points with error 4 u m; the error of the
                // approximate difference of the keys is below 20 u |d|_1 m, 64 with a margin
                double fdx = (double)dx, fdy = (double)dy, fdz = (double)dz;
                if (DisableFilters == false && double.IsInfinity(fdx) == false && double.IsInfinity(fdy) == false && double.IsInfinity(fdz) == false) {
                    Vector3d fa = approx[p1], fb = approx[p2];
                    double diff = fdx * (fa.x - fb.x) + fdy * (fa.y - fb.y) + fdz * (fa.z - fb.z);
                    double m = Math.Max(Math.Max(Math.Max(Math.Abs(fa.x), Math.Abs(fa.y)), Math.Max(Math.Abs(fa.z), Math.Abs(fb.x))), Math.Max(Math.Abs(fb.y), Math.Abs(fb.z)));
                    double bound = 64 * U * (Math.Abs(fdx) + Math.Abs(fdy) + Math.Abs(fdz)) * m;
                    if (diff > bound) return 1;
                    if (-diff > bound) return -1;
                }
            }
            ExactPoint3 a = Points[p1], b = Points[p2];
            BigInteger ka = (dx * a.X + dy * a.Y + dz * a.Z) * b.W;
            BigInteger kb = (dx * b.X + dy * b.Y + dz * b.Z) * a.W;
            return ka.CompareTo(kb);
        }

        // direction of the line where the plane line meets the triangle plane
        static void line_direction(InputTriangle t, Plane line, out BigInteger dx, out BigInteger dy, out BigInteger dz)
        {
            dx = t.NY * line.C - t.NZ * line.B;
            dy = t.NZ * line.A - t.NX * line.C;
            dz = t.NX * line.B - t.NY * line.A;
        }



        /// <summary> also intersect the triangles of each mesh with each other (self-intersections) </summary>
        public bool SelfIntersections = false;
        public int SelfIntersectingPairs;
        /// <summary> per mesh: box tree of its triangles (in the order of MeshTriangles), after Compute() </summary>
        public TriangleBoxTree[] Trees;

        void find_intersections()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // boxes of the exact input coordinates
            var boxes = new List<AxisAlignedBox3d>[Meshes.Count];
            var trees = Trees = new TriangleBoxTree[Meshes.Count];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                boxes[mi] = new List<AxisAlignedBox3d>();
                foreach (int ti in MeshTriangles[mi])
                    boxes[mi].Add(triangle_box(Triangles[ti]));
                trees[mi] = new TriangleBoxTree(boxes[mi]);
            }
            PhaseMilliseconds["intersections.trees"] = sw.ElapsedMilliseconds; sw.Restart();
            // candidate pairs (boxes overlap, not separated by a plane, not only touching), in parallel per triangle
            var pairs = new List<long>();
            for (int ma = 0; ma < Meshes.Count; ++ma) {
                for (int mb = (SelfIntersections ? ma : ma + 1); mb < Meshes.Count; ++mb) {
                    int na = MeshTriangles[ma].Count;
                    var found = new List<long>[na];
                    int self = 0;
                    gParallel.ForEach(Interval1i.Range(na), (ka) => {
                        InputTriangle T = Triangles[MeshTriangles[ma][ka]];
                        if (T.Degenerate) return;
                        trees[mb].Query(boxes[ma][ka], (kb) => {
                            if (ma == mb && kb <= ka)
                                return;
                            InputTriangle U = Triangles[MeshTriangles[mb][kb]];
                            if (U.Degenerate)
                                return;
                            if (ma == mb) {
                                if (touch_only(T, U))
                                    return;
                            } else if (separated(T, U))
                                return;
                            if (ma == mb)
                                System.Threading.Interlocked.Increment(ref self);
                            if (found[ka] == null)
                                found[ka] = new List<long>();
                            found[ka].Add(((long)MeshTriangles[ma][ka] << 32) | (uint)MeshTriangles[mb][kb]);
                        });
                    });
                    SelfIntersectingPairs += self;
                    foreach (var list in found)
                        if (list != null)
                            pairs.AddRange(list);
                }
            }
            PhaseMilliseconds["intersections.pairs"] = sw.ElapsedMilliseconds; sw.Restart();
            // planes of the triangles involved (computed once, before the parallel use)
            var needed = new bool[Triangles.Count];
            foreach (long pr in pairs) {
                needed[(int)(pr >> 32)] = true;
                needed[(int)(pr & 0xFFFFFFFF)] = true;
            }
            gParallel.ForEach(Interval1i.Range(Triangles.Count), (ti) => {
                if (needed[ti]) { var plane = Triangles[ti].P; }
            });
            PhaseMilliseconds["intersections.planes"] = sw.ElapsedMilliseconds; sw.Restart();
            gParallel.ForEach(Interval1i.Range(pairs.Count), (k) => intersect((int)(pairs[k] >> 32), (int)(pairs[k] & 0xFFFFFFFF)));
            PhaseMilliseconds["intersections.intersect"] = sw.ElapsedMilliseconds;
            CandidatePairs = pairs.Count;
        }
        public int CandidatePairs;

        // all corners of one triangle strictly on one side of the other's plane (exact double predicates)
        bool separated(InputTriangle T, InputTriangle U)
        {
            Vector3d t0 = input_vertex(T, 0), t1 = input_vertex(T, 1), t2 = input_vertex(T, 2);
            Vector3d u0 = input_vertex(U, 0), u1 = input_vertex(U, 1), u2 = input_vertex(U, 2);
            int a = ExactPredicates.Orient3d(u0, u1, u2, t0), b = ExactPredicates.Orient3d(u0, u1, u2, t1), c = ExactPredicates.Orient3d(u0, u1, u2, t2);
            if ((a > 0 && b > 0 && c > 0) || (a < 0 && b < 0 && c < 0))
                return true;
            a = ExactPredicates.Orient3d(t0, t1, t2, u0); b = ExactPredicates.Orient3d(t0, t1, t2, u1); c = ExactPredicates.Orient3d(t0, t1, t2, u2);
            return (a > 0 && b > 0 && c > 0) || (a < 0 && b < 0 && c < 0);
        }

        // triangles of one mesh that share vertices and do not intersect anywhere else (exact)
        bool touch_only(InputTriangle T, InputTriangle U)
        {
            int shared = 0;
            foreach (int c in U.Corners)
                if (c == T.Corners[0] || c == T.Corners[1] || c == T.Corners[2])
                    shared++;
            // quick test (not for a shared edge, there the full test is one predicate and 2D tests): the corners
            // of one triangle that are not shared all strictly on one side of the other's plane: then they meet
            // at most in the shared vertices
            if (shared < 2 && (one_side_except_shared(T, U) || one_side_except_shared(U, T)))
                return true;
            // corners by point id, so that coincident (unwelded) vertices count as shared
            Index3i pa = new Index3i(T.Corners[0], T.Corners[1], T.Corners[2]), pb = new Index3i(U.Corners[0], U.Corners[1], U.Corners[2]);
            Triangle3d A = new Triangle3d(T.V0, T.V1, T.V2), B = new Triangle3d(U.V0, U.V1, U.V2);
            return ExactTriangleIntersection.IntersectsBeyondShared(pa, ref A, pb, ref B) == false;
        }

        bool one_side_except_shared(InputTriangle T, InputTriangle U)
        {
            Vector3d t0 = input_vertex(T, 0), t1 = input_vertex(T, 1), t2 = input_vertex(T, 2);
            int side = 0;
            for (int k = 0; k < 3; ++k) {
                int c = U.Corners[k];
                if (c == T.Corners[0] || c == T.Corners[1] || c == T.Corners[2])
                    continue;
                int o = ExactPredicates.Orient3d(t0, t1, t2, input_vertex(U, k));
                if (o == 0 || (side != 0 && o != side))
                    return false;
                side = o;
            }
            return side != 0;
        }

        static AxisAlignedBox3d triangle_box(InputTriangle t)
        {
            AxisAlignedBox3d box = new AxisAlignedBox3d(t.V0);
            box.Contain(t.V1);
            box.Contain(t.V2);
            return box;
        }

        static Vector3d input_vertex(InputTriangle t, int k)
        {
            return (k == 0) ? t.V0 : (k == 1) ? t.V1 : t.V2;
        }


        void intersect(int ti, int ui)
        {
            InputTriangle T = Triangles[ti], U = Triangles[ui];
            Vector3d t0 = input_vertex(T, 0), t1 = input_vertex(T, 1), t2 = input_vertex(T, 2);
            Vector3d u0 = input_vertex(U, 0), u1 = input_vertex(U, 1), u2 = input_vertex(U, 2);
            // sides with the exact double predicates (same sign as N . (v - a) on the integer grid)
            int[] sT = { ExactPredicates.Orient3d(u0, u1, u2, t0), ExactPredicates.Orient3d(u0, u1, u2, t1), ExactPredicates.Orient3d(u0, u1, u2, t2) };
            if ((sT[0] > 0 && sT[1] > 0 && sT[2] > 0) || (sT[0] < 0 && sT[1] < 0 && sT[2] < 0))
                return;
            int[] sU = { ExactPredicates.Orient3d(t0, t1, t2, u0), ExactPredicates.Orient3d(t0, t1, t2, u1), ExactPredicates.Orient3d(t0, t1, t2, u2) };
            if ((sU[0] > 0 && sU[1] > 0 && sU[2] > 0) || (sU[0] < 0 && sU[1] < 0 && sU[2] < 0))
                return;

            if (sT[0] == 0 && sT[1] == 0 && sT[2] == 0) {
                intersect_coplanar(ti, ui);
                return;
            }

            List<int> pT = plane_points(T, sT, U.P), pU = plane_points(U, sU, T.P);
            BigInteger dx = T.NY * U.NZ - T.NZ * U.NY, dy = T.NZ * U.NX - T.NX * U.NZ, dz = T.NX * U.NY - T.NY * U.NX;
            int loT, hiT, loU, hiU;
            min_max(pT, dx, dy, dz, out loT, out hiT);
            min_max(pU, dx, dy, dz, out loU, out hiU);
            int lo = (compare_along(dx, dy, dz, loT, loU) >= 0) ? loT : loU;
            int hi = (compare_along(dx, dy, dz, hiT, hiU) <= 0) ? hiT : hiU;
            int c = compare_along(dx, dy, dz, lo, hi);
            if (c > 0)
                return;
            System.Threading.Interlocked.Increment(ref IntersectingPairs);
            if (c == 0) {
                lock (T) T.Points.Add(lo);
                lock (U) U.Points.Add(lo);
            } else {
                lock (T) T.Segments.Add(new Segment(lo, hi, U.P));
                lock (U) U.Segments.Add(new Segment(lo, hi, T.P));
            }
        }

        // the points where the boundary of t meets the plane (t is not in the plane)
        List<int> plane_points(InputTriangle t, int[] side, Plane plane)
        {
            var pts = new List<int>();
            for (int k = 0; k < 3; ++k) {
                if (side[k] == 0)
                    pts.Add(t.Corners[k]);
                int k1 = (k + 1) % 3;
                if (side[k] * side[k1] < 0)
                    pts.Add(EdgePlanePoint(t.Corners[k], t.Corners[k1], plane));
            }
            return pts;
        }

        void min_max(List<int> pts, BigInteger dx, BigInteger dy, BigInteger dz, out int lo, out int hi)
        {
            lo = hi = pts[0];
            for (int k = 1; k < pts.Count; ++k) {
                if (compare_along(dx, dy, dz, pts[k], lo) < 0) lo = pts[k];
                if (compare_along(dx, dy, dz, pts[k], hi) > 0) hi = pts[k];
            }
        }


        void intersect_coplanar(int ti, int ui)
        {
            InputTriangle T = Triangles[ti], U = Triangles[ui];
            bool any = false;
            // the edges of each triangle, clipped to the other, become constraints of the other
            for (int k = 0; k < 3; ++k) {
                any |= clip_into(T, U.Corners[k], U.Corners[(k + 1) % 3], U.EdgePlanes[k]);
                any |= clip_into(U, T.Corners[k], T.Corners[(k + 1) % 3], T.EdgePlanes[k]);
            }
            if (any) {
                System.Threading.Interlocked.Increment(ref IntersectingPairs);
                lock (T) T.Coplanar.Add(ui);
                lock (U) U.Coplanar.Add(ti);
            }
        }

        // clip segment p-q (in the plane of t, on the line t.P ∩ line) to the closed triangle t, add the result to t
        bool clip_into(InputTriangle t, int p, int q, Plane line)
        {
            var cands = new List<int>();
            if (inside(t, p)) cands.Add(p);
            if (inside(t, q)) cands.Add(q);
            BigInteger dx, dy, dz;
            line_direction(t, line, out dx, out dy, out dz);
            for (int k = 0; k < 3; ++k) {
                int ci = t.Corners[k], cj = t.Corners[(k + 1) % 3];
                int o1 = Orient(t, p, q, ci), o2 = Orient(t, p, q, cj);
                if (o1 == 0 && between(dx, dy, dz, p, q, ci))
                    cands.Add(ci);
                if (o1 * o2 < 0) {
                    int o3 = Orient(t, ci, cj, p), o4 = Orient(t, ci, cj, q);
                    if (o3 * o4 < 0)
                        cands.Add(PlanesPoint(t.P, line, t.EdgePlanes[k]));
                }
            }
            if (cands.Count == 0)
                return false;
            int lo, hi;
            min_max(cands, dx, dy, dz, out lo, out hi);
            lock (t) {
                if (lo == hi)
                    t.Points.Add(lo);
                else
                    t.Segments.Add(new Segment(lo, hi, line));
            }
            return true;
        }

        // r (on the line p-q) is in the closed segment p-q
        bool between(BigInteger dx, BigInteger dy, BigInteger dz, int p, int q, int r)
        {
            int a = compare_along(dx, dy, dz, p, r), b = compare_along(dx, dy, dz, r, q);
            return (a <= 0 && b <= 0) || (a >= 0 && b >= 0);
        }



        // points where the segments of t cross (only happens with three or more meshes, or self-intersections)
        void prepare_triangle(InputTriangle t)
        {
            if (t.Degenerate || (t.Segments.Count == 0 && t.Points.Count == 0))
                return;
            // unique segments
            // deterministic order (the pairs were intersected in parallel)
            t.Segments.Sort((x, y) => {
                int xa = (ComparePoints(x.P, x.Q) <= 0) ? x.P : x.Q, xb = (xa == x.P) ? x.Q : x.P;
                int ya = (ComparePoints(y.P, y.Q) <= 0) ? y.P : y.Q, yb = (ya == y.P) ? y.Q : y.P;
                int c = ComparePoints(xa, ya);
                return (c != 0) ? c : ComparePoints(xb, yb);
            });
            t.Coplanar.Sort();
            var unique = new List<Segment>();
            var seen = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (Segment s in t.Segments) {
                if (s.P == s.Q) { t.Points.Add(s.P); continue; }
                if (seen.Add(EdgeKey(s.P, s.Q)))
                    unique.Add(s);
            }
            t.Segments = unique;
            foreach (Segment s in unique) {
                t.Points.Add(s.P);
                t.Points.Add(s.Q);
            }
            for (int i = 0; i < unique.Count; ++i) {
                Segment a = unique[i];
                for (int j = i + 1; j < unique.Count; ++j) {
                    Segment b = unique[j];
                    if (a.P == b.P || a.P == b.Q || a.Q == b.P || a.Q == b.Q)
                        continue;
                    int o1 = Orient(t, a.P, a.Q, b.P), o2 = Orient(t, a.P, a.Q, b.Q);
                    if (o1 * o2 >= 0) continue;
                    int o3 = Orient(t, b.P, b.Q, a.P), o4 = Orient(t, b.P, b.Q, a.Q);
                    if (o3 * o4 >= 0) continue;
                    t.Points.Add(PlanesPoint(t.P, a.Line, b.Line));
                    System.Threading.Interlocked.Increment(ref SegmentCrossings);
                }
            }
            foreach (int c in t.Corners)
                t.Points.Remove(c);
        }


        void triangulate(int ti, out List<OutputTriangle> output, out List<long> constraints)
        {
            output = null;
            constraints = null;
            InputTriangle t = Triangles[ti];
            if (t.Degenerate)
                return;
            output = new List<OutputTriangle>();
            var pts = new HashSet<int>(t.Points);
            foreach (int c in t.Corners)
                pts.Remove(c);
            if (pts.Count == 0 && t.Segments.Count == 0) {
                output.Add(new OutputTriangle() { Mesh = t.Mesh, Source = ti, A = t.Corners[0], B = t.Corners[1], C = t.Corners[2] });
                return;
            }
            constraints = new List<long>();

            var tri = new ExactTriangulation2(t.Corners[0], t.Corners[1], t.Corners[2],
                (a, b, c) => Orient(t, a, b, c), (a, b, c, d) => InCircle(t, a, b, c, d));
            var ordered = new List<int>(pts);
            ordered.Sort(ComparePoints);    // deterministic (point ids are not, they are assigned in parallel)
            foreach (int p in ordered)
                tri.InsertPoint(p);
            tri.MakeDelaunay();

            // segments split at the points on them
            var all = new List<int>(ordered);
            all.AddRange(t.Corners);
            foreach (Segment s in t.Segments) {
                BigInteger dx, dy, dz;
                line_direction(t, s.Line, out dx, out dy, out dz);
                var on = new List<int>() { s.P, s.Q };
                foreach (int r in all) {
                    if (r == s.P || r == s.Q) continue;
                    if (Orient(t, s.P, s.Q, r) == 0 && between(dx, dy, dz, s.P, s.Q, r))
                        on.Add(r);
                }
                on.Sort((a, b) => compare_along(dx, dy, dz, a, b));
                for (int k = 0; k + 1 < on.Count; ++k) {
                    tri.InsertConstraint(on[k], on[k + 1]);
                    constraints.Add(EdgeKey(on[k], on[k + 1]));
                }
            }
            tri.MakeDelaunay();
            foreach (Index3i f in tri.Triangles())
                output.Add(new OutputTriangle() { Mesh = t.Mesh, Source = ti, A = f.a, B = f.b, C = f.c });
        }
    }



    /// <summary>
    /// Append-only list in fixed-size chunks that are never moved: elements can be read while another thread
    /// appends (appends must be serialized by the caller).
    /// </summary>
    public class ChunkedList<T>
    {
        const int Shift = 12, Size = 1 << Shift, Mask = Size - 1;
        volatile T[][] chunks = new T[16][];
        int count;

        public int Count { get { return System.Threading.Volatile.Read(ref count); } }

        public T this[int i] { get { return chunks[i >> Shift][i & Mask]; } }

        readonly object growLock = new object();

        /// <summary> set element i (thread-safe for distinct i; Count becomes at least i + 1) </summary>
        public void Set(int i, T v)
        {
            int ci = i >> Shift;
            T[][] ch = chunks;
            if (ci >= ch.Length || ch[ci] == null) {
                lock (growLock) {
                    ch = chunks;
                    if (ci >= ch.Length) {
                        int n = ch.Length;
                        while (n <= ci) n *= 2;
                        T[][] grown = new T[n][];
                        Array.Copy(ch, grown, ch.Length);
                        chunks = ch = grown;
                    }
                    if (ch[ci] == null)
                        ch[ci] = new T[Size];
                }
            }
            ch[ci][i & Mask] = v;
            int c;
            while ((c = System.Threading.Volatile.Read(ref count)) < i + 1)
                if (System.Threading.Interlocked.CompareExchange(ref count, i + 1, c) == c)
                    break;
        }

        public void Add(T v)
        {
            int c = count, ci = c >> Shift;
            T[][] ch = chunks;
            if (ci == ch.Length) {
                T[][] grown = new T[2 * ch.Length][];
                Array.Copy(ch, grown, ch.Length);
                chunks = ch = grown;
            }
            if (ch[ci] == null)
                ch[ci] = new T[Size];
            ch[ci][c & Mask] = v;
            System.Threading.Volatile.Write(ref count, c + 1);
        }
    }



    /// <summary>
    /// Hash for edge keys (a << 32 | b): the default long hash is a ^ b, which collides for most edges of a mesh
    /// </summary>
    internal sealed class EdgeKeyComparer : IEqualityComparer<long>
    {
        public static readonly EdgeKeyComparer Instance = new EdgeKeyComparer();
        public bool Equals(long a, long b) { return a == b; }
        public int GetHashCode(long key)
        {
            // SplitMix64 finalizer
            ulong z = (ulong)key + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)z ^ (int)(z >> 32);
        }
    }



    /// <summary>
    /// Bounding volume hierarchy of boxes with closed overlap tests and no padding, so that it never
    /// misses a pair of touching boxes (for exact computations).
    /// </summary>
    internal class TriangleBoxTree
    {
        readonly List<AxisAlignedBox3d> boxes;
        readonly int[] order;
        readonly double[] keys;     // sort keys while building
        readonly List<AxisAlignedBox3d> nodeBox = new List<AxisAlignedBox3d>();
        readonly List<int> nodeStart = new List<int>(), nodeCount = new List<int>(), nodeLeft = new List<int>(), nodeRight = new List<int>();
        const int LeafSize = 8;

        public TriangleBoxTree(List<AxisAlignedBox3d> boxes)
        {
            this.boxes = boxes;
            order = new int[boxes.Count];
            for (int i = 0; i < order.Length; ++i)
                order[i] = i;
            keys = new double[boxes.Count];
            if (order.Length > 0)
                build(0, order.Length);
        }

        int build(int start, int count)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            for (int i = start; i < start + count; ++i)
                box.Contain(boxes[order[i]]);
            int node = nodeBox.Count;
            nodeBox.Add(box); nodeStart.Add(start); nodeCount.Add(count); nodeLeft.Add(-1); nodeRight.Add(-1);
            if (count <= LeafSize)
                return node;
            Vector3d d = box.Diagonal;
            int axis = (d.x >= d.y && d.x >= d.z) ? 0 : (d.y >= d.z) ? 1 : 2;
            for (int i = start; i < start + count; ++i)
                keys[i] = boxes[order[i]].Min[axis] + boxes[order[i]].Max[axis];
            int half = count / 2;
            select(start, start + count - 1, start + half);
            int left = build(start, half);
            int right = build(start + half, count - half);
            nodeLeft[node] = left;
            nodeRight[node] = right;
            return node;
        }

        /// <summary> call f for every box that the segment p-q intersects, with all boxes expanded by pad </summary>
        public void QuerySegment(Vector3d p, Vector3d q, double pad, Action<int> f)
        {
            if (nodeBox.Count == 0)
                return;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (segment_hits(nodeBox[node], p, q, pad) == false)
                    continue;
                if (nodeLeft[node] < 0) {
                    for (int i = nodeStart[node]; i < nodeStart[node] + nodeCount[node]; ++i)
                        if (segment_hits(boxes[order[i]], p, q, pad))
                            f(order[i]);
                } else {
                    stack.Push(nodeLeft[node]);
                    stack.Push(nodeRight[node]);
                }
            }
        }

        // slab test of segment p-q against the box expanded by pad (conservative: pad covers the rounding)
        static bool segment_hits(AxisAlignedBox3d box, Vector3d p, Vector3d q, double pad)
        {
            double t0 = 0, t1 = 1;
            for (int k = 0; k < 3; ++k) {
                double lo = box.Min[k] - pad, hi = box.Max[k] + pad;
                double d = q[k] - p[k];
                if (Math.Abs(d) < 1e-300) {
                    if (p[k] < lo || p[k] > hi) return false;
                    continue;
                }
                double ta = (lo - p[k]) / d, tb = (hi - p[k]) / d;
                if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                // widen the interval a little against rounding of the divisions
                double slack = 1e-9 * (Math.Abs(ta) + Math.Abs(tb)) + 1e-12;
                t0 = Math.Max(t0, ta - slack);
                t1 = Math.Min(t1, tb + slack);
                if (t0 > t1) return false;
            }
            return true;
        }

        // partial sort of order[lo..hi] by keys so that position k holds its element and the halves are split
        // there (Hoare quickselect)
        void select(int lo, int hi, int k)
        {
            while (lo < hi) {
                double pivot = keys[(lo + hi) >> 1];
                int i = lo, j = hi;
                while (i <= j) {
                    while (keys[i] < pivot) i++;
                    while (keys[j] > pivot) j--;
                    if (i <= j) {
                        double tk = keys[i]; keys[i] = keys[j]; keys[j] = tk;
                        int to = order[i]; order[i] = order[j]; order[j] = to;
                        i++; j--;
                    }
                }
                if (k <= j) hi = j;
                else if (k >= i) lo = i;
                else return;
            }
        }

        static bool overlaps(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return !(a.Max.x < b.Min.x || a.Min.x > b.Max.x || a.Max.y < b.Min.y || a.Min.y > b.Max.y || a.Max.z < b.Min.z || a.Min.z > b.Max.z);
        }

        /// <summary> call f for every box that overlaps the closed query box </summary>
        public void Query(AxisAlignedBox3d query, Action<int> f)
        {
            if (nodeBox.Count == 0)
                return;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (overlaps(nodeBox[node], query) == false)
                    continue;
                if (nodeLeft[node] < 0) {
                    for (int i = nodeStart[node]; i < nodeStart[node] + nodeCount[node]; ++i)
                        if (overlaps(boxes[order[i]], query))
                            f(order[i]);
                } else {
                    stack.Push(nodeLeft[node]);
                    stack.Push(nodeRight[node]);
                }
            }
        }
    }
}
