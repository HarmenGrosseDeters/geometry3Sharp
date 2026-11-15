using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Lever 4 of the boolean extreme-runtime study (fewer allocations in L2's tracing, bit-identical by design): the local
    /// frame's corners without the exact point, the struct visitors of the box tree and the crossing grid, and the
    /// projection query on a prefix of the caller's array. Each against the code path it replaces.
    /// </summary>
    public class ExactMeshBooleanTestsLever4
    {
        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Corner_IsPointOfTheExactPoint_BitForBit(string name)
        {
            // every corner of a sample of input triangles in the frames of a sample of pieces' triangles: the fast path
            // (integer points with double coordinates, exact differences) gives Point(Exact(id)) bit for bit, under every
            // similarity (utm: grid coordinates far from the origin)
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsLocalFilter.NearCopy(16, 1e-12);
            a = Invariance.Transformed(a, sim); b = Invariance.Transformed(b, sim);
            var s = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(s.Compute(), s.Error);
            var arr = s.Arrangement;
            int checks = 0, doubles = 0, onFast = 0;
            for (int i = 0; i < arr.Output.Count; i += 37) {
                var frame = new ExactMeshBoolean.LocalFrame(s, arr.Triangles[arr.Output[i].Source]);
                for (int t = 0; t < arr.Triangles.Count; t += 11) {
                    if (arr.Triangles[t].Degenerate)
                        continue;
                    Index3i c = arr.Triangles[t].Corners;
                    foreach (int id in new[] { c.a, c.b, c.c }) {
                        Vector3d fast = frame.corner_point(id), plain = frame.Point(arr.Exact(id));
                        for (int k = 0; k < 3; ++k)
                            Assert.Equal(Bits(plain[k]), Bits(fast[k]));
                        Assert.Equal(Bits(plain.x), Bits(frame.Corner(id).x));
                        // (the fast path's conditions: an integer point with double coordinates, exact differences)
                        Vector3d d, o = frame.Origin;
                        if (arr.TryExactDouble(id, out d)) {
                            doubles++;
                            if (ExactMeshBoolean.LocalFrame.exact_difference(d.x, o.x, out _) && ExactMeshBoolean.LocalFrame.exact_difference(d.y, o.y, out _)
                                && ExactMeshBoolean.LocalFrame.exact_difference(d.z, o.z, out _))
                                onFast++;
                        }
                        checks++;
                    }
                }
            }
            Assert.True(checks > 500 && onFast > checks / 20, $"{checks} checks, {doubles} double points, {onFast} on the fast path");
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Difference)]
        public void CrossSteps_SkipDegenerateTrianglesOfTheTree(Ops op)
        {
            // the sheet and cap of the cross-carry tests with zero-area triangles on the sheet (corners at two positions,
            // welded to one: the arrangement marks them degenerate), where the steps across the sheet's edges query its
            // box tree: they are skipped as in the full evaluation, and the result is that without them
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsCrossCarry.SheetAndCap();
            DMesh3 withFlat = new DMesh3(a);
            int added = 0;
            foreach (int t in a.TriangleIndices().ToList()) {
                if (t % 9 != 4)
                    continue;
                Index3i tv = a.GetTriangle(t);
                Vector3d p = a.GetVertex(tv.a), q = a.GetVertex(tv.b);
                int i = withFlat.AppendVertex(p), j = withFlat.AppendVertex(q), k = withFlat.AppendVertex(p);
                withFlat.AppendTriangle(i, j, k);
                added++;
            }
            Assert.True(added > 50);
            var plain = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { AllowOpenMeshes = true, TracedAcrossEdges = true, PerPatchOpenWinding = false };
            Assert.True(plain.Compute(), plain.Error);
            var flat = new ExactMeshBoolean(withFlat, new DMesh3(b), op) { AllowOpenMeshes = true, TracedAcrossEdges = true, PerPatchOpenWinding = false };
            Assert.True(flat.Compute(), flat.Error);
            Assert.True(flat.Counts["l2_cross_steps"] > 500, $"{flat.Counts["l2_cross_steps"]} cross steps");
            Assert.True(Invariance.AllFinite(flat.Result));
            Assert.Equal("", Invariance.Difference(plain.Result, flat.Result));
        }

        [Fact]
        public void ExactDifference_TrueOnlyWhereTheDoubleIsTheExactDifference()
        {
            double s;
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(5, 3, out s) && s == 2);
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(3, 5, out s) && s == -2);
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(7, 7, out s) && Bits(s) == 0);   // +0, as ToDouble's 0
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(9007199254740992, -9007199254740992, out s) && s == 18014398509481984);
            // (2^53 + 2 - 1 and 2^60 - 1 are not doubles; 2^60 - 2^8 is)
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(9007199254740994, 1, out s));
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(1152921504606846976, 1, out s));
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(1152921504606846976, 256, out s) && s == 1152921504606846720);
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(double.MaxValue, -double.MaxValue, out s));
            // against the exact difference on random integers of every size up to 2^70
            var r = new Random(4);
            int exact = 0, inexact = 0;
            for (int n = 0; n < 20000; ++n) {
                double x = Math.Round(Math.Pow(2, 70 * r.NextDouble())) * (r.Next(2) * 2 - 1);
                double y = Math.Round(Math.Pow(2, 70 * r.NextDouble())) * (r.Next(2) * 2 - 1);
                BigInteger d = new BigInteger(x) - new BigInteger(y);
                bool ok = ExactMeshBoolean.LocalFrame.exact_difference(x, y, out s);
                Assert.Equal(new BigInteger(s) == d, ok);
                if (ok) exact++; else inexact++;
            }
            Assert.True(exact > 1000 && inexact > 1000, $"{exact} exact, {inexact} not");
        }

        [Fact]
        public void GridCoordOf_IsGridCoordOfTheInteger()
        {
            foreach (double v in new[] { 0, 1, -1, 3, 4503599627370497, -9007199254740992, 1e300, ExactMeshBoolean.TinyCoordinate, ExactMeshBoolean.TinyCoordinate / 2 }) {
                double plain = v == 0 || Math.Abs(v) >= 1 ? ExactMeshBoolean.grid_coord(new BigInteger(v), BigInteger.One) : double.NaN;
                if (Math.Abs(v) < 1 && v != 0)
                    // (not an integer: the rule alone, NaN below TinyCoordinate)
                    plain = Math.Abs(v) >= ExactMeshBoolean.TinyCoordinate ? v : double.NaN;
                Assert.Equal(Bits(plain), Bits(ExactMeshBoolean.LocalFrame.grid_coord_of(v)));
            }
        }

        struct Collect : TriangleBoxTree.IIndexVisitor
        {
            public List<int> Found;
            public void Visit(int i) { Found.Add(i); }
        }

        [Fact]
        public void QuerySegment_StructVisitor_SameIndicesInTheSameOrder()
        {
            var r = new Random(7);
            var boxes = new List<AxisAlignedBox3d>();
            for (int n = 0; n < 3000; ++n) {
                var c = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                boxes.Add(new AxisAlignedBox3d(c, 0.002 + 0.02 * r.NextDouble()));
            }
            var tree = new TriangleBoxTree(boxes);
            int nonEmpty = 0;
            for (int q = 0; q < 300; ++q) {
                Vector3d p0 = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()), p1 = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                double pad = r.Next(3) == 0 ? 0 : 0.01 * r.NextDouble();
                var viaAction = new List<int>();
                tree.QuerySegment(p0, p1, pad, viaAction.Add);
                var v = new Collect { Found = new List<int>() };
                tree.QuerySegment(p0, p1, pad, ref v);
                Assert.Equal(viaAction, v.Found);
                // (against the boxes themselves: every box that meets the padded segment is found)
                foreach (int k in v.Found)
                    Assert.InRange(k, 0, boxes.Count - 1);
                if (v.Found.Count > 0) nonEmpty++;
            }
            Assert.True(nonEmpty > 200);
        }

        [Fact]
        public void SegmentProjection_QueryOfAPrefix_IsTheQueryOfItsCopy()
        {
            var r = new Random(11);
            var segs = new List<Vector3d>();
            for (int s = 0; s < 400; ++s) {
                var a = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                segs.Add(a); segs.Add(a + 0.02 * new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()));
            }
            var pr = new ExactMeshBoolean.SegmentProjection(segs.ToArray(), new Vector3d(4000, 100, 50));
            Assert.True(pr.Valid);
            int found = 0;
            for (int q = 0; q < 200; ++q) {
                int n = 3 + r.Next(4);
                var v = new Vector3d[n + 1];
                Vector3d c = segs[r.Next(segs.Count)];
                for (int k = 0; k < n; ++k)
                    v[k] = c + 0.01 * new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                // (the last entry, as the far point of the caller's array, beyond the plane of R: Query of all of v declines)
                v[n] = new Vector3d(5000, 0, 0);
                int[] prefix = pr.Query(v, n), copy = pr.Query(v.Take(n).ToArray());
                Assert.Equal(copy, prefix);
                Assert.Null(pr.Query(v));
                if (prefix != null && prefix.Length > 0) found++;
            }
            Assert.True(found > 50, $"{found} queries found segments");
        }

