// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> TargetHeights: the level lines of the slope (TERRAIN.md 10, TERRAIN_1C_PLAN.md section 10) </summary>
    public class TerrainSlopeTargetHeightTests
    {
        [Fact]
        public void LevelLines_OfSimpleMeshes()
        {
            // a pyramid over the square (-1, 1)^2, apex at (0, 0, 1): a closed square at each height in (0, 1)
            var m = new DMesh3();
            int c = m.AppendVertex(new Vector3d(0, 0, 1));
            var corners = new[] { new Vector3d(-1, -1, 0), new Vector3d(1, -1, 0), new Vector3d(1, 1, 0), new Vector3d(-1, 1, 0) }.Select(p => m.AppendVertex(p)).ToArray();
            for (int i = 0; i < 4; ++i) m.AppendTriangle(corners[i], corners[(i + 1) % 4], c);
            var l = TargetHeightLine.LevelLines(m, 0.5);
            Assert.Single(l);
            Assert.True(l[0].Closed);
            Assert.Equal(4, l[0].Points.Count);
            Assert.All(l[0].Points, p => { Assert.Equal(0.5, p.z); Assert.Equal(0.5, Math.Max(Math.Abs(p.x), Math.Abs(p.y)), 12); });
            // the higher side on the left: counter-clockwise around the apex
            double area = 0;
            for (int i = 0; i < 4; ++i) area += l[0].Points[i].x * l[0].Points[(i + 1) % 4].y - l[0].Points[(i + 1) % 4].x * l[0].Points[i].y;
            Assert.True(area > 0);
            // below and above: nothing; at the base height (the corners count as above): nothing either
            Assert.Empty(TargetHeightLine.LevelLines(m, -0.1));
            Assert.Empty(TargetHeightLine.LevelLines(m, 1.1));
            Assert.Empty(TargetHeightLine.LevelLines(m, 0));
            Assert.Empty(TargetHeightLine.LevelLines(m, double.NaN));
            // an inclined plane (a strip of two triangles): one open line across it
            var s = new DMesh3();
            int a0 = s.AppendVertex(new Vector3d(0, 0, 0)), a1 = s.AppendVertex(new Vector3d(4, 0, 4)), a2 = s.AppendVertex(new Vector3d(4, 2, 4)), a3 = s.AppendVertex(new Vector3d(0, 2, 0));
            s.AppendTriangle(a0, a1, a2); s.AppendTriangle(a0, a2, a3);
            var o = TargetHeightLine.LevelLines(s, 1);
            Assert.Single(o);
            Assert.False(o[0].Closed);
            Assert.All(o[0].Points, p => Assert.Equal(1, p.x, 12));
            // (rising to +x: the higher side, +x, on the left of the line, so it runs towards -y)
            Assert.True(o[0].Points.First().y > o[0].Points.Last().y);
            // a vertex exactly at h (at x = 1 on both edges of the strip's middle): the vertex itself, once
            var w = new DMesh3();
            int b0 = w.AppendVertex(new Vector3d(0, 0, 0)), b1 = w.AppendVertex(new Vector3d(0.1, 1, 0.3)), b2 = w.AppendVertex(new Vector3d(2, 0, 2)),
                b3 = w.AppendVertex(new Vector3d(2, 2, 2)), b4 = w.AppendVertex(new Vector3d(0, 2, 0));
            w.AppendTriangle(b0, b2, b1); w.AppendTriangle(b2, b3, b1); w.AppendTriangle(b3, b4, b1); w.AppendTriangle(b4, b0, b1);
            var at = TargetHeightLine.LevelLines(w, 0.3);
            Assert.Single(at);
            Assert.Contains(new Vector3d(0.1, 1, 0.3), at[0].Points);
            for (int i = 0; i + 1 < at[0].Points.Count; ++i) Assert.NotEqual(at[0].Points[i], at[0].Points[i + 1]);
        }

        static void AssertOnTheSlope(TerrainSlopeDesign d, DMesh3 surface)
        {
            foreach (var line in d.TargetHeightLines) {
                Assert.True(line.Points.Count >= 2);
                foreach (var p in line.Points) {
                    Assert.Equal(line.Height, p.z);
                    double z = TerrainSlopeDesignTests.HeightAt(surface, p.xy);
                    Assert.True(Math.Abs(z - line.Height) <= 1e-9 * (1 + Math.Abs(line.Height)), $"{p}: surface {z}");
                }
            }
        }

        // a square pit (half 10) 1:1 in flat terrain at 5: the level line at h is the toe offset by h (the cone envelope,
        // within Tolerance), closed; above the catch line none
        [Fact]
        public void Pit_LevelLinesAreOffsetsOfTheToe()
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5);
            var d = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(TerrainSlopeDesignTests.Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) },
                Terrain = terrain, Tolerance = 0.05, BuildComposite = true, TargetHeights = new List<double> { 1, 2.5, 4, 6 }
            };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(3, d.TargetHeightLines.Count);
            Assert.Equal(new[] { 1.0, 2.5, 4.0 }, d.TargetHeightLines.Select(l => l.Height).ToArray());
            Assert.All(d.TargetHeightLines, l => Assert.True(l.Closed));
            AssertOnTheSlope(d, d.SlopeRegion);
            var toe = TerrainSlopeDesignTests.Square(10, 0);
            foreach (var line in d.TargetHeightLines)
                foreach (var p in line.Points) {
                    double dist = Enumerable.Range(0, 4).Min(i => new Segment2d(toe[i].xy, toe[(i + 1) % 4].xy).DistanceSquared(p.xy));
                    dist = Math.Sqrt(dist);
                    Assert.True(Math.Abs(dist - line.Height) <= 0.05 + 1e-9, $"{p}: {dist} from the toe");
                }
            // reuse: the same lines
            var first = d.TargetHeightLines.Select(l => l.Points.ToList()).ToList();
            Assert.True(d.Compute());
            for (int i = 0; i < first.Count; ++i) Assert.Equal(first[i], d.TargetHeightLines[i].Points);
        }

        // without a terrain (of SlopeSurface), an open toe: the lines end at the lateral limits
        [Fact]
        public void OpenToe_WithoutTerrain_LinesEndAtTheLimits()
        {
            var pts = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(20, 0, 0), new Vector3d(30, 10, 0) };
            var d = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(pts, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1.5)) },
                HeightLimit = 6, Tolerance = 0.05, TargetHeights = new List<double> { 3 }
            };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Single(d.TargetHeightLines);
            Assert.False(d.TargetHeightLines[0].Closed);
            AssertOnTheSlope(d, d.SlopeSurface);
        }

        // mixed cut and fill: the lines of both parts of the region
        [Fact]
        public void Mixed_LinesOfCutAndFill()
        {
            var d = TerrainSlopeMixedTests.Make(0.15, 0.3, 0.37, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            d.TargetHeights = new List<double> { 1.5, -1.5 };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Contains(d.TargetHeightLines, l => l.Height == 1.5);
            Assert.Contains(d.TargetHeightLines, l => l.Height == -1.5);
            AssertOnTheSlope(d, d.SlopeRegion);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            // (17 x 15 cells: the toe's corners are no grid nodes. With them on grid nodes the composite of this frame
            // overlaps, on master too: a near-coincidence of a toe corner and a terrain vertex; noted in the plan)
            Func<Similarity, TerrainSlopeDesign> make = (f) => {
                var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 17, 15, (x, y) => 5);
                foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, f.Apply(terrain.GetVertex(v)));
                var toe = TerrainSlopeDesignTests.Square(10, 0).Select(p => f.Apply(p)).ToList();
                return new TerrainSlopeDesign() {
                    Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) }, Terrain = terrain,
                    // (SlopeMargin 1e-4: see TerrainSlopeMixedTests.MakeForFrames; with the default the toe is below
                    // MinFeatureSize at scale 1e-3 and UTM, and the composite fails there on master too)
                    Tolerance = f.ApplyLength(0.05), BuildComposite = true, SlopeMargin = 1e-4,
                    TargetHeights = new List<double> { f.Apply(new Vector3d(0, 0, 2.5)).z }
                };
            };
            var r = make(Similarity.Identity);
            Assert.True(r.Compute(), r.Message);
            var d = make(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(r.TargetHeightLines.Count, d.TargetHeightLines.Count);
            AssertOnTheSlope(d, d.SlopeRegion);
            if (s.IsExact)
                for (int i = 0; i < r.TargetHeightLines.Count; ++i)
                    Assert.Equal(r.TargetHeightLines[i].Points, d.TargetHeightLines[i].Points.Select(p => s.Rotation.Transpose() * ((p - s.Translation) / s.Scale)).ToList());
        }
    }
}
