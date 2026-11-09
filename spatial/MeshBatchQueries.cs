// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Threading;

namespace g3
{
    /// <summary> The nearest hit of a ray: the triangle (DMesh3.InvalidID: no hit) and the ray parameter (double.MaxValue: no hit) </summary>
    public struct RayHit
    {
        public int Triangle;
        public double T;
    }

    /// <summary> Result of a batch call as a whole </summary>
    public enum BatchStatus
    {
        /// <summary> every query was answered </summary>
        Ok,
        /// <summary> a null argument, or a negative option </summary>
        InvalidArguments,
        /// <summary> the tree is not built, or the mesh changed after Build (the single queries throw) </summary>
        MeshModified,
        /// <summary> a result or status array is shorter than the input </summary>
        BufferTooSmall,
        /// <summary> BatchOptions.Cancel was signalled; the result arrays are partly filled </summary>
        Cancelled
    }

    /// <summary> Status of one ray; the single query throws for a ray that is not normalized, a batch must not fail for one ray </summary>
    public enum RayStatus : byte
    {
        Ok,
        /// <summary> the direction is not normalized (as the single query's exception); no hit is reported </summary>
        NotNormalized,
        /// <summary> origin or direction is not finite; no hit is reported </summary>
        NonFinite
    }

    /// <summary> Status of one query point </summary>
    public enum PointStatus : byte
    {
        Ok,
        /// <summary> the point is not finite; no result is reported </summary>
        NonFinite
    }

    /// <summary>
    /// The nearest triangle of a point: the triangle (DMesh3.InvalidID: none strictly closer than MaxDistance), the
    /// closest point on it, its barycentric coordinates and the distance (double.MaxValue if there is none).
    /// </summary>
    public struct NearestPointResult
    {
        public int Triangle;
        public Vector3d Point;
        public Vector3d Bary;
        public double Distance;
    }

    /// <summary> Options of the batch queries; the results do not depend on Parallel or SortForCoherence </summary>
    public class BatchOptions
    {
        /// <summary> run on all cores (gParallel); the result equals the serial one </summary>
        public bool Parallel = true;
        /// <summary> answer the queries in the Morton order of their origin and direction (cache coherence); results stay in input order </summary>
        public bool SortForCoherence = true;
        /// <summary> as the fMaxDist of the single queries: only hits strictly closer than this count </summary>
        public double MaxDistance = double.MaxValue;
        /// <summary> checked before the call and once per block of queries (not during the sort); after Cancelled the results and statuses of the skipped blocks are not written </summary>
        public CancellationToken Cancel = default(CancellationToken);
    }

    /// <summary>
    /// Batch ray and point queries against a DMeshAABBTree3 (docs/design/BATCH_QUERIES.md). Every result equals the
    /// result of the single call for the same tree and query, bit for bit: a batch runs the same single queries (the
    /// tree's FindNearestHitTriangle and FindNearestTriangle, the triangle tests of IntrRay3Triangle3 and
    /// DistPoint3Triangle3) and only reorders independent queries (Morton order, for cache coherence) and runs them in
    /// parallel, writing each result at the index of its query. So the results do not depend on the options, the thread
    /// count or the order of the queries.
    ///
    /// Thread safety: the tree and its mesh are only read; the mesh must not change during a call, and a
    /// DMeshAABBTree3.TriangleFilterF must be thread-safe with Parallel (the default).
    /// A batch does not throw for a bad query (see RayStatus and PointStatus), and a stale or unbuilt tree is
    /// BatchStatus.MeshModified instead of the single call's exception (an unbuilt tree of an empty mesh is not told apart
    /// from a built one: every query has no result). An exception of the user's TriangleFilterF, or a mesh modified during
    /// the call, ends the batch as it ends a single call (several at once from the worker threads as AggregateException)
    /// with the results partly written.
    /// </summary>
    public static class MeshBatchQueries
    {
        // below this many queries sorting and threads cost more than they gain
        const int MinSortCount = 1024;
        const int BlockSize = 1024;

