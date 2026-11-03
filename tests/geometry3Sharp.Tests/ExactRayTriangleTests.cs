using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact ray-triangle decisions (math/ExactRayTriangle.cs; findings Q2 and Q3 in docs/ISSUE_CASE_FINDINGS.md):
    /// the symbolic perturbation against an explicit one in big integers, watertight hits and single-counted
    /// crossings through vertices and edges of closed meshes, invariance under Similarity.All(), and the reuse of
    /// IntrRay3Triangle3.
    /// </summary>
    public class ExactRayTriangleTests
    {
        // ---- the perturbation, explicitly ----

        static BigInteger[] Big(Vector3d v, int k, int ex, int ey, int ez)
        {
            // v 2^k + (2^ex, 2^ey, 2^ez) for small integer coordinates (2^e omitted for e < 0)
            BigInteger s = BigInteger.One << k;
            Func<double, int, BigInteger> c = (x, e) => new BigInteger(x) * s + (e >= 0 ? BigInteger.One << e : BigInteger.Zero);
            return new[] { c(v.x, ex), c(v.y, ey), c(v.z, ez) };
        }

        static int DetSign(BigInteger[] o, BigInteger[] p, BigInteger[] a, BigInteger[] b)
        {
            var u = new BigInteger[3]; var v = new BigInteger[3]; var w = new BigInteger[3];
            for (int i = 0; i < 3; ++i) { u[i] = p[i] - o[i]; v[i] = a[i] - o[i]; w[i] = b[i] - o[i]; }
            BigInteger d = (u[1] * v[2] - u[2] * v[1]) * w[0] + (u[2] * v[0] - u[0] * v[2]) * w[1] + (u[0] * v[1] - u[1] * v[0]) * w[2];
            return d.Sign;
        }

        [Fact]
        public void EdgeSign_EqualsTheExplicitlyPerturbedDeterminant()
        {
            // ε = 2^-20, scaled by 2^240: o + (ε⁴, ε⁸, ε¹²), p + (ε, ε², ε³), on points of a small integer grid, so that
            // most configurations are degenerate (collinear, coplanar, coincident). Also the antisymmetry in a, b.
            var rnd = new Random(5);
            int degenerate = 0;
            for (int it = 0; it < 20000; ++it) {
                Func<Vector3d> r = () => new Vector3d(rnd.Next(-2, 3), rnd.Next(-2, 3), rnd.Next(-2, 3));
                Vector3d o = r(), p = r(), a = r(), b = r();
                if (o == p) continue;
                int s = ExactRayTriangle.EdgeSign(o, p, a, b);
                Assert.Equal(-s, ExactRayTriangle.EdgeSign(o, p, b, a));
                if (a == b) { Assert.Equal(0, s); continue; }
                if (ExactPredicates.Orient3d(o, p, a, b) == 0) degenerate++;
                int k = 240;
                int expected = DetSign(Big(o, k, k - 80, k - 160, k - 240), Big(p, k, k - 20, k - 40, k - 60), Big(a, k, -1, -1, -1), Big(b, k, -1, -1, -1));
                Assert.True(expected == s, $"o {o} p {p} a {a} b {b}: {s}, perturbed {expected}");
            }
            Assert.True(degenerate > 1000, $"{degenerate}");
        }

        // ---- a closed mesh with many exact degeneracies ----

        /// <summary> the cube [0,n]^3, each face an n x n grid of unit squares split along alternating diagonals, outward </summary>
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
        public void RaysThroughVerticesAndEdgesOfAClosedMesh_ParityAndHitsRight()
        {
            // Rays from the integer and half-integer points of [-1, 5]^3 (off the surface) along the 26 grid directions and
            // three more, unnormalized: they pass exactly through vertices and along or across edges of the cube's faces.
            // The crossing count (IsInside's parity) is odd iff the point is inside; a ray from inside always hits
            // (FindNearestHitTriangle, closed triangles: watertight), and every crossing is also a hit.
            int n = 4;
            DMesh3 mesh = GridCube(n);
            foreach (DMeshAABBTree3 tree in new[] { 1, 6 }.Select(leaf => { var t = new DMeshAABBTree3(mesh) { TopDownLeafMaxTriCount = leaf }; t.Build(); return t; })) {
                for (int z = -2; z <= 10; ++z)
                    for (int y = -2; y <= 10; ++y)
                        for (int x = -2; x <= 10; ++x) {
                            var q = new Vector3d(x, y, z) * 0.5;
                            Func<double, bool> inside = c => c > 0 && c < n;
                            Func<double, bool> onSide = c => c >= 0 && c <= n;
                            bool isIn = inside(q.x) && inside(q.y) && inside(q.z);
                            bool onSurface = !isIn && onSide(q.x) && onSide(q.y) && onSide(q.z);
                            if (onSurface) continue;
                            Assert.Equal(isIn, tree.IsInside(q));
                            foreach (Vector3d d in Directions) {
                                int crossings = tree.CountCrossings(q, d);
                                Assert.True((crossings % 2 == 1) == isIn, $"{q} along {d}: {crossings} crossings");
                                // the closed hits of the same (exact, unnormalized) line, by brute force, once
                                if (tree.TopDownLeafMaxTriCount == 1) {
                                    var exactRay = new Ray3d(q, d, true);
                                    int all = mesh.TriangleIndices().Count(ti => {
                                        Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                                        mesh.GetTriVertices(ti, ref a, ref b, ref c);
                                        return IntrRay3Triangle3.Intersects(ref exactRay, ref a, ref b, ref c, out double _);
                                    });
                                    Assert.True(all >= crossings && (all > 0 || crossings == 0), $"{q} along {d}: {all} hits, {crossings} crossings");
                                }
                                // a ray from inside always hits (normalized, so its line is rounded)
                                var ray = new Ray3d(q, d.Normalized);
                                if (isIn) {
                                    int hit = tree.FindNearestHitTriangle(ray);
                                    Assert.True(hit >= 0, $"{q} along {d}: no hit from inside");
                                    Assert.True(MeshQueries.TriangleIntersection(mesh, hit, ray).Find());
                                }
                            }
                        }
            }
        }

        [Fact]
        public void ExactWinding_ThroughVerticesAndEdges_IsExact()
        {
            // the exact winding number of VolumeSampleCells on the same solid, at points whose segment to the far point
            // may pass anywhere: 1 inside, 0 outside, now in one cast (no degenerate direction to retry)
            DMesh3 mesh = GridCube(4);
            var tree = new DMeshAABBTree3(mesh, true);
            AxisAlignedBox3d root = mesh.CachedBounds;
            for (int z = -1; z <= 9; ++z)
                for (int y = -1; y <= 9; y += 2)
                    for (int x = -1; x <= 9; x += 2) {
                        var q = new Vector3d(x, y, z) * 0.5;
                        bool isIn = q.x > 0 && q.x < 4 && q.y > 0 && q.y < 4 && q.z > 0 && q.z < 4;
                        bool onSurface = !isIn && q.x >= 0 && q.x <= 4 && q.y >= 0 && q.y <= 4 && q.z >= 0 && q.z <= 4;
                        if (onSurface) continue;
                        Assert.True(VolumeSampleCells.ExactWinding(mesh, tree, q, root, out int w));
                        Assert.Equal(isIn ? 1 : 0, w);
                    }
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(0.0009765625)]      // 2^-10
        [InlineData(1048576.0)]         // 2^20
        public void WindingAlong_SegmentThroughVerticesAndEdges_CountsOnce(double scale)
        {
            // segments placed exactly through the solid's vertices, edges and face diagonals (ExactWinding's own far
            // point almost never meets them): each crossing counts once, so the winding number is 1 inside, 0 outside
            DMesh3 mesh = GridCube(4);
            MeshTransforms.Scale(mesh, scale);
            var tree = new DMeshAABBTree3(mesh, true);
            var cases = new (Vector3d p, Vector3d q, int w)[] {
                (new Vector3d(2, 2, 2), new Vector3d(6, 6, 6), 1),              // out through the corner (4,4,4)
                (new Vector3d(-2, -2, -2), new Vector3d(6, 6, 6), 0),           // in and out through two corners
                (new Vector3d(2, 2, 2), new Vector3d(6, 2, 3), 1),              // through the grid edge point (4,2,2.5)
                (new Vector3d(2, 2.5, 2.5), new Vector3d(6, 2.5, 2.5), 1),      // through a quad's diagonal midpoint
                (new Vector3d(-1, 2.5, 2.5), new Vector3d(6, 2.5, 2.5), 0),     // in and out through two diagonals
                (new Vector3d(2, 2, 2.5), new Vector3d(6, 6, 2.5), 1),          // through the crease edge at (4,4,2.5)
                (new Vector3d(-1, -1, 1.5), new Vector3d(6, 6, 1.5), 0),        // in and out through two crease edges
            };
            foreach (var (p, q, w) in cases)
                Assert.Equal(w, VolumeSampleCells.WindingAlong(mesh, tree, scale * p, scale * q));
        }

        [Fact]
        public void SharedDifferenceFilter_SameDecisionsAsOrient3d_NearEdgesAndVertices()
        {
            // ClosedCrossing and LineCrossing share the differences between their three edge predicates and run
            // Orient3d's filter on them; their decisions must be those of the plain ExactPredicates.Orient3d /
            // EdgeSign, also on rays aimed at edges and vertices (exact zeros and filter failures). At 1 and 1e-3 the
            // filter decides about half of the signs; at 1e-150, 1e150 and the subnormal scale none (the permanent
            // under- or overflows), so there only the fallback is compared. This checks the sharing (differences,
            // edge roles, fallback arguments), not the filter's bound itself: the reference calls the same
            // Orient3dFilter, whose soundness ExactPredicatesTests and PredicateDifferentialTests check.
            var rnd = new Random(23);
            Func<double> r = () => rnd.NextDouble() * 2 - 1;
            int zeros = 0, n = 0;
            foreach (double scale in new[] { 1.0, 1e-3, 1e-150, 1e150, 3.0e-310 / 4 }) {
                for (int i = 0; i < 20000; ++i) {
                    Vector3d a = new Vector3d(r(), r(), r()), b = new Vector3d(r(), r(), r()), c = new Vector3d(r(), r(), r());
                    if (i % 4 == 0) c = a + (b - a) * 0.5 + new Vector3d(0, 0, 1e-17 * r());     // nearly degenerate
                    Vector3d o = new Vector3d(r(), r(), r()) * 4;
                    // aim at a vertex, an edge point (on the double grid or one ulp off), or the interior
                    double w = rnd.NextDouble();
                    Vector3d target = (i % 3 == 0) ? a : (i % 3 == 1) ? a + (b - a) * w : (a + b + c) / 3;
                    if (i % 7 == 0) target.x = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(target.x) + (rnd.Next(2) == 0 ? 1 : -1));   // one ulp off
                    Vector3d p = (i % 5 == 0) ? target : o + (target - o) * 2;
                    a *= scale; b *= scale; c *= scale; o *= scale; p *= scale;
                    if (o == p) continue;
                    int s0 = ExactPredicates.Orient3d(o, p, a, b), s1 = ExactPredicates.Orient3d(o, p, b, c), s2 = ExactPredicates.Orient3d(o, p, c, a);
                    int closed = (s0 * s1 < 0 || s0 * s2 < 0 || s1 * s2 < 0) ? 0 : (s0 != 0 ? s0 : (s1 != 0 ? s1 : s2));
                    int e0 = ExactRayTriangle.EdgeSign(o, p, a, b), e1 = ExactRayTriangle.EdgeSign(o, p, b, c), e2 = ExactRayTriangle.EdgeSign(o, p, c, a);
                    int line = (e0 != 0 && e1 == e0 && e2 == e0) ? e0 : 0;
                    Assert.Equal(closed, ExactRayTriangle.ClosedCrossing(o, p, a, b, c));
                    Assert.Equal(line, ExactRayTriangle.LineCrossing(o, p, a, b, c));
                    if (s0 == 0 || s1 == 0 || s2 == 0) zeros++;
                    n++;
                }
            }
            Assert.True(zeros > 1000, $"only {zeros} of {n} cases with an exact zero");
        }

        [Fact]
        public void SecondPoint_IsOriginPlusDirectionScaledByTheDocumentedPowerOfTwo()
        {
            // p = origin + ScaleB(direction, m), m = e(max |origin|) - e(max |direction|) + 1 clamped to +-2000 (0 for
            // a zero origin), bit for bit: the one-multiplication path for a normal 2^m and the ScaleB path beyond,
            // with subnormal and overflowing products
            Func<double, int> e = (x) => {
                long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
                int ex = (int)((bits >> 52) & 0x7FF);
                if (ex != 0) return ex - 1023;
                return (int)((BitConverter.DoubleToInt64Bits(Math.Abs(x) * 18446744073709551616.0) >> 52) & 0x7FF) - 1023 - 64;
            };
            Func<Vector3d, double> maxAbs = (v) => Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
            var rnd = new Random(31);
            Func<int, double> val = (k) => (rnd.NextDouble() * 2 - 1) * Math.Pow(2, k);
            int scaleBPath = 0, fastPath = 0;
            for (int i = 0; i < 20000; ++i) {
                int ko = rnd.Next(-1070, 1020), kd = rnd.Next(-1070, 1020);
                var o = (i % 50 == 0) ? Vector3d.Zero : new Vector3d(val(ko), val(ko - rnd.Next(60)), val(ko - rnd.Next(1100)));
                var d = new Vector3d(val(kd), val(kd - rnd.Next(60)), val(kd - rnd.Next(1100)));
                if (maxAbs(d) == 0) continue;
                int m = maxAbs(o) > 0 ? Math.Max(-2000, Math.Min(2000, e(maxAbs(o)) - e(maxAbs(d)) + 1)) : 0;
                var expected = o + new Vector3d(ExactPoint3.ScaleB(d.x, m), ExactPoint3.ScaleB(d.y, m), ExactPoint3.ScaleB(d.z, m));
                bool finite = !double.IsNaN(expected.x + expected.y + expected.z) && !double.IsInfinity(expected.x + expected.y + expected.z);
                var p = ExactRayTriangle.SecondPoint(o, d);
                if (finite) {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected.x), BitConverter.DoubleToInt64Bits(p.x));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected.y), BitConverter.DoubleToInt64Bits(p.y));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected.z), BitConverter.DoubleToInt64Bits(p.z));
                } else {
                    Assert.Equal(o, p);     // not finite: the origin (no hits)
                }
                if (m >= -1022 && m <= 1023) fastPath++; else scaleBPath++;
            }
            Assert.True(fastPath > 1000 && scaleBPath > 1000, $"paths {fastPath} / {scaleBPath}");
        }

        // ---- invariance ----

        /// <summary> a height field over [0,n]^2 with dyadic heights, two triangles per unit cell, alternating diagonals </summary>
        static DMesh3 HeightField(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i, j, ((i * 7 + j * 3) % 5) / 4.0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    if ((i + j) % 2 == 1) {
                        m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c);
                    } else {
                        m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                    }
                }
            return m;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RaysOntoAHeightField_AllSimilarities_WatertightAndSingleCounted(string name)
        {
            // Vertical rays onto the vertices, edges and faces of a height field (1/4 grid), transformed: a ray onto the
            // closed field always hits (also on its boundary), FindNearestHitTriangle's triangle is among
            // FindAllHitTriangles, and a ray over the interior crosses the field exactly once. Under 2^k the hits, the
            // crossing count and t / 2^k are the same bit for bit (IsExact); under the others the counts hold anyway.
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = HeightField(5), moved = Invariance.Transformed(mesh, sim);
            var tree = new DMeshAABBTree3(mesh, true);
            var movedTree = new DMeshAABBTree3(moved, true);
            for (int j = 0; j <= 20; ++j)
                for (int i = 0; i <= 20; ++i) {
                    var q = new Vector3d(i / 4.0, j / 4.0, 10);
                    bool interior = i > 0 && i < 20 && j > 0 && j < 20;
                    var ray = new Ray3d(q, -Vector3d.AxisZ);
                    var mray = new Ray3d(sim.Apply(ray.Origin), sim.ApplyDirection(ray.Direction).Normalized);
                    foreach (var (t, r, baseline) in new[] { (tree, ray, true), (movedTree, mray, false) }) {
                        int near = t.FindNearestHitTriangle(r);
                        var all = new List<int>();
                        t.FindAllHitTriangles(r, all);
                        int crossings = t.CountCrossings(r.Origin, r.Direction);
                        // (a rotated, rounded ray onto the boundary may pass just outside the field)
                        if (sim.IsExact || baseline || interior) {
                            Assert.True(near >= 0, $"{name}: no hit at {q}");
                            Assert.Contains(near, all);
                        }
                        if (interior)
                            Assert.True(crossings == 1, $"{name}: {crossings} crossings at {q}");
                        Assert.True(crossings <= all.Count);
                    }
                    // an oblique ray from far away onto the same point (its line rounded, through or near the target)
                    var target = new Vector3d(i / 4.0, j / 4.0, 0.5);
                    var oblique = new Ray3d(target + new Vector3d(3.375, 7.125, 10), new Vector3d(-3.375, -7.125, -10).Normalized);
                    var moblique = new Ray3d(sim.Apply(oblique.Origin), sim.ApplyDirection(oblique.Direction).Normalized);
                    if (sim.IsExact) {
                        var o0 = new List<int>(); var o1 = new List<int>();
                        tree.FindAllHitTriangles(oblique, o0); movedTree.FindAllHitTriangles(moblique, o1);
                        Assert.Equal(o0.OrderBy(x => x), o1.OrderBy(x => x));
                        Assert.Equal(tree.CountCrossings(oblique.Origin, oblique.Direction), movedTree.CountCrossings(moblique.Origin, moblique.Direction));
                        Assert.Equal(tree.FindNearestHitTriangle(ray), movedTree.FindNearestHitTriangle(mray));
                        var a0 = new List<int>(); var a1 = new List<int>();
                        tree.FindAllHitTriangles(ray, a0); movedTree.FindAllHitTriangles(mray, a1);
                        Assert.Equal(a0.OrderBy(x => x), a1.OrderBy(x => x));
                        Assert.Equal(tree.CountCrossings(ray.Origin, ray.Direction), movedTree.CountCrossings(mray.Origin, mray.Direction));
                        foreach (int ti in a0) {
                            var h0 = MeshQueries.TriangleIntersection(mesh, ti, ray);
                            var h1 = MeshQueries.TriangleIntersection(moved, ti, mray);
                            Assert.Equal(h0.RayParameter, h1.RayParameter / sim.Scale);
                            Assert.Equal(h0.TriangleBaryCoords, h1.TriangleBaryCoords);
                        }
                    }
                }
        }

    }
}
