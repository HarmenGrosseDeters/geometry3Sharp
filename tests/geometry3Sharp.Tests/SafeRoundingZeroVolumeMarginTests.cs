using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> the noise margin of the numerically-zero-volume rule (#1067), on the decision itself with synthetic numbers (a separate file: it needs the helper, the sheet tests run against the base library) </summary>
    public class SafeRoundingZeroVolumeMarginTests
    {
        static double Ulp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;

        /// <summary>
        /// the margin covers the error of each triple product, not only the rounding of the sum: a flat sheet of about 1.2 ulp near the
        /// origin from many triangles has terms that cancel to far below their size, so a margin from the terms' absolute sum (the old
        /// one, (n + 8) u sum |term|) is nearly zero and would remove it; the bound from the lengths (n + 16) u sum |pa-o||pb-o||pc-o|
        /// keeps it. The numbers are those of a unit sheet of 2000 triangles at coordinates of 0.5
        /// </summary>
        [Fact]
        public void FlatSheetNearTheOrigin_WithManyTriangles_Stays()
        {
            const double u = 1.1102230246251565e-16;
            double largest = 0.5, area = 1, count = 2000;
            // (the true thickness is 1.2 ulp; the computed sum of the cancelling terms came out at 0.5 ulp, within the error of
            // the triple products: terms of about 0.1 each, errors of u * 0.1 each, 2000 of them)
            double computed = 0.5 * Ulp(largest) * area / 2, absTerms = 1e-16, tripleNorms = count * 0.125;
            Assert.True(2 * (computed + (count + 8) * u * absTerms) < Ulp(largest) * area, "the old margin removes it");
            Assert.False(MeshRoundingRepair.numerically_zero_volume(computed, area, largest, (int)count, tripleNorms));
            // a vanishing volume of a small sheet is still removed
            Assert.True(MeshRoundingRepair.numerically_zero_volume(0, 6.5e-6, 0.3, 8, 5e-8));
            Assert.False(MeshRoundingRepair.numerically_zero_volume(double.NaN, 1, 0.5, 8, 1));
        }
    }
}
