using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// Triangle's .node / .poly / .ele files (TrianglePolyFormat) in mesh2d: the domain of a .poly as Triangle -p gives it (closed
    /// loops of segments bound it, hole points remove holes), regions, heights, output files read back, and malformed files as
    /// read_error, never internal_error. Inputs are written here.
    /// </summary>
    public class TrianglePolyCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public TrianglePolyCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertOk(int exit, JsonObject r)
        {
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal("ok", S(r["code"]));
        }

        // an L-shape (area 3, its convex hull 3.5) with a 0.5 x 0.5 hole, numbered from 1, with comments, markers and a region
        const string LShape = @"# an L-shape with a square hole
10 2 0 1
1 0 0
2 2 0
3 2 1
4 1 1
5 1 2
6 0 2
7 0.25 0.25
8 0.75 0.25   # hole corners
9 0.75 0.75
10 0.25 0.75

10 1
1 1 2 5
2 2 3 5
3 3 4 0
4 4 5 0
5 5 6 0
6 6 1 0
7 7 8 9
8 8 9 9
9 9 10 9
10 10 7 9
1
1 0.5 0.5
1
1 1.5 0.5 3 0.01
";

        string Write(string name, string text)
        {
            string path = m.Path_(name);
            File.WriteAllText(path, text);
            return path;
        }

        static double Area(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).y;

        [Fact]
        public void Poly_LShapeWithHole_DomainAsTriangleCarvesIt()
        {
            string poly = Write("l.poly", LShape);
            var (exit, r) = Run("mesh2d", poly, m.Path_("l.obj"), "--max-area", "0.1", "--min-angle", "25");
            AssertOk(exit, r);
            Assert.Equal(2, I(r["rings_from_segments"]));
            // the L minus the hole, not the convex hull: the concavity is carved as with Triangle -p
            Assert.Equal(2.75, D(r["area"]), 12);
            Assert.True(D(r["smallest_angle"]) >= 25 - 1e-9);
            DMesh3 mesh = StandardMeshReader.ReadMesh(m.Path_("l.obj"));
            Assert.Equal(2.75, Area(mesh), 12);
            Assert.Equal(2, new MeshBoundaryLoops(mesh).Count);
            foreach (int t in mesh.TriangleIndices()) {
                Vector3d c = mesh.GetTriCentroid(t);
                Assert.False(c.x > 1 && c.y > 1, "triangle in the concavity");
                Assert.False(c.x > 0.25 && c.x < 0.75 && c.y > 0.25 && c.y < 0.75, "triangle in the hole");
            }
            // the region (label 3, area 0.01) covers the whole connected domain
            Assert.Equal(I(r["triangles"]), I(r["labels"]["3"]));
            Assert.True(D(r["largest_area"]) <= 0.01);
        }

        static string Poly(double[] xy, int[][] segments, double[] holes = null)
        {
            var sb = new StringBuilder();
            sb.Append(xy.Length / 2).Append(" 2 0 0\n");
            for (int i = 0; i < xy.Length / 2; ++i) sb.Append(i + 1).Append(' ').Append(xy[2 * i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(xy[2 * i + 1].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(segments.Length).Append(" 0\n");
            for (int i = 0; i < segments.Length; ++i) sb.Append(i + 1).Append(' ').Append(segments[i][0]).Append(' ').Append(segments[i][1]).Append('\n');
            int h = holes == null ? 0 : holes.Length / 2;
            sb.Append(h).Append('\n');
            for (int i = 0; i < h; ++i) sb.Append(i + 1).Append(' ').Append(holes[2 * i]).Append(' ').Append(holes[2 * i + 1]).Append('\n');
            return sb.ToString();
        }

        static readonly double[] Outer4 = { 0, 0, 4, 0, 4, 4, 0, 4 }, Inner2 = { 1, 1, 3, 1, 3, 3, 1, 3 };
        static readonly int[][] TwoSquares = { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 8 }, new[] { 8, 5 } };

        // the area Triangle -p leaves: what segments enclose, minus what hole points reach (by its file format description)
        public static IEnumerable<object[]> Domains => new List<object[]> {
            // a loop inside a loop: the inner region stays (it is enclosed), a constraint only
            new object[] { "nested", Poly(Outer4.Concat(Inner2).ToArray(), TwoSquares), 16.0, 2 },
            // the same with a hole point inside the inner loop: removed
            new object[] { "nested-hole", Poly(Outer4.Concat(Inner2).ToArray(), TwoSquares, new double[] { 2, 2 }), 12.0, 2 },
            // an island in the hole: the hole point is stopped by the island's segments
            new object[] { "island", Poly(Outer4.Concat(new double[] { 0.5, 0.5, 3.5, 0.5, 3.5, 3.5, 0.5, 3.5 }).Concat(Inner2).ToArray(),
                TwoSquares.Concat(new[] { new[] { 9, 10 }, new[] { 10, 11 }, new[] { 11, 12 }, new[] { 12, 9 } }).ToArray(), new double[] { 0.75, 0.75 }), 16.0 - 9 + 4, 3 },
            // two squares sharing a corner (a vertex with four segments): both stay, the concavities go
            new object[] { "figure-eight", Poly(new double[] { 0, 0, 1, 0, 1, 1, 0, 1, 2, 1, 2, 2, 1, 2 },
                new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 }, new[] { 3, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 3 } }), 2.0, 1 },
            // two squares joined by a bridge: the bridge encloses nothing
            new object[] { "bridge", Poly(new double[] { 0, 0, 1, 0, 1, 1, 0, 1, 3, 0, 4, 0, 4, 1, 3, 1 }, TwoSquares.Concat(new[] { new[] { 2, 5 } }).ToArray()), 2.0, 2 },
            // a square with a chord and a dangling chain outside: the square, both halves
            new object[] { "chord-dangle", Poly(new double[] { 0, 0, 1, 0, 1, 1, 0, 1, 2, 2, 3, 0.5 },
                new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 }, new[] { 1, 3 }, new[] { 3, 5 }, new[] { 5, 6 } }), 1.0, 1 },
            // an L-shape split by a chord (the usual way to give two regional attributes): the L, not its hull
            new object[] { "l-chord", Poly(new double[] { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2 },
                new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 1 }, new[] { 1, 4 } }), 3.0, 1 },
            // a repeated segment and a duplicate point at the same position are one
            new object[] { "duplicates", Poly(new double[] { 0, 0, 2, 0, 2, 2, 0, 2, 2, 0 },
                new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 }, new[] { 2, 1 }, new[] { 5, 3 } }), 4.0, 1 },
        };

        [Theory]
        [MemberData(nameof(Domains))]
        public void Poly_Domain_IsWhatTriangleMinusPLeaves(string name, string poly, double area, int rings)
        {
            string path = Write("dom_" + name + ".poly", poly);
            var (exit, r) = Run("mesh2d", path, m.Path_("dom_" + name + ".obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(rings, I(r["rings_from_segments"]));
            Assert.Equal(area, D(r["area"]), 12);
            Assert.Equal(area, Area(StandardMeshReader.ReadMesh(m.Path_("dom_" + name + ".obj"))), 12);
            Assert.Empty(r["warnings"].AsArray());
        }

        // ---- the ring construction is exact: invariant under 2^k scales and exactly representable offsets ----

        static readonly double[] LShape6 = { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2 };
        static readonly int[][] LLoop = { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 1 } };

        static string Transformed(double[] xy, double scale, double dx, double dy, int[][] segments, double[] holes = null)
        {
            var t = new double[xy.Length];
            for (int i = 0; i < xy.Length; i += 2) { t[i] = xy[i] * scale + dx; t[i + 1] = xy[i + 1] * scale + dy; }
            return Poly(t, segments, holes?.Select((v, i) => v * scale + (i % 2 == 0 ? dx : dy)).ToArray());
        }

        public static IEnumerable<object[]> Transforms => new[] {
            new object[] { 1.0, 0.0, 0.0 }, new object[] { Math.ScaleB(1, -560), 0.0, 0.0 }, new object[] { Math.ScaleB(1, -20), 0.0, 0.0 },
            new object[] { Math.ScaleB(1, 20), 0.0, 0.0 }, new object[] { Math.ScaleB(1, 500), 0.0, 0.0 },
            new object[] { 1.0, 1e9, 1e9 }, new object[] { 1.0, 632117.0, 5712043.0 }, new object[] { 0.125, 632117.25, 5712043.5 },
        };

        [Theory]
        [MemberData(nameof(Transforms))]
        public void Rings_AreTheSameUnderScaleAndOffset(double scale, double dx, double dy)
        {
            // the L, a figure eight and nested loops with a chord: the same rings (as vertex sequences) whatever the scale or offset
            var shapes = new[] {
                (LShape6, LLoop.Concat(new[] { new[] { 1, 4 } }).ToArray()),
                (new double[] { 0, 0, 1, 0, 1, 1, 0, 1, 2, 1, 2, 2, 1, 2 }, new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 }, new[] { 3, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 3 } }),
                (Outer4.Concat(Inner2).ToArray(), TwoSquares),
            };
            foreach (var (xy, segs) in shapes) {
                var reference = TrianglePolyFormat.ReadPoly(Poly(xy, segs), null, false, out var refInfo);
                var moved = TrianglePolyFormat.ReadPoly(Transformed(xy, scale, dx, dy, segs), null, false, out var info);
                Assert.True(refInfo.Loops > 0);
                Assert.Equal(refInfo.Loops, info.Loops);
                Assert.Equal(reference.Rings.Select(r => string.Join(",", r.Vertices)), moved.Rings.Select(r => string.Join(",", r.Vertices)));
                Assert.Empty(info.Notes);
            }
            // meshed (where the area is representable): the L's area, scaled
            if (scale >= Math.ScaleB(1, -20) && scale <= Math.ScaleB(1, 20)) {
                string path = Write("inv_" + scale + "_" + dx + ".poly", Transformed(LShape6, scale, dx, dy, LLoop));
                var (exit, r) = Run("mesh2d", path, m.Path_("inv.off"), "--quality", "false");
                AssertOk(exit, r);
                Assert.Equal(1, I(r["rings_from_segments"]));
                Assert.Equal(3 * scale * scale, D(r["area"]), 3 * scale * scale * 1e-9);
            }
        }

        [Fact]
        public void Rings_DirectionsThatTieInFloatingPoint_AreOrderedExactly()
        {
            // from (0, 0), the directions to (1e8, 1e8 - 1) and (1e8 + 1, 1e8) differ but have the same atan2: a quad with a chord
            // to the first, and a separate square; both enclose their regions
            string poly = Poly(new double[] { 0, 0, 1e8, -1e8, 1e8, 1e8 - 1, 1e8 + 1, 1e8, -10, -10, -5, -10, -5, -5, -10, -5 },
                new[] { new[] { 1, 2 }, new[] { 2, 4 }, new[] { 4, 3 }, new[] { 3, 1 }, new[] { 1, 3 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 8 }, new[] { 8, 5 } });
            TrianglePolyFormat.ReadPoly(poly, null, false, out var info);
            Assert.Equal(2, info.Loops);
            Assert.Empty(info.Notes);
        }

        [Fact]
        public void Rings_APinwheelAroundOneVertex()
        {
            // regions in all four quadrants around the origin (a square, a triangle, two squares; five segments leave the origin,
            // in both half-planes, so the angular order needs both): one outer ring, area 1 + 0.5 + 1 + 1
            string poly = Poly(new double[] { 0, 0, 1, 0, 1, 1, 0, 1, -1, 1, -1, 0, -1, -1, 0, -1, 1, -1 },
                new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 }, new[] { 1, 5 }, new[] { 5, 6 }, new[] { 6, 1 },
                        new[] { 6, 7 }, new[] { 7, 8 }, new[] { 8, 1 }, new[] { 8, 9 }, new[] { 9, 2 } });
            string path = Write("pinwheel.poly", poly);
            var (exit, r) = Run("mesh2d", path, m.Path_("pinwheel.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(1, I(r["rings_from_segments"]));
            Assert.Equal(3.5, D(r["area"]), 12);
        }

        [Fact]
        public void CompareAround_IsTheCounterclockwiseOrderFromPlusX()
        {
            // sixteen directions around a point far from the origin, in every order: sorted, they run counterclockwise from +x,
            // also where atan2 ties (directions that differ by one unit at 1e8)
            var p = new Vector2d(632117.25, 5712043.5);
            var dirs = new[] { (1.0, 0.0), (2, 1), (1, 1), (1, 2), (0, 1), (-1, 2), (-1, 1), (-2, 1), (-1, 0), (-2, -1), (-1, -1), (-1, -2), (0, -1), (1, -2), (1, -1), (2, -1) }
                .Select(d => new Vector2d(p.x + d.Item1, p.y + d.Item2)).ToList();
            var rnd = new Random(3);
            for (int k = 0; k < 50; ++k) {
                var shuffled = dirs.OrderBy(_ => rnd.Next()).ToList();
                shuffled.Sort((a, b) => TrianglePolyFormat.CompareAround(p, a, b));
                Assert.Equal(dirs, shuffled);
            }
            var o = new Vector2d(0, 0);
            Assert.True(TrianglePolyFormat.CompareAround(o, new Vector2d(1e8, 1e8 - 1), new Vector2d(1e8 + 1, 1e8)) < 0);
            Assert.Equal(0, TrianglePolyFormat.CompareAround(o, new Vector2d(2, 2), new Vector2d(3, 3)));
        }

        [Fact]
        public void Rings_ThreeTrianglesAroundOneVertex()
        {
            // three triangles meeting only at the origin, about 120 degrees apart: the six directions there span the full turn,
            // where an orientation test alone gives no order; one ring, the sum of the areas (2 x area: 60 + 61 + 59)
            string poly = Poly(new double[] { 0, 0, 10, 0, 8, 6, -5, 9, -9, 4, -5, -9, 1, -10 },
                new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 1 }, new[] { 1, 4 }, new[] { 4, 5 }, new[] { 5, 1 }, new[] { 1, 6 }, new[] { 6, 7 }, new[] { 7, 1 } });
            string path = Write("three.poly", poly);
            var (exit, r) = Run("mesh2d", path, m.Path_("three.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(1, I(r["rings_from_segments"]));
            Assert.Equal((60 + 61 + 59) / 2.0, D(r["area"]), 12);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Rings_TJunctions_InEveryOrder(bool pointFirst, bool chordFirst)
        {
            // a chord of a square ending on its bottom edge at (1, 0): the T point numbered first or last, the chord listed first
            // or last, so each end of the exact test meets it; always a T-junction (constraints only, with the note)
            var xy = new List<double> { 0, 0, 2, 0, 2, 2, 0, 2, 1, 1 };
            if (pointFirst) xy.InsertRange(0, new double[] { 1, 0 }); else xy.AddRange(new double[] { 1, 0 });
            int o = pointFirst ? 1 : 0, t = pointFirst ? 1 : 6;
            var loop = new List<int[]> { new[] { 1 + o, 2 + o }, new[] { 2 + o, 3 + o }, new[] { 3 + o, 4 + o }, new[] { 4 + o, 1 + o } };
            var chord = new[] { t, 5 + o };
            if (chordFirst) loop.Insert(0, chord); else loop.Add(chord);
            TrianglePolyFormat.ReadPoly(Poly(xy.ToArray(), loop.ToArray()), null, false, out var info);
            Assert.Equal(0, info.Loops);
            Assert.Contains(info.Notes, n => n.Contains("cross or touch"));
        }

        [Fact]
        public void Rings_AVertexOfHighDegree()
        {
            // a wheel: 2000 spokes from the hub to a rim of 2000 points (on an integer polygon): one ring, the rim
            int n = 2000;
            var xy = new List<double> { 0, 0 };
            var segs = new List<int[]>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                xy.Add(Math.Round(1e6 * Math.Cos(a))); xy.Add(Math.Round(1e6 * Math.Sin(a)));
                segs.Add(new[] { 1, i + 2 });
                segs.Add(new[] { i + 2, (i + 1) % n + 2 });
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var input = TrianglePolyFormat.ReadPoly(Poly(xy.ToArray(), segs.ToArray()), null, false, out var info);
            Assert.Equal(1, info.Loops);
            Assert.Equal(n, input.Rings[0].Vertices.Length);
            Assert.DoesNotContain(0, input.Rings[0].Vertices);
            Assert.True(sw.Elapsed.TotalSeconds < 20, "reading took " + sw.Elapsed);
        }

        [Fact]
        public void Poly_CrossingLoop_FallsBackToTheHullWithANote()
        {
            // a bow tie: its segments cross, so it is no plane graph here (Triangle would split it at the crossing): the convex hull
            string path = Write("bowtie.poly", Poly(new double[] { 0, 0, 1, 1, 1, 0, 0, 1 }, new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 1 } }));
            var (exit, r) = Run("mesh2d", path, m.Path_("bowtie.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(0, I(r["rings_from_segments"]));
            Assert.Equal(1, D(r["area"]), 12);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("cross or touch"));
            // a T-junction (an end point on another segment) and an overlap count as such too; on an L the fallback to the hull
            // (3.5, the documented limit) is told apart from what -p gives (3)
            path = Write("tee.poly", Poly(LShape6.Concat(new double[] { 1, 0 }).ToArray(), LLoop.Concat(new[] { new[] { 7, 4 } }).ToArray()));
            (exit, r) = Run("mesh2d", path, m.Path_("tee.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("cross or touch"));
            Assert.Equal(0, I(r["rings_from_segments"]));
            Assert.Equal(3.5, D(r["area"]), 12);
            path = Write("overlap.poly", Poly(LShape6.Concat(new double[] { 1, 0 }).ToArray(), LLoop.Concat(new[] { new[] { 1, 7 } }).ToArray()));
            (exit, r) = Run("mesh2d", path, m.Path_("overlap.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("cross or touch"));
            Assert.Equal(3.5, D(r["area"]), 12);
            // a separate valid L next to the bad group: the L is meshed, the bad group's region is lost (documented)
            path = Write("mixed.poly", Poly(LShape6.Concat(new double[] { 1, 0, 10, 0, 12, 0, 12, 1, 11, 1, 11, 2, 10, 2 }).ToArray(),
                LLoop.Concat(new[] { new[] { 7, 4 }, new[] { 8, 9 }, new[] { 9, 10 }, new[] { 10, 11 }, new[] { 11, 12 }, new[] { 12, 13 }, new[] { 13, 8 } }).ToArray()));
            (exit, r) = Run("mesh2d", path, m.Path_("mixed.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(1, I(r["rings_from_segments"]));
            Assert.Equal(3, D(r["area"]), 12);
        }

        [Fact]
        public void TriangleOutput_ReadsBackAsTheSameMesh()
        {
            string poly = Write("l2.poly", LShape);
            var (exit, r) = Run("mesh2d", poly, m.Path_("l2out.ele"), "--max-area", "0.05");
            AssertOk(exit, r);
            var files = r["files"].AsArray().Select(S).ToArray();
            Assert.Equal(new[] { m.Path_("l2out.node"), m.Path_("l2out.ele"), m.Path_("l2out.poly") }, files);

            // .node + .ele read back: the same counts and area, counterclockwise, the labels as the triangle attribute
            var nodes = TrianglePolyFormat.ReadNode(File.ReadAllText(files[0]), false, out var info);
            Assert.Equal(1, info.FirstIndex);
            Assert.Equal(I(r["vertices"]), nodes.Points.Count);
            var tris = TrianglePolyFormat.ReadEle(File.ReadAllText(files[1]), nodes.Points.Count, 1, out var attr);
            Assert.Equal(I(r["triangles"]), tris.Count);
            Assert.All(attr, a => Assert.Equal(3, a));
            double area = 0;
            foreach (var t in tris) {
                Vector2d a = nodes.Points[t.a], b = nodes.Points[t.b], c = nodes.Points[t.c];
                double signed = 0.5 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                Assert.True(signed > 0);
                area += signed;
            }
            Assert.Equal(2.75, area, 12);

            // the .poly of the result (subsegments with markers, vertices in the .node file) meshed again without refinement keeps
            // the domain and every vertex: the subsegments are its constraints
            var (exit2, r2) = Run("mesh2d", files[2], m.Path_("again.off"), "--quality", "false");
            AssertOk(exit2, r2);
            Assert.Equal(2.75, D(r2["area"]), 12);
            Assert.Equal(I(r["vertices"]), I(r2["vertices"]));
            Assert.Equal(0, I(r2["added_vertices"]));
            // markers: vertices on the outer boundary carry 5 (bottom, right) or 1 (unmarked boundary), hole corners 9
            string node = File.ReadAllText(files[0]);
            Assert.Contains("\n1 0 0 5\n", node);
            Assert.Contains(" 0.25 0.25 9\n", node);
        }

        [Fact]
        public void Poly_ZeroBasedNodeFileAndHeights()
        {
            // a .poly without vertices takes them from the .node file of the same name; numbering from 0; the first attribute as height
            Write("terrain.node", "4 2 1 0\n0 0 0 0\n1 4 0 4\n2 4 4 8\n3 0 4 4\n");
            string poly = Write("terrain.poly", "0 2 1 0\n4 0\n0 0 1\n1 1 2\n2 2 3\n3 3 0\n0\n");
            var (exit, r) = Run("mesh2d", poly, m.Path_("terrain.obj"), "--max-area", "0.5", "--poly-heights");
            AssertOk(exit, r);
            Assert.Equal(1, I(r["rings_from_segments"]));
            Assert.Equal(16, D(r["area"]), 12);
            DMesh3 mesh = StandardMeshReader.ReadMesh(m.Path_("terrain.obj"));
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                Assert.Equal(p.x + p.y, p.z, 9);
            }
            // without the flag the attribute is not a height
            (exit, r) = Run("mesh2d", poly, m.Path_("terrain0.obj"), "--max-area", "0.5");
            AssertOk(exit, r);
            Assert.All(StandardMeshReader.ReadMesh(m.Path_("terrain0.obj")).Vertices(), p => Assert.Equal(0, p.z));
            // a .node file alone: its convex hull
            (exit, r) = Run("mesh2d", m.Path_("terrain.node"), m.Path_("hull.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(2, I(r["triangles"]));
        }

        [Fact]
        public void Poly_OpenChains_AreConstraintsAndTheHullIsTheDomain()
        {
            // an open chain inside a square of points: no loop, so the convex hull is meshed (Triangle -c), with a note
            string poly = Write("open.poly", "6 2 0 0\n1 0 0\n2 4 0\n3 4 4\n4 0 4\n5 1 1\n6 3 3\n1 0\n1 5 6\n0\n");
            var (exit, r) = Run("mesh2d", poly, m.Path_("open.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(0, I(r["rings_from_segments"]));
            Assert.Equal(16, D(r["area"]), 12);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("convex hull"));
            DMesh3 mesh = StandardMeshReader.ReadMesh(m.Path_("open.obj"));
            Assert.NotEqual(DMesh3.InvalidID, mesh.FindEdge(4, 5));     // the constraint 5-6 (0-based 4-5) is an edge
        }

        [Fact]
        public void PolyOfInput_RoundTrips()
        {
            var input = new ConstrainedMesh2Input();
            foreach (var p in new[] { new Vector2d(0, 0), new Vector2d(3, 0), new Vector2d(3, 2), new Vector2d(0, 2), new Vector2d(1, 1), new Vector2d(2, 1) })
                input.Points.Add(p);
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, false, 4));
            input.Segments.Add(new Mesh2Segment(4, 5, 7));
            input.HoleSeeds.Add(new Vector2d(10, 10));
            input.Regions.Add(Mesh2Region.BySeed(new Vector2d(0.5, 0.5), 2, 0.25));
            input.Heights = new List<double> { 0, 1, 2, 3, 4, 5 };
            string text = TrianglePolyFormat.WritePolyOfInput(input);
            var back = TrianglePolyFormat.ReadPoly(text, null, true, out var info);
            Assert.Equal(1, info.FirstIndex);
            Assert.Equal(input.Points, back.Points);
            Assert.Equal(input.Heights, back.Heights);
            Assert.Equal(5, back.Segments.Count);           // the segment and the four ring edges
            Assert.Equal(new[] { 7, 4, 4, 4, 4 }, back.Segments.Select(s => s.Marker).ToArray());
            Assert.Single(back.Rings);                      // the ring edges form a loop again
            Assert.Equal(new[] { 0, 1, 2, 3 }, back.Rings[0].Vertices.OrderBy(v => v));     // the outer face, in either orientation
            Assert.Equal(input.HoleSeeds, back.HoleSeeds);
            Assert.Equal(2, back.Regions[0].Label);
            Assert.Equal(0.25, back.Regions[0].MaxArea);
            Assert.Equal(text, TrianglePolyFormat.WritePolyOfInput(new ConstrainedMesh2Input {
                Points = back.Points, Segments = back.Segments, HoleSeeds = back.HoleSeeds, Regions = back.Regions, Heights = back.Heights }));
            // what the format cannot hold
            input.Rings.Add(new Mesh2Ring(new[] { 4, 5, 2 }, true));
            Assert.Throws<ArgumentException>(() => TrianglePolyFormat.WritePolyOfInput(input));
        }

        [Fact]
        public void TriangleOutput_RingRegions_AreReportedAsLeftOut()
        {
            // a region by ring has no .poly form: the .ele keeps the labels, a warning says the .poly has only seed regions
            string geometry = "{\"points\": [[0,0],[2,0],[2,1],[0,1],[1,0],[1,1]], \"rings\": [[0,1,2,3]], \"regions\": [{\"ring\": [0,4,5,3], \"label\": 4}]}";
            var (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("ringreg.ele"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("no .poly form"));
            var nodes = TrianglePolyFormat.ReadNode(File.ReadAllText(m.Path_("ringreg.node")), false, out var info);
            TrianglePolyFormat.ReadEle(File.ReadAllText(m.Path_("ringreg.ele")), nodes.Points.Count, info.FirstIndex, out var labels);
            Assert.Contains(4.0, labels);
            Assert.Contains(0.0, labels);
        }

        [Fact]
        public void MalformedFiles_AreReadErrors()
        {
            string output = m.Path_("bad_out.obj");
            var bad = new Dictionary<string, string> {
                ["truncated"] = "4 2 0 0\n1 0 0\n2 1 0\n",
                ["dimension 3"] = "3 3 0 0\n1 0 0 0\n2 1 0 0\n3 0 1 0\n0 0\n0\n",
                ["out of order"] = "3 2 0 0\n1 0 0\n3 1 0\n2 0 1\n0 0\n0\n",
                ["first index 2"] = "3 2 0 0\n2 0 0\n3 1 0\n4 0 1\n0 0\n0\n",
                ["segment index"] = "3 2 0 0\n1 0 0\n2 1 0\n3 0 1\n1 0\n1 1 4\n0\n",
                ["not a number"] = "3 2 0 0\n1 0 0\n2 x 0\n3 0 1\n0 0\n0\n",
                ["infinite"] = "3 2 0 0\n1 0 0\n2 1e400 0\n3 0 1\n0 0\n0\n",
                ["fractional label"] = "3 2 0 0\n1 0 0\n2 1 0\n3 0 1\n0 0\n0\n1\n1 0.2 0.2 1.5 -1\n",
                ["trailing text"] = "3 2 0 0\n1 0 0\n2 1 0\n3 0 1\n0 0\n0\n0\nextra\n",
                ["no node file"] = "0 2 0 0\n0 0\n0\n",
            };
            foreach (var kv in bad) {
                string path = Write("bad_" + kv.Key.Replace(' ', '_') + ".poly", kv.Value);
                var (exit, r) = Run("mesh2d", path, output);
                Assert.True(S(r["code"]) == ResultCodes.ReadError, kv.Key + ": " + r.ToJsonString());
                Assert.Equal(3, exit);
            }
            Assert.False(File.Exists(output));
            var (e2, r2) = Run("mesh2d", m.Path_("missing.poly"), output);
            Assert.Equal(ResultCodes.ReadError, S(r2["code"]));
            // poly_heights only with Triangle input
            File.WriteAllText(m.Path_("p.json"), "{\"points\": [[0,0],[1,0],[0,1]]}");
            (e2, r2) = Run("mesh2d", m.Path_("p.json"), output, "--poly-heights");
            Assert.Equal(ResultCodes.InvalidArguments, S(r2["code"]));
        }

        [Fact]
        public void FuzzedPoly_NeverAnInternalError()
        {
            var rnd = new Random(1102);
            const string chars = "0123456789.-e #\n ";
            var allowed = new[] { ResultCodes.Ok, ResultCodes.ReadError, ResultCodes.OperationFailed };
            string path = m.Path_("fuzz.poly"), output = m.Path_("fuzz.off");
            for (int iter = 0; iter < 300; ++iter) {
                var sb = new StringBuilder(LShape);
                int edits = 1 + rnd.Next(3);
                for (int e = 0; e < edits && sb.Length > 1; ++e) {
                    int pos = rnd.Next(sb.Length);
                    switch (rnd.Next(4)) {
                        case 0: sb.Remove(pos, 1); break;
                        case 1: sb[pos] = chars[rnd.Next(chars.Length)]; break;
                        case 2: sb.Insert(pos, sb[pos]); break;
                        default: sb.Length = pos; break;
                    }
                }
                File.WriteAllText(path, sb.ToString());
                var (exit, r) = Run("mesh2d", path, output, "--steiner-limit", "2000");
                Assert.True(allowed.Contains(S(r["code"])), sb + " -> " + r.ToJsonString());
                Assert.Equal(ResultCodes.ExitCode(S(r["code"])), exit);
            }
        }
    }
}
