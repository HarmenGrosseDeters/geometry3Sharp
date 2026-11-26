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
    [Collection("Terrain301")]       // (one design of 301 is shared by the tests of both classes; they must not run it at once)
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

        // #1539: the angles whose realization ended in "non-manifold realization" (96, 100, 119, 120, 132, 159, 177), in "left a crack"
        // (234, 236, 238, 244, 278) or in SlopeViolation by a turned-over sliver (291): slivers of different faces a few ulps wide
        // along one line. 100 has tiny triangles under the degenerate area of NotCleanReason besides, the others are clean.
        [Theory]
        [InlineData(100, true)]
        [InlineData(119, false)]
        [InlineData(120, false)]
        [InlineData(132, false)]
        [InlineData(159, false)]
        [InlineData(177, false)]
        [InlineData(234, false)]
        [InlineData(238, false)]
        [InlineData(278, false)]
        public void AroundCorner_RotatedWithSliverStrips_Succeeds(int degrees, bool tinyTriangles) { SliverStripsSucceed(degrees, tinyTriangles); }

        // (Slow: these four run three or four realizations each, 2.5-4.5 s alone, 5-8 s on a loaded runner; under 10 s)
        [Theory, Trait("Category", "Slow")]
        [InlineData(96, false)]
        [InlineData(236, false)]
        [InlineData(244, false)]
        [InlineData(291, false)]
        public void AroundCorner_RotatedWithSliverStrips_SlowSucceeds(int degrees, bool tinyTriangles) { SliverStripsSucceed(degrees, tinyTriangles); }

        static void SliverStripsSucceed(int degrees, bool tinyTriangles)
        {
            var d = Make(degrees);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeRampTests.AssertRamps(d);
            Assert.Equal(tinyTriangles ? MeshRobustRepair.Defects.NotClosed | MeshRobustRepair.Defects.DegenerateTriangles : MeshRobustRepair.Defects.NotClosed,
                         MeshRobustRepair.FindDefects(d.SlopeSurface));
        }

        // The surface of angles that succeed with the strict realization (the plain ones are refused, #1628) is pinned: counts and a hash of
        // the topology (triangle ids, vertices, groups; no coordinates, so that it holds on every runtime). The zip realization of #1647
        // runs only after strict throws, so these angles must keep the surface strict gives: a zip run in its place changes the triangles.
        // FNV-1a of the triangle ids, vertices and groups
        internal static string TopologyHash(DMesh3 m)
        {
            ulong h = 14695981039346656037UL;
            Func<ulong, int, ulong> fnv = (acc, v) => { unchecked { for (int i = 0; i < 4; ++i) { acc ^= (ulong)((v >> (8 * i)) & 0xFF); acc *= 1099511628211UL; } } return acc; };
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                h = fnv(fnv(fnv(fnv(fnv(h, t), tv.a), tv.b), tv.c), m.GetTriangleGroup(t));
            }
            return h.ToString("x16");
        }

        [Theory]
        [InlineData(117, 452, 653, "54b45d240c450401")]
        [InlineData(127, 450, 651, "5b2e07889356bd85")]
        public void StrictAngles_KeepTheirSurface(int degrees, int vertexCount, int triangleCount, string topologyHash)
        {
            var d = Make(degrees);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            DMesh3 m = d.SlopeSurface;
            Assert.Equal(vertexCount, m.VertexCount);
            Assert.Equal(triangleCount, m.TriangleCount);
            Assert.Equal(topologyHash, TopologyHash(m));
        }

        // #1539, angle 301: a needle of altitude 2e-15 at (-39.234, -0.0177) whose flip changes the surface by 1.4e-11, 7 times the reserved
        // flip share: the plane of the needle fails every margin. The rerun with a scaled flip share (budgets counted) removes it
        // (Slow: the first attempts fail, the rerun runs them again: 6 builds, one design, under 10 s alone)
        [Fact, Trait("Category", "Slow")]
        public void AroundCorner_301_NeedleWithARefusedFlip_Succeeds()
        {
            var d = Reference301.Value;
            Assert.Equal(MeshRobustRepair.Defects.NotClosed | MeshRobustRepair.Defects.DegenerateTriangles, MeshRobustRepair.FindDefects(d.SlopeSurface));
            Assert.True(d.UsedToeLowering > 0 && d.UsedToeLowering <= d.Tolerance, "toe lowering " + d.UsedToeLowering);
            Assert.Equal("", d.Message);
            Assert.Equal(Reference301Vertices, d.SlopeSurface.VertexCount);
            Assert.Equal(Reference301Triangles, d.SlopeSurface.TriangleCount);
            Assert.Equal(Reference301Topology, TopologyHash(d.SlopeSurface));
        }

        // the oracle of the ramps on 301 (the surface within Tolerance of the references, below and above, every triangle exact): the check is
        // 2 s on top of the design of 8 s, so it runs nightly (the success test above keeps the design under 10 s)
        [Fact, Trait("Category", "Nightly")]
        public void AroundCorner_301_Oracle()
        {
            TerrainSlopeRampTests.AssertRamps(Reference301.Value);
        }

        // the surface of 301 (the topology: no coordinates, so that it holds on every runtime), pinned for the frames below
        internal const int Reference301Vertices = 2475, Reference301Triangles = 4624;
        internal const string Reference301Topology = "bf9f78a6f7df6ccd";

        // 301 in the 2^k frames: the rerun's budgets and the flips scale with the frame, bit for bit (Nightly: the identity reference and each
        // frame are 5-8 s alone; the first case of the run pays for the reference as well, 13 s)
        internal static readonly Lazy<TerrainSlopeDesign> Reference301 = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(301);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // two frames in the PR tier (Slow: one design of 301 each, 7.7 to 8.5 s alone): the surface has the topology of the identity frame, which is
        // pinned above (the bit for bit comparison with the identity frame's coordinates is the Nightly test below)
        [Theory, Trait("Category", "Slow")]
        [InlineData("scale 2^-10")]
        [InlineData("scale 2^10")]
        public void AroundCorner_301_Pow2Frames(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            var d = Make(301, s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(Reference301Vertices, d.SlopeSurface.VertexCount);
            Assert.Equal(Reference301Triangles, d.SlopeSurface.TriangleCount);
            Assert.Equal(Reference301Topology, TopologyHash(d.SlopeSurface));
        }

        [Theory]
        [Trait("Category", "Nightly")]
        [MemberData(nameof(Similarity.NamesExceptExtremeFrame), MemberType = typeof(Similarity))]
        public void AroundCorner_301_Similarities(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            var d = Make(301, s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeDesignTests.AssertSurface(d);
            Assert.True(d.MaxSlopeRatio <= 1);
            // (the open boundary, and the needles only where the frame's rounding leaves them)
            var defects = MeshRobustRepair.FindDefects(d.SlopeSurface);
            Assert.True((defects & MeshRobustRepair.Defects.NotClosed) != 0 && (defects & ~(MeshRobustRepair.Defects.NotClosed | MeshRobustRepair.Defects.DegenerateTriangles)) == 0, defects.ToString());
            if (s.IsExact) Assert.Equal("", Invariance.Difference(Reference301.Value.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
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

        // the strict realization with its sliver bound and strips (119 degrees): the 2^k frames give the identity's result, scaled, bit for bit
        [Theory]
        [MemberData(nameof(Similarity.NamesExceptExtremeFrame), MemberType = typeof(Similarity))]
        public void SliverStrips_Similarities(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            if (s.IsExact == false) return;       // (only the 2^k frames repeat the identity's result bit for bit; the others are covered by the identity frame's tests above)
            var d = Make(119, s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            var r = Make(119);
            Assert.True(r.Compute(), r.Message);
            Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
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

        // ---- a sweep: the Nightly tier runs every degree, and every angle ends in its exact outcome ----

        // The outcome of each angle of the 1 degree sweep (measured with the fixes of #1539; an angle not in the list succeeds with a surface
        // whose only defect is NotClosed). A change of any angle's outcome fails the sweep.
        // - Needles: Success, and MeshRobustRepair.FindDefects also reports DegenerateTriangles (area below 1e-12 diag^2, about
        //   1.5e-8): the map's slivers between creases a few ulps apart (long, height 1e-10 to 1e-9) and its tiny triangles at
        //   clusters of near-coincident vertices (heights up to 1e-4), all far below the tolerance of 1e-2. The library is right
        //   here: the slope and support checks accept every triangle and AssertRamps below checks the surface against the exact
        //   specification (heights, width, boundary); the generic defect check of MeshRobustRepair is an absolute area threshold.
        static readonly HashSet<int> Needles = new HashSet<int> { 25, 26, 28, 30, 31, 41, 46, 57, 66, 67, 69, 71, 72, 76, 82, 94, 99, 100, 102, 107, 118, 123, 128, 133, 136, 139, 143, 148, 150, 153, 166, 171, 173, 174, 176, 181, 184, 192, 196, 202, 208, 212, 217, 223, 227, 241, 273, 274, 280, 319 };
        // - 301 succeeds with needles since #1539's flip share rerun; its own Slow test above pins it (a design of 8 s does not belong in a shard of five).
        //   Every angle of the sweep now succeeds: a failure is a regression.
        const int Slow301 = 301;

        const int SweepShards = 72;
        public static IEnumerable<object[]> Shards() => Enumerable.Range(0, SweepShards).Select(i => new object[] { i });

        // Nightly: the run is the check (360 designs at about 0.7 s: 250 s), more than the 10 s of the PR tier; in 72 shards of 5
        // angles (each test about 4 s alone, 13 s at most with four running at once; the angles with a strict rerun take 2-4 s each)
        [Theory]
        [MemberData(nameof(Shards))]
        [Trait("Category", "Nightly")]
        public void Sweep_EveryDegree_HasItsOutcome(int shard)
        {
            var bad = new List<string>();
            for (int a = shard * 360 / SweepShards; a < (shard + 1) * 360 / SweepShards; ++a) {
                if (a == Slow301) continue;
                var d = Make(a);
                bool ok = d.Compute();
                string expected = Needles.Contains(a) ? "Success: not closed, degenerate triangles" : "Success: not closed";
                string got;
                if (ok) {
                    TerrainSlopeRampTests.AssertRamps(d);
                    got = "Success: " + MeshRobustRepair.DescribeDefects(MeshRobustRepair.FindDefects(d.SlopeSurface));
                    if (Needles.Contains(a)) AssertOnlyNeedles(d.SlopeSurface);
                } else
                    got = d.Status == TerrainSlopeDesign.Statuses.Failed ? "Failed: " + d.Message : d.Status.ToString();
                if (got != expected) bad.Add($"{a} degrees: {got} (expected {expected})");
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        // every triangle below the degenerate area of NotCleanReason is a needle or a tiny triangle: its height over the longest edge
        // at most 1e-3 (a tenth of the tolerance of 1e-2; they measure up to 1.1e-4)
        static void AssertOnlyNeedles(DMesh3 m)
        {
            AxisAlignedBox3d box = MeshRepairMetrics.TriangleBounds(m);
            double eps = 1e-12 * box.DiagonalLength * box.DiagonalLength;
            foreach (int t in m.TriangleIndices()) {
                double area = m.GetTriArea(t);
                if (area >= eps) continue;
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                double longest = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a)));
                Assert.True(2 * area / longest <= 1e-3, $"triangle {t}: height {2 * area / longest}");
            }
        }
    }
}
