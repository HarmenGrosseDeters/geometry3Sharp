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

        // more than 64 components are pieces, not parts: nothing passes
        [Fact]
        public void ManyComponents_NoPassThrough()
        {
            var parts = new List<DMesh3>();
            for (int i = 0; i < 70; ++i)
                parts.Add(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(10 * i, 0, 0), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh());
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
