using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline and thin solids: a strict refusal whose only cause is components thinner than the rounding
    /// bound keeps the result (Report.ThinComponentsKept), and MinThickness removes solids thinner than it (an outer shell
    /// with its cavities; Report.RemovedThinSolids).
    /// </summary>
    public class SolidBooleanPipelineThinSolidTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, D = ExactMeshBoolean.Operations.Difference;

        // ---- the layers: two faceted CAD layers at UTM-like coordinates (y about 6.6e6), each about a millimetre thick
        // over 32 m x 32 m and 64 m x 64 m (two nearly coincident sheets each). Derived from a real case, translated by
        // integers (exact; every coordinate stays in its binade). Their union is thinner than the strict rounding bound
        // (1e-5 of its diagonal of 72 m): the strict boolean refuses it although the rounding left no defect ----

        static readonly double[] Layer0Vertices = {
            2227840.0000000005, 6633920, 78.06635000000006,
            2227872.0000000005, 6633920, 78.24112000000005,
            2227872.0000000005, 6633888.000000001, 78.32313000000003,
            2227840.0000000005, 6633888.000000001, 78.15238000000004,
            2227872.0000000005, 6633888.000000001, 78.32414000000004,
            2227840.0000000005, 6633920, 78.06779000000004,
            2227840.0000000005, 6633888.000000001, 78.15381000000004,
            2227808.0000000005, 6633920, 77.99773000000006,
            2227808.0000000005, 6633920, 77.99629000000002,
            2227808.0000000005, 6633888.000000001, 78.00854000000002,
            2227808.0000000005, 6633888.000000001, 78.00997000000005,
        };
        static readonly int[] Layer0Triangles = {
            0, 1, 2, 2, 3, 0, 1, 4, 2, 5, 4, 1, 5, 6, 4, 7, 5, 0, 0, 8, 7, 5, 1, 0,
            0, 3, 8, 3, 9, 8, 3, 6, 10, 6, 3, 2, 2, 4, 6, 10, 9, 3, 7, 8, 9, 9, 10, 7,
            7, 6, 5, 7, 10, 6,
        };
        static readonly double[] Layer1Vertices = {
            2227808.0000000005, 6633888.000000002, 78.00854000000004,
            2227776.0000000005, 6633920.000000001, 77.90063000000006,
            2227808.000000001, 6633920.000000003, 77.99629000000004,
            2227776.0000000005, 6633888.000000002, 77.93891000000006,
            2227808.0000000005, 6633888.000000002, 78.00997000000005,
            2227808.0000000005, 6633920.000000001, 77.99773000000006,
            2227776.000000001, 6633952.000000004, 77.75981000000006,
            2227808.000000001, 6633952.000000004, 77.92621000000004,
            2227840.000000001, 6633920.000000003, 78.06635000000007,
            2227840.000000001, 6633920.000000003, 78.06779000000007,
            2227840.000000001, 6633952.000000004, 77.98640000000005,
        };
        static readonly int[] Layer1Triangles = {
            0, 1, 2, 0, 3, 1, 4, 3, 0, 1, 3, 4, 0, 2, 5, 5, 4, 0, 1, 4, 5, 2, 1, 6,
            6, 1, 5, 2, 6, 7, 2, 7, 8, 6, 5, 7, 5, 2, 8, 8, 9, 5, 8, 7, 10, 10, 9, 8,
            7, 9, 10, 7, 5, 9,
        };

        static DMesh3 FromArrays(double[] v, int[] t)
        {
            var m = new DMesh3();
            for (int i = 0; i < v.Length; i += 3)
                m.AppendVertex(new Vector3d(v[i], v[i + 1], v[i + 2]));
            for (int i = 0; i < t.Length; i += 3)
                m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
            return m;
        }

        static List<DMesh3> Layers()
        {
            return new List<DMesh3> { FromArrays(Layer0Vertices, Layer0Triangles), FromArrays(Layer1Vertices, Layer1Triangles) };
        }

        static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)) }.Generate().MakeDMesh();
        }

        // a closed mesh of two shells: a box with a box-shaped cavity (walls of thickness w)
        static DMesh3 HollowBox(Vector3d min, Vector3d max, double w)
        {
            DMesh3 outer = Box(min, max);
            DMesh3 inner = Box(min + new Vector3d(w, w, w), max - new Vector3d(w, w, w));
            inner.ReverseOrientation();
            var m = new DMesh3(outer);
            new MeshEditor(m).AppendMesh(inner);
            return m;
        }

        static void AssertSolid(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
        }

        // ---- the strict refusal for thin components only ----

        [Fact]
        public void ThinLayers_RefusedOnlyForTheirThinness_AreKeptAndCounted()
        {
            // the strict boolean itself refuses them, with no defect left
            var strict = new ExactMeshBoolean(Layers(), U) { AllowOpenMeshes = false, StrictRounding = true };
            Assert.False(strict.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, strict.Status);
            Assert.True(strict.Rounding.ScaleDisparity > 0);
            Assert.Equal(0, strict.Rounding.RemainingIntersections + strict.Rounding.RemainingDegenerate + strict.Rounding.RemainingFalseContacts);

            var p = new SolidBooleanPipeline(Layers(), U);
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);     // (kept as it is, see ThinComponentsKept)
            Assert.Equal(strict.Rounding.ScaleDisparity, p.Report.ThinComponentsKept);
            Assert.Empty(p.Report.RemovedThinSolids);
            Assert.Equal(0, p.Report.ContactsResult);
            // the same solid as the exact result rounded without the strict check, within the rounding bound times its area
            var loose = new ExactMeshBoolean(Layers(), U);
            Assert.True(loose.Compute());
            double area = MeshMeasurements.VolumeArea(loose.Result, loose.Result.TriangleIndices(), loose.Result.GetVertex).y;
            Assert.True(Math.Abs(Volume(p.Result) - Volume(loose.Result)) <= area * strict.Rounding.MaxLength);
            double sum = Layers().Sum(Volume);
            Assert.True(Volume(p.Result) <= sum + area * strict.Rounding.MaxLength && Volume(p.Result) > 0.5 * sum);      // (the layers touch: about their sum)
        }

        [Fact]
        public void ThinLayers_WithMinThickness_AreRemoved_AndTheResultIsEmpty()
        {
            var p = new SolidBooleanPipeline(Layers(), U) { MinThickness = 1e-3 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal("empty", p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanStopReason.EmptyResult, p.Report.StopReason);
            Assert.Contains("MinThickness", p.Report.StopDetail);
            Assert.Equal(0, p.Report.ContactsBoolean);
            SolidBooleanThinSolid s = Assert.Single(p.Report.RemovedThinSolids);
            Assert.True(s.Thickness > 1e-4 && s.Thickness < 1e-3, s.Thickness.ToString("R"));
            Assert.Equal(p.Report.BooleanTriangles, s.Triangles);
            Assert.Equal(1, s.Shells);
            Assert.Equal(2 * s.Volume / s.Area, s.Thickness);

            // at its thickness and below it the solid stays
            var at = new SolidBooleanPipeline(Layers(), U) { MinThickness = s.Thickness };
            Assert.True(at.Compute(), at.Error);
            Assert.Empty(at.Report.RemovedThinSolids);
            Assert.True(at.Result.TriangleCount > 0);
            var q = new SolidBooleanPipeline(Layers(), U) { MinThickness = s.Thickness / 2 };
            Assert.True(q.Compute(), q.Error);
            AssertSolid(q);
            Assert.Empty(q.Report.RemovedThinSolids);
            // (the repair's edits move the surface within its bound: 9e-10 of 4.5 here, 9e-12 until #1464 changed their order)
            Assert.InRange(Volume(q.Result), s.Volume - 2e-9, s.Volume + 2e-9);
        }

        [Fact]
        public void OnlyThinComponents_RequiresNoOtherDefect()
        {
            var m = Box(Vector3d.Zero, Vector3d.One);
            Func<MeshRoundingRepair> make = () => new MeshRoundingRepair(m) { ScaleDisparity = 1 };
            Assert.True(SolidBooleanPipeline.OnlyThinComponents(make()));
            Assert.False(SolidBooleanPipeline.OnlyThinComponents(null));
            var r = make(); r.ScaleDisparity = 0; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
            r = make(); r.RemainingIntersections = 1; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
            r = make(); r.RemainingDegenerate = 1; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
            r = make(); r.RemainingFalseContacts = 1; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
            r = make(); r.ScaleNotFinite = true; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
            r = make(); r.OutsideRange = true; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
            r = make(); r.Mesh = null; Assert.False(SolidBooleanPipeline.OnlyThinComponents(r));
        }

        // ---- MinThickness on procedural solids ----

        /// <summary> an empty boolean result (the intersection of two disjoint boxes) has no solids to judge: nothing is removed, with
        /// MinThickness set or not, and the result is the empty one both times </summary>
        [Fact]
        public void EmptyBooleanResult_HasNoThinSolids()
        {
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One), b = Box(new Vector3d(3, 0, 0), new Vector3d(4, 1, 1));
            var p = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Intersection) { MinThickness = 0.05 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0, p.Report.BooleanTriangles);
            Assert.Empty(p.Report.RemovedThinSolids);
            Assert.Equal(0, p.Result.TriangleCount);
            var plain = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Intersection);
            Assert.True(plain.Compute(), plain.Error);
            Assert.Equal(0, plain.Result.TriangleCount);
            Assert.Equal(plain.Report.NotCleanReason, p.Report.NotCleanReason);
            Assert.Empty(SolidBooleanPipeline.RemoveThinSolids(new DMesh3(), 0.05));
        }

        [Fact]
        public void ThinSlab_IsRemoved_TheBoxStays()
        {
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 slab = Box(new Vector3d(3, 0, 0), new Vector3d(5, 2, 0.01));
            var p = new SolidBooleanPipeline(box, slab, U) { MinThickness = 0.05 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            SolidBooleanThinSolid s = Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(2 * 0.04 / (8 + 0.08), s.Thickness, 12);       // (2 V / A of the 2 x 2 x 0.01 slab)
            Assert.Equal(new Vector3d(4, 1, 0.005), s.Center);
            Assert.Equal(12, s.Triangles);
            Assert.Equal(1.0, Volume(p.Result), 12);
            Assert.True(p.Result.Vertices().All(q => q.x <= 1));
            Assert.Equal(p.Report.BooleanTriangles - 12, p.Result.TriangleCount);
        }

        [Fact]
        public void HollowBox_IsJudgedByItsWall_WithItsCavity()
        {
            // one solid of two shells, walls 0.01 thick: 2 V / A is about the wall thickness. Judged per shell, the outer
            // box would measure 1/3 and stay, and the cavity would be left without its body
            DMesh3 hollow = HollowBox(Vector3d.Zero, Vector3d.One, 0.01);
            DMesh3 far = Box(new Vector3d(3, 0, 0), new Vector3d(4, 1, 1));
            var p = new SolidBooleanPipeline(hollow, far, U) { MinThickness = 0.02 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            SolidBooleanThinSolid s = Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(2, s.Shells);
            Assert.Equal(24, s.Triangles);
            double v = 1 - Math.Pow(0.98, 3), a = 6 + 6 * 0.98 * 0.98;
            Assert.Equal(v, s.Volume, 12);
            Assert.Equal(2 * v / a, s.Thickness, 12);
            Assert.Equal(1.0, Volume(p.Result), 12);
            Assert.True(p.Result.Vertices().All(q => q.x >= 3));

            var q2 = new SolidBooleanPipeline(hollow, far, U) { MinThickness = 0.005 };
            Assert.True(q2.Compute(), q2.Error);
            AssertSolid(q2);
            Assert.Empty(q2.Report.RemovedThinSolids);
            Assert.Equal(1 + v, Volume(q2.Result), 12);
        }

        [Fact]
        public void IslandInACavity_StaysWhenTheThinWallGoes()
        {
            // a thin-walled hollow box with a cube floating in its cavity: three shells, two solids
            DMesh3 hollow = HollowBox(Vector3d.Zero, Vector3d.One, 0.01);
            DMesh3 island = Box(new Vector3d(0.4, 0.4, 0.4), new Vector3d(0.6, 0.6, 0.6));
            var p = new SolidBooleanPipeline(hollow, island, U) { MinThickness = 0.02 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            SolidBooleanThinSolid s = Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(2, s.Shells);
            Assert.Equal(0.008, Volume(p.Result), 12);
            Assert.Equal(12, p.Result.TriangleCount);
        }

        [Fact]
        public void NestedHollowBoxes_EachCavityBelongsToTheSmallestShellAroundIt()
        {
            // a thin-walled hollow box (walls 0.01) with a thick-walled hollow box (walls 0.1) in its cavity: four shells,
            // two solids. The inner cavity lies inside both outer shells and belongs to the inner one; given to the outer
            // one, the inner box would lose its cavity (volume 0.216 instead of 0.152)
            DMesh3 big = HollowBox(Vector3d.Zero, Vector3d.One, 0.01);
            DMesh3 small = HollowBox(new Vector3d(0.2, 0.2, 0.2), new Vector3d(0.8, 0.8, 0.8), 0.1);
            var p = new SolidBooleanPipeline(big, small, U) { MinThickness = 0.02 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            SolidBooleanThinSolid s = Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(2, s.Shells);
            Assert.Equal(0.216 - 0.064, Volume(p.Result), 12);
            Assert.Equal(24, p.Result.TriangleCount);
            // the same with the inputs swapped (other component order)
            var q = new SolidBooleanPipeline(small, big, U) { MinThickness = 0.02 };
            Assert.True(q.Compute(), q.Error);
            AssertSolid(q);
            Assert.Equal(2, Assert.Single(q.Report.RemovedThinSolids).Shells);
            Assert.Equal(0.216 - 0.064, Volume(q.Result), 12);
        }

        [Fact]
        public void FarFromTheOrigin_TheThicknessIsMeasuredExactlyEnough()
        {
            // a slab 1e-3 thick at UTM-like coordinates: with absolute coordinates the terms of the volume (about 1e-3 x
            // 6e6 per triangle) cancel to noise; relative to a vertex of the component the thickness is close to exact
            var at = new Vector3d(2527500, 5634200, 100);
            DMesh3 slab = Box(at, at + new Vector3d(2, 2, 1e-3));
            DMesh3 box = Box(at + new Vector3d(5, 0, 0), at + new Vector3d(6, 1, 1));
            var p = new SolidBooleanPipeline(box, slab, U) { MinThickness = 2e-3 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            SolidBooleanThinSolid s = Assert.Single(p.Report.RemovedThinSolids);
            double want = 2 * 4e-3 / (8 + 8e-3);
            Assert.True(Math.Abs(s.Thickness - want) <= 1e-6 * want, s.Thickness.ToString("R") + " vs " + want.ToString("R"));
            Assert.Equal(1.0, Volume(p.Result), 6);
        }

        [Fact]
        public void ManyHollowBoxes_AreAllRemoved_AndTheThickOnesStay()
        {
            // 150 thin-walled hollow boxes (300 shells) and 150 thick cubes in one input: every cavity finds its own box
            var many = new DMesh3();
            var ed = new MeshEditor(many);
            for (int i = 0; i < 150; ++i) {
                var c = new Vector3d(3 * (i % 15), 3 * (i / 15), 0);
                ed.AppendMesh(HollowBox(c, c + Vector3d.One, 0.01));
                ed.AppendMesh(Box(c + new Vector3d(1.5, 0, 0), c + new Vector3d(2.5, 1, 1)));
            }
            DMesh3 far = Box(new Vector3d(-10, -10, -10), new Vector3d(-9, -9, -9));
            var p = new SolidBooleanPipeline(many, far, U) { MinThickness = 0.02 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(150, p.Report.RemovedThinSolids.Count);
            Assert.All(p.Report.RemovedThinSolids, s => Assert.Equal(2, s.Shells));
            Assert.Equal(151.0, Volume(p.Result), 9);
            Assert.Equal(151 * 12, p.Result.TriangleCount);
        }

        [Fact]
        public void ThinSlabTouchingABoxAlongAnEdge_TakesItsContactsAlong()
        {
            // the slab touches the box along the edge x = 1, y = 1: the union keeps them as two solids with contacts
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 slab = Box(new Vector3d(1, 1, 0), new Vector3d(3, 3, 0.01));
            var keep = new SolidBooleanPipeline(box, slab, U);
            Assert.True(keep.Compute(), keep.Error);
            Assert.True(keep.Report.ContactsBoolean > 0);
            Assert.True(keep.Report.ContactsResult > 0);
            var p = new SolidBooleanPipeline(box, slab, U) { MinThickness = 0.05 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(0, p.Report.ContactsBoolean);
            Assert.Equal(0, p.Report.ContactsResult);
            Assert.Equal(1.0, Volume(p.Result), 12);
        }

        [Fact]
        public void Off_ByDefault_AndZeroRemovesNothing()
        {
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 slab = Box(new Vector3d(3, 0, 0), new Vector3d(5, 2, 0.01));
            var a = new SolidBooleanPipeline(box, slab, U);
            Assert.Equal(0, a.MinThickness);
            Assert.True(a.Compute(), a.Error);
            AssertSolid(a);
            Assert.Empty(a.Report.RemovedThinSolids);
            Assert.Equal(0, a.Report.ThinComponentsKept);
            Assert.Equal(1.04, Volume(a.Result), 12);
        }

        [Theory]
        [InlineData(-1e-3)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidMinThickness_IsRefused(double t)
        {
            var p = new SolidBooleanPipeline(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)), U) { MinThickness = t };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
            Assert.Contains("MinThickness", p.Error);
            Assert.Null(p.Result);
        }

        [Fact]
        public void Compute_Twice_GivesTheSameResult_AndFollowsAChangedThreshold()
        {
            DMesh3 hollow = HollowBox(Vector3d.Zero, Vector3d.One, 0.01);
            DMesh3 far = Box(new Vector3d(3, 0, 0), new Vector3d(4, 1, 1));
            var p = new SolidBooleanPipeline(hollow, far, U) { MinThickness = 0.02 };
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Single(p.Report.RemovedThinSolids);
            p.MinThickness = 0;
            Assert.True(p.Compute(), p.Error);
            Assert.Empty(p.Report.RemovedThinSolids);
            Assert.Equal(36, p.Result.TriangleCount);
            p.MinThickness = 0.02;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
        }

        [Fact]
        public void Difference_CanLeaveAThinWall_ThatIsRemoved()
        {
            // a box minus a slightly smaller box shifted up: a floor of 1e-3 is left, removed by MinThickness 2e-3
            DMesh3 a = Box(Vector3d.Zero, new Vector3d(4, 4, 1));
            DMesh3 b = Box(new Vector3d(-1, -1, 1e-3), new Vector3d(5, 5, 2));
            var p = new SolidBooleanPipeline(a, b, D);
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(16e-3, Volume(p.Result), 12);
            var q = new SolidBooleanPipeline(a, b, D) { MinThickness = 2e-3 };
            Assert.True(q.Compute(), q.Error);
            Assert.Equal(0, q.Result.TriangleCount);
            Assert.Equal(16e-3, Assert.Single(q.Report.RemovedThinSolids).Volume, 12);
        }

        // ---- invariance: the threshold scales with the input ----

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            DMesh3 hollow = HollowBox(Vector3d.Zero, Vector3d.One, 0.01);
            DMesh3 island = Box(new Vector3d(0.4, 0.4, 0.4), new Vector3d(0.6, 0.6, 0.6));
            var reference = new SolidBooleanPipeline(hollow, island, U) { MinThickness = 0.02 };
            Assert.True(reference.Compute(), reference.Error);

            var p = new SolidBooleanPipeline(Invariance.Transformed(hollow, s), Invariance.Transformed(island, s), U) { MinThickness = 0.02 * s.Scale };
            Assert.True(p.Compute(), s.Name + ": " + p.Error);
            AssertSolid(p);
            SolidBooleanThinSolid r = Assert.Single(p.Report.RemovedThinSolids);
            Assert.Equal(2, r.Shells);
            Assert.Equal(reference.Report.RemovedThinSolids[0].Thickness * s.Scale, r.Thickness, 6 - (int)Math.Round(Math.Log10(s.Scale)));
            Assert.Equal(12, p.Result.TriangleCount);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
