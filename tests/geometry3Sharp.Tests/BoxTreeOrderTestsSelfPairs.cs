using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// (The class name contains BoxTreeOrderTests, which the test-case filter of the arrangement mutation shards matches.)
    /// TriangleBoxTree.SelfPairs (the list version, used by the exact boolean) returns the same pairs in the same order as
    /// before it became a wrapper of the visit version: the hash of the pair sequence over all work items of fixed inputs is
    /// pinned (computed with the list implementation of master before the change). The visit version hands over the same
    /// sequence and stops where visit returns false.
    /// </summary>
    public class BoxTreeOrderTestsSelfPairs
    {
        static List<AxisAlignedBox3d> SphereBoxes(int edgeVertices)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            var boxes = new List<AxisAlignedBox3d>();
            foreach (int t in m.TriangleIndices()) {
                Index3i i = m.GetTriangle(t);
                var b = new AxisAlignedBox3d(m.GetVertex(i.a)); b.Contain(m.GetVertex(i.b)); b.Contain(m.GetVertex(i.c));
                boxes.Add(b);
            }
            return boxes;
        }

        static List<AxisAlignedBox3d> RandomBoxes(int n, int seed)
        {
            var rnd = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>();
            for (int k = 0; k < n; ++k) {
                var c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                double e = 0.001 + 0.05 * rnd.NextDouble() * rnd.NextDouble();
                boxes.Add(new AxisAlignedBox3d(c - e * Vector3d.One, c + new Vector3d(e, e * rnd.NextDouble(), 0)));   // some flat boxes
            }
            return boxes;
        }

        // FNV-1a over the pair sequence of all work items (the order is part of the hash)
        static ulong Hash(IEnumerable<Index2i> pairs)
        {
            ulong h = 14695981039346656037UL;
            foreach (Index2i p in pairs) {
                h = (h ^ (uint)p.a) * 1099511628211UL;
                h = (h ^ (uint)p.b) * 1099511628211UL;
            }
            return h;
        }

        static List<Index2i> ListVersion(TriangleBoxTree tree, int minTasks)
        {
            var all = new List<Index2i>();
            foreach (Index2i task in tree.SelfPairTasks(minTasks)) {
                var l = new List<Index2i>();
                tree.SelfPairs(task, l);
                all.AddRange(l);
            }
            return all;
        }

        static List<Index2i> VisitVersion(TriangleBoxTree tree, int minTasks)
        {
            var all = new List<Index2i>();
            foreach (Index2i task in tree.SelfPairTasks(minTasks))
                tree.SelfPairs(task, (i, j) => { all.Add(new Index2i(i, j)); return true; });
            return all;
        }

        public static IEnumerable<object[]> Inputs()
        {
            // name, boxes, minTasks, pairs, pinned hash
            yield return new object[] { "sphere 20 (median split)", 20, 0, 256, PINNED_SPHERE_20_COUNT, PINNED_SPHERE_20 };
            yield return new object[] { "sphere 60 (Morton)", 60, 0, 256, PINNED_SPHERE_60_COUNT, PINNED_SPHERE_60 };
            yield return new object[] { "random 3000 boxes, 1 task", 3000, 1, 1, PINNED_R3000_COUNT, PINNED_R3000 };
            yield return new object[] { "random 9000 boxes (Morton), 64 tasks", 9000, 2, 64, PINNED_R9000_COUNT, PINNED_R9000 };
        }

        const int PINNED_SPHERE_20_COUNT = 27170, PINNED_SPHERE_60_COUNT = 258914, PINNED_R3000_COUNT = 538, PINNED_R9000_COUNT = 4979;
        const ulong PINNED_SPHERE_20 = 7937513078983382269UL, PINNED_SPHERE_60 = 7483684505465745745UL, PINNED_R3000 = 14065936367266205964UL, PINNED_R9000 = 13934730575883885722UL;

        static List<AxisAlignedBox3d> Make(int size, int kind) => kind == 0 ? SphereBoxes(size) : RandomBoxes(size, kind * 1000 + size);

        [Theory]
        [MemberData(nameof(Inputs))]
        public void ListVersion_PinnedSequence_AndVisitVersionHandsOverTheSame(string name, int size, int kind, int minTasks, int count, ulong hash)
        {
            var tree = new TriangleBoxTree(Make(size, kind));
            List<Index2i> list = ListVersion(tree, minTasks);
            Console.WriteLine($"{name}: {list.Count} pairs, hash {Hash(list)}");
            Assert.Equal(count, list.Count);
            Assert.Equal(hash, Hash(list));
            Assert.True(list.SequenceEqual(VisitVersion(tree, minTasks)), name);
        }

        [Fact]
        public void VisitVersion_StopsWhereVisitReturnsFalse_ThePrefixOfTheFullSequence()
        {
            var tree = new TriangleBoxTree(SphereBoxes(20));
            foreach (Index2i task in tree.SelfPairTasks(16)) {
                var full = new List<Index2i>();
                tree.SelfPairs(task, full);
                for (int stopAfter = 1; stopAfter <= Math.Min(3, full.Count); ++stopAfter) {
                    var part = new List<Index2i>();
                    tree.SelfPairs(task, (i, j) => { part.Add(new Index2i(i, j)); return part.Count < stopAfter; });
                    Assert.True(part.SequenceEqual(full.Take(stopAfter)));
                }
            }
        }
    }
}
