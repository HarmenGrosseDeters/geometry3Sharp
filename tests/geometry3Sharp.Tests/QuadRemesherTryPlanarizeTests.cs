using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1674: QuadRemesher.TryPlanarize keeps the rebuilt faces of a graph without its crossing edges only if they drop fewer faces than the result so far,
    /// and the list variant tries the graphs in turn. On a grid of 3 x 3 quads with and without a long edge that crosses two of its edges.
    /// </summary>
    public class QuadRemesherTryPlanarizeTests
    {
        static QuadRemesher.PolyGraph Grid(bool chord)
        {
            var g = new QuadRemesher.PolyGraph();
            for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) g.Add(new Vector3d(i, j, 0), Vector3d.AxisZ, 0);
            for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) {
                if (i + 1 < 4) g.Link(j * 4 + i, j * 4 + i + 1);
                if (j + 1 < 4) g.Link(j * 4 + i, (j + 1) * 4 + i);
            }
            if (chord) g.Link(1 * 4 + 0, 2 * 4 + 3);       // (0, 1) to (3, 2): crosses the edges at x = 1 and x = 2
            return g;
        }

        static DPolyMesh Build(QuadRemesher.PolyGraph g, out int dropped)
        {
            return QuadRemesher.BuildFaces(Grid2(g), 6, out dropped);
        }

        // (BuildFaces prunes the graph it is given: a copy)
        static QuadRemesher.PolyGraph Grid2(QuadRemesher.PolyGraph g)
        {
            var c = new QuadRemesher.PolyGraph();
            for (int i = 0; i < g.Pos.Count; ++i) c.Add(g.Pos[i], g.Nrm[i], g.Kind[i]);
            for (int i = 0; i < g.Pos.Count; ++i) foreach (int j in g.Adj[i]) if (i < j) c.Link(i, j);
            return c;
        }

        static int EdgeCount(QuadRemesher.PolyGraph g) { return g.Adj.Sum(a => a.Count) / 2; }

        [Fact]
        public void TheCrossingEdgeCostsFaces_AndTheRepairGetsThemBack()
        {
            int d0, d1;
            DPolyMesh plain = Build(Grid(false), out d0);
            DPolyMesh crossed = Build(Grid(true), out d1);
            Assert.True(d1 > d0, "the crossing edge drops no more faces than the grid: " + d1 + " vs " + d0);   // the premise of the tests below
            Assert.Equal(9, plain.FaceCount);

            QuadRemesher.PolyGraph g = Grid(true);
            int dropped = d1;
            DPolyMesh r = QuadRemesher.TryPlanarize(g, 6, crossed, ref dropped);
            Assert.NotSame(crossed, r);
            Assert.Equal(d0, dropped);
            Assert.Equal(9, r.FaceCount);
            Assert.Equal(EdgeCount(Grid(true)), EdgeCount(g));              // the graph that was given is not changed
        }

        [Fact]
        public void ARebuildThatDropsNoFewerFaces_IsNotKept()
        {
            int d0, d1;
            Build(Grid(false), out d0);
            DPolyMesh crossed = Build(Grid(true), out d1);
            // the result so far drops as many faces as the rebuild would: the result so far stays (a tie is not an improvement)
            int dropped = d0;
            DPolyMesh r = QuadRemesher.TryPlanarize(Grid(true), 6, crossed, ref dropped);
            Assert.Same(crossed, r);
            Assert.Equal(d0, dropped);
            // and one that drops fewer
            dropped = d0 - 1;
            Assert.Same(crossed, QuadRemesher.TryPlanarize(Grid(true), 6, crossed, ref dropped));
            Assert.Equal(d0 - 1, dropped);
        }

        [Fact]
        public void AGraphWithoutCrossings_ChangesNothing()
        {
            int d0;
            DPolyMesh plain = Build(Grid(false), out d0);
            int dropped = d0 + 5;
            Assert.Same(plain, QuadRemesher.TryPlanarize(Grid(false), 6, plain, ref dropped));
            Assert.Equal(d0 + 5, dropped);
        }

        [Fact]
        public void TheGraphsAreTriedInTurn_UntilNoFaceIsDropped()
        {
            int d0, d1;
            Build(Grid(false), out d0);
            DPolyMesh crossed = Build(Grid(true), out d1);
            // the first has no crossing, the second has one: the second is the one that helps
            int dropped = d1;
            DPolyMesh r = QuadRemesher.TryPlanarize(new List<QuadRemesher.PolyGraph> { Grid(false), Grid(true) }, 6, crossed, ref dropped);
            Assert.NotSame(crossed, r);
            Assert.Equal(d0, dropped);
            Assert.Equal(9, r.FaceCount);
            // the first helps, the second is tried with the better result as the one to beat (no change)
            dropped = d1;
            DPolyMesh r2 = QuadRemesher.TryPlanarize(new List<QuadRemesher.PolyGraph> { Grid(true), Grid(true) }, 6, crossed, ref dropped);
            Assert.Equal(d0, dropped);
            Assert.Equal(9, r2.FaceCount);
            // nothing dropped: nothing is tried
            dropped = 0;
            Assert.Same(crossed, QuadRemesher.TryPlanarize(new List<QuadRemesher.PolyGraph> { Grid(true) }, 6, crossed, ref dropped));
            Assert.Equal(0, dropped);
        }
    }
}
