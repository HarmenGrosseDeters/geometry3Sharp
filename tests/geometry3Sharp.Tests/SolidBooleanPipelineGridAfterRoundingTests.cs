using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;
using Auto = gs.SolidBooleanPipeline.AutomaticStepModes;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.AutomaticSteps, the default (owner 2026-11-24): a run that ends with the boolean's RoundingNotClean is done once more on the
    /// grid taken from the data (the bit GridAfterRoundingNotClean, value 4), next to the sheet retry of an open tool that the pipeline routed itself (GridBeforeFallback,
    /// value 2). A sheet that the caller stated is retried only with Grid (1). The modes that the base library does not have are named by value ((Auto)6, (Auto)4, (Auto)2),
    /// so that these tests compile against it and fail there; the tests that name the members are in SolidBooleanPipelineGridBeforeFallbackEnumTests.
    /// </summary>
    public class SolidBooleanPipelineGridAfterRoundingTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.SheetRefusal Rim = ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece;
        // the default of the library, by value: GridBeforeFallback (2) | GridAfterRoundingNotClean (4)
        static readonly Auto Default = (Auto)6, SheetOnly = (Auto)2, RoundingOnly = (Auto)4;

        static double Pow2(int k) { return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); }

        static List<DMesh3> Fragment() => SolidBooleanPipelineInputGridTests.Fragment();

        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static string Names(SolidBooleanPipeline p) => string.Join(",", p.Report.Route.Select(s => s.Name + ":" + s.Input));

        // the box [0.1, 4.1] x [0.2, 4.3] x [0.3, 4.7] and a horizontal sheet whose rim is meant to lie in its four side faces, 3 ulps inside
        static readonly Vector3d Lo = new Vector3d(0.1, 0.2, 0.3), Hi = new Vector3d(4.1, 4.3, 4.7);
        static readonly double Ulp = Pow2(-50);

        static List<DMesh3> FlushRim()
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Lo, Hi)) }.Generate().MakeDMesh();
            double x0 = Lo.x + 3 * Ulp, x1 = Hi.x - 3 * Ulp, y0 = Lo.y + 3 * Ulp, y1 = Hi.y - 3 * Ulp;
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(x0, y0, 2.5)), b = m.AppendVertex(new Vector3d(x1, y0, 2.5)), c = m.AppendVertex(new Vector3d(x1, y1, 2.5)), d = m.AppendVertex(new Vector3d(x0, y1, 2.5));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            return new List<DMesh3> { box, m };
        }

        static Roles SheetSecond() => new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };

        static void AssertGridResult(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            // (the snapped solids can touch along a contact, which the final check reports as such: a valid solid)
            Assert.True(p.Report.Clean || p.Report.OnlyContacts, p.Report.NotCleanReason);
        }

        // ---- the rounding refusal is answered by default ----

        [Fact]
        public void ARoundingRefusal_IsAnsweredByDefault_AndIsTheRefusalWithNone()
        {
            var none = new SolidBooleanPipeline(Fragment(), D) { AutomaticSteps = Auto.None };
            Assert.False(none.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, none.BooleanStatus);
            Assert.Empty(none.Report.Route);
            // the sheet retry alone (the mode before the owner's decision) is the same refusal, bit for bit
            var sheetOnly = new SolidBooleanPipeline(Fragment(), D) { AutomaticSteps = SheetOnly };
            Assert.False(sheetOnly.Compute());
            Assert.Equal(none.Status, sheetOnly.Status);
            Assert.Equal(none.Error, sheetOnly.Error);
            Assert.Equal(none.BooleanStatus, sheetOnly.BooleanStatus);
            Assert.Empty(sheetOnly.Report.Route);

            int booleans = 0;
            var p = new SolidBooleanPipeline(Fragment(), D) { ConfigureBoolean = op => booleans++ };
            Assert.Equal(Default, p.AutomaticSteps);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertGridResult(p);
            Assert.Equal(2, booleans);      // the refused run and the retry
            Assert.Equal("Grid:-1", Names(p));
            SolidBooleanRouteStep g = p.Report.Route[0];
            Assert.Equal(Pow2(-18), g.Grid);
            Assert.StartsWith("RoundingNotClean: ", g.Reason);
            Assert.Equal(53, p.Report.InputSnappedVertices);
            Assert.Contains("the cell 2^-18 (3.814697265625E-06), which moved 53 vertices, and changed the ", g.Change);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, g.SheetReason);
            // the result is the Boolean of the SNAPPED inputs, as with the caller's grid at that cell (no step listed there)
            var stated = new SolidBooleanPipeline(Fragment(), D) { InputGrid = Pow2(-18), AutomaticSteps = Auto.None };
            Assert.True(stated.Compute(), stated.Error);
            Assert.Equal("", Invariance.Difference(stated.Result, p.Result));
            // ... and within the snap of the exact result (rounded without the strict check): half a cell per coordinate times the area
            var loose = new ExactMeshBoolean(Fragment(), D);
            Assert.True(loose.Compute());
            double area = MeshMeasurements.VolumeArea(loose.Result, loose.Result.TriangleIndices(), loose.Result.GetVertex).y;
            Assert.True(Math.Abs(Volume(p.Result) - Volume(loose.Result)) <= area * Pow2(-18), Volume(p.Result) + " vs " + Volume(loose.Result));
            Assert.True(Volume(p.Result) > 0 && Volume(p.Result) < Volume(Fragment()[0]));
            // the report is that of the run that is the result
            Assert.Equal(stated.Report.RoundingVolumeBefore, p.Report.RoundingVolumeBefore);
            Assert.Equal(stated.Report.RoundingVolumeAfter, p.Report.RoundingVolumeAfter);
            Assert.Equal(stated.Report.OnlyContacts, p.Report.OnlyContacts);
            Assert.Equal(stated.Report.NotCleanReason, p.Report.NotCleanReason);
        }

        [Fact]
        public void ARunWithoutAFailure_CostsNothingExtra_AndIsTheRunWithNone()
        {
            // a cut sheet, and a boolean of two solids that is clean: one boolean each, and the output of the run without the steps
            Func<List<DMesh3>>[] cases = {
                () => {
                    var f = FlushRim();
                    var big = new DMesh3();
                    int v0 = big.AppendVertex(new Vector3d(-1, -1, 2.5)), v1 = big.AppendVertex(new Vector3d(5, -1, 2.5)), v2 = big.AppendVertex(new Vector3d(5, 5, 2.5)), v3 = big.AppendVertex(new Vector3d(-1, 5, 2.5));
                    big.AppendTriangle(v0, v1, v2); big.AppendTriangle(v0, v2, v3);
                    f[1] = big;
                    return f;
                },
                () => {
                    var s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
                    var t = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
                    MeshTransforms.Translate(t, new Vector3d(1.2, 0.1, 0.05));
                    return new List<DMesh3> { s, t };
                } };
            foreach (var inputs in cases) {
                int a = 0, b = 0;
                var def = new SolidBooleanPipeline(inputs(), D) { ConfigureBoolean = op => a++ };
                var none = new SolidBooleanPipeline(inputs(), D) { AutomaticSteps = Auto.None, ConfigureBoolean = op => b++ };
                bool d1 = def.Compute(), d2 = none.Compute();
                Assert.Equal(d2, d1);
                Assert.Equal(1, a);
                Assert.Equal(1, b);
                Assert.Equal(none.Status, def.Status);
                if (d1)
                    Assert.Equal("", Invariance.Difference(none.Result, def.Result));
                Assert.DoesNotContain("Grid", Names(def));
                Assert.Equal(0, def.Report.InputSnappedVertices);
            }
        }

        // ---- a refusal that the grid does not cure: one try, the first failure stands ----

        const int HoleTriangle = 11;

        [Fact]
        public void ARetryThatFailsToo_LeavesTheFirstFailure_AfterOneTry()
        {
            // the tool of the fragment as a solid, with one triangle flipped: the first boolean reorients it (set in ConfigureBoolean) and refuses the rounding;
            // the boolean of the retry keeps the pipeline's Orientation (Refuse) and refuses the orientation
            Func<List<DMesh3>> inputs = () => { var f = Fragment(); f[1].ReverseTriOrientation(0); return f; };
            Roles solids = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Solid };
            int booleans = 0;
            var p = new SolidBooleanPipeline(inputs(), D) { InputRoles = solids };
            p.ConfigureBoolean = op => { if (++booleans == 1) op.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority; };
            Assert.False(p.Compute());
            Assert.Equal(2, booleans);      // the failed run and one try, not more
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.StartsWith("RoundingNotClean: ", p.Error);
            Assert.Null(p.Result);
            Assert.Equal("Grid:-1", Names(p));
            Assert.Contains("it failed too (InconsistentOrientation: ", p.Report.Route[0].Change);
            Assert.EndsWith("so the first failure is the result", p.Report.Route[0].Change);
            Assert.Equal(0, p.Report.InputSnappedVertices);
            // the first failure is the one of the run without the steps
            int none = 0;
            var q = new SolidBooleanPipeline(inputs(), D) { InputRoles = solids, AutomaticSteps = Auto.None };
            q.ConfigureBoolean = op => { if (++none == 1) op.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority; };
            Assert.False(q.Compute());
            Assert.Equal(q.Error, p.Error);
            Assert.Equal(q.Status, p.Status);
        }

        [Fact]
        public void AFailureAfterTheSheetRetryAndTheFallback_IsNotRetriedAgain()
        {
            // the fragment's tool without a triangle is refused as a sheet with its rim inside, also on the grid (the one try); closed by the repair,
            // the rounding is refused, and the default does not try it again
            Func<List<DMesh3>> inputs = () => { var f = Fragment(); f[1].RemoveTriangle(HoleTriangle); return f; };
            var p = new SolidBooleanPipeline(inputs(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.False(p.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.Equal("SheetRole:1,Grid:-1,Fallback:-1,InputRepair:1", Names(p));
        }

        [Fact]
        public void ARimRefusalThatIsNotAnswered_LeavesTheOneTryForTheRoundingRefusalOfTheFallback()
        {
            // the fragment's tool without a triangle is open and routed as a sheet, refused with its rim inside: the rounding bit alone does not answer that, so
            // the run goes to the fallback, the repair closes the tool, the boolean refuses the rounding, and that is retried once on the grid (the try is not spent)
            Func<List<DMesh3>> inputs = () => { var f = Fragment(); f[1].RemoveTriangle(HoleTriangle); return f; };
            var none = new SolidBooleanPipeline(inputs(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, AutomaticSteps = Auto.None };
            Assert.False(none.Compute());
            Assert.Equal("SheetRole:1,Fallback:-1,InputRepair:1", Names(none));
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, none.BooleanStatus);
            var p = new SolidBooleanPipeline(inputs(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, AutomaticSteps = RoundingOnly };
            bool done = p.Compute();
            Assert.True(done, p.Status + " " + p.Error);
            AssertGridResult(p);
            // (the retry is the run of the fallback on the snapped inputs: it repairs the tool again)
            Assert.Equal("SheetRole:1,Fallback:-1,InputRepair:1,Grid:-1,InputRepair:1", Names(p));
            Assert.StartsWith("RoundingNotClean: ", p.Report.Route[3].Reason);
            Assert.Equal(Pow2(-18), p.Report.Route[3].Grid);
            Assert.True(Volume(p.Result) > 0 && Volume(p.Result) < Volume(Fragment()[0]));
            // the sheet bit does not answer it: the same input ends in the refusal
            var sheetOnly = new SolidBooleanPipeline(inputs(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, AutomaticSteps = SheetOnly };
            Assert.False(sheetOnly.Compute());
        }

        // ---- the guards apply on this path ----

        [Fact]
        public void ACellThatIsNotSmallAgainstAnInput_IsNotRetried_AndTheStepSaysWhich()
        {
            // a far closed component of the first input: the cell (2^12 ulps of 1e13: 8) is more than 1/64 of the size of the fragment
            Func<List<DMesh3>> inputs = () => {
                var f = Fragment();
                DMesh3 far = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(1e13, 0, 0), new Vector3d(1e13 + 1, 1, 1))) }.Generate().MakeDMesh();
                f[0] = Merge(f[0], far);
                return f;
            };
            int booleans = 0;
            var none = new SolidBooleanPipeline(inputs(), D) { AutomaticSteps = Auto.None };
            Assert.False(none.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, none.BooleanStatus);
            var p = new SolidBooleanPipeline(inputs(), D) { ConfigureBoolean = op => booleans++ };
            Assert.False(p.Compute());
            Assert.Equal(1, booleans);      // no run on the grid
            Assert.Equal("Grid:-1", Names(p));
            Assert.Equal(8.0, p.Report.Route[0].Grid);
            Assert.Contains("is more than 1/64 of the size of input ", p.Report.Route[0].Change);
            Assert.Contains("so the run is not done again and the first failure is the result", p.Report.Route[0].Change);
            Assert.Equal(none.Status, p.Status);
            Assert.Equal(none.Error, p.Error);
            Assert.Equal(0, p.Report.InputSnappedVertices);
        }

        static DMesh3 Merge(DMesh3 a, DMesh3 b)
        {
            var m = new DMesh3(a);
            var map = new int[b.MaxVertexID];
            foreach (int v in b.VertexIndices())
                map[v] = m.AppendVertex(b.GetVertex(v));
            foreach (int t in b.TriangleIndices()) {
                Index3i tv = b.GetTriangle(t);
                m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
            }
            return m;
        }

        [Fact]
        public void ARetryWithAnEmptyResultOfANonEmptyFirstInput_IsRejected_AsTheFirstFailure()
        {
            // on the retry the tool is swapped for a closed box that holds the whole first input: the Difference is empty although the first input is not
            int booleans = 0;
            DMesh3 big = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(-1e7, -1e7, -1e7), new Vector3d(1e7, 1e7, 1e7))) }.Generate().MakeDMesh();
            var p = new SolidBooleanPipeline(Fragment(), D) { ConfigureBoolean = op => { if (++booleans == 2) op.Meshes[1] = big; } };
            Assert.False(p.Compute());
            Assert.Equal(2, booleans);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.Null(p.Result);
            Assert.Equal("Grid:-1", Names(p));
            Assert.Contains("its result is empty although the first input is not, so it is rejected and the first failure is the result", p.Report.Route[0].Change);
        }

        // ---- a sheet that the caller stated: not by default, with Grid (stated roles behave as before) ----

        [Fact]
        public void AStatedSheetThatRefuses_IsNotRetriedByDefault_NorByTheRoundingBit_ButByGrid()
        {
            var plain = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.None };
            Assert.False(plain.Compute());
            Assert.Equal(Rim, plain.SheetReason);
            foreach (Auto mode in new[] { Default, SheetOnly, RoundingOnly }) {
                var p = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond(), AutomaticSteps = mode };
                Assert.False(p.Compute(), mode.ToString());
                Assert.Equal(plain.Status, p.Status);
                Assert.Equal(plain.Error, p.Error);
                Assert.Equal(Rim, p.SheetReason);
                Assert.Empty(p.Report.Route);
            }
            var g = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            Assert.True(g.Compute(), g.Error);
            Assert.Equal("Grid:-1", Names(g));
        }

        [Fact]
        public void TheRoundingBitAlone_DoesNotRetryARoutedSheet_AndTheSheetBitAlone_DoesNotRetryRounding()
        {
            // a routed tool whose rim is refused: the rounding bit alone leaves it to the fallback, as None does
            var none = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = Auto.None };
            var rounding = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = RoundingOnly };
            Assert.False(none.Compute());
            Assert.False(rounding.Compute());
            Assert.Equal(Names(none), Names(rounding));
            Assert.Equal(none.Error, rounding.Error);
            var sheet = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = SheetOnly };
            Assert.True(sheet.Compute(), sheet.Error);
            Assert.Equal("SheetRole:1,Grid:-1,SheetRole:1", Names(sheet));
            // the rounding bit alone retries the rounding refusal, the sheet bit alone does not
            var r = new SolidBooleanPipeline(Fragment(), D) { AutomaticSteps = RoundingOnly };
            Assert.True(r.Compute(), r.Error);
            Assert.Equal("Grid:-1", Names(r));
            var s = new SolidBooleanPipeline(Fragment(), D) { AutomaticSteps = SheetOnly };
            Assert.False(s.Compute());
            Assert.Empty(s.Report.Route);
            // Grid: everything
            var all = new SolidBooleanPipeline(Fragment(), D) { AutomaticSteps = Auto.Grid };
            Assert.True(all.Compute(), all.Error);
            Assert.Equal("", Invariance.Difference(r.Result, all.Result));
        }

        // ---- invariance, reuse, repeated runs ----

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var reference = new SolidBooleanPipeline(Fragment(), D);
            Assert.True(reference.Compute(), reference.Error);
            var p = new SolidBooleanPipeline(Fragment().Select(m => Invariance.Transformed(m, s)).ToList(), D);
            bool done = p.Compute();
            if (s.IsExact) {
                // a power-of-two scale: the cell is scaled with the data, and the result is the same, bit for bit
                Assert.True(done, s.Name + ": " + p.Error);
                AssertGridResult(p);
                Assert.Equal("Grid:-1", Names(p));
                Assert.Equal(reference.Report.Route[0].Grid * s.Scale, p.Report.Route[0].Grid);
                Assert.Equal(reference.Report.InputSnappedVertices, p.Report.InputSnappedVertices);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                return;
            }
            // the other frames round the coordinates to other doubles. Whether the first run is then refused (and the grid answers it) or the rounded data
            // runs clean at once depends on the last bit of the coordinates, so it is not pinned per frame: with the correctly rounded ToDouble (#1410)
            // the frame "scale 1e3", the only one that had needed the grid, runs clean at once. Every frame ends in a solid of the reference's volume,
            // on one of the two routes; the exact frames above are the ones that must take the grid
            Assert.True(done, s.Name + ": " + p.Status + " " + p.Error);
            AssertGridResult(p);
            Assert.Contains(Names(p), new[] { "", "Grid:-1" });
            double scale = s.Scale * s.Scale * s.Scale;
            Assert.True(Math.Abs(Volume(p.Result) - Volume(reference.Result) * scale) <= 1e-6 * Volume(reference.Result) * scale, s.Name + ": " + Volume(p.Result));
        }

        [Fact]
        public void ComputeTwice_OnOneInstance_GivesTheSameRouteAndResult_AndFollowsTheOption()
        {
            var p = new SolidBooleanPipeline(Fragment(), D);
            Assert.True(p.Compute(), p.Error);
            string route = Names(p), change = p.Report.Route[0].Change;
            DMesh3 first = p.Result;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(route, Names(p));
            Assert.Equal(change, p.Report.Route[0].Change);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            p.AutomaticSteps = Auto.None;
            Assert.False(p.Compute());
            Assert.Empty(p.Report.Route);
            Assert.Equal(0, p.Report.InputSnappedVertices);
            p.AutomaticSteps = Default;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
        }

        [Fact]
        public void RepeatedRuns_GiveTheSameResult()
        {
            DMesh3 first = null;
            for (int i = 0; i < 4; ++i) {
                var p = new SolidBooleanPipeline(Fragment(), D);
                Assert.True(p.Compute(), p.Error);
                if (first == null) first = p.Result;
                else Assert.Equal("", Invariance.Difference(first, p.Result));
            }
        }
    }
}
