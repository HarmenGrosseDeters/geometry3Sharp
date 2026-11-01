// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace g3
{
    /// <summary>
    /// An owned, read-only 2D triangulation of a point set. Vertex ids are input indices: <see cref="Vertices"/>[i] is input
    /// point i, also for an exact duplicate, which is mapped to the first input index with its coordinates by
    /// <see cref="InputToVertex"/> and is in no triangle. Triangle ids are local to this mesh.
    /// </summary>
    public sealed class TriangulationMesh2
    {
        readonly Vector2d[] vertices;
        readonly int[] inputToVertex;
        readonly Index3i[] triangles;
        readonly Index3i[] neighbours;
        readonly int[] vertexTriangle;   // per vertex: one triangle that has it, or -1

        /// <summary> Coordinates in input order, including unused duplicate slots (-0 is stored as +0). </summary>
        public IReadOnlyList<Vector2d> Vertices { get; }
        /// <summary> Maps every input index to the first input index with exactly equal coordinates (itself if none). </summary>
        public IReadOnlyList<int> InputToVertex { get; }
        /// <summary>
        /// Counter-clockwise triangles, each rotated so that its smallest vertex id comes first. Their order is deterministic
        /// (a function of the input alone) but not sorted.
        /// </summary>
        public IReadOnlyList<Index3i> Triangles { get; }
        /// <summary> Neighbours[t][k] is the triangle across the edge opposite Triangles[t][k]; -1 on the hull. </summary>
        public IReadOnlyList<Index3i> Neighbours { get; }

        internal TriangulationMesh2(Vector2d[] vertices, int[] inputToVertex, Index3i[] triangles, Index3i[] neighbours)
        {
            this.vertices = vertices;
            this.inputToVertex = inputToVertex;
            this.triangles = triangles;
            this.neighbours = neighbours;
            vertexTriangle = new int[vertices.Length];
            for (int i = 0; i < vertexTriangle.Length; ++i) vertexTriangle[i] = -1;
            for (int t = triangles.Length - 1; t >= 0; --t)
                for (int k = 0; k < 3; ++k) {
                    int v = triangles[t][k];
                    if (v >= 0 && v < vertexTriangle.Length) vertexTriangle[v] = t;   // (an invalid id is reported by IsValid)
                }
            Vertices = new ReadOnlyCollection<Vector2d>(vertices);
            InputToVertex = new ReadOnlyCollection<int>(inputToVertex);
            Triangles = new ReadOnlyCollection<Index3i>(triangles);
            Neighbours = new ReadOnlyCollection<Index3i>(neighbours);
        }

        /// <summary> Number of triangles. </summary>
        public int TriangleCount => triangles.Length;

        /// <summary> Enumerates each undirected edge once, as (smaller id, larger id), sorted. </summary>
        public IEnumerable<Index2i> Edges()
        {
            var keys = new List<long>(triangles.Length * 3 / 2 + 2);
            for (int t = 0; t < triangles.Length; ++t) {
                for (int k = 0; k < 3; ++k) {
                    int nb = neighbours[t][k];
                    if (nb >= 0 && nb < t) continue;
                    int u = triangles[t][(k + 1) % 3], v = triangles[t][(k + 2) % 3];
                    if (u > v) { int x = u; u = v; v = x; }
                    keys.Add(((long)u << 32) | (uint)v);
                }
            }
            keys.Sort();
            foreach (long key in keys) yield return new Index2i((int)(key >> 32), (int)(key & 0xffffffff));
        }

        /// <summary> The triangles that have the vertex, in ascending order; none for an unused duplicate slot. </summary>
        /// <exception cref="ArgumentOutOfRangeException">The vertex id is not an input index.</exception>
        public IEnumerable<int> IncidentTriangles(int vertexId)
        {
            if (vertexId < 0 || vertexId >= vertices.Length) throw new ArgumentOutOfRangeException(nameof(vertexId));
            return Incident(vertexId);
        }

        List<int> Incident(int v)
        {
            var result = new List<int>();
            int start = vertexTriangle[v];
            if (start < 0) return result;
            // turn clockwise around v (across the edge from v to its successor), then the other way if the hull was reached
            int t = start;
            do {
                result.Add(t);
                int k = triangles[t].a == v ? 0 : triangles[t].b == v ? 1 : 2;
                t = neighbours[t][(k + 2) % 3];
            } while (t >= 0 && t != start);
            if (t < 0) {
                t = start;
                while (true) {
                    int k = triangles[t].a == v ? 0 : triangles[t].b == v ? 1 : 2;
                    t = neighbours[t][(k + 1) % 3];
                    if (t < 0) break;
                    result.Add(t);
                }
            }
            result.Sort();
            return result;
        }

        /// <summary>
        /// Locates a point in the closed triangles: points on an edge or a vertex are found too. Of several triangles that
        /// contain the point, the one with the smallest id is returned. Exact; a walk from a deterministic start.
        /// </summary>
        /// <param name="point">Finite coordinates with absolute values at most 1e70.</param>
        /// <param name="triangleId">The containing triangle, or -1 if the point is outside the hull or the mesh is empty.</param>
        /// <exception cref="ArgumentException">A coordinate is not finite or out of range.</exception>
        public bool TryLocate(Vector2d point, out int triangleId)
        {
            DelaunayMesh2.CheckCoordinate(point.x, point.y, "point");
            triangleId = -1;
            int nt = triangles.Length;
            if (nt == 0) return false;
            // jump: the sampled triangle whose first vertex is closest (doubles decide only where the walk starts)
            int samples = Math.Max(1, (int)Math.Pow(nt, 1.0 / 3.0));
            int t = 0;
            double best = double.MaxValue;
            for (int s = 0; s < samples; ++s) {
                int c = (int)((long)s * nt / samples);
                var q = vertices[triangles[c].a];
                double dx = q.x - point.x, dy = q.y - point.y, d = dx * dx + dy * dy;
                if (d < best) { best = d; t = c; }
            }
            // walk
            long limit = 4L * nt + 16;
            int entry = -1;
            for (long step = 0; step < limit; ++step) {
                int moved = -1;
                for (int j = 0; j < 3; ++j) {
                    int k = (int)((step + j) % 3);
                    int nb = neighbours[t][k];
                    if (nb == entry && nb >= 0) continue;
                    var u = vertices[triangles[t][(k + 1) % 3]];
                    var w = vertices[triangles[t][(k + 2) % 3]];
                    if (ExactPredicates.Orient2d(u, w, point) < 0) { moved = k; break; }
                }
                if (moved < 0) { triangleId = Smallest(t, point); return true; }
                int next = neighbours[t][moved];
                if (next < 0) return false;   // beyond a hull edge: outside the (convex) hull
                entry = t;
                t = next;
            }
            // not reached in a Delaunay triangulation; a scan is still exact
            for (int c = 0; c < nt; ++c) {
                if (Contains(c, point)) { triangleId = Smallest(c, point); return true; }
            }
            return false;
        }

        bool Contains(int t, Vector2d p)
        {
            for (int k = 0; k < 3; ++k)
                if (ExactPredicates.Orient2d(vertices[triangles[t][(k + 1) % 3]], vertices[triangles[t][(k + 2) % 3]], p) < 0) return false;
            return true;
        }

        /// <summary> the smallest triangle id containing p, given triangle t that contains it </summary>
        int Smallest(int t, Vector2d p)
        {
            for (int k = 0; k < 3; ++k) {
                var q = vertices[triangles[t][k]];
                if (q.x == p.x && q.y == p.y) return Incident(triangles[t][k])[0];
            }
            int best = t;
            for (int k = 0; k < 3; ++k) {
                var u = vertices[triangles[t][(k + 1) % 3]];
                var w = vertices[triangles[t][(k + 2) % 3]];
                if (ExactPredicates.Orient2d(u, w, p) == 0) {
                    int nb = neighbours[t][k];
                    if (nb >= 0 && nb < best) best = nb;
                }
            }
            return best;
        }

        /// <summary>
        /// Checks the published mesh in linear time: every triangle counter-clockwise under the exact test, neighbours
        /// symmetric and edge-to-edge, every interior edge locally Delaunay (exact in-circle; with a valid triangulation of
        /// the hull that is the Delaunay property), the boundary one convex cycle winding once, every distinct input point a
        /// vertex and every duplicate slot unused and mapped to an equal point.
        /// </summary>
        /// <param name="reason">The first failed check, or null if the mesh is valid.</param>
        public bool IsValid(out string reason)
        {
            reason = null;
            int nv = vertices.Length, nt = triangles.Length;
            for (int i = 0; i < nv; ++i) {
                int m = inputToVertex[i];
                if (m < 0 || m > i || inputToVertex[m] != m) { reason = "input " + i + " is mapped to " + m; return false; }
                if (vertices[m].x != vertices[i].x || vertices[m].y != vertices[i].y) { reason = "input " + i + " is mapped to a different point"; return false; }
                if (m != i && vertexTriangle[i] >= 0) { reason = "duplicate slot " + i + " is in a triangle"; return false; }
            }
            if (nt == 0) return true;
            int hullEdges = 0, hullStart = -1;
            var hullNext = new Dictionary<int, int>();
            for (int t = 0; t < nt; ++t) {
                var tri = triangles[t];
                if (tri.a < 0 || tri.b < 0 || tri.c < 0 || tri.a >= nv || tri.b >= nv || tri.c >= nv) { reason = "triangle " + t + " has an invalid vertex"; return false; }
                if (inputToVertex[tri.a] != tri.a || inputToVertex[tri.b] != tri.b || inputToVertex[tri.c] != tri.c) { reason = "triangle " + t + " uses a duplicate slot"; return false; }
                if (ExactPredicates.Orient2d(vertices[tri.a], vertices[tri.b], vertices[tri.c]) <= 0) { reason = "triangle " + t + " is not counter-clockwise"; return false; }
                for (int k = 0; k < 3; ++k) {
                    int u = tri[(k + 1) % 3], w = tri[(k + 2) % 3];
                    int nb = neighbours[t][k];
                    if (nb < 0) {
                        if (hullNext.ContainsKey(u)) { reason = "vertex " + u + " starts two hull edges"; return false; }
                        hullNext[u] = w;
                        hullStart = hullStart < 0 || Less(u, hullStart) ? u : hullStart;
                        ++hullEdges;
                        continue;
                    }
                    if (nb >= nt) { reason = "triangle " + t + " has an invalid neighbour"; return false; }
                    var o = triangles[nb];
                    int j = o.a == w ? 0 : o.b == w ? 1 : o.c == w ? 2 : -1;
                    if (j < 0 || o[(j + 1) % 3] != u || neighbours[nb][(j + 2) % 3] != t) { reason = "triangles " + t + " and " + nb + " are not mutual neighbours"; return false; }
                    if (nb < t) continue;
                    int d = o[(j + 2) % 3];
                    if (ExactPredicates.InCircle(vertices[tri.a], vertices[tri.b], vertices[tri.c], vertices[d]) > 0) {
                        reason = "edge " + u + "-" + w + " is not locally Delaunay"; return false;
                    }
                }
            }
            // the boundary: one cycle through all hull edges, turning only one way, winding once
            int cur = hullStart, count = 0, minima = 0;
            do {
                if (!hullNext.TryGetValue(cur, out int nxt) || !hullNext.TryGetValue(nxt, out int after)) { reason = "the hull is not closed"; return false; }
                if (ExactPredicates.Orient2d(vertices[cur], vertices[nxt], vertices[after]) < 0) { reason = "the hull is not convex at " + nxt; return false; }
                if (Less(nxt, cur) && Less(nxt, after)) ++minima;
                cur = nxt;
                if (++count > hullEdges) { reason = "the hull does not close"; return false; }
            } while (cur != hullStart);
            if (count != hullEdges) { reason = "the hull has several cycles"; return false; }
            if (minima != 1) { reason = "the hull winds " + minima + " times"; return false; }
            // every distinct point a vertex. (With positive triangles, mutual neighbours and one convex boundary cycle winding once
            // the mesh is an embedded disk already; an Euler check added nothing, as a hand mutation showed.)
            for (int i = 0; i < nv; ++i) if (inputToVertex[i] == i && vertexTriangle[i] < 0) { reason = "point " + i + " is in no triangle"; return false; }
            return true;
        }

        bool Less(int u, int v) => vertices[u].x < vertices[v].x || (vertices[u].x == vertices[v].x && vertices[u].y < vertices[v].y);
    }
}
