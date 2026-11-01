// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
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
    }
}
