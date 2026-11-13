using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The environment adversary for the schedule (docs/design/DETERMINISM_PROOF.md, option B). Under
    /// gParallel.SerialOnThisThread the library's parallel loops run their items one after the other, and
    /// gParallel.SerialOrderOnThisThread chooses the order: as given, backwards, or a seeded permutation of every loop.
    /// A result that depends on the schedule (the order of appends to a shared list, ids handed out by a counter, a
    /// reduction) changes with that order; these tests ask the operations for the same bits under every order.
    /// A repeated parallel run samples the schedules the machine happens to produce; this explores orders it never does.
    /// </summary>
    public class OrderAdversaryTests
    {
        static readonly int[] Orders = { -1, 1, 2, 3 };

        // f under the loop order (0: as given), serially on this thread
        static T WithOrder<T>(int order, Func<T> f)
        {
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            gParallel.SerialOrderOnThisThread = order;
            try {
                return f();
            } finally {
                gParallel.SerialOrderOnThisThread = 0;
                gParallel.SerialOnThisThread = serial;
            }
        }

        // the mesh of an operation is the same, bit for bit and id for id, for every loop order
        static DMesh3 AssertOrderIndependent(Func<DMesh3> run)
        {
            DMesh3 reference = WithOrder(0, run);
            foreach (int order in Orders) {
                string diff = Invariance.Difference(reference, WithOrder(order, run));
                Assert.True(diff == "", $"loop order {order}: {diff}");
            }
            return reference;
        }

        // ---------------------------------------------------------------- the hook

        static List<int> Visited(int order, int count, int loops = 1)
        {
            return WithOrder(order, () => {
                var seen = new List<int>();
                for (int l = 0; l < loops; ++l)
                    gParallel.ForRange(count, (i) => seen.Add(i));
                return seen;
            });
        }

        [Fact]
        public void ForRange_OrderZero_AsGiven_MinusOne_Backwards()
        {
            Assert.Equal(Enumerable.Range(0, 50), Visited(0, 50));
            Assert.Equal(Enumerable.Range(0, 50).Reverse(), Visited(-1, 50));
            Assert.Equal(new[] { 0 }, Visited(-1, 1));
            Assert.Empty(Visited(-1, 0));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(77)]
        public void ForRange_Seed_PermutesEveryLoop_Repeatably(int seed)
        {
            const int n = 200;
            List<int> two = Visited(seed, n, 2);
            List<int> first = two.Take(n).ToList(), second = two.Skip(n).ToList();
            // every item once per loop
            Assert.Equal(Enumerable.Range(0, n), first.OrderBy(x => x));
            Assert.Equal(Enumerable.Range(0, n), second.OrderBy(x => x));
            // a real permutation, another one for the next loop, and not the reversal
            Assert.NotEqual(Enumerable.Range(0, n), first);
            Assert.NotEqual(Enumerable.Range(0, n).Reverse(), first);
            Assert.NotEqual(first, second);
            // far from the given order: most items moved
            Assert.True(Enumerable.Range(0, n).Count(i => first[i] != i) > n / 2);
            // the same orders when the seed is set again (also right after itself), other orders for another seed
            Assert.Equal(two, Visited(seed, n, 2));
            Assert.NotEqual(first, Visited(seed + 1, n));
        }

        [Fact]
        public void ForRangeWithLocal_And_ForEach_FollowTheOrder()
        {
            var seen = WithOrder(-1, () => {
                var s = new List<int>();
                int locals = 0;
                gParallel.ForRange(20, () => { locals++; return new List<int>(); }, (i, local) => { local.Add(i); s.Add(i); });
                Assert.Equal(1, locals);
                return s;
            });
            Assert.Equal(Enumerable.Range(0, 20).Reverse(), seen);

            var items = new[] { "a", "b", "c", "d", "e" };
            Assert.Equal(Enumerable.Reverse(items), WithOrder(-1, () => { var s = new List<string>(); gParallel.ForEach(items, (x) => s.Add(x)); return s; }));
            Assert.Equal(items, WithOrder(0, () => { var s = new List<string>(); gParallel.ForEach(items, (x) => s.Add(x)); return s; }));
            var permuted = WithOrder(5, () => { var s = new List<int>(); gParallel.ForEach(Enumerable.Range(0, 100), (x) => s.Add(x)); return s; });
            Assert.Equal(Enumerable.Range(0, 100), permuted.OrderBy(x => x));
            Assert.NotEqual(Enumerable.Range(0, 100), permuted);
            // BlockStartEnd goes through ForEach: its blocks backwards, the rest last
            var blocks = WithOrder(-1, () => { var s = new List<int>(); gParallel.BlockStartEnd(0, 34, (a, b) => s.Add(a), 10); return s; });
            Assert.Equal(new[] { 20, 10, 0, 30 }, blocks);
        }

        [Fact]
        public void WithoutSerial_TheOrderHasNoEffect_AndEveryItemRunsOnce()
        {
            // the order belongs to the serial switch; a parallel loop ignores it
            gParallel.SerialOrderOnThisThread = -1;
            try {
                Assert.False(gParallel.SerialOnThisThread);
                var hits = new int[1000];
                gParallel.ForRange(hits.Length, (i) => System.Threading.Interlocked.Increment(ref hits[i]));
                Assert.All(hits, h => Assert.Equal(1, h));
                var bag = new System.Collections.Concurrent.ConcurrentBag<int>();
                gParallel.ForEach(Enumerable.Range(0, 1000), (i) => bag.Add(i));
                Assert.Equal(Enumerable.Range(0, 1000), bag.OrderBy(x => x));
            } finally {
                gParallel.SerialOrderOnThisThread = 0;
            }
        }

        [Fact]
        public void TheAdversaryDetects_ASharedAppend_ACounterId_AndAFloatingPointReduction()
        {
            // the three schedule dependences of DETERMINISM_PROOF.md (R12, R13), each caught by every order
            double[] values = Enumerable.Range(0, 300).Select(i => 1.0 / (i + 1) * ((i % 3 == 0) ? 1e10 : 1)).ToArray();
            Func<string> append = () => { var s = new List<int>(); gParallel.ForRange(40, (i) => { lock (s) s.Add(i); }); return string.Join(",", s); };
            Func<string> ids = () => {
                int next = 0; var id = new int[40];
                gParallel.ForRange(40, (i) => id[i] = System.Threading.Interlocked.Increment(ref next));
                return string.Join(",", id);
            };
            Func<string> sum = () => {
                double total = 0; object gate = new object();
                gParallel.ForRange(values.Length, (i) => { lock (gate) total += values[i]; });
                return BitConverter.DoubleToInt64Bits(total).ToString("x16");
            };
            foreach (Func<string> dependent in new[] { append, ids, sum }) {
                string reference = WithOrder(0, dependent);
                foreach (int order in Orders)
                    Assert.NotEqual(reference, WithOrder(order, dependent));
            }
            // and passes what does not depend on it: own slots, an integer sum, a sort by content afterwards
            Func<string> independent = () => {
                var slot = new int[40]; long count = 0; var s = new List<int>();
                gParallel.ForRange(40, (i) => { slot[i] = i * i; System.Threading.Interlocked.Add(ref count, i); lock (s) s.Add(i % 7); });
                s.Sort();
                return string.Join(",", slot) + "|" + count + "|" + string.Join(",", s);
            };
            string expected = WithOrder(0, independent);
            foreach (int order in Orders)
                Assert.Equal(expected, WithOrder(order, independent));
        }

        // ---------------------------------------------------------------- operations

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

        // a sphere with bumps, from + - * / only
        static DMesh3 Bumpy(int n = 14)
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1, n);
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.2 * (p.x * p.y - 0.4 * p.z * p.z * p.x + 0.3 * p.y * p.y * p.z)));
            }
            return m;
        }

        static DMesh3 Boolean(Func<ExactMeshBoolean> make)
        {
            ExactMeshBoolean b = make();
            Assert.True(b.Compute(), b.Error);
            return b.Result;
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void ExactBoolean_BoxAndSphere_AnyLoopOrder_BitIdentical(ExactMeshBoolean.Operations op)
        {
            // flat regions (simplified, Lawson flips) and curved ones; the intersection points get their ids in the order
            // of the pair loop
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6);
            DMesh3 result = AssertOrderIndependent(() => Boolean(() => new ExactMeshBoolean(box, sphere, op)));
            ResultChecks.AssertClosed(result);
        }

        [Fact]
        public void ExactBoolean_SafeRounding_AnyLoopOrder_BitIdentical()
        {
            DMesh3 a = Bumpy(8), b = Sphere(new Vector3d(0.7, 0.2, 0.1), 0.9, 7);
            DMesh3 result = AssertOrderIndependent(() => {
                var boolean = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { SafeRounding = true };
                Assert.True(boolean.Compute(), boolean.Error);
                Assert.True(boolean.RoundingClean);
                return boolean.Result;
            });
            ResultChecks.AssertClean(result);
        }

        [Fact]
        public void ExactBoolean_CoplanarBoxes_AnyLoopOrder_BitIdentical()
        {
            // n-ary, coplanar faces: merged points, stacks of coplanar pieces, many points inside flat regions
            var meshes = new List<DMesh3>();
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j)
                    for (int k = 0; k < 2; ++k)
                        meshes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0.9 * k), new Vector3d(0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1)));
            DMesh3 result = AssertOrderIndependent(() => Boolean(() => new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union)));
            ResultChecks.AssertClosed(result);
            Assert.Equal(1.9 * 1.9 * 1.9, ResultChecks.Volume(result), 12);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExactBoolean_TouchingAlongEdges_AnyLoopOrder_BitIdentical(bool simplify)
        {
            // non-manifold edges split per pair at new vertices (the fans around a vertex: DETERMINISM_PROOF.md R8)
            var meshes = new List<DMesh3>() { Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)), Box(new Vector3d(1, 1, -0.2), new Vector3d(2, 2, 1.3)),
                Box(new Vector3d(-1, -1, -0.3), new Vector3d(0, 0, 1.2)) };
            for (int k = 0; k < 3; ++k) {
                meshes.Add(Sphere(new Vector3d(1.05, 0.93, 0.2 + 0.3 * k), 0.15, 5));
                meshes.Add(Sphere(new Vector3d(-0.04, 0.06, 0.25 + 0.3 * k), 0.13, 5));
            }
            int split = 0;
            DMesh3 result = AssertOrderIndependent(() => {
                var b = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union) { SimplifyFlatRegions = simplify };
                Assert.True(b.Compute(), b.Error);
                split = b.SplitNonManifoldEdges;
                return b.Result;
            });
            Assert.True(split > 2);
            Assert.True(result.TriangleCount > 0 && Invariance.AllFinite(result));
        }

        [Fact]
        public void ExactBoolean_SelfIntersectingAndOpenInputs_AnyLoopOrder_BitIdentical()
        {
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 twisted = Sphere(new Vector3d(0, 0, 0), 1, 8);
            MeshEditor.Append(twisted, Sphere(new Vector3d(0.6, 0.2, 0.1), 0.8, 7));
            DMesh3 union = AssertOrderIndependent(() => Boolean(() => new ExactMeshBoolean(twisted, box, ExactMeshBoolean.Operations.Union)));
            ResultChecks.AssertClosed(union);
            // an open input: generalized winding numbers
            DMesh3 open = Sphere(new Vector3d(-0.2, 0.1, 0), 0.9, 7);
            open.RemoveTriangle(open.TriangleIndices().First());
            DMesh3 cut = AssertOrderIndependent(() => Boolean(() => new ExactMeshBoolean(open, box, ExactMeshBoolean.Operations.Intersection)));
            Assert.True(cut.TriangleCount > 0 && Invariance.AllFinite(cut));
        }

        [Fact]
        public void RobustRepair_AnyLoopOrder_BitIdentical()
        {
            DMesh3 result = AssertOrderIndependent(() => {
                DMesh3 m = Bumpy();
                MeshEditor.Append(m, Sphere(new Vector3d(1.0, 0.3, 0.1), 0.8, 10));
                var r = new MeshRobustRepair(m);
                Assert.True(r.Apply());
                Assert.True(r.UsedExactResolve, r.ExactResolveReason);
                return r.Mesh;
            });
            ResultChecks.AssertClean(result);
        }

        static BoundedImplicitFunction3d Blob() => new ImplicitSmoothUnion3d() {
            A = new ImplicitSphere3d() { Origin = new Vector3d(0.1, -0.2, 0.05), Radius = 0.9 },
            B = new ImplicitSphere3d() { Origin = new Vector3d(0.8, 0.5, 0.3), Radius = 0.6 }
        };

        [Fact]
        public void Contouring_AnyLoopOrder_BitIdentical()
        {
            DMesh3 mc = AssertOrderIndependent(() => {
                var c = new MarchingCubes() { Implicit = Blob(), Bounds = Blob().Bounds().Expanded(0.2), CubeSize = 0.07, ParallelCompute = true };
                c.Generate();
                return c.Mesh;
            });
            Assert.True(mc.TriangleCount > 500 && Invariance.AllFinite(mc));
            DMesh3 dc = AssertOrderIndependent(() => {
                var c = new DualContouring() { Implicit = Blob(), IsoValue = 0, InsideIsLess = true, CellSize = 0.07, Bounds = Blob().Bounds().Expanded(0.2), ParallelCompute = true };
                c.Generate();
                return c.Mesh;
            });
            Assert.True(dc.TriangleCount > 500 && Invariance.AllFinite(dc));
        }

        [Fact]
        public void RemesherAndReducer_AnyLoopOrder_BitIdentical()
        {
            DMesh3 reduced = AssertOrderIndependent(() => {
                DMesh3 m = Bumpy();
                new Reducer(m).ReduceToTriangleCount(400);
                return m;
            });
            Assert.Equal(400, reduced.TriangleCount);
            DMesh3 remeshed = AssertOrderIndependent(() => {
                DMesh3 m = Bumpy(10);
                var r = new Remesher(m) { SmoothSpeedT = 0.5 };
                r.SetTargetEdgeLength(0.12);
                for (int k = 0; k < 4; ++k) r.BasicRemeshPass();
                return m;
            });
            Assert.True(remeshed.TriangleCount > 100 && Invariance.AllFinite(remeshed) && remeshed.IsClosed());
        }
    }
}
