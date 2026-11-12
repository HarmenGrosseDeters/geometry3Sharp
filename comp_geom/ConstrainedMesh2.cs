// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;

namespace g3
{
    /// <summary>
    /// An owned, read-only constrained triangulation of a domain. Vertex ids below <see cref="InputCount"/> are input indices
    /// (an exact duplicate keeps its slot, is mapped to the first index with its coordinates and is in no triangle); vertices
    /// the mesher made (crossings of constraints, refinement) have ids from <see cref="InputCount"/> on (spec Q8).
    /// </summary>
    public sealed class ConstrainedMesh2
    {
        readonly Vector2d[] vertices;
        readonly Index3i[] triangles, neighbours;
        // the full triangulation of the hull behind the mesh, for point location: slots, twins, slot -> triangle id
        readonly int[] fullV, fullH, slotId, leaving;
        readonly Vector2d[] fullP;
        readonly int infinite;

        public IReadOnlyList<Vector2d> Vertices { get; }
        /// <summary> Number of input points; ids at or above it are vertices made by the mesher. </summary>
        public int InputCount { get; }
        /// <summary> Maps every input index to the vertex it became: itself, or the first input index with its coordinates. </summary>
        public IReadOnlyList<int> InputToVertex { get; }
        /// <summary> Counter-clockwise triangles, each rotated to start at its smallest id; deterministic order. </summary>
        public IReadOnlyList<Index3i> Triangles { get; }
        /// <summary> Neighbours[t][k] is across the edge opposite Triangles[t][k]; -1 on the boundary of the domain. </summary>
        public IReadOnlyList<Index3i> Neighbours { get; }
        /// <summary> The region label of each triangle (0 outside all regions). </summary>
        public IReadOnlyList<int> TriangleLabels { get; }
        /// <summary> The pieces of input constraints that are edges of the mesh, each once. </summary>
        public IReadOnlyList<Mesh2Subsegment> Subsegments { get; }
        /// <summary> Triangles below the minimum angle of a quality mesh (spec Q11); empty without one. </summary>
        public IReadOnlyList<int> BadTriangles { get; internal set; } = new int[0];
        /// <summary> Refinement stopped at the Steiner limit: the mesh is valid, its quality is not guaranteed (spec O7, Q11). </summary>
        public bool SteinerLimitReached { get; internal set; }
        /// <summary> Heights per vertex if the input had heights, otherwise null. </summary>
        public IReadOnlyList<double> Heights { get; }

        internal ConstrainedMesh2(Vector2d[] vertices, int inputCount, int[] inputToVertex, Index3i[] triangles, Index3i[] neighbours,
            int[] labels, Mesh2Subsegment[] subsegments, double[] heights, int[] fullV, int[] fullH, int[] slotId, Vector2d[] fullP, int infinite)
        {
            this.vertices = vertices; this.triangles = triangles; this.neighbours = neighbours;
            this.fullV = fullV; this.fullH = fullH; this.slotId = slotId; this.fullP = fullP; this.infinite = infinite;
            leaving = new int[fullP.Length];
            for (int i = 0; i < leaving.Length; ++i) leaving[i] = -1;
            for (int e = 0; e < fullV.Length; ++e) if (fullV[e] != infinite) leaving[fullV[e]] = e;
            InputCount = inputCount;
            Vertices = new ReadOnlyCollection<Vector2d>(vertices);
            InputToVertex = new ReadOnlyCollection<int>(inputToVertex);
            Triangles = new ReadOnlyCollection<Index3i>(triangles);
            Neighbours = new ReadOnlyCollection<Index3i>(neighbours);
            TriangleLabels = new ReadOnlyCollection<int>(labels);
            Subsegments = new ReadOnlyCollection<Mesh2Subsegment>(subsegments);
            Heights = heights == null ? null : new ReadOnlyCollection<double>(heights);
        }

        public int TriangleCount => triangles.Length;

