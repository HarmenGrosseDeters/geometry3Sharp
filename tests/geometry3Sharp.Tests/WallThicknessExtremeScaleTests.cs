using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T1 finding R34: a sphere at subnormal scale (1e-310) made MeshWallThickness.Compute hang (the absolute
    /// float floor of the distance was 1e303 times the mesh); at 2^k scales the result must equal that at scale 1
    /// </summary>
    public class WallThicknessExtremeScaleTests
    {
        static DMesh3 Scaled(double s)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, s * m.GetVertex(v));
            return m;
        }

        static double[] Run(double s, out double mean, out double max)
        {
            DMesh3 m = Scaled(s);
            var w = new MeshWallThickness(m) { Method = MeshWallThickness.Methods.Ray };
            bool done = false;
            var t = new System.Threading.Thread(() => { w.Compute(); done = true; }) { IsBackground = true };
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(10)) && done, "Compute did not finish at scale " + s);
            mean = w.Statistics.Mean / s; max = w.Statistics.Max / s;
            return m.VertexIndices().Select(v => w.VertexThickness[v] / s).ToArray();
        }

        [Theory]
        [InlineData(-600)]
        [InlineData(-530)]
        [InlineData(530)]
        [InlineData(600)]
        public void ThicknessAtPowerOfTwoScales_EqualsTheUnitScaleResult(int k)
        {
            double[] unit = Run(1, out double mean0, out double max0);
            double[] scaled = Run(Math.Pow(2, k), out double mean, out double max);
            Assert.Equal(unit.Length, scaled.Length);
            for (int i = 0; i < unit.Length; ++i)
                Assert.True(unit[i].Equals(scaled[i]) || (double.IsNaN(unit[i]) && double.IsNaN(scaled[i])), $"vertex {i}: {unit[i]:R} vs {scaled[i]:R}");
            Assert.Equal(mean0, mean);
            Assert.Equal(max0, max);
        }

        [Fact]
        public void SubnormalSphere_Finishes()
        {
            Run(1e-310, out double _, out double _);
        }
    }
}
