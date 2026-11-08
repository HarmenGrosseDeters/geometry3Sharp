using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The triangle quality guard of the alpha wrap simplification (MeshRobustRepair.SimplifyWithin,
    /// WrapSimplifyMinQuality, Reducer.CollapseQualityAgainstWorst): no caps from the simplification, slivers of the
    /// wrap still removed, the certificate kept.
    /// </summary>
    public class WrapSimplifyQualityTests
    {
        static double MaxAngle(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
            return Math.Max(A, Math.Max(B, 180 - A - B));
        }

        static double Quality(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            return MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
        }

        static double MinQuality(DMesh3 m) => m.TriangleIndices().Min(t => Quality(m, t));

        // alpha wraps with flat faces and a curved part: the reduction of these made caps up to about 175 degrees
        public static IEnumerable<object[]> Shapes()
        {
            yield return new object[] { "box" };
            yield return new object[] { "cylinder" };
        }

        internal static DMesh3 Shape(string name)
        {
            if (name == "box")
                return new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), NoSharedVertices = false }.Generate().MakeDMesh();
            return new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 24, NoSharedVertices = false }.Generate().MakeDMesh();
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void SimplifyWithin_AlphaWrap_NoCapsCertifiedClean(string shape)
        {
            var w = new MeshAlphaWrap(Shape(shape)) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute());
            DMesh3 wrap = w.Result;
            double tol = 0.5 * w.UsedOffset;
            DMesh3 simp = MeshRobustRepair.SimplifyWithin(wrap, tol, tol, true, () => false);
            Assert.NotNull(simp);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(simp, true));
            Assert.True(simp.TriangleCount < wrap.TriangleCount / 2, $"{simp.TriangleCount} of {wrap.TriangleCount}");
            // no triangle below the guard unless the wrap had a worse one
            double floor = Math.Min(MeshRobustRepair.WrapSimplifyMinQuality, MinQuality(wrap));
            Assert.True(MinQuality(simp) >= floor, $"{MinQuality(simp)} < {floor}");
            double maxAngle = simp.TriangleIndices().Max(t => MaxAngle(simp, t));
            Assert.True(maxAngle < 155, $"max angle {maxAngle}");
            var hd = new MeshHausdorffDistance(simp, wrap) { Symmetric = false, Tolerance = 0.01 * tol };
            Assert.True(hd.Compute() && hd.UpperBound <= tol, $"hausdorff {hd.UpperBound}, tolerance {tol}");
            // reuse: the same again
            Assert.Equal("", Invariance.Difference(simp, MeshRobustRepair.SimplifyWithin(wrap, tol, tol, true, () => false)));
        }

        // a flat grid with a sliver: one vertex moved next to a neighbour (a short edge, two needles) or next to the
        // opposite edge of one of its triangles (a cap);
        // a flat grid has no quadric error, so the guards alone decide
        static DMesh3 GridWithSliver(double gap, bool cap)
        {
            DMesh3 m = new TrivialRectGenerator() { Width = 1, Height = 1, IndicesMap = new Index2i(1, 3) }.Generate().MakeDMesh();
            m = new DMesh3(m);
            var r = new Remesher(m);
            r.SetTargetEdgeLength(0.1);
            for (int k = 0; k < 6; ++k) r.BasicRemeshPass();
            // the interior vertex nearest the centre, moved next to its nearest interior neighbour
            int v = m.VertexIndices().Where(x => !m.IsBoundaryVertex(x)).OrderBy(x => m.GetVertex(x).Distance(new Vector3d(0.5, 0, 0.5))).First();
            if (cap) {
                // v moved next to the midpoint of the opposite edge of one of its triangles: a cap of nearly 180 degrees
                Index3i t = m.GetTriangle(m.VtxTrianglesItr(v).First());
                int u = t.a == v ? t.b : t.a, w = t.c == v ? t.b : t.c;
                Vector3d mid = 0.5 * (m.GetVertex(u) + m.GetVertex(w)), pv0 = m.GetVertex(v);
                m.SetVertex(v, mid + gap * (pv0 - mid).Normalized);
            } else {
                int u = m.VtxVerticesItr(v).Where(x => !m.IsBoundaryVertex(x)).OrderBy(x => m.GetVertex(x).Distance(m.GetVertex(v))).First();
                Vector3d pu = m.GetVertex(u), pv = m.GetVertex(v);
                m.SetVertex(v, pu + gap * (pv - pu).Normalized);
            }
            return m;
        }

        [Fact]
        public void Reducer_QualityAgainstWorst_RemovesSliverKeepsFloor()
        {
            foreach (bool cap in new[] { false, true })
            foreach (bool worst in new[] { false, true }) {
                DMesh3 m = GridWithSliver(1e-4, cap);
                double before = MinQuality(m);
                var r = new Reducer(m) { MinCollapseTriangleQuality = 0.3, CollapseQualityAgainstWorst = worst, AreaWeightedQuadrics = false };
                r.SetExternalConstraints(new MeshConstraints());
                MeshConstraintUtil.FixAllBoundaryEdges(r.Constraints, m);
                r.ReduceToMaxQuadricError(1e-12);
                double after = MinQuality(m);
                Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                // never below min(threshold, the worst before)
                Assert.True(after >= Math.Min(0.3, before), $"cap {cap}, worst {worst}: {after} < {before}");
                if (worst)
                    Assert.True(after > 0.01, $"cap {cap}: the sliver stays: {after} (before {before})");
            }
        }

        // the wrap of a shape with a sliver put in: vertex v (the k-th vertex) moved next to a neighbour (needle) or next
        // to the midpoint of the opposite edge of one of its triangles (cap), within the tangent plane
        static DMesh3 WrapWithSliver(string shape, int k, bool cap, out double tol)
        {
            var w = new MeshAlphaWrap(Shape(shape)) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute());
            DMesh3 m = new DMesh3(w.Result);
            tol = 0.5 * w.UsedOffset;
            int v = m.VertexIndices().ElementAt(k);
            Index3i t = m.GetTriangle(m.VtxTrianglesItr(v).First());
            int u = t.a == v ? t.b : t.a, x = t.c == v ? t.b : t.c;
            Vector3d target = cap ? 0.5 * (m.GetVertex(u) + m.GetVertex(x)) : m.GetVertex(u);
            m.SetVertex(v, target + 1e-3 * (m.GetVertex(v) - target));
            return m;
        }

        [Fact]
        public void SimplifyWithin_SliverOfTheWrap_Removed()
        {
            int kept = 0, cases = 0;
            foreach (string shape in new[] { "box", "cylinder" })
            foreach (bool cap in new[] { false, true })
            for (int k = 0; k < 400; k += 37) {
                DMesh3 m = WrapWithSliver(shape, k, cap, out double tol);
                if (MeshRobustRepair.NotCleanReason(m, true) != "") continue;
                double before = MinQuality(m);
                DMesh3 simp = MeshRobustRepair.SimplifyWithin(m, tol, tol, true, () => false);
                if (simp == null) continue;
                cases++;
                double after = MinQuality(simp);
                Assert.True(after >= Math.Min(MeshRobustRepair.WrapSimplifyMinQuality, before), $"{shape} {cap} {k}: {after} < {before}");
                if (after < 0.01) kept++;
            }
            Assert.True(cases >= 20, $"{cases} cases");
            Assert.True(kept == 0, $"the sliver stays in {kept} of {cases} cases");
        }

        // wraps with poor triangles (RandomSoup, the refinement at non-manifold spots off; AlphaWrapNonManifoldTests): the
        // simplification improves the worst triangle, though each collapse that removes it makes a triangle below the guard
        // (better than the worst, worse than its own predecessor), which the comparison per triangle refused: there the
        // worst triangle of the wrap stayed as it was (seed search over 1-120: equal on every seed, better on 20 with the
        // comparison against the worst)
        [Theory]
        [InlineData(36)]
        [InlineData(47)]
        [InlineData(90)]
        public void SimplifyWithin_WorstTriangleOfTheWrapImproved(int seed)
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(seed)) { RelativeAlpha = 20, RelativeOffset = 200, RefineNonManifold = false };
            w.Compute();
            Assert.NotNull(w.Result);
            DMesh3 wrap = w.Result;
            double tol = 0.5 * w.UsedOffset;
            double before = MinQuality(wrap);
            DMesh3 simp = MeshRobustRepair.SimplifyWithin(wrap, tol, tol, true, () => false);
            Assert.NotNull(simp);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(simp, true));
            double after = MinQuality(simp);
            Assert.True(after > 1.05 * before, $"{after} (wrap {before})");
        }

        class OneCollapse : Reducer
        {
            public OneCollapse(DMesh3 m) : base(m) { }
            public bool Collapse(int a, int b, Vector3d p)
            {
                int k;
                return CollapseEdge(mesh.FindEdge(a, b), p, out k) == ProcessResult.Ok_Collapsed;
            }
        }

        // a 6 x 6 grid of unit squares (vertex j * 7 + i at (i, j)) with vertex 8 moved to 0.02 from vertex 0: needles
        static DMesh3 GridWithNeedles()
        {
            var m = new DMesh3();
            for (int j = 0; j <= 6; ++j) for (int i = 0; i <= 6; ++i) m.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < 6; ++j)
                for (int i = 0; i < 6; ++i) {
                    int a = j * 7 + i, b = a + 1, c = a + 7, d = c + 1;
                    if ((i + j) % 2 == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                }
            m.SetVertex(8, new Vector3d(0, 0, 0) + 0.02 * new Vector3d(1, 1, 0).Normalized);
            return m;
        }

        // the exception of CollapseQualityAgainstWorst holds only for a collapse that removes the worst triangle around
        // its edge: then it may make triangles below the guard (better than the removed one); a collapse next to the
        // needles that would make such triangles without removing them is refused, as without the option (allowing it
        // whenever the new triangles beat the worst one around would spread triangles below the guard: 406 of about 8000
        // collapses on such grids differ)
        [Fact]
        public void Reducer_QualityAgainstWorst_OnlyWhenTheWorstIsRemoved()
        {
            Func<bool, int, int, Vector3d, bool> collapse = (worst, a, b, p) =>
                new OneCollapse(GridWithNeedles()) { MinCollapseTriangleQuality = 0.3, CollapseQualityAgainstWorst = worst }.Collapse(a, b, p);
            // edge 2-10 onto vertex 10: a needle at vertex 8 is in the one-ring of 2 but not one of the edge's triangles; the
            // collapse makes triangles below the guard that are worse than their predecessors (but better than the needle)
            Vector3d p10 = GridWithNeedles().GetVertex(10);
            Assert.False(collapse(false, 2, 10, p10));
            Assert.False(collapse(true, 2, 10, p10));
            // (where the exception applies, collapses remove the worst triangles: SimplifyWithin_WorstTriangleOfTheWrapImproved)
        }
    }
}
