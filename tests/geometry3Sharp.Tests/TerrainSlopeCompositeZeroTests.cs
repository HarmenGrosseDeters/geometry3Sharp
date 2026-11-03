// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The composite's moves by ulps (SlopeComposite, onto the slope side or off it) from a coordinate of exactly 0: a
    /// terrain grid line through the origin, crossed by the toe's edges. One ulp of 0 is the subnormal 4.9e-324 in every
    /// frame, which 2^k does not scale; the step from 0 is the ulp of the edge's coordinate scale, which does.
    /// (Base API only, so CI's new-tests-on-base runs it against the old library; the step's unit test is in
    /// TerrainSlopeCompositeStepTests.)
    /// </summary>
    public class TerrainSlopeCompositeZeroTests
    {
        // a square toe (half side 6, rotated by 9.4°, centred at (0.3, 1.1)) on a tilted 16 x 16 grid on [-40, 40]^2: the
        // grid lines x = 0 and y = 0 cross the toe's edges
        static TerrainSlopeDesign Make(Similarity s)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5 + 0.03 * x + 0.02 * y);
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisZ, 9.4);
            var toe = TerrainSlopeDesignTests.Square(6, 0).Select(p => R * p + new Vector3d(0.3, 1.1, 0)).Select(p => s.Apply(p)).ToList();
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) },
                Terrain = terrain, Tolerance = s.ApplyLength(0.05), BuildComposite = true
            };
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void GridLineThroughTheOrigin_CompositeInvariant(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Make(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            var r = reference.Value;
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                // (no subnormal coordinate anywhere: a step from 0 is never the smallest double)
                foreach (Vector3d v in d.Composite.Vertices())
                    Assert.True((v.x == 0 || Math.Abs(v.x) >= 2.2250738585072014E-308) && (v.y == 0 || Math.Abs(v.y) >= 2.2250738585072014E-308), v.ToString());
            }
        }
    }
}
