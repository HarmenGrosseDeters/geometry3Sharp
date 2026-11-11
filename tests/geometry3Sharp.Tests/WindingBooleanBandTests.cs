using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshWindingNumberBoolean inside the band [2^-32, 2^32] of the extent takes the old path: no scaled copy is made and the result is the
    /// one of the unchanged inner routine on the same runtime (in its own file: it uses internals the base library does not have)
    /// </summary>
    public class WindingBooleanBandTests
    {
        static DMesh3 Octahedron(Vector3d c, double r)
        {
            var m = new DMesh3();
            Vector3d[] v = { c + new Vector3d(r, 0, 0), c + new Vector3d(-r, 0, 0), c + new Vector3d(0, r, 0), c + new Vector3d(0, -r, 0), c + new Vector3d(0, 0, r), c + new Vector3d(0, 0, -r) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            int[][] t = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            foreach (int[] f in t) m.AppendTriangle(f[0], f[1], f[2]);
            return m;
        }

        static MeshWindingNumberBoolean Make(double r, MeshWindingNumberBoolean.Operations op)
        {
            return new MeshWindingNumberBoolean(Octahedron(Vector3d.Zero, r), Octahedron(new Vector3d(0.5 * r, 0, 0), r), op) { GridResolution = 24 };
        }

        // the octahedra's extent is 2.5 r: r = 2^e is inside the band for 2.5 * 2^e in [2^-32, 2^32]: e from -33 (2^-31.7) to 30 (2^31.3)
        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference)]
        public void InsideTheBand_NoScaledCopyIsMade_AndTheResultIsTheUnscaledRoutines(MeshWindingNumberBoolean.Operations op)
        {
            foreach (int e in new[] { -33, -30, -10, 0, 10, 29, 30 }) {
                double r = Math.Pow(2, e);
                var viaCompute = Make(r, op);
                Assert.True(viaCompute.Compute());
                Assert.Equal(1.0, viaCompute.NormalizationScale);
                var direct = Make(r, op);
                Assert.True(direct.ComputeUnscaled());
                Assert.Equal("", Invariance.Difference(direct.Result, viaCompute.Result));
                Assert.Equal(direct.UsedCellSize, viaCompute.UsedCellSize);
            }
        }

        // the first sizes outside the band on both sides do scale
        [Theory]
        [InlineData(-35)]
        [InlineData(31)]
        [InlineData(100)]
        [InlineData(-100)]
        public void OutsideTheBand_ACopyIsScaledByAPowerOfTwo(int e)
        {
            double r = Math.Pow(2, e);
            var b = Make(r, MeshWindingNumberBoolean.Operations.Union);
            Assert.True(b.Compute());
            double s = b.NormalizationScale;
            Assert.NotEqual(1.0, s);
            Assert.True(s > 0);
            double m = Math.Log(s, 2);
            Assert.Equal(Math.Round(m), m, 12);            // a power of two
            double extent = 2.5 * r * s;
            Assert.InRange(extent, 1.0, 4.0);             // the unit frame
        }
    }
}
