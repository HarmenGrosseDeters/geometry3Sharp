using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Ray queries through the public API (findings Q2 and Q3 in docs/ISSUE_CASE_FINDINGS.md): watertight hits through
    /// vertices and edges of a closed mesh, the parity of IsInside under Similarity.All(), the reuse of IntrRay3Triangle3
    /// and non-finite origins. The internal exact decisions are tested in ExactRayTriangleTests.
    /// </summary>
    public class RayQueryWatertightTests
    {
        static DMesh3 GridCube(int n)
        {
            var m = new DMesh3();
            var ids = new Dictionary<Vector3i, int>();
            Func<Vector3i, int> vid = (q) => {
                if (ids.TryGetValue(q, out int id) == false)
                    ids[q] = id = m.AppendVertex(new Vector3d(q.x, q.y, q.z));
                return id;
            };
            for (int axis = 0; axis < 3; ++axis)
                for (int side = 0; side <= 1; ++side)
                    for (int j = 0; j < n; ++j)
                        for (int i = 0; i < n; ++i) {
                            Func<int, int, Vector3i> at = (u, v) => {
                                var q = new int[3];
                                q[axis] = side * n; q[(axis + 1) % 3] = u; q[(axis + 2) % 3] = v;
                                return new Vector3i(q[0], q[1], q[2]);
                            };
                            int a = vid(at(i, j)), b = vid(at(i + 1, j)), c = vid(at(i + 1, j + 1)), d = vid(at(i, j + 1));
                            // (u, v, axis) is right-handed: counter-clockwise in (u, v) points along +axis
                            bool flip = side == 0;
                            if ((i + j) % 2 == 0) {
                                Tri(m, a, b, c, flip); Tri(m, a, c, d, flip);
                            } else {
                                Tri(m, a, b, d, flip); Tri(m, b, c, d, flip);
                            }
                        }
            Assert.True(m.IsClosed());
            Assert.True(ResultChecks.Volume(m) > 0);
            return m;
        }

        static void Tri(DMesh3 m, int a, int b, int c, bool flip)
        {
            Assert.True((flip ? m.AppendTriangle(a, c, b) : m.AppendTriangle(a, b, c)) >= 0);
        }

        static readonly Vector3d[] Directions = Enumerable.Range(0, 27).Where(i => i != 13)
            .Select(i => new Vector3d(i % 3 - 1, (i / 3) % 3 - 1, i / 9 - 1))
            .Concat(new[] { new Vector3d(1, 2, 0), new Vector3d(2, -1, 1), new Vector3d(-1, 1, 2) }).ToArray();

        [Fact]
        public void RaysFromInsideAGridCube_AlongGridDirections_AlwaysHit()
        {
            // rays from the half-integer points inside the closed grid cube along the 26 grid directions and three
            // more: they pass through vertices and along or across edges of its faces, and must hit (watertight;
            // finding Q2), at the triangle the ray meets; the parity of IsInside is right at those points (Q3)
            int n = 4;
            DMesh3 mesh = GridCube(n);
            var tree = new DMeshAABBTree3(mesh, true);
            for (int z = 1; z < 2 * n; ++z)
                for (int y = 1; y < 2 * n; ++y)
                    for (int x = 1; x < 2 * n; ++x) {
                        var q = new Vector3d(x, y, z) * 0.5;
                        Assert.True(tree.IsInside(q), $"{q}");
                        Assert.False(tree.IsInside(q + new Vector3d(2 * n, 0, 0)), $"{q} + (2n, 0, 0)");
                        foreach (Vector3d d in Directions) {
                            var ray = new Ray3d(q, d.Normalized);
                            int hit = tree.FindNearestHitTriangle(ray);
                            Assert.True(hit >= 0, $"{q} along {d}: no hit from inside");
                            Assert.True(MeshQueries.TriangleIntersection(mesh, hit, ray).Find());
                        }
                    }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void IsInside_AllSimilarities_EqualsTheBaseline(string name)
        {
            // IsInside on the grid cube at points off the surface (at least 1/4 away), transformed: the same answer
            DMesh3 mesh = GridCube(4);
            Similarity sim = Similarity.ByName(name);
            var tree = new DMeshAABBTree3(mesh, true);
            var movedTree = new DMeshAABBTree3(Invariance.Transformed(mesh, sim), true);
            for (int z = -1; z <= 9; ++z)
                for (int y = -1; y <= 9; ++y)
                    for (int x = -1; x <= 9; ++x) {
                        var q = new Vector3d(x, y, z) * 0.5 + new Vector3d(0.25, 0.25, 0.25);
                        bool isIn = q.x > 0 && q.x < 4 && q.y > 0 && q.y < 4 && q.z > 0 && q.z < 4;
                        Assert.Equal(isIn, tree.IsInside(q));
                        Assert.True(isIn == movedTree.IsInside(sim.Apply(q)), $"{name}: {q}");
                    }
        }

        // ---- IntrRay3Triangle3 ----

        [Fact]
        public void IntrRay3Triangle3_ComputeTwiceAndReuse_SameAsFresh()
        {
            // reuse: Compute twice gives the same; a new Ray / Triangle recomputes and equals a fresh instance; Find agrees
            // with the static Intersects, and the barycentrics lie in the triangle and give the hit point
            var rnd = new Random(9);
            var a = new Vector3d(0, 0, 0); var b = new Vector3d(1, 0, 0); var c = new Vector3d(0, 1, 0);
            var intr = new IntrRay3Triangle3(new Ray3d(new Vector3d(0.2, 0.2, 1), -Vector3d.AxisZ), new Triangle3d(a, b, c));
            Assert.True(intr.Compute().Find());
            double t0 = intr.RayParameter;
            Assert.True(intr.Compute().Find());
            Assert.Equal(t0, intr.RayParameter);
            // a ray that starts on the triangle (interior, edge, vertex) hits it at t = 0; one in its plane misses
            foreach (var (o, d, expect) in new[] {
                (new Vector3d(0.25, 0.25, 0), new Vector3d(0.3, 0.1, 1), true), (new Vector3d(0.5, 0, 0), new Vector3d(0, 0, -1), true),
                (new Vector3d(0, 1, 0), new Vector3d(1, 1, 1), true), (new Vector3d(2, 2, 0), new Vector3d(0, 0, 1), false),
                (new Vector3d(-1, 0.25, 0), new Vector3d(1, 0, 0), false) }) {
                var r = new Ray3d(o, d);
                var h = new IntrRay3Triangle3(r, new Triangle3d(a, b, c)).Compute();
                Assert.True(expect == h.Find(), $"{o} along {d}");
                Assert.Equal(expect, IntrRay3Triangle3.Intersects(ref r, ref a, ref b, ref c, out double t0hit));
                if (expect) Assert.Equal(0.0, t0hit);
            }
            int hits = 0;
            for (int it = 0; it < 2000; ++it) {
                // origins and targets on a 1/8 grid: many rays onto edges and vertices
                var o = new Vector3d(rnd.Next(-8, 17), rnd.Next(-8, 17), rnd.Next(-16, 17)) / 8.0;
                var target = new Vector3d(rnd.Next(-2, 11), rnd.Next(-2, 11), 0) / 8.0;
                if (o == target) continue;
                // (unnormalized: the line passes exactly through target, ExactRayTriangle.SecondPoint is exact here)
                var ray = new Ray3d(o, target - o, true);
                var tri = new Triangle3d(a, b, c);
                intr.Ray = ray;
                intr.Triangle = tri;
                bool found = intr.Find();
                var fresh = new IntrRay3Triangle3(ray, tri).Compute();
                Assert.Equal(fresh.Result, intr.Result);
                bool inClosed = o.z != 0 && target.x >= 0 && target.y >= 0 && target.x + target.y <= 1;
                Assert.True(found == inClosed, $"{o} -> {target}: {found}");
                Assert.Equal(found, IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref c, out double t));
                Assert.Equal(found, IntersectionUtil.Intersects(ray.Origin, target - o, a, b, c));
                if (found) {
                    hits++;
                    Assert.Equal(t, intr.RayParameter);
                    Assert.Equal(fresh.RayParameter, intr.RayParameter);
                    Vector3d w = intr.TriangleBaryCoords;
                    Assert.True(w.x >= -1e-15 && w.y >= 0 && w.z >= 0, $"{w}");
                    Vector3d hp = w.x * a + w.y * b + w.z * c;
                    Assert.True(hp.Distance(ray.PointAt(t)) < 1e-12, $"{hp} vs {ray.PointAt(t)}");
                }
            }
            Assert.True(hits > 100);
        }

        [Fact]
        public void NonFiniteRayOrigin_NoHitAndNoThrow()
        {
            // a NaN or infinite origin hits nothing (as upstream: every comparison fails), and the exact predicates,
            // which reject non-finite input, are never reached
            DMesh3 mesh = GridCube(2);
            var tree = new DMeshAABBTree3(mesh, true);
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 0, 0), c = new Vector3d(0, 1, 0);
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                var ray = new Ray3d(new Vector3d(bad, 0.2, 1), -Vector3d.AxisZ);
                Assert.False(IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref c, out double _));
                Assert.False(new IntrRay3Triangle3(ray, new Triangle3d(a, b, c)).Find());
                Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(ray));
                Assert.Equal(0, tree.FindAllHitTriangles(ray));
                Assert.False(tree.IsInside(new Vector3d(bad, 1, 1)));
                Assert.False(IntersectionUtil.Intersects(ray.Origin, ray.Direction, a, b, c));
            }
        }
    }
}
