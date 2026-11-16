using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public partial class ArapMeshDeformerTests
    {
        static DMesh3 Patch(double scale = 1)
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(0, 0, 0) * scale);
            mesh.AppendVertex(new Vector3d(1, 0, 0) * scale);
            mesh.AppendVertex(new Vector3d(1, 1, 0) * scale);
            mesh.AppendVertex(new Vector3d(0, 1, 0) * scale);
            mesh.AppendTriangle(0, 1, 2);
            mesh.AppendTriangle(0, 2, 3);
            return mesh;
        }

        [Fact]
        public void IdentityAndReuse_PreserveMeshAndHandles()
        {
            var mesh = Patch();
            var deformer = new ArapMeshDeformer(mesh);
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3) };
            var a = deformer.Compute(handles);
            var b = deformer.Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, a.Status);
            Assert.Equal(ArapMeshDeformer.Status.Success, b.Status);
            Assert.True(a.Mesh.CheckValidity());
            foreach (int v in mesh.VertexIndices()) {
                Assert.True(a.Mesh.GetVertex(v).Distance(mesh.GetVertex(v)) < 1e-10);
                Assert.Equal(a.Mesh.GetVertex(v), b.Mesh.GetVertex(v));
            }
        }

        [Fact]
        public void Translation_WithThreeHandles_PreservesRigidShape()
        {
            var mesh = Patch();
            var move = new Vector3d(2, -3, 4);
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) + move, [1] = mesh.GetVertex(1) + move, [3] = mesh.GetVertex(3) + move };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            Assert.True(result.Mesh.CheckValidity());
            foreach (int v in mesh.VertexIndices()) {
                Assert.True(result.Mesh.GetVertex(v).IsFinite);
                Assert.True(result.Mesh.GetVertex(v).Distance(mesh.GetVertex(v) + move) < 1e-6);
                Assert.Equal(Patch().GetVertex(v), mesh.GetVertex(v));
            }
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void Rotation_WithThreeHandles_PreservesRigidShape()
        {
            var mesh = Patch();
            var rotation = Matrix3d.AxisAngleD(Vector3d.AxisZ, 37);
            var handles = new Dictionary<int, Vector3d> {
                [0] = rotation * mesh.GetVertex(0), [1] = rotation * mesh.GetVertex(1), [3] = rotation * mesh.GetVertex(3)
            };
            var deformer = new ArapMeshDeformer(mesh);
            deformer.Options.MaxIterations = 500;
            var result = deformer.Compute(handles);
            Assert.True(result.Converged, $"{result.Status} residual {result.Residual:R}");
            foreach (int v in mesh.VertexIndices())
                Assert.True(result.Mesh.GetVertex(v).Distance(rotation * mesh.GetVertex(v)) < 1e-6);
        }

        [Fact]
        public void UniformScale_ExtremeFiniteCoordinates_ReturnValidTranslation()
        {
            foreach (double scale in new[] { 1e-100, 1e100 }) {
                var mesh = Patch(scale);
                var move = new Vector3d(2, -3, 4) * scale;
                var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) + move, [1] = mesh.GetVertex(1) + move, [3] = mesh.GetVertex(3) + move };
                var result = new ArapMeshDeformer(mesh).Compute(handles);
                Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
                foreach (int v in mesh.VertexIndices())
                    Assert.True(((result.Mesh.GetVertex(v) - (mesh.GetVertex(v) + move)) / scale).Length < 1e-6);
            }
        }

        [Fact]
        public void Similarities_PreserveDeformation()
        {
            var mesh = Patch();
            var targets = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3) + new Vector3d(0.2, 0, 0) };
            var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = 500;
            var reference = d.Compute(targets);
            Assert.True(reference.Converged, reference.Status.ToString());
            foreach (Similarity s in Similarity.All()) {
                var transformed = new DMesh3(mesh);
                foreach (int v in transformed.VertexIndices()) transformed.SetVertex(v, s.Apply(mesh.GetVertex(v)));
                var mappedTargets = new Dictionary<int, Vector3d>();
                foreach (var h in targets) mappedTargets.Add(h.Key, s.Apply(h.Value));
                var next = new ArapMeshDeformer(transformed); next.Options.MaxIterations = 500;
                var output = next.Compute(mappedTargets);
                Assert.True(output.Converged, $"{s.Name}: {output.Status}, {output.Residual:R}");
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d mapped = s.Inverse(output.Mesh.GetVertex(v));
                    if (s.IsExact) Assert.Equal(reference.Mesh.GetVertex(v), mapped);
                    else Assert.True(reference.Mesh.GetVertex(v).Distance(mapped) <= Math.Max(1e-6, 64 * s.Resolution(4)), s.Name);
                }
            }
        }

        [Fact]
        public void IterationLimit_ReturnsFiniteLastIterateWithExactHandles()
        {
            var mesh = Patch();
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3) + new Vector3d(0, 0, 0.2) };
            var deformer = new ArapMeshDeformer(mesh); deformer.Options.MaxIterations = 3;
            var result = deformer.Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.MaxIterations, result.Status);
            Assert.False(result.Converged);
            Assert.NotNull(result.Mesh);
            Assert.True(result.Mesh.CheckValidity());
            Assert.True(result.Residual > 0);
            foreach (int v in mesh.VertexIndices()) Assert.True(result.Mesh.GetVertex(v).IsFinite);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void DifferentHandles_ReusedInstanceAgreesWithFreshInstance()
        {
            var mesh = Patch(); var d = new ArapMeshDeformer(mesh);
            var first = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1) };
            var second = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) + Vector3d.AxisZ, [1] = mesh.GetVertex(1) + Vector3d.AxisZ };
            Assert.True(d.Compute(first).Converged);
            var reused = d.Compute(second); var fresh = new ArapMeshDeformer(mesh).Compute(second);
            Assert.True(reused.Converged); Assert.True(fresh.Converged);
            foreach (int v in mesh.VertexIndices()) Assert.Equal(fresh.Mesh.GetVertex(v), reused.Mesh.GetVertex(v));
        }

        [Fact]
        public void InvalidHandleAndUnanchoredComponent_AreReported()
        {
            var mesh = Patch();
            var bad = new ArapMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> { [999] = Vector3d.Zero });
            Assert.Equal(ArapMeshDeformer.Status.InvalidInput, bad.Status);
            int isolated = mesh.AppendVertex(new Vector3d(3, 3, 3));
            var disconnected = new ArapMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> { [0] = Vector3d.Zero });
            Assert.Equal(ArapMeshDeformer.Status.UnanchoredComponent, disconnected.Status);
            Assert.True(mesh.IsVertex(isolated));
        }

        [Fact]
        public void ComponentsWithDifferentScales_KeepTheirOwnGeometry()
        {
            var mesh = new DMesh3();
            int isolated = mesh.AppendVertex(new Vector3d(1e100, 0, 0));
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(Vector3d.AxisY);
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [isolated] = mesh.GetVertex(isolated), [a] = mesh.GetVertex(a) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            foreach (int v in mesh.VertexIndices())
                Assert.True(result.Mesh.GetVertex(v).Distance(mesh.GetVertex(v)) < 1e-8);
        }

        [Fact]
        public void AllHandles_WithDistantFiniteTargets_ReturnExactTargets()
        {
            var mesh = Patch(1e-300);
            var handles = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) handles.Add(v, new Vector3d(1e100, -1e100, 1e100));
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void FullyConstrainedComponent_DoesNotNormalizeItsDistantTarget()
        {
            var mesh = new DMesh3();
            int isolated = mesh.AppendVertex(new Vector3d(-1e308, 0, 0));
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(Vector3d.AxisY);
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [isolated] = new Vector3d(1e308, 0, 0), [a] = Vector3d.Zero };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.Equal(handles[isolated], result.Mesh.GetVertex(isolated));
            Assert.True(result.Mesh.GetVertex(b).Distance(mesh.GetVertex(b)) < 1e-8);
        }

        [Fact]
        public void FullyConstrainedThinTriangle_IsNotCalledDegenerate()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(new Vector3d(0, 1e-200, 0));
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a), [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void ConnectedExtremeScaleDisparity_ReturnsNumericalRange()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(new Vector3d(1e-230, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0, 1e-230, 0)), d = mesh.AppendVertex(new Vector3d(0, -1e100, 0));
            mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(b, a, d);
            var result = new ArapMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> { [a] = Vector3d.Zero });
            Assert.Equal(ArapMeshDeformer.Status.NumericalRange, result.Status);
            Assert.Null(result.Mesh);
        }

        [Fact]
        public void FixedThinComponent_DoesNotRequireCotangentWeights()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(new Vector3d(0, 1e-310, 0));
            mesh.AppendTriangle(a, b, c);
            int d = mesh.AppendVertex(new Vector3d(3, 0, 0)), e = mesh.AppendVertex(new Vector3d(4, 0, 0)), f = mesh.AppendVertex(new Vector3d(3, 1, 0));
            mesh.AppendTriangle(d, e, f);
            var handles = new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a), [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c), [d] = mesh.GetVertex(d) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            Assert.True(result.Mesh.GetVertex(e).Distance(mesh.GetVertex(e)) < 1e-8);
            Assert.True(result.Mesh.GetVertex(f).Distance(mesh.GetVertex(f)) < 1e-8);
        }

        [Fact]
        public void AllHandles_ValidTriangleWithVeryDifferentEdgeLengths()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(new Vector3d(1e308, 0, 0)), c = mesh.AppendVertex(new Vector3d(0, 1e-100, 0));
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a), [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void AllHandles_DoesNotLoseTinyCrossProductComponent()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(new Vector3d(1e308, 1e-100, 0));
            int c = mesh.AppendVertex(new Vector3d(1e308, 0, 0));
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a), [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void NoncompactVertexIds_ArePreserved()
        {
            var mesh = new DMesh3(); int unused = mesh.AppendVertex(new Vector3d(5, 5, 5));
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX);
            int c = mesh.AppendVertex(new Vector3d(1, 1, 0)), d = mesh.AppendVertex(Vector3d.AxisY);
            mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d);
            Assert.Equal(MeshResult.Ok, mesh.RemoveVertex(unused));
            Assert.False(mesh.IsCompact);
            var handles = new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a), [b] = mesh.GetVertex(b), [d] = mesh.GetVertex(d) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.False(result.Mesh.IsVertex(unused));
            foreach (int v in mesh.VertexIndices()) {
                Assert.True(result.Mesh.IsVertex(v));
                Assert.True(result.Mesh.GetVertex(v).Distance(mesh.GetVertex(v)) < 1e-8);
            }
            Assert.True(result.Mesh.CheckValidity());
        }

        [Fact]
        public void ObtuseTriangles_WithNegativeCotanEdge_PreserveRestState()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0.5, 0.1, 0)), d = mesh.AppendVertex(new Vector3d(0.5, -0.1, 0));
            mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(b, a, d);
            var handles = new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a), [b] = mesh.GetVertex(b) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.True(result.Mesh.CheckValidity());
            foreach (int v in mesh.VertexIndices())
                Assert.True(result.Mesh.GetVertex(v).Distance(mesh.GetVertex(v)) < 1e-8);
        }

        // Independent objective: local optimum from the separate general SVD and analytic cotangent weights.
        static double ClampedEdgeEnergy(DMesh3 rest, DMesh3 deformed)
        {
            var edges = new[] { new Index2i(0, 1), new Index2i(0, 2), new Index2i(1, 2), new Index2i(0, 3), new Index2i(1, 3) };
            var weights = new[] { 0.0, 2.5, 2.5, 2.5, 2.5 };
            double energy = 0;
            for (int i = 0; i < 4; ++i) {
                var covariance = Matrix3d.Zero; double constant = 0;
                for (int e = 0; e < edges.Length; ++e) {
                    int j = edges[e].a == i ? edges[e].b : edges[e].b == i ? edges[e].a : -1;
                    if (j < 0) continue;
                    Vector3d p = deformed.GetVertex(i) - deformed.GetVertex(j);
                    Vector3d q = rest.GetVertex(i) - rest.GetVertex(j);
                    covariance += weights[e] * new Matrix3d(ref p, ref q);
                    constant += weights[e] * (p.LengthSquared + q.LengthSquared);
                }
                var svd = new SingularValueDecomposition(3, 3, 1000);
                Assert.NotEqual(uint.MaxValue, svd.Solve(covariance.ToBuffer()));
                var singular = new double[3]; svd.GetSingularValues(singular);
                double optimum = singular[0] + singular[1] + (covariance.Determinant < 0 ? -singular[2] : singular[2]);
                energy += constant - 2 * optimum;
            }
            return energy;
        }

        [Fact]
        public void LocalRotation_OptimalScoreAgreesWithIndependentSvd()
        {
            var random = new Random(886);
            var inputs = new List<Matrix3d> {
                Matrix3d.Zero, new Matrix3d(3, 2, 0), new Matrix3d(3, 0, 0), new Matrix3d(-3, 2, 1)
            };
            for (int i = 0; i < 64; ++i)
                inputs.Add(new Matrix3d((r, c) => 2 * random.NextDouble() - 1));
            foreach (var covariance in inputs) {
                var fast = new FastQuaternionSVD(covariance, jacobiIters: 20);
                Matrix3d rotation = fast.U.ToRotationMatrix() * fast.V.Conjugate().ToRotationMatrix();
                Assert.True(Math.Abs(rotation.Determinant - 1) < 1e-10);
                double actual = 0;
                for (int r = 0; r < 3; ++r) for (int c = 0; c < 3; ++c) actual += rotation[r, c] * covariance[r, c];
                var reference = new SingularValueDecomposition(3, 3, 1000);
                Assert.NotEqual(uint.MaxValue, reference.Solve(covariance.ToBuffer()));
                var sigma = new double[3]; reference.GetSingularValues(sigma);
                double best = sigma[0] + sigma[1] + (covariance.Determinant < 0 ? -sigma[2] : sigma[2]);
                Assert.True(Math.Abs(best - actual) < 1e-9, $"score {actual:R}, optimum {best:R}");
            }
        }

        [Fact]
        public void NegativeCotanWeight_WithFreeEndpoint_BeatsIndependentCandidate()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0.5, 0.1, 0)), d = mesh.AppendVertex(new Vector3d(0.5, -0.1, 0));
            mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(b, a, d);
            var handles = new Dictionary<int, Vector3d> { [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c) + new Vector3d(0.1, 0.02, 0), [d] = mesh.GetVertex(d) };
            var initial = new DMesh3(mesh); foreach (var h in handles) initial.SetVertex(h.Key, h.Value);
            var deformer = new ArapMeshDeformer(mesh); deformer.Options.MaxIterations = 500;
            var result = deformer.Compute(handles);
            Assert.NotNull(result.Mesh);
            Assert.True(result.Mesh.CheckValidity());
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            var candidate = new DMesh3(initial); candidate.SetVertex(a, new Vector3d(0.05, 0.05, 0));
            double before = ClampedEdgeEnergy(mesh, initial), after = ClampedEdgeEnergy(mesh, result.Mesh);
            double feasible = ClampedEdgeEnergy(mesh, candidate);
            Assert.True(after < before - 1e-10, $"energy {before:R} -> {after:R}; {result.Status}");
            Assert.True(after <= feasible + 1e-9, $"energy {after:R} exceeds feasible {feasible:R}");
            Assert.True(result.Mesh.GetVertex(a).Distance(mesh.GetVertex(a)) > 1e-6);
        }

        [Fact]
        public void DegenerateTriangle_ReturnsNoMesh()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(2 * Vector3d.AxisX);
            mesh.AppendTriangle(a, b, c);
            var result = new ArapMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> { [a] = mesh.GetVertex(a) });
            Assert.Equal(ArapMeshDeformer.Status.DegenerateTriangle, result.Status);
            Assert.Null(result.Mesh);
        }

        [Fact]
        public void OutputNormals_FollowDeformedMesh_AndInputIsUnchanged()
        {
            var mesh = Patch(); MeshNormals.QuickCompute(mesh);
            var rotation = Matrix3d.AxisAngleD(Vector3d.AxisX, 90);
            var handles = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) handles.Add(v, rotation * mesh.GetVertex(v));
            var deformer = new ArapMeshDeformer(mesh);
            var a = deformer.Compute(handles); var b = deformer.Compute(handles);
            Assert.True(a.Converged); Assert.True(b.Converged);
            Assert.True(a.Mesh.HasVertexNormals);
            foreach (int v in mesh.VertexIndices()) {
                Vector3d expected = rotation * (Vector3d)mesh.GetVertexNormal(v);
                Assert.True(((Vector3d)a.Mesh.GetVertexNormal(v)).Distance(expected) < 1e-6);
                Assert.Equal(a.Mesh.GetVertexNormal(v), b.Mesh.GetVertexNormal(v));
                Assert.True(((Vector3d)mesh.GetVertexNormal(v)).Distance(Vector3d.AxisZ) < 1e-6);
            }
            var withoutNormals = new DMesh3(mesh); withoutNormals.DiscardVertexNormals();
            Assert.False(new ArapMeshDeformer(withoutNormals).Compute(handles).Mesh.HasVertexNormals);
        }

        [Fact]
        public void OutputNormals_WithFreeVertexAndIterationLimit_FollowFaces()
        {
            var mesh = Patch(); MeshNormals.QuickCompute(mesh);
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3) + new Vector3d(0, 0, 0.2) };
            var deformer = new ArapMeshDeformer(mesh); deformer.Options.MaxIterations = 1;
            var a = deformer.Compute(handles); var b = deformer.Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.MaxIterations, a.Status);
            Assert.NotNull(a.Mesh); Assert.True(a.Mesh.HasVertexNormals);
            // Vertex 1 is incident only to triangle 0, so its normal is that face normal.
            Assert.True(((Vector3d)a.Mesh.GetVertexNormal(1)).Distance(a.Mesh.GetTriNormal(0)) < 1e-6);
            foreach (int v in mesh.VertexIndices()) {
                Assert.Equal(a.Mesh.GetVertexNormal(v), b.Mesh.GetVertexNormal(v));
                Assert.True(((Vector3d)mesh.GetVertexNormal(v)).Distance(Vector3d.AxisZ) < 1e-6);
            }
        }

        [Fact]
        public void PositiveCotanWeight_MustNotUnderflowToAFalseSuccess()
        {
            var mesh = new DMesh3(MeshComponents.None);
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(new Vector3d(1e-200, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0, 1e100, 0));
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [a] = Vector3d.Zero, [c] = new Vector3d(1e308, 0, 0) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.NumericalRange, result.Status);
            Assert.Null(result.Mesh);
        }

        [Fact]
        public void CotanDotProductUnderflow_ReportsNumericalRange()
        {
            var mesh = new DMesh3(MeshComponents.None);
            int a = mesh.AppendVertex(Vector3d.Zero);
            int b = mesh.AppendVertex(new Vector3d(1, 1e-200, 0));
            int c = mesh.AppendVertex(new Vector3d(0, 1e-200, 1));
            mesh.AppendTriangle(a, b, c);
            var result = new ArapMeshDeformer(mesh).Compute(new Dictionary<int, Vector3d> {
                [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c)
            });
            Assert.Equal(ArapMeshDeformer.Status.NumericalRange, result.Status);
            Assert.Null(result.Mesh);
        }

        [Fact]
        public void NoHandles_ReportsUnanchoredComponent()
        {
            var result = new ArapMeshDeformer(Patch()).Compute(new Dictionary<int, Vector3d>());
            Assert.Equal(ArapMeshDeformer.Status.UnanchoredComponent, result.Status);
            Assert.Null(result.Mesh);
        }

        // ---- Meshes above 16 vertices, rotated off the axes so that right angles are rounding noise ----

        static DMesh3 Cylinder(int around, int rows)
        {
            var m = new DMesh3();
            for (int r = 0; r < rows; ++r) for (int a = 0; a < around; ++a) {
                double t = 2 * Math.PI * a / around; m.AppendVertex(new Vector3d(Math.Cos(t), Math.Sin(t), 0.3 * r));
            }
            for (int r = 0; r + 1 < rows; ++r) for (int a = 0; a < around; ++a) {
                int a2 = (a + 1) % around, p = r * around + a, q = r * around + a2, u = (r + 1) * around + a, v = (r + 1) * around + a2;
                m.AppendTriangle(p, q, v); m.AppendTriangle(p, v, u);
            }
            return m;
        }

        // Jittered grid with a curved, noisy height field and alternating diagonals: irregular angles and valences.
        static DMesh3 IrregularSurface(int nx, int ny, int seed)
        {
            var random = new Random(seed); var m = new DMesh3();
            for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i)
                m.AppendVertex(new Vector3d(i + 0.4 * (random.NextDouble() - 0.5), j + 0.4 * (random.NextDouble() - 0.5),
                    0.3 * Math.Sin(0.7 * i) * Math.Cos(0.5 * j) + 0.1 * random.NextDouble()));
            for (int j = 0; j + 1 < ny; ++j) for (int i = 0; i + 1 < nx; ++i) {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
            }
            return m;
        }

        static Func<Vector3d, Vector3d> Place(Matrix3d rotation, Vector3d translation) { return p => rotation * p + translation; }

        static DMesh3 Moved(DMesh3 mesh, Func<Vector3d, Vector3d> map)
        {
            var copy = new DMesh3(mesh);
            foreach (int v in copy.VertexIndices()) copy.SetVertex(v, map(mesh.GetVertex(v)));
            return copy;
        }

        static readonly Matrix3d Frame = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37);
        static readonly Vector3d FrameShift = new Vector3d(5, -3, 2);

        // Both rings of a cylinder are handles. Rigid: both move by one rigid motion. Bend: the top ring rotates about
        // an axis through its centre. Everything is expressed in the rotated frame.
        static Dictionary<int, Vector3d> RingHandles(DMesh3 rest, int around, int rows, Func<Vector3d, Vector3d> topMotion, Func<Vector3d, Vector3d> bottomMotion)
        {
            var handles = new Dictionary<int, Vector3d>();
            for (int a = 0; a < around; ++a) {
                handles[a] = bottomMotion(rest.GetVertex(a));
                handles[(rows - 1) * around + a] = topMotion(rest.GetVertex((rows - 1) * around + a));
            }
            return handles;
        }

        static Func<Vector3d, Vector3d> BendMotion(double degrees, Vector3d center)
        {
            var rotation = Matrix3d.AxisAngleD(Vector3d.AxisX, degrees);
            return p => rotation * (p - center) + center;
        }

        static void AssertSuccessPath(DMesh3 input, ArapMeshDeformer.ResultInfo result, Dictionary<int, Vector3d> handles)
        {
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            Assert.NotNull(result.Mesh);
            Assert.True(result.Mesh.CheckValidity());
            Assert.Equal(input.VertexCount, result.Mesh.VertexCount);
            Assert.Equal(input.TriangleCount, result.Mesh.TriangleCount);
            foreach (int v in input.VertexIndices()) Assert.True(result.Mesh.GetVertex(v).IsFinite);
            foreach (var h in handles) Assert.Equal(h.Value, result.Mesh.GetVertex(h.Key));
            Assert.True(MathUtil.IsFinite(result.Energy) && result.Energy >= 0);
        }

        [Fact]
        public void RotatedCylinder_RigidHandleMotion_IsReproduced()
        {
            DMesh3 plain = Cylinder(24, 12);
            Func<Vector3d, Vector3d> frame = Place(Frame, FrameShift);
            DMesh3 mesh = Moved(plain, frame);
            Assert.True(mesh.VertexCount > 16);
            var rigid = Place(Matrix3d.AxisAngleD(new Vector3d(-2, 1, 1).Normalized, 25), new Vector3d(1, 2, -1));
            var handles = RingHandles(mesh, 24, 12, rigid, rigid);
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            AssertSuccessPath(mesh, result, handles);
            foreach (int v in mesh.VertexIndices())
                Assert.True(result.Mesh.GetVertex(v).Distance(rigid(mesh.GetVertex(v))) < 1e-6, "vertex " + v);
            Assert.True(result.Energy < 1e-12, "energy " + result.Energy.ToString("R"));
        }

        [Fact]
        public void JitteredIrregularSurface_RigidHandleMotion_IsReproduced()
        {
            DMesh3 mesh = Moved(IrregularSurface(12, 10, 7), Place(Frame, FrameShift));
            Assert.True(mesh.VertexCount > 16);
            var rigid = Place(Matrix3d.AxisAngleD(new Vector3d(1, -1, 2).Normalized, -40), new Vector3d(-2, 1, 3));
            var handles = new Dictionary<int, Vector3d>();
            foreach (int v in new[] { 0, 11, 108, 119, 55, 66 }) handles[v] = rigid(mesh.GetVertex(v));
            var deformer = new ArapMeshDeformer(mesh); deformer.Options.MaxIterations = 500;
            var result = deformer.Compute(handles);
            AssertSuccessPath(mesh, result, handles);
            foreach (int v in mesh.VertexIndices())
                Assert.True(result.Mesh.GetVertex(v).Distance(rigid(mesh.GetVertex(v))) < 1e-6 * 12, "vertex " + v + " off by " + result.Mesh.GetVertex(v).Distance(rigid(mesh.GetVertex(v))).ToString("R") + " iterations " + result.Iterations + " energy " + result.Energy.ToString("R") + " residual " + result.Residual.ToString("R"));
            Assert.True(result.Energy < 1e-12, "energy " + result.Energy.ToString("R"));
        }

        [Fact]
        public void RotatedCylinder_Bend_ReachesTheMinimumOfThePlainIteration()
        {
            DMesh3 mesh = Moved(Cylinder(16, 7), Place(Frame, FrameShift));
            Vector3d center = Frame * new Vector3d(0, 0, 1.8) + FrameShift;
            var handles = RingHandles(mesh, 16, 7, BendMotion(20, center), p => p);
            var fast = new ArapMeshDeformer(mesh); fast.Options.MaxIterations = 400;
            var reference = new ArapMeshDeformer(mesh); reference.Options.MaxIterations = 20000; reference.Options.AccelerationDepth = 0;
            var a = fast.Compute(handles); var b = reference.Compute(handles);
            AssertSuccessPath(mesh, a, handles); AssertSuccessPath(mesh, b, handles);
            Assert.True(a.Iterations < b.Iterations, a.Iterations + " vs " + b.Iterations);
            Assert.True(a.Energy > 1e-6, "a bend is not rigid: " + a.Energy.ToString("R"));
            Assert.True(Math.Abs(a.Energy - b.Energy) <= 1e-7 * b.Energy, a.Energy.ToString("R") + " vs " + b.Energy.ToString("R"));
            foreach (int v in mesh.VertexIndices()) Assert.True(a.Mesh.GetVertex(v).Distance(b.Mesh.GetVertex(v)) < 1e-4);
        }

        [Fact]
        public void JitteredIrregularSurface_Bend_ReachesTheMinimumOfThePlainIteration()
        {
            DMesh3 mesh = Moved(IrregularSurface(10, 8, 3), Place(Frame, FrameShift));
            var handles = new Dictionary<int, Vector3d>();
            for (int i = 0; i < 10; ++i) { handles[i] = mesh.GetVertex(i); }
            var lift = Frame * new Vector3d(0, 0, 2.5);
            for (int i = 0; i < 10; ++i) handles[70 + i] = mesh.GetVertex(70 + i) + lift;
            var fast = new ArapMeshDeformer(mesh); fast.Options.MaxIterations = 400;
            var reference = new ArapMeshDeformer(mesh); reference.Options.MaxIterations = 20000; reference.Options.AccelerationDepth = 0;
            var a = fast.Compute(handles); var b = reference.Compute(handles);
            AssertSuccessPath(mesh, a, handles); AssertSuccessPath(mesh, b, handles);
            Assert.True(a.Energy > 1e-6);
            Assert.True(Math.Abs(a.Energy - b.Energy) <= 1e-6 * b.Energy, a.Energy.ToString("R") + " vs " + b.Energy.ToString("R"));
        }

        [Fact]
        public void OutOfPlaneSquare_ConvergesWithinTheDefaultBudget()
        {
            var mesh = Patch();
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = mesh.GetVertex(3) + new Vector3d(0, 0, 0.2) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, result.Status);
            Assert.True(result.Iterations < 50, result.Iterations.ToString());
            var plain = new ArapMeshDeformer(mesh); plain.Options.MaxIterations = 20000; plain.Options.AccelerationDepth = 0;
            var reference = plain.Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.Success, reference.Status);
            Assert.True(reference.Iterations > 1000, "the plain iteration is slow here: " + reference.Iterations);
            Assert.True(Math.Abs(result.Energy - reference.Energy) <= 1e-9 * reference.Energy);
            Assert.True(result.Mesh.GetVertex(2).Distance(reference.Mesh.GetVertex(2)) < 1e-5);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public void Energy_NeverIncreasesWithTheIterationBudget(int depth)
        {
            DMesh3 mesh = Moved(Cylinder(16, 7), Place(Frame, FrameShift));
            Vector3d center = Frame * new Vector3d(0, 0, 1.8) + FrameShift;
            var handles = RingHandles(mesh, 16, 7, BendMotion(35, center), p => p);
            double previous = double.PositiveInfinity;
            for (int budget = 1; budget <= 40; ++budget) {
                var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = budget; d.Options.AccelerationDepth = depth;
                var r = d.Compute(handles);
                Assert.NotNull(r.Mesh);
                Assert.True(r.Energy <= previous, "budget " + budget + ": " + r.Energy.ToString("R") + " after " + previous.ToString("R"));
                previous = r.Energy;
            }
        }

        [Fact]
        public void BentCylinder_Invariance_OverSimilarityAll()
        {
            DMesh3 mesh = Cylinder(12, 6);
            var handles = RingHandles(mesh, 12, 6, BendMotion(30, new Vector3d(0, 0, 1.5)), p => p);
            var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = 400;
            var reference = d.Compute(handles);
            AssertSuccessPath(mesh, reference, handles);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 moved = Moved(mesh, s.Apply);
                var mapped = new Dictionary<int, Vector3d>();
                foreach (var h in handles) mapped.Add(h.Key, s.Apply(h.Value));
                var next = new ArapMeshDeformer(moved); next.Options.MaxIterations = 400;
                var output = next.Compute(mapped);
                AssertSuccessPath(moved, output, mapped);
                Assert.Equal(reference.Iterations, output.Iterations);
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d back = s.Inverse(output.Mesh.GetVertex(v));
                    if (s.IsExact) Assert.Equal(reference.Mesh.GetVertex(v), back);
                    else Assert.True(reference.Mesh.GetVertex(v).Distance(back) <= Math.Max(1e-6, 64 * s.Resolution(4)), s.Name);
                }
                if (s.IsExact) Assert.Equal(reference.Energy, output.Energy);
                else Assert.True(Math.Abs(reference.Energy - output.Energy) <= 1e-6 * reference.Energy, s.Name);
            }
        }

        [Fact]
        public void Reuse_RepeatedComputeOnOneInstance_IsBitIdentical()
        {
            DMesh3 mesh = Moved(Cylinder(16, 7), Place(Frame, FrameShift));
            Vector3d center = Frame * new Vector3d(0, 0, 1.8) + FrameShift;
            var bend = RingHandles(mesh, 16, 7, BendMotion(20, center), p => p);
            var shift = RingHandles(mesh, 16, 7, p => p + new Vector3d(0, 0, 1), p => p);
            var d = new ArapMeshDeformer(mesh);
            var first = d.Compute(bend); var other = d.Compute(shift); var second = d.Compute(bend);
            var fresh = new ArapMeshDeformer(mesh).Compute(bend);
            Assert.Equal(first.Status, second.Status);
            Assert.NotEqual(first.Energy, other.Energy);
            foreach (int v in mesh.VertexIndices()) {
                Assert.Equal(first.Mesh.GetVertex(v), second.Mesh.GetVertex(v));
                Assert.Equal(first.Mesh.GetVertex(v), fresh.Mesh.GetVertex(v));
            }
            Assert.Equal(first.Iterations, second.Iterations); Assert.Equal(first.Energy, second.Energy);
        }

        [Fact]
        public void InvalidAccelerationDepth_IsInvalidInput()
        {
            var mesh = Patch();
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0) };
            foreach (int depth in new[] { -1, 33 }) {
                var d = new ArapMeshDeformer(mesh); d.Options.AccelerationDepth = depth;
                var r = d.Compute(handles);
                Assert.Equal(ArapMeshDeformer.Status.InvalidInput, r.Status);
                Assert.Null(r.Mesh);
            }
        }

        [Fact]
        public void FlushedProductNextToLargeTerms_IsNotRejected()
        {
            // The two off-axis components multiply to 1e-340, which flushes to zero, next to a dot product of 0.5.
            var mesh = new DMesh3(MeshComponents.None);
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(new Vector3d(1, 1e-170, 0));
            int c = mesh.AppendVertex(new Vector3d(0.5, 1e-170, 0.4));
            mesh.AppendTriangle(a, b, c);
            var handles = new Dictionary<int, Vector3d> { [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            AssertSuccessPath(mesh, result, handles);
            Assert.True(result.Mesh.GetVertex(a).Distance(Vector3d.Zero) < 1e-9);
        }

        [Fact]
        public void Energy_MatchesTheIndependentEnergy()
        {
            // Mesh and handles of the negative-cotangent test: ClampedEdgeEnergy uses the analytic clamped weights.
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0.5, 0.1, 0)), d = mesh.AppendVertex(new Vector3d(0.5, -0.1, 0));
            mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(b, a, d);
            var handles = new Dictionary<int, Vector3d> { [b] = mesh.GetVertex(b), [c] = mesh.GetVertex(c) + new Vector3d(0.1, 0.02, 0), [d] = mesh.GetVertex(d) };
            var deformer = new ArapMeshDeformer(mesh); deformer.Options.MaxIterations = 500;
            var result = deformer.Compute(handles);
            Assert.NotNull(result.Mesh);
            var edges = new[] { new Index2i(0, 2), new Index2i(1, 2), new Index2i(0, 3), new Index2i(1, 3) };
            double normalizer = 0;
            foreach (var e in edges) normalizer += 2 * 2.5 * (mesh.GetVertex(e.a) - mesh.GetVertex(e.b)).LengthSquared;
            double expected = ClampedEdgeEnergy(mesh, result.Mesh) / normalizer;
            Assert.True(expected > 1e-4);
            Assert.True(Math.Abs(result.Energy - expected) <= 1e-9 * expected, result.Energy.ToString("R") + " vs " + expected.ToString("R"));
        }

        // The safeguard of the Anderson step: a candidate whose local step leaves the numerical range (energy overflow,
        // non-finite coordinate) is rejected like one that raises the energy: the plain step is taken, the history is
        // cleared, and neither the status nor the result is affected. Public input cannot make the extrapolation
        // overshoot on demand (the overflow window of the energy is a few ulps wide and the extrapolation is damped),
        // so the test corrupts the candidate through the internal CandidateProbe seam.
        [Theory]
        [InlineData(1e160)]          // finite candidate, energy overflows: LocalStep fails with NumericalRange
        [InlineData(double.NaN)]     // non-finite candidate coordinate: LocalStep fails with NonFiniteResult
        [InlineData(7.0)]            // finite and in range, but the energy rises
        public void CorruptedAndersonCandidate_IsRejectedWithoutChangingTheResult(double corruption)
        {
            DMesh3 mesh = Moved(Cylinder(16, 7), Place(Frame, FrameShift));
            Vector3d center = Frame * new Vector3d(0, 0, 1.8) + FrameShift;
            var handles = RingHandles(mesh, 16, 7, BendMotion(35, center), p => p);
            var reference = new ArapMeshDeformer(mesh).Compute(handles);
            AssertSuccessPath(mesh, reference, handles);
            const int corruptAt = 4;
            Assert.True(reference.Iterations > corruptAt + 2, "the bend must still iterate after the corrupted step: " + reference.Iterations);

            double previous = double.PositiveInfinity;
            for (int budget = 1; budget <= reference.Iterations + 2; ++budget) {
                int fired = 0, historyBefore = -1, historyAfter = -1;
                var d = new ArapMeshDeformer(mesh);
                d.Options.MaxIterations = budget;
                d.CandidateProbe = (iter, history, candidate) => {
                    if (iter == corruptAt) { ++fired; historyBefore = history; candidate[0] = corruption; }
                    else if (iter == corruptAt + 1) historyAfter = history;
                };
                var r = d.Compute(handles);
                Assert.Equal(budget < reference.Iterations ? ArapMeshDeformer.Status.MaxIterations : ArapMeshDeformer.Status.Success, r.Status);
                Assert.NotNull(r.Mesh);
                Assert.True(r.Mesh.CheckValidity());
                foreach (int v in mesh.VertexIndices()) Assert.True(r.Mesh.GetVertex(v).IsFinite);
                foreach (var h in handles) Assert.Equal(h.Value, r.Mesh.GetVertex(h.Key));
                Assert.True(MathUtil.IsFinite(r.Energy) && r.Energy >= 0);
                Assert.True(r.Energy <= previous, "budget " + budget + ": " + r.Energy.ToString("R") + " after " + previous.ToString("R"));
                previous = r.Energy;
                if (budget > corruptAt) {
                    Assert.Equal(1, fired);
                    // A rejection clears the history: the next candidate is built from the one difference of its own step.
                    Assert.True(historyBefore > 1, "the rejected candidate used a history of " + historyBefore);
                    if (budget > corruptAt + 1) {
                        Assert.True(historyAfter != -1, "no candidate was built after the rejection");
                        Assert.Equal(1, historyAfter);
                    }
                }
                if (budget == reference.Iterations + 2) {
                    // The rejected step costs at most a few iterations; the iteration still reaches the same minimum.
                    Assert.Equal(ArapMeshDeformer.Status.Success, r.Status);
                    Assert.True(Math.Abs(r.Energy - reference.Energy) <= 1e-6 * reference.Energy + 1e-12, r.Energy.ToString("R") + " vs " + reference.Energy.ToString("R"));
                }
            }
        }

        [Fact]
        public void EnergyOverflowOnFiniteInput_IsNumericalRange()
        {
            var mesh = Patch();
            var handles = new Dictionary<int, Vector3d> { [0] = mesh.GetVertex(0), [1] = mesh.GetVertex(1), [3] = new Vector3d(0, 1e160, 0) };
            var result = new ArapMeshDeformer(mesh).Compute(handles);
            Assert.Equal(ArapMeshDeformer.Status.NumericalRange, result.Status);
            Assert.Null(result.Mesh);
        }
    }
}
