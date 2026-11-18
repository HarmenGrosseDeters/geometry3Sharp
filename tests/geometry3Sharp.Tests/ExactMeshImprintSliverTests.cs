using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshImprint at near contact (#1034, docs/design/IMPRINT_SLIVER_CLEANUP.md): a cutter that is the target shifted by a few
    /// ulps of its largest coordinate. The result is clean per NotCleanReason, closed, keeps the target's vertices bit for bit and
    /// carries the curve (every curve edge is an edge of Result). Through the public API only, so that the tests run on the base.
    /// </summary>
    public class ExactMeshImprintSliverTests
    {
        // the box (1, 0.7, 0.4) rotated by 29 degrees about z and 17 about x, as doubles: the rotation would use Math.Sin / Cos, which
        // differ in the last bit between runtimes (docs/design/DETERMINISM.md C1), so the corners are given as the literals that
        // Matrix3d.AxisAngleD gives on net10 (the box whose 1-ulp shifts left slivers before #1034)
        static readonly double[] BoxCorners = {
            -0.6067795312896138, -0.9680060882596397, -0.58718209569113,
            1.1424598829891777, 0.0016131522330344594, -0.58718209569113,
            0.4933838391796905, 1.1725773320679964, -0.1778617090792985,
            -1.255855575099101, 0.20295809157532227, -0.1778617090792985,
            -0.4933838391796905, -1.1725773320679964, 0.1778617090792985,
            1.255855575099101, -0.20295809157532227, 0.1778617090792985,
            0.6067795312896138, 0.9680060882596397, 0.58718209569113,
            -1.1424598829891777, -0.0016131522330344594, 0.58718209569113 };

        // a box rotated off the axes, and a capped cone frustum (fan caps: many target edges meet at the rim and the centres)
        internal static DMesh3 Shape(string kind)
        {
            if (kind == "box") {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)) }.Generate().MakeDMesh();
                Assert.Equal(8, m.MaxVertexID);
                foreach (int v in m.VertexIndices().ToArray())
                    m.SetVertex(v, new Vector3d(BoxCorners[3 * v], BoxCorners[3 * v + 1], BoxCorners[3 * v + 2]));
                return m;
            }
            return Snapped(new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.6f, Height = 2f, Slices = 12 }.Generate().MakeDMesh());
        }

        // the generator uses Math.Sin / Cos, which differ in the last bit between runtimes (docs/design/DETERMINISM.md C1): the
        // vertices are snapped to a grid of 2^-24, exactly, so that the input is the same on net48 and net10
        internal static DMesh3 Snapped(DMesh3 m)
        {
            const double grid = 1.0 / (1 << 24);
            foreach (int v in m.VertexIndices().ToArray()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, new Vector3d(Math.Round(p.x / grid) * grid, Math.Round(p.y / grid) * grid, Math.Round(p.z / grid) * grid));
            }
            return m;
        }

        static double NextUp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);

        // the mesh moved along the axis by ulps units in the last place of its largest coordinate on that axis (exact: checked per vertex)
        internal static DMesh3 Shifted(DMesh3 m, int axis, int ulps)
        {
            double maxAbs = m.Vertices().Max(p => Math.Abs(p[axis]));
            double step = ulps * (NextUp(maxAbs) - maxAbs);
            var b = new DMesh3(m);
            foreach (int v in b.VertexIndices().ToArray()) {
                Vector3d p = b.GetVertex(v);
                double x = p[axis] + step;
                Assert.Equal(step, x - p[axis]);       // the shift is exact
                p[axis] = x;
                b.SetVertex(v, p);
            }
            return b;
        }

        internal static ExactMeshImprint Run(DMesh3 target, DMesh3 cutter)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
            Assert.True(imprint.Compute(), imprint.Error);
            Assert.Equal(ExactMeshImprint.ImprintStatus.Success, imprint.Status);
            return imprint;
        }

        // clean, closed, the target's vertices kept, the curve carried
        internal static void AssertCleanAndCarried(DMesh3 target, ExactMeshImprint imprint)
        {
            DMesh3 r = imprint.Result;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.True(imprint.RoundingClean);
            Assert.True(r.IsClosed());
            Assert.True(r.Vertices().All(p => p.IsFinite));
            foreach (int v in target.VertexIndices()) {
                Assert.True(r.IsVertex(v));
                Assert.Equal(target.GetVertex(v), r.GetVertex(v));
            }
            Assert.NotEmpty(imprint.CurveEdges);
            Assert.Equal(imprint.CurveEdges.Count, imprint.Counts["curve_edges"]);
            foreach (long k in imprint.CurveEdges)
                Assert.True(r.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0, "a curve edge is not an edge of Result");
            Assert.True(imprint.Counts["new_vertices"] > 0);
        }

        // (kind, axis, ulps): each one left slivers before #1034 in both directions
        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "box", 2, 1 };
            yield return new object[] { "box", 2, -1 };
            yield return new object[] { "box", 2, 3 };
            yield return new object[] { "cylinder", 0, 1 };
            yield return new object[] { "cylinder", 0, -4 };
            yield return new object[] { "cylinder", 2, 1 };
            yield return new object[] { "cylinder", 2, -2 };
            yield return new object[] { "cylinder", 2, 5 };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void UlpShiftedCopy_BothDirections_AreCleanClosedAndCarryTheCurve(string kind, int axis, int ulps)
        {
            DMesh3 a = Shape(kind), b = Shifted(a, axis, ulps);
            AssertCleanAndCarried(a, Run(a, b));
            AssertCleanAndCarried(b, Run(b, a));
        }

        public static IEnumerable<object[]> Similarities() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Invariance_UlpShiftedCopy_IsCleanUnderEverySimilarity_AndBitIdenticalUnderPowersOfTwo(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (string kind in new[] { "box", "cylinder" }) {
                int axis = kind == "box" ? 2 : 0;
                DMesh3 a = Invariance.Transformed(Shape(kind), s), b = Shifted(a, axis, 1);
                ExactMeshImprint imprint = Run(a, b);
                AssertCleanAndCarried(a, imprint);
                // a scaling by 2^k is exact (the ulp scales with it): the same result bit for bit, scaled
                if (s.IsRotated == false && s.Translation == Vector3d.Zero && Math.Log(s.Scale, 2) % 1 == 0) {
                    DMesh3 a0 = Shape(kind);
                    ExactMeshImprint i0 = Run(a0, Shifted(a0, axis, 1));
                    Assert.Equal(i0.Result.MaxVertexID, imprint.Result.MaxVertexID);
                    foreach (int v in i0.Result.VertexIndices())
                        Assert.Equal(s.Apply(i0.Result.GetVertex(v)), imprint.Result.GetVertex(v));
                    Assert.Equal(i0.Result.MaxTriangleID, imprint.Result.MaxTriangleID);
                    foreach (int t in i0.Result.TriangleIndices())
                        Assert.Equal(i0.Result.GetTriangle(t), imprint.Result.GetTriangle(t));
                    Assert.Equal(i0.CurveEdges.OrderBy(x => x), imprint.CurveEdges.OrderBy(x => x));
                    Assert.Equal(i0.SourceTriangle, imprint.SourceTriangle);
                }
            }
        }

        static bool Same(DMesh3 a, DMesh3 b) =>
            a.MaxVertexID == b.MaxVertexID && a.MaxTriangleID == b.MaxTriangleID
            && a.VertexIndices().All(v => b.IsVertex(v) && a.GetVertex(v) == b.GetVertex(v))
            && a.TriangleIndices().All(t => b.IsTriangle(t) && a.GetTriangle(t) == b.GetTriangle(t));

        [Fact]
        public void Reuse_ComputeTwiceOnOneInstance_SameCleanResult()
        {
            DMesh3 a = Shape("cylinder"), b = Shifted(a, 2, 1);
            var imprint = new ExactMeshImprint() { Target = a, Cutter = b };
            Assert.True(imprint.Compute(), imprint.Error);
            AssertCleanAndCarried(a, imprint);
            DMesh3 first = imprint.Result;
            var edges = new HashSet<long>(imprint.CurveEdges);
            var sources = (int[])imprint.SourceTriangle.Clone();
            Assert.True(imprint.Compute(), imprint.Error);
            AssertCleanAndCarried(a, imprint);
            Assert.True(Same(first, imprint.Result));
            Assert.Equal(edges, imprint.CurveEdges);
            Assert.Equal(sources, imprint.SourceTriangle);
        }

        [Fact]
        public void Determinism_SerialEqualsParallel_AndRepeatedRuns()
        {
            DMesh3 a = Shape("cylinder"), b = Shifted(a, 0, 1);
            ExactMeshImprint reference = Run(a, b);
            AssertCleanAndCarried(a, reference);
            for (int i = 0; i < 3; ++i) {
                ExactMeshImprint again = Run(a, b);
                Assert.True(Same(reference.Result, again.Result));
                Assert.Equal(reference.CurveEdges, again.CurveEdges);
                Assert.Equal(reference.SourceTriangle, again.SourceTriangle);
            }
            gParallel.SerialOnThisThread = true;
            try {
                ExactMeshImprint serial = Run(a, b);
                Assert.True(Same(reference.Result, serial.Result));
                Assert.Equal(reference.CurveEdges, serial.CurveEdges);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }
    }
}
