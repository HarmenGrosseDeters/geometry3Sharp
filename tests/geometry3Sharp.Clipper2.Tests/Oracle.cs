using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using g3.Clipper2;
using Xunit;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Reference checks by definition, sharing no code with Clipper2 or the adapter: winding numbers by signed edge
    /// crossings, point-segment distances, shoelace areas, and generators of test polygons.
    /// </summary>
    static class Oracle
    {
        /// <summary> winding number of a closed ring around p (signed crossings of the ray to +x) </summary>
        public static int Winding(IReadOnlyList<Vector2d> ring, Vector2d p)
        {
            int w = 0, n = ring.Count;
            for (int i = 0; i < n; ++i) {
                Vector2d a = ring[i], b = ring[(i + 1) % n];
                double side = (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y);
                if (a.y <= p.y) {
                    if (b.y > p.y && side > 0) ++w;
                } else {
                    if (b.y <= p.y && side < 0) --w;
                }
            }
            return w;
        }

        public static int Winding(Polygon2d ring, Vector2d p) => Winding(ring.Vertices, p);

        /// <summary> winding number of an input polygon: outer +1 and holes -1 inside, whatever their orientation </summary>
        public static int Winding(GeneralPolygon2d g, Vector2d p)
        {
            int w = Math.Abs(Winding(g.Outer, p));
            foreach (Polygon2d h in g.Holes)
                w -= Math.Abs(Winding(h, p));
            return w;
        }

        /// <summary> the number of result polygons that contain p (outer winding 1, no hole winding); at most 1 for a valid result </summary>
        public static int ResultCount(IEnumerable<GeneralPolygon2d> result, Vector2d p)
        {
            int count = 0;
            foreach (GeneralPolygon2d g in result) {
                if (Winding(g.Outer, p) == 0) continue;
                if (g.Holes.Any(h => Winding(h, p) != 0)) continue;
                ++count;
            }
            return count;
        }

        public static double SegmentDistance(Vector2d p, Vector2d a, Vector2d b)
        {
            Vector2d d = b - a;
            double len2 = d.x * d.x + d.y * d.y;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.x - a.x) * d.x + (p.y - a.y) * d.y) / len2)) : 0;
            Vector2d q = new Vector2d(a.x + t * d.x, a.y + t * d.y);
            return Math.Sqrt((p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y));
        }

        public static double RingDistance(IReadOnlyList<Vector2d> ring, Vector2d p, bool closed = true)
        {
            double d = double.MaxValue;
            int n = ring.Count, m = closed ? n : n - 1;
            for (int i = 0; i < m; ++i)
                d = Math.Min(d, SegmentDistance(p, ring[i], ring[(i + 1) % n]));
            if (n == 1) d = (p - ring[0]).Length;
            return d;
        }

        public static IEnumerable<IReadOnlyList<Vector2d>> Rings(GeneralPolygon2d g)
        {
            yield return g.Outer.Vertices;
            foreach (Polygon2d h in g.Holes) yield return h.Vertices;
        }

        public static double BoundaryDistance(IEnumerable<GeneralPolygon2d> polygons, Vector2d p)
            => polygons.SelectMany(Rings).Select(r => RingDistance(r, p)).DefaultIfEmpty(double.MaxValue).Min();

        /// <summary> shoelace area relative to a reference point (accurate far from the origin) </summary>
        public static double SignedArea(IReadOnlyList<Vector2d> ring)
        {
            if (ring.Count == 0) return 0;
            Vector2d c = ring[0];
            double a = 0;
            for (int i = 1; i + 1 < ring.Count; ++i) {
                Vector2d u = ring[i] - c, v = ring[i + 1] - c;
                a += u.x * v.y - u.y * v.x;
            }
            return 0.5 * a;
        }

        public static double Area(IEnumerable<GeneralPolygon2d> polygons)
            => polygons.Sum(g => Math.Abs(SignedArea(g.Outer.Vertices)) - g.Holes.Sum(h => Math.Abs(SignedArea(h.Vertices))));

        /// <summary>
        /// The structural checks of a polygon result: finite vertices, outer rings counterclockwise and holes
        /// clockwise (non-zero area), every hole inside its outer ring, and the polygons and holes not overlapping
        /// (checked at the given sample points).
        /// </summary>
        public static void CheckWellFormed(Clipper2Result r, IEnumerable<Vector2d> samples = null)
        {
            Assert.True(r.Ok, r.ToString());
            foreach (GeneralPolygon2d g in r.Polygons) {
                Assert.True(g.Outer.VertexCount >= 3);
                Assert.All(g.Outer.Vertices, v => Assert.True(v.IsFinite));
                Assert.True(SignedArea(g.Outer.Vertices) > 0, "outer ring not counterclockwise");
                foreach (Polygon2d h in g.Holes) {
                    Assert.True(h.VertexCount >= 3);
                    Assert.True(SignedArea(h.Vertices) < 0, "hole not clockwise");
                    // a hole lies inside its outer ring: its vertices are inside or on the outer ring
                    foreach (Vector2d v in h.Vertices)
                        Assert.True(Winding(g.Outer, v) != 0 || RingDistance(g.Outer.Vertices, v) <= 4 * r.Frame.Resolution,
                            "hole vertex " + v + " outside its outer ring");
                }
            }
            if (samples != null)
                foreach (Vector2d p in samples)
                    Assert.True(ResultCount(r.Polygons, p) <= 1, "result polygons overlap at " + p);
        }

        public static GeneralPolygon2d Rect(double x0, double y0, double x1, double y1)
            => new GeneralPolygon2d(new Polygon2d(new[] { new Vector2d(x0, y0), new Vector2d(x1, y0), new Vector2d(x1, y1), new Vector2d(x0, y1) }));

        public static Polygon2d Ring(params double[] xy)
        {
            var v = new List<Vector2d>();
            for (int i = 0; i + 1 < xy.Length; i += 2) v.Add(new Vector2d(xy[i], xy[i + 1]));
            return new Polygon2d(v);
        }

        /// <summary> a random star-shaped polygon (simple, counterclockwise) </summary>
        public static Polygon2d Star(Random rand, Vector2d center, int n, double rmin, double rmax)
        {
            var v = new List<Vector2d>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * (i + 0.8 * rand.NextDouble()) / n;
                double r = rmin + (rmax - rmin) * rand.NextDouble();
                v.Add(new Vector2d(center.x + r * Math.Cos(a), center.y + r * Math.Sin(a)));
            }
            return new Polygon2d(v);
        }

        /// <summary> a random polygon with one star-shaped hole around its center </summary>
        public static GeneralPolygon2d StarWithHole(Random rand, Vector2d center, int n, double r)
        {
            var g = new GeneralPolygon2d(Star(rand, center, n, 0.7 * r, r));
            Polygon2d hole = Star(rand, center, n, 0.2 * r, 0.45 * r);
            hole.Reverse();
            g.AddHole(hole, false, false);
            return g;
        }

        public static IEnumerable<Vector2d> Samples(Random rand, AxisAlignedBox2d box, int n)
        {
            for (int i = 0; i < n; ++i)
                yield return new Vector2d(box.Min.x + rand.NextDouble() * box.Width, box.Min.y + rand.NextDouble() * box.Height);
        }
    }
}
