// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;

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
    /// RefinementSupport (off by default) adds what Delaunay refinement needs (docs/design/TET_MESHING.md section 14.6 step 1):
    /// an int label per cell (new cells take the label of the cavity cell whose facet they are built on), one incident cell
    /// per vertex (IncidentCells, IsEdge, IsFacet), and an undo record of the last successful insertion (UndoLastInsert).
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

        // RefinementSupport: per-cell labels, a live incident cell per vertex, the undo record of the last insertion
        bool support;
        int[] label = new int[0];
        int[] vcell = new int[0];
        bool undoValid;
        int undoVertex, undoMaxCell, undoLastCell;
        uint undoRng, insertRng;
        readonly List<int> undoCavity = new List<int>();       // the cavity's cells keep their data while dead (new cells get other ids)
        readonly List<int> undoNewCells = new List<int>();
        readonly List<int> undoFreePopped = new List<int>();   // per new cell: 1 if its id came from the free list

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

        Delaunay3(Delaunay3 other)
        {
            Box = other.Box;
            vertices.AddRange(other.vertices);
            cv = (int[])other.cv.Clone(); cn = (int[])other.cn.Clone(); stamp = (int[])other.stamp.Clone();
            alive = (bool[])other.alive.Clone(); mark = new int[other.mark.Length];
            maxCell = other.maxCell; cellCount = other.cellCount; lastCell = other.lastCell; rng = other.rng;
            foreach (int c in other.freeCells.ToArray().Reverse()) freeCells.Push(c);
            support = other.support;
            label = (int[])other.label.Clone(); vcell = (int[])other.vcell.Clone();
        }

        /// <summary>
        /// an independent copy (cells, ids, neighbours, stamps, the free ids and the walk's random state, so the same
        /// insertions give the same result on both; RefinementSupport and the labels too, but not the undo record)
        /// </summary>
        public Delaunay3 Clone() { return new Delaunay3(this); }

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
            bool fromFree = freeCells.Count > 0;
            if (fromFree) {
                id = freeCells.Pop();
            } else {
                id = maxCell++;
                if (id >= alive.Length) {
                    int n = Math.Max(64, 2 * alive.Length);
                    Array.Resize(ref cv, 4 * n); Array.Resize(ref cn, 4 * n);
                    Array.Resize(ref stamp, n); Array.Resize(ref alive, n); Array.Resize(ref mark, n);
                    if (support) Array.Resize(ref label, n);
                }
            }
            cv[4 * id] = a; cv[4 * id + 1] = b; cv[4 * id + 2] = c; cv[4 * id + 3] = d;
            cn[4 * id] = cn[4 * id + 1] = cn[4 * id + 2] = cn[4 * id + 3] = -1;
            alive[id] = true;
            stamp[id]++;
            mark[id] = 0;
            cellCount++;
            lastCell = id;
            if (support) {
                label[id] = 0;
                set_vcells(id);
                undoFreePopped.Add(fromFree ? 1 : 0);
            }
            return id;
        }

        void set_vcells(int c)
        {
            for (int i = 0; i < 4; ++i) {
                int v = cv[4 * c + i];
                if (v >= vcell.Length) Array.Resize(ref vcell, Math.Max(v + 1, 2 * vcell.Length));
                vcell[v] = c;
            }
        }

        /// <summary>
        /// Turns on the bookkeeping for refinement (see the class comment): labels (all 0 when turned on), an incident cell per
        /// vertex, the undo record. Off by default; turning it on scans the cells once, turning it off drops the bookkeeping.
        /// </summary>
        public bool RefinementSupport
        {
            get { return support; }
            set {
                if (value == support) return;
                support = value;
                undoValid = false;
                if (!support) { label = new int[0]; vcell = new int[0]; return; }
                label = new int[alive.Length];
                vcell = new int[Math.Max(1, vertices.Count)];
                for (int v = 0; v < vcell.Length; ++v) vcell[v] = -1;
                for (int c = 0; c < maxCell; ++c)
                    if (alive[c]) set_vcells(c);
            }
        }

        /// <summary> label of cell c (RefinementSupport); new cells take the label of the cavity cell they replace </summary>
        public int CellLabel(int c) { return label[c]; }
        public void SetCellLabel(int c, int value) { label[c] = value; }

        /// <summary> the cells incident to vertex v, into cells (cleared first; RefinementSupport) </summary>
        public void IncidentCells(int v, List<int> cells)
        {
            cells.Clear();
            int c0 = v < vcell.Length ? vcell[v] : -1;
            if (c0 < 0 || !IsCell(c0)) return;
            if (++markValue == int.MaxValue) { Array.Clear(mark, 0, mark.Length); markValue = 1; }
            cells.Add(c0); mark[c0] = markValue;
            for (int k = 0; k < cells.Count; ++k) {
                int c = cells[k];
                for (int i = 0; i < 4; ++i) {
                    if (cv[4 * c + i] == v) continue;             // the facet opposite v does not contain v
                    int n = cn[4 * c + i];
                    if (n >= 0 && mark[n] != markValue) { mark[n] = markValue; cells.Add(n); }
                }
            }
        }

        bool mark_is_cavity(int c) { return mark[c] == markValue; }

        readonly List<int> starCells = new List<int>();

        /// <summary> true if (a, b) is an edge of the triangulation; cell then contains both (RefinementSupport) </summary>
        public bool IsEdge(int a, int b, out int cell)
        {
            cell = -1;
            IncidentCells(a, starCells);
            foreach (int c in starCells)
                if (VertexIndexIn(c, b) >= 0) { cell = c; return true; }
            return false;
        }

        /// <summary>
        /// true if (a, b, c) is a facet of the triangulation (any orientation); cell and facet then name it in one of its two
        /// cells (RefinementSupport)
        /// </summary>
        public bool IsFacet(int a, int b, int c, out int cell, out int facet)
        {
            cell = facet = -1;
            IncidentCells(a, starCells);
            foreach (int t in starCells) {
                int ib = VertexIndexIn(t, b), ic = VertexIndexIn(t, c);
                if (ib < 0 || ic < 0) continue;
                cell = t; facet = 6 - VertexIndexIn(t, a) - ib - ic;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Undoes the last successful Insert (RefinementSupport; only directly after it, at most once): the cavity's cells come
        /// back with their ids, vertices, neighbours, stamps and labels, the new vertex and cells are removed, and the cell id
        /// allocation and the point location's random state are as before the insertion (so the same insertion again gives the
        /// same cells and ids). Not restored: the stamps of the ids the undone insertion took from the free list stay incremented
        /// (stamps only mark staleness, so a cell id reused later just has a higher stamp). Lists passed to Insert as removed /
        /// added are not changed. False if there is nothing to undo.
        /// </summary>
        public bool UndoLastInsert()
        {
            if (!support || !undoValid) return false;
            undoValid = false;
            // the cavity ids were pushed onto the free list last, in cavity order
            for (int k = undoCavity.Count - 1; k >= 0; --k) freeCells.Pop();
            for (int k = undoNewCells.Count - 1; k >= 0; --k) {
                int nc = undoNewCells[k];
                alive[nc] = false;
                cellCount--;
                if (undoFreePopped[k] != 0) freeCells.Push(nc);
            }
            if (++markValue == int.MaxValue) { Array.Clear(mark, 0, mark.Length); markValue = 1; }
            foreach (int c in undoCavity) mark[c] = markValue;
            // the cavity's cells were only marked dead: their vertices, neighbours, stamps and labels are unchanged (the new
            // cells took ids from before the cavity was freed)
            foreach (int c in undoCavity) { alive[c] = true; cellCount++; }
            // outside neighbours pointed at the new cells: back to the cavity cells. The slot of n is the one opposite n's
            // vertex that is not on the shared facet (n can border several cavity cells)
            for (int k = 0; k < undoCavity.Count; ++k) {
                int c = undoCavity[k];
                for (int i = 0; i < 4; ++i) {
                    int n = cn[4 * c + i];
                    if (n < 0 || mark_is_cavity(n)) continue;
                    int f0 = cv[4 * c + FacetIndices[i, 0]], f1 = cv[4 * c + FacetIndices[i, 1]], f2 = cv[4 * c + FacetIndices[i, 2]];
                    for (int j = 0; j < 4; ++j) {
                        int w = cv[4 * n + j];
                        if (w != f0 && w != f1 && w != f2) { cn[4 * n + j] = c; break; }
                    }
                }
            }
            for (int k = 0; k < undoCavity.Count; ++k) set_vcells(undoCavity[k]);
            maxCell = undoMaxCell; lastCell = undoLastCell; rng = undoRng;
            vertices.RemoveAt(undoVertex);
            if (undoVertex < vcell.Length) vcell[undoVertex] = -1;
            return true;
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
        readonly List<int> boundaryVerts = new List<int>();
        readonly List<int> newCells = new List<int>();
        int[] vmark = new int[0];
        int vmarkValue = 0;

        /// <summary>
        /// Inserts p (located from cell hint). Fails, leaving the triangulation unchanged, if p is not strictly
        /// inside the box, or closer than minSeparation to an existing vertex (or equal to one). On success
        /// vertex is the new vertex id; removed / added (if not null) receive the ids of the removed and new
        /// cells (ids can be in both).
        /// </summary>
        public InsertResult Insert(Vector3d p, int hint, double minSeparation, out int vertex, List<int> removed = null, List<int> added = null)
        {
            vertex = -1;
            undoValid = false;
            insertRng = rng;
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
            if (vmark.Length < vertices.Count + 1)
                Array.Resize(ref vmark, Math.Max(vertices.Count + 1, 2 * vmark.Length));
            if (++vmarkValue == int.MaxValue) {
                Array.Clear(vmark, 0, vmark.Length);
                vmarkValue = 1;
            }
            boundaryVerts.Clear();
            foreach (var bf in boundary)
                for (int j = 0; j < 3; ++j) {
                    int v = cv[4 * bf.cell + FacetIndices[bf.facet, j]];
                    if (vmark[v] != vmarkValue) { vmark[v] = vmarkValue; boundaryVerts.Add(v); }
                }
            foreach (int c in cavity)
                for (int i = 0; i < 4; ++i)
                    if (vmark[cv[4 * c + i]] != vmarkValue) return InsertResult.Failed;
            // the nearest vertex of p is a vertex of its conflict region
            double minSqr = minSeparation * minSeparation;
            foreach (int v in boundaryVerts)
                if (vertices[v].DistanceSquared(p) <= minSqr) return InsertResult.TooClose;

            if (support) {
                undoVertex = vertices.Count; undoMaxCell = maxCell; undoLastCell = lastCell; undoRng = insertRng;
                undoCavity.Clear(); undoNewCells.Clear(); undoFreePopped.Clear();
                undoCavity.AddRange(cavity);
            }
            vertex = vertices.Count;
            vertices.Add(p);
            int nb = boundary.Count;
            newCells.Clear();
            // new cells before freeing the cavity (their ids must not be reused while it is read)
            for (int k = 0; k < nb; ++k) {
                var (c, i) = boundary[k];
                int a = cv[4 * c + FacetIndices[i, 0]], b = cv[4 * c + FacetIndices[i, 1]], d = cv[4 * c + FacetIndices[i, 2]];
                // the facet is outward for the cavity, so (a, d, b) faces p
                int nc = new_cell(a, d, b, vertex);
                newCells.Add(nc);
                if (support) { label[nc] = label[c]; undoNewCells.Add(nc); }
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
            if (support) undoValid = true;
            return InsertResult.Inserted;
        }


        // ---- round insertion (Alpha Wrap 3.0 lever (B), docs/design/ALPHA_WRAP_ROUNDS.md) ----
        //
        // Insert split in three steps so that several points with pairwise disjoint cavities can be inserted in parallel
        // with the same result for every thread count: PlanInsert (read only: the cavity, its boundary and the checks of
        // Insert, against the current triangulation; any thread), AssignInsertIds (serial, in a fixed order: the vertex
        // id and the cell ids), ApplyInsert (writes only the plan's cells and the cavity's outer neighbours; plans whose
        // Reads are pairwise disjoint can be applied in parallel). For one plan, the three steps give the cells of Insert
        // (as a set of tetrahedra with their adjacency); the cell ids can differ.

        /// <summary> (round insertion) a boundary facet of a cavity: the new cell (A, B, C, new vertex) and its outer neighbour </summary>
        internal struct PlannedCell
        {
            public int A, B, C;
            /// <summary> the cell across the facet (-1 on the box) and the index of the facet in it </summary>
            public int Outer, OuterFacet;
        }

        /// <summary> (round insertion) the plan of one insertion; reusable, with its own scratch (one plan per task) </summary>
        internal sealed class InsertPlan
        {
            public Vector3d P;
            public InsertResult Result;
            /// <summary> the cavity (removed cells), in search order </summary>
            public readonly List<int> Cavity = new List<int>();
            /// <summary> every cell the plan read: the located cell, the cavity and the cells adjacent to it </summary>
            public readonly List<int> Reads = new List<int>();
            /// <summary> per boundary facet, from the triangulation the plan was made in </summary>
            public readonly List<PlannedCell> Cells = new List<PlannedCell>();
            /// <summary> set by AssignInsertIds: the new vertex and the ids of the new cells (in the order of Cells) </summary>
            public int Vertex = -1;
            public readonly List<int> NewIds = new List<int>();
            // scratch
            internal readonly CellSet InCavity = new CellSet(), Seen = new CellSet();
            internal readonly List<(int cell, int facet)> Boundary = new List<(int, int)>();
            internal readonly List<int> BoundaryVerts = new List<int>();
            internal readonly CellSet VertSet = new CellSet();
            internal readonly Dictionary<long, int> EdgeLinks = new Dictionary<long, int>();
        }

        /// <summary> small open-addressed set of non-negative ints with O(count) clear </summary>
        internal sealed class CellSet
        {
            int[] slots = new int[64];
            readonly List<int> used = new List<int>();
            public CellSet() { for (int k = 0; k < slots.Length; ++k) slots[k] = -1; }
            public void Clear()
            {
                foreach (int k in used) slots[k] = -1;
                used.Clear();
            }
            /// <summary> true if x was added (not in the set before) </summary>
            public bool Add(int x)
            {
                if (2 * (used.Count + 1) > slots.Length) grow();
                int mask = slots.Length - 1;
                int k = (int)(((uint)x * 2654435761u) >> 7) & mask;
                while (slots[k] >= 0) {
                    if (slots[k] == x) return false;
                    k = (k + 1) & mask;
                }
                slots[k] = x;
                used.Add(k);
                return true;
            }
            public bool Contains(int x)
            {
                int mask = slots.Length - 1;
                int k = (int)(((uint)x * 2654435761u) >> 7) & mask;
                while (slots[k] >= 0) {
                    if (slots[k] == x) return true;
                    k = (k + 1) & mask;
                }
                return false;
            }
            void grow()
            {
                var items = new List<int>(used.Count);
                foreach (int k in used) items.Add(slots[k]);
                slots = new int[2 * slots.Length];
                for (int k = 0; k < slots.Length; ++k) slots[k] = -1;
                used.Clear();
                foreach (int x in items) Add(x);
            }
        }

        // Locate without writing any field: its own walk state, no LastWalkSteps / lastCell
        int locate_readonly(Vector3d p, int hint, ref uint walk)
        {
            int c = hint;
            if (!IsCell(c)) {
                for (c = 0; c < maxCell && !alive[c]; ++c) { }
            }
            int prev = -1;
            int maxSteps = 64 + 4 * cellCount;
            for (int step = 0; step < maxSteps; ++step) {
                walk ^= walk << 13; walk ^= walk >> 17; walk ^= walk << 5;
                int start = (int)(walk & 3);
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
            for (c = 0; c < maxCell; ++c) {
                if (alive[c] && contains(c, p))
                    return c;
            }
            return -1;
        }

        /// <summary>
        /// (round insertion) the plan of Insert(p, hint, minSeparation) without changing the triangulation or any field of
        /// it (safe to call from several threads while nothing writes): plan.Result is what Insert would return, and for
        /// Inserted plan.Cavity / plan.Cells describe the change. The walk that locates p uses walkSeed (nonzero) instead of
        /// the shared random state; the cavity as a set does not depend on it.
        /// </summary>
        internal void PlanInsert(Vector3d p, int hint, double minSeparation, InsertPlan plan, uint walkSeed)
        {
            plan.P = p;
            plan.Cavity.Clear(); plan.Reads.Clear(); plan.Cells.Clear(); plan.NewIds.Clear(); plan.Vertex = -1;
            plan.Result = plan_insert(p, hint, minSeparation, plan, walkSeed == 0 ? 1u : walkSeed);
            if (plan.Result != InsertResult.Inserted)
                plan.Cells.Clear();
        }

        InsertResult plan_insert(Vector3d p, int hint, double minSeparation, InsertPlan plan, uint walk)
        {
            if (!(p.x > Box.Min.x && p.x < Box.Max.x && p.y > Box.Min.y && p.y < Box.Max.y && p.z > Box.Min.z && p.z < Box.Max.z))
                return InsertResult.Outside;
            int c0 = locate_readonly(p, hint, ref walk);
            if (c0 < 0)
                return InsertResult.Outside;
            plan.Reads.Add(c0);
            for (int i = 0; i < 4; ++i)
                if (vertices[cv[4 * c0 + i]] == p) return InsertResult.TooClose;
            if (InCircumsphere(c0, p) <= 0)
                return InsertResult.TooClose;

            var cavity = plan.Cavity;
            var inCavity = plan.InCavity;
            inCavity.Clear();
            cavity.Add(c0);
            inCavity.Add(c0);
            InsertResult result = InsertResult.Inserted;
            for (int k = 0; k < cavity.Count; ++k) {
                int c = cavity[k];
                for (int i = 0; i < 4; ++i) {
                    int n = cn[4 * c + i];
                    if (n >= 0 && !inCavity.Contains(n) && InCircumsphere(n, p) > 0) {
                        inCavity.Add(n);
                        cavity.Add(n);
                    }
                }
            }
            var boundary = plan.Boundary;
            // (the star-shape growth of Insert, kept for parity with it: in an exact Delaunay triangulation it never adds a
            // cell, as a point in the plane of a boundary facet lies inside both circumspheres of the facet's cells or inside
            // neither, both meeting that plane in the facet's circumcircle; docs/design/ALPHA_WRAP_ROUNDS.md section 6)
            while (true) {
                boundary.Clear();
                bool grown = false, outside = false;
                for (int k = 0; k < cavity.Count && !outside; ++k) {
                    int c = cavity[k];
                    for (int i = 0; i < 4; ++i) {
                        int n = cn[4 * c + i];
                        if (n >= 0 && inCavity.Contains(n))
                            continue;
                        Vector3d a = vertices[cv[4 * c + FacetIndices[i, 0]]], b = vertices[cv[4 * c + FacetIndices[i, 1]]],
                            d = vertices[cv[4 * c + FacetIndices[i, 2]]];
                        if (ExactPredicates.Orient3d(a, b, d, p) >= 0) {
                            if (n < 0) { outside = true; break; }
                            inCavity.Add(n);
                            cavity.Add(n);
                            grown = true;
                        } else
                            boundary.Add((c, i));
                    }
                }
                if (outside) { result = InsertResult.Outside; break; }
                if (!grown)
                    break;
            }
            // the read set: the cavity and every cell adjacent to it (all cells the search tested are among them)
            var seen = plan.Seen;
            seen.Clear();
            seen.Add(c0);
            foreach (int c in cavity) {
                if (seen.Add(c)) plan.Reads.Add(c);
                for (int i = 0; i < 4; ++i) {
                    int n = cn[4 * c + i];
                    if (n >= 0 && seen.Add(n)) plan.Reads.Add(n);
                }
            }
            if (result != InsertResult.Inserted)
                return result;
            var vset = plan.VertSet;
            vset.Clear();
            var boundaryVerts = plan.BoundaryVerts;
            boundaryVerts.Clear();
            foreach (var bf in boundary)
                for (int j = 0; j < 3; ++j) {
                    int v = cv[4 * bf.cell + FacetIndices[bf.facet, j]];
                    if (vset.Add(v)) boundaryVerts.Add(v);
                }
            foreach (int c in cavity)
                for (int i = 0; i < 4; ++i)
                    if (!vset.Contains(cv[4 * c + i])) return InsertResult.Failed;
            double minSqr = minSeparation * minSeparation;
            foreach (int v in boundaryVerts)
                if (vertices[v].DistanceSquared(p) <= minSqr) return InsertResult.TooClose;
            foreach (var (c, i) in boundary) {
                int n = cn[4 * c + i];
                plan.Cells.Add(new PlannedCell() {
                    A = cv[4 * c + FacetIndices[i, 0]], B = cv[4 * c + FacetIndices[i, 2]], C = cv[4 * c + FacetIndices[i, 1]],
                    Outer = n, OuterFacet = n >= 0 ? FacetTo(n, c) : -1
                });
            }
            return InsertResult.Inserted;
        }

        /// <summary>
        /// (round insertion, serial, in the commit order) assigns the vertex id and the new cell ids of an Inserted plan:
        /// first the cavity's own ids (in cavity order), then ids from the free stack, then fresh ids; cavity ids left over
        /// (fewer new cells than removed ones) are freed here. Grows the cell arrays; ApplyInsert writes the cells.
        /// </summary>
        internal void AssignInsertIds(InsertPlan plan)
        {
            if (plan.Result != InsertResult.Inserted)
                throw new InvalidOperationException("Delaunay3: plan is not an insertion");
            // (the round insertion keeps no labels, incident cells or undo record: RefinementSupport is for a serial refiner,
            // eg MeshTetrahedralization on a Clone of the wrap's triangulation)
            if (support)
                throw new InvalidOperationException("Delaunay3: round insertion with RefinementSupport on");
            undoValid = false;
            plan.Vertex = vertices.Count;
            vertices.Add(plan.P);
            int nb = plan.Cells.Count, nt = plan.Cavity.Count;
            plan.NewIds.Clear();
            for (int k = 0; k < nb; ++k) {
                int id;
                if (k < nt) {
                    id = plan.Cavity[k];
                } else if (freeCells.Count > 0) {
                    id = freeCells.Pop();
                    cellCount++;
                } else {
                    id = maxCell++;
                    if (id >= alive.Length) {
                        int n = Math.Max(64, 2 * alive.Length);
                        Array.Resize(ref cv, 4 * n); Array.Resize(ref cn, 4 * n);
                        Array.Resize(ref stamp, n); Array.Resize(ref alive, n); Array.Resize(ref mark, n);
                    }
                    cellCount++;
                }
                plan.NewIds.Add(id);
            }
            for (int k = nb; k < nt; ++k)
                free_cell(plan.Cavity[k]);
            lastCell = plan.NewIds[0];
        }

        /// <summary>
        /// (round insertion) writes the new cells of a plan whose ids are assigned: only its new cell ids and the facet
        /// entries of the cavity's outer neighbours, from the plan alone (never from the cavity cells, whose ids are
        /// reused). Plans with pairwise disjoint Reads can be applied in parallel, in any order.
        /// </summary>
        internal void ApplyInsert(InsertPlan plan)
        {
            int nb = plan.Cells.Count, vertex = plan.Vertex;
            for (int k = 0; k < nb; ++k) {
                PlannedCell pc = plan.Cells[k];
                int id = plan.NewIds[k];
                cv[4 * id] = pc.A; cv[4 * id + 1] = pc.B; cv[4 * id + 2] = pc.C; cv[4 * id + 3] = vertex;
                cn[4 * id] = cn[4 * id + 1] = cn[4 * id + 2] = -1;
                cn[4 * id + 3] = pc.Outer;
                if (pc.Outer >= 0)
                    cn[4 * pc.Outer + pc.OuterFacet] = id;
                alive[id] = true;
                stamp[id]++;
                mark[id] = 0;
            }
            var edgeLinks = plan.EdgeLinks;
            edgeLinks.Clear();
            foreach (int nc in plan.NewIds) {
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
