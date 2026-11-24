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
    /// #1423: value tests of the conservative bounds of ExactMeshBoolean (box padding, scales, cell sizes, index arithmetic
    /// of cached boxes). A wrong value there changes how many candidates a query visits or when a guarded fallback is taken,
    /// not the Boolean's result, so these tests assert the intermediate values against an independent computation (exact
    /// arithmetic where a bound is stated, an independent re-coding of the documented formula where a value is).
    /// Private members are reached by reflection; the library is not touched (mutation shards are split by character span).
    /// </summary>
    public class ExactBooleanBoundValueTests
    {
        // ---------------------------------------------------------------------------------------------------------------
        // helpers
        // ---------------------------------------------------------------------------------------------------------------
        internal const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        internal static object Call(object target, string name, params object[] args)
        {
            Type t = typeof(ExactMeshBoolean);
            MethodInfo m = t.GetMethods(NP).Single(x => x.Name == name && x.GetParameters().Length == args.Length
                && x.GetParameters().Zip(args, (p, a) => p.ParameterType.IsInstanceOfType(a)).All(b => b));
            try { return m.Invoke(target, args); }
            catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }

        internal static T GetField<T>(object o, string name)
        {
            return (T)o.GetType().GetField(name, NP).GetValue(o);
        }

        static DMesh3 Box(Vector3d c, Vector3d h, double angleDegrees, Vector3d axis)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(c, h) }.Generate().MakeDMesh();
            Quaterniond q = new Quaterniond(axis.Normalized, angleDegrees);
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, c + q * (m.GetVertex(v) - c));
            return m;
        }

        /// <summary> a computed union of two overlapping boxes (one tilted): 132 output pieces with approximate corners </summary>
        internal sealed class Fixture
        {
            public ExactMeshBoolean Op;
            public ExactMeshArrangement Arr;
            static Fixture Make(Vector3d axisA, Vector3d axisB)
            {
                var a = Box(new Vector3d(0.1, 0.2, 0.3), new Vector3d(1.1, 1.2, 1.3), 17, axisA);
                var b = Box(new Vector3d(0.7, 0.45, 0.2), new Vector3d(1.0, 0.9, 1.1), 40, axisB);
                var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
                Assert.True(op.Compute());
                var arr = (ExactMeshArrangement)typeof(ExactMeshBoolean).GetField("arr", NP).GetValue(op);
                Assert.NotNull(arr);
                return new Fixture { Op = op, Arr = arr };
            }
            /// <summary> two boxes tilted about the y axis and a generic one: faces whose plane has every coefficient nonzero </summary>
            public static readonly Lazy<Fixture> Instance = new Lazy<Fixture>(() => Make(Vector3d.AxisY, Vector3d.AxisY));
            public static readonly Lazy<Fixture> Generic = new Lazy<Fixture>(() => Make(new Vector3d(1, 2, 3), new Vector3d(-2, 1, 1)));
        }

        /// <summary> sign of coordinate q of the exact point p (in grid units) minus the double v, in exact arithmetic </summary>
        internal static int CompareToDouble(ExactPoint3 p, int q, double v)
        {
            BigInteger x = q == 0 ? p.X : q == 1 ? p.Y : p.Z;
            BigInteger w = p.W;
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            BigInteger mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            if (bits < 0) mant = -mant;
            int e = exp - 1075;                                  // v = mant 2^e
            BigInteger lhs = x, rhs = mant * w;
            if (e >= 0) rhs <<= e; else lhs <<= -e;
            int c = lhs.CompareTo(rhs);
            return w.Sign >= 0 ? c : -c;
        }

        /// <summary> the box of a piece as the library documents it: the corners' approximations widened by the summed errors </summary>
        internal static double[] RefBox(ExactMeshArrangement arr, ExactMeshArrangement.OutputTriangle ot, out double err)
        {
            int[] ids = { ot.A, ot.B, ot.C };
            err = 0;
            foreach (int id in ids)
                err += arr.ApproxError(id);
            var box = new double[6];
            for (int q = 0; q < 3; ++q) {
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                foreach (int id in ids) {
                    lo = Math.Min(lo, arr.Approx(id)[q]);
                    hi = Math.Max(hi, arr.Approx(id)[q]);
                }
                box[q] = lo - err;
                box[q + 3] = hi + err;
            }
            return box;
        }

        /// <summary> gradient_bound as documented: the far term of the total length at the box distance, else the sum of the segments' terms </summary>
        internal static double RefGradient(double[] box, ExactMeshBoolean.BoundaryBound b, ExactMeshBoolean.ClusterBound c = null)
        {
            double s = b.Size;
            foreach (double x in box)
                s = Math.Max(s, 3 * Math.Abs(x));
            double near = 1e-3 * s, slack = 1e-12 * s;
            Func<double[], int, double> dist = (y, o) => {
                double sum = 0;
                for (int q = 0; q < 3; ++q) {
                    double gap = Math.Max(0, Math.Max(box[q] - y[o + q + 3], y[o + q] - box[q + 3]));
                    sum += gap * gap;
                }
                return Math.Sqrt(sum);
            };
            double d = dist(b.All, 0) - slack;
            double g;
            if (d >= near)
                g = ExactMeshBoolean.far_term(b.Total, b.Lengths.Length, d);
            else {
                g = c == null ? 0 : c.Far;
                IEnumerable<int> ts = c == null ? Enumerable.Range(0, b.Lengths.Length) : (IEnumerable<int>)c.Near;
                foreach (int t in ts) {
                    double dt = dist(b.Boxes, 6 * t) - slack;
                    g = dt >= near ? g + ExactMeshBoolean.far_term(b.Lengths[t], 1, dt) : double.PositiveInfinity;
                    if (double.IsInfinity(g)) break;
                }
            }
            return ExactMeshBoolean.sum_bound(g, b);
        }

        internal static Vector3d[] RandomSegments(Random rnd, int n, double range, bool integer = true)
        {
            var g = new Vector3d[2 * n];
            for (int i = 0; i < g.Length; ++i) {
                double x = (rnd.NextDouble() * 2 - 1) * range, y = (rnd.NextDouble() * 2 - 1) * range, z = (rnd.NextDouble() * 2 - 1) * range;
                g[i] = integer ? new Vector3d(Math.Round(x), Math.Round(y), Math.Round(z)) : new Vector3d(x, y, z);
            }
            return g;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // boundary_bound: the boxes of the segments, their union, the coordinates' size
        // ---------------------------------------------------------------------------------------------------------------
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        public void BoundaryBound_BoxesUnionAndSize_AreTheExactExtremaOfTheEndpoints(int seed, bool swap)
        {
            var rnd = new Random(seed);
            // (integers below 2^50: every sum below is exact, so the references are exact whatever the order of the additions)
            Vector3d[] g = RandomSegments(rnd, 9, 1e14);
            if (swap)
                for (int t = 0; t < 9; ++t) { Vector3d x = g[2 * t]; g[2 * t] = g[2 * t + 1]; g[2 * t + 1] = x; }
            var b = ExactMeshBoolean.boundary_bound(g);
            Assert.Equal(9, b.Lengths.Length);
            Assert.Equal(54, b.Boxes.Length);
            double size = 0;
            var all = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            for (int t = 0; t < 9; ++t) {
                Vector3d x = g[2 * t], y = g[2 * t + 1];
                for (int q = 0; q < 3; ++q) {
                    Assert.Equal(Math.Min(x[q], y[q]), b.Boxes[6 * t + q]);
                    Assert.Equal(Math.Max(x[q], y[q]), b.Boxes[6 * t + q + 3]);
                    all[q] = Math.Min(all[q], Math.Min(x[q], y[q]));
                    all[q + 3] = Math.Max(all[q + 3], Math.Max(x[q], y[q]));
                }
                size = Math.Max(size, Math.Max(Math.Abs(x.x) + Math.Abs(x.y) + Math.Abs(x.z), Math.Abs(y.x) + Math.Abs(y.y) + Math.Abs(y.z)));
            }
            for (int q = 0; q < 6; ++q)
                Assert.Equal(all[q], b.All[q]);
            Assert.Equal(size, b.Size);
        }

        [Fact]
        public void BoundaryBound_SizeIsTheL1NormOfTheLargestEndpointOnEachSide()
        {
            // (the largest L1 norm is the x endpoint of one segment and the y endpoint of another, every coordinate nonzero)
            var big = new Vector3d(-1000, 2000, -3000);       // L1 6000
            var small = new Vector3d(1, -2, 3);
            var xSide = ExactMeshBoolean.boundary_bound(new[] { big, small });
            var ySide = ExactMeshBoolean.boundary_bound(new[] { small, big });
            Assert.Equal(6000.0, xSide.Size);
            Assert.Equal(6000.0, ySide.Size);
            // (a mixed-sign point: |x| + |y| + |z| differs from every sign-flipped sum)
            var mixed = ExactMeshBoolean.boundary_bound(new[] { new Vector3d(5, -7, 11), new Vector3d(0, 0, 0) });
            Assert.Equal(23.0, mixed.Size);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // box_distance and its scaled fallback
        // ---------------------------------------------------------------------------------------------------------------
        static void Boxes(int mask, double scale, out double[] xb, out double[] yb)
        {
            double[] gaps = { 3 * scale, 4 * scale, 12 * scale };
            xb = new double[6]; yb = new double[6];
            for (int q = 0; q < 3; ++q) {
                double lo = 10 * scale * (q + 1);
                if ((mask >> q & 1) == 0) {      // x below y along this axis
                    xb[q] = lo - 5 * scale; xb[q + 3] = lo; yb[q] = lo + gaps[q]; yb[q + 3] = lo + gaps[q] + 7 * scale;
                } else {                         // x above y
                    yb[q] = lo - 5 * scale; yb[q + 3] = lo; xb[q] = lo + gaps[q]; xb[q + 3] = lo + gaps[q] + 7 * scale;
                }
            }
        }

        [Theory]
        [InlineData(1.0)] [InlineData(0.5)] [InlineData(1e100)]
        [InlineData(1.0715086071862673E+301)]       // 2^1000 / 2^-0: the squares overflow, the scaled path (box_distance_scaled)
        public void BoxDistance_IsTheEuclideanNormOfTheAxisGaps_ForBothOrdersAndOffsets(double scale)
        {
            // (junk before and between the boxes: the offsets a and b matter; gaps (3, 4, 12) scaled give 13)
            var junk = new double[] { -77, 99, 55, 123, 456, 789 };
            for (int mask = 0; mask < 8; ++mask) {
                double[] xb, yb;
                Boxes(mask, scale, out xb, out yb);
                var x = junk.Concat(xb).ToArray();
                var y = junk.Concat(junk).Concat(yb).ToArray();
                Assert.Equal(13 * scale, ExactMeshBoolean.box_distance(x, 6, y, 12));
                Assert.Equal(13 * scale, ExactMeshBoolean.box_distance(y, 12, x, 6));
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // gradient_bound and cluster_bound: the scale s, the near limit and the slack
        // ---------------------------------------------------------------------------------------------------------------
        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void GradientAndClusterBound_AreTheDocumentedFunctionOfTheBoxDistances(int seed)
        {
            var rnd = new Random(seed);
            // segments of moderate size, boxes of large coordinates (so that 3 |box| exceeds the size, whose own value matters) at
            // all distances: far (one term), near (per segment), inside (+infinity)
            Vector3d[] g = RandomSegments(rnd, 12, 1e3);
            var b = ExactMeshBoolean.boundary_bound(g);
            int far = 0, mid = 0, inf = 0;
            for (int i = 0; i < 60; ++i) {
                double cx = (rnd.NextDouble() * 2 - 1) * 1e5, cy = (rnd.NextDouble() * 2 - 1) * 1e5, cz = (rnd.NextDouble() * 2 - 1) * 1e5;
                double hx = 1 + rnd.NextDouble() * 1e4 * (i % 3 == 0 ? 0.01 : 1), hy = 1 + rnd.NextDouble() * 1e3, hz = 1 + rnd.NextDouble() * 1e3;
                if (i % 4 == 0) { cx *= 1e-2; cy *= 1e-2; cz *= 1e-2; }
                var box = new[] { Math.Round(cx - hx), Math.Round(cy - hy), Math.Round(cz - hz), Math.Round(cx + hx), Math.Round(cy + hy), Math.Round(cz + hz) };
                double expected = RefGradient(box, b);
                double got = ExactMeshBoolean.gradient_bound(box, b);
                Assert.Equal(expected, got);
                var cb = ExactMeshBoolean.cluster_bound(box, b);
                // (the cluster of one box: its far part and near list against the per-segment reference)
                double sc = b.Size;
                foreach (double x in box) sc = Math.Max(sc, 3 * Math.Abs(x));
                double near = 1e-3 * sc, slack = 1e-12 * sc, farSum = 0;
                var nearList = new List<int>();
                for (int t = 0; t < b.Lengths.Length; ++t) {
                    double sum = 0;
                    for (int q = 0; q < 3; ++q) {
                        double gap = Math.Max(0, Math.Max(box[q] - b.Boxes[6 * t + q + 3], b.Boxes[6 * t + q] - box[q + 3]));
                        sum += gap * gap;
                    }
                    double dt = Math.Sqrt(sum) - slack;
                    if (dt >= near) farSum += ExactMeshBoolean.far_term(b.Lengths[t], 1, dt); else nearList.Add(t);
                }
                Assert.Equal(farSum, cb.Far);
                Assert.Equal(nearList, cb.Near.ToList());
                Assert.Equal(expected, ExactMeshBoolean.gradient_bound(box, b, cb));
                if (double.IsInfinity(expected)) ++inf;
                else if (ExactMeshBoolean.box_distance(box, 0, b.All, 0) - slack >= near) ++far;
                else ++mid;
            }
            Assert.True(far > 3 && mid + inf > 3, $"far {far} mid {mid} inf {inf}");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // piece_box and piece_cost on the pieces of a computed arrangement
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void PieceBox_IsTheDocumentedBox_ContainsTheExactCorners_AndIsNotWiderThanTwiceTheError()
        {
            Fixture f = Fixture.Instance.Value;
            ExactMeshArrangement arr = f.Arr;
            int n = arr.Output.Count;
            Assert.True(n > 50);
            // (a permutation of the slots: a wrong index reads another piece's box)
            var loc = new int[n];
            for (int i = 0; i < n; ++i) loc[i] = (i * 37 + 11) % n;
            Assert.Equal(n, loc.Distinct().Count());
            var boxes = new double[6 * n];
            var has = new bool[n];
            bool someError = false;
            for (int i = 0; i < n; ++i) {
                Call(f.Op, "piece_box", i, boxes, has, loc);
                double err;
                double[] r = RefBox(arr, arr.Output[i], out err);
                someError |= err > 0;
                for (int k = 0; k < 6; ++k)
                    Assert.Equal(r[k], boxes[6 * loc[i] + k]);
                var ot = arr.Output[i];
                for (int q = 0; q < 3; ++q) {
                    // exact: the box contains every exact corner and reaches at most 2 err beyond the extreme one (the corner's own
                    // error is at most err, and the box adds err)
                    bool lowReached = false, highReached = false;
                    foreach (int id in new[] { ot.A, ot.B, ot.C }) {
                        ExactPoint3 p = arr.Exact(id);
                        Assert.True(CompareToDouble(p, q, boxes[6 * loc[i] + q]) >= 0, "below the box");
                        Assert.True(CompareToDouble(p, q, boxes[6 * loc[i] + q + 3]) <= 0, "above the box");
                        lowReached |= CompareToDouble(p, q, boxes[6 * loc[i] + q] + 2 * err) <= 0;
                        highReached |= CompareToDouble(p, q, boxes[6 * loc[i] + q + 3] - 2 * err) >= 0;
                    }
                    Assert.True(lowReached, "the box reaches more than 2 err below the lowest corner");
                    Assert.True(highReached, "the box reaches more than 2 err above the highest corner");
                }
                Assert.True(has[loc[i]]);
            }
            Assert.True(someError);
        }

        internal static ExactMeshBoolean.BoundaryBound PieceBoundary(ExactMeshArrangement arr)
        {
            // (segments through the middle of the model and some beyond it: pieces near, far and inside the limit of the term)
            var rnd = new Random(5);
            var pts = new List<Vector3d>();
            for (int i = 0; i < 6; ++i) {
                Vector3d a = arr.Approx(arr.Output[rnd.Next(arr.Output.Count)].A), c = arr.Approx(arr.Output[rnd.Next(arr.Output.Count)].B);
                double j = i < 3 ? 1e12 : 3e15;
                pts.Add(new Vector3d(Math.Round(a.x + (rnd.NextDouble() - 0.5) * j), Math.Round(a.y + (rnd.NextDouble() - 0.5) * j), Math.Round(a.z + (rnd.NextDouble() - 0.5) * j)));
                pts.Add(new Vector3d(Math.Round(c.x + (rnd.NextDouble() - 0.5) * j), Math.Round(c.y + (rnd.NextDouble() - 0.5) * j), Math.Round(c.z + (rnd.NextDouble() - 0.5) * j)));
            }
            return ExactMeshBoolean.boundary_bound(pts.ToArray());
        }

        [Fact]
        public void PieceCost_IsTheGradientBoundOfThePiecesBoxTimesItsDiameter()
        {
            Fixture f = Fixture.Instance.Value;
            ExactMeshArrangement arr = f.Arr;
            var b = PieceBoundary(arr);
            int infinite = 0, finite = 0, farPath = 0, nearPath = 0;
            var cache = new Dictionary<int, double>();
            for (int i = 0; i < arr.Output.Count; ++i) {
                var ot = arr.Output[i];
                double err;
                double[] box = RefBox(arr, ot, out err);
                Vector3d pa = arr.Approx(ot.A), pb = arr.Approx(ot.B), pc = arr.Approx(ot.C);
                double diam = (Math.Max(pa.Distance(pb), Math.Max(pb.Distance(pc), pc.Distance(pa))) + 2 * err) * (1 + 8 * 1.1102230246251565e-16);
                double expected = RefGradient(box, b) * diam;
                double got = (double)Call(f.Op, "piece_cost", i, b, cache);
                Assert.Equal(expected, got);
                Assert.Equal(got, cache[i]);
                Assert.Equal(got, (double)Call(f.Op, "piece_cost", i, b, cache));          // cached
                if (double.IsInfinity(got)) ++infinite; else ++finite;
                double s = b.Size;
                foreach (double x in box) s = Math.Max(s, 3 * Math.Abs(x));
                if (ExactMeshBoolean.box_distance(box, 0, b.All, 0) - 1e-12 * s >= 1e-3 * s) ++farPath; else ++nearPath;
            }
            Assert.True(infinite > 0 && finite > 0 && farPath > 0 && nearPath > 0, $"inf {infinite} fin {finite} far {farPath} near {nearPath}");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // boundary_bound: lengths and total; sum_bound; scaled_norm
        // ---------------------------------------------------------------------------------------------------------------
        [Theory]
        [InlineData(1)] [InlineData(2)]
        public void BoundaryBound_LengthsAndTotal_AreTheDocumentedBound(int seed)
        {
            var rnd = new Random(seed);
            Vector3d[] g = RandomSegments(rnd, 9, 1e9);
            if (seed == 2)
                for (int t = 0; t < 9; ++t) { Vector3d x = g[2 * t]; g[2 * t] = g[2 * t + 1]; g[2 * t + 1] = x; }
            var b = ExactMeshBoolean.boundary_bound(g);
            const double E = 2 * 1.1102230246251565e-16;
            double total = 0;
            for (int t = 0; t < 9; ++t) {
                Vector3d x = g[2 * t], y = g[2 * t + 1];
                double r = (Math.Abs(x.x) + Math.Abs(x.y) + Math.Abs(x.z)) + (Math.Abs(y.x) + Math.Abs(y.y) + Math.Abs(y.z));
                double expected = (x.Distance(y) + E * r) * (1 + 10 * 1.1102230246251565e-16);
                Assert.Equal(expected, b.Lengths[t]);
                // (exactly: at least the true length of the segment, and not more than 1e-9 above it)
                BigInteger dx = (BigInteger)x.x - (BigInteger)y.x, dy = (BigInteger)x.y - (BigInteger)y.y, dz = (BigInteger)x.z - (BigInteger)y.z;
                BigInteger sq = dx * dx + dy * dy + dz * dz;
                double trueLength = Math.Sqrt((double)sq);
                Assert.True(b.Lengths[t] >= trueLength * (1 + 1e-16) && b.Lengths[t] <= trueLength * (1 + 1e-9), $"{b.Lengths[t]:R} vs {trueLength:R}");
                total += b.Lengths[t];
            }
            Assert.Equal(total, b.Total);
            Assert.Equal(ExactMeshBoolean.SumTermsFactor(9), b.SumFactor);
        }

        [Fact]
        public void SumBound_IsTheProductWhereTheFactorIsAFiniteNumberAtLeastOne_AndInfinityElsewhere()
        {
            Func<double, double, double> sb = (g, f) => ExactMeshBoolean.sum_bound(g, new ExactMeshBoolean.BoundaryBound { SumFactor = f });
            Assert.Equal(3.0, sb(3.0, 1.0));                                   // the smallest factor
            Assert.Equal(3 * (1 + 1e-15), sb(3.0, 1 + 1e-15));
            Assert.Equal(0.5 * double.MaxValue, sb(0.5, double.MaxValue));    // the largest finite one
            Assert.Equal(double.PositiveInfinity, sb(3.0, 0.0));
            Assert.Equal(double.PositiveInfinity, sb(3.0, 0.5));
            Assert.Equal(double.PositiveInfinity, sb(0.0, 0.0));               // not 0 * 0 and not NaN
            Assert.Equal(double.PositiveInfinity, sb(3.0, double.PositiveInfinity));
            Assert.Equal(double.PositiveInfinity, sb(0.0, double.PositiveInfinity));
            Assert.Equal(double.PositiveInfinity, sb(3.0, double.NaN));
            Assert.Equal(double.PositiveInfinity, sb(3.0, -2.0));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // gradient_bound, cluster_bound and piece_cost where a distance is exactly at the limit `near`
        // ---------------------------------------------------------------------------------------------------------------
        static double BitUp(double x) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? 1 : -1)); }
        static double BitDown(double x) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) - (x > 0 ? 1 : -1)); }

        /// <summary>
        /// A boundary of segments relative to the box q along x below it: A at the distance D, B beyond it, and (with `both`) C
        /// above it, where D is such that D - slack == near for the scale s (searched: the doubles that give an exact equality).
        /// Their boxes span y, z of q. The union box of A and B then lies at the distance D (far path), or, with C, around q
        /// (per segment path)
        /// </summary>
        internal static ExactMeshBoolean.BoundaryBound EqualityBoundary(double[] q, bool both)
        {
            double top = 0;
            foreach (double x in q) top = Math.Max(top, 3 * Math.Abs(x));
            double s = 0, aMax = 0, dist = 0;
            bool found = false;
            for (double D0 = Math.Max(top, 1e6) * 1e-3 * 4; !found && D0 < 1e30; D0 *= 1.37) {
                for (int da = -40; da <= 40 && !found; ++da) {
                    double a = q[0] - D0;
                    for (int i = 0; i < Math.Abs(da); ++i) a = da > 0 ? BitUp(a) : BitDown(a);
                    var box = new[] { a - 1, q[1], q[2], a, q[4], q[5] };
                    double d = ExactMeshBoolean.box_distance(q, 0, box, 0);
                    double s0 = d / (1e-3 + 1e-12);
                    for (int ds = -300; ds <= 300 && !found; ++ds) {
                        double sc = s0;
                        for (int i = 0; i < Math.Abs(ds); ++i) sc = ds > 0 ? BitUp(sc) : BitDown(sc);
                        if (sc >= top && d - 1e-12 * sc == 1e-3 * sc) { found = true; s = sc; aMax = a; dist = d; }
                    }
                }
            }
            Assert.True(found, "no exact equality found");
            double W = 10 * dist;
            var seg = new List<double[]> {
                new[] { aMax - W, q[1], q[2], aMax, q[4], q[5] },                              // A: at the distance D
                new[] { aMax - 3 * W, q[1], q[2], aMax - 2 * dist, q[4], q[5] } };            // B: farther
            if (both)
                seg.Add(new[] { q[3] + 3 * dist, q[1], q[2], q[3] + 3 * dist + W, q[4], q[5] });   // C: above, farther
            var b = new ExactMeshBoolean.BoundaryBound {
                Boxes = seg.SelectMany(x => x).ToArray(), Lengths = seg.Select((x, i) => 7.0 + 3 * i).ToArray(), Size = s,
                All = new[] { seg.Min(x => x[0]), q[1], q[2], seg.Max(x => x[3]), q[4], q[5] } };
            b.Total = 5 * b.Lengths.Sum();
            b.SumFactor = ExactMeshBoolean.SumTermsFactor(seg.Count);
            return b;
        }

        [Fact]
        public void GradientBound_AtADistanceExactlyAtTheNearLimit_TakesTheFarTermsAndTheNearTerms()
        {
            var q = new double[] { 0, 0, 0, 1, 1, 1 };
            foreach (bool both in new[] { false, true }) {
                var b = EqualityBoundary(q, both);
                double s = Math.Max(b.Size, 3);
                // the boundary really is at the limit
                double dA = ExactMeshBoolean.box_distance(q, 0, b.Boxes, 0) - 1e-12 * s;
                Assert.Equal(1e-3 * s, dA);
                double expected = RefGradient(q, b);
                Assert.Equal(expected, ExactMeshBoolean.gradient_bound(q, b));
                Assert.True(expected < double.MaxValue);
                if (both == false)
                    Assert.Equal(ExactMeshBoolean.sum_bound(ExactMeshBoolean.far_term(b.Total, 2, dA), b), expected);
                // cluster: the segment at the limit is a far one
                var cb = ExactMeshBoolean.cluster_bound(q, b);
                Assert.DoesNotContain(0, cb.Near);
                double farSum = 0;
                for (int t = 0; t < b.Lengths.Length; ++t) {
                    double dt = ExactMeshBoolean.box_distance(q, 0, b.Boxes, 6 * t) - 1e-12 * s;
                    if (dt >= 1e-3 * s) farSum += ExactMeshBoolean.far_term(b.Lengths[t], 1, dt);
                }
                Assert.Equal(farSum, cb.Far);
                Assert.Equal(expected, ExactMeshBoolean.gradient_bound(q, b, cb));
            }
        }

        [Fact]
        public void GradientBound_WithAClusterOfALargerBox_EvaluatesTheNearListAtTheQueryBox()
        {
            var q = new double[] { 0, 0, 0, 1, 1, 1 };
            var b = EqualityBoundary(q, true);
            double D = ExactMeshBoolean.box_distance(q, 0, b.Boxes, 0);
            // the cluster box reaches out below q, so that A and B are near segments of it; at q, A lies at the limit and B beyond
            var big = new double[] { -2.5 * D, 0, 0, 1, 1, 1 };
            var cb = ExactMeshBoolean.cluster_bound(big, b);
            Assert.True(cb.Near.Length >= 2, $"near {cb.Near.Length}");
            double expected = RefGradient(q, b, cb);
            Assert.True(expected < double.MaxValue);
            Assert.Equal(expected, ExactMeshBoolean.gradient_bound(q, b, cb));
        }

        [Fact]
        public void PieceCost_AtADistanceExactlyAtTheNearLimit_IsTheGradientBoundTimesTheDiameter()
        {
            Fixture f = Fixture.Instance.Value;
            ExactMeshArrangement arr = f.Arr;
            int checkedPieces = 0;
            for (int i = 0; i < arr.Output.Count && checkedPieces < 4; i += 29) {
                var ot = arr.Output[i];
                double err;
                double[] box = RefBox(arr, ot, out err);
                Vector3d pa = arr.Approx(ot.A), pb = arr.Approx(ot.B), pc = arr.Approx(ot.C);
                double diam = (Math.Max(pa.Distance(pb), Math.Max(pb.Distance(pc), pc.Distance(pa))) + 2 * err) * (1 + 8 * 1.1102230246251565e-16);
                foreach (bool both in new[] { false, true }) {
                    var b = EqualityBoundary(box, both);
                    double s = b.Size;
                    foreach (double x in box) s = Math.Max(s, 3 * Math.Abs(x));
                    Assert.Equal(1e-3 * s, ExactMeshBoolean.box_distance(box, 0, b.Boxes, 0) - 1e-12 * s);
                    double expected = RefGradient(box, b) * diam;
                    Assert.True(expected < double.MaxValue);
                    Assert.Equal(expected, (double)Call(f.Op, "piece_cost", i, b, new Dictionary<int, double>()));
                }
                ++checkedPieces;
            }
            Assert.Equal(4, checkedPieces);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // gradient_bound in the regime between the tiny and the large near limit
        // ---------------------------------------------------------------------------------------------------------------
        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void GradientAndClusterBound_AwayFromTheOrigin_AreTheDocumentedFunctionAlsoWhereTheDistanceIsBelowTheNearLimit(int seed)
        {
            var rnd = new Random(seed);
            // segments around a centre far from the origin (the near limit 1e-3 s is then large) and boxes at distances 1e2 .. 1e5 of them
            var cen = new Vector3d(4e6, -3e6, 5e6);
            Vector3d[] g = RandomSegments(rnd, 10, 1e3).Select(p => p + cen).ToArray();
            var b = ExactMeshBoolean.boundary_bound(g);
            int finite = 0, below = 0;
            for (int i = 0; i < 80; ++i) {
                Vector3d c = cen + new Vector3d((rnd.NextDouble() - 0.5) * 6e4, (rnd.NextDouble() - 0.5) * 6e4, (rnd.NextDouble() - 0.5) * 6e4);
                double hx = 1 + rnd.NextDouble() * 20, hy = 1 + rnd.NextDouble() * 20, hz = 1 + rnd.NextDouble() * 20;
                var box = new[] { Math.Round(c.x - hx), Math.Round(c.y - hy), Math.Round(c.z - hz), Math.Round(c.x + hx), Math.Round(c.y + hy), Math.Round(c.z + hz) };
                double s = b.Size;
                foreach (double x in box) s = Math.Max(s, 3 * Math.Abs(x));
                double d = ExactMeshBoolean.box_distance(box, 0, b.All, 0) - 1e-12 * s;
                double expected = RefGradient(box, b);
                Assert.Equal(expected, ExactMeshBoolean.gradient_bound(box, b));
                var cb = ExactMeshBoolean.cluster_bound(box, b);
                Assert.Equal(expected, ExactMeshBoolean.gradient_bound(box, b, cb));
                // a cluster around a larger box: the near list is evaluated at this box
                var bigBox = new[] { box[0] - 3e3, box[1] - 3e3, box[2] - 3e3, box[3] + 3e3, box[4] + 3e3, box[5] + 3e3 };
                var cb2 = ExactMeshBoolean.cluster_bound(bigBox, b);
                Assert.Equal(RefGradient(box, b, cb2), ExactMeshBoolean.gradient_bound(box, b, cb2));
                if (expected < double.MaxValue) ++finite;
                if (d < 1e-3 * s && d > 1e-12) ++below;
            }
            Assert.True(finite > 10 && below > 4, $"finite {finite} below {below}");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // carry_bound: the union box of two pieces from the cached boxes of their slots
        // ---------------------------------------------------------------------------------------------------------------
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void CarryBound_IsTheGradientBoundOfTheUnionBoxTimesTheCarryLength(bool withCluster)
        {
            Fixture f = Fixture.Instance.Value;
            ExactMeshArrangement arr = f.Arr;
            int n = arr.Output.Count;
            var loc = new int[n];
            for (int i = 0; i < n; ++i) loc[i] = (i * 37 + 11) % n;
            var boxes = new double[6 * n];
            var has = new bool[n];
            // a boundary far from the model: every pair finite; with a cluster, its box reaches the segments, which are then near ones
            double reach = withCluster ? 1.1e17 : 3e18;
            var far = new[] { new Vector3d(reach, reach, reach), new Vector3d(-reach, reach, -reach), new Vector3d(reach, -reach, 0.3 * reach), new Vector3d(-reach, -0.7 * reach, reach) };
            var b = ExactMeshBoolean.boundary_bound(far);
            MethodInfo carry = typeof(ExactMeshBoolean).GetMethod("carry_bound", NP);
            var rnd = new Random(12);
            var frames = new Dictionary<int, ExactMeshBoolean.LocalFrame>();
            Func<int, ExactMeshBoolean.Center> center = p => {
                int src = arr.Output[p].Source;
                ExactMeshBoolean.LocalFrame fr;
                if (!frames.TryGetValue(src, out fr)) frames[src] = fr = new ExactMeshBoolean.LocalFrame(f.Op, arr.Triangles[src]);
                return new ExactMeshBoolean.Center(f.Op, arr.Output[p], fr);
            };
            int finite = 0, fromP = 0, fromQ = 0, nearTotal = 0;
            for (int trial = 0; trial < 60; ++trial) {
                int p = rnd.Next(n), q = rnd.Next(n);
                if (p == q) continue;
                ExactMeshBoolean.Center cp = center(p), cq = center(q);
                double ep, eq;
                Vector3d gp = center(p).Local(out ep), gq = center(q).Local(out eq);
                double ignore1, ignore2;
                double[] bp = RefBox(arr, arr.Output[p], out ignore1), bq = RefBox(arr, arr.Output[q], out ignore2);
                var box = new double[6];
                for (int k = 0; k < 3; ++k) {
                    box[k] = Math.Min(bp[k], bq[k]);
                    box[k + 3] = Math.Max(bp[k + 3], bq[k + 3]);
                    if (bq[k] < bp[k]) ++fromQ; else ++fromP;
                    if (bq[k + 3] > bp[k + 3]) ++fromQ; else ++fromP;
                }
                double length = ExactMeshBoolean.carry_length(gp, gq, ep, eq);
                ExactMeshBoolean.ClusterBound cb = withCluster ? ExactMeshBoolean.cluster_bound(new[] { box[0] - 1e17, box[1] - 1e17, box[2] - 1e17, box[3] + 1e17, box[4] + 1e17, box[5] + 1e17 }, b) : null;
                if (cb != null) nearTotal += cb.Near.Length;
                double expected = double.IsNaN(length) ? double.PositiveInfinity : RefGradient(box, b, cb) * length;
                double got = (double)carry.Invoke(f.Op, new object[] { p, q, cp, cq, boxes, has, loc, b, cb });
                Assert.Equal(expected, got);
                Assert.True(has[loc[p]] && has[loc[q]]);
                if (got < double.MaxValue) ++finite;
            }
            Assert.True(finite > 40 && fromP > 20 && fromQ > 20, $"finite {finite} fromP {fromP} fromQ {fromQ}");
            if (withCluster) Assert.True(nearTotal > 20, $"near entries {nearTotal}");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // scaled_norm: the norm without overflow or underflow of the squares
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void ScaledNorm_IsTheNormOfTheScaledVector_AndZeroInfinityNaNPassThrough()
        {
            Assert.Equal(0.0, ExactMeshBoolean.scaled_norm(0, 0, 0));
            Assert.Equal(0.0, ExactMeshBoolean.scaled_norm(-0.0, 0, -0.0));
            Assert.Equal(double.PositiveInfinity, ExactMeshBoolean.scaled_norm(double.PositiveInfinity, 3, 4));
            Assert.Equal(double.PositiveInfinity, ExactMeshBoolean.scaled_norm(1, double.NegativeInfinity, 4));
            Assert.True(double.IsNaN(ExactMeshBoolean.scaled_norm(double.NaN, 3, 4)));
            Assert.True(double.IsNaN(ExactMeshBoolean.scaled_norm(1, 3, double.NaN)));
            foreach (int e in new[] { -1074 + 60, -600, -1, 0, 1, 500, 1000 })
                Assert.Equal(13 * Math.Pow(2, e), ExactMeshBoolean.scaled_norm(3 * Math.Pow(2, e), -4 * Math.Pow(2, e), 12 * Math.Pow(2, e)));
            // (subnormal inputs: the squares of the plain norm underflow to 0, the scaled one keeps the value)
            double tiny = double.Epsilon * 1024;
            Assert.Equal(13 * tiny, ExactMeshBoolean.scaled_norm(3 * tiny, 4 * tiny, 12 * tiny));
            Assert.Equal(5 * double.Epsilon, ExactMeshBoolean.scaled_norm(3 * double.Epsilon, 4 * double.Epsilon, 0));
            Assert.Equal(double.Epsilon, ExactMeshBoolean.scaled_norm(0, double.Epsilon, 0));
            // (the largest component decides the scale: a small first component, a large last one)
            Assert.Equal(13 * Math.Pow(2, 800), ExactMeshBoolean.scaled_norm(12 * Math.Pow(2, 800), 3 * Math.Pow(2, 800), 4 * Math.Pow(2, 800)));
            Assert.Equal(13 * Math.Pow(2, 800), ExactMeshBoolean.scaled_norm(3 * Math.Pow(2, 800), 4 * Math.Pow(2, 800), -12 * Math.Pow(2, 800)));
        }
    }
}
