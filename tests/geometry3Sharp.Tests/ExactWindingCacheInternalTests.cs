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


        // independent oracle (Van Oosterom-Strakhov): half the solid angle of the spherical triangle (u, v, w = (-1,0,0)),
        // atan2(u.(v x w), |u||v||w| + (u.v)|w| + (u.w)|v| + (v.w)|u|); only for well-scaled generic inputs
        static double EdgeAngleOracle(Vector3d p, Vector3d a, Vector3d b)
        {
            Vector3d u = a - p, v = b - p;
            double lu = u.Length, lv = v.Length;
            double det = -(u.y * v.z - u.z * v.y);   // u . (v x w), w = (-1, 0, 0): w.x * (u.y v.z - u.z v.y) with the sign of the cross product
            double den = lu * lv + u.Dot(v) + (-u.x) * lv + (-v.x) * lu;
            return Math.Atan2(det, den);
        }

        static Vector3d Scaled(Vector3d v, double s) { return new Vector3d(v.x * s, v.y * s, v.z * s); }

        [Fact]
        public void BoundaryEdgeAngleX_MatchesAnIndependentOracle_AtEveryScale_AndIsScaleInvariant()
        {
            // generic well-scaled edges (coordinates are multiples of 1/8 in [-4, 4], so every 2^k scaling is exact);
            // the same edge at 2^k for k from -1000 to 1000 takes the fast path (2^+-60) and the rescaling path (the others)
            var rng = new Random(20261122);
            int[] ks = { 0, 60, -60, 150, -150, 400, -400, 1000, -1000 };
            int tested = 0;
            for (int i = 0; i < 400; ++i) {
                Vector3d P() { return new Vector3d(rng.Next(-32, 33) / 8.0, rng.Next(-32, 33) / 8.0, rng.Next(-32, 33) / 8.0); }
                Vector3d p = P(), a = P(), b = P();
                if (a.y == p.y && a.z == p.z || b.y == p.y && b.z == p.z || a.Equals(b)) continue;
                if (ExactPredicates.Orient2d(new Vector2d(p.y, p.z), new Vector2d(a.y, a.z), new Vector2d(b.y, b.z)) == 0) continue;   // (p behind the edge or on it: the limit paths, their own tests)   // (the special-case paths: their own tests)
                double expected = EdgeAngleOracle(p, a, b);
                if (Math.Abs(expected) > 3.1415 || double.IsNaN(expected)) continue;                // (near the branch cut the oracle is not exact)
                double baseAngle = ExactWindingCache.boundary_edge_angle_x(p, a, b);
                Assert.True(Math.Abs(baseAngle - expected) < 1e-12, $"p {p} a {a} b {b}: {baseAngle:R} vs oracle {expected:R}");
                foreach (int k in ks) {
                    double s = BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
                    double scaled = ExactWindingCache.boundary_edge_angle_x(Scaled(p, s), Scaled(a, s), Scaled(b, s));
                    Assert.True(Math.Abs(scaled - expected) < 1e-12, $"k {k}, p {p} a {a} b {b}: {scaled:R} vs oracle {expected:R}");
                    // swapping the edge ends negates the angle exactly
                    double swapped = ExactWindingCache.boundary_edge_angle_x(Scaled(p, s), Scaled(b, s), Scaled(a, s));
                    Assert.Equal(-scaled, swapped);
                    ++tested;
                }
            }
            Assert.True(tested > 1500, $"only {tested} cases");
        }


        [Fact]
        public void BoundaryEdgeAngleX_BranchCutWithRoundingNoise_TakesTheSignOfTheExactOrientation()
        {
            // p behind the middle of the edge in the yz plane: |angle| is close to pi and its sign is the exact sign of
            // (a - p) x (b - p) in yz. With decimal inputs (0.1, 0.3, ...) the rounded num is 0 or noise of the size 1e-17,
            // which the error bounds must route to the exact orientation (num == 0 always does; the rounded differences are
            // exact here, the next tests cover them).
            // Every 2^k scaling must give the same exact sign (k = +-60 take the fast path, the others the rescaling path).
            var rng = new Random(77);
            int[] ks = { 0, 60, -60, 400, -400 };
            int tested = 0, noisy = 0;
            for (int i = 0; i < 300; ++i) {
                double dy = rng.Next(1, 20) / 10.0, dz = rng.Next(1, 20) / 10.0, t = rng.Next(1, 20) / 10.0, u = rng.Next(1, 20) / 10.0;
                double w = 1.0 + rng.Next(0, 3) / 10.0;
                // the x coordinates differ, so that the denominator is not dominated by the yz part for every sample
                // (b.z is moved by up to 3 ulps, so that the exact orientation and the rounding noise disagree in some samples)
                double bz = -u * dz;
                bz = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(bz) + rng.Next(-3, 4));
                var a = new Vector3d(w, t * dy, t * dz);
                var b = new Vector3d(w + 0.1, -u * dy, bz);
                int oyz = ExactPredicates.Orient2d(new Vector2d(0, 0), new Vector2d(a.y, a.z), new Vector2d(b.y, b.z));
                if (oyz == 0) continue;
                if ((a.z * b.y - a.y * b.z > 0) != (oyz < 0)) ++noisy;
                foreach (int k in ks) {
                    double s = BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
                    double angle = ExactWindingCache.boundary_edge_angle_x(Vector3d.Zero, Scaled(a, s), Scaled(b, s));
                    Assert.True(Math.Abs(angle) > 3.0, $"k {k}, a {a}, b {b}: {angle:R}");
                    Assert.True(angle > 0 == oyz < 0, $"k {k}, a {a}, b {b}: angle {angle:R}, exact orientation {oyz}");
                    ++tested;
                }
            }
            Assert.True(tested > 200, $"only {tested} cases");
            Assert.True(noisy >= 5, $"only {noisy} samples where the rounded num is 0 or has the wrong sign");
        }


        [Fact]
        public void BoundaryEdgeAngleX_PointOnTheOpenEdgeWithRoundingNoise_IsDecidedExactly()
        {
            // p on the open segment (a, b) in the plane x = 0: den = da*db + dotYZ cancels exactly in exact arithmetic, and its
            // rounded value has either sign (the error bound denErr has to cover it). The exact limit is the half angle of
            // the edge seen from p on it, |angle| = pi/2 for a.z != b.z with a.x == b.x, never 0.
            var rng = new Random(5);
            int[] ks = { 0, 60, -60, 400, -400 };
            int tested = 0, noisy = 0;
            for (int i = 0; i < 300; ++i) {
                double y1 = rng.Next(1, 40) / 10.0, y2 = rng.Next(1, 40) / 10.0;
                var a = new Vector3d(0, y1, 2 * y1);
                var b = new Vector3d(0, -y2, -2 * y2);
                double la = Math.Sqrt(a.y * a.y + a.z * a.z), lb = Math.Sqrt(b.y * b.y + b.z * b.z);
                if (la * lb + (a.y * b.y + a.z * b.z) > 0) ++noisy;       // (the rounded den is > 0 though the exact one is 0)
                foreach (int k in ks) {
                    double s = BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
                    double angle = ExactWindingCache.boundary_edge_angle_x(Vector3d.Zero, Scaled(a, s), Scaled(b, s));
                    Assert.True(Math.Abs(Math.Abs(angle) - 0.5 * Math.PI) < 1e-12, $"k {k}, y1 {y1}, y2 {y2}: {angle:R}");
                    Assert.Equal(-angle, ExactWindingCache.boundary_edge_angle_x(Vector3d.Zero, Scaled(b, s), Scaled(a, s)));
                    ++tested;
                }
            }
            Assert.True(tested > 1000);
            Assert.True(noisy >= 20, $"only {noisy} samples with a positive rounded den");
        }


        [Fact]
        public void BoundaryEdgeAngleX_PointOnTheOpenEdge_RoundedDifferencesDoNotMoveTheAngle()
        {
            // p, a, b exactly collinear in 3D (z = 1.5 y, x = 0), p between a and b, but p is tiny against a and b, so
            // a - p and b - p round and their cross product num is not 0 in floating point (about half of the samples):
            // numErr must still send them to the exact predicates, whose answer is the open-edge limit |angle| = pi/2.
            var rng = new Random(11);
            int[] ks = { 0, 60, -60, 400, -400 };
            int tested = 0, noisy = 0;
            for (int i = 0; i < 300; ++i) {
                double ya = rng.Next(1, 1 << 20) * Math.Pow(2, -20), yb = -rng.Next(1, 1 << 20) * Math.Pow(2, -20);
                double c = rng.Next(1, 50) * Math.Pow(2, -55);
                var p = new Vector3d(0, c, 1.5 * c);
                var a = new Vector3d(0, ya, 1.5 * ya);
                var b = new Vector3d(0, yb, 1.5 * yb);
                if ((a.z - p.z) * (b.y - p.y) - (a.y - p.y) * (b.z - p.z) != 0) ++noisy;
                foreach (int k in ks) {
                    double s = BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
                    double angle = ExactWindingCache.boundary_edge_angle_x(Scaled(p, s), Scaled(a, s), Scaled(b, s));
                    Assert.True(Math.Abs(Math.Abs(angle) - 0.5 * Math.PI) < 1e-12, $"k {k}, p {p}, a {a}, b {b}: {angle:R}");
                    Assert.Equal(-angle, ExactWindingCache.boundary_edge_angle_x(Scaled(p, s), Scaled(b, s), Scaled(a, s)));
                    ++tested;
                }
            }
            Assert.True(tested > 1000);
            Assert.True(noisy >= 50, $"only {noisy} samples with a rounded cross product that is not 0");
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


        // ---- the clauses of the node bound, one test each (docs/REVIEW_CHECKLIST.md 10a) ----

        static ExactWindingCache OffsetGridCache(Vector3d offset, int n, out DMesh3 mesh)
        {
            mesh = ExactWindingAdoptionTests.LargeBoundaryOpenGrid(n);
            var rnd = new Random(3);
            foreach (int v in mesh.VertexIndices()) {
                // (jitter and offset: the rounded differences and cross products of the node build lose bits)
                Vector3d q = mesh.GetVertex(v) + offset + new Vector3d(rnd.NextDouble() * 1e-3, rnd.NextDouble() * 1e-3, rnd.NextDouble() * 1e-3);
                mesh.SetVertex(v, q);
            }
            ExactWindingCache cache = new DMeshAABBTree3(mesh, true).GetExactWindingCache();
            cache.PrepareThresholdQueries();
            Assert.True(cache.HasBoundaryTree);
            return cache;
        }

        [Fact]
        public void BoundaryTree_ConeTest_NodeInsideTheEpsWindowOfTheCapIsNotAccepted()
        {
            // The ray (+x from p) must lie outside the cap of the node's ball by the margin eps = 1e-9 + 16 coordEps: a point whose
            // direction to the node centre is inside that margin (but still outside the cap itself) must descend, and one beyond
            // the margin accepts. Without eps the first would be accepted on the edge of the cap, where the remainder bound is
            // not valid.
            ExactWindingCache cache = OffsetGridCache(Vector3d.Zero, 160, out _);
            ExactWindingCache.NodeInfo root = cache.DescribeBoundaryNode(0, new Vector3d(100, 100, 100));
            Assert.NotNull(root);
            Vector3d c = root.Center;
            double r = root.R;
            double d = 40 * r;
            double cosA = Math.Sqrt(1 - (r / d) * (r / d) * (1 + 1e-14));      // (the tree rounds d down by 1e-15 relative: close enough for the window)
            int inWindowDescended = 0, outsideAccepted = 0;
            foreach (double gap in new[] { 0.2e-9, 0.5e-9, 0.8e-9 }) {
                // minusX1U = dx / dn = cosA - gap: the +x ray misses the cap by the angle gap, less than eps
                double m = cosA - gap;
                Vector3d p = c - d * new Vector3d(m, Math.Sqrt(1 - m * m), 0);
                ExactWindingCache.NodeInfo info = cache.DescribeBoundaryNode(0, p);
                Assert.True(info.Query.MinusX1U > info.Query.CosA - info.Query.Eps && info.Query.MinusX1U < info.Query.CosA + 1e-12,
                    $"gap {gap}: not in the window: {info.Query.MinusX1U:R} vs cosA {info.Query.CosA:R} eps {info.Query.Eps:R}");
                Assert.Equal(0, info.Status);
                Assert.False(info.Query.Accepted);
                ++inWindowDescended;
            }
            foreach (double gap in new[] { 3e-9, 1e-6 }) {
                double m = cosA - gap;
                Vector3d p = c - d * new Vector3d(m, Math.Sqrt(1 - m * m), 0);
                ExactWindingCache.NodeInfo info = cache.DescribeBoundaryNode(0, p);
                Assert.Equal(1, info.Status);
                Assert.True(info.Query.Accepted);
                ++outsideAccepted;
            }
            Assert.Equal(3, inWindowDescended);
            Assert.Equal(2, outsideAccepted);
        }

        // exact arithmetic on doubles: every double is an integer multiple of 2^-1074
        static System.Numerics.BigInteger Scaled(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0;
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            var v = new System.Numerics.BigInteger(m) << (e - 1);          // x * 2^1074
            return neg ? -v : v;
        }

        [Fact]
        public void BoundaryTree_VaErrTerm_CoversTheTrueRoundingErrorOfTheDipoleMoment()
        {
            // va = sum (v - c) x (w - c) over the node's chain is computed in doubles. Its true value, in exact integers over the
            // grid 2^-1074, differs from it by rounding; the dipole term 0.5 va . (c - p) / d^3 is then off by at most
            // 0.5 |dva| . |dx| / d^3 <= vaErrTerm. Large coordinates and jitter make the rounding visible; without the clause
            // (vaErrTerm = 0) the true error stays above it.
            ExactWindingCache cache = OffsetGridCache(new Vector3d(1e5, -2e5, 3e4), 160, out _);
            var rnd = new Random(8);
            int checkedNodes = 0, visibleError = 0;
            for (int k = 0; k < 40; ++k) {
                ExactWindingCache.NodeInfo info = cache.DescribeBoundaryNode(k, Vector3d.Zero);
                if (info == null) break;
                Vector3d c = info.Center;
                Vector3d[] v = info.Vertices;
                System.Numerics.BigInteger cx = Scaled(c.x), cy = Scaled(c.y), cz = Scaled(c.z);
                System.Numerics.BigInteger sx = 0, sy = 0, sz = 0;
                for (int q = 0; q < v.Length; ++q) {
                    Vector3d w = (q < v.Length - 1) ? v[q + 1] : v[0];
                    System.Numerics.BigInteger ux = Scaled(v[q].x) - cx, uy = Scaled(v[q].y) - cy, uz = Scaled(v[q].z) - cz;
                    System.Numerics.BigInteger wx = Scaled(w.x) - cx, wy = Scaled(w.y) - cy, wz = Scaled(w.z) - cz;
                    sx += uy * wz - uz * wy;
                    sy += uz * wx - ux * wz;
                    sz += ux * wy - uy * wx;
                }
                // the stored half va, as exact integers over 2^-2148 (the cross products carry two factors 2^1074)
                System.Numerics.BigInteger Stored(double halfVa) => Scaled(halfVa) << 1074;
                // true half va = s / 2: dhv = 0.5 * s - stored
                double Err(System.Numerics.BigInteger s2, double stored)
                {
                    System.Numerics.BigInteger diff = (s2 >> 1) - Stored(stored);
                    return (double)(diff >> 1900) * Math.Pow(2, 1900 - 2148);
                }
                double ex = Err(sx, info.HalfVa.x), ey = Err(sy, info.HalfVa.y), ez = Err(sz, info.HalfVa.z);
                // (VaErr bounds the error of each component of the stored half va; the dipole is halfOmega = 0.5 * halfVa . dx / d^3)
                for (int t = 0; t < 12; ++t) {
                    Vector3d p = c + 8 * info.R * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                    ExactWindingCache.NodeInfo q = cache.DescribeBoundaryNode(k, p);
                    if (q.Status != 1) continue;
                    double dx = c.x - p.x, dy = c.y - p.y, dz = c.z - p.z;
                    double dd = dx * dx + dy * dy + dz * dz;
                    double invD3 = 1.0 / (dd * Math.Sqrt(dd));
                    // the dipole's error from the error of the stored half va (its exact error times dx)
                    double dipoleErr = 0.5 * Math.Abs(ex * dx + ey * dy + ez * dz) * invD3;
                    ++checkedNodes;
                    if (dipoleErr > 0) ++visibleError;
                    Assert.True(dipoleErr <= q.Query.VaErrTerm * (1 + 1e-9),
                        $"node {k}: the true error of the dipole from va's rounding, {dipoleErr:E3}, exceeds the clause {q.Query.VaErrTerm:E3}");
                    Assert.True(q.Query.VaErrTerm > 0);
                }
            }
            Assert.True(checkedNodes > 100, $"only {checkedNodes} nodes were accepted");
            Assert.True(visibleError > checkedNodes / 2, $"the rounding of va was visible in only {visibleError} of {checkedNodes} checks");
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
