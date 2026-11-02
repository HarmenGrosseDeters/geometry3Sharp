using System;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Defects and questions the Boolean property tests found (ExactBooleanMetamorphicTests, ExactBooleanPropertyTests),
    /// pinned with minimal procedural reproductions. The active tests check that each reproduction still holds
    /// without the step in question.
    ///
    /// Finding 1, resolved by the owner's decision (November 2026): SafeRounding (on by default) treats every
    /// triangle with an area below 1e-12 diagonal^2 as degenerate and contracts it, also where the triangle and its
    /// vertices are exact (dyadic input, nothing was rounded; here a well-shaped triangle of size 2^-20). That is the
    /// intended behaviour: such tiny triangles trouble downstream programs (slicers, FEM, remeshers), and SafeRounding
    /// is there to make the result safe for them. Its guarantee: untouched triangles are the exact result, rounded, and
    /// every edit stays within a bounded displacement: the surface moves by at most MeshRoundingRepair.MaxDrift (at most 1e-5 of the diagonal,
    /// in Hausdorff distance, without removed components), and closed solids change in volume by about the edits'
    /// displacements times the areas they replace. The test asserts a clean result, the drift bound, the same solid
    /// within MaxDrift, and a volume sanity check, not bit identity.
    ///
    /// Design question 1b, resolved by the owner's decision (November 2026): an input that is already not clean (a
    /// triangle below the threshold, MeshRobustRepair "degenerate triangles") is repaired too, within the same bound.
    /// The test asserts a clean result, the drift bound and the same solid within MaxDrift. The families of degenerate
    /// input are in SafeRoundingDegenerateInputTests (docs/design/SAFE_ROUNDING.md section 2).
    /// </summary>
    public class ExactBooleanPropertyFindings
    {
        static readonly double T = Math.Pow(2, -20);

        // the unit cube with its edge (0,0,0)-(1,0,0) split at (2^-20, 0, 0): clean (no triangle below the
        // threshold), exact, two sliver triangles at the split
        static DMesh3 CubeWithSplitEdge()
        {
            DMesh3 a = new BoxCase(new IntBox(0, 0, 0, 1, 1, 1), new IntBox(0, 0, 0, 1, 1, 1)).MeshA();
            int e = a.FindEdge(0, 1);
            Assert.Equal(MeshResult.Ok, a.SplitEdge(e, out DMesh3.EdgeSplitInfo info, 0.5));
            a.SetVertex(info.vNew, new Vector3d(T, 0, 0));
            return a;
        }

        // [-1,2] x [-1,2] x [2^-20, 2]: its bottom plane cuts the slivers 2^-20 above the cube's bottom, into pieces
        // of area about 2^-41, below the threshold of the union (1e-12 * 27)
        static DMesh3 Slab() => new TrivialBox3Generator() {
            Box = new Box3d(new AxisAlignedBox3d(new Vector3d(-1, -1, T), new Vector3d(2, 2, 2)))
        }.Generate().MakeDMesh();

        static Dyadic SixVolume(DMesh3 m) => BooleanOracle.ExactSixVolume(m);

        // vol = 9 (2 - 2^-20) + 2^-20
        static readonly Dyadic ExpectedSixVolume = Dyadic.Of(6 * (18 - 8 * T));
        static readonly Dyadic Six = Dyadic.Of(6);

        [Fact]
        public void Reproduction1_IsCleanAndExact_WithoutSafeRounding()
        {
            DMesh3 a = CubeWithSplitEdge();
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReasons(a));
            Assert.Equal(Six, SixVolume(a));
            var boolean = new ExactMeshBoolean(a, Slab(), Ops.Union) { SafeRounding = false };
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(ExpectedSixVolume, SixVolume(boolean.Result));
            // the arrangement gives a piece below the degenerate threshold, which SafeRounding then contracts
            Assert.Contains("degenerate", gs.MeshRobustRepair.NotCleanReasons(boolean.Result));
        }

        [Fact]
        public void SafeRounding_ContractsTheSmallPieces_WithinItsDisplacementBound()
        {
            var raw = new ExactMeshBoolean(CubeWithSplitEdge(), Slab(), Ops.Union) { SafeRounding = false };
            Assert.True(raw.Compute(), raw.Error);
            var boolean = new ExactMeshBoolean(CubeWithSplitEdge(), Slab(), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            // clean
            ResultChecks.AssertClean(boolean.Result);
            Assert.True(boolean.RoundingClean);
            // within the bound: the drift of the edits is at most MaxDisplacement times the diagonal, and the solid
            // is the exact one up to it (volume 17.999992370605316 instead of 17.99999237060547: T^2 / 6 less)
            MeshRoundingRepair r = boolean.Rounding;
            double diag = gs.MeshRepairMetrics.TriangleBounds(raw.Result).DiagonalLength;
            Assert.True(r.MaxDrift > 0 && r.MaxDrift <= r.MaxDisplacement * diag, $"drift {r.MaxDrift:G3}");
            // (a sanity check of the volume, not the proven per-edit form: see MeshRoundingRepair)
            double dv = Math.Abs((SixVolume(boolean.Result) - SixVolume(raw.Result)).ToDouble()) / 6;
            Assert.True(dv <= 2 * r.MaxDrift * Invariance.Area(raw.Result), $"volume change {dv:G3}");
            Assert.Equal("", BooleanOracle.SameSolid(boolean.Result, raw.Result, 3, r.MaxDrift));
            Assert.NotEqual(ExpectedSixVolume, SixVolume(boolean.Result));
        }

        // the unit cube with three splits at 2^-20 in its bottom face, one of them of a new edge: the triangle
        // (2^-20,2^-20,0), (2^-20,0,0), (0,0,0) has area 2^-41, below the threshold already in the input
        static DMesh3 CubeWithDegenerateTriangle()
        {
            var m = new DMesh3();
            Vector3d[] v = {
                new Vector3d(0, 0, 0), new Vector3d(0, 0, 1), new Vector3d(0, 1, 1), new Vector3d(0, 1, 0),
                new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(1, 1, 1), new Vector3d(1, 0, 1),
                new Vector3d(T, 0, 0), new Vector3d(T, 1, 0), new Vector3d(T, T, 0) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            const int p8 = 8, r = 9, q = 10;
            int[][] tris = {
                new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 }, new[] { 0, p8, 7 }, new[] { 0, 7, 1 },
                new[] { 3, 2, 6 }, new[] { r, 6, 5 }, new[] { q, r, 5 }, new[] { q, 5, p8 }, new[] { 1, 7, 6 }, new[] { 1, 6, 2 },
                new[] { p8, 4, 7 }, new[] { p8, 5, 4 }, new[] { q, 0, 3 }, new[] { q, p8, 0 }, new[] { r, 3, 6 }, new[] { r, q, 3 } };
            foreach (int[] t in tris) Assert.True(m.AppendTriangle(t[0], t[1], t[2]) >= 0);
            return m;
        }

        [Fact]
        public void Reproduction1b_IsExact_WithoutSafeRounding()
        {
            DMesh3 a = CubeWithDegenerateTriangle();
            ResultChecks.AssertClosed(a);
            Assert.Equal(Six, SixVolume(a));
            Assert.Equal("degenerate triangles", gs.MeshRobustRepair.NotCleanReasons(a));
            Assert.Equal(Six, SixVolume(ExactMeshBoolean.Resolve(a, false)));
        }

        [Fact]
        public void Resolve_RepairsTheDegenerateInputTriangle_WithinItsDisplacementBound()
        {
            DMesh3 a = CubeWithDegenerateTriangle();
            var op = new ExactMeshBoolean(a, new DMesh3(), Ops.Union);
            Assert.True(op.Compute(), op.Error);
            // clean
            ResultChecks.AssertClean(op.Result);
            Assert.True(op.RoundingClean);
            // within the bound, and the cube up to it (volume 0.99999984105428 instead of 1: the vertex (2^-20,0,0) on
            // the cube's edge is moved into the face)
            MeshRoundingRepair r = op.Rounding;
            double diag = gs.MeshRepairMetrics.TriangleBounds(a).DiagonalLength;
            Assert.True(r.MaxDrift > 0 && r.MaxDrift <= r.MaxDisplacement * diag, $"drift {r.MaxDrift:G3}");
            Assert.Equal("", BooleanOracle.SameSolid(op.Result, a, 1, r.MaxDrift));
            double dv = Math.Abs((SixVolume(op.Result) - Six).ToDouble()) / 6;
            Assert.True(dv <= 2 * r.MaxDrift * Invariance.Area(a), $"volume change {dv:G3}");
        }
    }
}