        /// <summary>
        /// For every ray the nearest hit, as tree.FindNearestHitTriangle(ray, options.MaxDistance) (smallest ray parameter,
        /// the lowest id among equal ones; hits behind the origin ignored; front and back faces), with its ray parameter
        /// (that of IntrRay3Triangle3.Intersects, as MeshQueries.TriangleIntersection(...).RayParameter). hits has at least
        /// rays.Length entries; status (optional) the same, and tells the rays the single query would reject
        /// (not normalized) or cannot answer (not finite): those get no hit.
        /// </summary>
        public static BatchStatus IntersectBatch(DMeshAABBTree3 tree, Ray3d[] rays, RayHit[] hits, RayStatus[] status = null, BatchOptions options = null)
        {
            if (tree == null || rays == null || hits == null)
                return BatchStatus.InvalidArguments;
            if (hits.Length < rays.Length || (status != null && status.Length < rays.Length))
                return BatchStatus.BufferTooSmall;
            return RunRays(tree, rays, status, options, (ray, i, maxDist) => {
                int tid = tree.FindNearestHitTriangle(ray, maxDist);
                RayHit h;
                h.Triangle = tid; h.T = double.MaxValue;
                if (tid != DMesh3.InvalidID)
                    h.T = HitParameter(tree.Mesh, tid, ref ray);
                hits[i] = h;
            }, i => { hits[i] = new RayHit() { Triangle = DMesh3.InvalidID, T = double.MaxValue }; });
        }

        /// <summary>
        /// For every ray whether it hits the mesh closer than options.MaxDistance: hit[i] is
        /// tree.FindNearestHitTriangle(ray, MaxDistance) != DMesh3.InvalidID. (It runs the nearest-hit query: an
        /// early-out traversal needs the tree's internals, which this class does not use.)
        /// </summary>
        public static BatchStatus AnyHitBatch(DMeshAABBTree3 tree, Ray3d[] rays, bool[] hit, RayStatus[] status = null, BatchOptions options = null)
        {
            if (tree == null || rays == null || hit == null)
                return BatchStatus.InvalidArguments;
            if (hit.Length < rays.Length || (status != null && status.Length < rays.Length))
                return BatchStatus.BufferTooSmall;
            return RunRays(tree, rays, status, options,
                (ray, i, maxDist) => { hit[i] = tree.FindNearestHitTriangle(ray, maxDist) != DMesh3.InvalidID; },
                i => { hit[i] = false; });
        }

        /// <summary>
        /// For every point the nearest triangle, as tree.FindNearestTriangle(p, out dSqr, options.MaxDistance), the
        /// closest point on it and its barycentric coordinates (DistPoint3Triangle3.DistanceSqr on that triangle, as
        /// MeshQueries.TriangleDistance) and the distance Math.Sqrt(dSqr) of the tree query. A point with no triangle
        /// strictly closer than MaxDistance (or any, for an empty mesh) gets Triangle = DMesh3.InvalidID,
        /// Distance = double.MaxValue and a zero Point and Bary.
        /// </summary>
        public static BatchStatus NearestPointBatch(DMeshAABBTree3 tree, Vector3d[] points, NearestPointResult[] results, PointStatus[] status = null, BatchOptions options = null)
        {
            if (tree == null || points == null || results == null)
                return BatchStatus.InvalidArguments;
            if (results.Length < points.Length || (status != null && status.Length < points.Length))
                return BatchStatus.BufferTooSmall;
            BatchStatus bs = Check(tree, ref options);
            if (bs != BatchStatus.Ok)
                return bs;
            DMesh3 mesh = tree.Mesh;
            double maxDist = options.MaxDistance;
            int n = points.Length;
            var noResult = new NearestPointResult() { Triangle = DMesh3.InvalidID, Distance = double.MaxValue };
            Action<int> one = i => {
                Vector3d p = points[i];
                if (Finite(p) == false) {
                    results[i] = noResult;
                    if (status != null) status[i] = PointStatus.NonFinite;
                    return;
                }
                if (status != null) status[i] = PointStatus.Ok;
                if (mesh.TriangleCount == 0) {
                    results[i] = noResult;
                    return;
                }
                double dSqr;
                int tid = tree.FindNearestTriangle(p, out dSqr, maxDist);
                if (tid == DMesh3.InvalidID) {
                    results[i] = noResult;
                    return;
                }
                Triangle3d tri = new Triangle3d();
                mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
                NearestPointResult r = new NearestPointResult() { Triangle = tid, Distance = Math.Sqrt(dSqr) };
                DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out r.Point, out r.Bary);
                results[i] = r;
            };
            return Execute(n, options, () => PointKeys(points), one);
        }


