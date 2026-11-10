using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRepairMetrics.SelfIntersectionPairs / HasSelfIntersections (the search under NotCleanReason) against a brute
    /// force over all pairs of non-degenerate triangles with ExactTriangleIntersection.IntersectsBeyondShared, the
    /// definition: the pair sets are equal (not only their number), also on meshes with coplanar, touching, shared-edge
    /// and shared-vertex pairs. The brute force shares no code with the search (no tree, no pair filter).
    /// </summary>
    public class SelfIntersectionPairsTests
    {
        internal static List<Index2i> BruteForce(DMesh3 mesh)
        {
            var tris = mesh.TriangleIndices().ToArray();
            var res = new List<Index2i>();
            for (int i = 0; i < tris.Length; ++i) {
                Index3i ia = mesh.GetTriangle(tris[i]);
                var A = new Triangle3d(mesh.GetVertex(ia.a), mesh.GetVertex(ia.b), mesh.GetVertex(ia.c));
                if (ExactTriangleIntersection.IsDegenerate(ref A)) continue;
                for (int j = i + 1; j < tris.Length; ++j) {
                    Index3i ib = mesh.GetTriangle(tris[j]);
                    var B = new Triangle3d(mesh.GetVertex(ib.a), mesh.GetVertex(ib.b), mesh.GetVertex(ib.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                    if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B))
                        res.Add(new Index2i(Math.Min(tris[i], tris[j]), Math.Max(tris[i], tris[j])));
                }
            }
            res.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return res;
        }

        /// <summary> a triangle soup over integer lattice points (a point is one vertex); triangles that would make the mesh non-manifold are left out </summary>
        internal static DMesh3 Soup(IEnumerable<(Vector3d, Vector3d, Vector3d)> tris)
        {
            var m = new DMesh3();
            var ids = new Dictionary<Vector3d, int>();
            int id(Vector3d p) { if (!ids.TryGetValue(p, out int v)) { v = m.AppendVertex(p); ids[p] = v; } return v; }
            foreach (var (a, b, c) in tris)
                m.AppendTriangle(id(a), id(b), id(c));
            return m;
        }

        internal static void AssertSamePairs(DMesh3 m, string what)
        {
            var expected = BruteForce(m);
            var actual = MeshRepairMetrics.SelfIntersectionPairs(m);
            Assert.True(expected.SequenceEqual(actual), $"{what}: expected {expected.Count} pairs, got {actual.Count}");
            Assert.Equal(expected.Count > 0, MeshRepairMetrics.HasSelfIntersections(m));
            Assert.Equal(expected.Count, MeshRepairMetrics.SelfIntersections(m));
        }

        static DMesh3 Box(Vector3d min, Vector3d max) =>
            new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();

        static DMesh3 Merge(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (var p in parts) MeshEditor.Append(m, p);
            return m;
        }

        [Fact]
        public void Procedural_Cases_SamePairsAsTheBruteForce()
        {
            var one = Vector3d.One;
            var cases = new Dictionary<string, DMesh3> {
                ["two overlapping boxes"] = Merge(Box(Vector3d.Zero, 2 * one), Box(0.5 * one, 2.5 * one)),
                ["boxes sharing a face plane, overlapping (coplanar faces)"] = Merge(Box(Vector3d.Zero, 2 * one), Box(new Vector3d(1, 0.5, 0.5), new Vector3d(3, 1.5, 1.5))),
                ["boxes touching at a face"] = Merge(Box(Vector3d.Zero, one), Box(new Vector3d(1, 0, 0), new Vector3d(2, 1, 1))),
                ["boxes touching at an edge"] = Merge(Box(Vector3d.Zero, one), Box(new Vector3d(1, 1, 0), new Vector3d(2, 2, 1))),
                ["boxes touching at a corner"] = Merge(Box(Vector3d.Zero, one), Box(new Vector3d(1, 1, 1), new Vector3d(2, 2, 2))),
                ["box inside a box"] = Merge(Box(Vector3d.Zero, 4 * one), Box(one, 2 * one)),
                ["separate boxes"] = Merge(Box(Vector3d.Zero, one), Box(3 * one, 4 * one)),
                ["two spheres crossing"] = Merge(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh(),
                    Translated(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh(), new Vector3d(1.1, 0.2, 0.1))),
                ["one sphere (clean)"] = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh(),
                ["rotated boxes crossing"] = Merge(Box(Vector3d.Zero, 2 * one), Rotated(Box(0.2 * one, 1.8 * one), 30)),
            };
            foreach (var kv in cases)
                AssertSamePairs(kv.Value, kv.Key);
            Assert.True(BruteForce(cases["two overlapping boxes"]).Count > 0);
        }

        static DMesh3 Translated(DMesh3 m, Vector3d t) { MeshTransforms.Translate(m, t); return m; }
        static DMesh3 Rotated(DMesh3 m, double deg) { MeshTransforms.Rotate(m, m.GetBounds().Center, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, deg)); return m; }

        [Fact]
        public void LatticeSoups_CoplanarTouchingSharedEdgeAndDegenerate_SamePairsAsTheBruteForce()
        {
            // triangles on a small lattice, half of them in the plane z = 0: coplanar overlaps, touching at points and
            // edges, shared vertices and edges, zero-area triangles
            var rnd = new Random(11);
            int withPairs = 0, coplanarSoups = 0;
            for (int n = 0; n < 300; ++n) {
                bool flat = n % 2 == 0;
                int range = 2 + n % 3;
                Vector3d P() => new Vector3d(rnd.Next(0, range + 1), rnd.Next(0, range + 1), flat ? 0 : rnd.Next(0, 3));
                var list = new List<(Vector3d, Vector3d, Vector3d)>();
                for (int k = 0, count = 6 + rnd.Next(10); k < count; ++k)
                    list.Add((P(), P(), P()));
                DMesh3 m = Soup(list);
                AssertSamePairs(m, "soup " + n);
                if (SelfIntersectionPairsTests.BruteForce(m).Count > 0) withPairs++;
                if (flat) coplanarSoups++;
            }
            Assert.True(withPairs > 100 && coplanarSoups == 150, $"{withPairs} soups with pairs");
        }


        [Fact]
        public void SeparateVerticesAtOnePosition_AndDuplicateTrianglesWithOppositeWinding_SamePairsAsTheBruteForce()
        {
            // every triangle gets its own vertices (ids differ at one position: not "shared"), some triangles are repeated with
            // the reverse winding on the same ids (three shared vertices)
            var rnd = new Random(23);
            int withPairs = 0;
            for (int n = 0; n < 200; ++n) {
                var m = new DMesh3();
                Vector3d P() => new Vector3d(rnd.Next(0, 4), rnd.Next(0, 4), n % 2 == 0 ? 0 : rnd.Next(0, 3));
                for (int k = 0, count = 5 + rnd.Next(8); k < count; ++k) {
                    int a = m.AppendVertex(P()), b = m.AppendVertex(P()), c = m.AppendVertex(P());
                    m.AppendTriangle(a, b, c);
                    if (rnd.Next(4) == 0) m.AppendTriangle(a, c, b);
                }
                AssertSamePairs(m, "separate vertices " + n);
                if (BruteForce(m).Count > 0) withPairs++;
            }
            Assert.True(withPairs > 100, $"{withPairs}");
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e-3)]
        [InlineData(1e6)]
        [InlineData(1e-150)]
        [InlineData(1e150)]
        public void NonLatticeCoordinates_NearlyCoplanarAndScaled_SamePairsAsTheBruteForce(double scale)
        {
            // random double coordinates, a third of the triangles nearly in the plane z = 0 (offsets of 1e-9..1e-15 of the
            // size) and triangles sharing vertices; the brute force runs on the very same mesh
            var rnd = new Random(31);
            int withPairs = 0;
            for (int n = 0; n < 120; ++n) {
                var pts = Enumerable.Range(0, 7).Select(_ => new Vector3d(rnd.NextDouble(), rnd.NextDouble(),
                    n % 3 == 0 ? rnd.NextDouble() * Math.Pow(10, -9 - rnd.Next(7)) : rnd.NextDouble()) * scale).ToArray();
                var m = new DMesh3();
                foreach (var p in pts) m.AppendVertex(p);
                for (int k = 0, count = 5 + rnd.Next(6); k < count; ++k)
                    m.AppendTriangle(rnd.Next(7), rnd.Next(7), rnd.Next(7));
                if (m.TriangleCount == 0) continue;
                AssertSamePairs(m, $"scale {scale} case {n}");
                if (BruteForce(m).Count > 0) withPairs++;
            }
            Assert.True(withPairs > 20, $"{withPairs}");
        }

        [Fact]
        public void NotCleanReason_Strings_OfTheCases()
        {
            var one = Vector3d.One;
            Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(Merge(Box(Vector3d.Zero, 2 * one), Box(0.5 * one, 2.5 * one))));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(Merge(Box(Vector3d.Zero, one), Box(3 * one, 4 * one))));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh()));
        }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Pairs_UnderSimilarities_SameSetAsAtUnitScale(string name)
        {
            var s = Similarity.ByName(name);
            var one = Vector3d.One;
            DMesh3 unit = Merge(Box(Vector3d.Zero, 2 * one), Rotated(Box(0.5 * one, 2.5 * one), 17));
            var expected = MeshRepairMetrics.SelfIntersectionPairs(unit);
            Assert.NotEmpty(expected);
            DMesh3 moved = new DMesh3(unit);
            foreach (int v in moved.VertexIndices()) moved.SetVertex(v, s.Apply(moved.GetVertex(v)));
            if (s.IsExact)
                Assert.True(expected.SequenceEqual(MeshRepairMetrics.SelfIntersectionPairs(moved)), name);
            else
                Assert.True(MeshRepairMetrics.HasSelfIntersections(moved), name);
        }

        public static IEnumerable<object[]> Similarities => Similarity.Names();
    }
}
