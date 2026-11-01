using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The candidate pair search of ExactMeshArrangement (docs/design/PAIR_SEARCH.md): the self-join of a mesh's box
    /// tree gives exactly the pairs of the query per triangle (the reference, SelfJoin = false), for any split into
    /// work items, and the same arrangement; the order of its pairs depends only on the input.
    /// </summary>
    public class ExactBooleanPairSearchTests
    {
        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        // input sets: plain, self-intersecting, coplanar (n-ary), open, with degenerate triangles and unwelded vertices,
        // tiny (fewer triangles than a leaf) and single triangles
        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "box+sphere" };
            yield return new object[] { "twisted" };
            yield return new object[] { "grid" };
            yield return new object[] { "open" };
            yield return new object[] { "degenerate" };
            yield return new object[] { "tiny" };
            yield return new object[] { "two" };
        }

        static List<DMesh3> Input(string name)
        {
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 7);
            switch (name) {
                case "box+sphere": return new List<DMesh3> { box, sphere };
                case "twisted": {
                    DMesh3 t = Sphere(new Vector3d(0, 0, 0), 1, 8);
                    MeshEditor.Append(t, Sphere(new Vector3d(0.6, 0.2, 0.1), 0.8, 7));
                    return new List<DMesh3> { t, box };
                }
                case "grid": {
                    var boxes = new List<DMesh3>();
                    for (int i = 0; i < 2; ++i)
                        for (int j = 0; j < 2; ++j)
                            for (int k = 0; k < 2; ++k)
                                boxes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0.9 * k), new Vector3d(0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1)));
                    return boxes;
                }
                case "open": {
                    DMesh3 o = Sphere(new Vector3d(-0.2, 0.1, 0), 0.9, 7);
                    o.RemoveTriangle(o.TriangleIndices().First());
                    return new List<DMesh3> { o, box };
                }
                case "degenerate": {
                    // a zero-area triangle, and a copy of a triangle on unwelded (coincident) vertices
                    DMesh3 d = Sphere(new Vector3d(0, 0, 0), 0.8, 6);
                    Index3i t0 = d.GetTriangle(0);
                    Vector3d a = d.GetVertex(t0.a), b = d.GetVertex(t0.b);
                    int va = d.AppendVertex(a), vb = d.AppendVertex(b), vm = d.AppendVertex(0.5 * (a + b));
                    d.AppendTriangle(va, vb, vm);
                    int wa = d.AppendVertex(d.GetVertex(t0.a)), wb = d.AppendVertex(d.GetVertex(t0.b)), wc = d.AppendVertex(d.GetVertex(t0.c));
                    d.AppendTriangle(wa, wb, wc);
                    // a degenerate triangle (a segment) piercing the sphere's surface away from its vertices: it is not
                    // touch-only with the triangles it crosses, so only its degeneracy keeps it out of the pairs
                    int s0 = d.AppendVertex(new Vector3d(0.013, 0.021, 0.0)), s1 = d.AppendVertex(new Vector3d(0.013, 0.021, 1.5));
                    int s2 = d.AppendVertex(new Vector3d(0.013, 0.021, 0.75));
                    d.AppendTriangle(s0, s1, s2);
                    d.AppendTriangle(s2, s1, s0);
                    // and a mesh whose first triangle is such a segment (the smaller index of its pairs)
                    var e = new DMesh3();
                    int e0 = e.AppendVertex(new Vector3d(-0.6, -0.48, 0.21)), e1 = e.AppendVertex(new Vector3d(0.9, -0.48, 0.21)), e2 = e.AppendVertex(new Vector3d(0.15, -0.48, 0.21));
                    e.AppendTriangle(e0, e1, e2);
                    MeshEditor.Append(e, Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6));
                    return new List<DMesh3> { d, box, e };
                }
                case "tiny": {
                    var m = new DMesh3();
                    m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
                    m.AppendVertex(new Vector3d(0.2, 0.2, -0.5)); m.AppendVertex(new Vector3d(0.3, 0.2, 0.5));
                    m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 3, 4); m.AppendTriangle(1, 3, 4);
                    var single = new DMesh3();
                    single.AppendVertex(new Vector3d(0.1, 0.1, -1)); single.AppendVertex(new Vector3d(0.1, 0.1, 1)); single.AppendVertex(new Vector3d(0.4, 0.1, 0));
                    single.AppendTriangle(0, 1, 2);
                    return new List<DMesh3> { m, single };
                }
                case "two": {
                    // a mesh of exactly two triangles that cross (no shared vertex), and an empty mesh
                    var m = new DMesh3();
                    m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
                    m.AppendVertex(new Vector3d(0.2, 0.2, -0.5)); m.AppendVertex(new Vector3d(0.3, 0.2, 0.5)); m.AppendVertex(new Vector3d(0.2, 0.3, 0.5));
                    m.AppendTriangle(0, 1, 2); m.AppendTriangle(3, 4, 5);
                    return new List<DMesh3> { m, new DMesh3(), box };
                }
            }
            throw new ArgumentException(name);
        }

        static ExactMeshArrangement Run(List<DMesh3> meshes, bool selfJoin, int tasks = 256, long batch = 1 << 24, bool selfIntersections = true)
        {
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = selfIntersections, SelfJoin = selfJoin, SelfJoinTasks = tasks,
                KeepCandidatePairs = true, CandidateBatchSize = batch };
            arr.Compute();
            return arr;
        }

        static string Pieces(ExactMeshArrangement arr)
        {
            return string.Join(";", arr.Output.Select(o => $"{o.Mesh},{o.Source}:" + string.Join(",",
                new[] { o.A, o.B, o.C }.Select(p => arr.Exact(p)).Select(p => $"{p.X}/{p.W},{p.Y}/{p.W},{p.Z}/{p.W}")))
                .OrderBy(x => x, StringComparer.Ordinal));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SelfJoin_GivesTheSamePairsAndArrangement(string name)
        {
            List<DMesh3> meshes = Input(name);
            ExactMeshArrangement reference = Run(meshes, false);
            var expected = reference.CandidatePairList.OrderBy(x => x).ToList();
            Assert.Equal(expected.Count, expected.Distinct().Count());
            foreach (var (tasks, batch) in new[] { (1, 1L << 24), (2, 1L << 24), (3, 1L), (7, 1L << 24), (256, 1L), (256, 1L << 24), (100000, 1L << 24) }) {
                ExactMeshArrangement arr = Run(meshes, true, tasks, batch);
                Assert.Equal(expected, arr.CandidatePairList.OrderBy(x => x).ToList());
                Assert.Equal(reference.SelfIntersectingPairs, arr.SelfIntersectingPairs);
                Assert.Equal(reference.IntersectingPairs, arr.IntersectingPairs);
                Assert.Equal(Pieces(reference), Pieces(arr));
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void WithoutSelfIntersections_TheSelfJoinIsNotUsed(string name)
        {
            // only pairs of different meshes: the same with either setting
            List<DMesh3> meshes = Input(name);
            var a = Run(meshes, true, selfIntersections: false).CandidatePairList;
            var b = Run(meshes, false, selfIntersections: false).CandidatePairList;
            Assert.Equal(b, a);
            Assert.Equal(0, Run(meshes, true, selfIntersections: false).SelfIntersectingPairs);
        }

        [Fact]
        public void SelfJoin_SerialEqualsParallel()
        {
            foreach (string name in new[] { "twisted", "grid", "degenerate" }) {
                List<DMesh3> meshes = Input(name);
                List<long> parallel = Run(meshes, true).CandidatePairList, serial;
                gParallel.SerialOnThisThread = true;
                try {
                    serial = Run(meshes, true).CandidatePairList;
                } finally {
                    gParallel.SerialOnThisThread = false;
                }
                Assert.Equal(serial, parallel);
            }
        }

        [Fact]
        public void SelfJoin_PairOrderIsRepeatable()
        {
            List<DMesh3> meshes = Input("twisted");
            List<long> first = Run(meshes, true).CandidatePairList;
            for (int run = 0; run < 10; ++run)
                Assert.Equal(first, Run(meshes, true).CandidatePairList);
        }

        // closed boxes overlap (touching counts, unlike AxisAlignedBox3d.Intersects)
        static bool closed_overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return a.Max.x >= b.Min.x && b.Max.x >= a.Min.x && a.Max.y >= b.Min.y && b.Max.y >= a.Min.y && a.Max.z >= b.Min.z && b.Max.z >= a.Min.z;
        }

        [Fact]
        public void SelfPairs_EveryOverlappingBoxPairOnce()
        {
            // the tree alone, on random boxes of mixed sizes (also touching ones: closed overlap), against all pairs
            var rand = new Random(4);
            foreach (int n in new[] { 0, 1, 2, 8, 9, 17, 300 }) {
                var boxes = new List<AxisAlignedBox3d>();
                for (int i = 0; i < n; ++i) {
                    var lo = new Vector3d(rand.Next(0, 20), rand.Next(0, 20), rand.Next(0, 20));
                    boxes.Add(new AxisAlignedBox3d(lo, lo + new Vector3d(rand.Next(0, 4), rand.Next(0, 4), rand.Next(0, 4))));
                }
                var tree = new TriangleBoxTree(boxes);
                var expected = new List<(int, int)>();
                for (int i = 0; i < n; ++i)
                    for (int j = i + 1; j < n; ++j)
                        if (closed_overlap(boxes[i], boxes[j]))
                            expected.Add((i, j));
                foreach (int minTasks in new[] { 1, 5, 64, 10000 }) {
                    var got = new List<(int, int)>();
                    foreach (Index2i t in tree.SelfPairTasks(minTasks)) {
                        var list = new List<Index2i>();
                        tree.SelfPairs(t, list);
                        got.AddRange(list.Select(p => (Math.Min(p.a, p.b), Math.Max(p.a, p.b))));
                    }
                    Assert.Equal(expected.OrderBy(x => x).ToList(), got.OrderBy(x => x).ToList());
                }
            }
        }
    }
}
