using System;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #636: a few triangles that fall through to the alpha wrap fallback took seconds, because the wrap size depends on the bounding
    /// box diagonal over alpha only (about 200 000 wrap triangles). For an input below SmallInputWrapTriangles the wrap takes its alpha
    /// from the input's own edge length.
    /// </summary>
    public class MeshRobustRepairSmallInputWrapTests
    {
        static readonly double[][] Oct = {
            new[] { 1.0, 0, 0 }, new[] { -1.0, 0, 0 }, new[] { 0.0, 1, 0 }, new[] { 0.0, 1, 0 },   // vertex 3 on vertex 2: two faces coincide, no volume
            new[] { 0.0, 0, 1 }, new[] { 0.0, 0, -1 } };
        static readonly int[][] OctTris = {
            new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };

        // the input of the issue (MeshFuzzPilot dump): the octahedron above at 2.407412430484045E-35
        static DMesh3 Input(double scale = 1)
        {
            var m = new DMesh3();
            foreach (var p in Oct) m.AppendVertex(new Vector3d(p[0] * scale, p[1] * scale, p[2] * scale));
            foreach (var t in OctTris) m.AppendTriangle(t[0], t[1], t[2]);
            return m;
        }

        const double TinyScale = 2.407412430484045E-35;

        // a power of two that brings the diagonal into [1, 2): the thresholds of NotCleanReason (1e-12 of a squared diagonal) underflow far below
        static DMesh3 Normalized(DMesh3 mesh)
        {
            double d = mesh.GetBounds().DiagonalLength;
            double k = Math.Pow(2, -Math.Floor(Math.Log(d, 2)));
            var copy = new DMesh3(mesh);
            foreach (int v in copy.VertexIndices()) copy.SetVertex(v, copy.GetVertex(v) * k);
            return copy;
        }

        static void AssertRepaired(MeshRobustRepair r, bool ok)
        {
            Assert.True(ok, "Apply() " + r.ResultReason);
            Assert.Equal("", r.ResultReason);
            Assert.True(r.UsedAlphaWrap, "the case is about the wrap fallback");
            Assert.True(r.Mesh.IsClosed());
            Assert.True(AllFinite(r.Mesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(Normalized(r.Mesh)));
        }

        static bool AllFinite(DMesh3 m) => m.VertexIndices().All(v => m.GetVertex(v).IsFinite);

        [Fact]
        public void TinyDegenerateOctahedron_IsRepairedInSeconds_NotWithTwoHundredThousandWrapTriangles()
        {
            DMesh3 input = Input(TinyScale);
            var r = new MeshRobustRepair(new DMesh3(input));
            var sw = Stopwatch.StartNew();
            bool ok = r.Apply();
            sw.Stop();
            AssertRepaired(r, ok);
            Assert.True(r.AlphaWrapTriangles < 5000, "wrap triangles " + r.AlphaWrapTriangles);   // master: 204 222
            Assert.True(sw.Elapsed.TotalSeconds < 4, "took " + sw.Elapsed.TotalSeconds + " s");   // master: 9 to 12 s
        }

        [Fact]
        public void WrapStillEnclosesTheInput_WithinTheReportedOffset()
        {
            DMesh3 input = Input();
            var r = new MeshRobustRepair(new DMesh3(input));
            AssertRepaired(r, r.Apply());
            double offset = r.AlphaWrap.UsedOffset, alpha = r.AlphaWrap.UsedAlpha;
            var wrapTree = new DMeshAABBTree3(r.Mesh, true);
            wrapTree.Build();
            // the result is within the offset of the input, plus the half offset of the simplification (the documented distance of the wrap and of simplify_wrap) ...
            var inputTree = new DMeshAABBTree3(input, true);
            inputTree.Build();
            foreach (int v in r.Mesh.VertexIndices()) {
                int tid = inputTree.FindNearestTriangle(r.Mesh.GetVertex(v));
                double d = Math.Sqrt(MeshQueries.TriDistanceSqr(input, tid, r.Mesh.GetVertex(v)));
                Assert.True(d <= 1.5 * offset, $"vertex {v} at {d}, offset {offset}");
            }
            // ... and encloses it: every input triangle's centroid is inside
            foreach (int t in input.TriangleIndices())
                Assert.True(wrapTree.FastWindingNumber(input.GetTriCentroid(t)) > 0.5, "triangle " + t);
        }

        [Fact]
        public void AtOrAboveTheThreshold_TheWrapIsTheFormerOne()
        {
            // a coarse wrap so that the test is fast: the same input with the threshold equal to its triangle count (not below it) and with the
            // rule off must give the same mesh bit for bit; one triangle more in the threshold changes the alpha
            DMesh3 input = Input();
            int n = input.TriangleCount;
            MeshRobustRepair Run(int threshold) {
                var r = new MeshRobustRepair(new DMesh3(input)) { WrapRelativeAlpha = 20, WrapRelativeOffset = 200, SmallInputWrapTriangles = threshold };
                Assert.True(r.Apply());
                Assert.True(r.UsedAlphaWrap);
                return r;
            }
            var off = Run(0);
            var atThreshold = Run(n);
            Assert.Equal(off.AlphaWrap.UsedAlpha, atThreshold.AlphaWrap.UsedAlpha);
            Assert.Equal(off.AlphaWrap.UsedOffset, atThreshold.AlphaWrap.UsedOffset);
            Assert.Equal(off.Mesh.VertexCount, atThreshold.Mesh.VertexCount);
            Assert.Equal(off.Mesh.TriangleCount, atThreshold.Mesh.TriangleCount);
            foreach (int v in off.Mesh.VertexIndices()) {
                Vector3d a = off.Mesh.GetVertex(v), b = atThreshold.Mesh.GetVertex(v);
                Assert.True(BitConverter.DoubleToInt64Bits(a.x) == BitConverter.DoubleToInt64Bits(b.x) && BitConverter.DoubleToInt64Bits(a.y) == BitConverter.DoubleToInt64Bits(b.y)
                    && BitConverter.DoubleToInt64Bits(a.z) == BitConverter.DoubleToInt64Bits(b.z), "vertex " + v);
            }
            var below = Run(n + 1);
            Assert.True(below.AlphaWrap.UsedAlpha > off.AlphaWrap.UsedAlpha);
            Assert.Equal(below.AlphaWrap.UsedAlpha * (20.0 / 200.0), below.AlphaWrap.UsedOffset, 12);   // the offset keeps its ratio to alpha
        }


        [Fact]
        public void FarParts_KeepTheFormerWrapJudgement()
        {
            // two degenerate parts 1e7 sizes apart (the far part case: lostPartInput is set, the wrap is judged against the largest component).
            // The rule is off there; it would also change nothing, since the edge length of parts that far apart is below diag / 300 (alpha is
            // the larger of the two). Either way the result is what it was with the rule off
            DMesh3 two = Input();
            DMesh3 b = Input(2);
            foreach (int v in b.VertexIndices().ToArray()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(1e7, 0, 0));
            new MeshEditor(two).AppendMesh(b);
            MeshRobustRepair Run(int threshold) {
                var r = new MeshRobustRepair(new DMesh3(two)) { WrapRelativeAlpha = 20, WrapRelativeOffset = 200, SmallInputWrapTriangles = threshold };
                r.Apply();
                return r;
            }
            var off = Run(0);
            var on = Run(64);
            Assert.Equal(off.ResultReason, on.ResultReason);
            Assert.Equal(off.UsedAlphaWrap, on.UsedAlphaWrap);
            Assert.Equal(off.WrapReason, on.WrapReason);
            Assert.Equal(off.Mesh.VertexCount, on.Mesh.VertexCount);
            Assert.Equal(off.Mesh.TriangleCount, on.Mesh.TriangleCount);
            if (on.AlphaWrap != null) Assert.Equal(off.AlphaWrap.UsedAlpha, on.AlphaWrap.UsedAlpha);
        }

        [Fact]
        public void Reuse_TwoRunsOnTheSameInputGiveTheSameWrap()
        {
            DMesh3 input = Input();
            var a = new MeshRobustRepair(new DMesh3(input)); AssertRepaired(a, a.Apply());
            var b = new MeshRobustRepair(new DMesh3(input)); AssertRepaired(b, b.Apply());
            Assert.Equal(a.AlphaWrap.UsedAlpha, b.AlphaWrap.UsedAlpha);
            Assert.Equal(a.Mesh.TriangleCount, b.Mesh.TriangleCount);
        }

        [Theory, MemberData(nameof(Similarities))]
        public void Invariance_AllSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Input();
            var r0 = new MeshRobustRepair(new DMesh3(input));
            AssertRepaired(r0, r0.Apply());
            var r1 = new MeshRobustRepair(Invariance.Transformed(input, s));
            AssertRepaired(r1, r1.Apply());
            Assert.True(Math.Abs(r1.AlphaWrap.UsedAlpha - s.Scale * r0.AlphaWrap.UsedAlpha) <= (s.IsExact ? 0 : 1e-6) * r1.AlphaWrap.UsedAlpha, "alpha scales with the input");
            if (s.IsExact)
                Assert.Equal(s.Scale * r0.AlphaWrap.UsedAlpha, r1.AlphaWrap.UsedAlpha);
            Assert.True(Math.Abs(r1.AlphaWrap.UsedOffset / r1.AlphaWrap.UsedAlpha - r0.AlphaWrap.UsedOffset / r0.AlphaWrap.UsedAlpha) < 1e-12);
            if (s.IsExact) {
                // a power of two: the same wrap bit for bit
                Assert.Equal(r0.Mesh.VertexCount, r1.Mesh.VertexCount);
                Assert.Equal(r0.Mesh.TriangleCount, r1.Mesh.TriangleCount);
                foreach (int v in r0.Mesh.VertexIndices()) {
                    Vector3d a = r0.Mesh.GetVertex(v) * s.Scale, b = r1.Mesh.GetVertex(v);
                    Assert.True(a.x == b.x && a.y == b.y && a.z == b.z, "vertex " + v);
                }
            } else {
                // other similarities: a wrap of the same size class (the grid it samples is not the rotated one)
                Assert.InRange((double)r1.Mesh.TriangleCount / r0.Mesh.TriangleCount, 0.5, 2.0);
            }
        }

        public static System.Collections.Generic.IEnumerable<object[]> Similarities() => Similarity.Names();
    }
}
