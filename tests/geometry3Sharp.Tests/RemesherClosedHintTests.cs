using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer / Remesher 2.x, T4 speed: the Remesher's flip test asked DMesh3.IsBoundaryVertex four times per edge on
    /// every mesh, because the closed-mesh hint was only set by an explicit Precompute() call, and each ask allocated an
    /// iterator (about 260 bytes per edge: 0.7 GB for 20 passes over 95k triangles). Now a pass detects a closed mesh by
    /// itself and IsBoundaryVertex does not allocate. The results do not change (no vertex of a closed mesh is a
    /// boundary vertex).
    /// </summary>
    public class RemesherClosedHintTests
    {
        static DMesh3 Sphere(int edgeVertices = 30)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 10, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        // a ring of n triangles around a centre vertex (valence n: the list of vertex edges spills past one block),
        // open (a fan) or closed (a bipyramid: a second ring below)
        static DMesh3 Pyramid(int n, bool closed)
        {
            var m = new DMesh3();
            int c = m.AppendVertex(new Vector3d(0, 0, 1));
            int[] ring = new int[n];
            for (int i = 0; i < n; ++i)
                ring[i] = m.AppendVertex(new Vector3d(Math.Cos(2 * Math.PI * i / n), Math.Sin(2 * Math.PI * i / n), 0));
            for (int i = 0; i < n; ++i)
                m.AppendTriangle(c, ring[i], ring[(i + 1) % n]);
            if (closed) {
                int c2 = m.AppendVertex(new Vector3d(0, 0, -1));
                for (int i = 0; i < n; ++i)
                    m.AppendTriangle(c2, ring[(i + 1) % n], ring[i]);
            }
            return m;
        }

        static double MeanEdge(DMesh3 m)
        {
            double sum = 0; int n = 0;
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                sum += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); n++;
            }
            return sum / n;
        }

        static bool ByDefinition(DMesh3 m, int vid)
        {
            foreach (int eid in m.VtxEdgesItr(vid))
                if (m.IsBoundaryEdge(eid))
                    return true;
            return false;
        }

        [Fact]
        public void IsBoundaryVertex_MatchesDefinition_AndSpilledLists()
        {
            DMesh3 holed = Sphere();
            foreach (int tid in holed.TriangleIndices().Where(t => t % 37 == 0).ToArray())
                holed.RemoveTriangle(tid);
            DMesh3 late = Pyramid(20, true);        // the centre's boundary spokes are among the last of its 20 (the spilled part of the list)
            late.RemoveTriangle(17);
            Assert.True(late.IsBoundaryVertex(0));
            var meshes = new[] { Sphere(), holed, Pyramid(5, false), Pyramid(20, false), Pyramid(20, true), Pyramid(3, true), late };
            int boundary = 0, spilled = 0;
            foreach (DMesh3 m in meshes) {
                foreach (int vid in m.VertexIndices()) {
                    bool expected = ByDefinition(m, vid);
                    Assert.Equal(expected, m.IsBoundaryVertex(vid));
                    if (expected) boundary++;
                    if (m.GetVtxEdgeCount(vid) > 8) spilled++;
                }
            }
            Assert.True(boundary > 0, "the open meshes have boundary vertices");
            Assert.True(spilled >= 2, "vertices whose edge list spilled past one block were tested");
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void IsBoundaryVertex_DoesNotAllocate()
        {
            DMesh3 m = Pyramid(20, false);
            int sum = 0;
            long bytes = Allocations.Least(100, () => {
                for (int vid = 0; vid < m.MaxVertexID; ++vid)
                    if (m.IsBoundaryVertex(vid)) sum++;
            });
            Assert.True(sum > 0);
            Assert.True(bytes == 0, bytes + " bytes allocated");
        }
#endif

        // one pass of the edge tests (flips only, as the flip test is what asked for the boundary vertices; splits,
        // collapses and smoothing allocate for their own reasons), serial: bytes per edge
        static double BytesPerEdgeOfFlipPass(DMesh3 mesh)
        {
            var r = new Remesher(mesh) { EnableSplits = false, EnableCollapses = false, EnableSmoothing = false, EnableParallelSmooth = false, RejectInvertingPasses = false /* (copies the mesh per pass) */ };
            r.SetTargetEdgeLength(1.2 * MeanEdge(mesh));
            r.BasicRemeshPass();        // (warm-up)
            int edges = mesh.EdgeCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            r.BasicRemeshPass();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)edges;
        }

        static DMesh3 RandomValences(DMesh3 mesh)
        {
            // flips at random edges: valences far from 6, so the flip test has candidates
            var rand = new Random(5);
            int[] eids = mesh.EdgeIndices().ToArray();
            for (int k = 0; k < eids.Length / 4; ++k) {
                DMesh3.EdgeFlipInfo info;
                mesh.FlipEdge(eids[rand.Next(eids.Length)], out info);
            }
            return mesh;
        }

        [Fact]
        public void Remesher_FlipPassOverClosedMesh_DoesNotAllocatePerEdge()
        {
            // (the old code: about 260 bytes per edge from the boundary-vertex iterators)
            double bytes = BytesPerEdgeOfFlipPass(RandomValences(Sphere(40)));
            Assert.True(bytes < 20, bytes + " bytes per edge");
        }

        [Fact]
        public void Remesher_FlipPassOverOpenMesh_DoesNotAllocatePerEdge()
        {
            DMesh3 m = Sphere(40);
            foreach (int tid in m.TriangleIndices().Where(t => t % 53 == 0).ToArray())
                m.RemoveTriangle(tid);
            double bytes = BytesPerEdgeOfFlipPass(RandomValences(m));
            Assert.True(bytes < 20, bytes + " bytes per edge");
        }

        static string Bits(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(m.TriangleCount).Append(':').Append(m.VertexCount);
            long h = 1469598103934665603L;
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                h = (h ^ BitConverter.DoubleToInt64Bits(v.x)) * 1099511628211L;
                h = (h ^ BitConverter.DoubleToInt64Bits(v.y)) * 1099511628211L;
                h = (h ^ BitConverter.DoubleToInt64Bits(v.z)) * 1099511628211L;
            }
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                h = (h ^ t.a) * 1099511628211L; h = (h ^ t.b) * 1099511628211L; h = (h ^ t.c) * 1099511628211L;
            }
            return sb.Append(':').Append(h).ToString();
        }

        static DMesh3 Run(DMesh3 input, bool precompute, bool pro)
        {
            DMesh3 m = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(m) : new Remesher(m);
            r.EnableParallelSmooth = false; r.EnableParallelProjection = false; r.SmoothSpeedT = 0.5;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryLoops(r);
            r.SetTargetEdgeLength(0.8 * MeanEdge(input));
            if (precompute) r.Precompute();
            if (pro) ((RemesherPro)r).FastestRemesh(8);
            else for (int k = 0; k < 6; ++k) r.BasicRemeshPass();
            return m;
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DetectedHint_GivesTheResultOfAnExplicitPrecompute(bool open, bool pro)
        {
            DMesh3 input = Sphere(24);
            if (open)
                foreach (int tid in input.TriangleIndices().Where(t => t % 41 == 0).ToArray())
                    input.RemoveTriangle(tid);
            Assert.Equal(!open, input.IsClosed());
            string detected = Bits(Run(input, false, pro));
            Assert.Equal(detected, Bits(Run(input, true, pro)));
            Assert.Equal(detected, Bits(Run(input, false, pro)));       // (reuse of the code path: repeatable)
        }

        // flips only, 3 passes over a sphere with random flips and holes: boundary vertices must keep their valence
        // as target (the mesh is open, so the closed hint must not be set). The expected value is from the code before
        // the closed hint was detected by the pass (where the boundary was always tested), and the same for the closed sphere.
        static string FlipPasses(bool open)
        {
            DMesh3 m = Sphere(20);
            if (open)
                foreach (int tid in m.TriangleIndices().Where(t => t % 11 == 0).ToArray())
                    m.RemoveTriangle(tid);
            RandomValences(m);
            var r = new Remesher(m) { EnableSplits = false, EnableCollapses = false, EnableSmoothing = false, RejectInvertingPasses = false };
            r.SetTargetEdgeLength(1.2 * MeanEdge(m));
            for (int k = 0; k < 3; ++k)
                r.BasicRemeshPass();
            return Bits(m);
        }

        [Fact]
        public void Remesher_FlipPasses_GiveTheResultOfTheBoundaryTest()
        {
            Assert.Equal("3938:2168:-5256502609165496273", FlipPasses(true));
            Assert.Equal("4332:2168:-56732057121605259", FlipPasses(false));
        }

        [Fact]
        public void Remesher_ReusedAfterTheMeshGotAHole_DetectsTheBoundaryAgain()
        {
            // the hint is per pass: a mesh opened between two passes must not keep the closed hint of the first
            // (the reference gets the same history and calls Precompute() after the edit, as the documentation asked)
            DMesh3 Go(bool precomputeAfterEdit)
            {
                DMesh3 m = Sphere(24);
                var r = new Remesher(m) { EnableParallelSmooth = false, SmoothSpeedT = 0.5 };
                r.SetTargetEdgeLength(0.8 * MeanEdge(m));
                r.Precompute();         // closed
                r.BasicRemeshPass();
                foreach (int tid in m.TriangleIndices().Where(t => t % 29 == 0).ToArray())
                    m.RemoveTriangle(tid);
                Assert.False(m.IsClosed());
                if (precomputeAfterEdit) r.Precompute();
                for (int k = 0; k < 3; ++k)
                    r.BasicRemeshPass();
                return m;
            }
            Assert.Equal(Bits(Go(true)), Bits(Go(false)));
        }
    }
}
