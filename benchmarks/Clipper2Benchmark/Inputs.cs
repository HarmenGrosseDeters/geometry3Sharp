using System;
using System.Collections.Generic;
using g3;
using g3.Clipper2;

namespace Clipper2Benchmark
{
    /// <summary> Procedural inputs only (no files): wavy stars with many crossings and grids of small polygons. </summary>
    static class Inputs
    {
        /// <summary> Star-shaped ring of n vertices, radius r(1 + a sin(waves t + phase)), counterclockwise. </summary>
        public static Polygon2d Star(int n, Vector2d c, double r, double a, int waves, double phase)
        {
            var v = new Vector2d[n];
            for (int i = 0; i < n; i++) {
                double t = 2 * Math.PI * i / n;
                double rr = r * (1 + a * Math.Sin(waves * t + phase));
                v[i] = new Vector2d(c.x + rr * Math.Cos(t), c.y + rr * Math.Sin(t));
            }
            return new Polygon2d(v);
        }

        /// <summary> Two overlapping wavy rings of n vertices; the crossings grow linearly with n (amplitude about a third of a wavelength). </summary>
        public static List<GeneralPolygon2d> StarPair(int n, out List<GeneralPolygon2d> other)
        {
            int waves = Math.Max(3, n / 12);
            double a = 3.0 / waves;
            other = new List<GeneralPolygon2d> { new GeneralPolygon2d(Star(n, new Vector2d(0.2, 0.1), 10, a, waves + 1, 0.7)) };
            return new List<GeneralPolygon2d> { new GeneralPolygon2d(Star(n, Vector2d.Zero, 10, a, waves, 0)) };
        }

        /// <summary> g x g rotated hexagons; every third one has a triangular hole. Offset shifts the grid. </summary>
        public static List<GeneralPolygon2d> Grid(int g, double shift)
        {
            var list = new List<GeneralPolygon2d>(g * g);
            double rot = 0.26;
            for (int i = 0; i < g; i++)
                for (int j = 0; j < g; j++) {
                    var c = new Vector2d(i * 1.0 + shift, j * 1.0 + shift);
                    var v = new Vector2d[6];
                    for (int k = 0; k < 6; k++) {
                        double t = rot + k * Math.PI / 3;
                        v[k] = new Vector2d(c.x + 0.62 * Math.Cos(t), c.y + 0.62 * Math.Sin(t));
                    }
                    var gp = new GeneralPolygon2d(new Polygon2d(v));
                    if ((i + j) % 3 == 0) {
                        var h = new Vector2d[3];
                        for (int k = 0; k < 3; k++) {
                            double t = rot - k * 2 * Math.PI / 3; // clockwise
                            h[k] = new Vector2d(c.x + 0.2 * Math.Cos(t), c.y + 0.2 * Math.Sin(t));
                        }
                        gp.AddHole(new Polygon2d(h), false, false);
                    }
                    list.Add(gp);
                }
            return list;
        }
    }
}
