using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// C5's union cover (#1631, Harness/BooleanCertificate.Coverage.cs): a triangle that no single triangle of the other mesh covers within
    /// Delta is covered by several together. The cases of the rule itself (tiles, a missing tile, tiles lifted by less and more than
    /// Delta on either side, a triangle that sticks out by less and more than Delta, a steep triangle), the repro of the issue (a sphere
    /// and its mirror image), and the negative controls on it: a piece removed or moved by more than Delta must not certify.
    /// </summary>
    public class BooleanCertificateCoverTests
    {
        readonly ITestOutputHelper log;
        public BooleanCertificateCoverTests(ITestOutputHelper log) { this.log = log; }

        // points on a grid of 2^-20 (exact), so that the shifts below are exact rationals
        const long S = 1L << 20;
        static ExactPoint3 P(double x, double y, double z) =>
            ExactPoint3.Create(new BigInteger(Math.Round(x * S)), new BigInteger(Math.Round(y * S)), new BigInteger(Math.Round(z * S)), S);

        static ExactPoint3[] T(params double[] c) => new[] { P(c[0], c[1], c[2]), P(c[3], c[4], c[5]), P(c[6], c[7], c[8]) };

        // two tiles of the square [0, 1]^2 at height z, split along the diagonal
        static List<ExactPoint3[]> Tiles(double z0, double z1 = double.NaN)
        {
            double z = double.IsNaN(z1) ? z0 : z1;
            return new List<ExactPoint3[]> { T(0, 0, z0, 1, 0, z0, 1, 1, z), T(0, 0, z0, 1, 1, z, 0, 1, z0) };
        }

        // one big triangle over the square, bigger by `out` around it: corners (-o, -o), (2 + o, -o), (-o, 2 + o): contains the square
        static ExactPoint3[] Big(double o, double z = 0) => T(-o, -o, z, 2 + o, -o, z, -o, 2 + o, z);

        // 1/8 of Delta in units of 2^-20: Delta 0.001 here
        const double Delta = 0.001;

        [Fact]
        public void TwoTiles_CoverATriangleThatNeitherCoversAlone()
        {
            // (the triangle (0,0) (1,0) (0,1) at z = 0 is covered by the tiles together, not by either)
            ExactPoint3[] t = T(0.1, 0.1, 0, 0.8, 0.1, 0, 0.1, 0.8, 0);
            Assert.True(BooleanCertificate.UnionCovers(t, Tiles(0), Delta, out bool left));
            Assert.False(left);
        }

        [Fact]
        public void AMissingTile_LeavesATriangleUncovered()
        {
            ExactPoint3[] t = T(0.1, 0.1, 0, 0.8, 0.1, 0, 0.1, 0.8, 0);
            Assert.False(BooleanCertificate.UnionCovers(t, Tiles(0).Take(1).ToList(), Delta, out _));
            Assert.False(BooleanCertificate.UnionCovers(t, Tiles(0).Skip(1).ToList(), Delta, out _));
            // (with a third tile that does not cover the gap)
            var far = Tiles(0); far.RemoveAt(1); far.Add(T(5, 5, 0, 6, 5, 0, 5, 6, 0));
            Assert.False(BooleanCertificate.UnionCovers(t, far, Delta, out _));
        }

        [Theory]
        [InlineData(0.5, true)]
        [InlineData(0.99, true)]
        [InlineData(1.01, false)]
        [InlineData(1.5, false)]
        public void TilesLiftedAboveOrBelow_CoverWithinDeltaOnBothSides(double inDeltas, bool covered)
        {
            ExactPoint3[] t = T(0.1, 0.1, 0, 0.8, 0.1, 0, 0.1, 0.8, 0);
            foreach (double sign in new[] { 1.0, -1.0 })
                Assert.Equal(covered, BooleanCertificate.UnionCovers(t, Tiles(sign * inDeltas * Delta), Delta, out _));
        }

        // a tilted tile pair: the gap to t changes along the tiles, it is within Delta only over a part
        [Fact]
        public void TilesThatLeaveTheBandPartway_ArePartlyCovered_SoNotCovered()
        {
            // z rises from 0 to 4 Delta over the square on the second tile's corner (1, 1) and the first tile's: the gap exceeds Delta over most of the triangle
            var tiles = new List<ExactPoint3[]> { T(0, 0, 0, 1, 0, 4 * Delta, 1, 1, 4 * Delta), T(0, 0, 0, 1, 1, 4 * Delta, 0, 1, 0) };
            ExactPoint3[] t = T(0.1, 0.1, 0, 0.8, 0.1, 0, 0.1, 0.8, 0);
            Assert.False(BooleanCertificate.UnionCovers(t, tiles, Delta, out _));
        }

        [Fact]
        public void ATriangleThatSticksOutOfTheTiles_IsCoveredByTheLeftoverOnlyWithinDelta()
        {
            var tiles = Tiles(0);
            // the corner (0, 0) pushed out by 0.4 Delta along x: the sliver that no tile covers is within Delta of one tile
            ExactPoint3[] inside = T(-0.4 * Delta, 0, 0, 1, 0, 0, 0, 1, 0);
            Assert.True(BooleanCertificate.UnionCovers(inside, tiles, Delta, out bool left));
            Assert.True(left, "decided through the leftover polygons");
            ExactPoint3[] outside = T(-1.5 * Delta, 0, 0, 1, 0, 0, 0, 1, 0);
            Assert.False(BooleanCertificate.UnionCovers(outside, tiles, Delta, out _));
        }

        // the same on tilted planes, with the axis of the chart each of the three coordinates: the corners lifted from the chart onto the plane of t
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void TiltedPlanes_TheLeftoverIsLiftedOntoThePlaneOfTheTriangle(int axis)
        {
            // the plane c = 0.25 a + 0.5 b (a, b, c the coordinates in the order axis + 1, axis + 2, axis), so that the largest normal component is c's
            double[] Pt(double a, double b) => new[] { a, b, 0.25 * a + 0.5 * b };
            ExactPoint3 At(double[] q) { var x = new double[3]; x[(axis + 1) % 3] = q[0]; x[(axis + 2) % 3] = q[1]; x[axis] = q[2]; return P(x[0], x[1], x[2]); }
            ExactPoint3[] Tri(double[] a, double[] b, double[] c) => new[] { At(a), At(b), At(c) };
            var tiles = new List<ExactPoint3[]> { Tri(Pt(0, 0), Pt(1, 0), Pt(1, 1)), Tri(Pt(0, 0), Pt(1, 1), Pt(0, 1)) };
            ExactPoint3[] inside = Tri(Pt(-0.4 * Delta, 0), Pt(1, 0), Pt(0, 1));
            Assert.True(BooleanCertificate.UnionCovers(inside, tiles, Delta, out bool left));
            Assert.True(left, "through the leftover polygons");
            Assert.False(BooleanCertificate.UnionCovers(Tri(Pt(-1.5 * Delta, 0), Pt(1, 0), Pt(0, 1)), tiles, Delta, out _));
            // (the triangle itself on the plane, covered by the tiles alone)
            Assert.True(BooleanCertificate.UnionCovers(Tri(Pt(0.1, 0.1), Pt(0.8, 0.1), Pt(0.1, 0.8)), tiles, Delta, out bool alone));
            Assert.False(alone);
        }

        [Fact]
        public void ASteepTile_IsSkipped_NotCountedAsCover()
        {
            // a vertical tile over the square's diagonal: its chart along z collapses (no region); the flat tile alone does not cover the triangle
            var tiles = new List<ExactPoint3[]> { T(0, 0, 0, 1, 0, 0, 1, 1, 0), T(0, 0, 0, 1, 1, 0, 1, 1, 1) };
            ExactPoint3[] t = T(0.1, 0.1, 0, 0.8, 0.1, 0, 0.1, 0.8, 0);
            Assert.False(BooleanCertificate.UnionCovers(t, tiles, Delta, out _));
        }

        [Fact]
        public void OverlappingTilesAreNotCountedTwice_ADuplicatedHalfDoesNotCoverTheWhole()
        {
            // the first tile twice: the sum of their areas is a whole square's, the union is half of it
            var tiles = new List<ExactPoint3[]> { Tiles(0)[0], Tiles(0)[0] };
            ExactPoint3[] t = T(0.1, 0.1, 0, 0.8, 0.1, 0, 0.1, 0.8, 0);
            Assert.False(BooleanCertificate.UnionCovers(t, tiles, Delta, out _));
        }

        // ---------------------------------------------------------------- the repro of #1631: a sphere and its mirror image

        static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
        static DMesh3 Mirror(DMesh3 m)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) { Vector3d p = r.GetVertex(v); r.SetVertex(v, new Vector3d(-p.x, p.y, p.z)); }
            r.ReverseOrientation(false);
            return r;
        }

        static BooleanCertificate SphereAndMirror(int edgeVertices)
        {
            DMesh3 s = Sphere(edgeVertices);
            return BooleanCertificate.Run(s, Mirror(s), Ops.Union, out _);
        }

        [Fact]
        public void SphereWithItsMirror_CertifiesAtTheDefaultDepth_ByTheUnionCover()
        {
            BooleanCertificate c = SphereAndMirror(7);
            c.UnionCover = false;
            Assert.Equal("", c.Check());
            int before = c.Uncovered;
            Assert.True(before > 0, "the repro must be uncovered without the union cover: the test would prove nothing");
            c.UnionCover = true;
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.Uncovered);
            Assert.Equal(before, c.UnionCovered);
            log.WriteLine($"{before} triangles uncovered without the union cover, {c.UnionCovered} decided by it");
        }

        // E scaled about the origin by 1 + f * Delta (exact): every piece of the result before rounding moved by about f Delta along its normal
        static void ScaleExact(BooleanCertificate c, double f)
        {
            BigInteger den = BigInteger.One << 60, num = new BigInteger(f * c.Delta * (double)(1L << 60));
            for (int v = 0; v < c.AssembledExact.Length; ++v) {
                ExactPoint3 p = c.AssembledExact[v];
                c.AssembledExact[v] = ExactPoint3.Create(p.X * (den + num), p.Y * (den + num), p.Z * (den + num), p.W * den);
            }
        }

        [Theory]
        [InlineData(0.5, true)]
        [InlineData(-0.5, true)]
        [InlineData(1.5, false)]
        [InlineData(-1.5, false)]
        public void ThePieces_MovedByLessOrMoreThanDelta_CertifyOrNot(double f, bool certifies)
        {
            BooleanCertificate c = SphereAndMirror(5);
            // (the sphere's exact radius is below 1 where the faces are flat: shifting by f Delta moves the faces' distance to R by about f Delta)
            ScaleExact(c, f);
            string r = c.CheckCoverageOnly();
            Assert.True(certifies == (r == "" && c.Uncovered == 0), $"f = {f}: '{r}', uncovered {c.Uncovered}");
        }

        // a piece of the exact result removed: the result triangles over it have a hole in the union. (Only pieces wider than 20 Delta: the mirror contact
        // leaves slivers of a few ulps among the pieces, and a hole that narrow is within Delta of its neighbours, so covered, rightly.)
        [Fact]
        public void ARemovedPiece_IsNotCovered_WithOrWithoutTheUnionCover()
        {
            BooleanCertificate probe = SphereAndMirror(5);
            var wide = new List<int>();
            foreach (int t in probe.Assembled.TriangleIndices()) {
                probe.Assembled.GetTriVertices(t, ref a_, ref b_, ref c_);
                double longest = Math.Max((a_ - b_).Length, Math.Max((b_ - c_).Length, (c_ - a_).Length));
                double altitude = Vector3d.Cross(b_ - a_, c_ - a_).Length / longest;
                if (altitude > 20 * probe.Delta) wide.Add(t);
            }
            Assert.True(wide.Count >= 12, $"{wide.Count} wide pieces");
            foreach (bool union in new[] { true, false }) {
                int caught = 0, tried = 0;
                foreach (int t in wide.Where((_, i) => i % Math.Max(1, wide.Count / 3) == 0).Take(3)) {
                    BooleanCertificate c = SphereAndMirror(5);
                    c.UnionCover = union;
                    Assert.Equal(MeshResult.Ok, c.Assembled.RemoveTriangle(t, true));
                    string r = c.CheckCoverageOnly();
                    tried++;
                    if (r != "" || c.Uncovered > 0) caught++;
                }
                Assert.True(tried >= 3 && caught == tried, $"union {union}: {caught} of {tried}");
            }
        }

        Vector3d a_, b_, c_;
    }
}
