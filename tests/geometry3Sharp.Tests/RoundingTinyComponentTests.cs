using System;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding on a component below about 1e-154 (found by tools/MeshFuzzPilot: an octahedron near 1e-159 whose
    /// equator corners coincide). Its degenerate area, DegenerateArea times the squared diagonal, underflows to 0; an
    /// exactly degenerate triangle is still bad, and fix_degenerate's steps started at twice the height for that area,
    /// 0, so "s *= 4" never left 0 and Apply() never returned. Now the steps start a few units in the last place of
    /// the apex.
    /// </summary>
    public class RoundingTinyComponentTests
    {
        // an octahedron whose four equator corners coincide at the origin, with the apexes (0, 0, s) and (s, 0, 0): closed,
        // every triangle exactly degenerate
        static DMesh3 CollapsedOctahedron(double s)
        {
            var m = new DMesh3();
            foreach (var v in new[] { new Vector3d(0, 0, s), Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, new Vector3d(s, 0, 0) })
                m.AppendVertex(v);
            int[] t = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            for (int i = 0; i < t.Length; i += 3)
                m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
            return m;
        }

        // Apply() on another thread, with a time limit (before the fix it never returned)
        static MeshRoundingRepair ApplyWithin(DMesh3 m, int seconds)
        {
            var r = new MeshRoundingRepair(m);
            Task task = Task.Run(() => r.Apply());
            Assert.True(task.Wait(TimeSpan.FromSeconds(seconds)), $"Apply() did not return within {seconds} s");
            return r;
        }

        [Theory]
        [InlineData(-530)]
        [InlineData(-600)]
        [InlineData(-1000)]
        public void CollapsedOctahedron_Tiny_ApplyReturns(int exponent)
        {
            double s = Math.Pow(2, exponent);
            DMesh3 m = CollapsedOctahedron(s);
            Assert.True(m.IsClosed());
            // the setup: the degenerate area of this size underflows to 0
            double diag = m.CachedBounds.DiagonalLength;
            Assert.Equal(0.0, 1e-12 * diag * diag);
            MeshRoundingRepair r = ApplyWithin(m, 5);
            Assert.True(r.InitialDegenerate > 0);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(m.Vertices().All(v => Math.Abs(v.x) <= double.MaxValue && Math.Abs(v.y) <= double.MaxValue && Math.Abs(v.z) <= double.MaxValue));
            // the counts are the mesh's (whether the repair can clear it at this size is not claimed)
            Assert.Equal(r.RemainingDegenerate == 0 && r.RemainingIntersections == 0 && r.RemainingFalseContacts == 0
                && r.ScaleDisparity == 0 && !r.ScaleNotFinite, r.IsClean);
        }

        [Fact]
        public void CollapsedOctahedron_UnitSize_Returns()
        {
            // at unit size the height is not 0 and the steps start as before
            DMesh3 m = CollapsedOctahedron(1);
            MeshRoundingRepair r = ApplyWithin(m, 5);
            Assert.True(r.InitialDegenerate > 0);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
        }
    }
}
