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

        // G1 / G2 on exact contacts: never NoCollision, and the time of impact not after the contact
        [Fact]
        public void ExactContacts_AreNeverMissed()
        {
            var rnd = new Random(7);
            var solver = new CcdSolver();
            int n = 0;
            for (int i = 0; i < 600; ++i) {
                bool vf = (i & 1) == 0;
                Query q = ConstructedContact(rnd, vf, out double tStar);
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
        [Fact]
        public void RandomQueries_SampledContactsAndReportedBoxes()
        {
            var rnd = new Random(11);
            var solver = new CcdSolver();
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

        // G3: at the corners of the reported box every coordinate of the exact F is within bound
        internal static void AssertNearBox(Query q, double[] box, double bound)
        {
            for (int k = 0; k < 8; ++k) {
                double t = box[(k & 1)], u = box[2 + ((k >> 1) & 1)], v = box[4 + ((k >> 2) & 1)];
                for (int axis = 0; axis < 3; ++axis) {
                    ExactDyadic f = ExactF(q, axis, t, u, v);
                    // compare with the bound rounded up by a relative 2^-50 (the bound is a computed double)
                    double b = bound * (1 + 1.0 / (1L << 50));
                    Assert.True(ExactDyadic.Compare(f, ExactDyadic.Of(b)) <= 0 && ExactDyadic.Compare(f, ExactDyadic.Of(-b)) >= 0,
                        $"corner {k} axis {axis}: |F| > {b:R}");
                }
            }
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

        // a few queries with clear margins (deep crossings, clear misses) for the invariance test
        static IEnumerable<(Query q, CcdStatus status, double tStar)> invarianceSet()
        {
            yield return (vertexThrough(0.25, 0.25), CcdStatus.Collision, 0.5);
            yield return (vertexThrough(0.75, 0.75), CcdStatus.NoCollision, 0);
            yield return (VFq(V(0.3, 0.2, 1), V(0, 0, 0.2), V(1, 0, -0.1), V(0, 1, 0), V(0.1, 0.4, -1), V(0, 0.1, -0.2), V(1, 0.1, 0.1), V(0.1, 1, 0)), CcdStatus.Collision, double.NaN);
            Vector3d c = V(0, -1, 0), d = V(0, 1, 0);
            yield return (EEq(V(-1, 0, 1), V(1, 0, 1), c, d, V(-1, 0, -1), V(1, 0, -1), c, d), CcdStatus.Collision, 0.5);
            yield return (EEq(V(-1, 2, 1), V(1, 2, 1), c, d, V(-1, 2, -1), V(1, 2, -1), c, d), CcdStatus.NoCollision, 0);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (var (q, status, tStar) in invarianceSet()) {
                CcdResult r0 = Run(q);
                Assert.Equal(status, r0.Status);
                CcdResult r = Run(q.Map(s.Apply));
                Assert.Equal(status, r.Status);
                if (s.IsExact) {
                    // the default tolerance is relative: everything scales exactly
                    Assert.Equal(r0.TimeOfImpact, r.TimeOfImpact);
                    Assert.Equal(r0.Iterations, r.Iterations);
                    Assert.Equal(s.ApplyLength(r0.ToleranceReached), r.ToleranceReached);
                } else if (status == CcdStatus.Collision) {
                    // G2 holds in each frame; the times agree within the boxes' time widths
                    Assert.True(Math.Abs(r.TimeOfImpact - r0.TimeOfImpact) < 1e-4, $"{name}: {r.TimeOfImpact:R} vs {r0.TimeOfImpact:R}");
                    if (double.IsNaN(tStar) == false) Assert.True(r.TimeOfImpact <= tStar + 1e-9);
                }
            }
        }
    }
}
