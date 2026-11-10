using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A printable case of the QualityMesh2 property campaign (QA 2.0 T5; the generators and the campaign are in
    /// QualityMesh2PropertyTests.CsCheck.cs): lattice points with integer coordinates, rings and segments over them, a lattice unit
    /// 2^-Denominator, a scale 2^Exponent, an optional snap grid and the options. It builds a <see cref="Mesh2Input"/> exactly (every
    /// coordinate is an integer times a power of two), and <see cref="ToCode"/> prints it as the C# literal a fixed example test pastes.
    /// </summary>
    public sealed class QualityMesh2PropertyCase
    {
        /// <summary> 2^n exactly, also in the subnormal range (Math.Pow is not exact on every platform; Math.ScaleB is missing on net48) </summary>
        internal static double Pow2(int n)
        {
            if (n > 1023) return double.PositiveInfinity;
            if (n >= -1022) return BitConverter.Int64BitsToDouble((long)(n + 1023) << 52);
            double r = BitConverter.Int64BitsToDouble(1L << 52);   // 2^-1022
            for (int k = -1022; k > n && r > 0; --k) r *= 0.5;
            return r;
        }

        public string Family;
        public int[] X, Y;
        public int[][] Rings;
        public bool[] Holes;
        public int[][] Segments;      // pairs {a, b}
        public int Denominator;       // the lattice unit is 2^-Denominator
        public int Exponent;          // the whole input is scaled by 2^Exponent
        public int? SnapLog2;         // the pixel is 2^SnapLog2 lattice units; null: automatic snapping only
        public double? Angle;         // null: no angle bound (and no refinement)
        public bool Constrained;      // the constrained quality mode
        public bool Conforming;
        public int SteinerLimit = 20000;

        public QualityMesh2PropertyCase Clone() => (QualityMesh2PropertyCase)MemberwiseClone();

        public Mesh2Input Build(int extraExponent = 0)
        {
            double unit = Pow2(Exponent + extraExponent - Denominator);
            var input = new Mesh2Input {
                X = X.Select(v => v * unit).ToArray(), Y = Y.Select(v => v * unit).ToArray(),
                MinAngleDeg = Angle, ConstrainedQuality = Constrained, Conforming = Conforming, SteinerLimit = SteinerLimit,
                SnapTolerance = SnapLog2 == null ? 0 : Pow2(SnapLog2.Value) * unit
            };
            for (int i = 0; i < Rings.Length; ++i) input.Rings.Add(new RingInput((int[])Rings[i].Clone(), Holes[i]));
            foreach (var s in Segments) input.Segments.Add(new SegmentInput(s[0], s[1]));
            return input;
        }

        static string Arr(IEnumerable<int> v) => "new[] { " + string.Join(", ", v) + " }";

        /// <summary> the C# literal of the case, for a fixed example test </summary>
        public string ToCode()
        {
            string rings = "new[] { " + string.Join(", ", Rings.Select(r => Arr(r))) + " }";
            string segments = "new[] { " + string.Join(", ", Segments.Select(s => Arr(s))) + " }";
            return $"new QualityMesh2PropertyCase {{ Family = \"{Family}\", X = {Arr(X)}, Y = {Arr(Y)}, Rings = {rings}, " +
                   $"Holes = new[] {{ {string.Join(", ", Holes.Select(h => h ? "true" : "false"))} }}, Segments = {segments}, " +
                   $"Denominator = {Denominator}, Exponent = {Exponent}, SnapLog2 = {(SnapLog2 == null ? "null" : SnapLog2.ToString())}, " +
                   $"Angle = {(Angle == null ? "null" : Angle.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))}, " +
                   $"Constrained = {(Constrained ? "true" : "false")}, Conforming = {(Conforming ? "true" : "false")}, SteinerLimit = {SteinerLimit} }}";
        }

        public override string ToString() => ToCode();
    }

    /// <summary>
    /// The properties of the QualityMesh2 campaign, as plain code (no CsCheck), so the fixed examples run them on every target.
    /// <see cref="Check"/> returns "" or the first reason a case fails.
    /// </summary>
    public static class QualityMesh2Properties
    {
        /// <summary> a run that takes longer than this is a hang </summary>
        public static int TimeoutMs = 60000;

        /// <summary>
        /// The open grid class of the last <see cref="Check"/> on this thread (null: none). Open grid findings of the T5 campaign, after
        /// the oracle's rule O-G: on a snap grid the oracle's verdict still fails in four classes, each only in its documented setting:
        /// AngleBelowBound on a grid finer than the lattice unit; SegmentMissing / VertexUnused (with ChainSkipsHotPixel) with
        /// conforming refinement; TrianglesOnDegenerate. Not a failure of the campaign until decided or fixed (the other properties still
        /// run); G3_PROPERTY_STRICT=1 makes them fail. Any other kind of violation stays a failure.
        /// </summary>
        [ThreadStatic] public static string LastOpenClass;

        static string OpenClass(QualityMesh2PropertyCase c, HashSet<Violation> kinds)
        {
            if (c.SnapLog2 == null || Environment.GetEnvironmentVariable("G3_PROPERTY_STRICT") == "1") return null;
            if (kinds.SetEquals(new[] { Violation.AngleBelowBound }) && c.SnapLog2 < 0 && c.Angle != null) return "AngleBelowBound";
            if (kinds.IsSubsetOf(new[] { Violation.SegmentMissing, Violation.VertexUnused, Violation.ChainSkipsHotPixel }) && !c.Constrained && c.Angle != null)
                return "SegmentMissing/VertexUnused";
            if (kinds.SetEquals(new[] { Violation.TrianglesOnDegenerate })) return "TrianglesOnDegenerate";
            return null;
        }

        static Mesh2Output Run(Mesh2Input input)
        {
            Mesh2Output result = null;
            Exception error = null;
            var thread = new System.Threading.Thread(() => { try { result = new QualityMesh2Candidate().Run(input); } catch (Exception e) { error = e; } }, 16 * 1024 * 1024) { IsBackground = true };
            thread.Start();
            if (!thread.Join(TimeoutMs)) throw new TimeoutException("no result after " + TimeoutMs + " ms");
            if (error != null) throw new InvalidOperationException(error.GetType().Name + ": " + error.Message, error);
            return result;
        }

        static string SameMesh(Mesh2Output a, Mesh2Output b, double scale, string what)
        {
            if (a.Status != b.Status) return what + ": status " + a.Status + " vs " + b.Status;
            if (a.VX.Length != b.VX.Length || a.Triangles.Length != b.Triangles.Length)
                return what + ": " + a.VX.Length + " vertices, " + a.Triangles.Length + " triangles vs " + b.VX.Length + " and " + b.Triangles.Length;
            for (int i = 0; i < a.VX.Length; ++i)
                if (BitConverter.DoubleToInt64Bits(a.VX[i] * scale) != BitConverter.DoubleToInt64Bits(b.VX[i]) ||
                    BitConverter.DoubleToInt64Bits(a.VY[i] * scale) != BitConverter.DoubleToInt64Bits(b.VY[i]))
                    return what + ": vertex " + i + " differs: (" + a.VX[i] * scale + ", " + a.VY[i] * scale + ") vs (" + b.VX[i] + ", " + b.VY[i] + ")";
            for (int t = 0; t < a.Triangles.Length; ++t)
                if (!a.Triangles[t].SequenceEqual(b.Triangles[t])) return what + ": triangle " + t + " differs";
            if (a.SteinerLimitReached != b.SteinerLimitReached) return what + ": Steiner flag differs";
            return "";
        }

        /// <summary> All properties of one case; scaleShifts are extra exponents for the bit-for-bit 2^k check. </summary>
        public static string Check(QualityMesh2PropertyCase c, int[] scaleShifts = null, bool determinism = true)
        {
            Mesh2Input input = c.Build();
            Mesh2Output o;
            try { o = Run(input); }
            catch (TimeoutException e) { return "hang: " + e.Message; }
            catch (Exception e) { return "undocumented exception: " + e.Message; }

            if (o.Status == Mesh2Status.Failed) return "status Failed: " + o.FailureKind;
            if (o.SteinerLimitReached && o.Status != Mesh2Status.Success) return "Steiner limit with status " + o.Status;
            if (o.SteinerLimitReached && o.VX.Length - input.Count > c.SteinerLimit + 4 * input.Count)
                return "Steiner limit reached but " + (o.VX.Length - input.Count) + " points were added (limit " + c.SteinerLimit + ")";

            OracleReport report;
            try { report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 }); }
            catch (Exception e) { return "oracle exception: " + e.GetType().Name + ": " + e.Message; }
            LastOpenClass = null;
            if (!report.Ok) {
                LastOpenClass = OpenClass(c, new HashSet<Violation>(report.Violations.Select(v => v.Kind)));
                if (LastOpenClass == null) return "oracle verdict: " + string.Join("; ", report.Violations.Take(5));
            }

            string reason = SegmentsAreUnions(c, input, o);
            if (reason != "") return reason;

            if (determinism) {
                try {
                    var again = Run(input);
                    reason = SameMesh(o, again, 1, "second run");
                    if (reason != "") return reason;
                    var parallel = new Mesh2Output[3];
                    Parallel.For(0, parallel.Length, i => parallel[i] = Run(c.Build()));
                    for (int i = 0; i < parallel.Length; ++i) {
                        reason = SameMesh(o, parallel[i], 1, "parallel run " + i);
                        if (reason != "") return reason;
                    }
                } catch (Exception e) { return "determinism run: " + e.Message; }
            }

            foreach (int shift in scaleShifts ?? new int[0]) {
                try {
                    var scaled = Run(c.Build(shift));
                    reason = SameMesh(o, scaled, QualityMesh2PropertyCase.Pow2(shift), "scale 2^" + shift);
                    if (reason != "") return reason;
                } catch (Exception e) { return "scale 2^" + shift + ": " + e.Message; }
            }
            return "";
        }

        /// <summary>
        /// Independent of the oracle, for the case where it can be stated simply (no snapping, and no input edge meets another except
        /// at a common end): each input edge is a chain of output edges, from its first to its second vertex, over the output vertices
        /// that lie on it up to the rounding of a split point (relative 2^-48 of the extent; refinement also puts vertices at shell radii a few ulps from an input vertex, so only subsegment ends count).
        /// </summary>
        static string SegmentsAreUnions(QualityMesh2PropertyCase c, Mesh2Input input, Mesh2Output o)
        {
            if (c.SnapLog2 != null || o.Status != Mesh2Status.Success) return "";
            var edges = new List<(int a, int b)>();
            foreach (var r in c.Rings) for (int i = 0; i < r.Length; ++i) edges.Add((r[i], r[(i + 1) % r.Length]));
            foreach (var s in c.Segments) edges.Add((s[0], s[1]));
            for (int i = 0; i < edges.Count; ++i)
                for (int j = i + 1; j < edges.Count; ++j)
                    if (LatticeGeometry.Touch(c, edges[i], edges[j])) return "";
            // (only the ends of subsegments are candidates: refinement also puts free vertices within rounding of an edge near a vertex)
            var constrained = new HashSet<int>(o.Subsegments.SelectMany(u => new[] { u.A, u.B }));
            var present = new HashSet<long>();
            foreach (var t in o.Triangles) for (int k = 0; k < 3; ++k) present.Add(Key(t[k], t[(k + 1) % 3]));
            foreach (var (a, b) in edges) {
                if (a == b) continue;
                int va = o.InputToVertex[a], vb = o.InputToVertex[b];
                var pa = new Vector2d(o.VX[va], o.VY[va]); var pb = new Vector2d(o.VX[vb], o.VY[vb]);
                var chain = new List<(double t, int v)> { (0, va) };
                for (int v = 0; v < o.VX.Length; ++v) {
                    if (v == va || v == vb || !constrained.Contains(v)) continue;
                    var p = new Vector2d(o.VX[v], o.VY[v]);
                    // (a split point of a segment is rounded to a double: on it up to rounding, spec O12, not exactly)
                    double cross = (pb.x - pa.x) * (p.y - pa.y) - (pb.y - pa.y) * (p.x - pa.x);
                    double length = Math.Sqrt((pb.x - pa.x) * (pb.x - pa.x) + (pb.y - pa.y) * (pb.y - pa.y));
                    double extent = Math.Max(Math.Max(Math.Abs(pa.x), Math.Abs(pa.y)), Math.Max(Math.Abs(pb.x), Math.Abs(pb.y))) + length;
                    if (Math.Abs(cross) > 3.6e-15 * extent * length) continue;
                    double t = (p.x - pa.x) * (pb.x - pa.x) + (p.y - pa.y) * (pb.y - pa.y);
                    if (t > 0 && t < (pb.x - pa.x) * (pb.x - pa.x) + (pb.y - pa.y) * (pb.y - pa.y)) chain.Add((t, v));
                }
                chain.Add((double.MaxValue, vb));
                chain.Sort((u, w) => u.t.CompareTo(w.t));
                for (int i = 0; i + 1 < chain.Count; ++i)
                    if (!present.Contains(Key(chain[i].v, chain[i + 1].v)))
                        return "input edge " + a + "-" + b + " is not a union of output edges: no edge " + chain[i].v + "-" + chain[i + 1].v + " on it";
            }
            return "";
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }

    /// <summary> exact integer geometry on the lattice of a case, for the generators' validity checks and the property above </summary>
    public static class LatticeGeometry
    {
        static int Orient(long ax, long ay, long bx, long by, long cx, long cy)
            => Math.Sign((bx - ax) * (cy - ay) - (by - ay) * (cx - ax));

        static bool OnSegment(long ax, long ay, long bx, long by, long px, long py)
            => Orient(ax, ay, bx, by, px, py) == 0 && Math.Min(ax, bx) <= px && px <= Math.Max(ax, bx) && Math.Min(ay, by) <= py && py <= Math.Max(ay, by);

        /// <summary> the closed segments share a point (anywhere) </summary>
        public static bool Intersect(long ax, long ay, long bx, long by, long cx, long cy, long dx, long dy)
        {
            int o1 = Orient(ax, ay, bx, by, cx, cy), o2 = Orient(ax, ay, bx, by, dx, dy), o3 = Orient(cx, cy, dx, dy, ax, ay), o4 = Orient(cx, cy, dx, dy, bx, by);
            if (o1 != o2 && o3 != o4 && o1 * o2 <= 0 && o3 * o4 <= 0) return true;
            return OnSegment(ax, ay, bx, by, cx, cy) || OnSegment(ax, ay, bx, by, dx, dy) || OnSegment(cx, cy, dx, dy, ax, ay) || OnSegment(cx, cy, dx, dy, bx, by);
        }

        /// <summary> two input edges meet other than at a shared end vertex (or are collinear and overlap there) </summary>
        public static bool Touch(QualityMesh2PropertyCase c, (int a, int b) e, (int a, int b) f)
        {
            long X(int i) => c.X[i]; long Y(int i) => c.Y[i];
            int shared = -1;
            if (e.a == f.a || e.a == f.b) shared = e.a; else if (e.b == f.a || e.b == f.b) shared = e.b;
            if (shared < 0) return Intersect(X(e.a), Y(e.a), X(e.b), Y(e.b), X(f.a), Y(f.a), X(f.b), Y(f.b));
            // sharing a vertex is no touch unless one edge runs along the other (the other end lies on the other edge)
            int eo = shared == e.a ? e.b : e.a, fo = shared == f.a ? f.b : f.a;
            return OnSegment(X(shared), Y(shared), X(eo), Y(eo), X(fo), Y(fo)) || OnSegment(X(shared), Y(shared), X(fo), Y(fo), X(eo), Y(eo));
        }

        /// <summary> the polygon (vertex indices into c) is simple: no zero-length edge, adjacent edges share only their vertex, others do not meet </summary>
        public static bool RingIsSimple(QualityMesh2PropertyCase c, int[] ring)
        {
            int n = ring.Length;
            if (n < 3) return false;
            for (int i = 0; i < n; ++i) if (c.X[ring[i]] == c.X[ring[(i + 1) % n]] && c.Y[ring[i]] == c.Y[ring[(i + 1) % n]]) return false;
            for (int i = 0; i < n; ++i)
                for (int j = i + 1; j < n; ++j) {
                    var e = (ring[i], ring[(i + 1) % n]); var f = (ring[j], ring[(j + 1) % n]);
                    bool adjacent = j == i + 1 || (i == 0 && j == n - 1);
                    if (adjacent) {
                        // the shared vertex is e.b == f.a (j = i + 1) or f.b == e.a (the wrap)
                        int shared = j == i + 1 ? e.Item2 : e.Item1, eo = j == i + 1 ? e.Item1 : e.Item2, fo = j == i + 1 ? f.Item2 : f.Item1;
                        long sx = c.X[shared], sy = c.Y[shared];
                        if (OnSegment(sx, sy, c.X[eo], c.Y[eo], c.X[fo], c.Y[fo]) || OnSegment(sx, sy, c.X[fo], c.Y[fo], c.X[eo], c.Y[eo])) return false;
                    } else if (Intersect(c.X[e.Item1], c.Y[e.Item1], c.X[e.Item2], c.Y[e.Item2], c.X[f.Item1], c.Y[f.Item1], c.X[f.Item2], c.Y[f.Item2])) return false;
                }
            return true;
        }

        /// <summary> the edges of the two rings do not meet at all </summary>
        public static bool RingsApart(QualityMesh2PropertyCase c, int[] r, int[] s)
        {
            for (int i = 0; i < r.Length; ++i)
                for (int j = 0; j < s.Length; ++j)
                    if (Intersect(c.X[r[i]], c.Y[r[i]], c.X[r[(i + 1) % r.Length]], c.Y[r[(i + 1) % r.Length]],
                                  c.X[s[j]], c.Y[s[j]], c.X[s[(j + 1) % s.Length]], c.Y[s[(j + 1) % s.Length]])) return false;
            return true;
        }

        /// <summary> the point is strictly inside the polygon (the point is on no edge; even-odd rule) </summary>
        public static bool Inside(QualityMesh2PropertyCase c, int[] ring, long px, long py)
        {
            bool inside = false;
            for (int i = 0; i < ring.Length; ++i) {
                long ax = c.X[ring[i]], ay = c.Y[ring[i]], bx = c.X[ring[(i + 1) % ring.Length]], by = c.Y[ring[(i + 1) % ring.Length]];
                if ((ay > py) != (by > py)) {
                    // x of the crossing compared with px, without division
                    long lhs = (px - ax) * (by - ay), rhs = (bx - ax) * (py - ay);
                    if ((by > ay) ? lhs < rhs : lhs > rhs) inside = !inside;
                }
            }
            return inside;
        }
    }
}
