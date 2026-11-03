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
    /// A replaced terrain triangle with nodes inside an edge whose neighbour has no region face (docs/design/TERRAIN.md
    /// §11): an open toe along a terrain edge that ends inside it, a lateral limit along a terrain edge, a catch point
    /// rounded onto a terrain edge. The neighbour was copied whole, so the composite had a T-junction there: a crack
    /// between two terrain triangles that the area and overlap checks do not see (TerrainSlopeCatchLineTests.TJunction
    /// now does). The neighbour is split at those nodes and replaced too.
    /// </summary>
    public class TerrainSlopeOpenToeAlongEdgeTests
    {
        // the plane z = 5 + 0.03 x on a 16 x 16 grid of [-40, 40]^2 (5 m cells); an open toe along the grid line y = -5
        // from x = x0 to x1 at z, its slope on the right (y < -5)
        internal static TerrainSlopeDesign Make(double x0, double x1, double z, TerrainSlopeDesign.Directions dir, Similarity s)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5 + 0.03 * x);
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            var pts = new List<Vector3d> { new Vector3d(x0, -5, z), new Vector3d(x1, -5, z) }.Select(p => s.Apply(p)).ToList();
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(pts, false, SlopeToe.Sides.Right, SlopeSpec.Degrees(30)) },
                Terrain = terrain, Tolerance = s.ApplyLength(0.05), BuildComposite = true, Direction = dir, SlopeMargin = 1e-4
            };
        }

        static void AssertGood(TerrainSlopeDesign d)
        {
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            if (d.Direction == TerrainSlopeDesign.Directions.Auto) TerrainSlopeMixedTests.AssertMixed(d);
            else TerrainSlopeCatchLineTests.AssertComposite(d, d.Direction == TerrainSlopeDesign.Directions.Fill);
            Assert.True(d.Composite.CheckValidity(true, FailMode.ReturnOnly));
        }

        // the toe's ends inside terrain edges (x0 -12 and -12.5, x1 12), in cut, in fill, and in a mixed run whose
        // crossing is inside the edge too (z 4.9: x = -3.3), also with the fill end (x0 -4) in the terrain triangle that
        // both runs replace at the crossing (the point inside the edge is the fill run's); the ends on terrain vertices
        // (-10, 10), where the lateral limits run along the terrain edges x = -10 and 10 and the catch points on them
        // are inside those edges
        [Theory]
        [InlineData(-12.0, 12.0, 3.0, TerrainSlopeDesign.Directions.Cut)]
        [InlineData(-12.5, 12.0, 3.0, TerrainSlopeDesign.Directions.Cut)]
        [InlineData(-12.0, 12.0, 7.0, TerrainSlopeDesign.Directions.Fill)]
        [InlineData(-12.0, 12.0, 4.9, TerrainSlopeDesign.Directions.Auto)]
        [InlineData(-4.0, 12.0, 4.9, TerrainSlopeDesign.Directions.Auto)]
        [InlineData(-10.0, 10.0, 3.0, TerrainSlopeDesign.Directions.Cut)]
        public void ToeAlongATerrainEdge_EndsInsideIt(double x0, double x1, double z, TerrainSlopeDesign.Directions dir)
        {
            var d = Make(x0, x1, z, dir, Similarity.Identity);
            AssertGood(d);
            // (the terrain triangle across the toe's line beyond its start has no region face: it is split and replaced)
            if (x0 != -10) {
                int beyond = d.Terrain.TriangleIndices().First(t => {
                    Index3i v = d.Terrain.GetTriangle(t);
                    Vector2d pa = d.Terrain.GetVertex(v.a).xy, pb = d.Terrain.GetVertex(v.b).xy, pc = d.Terrain.GetVertex(v.c).xy, x = new Vector2d(x0 - 0.25, -4.9);
                    int o1 = ExactPredicates.Orient2d(pa, pb, x), o2 = ExactPredicates.Orient2d(pb, pc, x), o3 = ExactPredicates.Orient2d(pc, pa, x);
                    return (o1 > 0 && o2 > 0 && o3 > 0) || (o1 < 0 && o2 < 0 && o3 < 0);
                });
                Assert.Contains(beyond, d.ReplacedTerrainTriangles);
            }
        }

        // the T-junction check of the tests itself: a vertex inside a boundary edge with a triangle of the edge's group,
        // or at the edge's height, is a crack; one at another height in another group (a step) is not, nor one off the
        // edge's line
        // (on a toe, onStep: a step between slope and terrain at the same height is not a crack, between two slope pieces
        // it is; terrain group 2)
        [Theory]
        [InlineData(0, 0.0, 0.0, false, true)]
        [InlineData(1, 0.0, 0.0, false, true)]
        [InlineData(1, 1e-3, 0.0, false, false)]
        [InlineData(0, 0.0, 1e-12, false, false)]
        [InlineData(2, 0.0, 0.0, true, false)]
        [InlineData(1, 0.0, 0.0, true, true)]
        public void TJunctionCheck(int lowerGroup, double dz, double dy, bool onToe, bool crack)
        {
            var m = new DMesh3(false, false, false, true);
            int a = m.AppendVertex(new Vector3d(0, 0, 1)), b = m.AppendVertex(new Vector3d(2, 0, 1)), c = m.AppendVertex(new Vector3d(1, 1, 1));
            int p = m.AppendVertex(new Vector3d(1, dy, 1 + dz)), q = m.AppendVertex(new Vector3d(1, -1, 1));
            int a2 = m.AppendVertex(new Vector3d(0, 0, 1 + dz)), b2 = m.AppendVertex(new Vector3d(2, 0, 1 + dz));
            m.AppendTriangle(a, b, c, 0);
            m.AppendTriangle(a2, q, p, lowerGroup);
            m.AppendTriangle(p, q, b2, lowerGroup);
            string r = TerrainSlopeCatchLineTests.TJunction(m, onToe ? (Func<Vector2d, bool>)(x => true) : null, 2);
            Assert.True(crack ? r != "" : r == "", r);
        }

        // the replacement and the terrain triangles it does not replace, joined by position: no T-junction there either
        [Theory]
        [InlineData(TerrainSlopeDesign.Directions.Cut, 3.0)]
        [InlineData(TerrainSlopeDesign.Directions.Auto, 4.9)]
        public void Replacement_JoinsTheTerrainWithoutAT(TerrainSlopeDesign.Directions dir, double z)
        {
            var d = Make(-12, 12, z, dir, Similarity.Identity);
            d.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            var whole = new DMesh3(false, false, false, true);
            var at = new Dictionary<Vector3d, int>();
            Func<Vector3d, int> vertex = (p) => {
                p.x += 0.0; p.y += 0.0; p.z += 0.0;
                int id;
                if (at.TryGetValue(p, out id) == false) { id = whole.AppendVertex(p); at[p] = id; }
                return id;
            };
            foreach (int t in d.Replacement.TriangleIndices()) {
                Index3i v = d.Replacement.GetTriangle(t);
                Assert.True(whole.AppendTriangle(vertex(d.Replacement.GetVertex(v.a)), vertex(d.Replacement.GetVertex(v.b)), vertex(d.Replacement.GetVertex(v.c)), d.Replacement.GetTriangleGroup(t)) >= 0);
            }
            var replaced = new HashSet<int>(d.ReplacedTerrainTriangles);
            foreach (int t in d.Terrain.TriangleIndices()) {
                if (replaced.Contains(t)) continue;
                Index3i v = d.Terrain.GetTriangle(t);
                Assert.True(whole.AppendTriangle(vertex(d.Terrain.GetVertex(v.a)), vertex(d.Terrain.GetVertex(v.b)), vertex(d.Terrain.GetVertex(v.c)), d.Pieces.Count) >= 0);
            }
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(whole));
            string tj = TerrainSlopeCatchLineTests.TJunction(whole);
            Assert.True(tj == "", tj);
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(-12.5, 12, 3, TerrainSlopeDesign.Directions.Cut, Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // every frame; the 2^k frames give the identity's result scaled, bit for bit (rotated, the toe's end is not
        // exactly on the terrain edge's line any more, so nothing is split there)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Make(-12.5, 12, 3, TerrainSlopeDesign.Directions.Cut, s);
            AssertGood(d);
            if (s.IsExact) {
                var r = reference.Value;
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                Assert.Equal(r.ReplacedTerrainTriangles, d.ReplacedTerrainTriangles);
            }
        }

        [Fact]
        public void Reuse()
        {
            var d = Make(-12, 12, 3, TerrainSlopeDesign.Directions.Cut, Similarity.Identity);
            AssertGood(d);
            var c1 = new DMesh3(d.Composite);
            var r1 = new List<int>(d.ReplacedTerrainTriangles);
            AssertGood(d);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
            Assert.Equal(r1, d.ReplacedTerrainTriangles);
        }
    }
}
