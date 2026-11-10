using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Speedups of the Reducer that must not change its result (2026-11-08, reducer profile in
    /// docs/roadmap/alpha-wrap-and-repair.md): the cell size of the self-intersection grid, the per-vertex bowtie
    /// states, and the self-intersection check without allocations per collapse. Each is compared with the slower
    /// reference, bit for bit.
    /// </summary>
    public class ReducerSpeedTests
    {
        // a wavy plate with triangles removed at random: many bowtie vertices (fans that meet only at the vertex). They
        // lie on the boundary, so the bowtie rule only matters with PreserveBoundaryShape = false
        internal static DMesh3 HolyPlate(int n, int seed, double removed)
        {
            var m = new DMesh3();
            var rnd = new Random(seed);
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i, j, 0.3 * Math.Sin(0.7 * i) * Math.Cos(0.5 * j) + 0.05 * rnd.NextDouble()));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    if (rnd.NextDouble() >= removed) m.AppendTriangle(a, b, d);
                    if (rnd.NextDouble() >= removed) m.AppendTriangle(a, d, c);
                }
            foreach (int v in m.VertexIndices().ToArray())
                if (m.GetVtxEdgeCount(v) == 0) m.RemoveVertex(v);
            return m;
        }

        static int Bowties(DMesh3 m) => m.VertexIndices().Count(v => m.IsBowtieVertex(v));

        public static IEnumerable<object[]> Cases()
        {
            foreach (object[] c in DefectFreeReduceTests.UnionQueryCases())
                yield return c;
            yield return new object[] { "holy plate 1", 0 };
            yield return new object[] { "holy plate 2", 0 };
            yield return new object[] { "holy plate 3", 0 };
        }

        static DMesh3 CaseMesh(string name)
        {
            if (name.StartsWith("holy plate"))
                return HolyPlate(30, int.Parse(name.Substring(11)), 0.2);
            return DefectFreeReduceTests.UnionQueryMesh(name);
        }

        // the reduction with the reference settings (grid cells of 1.5 mean edge lengths, as before; IsBowtieVertex on
        // every collapse)
        static DMesh3 Reference(DMesh3 input, Action<Reducer> reduce, bool preserveBoundary = true)
        {
            DMesh3 m = new DMesh3(input);
            var r = new Reducer(m) { PreventSelfIntersections = true, GridCellEdgeLengths = 1.5, CacheBowtieStates = false,
                PreserveBoundaryShape = preserveBoundary };
            reduce(r);
            return m;
        }

        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Cases))]
        public void SameReductionAsTheReference(string name, int target)
        {
            DMesh3 input = CaseMesh(name);
            bool plate = name.StartsWith("holy plate");
            if (target == 0) target = input.TriangleCount / 4;
            Action<Reducer> reduce = (r) => r.ReduceToTriangleCount(target);
            DMesh3 reference = Reference(input, reduce, plate == false);
            DMesh3 fast = new DMesh3(input);
            var reducer = new Reducer(fast) { PreventSelfIntersections = true, PreserveBoundaryShape = plate == false };
            Assert.True(reducer.CacheBowtieStates);        // the defaults
            Assert.Equal(3.0, reducer.GridCellEdgeLengths);
            reduce(reducer);
            Assert.Equal("", Invariance.Difference(reference, fast));
            Assert.True(fast.CheckValidity(true, FailMode.ReturnOnly));
            // (on the coincident sheets the check refuses every collapse)
            if (name != "coincident sheets")
                Assert.True(fast.TriangleCount < input.TriangleCount);
            if (plate) {
                Assert.True(Bowties(input) > 20, $"{Bowties(input)} bowtie vertices");
                Assert.True(Bowties(fast) > 0);
            }
        }

        // reused on one instance, and repeated reductions on one mesh: the states of the first reduction are not kept
        [Fact]
        public void ReusedReducer_SameAsTheReference()
        {
            DMesh3 input = HolyPlate(30, 7, 0.2);
            DMesh3 fast = new DMesh3(input);
            var r = new Reducer(fast) { PreventSelfIntersections = true };
            r.ReduceToTriangleCount(input.TriangleCount / 2);
            r.ReduceToTriangleCount(input.TriangleCount / 4);
            r.FastCollapsePass(1.2, 2);
            DMesh3 reference = Reference(input, (x) => {
                x.ReduceToTriangleCount(input.TriangleCount / 2);
                x.ReduceToTriangleCount(input.TriangleCount / 4);
                x.FastCollapsePass(1.2, 2);
            });
            Assert.Equal("", Invariance.Difference(reference, fast));
            Assert.True(fast.CheckValidity(true, FailMode.ReturnOnly));
        }

        // the mesh changed between two reductions (triangles removed, which makes new bowtie vertices): the second
        // reduction must not use states of the first. (Within one reduction no collapse changed a state in 960
        // searched plate reductions: DMesh3.CollapseEdge's neighbourhood test refuses the collapses that would; the
        // resets after a collapse are kept for safety and cannot be tested here)
        [Fact]
        public void MeshChangedBetweenReductions_SameAsTheReference()
        {
            DMesh3 input = HolyPlate(30, 11, 0.15);
            Func<DMesh3, Reducer, DMesh3> run = (m, r) => {
                r.ReduceToTriangleCount(m.TriangleCount * 2 / 3);
                var rnd = new Random(5);
                foreach (int t in m.TriangleIndices().ToArray())
                    if (rnd.NextDouble() < 0.1) m.RemoveTriangle(t, true, false);
                r.ReduceToTriangleCount(m.TriangleCount / 2);
                return m;
            };
            DMesh3 a = new DMesh3(input), b = new DMesh3(input);
            run(a, new Reducer(a) { PreventSelfIntersections = true, PreserveBoundaryShape = false });
            run(b, new Reducer(b) { PreventSelfIntersections = true, PreserveBoundaryShape = false, GridCellEdgeLengths = 1.5, CacheBowtieStates = false });
            Assert.Equal("", Invariance.Difference(b, a));
        }

        // a subclass that removes a triangle next to every k-th collapse (in OnEdgeCollapse, while the states are
        // live): it makes new bowtie vertices that the states must not hide (the mesh's ShapeTimestamp)
        class TriangleRemovingReducer : Reducer
        {
            readonly int every;
            int collapses;
            public TriangleRemovingReducer(DMesh3 m, int every) : base(m) { this.every = every; }
            protected override void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo collapseInfo)
            {
                if (++collapses % every != 0)
                    return;
                // the first triangle of the kept vertex's first neighbour that does not contain the kept vertex
                foreach (int v in mesh.VtxVerticesItr(va)) {
                    foreach (int t in mesh.VtxTrianglesItr(v)) {
                        Index3i tv = mesh.GetTriangle(t);
                        if (tv.a == va || tv.b == va || tv.c == va) continue;
                        mesh.RemoveTriangle(t, true, false);
                        return;
                    }
                }
            }
        }

        [Theory]
        [InlineData(3, 5)]
        [InlineData(5, 3)]
        [InlineData(9, 2)]
        public void SubclassChangesTheMeshDuringTheReduction_SameAsTheReference(int seed, int every)
        {
            DMesh3 input = HolyPlate(24, seed, 0.1);
            DMesh3 a = new DMesh3(input), b = new DMesh3(input);
            new TriangleRemovingReducer(a, every) { PreventSelfIntersections = true, PreserveBoundaryShape = false }.ReduceToTriangleCount(input.TriangleCount / 3);
            new TriangleRemovingReducer(b, every) { PreventSelfIntersections = true, PreserveBoundaryShape = false, CacheBowtieStates = false }
                .ReduceToTriangleCount(input.TriangleCount / 3);
            Assert.Equal("", Invariance.Difference(b, a));
            Assert.True(a.TriangleCount < input.TriangleCount / 2);
        }

        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameReductionAsTheReference_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (DMesh3 mesh in new[] { DefectFreeReduceTests.ThinSlab(), HolyPlate(20, 3, 0.2) }) {
                DMesh3 input = Invariance.Transformed(mesh, sim);
                int target = mesh.TriangleCount < 2000 ? mesh.TriangleCount / 4 : 120;
                DMesh3 reference = Reference(input, (r) => r.ReduceToTriangleCount(target));
                DMesh3 fast = new DMesh3(input);
                new Reducer(fast) { PreventSelfIntersections = true }.ReduceToTriangleCount(target);
                Assert.Equal("", Invariance.Difference(reference, fast));
            }
        }
    }
}
