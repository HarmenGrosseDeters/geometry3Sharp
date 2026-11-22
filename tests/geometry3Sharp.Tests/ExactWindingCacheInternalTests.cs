using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Internal-hook tests for <see cref="ExactWindingCache"/> invariants (boundary reduction,
    /// unwelded soup cancellation, direct-sum fallback threshold, and parity with legacy <c>WindingCache</c>).
    /// </summary>
    public class ExactWindingCacheInternalTests
    {
        static DMesh3 Sphere(int edgeVertices = 12)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 1.0, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        [Fact]
        public void ClosedAndUnweldedClosedMeshes_HaveZeroExteriorBoundaryAndDoNotUseDirectSum()
        {
            DMesh3 closed = Sphere(12);
            var closedTree = new DMeshAABBTree3(closed, true);
            ExactWindingCache closedCache = closedTree.GetExactWindingCache();
            Assert.Equal(0, closedCache.BoundaryCount);
            Assert.False(closedCache.UseDirectSum);
            Assert.Equal(closed.TriangleCount, closedCache.TriangleCount);

            // Unwelded STL-style soup of the same closed mesh must cancel every coincident opposite edge to 0
            DMesh3 unwelded = new DMesh3();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in closed.TriangleIndices()) {
                closed.GetTriVertices(tid, ref a, ref b, ref c);
                unwelded.AppendTriangle(unwelded.AppendVertex(a), unwelded.AppendVertex(b), unwelded.AppendVertex(c));
            }
            var unweldedTree = new DMeshAABBTree3(unwelded, true);
            ExactWindingCache unweldedCache = unweldedTree.GetExactWindingCache();
            Assert.Equal(0, unweldedCache.BoundaryCount);
            Assert.False(unweldedCache.UseDirectSum);
        }

        [Fact]
        public void DisconnectedSoup_SelectsDirectSumFallback_AndOpenMeshDoesNot()
        {
            DMesh3 open = Sphere(12);
            foreach (int tid in open.TriangleIndices().ToArray())
                if (open.GetTriCentroid(tid).z > 0.4)
                    open.RemoveTriangle(tid, true, false);
            var openTree = new DMeshAABBTree3(open, true);
            ExactWindingCache openCache = openTree.GetExactWindingCache();
            Assert.True(openCache.BoundaryCount > 0);
            Assert.False(openCache.UseDirectSum);

            var rnd = new Random(19);
            DMesh3 soup = new DMesh3();
            for (int i = 0; i < 100; ++i) {
                Vector3d o = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
                Vector3d d1 = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.2;
                Vector3d d2 = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.2;
                soup.AppendTriangle(soup.AppendVertex(o), soup.AppendVertex(o + d1), soup.AppendVertex(o + d2));
            }
            var soupTree = new DMeshAABBTree3(soup, true);
            ExactWindingCache soupCache = soupTree.GetExactWindingCache();
            Assert.True(soupCache.UseDirectSum);
        }

        [Fact]
        public void ExactCacheMatchesLegacyWindingCache_OnOpenAndClosedMeshes()
        {
            DMesh3 open = Sphere(12);
            foreach (int tid in open.TriangleIndices().ToArray())
                if (open.GetTriCentroid(tid).z > 0.35)
                    open.RemoveTriangle(tid, true, false);

            foreach (DMesh3 m in new[] { Sphere(12), open }) {
                var exactTree = new DMeshAABBTree3(m, true);
                var legacyTree = new DMeshAABBTree3(m, true) { ForceLegacyWindingCache = true };
                var rnd = new Random(29);
                for (int i = 0; i < 150; ++i) {
                    Vector3d p = new Vector3d(rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5);
                    double e = exactTree.WindingNumber(p);
                    double l = legacyTree.WindingNumber(p);
                    Assert.True(Math.Abs(e - l) < 1e-11, $"at {p}: exact={e:R} vs legacy={l:R}");
                }
            }
        }
        [Fact]
        public void BoundaryEdgeAngleX_TiltedEdgesNearBranchCut_DoesNotFlipDenominatorVia2DOx()
        {
            // Blocker #1 regression: when oyz != 0, den = da*db + dotYZ has an independent sign from 2D ox.
            // Case 1: a=(-1,-1,-1), b=(1,1,1), p=(9.9e-14, 1e-13, -1e-13) -> num ≈ +4e-13, den ≈ +2.2863e-13, angle ≈ 1.0517 rad.
            double w1 = ExactWindingCache.boundary_edge_angle_x(
                new Vector3d(9.9e-14, 1e-13, -1e-13),
                new Vector3d(-1.0, -1.0, -1.0),
                new Vector3d(1.0, 1.0, 1.0));
            Assert.True(Math.Abs(w1 - 1.0517) < 2e-3, $"Case 1 expected ~1.0517 rad, got {w1:R}");

            // Case 2: a=(-100,-1,-1), b=(100,1,1), p=(9.9e-12, 1e-13, -1e-13) -> num ≈ +4e-13, den ≈ +3.9596e-13, angle ≈ 0.7904 rad.
            double w2 = ExactWindingCache.boundary_edge_angle_x(
                new Vector3d(9.9e-12, 1e-13, -1e-13),
                new Vector3d(-100.0, -1.0, -1.0),
                new Vector3d(100.0, 1.0, 1.0));
            Assert.True(Math.Abs(w2 - 0.7904) < 2e-3, $"Case 2 expected ~0.7904 rad, got {w2:R}");

            // Case 3: opposite side p=(-9.9e-12, -1e-13, 1e-13) -> num ≈ -4e-13, den ≈ -3.9596e-13, angle ≈ -2.3512 rad.
            double w3 = ExactWindingCache.boundary_edge_angle_x(
                new Vector3d(-9.9e-12, -1e-13, 1e-13),
                new Vector3d(-100.0, -1.0, -1.0),
                new Vector3d(100.0, 1.0, 1.0));
            Assert.True(Math.Abs(w3 - (-2.3512)) < 2e-3, $"Case 3 expected ~-2.3512 rad, got {w3:R}");

            // Antisymmetry under edge reversal
            double w2Rev = ExactWindingCache.boundary_edge_angle_x(
                new Vector3d(9.9e-12, 1e-13, -1e-13),
                new Vector3d(100.0, 1.0, 1.0),
                new Vector3d(-100.0, -1.0, -1.0));
            Assert.Equal(-w2, w2Rev);
        }

        [Fact]
        public void BoundaryEdgeAngleX_MixedExponentsAndUnderflow_ExactAndAntisymmetric()
        {
            double p500 = BitConverter.Int64BitsToDouble((long)(500 + 1023) << 52);
            double m600 = BitConverter.Int64BitsToDouble((long)(-600 + 1023) << 52);
            double m700 = BitConverter.Int64BitsToDouble((long)(-700 + 1023) << 52);
            double p100 = BitConverter.Int64BitsToDouble((long)(100 + 1023) << 52);
            double p99 = BitConverter.Int64BitsToDouble((long)(99 + 1023) << 52);
            double m1000 = BitConverter.Int64BitsToDouble((long)(-1000 + 1023) << 52);
            double m1001 = BitConverter.Int64BitsToDouble((long)(-1001 + 1023) << 52);
            double p200 = BitConverter.Int64BitsToDouble((long)(200 + 1023) << 52);
            Vector3d zero = Vector3d.Zero;

            // Blocker #2a: both +x huge, yz tiny -> da*db << |dotYZ|, atan2(-1, -1) = -3*pi/4
            double a1 = ExactWindingCache.boundary_edge_angle_x(zero, new Vector3d(p500, m600, 0.0), new Vector3d(p500, -m600, m600));
            Assert.True(Math.Abs(a1 - (-0.75 * Math.PI)) < 1e-14, $"Expected -3pi/4, got {a1:R}");

            // Blocker #2b: A=(1, 2^-600, 0), B=(1, 2^-600, 2^-700) -> nearly aligned +y, angle ≈ 0
            double a2 = ExactWindingCache.boundary_edge_angle_x(zero, new Vector3d(1.0, m600, 0.0), new Vector3d(1.0, m600, m700));
            Assert.True(Math.Abs(a2) < 1e-14, $"Expected ~0, got {a2:R}");

            // Blocker #2c: A=(2^500, 2^-600, 0), B=(-2^500, -2^-600, 2^-600) -> da*db cancels dotYZ, angle = -pi/2
            double a3 = ExactWindingCache.boundary_edge_angle_x(zero, new Vector3d(p500, m600, 0.0), new Vector3d(-p500, -m600, m600));
            Assert.True(Math.Abs(a3 - (-0.5 * Math.PI)) < 1e-14, $"Expected -pi/2, got {a3:R}");

            // Blocker #2d: on_open_edge uz underflow with a.z != b.z -> exact +pi / -pi
            Vector3d ea = zero;
            Vector3d eb = new Vector3d(p100, p100, m1000);
            Vector3d mid = new Vector3d(p99, p99, m1001);
            double a4 = ExactWindingCache.boundary_edge_angle_x(mid, ea, eb);
            double a4Rev = ExactWindingCache.boundary_edge_angle_x(mid, eb, ea);
            Assert.Equal(Math.PI, a4);
            Assert.Equal(-Math.PI, a4Rev);

            // Blocker #2e: on_vertex_angle_x mixed exponent p == a, other=(2^500, 2^-600, 2^-600) -> +3*pi/4
            double a5 = ExactWindingCache.boundary_edge_angle_x(zero, zero, new Vector3d(p500, m600, m600));
            Assert.True(Math.Abs(a5 - (0.75 * Math.PI)) < 1e-14, $"Expected +3pi/4, got {a5:R}");

            // on_vertex_angle_x rz underflow when other.z != p.z (|other.z| / |other.y| < 2^-1075)
            double a6Pos = ExactWindingCache.boundary_edge_angle_x(zero, zero, new Vector3d(p500, p200, m1000));
            double a6Neg = ExactWindingCache.boundary_edge_angle_x(zero, zero, new Vector3d(p500, p200, -m1000));
            Assert.Equal(Math.PI, a6Pos);
            Assert.Equal(-Math.PI, a6Neg);

            // Issue #A: on_vertex_angle_x with relExp = -61 (dx = +-5e-19, dy = 1, dz = 1e-22) and relExp = -1200
            double vPos = ExactWindingCache.boundary_edge_angle_x(zero, zero, new Vector3d(5e-19, 1.0, 1e-22));
            double vNeg = ExactWindingCache.boundary_edge_angle_x(zero, zero, new Vector3d(-5e-19, 1.0, 1e-22));
            Assert.True(Math.Abs(vPos - (Math.PI - 2e-4)) < 1e-6, $"Expected ~pi-2e-4, got {vPos:R}");
            Assert.True(Math.Abs(vNeg - 2e-4) < 1e-6, $"Expected ~2e-4, got {vNeg:R}");

            double vExt = ExactWindingCache.boundary_edge_angle_x(zero, zero, new Vector3d(m1000, p200, m1000));
            Assert.True(Math.Abs(vExt - (0.75 * Math.PI)) < 1e-14, $"Expected +3pi/4, got {vExt:R}");

            // Issue #C: finish_edge_angle with overflowing b.x - a.x and subnormal b.z - a.z (a.y == b.y == 0)
            double sub = double.Epsilon;
            double cAng = ExactWindingCache.boundary_edge_angle_x(
                new Vector3d(-5e307, 0.0, 0.0),
                new Vector3d(-1e308, 0.0, -sub),
                new Vector3d(1e308, 0.0, sub));
            // EdgeSignX(0, 0, a, b): a.z < b.z => -1 => +pi (not on-open-edge)
            Assert.Equal(Math.PI, cAng);
        }

        [Fact]
        public void BoundaryEdgeAngleX_ExactBranchCut_ReturnsExactPiSignFromEdgeSignX()
        {
            // Minor #8: oyz == 0, dotYZ < 0, ox > 0 (ray strictly hits open segment ahead along +x) -> exact +-pi
            Vector3d p = Vector3d.Zero;
            Vector3d a = new Vector3d(2.0, -1.0, 0.0);
            Vector3d b = new Vector3d(2.0, 1.0, 0.0);
            // EdgeSignX(0, 0, a, b): a.z == b.z == 0, b.y > a.y => +1 => -pi; swapped => +pi
            Assert.Equal(-Math.PI, ExactWindingCache.boundary_edge_angle_x(p, a, b));
            Assert.Equal(Math.PI, ExactWindingCache.boundary_edge_angle_x(p, b, a));
        }

        [Fact]
        public void EvaluateFullRowX_DefensiveGuards_NonPositiveDxNonFiniteAndDirectSum()
        {
            // Minor #7: EvaluateFullRowX must safely handle ni == 0, dx <= 0, non-finite originX/dx/y/z,
            // and non-finite xi in both BVH and direct-sum modes.
            var closedTree = new DMeshAABBTree3(Sphere(8), true);
            ExactWindingCache closedCache = closedTree.GetExactWindingCache();
            var hits = new System.Collections.Generic.List<ExactWindingCache.LineHit>();
            var leaves = new System.Collections.Generic.List<int>();
            int[] diff = new int[8];
            float[] row = new float[8];

            // ni == 0 is a no-op
            row[0] = 42.0f;
            closedCache.EvaluateFullRowX(0.0, 0.25, 0, 0.0, 0.0, hits, leaves, diff, row);
            Assert.Equal(42.0f, row[0]);

            // Negative dx and zero dx fall back to exact per-sample EvaluateOnRowX
            foreach (double dx in new[] { -0.35, 0.0 }) {
                closedCache.EvaluateFullRowX(1.2, dx, 8, 0.1, -0.1, hits, leaves, diff, row);
                for (int i = 0; i < 8; ++i) {
                    double xi = (double)i * dx + 1.2;
                    float expected = (float)closedCache.Evaluate(new Vector3d(xi, 0.1, -0.1));
                    Assert.Equal(expected, row[i]);
                }
            }

            // Non-finite originX, dx, y, z produce 0 (no NaN / out-of-range)
            closedCache.EvaluateFullRowX(double.NaN, 0.25, 4, 0.0, 0.0, hits, leaves, diff, row);
            for (int i = 0; i < 4; ++i) Assert.Equal(0.0f, row[i]);

            closedCache.EvaluateFullRowX(0.0, double.PositiveInfinity, 4, 0.0, 0.0, hits, leaves, diff, row);
            // i = 0 has 0 * Inf = NaN -> 0; i > 0 has +Inf -> 0
            for (int i = 0; i < 4; ++i) Assert.Equal(0.0f, row[i]);

            closedCache.EvaluateFullRowX(double.MaxValue * 0.9, double.MaxValue * 0.5, 4, 0.0, 0.0, hits, leaves, diff, row);
            for (int i = 0; i < 4; ++i) Assert.False(float.IsNaN(row[i]) || float.IsInfinity(row[i]));

            // Direct-sum fallback (> 64 disconnected triangles) with overflowing xi
            DMesh3 soup = new DMesh3();
            for (int t = 0; t < 70; ++t) {
                double ox = t * 0.05;
                soup.AppendTriangle(
                    soup.AppendVertex(new Vector3d(ox, 0, 0)),
                    soup.AppendVertex(new Vector3d(ox + 0.02, 0, 0)),
                    soup.AppendVertex(new Vector3d(ox, 0.02, 0)));
            }
            ExactWindingCache directCache = new DMeshAABBTree3(soup, true).GetExactWindingCache();
            Assert.True(directCache.UseDirectSum);
            directCache.EvaluateFullRowX(double.MaxValue * 0.9, double.MaxValue * 0.5, 4, 0.2, 0.2, hits, leaves, diff, row);
            for (int i = 0; i < 4; ++i) Assert.False(float.IsNaN(row[i]) || float.IsInfinity(row[i]));
        }
    }
}
