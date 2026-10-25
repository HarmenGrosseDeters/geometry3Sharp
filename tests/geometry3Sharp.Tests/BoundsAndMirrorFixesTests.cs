using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Decisions left open by the low-severity round (docs/Analyse-2026-10.md): bounds of an empty mesh,
    /// mirroring scale, and FlipLeftRightCoordSystems for frames.
    /// </summary>
    public class BoundsAndMirrorFixesTests
    {
        static DMesh3 Sphere()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshNormals.QuickCompute(mesh);
            return mesh;
        }

        static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

        // --- DMesh3.GetBounds on an empty mesh ---

        [Fact]
        public void GetBounds_EmptyMesh_IsEmptyBox()
        {
            // a zero box at the origin pulled the origin into every bounds it was combined with
            var mesh = new DMesh3();
            AxisAlignedBox3d b = mesh.GetBounds();
            Assert.True(b.Max.x < b.Min.x, $"bounds of an empty mesh: {b}");
            AxisAlignedBox3d combined = new AxisAlignedBox3d(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6));
            combined.Contain(mesh.CachedBounds);
            Assert.Equal(new Vector3d(5, 5, 5), combined.Min);
        }

        [Fact]
        public void GetBounds_NonEmptyMeshUnchanged()
        {
            DMesh3 mesh = Sphere();
            MeshTransforms.Translate(mesh, new Vector3d(3, -2, 1));
            AxisAlignedBox3d b = mesh.GetBounds();
            var verts = mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList();
            var min = new Vector3d(verts.Min(v => v.x), verts.Min(v => v.y), verts.Min(v => v.z));
            var max = new Vector3d(verts.Max(v => v.x), verts.Max(v => v.y), verts.Max(v => v.z));
            Assert.True(b.Min == min && b.Max == max, $"{b}");
        }

        // the grid classes size their grid from the bounds: an empty mesh is rejected with a clear exception
        // (with the Empty box the grid size would overflow)
        [Fact]
        public void GridClasses_EmptyMesh_ThrowArgumentException()
        {
            var empty = new DMesh3();
            Assert.Throws<ArgumentException>(() => new MeshSignedDistanceGrid(empty, 0.1).Compute());
            Assert.Throws<ArgumentException>(() => new MeshWindingNumberGrid(empty, new DMeshAABBTree3(empty, true), 0.1).Compute());
            Assert.Throws<ArgumentException>(() => new MeshScalarSamplingGrid(empty, 0.1, p => 0).Compute());
            Assert.Throws<ArgumentException>(() => new CachingMeshSDF(empty, 0.1, new DMeshAABBTree3(empty, true)).Initialize());
        }

        // --- MeshTransforms.Scale with a mirroring scale ---

        [Fact]
        public void Scale_Mirror_WithFixOrientation_KeepsVolumeAndNormalsOutward()
        {
            DMesh3 mesh = Sphere();
            double vol = Volume(mesh);
            // the opt-in overload Scale(mesh, scale, origin, bFixOrientation), called by reflection so that the
            // other tests still compile against the code without it
            var scaleFix = typeof(MeshTransforms).GetMethod("Scale", new[] { typeof(IDeformableMesh), typeof(Vector3d), typeof(Vector3d), typeof(bool) });
            Assert.NotNull(scaleFix);
            scaleFix.Invoke(null, new object[] { mesh, new Vector3d(-2, 1, 1), Vector3d.Zero, true });
            Assert.Equal(2 * vol, Volume(mesh), 6);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                Vector3d n = mesh.GetVertexNormal(vid);
                // outward: the normal of the ellipsoid x^2/4 + y^2 + z^2 = 1 is (x/4, y, z)
                Vector3d expected = new Vector3d(p.x / 4, p.y, p.z).Normalized;
                Assert.True(n.Dot(expected) > 0.9, $"vertex {vid}: normal {n}, expected {expected}");
            }
        }

        [Fact]
        public void Scale_Mirror_DefaultUnchanged()
        {
            // without the flag Scale only moves the vertices, as before (callers may fix orientation themselves)
            DMesh3 mesh = Sphere();
            double vol = Volume(mesh);
            Vector3f n0 = mesh.GetVertexNormal(0);
            MeshTransforms.Scale(mesh, new Vector3d(-1, 1, 1), Vector3d.Zero);
            Assert.Equal(-vol, Volume(mesh), 6);
            Assert.Equal(n0, mesh.GetVertexNormal(0));
        }

        // --- MeshTransforms.FlipLeftRightCoordSystems(Frame3f) ---

        [Fact]
        public void FlipLeftRightCoordSystems_Frame_MatchesFlippedPoints()
        {
            var f = new Frame3f(new Vector3f(1, 2, 3), Quaternionf.AxisAngleD(new Vector3f(1, 2, 0.5f).Normalized, 40));
            Frame3f g = MeshTransforms.FlipLeftRightCoordSystems(f);
            // points in the frame's XY plane map to the flipped points
            foreach (var uv in new[] { new Vector2f(0, 0), new Vector2f(1, 0), new Vector2f(0, 1), new Vector2f(0.3f, -2) }) {
                Vector3f p = f.FromFrameP(new Vector3f(uv.x, uv.y, 0));
                Vector3f q = g.FromFrameP(new Vector3f(uv.x, uv.y, 0));
                Assert.True(q.EpsilonEqual(MeshTransforms.FlipLeftRightCoordSystems(p), 1e-5f), $"{uv}: {q} vs {p}");
            }
            // still a right-handed orthonormal frame
            Assert.True(g.X.Cross(g.Y).EpsilonEqual(g.Z, 1e-5f));
        }
    }
}
