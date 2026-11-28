using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;
using V = gs.SolidBooleanPipeline.VerificationModes;
using Status = gs.SolidBooleanPipeline.PipelineStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.Verification: the opt-in checks of a finished result. NoInventedSurface (the result lies on the inputs as the caller
    /// passed them, so a repair that made surface up is refused) and RoundingVolume (the volume that the rounding repair moved stays within a
    /// limit, with a floor for slivers). Procedural inputs only.
    /// </summary>
    public class SolidBooleanPipelineVerificationTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference, U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection;
        const V Both = V.NoInventedSurface | V.RoundingVolume;
        const double Inf = double.PositiveInfinity;
        static readonly SolidBooleanPipeline.InputRepairModes Repair = SolidBooleanPipeline.InputRepairModes.AutoRepair;

        // ---- the inputs ----

        static DMesh3 Box(Vector3d lo, Vector3d hi) => RoundingThinVolumeTests.Box(lo, hi);
        static DMesh3 Cube(double size) => Box(Vector3d.Zero, new Vector3d(size, size, size));
        static DMesh3 CubeAt(double lo, double hi) => Box(new Vector3d(lo, lo, lo), new Vector3d(hi, hi, hi));

        // the box [x0, x1]^2 x [z0, z1] without its top face: an open bucket with shared vertices
        static DMesh3 Bucket(double x0, double x1, double z0, double z1)
        {
            var m = new DMesh3();
            var v = new int[8];
            for (int k = 0; k < 8; ++k)
                v[k] = m.AppendVertex(new Vector3d((k & 1) == 0 ? x0 : x1, (k & 2) == 0 ? x0 : x1, (k & 4) == 0 ? z0 : z1));
            Action<int, int, int, int> quad = (a, b, c, d) => { m.AppendTriangle(v[a], v[b], v[c]); m.AppendTriangle(v[a], v[c], v[d]); };
            quad(0, 2, 3, 1); quad(0, 1, 5, 4); quad(2, 6, 7, 3); quad(0, 4, 6, 2); quad(1, 3, 7, 5);
            return m;
        }

        // a square sheet at height z over [x0, x1]^2, normal up
        static DMesh3 Sheet(double z, double x0, double x1)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(x0, x0, z)), b = m.AppendVertex(new Vector3d(x1, x0, z)), c = m.AppendVertex(new Vector3d(x1, x1, z)), d = m.AppendVertex(new Vector3d(x0, x1, z));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            return m;
        }

        static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, m.GetVertex(v) + c);
            return m;
        }

        // the cube of 4 minus an open bucket inside it: the sheet role refuses (the rim is inside the workpiece), the repair closes the bucket with a lid that
        // lies on no input: 4 of the 120 of area of the result (cube 96, cavity 24)
        static List<DMesh3> CubeAndBucket() => new List<DMesh3> { Cube(4), Bucket(1, 3, 1, 3) };

        static SolidBooleanPipeline Invented(V mode = V.NoInventedSurface, double limit = 0) =>
            new SolidBooleanPipeline(CubeAndBucket(), D) { InputRepair = Repair, Verification = mode, MaxInventedSurfaceShare = limit };

        // a block of 0.02 x 0.02 x 0.01 at 6e5, 5.7e6, 1e2 (the Grid step's cell is 2^-18 = 3.8e-6) whose faces lie a fraction f of the cell above grid
        // points, and a sheet that ends gap inside its side faces: the sheet role refuses, the Grid step snaps (the faces move by up to f cell = 1.4e-6)
        const double X0 = 600000, Y0 = 5700000, Z0 = 100, Cell = 1.0 / (1 << 18), Frac = 0.38, Width = 0.02;
        static DMesh3 FarBlock()
        {
            double ax = X0 + (1000 + Frac) * Cell, ay = Y0 + (2000 + Frac) * Cell, az = Z0 + (3000 + Frac) * Cell;
            return Box(new Vector3d(ax, ay, az), new Vector3d(ax + Width, ay + Width, az + Width / 2));
        }
        static DMesh3 FarSheet()
        {
            double ax = X0 + (1000 + Frac) * Cell, ay = Y0 + (2000 + Frac) * Cell, az = Z0 + (3000 + Frac) * Cell, gap = 0.1 * Cell;
            return Sheet(az + Width / 4, 0, 1).Also(m => {
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    m.SetVertex(v, new Vector3d(p.x == 0 ? ax + gap : ax + Width - gap, p.y == 0 ? ay + gap : ay + Width - gap, p.z));
                }
            });
        }
        static List<DMesh3> FarCut() => new List<DMesh3> { FarBlock(), FarSheet() };

        static List<DMesh3> Plates() => RoundingThinVolumeTests.ThinPlates(RoundingThinVolumeTests.TA, RoundingThinVolumeTests.E, RoundingThinVolumeTests.Dz);

        // a box under a generic rotation minus its copy 4 ulps away: the exact result is slivers below the resolution of the coordinates, which the
        // rounding repair removes (RoundingVolumeReportTests.RemovedComponents_AreInTheDifference)
        static List<DMesh3> Slivers()
        {
            DMesh3 a = Box(new Vector3d(-0.5, -0.5, -0.5), new Vector3d(0.5, 0.5, 0.5));
            MeshTransforms.Rotate(a, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
            double largest = a.Vertices().Max(v => v.MaxAbs);
            double ulp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(largest) + 1) - largest;
            var b = new DMesh3(a);
            MeshTransforms.Translate(b, 4 * ulp * new Vector3d(0.61, -0.37, 0.7).Normalized);
            return new List<DMesh3> { a, b };
        }

        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static void AssertSolid(DMesh3 r)
        {
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
        }

        static string Names(SolidBooleanPipeline p) => string.Join(",", p.Report.Route.Select(s => s.Name));
        static SolidBooleanRouteStep VerificationStep(SolidBooleanPipeline p) => p.Report.Route.Last(s => s.Name == "Verification");

        static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        // ---- Verification = None: the run is the run without the option ----

        [Fact]
        public void WithoutTheMode_TheRunIsTheOldRun_AndNothingIsMeasured()
        {
            var cases = new List<Func<SolidBooleanPipeline>> {
                () => new SolidBooleanPipeline(CubeAndBucket(), D) { InputRepair = Repair },
                () => new SolidBooleanPipeline(Plates(), U),
                () => new SolidBooleanPipeline(FarCut(), D),
                () => new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), D),
            };
            foreach (var make in cases) {
                var plain = make();
                bool plainDone = plain.Compute();
                // (the limits are set, even to values that would fail every result: they act only with the mode)
                var none = make();
                none.Verification = V.None; none.MaxInventedSurfaceShare = 0; none.MaxRoundingVolumeChange = 0;
                Assert.Equal(plainDone, none.Compute());
                Assert.Equal(plain.Status, none.Status);
                Assert.Equal(plain.Error, none.Error);
                Assert.NotEqual(Status.VerificationFailed, none.Status);
                Assert.Equal(Names(plain), Names(none));
                Assert.DoesNotContain("Verification", Names(none));
                Assert.Equal(plain.Result == null, none.Result == null);
                if (plain.Result != null)
                    Assert.Equal("", Invariance.Difference(plain.Result, none.Result));
                // no tree is built and nothing is measured: the report fields of the checks stay at their defaults
                Assert.Equal(0.0, none.Report.InventedSurfaceShare);
                Assert.Equal(0.0, none.Report.InventedSurfaceMaxDistance);
                Assert.Equal(0.0, none.Report.InventedSurfaceTolerance);
                Assert.Equal(0.0, none.Report.RoundingVolumeChange);
                Assert.Equal(0L, none.Report.VerificationMilliseconds);
                Assert.Equal(plain.Report.RoundingVolumeBefore, none.Report.RoundingVolumeBefore);
                Assert.Equal(plain.Report.RoundingVolumeAfter, none.Report.RoundingVolumeAfter);
            }
        }

        [Fact]
        public void ARunThatPasses_HasTheResultOfTheRunWithoutTheMode_BitForBit()
        {
            foreach (V mode in new[] { V.NoInventedSurface, V.RoundingVolume, Both }) {
                var plain = new SolidBooleanPipeline(Plates(), U);
                Assert.True(plain.Compute(), plain.Error);
                var p = new SolidBooleanPipeline(Plates(), U) { Verification = mode, MaxInventedSurfaceShare = Inf, MaxRoundingVolumeChange = Inf };
                Assert.True(p.Compute(), p.Error);
                Assert.Equal("", Invariance.Difference(plain.Result, p.Result));
                Assert.Equal(plain.Report.BooleanTriangles, p.Report.BooleanTriangles);
                Assert.Equal(plain.Report.RoundingVolumeAfter, p.Report.RoundingVolumeAfter);
                Assert.Equal(Names(plain) == "" ? "Verification" : Names(plain) + ",Verification", Names(p));
            }
        }

        // ---- NoInventedSurface: a result that has surface on no input ----

        [Fact]
        public void AnOpenToolClosedByTheRepair_IsRefused_WithTheMode()
        {
            // without the mode: Success, a cavity with a lid that no input has
            var old = Invented(V.None);
            Assert.True(old.Compute(), old.Error);
            Assert.Equal(56.0, Volume(old.Result), 9);

            var p = Invented();
            Assert.False(p.Compute());
            Assert.Equal(Status.VerificationFailed, p.Status);
            Assert.Null(p.Result);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, p.BooleanStatus);
            // the error names the check, the value and the limit
            Assert.Contains("NoInventedSurface", p.Error);
            Assert.Contains("MaxInventedSurfaceShare 0:", p.Error);
            Assert.Contains("input 1 by MeshAutoRepair", p.Error);
            Assert.Equal(2, p.Report.InventedTriangles);        // (the lid: two triangles)
            Assert.Equal(4.0, p.Report.InventedSurfaceArea, 12);
            Assert.Contains("2 triangles, area 4", VerificationStep(p).Change);
            Assert.Contains(p.Report.InventedSurfaceShare.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Error);
            // the report keeps the run: the route up to the repair, then the Verification step, and the measurements
            Assert.Equal("SheetRole,Grid,Fallback,InputRepair,Verification", Names(p));
            Assert.Single(p.Report.RepairedInputs);
            Assert.Equal(1.0 / 30, p.Report.InventedSurfaceShare, 12);        // the lid, 4 of 120
            Assert.Equal(1.0, p.Report.InventedSurfaceMaxDistance, 12);        // its centre to the nearest wall
            Assert.Contains("refused", VerificationStep(p).Change);
            Assert.Equal(0.0, p.Report.RoundingVolumeChange);       // (the mode is not set)
        }

        [Fact]
        public void TheSameRun_WithTheLimitAtInfinity_IsASuccess_WithTheShareReported()
        {
            var p = Invented(V.NoInventedSurface, Inf);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(Status.Success, p.Status);
            AssertSolid(p.Result);
            Assert.Equal(56.0, Volume(p.Result), 9);
            Assert.Equal(1.0 / 30, p.Report.InventedSurfaceShare, 12);
            Assert.Equal(1.0, p.Report.InventedSurfaceMaxDistance, 12);
            Assert.True(p.Report.InventedSurfaceTolerance > 0 && p.Report.InventedSurfaceTolerance < 1e-3);
            Assert.Contains("invented share", VerificationStep(p).Change);
            Assert.Contains("passed", VerificationStep(p).Change);
            // and the result is the one of the run without the mode, bit for bit
            var old = Invented(V.None);
            Assert.True(old.Compute());
            Assert.Equal("", Invariance.Difference(old.Result, p.Result));
        }

        [Fact]
        public void TheLimitIsExceeded_NotReached()
        {
            var measured = Invented(V.NoInventedSurface, Inf);
            Assert.True(measured.Compute());
            double share = measured.Report.InventedSurfaceShare;
            // a limit equal to the measured share passes ("more than the limit" fails), one just below it fails
            var at = Invented(V.NoInventedSurface, share);
            Assert.True(at.Compute(), at.Error);
            var below = Invented(V.NoInventedSurface, share * (1 - 1e-12));
            Assert.False(below.Compute());
            Assert.Equal(Status.VerificationFailed, below.Status);
            // the default limit is 0: any invented triangle refuses the result
            var plain = new SolidBooleanPipeline(CubeAndBucket(), D) { InputRepair = Repair, Verification = V.NoInventedSurface };
            Assert.Equal(0.0, plain.MaxInventedSurfaceShare);
            Assert.False(plain.Compute());
            Assert.Equal(Status.VerificationFailed, plain.Status);
        }

        [Fact]
        public void ASmallInventedRegion_IsRefusedByTheDefault_AndPassesAboveItsShare_AndTheToleranceIsNotTooWide()
        {
            // a bucket 0.02 wide: its lid is 4e-4 of area, 4e-6 of the result, with samples at most 0.01 from the walls. The tolerance (2e-4) is far below
            // that, and one 100 times as wide would see none of it. The default limit 0 refuses it; a limit above its share passes
            Func<double, SolidBooleanPipeline> make = limit => new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Bucket(1.99, 2.01, 1.99, 2.01) }, D) {
                InputRepair = Repair, Verification = V.NoInventedSurface, MaxInventedSurfaceShare = limit
            };
            var p = make(1e-5);
            Assert.True(p.Compute(), p.Error);
            Assert.InRange(p.Report.InventedSurfaceShare, 4e-6, 4.3e-6);
            Assert.Equal(0.01, p.Report.InventedSurfaceMaxDistance, 9);
            var strict = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Bucket(1.99, 2.01, 1.99, 2.01) }, D) { InputRepair = Repair, Verification = V.NoInventedSurface };
            Assert.False(strict.Compute());
            Assert.Equal(Status.VerificationFailed, strict.Status);
            Assert.Equal(p.Report.InventedSurfaceShare, strict.Report.InventedSurfaceShare);
        }

        // ---- the tolerance ----

        // the bound that the rounding repair of the run applied
        sealed class Boolean { public ExactMeshBoolean Op; }

        [Fact]
        public void TheTolerance_IsThreeTimesTheBoundTheRoundingRepairApplied()
        {
            var seen = new Boolean();
            var p = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), D) { Verification = V.NoInventedSurface, ConfigureBoolean = op => seen.Op = op };
            Assert.True(p.Compute(), p.Error);
            double bound = seen.Op.Rounding.MaxLength;
            // (one component: its diagonal times 1e-5 is the result's)
            Assert.Equal(1e-5 * p.Result.GetBounds().DiagonalLength, bound, 15);
            Assert.Equal(3 * bound, p.Report.InventedSurfaceTolerance, 15);
            // the pipeline reads the bound from the repair that ran, which its boolean was configured with
            var wideSeen = new Boolean();
            var wide = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), D) { Verification = V.NoInventedSurface, ConfigureBoolean = op => { wideSeen.Op = op; op.RoundingMaxDisplacement = 2e-5; } };
            Assert.True(wide.Compute(), wide.Error);
            Assert.Equal(2 * p.Report.InventedSurfaceTolerance, wide.Report.InventedSurfaceTolerance, 15);
        }

        static double Ulp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(Math.Abs(x)) + 1) - Math.Abs(x);

        [Fact]
        public void WithoutARoundingRepair_TheBoundIsZero_AndTheToleranceIsTheUlpTerm()
        {
            var p = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), D) { Verification = V.NoInventedSurface, ConfigureBoolean = op => op.SafeRounding = false };
            Assert.True(p.Compute(), p.Error);
            // 256 ulp of 6 (the largest coordinate): a power of two, 4 * 2^-50... computed by the exponent
            Assert.Equal(3 * 256 * Ulp(6), p.Report.InventedSurfaceTolerance, 15);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
        }

        [Fact]
        public void FarFromTheOrigin_TheUlpTermOfTheBoundIsTheTolerance()
        {
            // two cubes of 1 at 1e9: 256 ulp of the largest coordinate (3e-5) is above 1e-5 of the diagonal (1.7e-5)
            double o = 1e9;
            var p = new SolidBooleanPipeline(Box(new Vector3d(o, o, o), new Vector3d(o + 1, o + 1, o + 1)), Box(new Vector3d(o + 0.5, o + 0.5, o + 0.5), new Vector3d(o + 1.5, o + 1.5, o + 1.5)), U) { Verification = V.NoInventedSurface };
            Assert.True(p.Compute(), p.Error);
            double ulp = Ulp(o + 1.5);
            double diag = p.Result.GetBounds().DiagonalLength;
            Assert.True(256 * ulp > 1e-5 * diag);
            Assert.Equal(3 * 256 * ulp, p.Report.InventedSurfaceTolerance, 15);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
        }

        [Fact]
        public void WithInputGrid_TheToleranceHasTheDiagonalOfTheCell_AndTheSnappedResultIsNotInvented()
        {
            // inputs 0.06 off the grid cell 0.125 (the snap moves a face by 0.06): the result lies on the snapped faces
            double cell = 0.125;
            var seen = new Boolean();
            var p = new SolidBooleanPipeline(CubeAt(0.06, 4.06), CubeAt(1.04, 6.06), U) { InputGrid = cell, Verification = V.NoInventedSurface, ConfigureBoolean = op => seen.Op = op };
            Assert.True(p.Compute(), p.Error);
            Assert.True(p.Report.InputSnappedVertices > 0);
            double bound = seen.Op.Rounding.MaxLength;
            Assert.Equal(3 * (bound + cell * Math.Sqrt(3)), p.Report.InventedSurfaceTolerance, 12);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            // the snap moved the surface farther than the rounding bound alone allows: the cell term is what keeps it from being invented
            Assert.True(p.Report.InventedSurfaceMaxDistance > 3 * bound);
            AssertSolid(p.Result);
        }

        [Fact]
        public void ASheetCutOnTheGridStep_FarFromTheOrigin_IsNotInvented_BecauseTheToleranceHasTheCell()
        {
            var p = new SolidBooleanPipeline(FarCut(), D) { Verification = Both };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal("SheetRole,Grid,SheetRole,Verification", Names(p));
            Assert.Equal(Cell, p.Report.Route[1].Grid);
            AssertSolid(p.Result);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            // the tolerance is that of the run that is the result: with the cell of the Grid step, which the snap used
            double bound = 3 * 1e-5 * p.Result.GetBounds().DiagonalLength;
            Assert.Equal(3 * (1e-5 * p.Result.GetBounds().DiagonalLength + Cell * Math.Sqrt(3)), p.Report.InventedSurfaceTolerance, 12);
            Assert.Equal(0L, p.VerificationFallbacks);
            // the snap moved the faces by more than the rounding bound without the cell, which would have flagged the result
            Assert.True(p.Report.InventedSurfaceMaxDistance > bound, p.Report.InventedSurfaceMaxDistance + " vs " + bound);
            Assert.True(p.Report.InventedSurfaceMaxDistance < p.Report.InventedSurfaceTolerance);
        }

        // ---- NoInventedSurface: correct results are not flagged ----

        public static IEnumerable<object[]> Solids()
        {
            yield return new object[] { "boxes", U }; yield return new object[] { "boxes", I }; yield return new object[] { "boxes", D };
            yield return new object[] { "spheres", U }; yield return new object[] { "spheres", I }; yield return new object[] { "spheres", D };
            yield return new object[] { "coplanar", U }; yield return new object[] { "coplanar", D }; yield return new object[] { "coplanar", I };
            yield return new object[] { "touching", U }; yield return new object[] { "touching", D };
            yield return new object[] { "contained", D }; yield return new object[] { "contained", U };
        }

        static List<DMesh3> SolidCase(string name)
        {
            switch (name) {
                case "boxes": return new List<DMesh3> { CubeAt(0, 4), CubeAt(1, 6) };
                case "spheres": return new List<DMesh3> { Sphere(Vector3d.Zero, 1, 8), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 8) };
                // a box on top of another, sharing the part of a face
                case "coplanar": return new List<DMesh3> { Cube(4), Box(new Vector3d(0, 0, 4), new Vector3d(4, 4, 6)) };
                // two boxes that share a face
                case "touching": return new List<DMesh3> { Cube(4), Box(new Vector3d(4, 0, 0), new Vector3d(8, 4, 4)) };
                case "contained": return new List<DMesh3> { Cube(4), CubeAt(1, 3) };
                default: throw new ArgumentException(name);
            }
        }

        [Theory]
        [MemberData(nameof(Solids))]
        public void ASolidOperation_HasNoInventedSurface(string name, ExactMeshBoolean.Operations op)
        {
            var p = new SolidBooleanPipeline(SolidCase(name), op) { Verification = Both };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.True(p.Report.InventedSurfaceMaxDistance <= p.Report.InventedSurfaceTolerance);
            Assert.Equal(0.0, p.Report.RoundingVolumeChange);
            if (p.Result.TriangleCount > 0)
                AssertSolid(p.Result);
        }

        [Fact]
        public void ADirectSheetCut_HasNoInventedSurface()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) {
                InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }, Verification = Both
            };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(32.0, Volume(p.Result), 9);
            AssertSolid(p.Result);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            // the sheet as routed by the pipeline itself (no stated role): the same
            var routed = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { Verification = Both };
            Assert.True(routed.Compute(), routed.Error);
            Assert.Equal("SheetRole,Verification", Names(routed));
            Assert.Equal(0.0, routed.Report.InventedSurfaceShare);
        }

        [Fact]
        public void ARepairedSolidWithASmallHole_IsMeasured_AndItsFillIsTheInventedSurface()
        {
            // a hole fill adds surface by design: the share is the fill's, and the limit says how much of it the caller accepts
            DMesh3 holed = Cube(4);
            holed.RemoveTriangle(0);
            var p = new SolidBooleanPipeline(new List<DMesh3> { holed, CubeAt(1, 6) }, U) { InputRepair = Repair, Verification = V.NoInventedSurface, MaxInventedSurfaceShare = Inf };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Single(p.Report.RepairedInputs);
            Assert.InRange(p.Report.InventedSurfaceShare, 0.04, 0.045);
            // (the fill is the missing triangle of a face of the cube, 8 of 192 of the area: its centroid is 2 sqrt(2) / 3 from the triangle that is left)
            Assert.Equal(2 * Math.Sqrt(2) / 3, p.Report.InventedSurfaceMaxDistance, 9);
        }

        [Fact]
        public void MinThicknessAndThinInputs_RemoveWholeSolids_AndNothingIsInvented()
        {
            // the cube of 4 and a far plate 1e-3 thick: MinThickness removes the plate
            var inputs = new List<DMesh3> { Cube(4), Box(new Vector3d(10, 10, 10), new Vector3d(14, 14, 10.001)) };
            var p = new SolidBooleanPipeline(inputs, U) { MinThickness = 0.01, Verification = Both };
            Assert.True(p.Compute(), p.Error);
            Assert.Single(p.Report.RemovedThinSolids);
            AssertSolid(p.Result);
            Assert.Equal(64.0, Volume(p.Result), 9);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            // a thin input treated as empty: an empty result passes, the step says so
            var thin = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Box(new Vector3d(1, 1, 1), new Vector3d(3, 3, 1.001)) }, I) {
                MinThickness = 0.01, ThinInputs = SolidBooleanPipeline.ThinInputModes.TreatAsEmpty, Verification = Both
            };
            Assert.True(thin.Compute(), thin.Error);
            Assert.Equal(0, thin.Result.TriangleCount);
            Assert.Equal(0.0, thin.Report.InventedSurfaceShare);
            Assert.Contains("no surface", VerificationStep(thin).Change);
        }

        [Fact]
        public void WhereTheRoundingMovesVertices_TheResultIsNotInvented_BecauseTheToleranceHasTheRoundingBound()
        {
            var p = new SolidBooleanPipeline(Plates(), U) { Verification = V.NoInventedSurface };
            Assert.True(p.Compute(), p.Error);
            // the six contractions moved the surface by up to 1.15 Dz = 1.7e-6, far above the 256 ulp of a coordinate of 1.3 (7e-14)
            Assert.InRange(p.Report.InventedSurfaceMaxDistance, 1.5e-6, 1.8e-6);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.True(p.Report.InventedSurfaceMaxDistance < p.Report.InventedSurfaceTolerance);
            Assert.True(p.Report.InventedSurfaceMaxDistance > 3 * 256 * 2.3e-16);
            AssertSolid(p.Result);
        }

        [Fact]
        public void TheReduction_IsNotMeasured_AndMinThicknessIsNot_TheBooleansResultIs()
        {
            // the share is that of the boolean's result: a reduction after it changes the result, not the measurement
            var plain = new SolidBooleanPipeline(new List<DMesh3> { Sphere(Vector3d.Zero, 1, 8), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 8) }, U) { Verification = V.NoInventedSurface };
            var reduced = new SolidBooleanPipeline(new List<DMesh3> { Sphere(Vector3d.Zero, 1, 8), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 8) }, U) { Verification = V.NoInventedSurface, TargetFraction = 0.25 };
            Assert.True(plain.Compute(), plain.Error);
            Assert.True(reduced.Compute(), reduced.Error);
            Assert.True(reduced.Result.TriangleCount < 0.5 * plain.Result.TriangleCount);
            Assert.Equal(plain.Report.InventedSurfaceShare, reduced.Report.InventedSurfaceShare);
            Assert.Equal(plain.Report.InventedSurfaceMaxDistance, reduced.Report.InventedSurfaceMaxDistance);
            Assert.Equal(plain.Report.InventedSurfaceTolerance, reduced.Report.InventedSurfaceTolerance);
        }

        // ---- the samples ----

        // a triangle of size 1e-6 at p: a reference surface that a sample is near only if it is at p
        static DMesh3 Speck(Vector3d p)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(p), m.AppendVertex(p + new Vector3d(1e-6, 0, 0)), m.AppendVertex(p + new Vector3d(0, 1e-6, 0)));
            return m;
        }

        static DMesh3 UnitTriangle(double s = 1)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(s, 0, 0)), m.AppendVertex(new Vector3d(0, s, 0)));
            return m;
        }

        static double ShareOf(DMesh3 mesh, IEnumerable<Vector3d> nearPoints, out double maxDistance)
        {
            var m = SolidBooleanPipeline.InventedSurfaceOf(mesh, nearPoints.Select(Speck).ToList(), 1e-3, CancellationToken.None);
            Assert.NotNull(m);
            maxDistance = m.MaxDistance;
            return m.Share;
        }

        static SolidBooleanPipeline.InventedSurfaceMeasure Measure(DMesh3 mesh, DMesh3 reference, double tolerance)
        {
            var m = SolidBooleanPipeline.InventedSurfaceOf(mesh, new List<DMesh3> { reference }, tolerance, CancellationToken.None);
            Assert.NotNull(m);
            return m;
        }

        [Fact]
        public void EverySampleKindCounts_ASingleFarSampleMakesTheTriangleInvented()
        {
            var a = new Vector3d(0, 0, 0); var b = new Vector3d(1, 0, 0); var c = new Vector3d(0, 1, 0);
            var vertices = new[] { a, b, c };
            var centroid = new[] { (a + b + c) / 3 };
            var midpoints = new[] { (a + b) / 2, (b + c) / 2, (c + a) / 2 };
            double d;
            // all seven samples near a reference: nothing is invented
            Assert.Equal(0.0, ShareOf(UnitTriangle(), vertices.Concat(centroid).Concat(midpoints), out d));
            Assert.True(d < 1e-3);
            // each kind alone is far from the references
            Assert.Equal(1.0, ShareOf(UnitTriangle(), centroid.Concat(midpoints), out d));       // (the vertices)
            Assert.Equal(1.0, ShareOf(UnitTriangle(), vertices.Concat(midpoints), out d));       // (the centroid)
            Assert.Equal(1.0, ShareOf(UnitTriangle(), vertices.Concat(centroid), out d));        // (the midpoints)
            Assert.True(d > 0.3);
        }

        [Fact]
        public void TheShareIsTheAreaOfTheInventedTrianglesOverTheAreaOfTheResult()
        {
            // a triangle of area 1/2 on the reference and one of area 2 away from it
            var m = UnitTriangle();
            int v0 = m.AppendVertex(new Vector3d(0, 0, 5)), v1 = m.AppendVertex(new Vector3d(2, 0, 5)), v2 = m.AppendVertex(new Vector3d(0, 2, 5));
            m.AppendTriangle(v0, v1, v2);
            var r = Measure(m, UnitTriangle(), 1e-3);
            Assert.Equal(2.0 / 2.5, r.Share, 15);
            Assert.Equal(2.0, r.Area, 15);
            Assert.Equal(1, r.Triangles);
            Assert.Equal(Math.Sqrt(26), r.MaxDistance, 9);
            Assert.Equal(0.0, Measure(new DMesh3(), UnitTriangle(), 1e-3).Share);
        }

        [Fact]
        public void ADistanceAtTheToleranceIsNotInvented_OneAboveIs()
        {
            var near = UnitTriangle(); foreach (int v in near.VertexIndices()) near.SetVertex(v, near.GetVertex(v) + new Vector3d(0, 0, 0.25));
            var at = Measure(near, UnitTriangle(), 0.25);
            Assert.Equal(0.0, at.Share);
            Assert.Equal(0.25, at.MaxDistance);
            Assert.Equal(1.0, Measure(near, UnitTriangle(), 0.2499).Share);
        }

        // ---- RoundingVolume ----

        [Fact]
        public void TheThinPlates_LoseSevenTenthsOfAPercentOfVolume_AndAreRefused_WithTheMode()
        {
            var old = new SolidBooleanPipeline(Plates(), U);
            Assert.True(old.Compute(), old.Error);

            var p = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume };
            Assert.False(p.Compute());
            Assert.Equal(Status.VerificationFailed, p.Status);
            Assert.Null(p.Result);
            Assert.Contains("RoundingVolume", p.Error);
            Assert.DoesNotContain("NoInventedSurface", p.Error);
            Assert.Contains("MaxRoundingVolumeChange 0.001", p.Error);
            Assert.Equal("Verification", Names(p));
            // the change is |after - before| / |before| of the run's own volumes, 0.7 % (the thin plate A loses 1.85 % of its own volume)
            double change = Math.Abs(p.Report.RoundingVolumeAfter - p.Report.RoundingVolumeBefore) / Math.Abs(p.Report.RoundingVolumeBefore);
            Assert.Equal(change, p.Report.RoundingVolumeChange);
            Assert.InRange(p.Report.RoundingVolumeChange, 0.0070, 0.0073);
            Assert.Equal(old.Report.RoundingVolumeBefore, p.Report.RoundingVolumeBefore);
            Assert.Equal(old.Report.RoundingVolumeAfter, p.Report.RoundingVolumeAfter);
            // (the mode of the other check is not set: nothing of it is measured)
            Assert.Equal(0.0, p.Report.InventedSurfaceTolerance);

            // a limit above it, or at infinity: a success with the same result as without the mode
            foreach (double limit in new[] { 0.01, Inf }) {
                var ok = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = limit };
                Assert.True(ok.Compute(), ok.Error);
                AssertSolid(ok.Result);
                Assert.Equal("", Invariance.Difference(old.Result, ok.Result));
                Assert.Equal(change, ok.Report.RoundingVolumeChange);
                Assert.Contains("passed", VerificationStep(ok).Change);
            }
            // the limit is exceeded, not reached
            var at = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = change };
            Assert.True(at.Compute(), at.Error);
            var below = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = change * (1 - 1e-12) };
            Assert.False(below.Compute());
            Assert.Equal(Status.VerificationFailed, below.Status);
        }

        [Fact]
        public void ARoundingThatMovesNothing_HasChangeZero()
        {
            var p = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = 0 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0.0, p.Report.RoundingVolumeChange);
            Assert.Equal(162.0, Math.Abs(p.Report.RoundingVolumeBefore), 9);
            Assert.Contains("relative change 0 ", VerificationStep(p).Change);
        }

        [Fact]
        public void ASliverResultThatTheRoundingRemoves_PassesBecauseOfTheFloor()
        {
            // the repair removes every sliver of the difference: all of its volume is lost (change 1), which is below the floor of the inputs
            var p = new SolidBooleanPipeline(Slivers(), D) { Verification = V.RoundingVolume };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.NotEmpty(p.Report.RoundingRemoved);
            Assert.Equal(0.0, p.Report.RoundingVolumeAfter);
            Assert.True(p.Report.RoundingVolumeBefore > 0);
            Assert.Equal(1.0, p.Report.RoundingVolumeChange);
            Assert.Equal(0, p.Result.TriangleCount);
            double floor = SolidBooleanPipeline.RoundingVolumeFloorOf(Slivers(), i => true);
            Assert.Equal(1e-6 * Volume(Slivers()[0]), floor, 12);
            Assert.True(p.Report.RoundingVolumeBefore < floor);
            Assert.Contains("passed", VerificationStep(p).Change);
        }

        [Fact]
        public void TheFloor_IsAShareOfTheSmallestClosedInput_OrOfTheCubedDiagonal()
        {
            var inputs = new List<DMesh3> { Cube(10), Cube(1), Sheet(0.5, 0, 1) };
            // closed: the two cubes (volumes 1000 and 1): the smallest counts, the sheet does not
            Assert.Equal(1e-6 * 1.0, SolidBooleanPipeline.RoundingVolumeFloorOf(inputs, i => i < 2), 15);
            Assert.Equal(1e-6 * 1000.0, SolidBooleanPipeline.RoundingVolumeFloorOf(inputs, i => i == 0), 12);
            // a closed input without volume (a sheet given twice, opposite) is skipped
            var doubled = Sheet(0.5, 0, 1); var flipped = Sheet(0.5, 0, 1);
            foreach (int t in flipped.TriangleIndices()) { Index3i q = flipped.GetTriangle(t); flipped.SetTriangle(t, new Index3i(q.a, q.c, q.b)); }
            var both = new DMesh3(); both.AppendTriangle(both.AppendVertex(new Vector3d(0, 0, 0.5)), both.AppendVertex(new Vector3d(1, 0, 0.5)), both.AppendVertex(new Vector3d(1, 1, 0.5)));
            both.AppendTriangle(both.AppendVertex(new Vector3d(0, 0, 0.5)), both.AppendVertex(new Vector3d(1, 1, 0.5)), both.AppendVertex(new Vector3d(1, 0, 0.5)));
            Assert.Equal(1e-6 * 1000.0, SolidBooleanPipeline.RoundingVolumeFloorOf(new List<DMesh3> { both, Cube(10) }, i => true), 12);
            // no closed input: 1e-9 of the cubed diagonal of all inputs
            double diag = Math.Sqrt(3) * 10;
            Assert.Equal(1e-9 * diag * diag * diag, SolidBooleanPipeline.RoundingVolumeFloorOf(inputs, i => false), 9);
        }

        [Fact]
        public void WithoutARounding_TheCheckPasses_WithChangeZero_AndTheStepSaysSo()
        {
            var p = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume, ConfigureBoolean = op => op.SafeRounding = false };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0.0, p.Report.RoundingVolumeChange);
            Assert.Contains("no rounding", VerificationStep(p).Change);
            Assert.Equal(0.0, p.Report.RoundingVolumeBefore);
        }

        [Fact]
        public void BothChecks_RunOnOneResult_AndTheErrorNamesTheOnesThatFailed()
        {
            var p = new SolidBooleanPipeline(Plates(), U) { Verification = Both };
            Assert.False(p.Compute());
            Assert.Contains("RoundingVolume", p.Error);
            Assert.DoesNotContain("NoInventedSurface:", p.Error);
            string step = VerificationStep(p).Change;
            Assert.Contains("NoInventedSurface", step);
            Assert.Contains("RoundingVolume", step);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.True(p.Report.RoundingVolumeChange > 1e-3);
            // a result that fails the first check: its error names it, and the second is measured too
            var q = Invented(Both);
            Assert.False(q.Compute());
            Assert.Contains("NoInventedSurface", q.Error);
            Assert.Contains("RoundingVolume", VerificationStep(q).Change);
            Assert.Equal(0.0, q.Report.RoundingVolumeChange);
        }

        [Fact]
        public void AfterAGridRetry_TheChecksAreOfTheRetry()
        {
            // the Grid step answered a refused sheet: the volumes of the rounding are those of the run that is the result
            var p = new SolidBooleanPipeline(FarCut(), D) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = 0 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("SheetRole,Grid,SheetRole,Verification", Names(p));
            Assert.True(p.Report.RoundingVolumeBefore > 0);
            Assert.Equal(p.Report.RoundingVolumeBefore, p.Report.RoundingVolumeAfter);
            Assert.Equal(0.0, p.Report.RoundingVolumeChange);
        }

        // ---- the options ----

        [Fact]
        public void UnknownBitsAndBadLimits_AreInvalidInput()
        {
            foreach (V bad in new[] { (V)32, (V)64, (V)(-1), (V)33 }) {
                var p = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = bad };
                Assert.False(p.Compute());
                Assert.Equal(Status.InvalidInput, p.Status);
                Assert.Null(p.Result);
            }
            foreach (double bad in new[] { -1.0, -1e-300, double.NaN, double.NegativeInfinity }) {
                var share = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = Both, MaxInventedSurfaceShare = bad };
                Assert.False(share.Compute());
                Assert.Equal(Status.InvalidInput, share.Status);
                var volume = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = bad };
                Assert.False(volume.Compute());
                Assert.Equal(Status.InvalidInput, volume.Status);
            }
            // (and infinity and zero are valid)
            var ok = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = Both, MaxInventedSurfaceShare = 0, MaxRoundingVolumeChange = Inf };
            Assert.True(ok.Compute(), ok.Error);
        }

        // ---- invariance, reuse, determinism ----

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Frames))]
        public void TheVerdict_IsTheSameInEveryFrame(string name)
        {
            Similarity s = Similarity.ByName(name);
            Func<List<DMesh3>, List<DMesh3>> map = list => list.Select(m => Invariance.Transformed(m, s)).ToList();
            var bad = new SolidBooleanPipeline(map(CubeAndBucket()), D) { InputRepair = Repair, Verification = Both };
            Assert.False(bad.Compute());
            Assert.Equal(Status.VerificationFailed, bad.Status);
            Assert.Contains("NoInventedSurface", bad.Error);
            Assert.Equal(1.0 / 30, bad.Report.InventedSurfaceShare, 6);
            var good = new SolidBooleanPipeline(map(new List<DMesh3> { CubeAt(0, 4), CubeAt(1, 6) }), D) { Verification = Both };
            Assert.True(good.Compute(), good.Status + " " + good.Error);
            Assert.Equal(0.0, good.Report.InventedSurfaceShare);
            // not exactly 0 in every frame: in a rotated frame the rounding repair contracts the edges that the rotation split (4 collapses, vertices moved by
            // 1 ulp), the signed volume of the other triangulation differs in the last bits. That is round-off (a few ulp of the volume), not a moved volume
            Assert.True(good.Report.RoundingVolumeChange <= 1e-13, "RoundingVolumeChange " + good.Report.RoundingVolumeChange);
            Assert.True(good.Report.InventedSurfaceMaxDistance < good.Report.InventedSurfaceTolerance / 8, "max distance " + good.Report.InventedSurfaceMaxDistance + " tolerance " + good.Report.InventedSurfaceTolerance);
        }

        [Fact]
        public void ScalingByAPowerOfTwo_GivesTheSameShareBitForBit_AndTheScaledTolerance()
        {
            Func<Similarity, SolidBooleanPipeline> run = s => {
                var p = new SolidBooleanPipeline(CubeAndBucket().Select(m => Invariance.Transformed(m, s)).ToList(), D) { InputRepair = Repair, Verification = V.NoInventedSurface, MaxInventedSurfaceShare = Inf };
                Assert.True(p.Compute(), p.Error);
                return p;
            };
            var one = run(Similarity.Identity);
            foreach (Similarity s in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                var scaled = run(s);
                Assert.True(s.IsExact);
                Assert.True(Same(one.Report.InventedSurfaceShare, scaled.Report.InventedSurfaceShare), s.Name);
                Assert.True(Same(one.Report.InventedSurfaceTolerance * s.Scale, scaled.Report.InventedSurfaceTolerance), s.Name);
                Assert.True(Same(one.Report.InventedSurfaceMaxDistance * s.Scale, scaled.Report.InventedSurfaceMaxDistance), s.Name);
            }
            // the rounding volume: the change is a ratio of volumes, which scale alike
            Func<Similarity, SolidBooleanPipeline> plates = s => {
                var p = new SolidBooleanPipeline(Plates().Select(m => Invariance.Transformed(m, s)).ToList(), U) { Verification = Both, MaxRoundingVolumeChange = Inf };
                Assert.True(p.Compute(), p.Error);
                return p;
            };
            var base1 = plates(Similarity.Identity);
            foreach (Similarity s in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                var scaled = plates(s);
                Assert.True(Same(base1.Report.RoundingVolumeChange, scaled.Report.RoundingVolumeChange), s.Name);
                Assert.True(Same(base1.Report.InventedSurfaceShare, scaled.Report.InventedSurfaceShare), s.Name);
                Assert.True(Same(base1.Report.InventedSurfaceTolerance * s.Scale, scaled.Report.InventedSurfaceTolerance), s.Name);
            }
        }

        [Fact]
        public void ComputeTwice_OnOneInstance_GivesTheSame_AlsoWhenTheModeIsSwitched()
        {
            var p = Invented(V.None);
            Assert.True(p.Compute(), p.Error);
            DMesh3 noneResult = new DMesh3(p.Result);      // (a copy: the later runs are compared with the result of this first one)
            // the mode on: refused, with the report of that run only
            p.Verification = V.NoInventedSurface;
            Assert.False(p.Compute());
            Assert.Equal(Status.VerificationFailed, p.Status);
            double share = p.Report.InventedSurfaceShare;
            Assert.Equal(1.0 / 30, share, 12);
            Assert.Equal(1, p.Report.Route.Count(s => s.Name == "Verification"));
            // again: the same, no leftovers of the first
            Assert.False(p.Compute());
            Assert.Equal(share, p.Report.InventedSurfaceShare);
            Assert.Equal(1, p.Report.Route.Count(s => s.Name == "Verification"));
            // the mode off again: the old run, nothing of the checks left
            p.Verification = V.None;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(Status.Success, p.Status);
            Assert.Equal("", Invariance.Difference(noneResult, p.Result));
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.Equal(0.0, p.Report.InventedSurfaceTolerance);
            Assert.DoesNotContain("Verification", Names(p));
            // and with the limit raised on the same instance
            p.Verification = V.NoInventedSurface; p.MaxInventedSurfaceShare = Inf;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(share, p.Report.InventedSurfaceShare);
        }

        [Fact]
        public void RepeatedRuns_GiveTheSameNumbers_BitForBit()
        {
            var runs = Enumerable.Range(0, 3).Select(_ => {
                var p = new SolidBooleanPipeline(Plates(), U) { Verification = Both, MaxRoundingVolumeChange = Inf };
                Assert.True(p.Compute(), p.Error);
                return p.Report;
            }).ToList();
            foreach (var r in runs.Skip(1)) {
                Assert.True(Same(runs[0].InventedSurfaceShare, r.InventedSurfaceShare));
                Assert.True(Same(runs[0].InventedSurfaceMaxDistance, r.InventedSurfaceMaxDistance));
                Assert.True(Same(runs[0].InventedSurfaceTolerance, r.InventedSurfaceTolerance));
                Assert.True(Same(runs[0].RoundingVolumeChange, r.RoundingVolumeChange));
            }
        }

        [Fact]
        public void ACancelledRun_IsCancelled_NotVerified()
        {
            var cts = new CancellationTokenSource();
            var p = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = Both, Cancellation = cts.Token };
            cts.Cancel();
            Assert.False(p.Compute());
            Assert.Equal(Status.Cancelled, p.Status);
        }

        // ---- found by the review: exact ranking, the bound of the repair, magnitudes, the floor ----

        // the cube of side s centred at (cx, cy, H) straddles the top face of the box [-H, H]^3 (z = H), near one of its diagonals; both rotated by 23 degrees if asked
        static List<DMesh3> StraddlingCube(double H, double s, double cx, double cy, bool rotated, bool bigFirst)
        {
            DMesh3 big = Box(new Vector3d(-H, -H, -H), new Vector3d(H, H, H));
            DMesh3 small = Box(new Vector3d(cx - s / 2, cy - s / 2, H - s / 2), new Vector3d(cx + s / 2, cy + s / 2, H + s / 2));
            if (rotated)
                foreach (DMesh3 m in new[] { big, small })
                    MeshTransforms.Rotate(m, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
            return bigFirst ? new List<DMesh3> { big, small } : new List<DMesh3> { small, big };
        }

        public static IEnumerable<object[]> Straddling()
        {
            foreach (bool rotated in new[] { false, true })
                foreach (double[] hs in new[] { new[] { 1e2, 1e-5 }, new[] { 1e4, 1e-5 }, new[] { 1e5, 1e-3 } })
                    foreach (double sign in new[] { 1.0, -1.0 })
                        yield return new object[] { hs[0], hs[1], sign, rotated };
        }

        [Theory]
        [MemberData(nameof(Straddling))]
        public void ATinyCubeOnABigFace_IsNotInvented_WhereTheTreesSquaredRankingIsNoise(double H, double s, double sign, bool rotated)
        {
            // (a reference triangle of size 1e4 ranks by a squared distance with an error of 1e-8 and more: its absolute error is eps E^2, far above the
            // squared tolerance. The bounded search alone returned a triangle that is 2e-7 away for a sample that lies on the face)
            foreach (var op in new[] { I, D }) {
                var inputs = StraddlingCube(H, s, 0.3 * s, sign * 0.3 * s, rotated, op == I);
                var p = new SolidBooleanPipeline(inputs, op) { Verification = V.NoInventedSurface };
                Assert.True(p.Compute(), H + " " + s + " " + op + ": " + p.Status + " " + p.Error);
                Assert.Equal(0.0, p.Report.InventedSurfaceShare);
                Assert.Equal(0, p.Report.InventedTriangles);
                Assert.True(p.Report.InventedSurfaceMaxDistance <= p.Report.InventedSurfaceTolerance);
                Assert.True(p.VerificationSamples > 0);
            }
        }

        [Fact]
        public void TheExactSearch_IsTakenWhereTheFastPathCannotDecide_AndNotOnOrdinaryInputs()
        {
            // the scenario: the fast path alone is wrong for some samples (the test above would fail without the exact search), so the exact search ran
            var big = new SolidBooleanPipeline(StraddlingCube(1e4, 1e-5, 3e-6, 3e-6, false, true), I) { Verification = V.NoInventedSurface };
            Assert.True(big.Compute(), big.Error);
            Assert.True(big.VerificationFallbacks > 0, "fallbacks " + big.VerificationFallbacks + " of " + big.VerificationSamples);
            Assert.Equal(0.0, big.Report.InventedSurfaceShare);
            // ordinary inputs: the fast path decides every sample
            foreach (string name in new[] { "boxes", "spheres", "coplanar", "touching", "contained" }) {
                var p = new SolidBooleanPipeline(SolidCase(name), U) { Verification = V.NoInventedSurface };
                Assert.True(p.Compute(), p.Error);
                Assert.True(p.VerificationSamples > 0);
                Assert.Equal(0L, p.VerificationFallbacks);
            }
            var plates = new SolidBooleanPipeline(Plates(), U) { Verification = V.NoInventedSurface };
            Assert.True(plates.Compute(), plates.Error);
            Assert.Equal(0L, plates.VerificationFallbacks);
            // an invented region takes the exact search for its samples, and only for those
            var lid = Invented(V.NoInventedSurface, Inf);
            Assert.True(lid.Compute(), lid.Error);
            Assert.True(lid.VerificationFallbacks > 0 && lid.VerificationFallbacks < lid.VerificationSamples / 2);
        }

        [Fact]
        public void TheBoundIsTheRepairs_NotTheDiagonalOfTheResultAfterMinThickness()
        {
            // input 0: a unit cube under a generic rotation plus a far plate 1000 x 1000 x 0.01 in the same mesh; input 1: the cube rotated by 0.1 degree. The
            // bound of the repair is 1e-5 of the diagonal of the plate (1.4e-2); MinThickness removes the plate, and the result is two cubes, whose own box
            // is 1e-5 of 1.8 (the old tolerance 5e-5) while the repair legitimately moved the surface by up to 5e-4
            DMesh3 cube = CubeAt(0, 1);
            MeshTransforms.Rotate(cube, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
            DMesh3 tilted = new DMesh3(cube);
            MeshTransforms.Rotate(tilted, new Vector3d(0.5, 0.5, 0.5), Quaterniond.AxisAngleD(new Vector3d(0.2, -0.4, 0.9).Normalized, 0.1));
            DMesh3 plate = Box(new Vector3d(500, 500, 500), new Vector3d(1500, 1500, 500.01));
            var first = new DMesh3(cube);
            var map = new int[plate.MaxVertexID];
            foreach (int v in plate.VertexIndices()) map[v] = first.AppendVertex(plate.GetVertex(v));
            foreach (int t in plate.TriangleIndices()) { Index3i q = plate.GetTriangle(t); first.AppendTriangle(map[q.a], map[q.b], map[q.c]); }
            var seen = new Boolean();
            var p = new SolidBooleanPipeline(new List<DMesh3> { first, tilted }, U) { MinThickness = 0.05, Verification = V.NoInventedSurface, ConfigureBoolean = op => seen.Op = op };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Single(p.Report.RemovedThinSolids);
            double bound = seen.Op.Rounding.MaxLength;
            Assert.True(bound > 1e-2);
            // the result's own box: far smaller than the box that the bound came from
            Assert.True(1e-5 * p.Result.GetBounds().DiagonalLength < 1e-4);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
            Assert.Equal(3 * bound, p.Report.InventedSurfaceTolerance, 12);
            AssertSolid(p.Result);
        }

        [Fact]
        public void TheUlpTermIsAtTheMagnitudeOfTheInputs_NotOnlyOfTheResult()
        {
            // a big box under a rotation whose top face passes through the origin, and a cube of 1e-7 at the origin: the result is at the origin (its ulp is
            // 1e-23) but the distance of its samples to the big faces is computed with coordinates of 1e5 (ulp 1.5e-11)
            foreach (double[] hs in new[] { new[] { 1e5, 1e-7 }, new[] { 1e3, 1e-9 } }) {
                double H = hs[0], s = hs[1];
                DMesh3 big = Box(new Vector3d(-H, -H, -2 * H), new Vector3d(H, H, 0));
                DMesh3 small = Box(new Vector3d(-s / 2, -s / 2, -s / 2), new Vector3d(s / 2, s / 2, s / 2));
                foreach (DMesh3 m in new[] { big, small })
                    MeshTransforms.Rotate(m, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
                var p = new SolidBooleanPipeline(new List<DMesh3> { big, small }, I) { Verification = V.NoInventedSurface };
                Assert.True(p.Compute(), p.Error);
                Assert.Equal(0.0, p.Report.InventedSurfaceShare);
                Assert.True(p.Report.InventedSurfaceTolerance >= 3 * 256 * Ulp(H));
            }
        }

        [Fact]
        public void ANonFiniteTolerance_IsAFailedVerification_NotAPass()
        {
            // a NaN rounding bound (MaxDisplacement NaN): the tolerance is NaN, and every "farther than" would be false
            var p = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), U) { Verification = V.NoInventedSurface, ConfigureBoolean = op => op.RoundingMaxDisplacement = double.NaN };
            bool ok = p.Compute();
            if (p.Status == Status.BooleanFailed)
                return;     // (the boolean refuses a NaN bound itself: nothing to verify)
            Assert.False(ok);
            Assert.Equal(Status.VerificationFailed, p.Status);
            Assert.Contains("not finite", p.Error);
        }

        // the box [0, size]^3 with its own vertices per face (an unwelded soup); the copies that the +x face owns lie jitter (1e-13) off the copies of the other faces
        static DMesh3 UnweldedBox(double size, double jitter)
        {
            var m = new DMesh3();
            double off = 0;
            Func<int, int, int, int> v = (i, j, k) => m.AppendVertex(new Vector3d(i * size + off, j * size + off, k * size + off));
            Action<int[], int[], int[], int[]> quad = (a, b, c, d) => {
                int ia = v(a[0], a[1], a[2]), ib = v(b[0], b[1], b[2]), ic = v(c[0], c[1], c[2]), id = v(d[0], d[1], d[2]);
                m.AppendTriangle(ia, ib, ic); m.AppendTriangle(ia, ic, id);
            };
            quad(new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 1, 1, 0 }, new[] { 1, 0, 0 });
            quad(new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 });
            quad(new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 0, 1 }, new[] { 0, 0, 1 });
            quad(new[] { 0, 1, 0 }, new[] { 0, 1, 1 }, new[] { 1, 1, 1 }, new[] { 1, 1, 0 });
            quad(new[] { 0, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 1, 1 }, new[] { 0, 1, 0 });
            off = jitter;
            quad(new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 }, new[] { 1, 0, 1 });
            return m;
        }

        [Fact]
        public void TheFloor_TakesClosedFromTheInputsAsPassed_NotFromTheSnappedCopies()
        {
            // an unwelded box of 2 whose coincident corner copies differ by 1e-13: open as passed (the winding test works by position), closed after the
            // InputGrid snap merges the copies. The tool (3 x 3 x 3, closed as passed) is the only input that counts for the floor: 1e-6 * 27. A floor
            // taken from the pipeline's InputClosed (the snapped copies) would also count the box: 1e-6 * 8. Asserted through the floor in the step's text.
            DMesh3 box = UnweldedBox(2, 1e-13), tool = Box(new Vector3d(1, 1, 1), new Vector3d(4, 4, 4));
            Assert.False(ExactMeshBoolean.IsWindingClosed(box));
            Assert.True(ExactMeshBoolean.IsWindingClosed(tool));
            var p = new SolidBooleanPipeline(new List<DMesh3> { box, tool }, D) { InputGrid = Math.Pow(2, -20), Verification = V.RoundingVolume };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.True(p.Report.InputSnappedVertices > 0);
            string text = VerificationStep(p).Change;
            Assert.Contains("floor " + (1e-6 * 27.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture), text);
            Assert.DoesNotContain("floor " + (1e-6 * 8.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture), text);
        }

        [Fact]
        public void TheFloor_UsesTheVerticesTrianglesUse_NotAnIsolatedOne()
        {
            var cube = Cube(2);
            cube.AppendVertex(new Vector3d(1e300, 0, 0));        // (no triangle uses it)
            double floor = SolidBooleanPipeline.RoundingVolumeFloorOf(new List<DMesh3> { cube, Cube(10) }, i => true);
            Assert.Equal(1e-6 * 8, floor, 15);
            // no closed input: the diagonal of the used vertices only
            double floorOpen = SolidBooleanPipeline.RoundingVolumeFloorOf(new List<DMesh3> { cube }, i => false);
            Assert.Equal(1e-9 * Math.Pow(Math.Sqrt(12), 3), floorOpen, 15);
        }

        [Fact]
        public void TheRoundingVolumeVerdict_HandlesABeforeOfZero_AndAnInfiniteLimit()
        {
            double change;
            // before 0, after not: the change is +infinity, and it fails above the floor
            Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(0, 1e-3, 1e-3, 1e-6, out change));
            Assert.Equal(double.PositiveInfinity, change);
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(0, 1e-9, 1e-3, 1e-6, out change));      // (below the floor)
            Assert.Equal(double.PositiveInfinity, change);
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(0, 1e-3, double.PositiveInfinity, 1e-6, out change));
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(0, 0, 0, 0, out change));
            Assert.Equal(0.0, change);
            // ordinary: relative to before
            Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(2, 3, 0.4, 1e-6, out change));
            Assert.Equal(0.5, change);
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(2, 3, 0.5, 1e-6, out change));
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(2, 3, double.PositiveInfinity, 1e-6, out change));
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(2, 3, 0.4, 1, out change));            // (the floor)
        }

        // ---- RoundingVolume: the change of the parts thinner than their own rounding length is taken out, part by part ----

        // a plate [off, off + 1]^2 x [off, off + h] minus the plate shifted down by t: a slab of thickness t over a whole face (both rotated about the
        // centre of the bottom face by 23 degrees about the axis when there is one)
        static List<DMesh3> PlateSlab(double off, double h, double t, Vector3d? axis)
        {
            DMesh3 plate = Box(new Vector3d(off, off, off), new Vector3d(off + 1, off + 1, off + h));
            DMesh3 tool = Box(new Vector3d(off, off, off - t), new Vector3d(off + 1, off + 1, off + h - t));
            if (axis.HasValue) {
                var q = Quaterniond.AxisAngleD(axis.Value.Normalized, 23);
                var centre = new Vector3d(off + 0.5, off + 0.5, off);
                MeshTransforms.Rotate(plate, centre, q);
                MeshTransforms.Rotate(tool, centre, q);
            }
            return new List<DMesh3> { plate, tool };
        }

        static readonly Vector3d AxisA = new Vector3d(0.3, 0.5, 0.81), AxisB = new Vector3d(1, 2, 3), AxisC = new Vector3d(-0.7, 0.2, 0.4);

        // (the repair removes the slab as a component that its coordinates do not resolve: removal is a matter of ulps, not of the bound, so it takes a
        // slab of 1e-9 to 1e-8 over a face, and the smallest input must be a thin plate for the slab's volume to be above the floor share)
        [Theory]
        [InlineData(0.0, 0, 17)]
        [InlineData(0.0, 0, 19)]
        [InlineData(0.0, 2, 15)]
        [InlineData(1000.0, 1, 14)]
        public void AFaceWideSlabThatTheRoundingRemoves_PassesBecauseItIsThinnerThanTheBound(double off, int axis, int step)
        {
            double t = 1e-11 * Math.Pow(10, step / 6.0);        // 6.8e-9, 1.5e-8, 3.2e-9, 2.2e-9
            Vector3d ax = axis == 0 ? AxisA : axis == 1 ? AxisB : AxisC;
            var p = new SolidBooleanPipeline(PlateSlab(off, 1e-4, t, ax), D) { Verification = V.RoundingVolume };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(Status.Success, p.Status);
            // the slab was removed as a component, whole: its volume is the whole change, and it is above the share of the smallest input (the old floor)
            Assert.Single(p.Report.RoundingRemoved);
            var gone = p.Report.RoundingRemoved[0];
            Assert.True(Math.Abs(p.Report.RoundingVolumeBefore - gone.Volume) <= 1e-6 * Math.Abs(gone.Volume), p.Report.RoundingVolumeBefore + " " + gone.Volume);
            Assert.Equal(1.0, p.Report.RoundingVolumeChange);
            double share = SolidBooleanPipeline.RoundingVolumeFloorOf(PlateSlab(off, 1e-4, t, ax), i => true);
            Assert.True(gone.Volume > share, gone.Volume + " " + share);
            // the result is empty (a closed solid trivially: nothing is left of a slab)
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal(0.0, p.Report.RoundingVolumeAfter);
            Assert.Contains("thinner than", VerificationStep(p).Change);
            // it is thinner than the bound (about 1e-5)
            Assert.True(gone.Thickness < 1e-6);
        }

        // a thin plate whose result is itself far thinner than the rounding bound: the rounding moves its volume (it grows to a few times the slab's), which is
        // a move of a few 1e-12 of the surface against a bound of 1e-5
        [Theory]
        [InlineData(0.0, 1e-12)]
        [InlineData(0.0, 2.15e-12)]
        [InlineData(1000.0, 1e-12)]
        [InlineData(1000.0, 2.15e-12)]
        public void AThinResultThatTheRoundingMoves_PassesBecauseItIsThinnerThanTheBound(double off, double t)
        {
            var p = new SolidBooleanPipeline(PlateSlab(off, 1e-7, t, null), D) { Verification = V.RoundingVolume };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertSolid(p.Result);
            Assert.True(p.Report.RoundingVolumeChange > 1e-3, "change " + p.Report.RoundingVolumeChange);       // (more than the limit: only taking the thin part out passes it)
            Assert.Empty(p.Report.RoundingRemoved);
            Assert.True(Math.Abs(p.Report.RoundingVolumeAfter - Volume(p.Result)) <= 1e-6 * p.Report.RoundingVolumeAfter, p.Report.RoundingVolumeAfter + " " + Volume(p.Result));
            double floor = SolidBooleanPipeline.RoundingVolumeFloorOf(PlateSlab(off, 1e-7, t, null), i => true);
            Assert.True(Math.Abs(p.Report.RoundingVolumeAfter - p.Report.RoundingVolumeBefore) > floor);       // (more than the share of the inputs: the old floor)
        }

        // the rounding repair on a mesh that it leaves as it is (clean boxes), recording the components it found
        static MeshRoundingRepair Recorded(DMesh3 mesh, double maxDisplacement = 1e-5)
        {
            var r = new MeshRoundingRepair(mesh) { RecordComponents = true, MaxDisplacement = maxDisplacement };
            r.Apply();
            return r;
        }

        static void ScaleZ(DMesh3 m, double z0, double factor)
        {
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, new Vector3d(p.x, p.y, z0 + (p.z - z0) * factor));
            }
        }

        [Fact]
        public void TheThinChange_IsThatOfEachThinPartAlone_AndNeverOfAThickOne()
        {
            // a slab of 1e-6 over [0,1]^2 (its length 1.4e-5), a cube far from it, a slab of 3e-5 (thicker than its own length 1.4e-5)
            DMesh3 mesh = Box(Vector3d.Zero, new Vector3d(1, 1, 1e-6));
            new MeshEditor(mesh).AppendMesh(Box(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6)));
            new MeshEditor(mesh).AppendMesh(Box(new Vector3d(9, 9, 9), new Vector3d(10, 10, 10 + 3e-5)));
            int parts;
            var rounding = Recorded(mesh);
            Assert.Equal(3, rounding.ComponentsBefore.Count);
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(rounding, mesh, out parts));      // (nothing moved)
            Assert.Equal(1, parts);
            // the thin slab loses half, the thick ones 10 %: only the slab's change is taken out
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                double f = p.x < 2 ? 0.5 : 0.9;
                mesh.SetVertex(v, new Vector3d(p.x, p.y, p.x < 2 ? p.z * f : p.x < 8 ? 5 + (p.z - 5) * f : 10 + (p.z - 10) * f));
            }
            double explained = SolidBooleanPipeline.RoundingVolumeThinChangeOf(rounding, mesh, out parts);
            Assert.Equal(1, parts);
            Assert.Equal(-0.5e-6, explained, 15);
        }

        [Fact]
        public void TheThinChange_OfARemovedThinPart_IsItsWholeVolume_AndNotOfAThickOne()
        {
            DMesh3 mesh = Box(Vector3d.Zero, new Vector3d(1, 1, 1e-6));
            new MeshEditor(mesh).AppendMesh(Box(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6)));
            var rounding = Recorded(mesh);
            // the slab's triangles are removed: its volume goes, the cube stays
            foreach (int t in mesh.TriangleIndices().ToList().Where(t => mesh.GetVertex(mesh.GetTriangle(t).a).x < 2))
                mesh.RemoveTriangle(t, true);
            int parts;
            Assert.Equal(-1e-6, SolidBooleanPipeline.RoundingVolumeThinChangeOf(rounding, mesh, out parts), 15);
            Assert.Equal(1, parts);
            // the cube removed instead: a thick part that disappears is not taken out
            DMesh3 other = Box(Vector3d.Zero, new Vector3d(1, 1, 1e-6));
            new MeshEditor(other).AppendMesh(Box(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6)));
            var r2 = Recorded(other);
            foreach (int t in other.TriangleIndices().ToList().Where(t => other.GetVertex(other.GetTriangle(t).a).x > 2))
                other.RemoveTriangle(t, true);
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(r2, other, out parts));
            Assert.Equal(1, parts);          // (the slab, unchanged, counts as a part with change 0)
        }

        [Fact]
        public void TheThinChange_TakesNothingOut_WhereThePartsCannotBeMatched()
        {
            int parts;
            DMesh3 mesh = Box(Vector3d.Zero, new Vector3d(1, 1, 1e-6));
            var rounding = Recorded(mesh);
            ScaleZ(mesh, 0, 0.5);
            Assert.True(SolidBooleanPipeline.RoundingVolumeThinChangeOf(rounding, mesh, out parts) < 0);
            // a part with vertices that the repair did not have (a new triangle): nothing
            DMesh3 added = new DMesh3(mesh);
            added.AppendTriangle(added.AppendVertex(new Vector3d(7, 7, 7)), added.AppendVertex(new Vector3d(8, 7, 7)), added.AppendVertex(new Vector3d(7, 8, 7)));
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(rounding, added, out parts));
            Assert.Equal(0, parts);
            // a mesh that is not the repair's
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(rounding, new DMesh3(mesh), out parts));
            // a repair that recorded nothing
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(new MeshRoundingRepair(mesh), mesh, out parts));
            // an open part (a sheet): not closed, nothing
            DMesh3 sheet = Sheet(0, 0, 1);
            var rs = Recorded(sheet);
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(rs, sheet, out parts));
            Assert.Equal(0, parts);
        }

        [Fact]
        public void TheThinChange_UsesTheUlpTermWhereTheBoundIsZero_AndScalesExactly()
        {
            // far from the origin the ulp term (256 ulp of 1e6 = 2.9e-8) is the length when MaxDisplacement is 0: a slab of 1e-8 is thin, one of 1e-7 is not
            Func<double, DMesh3> far = th => Box(new Vector3d(1e6, 1e6, 1e6), new Vector3d(1e6 + 1, 1e6 + 1, 1e6 + th));
            int parts;
            DMesh3 thin = far(1e-8), thick = far(1e-7);
            var rt = Recorded(thin, 0);
            var rk = Recorded(thick, 0);
            foreach (DMesh3 m in new[] { thin, thick }) {
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    if (p.z > 1e6 + 1e-9)
                        m.SetVertex(v, new Vector3d(p.x, p.y, 1e6 + (p.z - 1e6) * 0.5));
                }
            }
            double e = SolidBooleanPipeline.RoundingVolumeThinChangeOf(rt, thin, out parts);
            Assert.Equal(1, parts);
            Assert.True(e < 0);
            Assert.Equal(0.0, SolidBooleanPipeline.RoundingVolumeThinChangeOf(rk, thick, out parts));
            Assert.Equal(0, parts);
            // a power of two scales the change by the cube of the factor, bit for bit
            foreach (Similarity s in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                DMesh3 m = Invariance.Transformed(far(1e-8), s);
                var r = Recorded(m, 0);
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    if (p.z > s.Scale * (1e6 + 1e-9))
                        m.SetVertex(v, new Vector3d(p.x, p.y, s.Scale * 1e6 + (p.z - s.Scale * 1e6) * 0.5));
                }
                double scaled = SolidBooleanPipeline.RoundingVolumeThinChangeOf(r, m, out parts);
                Assert.Equal(1, parts);
                Assert.True(Same(e * s.Scale * s.Scale * s.Scale, scaled), s.Name);
            }
        }

        // the plates (their loss of 0.71 % is refused) with a separate thin box that the rounding leaves alone: the thin box must not hide the loss
        // (a pooled allowance of length x area of the thin parts did: 100 times the loss for 1 x 1 x 1e-6, 0.029 for 10 x 10)
        [Theory]
        [InlineData(1.0, 0.0)]
        [InlineData(1.0, 1000.0)]
        [InlineData(10.0, 0.0)]
        [InlineData(10.0, 1000.0)]
        public void ThePlates_WithASeparateThinSlab_AreRefusedLikeThePlatesAlone(double size, double at)
        {
            var inputs = Plates();
            inputs.Add(Box(new Vector3d(at + 20, at + 20, at + 20), new Vector3d(at + 20 + size, at + 20 + size, at + 20 + 1e-6)));
            var p = new SolidBooleanPipeline(inputs, U) { Verification = V.RoundingVolume };
            Assert.False(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(Status.VerificationFailed, p.Status);
            Assert.Null(p.Result);
            Assert.Contains("RoundingVolume", p.Error);
            // the plates' own loss is above the limit of the whole, and the box's part is taken out as a part that did not move
            string step = VerificationStep(p).Change;
            Assert.Contains("1 parts thinner", step);
            Assert.True(p.Report.RoundingVolumeChange > 1e-3, "change " + p.Report.RoundingVolumeChange);
            // with the limit above the plates' loss it passes
            var ok = new SolidBooleanPipeline(inputs, U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = 0.05 };
            Assert.True(ok.Compute(), ok.Status + " " + ok.Error);
            if (at == 0)
                AssertSolid(ok.Result);
            else {
                // (far apart, the plates' triangles are degenerate against the box of everything: an evaluator's reading, not of this check)
                Assert.True(ok.Result.TriangleCount > 0);
                Assert.True(Invariance.AllFinite(ok.Result));
            }
        }

        [Fact]
        public void TheThinSlab_AloneOrNextToThePlates_DoesNotChangeTheVerdictOnThePlates()
        {
            // the change of the whole run without the slab and with it differ by the slab's own volume only
            var alone = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = Inf };
            Assert.True(alone.Compute(), alone.Error);
            var inputs = Plates();
            inputs.Add(Box(new Vector3d(20, 20, 20), new Vector3d(30, 30, 20 + 1e-6)));
            var with = new SolidBooleanPipeline(inputs, U) { Verification = V.RoundingVolume, MaxRoundingVolumeChange = Inf };
            Assert.True(with.Compute(), with.Error);
            double lost = alone.Report.RoundingVolumeBefore - alone.Report.RoundingVolumeAfter;
            double lostWith = with.Report.RoundingVolumeBefore - with.Report.RoundingVolumeAfter;
            Assert.True(Math.Abs(lost - lostWith) <= 1e-2 * Math.Abs(lost), lost + " " + lostWith + " " + alone.Report.RoundingVolumeAfter + " " + with.Report.RoundingVolumeAfter + " " + with.Report.RoundingVolumeBefore);
        }

        [Theory]
        [MemberData(nameof(Frames))]
        public void ATinyResult_IsJudgedTheSameInEveryFrame_AndExactlyTheSameUnderPowersOfTwo(string name)
        {
            Similarity s = Similarity.ByName(name);
            Func<Similarity, SolidBooleanPipeline> run = sim => {
                var p = new SolidBooleanPipeline(PlateSlab(0, 1e-7, 1e-12, null).Select(m => Invariance.Transformed(m, sim)).ToList(), D) { Verification = V.RoundingVolume };
                p.Compute();
                return p;
            };
            var one = run(Similarity.Identity);
            var other = run(s);
            Assert.Equal(Status.Success, one.Status);
            Assert.Equal(Status.Success, other.Status);
            if (s.IsExact && s.Scale != 1) {
                Assert.True(Same(one.Report.RoundingVolumeChange, other.Report.RoundingVolumeChange), s.Name);
                Assert.True(Same(one.Report.RoundingVolumeBefore * s.Scale * s.Scale * s.Scale, other.Report.RoundingVolumeBefore), s.Name);
            }
        }

        [Fact]
        public void TheAllowanceRun_ComputedTwiceOnOneInstance_GivesTheSame()
        {
            var p = new SolidBooleanPipeline(PlateSlab(0, 1e-7, 1e-12, null), D) { Verification = V.RoundingVolume };
            Assert.True(p.Compute(), p.Error);
            double change = p.Report.RoundingVolumeChange;
            string step = VerificationStep(p).Change;
            DMesh3 first = new DMesh3(p.Result);
            for (int k = 0; k < 2; ++k) {
                Assert.True(p.Compute(), p.Error);
                Assert.True(Same(change, p.Report.RoundingVolumeChange));
                Assert.Equal(step, VerificationStep(p).Change);
                Assert.Equal("", Invariance.Difference(first, p.Result));
                Assert.Equal(1, p.Report.Route.Count(r => r.Name == "Verification"));
            }
            // the mode off in between: the run without it, and then again as before
            p.Verification = V.None;
            Assert.True(p.Compute(), p.Error);
            Assert.DoesNotContain("Verification", Names(p));
            Assert.Equal(0.0, p.Report.RoundingVolumeChange);
            p.Verification = V.RoundingVolume;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(step, VerificationStep(p).Change);
        }

        [Fact]
        public void AResultThickerThanTheBound_WhoseVolumeTheRoundingMoves_IsStillRefused()
        {
            // the thin plates lose 0.7 % and are refused: no part of the result is thinner than its own length, so nothing is taken out
            var p = new SolidBooleanPipeline(Plates(), U) { Verification = V.RoundingVolume };
            Assert.False(p.Compute());
            Assert.Equal(Status.VerificationFailed, p.Status);
            Assert.Contains("floor", p.Error);
            Assert.Contains("0 parts thinner than", VerificationStep(p).Change);
        }

        [Fact]
        public void ANonFiniteVolume_IsAFailedVerification_NotAPass()
        {
            double change;
            double nan = double.NaN, inf = double.PositiveInfinity;
            foreach (double limit in new[] { 1e-3, inf }) {
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(nan, 1, limit, 0, out change), "before NaN");
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(1, nan, limit, 0, out change), "after NaN");
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(inf, inf, limit, 0, out change), "inf - inf");
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(1, double.NegativeInfinity, limit, 0, out change), "after -inf");
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(1, 2, limit, nan, out change), "floor NaN");
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(1, 2, limit, inf, out change), "floor inf");
                Assert.True(SolidBooleanPipeline.RoundingVolumeExceeds(double.MaxValue, -double.MaxValue, limit, 0, out change), "dV overflows");
            }
            // a finite verdict is as before
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(1, 1, 1e-3, 0, out change));
            Assert.False(SolidBooleanPipeline.RoundingVolumeExceeds(2, 3, inf, 0, out change));
            // the pipeline does not get that far with coordinates whose volume overflows (2^400): the boolean refuses, which is what the caller sees
            // (BooleanFailed), never a pass
            double k = Math.Pow(2, 400);
            var p = new SolidBooleanPipeline(Box(Vector3d.Zero, new Vector3d(4 * k, 4 * k, 4 * k)), Box(new Vector3d(k, k, k), new Vector3d(6 * k, 6 * k, 6 * k)), U)
                { Verification = V.RoundingVolume, MaxRoundingVolumeChange = inf };
            Assert.False(p.Compute());
            Assert.NotEqual(Status.Success, p.Status);
            Assert.Null(p.Result);
        }

        [Fact]
        public void TheSampleLoop_ChecksForCancellation()
        {
            var sphere = Sphere(Vector3d.Zero, 1, 20);
            Assert.True(sphere.TriangleCount > 1024);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Null(SolidBooleanPipeline.InventedSurfaceOf(sphere, new List<DMesh3> { sphere }, 1e-3, cts.Token));
            Assert.NotNull(SolidBooleanPipeline.InventedSurfaceOf(sphere, new List<DMesh3> { sphere }, 1e-3, CancellationToken.None));
        }

        [Fact]
        public void TheMeasurement_IsOfTheResultAfterMinThickness_NotBeforeIt()
        {
            // a flat bucket far from the cube: an open input that the repair closes with a lid (invented), and MinThickness removes the thin solid that it
            // makes. The result is the cube: nothing is invented. Measured before MinThickness, the lid would count
            var flat = Bucket(10, 14, 10, 10.001);
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), flat }, U) { InputRepair = Repair, MinThickness = 0.01, Verification = V.NoInventedSurface };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Single(p.Report.RepairedInputs);
            Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(64.0, Volume(p.Result), 9);
            Assert.Equal(0.0, p.Report.InventedSurfaceShare);
        }

        // ---- Verification v2 (1B): ExactQuantities, TopologyPreservation, InputIntentGuard, Standard ----

        [Fact]
        public void ExactQuantities_BracketsExactVolumeAndArea_OnBoxesSpheresAndUtmFrame()
        {
            // 1. Exact analytical box difference: [0,4]^3 \ [1,6]^3 = 64 - 27 = 37, area = 6*16 = 96
            var boxDiff = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), D) { Verification = V.ExactQuantities };
            Assert.True(boxDiff.Compute(), boxDiff.Status + " " + boxDiff.Error);
            AssertSolid(boxDiff.Result);
            Assert.True(boxDiff.Report.ExactVolumeMin <= 37.0 && 37.0 <= boxDiff.Report.ExactVolumeMax,
                $"expected 37 inside [{boxDiff.Report.ExactVolumeMin:R}, {boxDiff.Report.ExactVolumeMax:R}]");
            Assert.True(boxDiff.Report.ExactAreaMin <= 96.0 && 96.0 <= boxDiff.Report.ExactAreaMax,
                $"expected 96 inside [{boxDiff.Report.ExactAreaMin:R}, {boxDiff.Report.ExactAreaMax:R}]");
            Assert.True(boxDiff.Report.ExactVolumeMax - boxDiff.Report.ExactVolumeMin < 1e-11 * 37.0);
            Assert.True(boxDiff.Report.ExactAreaMax - boxDiff.Report.ExactAreaMin < 1e-11 * 96.0);

            // 2. UTM 1e7 offset: 2x2x2 cube minus 1x1x1 corner cube -> exact volume 7.0, area 24.0
            Vector3d utm = new Vector3d(600000.0, 5700000.0, 100.0);
            var utmA = Box(utm, utm + new Vector3d(2, 2, 2));
            var utmB = Box(utm + new Vector3d(1, 1, 1), utm + new Vector3d(3, 3, 3));
            var utmOp = new SolidBooleanPipeline(utmA, utmB, D) { Verification = V.ExactQuantities };
            Assert.True(utmOp.Compute(), utmOp.Status + " " + utmOp.Error);
            AssertSolid(utmOp.Result);
            Assert.True(utmOp.Report.ExactVolumeMin <= 7.0 && 7.0 <= utmOp.Report.ExactVolumeMax,
                $"UTM: 7 not in [{utmOp.Report.ExactVolumeMin:R}, {utmOp.Report.ExactVolumeMax:R}]");
            Assert.True(utmOp.Report.ExactAreaMin <= 24.0 && 24.0 <= utmOp.Report.ExactAreaMax,
                $"UTM: 24 not in [{utmOp.Report.ExactAreaMin:R}, {utmOp.Report.ExactAreaMax:R}]");
            Assert.True(utmOp.Report.ExactVolumeMax - utmOp.Report.ExactVolumeMin < 1e-11 * 7.0);
            Assert.True(utmOp.Report.ExactAreaMax - utmOp.Report.ExactAreaMin < 1e-11 * 24.0);

            // 3. Curved spheres: interval must bracket exact BooleanVolumeGradient volume with relative width < 1e-11
            var spA = Sphere(Vector3d.Zero, 1.0, 6);
            var spB = Sphere(new Vector3d(0.6, 0.25, 0.125), 1.0, 6);
            var spOp = new SolidBooleanPipeline(spA, spB, U) { Verification = V.ExactQuantities };
            Assert.True(spOp.Compute(), spOp.Status + " " + spOp.Error);
            AssertSolid(spOp.Result);
            var refEb = new ExactMeshBoolean(spA, spB, U) { RetainClassification = true };
            Assert.True(refEb.Compute(), refEb.Error);
            var bvg = new BooleanVolumeGradient(refEb);
            Assert.True(bvg.Compute(), bvg.Error);
            Assert.True(spOp.Report.ExactVolumeMin <= bvg.Volume + bvg.VolumeBound && spOp.Report.ExactVolumeMax >= bvg.Volume - bvg.VolumeBound);
            Assert.True(spOp.Report.ExactVolumeMax - spOp.Report.ExactVolumeMin < 1e-11 * Math.Abs(bvg.Volume));

            // 4. Thin plates whose rounding moves 0.7% volume must be refused by ExactQuantities alone
            var plates = new SolidBooleanPipeline(Plates(), U) { Verification = V.ExactQuantities };
            Assert.False(plates.Compute());
            Assert.Equal(Status.VerificationFailed, plates.Status);
            Assert.Contains("ExactQuantities", plates.Error);
        }

        [Fact]
        public void ExactQuantities_SimilarityAll_ExactScalesBitIdenticalAndTight()
        {
            DMesh3 a0 = CubeAt(0, 2), b0 = CubeAt(1, 3);
            var refP = new SolidBooleanPipeline(a0, b0, D) { Verification = V.ExactQuantities };
            Assert.True(refP.Compute(), refP.Error);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 Map(DMesh3 m) { var c = new DMesh3(m); foreach (int v in c.VertexIndices()) c.SetVertex(v, sim.Apply(c.GetVertex(v))); return c; }
                var p = new SolidBooleanPipeline(Map(a0), Map(b0), D) { Verification = V.ExactQuantities };
                Assert.True(p.Compute(), sim.Name + ": " + p.Status + " " + p.Error);
                AssertSolid(p.Result);
                double vMag = Math.Max(Math.Abs(p.Report.ExactVolumeMin), Math.Abs(p.Report.ExactVolumeMax));
                double aMag = Math.Max(Math.Abs(p.Report.ExactAreaMin), Math.Abs(p.Report.ExactAreaMax));
                Assert.True(p.Report.ExactVolumeMin <= p.Report.ExactVolumeMax, sim.Name);
                Assert.True(p.Report.ExactAreaMin >= 0 && p.Report.ExactAreaMin <= p.Report.ExactAreaMax, sim.Name);
                Assert.True(p.Report.ExactVolumeMax - p.Report.ExactVolumeMin < 1e-11 * vMag, sim.Name);
                Assert.True(p.Report.ExactAreaMax - p.Report.ExactAreaMin < 1e-11 * aMag, sim.Name);
                if (sim.IsExact) {
                    double s3 = sim.Scale * sim.Scale * sim.Scale;
                    double s2 = sim.Scale * sim.Scale;
                    Assert.True(Same(refP.Report.ExactVolumeMin * s3, p.Report.ExactVolumeMin), sim.Name + " vMin");
                    Assert.True(Same(refP.Report.ExactVolumeMax * s3, p.Report.ExactVolumeMax), sim.Name + " vMax");
                    Assert.True(Same(refP.Report.ExactAreaMin * s2, p.Report.ExactAreaMin), sim.Name + " aMin");
                    Assert.True(Same(refP.Report.ExactAreaMax * s2, p.Report.ExactAreaMax), sim.Name + " aMax");
                }
            }
        }

        [Fact]
        public void TopologyPreservation_PassesMultiShellCavityTorusAndMinThickness_AndCatchesInjectedDefects()
        {
            // 1. Hollow box (multi-shell cavity: outer chi=2 (+1) + inner chi=2 (-1) -> 2 components, total chi=4)
            var cavity = new SolidBooleanPipeline(Cube(4), Box(new Vector3d(1, 1, 1), new Vector3d(3, 3, 3)), D) {
                Verification = V.TopologyPreservation
            };
            Assert.True(cavity.Compute(), cavity.Status + " " + cavity.Error);
            AssertSolid(cavity.Result);
            Assert.Equal(2, cavity.Report.ExactComponents);
            Assert.Equal(4, cavity.Report.ExactEulerCharacteristic);
            Assert.Equal(2, cavity.Report.RoundedComponents);
            Assert.Equal(4, cavity.Report.RoundedEulerCharacteristic);
            Assert.Equal(2, cavity.Report.ReducedComponents);
            Assert.Equal(4, cavity.Report.ReducedEulerCharacteristic);

            // 2. Genus-1 solid (box with through-hole -> 1 component, Euler chi = 0)
            var torus = new SolidBooleanPipeline(Cube(4), Box(new Vector3d(1, 1, -1), new Vector3d(3, 3, 5)), D) {
                Verification = V.TopologyPreservation
            };
            Assert.True(torus.Compute(), torus.Status + " " + torus.Error);
            AssertSolid(torus.Result);
            Assert.Equal(1, torus.Report.ExactComponents);
            Assert.Equal(0, torus.Report.ExactEulerCharacteristic);
            Assert.Equal(1, torus.Report.RoundedComponents);
            Assert.Equal(0, torus.Report.RoundedEulerCharacteristic);
            Assert.Equal(1, torus.Report.ReducedComponents);
            Assert.Equal(0, torus.Report.ReducedEulerCharacteristic);

            // 3. MinThickness drops a thin component cleanly (2 -> 2 rounded -> 1 reduced)
            var thinSlab = Box(new Vector3d(10, 10, 10), new Vector3d(14, 14, 10.001));
            var withThin = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), thinSlab }, U) {
                MinThickness = 0.01, Verification = V.TopologyPreservation
            };
            Assert.True(withThin.Compute(), withThin.Status + " " + withThin.Error);
            AssertSolid(withThin.Result);
            Assert.Equal(2, withThin.Report.ExactComponents);
            Assert.Equal(2, withThin.Report.RoundedComponents);
            Assert.Equal(1, withThin.Report.ReducedComponents);
            Assert.Equal(2, withThin.Report.ReducedEulerCharacteristic);

            // 3b. MinThickness drops a HOLLOW thin-walled solid (2 shells: outer + cavity, 1 RemovedThinSolids entry with Shells == 2)
            DMesh3 hollowThin = Box(new Vector3d(10, 10, 10), new Vector3d(14, 14, 14));
            DMesh3 hollowCavity = Box(new Vector3d(10.002, 10.002, 10.002), new Vector3d(13.998, 13.998, 13.998));
            hollowCavity.ReverseOrientation();
            MeshEditor.Append(hollowThin, hollowCavity);
            var withHollowThin = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), hollowThin }, U) {
                MinThickness = 0.01, Verification = V.TopologyPreservation
            };
            Assert.True(withHollowThin.Compute(), withHollowThin.Status + " " + withHollowThin.Error);
            AssertSolid(withHollowThin.Result);
            Assert.Single(withHollowThin.Report.RemovedThinSolids);
            Assert.Equal(2, withHollowThin.Report.RemovedThinSolids[0].Shells);
            Assert.Equal(3, withHollowThin.Report.ExactComponents);
            Assert.Equal(3, withHollowThin.Report.RoundedComponents);
            Assert.Equal(1, withHollowThin.Report.ReducedComponents);
            Assert.Equal(2, withHollowThin.Report.ReducedEulerCharacteristic);

            // 4. Documented RoundingRemoved (Slivers) passes TopologyPreservation
            var slivers = new SolidBooleanPipeline(Slivers(), D) { Verification = V.TopologyPreservation };
            Assert.True(slivers.Compute(), slivers.Status + " " + slivers.Error);
            Assert.True(slivers.Report.ExactComponents > 0);
            Assert.Equal(0, slivers.Report.RoundedComponents);
            Assert.Equal(slivers.Report.ExactComponents, slivers.Report.RoundingRemoved.Count);

            // 5. Seeded topology defects must be rejected by TopologyMatches
            var sBox = SolidBooleanPipeline.MeasureTopology(Cube(4));
            var sTorus = SolidBooleanPipeline.MeasureTopology(torus.Result);
            var sCavity = SolidBooleanPipeline.MeasureTopology(cavity.Result);
            DMesh3 flippedBox = Cube(4);
            flippedBox.ReverseOrientation();
            var sFlipped = SolidBooleanPipeline.MeasureTopology(flippedBox);
            var sOpen = SolidBooleanPipeline.MeasureTopology(Bucket(0, 4, 0, 4));
            string why;
            Assert.True(SolidBooleanPipeline.TopologyMatches(sBox, sBox, 0, out why), why);
            Assert.False(SolidBooleanPipeline.TopologyMatches(sBox, sTorus, 0, out why));
            Assert.Contains("Euler", why);
            Assert.False(SolidBooleanPipeline.TopologyMatches(sCavity, sBox, 0, out why));
            Assert.Contains("component count", why);
            Assert.False(SolidBooleanPipeline.TopologyMatches(sBox, sFlipped, 0, out why));
            Assert.Contains("volumeSign", why);
            Assert.False(SolidBooleanPipeline.TopologyMatches(sBox, sOpen, 0, out why));
            Assert.Contains("Euler", why);
            // Documented removal of 1 component from a 2-component mesh must still reject if the surviving component changed genus
            Assert.False(SolidBooleanPipeline.TopologyMatches(sCavity, sTorus, 1, out why));
            Assert.Contains("does not match", why);
            // Sub-multiset match where the removed component sorts BEFORE the surviving component (exercises while-loop advance)
            var sMixed = new SolidBooleanPipeline.TopologySnapshot {
                Components = 2, Euler = 2,
                Parts = new[] {
                    new SolidBooleanPipeline.TopologyComponent { Closed = true, Euler = 0, VolumeSign = 1 },
                    new SolidBooleanPipeline.TopologyComponent { Closed = true, Euler = 2, VolumeSign = 1 }
                }
            };
            Assert.True(SolidBooleanPipeline.TopologyMatches(sMixed, sBox, 1, out why), why);
            // TopologyComponent.CompareTo discriminating strictly on Closed when Euler and VolumeSign are equal
            var closedOne = new SolidBooleanPipeline.TopologyComponent { Closed = true, Euler = 1, VolumeSign = 0 };
            var openOne = new SolidBooleanPipeline.TopologyComponent { Closed = false, Euler = 1, VolumeSign = 0 };
            Assert.True(openOne.CompareTo(closedOne) < 0);
            Assert.True(closedOne.CompareTo(openOne) > 0);
            Assert.False(SolidBooleanPipeline.TopologyMatches(
                new SolidBooleanPipeline.TopologySnapshot { Components = 1, Euler = 1, Parts = new[] { closedOne } },
                new SolidBooleanPipeline.TopologySnapshot { Components = 1, Euler = 1, Parts = new[] { openOne } }, 0, out why));
            Assert.Contains("closed", why);
        }

        [Fact]
        public void InputIntentGuard_RefusesOutwardCavityAndEmptyAfterRounding_AllowsValidAndEmptyExact()
        {
            // 1. Audit F2: outer box + outward-pointing inner cavity box in one input (winding 2 inside cavity)
            DMesh3 outerAndOutwardCavity = Cube(4);
            DMesh3 inner = Box(new Vector3d(1, 1, 1), new Vector3d(3, 3, 3)); // outward normals -> winding +2 inside!
            MeshEditor.Append(outerAndOutwardCavity, inner);
            DMesh3 cutter = Box(new Vector3d(2, -1, -1), new Vector3d(5, 5, 5));

            var plainF2 = new SolidBooleanPipeline(outerAndOutwardCavity, cutter, D);
            Assert.True(plainF2.Compute(), plainF2.Error);
            Assert.Contains(0, plainF2.Report.InputsWithWindingOutsideZeroOne);

            var guardF2 = new SolidBooleanPipeline(outerAndOutwardCavity, cutter, D) { Verification = V.InputIntentGuard };
            Assert.False(guardF2.Compute());
            Assert.Equal(Status.VerificationFailed, guardF2.Status);
            Assert.Null(guardF2.Result);
            Assert.Contains("InputIntentGuard", guardF2.Error);
            Assert.Contains("winding number outside {0, 1}", guardF2.Error);

            // 2. Audit F1: Slivers produce EmptyAfterRounding -> refused by InputIntentGuard
            var plainF1 = new SolidBooleanPipeline(Slivers(), D);
            Assert.True(plainF1.Compute(), plainF1.Error);
            Assert.Equal(SolidBooleanResultKind.EmptyAfterRounding, plainF1.Report.ResultKind);

            var guardF1 = new SolidBooleanPipeline(Slivers(), D) { Verification = V.InputIntentGuard };
            Assert.False(guardF1.Compute());
            Assert.Equal(Status.VerificationFailed, guardF1.Status);
            Assert.Null(guardF1.Result);
            Assert.Contains("EmptyAfterRounding", guardF1.Error);

            // 3. Legitimate EmptyExact (disjoint intersection) and valid solid both pass InputIntentGuard
            var disjoint = new SolidBooleanPipeline(CubeAt(0, 1), CubeAt(5, 6), I) { Verification = V.InputIntentGuard };
            Assert.True(disjoint.Compute(), disjoint.Status + " " + disjoint.Error);
            Assert.Equal(SolidBooleanResultKind.EmptyExact, disjoint.Report.ResultKind);

            var valid = new SolidBooleanPipeline(CubeAt(0, 4), CubeAt(1, 6), D) { Verification = V.InputIntentGuard };
            Assert.True(valid.Compute(), valid.Status + " " + valid.Error);
            AssertSolid(valid.Result);
        }

        [Fact]
        public void VerificationStandard_BitIdenticalToNone_DeterministicAndReusable()
        {
            Assert.Equal(V.NoInventedSurface | V.RoundingVolume | V.ExactQuantities | V.TopologyPreservation, V.Standard);

            var spA = Sphere(Vector3d.Zero, 1.0, 5);
            var spB = Sphere(new Vector3d(0.6, 0.25, 0.125), 1.0, 5);

            var none = new SolidBooleanPipeline(spA, spB, U) { TargetFraction = 0.5 };
            Assert.True(none.Compute(), none.Error);

            var std = new SolidBooleanPipeline(spA, spB, U) { TargetFraction = 0.5, Verification = V.Standard };
            Assert.True(std.Compute(), std.Status + " " + std.Error);
            AssertSolid(std.Result);
            Assert.Equal("", Invariance.Difference(none.Result, std.Result));
            Assert.Equal(1, std.Report.ExactComponents);
            Assert.Equal(2, std.Report.ExactEulerCharacteristic);
            Assert.Equal(1, std.Report.ReducedComponents);
            Assert.Equal(2, std.Report.ReducedEulerCharacteristic);
            Assert.True(std.Report.ExactVolumeMin > 0 && std.Report.ExactVolumeMin <= std.Report.ExactVolumeMax);
            Assert.True(std.Report.ExactAreaMin > 0 && std.Report.ExactAreaMin <= std.Report.ExactAreaMax);

            // Serial vs parallel bit-identical result and report fields
            var serial = new SolidBooleanPipeline(spA, spB, U) { TargetFraction = 0.5, Verification = V.Standard };
            gParallel.SerialOnThisThread = true;
            try { Assert.True(serial.Compute(), serial.Error); } finally { gParallel.SerialOnThisThread = false; }
            Assert.Equal("", Invariance.Difference(std.Result, serial.Result));
            Assert.True(Same(std.Report.ExactVolumeMin, serial.Report.ExactVolumeMin));
            Assert.True(Same(std.Report.ExactVolumeMax, serial.Report.ExactVolumeMax));
            Assert.True(Same(std.Report.ExactAreaMin, serial.Report.ExactAreaMin));
            Assert.True(Same(std.Report.ExactAreaMax, serial.Report.ExactAreaMax));

            // Reuse: toggling Verification off on the same instance resets all new report fields to 0
            std.Verification = V.None;
            Assert.True(std.Compute(), std.Error);
            Assert.Equal("", Invariance.Difference(none.Result, std.Result));
            Assert.Equal(0.0, std.Report.ExactVolumeMin);
            Assert.Equal(0.0, std.Report.ExactVolumeMax);
            Assert.Equal(0.0, std.Report.ExactAreaMin);
            Assert.Equal(0.0, std.Report.ExactAreaMax);
            Assert.Equal(0, std.Report.ExactComponents);
            Assert.Equal(0, std.Report.ExactEulerCharacteristic);
            Assert.Equal(0, std.Report.RoundedComponents);
            Assert.Equal(0, std.Report.RoundedEulerCharacteristic);
            Assert.Equal(0, std.Report.ReducedComponents);
            Assert.Equal(0, std.Report.ReducedEulerCharacteristic);
        }

        [Fact]
        public void ExactQuantities_ExactFallbackAgreesWithFastPath_AndHandlesDistantDisjointSolids()
        {
            // Fast path vs exact BigInteger fallback must overlap tightly on curved spheres
            var spA = Sphere(Vector3d.Zero, 1.0, 5);
            var spB = Sphere(new Vector3d(0.6, 0.25, 0.125), 1.0, 5);
            var eb = new ExactMeshBoolean(spA, spB, U) { RecordCertificate = true };
            Assert.True(eb.Compute(), eb.Error);
            double fvLo, fvHi, faLo, faHi, evLo, evHi, eaLo, eaHi;
            Assert.True(SolidBooleanPipeline.ComputeExactQuantities(eb, out fvLo, out fvHi, out faLo, out faHi, false));
            Assert.True(SolidBooleanPipeline.ComputeExactQuantities(eb, out evLo, out evHi, out eaLo, out eaHi, true));
            Assert.True(fvLo <= evHi && evLo <= fvHi, $"volume intervals disjoint: [{fvLo:R},{fvHi:R}] vs [{evLo:R},{evHi:R}]");
            Assert.True(faLo <= eaHi && eaLo <= faHi, $"area intervals disjoint: [{faLo:R},{faHi:R}] vs [{eaLo:R},{eaHi:R}]");
            Assert.True(evHi - evLo < 1e-11 * Math.Abs(evLo));
            Assert.True(eaHi - eaLo < 1e-11 * Math.Abs(eaLo));

            // Two small unit boxes 1e6 apart: Difference keeps only the far box; local-origin shift keeps interval < 1e-11
            var farA = Box(new Vector3d(1e6, 1e6, 1e6), new Vector3d(1e6 + 1, 1e6 + 1, 1e6 + 1));
            var nearB = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            var farDiff = new SolidBooleanPipeline(farA, nearB, D) { Verification = V.ExactQuantities };
            Assert.True(farDiff.Compute(), farDiff.Status + " " + farDiff.Error);
            AssertSolid(farDiff.Result);
            Assert.True(farDiff.Report.ExactVolumeMin <= 1.0 && 1.0 <= farDiff.Report.ExactVolumeMax);
            Assert.True(farDiff.Report.ExactAreaMin <= 6.0 && 6.0 <= farDiff.Report.ExactAreaMax);
            Assert.True(farDiff.Report.ExactVolumeMax - farDiff.Report.ExactVolumeMin < 1e-11);
            Assert.True(farDiff.Report.ExactAreaMax - farDiff.Report.ExactAreaMin < 1e-11);

            // Distant multi-component Union: Box(0,1) U Box(1e5, 1e5+1) (clean under NotCleanReason) and 1e8 separation (where global-center MeasureInput fails by ~1e7!)
            var farUnion = new SolidBooleanPipeline(
                Box(Vector3d.Zero, new Vector3d(1, 1, 1)),
                Box(new Vector3d(1e5, 1e5, 1e5), new Vector3d(1e5 + 1, 1e5 + 1, 1e5 + 1)), U) {
                Verification = V.ExactQuantities | V.TopologyPreservation
            };
            Assert.True(farUnion.Compute(), farUnion.Status + " " + farUnion.Error);
            AssertSolid(farUnion.Result);
            Assert.True(farUnion.Report.ExactVolumeMin <= 2.0 && 2.0 <= farUnion.Report.ExactVolumeMax,
                $"distant union 1e5: 2 not in [{farUnion.Report.ExactVolumeMin:R}, {farUnion.Report.ExactVolumeMax:R}]");
            Assert.True(farUnion.Report.ExactAreaMin <= 12.0 && 12.0 <= farUnion.Report.ExactAreaMax);
            Assert.True(farUnion.Report.ExactVolumeMax - farUnion.Report.ExactVolumeMin < 1e-11 * 2.0);
            Assert.Equal(2, farUnion.Report.ExactComponents);
            Assert.Equal(4, farUnion.Report.ExactEulerCharacteristic);

            var farUnion1e8 = new SolidBooleanPipeline(
                Box(Vector3d.Zero, new Vector3d(1, 1, 1)),
                Box(new Vector3d(1e8, 1e8, 1e8), new Vector3d(1e8 + 1, 1e8 + 1, 1e8 + 1)), U) {
                Verification = V.ExactQuantities | V.TopologyPreservation
            };
            Assert.True(farUnion1e8.Compute(), farUnion1e8.Status + " " + farUnion1e8.Error);
            Assert.True(farUnion1e8.Report.ExactVolumeMin <= 2.0 && 2.0 <= farUnion1e8.Report.ExactVolumeMax,
                $"distant union 1e8: 2 not in [{farUnion1e8.Report.ExactVolumeMin:R}, {farUnion1e8.Report.ExactVolumeMax:R}]");
            Assert.True(farUnion1e8.Report.ExactVolumeMax - farUnion1e8.Report.ExactVolumeMin < 1e-11 * 2.0);
            Assert.Equal(2.0, SolidBooleanPipeline.ComponentShiftedVolume(farUnion1e8.Result), 12);
        }

        [Fact]
        public void VerificationV2_BoundaryCases_EmptySubnormalClampAndReportOnly()
        {
            // 1. EmptyExact under ExactQuantities | TopologyPreservation
            var emptyExact = new SolidBooleanPipeline(CubeAt(0, 1), CubeAt(5, 6), I) {
                Verification = V.ExactQuantities | V.TopologyPreservation
            };
            Assert.True(emptyExact.Compute(), emptyExact.Status + " " + emptyExact.Error);
            Assert.Equal(0.0, emptyExact.Report.ExactVolumeMin);
            Assert.Equal(0.0, emptyExact.Report.ExactAreaMax);
            Assert.Equal(0, emptyExact.Report.ExactComponents);

            // 2. ThinInputs = TreatAsEmpty early exit (emptyResult without running boolean)
            var thinEmpty = new SolidBooleanPipeline(Box(Vector3d.Zero, new Vector3d(1, 1, 1e-4)), Cube(2), I) {
                MinThickness = 0.01, ThinInputs = SolidBooleanPipeline.ThinInputModes.TreatAsEmpty,
                Verification = V.ExactQuantities | V.TopologyPreservation
            };
            Assert.True(thinEmpty.Compute(), thinEmpty.Status + " " + thinEmpty.Error);
            Assert.Equal(0, thinEmpty.Result.TriangleCount);
            Assert.Equal(0.0, thinEmpty.Report.ExactVolumeMin);
            Assert.Equal(0, thinEmpty.Report.ExactComponents);

            // 3. Report-only (MaxRoundingVolumeChange = PositiveInfinity) passes Plates() under ExactQuantities
            var platesReportOnly = new SolidBooleanPipeline(Plates(), U) {
                Verification = V.ExactQuantities, MaxRoundingVolumeChange = Inf
            };
            Assert.True(platesReportOnly.Compute(), platesReportOnly.Status + " " + platesReportOnly.Error);
            AssertSolid(platesReportOnly.Result);
            Assert.True(platesReportOnly.Report.ExactVolumeMin > 0 && platesReportOnly.Report.ExactVolumeMin <= platesReportOnly.Report.ExactVolumeMax);

            // 4. Helper edge cases: CeilLog2 (subnormal + normal + MaxValue), ClampExp, null/non-finite inputs, ComponentShiftedVolume with unused vertex
            Assert.Equal(-1073, SolidBooleanPipeline.CeilLog2(double.Epsilon));
            Assert.Equal(-1070, SolidBooleanPipeline.CeilLog2(8.0 * double.Epsilon));
            Assert.Equal(1, SolidBooleanPipeline.CeilLog2(1.0));
            Assert.Equal(1024, SolidBooleanPipeline.CeilLog2(double.MaxValue));
            Assert.Equal(-4096, SolidBooleanPipeline.ClampExp(-10000));
            Assert.Equal(4096, SolidBooleanPipeline.ClampExp(10000));
            Assert.Equal(42, SolidBooleanPipeline.ClampExp(42));
            Assert.Equal(0, SolidBooleanPipeline.MeasureTopology(null).Components);
            Assert.Equal(0.0, SolidBooleanPipeline.ComponentShiftedVolume(null));
            double v0, v1, a0, a1;
            Assert.False(SolidBooleanPipeline.ComputeExactQuantities(null, out v0, out v1, out a0, out a1));
            // Unused vertex at 1e150 must not affect ComponentShiftedVolume
            DMesh3 boxWithUnused = Cube(2);
            boxWithUnused.AppendVertex(new Vector3d(1e150, 1e150, 1e150));
            Assert.Equal(8.0, SolidBooleanPipeline.ComponentShiftedVolume(boxWithUnused), 12);
            // Non-finite vertex produces VolumeSign == 0 in MeasureTopology without throwing
            DMesh3 badMesh = Cube(2);
            badMesh.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var badTopo = SolidBooleanPipeline.MeasureTopology(badMesh);
            Assert.Equal(1, badTopo.Components);
            Assert.Equal(0, badTopo.Parts[0].VolumeSign);
        }
    }

    static class VerificationTestExtensions
    {
        public static T Also<T>(this T x, Action<T> f) { f(x); return x; }
    }
}
