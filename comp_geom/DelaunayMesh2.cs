// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Delaunay triangulation of a 2D point set over its convex hull, with exact predicates and without Steiner points.
    /// </summary>
    /// <remarks>
    /// <para>Vertex ids are input indices. Exact duplicates are mapped to their first input index and are in no triangle;
    /// signed zeros count as equal. Fewer than three distinct points, or all collinear, give an empty mesh.</para>
    /// <para>Cocircular points (ties of the Delaunay property) are resolved by a rule that depends only on the points
    /// and their indices: each Delaunay cell with more than three vertices is triangulated as a fan from its vertex with
    /// the smallest index. The result is therefore the same for any insertion order, and bit-identical across runs,
    /// threads and power-of-two scalings of the input.</para>
    /// <para>The finished mesh is verified in linear time (orientation, adjacency, local Delaunay property, convex hull);
    /// a defect, which is not expected, gives <see cref="Triangulation2Status.Failed"/> instead of an invalid mesh.</para>
    /// <para>Independent instances, and one instance on different threads, may compute concurrently. Expected time
    /// O(n log n).</para>
    /// </remarks>
    public sealed class DelaunayMesh2
    {
        /// <summary> Coordinates with a larger absolute value are rejected: beyond it the exact predicates could overflow. </summary>
        public const double CoordinateLimit = 1e70;

        /// <summary> Triangulates the points. </summary>
        /// <param name="points">The input points; index i is the identity of point i.</param>
        /// <param name="cancellationToken">Checked during the insertion; cancelling throws and leaves no state behind.</param>
        /// <returns>A mesh on Success or Empty; no mesh on Failed.</returns>
        /// <exception cref="ArgumentNullException">The input is null.</exception>
        /// <exception cref="ArgumentException">A coordinate is NaN, infinite, or larger than <see cref="CoordinateLimit"/> in absolute value.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        [MethodImpl(JitHints.Optimize)]
        public Triangulation2Result Compute(IReadOnlyList<Vector2d> points, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            int n = points.Count;
            var x = new double[n];
            var y = new double[n];
            var vertices = new Vector2d[n];
            for (int i = 0; i < n; ++i) {
                var p = points[i];
                CheckCoordinate(p.x, p.y, "points[" + i + "]");
                // +0 for -0, so that equal points have equal bits
                x[i] = p.x == 0 ? 0.0 : p.x;
                y[i] = p.y == 0 ? 0.0 : p.y;
                vertices[i] = new Vector2d(x[i], y[i]);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var order = DelaunayKernel2.InsertionOrder(x, y);
            var xs = new double[n];
            var ys = new double[n];
            for (int r = 0; r < n; ++r) { xs[r] = x[order[r]]; ys[r] = y[order[r]]; }
            var kernel = new DelaunayKernel2(xs, ys, order);
            bool area;
            try {
                area = kernel.Build(cancellationToken);
            } catch (InvalidOperationException e) {
                return Publish(kernel, vertices, false, e.Message);
            }
            return Publish(kernel, vertices, area, null);
        }

        /// <summary>
        /// Verifies and canonicalizes the built kernel and publishes the result: Failed with the coordinate of the defect, Empty
        /// without area, Success otherwise. (Separate from Compute so that a test can hand it a corrupted kernel.)
        /// </summary>
        internal static Triangulation2Result Publish(DelaunayKernel2 kernel, Vector2d[] vertices, bool area, string failure)
        {
            int n = vertices.Length;
            var order = kernel.Input;
            if (failure == null && area && kernel.CanonicalizeTies(out failure) < 0) area = false;
            if (failure != null) {
                int v = kernel.FailureVertex;
                return new Triangulation2Result(Triangulation2Status.Failed, Triangulation2FailureKind.InvalidTopology, null,
                    v >= 0 && v < n ? vertices[order[v]] : (Vector2d?)null, failure);
            }
            var mapping = new int[n];
            if (!area) {
                // no triangle: duplicates are still mapped to their first index
                var first = new Dictionary<Vector2d, int>();
                for (int i = 0; i < n; ++i) {
                    if (!first.TryGetValue(vertices[i], out int f)) first.Add(vertices[i], f = i);
                    mapping[i] = f;
                }
                var empty = new TriangulationMesh2(vertices, mapping, new Index3i[0], new Index3i[0]);
                return new Triangulation2Result(Triangulation2Status.Empty, Triangulation2FailureKind.None, empty);
            }
            for (int r = 0; r < n; ++r) mapping[order[r]] = kernel.Representative[kernel.Vertex[r]];
            Extract(kernel, out var triangles, out var neighbours);
            var mesh = new TriangulationMesh2(vertices, mapping, triangles, neighbours);
            return new Triangulation2Result(Triangulation2Status.Success, Triangulation2FailureKind.None, mesh);
        }

        internal static void CheckCoordinate(double x, double y, string name)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > CoordinateLimit)
                throw new ArgumentException(name + " has an invalid x coordinate (" + x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "): it must be finite and at most 1e70 in absolute value.");
            if (double.IsNaN(y) || double.IsInfinity(y) || Math.Abs(y) > CoordinateLimit)
                throw new ArgumentException(name + " has an invalid y coordinate (" + y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "): it must be finite and at most 1e70 in absolute value.");
        }

        /// <summary>
        /// The real triangles, in the kernel's (deterministic) order, with representative vertex ids, each rotated to start at
        /// its smallest id; neighbours across the edge opposite each vertex.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Extract(DelaunayKernel2 k, out Index3i[] triangles, out Index3i[] neighbours)
        {
            int slots = k.TriangleSlots;
            var rep = k.Representative;
            var V = k.V; var H = k.H;
            int inf = k.Infinite;
            var id = new int[slots];
            var rotation = new byte[slots];
            int count = 0;
            for (int s = 0; s < slots; ++s) {
                int a = V[3 * s], b = V[3 * s + 1], c = V[3 * s + 2];
                if (a == inf || b == inf || c == inf) { id[s] = -1; continue; }
                a = rep[a]; b = rep[b]; c = rep[c];
                rotation[s] = (byte)(a < b && a < c ? 0 : b < c ? 1 : 2);
                id[s] = count++;
            }
            triangles = new Index3i[count];
            neighbours = new Index3i[count];
            for (int s = 0; s < slots; ++s) {
                int i = id[s];
                if (i < 0) continue;
                int r = rotation[s], e0 = 3 * s + r, e1 = 3 * s + (r + 1) % 3, e2 = 3 * s + (r + 2) % 3;
                triangles[i] = new Index3i(rep[V[e0]], rep[V[e1]], rep[V[e2]]);
                // the edge opposite output vertex j starts at output vertex j + 1
                neighbours[i] = new Index3i(id[H[e1] / 3], id[H[e2] / 3], id[H[e0] / 3]);
            }
        }

        /// <summary>
        /// Exact sign of Orient2d(u, w, (a + b + c) / 3), evaluated without dividing by 3 via 3 * Orient2d(u, w, G) =
        /// (w - u) x ((a - u) + (b - u) + (c - u)). Uses a conservative normal-range double filter with exact BigInteger fallback,
        /// so thin, huge, subnormal, and mixed-scale triangles always match the exact rational centroid.
        /// </summary>
        internal static int OrientCentroid2d(Vector2d u, Vector2d w, Vector2d a, Vector2d b, Vector2d c)
        {
            double dx = w.x - u.x, dy = w.y - u.y;
            double ax = a.x - u.x, ay = a.y - u.y;
            double bx = b.x - u.x, by = b.y - u.y;
            double cx = c.x - u.x, cy = c.y - u.y;
            double adx = Math.Abs(dx), ady = Math.Abs(dy);
            double sumAbsX = Math.Abs(ax) + Math.Abs(bx) + Math.Abs(cx);
            double sumAbsY = Math.Abs(ay) + Math.Abs(by) + Math.Abs(cy);
            if (adx < 1e140 && ady < 1e140 && sumAbsX < 1e140 && sumAbsY < 1e140) {
                double sx = ax + bx + cx, sy = ay + by + cy;
                double det = dx * sy - dy * sx;
                double errScale = adx * sumAbsY + ady * sumAbsX;
                if (Math.Abs(det) > errScale * 1e-13 && Math.Abs(det) > 1e-280)
                    return det > 0 ? 1 : -1;
            }
            return OrientCentroid2dExact(u, w, a, b, c);
        }

        static int OrientCentroid2dExact(Vector2d u, Vector2d w, Vector2d a, Vector2d b, Vector2d c)
        {
            double[] vals = { u.x, u.y, w.x, w.y, a.x, a.y, b.x, b.y, c.x, c.y };
            var mant = new BigInteger[10];
            var exp = new int[10];
            int emin = int.MaxValue;
            for (int i = 0; i < 10; ++i) {
                ExactPoint3.Decompose(vals[i], out mant[i], out exp[i]);
                if (!mant[i].IsZero && exp[i] < emin) emin = exp[i];
            }
            if (emin == int.MaxValue) return 0;
            var q = new BigInteger[10];
            for (int i = 0; i < 10; ++i) q[i] = mant[i].IsZero ? BigInteger.Zero : mant[i] << (exp[i] - emin);
            BigInteger ux = q[0], uy = q[1], wx = q[2], wy = q[3];
            BigInteger sx = (q[4] - ux) + (q[6] - ux) + (q[8] - ux);
            BigInteger sy = (q[5] - uy) + (q[7] - uy) + (q[9] - uy);
            return ((wx - ux) * sy - (wy - uy) * sx).Sign;
        }

        /// <summary> Exact check whether (a + b + c) / 3 == v. </summary>
        internal static bool CentroidEqualsVertex(Vector2d v, Vector2d a, Vector2d b, Vector2d c)
        {
            double ax = a.x - v.x, bx = b.x - v.x, cx = c.x - v.x;
            double ay = a.y - v.y, by = b.y - v.y, cy = c.y - v.y;
            double sx = Math.Abs(ax) + Math.Abs(bx) + Math.Abs(cx);
            double sy = Math.Abs(ay) + Math.Abs(by) + Math.Abs(cy);
            if (sx < 1e140 && sy < 1e140) {
                if (Math.Abs(ax + bx + cx) > sx * 1e-12 + 1e-280) return false;
                if (Math.Abs(ay + by + cy) > sy * 1e-12 + 1e-280) return false;
            }
            return Sum3EqualsZeroExact(v.x, a.x, b.x, c.x) && Sum3EqualsZeroExact(v.y, a.y, b.y, c.y);
        }

        static bool Sum3EqualsZeroExact(double v, double a, double b, double c)
        {
            double[] vals = { v, a, b, c };
            var mant = new BigInteger[4];
            var exp = new int[4];
            int emin = int.MaxValue;
            for (int i = 0; i < 4; ++i) {
                ExactPoint3.Decompose(vals[i], out mant[i], out exp[i]);
                if (!mant[i].IsZero && exp[i] < emin) emin = exp[i];
            }
            if (emin == int.MaxValue) return true;
            BigInteger qv = mant[0].IsZero ? BigInteger.Zero : mant[0] << (exp[0] - emin);
            BigInteger qa = mant[1].IsZero ? BigInteger.Zero : mant[1] << (exp[1] - emin);
            BigInteger qb = mant[2].IsZero ? BigInteger.Zero : mant[2] << (exp[2] - emin);
            BigInteger qc = mant[3].IsZero ? BigInteger.Zero : mant[3] << (exp[3] - emin);
            return ((qa - qv) + (qb - qv) + (qc - qv)).IsZero;
        }
    }
}
