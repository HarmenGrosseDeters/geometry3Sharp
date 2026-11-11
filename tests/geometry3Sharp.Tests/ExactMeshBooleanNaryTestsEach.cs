using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean.IntersectEach / DifferenceEach (T3b): A ∩ B_i and A − B_i for every body, each the same result, bit
    /// for bit, as a separate boolean, in parallel over the bodies. (The class name keeps the mutation shard's test filter,
    /// FullyQualifiedName~ExactMeshBooleanNaryTests.)
    /// </summary>
    public class ExactMeshBooleanNaryTestsEach
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        // A: a slab 4 x 4 x 1 with subdivided faces (most of it lies outside each body); bodies: random boxes through its
        // top, one wholly inside, one far away, one open (a box with a triangle removed)
        static DMesh3 A()
        {
            DMesh3 a = new GridBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(4, 4, 1))), EdgeVertices = 9 }.Generate().MakeDMesh();
            return a;
        }

        static List<DMesh3> Bodies(int n, int seed)
        {
            var rnd = new Random(seed);
            var list = new List<DMesh3>();
            for (int k = 0; k < n; ++k) {
                double x = 3.5 * rnd.NextDouble(), y = 3.5 * rnd.NextDouble(), z = 0.2 + 0.6 * rnd.NextDouble();
                list.Add(Box(x, y, z, x + 0.2 + 0.6 * rnd.NextDouble(), y + 0.2 + 0.6 * rnd.NextDouble(), z + 0.3 + 1.2 * rnd.NextDouble()));
            }
            list.Add(Box(1.5, 1.5, 0.3, 1.9, 1.9, 0.6));
            list.Add(Box(10, 10, 10, 11, 11, 11));
            DMesh3 open = Box(2.5, 0.5, 0.5, 3.2, 1.1, 1.8);
            open.RemoveTriangle(open.TriangleIndices().Last(), true, false);
            list.Add(open);
            return list;
        }

        static DMesh3 Separate(DMesh3 a, DMesh3 b, Ops op, Action<ExactMeshBoolean> configure = null)
        {
            var s = new ExactMeshBoolean(a, b, op);
            configure?.Invoke(s);
            s.Compute();
            return s.Result;
        }

        [Theory]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Difference)]
        public void EachEqualsSeparateBooleans_BitForBit(Ops op)
        {
            DMesh3 a = A();
            // (more bodies than cores: the parallel path over the bodies; two: one after the other)
            foreach (int n in new[] { Math.Max(2 * Environment.ProcessorCount, 6), 2 }) {
                List<DMesh3> bodies = Bodies(n, 3);
                var status = new Status[bodies.Count];
                DMesh3[] each = op == Ops.Intersection ? ExactMeshBoolean.IntersectEach(a, bodies, null, status) : ExactMeshBoolean.DifferenceEach(a, bodies, null, status);
                Assert.Equal(bodies.Count, each.Length);
                for (int i = 0; i < bodies.Count; ++i) {
                    Assert.Equal(Status.Success, status[i]);
                    Assert.Equal("", Invariance.Difference(Separate(a, bodies[i], op), each[i]));
                }
                // (A ∩ the far box is empty, A − it is all of A)
                int far = bodies.Count - 2;
                if (op == Ops.Intersection)
                    Assert.Equal(0, each[far].TriangleCount);
                else
                    Assert.True(each[far].TriangleCount > 0);
            }
        }

        [Fact]
        public void Configure_AppliesToEveryBoolean()
        {
            DMesh3 a = A();
            List<DMesh3> bodies = Bodies(5, 4);
            Action<ExactMeshBoolean> raw = (b) => { b.SafeRounding = false; b.SimplifyFlatRegions = false; };
            DMesh3[] each = ExactMeshBoolean.IntersectEach(a, bodies, raw);
            for (int i = 0; i < bodies.Count; ++i)
                Assert.Equal("", Invariance.Difference(Separate(a, bodies[i], Ops.Intersection, raw), each[i]));
            // (and it matters: without simplification the results have more triangles)
            DMesh3[] plain = ExactMeshBoolean.IntersectEach(a, bodies);
            Assert.True(Enumerable.Range(0, bodies.Count).Sum(i => each[i].TriangleCount) > Enumerable.Range(0, bodies.Count).Sum(i => plain[i].TriangleCount));
        }

        [Fact]
        public void PreparedA_ReorientedOnce_SameAsSeparate()
        {
            // an open A with a flipped triangle: its orientation is repaired once for all booleans, with the same result
            // as each boolean repairing it on its own
            DMesh3 a = A();
            a.RemoveTriangle(a.TriangleIndices().First(), true, false);
            a.ReverseTriOrientation(a.TriangleIndices().Skip(10).First());
            List<DMesh3> bodies = Bodies(4, 5);
            foreach (Ops op in new[] { Ops.Intersection, Ops.Difference }) {
                DMesh3[] each = op == Ops.Intersection ? ExactMeshBoolean.IntersectEach(a, bodies) : ExactMeshBoolean.DifferenceEach(a, bodies);
                for (int i = 0; i < bodies.Count; ++i)
                    Assert.Equal("", Invariance.Difference(Separate(a, bodies[i], op), each[i]));
            }
        }

        [Fact]
        public void FailingBodies_NullResultAndStatus()
        {
            DMesh3 a = A();
            var nonFinite = Box(1, 1, 0.5, 2, 2, 2);
            nonFinite.SetVertex(0, new Vector3d(double.NaN, 1, 0.5));
            var bodies = new List<DMesh3> { Box(1, 1, 0.5, 2, 2, 2), null, nonFinite };
            var status = new Status[3];
            DMesh3[] each = ExactMeshBoolean.IntersectEach(a, bodies, null, status);
            Assert.Equal(Status.Success, status[0]);
            Assert.NotNull(each[0]);
            Assert.Equal(Status.InvalidInput, status[1]);
            Assert.Null(each[1]);
            Assert.Equal(Status.InvalidInput, status[2]);
            Assert.Null(each[2]);
            // a non-finite A: every boolean fails the same way
            var badA = new DMesh3(a);
            badA.SetVertex(badA.VertexIndices().First(), new Vector3d(double.PositiveInfinity, 0, 0));
            var s2 = new Status[2];
            DMesh3[] none = ExactMeshBoolean.DifferenceEach(badA, bodies.Take(1).Concat(bodies.Take(1)).ToList(), null, s2);
            Assert.All(s2, s => Assert.Equal(Status.InvalidInput, s));
            Assert.All(none, r => Assert.Null(r));
            Assert.Throws<ArgumentNullException>(() => ExactMeshBoolean.IntersectEach(a, null));
            Assert.Throws<ArgumentException>(() => ExactMeshBoolean.IntersectEach(a, bodies, null, new Status[1]));
        }

        [Fact]
        public void SerialEqualsParallel_Repeated_AndSimilarities()
        {
            DMesh3 a = A();
            List<DMesh3> bodies = Bodies(Math.Max(2 * Environment.ProcessorCount, 6), 6);
            DMesh3[] serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = ExactMeshBoolean.IntersectEach(a, bodies);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 2; ++run) {
                DMesh3[] p = ExactMeshBoolean.IntersectEach(a, bodies);
                for (int i = 0; i < bodies.Count; ++i)
                    Assert.Equal("", Invariance.Difference(serial[i], p[i]));
            }
            // under the similarities: each result as the separate boolean of the transformed inputs; the exact ones
            // mapped back give the untransformed results
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 ta = Invariance.Transformed(a, sim);
                List<DMesh3> tb = bodies.Take(4).Select(b => Invariance.Transformed(b, sim)).ToList();
                DMesh3[] each = ExactMeshBoolean.IntersectEach(ta, tb);
                for (int i = 0; i < tb.Count; ++i) {
                    Assert.True(Invariance.Difference(Separate(ta, tb[i], Ops.Intersection), each[i]) == "", $"{sim} body {i}");
                    if (sim.IsExact)
                        Assert.True(Invariance.Difference(serial[i], Invariance.MappedBack(each[i], sim)) == "", $"{sim} body {i}");
                }
            }
        }
    }
}
