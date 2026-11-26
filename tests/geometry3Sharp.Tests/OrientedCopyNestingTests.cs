using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1609 (sibling of #1566): WindingContouring.OrientedCopy reverses a closed shell inside an odd number of others (parity), also when it is outward
    /// by its own volume (an insert in a housing). MeshWindingNumberSolidify, MeshWindingNumberBoolean, the metrics reference and MeshWallThickness
    /// then saw it as a cavity and the result was hollow (volume 7.936 / 7.000 / 3.904 for an inner cube of half size 0.2 / 0.5 / 0.8 in a cube
    /// of volume 8), with Compute() true and nothing reported. Now the shells are counted (ComponentsFlippedByNesting) and Compute() is false with
    /// ResultReason NestingAmbiguous where the plain union of the shells differs by more than 1 - MinVolumeIoU. A real cavity (inner cube inward) is
    /// unchanged. The new public members are read by reflection, so that this file compiles against the base branch's library (the tests fail there).
    /// </summary>
    public class OrientedCopyNestingTests
    {
        readonly ITestOutputHelper output;
        public OrientedCopyNestingTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Box(Vector3d c, double h)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(c, new Vector3d(h, h, h)), NoSharedVertices = false };
            g.Generate();
            return g.MakeDMesh();
        }

        // the outer cube (half size 1, volume 8) with an inner cube of the given half size; inward: the inner cube is reversed (a real cavity)
        static DMesh3 Nested(double inner, bool inward)
        {
            DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0);
            DMesh3 n = Box(new Vector3d(1, 1, 1), inner);
            if (inward) n.ReverseOrientation(false);
            MeshEditor.Append(m, n);
            return m;
        }

        static DMesh3 Far() { return Box(new Vector3d(5, 1, 1), 0.5); }

        static DMesh3 TwoDisjoint()
        {
            DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0);
            MeshEditor.Append(m, Box(new Vector3d(5, 1, 1), 0.5));
            return m;
        }

        public static IEnumerable<object[]> Sizes() { yield return new object[] { 0.2 }; yield return new object[] { 0.5 }; yield return new object[] { 0.8 }; }

        // ---- reflection on the new members (the file compiles against the base library) ----

        static T Get<T>(object o, string name)
        {
            PropertyInfo p = o.GetType().GetProperty(name);
            Assert.True(p != null, o.GetType().Name + "." + name + " does not exist");
            return (T)p.GetValue(o);
        }

        static void SetMin(object o, double v)
        {
            FieldInfo f = o.GetType().GetField("MinVolumeIoU");
            Assert.True(f != null, o.GetType().Name + ".MinVolumeIoU does not exist");
            f.SetValue(o, v);
        }

        static string AmbiguousText(Type t)
        {
            FieldInfo f = t.GetField("NestingAmbiguous");
            Assert.True(f != null, t.Name + ".NestingAmbiguous does not exist");
            return (string)f.GetRawConstantValue();
        }

        static int StaticInt(Type t, string name, params object[] args)
        {
            MethodInfo m = t.GetMethod(name);
            Assert.True(m != null, t.Name + "." + name + " does not exist");
            return (int)m.Invoke(null, args);
        }

        static double Readings(DMesh3 input, out int count)
        {
            MethodInfo m = typeof(MeshRepairMetrics).GetMethod("NestingReadingsIoU");
            Assert.True(m != null, "MeshRepairMetrics.NestingReadingsIoU does not exist");
            object[] args = { input, 0, 100000, 1 };
            double r = (double)m.Invoke(null, args);
            count = (int)args[1];
            return r;
        }

        // ---- the operations ----

        static MeshWindingNumberSolidify Solidify(DMesh3 m, double minIoU = double.NaN)
        {
            var s = new MeshWindingNumberSolidify(new DMesh3(m)) { GridResolution = 24, Adaptive = true };
            if (!double.IsNaN(minIoU)) SetMin(s, minIoU);
            s.Compute();
            return s;
        }

        static MeshWindingNumberBoolean Boolean(DMesh3 a, DMesh3 b, MeshWindingNumberBoolean.Operations op = MeshWindingNumberBoolean.Operations.Union, double minIoU = double.NaN)
        {
            var s = new MeshWindingNumberBoolean(new DMesh3(a), new DMesh3(b), op) { GridResolution = 24 };
            if (!double.IsNaN(minIoU)) SetMin(s, minIoU);
            s.Compute();
            return s;
        }

        // the nesting check switched off (internal NestingCheck): what the operation computed before #1609
        static void NoCheck(object o)
        {
            FieldInfo f = o.GetType().GetField("NestingCheck", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.True(f != null, o.GetType().Name + ".NestingCheck does not exist");
            f.SetValue(o, false);
        }

        static string Reason(object o) { return Get<string>(o, "ResultReason"); }

        // ---- OrientedCopy ----

        [Theory]
        [MemberData(nameof(Sizes))]
        public void OrientedCopy_ReportsTheOutwardShellItReversed(double inner)
        {
            DMesh3 input = Nested(inner, false);
            MethodInfo m = typeof(WindingContouring).GetMethod("OrientedCopy", new[] { typeof(DMesh3), typeof(int).MakeByRefType() });
            Assert.True(m != null, "WindingContouring.OrientedCopy(DMesh3, out int) does not exist");
            object[] args = { input, 0 };
            DMesh3 copy = (DMesh3)m.Invoke(null, args);
            Assert.Equal(1, (int)args[1]);
            // the same copy as OrientedCopy(m): hollow, volume 8 - inner
            Assert.Equal("", Invariance.Difference(WindingContouring.OrientedCopy(input), copy));
            Assert.Equal(8 - Math.Pow(2 * inner, 3), Invariance.Volume(copy), 9);
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void OrientedCopy_RealCavity_ReportsNothing(double inner)
        {
            object[] args = { Nested(inner, true), 0 };
            MethodInfo m = typeof(WindingContouring).GetMethod("OrientedCopy", new[] { typeof(DMesh3), typeof(int).MakeByRefType() });
            Assert.True(m != null, "WindingContouring.OrientedCopy(DMesh3, out int) does not exist");
            m.Invoke(null, args);
            Assert.Equal(0, (int)args[1]);
        }

        [Fact]
        public void OrientedCopy_InvertedPart_IsNotCountedAsNesting()
        {
            // an inside-out cube next to an outward one (no nesting): reversed, but not "by nesting"
            DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0);
            DMesh3 n = Box(new Vector3d(5, 1, 1), 1.0);
            n.ReverseOrientation(false);
            MeshEditor.Append(m, n);
            object[] args = { m, 0 };
            MethodInfo mi = typeof(WindingContouring).GetMethod("OrientedCopy", new[] { typeof(DMesh3), typeof(int).MakeByRefType() });
            Assert.True(mi != null, "WindingContouring.OrientedCopy(DMesh3, out int) does not exist");
            mi.Invoke(null, args);
            Assert.Equal(0, (int)args[1]);
        }

        // ---- MeshWindingNumberSolidify ----

        // the repro of #1609 without the new members (it runs on the base library too): Compute() true with the hollow solid, nothing reported, is the defect
        [Theory]
        [InlineData(0.5)]
        [InlineData(0.8)]
        public void Solidify_OutwardInnerShell_NeverReturnsTheHollowSolidSilently(double inner)
        {
            var s = new MeshWindingNumberSolidify(Nested(inner, false)) { GridResolution = 24, Adaptive = true };
            if (s.Compute())
                Assert.True(Invariance.Volume(s.Result) > 7.9, "solidify: hollow volume " + Invariance.Volume(s.Result));
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(0.8)]
        public void Boolean_OutwardInnerShell_NeverReturnsTheHollowSolidSilently(double inner)
        {
            var b = new MeshWindingNumberBoolean(Nested(inner, false), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            if (b.Compute())
                Assert.True(Invariance.Volume(b.Result) > 8.9, "boolean: hollow volume " + Invariance.Volume(b.Result));
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void Solidify_OutwardInnerShell_IsReported_AndNotReturnedAsHollow(double inner)
        {
            MeshWindingNumberSolidify s = Solidify(Nested(inner, false));
            double innerVolume = Math.Pow(2 * inner, 3);
            output.WriteLine(inner + ": reason='" + Reason(s) + "' flipped " + Get<int>(s, "ComponentsFlippedByNesting") + " result " + (s.Result == null ? "null" : "" + Invariance.Volume(s.Result)));
            Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNesting"));
            if (innerVolume / 8 > 0.01) {
                Assert.Null(s.Result);
                Assert.Equal(AmbiguousText(typeof(MeshWindingNumberSolidify)), Reason(s));
            } else {
                // below the tolerance (0.2: 0.8 %): reported by the count only, the result is the hollow one
                Assert.NotNull(s.Result);
                Assert.Equal("", Reason(s));
                Assert.Equal(8 - innerVolume, Invariance.Volume(s.Result), 1);
            }
        }

        [Fact]
        public void Solidify_OutwardInnerShell_WithoutTheGate_IsHollow_ButCounted()
        {
            MeshWindingNumberSolidify s = Solidify(Nested(0.5, false), 0);
            Assert.NotNull(s.Result);
            Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNesting"));
            Assert.Equal("", Reason(s));
            Assert.Equal(7.0, Invariance.Volume(s.Result), 1);
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void Solidify_RealCavity_StaysAsItWas(double inner)
        {
            MeshWindingNumberSolidify s = Solidify(Nested(inner, true));
            Assert.NotNull(s.Result);
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNesting"));
            Assert.Equal("", Reason(s));
            // (the contouring error of this grid: 0.0020 measured for all three sizes, a quarter of a percent of the volume)
            Assert.InRange(Math.Abs(Invariance.Volume(s.Result) - (8 - Math.Pow(2 * inner, 3))), 0, 0.003);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(s.Result));
            // the same bit for bit as without the check
            var b = new MeshWindingNumberSolidify(Nested(inner, true)) { GridResolution = 24, Adaptive = true };
            NoCheck(b);
            b.Compute();
            Assert.Equal("", Invariance.Difference(b.Result, s.Result));
        }

        [Fact]
        public void Solidify_WithoutNesting_IsBitIdenticalToTheUncheckedRun()
        {
            // (TwoDisjoint exits at MayHaveNestedShells; the real cavity passes it and has count 0)
            foreach (DMesh3 input in new[] { TwoDisjoint(), Nested(0.5, true) }) {
                MeshWindingNumberSolidify a = Solidify(input);
                var b = new MeshWindingNumberSolidify(new DMesh3(input)) { GridResolution = 24, Adaptive = true };
                NoCheck(b);
                b.Compute();
                Assert.Equal(0, Get<int>(a, "ComponentsFlippedByNesting"));
                Assert.NotNull(a.Result);
                Assert.Equal("", Invariance.Difference(b.Result, a.Result));
            }
        }

        [Fact]
        public void Solidify_Reuse_SameOutcomeTwice()
        {
            foreach (double inner in new[] { 0.2, 0.5 }) {
                DMesh3 input = Nested(inner, false);
                var s = new MeshWindingNumberSolidify(input) { GridResolution = 24, Adaptive = true };
                bool a = s.Compute();
                string reason = Reason(s);
                int n = Get<int>(s, "ComponentsFlippedByNesting");
                DMesh3 first = s.Result == null ? null : new DMesh3(s.Result);
                bool b = s.Compute();
                Assert.Equal(a, b);
                Assert.Equal(reason, Reason(s));
                Assert.Equal(n, Get<int>(s, "ComponentsFlippedByNesting"));
                Assert.Equal(1, n);
                if (first != null) Assert.Equal("", Invariance.Difference(first, s.Result));
            }
        }

        [Fact]
        public void Solidify_ReuseAfterTheInputChanged_ReadsTheNewInput()
        {
            var s = new MeshWindingNumberSolidify(Nested(0.5, false)) { GridResolution = 24, Adaptive = true };
            Assert.False(s.Compute());
            Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNesting"));
            s.Mesh = Nested(0.5, true);
            Assert.True(s.Compute());
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNesting"));
            Assert.Equal("", Reason(s));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Solidify_Invariance(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (double inner in new[] { 0.2, 0.5 }) {
                DMesh3 input = Nested(inner, false);
                MeshWindingNumberSolidify id = Solidify(input);
                MeshWindingNumberSolidify r = Solidify(Invariance.Transformed(input, sim));
                Assert.Equal(Get<int>(id, "ComponentsFlippedByNesting"), Get<int>(r, "ComponentsFlippedByNesting"));
                Assert.Equal(Reason(id), Reason(r));
                Assert.Equal(id.Result == null, r.Result == null);
                if (sim.IsExact && id.Result != null)
                    Assert.Equal("", Invariance.Difference(id.Result, Invariance.MappedBack(r.Result, sim)));
            }
        }

        // ---- MeshWindingNumberBoolean ----

        [Theory]
        [MemberData(nameof(Sizes))]
        public void Boolean_OutwardInnerShell_IsReported_AndNotReturnedAsHollow(double inner)
        {
            MeshWindingNumberBoolean s = Boolean(Nested(inner, false), Far());
            double innerVolume = Math.Pow(2 * inner, 3);
            Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNestingA"));
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNestingB"));
            if (innerVolume / 8 > 0.01) {
                Assert.False(s.Result != null);
                Assert.Equal(AmbiguousText(typeof(MeshWindingNumberBoolean)), Reason(s));
            } else {
                Assert.NotNull(s.Result);
                Assert.Equal("", Reason(s));
                Assert.Equal(9 - innerVolume, Invariance.Volume(s.Result), 1);
            }
        }

        [Fact]
        public void Boolean_NestedSecondInput_IsReportedAsB()
        {
            MeshWindingNumberBoolean s = Boolean(Far(), Nested(0.5, false));
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNestingA"));
            Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNestingB"));
            Assert.Null(s.Result);
            Assert.Equal(AmbiguousText(typeof(MeshWindingNumberBoolean)), Reason(s));
        }

        [Fact]
        public void Boolean_OutwardInnerShell_WithoutTheGate_IsHollow_ButCounted()
        {
            MeshWindingNumberBoolean s = Boolean(Nested(0.5, false), Far(), MeshWindingNumberBoolean.Operations.Union, 0);
            Assert.NotNull(s.Result);
            Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNestingA"));
            Assert.Equal("", Reason(s));
            Assert.Equal(8.0, Invariance.Volume(s.Result), 1);      // the hollow outer cube (7) and the far cube (1)
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void Boolean_RealCavity_StaysAsItWas(double inner)
        {
            MeshWindingNumberBoolean s = Boolean(Nested(inner, true), Far());
            Assert.NotNull(s.Result);
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNestingA"));
            Assert.Equal("", Reason(s));
            Assert.Equal(9 - Math.Pow(2 * inner, 3), Invariance.Volume(s.Result), 1);
        }

        [Fact]
        public void Boolean_WithoutNesting_IsBitIdenticalWithAndWithoutTheGate()
        {
            // (the reference run bypasses the check entirely; the real cavity passes MayHaveNestedShells and has count 0)
            foreach (DMesh3 first in new[] { TwoDisjoint(), Nested(0.5, true) }) {
                DMesh3 second = Box(new Vector3d(1.5, 1, 1), 1.0);
                MeshWindingNumberBoolean a = Boolean(first, second);
                var b = new MeshWindingNumberBoolean(new DMesh3(first), new DMesh3(second), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
                NoCheck(b);
                b.Compute();
                Assert.NotNull(a.Result);
                Assert.Equal(0, Get<int>(a, "ComponentsFlippedByNestingA") + Get<int>(a, "ComponentsFlippedByNestingB"));
                Assert.Equal("", Invariance.Difference(b.Result, a.Result));
            }
        }

        [Fact]
        public void Boolean_Reuse_SameOutcomeTwice()
        {
            foreach (double inner in new[] { 0.2, 0.5 }) {
                var s = new MeshWindingNumberBoolean(Nested(inner, false), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
                bool a = s.Compute();
                string reason = Reason(s);
                DMesh3 first = s.Result == null ? null : new DMesh3(s.Result);
                bool b = s.Compute();
                Assert.Equal(a, b);
                Assert.Equal(reason, Reason(s));
                Assert.Equal(1, Get<int>(s, "ComponentsFlippedByNestingA"));
                if (first != null) Assert.Equal("", Invariance.Difference(first, s.Result));
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Boolean_Invariance(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (double inner in new[] { 0.2, 0.5 }) {
                DMesh3 a = Nested(inner, false), b = Far();
                MeshWindingNumberBoolean id = Boolean(a, b);
                MeshWindingNumberBoolean r = Boolean(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim));
                Assert.Equal(Get<int>(id, "ComponentsFlippedByNestingA"), Get<int>(r, "ComponentsFlippedByNestingA"));
                Assert.Equal(Reason(id), Reason(r));
                Assert.Equal(id.Result == null, r.Result == null);
                if (sim.IsExact && id.Result != null)
                    Assert.Equal("", Invariance.Difference(id.Result, Invariance.MappedBack(r.Result, sim)));
            }
        }

        // ---- parallel equals serial: the nesting readings and the IoU of the check (samples in parallel) ----

        [Fact]
        public void NestingCheck_ParallelEqualsSerial_AndRepeatable()
        {
            DMesh3 input = Nested(0.5, false);
            int c0;
            double p = Readings(input, out c0);
            double again = Readings(input, out c0);
            Assert.Equal(BitConverter.DoubleToInt64Bits(p), BitConverter.DoubleToInt64Bits(again));
            gParallel.SerialOnThisThread = true;
            try {
                int c1;
                double q = Readings(input, out c1);
                Assert.Equal(c0, c1);
                Assert.Equal(BitConverter.DoubleToInt64Bits(p), BitConverter.DoubleToInt64Bits(q));
                MeshWindingNumberSolidify s = Solidify(input);
                MeshWindingNumberSolidify t = Solidify(Nested(0.2, false));
                Assert.Null(s.Result);
                Assert.Equal(AmbiguousText(typeof(MeshWindingNumberSolidify)), Reason(s));
                gParallel.SerialOnThisThread = false;
                MeshWindingNumberSolidify u = Solidify(Nested(0.2, false));
                Assert.Equal("", Invariance.Difference(t.Result, u.Result));
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        // ---- MeshRepairMetrics ----

        [Fact]
        public void Metrics_ReadingsOnTheRepro_AndTheExistingFieldsUnchanged()
        {
            DMesh3 input = Nested(0.5, false);
            Assert.Equal(1, StaticInt(typeof(MeshRepairMetrics), "ComponentsFlippedByNesting", input));
            int count;
            double iou = Readings(input, out count);
            Assert.Equal(1, count);
            Assert.InRange(iou, 0.85, 0.90);        // hollow 7 against solid 8
            // the existing reading is the parity one: a result that is the hollow solid has IoU 1, the plain union (what the shells enclose) does not
            DMesh3 hollow = WindingContouring.OrientedCopy(input);
            double missing, extra;
            Assert.Equal(1.0, MeshRepairMetrics.VolumeIoU(input, hollow, out missing, out extra), 3);
            DMesh3 solid = Box(new Vector3d(1, 1, 1), 1.0);
            Assert.InRange(MeshRepairMetrics.VolumeIoU(input, solid, out missing, out extra), 0.85, 0.90);
        }

        [Fact]
        public void Metrics_NoNestedShell_CountsNothingAndIoUIsOne()
        {
            foreach (DMesh3 input in new[] { TwoDisjoint(), Nested(0.5, true), Box(new Vector3d(1, 1, 1), 1.0) }) {
                int count;
                Assert.Equal(1.0, Readings(input, out count));
                Assert.Equal(0, count);
                Assert.Equal(0, StaticInt(typeof(MeshRepairMetrics), "ComponentsFlippedByNesting", input));
            }
        }

        [Fact]
        public void NestingReadingsIoU_UsesTheSamples()
        {
            DMesh3 input = Nested(0.5, false);
            int c;
            double fine = Readings(input, out c);
            MethodInfo m = typeof(MeshRepairMetrics).GetMethod("NestingReadingsIoU");
            object[] args = { input, 0, 2000, 1 };
            double coarse = (double)m.Invoke(null, args);
            Assert.NotEqual(BitConverter.DoubleToInt64Bits(fine), BitConverter.DoubleToInt64Bits(coarse));
            Assert.InRange(coarse, 0.8, 0.95);
        }

        [Fact]
        public void NestingCheckOff_OnTheRepro_CountsNothingAndReturnsTheHollowSolid()
        {
            var s = new MeshWindingNumberSolidify(Nested(0.5, false)) { GridResolution = 24, Adaptive = true };
            NoCheck(s);
            Assert.True(s.Compute());
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNesting"));
            var b = new MeshWindingNumberBoolean(Nested(0.5, false), Far(), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            NoCheck(b);
            Assert.True(b.Compute());
            Assert.Equal(0, Get<int>(b, "ComponentsFlippedByNestingA"));
            Assert.Equal("", Reason(b));
        }

        // the mirrored case, a limit of the parity reading (as MeshRobustRepair, #1566): an insert in a housing with both shells inside-out. Only the
        // housing is flagged (it is inside out by itself), the insert is a cavity in the housing's parity: count 0, the hollow reading, no refusal
        [Fact]
        public void BothShellsInsideOut_IsNotSeen_CountZero_NoRefusal()
        {
            DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0);
            m.ReverseOrientation(false);
            DMesh3 n = Box(new Vector3d(1, 1, 1), 0.5);
            n.ReverseOrientation(false);
            MeshEditor.Append(m, n);
            Assert.Equal(0, StaticInt(typeof(MeshRepairMetrics), "ComponentsFlippedByNesting", m));
            object[] args = { m, 0 };
            typeof(WindingContouring).GetMethod("OrientedCopy", new[] { typeof(DMesh3), typeof(int).MakeByRefType() }).Invoke(null, args);
            Assert.Equal(0, (int)args[1]);
            MeshWindingNumberSolidify s = Solidify(m);
            Assert.Equal(0, Get<int>(s, "ComponentsFlippedByNesting"));
            Assert.Equal("", Reason(s));
            Assert.NotNull(s.Result);
            Assert.Equal(0, Get<int>(new MeshWallThickness(m), "ComponentsFlippedByNesting"));
        }

        // ---- MeshWallThickness ----

        [Fact]
        public void WallThickness_CountsTheOutwardShellItTakesAsACavity()
        {
            var w = new MeshWallThickness(Nested(0.5, false));
            Assert.Equal(1, Get<int>(w, "ComponentsFlippedByNesting"));
            w.OrientInvertedParts = false;
            Assert.Equal(0, Get<int>(w, "ComponentsFlippedByNesting"));
            w.OrientInvertedParts = true;
            Assert.Equal(1, Get<int>(w, "ComponentsFlippedByNesting"));
            // the mesh changes: the count follows
            w.Mesh = Nested(0.5, true);
            Assert.Equal(0, Get<int>(w, "ComponentsFlippedByNesting"));
        }

        [Fact]
        public void WallThickness_RealCavityAndPlainMesh_CountNothing()
        {
            Assert.Equal(0, Get<int>(new MeshWallThickness(Nested(0.5, true)), "ComponentsFlippedByNesting"));
            Assert.Equal(0, Get<int>(new MeshWallThickness(TwoDisjoint()), "ComponentsFlippedByNesting"));
        }

        [Fact]
        public void WallThickness_TheThicknessesStillFollowTheParityReading()
        {
            // unchanged behaviour: the insert is taken as a cavity, the walls between the shells are 0.5 thick on each side of the 1.0 gap
            var w = new MeshWallThickness(Nested(0.5, false)) { Method = MeshWallThickness.Methods.Ray, ComputeVertices = true };
            w.Compute();
            Assert.Equal(1, Get<int>(w, "ComponentsFlippedByNesting"));
            Assert.NotNull(w.TriangleThickness);
            // every triangle of both cubes sees the other cube's face 0.5 away (the inner cube is taken as a cavity: its normals point into it)
            foreach (int tid in w.Mesh.TriangleIndices())
                Assert.Equal(0.5, w.TriangleThickness[tid], 9);
        }
    }
}
