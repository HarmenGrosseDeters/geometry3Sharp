using System;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Regression tests for #1403 (owner decision Option A): under snap rounding with identity rules,
    /// subsegment markers follow combinatorial snapped-chain provenance (R8, D7), not a 1-pixel Euclidean tube.
    /// </summary>
    public class Meshing2OracleSnapMarkerTests
    {
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = 0x9E3779B97F4A7C15UL ^ seed; }
            public double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
        }

        static Mesh2Input Soup(int n, double snapTol)
        {
            var input = new Mesh2Input { SnapTolerance = snapTol };
            var xs = new double[2 * n];
            var ys = new double[2 * n];
            var rng = new Rng((ulong)n * 31 + 4UL);
            int p = 0;
            for (int i = 0; i < n; ++i) {
                double ax = rng.Next(), ay = rng.Next(), bx = rng.Next(), by = rng.Next();
                if (ax == bx && ay == by) continue;
                xs[p] = ax; ys[p] = ay;
                xs[p + 1] = bx; ys[p + 1] = by;
                input.Segments.Add(new SegmentInput(p, p + 1, input.Segments.Count % 7));
                p += 2;
            }
            Array.Resize(ref xs, p);
            Array.Resize(ref ys, p);
            input.X = xs;
            input.Y = ys;
            return input;
        }

        [Fact]
        public void Soup40_AtSnapTolerance_PassesOracleFastAndReference()
        {
            var input = Soup(40, 1.0 / 1024.0);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);

            var fast = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(fast.Ok, "fast oracle: " + fast);

            var reference = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M2, Reference = true });
            Assert.True(reference.Ok, "reference oracle: " + reference);
        }

        [Fact]
        public void CrossingWithDistinctAndZeroMarkers_SnappedM2AndM3_PassesOracleAndRejectsWrongMarker()
        {
            // Two crossing segments with 1-pixel legs leaving the crossing: Euclidean distance to the crossing
            // segment is <= 1 px at the adjacent pixel even though L_inf > 0.5 px (the crossing segment does not enter it).
            foreach (int m1 in new[] { 5, 0 }) {
                var input = new Mesh2Input {
                    SnapTolerance = 1.0,
                    X = new[] { 2.2, 2.0, 0.8, 3.2, -2.0, 6.0, 6.0, -2.0 },
                    Y = new[] { 3.3, 0.9, 2.2, 1.8, -2.0, -2.0, 6.0, 6.0 },
                };
                input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, false, 1));
                input.Segments.Add(new SegmentInput(0, 1, m1));
                input.Segments.Add(new SegmentInput(2, 3, 3));

                var m2Out = new ConstrainedDelaunayMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Success, m2Out.Status);
                var m2Rep = Oracle.Check(input, m2Out, new OracleExpect { Milestone = Milestone.M2 });
                Assert.True(m2Rep.Ok, "M2 m1=" + m1 + ": " + m2Rep);

                // M3 quality refinement on the snap grid must preserve chain markers across refined stretches
                input.MinAngleDeg = 20;
                var m3Out = new QualityMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Success, m3Out.Status);
                var m3Rep = Oracle.Check(input, m3Out, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(m3Rep.Ok, "M3 m1=" + m1 + ": " + m3Rep);

                // Corrupt one subsegment marker on the crossing chain: oracle must reject as SubsegmentMarker
                var target = m3Out.Subsegments.First(s => s.Marker == m1);
                target.Marker = m1 == 0 ? 3 : 0;
                var badRep = Oracle.Check(input, m3Out, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(badRep.Has(Violation.SubsegmentMarker), "corrupted M3 m1=" + m1 + ": " + badRep);
            }
        }

        [Fact]
        public void CoincidingSnappedChains_DistinctMarkers_ProduceZeroAndListBoth()
        {
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0.0, 0.1));
            input.Points.Add(new Vector2d(4.0, 0.1));
            input.Points.Add(new Vector2d(0.0, -0.1));
            input.Points.Add(new Vector2d(4.0, -0.1));
            input.Points.Add(new Vector2d(2.0, 4.0));
            input.Segments.Add(new Mesh2Segment(0, 1, 5));
            input.Segments.Add(new Mesh2Segment(2, 3, 3));

            var res = new ConstrainedDelaunayMesh2 { SnapTolerance = 1.0 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            var shared = Assert.Single(res.Mesh.Subsegments);
            Assert.Equal(0, shared.Marker);
            Assert.Equal(new[] { 3, 5 }, shared.Markers);
        }
    }
}
