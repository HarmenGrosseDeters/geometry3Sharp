using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DualContouring.DegenerateSplitsAvoided (#800), apart from DualContouringDegenerateSplitTests so that those still compile
    /// against a library without the counter (the new-tests-on-base check of the bugfix label runs them there).
    /// </summary>
    public class DualContouringDegenerateSplitCounterTests
    {
        [Fact]
        public void DegenerateSplitsAvoided_CountsTheChangedChoices_AndIsResetPerGenerate()
        {
            // the counter is positive exactly where the rule changed a split (these offsets had degenerate triangles before),
            // and a second Generate on the same instance gives the same count and the same mesh (reset, not accumulated)
            DualContouring dc;
            DMesh3 first = DualContouringDegenerateSplitTests.Contour(DualContouringDegenerateSplitTests.Offsets[1], Similarity.Identity, true, out dc);
            int n = dc.DegenerateSplitsAvoided;
            Assert.True(n > 0, $"{n}");
            dc.Generate();
            Assert.Equal(n, dc.DegenerateSplitsAvoided);
            Assert.Equal("", Invariance.Difference(first, dc.Mesh));
            // a box without the chamfer has no crease off the grid's axes: no split changed
            DualContouring plain;
            DualContouringDegenerateSplitTests.Contour(-1, Similarity.Identity, true, out plain);
            Assert.Equal(0, plain.DegenerateSplitsAvoided);
        }
        [Fact]
        public void DegenerateSplitsAvoided_IsPositive_WhereTheRuleChangedASplit()
        {
            foreach (double c in DualContouringDegenerateSplitTests.Offsets) {
                DualContouring dc;
                DualContouringDegenerateSplitTests.Contour(c, Similarity.Identity, true, out dc);
                Assert.True(dc.DegenerateSplitsAvoided > 0, $"c {c}");
            }
        }
    }
}
