using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Layer B of the exact side labelling of a cutting sheet (described here: the sides of the pieces, the local cells, the labelling of the sheet's marks): the sides of the pieces
    /// of an exact arrangement, joined into local cells, and the integer label of the cells that a sheet's pieces mark.
    /// Purely combinatorial: the only geometric decision is the exact cyclic order of the pieces around an edge
    /// (<see cref="ExactMeshBoolean.radial_order"/>, exact signs of BigInteger dot products), so there is no floating-point
    /// decision and no error bound here.
    ///
    /// A piece k has two side nodes, 2k (its front, the side its normal points to) and 2k + 1 (its back). Around every edge
    /// of the included pieces the pieces are taken in their exact radial order; the wedge between two neighbours p, q
    /// (q after p) is seen from the side of p that looks to increasing angles (the front if p runs Low to High, else the back)
    /// and from the side of q that looks to decreasing angles (the back if q runs Low to High, else the front): these two
    /// nodes are one local cell. With two pieces no
    /// sort is needed (both wedges follow from the directions alone). An edge with a single piece (a free edge) has no wedge:
    /// the edge is reported (<see cref="FreeEdges"/>) and <see cref="Label"/> refuses it, so no cell is derived from it. Within a piece the
    /// sides are continuous, so a piece needs no more than its two nodes.
    ///
    /// A local cell is one connected region of the workpiece's interior that the included pieces bound; two local cells that
    /// lie in one cell (a cavity inside a cell, another shell inside a cell) are not recognized as one: that needs the nesting
    /// of the shells, which is not part of this layer: such a configuration is refused by <see cref="Label"/>, not guessed (a cell without a mark
    /// next to another shell, or two shells whose boxes nest that carry both labels: the box test errs towards "may nest", so it is conservative).
    /// </summary>
    internal sealed class ExactMeshCells
    {
        /// <summary> the local cell of every node (2k front, 2k + 1 back of the k-th piece), numbered in the order of the nodes </summary>
        public int[] Cell;
        public int CellCount;
        /// <summary> the free edges: the pieces (compact index) with an edge that no other included piece has, as the piece and the corner index e (the edge from corner e to corner e + 1), in the order of the pieces </summary>
        public List<KeyValuePair<int, int>> FreeEdges = new List<KeyValuePair<int, int>>();
        /// <summary> the shell (connected set of the pieces flagged as boundary of the workpiece, across shared edges) of each piece flagged so, else -1 </summary>
        public int[] Shell;
        public int ShellCount;
        /// <summary>
        /// the box of each shell (the input doubles of its corners, rounded outwards by <see cref="NestingPad"/>: a cavity must lie inside the box of
        /// its outer shell, so a box that does not hold another shell's box is no outer shell of it)
        /// </summary>
        public AxisAlignedBox3d[] ShellBox;
        /// <summary> the flags that Build was given (per piece): no wall, and a piece of the workpiece's boundary </summary>
        internal bool[] Transparent, Boundary;

        /// <summary> the relative padding of the shell boxes (the input doubles of the corners are the arrangement's points rounded: the true box lies within a few ulps of it); the test errs towards "may nest" </summary>
        internal const double NestingPad = 1e-9;

        static bool NotNumber(AxisAlignedBox3d box)
        {
            return double.IsNaN(box.Min.x) || double.IsNaN(box.Min.y) || double.IsNaN(box.Min.z) || double.IsNaN(box.Max.x) || double.IsNaN(box.Max.y) || double.IsNaN(box.Max.z);
        }

        /// <summary> can shell a lie inside shell b, or b inside a (the box of one holds the box of the other)? </summary>
        internal bool MayNest(int a, int b)
        {
            if (ShellBox == null || NotNumber(ShellBox[a]) || NotNumber(ShellBox[b]))
                return true;        // (no geometry known, or a corner that is not a number: err towards "may nest")
            return ShellBox[a].Contains(ShellBox[b]) || ShellBox[b].Contains(ShellBox[a]);
        }

        static int corner(Index3i t, int e)
        {
            return e == 0 ? t.a : e == 1 ? t.b : t.c;
        }

        static int find(int[] parent, int x)
        {
            while (parent[x] != x) {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        // the smaller root becomes the root: the partition does not depend on the order of the unions
        static void union(int[] parent, int a, int b)
        {
            a = find(parent, a);
            b = find(parent, b);
            if (a == b)
                return;
            if (a < b) parent[b] = a; else parent[a] = b;
        }

        static bool runs(Index3i t, int u, int v)
        {
            return (t.a == u && t.b == v) || (t.b == u && t.c == v) || (t.c == u && t.a == v);
        }

        /// <summary>
        /// The local cells of the pieces. <paramref name="tris"/>: the point ids of each included piece, wound as the piece
        /// (index = compact piece id, in ascending order of the arrangement's pieces); <paramref name="boundary"/> (or null): which
        /// pieces bound the workpiece, for the shells; <paramref name="transparent"/>: the pieces that are no wall (their two sides are one
        /// cell, they only carry the cell's label to the pieces on them: pieces of other tools inside the workpiece).
        /// </summary>
        public static ExactMeshCells Build(ExactMeshArrangement arr, List<Index3i> tris, bool[] boundary, bool[] transparent)
        {
            int k = tris.Count;
            var cells = new ExactMeshCells { Cell = new int[2 * k], Shell = new int[k] };
            var keys = new long[3 * k];
            var items = new int[3 * k];
            int m = 0;
            for (int p = 0; p < k; ++p) {
                Index3i t = tris[p];
                for (int e = 0; e < 3; ++e) {
                    int u = corner(t, e), v = corner(t, (e + 1) % 3);
                    if (u == v)
                        continue;
                    keys[m] = ExactMeshArrangement.EdgeKey(u, v);
                    items[m] = 3 * p + e;
                    ++m;
                }
            }
            Array.Resize(ref keys, m);
            Array.Resize(ref items, m);
            // (ties by the item: the groups come out in ascending order of the pieces)
            Array.Sort(keys, items);
            for (int g0 = 0; g0 < m;) {
                int g1 = g0 + 1;
                while (g1 < m && keys[g1] == keys[g0]) g1++;
                Array.Sort(items, g0, g1 - g0);
                g0 = g1;
            }

            var parent = new int[2 * k];
            for (int i = 0; i < parent.Length; ++i)
                parent[i] = i;
            for (int i = 0; i < k; ++i)
                if (transparent[i])
                    union(parent, 2 * i, 2 * i + 1);
            cells.Transparent = transparent;
            cells.Boundary = boundary;
            var shell = new int[k];
            for (int i = 0; i < k; ++i)
                shell[i] = i;
            var list = new List<int>();
            for (int g0 = 0, g1; g0 < m; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < m && keys[g1] == keys[g0]) g1++;
                int n = g1 - g0;
                int p0 = items[g0] / 3, e0 = items[g0] % 3;
                int u = corner(tris[p0], e0), v = corner(tris[p0], (e0 + 1) % 3);
                if (boundary != null) {
                    int first = -1;
                    for (int g = g0; g < g1; ++g) {
                        int p = items[g] / 3;
                        if (boundary[p] == false)
                            continue;
                        if (first < 0) first = p; else union(shell, first, p);
                    }
                }
                if (n == 1) {
                    // (a free edge has no wedge to join: it is reported, and Label refuses it before it looks at any cell)
                    if (transparent[p0] == false)
                        cells.FreeEdges.Add(new KeyValuePair<int, int>(p0, e0));
                    continue;
                }
                if (n == 2) {
                    int q0 = items[g0 + 1] / 3;
                    if (q0 == p0)
                        continue;       // (a piece with both ends on the edge twice: degenerate)
                    int dp = runs(tris[p0], u, v) ? 1 : -1, dq = runs(tris[q0], u, v) ? 1 : -1;
                    union(parent, after(p0, dp), before(q0, dq));
                    union(parent, after(q0, dq), before(p0, dp));
                    continue;
                }
                list.Clear();
                for (int g = g0; g < g1; ++g) {
                    int p = items[g] / 3;
                    if (list.Count == 0 || list[list.Count - 1] != p)
                        list.Add(p);
                }
                if (list.Count < 3) {
                    // (a piece that has the edge twice: degenerate; the wedges of the distinct pieces as for two)
                    if (list.Count == 2) {
                        int dp = runs(tris[list[0]], u, v) ? 1 : -1, dq = runs(tris[list[1]], u, v) ? 1 : -1;
                        union(parent, after(list[0], dp), before(list[1], dq));
                        union(parent, after(list[1], dq), before(list[0], dp));
                    }
                    continue;
                }
                ExactMeshBoolean.RadialOrder order = ExactMeshBoolean.radial_order(arr, tris, list, u, v, false);
                int c = order.Pieces.Length;
                for (int j = 0; j < c; ++j) {
                    int p = order.Pieces[j], q = order.Pieces[(j + 1) % c];
                    int dp = order.Direction[j], dq = order.Direction[(j + 1) % c];
                    if (dp == 0 || dq == 0)
                        continue;
                    union(parent, after(p, dp), before(q, dq));
                }
            }
            // the cells in the order of their first node
            var id = new int[2 * k];
            for (int i = 0; i < id.Length; ++i)
                id[i] = -1;
            for (int i = 0; i < 2 * k; ++i) {
                int r = find(parent, i);
                if (id[r] < 0)
                    id[r] = cells.CellCount++;
                cells.Cell[i] = id[r];
            }
            var sid = new int[k];
            for (int i = 0; i < k; ++i)
                sid[i] = -1;
            for (int i = 0; i < k; ++i) {
                if (boundary == null || boundary[i] == false) {
                    cells.Shell[i] = -1;
                    continue;
                }
                int r = find(shell, i);
                if (sid[r] < 0)
                    sid[r] = cells.ShellCount++;
                cells.Shell[i] = sid[r];
            }
            cells.ShellBox = new AxisAlignedBox3d[cells.ShellCount];
            for (int sh = 0; sh < cells.ShellCount; ++sh)
                cells.ShellBox[sh] = AxisAlignedBox3d.Empty;
            for (int i = 0; i < k; ++i)
                if (cells.Shell[i] >= 0)
                    for (int e = 0; e < 3; ++e)
                        cells.ShellBox[cells.Shell[i]].Contain(arr.ToInputVector(arr.Exact(corner(tris[i], e))));
            for (int sh = 0; sh < cells.ShellCount; ++sh) {
                AxisAlignedBox3d box = cells.ShellBox[sh];
                double size = Math.Max(box.Extents.x, Math.Max(box.Extents.y, box.Extents.z));
                double big = Math.Max(Math.Max(Math.Abs(box.Min.x), Math.Abs(box.Max.x)), Math.Max(Math.Max(Math.Abs(box.Min.y), Math.Abs(box.Max.y)), Math.Max(Math.Abs(box.Min.z), Math.Abs(box.Max.z))));
                box.Expand(NestingPad * (size + big));
                cells.ShellBox[sh] = box;
            }
            return cells;
        }

        // the node of piece p that looks to increasing angles (d = +1: p runs Low to High, its normal points to increasing angles), and the one that looks to decreasing angles
        static int after(int p, int d) { return d > 0 ? 2 * p : 2 * p + 1; }
        static int before(int p, int d) { return d > 0 ? 2 * p + 1 : 2 * p; }

        /// <summary> the result of <see cref="Label"/> </summary>
        internal sealed class Labelling
        {
            public ExactMeshBoolean.SheetRefusal Reason;
            /// <summary> per node: the label of its local cell (0: outside the tool, 1: inside), 0 for a node that is not on the workpiece's interior </summary>
            public sbyte[] Label;
            /// <summary> RimInsideWorkpiece: the piece and corner of the free edge; SidesInconsistent: the first conflicting cell's pieces with the front mark and the back mark; UncutComponents: a piece of the first unmarked cell (Piece) </summary>
            public int Piece = -1, Piece2 = -1, Corner = -1;
            /// <summary> the arrangement's input triangles that gave the marks (SidesInconsistent), or -1 </summary>
            public int MarkTriangle0 = -1, MarkTriangle1 = -1;
            public int ConflictingCells, UncutCells, FloatingCells;
        }

        /// <summary>
        /// The labels of the local cells from the marks of one sheet. A node with a mark of 0 (<paramref name="mark0"/>, the arrangement's
        /// input triangle that gave it, else -1) is in front of a piece of the sheet, one with a mark of 1 behind it; only nodes on the
        /// workpiece's interior (<paramref name="interior"/>) take marks and get labels. Refused, in this order: a free edge (the
        /// sheet's rim inside the workpiece), a cell with both marks (the sides are inconsistent), a cell without a mark
        /// (an uncut component), unless uncut says which side it is on and the workpiece has a single shell.
        /// </summary>
        public static Labelling Label(ExactMeshCells cells, bool[] interior, int[] mark0, int[] mark1, ExactMeshBoolean.SheetUncutSides uncut)
        {
            var result = new Labelling { Label = new sbyte[cells.Cell.Length] };
            if (cells.FreeEdges.Count > 0) {
                result.Reason = ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece;
                result.Piece = cells.FreeEdges[0].Key;
                result.Corner = cells.FreeEdges[0].Value;
                return result;
            }
            int nc = cells.CellCount;
            var wall = new bool[nc];          // the cell has a piece of the workpiece's boundary on it
            for (int node = 0; node < cells.Cell.Length; ++node)
                if (cells.Boundary[node / 2])
                    wall[cells.Cell[node]] = true;
            var first0 = new int[nc];
            var first1 = new int[nc];
            var seen = new bool[nc];
            var firstNode = new int[nc];
            for (int c = 0; c < nc; ++c) {
                first0[c] = first1[c] = -1;
                firstNode[c] = -1;
            }
            for (int node = 0; node < cells.Cell.Length; ++node) {
                if (interior[node] == false)
                    continue;
                int c = cells.Cell[node];
                seen[c] = true;
                if (firstNode[c] < 0)
                    firstNode[c] = node;
                if (mark0[node] >= 0 && first0[c] < 0)
                    first0[c] = node;
                if (mark1[node] >= 0 && first1[c] < 0)
                    first1[c] = node;
            }
            for (int c = 0; c < nc; ++c) {
                if (seen[c] && first0[c] >= 0 && first1[c] >= 0) {
                    if (result.ConflictingCells++ == 0) {
                        result.Piece = first0[c] / 2;
                        result.Piece2 = first1[c] / 2;
                        result.MarkTriangle0 = mark0[first0[c]];
                        result.MarkTriangle1 = mark1[first1[c]];
                    }
                }
            }
            if (result.ConflictingCells > 0) {
                result.Reason = ExactMeshBoolean.SheetRefusal.SidesInconsistent;
                return result;
            }
            // (a cell with a mark that has no piece of the workpiece's surface: a component of the sheet with no edge in common with the walls, in one
            // cell of the workpiece. Cells are joined only across shared edges, so this cell may be the same region as another cell with the other
            // mark, which only the nesting of the shells could tell: refused, whatever UncutSide says)
            // (shells that may nest: two cells of different shells are joined by no edge, so they may be one region of material (a cavity inside the
            // first input), and a 0 mark on one and a 1 mark on the other may be a contradiction that only the nesting of the shells could show: refused,
            // as an uncut cell is, when the box of one shell holds the box of the other and both labels occur on the two. Shells that cannot nest
            // (disjoint boxes) are independent: two sibling cavities of one block with different labels are seen only through the block's shell
            // (each nests with it), and if that shell is unmarked, by the uncut cell below (ShellCount > 1 always refuses it): do not relax that
            // rule without a nesting layer. A legitimate cut through a hollow body, with both labels on the nested shells, is refused too)
            if (cells.ShellCount > 1) {
                var shell0 = new bool[cells.ShellCount];
                var shell1 = new bool[cells.ShellCount];
                var shell0Cell = new int[cells.ShellCount];
                var shell1Cell = new int[cells.ShellCount];
                for (int sh = 0; sh < cells.ShellCount; ++sh)
                    shell0Cell[sh] = shell1Cell[sh] = -1;
                for (int node = 0; node < cells.Cell.Length; ++node) {
                    if (cells.Boundary[node / 2] == false || interior[node] == false)
                        continue;
                    int sh = cells.Shell[node / 2], c = cells.Cell[node];
                    if (first0[c] >= 0) { shell0[sh] = true; if (shell0Cell[sh] < 0) shell0Cell[sh] = c; }
                    if (first1[c] >= 0) { shell1[sh] = true; if (shell1Cell[sh] < 0) shell1Cell[sh] = c; }
                }
                for (int sa = 0; sa < cells.ShellCount; ++sa)
                    for (int sb = sa + 1; sb < cells.ShellCount; ++sb) {
                        if (cells.MayNest(sa, sb) == false)
                            continue;
                        if ((shell0[sa] || shell0[sb]) == false || (shell1[sa] || shell1[sb]) == false)
                            continue;       // (one label on the two shells at most)
                        // (the cells that name the two labels, for the witness)
                        int c0 = shell0[sa] ? shell0Cell[sa] : shell0Cell[sb];
                        int c1 = shell1[sa] ? shell1Cell[sa] : shell1Cell[sb];
                        result.ConflictingCells = 1;
                        result.Piece = first0[c0] / 2;
                        result.Piece2 = first1[c1] / 2;
                        result.MarkTriangle0 = mark0[first0[c0]];
                        result.MarkTriangle1 = mark1[first1[c1]];
                        result.Reason = ExactMeshBoolean.SheetRefusal.SidesInconsistent;
                        return result;
                    }
            }
            for (int c = 0; c < nc; ++c) {
                if (seen[c] && wall[c] == false && (first0[c] >= 0 || first1[c] >= 0)) {
                    if (result.FloatingCells++ == 0) {
                        result.Piece = (first0[c] >= 0 ? first0[c] : first1[c]) / 2;
                        result.MarkTriangle0 = first0[c] >= 0 ? mark0[first0[c]] : -1;
                        result.MarkTriangle1 = first1[c] >= 0 ? mark1[first1[c]] : -1;
                    }
                }
            }
            if (result.FloatingCells > 0) {
                result.Reason = ExactMeshBoolean.SheetRefusal.FloatingComponent;
                return result;
            }
            for (int c = 0; c < nc; ++c) {
                if (seen[c] && first0[c] < 0 && first1[c] < 0) {
                    if (result.UncutCells++ == 0)
                        result.Piece = firstNode[c] / 2;
                }
            }
            // (a part of another tool that is in no cell the sheet or the workpiece's surface touches is in a cell that is not known here, whatever uncut says)
            bool floating = false;
            for (int c = 0; c < nc; ++c)
                floating |= seen[c] && first0[c] < 0 && first1[c] < 0 && wall[c] == false;
            if (result.UncutCells > 0 && (uncut == ExactMeshBoolean.SheetUncutSides.Refuse || cells.ShellCount > 1 || floating)) {
                result.Reason = ExactMeshBoolean.SheetRefusal.UncutComponents;
                return result;
            }
            sbyte side = (sbyte)(uncut == ExactMeshBoolean.SheetUncutSides.Inside ? 1 : 0);
            for (int node = 0; node < cells.Cell.Length; ++node) {
                if (interior[node] == false)
                    continue;
                int c = cells.Cell[node];
                result.Label[node] = first1[c] >= 0 ? (sbyte)1 : first0[c] >= 0 ? (sbyte)0 : side;
            }
            return result;
        }
    }
}
