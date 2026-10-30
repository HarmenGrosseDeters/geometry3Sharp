using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace g3
{
    /// <summary>Exact-predicate point-set Delaunay triangulation over the convex hull, without snapping or constraints.</summary>
    /// <remarks>
    /// Coordinates must be finite with each component's absolute value at most 1e70; subnormal values are accepted.
    /// Exact duplicates use their first input index; signed zeros are canonicalized. No Steiner vertices are added.
    /// Uses full scans and exhaustive validation rather than a large-input throughput guarantee.
    /// Independent instances may run in parallel. Inputs must not be mutated during computation.
    /// </remarks>
    public sealed class DelaunayMesh2
    {
        const double CoordinateLimit = 1e70;

        internal static bool IsValidPoint(Vector2d point) =>
            !double.IsNaN(point.x) && !double.IsInfinity(point.x) && Math.Abs(point.x) <= CoordinateLimit &&
            !double.IsNaN(point.y) && !double.IsInfinity(point.y) && Math.Abs(point.y) <= CoordinateLimit;

        internal static void ValidatePoint(Vector2d point, string location)
        {
            if (double.IsNaN(point.x) || double.IsInfinity(point.x) || Math.Abs(point.x) > CoordinateLimit)
                throw new ArgumentException(location + " has an invalid x coordinate.");
            if (double.IsNaN(point.y) || double.IsInfinity(point.y) || Math.Abs(point.y) > CoordinateLimit)
                throw new ArgumentException(location + " has an invalid y coordinate.");
        }

        /// <summary>Returns an owned, read-only triangulation; empty and collinear inputs retain their vertex slots.</summary>
        /// <param name="points">Input coordinates, indexed by stable caller identity.</param>
        /// <param name="cancellationToken">Cancellation checked during snapshotting, insertion, legalization and validation.</param>
        /// <returns>A mesh on Success or Empty; a Failed result has no mesh.</returns>
        /// <exception cref="ArgumentNullException">The input is null.</exception>
        /// <exception cref="ArgumentException">A coordinate is non-finite or outside the supported component range.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested; input and prior results are unchanged.</exception>
        public Triangulation2Result Compute(IReadOnlyList<Vector2d> points,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            cancellationToken.ThrowIfCancellationRequested();
            var vertices = new Vector2d[points.Count];
            var mapping = new int[vertices.Length];
            var unique = new Dictionary<Vector2d, int>();
            var ids = new List<int>();
            for (int i = 0; i < vertices.Length; ++i) {
                cancellationToken.ThrowIfCancellationRequested();
                var point = points[i];
                ValidatePoint(point, "points[" + i + "]");
                point.x = point.x == 0 ? 0.0 : point.x;
                point.y = point.y == 0 ? 0.0 : point.y;
                vertices[i] = point;
                if (!unique.TryGetValue(point, out int first)) {
                    unique.Add(point, first = i);
                    ids.Add(i);
                }
                mapping[i] = first;
            }
            var builder = new Builder(vertices, cancellationToken);
            if (!builder.Compute(ids, out var faces, out var failure))
                return new Triangulation2Result(Triangulation2Status.Failed, failure, null);
            cancellationToken.ThrowIfCancellationRequested();
            var mesh = new TriangulationMesh2(vertices, mapping, faces, cancellationToken);
            if (!mesh.Validate(cancellationToken, out _))
                return new Triangulation2Result(Triangulation2Status.Failed, Triangulation2FailureKind.InvalidTopology, null);
            return new Triangulation2Result(faces.Length == 0 ? Triangulation2Status.Empty : Triangulation2Status.Success,
                Triangulation2FailureKind.None, mesh);
        }

        sealed class Builder
        {
            readonly Vector2d[] vertices;
            readonly CancellationToken token;
            readonly List<Index3i> triangles = new List<Index3i>();

            internal Builder(Vector2d[] vertices, CancellationToken token) { this.vertices = vertices; this.token = token; }
            int Orient(int a, int b, int c) => ExactPredicates.Orient2d(vertices[a], vertices[b], vertices[c]);

            bool Add(int a, int b, int c)
            {
                int sign = Orient(a, b, c);
                if (sign == 0) return false;
                triangles.Add(sign > 0 ? new Index3i(a, b, c) : new Index3i(b, a, c));
                return true;
            }

            bool OnSegment(int a, int b, int p)
            {
                if (Orient(a, b, p) != 0) return false;
                var u = vertices[a]; var v = vertices[b]; var q = vertices[p];
                return Math.Min(u.x, v.x) <= q.x && q.x <= Math.Max(u.x, v.x) &&
                    Math.Min(u.y, v.y) <= q.y && q.y <= Math.Max(u.y, v.y);
            }

            internal bool Compute(List<int> ids, out Index3i[] faces, out Triangulation2FailureKind failure)
            {
                faces = null;
                failure = Triangulation2FailureKind.None;
                if (ids.Count < 3) { faces = new Index3i[0]; return true; }
                int third = -1;
                for (int i = 2; i < ids.Count; ++i) {
                    token.ThrowIfCancellationRequested();
                    if (Orient(ids[0], ids[1], ids[i]) != 0) { third = i; break; }
                }
                if (third < 0) { faces = new Index3i[0]; return true; }
                Add(ids[0], ids[1], ids[third]);
                for (int i = 2; i < ids.Count; ++i) {
                    token.ThrowIfCancellationRequested();
                    if (i == third) continue;
                    if (!Insert(ids[i])) { failure = Triangulation2FailureKind.InvalidTopology; return false; }
                    if (!Legalize()) { failure = Triangulation2FailureKind.IterationLimit; return false; }
                }
                faces = triangles.Select(Canonical).ToArray();
                Array.Sort(faces, (a, b) => {
                    int c = a.a.CompareTo(b.a);
                    if (c == 0) c = a.b.CompareTo(b.b);
                    return c == 0 ? a.c.CompareTo(b.c) : c;
                });
                return true;
            }

            static Index3i Canonical(Index3i t)
            {
                if (t.b < t.a && t.b < t.c) return new Index3i(t.b, t.c, t.a);
                if (t.c < t.a && t.c < t.b) return new Index3i(t.c, t.a, t.b);
                return t;
            }

            bool Insert(int p)
            {
                var edges = TriangulationMesh2.BuildEdges(triangles, token);
                foreach (var pair in edges) {
                    token.ThrowIfCancellationRequested();
                    var uses = pair.Value;
                    if (uses.Count == 0 || uses.Count > 2) return false;
                    var edge = uses[0];
                    if (!OnSegment(edge.A, edge.B, p)) continue;
                    if (uses.Count == 2 && (uses[0].A != uses[1].B || uses[0].B != uses[1].A)) return false;
                    var old = uses.ToArray();
                    foreach (var face in old.OrderByDescending(x => x.Triangle)) triangles.RemoveAt(face.Triangle);
                    foreach (var face in old)
                        if (!Add(face.A, p, face.C) || !Add(p, face.B, face.C)) return false;
                    return true;
                }
                for (int ti = 0; ti < triangles.Count; ++ti) {
                    token.ThrowIfCancellationRequested();
                    var t = triangles[ti];
                    if (Orient(t.a, t.b, p) > 0 && Orient(t.b, t.c, p) > 0 && Orient(t.c, t.a, p) > 0) {
                        triangles.RemoveAt(ti);
                        return Add(t.a, t.b, p) && Add(t.b, t.c, p) && Add(t.c, t.a, p);
                    }
                }
                bool visible = false;
                foreach (var pair in edges) {
                    token.ThrowIfCancellationRequested();
                    var uses = pair.Value;
                    if (uses.Count != 1) continue;
                    var edge = uses[0];
                    if (Orient(edge.A, edge.B, p) < 0) {
                        visible = true;
                        if (!Add(edge.B, edge.A, p)) return false;
                    }
                }
                return visible;
            }

            bool Legalize()
            {
                var seen = new HashSet<string>();
                while (true) {
                    token.ThrowIfCancellationRequested();
                    var edges = TriangulationMesh2.BuildEdges(triangles, token);
                    var state = new StringBuilder();
                    foreach (long key in edges.Keys) state.Append(key).Append(',');
                    if (!seen.Add(state.ToString())) return false;
                    bool changed = false;
                    foreach (var pair in edges) {
                        token.ThrowIfCancellationRequested();
                        var uses = pair.Value;
                        if (uses.Count != 2) continue;
                        var e = uses[0]; var f = uses[1];
                        int a = e.A, b = e.B, c = e.C, d = f.C;
                        if (e.A != f.B || e.B != f.A) return false;
                        if (Orient(c, d, b) <= 0 || Orient(d, c, a) <= 0) continue;
                        int incircle = ExactPredicates.InCircle(vertices[a], vertices[b], vertices[c], vertices[d]);
                        long replacement = TriangulationMesh2.EdgeKey(c, d);
                        if (incircle < 0 || (incircle == 0 && replacement >= pair.Key)) continue;
                        triangles.RemoveAt(Math.Max(e.Triangle, f.Triangle));
                        triangles.RemoveAt(Math.Min(e.Triangle, f.Triangle));
                        if (!Add(c, d, b) || !Add(d, c, a)) return false;
                        changed = true;
                        break;
                    }
                    if (!changed) return true;
                }
            }
        }
    }
}
