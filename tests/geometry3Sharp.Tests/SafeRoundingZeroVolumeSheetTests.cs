using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1067 (abc-chained iud p0008, run 44485992303): a closed double-sided sheet of numerically zero volume (thickness 2e-18 at
    /// coordinates of 0.3, area 6.5e-6) that no edit touched survived the removal of tiny and thin components, because it is not
    /// tiny (the removal of untouched components took only tiny inside-out residues, #1152). Owner decision 2026-11-19: an untouched
    /// closed component whose thickness (2 |V| / area) is below the unit in the last place of its largest coordinate is removed,
    /// whatever its orientation and whatever encloses it; a sheet with a thickness its coordinates can resolve stays, in either
    /// orientation (an inverted one is reported by NotCleanReason, not deleted).
    /// </summary>
    public class SafeRoundingZeroVolumeSheetTests
    {
        // the unit in the last place of x > 0
        static double Ulp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;

        // double cone over a 17-gon of radius r, apexes at +-h, whose thickness (2 |V| / area, from the formulas) is f times the
        // ulp of its largest coordinate; centred at c
        static DMesh3 Sheet(Vector3d c, double r, double f, bool inverted)
        {
            const int n = 17;
            double ulp = Ulp(Math.Max(Math.Abs(c.x) + r, Math.Max(Math.Abs(c.y) + r, Math.Abs(c.z))));
            double h0 = 1e-15;
            double thickness0 = 2 * Volume(n, r, h0) / Area(n, r, h0);
            return SafeRoundingInsideOutResidueTests.Bipyramid(n, r, h0 * f * ulp / thickness0, c, inverted);
        }

        static double Area(int n, double r, double h) => 2 * n * 0.5 * (2 * r * Math.Sin(Math.PI / n)) * Math.Sqrt(h * h + Math.Pow(r * Math.Cos(Math.PI / n), 2));
        static double Volume(int n, double r, double h) => 2.0 / 3 * (0.5 * n * r * r * Math.Sin(2 * Math.PI / n)) * h;

        // the solid (a sphere with needles, so that the repair edits and looks at the components) with the sheets
        static DMesh3 Input(params DMesh3[] sheets)
        {
            DMesh3 m = SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(new DMesh3());
            foreach (DMesh3 s in sheets)
                new MeshEditor(m).AppendMesh(s);
            return m;
        }

        static (DMesh3 Mesh, MeshRoundingRepair Repair) Repaired(DMesh3 m)
        {
            var copy = new DMesh3(m);
            var r = new MeshRoundingRepair(copy);
            r.Apply();
            return (copy, r);
        }

        static readonly Vector3d Right = new Vector3d(3, 0, 0), Left = new Vector3d(-3, 0, 0);

        /// <summary> thickness 0.75 ulp: removed, inverted or not (the p0008 residue was inverted; its positive companion is the same kind), result closed, clean </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SheetBelowTheUlp_IsRemoved_InEitherOrientation(bool inverted)
        {
            var (m, r) = Repaired(Input(Sheet(Right, 0.05, 0.75, inverted)));
            Assert.Equal(1, r.RemovedComponents);
            Assert.Equal(34, r.RemovedTriangles);
            Assert.True(r.IsClean);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(m));
            Assert.True(Invariance.Volume(m) > 4);
        }

        /// <summary> thickness 0.95 ulp is removed, 1.05 ulp stays: the bound is the ulp itself, not a multiple (the estimate of the thickness is good to a few percent at this size) </summary>
        [Theory]
        [InlineData(0.95, 1, true)]
        [InlineData(1.05, 0, false)]
        public void TheBoundIsTheUlpItself(double f, int removed, bool inverted)
        {
            var (m, r) = Repaired(Input(Sheet(Right, 0.05, f, inverted)));
            Assert.Equal(removed, r.RemovedComponents);
        }

        /// <summary>
        /// a large sheet near the origin (the rounding of the volume sum is as large as the bound there: unit sheet at x = 5, coordinates
        /// up to 6): 0.5 ulp is removed, 3 ulps stay (the noise margin keeps the doubtful ones, never removes a real one)
        /// </summary>
        [Theory]
        [InlineData(0.5, 1)]
        [InlineData(3, 0)]
        public void LargeSheet_NoiseDoesNotDecide(double f, int removed)
        {
            var (m, r) = Repaired(Input(Sheet(new Vector3d(5, 0, 0), 1, f, true)));
            Assert.Equal(removed, r.RemovedComponents);
        }

        /// <summary>
        /// the rule does not depend on the tiny / thin bound of the removal: with no ulp term and a vanishing displacement bound (every
        /// sheet is "thick" by max_length) a numerically zero sheet still goes
        /// </summary>
        [Fact]
        public void IndependentOfTheThinBound()
        {
            DMesh3 m = Input(Sheet(Right, 0.05, 0.75, true));
            var r = new MeshRoundingRepair(m) { UlpFactor = 0, MaxDisplacement = 1e-30 };
            r.Apply();
            Assert.DoesNotContain(34, MeshRoundingRepair.connected_components(m).Select(c => c.Count).ToList());
            Assert.True(r.RemovedComponents >= 1);
        }

        /// <summary> thickness 1.5 and 4 ulps: a solid that the coordinates resolve, kept in either orientation; the inverted one is still reported, not deleted </summary>
        [Theory]
        [InlineData(1.5, true)]
        [InlineData(1.5, false)]
        [InlineData(4, true)]
        [InlineData(4, false)]
        public void SheetAboveTheUlp_Stays(double f, bool inverted)
        {
            var (m, r) = Repaired(Input(Sheet(Right, 0.05, f, inverted)));
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal(inverted ? "inside-out components" : "", MeshRobustRepair.NotCleanReason(m));
            Assert.Contains(34, MeshRoundingRepair.connected_components(m).Select(c => c.Count).ToList());
        }

        /// <summary> a zero-volume sheet inside the solid (an inverted cavity) goes as well: whatever encloses it, it has no volume that its coordinates can resolve </summary>
        [Fact]
        public void EnclosedZeroVolumeCavity_IsRemoved()
        {
            var (m, r) = Repaired(Input(Sheet(new Vector3d(0.1, 0.05, 0), 0.05, 0.75, true)));
            Assert.Equal(1, r.RemovedComponents);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary>
        /// the bound is the ulp of the component's largest coordinate, not of any one vertex: a sheet at x from -2.1 to -1.7 has its
        /// first vertex at -1.7 (ulp 2.2e-16) and its largest coordinate 2.1 (ulp 4.4e-16); a thickness of 0.75 of the larger is removed
        /// </summary>
        [Fact]
        public void TheBoundIsTheUlpOfTheLargestCoordinate()
        {
            var (m, r) = Repaired(Input(Sheet(new Vector3d(-1.9, 0, 0), 0.2, 0.75, false)));
            Assert.Equal(1, r.RemovedComponents);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary> both sheets at once, and a resolvable one beside them: only the two go </summary>
        [Fact]
        public void TwoSheetsGo_TheResolvableOneStays()
        {
            var (m, r) = Repaired(Input(Sheet(Right, 0.05, 0.75, true), Sheet(Left, 0.05, 0.75, false), Sheet(new Vector3d(0, 3, 0), 0.05, 4, false)));
            Assert.Equal(2, r.RemovedComponents);
            Assert.Equal(68, r.RemovedTriangles);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        static string Positions(DMesh3 m, Similarity s)
        {
            DMesh3 back = Invariance.MappedBack(m, s);
            return string.Join(";", back.VertexIndices().Select(v => back.GetVertex(v)).Select(p => $"{p.x:R},{p.y:R},{p.z:R}"));
        }

        /// <summary>
        /// invariance over Similarity.All(): at the 2^k scales (the thickness and the ulp scale exactly) the same two sheets go and the
        /// result is the same bit for bit; in the other frames the coordinates are rounded (a sheet of thickness 1e-16 is not
        /// resolved there: it may stay as a flat or a noisy plate) and the result is a closed finite solid of the same volume
        /// </summary>
        [Fact]
        public void Removal_Invariance()
        {
            DMesh3 input = Input(Sheet(Right, 0.05, 0.75, true), Sheet(Left, 0.05, 0.75, false));
            var (m0, r0) = Repaired(input);
            Assert.Equal(2, r0.RemovedComponents);
            string expected = Positions(m0, Similarity.Identity);
            double v0 = Invariance.Volume(m0);
            foreach (Similarity s in Similarity.All()) {
                var (m, r) = Repaired(Invariance.Transformed(input, s));
                if (s.IsExact) {
                    Assert.True(r.RemovedComponents == 2 && r.RemovedTriangles == 68, s.Name);
                    Assert.Equal(expected, Positions(m, s));
                }
                Assert.True(Invariance.AllFinite(m), s.Name);
                Assert.True(m.IsClosed(), s.Name);
                Assert.Equal(v0 * s.Scale * s.Scale * s.Scale, Invariance.Volume(m), 1e-3 * v0 * s.Scale * s.Scale * s.Scale);
            }
        }

        /// <summary> a second Apply on the same instance changes nothing, and equal inputs give the same result </summary>
        [Fact]
        public void Reuse_AndDeterminism()
        {
            DMesh3 input = Input(Sheet(Right, 0.05, 0.75, true));
            DMesh3 m = new DMesh3(input);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            string once = Positions(m, Similarity.Identity);
            r.Apply();
            Assert.Equal(once, Positions(m, Similarity.Identity));
            Assert.Equal(once, Positions(Repaired(input).Mesh, Similarity.Identity));
        }
    }
}
