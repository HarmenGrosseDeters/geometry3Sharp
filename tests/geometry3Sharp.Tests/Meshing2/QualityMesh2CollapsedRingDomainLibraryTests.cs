using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The library members of the fix for an input whose outer rings all collapse in snapping (QualityMesh2CollapsedRingDomainTests has
    /// the behaviour): SnapRounding2.OuterRingsCollapsed and the diagnostic that says why the domain is empty.
    /// </summary>
    public class QualityMesh2CollapsedRingDomainLibraryTests
    {
        static Mesh2Input ThinRing(bool square, bool squareIsHole = false) => QualityMesh2CollapsedRingDomainTests.ThinRing(square, squareIsHole);

        /// <summary> the flag itself: true only when the input has outer rings and all of them collapse </summary>
        [Theory]
        [InlineData("none", false)]
        [InlineData("holes only", false)]
        [InlineData("one of two", false)]
        [InlineData("all", true)]
        [InlineData("all, a hole survives", true)]
        public void SnapRounding_OuterRingsCollapsed(string which, bool expected)
        {
            var input = which == "one of two" ? ThinRing(true) : which == "all, a hole survives" ? ThinRing(true, true) : ThinRing(false);
            if (which == "none") input.Rings.Clear();
            if (which == "holes only") input.Rings[0] = new RingInput(input.Rings[0].Vertices, true);
            var snap = SnapRounding2.Apply(ConstrainedDelaunayMesh2Candidate.Convert(input), 1, System.Threading.CancellationToken.None);
            Assert.Equal(expected, snap.OuterRingsCollapsed);
            Assert.Equal(expected, snap.Diagnostics.Any(d => d.Contains("every outer ring collapsed")));
        }

        /// <summary> the diagnostics say why: the ring collapsed </summary>
        [Fact]
        public void ThinRing_Collapses_Reported()
        {
            var lib = ConstrainedDelaunayMesh2Candidate.Convert(ThinRing(false));
            var r = new QualityMesh2 { MinAngle = 20, SnapTolerance = 1 }.Compute(lib);
            Assert.Equal(Triangulation2Status.Empty, r.Status);
            Assert.Contains(r.Diagnostics, d => d.Contains("ring 0 collapsed below the tolerance"));
            Assert.Contains(r.Diagnostics, d => d.Contains("every outer ring collapsed below the tolerance; the domain is empty"));
            var c = new ConstrainedDelaunayMesh2 { SnapTolerance = 1 }.Compute(lib);
            Assert.Equal(Triangulation2Status.Empty, c.Status);
        }
    }
}
