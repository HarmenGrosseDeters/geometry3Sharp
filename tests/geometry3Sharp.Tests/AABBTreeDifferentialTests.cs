using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class AABBTreeDifferentialTests
    {
        readonly ITestOutputHelper output;

        public AABBTreeDifferentialTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public static IEnumerable<object[]> EmptyCases()
        {
            var builds = new[] {
                Tuple.Create(DMeshAABBTree3.BuildStrategy.Default, DMeshAABBTree3.ClusterPolicy.Default),
                Tuple.Create(DMeshAABBTree3.BuildStrategy.TopDownMidpoint, DMeshAABBTree3.ClusterPolicy.Default),
                Tuple.Create(DMeshAABBTree3.BuildStrategy.TopDownMedian, DMeshAABBTree3.ClusterPolicy.Default),
                Tuple.Create(DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings, DMeshAABBTree3.ClusterPolicy.Default),
                Tuple.Create(DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings, DMeshAABBTree3.ClusterPolicy.Fastest),
                Tuple.Create(DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings, DMeshAABBTree3.ClusterPolicy.FastVolumeMetric),
                Tuple.Create(DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings, DMeshAABBTree3.ClusterPolicy.MinimalVolume)
            };
            foreach (var build in builds)
                foreach (string operation in new[] {
                    "nearest", "nearest-out", "nearest-hit", "all-null", "all-list", "triangle", "tree"
                })
                    foreach (double cutoff in new[] { 0.0, 1.0, double.MaxValue })
                        yield return new object[] { build.Item1, build.Item2, operation, cutoff };
        }

        // An empty linear scan has no admissible IDs, independently of the leaf predicates.
        // Keep this regression when the bottom-up empty-root defect receives its own fix brief.
        [Theory]
        [MemberData(nameof(EmptyCases))]
        public void EmptyMesh_QueriesAgreeWithEmptyScan(
            DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy,
            string operation, double cutoff)
        {
            string context = string.Format(CultureInfo.InvariantCulture,
                "seed=none; fixture=empty; query=0; strategy={0}; policy={1}; operation={2}; " +
                "cutoff={3:R}; vertices=[]; triangles=[]; point=(0,0,1); " +
                "ray.origin=(0,0,1); ray.direction=(0,0,-1); " +
                "testTriangle=[(0,0,0),(1,0,0),(0,1,0)]; prefix=[-17,-23]",
                strategy, policy, operation, cutoff);
            output.WriteLine(context);
            var mesh = new DMesh3();
            int timestamp = mesh.ShapeTimestamp;
            var tree = new DMeshAABBTree3(mesh);
            tree.Build(strategy, policy);
            Assert.True(tree.IsValid, context);
            var point = new Vector3d(0, 0, 1);
            var ray = new Ray3d(point, -Vector3d.AxisZ);
            var triangle = new Triangle3d(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY);

            switch (operation) {
                case "nearest":
                    int actualId = tree.FindNearestTriangle(point, cutoff);
                    Assert.True(actualId == DMesh3.InvalidID, context + "; actualId=" + actualId);
                    break;
                case "nearest-out":
                    int id = tree.FindNearestTriangle(point, out double distanceSquared, cutoff);
                    Assert.True(id == DMesh3.InvalidID, context + "; actualId=" + id);
                    double expected = cutoff < double.MaxValue ? cutoff * cutoff : double.MaxValue;
                    Assert.True(distanceSquared == expected, context + "; actualDistanceSquared=" +
                        distanceSquared.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case "nearest-hit":
                    Assert.True(tree.FindNearestHitTriangle(ray, cutoff) == DMesh3.InvalidID, context);
                    break;
                case "all-null":
                    Assert.True(tree.FindAllHitTriangles(ray, null, cutoff) == 0, context);
                    break;
                case "all-list":
                    var destination = new List<int> { -17, -23 };
                    Assert.True(tree.FindAllHitTriangles(ray, destination, cutoff) == 0, context);
                    Assert.True(destination.Count == 2 && destination[0] == -17 && destination[1] == -23,
                        context + "; actualDestination=[" + string.Join(",", destination) + "]");
                    break;
                case "triangle":
                    Assert.False(tree.TestIntersection(triangle), context);
                    break;
                case "tree":
                    var otherMesh = new DMesh3();
                    int a = otherMesh.AppendVertex(triangle.V0);
                    int b = otherMesh.AppendVertex(triangle.V1);
                    int c = otherMesh.AppendVertex(triangle.V2);
                    Assert.True(otherMesh.AppendTriangle(a, b, c) >= 0, context);
                    var other = new DMeshAABBTree3(otherMesh, true);
                    Assert.False(tree.TestIntersection(other), context);
                    Assert.False(other.TestIntersection(tree), context);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, context);
            }

            Assert.True(mesh.ShapeTimestamp == timestamp && mesh.VertexCount == 0 && mesh.TriangleCount == 0,
                context + "; read-only query changed the mesh");
        }

        public static IEnumerable<object[]> BuildCases()
        {
            foreach (var strategy in new[] { DMeshAABBTree3.BuildStrategy.Default,
                DMeshAABBTree3.BuildStrategy.TopDownMidpoint, DMeshAABBTree3.BuildStrategy.TopDownMedian })
                foreach (int leafSize in new[] { 2, 4, 129 })
                    yield return new object[] { strategy, DMeshAABBTree3.ClusterPolicy.Default, leafSize };
            foreach (var policy in new[] { DMeshAABBTree3.ClusterPolicy.Default,
                DMeshAABBTree3.ClusterPolicy.Fastest, DMeshAABBTree3.ClusterPolicy.FastVolumeMetric,
                DMeshAABBTree3.ClusterPolicy.MinimalVolume })
                yield return new object[] { DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings, policy, 4 };
        }

        public static IEnumerable<object[]> NamedCases()
        {
            foreach (object[] build in BuildCases())
                foreach (string fixture in new[] { "one", "grid", "box", "components", "equal-centroid",
                    "equal-centroid-vertex-ties", "sparse", "isolated", "slivers", "odd-stack" })
                    yield return new object[] { fixture, build[0], build[1], build[2] };
        }

        static DMeshAABBTree3 Build(DMesh3 mesh, DMeshAABBTree3.BuildStrategy strategy,
            DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            var tree = new DMeshAABBTree3(mesh) { TopDownLeafMaxTriCount = leafSize };
            tree.Build(strategy, policy);
            return tree;
        }

        static Triangle3d Triangle(DMesh3 mesh, int id)
        {
            var triangle = new Triangle3d();
            mesh.GetTriVertices(id, ref triangle.V0, ref triangle.V1, ref triangle.V2);
            return triangle;
        }

        static int AddTriangle(DMesh3 mesh, Triangle3d triangle)
        {
            int a = mesh.AppendVertex(triangle.V0), b = mesh.AppendVertex(triangle.V1),
                c = mesh.AppendVertex(triangle.V2);
            int id = mesh.AppendTriangle(a, b, c);
            Assert.True(id >= 0, "Procedural triangle insertion failed: " + TriText(triangle));
            return id;
        }

        static Triangle3d UnitTriangle(double z = 0)
        {
            return new Triangle3d(new Vector3d(0, 0, z), new Vector3d(2, 0, z), new Vector3d(0, 2, z));
        }

        static DMesh3 Fixture(string name)
        {
            var mesh = new DMesh3();
            switch (name) {
                case "one":
                    AddTriangle(mesh, UnitTriangle());
                    break;
                case "grid":
                case "isolated":
                    var vertices = new int[4, 4];
                    for (int y = 0; y < 4; ++y)
                        for (int x = 0; x < 4; ++x)
                            vertices[x, y] = mesh.AppendVertex(new Vector3d(x, y, 0));
                    for (int y = 0; y < 3; ++y)
                        for (int x = 0; x < 3; ++x) {
                            Assert.True(mesh.AppendTriangle(vertices[x, y], vertices[x + 1, y], vertices[x, y + 1]) >= 0);
                            Assert.True(mesh.AppendTriangle(vertices[x + 1, y], vertices[x + 1, y + 1], vertices[x, y + 1]) >= 0);
                        }
                    if (name == "isolated")
                        mesh.AppendVertex(new Vector3d(4096, -4096, 4096));
                    break;
                case "box":
                    var corners = new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0),
                        new Vector3d(2, 2, 0), new Vector3d(0, 2, 0), new Vector3d(0, 0, 2),
                        new Vector3d(2, 0, 2), new Vector3d(2, 2, 2), new Vector3d(0, 2, 2) };
                    foreach (Vector3d corner in corners) mesh.AppendVertex(corner);
                    int[,] faces = { { 0, 2, 1 }, { 0, 3, 2 }, { 4, 5, 6 }, { 4, 6, 7 },
                        { 0, 1, 5 }, { 0, 5, 4 }, { 1, 2, 6 }, { 1, 6, 5 },
                        { 2, 3, 7 }, { 2, 7, 6 }, { 3, 0, 4 }, { 3, 4, 7 } };
                    for (int i = 0; i < faces.GetLength(0); ++i)
                        Assert.True(mesh.AppendTriangle(faces[i, 0], faces[i, 1], faces[i, 2]) >= 0);
                    Assert.True(mesh.IsClosed());
                    break;
                case "components":
                    for (int i = 0; i < 7; ++i) {
                        Triangle3d triangle = UnitTriangle(i % 2);
                        Vector3d shift = new Vector3d(3 * i, 0, 0);
                        AddTriangle(mesh, new Triangle3d(triangle.V0 + shift, triangle.V1 + shift, triangle.V2 + shift));
                    }
                    break;
                case "equal-centroid":
                    for (int k = 1; k <= 5; ++k)
                        AddTriangle(mesh, new Triangle3d(new Vector3d(-k, -k, 0),
                            new Vector3d(k, 0, 0), new Vector3d(0, k, 0)));
                    break;
                case "equal-centroid-vertex-ties":
                    // A shared vertex gives exact distance ties without rounded 1/3 centroid barycentrics.
                    for (int k = 1; k <= 5; ++k)
                        AddTriangle(mesh, new Triangle3d(Vector3d.Zero,
                            new Vector3d(k, 0, 0), new Vector3d(-k, 3, 0)));
                    break;
                case "sparse":
                case "odd-stack":
                    for (int i = 0; i < (name == "sparse" ? 7 : 5); ++i)
                        AddTriangle(mesh, UnitTriangle(i));
                    if (name == "sparse") {
                        Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(1));
                        Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(3));
                        Assert.True(mesh.MaxTriangleID > mesh.TriangleCount);
                    }
                    break;
                case "slivers":
                    AddTriangle(mesh, new Triangle3d(Vector3d.Zero, new Vector3d(2, 0, 0),
                        new Vector3d(1, 1.0 / 4096, 0)));
                    AddTriangle(mesh, new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(2, 0, 1),
                        new Vector3d(1, 1.0 / 8192, 1)));
                    double size = 1.0 / 4096;
                    AddTriangle(mesh, new Triangle3d(new Vector3d(1, 1, 0),
                        new Vector3d(1 + size, 1, 0), new Vector3d(1, 1 + size, 0)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown fixture");
            }
            Assert.True(mesh.TriangleCount > 0 && mesh.TriangleCount <= 128);
            Assert.True(mesh.CheckValidity());
            foreach (int id in mesh.VertexIndices()) Assert.True(mesh.GetVertex(id).IsFinite);
            foreach (int id in mesh.TriangleIndices()) {
                Triangle3d triangle = Triangle(mesh, id);
                Assert.True((triangle.V1 - triangle.V0).Cross(triangle.V2 - triangle.V0).LengthSquared > 0,
                    name + "; nonzero-area fixture required; triangle=" + TriText(triangle));
            }
            return mesh;
        }

        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string VecText(Vector3d value) => "(" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + ")";
        static string TriText(Triangle3d triangle) => "[" + VecText(triangle.V0) + "," +
            VecText(triangle.V1) + "," + VecText(triangle.V2) + "]";

        static string MeshText(DMesh3 mesh)
        {
            var text = new StringBuilder();
            text.Append("timestamp=").Append(mesh.Timestamp).Append("; shapeTimestamp=").Append(mesh.ShapeTimestamp)
                .Append("; edges=").Append(mesh.EdgeCount).Append("; vertices=[");
            foreach (int id in mesh.VertexIndices().OrderBy(id => id))
                text.Append(id).Append(':').Append(VecText(mesh.GetVertex(id))).Append(';');
            text.Append("]; triangles=[");
            foreach (int id in mesh.TriangleIndices().OrderBy(id => id)) {
                Index3i triangle = mesh.GetTriangle(id);
                text.Append(id).Append(":(").Append(triangle.a).Append(',').Append(triangle.b)
                    .Append(',').Append(triangle.c).Append(");");
            }
            return text.Append(']').ToString();
        }

        string Context(DMesh3 mesh, string fixture, DMeshAABBTree3.BuildStrategy strategy,
            DMeshAABBTree3.ClusterPolicy policy, int leafSize, int seed = 0)
        {
            string context = "seed=" + seed + "; fixture=" + fixture + "; strategy=" + strategy +
                "; policy=" + policy + "; leafSize=" + leafSize + "; " + MeshText(mesh);
            output.WriteLine(context);
            return context;
        }

        static double CoordinateSize(Vector3d point, Triangle3d triangle)
        {
            double size = 0;
            foreach (Vector3d v in new[] { point, triangle.V0, triangle.V1, triangle.V2 })
                size = Math.Max(size, Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))));
            return Math.Max(size, 1e-100);
        }

        static void CheckClosest(DMesh3 mesh, int id, Vector3d point, double expectedSquared, string context)
        {
            Triangle3d triangle = Triangle(mesh, id);
            var distance = MeshQueries.TriangleDistance(mesh, id, point);
            Vector3d closest = distance.TriangleClosest, bary = distance.TriangleBaryCoords;
            double size = CoordinateSize(point, triangle);
            double coordinateTolerance = 128 * MathUtil.Epsilon * size;
            double squaredTolerance = 128 * MathUtil.Epsilon * size * size;
            Assert.True(closest.IsFinite && bary.IsFinite, context + "; nonfinite closest point/barycentrics");
            Assert.True(bary.x >= -128 * MathUtil.Epsilon && bary.y >= -128 * MathUtil.Epsilon &&
                bary.z >= -128 * MathUtil.Epsilon && Math.Abs(bary.x + bary.y + bary.z - 1) <= 128 * MathUtil.Epsilon,
                context + "; bary=" + VecText(bary));
            Vector3d reconstructed = bary.x * triangle.V0 + bary.y * triangle.V1 + bary.z * triangle.V2;
            Assert.True(reconstructed.Distance(closest) <= coordinateTolerance,
                context + "; closest=" + VecText(closest) + "; reconstructed=" + VecText(reconstructed));
            Assert.True(Math.Abs(closest.DistanceSquared(point) - expectedSquared) <= squaredTolerance,
                context + "; closest=" + VecText(closest) + "; expectedSquared=" + Number(expectedSquared));
            Assert.True(Math.Abs(distance.GetSquared() - expectedSquared) <= squaredTolerance, context);
        }

        // Both scans enumerate sorted live IDs only: no tree bounds, traversal or candidate lists.
        static List<int> PointScan(DMesh3 mesh, Vector3d point, double cutoff,
            Func<int, bool> filter, out double bestSquared)
        {
            bestSquared = cutoff < double.MaxValue ? cutoff * cutoff : double.MaxValue;
            var bestIds = new List<int>();
            foreach (int id in mesh.TriangleIndices().OrderBy(id => id)) {
                if (filter != null && !filter(id)) continue;
                double distance = MeshQueries.TriDistanceSqr(mesh, id, point);
                Assert.True(!double.IsNaN(distance) && !double.IsInfinity(distance) && distance >= 0,
                    "Nondegenerate point oracle: point=" + VecText(point) + "; triangle=" + TriText(Triangle(mesh, id)));
                if (distance < bestSquared) {
                    bestSquared = distance;
                    bestIds.Clear();
                    bestIds.Add(id);
                } else if (distance == bestSquared && bestIds.Count > 0) {
                    bestIds.Add(id);
                }
            }
            return bestIds;
        }

        static Dictionary<int, double> RayScan(DMesh3 mesh, Ray3d ray, double cutoff, Func<int, bool> filter)
        {
            // The public ray queries substitute float.MaxValue for the default length cutoff.
            double maximum = cutoff < double.MaxValue ? cutoff : float.MaxValue;
            var hits = new Dictionary<int, double>();
            foreach (int id in mesh.TriangleIndices().OrderBy(id => id)) {
                if (filter != null && !filter(id)) continue;
                Triangle3d triangle = Triangle(mesh, id);
                if (IntrRay3Triangle3.Intersects(ref ray, ref triangle.V0, ref triangle.V1, ref triangle.V2, out double t) &&
                    t < maximum) {
                    Assert.True(t >= 0 && !double.IsNaN(t) && !double.IsInfinity(t));
                    hits.Add(id, t);
                }
            }
            return hits;
        }

        void CheckPoint(DMeshAABBTree3 tree, Vector3d point, double cutoff, string context)
        {
            context += "; point=" + VecText(point) + "; cutoff=" + Number(cutoff);
            output.WriteLine(context);
            List<int> expectedIds = PointScan(tree.Mesh, point, cutoff, tree.TriangleFilterF, out double expectedSquared);
            int first = tree.FindNearestTriangle(point, cutoff);
            int second = tree.FindNearestTriangle(point, out double squared, cutoff);
            foreach (int id in new[] { first, second }) {
                Assert.True(expectedIds.Count == 0 ? id == DMesh3.InvalidID : expectedIds.Contains(id),
                    context + "; actualId=" + id + "; minimizingIds=[" + string.Join(",", expectedIds) + "]");
                if (id != DMesh3.InvalidID) CheckClosest(tree.Mesh, id, point, expectedSquared, context);
            }
            Assert.True(squared == expectedSquared,
                context + "; actualSquared=" + Number(squared) + "; expectedSquared=" + Number(expectedSquared));
            Assert.Equal(first, tree.FindNearestTriangle(point, cutoff));
            Assert.Equal(second, tree.FindNearestTriangle(point, out double repeatedSquared, cutoff));
            Assert.Equal(squared, repeatedSquared);
        }

        void CheckRay(DMeshAABBTree3 tree, Ray3d ray, double cutoff, string context)
        {
            context += "; ray.origin=" + VecText(ray.Origin) + "; ray.direction=" + VecText(ray.Direction) +
                "; cutoff=" + Number(cutoff);
            output.WriteLine(context);
            Dictionary<int, double> expected = RayScan(tree.Mesh, ray, cutoff, tree.TriangleFilterF);
            int nearest = tree.FindNearestHitTriangle(ray, cutoff);
            double minimum = expected.Count == 0 ? double.MaxValue : expected.Values.Min();
            int[] optimal = expected.Where(hit => hit.Value == minimum).Select(hit => hit.Key).OrderBy(id => id).ToArray();
            Assert.True(expected.Count == 0 ? nearest == DMesh3.InvalidID : optimal.Contains(nearest),
                context + "; actualId=" + nearest + "; optimalIds=[" + string.Join(",", optimal) + "]");
            if (nearest != DMesh3.InvalidID) {
                Triangle3d triangle = Triangle(tree.Mesh, nearest);
                Assert.True(IntrRay3Triangle3.Intersects(ref ray, ref triangle.V0, ref triangle.V1, ref triangle.V2,
                    out double actualParameter), context);
                Assert.True(actualParameter == minimum, context + "; actualT=" + Number(actualParameter));
                CheckClosest(tree.Mesh, nearest, ray.PointAt(actualParameter), 0, context);
            }
            Assert.Equal(nearest, tree.FindNearestHitTriangle(ray, cutoff));
            Assert.True(tree.FindAllHitTriangles(ray, null, cutoff) == expected.Count, context + "; null-list count");
            foreach (bool prefixed in new[] { false, true }) {
                var hits = prefixed ? new List<int> { -17, -23 } : new List<int>();
                int prefixLength = hits.Count;
                int count = tree.FindAllHitTriangles(ray, hits, cutoff);
                int[] appended = hits.Skip(prefixLength).OrderBy(id => id).ToArray();
                Assert.True(count == expected.Count && count == hits.Count - prefixLength,
                    context + "; returnedCount=" + count + "; hits=[" + string.Join(",", hits) + "]");
                Assert.True(!prefixed || (hits[0] == -17 && hits[1] == -23), context + "; prefix overwritten");
                Assert.True(appended.Distinct().Count() == appended.Length, context + "; duplicate hit IDs");
                Assert.True(appended.SequenceEqual(expected.Keys.OrderBy(id => id)),
                    context + "; actualHits=[" + string.Join(",", appended) + "]; expectedHits=[" +
                    string.Join(",", expected.Keys.OrderBy(id => id)) + "]");
                foreach (int id in appended) CheckClosest(tree.Mesh, id, ray.PointAt(expected[id]), 0, context);
            }
        }

        // Match the tree's static leaf predicate, including its coplanar SAT policy, not Find() or an exact oracle.
        static bool IntersectionScan(DMesh3 mesh, Triangle3d query, Func<int, bool> filter)
        {
            foreach (int id in mesh.TriangleIndices().OrderBy(id => id)) {
                if (filter != null && !filter(id)) continue;
                Triangle3d triangle = Triangle(mesh, id);
                if (IntrTriangle3Triangle3.Intersects(ref query, ref triangle)) return true;
            }
            return false;
        }

        static bool PairScan(DMeshAABBTree3 tree, DMeshAABBTree3 other, Func<Vector3d, Vector3d> transform = null)
        {
            foreach (int id in other.Mesh.TriangleIndices().OrderBy(id => id)) {
                if (other.TriangleFilterF != null && !other.TriangleFilterF(id)) continue;
                Triangle3d triangle = Triangle(other.Mesh, id);
                if (transform != null)
                    triangle = new Triangle3d(transform(triangle.V0), transform(triangle.V1), transform(triangle.V2));
                if (IntersectionScan(tree.Mesh, triangle, tree.TriangleFilterF)) return true;
            }
            return false;
        }

        void CheckIntersection(DMeshAABBTree3 tree, Triangle3d query, DMeshAABBTree3 other, string context,
            Func<Vector3d, Vector3d> transform = null)
        {
            context += "; testTriangle=" + TriText(query) + "; otherMesh=" + MeshText(other.Mesh);
            output.WriteLine(context);
            bool expectedTriangle = IntersectionScan(tree.Mesh, query, tree.TriangleFilterF);
            bool expectedPair = PairScan(tree, other, transform);
            Assert.True(tree.TestIntersection(query) == expectedTriangle, context + "; triangle Boolean mismatch");
            Assert.True(tree.TestIntersection(other, transform) == expectedPair, context + "; tree/tree Boolean mismatch");
            Assert.True(tree.TestIntersection(query) == expectedTriangle, context + "; repeated triangle query");
            Assert.True(tree.TestIntersection(other, transform) == expectedPair, context + "; repeated tree/tree query");
        }

        static Vector3d[] NamedPoints() => new[] { new Vector3d(0.5, 0.5, 1), new Vector3d(1, -1, 1),
            new Vector3d(-1, -1, 1), new Vector3d(0.5, 0.5, 0), new Vector3d(16, 16, 16),
            Vector3d.Zero, new Vector3d(1, 1, 1), new Vector3d(4096, -4096, 4096),
            new Vector3d(1, 1.0 / 16384, 0.5) };

        static Ray3d[] NamedRays() => new[] {
            new Ray3d(new Vector3d(0.5, 0.5, 8), -Vector3d.AxisZ),
            new Ray3d(new Vector3d(0.5, 0.5, -2), Vector3d.AxisZ),
            new Ray3d(new Vector3d(0.5, 0.5, 8), Vector3d.AxisZ),
            new Ray3d(new Vector3d(1, 0.5, 0.5), -Vector3d.AxisZ),
            new Ray3d(new Vector3d(0.5, 0.5, 0), -Vector3d.AxisZ),
            new Ray3d(new Vector3d(1, 1, 8), -Vector3d.AxisZ),
            new Ray3d(new Vector3d(0, 0, 8), -Vector3d.AxisZ),
            new Ray3d(new Vector3d(-1, 0.5, 0), Vector3d.AxisX),
            new Ray3d(new Vector3d(16, 16, 8), -Vector3d.AxisZ),
            new Ray3d(new Vector3d(1, 1.0 / 16384, 8), -Vector3d.AxisZ) };

        static Triangle3d[] IntersectionQueries() => new[] {
            new Triangle3d(new Vector3d(8, 8, 0), new Vector3d(9, 8, 0), new Vector3d(8, 9, 0)),
            new Triangle3d(new Vector3d(1.5, 1.5, 0), new Vector3d(2, 1.5, 0), new Vector3d(1.5, 2, 0)),
            new Triangle3d(new Vector3d(0.5, 0.5, -1), new Vector3d(0.5, 0.5, 1), new Vector3d(1, 0.5, 0)),
            new Triangle3d(new Vector3d(2, 0, 0), new Vector3d(3, 0, 1), new Vector3d(3, 1, 1)),
            new Triangle3d(new Vector3d(2, 0, 0), new Vector3d(0, 2, 0), new Vector3d(2, 2, 0)),
            UnitTriangle() };

        static IEnumerable<double> Cutoffs(double distance)
        {
            yield return 0;
            yield return 1;
            yield return double.MaxValue;
            if (distance > 0 && distance < double.MaxValue) {
                yield return distance * 0.5;
                yield return distance;
                yield return distance * 1.5;
            }
        }

        [Theory]
        [MemberData(nameof(NamedCases))]
        public void NamedFixtures_FullQueryAndBuildMatrix(string fixture, DMeshAABBTree3.BuildStrategy strategy,
            DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            DMesh3 mesh = Fixture(fixture);
            var tree = Build(mesh, strategy, policy, leafSize);
            string before = MeshText(mesh);
            string context = Context(mesh, fixture, strategy, policy, leafSize);
            Vector3d[] points = NamedPoints();
            for (int i = 0; i < points.Length; ++i) {
                PointScan(mesh, points[i], double.MaxValue, null, out double squared);
                foreach (double cutoff in Cutoffs(Math.Sqrt(squared)))
                    CheckPoint(tree, points[i], cutoff, context + "; pointQuery=" + i);
            }
            Ray3d[] rays = NamedRays();
            for (int i = 0; i < rays.Length; ++i) {
                Dictionary<int, double> hits = RayScan(mesh, rays[i], double.MaxValue, null);
                foreach (double cutoff in Cutoffs(hits.Count == 0 ? 0 : hits.Values.Min()))
                    CheckRay(tree, rays[i], cutoff, context + "; rayQuery=" + i);
            }
            Triangle3d[] queries = IntersectionQueries();
            for (int i = 0; i < queries.Length; ++i) {
                var otherMesh = new DMesh3();
                AddTriangle(otherMesh, queries[i]);
                AddTriangle(otherMesh, new Triangle3d(new Vector3d(32, 32, 0),
                    new Vector3d(33, 32, 0), new Vector3d(32, 33, 0)));
                AddTriangle(otherMesh, new Triangle3d(new Vector3d(-32, -32, 0),
                    new Vector3d(-31, -32, 0), new Vector3d(-32, -31, 0)));
                var other = Build(otherMesh, strategy, policy, leafSize);
                string otherBefore = MeshText(otherMesh);
                CheckIntersection(tree, queries[i], other, context + "; intersectionQuery=" + i);
                Assert.True(other.TestIntersection(tree) == PairScan(other, tree), context + "; reversed tree pair");
                Assert.True(otherBefore == MeshText(otherMesh), context + "; other mesh mutated");
            }
            Assert.True(before == MeshText(mesh), context + "; read-only queries mutated mesh");
        }

        [Fact]
        public void NamedOracles_ExerciseStrictCutoffsTiesAndSuccessfulContacts()
        {
            DMesh3 one = Fixture("one");
            var tree = new DMeshAABBTree3(one, true);
            string context = Context(one, "analytic", DMeshAABBTree3.BuildStrategy.Default,
                DMeshAABBTree3.ClusterPolicy.Default, 4);
            var points = new[] { new Vector3d(0.5, 0.5, 1), new Vector3d(1, -1, 0), new Vector3d(-1, 0, 0) };
            var closest = new[] { new Vector3d(0.5, 0.5, 0), new Vector3d(1, 0, 0), Vector3d.Zero };
            for (int i = 0; i < points.Length; ++i) {
                Assert.Equal(closest[i], MeshQueries.TriangleDistance(one, 0, points[i]).TriangleClosest);
                Assert.Equal(1, MeshQueries.TriDistanceSqr(one, 0, points[i]));
                Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(points[i], 1));
                Assert.Equal(0, tree.FindNearestTriangle(points[i], 2));
            }
            var ray = new Ray3d(new Vector3d(0.5, 0.5, 1), -Vector3d.AxisZ);
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(ray, 1));
            Assert.Equal(0, tree.FindAllHitTriangles(ray, null, 1));
            Assert.Equal(0, tree.FindNearestHitTriangle(ray, 2));
            Assert.Equal(1, tree.FindAllHitTriangles(ray, null, 2));
            Triangle3d[] queries = IntersectionQueries();
            for (int i = 0; i < queries.Length; ++i)
                Assert.True(IntersectionScan(one, queries[i], null) == (i >= 2),
                    context + "; analytic intersectionQuery=" + i + "; query=" + TriText(queries[i]));
            DMesh3 equal = Fixture("equal-centroid-vertex-ties");
            Vector3d tiePoint = new Vector3d(0, 0, 1);
            string tieContext = Context(equal, "analytic-exact-vertex-ties", DMeshAABBTree3.BuildStrategy.Default,
                DMeshAABBTree3.ClusterPolicy.Default, 4) + "; point=" + VecText(tiePoint) + "; cutoff=2";
            foreach (int id in equal.TriangleIndices().OrderBy(id => id)) {
                Triangle3d triangle = Triangle(equal, id);
                Assert.True(triangle.V0 == Vector3d.Zero &&
                    triangle.V0 + triangle.V1 + triangle.V2 == new Vector3d(0, 3, 0),
                    tieContext + "; shared vertex/equal centroid precondition; triangle=" + TriText(triangle));
                double primitiveSquared = MeshQueries.TriDistanceSqr(equal, id, tiePoint);
                Assert.True(primitiveSquared == 1, tieContext + "; triangleId=" + id +
                    "; primitiveSquared=" + Number(primitiveSquared));
            }
            List<int> ties = PointScan(equal, tiePoint, 2, null, out double squared);
            Assert.True(ties.SequenceEqual(equal.TriangleIndices().OrderBy(id => id)), tieContext +
                "; minimizingIds=[" + string.Join(",", ties) + "]");
            Assert.Equal(equal.TriangleCount, ties.Count);
            Assert.Equal(1, squared);
            CheckPoint(new DMeshAABBTree3(equal, true), tiePoint, 2, tieContext);
            DMesh3 odd = Fixture("odd-stack");
            Assert.Equal(5, RayScan(odd, NamedRays()[0], double.MaxValue, null).Count);
            DMesh3 box = Fixture("box");
            Assert.NotEmpty(RayScan(box, NamedRays()[3], double.MaxValue, null));
            Assert.NotEmpty(RayScan(one, NamedRays()[4], double.MaxValue, null));
            Assert.NotEmpty(RayScan(one, NamedRays()[5], double.MaxValue, null));
            Assert.NotEmpty(RayScan(one, NamedRays()[6], double.MaxValue, null));
            Assert.Empty(RayScan(one, NamedRays()[7], double.MaxValue, null));
            Assert.Empty(RayScan(one, NamedRays()[8], double.MaxValue, null));
        }

        [Theory]
        [InlineData(17, 0)]
        [InlineData(29, 1)]
        [InlineData(43, 2)]
        [InlineData(71, 3)]
        [InlineData(101, 4)]
        [InlineData(137, 5)]
        [InlineData(181, 6)]
        [InlineData(233, 7)]
        public void SeededMeshes_BoundedPointRayDifferential(int seed, int buildIndex)
        {
            var random = new Random(seed);
            var mesh = new DMesh3();
            int triangleCount = 32 + random.Next(97);
            for (int i = 0; i < triangleCount; ++i) {
                Vector3d origin = new Vector3d(random.Next(-32, 33) / 8.0,
                    random.Next(-32, 33) / 8.0, random.Next(-32, 33) / 8.0);
                int axis = random.Next(3);
                Vector3d u = Vector3d.Zero, v = Vector3d.Zero;
                u[axis] = random.Next(1, 17) / 8.0;
                v[(axis + 1) % 3] = random.Next(1, 17) / 8.0;
                AddTriangle(mesh, new Triangle3d(origin, origin + u, origin + v));
            }
            Assert.True(mesh.CheckValidity());
            object[][] builds = BuildCases().ToArray();
            // Cover both top-down strategies and every bottom-up policy without a random Cartesian product.
            int[] distribution = { 0, 3, 6, 9, 10, 11, 12, 7 };
            object[] configuration = builds[distribution[buildIndex]];
            var strategy = (DMeshAABBTree3.BuildStrategy)configuration[0];
            var policy = (DMeshAABBTree3.ClusterPolicy)configuration[1];
            int leafSize = (int)configuration[2];
            var tree = Build(mesh, strategy, policy, leafSize);
            string before = MeshText(mesh);
            string context = Context(mesh, "generated", strategy, policy, leafSize, seed);
            int[] ids = mesh.TriangleIndices().OrderBy(id => id).ToArray();
            for (int i = 0; i < 128; ++i) {
                Triangle3d selected = Triangle(mesh, ids[random.Next(ids.Length)]);
                Vector3d surface = 0.5 * selected.V0 + 0.25 * selected.V1 + 0.25 * selected.V2;
                Vector3d normal = (selected.V1 - selected.V0).Cross(selected.V2 - selected.V0).Normalized;
                int mode = (i / 2) % 4;
                Vector3d point = mode == 0 ? new Vector3d(32, 32, 32) :
                    surface + (random.Next(-16, 17) / 8.0) * normal;
                var ray = mode == 0 ? new Ray3d(new Vector3d(32, 32, 32), Vector3d.AxisZ) :
                    new Ray3d(surface + 2 * normal, -normal);
                double cutoff = mode == 0 ? 1 : mode == 1 ? 0 :
                    mode == 2 ? double.MaxValue : random.Next(1, 33) / 8.0;
                if (i % 2 == 0) CheckPoint(tree, point, cutoff, context + "; query=" + i);
                else CheckRay(tree, ray, cutoff, context + "; query=" + i);
            }
            Assert.True(before == MeshText(mesh), context + "; read-only queries mutated mesh");
        }

        static string QuerySignature(DMeshAABBTree3 tree)
        {
            Vector3d point = new Vector3d(0.5, 0.5, 8);
            var ray = new Ray3d(point, -Vector3d.AxisZ);
            int nearest = tree.FindNearestTriangle(point, out double squared);
            var hits = new List<int>();
            int count = tree.FindAllHitTriangles(ray, hits);
            return nearest + ";" + Number(squared) + ";" + tree.FindNearestHitTriangle(ray) + ";" + count +
                ";" + string.Join(",", hits.OrderBy(id => id)) + ";" + tree.TestIntersection(UnitTriangle());
        }

        [Theory]
        [MemberData(nameof(BuildCases))]
        public void FiltersRebuildAndQueryReuse_A_B_A_AgreeWithFreshTrees(DMeshAABBTree3.BuildStrategy strategy,
            DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            DMesh3 mesh = Fixture("odd-stack");
            var tree = Build(mesh, strategy, policy, leafSize);
            string context = Context(mesh, "filter/reuse", strategy, policy, leafSize);
            string before = MeshText(mesh);
            var filters = new Func<int, bool>[] { null, id => false, id => id % 2 != 0, id => true, null };
            var other = Build(Fixture("one"), strategy, policy, leafSize);
            for (int i = 0; i < filters.Length; ++i) {
                tree.TriangleFilterF = filters[i];
                var fresh = Build(mesh, strategy, policy, leafSize);
                fresh.TriangleFilterF = filters[i];
                CheckPoint(tree, new Vector3d(0.5, 0.5, 8), 16, context + "; filterPhase=" + i);
                CheckRay(tree, NamedRays()[0], double.MaxValue, context + "; filterPhase=" + i);
                CheckIntersection(tree, UnitTriangle(), other, context + "; filterPhase=" + i);
                for (int j = 0; j < filters.Length; ++j) {
                    other.TriangleFilterF = filters[j];
                    Assert.True(tree.TestIntersection(other) == PairScan(tree, other),
                        context + "; receiverFilter=" + i + "; argumentFilter=" + j);
                    Assert.True(other.TestIntersection(tree) == PairScan(other, tree),
                        context + "; reversed receiverFilter=" + j + "; argumentFilter=" + i);
                }
                other.TriangleFilterF = null;
                Assert.True(QuerySignature(tree) == QuerySignature(fresh), context + "; filter/fresh mismatch phase=" + i);
                Assert.True(tree.IsValid && before == MeshText(mesh), context + "; filter changed mesh validity");
            }
            string signatureA = QuerySignature(tree);
            int[] vertices = mesh.VertexIndices().OrderBy(id => id).ToArray();
            Vector3d[] positions = vertices.Select(mesh.GetVertex).ToArray();
            for (int phase = 0; phase < 3; ++phase) {
                for (int i = 0; i < vertices.Length; ++i)
                    mesh.SetVertex(vertices[i], positions[i] + (phase == 1 ? new Vector3d(0, 0, 3) : Vector3d.Zero));
                tree.Build(strategy, policy);
                var fresh = Build(mesh, strategy, policy, leafSize);
                string phaseContext = Context(mesh, "reuse-phase-" + phase, strategy, policy, leafSize);
                string phaseBefore = MeshText(mesh);
                CheckPoint(tree, new Vector3d(0.5, 0.5, 8), 16, phaseContext);
                CheckRay(tree, NamedRays()[0], 16, phaseContext);
                Assert.True(QuerySignature(tree) == QuerySignature(fresh), phaseContext + "; reused/fresh mismatch");
                if (phase != 1) Assert.True(signatureA == QuerySignature(tree), phaseContext + "; A was not restored");
                else Assert.True(signatureA != QuerySignature(tree), phaseContext + "; B must exercise different query results");
                string signature = QuerySignature(tree);
                tree.Build(strategy, policy);
                Assert.True(signature == QuerySignature(tree), phaseContext + "; repeated Build mismatch");
                Assert.True(phaseBefore == MeshText(mesh), phaseContext + "; reuse queries or repeated Build mutated mesh");
            }
        }

        public static IEnumerable<object[]> EditCases()
        {
            foreach (object[] build in BuildCases())
                foreach (bool topology in new[] { false, true })
                    yield return new object[] { topology, build[0], build[1], build[2] };
        }

        [Theory]
        [MemberData(nameof(EditCases))]
        public void ShapeAndTopologyEdits_RejectStaleReceiverUntilRebuild(bool topology,
            DMeshAABBTree3.BuildStrategy strategy, DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            DMesh3 mesh = Fixture("odd-stack");
            var tree = Build(mesh, strategy, policy, leafSize);
            var other = Build(Fixture("one"), strategy, policy, leafSize);
            string context = Context(mesh, topology ? "topology-edit" : "position-edit", strategy, policy, leafSize);
            if (topology) Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(2));
            else mesh.SetVertex(0, mesh.GetVertex(0) + new Vector3d(0, 0, 0.5));
            context += "; edit=" + (topology ? "RemoveTriangle(2)" : "SetVertex(0,old+(0,0,0.5))") +
                "; editedMesh=" + MeshText(mesh);
            output.WriteLine(context);
            Assert.False(tree.IsValid);
            Vector3d point = new Vector3d(0.5, 0.5, 8);
            Ray3d ray = NamedRays()[0];
            Assert.Throws<Exception>(() => tree.FindNearestTriangle(point));
            Assert.Throws<Exception>(() => tree.FindNearestTriangle(point, out double ignored));
            Assert.Throws<Exception>(() => tree.FindNearestHitTriangle(ray));
            Assert.Throws<Exception>(() => tree.FindAllHitTriangles(ray));
            var prefix = new List<int> { -17, -23 };
            Assert.Throws<Exception>(() => tree.FindAllHitTriangles(ray, prefix));
            Assert.Equal(new[] { -17, -23 }, prefix);
            Assert.Throws<Exception>(() => tree.TestIntersection(UnitTriangle()));
            Assert.Throws<Exception>(() => tree.TestIntersection(other));
            tree.Build(strategy, policy);
            Assert.True(tree.IsValid);
            var fresh = Build(mesh, strategy, policy, leafSize);
            string rebuiltBefore = MeshText(mesh);
            Assert.True(QuerySignature(tree) == QuerySignature(fresh), context + "; rebuild/fresh mismatch");
            CheckPoint(tree, point, double.MaxValue, context);
            CheckRay(tree, ray, double.MaxValue, context);
            CheckIntersection(tree, UnitTriangle(), other, context);
            Assert.True(rebuiltBefore == MeshText(mesh), context + "; rebuilt queries mutated mesh");
        }

        // Independent blocker: TestIntersection checks the receiver timestamp, but not the argument tree's timestamp.
        [Fact]
        public void StaleOtherTree_IntersectionMustRejectInvalidArgument()
        {
            var receiver = new DMeshAABBTree3(Fixture("one"), true);
            DMesh3 edited = Fixture("one");
            var other = new DMeshAABBTree3(edited, true);
            string context = Context(edited, "stale-other-tree", DMeshAABBTree3.BuildStrategy.Default,
                DMeshAABBTree3.ClusterPolicy.Default, 4);
            edited.SetVertex(0, new Vector3d(0, 0, 0.5));
            output.WriteLine(context + "; edit=SetVertex(0,(0,0,0.5)); editedMesh=" + MeshText(edited));
            Assert.True(receiver.IsValid);
            Assert.False(other.IsValid);
            Assert.Throws<Exception>(() => receiver.TestIntersection(other));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void NonNormalizedRay_IsRejectedWithoutChangingDestination(int directionLength)
        {
            DMesh3 mesh = Fixture("one");
            var tree = new DMeshAABBTree3(mesh, true);
            var ray = new Ray3d(new Vector3d(0.5, 0.5, 1), -Vector3d.AxisZ);
            ray.Direction = -directionLength * Vector3d.AxisZ;
            string before = MeshText(mesh);
            output.WriteLine("fixture=one; query=0; ray.origin=" + VecText(ray.Origin) +
                "; ray.direction=" + VecText(ray.Direction) + "; " + before);
            Assert.False(ray.Direction.IsNormalized);
            Assert.Throws<Exception>(() => tree.FindNearestHitTriangle(ray));
            Assert.Throws<Exception>(() => tree.FindAllHitTriangles(ray));
            var hits = new List<int> { -17, -23 };
            Assert.Throws<Exception>(() => tree.FindAllHitTriangles(ray, hits));
            Assert.Equal(new[] { -17, -23 }, hits);
            Assert.Equal(before, MeshText(mesh));
        }

        [Theory]
        [MemberData(nameof(BuildCases))]
        public void DefaultAliasesAndInputPermutation_PreserveQueryGeometry(DMeshAABBTree3.BuildStrategy strategy,
            DMeshAABBTree3.ClusterPolicy policy, int leafSize)
        {
            DMesh3 mesh = Fixture("odd-stack");
            var original = Build(mesh, strategy, policy, leafSize);
            string originalBefore = MeshText(mesh);
            Vector3d point = new Vector3d(0.5, 0.5, 8);
            Ray3d ray = NamedRays()[0];
            var other = Build(Fixture("one"), strategy, policy, leafSize);
            foreach (int seed in new[] { 17, 29, 43, 71, 101, 137, 181, 233 }) {
                DMesh3 permuted = Invariance.Permuted(mesh, seed);
                var reordered = Build(permuted, strategy, policy, leafSize);
                string context = Context(permuted, "permuted-odd-stack", strategy, policy, leafSize, seed);
                string reorderedBefore = MeshText(permuted);
                CheckPoint(reordered, point, double.MaxValue, context);
                CheckRay(reordered, ray, double.MaxValue, context);
                CheckIntersection(reordered, UnitTriangle(), other, context);
                int first = original.FindNearestTriangle(point, out double a);
                int second = reordered.FindNearestTriangle(point, out double b);
                Assert.Equal(a, b);
                Assert.Equal(MeshQueries.TriangleDistance(mesh, first, point).TriangleClosest,
                    MeshQueries.TriangleDistance(permuted, second, point).TriangleClosest);
                Assert.True(RayScan(mesh, ray, double.MaxValue, null).Values.OrderBy(t => t).SequenceEqual(
                    RayScan(permuted, ray, double.MaxValue, null).Values.OrderBy(t => t)), context + "; hit geometry changed");
                Assert.True(reorderedBefore == MeshText(permuted), context + "; permutation queries mutated mesh");
            }
            var automatic = new DMeshAABBTree3(mesh, true);
            var defaultBuild = new DMeshAABBTree3(mesh);
            defaultBuild.Build();
            var alias = Build(mesh, DMeshAABBTree3.BuildStrategy.Default, DMeshAABBTree3.ClusterPolicy.Default, 4);
            Assert.Equal(QuerySignature(automatic), QuerySignature(defaultBuild));
            Assert.Equal(QuerySignature(automatic), QuerySignature(alias));
            Assert.Equal(originalBefore, MeshText(mesh));
        }

        static void SimilarityScalar(double expected, double actual, Similarity similarity, double size, string context)
        {
            if (similarity.IsExact)
                Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
                    context + "; expected=" + Number(expected) + "; actual=" + Number(actual));
            else
                Assert.True(Math.Abs(expected - actual) <= 128 * similarity.Resolution(size) * Math.Max(1, size),
                    context + "; expected=" + Number(expected) + "; actual=" + Number(actual));
        }

        static void SimilarityPoint(Vector3d expected, Vector3d mapped, Similarity similarity, string context)
        {
            Assert.True(mapped.IsFinite, context);
            if (similarity.IsExact) {
                for (int i = 0; i < 3; ++i)
                    Assert.True(BitConverter.DoubleToInt64Bits(expected[i]) == BitConverter.DoubleToInt64Bits(mapped[i]),
                        context + "; expectedPoint=" + VecText(expected) + "; mappedPoint=" + VecText(mapped));
            } else {
                Assert.True(expected.Distance(mapped) <= 128 * similarity.Resolution(16),
                    context + "; expectedPoint=" + VecText(expected) + "; mappedPoint=" + VecText(mapped));
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RepresentativeQueries_AllSimilarities_MapBackGeometryAndCutoffs(string name)
        {
            Similarity similarity = Similarity.ByName(name);
            DMesh3 baseline = Fixture("odd-stack");
            DMesh3 transformed = Invariance.Transformed(baseline, similarity);
            Triangle3d crossing = new Triangle3d(new Vector3d(0.5, 0.5, 3.5),
                new Vector3d(0.5, 0.5, 4.5), new Vector3d(1, 0.5, 4));
            var otherMesh = new DMesh3();
            AddTriangle(otherMesh, crossing);
            Vector3d point = new Vector3d(0.5, 0.5, 8);
            Ray3d ray = NamedRays()[0];
            var mappedRay = new Ray3d(similarity.Apply(ray.Origin), similarity.ApplyDirection(ray.Direction));
            string before = MeshText(transformed);
            foreach (object[] build in BuildCases()) {
                var strategy = (DMeshAABBTree3.BuildStrategy)build[0];
                var policy = (DMeshAABBTree3.ClusterPolicy)build[1];
                int leafSize = (int)build[2];
                var reference = Build(baseline, strategy, policy, leafSize);
                var tree = Build(transformed, strategy, policy, leafSize);
                string context = Context(transformed, "similarity:" + name, strategy, policy, leafSize);
                foreach (double cutoff in new[] { 0.0, 1.5, 16.0, double.MaxValue }) {
                    double transformedCutoff = cutoff == double.MaxValue ? cutoff : similarity.ApplyLength(cutoff);
                    Vector3d mappedPoint = similarity.Apply(point);
                    CheckPoint(tree, mappedPoint, transformedCutoff, context);
                    CheckRay(tree, mappedRay, transformedCutoff, context);
                    int id = reference.FindNearestTriangle(point, out double expectedSquared, cutoff);
                    int transformedId = tree.FindNearestTriangle(mappedPoint, out double actualSquared, transformedCutoff);
                    Assert.True(id == transformedId, context + "; unique nearest ID changed");
                    if (id != DMesh3.InvalidID || cutoff != double.MaxValue)
                        SimilarityScalar(expectedSquared, actualSquared / (similarity.Scale * similarity.Scale),
                            similarity, 16, context + "; squared distance or finite miss sentinel");
                    if (id != DMesh3.InvalidID) {
                        Vector3d closest = MeshQueries.TriangleDistance(transformed, transformedId, mappedPoint).TriangleClosest;
                        SimilarityPoint(MeshQueries.TriangleDistance(baseline, id, point).TriangleClosest,
                            similarity.Inverse(closest), similarity, context + "; closest point");
                    }
                    Assert.Equal(reference.FindNearestHitTriangle(ray, cutoff), tree.FindNearestHitTriangle(mappedRay, transformedCutoff));
                    Dictionary<int, double> expectedHits = RayScan(baseline, ray, cutoff, null);
                    Dictionary<int, double> actualHits = RayScan(transformed, mappedRay, transformedCutoff, null);
                    Assert.True(expectedHits.Keys.OrderBy(i => i).SequenceEqual(actualHits.Keys.OrderBy(i => i)), context);
                    foreach (int hit in expectedHits.Keys.OrderBy(i => i)) {
                        SimilarityScalar(expectedHits[hit], actualHits[hit] / similarity.Scale, similarity, 16, context + "; ray parameter");
                        SimilarityPoint(ray.PointAt(expectedHits[hit]), similarity.Inverse(mappedRay.PointAt(actualHits[hit])),
                            similarity, context + "; ray hit point");
                    }
                }
                var originalOther = Build(otherMesh, strategy, policy, leafSize);
                var movedOther = Build(Invariance.Transformed(otherMesh, similarity), strategy, policy, leafSize);
                Triangle3d movedQuery = new Triangle3d(similarity.Apply(crossing.V0),
                    similarity.Apply(crossing.V1), similarity.Apply(crossing.V2));
                Assert.True(reference.TestIntersection(crossing), context + "; baseline crossing must hit");
                CheckIntersection(tree, movedQuery, movedOther, context + "; transformed other geometry");
                CheckIntersection(tree, movedQuery, originalOther, context + "; TransformF=Similarity.Apply", similarity.Apply);
                Assert.True(tree.TestIntersection(movedQuery), context + "; crossing intersection changed");
                Triangle3d separated = IntersectionQueries()[0];
                var movedSeparated = new Triangle3d(similarity.Apply(separated.V0), similarity.Apply(separated.V1), similarity.Apply(separated.V2));
                Assert.False(tree.TestIntersection(movedSeparated), context + "; separated intersection changed");
            }
            Assert.True(before == MeshText(transformed), name + "; read-only queries changed transformed mesh");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ClosedBox_AllSimilarities_PreserveExteriorDistanceAndInteriorRay(string name)
        {
            Similarity similarity = Similarity.ByName(name);
            DMesh3 baseline = Fixture("box");
            DMesh3 transformed = Invariance.Transformed(baseline, similarity);
            Assert.True(transformed.IsClosed());
            Vector3d point = new Vector3d(0.5, 0.75, 3);
            var ray = new Ray3d(new Vector3d(0.5, 0.75, 1), -Vector3d.AxisZ);
            Vector3d mappedPoint = similarity.Apply(point);
            var mappedRay = new Ray3d(similarity.Apply(ray.Origin), similarity.ApplyDirection(ray.Direction));
            string before = MeshText(transformed);
            foreach (var strategy in new[] { DMeshAABBTree3.BuildStrategy.TopDownMedian,
                DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings }) {
                var policy = DMeshAABBTree3.ClusterPolicy.MinimalVolume;
                var reference = Build(baseline, strategy, policy, 2);
                var tree = Build(transformed, strategy, policy, 2);
                string context = Context(transformed, "closed-box-similarity:" + name, strategy, policy, 2);
                Assert.Single(RayScan(baseline, ray, double.MaxValue, null));
                foreach (double cutoff in new[] { 0.0, 0.5, 2.0, double.MaxValue }) {
                    double mappedCutoff = cutoff == double.MaxValue ? cutoff : similarity.ApplyLength(cutoff);
                    CheckPoint(tree, mappedPoint, mappedCutoff, context);
                    CheckRay(tree, mappedRay, mappedCutoff, context);
                    int expectedId = reference.FindNearestTriangle(point, out double expectedSquared, cutoff);
                    int actualId = tree.FindNearestTriangle(mappedPoint, out double actualSquared, mappedCutoff);
                    Assert.True(expectedId == actualId, context + "; unique exterior nearest ID changed");
                    if (expectedId != DMesh3.InvalidID || cutoff != double.MaxValue)
                        SimilarityScalar(expectedSquared, actualSquared / (similarity.Scale * similarity.Scale),
                            similarity, 4, context + "; exterior squared distance");
                    if (expectedId != DMesh3.InvalidID)
                        SimilarityPoint(MeshQueries.TriangleDistance(baseline, expectedId, point).TriangleClosest,
                            similarity.Inverse(MeshQueries.TriangleDistance(transformed, actualId, mappedPoint).TriangleClosest),
                            similarity, context + "; exterior closest point");
                    Assert.Equal(reference.FindNearestHitTriangle(ray, cutoff), tree.FindNearestHitTriangle(mappedRay, mappedCutoff));
                    Dictionary<int, double> expected = RayScan(baseline, ray, cutoff, null);
                    Dictionary<int, double> actual = RayScan(transformed, mappedRay, mappedCutoff, null);
                    Assert.True(expected.Keys.OrderBy(id => id).SequenceEqual(actual.Keys.OrderBy(id => id)), context);
                    foreach (int id in expected.Keys) {
                        SimilarityScalar(expected[id], actual[id] / similarity.Scale, similarity, 4, context + "; interior ray t");
                        SimilarityPoint(ray.PointAt(expected[id]), similarity.Inverse(mappedRay.PointAt(actual[id])),
                            similarity, context + "; interior ray hit point");
                    }
                }
            }
            Assert.True(before == MeshText(transformed), name + "; closed-box queries mutated mesh");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DegenerateDistance_SeparateSegmentAndPointContractProbe(bool pointTriangle)
        {
            Triangle3d triangle = pointTriangle ? new Triangle3d(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero) :
                new Triangle3d(Vector3d.Zero, Vector3d.AxisX, 2 * Vector3d.AxisX);
            var mesh = new DMesh3();
            AddTriangle(mesh, triangle);
            var tree = new DMeshAABBTree3(mesh, true);
            Vector3d query = new Vector3d(1, 1, 0);
            double expected = pointTriangle ? 2 : 1;
            string context = Context(mesh, "degenerate:" + pointTriangle, DMeshAABBTree3.BuildStrategy.Default,
                DMeshAABBTree3.ClusterPolicy.Default, 4);
            Assert.True(MeshQueries.TriDistanceSqr(mesh, 0, query) == expected, context + "; segment/point oracle");
            CheckPoint(tree, query, double.MaxValue, context);
            CheckPoint(tree, query, 0, context);
            CheckRay(tree, new Ray3d(new Vector3d(1, 1, 1), -Vector3d.AxisZ), double.MaxValue, context);
        }
    }
}
