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
    /// SolidBooleanPipeline.AutomaticSteps = GridBeforeFallback (the default): an open tool of a Difference that the pipeline routes to the sheet role by
    /// itself and that the sheet role refuses because its rim lies inside the workpiece (a rim meant to lie in a face, a few ulps inside it, far from the
    /// origin) is done once more on the grid taken from the data, before the fallback to solids. AutomaticSteps = None is the old behaviour.
    /// </summary>
    public class SolidBooleanPipelineGridBeforeFallbackTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.SheetRefusal Rim = ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece;
        const double Top = 5, Depth = 3;

        static string Names(SolidBooleanPipeline p) => string.Join(",", p.Report.Route.Select(s => s.Name));

        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        // the workpiece [0,10]^2 x [0,5], moved by t on every axis (t an integer: the box is on the grid cell)
        static DMesh3 Block(double t)
        {
            var lo = new Vector3d(t, t, t); var hi = new Vector3d(t + 10, t + 10, t + Top);
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
        }

        // z moved by ulps units in the last place downwards (upwards for a negative count; z is positive)
        static double Down(double z, int ulps) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(z) - ulps);

        // an open cutter: the prism over the counter-clockwise polygon, its rim at the top z = t + 5 pushed down by ulps(i) ulps per rim vertex, the
        // walls reaching down to z = t + 2, outward normals (the sheet removes what is inside); bottom: the given triangles of the polygon
        static DMesh3 Cutter(double t, Vector2d[] poly, int[][] bottom, Func<int, int> ulps, double depth = Depth)
        {
            var m = new DMesh3();
            int n = poly.Length;
            var low = new int[n]; var rim = new int[n];
            for (int i = 0; i < n; ++i) {
                low[i] = m.AppendVertex(new Vector3d(t + poly[i].x, t + poly[i].y, t + Top - depth));
                rim[i] = m.AppendVertex(new Vector3d(t + poly[i].x, t + poly[i].y, Down(t + Top, ulps(i))));
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(low[i], low[j], rim[j]);
                m.AppendTriangle(low[i], rim[j], rim[i]);
            }
            foreach (int[] f in bottom)
                m.AppendTriangle(low[f[0]], low[f[2]], low[f[1]]);
            return m;
        }

        static int[][] Fan(int n) => Enumerable.Range(1, n - 2).Select(i => new[] { 0, i, i + 1 }).ToArray();

        static readonly Vector2d[] BoxPoly = { new Vector2d(3, 3), new Vector2d(7, 3), new Vector2d(7, 7), new Vector2d(3, 7) };
        static readonly Vector2d[] LPoly = { new Vector2d(2, 2), new Vector2d(8, 2), new Vector2d(8, 5), new Vector2d(5, 5), new Vector2d(5, 8), new Vector2d(2, 8) };

        static Vector2d[] Circle(int n, double r) => Enumerable.Range(0, n).Select(i => new Vector2d(5 + r * Math.Cos(2 * Math.PI * i / n), 5 + r * Math.Sin(2 * Math.PI * i / n))).ToArray();

        static double PolyArea(Vector2d[] p)
        {
            double a = 0;
            for (int i = 0; i < p.Length; ++i) { Vector2d u = p[i], v = p[(i + 1) % p.Length]; a += u.x * v.y - v.x * u.y; }
            return a / 2;
        }

        static DMesh3 Shape(string name, double t, Func<int, int> ulps, out double area)
        {
            switch (name) {
                case "box": area = 16; return Cutter(t, BoxPoly, Fan(4), ulps);
                case "L":
                    area = 27;
                    return Cutter(t, LPoly, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 4 }, new[] { 0, 4, 5 } }, ulps);
                default: {
                    Vector2d[] c = Circle(32, 3);
                    area = PolyArea(c);
                    return Cutter(t, c, Fan(32), ulps);
                }
            }
        }

        static void AssertClean(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
        }

        // the volume of the result, measured near the origin (the formula cancels at large coordinates)
        static double Near(SolidBooleanPipeline p, double t)
        {
            var c = new DMesh3(p.Result);
            MeshTransforms.Translate(c, new Vector3d(-t, -t, -t));
            return Volume(c);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string shape in new[] { "box", "L", "cylinder" })
                foreach (double t in new[] { 1e5, 1.2e6, 1.2e7 })
                    yield return new object[] { shape, t };
        }

        // ---- the default: a flush rim a few ulps inside ----

        [Theory]
        [MemberData(nameof(Cases))]
        public void ByDefault_AFlushRimAFewUlpsInside_IsCutOnTheGrid(string shape, double t)
        {
            double area;
            DMesh3 tool = Shape(shape, t, i => 1 + (i % 3), out area);
            var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), tool }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertClean(p);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            double cell = SolidBooleanPipeline.DataGrid(p.Inputs);
            Assert.Equal(cell, p.Report.Route[1].Grid);
            Assert.Equal(Rim, p.Report.Route[1].SheetReason);       // the refusal that the step answers
            Assert.Empty(p.Report.RepairedInputs);
            // the walls of a polygon with corners off the cell move by at most half a cell per coordinate
            double tol = 1e-9 * 500 + (shape == "cylinder" ? 2 * cell * 20 * Depth : 0);
            double v = Near(p, t);
            Assert.True(Math.Abs(v - (10 * 10 * Top - area * Depth)) <= tol, shape + " " + t + ": " + v);

            // None: the old route and status
            var old = new SolidBooleanPipeline(new List<DMesh3> { Block(t), Shape(shape, t, i => 1 + (i % 3), out area) }, D) { AutomaticSteps = Auto.None };
            Assert.False(old.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, old.Status);
            Assert.Equal("SheetRole,Fallback,Refused", Names(old));
            Assert.Equal(Rim, old.Report.Route[1].SheetReason);
        }

        [Fact]
        public void ARimExactlyInTheFace_NeedsNoStep_AndCostsNothingExtra()
        {
            int booleans = 0;
            double area;
            var p = new SolidBooleanPipeline(new List<DMesh3> { Block(0), Shape("box", 0, i => 0, out area) }, D) { ConfigureBoolean = op => booleans++ };
            Assert.True(p.Compute(), p.Error);
            AssertClean(p);
            Assert.Equal("SheetRole", Names(p));
            Assert.Equal(1, booleans);
        }

        // ---- InputRepair = AutoRepair: the grid comes before the repair of the open tool; the rim is up to 3 ulps above or below the face ----

        static DMesh3 NoisyCylinder(double t, out double area)
        {
            var rnd = new Random(7);
            var noise = Enumerable.Range(0, 64).Select(i => rnd.Next(-3, 4)).ToArray();
            Vector2d[] c = Circle(64, 3);
            area = PolyArea(c);
            return Cutter(t, c, Fan(64), i => noise[i]);
        }

        [Fact]
        public void WithAutoRepair_ANoisyRimIsCutOnTheGrid_WhereTheRepairOfTheOpenToolIsNotClean()
        {
            const double t = 1.2e6;
            double area;
            var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), NoisyCylinder(t, out area) }, D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertClean(p);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            Assert.Empty(p.Report.RepairedInputs);
            double cell = p.Report.Route[1].Grid;
            Assert.True(Math.Abs(Near(p, t) - (500 - area * Depth)) <= 2 * cell * 20 * Depth + 1e-9 * 500);

            var old = new SolidBooleanPipeline(new List<DMesh3> { Block(t), NoisyCylinder(t, out area) }, D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, AutomaticSteps = Auto.None };
            bool done = old.Compute();
            Assert.Equal("SheetRole,Fallback,InputRepair", Names(old));
            // (what the feature buys: the repair of the open tool closes it, and the result has self-intersections)
            Assert.True(done);
            Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(old.Result));
        }

        // ---- a refusal that the grid cannot help ----

        static DMesh3 PlainBox(double lo, double hi)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(lo, lo, lo), new Vector3d(hi, hi, hi))) }.Generate().MakeDMesh();
        }

        // a patch at height z over [a,b]^2
        static DMesh3 Patch(double a, double b, double z)
        {
            var m = new DMesh3();
            int p = m.AppendVertex(new Vector3d(a, a, z)), q = m.AppendVertex(new Vector3d(b, a, z)), r = m.AppendVertex(new Vector3d(b, b, z)), s = m.AppendVertex(new Vector3d(a, b, z));
            m.AppendTriangle(p, q, r); m.AppendTriangle(p, r, s);
            return m;
        }

        // off the cell: a box [0.1, 4.7]^3 and a patch well inside it
        static List<DMesh3> PatchInside() => new List<DMesh3> { PlainBox(0.1, 4.7), Patch(1.1, 3, 2.5) };

        [Fact]
        public void ASheetThatEndsInsideTheWorkpiece_TriesTheGridOnce_AndHasTheOldResult()
        {
            int booleans = 0;
            var old = new SolidBooleanPipeline(PatchInside(), D) { AutomaticSteps = Auto.None, ConfigureBoolean = op => booleans++ };
            Assert.False(old.Compute());
            Assert.Equal(2, booleans);          // the sheet, the fallback
            booleans = 0;
            var p = new SolidBooleanPipeline(PatchInside(), D) { ConfigureBoolean = op => booleans++ };
            Assert.False(p.Compute());
            Assert.Equal(3, booleans);          // one more: the grid
            Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
            Assert.Contains("it failed too", p.Report.Route[1].Change);
            Assert.Equal(1, p.Report.Route.Count(s => s.Name == "Grid"));
            Assert.Equal(old.Status, p.Status);
            Assert.Equal(old.Error, p.Error);
            Assert.Equal(old.BooleanStatus, p.BooleanStatus);
            Assert.Equal(Rim, p.Report.Route[2].SheetReason);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.SheetReason);

            // with AutoRepair: the fallback goes on to the repair as before
            var repaired = new SolidBooleanPipeline(PatchInside(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            var repairedOld = new SolidBooleanPipeline(PatchInside(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, AutomaticSteps = Auto.None };
            Assert.Equal(repairedOld.Compute(), repaired.Compute());
            Assert.Equal(repairedOld.Status, repaired.Status);
            Assert.Equal(repairedOld.Error, repaired.Error);
            Assert.Equal(Names(repairedOld).Replace("SheetRole,", "SheetRole,Grid,"), Names(repaired));
        }

        [Fact]
        public void AToolWhoseInputsAreOnTheCellAlready_IsNotRunAgain_AndTheStepSaysSo()
        {
            int booleans = 0;
            // integer coordinates: on the cell
            var p = new SolidBooleanPipeline(new List<DMesh3> { PlainBox(0, 4), Patch(1, 3, 2) }, D) { ConfigureBoolean = op => booleans++ };
            Assert.False(p.Compute());
            Assert.Equal(2, booleans);
            Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
            Assert.Contains("already", p.Report.Route[1].Change);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
        }

        // ---- what the default does not touch ----

        [Fact]
        public void AStatedRole_KeepsItsRefusal_UnlessGridIsSet()
        {
            const double t = 1.2e6;
            double area;
            Roles sheetSecond = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
            int booleans = 0;
            var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area) }, D) { InputRoles = sheetSecond, ConfigureBoolean = op => booleans++ };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
            Assert.Equal(Rim, p.SheetReason);
            Assert.Empty(p.Report.Route);
            Assert.Equal(1, booleans);

            var g = new SolidBooleanPipeline(new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area) }, D) { InputRoles = sheetSecond, AutomaticSteps = Auto.Grid };
            Assert.True(g.Compute(), g.Error);
            Assert.Equal("Grid", Names(g));
        }

        [Fact]
        public void WithACallerGrid_TheStepIsNotTaken()
        {
            const double t = 1.2e6;
            double area;
            int booleans = 0;
            var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area) }, D) { InputGrid = Math.Pow(2, -60), ConfigureBoolean = op => booleans++ };
            Assert.False(p.Compute());
            Assert.Equal("SheetRole,Fallback,Refused", Names(p));
            Assert.Equal(2, booleans);
        }

        // ---- the cell and the retry must not destroy the inputs ----

        [Fact]
        public void TheCell_IgnoresAVertexThatNoTriangleUses()
        {
            const double t = 1.2e6;
            double area;
            DMesh3 plainTool = Shape("box", t, i => 2, out area);
            DMesh3 tool = Shape("box", t, i => 2, out area);
            tool.AppendVertex(new Vector3d(1e15, 0, 0));       // used by no triangle: it must not coarsen the cell to 512
            Assert.Equal(SolidBooleanPipeline.DataGrid(new[] { Block(t), plainTool }), SolidBooleanPipeline.DataGrid(new[] { Block(t), tool }));
            foreach (var repair in new[] { SolidBooleanPipeline.InputRepairModes.Refuse, SolidBooleanPipeline.InputRepairModes.AutoRepair }) {
                var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), tool }, D) { InputRepair = repair };
                Assert.True(p.Compute(), p.Status + " " + p.Error);
                AssertClean(p);
                Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
                Assert.Equal(SolidBooleanPipeline.DataGrid(new[] { Block(t), plainTool }), p.Report.Route[1].Grid);
                Assert.True(p.Result.TriangleCount > 0);
                Assert.True(Math.Abs(Near(p, t) - 452) <= 1e-6, Near(p, t).ToString());
            }
        }

        // a far second component of the tool: a triangle that triangles do use, so the cell is as coarse as its coordinates make it
        static DMesh3 WithFarTriangle(DMesh3 tool, double x)
        {
            int a = tool.AppendVertex(new Vector3d(x, 0, 0)), b = tool.AppendVertex(new Vector3d(x + 1, 0, 0)), c = tool.AppendVertex(new Vector3d(x, 1, 0));
            tool.AppendTriangle(a, b, c);
            return tool;
        }

        [Fact]
        public void ACellThatIsNotSmallAgainstTheFirstInput_IsNotTried_AndTheStepSaysSo()
        {
            const double t = 1.2e6;
            double area;
            foreach (var repair in new[] { SolidBooleanPipeline.InputRepairModes.Refuse, SolidBooleanPipeline.InputRepairModes.AutoRepair }) {
                int booleans = 0;
                var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), WithFarTriangle(Shape("box", t, i => 2, out area), 1e13) }, D) { InputRepair = repair, ConfigureBoolean = op => booleans++ };
                var old = new SolidBooleanPipeline(new List<DMesh3> { Block(t), WithFarTriangle(Shape("box", t, i => 2, out area), 1e13) }, D) { InputRepair = repair, AutomaticSteps = Auto.None };
                bool done = p.Compute(), oldDone = old.Compute();
                // the cell is 8 (2^12 ulps of 1e13) against a block of 10: the snap could change the volume by more than a quarter
                Assert.Equal(8.0, p.Report.Route[1].Grid);
                Assert.Equal("SheetRole,Grid,Fallback" + (repair == SolidBooleanPipeline.InputRepairModes.Refuse ? ",Refused" : ",InputRepair"), Names(p));
                Assert.Contains("so the run is not done again and the first failure is the result", p.Report.Route[1].Change);
                Assert.Contains("is more than 1/64 of the size of input", p.Report.Route[1].Change);
                // (no run on the grid: the sheet and the fallback, and the old result)
                Assert.Equal(repair == SolidBooleanPipeline.InputRepairModes.Refuse ? 2 : 3, booleans);       // (AutoRepair: the repaired tool is a third boolean)
                Assert.Equal(oldDone, done);
                Assert.Equal(old.Status, p.Status);
                Assert.Equal(old.Error, p.Error);
            }
        }

        [Fact]
        public void ARetryWithAnEmptyResultOfANonEmptyFirstInput_IsRejected()
        {
            // in a run: the rim is refused as inside the block; on the retry the tool is swapped for a sheet far from the block, with UncutSide Inside
            // (the part that no sheet touches is removed), so the boolean of the retry gives an empty result of a block that is not empty
            const double t = 1.2e6;
            double area;
            int booleans = 0;
            Func<List<DMesh3>> inputs = () => new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area) };
            var d = new SolidBooleanPipeline(inputs(), D) { UncutSide = ExactMeshBoolean.SheetUncutSides.Inside, ConfigureBoolean = op => { if (++booleans == 2) op.Meshes[1] = Patch(1e6, 1e6 + 1, 1e6 + 2); } };
            bool done = d.Compute();
            Assert.Equal(3, booleans);
            Assert.False(done, "the retry with an empty result is rejected");
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, d.Status);
            Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(d));
            Assert.True(d.Report.Route[1].Change.Contains("its result is empty although the first input is not"), d.Report.Route[1].Change);
            Assert.Null(d.Result);
        }

        [Fact]
        public void TheGridStep_ReportsTheLargestRelativeSizeChange_OfTheInputThatChangesMost()
        {
            // the far block of 0.1 and the sheet 1e-6 inside its faces: both change a little by the snap (cell 2^-18); the step names the larger change
            const double gap = 1e-6;
            double cell = Math.Pow(2, -18);
            Func<double, double> snap = x => Math.Round(x / cell) * cell;
            double w0x = (X0 + 0.1) - X0, w0y = (Y0 + 0.1) - Y0, w0z = (Z0 + 0.05) - Z0;
            double block = (snap(X0 + 0.1) - snap(X0)) * (snap(Y0 + 0.1) - snap(Y0)) * (snap(Z0 + 0.05) - snap(Z0)) / (w0x * w0y * w0z) - 1;
            double sheet = (snap(X0 + 0.1 - gap) - snap(X0 + gap)) * (snap(Y0 + 0.1 - gap) - snap(Y0 + gap)) / (((X0 + 0.1 - gap) - (X0 + gap)) * ((Y0 + 0.1 - gap) - (Y0 + gap))) - 1;
            Assert.NotEqual(0.0, block);
            var p = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), FarSheet(gap) }, D);
            Assert.True(p.Compute(), p.Error);
            var m = System.Text.RegularExpressions.Regex.Match(p.Report.Route[1].Change, @"and changed the (\w+) of input (\d+) by (\S+) of it \(the largest relative change of an input\)");
            Assert.True(m.Success, p.Report.Route[1].Change);
            bool blockLarger = Math.Abs(block) >= Math.Abs(sheet);
            Assert.Equal(blockLarger ? "volume" : "area", m.Groups[1].Value);
            Assert.Equal(blockLarger ? "0" : "1", m.Groups[2].Value);
            double reported = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), expected = blockLarger ? block : sheet;
            Assert.True(Math.Abs(reported - expected) <= 1e-3 * Math.Abs(expected), reported + " against " + expected);
        }

        // ---- every input is guarded, not only the first ----

        // a pocket tool 0.03 wide and 0.03 deep with its rim 2 ulps under the top face z = 5 of the block [0,10]^2 x [0,5]
        static DMesh3 SmallPocket()
        {
            Vector2d[] poly = { new Vector2d(5, 5), new Vector2d(5.03, 5), new Vector2d(5.03, 5.03), new Vector2d(5, 5.03) };
            return Cutter(0, poly, Fan(4), i => 2, 0.03);
        }

        static DMesh3 ClosedUnitBoxAt(double x) => new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x, 0, 0), new Vector3d(x + 1, 1, 1))) }.Generate().MakeDMesh();

        [Fact]
        public void ASmallToolIsGuarded_ByTheCellOfAFarInput_ButNotWithoutIt()
        {
            double pocket = 0.03 * 0.03 * 0.03;
            var alone = new SolidBooleanPipeline(new List<DMesh3> { Block(0), SmallPocket() }, D);
            Assert.True(alone.Compute(), alone.Error);
            AssertClean(alone);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(alone));
            Assert.True(Math.Abs(Volume(alone.Result) - (500 - pocket)) <= 1e-9, Volume(alone.Result).ToString());
            foreach (double x in new[] { 3e10, 1e10 }) {
                // the far closed box moves the cell to 0.0156 or 0.0078: more than 1/64 of the pocket's width (0.075), so no retry; the pocket would grow by 13 %
                var p = new SolidBooleanPipeline(new List<DMesh3> { Block(0), SmallPocket(), ClosedUnitBoxAt(x) }, D);
                Assert.False(p.Compute(), x + " " + p.Status);
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
                Assert.Contains("is more than 1/64 of the size of input 1 (width 2A/L = ", p.Report.Route[1].Change);
                Assert.Equal(Rim, p.Report.Route[1].SheetReason);
            }
        }

        // a plate of 1 x 1 x thickness at coordinates about t, a horizontal sheet 1e-6 inside its side faces
        static List<DMesh3> Plate(double t, double thickness)
        {
            var block = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(t, t, t), new Vector3d(t + 1, t + 1, t + thickness))) }.Generate().MakeDMesh();
            var sheet = new DMesh3();
            double z = t + thickness / 2, a = t + 1e-6, b = t + 1 - 1e-6;
            int p = sheet.AppendVertex(new Vector3d(a, a, z)), q = sheet.AppendVertex(new Vector3d(b, a, z)), r = sheet.AppendVertex(new Vector3d(b, b, z)), s = sheet.AppendVertex(new Vector3d(a, b, z));
            sheet.AppendTriangle(p, q, r); sheet.AppendTriangle(p, r, s);
            return new List<DMesh3> { block, sheet };
        }

        [Theory]
        [InlineData(5.7e6, 1e-3, true)]           // cell 3.8e-6: 3.8e-3 of the thickness, the intended plate
        [InlineData(1.2e7, 1e-3, true)]           // cell 7.6e-6: 7.6e-3
        [InlineData(5.7e6, 3e-4, true)]           // just under 1/64 (1.2e-2): 3.8e-6 / 3e-4
        [InlineData(5.7e6, 2e-4, false)]          // just over: 3.8e-6 / 2e-4 = 1.9e-2
        public void AThinPlate_IsCutOnTheGrid_UntilTheCellIsMoreThanAShareOfItsThickness(double t, double thickness, bool retried)
        {
            var p = new SolidBooleanPipeline(Plate(t, thickness), D);
            bool done = p.Compute();
            if (retried) {
                Assert.True(done, p.Status + " " + p.Error);
                AssertClean(p);
                Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
                var c = new DMesh3(p.Result);
                MeshTransforms.Translate(c, new Vector3d(-t, -t, -t));
                Assert.True(Math.Abs(Volume(c) - thickness / 2) <= 4 * p.Report.Route[1].Grid, Volume(c).ToString());
            } else {
                Assert.False(done);
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
                Assert.Contains("is more than 1/64 of the size of input 0 (thickness 2V/A = ", p.Report.Route[1].Change);
            }
        }

        // ---- a refusal of a sheet that the caller stated is not answered by the default ----

        [Fact]
        public void AStatedSheetThatRefuses_NextToARoutedTool_IsNotSnapped()
        {
            const double t = 1.2e6;
            double area;
            Func<List<DMesh3>> inputs = () => {
                var wall = new DMesh3();       // an open wall across the block, no role: it is routed as a sheet
                int a = wall.AppendVertex(new Vector3d(t + 5, t - 1, t - 1)), b = wall.AppendVertex(new Vector3d(t + 5, t + 11, t - 1));
                int c = wall.AppendVertex(new Vector3d(t + 5, t + 11, t + 6)), d = wall.AppendVertex(new Vector3d(t + 5, t - 1, t + 6));
                wall.AppendTriangle(a, b, c); wall.AppendTriangle(a, c, d);
                return new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area), wall };
            };
            Roles roles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
            var alone = new SolidBooleanPipeline(new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area) }, D) { InputRoles = roles };
            Assert.False(alone.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, alone.Status);

            var p = new SolidBooleanPipeline(inputs(), D) { InputRoles = roles };
            var old = new SolidBooleanPipeline(inputs(), D) { InputRoles = roles, AutomaticSteps = Auto.None };
            bool done = p.Compute(), oldDone = old.Compute();
            Assert.False(done);
            Assert.Equal(oldDone, done);
            Assert.Equal(old.Status, p.Status);
            Assert.Equal(old.Error, p.Error);
            Assert.Equal(Names(old), Names(p));
            Assert.DoesNotContain("Grid", Names(p));
            // Grid retries a stated sheet, as before
            var g = new SolidBooleanPipeline(inputs(), D) { InputRoles = roles, AutomaticSteps = Auto.Grid };
            g.Compute();
            Assert.Contains("Grid", Names(g));
        }

        // ---- the capture distance is about one cell: gaps and skins by design are closed too (pinned, with the old behaviour next to it) ----

        // an open box (outward normals) with its rim at rimZ, walls from lo.z up
        static DMesh3 OpenBox(Vector3d lo, Vector3d hi, double rimZ)
        {
            var m = new DMesh3();
            Vector2d[] c = { new Vector2d(lo.x, lo.y), new Vector2d(hi.x, lo.y), new Vector2d(hi.x, hi.y), new Vector2d(lo.x, hi.y) };
            var low = new int[4]; var rim = new int[4];
            for (int i = 0; i < 4; ++i) {
                low[i] = m.AppendVertex(new Vector3d(c[i].x, c[i].y, lo.z));
                rim[i] = m.AppendVertex(new Vector3d(c[i].x, c[i].y, rimZ));
            }
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(low[i], low[j], rim[j]); m.AppendTriangle(low[i], rim[j], rim[i]);
            }
            m.AppendTriangle(low[0], low[2], low[1]); m.AppendTriangle(low[0], low[3], low[2]);
            return m;
        }

        // a block of 0.1 x 0.1 x 0.05 far from the origin (coordinates about 6e5, 5.7e6, 1e2: the cell is 2^-18 = 3.8e-6)
        const double X0 = 600000, Y0 = 5700000, Z0 = 100;

        static DMesh3 FarBlock() => new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(X0, Y0, Z0), new Vector3d(X0 + 0.1, Y0 + 0.1, Z0 + 0.05))) }.Generate().MakeDMesh();

        static double FarVolume(DMesh3 m)
        {
            var c = new DMesh3(m);
            MeshTransforms.Translate(c, new Vector3d(-X0, -Y0, -Z0));
            return Volume(c);
        }

        static DMesh3 FarSheet(double gap)
        {
            var m = new DMesh3();
            double z = Z0 + 0.025, x0 = X0 + gap, x1 = X0 + 0.1 - gap, y0 = Y0 + gap, y1 = Y0 + 0.1 - gap;
            int a = m.AppendVertex(new Vector3d(x0, y0, z)), b = m.AppendVertex(new Vector3d(x1, y0, z)), c = m.AppendVertex(new Vector3d(x1, y1, z)), d = m.AppendVertex(new Vector3d(x0, y1, z));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            return m;
        }

        [Theory]
        [InlineData(1e-6)]
        [InlineData(1.5e-6)]
        public void ASheetThatStopsShortOfTheFacesByDesign_IsClosedToThem_ByTheDefault(double gap)
        {
            // the sheet ends 1 or 1.5 micro-units inside the block's faces (the cell is 3.8e-6): the default takes it as meant to be flush and cuts the
            // block in half; None with Refuse refuses, None with AutoRepair closes the sheet and cuts nothing
            var p = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), FarSheet(gap) }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertClean(p);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            Assert.Equal(Math.Pow(2, -18), p.Report.Route[1].Grid);
            Assert.True(Math.Abs(FarVolume(p.Result) - 2.5e-4) < 1e-6, FarVolume(p.Result).ToString());

            var refuse = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), FarSheet(gap) }, D) { AutomaticSteps = Auto.None };
            Assert.False(refuse.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, refuse.Status);
            // (AutoRepair closed the flat sheet with a fill that lies on it, which cut nothing: a coincident-face self-intersection, so since
            // owner 2026-11-22 the repaired copy is refused, RepairSelfIntersectionTests)
            var repair = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), FarSheet(gap) }, D) { AutomaticSteps = Auto.None, InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.False(repair.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, repair.Status);
            Assert.Contains("MeshAutoRepair closed it into a self-intersecting mesh", repair.Error);
            Assert.Equal("SheetRole,Fallback,InputRepair,Refused", Names(repair));
        }

        static int Shells(DMesh3 m) { var c = new MeshConnectedComponents(m); c.FindConnectedT(); return c.Count; }

        [Fact]
        public void ABucketThatLeavesASkinUnderTheTopFace_GivesAnOpenPocket_ByTheDefault_AndACavityWithTheRepair()
        {
            // the bucket's rim is 1e-6 under the top face (the cell is 3.8e-6): by default the rim lands in the face and the pocket is open (one shell);
            // None with AutoRepair closes the bucket with a lid and leaves an enclosed cavity under the skin (two shells)
            Func<DMesh3> tool = () => OpenBox(new Vector3d(X0 + 0.03, Y0 + 0.03, Z0 + 0.01), new Vector3d(X0 + 0.07, Y0 + 0.07, 0), Z0 + 0.05 - 1e-6);
            var p = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), tool() }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertClean(p);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            Assert.Equal(1, Shells(p.Result));
            double pocket = 0.04 * 0.04 * 0.04;
            Assert.True(Math.Abs(FarVolume(p.Result) - (5e-4 - pocket)) < 1e-6, FarVolume(p.Result).ToString());

            var refuse = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), tool() }, D) { AutomaticSteps = Auto.None };
            Assert.False(refuse.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, refuse.Status);
            var repair = new SolidBooleanPipeline(new List<DMesh3> { FarBlock(), tool() }, D) { AutomaticSteps = Auto.None, InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.True(repair.Compute(), repair.Error);
            Assert.Equal(2, Shells(repair.Result));
        }

        // ---- the rim of a tool in a rotated frame ----

        // an open frustum: rim polygon at the top (z = t + 5 pushed down by ulps(i) ulps), bottom polygon at t + 2, both counter-clockwise, outward normals
        static DMesh3 Frustum(double t, Vector2d[] rimXY, Vector2d[] lowXY, Func<int, int> ulps)
        {
            var m = new DMesh3();
            int n = rimXY.Length;
            var low = new int[n]; var rim = new int[n];
            for (int i = 0; i < n; ++i) {
                low[i] = m.AppendVertex(new Vector3d(t + lowXY[i].x, t + lowXY[i].y, t + Top - Depth));
                rim[i] = m.AppendVertex(new Vector3d(t + rimXY[i].x, t + rimXY[i].y, Down(t + Top, ulps(i))));
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(low[i], low[j], rim[j]);
                m.AppendTriangle(low[i], rim[j], rim[i]);
            }
            foreach (int[] f in Fan(n))
                m.AppendTriangle(low[f[0]], low[f[2]], low[f[1]]);
            return m;
        }

        static readonly Vector2d[] Corners = { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) };
        static readonly Vector2d[] Midpoints = { new Vector2d(5, 0), new Vector2d(10, 5), new Vector2d(5, 10), new Vector2d(0, 5) };
        static readonly Vector2d[] Inner = { new Vector2d(2, 2), new Vector2d(8, 2), new Vector2d(8, 8), new Vector2d(2, 8) };
        static readonly Vector2d[] InnerSmall = { new Vector2d(4, 4), new Vector2d(6, 4), new Vector2d(6, 6), new Vector2d(4, 6) };

        static readonly Vector2d[] CornersAndEdgePoint = { new Vector2d(0, 0), new Vector2d(5, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) };
        static readonly Vector2d[] InnerFive = { new Vector2d(2, 2), new Vector2d(5, 2), new Vector2d(8, 2), new Vector2d(8, 8), new Vector2d(2, 8) };

        // what the default does with a rim in a rotated frame (measured, then pinned): the snap rounds each coordinate on its own
        [Theory]
        [InlineData("rotated", "corners", true)]                    // every rim vertex is a vertex of the workpiece: the snap makes them identical
        [InlineData("rotated", "midpoints", false)]                 // rim vertices on edges of the workpiece, rim edges across the interior of the slanted top face
        [InlineData("rotated", "cornersAndEdgePoint", false)]       // as corners, with one more rim vertex on an edge of the workpiece (not at a vertex)
        [InlineData("identity", "midpoints", true)]                 // the same rim in the frame of the axes: the top face is a plane of the grid
        public void InARotatedFrame_TheDefaultCutsWhereTheRimVerticesAreVerticesOfTheWorkpiece_AndKeepsTheRefusalOtherwise(string frame, string rim, bool cuts)
        {
            const double t = 1.2e5;
            Similarity s = Similarity.ByName(frame);
            DMesh3 tool = rim == "corners" ? Frustum(t, Corners, Inner, i => 2) : rim == "midpoints" ? Frustum(t, Midpoints, InnerSmall, i => 2) : Frustum(t, CornersAndEdgePoint, InnerFive, i => 2);
            Func<SolidBooleanPipeline.AutomaticStepModes, SolidBooleanPipeline> make = mode => new SolidBooleanPipeline(new List<DMesh3> { Invariance.Transformed(Block(t), s), Invariance.Transformed(tool, s) }, D) { AutomaticSteps = mode };
            SolidBooleanPipeline p = make((Auto)2), none = make(Auto.None);       // (2: the default, GridBeforeFallback, by value so that this test compiles against the base library, where the default is None)
            bool done = p.Compute();
            Assert.False(none.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, none.Status);
            if (cuts) {
                Assert.True(done, p.Status + " " + p.Error);
                AssertClean(p);
                Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            } else {
                Assert.False(done);
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
                Assert.Contains("it failed too (BooleanFailed: SheetRefused: ", p.Report.Route[1].Change);
            }
        }

        // ---- the guard measures what the snap could distort ----

        [Fact]
        public void AClosedInputOfVolumeZero_AdoubledSheet_DoesNotStopTheStep()
        {
            const double t = 1.2e6;
            double area;
            var doubled = new DMesh3();       // a sheet and the same sheet reversed: winding-closed, volume 0, beside the block
            int a = doubled.AppendVertex(new Vector3d(t + 20, t, t)), b = doubled.AppendVertex(new Vector3d(t + 21, t, t)), c = doubled.AppendVertex(new Vector3d(t + 21, t + 1, t)), d = doubled.AppendVertex(new Vector3d(t + 20, t + 1, t));
            doubled.AppendTriangle(a, b, c); doubled.AppendTriangle(a, c, d);
            int a2 = doubled.AppendVertex(doubled.GetVertex(a)), b2 = doubled.AppendVertex(doubled.GetVertex(b)), c2 = doubled.AppendVertex(doubled.GetVertex(c)), d2 = doubled.AppendVertex(doubled.GetVertex(d));
            doubled.AppendTriangle(a2, c2, b2); doubled.AppendTriangle(a2, d2, c2);
            Assert.True(ExactMeshBoolean.IsWindingClosed(doubled));
            var p = new SolidBooleanPipeline(new List<DMesh3> { Block(t), Shape("box", t, i => 2, out area), doubled }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error + " " + (p.Report.Route.Count > 1 ? p.Report.Route[1].Change : ""));
            AssertClean(p);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
        }

        [Fact]
        public void TheBoundaryLength_CountsEdgesByPosition_AndTreatsMinusZeroAsZero()
        {
            // two triangles of a unit square that share the diagonal by position, with separate vertices, one of them at -0.0
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(1, 1, 0));
            m.AppendTriangle(a, b, c);
            int a2 = m.AppendVertex(new Vector3d(-0.0, -0.0, -0.0)), c2 = m.AppendVertex(new Vector3d(1, 1, -0.0)), d = m.AppendVertex(new Vector3d(-0.0, 1, 0));
            m.AppendTriangle(a2, c2, d);
            Assert.Equal(4.0, SolidBooleanPipeline.BoundaryLength(m));
            // a lone triangle: its three edges
            var t = new DMesh3();
            t.AppendTriangle(t.AppendVertex(new Vector3d(0, 0, 0)), t.AppendVertex(new Vector3d(3, 0, 0)), t.AppendVertex(new Vector3d(0, 4, 0)));
            Assert.Equal(12.0, SolidBooleanPipeline.BoundaryLength(t));
        }

        static DMesh3 Moved(DMesh3 m, Similarity s, double t)
        {
            DMesh3 back = Invariance.MappedBack(m, s);
            MeshTransforms.Translate(back, new Vector3d(-t, -t, -t));
            return back;
        }

        // ---- invariance, reuse, repeated runs (the pipeline has no parallel path of its own) ----

        static List<DMesh3> Flush(double t) { double area; return new List<DMesh3> { Block(t), Shape("box", t, i => 1 + i % 3, out area) }; }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            const double t = 1.2e5;
            var reference = new SolidBooleanPipeline(Flush(t), D);
            Assert.True(reference.Compute(), reference.Error);
            AssertClean(reference);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(reference));
            var p = new SolidBooleanPipeline(Flush(t).Select(m => Invariance.Transformed(m, s)).ToList(), D);
            bool done = p.Compute();
            if (!s.IsRotated) {
                // every frame without a rotation keeps the faces parallel to the axes, so the snap puts the rim in the face: the run cuts on the route of the
                // reference, with its volume (measured near the origin, in the units of the reference, to the half cell that the snap moves each face by)
                Assert.True(done, s.Name + ": " + p.Status + " " + p.Error + " " + (p.Report.Route.Count > 1 ? p.Report.Route[1].Change : ""));
                AssertClean(p);
                Assert.Equal(Names(reference), Names(p));
                DMesh3 back = Invariance.MappedBack(p.Result, s);
                MeshTransforms.Translate(back, new Vector3d(-t, -t, -t));
                double cell = p.Report.Route[1].Grid / s.Scale;
                Assert.True(Math.Abs(Volume(back) - 452) <= 2 * cell * 600 + 1e-12 * 452, s.Name + ": " + Volume(back));
                if (s.IsExact) {
                    Assert.Equal(reference.Report.Route[1].Grid * s.Scale, p.Report.Route[1].Grid);
                    Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                }
                return;
            }
            // a rotation leaves no face parallel to an axis: the snap moves the rim and the face to different places, so the rim stays inside the
            // workpiece and the grid try fails, and the first failure is the result (the sheet is not closed to the face, and the fallback refuses an open tool)
            Assert.False(done, s.Name);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
            Assert.Contains("it failed too (BooleanFailed: SheetRefused: ", p.Report.Route[1].Change);
            Assert.Contains("lies inside the workpiece", p.Report.Route[1].Change);       // (the retry is refused for the same reason: the rim is still inside)
            Assert.Equal(Rim, p.Report.Route[2].SheetReason);
            Assert.Null(p.Result);
        }

        [Fact]
        public void ComputeTwice_OnOneInstance_GivesTheSameRouteAndResult()
        {
            var p = new SolidBooleanPipeline(Flush(1.2e6), D);
            Assert.True(p.Compute(), p.Error);
            string route = Names(p);
            DMesh3 first = p.Result;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(route, Names(p));
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Equal(3, p.Report.Route.Count);
        }

        [Fact]
        public void RepeatedRuns_GiveTheSameResult()
        {
            DMesh3 first = null;
            for (int i = 0; i < 4; ++i) {
                var p = new SolidBooleanPipeline(Flush(1.2e6), D);
                Assert.True(p.Compute(), p.Error);
                if (first == null) first = p.Result;
                else Assert.Equal("", Invariance.Difference(first, p.Result));
            }
        }
    }
}
