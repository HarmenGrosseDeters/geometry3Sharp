using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1352: the range of IntrinsicTriangulation.ExtractCommonSubdivision. The area checks used to pass NaN near 1e160 (areas
    /// of infinite size) and were vacuous below about 2^-500 (areas below the 1e-300 floor); the triangulation's own area
    /// formula (a product of four lengths) underflowed from about 2^-256 on, so the flips of a scaled mesh differed from those
    /// of the unit mesh (a 3 x 3 sheared grid at 2^-300 and 2^-480 gave 57 points where the unit mesh gives 25). The prescaling
    /// of #1419 and the NaN-safe checks of #1417 fixed them; these tests pin the observed range: a scaled mesh gives the
    /// scaled subdivision of the unit mesh, bit for bit, or it is reported (never a Success with other content).
    /// </summary>
    public class IntrinsicCommonSubdivisionRangeTests
    {
        // (n + 1)^2 vertices at (a * (i + shear * j), j, 0), transformed
        static DMesh3 ShearedGrid(int n, double a, double shear, Similarity sim)
        {
            var mesh = new DMesh3();
            int[,] vid = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    vid[i, j] = mesh.AppendVertex(sim.Apply(new Vector3d(a * (i + shear * j), j, 0)));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        static IntrinsicCommonSubdivision Subdivision(DMesh3 mesh)
        {
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            T.FlipToDelaunay();
            return T.ExtractCommonSubdivision();
        }

        static Similarity Scale(double s) { return new Similarity("scale " + s, s, Matrix3d.Identity, Vector3d.Zero); }

        static void AssertScaledSubdivision(IntrinsicCommonSubdivision cs0, IntrinsicCommonSubdivision cs, Similarity sim)
        {
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.Equal(cs0.Points.Count, cs.Points.Count);
            for (int i = 0; i < cs.Points.Count; ++i) Assert.Equal(sim.Apply(cs0.Points[i]), cs.Points[i]);
            Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
            for (int f = 0; f < cs.Faces.Count; ++f) {
                Assert.Equal(cs0.Faces[f].Corners, cs.Faces[f].Corners);
                Assert.Equal(cs0.Faces[f].Barycentric, cs.Faces[f].Barycentric);
                Assert.Equal(cs0.Faces[f].InputTriangle, cs.Faces[f].InputTriangle);
                Assert.Equal(cs0.Faces[f].IntrinsicTriangle, cs.Faces[f].IntrinsicTriangle);
            }
            // (the checks are not vacuous: the relative errors are those of the unit mesh, bit for bit, with areas that are
            // no longer compared against a floor of 1e-300)
            Assert.Equal(cs0.MaxInputAreaError, cs.MaxInputAreaError);
            Assert.Equal(cs0.MaxIntrinsicAreaError, cs.MaxIntrinsicAreaError);
        }

        [Theory]
        [InlineData(-256)] [InlineData(-300)] [InlineData(-480)] [InlineData(-520)] [InlineData(-600)] [InlineData(-900)]
        [InlineData(-1000)]
        [InlineData(256)] [InlineData(300)] [InlineData(480)] [InlineData(510)]
        public void PowersOfTwo_GiveTheScaledSubdivisionOfTheUnitMesh(int exponent)
        {
            Similarity sim = Scale(Math.Pow(2, exponent));
            IntrinsicCommonSubdivision cs0 = Subdivision(ShearedGrid(3, 1, 0.375, Similarity.Identity));
            Assert.Equal(CommonSubdivisionStatus.Success, cs0.Status);
            Assert.Equal(25, cs0.Points.Count);
            AssertScaledSubdivision(cs0, Subdivision(ShearedGrid(3, 1, 0.375, sim)), sim);
        }

        [Theory]
        [InlineData(1e-160)] [InlineData(7e-170)] [InlineData(3e-200)] [InlineData(1e-250)] [InlineData(1e-300)] [InlineData(1e150)]
        public void AnyScaleInTheRange_GivesTheSubdivisionOfTheUnitMesh_AsToPointsFacesAndCheckedAreas(double scale)
        {
            IntrinsicCommonSubdivision cs0 = Subdivision(ShearedGrid(3, 1, 0.375, Similarity.Identity));
            IntrinsicCommonSubdivision cs = Subdivision(ShearedGrid(3, 1, 0.375, Scale(scale)));
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.Equal(cs0.Points.Count, cs.Points.Count);
            Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
            for (int f = 0; f < cs.Faces.Count; ++f) {
                Assert.Equal(cs0.Faces[f].Corners, cs.Faces[f].Corners);
                Assert.Equal(cs0.Faces[f].InputTriangle, cs.Faces[f].InputTriangle);
            }
            Assert.True(cs.MaxInputAreaError <= 1e-6 && cs.MaxIntrinsicAreaError <= 1e-6);
        }

        // above the range of the output (areas, squared lengths, in the units of the mesh, beyond the double range): reported,
        // with no faces; never a Success with a NaN or an infinite area
        [Theory]
        [InlineData(1e155)] [InlineData(1e160)] [InlineData(5.26e210)]
        [InlineData(520)] [InlineData(531)] [InlineData(700)]
        public void BeyondTheRangeOfTheAreas_IsReportedNotSuccessWithNaN(double scaleOrExponent)
        {
            double scale = scaleOrExponent > 400 && scaleOrExponent < 1000 && scaleOrExponent == Math.Floor(scaleOrExponent) && scaleOrExponent < 1e4
                ? Math.Pow(2, scaleOrExponent) : scaleOrExponent;
            IntrinsicCommonSubdivision cs = Subdivision(ShearedGrid(3, 1, 0.375, Scale(scale)));
            if (cs.Status == CommonSubdivisionStatus.Success) {
                // (the range of the double: every face area is finite and the faces sum to the mesh)
                foreach (var f in cs.Faces) Assert.True(f.Area >= 0 && !double.IsInfinity(f.Area) && !double.IsNaN(f.Area), "area " + f.Area);
                Assert.True(cs.MaxInputAreaError <= 1e-6 && cs.MaxIntrinsicAreaError <= 1e-6);
            } else {
                Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
                Assert.Contains("outside the double range", cs.Error);
                Assert.Empty(cs.Faces);
            }
            if (scale >= 1e155) Assert.NotEqual(CommonSubdivisionStatus.Success, cs.Status);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ShearedGrid_UnderEverySimilarity_IsASuccessWithCheckedAreas(string name)
        {
            Similarity sim = Similarity.ByName(name);
            IntrinsicCommonSubdivision cs0 = Subdivision(ShearedGrid(3, 1, 0.375, Similarity.Identity));
            IntrinsicCommonSubdivision cs = Subdivision(ShearedGrid(3, 1, 0.375, sim));
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.True(cs.MaxInputAreaError <= 1e-6 && cs.MaxIntrinsicAreaError <= 1e-6);
            if (sim.IsExact) {
                // (a power-of-two scale and a translation of the unit mesh: the same subdivision slot for slot)
                Assert.Equal(cs0.Points.Count, cs.Points.Count);
                Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
            }
        }

        // reuse: a second extraction from one triangulation is the same
        [Fact]
        public void ExtractingTwice_GivesTheSameSubdivision_AtAnExtremeScale()
        {
            var T = new IntrinsicTriangulation(ShearedGrid(3, 1, 0.375, Scale(Math.Pow(2, -480))));
            Assert.True(T.EnableIntegerCoordinates());
            T.FlipToDelaunay();
            IntrinsicCommonSubdivision a = T.ExtractCommonSubdivision(), b = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, a.Status);
            Assert.Equal(a.Points, b.Points);
            Assert.Equal(a.Faces.Select(f => f.Corners), b.Faces.Select(f => f.Corners));
        }
    }
}
