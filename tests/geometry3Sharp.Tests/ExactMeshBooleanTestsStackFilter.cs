using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1047 (the 20 s rule on Thingi10K 42327): the classification's coplanar stacks test a piece only against the coplanar
    /// triangles whose boxes meet the piece's box, and L2's steps compute the exact side of a piece's centroid of a triangle's
    /// plane once per cluster. Both leave every decision as it was: the tests compare the results, the open winding numbers
    /// per piece and the work counted with and without them, on an open sheet of coplanar layers (each triangle overlaps
    /// triangles of the other layers in its plane, so most pieces lie in stacks) cut by a sphere. (The class name keeps the
    /// mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsStackFilter
    {
        // a disc of rings x sectors in the plane z = 0, turned by angle about the z axis, then tilted by tilt about the x
        // axis (its own vertices)
        static void AppendDisc(DMesh3 m, int rings, int sectors, double radius, double angle, double tilt = 0)
        {
            int center = m.AppendVertex(Vector3d.Zero);
            var ring = new int[rings + 1, sectors];
            for (int r = 1; r <= rings; ++r)
                for (int s = 0; s < sectors; ++s) {
                    double a = angle + 2 * Math.PI * s / sectors, d = radius * r / rings;
                    double y = d * Math.Sin(a);
                    ring[r, s] = m.AppendVertex(tilt == 0 ? new Vector3d(d * Math.Cos(a), y, 0) : new Vector3d(d * Math.Cos(a), y * Math.Cos(tilt), y * Math.Sin(tilt)));
                }
            for (int s = 0; s < sectors; ++s) {
                int s1 = (s + 1) % sectors;
                m.AppendTriangle(center, ring[1, s], ring[1, s1]);
                for (int r = 1; r < rings; ++r) {
                    m.AppendTriangle(ring[r, s], ring[r + 1, s], ring[r + 1, s1]);
                    m.AppendTriangle(ring[r, s], ring[r + 1, s1], ring[r, s1]);
                }
            }
        }

        // an open mesh of layers coplanar discs, each turned a little against the last, and a tilted disc through them (so
        // that L2's steps in the layers meet triangles of their own mesh, and those of the tilted disc the layers), and a
        // closed sphere through all of them
        internal static (DMesh3, DMesh3) Layers(int layers = 3)
        {
            var a = new DMesh3();
            for (int k = 0; k < layers; ++k)
                AppendDisc(a, 3, 12, 1, 0.23 * k);
            // (a copy of the first layer: coincident triangles, each a coplanar triangle of the other with the same box)
            AppendDisc(a, 3, 12, 1, 0);
            AppendDisc(a, 3, 12, 0.9, 0.1, 0.5);
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 0.55, EdgeVertices = 6 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(0.31, 0.17, 0.05));
            return (a, b);
        }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, Ops op, bool filters, bool exactOnly = false)
        {
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, RecordOpenWinding = true,
                CoplanarBoxFilter = filters, MemoTracedSides = filters, DisableFilters = exactOnly, RecordStacks = true };
            Assert.True(s.Compute(), s.Error);
            return s;
        }

        static void AssertFinite(DMesh3 m)
        {
            Assert.True(m.TriangleCount > 0);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z), $"vertex {v}: {p}");
            }
        }

        static void AssertSameDecisions(ExactMeshBoolean x, ExactMeshBoolean y)
        {
            // the stacks themselves: the same triangles in the same order for every piece
            Assert.Equal(x.StacksPerPiece.Length, y.StacksPerPiece.Length);
            for (int i = 0; i < x.StacksPerPiece.Length; ++i)
                Assert.Equal(x.StacksPerPiece[i], y.StacksPerPiece[i]);
            Assert.Equal("", Invariance.Difference(x.Result, y.Result));
            Assert.Equal(x.OpenWindingPerPiece.Length, y.OpenWindingPerPiece.Length);
            for (int i = 0; i < x.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(x.OpenWindingPerPiece[i], y.OpenWindingPerPiece[i]);
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Difference)]
        public void StackFilterAndSideMemo_SameResult_LessWork(Ops op)
        {
            (DMesh3 a, DMesh3 b) = Layers();
            ExactMeshBoolean on = Computed(a, b, op, true), off = Computed(a, b, op, false);
            AssertFinite(on.Result);
            AssertSameDecisions(off, on);
            // the stacks: most coplanar triangles of a piece's source miss the piece (the source overlaps them only in part)
            Assert.True(off.Counts["coplanar_tests"] > 1000, $"{off.Counts["coplanar_tests"]} tests");
            Assert.True(on.StacksPerPiece.Count(st => st != null) > 100, "pieces in stacks");
            Assert.True(10 * on.Counts["coplanar_tests"] < 7 * off.Counts["coplanar_tests"], $"{on.Counts["coplanar_tests"]} of {off.Counts["coplanar_tests"]} tests");
            // it traces (L2), so the steps' sides are taken
            Assert.True(on.Counts["l2_carried"] + on.Counts["l2_traced"] > 0);
            // the memo, where every side is exact (no filters): each side once per piece and triangle, the same decisions
            ExactMeshBoolean onExact = Computed(a, b, op, true, exactOnly: true), offExact = Computed(a, b, op, false, exactOnly: true);
            AssertSameDecisions(offExact, onExact);
            AssertSameDecisions(on, onExact);
            Assert.True(offExact.Counts["l2_exact_sides"] > 300, $"{offExact.Counts["l2_exact_sides"]} sides");
            Assert.True(10 * onExact.Counts["l2_exact_sides"] < 8 * offExact.Counts["l2_exact_sides"], $"{onExact.Counts["l2_exact_sides"]} of {offExact.Counts["l2_exact_sides"]} sides");
            Assert.Equal(offExact.Counts["coplanar_tests"], off.Counts["coplanar_tests"]);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void StackFilter_UnderSimilarity_SameAsWithout(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = Layers();
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            ExactMeshBoolean on = Computed(ta, tb, Ops.Union, true), off = Computed(ta, tb, Ops.Union, false);
            AssertSameDecisions(off, on);
            // (a rotation rounds the layers' vertices one by one, so that only the copy of the first layer stays exactly
            // coplanar with it, with the same boxes: nothing to filter there)
            if (sim.IsExact)
                Assert.True(10 * on.Counts["coplanar_tests"] < 7 * off.Counts["coplanar_tests"], $"{on.Counts["coplanar_tests"]} of {off.Counts["coplanar_tests"]} tests");
            if (sim.IsExact) {
                Assert.True(off.Counts["coplanar_tests"] > 1000, $"{off.Counts["coplanar_tests"]} tests");
                ExactMeshBoolean plain = Computed(a, b, Ops.Union, true);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(on.Result, sim)));
                Assert.Equal(plain.Counts["coplanar_tests"], on.Counts["coplanar_tests"]);
            }
        }

        [Fact]
        public void StackFilter_SerialEqualsParallel_RepeatedAndReused()
        {
            (DMesh3 a, DMesh3 b) = Layers();
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Computed(a, b, Ops.Difference, true, exactOnly: true);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(a, b, Ops.Difference) { AllowOpenMeshes = true, RecordOpenWinding = true, DisableFilters = true, RecordStacks = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute(), reused.Error);
                AssertSameDecisions(serial, reused);
                foreach (string c in new[] { "coplanar_tests", "l2_exact_sides", "l2_carried", "l2_traced", "l2_direct" })
                    Assert.Equal(serial.Counts[c], reused.Counts[c]);
            }
        }
    }
}
