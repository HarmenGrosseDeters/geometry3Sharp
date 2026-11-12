using System;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #638 (MeshFuzzPilot, QA 2.0 F7): a cube projected to xy gave Failed (RefinementLimit), "refinement did not settle within 296225
    /// steps", at SteinerLimit 1500. Not a livelock and not the point 1e-286 of the extent away (at the origin it fails the same): the box
    /// [-w, 1 - w] x [0, 1] with a point at the origin makes a channel of width w = 2^-38 between segments 0-3 and 3-4. Splits across it
    /// flip the same triangles many times, and Collect queued a triangle once per flip (up to 31 536 entries on 1600 triangles for one
    /// insertion): a fixed burst of 600k queue steps, above the defect cap 64 (limit + slots) + 100000 at a small Steiner limit, while
    /// refinement goes on linearly after it. Collect now queues each rewritten triangle once; the run reaches the Steiner limit, an honest
    /// Success with SteinerLimitReached (as at w = 2^-30, where the burst stays below the cap).
    /// </summary>
    public class QualityMesh2NarrowChannelTests
    {
        /// <summary> the projected cube of #638: corners 1, 5 (and 2, 6; 3, 7) coincide, point 0 at (x0, 0) </summary>
        internal static Mesh2Input Channel(double x0, double w, int steinerLimit = 1500)
        {
            var input = new Mesh2Input {
                X = new[] { x0, 1 - w, 1 - w, -w, -w, 1 - w, 1 - w, -w },
                Y = new double[] { 0, 0, 1, 1, 0, 0, 1, 1 },
                MinAngleDeg = 25, ConstrainedQuality = true, Conforming = true, SteinerLimit = steinerLimit,
            };
            int[] s = { 0, 2, 1, 2, 0, 1, 0, 3, 2, 3, 4, 5, 5, 6, 4, 6, 6, 7, 4, 7, 0, 5, 0, 4, 1, 6, 2, 7, 3, 4 };
            for (int i = 0; i < s.Length; i += 2) input.Segments.Add(new SegmentInput(s[i], s[i + 1]));
            return input;
        }

        /// <summary> the issue's input, exactly as MeshFuzzPilot builds it (x0 = 1.0176044597843959E-286, w = 2^-38) </summary>
        internal static Mesh2Input Issue638() => Channel(1.0176044597843959E-286, Math.Pow(2, -38));

        static Mesh2Output Run(Mesh2Input input, string what, bool channelKept = true)
        {
            var o = new QualityMesh2Candidate().Run(input);
            Assert.True(o.Status == Mesh2Status.Success, what + ": " + o.Status + " " + o.FailureKind);
            // the honest outcome: the channel asks for about 1 / w vertices, so the limit stops refinement, and says so
            if (channelKept) Assert.True(o.SteinerLimitReached, what + ": the Steiner limit was not reported");
            Assert.True(o.VX.Length <= input.X.Length + input.SteinerLimit, what + ": " + o.VX.Length + " vertices");
            Assert.All(o.VX.Concat(o.VY), v => Assert.False(double.IsNaN(v) || double.IsInfinity(v), what + ": a vertex is not finite"));
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, what + ": " + report);
            return o;
        }

        [Fact]
        public void Issue638_ReachesTheSteinerLimit_NotTheDefectCap() => Run(Issue638(), "#638");

        /// <summary> the window where the burst ran into the cap at limit 1500 (2^-34 .. 2^-39; at 2^-40 and below the tolerance merges
        /// the channel's sides) </summary>
        [Theory]
        [InlineData(34)]
        [InlineData(36)]
        [InlineData(39)]
        public void Channel_AtTheOrigin_ReachesTheSteinerLimit(int k) => Run(Channel(0, Math.Pow(2, -k)), "w = 2^-" + k);

        public static TheoryData<string> Similarities()
        {
            var data = new TheoryData<string>();
            foreach (var s in Similarity.All2D()) data.Add(s.Name);
            return data;
        }

        /// <summary> under every similarity the run succeeds, oracle-valid, and ends at the Steiner limit where the channel survives the
        /// rounding; the 2^k scales give the same mesh bit for bit. (A translation to utm coordinates, about 1e6, rounds the channel's width
        /// 2^-38 away, an ulp there is 1.2e-10: the sides coincide and nothing needs refining) </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void Issue638_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Issue638();
            var moved = Issue638();
            for (int i = 0; i < plain.X.Length; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            bool translated = s.Translation.x != 0 || s.Translation.y != 0;
            var b = Run(moved, name, !translated);
            if (s.IsExact) {
                var a = Run(plain, "plain");
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes, and a fresh mesher: the same mesh (Collect's set of queued triangles is reused) </summary>
        [Fact]
        public void Issue638_Reuse_SameInstance()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Issue638());
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = false, SteinerLimit = 1500 };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            var fresh = new QualityMesh2 { MinAngle = 25, Conforming = false, SteinerLimit = 1500 }.Compute(c);
            Assert.Equal(Triangulation2Status.Success, a.Status);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            Assert.Equal(a.Mesh.Vertices, fresh.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, fresh.Mesh.Triangles);
        }
    }
}
