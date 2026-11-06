using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The decisions of the small-angle rule (QualityMesh2.Refiner.ApexExempt; MESHING2D_SMALL_ANGLES.md section 2), read from the
    /// refiner's log of vertex encroachments: which vertices exempt which subsegments, and which do not (Shewchuk 2002 section 6.2
    /// splits for a parent that "is an input vertex or lies on a nonincident segment").
    /// </summary>
    public class QualityMesh2ApexExemptionTests
    {
        static List<(Vector2d U, Vector2d W, Vector2d V, bool Exempt)> Log(Mesh2Input input, out Mesh2Output output, bool? exemption = null)
        {
            var log = new List<(Vector2d U, Vector2d W, Vector2d V, bool Exempt)>();
            QualityMesh2.Refiner.SmallAngleExemptionForTests = exemption;
            QualityMesh2.Refiner.ApexLogForTests = log;
            try { output = new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.SmallAngleExemptionForTests = null; QualityMesh2.Refiner.ApexLogForTests = null; }
            Assert.True(output.Status == Mesh2Status.Success, output.Status + " " + output.FailureKind);
            Assert.False(output.SteinerLimitReached);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            return log;
        }

        /// <summary>
        /// a = A-B from A = (0, 0) to B = (10, 0), c from A at a small angle, and a top point (8, 6); kind:
        /// 0 c = A-(16, 1) through an input point P = (8, 0.5);
        /// 1 c = A-F with F = (8, 0.5), which a segment to the top continues (c's far endpoint, on two sources);
        /// 2 c = A-(16, 1), and a segment from the top ends on c at Y = (8, 0.5) (an input vertex with a third source);
        /// 3 a = A-(16, 0) through an input point Q = (2, 0), and c = A-(16, 1).
        /// The vertex (8, 0.5) faces a's subsegment from A in kinds 0-2.
        /// </summary>
        internal static Mesh2Input Pair(int kind, double angle = 30)
        {
            var x = new List<double> { 0, kind == 3 ? 16 : 10, 8 };
            var y = new List<double> { 0, 0, 6 };
            var segments = new List<SegmentInput> { new SegmentInput(0, 1) };
            void Add(double px, double py) { x.Add(px); y.Add(py); }
            if (kind == 1) { Add(8, 0.5); segments.Add(new SegmentInput(0, 3)); segments.Add(new SegmentInput(3, 2)); }
            else {
                Add(16, 1); segments.Add(new SegmentInput(0, 3));
                if (kind == 0) Add(8, 0.5);
                if (kind == 2) { Add(8, 0.5); segments.Add(new SegmentInput(4, 2)); }
                if (kind == 3) Add(2, 0);
            }
            var xy = new double[2 * x.Count];
            for (int i = 0; i < x.Count; ++i) { xy[2 * i] = x[i]; xy[2 * i + 1] = y[i]; }
            var input = Generators.Literal(xy);
            input.Segments.AddRange(segments);
            input.MinAngleDeg = angle;
            input.ConstrainedQuality = true;
            return input;
        }

        static readonly Vector2d At = new Vector2d(8, 0.5);

        static bool OnA(Vector2d p) => p.y == 0;

        /// <summary> an input point on c, c's far endpoint, an input vertex with a third source: each encroaches a subsegment of a (at
        /// least once) and never exempts it </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void InputVertexOnThePartner_DoesNotExempt(int kind)
        {
            var log = Log(Pair(kind), out _);
            var byIt = log.Where(l => l.V == At && OnA(l.U) && OnA(l.W)).ToList();
            Assert.NotEmpty(byIt);
            Assert.All(byIt, l => Assert.False(l.Exempt, l.U + " - " + l.W));
        }

        /// <summary> a refinement vertex on c encroaching a's subsegment between two input vertices Q = (2, 0) and B = (16, 0): neither end is
        /// on c, and their distances from the apex differ by 8 (more than 1.5 from the vertex's), so it is split as before </summary>
        [Fact]
        public void FarSubsegment_IsSplitAsBefore()
        {
            var log = Log(Pair(3), out _);
            var far = log.Where(l => l.V == At && (l.U == new Vector2d(2, 0) && l.W == new Vector2d(16, 0) || l.U == new Vector2d(16, 0) && l.W == new Vector2d(2, 0))).ToList();
            Assert.NotEmpty(far);
            Assert.All(far, l => Assert.False(l.Exempt));
            // the rule does act elsewhere in this mesh
            Assert.Contains(log, l => l.Exempt);
        }

        /// <summary> the hull wedge: refinement vertices on the segment exempt the hull edge's shell subsegment from A; without the rule
        /// nothing is exempt, and the cascade makes many times the triangles </summary>
        [Fact]
        public void HullWedge_ShellSubsegmentIsExempt()
        {
            var log = Log(QualityMesh2SmallAngleRuleTests.HullWedge(30), out var on);
            Assert.Contains(log, l => l.Exempt && (l.U == Vector2d.Zero || l.W == Vector2d.Zero));
            var off = Log(QualityMesh2SmallAngleRuleTests.HullWedge(30), out var offOutput, exemption: false);
            Assert.DoesNotContain(off, l => l.Exempt);
            Assert.True(on.Triangles.Length * 4 < offOutput.Triangles.Length, on.Triangles.Length + " against " + offOutput.Triangles.Length);
        }

        /// <summary> input angles at or above the bound (also between it and 60 degrees, where Shewchuk's clusters reach), and in the
        /// oracle's window below it (30 degrees - 6e-10 rad): no exemption, and the mesh is the one without the rule, bit for bit </summary>
        [Theory]
        [InlineData(30.0)]
        [InlineData(31.0)]
        [InlineData(30 - 6e-10 * 180 / Math.PI)]
        [InlineData(40.0)]
        public void AngleAtTheBound_IsNotSmall(double degrees)
        {
            var log = Log(QualityMesh2SmallAngleRuleTests.HullWedge(30, degrees), out var on);
            Assert.DoesNotContain(log, l => l.Exempt);
            Log(QualityMesh2SmallAngleRuleTests.HullWedge(30, degrees), out var off, exemption: false);
            Assert.Equal(off.VX, on.VX);
            Assert.Equal(off.VY, on.VY);
            Assert.Equal(off.Triangles.SelectMany(t => t), on.Triangles.SelectMany(t => t));
        }

        /// <summary> in the shell cascade the encroaching partner vertex lies at about half the radius of the shell subsegment's far end:
        /// beyond radius matching (1.5), so the shell rule (i) exempts it (the wedge at 20 degrees, with lenses) </summary>
        [Fact]
        public void ShellSubsegment_AtTwiceTheRadius_IsExempt()
        {
            var log = Log(QualityMesh2SmallAngleRuleTests.HullWedge(20), out _);
            Assert.Contains(log, l => l.Exempt && (l.U == Vector2d.Zero || l.W == Vector2d.Zero)
                && (l.U == Vector2d.Zero ? l.W : l.U).LengthSquared > 2.25 * l.V.LengthSquared);
        }

        /// <summary>
        /// Both sources end at the apex (a segment P-A and a segment B-A): computed in doubles, the parameter of the apex on one of them is
        /// 1 + 2^-52, and the strict test of SpansSmallAngle misses the pair (about a fifth of random such pairs). The exact meeting test
        /// pairs them: the subsegment of B-A from the apex is exempt
        /// </summary>
        [Fact]
        public void BothSourcesEndAtTheApex_ArePaired()
        {
            var input = Generators.Literal(0.207, 0.778, 8.553, 0.778, 6.172, 1.083, 0.2, 6);
            input.Segments.Add(new SegmentInput(2, 0)); input.Segments.Add(new SegmentInput(1, 0));
            input.MinAngleDeg = 30;
            input.ConstrainedQuality = true;
            var apex = new Vector2d(0.207, 0.778);
            var log = Log(input, out _);
            Assert.Contains(log, l => l.Exempt && (l.U == apex || l.W == apex));
        }

        /// <summary> the conforming mode is unchanged: no exemption, the same mesh as without the rule </summary>
        [Fact]
        public void ConformingMode_IsUnchanged()
        {
            var input = QualityMesh2SmallAngleRuleTests.HullWedge(25, 5.0);
            input.ConstrainedQuality = false;
            var log = Log(input, out var on);
            Assert.DoesNotContain(log, l => l.Exempt);
            Log(input, out var off, exemption: false);
            Assert.Equal(off.VX, on.VX);
            Assert.Equal(off.Triangles.SelectMany(t => t), on.Triangles.SelectMany(t => t));
        }

        /// <summary> how two closed segments meet, exactly: a shared endpoint, a T-junction, a crossing, and three that do not meet </summary>
        [Fact]
        public void MeetKind_IsExact()
        {
            int Kind(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
                => QualityMesh2.Refiner.MeetKind(new Vector2d(ax, ay), new Vector2d(bx, by), new Vector2d(cx, cy), new Vector2d(dx, dy));
            Assert.Equal(1, Kind(0, 0, 10, 0, 0, 0, 10, 1));            // shared endpoint
            Assert.Equal(1, Kind(10, 0, 0, 0, 10, 1, 0, 0));            // the same, reversed
            Assert.Equal(2, Kind(0, 0, 10, 0, 5, 0, 10, 1));            // T-junction: c starts on a's interior
            Assert.Equal(2, Kind(0, 0, 10, 0, 1, -1e-3, 9, 1e-3));      // crossing at a small angle
            // a near miss: c ends 2^-30 above a long a (inside a relative window of 1e-9 of a, but not on it)
            Assert.Equal(0, Kind(0, 0, 1e6, 0, 5e5, 1.0 / (1 << 30), 6e5, 1));
            Assert.Equal(0, Kind(0, 0, 10, 0, 10 + 1e-9, 0, 20, 1));    // c starts just past a's end
            Assert.Equal(0, Kind(0, 0, 10, 0, 2, 0, 20, 0));            // collinear, overlapping: no apex
        }
    }
}
