// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Exact strictly-interior point finder for closed 2D rings (spec I4, F-49, F-50, decision D10).
    /// Uses exact even-odd parity (<see cref="ExactPredicates.Orient2d"/>) to certify every candidate,
    /// selects the candidate with largest boundary clearance across triangle incenter/centroid and
    /// horizontal/vertical scanlines, and falls back to exact rational scanline intersections when
    /// double-precision intersections cannot resolve a narrow interior.
    /// </summary>
    internal static class RingInterior2
    {
        readonly struct Edge
        {
            public readonly Vector2d A, B;
            public readonly Vector2d Lo, Hi; // ordered by y (Lo.y <= Hi.y)
            public readonly Vector2d Left, Right; // ordered by x (Left.x <= Right.x)
            public readonly double MinX, MaxX, MinY, MaxY;

            public Edge(Vector2d a, Vector2d b)
            {
                A = a;
                B = b;
                if (a.y <= b.y) { Lo = a; Hi = b; } else { Lo = b; Hi = a; }
                if (a.x <= b.x) { Left = a; Right = b; } else { Left = b; Right = a; }
                MinX = Left.x; MaxX = Right.x;
                MinY = Lo.y; MaxY = Hi.y;
            }
        }

        readonly struct ExactRat : IComparable<ExactRat>
        {
            public readonly BigInteger Num;
            public readonly BigInteger Den; // strictly positive
            public readonly int Exp;        // value = (Num / Den) * 2^Exp

            public ExactRat(BigInteger num, BigInteger den, int exp)
            {
                if (den.Sign < 0) { num = -num; den = -den; }
                Num = num;
                Den = den;
                Exp = exp;
            }

            public int CompareTo(ExactRat other)
            {
                int s1 = Num.Sign, s2 = other.Num.Sign;
                if (s1 != s2) return s1.CompareTo(s2);
                if (s1 == 0) return 0;

                // Approximate log2(|val|) = BitLength(|Num|) - BitLength(Den) + Exp.
                // Exact value lies strictly inside (2^(mag - 1), 2^(mag + 1)), so |mag1 - mag2| >= 2 decides without shifting.
                long mag1 = ExactPoint3.BitLength(Num) - ExactPoint3.BitLength(Den) + Exp;
                long mag2 = ExactPoint3.BitLength(other.Num) - ExactPoint3.BitLength(other.Den) + other.Exp;
                if (mag1 - mag2 >= 2) return s1;
                if (mag2 - mag1 >= 2) return -s1;

                int lo = Math.Min(Exp, other.Exp);
                BigInteger lhs = (Exp > lo ? Num << (Exp - lo) : Num) * other.Den;
                BigInteger rhs = (other.Exp > lo ? other.Num << (other.Exp - lo) : other.Num) * Den;
                return lhs.CompareTo(rhs);
            }
        }

        public static bool TryGetInteriorPoint(IReadOnlyList<Vector2d> ring, out Vector2d point)
        {
            if (ring == null) throw new ArgumentNullException(nameof(ring));
            int count = ring.Count;
            var verts = new List<Vector2d>(count);
            for (int i = 0; i < count; ++i) {
                var p = ValidateAndNormalize(ring[i], i);
                if (verts.Count == 0 || verts[verts.Count - 1] != p)
                    verts.Add(p);
            }
            return FindInterior(verts, out point);
        }

        public static bool TryGetInteriorPoint(IReadOnlyList<Vector2d> points, IReadOnlyList<int> ringIndices, out Vector2d point)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (ringIndices == null) throw new ArgumentNullException(nameof(ringIndices));
            int pCount = points.Count;
            int rCount = ringIndices.Count;
            var verts = new List<Vector2d>(rCount);
            for (int i = 0; i < rCount; ++i) {
                int idx = ringIndices[i];
                if (idx < 0 || idx >= pCount)
                    throw new ArgumentOutOfRangeException(nameof(ringIndices), "ring index " + idx + " is outside [0, " + pCount + ")");
                var p = ValidateAndNormalize(points[idx], idx);
                if (verts.Count == 0 || verts[verts.Count - 1] != p)
                    verts.Add(p);
            }
            return FindInterior(verts, out point);
        }

        static Vector2d ValidateAndNormalize(Vector2d p, int index)
        {
            if (double.IsNaN(p.x) || double.IsInfinity(p.x) || Math.Abs(p.x) > DelaunayMesh2.CoordinateLimit)
                throw new ArgumentException("ring point " + index + " has an invalid x coordinate: " + p.x);
            if (double.IsNaN(p.y) || double.IsInfinity(p.y) || Math.Abs(p.y) > DelaunayMesh2.CoordinateLimit)
                throw new ArgumentException("ring point " + index + " has an invalid y coordinate: " + p.y);
            return new Vector2d(p.x == 0 ? 0.0 : p.x, p.y == 0 ? 0.0 : p.y);
        }

        static bool FindInterior(List<Vector2d> verts, out Vector2d point)
        {
            point = default(Vector2d);
            while (verts.Count > 1 && verts[verts.Count - 1] == verts[0])
                verts.RemoveAt(verts.Count - 1);
            if (verts.Count < 3) return false;

            // Reject all-collinear rings immediately (exact).
            bool collinear = true;
            Vector2d v0 = verts[0], v1 = verts[1];
            for (int i = 2; i < verts.Count; ++i) {
                if (ExactPredicates.Orient2d(v0, v1, verts[i]) != 0) { collinear = false; break; }
            }
            if (collinear) return false;

            int n = verts.Count;
            var edges = new Edge[n];
            double maxAbs = 0;
            for (int i = 0; i < n; ++i) {
                var a = verts[i];
                var b = verts[(i + 1) % n];
                edges[i] = new Edge(a, b);
                double ma = Math.Max(Math.Abs(a.x), Math.Abs(a.y));
                if (ma > maxAbs) maxAbs = ma;
            }
            if (maxAbs == 0) return false;

            // Cancel identical undirected edges modulo 2 for candidate generation (a folded back-and-forth ring
            // cancels completely and has empty even-odd interior). Keep all original edges for boundary checks!
            var activeEdges = CancelDuplicateEdgesMod2(edges);
            if (activeEdges.Length < 3) return false;

            // Power-of-two shift for overflow/underflow-free, 2^k-scale-invariant clearance scoring.
            ExactPoint3.Decompose(maxAbs, out var mant, out int exp);
            int shift = -(int)(exp + ExactPoint3.BitLength(mant) - 1);

            bool found = false;
            Vector2d best = default(Vector2d);
            double bestDist2 = -1;

            bool Consider(Vector2d c)
            {
                if (double.IsNaN(c.x) || double.IsNaN(c.y) || double.IsInfinity(c.x) || double.IsInfinity(c.y)) return false;
                c = new Vector2d(c.x == 0 ? 0.0 : c.x, c.y == 0 ? 0.0 : c.y);
                if (!IsStrictlyInside(edges, c)) return false;
                double d2 = MinDist2Scaled(edges, c, shift);
                if (!found || d2 > bestDist2) {
                    found = true;
                    best = c;
                    bestDist2 = d2;
                }
                return true;
            }

            // Triangle fast candidates: incenter (optimal clearance for obtuse/thin triangles) and centroid.
            if (verts.Count == 3) {
                var a = verts[0];
                var b = verts[1];
                var c = verts[2];
                double la = ScaledDist(b, c, shift);
                double lb = ScaledDist(c, a, shift);
                double lc = ScaledDist(a, b, shift);
                double perim = la + lb + lc;
                if (perim > 0 && !double.IsInfinity(perim)) {
                    // Offset from vertex a to retain full precision at large offsets (e.g. UTM 5.7e6).
                    double ix = a.x + (lb * (b.x - a.x) + lc * (c.x - a.x)) / perim;
                    double iy = a.y + (lb * (b.y - a.y) + lc * (c.y - a.y)) / perim;
                    Consider(new Vector2d(ix, iy));
                }
                double cx = a.x + ((b.x - a.x) + (c.x - a.x)) / 3.0;
                double cy = a.y + ((b.y - a.y) + (c.y - a.y)) / 3.0;
                Consider(new Vector2d(cx, cy));
            }

            // Distinct sorted Y and X coordinates for horizontal and vertical scanline slabs.
            var ys = DistinctSorted(activeEdges, horizontal: true);
            var xs = DistinctSorted(activeEdges, horizontal: false);

            var hSlabs = SelectSampledSlabs(ys);
            var vSlabs = SelectSampledSlabs(xs);

            // Phase 1: Fast double-precision scanlines on sampled horizontal & vertical slabs (O(n log n)).
            // Probing only the widest spans per scanline bounds Consider calls to O(1) per slab.
            var crossBuf = new List<double>(16);
            var spanBuf = new List<(double width, double lo, double hi)>(16);
            for (int i = 0; i < hSlabs.Count; ++i) {
                int j = hSlabs[i];
                ProbeDoubleHorizontal(activeEdges, ys[j], ys[j + 1], crossBuf, spanBuf, Consider, maxSpansToTest: 4, stopOnHit: false);
            }
            for (int i = 0; i < vSlabs.Count; ++i) {
                int j = vSlabs[i];
                ProbeDoubleVertical(activeEdges, xs[j], xs[j + 1], crossBuf, spanBuf, Consider, maxSpansToTest: 4, stopOnHit: false);
            }
            if (found) { point = best; return true; }

            // Phase 2a: Exact rational scanlines on the sampled slabs (handles narrow slivers at UTM scale
            // where double intersection rounding falls outside the interior, and bowties crossing at slab midpoints).
            long exactBudget = 64L * activeEdges.Length + 4096;
            for (int i = 0; i < hSlabs.Count && exactBudget > 0; ++i) {
                int j = hSlabs[i];
                if (ProbeExactHorizontalSlab(activeEdges, ys[j], ys[j + 1], Consider, ref exactBudget)) {
                    point = best;
                    return true;
                }
            }
            for (int i = 0; i < vSlabs.Count && exactBudget > 0; ++i) {
                int j = vSlabs[i];
                if (ProbeExactVerticalSlab(activeEdges, xs[j], xs[j + 1], Consider, ref exactBudget)) {
                    point = best;
                    return true;
                }
            }

            // Phase 2b: Sweep over remaining horizontal & vertical slabs with bounded active-edge work
            // (finds a sole interior lobe hidden outside the 16 sampled slabs while keeping worst-case O(n log n)).
            long sweepBudget = 64L * activeEdges.Length + 8192;
            if (ys.Length - 1 > hSlabs.Count) {
                SweepAllHorizontal(activeEdges, ys, hSlabs, crossBuf, spanBuf, Consider, () => found, ref sweepBudget, ref exactBudget);
                if (found) { point = best; return true; }
            }
            if (xs.Length - 1 > vSlabs.Count) {
                SweepAllVertical(activeEdges, xs, vSlabs, crossBuf, spanBuf, Consider, () => found, ref sweepBudget, ref exactBudget);
                if (found) { point = best; return true; }
            }

            return false;
        }

        static Edge[] CancelDuplicateEdgesMod2(Edge[] edges)
        {
            var counts = new Dictionary<(double, double, double, double), int>(edges.Length);
            var order = new List<Edge>(edges.Length);
            for (int i = 0; i < edges.Length; ++i) {
                var e = edges[i];
                var k = EdgeKey(e.A, e.B);
                counts.TryGetValue(k, out int c);
                counts[k] = c + 1;
            }
            var emitted = new HashSet<(double, double, double, double)>();
            for (int i = 0; i < edges.Length; ++i) {
                var e = edges[i];
                var k = EdgeKey(e.A, e.B);
                if ((counts[k] & 1) != 0 && emitted.Add(k))
                    order.Add(e);
            }
            return order.ToArray();
        }

        static (double, double, double, double) EdgeKey(Vector2d a, Vector2d b)
        {
            if (a.x < b.x || (a.x == b.x && a.y <= b.y))
                return (a.x, a.y, b.x, b.y);
            return (b.x, b.y, a.x, a.y);
        }

        static double[] DistinctSorted(Edge[] edges, bool horizontal)
        {
            var vals = new double[edges.Length * 2];
            for (int i = 0; i < edges.Length; ++i) {
                vals[2 * i] = horizontal ? edges[i].Lo.y : edges[i].Left.x;
                vals[2 * i + 1] = horizontal ? edges[i].Hi.y : edges[i].Right.x;
            }
            Array.Sort(vals);
            int w = 0;
            for (int i = 0; i < vals.Length; ++i) {
                if (w == 0 || vals[i] != vals[w - 1])
                    vals[w++] = vals[i];
            }
            if (w == vals.Length) return vals;
            var res = new double[w];
            Array.Copy(vals, res, w);
            return res;
        }

        static List<int> SelectSampledSlabs(double[] coords)
        {
            int slabs = coords.Length - 1;
            var selected = new List<int>(Math.Min(slabs, 16));
            if (slabs <= 0) return selected;
            if (slabs <= 16) {
                for (int j = 0; j < slabs; ++j) selected.Add(j);
                return selected;
            }
            var set = new HashSet<int>();
            // 8 evenly spaced quantile slabs
            for (int q = 0; q < 8; ++q)
                set.Add((int)((long)q * slabs / 8));
            // Top 8 widest slabs (tie-broken by lower index)
            var widths = new (double w, int idx)[slabs];
            for (int j = 0; j < slabs; ++j) widths[j] = (coords[j + 1] - coords[j], j);
            Array.Sort(widths, (a, b) => {
                int c = b.w.CompareTo(a.w);
                return c != 0 ? c : a.idx.CompareTo(b.idx);
            });
            for (int k = 0; k < 8 && k < slabs; ++k) set.Add(widths[k].idx);
            selected.AddRange(set);
            selected.Sort();
            return selected;
        }

        static void ProbeDoubleHorizontal(IReadOnlyList<Edge> edges, double y0, double y1, List<double> cross,
            List<(double width, double lo, double hi)> spans, Func<Vector2d, bool> consider, int maxSpansToTest, bool stopOnHit)
        {
            double yMid = Midpoint(y0, y1);
            if (!(yMid > y0 && yMid < y1)) return;
            cross.Clear();
            for (int i = 0; i < edges.Count; ++i) {
                var e = edges[i];
                if (e.Lo.y < yMid && yMid < e.Hi.y) {
                    double t = (yMid - e.Lo.y) / (e.Hi.y - e.Lo.y);
                    cross.Add(e.Lo.x + (e.Hi.x - e.Lo.x) * t);
                }
            }
            if (cross.Count < 2) return;
            cross.Sort();
            spans.Clear();
            for (int k = 0; k + 1 < cross.Count; k += 2) {
                double lo = cross[k], hi = cross[k + 1];
                double xMid = Midpoint(lo, hi);
                if (xMid > lo && xMid < hi)
                    spans.Add((hi - lo, lo, hi));
            }
            if (spans.Count == 0) return;
            spans.Sort((a, b) => {
                int c = b.width.CompareTo(a.width);
                return c != 0 ? c : a.lo.CompareTo(b.lo);
            });
            int limit = Math.Min(spans.Count, maxSpansToTest);
            for (int i = 0; i < limit; ++i) {
                var (_, lo, hi) = spans[i];
                double xMid = Midpoint(lo, hi);
                bool hit = consider(new Vector2d(xMid, yMid));
                // If an internal retraced slit passes through the midpoint, test quarter-points so clearance stays high.
                if (!hit) {
                    double q1 = Midpoint(lo, xMid);
                    if (q1 > lo && q1 < xMid) hit = consider(new Vector2d(q1, yMid));
                    if (!hit) {
                        double q3 = Midpoint(xMid, hi);
                        if (q3 > xMid && q3 < hi) hit = consider(new Vector2d(q3, yMid));
                    }
                }
                if (hit && stopOnHit) return;
            }
        }

        static void ProbeDoubleVertical(IReadOnlyList<Edge> edges, double x0, double x1, List<double> cross,
            List<(double width, double lo, double hi)> spans, Func<Vector2d, bool> consider, int maxSpansToTest, bool stopOnHit)
        {
            double xMid = Midpoint(x0, x1);
            if (!(xMid > x0 && xMid < x1)) return;
            cross.Clear();
            for (int i = 0; i < edges.Count; ++i) {
                var e = edges[i];
                if (e.Left.x < xMid && xMid < e.Right.x) {
                    double t = (xMid - e.Left.x) / (e.Right.x - e.Left.x);
                    cross.Add(e.Left.y + (e.Right.y - e.Left.y) * t);
                }
            }
            if (cross.Count < 2) return;
            cross.Sort();
            spans.Clear();
            for (int k = 0; k + 1 < cross.Count; k += 2) {
                double lo = cross[k], hi = cross[k + 1];
                double yMid = Midpoint(lo, hi);
                if (yMid > lo && yMid < hi)
                    spans.Add((hi - lo, lo, hi));
            }
            if (spans.Count == 0) return;
            spans.Sort((a, b) => {
                int c = b.width.CompareTo(a.width);
                return c != 0 ? c : a.lo.CompareTo(b.lo);
            });
            int limit = Math.Min(spans.Count, maxSpansToTest);
            for (int i = 0; i < limit; ++i) {
                var (_, lo, hi) = spans[i];
                double yMid = Midpoint(lo, hi);
                bool hit = consider(new Vector2d(xMid, yMid));
                if (!hit) {
                    double q1 = Midpoint(lo, yMid);
                    if (q1 > lo && q1 < yMid) hit = consider(new Vector2d(xMid, q1));
                    if (!hit) {
                        double q3 = Midpoint(yMid, hi);
                        if (q3 > yMid && q3 < hi) hit = consider(new Vector2d(xMid, q3));
                    }
                }
                if (hit && stopOnHit) return;
            }
        }

        static bool ProbeExactHorizontalSlab(IReadOnlyList<Edge> edges, double y0, double y1, Func<Vector2d, bool> consider, ref long exactBudget)
        {
            double yMid = Midpoint(y0, y1);
            if (!(yMid > y0 && yMid < y1)) return false;
            bool hasActiveEdges = ProbeExactHorizontalLine(edges, yMid, consider, ref exactBudget, out bool hit);
            if (hit) return true;
            // Only skip extra lines when fewer than 2 edges cross the slab at all (no vertex lies strictly inside (y0, y1)).
            // When 2+ edges cross at yMid (e.g. a bowtie crossing at yMid), extraY still finds the interior!
            if (!hasActiveEdges || exactBudget <= 0) return false;

            double[] extraY = { Midpoint(y0, yMid), Midpoint(yMid, y1), NextUp(y0), NextDown(y1) };
            for (int i = 0; i < extraY.Length && exactBudget > 0; ++i) {
                double ys = extraY[i];
                if (ys > y0 && ys < y1 && ys != yMid) {
                    ProbeExactHorizontalLine(edges, ys, consider, ref exactBudget, out hit);
                    if (hit) return true;
                }
            }
            return false;
        }

        static bool ProbeExactHorizontalLine(IReadOnlyList<Edge> edges, double yLine, Func<Vector2d, bool> consider, ref long exactBudget, out bool foundAny)
        {
            foundAny = false;
            int activeCount = 0;
            for (int i = 0; i < edges.Count; ++i) {
                if (edges[i].Lo.y < yLine && yLine < edges[i].Hi.y) ++activeCount;
            }
            if (activeCount < 2) return false;
            if (activeCount > exactBudget) { exactBudget = 0; return true; }
            exactBudget -= activeCount;

            var xs = new List<ExactRat>(activeCount);
            for (int i = 0; i < edges.Count; ++i) {
                var e = edges[i];
                if (e.Lo.y < yLine && yLine < e.Hi.y)
                    xs.Add(ExactIntercept(e.Lo.x, e.Lo.y, e.Hi.x, e.Hi.y, yLine));
            }
            xs.Sort();
            int testedSpans = 0;
            for (int k = 0; k + 1 < xs.Count && testedSpans < 8; k += 2) {
                if (xs[k].CompareTo(xs[k + 1]) >= 0) continue;
                ++testedSpans;
                double xMid = ExactMidpointDouble(xs[k], xs[k + 1]);
                double xQ1 = ExactQuarterDouble(xs[k], xs[k + 1], firstQuarter: true);
                double xQ3 = ExactQuarterDouble(xs[k], xs[k + 1], firstQuarter: false);
                if (consider(new Vector2d(xMid, yLine))
                    || consider(new Vector2d(xQ1, yLine))
                    || consider(new Vector2d(xQ3, yLine))
                    || consider(new Vector2d(NextUp(xMid), yLine))
                    || consider(new Vector2d(NextDown(xMid), yLine))) {
                    foundAny = true;
                    return true;
                }
            }
            return true;
        }

        static bool ProbeExactVerticalSlab(IReadOnlyList<Edge> edges, double x0, double x1, Func<Vector2d, bool> consider, ref long exactBudget)
        {
            double xMid = Midpoint(x0, x1);
            if (!(xMid > x0 && xMid < x1)) return false;
            bool hasActiveEdges = ProbeExactVerticalLine(edges, xMid, consider, ref exactBudget, out bool hit);
            if (hit) return true;
            if (!hasActiveEdges || exactBudget <= 0) return false;

            double[] extraX = { Midpoint(x0, xMid), Midpoint(xMid, x1), NextUp(x0), NextDown(x1) };
            for (int i = 0; i < extraX.Length && exactBudget > 0; ++i) {
                double xs = extraX[i];
                if (xs > x0 && xs < x1 && xs != xMid) {
                    ProbeExactVerticalLine(edges, xs, consider, ref exactBudget, out hit);
                    if (hit) return true;
                }
            }
            return false;
        }

        static bool ProbeExactVerticalLine(IReadOnlyList<Edge> edges, double xLine, Func<Vector2d, bool> consider, ref long exactBudget, out bool foundAny)
        {
            foundAny = false;
            int activeCount = 0;
            for (int i = 0; i < edges.Count; ++i) {
                if (edges[i].Left.x < xLine && xLine < edges[i].Right.x) ++activeCount;
            }
            if (activeCount < 2) return false;
            if (activeCount > exactBudget) { exactBudget = 0; return true; }
            exactBudget -= activeCount;

            var ys = new List<ExactRat>(activeCount);
            for (int i = 0; i < edges.Count; ++i) {
                var e = edges[i];
                if (e.Left.x < xLine && xLine < e.Right.x)
                    ys.Add(ExactIntercept(e.Left.y, e.Left.x, e.Right.y, e.Right.x, xLine));
            }
            ys.Sort();
            int testedSpans = 0;
            for (int k = 0; k + 1 < ys.Count && testedSpans < 8; k += 2) {
                if (ys[k].CompareTo(ys[k + 1]) >= 0) continue;
                ++testedSpans;
                double yMid = ExactMidpointDouble(ys[k], ys[k + 1]);
                double yQ1 = ExactQuarterDouble(ys[k], ys[k + 1], firstQuarter: true);
                double yQ3 = ExactQuarterDouble(ys[k], ys[k + 1], firstQuarter: false);
                if (consider(new Vector2d(xLine, yMid))
                    || consider(new Vector2d(xLine, yQ1))
                    || consider(new Vector2d(xLine, yQ3))
                    || consider(new Vector2d(xLine, NextUp(yMid)))
                    || consider(new Vector2d(xLine, NextDown(yMid)))) {
                    foundAny = true;
                    return true;
                }
            }
            return true;
        }

        static void SweepAllHorizontal(Edge[] edges, double[] ys, List<int> alreadyChecked, List<double> crossBuf,
            List<(double width, double lo, double hi)> spanBuf, Func<Vector2d, bool> consider, Func<bool> isFound,
            ref long sweepBudget, ref long exactBudget)
        {
            var skip = new HashSet<int>(alreadyChecked);
            var byLo = new Edge[edges.Length];
            Array.Copy(edges, byLo, edges.Length);
            Array.Sort(byLo, (a, b) => {
                int c = a.Lo.y.CompareTo(b.Lo.y);
                if (c != 0) return c;
                c = a.Lo.x.CompareTo(b.Lo.x);
                if (c != 0) return c;
                c = a.Hi.y.CompareTo(b.Hi.y);
                return c != 0 ? c : a.Hi.x.CompareTo(b.Hi.x);
            });
            // Bucket edge expirations by Hi.y slab index so active-edge removal is O(1) amortized instead of O(|active|).
            var expireAt = new Dictionary<int, List<int>>();
            var active = new List<Edge>();
            var activePos = new int[byLo.Length];
            var activeIds = new List<int>();
            int nextAdd = 0;
            for (int j = 0; j + 1 < ys.Length && sweepBudget > 0; ++j) {
                double y0 = ys[j], y1 = ys[j + 1];
                if (expireAt.TryGetValue(j, out var expiring)) {
                    for (int k = 0; k < expiring.Count; ++k) {
                        int id = expiring[k];
                        int pos = activePos[id];
                        if (pos >= 0) {
                            int lastPos = active.Count - 1;
                            if (pos != lastPos) {
                                int movedId = activeIds[lastPos];
                                active[pos] = active[lastPos];
                                activeIds[pos] = movedId;
                                activePos[movedId] = pos;
                            }
                            active.RemoveAt(lastPos);
                            activeIds.RemoveAt(lastPos);
                            activePos[id] = -1;
                        }
                    }
                    expireAt.Remove(j);
                }
                while (nextAdd < byLo.Length && byLo[nextAdd].Lo.y <= y0) {
                    if (byLo[nextAdd].Hi.y > y0) {
                        int id = nextAdd;
                        int hiIdx = Array.BinarySearch(ys, byLo[id].Hi.y);
                        activePos[id] = active.Count;
                        active.Add(byLo[id]);
                        activeIds.Add(id);
                        if (hiIdx > j && hiIdx < ys.Length - 1) {
                            if (!expireAt.TryGetValue(hiIdx, out var list)) expireAt[hiIdx] = list = new List<int>();
                            list.Add(id);
                        }
                    } else {
                        activePos[nextAdd] = -1;
                    }
                    ++nextAdd;
                }
                if (skip.Contains(j) || active.Count < 2) continue;
                if (active.Count > sweepBudget) { sweepBudget = 0; return; }
                sweepBudget -= active.Count;
                ProbeDoubleHorizontal(active, y0, y1, crossBuf, spanBuf, consider, maxSpansToTest: 2, stopOnHit: true);
                if (isFound()) return;
                if (exactBudget > 0 && ProbeExactHorizontalSlab(active, y0, y1, consider, ref exactBudget)) return;
            }
        }

        static void SweepAllVertical(Edge[] edges, double[] xs, List<int> alreadyChecked, List<double> crossBuf,
            List<(double width, double lo, double hi)> spanBuf, Func<Vector2d, bool> consider, Func<bool> isFound,
            ref long sweepBudget, ref long exactBudget)
        {
            var skip = new HashSet<int>(alreadyChecked);
            var byLeft = new Edge[edges.Length];
            Array.Copy(edges, byLeft, edges.Length);
            Array.Sort(byLeft, (a, b) => {
                int c = a.Left.x.CompareTo(b.Left.x);
                if (c != 0) return c;
                c = a.Left.y.CompareTo(b.Left.y);
                if (c != 0) return c;
                c = a.Right.x.CompareTo(b.Right.x);
                return c != 0 ? c : a.Right.y.CompareTo(b.Right.y);
            });
            var expireAt = new Dictionary<int, List<int>>();
            var active = new List<Edge>();
            var activePos = new int[byLeft.Length];
            var activeIds = new List<int>();
            int nextAdd = 0;
            for (int j = 0; j + 1 < xs.Length && sweepBudget > 0; ++j) {
                double x0 = xs[j], x1 = xs[j + 1];
                if (expireAt.TryGetValue(j, out var expiring)) {
                    for (int k = 0; k < expiring.Count; ++k) {
                        int id = expiring[k];
                        int pos = activePos[id];
                        if (pos >= 0) {
                            int lastPos = active.Count - 1;
                            if (pos != lastPos) {
                                int movedId = activeIds[lastPos];
                                active[pos] = active[lastPos];
                                activeIds[pos] = movedId;
                                activePos[movedId] = pos;
                            }
                            active.RemoveAt(lastPos);
                            activeIds.RemoveAt(lastPos);
                            activePos[id] = -1;
                        }
                    }
                    expireAt.Remove(j);
                }
                while (nextAdd < byLeft.Length && byLeft[nextAdd].Left.x <= x0) {
                    if (byLeft[nextAdd].Right.x > x0) {
                        int id = nextAdd;
                        int hiIdx = Array.BinarySearch(xs, byLeft[id].Right.x);
                        activePos[id] = active.Count;
                        active.Add(byLeft[id]);
                        activeIds.Add(id);
                        if (hiIdx > j && hiIdx < xs.Length - 1) {
                            if (!expireAt.TryGetValue(hiIdx, out var list)) expireAt[hiIdx] = list = new List<int>();
                            list.Add(id);
                        }
                    } else {
                        activePos[nextAdd] = -1;
                    }
                    ++nextAdd;
                }
                if (skip.Contains(j) || active.Count < 2) continue;
                if (active.Count > sweepBudget) { sweepBudget = 0; return; }
                sweepBudget -= active.Count;
                ProbeDoubleVertical(active, x0, x1, crossBuf, spanBuf, consider, maxSpansToTest: 2, stopOnHit: true);
                if (isFound()) return;
                if (exactBudget > 0 && ProbeExactVerticalSlab(active, x0, x1, consider, ref exactBudget)) return;
            }
        }

        /// <summary>
        /// Exact rational intercept u(v) = au + (bu - au) * (v - av) / (bv - av), with av &lt; v &lt; bv.
        /// </summary>
        static ExactRat ExactIntercept(double au, double av, double bu, double bv, double v)
        {
            ExactPoint3.Decompose(au, out var mau, out int eau);
            ExactPoint3.Decompose(bu, out var mbu, out int ebu);
            int loU = mau.IsZero ? ebu : (mbu.IsZero ? eau : Math.Min(eau, ebu));
            BigInteger Au = mau.IsZero ? BigInteger.Zero : mau << (eau - loU);
            BigInteger Bu = mbu.IsZero ? BigInteger.Zero : mbu << (ebu - loU);

            ExactPoint3.Decompose(av, out var mav, out int eav);
            ExactPoint3.Decompose(bv, out var mbv, out int ebv);
            ExactPoint3.Decompose(v, out var mv, out int ev);
            int loV = int.MaxValue;
            if (!mav.IsZero) loV = Math.Min(loV, eav);
            if (!mbv.IsZero) loV = Math.Min(loV, ebv);
            if (!mv.IsZero) loV = Math.Min(loV, ev);
            if (loV == int.MaxValue) loV = 0;
            BigInteger Av = mav.IsZero ? BigInteger.Zero : mav << (eav - loV);
            BigInteger Bv = mbv.IsZero ? BigInteger.Zero : mbv << (ebv - loV);
            BigInteger V = mv.IsZero ? BigInteger.Zero : mv << (ev - loV);

            BigInteger den = Bv - Av; // strictly positive since av < v < bv
            BigInteger num = Au * den + (Bu - Au) * (V - Av);
            return new ExactRat(num, den, loU);
        }

        static double ExactMidpointDouble(ExactRat a, ExactRat b)
        {
            if (a.Num.IsZero) return ExactPoint3.ToDoubleRounded(b.Num, b.Den, b.Exp - 1);
            if (b.Num.IsZero) return ExactPoint3.ToDoubleRounded(a.Num, a.Den, a.Exp - 1);

            long magA = ExactPoint3.BitLength(a.Num) - ExactPoint3.BitLength(a.Den) + a.Exp;
            long magB = ExactPoint3.BitLength(b.Num) - ExactPoint3.BitLength(b.Den) + b.Exp;
            if (magA - magB > 64) return ExactPoint3.ToDoubleRounded(a.Num, a.Den, a.Exp - 1);
            if (magB - magA > 64) return ExactPoint3.ToDoubleRounded(b.Num, b.Den, b.Exp - 1);

            int lo = Math.Min(a.Exp, b.Exp);
            BigInteger na = a.Exp > lo ? a.Num << (a.Exp - lo) : a.Num;
            BigInteger nb = b.Exp > lo ? b.Num << (b.Exp - lo) : b.Num;
            BigInteger num = na * b.Den + nb * a.Den;
            BigInteger den = a.Den * b.Den;
            return ExactPoint3.ToDoubleRounded(num, den, lo - 1);
        }

        /// <summary> (3a + b)/4 when firstQuarter, else (a + 3b)/4, rounded to nearest double. </summary>
        static double ExactQuarterDouble(ExactRat a, ExactRat b, bool firstQuarter)
        {
            int wa = firstQuarter ? 3 : 1;
            int wb = firstQuarter ? 1 : 3;
            if (a.Num.IsZero) return ExactPoint3.ToDoubleRounded(wb * b.Num, b.Den, b.Exp - 2);
            if (b.Num.IsZero) return ExactPoint3.ToDoubleRounded(wa * a.Num, a.Den, a.Exp - 2);

            long magA = ExactPoint3.BitLength(a.Num) - ExactPoint3.BitLength(a.Den) + a.Exp;
            long magB = ExactPoint3.BitLength(b.Num) - ExactPoint3.BitLength(b.Den) + b.Exp;
            if (magA - magB > 64) return ExactPoint3.ToDoubleRounded(wa * a.Num, a.Den, a.Exp - 2);
            if (magB - magA > 64) return ExactPoint3.ToDoubleRounded(wb * b.Num, b.Den, b.Exp - 2);

            int lo = Math.Min(a.Exp, b.Exp);
            BigInteger na = a.Exp > lo ? a.Num << (a.Exp - lo) : a.Num;
            BigInteger nb = b.Exp > lo ? b.Num << (b.Exp - lo) : b.Num;
            BigInteger num = wa * na * b.Den + wb * nb * a.Den;
            BigInteger den = a.Den * b.Den;
            return ExactPoint3.ToDoubleRounded(num, den, lo - 2);
        }

        /// <summary> Exact strictly-inside check under even-odd parity (zero tolerance; never accepts boundary points). </summary>
        static bool IsStrictlyInside(IReadOnlyList<Edge> edges, Vector2d p)
        {
            if (double.IsNaN(p.x) || double.IsNaN(p.y) || double.IsInfinity(p.x) || double.IsInfinity(p.y))
                return false;
            bool inside = false;
            for (int i = 0; i < edges.Count; ++i) {
                var e = edges[i];
                if (p.x >= e.MinX && p.x <= e.MaxX && p.y >= e.MinY && p.y <= e.MaxY
                    && ExactPredicates.Orient2d(e.A, e.B, p) == 0)
                    return false;
                if (e.Lo.y <= p.y && p.y < e.Hi.y) {
                    if (ExactPredicates.Orient2d(e.Lo, e.Hi, p) > 0)
                        inside = !inside;
                }
            }
            return inside;
        }

        /// <summary> Public-facing test helper: exact strictly-inside check for an arbitrary ring. </summary>
        internal static bool IsStrictlyInside(IReadOnlyList<Vector2d> ring, Vector2d p)
        {
            if (ring == null || ring.Count < 3) return false;
            var verts = new List<Vector2d>(ring.Count);
            for (int i = 0; i < ring.Count; ++i) {
                var v = new Vector2d(ring[i].x == 0 ? 0.0 : ring[i].x, ring[i].y == 0 ? 0.0 : ring[i].y);
                if (verts.Count == 0 || verts[verts.Count - 1] != v) verts.Add(v);
            }
            while (verts.Count > 1 && verts[verts.Count - 1] == verts[0]) verts.RemoveAt(verts.Count - 1);
            if (verts.Count < 3) return false;
            var edges = new Edge[verts.Count];
            for (int i = 0; i < verts.Count; ++i) edges[i] = new Edge(verts[i], verts[(i + 1) % verts.Count]);
            return IsStrictlyInside(edges, p);
        }

        static double ScaledDist(Vector2d a, Vector2d b, int shift)
        {
            double dx = ExactPoint3.ScaleB(b.x - a.x, shift);
            double dy = ExactPoint3.ScaleB(b.y - a.y, shift);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static double MinDist2Scaled(Edge[] edges, Vector2d p, int shift)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < edges.Length; ++i) {
                var e = edges[i];
                double abx = ExactPoint3.ScaleB(e.B.x - e.A.x, shift);
                double aby = ExactPoint3.ScaleB(e.B.y - e.A.y, shift);
                double apx = ExactPoint3.ScaleB(p.x - e.A.x, shift);
                double apy = ExactPoint3.ScaleB(p.y - e.A.y, shift);
                double ab2 = abx * abx + aby * aby;
                double d2;
                if (ab2 <= 0) {
                    d2 = apx * apx + apy * apy;
                } else {
                    double t = (apx * abx + apy * aby) / ab2;
                    if (t <= 0) d2 = apx * apx + apy * apy;
                    else if (t >= 1) {
                        double bpx = ExactPoint3.ScaleB(p.x - e.B.x, shift);
                        double bpy = ExactPoint3.ScaleB(p.y - e.B.y, shift);
                        d2 = bpx * bpx + bpy * bpy;
                    } else {
                        double dx = apx - t * abx;
                        double dy = apy - t * aby;
                        d2 = dx * dx + dy * dy;
                    }
                }
                if (d2 < best) best = d2;
            }
            return best;
        }

        static double Midpoint(double a, double b)
        {
            // CoordinateLimit is 1e70, so a + b cannot overflow; (a + b) * 0.5 is bit-for-bit 2^k scale invariant.
            double m = (a + b) * 0.5;
            return m == 0 ? 0.0 : m;
        }

        internal static double NextUp(double x)
        {
            if (double.IsNaN(x) || double.IsPositiveInfinity(x)) return x;
            if (x == 0.0) return double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(x);
            bits += bits >= 0 ? 1 : -1;
            double r = BitConverter.Int64BitsToDouble(bits);
            return r == 0.0 ? 0.0 : r;
        }

        internal static double NextDown(double x)
        {
            if (double.IsNaN(x) || double.IsNegativeInfinity(x)) return x;
            if (x == 0.0) return -double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(x);
            bits += bits > 0 ? -1 : 1;
            double r = BitConverter.Int64BitsToDouble(bits);
            return r == 0.0 ? 0.0 : r;
        }
    }
}
