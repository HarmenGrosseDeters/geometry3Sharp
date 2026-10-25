// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Incremental 3D Delaunay triangulation of points inside a box (Bowyer-Watson insertion with exact
    /// predicates, ExactPredicates.Orient3d / InSphere). The eight box corners are vertices 0 to 7 and the box is
    /// the convex hull, so there are no infinite cells: a facet on the box has neighbour -1. Points are inserted
    /// strictly inside the box.
    ///
    /// Cells are positively oriented (Orient3d(v0, v1, v2, v3) > 0). Facet i of a cell is the one opposite its
    /// vertex i; FacetVertices(c, i) lists it oriented outwards (counter-clockwise seen from outside the cell).
    /// Cell ids are reused after cells are removed; CellStamp(c) changes whenever id c gets a new cell.
    ///
    /// With cospherical points (eg the box corners) the triangulation is one of the Delaunay triangulations.
    /// The conflict region of a new point is grown, where needed, until it is star-shaped from the point, so
    /// every cell has positive volume (exactly) and the adjacency stays valid also for degenerate input.
    ///
    /// Sources (docs/SOURCES.md): A. Bowyer 1981, D. F. Watson 1981 (insertion by cavities); O. Devillers,
    /// S. Pion, M. Teillaud, "Walking in a triangulation", 2002 (point location by a stochastic walk).
    /// </summary>
    public class Delaunay3
    {
        /// <summary> outward oriented vertex indices (into the cell) of facet i, which is opposite vertex i </summary>
        public static readonly int[,] FacetIndices = { { 1, 2, 3 }, { 0, 3, 2 }, { 0, 1, 3 }, { 0, 2, 1 } };

        public enum InsertResult
        {
            Inserted = 0,
            /// <summary> the point is not strictly inside the box </summary>
            Outside = 1,
            /// <summary> the point coincides with a vertex or is closer than the requested separation </summary>
            TooClose = 2,
            /// <summary> no valid cavity (the point would remove a vertex); the triangulation is unchanged </summary>
            Failed = 3
        }

        public readonly AxisAlignedBox3d Box;
        readonly List<Vector3d> vertices = new List<Vector3d>();

        int[] cv = new int[0];       // 4 vertices per cell
        int[] cn = new int[0];       // 4 neighbours per cell (-1 = on the box)
        int[] stamp = new int[0];
        bool[] alive = new bool[0];
        int[] mark = new int[0];
        int markValue = 0;
        int maxCell = 0, cellCount = 0;
        readonly Stack<int> freeCells = new Stack<int>();
        int lastCell = 0;
        uint rng = 2463534242;

        /// <summary> cells visited by the last point location (for diagnostics) </summary>
        public int LastWalkSteps;

        public Delaunay3(AxisAlignedBox3d box)
        {
            if (!(box.Max.x > box.Min.x && box.Max.y > box.Min.y && box.Max.z > box.Min.z))
                throw new ArgumentException("Delaunay3: box must have positive extent");
            Box = box;
            for (int i = 0; i < 8; ++i)
                vertices.Add(new Vector3d((i & 1) != 0 ? box.Max.x : box.Min.x, (i & 2) != 0 ? box.Max.y : box.Min.y,
                    (i & 4) != 0 ? box.Max.z : box.Min.z));
            // six tetrahedra around the diagonal 0-7
            int[][] tets = { new[] { 0, 1, 3, 7 }, new[] { 0, 3, 2, 7 }, new[] { 0, 2, 6, 7 },
                             new[] { 0, 6, 4, 7 }, new[] { 0, 4, 5, 7 }, new[] { 0, 5, 1, 7 } };
            var faces = new Dictionary<(int, int, int), (int, int)>();
            foreach (int[] t in tets) {
                if (ExactPredicates.Orient3d(vertices[t[0]], vertices[t[1]], vertices[t[2]], vertices[t[3]]) < 0) {
                    int tmp = t[0]; t[0] = t[1]; t[1] = tmp;
                }
                int c = new_cell(t[0], t[1], t[2], t[3]);
                for (int i = 0; i < 4; ++i) {
                    var key = sorted_key(t[FacetIndices[i, 0]], t[FacetIndices[i, 1]], t[FacetIndices[i, 2]]);
                    if (faces.TryGetValue(key, out var other)) {
                        cn[4 * c + i] = other.Item1;
                        cn[4 * other.Item1 + other.Item2] = c;
                        faces.Remove(key);
                    } else
                        faces[key] = (c, i);
                }
            }
        }

        static (int, int, int) sorted_key(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return (a, b, c);
        }

        public int VertexCount { get { return vertices.Count; } }
        public Vector3d GetVertex(int v) { return vertices[v]; }
        /// <summary> number of live cells </summary>
        public int CellCount { get { return cellCount; } }
        /// <summary> cell ids are in [0, MaxCellID) </summary>
        public int MaxCellID { get { return maxCell; } }
        public bool IsCell(int c) { return c >= 0 && c < maxCell && alive[c]; }
        public int CellStamp(int c) { return stamp[c]; }
        public int CellVertex(int c, int i) { return cv[4 * c + i]; }
        public Index4i GetCell(int c) { return new Index4i(cv[4 * c], cv[4 * c + 1], cv[4 * c + 2], cv[4 * c + 3]); }
        /// <summary> cell across facet i (opposite vertex i), -1 on the box </summary>
        public int Neighbour(int c, int i) { return cn[4 * c + i]; }
        /// <summary> index of the facet of c shared with cell n, -1 if none </summary>
        public int FacetTo(int c, int n)
        {
            for (int i = 0; i < 4; ++i)
                if (cn[4 * c + i] == n) return i;
            return -1;
        }
        /// <summary> index of vertex v in cell c, -1 if not a vertex of c </summary>
        public int VertexIndexIn(int c, int v)
        {
            for (int i = 0; i < 4; ++i)
                if (cv[4 * c + i] == v) return i;
            return -1;
        }
        /// <summary> facet i of c, oriented outwards </summary>
        public Index3i FacetVertices(int c, int i)
        {
            return new Index3i(cv[4 * c + FacetIndices[i, 0]], cv[4 * c + FacetIndices[i, 1]], cv[4 * c + FacetIndices[i, 2]]);
        }
        public IEnumerable<int> CellIndices()
        {
            for (int c = 0; c < maxCell; ++c)
                if (alive[c]) yield return c;
        }

        public double CellVolume(int c)
        {
            Vector3d a = vertices[cv[4 * c]], b = vertices[cv[4 * c + 1]], d = vertices[cv[4 * c + 2]], e = vertices[cv[4 * c + 3]];
            return (b - a).Cross(d - a).Dot(e - a) / 6.0;
        }

        /// <summary> circumcenter of cell c (floating point) </summary>
        public Vector3d Circumcenter(int c)
        {
            return Circumcenter(vertices[cv[4 * c]], vertices[cv[4 * c + 1]], vertices[cv[4 * c + 2]], vertices[cv[4 * c + 3]]);
        }

        public static Vector3d Circumcenter(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            Vector3d u = b - a, v = c - a, w = d - a;
            Vector3d vw = v.Cross(w), wu = w.Cross(u), uv = u.Cross(v);
            double den = 2 * u.Dot(vw);
            return a + (u.LengthSquared * vw + v.LengthSquared * wu + w.LengthSquared * uv) / den;
        }


        int new_cell(int a, int b, int c, int d)
        {
            int id;
            if (freeCells.Count > 0) {
                id = freeCells.Pop();
            } else {
                id = maxCell++;
                if (id >= alive.Length) {
                    int n = Math.Max(64, 2 * alive.Length);
                    Array.Resize(ref cv, 4 * n); Array.Resize(ref cn, 4 * n);
                    Array.Resize(ref stamp, n); Array.Resize(ref alive, n); Array.Resize(ref mark, n);
                }
            }
            cv[4 * id] = a; cv[4 * id + 1] = b; cv[4 * id + 2] = c; cv[4 * id + 3] = d;
            cn[4 * id] = cn[4 * id + 1] = cn[4 * id + 2] = cn[4 * id + 3] = -1;
            alive[id] = true;
            stamp[id]++;
            mark[id] = 0;
            cellCount++;
            lastCell = id;
            return id;
        }

        void free_cell(int c)
        {
            alive[c] = false;
            cellCount--;
            freeCells.Push(c);
        }

        uint next_random()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return rng;
        }

        /// <summary>
        /// cell containing p (closed: p may be on its boundary), walking from cell hint; -1 if p is not inside the box
        /// </summary>
        public int Locate(Vector3d p, int hint = -1)
        {
            int c = IsCell(hint) ? hint : lastCell;
            if (!IsCell(c)) {
                for (c = 0; c < maxCell && !alive[c]; ++c) { }
            }
            int prev = -1;
            int maxSteps = 64 + 4 * cellCount;
            for (int step = 0; step < maxSteps; ++step) {
                LastWalkSteps = step + 1;
                int start = (int)(next_random() & 3);
                int next = -2;
                for (int k = 0; k < 4; ++k) {
                    int i = (start + k) & 3;
                    int n = cn[4 * c + i];
                    if (n == prev && n >= 0)
                        continue;
                    Vector3d a = vertices[cv[4 * c + FacetIndices[i, 0]]], b = vertices[cv[4 * c + FacetIndices[i, 1]]],
                        d = vertices[cv[4 * c + FacetIndices[i, 2]]];
                    if (ExactPredicates.Orient3d(a, b, d, p) > 0) {
                        next = n;
                        break;
                    }
                }
                if (next == -2)
                    return c;
                if (next == -1)
                    return -1;
                prev = c;
                c = next;
            }
            // (the walk does not cycle on Delaunay triangulations; this is a safety net)
            for (c = 0; c < maxCell; ++c) {
                if (alive[c] && contains(c, p))
                    return c;
            }
            return -1;
        }

        bool contains(int c, Vector3d p)
        {
            for (int i = 0; i < 4; ++i) {
                Vector3d a = vertices[cv[4 * c + FacetIndices[i, 0]]], b = vertices[cv[4 * c + FacetIndices[i, 1]]],
                    d = vertices[cv[4 * c + FacetIndices[i, 2]]];
                if (ExactPredicates.Orient3d(a, b, d, p) > 0)
                    return false;
            }
            return true;
        }

        /// <summary> InSphere of p and the circumsphere of cell c: +1 inside, 0 on, -1 outside (exact) </summary>
        public int InCircumsphere(int c, Vector3d p)
        {
            return ExactPredicates.InSphere(vertices[cv[4 * c]], vertices[cv[4 * c + 1]], vertices[cv[4 * c + 2]], vertices[cv[4 * c + 3]], p);
        }


        readonly List<int> cavity = new List<int>();
        readonly List<(int cell, int facet)> boundary = new List<(int, int)>();
        readonly Dictionary<long, int> edgeLinks = new Dictionary<long, int>();
        readonly HashSet<int> cavityVerts = new HashSet<int>(), boundaryVerts = new HashSet<int>();

        /// <summary>
        /// Inserts p (located from cell hint). Fails, leaving the triangulation unchanged, if p is not strictly
        /// inside the box, or closer than minSeparation to an existing vertex (or equal to one). On success
        /// vertex is the new vertex id; removed / added (if not null) receive the ids of the removed and new
        /// cells (ids can be in both).
        /// </summary>
        public InsertResult Insert(Vector3d p, int hint, double minSeparation, out int vertex, List<int> removed = null, List<int> added = null)
        {
            vertex = -1;
            if (!(p.x > Box.Min.x && p.x < Box.Max.x && p.y > Box.Min.y && p.y < Box.Max.y && p.z > Box.Min.z && p.z < Box.Max.z))
                return InsertResult.Outside;
            int c0 = Locate(p, hint);
            if (c0 < 0)
                return InsertResult.Outside;
            for (int i = 0; i < 4; ++i)
                if (vertices[cv[4 * c0 + i]] == p) return InsertResult.TooClose;
            // p in the closed cell and not a vertex: strictly inside its circumsphere
            if (InCircumsphere(c0, p) <= 0)
                return InsertResult.TooClose;

            if (++markValue == int.MaxValue) {
                Array.Clear(mark, 0, mark.Length);
                markValue = 1;
            }
            cavity.Clear();
            cavity.Add(c0);
            mark[c0] = markValue;
            for (int k = 0; k < cavity.Count; ++k) {
                int c = cavity[k];
                for (int i = 0; i < 4; ++i) {
                    int n = cn[4 * c + i];
                    if (n >= 0 && mark[n] != markValue && InCircumsphere(n, p) > 0) {
                        mark[n] = markValue;
                        cavity.Add(n);
                    }
                }
            }
            // grow the cavity until p sees every boundary facet strictly from inside (star-shaped); only needed
            // for cospherical configurations, where the strict conflict region can have facets coplanar with p
            while (true) {
                boundary.Clear();
                bool grown = false;
                for (int k = 0; k < cavity.Count; ++k) {
                    int c = cavity[k];
                    for (int i = 0; i < 4; ++i) {
                        int n = cn[4 * c + i];
                        if (n >= 0 && mark[n] == markValue)
                            continue;
                        Vector3d a = vertices[cv[4 * c + FacetIndices[i, 0]]], b = vertices[cv[4 * c + FacetIndices[i, 1]]],
                            d = vertices[cv[4 * c + FacetIndices[i, 2]]];
                        if (ExactPredicates.Orient3d(a, b, d, p) >= 0) {
                            if (n < 0)
                                return InsertResult.Outside;
                            mark[n] = markValue;
                            cavity.Add(n);
                            grown = true;
                        } else
                            boundary.Add((c, i));
                    }
                }
                if (!grown)
                    break;
            }
            // every vertex of the cavity must stay (on its boundary)
            cavityVerts.Clear(); boundaryVerts.Clear();
            foreach (int c in cavity)
                for (int i = 0; i < 4; ++i) cavityVerts.Add(cv[4 * c + i]);
            foreach (var bf in boundary)
                for (int j = 0; j < 3; ++j) boundaryVerts.Add(cv[4 * bf.cell + FacetIndices[bf.facet, j]]);
            if (cavityVerts.Count != boundaryVerts.Count)
                return InsertResult.Failed;
            // the nearest vertex of p is a vertex of its conflict region
            double minSqr = minSeparation * minSeparation;
            foreach (int v in boundaryVerts)
                if (vertices[v].DistanceSquared(p) <= minSqr) return InsertResult.TooClose;

            vertex = vertices.Count;
            vertices.Add(p);
            int nb = boundary.Count;
            int[] newCells = new int[nb];
            // new cells before freeing the cavity (their ids must not be reused while it is read)
            for (int k = 0; k < nb; ++k) {
                var (c, i) = boundary[k];
                int a = cv[4 * c + FacetIndices[i, 0]], b = cv[4 * c + FacetIndices[i, 1]], d = cv[4 * c + FacetIndices[i, 2]];
                // the facet is outward for the cavity, so (a, d, b) faces p
                int nc = new_cell(a, d, b, vertex);
                newCells[k] = nc;
                int n = cn[4 * c + i];
                cn[4 * nc + 3] = n;
                if (n >= 0)
                    cn[4 * n + FacetTo(n, c)] = nc;
            }
            // facets of the new cells through p: opposite vertex j (0..2) is the edge of the other two
            edgeLinks.Clear();
            foreach (int nc in newCells) {
                for (int j = 0; j < 3; ++j) {
                    int u = cv[4 * nc + (j + 1) % 3], w = cv[4 * nc + (j + 2) % 3];
                    long key = u < w ? ((long)u << 32) | (uint)w : ((long)w << 32) | (uint)u;
                    if (edgeLinks.TryGetValue(key, out int other)) {
                        int oc = other >> 2, oj = other & 3;
                        cn[4 * nc + j] = oc;
                        cn[4 * oc + oj] = nc;
                        edgeLinks.Remove(key);
                    } else
                        edgeLinks[key] = (nc << 2) | j;
                }
            }
            foreach (int c in cavity) {
                free_cell(c);
                removed?.Add(c);
            }
            if (added != null)
                added.AddRange(newCells);
            lastCell = newCells[0];
            return InsertResult.Inserted;
        }


        /// <summary>
        /// Checks orientation, adjacency (symmetric, matching facets) and, if checkDelaunay, the empty sphere
        /// property against all vertices (quadratic, for tests). Returns null if valid, otherwise the problem.
        /// </summary>
        public string Validate(bool checkDelaunay)
        {
            int count = 0;
            for (int c = 0; c < maxCell; ++c) {
                if (!alive[c]) continue;
                count++;
                Index4i t = GetCell(c);
                if (ExactPredicates.Orient3d(vertices[t.a], vertices[t.b], vertices[t.c], vertices[t.d]) <= 0)
                    return "cell " + c + " not positively oriented";
                for (int i = 0; i < 4; ++i) {
                    int n = cn[4 * c + i];
                    Index3i f = FacetVertices(c, i);
                    if (n < 0) {
                        // on the box: the facet lies in a box face
                        Vector3d a = vertices[f.a], b = vertices[f.b], d = vertices[f.c];
                        bool onFace = (a.x == b.x && b.x == d.x && (a.x == Box.Min.x || a.x == Box.Max.x))
                            || (a.y == b.y && b.y == d.y && (a.y == Box.Min.y || a.y == Box.Max.y))
                            || (a.z == b.z && b.z == d.z && (a.z == Box.Min.z || a.z == Box.Max.z));
                        if (!onFace) return "cell " + c + " has an open facet inside the box";
                        continue;
                    }
                    if (!alive[n]) return "cell " + c + " has a removed neighbour";
                    int j = FacetTo(n, c);
                    if (j < 0) return "adjacency of " + c + " and " + n + " not symmetric";
                    if (sorted_key(f.a, f.b, f.c) != sorted_key(FacetVertices(n, j).a, FacetVertices(n, j).b, FacetVertices(n, j).c))
                        return "cells " + c + " and " + n + " do not share their facet";
                }
                if (checkDelaunay) {
                    for (int v = 0; v < vertices.Count; ++v)
                        if (v != t.a && v != t.b && v != t.c && v != t.d && InCircumsphere(c, vertices[v]) > 0)
                            return "vertex " + v + " inside the circumsphere of cell " + c;
                }
            }
            if (count != cellCount) return "cell count mismatch";
            return null;
        }
    }
}
