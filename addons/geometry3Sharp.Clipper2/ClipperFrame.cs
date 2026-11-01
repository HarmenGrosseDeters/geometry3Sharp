// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace g3.Clipper2
{
    /// <summary>
    /// The map between double coordinates and Clipper2's integer coordinates (Point64):
    /// <c>q = round((p - Origin) * Scale)</c> and back <c>p = Origin + q / Scale</c>.
    ///
    /// Clipper2 computes on integers, which makes its booleans robust; the price is that every input coordinate is
    /// snapped to the grid of spacing <see cref="Resolution"/> = 1 / Scale around <see cref="Origin"/>.
    /// Round-trip precision: <c>|p - ToVector2d(ToPoint64(p))| &lt;= Resolution / 2</c> per coordinate, plus one rounding
    /// of the subtraction and one of the addition of Origin (one unit in the last place of the coordinate each).
    ///
    /// <see cref="ForBounds"/> picks Origin = the center of the input bounds and Scale = the largest power of two that
    /// maps the input (plus a margin, eg the offset distance) into ±2^precisionBits. A power of two keeps the map exact
    /// apart from the rounding to the grid, and makes it scale invariant: inputs scaled by 2^k give the same integers,
    /// so the results are the same, scaled, bit for bit. Translated inputs give the same integers up to the rounding of
    /// the subtraction. With the default of 40 bits the grid spacing is the input extent times 2^-40 (about 1e-12):
    /// 5 nm for a 10 km footprint at UTM coordinates.
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
        /// False if the bounds are empty or not finite, or the scale is outside the double range (coordinates beyond
        /// about 1e300, or extents below about 1e-290).
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
            if (!IsFinite(extent))
                return false;
            if (extent == 0)
                extent = 1;   // a single point: nothing to clip, any grid will do
            int e = CeilExponent(extent);   // extent <= 2^e
            double scale = PowerOfTwo(precisionBits - e);
            if (!(scale > 0) || !IsFinite(scale) || !IsFinite(1.0 / scale))
                return false;
            frame = new ClipperFrame(origin, scale);
            return true;
        }

        /// <summary> the grid point of p. Throws if p is not finite or outside ±2^52 integer units. </summary>
        public Point64 ToPoint64(Vector2d p)
        {
            return new Point64(ToInteger(p.x - Origin.x), ToInteger(p.y - Origin.y));
        }

        /// <summary> a displacement (no Origin), eg a Minkowski pattern relative to its reference point </summary>
        public Point64 ToPoint64Displacement(Vector2d d)
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
        public Vector2d ToVector2d(Point64 q)
        {
            return new Vector2d(Origin.x + q.X / Scale, Origin.y + q.Y / Scale);
        }

        /// <summary> the vertices of a polygon, in order (orientation kept) </summary>
        public Path64 ToPath64(Polygon2d polygon)
        {
            var path = new Path64(polygon.VertexCount);
            foreach (Vector2d v in polygon.Vertices)
                path.Add(ToPoint64(v));
            return path;
        }

        /// <summary> the vertices of a polyline, in order </summary>
        public Path64 ToPath64(PolyLine2d polyline)
        {
            var path = new Path64(polyline.VertexCount);
            foreach (Vector2d v in polyline.Vertices)
                path.Add(ToPoint64(v));
            return path;
        }

        /// <summary>
        /// The outer boundary and the holes of a polygon as Clipper2 paths, oriented the way Clipper2 reports its
        /// results: the outer boundary counterclockwise (positive area, y up), the holes clockwise; rings given the
        /// other way round are reversed (by the sign of their area on the grid). With this orientation the fill rules
        /// NonZero and Positive give the region of the polygon. Empty if Outer is null.
        /// </summary>
        public Paths64 ToPaths64(GeneralPolygon2d polygon)
        {
            var paths = new Paths64();
            AddPaths(polygon, paths);
            return paths;
        }

        /// <summary> ToPaths64 of each polygon, appended </summary>
        public Paths64 ToPaths64(IEnumerable<GeneralPolygon2d> polygons)
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
            paths.Add(Oriented(ToPath64(polygon.Outer), true));
            foreach (Polygon2d hole in polygon.Holes)
                paths.Add(Oriented(ToPath64(hole), false));
        }

        static Path64 Oriented(Path64 path, bool positive)
        {
            double area = Clipper.Area(path);
            if ((positive && area < 0) || (!positive && area > 0))
                path.Reverse();
            return path;
        }

        /// <summary> a polygon with the vertices of a path </summary>
        public Polygon2d ToPolygon2d(Path64 path)
        {
            var vertices = new List<Vector2d>(path.Count);
            foreach (Point64 q in path)
                vertices.Add(ToVector2d(q));
            return new Polygon2d(vertices);
        }

        /// <summary> a polyline with the vertices of a path </summary>
        public PolyLine2d ToPolyLine2d(Path64 path)
        {
            var vertices = new List<Vector2d>(path.Count);
            foreach (Point64 q in path)
                vertices.Add(ToVector2d(q));
            return new PolyLine2d(vertices);
        }

        /// <summary>
        /// The polygons of a Clipper2 PolyTree: one GeneralPolygon2d per outer boundary, with its holes; islands inside
        /// a hole are polygons of their own (after their enclosing polygon in the list). Outer boundaries are
        /// counterclockwise and holes clockwise, as on the grid (the conversion to double does not change that except
        /// for rings of a few grid units of area far from the origin). Holes are not checked again for containment.
        /// </summary>
        public List<GeneralPolygon2d> ToGeneralPolygons(PolyTree64 tree)
        {
            var result = new List<GeneralPolygon2d>();
            for (int i = 0; i < tree.Count; ++i)
                AddOuter(tree.Child(i), result);
            return result;
        }

        void AddOuter(PolyPath64 node, List<GeneralPolygon2d> result)
        {
            var polygon = new GeneralPolygon2d(ToPolygon2d(node.Polygon));
            result.Add(polygon);
            for (int i = 0; i < node.Count; ++i) {
                PolyPath64 hole = node.Child(i);
                polygon.AddHole(ToPolygon2d(hole.Polygon), false, false);
                for (int j = 0; j < hole.Count; ++j)
                    AddOuter(hole.Child(j), result);
            }
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
        internal static double PowerOfTwo(int n)
        {
            if (n > 1023) return double.PositiveInfinity;
            if (n < -1022) return 0;
            return BitConverter.Int64BitsToDouble((long)(n + 1023) << 52);
        }
    }
}
