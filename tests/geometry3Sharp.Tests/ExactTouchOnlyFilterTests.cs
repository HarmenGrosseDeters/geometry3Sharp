using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshArrangement.touch_only asks TriangleTriangleFilter.BeyondShared before the exact test (and falls back to it
    /// on Undecided). Pinned on every pair of generated triangle soups with shared corners 0, 1 and 2, coplanar neighbours
    /// and near-coplanar slivers: touch_only equals the exact test (IntersectsBeyondShared == false), in both argument
    /// orders and over the similarities (the 2^k scales bit for bit), and the filter never contradicts the exact test
    /// where it decides.
    /// </summary>
    public class ExactTouchOnlyFilterTests
    {
        // triangles on a small lattice (corners are lattice points, each triangle with vertices of its own: the arrangement
        // welds them by position, so triangles share corners by point id); sliver: the lattice points of some corners are
        // moved by a tiny step, which makes near-coplanar and nearly collinear configurations
        static DMesh3 Soup(Random r, int triangles, int lattice, double step, double tiny)
        {
            var m = new DMesh3();
            Func<int> coord = () => r.Next(lattice);
            for (int k = 0; k < triangles; ++k) {
                var ids = new int[3];
                for (int c = 0; c < 3; ++c) {
                    var p = new Vector3d(coord() * step, coord() * step, r.Next(3) == 0 ? 0 : coord() * step);
                    // (the same lattice point, nudged by a few ulps-scale steps, for a share of the corners)
                    if (tiny > 0 && r.Next(4) == 0)
                        p += new Vector3d(r.Next(-1, 2) * tiny, r.Next(-1, 2) * tiny, r.Next(-1, 2) * tiny);
                    ids[c] = m.AppendVertex(p);
                }
                m.AppendTriangle(ids[0], ids[1], ids[2]);
            }
            return m;
        }

        sealed class Counts { public int Shared0, Shared1, Shared2, Coplanar, Decided, Undecided, Differ, Contradicted; }

        static string AllPairs(DMesh3 mesh, Counts n)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3> { mesh }) { SelfIntersections = true };
            arr.Compute();
            var answers = new System.Text.StringBuilder();
            for (int ti = 0; ti < arr.Triangles.Count; ++ti)
                for (int ui = ti + 1; ui < arr.Triangles.Count; ++ui) {
                    var T = arr.Triangles[ti]; var U = arr.Triangles[ui];
                    if (T.Degenerate || U.Degenerate)
                        continue;
                    int shared = 0;
                    for (int k = 0; k < 3; ++k)
                        if (U.Corners[k] == T.Corners[0] || U.Corners[k] == T.Corners[1] || U.Corners[k] == T.Corners[2])
                            shared++;
                    if (shared == 3)
                        continue;
                    if (shared == 0) n.Shared0++; else if (shared == 1) n.Shared1++; else n.Shared2++;
                    var pa = new Index3i(T.Corners[0], T.Corners[1], T.Corners[2]);
                    var pb = new Index3i(U.Corners[0], U.Corners[1], U.Corners[2]);
                    var A = new Triangle3d(T.V0, T.V1, T.V2); var B = new Triangle3d(U.V0, U.V1, U.V2);
                    bool expected = ExactTriangleIntersection.IntersectsBeyondShared(pa, ref A, pb, ref B) == false;
                    if (ExactPredicates.Orient3d(T.V0, T.V1, T.V2, U.V0) == 0 && ExactPredicates.Orient3d(T.V0, T.V1, T.V2, U.V1) == 0
                        && ExactPredicates.Orient3d(T.V0, T.V1, T.V2, U.V2) == 0)
                        n.Coplanar++;
                    // the filter's contract, on the pair classes touch_only sends to it
                    int f = TriangleTriangleFilter.BeyondShared(pa, ref A, pb, ref B);
                    if (f == TriangleTriangleFilter.Undecided)
                        n.Undecided++;
                    else {
                        n.Decided++;
                        if ((f == TriangleTriangleFilter.Intersecting) != (expected == false))
                            n.Contradicted++;
                    }
                    bool got = arr.touch_only(T, U);
                    if (got != expected || arr.touch_only(U, T) != expected)
                        n.Differ++;
                    answers.Append(ti).Append(',').Append(ui).Append(got ? '+' : '-').Append(';');
                }
            return answers.ToString();
        }

        static void CheckSoups(Counts n)
        {
            // (shared corners of every class need a small lattice and many triangles; the nudged one makes slivers)
            AllPairs(Soup(new Random(11), 70, 3, 0.25, 0), n);
            AllPairs(Soup(new Random(12), 70, 4, 0.125, 0), n);
            AllPairs(Soup(new Random(13), 60, 3, 0.1, 1e-13), n);
            AllPairs(Soup(new Random(14), 60, 3, 3.0, 5e-16), n);
        }

        [Fact]
        public void TouchOnly_EqualsTheExactTest_AndTheFilterNeverContradicts()
        {
            var n = new Counts();
            CheckSoups(n);
            Assert.Equal(0, n.Differ);
            Assert.Equal(0, n.Contradicted);
            // not vacuous: every class of pairs occurs, coplanar ones too, and the filter decides a share of them (random soups mostly
            // cross, which it leaves to the exact test; the pairs touch_only sees in meshes are mostly separate)
            Assert.True(n.Shared0 > 1000, "pairs without shared corners: " + n.Shared0);
            Assert.True(n.Shared1 > 100, "pairs sharing a corner: " + n.Shared1);
            Assert.True(n.Shared2 > 20, "pairs sharing an edge: " + n.Shared2);
            Assert.True(n.Coplanar > 50, "coplanar pairs: " + n.Coplanar);
            Assert.True(n.Decided > 1000 && n.Undecided > 100, "decided " + n.Decided + ", undecided " + n.Undecided);
        }

        [Fact]
        public void Invariance_OverSimilarities()
        {
            // every similarity: touch_only equals the exact test; the 2^k scales give the same answers bit for bit
            var r = new Random(21);
            DMesh3 soup = Soup(r, 50, 3, 0.25, 1e-13);
            var n0 = new Counts();
            string reference = AllPairs(soup, n0);
            foreach (Similarity sim in Similarity.All()) {
                var m = new DMesh3(soup);
                foreach (int vid in m.VertexIndices())
                    m.SetVertex(vid, sim.Apply(m.GetVertex(vid)));
                var n = new Counts();
                string answers = AllPairs(m, n);
                Assert.Equal(0, n.Differ);
                Assert.Equal(0, n.Contradicted);
                if (sim.IsExact)
                    Assert.Equal(reference, answers);
            }
            Assert.Equal(0, n0.Differ);
        }
    }
}
