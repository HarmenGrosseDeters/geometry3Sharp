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
    }
}
