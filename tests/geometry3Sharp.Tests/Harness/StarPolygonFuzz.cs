using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A deterministic fuzz of small snapped meshing inputs (Q17, rule (ii')): a star polygon of 4 to 9 vertices around (8, 8), radii 1
    /// to 8, coordinates at 0.1, 0 to 2 free points in [0, 16]², in a third of the seeds one segment between two input points; pixel 1,
    /// 20°, at most 5000 Steiner points. Seeds 0 to 2999 are the set measured in the Q17 PR (M3: 211 rejected, 197 by rule (ii') alone:
    /// 9 by SRR's restore step, 188 refinement pinches). For reuse by the mesher's bugfix and the oracle's own restore step.
    /// </summary>
    public static class StarPolygonFuzz
    {
        public static Mesh2Input Make(int seed)
        {
            var rnd = new Random(seed);
            int n = 4 + rnd.Next(6), free = rnd.Next(3);
            double cx = 8, cy = 8;
            var ang = Enumerable.Range(0, n).Select(_ => rnd.NextDouble() * 2 * Math.PI).OrderBy(a => a).ToArray();
            var xs = new List<double>(); var ys = new List<double>();
            foreach (var a in ang) { double r = 1 + 7 * rnd.NextDouble(); xs.Add(Math.Round(cx + r * Math.Cos(a), 1)); ys.Add(Math.Round(cy + r * Math.Sin(a), 1)); }
            for (int i = 0; i < free; ++i) { xs.Add(Math.Round(16 * rnd.NextDouble(), 1)); ys.Add(Math.Round(16 * rnd.NextDouble(), 1)); }
            var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 5000 };
            input.Rings.Add(new RingInput(Enumerable.Range(0, n).ToArray(), false));
            if (rnd.Next(3) == 0) { int a = rnd.Next(xs.Count), b = rnd.Next(xs.Count); if (a != b) input.Segments.Add(new SegmentInput(a, b)); }
            return input;
        }
    }
}
