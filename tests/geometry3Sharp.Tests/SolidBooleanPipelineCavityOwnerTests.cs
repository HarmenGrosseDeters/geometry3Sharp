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

        // the triangle ids of the shells appended by Shells(...) into one mesh: shell i has the triangles of range i
        static int[][] Ranges(params DMesh3[] shells)
        {
            var r = new int[shells.Length][];
            int at = 0;
            for (int i = 0; i < shells.Length; ++i) {
                r[i] = Enumerable.Range(at, shells[i].TriangleCount).ToArray();
                at += shells[i].TriangleCount;
            }
            return r;
        }

        static AxisAlignedBox3d BoundsOf(DMesh3 m, int[] tris)
        {
            var box = AxisAlignedBox3d.Empty;
            foreach (int t in tris) {
                Index3i tv = m.GetTriangle(t);
                box.Contain(m.GetVertex(tv.a)); box.Contain(m.GetVertex(tv.b)); box.Contain(m.GetVertex(tv.c));
            }
            return box;
        }

        [Fact]
        public void Contains_SkipsVerticesOnTheShell_AndFallsBackToCentroids()
        {
            // a cube 4 around a cavity 1 x 1 x 1 on its floor: the four vertices of the cavity in z = 0 lie on the shell, the
            // others decide
            DMesh3 outer = Box(0, 0, 0, 4, 4, 4), cav = Box(1, 1, 0, 2, 2, 1);
            DMesh3 m = Shells((outer, false), (cav, true));
            int[][] r = Ranges(outer, cav);
            var box = BoundsOf(m, r[0]);
            Assert.True(SolidBooleanPipeline.ShellIndex.Contains(m, r[0], box, r[1]));
            // only the cavity's floor (every vertex and every centroid on the shell): nothing decides
            int[] floor = r[1].Where(t => m.GetTriCentroid(t).z == 0).ToArray();
            Assert.Equal(2, floor.Length);
            Assert.False(SolidBooleanPipeline.ShellIndex.Contains(m, r[0], box, floor));
            // a tetrahedron on four corners of the cube: every vertex on the shell, the centroids (the fallback) decide
            var tet = new DMesh3();
            int a = tet.AppendVertex(new Vector3d(0, 0, 0)), b = tet.AppendVertex(new Vector3d(4, 4, 0)), c = tet.AppendVertex(new Vector3d(4, 0, 4)), d = tet.AppendVertex(new Vector3d(0, 4, 4));
            tet.AppendTriangle(a, c, b); tet.AppendTriangle(a, b, d); tet.AppendTriangle(a, d, c); tet.AppendTriangle(b, c, d);
            DMesh3 m2 = Shells((outer, false), (tet, false));
            int[][] r2 = Ranges(outer, tet);
            Assert.True(SolidBooleanPipeline.ShellIndex.Contains(m2, r2[0], box, r2[1]));
        }

        // the cube [X, X + 4]^3 with a cavity that touches its wall x = X from inside, the large touching face first
        static DMesh3 TouchingCavity(double X, out int[][] ranges)
        {
            DMesh3 outer = Box(X, X, X, X + 4, X + 4, X + 4), cav = Box(X, X + 1, X + 1, X + 0.5, X + 3, X + 3);
            ranges = Ranges(outer, cav);
            return Shells((outer, false), (cav, true));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.0)]
        [InlineData(0.1)]
        [InlineData(1e6 + 1.0 / 3)]
        [InlineData(2527500.3)]
        [InlineData(5634200.7)]
        [InlineData(123456.789)]
        [InlineData(37.123456789)]
        [InlineData(-98765.4321)]
        public void Contains_ACavityTouchingTheWall_IsContainedAtEveryOffset(double X)
        {
            int[][] r;
            DMesh3 m = TouchingCavity(X, out r);
            Assert.True(SolidBooleanPipeline.ShellIndex.Contains(m, r[0], BoundsOf(m, r[0]), r[1]));
        }

        static IEnumerable<Similarity> Rotations()
        {
            var rng = new Random(1204);
            for (int i = 0; i < 40; ++i) {
                var axis = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized;
                yield return new Similarity("rot" + i, 1, Matrix3d.AxisAngleD(axis, 5 + 8.5 * i), new Vector3d(rng.Next(-5, 5) * 1000.3, rng.NextDouble() * 1e6, 0));
            }
        }

        [Fact]
        public void Contains_ARotatedCavityTouchingTheWall_IsContained()
        {
            // rotation rounds the vertices of the cavity and of the shell apart: a probe that is a rounded point of the cavity
            // would come out on either side; the vertices of the cavity decide
            int[][] r;
            DMesh3 m0 = TouchingCavity(0, out r);
            string bad = "";
            foreach (var s in Rotations()) {
                DMesh3 m = Invariance.Transformed(m0, s);
                if (!SolidBooleanPipeline.ShellIndex.Contains(m, r[0], BoundsOf(m, r[0]), r[1])) bad += s.Name + " ";
            }
            Assert.Equal("", bad);
        }

        [Fact]
        public void Contains_ARotatedPlugThatFillsTheCavity_IsNotItsOwner()
        {
            // a plug (an outer shell with the cavity's vertices): every vertex of the cavity lies on it, no point decides
            DMesh3 block = Box(0, 0, 0, 10, 10, 10), cavity = Box(1, 1, 1, 3, 3, 3), plug = Box(1, 1, 1, 3, 3, 3);
            DMesh3 m0 = Shells((block, false), (cavity, true), (plug, false));
            int[][] r = Ranges(block, cavity, plug);
            string bad = "";
            foreach (var s in Rotations()) {
                DMesh3 m = Invariance.Transformed(m0, s);
                if (SolidBooleanPipeline.ShellIndex.Contains(m, r[2], BoundsOf(m, r[2]), r[1])) bad += "P" + s.Name + " ";
                if (!SolidBooleanPipeline.ShellIndex.Contains(m, r[0], BoundsOf(m, r[0]), r[1])) bad += "B" + s.Name + " ";
            }
            Assert.Equal("", bad);
            // and through RemoveThinSolids: the block owns the cavity; the plug (thickness 2/3) is its own solid, removed alone
            DMesh3 rest;
            var removed = Removed(m0, out rest);
            Assert.Single(removed);
            Assert.Equal(1, removed[0].Shells);
            Assert.Equal(8, removed[0].Volume, 9);
        }

        // a one-triangle probe at depth d below the wall x = X of the cube [X, X + 4]^3 (inside the cube)
        static bool ProbeContained(double X, double d, out double depth)
        {
            DMesh3 outer = Box(X, X, X, X + 4, X + 4, X + 4);
            var probe = new DMesh3();
            double x = X + d;
            depth = x - X;
            int a = probe.AppendVertex(new Vector3d(x, X + 1, X + 1)), b = probe.AppendVertex(new Vector3d(x, X + 3, X + 1)), c = probe.AppendVertex(new Vector3d(x, X + 1, X + 3));
            probe.AppendTriangle(a, b, c);
            DMesh3 m = Shells((outer, false), (probe, false));
            int[][] r = Ranges(outer, probe);
            return SolidBooleanPipeline.ShellIndex.Contains(m, r[0], BoundsOf(m, r[0]), r[1]);
        }

        [Theory]
        [InlineData(1e6)]
        [InlineData(3.0e3)]
        [InlineData(-7.7e5)]
        public void Contains_AProbeWithinTheTouchDistanceIsSkipped_OneBeyondItDecides(double X)
        {
            // the tolerance is 256 eps M (M the largest coordinate of the shell's box): a probe at half of it from the wall is
            // not decided (every vertex and the centroid are skipped: not contained), at twice of it the exact winding number
            // decides (inside)
            double touch = SolidBooleanPipeline.ShellIndex.Touch(Box(X, X, X, X + 4, X + 4, X + 4).GetBounds());
            Assert.Equal(256 * 2.220446049250313e-16 * Math.Max(Math.Abs(X), Math.Abs(X + 4)), touch, 15);
            double depth;
            Assert.False(ProbeContained(X, 0.5 * touch, out depth));
            Assert.True(depth > 0 && depth < touch);
            Assert.True(ProbeContained(X, 2 * touch, out depth));
            Assert.True(depth > touch);
        }

        [Fact]
        public void Contains_AProbeExactlyAtTheTouchDistance_IsSkipped()
        {
            // the cube [0, 4]^3: touch = 256 * 2^-52 * 4 = 2^-42, a probe at that depth is at the distance exactly (the closest point of the
            // wall is straight below it): "within" includes the boundary, so it is skipped
            double depth;
            double touch = SolidBooleanPipeline.ShellIndex.Touch(Box(0, 0, 0, 4, 4, 4).GetBounds());
            Assert.Equal(Math.Pow(2, -42), touch);
            Assert.False(ProbeContained(0.0, touch, out depth));
            Assert.Equal(touch, depth);
            Assert.True(ProbeContained(0.0, 2 * touch, out depth));
        }

        [Fact]
        public void TwoEqualShellsAroundACavity_TheFirstOwnsIt()
        {
            // two identical blocks (a doubled shell) and a cavity in both: the first of equal volume keeps the cavity, the later one
            // does not take it over; the first solid is the block with the cavity (volume 64 - 8), the second the plain block
            DMesh3 m = Shells((Box(0, 0, 0, 4, 4, 4), false), (Box(0, 0, 0, 4, 4, 4), false), (Box(1, 1, 1, 3, 3, 3), true));
            DMesh3 rest;
            var removed = Removed(m, out rest, 100.0);
            Assert.Equal(2, removed.Count);
            Assert.Equal(2, removed[0].Shells);
            Assert.Equal(56, removed[0].Volume, 9);
            Assert.Equal(1, removed[1].Shells);
            Assert.Equal(64, removed[1].Volume, 9);
        }

        [Fact]
        public void BoxHolds_AllowsTheRoundingOfAMovedBox_NotMore()
        {
            // a cavity that touches the shell from inside has a box that sticks out of the shell's by the rounding of its
            // coordinates (checked: the touch distance of the shell's box)
            foreach (double X in new[] { 0.0, 1e6, -3.3e4 }) {
                var shell = new AxisAlignedBox3d(new Vector3d(X, X, X), new Vector3d(X + 4, X + 4, X + 4));
                double touch = SolidBooleanPipeline.ShellIndex.Touch(shell);
                Assert.True(touch >= 0);
                Assert.True(SolidBooleanPipeline.ShellIndex.BoxHolds(shell, new AxisAlignedBox3d(new Vector3d(X + 1, X + 1, X + 1), new Vector3d(X + 2, X + 2, X + 2))));
                if (touch == 0)
                    continue;
                Assert.True(SolidBooleanPipeline.ShellIndex.BoxHolds(shell, new AxisAlignedBox3d(new Vector3d(X - 0.5 * touch, X + 1, X + 1), new Vector3d(X + 2, X + 2, X + 2))));
                Assert.True(SolidBooleanPipeline.ShellIndex.BoxHolds(shell, new AxisAlignedBox3d(new Vector3d(X + 1, X + 1, X + 1), new Vector3d(X + 4 + 0.5 * touch, X + 2, X + 2))));
                Assert.False(SolidBooleanPipeline.ShellIndex.BoxHolds(shell, new AxisAlignedBox3d(new Vector3d(X - 2 * touch, X + 1, X + 1), new Vector3d(X + 2, X + 2, X + 2))));
                Assert.False(SolidBooleanPipeline.ShellIndex.BoxHolds(shell, new AxisAlignedBox3d(new Vector3d(X + 1, X + 1, X + 1), new Vector3d(X + 2, X + 2, X + 4 + 2 * touch))));
            }
        }

        // one triangle (corners in the given order) in the plane z = 0 as a mesh, and the crossings of the segment from (x, y, -1) to (x, y, 1)
        static int Along(Vector3d[] corners, double x, double y, out bool degenerate)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(corners[0]), b = m.AppendVertex(corners[1]), c = m.AppendVertex(corners[2]);
            m.AppendTriangle(a, b, c);
            return SolidBooleanPipeline.ShellIndex.CrossingsAlong(m, new[] { 0 }, new Vector3d(x, y, -1), new Vector3d(x, y, 1), out degenerate);
        }

        [Fact]
        public void CrossingsAlong_ThroughAnEdgeOrAVertex_IsDegenerate_BesideIsNot_InsideCountsOnce()
        {
            // the triangle (0,0), (4,0), (0,4), each cyclic order of its corners and both orientations, so that every edge is the
            // first, the second and the third of the three orientation tests, and the line passes on both sides of each of them
            var tri = new[] { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0) };
            for (int flip = 0; flip < 2; ++flip)
                for (int rot = 0; rot < 3; ++rot) {
                    var c = new Vector3d[3];
                    for (int k = 0; k < 3; ++k)
                        c[k] = tri[(rot + (flip == 0 ? k : 2 - k)) % 3];
                    string at = "flip " + flip + " rot " + rot;
                    bool degenerate;
                    foreach (var xy in new[] { (2.0, 0.0), (2.0, 2.0), (0.0, 2.0), (0.0, 0.0), (4.0, 0.0), (0.0, 4.0) }) {
                        Along(c, xy.Item1, xy.Item2, out degenerate);
                        Assert.True(degenerate, at + " through " + xy);
                    }
                    foreach (var xy in new[] { (2.0, -1.0), (3.0, 3.0), (-1.0, 2.0), (-1.0, -1.0), (5.0, -1.0), (-1.0, 5.0), (1.0, 5.0), (5.0, 1.0) }) {
                        Assert.Equal(0, Along(c, xy.Item1, xy.Item2, out degenerate));
                        Assert.False(degenerate, at + " beside " + xy);
                    }
                    // inside: one crossing, from behind it (the side opposite its normal) it leaves: +1 for the normal +z, -1 for -z
                    int w = Along(c, 1, 1, out degenerate);
                    Assert.False(degenerate);
                    Assert.Equal(flip == 0 ? 1 : -1, w);
                }
        }

        [Fact]
        public void ExactWinding_AProbeInThePlaneOfATriangleButOutsideIt_IsDecided()
        {
            // the tetrahedron (0,0,0), (4,0,0), (0,4,0), (0,0,4); the probe (3.5, 3.5, 0) lies in the plane z = 0 of its floor and in the
            // floor's box, but outside the floor and outside the solid: not on the shell, winding 0
            var t = new DMesh3();
            int a = t.AppendVertex(new Vector3d(0, 0, 0)), b = t.AppendVertex(new Vector3d(4, 0, 0)), c = t.AppendVertex(new Vector3d(0, 4, 0)), d = t.AppendVertex(new Vector3d(0, 0, 4));
            t.AppendTriangle(a, c, b); t.AppendTriangle(a, b, d); t.AppendTriangle(a, d, c); t.AppendTriangle(b, c, d);
            bool decided;
            Assert.Equal(0, Winding(t, new Vector3d(3.5, 3.5, 0), out decided));
            Assert.True(decided);
            Assert.Equal(1, Winding(t, new Vector3d(0.5, 0.5, 0.5), out decided));
            Assert.True(decided);
            // on the floor itself: not decided
            Winding(t, new Vector3d(1, 1, 0), out decided);
            Assert.False(decided);
        }

        [Fact]
        public void ExactWinding_ARayStraightAlongTheAxis_LeavesTheBox()
        {
            // no jitter at all: the segment runs along the nearest face's axis and must end beyond the box (the margin), not on its face
            DMesh3 cube = Box(0, 0, 0, 4, 4, 4);
            int w;
            bool decided = SolidBooleanPipeline.ShellIndex.ExactWinding(cube, cube.TriangleIndices().ToArray(), cube.GetBounds(), new Vector3d(1.3, 1.7, 2.1), out w, i => Vector3d.Zero);
            Assert.True(decided);
            Assert.Equal(1, w);
        }

        [Fact]
        public void Contains_AnInvertedShellAroundAPoint_DoesNotContain()
        {
            // winding -1 is not inside: a shell with inward normals around an island
            DMesh3 outer = Box(0, 0, 0, 4, 4, 4), inner = Box(1, 1, 1, 2, 2, 2);
            DMesh3 m = Shells((outer, true), (inner, false));
            int[][] r = Ranges(outer, inner);
            Assert.False(SolidBooleanPipeline.ShellIndex.Contains(m, r[0], BoundsOf(m, r[0]), r[1]));
            bool decided;
            Assert.Equal(-1, Winding(Shells((Box(0, 0, 0, 4, 4, 4), true)), new Vector3d(1, 1, 1), out decided));
            Assert.True(decided);
        }

        [Fact]
        public void CrossingsAlong_InThePlaneOfATriangle_AndEndingOnIt()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            int[] tris = cube.TriangleIndices().ToArray();
            bool degenerate;
            // in the plane z = 1 of the top face and through it: degenerate
            SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, new Vector3d(-1, 0.6, 1), new Vector3d(2, 0.6, 1), out degenerate);
            Assert.True(degenerate);
            // in that plane beside the face: nothing is crossed
            int w = SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, new Vector3d(-1, 2, 1), new Vector3d(2, 2, 1), out degenerate);
            Assert.False(degenerate); Assert.Equal(0, w);
            // ending on the top face (inside it): degenerate
            SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.6, 0.2, 1), out degenerate);
            Assert.True(degenerate);
            // ending in the plane z = 1 beside the cube, or starting in it: clean
            w = SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, new Vector3d(2.5, 0.5, 0.5), new Vector3d(3, 0.5, 1), out degenerate);
            Assert.False(degenerate); Assert.Equal(0, w);
            w = SolidBooleanPipeline.ShellIndex.CrossingsAlong(cube, tris, new Vector3d(3, 0.5, 1), new Vector3d(3, 0.5, -2), out degenerate);
            Assert.False(degenerate); Assert.Equal(0, w);
        }

        static DMesh3 Positive(DMesh3 m)
        {
            if (Volume(m) < 0)
                m.ReverseOrientation();
            return m;
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    m.AppendTriangle(id(i, j), id(i + 1, j), id(i + 1, j + 1));
                    m.AppendTriangle(id(i, j), id(i + 1, j + 1), id(i, j + 1));
                }
            return Positive(m);
        }

        [Fact]
        public void ExactWinding_OfASphereAndATorus_AgreesWithTheAnalyticInsideTest()
        {
            DMesh3 sphere = Positive(new Sphere3Generator_NormalizedCube() { Radius = 2, EdgeVertices = 16 }.Generate().MakeDMesh());
            DMesh3 torus = Torus(3, 1, 32, 16);
            var rng = new Random(77);
            int insideS = 0, outsideS = 0, insideT = 0, outsideT = 0;
            for (int k = 0; k < 600; ++k) {
                var p = new Vector3d((rng.NextDouble() - 0.5) * 9, (rng.NextDouble() - 0.5) * 9, (rng.NextDouble() - 0.5) * 3);
                double ds = p.Length - 2;
                bool decided;
                if (Math.Abs(ds) > 0.08) {
                    int w = Winding(sphere, p, out decided);
                    Assert.True(decided);
                    Assert.Equal(ds < 0 ? 1 : 0, w);
                    if (ds < 0) insideS++; else outsideS++;
                }
                double dt = Math.Sqrt(Math.Pow(Math.Sqrt(p.x * p.x + p.y * p.y) - 3, 2) + p.z * p.z) - 1;
                if (Math.Abs(dt) > 0.08) {
                    int w = Winding(torus, p, out decided);
                    Assert.True(decided);
                    Assert.Equal(dt < 0 ? 1 : 0, w);
                    if (dt < 0) insideT++; else outsideT++;
                }
            }
            Assert.True(insideS > 10 && outsideS > 10 && insideT > 10 && outsideT > 10, insideS + " " + outsideS + " " + insideT + " " + outsideT);
        }

        // an L-shaped prism: the outline (0,0) (1,0) (2,0) (2,1) (1,1) (1,2) (0,2) (0,1) scaled by size in x and y, extruded to height h
        static DMesh3 LPrism(double size, double h)
        {
            double[,] o = { { 0, 0 }, { 1, 0 }, { 2, 0 }, { 2, 1 }, { 1, 1 }, { 1, 2 }, { 0, 2 }, { 0, 1 } };
            var m = new DMesh3();
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < 8; ++i)
                    m.AppendVertex(new Vector3d(o[i, 0] * size, o[i, 1] * size, k * h));
            // the caps: the squares (0,1,4,7), (1,2,3,4), (7,4,5,6)
            int[][] squares = { new[] { 0, 1, 4, 7 }, new[] { 1, 2, 3, 4 }, new[] { 7, 4, 5, 6 } };
            foreach (var q in squares) {
                m.AppendTriangle(q[0], q[2], q[1]); m.AppendTriangle(q[0], q[3], q[2]);
                m.AppendTriangle(8 + q[0], 8 + q[1], 8 + q[2]); m.AppendTriangle(8 + q[0], 8 + q[2], 8 + q[3]);
            }
            for (int i = 0; i < 8; ++i) {
                int j = (i + 1) % 8;
                m.AppendTriangle(i, j, 8 + j); m.AppendTriangle(i, 8 + j, 8 + i);
            }
            return Positive(m);
        }

        static DMesh3 Moved(DMesh3 m, Vector3d t)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices())
                c.SetVertex(v, c.GetVertex(v) + t);
            return c;
        }

        [Theory]
        [InlineData(0.0, 0.0, 0.0)]
        [InlineData(2527500.0, 5634200.0, 100.0)]
        public void ANonconvexShell_OwnsItsCavityInAnArm_NotOneInTheNotch(double ox, double oy, double oz)
        {
            // a thin L prism (thickness 0.1 < MinThickness 1) with a small cavity inside its arm and another one in its notch (the
            // empty square, inside the prism's box and outside the prism): the first goes with the prism, the second is no one's
            var t = new Vector3d(ox, oy, oz);
            DMesh3 l = Moved(LPrism(10, 0.1), t);
            DMesh3 arm = Moved(Box(15, 2, 0.04, 16, 3, 0.06), t), notch = Moved(Box(15, 15, 0.04, 16, 16, 0.06), t);
            DMesh3 m = Shells((l, false), (arm, true), (notch, true));
            DMesh3 rest;
            var removed = Removed(m, out rest);
            Assert.Single(removed);
            Assert.Equal(2, removed[0].Shells);
            Assert.Equal(notch.TriangleCount, rest.TriangleCount);
            // the same prism thick (thickness 5): nothing is removed
            DMesh3 thick = Shells((Moved(LPrism(10, 20), t), false), (Moved(Box(15, 2, 4, 16, 3, 5), t), true));
            Assert.Empty(Removed(thick, out rest));
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
            // the box filter: a shell whose box does not contain the cavity's box cannot own it (a cavity in no shell is left).
            // The tetrahedron's first vertex (5, 5, 0.1) lies inside the plate and the others outside: a vertex alone would
            // decide "inside"; the filter keeps it from owning the cavity
            var tet = new DMesh3();
            int a = tet.AppendVertex(new Vector3d(5, 5, 0.1)), b = tet.AppendVertex(new Vector3d(12, 5, 0.1)), c = tet.AppendVertex(new Vector3d(5, 12, 0.1)), d = tet.AppendVertex(new Vector3d(5, 5, 5));
            tet.AppendTriangle(a, c, b); tet.AppendTriangle(a, b, d); tet.AppendTriangle(a, d, c); tet.AppendTriangle(b, c, d);
            if (Volume(tet) > 0)
                tet.ReverseOrientation();
            DMesh3 m = Shells((Box(0, 0, 0, 10, 10, 0.2), false), (tet, false));
            DMesh3 rest;
            var removed = Removed(m, out rest);
            Assert.Single(removed);
            Assert.Equal(1, removed[0].Shells);
            Assert.Equal(4, rest.TriangleCount);
            // (a cube cavity that crosses a block: no owner either)
            m = Shells((Box(0, 0, 0, 10, 10, 10), false), (Box(5, 5, 5, 12, 12, 12), true));
            Assert.Empty(Removed(m, out rest));
            Assert.Equal(24, rest.TriangleCount);
        }

        [Fact]
        public void Contains_AFallbackCentroidOutsideTheBox_IsSkipped()
        {
            // a tetrahedron with every vertex on the walls of the cube [X, X + 4]^3, the face on the wall x = X the largest: its
            // centroid is a rounded point that may lie one unit in the last place outside the cube, and is skipped
            var rng = new Random(5);
            int outside = 0;
            for (int k = 0; k < 60; ++k) {
                double X = (rng.NextDouble() - 0.5) * 1e5;
                DMesh3 cube = Box(X, X, X, X + 4, X + 4, X + 4);
                var tet = new DMesh3();
                int a = tet.AppendVertex(new Vector3d(X, X, X)), b = tet.AppendVertex(new Vector3d(X, X + 4, X)), c = tet.AppendVertex(new Vector3d(X, X, X + 4)), d = tet.AppendVertex(new Vector3d(X + 1, X, X + 1));
                tet.AppendTriangle(a, c, b); tet.AppendTriangle(a, b, d); tet.AppendTriangle(a, d, c); tet.AppendTriangle(b, c, d);
                DMesh3 m = Shells((cube, false), (tet, true));
                int[][] r = Ranges(cube, tet);
                var box = BoundsOf(m, r[0]);
                Assert.True(SolidBooleanPipeline.ShellIndex.Contains(m, r[0], box, r[1]), X.ToString("R"));
                if (!box.Contains(m.GetTriCentroid(r[1][0])))
                    outside++;
            }
            Assert.True(outside > 0, "no centroid fell outside the box: the case is not tested");
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
            if (s.IsExact)
                for (int i = 0; i < a.Count; ++i) {
                    // a power-of-two scale: volume, thickness and centre are the same bit for bit, scaled
                    double k = s.Scale;
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Volume * k * k * k), BitConverter.DoubleToInt64Bits(b[i].Volume));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Thickness * k), BitConverter.DoubleToInt64Bits(b[i].Thickness));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Center.x * k), BitConverter.DoubleToInt64Bits(b[i].Center.x));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Center.y * k), BitConverter.DoubleToInt64Bits(b[i].Center.y));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a[i].Center.z * k), BitConverter.DoubleToInt64Bits(b[i].Center.z));
                }
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
