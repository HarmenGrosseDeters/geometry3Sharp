using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Meshing2Acceptance
{
    /// <summary>
    /// Records every step, so that <see cref="Text"/> is a complete description of the input (the notation of
    /// docs/design/MESHING2D_GOLDEN_FORMAT.md, section 2): points get their numbers in order of first appearance.
    /// </summary>
    public sealed class B
    {
        readonly List<(double x, double y)> pts = new List<(double, double)>();
        readonly Dictionary<(double, double), int> index = new Dictionary<(double, double), int>();
        readonly Mesh2Input input = new Mesh2Input();
        readonly List<string> text = new List<string>();

        public static string Num(double v)
        {
            if (v == 0) return "0";
            var q = Q.FromDouble(v);
            if (q.D.IsOne && BigInteger.Abs(q.N) < (BigInteger.One << 64)) return q.ToString();
            if (q.D.IsPowerOfTwo && q.D <= (BigInteger.One << 40) && BigInteger.Abs(q.N) < (BigInteger.One << 64)) {
                string s = q.ToDecimal(40).TrimEnd('0');
                return s;
            }
            return "0x" + BitConverter.DoubleToInt64Bits(v).ToString("x16");
        }

        int P(double x, double y)
        {
            x = x == 0 ? 0.0 : x; y = y == 0 ? 0.0 : y;
            if (index.TryGetValue((x, y), out int i)) return i;
            i = pts.Count; pts.Add((x, y)); index[(x, y)] = i;
            return i;
        }

        int[] Ps(double[] xy) => Enumerable.Range(0, xy.Length / 2).Select(i => P(xy[2 * i], xy[2 * i + 1])).ToArray();
        static string List(double[] xy) => string.Join(" ", xy.Select(Num));

        public B Point(double x, double y) { P(x, y); text.Add("point(" + Num(x) + " " + Num(y) + ")"); return this; }
        public B PointNoMerge(double x, double y) { pts.Add((x, y)); text.Add("point!(" + Num(x) + " " + Num(y) + ")"); return this; }
        public B Square(double s = 10) => Outer(0, 0, s, 0, s, s, 0, s);
        public B Rect(double x0, double y0, double x1, double y1) => Outer(x0, y0, x1, y0, x1, y1, x0, y1);
        public B Outer(params double[] xy) { input.Rings.Add(new RingInput(Ps(xy), false)); text.Add("outer(" + List(xy) + ")"); return this; }
        public B Hole(params double[] xy) { input.Rings.Add(new RingInput(Ps(xy), true)); text.Add("hole(" + List(xy) + ")"); return this; }
        public B HoleSeed(double x, double y) { input.HoleSeeds.Add(new[] { x, y }); text.Add("hole_seed(" + Num(x) + " " + Num(y) + ")"); return this; }
        public B Seg(double x0, double y0, double x1, double y1, int marker = 0, bool locked = false)
        {
            input.Segments.Add(new SegmentInput(P(x0, y0), P(x1, y1), marker, locked));
            text.Add("seg(" + Num(x0) + " " + Num(y0) + " " + Num(x1) + " " + Num(y1) + (marker != 0 || locked ? " " + marker : "") + (locked ? " locked" : "") + ")");
            return this;
        }
        public B Segs(params double[] v) { for (int i = 0; i + 3 < v.Length; i += 4) Seg(v[i], v[i + 1], v[i + 2], v[i + 3]); return this; }
        public B Polyline(params double[] xy) { for (int i = 0; i + 3 < xy.Length; i += 2) Seg(xy[i], xy[i + 1], xy[i + 2], xy[i + 3]); return this; }
        /// <summary>count segments with endpoints drawn from SplitMix64(seed), each coordinate Next() % (range + 1), shifted by the offset.</summary>
        public B RandomSegments(int range, int count, ulong seed, bool locked = false, int marker = 0)
        {
            var random = new SplitMix64(seed);
            var saved = text.Count;
            for (int i = 0; i < count; ++i) {
                double a = random.Next() % (ulong)(range + 1), b = random.Next() % (ulong)(range + 1), c = random.Next() % (ulong)(range + 1), d = random.Next() % (ulong)(range + 1);
                Seg(a, b, c, d, marker, locked);
            }
            text.RemoveRange(saved, text.Count - saved);
            text.Add("random_segments(range=" + range + " count=" + count + " seed=" + seed + (locked ? " locked" : "") + ")");
            return this;
        }
        public B RandomPoints(int range, int count, ulong seed)
        {
            var random = new SplitMix64(seed);
            var saved = text.Count;
            for (int i = 0; i < count; ++i) {
                double a = random.Next() % (ulong)(range + 1), b = random.Next() % (ulong)(range + 1);
                Point(a, b);
            }
            text.RemoveRange(saved, text.Count - saved);
            text.Add("random_points(range=" + range + " count=" + count + " seed=" + seed + ")");
            return this;
        }
        public B RegionSeed(double x, double y, int label, double? maxArea = null)
        {
            input.Regions.Add(new RegionInput { Seed = new[] { x, y }, Label = label, MaxArea = maxArea });
            text.Add("region_seed(" + Num(x) + " " + Num(y) + " " + label + (maxArea == null ? "" : " " + Num(maxArea.Value)) + ")");
            return this;
        }
        public B RegionRing(int label, double? maxArea, params double[] xy)
        {
            input.Regions.Add(new RegionInput { Ring = Ps(xy), Label = label, MaxArea = maxArea });
            text.Add("region_ring(" + label + " " + (maxArea == null ? "-" : Num(maxArea.Value)) + " " + List(xy) + ")");
            return this;
        }
        public B Min(double degrees) { input.MinAngleDeg = degrees; text.Add("min_angle(" + Num(degrees) + ")"); return this; }
        public B Area(double v) { input.MaxArea = v; text.Add("max_area(" + Num(v) + ")"); return this; }
        public B Limit(int n) { input.SteinerLimit = n; text.Add("steiner_limit(" + n + ")"); return this; }
        public B Snap(double t) { input.SnapTolerance = t; text.Add("snap(" + Num(t) + ")"); return this; }
        public B NoNoding() { input.ResolveCrossings = false; text.Add("no_noding"); return this; }
        public B Conforming() { input.Conforming = true; text.Add("conforming"); return this; }

        public string Text => string.Join(" ", text);
        public Mesh2Input Build()
        {
            input.X = pts.Select(p => p.x).ToArray();
            input.Y = pts.Select(p => p.y).ToArray();
            return input;
        }
    }
}
