using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The double-precision frame queries (NearestPointFrameD, RayHitPointFrameD, SurfaceFrameD, GetVertexFrameD,
    /// GetTriFrameD): the origin keeps double precision at UTM-scale offsets where the Frame3f of the float
    /// methods is off by up to 0.25 m (docs/ROBUSTNESS_SWEEP.md S6).
    /// </summary>
    public class Frame3dQueryTests
    {
        const double Size = 2;   // sphere of radius 1

        static DMesh3 Sphere()
        {
            var gen = new Sphere3Generator_NormalizedCube() { Radius = 1.0, EdgeVertices = 6 };
            DMesh3 m = gen.Generate().MakeDMesh();
            Assert.True(m.HasVertexNormals);
            return m;
        }

        // above the interior of triangles (centroid * 1.3): the nearest point is no tie between neighbouring triangles
        static readonly Vector3d[] Queries = new[] { 3, 57, 120, 201, 290 }.Select(t => { DMesh3 m = Sphere(); return m.GetTriCentroid(t) * 1.3; }).ToArray();

        static Similarity S(string name) => Similarity.ByName(name);

        static double Tol(Similarity s) => 16 * s.Resolution(Size);

        // origin of a frame computed on the transformed mesh, mapped back, against the one on the original
        static void AssertFrame(Frame3d f0, Frame3d ft, Similarity s)
        {
            Assert.True(ft.Origin.IsFinite && ft.Z.IsFinite);
            Vector3d back = s.Inverse(ft.Origin);
            Assert.True((back - f0.Origin).Length <= Tol(s), s.Name + ": origin off by " + (back - f0.Origin).Length);
            Vector3d zt = s.ApplyDirection(f0.Z);
            Assert.True((zt - ft.Z).Length < 1e-5, s.Name + ": z axis differs by " + (zt - ft.Z).Length);
            Assert.Equal(1.0, ft.Z.Length, 12);
            Assert.True(Math.Abs(ft.X.Dot(ft.Y)) < 1e-12 && Math.Abs(ft.X.Dot(ft.Z)) < 1e-12);
        }

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void NearestPointFrameD_Invariant(string name)
        {
            Similarity s = S(name);
            DMesh3 mesh = Sphere(), meshT = Invariance.Transformed(mesh, s);
            var tree = new DMeshAABBTree3(mesh, true); var treeT = new DMeshAABBTree3(meshT, true);
            foreach (Vector3d q in Queries)
                foreach (bool face in new[] { false, true })
                    AssertFrame(MeshQueries.NearestPointFrameD(mesh, tree, q, face), MeshQueries.NearestPointFrameD(meshT, treeT, s.Apply(q), face), s);
        }

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RayHitPointFrameD_Invariant(string name)
        {
            Similarity s = S(name);
            DMesh3 mesh = Sphere(), meshT = Invariance.Transformed(mesh, s);
            var tree = new DMeshAABBTree3(mesh, true); var treeT = new DMeshAABBTree3(meshT, true);
            foreach (Vector3d q in Queries) {
                var ray = new Ray3d(q, (-q).Normalized);
                var rayT = new Ray3d(s.Apply(q), s.ApplyDirection(ray.Direction));
                Assert.True(MeshQueries.RayHitPointFrameD(mesh, tree, ray, out Frame3d f0));
                Assert.True(MeshQueries.RayHitPointFrameD(meshT, treeT, rayT, out Frame3d ft));
                AssertFrame(f0, ft, s);
            }
            var miss = new Ray3d(new Vector3d(5, 5, 5), Vector3d.AxisX);
            Assert.False(MeshQueries.RayHitPointFrameD(mesh, tree, miss, out _));
        }

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SurfaceFrameD_Invariant(string name)
        {
            Similarity s = S(name);
            DMesh3 mesh = Sphere(), meshT = Invariance.Transformed(mesh, s);
            foreach (int tid in mesh.TriangleIndices().Where(t => t % 17 == 0)) {
                Vector3d p = mesh.GetTriCentroid(tid) * 1.01;   // slightly off the plane: projected by barycentric coordinates
                AssertFrame(MeshQueries.SurfaceFrameD(mesh, tid, p), MeshQueries.SurfaceFrameD(meshT, tid, s.Apply(p)), s);
            }
        }

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void MeshFramesD_Invariant(string name)
        {
            Similarity s = S(name);
            DMesh3 mesh = Sphere(), meshT = Invariance.Transformed(mesh, s);
            foreach (int tid in mesh.TriangleIndices().Where(t => t % 11 == 0))
                for (int e = 0; e < 3; e++) {
                    Frame3d f0 = mesh.GetTriFrameD(tid, e), ft = meshT.GetTriFrameD(tid, e);
                    AssertFrame(f0, ft, s);
                    Vector3d xt = s.ApplyDirection(f0.X);
                    Assert.True((xt - ft.X).Length < 1e-5, s.Name + ": tri x axis " + (xt - ft.X).Length);
                }
            foreach (int vid in mesh.VertexIndices().Where(v => v % 7 == 0))
                foreach (bool ny in new[] { false, true }) {
                    Frame3d f0 = mesh.GetVertexFrameD(vid, ny), ft = meshT.GetVertexFrameD(vid, ny);
                    Assert.True(ft.Origin.IsFinite);
                    Assert.True((s.Inverse(ft.Origin) - f0.Origin).Length <= Tol(s), s.Name + ": vertex frame origin");
                    Assert.True((s.ApplyDirection(f0.X) - ft.X).Length < 1e-5, s.Name + ": vertex frame x axis " + (s.ApplyDirection(f0.X) - ft.X).Length);
                    Vector3d n = ny ? ft.Y : ft.Z;
                    Assert.True((n - (Vector3d)meshT.GetVertexNormal(vid)).Length < 1e-6, s.Name + ": normal axis");
                }
        }

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Pow2Scaling_IsBitExact(string name)
        {
            Similarity s = S(name);
            if (!s.IsExact) return;
            DMesh3 mesh = Sphere(), meshT = Invariance.Transformed(mesh, s);
            var tree = new DMeshAABBTree3(mesh, true); var treeT = new DMeshAABBTree3(meshT, true);
            foreach (Vector3d q in Queries)
                Assert.Equal(s.Apply(MeshQueries.NearestPointFrameD(mesh, tree, q, true).Origin), MeshQueries.NearestPointFrameD(meshT, treeT, s.Apply(q), true).Origin);
            foreach (int tid in mesh.TriangleIndices().Take(20))
                Assert.Equal(s.Apply(mesh.GetTriFrameD(tid).Origin), meshT.GetTriFrameD(tid).Origin);
        }

        // The float methods keep returning a Frame3f whose origin is rounded to float: documents the error the D variants avoid.
        [Fact]
        public void FloatFrames_AreOffAtUtm_DoubleFramesAreNot()
        {
            Similarity s = Similarity.Utm;
            DMesh3 meshT = Invariance.Transformed(Sphere(), s);
            var treeT = new DMeshAABBTree3(meshT, true);
            double worstFloat = 0, worstDouble = 0;
            foreach (Vector3d q in Queries) {
                Vector3d qt = s.Apply(q);
                Vector3d truth = MeshQueries.TriangleDistance(meshT, treeT.FindNearestTriangle(qt), qt).TriangleClosest;
                worstFloat = Math.Max(worstFloat, ((Vector3d)MeshQueries.NearestPointFrame(meshT, treeT, qt, true).Origin - truth).Length);
                worstDouble = Math.Max(worstDouble, (MeshQueries.NearestPointFrameD(meshT, treeT, qt, true).Origin - truth).Length);
            }
            Assert.True(worstFloat > 1e-3, "float origin error " + worstFloat);   // float spacing at 5.7e6 is 0.5
            Assert.Equal(0.0, worstDouble);
            double triFloat = 0, triDouble = 0;
            foreach (int tid in meshT.TriangleIndices().Take(30)) {
                Vector3d c = meshT.GetTriCentroid(tid);
                triFloat = Math.Max(triFloat, ((Vector3d)meshT.GetTriFrame(tid).Origin - c).Length);
                triDouble = Math.Max(triDouble, (meshT.GetTriFrameD(tid).Origin - c).Length);
            }
            Assert.True(triFloat > 1e-3, "float tri frame error " + triFloat);
            Assert.True(triDouble < 1e-9, "double tri frame error " + triDouble);
        }

        // At unit scale the double frames agree with the float ones to float precision.
        [Fact]
        public void ConsistentWithFloatFramesAtUnitScale()
        {
            DMesh3 mesh = Sphere();
            var tree = new DMeshAABBTree3(mesh, true);
            foreach (Vector3d q in Queries)
                foreach (bool face in new[] { false, true })
                    AssertClose(MeshQueries.NearestPointFrame(mesh, tree, q, face), MeshQueries.NearestPointFrameD(mesh, tree, q, face));
            foreach (Vector3d q in Queries) {
                var ray = new Ray3d(q, (-q).Normalized);
                Assert.True(MeshQueries.RayHitPointFrame(mesh, tree, ray, out Frame3f ff));
                Assert.True(MeshQueries.RayHitPointFrameD(mesh, tree, ray, out Frame3d fd));
                AssertClose(ff, fd);
            }
            foreach (int tid in mesh.TriangleIndices().Where(t => t % 13 == 0)) {
                AssertClose(MeshQueries.SurfaceFrame(mesh, tid, mesh.GetTriCentroid(tid)), MeshQueries.SurfaceFrameD(mesh, tid, mesh.GetTriCentroid(tid)));
                for (int e = 0; e < 3; e++) AssertClose(mesh.GetTriFrame(tid, e), mesh.GetTriFrameD(tid, e));
            }
            foreach (int vid in mesh.VertexIndices().Where(v => v % 9 == 0))
                foreach (bool ny in new[] { false, true }) AssertClose(mesh.GetVertexFrame(vid, ny), mesh.GetVertexFrameD(vid, ny), 5e-2);   // the float method builds its quaternion from non-orthogonal axes: its X, Y are approximate to about 1e-2
        }

        static void AssertClose(Frame3f f, Frame3d d, double axisTol = 1e-5)
        {
            Assert.True(((Vector3d)f.Origin - d.Origin).Length < 1e-6);
            double ex = ((Vector3d)f.X - d.X).Length, ey = ((Vector3d)f.Y - d.Y).Length, ez = ((Vector3d)f.Z - d.Z).Length;
            Assert.True(ex < axisTol && ey < axisTol && ez < axisTol, "axis differences " + ex + " " + ey + " " + ez);
        }

        // The D variant is a proper right-handed frame, equal to DMesh3's.
        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void NTMesh3_GetTriFrameD_IsRightHandedAndInvariant(string name)
        {
            Similarity s = S(name);
            DMesh3 d = Sphere();
            var m = new NTMesh3(d); var mt = new NTMesh3(Invariance.Transformed(d, s));
            foreach (int tid in m.TriangleIndices().Take(25))
                for (int e = 0; e < 3; e++) {
                    Frame3d f = m.GetTriFrameD(tid, e), ft = mt.GetTriFrameD(tid, e);
                    Assert.True((f.X.Cross(f.Y) - f.Z).Length < 1e-12, "right-handed");
                    Assert.True((f.Z - m.GetTriNormal(tid)).Length < 1e-9, "z is the face normal");
                    AssertFrame(f, ft, s);
                    if (name == "identity") {
                        Frame3d g = d.GetTriFrameD(tid, e);
                        Assert.True((f.Origin - g.Origin).Length < 1e-12 && (f.X - g.X).Length < 1e-9 && (f.Y - g.Y).Length < 1e-9 && (f.Z - g.Z).Length < 1e-9);
                    }
                }
        }

        // #1029: the float NTMesh3.GetTriFrame built a reflection that Matrix3f.ToQuaternion turned into a wrong frame
        // (Z was the face normal for 4 of 300 sphere triangles). It must be a rotation with Z the face normal, equal to DMesh3's.
        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void NTMesh3_GetTriFrame_Float_IsRightHandedFaceNormalAndInvariant(string name)
        {
            Similarity s = S(name);
            DMesh3 d = Sphere();
            DMesh3 dt = Invariance.Transformed(d, s);
            var m = new NTMesh3(d); var mt = new NTMesh3(dt);
            Assert.Equal(300, d.TriangleCount);
            foreach (int tid in m.TriangleIndices())
                for (int e = 0; e < 3; e++) {
                    Frame3f f = m.GetTriFrame(tid, e), ft = mt.GetTriFrame(tid, e);
                    Assert.True(((Vector3d)f.Z - m.GetTriNormal(tid)).Length < 1e-4, "z is the face normal");
                    Assert.True(((Vector3d)f.X.Cross(f.Y) - (Vector3d)f.Z).Length < 1e-4, "right-handed");
                    Assert.True(((Vector3d)ft.Z - mt.GetTriNormal(tid)).Length < 1e-4, "z is the face normal (transformed)");
                    Assert.True(((Vector3d)ft.Z - s.ApplyDirection((Vector3d)f.Z)).Length < 1e-4, "z axis follows the transform");
                    Index3i tri = dt.GetTriangle(tid);
                    Vector3d edgeDir = (dt.GetVertex(tri[(e + 1) % 3]) - dt.GetVertex(tri[e % 3])).Normalized;
                    Assert.True(((Vector3d)ft.X - edgeDir).Length < 1e-4, "x is the edge direction");
                    if (name == "identity") {
                        Frame3f g = d.GetTriFrame(tid, e);
                        Assert.True(((Vector3d)f.X - (Vector3d)g.X).Length < 1e-4 && ((Vector3d)f.Y - (Vector3d)g.Y).Length < 1e-4 && ((Vector3d)f.Z - (Vector3d)g.Z).Length < 1e-4);
                    }
                }
        }

        [Fact]
        public void SurfaceFrameD_InvalidTriangleThrows_AndFaceNormalWithoutVertexNormals()
        {
            DMesh3 mesh = Sphere();
            Assert.Throws<Exception>(() => MeshQueries.SurfaceFrameD(mesh, 100000, Vector3d.Zero));
            var plain = new DMesh3(mesh); plain.DiscardVertexNormals();
            Assert.False(plain.HasVertexNormals);
            var tree = new DMeshAABBTree3(plain, true);
            Vector3d q = Queries[0];
            Frame3d a = MeshQueries.NearestPointFrameD(plain, tree, q), b = MeshQueries.NearestPointFrameD(mesh, new DMeshAABBTree3(mesh, true), q, true);
            Assert.True((a.Origin - b.Origin).Length < 1e-15 && (a.Z - b.Z).Length < 1e-12);
            int tid = tree.FindNearestTriangle(q);
            Assert.True((MeshQueries.SurfaceFrameD(plain, tid, q).Z - plain.GetTriNormal(tid)).Length < 1e-9);
            Assert.True(MeshQueries.RayHitPointFrameD(plain, tree, new Ray3d(q, (-q).Normalized), out Frame3d h));
            Assert.True((h.Z - plain.GetTriNormal(tree.FindNearestHitTriangle(new Ray3d(q, (-q).Normalized)))).Length < 1e-9);
        }

        [Fact]
        public void Frame3d_ConversionsAndMapping()
        {
            var f = new Frame3d(new Vector3d(632117.123, 5712043.456, 113.789), new Vector3d(1, 2, 3).Normalized);
            Vector3d p = new Vector3d(0.3, -0.2, 0.7);
            Assert.True((f.ToFrameP(f.FromFrameP(p)) - p).Length < 1e-9);
            Assert.True((f.ToFrameV(f.FromFrameV(p)) - p).Length < 1e-12);
            Assert.True((f.Z - new Vector3d(1, 2, 3).Normalized).Length < 1e-12);
            Assert.True((f.X.Cross(f.Y) - f.Z).Length < 1e-12);
            Assert.Equal(f.Origin, new Frame3d(f.Origin, f.X, f.Y, f.Z).Origin);
            Assert.True((new Frame3d(f.Origin, f.X, f.Y, f.Z).Z - f.Z).Length < 1e-12);
            // Frame3f -> Frame3d is exact; back is the float rounding of the origin
            var ff = new Frame3f(new Vector3f(1.5f, -2.25f, 3f), new Vector3f(0, 0, 1));
            Frame3d widened = ff;
            Assert.Equal(new Vector3d(1.5, -2.25, 3), widened.Origin);
            Assert.Equal(ff.Origin, ((Frame3f)widened).Origin);
            Assert.True(((Vector3d)ff.Z - widened.Z).Length < 1e-6);
            Assert.NotEqual(f.Origin, (Vector3d)((Frame3f)f).Origin);
        }

        [Fact]
        public void Frame3d_IdentityAndOriginOnlyConstructor_HaveTheWorldAxes()
        {
            // (the changed-line coverage gate: Identity, Frame3d(origin))
            var o = new Vector3d(632117.25, 5712043.5, 113.75);
            foreach (Frame3d f in new[] { Frame3d.Identity, new Frame3d(o) }) {
                Assert.Equal(Vector3d.AxisX, f.X);
                Assert.Equal(Vector3d.AxisY, f.Y);
                Assert.Equal(Vector3d.AxisZ, f.Z);
            }
            Assert.Equal(Vector3d.Zero, Frame3d.Identity.Origin);
            Assert.Equal(o, new Frame3d(o).Origin);
            Assert.Equal(o + new Vector3d(1, 2, 3), new Frame3d(o).FromFrameP(new Vector3d(1, 2, 3)));
            Assert.Equal(new Vector3d(1, 2, 3), new Frame3d(o).ToFrameP(o + new Vector3d(1, 2, 3)));
        }

        [Fact]
        public void Frame3d_RotationAndOrigin_AreSettable()
        {
            var f = new Frame3d(Vector3d.Zero);
            Quaterniond q = new Quaterniond(Vector3d.AxisZ, 90);
            f.Rotation = q;
            f.Origin = new Vector3d(1, 2, 3);
            Assert.Equal(q.x, f.Rotation.x); Assert.Equal(q.y, f.Rotation.y); Assert.Equal(q.z, f.Rotation.z); Assert.Equal(q.w, f.Rotation.w);
            Assert.Equal(new Vector3d(1, 2, 3), f.Origin);
            // a quarter turn about Z: X goes to Y
            Assert.True((f.X - Vector3d.AxisY).Length < 1e-12);
            Assert.True((f.FromFrameP(Vector3d.AxisX) - new Vector3d(1, 3, 3)).Length < 1e-12);
        }

        [Fact]
        public void Frame3d_ToString_ListsOriginAndAxes()
        {
            string s = new Frame3d(new Vector3d(1, 2, 3)).ToString();
            Assert.StartsWith("[Frame3d: Origin=", s);
            Assert.Contains("X=", s); Assert.Contains("Y=", s); Assert.Contains("Z=", s);
            Assert.Contains(new Vector3d(1, 2, 3).ToString(), s);
            Assert.Contains(Vector3d.AxisZ.ToString(), s);
        }

        // a spatial that names a triangle for every ray (as a spatial with a coarse test would): the exact test then decides
        sealed class AnyTriangleSpatial : ISpatial
        {
            readonly int tid;
            public AnyTriangleSpatial(int tid) { this.tid = tid; }
            public bool SupportsNearestTriangle { get { return false; } }
            public int FindNearestTriangle(Vector3d p, double fMaxDist = double.MaxValue) { return tid; }
            public bool SupportsTriangleRayIntersection { get { return true; } }
            public int FindNearestHitTriangle(Ray3d ray, double fMaxDist = double.MaxValue) { return tid; }
            public bool SupportsPointContainment { get { return false; } }
            public bool IsInside(Vector3d p) { return false; }
        }

        [Fact]
        public void RayHitPointFrameD_TheNamedTriangleIsMissed_NoHit()
        {
            // the ray passes beside the triangle the spatial names: false, the frame stays the default one
            DMesh3 mesh = Sphere();
            int tid = mesh.TriangleIndices().First();
            Vector3d c = mesh.GetTriCentroid(tid);
            var spatial = new AnyTriangleSpatial(tid);
            var miss = new Ray3d(c + new Vector3d(10, 10, 10), mesh.GetTriNormal(tid));
            Frame3d f = new Frame3d(new Vector3d(7, 7, 7));
            Assert.False(MeshQueries.RayHitPointFrameD(mesh, spatial, miss, out f));
            Assert.Equal(Vector3d.Zero, f.Origin);
            // the ray through the centroid hits: the same stub gives a hit
            var hit = new Ray3d(c + 3 * mesh.GetTriNormal(tid), -mesh.GetTriNormal(tid));
            Assert.True(MeshQueries.RayHitPointFrameD(mesh, spatial, hit, out f));
            Assert.True((f.Origin - c).Length < 1e-12);
        }
    }
}
