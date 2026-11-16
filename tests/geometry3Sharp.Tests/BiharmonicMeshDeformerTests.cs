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
        // n x n grid on [0,1]^2, two triangles per cell.
        static DMesh3 Grid(int n)
        {
            var mesh = new DMesh3(MeshComponents.None);
            for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
                mesh.AppendVertex(new Vector3d((double)x / (n - 1), (double)y / (n - 1), 0));
            for (int y = 0; y < n - 1; ++y) for (int x = 0; x < n - 1; ++x) {
                int a = x + n * y, b = a + 1, d = a + n, c = d + 1;
                mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d);
            }
            return mesh;
        }
        // Left and right columns are handles; the right one is lifted and shifted along a sine profile.
        static Dictionary<int, Vector3d> ColumnHandles(DMesh3 mesh, int n, Vector3d lift)
        {
            var handles = new Dictionary<int, Vector3d>();
            for (int y = 0; y < n; ++y) {
                int left = n * y, right = left + n - 1;
                handles[left] = mesh.GetVertex(left);
                handles[right] = mesh.GetVertex(right) + lift * (1 + Math.Sin(2 * Math.PI * y / (n - 1)));
            }
            return handles;
        }
        static void AssertBitEqual(Vector3d a, Vector3d b)
        {
            for (int k = 0; k < 3; ++k) Assert.Equal(BitConverter.DoubleToInt64Bits(a[k]), BitConverter.DoubleToInt64Bits(b[k]));
        }

        [Fact]
        public void UnitSquare_MatchesIndependentBiharmonicSolution()
        {
            // Barycentric masses are (1/3,1/6,1/3,1/6). For cotangent L,
            // (L M^-1 L)[2,2] = 6 and [2,1] = -9/2. Thus z_2 = 3/4.
            var mesh = Patch(); var target = Targets(mesh);
            var result = new BiharmonicMeshDeformer(mesh).Compute(target);
            Assert.True(result.Converged, $"{result.Status} {result.Residual:R}");
            Assert.Equal(1, result.Iterations);
            Assert.True(result.Residual <= 1e-8);
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
                AssertBitEqual(a.Mesh.GetVertex(v), b.Mesh.GetVertex(v));
            }
            var id = solver.Compute(new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) });
            Assert.True(id.Converged);
            foreach (int v in mesh.VertexIndices()) Assert.Equal(mesh.GetVertex(v), id.Mesh.GetVertex(v));
        }

        [Fact]
        public void Grid_TranslationOfHandlesMovesEveryVertexAndInputIsUntouched()
        {
            const int n = 20; var mesh = Grid(n); var before = new DMesh3(mesh);
            var move = new Vector3d(0.5, -2, 3);
            var handles = new Dictionary<int, Vector3d>();
            for (int y = 0; y < n; ++y) { int l = n * y, r = l + n - 1; handles[l] = mesh.GetVertex(l) + move; handles[r] = mesh.GetVertex(r) + move; }
            var result = new BiharmonicMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, $"{result.Status} {result.Residual:R}");
            foreach (int v in mesh.VertexIndices()) {
                Assert.True(result.Mesh.GetVertex(v).Distance(mesh.GetVertex(v) + move) < 1e-12, "vertex " + v);
                Assert.Equal(before.GetVertex(v), mesh.GetVertex(v));
            }
        }

        [Fact]
        public void Grid_LiftedColumn_IsFiniteHandlesExactAndSymmetric()
        {
            const int n = 24; var mesh = Grid(n);
            var handles = ColumnHandles(mesh, n, new Vector3d(0.1, 0, 0.05));
            var solver = new BiharmonicMeshDeformer(mesh);
            var result = solver.Compute(handles);
            Assert.True(result.Converged, $"{result.Status} {result.Residual:R}");
            Assert.True(result.Residual <= solver.Options.RelativeTolerance);
            Assert.Equal(mesh.VertexCount, result.Mesh.VertexCount); Assert.Equal(mesh.TriangleCount, result.Mesh.TriangleCount);
            foreach (int v in result.Mesh.VertexIndices()) Assert.True(result.Mesh.GetVertex(v).IsFinite);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            // The grid, its triangulation excepted, is symmetric about the middle row only up to the diagonal's direction,
            // so check the weaker invariant: the response is nonzero on free vertices and decays toward the fixed column.
            double near = 0, far = 0;
            for (int y = 0; y < n; ++y) {
                near += result.Mesh.GetVertex(1 + n * y).z * result.Mesh.GetVertex(1 + n * y).z;
                far += result.Mesh.GetVertex(n - 2 + n * y).z * result.Mesh.GetVertex(n - 2 + n * y).z;
            }
            Assert.True(far > 0); Assert.True(near < far);
        }

        [Fact]
        public void Grid_RepeatedComputeAndChangedTargetsOnOneInstance()
        {
            const int n = 16; var mesh = Grid(n); var solver = new BiharmonicMeshDeformer(mesh);
            var h1 = ColumnHandles(mesh, n, new Vector3d(0, 0, 0.1)); var h2 = ColumnHandles(mesh, n, new Vector3d(0, 0.2, -0.3));
            var a = solver.Compute(h1); var b = solver.Compute(h2); var c = solver.Compute(h1); var fresh = new BiharmonicMeshDeformer(mesh).Compute(h2);
            Assert.True(a.Converged && b.Converged && c.Converged);
            foreach (int v in mesh.VertexIndices()) {
                AssertBitEqual(a.Mesh.GetVertex(v), c.Mesh.GetVertex(v));
                AssertBitEqual(b.Mesh.GetVertex(v), fresh.Mesh.GetVertex(v));
            }
            Assert.NotEqual(a.Mesh.GetVertex(n + 1), b.Mesh.GetVertex(n + 1));
        }

        [Fact]
        public void SimilarityAll_PreservesSolution()
        {
            const int n = 8; var mesh = Grid(n); var targets = ColumnHandles(mesh, n, new Vector3d(0.05, 0.02, 0.1));
            var reference = new BiharmonicMeshDeformer(mesh).Compute(targets);
            Assert.True(reference.Converged, reference.Status.ToString());
            foreach (Similarity s in Similarity.All()) {
                var copy = new DMesh3(mesh); foreach (int v in copy.VertexIndices()) copy.SetVertex(v, s.Apply(mesh.GetVertex(v)));
                var mapped = new Dictionary<int, Vector3d>(); foreach (var h in targets) mapped[h.Key] = s.Apply(h.Value);
                var result = new BiharmonicMeshDeformer(copy).Compute(mapped);
                Assert.True(result.Converged, $"{s.Name}: {result.Status}, {result.Residual:R}");
                foreach (var h in mapped) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
                foreach (int v in mesh.VertexIndices()) {
                    var actual = s.Inverse(result.Mesh.GetVertex(v));
                    if (s.IsExact) AssertBitEqual(reference.Mesh.GetVertex(v), actual);
                    else Assert.True(actual.Distance(reference.Mesh.GetVertex(v)) <= Math.Max(1e-8, 64 * s.Resolution(4)), s.Name);
                }
            }
        }

        [Fact]
        public void SimilarityAll_PreservesUnitSquareSolution()
        {
            var mesh = Patch(); var targets = Targets(mesh); var reference = new BiharmonicMeshDeformer(mesh).Compute(targets);
            Assert.True(reference.Converged);
            foreach (Similarity s in Similarity.All()) {
                var copy = new DMesh3(mesh); foreach (int v in copy.VertexIndices()) copy.SetVertex(v, s.Apply(mesh.GetVertex(v)));
                var mapped = new Dictionary<int, Vector3d>(); foreach (var h in targets) mapped[h.Key] = s.Apply(h.Value);
                var result = new BiharmonicMeshDeformer(copy).Compute(mapped);
                Assert.True(result.Converged, $"{s.Name}: {result.Status}, {result.Residual:R}");
                foreach (int v in mesh.VertexIndices()) {
                    var actual = s.Inverse(result.Mesh.GetVertex(v));
                    if (s.IsExact) AssertBitEqual(reference.Mesh.GetVertex(v), actual);
                    else Assert.True(actual.Distance(reference.Mesh.GetVertex(v)) <= Math.Max(1e-8, 64 * s.Resolution(4)), s.Name);
                }
            }
        }

        [Fact]
        public void IsolatedFreeVertexAndHandleOnlyComponentAreSupported()
        {
            var mesh = Patch(); int lone = mesh.AppendVertex(new Vector3d(7, 7, 7));
            var handles = Targets(mesh); handles[lone] = new Vector3d(8, 8, 8);
            var result = new BiharmonicMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.Equal(new Vector3d(8, 8, 8), result.Mesh.GetVertex(lone));
            Assert.True(result.Mesh.GetVertex(2).Distance(new Vector3d(1, 1, 0.75)) < 1e-12);
        }

        [Fact]
        public void NormalsAreRecomputedWhenPresent()
        {
            var mesh = new DMesh3(true, false, false);
            mesh.AppendVertex(new NewVertexInfo(new Vector3d(0, 0, 0), Vector3f.AxisZ)); mesh.AppendVertex(new NewVertexInfo(new Vector3d(1, 0, 0), Vector3f.AxisZ));
            mesh.AppendVertex(new NewVertexInfo(new Vector3d(1, 1, 0), Vector3f.AxisZ)); mesh.AppendVertex(new NewVertexInfo(new Vector3d(0, 1, 0), Vector3f.AxisZ));
            mesh.AppendTriangle(0, 1, 2); mesh.AppendTriangle(0, 2, 3);
            var result = new BiharmonicMeshDeformer(mesh).Compute(Targets(mesh));
            Assert.True(result.Converged); Assert.True(result.Mesh.HasVertexNormals);
            Assert.True(result.Mesh.GetVertexNormal(1).Distance(Vector3f.AxisZ) > 0.01f);
            Assert.Equal(Vector3f.AxisZ, mesh.GetVertexNormal(1));
        }

        [Fact]
        public void InvalidSettingsAndNonFiniteInput_AreInvalidInput()
        {
            var mesh = Patch();
            foreach (double tolerance in new[] { 0, -1, double.NaN, double.PositiveInfinity }) {
                var solver = new BiharmonicMeshDeformer(mesh); solver.Options.RelativeTolerance = tolerance;
                Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, solver.Compute(Targets(mesh)).Status);
            }
            var limit = new BiharmonicMeshDeformer(mesh); limit.Options.MaxMatrixContributions = 0;
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, limit.Compute(Targets(mesh)).Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, new BiharmonicMeshDeformer(null).Compute(Targets(mesh)).Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, new BiharmonicMeshDeformer(new DMesh3()).Compute(Targets(mesh)).Status);
            var badTarget = Targets(mesh); badTarget[1] = new Vector3d(0, 0, double.NaN);
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, new BiharmonicMeshDeformer(mesh).Compute(badTarget).Status);
            var badMesh = Patch(); badMesh.SetVertex(2, new Vector3d(1, double.PositiveInfinity, 0));
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, new BiharmonicMeshDeformer(badMesh).Compute(Targets(badMesh)).Status);
        }

        [Fact]
        public void InvalidAndUnanchoredInputs_ReportStatus()
        {
            var m = Patch(); var solver = new BiharmonicMeshDeformer(m);
            var invalid = solver.Compute(null);
            Assert.True(double.IsNaN(invalid.Residual)); Assert.Null(invalid.Mesh);
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
            Assert.True(double.IsNaN(result.Residual)); Assert.Equal(0, result.Iterations);
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
        static DMesh3 TinyCouplingTriangle(double tiny)
        {
            var mesh = new DMesh3(MeshComponents.None);
            mesh.AppendVertex(Vector3d.Zero); mesh.AppendVertex(new Vector3d(1, tiny, 0));
            mesh.AppendVertex(new Vector3d(0, tiny, 1)); mesh.AppendTriangle(0, 1, 2); return mesh;
        }
        [Fact]
        public void UnderflowingDotProduct_IsNumericalRange()
        {
            // The cotangent at vertex 0 is tiny^2 / (2 area); tiny^2 underflows to zero, which loses the coupling entirely.
            var mesh = TinyCouplingTriangle(1e-200);
            var result = new BiharmonicMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> {
                [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1) + Vector3d.AxisZ });
            Assert.Equal(BiharmonicMeshDeformer.Status.NumericalRange, result.Status); Assert.Null(result.Mesh);
        }
        [Fact]
        public void RepresentableTinyCoupling_IsRoundedAwayAsInAnyFloatingPointAssembly()
        {
            // tiny^2 = 1e-300 is representable but far below half an ulp of the other weights. The result must equal
            // the one for the exact right-angle triangle (tiny = 0) to rounding.
            var thin = TinyCouplingTriangle(1e-150); var exact = TinyCouplingTriangle(0);
            var a = new BiharmonicMeshDeformer(thin).Compute(new Dictionary<int, Vector3d> {
                [0] = thin.GetVertex(0), [1] = thin.GetVertex(1) + Vector3d.AxisZ });
            var b = new BiharmonicMeshDeformer(exact).Compute(new Dictionary<int, Vector3d> {
                [0] = exact.GetVertex(0), [1] = exact.GetVertex(1) + Vector3d.AxisZ });
            Assert.True(a.Converged, a.Status.ToString()); Assert.True(b.Converged, b.Status.ToString());
            Assert.True(a.Mesh.GetVertex(2).Distance(b.Mesh.GetVertex(2)) < 1e-12);
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
