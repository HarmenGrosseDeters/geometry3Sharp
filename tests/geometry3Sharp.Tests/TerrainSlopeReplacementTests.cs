using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TerrainSlopeDesign.CompositeKinds.Replacement: only the part of the composite that replaces terrain triangles,
    /// with the replaced triangles and the terrain vertex of each of its vertices. With the other terrain triangles it is
    /// exactly the Full composite (the same triangles and vertices, as sets); invariance over Similarity.All(); reuse.
    /// </summary>
    public class TerrainSlopeReplacementTests
    {
        // a triangle by its corner positions, counter-clockwise in XY, starting at the smallest corner
        static string key(Vector3d a, Vector3d b, Vector3d c)
        {
            if (ExactPredicates.Orient2d(a.xy, b.xy, c.xy) < 0) { Vector3d x = b; b = c; c = x; }
            var v = new[] { a, b, c };
            int m = 0;
            for (int i = 1; i < 3; ++i) if (less(v[i], v[m])) m = i;
            return string.Join(";", Enumerable.Range(0, 3).Select(i => pos(v[(m + i) % 3])));
        }

        static bool less(Vector3d p, Vector3d q) => p.x != q.x ? p.x < q.x : p.y != q.y ? p.y < q.y : p.z < q.z;
        // (-0 as +0, as the composite takes the terrain's vertices where it cuts them)
        static string pos(Vector3d p) => $"{p.x + 0.0:R},{p.y + 0.0:R},{p.z + 0.0:R}";

        static List<string> triangles(DMesh3 m) => m.TriangleIndices().Select(t => {
            Index3i tv = m.GetTriangle(t);
            return key(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
        }).OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary> Replacement plus the terrain triangles not replaced, joined by CompositeTerrainVertices, is Full </summary>
        internal static void AssertReplacementIsFull(TerrainSlopeDesign full, TerrainSlopeDesign repl)
        {
            Assert.Equal(full.Status, repl.Status);
            Assert.Null(repl.Composite);
            if (full.Composite == null) { Assert.Null(repl.Replacement); return; }
            DMesh3 terrain = full.Terrain, r = repl.Replacement;
            Assert.NotNull(r);
            Assert.Equal(full.ReplacedTerrainTriangles, repl.ReplacedTerrainTriangles);
            Assert.Equal(full.ReplacedTerrainTriangles.OrderBy(x => x).Distinct(), full.ReplacedTerrainTriangles);
            Assert.Equal("", Invariance.Difference(full.SlopeRegion, repl.SlopeRegion));
            var replaced = new HashSet<int>(repl.ReplacedTerrainTriangles);
            var kept = terrain.TriangleIndices().Where(t => replaced.Contains(t) == false).ToList();
            // the same triangles
            var joined = triangles(r);
            foreach (int t in kept) {
                Index3i tv = terrain.GetTriangle(t);
                joined.Add(key(terrain.GetVertex(tv.a), terrain.GetVertex(tv.b), terrain.GetVertex(tv.c)));
            }
            joined.Sort(StringComparer.Ordinal);
            Assert.Equal(triangles(full.Composite), joined);
            // the same vertices
            var keptVerts = new HashSet<int>(kept.SelectMany(t => { Index3i tv = terrain.GetTriangle(t); return new[] { tv.a, tv.b, tv.c }; }));
            var fullPos = full.Composite.Vertices().Select(pos).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var joinedPos = r.Vertices().Select(pos).Concat(keptVerts.Select(v => pos(terrain.GetVertex(v)))).Distinct()
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(fullPos, joinedPos);
            // the join: a vertex of the replacement names the terrain vertex at its position, and every kept terrain
            // vertex at a replacement vertex is named (so joining by the map gives the full composite's sharing)
            Assert.NotNull(repl.CompositeTerrainVertices);
            Assert.Equal(r.MaxVertexID, repl.CompositeTerrainVertices.Length);
            var named = new HashSet<int>();
            var replPos = new Dictionary<string, int>();
            foreach (int v in r.VertexIndices()) {
                replPos[pos(r.GetVertex(v))] = v;
                int tv = repl.CompositeTerrainVertices[v];
                if (tv < 0) continue;
                Assert.Equal(pos(terrain.GetVertex(tv)), pos(r.GetVertex(v)));
                named.Add(tv);
            }
            foreach (int v in keptVerts)
                if (replPos.ContainsKey(pos(terrain.GetVertex(v))))
                    Assert.Contains(v, named);
            // and the full composite names its terrain vertices too
            foreach (int v in full.Composite.VertexIndices()) {
                int tv = full.CompositeTerrainVertices[v];
                if (tv >= 0) Assert.Equal(pos(terrain.GetVertex(tv)), pos(full.Composite.GetVertex(v)));
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void RandomTerrains_ReplacementPlusTerrainIsFull(int seed, bool utm)
        {
            int checkedCases = 0;
            for (int it = 0; it < 6; ++it) {
                var full = TerrainSlopeCatchLineTests.RandomTerrainCase(seed, utm, it);
                var repl = TerrainSlopeCatchLineTests.RandomTerrainCase(seed, utm, it);
                repl.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
                full.Compute(); repl.Compute();
                AssertReplacementIsFull(full, repl);
                if (full.Composite != null) {
                    checkedCases++;
                    // much smaller than the terrain: only the cut triangles are replaced
                    Assert.True(repl.Replacement.TriangleCount < full.Composite.TriangleCount);
                    Assert.True(repl.ReplacedTerrainTriangles.Count > 0);
                }
            }
            Assert.True(checkedCases > 0);
        }

        [Fact]
        public void Fill_ReplacementPlusTerrainIsFull()
        {
            Func<TerrainSlopeDesign> make = () => {
                var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 20, 20, (x, y) => -6 + 0.05 * x, flip: true);
                var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(3));
                toe.FillStart = toe.CutStart; toe.CutStart = null;
                return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.05, Direction = TerrainSlopeDesign.Directions.Fill, BuildComposite = true };
            };
            var full = make(); var repl = make();
            repl.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
            Assert.True(full.Compute(), full.Message);
            Assert.True(repl.Compute(), repl.Message);
            AssertReplacementIsFull(full, repl);
        }

        [Fact]
        public void Replacement_IsReusable()
        {
            var d = TerrainSlopeCatchLineTests.RandomTerrainCase(2, true, 0);
            d.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
            d.Compute();
            Assert.NotNull(d.Replacement);
            DMesh3 first = d.Replacement;
            var replaced = d.ReplacedTerrainTriangles.ToList();
            var map = d.CompositeTerrainVertices.ToArray();
            d.Compute();
            Assert.Equal("", Invariance.Difference(first, d.Replacement));
            Assert.Equal(replaced, d.ReplacedTerrainTriangles);
            Assert.Equal(map, d.CompositeTerrainVertices);
            // and back to Full on the same instance: no replacement left over
            d.CompositeKind = TerrainSlopeDesign.CompositeKinds.Full;
            d.Compute();
            Assert.Null(d.Replacement);
            Assert.NotNull(d.Composite);
        }

        [Fact]
        public void TerrainTriangleAroundThePit_IsNotWhollyReplaced()
        {
            // two terrain triangles over 30 x 30 m at z 30, above the cap at 20: the slope surface covers both and lies
            // below the terrain everywhere, so every overlay face is in the region and every terrain corner is on the
            // slope surface. But the pit (the toe, a hole of the slope surface) lies inside them: they must not be skipped
            // as wholly replaced, the pit floor stays terrain (the area check of AssertComposite)
            var terrain = new DMesh3(false, false, false, true);
            int a = terrain.AppendVertex(new Vector3d(-15, -15, 30)), b = terrain.AppendVertex(new Vector3d(15, -15, 30));
            int c = terrain.AppendVertex(new Vector3d(15, 15, 30)), d = terrain.AppendVertex(new Vector3d(-15, 15, 30));
            terrain.AppendTriangle(a, b, c); terrain.AppendTriangle(a, c, d);
            foreach (var kind in new[] { TerrainSlopeDesign.CompositeKinds.Full, TerrainSlopeDesign.CompositeKinds.Replacement }) {
                var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
                var dsg = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, HeightLimit = 20, Tolerance = 0.05, BuildComposite = true, CompositeKind = kind };
                dsg.Compute();
                Assert.True(dsg.Status == TerrainSlopeDesign.Statuses.NoDaylight || dsg.Status == TerrainSlopeDesign.Statuses.Success, dsg.Status + " " + dsg.Message);
                if (kind == TerrainSlopeDesign.CompositeKinds.Full) {
                    TerrainSlopeCatchLineTests.AssertComposite(dsg);
                } else {
                    // both terrain triangles are replaced, and the replacement holds the pit floor (terrain group)
                    Assert.Equal(new List<int> { 0, 1 }, dsg.ReplacedTerrainTriangles);
                    double floor = dsg.Replacement.TriangleIndices().Where(t => dsg.Replacement.GetTriangleGroup(t) == dsg.Pieces.Count)
                        .Sum(t => dsg.Replacement.GetTriArea(t));
                    Assert.True(floor > 0.99 * 16 * 16, $"pit floor {floor}");
                }
            }
        }

        static Similarity ZUp(Similarity s) =>
            s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;

        static TerrainSlopeDesign Make(Similarity s, TerrainSlopeDesign.CompositeKinds kind)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 25, 23,
                (x, y) => 9 + 1.5 * Math.Sin(0.13 * x) * Math.Cos(0.09 * y));
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            var toe = SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(5, 9, 2, Vector2d.Zero, 0.2).Select(p => s.Apply(p)).ToList(),
                true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5));
            return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = s.ApplyLength(0.02), BuildComposite = true, CompositeKind = kind };
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity, TerrainSlopeDesign.CompositeKinds.Replacement);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            // in every frame the replacement plus the terrain is the full composite; for 2^k scales the replacement,
            // the replaced triangles and the vertex map are those of the identity frame, bit for bit
            Similarity s = ZUp(Similarity.ByName(name));
            var full = Make(s, TerrainSlopeDesign.CompositeKinds.Full);
            var repl = Make(s, TerrainSlopeDesign.CompositeKinds.Replacement);
            Assert.True(full.Compute(), full.Status + " " + full.Message);
            Assert.True(repl.Compute(), repl.Status + " " + repl.Message);
            AssertReplacementIsFull(full, repl);
            if (s.IsExact) {
                var r = reference.Value;
                Assert.Equal("", Invariance.Difference(r.Replacement, Invariance.MappedBack(repl.Replacement, s)));
                Assert.Equal(r.ReplacedTerrainTriangles, repl.ReplacedTerrainTriangles);
                Assert.Equal(r.CompositeTerrainVertices, repl.CompositeTerrainVertices);
            }
        }
    }
}
