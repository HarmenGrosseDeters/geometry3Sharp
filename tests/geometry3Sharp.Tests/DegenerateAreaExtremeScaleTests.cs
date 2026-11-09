using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The degenerate-area threshold (1e-12 * diagonal^2) outside the squares' range: at a diagonal of 2^520 and more
    /// the square overflows (every triangle is "degenerate" or the threshold is inf), at 2^-520 and less it underflows
    /// to 0 (no triangle is). A 2^k scale is exact, so every result in the input's units must equal the one at scale 1
    /// (the pattern of NotCleanReason, #458).
    /// </summary>
    public class DegenerateAreaExtremeScaleTests
    {
        public static readonly int[] Exponents = { -600, -530, 530, 600 };

        static DMesh3 Slivers() => DegenerateFamilies.Make(DegenerateFamilies.Family.Sliver, 45, 0);

        static DMesh3 Scaled(DMesh3 m, int k)
        {
            double s = Math.Pow(2, k);
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, s * c.GetVertex(v));
            return c;
        }

        public static System.Collections.Generic.IEnumerable<object[]> Ks() => Exponents.Select(k => new object[] { k });

        [Theory, MemberData(nameof(Ks))]
        public void CollapseSlivers_SameCountAtEveryScale(int k)
        {
            DMesh3 a = Slivers(), b = Scaled(Slivers(), k);
            int na = MeshRobustRepair.CollapseSlivers(a), nb = MeshRobustRepair.CollapseSlivers(b);
            Assert.True(na > 0, "the input has slivers");
            Assert.Equal(na, nb);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
        }

        [Theory, MemberData(nameof(Ks))]
        public void PostRepairCleanup_SameResultAtEveryScale(int k)
        {
            DMesh3 a = Slivers(), b = Scaled(Slivers(), k);
            var ca = new MeshPostRepairCleanup(a); ca.Apply();
            var cb = new MeshPostRepairCleanup(b); cb.Apply();
            Assert.Equal(ca.FixedDegenerate, cb.FixedDegenerate);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
        }

        [Theory, MemberData(nameof(Ks))]
        public void TriangleQuality_SameDegenerateCountAtEveryScale(int k)
        {
            var qa = TriangleQualityStatistics.Compute(Slivers());
            var qb = TriangleQualityStatistics.Compute(Scaled(Slivers(), k));
            Assert.Equal(qa.Degenerate, qb.Degenerate);
        }

        [Theory, MemberData(nameof(Ks))]
        public void NotCleanReason_AgreesWithItsScaleVariant(int k)
        {
            DMesh3 b = Scaled(Slivers(), k);
            Assert.Equal(MeshRobustRepair.NotCleanReason(Slivers(), false), MeshRobustRepair.NotCleanReason(b, false));
        }
    }
}
