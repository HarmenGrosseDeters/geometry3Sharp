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

        static DMesh3 OpenWavyPatch(int n)
        {
            DMesh3 m = new DMesh3();
            int[,] vid = new int[n + 1, n + 1];
            for (int iy = 0; iy <= n; ++iy) {
                double y = -1.0 + 2.0 * iy / n;
                for (int ix = 0; ix <= n; ++ix) {
                    double x = -1.0 + 2.0 * ix / n;
                    double z = 0.3 * Math.Sin(1.5 * x) * Math.Cos(1.5 * y);
                    vid[ix, iy] = m.AppendVertex(new Vector3d(x, y, z));
                }
            }
            for (int iy = 0; iy < n; ++iy) {
                for (int ix = 0; ix < n; ++ix) {
                    int v00 = vid[ix, iy], v10 = vid[ix + 1, iy], v01 = vid[ix, iy + 1], v11 = vid[ix + 1, iy + 1];
                    m.AppendTriangle(v00, v10, v11);
                    m.AppendTriangle(v00, v11, v01);
                }
            }
            return m;
        }

        [Fact]
        public void BoundaryTree_CertifiedBoundsHoldAndThresholdsMatchExact100Percent()
        {
            // 160x160 patch has 640 exterior boundary edges (>= BoundaryTreeMinEdges = 500)
            DMesh3 patch = OpenWavyPatch(160);
            // Also duplicate a boundary triangle to create Weight == 2 heavy edges alongside the 640-edge loop
            int firstTri = patch.TriangleIndices().First();
            Vector3d ta = Vector3d.Zero, tb = Vector3d.Zero, tc = Vector3d.Zero;
            patch.GetTriVertices(firstTri, ref ta, ref tb, ref tc);
            patch.AppendTriangle(patch.AppendVertex(ta), patch.AppendVertex(tb), patch.AppendVertex(tc));

            var tree = new DMeshAABBTree3(patch, true);
            ExactWindingCache cache = tree.GetExactWindingCache();
            Assert.True(cache.BoundaryCount >= ExactWindingCache.BoundaryTreeMinEdges);
            cache.PrepareThresholdQueries();
            Assert.True(cache.HasBoundaryTree);

            var rnd = new Random(103);
            int treeUsedCount = 0;
            int farTightCount = 0;
            double[] isos = new[] { -0.5, 0.0, 0.25, 0.5, 0.75, double.NaN, double.PositiveInfinity, double.NegativeInfinity };

            // Mix of far-field, near-patch, near-boundary, +x-cone-aligned, and on-surface points
            var points = new System.Collections.Generic.List<Vector3d>(650);
            for (int i = 0; i < 400; ++i)
                points.Add(new Vector3d(rnd.NextDouble() * 6.0 - 3.0, rnd.NextDouble() * 6.0 - 3.0, rnd.NextDouble() * 6.0 - 3.0));
            for (int i = 0; i < 150; ++i)
                points.Add(new Vector3d(rnd.NextDouble() * 2.2 - 1.1, rnd.NextDouble() * 2.2 - 1.1, rnd.NextDouble() * 0.6 - 0.3));
            // Points with +x rays aimed directly at the boundary rim (exercises cone rejection)
            for (int i = 0; i < 60; ++i)
                points.Add(new Vector3d(-2.5, -1.0 + 2.0 * i / 59.0, 0.0));
            // On-surface vertices and triangle centroids
            for (int i = 0; i < 20; ++i)
                points.Add(patch.GetVertex(i * 100));
            points.Add((ta + tb + tc) / 3.0);

            foreach (Vector3d p in points) {
                double exact = cache.Evaluate(p);
                // Verify exactness of EvaluateAbove / EvaluateAbsAbove for fixed isos AND near-exact isos (within 1e-15)
                foreach (double iso in isos) {
                    Assert.Equal(exact > iso, cache.EvaluateAbove(p, iso));
                    Assert.Equal(Math.Abs(exact) > iso, cache.EvaluateAbsAbove(p, iso));
                }
                double epsIso = 1e-15 * Math.Max(1.0, Math.Abs(exact));
                foreach (double nearIso in new[] { exact - epsIso, exact, exact + epsIso }) {
                    Assert.Equal(exact > nearIso, cache.EvaluateAbove(p, nearIso));
                }

                // Directly verify the certified bound |w_tree - w_exact| <= bErr (far from surface where surfaceCorrection == 0)
                if (Math.Abs(p.x) > 1.2 || Math.Abs(p.y) > 1.2 || Math.Abs(p.z) > 0.5) {
                    if (cache.TryEvaluateTreeBoundarySumX(p, out double bSum, out double bErr)) {
                        treeUsedCount++;
                        if (bErr < 0.05)
                            farTightCount++;
                        double diff = Math.Abs((exact - Math.Round(exact - bSum)) - bSum);
                        Assert.True(diff <= bErr + 1e-15 * Math.Max(1.0, Math.Abs(exact)),
                            $"Bound violated at {p}: diff={diff:R} > bErr={bErr:R}");
                    }
                }
            }
            Assert.True(treeUsedCount > 300, $"Expected BoundaryTree to certify far queries, got {treeUsedCount}");
            Assert.True(farTightCount > 250, $"Expected tight far-field bounds (bErr < 0.05), got {farTightCount}");

            // Reversed orientation of the same open patch must match exact negated thresholds 100%
            DMesh3 revPatch = new DMesh3(patch);
            revPatch.ReverseOrientation(false);
            ExactWindingCache revCache = new DMeshAABBTree3(revPatch, true).GetExactWindingCache();
            revCache.PrepareThresholdQueries();
            Assert.True(revCache.HasBoundaryTree);
            for (int i = 0; i < 80; ++i) {
                Vector3d p = points[i];
                double exRev = revCache.Evaluate(p);
                Assert.Equal(exRev > -0.25, revCache.EvaluateAbove(p, -0.25));
                Assert.Equal(Math.Abs(exRev) > 0.25, revCache.EvaluateAbsAbove(p, 0.25));
            }
        }


        /// <summary> a long band of n x 3 quads (6n triangles: more than its 2n + 6 boundary edges, so no direct-sum mode), the boundary one chain </summary>
        static DMesh3 LongStrip(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= 3; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i * 0.01, j / 3.0, 0.001 * Math.Sin(i + j)));
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        [Fact]
        public void BoundaryTree_ErrorBound_CoversSummationOrderAndPerEdgeErrorOfALongBoundary_AndIsSound()
        {
            // The summation-order clause 7.5e-16 * N * sum|w| of bErr is the largest one for a long boundary (N = 40006 here and the point far away:
            // 1.9e-7 against about 1e-10 for the nodes), so a bound without it is below its own formula: pinned from
            // below, and still above the true difference to the exact boundary sum at every point.
            DMesh3 strip = LongStrip(20000);
            ExactWindingCache cache = new DMeshAABBTree3(strip, true).GetExactWindingCache();
            cache.PrepareThresholdQueries();
            Assert.True(cache.HasBoundaryTree);
            // (the summation-order clause, and the evaluation error 1e-12 per edge that the accepted node replaces: the whole chain)
            double lowerBound = 1.01 * (7.5e-16 * cache.BoundaryCount * (double)cache.BoundaryCount + 1e-12 * cache.BoundaryCount) / MathUtil.TwoPI;
            var rnd = new Random(31);
            int used = 0;
            for (int i = 0; i < 200; ++i) {
                var p = new Vector3d(rnd.NextDouble() * 4e5 - 2e5, rnd.NextDouble() * 4e5 - 2e5, rnd.NextDouble() * 4e5 - 2e5);   // (far: one node of the chain is enough, its remainder is tiny)
                if (!cache.TryEvaluateTreeBoundarySumX(p, out double bSum, out double bErr)) continue;
                ++used;
                Assert.True(bErr >= lowerBound, $"bErr {bErr:R} below the summation-order clause {lowerBound:R} at {p}");
                double exact = cache.Evaluate(p);
                double diff = Math.Abs((exact - Math.Round(exact - bSum)) - bSum);
                Assert.True(diff <= bErr, $"bound violated at {p}: {diff:R} > {bErr:R}");
            }
            Assert.True(used > 100, $"only {used} queries took the tree");
        }

        [Fact]
        public void BoundaryTree_ExtremeScalesLeafOnlyChainsAndConcurrentInit_SafeAndExact()
        {
            // Extreme scales (> 1e65 and < 1e-65) skip BoundaryTree and still match Evaluate(p) > iso
            DMesh3 patch = OpenWavyPatch(160);
            foreach (int exp in new[] { 250, -250 }) {
                double sc = BitConverter.Int64BitsToDouble((long)(exp + 1023) << 52);
                DMesh3 scaled = new DMesh3(patch);
                MeshTransforms.Scale(scaled, sc);
                ExactWindingCache scCache = new DMeshAABBTree3(scaled, true).GetExactWindingCache();
                scCache.PrepareThresholdQueries();
                Assert.False(scCache.HasBoundaryTree);
                Vector3d qp = new Vector3d(0.2 * sc, -0.3 * sc, 0.4 * sc);
                Assert.Equal(scCache.Evaluate(qp) > 0.5, scCache.EvaluateAbove(qp, 0.5));
                Assert.Equal(Math.Abs(scCache.Evaluate(qp)) > 0.5, scCache.EvaluateAbsAbove(qp, 0.5));
            }

            // All-4-edge chains (140 1-quad holes in a 30x30 grid: 680 boundary edges across 141 chains -> 141*5 = 705 > 680) skip BoundaryTree
            DMesh3 gridWithQuadHoles = OpenWavyPatch(30);
            int removedQuads = 0;
            for (int iy = 1; iy < 29 && removedQuads < 140; iy += 2) {
                for (int ix = 1; ix < 29 && removedQuads < 140; ix += 2) {
                    int baseTid = 2 * (iy * 30 + ix);
                    gridWithQuadHoles.RemoveTriangle(baseTid, true, false);
                    gridWithQuadHoles.RemoveTriangle(baseTid + 1, true, false);
                    removedQuads++;
                }
            }
            ExactWindingCache holeCache = new DMeshAABBTree3(gridWithQuadHoles, true).GetExactWindingCache();
            Assert.False(holeCache.UseDirectSum);
            Assert.True(holeCache.BoundaryCount >= ExactWindingCache.BoundaryTreeMinEdges);
            holeCache.PrepareThresholdQueries();
            Assert.False(holeCache.HasBoundaryTree);

            // Concurrent first-call BoundaryTree initialization matches serial bit-for-bit
            var tree = new DMeshAABBTree3(patch, true);
            ExactWindingCache concCache = tree.GetExactWindingCache();
            var rnd = new Random(17);
            var pts = new Vector3d[128];
            for (int i = 0; i < pts.Length; ++i)
                pts[i] = new Vector3d(rnd.NextDouble() * 4.0 - 2.0, rnd.NextDouble() * 4.0 - 2.0, rnd.NextDouble() * 4.0 - 2.0);
            var expected = new bool[pts.Length];
            for (int i = 0; i < pts.Length; ++i)
                expected[i] = concCache.Evaluate(pts[i]) > 0.25;

            var actual = new bool[pts.Length];
            System.Threading.Tasks.Parallel.For(0, pts.Length, i => {
                actual[i] = concCache.EvaluateAbove(pts[i], 0.25);
            });
            Assert.True(concCache.HasBoundaryTree);
            for (int i = 0; i < pts.Length; ++i)
                Assert.Equal(expected[i], actual[i]);
        }
    }
}
