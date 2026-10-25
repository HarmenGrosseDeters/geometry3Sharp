using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Parallel smoothing must give the same result as sequential smoothing. The "modified" flags of the
    /// vertex buffer were a BitArray: concurrent writes to bits of the same word lost flags, so some
    /// smoothed positions were randomly dropped.
    /// </summary>
    public class RemesherDeterminismTests
    {
        static DMesh3 NoisySphere()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 10, EdgeVertices = 120 }.Generate().MakeDMesh();
            Random rand = new Random(17);
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d d = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                mesh.SetVertex(vid, mesh.GetVertex(vid) + 0.05 * d);
            }
            return mesh;
        }

        static void AssertIdentical(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            int nDifferent = 0;
            foreach (int vid in a.VertexIndices())
                if (a.GetVertex(vid) != b.GetVertex(vid))
                    nDifferent++;
            Assert.True(nDifferent == 0, nDifferent + " vertices differ");
        }

        static DMesh3 RemeshSmoothOnly(bool parallel)
        {
            DMesh3 mesh = NoisySphere();
            Remesher r = new Remesher(mesh) {
                EnableFlips = false, EnableCollapses = false, EnableSplits = false,
                EnableParallelSmooth = parallel, SmoothSpeedT = 0.5
            };
            r.SetTargetEdgeLength(0.3);
            for (int k = 0; k < 5; ++k)
                r.BasicRemeshPass();
            return mesh;
        }

        static DMesh3 Remesh(bool parallel)
        {
            DMesh3 mesh = NoisySphere();
            Remesher r = new Remesher(mesh) { EnableParallelSmooth = parallel, SmoothSpeedT = 0.5 };
            r.SetTargetEdgeLength(0.4);
            for (int k = 0; k < 4; ++k)
                r.BasicRemeshPass();
            return mesh;
        }

        [Fact]
        public void Remesher_ParallelSmoothing_MatchesSequential()
        {
            DMesh3 sequential = RemeshSmoothOnly(false);
            for (int run = 0; run < 3; ++run)
                AssertIdentical(sequential, RemeshSmoothOnly(true));
        }

        [Fact]
        public void Remesher_ParallelRemesh_IsDeterministic()
        {
            DMesh3 sequential = Remesh(false);
            for (int run = 0; run < 2; ++run)
                AssertIdentical(sequential, Remesh(true));
        }

        static DMesh3 RemesherProSmoothOnly(bool parallel)
        {
            DMesh3 mesh = NoisySphere();
            RemesherPro r = new RemesherPro(mesh) {
                EnableFlips = false, EnableCollapses = false, EnableSplits = false,
                EnableParallelSmooth = parallel, SmoothSpeedT = 0.5
            };
            r.SetTargetEdgeLength(0.3);
            r.FastestRemesh(5, false);
            return mesh;
        }

        [Fact]
        public void RemesherPro_ParallelSmoothing_MatchesSequential()
        {
            DMesh3 sequential = RemesherProSmoothOnly(false);
            for (int run = 0; run < 3; ++run)
                AssertIdentical(sequential, RemesherProSmoothOnly(true));
        }
    }
}
