using System;
using System.Collections.Generic;
using g3;
using g3.Clipper2;

namespace Clipper2Benchmark
{
    /// <summary> The measured operations: a name and a delegate that returns a checksum (area), so nothing is optimized away. </summary>
    static class Ops
    {
        /// <summary> A hash of every coordinate bit and the ring structure: equal digests mean equal results bit for bit. </summary>
        public static double Digest(this Clipper2Result r)
        {
            long h = 17 + (int)r.Status;
            foreach (var p in r.Polygons) h = Mix(h, p);
            foreach (var l in r.PolyLines) { h = Mix(h, l.VertexCount); for (int i = 0; i < l.VertexCount; i++) { h = Mix(h, l[i].x); h = Mix(h, l[i].y); } }
            return (double)(h & ((1L << 52) - 1));
        }
        public static double Digest(List<GeneralPolygon2d> l) { long h = 3; foreach (var p in l) h = Mix(h, p); return (double)(h & ((1L << 52) - 1)); }
        public static double Digest(g3.Clipper2.Vendored.Paths64 l)
        {
            long h = 5;
            foreach (var path in l) { h = Mix(h, path.Count); foreach (var q in path) { h = Mix(h, q.X); h = Mix(h, q.Y); } }
            return (double)(h & ((1L << 52) - 1));
        }
        static long Mix(long h, long v) { unchecked { return (h ^ v) * 1099511628211L + (h >> 29); } }
        static long Mix(long h, double v) => Mix(h, BitConverter.DoubleToInt64Bits(v));
        static long Mix(long h, Polygon2d poly) { h = Mix(h, poly.VertexCount); for (int i = 0; i < poly.VertexCount; i++) { h = Mix(h, poly[i].x); h = Mix(h, poly[i].y); } return h; }
        static long Mix(long h, GeneralPolygon2d p) { h = Mix(h, p.Outer); h = Mix(h, p.Holes.Count); foreach (var hole in p.Holes) h = Mix(h, hole); return h; }

        public static List<(string Name, Func<double> Run)> All()
        {
            var ops = new List<(string, Func<double>)>();
            var m = new Clipper2Options();
            var miter = new Clipper2Options { JoinType = g3.Clipper2.JoinType.Miter, MiterLimit = 2 };
            foreach (int n in new[] { 1000, 20000, 100000 }) {
                var a = Inputs.StarPair(n, out var b);
                ops.Add(($"Union star n={n}", () => Clipper2Polygons.Union(a, b, m).Digest()));
                ops.Add(($"Intersection star n={n}", () => Clipper2Polygons.Intersection(a, b, m).Digest()));
            }
            // offsets of a smooth ring with many vertices (a densely sampled outline; the feature size is far above the offset distance)
            foreach (int n in new[] { 1000, 20000, 100000 }) {
                var ring = new List<GeneralPolygon2d> { new GeneralPolygon2d(Inputs.Star(n, Vector2d.Zero, 10, 0.1, 12, 0)) };
                ops.Add(($"Offset round ring n={n}", () => Clipper2Polygons.Offset(ring, 0.4, m).Digest()));
                ops.Add(($"Offset miter ring n={n}", () => Clipper2Polygons.Offset(ring, 0.4, miter).Digest()));
            }
            foreach (int g in new[] { 30, 100 }) {
                var a = Inputs.Grid(g, 0);
                var b = Inputs.Grid(g, 0.4);
                string s = $"grid {g}x{g} ({g * g} polys)";
                ops.Add(($"Union {s}", () => Clipper2Polygons.Union(a, b, m).Digest()));
                ops.Add(($"Difference {s}", () => Clipper2Polygons.Difference(a, b, m).Digest()));
                ops.Add(($"Offset round {s}", () => Clipper2Polygons.Offset(a, 0.05, m).Digest()));
                ops.Add(($"Offset miter {s}", () => Clipper2Polygons.Offset(a, 0.05, miter).Digest()));
            }
            // bridge conversions alone (Polygon2d <-> Paths64, PolyTree64 -> GeneralPolygon2d)
            foreach (int g in new[] { 30, 100 }) {
                var a = Inputs.Grid(g, 0);
                var res = Clipper2Polygons.Union(a, null, m);
                ClipperFrame frame = res.Frame;
                string s = $"grid {g}x{g}";
                ops.Add(($"Bridge ToPaths {s}", () => Digest(frame.ToPaths(a))));
                var paths = frame.ToPaths(a);
                var tree = new g3.Clipper2.Vendored.PolyTree64();
                var c = new g3.Clipper2.Vendored.Clipper64();
                c.AddSubject(paths);
                c.Execute(g3.Clipper2.Vendored.ClipType.Union, g3.Clipper2.Vendored.FillRule.NonZero, tree);
                ops.Add(($"Bridge ToGeneralPolygons {s}", () => Digest(frame.ToGeneralPolygons(tree))));
            }
            {
                var a = Inputs.StarPair(20000, out var b);
                var frame = Clipper2Polygons.Union(a, b, m).Frame;
                ops.Add(("Bridge ToPaths star n=20000", () => Digest(frame.ToPaths(a))));
            }
            return ops;
        }
    }
}
