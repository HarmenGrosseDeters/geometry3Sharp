using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Situations reported against other meshing libraries, rebuilt here from their description only (no code read): each runs as a
    /// constrained Delaunay triangulation (M2) and refined (M3) in the default and the constrained-quality mode, with and without a snap
    /// tolerance where the situation is about tolerances, and must pass the oracle (or fail cleanly where the situation asks for it).
    /// </summary>
    public class Meshing2TrackerCasesTests
    {
        sealed class Builder
        {
            readonly List<double> x = new List<double>(), y = new List<double>();
            public readonly Mesh2Input Input = new Mesh2Input();
            public int P(double px, double py) { x.Add(px); y.Add(py); return x.Count - 1; }
            public int[] Ps(params double[] xy) { var r = new int[xy.Length / 2]; for (int i = 0; i < r.Length; ++i) r[i] = P(xy[2 * i], xy[2 * i + 1]); return r; }
            public void Ring(bool hole, params double[] xy) => Input.Rings.Add(new RingInput(Ps(xy), hole));
            public void RingOf(bool hole, int[] ids) => Input.Rings.Add(new RingInput(ids, hole));
            public void Seg(int a, int b) => Input.Segments.Add(new SegmentInput(a, b));
            public void Seg(double ax, double ay, double bx, double by) => Seg(P(ax, ay), P(bx, by));
            public Mesh2Input Done(double snap = 0) { Input.X = x.ToArray(); Input.Y = y.ToArray(); Input.SnapTolerance = snap; return Input; }
        }

        /// <summary> the situations, by name </summary>
        internal static Mesh2Input Case(string name)
        {
            var b = new Builder();
            switch (name) {
                // a constraint loop given twice (CGAL#3267: non-termination when the same loop is inserted twice)
                case "loop-twice": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    var loop = b.Ps(3, 3, 7, 3, 7, 7, 3, 7);
                    for (int k = 0; k < 2; ++k) for (int i = 0; i < 4; ++i) b.Seg(loop[i], loop[(i + 1) % 4]);
                    var again = b.Ps(3, 3, 7, 3, 7, 7, 3, 7);
                    for (int i = 0; i < 4; ++i) b.Seg(again[i], again[(i + 1) % 4]);
                    return b.Done();
                }
                // a point 1e-15 off a segment, the orientation decided by an epsilon elsewhere (poly2tri#39)
                case "point-1e-15-off": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    b.Seg(1, 5, 9, 5);
                    b.P(5, 5 + 1e-15); b.P(3, 5 - 1e-15); b.P(7, 5);
                    return b.Done();
                }
                // a hole vertex collinear with an outer vertex and another hole vertex: a bridge along that line may fill the hole (earcut#219)
                case "collinear-bridge": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    b.Ring(true, 2, 2, 5, 2, 8, 2, 8, 6, 5, 6, 2, 6);
                    b.P(5, 0.5);
                    return b.Done();
                }
                // holes touching each other and the outer ring at single vertices (earcut#129, poly2tri#55)
                case "holes-touching": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    b.Ring(true, 0, 5, 3, 3, 3, 7);          // touches the outer ring at (0, 5)
                    b.Ring(true, 3, 7, 6, 5, 6, 9);          // touches the first hole at (3, 7)
                    b.Ring(true, 6, 5, 9, 2, 10, 5);         // touches the second at (6, 5) and the outer ring at (10, 5)
                    return b.Done();
                }
                // constraints that already are Delaunay edges: a regular grid of points whose grid lines are constraints (JTS#1190: Steiner
                // points added to them, no convergence)
                case "delaunay-edges": {
                    b.Ring(false, 0, 0, 8, 0, 8, 8, 0, 8);
                    var id = new int[9, 9];
                    for (int i = 0; i <= 8; ++i) for (int j = 0; j <= 8; ++j) id[i, j] = (i == 0 || j == 0 || i == 8 || j == 8) ? -1 : b.P(i, j);
                    for (int i = 1; i < 8; ++i) for (int j = 1; j < 7; ++j) { b.Seg(id[i, j], id[i, j + 1]); b.Seg(id[j, i], id[j + 1, i]); }
                    return b.Done();
                }
                // short near-collinear constraints crossing at a shallow angle, the crossing 1e-7 off (CDT#229)
                case "shallow-crossing": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    b.Seg(4, 5, 6, 5 + 1e-7);
                    b.Seg(4, 5 + 1e-7, 6, 5);
                    b.Seg(4.5, 5 - 3e-8, 5.5, 5 + 7e-8);
                    return b.Done();
                }
                // an edge through many collinear connected vertices (CDT#86: recursion depth)
                case "collinear-chain": {
                    b.Ring(false, -1, -1, 2001, -1, 2001, 1, -1, 1);
                    int prev = b.P(0, 0);
                    for (int i = 1; i <= 2000; ++i) { int c = b.P(i, 0); b.Seg(prev, c); prev = c; }
                    b.Seg(b.P(0, 0), b.P(2000, 0));
                    return b.Done();
                }
                // zero-length constraints (CGAL#781)
                case "zero-length": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    int p = b.P(5, 5); b.Seg(p, p);
                    b.Seg(3, 3, 3, 3);
                    b.Seg(7, 2, 7, 2 + 1e-300);
                    return b.Done();
                }
                // snap rounding a chain that doubles back, at 9e8 (CGAL#4741)
                case "double-back-9e8": {
                    double o = 9e8;
                    b.Ring(false, o, o, o + 64, o, o + 64, o + 64, o, o + 64);
                    var chain = b.Ps(o + 10, o + 10, o + 50, o + 30.0000001, o + 10.0000002, o + 30, o + 50, o + 50.0000003);
                    for (int i = 0; i + 1 < chain.Length; ++i) b.Seg(chain[i], chain[i + 1]);
                    return b.Done(0.5);
                }
                // two vertices 1e-8 apart against a tolerance of 1e-5 (JTS#321): merged
                case "close-pair": {
                    b.Ring(false, 0, 0, 10, 0, 10, 10, 0, 10);
                    b.Seg(2, 5, 8, 5);
                    b.P(5, 5 + 1e-8); b.P(5 + 1e-8, 7); b.P(5, 7);
                    return b.Done(1e-5);
                }
                // a self-crossing comb-shaped hole (GEOS#1283: a clean rejection expected there)
                case "comb-hole": {
                    b.Ring(false, 0, 0, 20, 0, 20, 10, 0, 10);
                    b.Ring(true, 2, 2, 18, 2, 18, 8, 16, 8, 16, 3, 14, 9, 12, 3, 10, 9, 8, 3, 6, 9, 4, 3, 4, 8, 2, 8);
                    return b.Done();
                }
                // a point 2e-11 off a hull edge (delaunator#91); points only, the hull is the boundary
                case "hull-2e-11": {
                    b.Ps(0, 0, 10, 0, 10, 10, 0, 10, 5, -2e-11, 5, 5, 2.5, 1e-11, 7.5, 2e-11);
                    return b.Done();
                }
            }
            throw new ArgumentException(name);
        }

        public static IEnumerable<object[]> Runs()
        {
            var names = new[] { "loop-twice", "point-1e-15-off", "collinear-bridge", "holes-touching", "delaunay-edges", "shallow-crossing",
                "collinear-chain", "zero-length", "double-back-9e8", "close-pair", "comb-hole", "hull-2e-11" };
            foreach (var n in names)
                foreach (var mode in new[] { "M2", "M3@20", "M3@30", "M3c@20", "M3c@30" })
                    yield return new object[] { n, mode };
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void TrackerCase_PassesTheOracle(string name, string mode)
        {
            var input = Case(name);
            var milestone = mode == "M2" ? Milestone.M2 : Milestone.M3;
            if (milestone == Milestone.M3) {
                input.MinAngleDeg = mode.EndsWith("30") ? 30 : 20;
                // (two segments 1e-7 apart along a length of 2 have a local feature size of 1e-7 all along: a quality mesh needs about
                // length / gap vertices, so refinement stops at the limit, O7, with a valid mesh)
                input.SteinerLimit = name == "shallow-crossing" ? 5000 : 200000;
                input.ConstrainedQuality = mode.StartsWith("M3c");
            }
            var o = CandidateRegistry.For(milestone).Run(input);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = milestone });
            Assert.True(report.Ok, name + " " + mode + " (" + o.Status + "): " + report);
            Assert.Equal(Mesh2Status.Success, o.Status);
            if (name == "shallow-crossing") Assert.Equal(milestone == Milestone.M3, o.SteinerLimitReached);
            else Assert.False(o.SteinerLimitReached, name + " " + mode + " reached the Steiner limit");
            // two vertices 1e-8 apart against a tolerance of 1e-5 are one vertex
            if (name == "close-pair") Assert.Equal(o.InputToVertex[7], o.InputToVertex[8]);
        }
    }
}
