using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Non-orientable regions in the repair: OrientComponents leaves a short seam, MeshEditor.CutAlongEdges
    /// cuts along it, and MeshAutoRepair fills the holes at such a seam instead of eroding the mesh.
    /// </summary>
    public class MeshRepairSeamTests
    {
        // Moebius band: n quads around, m across; the band closes with a half twist, so whatever the
        // orientation, m edges (or more) are inconsistent. Triangles are oriented consistently except at
        // the closing column, where the seam is m edges.
        static DMesh3 Moebius(int n, int m, double radius = 3, double width = 1)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, m + 1];
            for (int i = 0; i < n; ++i) {
                double u = 2 * Math.PI * i / n;
                for (int j = 0; j <= m; ++j) {
                    double v = width * (2.0 * j / m - 1);
                    double r = radius + v * Math.Cos(u / 2);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(r * Math.Cos(u), r * Math.Sin(u), v * Math.Sin(u / 2)));
                }
            }
            for (int i = 0; i < n; ++i) {
                int i1 = (i + 1) % n;
                for (int j = 0; j < m; ++j) {
                    // after the full turn, column 0 is reached upside down
                    int a = vid[i, j], b = vid[i, j + 1];
                    int c = (i1 == 0) ? vid[0, m - j] : vid[i1, j];
                    int d = (i1 == 0) ? vid[0, m - j - 1] : vid[i1, j + 1];
                    Assert.True(mesh.AppendTriangle(a, c, d) >= 0);
                    Assert.True(mesh.AppendTriangle(a, d, b) >= 0);
                }
            }
            return mesh;
        }

        static int InconsistentEdges(DMesh3 m)
        {
            return m.EdgeIndices().Count(eid => {
                Index2i et = m.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) return false;
                Index2i ev = m.GetEdgeV(eid);
                return (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.a)) >= 0)
                    == (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.b)) >= 0);
            });
        }

        [Fact]
        public void OrientComponents_Moebius_KeepsTheShortSeam()
        {
            // a long, wide band with 10 % of its triangles reversed: the orientation is rebuilt, and the
            // seam must not be longer than the shortest one (m edges across); a depth-first propagation
            // from a single triangle closes the cycle wherever its search happens to meet itself
            var rnd = new Random(3);
            for (int trial = 0; trial < 5; ++trial) {
                DMesh3 mesh = Moebius(60, 8);
                Assert.Equal(8, InconsistentEdges(mesh));
                foreach (int tid in mesh.TriangleIndices().ToList())
                    if (rnd.NextDouble() < 0.1)
                        mesh.ReverseTriOrientation(tid);
                new MeshRepairOrientation(mesh).OrientComponents();
                Assert.Equal(8, InconsistentEdges(mesh));
            }
        }

        [Fact]
        public void OrientComponents_ManyRandomFlips_SeamSmoothed()
        {
            // with 30 % of the triangles reversed the patches are small and the spanning tree leaves a jagged
            // seam; flipping triangles with two or three inconsistent edges shortens it (without: 69 edges
            // over these five bands, at least 8 each)
            var rnd = new Random(3);
            int total = 0;
            for (int trial = 0; trial < 5; ++trial) {
                DMesh3 mesh = Moebius(60, 8);
                foreach (int tid in mesh.TriangleIndices().ToList())
                    if (rnd.NextDouble() < 0.3)
                        mesh.ReverseTriOrientation(tid);
                new MeshRepairOrientation(mesh).OrientComponents();
                int n = InconsistentEdges(mesh);
                Assert.True(n >= 8);
                total += n;
            }
            Assert.True(total <= 60, $"{total} inconsistent edges");
        }

        [Fact]
        public void OrientComponents_OrientableWithFlippedPatches_Consistent()
        {
            // a sphere with reversed triangles and reversed patches: orientable, so all consistent afterwards
            var rnd = new Random(5);
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            double vol = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
            foreach (int tid in mesh.TriangleIndices().ToList())
                if (rnd.NextDouble() < 0.2 || mesh.GetTriCentroid(tid).x > 0.5)
                    mesh.ReverseTriOrientation(tid);
            Assert.True(InconsistentEdges(mesh) > 0);
            var orient = new MeshRepairOrientation(mesh);
            orient.OrientComponents();
            Assert.Equal(0, InconsistentEdges(mesh));
            orient.SolveGlobalOrientation();
            Assert.Equal(vol, MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x, 6);
        }

        [Fact]
        public void CutAlongEdges_SplitsGridIntoTwo()
        {
            // a 4x4 grid cut along its middle row of edges (boundary to boundary): two pieces, the cut edges
            // become boundary edges on both sides
            DMesh3 mesh = new DMesh3();
            int[,] v = new int[5, 5];
            for (int i = 0; i < 5; ++i)
                for (int j = 0; j < 5; ++j)
                    v[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) {
                    mesh.AppendTriangle(v[i, j], v[i + 1, j], v[i + 1, j + 1]);
                    mesh.AppendTriangle(v[i, j], v[i + 1, j + 1], v[i, j + 1]);
                }
            int boundary = MeshTestUtil.BoundaryEdgeCount(mesh);
            var cut = Enumerable.Range(0, 4).Select(i => mesh.FindEdge(v[i, 2], v[i + 1, 2])).ToList();
            int added = new MeshEditor(mesh).CutAlongEdges(cut);
            Assert.Equal(5, added);
            Assert.Equal(boundary + 8, MeshTestUtil.BoundaryEdgeCount(mesh));
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Empty(MeshIterators.BowtieVertices(mesh));
        }

        [Fact]
        public void CutAlongEdges_SingleInteriorEdge_NothingToCut()
        {
            // an edge with both endpoints inside the mesh cannot be opened without a hole around it
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 5 }.Generate().MakeDMesh();
            int eid = mesh.EdgeIndices().First();
            Assert.Equal(0, new MeshEditor(mesh).CutAlongEdges(new[] { eid }));
            Assert.True(mesh.IsClosed());
        }

        [Fact]
        public void AutoRepair_ClosedNonOrientable_CutAndClosedAgain()
        {
            // a Moebius band closed by a fan from its boundary to an apex: closed (every edge has two triangles),
            // but not orientable, so not closed in the winding number sense. The repair skips the hole filling
            // (nothing is open) and cuts the seam at the end: both sides of the cut are closed separately
            DMesh3 mesh = Moebius(30, 3);
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Single(loops.Loops);
            int[] loop = loops.Loops[0].Vertices;
            int apex = mesh.AppendVertex(new Vector3d(0, 0, 3));
            for (int i = 0; i < loop.Length; ++i)
                Assert.True(mesh.AppendTriangle(loop[i], loop[(i + 1) % loop.Length], apex) >= 0);
            Assert.True(mesh.IsClosed());
            Assert.True(InconsistentEdges(mesh) > 0);
            var repair = new MeshAutoRepair(mesh);
            var stages = new List<string>();
            repair.OnStage = (name, m) => stages.Add(name);
            Assert.True(repair.Apply());
            DMesh3 r = repair.Mesh;
            Assert.Contains("cut_seams_final", stages);
            Assert.True(r.IsClosed());
            Assert.Equal(0, InconsistentEdges(r));
            Assert.True(ExactMeshBoolean.IsWindingClosed(r));
        }

        [Fact]
        public void AutoRepair_HoleAtOrientationSeam_FilledWithoutErosion()
        {
            // the boundary of a Moebius band runs against its orientation at the seam, so the old repair did
            // not fill it and eroded the band instead. Cut along the seam, the band is a strip: its boundary
            // is one consistent loop, which is filled; all band triangles are kept
            DMesh3 mesh = Moebius(30, 3);
            var input = new HashSet<Vector3d>(mesh.Vertices());
            int n = mesh.TriangleCount;
            var repair = new MeshAutoRepair(mesh);
            var stages = new List<string>();
            repair.OnStage = (name, m) => stages.Add(name);
            Assert.True(repair.Apply());
            DMesh3 r = repair.Mesh;
            Assert.DoesNotContain("erosion", stages);
            Assert.Contains("cut_seams", stages);
            Assert.True(r.IsClosed());
            Assert.Equal(0, InconsistentEdges(r));
            Assert.True(ExactMeshBoolean.IsWindingClosed(r));
            Assert.True(r.TriangleCount >= n);
            Assert.True(input.All(p => r.Vertices().Contains(p)));
        }
    }
}
