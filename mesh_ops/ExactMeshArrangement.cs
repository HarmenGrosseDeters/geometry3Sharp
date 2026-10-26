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
    /// All input coordinates are converted exactly to one integer grid (a double is m * 2^e). New points are
    /// implicit: an edge crossing a triangle plane, two coplanar edges crossing, or three planes (the triangle
    /// planes, and for each triangle edge the plane through the edge perpendicular to the triangle). A point is
    /// stored as its construction with a double approximation and a bound on its error; the predicates decide
    /// from the approximations when the bounds allow it, and compute the point as a canonical rational
    /// (ExactPoint3) only when they do not. The same construction gives the same point id; coincident points
    /// from different constructions are merged afterwards (merge_points), so equal points have one id.
    /// Each split triangle is retriangulated with an exact constrained Delaunay triangulation in a coordinate
    /// projection. A point on an edge is always also computed by the triangles on the other side of the edge
    /// (it is the end of their intersection with the same triangle), so the pieces stay connected.
    ///
    /// Intersections between triangles of the same mesh are computed if SelfIntersections is set; then also
    /// T-junctions (a vertex on the edge of a neighbour, closed by a degenerate triangle) are split, since the
    /// triangles touch along a segment. Degenerate (zero-area) triangles are not part of the output.
    ///
    /// Sources (implemented from the papers, no code taken over; see docs/SOURCES.md): Zhou, Grinspun, Zorin,
    /// Jacobson, "Mesh Arrangements for Solid Geometry", 2016 (exact arrangement); Attene, "Indirect Predicates
    /// for Geometric Constructions", 2020, and Cherchi, Livesu, Scateni, Attene, "Fast and Robust Mesh
    /// Arrangements using Floating-point Arithmetic", 2020 (implicit points with filters); Shewchuk, "Adaptive
    /// Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates", 1997 (error bounds); Sloan,
    /// "A fast algorithm for generating constrained Delaunay triangulations", 1993.
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
            public int Line;       // key of a plane that intersects the triangle plane in the segment's line (PlaneOf)
            public Segment(int p, int q, int line) { P = p; Q = q; Line = line; }
        }

        public class InputTriangle
        {
            public int Mesh, Tid;
            public int[] Corners = new int[3];      // point ids
            public Vector3d V0, V1, V2;             // input coordinates of the corners
            public bool Degenerate;
            // (allocated when the triangle is touched: most are not; the shared empty ones must not be changed, not
            // even sorted)
            List<Segment> segments;
            HashSet<int> points;
            List<int> coplanar;
            static readonly List<Segment> noSegments = new List<Segment>();
            static readonly HashSet<int> noPoints = new HashSet<int>();
            static readonly List<int> noCoplanar = new List<int>();
            public List<Segment> Segments { get { return segments ?? noSegments; } set { segments = value; } }
            public HashSet<int> Points { get { return points ?? noPoints; } set { points = value; } }
            /// <summary> input triangles of other meshes in the same plane, intersecting </summary>
            public List<int> Coplanar { get { return coplanar ?? noCoplanar; } }
            internal void AddSegment(Segment s) { (segments ?? (segments = new List<Segment>())).Add(s); }
            internal void AddPoint(int p) { (points ?? (points = new HashSet<int>())).Add(p); }
            internal void AddCoplanar(int t) { (coplanar ?? (coplanar = new List<int>())).Add(t); }

            // planes are computed when first needed (most triangles are far from all intersections)
            internal ExactMeshArrangement arr;
            Plane p;
            Plane[] edgePlanes;
            double[] planeApprox;
            int axis, sign;
            bool haveP;

            public Plane P { get { ensure(); return p; } }
            /// <summary> edge k: Corners[k] -> Corners[k+1], plane through the edge perpendicular to the triangle </summary>
            public Plane[] EdgePlanes { get { ensure(); return edgePlanes; } }
            /// <summary> the planes P, EdgePlanes[0..2] as doubles (A, B, C, D each, relative error at most 2^-52) </summary>
            internal double[] PlaneApprox { get { ensure(); return planeApprox; } }
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
                ExactPoint3 a = arr.Exact(Corners[0]), b = arr.Exact(Corners[1]), c = arr.Exact(Corners[2]);
                BigInteger ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                BigInteger vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                BigInteger nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                Plane tp = new Plane(nx, ny, nz, nx * a.X + ny * a.Y + nz * a.Z);
                BigInteger ax = BigInteger.Abs(nx), ay = BigInteger.Abs(ny), az = BigInteger.Abs(nz);
                int ta = (ax >= ay && ax >= az) ? 0 : (ay >= az) ? 1 : 2;
                int ts = ((ta == 0) ? nx : (ta == 1) ? ny : nz).Sign;
                var ep = new Plane[3];
                for (int k = 0; k < 3; ++k)
                    ep[k] = edge_plane(nx, ny, nz, arr.Exact(Corners[k]), arr.Exact(Corners[(k + 1) % 3]));
                var pa = new double[16];
                for (int k = 0; k < 4; ++k) {
                    Plane q = (k == 0) ? tp : ep[k - 1];
                    pa[4 * k] = (double)q.A; pa[4 * k + 1] = (double)q.B; pa[4 * k + 2] = (double)q.C; pa[4 * k + 3] = (double)q.D;
                }
                // (the fields are set before haveP: other threads use them once haveP is set)
                p = tp; axis = ta; sign = ts; edgePlanes = ep; planeApprox = pa;
                System.Threading.Volatile.Write(ref haveP, true);
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

        public struct OutputTriangle
        {
            public int Mesh, Source;     // Source: index into Triangles
            public int A, B, C;
        }

        public List<DMesh3> Meshes;
        public int ScaleExp;        // input coordinate = integer coordinate * 2^ScaleExp
        public List<InputTriangle> Triangles = new List<InputTriangle>();
        public List<OutputTriangle> Output = new List<OutputTriangle>();
        public HashSet<long> ConstraintEdges = new HashSet<long>(EdgeKeyComparer.Instance);     // undirected point id pairs on intersection curves
        public int[][] VertexPoints;        // per mesh: vertex id -> point id
        public List<int>[] MeshTriangles;   // per mesh: indices into Triangles
        public int IntersectingPairs;
        public int SegmentCrossings;        // points where two intersection segments cross inside a triangle
        /// <summary> segment pairs and segment-point pairs examined in the triangles (for tests: about linear in their number) </summary>
        public long CandidateTests;
        /// <summary> points that were merged because different constructions gave the same point </summary>
        public int MergedPoints;

        public ExactMeshArrangement(List<DMesh3> meshes)
        {
            Meshes = meshes;
        }

        public static long EdgeKey(int a, int b)
        {
            return (a < b) ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a);
        }

        /// <summary> plane key: 4 * triangle for the triangle's plane, 4 * triangle + 1 + k for its edge plane k </summary>
        public Plane PlaneOf(int key)
        {
            InputTriangle t = Triangles[key >> 2];
            return ((key & 3) == 0) ? t.P : t.EdgePlanes[(key & 3) - 1];
        }



        /// <summary> how a point is constructed </summary>
        internal enum PointKind : byte
        {
            Input = 0,          // an input vertex (A = point id): its coordinates are exact doubles
            Rational = 1,       // given exactly (AddPoint); A = point id
            EdgePlane = 2,      // edge A (EdgeKey of two input points) crossing plane B (plane key)
            Segments = 3,       // edges A, B (EdgeKeys of input points, A < B) crossing, in a common plane, projected by dropping axis C
            Planes = 4          // the planes A < B < C (plane keys)
        }

        /// <summary> construction of a point: equal keys give the same point (but equal points can have different keys) </summary>
        internal struct PointKey : IEquatable<PointKey>, IComparable<PointKey>
        {
            public readonly PointKind Kind;
            public readonly long A, B, C;
            public PointKey(PointKind kind, long a, long b, long c) { Kind = kind; A = a; B = b; C = c; }

            public bool Equals(PointKey o) { return Kind == o.Kind && A == o.A && B == o.B && C == o.C; }
            public override bool Equals(object obj) { return obj is PointKey && Equals((PointKey)obj); }
            public override int GetHashCode()
            {
                ulong h = (ulong)Kind;
                h = mix(h ^ (ulong)A);
                h = mix(h ^ (ulong)B);
                h = mix(h ^ (ulong)C);
                return (int)h ^ (int)(h >> 32);
            }
            static ulong mix(ulong z)
            {
                // SplitMix64 finalizer
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
            public int CompareTo(PointKey o)
            {
                if (Kind != o.Kind) return Kind.CompareTo(o.Kind);
                if (A != o.A) return A.CompareTo(o.A);
                if (B != o.B) return B.CompareTo(o.B);
                return C.CompareTo(o.C);
            }
        }

        internal sealed class ExactBox
        {
            public readonly ExactPoint3 P;
            public ExactBox(ExactPoint3 p) { P = p; }
        }

        /// <summary> integer grid offset: input coordinate = (point + Center) * 2^ScaleExp (keeps the filters' error bounds small) </summary>
        public BigInteger[] Center = { BigInteger.Zero, BigInteger.Zero, BigInteger.Zero };
        double[] centerD = new double[3];   // Center, exactly (a double)
        // per point: double approximation (grid units), a bound on the error of each coordinate, the construction,
        // the exact point (when computed); input points also have their input coordinates
        readonly ChunkedList<Vector3d> approx = new ChunkedList<Vector3d>();
        readonly ChunkedList<double> approxErr = new ChunkedList<double>();
        readonly ChunkedList<PointKey> keys = new ChunkedList<PointKey>();
        readonly ChunkedList<ExactBox> exactPoints = new ChunkedList<ExactBox>();
        readonly ChunkedList<Vector3d> inputCoords = new ChunkedList<Vector3d>();
        int nextPoint;
        // implicit point ids by construction, in shards (by hash) so that threads adding points rarely wait for each other
        const int Shards = 64;
        readonly Dictionary<PointKey, int>[] keyIndex = make_shards();
        readonly Dictionary<ExactPoint3, int> rationalIndex = new Dictionary<ExactPoint3, int>();
        static Dictionary<PointKey, int>[] make_shards()
        {
            var d = new Dictionary<PointKey, int>[Shards];
            for (int i = 0; i < Shards; ++i)
                d[i] = new Dictionary<PointKey, int>(1024);
            return d;
        }
        // canonical id of each point after merge_points (null before), for ids below its length
        int[] canonical;

        public int PointCount { get { return System.Threading.Volatile.Read(ref nextPoint); } }

        /// <summary> the id that stands for all points equal to this one (after Compute: every id in Output is canonical) </summary>
        public int Canonical(int id)
        {
            int[] c = canonical;
            return (c != null && id < c.Length) ? c[id] : id;
        }

        internal PointKind KindOf(int id) { return keys[id].Kind; }
        internal double ApproxError(int id) { return approxErr[id]; }
        internal Vector3d Approx(int id) { return approx[id]; }

        int new_id()
        {
            return System.Threading.Interlocked.Increment(ref nextPoint) - 1;
        }

        void store(int id, Vector3d a, double e, PointKey key, ExactBox exact)
        {
            approx.Set(id, a);
            approxErr.Set(id, e);
            keys.Set(id, key);
            exactPoints.Set(id, exact);
        }

        /// <summary> id of an exactly given point, a new one if it is not known yet (thread-safe) </summary>
        public int AddPoint(ExactPoint3 p)
        {
            Vector3d a = p.ApproxVector3d();
            double e = (p.W.IsOne ? 1 : 4) * U * max_abs(a) + Tiny;
            lock (rationalIndex) {
                int id;
                if (rationalIndex.TryGetValue(p, out id))
                    return id;
                id = new_id();
                store(id, a, e, new PointKey(PointKind.Rational, id, 0, 0), new ExactBox(p));
                // published after the point is stored: other threads only see the id once its data is valid
                rationalIndex[p] = id;
                return id;
            }
        }

        // id of an implicit point (thread-safe); a, e: its approximation and error bound
        int add_implicit(PointKey key, Vector3d a, double e)
        {
            ExactBox exact = null;
            if (finite(a) == false || double.IsNaN(e) || double.IsInfinity(e)) {
                // out of the double range (or no usable bound): from the exact point
                ExactPoint3 x = compute_exact(key, true);
                exact = new ExactBox(x);
                a = x.ApproxVector3d();
                e = 4 * U * max_abs(a) + Tiny;
            }
            var shard = keyIndex[(key.GetHashCode() & 0x7FFFFFFF) % Shards];
            lock (shard) {
                int id;
                if (shard.TryGetValue(key, out id))
                    return id;
                id = new_id();
                store(id, a, e, key, exact);
                shard[key] = id;
                return id;
            }
        }

        /// <summary> the exact point (computed when first needed; thread-safe) </summary>
        public ExactPoint3 Exact(int id)
        {
            ExactBox b = exactPoints[id];
            if (b == null) {
                PointKey key = keys[id];
                b = new ExactBox((key.Kind == PointKind.Input) ? input_exact(inputCoords[id]) : compute_exact(key, true));
                // (another thread may store the same value)
                exactPoints.Set(id, b);
            }
            return b.P;
        }

        // the grid point of input coordinates
        ExactPoint3 input_exact(Vector3d v)
        {
            return ExactPoint3.FromInteger(GridValue(v.x, 0), GridValue(v.y, 1), GridValue(v.z, 2));
        }

        /// <summary> the grid coordinate (relative to Center) of the input coordinate v on axis k, exactly </summary>
        public BigInteger GridValue(double v, int k)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            return (m.IsZero ? BigInteger.Zero : (m << (e - ScaleExp))) - Center[k];
        }

        // the point of a construction, canonical if reduce (else W > 0 but not reduced, cheaper)
        ExactPoint3 compute_exact(PointKey key, bool reduce)
        {
            BigInteger x, y, z, w;
            switch (key.Kind) {
                case PointKind.EdgePlane:
                    edge_plane_exact(Exact((int)(key.A >> 32)), Exact((int)(key.A & 0xFFFFFFFF)), PlaneOf((int)key.B), out x, out y, out z, out w);
                    break;
                case PointKind.Segments:
                    segments_exact(Exact((int)(key.A >> 32)), Exact((int)(key.A & 0xFFFFFFFF)),
                        Exact((int)(key.B >> 32)), Exact((int)(key.B & 0xFFFFFFFF)), (int)key.C, out x, out y, out z, out w);
                    break;
                case PointKind.Planes:
                    planes_exact(PlaneOf((int)key.A), PlaneOf((int)key.B), PlaneOf((int)key.C), out x, out y, out z, out w);
                    break;
                default:
                    throw new InvalidOperationException("ExactMeshArrangement: rational point without its value");
            }
            return reduce ? ExactPoint3.Create(x, y, z, w) : ExactPoint3.CreateUnreduced(x, y, z, w);
        }

        // where the segment P-Q crosses the plane (the endpoints are on opposite sides): (s_p Q - s_q P) / (s_p - s_q)
        // with s the plane values (homogeneous)
        static void edge_plane_exact(ExactPoint3 P, ExactPoint3 Q, Plane plane, out BigInteger x, out BigInteger y, out BigInteger z, out BigInteger w)
        {
            BigInteger sp = plane.A * P.X + plane.B * P.Y + plane.C * P.Z - plane.D * P.W;
            BigInteger sq = plane.A * Q.X + plane.B * Q.Y + plane.C * Q.Z - plane.D * Q.W;
            x = sp * Q.X - sq * P.X; y = sp * Q.Y - sq * P.Y; z = sp * Q.Z - sq * P.Z; w = sp * Q.W - sq * P.W;
        }

        // where the segment P-Q crosses the line R-S (in the same plane), in the projection that drops axis
        static void segments_exact(ExactPoint3 P, ExactPoint3 Q, ExactPoint3 R, ExactPoint3 S, int axis, out BigInteger x, out BigInteger y, out BigInteger z, out BigInteger w)
        {
            int i = (axis + 1) % 3, j = (axis + 2) % 3;
            BigInteger sp = ExactPoint3.Det3(R[i], R[j], R.W, S[i], S[j], S.W, P[i], P[j], P.W);
            BigInteger sq = ExactPoint3.Det3(R[i], R[j], R.W, S[i], S[j], S.W, Q[i], Q[j], Q.W);
            x = sp * Q.X - sq * P.X; y = sp * Q.Y - sq * P.Y; z = sp * Q.Z - sq * P.Z; w = sp * Q.W - sq * P.W;
        }

        // the point common to three planes (they must be independent)
        static void planes_exact(Plane p1, Plane p2, Plane p3, out BigInteger x, out BigInteger y, out BigInteger z, out BigInteger w)
        {
            // Cramer: x = (d1 (n2 x n3) + d2 (n3 x n1) + d3 (n1 x n2)) / det(n1, n2, n3)
            BigInteger c23x = p2.B * p3.C - p2.C * p3.B, c23y = p2.C * p3.A - p2.A * p3.C, c23z = p2.A * p3.B - p2.B * p3.A;
            BigInteger c31x = p3.B * p1.C - p3.C * p1.B, c31y = p3.C * p1.A - p3.A * p1.C, c31z = p3.A * p1.B - p3.B * p1.A;
            BigInteger c12x = p1.B * p2.C - p1.C * p2.B, c12y = p1.C * p2.A - p1.A * p2.C, c12z = p1.A * p2.B - p1.B * p2.A;
            w = p1.A * c23x + p1.B * c23y + p1.C * c23z;
            if (w.IsZero)
                throw new InvalidOperationException("ExactMeshArrangement: planes are not independent");
            x = p1.D * c23x + p2.D * c31x + p3.D * c12x;
            y = p1.D * c23y + p2.D * c31y + p3.D * c12y;
            z = p1.D * c23z + p2.D * c31z + p3.D * c12z;
        }

        /// <summary> the point where the segment between the rational points p, q crosses the plane, as a rational point </summary>
        public int EdgePlanePoint(int p, int q, Plane plane)
        {
            BigInteger x, y, z, w;
            edge_plane_exact(Exact(p), Exact(q), plane, out x, out y, out z, out w);
            return AddPoint(ExactPoint3.Create(x, y, z, w));
        }

        /// <summary> the point common to three planes (they must be independent), as a rational point </summary>
        public int PlanesPoint(Plane p1, Plane p2, Plane p3)
        {
            BigInteger x, y, z, w;
            planes_exact(p1, p2, p3, out x, out y, out z, out w);
            return AddPoint(ExactPoint3.Create(x, y, z, w));
        }



        // ---- implicit points: constructions with their approximations ----

        /// <summary> where edge p-q of rational points crosses the plane (key); the ends are on opposite sides </summary>
        internal int EdgePlanePointImplicit(int p, int q, int plane)
        {
            if (p > q) { int tmp = p; p = q; q = tmp; }
            double[] pa = Triangles[plane >> 2].PlaneApprox;
            int o = 4 * (plane & 3);
            Vector3d P = approx[p], Q = approx[q];
            double ep = approxErr[p], eq = approxErr[q];
            double esp, esq;
            double sp = plane_value(pa, o, P, ep, out esp), sq = plane_value(pa, o, Q, eq, out esq);
            double e;
            Vector3d a = segment_point(P, ep, Q, eq, sp, esp, sq, esq, out e);
            return add_implicit(new PointKey(PointKind.EdgePlane, EdgeKey(p, q), plane, 0), a, e);
        }

        /// <summary> where the edges p-q and r-s of rational points cross (in one plane, projected by dropping axis) </summary>
        internal int SegmentsPointImplicit(int p, int q, int r, int s, int axis)
        {
            long e1 = EdgeKey(p, q), e2 = EdgeKey(r, s);
            if (e1 > e2) { long tmp = e1; e1 = e2; e2 = tmp; }
            // the first edge is the parametrized one, the second defines the line
            p = (int)(e1 >> 32); q = (int)(e1 & 0xFFFFFFFF); r = (int)(e2 >> 32); s = (int)(e2 & 0xFFFFFFFF);
            int i = (axis + 1) % 3, j = (axis + 2) % 3;
            Vector3d P = approx[p], Q = approx[q], R = approx[r], S = approx[s];
            double ep = approxErr[p], eq = approxErr[q], er = Math.Max(approxErr[r], approxErr[s]);
            double esp, esq;
            double sp = orient2d_approx(R, S, P, i, j, Math.Max(er, ep), out esp);
            double sq = orient2d_approx(R, S, Q, i, j, Math.Max(er, eq), out esq);
            double e;
            Vector3d a = segment_point(P, ep, Q, eq, sp, esp, sq, esq, out e);
            return add_implicit(new PointKey(PointKind.Segments, e1, e2, axis), a, e);
        }

        /// <summary> the point common to three planes (keys) of different triangles </summary>
        internal int PlanesPointImplicit(int k1, int k2, int k3)
        {
            if (k1 > k2) { int tmp = k1; k1 = k2; k2 = tmp; }
            if (k2 > k3) { int tmp = k2; k2 = k3; k3 = tmp; }
            if (k1 > k2) { int tmp = k1; k1 = k2; k2 = tmp; }
            double e;
            Vector3d a = planes_approx(k1, k2, k3, out e);
            return add_implicit(new PointKey(PointKind.Planes, k1, k2, k3), a, e);
        }

        const double U = 2.220446049250313e-16;     // 2^-52
        // absolute term of the error bounds of approximations: relative bounds (multiples of U) fail where a result
        // is subnormal or underflows to zero (absolute error up to 2^-1075 per rounding, or the whole value); far
        // above that, and far below anything a grid coordinate (an integer, or a quotient of bounded integers near
        // a grid point) needs
        internal const double Tiny = 1e-300;
        // for tests: always evaluate the predicates exactly
        internal bool DisableFilters = false;

        static double max_abs(Vector3d a)
        {
            return Math.Max(Math.Abs(a.x), Math.Max(Math.Abs(a.y), Math.Abs(a.z)));
        }

        static bool finite(Vector3d a)
        {
            double s = a.x + a.y + a.z;
            return double.IsNaN(s) == false && double.IsInfinity(s) == false;
        }

        // A x + B y + C z - D for the plane at pa[o..o+3] (relative errors 2^-52) and a point with coordinate errors e
        internal static double plane_value(double[] pa, int o, Vector3d x, double e, out double err)
        {
            double A = pa[o], B = pa[o + 1], C = pa[o + 2], D = pa[o + 3];
            double ax = A * x.x, by = B * x.y, cz = C * x.z;
            // coefficients U, evaluation (3 products, 3 sums) below 2 U of the sum of the absolute terms
            err = 1.01 * (3 * U * (Math.Abs(ax) + Math.Abs(by) + Math.Abs(cz) + Math.Abs(D)) + (Math.Abs(A) + Math.Abs(B) + Math.Abs(C)) * e);
            return ax + by + cz - D;
        }

        /// <summary>
        /// Approximation of the point P + t (Q - P), t = |sp| / (|sp| + |sq|), where the segment P-Q crosses the zero
        /// set of a function (exactly of opposite signs at the ends) with approximate values sp, sq (errors esp, esq).
        /// The error of t is below (esp + esq) / (|sp| + |sq| - esp - esq), and the point stays on the segment.
        /// </summary>
        internal static Vector3d segment_point(Vector3d P, double ep, Vector3d Q, double eq, double sp, double esp, double sq, double esq, out double err)
        {
            double a = Math.Abs(sp), b = Math.Abs(sq), den = a + b, ee = 1.01 * (esp + esq);
            double t = a / den;
            if (!(t >= 0)) t = 0.5;     // NaN
            if (t > 1) t = 1;
            double dt = (den > 2 * ee) ? ee / (den - ee) + 2 * U : 1;
            if (!(dt < 1)) dt = 1;
            Vector3d d = Q - P;
            err = 1.01 * (Math.Max(ep, eq) + dt * max_abs(d) + 3 * U * (max_abs(P) + max_abs(d))) + Tiny;
            return P + t * d;
        }

        // orient2d of the approximations in coordinates (i, j), with coordinate errors at most e: the determinant and
        // a bound on its error. With errors eps = 2 e + U M of the differences (all at most M): products 2 eps (2 M + eps),
        // rounding 2 U M^2 (Shewchuk: 3 u + 16 u^2 of the terms)
        internal static double orient2d_approx(Vector3d a, Vector3d b, Vector3d c, int i, int j, double e, out double err)
        {
            double bx = b[i] - a[i], by = b[j] - a[j], cx = c[i] - a[i], cy = c[j] - a[j];
            double M = Math.Max(Math.Max(Math.Abs(bx), Math.Abs(by)), Math.Max(Math.Abs(cx), Math.Abs(cy)));
            double eps = 2 * e + U * M;
            err = 1.01 * (2 * eps * (2 * M + eps) + 3 * U * M * M) + 1e-300;
            return bx * cy - by * cx;
        }

        Vector3d planes_approx(int k1, int k2, int k3, out double err)
        {
            return planes_approx(Triangles[k1 >> 2].PlaneApprox, 4 * (k1 & 3), Triangles[k2 >> 2].PlaneApprox, 4 * (k2 & 3),
                Triangles[k3 >> 2].PlaneApprox, 4 * (k3 & 3), out err);
        }

        // the point common to the planes at q1[o1..o1+3], q2[o2..], q3[o3..] (coefficients with relative errors 2^-52)
        internal static Vector3d planes_approx(double[] q1, int o1, double[] q2, int o2, double[] q3, int o3, out double err)
        {
            double a1 = q1[o1], b1 = q1[o1 + 1], c1 = q1[o1 + 2], d1 = q1[o1 + 3];
            double a2 = q2[o2], b2 = q2[o2 + 1], c2 = q2[o2 + 2], d2 = q2[o2 + 3];
            double a3 = q3[o3], b3 = q3[o3 + 1], c3 = q3[o3 + 2], d3 = q3[o3 + 3];
            // cross products and their absolute versions (for the error bounds)
            double c23x = b2 * c3 - c2 * b3, c23y = c2 * a3 - a2 * c3, c23z = a2 * b3 - b2 * a3;
            double c31x = b3 * c1 - c3 * b1, c31y = c3 * a1 - a3 * c1, c31z = a3 * b1 - b3 * a1;
            double c12x = b1 * c2 - c1 * b2, c12y = c1 * a2 - a1 * c2, c12z = a1 * b2 - b1 * a2;
            double p23x = Math.Abs(b2 * c3) + Math.Abs(c2 * b3), p23y = Math.Abs(c2 * a3) + Math.Abs(a2 * c3), p23z = Math.Abs(a2 * b3) + Math.Abs(b2 * a3);
            double p31x = Math.Abs(b3 * c1) + Math.Abs(c3 * b1), p31y = Math.Abs(c3 * a1) + Math.Abs(a3 * c1), p31z = Math.Abs(a3 * b1) + Math.Abs(b3 * a1);
            double p12x = Math.Abs(b1 * c2) + Math.Abs(c1 * b2), p12y = Math.Abs(c1 * a2) + Math.Abs(a1 * c2), p12z = Math.Abs(a1 * b2) + Math.Abs(b1 * a2);
            double det = a1 * c23x + b1 * c23y + c1 * c23z;
            double nx = d1 * c23x + d2 * c31x + d3 * c12x;
            double ny = d1 * c23y + d2 * c31y + d3 * c12y;
            double nz = d1 * c23z + d2 * c31z + d3 * c12z;
            // degree 3 in the coefficients: their errors (U each) and the rounding of 5 operations, below 8 U of the
            // absolute terms
            double edet = 8 * U * (Math.Abs(a1) * p23x + Math.Abs(b1) * p23y + Math.Abs(c1) * p23z);
            double enx = 8 * U * (Math.Abs(d1) * p23x + Math.Abs(d2) * p31x + Math.Abs(d3) * p12x);
            double eny = 8 * U * (Math.Abs(d1) * p23y + Math.Abs(d2) * p31y + Math.Abs(d3) * p12y);
            double enz = 8 * U * (Math.Abs(d1) * p23z + Math.Abs(d2) * p31z + Math.Abs(d3) * p12z);
            Vector3d x = new Vector3d(nx / det, ny / det, nz / det);
            double ad = Math.Abs(det);
            // (nearly dependent planes: no bound, the point is computed exactly)
            err = double.PositiveInfinity;
            if (ad > 2.02 * edet) {
                double den = ad - 1.01 * edet;
                double ex = (enx + Math.Abs(x.x) * edet) / den, ey = (eny + Math.Abs(x.y) * edet) / den, ez = (enz + Math.Abs(x.z) * edet) / den;
                err = 1.01 * Math.Max(ex, Math.Max(ey, ez)) + 2 * U * max_abs(x) + Tiny;
            }
            return x;
        }



        /// <summary> input coordinates of a point, rounded to double (correctly: from the exact point) </summary>
        public Vector3d PointToVector(int id)
        {
            PointKey key = keys[id];
            if (key.Kind == PointKind.Input)
                return inputCoords[id];
            ExactBox b = exactPoints[id];
            return ToInputVector(b != null ? b.P : compute_exact(key, false));
        }

        /// <summary> PointToVector of many points, in parallel </summary>
        public Vector3d[] PointsToVectors(IList<int> ids)
        {
            var v = new Vector3d[ids.Count];
            gParallel.ForEach(Interval1i.Range(ids.Count), (k) => v[k] = PointToVector(ids[k]));
            return v;
        }

        /// <summary> the exact point p (grid coordinates) in input coordinates </summary>
        public ExactPoint3 ToInputExact(ExactPoint3 p)
        {
            BigInteger x = p.X + Center[0] * p.W, y = p.Y + Center[1] * p.W, z = p.Z + Center[2] * p.W, w = p.W;
            if (ScaleExp >= 0) {
                x <<= ScaleExp; y <<= ScaleExp; z <<= ScaleExp;
            } else {
                w <<= -ScaleExp;
            }
            return ExactPoint3.Create(x, y, z, w);
        }

        /// <summary> input coordinates of a grid point (rounded to double) </summary>
        public Vector3d ToInputVector(ExactPoint3 p)
        {
            return new Vector3d(ExactPoint3.ToDouble(p.X + Center[0] * p.W, p.W, ScaleExp),
                ExactPoint3.ToDouble(p.Y + Center[1] * p.W, p.W, ScaleExp), ExactPoint3.ToDouble(p.Z + Center[2] * p.W, p.W, ScaleExp));
        }



        // order of the points: by a generic linear function a . p (few points with different coordinates have equal
        // values, unlike for a single coordinate, so the exact points are rarely needed), then lexicographic
        static readonly double[] Alpha = { 1.0, 0.7548776662466927, 0.5698402909980532 };
        static readonly BigInteger[] AlphaInt = { alpha_int(Alpha[0]), alpha_int(Alpha[1]), alpha_int(Alpha[2]) };
        static BigInteger alpha_int(double a)
        {
            // a * 2^53, an integer for a in [0.5, 1]
            BigInteger m; int e;
            ExactPoint3.Decompose(a, out m, out e);
            return m << (e + 53);
        }

        // a . p of the approximation, with a bound on its error
        internal static double order_key(Vector3d a, double e, out double err)
        {
            double x = Alpha[0] * a.x, y = Alpha[1] * a.y, z = Alpha[2] * a.z;
            err = 1.01 * (2.33 * e + 2 * U * (Math.Abs(x) + Math.Abs(y) + Math.Abs(z))) + 1e-300;
            return x + y + z;
        }

        /// <summary> a total order of the exact points (deterministic, unlike point ids); 0 iff the points are equal </summary>
        public int ComparePoints(int a, int b)
        {
            if (a == b)
                return 0;
            if (DisableFilters == false) {
                double ea, eb;
                double ka = order_key(approx[a], approxErr[a], out ea), kb = order_key(approx[b], approxErr[b], out eb);
                double bound = 1.01 * (ea + eb);
                if (ka - kb > bound) return 1;
                if (kb - ka > bound) return -1;
            }
            ExactPoint3 pa = Exact(a), pb = Exact(b);
            BigInteger fa = (AlphaInt[0] * pa.X + AlphaInt[1] * pa.Y + AlphaInt[2] * pa.Z) * pb.W;
            BigInteger fb = (AlphaInt[0] * pb.X + AlphaInt[1] * pb.Y + AlphaInt[2] * pb.Z) * pa.W;
            int c = fa.CompareTo(fb);
            if (c != 0)
                return c;
            for (int k = 0; k < 3; ++k) {
                c = (pa[k] * pb.W).CompareTo(pb[k] * pa.W);
                if (c != 0) return c;
            }
            return 0;
        }

        /// <summary>
        /// Merge coincident points from different constructions: each gets the id with the smallest key (a
        /// rational point if there is one, so input vertices keep their coordinates). Candidates are points whose
        /// order keys (with error bounds) overlap; they are compared exactly.
        /// </summary>
        void merge_points()
        {
            int n = PointCount;
            int[] prev = canonical;
            var ids = new List<int>(n);
            for (int id = 0; id < n; ++id)
                if (prev == null || id >= prev.Length || prev[id] == id)
                    ids.Add(id);
            int m = ids.Count;
            double[] lo = new double[m], hi = new double[m];
            int[] order = new int[m];
            gParallel.ForEach(Interval1i.Range(m), (k) => {
                double e;
                double key = order_key(approx[ids[k]], approxErr[ids[k]], out e);
                e = 1.01 * e + U * Math.Abs(key);
                lo[k] = key - e;
                hi[k] = key + e;
                // an approximation beyond the double range (key or error infinite, interval NaN): the point
                // may equal any other
                if (!(lo[k] <= hi[k])) {
                    lo[k] = double.NegativeInfinity;
                    hi[k] = double.PositiveInfinity;
                }
                order[k] = k;
            });
            double[] sortLo = (double[])lo.Clone();
            Array.Sort(sortLo, order);
            int[] next = new int[n];
            for (int id = 0; id < n; ++id)
                next[id] = (prev != null && id < prev.Length) ? prev[id] : id;
            var clusters = new List<KeyValuePair<int, int>>();
            for (int s = 0; s < m; ) {
                double top = hi[order[s]];
                int e = s + 1;
                while (e < m && lo[order[e]] <= top) {
                    top = Math.Max(top, hi[order[e]]);
                    e++;
                }
                if (e - s > 1)
                    clusters.Add(new KeyValuePair<int, int>(s, e));
                s = e;
            }
            int merged = 0;
            gParallel.ForEach(Interval1i.Range(clusters.Count), (ci) => {
                var members = new List<int>();
                for (int k = clusters[ci].Key; k < clusters[ci].Value; ++k)
                    members.Add(ids[order[k]]);
                members.Sort(ComparePoints);
                for (int r0 = 0, r1; r0 < members.Count; r0 = r1) {
                    r1 = r0 + 1;
                    while (r1 < members.Count && ComparePoints(members[r0], members[r1]) == 0) r1++;
                    if (r1 - r0 == 1)
                        continue;
                    int best = members[r0];
                    for (int r = r0 + 1; r < r1; ++r)
                        if (keys[members[r]].CompareTo(keys[best]) < 0)
                            best = members[r];
                    for (int r = r0; r < r1; ++r)
                        next[members[r]] = best;
                    System.Threading.Interlocked.Add(ref merged, r1 - r0 - 1);
                }
            });
            // points merged before follow their (possibly merged) canonical point
            for (int id = 0; id < n; ++id)
                next[id] = next[next[id]];
            MergedPoints += merged;
            canonical = next;
        }



        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        public void Compute()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            build_points();
            build_triangles();
            PhaseMilliseconds["setup"] = sw.ElapsedMilliseconds; sw.Restart();
            find_intersections();
            PhaseMilliseconds["intersections"] = sw.ElapsedMilliseconds; sw.Restart();
            merge_points();
            int before = PointCount;
            gParallel.ForEach(Interval1i.Range(Triangles.Count), (ti) => prepare_triangle(ti));
            if (PointCount > before)
                merge_points();
            PhaseMilliseconds["merge"] = sw.ElapsedMilliseconds; sw.Restart();
            var pieces = new List<OutputTriangle>[Triangles.Count];
            var constraints = new List<long>[Triangles.Count];
            gParallel.ForEach(Interval1i.Range(Triangles.Count), (ti) => triangulate(ti, out pieces[ti], out constraints[ti]));
            for (int ti = 0; ti < Triangles.Count; ++ti) {
                InputTriangle t = Triangles[ti];
                if (pieces[ti] != null)
                    Output.AddRange(pieces[ti]);
                else if (t.Degenerate == false)
                    Output.Add(new OutputTriangle() { Mesh = t.Mesh, Source = ti, A = t.Corners[0], B = t.Corners[1], C = t.Corners[2] });
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
            Vector3d min = Vector3d.Zero, max = Vector3d.Zero;
            bool first = true;
            foreach (DMesh3 mesh in Meshes) {
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    for (int k = 0; k < 3; ++k) {
                        if (v[k] != 0)
                            eMin = Math.Min(eMin, lowest_exponent(v[k]));
                        if (first || v[k] < min[k]) min[k] = v[k];
                        if (first || v[k] > max[k]) max[k] = v[k];
                    }
                    first = false;
                }
            }
            if (eMin == int.MaxValue)
                eMin = 0;
            ScaleExp = eMin;
            // center of the box, a grid point that is a double (so that the approximations below are exact sums)
            for (int k = 0; k < 3; ++k) {
                double c = Math.Round(ExactPoint3.ScaleB(0.5 * min[k] + 0.5 * max[k], -ScaleExp));
                if (double.IsInfinity(c) || double.IsNaN(c))
                    c = 0;
                centerD[k] = c;
                Center[k] = new BigInteger(c);
            }

            // input points (welded by position: equal doubles are equal points)
            var index = new Dictionary<Vector3d, int>();
            VertexPoints = new int[Meshes.Count][];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                DMesh3 mesh = Meshes[mi];
                VertexPoints[mi] = new int[mesh.MaxVertexID];
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    for (int k = 0; k < 3; ++k)
                        if (v[k] == 0) v[k] = 0;    // -0.0
                    int id;
                    if (index.TryGetValue(v, out id) == false) {
                        id = new_id();
                        index[v] = id;
                        inputCoords.Set(id, v);
                        Vector3d a = Vector3d.Zero;
                        double e = 0;
                        ExactBox exact = null;
                        for (int k = 0; k < 3; ++k) {
                            // grid value v 2^-ScaleExp (exact) minus the center: rounded once, error below u |a|
                            a[k] = ExactPoint3.ScaleB(v[k], -ScaleExp) - centerD[k];
                            e = Math.Max(e, U * Math.Abs(a[k]));
                        }
                        if (finite(a) == false) {
                            // beyond the double range in grid units
                            ExactPoint3 x = input_exact(v);
                            exact = new ExactBox(x);
                            a = x.ApproxVector3d();
                            e = 4 * U * max_abs(a);
                        }
                        store(id, a, e, new PointKey(PointKind.Input, id, 0, 0), exact);
                    }
                    VertexPoints[mi][vid] = id;
                }
            }
        }

        // the exponent of the lowest set bit of v != 0 (v = m * 2^e with m odd)
        static int lowest_exponent(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0)
                exp = 1;
            else
                man |= 1L << 52;
            int e = exp - 1075;
            while ((man & 1) == 0) {
                man >>= 1; e++;
            }
            return e;
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
                    it.arr = this;
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


        /// <summary> orientation of three points in the projection of triangle t, +1 if counter-clockwise like t </summary>
        public int Orient(InputTriangle t, int p, int q, int r)
        {
            if (p == q || q == r || r == p)
                return 0;
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            if (DisableFilters == false) {
                double e = Math.Max(approxErr[p], Math.Max(approxErr[q], approxErr[r]));
                double err;
                double fdet = orient2d_approx(approx[p], approx[q], approx[r], i, j, e, out err);
                if (fdet > err) return t.Sign;
                if (-fdet > err) return -t.Sign;
            }
            ExactPoint3 a = Exact(p), b = Exact(q), c = Exact(r);
            BigInteger det = ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W);
            return det.Sign * t.Sign;
        }

        /// <summary> InCircle decided from the approximations only (also with DisableFilters): 0 if they do not decide it </summary>
        internal int InCircleFiltered(InputTriangle t, int p, int q, int r, int s)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            // differences at most M with errors eps = 2 e + U M; lifts and 2x2 minors at most 2 M^2 with errors
            // eta = 2 eps (2 M + eps); the three terms: 3 eta (4 M^2 + eta); rounding (Shewchuk) 10 u of the
            // absolute terms, at most 12 M^4
            double e = Math.Max(Math.Max(approxErr[p], approxErr[q]), Math.Max(approxErr[r], approxErr[s]));
            double bound;
            double fdet = incircle_approx(approx[p], approx[q], approx[r], approx[s], i, j, e, out bound);
            if (fdet > bound) return t.Sign;
            if (-fdet > bound) return -t.Sign;
            return 0;
        }

        // incircle determinant of the approximations in coordinates (i, j), with coordinate errors at most e, and a
        // bound on its error
        internal static double incircle_approx(Vector3d fa, Vector3d fb, Vector3d fc, Vector3d fd, int i, int j, double e, out double bound)
        {
            double adx = fa[i] - fd[i], ady = fa[j] - fd[j], bdx = fb[i] - fd[i], bdy = fb[j] - fd[j], cdx = fc[i] - fd[i], cdy = fc[j] - fd[j];
            double fdet = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
            double M = Math.Max(Math.Max(Math.Max(Math.Abs(adx), Math.Abs(ady)), Math.Max(Math.Abs(bdx), Math.Abs(bdy))), Math.Max(Math.Abs(cdx), Math.Abs(cdy)));
            double eps = 2 * e + U * M, eta = 2 * eps * (2 * M + eps), M2 = M * M;
            bound = 1.01 * (3 * eta * (4 * M2 + eta) + 64 * U * M2 * M2) + 1e-300;
            return fdet;
        }

        /// <summary> +1 if s is inside the circle through p, q, r (counter-clockwise in the projection of t) </summary>
        public int InCircle(InputTriangle t, int p, int q, int r, int s)
        {
            if (DisableFilters == false) {
                int f = InCircleFiltered(t, p, q, r, s);
                if (f != 0)
                    return f;
            }
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            ExactPoint3 a = Exact(p), b = Exact(q), c = Exact(r), d = Exact(s);
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
            if (DisableFilters == false) {
                double bound;
                double diff = along_approx((double)dx, (double)dy, (double)dz, approx[p1], approx[p2], Math.Max(approxErr[p1], approxErr[p2]), out bound);
                if (diff > bound) return 1;
                if (-diff > bound) return -1;
            }
            ExactPoint3 a = Exact(p1), b = Exact(p2);
            BigInteger ka = (dx * a.X + dy * a.Y + dz * a.Z) * b.W;
            BigInteger kb = (dx * b.X + dy * b.Y + dz * b.Z) * a.W;
            return ka.CompareTo(kb);
        }

        // d . (a - b) for the direction d rounded to double (fdx, fdy, fdz: relative errors U) and approximations a, b
        // with coordinate errors at most e, and a bound on its error: differences with errors 2 e + U |.|, rounding
        // of the dot product
        internal static double along_approx(double fdx, double fdy, double fdz, Vector3d fa, Vector3d fb, double e, out double bound)
        {
            double ux = fa.x - fb.x, uy = fa.y - fb.y, uz = fa.z - fb.z;
            bound = 1.01 * (2 * e * (Math.Abs(fdx) + Math.Abs(fdy) + Math.Abs(fdz))
                + 5 * U * (Math.Abs(fdx * ux) + Math.Abs(fdy * uy) + Math.Abs(fdz * uz))) + 1e-300;
            return fdx * ux + fdy * uy + fdz * uz;
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
        /// <summary> triangles per batch of the candidate pair search (bounds its memory; smaller for tests) </summary>
        internal long CandidateBatchSize = 1 << 24;
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
            // the pairs of meshes to intersect (their boxes overlap; with many meshes most pairs are far apart),
            // in the order (ma, mb), mb >= ma (> ma without self-intersections)
            var meshBoxes = new List<AxisAlignedBox3d>();
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                AxisAlignedBox3d mb = AxisAlignedBox3d.Empty;
                foreach (AxisAlignedBox3d b in boxes[mi])
                    mb.Contain(b);
                meshBoxes.Add(mb);
            }
            var meshTree = new TriangleBoxTree(meshBoxes);
            var meshPairs = new List<Index2i>();
            var near = new List<int>();
            for (int ma = 0; ma < Meshes.Count; ++ma) {
                if (boxes[ma].Count == 0)
                    continue;
                near.Clear();
                meshTree.Query(meshBoxes[ma], (mb) => {
                    if (mb > ma || (mb == ma && SelfIntersections))
                        near.Add(mb);
                });
                near.Sort();
                foreach (int mb in near)
                    meshPairs.Add(new Index2i(ma, mb));
            }
            // candidate pairs (boxes overlap, not separated by a plane, not only touching), in parallel per
            // triangle of the first mesh of each pair of meshes; in batches of consecutive pairs, so that the
            // per-triangle lists stay bounded with many overlapping meshes (the order is that of the pairs)
            var pairs = new List<long>();
            int self = 0;
            for (int p0 = 0, p1 = 0; p0 < meshPairs.Count; p0 = p1) {
                long size = 0;
                p1 = p0;
                while (p1 < meshPairs.Count && (p1 == p0 || size + MeshTriangles[meshPairs[p1].a].Count <= CandidateBatchSize))
                    size += MeshTriangles[meshPairs[p1++].a].Count;
                var offsets = new int[p1 - p0 + 1];
                for (int p = p0; p < p1; ++p)
                    offsets[p - p0 + 1] = offsets[p - p0] + MeshTriangles[meshPairs[p].a].Count;
                var found = new List<long>[offsets[p1 - p0]];
                gParallel.ForEach(Interval1i.Range(found.Length), (j) => {
                    int p = Array.BinarySearch(offsets, j);
                    if (p < 0)      // (the offsets increase strictly: every pair has triangles of its first mesh)
                        p = ~p - 1;
                    int ma = meshPairs[p0 + p].a, mb = meshPairs[p0 + p].b, ka = j - offsets[p];
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
                        if (found[j] == null)
                            found[j] = new List<long>();
                        found[j].Add(((long)MeshTriangles[ma][ka] << 32) | (uint)MeshTriangles[mb][kb]);
                    });
                });
                foreach (var list in found)
                    if (list != null)
                        pairs.AddRange(list);
            }
            SelfIntersectingPairs += self;
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
            // quick test: the corners of one triangle that are not shared all strictly on one side of the other's
            // plane: then they meet at most in the shared vertices (for a shared edge: the triangles are not
            // coplanar, so the plane of one meets the other only in the edge)
            if (shared < 3 && (one_side_except_shared(T, U) || (shared < 2 && one_side_except_shared(U, T))))
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

            List<int> pT = plane_points(T, sT, 4 * ui), pU = plane_points(U, sU, 4 * ti);
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
                lock (T) T.AddPoint(lo);
                lock (U) U.AddPoint(lo);
            } else {
                lock (T) T.AddSegment(new Segment(lo, hi, 4 * ui));
                lock (U) U.AddSegment(new Segment(lo, hi, 4 * ti));
            }
        }

        // the points where the boundary of t meets the plane (key; t is not in the plane)
        List<int> plane_points(InputTriangle t, int[] side, int plane)
        {
            var pts = new List<int>();
            for (int k = 0; k < 3; ++k) {
                if (side[k] == 0)
                    pts.Add(t.Corners[k]);
                int k1 = (k + 1) % 3;
                if (side[k] * side[k1] < 0)
                    pts.Add(EdgePlanePointImplicit(t.Corners[k], t.Corners[k1], plane));
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
                any |= clip_into(T, U.Corners[k], U.Corners[(k + 1) % 3], 4 * ui + 1 + k);
                any |= clip_into(U, T.Corners[k], T.Corners[(k + 1) % 3], 4 * ti + 1 + k);
            }
            if (any) {
                System.Threading.Interlocked.Increment(ref IntersectingPairs);
                lock (T) T.AddCoplanar(ui);
                lock (U) U.AddCoplanar(ti);
            }
        }

        // clip segment p-q (an edge of a triangle in the plane of t, on the line t.P ∩ line) to the closed triangle t,
        // add the result to t
        bool clip_into(InputTriangle t, int p, int q, int line)
        {
            var cands = new List<int>();
            if (inside(t, p)) cands.Add(p);
            if (inside(t, q)) cands.Add(q);
            BigInteger dx, dy, dz;
            line_direction(t, PlaneOf(line), out dx, out dy, out dz);
            for (int k = 0; k < 3; ++k) {
                int ci = t.Corners[k], cj = t.Corners[(k + 1) % 3];
                int o1 = Orient(t, p, q, ci), o2 = Orient(t, p, q, cj);
                if (o1 == 0 && between(dx, dy, dz, p, q, ci))
                    cands.Add(ci);
                if (o1 * o2 < 0) {
                    int o3 = Orient(t, ci, cj, p), o4 = Orient(t, ci, cj, q);
                    if (o3 * o4 < 0)
                        cands.Add(SegmentsPointImplicit(p, q, ci, cj, t.Axis));
                }
            }
            if (cands.Count == 0)
                return false;
            int lo, hi;
            min_max(cands, dx, dy, dz, out lo, out hi);
            lock (t) {
                if (compare_along(dx, dy, dz, lo, hi) == 0)
                    t.AddPoint(lo);
                else
                    t.AddSegment(new Segment(lo, hi, line));
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
        void prepare_triangle(int ti)
        {
            InputTriangle t = Triangles[ti];
            if (t.Degenerate || (t.Segments.Count == 0 && t.Points.Count == 0))
                return;
            // coincident points of different constructions are merged
            var points = new HashSet<int>();
            foreach (int p in t.Points)
                points.Add(Canonical(p));
            t.Points = points;
            for (int k = 0; k < t.Segments.Count; ++k) {
                Segment s = t.Segments[k];
                t.Segments[k] = new Segment(Canonical(s.P), Canonical(s.Q), s.Line);
            }
            // unique segments
            // deterministic order (the pairs were intersected in parallel)
            // (only lists with elements: Sort also changes the version of an empty list, and the empty ones are
            // shared by all untouched triangles, which other threads enumerate)
            if (t.Segments.Count > 1) t.Segments.Sort((x, y) => {
                int xa = (ComparePoints(x.P, x.Q) <= 0) ? x.P : x.Q, xb = (xa == x.P) ? x.Q : x.P;
                int ya = (ComparePoints(y.P, y.Q) <= 0) ? y.P : y.Q, yb = (ya == y.P) ? y.Q : y.P;
                int c = ComparePoints(xa, ya);
                if (c == 0) c = ComparePoints(xb, yb);
                return (c != 0) ? c : x.Line.CompareTo(y.Line);
            });
            if (t.Coplanar.Count > 1) t.Coplanar.Sort();
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
            // pairs of segments whose boxes overlap (a grid of the boxes: a triangle can have thousands of segments)
            var grid = new BoxGrid2(this, t, unique.Count);
            foreach (Segment s in unique)
                grid.Add(s.P, s.Q);
            grid.ForEachPair((i, j) => {
                Segment a = unique[i], b = unique[j];
                if (a.P == b.P || a.P == b.Q || a.Q == b.P || a.Q == b.Q)
                    return;
                int o1 = Orient(t, a.P, a.Q, b.P), o2 = Orient(t, a.P, a.Q, b.Q);
                if (o1 * o2 >= 0) return;
                int o3 = Orient(t, b.P, b.Q, a.P), o4 = Orient(t, b.P, b.Q, a.Q);
                if (o3 * o4 >= 0) return;
                t.Points.Add(PlanesPointImplicit(4 * ti, a.Line, b.Line));
                System.Threading.Interlocked.Increment(ref SegmentCrossings);
            });
            System.Threading.Interlocked.Add(ref CandidateTests, grid.Examined);
            foreach (int c in t.Corners)
                t.Points.Remove(c);
        }


        void triangulate(int ti, out List<OutputTriangle> output, out List<long> constraints)
        {
            output = null;
            constraints = null;
            InputTriangle t = Triangles[ti];
            // (untouched triangles are output as they are, by the caller)
            if (t.Degenerate || (t.Points.Count == 0 && t.Segments.Count == 0))
                return;
            var pts = new HashSet<int>();
            foreach (int p in t.Points)
                pts.Add(Canonical(p));
            foreach (int c in t.Corners)
                pts.Remove(c);
            output = new List<OutputTriangle>();
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

            // segments split at the points on them (the points whose boxes overlap the segment's, from a grid)
            var grid = new BoxGrid2(this, t, ordered.Count);
            foreach (int p in ordered)
                grid.Add(p, p);
            var candidates = new List<int>();
            foreach (Segment s in t.Segments) {
                BigInteger dx, dy, dz;
                line_direction(t, PlaneOf(s.Line), out dx, out dy, out dz);
                int sp = Canonical(s.P), sq = Canonical(s.Q);
                var on = new List<int>() { sp, sq };
                candidates.Clear();
                grid.Query(sp, sq, (k) => candidates.Add(ordered[k]));
                candidates.AddRange(t.Corners);
                foreach (int r in candidates) {
                    if (r == sp || r == sq) continue;
                    if (Orient(t, sp, sq, r) == 0 && between(dx, dy, dz, sp, sq, r))
                        on.Add(r);
                }
                on.Sort((a, b) => compare_along(dx, dy, dz, a, b));
                for (int k = 0; k + 1 < on.Count; ++k) {
                    tri.InsertConstraint(on[k], on[k + 1]);
                    constraints.Add(EdgeKey(on[k], on[k + 1]));
                }
            }
            System.Threading.Interlocked.Add(ref CandidateTests, grid.Examined);
            tri.MakeDelaunay();
            foreach (Index3i f in tri.Triangles())
                output.Add(new OutputTriangle() { Mesh = t.Mesh, Source = ti, A = f.a, B = f.b, C = f.c });
        }


        /// <summary>
        /// Uniform grid over the projection of a triangle, of the boxes of items (segments p-q or points p = q):
        /// each box contains the exact item (approximations widened by their error bounds), so items whose exact
        /// points meet have overlapping boxes. About one cell per item; fewer cells where the boxes are large (long
        /// oblique segments cover many cells), so that the cells hold at most a few times as many entries as there
        /// are items; a single cell for few items.
        /// </summary>
        sealed class BoxGrid2
        {
            readonly ExactMeshArrangement arr;
            readonly int i, j, n0;
            readonly double[] bounds;
            int n;
            double cx, cy;
            readonly List<double[]> boxes = new List<double[]>();
            List<int>[] cells;
            /// <summary> pairs of items (or of the query and an item) whose boxes were compared </summary>
            public long Examined;

            public BoxGrid2(ExactMeshArrangement arr, InputTriangle t, int count)
            {
                this.arr = arr;
                i = (t.Axis + 1) % 3; j = (t.Axis + 2) % 3;
                double[] b = box(t.Corners[0], t.Corners[0]);
                foreach (int c in t.Corners) {
                    double[] cb = box(c, c);
                    b[0] = Math.Min(b[0], cb[0]); b[1] = Math.Min(b[1], cb[1]); b[2] = Math.Max(b[2], cb[2]); b[3] = Math.Max(b[3], cb[3]);
                }
                bounds = b;
                n0 = (count < 16) ? 1 : Math.Min(1024, (int)Math.Ceiling(Math.Sqrt(count)));
            }

            void set_size(int size)
            {
                n = size;
                cx = (bounds[2] - bounds[0]) / n; cy = (bounds[3] - bounds[1]) / n;
                if (!(cx > 0 && cy > 0 && cx < double.MaxValue && cy < double.MaxValue))
                    n = 1;
            }

            // the cells, after all items are added: the grid is made coarser while the boxes would have more than
            // 8 entries per item (64 at least)
            void build()
            {
                if (cells != null)
                    return;
                long limit = 8L * boxes.Count + 64;
                for (set_size(n0); n > 1; set_size(n / 2)) {
                    long entries = 0;
                    foreach (double[] b in boxes) {
                        int ix0, iy0, ix1, iy1;
                        range(b, out ix0, out iy0, out ix1, out iy1);
                        entries += (long)(ix1 - ix0 + 1) * (iy1 - iy0 + 1);
                    }
                    if (entries <= limit)
                        break;
                }
                cells = new List<int>[n * n];
                for (int k = 0; k < boxes.Count; ++k) {
                    int ix0, iy0, ix1, iy1;
                    range(boxes[k], out ix0, out iy0, out ix1, out iy1);
                    for (int x = ix0; x <= ix1; ++x)
                        for (int y = iy0; y <= iy1; ++y)
                            (cells[x * n + y] ?? (cells[x * n + y] = new List<int>())).Add(k);
                }
            }

            // the box of the segment p-q: min x, min y, max x, max y
            double[] box(int p, int q)
            {
                Vector3d a = arr.approx[p], c = arr.approx[q];
                double e = Math.Max(arr.approxErr[p], arr.approxErr[q]);
                double[] r = { Math.Min(a[i], c[i]), Math.Min(a[j], c[j]), Math.Max(a[i], c[i]), Math.Max(a[j], c[j]) };
                for (int k = 0; k < 4; ++k) {
                    // widened by the error bound and the rounding of this sum
                    double w = e + 4 * U * (Math.Abs(r[k]) + e);
                    r[k] += (k < 2) ? -w : w;
                }
                return r;
            }

            int cell(double v, double v0, double c)
            {
                double f = Math.Floor((v - v0) / c);
                return !(f > 0) ? 0 : (f >= n - 1) ? n - 1 : (int)f;
            }

            void range(double[] b, out int ix0, out int iy0, out int ix1, out int iy1)
            {
                if (n == 1) { ix0 = iy0 = ix1 = iy1 = 0; return; }
                ix0 = cell(b[0], bounds[0], cx); iy0 = cell(b[1], bounds[1], cy); ix1 = cell(b[2], bounds[0], cx); iy1 = cell(b[3], bounds[1], cy);
            }

            static bool overlap(double[] a, double[] b)
            {
                return !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
            }

            /// <summary> add an item (index: the number of items added before), before the first query </summary>
            public void Add(int p, int q)
            {
                if (cells != null)
                    throw new InvalidOperationException("BoxGrid2: items added after a query");
                boxes.Add(box(p, q));
            }

            /// <summary> f(k) once for every item k whose box overlaps the box of the segment p-q </summary>
            public void Query(int p, int q, Action<int> f)
            {
                build();
                double[] b = box(p, q);
                int ix0, iy0, ix1, iy1;
                range(b, out ix0, out iy0, out ix1, out iy1);
                for (int x = ix0; x <= ix1; ++x)
                    for (int y = iy0; y <= iy1; ++y) {
                        List<int> list = cells[x * n + y];
                        if (list == null) continue;
                        foreach (int k in list) {
                            Examined++;
                            // (reported in the first cell of the query that has the item)
                            int kx0, ky0, kx1, ky1;
                            range(boxes[k], out kx0, out ky0, out kx1, out ky1);
                            if (x == Math.Max(ix0, kx0) && y == Math.Max(iy0, ky0) && overlap(b, boxes[k]))
                                f(k);
                        }
                    }
            }

            /// <summary> f(a, b), a &lt; b, once for every pair of items whose boxes overlap </summary>
            public void ForEachPair(Action<int, int> f)
            {
                build();
                var r = new int[boxes.Count * 4];
                for (int k = 0; k < boxes.Count; ++k)
                    range(boxes[k], out r[4 * k], out r[4 * k + 1], out r[4 * k + 2], out r[4 * k + 3]);
                for (int x = 0; x < n; ++x)
                    for (int y = 0; y < n; ++y) {
                        List<int> list = cells[x * n + y];
                        if (list == null) continue;
                        for (int s = 0; s < list.Count; ++s)
                            for (int u = s + 1; u < list.Count; ++u) {
                                int a = list[s], b = list[u];
                                Examined++;
                                // (reported in the first cell that has both)
                                if (x == Math.Max(r[4 * a], r[4 * b]) && y == Math.Max(r[4 * a + 1], r[4 * b + 1]) && overlap(boxes[a], boxes[b]))
                                    f(a, b);
                            }
                    }
            }
        }
    }





    /// <summary>
    /// Append-only list in fixed-size chunks that are never moved: elements can be read while another thread
    /// appends (appends must be serialized by the caller).
    /// </summary>
    public class ChunkedList<T>
    {
        // 2048 elements: chunks of structs up to 40 bytes stay below the large object heap threshold (85000 bytes)
        const int Shift = 11, Size = 1 << Shift, Mask = Size - 1;
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
    /// Hash for edge keys (a &lt;&lt; 32 | b): the default long hash is a ^ b, which collides for most edges of a mesh
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
