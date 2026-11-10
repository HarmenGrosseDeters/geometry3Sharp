using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The bowtie and degenerate-triangle checks of NotCleanReason / FindDefects run over blocks of ids in parallel on large
    /// meshes: a defect anywhere (first block, middle, last block, a gap in the id range) is found, a clean mesh is clean,
    /// and the answers equal the serial definitions.
    /// </summary>
    public class NotCleanParallelChecksTests
    {
        static DMesh3 BigSphere() => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 70 }.Generate().MakeDMesh();   // ~29k triangles, 15k vertices

        // two tetrahedra sharing the vertex tip (a bowtie), appended after the sphere: its vertex id is in the last block
        static void AddBowtie(DMesh3 m)
        {
            int tip = m.AppendVertex(new Vector3d(5, 0, 0));
            int a1 = m.AppendVertex(new Vector3d(6, 1, 0)), a2 = m.AppendVertex(new Vector3d(6, -1, 1)), a3 = m.AppendVertex(new Vector3d(6, -1, -1));
            int b1 = m.AppendVertex(new Vector3d(4, 1, 0)), b2 = m.AppendVertex(new Vector3d(4, -1, 1)), b3 = m.AppendVertex(new Vector3d(4, -1, -1));
            m.AppendTriangle(tip, a1, a2); m.AppendTriangle(tip, a2, a3); m.AppendTriangle(tip, a3, a1); m.AppendTriangle(a1, a3, a2);
            m.AppendTriangle(tip, b2, b1); m.AppendTriangle(tip, b3, b2); m.AppendTriangle(tip, b1, b3); m.AppendTriangle(b1, b2, b3);
        }

        static bool SerialBowtie(DMesh3 m) => MeshIterators.BowtieVertices(m).Any();

        [Fact]
        public void CleanSphere_IsClean_AndHasNoDefectsFlagged()
        {
            DMesh3 m = BigSphere();
            Assert.True(m.MaxVertexID > 16384 && m.MaxTriangleID > 16384);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.Equal(MeshRobustRepair.Defects.None, MeshRobustRepair.FindDefects(m));
        }

        [Fact]
        public void BowtieInTheLastBlock_IsFound()
        {
            DMesh3 m = BigSphere();
            AddBowtie(m);
            Assert.True(SerialBowtie(m));
            Assert.Equal("bowties", MeshRobustRepair.NotCleanReason(m, false));
            Assert.True(MeshRobustRepair.FindDefects(m, false).HasFlag(MeshRobustRepair.Defects.Bowties));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void DegenerateTriangleAtAnyPlace_IsFound(int where)
        {
            DMesh3 m = BigSphere();
            int[] tris = m.TriangleIndices().ToArray();
            int t = where == 0 ? tris[0] : where == 1 ? tris[tris.Length / 2] : where == 2 ? tris[tris.Length - 1] : tris[tris.Length - 2];
            Index3i tv = m.GetTriangle(t);
            m.SetVertex(tv.b, m.GetVertex(tv.a));      // zero area
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(m, false));
            Assert.True(MeshRobustRepair.FindDefects(m, false).HasFlag(MeshRobustRepair.Defects.DegenerateTriangles));
        }

        [Fact]
        public void IdGapsFromRemovedTriangles_DoNotHideOrInventDefects()
        {
            DMesh3 m = BigSphere();
            int[] tris = m.TriangleIndices().ToArray();
            // a hole: the mesh is not closed, but has no bowtie and no degenerate triangle among the live ones
            for (int i = 0; i < tris.Length; i += 997)
                m.RemoveTriangle(tris[i], true);
            Assert.False(SerialBowtie(m));
            var d = MeshRobustRepair.FindDefects(m, false);
            Assert.False(d.HasFlag(MeshRobustRepair.Defects.Bowties));
            Assert.False(d.HasFlag(MeshRobustRepair.Defects.DegenerateTriangles));
            Assert.True(d.HasFlag(MeshRobustRepair.Defects.NotClosed));
        }

        // runs f with Parallel.For limited to n concurrent tasks (Parallel.For uses the current TaskScheduler)
        static T WithThreads<T>(int n, Func<T> f)
        {
            var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, n).ConcurrentScheduler;
            return Task.Factory.StartNew(f, CancellationToken.None, TaskCreationOptions.None, scheduler).GetAwaiter().GetResult();
        }

        [Fact]
        public void SeveralDefectsInDifferentBlocks_SameReportAsSerial_UnderOneTwoEightThreads_Repeated()
        {
            // a zero-area triangle in the first and in the last block, a bowtie in the last one: the report (the flags, and
            // the reason string with its fixed order) is the one of the serial scan, whichever block finishes first
            DMesh3 m = BigSphere();
            int[] tris = m.TriangleIndices().ToArray();
            foreach (int t in new[] { tris[3], tris[tris.Length / 2], tris[tris.Length - 3] }) {
                Index3i tv = m.GetTriangle(t);
                m.SetVertex(tv.b, m.GetVertex(tv.a));
            }
            AddBowtie(m);
            MeshRobustRepair.Defects serial;
            gParallel.SerialOnThisThread = true;
            try { serial = MeshRobustRepair.FindDefects(m, false); } finally { gParallel.SerialOnThisThread = false; }
            Assert.True(serial.HasFlag(MeshRobustRepair.Defects.Bowties) && serial.HasFlag(MeshRobustRepair.Defects.DegenerateTriangles));
            string reason = MeshRobustRepair.NotCleanReasons(m, false);
            foreach (int threads in new[] { 1, 2, 8 })
                for (int rep = 0; rep < 6; ++rep) {
                    Assert.Equal(serial, WithThreads(threads, () => MeshRobustRepair.FindDefects(m, false)));
                    Assert.Equal(reason, WithThreads(threads, () => MeshRobustRepair.NotCleanReasons(m, false)));
                    Assert.Equal("bowties", WithThreads(threads, () => MeshRobustRepair.NotCleanReason(m, false)));
                }
        }
    }
}
