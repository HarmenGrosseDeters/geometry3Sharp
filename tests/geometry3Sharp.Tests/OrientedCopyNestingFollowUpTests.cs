using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1661, follow-up of #1641 (the nesting check of MeshWindingNumberBoolean / MeshWindingNumberSolidify): the check does not write the caller's
    /// input (the lazily cached bounds), "nesting" is in PhaseMilliseconds only if the check read a nested shell, MinVolumeIoU above 1 and NaN behave as
    /// documented, and the same instance as MeshA and MeshB is checked once. New members are read by reflection, so that this file compiles against
    /// the base branch's library (the cache test fails there).
    /// </summary>
    public class OrientedCopyNestingFollowUpTests
    {
        static DMesh3 Box(Vector3d c, double h)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(c, new Vector3d(h, h, h)), NoSharedVertices = false };
            g.Generate();
            return g.MakeDMesh();
        }

        // the outer cube (half size 1) with an outward inner cube of half size 0.5: a nested outward shell (the parity reading takes it for a cavity)
        static DMesh3 Nested(double scale = 1)
        {
            DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0);
            MeshEditor.Append(m, Box(new Vector3d(1, 1, 1), 0.5));
            if (scale != 1) foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) * scale);
            return m;
        }

        // the same with an extent of 1.4: in [1, 2), where Solidify runs on the caller's mesh as it is (outside it works on a scaled copy)
        static DMesh3 NestedUnitExtent()
        {
            DMesh3 m = Box(new Vector3d(1, 1, 1), 0.7);
            MeshEditor.Append(m, Box(new Vector3d(1, 1, 1), 0.35));
            return m;
        }

        static DMesh3 Far() { return Box(new Vector3d(5, 1, 1), 0.5); }
        static DMesh3 TwoDisjoint() { DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0); MeshEditor.Append(m, Box(new Vector3d(5, 1, 1), 0.5)); return m; }

        // whether CachedBounds holds the bounds of the mesh's current state (a read of CachedBounds writes it): the private cache fields
        static bool BoundsCached(DMesh3 m)
        {
            FieldInfo stamp = typeof(DMesh3).GetField("cached_bounds_timestamp", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(stamp != null, "DMesh3.cached_bounds_timestamp does not exist");
            return (int)stamp.GetValue(m) == m.Timestamp;
        }

        static int ChecksRun(MeshWindingNumberBoolean b)
        {
            PropertyInfo p = typeof(MeshWindingNumberBoolean).GetProperty("NestingChecksRun", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.True(p != null, "MeshWindingNumberBoolean.NestingChecksRun does not exist");
            return (int)p.GetValue(b);
        }

        static void SetMin(object o, double v) { o.GetType().GetField("MinVolumeIoU").SetValue(o, v); }

        // two concurrent Compute() calls on one shared input: no write to its cached bounds (before: the nesting check read CachedBounds)
        [Fact]
        public void Boolean_TwoConcurrentComputes_LeaveTheSharedInputsAsTheyWere()
        {
            DMesh3 a = Nested(), b = Far();
            Assert.False(BoundsCached(a));
            Assert.False(BoundsCached(b));
            var x = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            var y = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            bool okX = false, okY = false;
            Parallel.Invoke(() => okX = x.Compute(), () => okY = y.Compute());
            Assert.Equal(okX, okY);
            Assert.Equal(x.ComponentsFlippedByNestingA, y.ComponentsFlippedByNestingA);
            Assert.True(x.ComponentsFlippedByNestingA > 0, "the input has a nested outward shell: the check read it");
            Assert.False(BoundsCached(a));
            Assert.False(BoundsCached(b));
        }

        [Fact]
        public void Solidify_TwoConcurrentComputes_LeaveTheSharedInputAsItWas()   // (extent in [1, 2): the caller's mesh reaches the check)
        {
            DMesh3 input = NestedUnitExtent();
            Assert.False(BoundsCached(input));
            var x = new MeshWindingNumberSolidify(input) { GridResolution = 24, Adaptive = true };
            var y = new MeshWindingNumberSolidify(input) { GridResolution = 24, Adaptive = true };
            Parallel.Invoke(() => x.Compute(), () => y.Compute());
            Assert.True(x.ComponentsFlippedByNesting > 0);
            Assert.Equal(x.ComponentsFlippedByNesting, y.ComponentsFlippedByNesting);
            Assert.False(BoundsCached(input));
        }

        // "nesting" only where the check read a nested shell (the cheap test for one is not a phase)
        [Fact]
        public void PhaseMilliseconds_HasNesting_OnlyWithANestedShell()
        {
            var plain = new MeshWindingNumberBoolean(TwoDisjoint(), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            Assert.True(plain.Compute());
            Assert.False(plain.PhaseMilliseconds.ContainsKey("nesting"));
            var nested = new MeshWindingNumberBoolean(Nested(), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            nested.Compute();
            Assert.True(nested.PhaseMilliseconds.ContainsKey("nesting"));
            var sPlain = new MeshWindingNumberSolidify(TwoDisjoint()) { GridResolution = 24, Adaptive = true };
            Assert.True(sPlain.Compute());
            Assert.False(sPlain.PhaseMilliseconds.ContainsKey("nesting"));
            var sNested = new MeshWindingNumberSolidify(Nested()) { GridResolution = 24, Adaptive = true };
            sNested.Compute();
            Assert.True(sNested.PhaseMilliseconds.ContainsKey("nesting"));
        }

        // MinVolumeIoU above 1: every input with a nested outward shell is refused; NaN: no refusal (the count is still reported)
        [Fact]
        public void MinVolumeIoU_AboveOne_Refuses_AndNaN_DoesNot()
        {
            string reason = (string)typeof(MeshWindingNumberBoolean).GetField("NestingAmbiguous").GetRawConstantValue();
            // (the inner cube is half the size: the readings differ by 0.125 of the volume, below the default 0.99 only at IoU 0.875)
            var above = new MeshWindingNumberBoolean(Nested(), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            SetMin(above, 1.5);
            Assert.False(above.Compute());
            Assert.Equal(reason, above.ResultReason);
            var nan = new MeshWindingNumberBoolean(Nested(), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            SetMin(nan, double.NaN);
            Assert.True(nan.Compute());
            Assert.Equal("", nan.ResultReason);
            Assert.True(nan.ComponentsFlippedByNestingA > 0);
            var sAbove = new MeshWindingNumberSolidify(Nested()) { GridResolution = 24, Adaptive = true };
            SetMin(sAbove, 1.5);
            Assert.False(sAbove.Compute());
            var sNan = new MeshWindingNumberSolidify(Nested()) { GridResolution = 24, Adaptive = true };
            SetMin(sNan, double.NaN);
            Assert.True(sNan.Compute());
        }

        // the same instance as MeshA and MeshB: one check (two for two instances), the counts of both inputs the same; also where the extent is
        // outside the band and the computation runs on scaled copies
        [Theory]
        [InlineData(1.0)]
        [InlineData(1e12)]
        public void Boolean_SameInstanceAsBothInputs_IsCheckedOnce(double scale)
        {
            DMesh3 a = Nested(scale);
            var same = new MeshWindingNumberBoolean(a, a, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            same.Compute();
            Assert.Equal(1, ChecksRun(same));
            Assert.True(same.ComponentsFlippedByNestingA > 0);
            Assert.Equal(same.ComponentsFlippedByNestingA, same.ComponentsFlippedByNestingB);
            var two = new MeshWindingNumberBoolean(a, new DMesh3(a), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            two.Compute();
            Assert.Equal(2, ChecksRun(two));
            Assert.Equal(two.ComponentsFlippedByNestingA, same.ComponentsFlippedByNestingA);
            Assert.Equal(two.ComponentsFlippedByNestingB, same.ComponentsFlippedByNestingB);
            Assert.Equal(two.ResultReason, same.ResultReason);
        }
    }
}
