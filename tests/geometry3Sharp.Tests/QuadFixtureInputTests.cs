using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The inputs of the quad pipeline tests are the same bits on every platform: their sines and cosines are correctly rounded (PortableMath,
    /// CoreMath) instead of the platform libm. The diagnosis (Windows net10.0 failed QuadRemesherCrossingTests gear/18, Linux passed): a 1-ulp change of
    /// one of the 64 values of the gear outline decides in 7 of 128 cases whether the extraction of gear/18 drops a face, so a test input built with
    /// Math.Sin / Math.Cos (the Windows CRT gives other last bits than glibc for a few % of arguments) changes a topology-sensitive result. Each input is
    /// pinned here, so a platform-dependent input fails as a named fixture; the remeshed meshes of the two cases where the extraction drops a face
    /// are pinned as well (the remesh passes are libm-free: a failure there names the remesher, not the input).
    /// </summary>
    public class QuadFixtureInputTests
    {
        // the pinned inputs (before the remesh passes) of the shapes of QuadRemesherClosedOutputTests and QuadCreasePreservationTests: both build the
        // same triangulations, and their builders check against this one table
        internal static readonly System.Collections.Generic.Dictionary<string, string> ShapeInputHashes = new System.Collections.Generic.Dictionary<string, string> {
            { "sharp-box", "89E4378B303389FF" }, { "sharp-cylinder", "ED035019366524F9" }, { "l-profile", "52390C384A8C36B6" }, { "gear", "A1310834CD2DAAE4" }
        };

        [Theory]
        [InlineData("sharp-box")]
        [InlineData("sharp-cylinder")]
        [InlineData("l-profile")]
        [InlineData("gear")]
        public void ClosedOutputShapes_ArePinned(string name)
        {
            // BuildShape checks the input before the passes against ShapeInputHashes
            DMesh3 m = QuadRemesherClosedOutputTests.Shape(name, 18);
            Assert.True(m.TriangleCount > 0);
        }

        [Theory]
        [InlineData("gear", 18, "8F12E88272F2CC9F")]
        [InlineData("sharp-box", 26, "AC17249FA7AC6D15")]
        public void RemeshedShapes_WhereTheExtractionDropsAFace_ArePinned(string name, int passes, string expected)
        {
            Assert.Equal(expected, PortableMath.Hash(QuadRemesherClosedOutputTests.Shape(name, passes)));
        }

        [Theory]
        [InlineData("sharp-box")]
        [InlineData("sharp-cylinder")]
        [InlineData("l-profile")]
        [InlineData("gear")]
        public void CreasePreservationShapes_ArePinned(string name)
        {
            DMesh3 m = QuadCreasePreservationTests.Shape(name);
            Assert.True(m.TriangleCount > 0);
        }

        [Theory]
        [InlineData(2, "10855BEA84FDEC0F")]
        [InlineData(3, "644D31E07A9ED950")]
        [InlineData(6, "D57204952048B75A")]
        [InlineData(24, "A3017904411E2B51")]
        [InlineData(50, "0778948BBA87CAD1")]
        public void OpenPatch_IsPinned(int n, string expected)
        {
            Assert.Equal(expected, PortableMath.Hash(CrossFieldShapes.OpenPatch(n)));
        }

        [Fact]
        public void Strips_ArePinned()
        {
            Assert.Equal("E29FE2B31484F3F9", PortableMath.Hash(QuadCollapseDefaultTests.Strip(100, 8, 0.1)));
            Assert.Equal("E29FE2B31484F3F9", PortableMath.Hash(QuadNonManifoldVertexTests.Strip(100, 8, 0.1)));
            Assert.Equal("8CE424F392364126", PortableMath.Hash(QuadRemesherTests.Strip(3)));
        }
    }
}
