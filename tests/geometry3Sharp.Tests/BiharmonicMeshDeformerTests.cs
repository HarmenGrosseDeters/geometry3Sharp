using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class BiharmonicMeshDeformerTests
    {
        static DMesh3 Patch()
        {
            var m = new DMesh3(MeshComponents.None);
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0));
            m.AppendVertex(new Vector3d(1, 1, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 2, 3); return m;
        }
        static Dictionary<int, Vector3d> Targets(DMesh3 m) => new Dictionary<int, Vector3d> {
            [0] = m.GetVertex(0), [1] = m.GetVertex(1) + Vector3d.AxisZ, [3] = m.GetVertex(3)
        };
        [Fact]
        public void UnitSquare_MatchesIndependentBiharmonicSolution()
        {
            // Barycentric masses are (1/3,1/6,1/3,1/6). For cotangent L,
            // (L M^-1 L)[2,2] = 6 and [2,1] = -9/2. Thus z_2 = 3/4.
            var mesh = Patch(); var target = Targets(mesh);
            var result = new BiharmonicMeshDeformer(mesh).Compute(target);
            Assert.True(result.Converged, $"{result.Status} {result.Residual:R}");
            Assert.True(result.Mesh.CheckValidity());
            Assert.True(result.Mesh.GetVertex(2).Distance(new Vector3d(1, 1, 0.75)) < 1e-12);
            foreach (var h in target) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            Assert.Equal(new Vector3d(1, 1, 0), mesh.GetVertex(2));
        }
        [Fact]
        public void IdentityTranslationAndReuse()
        {
            var mesh = Patch(); var solver = new BiharmonicMeshDeformer(mesh);
            var move = new Vector3d(2, -3, 4);
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) + move, [1] = mesh.GetVertex(1) + move, [3] = mesh.GetVertex(3) + move };
            var a = solver.Compute(handles); var b = solver.Compute(handles);
            Assert.True(a.Converged, $"{a.Status} {a.Residual:R}"); Assert.True(b.Converged);
            foreach (int v in mesh.VertexIndices()) {
                Assert.True(a.Mesh.GetVertex(v).IsFinite);
                Assert.True(a.Mesh.GetVertex(v).Distance(mesh.GetVertex(v) + move) < 1e-12);
                Assert.Equal(a.Mesh.GetVertex(v), b.Mesh.GetVertex(v));
            }
            var id = solver.Compute(new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) });
            Assert.True(id.Converged);
            foreach (int v in mesh.VertexIndices()) Assert.Equal(mesh.GetVertex(v), id.Mesh.GetVertex(v));
        }
        [Fact]
        public void SimilarityAll_PreservesSolution()
        {
            var mesh = Patch(); var targets = Targets(mesh); var reference = new BiharmonicMeshDeformer(mesh).Compute(targets);
            Assert.True(reference.Converged);
            bool usedFallback = false;
            foreach (Similarity s in Similarity.All()) {
                var copy = new DMesh3(mesh); foreach (int v in copy.VertexIndices()) copy.SetVertex(v, s.Apply(mesh.GetVertex(v)));
                var mapped = new Dictionary<int, Vector3d>(); foreach (var h in targets) mapped[h.Key] = s.Apply(h.Value);
                var result = new BiharmonicMeshDeformer(copy).Compute(mapped);
                Assert.True(result.Converged, $"{s.Name}: {result.Status}, {result.Residual:R}");
                if (result.UsedIntervalFallback) {
                    usedFallback = true;
                    Assert.True(double.IsNaN(result.Residual));
                    Assert.True(result.PositionErrorBound >= 0 && !double.IsInfinity(result.PositionErrorBound));
                }
                foreach (int v in mesh.VertexIndices()) {
                    var actual = s.Inverse(result.Mesh.GetVertex(v));
                    if (s.IsExact) Assert.Equal(reference.Mesh.GetVertex(v), actual);
                    else Assert.True(actual.Distance(reference.Mesh.GetVertex(v)) <= Math.Max(1e-8, 64 * s.Resolution(4)), s.Name);
                }
            }
            Assert.True(usedFallback, "the rotated fixture should exercise the interval fallback");
        }
        [Fact]
        public void RotatedFourByFourGrid_IsCertifiedByFallback()
        {
            var mesh = new DMesh3(MeshComponents.None);
            var r = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 31);
            const int n = 4;
            for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
                mesh.AppendVertex(r * new Vector3d((double)x / (n - 1), (double)y / (n - 1), 0));
            for (int y = 0; y < n - 1; ++y) for (int x = 0; x < n - 1; ++x) {
                int a = x + n * y, b = a + 1, d = a + n, c = d + 1;
                mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d);
            }
            var handles = new Dictionary<int, Vector3d>();
            var move = new Vector3d(0, 0, 0.1);
            for (int y = 0; y < n; ++y) {
                int a = y * n, b = a + n - 1;
                handles[a] = mesh.GetVertex(a) + move; handles[b] = mesh.GetVertex(b) + move;
            }
            var result = new BiharmonicMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.True(result.UsedIntervalFallback);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            foreach (int v in mesh.VertexIndices()) {
                Assert.True(result.Mesh.GetVertex(v).IsFinite);
                Assert.True(result.Mesh.GetVertex(v).Distance(mesh.GetVertex(v) + move) < 1e-8);
            }
            // The dense interval fallback has an explicit whole-mesh size limit.
            int extra = mesh.AppendVertex(new Vector3d(4, 4, 4));
            handles[extra] = mesh.GetVertex(extra);
            var overLimit = new BiharmonicMeshDeformer(mesh).Compute(handles);
            Assert.Equal(BiharmonicMeshDeformer.Status.NumericalRange, overLimit.Status);
            Assert.Null(overLimit.Mesh);
        }
        [Fact]
        public void InvalidAndUnanchoredInputs_ReportStatus()
        {
            var m = Patch(); var solver = new BiharmonicMeshDeformer(m);
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, solver.Compute(null).Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, solver.Compute(new Dictionary<int, Vector3d> { [99] = Vector3d.Zero }).Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.UnanchoredComponent, solver.Compute(new Dictionary<int, Vector3d>()).Status);
            m.AppendVertex(new Vector3d(5, 5, 5));
            Assert.Equal(BiharmonicMeshDeformer.Status.UnanchoredComponent, solver.Compute(Targets(m)).Status);
        }
        [Fact]
        public void DegenerateTriangle_IsRejected()
        {
            var m = new DMesh3(MeshComponents.None);
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(Vector3d.AxisX); m.AppendVertex(2 * Vector3d.AxisX); m.AppendTriangle(0, 1, 2);
            var result = new BiharmonicMeshDeformer(m).Compute(new Dictionary<int, Vector3d> { [0] = Vector3d.Zero });
            Assert.Equal(BiharmonicMeshDeformer.Status.DegenerateTriangle, result.Status); Assert.Null(result.Mesh);
        }
        [Fact]
        public void FullyConstrained_RigidMotionIsExact()
        {
            var mesh = Patch(); var rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, 37);
            var targets = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) targets[v] = rot * mesh.GetVertex(v) + Vector3d.AxisZ;
            var result = new BiharmonicMeshDeformer(mesh).Compute(targets);
            Assert.True(result.Converged); foreach (var h in targets) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }
        [Fact]
        public void TinyMotionOnLargeMesh_IsNotErased()
        {
            var mesh = Patch(); foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, 1e100 * mesh.GetVertex(v));
            var result = new BiharmonicMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> {
                [0] = new Vector3d(0, 0, 1e-300), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3)
            });
            Assert.True(result.Converged, result.Status.ToString());
            Assert.True(Math.Abs(result.Mesh.GetVertex(2).z / 1e-300 + 0.5) < 1e-12);
        }
        [Theory]
        [InlineData(1e-200)]
        [InlineData(1e-150)]
        public void TinyCotanCouplingWithLargeMotion_DoesNotReportFalseSuccess(double tiny)
        {
            var mesh = new DMesh3(MeshComponents.None);
            mesh.AppendVertex(Vector3d.Zero); mesh.AppendVertex(new Vector3d(1, tiny, 0));
            mesh.AppendVertex(new Vector3d(0, tiny, 1)); mesh.AppendTriangle(0, 1, 2);
            double d = Math.Pow(2, 1000);
            var result = new BiharmonicMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> {
                [0] = new Vector3d(0, d, 0), [1] = new Vector3d(1, 3 * d, 0)
            });
            Assert.Equal(BiharmonicMeshDeformer.Status.NumericalRange, result.Status);
            Assert.Null(result.Mesh);
        }
        [Fact]
        public void LostHandleDifference_DoesNotReportFalseSuccess()
        {
            var mesh = new DMesh3(MeshComponents.None);
            mesh.AppendVertex(Vector3d.Zero); mesh.AppendVertex(Vector3d.AxisX);
            mesh.AppendVertex(Vector3d.AxisZ); mesh.AppendTriangle(0, 1, 2);
            double d = Math.Pow(2, 1000);
            var result = new BiharmonicMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> {
                [0] = new Vector3d(d, 0, 0), [1] = new Vector3d(3 * d, 0, 0)
            });
            Assert.Equal(BiharmonicMeshDeformer.Status.NumericalRange, result.Status); Assert.Null(result.Mesh);
        }
        [Fact]
        public void FullyConstrained_ExtremeFiniteTargets_AreCopiedExactly()
        {
            var mesh = new DMesh3(MeshComponents.None);
            mesh.AppendVertex(new Vector3d(1e308, 0, 0)); mesh.AppendVertex(new Vector3d(1e308, 1, 0));
            mesh.AppendVertex(new Vector3d(1e308, 0, 1)); mesh.AppendTriangle(0, 1, 2);
            var targets = new Dictionary<int, Vector3d> { [0] = new Vector3d(-1e308, 0, 0),
                [1] = new Vector3d(-1e308, 1, 0), [2] = new Vector3d(-1e308, 0, 1) };
            var result = new BiharmonicMeshDeformer(mesh).Compute(targets);
            Assert.True(result.Converged); foreach (var h in targets) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }
        [Fact]
        public void FixedExtremeComponent_DoesNotAffectAnotherComponent()
        {
            var mesh = Patch();
            int a = mesh.AppendVertex(new Vector3d(1e308, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(1e308, 1, 0));
            int c = mesh.AppendVertex(new Vector3d(1e308, 0, 1)); mesh.AppendTriangle(a, b, c);
            var handles = Targets(mesh); handles[a] = new Vector3d(-1e308, 0, 0);
            handles[b] = new Vector3d(-1e308, 1, 0); handles[c] = new Vector3d(-1e308, 0, 1);
            var result = new BiharmonicMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            Assert.True(result.Mesh.GetVertex(2).Distance(new Vector3d(1, 1, 0.75)) < 1e-12);
        }
        [Fact]
        public void MatrixAssemblyLimit_IsReportedBeforeFactorization()
        {
            var solver = new BiharmonicMeshDeformer(Patch()); solver.Options.MaxMatrixContributions = 1;
            var result = solver.Compute(Targets(solver.Mesh));
            Assert.Equal(BiharmonicMeshDeformer.Status.ResourceLimit, result.Status); Assert.Null(result.Mesh);
        }
        [Fact]
        public void HighValenceFan_RespectsMatrixAssemblyLimit()
        {
            var mesh = new DMesh3(MeshComponents.None); mesh.AppendVertex(Vector3d.Zero);
            const int count = 128;
            for (int i = 0; i < count; ++i) {
                double a = 2 * Math.PI * i / count;
                mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0));
            }
            for (int i = 0; i < count; ++i) mesh.AppendTriangle(0, i + 1, (i + 1) % count + 1);
            var solver = new BiharmonicMeshDeformer(mesh); solver.Options.MaxMatrixContributions = 1000;
            var result = solver.Compute(new Dictionary<int, Vector3d> { [1] = mesh.GetVertex(1) });
            Assert.Equal(BiharmonicMeshDeformer.Status.ResourceLimit, result.Status); Assert.Null(result.Mesh);
        }
        [Fact]
        public void PartialRigidRotation_IsNotAssumedToBeEnergyMinimum()
        {
            var mesh = Patch(); var rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, 90);
            var result = new BiharmonicMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> {
                [0] = rot * mesh.GetVertex(0), [1] = rot * mesh.GetVertex(1), [3] = rot * mesh.GetVertex(3)
            });
            Assert.True(result.Converged);
            Assert.True(result.Mesh.GetVertex(2).Distance(rot * mesh.GetVertex(2)) > 0.1);
        }
    }
}
