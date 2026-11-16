using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class BiharmonicMeshDeformerTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StrictCertificate_AboveFormerLimitPreservesTranslationAndReuse(bool rotated)
        {
            const int n=21;
            var mesh=new DMesh3(MeshComponents.None);
            var rotation=rotated ? Matrix3d.AxisAngleD(new Vector3d(1,2,3).Normalized,31) : Matrix3d.Identity;
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)mesh.AppendVertex(rotation*new Vector3d((double)x/(n-1),(double)y/(n-1),0));
            for(int y=0;y<n-1;y++)for(int x=0;x<n-1;x++) {
                int a=x+n*y,b=a+1,d=a+n,c=d+1;mesh.AppendTriangle(a,b,c);mesh.AppendTriangle(a,c,d);
            }
            var move=new Vector3d(.04,-.03,.1);var handles=new Dictionary<int,Vector3d>();
            for(int y=0;y<n;y++)foreach(int i in new[]{n*y,n*y+n-1})handles[i]=mesh.GetVertex(i)+move;
            var solver=new BiharmonicMeshDeformer(mesh);solver.Options.RequireEuclideanAccuracy=true;
            var result=solver.Compute(handles);
            if(!BiharmonicScalarCertificate.IsSupported) { Assert.Equal(BiharmonicMeshDeformer.Status.AccuracyNotEstablished,result.Status);return; }
            Assert.True(result.Converged,result.Status.ToString());
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified,result.Accuracy);
            Assert.True(result.Mesh.CheckValidity());
            foreach(int i in mesh.VertexIndices())Assert.True(result.Mesh.GetVertex(i).Distance(mesh.GetVertex(i)+move)<1e-8);
            var reused=solver.Compute(handles);Assert.True(reused.Converged);
            foreach(int i in mesh.VertexIndices())Assert.Equal(result.Mesh.GetVertex(i),reused.Mesh.GetVertex(i));
            foreach(var h in handles)Assert.Equal(h.Value,result.Mesh.GetVertex(h.Key));
        }

        [Fact]
        public void ScalarCertificate_RejectsIndefiniteIntervalsAndExhaustedBudgets()
        {
#if NET10_0_OR_GREATER
            if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
                Assert.True(BiharmonicScalarCertificate.IsSupported);
#endif
            if (!BiharmonicScalarCertificate.IsSupported) return;
            var one = BiharmonicFixedInterval.FromDouble(1);
            var four = BiharmonicFixedInterval.FromDouble(4);
            var columns = new[] {
                new Dictionary<int,BiharmonicFixedInterval> {{0,four},{1,one}},
                new Dictionary<int,BiharmonicFixedInterval> {{1,four}}
            };
            Assert.True(BiharmonicScalarCertificate.Verify(columns));
            Assert.False(BiharmonicScalarCertificate.Verify(columns,0));
            Assert.False(BiharmonicScalarCertificate.Verify(columns,100,2));
            Assert.Equal(four.Lo,columns[0][0].Lo); // verifier owns its scratch
            columns[1][1] = BiharmonicFixedInterval.FromDouble(-1);
            Assert.False(BiharmonicScalarCertificate.Verify(columns));
            // Positive midpoint does not establish positivity of every enclosed matrix.
            columns[0][0] = new BiharmonicFixedInterval(-one.Hi, 3*one.Hi);
            columns[1][1] = four;
            Assert.False(BiharmonicScalarCertificate.Verify(columns));
            // A nearly singular SPD matrix is inconclusive after the trace correction.
            columns[0][0] = one;
            columns[1][1] = BiharmonicFixedInterval.FromDouble(1.0000000000000002);
            Assert.False(BiharmonicScalarCertificate.Verify(columns));
        }

        [Fact]
        public void ScalarCertificate_DirectedConversionEnclosesExactUnits()
        {
            var unit = System.Numerics.BigInteger.One << 1074;
            foreach (var exact in new[] { unit,unit+1,unit-1,-unit-1,-unit+1,
                System.Numerics.BigInteger.One,-System.Numerics.BigInteger.One }) {
                double lo = BiharmonicScalarCertificate.Directed(exact,false);
                double hi = BiharmonicScalarCertificate.Directed(exact,true);
                Assert.True(BiharmonicScalarCertificate.Units(lo)<=exact);
                Assert.True(BiharmonicScalarCertificate.Units(hi)>=exact);
            }
        }

        [Fact]
        public void CertificateOrdering_ValidatesPermutationAndOwnsSnapshot()
        {
            Assert.Equal(new[] {1,2,0}, BiharmonicSparseCertificate.InvertOrder(new[] {2,0,1},3));
            Assert.Null(BiharmonicSparseCertificate.InvertOrder(new[] {0,0,2},3));
            Assert.Null(BiharmonicSparseCertificate.InvertOrder(new[] {-1,0,1},3));
            Assert.Null(BiharmonicSparseCertificate.InvertOrder(new[] {0,1,3},3));
            Assert.Null(BiharmonicSparseCertificate.InvertOrder(new[] {0,1},3));
            var factor = new SparseCholesky(3);
            Assert.Throws<InvalidOperationException>(() => factor.CopyPermutation());
            factor.Add(0,0,4);factor.Add(1,1,4);factor.Add(2,2,4);factor.Add(1,0,1);factor.Add(2,1,1);
            Assert.True(factor.Factorize());var snapshot=factor.CopyPermutation();var pristine=factor.CopyPermutation();
            Assert.NotNull(BiharmonicSparseCertificate.InvertOrder(snapshot,3));snapshot[0]=-1;
            Assert.Equal(pristine,factor.CopyPermutation());
            factor.Clear();Assert.Throws<InvalidOperationException>(() => factor.CopyPermutation());
        }
        [Fact]
        public void PreparedCertificate_RechecksOutputAndInvalidatesGeometryTopologyAndIdentity()
        {
            var mesh = Patch(); var ids = new List<int>(mesh.VertexIndices()); var lookup = new Dictionary<int,int>();
            var original = new Vector3d[4]; for (int i = 0; i < 4; i++) { lookup[i] = i; original[i] = mesh.GetVertex(i); }
            var handles = Targets(mesh); var candidate = (Vector3d[])original.Clone();
            foreach (var h in handles) candidate[h.Key] = h.Value; candidate[2].z = .75;
            var roots = new int[4]; var active = new[] { true,true,true,true }; var free = new[] {-1,-1,0,-1};
            BiharmonicSparseCertificate.Prepared prepared = null; double bound;
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            var first = prepared; Assert.NotNull(first);
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            Assert.Same(first, prepared);
            candidate[2].z = 0;
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            Assert.Same(first, prepared); candidate[2].z = .75;
            var newTargets = new Dictionary<int,Vector3d>(handles); newTargets[1] = original[1] + 2*Vector3d.AxisZ;
            candidate[1] = newTargets[1]; candidate[2].z = 1.5;
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,newTargets,1e-8,1e-8,out bound,ref prepared));
            Assert.Same(first, prepared);
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            candidate[1] = handles[1]; candidate[2].z = .75;
            Assert.False(prepared.Matches(mesh,ids,original,new[] {-1,-1,-1,0}));
            Assert.Equal(MeshResult.Ok, mesh.ReverseTriOrientation(0));
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            Assert.NotSame(first, prepared); var afterTopology = prepared;
            mesh = new DMesh3(mesh);
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            Assert.NotSame(afterTopology, prepared); var afterIdentity = prepared;
            mesh.SetVertex(2, original[2] + new Vector3d(.125,0,0));
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared));
            Assert.Null(prepared); original[2] = mesh.GetVertex(2);
            BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,1,handles,1e-8,1e-8,out bound,ref prepared);
            Assert.NotNull(prepared); Assert.NotSame(afterIdentity, prepared);
        }
        [Fact]
        public void FixedCertificateArithmetic_RejectsOversizedEndpoints()
        {
            var huge = System.Numerics.BigInteger.One << 4096;
            Assert.Throws<BiharmonicCertificateRangeException>(() => new BiharmonicFixedInterval(huge, huge));
            var limit = System.Numerics.BigInteger.One << 4095;
            var endpoints = new BiharmonicFixedInterval(-limit, limit - 1);
            Assert.Equal(-limit, endpoints.Lo); Assert.Equal(limit - 1, endpoints.Hi);
            Assert.Throws<BiharmonicCertificateRangeException>(() => new BiharmonicFixedInterval(-limit - 1, 0));
            Assert.Throws<BiharmonicCertificateRangeException>(() => new BiharmonicFixedInterval(0, limit));
            var tiny = BiharmonicFixedInterval.FromDouble(double.Epsilon);
            Assert.Equal(System.Numerics.BigInteger.Zero, tiny.Lo);
            Assert.Equal(System.Numerics.BigInteger.One, tiny.Hi);
            var root = BiharmonicFixedInterval.Sqrt(BiharmonicFixedInterval.FromDouble(4));
            Assert.Equal(BiharmonicFixedInterval.FromDouble(2).Lo, root.Lo);
            Assert.Equal(root.Lo, root.Hi);
        }
        [Fact]
        public void SparseVerifier_LargerComponentRejectsEveryAxisAndHandleCorruption()
        {
            const int n = 5; var mesh = new DMesh3(MeshComponents.None);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) mesh.AppendVertex(new Vector3d((double)x / (n - 1), (double)y / (n - 1), 0));
            for (int y = 0; y < n - 1; y++) for (int x = 0; x < n - 1; x++) {
                int a = x + n*y, b = a + 1, d = a + n, c = d + 1; mesh.AppendTriangle(a,b,c); mesh.AppendTriangle(a,c,d);
            }
            var ids = new List<int>(mesh.VertexIndices()); var lookup = new Dictionary<int,int>();
            var original = new Vector3d[n*n]; var roots = new int[n*n]; var active = new bool[n*n]; var free = new int[n*n];
            var handles = new Dictionary<int,Vector3d>(); int count = 0;
            for (int i = 0; i < n*n; i++) {
                lookup[i] = i; original[i] = mesh.GetVertex(i); active[i] = true;
                bool fixedV = i % n == 0 || i % n == n-1; free[i] = fixedV ? -1 : count++;
                if (fixedV) handles[i] = original[i] + (i % n == n-1 ? new Vector3d(.04,-.03,.1) : Vector3d.Zero);
            }
            var solver = new BiharmonicMeshDeformer(mesh); solver.Options.RequireEuclideanAccuracy = true;
            var result = solver.Compute(handles); Assert.True(result.Converged, result.Status.ToString());
            var firstFactor = solver.FactorIdentity; Assert.NotNull(firstFactor);
            var repeated = solver.Compute(handles); Assert.True(repeated.Converged);
            Assert.Same(firstFactor, solver.FactorIdentity);
            foreach (int id in ids) Assert.Equal(result.Mesh.GetVertex(id), repeated.Mesh.GetVertex(id));
            var targetOnly = new Dictionary<int, Vector3d>(handles); targetOnly[n - 1] += new Vector3d(.01, .02, -.01);
            Assert.True(solver.Compute(targetOnly).Converged); Assert.Same(firstFactor, solver.FactorIdentity);
            var changedHandles = new Dictionary<int, Vector3d>(handles);
            changedHandles[n - 1] += new Vector3d(.01, .02, -.01);
            changedHandles[n + 1] = original[n + 1] + new Vector3d(0, 0, .02);
            var reused = solver.Compute(changedHandles);
            var freshSolver = new BiharmonicMeshDeformer(mesh); freshSolver.Options.RequireEuclideanAccuracy = true;
            var fresh = freshSolver.Compute(changedHandles);
            Assert.True(reused.Converged, reused.Status.ToString()); Assert.True(fresh.Converged);
            Assert.NotSame(firstFactor, solver.FactorIdentity);
            foreach (int id in ids) Assert.Equal(fresh.Mesh.GetVertex(id), reused.Mesh.GetVertex(id));
            foreach (var h in changedHandles) Assert.Equal(h.Value, reused.Mesh.GetVertex(h.Key));
            void CheckFresh() {
                var a = solver.Compute(changedHandles);
                var freshInstance = new BiharmonicMeshDeformer(solver.Mesh); freshInstance.Options.RequireEuclideanAccuracy = true;
                var b = freshInstance.Compute(changedHandles);
                Assert.True(a.Converged, a.Status.ToString()); Assert.True(b.Converged, b.Status.ToString());
                foreach (int id in ids) Assert.Equal(b.Mesh.GetVertex(id), a.Mesh.GetVertex(id));
                Assert.Equal(BitConverter.DoubleToInt64Bits(b.PositionErrorBound), BitConverter.DoubleToInt64Bits(a.PositionErrorBound));
            }
            var oldPoint = mesh.GetVertex(n + 2); mesh.SetVertex(n + 2, oldPoint + new Vector3d(0, 0, .015));
            CheckFresh(); mesh.SetVertex(n + 2, oldPoint);
            Assert.Equal(MeshResult.Ok, mesh.ReverseTriOrientation(0)); CheckFresh();
            Assert.Equal(MeshResult.Ok, mesh.ReverseTriOrientation(0));
            solver.Mesh = new DMesh3(mesh); CheckFresh();
            var lastFactor = solver.FactorIdentity; solver.Options.ResponseTolerance = 1e-30;
            Assert.Equal(BiharmonicMeshDeformer.Status.AccuracyNotEstablished, solver.Compute(changedHandles).Status);
            Assert.Same(lastFactor, solver.FactorIdentity);
            solver.Options.ResponseTolerance = 1e-8; CheckFresh();
            var candidate = new Vector3d[n*n]; for (int i = 0; i < n*n; i++) candidate[i] = result.Mesh.GetVertex(i);
            double bound;
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,count,handles,1e-8,1e-8,out bound));
            double naturalBound = bound;
            var orderSource = new SparseCholesky(count);
            for (int i = 0; i < count; i++) { orderSource.Add(i,i,count+1); if (i > 0) orderSource.Add(i,0,1); }
            Assert.True(orderSource.Factorize());
            var order = orderSource.CopyPermutation(); bool nontrivial = false;
            for (int i = 0; i < count; i++) nontrivial |= order[i] != i;
            Assert.True(nontrivial);
            BiharmonicSparseCertificate.Prepared ordered = null;
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,count,handles,1e-8,1e-8,out bound,ref ordered,orderSource));
            Assert.Equal(BitConverter.DoubleToInt64Bits(naturalBound),BitConverter.DoubleToInt64Bits(bound));
            var wrongSize = new SparseCholesky(1); wrongSize.Add(0,0,1); Assert.True(wrongSize.Factorize());
            BiharmonicSparseCertificate.Prepared invalidOrder = null;
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,count,handles,1e-8,1e-8,out bound,ref invalidOrder,wrongSize));
            Assert.Null(invalidOrder);
            // Supplying the numerical factor exercises the defect-correction bound,
            // which must not turn deliberately wrong output into a certificate.
            var actualFactor=(SparseCholesky)firstFactor;
            Assert.Equal(count,actualFactor.N);
            for(int axis=0;axis<3;axis++) {
                var corrupt=(Vector3d[])candidate.Clone();corrupt[1][axis]+=.001;
                BiharmonicSparseCertificate.Prepared proof=null;
                Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,corrupt,roots,active,free,count,handles,1e-8,1e-8,out bound,ref proof,actualFactor));
            }
            var saved = candidate[1];
            for (int axis = 0; axis < 3; axis++) {
                candidate[1] = saved; candidate[1][axis] += .01;
                Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,count,handles,1e-8,1e-8,out bound));
            }
            candidate[1] = saved; candidate[0].z += .01;
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,count,handles,1e-8,1e-8,out bound));
        }
        [Fact]
        public void SparseVerifier_RejectsWrongFreePositionAndHandle()
        {
            var mesh = Patch(); var ids = new List<int>(mesh.VertexIndices());
            var lookup = new Dictionary<int, int>(); var original = new Vector3d[4];
            for (int i = 0; i < 4; i++) { lookup[ids[i]] = i; original[i] = mesh.GetVertex(ids[i]); }
            var target = Targets(mesh); var candidate = (Vector3d[])original.Clone();
            foreach (var h in target) candidate[lookup[h.Key]] = h.Value;
            candidate[2].z = 0.75;
            var roots = new[] { 0, 0, 0, 0 }; var active = new[] { true, true, true, true }; var free = new[] { -1, -1, 0, -1 };
            double bound;
            Assert.True(BiharmonicSparseCertificate.TryVerify(mesh, ids, lookup, original, candidate, roots, active, free, 1, target, 1e-8, 1e-8, out bound));
            candidate[2].z = 0;
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh, ids, lookup, original, candidate, roots, active, free, 1, target, 1e-8, 1e-8, out bound));
            candidate[2].z = 0.75; candidate[1].z = 2;
            Assert.False(BiharmonicSparseCertificate.TryVerify(mesh, ids, lookup, original, candidate, roots, active, free, 1, target, 1e-8, 1e-8, out bound));
        }
        [Fact]
        public void StrictSparseCertificate_RotatedTwentyGridPreservesTranslation()
        {
            var mesh = new DMesh3(MeshComponents.None); const int n = 20;
            var rotation = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 31);
            for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
                mesh.AppendVertex(rotation * new Vector3d((double)x / (n - 1), (double)y / (n - 1), 0));
            for (int y = 0; y < n - 1; ++y) for (int x = 0; x < n - 1; ++x) {
                int a = x + n * y, b = a + 1, d = a + n, c = d + 1;
                mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d);
            }
            var handles = new Dictionary<int, Vector3d>(); var move = new Vector3d(0.04, -0.03, 0.1);
            for (int y = 0; y < n; ++y) { int a = n * y, b = a + n - 1; handles[a] = mesh.GetVertex(a) + move; handles[b] = mesh.GetVertex(b) + move; }
            var solver = new BiharmonicMeshDeformer(mesh); solver.Options.RequireEuclideanAccuracy = true;
            var result = solver.Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, result.Accuracy);
            Assert.True(result.Mesh.CheckValidity());
            foreach (int id in mesh.VertexIndices()) Assert.True(result.Mesh.GetVertex(id).Distance(mesh.GetVertex(id) + move) < 1e-8);
        }
        [Fact]
        public void EuclideanAccuracy_RejectsLostResponseAndUsesComponentScale()
        {
            var original = new[] { Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY };
            var root = new[] { 0, 0, 0 }; var active = new[] { true, true, true }; var free = new[] { -1, -1, 0 };
            var output = (Vector3d[])original.Clone();
            Assert.False(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, root, active, free, 1e-12, 1e-8, 1e-8));
            output[2] += new Vector3d(0.01, 0.02, 0.03);
            Assert.True(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, root, active, free, 1e-12, 1e-8, 1e-8));
            Assert.False(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, root, active, free, 1e-7, 1e-8, 1e-8));
            Assert.False(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, root, active, free, double.NaN, 1e-8, 1e-8));
            Assert.False(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, root, new[] { false, false, false }, free, 0, 1e-8, 1e-8));
            var rotate = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 31);
            for (int i = 0; i < original.Length; ++i) { original[i] = rotate * original[i]; output[i] = rotate * output[i]; }
            Assert.True(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, root, active, free, 1e-12, 1e-8, 1e-8));
        }

        [Fact]
        public void StrictEuclideanAccuracy_ReportsCertifiedSolutionOrAccuracyFailure()
        {
            var mesh = Patch(); var solver = new BiharmonicMeshDeformer(mesh);
            solver.Options.RequireEuclideanAccuracy = true;
            var result = solver.Compute(Targets(mesh));
            Assert.True(result.Converged, result.Status.ToString());
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, result.Accuracy);
            Assert.True(result.Mesh.GetVertex(2).Distance(new Vector3d(1, 1, 0.75)) < 1e-8);
            solver.Options.ResponseTolerance = 1e-20;
            var strict = solver.Compute(Targets(mesh));
            Assert.Equal(BiharmonicMeshDeformer.Status.AccuracyNotEstablished, strict.Status);
            Assert.Null(strict.Mesh);
        }

        [Fact]
        public void StrictEuclideanAccuracy_UnchangedHandlesCertifiesLargeComponent()
        {
            var mesh = new DMesh3(MeshComponents.None); const int n = 5;
            for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x) mesh.AppendVertex(new Vector3d(x, y, 0));
            for (int y = 0; y < n - 1; ++y) for (int x = 0; x < n - 1; ++x) {
                int a = x + y * n, b = a + 1, d = a + n, c = d + 1;
                mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d);
            }
            var target = mesh.GetVertex(0); target.x = -0.0;
            var handles = new Dictionary<int, Vector3d> { [0] = target };
            var solver = new BiharmonicMeshDeformer(mesh); solver.Options.RequireEuclideanAccuracy = true;
            var result = solver.Compute(handles);
            Assert.True(result.Converged, result.Status.ToString());
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, result.Accuracy);
            Assert.Equal(0, result.PositionErrorBound);
            Assert.Equal(BitConverter.DoubleToInt64Bits(target.x), BitConverter.DoubleToInt64Bits(result.Mesh.GetVertex(0).x));
            foreach (int v in mesh.VertexIndices()) Assert.Equal(mesh.GetVertex(v), result.Mesh.GetVertex(v));
            int isolated = mesh.AppendVertex(new Vector3d(10, 10, 10));
            Assert.Equal(BiharmonicMeshDeformer.Status.UnanchoredComponent, solver.Compute(handles).Status);
            handles[isolated] = mesh.GetVertex(isolated);
            Assert.True(solver.Compute(handles).Converged);
            handles[0] = target + Vector3d.AxisZ;
            Assert.Equal(BiharmonicMeshDeformer.Status.AccuracyNotEstablished, solver.Compute(handles).Status);
        }

        [Fact]
        public void EuclideanAccuracy_ChecksEveryComponentSeparately()
        {
            var original = new[] { Vector3d.Zero, Vector3d.AxisX, new Vector3d(1e100, 0, 0), new Vector3d(2e100, 0, 0) };
            var output = (Vector3d[])original.Clone();
            output[1] += new Vector3d(0, 0.01, 0); output[3] += new Vector3d(0, 1e98, 0);
            var roots = new[] { 0, 0, 2, 2 }; var active = new[] { true, true, true, true }; var free = new[] { -1, 0, -1, 1 };
            Assert.True(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, roots, active, free, 1e-12, 1e-8, 1e-8));
            output[3] = original[3];
            Assert.False(BiharmonicIntervalFallback.MeetsEuclideanAccuracy(original, output, roots, active, free, 1e-12, 1e-8, 1e-8));
        }

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
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.NotCertified, result.Accuracy);
            Assert.True(double.IsNaN(result.PositionErrorBound));
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
                    Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, result.Accuracy);
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
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, result.Accuracy);
            var strictSolver = new BiharmonicMeshDeformer(mesh); strictSolver.Options.RequireEuclideanAccuracy = true;
            var strictResult = strictSolver.Compute(handles);
            Assert.True(strictResult.Converged, strictResult.Status.ToString());
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, strictResult.Accuracy);
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
            var strictOverLimit = strictSolver.Compute(handles);
            Assert.Equal(BiharmonicMeshDeformer.Status.AccuracyNotEstablished, strictOverLimit.Status);
            Assert.Null(strictOverLimit.Mesh);
        }
        [Fact]
        public void InvalidAndUnanchoredInputs_ReportStatus()
        {
            var m = Patch(); var solver = new BiharmonicMeshDeformer(m);
            var invalid = solver.Compute(null);
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.NotEstablished, invalid.Accuracy);
            Assert.True(double.IsNaN(invalid.PositionErrorBound)); Assert.True(double.IsNaN(invalid.Residual));
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
            var strict = new BiharmonicMeshDeformer(m); strict.Options.RequireEuclideanAccuracy = true;
            Assert.Equal(BiharmonicMeshDeformer.Status.DegenerateTriangle, strict.Compute(new Dictionary<int, Vector3d> { [0] = Vector3d.Zero }).Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.UnanchoredComponent, strict.Compute(new Dictionary<int, Vector3d>()).Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.InvalidInput, strict.Compute(new Dictionary<int, Vector3d> { [99] = Vector3d.Zero }).Status);
        }
        [Fact]
        public void FullyConstrained_RigidMotionIsExact()
        {
            var mesh = Patch(); var rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, 37);
            var targets = new Dictionary<int, Vector3d>();
            foreach (int v in mesh.VertexIndices()) targets[v] = rot * mesh.GetVertex(v) + Vector3d.AxisZ;
            var result = new BiharmonicMeshDeformer(mesh).Compute(targets);
            Assert.Equal(BiharmonicMeshDeformer.AccuracyAssessment.Certified, result.Accuracy);
            Assert.Equal(0, result.PositionErrorBound); Assert.True(double.IsNaN(result.Residual));
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
