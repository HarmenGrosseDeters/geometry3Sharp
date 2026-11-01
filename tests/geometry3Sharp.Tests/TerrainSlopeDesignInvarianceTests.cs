using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// CLAUDE.md for TerrainSlopeDesign: invariance over Similarity.All() with rotations about z (the slope is defined
    /// against the vertical), O1 in every frame on the transformed output itself; reuse; determinism (repeated runs).
    /// </summary>
    public class TerrainSlopeDesignInvarianceTests
    {
        static Similarity ZUp(Similarity s) =>
            s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;

        // an L-shaped pit (convex and concave corners) and an island, slopes 1:1 and 1:1.5 with a jump
        static TerrainSlopeDesign Make(Similarity s)
        {
            var outer = new[] { (0.0, 0.0), (30, 0), (30, 12), (12, 12), (12, 30), (0, 30) }.Select(p => s.Apply(new Vector3d(p.Item1, p.Item2, 2))).ToList();
            var specs = new[] { SlopeSpec.Ratio(1), SlopeSpec.Ratio(1), SlopeSpec.Ratio(1.5), SlopeSpec.Ratio(1.5), SlopeSpec.Ratio(1), SlopeSpec.Ratio(1) };
            var toe = new SlopeToe() { Points = outer, Closed = true, Side = SlopeToe.Sides.Outside, CutStart = specs };
            var island = SlopeToe.Constant(new[] { (4.0, 4.0), (8, 4), (6, 8) }.Select(p => s.Apply(new Vector3d(p.Item1, p.Item2, 2))).ToList(),
                true, SlopeToe.Sides.Inside, SlopeSpec.Percent(80));
            return new TerrainSlopeDesign() {
                Toes = new List<SlopeToe> { toe, island },
                HeightLimit = s.Apply(new Vector3d(0, 0, 9)).z,
                Tolerance = s.ApplyLength(0.01)
            };
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            var d = Make(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            // the checks in the transformed frame itself: O1 on the stored output, the surface, O2
            TerrainSlopeDesignTests.AssertAll(d);
            TerrainSlopeDesignTests.AssertBoundary(d);
            var r = reference.Value;
            if (s.IsExact) {
                // 2^k: the same result, scaled, bit for bit
                Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
                Assert.Equal(r.UsedToeLowering * s.Scale, d.UsedToeLowering);
            } else {
                // the same surface up to Tolerance: the mapped-back vertices lie on the reference within it
                DMesh3 back = Invariance.MappedBack(d.SlopeSurface, s);
                var spatial = new DMeshAABBTree3(r.SlopeSurface, true);
                foreach (Vector3d v in back.Vertices()) {
                    var ray = new Ray3d(new Vector3d(v.x, v.y, 1000), -Vector3d.AxisZ);
                    int hit = spatial.FindNearestHitTriangle(ray);
                    if (hit < 0) continue;      // at the boundary, by rounding
                    var intr = MeshQueries.TriangleIntersection(r.SlopeSurface, hit, ray);
                    Assert.True(Math.Abs(intr.RayParameter - (1000 - v.z)) <= 0.011, $"{v}: {1000 - intr.RayParameter}");
                }
                Assert.Equal(r.Pieces.Count, d.Pieces.Count);
            }
        }

        [Fact]
        public void Reuse_AndDeterminism()
        {
            var a = Make(Similarity.Identity);
            Assert.True(a.Compute());
            DMesh3 first = a.SlopeSurface;
            // again on the same instance: the same result
            Assert.True(a.Compute());
            Assert.Equal("", Invariance.Difference(first, a.SlopeSurface));
            // another input on the same instance equals a fresh instance
            var other = SlopeToe.Constant(TerrainSlopeDesignTests.Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(2));
            a.Toes = new List<SlopeToe> { other }; a.HeightLimit = 5;
            Assert.True(a.Compute());
            var fresh = new TerrainSlopeDesign() { Toes = new List<SlopeToe> { other }, HeightLimit = 5, Tolerance = 0.01 };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.SlopeSurface, a.SlopeSurface));
            Assert.Equal(1, a.Pieces.Max(p => p.Toe) + 1);
            // a failure clears the outputs of the earlier success
            a.Tolerance = double.NaN;
            Assert.False(a.Compute());
            Assert.Null(a.SlopeSurface);
            Assert.Empty(a.Pieces);
            // repeated runs from fresh instances are bitwise equal
            for (int k = 0; k < 3; ++k) {
                var b = Make(Similarity.Identity);
                Assert.True(b.Compute());
                Assert.Equal("", Invariance.Difference(first, b.SlopeSurface));
            }
        }

        [Fact]
        public void Cancel()
        {
            var d = Make(Similarity.Identity);
            int calls = 0;
            d.CancelF = () => ++calls > 3;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.Cancelled, d.Status);
            Assert.Null(d.SlopeSurface);
        }
    }
}