#if NET
        [Fact]
        public void TheReplacedPaths_AllocateNothing()
        {
            // the struct visitor's query, and the fast path of a frame's corner, after their first calls (the box tree's
            // per-thread stack, a corner's cache entry)
            var r = new Random(5);
            var boxes = new List<AxisAlignedBox3d>();
            for (int n = 0; n < 2000; ++n)
                boxes.Add(new AxisAlignedBox3d(new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()), 0.01));
            var tree = new TriangleBoxTree(boxes);
            var v = new CountOnly();
            tree.QuerySegment(Vector3d.Zero, Vector3d.One, 0.01, ref v);
            // (each query on its own: the runtime's tier-up may allocate on this thread between them)
            long most = 0;
            for (int q = 0; q < 100; ++q) {
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                tree.QuerySegment(new Vector3d(0, 0.01 * q, 0), Vector3d.One, 0.01, ref v);
                most = Math.Max(most, GC.GetAllocatedBytesForCurrentThread() - b0);
            }
            Assert.Equal(0, most);
            Assert.True(v.N > 100);

            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsLocalFilter.NearCopy(16, 1e-12);
            var s = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(s.Compute(), s.Error);
            var arr = s.Arrangement;
            var frame = new ExactMeshBoolean.LocalFrame(s, arr.Triangles[arr.Output[0].Source]);
            // (the ids on the fast path: double coordinates whose differences from the origin are exact)
            var ids = new List<int>();
            Vector3d o = frame.Origin;
            for (int t = 0; t < arr.Triangles.Count && ids.Count < 200; ++t) {
                int id = arr.Triangles[t].Corners.a;
                if (arr.Triangles[t].Degenerate || arr.TryExactDouble(id, out Vector3d d) == false)
                    continue;
                if (ExactMeshBoolean.LocalFrame.exact_difference(d.x, o.x, out _) && ExactMeshBoolean.LocalFrame.exact_difference(d.y, o.y, out _)
                    && ExactMeshBoolean.LocalFrame.exact_difference(d.z, o.z, out _))
                    ids.Add(id);
            }
            Assert.True(ids.Count > 50, $"{ids.Count} ids on the fast path");
            frame.corner_point(ids[0]);
            double sum = 0;
            most = 0;
            foreach (int id in ids) {
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                sum += frame.corner_point(id).x;
                most = Math.Max(most, GC.GetAllocatedBytesForCurrentThread() - b0);
            }
            Assert.Equal(0, most);
            Assert.False(double.IsNaN(sum));
        }

        struct CountOnly : TriangleBoxTree.IIndexVisitor
        {
            public int N;
            public void Visit(int i) { N++; }
        }
#endif
    }
}
