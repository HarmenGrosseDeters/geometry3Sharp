using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.InputGrid: the exact snap of the inputs to a power-of-two grid before the boolean, for faceted
    /// CAD solids whose shared faces are nearly but not exactly coincident far from the origin.
    /// </summary>
    public class SolidBooleanPipelineInputGridTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, D = ExactMeshBoolean.Operations.Difference;
        static readonly double Grid = ScaleB(1, -20);

        // x * 2^k for a normal power of two (Math.ScaleB is not in net48)
        static double ScaleB(double x, int k)
        {
            return x * BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
        }

        // ---- the fragment: two faceted CAD solids at UTM-like coordinates (y about 6.6e6, one ulp 9.3e-10), a body and a
        // small tool whose faces lie on faces of the body up to noise of about 1e-8. Derived from a real case: cropped to a
        // box around the defect and translated by integers (exact; every coordinate stays in its binade, so the double
        // grid is the same). The difference resolves the noise into slivers below that grid; the strict rounding cannot
        // repair them within its bound (RoundingNotClean). The same input moved to the origin is clean, and the result
        // cannot be moved back without folding the slivers again ----

        static readonly double[] Fragment0Vertices = {
            2227856.244429708, 6633856.000000001, 78.22341574190881,
            2227857.757788753, 6633857.359375, 76.91331425325741,
            2227857.75778875, 6633857.359375, 76.05042959855203,
            2227855.0201803804, 6633854.900317854, 78.21504881871925,
            2227857.757788753, 6633857.359375, 78.23101259865595,
            2227855.7430819133, 6633854.106173504, 78.21670362011982,
            2227858.980062311, 6633857.359375, 75.51999142436114,
            2227855.961499703, 6633853.865959863, 78.21720298673672,
            2227859.3219390814, 6633857.359375, 75.37154414154448,
            2227859.850530536, 6633857.359375, 75.14202294579945,
            2227855.9627507613, 6633853.864565773, 78.21720580561599,
            2227859.8529491383, 6633857.359375, 75.14096649278686,
            2227858.8651132896, 6633850.630378257, 78.2237453958053,
            2227863.546875, 6633855.910351869, 74.09837685754809,
            2227863.546875, 6633854.875249699, 78.2558319599693,
            2227863.546875, 6633854.874427143, 78.25583008762426,
            2227863.546875, 6633852.544310979, 78.25052616070664,
            2227863.546875, 6633856.000000001, 78.25839217284306,
            2227863.546875, 6633851.042729583, 76.0162665773077,
            2227863.546875, 6633846.046875, 77.98447496020552,
            2227862.9778908584, 6633846.046875, 78.23301111583129,
            2227863.546875, 6633857.359375, 73.52744789004197,
            2227863.546875, 6633857.359375, 78.25874051268659,
            2227863.546875, 6633846.046875, 78.2357363720598,
        };
        static readonly int[] Fragment0Triangles = {
            0, 1, 2, 2, 3, 0, 0, 4, 1, 3, 2, 5, 6, 5, 2, 5, 6, 7, 8, 7, 6, 9, 7, 8,
            7, 9, 10, 11, 10, 9, 10, 11, 12, 13, 12, 11, 14, 7, 10, 10, 15, 14, 0, 5, 7, 15, 10, 12,
            12, 16, 15, 0, 3, 5, 7, 14, 0, 17, 0, 14, 18, 19, 20, 20, 12, 18, 13, 21, 22, 22, 17, 13,
            19, 23, 20, 2, 1, 6, 4, 0, 17, 17, 22, 4, 22, 11, 9, 22, 9, 8, 4, 8, 6, 6, 1, 4,
            23, 19, 18, 18, 16, 23, 16, 12, 20, 20, 23, 16, 8, 4, 22, 22, 21, 11, 21, 13, 11, 18, 12, 13,
            13, 17, 14, 15, 18, 13, 13, 14, 15, 15, 16, 18,
        };
        static readonly double[] Fragment1Vertices = {
            2227858.8660072777, 6633850.630456995, 78.22332387480276,
            2227862.978821852, 6633846.046875, 78.23260445143953,
            2227862.9781592772, 6633846.046875, 78.23289386824668,
            2227858.865113289, 6633850.630378256, 78.22374539580055,
            2227862.9778908575, 6633846.046875, 78.23301111582737,
            2227855.963297973, 6633853.865057368, 78.21677308811725,
            2227855.9627507604, 6633853.864565773, 78.21720580561121,
            2227855.9614997027, 6633853.865959863, 78.21720298673196,
            2227855.962046945, 6633853.866451436, 78.21677026481636,
            2227855.7430819124, 6633854.106173503, 78.21670362011507,
            2227855.7436022228, 6633854.10669642, 78.21627015247704,
            2227855.0201803795, 6633854.900317854, 78.21504881871451,
            2227855.020731328, 6633854.900812743, 78.21461318534297,
            2227856.2444297113, 6633856.000000004, 78.22290302711608,
            2227863.546875, 6633856.000000004, 78.25764897460634,
            2227863.546875, 6633854.875470416, 78.25512475709769,
            2227863.546875, 6633854.874647861, 78.25512291072198,
            2227863.546875, 6633852.544126439, 78.24989161841447,
            2227863.546875, 6633854.8752497, 78.25583195997027,
            2227863.546875, 6633854.8744271435, 78.25583008762646,
            2227856.2444297113, 6633856.000000004, 78.22341574192879,
            2227863.546875, 6633852.544310979, 78.2505261607072,
            2227863.546875, 6633856.000000003, 78.25839217284745,
            2227857.7577887536, 6633857.359375, 78.2303913713835,
            2227863.546875, 6633857.359375, 78.25793656737892,
            2227863.546875, 6633857.359375, 78.2587405126905,
            2227863.546875, 6633846.046875, 78.23530731932526,
            2227863.546875, 6633846.046875, 78.23573637206108,
            2227857.7577887536, 6633857.359375, 78.23101259866115,
        };
        static readonly int[] Fragment1Triangles = {
            0, 1, 2, 2, 3, 0, 3, 2, 4, 5, 0, 3, 3, 6, 5, 7, 8, 5, 5, 6, 7, 8, 7, 9,
            9, 10, 8, 11, 12, 10, 10, 9, 11, 13, 14, 15, 15, 8, 13, 0, 5, 16, 16, 17, 0, 8, 10, 13,
            5, 8, 15, 15, 16, 5, 10, 12, 13, 18, 7, 6, 6, 19, 18, 20, 9, 7, 19, 6, 3, 3, 21, 19,
            20, 11, 9, 7, 18, 20, 22, 20, 18, 23, 13, 20, 20, 13, 12, 12, 11, 20, 19, 21, 17, 17, 16, 19,
            24, 25, 22, 18, 19, 16, 16, 15, 18, 22, 18, 15, 15, 14, 22, 22, 14, 24, 1, 26, 27, 27, 4, 2,
            27, 2, 1, 23, 20, 28, 28, 20, 22, 22, 25, 28, 23, 24, 14, 14, 13, 23, 24, 23, 28, 28, 25, 24,
            27, 26, 17, 21, 3, 4, 4, 27, 21, 17, 26, 1, 1, 0, 17, 17, 21, 27,
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

        static List<DMesh3> Fragment()
        {
            return new List<DMesh3> { FromArrays(Fragment0Vertices, Fragment0Triangles), FromArrays(Fragment1Vertices, Fragment1Triangles) };
        }

        static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

        static bool OnGrid(Vector3d p, double g)
        {
            return Math.Round(p.x / g) * g == p.x && Math.Round(p.y / g) * g == p.y && Math.Round(p.z / g) * g == p.z;
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

        static DMesh3 Soup(DMesh3 m)
        {
            var s = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                s.AppendTriangle(s.AppendVertex(m.GetVertex(tv.a)), s.AppendVertex(m.GetVertex(tv.b)), s.AppendVertex(m.GetVertex(tv.c)));
            }
            return s;
        }

        static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static List<DMesh3> TwoSpheres() { return new List<DMesh3> { Soup(Sphere(Vector3d.Zero, 1, 12)), Soup(Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 12)) }; }

        // ---- the fix ----

        [Fact]
        public void NearlyCoincidentFacesFarFromTheOrigin_FailWithoutTheGrid_AndAreCleanWithIt()
        {
            var plain = new SolidBooleanPipeline(Fragment(), D);
            Assert.False(plain.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, plain.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, plain.BooleanStatus);

            var p = new SolidBooleanPipeline(Fragment(), D) { InputGrid = Grid };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(0, p.Report.ContactsResult);
            Assert.True(p.Report.InputSnappedVertices > 0);
            Assert.Empty(p.Report.RepairedInputs);
            // the same solid: within the snap (half the grid per coordinate, times the area) of the exact result that is
            // rounded without the strict check
            var loose = new ExactMeshBoolean(Fragment(), D);
            Assert.True(loose.Compute());
            double area = MeshMeasurements.VolumeArea(loose.Result, loose.Result.TriangleIndices(), loose.Result.GetVertex).y;
            Assert.True(Math.Abs(Volume(p.Result) - Volume(loose.Result)) <= area * Grid, Volume(p.Result) + " vs " + Volume(loose.Result));
            Assert.True(Volume(p.Result) < Volume(Fragment()[0]));
        }

        [Fact]
        public void TheInputsAreNotModified_AndTheSnapIsCountedAndBounded()
        {
            var inputs = Fragment();
            var before = inputs.Select(m => new DMesh3(m)).ToList();
            var p = new SolidBooleanPipeline(inputs, D) { InputGrid = Grid };
            Assert.True(p.Compute(), p.Error);
            for (int i = 0; i < inputs.Count; ++i)
                Assert.Equal("", Invariance.Difference(before[i], inputs[i]));
            int off = inputs.Sum(m => m.Vertices().Count(q => !OnGrid(q, Grid)));
            Assert.True(off > 0);
            Assert.Equal(off, p.Report.InputSnappedVertices);
            int moved;
            DMesh3 snapped = SolidBooleanPipeline.Snapped(inputs[0], Grid, out moved);
            Assert.Equal(inputs[0].Vertices().Count(q => !OnGrid(q, Grid)), moved);
            foreach (int v in inputs[0].VertexIndices()) {
                Vector3d a = inputs[0].GetVertex(v), b = snapped.GetVertex(v);
                Assert.True(OnGrid(b, Grid));
                Assert.True(Math.Abs(a.x - b.x) <= Grid / 2 && Math.Abs(a.y - b.y) <= Grid / 2 && Math.Abs(a.z - b.z) <= Grid / 2);
            }
        }

        [Fact]
        public void Snap_RoundsToTheNearestMultiple_TiesToEven_WithoutNegativeZero()
        {
            var m = new DMesh3();
            double g = 0.25;
            double[] xs = { 0.1, 0.125, -0.125, 0.375, 0.3, -0.3, 1e300, 5633851.703125, 5633851.7031250009 };
            double[] want = { 0, 0, 0, 0.5, 0.25, -0.25, 1e300, 5633851.75, 5633851.75 };
            foreach (double x in xs)
                m.AppendVertex(new Vector3d(x, -x, 0));
            int moved;
            DMesh3 s = SolidBooleanPipeline.Snapped(m, g, out moved);
            for (int i = 0; i < want.Length; ++i) {
                Vector3d q = s.GetVertex(i);
                Assert.Equal(want[i], q.x);
                Assert.Equal(want[i] == 0 ? 0 : -want[i], q.y);
                Assert.False(q.x == 0 && (BitConverter.DoubleToInt64Bits(q.x) < 0), "negative zero at " + i);
                Assert.False(q.y == 0 && (BitConverter.DoubleToInt64Bits(q.y) < 0), "negative zero at " + i);
            }
            Assert.Equal(8, moved);     // (1e300 is a multiple already)
            // a grid so fine that the quotient overflows leaves the coordinate where it is
            var big = new DMesh3();
            big.AppendVertex(new Vector3d(1e300, 1, 0));
            DMesh3 b = SolidBooleanPipeline.Snapped(big, ScaleB(1, -1000), out moved);
            Assert.Equal(new Vector3d(1e300, 1, 0), b.GetVertex(0));
            Assert.Equal(0, moved);
            // a grid so coarse that the nearest multiple overflows: the neighbour towards zero, finite
            var huge = new DMesh3();
            huge.AppendVertex(new Vector3d(1.6e308, -1.6e308, 0));
            DMesh3 h = SolidBooleanPipeline.Snapped(huge, ScaleB(1, 1023), out moved);
            Assert.Equal(new Vector3d(ScaleB(1, 1023), -ScaleB(1, 1023), 0), h.GetVertex(0));
            Assert.Equal(1, moved);
        }

        [Fact]
        public void Snap_LeavesNonFiniteCoordinates_AndWritesNegativeZeroAsZero_WithoutCountingIt()
        {
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(double.NaN, 0.5, 0));
            m.AppendVertex(new Vector3d(-0.0, 0, -0.0));
            m.AppendVertex(new Vector3d(double.PositiveInfinity, double.NegativeInfinity, 1));
            int moved;
            DMesh3 s = SolidBooleanPipeline.Snapped(m, 0.25, out moved);
            Assert.Equal(0, moved);
            Assert.True(double.IsNaN(s.GetVertex(0).x));
            Assert.Equal(0.5, s.GetVertex(0).y);
            Vector3d z = s.GetVertex(1);
            Assert.True(BitConverter.DoubleToInt64Bits(z.x) == 0 && BitConverter.DoubleToInt64Bits(z.z) == 0, "negative zero kept");
            Assert.Equal(new Vector3d(double.PositiveInfinity, double.NegativeInfinity, 1), s.GetVertex(2));
        }

        // ---- off, and the settings ----

        [Fact]
        public void InputsOnTheGrid_GiveTheResultWithoutIt_BitForBit()
        {
            int moved;
            var inputs = TwoSpheres().Select(m => SolidBooleanPipeline.Snapped(m, Grid, out moved)).ToList();
            var without = new SolidBooleanPipeline(inputs, U) { TargetFraction = 0.25 };
            Assert.True(without.Compute(), without.Error);
            Assert.Equal(0, without.Report.InputSnappedVertices);
            var with = new SolidBooleanPipeline(inputs, U) { TargetFraction = 0.25, InputGrid = Grid };
            Assert.True(with.Compute(), with.Error);
            Assert.Equal(0, with.Report.InputSnappedVertices);
            Assert.Equal("", Invariance.Difference(without.Result, with.Result));
        }

        [Theory]
        [InlineData(3.0)]
        [InlineData(0.3)]
        [InlineData(-0.25)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.Epsilon)]        // (a subnormal power of two)
        public void InvalidGrid_IsRefused(double g)
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { InputGrid = g };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
            Assert.Contains("InputGrid", p.Error);
            Assert.Null(p.Result);
        }

        [Fact]
        public void OpenInput_IsRepairedAfterTheSnap()
        {
            DMesh3 holed = Sphere(Vector3d.Zero, 1, 10);
            holed.RemoveTriangle(holed.TriangleIndices().First());
            var p = new SolidBooleanPipeline(new List<DMesh3> { Soup(holed), Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 10)) }, U) { InputGrid = Grid };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.False(p.Report.InputClosed[0]);
            Assert.Single(p.Report.RepairedInputs);
            Assert.True(p.Report.InputSnappedVertices > 0);
            // the repaired input is the snapped copy: away from the other sphere (x < -0.5) the result has only its vertices,
            // and those are on the grid (a hole of one triangle is filled without a new vertex); so are those of the input
            // that needed no repair, on its far side (x > 1.7)
            var far = p.Result.Vertices().Where(q => q.x < -0.5).ToList();
            Assert.True(far.Count > 20);
            Assert.All(far, q => Assert.True(OnGrid(q, Grid), q.ToString()));
            var other = p.Result.Vertices().Where(q => q.x > 1.7).ToList();
            Assert.True(other.Count > 20);
            Assert.All(other, q => Assert.True(OnGrid(q, Grid), q.ToString()));
        }

        [Fact]
        public void Compute_Twice_GivesTheSameResult_AndFollowsAChangedGrid()
        {
            var p = new SolidBooleanPipeline(Fragment(), D) { InputGrid = Grid };
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            int moved = p.Report.InputSnappedVertices;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Equal(moved, p.Report.InputSnappedVertices);
            p.InputGrid = 0;
            Assert.False(p.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.Equal(0, p.Report.InputSnappedVertices);
            p.InputGrid = Grid;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
        }

        // ---- invariance: the grid scales with the input (a power-of-two scale of a power of two is one) ----

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            double grid = ScaleB(Grid, (int)Math.Round(Math.Log(s.Scale, 2)));
            var inputs = TwoSpheres();
            var reference = new SolidBooleanPipeline(inputs, U) { TargetFraction = 0.25, InputGrid = Grid };
            Assert.True(reference.Compute(), reference.Error);
            AssertSolid(reference);

            var p = new SolidBooleanPipeline(inputs.Select(m => Invariance.Transformed(m, s)).ToList(), U) { TargetFraction = 0.25, InputGrid = grid };
            Assert.True(p.Compute(), s.Name + ": " + p.Error);
            AssertSolid(p);
            if (s.IsExact) {
                Assert.Equal(reference.Report.InputSnappedVertices, p.Report.InputSnappedVertices);
                Assert.Equal(reference.Report.BooleanTriangles, p.Report.BooleanTriangles);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
            }
        }

        [Fact]
        public void Fragment_ScaledByPowersOfTwo_WithTheGridScaled_GivesTheSameResult()
        {
            var reference = new SolidBooleanPipeline(Fragment(), D) { InputGrid = Grid };
            Assert.True(reference.Compute(), reference.Error);
            foreach (int k in new[] { -10, 10 }) {
                double f = ScaleB(1, k);
                var scaled = Fragment().Select(m => { var c = new DMesh3(m); MeshTransforms.Scale(c, f); return c; }).ToList();
                var p = new SolidBooleanPipeline(scaled, D) { InputGrid = ScaleB(Grid, k) };
                Assert.True(p.Compute(), "2^" + k + ": " + p.Error);
                AssertSolid(p);
                DMesh3 back = new DMesh3(p.Result);
                MeshTransforms.Scale(back, 1 / f);
                Assert.Equal("", Invariance.Difference(reference.Result, back));
            }
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
