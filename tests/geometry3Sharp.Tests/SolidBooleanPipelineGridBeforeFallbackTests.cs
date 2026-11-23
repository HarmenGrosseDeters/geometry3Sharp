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
        static DMesh3 Cutter(double t, Vector2d[] poly, int[][] bottom, Func<int, int> ulps)
        {
            var m = new DMesh3();
            int n = poly.Length;
            var low = new int[n]; var rim = new int[n];
            for (int i = 0; i < n; ++i) {
                low[i] = m.AppendVertex(new Vector3d(t + poly[i].x, t + poly[i].y, t + Top - Depth));
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
            Assert.Equal(Auto.GridBeforeFallback, p.AutomaticSteps);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertClean(p);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            double cell = SolidBooleanPipeline.DataGrid(p.Inputs);
            Assert.Equal(cell, p.Report.Route[1].Grid);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.Report.Route[1].SheetReason);
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

        [Fact]
        public void BothBitsTogether_AreDefined_AndOtherBitsAreRefused()
        {
            double area;
            var ok = new SolidBooleanPipeline(new List<DMesh3> { Block(0), Shape("box", 0, i => 0, out area) }, D) { AutomaticSteps = Auto.Grid | Auto.GridBeforeFallback };
            Assert.True(ok.Compute(), ok.Error);
            foreach (int v in new[] { 4, 6, 8, -1 }) {
                var p = new SolidBooleanPipeline(new List<DMesh3> { Block(0), Shape("box", 0, i => 0, out area) }, D) { AutomaticSteps = (Auto)v };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
                Assert.Contains("AutomaticSteps", p.Error);
            }
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
            var p = new SolidBooleanPipeline(Flush(t).Select(m => Invariance.Transformed(m, s)).ToList(), D);
            bool done = p.Compute();
            if (s.IsExact) {
                Assert.True(done, s.Name + ": " + p.Error);
                Assert.Equal(Names(reference), Names(p));
                Assert.Equal(reference.Report.Route[1].Grid * s.Scale, p.Report.Route[1].Grid);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                return;
            }
            // another frame: the rim lands elsewhere; the default either cuts (as given or on the grid) or refuses as before, never with another route
            if (done)
                Assert.Contains(Names(p), new[] { "SheetRole", "SheetRole,Grid,SheetRole" });
            else {
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(p));
            }
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
