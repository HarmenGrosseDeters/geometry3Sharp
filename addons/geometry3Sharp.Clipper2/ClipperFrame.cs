// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3.Clipper2.Vendored;

namespace g3.Clipper2
{
    /// <summary>
    /// The map between double coordinates and Clipper2's integer coordinates (Point64):
    /// <c>q = round((p - Origin) * Scale)</c> and back <c>p = Origin + q / Scale</c>.
    ///
    /// Clipper2 computes on integers, which makes its booleans robust; the price is that every input coordinate is
    /// snapped to the grid of spacing <see cref="Resolution"/> = 1 / Scale around <see cref="Origin"/>.
    /// Round-trip precision: a point mapped to the grid and back is within Resolution / 2 per coordinate, plus one rounding
    /// of the subtraction and one of the addition of Origin (one unit in the last place of the coordinate each).
    ///
    /// <see cref="ForBounds"/> picks Origin = the center of the input bounds and Scale = the largest power of two that
    /// maps the input (plus a margin, eg the offset distance) into ±2^precisionBits. A power of two keeps the map exact
    /// apart from the rounding to the grid, and makes it scale invariant: inputs scaled by 2^k give the same integers,
    /// so the results are the same, scaled, bit for bit. Translated inputs give the same integers up to the rounding of
    /// the subtraction. With the default of 40 bits the grid spacing is the input extent times 2^-40 (about 1e-12):
    /// 5 nm for a 10 km footprint at UTM coordinates. The grid is never finer than the doubles of the coordinates: with
    /// half extent + margin at most 2^e and |Origin| (the larger coordinate of the bounds center) + half extent + margin
    /// below 2^E, Resolution = max(2^(e - bits), 2^(E - 53)). The second term acts only for coordinates more than about
    /// 2^(53 - bits) times the extent from zero (8192 at 40 bits, eg a millimetre design at UTM coordinates: 2^-30, about 1 nm); then the Origin is a grid point and every
    /// grid point is a double, so results map back to doubles exactly.
    /// </summary>
    public readonly struct ClipperFrame
    {
        /// <summary> default for <see cref="Clipper2Options.PrecisionBits"/> </summary>
        public const int DefaultPrecisionBits = 40;
        /// <summary> smallest accepted precision </summary>
        public const int MinPrecisionBits = 8;
        /// <summary>
        /// largest accepted precision: integer coordinates (including offsets) stay below 2^52, so they convert to
        /// double exactly; Clipper2 itself accepts up to 2^61
        /// </summary>
        public const int MaxPrecisionBits = 50;
        /// <summary> integer coordinates of a frame given explicitly must stay within ±2^52 (exact in double) </summary>
        public const double MaxIntegerCoordinate = 4503599627370496.0;   // 2^52

        /// <summary> the point that maps to the integer origin </summary>
        public readonly Vector2d Origin;
        /// <summary> integer units per input unit </summary>
        public readonly double Scale;

        /// <summary> spacing of the integer grid in input units (1 / Scale) </summary>
        public double Resolution => 1.0 / Scale;

        /// <summary>
        /// A frame with an explicit origin and scale, eg Scale = 1000 for a millimetre grid on metre coordinates. A
        /// power of two converts back without rounding (other than adding Origin). Throws on a non-finite origin or a
        /// scale that is not positive and finite.
        /// </summary>
        public ClipperFrame(Vector2d origin, double scale)
        {
            if (!IsFinite(origin.x) || !IsFinite(origin.y))
                throw new ArgumentException("ClipperFrame: origin is not finite");
            if (!(scale > 0) || !IsFinite(scale))
                throw new ArgumentException("ClipperFrame: scale must be positive and finite");
            Origin = origin;
            Scale = scale;
        }

        /// <summary>
        /// The frame for inputs within bounds, with room for a margin around them (offset distance, Minkowski pattern
        /// size): Origin = bounds center, Scale = the largest power of two with (half extent + margin) * Scale &lt;= 2^bits.
        /// False if the bounds are empty or not finite, if a coordinate plus the extent (and margin) exceeds the double
        /// range, or if the scale is outside the double range (extents below about 1e-290 near zero; far from zero the grid
        /// is clamped to the doubles there, see the summary of the struct).
        /// </summary>
        public static bool ForBounds(AxisAlignedBox2d bounds, double margin, int precisionBits, out ClipperFrame frame)
        {
            frame = default(ClipperFrame);
            if (precisionBits < MinPrecisionBits || precisionBits > MaxPrecisionBits)
                return false;
            if (bounds.Min.x > bounds.Max.x || bounds.Min.y > bounds.Max.y || !IsFinite(margin) || margin < 0)
                return false;
            if (!IsFinite(bounds.Min.x) || !IsFinite(bounds.Min.y) || !IsFinite(bounds.Max.x) || !IsFinite(bounds.Max.y))
                return false;
            // halves first: (min + max) / 2 overflows near the double range
            var origin = new Vector2d(0.5 * bounds.Min.x + 0.5 * bounds.Max.x, 0.5 * bounds.Min.y + 0.5 * bounds.Max.y);
            double extent = Math.Max(Math.Max(origin.x - bounds.Min.x, bounds.Max.x - origin.x),
                                     Math.Max(origin.y - bounds.Min.y, bounds.Max.y - origin.y)) + margin;
            // the grid must stay in the double range: origin ± extent finite (results at most extent from the origin)
            if (!IsFinite(extent) || !IsFinite(Math.Abs(origin.x) + extent) || !IsFinite(Math.Abs(origin.y) + extent))
                return false;
            if (extent == 0)
                extent = 1;   // a single point: nothing to clip, any grid will do
            int e = CeilExponent(extent);   // extent <= 2^e
            double scale = PowerOfTwo(precisionBits - e);
            // Far from the origin the grid must not be finer than the doubles there: with |coordinate| <= 2^E every
            // multiple of 2^(E-53) is a double, so a grid of that spacing around an origin on it maps back to doubles
            // exactly. A finer grid gives grid points that round to the same or to swapped doubles, and slivers of the
            // integer result come out as rings of zero or reversed orientation (eg a millimetre design at UTM
            // coordinates). The clamp only acts if the coordinates exceed the extent by about 2^(53 - precisionBits)
            // (8192 at the default 40 bits); then the origin moves onto the grid (at most half a grid unit).
            // reach = |Origin| + half extent + margin, rounded up: the rounded sum can lie below a power of two that the
            // true reach (eg of a Minkowski pattern) passes, where the doubles are twice as far apart
            int top = CeilExponent(NextUp(Math.Max(Math.Abs(origin.x), Math.Abs(origin.y)) + extent));
            double coarsest = PowerOfTwo(53 - top);
            if (coarsest < scale) {
                scale = coarsest;   // >= 2^-971: coordinates are below 2^1024
                // The clamp acts only for extent <= 2^(top - 4) (at most 50 bits), so the larger origin coordinate lies in
                // [2^(top-1), 2^top), where every double is a grid point already, or just below 2^(top-1), where the move
                // (at most half a grid unit) keeps it and the extent below 2^top: the coordinates stay where the grid points are doubles.
                origin = new Vector2d(Math.Round(origin.x * scale) / scale, Math.Round(origin.y * scale) / scale);
            }
            if (!(scale > 0) || !IsFinite(scale) || !IsFinite(1.0 / scale))
                return false;
            frame = new ClipperFrame(origin, scale);
            return true;
        }

        /// <summary> the grid point of p. Throws if p is not finite or outside ±2^52 integer units. </summary>
        internal Point64 ToPoint(Vector2d p)
        {
            return new Point64(ToInteger(p.x - Origin.x), ToInteger(p.y - Origin.y));
        }

        /// <summary> a displacement (no Origin), eg a Minkowski pattern relative to its reference point </summary>
        internal Point64 ToDisplacement(Vector2d d)
        {
            return new Point64(ToInteger(d.x), ToInteger(d.y));
        }

        long ToInteger(double v)
        {
            double q = Math.Round(v * Scale);
            if (!(Math.Abs(q) <= MaxIntegerCoordinate))   // also NaN
                throw new ArgumentOutOfRangeException(nameof(v), "ClipperFrame: coordinate not finite or outside the frame (±2^52 integer units)");
            return (long)q;
        }

        /// <summary> the input-space point of a grid point </summary>
        internal Vector2d ToVector2d(Point64 q)
        {
            return new Vector2d(Origin.x + q.X / Scale, Origin.y + q.Y / Scale);
        }

        /// <summary> the vertices of a polygon, in order (orientation kept) </summary>
        internal Path64 ToPath(Polygon2d polygon)
        {
            int n = polygon.VertexCount;
            var path = new Path64(n);
            if (Clipper2Levers.LeanBridge) {
                for (int i = 0; i < n; i++)
                    path.Add(ToPoint(polygon[i]));
                return path;
            }
            foreach (Vector2d v in polygon.Vertices)
                path.Add(ToPoint(v));
            return path;
        }

        /// <summary> the vertices of a polyline, in order </summary>
        internal Path64 ToPath(PolyLine2d polyline)
        {
            var path = new Path64(polyline.VertexCount);
            foreach (Vector2d v in polyline.Vertices)
                path.Add(ToPoint(v));
            return path;
        }

        /// <summary>
        /// The outer boundary and the holes of a polygon as Clipper2 paths, oriented the way Clipper2 reports its
        /// results: the outer boundary counterclockwise (positive area, y up), the holes clockwise; rings given the
        /// other way round are reversed (by the sign of their area on the grid). With this orientation the fill rules
        /// NonZero and Positive give the region of the polygon. Empty if Outer is null.
        /// </summary>
        internal Paths64 ToPaths(GeneralPolygon2d polygon)
        {
            var paths = new Paths64();
            AddPaths(polygon, paths);
            return paths;
        }

        /// <summary> ToPaths of each polygon, appended </summary>
        internal Paths64 ToPaths(IEnumerable<GeneralPolygon2d> polygons)
        {
            var paths = new Paths64();
            if (polygons != null)
                foreach (GeneralPolygon2d p in polygons)
                    AddPaths(p, paths);
            return paths;
        }

        void AddPaths(GeneralPolygon2d polygon, Paths64 paths)
        {
            if (polygon == null || polygon.Outer == null)
                return;
            paths.Add(Oriented(ToPath(polygon.Outer), true));
            foreach (Polygon2d hole in polygon.Holes)
                paths.Add(Oriented(ToPath(hole), false));
        }

        static Path64 Oriented(Path64 path, bool positive)
        {
            double area = Clipper.Area(path);
            if ((positive && area < 0) || (!positive && area > 0))
                path.Reverse();
            return path;
        }

        /// <summary> a polygon with the vertices of a path </summary>
        internal Polygon2d ToPolygon2d(Path64 path)
        {
            var vertices = new List<Vector2d>(path.Count);
            foreach (Point64 q in path)
                vertices.Add(ToVector2d(q));
            if (Clipper2Levers.LeanBridge) {
                var lean = new Polygon2d();
                lean.SetVertices(vertices, true);
                lean.Timestamp = 0;
                return lean;
            }
            return new Polygon2d(vertices);
        }

        /// <summary> a polyline with the vertices of a path </summary>
        internal PolyLine2d ToPolyLine2d(Path64 path)
        {
            var vertices = new List<Vector2d>(path.Count);
            foreach (Point64 q in path)
                vertices.Add(ToVector2d(q));
            return new PolyLine2d(vertices);
        }

        /// <summary>
        /// The polygons of a Clipper2 PolyTree: one GeneralPolygon2d per outer boundary, with its holes; islands inside a hole are
        /// polygons of their own. Outer boundaries are counterclockwise and holes clockwise: each ring is oriented by its grid area
        /// (the conversion to double rounds a grid point by at most half a grid unit, as ForBounds keeps the grid no finer than
        /// the doubles, and not at all where it clamps the grid; so only rings of a few grid units of area can change sign, near
        /// that threshold or in a frame set explicitly); a path
        /// of zero grid area whose vertices all lie on one line bounds nothing and is dropped (its children go to its parent; a
        /// zero-area path that is not collinear, eg a figure eight of two equal lobes, is kept as before). The
        /// tree decides outer or hole, except where it is certainly wrong, which a self-touching or self-overlapping result makes it:
        /// a path nested as a hole that has the area sign of its parent is a region of its own (subtracting it would drop its area);
        /// a path of negative area nested in a hole as an island but not inside it (see Contains) is another hole of that hole's
        /// outer boundary if that contains it (two holes that touch: filling it would add its area; inside the hole it is a filled
        /// island of winding -1), and otherwise is treated as a path at the top level; a path of negative area at the top level of the tree is a hole of the smallest positive
        /// path that contains it (all its vertices on or inside, one of its vertices or edge midpoints strictly inside), and a filled
        /// lobe of negative winding (reversed to an outer boundary) if none does. Such a path is then no polygon of its own, so the
        /// polygons are the tree's outer boundaries in pre-order, minus the reclassified ones, plus the reclassified ones nested as
        /// holes.
        /// </summary>
        internal List<GeneralPolygon2d> ToGeneralPolygons(PolyTree64 tree)
        {
            var entries = new List<(Path64 path, double area, bool treeHole, int parent)>();
            void Collect(PolyPath64 node, int parent)
            {
                for (int i = 0; i < node.Count; ++i) {
                    PolyPath64 child = node.Child(i);
                    double area = Clipper.Area(child.Polygon);
                    if (area == 0 && AllCollinear(child.Polygon)) { Collect(child, parent); continue; }   // bounds nothing: dropped
                    // hole or not by the depth among the kept paths (PolyPath64.IsHole counts a dropped path too)
                    entries.Add((child.Polygon, area, parent >= 0 && !entries[parent].treeHole, parent));
                    Collect(child, entries.Count - 1);
                }
            }
            Collect(tree, -1);
            int n = entries.Count;
            var holeOf = new int[n];            // -1: an outer boundary
            List<int> positives = null;         // by area, for the negative paths at the top level of the tree
            // the smallest positive path that holds path i (by Contains), -1 if none
            int SmallestHolder(int i)
            {
                if (positives == null)
                    positives = Enumerable.Range(0, n).Where(j => entries[j].area > 0).OrderBy(j => entries[j].area).ToList();
                var e = entries[i];
                Rect64 box = Clipper.GetBounds(e.path);
                foreach (int j in positives) {
                    if (entries[j].area <= -e.area) continue;       // a hole is smaller than what holds it
                    Rect64 outer = Clipper.GetBounds(entries[j].path);
                    // the bounds with Contains' tolerance of one grid unit
                    if (box.left < outer.left - 1 || box.right > outer.right + 1 || box.top < outer.top - 1 || box.bottom > outer.bottom + 1) continue;
                    if (Contains(entries[j].path, e.path)) return j;
                }
                return -1;
            }
            for (int i = 0; i < n; ++i) {
                holeOf[i] = -1;
                var e = entries[i];
                if (e.treeHole) {   // (a top-level path is never a tree hole: the flag follows the kept depth)
                    var parent = entries[e.parent];
                    bool sameSign = parent.area != 0 && e.area != 0 && (parent.area > 0) == (e.area > 0);
                    if (!sameSign && holeOf[e.parent] < 0) holeOf[i] = e.parent;
                } else if (e.parent >= 0 && e.area < 0 && entries[e.parent].area < 0 && holeOf[e.parent] >= 0
                           && !Contains(entries[e.parent].path, e.path)) {
                    // beside its hole: a hole of the hole's outer boundary if that holds it
                    int outer = holeOf[e.parent];
                    holeOf[i] = Contains(entries[outer].path, e.path) ? outer : SmallestHolder(i);
                } else if (e.parent < 0 && e.area < 0) {
                    // (also a tree hole whose parent, a path of zero area, was dropped)
                    holeOf[i] = SmallestHolder(i);
                }
            }
            // a hole whose holder is itself a hole (a candidate that was reclassified later) is an outer boundary
            for (int i = 0; i < n; ++i)
                if (holeOf[i] >= 0 && holeOf[holeOf[i]] >= 0) holeOf[i] = -1;
            var result = new List<GeneralPolygon2d>();
            var polygonOf = new Dictionary<int, GeneralPolygon2d>();
            for (int i = 0; i < n; ++i) {
                if (holeOf[i] >= 0) continue;
                var polygon = new GeneralPolygon2d(ToPolygon2d(Oriented(new Path64(entries[i].path), true)));
                polygonOf[i] = polygon;
                result.Add(polygon);
            }
            for (int i = 0; i < n; ++i)
                if (holeOf[i] >= 0)
                    polygonOf[holeOf[i]].AddHole(ToPolygon2d(Oriented(new Path64(entries[i].path), false)), false, false);
            return result;
        }

        /// <summary>
        /// The path lies in the holder: every vertex is on it or inside (the even-odd test, or a non-zero winding for a self-overlapping
        /// holder, or within one grid unit of the holder's boundary: a vertex of a hole that touches the holder's edge can be rounded to
        /// the grid point beside it), and some vertex or edge midpoint is strictly inside, so that a path spanning a notch with its
        /// corners on the holder's boundary is not taken for a hole. The holder's coordinates are doubled (they are below 2^52) so that an edge
        /// midpoint is a grid point and no integer division moves it; the winding number uses double cross products, exact up to 2^53, so
        /// it can misjudge a point one grid unit outside an edge only for a frame set explicitly near the limit of 2^52. The cost is the product of the vertex counts, with the bounding boxes as the only prefilter; it is
        /// paid only for a negative path at the top level of the tree or nested as an island in a hole, which Clipper2 reports for
        /// degenerate input only
        /// </summary>
        static bool Contains(Path64 holder, Path64 path)
        {
            var doubled = new Path64(holder.Count);
            foreach (Point64 h in holder) doubled.Add(new Point64(2 * h.X, 2 * h.Y));
            bool Inside(Point64 q) => Clipper.PointInPolygon(q, doubled) != PointInPolygonResult.IsOutside || Winding(doubled, q) != 0
                                      || NearBoundary(doubled, q, 2);   // one grid unit, doubled
            bool Strict(Point64 q)
            {
                PointInPolygonResult where = Clipper.PointInPolygon(q, doubled);
                return where == PointInPolygonResult.IsInside || (where == PointInPolygonResult.IsOutside && Winding(doubled, q) != 0);
            }
            bool strict = false;
            for (int k = 0; k < path.Count; ++k) {
                Point64 q = path[k], next = path[(k + 1) % path.Count];
                Point64 q2 = new Point64(2 * q.X, 2 * q.Y);
                if (!Inside(q2)) return false;
                strict |= Strict(q2) || Strict(new Point64(q.X + next.X, q.Y + next.Y));
            }
            return strict;
        }

        /// <summary> whether every vertex lies on one line (exact: Clipper2's 128-bit product comparison) </summary>
        static bool AllCollinear(Path64 path)
        {
            int k = 1;
            while (k < path.Count && path[k] == path[0]) ++k;
            for (int i = k + 1; i < path.Count; ++i)
                if (!InternalClipper.IsCollinear(path[0], path[k], path[i])) return false;
            return true;
        }

        /// <summary>
        /// whether a point is within the given distance of an edge of a closed path, in doubles: exact enough at the default
        /// frame (coordinates about 2^40; error about 2^-11 unit), but for an explicit frame near the limit of 2^52 the
        /// cancellation in the projection costs several units, so the one-unit tolerance there is approximate
        /// </summary>
        static bool NearBoundary(Path64 path, Point64 p, double distance)
        {
            for (int i = 0; i < path.Count; ++i) {
                Point64 a = path[i], b = path[(i + 1) % path.Count];
                double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y, px = (double)p.X - a.X, py = (double)p.Y - a.Y;
                double length2 = dx * dx + dy * dy;
                double t = length2 == 0 ? 0 : Math.Max(0, Math.Min(1, (px * dx + py * dy) / length2));
                double ex = px - t * dx, ey = py - t * dy;
                if (ex * ex + ey * ey <= distance * distance) return true;
            }
            return false;
        }

        /// <summary> the winding number of a path around a point (the crossing rule; double cross products, as Clipper2's own) </summary>
        static int Winding(Path64 path, Point64 p)
        {
            int winding = 0;
            for (int i = 0; i < path.Count; ++i) {
                Point64 a = path[i], b = path[(i + 1) % path.Count];
                double cross = (double)(b.X - a.X) * (p.Y - a.Y) - (double)(p.X - a.X) * (b.Y - a.Y);
                if (a.Y <= p.Y) { if (b.Y > p.Y && cross > 0) ++winding; }
                else if (b.Y <= p.Y && cross < 0) --winding;
            }
            return winding;
        }

        public override string ToString()
        {
            return string.Format("origin {0}, scale {1} (resolution {2})", Origin, Scale, Resolution);
        }

        internal static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary> the smallest e with x &lt;= 2^e, for finite x &gt; 0 </summary>
        internal static int CeilExponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int biased = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (biased == 0) {
                // subnormal: x = mantissa * 2^-1074
                int e = -1074;
                while (mantissa > 1) { mantissa = (mantissa + 1) >> 1; ++e; }
                return e;
            }
            int exponent = biased - 1023;            // x = 1.f * 2^exponent
            return mantissa == 0 ? exponent : exponent + 1;
        }

        /// <summary> 2^n for -1022 &lt;= n &lt;= 1023, otherwise 0 or infinity </summary>
        /// <summary> the next double above a positive finite x (Math.BitIncrement is not in netstandard2.0) </summary>
        internal static double NextUp(double x)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);
        }

        internal static double PowerOfTwo(int n)
        {
            if (n > 1023) return double.PositiveInfinity;
            if (n < -1022) return 0;
            return BitConverter.Int64BitsToDouble((long)(n + 1023) << 52);
        }
    }
}
