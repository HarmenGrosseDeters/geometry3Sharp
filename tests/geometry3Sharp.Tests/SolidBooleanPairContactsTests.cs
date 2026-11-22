using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.PairContacts: the contact lines of touching solids are reduced with their twin edges (Reducer.PairCoincidentVertices).
    /// Off (default) the pipeline is the one before the option.
    /// </summary>
    public class SolidBooleanPairContactsTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union;

        static List<DMesh3> EdgeTouch(int n) { return new List<DMesh3> { ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(Vector3d.Zero, n)), ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(new Vector3d(2, 2, 0), n)) }; }

        static SolidBooleanPipeline Run(List<DMesh3> inputs, bool pair, Action<SolidBooleanPipeline> set = null)
        {
            var p = new SolidBooleanPipeline(inputs, U) { PairContacts = pair };
            if (set != null) set(p);
            Assert.True(p.Compute(), p.Error);
            return p;
        }

        [Fact]
        public void Default_IsOff_AndOffIsTheOldPipeline_ByHash()
        {
            Assert.False(new SolidBooleanPipeline(new List<DMesh3>(), U).PairContacts);
            var p = Run(EdgeTouch(8), false, x => x.TargetFraction = 0.1);
            Assert.Equal("CE10E8713978347AEC59A512", ReducerPairedContactsTests.Hash(p.Result));      // (taken on the code before the option)
            Assert.Equal(0, p.Report.PairedCollapses);
        }

        [Fact]
        public void EdgeTouch_ReducesTheContactLine_ResultIsASolid()
        {
            var off = Run(EdgeTouch(8), false, x => x.TargetTriangleCount = 4);
            var on = Run(EdgeTouch(8), true, x => x.TargetTriangleCount = 4);
            Assert.True(off.Report.ResultTriangles >= 60, "off: " + off.Report.ResultTriangles);
            Assert.Equal(8, on.Report.ResultTriangles);                // a tetrahedron per solid
            Assert.Equal(SolidBooleanStopReason.Exhausted, on.Report.StopReason);
            Assert.True(on.Report.PairedCollapses >= 6);
            Assert.Equal(2, on.Report.ContactsResult);                 // the two ends of the contact line
            Assert.True(on.Report.ContactsResult <= on.Report.ContactsBoolean);
            Assert.True(on.Result.IsClosed());
            Assert.True(on.Result.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Null(ReducerPairedContactsTests.FirstCrossing(on.Result));
            // NotCleanReason is not changed: a result that keeps contacts is reported as before (documented on SolidBooleanReport.Clean)
            Assert.Equal("self-intersections", on.Report.NotCleanReason);
            Assert.Equal(off.Report.NotCleanReason, on.Report.NotCleanReason);
        }

        [Fact]
        public void TargetFraction_FineBoxes_ReachesTheTargetWithFewerContacts()
        {
            var on = Run(EdgeTouch(8), true, x => x.TargetFraction = 0.02);
            var off = Run(EdgeTouch(8), false, x => x.TargetFraction = 0.02);
            Assert.True(on.Report.ResultTriangles < off.Report.ResultTriangles, on.Report.ResultTriangles + " vs " + off.Report.ResultTriangles);
            Assert.True(on.Report.ContactsResult < off.Report.ContactsResult || on.Report.ResultTriangles <= on.Report.TargetTriangles);
            Assert.True(on.Result.IsClosed());
            Assert.Null(ReducerPairedContactsTests.FirstCrossing(on.Result));
        }

        [Fact]
        public void NoContacts_OptionChangesNothing()
        {
            var inputs = new List<DMesh3> { ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(Vector3d.Zero, 6)), ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(new Vector3d(0.6, 0.4, 0.2), 6)) };
            var off = Run(inputs, false, x => x.TargetFraction = 0.2);
            var on = Run(inputs, true, x => x.TargetFraction = 0.2);
            Assert.Equal(ReducerPairedContactsTests.Hash(off.Result), ReducerPairedContactsTests.Hash(on.Result));
            Assert.Equal(0, on.Report.PairedCollapses);
            Assert.Equal("", on.Report.NotCleanReason);
        }

        [Fact]
        public void Compute_Twice_SameResult()
        {
            var p = Run(EdgeTouch(6), true, x => x.TargetFraction = 0.05);
            string h = ReducerPairedContactsTests.Hash(p.Result);
            int paired = p.Report.PairedCollapses;
            Assert.True(p.Compute());
            Assert.Equal(h, ReducerPairedContactsTests.Hash(p.Result));
            Assert.Equal(paired, p.Report.PairedCollapses);
            p.MinTriangleQuality = 0.05;      // (settings can change between calls)
            Assert.True(p.Compute());
            Assert.True(p.Result.IsClosed());
        }

        [Fact]
        public void MaxDeviation_IsKept_AndTwoSidedIsRefused()
        {
            var p = Run(EdgeTouch(6), true, x => { x.TargetTriangleCount = 4; x.MaxDeviation = 0.05; });
            Assert.True(p.Result.IsClosed());
            Assert.True(p.Report.PairedCollapses > 0);
            Assert.Null(ReducerPairedContactsTests.FirstCrossing(p.Result));
            var bad = new SolidBooleanPipeline(EdgeTouch(4), U) { PairContacts = true, TargetTriangleCount = 4, MaxDeviation = 0.05, MaxDeviationTwoSided = true };
            Assert.False(bad.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, bad.Status);
        }

        [Fact]
        public void PowerOfTwoScales_GiveTheSameResult_BitForBit()
        {
            var unit = Run(EdgeTouch(6), true, x => x.TargetFraction = 0.05);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var inputs = EdgeTouch(6).Select(m => Invariance.Transformed(m, s)).ToList();
                var p = Run(inputs, true, x => x.TargetFraction = 0.05);
                string d = Invariance.Difference(unit.Result, Invariance.MappedBack(p.Result, s));
                Assert.True(d == "", s.Name + ": " + d);
            }
        }

        // ---- PairContactsMode ----
        const SolidBooleanPipeline.PairContactsModes Auto = SolidBooleanPipeline.PairContactsModes.Auto;

        static SolidBooleanPipeline RunMode(List<DMesh3> inputs, SolidBooleanPipeline.PairContactsModes mode, Action<SolidBooleanPipeline> set = null)
        {
            var p = new SolidBooleanPipeline(inputs, U) { PairContactsMode = mode };
            if (set != null) set(p);
            Assert.True(p.Compute(), p.Error);
            return p;
        }

        static string H(SolidBooleanPipeline p) { return ReducerPairedContactsTests.Hash(p.Result); }

        static List<DMesh3> VertexTouch(int n) { return new List<DMesh3> { ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(Vector3d.Zero, n)), ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(new Vector3d(2, 2, 2), n)) }; }

        static DMesh3 PoleSphere()
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 13 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, new Vector3d(0, 2, 0));
            return m;
        }

        [Fact]
        public void Mode_DefaultIsOff_AndOffIsTheOldPipeline()
        {
            Assert.Equal(SolidBooleanPipeline.PairContactsModes.Off, new SolidBooleanPipeline(new List<DMesh3>(), U).PairContactsMode);
            var p = Run(EdgeTouch(8), false, x => x.TargetFraction = 0.1);
            Assert.Equal("CE10E8713978347AEC59A512", H(p));
            Assert.False(p.Report.PairPassRan);
            Assert.False(p.Report.PairPassKept);
            Assert.Equal(0, p.Report.PairPassMilliseconds);
        }

        [Fact]
        public void Auto_EdgeTouch_RunsTheSecondPass_AndKeepsTheResultOfOn()
        {
            Action<SolidBooleanPipeline> set = x => x.TargetTriangleCount = 4;
            var off = Run(EdgeTouch(8), false, set);
            var on = Run(EdgeTouch(8), true, set);
            var auto = RunMode(EdgeTouch(8), Auto, set);
            Assert.True(auto.Report.PairPassRan);
            Assert.True(auto.Report.PairPassKept);
            Assert.Equal(off.Report.ResultTriangles, auto.Report.PairPassFirstTriangles);
            Assert.True(auto.Report.ResultTriangles < auto.Report.PairPassFirstTriangles);
            Assert.Equal(H(on), H(auto));                                 // the second reduction is the one of On, bit for bit
            Assert.Equal(on.Report.PairedCollapses, auto.Report.PairedCollapses);
            Assert.Equal(on.Report.StopReason, auto.Report.StopReason);
            Assert.Equal(on.Report.ContactsResult, auto.Report.ContactsResult);
            Assert.Equal(on.Report.NotCleanReason, auto.Report.NotCleanReason);
            Assert.Equal(auto.Result.TriangleCount, auto.Report.ResultTriangles);
            Assert.True(auto.Result.IsClosed());
            Assert.Null(ReducerPairedContactsTests.FirstCrossing(auto.Result));
            Assert.True(auto.Report.TotalMilliseconds >= auto.Report.PairPassMilliseconds);
        }

        [Fact]
        public void Auto_KeptPairPass_ReportsTheFinalCheckOfTheReturnedMesh()
        {
            var auto = RunMode(EdgeTouch(16), Auto, x => x.TargetFraction = 0.1);
            Assert.True(auto.Report.PairPassKept);
            Assert.True(auto.Report.OnlyContacts, auto.Report.NotCleanReason + " " + auto.Report.ContactsResult);     // (so the copy of the fields is seen)
            var again = new SolidBooleanReport();
            SolidBooleanPipeline.FinalCheck(auto.Result, again);
            Assert.Equal(again.NotCleanReason, auto.Report.NotCleanReason);
            Assert.Equal(again.ContactsResult, auto.Report.ContactsResult);
            Assert.Equal(again.SelfIntersectingTriangles, auto.Report.SelfIntersectingTriangles);
            Assert.Equal(again.OnlyContacts, auto.Report.OnlyContacts);
        }

        [Fact]
        public void Auto_AsManyTrianglesAndFewerContacts_IsKept()
        {
            // fine boxes at 10 %: both reductions reach the target (the same triangle count), the paired one keeps fewer contacts
            var auto = RunMode(EdgeTouch(16), Auto, x => x.TargetFraction = 0.1);
            Assert.True(auto.Report.PairPassRan);
            Assert.Equal(auto.Report.PairPassFirstTriangles, auto.Report.ResultTriangles);
            Assert.Equal(auto.Report.TargetTriangles, auto.Report.ResultTriangles);
            Assert.True(auto.Report.ContactsResult < auto.Report.ContactsBoolean, auto.Report.ContactsResult + " of " + auto.Report.ContactsBoolean);      // (the unpaired result keeps all of them)
            Assert.True(auto.Report.PairPassKept);
            Assert.True(auto.Result.IsClosed());
        }

        [Fact]
        public void Auto_SecondPassWithoutGain_IsNotKept_AndTheReportIsTheFirstOnes()
        {
            // a contact point has no twin edge: pairing cannot help, the result is the one of Off with its report
            Action<SolidBooleanPipeline> set = x => x.TargetFraction = 0.1;
            var off = Run(VertexTouch(6), false, set);
            var auto = RunMode(VertexTouch(6), Auto, set);
            Assert.True(off.Report.ContactsResult > 0 && !off.Report.Clean);
            Assert.True(auto.Report.PairPassRan);
            Assert.False(auto.Report.PairPassKept);
            Assert.Equal(H(off), H(auto));
            Assert.Equal(off.Report.ResultTriangles, auto.Report.PairPassFirstTriangles);
            Assert.Equal(off.Report.StopReason, auto.Report.StopReason);
            Assert.Equal(off.Report.StopDetail, auto.Report.StopDetail);
            Assert.Equal(off.Report.PairedCollapses, auto.Report.PairedCollapses);
            Assert.Equal(off.Report.TargetTriangles, auto.Report.TargetTriangles);
            Assert.Equal((off.Report.BlockedByGuard, off.Report.BlockedByDeviation, off.Report.BlockedByQuality, off.Report.BlockedByTopology, off.Report.BlockedAtContacts),
                (auto.Report.BlockedByGuard, auto.Report.BlockedByDeviation, auto.Report.BlockedByQuality, auto.Report.BlockedByTopology, auto.Report.BlockedAtContacts));
            Assert.Equal(off.Report.ContactsResult, auto.Report.ContactsResult);
            Assert.Equal(off.Report.NotCleanReason, auto.Report.NotCleanReason);
        }

        [Fact]
        public void Auto_CleanResults_AreTheOnesOfOff_ByHash()
        {
            // no contacts in the boolean result
            var noContacts = new List<DMesh3> { ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(Vector3d.Zero, 6)), ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(new Vector3d(0.6, 0.4, 0.2), 6)) };
            var a0 = RunMode(noContacts, Auto, x => x.TargetFraction = 0.2);
            Assert.Equal(H(Run(noContacts, false, x => x.TargetFraction = 0.2)), H(a0));
            Assert.False(a0.Report.PairPassRan);

            // a contact in the boolean result that the reduction removes: the result is clean, no second pass
            Func<List<DMesh3>> pole = () => new List<DMesh3> {
                ReducerPairedContactsTests.Soup(ReducerPairedContactsTests.Box(Vector3d.Zero, 8)),
                ReducerPairedContactsTests.Soup(PoleSphere()) };
            var off = Run(pole(), false, x => x.TargetFraction = 0.25);
            var auto = RunMode(pole(), Auto, x => x.TargetFraction = 0.25);
            Assert.True(auto.Report.ContactsBoolean > 0);
            Assert.Equal(H(off), H(auto));
            Assert.Equal(off.Report.Clean, auto.Report.Clean);
            Assert.True(off.Report.Clean);
            Assert.Equal(0, off.Report.ContactsResult);
            Assert.False(auto.Report.PairPassRan);
        }

        [Fact]
        public void ReductionState_RoundTrips_AllTheFieldsOfAReduction()
        {
            var r = new SolidBooleanReport {
                StopReason = SolidBooleanStopReason.Contacts, StopDetail = "detail", TargetTriangles = 11, PairedCollapses = 3,
                BlockedByGuard = 1, BlockedByDeviation = 2, BlockedByQuality = 4, BlockedByTopology = 5, BlockedAtContacts = 6, ResultTriangles = 99
            };
            SolidBooleanReport saved = r.ReductionState();
            var other = new SolidBooleanReport { ResultTriangles = 7 };
            other.SetReductionState(saved);
            Assert.Equal(SolidBooleanStopReason.Contacts, other.StopReason);
            Assert.Equal("detail", other.StopDetail);
            Assert.Equal((11, 3, 1, 2, 4, 5, 6), (other.TargetTriangles, other.PairedCollapses, other.BlockedByGuard, other.BlockedByDeviation, other.BlockedByQuality, other.BlockedByTopology, other.BlockedAtContacts));
            Assert.Equal(7, other.ResultTriangles);      // (only the reduction's fields)
        }

        [Fact]
        public void Auto_NoCriterion_HasNoSecondPass()
        {
            var p = RunMode(EdgeTouch(4), Auto);
            Assert.False(p.Report.PairPassRan);
            Assert.Equal(SolidBooleanStopReason.NotReduced, p.Report.StopReason);
        }

        [Fact]
        public void Modes_OnIsPairContacts_Auto_IgnoresTwoSided_UndefinedIsRefused()
        {
            var viaMode = RunMode(EdgeTouch(6), SolidBooleanPipeline.PairContactsModes.On, x => x.TargetTriangleCount = 4);
            Assert.Equal(H(Run(EdgeTouch(6), true, x => x.TargetTriangleCount = 4)), H(viaMode));
            Assert.False(viaMode.Report.PairPassRan);
            var boolAndAuto = new SolidBooleanPipeline(EdgeTouch(6), U) { PairContacts = true, PairContactsMode = Auto, TargetTriangleCount = 4 };
            Assert.True(boolAndAuto.Compute());
            Assert.Equal(H(viaMode), H(boolAndAuto));                        // (the bool wins: pairing from the start, no second pass)
            Assert.False(boolAndAuto.Report.PairPassRan);

            var twoSided = RunMode(EdgeTouch(6), Auto, x => { x.TargetTriangleCount = 4; x.MaxDeviation = 0.05; x.MaxDeviationTwoSided = true; });
            Assert.False(twoSided.Report.PairPassRan);
            Assert.Equal(H(Run(EdgeTouch(6), false, x => { x.TargetTriangleCount = 4; x.MaxDeviation = 0.05; x.MaxDeviationTwoSided = true; })), H(twoSided));
            var onTwoSided = new SolidBooleanPipeline(EdgeTouch(4), U) { PairContactsMode = SolidBooleanPipeline.PairContactsModes.On, TargetTriangleCount = 4, MaxDeviation = 0.05, MaxDeviationTwoSided = true };
            Assert.False(onTwoSided.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, onTwoSided.Status);
            foreach (int bad in new[] { -1, 3 }) {
                var q = new SolidBooleanPipeline(EdgeTouch(4), U) { PairContactsMode = (SolidBooleanPipeline.PairContactsModes)bad, TargetTriangleCount = 4 };
                Assert.False(q.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, q.Status);
            }
        }

        [Fact]
        public void Auto_MaxDeviation_IsKeptInTheSecondPass()
        {
            var p = RunMode(EdgeTouch(6), Auto, x => { x.TargetTriangleCount = 4; x.MaxDeviation = 0.05; });
            Assert.True(p.Report.PairPassRan);
            Assert.True(p.Result.IsClosed());
            Assert.Null(ReducerPairedContactsTests.FirstCrossing(p.Result));
            var on = Run(EdgeTouch(6), true, x => { x.TargetTriangleCount = 4; x.MaxDeviation = 0.05; });
            Assert.True(p.Report.PairPassKept);
            Assert.Equal(H(on), H(p));
        }

        [Fact]
        public void Auto_CancelledInTheSecondPass_StopsWithNoResult()
        {
            var cts = new System.Threading.CancellationTokenSource();
            var p = new SolidBooleanPipeline(EdgeTouch(6), U) { PairContactsMode = Auto, TargetTriangleCount = 4, Cancellation = cts.Token, BeforePairPass = () => cts.Cancel() };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Null(p.Result);
        }

        [Fact]
        public void Auto_Compute_Twice_SameResult_AndFollowsChangedMode()
        {
            var p = RunMode(EdgeTouch(6), Auto, x => x.TargetTriangleCount = 4);
            string h = H(p);
            Assert.True(p.Report.PairPassKept);
            Assert.True(p.Compute());
            Assert.Equal(h, H(p));
            Assert.True(p.Report.PairPassKept);
            p.PairContactsMode = SolidBooleanPipeline.PairContactsModes.Off;
            Assert.True(p.Compute());
            Assert.False(p.Report.PairPassRan);
            Assert.False(p.Report.PairPassKept);
            Assert.Equal(0, p.Report.PairPassMilliseconds);
            Assert.NotEqual(h, H(p));
            p.PairContactsMode = Auto;
            Assert.True(p.Compute());
            Assert.Equal(h, H(p));
        }

        [Fact]
        public void Auto_RepeatedRuns_AreBitIdentical()
        {
            string h = H(RunMode(EdgeTouch(6), Auto, x => x.TargetFraction = 0.05));
            for (int k = 0; k < 3; ++k)
                Assert.Equal(h, H(RunMode(EdgeTouch(6), Auto, x => x.TargetFraction = 0.05)));
        }

        [Fact]
        public void Auto_PowerOfTwoScales_GiveTheSameResult_BitForBit()
        {
            var unit = RunMode(EdgeTouch(6), Auto, x => x.TargetTriangleCount = 4);
            Assert.True(unit.Report.PairPassKept);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var inputs = EdgeTouch(6).Select(m => Invariance.Transformed(m, s)).ToList();
                var p = RunMode(inputs, Auto, x => x.TargetTriangleCount = 4);
                Assert.Equal(unit.Report.PairPassKept, p.Report.PairPassKept);
                string d = Invariance.Difference(unit.Result, Invariance.MappedBack(p.Result, s));
                Assert.True(d == "", s.Name + ": " + d);
            }
        }
    }
}
