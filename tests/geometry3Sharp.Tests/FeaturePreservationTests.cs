using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer and Remesher must keep the outer boundary ring (including its corners) and sharp feature
    /// edges of a mesh.
    /// </summary>
    public class FeaturePreservationTests
    {
        // flat L-shape in the XZ plane: 10x10 square without the (+x,+z) quadrant, 0.5 grid spacing
        static DMesh3 LShape()
        {
            var gen = new GriddedRectGenerator() { Width = 10, Height = 10, EdgeVertices = 21 };
            DMesh3 mesh = gen.Generate().MakeDMesh();
            foreach (int tid in mesh.TriangleIndices().ToArray()) {
                Vector3d c = mesh.GetTriCentroid(tid);
                if (c.x > 0 && c.z > 0)
                    mesh.RemoveTriangle(tid, true, false);
            }
            return mesh;
        }
        static readonly Vector3d[] LCorners = {
            new Vector3d(-5, 0, -5), new Vector3d(5, 0, -5), new Vector3d(5, 0, 0),
            new Vector3d(0, 0, 0), new Vector3d(0, 0, 5), new Vector3d(-5, 0, 5) };

        // box with a fine grid on the faces
        static DMesh3 GridBox(int edgeVertices)
        {
            var gen = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, 5 * Vector3d.One), EdgeVertices = edgeVertices };
            return gen.Generate().MakeDMesh();
        }

        // same box, but the vertices are moved randomly within their face planes / along their edges:
        // exact geometry, irregular triangle sizes (like CAD exports, small triangles along the edges)
        static DMesh3 IrregularGridBox(int edgeVertices, int seed)
        {
            DMesh3 mesh = GridBox(edgeVertices);
            var rnd = new Random(seed);
            double spacing = 10.0 / (edgeVertices - 1);
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d p = mesh.GetVertex(vid);
                for (int k = 0; k < 3; ++k)
                    if (Math.Abs(Math.Abs(p[k]) - 5) > 1e-6)
                        p[k] += (rnd.NextDouble() - 0.5) * 0.9 * spacing;
                mesh.SetVertex(vid, p);
            }
            return mesh;
        }

        static double Area(DMesh3 mesh) { return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).y; }
        static double Volume(DMesh3 mesh) { return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x; }

        static double MinDistance(DMesh3 mesh, Vector3d p)
        {
            return mesh.VertexIndices().Min(v => mesh.GetVertex(v).Distance(p));
        }

        // distance of every boundary vertex of 'mesh' to the boundary edges of 'original'
        static double MaxBoundaryDeviation(DMesh3 mesh, DMesh3 original)
        {
            var segments = MeshIterators.BoundaryEdges(original).Select(e => {
                Index2i ev = original.GetEdgeV(e);
                return new Segment3d(original.GetVertex(ev.a), original.GetVertex(ev.b));
            }).ToList();
            double max = 0;
            foreach (int eid in MeshIterators.BoundaryEdges(mesh)) {
                Index2i ev = mesh.GetEdgeV(eid);
                foreach (int vid in new[] { ev.a, ev.b })
                    max = Math.Max(max, segments.Min(s => s.DistanceSquared(mesh.GetVertex(vid))));
            }
            return Math.Sqrt(max);
        }

        static double MaxSurfaceDeviation(DMesh3 mesh, DMesh3 original)
        {
            var spatial = new DMeshAABBTree3(original, true);
            return mesh.VertexIndices().Max(v => {
                Vector3d p = mesh.GetVertex(v);
                return Math.Sqrt(MeshQueries.TriDistanceSqr(original, spatial.FindNearestTriangle(p), p));
            });
        }


        [Fact]
        public void Reducer_PlanarLShape_KeepsOuterRingCorners()
        {
            DMesh3 original = LShape();
            DMesh3 mesh = new DMesh3(original);
            new Reducer(mesh).ReduceToTriangleCount(20);

            Assert.True(mesh.TriangleCount <= 20);
            foreach (Vector3d c in LCorners)
                Assert.True(MinDistance(mesh, c) < 1e-6, $"corner {c} lost");
            Assert.InRange(MaxBoundaryDeviation(mesh, original), 0, 1e-6);
            Assert.InRange(Area(mesh), 75 - 1e-6, 75 + 1e-6);
        }

        [Fact]
        public void Reducer_FeatureAngle_KeepsBoxEdgesAndCorners()
        {
            // without feature quadrics this loses a corner and 20% of the volume
            DMesh3 original = IrregularGridBox(12, 1);
            DMesh3 mesh = new DMesh3(original);
            new Reducer(mesh) { FeatureAngle = 30 }.ReduceToTriangleCount(60);

            Assert.True(mesh.TriangleCount <= 60);
            foreach (Vector3d c in new Box3d(Vector3d.Zero, 5 * Vector3d.One).ComputeVertices())
                Assert.True(MinDistance(mesh, c) < 1e-6, $"corner {c} lost");
            Assert.InRange(MaxSurfaceDeviation(mesh, original), 0, 1e-6);   // generators use float coordinates
            Assert.InRange(Volume(mesh), 1000 - 1e-6, 1000 + 1e-6);
        }

        [Fact]
        public void Remesher_PlanarLShape_WithFeatureConstraints_KeepsOuterRing()
        {
            DMesh3 original = LShape();
            DMesh3 mesh = new DMesh3(original);
            var remesher = new Remesher(mesh);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, 30);
            remesher.SetTargetEdgeLength(1.3);
            for (int k = 0; k < 20; ++k)
                remesher.BasicRemeshPass();

            Assert.True(mesh.VertexCount < original.VertexCount / 2, "remeshing did not coarsen the mesh");
            foreach (Vector3d c in LCorners)
                Assert.True(MinDistance(mesh, c) < 1e-6, $"corner {c} lost");
            Assert.InRange(MaxBoundaryDeviation(mesh, original), 0, 1e-6);
            Assert.InRange(Area(mesh), 75 - 1e-4, 75 + 1e-4);
        }

        [Fact]
        public void Remesher_Box_WithFeatureConstraints_KeepsEdgesAndCorners()
        {
            DMesh3 original = GridBox(6);
            DMesh3 mesh = new DMesh3(original);
            var remesher = new Remesher(mesh);
            remesher.SetProjectionTarget(MeshProjectionTarget.Auto(original));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, 30);
            remesher.SetTargetEdgeLength(0.8);
            for (int k = 0; k < 15; ++k)
                remesher.BasicRemeshPass();

            Assert.True(mesh.VertexCount > 2 * original.VertexCount, "remeshing did not refine the mesh");
            foreach (Vector3d c in new Box3d(Vector3d.Zero, 5 * Vector3d.One).ComputeVertices())
                Assert.True(MinDistance(mesh, c) < 1e-6, $"corner {c} lost");
            Assert.InRange(MaxSurfaceDeviation(mesh, original), 0, 1e-6);
            Assert.InRange(Volume(mesh), 1000 - 0.01, 1000 + 0.01);
        }

        [Fact]
        public void MeshTopology_FindsFoldedEdges()
        {
            // two triangles folded onto each other along a shared edge (normals opposite)
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0.5, 1, 0)), d = mesh.AppendVertex(new Vector3d(0.5, 1, 0.001));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(b, a, d);
            var topo = new MeshTopology(mesh) { CreaseAngle = 30 };
            topo.Compute();
            Assert.Contains(mesh.FindEdge(a, b), topo.CreaseEdges);
        }
    }
}
