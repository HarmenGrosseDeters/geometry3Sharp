using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Public-API tests of #1539 (they compile against the base library: new-tests-on-base). #1539: the around-the-corner ramp (TerrainSlopeRampTests.AroundCorner) rotated about z by 127, 205 and 271 degrees.
    /// The map's faces along the road's end edge are slivers a few ulps wide. The rounding turns some of their triangles
    /// over (clockwise) or lays them over a neighbour's (an edge run in the same direction by two triangles; DMesh3 takes
    /// that), and the next triangle on that edge is refused: "SlopeMap: non-manifold realization". After the two realizations
    /// of the design (with and without the merge) the strict one drops such slivers and stitches the neighbours.
    /// </summary>
    public class TerrainSlopeRampRotationRegressionTests
    {
        // AroundCorner mapped by 'rotation' (degrees about z through the origin) and then by 's' (the toe points, the path, the
        // height limit, the width, the tolerance; no LocalFrame)
        internal static TerrainSlopeDesign Make(double degrees, Similarity s = null)
        {
            var rot = new Similarity("rot", 1, Matrix3d.AxisAngleD(Vector3d.AxisZ, degrees), Vector3d.Zero);
            s = s ?? Similarity.Identity;
            var d = TerrainSlopeRampTests.AroundCorner();
            foreach (var t in d.Toes) t.Points = t.Points.Select(p => s.Apply(rot.Apply(p))).ToList();
            foreach (var r in d.Ramps) {
                r.Path = r.Path.Select(p => s.Apply(rot.Apply(new Vector3d(p.x, p.y, 0))).xy).ToList();
                r.Width = s.ApplyLength(r.Width);
            }
            d.HeightLimit = s.Apply(new Vector3d(0, 0, d.HeightLimit)).z;
            d.Tolerance = s.ApplyLength(d.Tolerance);
            return d;
        }

        // a frame about z only (the slope is defined against the vertical)
        static Similarity ZUp(Similarity s) =>
            s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;

        // success: the ramp's oracles, the surface (finite, manifold, counter-clockwise, no XY overlap), open only at the toe and the
        // height limit (NotClosed is the one defect of NotCleanReason that a slope surface has by design)
        static void AssertGood(TerrainSlopeDesign d)
        {
            Assert.True(d.Status == TerrainSlopeDesign.Statuses.Success, d.Status + " " + d.Message);
            TerrainSlopeRampTests.AssertRamps(d);
            Assert.Equal(MeshRobustRepair.Defects.NotClosed, MeshRobustRepair.FindDefects(d.SlopeSurface));
            Assert.Equal("not closed", MeshRobustRepair.NotCleanReason(d.SlopeSurface));
        }

        // the angles of a 1 degree sweep (both ramp designs of the issue) that failed with "non-manifold realization" and succeed now
        [Theory]
        [InlineData(117)]
        [InlineData(127)]
        [InlineData(138)]
        [InlineData(205)]
        [InlineData(242)]
        [InlineData(271)]
        [InlineData(322)]
        public void AroundCorner_Rotated_Succeeds_AndIsClean(int degrees)
        {
            var d = Make(degrees);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertGood(d);
        }

        // the 2^k frames give the result of the rotated design, scaled, bit for bit; every frame succeeds (the reference, 127 degrees, is
        // the one that failed in the identity frame before)
        [Theory]
        [MemberData(nameof(Similarity.NamesExceptExtremeFrame), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            var d = Make(127, s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            // (TerrainSlopeRampTests' oracles are written for the unit frame: the reference's own are checked below, the others
            // through the surface checks and, for 2^k, the bit for bit equality with the reference)
            TerrainSlopeDesignTests.AssertSurface(d);
            Assert.True(d.MaxSlopeRatio <= 1);
            Assert.Equal(MeshRobustRepair.Defects.NotClosed, MeshRobustRepair.FindDefects(d.SlopeSurface));
            if (s.IsExact) {
                var r = Reference.Value;
                AssertGood(r);
                Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
            }
        }

        static readonly Lazy<TerrainSlopeDesign> Reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(127);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        [Fact]
        public void Reuse_AndDeterminism()
        {
            var a = Make(271);
            Assert.True(a.Compute(), a.Message);
            DMesh3 first = a.SlopeSurface;
            // the same instance again, a fresh instance: the same surface, vertex for vertex
            Assert.True(a.Compute(), a.Message);
            Assert.Equal("", Invariance.Difference(first, a.SlopeSurface));
            var b = Make(271);
            Assert.True(b.Compute(), b.Message);
            Assert.Equal("", Invariance.Difference(first, b.SlopeSurface));
        }

        // (the strict retry only follows two refused realizations: a design that realizes without it is as before)
        [Fact]
        public void AroundCorner_Unrotated_StillSucceeds()
        {
            var d = TerrainSlopeRampTests.AroundCorner();
            Assert.True(d.Compute(), d.Message);
            AssertGood(d);
        }

        // ---- a sweep: the Nightly tier runs every degree; the ones that still fail are listed, so that a new one is a regression ----

        // #1539 (open): removing an angle from this list is that issue's done-check.
        // angles of the 1 degree sweep that still end in a typed non-Success status or "Failed" (the same family of slivers, whose
        // neighbour cannot be stitched: the edge m x of the split exists already, or the sliver's long edge is no boundary edge)
        static readonly HashSet<int> StillFailing = new HashSet<int> { 96, 100, 119, 120, 132, 159, 177, 234, 236, 238, 244, 278, 291, 301 };

        [Fact]
        [Trait("Category", "Nightly")]
        public void Sweep_EveryDegree_FailsOnlyWhereItDidBefore()
        {
            // (360 designs at about 0.6 s: the run is the check, more than the 10 s of the PR tier)
            var bad = new List<string>();
            for (int a = 0; a < 360; ++a) {
                var d = Make(a);
                bool ok = d.Compute();
                if (ok) { AssertGood(d); continue; }
                if (StillFailing.Contains(a) == false) bad.Add(a + ": " + d.Status + " " + d.Message);
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }
    }
}
