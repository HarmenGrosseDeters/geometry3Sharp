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
            Assert.Equal(new[] { 0, 1, 2, 3 }, back.Rings[0].Vertices);
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
