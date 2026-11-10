// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

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
            public Index3i Corners;                 // point ids
            // (a field, not an array of its own, and the input coordinates in an array of the arrangement: the
            // triangles live through the whole computation, every object and byte of them is copied by the GC)
            internal int Index;                     // in Triangles
            /// <summary> input coordinates of the corners </summary>
            public Vector3d V0 { get { return arr.triangleCoords[3 * Index]; } }
            public Vector3d V1 { get { return arr.triangleCoords[3 * Index + 1]; } }
            public Vector3d V2 { get { return arr.triangleCoords[3 * Index + 2]; } }
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

            // planes are computed when first needed (most triangles are far from all intersections), in an object of
            // their own (published at once: other threads may compute and read them at the same time)
            internal ExactMeshArrangement arr;
            internal sealed class PlaneData
            {
                public Plane P;
                // (the exact edge planes only when they are used, in the exact stages of some coincident constructions:
                // they live as long as the triangle and are larger than the plane, so the GC would copy them for every
                // triangle near an intersection; their approximations are kept)
                public Plane[] EdgePlanes;
                public double[] PlaneApprox;
                public int Axis, Sign;
            }
            PlaneData planes;

            public Plane P { get { return ensure().P; } }
            /// <summary> edge k: Corners[k] -> Corners[k+1], plane through the edge perpendicular to the triangle </summary>
            public Plane[] EdgePlanes
            {
                get {
                    PlaneData d = ensure();
                    return System.Threading.Volatile.Read(ref d.EdgePlanes) ?? compute_edge_planes(d);
                }
            }
            /// <summary> the planes P, EdgePlanes[0..2] as doubles (A, B, C, D each, relative error at most 2^-52) </summary>
            internal double[] PlaneApprox { get { return ensure().PlaneApprox; } }
            /// <summary> unnormalized normal (b-a)x(c-a) </summary>
            public BigInteger NX { get { return ensure().P.A; } }
            public BigInteger NY { get { return ensure().P.B; } }
            public BigInteger NZ { get { return ensure().P.C; } }
            /// <summary> projection: drop Axis; Sign = sign of the normal component on that axis </summary>
            public int Axis { get { return ensure().Axis; } }
            public int Sign { get { return ensure().Sign; } }

            PlaneData ensure()
            {
                return System.Threading.Volatile.Read(ref planes) ?? compute_planes();
            }

            [MethodImpl(JitHints.Optimize)]
            PlaneData compute_planes()
            {
                if (Degenerate)
                    throw new InvalidOperationException("ExactMeshArrangement: degenerate triangle has no plane");
                PlaneData fd = compute_planes_fixed();
                if (fd != null) {
                    // (another thread may store the same values)
                    System.Threading.Volatile.Write(ref planes, fd);
                    return fd;
                }
                ExactPoint3 a = arr.Exact(Corners[0]), b = arr.Exact(Corners[1]), c = arr.Exact(Corners[2]);
                BigInteger ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                BigInteger vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                BigInteger nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                Plane tp = new Plane(nx, ny, nz, nx * a.X + ny * a.Y + nz * a.Z);
                BigInteger ax = BigInteger.Abs(nx), ay = BigInteger.Abs(ny), az = BigInteger.Abs(nz);
                int ta = (ax >= ay && ax >= az) ? 0 : (ay >= az) ? 1 : 2;
                int ts = ((ta == 0) ? nx : (ta == 1) ? ny : nz).Sign;
                Plane[] ep = edge_planes(nx, ny, nz);
                var pa = new double[16];
                for (int k = 0; k < 4; ++k) {
                    Plane q = (k == 0) ? tp : ep[k - 1];
                    pa[4 * k] = (double)q.A; pa[4 * k + 1] = (double)q.B; pa[4 * k + 2] = (double)q.C; pa[4 * k + 3] = (double)q.D;
                }
                // (another thread may store the same values)
                var d = new PlaneData() { P = tp, Axis = ta, Sign = ts, PlaneApprox = pa };
                System.Threading.Volatile.Write(ref planes, d);
                return d;
            }

            // compute_planes on the fixed-width corners (integer points, so W = 1), the same values (FixedArith.
            // TrianglePlanes); null if a corner is a fallback point
            // (internal: the tests check that this path is taken)
            internal PlaneData compute_planes_fixed()
            {
                int ra = arr.exact_ref(Corners[0]), rb = arr.exact_ref(Corners[1]), rc = arr.exact_ref(Corners[2]);
                if (ra <= 0 || rb <= 0 || rc <= 0)
                    return null;
                ref FixedPoint3 fa = ref arr.fixedPoints[ra - 1], fb = ref arr.fixedPoints[rb - 1], fc = ref arr.fixedPoints[rc - 1];
                if (fa.WIsOne == false || fb.WIsOne == false || fc.WIsOne == false)
                    return null;
                var pa = new double[16];
                BigInteger nx, ny, nz, d;
                int ta, ts;
                FixedArith.TrianglePlanes(ref fa, ref fb, ref fc, pa, out nx, out ny, out nz, out d, out ta, out ts);
                return new PlaneData() { P = new Plane(nx, ny, nz, d), Axis = ta, Sign = ts, PlaneApprox = pa };
            }

            Plane[] edge_planes(BigInteger nx, BigInteger ny, BigInteger nz)
            {
                var ep = new Plane[3];
                for (int k = 0; k < 3; ++k)
                    ep[k] = edge_plane(nx, ny, nz, arr.Exact(Corners[k]), arr.Exact(Corners[(k + 1) % 3]));
                return ep;
            }

            Plane[] compute_edge_planes(PlaneData d)
            {
                // (another thread may store the same values)
                Plane[] ep = edge_planes(d.P.A, d.P.B, d.P.C);
                System.Threading.Volatile.Write(ref d.EdgePlanes, ep);
                return ep;
            }

            // plane through the integer points p, q, perpendicular to the triangle plane
            [MethodImpl(JitHints.Optimize)]
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
        Vector3d[] triangleCoords;          // per input triangle: the input coordinates of its corners
        public List<OutputTriangle> Output = new List<OutputTriangle>();
        public HashSet<long> ConstraintEdges = new HashSet<long>(EdgeKeyComparer.Instance);     // undirected point id pairs on intersection curves
        public int[][] VertexPoints;        // per mesh: vertex id -> point id
        public List<int>[] MeshTriangles;   // per mesh: indices into Triangles
        public int IntersectingPairs;
        // tests (counted in check mode only): candidate pairs the Devillers-Guigue test rejected, and proper pairs whose
        // decision was compared with the construction's
        internal int PrefilterRejected, PrefilterChecked;
        // tests: every proper pair is decided both ways, the prefilter's decision compared with the construction's
        // (InvalidOperationException if they differ); read when the intersections are searched, on the calling thread
        [ThreadStatic] internal static bool CheckPrefilterOnThisThread;
        bool checkPrefilter;
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
        [MethodImpl(JitHints.Optimize)]
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
            [MethodImpl(JitHints.Optimize)]
            public override int GetHashCode()
            {
                ulong h = (ulong)Kind;
                h = mix(h ^ (ulong)A);
                h = mix(h ^ (ulong)B);
                h = mix(h ^ (ulong)C);
                return (int)h ^ (int)(h >> 32);
            }
            [MethodImpl(JitHints.Optimize)]
            static ulong mix(ulong z)
            {
                // SplitMix64 finalizer
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
            [MethodImpl(JitHints.Optimize)]
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
            /// <summary> the point is an integer point whose coordinates are doubles (D, exactly) </summary>
            public readonly bool IsDouble;
            public readonly Vector3d D;
            [MethodImpl(JitHints.Optimize)]
            public ExactBox(ExactPoint3 p)
            {
                P = p;
                IsDouble = FixedPoint3.DoubleOf(p, out D);
            }
        }

        /// <summary> integer grid offset: input coordinate = (point + Center) * 2^ScaleExp (keeps the filters' error bounds small) </summary>
        public BigInteger[] Center = { BigInteger.Zero, BigInteger.Zero, BigInteger.Zero };
        double[] centerD = new double[3];   // Center, exactly (a double)
        // per point: double approximation (grid units), a bound on the error of each coordinate, the construction,
        // the exact point (when computed); input points also have their input coordinates
        readonly ChunkedList<Vector3d> approx = new ChunkedList<Vector3d>();
        readonly ChunkedList<double> approxErr = new ChunkedList<double>();
        readonly ChunkedList<PointKey> keys = new ChunkedList<PointKey>();
        // The exact point of each id, once computed: in fixed width if it fits (docs/design/FIXED_WIDTH_EXACT_POINTS.md;
        // no objects for the GC to trace or copy), else as an ExactBox. exactRef[id]: 0 = not computed yet, r > 0 = slot
        // r - 1 of fixedPoints, -1 = exactPoints[id]. It is published with Volatile.Write after its data is written.
        readonly ChunkedList<int> exactRef = new ChunkedList<int>();
        readonly FixedPointStore fixedPoints = new FixedPointStore();
        readonly ChunkedList<ExactBox> exactPoints = new ChunkedList<ExactBox>();
        /// <summary>
        /// for tests: store all exact points as BigInteger (ExactBox), the code before the fixed-width points. Only
        /// before the first point: equality of exact points relies on the representation being a function of the value.
        /// </summary>
        internal bool FixedWidthPoints
        {
            get { return fixedWidthPoints; }
            set {
                if (PointCount > 0 && value != fixedWidthPoints)
                    throw new InvalidOperationException("ExactMeshArrangement: FixedWidthPoints changed after points were added");
                fixedWidthPoints = value;
            }
        }
        bool fixedWidthPoints = true;
        readonly ChunkedList<Vector3d> inputCoords = new ChunkedList<Vector3d>();
        int nextPoint;
        // implicit point ids by construction and rational point ids by value, in shards (by hash) so that threads adding
        // points rarely wait for each other (the classification adds the pieces' centroids in parallel)
        const int Shards = 64;
        readonly Dictionary<PointKey, int>[] keyIndex = make_shards<PointKey>();
        readonly Dictionary<HashedPoint, int>[] rationalIndex = make_shards<HashedPoint>();
        readonly Dictionary<FixedKey, int>[] fixedIndex = make_shards<FixedKey>();
        static Dictionary<T, int>[] make_shards<T>()
        {
            var d = new Dictionary<T, int>[Shards];
            for (int i = 0; i < Shards; ++i)
                d[i] = new Dictionary<T, int>();
            return d;
        }

        // an exact point with its hash computed once, outside the lock (the hash of four BigIntegers is not cheap)
        readonly struct HashedPoint : IEquatable<HashedPoint>
        {
            public readonly ExactPoint3 P;
            public readonly int Hash;
            public HashedPoint(ExactPoint3 p) { P = p; Hash = p.GetHashCode(); }
            public bool Equals(HashedPoint o) { return Hash == o.Hash && P.Equals(o.P); }
            public override bool Equals(object o) { return o is HashedPoint h && Equals(h); }
            public override int GetHashCode() { return Hash; }
        }
        // a point in fixed width with its hash (computed once, outside the lock)
        // (the limbs only: IsDouble and D are not set in the key)
        struct FixedKey : IEquatable<FixedKey>
        {
            FixedPoint3 P;
            public readonly int Hash;
            public FixedKey(ref FixedPoint3 p) { P = p; Hash = p.Hash(); }
            public bool Equals(FixedKey o)
            {
                return Hash == o.Hash && FixedPoint3.SameValue(ref P, ref o.P);
            }
            public override bool Equals(object o) { return o is FixedKey k && Equals(k); }
            public override int GetHashCode() { return Hash; }
        }
        // canonical id of each point after merge_points (null before), for ids below its length
        int[] canonical;

        public int PointCount { get { return System.Threading.Volatile.Read(ref nextPoint); } }

        /// <summary> the id that stands for all points equal to this one (after Compute: every id in Output is canonical) </summary>
        [MethodImpl(JitHints.Optimize)]
        public int Canonical(int id)
        {
            int[] c = canonical;
            return (c != null && id < c.Length) ? c[id] : id;
        }

        internal PointKind KindOf(int id) { return keys[id].Kind; }
        /// <summary> for tests: the exact point is an integer point with double coordinates (the predicates' double path) </summary>
        internal bool IsExactDouble(int id)
        {
            int r = exact_ref(id);
            return (r > 0) ? fixedPoints[r - 1].IsDouble : exactPoints[id].IsDouble;
        }
        /// <summary> the exact point of id as an integer point with double coordinates (d), if it is one </summary>
        internal bool TryExactDouble(int id, out Vector3d d) { return exact_double(id, exact_ref(id), out d); }
        /// <summary> for tests: the exact point is stored in fixed width </summary>
        internal bool IsExactFixed(int id) { return exact_ref(id) > 0; }
        internal double ApproxError(int id) { return approxErr[id]; }
        internal Vector3d Approx(int id) { return approx[id]; }

        int new_id()
        {
            return System.Threading.Interlocked.Increment(ref nextPoint) - 1;
        }

        [MethodImpl(JitHints.Optimize)]
        void store(int id, Vector3d a, double e, PointKey key)
        {
            approx.Set(id, a);
            approxErr.Set(id, e);
            keys.Set(id, key);
            exactRef.Set(id, 0);
        }

        // store the exact point x of id and publish it (thread-safe: two threads may store the same value, either
        // reference is valid); returns its exactRef value
        [MethodImpl(JitHints.Optimize)]
        int publish_exact(int id, ExactPoint3 x)
        {
            FixedPoint3 f;
            int r;
            if (FixedWidthPoints && FixedPoint3.TryCreate(x, out f)) {
                r = fixedPoints.Add(ref f) + 1;
            } else {
                exactPoints.Set(id, new ExactBox(x));
                r = -1;
            }
            System.Threading.Volatile.Write(ref exactRef.Ref(id), r);
            return r;
        }
        [MethodImpl(JitHints.Optimize)]
        int publish_exact(int id, ref FixedPoint3 f)
        {
            int r = fixedPoints.Add(ref f) + 1;
            System.Threading.Volatile.Write(ref exactRef.Ref(id), r);
            return r;
        }

        /// <summary> id of an exactly given point, a new one if it is not known yet (thread-safe) </summary>
        [MethodImpl(JitHints.Optimize)]
        public int AddPoint(ExactPoint3 p)
        {
            Vector3d a = p.ApproxVector3d();
            double e = (p.W.IsOne ? 1 : 4) * U * max_abs(a) + Tiny;
            FixedPoint3 f;
            if (FixedWidthPoints && FixedPoint3.TryCreateLimbs(p, out f)) {
                // (the representation is a function of the value: a point in this index is never in the other)
                var fk = new FixedKey(ref f);
                var fshard = fixedIndex[(fk.Hash & 0x7FFFFFFF) % Shards];
                lock (fshard) {
                    int id;
                    if (fshard.TryGetValue(fk, out id))
                        return id;
                    id = new_id();
                    store(id, a, e, new PointKey(PointKind.Rational, id, 0, 0));
                    f.SetDouble(p);
                    publish_exact(id, ref f);
                    fshard[fk] = id;
                    return id;
                }
            }
            var hp = new HashedPoint(p);
            var shard = rationalIndex[(hp.Hash & 0x7FFFFFFF) % Shards];
            lock (shard) {
                int id;
                if (shard.TryGetValue(hp, out id))
                    return id;
                id = new_id();
                store(id, a, e, new PointKey(PointKind.Rational, id, 0, 0));
                publish_exact(id, p);
                // published after the point is stored: other threads only see the id once its data is valid
                shard[hp] = id;
                return id;
            }
        }

        // id of an implicit point (thread-safe); a, e: its approximation and error bound
        [MethodImpl(JitHints.Optimize)]
        int add_implicit(PointKey key, Vector3d a, double e)
        {
            bool hasExact = false;
            ExactPoint3 x = default(ExactPoint3);
            if (finite(a) == false || double.IsNaN(e) || double.IsInfinity(e)) {
                // out of the double range (or no usable bound): from the exact point
                PredicateStats.Call(PredicateStats.Kind.ArrImplicitOverflow);
                x = compute_exact(key, true);
                hasExact = true;
                a = x.ApproxVector3d();
                e = 4 * U * max_abs(a) + Tiny;
            }
            var shard = keyIndex[(key.GetHashCode() & 0x7FFFFFFF) % Shards];
            lock (shard) {
                int id;
                if (shard.TryGetValue(key, out id))
                    return id;
                id = new_id();
                store(id, a, e, key);
                if (hasExact)
                    publish_exact(id, x);
                shard[key] = id;
                return id;
            }
        }

        /// <summary> the exact point (computed when first needed; thread-safe) </summary>
        [MethodImpl(JitHints.Optimize)]
        public ExactPoint3 Exact(int id)
        {
            int r = exact_ref(id);
            return (r > 0) ? fixedPoints[r - 1].ToExact() : exactPoints[id].P;
        }

        // the exactRef value of id, computing the exact point when first needed (thread-safe)
        [MethodImpl(JitHints.Optimize)]
        int exact_ref(int id)
        {
            int r = System.Threading.Volatile.Read(ref exactRef.Ref(id));
            if (r != 0)
                return r;
            PredicateStats.Call(PredicateStats.Kind.ArrExactPoint);
            PointKey key = keys[id];
            return publish_exact(id, (key.Kind == PointKind.Input) ? input_exact(inputCoords[id]) : compute_exact(key, true));
        }

        // the exact point of id as an integer point with double coordinates, if it is one
        [MethodImpl(JitHints.Optimize)]
        bool exact_double(int id, int r, out Vector3d d)
        {
            if (r > 0) {
                ref FixedPoint3 f = ref fixedPoints[r - 1];
                d = f.D;
                return f.IsDouble;
            }
            ExactBox b = exactPoints[id];
            d = b.D;
            return b.IsDouble;
        }

        /// <summary> the exact points of a and b are equal (both computed when needed) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal bool ExactEquals(int a, int b)
        {
            int ra = exact_ref(a), rb = exact_ref(b);
            if (ra > 0 && rb > 0)
                return FixedPoint3.SameValue(ref fixedPoints[ra - 1], ref fixedPoints[rb - 1]);
            if (ra > 0 || rb > 0)
                return false;       // (the representation is a function of the value)
            return exactPoints[a].P.Equals(exactPoints[b].P);
        }

        [MethodImpl(JitHints.Optimize)]
        internal int ExactHash(int id)
        {
            int r = exact_ref(id);
            return (r > 0) ? fixedPoints[r - 1].Hash() : exactPoints[id].P.GetHashCode();
        }

        // point ids compared by their exact points
        sealed class ExactIdComparer : IEqualityComparer<int>
        {
            readonly ExactMeshArrangement arr;
            public ExactIdComparer(ExactMeshArrangement arr) { this.arr = arr; }
            public bool Equals(int a, int b) { return a == b || arr.ExactEquals(a, b); }
            public int GetHashCode(int id) { return arr.ExactHash(id); }
        }

        // the grid point of input coordinates
        ExactPoint3 input_exact(Vector3d v)
        {
            return ExactPoint3.FromInteger(GridValue(v.x, 0), GridValue(v.y, 1), GridValue(v.z, 2));
        }

        /// <summary> the grid coordinate (relative to Center) of the input coordinate v on axis k, exactly </summary>
        [MethodImpl(JitHints.Optimize)]
        public BigInteger GridValue(double v, int k)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            return (m.IsZero ? BigInteger.Zero : (m << (e - ScaleExp))) - Center[k];
        }

        // the point of a construction, canonical if reduce (else W > 0 but not reduced, cheaper)
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        static void edge_plane_exact(ExactPoint3 P, ExactPoint3 Q, Plane plane, out BigInteger x, out BigInteger y, out BigInteger z, out BigInteger w)
        {
            BigInteger sp = plane.A * P.X + plane.B * P.Y + plane.C * P.Z - plane.D * P.W;
            BigInteger sq = plane.A * Q.X + plane.B * Q.Y + plane.C * Q.Z - plane.D * Q.W;
            x = sp * Q.X - sq * P.X; y = sp * Q.Y - sq * P.Y; z = sp * Q.Z - sq * P.Z; w = sp * Q.W - sq * P.W;
        }

        // where the segment P-Q crosses the line R-S (in the same plane), in the projection that drops axis
        [MethodImpl(JitHints.Optimize)]
        static void segments_exact(ExactPoint3 P, ExactPoint3 Q, ExactPoint3 R, ExactPoint3 S, int axis, out BigInteger x, out BigInteger y, out BigInteger z, out BigInteger w)
        {
            int i = (axis + 1) % 3, j = (axis + 2) % 3;
            BigInteger sp = ExactPoint3.Det3(R[i], R[j], R.W, S[i], S[j], S.W, P[i], P[j], P.W);
            BigInteger sq = ExactPoint3.Det3(R[i], R[j], R.W, S[i], S[j], S.W, Q[i], Q[j], Q.W);
            x = sp * Q.X - sq * P.X; y = sp * Q.Y - sq * P.Y; z = sp * Q.Z - sq * P.Z; w = sp * Q.W - sq * P.W;
        }

        // the point common to three planes (they must be independent)
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        public int EdgePlanePoint(int p, int q, Plane plane)
        {
            BigInteger x, y, z, w;
            edge_plane_exact(Exact(p), Exact(q), plane, out x, out y, out z, out w);
            return AddPoint(ExactPoint3.Create(x, y, z, w));
        }

        /// <summary> the point common to three planes (they must be independent), as a rational point </summary>
        [MethodImpl(JitHints.Optimize)]
        public int PlanesPoint(Plane p1, Plane p2, Plane p3)
        {
            BigInteger x, y, z, w;
            planes_exact(p1, p2, p3, out x, out y, out z, out w);
            return AddPoint(ExactPoint3.Create(x, y, z, w));
        }



        // ---- implicit points: constructions with their approximations ----

        /// <summary> where edge p-q of rational points crosses the plane (key); the ends are on opposite sides </summary>
        [MethodImpl(JitHints.Optimize)]
        internal int EdgePlanePointImplicit(int p, int q, int plane)
        {
            if (p > q) { int tmp = p; p = q; q = tmp; }
            plane = CanonicalPlane(plane);
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
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        internal int PlanesPointImplicit(int k1, int k2, int k3)
        {
            k1 = CanonicalPlane(k1); k2 = CanonicalPlane(k2); k3 = CanonicalPlane(k3);
            if (k1 > k2) { int tmp = k1; k1 = k2; k2 = tmp; }
            if (k2 > k3) { int tmp = k2; k2 = k3; k3 = tmp; }
            if (k1 > k2) { int tmp = k1; k1 = k2; k2 = tmp; }
            double e;
            Vector3d a = planes_approx(k1, k2, k3, out e);
            return add_implicit(new PointKey(PointKind.Planes, k1, k2, k3), a, e);
        }

        const double U = 2.220446049250313e-16;     // 2^-52
        // absolute term of the error bounds of approximations: relative bounds (multiples of U) fail where a result
        // is subnormal or underflows to zero (absolute error up to 2^-1075 per rounding, or the whole value). Far
        // above that, and far below the distances of distinct points (quotients of bounded integers), so the
        // filters still decide
        internal const double Tiny = 1e-300;
        // for tests: always evaluate the predicates exactly (integer points with double coordinates still go
        // through ExactPredicates, whose own filter is exact)
        internal bool DisableFilters = false;

        static double max_abs(Vector3d a)
        {
            return Math.Max(Math.Abs(a.x), Math.Max(Math.Abs(a.y), Math.Abs(a.z)));
        }

        [MethodImpl(JitHints.Optimize)]
        static bool finite(Vector3d a)
        {
            double s = a.x + a.y + a.z;
            return double.IsNaN(s) == false && double.IsInfinity(s) == false;
        }

        // A x + B y + C z - D for the plane at pa[o..o+3] (relative errors 2^-52) and a point with coordinate errors e
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        internal static double orient2d_approx(Vector3d a, Vector3d b, Vector3d c, int i, int j, double e, out double err)
        {
            double bx = b[i] - a[i], by = b[j] - a[j], cx = c[i] - a[i], cy = c[j] - a[j];
            double M = Math.Max(Math.Max(Math.Abs(bx), Math.Abs(by)), Math.Max(Math.Abs(cx), Math.Abs(cy)));
            double eps = 2 * e + U * M;
            err = 1.01 * (2 * eps * (2 * M + eps) + 3 * U * M * M) + Tiny;
            return bx * cy - by * cx;
        }

        [MethodImpl(JitHints.Optimize)]
        Vector3d planes_approx(int k1, int k2, int k3, out double err)
        {
            return planes_approx(Triangles[k1 >> 2].PlaneApprox, 4 * (k1 & 3), Triangles[k2 >> 2].PlaneApprox, 4 * (k2 & 3),
                Triangles[k3 >> 2].PlaneApprox, 4 * (k3 & 3), out err);
        }

        // the point common to the planes at q1[o1..o1+3], q2[o2..], q3[o3..] (coefficients with relative errors 2^-52)
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        public Vector3d PointToVector(int id)
        {
            PointKey key = keys[id];
            if (key.Kind == PointKind.Input)
                return inputCoords[id];
            int r = System.Threading.Volatile.Read(ref exactRef.Ref(id));
            return ToInputVector((r != 0) ? Exact(id) : compute_exact(key, false));
        }

        /// <summary> PointToVector of many points, in parallel </summary>
        [MethodImpl(JitHints.Optimize)]
        public Vector3d[] PointsToVectors(IList<int> ids)
        {
            var v = new Vector3d[ids.Count];
            gParallel.ForRange(ids.Count, (k) => v[k] = PointToVector(ids[k]));
            return v;
        }

        /// <summary> the exact point p (grid coordinates) in input coordinates </summary>
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        public Vector3d ToInputVector(ExactPoint3 p)
        {
            return new Vector3d(ExactPoint3.ToDouble(p.X + Center[0] * p.W, p.W, ScaleExp),
                ExactPoint3.ToDouble(p.Y + Center[1] * p.W, p.W, ScaleExp), ExactPoint3.ToDouble(p.Z + Center[2] * p.W, p.W, ScaleExp));
        }



        // order of the points: by a generic linear function a . p (few points with different coordinates have equal
        // values, unlike for a single coordinate, so the exact points are rarely needed), then lexicographic
        internal static readonly double[] Alpha = { 1.0, 0.7548776662466927, 0.5698402909980532 };
        static readonly BigInteger[] AlphaInt = { alpha_int(Alpha[0]), alpha_int(Alpha[1]), alpha_int(Alpha[2]) };
        // (each at most 2^53: one limb)
        static readonly ulong[] AlphaLimb = { (ulong)AlphaInt[0], (ulong)AlphaInt[1], (ulong)AlphaInt[2] };
        [MethodImpl(JitHints.Optimize)]
        static BigInteger alpha_int(double a)
        {
            // a * 2^53, an integer for a in [0.5, 1]
            BigInteger m; int e;
            ExactPoint3.Decompose(a, out m, out e);
            return m << (e + 53);
        }

        // a . p of the approximation, with a bound on its error
        [MethodImpl(JitHints.Optimize)]
        internal static double order_key(Vector3d a, double e, out double err)
        {
            double x = Alpha[0] * a.x, y = Alpha[1] * a.y, z = Alpha[2] * a.z;
            err = 1.01 * (2.33 * e + 2 * U * (Math.Abs(x) + Math.Abs(y) + Math.Abs(z))) + Tiny;
            return x + y + z;
        }

        /// <summary> a total order of the exact points (deterministic, unlike point ids); 0 iff the points are equal </summary>
        [MethodImpl(JitHints.Optimize)]
        public int ComparePoints(int a, int b)
        {
            if (a == b)
                return 0;
            PredicateStats.Call(PredicateStats.Kind.ArrCompare);
            if (DisableFilters == false) {
                double ea, eb;
                double ka = order_key(approx[a], approxErr[a], out ea), kb = order_key(approx[b], approxErr[b], out eb);
                double bound = 1.01 * (ea + eb);
                if (ka - kb > bound) return 1;
                if (kb - ka > bound) return -1;
            }
            PredicateStats.ExactStage(PredicateStats.Kind.ArrCompare);
            int ra = exact_ref(a), rb = exact_ref(b);
            if (ra > 0 && rb > 0)
                return FixedArith.ComparePoints(ref fixedPoints[ra - 1], ref fixedPoints[rb - 1], AlphaLimb[0], AlphaLimb[1], AlphaLimb[2]);
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

        // clusters larger than this with a point whose approximation is beyond the double range (its interval overlaps
        // all others, so its cluster can hold any number of distinct points) are sorted: ComparePoints separates most of
        // their points by the filter, without the exact points. (Large clusters of finite intervals are coincident
        // points, as in grids of coplanar boxes: hashed.) For tests: below 0 sorts all clusters of more than two points.
        internal int MaxHashedCluster = 64;

        // merge_cluster by sorting with ComparePoints: equal points are adjacent
        int merge_sorted(List<int> ids, int[] order, int k0, int k1, int[] next)
        {
            var members = new List<int>(k1 - k0);
            for (int k = k0; k < k1; ++k)
                members.Add(ids[order[k]]);
            members.Sort(ComparePoints);
            int merged = 0;
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
                merged += r1 - r0 - 1;
            }
            return merged;
        }

        // the points ids[order[k0..k1-1]] (a cluster of overlapping order key intervals with lower ends lo, in their
        // order): equal points get the id with the smallest key (next); returns the number of points merged. The points
        // are grouped by their exact (canonical) points, so that each is computed once instead of in every comparison
        // of a sort; two points are compared (the filter may separate them without the exact points)
        [MethodImpl(JitHints.Optimize)]
        int merge_cluster(List<int> ids, int[] order, double[] lo, int k0, int k1, int[] next)
        {
            if (k1 - k0 == 2) {
                int a = ids[order[k0]], b = ids[order[k0 + 1]];
                if (ComparePoints(a, b) != 0)
                    return 0;
                int best = (keys[a].CompareTo(keys[b]) <= 0) ? a : b;
                next[a] = next[b] = best;
                return 1;
            }
            if (MaxHashedCluster < 0 || (k1 - k0 > MaxHashedCluster && double.IsNegativeInfinity(lo[order[k0]])))
                return merge_sorted(ids, order, k0, k1, next);
            var groups = new Dictionary<int, List<int>>(new ExactIdComparer(this));
            for (int k = k0; k < k1; ++k) {
                int id = ids[order[k]];
                List<int> g;
                if (groups.TryGetValue(id, out g) == false)
                    groups[id] = g = new List<int>(2);
                g.Add(id);
            }
            int merged = 0;
            foreach (List<int> g in groups.Values) {
                if (g.Count == 1)
                    continue;
                int best = g[0];
                foreach (int id in g)
                    if (keys[id].CompareTo(keys[best]) < 0)
                        best = id;
                foreach (int id in g)
                    next[id] = best;
                merged += g.Count - 1;
            }
            return merged;
        }

        /// <summary>
        /// Merge coincident points from different constructions: each gets the id with the smallest key (a
        /// rational point if there is one, so input vertices keep their coordinates). Candidates are points whose
        /// order keys (with error bounds) overlap; they are compared exactly.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
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
            gParallel.ForRange(m, (k) => {
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
            gParallel.ForRange(clusters.Count, (ci) => {
                int c = merge_cluster(ids, order, lo, clusters[ci].Key, clusters[ci].Value, next);
                if (c > 0)
                    System.Threading.Interlocked.Add(ref merged, c);
            });
            // points merged before follow their (possibly merged) canonical point
            for (int id = 0; id < n; ++id)
                next[id] = next[next[id]];
            MergedPoints += merged;
            canonical = next;
        }



        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> bytes allocated per phase (by all threads; 0 on netstandard2.0), for profiling </summary>
        internal Dictionary<string, long> PhaseAllocatedBytes = new Dictionary<string, long>();

        [MethodImpl(JitHints.Optimize)]
        public void Compute()
        {
            var clock = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
            var total = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
            build_points();
            clock.Lap("setup.points");
            build_triangles();
            clock.Lap("setup.triangles");
            total.Lap("setup");
            find_intersections(clock);
            total.Lap("intersections");
            merge_points();
            clock.Lap("merge.points");
            int before = PointCount;
            gParallel.ForRange(Triangles.Count, (ti) => prepare_triangle(ti));
            clock.Lap("merge.crossings");
            if (PointCount > before)
                merge_points();
            clock.Lap("merge.points2");
            total.Lap("merge");
            var pieces = new List<OutputTriangle>[Triangles.Count];
            var constraints = new List<long>[Triangles.Count];
            gParallel.ForRange(Triangles.Count, (ti) => triangulate(ti, out pieces[ti], out constraints[ti]));
            int pieceCount = 0;
            for (int ti = 0; ti < Triangles.Count; ++ti)
                pieceCount += (pieces[ti] != null) ? pieces[ti].Count : 1;
            Output.Capacity = Math.Max(Output.Capacity, Output.Count + pieceCount);
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
            total.Lap("triangulation");
        }



        [MethodImpl(JitHints.Optimize)]
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
            int vertexCount = 0;
            foreach (DMesh3 mesh in Meshes)
                vertexCount += mesh.VertexCount;
            var index = new Dictionary<Vector3d, int>(vertexCount);
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
                        bool hasExact = false;
                        ExactPoint3 x = default(ExactPoint3);
                        for (int k = 0; k < 3; ++k) {
                            // grid value v 2^-ScaleExp (exact) minus the center: rounded once, error below u |a|
                            a[k] = ExactPoint3.ScaleB(v[k], -ScaleExp) - centerD[k];
                            e = Math.Max(e, U * Math.Abs(a[k]));
                        }
                        if (finite(a) == false) {
                            // beyond the double range in grid units
                            x = input_exact(v);
                            hasExact = true;
                            a = x.ApproxVector3d();
                            e = 4 * U * max_abs(a);
                        }
                        store(id, a, e, new PointKey(PointKind.Input, id, 0, 0));
                        if (hasExact)
                            publish_exact(id, x);
                    }
                    VertexPoints[mi][vid] = id;
                }
            }
        }

        // the exponent of the lowest set bit of v != 0 (v = m * 2^e with m odd)
        [MethodImpl(JitHints.Optimize)]
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


        [MethodImpl(JitHints.Optimize)]
        void build_triangles()
        {
            MeshTriangles = new List<int>[Meshes.Count];
            int total = Triangles.Count;
            foreach (DMesh3 mesh in Meshes)
                total += mesh.TriangleCount;
            Vector3d[] earlier = triangleCoords;
            triangleCoords = new Vector3d[3 * total];
            if (earlier != null)
                Array.Copy(earlier, triangleCoords, 3 * Triangles.Count);
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                DMesh3 mesh = Meshes[mi];
                var tids = new List<int>(mesh.TriangleIndices());
                var built = new InputTriangle[tids.Count];
                int first = Triangles.Count;
                gParallel.ForRange(tids.Count, (k) => built[k] = input_triangle(mi, mesh, tids[k], first + k));
                MeshTriangles[mi] = new List<int>(built.Length);
                foreach (InputTriangle it in built) {
                    MeshTriangles[mi].Add(Triangles.Count);
                    Triangles.Add(it);
                }
            }
        }

        [MethodImpl(JitHints.Optimize)]
        InputTriangle input_triangle(int mi, DMesh3 mesh, int tid, int index)
        {
            Index3i t = mesh.GetTriangle(tid);
            InputTriangle it = new InputTriangle() { Mesh = mi, Tid = tid, Index = index };
            it.Corners = new Index3i(VertexPoints[mi][t.a], VertexPoints[mi][t.b], VertexPoints[mi][t.c]);
            it.arr = this;
            Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
            triangleCoords[3 * index] = a; triangleCoords[3 * index + 1] = b; triangleCoords[3 * index + 2] = c;
            // degenerate iff the corners are collinear, ie collinear in all three coordinate projections (exact); the
            // projection along the largest component of the normal (in doubles) first: it is not degenerate for a
            // triangle that is not, so its filter decides without the exact stage (a projection along a direction in
            // the plane, as of triangles in coordinate planes, is degenerate)
            Vector3d n = (b - a).Cross(c - a);
            double nx = Math.Abs(n.x), ny = Math.Abs(n.y), nz = Math.Abs(n.z);
            int first = (nz >= nx && nz >= ny) ? 2 : (nx >= ny) ? 0 : 1;
            it.Degenerate = true;
            for (int k = 0; k < 3 && it.Degenerate; ++k) {
                int axis = (first + k) % 3;     // projection dropping this axis
                int i = (axis + 1) % 3, j = (axis + 2) % 3;
                it.Degenerate = ExactPredicates.Orient2d(new Vector2d(a[i], a[j]), new Vector2d(b[i], b[j]), new Vector2d(c[i], c[j])) == 0;
            }
            return it;
        }


        /// <summary> orientation of three points in the projection of triangle t, +1 if counter-clockwise like t </summary>
        [MethodImpl(JitHints.Optimize)]
        public int Orient(InputTriangle t, int p, int q, int r)
        {
            if (p == q || q == r || r == p)
                return 0;
            PredicateStats.Call(PredicateStats.Kind.ArrOrient);
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            if (DisableFilters == false) {
                double e = Math.Max(approxErr[p], Math.Max(approxErr[q], approxErr[r]));
                double err;
                double fdet = orient2d_approx(approx[p], approx[q], approx[r], i, j, e, out err);
                if (fdet > err) return t.Sign;
                if (-fdet > err) return -t.Sign;
            }
            PredicateStats.ExactStage(PredicateStats.Kind.ArrOrient);
            int ra = exact_ref(p), rb = exact_ref(q), rc = exact_ref(r);
            Vector3d da, db, dc;
            if (exact_double(p, ra, out da) && exact_double(q, rb, out db) && exact_double(r, rc, out dc)) {
                // integer points with double coordinates: the same determinant (W = 1), by the adaptive predicate
                return ExactPredicates.Orient2d(new Vector2d(da[i], da[j]), new Vector2d(db[i], db[j]), new Vector2d(dc[i], dc[j])) * t.Sign;
            }
            if (ra > 0 && rb > 0 && rc > 0)
                return FixedArith.Orient(ref fixedPoints[ra - 1], ref fixedPoints[rb - 1], ref fixedPoints[rc - 1], i, j) * t.Sign;
            ExactPoint3 a = Exact(p), b = Exact(q), c = Exact(r);
            BigInteger det = ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W);
            return det.Sign * t.Sign;
        }

        /// <summary> InCircle decided from the approximations only (also with DisableFilters): 0 if they do not decide it </summary>
        [MethodImpl(JitHints.Optimize)]
        internal int InCircleFiltered(InputTriangle t, int p, int q, int r, int s)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            double e = Math.Max(Math.Max(approxErr[p], approxErr[q]), Math.Max(approxErr[r], approxErr[s]));
            double bound;
            double fdet = incircle_approx(approx[p], approx[q], approx[r], approx[s], i, j, e, out bound);
            if (fdet > bound) return t.Sign;
            if (-fdet > bound) return -t.Sign;
            return 0;
        }

        // incircle determinant of the approximations in coordinates (i, j), with coordinate errors at most e, and a
        // bound on its error
        [MethodImpl(JitHints.Optimize)]
        internal static double incircle_approx(Vector3d fa, Vector3d fb, Vector3d fc, Vector3d fd, int i, int j, double e, out double bound)
        {
            // differences at most M with errors eps = 2 e + U M; lifts and 2x2 minors at most 2 M^2 with errors
            // eta = 2 eps (2 M + eps); the three terms: 3 eta (4 M^2 + eta); rounding (Shewchuk) 10 u of the
            // absolute terms, at most 12 M^4
            double adx = fa[i] - fd[i], ady = fa[j] - fd[j], bdx = fb[i] - fd[i], bdy = fb[j] - fd[j], cdx = fc[i] - fd[i], cdy = fc[j] - fd[j];
            double fdet = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
            double M = Math.Max(Math.Max(Math.Max(Math.Abs(adx), Math.Abs(ady)), Math.Max(Math.Abs(bdx), Math.Abs(bdy))), Math.Max(Math.Abs(cdx), Math.Abs(cdy)));
            double eps = 2 * e + U * M, eta = 2 * eps * (2 * M + eps), M2 = M * M;
            bound = 1.01 * (3 * eta * (4 * M2 + eta) + 64 * U * M2 * M2) + Tiny;
            return fdet;
        }

        /// <summary> +1 if s is inside the circle through p, q, r (counter-clockwise in the projection of t) </summary>
        [MethodImpl(JitHints.Optimize)]
        public int InCircle(InputTriangle t, int p, int q, int r, int s)
        {
            PredicateStats.Call(PredicateStats.Kind.ArrInCircle);
            if (DisableFilters == false) {
                int f = InCircleFiltered(t, p, q, r, s);
                if (f != 0)
                    return f;
            }
            PredicateStats.ExactStage(PredicateStats.Kind.ArrInCircle);
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            int ra = exact_ref(p), rb = exact_ref(q), rc = exact_ref(r), rd = exact_ref(s);
            Vector3d da, db, dc, dd;
            if (exact_double(p, ra, out da) && exact_double(q, rb, out db) && exact_double(r, rc, out dc) && exact_double(s, rd, out dd)) {
                // integer points with double coordinates: the same determinant (W = 1, see below), by the adaptive
                // predicate
                return ExactPredicates.InCircle(new Vector2d(da[i], da[j]), new Vector2d(db[i], db[j]),
                    new Vector2d(dc[i], dc[j]), new Vector2d(dd[i], dd[j])) * t.Sign;
            }
            // The 4x4 determinant with rows (x w, y w, x^2 + y^2, w^2) = w^2 (x/w, y/w, |.|^2, 1) has the sign of the
            // one with rows (x/w, y/w, |.|^2, 1) (w^2 > 0), which equals the 3x3 determinant with rows (dx, dy,
            // dx^2 + dy^2) of the differences to d (column operations, then subtracting row d). Each row is multiplied
            // by (w w_d)^2 > 0 to make it integer: (A s, B s, A^2 + B^2) with A = x w_d - x_d w, B likewise, s = w w_d.
            if (ra > 0 && rb > 0 && rc > 0 && rd > 0)
                return FixedArith.InCircle(ref fixedPoints[ra - 1], ref fixedPoints[rb - 1], ref fixedPoints[rc - 1], ref fixedPoints[rd - 1], i, j) * t.Sign;
            ExactPoint3 pd = Exact(s);
            BigInteger ax, ay, al, bx, by, bl, cx, cy, cl;
            incircle_row(Exact(p), pd, i, j, out ax, out ay, out al);
            incircle_row(Exact(q), pd, i, j, out bx, out by, out bl);
            incircle_row(Exact(r), pd, i, j, out cx, out cy, out cl);
            BigInteger det = ExactPoint3.Det3(ax, ay, al, bx, by, bl, cx, cy, cl);
            // for counter-clockwise a, b, c this determinant is positive iff d is inside; the projection
            // may mirror the triangle, which flips the orientation of a, b, c and the sign
            return det.Sign * t.Sign;
        }

        [MethodImpl(JitHints.Optimize)]
        static void incircle_row(ExactPoint3 v, ExactPoint3 d, int i, int j, out BigInteger x, out BigInteger y, out BigInteger l)
        {
            BigInteger A, B, s;
            if (v.W.IsOne && d.W.IsOne) {
                A = v[i] - d[i]; B = v[j] - d[j];
                x = A; y = B;
            } else {
                A = v[i] * d.W - d[i] * v.W; B = v[j] * d.W - d[j] * v.W; s = v.W * d.W;
                x = A * s; y = B * s;
            }
            l = A * A + B * B;
        }

        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        bool inside(InputTriangle t, int r)
        {
            return Orient(t, t.Corners[0], t.Corners[1], r) >= 0
                && Orient(t, t.Corners[1], t.Corners[2], r) >= 0
                && Orient(t, t.Corners[2], t.Corners[0], r) >= 0;
        }

        /// <summary> a direction (integer), with its coordinates rounded to double for the filter of compare_along </summary>
        internal struct Direction
        {
            public readonly BigInteger X, Y, Z;
            public readonly double FX, FY, FZ;
            public Direction(BigInteger x, BigInteger y, BigInteger z)
            {
                X = x; Y = y; Z = z;
                FX = (double)x; FY = (double)y; FZ = (double)z;
            }
        }

        // position of point along a direction: compare (d . p1) / w1 with (d . p2) / w2
        internal int compare_along(BigInteger dx, BigInteger dy, BigInteger dz, int p1, int p2)
        {
            return compare_along(new Direction(dx, dy, dz), p1, p2);
        }

        [MethodImpl(JitHints.Optimize)]
        internal int compare_along(in Direction d, int p1, int p2)
        {
            if (p1 == p2)
                return 0;
            PredicateStats.Call(PredicateStats.Kind.ArrAlong);
            if (DisableFilters == false) {
                double bound;
                double diff = along_approx(d.FX, d.FY, d.FZ, approx[p1], approx[p2], Math.Max(approxErr[p1], approxErr[p2]), out bound);
                if (diff > bound) return 1;
                if (-diff > bound) return -1;
            }
            PredicateStats.ExactStage(PredicateStats.Kind.ArrAlong);
            int r1 = exact_ref(p1), r2 = exact_ref(p2);
            if (r1 > 0 && r2 > 0) {
                int c;
                if (compare_along_fixed(d, ref fixedPoints[r1 - 1], ref fixedPoints[r2 - 1], out c))
                    return c;
            }
            ExactPoint3 a = Exact(p1), b = Exact(p2);
            BigInteger ka = (d.X * a.X + d.Y * a.Y + d.Z * a.Z) * b.W;
            BigInteger kb = (d.X * b.X + d.Y * b.Y + d.Z * b.Z) * a.W;
            return ka.CompareTo(kb);
        }

        // compare_along on fixed-width points, false if a component of the direction has more than 8 limbs
        [MethodImpl(JitHints.Optimize)]
        static unsafe bool compare_along_fixed(in Direction d, ref FixedPoint3 a, ref FixedPoint3 b, out int c)
        {
            c = 0;
            ulong* l = stackalloc ulong[24];
            int nx, ny, nz;
            bool sx, sy, sz;
            if (FixedPoint3.LoadN(d.X, l, 8, out nx, out sx) == false || FixedPoint3.LoadN(d.Y, l + 8, 8, out ny, out sy) == false
                || FixedPoint3.LoadN(d.Z, l + 16, 8, out nz, out sz) == false)
                return false;
            c = FixedArith.CompareAlong(ref a, ref b, new FixedInt(l, nx, sx), new FixedInt(l + 8, ny, sy), new FixedInt(l + 16, nz, sz));
            return true;
        }

        // d . (a - b) for the direction d rounded to double (fdx, fdy, fdz: relative errors U) and approximations a, b
        // with coordinate errors at most e, and a bound on its error: differences with errors 2 e + U |.|, rounding
        // of the dot product
        [MethodImpl(JitHints.Optimize)]
        internal static double along_approx(double fdx, double fdy, double fdz, Vector3d fa, Vector3d fb, double e, out double bound)
        {
            double ux = fa.x - fb.x, uy = fa.y - fb.y, uz = fa.z - fb.z;
            bound = 1.01 * (2 * e * (Math.Abs(fdx) + Math.Abs(fdy) + Math.Abs(fdz))
                + 5 * U * (Math.Abs(fdx * ux) + Math.Abs(fdy * uy) + Math.Abs(fdz * uz))) + Tiny;
            return fdx * ux + fdy * uy + fdz * uz;
        }

        // direction of the line where the plane line meets the triangle plane
        [MethodImpl(JitHints.Optimize)]
        static Direction line_direction(InputTriangle t, Plane line)
        {
            return new Direction(t.NY * line.C - t.NZ * line.B, t.NZ * line.A - t.NX * line.C, t.NX * line.B - t.NY * line.A);
        }



        /// <summary> also intersect the triangles of each mesh with each other (self-intersections) </summary>
        public bool SelfIntersections = false;
        /// <summary> triangles per batch of the candidate pair search (bounds its memory; smaller for tests) </summary>
        internal long CandidateBatchSize = 1 << 24;
        public int SelfIntersectingPairs;
        /// <summary>
        /// for tests: the candidate pairs within a mesh by one self-join of its box tree (each pair once), else by a
        /// query per triangle (each pair found twice, once dropped): the same pairs, in another order
        /// </summary>
        internal bool SelfJoin = true;
        /// <summary> per mesh: box tree of its triangles (in the order of MeshTriangles), after Compute() </summary>
        public TriangleBoxTree[] Trees;

        [MethodImpl(JitHints.Optimize)]
        void find_intersections(PhaseClock clock)
        {
            checkPrefilter = CheckPrefilterOnThisThread;
            // boxes of the exact input coordinates
            var boxes = new List<AxisAlignedBox3d>[Meshes.Count];
            var trees = Trees = new TriangleBoxTree[Meshes.Count];
            gParallel.ForRange(Meshes.Count, (mi) => {
                boxes[mi] = new List<AxisAlignedBox3d>(MeshTriangles[mi].Count);
                foreach (int ti in MeshTriangles[mi])
                    boxes[mi].Add(triangle_box(Triangles[ti]));
                trees[mi] = new TriangleBoxTree(boxes[mi]);
            });
            clock.Lap("intersections.trees");
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
                    if (mb > ma || (mb == ma && SelfIntersections && SelfJoin == false))
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
                gParallel.ForRange(found.Length, () => new List<int>(), (j, hits) => {
                    int p = Array.BinarySearch(offsets, j);
                    if (p < 0)      // (the offsets increase strictly: every pair has triangles of its first mesh)
                        p = ~p - 1;
                    int ma = meshPairs[p0 + p].a, mb = meshPairs[p0 + p].b, ka = j - offsets[p];
                    int s = candidates(ma, ka, mb, boxes[ma][ka], trees[mb], hits, ref found[j]);
                    if (s > 0)
                        System.Threading.Interlocked.Add(ref self, s);
                });
                foreach (var list in found)
                    if (list != null)
                        pairs.AddRange(list);
            }
            if (SelfIntersections && SelfJoin)
                self += self_pairs(trees, pairs);
            SelfIntersectingPairs += self;
            clock.Lap("intersections.pairs");
            // planes of the triangles involved (computed once, before the parallel use)
            var needed = new bool[Triangles.Count];
            foreach (long pr in pairs) {
                needed[(int)(pr >> 32)] = true;
                needed[(int)(pr & 0xFFFFFFFF)] = true;
            }
            // and the same plane for triangles in one plane: candidates by a key from the plane's double
            // approximation (normalized by its largest normal component, rounded), then compared exactly with at most
            // MaxPlaneCandidates planes of the bucket. Equal planes whose keys round apart, or that are not among those
            // candidates, are only constructed separately, as before; planes whose approximation is not finite too.
            var bucketKeys = new (double, double, double, double)[Triangles.Count];
            gParallel.ForRange(Triangles.Count, (ti) => {
                if (needed[ti])
                    bucketKeys[ti] = plane_bucket(Triangles[ti].PlaneApprox);
            });
            var buckets = new Dictionary<(double, double, double, double), List<int>>();
            var rep = new int[Triangles.Count];
            for (int ti = 0; ti < Triangles.Count; ++ti) {
                rep[ti] = ti;
                var bucketKey = bucketKeys[ti];
                if (needed[ti] == false || finite(bucketKey) == false)
                    continue;
                List<int> reps;
                if (buckets.TryGetValue(bucketKey, out reps) == false)
                    buckets[bucketKey] = reps = new List<int>(1);
                int same = -1;
                foreach (int r in reps)
                    if (same_plane(Triangles[r].P, Triangles[ti].P)) { same = r; break; }
                if (same >= 0)
                    rep[ti] = same;
                else if (reps.Count < MaxPlaneCandidates)
                    reps.Add(ti);
            }
            planeRep = rep;
            clock.Lap("intersections.planes");
            gParallel.ForRange(pairs.Count, (k) => intersect((int)(pairs[k] >> 32), (int)(pairs[k] & 0xFFFFFFFF)));
            clock.Lap("intersections.intersect");
            CandidatePairs = pairs.Count;
            if (KeepCandidatePairs)
                CandidatePairList = new List<long>(pairs);
        }
        public int CandidatePairs;

        // per triangle: a triangle in the same plane that stands for its plane (after find_intersections; null before:
        // each its own)
        int[] planeRep;
        // planes of one bucket compared exactly (distinct planes rarely share a bucket; bounds the time if they do)
        const int MaxPlaneCandidates = 8;

        static bool finite((double, double, double, double) k)
        {
            double s = k.Item1 + k.Item2 + k.Item3 + k.Item4;
            return double.IsNaN(s) == false && double.IsInfinity(s) == false;
        }

        /// <summary>
        /// the key of a plane (PlaneOf) that stands for equal planes: a triangle's plane by the first triangle (in the
        /// order of the triangles) in the same plane and the same rounding bucket (see find_intersections), so that the
        /// same point is the same construction (a key) when it is constructed with different triangles in one plane
        /// (grids of coplanar faces); edge planes as they are
        /// </summary>
        internal int CanonicalPlane(int key)
        {
            int[] r = planeRep;
            return (r == null || ShareCoplanarConstructions == false || (key & 3) != 0) ? key : 4 * r[key >> 2];
        }

        // for tests: points constructed with triangles of one plane share their key (CanonicalPlane)
        internal bool ShareCoplanarConstructions = true;

        // the plane's approximation pa[0..3] divided by its largest normal component (sign included), each rounded to a
        // multiple of 2^-30 times the power of two at or below the larger of 1 and its magnitude: equal planes give the
        // same key unless a value is within the approximation's error of a rounding boundary
        static (double, double, double, double) plane_bucket(double[] pa)
        {
            double a = pa[0], b = pa[1], c = pa[2], d = pa[3];
            double m = (Math.Abs(a) >= Math.Abs(b) && Math.Abs(a) >= Math.Abs(c)) ? a : (Math.Abs(b) >= Math.Abs(c)) ? b : c;
            return (round_key(a / m), round_key(b / m), round_key(c / m), round_key(d / m));
        }

        static double round_key(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
                return x;
            // 2^(e - 30) for the exponent e of max(1, |x|): a power of two, so that values a few ulps apart round alike
            long e = (BitConverter.DoubleToInt64Bits(Math.Max(1, Math.Abs(x))) >> 52) & 0x7FF;
            double scale = BitConverter.Int64BitsToDouble((e - 30) << 52);
            double r = Math.Round(x / scale) * scale;
            return (r == 0) ? 0 : r;    // (-0)
        }

        // the planes are the same point set: (A, B, C, D) proportional (all 2x2 minors zero)
        internal static bool same_plane(Plane p, Plane q)
        {
            return p.A * q.B == q.A * p.B && p.A * q.C == q.A * p.C && p.B * q.C == q.B * p.C
                && p.A * q.D == q.A * p.D && p.B * q.D == q.B * p.D && p.C * q.D == q.C * p.D;
        }

        // the candidate pairs of triangle ka of mesh ma with the triangles of mesh mb (tree) whose boxes overlap box,
        // appended to found (created when needed); hits: a buffer. Returns the number of pairs within one mesh.
        [MethodImpl(JitHints.Optimize)]
        int candidates(int ma, int ka, int mb, AxisAlignedBox3d box, TriangleBoxTree tree, List<int> hits, ref List<long> found)
        {
            InputTriangle T = Triangles[MeshTriangles[ma][ka]];
            if (T.Degenerate)
                return 0;
            int self = 0;
            hits.Clear();
            tree.Query(box, hits);
            foreach (int kb in hits) {
                if (ma == mb && kb <= ka)
                    continue;
                InputTriangle U = Triangles[MeshTriangles[mb][kb]];
                if (U.Degenerate)
                    continue;
                if (ma == mb) {
                    if (touch_only(T, U))
                        continue;
                } else if (separated(T, U))
                    continue;
                if (ma == mb)
                    self++;
                if (found == null)
                    found = new List<long>();
                found.Add(((long)MeshTriangles[ma][ka] << 32) | (uint)MeshTriangles[mb][kb]);
            }
            return self;
        }

        // the candidate pairs within each mesh (boxes overlap, not only touching), appended to pairs as (smaller,
        // larger triangle index); returns their number. The self-join's work items run in parallel, each into a
        // list of its own; the lists are appended in the order of the items, so the result depends only on the trees.
        [MethodImpl(JitHints.Optimize)]
        int self_pairs(TriangleBoxTree[] trees, List<long> pairs)
        {
            int self = 0;
            for (int m = 0; m < Meshes.Count; ++m) {
                List<int> tris = MeshTriangles[m];
                if (tris.Count < 2)
                    continue;
                List<Index2i> tasks = trees[m].SelfPairTasks(SelfJoinTasks);
                var found = new List<long>[tasks.Count];
                gParallel.ForRange(tasks.Count, () => new List<Index2i>(), (k, boxPairs) => {
                    boxPairs.Clear();
                    trees[m].SelfPairs(tasks[k], boxPairs);
                    List<long> list = null;
                    foreach (Index2i bp in boxPairs) {
                        int ka = Math.Min(bp.a, bp.b), kb = Math.Max(bp.a, bp.b);
                        InputTriangle T = Triangles[tris[ka]], U = Triangles[tris[kb]];
                        if (T.Degenerate || U.Degenerate || touch_only(T, U))
                            continue;
                        (list ?? (list = new List<long>())).Add(((long)tris[ka] << 32) | (uint)tris[kb]);
                    }
                    found[k] = list;
                });
                foreach (List<long> list in found)
                    if (list != null) {
                        pairs.AddRange(list);
                        self += list.Count;
                    }
            }
            return self;
        }

        /// <summary> for tests: keep a copy of the candidate pairs (CandidatePairList) </summary>
        internal bool KeepCandidatePairs = false;
        internal List<long> CandidatePairList;
        /// <summary> for tests: the number of work items the self-join is split into (at least) </summary>
        internal int SelfJoinTasks = 256;

        // all corners of one triangle strictly on one side of the other's plane (exact double predicates)
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        internal bool touch_only(InputTriangle T, InputTriangle U)
        {
            int shared = 0;
            for (int k = 0; k < 3; ++k) {
                int c = U.Corners[k];
                if (c == T.Corners[0] || c == T.Corners[1] || c == T.Corners[2])
                    shared++;
            }
            // quick test: the corners of one triangle that are not shared all strictly on one side of the other's
            // plane: then they meet at most in the shared vertices (for a shared edge: the triangles are not
            // coplanar, so the plane of one meets the other only in the edge)
            int st = -1;       // (shared = 3: the general code, as before)
            if (shared < 3) {
                st = one_side_except_shared(T, U);
                if (st > 0 || (st < 0 && shared < 2 && one_side_except_shared(U, T) > 0))
                    return true;
            }
            // corners by point id, so that coincident (unwelded) vertices count as shared
            Index3i pa = new Index3i(T.Corners[0], T.Corners[1], T.Corners[2]), pb = new Index3i(U.Corners[0], U.Corners[1], U.Corners[2]);
            Triangle3d A = new Triangle3d(T.V0, T.V1, T.V2), B = new Triangle3d(U.V0, U.V1, U.V2);
            // (all non-shared corners of U in T's plane: the coplanar branches of IntersectsBeyondShared without their
            // Orient3d, which are 0; docs/design/CHEAPER_CONSTRUCTIONS.md, section 4.1)
            if (st == 0 && shared > 0)
                return ExactTriangleIntersection.IntersectsBeyondSharedCoplanar(pa, ref A, pb, ref B) == false;
            return ExactTriangleIntersection.IntersectsBeyondShared(pa, ref A, pb, ref B) == false;
        }

        // the corners of U not shared with T against T's plane (T and U share at most two corners): 1 all strictly on
        // one side; 0 all in the plane; -1 otherwise (on both sides, or some in the plane and some not)
        [MethodImpl(JitHints.Optimize)]
        int one_side_except_shared(InputTriangle T, InputTriangle U)
        {
            Vector3d t0 = input_vertex(T, 0), t1 = input_vertex(T, 1), t2 = input_vertex(T, 2);
            int side = 0;
            bool zero = false;
            for (int k = 0; k < 3; ++k) {
                int c = U.Corners[k];
                if (c == T.Corners[0] || c == T.Corners[1] || c == T.Corners[2])
                    continue;
                int o = ExactPredicates.Orient3d(t0, t1, t2, input_vertex(U, k));
                if (o == 0) {
                    if (side != 0)
                        return -1;
                    zero = true;
                } else {
                    if (zero || (side != 0 && o != side))
                        return -1;
                    side = o;
                }
            }
            return zero ? 0 : (side != 0) ? 1 : -1;
        }

        [MethodImpl(JitHints.Optimize)]
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


        [MethodImpl(JitHints.Optimize)]
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
            // the proper case (no vertex in the other plane): decided by the signs alone, without the points
            // (docs/design/TRITRI_PREFILTER.md); other cases, and pairs that meet, take the construction below
            bool proper = sT[0] != 0 && sT[1] != 0 && sT[2] != 0 && sU[0] != 0 && sU[1] != 0 && sU[2] != 0;
            bool apart = proper && apart_on_the_line(t0, t1, t2, sT, u0, u1, u2, sU);
            if (apart) {
                // Stryker disable once all: without the early return the construction below decides the same (slower)
                if (checkPrefilter == false)
                    return;
                // (counted in check mode only: these pairs are most of the candidates, a shared counter would cost)
                System.Threading.Interlocked.Increment(ref PrefilterRejected);
            }

            List<int> pT = plane_points(T, sT, 4 * ui), pU = plane_points(U, sU, 4 * ti);
            var d = new Direction(T.NY * U.NZ - T.NZ * U.NY, T.NZ * U.NX - T.NX * U.NZ, T.NX * U.NY - T.NY * U.NX);
            int loT, hiT, loU, hiU;
            min_max(pT, d, out loT, out hiT);
            min_max(pU, d, out loU, out hiU);
            int lo = (compare_along(d, loT, loU) >= 0) ? loT : loU;
            int hi = (compare_along(d, hiT, hiU) <= 0) ? hiT : hiU;
            int c = compare_along(d, lo, hi);
            if (checkPrefilter && proper)
                System.Threading.Interlocked.Increment(ref PrefilterChecked);
            if (checkPrefilter && proper && apart != (c > 0))
                throw new InvalidOperationException($"ExactMeshArrangement: the prefilter says {(apart ? "apart" : "meeting")}, the construction {(c > 0 ? "apart" : "meeting")} (triangles {ti}, {ui})");
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

        // Devillers and Guigue (2002), the proper case: the triangles t and u each have their vertices strictly on both
        // sides of the other's plane (sT[k] = Orient3d(u0, u1, u2, t_k), sU[k] = Orient3d(t0, t1, t2, u_k), all non-zero).
        // Both triangles meet the line L of the two planes in a segment; they intersect iff the segments overlap. With
        // p1 the vertex of t alone on its side and p2 the one of u (q, r follow in cyclic order), and q2, r2 swapped where
        // p1 is below u's plane, q1, r1 where p2 is below t's plane, the segments overlap iff
        // Orient3d(p1, q1, p2, q2) <= 0 and Orient3d(p1, r1, r2, p2) <= 0. True: they are apart (no common point)
        [MethodImpl(JitHints.Optimize)]
        internal static bool apart_on_the_line(Vector3d t0, Vector3d t1, Vector3d t2, int[] sT, Vector3d u0, Vector3d u1, Vector3d u2, int[] sU)
        {
            int i = (sT[0] != sT[1] && sT[0] != sT[2]) ? 0 : (sT[1] != sT[0] && sT[1] != sT[2]) ? 1 : 2;
            int j = (sU[0] != sU[1] && sU[0] != sU[2]) ? 0 : (sU[1] != sU[0] && sU[1] != sU[2]) ? 1 : 2;
            Vector3d p1 = pick(t0, t1, t2, i), q1 = pick(t0, t1, t2, i + 1), r1 = pick(t0, t1, t2, i + 2);
            Vector3d p2 = pick(u0, u1, u2, j), q2 = pick(u0, u1, u2, j + 1), r2 = pick(u0, u1, u2, j + 2);
            // (a cyclic order keeps the plane's orientation: the sign of p1 to (p2, q2, r2) is sT[i], of p2 to (p1, q1, r1) sU[j])
            if (sT[i] < 0) { Vector3d w = q2; q2 = r2; r2 = w; }
            if (sU[j] < 0) { Vector3d w = q1; q1 = r1; r1 = w; }
            return ExactPredicates.Orient3d(p1, q1, p2, q2) > 0 || ExactPredicates.Orient3d(p1, r1, r2, p2) > 0;
        }

        static Vector3d pick(Vector3d a, Vector3d b, Vector3d c, int k)
        {
            k %= 3;
            return k == 0 ? a : k == 1 ? b : c;
        }

        // the points where the boundary of t meets the plane (key; t is not in the plane)
        [MethodImpl(JitHints.Optimize)]
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

        [MethodImpl(JitHints.Optimize)]
        void min_max(List<int> pts, in Direction d, out int lo, out int hi)
        {
            lo = hi = pts[0];
            for (int k = 1; k < pts.Count; ++k) {
                if (compare_along(d, pts[k], lo) < 0) lo = pts[k];
                if (compare_along(d, pts[k], hi) > 0) hi = pts[k];
            }
        }


        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        bool clip_into(InputTriangle t, int p, int q, int line)
        {
            var cands = new List<int>();
            if (inside(t, p)) cands.Add(p);
            if (inside(t, q)) cands.Add(q);
            Direction d = line_direction(t, PlaneOf(line));
            for (int k = 0; k < 3; ++k) {
                int ci = t.Corners[k], cj = t.Corners[(k + 1) % 3];
                int o1 = Orient(t, p, q, ci), o2 = Orient(t, p, q, cj);
                if (o1 == 0 && between(d, p, q, ci))
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
            min_max(cands, d, out lo, out hi);
            lock (t) {
                if (compare_along(d, lo, hi) == 0)
                    t.AddPoint(lo);
                else
                    t.AddSegment(new Segment(lo, hi, line));
            }
            return true;
        }

        // r (on the line p-q) is in the closed segment p-q
        [MethodImpl(JitHints.Optimize)]
        bool between(in Direction d, int p, int q, int r)
        {
            int a = compare_along(d, p, r), b = compare_along(d, r, q);
            return (a <= 0 && b <= 0) || (a >= 0 && b >= 0);
        }



        // points where the segments of t cross (only happens with three or more meshes, or self-intersections)
        [MethodImpl(JitHints.Optimize)]
        void prepare_triangle(int ti)
        {
            InputTriangle t = Triangles[ti];
            if (t.Degenerate || (t.Segments.Count == 0 && t.Points.Count == 0))
                return;
            prepare_touched(ti, t);
        }

        // (a method of its own: the lambdas' closure is created when the method is entered, here only for the
        // triangles that need it)
        [MethodImpl(JitHints.Optimize)]
        void prepare_touched(int ti, InputTriangle t)
        {
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
            for (int k = 0; k < 3; ++k)
                t.Points.Remove(t.Corners[k]);
        }


        [MethodImpl(JitHints.Optimize)]
        void triangulate(int ti, out List<OutputTriangle> output, out List<long> constraints)
        {
            output = null;
            constraints = null;
            InputTriangle t = Triangles[ti];
            // (untouched triangles are output as they are, by the caller)
            if (t.Degenerate || (t.Points.Count == 0 && t.Segments.Count == 0))
                return;
            triangulate_touched(ti, t, out output, out constraints);
        }

        // (a method of its own, as prepare_touched)
        [MethodImpl(JitHints.Optimize)]
        void triangulate_touched(int ti, InputTriangle t, out List<OutputTriangle> output, out List<long> constraints)
        {
            constraints = null;
            var pts = new HashSet<int>();
            foreach (int p in t.Points)
                pts.Add(Canonical(p));
            for (int k = 0; k < 3; ++k)
                pts.Remove(t.Corners[k]);
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
                Direction d = line_direction(t, PlaneOf(s.Line));
                int sp = Canonical(s.P), sq = Canonical(s.Q);
                var on = new List<int>() { sp, sq };
                candidates.Clear();
                grid.Query(sp, sq, (k) => candidates.Add(ordered[k]));
                candidates.Add(t.Corners[0]); candidates.Add(t.Corners[1]); candidates.Add(t.Corners[2]);
                foreach (int r in candidates) {
                    if (r == sp || r == sq) continue;
                    if (Orient(t, sp, sq, r) == 0 && between(d, sp, sq, r))
                        on.Add(r);
                }
                on.Sort((a, b) => compare_along(d, a, b));
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

            [MethodImpl(JitHints.Optimize)]
            public BoxGrid2(ExactMeshArrangement arr, InputTriangle t, int count)
            {
                this.arr = arr;
                i = (t.Axis + 1) % 3; j = (t.Axis + 2) % 3;
                double[] b = box(t.Corners[0], t.Corners[0]);
                for (int k = 0; k < 3; ++k) {
                    int c = t.Corners[k];
                    double[] cb = box(c, c);
                    b[0] = Math.Min(b[0], cb[0]); b[1] = Math.Min(b[1], cb[1]); b[2] = Math.Max(b[2], cb[2]); b[3] = Math.Max(b[3], cb[3]);
                }
                bounds = b;
                n0 = (count < 16) ? 1 : Math.Min(1024, (int)Math.Ceiling(Math.Sqrt(count)));
            }

            [MethodImpl(JitHints.Optimize)]
            void set_size(int size)
            {
                n = size;
                cx = (bounds[2] - bounds[0]) / n; cy = (bounds[3] - bounds[1]) / n;
                if (!(cx > 0 && cy > 0 && cx < double.MaxValue && cy < double.MaxValue))
                    n = 1;
            }

            // the cells, after all items are added: the grid is made coarser while the boxes would have more than
            // 8 entries per item (64 at least)
            [MethodImpl(JitHints.Optimize)]
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
            [MethodImpl(JitHints.Optimize)]
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

            [MethodImpl(JitHints.Optimize)]
            int cell(double v, double v0, double c)
            {
                double f = Math.Floor((v - v0) / c);
                return !(f > 0) ? 0 : (f >= n - 1) ? n - 1 : (int)f;
            }

            [MethodImpl(JitHints.Optimize)]
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
            [MethodImpl(JitHints.Optimize)]
            public void Add(int p, int q)
            {
                if (cells != null)
                    throw new InvalidOperationException("BoxGrid2: items added after a query");
                boxes.Add(box(p, q));
            }

            /// <summary> f(k) once for every item k whose box overlaps the box of the segment p-q </summary>
            [MethodImpl(JitHints.Optimize)]
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
            [MethodImpl(JitHints.Optimize)]
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

        /// <summary> element i by reference (it must have been Set), for Volatile reads and writes </summary>
        internal ref T Ref(int i) { return ref chunks[i >> Shift][i & Mask]; }

        readonly object growLock = new object();

        /// <summary> set element i (thread-safe for distinct i; Count becomes at least i + 1) </summary>
        [MethodImpl(JitHints.Optimize)]
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

        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
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
    /// misses a pair of touching boxes (for exact computations). Above MedianSplitBelow boxes they are sorted once along
    /// a Morton curve of their centres (as Geogram's MeshFacetsAABB, Lévy; Lauterbach et al. 2009; the coordinates as
    /// ranks where one far box would squeeze the others into a few cells) and every range splits where the highest
    /// differing bit of its codes changes (Karras 2012); smaller trees keep the median split on the longest axis. On
    /// Thingi10K this gives tighter nodes than the median split on the longest axis used before
    /// (docs/perf/EXACT_BOOLEAN_PERFORMANCE.md). The order only steers which boxes share a node: the boxes a query
    /// finds, and the pairs, are the same.
    /// </summary>
    internal class TriangleBoxTree
    {
        readonly List<AxisAlignedBox3d> boxes;
        readonly int[] order;
        const int LeafSize = 8;
        // small trees (eg SafeRounding's tree of the changed triangles, rebuilt often) take the median split on the
        // longest axis per node, which is cheaper to build; larger ones the Morton order
        const int MedianSplitBelow = 4096;
        readonly bool median;
        readonly double[] keys;     // the median split's sort keys while building
        long[] sortedCodes;         // the Morton codes in order (while building)
        // nodes (in the order of construction, the root first): box (min x, y, z, max x, y, z), first index into
        // order and count, children (-1 for a leaf)
        int nodeCount;
        double[] nodeBox;
        int[] nodeStart, nodeSize, nodeLeft, nodeRight;
        // the boxes in the order of `order` (6 doubles each), so that the leaves are scanned in sequence
        readonly double[] leafBox;

        [MethodImpl(JitHints.Optimize)]
        public TriangleBoxTree(List<AxisAlignedBox3d> boxes)
        {
            this.boxes = boxes;
            int n = boxes.Count;
            order = new int[n];
            for (int i = 0; i < n; ++i)
                order[i] = i;
            median = n <= MedianSplitBelow;
            // Stryker disable all: the order only decides which boxes share a node (performance, not results)
            if (median) {
                keys = new double[n];
                permute_for_tests(n);
            } else
                morton_order(n);
            // Stryker restore all
            // (at most 2 n / LeafSize * 2 + 1 nodes: every inner node splits a range of more than LeafSize in halves)
            int cap = 4 * (n / LeafSize) + 3;
            nodeBox = new double[6 * cap];
            nodeStart = new int[cap]; nodeSize = new int[cap]; nodeLeft = new int[cap]; nodeRight = new int[cap];
            if (n > 0)
                build(0, n);
            sortedCodes = null;
            leafBox = new double[6 * n];
            for (int i = 0; i < n; ++i)
                put(leafBox, i, boxes[order[i]]);
        }

        [MethodImpl(JitHints.Optimize)]
        static void put(double[] a, int i, AxisAlignedBox3d b)
        {
            a[6 * i] = b.Min.x; a[6 * i + 1] = b.Min.y; a[6 * i + 2] = b.Min.z;
            a[6 * i + 3] = b.Max.x; a[6 * i + 4] = b.Max.y; a[6 * i + 5] = b.Max.z;
        }

        [MethodImpl(JitHints.Optimize)]
        int build(int start, int count)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            for (int i = start; i < start + count; ++i)
                box.Contain(boxes[order[i]]);
            int node = nodeCount++;
            put(nodeBox, node, box);
            nodeStart[node] = start; nodeSize[node] = count; nodeLeft[node] = -1; nodeRight[node] = -1;
            if (count <= LeafSize)
                return node;
            int half = count / 2;
            // Stryker disable all: the split only decides which boxes share a node (performance, not results)
            if (median) {
                Vector3d d = box.Diagonal;
                int axis = (d.x >= d.y && d.x >= d.z) ? 0 : (d.y >= d.z) ? 1 : 2;
                for (int i = start; i < start + count; ++i)
                    keys[i] = boxes[order[i]].Min[axis] + boxes[order[i]].Max[axis];
                select(start, start + count - 1, start + half);
            }
            else {
                // the boxes are in Morton order: split where the highest bit in which the range's codes differ changes,
                // so that the halves are octree cells and not the two ends of a jump of the curve; equal codes, or a
                // split that leaves too little on a side: the middle
                long a = sortedCodes[start], b = sortedCodes[start + count - 1];
                if (a != b) {
                    long bit = highest_bit(a ^ b);
                    int lo = start, hi = start + count - 1;
                    // (the codes ascend and share the bits above: the first one with the bit set)
                    while (lo < hi) {
                        int mid = (lo + hi) >> 1;
                        if ((sortedCodes[mid] & bit) != 0) hi = mid; else lo = mid + 1;
                    }
                    // Stryker restore all
                    // (not performance only, the safety of the arrays: each side at least LeafSize / 2, so the leaves hold
                    // 4 or more and the nodes fit the capacity, at most n / 2 - 1; without it a crafted input peels single
                    // boxes off and overflows it, BoxTreeMortonTests. And an eighth, so the depth stays below
                    // log(n / 4) / log(8 / 7) + 1, 110 for 1e7 boxes, within the query stack of 256; the depth is also
                    // bounded without it, by the 63 bits of the codes (one highest-bit split each per path) plus the
                    // middle splits of equal codes, so the eighth is balance, and the tests do not see it)
                    int h = lo - start, least = Math.Max(count / 8, LeafSize / 2);
                    if (h >= least && count - h >= least)
                        half = h;
                    // Stryker disable all
                }
            }
            // Stryker restore all
            int left = build(start, half);
            int right = build(start + half, count - half);
            nodeLeft[node] = left;
            nodeRight[node] = right;
            return node;
        }

        // tests: the nodes, their capacity, and the depth of the tree (the root at depth 1)
        internal int NodeCount { get { return nodeCount; } }
        internal int NodeCapacity { get { return nodeStart.Length; } }
        internal int Depth()
        {
            int depth = 0;
            var stack = new Stack<(int node, int depth)>();
            if (nodeCount > 0)
                stack.Push((0, 1));
            while (stack.Count > 0) {
                var (node, d) = stack.Pop();
                depth = Math.Max(depth, d);
                if (nodeLeft[node] >= 0) {
                    stack.Push((nodeLeft[node], d + 1));
                    stack.Push((nodeRight[node], d + 1));
                }
            }
            return depth;
        }

        // tests: the nodes a query visits (the traversal of Query, counted; not on the hot path)
        internal long VisitedNodes(AxisAlignedBox3d query)
        {
            if (nodeCount == 0)
                return 0;
            double x0 = query.Min.x, y0 = query.Min.y, z0 = query.Min.z, x1 = query.Max.x, y1 = query.Max.y, z1 = query.Max.z;
            long visited = 0;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                visited++;
                if (overlaps(nodeBox, node, x0, y0, z0, x1, y1, z1) == false || nodeLeft[node] < 0)
                    continue;
                stack.Push(nodeLeft[node]);
                stack.Push(nodeRight[node]);
            }
            return visited;
        }

        /// <summary> call f for every box that the segment p-q intersects, with all boxes expanded by pad </summary>
        [MethodImpl(JitHints.Optimize)]
        public void QuerySegment(Vector3d p, Vector3d q, double pad, Action<int> f)
        {
            if (nodeCount == 0)
                return;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (segment_hits(nodeBox, node, p, q, pad) == false)
                    continue;
                if (nodeLeft[node] < 0) {
                    for (int i = nodeStart[node]; i < nodeStart[node] + nodeSize[node]; ++i)
                        if (segment_hits(leafBox, i, p, q, pad))
                            f(order[i]);
                } else {
                    stack.Push(nodeLeft[node]);
                    stack.Push(nodeRight[node]);
                }
            }
        }

        // slab test of segment p-q against the box b[6 i ..] expanded by pad (conservative: pad covers the rounding)
        [MethodImpl(JitHints.Optimize)]
        static bool segment_hits(double[] b, int i, Vector3d p, Vector3d q, double pad)
        {
            double t0 = 0, t1 = 1;
            for (int k = 0; k < 3; ++k) {
                double lo = b[6 * i + k] - pad, hi = b[6 * i + 3 + k] + pad;
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

        // Stryker disable all: an ordering heuristic; any permutation gives the same queries and pairs (performance only,
        // measured in docs/perf/EXACT_BOOLEAN_PERFORMANCE.md; BoxTreeOrderTests check the results under random orders)
        // sort order by the Morton code of the box centres: their coordinates over the box of all centres, 21 bits per
        // axis; if that squeezes the boxes into few cells (one far box), the coordinates as ranks instead: per axis the
        // centres in sorted order (as Array.Sort orders doubles: NaN first; equal values get equal ranks), the rank
        // scaled to 21 bits, so a far box only takes the last rank. Ties of the code: the box index
        [MethodImpl(JitHints.Optimize)]
        void morton_order(int n)
        {
            // the coordinates over the box of all centres first (cheap); if one far box squeezes the others into a few
            // cells (fewer than n / 4 distinct codes), the ranks
            var codes = new long[n];
            var all = AxisAlignedBox3d.Empty;
            for (int i = 0; i < n; ++i)
                all.Contain(boxes[i].Center);
            Vector3d lo = all.Min, ext = all.Max - all.Min;
            const double cells = 2097151;   // 2^21 - 1
            double sx = ext.x > 0 ? cells / ext.x : 0, sy = ext.y > 0 ? cells / ext.y : 0, sz = ext.z > 0 ? cells / ext.z : 0;
            for (int i = 0; i < n; ++i) {
                Vector3d c = boxes[i].Center;
                codes[i] = spread(quantize((c.x - lo.x) * sx)) << 2 | spread(quantize((c.y - lo.y) * sy)) << 1 | spread(quantize((c.z - lo.z) * sz));
            }
            sort_codes(codes, n);
            int distinct = 1;
            for (int k = 1; k < n; ++k)
                if (codes[k] != codes[k - 1]) distinct++;
            if (4 * distinct < n) {
                for (int i = 0; i < n; ++i)
                    order[i] = i;
                var rx = ranks(n, 0);
                var ry = ranks(n, 1);
                var rz = ranks(n, 2);
                for (int i = 0; i < n; ++i)
                    codes[i] = spread(rx[i]) << 2 | spread(ry[i]) << 1 | spread(rz[i]);
                sort_codes(codes, n);
            }
            sortedCodes = codes;
            permute_for_tests(n);
        }

        static long highest_bit(long x)
        {
            long b = 1;
            while ((x >>= 1) != 0) b <<= 1;
            return b;
        }

        // sort order (and codes) by code; equal codes by box index, the same order on every runtime (only the
        // results' order could depend on it, across net48 and net10, not the results; the runs are short)
        void sort_codes(long[] codes, int n)
        {
            Array.Sort(codes, order);
            for (int s0 = 0, s1; s0 < n; s0 = s1) {
                for (s1 = s0 + 1; s1 < n && codes[s1] == codes[s0]; ++s1) { }
                if (s1 - s0 > 1)
                    Array.Sort(order, s0, s1 - s0);
            }
        }

        // a coordinate scaled to the grid; not finite (NaN, an infinite extent): the low end
        static uint quantize(double t)
        {
            return (t >= 0) ? (t < 2097151 ? (uint)t : 2097151u) : 0u;
        }

        // tests (PermutationSeedForTests): a random order, to show that the results do not depend on it
        void permute_for_tests(int n)
        {
            int seed = PermutationSeedForTests;
            if (seed == 0)
                return;
            var rand = new Random(seed);
            for (int i = n - 1; i > 0; --i) {
                int k = rand.Next(i + 1);
                int t = order[i]; order[i] = order[k]; order[k] = t;
            }
            PermutedTrees++;
        }

        // per box, the rank of its centre's coordinate on the axis among all centres, scaled to 0 .. 2^21 - 1
        uint[] ranks(int n, int axis)
        {
            var c = new double[n];
            var idx = new int[n];
            for (int i = 0; i < n; ++i) {
                c[i] = boxes[i].Center[axis];
                idx[i] = i;
            }
            // (the order among equal values does not matter: they get one rank)
            Array.Sort(c, idx);
            var rank = new uint[n];
            int first = 0;
            for (int k = 0; k < n; ++k) {
                if (k > 0 && c[k].CompareTo(c[k - 1]) != 0)
                    first = k;
                rank[idx[k]] = (uint)((long)first * 2097151 / Math.Max(1, n - 1));
            }
            return rank;
        }

        // tests only: if not 0, the boxes are put in a random order of this seed after sorting (process-wide: the trees
        // are built on worker threads; tests that set it run alone, see BoxTreeMortonTests)
        internal static volatile int PermutationSeedForTests;
        // tests: trees built in a random order (PermutationSeedForTests), to show the hook ran
        internal static long PermutedTrees;

        // the 21 bits of v at every third bit position
        static long spread(uint v)
        {
            ulong x = v & 0x1fffff;
            x = (x | x << 32) & 0x1f00000000ffffUL;
            x = (x | x << 16) & 0x1f0000ff0000ffUL;
            x = (x | x << 8) & 0x100f00f00f00f00fUL;
            x = (x | x << 4) & 0x10c30c30c30c30c3UL;
            x = (x | x << 2) & 0x1249249249249249UL;
            return (long)x;
        }
        // Stryker restore all

        // partial sort of order[lo..hi] by keys so that position k holds its element and the halves are split
        // there (Hoare quickselect)
        [MethodImpl(JitHints.Optimize)]
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

        // the closed boxes b[6 i ..] and the query (min x, y, z, max x, y, z) overlap
        [MethodImpl(JitHints.Optimize)]
        static bool overlaps(double[] b, int i, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            int o = 6 * i;
            return !(b[o + 3] < x0 || b[o] > x1 || b[o + 4] < y0 || b[o + 1] > y1 || b[o + 5] < z0 || b[o + 2] > z1);
        }

        [ThreadStatic] static int[] queryStack;

        // the closed boxes of nodes a and b overlap
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool nodes_overlap(int a, int b)
        {
            int o = 6 * b;
            return overlaps(nodeBox, a, nodeBox[o], nodeBox[o + 1], nodeBox[o + 2], nodeBox[o + 3], nodeBox[o + 4], nodeBox[o + 5]);
        }

        /// <summary>
        /// The work items of SelfPairs: node pairs (a, a) for the pairs within node a, (a, b) for the pairs between
        /// nodes a and b. Together they give every pair of different overlapping boxes exactly once. Split breadth
        /// first until there are at least minTasks (fewer for small trees); the list is the same for the same tree.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public List<Index2i> SelfPairTasks(int minTasks)
        {
            var tasks = new List<Index2i>();
            if (nodeCount == 0)
                return tasks;
            tasks.Add(new Index2i(0, 0));
            for (bool split = true; split && tasks.Count < minTasks; ) {
                split = false;
                var next = new List<Index2i>(3 * tasks.Count);
                foreach (Index2i t in tasks) {
                    int a = t.a, b = t.b;
                    if (a == b) {
                        if (nodeLeft[a] < 0) { next.Add(t); continue; }
                        int l = nodeLeft[a], r = nodeRight[a];
                        next.Add(new Index2i(l, l)); next.Add(new Index2i(l, r)); next.Add(new Index2i(r, r));
                        split = true;
                    } else if (nodes_overlap(a, b)) {
                        if (nodeLeft[a] < 0 && nodeLeft[b] < 0) { next.Add(t); continue; }
                        if (split_first(a, b)) { next.Add(new Index2i(nodeLeft[a], b)); next.Add(new Index2i(nodeRight[a], b)); }
                        else { next.Add(new Index2i(a, nodeLeft[b])); next.Add(new Index2i(a, nodeRight[b])); }
                        split = true;
                    }
                }
                tasks = next;
            }
            return tasks;
        }

        // of the two nodes (not both leaves), split a (else b): the larger one that is not a leaf
        bool split_first(int a, int b)
        {
            return nodeLeft[a] >= 0 && (nodeLeft[b] < 0 || nodeSize[a] >= nodeSize[b]);
        }

        /// <summary>
        /// the pairs (i, j) of different boxes that overlap (closed, as Query), of the work item t of SelfPairTasks,
        /// appended to result as i, j; in an order that depends only on the tree
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public void SelfPairs(Index2i t, List<Index2i> result)
        {
            SelfPairs(t, (i, j) => { result.Add(new Index2i(i, j)); return true; });
        }

        /// <summary>
        /// SelfPairs, handing each pair (i, j) to visit instead of collecting it (no list of all overlapping pairs of a
        /// work item); the order is the same. visit returns false to stop the work item.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public void SelfPairs(Index2i t, Func<int, int, bool> visit)
        {
            var stack = new List<Index2i> { t };
            while (stack.Count > 0) {
                Index2i top = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                int a = top.a, b = top.b;
                if (a == b) {
                    if (nodeLeft[a] < 0) {
                        for (int i = nodeStart[a], e = nodeStart[a] + nodeSize[a]; i < e; ++i)
                            for (int j = i + 1; j < e; ++j)
                                if (leaves_overlap(i, j) && visit(order[i], order[j]) == false)
                                    return;
                    } else {
                        int l = nodeLeft[a], r = nodeRight[a];
                        stack.Add(new Index2i(r, r)); stack.Add(new Index2i(l, r)); stack.Add(new Index2i(l, l));
                    }
                    continue;
                }
                if (nodes_overlap(a, b) == false)
                    continue;
                if (nodeLeft[a] < 0 && nodeLeft[b] < 0) {
                    for (int i = nodeStart[a], ei = nodeStart[a] + nodeSize[a]; i < ei; ++i)
                        for (int j = nodeStart[b], ej = nodeStart[b] + nodeSize[b]; j < ej; ++j)
                            if (leaves_overlap(i, j) && visit(order[i], order[j]) == false)
                                return;
                } else if (split_first(a, b)) {
                    stack.Add(new Index2i(nodeRight[a], b)); stack.Add(new Index2i(nodeLeft[a], b));
                } else {
                    stack.Add(new Index2i(a, nodeRight[b])); stack.Add(new Index2i(a, nodeLeft[b]));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool leaves_overlap(int i, int j)
        {
            int o = 6 * j;
            return overlaps(leafBox, i, leafBox[o], leafBox[o + 1], leafBox[o + 2], leafBox[o + 3], leafBox[o + 4], leafBox[o + 5]);
        }

        /// <summary> call f for every box that overlaps the closed query box </summary>
        [MethodImpl(JitHints.Optimize)]
        public void Query(AxisAlignedBox3d query, Action<int> f)
        {
            var result = new List<int>();
            Query(query, result);
            foreach (int k in result)
                f(k);
        }

        /// <summary>
        /// the boxes that overlap the closed query box, appended to result (in the same order as Query calls f);
        /// thread-safe for different result lists
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public void Query(AxisAlignedBox3d query, List<int> result)
        {
            if (nodeCount == 0)
                return;
            double x0 = query.Min.x, y0 = query.Min.y, z0 = query.Min.z, x1 = query.Max.x, y1 = query.Max.y, z1 = query.Max.z;
            // (at most depth + 1 entries; the depth is below log(n / 4) / log(8 / 7) + 1: every split leaves an eighth on a side)
            int[] stack = queryStack ?? (queryStack = new int[256]);
            int top = 0;
            stack[top++] = 0;
            while (top > 0) {
                int node = stack[--top];
                if (overlaps(nodeBox, node, x0, y0, z0, x1, y1, z1) == false)
                    continue;
                if (nodeLeft[node] < 0) {
                    for (int i = nodeStart[node], e = nodeStart[node] + nodeSize[node]; i < e; ++i)
                        if (overlaps(leafBox, i, x0, y0, z0, x1, y1, z1))
                            result.Add(order[i]);
                } else {
                    stack[top++] = nodeLeft[node];
                    stack[top++] = nodeRight[node];
                }
            }
        }
    }
}