        /// <summary>
        /// The mesh as a 2.5D <see cref="DMesh3"/>: vertex i is (x, y, z) with z = <see cref="Heights"/>[i], or z = 0 when the input had
        /// no heights (a flat mesh in the plane z = 0, so a mesh without heights needs no special case). Vertex and triangle ids are the
        /// same as here: vertex i of the result is <see cref="Vertices"/>[i] and triangle t is <see cref="Triangles"/>[t], so
        /// <see cref="InputToVertex"/>, <see cref="BadTriangles"/> and the subsegments keep their meaning. A vertex no triangle uses (an
        /// exact duplicate of an input point, an input point outside the domain) stays an isolated vertex of the result; the triangles
        /// keep their counter-clockwise orientation (normals towards +z). When <paramref name="labelsAsGroups"/> is set, triangle groups are
        /// enabled and the group of each triangle is its region label (<see cref="TriangleLabels"/>, 0 outside all regions). No per-vertex
        /// normals, colours or UVs are allocated. Coordinates are copied as they are; nothing is rounded or moved.
        /// </summary>
        /// <exception cref="InvalidOperationException">A triangle could not be added to the <see cref="DMesh3"/> (a non-manifold edge
        /// or a repeated vertex). Not reachable for a mesh this class returns, whose triangles form a valid triangulation; the check
        /// guards the id correspondence.</exception>
        public DMesh3 ToDMesh3(bool labelsAsGroups = true)
        {
            var mesh = new DMesh3(false, false, false, labelsAsGroups);
            var labels = TriangleLabels;
            for (int i = 0; i < vertices.Length; ++i)
                mesh.AppendVertex(new Vector3d(vertices[i].x, vertices[i].y, Heights == null ? 0 : Heights[i]));
            for (int t = 0; t < triangles.Length; ++t) {
                int id = mesh.AppendTriangle(triangles[t], labelsAsGroups ? labels[t] : -1);
                if (id != t) throw new InvalidOperationException("triangle " + t + " could not be added to the DMesh3 (" + id + ")");
            }
            return mesh;
        }

        /// <summary>
        /// The same mesh with fewer input points: the points appended by snap rounding (crossing and restore pixels, SnapRounding2)
        /// become vertices made by the mesher, so ids at or above <paramref name="inputCount"/> are no input points
        /// </summary>
        internal ConstrainedMesh2 WithInputCount(int inputCount)
        {
            var map = new int[inputCount];
            for (int i = 0; i < inputCount; ++i) map[i] = InputToVertex[i];
            // the mesh of the snapped input took the vertices snap rounding made (crossings) as its input points, and keeps every input
            // point; a made vertex in no triangle and no input point's vertex (a crossing inside a hole) is dropped, as the mesher drops
            // its own unused vertices (spec Q8: made vertices are in triangles), and the ids above it move down
            var keep = new bool[vertices.Length];
            for (int v = 0; v < Math.Min(inputCount, vertices.Length); ++v) keep[v] = true;
            foreach (var t in triangles) { keep[t.a] = true; keep[t.b] = true; keep[t.c] = true; }
            foreach (int v in map) if (v >= 0) keep[v] = true;
            var newId = new int[vertices.Length];
            int count = 0;
            for (int v = 0; v < vertices.Length; ++v) newId[v] = keep[v] ? count++ : -1;
            if (count == vertices.Length)
                return new ConstrainedMesh2(vertices, inputCount, map, triangles, neighbours, TriangleLabels.ToArray(), Subsegments.ToArray(),
                    Heights?.ToArray(), fullV, fullH, slotId, fullP, infinite) { BadTriangles = BadTriangles, SteinerLimitReached = SteinerLimitReached };
            var kept = new Vector2d[count];
            double[] keptHeights = Heights == null ? null : new double[count];
            for (int v = 0; v < vertices.Length; ++v) if (keep[v]) { kept[newId[v]] = vertices[v]; if (keptHeights != null) keptHeights[newId[v]] = Heights[v]; }
            for (int i = 0; i < inputCount; ++i) if (map[i] >= 0) map[i] = newId[map[i]];
            var tris = new Index3i[triangles.Length];
            for (int i = 0; i < tris.Length; ++i) tris[i] = new Index3i(newId[triangles[i].a], newId[triangles[i].b], newId[triangles[i].c]);
            var subs = Subsegments.ToArray();
            for (int i = 0; i < subs.Length; ++i) { subs[i].A = newId[subs[i].A]; subs[i].B = newId[subs[i].B]; }
            return new ConstrainedMesh2(kept, inputCount, map, tris, neighbours, TriangleLabels.ToArray(), subs,
                keptHeights, fullV, fullH, slotId, fullP, infinite) { BadTriangles = BadTriangles, SteinerLimitReached = SteinerLimitReached };
        }

