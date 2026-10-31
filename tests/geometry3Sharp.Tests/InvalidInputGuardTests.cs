using System;
using System.Collections.Generic;
using System.Linq;
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

        [Fact]
        public void IsoCurves_NaNElsewhere_KeepsTheCurvesOfTheFiniteTriangles()
        {
            // the field is NaN far above the level: the curve is the one of the finite field, bit for bit
            DMesh3 mesh = Sphere(16);
            var finite = new MeshIsoCurves(mesh, x => x.z - 0.1);
            finite.Compute();
            Assert.True(finite.Graph.EdgeCount > 0);
            var partial = new MeshIsoCurves(mesh, x => (x.z > 0.6) ? double.NaN : x.z - 0.1);
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
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Safety_BadExcessTolerance_Fails(double tol)
        {
            var sl = Safety(Guaranteed);
            sl.ExcessTolerance = tol;
            AssertFailedWithNoResult(sl);
        }

        [Theory]
        [InlineData(Horizontal, 0.0)]
        [InlineData(Horizontal, double.NaN)]
        [InlineData(Horizontal, double.PositiveInfinity)]
        [InlineData(Guaranteed, 0.0)]
        [InlineData(Guaranteed, double.NaN)]
        [InlineData(Guaranteed, double.PositiveInfinity)]
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

        public static IEnumerable<object[]> BadSources()
        {
            foreach (TerrainSafetyLines.Modes mode in new[] { Horizontal, AlongSurface, Guaranteed })
                foreach (double bad in new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN })
                    foreach (int axis in new[] { 0, 2 })
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
