using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Refusal = g3.ExactMeshBoolean.SheetRefusal;
using Uncut = g3.ExactMeshBoolean.SheetUncutSides;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshCells (layer B) on its own: the local cells of pieces around an edge, and the labelling of them from marks. The pieces are
    /// single triangles ("pages") that share the edge (0,0,0)-(1,0,0) of the x axis, each in a mesh of its own, so the arrangement keeps them as
    /// they are; the order of the pages in the list must not change the cells.
    /// </summary>
    public class ExactMeshCellsTests
    {
        static readonly Vector3d E0 = new Vector3d(0, 0, 0), E1 = new Vector3d(1, 0, 0);

        // a page at the angle (degrees) around the x axis, running E0 -> E1 if forward
        static DMesh3 Page(double degrees, bool forward)
        {
            double a = degrees * Math.PI / 180;
            var p = new Vector3d(0.5, Math.Round(Math.Cos(a), 12), Math.Round(Math.Sin(a), 12));
            var m = new DMesh3();
            m.AppendVertex(E0); m.AppendVertex(E1); m.AppendVertex(p);
            if (forward) m.AppendTriangle(0, 1, 2); else m.AppendTriangle(1, 0, 2);
            return m;
        }

        sealed class Fan
        {
            public ExactMeshArrangement Arr;
            public List<Index3i> Tris = new List<Index3i>();
        }

        static Fan MakeFan(double[] degrees, bool[] forward)
        {
            var meshes = degrees.Select((d, k) => Page(d, forward[k])).ToList();
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            var fan = new Fan { Arr = arr };
            for (int k = 0; k < meshes.Count; ++k) {
                Index3i t = meshes[k].GetTriangle(0);
                int[] vp = arr.VertexPoints[k];
                fan.Tris.Add(new Index3i(vp[t.a], vp[t.b], vp[t.c]));
            }
            return fan;
        }

        static bool[] All(int n, bool v) => Enumerable.Repeat(v, n).ToArray();

        // the cells as sets of nodes, sorted: independent of the numbering
        static string Partition(ExactMeshCells c)
        {
            var groups = Enumerable.Range(0, c.Cell.Length).GroupBy(n => c.Cell[n]).Select(g => "{" + string.Join(",", g.OrderBy(x => x)) + "}");
            return string.Join(" ", groups.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void FourPagesAllRunningTheSameWay_FourWedges_EachBetweenTheBackOfOneAndTheFrontOfTheNext()
        {
            // pages at 0, 90, 180, 270 degrees all running E0 -> E1: the normal of each points to increasing angles, so the wedge after page k
            // is its front (node 2k) and the wedge before page k + 1 its back (node 2 (k + 1) + 1)
            Fan f = MakeFan(new[] { 0.0, 90, 180, 270 }, All(4, true));
            ExactMeshCells c = ExactMeshCells.Build(f.Arr, f.Tris, All(4, false), All(4, false));
            Assert.Equal(4, c.CellCount);
            Assert.Equal(8, c.FreeEdges.Count);       // (the two other edges of each page; the shared edge has four pieces)
            Assert.Equal("{0,3} {1,6} {2,5} {4,7}", Partition(c));
            Assert.Equal(c.Cell[0], c.Cell[3]);       // front of 0, back of 1 (the page at 90 degrees is node 2, 3)
            Assert.Equal(c.Cell[2], c.Cell[5]);
            Assert.Equal(c.Cell[4], c.Cell[7]);
            Assert.Equal(c.Cell[6], c.Cell[1]);       // front of 3, back of 0 across the wrap-around
            Assert.Equal(4, new HashSet<int>(c.Cell).Count);
        }

        [Fact]
        public void TheOrderOfThePagesInTheListDoesNotChangeTheCells()
        {
            double[] deg = { 0, 40, 130, 200, 300 };
            bool[] fw = { true, false, true, true, false };
            string first = null;
            var rnd = new Random(5);
            for (int run = 0; run < 12; ++run) {
                int[] perm = Enumerable.Range(0, deg.Length).OrderBy(_ => rnd.Next()).ToArray();
                if (run == 0) perm = Enumerable.Range(0, deg.Length).ToArray();
                Fan f = MakeFan(perm.Select(i => deg[i]).ToArray(), perm.Select(i => fw[i]).ToArray());
                ExactMeshCells c = ExactMeshCells.Build(f.Arr, f.Tris, All(5, false), All(5, false));
                // (the partition in terms of the pages: node 2k + s of the list is page perm[k], side s)
                var named = Enumerable.Range(0, c.Cell.Length).GroupBy(n => c.Cell[n])
                    .Select(g => string.Join(",", g.Select(n => perm[n / 2] + "." + (n % 2)).OrderBy(x => x, StringComparer.Ordinal)));
                string p = string.Join(" ", named.OrderBy(x => x, StringComparer.Ordinal));
                if (first == null) first = p; else Assert.Equal(first, p);
            }
        }

        [Fact]
        public void TwoPagesAtAHalfTurn_AFlatSurfaceAndAFold()
        {
            // two pages that form a flat surface (180 degrees, the same orientation of the surface: one runs E0 -> E1, the other E1 -> E0): the
            // front of one and the front of the other are one wedge, the two backs the other
            Fan f = MakeFan(new[] { 0.0, 180 }, new[] { true, false });
            ExactMeshCells c = ExactMeshCells.Build(f.Arr, f.Tris, All(2, false), All(2, false));
            Assert.Equal(2, c.CellCount);
            Assert.Equal(c.Cell[0], c.Cell[2]);
            Assert.Equal(c.Cell[1], c.Cell[3]);
            Assert.NotEqual(c.Cell[0], c.Cell[1]);
            // a fold (the same two pages both running E0 -> E1): the front of one is the back of the other, in both wedges
            Fan g = MakeFan(new[] { 0.0, 180 }, new[] { true, true });
            ExactMeshCells d = ExactMeshCells.Build(g.Arr, g.Tris, All(2, false), All(2, false));
            Assert.Equal(2, d.CellCount);
            Assert.Equal(d.Cell[0], d.Cell[3]);
            Assert.Equal(d.Cell[1], d.Cell[2]);
        }

        [Fact]
        public void OnePage_AFreeEdgeOnEveryEdge_AndItsSidesAreOneCell()
        {
            Fan f = MakeFan(new[] { 0.0 }, new[] { true });
            ExactMeshCells c = ExactMeshCells.Build(f.Arr, f.Tris, All(1, false), All(1, false));
            Assert.Equal(2, c.CellCount);        // (no wedge at a free edge: nothing joins the sides)
            Assert.Equal(3, c.FreeEdges.Count);
            // (a transparent page has no free edge: it is no wall)
            ExactMeshCells t = ExactMeshCells.Build(f.Arr, f.Tris, All(1, false), All(1, true));
            Assert.Empty(t.FreeEdges);
            Assert.Equal(1, t.CellCount);
        }

        [Fact]
        public void ATransparentPageJoinsTheWedgesOnBothOfItsSides()
        {
            // three pages at 0, 120, 240 degrees: with the middle one a wall: three cells; transparent: the two wedges next to it are one
            Fan f = MakeFan(new[] { 0.0, 120, 240 }, All(3, true));
            Assert.Equal(3, ExactMeshCells.Build(f.Arr, f.Tris, All(3, false), All(3, false)).CellCount);
            ExactMeshCells t = ExactMeshCells.Build(f.Arr, f.Tris, All(3, false), new[] { false, true, false });
            Assert.Equal(2, t.CellCount);
        }

        [Fact]
        public void Shells_ArePiecesOfTheBoundaryConnectedAcrossSharedEdges()
        {
            Fan f = MakeFan(new[] { 0.0, 90, 180 }, All(3, true));
            ExactMeshCells c = ExactMeshCells.Build(f.Arr, f.Tris, new[] { true, true, false }, All(3, false));
            Assert.Equal(1, c.ShellCount);
            Assert.Equal(0, c.Shell[0]);
            Assert.Equal(0, c.Shell[1]);
            Assert.Equal(-1, c.Shell[2]);
            Assert.Equal(0, ExactMeshCells.Build(f.Arr, f.Tris, All(3, false), All(3, false)).ShellCount);
        }

        // ---- Label ---------------------------------------------------------------------------------------------------------------------

        // the fan of pages at 0, 180 degrees as a flat sheet (both oriented the same way) closed around by nothing: two cells (front side, back side)
        static (ExactMeshCells, bool[], int[], int[]) Flat()
        {
            Fan f = MakeFan(new[] { 0.0, 180 }, new[] { true, false });
            ExactMeshCells c = ExactMeshCells.Build(f.Arr, f.Tris, All(2, true), All(2, false));
            c.FreeEdges.Clear();       // (the pages' other edges are free: Label would refuse them; the cells are what these tests look at)
            var interior = All(4, true);
            var m0 = new[] { -1, -1, -1, -1 }; var m1 = new[] { -1, -1, -1, -1 };
            return (c, interior, m0, m1);
        }

        [Fact]
        public void Label_FreeEdgesFirst_ThenConflicts_ThenUncut()
        {
            // a single page: three free edges, whatever the marks
            Fan one = MakeFan(new[] { 0.0 }, new[] { true });
            ExactMeshCells c1 = ExactMeshCells.Build(one.Arr, one.Tris, All(1, false), All(1, false));
            ExactMeshCells.Labelling rim = ExactMeshCells.Label(c1, All(2, true), new[] { 7, -1 }, new[] { -1, 7 }, Uncut.Refuse);
            Assert.Equal(Refusal.RimInsideWorkpiece, rim.Reason);
            Assert.Equal(0, rim.Piece);
            // the flat pair, a mark 0 on the front cell and 1 on the back cell: fine, labels 0 and 1
            var (c, interior, m0, m1) = Flat();
            m0[0] = 3; m1[1] = 3;
            ExactMeshCells.Labelling ok = ExactMeshCells.Label(c, interior, m0, m1, Uncut.Refuse);
            Assert.Equal(Refusal.None, ok.Reason);
            Assert.Equal(new sbyte[] { 0, 1, 0, 1 }, ok.Label);
            // both marks in one cell
            m1[2] = 4;
            ExactMeshCells.Labelling bad = ExactMeshCells.Label(c, interior, m0, m1, Uncut.Refuse);
            Assert.Equal(Refusal.SidesInconsistent, bad.Reason);
            Assert.Equal(1, bad.ConflictingCells);
            Assert.Equal(3, bad.MarkTriangle0);
            Assert.Equal(4, bad.MarkTriangle1);
            Assert.Equal(0, bad.Piece);
            Assert.Equal(1, bad.Piece2);
            // a mark on a side outside the workpiece's interior does not count: without it the conflict is gone
            interior[2] = false;
            ExactMeshCells.Labelling gone = ExactMeshCells.Label(c, interior, m0, m1, Uncut.Refuse);
            Assert.Equal(Refusal.None, gone.Reason);
            Assert.Equal(new sbyte[] { 0, 1, 0, 1 }, gone.Label);
            // the other mark of the pair is the one that is left
            interior[2] = true; interior[0] = false;
            ExactMeshCells.Labelling other = ExactMeshCells.Label(c, interior, m0, m1, Uncut.Refuse);
            Assert.Equal(Refusal.None, other.Reason);
            Assert.Equal(new sbyte[] { 0, 1, 1, 1 }, other.Label);
        }

        [Fact]
        public void Label_UncutCells_ByTheStatedSide_OnlyWithOneShellAndNoFloatingPart()
        {
            var (c, interior, m0, m1) = Flat();
            m0[0] = 1;                 // the front cell is marked 0, the back cell (nodes 1 and 3) has no mark
            ExactMeshCells.Labelling refused = ExactMeshCells.Label(c, interior, m0, m1, Uncut.Refuse);
            Assert.Equal(Refusal.UncutComponents, refused.Reason);
            Assert.Equal(1, refused.UncutCells);
            // (pieces that are no boundary of the workpiece: a marked cell without a wall is a floating part of the sheet, refused whatever the side;
            // an unmarked one is refused as uncut)
            Fan fl = MakeFan(new[] { 0.0, 180 }, new[] { true, false });
            ExactMeshCells floating = ExactMeshCells.Build(fl.Arr, fl.Tris, All(2, false), All(2, false));
            floating.FreeEdges.Clear();
            ExactMeshCells.Labelling fr = ExactMeshCells.Label(floating, interior, m0, m1, Uncut.Outside);
            Assert.Equal(Refusal.FloatingComponent, fr.Reason);
            Assert.Equal(1, fr.FloatingCells);
            Assert.Equal(1, fr.MarkTriangle0);
            Assert.Equal(-1, fr.MarkTriangle1);
            Assert.Equal(0, fr.Piece);
            Assert.Equal(Refusal.FloatingComponent, ExactMeshCells.Label(floating, interior, m0, m1, Uncut.Refuse).Reason);
            m0[0] = -1;
            Assert.Equal(Refusal.UncutComponents, ExactMeshCells.Label(floating, interior, m0, m1, Uncut.Outside).Reason);
            m0[0] = 1;
            // with the pieces as the boundary of the workpiece (one shell): the stated side labels the cell
            Fan f = MakeFan(new[] { 0.0, 180 }, new[] { true, false });
            ExactMeshCells w = ExactMeshCells.Build(f.Arr, f.Tris, All(2, true), All(2, false));
            w.FreeEdges.Clear();
            Assert.Equal(1, w.ShellCount);
            ExactMeshCells.Labelling outside = ExactMeshCells.Label(w, interior, m0, m1, Uncut.Outside);
            Assert.Equal(Refusal.None, outside.Reason);
            Assert.Equal(new sbyte[] { 0, 0, 0, 0 }, outside.Label);
            ExactMeshCells.Labelling inside = ExactMeshCells.Label(w, interior, m0, m1, Uncut.Inside);
            Assert.Equal(new sbyte[] { 0, 1, 0, 1 }, inside.Label);
            Assert.Equal(Refusal.UncutComponents, ExactMeshCells.Label(w, interior, m0, m1, Uncut.Refuse).Reason);
            // two shells: refused with a stated side too
            var two = new ExactMeshCells { Cell = w.Cell, CellCount = w.CellCount, Shell = new[] { 0, 1 }, ShellCount = 2, Boundary = All(2, true), Transparent = All(2, false) };
            Assert.Equal(Refusal.UncutComponents, ExactMeshCells.Label(two, interior, m0, m1, Uncut.Outside).Reason);
        }
    }
}