        static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;
        static int Prev(int e) => e % 3 == 0 ? e + 2 : e - 1;
        bool Ghost(int s) => fullV[3 * s] == infinite || fullV[3 * s + 1] == infinite || fullV[3 * s + 2] == infinite;

        /// <summary>
        /// Locates a point in the closed triangles of the mesh, including points on edges and vertices. Of several triangles that
        /// contain it, the smallest id is returned. Exact; a walk in the triangulation of the hull behind the mesh.
        /// </summary>
        /// <param name="point">Finite coordinates with absolute values at most 1e70.</param>
        /// <param name="triangleId">The containing triangle, or -1 outside the domain.</param>
        /// <exception cref="ArgumentException">A coordinate is not finite or out of range.</exception>
        public bool TryLocate(Vector2d point, out int triangleId)
        {
            DelaunayMesh2.CheckCoordinate(point.x, point.y, "point");
            triangleId = -1;
            if (triangles.Length == 0) return false;
            int slots = fullV.Length / 3;
            int t = 0;
            while (t < slots && Ghost(t)) ++t;
            if (t == slots) return false;
            long limit = 4L * slots + 16;
            int entry = -1;
            bool found = false;
            for (long step = 0; step < limit; ++step) {
                int moved = -1;
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + (int)((step + j) % 3);
                    if (e == entry) continue;
                    if (ExactPredicates.Orient2d(fullP[fullV[e]], fullP[fullV[Next(e)]], point) < 0) { moved = e; break; }
                }
                if (moved < 0) { found = true; break; }
                entry = fullH[moved];
                t = entry / 3;
                if (Ghost(t)) return false;   // outside the hull
            }
            if (!found) {
                t = -1;
                for (int s = 0; s < slots && t < 0; ++s) if (!Ghost(s) && ContainsSlot(s, point)) t = s;
                if (t < 0) return false;
            }
            // all slots that contain the point: t, a neighbour across an edge through it, or the star of a vertex at it
            int best = int.MaxValue;
            void Consider(int s) { if (!Ghost(s) && slotId[s] >= 0 && slotId[s] < best) best = slotId[s]; }
            Consider(t);
            for (int k = 0; k < 3; ++k) {
                int e = 3 * t + k, v = fullV[e];
                if (fullP[v].x == point.x && fullP[v].y == point.y) {
                    int start = leaving[v], h = start;
                    do { Consider(h / 3); h = fullH[Prev(h)]; } while (h != start);
                }
                if (ExactPredicates.Orient2d(fullP[v], fullP[fullV[Next(e)]], point) == 0) Consider(fullH[e] / 3);
            }
            if (best == int.MaxValue) return false;
            triangleId = best;
            return true;
        }

        bool ContainsSlot(int s, Vector2d p)
        {
            for (int k = 0; k < 3; ++k)
                if (ExactPredicates.Orient2d(fullP[fullV[3 * s + k]], fullP[fullV[3 * s + (k + 1) % 3]], p) < 0) return false;
            return true;
        }
    }

    /// <summary> Outcome of a constrained triangulation. </summary>
    public sealed class ConstrainedTriangulation2Result
    {
        public Triangulation2Status Status { get; }
        public Triangulation2FailureKind FailureKind { get; }
        /// <summary> On failure, a coordinate where the problem was found. </summary>
        public Vector2d? ProblemCoordinate { get; }
        public string FailureDetail { get; }
        /// <summary> The mesh on Success or Empty, null on Failed. </summary>
        public ConstrainedMesh2 Mesh { get; }
        /// <summary> Inputs that were ignored or adjusted, with the reason (seeds on constraints, conflicting heights, ...). </summary>
        public IReadOnlyList<string> Diagnostics { get; }

        internal ConstrainedTriangulation2Result(Triangulation2Status status, Triangulation2FailureKind kind, ConstrainedMesh2 mesh,
            IReadOnlyList<string> diagnostics, Vector2d? problem = null, string detail = null)
        {
            Status = status; FailureKind = kind; Mesh = mesh; Diagnostics = diagnostics; ProblemCoordinate = problem; FailureDetail = detail;
        }
    }
}
