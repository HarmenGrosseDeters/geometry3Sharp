using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class ZzScratchFilterProbe
    {
        readonly ITestOutputHelper log;
        public ZzScratchFilterProbe(ITestOutputHelper l) { log = l; }

        [Fact]
        public void RingBucketTop()
        {
            // 20-vertex ring, y in [-1, 0], top edge horizontal at y = 0
            var xs = new List<double>(); var ys = new List<double>();
            xs.Add(0); ys.Add(0); xs.Add(1); ys.Add(0);
            for (int i = 0; i < 18; ++i) { xs.Add(1 - i / 17.0); ys.Add(-1 + 0.01 * (i % 2)); }
            var ring = xs.Select((x, i) => Pt.FromDoubles(x, ys[i])).ToArray();
            var p = Pt.FromDoubles(0.5, 0);
            var f = Domain.InRing(ring, p, true); var r = Domain.InRing(ring, p, false);
            log.WriteLine("InRing fast " + f + " ref " + r);
            var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), Rings = { new RingInput(Enumerable.Range(0, 20).ToArray(), false) } };
            var d1 = new Domain(input); var d2 = new Domain(input) { Filter = false };
            log.WriteLine("Classify fast " + d1.Classify(p) + " ref " + d2.Classify(p));
            Assert.Equal(r, f);
        }

        static string V(OracleReport r) => string.Join(",", r.Violations.Select(v => v.Kind)) + " | minsin2 " + r.MinSin2 + " | below " + r.BelowAngle;

        [Fact]
        public void HugeEquilateral()
        {
            double s = 1e80;
            var input = new Mesh2Input { X = new[] { 0, s, 0.5 * s }, Y = new[] { 0, 0, 0.8660254037844386 * s }, MinAngleDeg = 20,
                Rings = { new RingInput(new[] { 0, 1, 2 }, false) } };
            var o = new Mesh2Output { Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(),
                Triangles = new[] { new[] { 0, 1, 2 } }, InputToVertex = new[] { 0, 1, 2 }, BadTriangles = new int[0],
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 0) } };
            var e = new OracleExpect { Milestone = Milestone.M3 };
            var f = Oracle.Check(input, o, e); var r = Oracle.Check(input, o, e.WithReference());
            log.WriteLine("fast " + V(f) + "\nref  " + V(r));
            Assert.Equal(V(r), V(f));
        }
    }
}
