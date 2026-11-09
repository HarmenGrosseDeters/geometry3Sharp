using System;
using System.Linq;
using BaselineHarness;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The logic of benchmarks/ReducerRemesherBaseline (not its measurements): the statistics, the similarities of the
    /// scale invariance column, the bit-for-bit mesh hash of the determinism columns, the angle counts of the quality
    /// columns and the procedural inputs. A wrong helper would make every baseline number wrong without any failure.
    /// </summary>
    public class BaselineHarnessTests
    {
        [Fact]
        public void Percentile_InterpolatesAndSkipsNonFinite()
        {
            double[] v = { 4, double.NaN, 1, 3, double.PositiveInfinity, 2 };
            Assert.Equal(1, Stats.Percentile(v, 0));
            Assert.Equal(4, Stats.Percentile(v, 1));
            Assert.Equal(2.5, Stats.Median(v));
            Assert.Equal(1.3, Stats.Percentile(v, 0.1), 12);     // 1 + 0.3 * (2 - 1)
            Assert.Equal(7, Stats.Median(new[] { 7.0 }));
            Assert.True(double.IsNaN(Stats.Median(new double[0])));
            Assert.True(double.IsNaN(Stats.Median(new[] { double.NaN })));
        }

        [Fact]
        public void Xform_RoundTripsAndFlagsExactlyThePowersOfTwo()
        {
            var p = new Vector3d(0.3, -1.7, 2.9);
            foreach (Xform x in Xform.All()) {
                Vector3d q = x.Inverse(x.Apply(p));
                Assert.True(p.Distance(q) < 1e-9, x.Name);
            }
            Assert.Equal(new[] { "identity", "scale 2^-10", "scale 2^10" }, Xform.All().Where(x => x.IsPowerOfTwoScaling).Select(x => x.Name).ToArray());
            Assert.Equal(8, Xform.All().Count());
        }

        [Fact]
        public void Xform_PowerOfTwoScalingMapsBackBitForBit_Others_DoNot()
        {
            DMesh3 m = Inputs.BumpySphere(12);
            string hash = MeshHash.Of(m);
            foreach (Xform x in Xform.All().Where(x => x.IsPowerOfTwoScaling))
                Assert.Equal(hash, MeshHash.Of(x.MapBack(x.Transform(m))));
            Assert.NotEqual(hash, MeshHash.Of(Xform.All().First(x => x.Name == "rotated").Let(x => x.MapBack(x.Transform(m)))));
            Assert.Equal(hash, MeshHash.Of(m));      // the input is not modified
        }

        [Fact]
        public void MeshHash_SeesOneUlpAndTheTriangles()
        {
            DMesh3 a = Inputs.Terrain(6), b = new DMesh3(a);
            Assert.Equal(MeshHash.Of(a), MeshHash.Of(b));
            Vector3d v = b.GetVertex(10);
            b.SetVertex(10, new Vector3d(v.x, v.y, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v.z) + 1)));
            Assert.NotEqual(MeshHash.Of(a), MeshHash.Of(b));
            DMesh3 c = new DMesh3(a);
            Index3i t = c.GetTriangle(0);
            c.SetTriangle(0, new Index3i(t.a, t.c, t.b));      // flipped orientation
            Assert.NotEqual(MeshHash.Of(a), MeshHash.Of(c));
        }

        [Fact]
        public void AngleSummary_CountsSliversAndCapsAndTakesTheP01()
        {
            var m = new DMesh3();
            void tri(double ax, double ay, double bx, double by, double cx, double cy) =>
                m.AppendTriangle(m.AppendVertex(new Vector3d(ax, ay, 0)), m.AppendVertex(new Vector3d(bx, by, 0)), m.AppendVertex(new Vector3d(cx, cy, 0)));
            tri(0, 0, 1, 0, 0.5, Math.Sqrt(3) / 2);      // equilateral: 60 degrees
            tri(0, 0, 1, 0, 1, 0.005);                   // needle: min angle atan(0.005) = 0.286 degrees, max about 90
            tri(0, 0, 1, 0, 0.5, 0.02);                  // cap: apex 180 - 2 atan(0.04) = 175.4 degrees, min 2.29
            tri(0, 0, 1, 0, 2, 0);                       // collinear: degenerate, counts as below 0.5 and above 170
            AngleSummary s = AngleSummary.Compute(m);
            Assert.Equal(4, s.Triangles);
            Assert.Equal(2, s.Below05);
            Assert.Equal(2, s.Above170);
            Assert.Equal(0, s.MinAngle, 12);
            Assert.True(s.MinAngleP01 >= 0 && s.MinAngleP01 < 0.05, s.MinAngleP01.ToString());      // 0 + 0.03 * 0.286 (interpolated)
        }

        [Fact]
        public void AngleSummary_EmptyMeshIsNaN()
        {
            AngleSummary s = AngleSummary.Compute(new DMesh3());
            Assert.Equal(0, s.Triangles);
            Assert.True(double.IsNaN(s.MinAngle) && double.IsNaN(s.MinAngleP01));
        }

        [Fact]
        public void Inputs_HaveTheTopologyTheyAreNamedFor()
        {
            MeshTopologySummary sphere = MeshTopologySummary.Compute(Inputs.BumpySphere(12)), torus = MeshTopologySummary.Compute(Inputs.Torus(24, 12));
            MeshTopologySummary box = MeshTopologySummary.Compute(Inputs.Box(5)), terrain = MeshTopologySummary.Compute(Inputs.Terrain(8));
            Assert.Equal((1, 1, 0.0), (sphere.Components, sphere.ClosedComponents, sphere.Genus));
            Assert.Equal((1, 1, 1.0), (torus.Components, torus.ClosedComponents, torus.Genus));
            Assert.Equal((1, 1, 0.0), (box.Components, box.ClosedComponents, box.Genus));
            Assert.Equal((1, 0, 1), (terrain.Components, terrain.ClosedComponents, terrain.BoundaryLoops));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(Inputs.Torus(24, 12)));
            Assert.Equal(2 * 24 * 12, Inputs.Torus(24, 12).TriangleCount);
            Assert.Equal(2 * 8 * 8, Inputs.Terrain(8).TriangleCount);
        }

        [Fact]
        public void MeanEdge_OfAUnitGrid()
        {
            // 1 x 1 grid of unit squares split in two: 2 unit edges per square side and one diagonal of sqrt 2
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(1, 1, 0)), d = m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            Assert.Equal((4 + Math.Sqrt(2)) / 5, Inputs.MeanEdge(m), 12);
            Assert.Equal(0, Inputs.MeanEdge(new DMesh3()));
        }
    }

    static class BaselineHarnessExtensions
    {
        public static R Let<T, R>(this T x, Func<T, R> f) => f(x);
    }
}
