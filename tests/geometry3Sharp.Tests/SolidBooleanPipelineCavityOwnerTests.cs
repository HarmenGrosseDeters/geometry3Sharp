using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.RemoveThinSolids: a cavity belongs to the smallest outer shell that contains it, decided exactly
    /// (signed crossings with exact orientations; a probe point on the shell is skipped), not by a fast winding number
    /// above 1/2 (issue #1204). The owner decides what MinThickness measures.
    /// </summary>
    public class SolidBooleanPipelineCavityOwnerTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        // the shells appended into one mesh (separate components); inverted: a cavity (inward normals)
        static DMesh3 Shells(params (DMesh3 mesh, bool inverted)[] shells)
        {
            var m = new DMesh3();
            foreach (var (s, inverted) in shells) {
                var map = new Dictionary<int, int>();
                foreach (int v in s.VertexIndices())
                    map[v] = m.AppendVertex(s.GetVertex(v));
                foreach (int t in s.TriangleIndices()) {
                    Index3i tv = s.GetTriangle(t);
                    m.AppendTriangle(inverted ? new Index3i(map[tv.a], map[tv.c], map[tv.b]) : new Index3i(map[tv.a], map[tv.b], map[tv.c]));
                }
            }
            return m;
        }

        static double Volume(DMesh3 m)
        {
            double v6 = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                v6 += m.GetVertex(tv.a).Dot(m.GetVertex(tv.b).Cross(m.GetVertex(tv.c)));
            }
            return v6 / 6;
        }

        // the neighbouring double of a positive x (net48 has no Math.BitIncrement)
        static double Next(double x, int dir) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + dir); }

        static int Winding(DMesh3 shell, Vector3d p, out bool decided)
        {
            int w;
            decided = SolidBooleanPipeline.ShellIndex.ExactWinding(shell, shell.TriangleIndices().ToArray(), shell.GetBounds(), p, out w);
            return w;
        }

        [Fact]
        public void ExactWinding_OfACube_InsideOutsideAndOnTheSurface()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            Assert.True(Volume(cube) > 0);
            bool decided;
            Assert.Equal(1, Winding(cube, new Vector3d(0.5, 0.5, 0.5), out decided)); Assert.True(decided);
            Assert.Equal(1, Winding(cube, new Vector3d(1e-300, 0.5, 0.5), out decided)); Assert.True(decided);
            Assert.Equal(0, Winding(cube, new Vector3d(2, 0.5, 0.5), out decided)); Assert.True(decided);
            Assert.Equal(0, Winding(cube, new Vector3d(-1e-300, 0.5, 0.5), out decided)); Assert.True(decided);
            // on a face, an edge, a vertex, and on the diagonal of a face (an edge of its two triangles): not decided
            foreach (var on in new[] { new Vector3d(1, 0.5, 0.25), new Vector3d(1, 1, 0.5), new Vector3d(1, 1, 1), new Vector3d(0.5, 0.5, 0) }) {
                Winding(cube, on, out decided);
                Assert.False(decided, on.ToString());
            }
            // a cavity (inward normals): -1 inside
            DMesh3 cavity = Shells((Box(0, 0, 0, 1, 1, 1), true));
            Assert.Equal(-1, Winding(cavity, new Vector3d(0.5, 0.5, 0.5), out decided)); Assert.True(decided);
        }

        [Fact]
        public void CrossingsAlong_ASegmentThroughAnEdgeOrAVertex_IsDegenerate()
        {
            // from the centre of the unit cube: through the middle of the edge x = 1, y = 1, through the corner (1, 1, 1),
            // through the diagonal of a face (an edge of its two triangles), and through the middle of a face (a clean crossing)
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            int[] tris = cube.TriangleIndices().ToArray();
            var c = new Vector3d(0.5, 0.5, 0.5);
            bool degenerate;
            foreach (var far in new[] { new Vector3d(3.5, 3.5, 0.5), new Vector3d(3.5, 3.5, 3.5), new Vector3d(0.5, 0.5, -2.5) })
            {
                SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, c, far, out degenerate);
                // (a face's diagonal runs through its centre for one of the two possible splits; the test checks it only then)
                if (far.z == -2.5 && !FaceDiagonalThroughCentre(cube, 0))
                    continue;
                Assert.True(degenerate, far.ToString());
            }
            int w = SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, c, new Vector3d(3.5, 0.75, 0.6), out degenerate);
            Assert.False(degenerate);
            Assert.Equal(1, w);
        }

        // does the bottom face (z = z0) of the cube have a triangle edge through its centre?
        static bool FaceDiagonalThroughCentre(DMesh3 cube, double z0)
        {
            foreach (int e in cube.EdgeIndices()) {
                Index2i ev = cube.GetEdgeV(e);
                Vector3d a = cube.GetVertex(ev.a), b = cube.GetVertex(ev.b);
                if (a.z == z0 && b.z == z0 && a.x != b.x && a.y != b.y)
                    return true;
            }
            return false;
        }

        [Fact]
        public void ExactWinding_FarFromTheOrigin_IsExact()
        {
            // at UTM-like coordinates a point one unit in the last place inside or outside a face is decided right
            var o = new Vector3d(2527500, 5634200, 100);
            DMesh3 cube = Box(o.x, o.y, o.z, o.x + 1, o.y + 1, o.z + 1);
            double inside = Next(o.x + 1, -1), outside = Next(o.x + 1, 1);
            bool decided;
            Assert.Equal(1, Winding(cube, new Vector3d(inside, o.y + 0.5, o.z + 0.25), out decided)); Assert.True(decided);
            Assert.Equal(0, Winding(cube, new Vector3d(outside, o.y + 0.5, o.z + 0.25), out decided)); Assert.True(decided);
        }

        [Fact]
        public void ExactWinding_ASegmentThroughAnEdge_TriesAnotherDirection()
        {
            // the first direction is axis-parallel (no jitter): from (1/4, y, 1/4) towards the face x = 0 it hits the diagonal of
            // the face (an edge of its two triangles) for one of y = 1/4 and y = 3/4, whichever split the box has; the count
            // along that segment is not a winding number, the next attempt (jittered) gives the right one
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            int[] tris = cube.TriangleIndices().ToArray();
            Func<int, Vector3d> jitter = attempt => attempt == 0 ? Vector3d.Zero : new Vector3d(0.1, 0.07, -0.05);
            foreach (double y in new[] { 0.25, 0.75 }) {
                int w;
                Assert.True(SolidBooleanPipeline.ShellIndex.ExactWinding(cube, tris, cube.GetBounds(), new Vector3d(0.25, y, 0.25), out w, jitter));
                Assert.Equal(1, w);
            }
            // every attempt along the diagonal: not decided
            bool anyUndecided = false;
            foreach (double y in new[] { 0.25, 0.75 }) {
                int w;
                anyUndecided |= !SolidBooleanPipeline.ShellIndex.ExactWinding(cube, tris, cube.GetBounds(), new Vector3d(0.25, y, 0.25), out w, a => Vector3d.Zero);
            }
            Assert.True(anyUndecided);
        }

        [Fact]
        public void Contains_SkipsProbesOnTheShell()
        {
            // the cube 4 around a cavity 1 x 1 x 1 on its floor: the cavity's triangles in z = 0 (centroids on the shell's
            // bottom face) come first; the next one decides
            DMesh3 outer = Box(0, 0, 0, 4, 4, 4);
            DMesh3 cavity = Shells((Box(1, 1, 0, 2, 2, 1), true));
            DMesh3 m = Shells((Box(0, 0, 0, 4, 4, 4), false), (Box(1, 1, 0, 2, 2, 1), true));
            int[] outerTris = Enumerable.Range(0, outer.TriangleCount).ToArray();
            int[] cavTris = Enumerable.Range(outer.TriangleCount, cavity.TriangleCount).ToArray();
            int[] probes = cavTris.OrderBy(t => m.GetTriCentroid(t).z == 0 ? 0 : 1).ToArray();
            Assert.Equal(0, m.GetTriCentroid(probes[0]).z);
            Assert.True(SolidBooleanPipeline.ShellIndex.Contains(m, outerTris, m.GetBounds(), probes));
            // every probe on the shell: not contained (nothing decides)
            int[] onShell = cavTris.Where(t => m.GetTriCentroid(t).z == 0).ToArray();
            Assert.False(SolidBooleanPipeline.ShellIndex.Contains(m, outerTris, m.GetBounds(), onShell));
        }

        // the thin solids removed from a copy of m (MinThickness scale)
        static List<SolidBooleanThinSolid> Removed(DMesh3 m, out DMesh3 rest, double scale = 1.0)
        {
            rest = new DMesh3(m);
            return SolidBooleanPipeline.RemoveThinSolids(rest, scale);
        }

        [Fact]
        public void ACavityWhoseLargestTrianglesLieOnAnIsland_BelongsToTheBlock()
        {
            // a block with a flat cavity (its largest triangles are its floor and ceiling) and inside it an island that fills
            // the cavity's height and touches its floor and ceiling face to face: every large probe of the cavity lies on the
            // island's surface (a fast winding number there is about 1/2). The island is the smaller outer shell around
            // those probes, but it does not contain the cavity: the owner is the block, so the block (thickness 2.8) stays
            // and only the island (thickness 0.67, alone) is measured and removed. With the island as owner, the island and the
            // cavity together (negative volume) would go
            DMesh3 m = Shells((Box(0, 0, 0, 10, 10, 10), false), (Box(2, 2, 2, 8, 8, 3), true), (Box(3, 3, 2, 7, 7, 3), false));
            DMesh3 rest;
            var removed = Removed(m, out rest);
            Assert.Single(removed);
            Assert.Equal(1, removed[0].Shells);
            Assert.Equal(16, removed[0].Volume, 9);
            Assert.Equal(24, rest.TriangleCount);
        }

        [Fact]
        public void ACavityInAThinWall_BelongsToItsPlate()
        {
            // a plate with a cavity whose walls above and below are 2^-30 thick, inside a larger box (also around the cavity;
            // the plate is the smaller): the plate with its cavity is one thin solid (volume 36), the large box stays
            double e = Math.Pow(2, -30);
            DMesh3 m = Shells((Box(0, 0, 0, 10, 10, 1), false), (Box(1, 1, e, 9, 9, 1 - e), true), (Box(-1, -1, -1, 11, 11, 2), false));
            DMesh3 rest;
            var removed = Removed(m, out rest);
            Assert.Single(removed);
            Assert.Equal(2, removed[0].Shells);
            Assert.Equal(36, removed[0].Volume, 3);
            Assert.Equal(12, rest.TriangleCount);
        }

        [Fact]
        public void ACavityOutsideAShellsBoxContents_IsLeftAlone()
        {
            // the box filter: a shell whose box does not contain the cavity's box cannot own it (a cavity in no shell is left)
            DMesh3 m = Shells((Box(0, 0, 0, 10, 10, 10), false), (Box(5, 5, 5, 12, 12, 12), true));
            DMesh3 rest;
            var removed = Removed(m, out rest);
            Assert.Empty(removed);
            Assert.Equal(24, rest.TriangleCount);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Owners_AreInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            DMesh3 m = Shells((Box(0, 0, 0, 10, 10, 10), false), (Box(2, 2, 2, 8, 8, 3), true), (Box(3, 3, 2, 7, 7, 3), false), (Box(4, 4, 5, 6, 6, 9), true),
                (Box(20, 0, 0, 30, 10, 1), false), (Box(21, 1, 0.25, 29, 9, 0.75), true));
            DMesh3 restA, restB;
            var a = Removed(m, out restA);
            var b = Removed(Invariance.Transformed(m, s), out restB, s.Scale);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; ++i) {
                Assert.Equal(a[i].Shells, b[i].Shells);
                Assert.Equal(a[i].Triangles, b[i].Triangles);
            }
            Assert.Equal(restA.TriangleCount, restB.TriangleCount);
            Assert.Equal(2, a.Count);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