        // ---- shared implementation ----

        // every component finite (Vector3d.IsFinite sums the components, which overflows for finite 1e308 values)
        static bool Finite(Vector3d v)
        {
            return (v.x - v.x) == 0 && (v.y - v.y) == 0 && (v.z - v.z) == 0;
        }

        delegate void RayQuery(Ray3d ray, int index, double maxDist);

        static BatchStatus RunRays(DMeshAABBTree3 tree, Ray3d[] rays, RayStatus[] status, BatchOptions options, RayQuery query, Action<int> noHit)
        {
            BatchStatus bs = Check(tree, ref options);
            if (bs != BatchStatus.Ok)
                return bs;
            double maxDist = options.MaxDistance;
            bool empty = tree.Mesh.TriangleCount == 0;      // (nothing to hit; an unbuilt tree of an empty mesh is not told apart from a built one)
            Action<int> one = i => {
                Ray3d ray = rays[i];
                RayStatus rs = RayStatus.Ok;
                if (Finite(ray.Origin) == false || Finite(ray.Direction) == false)
                    rs = RayStatus.NonFinite;
                else if (ray.Direction.IsNormalized == false)
                    rs = RayStatus.NotNormalized;
                if (status != null) status[i] = rs;
                if (rs != RayStatus.Ok || empty) {
                    noHit(i);
                    return;
                }
                query(ray, i, maxDist);
            };
            return Execute(rays.Length, options, () => RayKeys(rays), one);
        }

        static BatchStatus Check(DMeshAABBTree3 tree, ref BatchOptions options)
        {
            if (options == null)
                options = new BatchOptions();
            if (options.MaxDistance < 0 || double.IsNaN(options.MaxDistance))
                return BatchStatus.InvalidArguments;
            if (tree.Mesh == null)
                return BatchStatus.InvalidArguments;
            if (tree.IsValid == false)
                return BatchStatus.MeshModified;
            return BatchStatus.Ok;
        }

        // Runs one(i) for i in [0, n) in the order of the sort keys (rays: the Morton key of origin and direction; else
        // the points' keys), in parallel by blocks of that order. The queries are independent and write only their own
        // result, so the order and the thread count do not change a result.
        static BatchStatus Execute(int n, BatchOptions options, Func<ulong[]> makeKeys, Action<int> one)
        {
            if (options.Cancel.IsCancellationRequested)
                return BatchStatus.Cancelled;
            int[] order = null;
            if (options.SortForCoherence && n >= MinSortCount)
                order = SortedOrder(n, makeKeys);
            int blocks = (int)(((long)n + BlockSize - 1) / BlockSize);
            int cancelled = 0;
            CancellationToken cancel = options.Cancel;
            Action<int, int> block = (b, unused) => {
                if (cancel.IsCancellationRequested) {
                    Volatile.Write(ref cancelled, 1);
                    return;
                }
                int end = (int)Math.Min((long)n, ((long)b + 1) * BlockSize);
                for (int i = b * BlockSize; i < end; ++i)
                    one(order != null ? order[i] : i);
            };
            if (options.Parallel && blocks > 1)
                gParallel.ForRange(blocks, () => 0, (b, local) => block(b, local));
            else
                for (int b = 0; b < blocks; ++b)
                    block(b, 0);
            return Volatile.Read(ref cancelled) != 0 ? BatchStatus.Cancelled : BatchStatus.Ok;
        }

        // (for the tests: the order the queries are answered in)
        internal static int[] RayOrder(Ray3d[] rays) { return SortedOrder(rays.Length, () => RayKeys(rays)); }
        internal static int[] PointOrder(Vector3d[] points) { return SortedOrder(points.Length, () => PointKeys(points)); }

