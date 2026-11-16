using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> ExactMeshImprint (docs/design/EXACT_IMPRINT.md): the guarantees G1 to G5 on procedural pairs. </summary>
    public class ExactMeshImprintTests
    {
        internal static DMesh3 Box(Vector3d c, Vector3d h, Matrix3d rot)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, h) }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices().ToArray())
                m.SetVertex(vid, rot * m.GetVertex(vid) + c);
            return m;
        }
        internal static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }
        static Matrix3d Rot() => Matrix3d.AxisAngleD(Vector3d.AxisZ, 29) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);

        // target and cutter after the similarity (exact for the powers of two)
        internal static void Pair(Similarity s, out DMesh3 target, out DMesh3 cutter)
        {
            target = Box(s.Apply(Vector3d.Zero), s.ApplyLength(1) * Vector3d.One, Matrix3d.Identity);
            cutter = Box(s.Apply(new Vector3d(0.3, 0.2, 0.1)), s.ApplyLength(0.7) * Vector3d.One, s.Rotation * Rot());
        }

        static ExactMeshImprint Run(DMesh3 target, DMesh3 cutter, Action<ExactMeshImprint> configure = null)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
            configure?.Invoke(imprint);
            Assert.True(imprint.Compute(), imprint.Error);
            Assert.Equal(ExactMeshImprint.ImprintStatus.Success, imprint.Status);
            Assert.True(imprint.Result.CheckValidity(true, FailMode.ReturnOnly));
            return imprint;
        }

        static double SignedArea(DMesh3 m, int tid, int axis)
        {
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
            Vector3d n = (b - a).Cross(c - a);
            return n[axis] / 2;
        }

        // G1: the target's vertices, untouched triangles and the orientation of the pieces
        internal static void AssertKeepsTarget(DMesh3 target, ExactMeshImprint imprint)
        {
            DMesh3 r = imprint.Result;
            foreach (int vid in target.VertexIndices()) {
                Assert.True(r.IsVertex(vid));
                Assert.Equal(target.GetVertex(vid), r.GetVertex(vid));
            }
            foreach (int tid in target.TriangleIndices()) {
                bool kept = r.IsTriangle(tid) && imprint.SourceTriangle[tid] == tid && r.GetTriangle(tid) == target.GetTriangle(tid);
                if (kept) continue;
                // split: every piece of it lies on the same side (its normal turns the same way)
                Vector3d n = target.GetTriNormal(tid);
                foreach (int pid in r.TriangleIndices().Where(p => imprint.SourceTriangle[p] == tid)) {
                    Assert.True(r.GetTriNormal(pid).Dot(n) > 0.99, "a piece is turned over");
                }
            }
            foreach (int pid in r.TriangleIndices())
                Assert.True(imprint.SourceTriangle[pid] >= 0 && target.IsTriangle(imprint.SourceTriangle[pid]));
        }

        [Fact]
        public void CubeCutByTiltedCube_ClosedManifoldKeepsTheTarget()
        {
            DMesh3 target, cutter;
            Pair(Similarity.Identity, out target, out cutter);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Result.TriangleCount > target.TriangleCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(imprint.Result, true));
            Assert.True(imprint.Result.IsClosed());
            AssertKeepsTarget(target, imprint);
            Assert.True(imprint.CurveEdges.Count > 8);
            Assert.True(imprint.RoundingClean);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(imprint.Result));
        }

        [Fact]
        public void Disjoint_ResultIsACopy()
        {
            var target = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity);
            var cutter = Box(new Vector3d(10, 0, 0), Vector3d.One, Matrix3d.Identity);
            var imprint = Run(target, cutter);
            Assert.Equal(target.TriangleCount, imprint.Result.TriangleCount);
            Assert.Empty(imprint.CurveEdges);
            AssertKeepsTarget(target, imprint);
        }

        [Fact]
        public void InvalidInput()
        {
            Assert.False(new ExactMeshImprint().Compute());
            var bad = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity);
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var imprint = new ExactMeshImprint() { Target = bad, Cutter = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity) };
            Assert.False(imprint.Compute());
            Assert.Equal(ExactMeshImprint.ImprintStatus.InvalidInput, imprint.Status);
            Assert.Null(imprint.Result);
        }
    }
}
