using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #873: MeshHausdorffDistance on coplanar (and parallel, nearly touching) meshes. The float distance of a point on the other
    /// mesh was the square root of the rounding noise of the squared-distance form (about 1e-9 instead of 0), and the nearest
    /// triangle was picked by that noise too, so Upper, never below the computed Lower, stalled at about 2e-9 with Converged
    /// false for any absolute tolerance below that. The distances are now the lengths to the closest points, |p - c|.
    /// </summary>
    public class HausdorffCoplanarTests
    {
        // n x n cells of the unit square in z, two triangles per cell; the interior vertices jittered in x, y by up to jitter of a
        // cell (seeded); the boundary unchanged, so grids of any n cover the same square
        internal static DMesh3 Grid(int n, double jitter, int seed, double z = 0)
        {
            var rnd = new Random(seed);
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    if (i > 0 && i < n && j > 0 && j < n) {
                        x += jitter / n * (2 * rnd.NextDouble() - 1);
                        y += jitter / n * (2 * rnd.NextDouble() - 1);
                    }
                    m.AppendVertex(new Vector3d(x, y, z));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static MeshHausdorffDistance Run(DMesh3 a, DMesh3 b, double tol, bool cert, bool symmetric = true)
        {
            var h = new MeshHausdorffDistance(a, b) { Tolerance = tol, MaxSubdivisions = 200000, CertifiedUpper = cert, Symmetric = symmetric };
            Assert.True(h.Compute());
            return h;
        }

        /// <summary> the issue's repro: grids of 4 and 8 cells against 5 and 9 (jittered or not) in the same plane, exact distance 0.
        /// Was Converged false with UpperBound 3.7e-9 to 6.5e-9 </summary>
        [Theory]
        [InlineData(4, 0.0, false)] [InlineData(4, 0.0, true)]
        [InlineData(8, 0.0, false)] [InlineData(8, 0.0, true)]
        [InlineData(8, 0.3, false)] [InlineData(8, 0.3, true)]
        public void CoplanarGrids_ConvergeToZero(int n, double jitter, bool cert)
        {
            const double tol = 1e-12;
            var h = Run(Grid(n, 0, 1), Grid(n + 1, jitter, 2), tol, cert);
            Assert.True(h.Converged, $"[{h.LowerBound:R}, {h.UpperBound:R}] after {h.Subdivisions}");
            Assert.Equal(0, h.LowerBound);
            Assert.True(h.UpperBound >= 0 && h.UpperBound <= tol, $"{h.UpperBound:R}");
            Assert.True(h.Subdivisions < 50000, $"{h.Subdivisions}");
        }

        /// <summary> one mesh 2^-33 (about 1.2e-10, exact in doubles) above the other, so the exact distance is 2^-33 (every point
        /// projects into the other square): the interval contains it and is within the tolerance, one-sided both ways and
        /// symmetric. Was stalled at the noise floor (about 1e-9 above it) </summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void ParallelGrids_IntervalContainsTheExactDistance(bool cert)
        {
            const double tol = 1e-13, exact = 1.0 / (1L << 33);
            DMesh3 a = Grid(6, 0.2, 3), b = Grid(7, 0.3, 4, exact);
            foreach (var (from, to, sym) in new[] { (a, b, true), (a, b, false), (b, a, false) }) {
                var h = Run(from, to, tol, cert, sym);
                Assert.True(h.Converged, $"{sym}: [{h.LowerBound:R}, {h.UpperBound:R}] after {h.Subdivisions}");
                Assert.True(h.LowerBound <= exact && exact <= h.UpperBound, $"{sym}: [{h.LowerBound:R}, {h.UpperBound:R}]");
                Assert.True(h.UpperBound - h.LowerBound <= tol, $"{sym}: [{h.LowerBound:R}, {h.UpperBound:R}]");
            }
        }

        /// <summary> the contract Converged => UpperBound - LowerBound <= Tolerance, at tolerances down to below the subdivision
        /// points' rounding (where triangles get stuck with their bounds), on parallel grids, whose computed and certified lower
        /// bounds agree (2^-33, exact), so Compute's own gap check does not cover a wrong Converged of a direction </summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void ParallelGrids_ConvergedOnlyWithinTheTolerance(bool cert)
        {
            const double exact = 1.0 / (1L << 33);
            DMesh3 a = Grid(4, 0.2, 18), b = Grid(5, 0.3, 19, exact);
            int converged = 0, open = 0;
            foreach (bool sym in new[] { true, false })
                foreach (double tol in new[] { 1e-17, 1e-16, 4e-16, 1e-15, 1e-14 }) {
                    var h = Run(a, b, tol, cert, sym);
                    Assert.True(h.LowerBound <= exact && exact <= h.UpperBound, $"{sym} {tol}: [{h.LowerBound:R}, {h.UpperBound:R}]");
                    if (h.Converged) { converged++; Assert.True(h.UpperBound - h.LowerBound <= tol, $"{sym} {tol}: [{h.LowerBound:R}, {h.UpperBound:R}]"); }
                    else open++;
                }
            Assert.True(converged > 0 && open > 0, $"{converged} converged, {open} not");
        }

        /// <summary> under every similarity: bit for bit at the exact ones (2^k scales); otherwise the vertices round off the plane by
        /// about a resolution, and the result converges within a tolerance of a few resolutions </summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void Coplanar_UnderEverySimilarity(bool cert)
        {
            DMesh3 a0 = Grid(4, 0.2, 5), b0 = Grid(5, 0.3, 6);
            const double tol0 = 1e-12;
            var h0 = Run(a0, b0, tol0, cert);
            Assert.True(h0.Converged && h0.LowerBound == 0 && h0.UpperBound <= tol0, $"{h0.UpperBound:R}");
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = new DMesh3(a0), b = new DMesh3(b0);
                MeshTransforms.PerVertexTransform(a, s.Apply);
                MeshTransforms.PerVertexTransform(b, s.Apply);
                double tol = s.IsExact ? tol0 * s.Scale : 64 * s.Resolution(1) * s.Scale;
                var h = Run(a, b, tol, cert);
                Assert.True(h.Converged, $"{s.Name}: [{h.LowerBound:R}, {h.UpperBound:R}] after {h.Subdivisions}");
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    Assert.Equal(h0.Subdivisions, h.Subdivisions);
                } else {
                    // (the rounded vertices are within a few resolutions of the plane, so is the exact distance)
                    Assert.True(h.LowerBound <= h.UpperBound && h.UpperBound <= 2 * tol, $"{s.Name}: [{h.LowerBound:R}, {h.UpperBound:R}] tol {tol:R}");
                }
            }
        }

        /// <summary> computing twice on one instance, a fresh instance and a serial run give the same result </summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void Coplanar_Reuse_Serial_Same(bool cert)
        {
            DMesh3 a = Grid(6, 0.2, 7), b = Grid(7, 0.3, 8);
            var h = Run(a, b, 1e-12, cert);
            Assert.True(h.Converged && h.UpperBound <= 1e-12);
            double lo = h.LowerBound, up = h.UpperBound; int sub = h.Subdivisions;
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound); Assert.Equal(up, h.UpperBound); Assert.Equal(sub, h.Subdivisions);
            var fresh = Run(a, b, 1e-12, cert);
            Assert.Equal(lo, fresh.LowerBound); Assert.Equal(up, fresh.UpperBound); Assert.Equal(sub, fresh.Subdivisions);
            gParallel.SerialOnThisThread = true;
            try {
                var serial = Run(a, b, 1e-12, cert);
                Assert.Equal(lo, serial.LowerBound); Assert.Equal(up, serial.UpperBound); Assert.Equal(sub, serial.Subdivisions);
            } finally { gParallel.SerialOnThisThread = false; }
        }

        /// <summary> the certified upper bound still holds on touching meshes: a tilted copy of a grid, B, crossing the plane of A
        /// (exact distances checked at A's vertices and B's vertices, CertifiedPointMeshDistance) </summary>
        [Fact]
        public void TouchingGrids_CertifiedUpperHolds()
        {
            DMesh3 a = Grid(6, 0.2, 9), b = Grid(7, 0.3, 10);
            foreach (int v in b.VertexIndices().ToList()) {
                Vector3d p = b.GetVertex(v);
                b.SetVertex(v, new Vector3d(p.x, p.y, 1e-9 * (p.x - 0.5)));
            }
            var h = Run(a, b, 1e-13, true);
            Assert.True(h.Converged, $"[{h.LowerBound:R}, {h.UpperBound:R}] after {h.Subdivisions}");
            Assert.True(h.UpperBound - h.LowerBound <= 1e-13);
            // (the exact distance is at least that of every vertex; B's corners at x = 0, 1 are 5e-10 off A's plane)
            Assert.True(h.UpperBound >= 5e-10 * (1 - 1e-15));
            foreach (int v in a.VertexIndices())
                Assert.True(CertifiedPointMeshDistance.AtLeastDistance(b, a.GetVertex(v), h.UpperBound));
            foreach (int v in b.VertexIndices())
                Assert.True(CertifiedPointMeshDistance.AtLeastDistance(a, b.GetVertex(v), h.UpperBound));
            // (and at points inside B's triangles: rounded onto them by about 1e-16, far below the upper bound's margin of
            // 1e-13 over the vertices' distances)
            var rnd = new Random(15);
            foreach (int t in b.TriangleIndices()) {
                Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
                b.GetTriVertices(t, ref v0, ref v1, ref v2);
                double s1 = rnd.NextDouble(), s2 = rnd.NextDouble() * (1 - s1);
                Vector3d p = v0 + s1 * (v1 - v0) + s2 * (v2 - v0);
                Assert.True(CertifiedPointMeshDistance.AtLeastDistance(a, p, h.UpperBound + 1e-15));
            }
            // the worst point's exact distance is within the interval
            Assert.True(CertifiedPointMeshDistance.AtLeastDistance(h.WorstPointOnB ? a : b, h.WorstPoint, h.UpperBound + 1e-15));
        }

        /// <summary> tolerances of a few ulps, near the rounding of the distances of points on the other mesh (about 1.1e-16 at
        /// coordinates of 1) and of the subdivision points: the run stays bounded (refinement stops at a stuck triangle's bound, which
        /// Upper keeps anyway; the second pass, against the certified lower bound, is capped by the first) and the interval holds,
        /// converged or not </summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void UlpTolerance_StaysBounded(bool cert)
        {
            DMesh3 a = Grid(4, 0.2, 16), b = Grid(5, 0.3, 17);
            var coarse = Run(a, b, 1e-12, cert);
            foreach (double tol in new[] { 1e-16, 4e-16, 1e-15, 2e-15, 4e-15, 1e-14, 1e-13 }) {
                var h = Run(a, b, tol, cert);
                Assert.Equal(0, h.LowerBound);
                Assert.True(h.Subdivisions <= 8 * coarse.Subdivisions + 2048, $"{tol}: {h.Subdivisions} vs {coarse.Subdivisions}");
                Assert.True(h.UpperBound >= 0 && h.UpperBound < 1, $"{tol}: {h.UpperBound:R}");
                if (h.Converged) Assert.True(h.UpperBound - h.LowerBound <= tol);
                // (from about 20 ulps the subdivision points' rounding no longer holds it open)
                if (tol >= 4e-15) Assert.True(h.Converged && h.UpperBound <= tol, $"{tol}: [{h.LowerBound:R}, {h.UpperBound:R}]");
            }
        }
    }
}
