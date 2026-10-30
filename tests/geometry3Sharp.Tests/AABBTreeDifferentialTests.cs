using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DMeshAABBTree3 against linear scans, the empty-mesh build, and the rejection of a stale or unbuilt tree argument.
    /// The scans use the closed forms in Scan below, not the tree's leaf primitives (Dist*, Intr*, MeshQueries).
    /// Limit of that independence: the closed forms have no tolerances, so they only agree with the leaf predicates
    /// in general position. The tree-pair fixtures are random, so no triangle pair is coplanar or touches; coplanar
    /// and exact-contact behaviour is pinned only by the hand-made cases at the end, not by a scan.
    /// </summary>
    public class AABBTreeDifferentialTests
    {
        const DMeshAABBTree3.BuildStrategy TopDownMidpoint = DMeshAABBTree3.BuildStrategy.TopDownMidpoint;
        const DMeshAABBTree3.BuildStrategy TopDownMedian = DMeshAABBTree3.BuildStrategy.TopDownMedian;
        const DMeshAABBTree3.BuildStrategy BottomUp = DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings;

        public static IEnumerable<object[]> BuildCases()
        {
            yield return new object[] { DMeshAABBTree3.BuildStrategy.Default, DMeshAABBTree3.ClusterPolicy.Default, 4 };
            yield return new object[] { TopDownMidpoint, DMeshAABBTree3.ClusterPolicy.Default, 2 };
            yield return new object[] { TopDownMidpoint, DMeshAABBTree3.ClusterPolicy.Default, 1000 };     // the root is a leaf
            yield return new object[] { TopDownMedian, DMeshAABBTree3.ClusterPolicy.Default, 4 };
            foreach (var policy in new[] { DMeshAABBTree3.ClusterPolicy.Default, DMeshAABBTree3.ClusterPolicy.Fastest,
                DMeshAABBTree3.ClusterPolicy.FastVolumeMetric, DMeshAABBTree3.ClusterPolicy.MinimalVolume })
                yield return new object[] { BottomUp, policy, 4 };
        }

        static DMeshAABBTree3 Build(DMesh3 mesh, DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            var tree = new DMeshAABBTree3(mesh) { TopDownLeafMaxTriCount = leafSize };
            tree.Build(strategy, policy);
            return tree;
        }

        // ---- procedural fixtures ----

        static void AddTriangle(DMesh3 mesh, Vector3d a, Vector3d b, Vector3d c)
        {
            Assert.True(mesh.AppendTriangle(mesh.AppendVertex(a), mesh.AppendVertex(b), mesh.AppendVertex(c)) >= 0);
        }

        static Vector3d RandomVector(Random r, double size)
        {
            return new Vector3d((2 * r.NextDouble() - 1) * size, (2 * r.NextDouble() - 1) * size, (2 * r.NextDouble() - 1) * size);
        }

        // Unconnected triangles in general position; removeEvery > 0 leaves holes in the id space.
        static DMesh3 Soup(int seed, int count, Vector3d center, int removeEvery = 0)
        {
            var r = new Random(seed);
            var mesh = new DMesh3();
            for (int i = 0; i < count; ++i) {
                Vector3d a = center + RandomVector(r, 4);
                AddTriangle(mesh, a, a + RandomVector(r, 2), a + RandomVector(r, 2));
            }
            for (int t = removeEvery - 1; removeEvery > 0 && t < count; t += removeEvery)
                Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(t));
            return mesh;
        }

        // Parallel triangles with an odd count (bottom-up builds duplicate the last box of a layer).
        static DMesh3 OddStack()
        {
            var mesh = new DMesh3();
            for (int z = 0; z < 5; ++z)
                AddTriangle(mesh, new Vector3d(0, 0, z), new Vector3d(2, 0, z), new Vector3d(0, 2, z));
            return mesh;
        }

        // All centroids coincide: the midpoint split has nothing to separate them by.
        static DMesh3 EqualCentroids()
        {
            var mesh = new DMesh3();
            for (int k = 1; k <= 5; ++k)
                AddTriangle(mesh, new Vector3d(-k, -k, 0), new Vector3d(k, 0, 0), new Vector3d(0, k, 0));
            return mesh;
        }

        // Connected, so that bottom-up builds have real one-rings.
        static DMesh3 Grid()
        {
            return new GriddedRectGenerator() { Width = 6, Height = 6, EdgeVertices = 5 }.Generate().MakeDMesh();
        }

        static Triangle3d Tri(DMesh3 mesh, int t, Func<Vector3d, Vector3d> transform = null)
        {
            var tri = new Triangle3d();
            mesh.GetTriVertices(t, ref tri.V0, ref tri.V1, ref tri.V2);
            return transform == null ? tri : new Triangle3d(transform(tri.V0), transform(tri.V1), transform(tri.V2));
        }

        // Not on an edge of any EqualCentroids triangle: with round weights, an aim point there lies exactly on the
        // edge of an inner triangle, where the scan and the leaf predicate may each decide either way.
        static Vector3d InteriorPoint(Triangle3d t)
        {
            return 0.47 * t.V0 + 0.31 * t.V1 + 0.22 * t.V2;
        }

        static int[] LiveTriangles(DMeshAABBTree3 tree)
        {
            return tree.Mesh.TriangleIndices().Where(t => tree.TriangleFilterF == null || tree.TriangleFilterF(t)).ToArray();
        }

        // ---- the oracle: closed forms on Vector3d arithmetic only ----

        static class Scan
        {
            static bool InsidePrism(Vector3d p, Triangle3d t, Vector3d n)
            {
                return n.Dot((t.V1 - t.V0).Cross(p - t.V0)) >= 0 && n.Dot((t.V2 - t.V1).Cross(p - t.V1)) >= 0
                    && n.Dot((t.V0 - t.V2).Cross(p - t.V2)) >= 0;
            }

            static double PointSegmentSqr(Vector3d p, Vector3d a, Vector3d b)
            {
                Vector3d d = b - a;
                double s = MathUtil.Clamp((p - a).Dot(d) / d.Dot(d), 0, 1);
                return (a + s * d - p).LengthSquared;
            }

            public static double PointTriangleSqr(Vector3d p, Triangle3d t)
            {
                Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
                if (InsidePrism(p, t, n)) {
                    double h = (p - t.V0).Dot(n);
                    return h / n.Dot(n) * h;
                }
                return Math.Min(PointSegmentSqr(p, t.V0, t.V1), Math.Min(PointSegmentSqr(p, t.V1, t.V2), PointSegmentSqr(p, t.V2, t.V0)));
            }

            static double SegmentSegmentSqr(Vector3d p1, Vector3d q1, Vector3d p2, Vector3d q2)
            {
                Vector3d d1 = q1 - p1, d2 = q2 - p2, w = p1 - p2;
                double a = d1.Dot(d1), e = d2.Dot(d2), b = d1.Dot(d2), c = d1.Dot(w), f = d2.Dot(w), denom = a * e - b * b;
                double s = denom > 0 ? MathUtil.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                double u = (b * s + f) / e;
                if (u < 0) { u = 0; s = MathUtil.Clamp(-c / a, 0, 1); }
                else if (u > 1) { u = 1; s = MathUtil.Clamp((b - c) / a, 0, 1); }
                return (p1 + s * d1 - p2 - u * d2).LengthSquared;
            }

            static bool SegmentPierces(Vector3d p, Vector3d q, Triangle3d t)
            {
                Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
                double dp = (p - t.V0).Dot(n), dq = (q - t.V0).Dot(n);
                return (dp < 0) != (dq < 0) && InsidePrism(p + (dp / (dp - dq)) * (q - p), t, n);
            }

            /// <summary> Two non-coplanar triangles cross if and only if an edge of one pierces the other. </summary>
            public static bool Cross(Triangle3d a, Triangle3d b)
            {
                return SegmentPierces(a.V0, a.V1, b) || SegmentPierces(a.V1, a.V2, b) || SegmentPierces(a.V2, a.V0, b)
                    || SegmentPierces(b.V0, b.V1, a) || SegmentPierces(b.V1, b.V2, a) || SegmentPierces(b.V2, b.V0, a);
            }

            /// <summary> 0 if they cross, else the closest pair is at a vertex or between two edges. </summary>
            public static double TriangleTriangleSqr(Triangle3d a, Triangle3d b)
            {
                if (Cross(a, b)) return 0;
                double best = double.MaxValue;
                for (int i = 0; i < 3; ++i) {
                    best = Math.Min(best, Math.Min(PointTriangleSqr(a[i], b), PointTriangleSqr(b[i], a)));
                    for (int j = 0; j < 3; ++j)
                        best = Math.Min(best, SegmentSegmentSqr(a[i], a[(i + 1) % 3], b[j], b[(j + 1) % 3]));
                }
                return best;
            }

            /// <summary> Ray parameter of the hit (either face), or -1. </summary>
            public static double RayT(Ray3d ray, Triangle3d t)
            {
                Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
                double rate = ray.Direction.Dot(n);
                if (rate == 0) return -1;
                double hit = (t.V0 - ray.Origin).Dot(n) / rate;
                return hit >= 0 && InsidePrism(ray.Origin + hit * ray.Direction, t, n) ? hit : -1;
            }
        }

        static void AssertClose(double expected, double actual, string what)
        {
            Assert.True(Math.Abs(expected - actual) <= 1e-9 * (1 + Math.Abs(expected)), $"{what}: expected {expected:R}, got {actual:R}");
        }

        // ---- differential: point and ray queries ----

        static void CheckNearest(DMeshAABBTree3 tree, int[] live, Vector3d p)
        {
            DMesh3 mesh = tree.Mesh;
            var sqr = live.ToDictionary(t => t, t => Scan.PointTriangleSqr(p, Tri(mesh, t)));
            double best = sqr.Values.Min(), distance = Math.Sqrt(best);
            int id = tree.FindNearestTriangle(p, out double squared);
            Assert.Contains(id, live);
            AssertClose(best, sqr[id], "distance of the returned triangle");     // ties: any minimizer is fine
            AssertClose(best, squared, "reported squared distance");
            Assert.Equal(id, tree.FindNearestTriangle(p));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(p, 0.5 * distance));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(p, out double missed, 0.5 * distance));
            Assert.Equal(0.5 * distance * (0.5 * distance), missed);
            Assert.Contains(tree.FindNearestTriangle(p, 1.5 * distance), live);

            var vertices = live.SelectMany(t => { Index3i v = mesh.GetTriangle(t); return new[] { v.a, v.b, v.c }; }).Distinct().ToArray();
            double bestVertex = vertices.Min(v => mesh.GetVertex(v).DistanceSquared(p));
            int vid = tree.FindNearestVertex(p);
            Assert.Contains(vid, vertices);
            AssertClose(bestVertex, mesh.GetVertex(vid).DistanceSquared(p), "distance of the returned vertex");
        }

        static int CheckRay(DMeshAABBTree3 tree, int[] live, Ray3d ray)
        {
            var hits = new Dictionary<int, double>();
            foreach (int t in live) {
                double hit = Scan.RayT(ray, Tri(tree.Mesh, t));
                if (hit >= 0) hits.Add(t, hit);
            }
            var list = new List<int> { -17 };
            Assert.Equal(hits.Count, tree.FindAllHitTriangles(ray, list));
            Assert.Equal(-17, list[0]);
            Assert.Equal(hits.Keys.OrderBy(t => t), list.Skip(1).OrderBy(t => t));
            Assert.Equal(hits.Count, tree.FindAllHitTriangles(ray));
            int nearest = tree.FindNearestHitTriangle(ray);
            if (hits.Count == 0) {
                Assert.Equal(DMesh3.InvalidID, nearest);
                return 0;
            }
            double first = hits.Values.Min();
            Assert.Contains(nearest, hits.Keys);
            AssertClose(first, hits[nearest], "ray parameter of the returned triangle");
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(ray, 0.5 * first));
            Assert.Equal(0, tree.FindAllHitTriangles(ray, null, 0.5 * first));
            Assert.Contains(tree.FindNearestHitTriangle(ray, 1.5 * first), hits.Keys);
            double far = hits.Values.Max() + 1;
            Assert.Equal(hits.Count, tree.FindAllHitTriangles(ray, null, far));
            return hits.Count;
        }

        [Theory]
        [MemberData(nameof(BuildCases))]
        public void PointAndRayQueries_MatchLinearScan(DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            int hitRays = 0, missRays = 0;
            foreach (DMesh3 mesh in new[] { Soup(11, 60, Vector3d.Zero, 3), OddStack(), EqualCentroids(), Grid() }) {
                DMeshAABBTree3 tree = Build(mesh, strategy, policy, leafSize);
                foreach (Func<int, bool> filter in new Func<int, bool>[] { null, t => t % 2 == 1 }) {
                    tree.TriangleFilterF = filter;
                    int[] live = LiveTriangles(tree);
                    var r = new Random(23);
                    for (int q = 0; q < 24; ++q) {
                        Vector3d p = RandomVector(r, 8) + new Vector3d(0, 0, 0.125);
                        CheckNearest(tree, live, p);
                        Triangle3d target = Tri(mesh, live[r.Next(live.Length)]);
                        Vector3d aim = InteriorPoint(target);
                        hitRays += CheckRay(tree, live, new Ray3d(p, (aim - p).Normalized));
                        if (CheckRay(tree, live, new Ray3d(p + 100 * (p - aim).Normalized, (p - aim).Normalized)) == 0)
                            missRays++;
                    }
                }
            }
            Assert.True(hitRays > 0 && missRays > 0, $"{hitRays} hits, {missRays} misses");
        }

        // ---- differential: tree pairs ----

        static int CheckPair(DMeshAABBTree3 a, DMeshAABBTree3 b, Func<Vector3d, Vector3d> transform)
        {
            var sqr = new Dictionary<(int, int), double>();
            foreach (int ta in LiveTriangles(a))
                foreach (int tb in LiveTriangles(b))
                    sqr.Add((ta, tb), Scan.TriangleTriangleSqr(Tri(a.Mesh, ta), Tri(b.Mesh, tb, transform)));
            var crossing = sqr.Where(kv => kv.Value == 0).Select(kv => kv.Key).OrderBy(k => k).ToArray();
            double best = sqr.Values.Min(), distance = Math.Sqrt(best);

            Assert.Equal(crossing.Length > 0, a.TestIntersection(b, transform));

            var all = a.FindAllIntersections(b, transform);
            Assert.Empty(all.Points);       // general position: every intersection is a segment
            Assert.Equal(crossing, all.Segments.Select(s => (s.t0, s.t1)).OrderBy(k => k));
            foreach (var s in all.Segments)
                foreach (Vector3d p in new[] { s.point0, s.point1 }) {
                    AssertClose(0, Scan.PointTriangleSqr(p, Tri(a.Mesh, s.t0)), "segment point on our triangle");
                    AssertClose(0, Scan.PointTriangleSqr(p, Tri(b.Mesh, s.t1, transform)), "segment point on the other triangle");
                }

            Index2i pair = a.FindNearestTriangles(b, transform, out double reported);
            AssertClose(best, sqr[(pair.a, pair.b)], "distance of the returned pair");
            AssertClose(distance, reported, "reported distance");
            if (distance > 0) {
                Assert.Equal(Index2i.Max, a.FindNearestTriangles(b, transform, out double missed, 0.5 * distance));
                AssertClose(0.5 * distance, missed, "distance reported for a miss");
                Assert.True(sqr.ContainsKey(ToTuple(a.FindNearestTriangles(b, transform, out double _, 1.5 * distance))));
            }
            return crossing.Length;
        }

        static (int, int) ToTuple(Index2i i) { return (i.a, i.b); }

        [Theory]
        [MemberData(nameof(BuildCases))]
        public void TreePairQueries_MatchLinearScan(DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            Matrix3d rotation = Matrix3d.AxisAngleD(Vector3d.AxisY, 40);
            Vector3d shift = new Vector3d(0.25, -0.5, 0.125);
            Func<Vector3d, Vector3d> rigid = p => rotation * p + shift;
            var pairs = new[] {
                Tuple.Create(Soup(1, 40, Vector3d.Zero), Soup(2, 40, Vector3d.Zero), (Func<Vector3d, Vector3d>)null, true),
                Tuple.Create(Soup(3, 24, Vector3d.Zero, 3), Soup(4, 24, Vector3d.Zero, 4), (Func<Vector3d, Vector3d>)null, true),
                Tuple.Create(Soup(5, 24, Vector3d.Zero), Soup(6, 24, Vector3d.Zero), rigid, true),
                Tuple.Create(Soup(7, 30, Vector3d.Zero), Grid(), (Func<Vector3d, Vector3d>)null, true),
                Tuple.Create(Soup(8, 24, Vector3d.Zero), Soup(9, 24, new Vector3d(14, 0, 0)), (Func<Vector3d, Vector3d>)null, false),
                Tuple.Create(Soup(10, 24, Vector3d.Zero), Soup(12, 24, new Vector3d(14, 0, 0)), rigid, false),
            };
            foreach (var p in pairs) {
                DMeshAABBTree3 a = Build(p.Item1, strategy, policy, leafSize), b = Build(p.Item2, strategy, policy, leafSize);
                Assert.Equal(p.Item4, CheckPair(a, b, p.Item3) > 0);
                a.TriangleFilterF = t => t % 2 == 1;
                CheckPair(a, b, p.Item3);
                b.TriangleFilterF = t => t % 3 != 1;
                CheckPair(a, b, p.Item3);
                a.TriangleFilterF = null;
                CheckPair(a, b, p.Item3);
            }
        }

        // ---- invariance ----

        // Every result of one run, in input units: ids, booleans and pairs as discrete values, lengths apart.
        static void RunQueries(DMesh3 mesh, DMesh3 other, Func<Vector3d, Vector3d> transform, Similarity s, DMeshAABBTree3.BuildStrategy strategy,
            List<string> discrete, List<double> lengths)
        {
            DMeshAABBTree3 tree = Build(mesh, strategy, DMeshAABBTree3.ClusterPolicy.Default, 4);
            DMeshAABBTree3 otherTree = Build(other, strategy, DMeshAABBTree3.ClusterPolicy.Default, 4);
            var r = new Random(5);
            for (int q = 0; q < 16; ++q) {
                Vector3d p = RandomVector(r, 8);
                Triangle3d target = Tri(mesh, q, x => s.Inverse(x));
                Vector3d aim = InteriorPoint(target);
                Vector3d mapped = s.Apply(p);
                discrete.Add("nearest " + tree.FindNearestTriangle(mapped, out double squared));
                lengths.Add(squared / (s.Scale * s.Scale));
                discrete.Add("vertex " + tree.FindNearestVertex(mapped));
                discrete.Add("cut " + tree.FindNearestTriangle(mapped, s.ApplyLength(0.25)));
                var ray = new Ray3d(mapped, s.ApplyDirection((aim - p).Normalized));
                discrete.Add("hit " + tree.FindNearestHitTriangle(ray));
                var hits = new List<int>();
                tree.FindAllHitTriangles(ray, hits);
                discrete.Add("hits " + string.Join(",", hits.OrderBy(t => t)));
                discrete.Add("tri " + tree.TestIntersection(new Triangle3d(s.Apply(p), s.Apply(p + 1.5 * (aim - p)), s.Apply(p + Vector3d.AxisY))));
            }
            discrete.Add("any " + tree.TestIntersection(otherTree, transform));
            var all = tree.FindAllIntersections(otherTree, transform);
            discrete.Add("points " + all.Points.Count);
            foreach (var seg in all.Segments.OrderBy(x => x.t0).ThenBy(x => x.t1)) {
                discrete.Add("segment " + seg.t0 + "," + seg.t1);
                // the endpoints as midpoint and length, which do not depend on their order
                Vector3d mid = s.Inverse(0.5 * seg.point0 + 0.5 * seg.point1);
                lengths.AddRange(new[] { mid.x, mid.y, mid.z, seg.point0.Distance(seg.point1) / s.Scale });
            }
            Index2i pair = tree.FindNearestTriangles(otherTree, transform, out double distance);
            // crossing meshes have many pairs at distance 0, and which one is found depends on the boxes
            discrete.Add(all.Segments.Count > 0 ? "pair at contact" : "pair " + pair);
            lengths.Add(distance / s.Scale);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void AllQueries_AllSimilarities_MapBackToTheBaseline(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 mesh = Soup(21, 40, Vector3d.Zero);
            foreach (DMesh3 other in new[] { Soup(22, 30, Vector3d.Zero), Soup(23, 30, new Vector3d(14, 0, 0)) })
                foreach (var strategy in new[] { TopDownMidpoint, TopDownMedian, BottomUp }) {
                    var baseDiscrete = new List<string>();
                    var baseLengths = new List<double>();
                    RunQueries(mesh, other, null, Similarity.Identity, strategy, baseDiscrete, baseLengths);
                    Assert.Contains(baseDiscrete, d => d.StartsWith("hits ", StringComparison.Ordinal) && d != "hits ");
                    DMesh3 moved = Invariance.Transformed(mesh, s);
                    // both meshes moved, then only ours with TransformF moving the other one into place
                    foreach (bool useTransform in new[] { false, true }) {
                        var discrete = new List<string>();
                        var lengths = new List<double>();
                        RunQueries(moved, useTransform ? other : Invariance.Transformed(other, s), useTransform ? s.Apply : (Func<Vector3d, Vector3d>)null,
                            s, strategy, discrete, lengths);
                        Assert.Equal(baseDiscrete, discrete);
                        Assert.Equal(baseLengths.Count, lengths.Count);
                        for (int i = 0; i < lengths.Count; ++i) {
                            if (s.IsExact)
                                Assert.Equal(BitConverter.DoubleToInt64Bits(baseLengths[i]), BitConverter.DoubleToInt64Bits(lengths[i]));
                            else
                                Assert.True(Math.Abs(baseLengths[i] - lengths[i]) <= s.DistanceResolution(32), $"length {i}: {baseLengths[i]:R} vs {lengths[i]:R}");
                        }
                    }
                }
        }

        // ---- the fix: empty meshes ----

        static void AssertNoHits(DMeshAABBTree3 tree)
        {
            Vector3d point = new Vector3d(0, 0, 1);
            var ray = new Ray3d(point, -Vector3d.AxisZ);
            Assert.True(tree.IsValid);
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(point));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(point, out double unlimited));
            Assert.Equal(double.MaxValue, unlimited);
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(point, out double limited, 3));
            Assert.Equal(9, limited);
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestVertex(point));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(ray));
            var hits = new List<int> { -17 };
            Assert.Equal(0, tree.FindAllHitTriangles(ray, hits));
            Assert.Equal(new[] { -17 }, hits);
            Assert.False(tree.TestIntersection(new Triangle3d(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY)));
            Assert.False(tree.IsInside(point));
            Assert.Equal(0, tree.WindingNumber(point));
            Assert.Equal(0, tree.FastWindingNumber(point));
        }

        static void AssertNoPairResults(DMeshAABBTree3 a, DMeshAABBTree3 b)
        {
            Assert.False(a.TestIntersection(b));
            var all = a.FindAllIntersections(b);
            Assert.Empty(all.Points);
            Assert.Empty(all.Segments);
            Assert.Equal(Index2i.Max, a.FindNearestTriangles(b, null, out double distance));
            Assert.Equal(double.MaxValue, distance);
        }

        [Theory]
        [MemberData(nameof(BuildCases))]
        public void EmptyMesh_BuildsAndQueriesReturnNoHit(DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            DMeshAABBTree3 empty = Build(new DMesh3(), strategy, policy, leafSize);
            AssertNoHits(empty);
            DMeshAABBTree3 nonempty = Build(OddStack(), strategy, policy, leafSize);
            AssertNoPairResults(empty, nonempty);
            AssertNoPairResults(nonempty, empty);
            AssertNoPairResults(empty, empty);
        }

        [Theory]
        [MemberData(nameof(BuildCases))]
        public void Reuse_NonemptyThenEmptyThenNonempty_MatchesFreshTree(DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            DMesh3 mesh = OddStack();
            int[] ids = mesh.TriangleIndices().ToArray();
            DMeshAABBTree3 tree = Build(mesh, strategy, policy, leafSize);
            Vector3d point = new Vector3d(0.5, 0.5, 8);
            int before = tree.FindNearestTriangle(point);
            Assert.Equal(4, before);
            foreach (int t in ids)
                Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(t));
            tree.Build(strategy, policy);
            AssertNoHits(tree);
            AddTriangle(mesh, new Vector3d(0, 0, 2), new Vector3d(2, 0, 2), new Vector3d(0, 2, 2));
            tree.Build(strategy, policy);
            Assert.True(tree.IsValid);
            Assert.Equal(Build(mesh, strategy, policy, leafSize).FindNearestTriangle(point), tree.FindNearestTriangle(point));
            AssertClose(36, tree.FindNearestTriangle(point, out double squared) >= 0 ? squared : -1, "distance to the new triangle");
        }

        [Theory]
        [InlineData(-1, TopDownMidpoint)]
        [InlineData(0, TopDownMidpoint)]
        [InlineData(1, TopDownMidpoint)]
        [InlineData(-1, TopDownMedian)]
        [InlineData(0, TopDownMedian)]
        [InlineData(1, TopDownMedian)]
        [InlineData(0, BottomUp)]
        public void NonPositiveLeafSize_BuildsInsteadOfOverflowingTheStack(int leafSize, DMeshAABBTree3.BuildStrategy strategy)
        {
            AssertNoHits(Build(new DMesh3(), strategy, DMeshAABBTree3.ClusterPolicy.Default, leafSize));
            DMeshAABBTree3 tree = Build(OddStack(), strategy, DMeshAABBTree3.ClusterPolicy.Default, leafSize);
            Assert.Equal(4, tree.FindNearestTriangle(new Vector3d(0.5, 0.5, 8)));
        }

        // ---- the fix: stale or unbuilt tree argument ----

        static void RunPairQuery(string query, DMeshAABBTree3 receiver, DMeshAABBTree3 other, Func<Vector3d, Vector3d> transform)
        {
            switch (query) {
                case "any": receiver.TestIntersection(other, transform); break;
                case "all": receiver.FindAllIntersections(other, transform); break;
                case "nearest": receiver.FindNearestTriangles(other, transform, out double _); break;
                default: throw new ArgumentOutOfRangeException(nameof(query));
            }
        }

        static Vector3d NotCalled(Vector3d p)
        {
            Assert.Fail("a stale or unbuilt tree must be rejected before the transform is called");
            return p;
        }

        // The edited tree has triangles at z = 3 and z = 5, the other one a triangle at z = 0. Editing changes the
        // answer, so the check after the rebuild can tell a real rebuild from stale boxes.
        [Theory]
        [InlineData(false, "any", false)]
        [InlineData(false, "all", false)]
        [InlineData(false, "nearest", false)]
        [InlineData(false, "any", true)]
        [InlineData(false, "all", true)]
        [InlineData(false, "nearest", true)]
        [InlineData(true, "any", false)]
        [InlineData(true, "all", false)]
        [InlineData(true, "nearest", false)]
        public void TreePairQueries_RejectStaleTreeBeforeTraversal(bool editReceiver, string query, bool topologyEdit)
        {
            var stack = new DMesh3();
            AddTriangle(stack, new Vector3d(0, 0, 3), new Vector3d(2, 0, 3), new Vector3d(0, 2, 3));
            AddTriangle(stack, new Vector3d(0, 0, 5), new Vector3d(2, 0, 5), new Vector3d(0, 2, 5));
            var flat = new DMesh3();
            AddTriangle(flat, Vector3d.Zero, new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
            var edited = new DMeshAABBTree3(stack, true);
            var clean = new DMeshAABBTree3(flat, true);
            DMeshAABBTree3 receiver = editReceiver ? edited : clean, other = editReceiver ? clean : edited;

            if (topologyEdit) Assert.Equal(MeshResult.Ok, stack.RemoveTriangle(0));
            else stack.SetVertex(0, new Vector3d(0.5, 0.5, 0));
            Assert.False(edited.IsValid);
            Exception e = Assert.Throws<Exception>(() => RunPairQuery(query, receiver, other, NotCalled));
            Assert.Contains("modified", e.Message);
            if (!editReceiver) Assert.Contains("other mesh", e.Message);

            edited.Build();
            Assert.Equal(!topologyEdit, receiver.TestIntersection(other));
            Assert.True(receiver.IsValid && other.IsValid);
            receiver.FindNearestTriangles(other, null, out double distance);
            Assert.Equal(topologyEdit ? 5 : 0, distance);
        }

        [Theory]
        [InlineData("any", false)]
        [InlineData("all", false)]
        [InlineData("nearest", false)]
        [InlineData("any", true)]
        [InlineData("all", true)]
        [InlineData("nearest", true)]
        public void TreePairQueries_RejectUnbuiltOtherTreeWithClearMessage(string query, bool emptyMesh)
        {
            // a new empty mesh still has its first timestamp, so it passes the timestamp check of a tree never built
            var receiver = new DMeshAABBTree3(OddStack(), true);
            var other = new DMeshAABBTree3(emptyMesh ? new DMesh3() : OddStack());
            Exception e = Assert.Throws<Exception>(() => RunPairQuery(query, receiver, other, NotCalled));
            Assert.Contains("other tree has not been built", e.Message);
            other.Build();
            RunPairQuery(query, receiver, other, null);
        }

        // ---- hand-made contracts the scans cannot pin ----

        // The cutoff is strict in every query: at exactly fMaxDist there is no hit, also at contact with cutoff 0.
        [Fact]
        public void Cutoffs_AreStrict_AtTheExactDistanceThereIsNoHit()
        {
            var mesh = new DMesh3();
            AddTriangle(mesh, Vector3d.Zero, new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
            var tree = new DMeshAABBTree3(mesh, true);
            var above = new Vector3d(0, 0, 1);
            var ray = new Ray3d(above, -Vector3d.AxisZ);
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(above, 1));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(above, out double _, 1));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestVertex(above, 1));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(ray, 1));
            Assert.Equal(0, tree.FindAllHitTriangles(ray, null, 1));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(Vector3d.Zero, 0));

            double nextUp = 1 + MathUtil.Epsilon;
            Assert.Equal(0, tree.FindNearestTriangle(above, nextUp));
            Assert.Equal(0, tree.FindNearestTriangle(above, out double _, nextUp));
            Assert.NotEqual(DMesh3.InvalidID, tree.FindNearestVertex(above, nextUp));
            Assert.Equal(0, tree.FindNearestHitTriangle(ray, nextUp));
            Assert.Equal(1, tree.FindAllHitTriangles(ray, null, nextUp));

            var otherMesh = new DMesh3();
            AddTriangle(otherMesh, above, new Vector3d(2, 0, 1), new Vector3d(0, 2, 1));
            var other = new DMeshAABBTree3(otherMesh, true);
            Assert.Equal(Index2i.Max, tree.FindNearestTriangles(other, null, out double missed, 1));
            Assert.Equal(1, missed);
            Assert.Equal(new Index2i(0, 0), tree.FindNearestTriangles(other, null, out double found, nextUp));
            Assert.Equal(1, found);
        }
    }
}
