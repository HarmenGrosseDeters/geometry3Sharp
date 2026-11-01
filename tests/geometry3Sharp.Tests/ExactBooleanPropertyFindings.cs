using System;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Defects the Boolean property tests found (ExactBooleanMetamorphicTests, ExactBooleanPropertyTests), pinned
    /// with minimal procedural reproductions and skipped until fixed in their own PR (tier L). The active tests
    /// check that each reproduction still holds without the defective step, so a fix can be verified by unskipping.
    ///
    /// Finding 1: SafeRounding (on by default) treats every triangle with an area below 1e-12 diagonal^2 as a
    /// rounding sliver and repairs it by moving vertices, also where the triangle and its vertices are exact (dyadic
    /// input, nothing was rounded). docs/OPERATIONS.md says it repairs "the slivers and self-intersections the
    /// rounding leaves". The surface moves by up to the sliver's width (about 2^-20 here, 1e-6 of the size), so a
    /// result that is exactly representable comes out inexact. Found as volume differences of 1e-8 (relative) in
    /// the representation campaign (sliver splits); SafeRounding = false gives the exact results. Expected: a
    /// triangle whose vertices are exact (no implicit point was rounded) is kept as the arrangement gives it.
    ///
    /// Design question 1b (not a defect by the documentation): an input that is already not clean (a triangle
    /// below the threshold, MeshRobustRepair "degenerate triangles") is repaired too, which moves its surface.
    /// That may be the intended repair; the owner decides whether SafeRounding should leave input triangles alone.
    /// </summary>
    public class ExactBooleanPropertyFindings
    {
        const string Finding1 = "Finding 1 of the Boolean property tests: SafeRounding moves exact vertices of pieces below its degenerate threshold";
        const string Question1b = "Design question 1b of the Boolean property tests: should SafeRounding repair degenerate triangles of the input?";

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
            // the arrangement gives a piece below the degenerate threshold, which SafeRounding then repairs
            Assert.Contains("degenerate", gs.MeshRobustRepair.NotCleanReasons(boolean.Result));
        }

        [Fact(Skip = Finding1)]
        public void SafeRounding_KeepsExactVertices()
        {
            var boolean = new ExactMeshBoolean(CubeWithSplitEdge(), Slab(), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            // now 17.999992370605316 instead of 17.99999237060547
            Assert.Equal(ExpectedSixVolume, SixVolume(boolean.Result));
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

        [Fact(Skip = Question1b)]
        public void Resolve_DegenerateInputTriangle()
        {
            // now 0.99999984105428 instead of 1: the vertex (2^-20,0,0) on the cube's edge is moved into the face
            Assert.Equal(Six, SixVolume(ExactMeshBoolean.Resolve(CubeWithDegenerateTriangle())));
        }
    }
}