        static int[] SortedOrder(int n, Func<ulong[]> makeKeys)
        {
            ulong[] keys = makeKeys();
            var order = new int[n];
            for (int i = 0; i < n; ++i)
                order[i] = i;
            Array.Sort(keys, order);
            return order;
        }

        // the ray parameter of the hit of triangle tid: the value of IntrRay3Triangle3.Intersects, which the tree used
        static double HitParameter(DMesh3 mesh, int tid, ref Ray3d ray)
        {
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            double t;
            if (IntrRay3Triangle3.Intersects(ref ray, ref tri.V0, ref tri.V1, ref tri.V2, out t))
                return t;
            return double.MaxValue;     // not reached: the tree found this triangle with the same test
        }


        // ---- Morton keys (only the order of the queries; never a result) ----

        static ulong[] RayKeys(Ray3d[] rays)
        {
            var b = AxisAlignedBox3d.Empty;
            for (int i = 0; i < rays.Length; ++i)
                if (Finite(rays[i].Origin))
                    b.Contain(rays[i].Origin);
            var keys = new ulong[rays.Length];
            for (int i = 0; i < rays.Length; ++i) {
                Ray3d r = rays[i];
                if (Finite(r.Origin) == false || Finite(r.Direction) == false)
                    continue;       // key 0
                ulong mo = Morton3(Q10(Unit(r.Origin.x, b.Min.x, b.Max.x)), Q10(Unit(r.Origin.y, b.Min.y, b.Max.y)), Q10(Unit(r.Origin.z, b.Min.z, b.Max.z)));
                ulong md = Morton3(Q10(Unit(r.Direction.x, -1, 1)), Q10(Unit(r.Direction.y, -1, 1)), Q10(Unit(r.Direction.z, -1, 1)));
                keys[i] = Spread2(mo) | (Spread2(md) << 1);     // 6D Morton key: origin and direction bits alternate
            }
            return keys;
        }

        static ulong[] PointKeys(Vector3d[] points)
        {
            var b = AxisAlignedBox3d.Empty;
            for (int i = 0; i < points.Length; ++i)
                if (Finite(points[i]))
                    b.Contain(points[i]);
            var keys = new ulong[points.Length];
            for (int i = 0; i < points.Length; ++i) {
                Vector3d p = points[i];
                if (Finite(p) == false)
                    continue;
                keys[i] = Morton3(Q10(Unit(p.x, b.Min.x, b.Max.x)), Q10(Unit(p.y, b.Min.y, b.Max.y)), Q10(Unit(p.z, b.Min.z, b.Max.z)));
            }
            return keys;
        }

        static double Unit(double v, double lo, double hi)
        {
            double e = hi - lo;
            if (!(e > 0)) return 0.5;
            double u = (v - lo) / e;
            return double.IsNaN(u) ? 0.5 : (u < 0 ? 0 : (u > 1 ? 1 : u));
        }

        static uint Q10(double u) { return (uint)Math.Min(1023, (int)(u * 1024)); }

        // 30-bit Morton code of three 10-bit coordinates
        static ulong Morton3(uint x, uint y, uint z)
        {
            return Spread3(x) | (Spread3(y) << 1) | (Spread3(z) << 2);
        }

        static ulong Spread3(ulong v)
        {
            v &= 0x3ff;
            v = (v | v << 16) & 0x30000ff;
            v = (v | v << 8) & 0x300f00f;
            v = (v | v << 4) & 0x30c30c3;
            v = (v | v << 2) & 0x9249249;
            return v;
        }

        // the bits of a 32-bit value at the even bit positions of a 64-bit one
        static ulong Spread2(ulong v)
        {
            v &= 0xffffffffUL;
            v = (v | v << 16) & 0x0000ffff0000ffffUL;
            v = (v | v << 8) & 0x00ff00ff00ff00ffUL;
            v = (v | v << 4) & 0x0f0f0f0f0f0f0f0fUL;
            v = (v | v << 2) & 0x3333333333333333UL;
            v = (v | v << 1) & 0x5555555555555555UL;
            return v;
        }
    }
}
