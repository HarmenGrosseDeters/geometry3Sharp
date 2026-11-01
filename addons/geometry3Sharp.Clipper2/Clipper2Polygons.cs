// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace g3.Clipper2
{
    /// <summary> outcome of a Clipper2Polygons operation </summary>
    public enum Clipper2Status
    {
        /// <summary> computed; an empty result (eg the intersection of disjoint polygons) is also a success </summary>
        Success,
        /// <summary> an input coordinate is NaN or infinite, or the coordinates are too large or the extent too small for a grid (see ClipperFrame.ForBounds) </summary>
        InvalidInput,
        /// <summary> an option or argument is out of its range (see Error) </summary>
        InvalidParameter,
        /// <summary> Clipper2 reported a failure </summary>
        Failed
    }

    /// <summary>
    /// Options of the Clipper2Polygons operations. Each field says which operations read it; the defaults suit
    /// polygons in any units at any position.
    /// </summary>
    public class Clipper2Options
    {
        /// <summary>
        /// booleans and Simplify: which regions count as inside, by the winding number of the input rings.
        /// GeneralPolygon2d inputs are oriented (outer counterclockwise, holes clockwise), so NonZero (default) and
        /// Positive give the union of overlapping polygons, EvenOdd lets overlaps cancel. For raw paths
        /// (BooleanPaths) the orientation of the given vertices counts.
        /// </summary>
        public FillRule FillRule = FillRule.NonZero;

        /// <summary>
        /// all operations: the inputs (with the margin of an offset or Minkowski pattern) are mapped to integers in
        /// ±2^PrecisionBits (ClipperFrame.ForBounds); the result vertices lie on that grid. Range 8 to 50.
        /// </summary>
        public int PrecisionBits = ClipperFrame.DefaultPrecisionBits;

        /// <summary>
        /// all operations: use this grid instead of one fitted to the input, eg a millimetre grid shared by several
        /// calls. The inputs (with the margin) must map within ±2^52 integer units, otherwise InvalidParameter.
        /// </summary>
        public ClipperFrame? Frame = null;

        /// <summary> booleans: keep vertices in the middle of straight edges (Clipper2 removes them by default) </summary>
        public bool PreserveCollinear = false;

        /// <summary> offsets: corner treatment (Round default, Miter, Square, Bevel) </summary>
        public JoinType JoinType = JoinType.Round;

        /// <summary>
        /// offsets of polylines: end treatment (Round default, Butt, Square, or Joined to offset a closed polyline on
        /// both sides). EndType.Polygon is not allowed here (use Offset for polygons).
        /// </summary>
        public EndType EndType = EndType.Round;

        /// <summary>
        /// offsets with JoinType.Miter: the largest distance of a miter tip from the input vertex, in multiples of the
        /// offset distance; sharper corners are squared off. At least 1, default 2 (as in Clipper2).
        /// </summary>
        public double MiterLimit = 2.0;

        /// <summary>
        /// offsets with round joins or ends: the largest distance between a true arc and its polygon, in input units.
        /// 0 (default): |delta| / 1000, about 70 segments per full circle, independent of the units.
        /// </summary>
        public double ArcTolerance = 0;
    }

    /// <summary> result of a Clipper2Polygons operation </summary>
    public class Clipper2Result
    {
        public Clipper2Status Status;
        /// <summary> why the status is not Success; null on success </summary>
        public string Error;
        /// <summary>
        /// closed results: one GeneralPolygon2d per outer boundary (counterclockwise) with its holes (clockwise);
        /// islands in holes are separate entries. The polygons do not overlap.
        /// </summary>
        public List<GeneralPolygon2d> Polygons = new List<GeneralPolygon2d>();
        /// <summary> open results: the pieces of clipped polylines (ClipPolyLines), simplified polylines (SimplifyPolyLines) </summary>
        public List<PolyLine2d> PolyLines = new List<PolyLine2d>();
        /// <summary> the integer grid the operation ran on; result vertices are on it </summary>
        public ClipperFrame Frame;

        public bool Ok => Status == Clipper2Status.Success;

        /// <summary> total area of Polygons (outer areas minus hole areas) </summary>
        public double Area => Polygons.Sum(p => p.Area);

        public override string ToString()
        {
            return Ok ? string.Format("Success: {0} polygons ({1} holes), {2} polylines, area {3}",
                                      Polygons.Count, Polygons.Sum(p => p.Holes.Count), PolyLines.Count, Area)
                      : Status + ": " + Error;
        }
    }

    /// <summary>
    /// Robust 2D polygon booleans, offsets, Minkowski sums and simplification for Polygon2d, GeneralPolygon2d and
    /// PolyLine2d, with Clipper2 (Angus Johnson, Boost-1.0; https://github.com/AngusJohnson/Clipper2).
    ///
    /// Clipper2 computes on integer coordinates; each call maps its inputs to a grid fitted to their bounds
    /// (ClipperFrame.ForBounds; Clipper2Options.PrecisionBits, default 2^40 grid units across the half extent) and
    /// the result back. So the results are robust for touching, overlapping and collinear input, scale invariant for
    /// power-of-two scales (bit for bit), independent of the position (UTM coordinates) up to the rounding of the
    /// translation. Input vertices are snapped to the grid and new vertices (intersections, offset arcs) are rounded to
    /// it, so a result vertex is off by about a grid spacing from its exact position. Results are
    /// deterministic (single-threaded). Nothing throws on bad input: see Clipper2Result.Status.
    ///
    /// Limits: input rings of a GeneralPolygon2d are reoriented (outer counterclockwise, holes clockwise) by their
    /// signed area; a self-intersecting ring counts with that net orientation and the fill rule. Features smaller
    /// than the grid spacing vanish or are snapped (eg slivers of a few grid units of width).
    /// </summary>
    public static class Clipper2Polygons
    {
        /// <summary> union of all polygons of subject and clip (clip may be null) </summary>
        public static Clipper2Result Union(IEnumerable<GeneralPolygon2d> subject, IEnumerable<GeneralPolygon2d> clip = null, Clipper2Options options = null)
            => Boolean(ClipType.Union, subject, clip, options);

        /// <summary> the region inside both subject and clip </summary>
        public static Clipper2Result Intersection(IEnumerable<GeneralPolygon2d> subject, IEnumerable<GeneralPolygon2d> clip, Clipper2Options options = null)
            => Boolean(ClipType.Intersection, subject, clip, options);

        /// <summary> subject minus clip </summary>
        public static Clipper2Result Difference(IEnumerable<GeneralPolygon2d> subject, IEnumerable<GeneralPolygon2d> clip, Clipper2Options options = null)
            => Boolean(ClipType.Difference, subject, clip, options);

        /// <summary> the region inside exactly one of subject and clip </summary>
        public static Clipper2Result Xor(IEnumerable<GeneralPolygon2d> subject, IEnumerable<GeneralPolygon2d> clip, Clipper2Options options = null)
            => Boolean(ClipType.Xor, subject, clip, options);

        /// <summary>
        /// Boolean operation of two polygon sets. Each set is first filled with options.FillRule (its rings oriented,
        /// see Clipper2Options.FillRule), then combined by op.
        /// </summary>
        public static Clipper2Result Boolean(ClipType op, IEnumerable<GeneralPolygon2d> subject, IEnumerable<GeneralPolygon2d> clip, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<GeneralPolygon2d> s = List(subject), c = List(clip);
            var result = new Clipper2Result();
            if (!CheckBoolean(op, options, result) || !Prepare(Points(s).Concat(Points(c)), 0, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => Execute(op, options, frame.ToPaths64(s), frame.ToPaths64(c), null, result));
        }

        /// <summary>
        /// Boolean operation of raw rings, oriented as given: with FillRule NonZero, Positive or Negative a clockwise
        /// ring counts with winding number -1. Use this for self-intersecting rings or to control the fill exactly.
        /// </summary>
        public static Clipper2Result BooleanPaths(ClipType op, IEnumerable<Polygon2d> subject, IEnumerable<Polygon2d> clip, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<Polygon2d> s = List(subject), c = List(clip);
            var result = new Clipper2Result();
            if (!CheckBoolean(op, options, result) || !Prepare(s.Concat(c).SelectMany(p => p.Vertices), 0, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => Execute(op, options, Paths(s.Select(frame.ToPath64)), Paths(c.Select(frame.ToPath64)), null, result));
        }

        /// <summary>
        /// Clips open polylines by polygons: op Intersection keeps the pieces inside clip, Difference the pieces
        /// outside (Union and Xor keep the pieces outside, as in Clipper2). Pieces go to Result.PolyLines; Polygons
        /// stays empty. Polylines with fewer than 2 vertices are ignored.
        /// </summary>
        public static Clipper2Result ClipPolyLines(ClipType op, IEnumerable<PolyLine2d> lines, IEnumerable<GeneralPolygon2d> clip, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<PolyLine2d> l = List(lines).Where(p => p.VertexCount >= 2).ToList();
            List<GeneralPolygon2d> c = List(clip);
            var result = new Clipper2Result();
            if (!CheckBoolean(op, options, result) || !Prepare(l.SelectMany(p => p.Vertices).Concat(Points(c)), 0, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => Execute(op, options, new Paths64(), frame.ToPaths64(c), Paths(l.Select(frame.ToPath64)), result));
        }

        /// <summary>
        /// Offsets polygons by delta: delta &gt; 0 inflates (outer boundaries grow, holes shrink), delta &lt; 0 deflates.
        /// Overlapping inputs are merged. Corners follow options.JoinType (MiterLimit, ArcTolerance).
        /// </summary>
        public static Clipper2Result Offset(IEnumerable<GeneralPolygon2d> polygons, double delta, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<GeneralPolygon2d> p = List(polygons);
            var result = new Clipper2Result();
            if (!CheckOffset(delta, options, result))
                return result;
            double margin = Math.Max(0, delta) * Math.Max(options.MiterLimit, 2);
            if (!Prepare(Points(p), margin, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => ExecuteOffset(frame.ToPaths64(p), EndType.Polygon, delta, options, result));
        }

        /// <summary>
        /// Offsets open polylines by delta &gt;= 0 to both sides: a band of width 2 delta around each line, with ends
        /// per options.EndType (EndType.Joined: the polyline is taken as closed). Polylines with fewer than 2 vertices
        /// are ignored, except a single point with round or square ends (a disc or square).
        /// </summary>
        public static Clipper2Result OffsetPolyLines(IEnumerable<PolyLine2d> lines, double delta, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<PolyLine2d> l = List(lines).Where(p => p.VertexCount >= 1).ToList();
            var result = new Clipper2Result();
            if (!CheckOffset(delta, options, result))
                return result;
            if (delta < 0)
                return Fail(result, Clipper2Status.InvalidParameter, "delta of a polyline offset must not be negative");
            if (options.EndType == EndType.Polygon)
                return Fail(result, Clipper2Status.InvalidParameter, "EndType.Polygon: use Offset for polygons");
            double margin = delta * Math.Max(options.MiterLimit, 2);
            if (!Prepare(l.SelectMany(p => p.Vertices), margin, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => ExecuteOffset(Paths(l.Select(frame.ToPath64)), options.EndType, delta, options, result));
        }

        /// <summary>
        /// Minkowski sum of the pattern (a closed polygon, its interior included; coordinates relative to the
        /// reference point, eg a circle around (0,0)) and the path: the union of the pattern placed at every point of
        /// the path. pathIsClosed: path is a closed ring and its interior is included too; otherwise an open
        /// polyline. The result has holes where the sweep encloses one.
        /// </summary>
        public static Clipper2Result MinkowskiSum(Polygon2d pattern, Polygon2d path, bool pathIsClosed, Clipper2Options options = null)
            => Minkowski(pattern, path, pathIsClosed, false, options);

        /// <summary> Minkowski difference: MinkowskiSum with the pattern mirrored at its reference point (path ⊕ -pattern) </summary>
        public static Clipper2Result MinkowskiDifference(Polygon2d pattern, Polygon2d path, bool pathIsClosed, Clipper2Options options = null)
            => Minkowski(pattern, path, pathIsClosed, true, options);

        static Clipper2Result Minkowski(Polygon2d pattern, Polygon2d path, bool closed, bool difference, Clipper2Options options)
        {
            options = options ?? new Clipper2Options();
            var result = new Clipper2Result();
            if (pattern == null || path == null)
                return Fail(result, Clipper2Status.InvalidParameter, "pattern and path are required");
            if (!CheckCommon(options, result))
                return result;
            double margin = 0;
            foreach (Vector2d v in pattern.Vertices) {
                if (!ClipperFrame.IsFinite(v.x) || !ClipperFrame.IsFinite(v.y))
                    return Fail(result, Clipper2Status.InvalidInput, "pattern coordinate is not finite");
                margin = Math.Max(margin, Math.Max(Math.Abs(v.x), Math.Abs(v.y)));
            }
            if (!Prepare(path.Vertices, margin, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => {
                var pat = new Path64(pattern.VertexCount);
                foreach (Vector2d v in pattern.Vertices)
                    pat.Add(frame.ToPoint64Displacement(difference ? -v : v));
                Path64 p = frame.ToPath64(path);
                if (pat.Count == 0 || p.Count == 0)
                    return;
                // Clipper2 sweeps the pattern's boundary along the path's edges (boundary ⊕ boundary). The interiors
                // are added as one copy of the pattern at a path vertex and, for a closed path, one copy of the path at
                // a pattern vertex: for connected A and B, A ⊕ B = (∂A ⊕ ∂B) ∪ (a + B) ∪ (A + b) for any a in A, b in B
                // (Minkowski.Sum returns a union: outer rings positive, holes negative; the copies are made positive)
                Paths64 all = Clipper2Lib.Minkowski.Sum(pat, p, closed);
                all.Add(Positive(Translated(pat, p[0])));
                if (closed)
                    all.Add(Positive(Translated(p, pat[0])));
                var clipper = new Clipper64();
                clipper.AddSubject(all);
                var tree = new PolyTree64();
                if (!clipper.Execute(ClipType.Union, FillRule.NonZero, tree))
                    throw new ClipperLibException("Minkowski union failed");
                result.Polygons = frame.ToGeneralPolygons(tree);
            });
        }

        static Path64 Translated(Path64 path, Point64 d)
        {
            var result = new Path64(path.Count);
            foreach (Point64 q in path)
                result.Add(new Point64(q.X + d.X, q.Y + d.Y));
            return result;
        }

        static Path64 Positive(Path64 path)
        {
            if (Clipper.Area(path) < 0)
                path.Reverse();
            return path;
        }

        /// <summary>
        /// Simplifies polygons: removes vertices closer than epsilon to the line through their neighbours (Clipper2
        /// SimplifyPaths), then repairs any overlaps this creates with a union under options.FillRule.
        /// </summary>
        public static Clipper2Result Simplify(IEnumerable<GeneralPolygon2d> polygons, double epsilon, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<GeneralPolygon2d> p = List(polygons);
            var result = new Clipper2Result();
            if (!CheckSimplify(epsilon, options, result) || !Prepare(Points(p), 0, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => {
                Paths64 simplified = Clipper.SimplifyPaths(frame.ToPaths64(p), epsilon * frame.Scale, true);
                Execute(ClipType.Union, options, simplified, new Paths64(), null, result);
            });
        }

        /// <summary> Simplifies open polylines (Clipper2 SimplifyPaths, ends kept); result in PolyLines </summary>
        public static Clipper2Result SimplifyPolyLines(IEnumerable<PolyLine2d> lines, double epsilon, Clipper2Options options = null)
        {
            options = options ?? new Clipper2Options();
            List<PolyLine2d> l = List(lines).Where(p => p.VertexCount >= 2).ToList();
            var result = new Clipper2Result();
            if (!CheckSimplify(epsilon, options, result) || !Prepare(l.SelectMany(p => p.Vertices), 0, options, result))
                return result;
            ClipperFrame frame = result.Frame;
            return Run(result, () => {
                Paths64 simplified = Clipper.SimplifyPaths(Paths(l.Select(frame.ToPath64)), epsilon * frame.Scale, false);
                foreach (Path64 path in simplified)
                    result.PolyLines.Add(frame.ToPolyLine2d(path));
            });
        }


        static void Execute(ClipType op, Clipper2Options options, Paths64 subject, Paths64 clip, Paths64 openSubject, Clipper2Result result)
        {
            var clipper = new Clipper64 { PreserveCollinear = options.PreserveCollinear };
            clipper.AddSubject(subject);
            if (openSubject != null)
                clipper.AddOpenSubject(openSubject);
            clipper.AddClip(clip);
            var tree = new PolyTree64();
            var open = new Paths64();
            if (!clipper.Execute(op, options.FillRule, tree, open))
                throw new ClipperLibException("Clipper64.Execute failed");
            result.Polygons = result.Frame.ToGeneralPolygons(tree);
            foreach (Path64 path in open)
                result.PolyLines.Add(result.Frame.ToPolyLine2d(path));
        }

        static void ExecuteOffset(Paths64 paths, EndType endType, double delta, Clipper2Options options, Clipper2Result result)
        {
            double scale = result.Frame.Scale;
            double arcTolerance = options.ArcTolerance > 0 ? options.ArcTolerance : Math.Abs(delta) / 1000;
            var tree = new PolyTree64();
            if (Math.Abs(delta * scale) < 0.5) {
                // below half a grid unit Clipper2 offsets nothing (its PolyTree output is then empty): the polygons
                // themselves, merged; open paths give a band of zero width, ie nothing
                if (endType == EndType.Polygon) {
                    var clipper = new Clipper64();
                    clipper.AddSubject(paths);
                    if (!clipper.Execute(ClipType.Union, FillRule.NonZero, tree))
                        throw new ClipperLibException("Clipper64.Execute failed");
                }
            } else {
                var offset = new ClipperOffset(options.MiterLimit, arcTolerance * scale);
                offset.AddPaths(paths, options.JoinType, endType);
                offset.Execute(delta * scale, tree);
            }
            result.Polygons = result.Frame.ToGeneralPolygons(tree);
        }

        static Clipper2Result Run(Clipper2Result result, Action compute)
        {
            try {
                compute();
                result.Status = Clipper2Status.Success;
            } catch (ClipperLibException e) {
                result.Polygons.Clear();
                result.PolyLines.Clear();
                Fail(result, Clipper2Status.Failed, e.Message);
            }
            return result;
        }

        /// <summary> finite input, then the grid (given or fitted to the points plus margin) </summary>
        static bool Prepare(IEnumerable<Vector2d> points, double margin, Clipper2Options options, Clipper2Result result)
        {
            var bounds = AxisAlignedBox2d.Empty;
            foreach (Vector2d v in points) {
                if (!ClipperFrame.IsFinite(v.x) || !ClipperFrame.IsFinite(v.y)) {
                    Fail(result, Clipper2Status.InvalidInput, "input coordinate is not finite");
                    return false;
                }
                bounds.Contain(v);
            }
            if (bounds.Min.x > bounds.Max.x)
                bounds = new AxisAlignedBox2d(Vector2d.Zero, 0);   // no input: an empty result on any grid
            if (options.Frame.HasValue) {
                ClipperFrame f = options.Frame.Value;
                double reach = Math.Max(Math.Max(Math.Abs(bounds.Min.x - f.Origin.x), Math.Abs(bounds.Max.x - f.Origin.x)),
                                        Math.Max(Math.Abs(bounds.Min.y - f.Origin.y), Math.Abs(bounds.Max.y - f.Origin.y))) + margin;
                if (!((reach * f.Scale) <= ClipperFrame.MaxIntegerCoordinate)) {
                    Fail(result, Clipper2Status.InvalidParameter, "input (with margin) outside the given Frame: more than 2^52 grid units from its origin");
                    return false;
                }
                result.Frame = f;
                return true;
            }
            if (!ClipperFrame.ForBounds(bounds, margin, options.PrecisionBits, out result.Frame)) {
                Fail(result, Clipper2Status.InvalidInput, "coordinates out of range for an integer grid (extent " + bounds.MaxDim + ", margin " + margin + ")");
                return false;
            }
            return true;
        }

        static bool CheckCommon(Clipper2Options options, Clipper2Result result)
        {
            if (!options.Frame.HasValue && (options.PrecisionBits < ClipperFrame.MinPrecisionBits || options.PrecisionBits > ClipperFrame.MaxPrecisionBits))
                return Fail(result, Clipper2Status.InvalidParameter, "PrecisionBits must be in [" + ClipperFrame.MinPrecisionBits + ", " + ClipperFrame.MaxPrecisionBits + "]").Ok;
            if (!Enum.IsDefined(typeof(FillRule), options.FillRule))
                return Fail(result, Clipper2Status.InvalidParameter, "unknown FillRule").Ok;
            return true;
        }

        static bool CheckBoolean(ClipType op, Clipper2Options options, Clipper2Result result)
        {
            if (op == ClipType.NoClip || !Enum.IsDefined(typeof(ClipType), op))
                return Fail(result, Clipper2Status.InvalidParameter, "op must be Union, Intersection, Difference or Xor").Ok;
            return CheckCommon(options, result);
        }

        static bool CheckOffset(double delta, Clipper2Options options, Clipper2Result result)
        {
            if (!ClipperFrame.IsFinite(delta))
                return Fail(result, Clipper2Status.InvalidParameter, "delta is not finite").Ok;
            if (!(options.MiterLimit >= 1) || !ClipperFrame.IsFinite(options.MiterLimit))
                return Fail(result, Clipper2Status.InvalidParameter, "MiterLimit must be finite and at least 1").Ok;
            if (!(options.ArcTolerance >= 0) || !ClipperFrame.IsFinite(options.ArcTolerance))
                return Fail(result, Clipper2Status.InvalidParameter, "ArcTolerance must be finite and not negative").Ok;
            if (!Enum.IsDefined(typeof(JoinType), options.JoinType) || !Enum.IsDefined(typeof(EndType), options.EndType))
                return Fail(result, Clipper2Status.InvalidParameter, "unknown JoinType or EndType").Ok;
            return CheckCommon(options, result);
        }

        static bool CheckSimplify(double epsilon, Clipper2Options options, Clipper2Result result)
        {
            if (!(epsilon >= 0) || !ClipperFrame.IsFinite(epsilon))
                return Fail(result, Clipper2Status.InvalidParameter, "epsilon must be finite and not negative").Ok;
            return CheckCommon(options, result);
        }

        static Clipper2Result Fail(Clipper2Result result, Clipper2Status status, string error)
        {
            result.Status = status;
            result.Error = error;
            return result;
        }

        static List<T> List<T>(IEnumerable<T> items) where T : class
            => items == null ? new List<T>() : items.Where(x => x != null).ToList();

        static IEnumerable<Vector2d> Points(IEnumerable<GeneralPolygon2d> polygons)
        {
            foreach (GeneralPolygon2d p in polygons) {
                if (p.Outer == null) continue;
                foreach (Vector2d v in p.Outer.Vertices) yield return v;
                foreach (Polygon2d h in p.Holes)
                    foreach (Vector2d v in h.Vertices) yield return v;
            }
        }

        static Paths64 Paths(IEnumerable<Path64> paths)
        {
            var result = new Paths64();
            result.AddRange(paths);
            return result;
        }
    }
}
