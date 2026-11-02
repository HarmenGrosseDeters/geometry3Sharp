// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A toe that crosses the terrain (part of it above, part below): the catch line runs from toe to toe, and the
    /// composite stays a height field where the region meets the toe at the crossings.
    /// </summary>
    public class TerrainSlopeToeCrossingTests
    {
        // a 7-gon pit at z 0 (r 11) in a plane tilted by g towards the angle rot, on a grid of 23 x 25 cells
        internal static TerrainSlopeDesign Make(double g, double c, double rot, bool fill, Similarity s)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-60, -60, 60, 60), 23, 25,
                (x, y) => g * (x * Math.Cos(rot) + y * Math.Sin(rot)) + c);
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            var toe = SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(7, 11, 0, Vector2d.Zero, 0.2).Select(p => s.Apply(p)).ToList(),
                true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(fill ? 2 : 1.5));
            return new TerrainSlopeDesign() {
                Toes = { toe }, Terrain = terrain, Tolerance = s.ApplyLength(0.05), BuildComposite = true,
                Direction = fill ? TerrainSlopeDesign.Directions.Fill : TerrainSlopeDesign.Directions.Cut
            };
        }

        [Theory]
        [InlineData(0.15, 0.3, 0.0, false)]
        [InlineData(0.15, 0.3, 0.37, true)]
        [InlineData(0.23, -1.1, 1.3, false)]
        // the toe edge from a toe vertex to the crossing crosses terrain edges: the slope part along it took the edge as
        // one constraint, the terrain part beside it (the design side of the cut terrain triangle) its pieces at those
        // crossings, rounded off the straight line: their triangles overlapped (CompositeFailed)
        [InlineData(0.07, 0.123, 2.1, false)]
        [InlineData(0.07, 0.123, 2.1, true)]
        public void CatchLineFromToeToToe_CompositeIsAHeightField(double g, double c, double rot, bool fill)
        {
            var d = Make(g, c, rot, fill, Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, fill);
            TerrainSlopeCatchLineTests.AssertComposite(d, fill);
            Assert.Single(d.CatchLines);
            Assert.False(d.CatchLines[0].Closed);
            Assert.Equal(CatchLine.EndReasons.Toe, d.CatchLines[0].StartReason);
            Assert.Equal(CatchLine.EndReasons.Toe, d.CatchLines[0].EndReason);
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(0.07, 0.123, 2.1, false, Similarity.Identity);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Make(0.07, 0.123, 2.1, false, s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            var r = reference.Value;
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
            }
        }
    }
}
