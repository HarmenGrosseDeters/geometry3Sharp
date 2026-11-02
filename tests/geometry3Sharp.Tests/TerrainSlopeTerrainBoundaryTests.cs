// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A toe that leaves the terrain across its boundary: the slope does not reach the terrain everywhere
    /// (NoDaylight, a partial result), and the composite is built and is a height field.
    /// </summary>
    public class TerrainSlopeTerrainBoundaryTests
    {
        // a square toe of side 10 centred at (37.3, 1.1) on a tilted 16 x 16 grid on [-40, 40]^2: its edges cross the
        // terrain's boundary edge at x = 40 (SlopeMargin 1e-4: see TerrainSlopeMixedTests.MakeForFrames; with the default
        // the toe is below MinFeatureSize at scale 1e-3 and UTM)
        static TerrainSlopeDesign Make(Similarity s)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5 + 0.03 * x);
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            var toe = TerrainSlopeDesignTests.Square(10, 0, new Vector2d(37.3, 1.1)).Select(p => s.Apply(p)).ToList();
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) },
                Terrain = terrain, Tolerance = s.ApplyLength(0.05), BuildComposite = true, SlopeMargin = 1e-4
            };
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity);
            d.Compute();
            return d;
        });

        // in the rotated frames a crossing of a toe edge and the terrain's boundary edge, on the slope part, rounded
        // 1 ulp off the slope side of the toe line: the slope part's edge to it dipped below the terrain nodes moved
        // off the slope side there, and the parts overlapped (CompositeFailed instead of NoDaylight)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ToeLeavesTheTerrain_NoDaylight_CompositeIsAHeightField(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Make(s);
            Assert.False(d.Compute());
            Assert.True(d.Status == TerrainSlopeDesign.Statuses.NoDaylight, d.Status + " " + d.Message);
            Assert.False(d.IsComplete);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            // no vertex of the slope region strictly inside the (convex) toe: the slope part stays on the slope side
            var toe = d.Toes[0].Points.Select(p => p.xy).ToList();
            foreach (Vector3d v in d.SlopeRegion.Vertices()) {
                var o = Enumerable.Range(0, toe.Count).Select(i => ExactPredicates.Orient2d(toe[i], toe[(i + 1) % toe.Count], v.xy)).ToList();
                Assert.False(o.All(x => x > 0) || o.All(x => x < 0), $"slope region vertex {v.x:R}, {v.y:R} inside the toe");
            }
            var r = reference.Value;
            Assert.Equal(TerrainSlopeDesign.Statuses.NoDaylight, r.Status);
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
            }
        }

        [Fact]
        public void Reuse()
        {
            var d = Make(Similarity.Identity);
            d.Compute();
            var c1 = d.Composite;
            d.Compute();
            Assert.Equal(TerrainSlopeDesign.Statuses.NoDaylight, d.Status);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
        }
    }
}
