using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 0 of the part handling of MeshRobustRepair (PassThroughCleanParts, design note ROBUST_REPAIR_SCALE_FREE.md, option B): a closed component that is
    /// clean on its own and whose box meets no other component's box is passed through untouched.
    /// </summary>
    public class RobustRepairPassThroughTests
    {
        static DMesh3 Sphere(double x, double radius, int edgeVertices = 6) => RobustRepairFrameTests.Sphere(new Vector3d(x, 0, 0), radius, edgeVertices);

        static DMesh3 WithHole(DMesh3 m)
        {
            foreach (int t in m.TriangleIndices().Take(8).ToList())
                m.RemoveTriangle(t, true, false);
            return m;
        }

        static DMesh3 Union(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (var p in parts) MeshEditor.Append(m, p);
            return m;
        }

        static DMesh3 Scaled(DMesh3 m, int shift)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.Pow(2, shift) * c.GetVertex(v));
            return c;
        }

        static HashSet<Vector3d> Vertices(DMesh3 m) => new HashSet<Vector3d>(m.VertexIndices().Select(v => m.GetVertex(v)));

        static MeshRobustRepair Run(DMesh3 m, bool passThrough = true)
        {
            var r = new MeshRobustRepair(m) { SolidifyResolution = 32, WrapRelativeAlpha = 16, WrapRelativeOffset = 160, PassThroughCleanParts = passThrough };
            r.Apply();
            return r;
        }

        // a clean sphere, and an open one next to it: the sphere's vertices are in the result as they were, the open one is repaired
        [Fact]
        public void CleanPart_PassesThrough_OthersRepaired()
        {
            DMesh3 clean = Sphere(0, 1);
            DMesh3 scene = Union(clean, WithHole(Sphere(8, 0.5, 5)));
            var r = Run(scene);
            Assert.True(r.ResultReason == "", r.ResultReason);
            Assert.Equal(clean.TriangleCount, r.PassedThroughTriangles);
            Assert.True(Vertices(clean).IsSubsetOf(Vertices(r.Mesh)));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            var comps = new MeshConnectedComponents(r.Mesh);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);
        }

        // every part clean: nothing runs, the mesh is as it was
        [Fact]
        public void AllPartsClean_MeshUnchanged_NoStageRuns()
        {
            DMesh3 scene = RobustRepairFrameTests.CleanTwoParts();
            int triangles = scene.TriangleCount;
            var r = new MeshRobustRepair(new DMesh3(scene));
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal(triangles, r.PassedThroughTriangles);
            Assert.Equal(triangles, r.Mesh.TriangleCount);
            Assert.True(Vertices(scene).SetEquals(Vertices(r.Mesh)));
            Assert.False(r.UsedExactResolve || r.UsedSolidify || r.UsedAlphaWrap);
        }

        // boxes that meet (here: overlap) are not parts that stay apart: nothing passes through
        [Theory]
        [InlineData(0.75)]
        [InlineData(1.0)]     // the boxes touch
        public void MeetingBoxes_NoPassThrough(double touchShare)
        {
            double x = 2 * touchShare * MeshRepairMetrics.TriangleBounds(Sphere(0, 1)).Max.x;
            var r = Run(Union(Sphere(0, 1), Sphere(x, 1)));
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        // a part that is not clean on its own stays in the pipeline: an open one, an inside-out one, one with a degenerate triangle
        [Fact]
        public void UncleanParts_NotPassedThrough()
        {
            DMesh3 inside = Sphere(16, 1);
            inside.ReverseOrientation();
            var r = Run(Union(Sphere(0, 1), WithHole(Sphere(8, 1, 5)), inside));
            Assert.Equal(Sphere(0, 1).TriangleCount, r.PassedThroughTriangles);
        }

        [Fact]
        public void Off_NothingPassesThrough()
        {
            var r = Run(Union(Sphere(0, 1), WithHole(Sphere(8, 0.5, 5))), passThrough: false);
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        [Fact]
        public void SingleComponent_Untouched()
        {
            var r = Run(WithHole(Sphere(0, 1)));
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        // the box [-1, 1]^3 with one edge split at the fraction t from a corner: the same solid, 14 triangles, one needle (#1337)
        static DMesh3 BoxWithSplitEdge(double t)
        {
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            int eid = b.EdgeIndices().First(x => { Index2i ev = b.GetEdgeV(x); return Math.Abs((b.GetVertex(ev.a) - b.GetVertex(ev.b)).Length - 2) < 1e-12; });
            Index2i evs = b.GetEdgeV(eid);
            b.SplitEdge(eid, out DMesh3.EdgeSplitInfo info);
            b.SetVertex(info.vNew, Vector3d.Lerp(b.GetVertex(evs.a), b.GetVertex(evs.b), t));
            return b;
        }

        // a clean input of one component is returned as it is: no needle collapse moves a corner (#1337)
        [Theory]
        [InlineData(1e-3)]
        [InlineData(1e-6)]
        public void CleanSingleComponent_ReturnedUnchanged(double t)
        {
            DMesh3 box = BoxWithSplitEdge(t);
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(box));
            var r = new MeshRobustRepair(new DMesh3(box));
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.Equal(box.TriangleCount, r.PassedThroughTriangles);
            Assert.Equal(box.TriangleCount, r.Mesh.TriangleCount);
            Assert.True(Vertices(box).SetEquals(Vertices(r.Mesh)));
            Assert.Equal(ResultChecks.Volume(box), ResultChecks.Volume(r.Mesh), 12);
        }

        // alone as next to a far part: the same result for the part
        [Fact]
        public void CleanSingleComponent_AsNextToAFarPart()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            DMesh3 both = Union(box, new TrivialBox3Generator() { Box = new Box3d(new Vector3d(10, 0, 0), new Vector3d(1, 1, 1)) }.Generate().MakeDMesh());
            var alone = new MeshRobustRepair(new DMesh3(box));
            var withFar = new MeshRobustRepair(both);
            Assert.True(alone.Apply());
            Assert.True(withFar.Apply());
            Assert.Equal(ResultChecks.Volume(withFar.Mesh) - 8, ResultChecks.Volume(alone.Mesh), 12);
            Assert.Equal(box.TriangleCount, alone.PassedThroughTriangles);
            Assert.Equal(both.TriangleCount, withFar.PassedThroughTriangles);
            Assert.True(Vertices(both).SetEquals(Vertices(withFar.Mesh)));
        }

        // under every similarity of the invariance tests the clean part passes through with its vertices as they are, and the 2^k scales map back bit for bit
        [Fact]
        public void CleanSingleComponent_InvariantUnderSimilarities()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            int ran = 0;
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 input = Invariance.Transformed(box, sim);
                if (MeshRobustRepair.NotCleanReason(input) != "")
                    continue;       // (a similarity that makes the rounded input not clean is no case of this test)
                var r = new MeshRobustRepair(new DMesh3(input));
                Assert.True(r.Apply(), sim.Name);
                Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
                ++ran;
                Assert.True(Vertices(input).SetEquals(Vertices(r.Mesh)), sim.Name);
                if (sim.IsExact)
                    Assert.True(Vertices(box).SetEquals(Vertices(Invariance.MappedBack(r.Mesh, sim))), sim.Name);
            }
            Assert.True(ran >= 4, "similarities run: " + ran);
        }

        // a box with a cavity: two clean parts whose boxes meet, with a needle in the outer one. The whole is clean, so the input is returned as it is (#1337)
        static DMesh3 BoxWithCavity()
        {
            DMesh3 outer = Scaled(BoxWithSplitEdge(1e-3), 2);
            DMesh3 cavity = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            cavity.ReverseOrientation();
            return Union(outer, cavity);
        }

        [Fact]
        public void CleanWholeInput_PartsWhoseBoxesMeet_ReturnedUnchanged()
        {
            DMesh3 input = BoxWithCavity();
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(input));
            var r = new MeshRobustRepair(new DMesh3(input));
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.Equal(input.TriangleCount, r.PassedThroughTriangles);
            Assert.True(Vertices(input).SetEquals(Vertices(r.Mesh)));
            Assert.Equal(ResultChecks.Volume(input), ResultChecks.Volume(r.Mesh), 12);
            // under the similarities (2^k scales map back bit for bit); a second Apply() on the instance
            int ran = 0;
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 t = Invariance.Transformed(input, sim);
                if (MeshRobustRepair.NotCleanReason(t) != "")
                    continue;
                var rs = new MeshRobustRepair(new DMesh3(t));
                Assert.True(rs.Apply(), sim.Name);
                Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, rs.ResultPath);
                Assert.True(Vertices(t).SetEquals(Vertices(rs.Mesh)), sim.Name);
                if (sim.IsExact)
                    Assert.True(Vertices(input).SetEquals(Vertices(Invariance.MappedBack(rs.Mesh, sim))), sim.Name);
                ++ran;
            }
            Assert.True(ran >= 4, "similarities run: " + ran);
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            // with ConfigureRepair or PassThroughCleanParts off the repair runs
            var off = new MeshRobustRepair(new DMesh3(input)) { PassThroughCleanParts = false };
            Assert.True(off.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, off.ResultPath);
        }

        // ConfigureRepair asks for a change of a clean mesh (eg a tolerance): the repair runs; RepairF too; PassThroughCleanParts off too
        [Fact]
        public void CleanSingleComponent_ConfigureRepairOrOff_RunsTheRepair()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            var configured = new MeshRobustRepair(new DMesh3(box)) { ConfigureRepair = rep => rep.MinEdgeLengthTol = 0.01 };
            Assert.True(configured.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, configured.ResultPath);
            Assert.Equal(0, configured.PassedThroughTriangles);
            Assert.True(configured.Mesh.TriangleCount < box.TriangleCount);     // the short edge collapsed, as asked
            var off = new MeshRobustRepair(new DMesh3(box)) { PassThroughCleanParts = false };
            Assert.True(off.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, off.ResultPath);
        }

        // the input as it is: triangle groups and ids are kept (the pass through of one part is a copy of the input)
        [Fact]
        public void CleanSingleComponent_KeepsGroupsAndIds()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            box.EnableTriangleGroups();
            foreach (int t in box.TriangleIndices())
                box.SetTriangleGroup(t, 1 + t % 3);
            var r = new MeshRobustRepair(new DMesh3(box));
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.True(r.Mesh.HasTriangleGroups);
            foreach (int t in box.TriangleIndices()) {
                Assert.Equal(box.GetTriangle(t), r.Mesh.GetTriangle(t));
                Assert.Equal(box.GetTriangleGroup(t), r.Mesh.GetTriangleGroup(t));
            }
            foreach (int v in box.VertexIndices())
                Assert.Equal(box.GetVertex(v), r.Mesh.GetVertex(v));
        }

        // a second Apply() on the same instance: clean, then an unclean mesh (repaired, nothing passed), then clean again the result of the first (clean) is passed through again, unchanged
        [Fact]
        public void CleanSingleComponent_ApplyTwice_Reuse()
        {
            DMesh3 box = BoxWithSplitEdge(1e-3);
            var r = new MeshRobustRepair(new DMesh3(box));
            Assert.True(r.Apply());
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.True(Vertices(box).SetEquals(Vertices(r.Mesh)));
            r.Mesh = WithHole(Sphere(0, 1));
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
            Assert.Equal(0, r.PassedThroughTriangles);
            r.Mesh = new DMesh3(box);
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.Equal(box.TriangleCount, r.PassedThroughTriangles);
            Assert.True(Vertices(box).SetEquals(Vertices(r.Mesh)));
        }

        // more than 64 components are pieces, not parts: no part passes through on its own; a whole input that is clean still is returned as it is (#1337)
        [Fact]
        public void ManyComponents_NoPartPassThrough_CleanWholeIsUnchanged()
        {
            var parts = new List<DMesh3>();
            for (int i = 0; i < 70; ++i)
                parts.Add(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(10 * i, 0, 0), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh());
            DMesh3 clean = Union(parts.ToArray());
            var all = new MeshRobustRepair(new DMesh3(clean));
            Assert.True(all.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, all.ResultPath);
            Assert.Equal(clean.TriangleCount, all.PassedThroughTriangles);
            parts[0] = WithHole(parts[0]);
            var r = new MeshRobustRepair(Union(parts.ToArray()));
            r.Apply();
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        // a clean part far from a big one, with an open part next to the big one: before, the clean part was collapsed or eroded by the thresholds relative to
        // all triangles, now it passes through untouched at every scale
        [Theory]
        [InlineData(20, 12)]
        [InlineData(24, 20)]
        public void FarCleanPart_Survives_Untouched(int f, int r)
        {
            DMesh3 tiny = Sphere(0, 1, 4);       // (scaled by a power of two: exact, a sphere of that radius rounded to 2^-12 would be degenerate)
            foreach (int v in tiny.VertexIndices()) tiny.SetVertex(v, Math.Pow(2, -r) * tiny.GetVertex(v) + new Vector3d(Math.Pow(2, f), 0, 0));
            DMesh3 scene = Union(WithHole(Sphere(0, 1, 5)), Sphere(5, 1, 5), tiny);
            var on = Run(new DMesh3(scene));
            Assert.Equal(tiny.TriangleCount + Sphere(5, 1, 5).TriangleCount, on.PassedThroughTriangles);
            Assert.True(Vertices(tiny).IsSubsetOf(Vertices(on.Mesh)));
            var off = Run(new DMesh3(scene), passThrough: false);
            // (falsification: without stage 0 the tiny part is not in the result as it was)
            Assert.False(off.ResultReason == "" && Vertices(tiny).IsSubsetOf(Vertices(off.Mesh)));
        }

        // a clean tetrahedron 1e3 away from two overlapping spheres: the repaired spheres and the tetrahedron are not clean by the degenerate area of all triangles
        // (NotCleanReason), which the whole repair meets by its own cleanups: that result is used, Apply() stays true
        [Fact]
        public void UnionNotCleanByTheGlobalArea_WholeRepairDecides()
        {
            var r = new MeshRobustRepair(RobustRepairFarPartTests.Scene(1e3));
            Assert.True(r.Apply());
            Assert.True(r.PassThroughFellBack);
            Assert.Equal(0, r.PassedThroughTriangles);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        // a clean part between two open sheets: the box of the repaired sheets together contains it, but no sheet meets it, so it still passes through
        [Fact]
        public void PartBetweenTwoOthers_StillPassesThrough()
        {
            DMesh3 clean = Sphere(0, 1);
            var r = Run(Union(WithHole(Sphere(-10, 1, 5)), clean, WithHole(Sphere(10, 1, 5))));
            Assert.Equal(clean.TriangleCount, r.PassedThroughTriangles);
            Assert.False(r.PassThroughFellBack);
        }

        // reuse after a refused input: the counters describe the last Apply()
        [Fact]
        public void Counters_ResetByEveryApply()
        {
            var r = new MeshRobustRepair(Union(WithHole(Sphere(0, 1, 5)), Sphere(5, 1, 5))) { SolidifyResolution = 32 };
            r.Apply();
            Assert.True(r.PassedThroughTriangles > 0);
            DMesh3 bad = Sphere(0, 1);
            bad.SetVertex(0, new Vector3d(double.PositiveInfinity, 0, 0));
            r.Mesh = bad;
            Assert.False(r.Apply());
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        // 2^k scaling and the similarities: the same result up to the scale
        [Fact]
        public void ShiftSweep_SameResult()
        {
            DMesh3 scene = Union(WithHole(Sphere(0, 1, 5)), Sphere(5, 1, 5));
            var r0 = Run(new DMesh3(scene));
            Assert.True(r0.PassedThroughTriangles > 0);
            foreach (int s in new[] { -190, -90, -20, 7, 60, 150, 300, 380 }) {
                var r = Run(Scaled(scene, s));
                Assert.Equal(r0.ResultReason, r.ResultReason);
                Assert.Equal(r0.PassedThroughTriangles, r.PassedThroughTriangles);
                Assert.Equal("", Invariance.Difference(r0.Mesh, Invariance.MappedBack(r.Mesh, new Similarity("s", Math.Pow(2, s), Matrix3d.Identity, Vector3d.Zero))));
            }
        }

        [Fact]
        public void Similarities_SameResult()
        {
            DMesh3 scene = Union(WithHole(Sphere(0, 1, 5)), Sphere(5, 1, 5));
            var r0 = Run(new DMesh3(scene));
            foreach (Similarity sim in Similarity.All()) {
                var r = Run(Invariance.Transformed(scene, sim));
                Assert.Equal(r0.PassedThroughTriangles, r.PassedThroughTriangles);
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(r0.Mesh, Invariance.MappedBack(r.Mesh, sim)));
            }
        }

        // reuse: the result is repaired again unchanged; one instance twice
        [Fact]
        public void Reuse_ApplyTwice_Unchanged()
        {
            var rep = new MeshRobustRepair(Union(WithHole(Sphere(0, 1, 5)), Sphere(5, 1, 5))) { SolidifyResolution = 32 };
            Assert.True(rep.Apply());
            DMesh3 first = new DMesh3(rep.Mesh);
            rep.Mesh = new DMesh3(first);
            Assert.True(rep.Apply());
            Assert.Equal("", Invariance.Difference(first, rep.Mesh));
            Assert.Equal(first.TriangleCount, rep.PassedThroughTriangles);
        }

        // the repair of the others grows into a passed part (the solidify and the wrap change the geometry by about a cell): the input is then repaired as a
        // whole, with no part lost
        [Fact]
        public void RestGrowsIntoPassedPart_RepairedAsAWhole()
        {
            DMesh3 scene = Union(Sphere(0, 1), WithHole(Sphere(8, 0.5, 5)));
            bool hooked = false;
            var r = new MeshRobustRepair(scene) { SolidifyResolution = 32 };
            r.PassThroughTestHook = (m) => {
                if (hooked) return;
                hooked = true;
                Vector3d c = MeshRepairMetrics.TriangleBounds(m).Center;       // (the hook sees the working frame) onto the origin: into the box of the sphere
                foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) - c);
            };
            Assert.True(r.Apply());
            Assert.True(hooked);
            Assert.True(r.PassThroughFellBack);
            Assert.Equal(0, r.PassedThroughTriangles);
            Assert.True(r.Mesh.TriangleCount > 0);
        }
    }
}
