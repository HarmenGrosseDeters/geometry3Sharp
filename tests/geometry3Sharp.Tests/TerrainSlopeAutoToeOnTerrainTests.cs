// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Direction Auto with the toe meeting the terrain exactly where it crosses it (docs/design/TERRAIN.md §11): the
    /// catch point of the cut part there is the toe point, so its region in the terrain triangle at the crossing is a
    /// sliver of zero width along the triangle's edge, and both parts' regions hold that piece of the edge. The joint
    /// triangulation of the triangles both runs replace took a piece in either region as no constraint (and dropped
    /// pieces whose ends have the same parameter along the edge), so the sliver's other side was left alone:
    /// "inconsistent parity", CompositeFailed. The terrain part's boundary is the triangle's plus both regions', mod 2.
    /// </summary>
    public class TerrainSlopeAutoToeOnTerrainTests
    {
        // the terrain: the plane z = 5 + 0.03 (x - x0) on a 16 x 16 grid of [-40, 40]^2 (5 m cells), or falling in x
        // (mirrored: the cut part on the other side)
        static DMesh3 Terrain(double x0, Similarity s, bool mirrored = false)
        {
            double k = mirrored ? -0.03 : 0.03;
            var t = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5 + k * (x - x0));
            foreach (int v in t.VertexIndices().ToList()) t.SetVertex(v, s.Apply(t.GetVertex(v)));
            return t;
        }

        // a closed square toe of half side h at z, outside; or an open toe from (-12, -h - tilt) to (12, -h + tilt), on its
        // right (it crosses x = 0 at y = -h)
        static TerrainSlopeDesign Make(double h, double z, double degrees, double x0, bool open, Similarity s, double tilt = 0, bool mirrored = false)
        {
            var pts = open ? new List<Vector3d> { new Vector3d(-12, -h - tilt, z), new Vector3d(12, -h + tilt, z) }
                           : new List<Vector3d> { new Vector3d(-h, -h, z), new Vector3d(h, -h, z), new Vector3d(h, h, z), new Vector3d(-h, h, z) };
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(pts.Select(p => s.Apply(p)).ToList(), !open, open ? SlopeToe.Sides.Right : SlopeToe.Sides.Outside, SlopeSpec.Degrees(degrees)) },
                Terrain = Terrain(x0, s, mirrored), Tolerance = s.ApplyLength(0.05), BuildComposite = true, Direction = TerrainSlopeDesign.Directions.Auto,
                SlopeMargin = 1e-4
            };
        }

        static void AssertGood(TerrainSlopeDesign d)
        {
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            TerrainSlopeMixedTests.AssertMixed(d);
            Assert.True(d.CutVolume > 0 && d.FillVolume > 0);
            Assert.True(d.Composite.CheckValidity(true, FailMode.ReturnOnly));
            Assert.All(d.Composite.VertexIndices(), v => Assert.True(d.Composite.GetVertex(v).IsFinite));
        }

        // the repro of the g3cli tests (the square of half side 5 at z = 5 on the plane through z = 5 at x = 0): the
        // crossings are terrain vertices, the toe edges lie along terrain edges
        [Theory]
        [InlineData(30.0)]
        [InlineData(40.0)]
        public void ToeMeetingTheTerrainOnItsCrossing(double degrees)
        {
            AssertGood(Make(5, 5, degrees, 0, false, Similarity.Identity));
            // a little below or above, the crossing is in a triangle: that was fine before, and stays
            foreach (double z in new[] { 4.9, 5.1 }) AssertGood(Make(5, z, degrees, 0, false, Similarity.Identity));
        }

        // the terrain falling in x: the cut and the fill part swap sides, so along the edges of the joint triangles the
        // nodes of the two runs come in the other order (the merge takes the cut run's node first at equal parameters)
        [Theory]
        [InlineData(4.0, false)]
        [InlineData(5.0, false)]
        [InlineData(4.0, true)]
        public void Mirrored(double h, bool open)
        {
            AssertGood(Make(h, 5, 30, 0, open, Similarity.Identity, 0, true));
        }

        // the crossing on a terrain edge (x = 0, between its vertices: h 4, 3.3), on a terrain vertex with the toe along
        // terrain edges (h 5, 10), and in a triangle (the plane through z = 5 at x = 1.3); an open toe with its crossing on
        // a terrain edge (h 4) and on a terrain vertex (h 5, tilted off the grid line y = -5: an open toe along a terrain
        // edge is a separate failure, docs/design/TERRAIN.md §11)
        [Theory]
        [InlineData(4.0, 0.0, false, 0.0)]
        [InlineData(3.3, 0.0, false, 0.0)]
        [InlineData(5.0, 0.0, false, 0.0)]
        [InlineData(10.0, 0.0, false, 0.0)]
        [InlineData(5.0, 1.3, false, 0.0)]
        [InlineData(4.0, 0.0, true, 0.0)]
        [InlineData(5.0, 0.0, true, 2.0)]
        public void Variants(double h, double x0, bool open, double tilt)
        {
            AssertGood(Make(h, 5, 30, x0, open, Similarity.Identity, tilt));
        }

        static readonly Dictionary<double, TerrainSlopeDesign> references = new Dictionary<double, TerrainSlopeDesign>();
        static TerrainSlopeDesign Reference(double h)
        {
            lock (references) {
                TerrainSlopeDesign d;
                if (references.TryGetValue(h, out d) == false) {
                    d = Make(h, 5, 30, 0, false, Similarity.Identity);
                    Assert.True(d.Compute(), d.Status + " " + d.Message);
                    references[h] = d;
                }
                return d;
            }
        }

        // every frame succeeds; the 2^k frames give the identity's result scaled, bit for bit
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            foreach (double h in new[] { 4.0, 5.0 }) {
                var d = Make(h, 5, 30, 0, false, s);
                AssertGood(d);
                if (s.IsExact) {
                    var r = Reference(h);
                    Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                    Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                    Assert.Equal(r.CutVolume * s.Scale * s.Scale * s.Scale, d.CutVolume);
                    Assert.Equal(r.FillVolume * s.Scale * s.Scale * s.Scale, d.FillVolume);
                }
            }
        }

        // the 7-gon of TerrainSlopeMixedTests crossing a gentle plane, at UTM: in a terrain triangle that both runs cut, a
        // catch point of one run on an edge, moved by the composite's snap (8 mm, off the edge), has the parameter of the
        // edge's end corner. Sorted by parameter and node index it came after that corner of the other run, so the edge's
        // pieces went past it, and they crossed the region boundary ("not simple after rounding", CompositeFailed). The
        // merge keeps each run's order, its corners at the ends
        [Theory]
        [InlineData(TerrainSlopeDesign.CompositeKinds.Full)]
        [InlineData(TerrainSlopeDesign.CompositeKinds.Replacement)]
        public void ToeCrossingAtUtm_EachRunsOrderAlongTheEdge(TerrainSlopeDesign.CompositeKinds kind)
        {
            var d = TerrainSlopeMixedTests.Make(0.07, 0.123, 2.1, TerrainSlopeDesign.Directions.Auto, Similarity.ByName("utm"));
            d.CompositeKind = kind;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            if (kind == TerrainSlopeDesign.CompositeKinds.Full) TerrainSlopeMixedTests.AssertMixed(d);
            else {
                // (the replacement with the terrain triangles it does not replace: no overlap, the terrain's area)
                Assert.True(d.Replacement.TriangleCount > 0 && d.ReplacedTerrainTriangles.Count > 0);
                var whole = new DMesh3(d.Replacement);
                var replaced = new HashSet<int>(d.ReplacedTerrainTriangles);
                foreach (int t in d.Terrain.TriangleIndices()) {
                    if (replaced.Contains(t)) continue;
                    Index3i tv = d.Terrain.GetTriangle(t);
                    whole.AppendTriangle(whole.AppendVertex(d.Terrain.GetVertex(tv.a)), whole.AppendVertex(d.Terrain.GetVertex(tv.b)), whole.AppendVertex(d.Terrain.GetVertex(tv.c)));
                }
                Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(whole));
                double at = TerrainSlopeCatchLineTests.XYArea(d.Terrain), aw = TerrainSlopeCatchLineTests.XYArea(whole);
                Assert.True(Math.Abs(at - aw) <= 1e-9 * at, $"area {aw} vs terrain {at}");
            }
            Assert.True(d.CutVolume > 0 && d.FillVolume > 0);
        }

        [Fact]
        public void Reuse()
        {
            var d = Make(5, 5, 30, 0, false, Similarity.Identity);
            AssertGood(d);
            var c1 = new DMesh3(d.Composite);
            AssertGood(d);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
        }
    }
}
