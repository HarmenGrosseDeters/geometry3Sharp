using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// An input whose outer rings all collapse in snapping (D14) has an empty domain, not the convex hull (D13 is the rule for input
    /// without outer rings): snapping keeps the collapsed rings' pieces as segments, and with no outer ring left the builder took the hull
    /// of all points for the domain and meshed free points outside the rings (star polygon fuzz seeds 37, 41, 1019, 1052, 1331, 2200,
    /// 2248: TriangleOutsideDomain and SubsegmentUnsourced; found again by the lens fuzz of #370). Only the library's existing API
    /// here, so that these tests run against the library before the fix (they fail there); the flag and the diagnostic are tested in
    /// QualityMesh2CollapsedRingDomainLibraryTests.
    /// </summary>
    public class QualityMesh2CollapsedRingDomainTests
    {
        static Mesh2Output Run(Mesh2Input input, string mode)
        {
            if (mode == "M2") { input.MinAngleDeg = null; return new ConstrainedDelaunayMesh2Candidate().Run(input); }
            if (mode == "M3cq") input.ConstrainedQuality = true;
            return new QualityMesh2Candidate().Run(input);
        }

        static OracleReport Judge(Mesh2Input input, Mesh2Output o, string mode)
            => Oracle.Check(input, o, new OracleExpect { Milestone = mode == "M2" ? Milestone.M2 : Milestone.M3 });

        public static IEnumerable<object[]> Seeds()
        {
            foreach (int seed in QualityMesh2KeepsChainsApartTests.EmptyDomain)
                foreach (var mode in new[] { "M2", "M3", "M3cq" }) yield return new object[] { seed, mode };
        }

        /// <summary> the fuzz seeds: empty, accepted by the oracle, in every mode </summary>
        [Theory]
        [MemberData(nameof(Seeds))]
        public void StarFuzz_CollapsedRing_EmptyDomain(int seed, string mode)
        {
            var input = StarPolygonFuzz.Make(seed);
            var o = Run(input, mode);
            Assert.Equal(Mesh2Status.Empty, o.Status);
            Assert.Empty(o.Triangles);
            var report = Judge(input, o, mode);
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary> a ring 0.1 wide along y = 0.4 (snapped to the grid line y = 0 at pixel 1) and a free point well outside it </summary>
        internal static Mesh2Input ThinRing(bool square, bool squareIsHole = false)
        {
            var x = new List<double> { 0.2, 10.2, 20.2, 10.2, 6, 3 }; var y = new List<double> { 0.4, 0.45, 0.4, 0.35, 8, 5 };
            var input = new Mesh2Input { SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 5000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            if (square) {
                // a second outer ring that survives, the free point (6, 8) inside it and (3, 5) outside both
                x.AddRange(new double[] { 4, 9, 9, 4 }); y.AddRange(new double[] { 6, 6, 11, 11 });
                input.Rings.Add(new RingInput(new[] { 6, 7, 8, 9 }, squareIsHole));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }

        [Theory]
        [InlineData("M2")]
        [InlineData("M3")]
        [InlineData("M3cq")]
        public void ThinRing_Collapses_DomainIsEmpty(string mode)
        {
            var input = ThinRing(false);
            var o = Run(input, mode);
            Assert.Equal(Mesh2Status.Empty, o.Status);
            Assert.Empty(o.Triangles);
            var report = Judge(input, o, mode);
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary> the outer ring collapses, a hole ring survives: still empty (a surviving hole is no outer ring) </summary>
        [Theory]
        [InlineData("M2")]
        [InlineData("M3")]
        [InlineData("M3cq")]
        public void OuterCollapses_HoleSurvives_DomainIsEmpty(string mode)
        {
            var input = ThinRing(true, true);
            var o = Run(input, mode);
            Assert.Equal(Mesh2Status.Empty, o.Status);
            Assert.Empty(o.Triangles);
            var report = Judge(input, o, mode);
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary> control: one outer ring collapses, another survives; the domain is the survivor (unchanged by the fix), and a free point
        /// outside both stays outside </summary>
        [Theory]
        [InlineData("M2")]
        [InlineData("M3")]
        [InlineData("M3cq")]
        public void OneOfTwoRingsCollapses_DomainIsTheOther(string mode)
        {
            var input = ThinRing(true);
            var o = Run(input, mode);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Judge(input, o, mode);
            Assert.True(report.Ok, report.ToString());
            Assert.All(o.Triangles, t => Assert.True(t.All(v => o.VX[v] >= 4 && o.VX[v] <= 9 && o.VY[v] >= 6 && o.VY[v] <= 11)));
        }

        /// <summary> control: without rings the domain is still the hull (D13) </summary>
        [Fact]
        public void NoRings_HullDomain()
        {
            var input = ThinRing(false);
            input.Rings.Clear();
            var o = Run(input, "M3");
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.NotEmpty(o.Triangles);
            Assert.True(Judge(input, o, "M3").Ok);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> in every frame the oracle accepts the mesh; at the 2^k scales the ring collapses and the domain is empty there too </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void ThinRing_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var plain = ThinRing(false);
            var moved = ThinRing(false);
            moved.SnapTolerance = plain.SnapTolerance * s.Scale;
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var o = Run(moved, "M3");
            Assert.True(Judge(moved, o, "M3").Ok, name);
            if (s.IsExact) {
                Assert.Equal(Mesh2Status.Empty, o.Status);
                Assert.Empty(o.Triangles);
            }
        }

        /// <summary> reuse: one mesher instance twice on the input that collapses, and then on one that does not </summary>
        [Fact]
        public void Reuse_SameInstance()
        {
            var mesher = new QualityMesh2 { MinAngle = 20, SnapTolerance = 1 };
            var collapsing = ConstrainedDelaunayMesh2Candidate.Convert(ThinRing(false));
            var surviving = ConstrainedDelaunayMesh2Candidate.Convert(ThinRing(true));
            Assert.Equal(Triangulation2Status.Empty, mesher.Compute(collapsing).Status);
            var a = mesher.Compute(surviving);
            Assert.Equal(Triangulation2Status.Empty, mesher.Compute(collapsing).Status);
            var b = mesher.Compute(surviving);
            Assert.Equal(Triangulation2Status.Success, a.Status);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
        }
    }
}
