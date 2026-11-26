using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ContinuousCollision (docs/design/CCD.md): constructed exact contacts and misses, the guarantees G1 (no false
    /// negatives), G2 (no contact before the time of impact) and G3 (a reported box is near) against exact dyadic
    /// evaluation, options and invalid input, and invariance over Similarity.All().
    /// </summary>
    public class ContinuousCollisionTests
    {
        static Vector3d V(double x, double y, double z) => new Vector3d(x, y, z);

        // a query: four points at t = 0 and t = 1; vertex-triangle (p, a, b, c) or edge-edge (a, b, c, d)
        internal sealed class Query
        {
            public bool VF;
            public Vector3d[] S = new Vector3d[4], E = new Vector3d[4];
            public Query Map(Func<Vector3d, Vector3d> f)
            {
                var q = new Query { VF = VF };
                for (int i = 0; i < 4; ++i) { q.S[i] = f(S[i]); q.E[i] = f(E[i]); }
                return q;
            }
        }

        internal static Query VFq(Vector3d p0, Vector3d a0, Vector3d b0, Vector3d c0, Vector3d p1, Vector3d a1, Vector3d b1, Vector3d c1)
            => new Query { VF = true, S = new[] { p0, a0, b0, c0 }, E = new[] { p1, a1, b1, c1 } };
        internal static Query EEq(Vector3d a0, Vector3d b0, Vector3d c0, Vector3d d0, Vector3d a1, Vector3d b1, Vector3d c1, Vector3d d1)
            => new Query { VF = false, S = new[] { a0, b0, c0, d0 }, E = new[] { a1, b1, c1, d1 } };

        internal static CcdResult Run(Query q, CcdOptions o = null, CcdSolver solver = null)
        {
            if (solver == null)
                return q.VF ? ContinuousCollision.VertexTriangle(q.S[0], q.S[1], q.S[2], q.S[3], q.E[0], q.E[1], q.E[2], q.E[3], o)
                            : ContinuousCollision.EdgeEdge(q.S[0], q.S[1], q.S[2], q.S[3], q.E[0], q.E[1], q.E[2], q.E[3], o);
            solver.Set(q.S[0], q.S[1], q.S[2], q.S[3], q.E[0], q.E[1], q.E[2], q.E[3]);
            return solver.Solve(q.VF, o ?? new CcdOptions());
        }

        // the exact F on one axis at the exact parameters (t, u, v)
        internal static ExactDyadic ExactF(Query q, int axis, double t, double u, double v)
        {
            ExactDyadic T = ExactDyadic.Of(t), U = ExactDyadic.Of(u), W = ExactDyadic.Of(v), one = ExactDyadic.Of(1);
            ExactDyadic X(int i) { var s = ExactDyadic.Of(q.S[i][axis]); return s + T * (ExactDyadic.Of(q.E[i][axis]) - s); }
            if (q.VF)
                return X(0) - ((one - U - W) * X(1) + U * X(2) + W * X(3));
            return (X(0) + U * (X(1) - X(0))) - (X(2) + W * (X(3) - X(2)));
        }

        // |F|_inf <= ms at (t, u, v), exactly
        internal static bool ExactContact(Query q, double t, double u, double v, double ms)
        {
            for (int axis = 0; axis < 3; ++axis) {
                ExactDyadic f = ExactF(q, axis, t, u, v);
                if (ExactDyadic.Compare(f, ExactDyadic.Of(ms)) > 0 || ExactDyadic.Compare(f, ExactDyadic.Of(-ms)) < 0)
                    return false;
            }
            return true;
        }

        // the unit triangle in z = 0, fixed
        static readonly Vector3d A = V(0, 0, 0), B = V(1, 0, 0), C = V(0, 1, 0);
        static Query vertexThrough(double x, double y) => VFq(V(x, y, 1), A, B, C, V(x, y, -1), A, B, C);

        static void AssertCollisionAt(CcdResult r, double tStar, double slack = 1e-5)
        {
            Assert.Equal(CcdStatus.Collision, r.Status);
            Assert.True(r.TimeOfImpact <= tStar, $"toi {r.TimeOfImpact:R} after the contact at {tStar:R}");
            Assert.True(r.TimeOfImpact >= tStar - slack, $"toi {r.TimeOfImpact:R} far before the contact at {tStar:R}");
            Assert.True(r.ToleranceReached <= 1e-6 * 4);
            Assert.True(r.NearContactTime >= r.TimeOfImpact && r.NearContactTime <= r.TimeOfImpact + 2e-6, $"near {r.NearContactTime:R}");
        }

        [Fact]
        public void VertexTriangle_ConstructedContacts()
        {
            AssertCollisionAt(Run(vertexThrough(0.25, 0.25)), 0.5);   // interior
            AssertCollisionAt(Run(vertexThrough(0.5, 0)), 0.5);       // through an edge
            AssertCollisionAt(Run(vertexThrough(0.5, 0.5)), 0.5);     // through the hypotenuse (u + v = 1)
            AssertCollisionAt(Run(vertexThrough(0, 0)), 0.5);         // through a vertex of the triangle
            AssertCollisionAt(Run(vertexThrough(1, 0)), 0.5);
            // contact at t = 0 and at t = 1
            AssertCollisionAt(Run(VFq(V(0.2, 0.2, 0), A, B, C, V(0.2, 0.2, 1), A, B, C)), 0);
            AssertCollisionAt(Run(VFq(V(0.2, 0.2, 1), A, B, C, V(0.2, 0.2, 0), A, B, C)), 1);
            // the triangle moves onto a fixed vertex
            AssertCollisionAt(Run(VFq(V(0.3, 0.3, 0), V(0, 0, 1), V(1, 0, 1), V(0, 1, 1), V(0.3, 0.3, 0), V(0, 0, -1), V(1, 0, -1), V(0, 1, -1))), 0.5);
            // no motion, in contact
            AssertCollisionAt(Run(VFq(V(0.3, 0.3, 0), A, B, C, V(0.3, 0.3, 0), A, B, C)), 0);
            // a degenerate (collinear) triangle and a vertex crossing its segment
            AssertCollisionAt(Run(VFq(V(0.5, 1, 0), A, B, V(0.5, 0, 0), V(0.5, -1, 0), A, B, V(0.5, 0, 0))), 0.5);
        }

        [Fact]
        public void VertexTriangle_Misses()
        {
            // outside the hypotenuse, by about 7e-4 (far beyond the false-positive distance of the defaults)
            Assert.Equal(CcdStatus.NoCollision, Run(vertexThrough(0.5 + 1.0 / 1024, 0.5)).Status);
            Assert.Equal(CcdStatus.NoCollision, Run(vertexThrough(-1.0 / 1024, 0.5)).Status);
            Assert.Equal(CcdStatus.NoCollision, Run(vertexThrough(2, 2)).Status);
            // passes above, parallel to the plane
            Assert.Equal(CcdStatus.NoCollision, Run(VFq(V(-1, 0.25, 0.01), A, B, C, V(2, 0.25, 0.01), A, B, C)).Status);
            // no motion, apart
            Assert.Equal(CcdStatus.NoCollision, Run(VFq(V(0.3, 0.3, 0.5), A, B, C, V(0.3, 0.3, 0.5), A, B, C)).Status);
            // stops just before the plane
            Assert.Equal(CcdStatus.NoCollision, Run(VFq(V(0.2, 0.2, 1), A, B, C, V(0.2, 0.2, 1.0 / 1024), A, B, C)).Status);
        }

        [Fact]
        public void EdgeEdge_ConstructedContactsAndMisses()
        {
            Vector3d c = V(0, -1, 0), d = V(0, 1, 0);
            // an edge along x moving down through a fixed edge along y
            AssertCollisionAt(Run(EEq(V(-1, 0, 1), V(1, 0, 1), c, d, V(-1, 0, -1), V(1, 0, -1), c, d)), 0.5);
            // touching at the end points (u = 1, v = 1)
            AssertCollisionAt(Run(EEq(V(-1, 1, 1), V(0, 1, 1), c, d, V(-1, 1, -1), V(0, 1, -1), c, d)), 0.5);
            // parallel edges, one moving onto the other (contact along a whole interval of u)
            AssertCollisionAt(Run(EEq(V(-1, 0, 1), V(1, 0, 1), V(-0.5, 0, 0), V(0.5, 0, 0), V(-1, 0, -1), V(1, 0, -1), V(-0.5, 0, 0), V(0.5, 0, 0))), 0.5);
            // collinear, sliding into each other
            AssertCollisionAt(Run(EEq(V(-3, 0, 0), V(-2, 0, 0), V(0, 0, 0), V(1, 0, 0), V(-1, 0, 0), V(0, 0, 0), V(0, 0, 0), V(1, 0, 0))), 1);
            // a zero-length edge (a point) through an edge
            AssertCollisionAt(Run(EEq(V(0, 0.5, 1), V(0, 0.5, 1), c, d, V(0, 0.5, -1), V(0, 0.5, -1), c, d)), 0.5);
            // misses: passes beside the end of the fixed edge, parallel and offset
            Assert.Equal(CcdStatus.NoCollision, Run(EEq(V(-1, 1 + 1.0 / 1024, 1), V(1, 1 + 1.0 / 1024, 1), c, d, V(-1, 1 + 1.0 / 1024, -1), V(1, 1 + 1.0 / 1024, -1), c, d)).Status);
            Assert.Equal(CcdStatus.NoCollision, Run(EEq(V(-1, 0.01, 1), V(1, 0.01, 1), V(-0.5, 0, 0), V(0.5, 0, 0), V(-1, 0.01, -1), V(1, 0.01, -1), V(-0.5, 0, 0), V(0.5, 0, 0))).Status);
        }

        [Fact]
        public void MinSeparation_And_TimeLimit()
        {
            // a vertex sliding across the triangle at height 1/64
            Query slide = VFq(V(-1, 0.25, 1.0 / 64), A, B, C, V(2, 0.25, 1.0 / 64), A, B, C);
            Assert.Equal(CcdStatus.NoCollision, Run(slide, new CcdOptions { MinSeparation = 1.0 / 128 }).Status);
            // it enters the box |F| <= 1/32 over x = -1/32 at t = (1 - 1/32) / 3
            AssertCollisionAt(Run(slide, new CcdOptions { MinSeparation = 1.0 / 32 }), (1 - 1.0 / 32) / 3);
            // approaching the plane: |F|_inf = 1 - 2t <= 1/8 from t = 7/16
            AssertCollisionAt(Run(vertexThrough(0.25, 0.25), new CcdOptions { MinSeparation = 0.125 }), 7.0 / 16);
            // the time limit: proven up to 0.4, the contact at 0.5 comes later
            Assert.Equal(CcdStatus.NoCollision, Run(vertexThrough(0.25, 0.25), new CcdOptions { TimeLimit = 0.4 }).Status);
            AssertCollisionAt(Run(vertexThrough(0.25, 0.25), new CcdOptions { TimeLimit = 0.6 }), 0.5);
            AssertCollisionAt(Run(vertexThrough(0.25, 0.25), new CcdOptions { TimeLimit = 0.5 }), 0.5);
        }

        [Fact]
        public void IterationLimit_IsConservative()
        {
            var r = Run(vertexThrough(0.25, 0.25), new CcdOptions { MaxIterations = 3 });
            Assert.Equal(CcdStatus.IterationLimit, r.Status);
            Assert.True(r.TimeOfImpact <= 0.5);
            Assert.Equal(3, r.Iterations);
            // a clear miss is decided within the limit
            Assert.Equal(CcdStatus.NoCollision, Run(vertexThrough(2, 2), new CcdOptions { MaxIterations = 3 }).Status);
        }

        // review #793: an iteration limit whose remaining boxes all start after TimeLimit has proven [0, TimeLimit]
        [Fact]
        public void IterationLimit_AfterTheTimeLimitIsNoCollision()
        {
            Query q = VFq(V(0.2, 0.2, 1), A, B, C, V(0.2, 0.2, -0.5), A, B, C);    // contact at t = 2/3
            for (int max = 1; max <= 60; ++max) {
                var r = Run(q, new CcdOptions { MaxIterations = max, TimeLimit = 0.4 });
                Assert.True(r.Status == CcdStatus.NoCollision || (r.Status == CcdStatus.IterationLimit && r.TimeOfImpact <= 0.4),
                    $"max {max}: {r.Status} at {r.TimeOfImpact:R}");
            }
            Assert.Equal(CcdStatus.NoCollision, Run(q, new CcdOptions { MaxIterations = 2, TimeLimit = 0.4 }).Status);
        }

        // review #793: a default result proves nothing; a constant image meets a tolerance of 0
        [Fact]
        public void DefaultResult_AndZeroTolerance()
        {
            Assert.Equal(CcdStatus.InvalidInput, default(CcdResult).Status);
            Vector3d p = V(0.5, 0.25, 0.125);
            var r = Run(EEq(p, p, p, p, p, p, p, p), new CcdOptions { Tolerance = 0 });
            Assert.Equal(CcdStatus.Collision, r.Status);
            Assert.Equal(0, r.TimeOfImpact);
            Assert.True(r.Iterations <= 2, $"{r.Iterations} iterations");
        }

        // bound review of #793, S2: coordinates whose differences or extent would overflow are InvalidInput
        [Fact]
        public void HugeCoordinates_BeyondTheRangeAreInvalid()
        {
            foreach (double scale in new[] { Math.Pow(2, 1010), Math.Pow(2, 1020), double.MaxValue / 2 }) {
                CcdResult r = Run(vertexThrough(0.25, 0.25).Map(x => x * scale));
                Assert.Equal(CcdStatus.InvalidInput, r.Status);
                Assert.True(double.IsPositiveInfinity(r.TimeOfImpact));
            }
            // 2^1000 itself is in range (the unit triangle and the path stay below it)
            Assert.Equal(CcdStatus.Collision, Run(vertexThrough(0.25, 0.25).Map(x => x * Math.Pow(2, 999))).Status);
            Assert.Equal(Math.Pow(2, 1000), CcdSolver.InputRange);
        }

        // bound review of #793, S3: NoCollision under a time limit below 1 proves [0, TimeLimit] only
        [Fact]
        public void TimeLimit_BoundsTheTimeOfImpact()
        {
            var r = Run(vertexThrough(0.25, 0.25), new CcdOptions { TimeLimit = 0.4 });
            Assert.Equal(CcdStatus.NoCollision, r.Status);
            Assert.Equal(0.4, r.TimeOfImpact);
            Assert.True(double.IsPositiveInfinity(Run(vertexThrough(2, 2)).TimeOfImpact));
        }

        // Nearly parallel, nearly coincident edges (the pattern of a cloth-funnel query of the ToI sample, built here
        // procedurally): the offset between the edges crosses zero in x at t = 0.4 and in z at t = 0.41, so the edges
        // miss by about 1.7e-7 (far beyond the false-positive distance); with both at t = 0.4 they coincide. Before the
        // separating direction across the strip and the split along it, the miss took 7.8e5 boxes.
        // two nearly parallel edges: the offset of edge (c, d) from edge (a, b) is x = s (t - 0.4), z = s (t - zCross),
        // across the edges; both drift together
        internal static Query ParallelEdges(double zCross)
        {
            const double s = 2.4e-5, len = 0.006;
            Vector3d dir = new Vector3d(0.33, 0.92, -0.11).Normalized * len;
            Vector3d drift = V(1e-4, 0, -3e-3);
            Vector3d a0 = V(0.0489, -0.023, -0.6051);
            Vector3d w(double t) => V(s * (t - 0.4), 0, s * (t - zCross));
            Vector3d a1 = a0 + drift;
            return EEq(a0, a0 + dir, a0 + w(0), a0 + dir + w(0), a1, a1 + dir, a1 + w(1), a1 + dir + w(1));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NearlyParallelEdges_DecideQuickly(bool intervals)
        {
            var solver = new CcdSolver { ForceIntervals = intervals };
            Query query(double zCross) => ParallelEdges(zCross);
            CcdResult Run(Query q, CcdOptions o = null) => ContinuousCollisionTests.Run(q, o, solver);
            CcdResult miss = Run(query(0.41));
            Assert.Equal(CcdStatus.NoCollision, miss.Status);
            Assert.True(miss.Iterations < 20000, $"{miss.Iterations} boxes");
            CcdResult touch = Run(query(0.4));
            Assert.Equal(CcdStatus.Collision, touch.Status);
            Assert.True(touch.TimeOfImpact <= 0.4 + 1e-12 && touch.TimeOfImpact >= 0.4 - 1e-3, $"{touch.TimeOfImpact:R}");
            Assert.True(touch.Iterations < 20000, $"{touch.Iterations} boxes");
            // a minimum separation above the miss distance: the contact set is a thin slab along the edges, where the
            // split along the separating direction stops once the spread is within the separation band
            CcdResult slab = Run(query(0.41), new CcdOptions { MinSeparation = 1e-6 });
            Assert.Equal(CcdStatus.Collision, slab.Status);
            Assert.True(slab.Iterations < 20000, $"{slab.Iterations} boxes");
            // the miss in exact arithmetic: no sampled point of the domain is within the false-positive distance
            Query q = query(0.41);
            for (int i = 0; i <= 200; ++i)
                for (int j = 0; j <= 8; ++j) {
                    double t = 0.35 + i * (0.1 / 200), u = j / 8.0;
                    Assert.False(ExactContact(q, Math.Round(t * 4096) / 4096, u, u, 1e-8));
                }
        }

        // review of #845: the decisions of the parallel-edge path are the same at every 2^k scale (the vectors are
        // normalized by powers of two), also where the raw products would over- or underflow; above 2^500 (the
        // interval path) the parallel pattern stays fast
        [Fact]
        public void ParallelEdgePath_ExtremeScales()
        {
            Query crossing = EEq(V(-1, 0, 1), V(1, 0.3, 1), V(0, -1, 0), V(0, 1, 0), V(-1, 0, -1), V(1, 0.3, -1), V(0, -1, 0), V(0, 1, 0));
            foreach (Query q in new[] { crossing, ParallelEdges(0.41), ParallelEdges(0.4) })
                foreach (double ms in new[] { 0.0, 1e-3 }) {
                    CcdResult r0 = Run(q, new CcdOptions { MinSeparation = ms });
                    foreach (int k in new[] { 10, 200, 262, 300, 400, -10, -270, -280 }) {
                        double scale = Math.Pow(2, k);
                        CcdResult r = Run(q.Map(x => x * scale), new CcdOptions { MinSeparation = ms * scale });
                        Assert.Equal(r0.Status, r.Status);
                        Assert.Equal(r0.Iterations, r.Iterations);
                        Assert.Equal(r0.TimeOfImpact, r.TimeOfImpact);
                    }
                }
            foreach (int k in new[] { 520, 900 }) {
                // (at 2^900 the products of the direction with the corner values would overflow without the scaling)
                CcdResult big = Run(ParallelEdges(0.41).Map(x => x * Math.Pow(2, k)));
                Assert.Equal(CcdStatus.NoCollision, big.Status);
                Assert.True(big.Iterations < 20000, $"2^{k}: {big.Iterations} boxes");
            }
        }

        // review of #845: no cliff at the parallel threshold (a sine of 0.01): edges just past it are split along their
        // cross product, so they need about as few boxes as those below it (6840 boxes at a sine of 0.011 without)
        [Theory]
        [InlineData(0.005)]
        [InlineData(0.011)]
        [InlineData(0.02)]
        public void ParallelEdgePath_NoCliffAtTheThreshold(double sine)
        {
            const double s = 2.4e-5, len = 0.006;
            Vector3d dir = new Vector3d(0.33, 0.92, -0.11).Normalized * len;
            Vector3d perp = dir.Cross(V(1, 0, 0)).Normalized * len;
            Vector3d dirb = dir * Math.Sqrt(1 - sine * sine) + perp * sine, mid = 0.5 * dir;
            Vector3d drift = V(1e-4, 0, -3e-3), a0 = V(0.0489, -0.023, -0.6051), a1 = a0 + drift;
            Vector3d w(double t) => V(s * (t - 0.4), 0, s * (t - 0.41));
            var q = EEq(a0, a0 + dir, a0 + mid - 0.5 * dirb + w(0), a0 + mid + 0.5 * dirb + w(0),
                        a1, a1 + dir, a1 + mid - 0.5 * dirb + w(1), a1 + mid + 0.5 * dirb + w(1));
            CcdResult r = Run(q);
            Assert.Equal(CcdStatus.Collision, r.Status);
            Assert.True(r.Iterations < 3000, $"sine {sine}: {r.Iterations} boxes");
        }

        // the parallel-edge path is for edge-edge only: vertex-triangle queries whose vertex path and triangle side are
        // parallel give the same results with the path switched off
        [Fact]
        public void ParallelEdgePath_LeavesVertexTriangleAlone()
        {
            var rnd = new Random(13);
            var on = new CcdSolver();
            var off = new CcdSolver { ParallelEdgeDirections = false };
            for (int i = 0; i < 200; ++i) {
                Vector3d a = point(rnd), b = point(rnd), c = point(rnd);
                // p - a parallel to c - b (the pairs (p, a) and (b, c) the edge-edge code would read as edges)
                Vector3d p0 = a + 0.5 * (c - b) + V(0, 0, 1.0 / 64), p1 = p0 + V(0, 0, -1.0 / 32);
                var q = VFq(p0, a, b, c, p1 + (c - b) * 0.25, a, b, c + (c - b) * 0.25);
                foreach (double ms in new[] { 0.0, 1.0 / 128 }) {
                    var o = new CcdOptions { MinSeparation = ms };
                    Assert.Equal(Run(q, o, off), Run(q, o, on));
                }
            }
        }

        [Fact]
        public void InvalidInput()
        {
            Assert.Equal(CcdStatus.InvalidInput, Run(vertexThrough(double.NaN, 0)).Status);
            Assert.Equal(CcdStatus.InvalidInput, Run(vertexThrough(double.PositiveInfinity, 0)).Status);
            foreach (var o in new[] { new CcdOptions { MinSeparation = -1 }, new CcdOptions { MinSeparation = double.NaN },
                                      new CcdOptions { Tolerance = -1 }, new CcdOptions { RelativeTolerance = double.NaN },
                                      new CcdOptions { TimeLimit = 1.5 }, new CcdOptions { TimeLimit = -0.1 } }) {
                var r = Run(vertexThrough(0.25, 0.25), o);
                Assert.Equal(CcdStatus.InvalidInput, r.Status);
                Assert.True(double.IsPositiveInfinity(r.TimeOfImpact));
            }
        }

        // a random coordinate: a multiple of 2^-10 in [-1, 1] (few bits, so constructed contacts are exact in doubles)
        static double coord(Random rnd) => rnd.Next(-1024, 1025) / 1024.0;
        static Vector3d point(Random rnd) => V(coord(rnd), coord(rnd), coord(rnd));

        // queries with an exact contact at (t*, u*, v*): t* in {0, 1/4, 1/2, 1}, u*, v* multiples of 1/8 (vertex-triangle)
        // or u* in {0, 1/2, 3/4}, v* a multiple of 1/8 (edge-edge), so that solving for the last point is exact
        internal static Query ConstructedContact(Random rnd, bool vf, out double tStar)
        {
            double[] ts = { 0, 0.25, 0.5, 1 };
            tStar = ts[rnd.Next(ts.Length)];
            double t = tStar;
            var q = new Query { VF = vf };
            for (int i = 0; i < 4; ++i) { q.S[i] = point(rnd); q.E[i] = q.S[i] + 0.25 * point(rnd); }
            Vector3d at(int i) => q.S[i] + t * (q.E[i] - q.S[i]);
            Vector3d target;
            int free;
            if (vf) {
                double u = rnd.Next(0, 9) / 8.0, v = rnd.Next(0, 9 - (int)(8 * u)) / 8.0;
                target = (1 - u - v) * at(1) + u * at(2) + v * at(3);
                free = 0;
            } else {
                double[] us = { 0, 0.5, 0.75 };
                double u = us[rnd.Next(us.Length)], v = rnd.Next(0, 9) / 8.0;
                Vector3d x = at(2) + v * (at(3) - at(2));
                target = (x - u * at(1)) / (1 - u);         // a(t*) so that a(t*) + u (b(t*) - a(t*)) = x
                free = 0;
            }
            // move the free point's end (t* > 0) or start (t* = 0) so that it is at target at t*
            if (t == 0) q.S[free] = target;
            else if (t == 1) q.E[free] = target;
            else if (t == 0.5) q.E[free] = 2 * target - q.S[free];
            else q.E[free] = 4 * target - 3 * q.S[free];
            return q;
        }

        // random query for the pre-filter tests: constructed contacts, near misses and far pairs, at several scales and shifts
        static Query prefilterQuery(Random rnd, int i)
        {
            bool vf = (i & 1) == 0;
            Query q;
            switch (i % 4) {
                case 0: q = ConstructedContact(rnd, vf, out _); break;
                case 1: {
                    // a near miss: a constructed contact moved off by a small offset
                    q = ConstructedContact(rnd, vf, out _);
                    Vector3d off = (1.0 / (1 << (3 + rnd.Next(8)))) * point(rnd);
                    for (int k = 1; k < 4; ++k) { q.S[k] += off; q.E[k] += off; }
                    break;
                }
                default: {
                    q = new Query { VF = vf };
                    double spread = rnd.Next(2) == 0 ? 1 : 0.125;
                    for (int k = 0; k < 4; ++k) { q.S[k] = spread * point(rnd); q.E[k] = q.S[k] + 0.5 * point(rnd); }
                    break;
                }
            }
            double scale = Math.Pow(2, rnd.Next(-6, 7));
            Vector3d shift = (i % 5 == 0) ? V(632117.25, 5712043.5, 113.75) : Vector3d.Zero;
            return q.Map(x => scale * x + shift);
        }

        static void sameResult(CcdResult x, CcdResult y, string at)
        {
            Assert.True(x.Status == y.Status && x.TimeOfImpact.Equals(y.TimeOfImpact) && x.NearContactTime.Equals(y.NearContactTime)
                        && x.ToleranceReached.Equals(y.ToleranceReached) && x.ErrorBound.Equals(y.ErrorBound) && x.Iterations == y.Iterations,
                        $"{at}: {x.Status} {x.TimeOfImpact:R} {x.NearContactTime:R} {x.ToleranceReached:R} {x.ErrorBound:R} {x.Iterations} vs {y.Status} {y.TimeOfImpact:R} {y.NearContactTime:R} {y.ToleranceReached:R} {y.ErrorBound:R} {y.Iterations}");
        }

        // #940: the shortcut of the first box (Solve discards it on an axis without the queue) gives every field of the
        // result that the search through the queue gives, and RootDiscards says exactly when the shortcut applies
        [Theory]
        [InlineData(0)]
        [InlineData(1e-3)]
        [InlineData(0.25)]
        public void RootShortcut_EqualsTheQueue(double ms)
        {
            var rnd = new Random(940);
            var fast = new CcdSolver();
            var slow = new CcdSolver { RootShortcut = false };
            var probe = new CcdSolver();
            int discarded = 0, kept = 0;
            for (int i = 0; i < 4000; ++i) {
                Query q = prefilterQuery(rnd, i);
                var o = new CcdOptions { MinSeparation = ms, TimeLimit = i % 7 == 0 ? 0.5 : 1 };
                CcdResult a = Run(q, o, fast), b = Run(q, o, slow);
                sameResult(a, b, $"query {i}");
                probe.Set(q.S[0], q.S[1], q.S[2], q.S[3], q.E[0], q.E[1], q.E[2], q.E[3]);
                if (probe.RootDiscards(q.VF, o)) {
                    ++discarded;
                    Assert.Equal(CcdStatus.NoCollision, b.Status);
                    Assert.Equal(1, b.Iterations);
                } else
                    ++kept;
            }
            Assert.True(discarded > 200 && kept > 400, $"{discarded} discarded, {kept} kept");
            // invalid input and range stay with the general path
            probe.Set(V(double.NaN, 0, 0), V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), V(0, 0, 0), V(1, 0, 0), V(0, 1, 0));
            Assert.False(probe.RootDiscards(true, new CcdOptions()));
            probe.Set(V(5, 5, 5), V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(5, 5, 5), V(0, 0, 0), V(1, 0, 0), V(0, 1, 0));
            Assert.False(probe.RootDiscards(true, new CcdOptions { MinSeparation = -1 }));
            Assert.True(probe.RootDiscards(true, new CcdOptions()));
            // a point that is NaN, infinite or beyond the solver's range on one axis, while another axis separates the
            // primitives: InvalidInput, not a discarded box
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 2 * CcdSolver.InputRange }) {
                probe.Set(V(bad, 0, 5), V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(bad, 0, 5), V(0, 0, 0), V(1, 0, 0), V(0, 1, 0));
                Assert.False(probe.RootDiscards(true, new CcdOptions()), $"{bad}");
                Assert.Equal(CcdStatus.InvalidInput, probe.Solve(true, new CcdOptions()).Status);
            }
        }

        // #940: AbandonAfter. A query that is not abandoned has the result it has without the bound, field for field; an
        // abandoned one has a time of impact above the bound without it (so it cannot be the earliest contact of a sweep
        // whose earliest near contact is at most the bound), and a bound at or above a query's time of impact never abandons it
        [Theory]
        [InlineData(0)]
        [InlineData(1e-3)]
        public void AbandonAfter_NeverChangesARelevantResult(double ms)
        {
            var rnd = new Random(941);
            var plain = new CcdSolver();
            var bounded = new CcdSolver();
            int abandoned = 0, same = 0;
            for (int i = 0; i < 1800; ++i) {
                Query q = prefilterQuery(rnd, i);
                var o = new CcdOptions { MinSeparation = ms, MaxIterations = i % 11 == 0 ? 40 : 1000000, TimeLimit = i % 3 == 0 ? 0.5 : 1, TimeTolerance = i % 4 == 0 ? 0.05 : 1e-6, Tolerance = i % 5 == 0 ? 1e-3 : double.NaN };
                CcdResult free = Run(q, o, plain);
                double[] bounds = { 0, 1e-3, 0.0625, 0.3, 0.5, 0.99, 1, free.TimeOfImpact, free.NearContactTime };
                foreach (double bound in bounds) {
                    bounded.AbandonAfter = bound;
                    CcdResult r = Run(q, o, bounded);
                    if (bounded.Abandoned) {
                        ++abandoned;
                        Assert.True(free.TimeOfImpact > bound, $"query {i} bound {bound:R}: abandoned, but the time of impact is {free.TimeOfImpact:R} ({free.Status})");
                        Assert.Equal(CcdStatus.NoCollision, r.Status);
                    } else {
                        ++same;
                        sameResult(r, free, $"query {i} bound {bound:R}");
                    }
                }
            }
            Assert.True(abandoned > 900 && same > 900, $"{abandoned} abandoned, {same} the same");
            // the flag is per solve
            bounded.AbandonAfter = double.PositiveInfinity;
            Run(prefilterQuery(rnd, 3), new CcdOptions(), bounded);
            Assert.False(bounded.Abandoned);
        }

        // G1 / G2 on exact contacts: never NoCollision, and the time of impact not after the contact
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ExactContacts_AreNeverMissed(bool intervals, bool shifted)
        {
            var rnd = new Random(7);
            var solver = new CcdSolver { ForceIntervals = intervals };
            // a UTM-like shift (exact: the constructions have few bits), where the filter's error bound matters
            Vector3d shift = shifted ? V(632117.25, 5712043.5, 113.75) : Vector3d.Zero;
            int n = 0;
            for (int i = 0; i < 600; ++i) {
                bool vf = (i & 1) == 0;
                Query q = ConstructedContact(rnd, vf, out double tStar).Map(x => x + shift);
                // the construction is exact (its contact holds in exact arithmetic), else skip it
                if (contact_point_exists(q, tStar) == false)
                    continue;
                ++n;
                double ms = (i % 3 == 0) ? 1.0 / 256 : 0;
                CcdResult r = Run(q, new CcdOptions { MinSeparation = ms }, solver);
                Assert.True(r.Status == CcdStatus.Collision, $"query {i}: {r.Status}");
                Assert.True(r.TimeOfImpact <= tStar, $"query {i}: toi {r.TimeOfImpact:R} > {tStar:R}");
            }
            Assert.True(n > 500, $"only {n} exact constructions");
        }

        // recompute the construction's contact exactly over the dyadic grid it was built on
        static bool contact_point_exists(Query q, double t)
        {
            if (q.VF) {
                for (int iu = 0; iu <= 8; ++iu)
                    for (int iv = 0; iu + iv <= 8; ++iv)
                        if (ExactContact(q, t, iu / 8.0, iv / 8.0, 0)) return true;
                return false;
            }
            foreach (double u in new[] { 0, 0.5, 0.75 })
                for (int iv = 0; iv <= 8; ++iv)
                    if (ExactContact(q, t, u, iv / 8.0, 0)) return true;
            return false;
        }

        // random queries near contact: G1 / G2 against exact samples, G3 at the reported box
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RandomQueries_SampledContactsAndReportedBoxes(bool intervals)
        {
            var rnd = new Random(11);
            var solver = new CcdSolver { ForceIntervals = intervals };
            int collisions = 0, sampled = 0;
            for (int i = 0; i < 300; ++i) {
                bool vf = (i & 1) == 0;
                Query q = ConstructedContact(rnd, vf, out _);
                // perturb by up to 2^-12 (some still collide, some miss narrowly)
                int which = rnd.Next(4);
                q.E[which] = q.E[which] + V(coord(rnd), coord(rnd), coord(rnd)) / 4096;
                double ms = (i % 4 == 0) ? 1.0 / 512 : 0;
                var o = new CcdOptions { MinSeparation = ms };
                CcdResult r = Run(q, o, solver);
                Assert.NotEqual(CcdStatus.InvalidInput, r.Status);
                // G1 / G2: an exact contact at a sampled dyadic point (t, u, v) is not missed, and not before toi
                for (int k = 0; k < 40; ++k) {
                    double t = rnd.Next(0, 65) / 64.0, u = rnd.Next(0, 17) / 16.0, v = rnd.Next(0, 17) / 16.0;
                    if (vf && u + v > 1) continue;
                    if (ExactContact(q, t, u, v, ms)) {
                        ++sampled;
                        Assert.NotEqual(CcdStatus.NoCollision, r.Status);
                        Assert.True(r.TimeOfImpact <= t);
                    }
                }
                if (r.Status == CcdStatus.Collision) {
                    ++collisions;
                    AssertNearBox(q, solver.LastBox, ms + 2 * r.ErrorBound + r.ToleranceReached);
                }
            }
            Assert.True(collisions > 50, $"{collisions} collisions");
        }

        // G3: at the vertices of the reported box (clipped to the triangle u + v <= 1 for vertex-triangle) every
        // coordinate of the exact F is within bound
        internal static void AssertNearBox(Query q, double[] box, double bound)
        {
            var points = new List<(double t, double u, double v)>();
            double u0 = box[2], u1 = box[3], v0 = box[4], v1 = box[5];
            foreach (double t in new[] { box[0], box[1] }) {
                foreach (var (u, v) in new[] { (u0, v0), (u1, v0), (u0, v1), (u1, v1) })
                    if (q.VF == false || u + v <= 1) points.Add((t, u, v));
                if (q.VF && u1 + v1 > 1) {
                    // where u + v = 1 crosses the box boundary
                    points.Add(u1 + v0 <= 1 ? (t, u1, 1 - u1) : (t, 1 - v0, v0));
                    points.Add(u0 + v1 <= 1 ? (t, 1 - v1, v1) : (t, u0, 1 - u0));
                }
            }
            for (int k = 0; k < points.Count; ++k) {
                var (t, u, v) = points[k];
                for (int axis = 0; axis < 3; ++axis) {
                    ExactDyadic f = ExactF(q, axis, t, u, v);
                    // compare with the bound rounded up by a relative 2^-50 (the bound is a computed double)
                    double b = bound * (1 + 1.0 / (1L << 50));
                    Assert.True(ExactDyadic.Compare(f, ExactDyadic.Of(b)) <= 0 && ExactDyadic.Compare(f, ExactDyadic.Of(-b)) >= 0,
                        $"point {k} axis {axis}: |F| > {b:R}");
                }
            }
        }

        // B1: the computed corner values on the filter path are within e of the exact values, on adversarial inputs
        // (cancellation at large offsets, mixed magnitudes, subnormal motions, parameters with many bits)
        [Fact]
        public void Filter_BoundsTheEvaluationError()
        {
            var rnd = new Random(5);
            var solver = new CcdSolver();
            var errors = new double[3];
            double worstRatio = 0, worstSubnormal = 0;
            for (int i = 0; i < 3000; ++i) {
                int kind = i % 4;
                double scale = Math.Pow(2, rnd.Next(-60, 400));
                Vector3d basePoint = kind == 1 ? V(632117.25, 5712043.5, 113.75) * scale : Vector3d.Zero;
                Vector3d rp(double size) => basePoint + size * V(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                var q = new Query { VF = (i & 1) == 0 };
                for (int k = 0; k < 4; ++k) {
                    switch (kind) {
                        case 0: q.S[k] = rp(scale); q.E[k] = q.S[k] + rp(scale) - basePoint; break;               // generic
                        case 1: q.S[k] = rp(scale * 1e-9); q.E[k] = rp(scale * 1e-9); break;                       // cancellation
                        case 2: q.S[k] = rp(k == 0 ? scale : scale * 1e-12); q.E[k] = rp(k == 3 ? scale : 1e-6 * scale); break;  // mixed
                        default:                                                                                     // subnormal
                            q.S[k] = V(rnd.Next(-1 << 20, 1 << 20), rnd.Next(-1 << 20, 1 << 20), rnd.Next(-1 << 20, 1 << 20)) * double.Epsilon;
                            q.E[k] = q.S[k] + V(rnd.Next(-1 << 20, 1 << 20), rnd.Next(-1 << 20, 1 << 20), rnd.Next(-1 << 20, 1 << 20)) * double.Epsilon;
                            break;
                    }
                }
                solver.Set(q.S[0], q.S[1], q.S[2], q.S[3], q.E[0], q.E[1], q.E[2], q.E[3]);
                // parameters: multiples of 2^-53 (as deep in the search as a box can be), or of 2^-k
                double par() { int bits = rnd.Next(1, 54); return Math.Floor(rnd.NextDouble() * (1L << bits)) / (1L << bits); }
                double t0 = par(), t1 = par(), u0 = par(), u1 = par(), v0 = par(), v1 = par();
                double[] c = solver.FilterCornersForTests(q.VF, t0, t1, u0, u1, v0, v1, errors);
                for (int k = 0; k < 8; ++k) {
                    double t = (k & 1) == 0 ? t0 : t1, u = (k & 2) == 0 ? u0 : u1, v = (k & 4) == 0 ? v0 : v1;
                    for (int axis = 0; axis < 3; ++axis) {
                        ExactDyadic err = ExactDyadic.Of(c[8 * axis + k]) - ExactF(q, axis, t, u, v);
                        Assert.True(ExactDyadic.Compare(err, ExactDyadic.Of(errors[axis])) <= 0
                                 && ExactDyadic.Compare(err, ExactDyadic.Of(-errors[axis])) >= 0, $"query {i} corner {k} axis {axis}");
                        double ratio = Math.Abs(ExactDyadic.Quotient(err, ExactDyadic.Of(errors[axis])));
                        if (kind == 3) worstSubnormal = Math.Max(worstSubnormal, ratio);
                        else worstRatio = Math.Max(worstRatio, ratio);
                    }
                }
            }
            // the bound is not absurdly loose (a constant 16 times smaller fails on these inputs, see the mutants in the PR)
            Assert.True(worstRatio > 1.0 / 16, $"worst observed error / bound {worstRatio}");
            Assert.True(worstSubnormal > 1.0 / 16, $"worst observed subnormal error / bound {worstSubnormal}");
        }

        // the filter's bound is used in the discard test: a vertex that reaches a moving triangle's corner exactly at
        // t = 1 (and only there), where the rounding of start + 1 * (end - start) can put the computed value at the
        // contact on the far side of zero
        [Fact]
        public void Filter_ContactOnlyAtACornerOfTheDomain()
        {
            var rnd = new Random(3);
            int rounded = 0;
            for (int i = 0; i < 400; ++i) {
                Vector3d a0 = 1000 * V(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                Vector3d a1 = a0 + V(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.37;
                // b and c keep a's z, so F_z = (1 - t)(p0 - a0)_z >= 0, zero only at t = 1
                Vector3d ex = V(1, 0, 0), ey = V(0, 1, 0);
                Vector3d p0 = a0 + V(0.1, 0.1, 3000.3);   // far apart in magnitude: end - start is rounded
                var q = VFq(p0, a0, a0 + ex, a0 + ey, a1, a1, a1 + ex, a1 + ey);
                Assert.True(ExactContact(q, 1, 0, 0, 0));
                double z1 = p0.z + (a1.z - p0.z), za = a0.z + (a1.z - a0.z);
                if (z1 - za > 0) ++rounded;
                CcdResult r = Run(q);
                Assert.Equal(CcdStatus.Collision, r.Status);
                Assert.True(r.TimeOfImpact <= 1);
            }
            Assert.True(rounded > 20, $"only {rounded} queries round the contact away");
        }

        // inputs beyond the filter's range take the interval path, and stay correct
        [Fact]
        public void HugeCoordinates_TakeTheIntervalPath()
        {
            foreach (double scale in new[] { Math.Pow(2, 600), 1e300 }) {
                Query q = vertexThrough(0.25, 0.25).Map(x => x * scale);
                CcdResult r = Run(q);
                Assert.Equal(CcdStatus.Collision, r.Status);
                Assert.Equal(0, r.ErrorBound);
                Assert.True(r.TimeOfImpact <= 0.5);
                Assert.Equal(CcdStatus.NoCollision, Run(vertexThrough(2, 2).Map(x => x * scale)).Status);
            }
            Assert.True(Run(vertexThrough(0.25, 0.25)).ErrorBound > 0);
            Assert.Equal(Math.Pow(2, 500), CcdSolver.FilterRange);
        }

        [Fact]
        public void Reuse_SameSolverSameResult()
        {
            var solver = new CcdSolver();
            var q1 = vertexThrough(0.25, 0.25);
            var q2 = EEq(V(-1, 0, 1), V(1, 0, 1), V(0, -1, 0), V(0, 1, 0), V(-1, 0, -1), V(1, 0, -1), V(0, -1, 0), V(0, 1, 0));
            CcdResult a = Run(q1, null, solver), b = Run(q2, null, solver), c = Run(q1, null, solver), d = Run(q2, null, solver);
            Assert.Equal(a, c);
            Assert.Equal(b, d);
            Assert.Equal(a, Run(q1));
        }

        // a few queries with clear margins (deep crossings, clear misses) for the invariance test, and the nearly
        // parallel edges: tStar the contact time (NaN: unknown), clearance the distance of a miss (0: none or large),
        // rate how fast F changes with t (input units; the times of a frame agree within its resolution / rate)
        static IEnumerable<(Query q, CcdStatus status, double tStar, double clearance, double rate)> invarianceSet()
        {
            yield return (vertexThrough(0.25, 0.25), CcdStatus.Collision, 0.5, 0, 2);
            yield return (vertexThrough(0.75, 0.75), CcdStatus.NoCollision, 0, 0, 2);
            yield return (VFq(V(0.3, 0.2, 1), V(0, 0, 0.2), V(1, 0, -0.1), V(0, 1, 0), V(0.1, 0.4, -1), V(0, 0.1, -0.2), V(1, 0.1, 0.1), V(0.1, 1, 0)), CcdStatus.Collision, double.NaN, 0, 2);
            Vector3d c = V(0, -1, 0), d = V(0, 1, 0);
            yield return (EEq(V(-1, 0, 1), V(1, 0, 1), c, d, V(-1, 0, -1), V(1, 0, -1), c, d), CcdStatus.Collision, 0.5, 0, 2);
            yield return (EEq(V(-1, 2, 1), V(1, 2, 1), c, d, V(-1, 2, -1), V(1, 2, -1), c, d), CcdStatus.NoCollision, 0, 0, 2);
            // (a miss by about 1.7e-7, below the resolution of the rotated millimetre UTM frame, where either verdict is
            // allowed; the touch is a contact in every frame)
            yield return (ParallelEdges(0.41), CcdStatus.NoCollision, 0, 1.7e-7, 2.4e-5);
            yield return (ParallelEdges(0.4), CcdStatus.Collision, double.NaN, 0, 2.4e-5);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (var (q, status, tStar, clearance, rate) in invarianceSet()) {
                CcdResult r0 = Run(q);
                Assert.Equal(status, r0.Status);
                CcdResult r = Run(q.Map(s.Apply));
                // the frame's resolution (input units) and the evaluation error (64 rounding units of the largest
                // coordinate) bound what a frame can tell apart
                double resolution = 1024 * s.Resolution(1);
                if (clearance > 0 && s.IsExact == false && clearance < resolution) {
                    Assert.NotEqual(CcdStatus.InvalidInput, r.Status);
                    continue;
                }
                Assert.Equal(status, r.Status);
                if (s.IsExact) {
                    // the default tolerance is relative: everything scales exactly
                    Assert.Equal(r0.TimeOfImpact, r.TimeOfImpact);
                    Assert.Equal(r0.Iterations, r.Iterations);
                    Assert.Equal(s.ApplyLength(r0.ToleranceReached), r.ToleranceReached);
                } else if (status == CcdStatus.Collision) {
                    // G2 holds in each frame; the times agree within the boxes' time widths and the frame's resolution
                    double slack = 1e-4 + resolution / rate;
                    Assert.True(Math.Abs(r.TimeOfImpact - r0.TimeOfImpact) < slack, $"{name}: {r.TimeOfImpact:R} vs {r0.TimeOfImpact:R}");
                    if (double.IsNaN(tStar) == false) Assert.True(r.TimeOfImpact <= tStar + 1e-9);
                }
            }
        }

        [Fact]
        public void SeparatedAlong_CCD1_SoundAcrossScalesGuardsAndExactOracle()
        {
            ExactDyadic D(double x) => ExactDyadic.Of(x);
            var solver = new CcdSolver();

            // 1) Non-finite / zero normals never separate and leave [alphaLo, alphaHi] = [0, 1]
            foreach (Vector3d badN in new[] { V(0, 0, 0), V(double.NaN, 1, 0), V(double.PositiveInfinity, 1, 0), V(1, double.NegativeInfinity, 0) }) {
                Assert.False(solver.SeparatedAlongForTests(badN.x, badN.y, badN.z, V(5, 5, 5), V(5, 5, 5), 0, 0, 0, 0, out double aLo, out double aHi));
                Assert.Equal(0.0, aLo);
                Assert.Equal(1.0, aHi);
            }

            // 2) Fast-path exact pin & 2^k scale invariance:
            //    n = (3, 4, 0) normalizes to (0.75, 1.0, 0), n1 = 1.75; f0 = (0, 2, 0), f1 = (0, -2, 0), e = 0, ms = 0.25
            //    baseScale = 0.25 * 1.75 = 0.4375, ad = 2.0, margin = 0.4375*(1+2e-15) + 2e-15*2.0
            {
                double baseMargin = (0.25 * 1.75) * (1.0 + 2e-15);
                double margin = baseMargin + 2e-15 * 2.0;
                double dlo0 = 2.0 - margin, dlo1 = -2.0 - margin;
                double dhi0 = 2.0 + margin, dhi1 = -2.0 + margin;
                double expLo = 0.0, expHi = 1.0;
                CcdSolver.UpdateTClip(dlo0, dhi0, dlo1, dhi1, 0.0, ref expLo, ref expHi);
                Assert.False(solver.SeparatedAlongForTests(3, 4, 0, V(0, 2, 0), V(0, -2, 0), 0, 0, 0, 0.25, out double aLo, out double aHi));
                Assert.Equal(BitConverter.DoubleToInt64Bits(expLo), BitConverter.DoubleToInt64Bits(aLo));
                Assert.Equal(BitConverter.DoubleToInt64Bits(expHi), BitConverter.DoubleToInt64Bits(aHi));
                Assert.True(aLo > 0.3 && aHi < 0.7, $"aLo={aLo:R} aHi={aHi:R}");
                // 2^k scaling of both f, ms and n must produce bit-identical [aLo, aHi]
                foreach (double sc in new[] { Math.Pow(2, -30), Math.Pow(2, 30) }) {
                    Assert.False(solver.SeparatedAlongForTests(3 * sc * sc, 4 * sc * sc, 0, V(0, 2 * sc, 0), V(0, -2 * sc, 0), 0, 0, 0, 0.25 * sc, out double sLo, out double sHi));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(aLo), BitConverter.DoubleToInt64Bits(sLo));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(aHi), BitConverter.DoubleToInt64Bits(sHi));
                }
            }

            // 3) Guard & margin tests:
            //    a) Cancellation in d = nx*fx + ny*fy + nz*fz (kills dropping 2e-15 * ad):
            //       exact n . f <= 0, but rounded d can be positive when terms cancel -> MUST NOT separate
            {
                var rndC = new Random(1660);
                for (int i = 0; i < 400; ++i) {
                    double u = 1.0 + rndC.NextDouble();
                    double v = 1.0 + rndC.NextDouble();
                    double c1 = 1.0 + rndC.NextDouble();
                    // Choose f0 so exact u*f0.x + v*f0.y + 1.0*f0.z <= 0 while fl(u*f0.x + v*f0.y + f0.z) can round positive
                    double fz = -(u * c1 + v * (1.0 + rndC.NextDouble()));
                    double fy = (-(u * c1) - fz) / v;
                    ExactDyadic ex = D(u) * D(c1) + D(v) * D(fy) + D(1.0) * D(fz);
                    if (ex.Sign <= 0) {
                        Assert.False(solver.SeparatedAlongForTests(u, v, 1.0, V(c1, fy, fz), V(c1, fy, fz), 0, 0, 0, 0, out _, out _),
                            $"cancellation i={i}: falsely separated when exact dot <= 0");
                    }
                }
                // Explicit base-enclosure rounding test (kills dropping (1 + 2e-15) on baseScale):
                // eDot + lim can round DOWN by up to 4 ulp relative to exact |n|.e + ms|n|_1;
                // placing f0 at exact dot == exact (|n|.e + ms|n|_1) must NEVER separate.
                for (int i = 0; i < 400; ++i) {
                    double n0 = 1.0 + rndC.NextDouble() * 0.5;
                    double n1 = 1.0 + rndC.NextDouble() * 0.5;
                    double n2 = 1.0 + rndC.NextDouble() * 0.5;
                    double e0 = 1.0 + rndC.NextDouble();
                    double e1 = 1.0 + rndC.NextDouble();
                    double e2 = 1.0 + rndC.NextDouble();
                    double m = 1.0 + rndC.NextDouble();
                    ExactDyadic exactBase = D(n0) * (D(e0) + D(m)) + D(n1) * (D(e1) + D(m)) + D(n2) * (D(e2) + D(m));
                    double fx = (n0 * (e0 + m) + n1 * (e1 + m) + n2 * (e2 + m)) / n0;
                    while (ExactDyadic.Compare(D(n0) * D(fx), exactBase) > 0) fx = ExactInterval.Down(fx);
                    Assert.True(ExactDyadic.Compare(D(n0) * D(fx), exactBase) <= 0);
                    Assert.False(solver.SeparatedAlongForTests(n0, n1, n2, V(fx, 0, 0), V(fx, 0, 0), e0, e1, e2, m, out _, out _),
                        $"baseMargin i={i}: falsely separated when exact n.f <= exact |n|.e + ms|n|_1");
                }
            }

            //    b) NormalSqLo guard (subnormal scale ~ 2^-1073, kills scale > NormalSqLo -> scale > 0)
            //       and NormalSqHi guard (huge scale >= 1e290, kills dropping scale < NormalSqHi)
            {
                double tiny = double.Epsilon;
                // Subnormal exact dot == ms * |n|_1 -> must NOT separate; subnormal separated -> ExactInterval fallback still separates
                Assert.False(solver.SeparatedAlongForTests(1, 0, 0, V(tiny, 0, 0), V(tiny, 0, 0), 0, 0, 0, tiny, out _, out _));
                Assert.True(solver.SeparatedAlongForTests(1, 0, 0, V(16 * tiny, 0, 0), V(16 * tiny, 0, 0), 0, 0, 0, tiny, out _, out _));
                // Subnormal double-rounding killer for scale > NormalSqLo -> scale > 0:
                // n = (1.6, 1.6, 0), f = (tiny, tiny, 0), ms = tiny: exact dot = 3.2*tiny == ms*|n|_1,
                // yet in subnormal float fl(1.6*tiny)+fl(1.6*tiny) = 4*tiny > fl(3.2*tiny) = 3*tiny!
                Assert.False(solver.SeparatedAlongForTests(1.6, 1.6, 0, V(tiny, tiny, 0), V(tiny, tiny, 0), 0, 0, 0, tiny, out _, out _));
                // Huge scale (1e295 >= NormalSqHi): fallback to ExactInterval still separates when strictly separated and never separates on tie
                Assert.True(solver.SeparatedAlongForTests(1, 0, 0, V(2e295, 0, 0), V(2e295, 0, 0), 0, 0, 0, 1e295, out _, out _));
                Assert.False(solver.SeparatedAlongForTests(1, 0, 0, V(1e295, 0, 0), V(1e295, 0, 0), 0, 0, 0, 1e295, out _, out _));
                // Overflowing fast-path sum (1e308 + 1e308 = +Inf, kills dropping scale < NormalSqHi which turns d - margin into NaN -> false)
                Assert.True(solver.SeparatedAlongForTests(1, 1, 0, V(1e308, 1e308, 0), V(1e308, 1e308, 0), 0, 0, 0, 0, out _, out _));
                Assert.False(solver.SeparatedAlongForTests(1, 1, 1, V(1e308, 0, 0), V(1e308, 0, 0), 0, 0, 0, 1e308, out _, out _));
            }

            // 4) Randomized exact-dyadic soundness oracle across scales (including subnormal -1040, normal, UTM, huge 480)
            var rnd = new Random(1661);
            int separatedCount = 0, clippedCount = 0;
            foreach (int exp in new[] { -1040, -537, -20, 0, 20, 480 }) {
                double sc = Math.Pow(2, exp);
                for (int i = 0; i < 200; ++i) {
                    double nx = rnd.NextDouble() * 2 - 1, ny = rnd.NextDouble() * 2 - 1, nz = rnd.NextDouble() * 2 - 1;
                    if (nx == 0 && ny == 0 && nz == 0) nx = 1;
                    Vector3d f0 = V((rnd.NextDouble() * 2 - 1) * sc, (rnd.NextDouble() * 2 - 1) * sc, (rnd.NextDouble() * 2 - 1) * sc);
                    Vector3d f1 = V((rnd.NextDouble() * 2 - 1) * sc, (rnd.NextDouble() * 2 - 1) * sc, (rnd.NextDouble() * 2 - 1) * sc);
                    double e0 = rnd.NextDouble() * 0.05 * sc, e1 = rnd.NextDouble() * 0.05 * sc, e2 = rnd.NextDouble() * 0.05 * sc;
                    double ms = (i % 3 == 0) ? 0.0 : rnd.NextDouble() * 0.2 * sc;
                    bool sep = solver.SeparatedAlongForTests(nx, ny, nz, f0, f1, e0, e1, e2, ms, out double aLo, out double aHi);

                    ExactDyadic exN0 = D(nx), exN1 = D(ny), exN2 = D(nz);
                    ExactDyadic exBase = D(Math.Abs(nx)) * (D(e0) + D(ms)) + D(Math.Abs(ny)) * (D(e1) + D(ms)) + D(Math.Abs(nz)) * (D(e2) + D(ms));
                    ExactDyadic d0 = exN0 * D(f0.x) + exN1 * D(f0.y) + exN2 * D(f0.z);
                    ExactDyadic d1 = exN0 * D(f1.x) + exN1 * D(f1.y) + exN2 * D(f1.z);
                    ExactDyadic g0Lo = d0 - exBase, g0Hi = d0 + exBase;
                    ExactDyadic g1Lo = d1 - exBase, g1Hi = d1 + exBase;

                    if (sep) {
                        ++separatedCount;
                        // Must be strictly separated for ALL alpha in [0, 1]: either both g_lo > 0 or both g_hi < 0
                        bool allPos = g0Lo.Sign > 0 && g1Lo.Sign > 0;
                        bool allNeg = g0Hi.Sign < 0 && g1Hi.Sign < 0;
                        Assert.True(allPos || allNeg, $"exp={exp} i={i}: unsound separation g0=[{g0Lo.Sign},{g0Hi.Sign}] g1=[{g1Lo.Sign},{g1Hi.Sign}]");
                    } else {
                        Assert.True(aLo >= 0.0 && aHi <= 1.0 && aLo <= aHi);
                        if (aLo > 0.0) {
                            ++clippedCount;
                            // At alpha = aLo, at least one side must still be >= 0 (lo) or <= 0 (hi) while the t=0 end was strictly outside
                            ExactDyadic oneMinus = D(1.0) - D(aLo);
                            bool leftAbove = g0Lo.Sign > 0 && (oneMinus * g0Lo + D(aLo) * g1Lo).Sign >= 0;
                            bool leftBelow = g0Hi.Sign < 0 && (oneMinus * g0Hi + D(aLo) * g1Hi).Sign <= 0;
                            Assert.True(leftAbove || leftBelow, $"exp={exp} i={i}: unsound left clip aLo={aLo:R}");
                        }
                        if (aHi < 1.0) {
                            ++clippedCount;
                            ExactDyadic oneMinus = D(1.0) - D(aHi);
                            bool rightAbove = g1Lo.Sign > 0 && (oneMinus * g0Lo + D(aHi) * g1Lo).Sign >= 0;
                            bool rightBelow = g1Hi.Sign < 0 && (oneMinus * g0Hi + D(aHi) * g1Hi).Sign <= 0;
                            Assert.True(rightAbove || rightBelow, $"exp={exp} i={i}: unsound right clip aHi={aHi:R}");
                        }
                    }
                }
            }
            Assert.True(separatedCount > 100 && clippedCount > 100, $"sep={separatedCount} clip={clippedCount}");
        }

        [Fact]
        public void UpdateTClip_AllFourBranches_SoundAndExact()
        {
            ExactDyadic D(double x) => ExactDyadic.Of(x);

            // 1) All four branches individually:
            //    Branch 1: lo0 > thr && lo1 <= thr (left clip from above +thr): lo0=3, lo1=-1, thr=1 -> exact a = 2/4 = 0.5
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(3, 5, -1, 1, 1, ref aLo, ref aHi);
                Assert.True(aLo <= 0.5 && aLo > 0.499999999999, $"b1 aLo={aLo:R}");
                Assert.Equal(1.0, aHi);
            }
            //    Branch 2: hi0 < -thr && hi1 >= -thr (left clip from below -thr): hi0=-3, hi1=1, thr=1 -> exact a = 2/4 = 0.5
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(-5, -3, -1, 1, 1, ref aLo, ref aHi);
                Assert.True(aLo <= 0.5 && aLo > 0.499999999999, $"b2 aLo={aLo:R}");
                Assert.Equal(1.0, aHi);
            }
            //    Branch 3: lo1 > thr && lo0 <= thr (right clip from above +thr): lo0=-1, lo1=3, thr=1 -> exact a = 2/4 = 0.5
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(-1, 1, 3, 5, 1, ref aLo, ref aHi);
                Assert.Equal(0.0, aLo);
                Assert.True(aHi >= 0.5 && aHi < 0.500000000001, $"b3 aHi={aHi:R}");
            }
            //    Branch 4: hi1 < -thr && hi0 >= -thr (right clip from below -thr): hi0=1, hi1=-3, thr=1 -> exact a = 2/4 = 0.5
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(-1, 1, -5, -3, 1, ref aLo, ref aHi);
                Assert.Equal(0.0, aLo);
                Assert.True(aHi >= 0.5 && aHi < 0.500000000001, $"b4 aHi={aHi:R}");
            }
            //    Combined two-axis disjoint clips: axis 0 keeps [0.6, 1], axis 1 keeps [0, 0.4] -> aLo > aHi (empty!)
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(3, 5, -2, 0, 0, ref aLo, ref aHi); // aLo ~ 0.6
                CcdSolver.UpdateTClip(-2, 0, 3, 5, 0, ref aLo, ref aHi); // aHi ~ 0.4
                Assert.True(aLo > aHi, $"expected empty clip, got [{aLo:R}, {aHi:R}]");
            }
            //    NaN / Inf inputs never narrow [0, 1] unsafely
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(double.NaN, 1, -1, 1, 0, ref aLo, ref aHi);
                CcdSolver.UpdateTClip(double.PositiveInfinity, double.PositiveInfinity, -1, 1, 0, ref aLo, ref aHi);
                CcdSolver.UpdateTClip(-1, 1, double.NegativeInfinity, double.NegativeInfinity, 0, ref aLo, ref aHi);
                CcdSolver.UpdateTClip(3, 5, -1, 1, double.PositiveInfinity, ref aLo, ref aHi);
                Assert.Equal(0.0, aLo);
                Assert.Equal(1.0, aHi);
            }
            //    Overflowing denominator difference (1e308 - (-1e308) -> [MaxValue, +Inf]) still encloses the true root 0.5 soundly
            {
                double aLo = 0, aHi = 1;
                CcdSolver.UpdateTClip(1e308, 1e308, -1e308, -1e308, 0, ref aLo, ref aHi);
                Assert.True(aLo >= 0.0 && aLo <= 0.5 && aHi >= 0.5 && aHi <= 1.0, $"overflow diff got [{aLo:R}, {aHi:R}]");
            }

            // 2) Randomized exact-dyadic soundness across 1,000 non-dyadic ratios (catches swapping .Lo/.Hi or flipping signs)
            var rnd = new Random(1662);
            for (int i = 0; i < 1000; ++i) {
                double lo0 = rnd.NextDouble() * 4 - 2;
                double hi0 = lo0 + rnd.NextDouble() * 2;
                double lo1 = rnd.NextDouble() * 4 - 2;
                double hi1 = lo1 + rnd.NextDouble() * 2;
                double thr = (i % 4 == 0) ? 0.0 : rnd.NextDouble() * 0.5;
                double aLo = 0.0, aHi = 1.0;
                CcdSolver.UpdateTClip(lo0, hi0, lo1, hi1, thr, ref aLo, ref aHi);
                Assert.True(aLo >= 0.0 && aHi <= 1.0);
                if (aLo > 0.0) {
                    ExactDyadic om = D(1.0) - D(aLo);
                    bool okAbove = lo0 > thr && ExactDyadic.Compare(om * D(lo0) + D(aLo) * D(lo1), D(thr)) >= 0;
                    bool okBelow = hi0 < -thr && ExactDyadic.Compare(om * D(hi0) + D(aLo) * D(hi1), D(-thr)) <= 0;
                    Assert.True(okAbove || okBelow, $"i={i}: unsound aLo={aLo:R}");
                }
                if (aHi < 1.0) {
                    ExactDyadic om = D(1.0) - D(aHi);
                    bool okAbove = lo1 > thr && ExactDyadic.Compare(om * D(lo0) + D(aHi) * D(lo1), D(thr)) >= 0;
                    bool okBelow = hi1 < -thr && ExactDyadic.Compare(om * D(hi0) + D(aHi) * D(hi1), D(-thr)) <= 0;
                    Assert.True(okAbove || okBelow, $"i={i}: unsound aHi={aHi:R}");
                }
            }
        }

        [Fact]
        public void LinearBezierTClip_SoundDeterministicReusableAndCutsIterations()
        {
            // Verify: (a) substantial iteration reduction on near-contact VF/EE queries (kills disabling t-clipping),
            // (b) solver reuse matches fresh solver bit-for-bit, (c) determinism across repeated runs,
            // (d) G2/G3 soundness via TimeOfImpact <= NearContactTime and AssertNearBox.
            var rnd = new Random(1663);
            var reused = new CcdSolver();
            var o = new CcdOptions();
            long totalIters = 0;
            int collisions = 0;
            const int n = 200;
            for (int i = 0; i < n; ++i) {
                bool vf = (i & 1) == 0;
                Query q = new Query { VF = vf };
                for (int k = 0; k < 4; ++k) {
                    q.S[k] = V(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                    q.E[k] = V(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                }
                // Force near-contact around a random t in [0.1, 0.9]
                double tStar = 0.1 + 0.8 * rnd.NextDouble();
                double u = 0.1 + 0.35 * rnd.NextDouble(), v = 0.1 + 0.35 * rnd.NextDouble();
                if (vf) {
                    Vector3d p1 = (1 - tStar) * q.S[1] + tStar * q.E[1];
                    Vector3d p2 = (1 - tStar) * q.S[2] + tStar * q.E[2];
                    Vector3d p3 = (1 - tStar) * q.S[3] + tStar * q.E[3];
                    Vector3d hit = p1 + u * (p2 - p1) + v * (p3 - p1) + V((rnd.NextDouble() - 0.5) * 0.01, (rnd.NextDouble() - 0.5) * 0.01, (rnd.NextDouble() - 0.5) * 0.01);
                    Vector3d vel = V((rnd.NextDouble() - 0.5) * 0.5, (rnd.NextDouble() - 0.5) * 0.5, (rnd.NextDouble() - 0.5) * 0.5);
                    q.S[0] = hit - tStar * vel;
                    q.E[0] = hit + (1 - tStar) * vel;
                } else {
                    Vector3d p2 = (1 - tStar) * q.S[2] + tStar * q.E[2];
                    Vector3d p3 = (1 - tStar) * q.S[3] + tStar * q.E[3];
                    Vector3d hit = p2 + v * (p3 - p2) + V((rnd.NextDouble() - 0.5) * 0.01, (rnd.NextDouble() - 0.5) * 0.01, (rnd.NextDouble() - 0.5) * 0.01);
                    Vector3d p0 = (1 - tStar) * (q.S[0] + u * (q.S[1] - q.S[0])) + tStar * (q.E[0] + u * (q.E[1] - q.E[0]));
                    Vector3d shift = hit - p0;
                    q.S[0] += shift; q.S[1] += shift; q.E[0] += shift; q.E[1] += shift;
                }
                CcdResult rReused = Run(q, o, reused);
                CcdResult rFresh = Run(q, o, new CcdSolver());
                sameResult(rReused, rFresh, $"reuse q={i}");
                if (rReused.Status == CcdStatus.Collision) {
                    ++collisions;
                    Assert.True(rReused.TimeOfImpact <= rReused.NearContactTime, $"q={i}: toi > near");
                    AssertNearBox(q, reused.LastBox, o.MinSeparation + 2 * rReused.ErrorBound + rReused.ToleranceReached);
                }
                totalIters += rReused.Iterations;
            }
            Assert.True(collisions > 40, $"collisions={collisions}");
            double meanIters = totalIters / (double)n;
            // Without linear Bezier t-clipping, these near-contact queries average ~200+ boxes; with clipping, ~126.5
            Assert.True(meanIters < 150.0, $"meanIters={meanIters:F1} (expected < 150 with t-clipping)");
        }
    }
}
