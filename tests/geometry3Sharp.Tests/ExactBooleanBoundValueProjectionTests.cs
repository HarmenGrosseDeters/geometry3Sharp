using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1423, part 3: the projected boxes, cells and error bound of SegmentProjection, the thresholds of pierce_prefilter,
    /// the plane test of LocalFrame.InPlane and the grid of CrossingGrid (see ExactBooleanBoundValueTests)
    /// </summary>
    public class ExactBooleanBoundValueProjectionTests
    {
        const double u = 1.1102230246251565e-16;
        const BindingFlags NP = ExactBooleanBoundValueTests.NP;

        static double MaxAbs(Vector3d x) { return Math.Max(Math.Abs(x.x), Math.Max(Math.Abs(x.y), Math.Abs(x.z))); }
        static double L1(Vector3d x) { return Math.Abs(x.x) + Math.Abs(x.y) + Math.Abs(x.z); }

        // ---------------------------------------------------------------------------------------------------------------
        // SegmentProjection
        // ---------------------------------------------------------------------------------------------------------------
        static readonly Vector3d R = new Vector3d(3e15, -9e15, 5e15);       // |y| is the largest: k = 1, i = 2, j = 0

        static Vector3d[] Segments(int seed, int count)
        {
            var rnd = new Random(seed);
            var g = new Vector3d[2 * count];
            // (R has i = 2, j = 0: some segments vary more along z, others more along x, so that either error term of Project decides)
            for (int s = 0; s < g.Length; ++s) {
                double ax = (s / 2) % 2 == 0 ? 2e12 : 2e14, az = (s / 2) % 2 == 0 ? 2e14 : 2e12;
                g[s] = new Vector3d(Math.Round((rnd.NextDouble() - 0.5) * ax), Math.Round((rnd.NextDouble() - 0.5) * 2e12), Math.Round((rnd.NextDouble() - 0.5) * az));
            }
            return g;
        }

        /// <summary> the projection and its error bound, documented formula </summary>
        static void RefProject(Vector3d x, Vector3d r, int k, out double px, out double py, out double pe)
        {
            int i = (k + 1) % 3, j = (k + 2) % 3;
            double ex = 2.3e-16 * MaxAbs(x), er = 2.3e-16 * MaxAbs(r);
            double d = x[k] - r[k], ni = x[i] - r[i], nj = x[j] - r[j];
            double dd = ex + er + u * Math.Abs(d);
            px = ni / d; py = nj / d;
            double di = ex + er + u * Math.Abs(ni), dj = ex + er + u * Math.Abs(nj);
            double ad = Math.Abs(d) - dd;
            pe = 1.01 * Math.Max((di + Math.Abs(px) * dd) / ad + u * Math.Abs(px), (dj + Math.Abs(py) * dd) / ad + u * Math.Abs(py)) + 1e-300;
        }

        static BigInteger Scaled(double v)       // v 2^1074
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            BigInteger mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            if (bits < 0) mant = -mant;
            return mant << (exp - 1);
        }

        [Fact]
        public void Project_IsTheDocumentedBoundAndContainsEveryExactProjectionOfThePerturbedPoints()
        {
            Vector3d[] g = Segments(1, 6);
            var proj = new ExactMeshBoolean.SegmentProjection(g, R);
            Assert.True(proj.Valid);
            int k = proj.Axis;
            Assert.Equal(1, k);
            int ci = (k + 1) % 3, cj = (k + 2) % 3;
            BigInteger S = BigInteger.One << 1074;
            for (int s = 0; s < g.Length; ++s) {
                Vector3d x = g[s];
                double px, py, pe;
                Assert.True(proj.Project(x, out px, out py, out pe));
                double rpx, rpy, rpe;
                RefProject(x, R, k, out rpx, out rpy, out rpe);
                Assert.Equal(rpx, px); Assert.Equal(rpy, py); Assert.Equal(rpe, pe);
                // exactly: the projection of every point within ex of x and er of R, against the computed one
                double ex = 2.3e-16 * MaxAbs(x), er = 2.3e-16 * MaxAbs(R);
                BigInteger PX = Scaled(px), PY = Scaled(py), PE = Scaled(pe);
                double worst = 0;
                for (int mask = 0; mask < 64; ++mask) {
                    BigInteger[] xs = new BigInteger[3], rs = new BigInteger[3];
                    for (int c = 0; c < 3; ++c) {
                        xs[c] = Scaled(x[c]) + ((mask >> c & 1) == 0 ? -Scaled(ex) : Scaled(ex));
                        rs[c] = Scaled(R[c]) + ((mask >> (c + 3) & 1) == 0 ? -Scaled(er) : Scaled(er));
                    }
                    BigInteger D = xs[k] - rs[k];
                    foreach (var pair in new[] { Tuple.Create(ci, PX), Tuple.Create(cj, PY) }) {
                        BigInteger N = xs[pair.Item1] - rs[pair.Item1];
                        BigInteger num = BigInteger.Abs(N * S - pair.Item2 * D), den = BigInteger.Abs(D) * S;
                        // contained: |N / D - P| <= PE, all scaled by 2^1074 (N, D, P, PE are)
                        Assert.True(num <= PE * BigInteger.Abs(D), $"point {s}: the exact projection leaves the box");
                        worst = Math.Max(worst, (double)((num << 64) / den) / Math.Pow(2, 64) * Math.Pow(2, 0));
                    }
                }
                // not wasteful: the bound is at most 1.5 times the largest deviation of a corner (the bound adds the roundings and 1 %)
                Assert.True(pe <= 2.5 * worst, $"point {s}: pe {pe:R} worst {worst:R}");
                Assert.True(worst > 0);
            }
        }

        static T F<T>(object o, string name) { return (T)o.GetType().GetField(name, NP).GetValue(o); }

        [Theory]
        [InlineData(1, 40)] [InlineData(2, 9)] [InlineData(3, 130)]
        public void SegmentProjection_BoxesGridAndCells_AreTheDocumentedOnes(int seed, int count)
        {
            Vector3d[] g = Segments(seed, count);
            var proj = new ExactMeshBoolean.SegmentProjection(g, R);
            Assert.True(proj.Valid);
            int k = proj.Axis;
            double[] box = F<double[]>(proj, "box");
            Assert.Equal(4 * count, box.Length);
            double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity, bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
            for (int s = 0; s < count; ++s) {
                double ax, ay, ae, bx, by, be;
                Assert.True(proj.Project(g[2 * s], out ax, out ay, out ae));
                Assert.True(proj.Project(g[2 * s + 1], out bx, out by, out be));
                Assert.Equal(Math.Min(ax - ae, bx - be), box[4 * s]);
                Assert.Equal(Math.Min(ay - ae, by - be), box[4 * s + 1]);
                Assert.Equal(Math.Max(ax + ae, bx + be), box[4 * s + 2]);
                Assert.Equal(Math.Max(ay + ae, by + be), box[4 * s + 3]);
                bx0 = Math.Min(bx0, box[4 * s]); by0 = Math.Min(by0, box[4 * s + 1]); bx1 = Math.Max(bx1, box[4 * s + 2]); by1 = Math.Max(by1, box[4 * s + 3]);
            }
            int side = Math.Max(1, (int)Math.Sqrt(count / 2.0));
            int nx = F<int>(proj, "nx"), ny = F<int>(proj, "ny");
            Assert.Equal(side, nx); Assert.Equal(side, ny);
            double x0 = F<double>(proj, "x0"), y0 = F<double>(proj, "y0"), cw = F<double>(proj, "cw"), ch = F<double>(proj, "ch");
            Assert.Equal(bx0, x0); Assert.Equal(by0, y0);
            Assert.Equal((bx1 - bx0) / nx, cw);
            Assert.Equal((by1 - by0) / ny, ch);
            // the cells: every segment in every cell its box meets (the clamped range of (v - x0) / cw), the segments of a cell in ascending order
            int[] start = F<int[]>(proj, "start"), items = F<int[]>(proj, "items");
            Func<double, double, int, int> cell = (v, v0, n) => { double t = Math.Floor((v - v0) / (v0 == x0 ? cw : ch)); return !(t >= 0) ? 0 : t >= n - 1 ? n - 1 : (int)t; };
            var expected = new List<int>[nx * ny];
            for (int c = 0; c < nx * ny; ++c) expected[c] = new List<int>();
            for (int s = 0; s < count; ++s) {
                int cx0 = ClampCell((box[4 * s] - x0) / cw, nx), cx1 = ClampCell((box[4 * s + 2] - x0) / cw, nx);
                int cy0 = ClampCell((box[4 * s + 1] - y0) / ch, ny), cy1 = ClampCell((box[4 * s + 3] - y0) / ch, ny);
                for (int cy = cy0; cy <= cy1; ++cy)
                    for (int cx = cx0; cx <= cx1; ++cx)
                        expected[cy * nx + cx].Add(2 * s);
            }
            Assert.Equal(nx * ny + 1, start.Length);
            for (int c = 0; c < nx * ny; ++c)
                Assert.Equal(expected[c], items.Skip(start[c]).Take(start[c + 1] - start[c]).ToList());
            Assert.True(nx >= 1);
        }

        static int ClampCell(double t, int n) { return double.IsNaN(t) || t < 0 ? 0 : t >= n - 1 ? n - 1 : (int)t; }

        [Fact]
        public void Query_FindsASegmentWhoseBoxTouchesTheWidenedQueryBox_AndNotOneBeyondIt()
        {
            Vector3d[] g = Segments(4, 40);
            var proj = new ExactMeshBoolean.SegmentProjection(g, R);
            int k = proj.Axis, ci = (k + 1) % 3, cj = (k + 2) % 3;
            double[] box = F<double[]>(proj, "box");
            // a query point whose projection is (tx, ty), found by correcting the point until Project returns it
            Func<double, double, Vector3d> point = (tx, ty) => {
                double D = 1e16;
                var v = new Vector3d();
                v[k] = R[k] + D; v[ci] = R[ci] + tx * D; v[cj] = R[cj] + ty * D;
                return v;
            };
            // (a segment whose box is alone in its corner of the plane is the target: the query at its edge, left, right, above, below)
            for (int s = 0; s < 40; ++s) {
                double mx = (box[4 * s] + box[4 * s + 2]) / 2, my = (box[4 * s + 1] + box[4 * s + 3]) / 2;
                foreach (int side in new[] { 0, 1, 2, 3 })
                    foreach (double factor in new[] { 0.5, 1.5 }) {
                        double tx = mx, ty = my;
                        double gap = 0;
                        for (int it = 0; it < 4; ++it) {
                            Vector3d v = point(tx, ty);
                            double px, py, pe;
                            Assert.True(proj.Project(v, out px, out py, out pe));
                            // the query box is [p - pe, p + pe]; put its edge `factor` pe beyond the segment's box on the chosen side
                            if (side == 0) tx += (box[4 * s + 2] + factor * pe - px);       // query right of the segment: its centre factor pe beyond the box
                            else if (side == 1) tx += (box[4 * s] - factor * pe - px);      // query left of the segment
                            else if (side == 2) ty += (box[4 * s + 3] + factor * pe - py);
                            else ty += (box[4 * s + 1] - factor * pe - py);
                            gap = pe;
                            // (the other coordinate in the middle of the segment's range)
                            if (side < 2) ty = my; else tx = mx;
                        }
                        Vector3d q = point(tx, ty);
                        double qx, qy, qe;
                        Assert.True(proj.Project(q, out qx, out qy, out qe));
                        double dist = side == 0 ? qx - box[4 * s + 2] : side == 1 ? box[4 * s] - qx : side == 2 ? qy - box[4 * s + 3] : box[4 * s + 1] - qy;
                        // the distance of the query point's projection from the box in units of the query's pad (the correction converges to about `factor`)
                        if (Math.Abs(dist / qe - factor) > 0.1)
                            continue;
                        int[] found = proj.Query(new[] { q });
                        bool has = found != null && found.Contains(2 * s);
                        // brute force: whether the boxes meet
                        bool meets = box[4 * s] <= qx + qe && box[4 * s + 2] >= qx - qe && box[4 * s + 1] <= qy + qe && box[4 * s + 3] >= qy - qe;
                        Assert.Equal(meets, has);
                        // (the pad is the point of this test: a segment at 0.5 pe is met, one at 1.5 pe is not, unless another box is in the way)
                        Assert.Equal(factor < 1, has);
                    }
            }
        }


        [Fact]
        public void SegmentProjection_L1_IsTheNormOfEveryEndAndTheirMaximumLast()
        {
            var g = new[] { new Vector3d(1, -2, 3), new Vector3d(-10, 0, 5), new Vector3d(0, 0, -7) };
            Assert.Equal(new double[] { 6, 15, 7, 15 }, ExactMeshBoolean.SegmentProjection.L1(g));
        }

        [Theory]
        [InlineData(5e15, 1e15, 2e15, 0)] [InlineData(1e15, -5e15, 2e15, 1)] [InlineData(1e15, 2e15, 5e15, 2)]
        [InlineData(5e15, -5e15, 2e15, 0)] [InlineData(5e15, 2e15, -5e15, 0)] [InlineData(1e15, -5e15, 5e15, 1)]
        [InlineData(5e15, 2e15, 9e15, 2)] [InlineData(2e15, 9e15, 5e15, 1)] [InlineData(5e15, 5e15, 5e15, 0)] [InlineData(3e15, 5e15, 5e15, 1)]
        public void SegmentProjection_ProjectsFromTheLargestCoordinateOfR_AlongTheOtherTwoAxes(double rx, double ry, double rz, int axis)
        {
            var r = new Vector3d(rx, ry, rz);
            var g = new[] { new Vector3d(1e3, 2e3, -3e3), new Vector3d(-5e3, 7e3, 1e3), new Vector3d(4e3, -1e3, 6e3), new Vector3d(2e3, 9e3, -4e3) };
            var proj = new ExactMeshBoolean.SegmentProjection(g, r);
            Assert.True(proj.Valid);
            Assert.Equal(axis, proj.Axis);
            for (int s = 0; s < g.Length; ++s) {
                double px, py, pe, qx, qy, qe;
                Assert.True(proj.Project(g[s], out px, out py, out pe));
                RefProject(g[s], r, axis, out qx, out qy, out qe);
                Assert.Equal(qx, px); Assert.Equal(qy, py); Assert.Equal(qe, pe);
            }
        }

        [Fact]
        public void SegmentProjection_IsInvalid_ForNoSegmentsAndForPointsOnBothSidesOrTooCloseToThePlane()
        {
            Assert.False(new ExactMeshBoolean.SegmentProjection(new Vector3d[0], R).Valid);
            Vector3d a = new Vector3d(1e3, 1e3, 1e3), across = new Vector3d(1e3, -2e16, 1e3), near = new Vector3d(1e3, R.y + 1, 1e3);
            Assert.False(new ExactMeshBoolean.SegmentProjection(new[] { a, across }, R).Valid);
            Assert.False(new ExactMeshBoolean.SegmentProjection(new[] { across, a }, R).Valid);
            Assert.False(new ExactMeshBoolean.SegmentProjection(new[] { a, near }, R).Valid);
            var proj = new ExactMeshBoolean.SegmentProjection(new[] { a, new Vector3d(5, 6, 7) }, R);
            Assert.True(proj.Valid);
            double px, py, pe;
            Assert.False(proj.Project(across, out px, out py, out pe));
            Assert.True(double.IsNaN(pe));
            Assert.False(proj.Project(near, out px, out py, out pe));            // |d| <= 2 dd
            Assert.False(proj.Project(new Vector3d(double.NaN, 1, 1), out px, out py, out pe));
            Assert.False(proj.Project(new Vector3d(double.PositiveInfinity, 1, 1), out px, out py, out pe));
            Assert.Null(proj.Query(new[] { across }));
            Assert.Null(proj.Query(new[] { near }));
        }

        [Fact]
        public void SegmentProjection_SignAndClamp_AreTheThreeValuedSignAndTheClampedCellIndex()
        {
            MethodInfo sign = typeof(ExactMeshBoolean.SegmentProjection).GetMethod("sign", NP), clamp = typeof(ExactMeshBoolean.SegmentProjection).GetMethod("clamp", NP);
            Func<double, int> S = v => (int)sign.Invoke(null, new object[] { v });
            Func<double, int, int> C = (t, n) => (int)clamp.Invoke(null, new object[] { t, n });
            Assert.Equal(1, S(1e-300)); Assert.Equal(-1, S(-1e-300)); Assert.Equal(0, S(0)); Assert.Equal(0, S(-0.0)); Assert.Equal(0, S(double.NaN));
            Assert.Equal(1, S(double.PositiveInfinity)); Assert.Equal(-1, S(double.NegativeInfinity));
            Assert.Equal(0, C(double.NaN, 5)); Assert.Equal(0, C(-0.5, 5)); Assert.Equal(0, C(-1e300, 5)); Assert.Equal(0, C(0.5, 5));
            Assert.Equal(2, C(2.9, 5)); Assert.Equal(3, C(3.0, 5)); Assert.Equal(3, C(3.5, 5)); Assert.Equal(4, C(4, 5)); Assert.Equal(4, C(1e300, 5));
            Assert.Equal(0, C(0.9, 1)); Assert.Equal(0, C(7, 1));
        }


        [Fact]
        public void PiercePrefilter_OnASubset_IsTheReferenceOnThoseSegments_AndEmptyForAnEmptySubset()
        {
            var rnd = new Random(31);
            var o = new Vector3d(1e8, -2e8, 5e7);
            var v = new[] { o, o + new Vector3d(1e6, 0, 0), o + new Vector3d(1e6, 1, 0), o + new Vector3d(4e5, 7e5, 3e5) };
            var segs = new List<Vector3d>();
            for (int i = 0; i < 80; ++i)
                segs.Add(o + new Vector3d((rnd.NextDouble() - 0.5) * 4e6, (rnd.NextDouble() - 0.5) * 4e6, (rnd.NextDouble() - 0.5) * 4e6));
            var g = segs.ToArray();
            var faces = RefFaces(v, g);
            double[] gl = ExactMeshBoolean.SegmentProjection.L1(g);
            Assert.Empty(ExactMeshBoolean.pierce_prefilter(v, g, gl, new int[0]));
            int[] subset = { 2, 6, 10, 14, 30, 34, 38, 50, 66, 70 };
            var expected = subset.Where(sx => !RefOutside(faces, g[sx], g[sx + 1])).ToList();
            Assert.Equal(expected, ExactMeshBoolean.pierce_prefilter(v, g, gl, subset).ToList());
            int[] all = ExactMeshBoolean.pierce_prefilter(v, g);
            Assert.Equal(all.Where(sx => subset.Contains(sx)).ToList(), ExactMeshBoolean.pierce_prefilter(v, g, gl, subset).ToList());
            Assert.True(expected.Count > 0 && expected.Count < subset.Length || expected.Count == subset.Length);
        }

        [Fact]
        public void Query_IsTheAscendingListOfTheSegmentsWhoseBoxesMeet_AlsoAtTiesOfTheBoxEdges()
        {
            Vector3d[] g = Segments(6, 60);
            var proj = new ExactMeshBoolean.SegmentProjection(g, R);
            int k = proj.Axis, ci = (k + 1) % 3, cj = (k + 2) % 3;
            double[] box = F<double[]>(proj, "box");
            var rnd = new Random(8);
            // wide queries (several cells): the list in ascending order, as the brute force
            for (int trial = 0; trial < 30; ++trial) {
                var pts = new List<Vector3d>();
                for (int i = 0; i < 3; ++i) pts.Add(g[rnd.Next(g.Length)]);
                int[] got = proj.Query(pts.ToArray());
                double ax = double.PositiveInfinity, ay = ax, bx = double.NegativeInfinity, by = bx;
                foreach (var q in pts) {
                    double px, py, pe;
                    Assert.True(proj.Project(q, out px, out py, out pe));
                    ax = Math.Min(ax, px - pe); ay = Math.Min(ay, py - pe); bx = Math.Max(bx, px + pe); by = Math.Max(by, py + pe);
                }
                var want = new List<int>();
                for (int s = 0; s < 60; ++s)
                    if (box[4 * s] <= bx && box[4 * s + 2] >= ax && box[4 * s + 1] <= by && box[4 * s + 3] >= ay) want.Add(2 * s);
                Assert.Equal(want, got.ToList());
            }
            // ties: the query box touches a segment's box exactly (the left edge of the query at the right edge of the segment, ...)
            int ties = 0;
            for (int s = 0; s < 60 && ties < 8; ++s)
                for (int side = 0; side < 4; ++side) {
                    int c = side < 2 ? ci : cj;
                    double target = side == 0 ? box[4 * s + 2] : side == 1 ? box[4 * s] : side == 2 ? box[4 * s + 3] : box[4 * s + 1];
                    // the point of the middle of the segment's box, its coordinate c moved until the query box edge is the target
                    double mx = (box[4 * s] + box[4 * s + 2]) / 2, my = (box[4 * s + 1] + box[4 * s + 3]) / 2;
                    double D = 1e16;
                    Func<double, Vector3d> point = x => {
                        var v = new Vector3d();
                        v[k] = R[k] + D;
                        v[ci] = R[ci] + (c == ci ? x : mx) * D; v[cj] = R[cj] + (c == cj ? x : my) * D;
                        return v;
                    };
                    double xs = target;
                    bool hit = false;
                    for (int it = 0; it < 6 && !hit; ++it) {
                        double px, py, pe;
                        proj.Project(point(xs), out px, out py, out pe);
                        double cur = c == ci ? px : py;
                        double edge = side % 2 == 0 ? cur - pe : cur + pe;
                        xs += target - edge;
                    }
                    // a few ulps around: look for the exact tie
                    double start = xs;
                    for (int u = -400; u <= 400 && !hit; ++u) {
                        double x = start;
                        for (int j = 0; j < Math.Abs(u); ++j) x = u > 0 ? BitUp(x) : BitDown(x);
                        double px, py, pe;
                        Vector3d v = point(x);
                        proj.Project(v, out px, out py, out pe);
                        double cur = c == ci ? px : py;
                        double edge = side % 2 == 0 ? cur - pe : cur + pe;
                        double other = c == ci ? py : px;
                        if (edge == target && Math.Abs(other - (c == ci ? my : mx)) < 1e-9) {
                            // the other coordinate's range must be met too: its middle lies in the segment's box
                            int[] got = proj.Query(new[] { v });
                            Assert.NotNull(got);
                            Assert.Contains(2 * s, got);
                            hit = true; ++ties;
                        }
                    }
                }
            Assert.True(ties >= 2, $"{ties} exact ties found");
        }

        static double BitUp(double x) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? 1 : -1)); }
        static double BitDown(double x) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) - (x > 0 ? 1 : -1)); }

        // ---------------------------------------------------------------------------------------------------------------
        // pierce_prefilter: the thresholds of the faces
        // ---------------------------------------------------------------------------------------------------------------
        sealed class Face { public Vector3d Normal; public double Offset, Slope, Al, Margin; public bool Usable; public Vector3d A, D1; }

        static double MaxSum(Vector3d x, Vector3d y) { return Math.Max(Math.Abs(x.x) + Math.Abs(y.x), Math.Max(Math.Abs(x.y) + Math.Abs(y.y), Math.Abs(x.z) + Math.Abs(y.z))); }

        static List<Face> RefFaces(Vector3d[] v, Vector3d[] g)
        {
            double[] gl = ExactMeshBoolean.SegmentProjection.L1(g);
            double size = gl[gl.Length - 1];
            foreach (var x in v) size = Math.Max(size, L1(x));
            var triples = new List<int[]>();
            if (v.Length == 4) { triples.Add(new[] { 0, 1, 2 }); triples.Add(new[] { 0, 1, 3 }); triples.Add(new[] { 1, 2, 3 }); triples.Add(new[] { 2, 0, 3 }); }
            else for (int i = 0; i < v.Length; ++i) for (int j = i + 1; j < v.Length; ++j) for (int k = j + 1; k < v.Length; ++k) triples.Add(new[] { i, j, k });
            var faces = new List<Face>();
            foreach (int[] f in triples) {
                Vector3d a = v[f[0]], b = v[f[1]], c = v[f[2]];
                Vector3d d1 = b - a, d2 = c - a, n = d1.Cross(d2);
                double ea = 2.3e-16 * MaxAbs(a), eb = 2.3e-16 * MaxAbs(b), ec = 2.3e-16 * MaxAbs(c);
                double e1 = ea + eb + u * MaxSum(a, b), e2 = ea + ec + u * MaxSum(a, c);
                double m1 = MaxAbs(d1), m2 = MaxAbs(d2);
                double dn = 2 * (e1 * (m2 + e2) + (m1 + e1) * e2) + 7 * u * m1 * m2;
                double n1 = L1(n);
                var fc = new Face { Slope = 1.01 * (dn + 7e-16 * n1), Al = L1(a), Margin = 1e-9 * size * n1 + 1e-300, A = a, D1 = d1 };
                fc.Usable = fc.Slope < double.MaxValue && fc.Margin < double.MaxValue;
                int sign = 0;
                for (int o = 0; o < v.Length && fc.Usable; ++o) {
                    if (o == f[0] || o == f[1] || o == f[2]) continue;
                    double side = n.Dot(v[o]) - n.Dot(a);
                    int so = side > 0 ? 1 : -1;
                    fc.Usable = Math.Abs(side) > fc.Slope * (L1(v[o]) + fc.Al) + fc.Margin && (sign == 0 || so == sign);
                    sign = so;
                }
                if (sign > 0) n = -n;
                fc.Normal = n; fc.Offset = n.Dot(a);
                faces.Add(fc);
            }
            return faces;
        }

        static bool RefOutside(List<Face> faces, Vector3d p, Vector3d q)
        {
            foreach (Face f in faces)
                if (f.Usable
                    && f.Normal.Dot(p) - f.Offset > f.Slope * (L1(p) + f.Al) + f.Margin
                    && f.Normal.Dot(q) - f.Offset > f.Slope * (L1(q) + f.Al) + f.Margin)
                    return true;
            return false;
        }

        [Theory]
        [InlineData(1, 4, 1e5, 1e3)]          // the tetrahedron: the faces of the former order
        [InlineData(2, 5, 1e5, 1e3)]          // five points: every triple
        [InlineData(3, 4, 1e9, 1e3)]          // far from the origin, small faces: the errors of the points dominate
        [InlineData(4, 4, 1e3, 1e5)]          // near the origin, large faces: the products' rounding dominates
        [InlineData(5, 4, 1e7, 1e6)]
        [InlineData(6, 4, 1e8, -1e6)]         // a sliver face (the angle 1e-6): the slope term outweighs the margin
        [InlineData(7, 5, 1e8, -1e6)]
        [InlineData(13, 4, 1e3, -1e6)]        // the rounding of the points small: the products' term of the slope and the margin of equal weight
        [InlineData(14, 5, 1e3, -1e6)]
        [InlineData(15, 4, 1e8, -1e6, 0, 1)]  // the offset rotated: the largest coordinate of max_sum is x, then z
        [InlineData(16, 4, 1e8, -1e6, 0, 2)]
        [InlineData(17, 4, 1e3, -1e6, 0, 1)]
        [InlineData(18, 4, 1e3, -1e6, 0, 2)]
        [InlineData(19, 4, 1e3, -1e6, 0.9)]
        [InlineData(20, 4, 1e3, -1e6, 1.1)]
        [InlineData(21, 4, 1e8, -1e6, 0.95)]
        [InlineData(22, 4, 1e8, -1e6, 1.05)]
        [InlineData(9, 4, 1e8, -1e6, 0.6)]    // the fourth point at 0.6, 0.85, 1.15, 1.6 times the threshold of the sliver face: usable or not
        [InlineData(10, 4, 1e8, -1e6, 0.85)]
        [InlineData(11, 4, 1e8, -1e6, 1.15)]
        [InlineData(12, 4, 1e8, -1e6, 1.6)]
        public void PiercePrefilter_DecidesAsTheDocumentedThresholdsAtSegmentsJustBelowAndAboveThem(int seed, int points, double offset, double extent, double usableFactor = 0, int rot = 0)
        {
            var rnd = new Random(seed);
            var v = new Vector3d[points];
            if (extent < 0) {
                double L = -extent;
                var o = rot == 0 ? new Vector3d(offset, -2 * offset, 0.5 * offset) : rot == 1 ? new Vector3d(-2 * offset, 0.5 * offset, offset) : new Vector3d(0.5 * offset, offset, -2 * offset);
                v[0] = o; v[1] = o + new Vector3d(L, 0, 0); v[2] = o + new Vector3d(L, 1, 0); v[3] = o + new Vector3d(0.4 * L, 0.7 * L, 0.3 * L);
                if (points == 5) v[4] = o + new Vector3d(-0.3 * L, 0.2 * L, 0.5 * L);
            } else
            for (int i = 0; i < points; ++i)
                v[i] = new Vector3d(offset * (1 + rnd.NextDouble()) + extent * (rnd.NextDouble() - 0.5), -offset * (rnd.NextDouble() + 0.5) + extent * (rnd.NextDouble() - 0.5), offset * (rnd.NextDouble() - 0.5) + extent * (rnd.NextDouble() - 0.5));
            // background segments (the size S of the margin is over all of them)
            var segs = new List<Vector3d>();
            for (int i = 0; i < 6; ++i)
                segs.Add(new Vector3d((rnd.NextDouble() - 0.5) * 4 * offset, (rnd.NextDouble() - 0.5) * 4 * offset, (rnd.NextDouble() - 0.5) * 4 * offset));
            if (usableFactor > 0) {
                // the fourth point at usableFactor times the threshold of the face (0, 1, 2) above its plane (iterated: the threshold depends on |v3|_1)
                for (int it = 0; it < 8; ++it) {
                    Face f0 = RefFaces(v, segs.ToArray())[0];
                    double T = f0.Slope * (L1(v[3]) + f0.Al) + f0.Margin;
                    v[3].z = f0.A.z + usableFactor * T / Math.Abs(f0.Normal.z);
                }
            }
            var faces = RefFaces(v, segs.ToArray());
            // probes: a segment with both ends at `factor` times the threshold of a face (iterated: the threshold depends on |x|_1)
            var probes = new List<Vector3d>();
            foreach (Face f in faces) {
                Vector3d nu = f.Normal / f.Normal.Length;
                foreach (double factor in new[] { 0.5, 0.8, 0.9, 0.95, 0.98, 0.9985, 0.9995, 1.0005, 1.0015, 1.02, 1.05, 1.1, 1.25, 1.5, 2, 4 }) {
                    double t = 0;
                    for (int it = 0; it < 6; ++it) {
                        Vector3d x = f.A + t * nu;
                        double T = f.Slope * (L1(x) + f.Al) + f.Margin;
                        t = factor * T / f.Normal.Length;
                    }
                    probes.Add(f.A + t * nu);
                    probes.Add(f.A + t * nu + 0.37 * f.D1);
                }
            }
            Assert.True(probes.Count >= 40, $"{probes.Count} probes");
            // the list of the segments the prefilter keeps, against the reference's
            var all = segs.Concat(probes).ToArray();
            int[] kept = ExactMeshBoolean.pierce_prefilter(v, all);
            var expected = new List<int>();
            for (int s = 0; s < all.Length / 2; ++s)
                if (!RefOutside(faces, all[2 * s], all[2 * s + 1]))
                    expected.Add(2 * s);
            Assert.Equal(expected, kept.ToList());
            // the probes decide both ways (the test has teeth)
            int dropped = all.Length / 2 - kept.Length;
            if (usableFactor == 0)
                Assert.True(dropped >= 5 && kept.Length >= 5, $"dropped {dropped} kept {kept.Length}");
            else
                Assert.Equal(usableFactor > 1, faces[0].Usable);
        }


        // ---------------------------------------------------------------------------------------------------------------
        // LocalFrame.InPlane
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void InPlane_IsTrueExactlyWhereBothEndsLieInThePlaneOfTheTriangle()
        {
            var f = ExactBooleanBoundValueTests.Fixture.Generic.Value;
            var arr = f.Arr;
            int ti = -1;
            for (int i = 0; i < arr.Triangles.Count && ti < 0; ++i) {
                var p = arr.Triangles[i].P;
                if (!p.A.IsZero && !p.B.IsZero && !p.C.IsZero && !p.D.IsZero) ti = i;
            }
            Assert.True(ti >= 0);
            var tri = arr.Triangles[ti];
            ExactPoint3 c0 = arr.Exact(tri.Corners.a), c1 = arr.Exact(tri.Corners.b), c2 = arr.Exact(tri.Corners.c);
            Func<int, int, int, ExactPoint3> inPlane = (w0, w1, w2) => ExactPoint3.Create(w0 * c0.X + w1 * c1.X + w2 * c2.X, w0 * c0.Y + w1 * c1.Y + w2 * c2.Y, w0 * c0.Z + w1 * c1.Z + w2 * c2.Z, w0 * c0.W + w1 * c1.W + w2 * c2.W);
            // (in the plane: weights summing to 1 per unit of W; off it: moved along x by one unit)
            Func<ExactPoint3, ExactPoint3> off = p => ExactPoint3.Create(p.X + p.W, p.Y, p.Z, p.W);
            var e = new[] {
                inPlane(2, 3, 2), inPlane(1, 1, 5), inPlane(-2, 5, 4), off(inPlane(1, 1, 3)), off(inPlane(2, 1, 4)),
                inPlane(3, 1, 3), inPlane(1, 4, -2), off(inPlane(2, 2, 3)), inPlane(5, -2, 4) };
            var dict = F<Dictionary<int, ExactPoint3[]>>(f.Op, "antipodalExact");
            dict[3] = e;
            try {
                var frame = new ExactMeshBoolean.LocalFrame(f.Op, tri);
                // pairs (t, t + 1): in-in, in-in, in-off, off-off, off-in, in-in, in-off, off-in
                bool[] want = { true, true, false, false, false, true, false, false };
                for (int t = 0; t < want.Length; ++t) {
                    Assert.Equal(want[t], frame.InPlane(3, t));
                    Assert.Equal(want[t], frame.InPlane(3, t, tri, ti));
                }
                // another mesh with the same segment index: its own entry (the key holds the mesh above bit 40)
                dict[5] = e.Select(p => off(p)).ToArray();           // every end off the plane
                for (int t = 0; t < want.Length; ++t)
                    Assert.False(frame.InPlane(5, t));
                dict.Remove(5);
            } finally { dict.Remove(3); }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // CrossingGrid: the box of the triangle, the clipped boxes of its partners and the cell sizes
        // ---------------------------------------------------------------------------------------------------------------
        static double[] CornerBox(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t)
        {
            var r = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            foreach (int id in new[] { t.Corners.a, t.Corners.b, t.Corners.c }) {
                Vector3d a = arr.Approx(id);
                double e = arr.ApproxError(id) * (1 + 1e-15);
                for (int q = 0; q < 3; ++q) { r[q] = Math.Min(r[q], a[q] - e); r[q + 3] = Math.Max(r[q + 3], a[q] + e); }
            }
            return r;
        }

        [Fact]
        public void CrossingGrid_BoxesAndCellSizes_AreTheDocumentedOnes()
        {
            var f = ExactBooleanBoundValueTests.Fixture.Generic.Value;
            var arr = f.Arr;
            int tested = 0, withGrid = 0;
            for (int ti = 0; ti < arr.Triangles.Count; ++ti) {
                var t = arr.Triangles[ti];
                for (int m = 0; m < 2; ++m) {
                    var partners = new List<int>(t.Partners);
                    partners.Sort();
                    var ids = new List<int>();
                    var boxes = new List<double>();
                    int ci = (t.Axis + 1) % 3, cj = (t.Axis + 2) % 3;
                    double[] tb = CornerBox(arr, t);
                    int last = -1;
                    foreach (int p in partners) {
                        if (p == last) continue;
                        last = p;
                        var U = arr.Triangles[p];
                        if (U.Degenerate || U.Mesh != m) continue;
                        double[] ub = CornerBox(arr, U);
                        ids.Add(p);
                        boxes.Add(Math.Max(ub[ci], tb[ci])); boxes.Add(Math.Max(ub[cj], tb[cj]));
                        boxes.Add(Math.Min(ub[ci + 3], tb[ci + 3])); boxes.Add(Math.Min(ub[cj + 3], tb[cj + 3]));
                    }
                    var grid = new ExactMeshBoolean.CrossingGrid(f.Op, t, m);
                    ++tested;
                    Assert.Equal(ids, F<int[]>(grid, "tri").ToList());
                    Assert.Equal(boxes, F<double[]>(grid, "box").ToList());
                    int n = ids.Count;
                    int side = Math.Max(1, (int)Math.Sqrt(n));
                    double cw = (tb[ci + 3] - tb[ci]) / side, ch = (tb[cj + 3] - tb[cj]) / side;
                    if (!(cw > 0) || !(ch > 0) || n == 0) {
                        Assert.Equal(1, F<int>(grid, "nx"));
                        Assert.True(double.IsPositiveInfinity(F<double>(grid, "cw")));
                    } else {
                        ++withGrid;
                        Assert.Equal(side, F<int>(grid, "nx")); Assert.Equal(side, F<int>(grid, "ny"));
                        Assert.Equal(tb[ci], F<double>(grid, "x0")); Assert.Equal(tb[cj], F<double>(grid, "y0"));
                        Assert.Equal(cw, F<double>(grid, "cw")); Assert.Equal(ch, F<double>(grid, "ch"));
                        if (side >= 2) Assert.True(true);
                    }
                }
            }
            Assert.True(withGrid > 10, $"{withGrid} grids of {tested}");
        }
    }
}
