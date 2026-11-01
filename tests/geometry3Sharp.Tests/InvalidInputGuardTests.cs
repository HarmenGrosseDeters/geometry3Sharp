using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Invalid parameters of the kind docs/ROBUSTNESS_SWEEP.md lists (R13, R15, S26, S28, S33): NaN or infinite
    /// values, and tolerances that give no step at all. They must be refused with the status the operation has, leave
    /// no stale result, and leave normal inputs alone.
    /// </summary>
    public class InvalidInputGuardTests
    {
        static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

        static DMesh3 Sphere(int n = 8) => new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();

        static void AssertClosed(DMesh3 mesh)
        {
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(mesh.IsClosed());
            Assert.True(Invariance.AllFinite(mesh));
        }

        // ---- Reducer: a NaN edge length (R13) ----

        [Fact]
        public void ReduceToEdgeLength_NaN_AcceptsNoCollapse()
        {
            DMesh3 mesh = Sphere(), before = new DMesh3(mesh);
            new Reducer(mesh).ReduceToEdgeLength(double.NaN);
            Assert.Equal("", Invariance.Difference(before, mesh));
            AssertClosed(mesh);
        }

        [Fact]
        public void FastCollapsePass_NaN_AcceptsNoCollapse()
        {
            DMesh3 mesh = Sphere(), before = new DMesh3(mesh);
            new Reducer(mesh).FastCollapsePass(double.NaN, 2);
            Assert.Equal("", Invariance.Difference(before, mesh));
            AssertClosed(mesh);
        }

        [Fact]
        public void ReduceToEdgeLength_NaN_DoesNotLeakIntoTheNextReduction()
        {
            DMesh3 mesh = Sphere(), fresh = new DMesh3(mesh);
            var reused = new Reducer(mesh);
            reused.ReduceToEdgeLength(double.NaN);
            reused.ReduceToTriangleCount(50);
            new Reducer(fresh).ReduceToTriangleCount(50);
            Assert.True(mesh.TriangleCount <= 50);
            Assert.Equal("", Invariance.Difference(fresh, mesh));
            AssertClosed(mesh);
        }

        [Fact]
        public void ReduceToEdgeLength_Finite_CollapsesAsBefore_InEveryFrame()
        {
            DMesh3 source = Sphere();
            DMesh3 reduced = new DMesh3(source);
            new Reducer(reduced).ReduceToEdgeLength(0.2);
            // (pinned on the code before the NaN guard)
            Assert.Equal(ReducedTriangles, reduced.TriangleCount);
            AssertClosed(reduced);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(source, s);
                new Reducer(mesh).ReduceToEdgeLength(s.ApplyLength(0.2));
                Assert.True(mesh.TriangleCount < source.TriangleCount, s.Name);
                AssertClosed(mesh);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reduced, Invariance.MappedBack(mesh, s)));
            }
        }
        const int ReducedTriangles = 364;

        // ---- MeshIsoCurves: a field that is NaN (R15) ----

        static void AssertGraphBits(DGraph3 expected, DGraph3 actual)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            Assert.Equal(expected.EdgeCount, actual.EdgeCount);
            foreach (int v in expected.VertexIndices())
                Assert.Equal(expected.GetVertex(v), actual.GetVertex(v));
        }

        [Theory]
        [InlineData(MeshIsoCurves.RootfindingModes.SingleLerp, false)]
        [InlineData(MeshIsoCurves.RootfindingModes.LerpSteps, false)]
        [InlineData(MeshIsoCurves.RootfindingModes.Bisection, false)]
        [InlineData(MeshIsoCurves.RootfindingModes.SingleLerp, true)]
        public void IsoCurves_NaNField_NoGeometry_InEveryFrame(MeshIsoCurves.RootfindingModes mode, bool precompute)
        {
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(Sphere(), s);
                var iso = new MeshIsoCurves(mesh, x => x.z - double.NaN) { RootMode = mode, PrecomputeVertexValues = precompute };
                iso.Compute();
                Assert.Equal(0, iso.Graph.VertexCount);
                Assert.Equal(0, iso.Graph.EdgeCount);
            }
        }

        [Fact]
        public void IsoCurves_NaNVertexValues_NoGeometry()
        {
            DMesh3 mesh = Sphere();
            var iso = new MeshIsoCurves(mesh, x => 0) { VertexValueF = v => double.NaN };
            iso.Compute();
            Assert.Equal(0, iso.Graph.VertexCount);
        }

        [Theory]
        [InlineData(MeshIsoCurves.RootfindingModes.SingleLerp)]
        [InlineData(MeshIsoCurves.RootfindingModes.LerpSteps)]
        [InlineData(MeshIsoCurves.RootfindingModes.Bisection)]
        public void IsoCurves_NaNElsewhere_KeepsTheCurvesOfTheFiniteTriangles(MeshIsoCurves.RootfindingModes mode)
        {
            // The field is NaN far above the level: the curve is the one of the finite field, bit for bit. No spurious
            // curve along the border of the NaN, which Bisection would give: a NaN is not below the level.
            DMesh3 mesh = Sphere(16);
            var finite = new MeshIsoCurves(mesh, x => x.z - 0.1) { RootMode = mode };
            finite.Compute();
            Assert.True(finite.Graph.EdgeCount > 0);
            var partial = new MeshIsoCurves(mesh, x => (x.z > 0.6) ? double.NaN : x.z - 0.1) { RootMode = mode };
            partial.Compute();
            AssertGraphBits(finite.Graph, partial.Graph);
        }

        [Fact]
        public void IsoCurves_OppositeInfinitiesInOneTriangle_NoNaNGeometry()
        {
            var iso = new MeshIsoCurves(Sphere(), x => (x.z > 0.1) ? double.PositiveInfinity : double.NegativeInfinity);
            iso.Compute();
            Assert.Equal(0, iso.Graph.VertexCount);
        }

        [Fact]
        public void IsoCurves_OneInfiniteValue_StillGivesFiniteCurve()
        {
            // not an invalid field: "far above" as +infinity, next to finite values. The crossing is defined.
            var iso = new MeshIsoCurves(Sphere(), x => (x.z > 0.1) ? double.PositiveInfinity : -1);
            iso.Compute();
            Assert.True(iso.Graph.EdgeCount > 0);
            Assert.All(iso.Graph.Vertices(), v => Assert.True(v.IsFinite));
        }

        // ---- MeshIsoCurves: a field that is infinite on one side (R15, eg Math.Log(0)) ----

        public static IEnumerable<object[]> InfiniteFields()
        {
            foreach (MeshIsoCurves.RootfindingModes mode in Enum.GetValues(typeof(MeshIsoCurves.RootfindingModes)))
                foreach (double bad in new[] { double.NegativeInfinity, double.PositiveInfinity })
                    foreach (bool precompute in new[] { false, true })
                        yield return new object[] { mode, bad, precompute };
        }

        static HashSet<Vector3d> VerticesOf(DMesh3 mesh, Func<Vector3d, bool> where) =>
            new HashSet<Vector3d>(mesh.VertexIndices().Select(mesh.GetVertex).Where(where));

        // a finite value above the level, the infinite one below it
        static Func<Vector3d, double> OneSidedField(double bad) => x => (x.z > 0.1) ? -Math.Sign(bad) : bad;

        [Theory]
        [MemberData(nameof(InfiniteFields))]
        public void IsoCurves_OneInfiniteValue_CrossesAtTheFiniteVertex(MeshIsoCurves.RootfindingModes mode, double bad, bool precompute)
        {
            DMesh3 mesh = Sphere(16), before = new DMesh3(mesh);
            var iso = new MeshIsoCurves(mesh, OneSidedField(bad)) { RootMode = mode, PrecomputeVertexValues = precompute };
            iso.Compute();
            Assert.True(iso.Graph.EdgeCount > 0);
            Assert.All(iso.Graph.Vertices(), v => Assert.True(v.IsFinite));
            if (mode != MeshIsoCurves.RootfindingModes.Bisection) {
                // the root finders that interpolate cannot look between the vertices: the finite end of the edge
                HashSet<Vector3d> finiteVertices = VerticesOf(mesh, x => x.z > 0.1);
                Assert.All(iso.Graph.Vertices(), v => Assert.Contains(v, finiteVertices));
            } else {
                // bisection finds the jump itself: within an edge length / 2^6 of the level
                double reach = before.EdgeIndices().Max(e => { Index2i ev = before.GetEdgeV(e); return before.GetVertex(ev.a).Distance(before.GetVertex(ev.b)); });
                Assert.All(iso.Graph.Vertices(), v => Assert.InRange(v.z, 0.1 - reach / 32, 0.1 + reach / 32));
            }

            // reuse: a second Compute() on the instance gives the same
            DGraph3 first = iso.Graph;
            iso.Compute();
            AssertGraphBits(first, iso.Graph);

            iso.SplitAtIsoCrossings();
            AssertClosed(mesh);
            if (mode != MeshIsoCurves.RootfindingModes.Bisection) {
                // a crossing at a vertex splits nothing: it would give an edge of length 0 and triangles without area
                Assert.Equal("", Invariance.Difference(before, mesh));
            } else {
                Assert.True(mesh.VertexCount > before.VertexCount);
                Assert.All(mesh.EdgeIndices(), e => { Index2i ev = mesh.GetEdgeV(e); Assert.True(mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)) > 0); });
            }
        }

        [Theory]
        [MemberData(nameof(InfiniteFields))]
        public void IsoCurves_OneInfiniteValue_InEveryFrame(MeshIsoCurves.RootfindingModes mode, double bad, bool precompute)
        {
            DMesh3 source = Sphere(16);
            Func<Vector3d, double> field = OneSidedField(bad);
            var reference = new MeshIsoCurves(new DMesh3(source), field) { RootMode = mode, PrecomputeVertexValues = precompute };
            reference.Compute();
            Assert.True(reference.Graph.EdgeCount > 0);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(source, s);
                var iso = new MeshIsoCurves(mesh, x => field(s.Inverse(x))) { RootMode = mode, PrecomputeVertexValues = precompute };
                iso.Compute();
                Assert.Equal(reference.Graph.VertexCount, iso.Graph.VertexCount);
                Assert.Equal(reference.Graph.EdgeCount, iso.Graph.EdgeCount);
                Assert.All(iso.Graph.Vertices(), v => Assert.True(v.IsFinite, s.Name));
                if (s.IsExact)
                    foreach (int v in reference.Graph.VertexIndices())
                        Assert.Equal(reference.Graph.GetVertex(v), s.Inverse(iso.Graph.GetVertex(v)));
            }
        }

        // One triangle with the given values at its corners, in every order of the corners: the Graph must not depend
        // on which vertex has which value.
        public static IEnumerable<object[]> InfiniteCorners()
        {
            double inf = double.PositiveInfinity;
            foreach (MeshIsoCurves.RootfindingModes mode in new[] { MeshIsoCurves.RootfindingModes.SingleLerp, MeshIsoCurves.RootfindingModes.LerpSteps }) {
                yield return new object[] { mode, 0.0, -1.0, inf, 2, 1 };       // through the 0, to the finite vertex
                yield return new object[] { mode, 0.0, 1.0, -inf, 2, 1 };
                yield return new object[] { mode, 0.0, 0.0, inf, 2, 1 };        // along the edge of the two 0
                yield return new object[] { mode, 0.0, 0.0, -inf, 2, 1 };
                yield return new object[] { mode, 0.0, inf, inf, 0, 0 };        // touches only the 0: nothing, as for finite values
                yield return new object[] { mode, 0.0, -inf, -inf, 0, 0 };
                yield return new object[] { mode, 1.0, 1.0, -inf, 2, 1 };       // two crossings, at the two finite vertices
                yield return new object[] { mode, -1.0, -1.0, inf, 2, 1 };
                yield return new object[] { mode, 1e308, 1e308, -inf, 2, 1 };   // the sum of the values overflows first
                yield return new object[] { mode, -1e308, -1e308, inf, 2, 1 };
                yield return new object[] { mode, 1.0, -inf, -inf, 1, 0 };      // both crossings at the one finite vertex
                yield return new object[] { mode, -1.0, inf, inf, 1, 0 };
                yield return new object[] { mode, -1.0, inf, -inf, 0, 0 };      // no crossing defined: nothing
                yield return new object[] { mode, 0.0, inf, -inf, 0, 0 };
                yield return new object[] { mode, 1e308, inf, -inf, 0, 0 };
                yield return new object[] { mode, double.NaN, inf, -1.0, 0, 0 };
            }
        }

        [Theory]
        [MemberData(nameof(InfiniteCorners))]
        public void IsoCurves_InfiniteCorners_FiniteGraph_InAnyVertexOrder(MeshIsoCurves.RootfindingModes mode, double a, double b, double c, int vertices, int edges)
        {
            var corners = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            var values = new[] { a, b, c };
            foreach (int[] order in new[] { new[] { 0, 1, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 0, 2, 1 }, new[] { 2, 1, 0 }, new[] { 1, 0, 2 } }) {
                var mesh = new DMesh3();
                foreach (int k in order)
                    mesh.AppendVertex(corners[k]);
                mesh.AppendTriangle(0, 1, 2);
                // (LerpSteps also samples the field: the values at the vertices, between them a value of the positive side)
                double[] byVertex = order.Select(k => values[k]).ToArray();
                var iso = new MeshIsoCurves(mesh, x => {
                    for (int v = 0; v < 3; ++v)
                        if (mesh.GetVertex(v) == x) return byVertex[v];
                    return 0.5;
                }) { RootMode = mode, VertexValueF = v => byVertex[v] };
                iso.Compute();
                string where = string.Join(",", byVertex);
                Assert.True(vertices == iso.Graph.VertexCount, $"{where}: {iso.Graph.VertexCount} vertices");
                Assert.True(edges == iso.Graph.EdgeCount, $"{where}: {iso.Graph.EdgeCount} edges");
                // at the vertices of the mesh: the finite end of a crossed edge, or a 0
                Assert.All(iso.Graph.Vertices(), p => {
                    Assert.True(p.IsFinite, where);
                    int k = Array.IndexOf(corners, p);
                    Assert.True(k >= 0, where);
                    Assert.False(double.IsInfinity(values[k]), where);
                });
            }
        }

        [Fact]
        public void IsoCurves_LerpSteps_NaNOrInfinityOnlyBetweenTheVertices_StaysFinite()
        {
            foreach (double between in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity }) {
                DMesh3 mesh = Sphere(16);
                HashSet<Vector3d> atVertices = VerticesOf(mesh, x => true);
                var iso = new MeshIsoCurves(mesh, x => atVertices.Contains(x) ? x.z - 0.1 : between) { RootMode = MeshIsoCurves.RootfindingModes.LerpSteps };
                iso.Compute();
                Assert.True(iso.Graph.EdgeCount > 0);
                Assert.All(iso.Graph.Vertices(), v => Assert.True(v.IsFinite, between.ToString()));
                iso.SplitAtIsoCrossings();
                AssertClosed(mesh);
            }
        }

        // ---- MeshIsoCurves: only the sign is known, -infinity on one side and +infinity on the other ----

        static DMesh3 Plate(int n = 20)
        {
            var mesh = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = i + j * (n + 1), b = a + 1, c = a + n + 2, d = a + n + 1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        static double SignLine(Vector3d x) => x.x - 0.37 * x.y - 3.3;
        static double SignField(Vector3d x) => (SignLine(x) < 0) ? double.NegativeInfinity : double.PositiveInfinity;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IsoCurves_Bisection_OppositeInfinities_FindsTheJump(bool precompute)
        {
            // bisection uses only the sign of the values: a good curve, along the line of the jump
            DMesh3 mesh = Plate(), before = new DMesh3(mesh);
            var iso = new MeshIsoCurves(mesh, SignField) { RootMode = MeshIsoCurves.RootfindingModes.Bisection, PrecomputeVertexValues = precompute };
            iso.Compute();
            Assert.Equal(41, iso.Graph.VertexCount);
            Assert.Equal(40, iso.Graph.EdgeCount);
            // 5 steps on edges up to sqrt(2) long, half of the last interval left; the gradient of the line is about 1
            Assert.All(iso.Graph.Vertices(), v => {
                Assert.True(v.IsFinite);
                Assert.InRange(SignLine(v), -0.04, 0.04);
            });
            DGraph3 first = iso.Graph;
            iso.Compute();
            AssertGraphBits(first, iso.Graph);
            iso.SplitAtIsoCrossings();
            Assert.True(mesh.VertexCount > before.VertexCount);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(mesh));
        }

        [Theory]
        [InlineData(MeshIsoCurves.RootfindingModes.SingleLerp)]
        [InlineData(MeshIsoCurves.RootfindingModes.LerpSteps)]
        public void IsoCurves_InterpolatingModes_OppositeInfinities_NoGeometry(MeshIsoCurves.RootfindingModes mode)
        {
            // no line through the two values, so no crossing to interpolate: nothing, not NaN
            DMesh3 mesh = Plate(), before = new DMesh3(mesh);
            var iso = new MeshIsoCurves(mesh, SignField) { RootMode = mode };
            iso.Compute();
            Assert.Equal(0, iso.Graph.VertexCount);
            iso.SplitAtIsoCrossings();
            Assert.Equal("", Invariance.Difference(before, mesh));
        }

        [Fact]
        public void IsoCurves_Bisection_OppositeInfinities_InEveryFrame()
        {
            DMesh3 source = Plate();
            var reference = new MeshIsoCurves(new DMesh3(source), SignField) { RootMode = MeshIsoCurves.RootfindingModes.Bisection };
            reference.Compute();
            Assert.Equal(40, reference.Graph.EdgeCount);
            foreach (Similarity s in Similarity.All()) {
                var iso = new MeshIsoCurves(Invariance.Transformed(source, s), x => SignField(s.Inverse(x))) { RootMode = MeshIsoCurves.RootfindingModes.Bisection };
                iso.Compute();
                Assert.True(reference.Graph.VertexCount == iso.Graph.VertexCount, s.Name);
                Assert.True(reference.Graph.EdgeCount == iso.Graph.EdgeCount, s.Name);
                Assert.All(iso.Graph.Vertices(), v => Assert.True(v.IsFinite, s.Name));
                if (s.IsExact)
                    foreach (int v in reference.Graph.VertexIndices())
                        Assert.Equal(reference.Graph.GetVertex(v), s.Inverse(iso.Graph.GetVertex(v)));
            }
        }

        // One triangle in every order of its corners; between the corners the field has the sign of the jump between
        // corners 1 and 2 (x below y: positive). Bisection finds a point on the crossed edges.
        [Theory]
        [InlineData(double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity, 2, 1)]    // two crossings, opposite infinities
        [InlineData(double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity, 2, 1)]
        [InlineData(double.NegativeInfinity, 1.0, double.PositiveInfinity, 2, 1)]                        // and a finite value
        [InlineData(1.0, double.NegativeInfinity, double.PositiveInfinity, 2, 1)]
        [InlineData(0.0, double.NegativeInfinity, double.PositiveInfinity, 2, 1)]                        // through the 0, to the jump
        [InlineData(double.NaN, double.NegativeInfinity, double.PositiveInfinity, 0, 0)]                 // a NaN: nothing
        public void IsoCurves_Bisection_InfiniteCorners_InAnyVertexOrder(double a, double b, double c, int vertices, int edges)
        {
            var corners = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            var values = new[] { a, b, c };
            foreach (int[] order in new[] { new[] { 0, 1, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 0, 2, 1 }, new[] { 2, 1, 0 }, new[] { 1, 0, 2 } }) {
                var mesh = new DMesh3();
                foreach (int k in order)
                    mesh.AppendVertex(corners[k]);
                mesh.AppendTriangle(0, 1, 2);
                var iso = new MeshIsoCurves(mesh, x => {
                    for (int k = 0; k < 3; ++k)
                        if (corners[k] == x) return values[k];
                    // the sign changes once along each edge: towards the positive corner it is positive
                    double t = 0;
                    for (int k = 0; k < 3; ++k)
                        if (values[k] > 0) t = Math.Max(t, x.Dot(corners[k]));
                    return (t >= 0.5) ? 1 : -1;
                }) { RootMode = MeshIsoCurves.RootfindingModes.Bisection };
                iso.Compute();
                string where = string.Join(",", order.Select(k => values[k]));
                Assert.True(vertices == iso.Graph.VertexCount, $"{where}: {iso.Graph.VertexCount} vertices");
                Assert.True(edges == iso.Graph.EdgeCount, $"{where}: {iso.Graph.EdgeCount} edges");
                Assert.All(iso.Graph.Vertices(), p => Assert.True(p.IsFinite, where));
            }
        }

        [Fact]
        public void IsoCurves_Split_CrossingsInsideTheEdges_StillSplit()
        {
            // the check for a crossing at a vertex is exact: it does not swallow a crossing next to one
            DMesh3 mesh = Sphere(16);
            int before = mesh.VertexCount;
            var iso = new MeshIsoCurves(mesh, x => x.z - 0.1);
            iso.Compute();
            iso.SplitAtIsoCrossings();
            Assert.True(mesh.VertexCount > before);
            AssertClosed(mesh);
        }

        [Fact]
        public void IsoCurves_ReusedAfterNaN_EqualsFreshInstance_AndSplitsNothing()
        {
            DMesh3 mesh = Sphere(), before = new DMesh3(mesh);
            double level = 0.1;
            var iso = new MeshIsoCurves(mesh, x => x.z - level);
            iso.Compute();
            Assert.True(iso.Graph.EdgeCount > 0);
            level = double.NaN;
            iso.Compute();
            Assert.Equal(0, iso.Graph.VertexCount);
            iso.SplitAtIsoCrossings();
            Assert.Equal("", Invariance.Difference(before, mesh));
            level = 0.1;
            iso.Compute();
            var fresh = new MeshIsoCurves(before, x => x.z - 0.1);
            fresh.Compute();
            Assert.True(fresh.Graph.EdgeCount > 0);
            AssertGraphBits(fresh.Graph, iso.Graph);
        }

        // ---- CellSize: NaN or infinite (S28) ----

        static DMesh3 SphereAt(double x) { DMesh3 m = Sphere(); MeshTransforms.Translate(m, new Vector3d(x, 0, 0)); return m; }

        static DMesh3 HillTerrain()
        {
            int n = 21;
            var mesh = new DMesh3();
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i)
                    mesh.AppendVertex(new Vector3d(i, j, 0.1 * i));
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    int a = i + j * n, b = a + 1, c = a + n + 1, d = a + n;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        public static IEnumerable<object[]> BadCellSizes() =>
            new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }.Select(x => new object[] { x });

        [Theory]
        [MemberData(nameof(BadCellSizes))]
        public void Solidify_NonFiniteCellSize_Fails(double cell)
        {
            foreach (var cfg in new[] {
                (MeshWindingNumberSolidify.Methods.DualContouring, true),
                (MeshWindingNumberSolidify.Methods.DualContouring, false),
                (MeshWindingNumberSolidify.Methods.MarchingCubes, false) }) {
                var sol = new MeshWindingNumberSolidify(Sphere()) { CellSize = cell, Method = cfg.Item1, Adaptive = cfg.Item2 };
                Assert.False(sol.Compute());
                Assert.Null(sol.Result);
            }
        }

        [Theory]
        [MemberData(nameof(BadCellSizes))]
        public void WindingBoolean_NonFiniteCellSize_Fails(double cell)
        {
            var b = new MeshWindingNumberBoolean(SphereAt(0), SphereAt(1), MeshWindingNumberBoolean.Operations.Union) { CellSize = cell };
            Assert.False(b.Compute());
            Assert.Null(b.Result);
        }

        [Theory]
        [MemberData(nameof(BadCellSizes))]
        public void TerrainDualContouring_NonFiniteCellSize_Fails(double cell)
        {
            var tdc = new TerrainDualContouring(HillTerrain()) { CellSize = cell };
            Assert.False(tdc.Compute());
            Assert.Null(tdc.Result);
            var off = new TerrainOffset(HillTerrain(), 1) { CellSize = cell };
            Assert.False(off.Compute());
            Assert.Null(off.Result);
        }

        [Fact]
        public void Solidify_ReusedAfterBadCellSize_EqualsFreshInstance_NoStaleResult()
        {
            var sol = new MeshWindingNumberSolidify(Sphere()) { GridResolution = 32 };
            Assert.True(sol.Compute());
            Assert.NotNull(sol.Result);
            sol.CellSize = double.PositiveInfinity;
            Assert.False(sol.Compute());
            Assert.Null(sol.Result);
            sol.CellSize = 0;
            Assert.True(sol.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(sol.Result));
            var fresh = new MeshWindingNumberSolidify(Sphere()) { GridResolution = 32 };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, sol.Result));
        }

        [Fact]
        public void WindingBoolean_ReusedAfterBadCellSize_EqualsFreshInstance_NoStaleResult()
        {
            Func<MeshWindingNumberBoolean> make = () =>
                new MeshWindingNumberBoolean(SphereAt(0), SphereAt(1), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32 };
            var b = make();
            Assert.True(b.Compute());
            b.CellSize = double.NaN;
            Assert.False(b.Compute());
            Assert.Null(b.Result);
            b.CellSize = 0;
            Assert.True(b.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
            var fresh = make();
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, b.Result));
        }

        [Fact]
        public void TerrainDualContouring_ReusedAfterBadCellSize_EqualsFreshInstance_NoStaleResult()
        {
            var tdc = new TerrainDualContouring(HillTerrain()) { GridResolution = 16 };
            Assert.True(tdc.Compute());
            tdc.CellSize = double.PositiveInfinity;
            Assert.False(tdc.Compute());
            Assert.Null(tdc.Result);
            tdc.CellSize = 0;
            Assert.True(tdc.Compute());
            Assert.True(tdc.Result.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(tdc.Result));
            var fresh = new TerrainDualContouring(HillTerrain()) { GridResolution = 16 };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, tdc.Result));
        }

        // ---- Solidify, winding boolean, terrain contouring: every failure after a success leaves no Result ----

        // cancels (or throws) from the call after Limit on; counts the calls, which may come from several threads
        sealed class CancelAfter
        {
            int calls;
            public int Limit = int.MaxValue;
            public bool Throw;
            public int Calls => calls;
            public void Arm(int limit, bool doThrow = false) { calls = 0; Limit = limit; Throw = doThrow; }
            public bool Check()
            {
                if (Interlocked.Increment(ref calls) <= Limit) return false;
                if (Throw) throw new InvalidOperationException("from CancelF");
                return true;
            }
        }

        // what a run reports. Set to something else, as many are 0 in a small test anyway.
        static readonly string[] SolidifyOutputs = { "NonManifoldTriangles", "NonManifoldRefinements", "WindingEvaluations", "HiddenGapEdges", "HiddenWallEdges",
            "GapEdgesPerLevel", "UsedCellSize", "RefinedCells", "RefineFactor", "PhaseMilliseconds" };
        static readonly string[] BooleanOutputs = { "NonManifoldTriangles", "WindingEvaluations", "HiddenGapEdges", "HiddenWallEdges", "HiddenSamples",
            "GapEdgesPerLevel", "UsedCellSize", "RefinedCells", "RefineFactor", "PhaseMilliseconds" };
        static readonly string[] ContouringOutputs = { "FinestCells", "LeafCells", "BoundaryCells", "SkippedTriangles", "BoundaryFallbacks", "SplitLeaves", "RemovedTriangles" };

        static void MakeStale(object op, string[] outputs)
        {
            foreach (string name in outputs) {
                FieldInfo f = op.GetType().GetField(name);
                Assert.NotNull(f);
                object value = f.GetValue(op);
                if (value is int) f.SetValue(op, 7);
                else if (value is double) f.SetValue(op, 7.5);
                else if (value is List<int>) f.SetValue(op, new List<int> { 7 });
                else if (value is Dictionary<string, long>) ((Dictionary<string, long>)value)["stale"] = 7;
                else if (value is List<KeyValuePair<Vector3d, double>>) f.SetValue(op, new List<KeyValuePair<Vector3d, double>> { new KeyValuePair<Vector3d, double>(Vector3d.One, 7) });
                else Assert.Fail("no stale value for " + name);
            }
        }

        // every public field and property, except the inputs that are references and the timings (only which phases ran)
        static void AssertSameState(object fresh, object reused, params string[] except)
        {
            Type type = fresh.GetType();
            var members = new List<KeyValuePair<string, Func<object, object>>>();
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                members.Add(new KeyValuePair<string, Func<object, object>>(f.Name, f.GetValue));
            foreach (PropertyInfo q in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (q.CanRead && q.GetIndexParameters().Length == 0)
                    members.Add(new KeyValuePair<string, Func<object, object>>(q.Name, o => q.GetValue(o, null)));
            Assert.NotEmpty(members);
            foreach (var m in members) {
                object a = m.Value(fresh), b = m.Value(reused);
                if (except.Contains(m.Key) || a is Delegate || a is DMesh3 || b is DMesh3 || a is IList<Vector3d>)
                    continue;
                if (a is Dictionary<string, long>)
                    Assert.True(((Dictionary<string, long>)a).Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(((Dictionary<string, long>)b).Keys.OrderBy(k => k, StringComparer.Ordinal)), m.Key);
                else if (a is System.Collections.IEnumerable)
                    Assert.True(((System.Collections.IEnumerable)a).Cast<object>().SequenceEqual(((System.Collections.IEnumerable)b).Cast<object>()), m.Key);
                else
                    Assert.True(Equals(a, b), $"{m.Key}: fresh {a}, reused {b}");
            }
        }

        [Theory]
        [MemberData(nameof(SolidifyConfigs))]
        public void Solidify_AfterAFailure_StateEqualsAFreshFailure(MeshWindingNumberSolidify.Methods method, bool adaptive)
        {
            DMesh3 input = Sphere();
            Func<MeshWindingNumberSolidify> make = () => new MeshWindingNumberSolidify(input) { GridResolution = 32, Method = method, Adaptive = adaptive };
            foreach (Action<MeshWindingNumberSolidify> fail in new Action<MeshWindingNumberSolidify>[] {
                x => x.CellSize = double.NaN, x => x.Mesh = new DMesh3(), x => x.CellSize = 1e-30 }) {
                var sol = make();
                Assert.True(sol.Compute());
                MakeStale(sol, SolidifyOutputs);
                fail(sol);
                Assert.False(sol.Compute());
                var fresh = make();
                fail(fresh);
                Assert.False(fresh.Compute());
                Assert.Null(sol.Result);
                AssertSameState(fresh, sol);
            }
        }

        [Fact]
        public void WindingBoolean_AfterAFailure_StateEqualsAFreshFailure()
        {
            Func<MeshWindingNumberBoolean> make = () => new MeshWindingNumberBoolean(SphereAt(0), SphereAt(1), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32 };
            foreach (Action<MeshWindingNumberBoolean> fail in new Action<MeshWindingNumberBoolean>[] {
                x => x.CellSize = double.NaN, x => { x.MeshA = new DMesh3(); x.MeshB = new DMesh3(); }, x => x.CellSize = 1e-30 }) {
                var op = make();
                Assert.True(op.Compute());
                MakeStale(op, BooleanOutputs);
                fail(op);
                Assert.False(op.Compute());
                var fresh = make();
                fail(fresh);
                Assert.False(fresh.Compute());
                Assert.Null(op.Result);
                AssertSameState(fresh, op);
            }
        }

        [Fact]
        public void WindingBoolean_EmptyInputSuccess_StateEqualsAFreshOne()
        {
            // the path that returns early with an empty mesh: nothing of the run before it may stay
            Func<MeshWindingNumberBoolean> make = () => new MeshWindingNumberBoolean(SphereAt(0), SphereAt(1), MeshWindingNumberBoolean.Operations.Intersection) { GridResolution = 32 };
            var op = make();
            Assert.True(op.Compute());
            MakeStale(op, BooleanOutputs);
            op.MeshB = new DMesh3();
            Assert.True(op.Compute());
            Assert.Equal(0, op.Result.TriangleCount);
            var fresh = make();
            fresh.MeshB = new DMesh3();
            Assert.True(fresh.Compute());
            AssertSameState(fresh, op);
        }

        [Fact]
        public void TerrainDualContouring_AfterAFailure_StateEqualsAFreshFailure()
        {
            DMesh3 input = HillTerrain();
            Func<TerrainDualContouring> make = () => new TerrainDualContouring(input) { GridResolution = 16 };
            foreach (Action<TerrainDualContouring> fail in new Action<TerrainDualContouring>[] {
                x => x.CellSize = double.NaN, x => x.Mesh = new DMesh3(), x => x.SamplesPerCell = 0, x => x.CellSize = 1e-6, x => x.CellSize = 100 }) {
                var tdc = make();
                Assert.True(tdc.Compute());
                MakeStale(tdc, ContouringOutputs);
                fail(tdc);
                Assert.False(tdc.Compute());
                var fresh = make();
                fail(fresh);
                Assert.False(fresh.Compute());
                Assert.Null(tdc.Result);
                AssertSameState(fresh, tdc);
            }
        }

        // A failure at each point of the run where it can stop (at most about 8 of them, and always the last),
        // each after a success on the same instance. A cancel can come too late to count: then there is a result.
        static void AssertCancelLeavesNoResult(CancelAfter cancel, Func<bool> compute, Func<DMesh3> result)
        {
            cancel.Arm(int.MaxValue);
            Assert.True(compute());
            Assert.NotNull(result());
            int calls = cancel.Calls;
            Assert.True(calls > 0);
            int failures = 0;
            var limits = new SortedSet<int> { calls - 1 };
            for (int limit = 0; limit < calls; limit += Math.Max(1, calls / 8))
                limits.Add(limit);
            foreach (int limit in limits) {
                cancel.Arm(int.MaxValue);
                Assert.True(compute());
                Assert.NotNull(result());
                cancel.Arm(limit);
                bool ok = compute();
                Assert.True(ok == (result() != null), $"cancel after {limit} of {calls} calls: Compute {ok}");
                if (ok == false) failures++;
                // an exception is a failure too
                cancel.Arm(int.MaxValue);
                Assert.True(compute());
                cancel.Arm(limit, true);
                bool finished = false;
                try {
                    compute();
                    finished = true;
                } catch (Exception e) when (e is InvalidOperationException || e is AggregateException) {
                }
                if (finished == false)
                    Assert.True(result() == null, $"exception after {limit} of {calls} calls");
            }
            Assert.True(failures > 0, "no cancel made Compute fail");
            cancel.Arm(int.MaxValue);
        }

        // a failure by the parameters: change, compute on the instance with an earlier result, undo
        static void AssertFailsWithoutResult(Func<bool> compute, Func<DMesh3> result, Action set, Action undo, string what)
        {
            Assert.True(compute(), what);
            Assert.NotNull(result());
            set();
            Assert.False(compute(), what);
            Assert.True(result() == null, what);
            undo();
        }

        public static IEnumerable<object[]> SolidifyConfigs()
        {
            yield return new object[] { MeshWindingNumberSolidify.Methods.DualContouring, true };
            yield return new object[] { MeshWindingNumberSolidify.Methods.DualContouring, false };
            yield return new object[] { MeshWindingNumberSolidify.Methods.MarchingCubes, false };
        }

        [Theory]
        [MemberData(nameof(SolidifyConfigs))]
        public void Solidify_EveryFailure_LeavesNoResult_ThenEqualsFreshInstance(MeshWindingNumberSolidify.Methods method, bool adaptive)
        {
            DMesh3 input = Sphere();
            var cancel = new CancelAfter();
            var sol = new MeshWindingNumberSolidify(input) { GridResolution = 32, Method = method, Adaptive = adaptive, CancelF = cancel.Check };
            AssertFailsWithoutResult(sol.Compute, () => sol.Result, () => sol.Mesh = new DMesh3(), () => sol.Mesh = input, "empty input");
            AssertFailsWithoutResult(sol.Compute, () => sol.Result, () => sol.CellSize = 1e-30, () => sol.CellSize = 0, "cell size too small");
            AssertFailsWithoutResult(sol.Compute, () => sol.Result, () => sol.CellSize = double.NaN, () => sol.CellSize = 0, "NaN cell size");
            // what the last run reported is gone with it
            foreach (Action fail in new Action[] { () => sol.CellSize = double.NaN, () => sol.Mesh = new DMesh3() }) {
                Assert.True(sol.Compute());
                Assert.True(sol.UsedCellSize > 0);
                sol.NonManifoldTriangles = 7;
                fail();
                Assert.False(sol.Compute());
                Assert.Equal(0, sol.UsedCellSize);
                Assert.Equal(0, sol.NonManifoldTriangles);
                sol.CellSize = 0; sol.Mesh = input;
            }
            AssertCancelLeavesNoResult(cancel, sol.Compute, () => sol.Result);
            Assert.True(sol.Compute());
            if (method == MeshWindingNumberSolidify.Methods.MarchingCubes) {
                // (two fresh instances differ too, see S31 in docs/ROBUSTNESS_SWEEP.md: nothing to compare with)
                Assert.True(sol.Result.TriangleCount > 0);
                Assert.True(Invariance.AllFinite(sol.Result));
                return;
            }
            Assert.Equal("", MeshRobustRepair.NotCleanReason(sol.Result));
            var fresh = new MeshWindingNumberSolidify(input) { GridResolution = 32, Method = method, Adaptive = adaptive };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, sol.Result));
            // (the number of evaluations varies between runs on one input: parallel, with a cache)
            AssertSameState(fresh, sol, "WindingEvaluations");
        }

        [Fact]
        public void Solidify_FinishPostProcess_AfterAFailure_DoesNothing()
        {
            // it reads Result: a stopped run followed by a failed one leaves nothing to finish
            var sol = new MeshWindingNumberSolidify(Sphere()) { GridResolution = 32 };
            Assert.True(sol.Compute());
            sol.Mesh = new DMesh3();
            Assert.False(sol.Compute());
            Assert.False(sol.StoppedOnSelfIntersections);
            sol.FinishPostProcess();
            Assert.Null(sol.Result);
        }

        [Fact]
        public void WindingBoolean_EveryFailure_LeavesNoResult_ThenEqualsFreshInstance()
        {
            DMesh3 a = SphereAt(0), b = SphereAt(1);
            var cancel = new CancelAfter();
            var op = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32, CancelF = cancel.Check };
            AssertFailsWithoutResult(op.Compute, () => op.Result, () => { op.MeshA = new DMesh3(); op.MeshB = new DMesh3(); }, () => { op.MeshA = a; op.MeshB = b; }, "both inputs empty");
            AssertFailsWithoutResult(op.Compute, () => op.Result, () => op.CellSize = 1e-30, () => op.CellSize = 0, "cell size too small");
            AssertFailsWithoutResult(op.Compute, () => op.Result, () => op.CellSize = double.PositiveInfinity, () => op.CellSize = 0, "infinite cell size");
            AssertCancelLeavesNoResult(cancel, op.Compute, () => op.Result);
            Assert.True(op.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result));
            var fresh = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32 };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, op.Result));
            // (the number of evaluations varies between runs on one input: parallel, with a cache)
            AssertSameState(fresh, op, "WindingEvaluations");
        }

        [Fact]
        public void WindingBoolean_EmptyResult_IsASuccess_NotAFailure()
        {
            // one empty input in an intersection: Result is an empty mesh, not null
            var op = new MeshWindingNumberBoolean(SphereAt(0), new DMesh3(), MeshWindingNumberBoolean.Operations.Intersection) { GridResolution = 32 };
            Assert.True(op.Compute());
            Assert.NotNull(op.Result);
            Assert.Equal(0, op.Result.TriangleCount);
        }

        [Fact]
        public void TerrainDualContouring_EveryFailure_LeavesNoResult_ThenEqualsFreshInstance()
        {
            DMesh3 input = HillTerrain();
            var cancel = new CancelAfter();
            var tdc = new TerrainDualContouring(input) { GridResolution = 16, CancelF = cancel.Check };
            AssertFailsWithoutResult(tdc.Compute, () => tdc.Result, () => tdc.Mesh = new DMesh3(), () => tdc.Mesh = input, "empty input");
            AssertFailsWithoutResult(tdc.Compute, () => tdc.Result, () => tdc.SamplesPerCell = 0, () => tdc.SamplesPerCell = 4, "no samples");
            AssertFailsWithoutResult(tdc.Compute, () => tdc.Result, () => tdc.CellSize = 1e-6, () => tdc.CellSize = 0, "too many cells");
            // these fail only after the mesh was built, and it is empty
            AssertFailsWithoutResult(tdc.Compute, () => tdc.Result, () => tdc.CellSize = 100, () => tdc.CellSize = 0, "cells larger than the footprint");
            AssertFailsWithoutResult(tdc.Compute, () => tdc.Result, () => tdc.CellSize = double.NaN, () => tdc.CellSize = 0, "NaN cell size");
            // what the last run reported is gone with it
            foreach (Action fail in new Action[] { () => tdc.CellSize = double.NaN, () => tdc.Mesh = new DMesh3(), () => tdc.SamplesPerCell = 0 }) {
                Assert.True(tdc.Compute());
                Assert.True(tdc.FinestCells > 0);
                tdc.SplitLeaves = 7; tdc.RemovedTriangles = 7;
                fail();
                Assert.False(tdc.Compute());
                Assert.Equal(0, tdc.FinestCells);
                Assert.Equal(0, tdc.SplitLeaves);
                Assert.Equal(0, tdc.RemovedTriangles);
                tdc.CellSize = 0; tdc.Mesh = input; tdc.SamplesPerCell = 4;
            }
            AssertCancelLeavesNoResult(cancel, tdc.Compute, () => tdc.Result);
            Assert.True(tdc.Compute());
            Assert.True(tdc.Result.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(tdc.Result));
            var fresh = new TerrainDualContouring(input) { GridResolution = 16 };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, tdc.Result));
            AssertSameState(fresh, tdc);
        }

        [Fact]
        public void TerrainDualContouring_TooFewPoints_LeavesNoResult()
        {
            var points = new List<Vector3d>();
            for (int j = 0; j < 20; ++j)
                for (int i = 0; i < 20; ++i)
                    points.Add(new Vector3d(i, j, 0.1 * i));
            var tdc = new TerrainDualContouring(points) { GridResolution = 8 };
            Assert.True(tdc.Compute());
            Assert.NotNull(tdc.Result);
            tdc.Points = new List<Vector3d> { Vector3d.Zero, Vector3d.AxisX };
            Assert.False(tdc.Compute());
            Assert.Null(tdc.Result);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Failure_LeavesNoResult_InEveryFrame(string name)
        {
            Similarity s = Similarity.ByName(name);
            bool stop = false;
            Func<bool> cancelF = () => stop;

            var sol = new MeshWindingNumberSolidify(Invariance.Transformed(Sphere(), s)) { GridResolution = 32, CancelF = cancelF };
            var op = new MeshWindingNumberBoolean(Invariance.Transformed(SphereAt(0), s), Invariance.Transformed(SphereAt(1), s),
                MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32, CancelF = cancelF };
            var tdc = new TerrainDualContouring(Invariance.Transformed(HillTerrain(), s)) { GridResolution = 16, CancelF = cancelF };

            stop = false;
            Assert.True(sol.Compute(), name);
            Assert.True(op.Compute(), name);
            Assert.True(tdc.Compute(), name);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(sol.Result));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result));
            Assert.True(tdc.Result.TriangleCount > 0);

            stop = true;
            Assert.False(sol.Compute(), name);
            Assert.False(op.Compute(), name);
            Assert.False(tdc.Compute(), name);
            Assert.Null(sol.Result);
            Assert.Null(op.Result);
            Assert.Null(tdc.Result);
        }

        [Fact]
        public void TerrainOffset_ReusedAfterFailure_EqualsFreshInstance_NoStaleResult()
        {
            var off = new TerrainOffset(HillTerrain(), 1) { GridResolution = 16 };
            Assert.True(off.Compute());
            Assert.NotNull(off.Result);
            off.CellSize = double.PositiveInfinity;
            Assert.False(off.Compute());
            Assert.Null(off.Result);
            off.CellSize = 0;
            Assert.True(off.Compute());
            Assert.True(off.Result.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(off.Result));
            var fresh = new TerrainOffset(HillTerrain(), 1) { GridResolution = 16 };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Result, off.Result));
            // the check of TerrainOffset itself, before the contouring
            off.Distance = 0;
            Assert.False(off.Compute());
            Assert.Null(off.Result);
        }

        [Fact]
        public void CellSize_FiniteValues_KeepTheirMeaning()
        {
            // 0 and negative values mean "automatic", as before; a finite size is used as given
            Func<double, MeshWindingNumberSolidify> run = cell => {
                var sol = new MeshWindingNumberSolidify(Sphere()) { CellSize = cell, GridResolution = 32 };
                Assert.True(sol.Compute());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(sol.Result));
                return sol;
            };
            Assert.Equal("", Invariance.Difference(run(0).Result, run(-1).Result));
            var given = run(0.1);
            Assert.Equal(0.1, given.UsedCellSize * given.RefineFactor, 12);
        }

        [Theory]
        [MemberData(nameof(BadCellSizes))]
        public void CellSize_NonFinite_FailsInEveryFrame(double cell)
        {
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = Invariance.Transformed(SphereAt(0), s), b = Invariance.Transformed(SphereAt(1), s);
                Assert.False(new MeshWindingNumberSolidify(a) { CellSize = cell }.Compute());
                Assert.False(new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = cell }.Compute());
                Assert.False(new TerrainDualContouring(Invariance.Transformed(HillTerrain(), s)) { CellSize = cell }.Compute());
            }
        }

        // ---- TerrainSafetyLines: invalid tolerances, edge lengths and sources (S26) ----

        const TerrainSafetyLines.Modes Horizontal = TerrainSafetyLines.Modes.Horizontal;
        const TerrainSafetyLines.Modes AlongSurface = TerrainSafetyLines.Modes.AlongSurface;
        const TerrainSafetyLines.Modes Guaranteed = TerrainSafetyLines.Modes.Guaranteed;

        static List<Vector3d> Crest() => new List<Vector3d> { new Vector3d(4, 10, 0.4), new Vector3d(16, 10, 1.6) };

        static TerrainSafetyLines Safety(TerrainSafetyLines.Modes mode, List<Vector3d> source = null) =>
            new TerrainSafetyLines(HillTerrain(), new[] { source ?? Crest() }, 2) { Mode = mode };

        // Compute() in a thread of its own, as a wrong guard is a near hang (about 2^31 steps)
        static bool Compute(TerrainSafetyLines sl)
        {
            bool ok = false;
            Assert.True(Timed.Finishes(() => ok = sl.Compute(), Budget), "did not finish within the budget");
            return ok;
        }

        static void AssertFailedWithNoResult(TerrainSafetyLines sl)
        {
            Assert.False(Compute(sl));
            Assert.Empty(sl.Lines);
            Assert.Empty(sl.Excess);
            Assert.False(sl.Certified);
            Assert.Equal(0, sl.UncertifiedSegments);
            Assert.Equal(0, sl.MaxExcess);
            // (double.MaxValue here would pass as "far enough from the sources")
            Assert.Equal(0, sl.MinLineDistance);
            Assert.Equal(0, sl.UsedSteinerPoints);
            Assert.Null(sl.RefinedTerrain);
            Assert.Null(sl.RemeshedTerrain);
            Assert.Null(sl.Geodesic);
        }

        static void AssertLinesBits(List<List<Vector3d>> a, List<List<Vector3d>> b)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; ++i)
                Assert.Equal(a[i], b[i]);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        public void Safety_BadExcessTolerance_Fails(double tol)
        {
            var sl = Safety(Guaranteed);
            sl.ExcessTolerance = tol;
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [InlineData(Horizontal, 0.0)]
        [InlineData(Horizontal, -1.0)]
        [InlineData(Horizontal, double.NaN)]
        [InlineData(Horizontal, double.NegativeInfinity)]
        [InlineData(Guaranteed, 0.0)]
        [InlineData(Guaranteed, -1.0)]
        [InlineData(Guaranteed, double.NaN)]
        [InlineData(Guaranteed, double.NegativeInfinity)]
        [InlineData(Guaranteed, double.PositiveInfinity)]       // the line is drawn this much outside the distance
        public void Safety_BadTolerance_Fails(TerrainSafetyLines.Modes mode, double tol)
        {
            var sl = Safety(mode);
            sl.Tolerance = tol;
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Safety_BadEdgeLength_Fails(double edge)
        {
            var sl = Safety(AlongSurface);
            sl.EdgeLength = edge;
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Safety_BadSafetyMargin_Fails(double margin)
        {
            // +infinity put the line out of reach: success with no lines
            var sl = Safety(AlongSurface);
            sl.SafetyMargin = margin;
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_InfiniteHorizontalTolerance_MeansNoLimitOfTheChords_InEveryFrame(string name)
        {
            // the edge length is also limited by SafetyDistance, so a large tolerance is the same as +infinity
            Similarity s = Similarity.ByName(name);
            Func<double, TerrainSafetyLines> make = tol => new TerrainSafetyLines(Invariance.Transformed(HillTerrain(), s),
                new[] { Crest().Select(s.Apply).ToList() }, s.ApplyLength(2)) { Tolerance = tol, Mode = Horizontal };
            var large = make(s.ApplyLength(1e6));
            Assert.True(Compute(large));
            Assert.NotEmpty(large.Lines);
            var infinite = make(double.PositiveInfinity);
            Assert.True(Compute(infinite), name);
            AssertLinesBits(large.Lines, infinite.Lines);
            Assert.All(infinite.Lines.SelectMany(l => l), p => Assert.True(p.IsFinite));
            Assert.True(Compute(infinite));     // reuse
            AssertLinesBits(large.Lines, infinite.Lines);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_InfiniteExcessTolerance_MeansOnlyTheLowerBound_InEveryFrame(string name)
        {
            // certified by the distance from the sources alone: the lines are those of any other ExcessTolerance
            Similarity s = Similarity.ByName(name);
            Func<double, TerrainSafetyLines> make = tol => new TerrainSafetyLines(Invariance.Transformed(HillTerrain(), s),
                new[] { Crest().Select(s.Apply).ToList() }, s.ApplyLength(2)) { Tolerance = s.ApplyLength(0.01), ExcessTolerance = tol, Mode = Guaranteed, RequireCertified = true };
            var finite = make(s.ApplyLength(2));
            Assert.True(Compute(finite));
            var infinite = make(double.PositiveInfinity);
            Assert.True(Compute(infinite), name);
            Assert.True(infinite.Certified);
            Assert.Equal(0, infinite.UncertifiedSegments);
            Assert.NotEmpty(infinite.Lines);
            AssertLinesBits(finite.Lines, infinite.Lines);
            Assert.Equal(finite.MinLineDistance, infinite.MinLineDistance);
            Assert.True(infinite.MinLineDistance >= s.ApplyLength(2));
            Assert.True(MathUtil.IsFinite(infinite.MaxExcess));
            Assert.True(Compute(infinite));     // reuse
            Assert.True(infinite.Certified);
            AssertLinesBits(finite.Lines, infinite.Lines);
        }

        [Fact]
        public void Safety_FailureAfterSuccess_LeavesNoOutputOfTheEarlierRun()
        {
            var horizontal = Safety(Horizontal);
            Assert.True(Compute(horizontal));
            Assert.NotNull(horizontal.RefinedTerrain);
            horizontal.Tolerance = double.NaN;
            AssertFailedWithNoResult(horizontal);

            var surface = Safety(AlongSurface);
            Assert.True(Compute(surface));
            Assert.NotNull(surface.RemeshedTerrain);
            Assert.NotNull(surface.Geodesic);
            surface.EdgeLength = double.NaN;
            AssertFailedWithNoResult(surface);

            var guaranteed = Safety(Guaranteed);
            guaranteed.ExcessTolerance = 2;
            Assert.True(Compute(guaranteed));
            Assert.NotNull(guaranteed.RefinedTerrain);
            Assert.True(guaranteed.UsedSteinerPoints > 0);
            Assert.True(guaranteed.MinLineDistance > 0);
            guaranteed.ExcessTolerance = 0;
            AssertFailedWithNoResult(guaranteed);
            // and with the check in Compute itself, which comes before the mode
            guaranteed.ExcessTolerance = 2;
            Assert.True(Compute(guaranteed));
            guaranteed.SafetyDistance = double.NaN;
            AssertFailedWithNoResult(guaranteed);
        }

        // ---- TerrainSafetyLines: finite values that would need more steps or memory than any sensible use (S26) ----

        static TerrainSafetyLines SafetyIn(Similarity s, TerrainSafetyLines.Modes mode, List<Vector3d> source)
        {
            var sl = new TerrainSafetyLines(Invariance.Transformed(HillTerrain(), s), new[] { source.Select(s.Apply).ToList() }, s.ApplyLength(2)) { Mode = mode };
            sl.Tolerance = s.ApplyLength(0.01);
            sl.ExcessTolerance = s.ApplyLength(2);
            return sl;
        }

        // far beyond the terrain of 20 x 20: few triangles near it, but very many steps along it
        static List<Vector3d> VeryLongSource(double length) => new List<Vector3d> { new Vector3d(4, 10, 0.4), new Vector3d(4 + length, 10, 0.4) };

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_TinyExcessTolerance_Refused_InEveryFrame(string name)
        {
            // (was an OverflowException on .NET, where the cast to int saturates, and one sample per segment on .NET Framework)
            Similarity s = Similarity.ByName(name);
            var sl = SafetyIn(s, Guaranteed, Crest());
            sl.ExcessTolerance = s.ApplyLength(1e-14);
            AssertFailedWithNoResult(sl);
            // the instance is fine afterwards
            sl.ExcessTolerance = s.ApplyLength(2);
            Assert.True(Compute(sl), name);
            Assert.True(sl.Certified, name);
            Assert.NotEmpty(sl.Lines);
        }

        [Fact]
        public void Safety_TinyExcessTolerance_AfterASuccess_LeavesNoOutputOfTheRun()
        {
            // it is known only when the lines are: the refusal must also clear what this run has made
            var sl = Safety(Guaranteed);
            sl.ExcessTolerance = 2;
            Assert.True(Compute(sl));
            sl.ExcessTolerance = 1e-14;
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_TinyEdgeLength_VeryLongSource_Refused_InEveryFrame(string name)
        {
            // 2e8 source points before anything else: the refinement of the small terrain alone would be fine
            Similarity s = Similarity.ByName(name);
            var sl = SafetyIn(s, AlongSurface, VeryLongSource(1e6));
            sl.EdgeLength = s.ApplyLength(0.01);
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_VeryLongSource_Refused_Guaranteed_InEveryFrame(string name)
        {
            // 1e13 steps along the source for the bound (about 2^31 on .NET, one on .NET Framework)
            AssertFailedWithNoResult(SafetyIn(Similarity.ByName(name), Guaranteed, VeryLongSource(1e12)));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_TinyEdgeLength_OnAPoint_Refused_InEveryFrame(string name)
        {
            // no long source: the refinement and the remeshing of the band are what would not end
            Similarity s = Similarity.ByName(name);
            var sl = new TerrainSafetyLines(Invariance.Transformed(HillTerrain(), s), new int[0], s.ApplyLength(2)) { Mode = AlongSurface, EdgeLength = s.ApplyLength(1e-9) };
            sl.SourcePoints.Add(s.Apply(new Vector3d(10, 10, 1)));
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_TinyTolerance_Refused_InEveryFrame(string name)
        {
            Similarity s = Similarity.ByName(name);
            var horizontal = SafetyIn(s, Horizontal, Crest());
            horizontal.Tolerance = s.ApplyLength(1e-14);
            AssertFailedWithNoResult(horizontal);
            var guaranteed = SafetyIn(s, Guaranteed, Crest());
            guaranteed.Tolerance = s.ApplyLength(1e-14);
            AssertFailedWithNoResult(guaranteed);
        }

        [Fact]
        public void Safety_TinySafetyDistance_Refused()
        {
            // the edge lengths follow SafetyDistance
            foreach (TerrainSafetyLines.Modes mode in new[] { Horizontal, AlongSurface }) {
                var sl = new TerrainSafetyLines(HillTerrain(), new[] { Crest() }, 1e-9) { Mode = mode };
                AssertFailedWithNoResult(sl);
            }
            // not in Guaranteed, where the line is drawn Tolerance further out: the edges follow that
            var guaranteed = new TerrainSafetyLines(HillTerrain(), new[] { Crest() }, 1e-9) { Mode = Guaranteed, ExcessTolerance = 2 };
            Assert.True(Compute(guaranteed));
            Assert.NotEmpty(guaranteed.Lines);
        }

        // ---- a terrain with a vertex that is not finite: the splits near it would never end ----

        public static IEnumerable<object[]> BadTerrainVertices()
        {
            const int middle = 220, corner = 0;     // of the 21 x 21 vertices of HillTerrain
            foreach (TerrainSafetyLines.Modes mode in new[] { Horizontal, AlongSurface, Guaranteed }) {
                yield return new object[] { mode, middle, double.NaN };
                yield return new object[] { mode, corner, double.NaN };
                yield return new object[] { mode, middle, double.PositiveInfinity };
                yield return new object[] { mode, corner, double.NegativeInfinity };
            }
        }

        [Theory]
        [MemberData(nameof(BadTerrainVertices))]
        public void Safety_NonFiniteTerrainVertex_Fails_InEveryFrame(TerrainSafetyLines.Modes mode, int vertex, double bad)
        {
            foreach (int axis in new[] { 0, 1, 2 }) {
                DMesh3 terrain = HillTerrain();
                Vector3d p = terrain.GetVertex(vertex);
                p[axis] = bad;
                terrain.SetVertex(vertex, p);
                // (untransformed first: the rotation matrix, even the identity, spreads a NaN to all three coordinates)
                AssertFailedWithNoResult(new TerrainSafetyLines(terrain, new[] { Crest() }, 2) { Mode = mode });
                foreach (Similarity s in Similarity.All()) {
                    var sl = new TerrainSafetyLines(Invariance.Transformed(terrain, s), new[] { Crest().Select(s.Apply).ToList() }, s.ApplyLength(2)) { Mode = mode };
                    AssertFailedWithNoResult(sl);
                }
            }
        }

        [Theory]
        [InlineData(Horizontal)]
        [InlineData(AlongSurface)]
        [InlineData(Guaranteed)]
        public void Safety_NonFiniteVertexWithoutTriangle_Fails_ThenRecovers(TerrainSafetyLines.Modes mode)
        {
            // every live vertex counts; removing it makes the terrain good again
            DMesh3 terrain = HillTerrain();
            int v = terrain.AppendVertex(new Vector3d(double.NaN, 0, 0));
            var sl = new TerrainSafetyLines(terrain, new[] { Crest() }, 2) { Mode = mode };
            AssertFailedWithNoResult(sl);
            Assert.Equal(MeshResult.Ok, terrain.RemoveVertex(v));
            Assert.True(Compute(sl));
            Assert.NotEmpty(sl.Lines);
        }

        // ---- the expected size of the refinement does not count sources that lie together twice ----

        // a plate of few triangles, large: nothing but the band near the sources is split
        static DMesh3 CoarsePlate(double side, int n = 2)
        {
            var mesh = Plate(n);
            MeshTransforms.Scale(mesh, side / n);
            return mesh;
        }

        // close together, beside a large plate: one disc, however many they are. Beside it, so that the whole run is short
        static List<Vector3d> ClusterOfPoints(int n)
        {
            var points = new List<Vector3d>(n);
            var random = new Random(31);
            for (int i = 0; i < n; ++i)
                points.Add(new Vector3d(-50 + 0.1 * random.NextDouble(), -50 + 0.1 * random.NextDouble(), 0));
            return points;
        }

        // the sources of a road or a line of survey points: rows of a serpentine, 1 m apart along it
        static List<Vector3d> Serpentine(int n, double distance)
        {
            var points = new List<Vector3d>(n);
            double width = 5000;
            for (int i = 0; i < n; ++i) {
                int row = (int)(i / width);
                double u = i - row * width;
                points.Add(new Vector3d(5000 + ((row % 2 == 0) ? u : width - u), 5000 + row * 3 * distance, 0));
            }
            return points;
        }

        [Fact]
        public void Safety_ExpectedTriangles_DoNotCountWhatLiesTogether()
        {
            // the example of the review of #239: D = 100, default tolerance, a terrain of 20 km, 13000 sources. The
            // whole run takes a minute and 2.2 million triangles: only the estimate here
            const double D = 100;
            double edge = Math.Sqrt(8 * D * 0.01);
            DMesh3 terrain = CoarsePlate(20100);
            List<Vector3d> points = Serpentine(13000, D);
            var asPoints = new TerrainSafetyLines(terrain, new int[0], D);
            asPoints.SourcePoints.AddRange(points);
            var asPolyline = new TerrainSafetyLines(terrain, new[] { points }, D);
            var shortLines = new List<List<Vector3d>>();
            for (int i = 0; i + 1 < points.Count; ++i)
                shortLines.Add(new List<Vector3d> { points[i], points[i + 1] });
            var asShortLines = new TerrainSafetyLines(terrain, shortLines, D);
            double byPoints = asPoints.expected_refined_triangles(D, edge), byPolyline = asPolyline.expected_refined_triangles(D, edge),
                byShortLines = asShortLines.expected_refined_triangles(D, edge);
            foreach (double expected in new[] { byPoints, byPolyline, byShortLines }) {
                Assert.True(expected <= TerrainSafetyLines.MaxRefinedTriangles, expected.ToString());
                // the order of the 2.2 million triangles of the whole run
                Assert.InRange(expected, 2e5, 2e7);
            }
            // the same polyline in pieces: the same strip, the same discs
            Assert.Equal(byPolyline, byShortLines, 6);
            // and it still counts: sources that do not lie together have a disc each
            var apart = new TerrainSafetyLines(terrain, new int[0], D);
            for (int i = 0; i < 40; ++i)
                apart.SourcePoints.Add(new Vector3d(1000 + 400 * i, 1000 + 400 * i, 0));
            var one = new TerrainSafetyLines(terrain, new int[0], D);
            one.SourcePoints.Add(new Vector3d(1000.5, 1000.5, 0));
            Assert.Equal(40 * one.expected_refined_triangles(D, edge), apart.expected_refined_triangles(D, edge), 3);
            // the strip along a segment, with the discs of its two ends
            var segment = new TerrainSafetyLines(terrain, new[] { new List<Vector3d> { new Vector3d(1000.5, 1000.5, 0), new Vector3d(2000.5, 1000.5, 0) } }, D);
            double r = D / edge;
            Assert.Equal(2 * (2 * r * (1000 / edge) + Math.PI * r * r * 2), segment.expected_refined_triangles(D, edge), 6);
            // also the single point of a polyline
            var onePointLine = new TerrainSafetyLines(terrain, new[] { new List<Vector3d> { new Vector3d(1000.5, 1000.5, 0) } }, D);
            Assert.Equal(one.expected_refined_triangles(D, edge), onePointLine.expected_refined_triangles(D, edge), 6);
            // and the terrain limits it
            var small = new TerrainSafetyLines(CoarsePlate(10), new[] { points }, D);
            Assert.Equal(2 * 100 / (edge * edge), small.expected_refined_triangles(D, edge), 6);
        }

        [Fact]
        public void Safety_ValuesOfSensibleUse_AreNotRefused()
        {
            // finer than anything in practice, by a wide margin below the limits
            var fine = Safety(Guaranteed);
            fine.ExcessTolerance = 2e-3;
            fine.Tolerance = 2e-3;
            Assert.True(Compute(fine));
            Assert.NotEmpty(fine.Lines);
            var horizontal = Safety(Horizontal);
            horizontal.Tolerance = 1e-4;
            Assert.True(Compute(horizontal));
            Assert.NotEmpty(horizontal.Lines);
            var surface = Safety(AlongSurface);
            surface.EdgeLength = 0.2;
            Assert.True(Compute(surface));
            Assert.NotEmpty(surface.Lines);

            // many points, many short polylines, a large terrain: at most one disc each would be 1.6e8 triangles
            List<Vector3d> cluster = ClusterOfPoints(20000);
            var manyPoints = new TerrainSafetyLines(CoarsePlate(400), new int[0], 1) { Tolerance = 1e-4 };
            manyPoints.SourcePoints.AddRange(cluster);
            Assert.True(Compute(manyPoints));
            var lines = new List<List<Vector3d>>();
            for (int i = 0; i + 1 < cluster.Count; ++i)
                lines.Add(new List<Vector3d> { cluster[i], cluster[i + 1] });
            var manyLines = new TerrainSafetyLines(CoarsePlate(400), lines, 1) { Tolerance = 1e-4 };
            Assert.True(Compute(manyLines));
        }

        [Fact]
        public void Safety_UpperBound_PinnedBits()
        {
            // the samples along a segment, without storing them: the same bound as with the array they were in before
            var sl = Safety(Guaranteed);
            sl.ExcessTolerance = 0.05;
            sl.SteinerPoints = 3;
            sl.MaxSteinerPoints = 3;
            Assert.True(Compute(sl));
            Assert.Equal(4593529488071912896, BitConverter.DoubleToInt64Bits(sl.MaxExcess));
            Assert.Equal(4579305932377577984, BitConverter.DoubleToInt64Bits(sl.Excess[0][sl.Excess[0].Count - 1]));
            Assert.Equal(70, sl.UncertifiedSegments);
        }

        [Fact]
        public void Safety_ExceptionInCertify_LeavesNoCertificate()
        {
            // (the points per edge are not limited: with these the node count just passes 2^31, wraps around to a
            // negative size, and the allocation throws)
            var sl = Safety(Guaranteed);
            sl.ExcessTolerance = 2;
            Assert.True(Compute(sl));
            Assert.True(sl.Certified);
            sl.SteinerPoints = (int)Math.Ceiling(Math.Pow(2, 31) / sl.RefinedTerrain.MaxEdgeID);
            try {
                sl.Compute();
            } catch (OverflowException) {
            }
            Assert.False(sl.Certified);
        }

        [Fact]
        public void Safety_SmallExcessTolerance_NotCertified_WithoutAnException()
        {
            var sl = Safety(Guaranteed);
            sl.ExcessTolerance = 1e-3;
            Assert.True(Compute(sl));
            Assert.False(sl.Certified);
            Assert.True(sl.UncertifiedSegments > 0);
            sl.RequireCertified = true;
            Assert.False(Compute(sl));
            Assert.False(sl.Certified);
        }

        [Fact]
        public void Safety_SwitchingTheMode_LeavesNoOutputOfTheOtherMode()
        {
            var sl = Safety(AlongSurface);
            Assert.True(Compute(sl));
            Assert.NotNull(sl.RemeshedTerrain);
            sl.Mode = Horizontal;
            Assert.True(Compute(sl));
            Assert.NotNull(sl.RefinedTerrain);
            Assert.Null(sl.RemeshedTerrain);
            Assert.Null(sl.Geodesic);
            sl.Mode = AlongSurface;
            Assert.True(Compute(sl));
            Assert.Null(sl.RefinedTerrain);
        }

        public static IEnumerable<object[]> BadSources()
        {
            foreach (TerrainSafetyLines.Modes mode in new[] { Horizontal, AlongSurface, Guaranteed })
                foreach (double bad in new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN })
                    foreach (int axis in new[] { 0, 1, 2 })
                        yield return new object[] { mode, bad, axis };
        }

        [Theory]
        [MemberData(nameof(BadSources))]
        public void Safety_NonFiniteSource_Fails(TerrainSafetyLines.Modes mode, double bad, int axis)
        {
            Vector3d p = new Vector3d(10, 10, 1);
            p[axis] = bad;
            var line = new List<Vector3d> { new Vector3d(4, 10, 0.4), p };
            AssertFailedWithNoResult(Safety(mode, line));
            // a polyline of one point has no segment length that would give it away
            AssertFailedWithNoResult(Safety(mode, new List<Vector3d> { p }));
            // as a single point too
            var sl = new TerrainSafetyLines(HillTerrain(), new int[0], 2) { Mode = mode };
            sl.SourcePoints.Add(p);
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [InlineData(Horizontal)]
        [InlineData(AlongSurface)]
        [InlineData(Guaranteed)]
        public void Safety_SourceSegmentLengthOverflows_Fails(TerrainSafetyLines.Modes mode)
        {
            // finite coordinates, but the squared length is +infinity: the steps would be as for an infinite source
            AssertFailedWithNoResult(Safety(mode, new List<Vector3d> { new Vector3d(4, 10, 0), new Vector3d(1e200, 10, 0) }));
        }

        [Fact]
        public void Safety_ParametersOfOtherModes_AreIgnored_AsBefore()
        {
            var plain = Safety(Horizontal);
            Assert.True(Compute(plain));
            Assert.NotEmpty(plain.Lines);
            var other = Safety(Horizontal);
            other.ExcessTolerance = 0;
            other.EdgeLength = double.NaN;
            Assert.True(Compute(other));
            AssertLinesBits(plain.Lines, other.Lines);

            var surface = Safety(AlongSurface);
            Assert.True(Compute(surface));
            Assert.NotEmpty(surface.Lines);
            // (EdgeLength 0 and negative mean the default)
            var surfaceOther = Safety(AlongSurface);
            surfaceOther.Tolerance = 0;
            surfaceOther.ExcessTolerance = -1;
            surfaceOther.EdgeLength = 0;
            Assert.True(Compute(surfaceOther));
            AssertLinesBits(surface.Lines, surfaceOther.Lines);
        }

        [Fact]
        public void Safety_ReusedAfterFailure_EqualsFreshInstance_NoStaleCertificate()
        {
            Func<TerrainSafetyLines> make = () => {
                var s = Safety(Guaranteed);
                s.ExcessTolerance = 2;
                return s;
            };
            var sl = make();
            Assert.True(Compute(sl));
            Assert.True(sl.Certified);
            Assert.NotEmpty(sl.Lines);
            Assert.NotEmpty(sl.Excess);

            sl.ExcessTolerance = 0;
            AssertFailedWithNoResult(sl);
            sl.ExcessTolerance = 2;
            Assert.True(Compute(sl));
            Assert.True(sl.Certified);

            // the source check comes before the mode: it must clear the certificate too
            sl.SourcePoints.Add(new Vector3d(double.PositiveInfinity, 0, 0));
            AssertFailedWithNoResult(sl);
            sl.SourcePoints.Clear();
            Assert.True(Compute(sl));
            Assert.True(sl.Certified);
            var fresh = make();
            Assert.True(Compute(fresh));
            AssertLinesBits(fresh.Lines, sl.Lines);
            Assert.Equal(fresh.MaxExcess, sl.MaxExcess);
            Assert.Equal(fresh.MinLineDistance, sl.MinLineDistance);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Safety_ValidAndInvalidInput_InEveryFrame(string name)
        {
            Similarity s = Similarity.ByName(name);
            Func<TerrainSafetyLines> make = () => new TerrainSafetyLines(Invariance.Transformed(HillTerrain(), s),
                new[] { Crest().Select(s.Apply).ToList() }, s.ApplyLength(2)) { Tolerance = s.ApplyLength(0.01), ExcessTolerance = s.ApplyLength(2), Mode = Guaranteed };
            var bad = make();
            bad.ExcessTolerance = 0;
            AssertFailedWithNoResult(bad);
            var good = make();
            Assert.True(Compute(good));
            Assert.True(good.Certified, name);
            Assert.NotEmpty(good.Lines);
            var reference = new TerrainSafetyLines(HillTerrain(), new[] { Crest() }, 2) { Tolerance = 0.01, ExcessTolerance = 2, Mode = Guaranteed };
            Assert.True(Compute(reference));
            if (s.IsExact)
                AssertLinesBits(reference.Lines, good.Lines.Select(l => l.Select(s.Inverse).ToList()).ToList());
        }

        // ---- AxisAlignedBox.IndicesInclusive: a range ending at int.MaxValue (S33) ----

        static List<T> Enumerate<T>(IEnumerable<T> items)
        {
            // Take: a loop that wraps around must not fill the memory while the budget runs
            List<T> result = null;
            Assert.True(Timed.Finishes(() => result = items.Take(100000).ToList(), Budget), "did not finish within the budget");
            return result;
        }

        static List<Vector3i> Expected3(Vector3i min, Vector3i max)
        {
            var result = new List<Vector3i>();
            for (long z = min.z; z <= max.z; ++z)
                for (long y = min.y; y <= max.y; ++y)
                    for (long x = min.x; x <= max.x; ++x)
                        result.Add(new Vector3i((int)x, (int)y, (int)z));
            return result;
        }

        static List<Vector2i> Expected2(Vector2i min, Vector2i max)
        {
            var result = new List<Vector2i>();
            for (long y = min.y; y <= max.y; ++y)
                for (long x = min.x; x <= max.x; ++x)
                    result.Add(new Vector2i((int)x, (int)y));
            return result;
        }

        const int Top = int.MaxValue, Low = int.MinValue;

        public static IEnumerable<object[]> Ranges3()
        {
            yield return new object[] { Top - 1, Top - 1, Top - 1, Top, Top, Top, 8 };
            yield return new object[] { Top, Top, Top, Top, Top, Top, 1 };
            yield return new object[] { 0, 0, Top - 2, 1, 1, Top, 12 };       // only z
            yield return new object[] { 0, Top - 1, 0, 1, Top, 1, 8 };        // only y
            yield return new object[] { Top - 1, 0, 0, Top, 1, 1, 8 };        // only x
            yield return new object[] { Low, Low, Low, Low + 1, Low + 1, Low + 1, 8 };
            yield return new object[] { -1, 2, 3, 1, 3, 3, 6 };               // normal
            yield return new object[] { 5, 5, 5, 5, 5, 5, 1 };                // one cell
            yield return new object[] { 5, 0, 0, 4, 1, 1, 0 };                // empty, in x
            yield return new object[] { 0, 0, 5, 1, 1, 4, 0 };                // empty, in z
            yield return new object[] { Top, Top, Top, Low, Low, Low, 0 };    // AxisAlignedBox3i.Empty
        }

        [Theory]
        [MemberData(nameof(Ranges3))]
        public void IndicesInclusive3_ExactlyTheCellsInOrder(int x0, int y0, int z0, int x1, int y1, int z1, int count)
        {
            // (this constructor keeps Min > Max; the one with two vectors would sort them)
            var box = new AxisAlignedBox3i(x0, y0, z0, x1, y1, z1);
            List<Vector3i> cells = Enumerate(box.IndicesInclusive());
            Assert.Equal(count, cells.Count);
            Assert.Equal(Expected3(box.Min, box.Max), cells);
        }

        public static IEnumerable<object[]> Ranges2()
        {
            yield return new object[] { Top - 1, Top - 1, Top, Top, 4 };
            yield return new object[] { Top, Top, Top, Top, 1 };
            yield return new object[] { 0, Top - 2, 1, Top, 6 };              // only y
            yield return new object[] { Top - 1, 0, Top, 2, 6 };              // only x
            yield return new object[] { Low, Low, Low + 1, Low + 1, 4 };
            yield return new object[] { -1, 2, 1, 3, 6 };                     // normal
            yield return new object[] { 5, 5, 5, 5, 1 };                      // one cell
            yield return new object[] { 5, 0, 4, 1, 0 };                      // empty, in x
            yield return new object[] { 0, 5, 1, 4, 0 };                      // empty, in y
            yield return new object[] { Top, Top, Low, Low, 0 };              // AxisAlignedBox2i.Empty
        }

        [Theory]
        [MemberData(nameof(Ranges2))]
        public void IndicesInclusive2_ExactlyTheCellsInOrder(int x0, int y0, int x1, int y1, int count)
        {
            var box = new AxisAlignedBox2i(x0, y0, x1, y1);
            List<Vector2i> cells = Enumerate(box.IndicesInclusive());
            Assert.Equal(count, cells.Count);
            Assert.Equal(Expected2(box.Min, box.Max), cells);
        }
    }
}
