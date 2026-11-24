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
                        Vector3d cached = frame.Corner(id);
                        for (int k = 0; k < 3; ++k)
                            Assert.Equal(Bits(plain[k]), Bits(cached[k]));
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

        // the open winding per piece, as a fingerprint (piece, then its sparse pairs mesh, winding)
        static string OpenWindingHash(ExactMeshBoolean s)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.OpenWindingPerPiece.Length; ++i) {
                sb.Append(i).Append(':');
                foreach (int w in s.OpenWindingPerPiece[i] ?? new int[0])
                    sb.Append(w).Append(',');
                sb.Append(';');
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }

        // the vertices on the grid of 2^-20: the inputs come from Math.Sin and Math.Cos, which the runtimes need not round
        // alike (net48 on Windows), and a hash pinned bit for bit must not depend on them
        static DMesh3 Snapped(DMesh3 m)
        {
            var o = new DMesh3(m);
            foreach (int vid in o.VertexIndices()) {
                Vector3d v = o.GetVertex(vid);
                o.SetVertex(vid, new Vector3d(Math.Round(v.x * 1048576) / 1048576, Math.Round(v.y * 1048576) / 1048576, Math.Round(v.z * 1048576) / 1048576));
            }
            return o;
        }

        [Theory]
        [InlineData("plain", Ops.Union, "B60EBCDD65C96BB7", "3DE37721ED0BB6B0", 7790, 4252, 11663, 246)]
        [InlineData("plain", Ops.Intersection, "E96FD9D21F2BCBFA", "3DE37721ED0BB6B0", 7790, 4252, 11663, 246)]
        [InlineData("plain", Ops.Difference, "6AA0082A1075F655", "3DE37721ED0BB6B0", 7790, 4252, 11663, 246)]
        [InlineData("swapped", Ops.Union, "B60EBCDD65C96BB7", "EAB2057DB13D376E", 7790, 4252, 11663, 246)]
        [InlineData("swapped", Ops.Difference, "898D571FB8AD9814", "EAB2057DB13D376E", 7790, 4252, 11663, 246)]
        [InlineData("steep", Ops.Union, "F5C25180922C2E0C", "44128D8F3C04F3FC", 7786, 4384, 11804, 248)]
        [InlineData("steep", Ops.Intersection, "DEE905A9A3749608", "44128D8F3C04F3FC", 7786, 4384, 11804, 248)]
        [InlineData("fold", Ops.Union, "14C2CE177FE29753", "73DC4763F1C8347C", 48, 0, 80, 19)]
        [InlineData("fold", Ops.Difference, "14C2CE177FE29753", "73DC4763F1C8347C", 48, 0, 80, 19)]
        public void CrossSteps_SameResultAsBeforeLever4_Pinned(string variant, Ops op, string result, string winding, long steps, long carried,
            long candidates, long direct)
        {
            // lever 4 is bit-identical by design: procedural open inputs (Snapped) on the path that queries the box tree from
            // steps across input-triangle edges (CrossVisit with Tris), the result mesh, the open winding of every piece and
            // the counts as the code before lever 4 gave them (master 079e8258, computed there). "plain" is the sheet and cap
            // of the cross-carry tests, "swapped" makes the sheet the second mesh, "steep" folds it more, and in "fold"
            // (FoldAndPlate) the steps across the crease cross the plate, the second mesh, whose box tree indices differ from
            // the input triangles' (there Union and Difference keep the same faces: the open winding and the counts tell
            // them apart). A visitor that lost a crossing (Crossings), a candidate (Found) or the map of the tree's indices
            // (Tris) changes them. (Dropping the own-triangle crossings survives: on a step across a consistently oriented
            // input edge, the only steps traced, those of the two ends cancel)
            (DMesh3 a, DMesh3 b) = variant == "fold" ? FoldAndPlate(6, 1.0)
                : ExactMeshBooleanTestsCrossCarry.SheetAndCap(40, variant == "steep" ? 0.6 : 0.15);
            a = Snapped(a);
            b = Snapped(b);
            if (variant == "swapped")
                (a, b) = (b, a);
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, TracedAcrossEdges = true, PerPatchOpenWinding = false, RecordOpenWinding = true };
            Assert.True(s.Compute(), s.Error);
            Assert.Equal(result, AlphaWrapManifoldStepTests.Hash(s.Result));
            Assert.Equal(winding, OpenWindingHash(s));
            Assert.Equal(steps, s.Counts["l2_cross_steps"]);
            Assert.Equal(carried, s.Counts["l2_carried"]);
            Assert.Equal(candidates, s.Counts["l2_cross_candidates"]);
            Assert.Equal(direct, s.Counts["l2_direct"]);
            Assert.True(Invariance.AllFinite(s.Result) && s.Result.TriangleCount > 0);
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
            // the integers the fast path gives, against grid_coord of the same integer (its own path), and the rule at
            // TinyCoordinate (2^-300) with written-out values: +0 stays +0, at or above it the value, below it NaN
            foreach (double v in new[] { 0, 1, -1, 3, 4503599627370497, -9007199254740992, 1e300 }) {
                Assert.Equal(Bits(ExactMeshBoolean.grid_coord(new BigInteger(v), BigInteger.One)), Bits(ExactMeshBoolean.LocalFrame.grid_coord_of(v)));
                Assert.Equal(Bits(v), Bits(ExactMeshBoolean.LocalFrame.grid_coord_of(v)));
            }
            double tiny = Math.Pow(2, -300);
            Assert.Equal(tiny, ExactMeshBoolean.TinyCoordinate);
            Assert.Equal(0L, Bits(ExactMeshBoolean.LocalFrame.grid_coord_of(0.0)));
            Assert.Equal(tiny, ExactMeshBoolean.LocalFrame.grid_coord_of(tiny));
            Assert.Equal(-tiny, ExactMeshBoolean.LocalFrame.grid_coord_of(-tiny));
            Assert.Equal(2 * tiny, ExactMeshBoolean.LocalFrame.grid_coord_of(2 * tiny));
            Assert.True(double.IsNaN(ExactMeshBoolean.LocalFrame.grid_coord_of(tiny / 2)));
            Assert.True(double.IsNaN(ExactMeshBoolean.LocalFrame.grid_coord_of(-tiny * 0.75)));
            Assert.True(double.IsNaN(ExactMeshBoolean.LocalFrame.grid_coord_of(double.Epsilon)));
        }

        // the padded segment p-q meets the closed box [lo - pad, hi + pad] (Liang-Barsky clipping, in doubles), its parameter
        // interval per axis widened by slack * (|ta| + |tb| + 1e-3) (0: none; the tree widens by 1e-9 * (|ta| + |tb|) + 1e-12)
        static bool SegmentMeetsBox(Vector3d p, Vector3d q, Vector3d lo, Vector3d hi, double pad, double slack)
        {
            double t0 = 0, t1 = 1;
            for (int k = 0; k < 3; ++k) {
                double d = q[k] - p[k], a = lo[k] - pad, b = hi[k] + pad;
                if (d == 0) {
                    if (p[k] < a || p[k] > b) return false;
                    continue;
                }
                double ta = (a - p[k]) / d, tb = (b - p[k]) / d;
                if (ta > tb) { double x = ta; ta = tb; tb = x; }
                double w = slack * (Math.Abs(ta) + Math.Abs(tb) + 1e-3);
                t0 = Math.Max(t0, ta - w);
                t1 = Math.Min(t1, tb + w);
                if (t0 > t1) return false;
            }
            return true;
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
                // (against the boxes themselves, brute force: every box that the segment meets with the pad less a margin
                // is found, and every box found meets it with the pad plus a margin and twice the tree's widening of the
                // parameter interval: the query is conservative only by that widening against rounding)
                var found = new HashSet<int>(v.Found);
                Assert.Equal(v.Found.Count, found.Count);
                for (int k = 0; k < boxes.Count; ++k) {
                    if (SegmentMeetsBox(p0, p1, boxes[k].Min, boxes[k].Max, pad - 1e-9, 0))
                        Assert.Contains(k, found);
                    if (found.Contains(k))
                        Assert.True(SegmentMeetsBox(p0, p1, boxes[k].Min, boxes[k].Max, pad + 1e-9, 2e-9), $"box {k} found but not met");
                }
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
        /// <summary> an open sheet folded into a V along the y axis (half-angle 15 degrees, faces of width w, n strips along
        /// the crease, each triangle reaching from the crease to the outer edge) and an open plate in the V's bisector plane
        /// x = 0 that touches neither face: a step between the centroids of two triangles across the crease passes through
        /// the plate </summary>
        internal static (DMesh3 fold, DMesh3 plate) FoldAndPlate(int n, double w)
        {
            var fold = new DMesh3();
            double s = Math.Sin(Math.PI / 12), c = Math.Cos(Math.PI / 12);
            var crease = new int[n + 1];
            var left = new int[n + 1];
            var right = new int[n + 1];
            for (int i = 0; i <= n; ++i) {
                double y = (double)i / n;
                crease[i] = fold.AppendVertex(new Vector3d(0, y, 0));
                left[i] = fold.AppendVertex(new Vector3d(-s * w, y, c * w));
                right[i] = fold.AppendVertex(new Vector3d(s * w, y, c * w));
            }
            for (int i = 0; i < n; ++i) {
                fold.AppendTriangle(crease[i], left[i], left[i + 1]);
                fold.AppendTriangle(crease[i], left[i + 1], crease[i + 1]);
                fold.AppendTriangle(crease[i], crease[i + 1], right[i + 1]);
                fold.AppendTriangle(crease[i], right[i + 1], right[i]);
            }
            var plate = new DMesh3();
            int p0 = plate.AppendVertex(new Vector3d(0, 0.1, 0.1 * w)), p1 = plate.AppendVertex(new Vector3d(0, 0.9, 0.1 * w));
            int p2 = plate.AppendVertex(new Vector3d(0, 0.9, 0.5 * w)), p3 = plate.AppendVertex(new Vector3d(0, 0.1, 0.5 * w));
            plate.AppendTriangle(p0, p1, p2);
            plate.AppendTriangle(p0, p2, p3);
            return (fold, plate);
        }
    }
}
