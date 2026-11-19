using System;
using System.Collections.Generic;
using g3;
using g3.Clipper2;

namespace Clipper2Benchmark
{
    /// <summary> The measured offsets: a name and a delegate that returns a digest of the result (equal digests: equal results bit for bit). </summary>
    static class Ops
    {
        public static double Digest(this Clipper2Result r)
        {
            long h = 17 + (int)r.Status;
            foreach (var p in r.Polygons) { h = Mix(h, p.Outer); h = Mix(h, p.Holes.Count); foreach (var hole in p.Holes) h = Mix(h, hole); }
            return (double)(h & ((1L << 52) - 1));
        }
        static long Mix(long h, long v) { unchecked { return (h ^ v) * 1099511628211L + (h >> 29); } }
        static long Mix(long h, double v) => Mix(h, BitConverter.DoubleToInt64Bits(v));
        static long Mix(long h, Polygon2d poly) { h = Mix(h, poly.VertexCount); for (int i = 0; i < poly.VertexCount; i++) { h = Mix(h, poly[i].x); h = Mix(h, poly[i].y); } return h; }

        public static List<(string Name, Func<Clipper2Result> Run)> All(int[] sizes)
        {
            var ops = new List<(string, Func<Clipper2Result>)>();
            var round = new Clipper2Options();
            var miter = new Clipper2Options { JoinType = g3.Clipper2.JoinType.Miter, MiterLimit = 2 };
            // offsets of a smooth ring with many vertices (a densely sampled outline; the feature size is far above the offset distance, #1169)
            foreach (int n in sizes) {
                var ring = new List<GeneralPolygon2d> { new GeneralPolygon2d(Inputs.Star(n, Vector2d.Zero, 10, 0.1, 12, 0)) };
                ops.Add(($"Offset round ring n={n}", () => Clipper2Polygons.Offset(ring, 0.4, round)));
                ops.Add(($"Offset miter ring n={n}", () => Clipper2Polygons.Offset(ring, 0.4, miter)));
            }
            foreach (int g in new[] { 30, 100 }) {
                var a = Inputs.Grid(g, 0);
                ops.Add(($"Offset round grid {g}x{g}", () => Clipper2Polygons.Offset(a, 0.05, round)));
                ops.Add(($"Offset miter grid {g}x{g}", () => Clipper2Polygons.Offset(a, 0.05, miter)));
            }
            return ops;
        }
    }
}
