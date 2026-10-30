using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace g3
{
    /// <summary>An owned, read-only point-set triangulation. Triangle IDs are local to this result.</summary>
    public sealed class TriangulationMesh2
    {
        internal struct EdgeUse
        {
            internal int Triangle, Opposite, A, B, C;
            internal EdgeUse(int triangle, int opposite, int a, int b, int c)
            { Triangle = triangle; Opposite = opposite; A = a; B = b; C = c; }
        }

        readonly Vector2d[] vertices;
        readonly int[] inputToVertex;
        readonly Index3i[] triangles;
        readonly Index3i[] neighbours;
        readonly ReadOnlyCollection<Vector2d> vertexView;
        readonly ReadOnlyCollection<int> mappingView;
        readonly ReadOnlyCollection<Index3i> triangleView, neighbourView;
        readonly SortedDictionary<long, List<EdgeUse>> edgeUses;

        /// <summary>Owned coordinates in input-index order, including unused duplicate slots.</summary>
        public IReadOnlyList<Vector2d> Vertices => vertexView;
        /// <summary>Maps every input slot to the first index with exactly equal coordinates.</summary>
        public IReadOnlyList<int> InputToVertex => mappingView;
        /// <summary>CCW faces with dense result-local IDs, canonically rotated and sorted by vertex IDs.</summary>
        public IReadOnlyList<Index3i> Triangles => triangleView;
        /// <summary>Neighbours[t][k] is opposite Triangles[t][k]; -1 denotes a hull edge.</summary>
        public IReadOnlyList<Index3i> Neighbours => neighbourView;

        internal static long EdgeKey(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)a << 32) | (uint)b;
        }

        internal static SortedDictionary<long, List<EdgeUse>> BuildEdges(IReadOnlyList<Index3i> faces, CancellationToken token)
        {
            var edges = new SortedDictionary<long, List<EdgeUse>>();
            for (int ti = 0; ti < faces.Count; ++ti) {
                token.ThrowIfCancellationRequested();
                var face = faces[ti];
                for (int k = 0; k < 3; ++k) {
                    int a = face[(k + 1) % 3], b = face[(k + 2) % 3];
                    long key = EdgeKey(a, b);
                    if (!edges.TryGetValue(key, out var uses))
                        edges.Add(key, uses = new List<EdgeUse>(2));
                    uses.Add(new EdgeUse(ti, k, a, b, face[k]));
                }
            }
            return edges;
        }

        internal TriangulationMesh2(Vector2d[] vertices, int[] mapping, Index3i[] faces, CancellationToken token)
        {
            this.vertices = vertices;
            inputToVertex = mapping;
            triangles = faces;
            neighbours = new Index3i[faces.Length];
            for (int i = 0; i < neighbours.Length; ++i) neighbours[i] = new Index3i(-1);
            edgeUses = BuildEdges(faces, token);
            foreach (var pair in edgeUses) {
                token.ThrowIfCancellationRequested();
                if (pair.Value.Count != 2) continue;
                var a = pair.Value[0]; var b = pair.Value[1];
                var na = neighbours[a.Triangle]; na[a.Opposite] = b.Triangle; neighbours[a.Triangle] = na;
                var nb = neighbours[b.Triangle]; nb[b.Opposite] = a.Triangle; neighbours[b.Triangle] = nb;
            }
            vertexView = Array.AsReadOnly(vertices);
            mappingView = Array.AsReadOnly(mapping);
            triangleView = Array.AsReadOnly(triangles);
            neighbourView = Array.AsReadOnly(neighbours);
        }

        /// <summary>Enumerates each undirected edge once in sorted order, lower endpoint first.</summary>
        public IEnumerable<Index2i> Edges()
        {
            foreach (long key in edgeUses.Keys)
                yield return new Index2i((int)(key >> 32), (int)(uint)key);
        }

        /// <summary>Enumerates incident triangle IDs in ascending order; duplicate alias slots have none.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The vertex index is invalid (checked immediately).</exception>
        public IEnumerable<int> IncidentTriangles(int vertexId)
        {
            if (vertexId < 0 || vertexId >= vertices.Length) throw new ArgumentOutOfRangeException(nameof(vertexId));
            return EnumerateIncidentTriangles(vertexId);
        }

        IEnumerable<int> EnumerateIncidentTriangles(int vertexId)
        {
            for (int i = 0; i < triangles.Length; ++i) {
                var t = triangles[i];
                if (t.a == vertexId || t.b == vertexId || t.c == vertexId) yield return i;
            }
        }

        /// <summary>Locates a point in closed triangles, including hull edges; multiple hits select the lowest triangle ID.</summary>
        /// <param name="point">Finite query coordinates with each component's absolute value at most 1e70.</param>
        /// <param name="triangleId">The containing triangle, or -1 when outside or empty.</param>
        /// <exception cref="ArgumentException">A query coordinate is outside the supported range or non-finite.</exception>
        public bool TryLocate(Vector2d point, out int triangleId)
        {
            DelaunayMesh2.ValidatePoint(point, "point");
            for (int i = 0; i < triangles.Length; ++i) {
                var t = triangles[i];
                if (ExactPredicates.Orient2d(vertices[t.a], vertices[t.b], point) >= 0 &&
                    ExactPredicates.Orient2d(vertices[t.b], vertices[t.c], point) >= 0 &&
                    ExactPredicates.Orient2d(vertices[t.c], vertices[t.a], point) >= 0) {
                    triangleId = i;
                    return true;
                }
            }
            triangleId = -1;
            return false;
        }

        static bool OnSegment(Vector2d a, Vector2d b, Vector2d p)
        {
            return ExactPredicates.Orient2d(a, b, p) == 0 &&
                Math.Min(a.x, b.x) <= p.x && p.x <= Math.Max(a.x, b.x) &&
                Math.Min(a.y, b.y) <= p.y && p.y <= Math.Max(a.y, b.y);
        }

        static bool Crosses(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int abC = ExactPredicates.Orient2d(a, b, c), abD = ExactPredicates.Orient2d(a, b, d);
            int cdA = ExactPredicates.Orient2d(c, d, a), cdB = ExactPredicates.Orient2d(c, d, b);
            return (abC * abD < 0 && cdA * cdB < 0) || OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b);
        }

        /// <summary>Checks mapping, exact Delaunay geometry, hull coverage, adjacency, unsplit vertices and crossings.</summary>
        /// <param name="reason">The first failed check, or null if valid.</param>
        /// <remarks>Exhaustive validation includes all point/face and edge/edge pairs.</remarks>
        public bool IsValid(out string reason) => Validate(CancellationToken.None, out reason);

        internal bool Validate(CancellationToken token, out string reason)
        {
            reason = null;
            var representatives = new Dictionary<Vector2d, int>();
            for (int i = 0; i < vertices.Length; ++i) {
                token.ThrowIfCancellationRequested();
                var p = vertices[i];
                if (!DelaunayMesh2.IsValidPoint(p)) return Invalid(out reason, "Invalid coordinate.");
                if (!representatives.TryGetValue(p, out int first)) representatives.Add(p, first = i);
                if (inputToVertex[i] != first) return Invalid(out reason, "Invalid input mapping.");
            }
            var ids = representatives.Values.OrderBy(i => i).ToArray();
            if (triangles.Length == 0) {
                if (ids.Length > 2)
                    for (int i = 2; i < ids.Length; ++i) {
                        token.ThrowIfCancellationRequested();
                        if (ExactPredicates.Orient2d(vertices[ids[0]], vertices[ids[1]], vertices[ids[i]]) != 0)
                            return Invalid(out reason, "Empty mesh has non-collinear vertices.");
                    }
                return true;
            }
            var used = new HashSet<int>();
            for (int i = 0; i < triangles.Length; ++i) {
                token.ThrowIfCancellationRequested();
                var t = triangles[i];
                for (int k = 0; k < 3; ++k) {
                    int id = t[k];
                    if (id < 0 || id >= vertices.Length || inputToVertex[id] != id)
                        return Invalid(out reason, "Invalid triangle vertex.");
                    used.Add(id);
                }
                if (ExactPredicates.Orient2d(vertices[t.a], vertices[t.b], vertices[t.c]) != 1)
                    return Invalid(out reason, "Triangle orientation is not positive.");
                foreach (int id in ids) {
                    token.ThrowIfCancellationRequested();
                    if (id != t.a && id != t.b && id != t.c &&
                        ExactPredicates.InCircle(vertices[t.a], vertices[t.b], vertices[t.c], vertices[id]) > 0)
                        return Invalid(out reason, "Triangle is not Delaunay.");
                }
            }
            if (used.Count != ids.Length) return Invalid(out reason, "Unused vertex.");
            var boundary = new Dictionary<int, int>();
            var incoming = new HashSet<int>();
            foreach (var pair in edgeUses) {
                token.ThrowIfCancellationRequested();
                var uses = pair.Value;
                if (uses.Count > 2) return Invalid(out reason, "Non-manifold edge.");
                var first = uses[0];
                foreach (int id in ids) {
                    token.ThrowIfCancellationRequested();
                    if (id != first.A && id != first.B && OnSegment(vertices[first.A], vertices[first.B], vertices[id]))
                        return Invalid(out reason, "Vertex lies on an unsplit edge.");
                }
                if (uses.Count == 2) {
                    var second = uses[1];
                    if (first.A != second.B || first.B != second.A ||
                        neighbours[first.Triangle][first.Opposite] != second.Triangle ||
                        neighbours[second.Triangle][second.Opposite] != first.Triangle)
                        return Invalid(out reason, "Inconsistent adjacency.");
                } else {
                    if (neighbours[first.Triangle][first.Opposite] != -1 || boundary.ContainsKey(first.A) || !incoming.Add(first.B))
                        return Invalid(out reason, "Invalid boundary.");
                    boundary.Add(first.A, first.B);
                    foreach (int id in ids) {
                        token.ThrowIfCancellationRequested();
                        if (ExactPredicates.Orient2d(vertices[first.A], vertices[first.B], vertices[id]) < 0)
                            return Invalid(out reason, "Boundary does not enclose the input.");
                    }
                }
            }
            if (boundary.Count < 3) return Invalid(out reason, "No closed boundary.");
            int start = boundary.Keys.Min(), current = start;
            for (int i = 0; i < boundary.Count; ++i) {
                token.ThrowIfCancellationRequested();
                if (!boundary.TryGetValue(current, out current) || (current == start && i != boundary.Count - 1))
                    return Invalid(out reason, "Disconnected boundary.");
            }
            if (current != start) return Invalid(out reason, "Open boundary.");
            var edges = edgeUses.Keys.ToArray();
            for (int i = 0; i < edges.Length; ++i) {
                token.ThrowIfCancellationRequested();
                int a = (int)(edges[i] >> 32), b = (int)(uint)edges[i];
                for (int j = i + 1; j < edges.Length; ++j) {
                    token.ThrowIfCancellationRequested();
                    int c = (int)(edges[j] >> 32), d = (int)(uint)edges[j];
                    if (a == c || a == d || b == c || b == d) continue;
                    if (Crosses(vertices[a], vertices[b], vertices[c], vertices[d]))
                        return Invalid(out reason, "Crossing edges.");
                }
            }
            return true;
        }

        static bool Invalid(out string reason, string message) { reason = message; return false; }
    }
}
