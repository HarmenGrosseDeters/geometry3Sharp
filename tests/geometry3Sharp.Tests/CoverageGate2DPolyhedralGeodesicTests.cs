// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Branches of PolyhedralGeodesicDistance that no other test reached: the merging of a new window into a pending window of the
    /// same origin when only some of the new window's pieces touch the pending one (the others stay as new windows).
    /// </summary>
    public class CoverageGate2DPolyhedralGeodesicTests
    {
        // an n x n grid with heights of amplitude x (u - 1/2), u from System.Random(seed) (the sequence of a seed is fixed): found by a search over seeds for
        // runs that merge a window into a pending one (first test: with only some pieces of the new window touching it), see the tests.
        static DMesh3 Terrain(int seed, int sizeDraw, int ampDraw, int n, double amplitude)
        {
            var rnd = new Random(seed);
            rnd.Next(sizeDraw); rnd.Next(ampDraw);              // (the draws of the search before the mesh: size and amplitude)
            bool diagonal = rnd.Next(2) == 0;
            var m = new DMesh3();
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(i, j, amplitude * (rnd.NextDouble() - 0.5)));
            for (int j = 0; j < n - 1; ++j)
                for (int i = 0; i < n - 1; ++i) {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    if (diagonal || ((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                }
            return m;
        }

        static PolyhedralGeodesicDistance RunAgainstOracle(DMesh3 m, int[] s)
        {
            var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(s));
            Assert.True(g.Merges > 0, "the run did not merge");
            var orc = new ExactMeshGeodesicDistance(m);
            Assert.Equal(ExactMeshGeodesicDistance.Result.Ok, orc.Compute(s));
            var b = m.CachedBounds; double ext = Math.Max(b.Max.x - b.Min.x, Math.Max(b.Max.y - b.Min.y, b.Max.z - b.Min.z));
            foreach (int v in m.VertexIndices()) {
                double tol = 5e-15 * orc.Upper[v] + 5e-16 * ext;
                Assert.True(g.Distance[v] >= orc.Lower[v] - tol && g.Distance[v] <= orc.Upper[v] + tol, $"vertex {v}: {g.Distance[v]:R} outside [{orc.Lower[v]:R}, {orc.Upper[v]:R}]");
            }
            return g;
        }

        [Fact]
        public void PartialMerge_KeepsThePiecesOfTheWindowThatDoNotTouch_DistancesStayWithinTheOracle()
        {
            // 11 x 11, amplitude 0.3, sources 39 and 120: a new window of two pieces merges with a pending window that only one of them touches
            var g = RunAgainstOracle(Terrain(87, 22, 4, 11, 0.3), new[] { 39, 120 });
            // the run is deterministic: the counts pin the pieces (the untouched piece becomes its own window; dropping it gives 695 windows)
            Assert.Equal(15, g.Merges); Assert.Equal(697, g.Windows);
        }

        [Fact]
        public void MergeIntoAPendingWindow_ContinuesWithTheRestOfTheWindowsOfTheSide_DistancesStayWithinTheOracle()
        {
            // 14 x 14, amplitude 2, sources 142 and 128: after a merge the scan goes on over the other windows of the side (ending it there gives 1351 windows)
            var g = RunAgainstOracle(Terrain(1676, 22, 4, 14, 2), new[] { 142, 128 });
            Assert.Equal(9, g.Merges); Assert.Equal(1350, g.Windows);
        }
    }
}
