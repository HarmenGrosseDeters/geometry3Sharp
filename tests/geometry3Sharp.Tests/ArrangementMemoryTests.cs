using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Exact Boolean 3.0 T2 (memory of the arrangement): what the arrangement keeps alive after a phase is over. The
    /// results do not change; these tests pin that the memory is released and that the structures still answer.
    /// </summary>
    public class ArrangementMemoryTests
    {
        static List<AxisAlignedBox3d> Boxes(int count, int seed)
        {
            var rand = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>(count);
            for (int i = 0; i < count; ++i) {
                var c = new Vector3d(rand.NextDouble() * 100, rand.NextDouble() * 100, rand.NextDouble() * 100);
                var b = new AxisAlignedBox3d(c, c);
                b.Contain(c + new Vector3d(rand.NextDouble() * 3, rand.NextDouble() * 3, rand.NextDouble() * 3));
                boxes.Add(b);
            }
            return boxes;
        }

        // (the tree's overlap is closed: touching boxes overlap)
        static bool Overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y && a.Min.z <= b.Max.z && b.Min.z <= a.Max.z;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static TriangleBoxTree Build(List<AxisAlignedBox3d> boxes, out WeakReference listRef)
        {
            listRef = new WeakReference(boxes);
            return new TriangleBoxTree(boxes);
        }

        // (in a method of its own: the list must be unreachable from this frame, and the lambda's closure would hold it)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static TriangleBoxTree BuildWithExpected(int count, out WeakReference listRef, out List<AxisAlignedBox3d> queries, out List<int>[] expected)
        {
            List<AxisAlignedBox3d> boxes = Boxes(count, 7);
            queries = Boxes(40, 8);
            var exp = new List<int>[queries.Count];
            for (int k = 0; k < exp.Length; ++k) {
                exp[k] = new List<int>();
                for (int i = 0; i < boxes.Count; ++i)
                    if (Overlap(boxes[i], queries[k])) exp[k].Add(i);
            }
            expected = exp;
            return Build(boxes, out listRef);
        }

        // below MedianSplitBelow (4096 boxes) the tree takes the median split (sort keys), above it the Morton order
        [Theory]
        [InlineData(300)]
        [InlineData(6000)]
        public void BoxTree_ReleasesItsInputListAfterTheBuild_AndStillAnswers(int count)
        {
            WeakReference listRef;
            List<AxisAlignedBox3d> queries;
            List<int>[] expected;
            TriangleBoxTree tree = BuildWithExpected(count, out listRef, out queries, out expected);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(listRef.IsAlive, "the tree keeps the list of boxes it was built from");
            for (int k = 0; k < queries.Count; ++k) {
                var found = new List<int>();
                tree.Query(queries[k], (i) => found.Add(i));
                found.Sort();
                Assert.Equal(expected[k], found);
            }
            GC.KeepAlive(tree);
        }

        static DMesh3 Sphere(Vector3d shift)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, m.GetVertex(v) + shift);
            return m;
        }

        [Fact]
        public void Boolean_ReleasesTheBoxTreesAfterTheClassification_AndComputesTwiceTheSame()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.6, 0.2, 0.1));
            var boolean = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            Assert.Null(boolean.Arrangement.Trees);
            DMesh3 first = boolean.Result;
            Assert.True(boolean.Compute());
            Assert.Null(boolean.Arrangement.Trees);
            Assert.Equal("", Invariance.GeometricDifference(first, boolean.Result) ?? "");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result) ?? "");
        }
    }
}
