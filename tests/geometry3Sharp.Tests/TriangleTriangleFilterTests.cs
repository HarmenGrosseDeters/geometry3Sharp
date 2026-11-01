using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TriangleTriangleFilter: where it decides, it gives the answer of the exact
    /// ExactTriangleIntersection.IntersectsBeyondShared; on pairs in one axis plane it always decides.
    /// </summary>
    public class TriangleTriangleFilterTests
    {
        struct Pair
        {
            public Index3i Ia, Ib;
            public Triangle3d A, B;
        }

        // pairs of triangles with small integer coordinates in the plane coordinate[axis] = level: without shared
        // vertices, with one and with two (ids as in a mesh). Integer grids give many touching configurations (a
        // vertex on an edge, collinear edges, coincident vertices)
        // (integer = false: random coordinates in [0, 6), no touching configurations)
        static List<Pair> PlanarPairs(int count, int axis, double level, int seed, bool integer = true)
        {
            var rand = new Random(seed);
            Func<Vector3d> point = () => {
                double u = integer ? rand.Next(0, 7) : 6 * rand.NextDouble(), v = integer ? rand.Next(0, 7) : 6 * rand.NextDouble();
                return axis == 0 ? new Vector3d(level, u, v) : axis == 1 ? new Vector3d(v, level, u) : new Vector3d(u, v, level);
            };
            var pairs = new List<Pair>();
            while (pairs.Count < count) {
                int kind = pairs.Count % 3;
                Vector3d a0 = point(), a1 = point(), a2 = point(), b1 = point(), b2 = point(), b0 = point();
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(a0, a1, a2) };
                if (kind == 0) {
                    p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(b0, b1, b2);
                } else if (kind == 1) {
                    // vertex 1 shared, at a varying corner of B
                    int c = rand.Next(3);
                    p.Ib = c == 0 ? new Index3i(1, 4, 5) : c == 1 ? new Index3i(4, 1, 5) : new Index3i(4, 5, 1);
                    p.B = c == 0 ? new Triangle3d(a1, b1, b2) : c == 1 ? new Triangle3d(b1, a1, b2) : new Triangle3d(b1, b2, a1);
                } else {
                    // edge 1-2 shared, either direction
                    bool flip = rand.Next(2) == 0;
                    p.Ib = flip ? new Index3i(2, 1, 5) : new Index3i(1, 2, 5);
                    p.B = flip ? new Triangle3d(a2, a1, b2) : new Triangle3d(a1, a2, b2);
                }
                if (ExactTriangleIntersection.IsDegenerate(ref p.A) || ExactTriangleIntersection.IsDegenerate(ref p.B))
                    continue;
                pairs.Add(p);
            }
            return pairs;
        }

        static Pair Transformed(Pair p, Func<Vector3d, Vector3d> f)
        {
            return new Pair { Ia = p.Ia, Ib = p.Ib, A = new Triangle3d(f(p.A.V0), f(p.A.V1), f(p.A.V2)), B = new Triangle3d(f(p.B.V0), f(p.B.V1), f(p.B.V2)) };
        }

        static bool Exact(Pair p) => ExactTriangleIntersection.IntersectsBeyondShared(p.Ia, ref p.A, p.Ib, ref p.B);
        static int Filter(Pair p) => TriangleTriangleFilter.BeyondShared(p.Ia, ref p.A, p.Ib, ref p.B);

        // the decided pairs agree with the exact test; returns (separate, intersecting, undecided)
        static (int, int, int) CheckAgainstExact(IEnumerable<Pair> pairs, string what)
        {
            int sep = 0, hit = 0, und = 0;
            foreach (Pair pair in pairs) {
                Pair p = pair;
                int f = Filter(p);
                bool e = Exact(p);
                if (f == TriangleTriangleFilter.Undecided) { und++; continue; }
                Assert.True((f == TriangleTriangleFilter.Intersecting) == e,
                    $"{what}: filter {f}, exact {e}, ids {p.Ia} {p.Ib}, A {p.A.V0} {p.A.V1} {p.A.V2}, B {p.B.V0} {p.B.V1} {p.B.V2}");
                // the convenience method gives the exact answer
                Assert.Equal(e, TriangleTriangleFilter.IntersectsBeyondShared(p.Ia, ref p.A, p.Ib, ref p.B));
                if (e) hit++; else sep++;
            }
            return (sep, hit, und);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AxisPlane_AlwaysDecided_AsTheExactTest(int axis)
        {
            List<Pair> pairs = PlanarPairs(30000, axis, 3.25, 11 + axis);
            var (sep, hit, und) = CheckAgainstExact(pairs, $"axis {axis}");
            Assert.Equal(0, und);
            // both outcomes in every sharing class
            for (int kind = 0; kind < 3; ++kind) {
                var k = pairs.Where((p, i) => i % 3 == kind).ToList();
                Assert.Contains(k, p => Exact(p));
                Assert.Contains(k, p => Exact(p) == false);
            }
            Assert.True(sep > 3000 && hit > 3000, $"{sep} separate, {hit} intersecting");
        }

        [Fact]
        public void AxisPlane_SwappedArguments_SameAnswer()
        {
            foreach (Pair p in PlanarPairs(6000, 2, 0, 5)) {
                Pair q = new Pair { Ia = p.Ib, Ib = p.Ia, A = p.B, B = p.A };
                Assert.Equal(Filter(p), Filter(q));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NearlyCoplanar_DecidedPairsAgree_MostSeparatePairsDecided(bool integer)
        {
            // the planar pairs with the z of every vertex moved by a tiny amount: no longer in an axis plane, so the
            // exact test needs its slow stage; the filter decides by the projection
            var rand = new Random(3);
            var pairs = PlanarPairs(30000, 2, 0, 7, integer).Select(p => {
                // the same id gets the same offset (shared vertices stay shared)
                var q = p;
                var offsets = new double[6];
                for (int i = 0; i < 6; ++i) offsets[i] = 1e-9 * (rand.NextDouble() - 0.5);
                Func<Vector3d, int, Vector3d> lift = (v, id) => new Vector3d(v.x, v.y, offsets[id]);
                q.A = new Triangle3d(lift(p.A.V0, p.Ia.a), lift(p.A.V1, p.Ia.b), lift(p.A.V2, p.Ia.c));
                q.B = new Triangle3d(lift(p.B.V0, p.Ib.a), lift(p.B.V1, p.Ib.b), lift(p.B.V2, p.Ib.c));
                return q;
            }).ToList();
            var (sep, hit, und) = CheckAgainstExact(pairs, "nearly coplanar");
            int exactSeparate = pairs.Count(p => Exact(p) == false);
            // (the filter never claims an intersection outside an axis plane)
            Assert.Equal(0, hit);
            // (many of these random pairs overlap in the projection, one just above the other, or touch there: only
            // the exact test separates them. Mesh neighbours do not overlap: TiltedPlate_NeighbourPairs_AlmostAllDecided)
            Assert.True(sep > 0.3 * exactSeparate, $"{sep} of {exactSeparate} separate pairs decided, {und} undecided");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TiltedPlate_NeighbourPairs_AlmostAllDecided(int tilt)
        {
            // the pairs with touching or overlapping boxes, the candidates of the self-intersection checks
            DMesh3 mesh = TiltedJitterPlate(20, 4, tilt);
            var pairs = new List<Pair>();
            int[] ids = mesh.TriangleIndices().ToArray();
            foreach (int i in ids)
                foreach (int j in ids) {
                    if (j <= i) continue;
                    Index3i ia = mesh.GetTriangle(i), ib = mesh.GetTriangle(j);
                    var p = new Pair { Ia = ia, Ib = ib, A = new Triangle3d(mesh.GetVertex(ia.a), mesh.GetVertex(ia.b), mesh.GetVertex(ia.c)),
                        B = new Triangle3d(mesh.GetVertex(ib.a), mesh.GetVertex(ib.b), mesh.GetVertex(ib.c)) };
                    AxisAlignedBox3d ba = new AxisAlignedBox3d(p.A.V0), bb = new AxisAlignedBox3d(p.B.V0);
                    ba.Contain(p.A.V1); ba.Contain(p.A.V2); bb.Contain(p.B.V1); bb.Contain(p.B.V2);
                    if (ba.Min.x <= bb.Max.x && bb.Min.x <= ba.Max.x && ba.Min.y <= bb.Max.y && bb.Min.y <= ba.Max.y && ba.Min.z <= bb.Max.z && bb.Min.z <= ba.Max.z)
                        pairs.Add(p);
                }
            var (sep, hit, und) = CheckAgainstExact(pairs, "tilted plate");
            Assert.Equal(0, hit);
            Assert.True(pairs.Count > 4000 && und < 0.01 * pairs.Count, $"{pairs.Count} pairs: {sep} separate, {und} undecided");
        }

        // coplanar pairs at a shared vertex and at a shared edge where A is a sliver (its third corner within 2^-20..2^-57 of
        // the segment of the other two, so its floating-point normal is often 0): the class ExactTriangleIntersection got
        // wrong before #249, in the axis planes and in the plane y = 2 z
        static List<Pair> SliverPairs(int count, int seed, out int zeroNormal)
        {
            var rnd = new Random(seed);
            var pairs = new List<Pair>();
            zeroNormal = 0;
            for (int i = 0; pairs.Count < count; ++i) {
                int plane = i % 4;
                double c = (i % 2 == 0) ? 0 : 4 * rnd.NextDouble() - 2;
                Func<double, double, Vector3d> lift = (u, v) => plane == 0 ? new Vector3d(c, u, v) : plane == 1 ? new Vector3d(v, c, u)
                    : plane == 2 ? new Vector3d(u, v, c) : new Vector3d(u, 2 * v, v);
                double su = 2 * rnd.NextDouble() - 1, sv = 2 * rnd.NextDouble() - 1, angle = 2 * Math.PI * rnd.NextDouble();
                double len = 0.05 + 0.3 * rnd.NextDouble(), t = 0.2 + 0.6 * rnd.NextDouble();
                double h = Math.Pow(2, -rnd.Next(20, 58)) * (rnd.Next(2) == 0 ? 1 : -1);
                double du = Math.Cos(angle), dv = Math.Sin(angle);
                Vector3d s = lift(su, sv), p = lift(su + len * du, sv + len * dv), q = lift(su + t * len * du - h * dv, sv + t * len * dv + h * du);
                double off = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(10, 50)) : 0.5 * rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
                double open = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(20, 50)) : 0.05 + rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
                double a1 = angle + off, a2 = a1 + open, l1 = 0.05 + 0.3 * rnd.NextDouble(), l2 = 0.05 + 0.3 * rnd.NextDouble();
                Vector3d u3 = lift(su + l1 * Math.Cos(a1), sv + l1 * Math.Sin(a1)), w3 = lift(su + l2 * Math.Cos(a2), sv + l2 * Math.Sin(a2));
                var pair = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(s, p, q) };
                if (i % 2 == 0) { pair.Ib = new Index3i(3, 0, 4); pair.B = new Triangle3d(u3, s, w3); }     // shared vertex s
                else { pair.Ib = new Index3i(1, 0, 3); pair.B = new Triangle3d(p, s, u3); }                // shared edge s-p
                if (ExactTriangleIntersection.IsDegenerate(ref pair.A) || ExactTriangleIntersection.IsDegenerate(ref pair.B))
                    continue;
                if ((p - s).Cross(q - s) == Vector3d.Zero) zeroNormal++;
                pairs.Add(pair);
            }
            return pairs;
        }

        [Fact]
        public void Slivers_AgreeWithTheExactTest()
        {
            // since #249 the exact test is right on these too: the filter and it agree everywhere
            int zeroNormal;
            List<Pair> pairs = SliverPairs(40000, 31, out zeroNormal);
            var (sep, hit, und) = CheckAgainstExact(pairs, "slivers");
            Assert.True(zeroNormal > 150, $"zero floating-point normal {zeroNormal}");
            Assert.True(sep > 2000 && hit > 2000, $"{sep} separate, {hit} intersecting, {und} undecided");
        }

        [Fact]
        public void General3D_DecidedPairsAgree()
        {
            var rand = new Random(9);
            Func<Vector3d> point = () => new Vector3d(rand.Next(0, 5), rand.Next(0, 5), rand.Next(0, 5));
            var pairs = new List<Pair>();
            while (pairs.Count < 30000) {
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(point(), point(), point()) };
                int kind = pairs.Count % 3;
                if (kind == 0) { p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(point(), point(), point()); }
                else if (kind == 1) { p.Ib = new Index3i(4, 0, 5); p.B = new Triangle3d(point(), p.A.V0, point()); }
                else { p.Ib = new Index3i(2, 1, 5); p.B = new Triangle3d(p.A.V2, p.A.V1, point()); }
                if (ExactTriangleIntersection.IsDegenerate(ref p.A) || ExactTriangleIntersection.IsDegenerate(ref p.B))
                    continue;
                pairs.Add(p);
            }
            var (sep, hit, und) = CheckAgainstExact(pairs, "general");
            Assert.True(sep > 1000 && hit > 100, $"{sep} separate, {hit} intersecting, {und} undecided");
        }

        [Fact]
        public void NonFinite_Undecided()
        {
            var p = PlanarPairs(1, 2, 0, 1)[0];
            p.A.V1 = new Vector3d(double.NaN, 0, 0);
            Assert.Equal(TriangleTriangleFilter.Undecided, Filter(p));
            p.A.V1 = new Vector3d(double.PositiveInfinity, 0, 0);
            Assert.Equal(TriangleTriangleFilter.Undecided, Filter(p));
        }

        [Fact]
        public void EveryFrame_DecidedPairsAgree_ExactScalesSameAnswer()
        {
            foreach (int axis in new[] { 0, 2 }) {
                List<Pair> pairs = PlanarPairs(9000, axis, 1.5, 21 + axis);
                int[] reference = pairs.Select(Filter).ToArray();
                foreach (Similarity s in Similarity.All()) {
                    var moved = pairs.Select(p => Transformed(p, s.Apply)).ToList();
                    var (sep, hit, und) = CheckAgainstExact(moved, s.Name);
                    Assert.True(sep > 1000, $"{s.Name}: {sep} separate, {und} undecided");
                    if (s.IsExact)
                        Assert.Equal(reference, moved.Select(Filter).ToArray());
                }
            }
        }

        // ---- in the Reducer ----

        // rotations of the plate (normal z) out of the axis planes: normal mostly z, mostly x, mostly y, and in the
        // plane x = 0 (a wall along the x axis: the projection must not drop y or z)
        static readonly Quaterniond[] Tilts = {
            new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37), new Quaterniond(new Vector3d(0.1, 1, 0.2).Normalized, 75),
            new Quaterniond(new Vector3d(1, 0.1, 0.2).Normalized, 75), new Quaterniond(Vector3d.AxisX, 45) };

        static DMesh3 TiltedJitterPlate(int n, int seed, int tilt = 0)
        {
            var rand = new Random(seed);
            var q = Tilts[tilt];
            var mesh = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = i, y = j;
                    if (i > 0 && j > 0 && i < n && j < n) { x += 0.3 * (2 * rand.NextDouble() - 1); y += 0.3 * (2 * rand.NextDouble() - 1); }
                    mesh.AppendVertex(q * new Vector3d(x, y, 0));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = i + j * (n + 1), b = a + 1, c = a + n + 2, d = a + n + 1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        [Fact]
        public void Reducer_TiltedPlate_EveryFrame_NoIntersection()
        {
            // nearly coplanar neighbours everywhere (the filter's main case outside axis planes)
            DMesh3 source = TiltedJitterPlate(14, 2);
            DMesh3 reference = new DMesh3(source);
            new Reducer(reference) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(source, s);
                new Reducer(mesh) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
                Assert.True(mesh.TriangleCount <= 60, $"{s.Name}: {mesh.TriangleCount}");
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly), s.Name);
                Assert.True(Invariance.AllFinite(mesh), s.Name);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(mesh, s)));
            }
        }
    }
}
