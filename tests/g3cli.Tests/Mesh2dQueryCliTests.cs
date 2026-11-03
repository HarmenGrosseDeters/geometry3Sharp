using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// The commands and options of stage 8: mesh2d (ConstrainedDelaunayMesh2 / QualityMesh2), query2d (TriangleQuadtree2d),
    /// boolean op xor, the SafeRounding switches and counts of boolean / resolve, and the newer MeshRobustRepair options of
    /// repair. Inputs are generated here; results are checked on the written files, not only on the reported status.
    /// </summary>
    public class Mesh2dQueryCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public Mesh2dQueryCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertOk(int exit, JsonObject r)
        {
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal("ok", S(r["code"]));
        }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.False(string.IsNullOrEmpty(S(r["message"])));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        static DMesh3 ReadMesh(string path)
        {
            DMesh3 mesh = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(mesh);
            return mesh;
        }

        static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        // 10 x 10 square with a 2 x 2 hole in the middle, the whole domain labelled 7 by a seed
        const string SquareWithHole = "{\"points\": [[0,0],[10,0],[10,10],[0,10],[4,4],[6,4],[6,6],[4,6]], " +
            "\"rings\": [[0,1,2,3], {\"vertices\": [4,5,6,7], \"hole\": true}], \"regions\": [{\"seed\": [1,1], \"label\": 7}]}";


        // ---- mesh2d ----

        [Fact]
        public void Mesh2d_SquareWithHole_MeetsTheQualityBounds()
        {
            string json = m.Path_("square_hole.json"), output = m.Path_("square_hole.obj");
            File.WriteAllText(json, SquareWithHole);
            var (exit, r) = Run("mesh2d", json, output, "--max-area", "2", "--min-angle", "25");
            AssertOk(exit, r);
            Assert.Equal("quality", S(r["method"]));
            Assert.True(D(r["smallest_angle"]) >= 25 - 1e-9, r.ToJsonString());
            Assert.True(D(r["largest_area"]) <= 2, r.ToJsonString());
            Assert.Equal(96, D(r["area"]), 9);
            Assert.Equal(0, I(r["bad_triangles"]));
            Assert.False(B(r["steiner_limit_reached"]));
            Assert.True(I(r["added_vertices"]) > 0);
            Assert.Equal(I(r["triangles"]), I(r["labels"]["7"]));

            // the written mesh: the triangles reported, all counterclockwise in XY, one component with two boundary loops
            // (outer square and hole), the hole empty, labels as groups
            DMesh3 mesh = ReadMesh(output);
            Assert.Equal(I(r["triangles"]), mesh.TriangleCount);
            Assert.Equal(I(r["vertices"]), mesh.VertexCount);
            double area = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Vector3d a = mesh.GetVertex(mesh.GetTriangle(tid).a), b = mesh.GetVertex(mesh.GetTriangle(tid).b), c = mesh.GetVertex(mesh.GetTriangle(tid).c);
                double signed = 0.5 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                Assert.True(signed > 0, "clockwise triangle " + tid);
                Assert.True(a.z == 0 && b.z == 0 && c.z == 0);
                Vector3d centroid = (a + b + c) / 3;
                Assert.False(centroid.x > 4 && centroid.x < 6 && centroid.y > 4 && centroid.y < 6, "triangle in the hole");
                area += signed;
            }
            Assert.Equal(96, area, 9);
            Assert.Equal(SmallestAngle(mesh), D(r["smallest_angle"]), 9);
            Assert.Equal(1, I(r["mesh"]["components"]));
            Assert.Equal(0, I(r["mesh"]["degenerate_triangles"]));
            Assert.Equal(0, I(r["mesh"]["bowtie_vertices"]));
            Assert.Equal(2, new MeshBoundaryLoops(mesh).Count);
            Assert.Contains("g mmGroup7", File.ReadAllText(output));
        }

        [Fact]
        public void Mesh2d_ConstrainedDelaunayOnly_AddsNoVertices()
        {
            // a square and its centre: 4 triangles, no Steiner points
            var (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[2,0],[2,2],[0,2],[1,1]]}", "--output", m.Path_("cdt.off"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal("constrained_delaunay", S(r["method"]));
            Assert.Null(r["input"]);
            Assert.Equal(5, I(r["vertices"]));
            Assert.Equal(4, I(r["triangles"]));
            Assert.Equal(0, I(r["added_vertices"]));
            Assert.Equal(45, D(r["smallest_angle"]), 9);
            DMesh3 mesh = ReadMesh(m.Path_("cdt.off"));
            Assert.Equal(4, mesh.TriangleCount);
            Assert.Equal(4.0, MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).y, 12);

            // a constraint across the square is kept as an edge (the diagonal 0-2 against the Delaunay choice of 1-3)
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[2,0],[2,2.1],[0,2]], \"segments\": [[1,3]]}",
                "--output", m.Path_("cdt2.obj"), "--quality", "false");
            AssertOk(exit, r);
            mesh = ReadMesh(m.Path_("cdt2.obj"));
            Assert.NotEqual(DMesh3.InvalidID, mesh.FindEdge(1, 3));
            Assert.Equal(DMesh3.InvalidID, mesh.FindEdge(0, 2));
        }

        [Fact]
        public void Mesh2d_DuplicateAndUnusedPoints_AreNotWrittenOrCounted()
        {
            // point 4 duplicates point 0, point 5 lies in the hole: neither is a vertex of a triangle
            string geometry = "{\"points\": [[0,0],[3,0],[3,3],[0,3],[0,0],[1.5,1.5],[1,1],[2,1],[2,2],[1,2]], " +
                "\"rings\": [[0,1,2,3], {\"vertices\": [6,7,8,9], \"hole\": true}]}";
            var (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("dup.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(0, I(r["added_vertices"]));
            Assert.Equal(8, I(r["vertices"]));
            DMesh3 mesh = ReadMesh(m.Path_("dup.obj"));
            Assert.Equal(8, mesh.VertexCount);
            Assert.All(mesh.VertexIndices(), v => Assert.True(mesh.GetVtxEdgeCount(v) > 0, "isolated vertex " + v));
            Assert.Equal(8, D(r["area"]), 12);

            // a right isosceles triangle of size 1e-300: the angles do not underflow (the area does: below the double range)
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1e-300,0],[0,1e-300]]}", "--output", m.Path_("tiny.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(45, D(r["smallest_angle"]), 12);
        }

        [Fact]
        public void Mesh2d_RegionsLabelsAndHeights()
        {
            // two unit squares side by side, the right one a ring region with label 2 and its own max area; heights z = x + 2y
            string geometry = "{\"points\": [[0,0],[1,0],[2,0],[2,1],[1,1],[0,1]], \"heights\": [0,1,2,4,3,2], " +
                "\"segments\": [{\"a\": 1, \"b\": 4, \"marker\": 3}], \"rings\": [[0,1,2,3,4,5]], " +
                "\"regions\": [{\"seed\": [0.5, 0.5], \"label\": 1}, {\"ring\": [1,2,3,4], \"label\": 2, \"max_area\": 0.02}]}";
            string output = m.Path_("regions.obj");
            var (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", output);
            AssertOk(exit, r);
            int left = I(r["labels"]["1"]), right = I(r["labels"]["2"]);
            Assert.Equal(I(r["triangles"]), left + right);
            Assert.True(right >= 50 && right > 4 * left, r.ToJsonString());

            DMesh3 mesh = ReadMesh(output);
            Assert.Equal(left + right, mesh.TriangleCount);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                Assert.Equal(v.x + 2 * v.y, v.z, 9);      // heights interpolated linearly along the constraints and inside
            }
            Assert.Equal(2, D(r["area"]), 12);
            Assert.Equal(SmallestAngle(mesh), D(r["smallest_angle"]), 9);
        }

        static double SmallestAngle(DMesh3 mesh)
        {
            // in XY (the quality bounds are 2D; the heights do not count)
            double best = double.MaxValue;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Vector2d[] p = { mesh.GetVertex(t.a).xy, mesh.GetVertex(t.b).xy, mesh.GetVertex(t.c).xy };
                for (int k = 0; k < 3; ++k)
                    best = Math.Min(best, Vector2d.AngleD((p[(k + 1) % 3] - p[k]).Normalized, (p[(k + 2) % 3] - p[k]).Normalized));
            }
            return best;
        }

        [Fact]
        public void Mesh2d_SteinerLimit_IsReportedWithAWarning()
        {
            // a thin rectangle (right angles only) needs many Steiner points for 30 degrees: a limit of 0 stops early
            string geometry = "{\"points\": [[0,0],[100,0],[100,1],[0,1]], \"rings\": [[0,1,2,3]]}";
            var (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("limit.off"), "--min-angle", "30", "--steiner-limit", "0");
            AssertOk(exit, r);
            Assert.True(B(r["steiner_limit_reached"]));
            int limited = I(r["added_vertices"]);
            Assert.True(I(r["bad_triangles"]) > 0);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("Steiner"));

            (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("nolimit.off"), "--min-angle", "30");
            AssertOk(exit, r);
            Assert.False(B(r["steiner_limit_reached"]));
            Assert.Equal(0, I(r["bad_triangles"]));
            Assert.True(D(r["smallest_angle"]) >= 30 - 1e-9);
            Assert.True(I(r["added_vertices"]) > limited + 10, r.ToJsonString());
        }

        [Fact]
        public void Mesh2d_Errors()
        {
            string output = m.Path_("err.obj");
            var (exit, r) = Run("mesh2d", "--output", output);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            string json = m.Path_("err_in.json");
            File.WriteAllText(json, SquareWithHole);
            (exit, r) = Run("mesh2d", json, output, "--geometry", SquareWithHole);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0]", "--output", output);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]], \"segments\": [[0,5]]}", "--output", output);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            Assert.Contains("point 5", S(r["message"]));
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]], \"colour\": 1}", "--output", output);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,\"x\"],[0,1]]}", "--output", output);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]], \"points\": [[5,5]]}", "--output", output);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            Assert.Contains("twice", S(r["message"]));
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]]}", "--output", output, "--quality", "false", "--min-angle", "20");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]]}", "--output", output, "--min-angle", "60");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]]}", "--output", output, "--max-area", "0");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[0,1]]}", "--output", m.Path_("err.xyz"));
            AssertError(exit, r, ResultCodes.UnknownFormat);

            // a bad file is a read error
            string bad = m.Path_("bad.json");
            File.WriteAllText(bad, "{\"points\": 3}");
            (exit, r) = Run("mesh2d", bad, output);
            AssertError(exit, r, ResultCodes.ReadError);
            (exit, r) = Run("mesh2d", m.Path_("missing.json"), output);
            AssertError(exit, r, ResultCodes.ReadError);

            // crossing constraints without resolving, collinear points: the operation fails
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[1,1],[0,1]], \"segments\": [[0,2],[1,3]]}",
                "--output", output, "--resolve-crossings", "false");
            AssertError(exit, r, ResultCodes.OperationFailed);
            Assert.Contains("CrossingConstraints", S(r["message"]));
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[1,1],[0,1]], \"segments\": [[0,2],[1,3]]}",
                "--output", output, "--resolve-crossings", "false", "--quality", "false");
            AssertError(exit, r, ResultCodes.OperationFailed);
            Assert.Contains("CrossingConstraints", S(r["message"]));
            (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[1,0],[2,0]]}", "--output", output);
            AssertError(exit, r, ResultCodes.OperationFailed);
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void Mesh2d_PowerOfTwoScale_GivesTheSameMeshAndMetricsScaled()
        {
            // the mesher is exact on the doubles and its constructions scale exactly: scaling the input (and max_area) by
            // 2^k gives the same triangles with the coordinates scaled bit for bit (as Harness/Invariance.cs requires), the same
            // smallest angle and the areas scaled by 4^k, also at 2^-1000 where plain products of coordinates underflow
            double[] pts = { 0, 0, 10, 0, 10, 10, 0, 10, 4, 4, 6, 4, 6, 6, 4, 6 };
            string Geometry(double s, double t)
            {
                var sb = new StringBuilder("{\"points\": [");
                for (int i = 0; i < pts.Length; i += 2)
                    sb.Append(i > 0 ? "," : "").Append("[" + F(pts[i] * s + t) + "," + F(pts[i + 1] * s + t) + "]");
                return sb.Append("], \"rings\": [[0,1,2,3], {\"vertices\": [4,5,6,7], \"hole\": true}]}").ToString();
            }
            JsonObject result = null;
            DMesh3 Mesh(double s, double t, string name)
            {
                var (exit, r) = Run("mesh2d", "--geometry", Geometry(s, t), "--output", m.Path_(name), "--max-area", F(1.5 * s * s), "--min-angle", "28");
                AssertOk(exit, r);
                result = r;
                return ReadMesh(m.Path_(name));
            }
            DMesh3 reference = Mesh(1, 0, "scale_ref.g3mesh");
            JsonObject refResult = result;
            foreach (var (s, t) in new[] { (1.0 / 1024, 0.0), (1024.0, 0.0), (1.0 / 8, 0.0), (1048576.0, 0.0), (Math.ScaleB(1, -500), 0.0) }) {
                DMesh3 scaled = Mesh(s, t, $"scale_{F(s)}_{F(t)}.g3mesh");
                Assert.Equal(D(refResult["smallest_angle"]), D(result["smallest_angle"]));
                Assert.Equal(D(refResult["largest_area"]) * s * s, D(result["largest_area"]));
                Assert.Equal(D(refResult["added_vertices"]), D(result["added_vertices"]));
                Assert.Equal(reference.TriangleCount, scaled.TriangleCount);
                Assert.Equal(reference.VertexCount, scaled.VertexCount);
                foreach (int vid in reference.VertexIndices()) {
                    Vector3d a = reference.GetVertex(vid), b = scaled.GetVertex(vid);
                    Assert.Equal(a.x * s + t, b.x);
                    Assert.Equal(a.y * s + t, b.y);
                }
                foreach (int tid in reference.TriangleIndices())
                    Assert.Equal(reference.GetTriangle(tid), scaled.GetTriangle(tid));
            }
        }

        [Fact]
        public void Mesh2d_RepeatedRuns_WriteIdenticalFiles()
        {
            File.WriteAllText(m.Path_("det.json"), SquareWithHole);
            var (exit, r) = Run("mesh2d", m.Path_("det.json"), m.Path_("det1.g3mesh"), "--max-area", "0.5");
            AssertOk(exit, r);
            (exit, r) = Run("mesh2d", m.Path_("det.json"), m.Path_("det2.g3mesh"), "--max-area", "0.5");
            AssertOk(exit, r);
            Assert.Equal(File.ReadAllBytes(m.Path_("det1.g3mesh")), File.ReadAllBytes(m.Path_("det2.g3mesh")));
        }

        [Fact]
        public void Mesh2d_FuzzedJson_NeverAnInternalError()
        {
            // mutations of a valid input (deleted, replaced, duplicated characters, truncation): every one is answered with a
            // result code, never an exception or internal_error
            var rnd = new Random(20261102);
            const string chars = "[]{},:\"0123456789.-eE truefalsnul";
            var allowed = new[] { ResultCodes.Ok, ResultCodes.InvalidArguments, ResultCodes.OperationFailed };
            string output = m.Path_("fuzz.off");
            for (int iter = 0; iter < 300; ++iter) {
                var sb = new StringBuilder(SquareWithHole);
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
                string text = sb.ToString();
                var (exit, r) = Run("mesh2d", "--geometry", text, "--output", output, "--steiner-limit", "2000");
                Assert.True(allowed.Contains(S(r["code"])), text + " -> " + r.ToJsonString());
                Assert.Equal(ResultCodes.ExitCode(S(r["code"])), exit);
            }
        }


        // ---- boolean xor, SafeRounding ----

        [Fact]
        public void Boolean_Xor_TwoAndThreeCubes()
        {
            // cube [-1,1]^3 xor the cube moved by 1: 8 + 8 - 2 * 4
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("xor2.obj"), "--op", "xor");
            AssertOk(exit, r);
            Assert.True(B(r["mesh"]["closed"]));
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());
            Assert.Equal(8, D(r["mesh"]["volume"]), 6);
            Assert.Equal(2, I(r["mesh"]["components"]));
            Assert.True(B(r["boolean"]["simplify_flat_regions"]));
            int removed = I(r["boolean"]["removed_vertices"]);
            Assert.True(removed > 0, r["boolean"].ToJsonString());

            // without the flat region simplification the crossings of the coplanar faces stay: more vertices, same solid
            int triangles = I(r["mesh"]["triangles"]);
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("xor2_flat.obj"), "--op", "xor", "--simplify-flat-regions", "false");
            AssertOk(exit, r);
            Assert.False(B(r["boolean"]["simplify_flat_regions"]));
            Assert.Equal(0, I(r["boolean"]["removed_vertices"]));
            Assert.True(I(r["mesh"]["triangles"]) > triangles);
            Assert.Equal(8, D(r["mesh"]["volume"]), 6);

            // three cubes along x at -1..1, 0..2, 1..3: odd count in -1..0 and 2..3
            string third = m.Path_("cube_x2.obj");
            StandardMeshWriter.WriteMesh(third, CubeMesh(new Vector3d(2, 0, 0)), WriteOptions.Defaults);
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("xor3.obj"), "--op", "xor", "--more", third);
            AssertOk(exit, r);
            Assert.Equal(3, r["inputs"].AsArray().Count);
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());
            Assert.Equal(8, D(r["mesh"]["volume"]), 6);
            Assert.Equal(2, I(r["mesh"]["components"]));
            DMesh3 mesh = ReadMesh(m.Path_("xor3.obj"));
            AxisAlignedBox3d box = mesh.GetBounds();
            Assert.Equal(-1, box.Min.x, 9);
            Assert.Equal(3, box.Max.x, 9);
        }

        [Fact]
        public void Boolean_Xor_EqualsItsPairwiseCompositions()
        {
            // xor = inside an odd number of the inputs. Three spheres, pairwise overlapping and with a common core:
            // n-ary xor(a, b, c) against (a xor b) xor c, and the two-input xor against (a - b) + (b - a)
            string Sphere(string name, Vector3d c)
            {
                string path = m.Path_(name);
                DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 9 }.Generate().MakeDMesh();
                MeshTransforms.Translate(s, c);
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, s, WriteOptions.Defaults).code);
                return path;
            }
            string a = Sphere("xs_a.g3mesh", Vector3d.Zero), b = Sphere("xs_b.g3mesh", new Vector3d(0.8, 0.1, 0)),
                c = Sphere("xs_c.g3mesh", new Vector3d(0.3, 0.7, 0.2));
            // xor results (and a - b united with b - a) are solids that touch along the curves where the input surfaces
            // cross: closed, with copies of the vertices there (non-manifold edges by position), and measure counts those
            // contacts as self-intersections; the rounding is verified clean
            JsonObject Ok(params string[] args)
            {
                var (exit, r) = Run(args);
                AssertOk(exit, r);
                Assert.True(B(r["mesh"]["closed"]), r["mesh"].ToJsonString());
                Assert.True(B(r["mesh"]["winding_closed"]), r["mesh"].ToJsonString());
                Assert.True(B(r["boolean"]["rounding_clean"]), r["boolean"].ToJsonString());
                Assert.Equal(0, I(r["mesh"]["degenerate_triangles"]));
                return r;
            }
            double Volume(JsonObject r) => D(r["mesh"]["volume"]);
            void AssertSameSolid(string p, string q)
            {
                var (exit, r) = Run("measure", p, "--compare", q, "--no-self-intersections", "--tolerance", "1e-7");
                AssertOk(exit, r);
                Assert.True(D(r["hausdorff"]["distance"]) < 1e-6, r["hausdorff"].ToJsonString());
            }

            var xor2 = Ok("boolean", a, b, m.Path_("xs_ab.g3mesh"), "--op", "xor");
            Assert.True(I(xor2["boolean"]["non_manifold_edges"]) > 0, xor2["boolean"].ToJsonString());
            Assert.Equal(2, I(xor2["mesh"]["components"]));
            Ok("boolean", a, b, m.Path_("xs_amb.g3mesh"), "--op", "difference");
            Ok("boolean", b, a, m.Path_("xs_bma.g3mesh"), "--op", "difference");
            var composed2 = Ok("boolean", m.Path_("xs_amb.g3mesh"), m.Path_("xs_bma.g3mesh"), m.Path_("xs_ab2.g3mesh"), "--op", "union");
            Assert.Equal(Volume(composed2), Volume(xor2), 6);
            AssertSameSolid(m.Path_("xs_ab.g3mesh"), m.Path_("xs_ab2.g3mesh"));
            // and it is union minus intersection
            var union = Ok("boolean", a, b, m.Path_("xs_u.g3mesh"), "--op", "union");
            var inter = Ok("boolean", a, b, m.Path_("xs_i.g3mesh"), "--op", "intersection");
            Assert.Equal(Volume(union) - Volume(inter), Volume(xor2), 6);

            var xor3 = Ok("boolean", a, b, m.Path_("xs_abc.g3mesh"), "--op", "xor", "--more", c);
            var chained = Ok("boolean", m.Path_("xs_ab.g3mesh"), c, m.Path_("xs_abc2.g3mesh"), "--op", "xor");
            Assert.Equal(Volume(chained), Volume(xor3), 6);
            AssertSameSolid(m.Path_("xs_abc.g3mesh"), m.Path_("xs_abc2.g3mesh"));
            // the core inside all three is kept (odd), unlike at_least 2 minus at_least 3
            var core = Ok("boolean", a, b, m.Path_("xs_core.g3mesh"), "--op", "intersection", "--more", c);
            Assert.True(Volume(core) > 0.1);
            var two = Ok("boolean", a, b, m.Path_("xs_two.g3mesh"), "--op", "at_least", "--k", "2", "--more", c);
            var one = Ok("boolean", a, b, m.Path_("xs_one.g3mesh"), "--op", "union", "--more", c);
            // odd = (inside >= 1) - (inside >= 2) + (inside >= 3), as volumes
            Assert.Equal(Volume(one) - Volume(two) + Volume(core), Volume(xor3), 5);
        }

        // the cube and a copy turned by 1e-10 about a generic axis and shifted by about 1e-10: rounding the intersection
        // points leaves slivers and self-intersections (as in MeshRoundingRepairTests)
        string NearlyCoincidentCubes()
        {
            string path = m.Path_("cube_turned.g3mesh");
            if (File.Exists(path) == false) {
                DMesh3 b = CubeMesh(Vector3d.Zero);
                MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(new Vector3d(0.19758121236114823, 0.3649866608274107, -0.30151001680712686).Normalized, 1e-10 * MathUtil.Rad2Deg));
                MeshTransforms.Translate(b, 1e-10 * new Vector3d(0.5604903733173806, 0.1805782146661441, 0.2501971587772468));
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, b, WriteOptions.Defaults).code);
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(m.Path_("cube_exact.g3mesh"), CubeMesh(Vector3d.Zero), WriteOptions.Defaults).code);
            }
            return path;
        }

        [Fact]
        public void Boolean_SafeRounding_RepairsAndReportsTheRounding()
        {
            string b = NearlyCoincidentCubes(), a = m.Path_("cube_exact.g3mesh");
            var (exit, r) = Run("boolean", a, b, m.Path_("safe.g3mesh"), "--op", "intersection");
            AssertOk(exit, r);
            JsonNode info = r["boolean"];
            Assert.True(B(info["safe_rounding"]));
            Assert.True(B(info["rounding_clean"]));
            Assert.True(B(info["simplify_flat_regions"]));
            JsonNode rounding = info["rounding"];
            Assert.True(B(rounding["clean"]));
            Assert.True(I(rounding["initial_intersections"]) > 0, rounding.ToJsonString());
            Assert.Equal(0, I(rounding["remaining_intersections"]));
            Assert.Equal(0, I(rounding["remaining_degenerate"]));
            Assert.True(I(rounding["collapses"]) + I(rounding["flips"]) + I(rounding["moves"]) + I(rounding["zips"]) > 0);
            Assert.InRange(D(rounding["max_drift"]), 0, 1e-5 * Math.Sqrt(12) * 1.01);
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());
            Assert.Equal(8, D(r["mesh"]["volume"]), 6);

            // off: the plainly rounded result, with the slivers the rounding left
            (exit, r) = Run("boolean", a, b, m.Path_("plain.g3mesh"), "--op", "intersection", "--no-safe-rounding");
            AssertOk(exit, r);
            Assert.False(B(r["boolean"]["safe_rounding"]));
            Assert.False(B(r["boolean"]["rounding_clean"]));
            Assert.Null(r["boolean"]["rounding"]);
            Assert.True(I(r["mesh"]["degenerate_triangles"]) > 0);
            Assert.False(B(r["mesh"]["clean"]));
        }

        [Fact]
        public void Resolve_SafeRoundingAndFlatRegions_AreOptions()
        {
            // the nearly coincident cubes in one file
            string turned = NearlyCoincidentCubes();
            DMesh3 both = ReadMesh(m.Path_("cube_exact.g3mesh"));
            new MeshEditor(both).AppendMesh(ReadMesh(turned));
            string input = m.Path_("both_turned.g3mesh");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(input, both, WriteOptions.Defaults).code);

            var (exit, r) = Run("resolve", input, m.Path_("resolved_safe.g3mesh"));
            AssertOk(exit, r);
            Assert.True(B(r["boolean"]["rounding_clean"]));
            Assert.NotNull(r["boolean"]["rounding"]);
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());

            (exit, r) = Run("resolve", input, m.Path_("resolved_plain.g3mesh"), "--safe-rounding", "false", "--simplify-flat-regions", "false");
            AssertOk(exit, r);
            Assert.Null(r["boolean"]["rounding"]);
            Assert.False(B(r["boolean"]["simplify_flat_regions"]));
            Assert.Equal(0, I(r["boolean"]["removed_vertices"]));
            Assert.False(B(r["mesh"]["clean"]));
        }


        // ---- snap tolerance (m2.snap, #277) and the SafeRounding tolerances (#287) ----

        [Fact]
        public void Mesh2d_SnapTolerance_PutsTheVerticesOnTheGrid()
        {
            // points near a unit grid, two of them within one cell: snapped, the square is the grid square 0..10 and the two merge
            string geometry = "{\"points\": [[0.1,0.2],[10.3,-0.2],[9.8,10.1],[0.2,9.9],[5.02,5.01],[5.04,5.03]], \"rings\": [[0,1,2,3]]}";
            var (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("snap.obj"), "--quality", "false", "--snap-tolerance", "1");
            AssertOk(exit, r);
            Assert.Equal(1, D(r["snap_tolerance"]));
            Assert.Equal(5, I(r["vertices"]));
            Assert.Equal(100, D(r["area"]), 12);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).StartsWith("snap:"));
            DMesh3 mesh = StandardMeshReader.ReadMesh(m.Path_("snap.obj"));
            Assert.All(mesh.Vertices(), v => { Assert.Equal(Math.Round(v.x), v.x); Assert.Equal(Math.Round(v.y), v.y); });

            // quality refinement on the grid: every vertex it adds is a grid point too
            (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("snapq.obj"), "--max-area", "4", "--snap-tolerance", "0.5");
            AssertOk(exit, r);
            Assert.True(I(r["added_vertices"]) > 0);
            Assert.All(StandardMeshReader.ReadMesh(m.Path_("snapq.obj")).Vertices(), v => { Assert.Equal(Math.Round(2 * v.x), 2 * v.x); Assert.Equal(Math.Round(2 * v.y), 2 * v.y); });

            // without: the input as given
            (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("nosnap.obj"), "--quality", "false");
            AssertOk(exit, r);
            Assert.Equal(6, I(r["vertices"]));
            Assert.Equal(0, D(r["snap_tolerance"]));
            Assert.NotEqual(100, D(r["area"]), 6);

            (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("snapbad.obj"), "--snap-tolerance", "1e-30");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            Assert.Contains("snap_tolerance", S(r["message"]));
            (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("snapbad.obj"), "--snap-tolerance", "-1");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
        }

        // a sphere with clusters of pokes (fans of slivers and zero-area triangles), turned and moved to UTM coordinates at
        // millimetre scale: the case of SafeRoundingBoundTests that the relative bound alone cannot repair (#287)
        string UtmCluster()
        {
            string path = m.Path_("utm_cluster.g3mesh");
            if (File.Exists(path)) return path;
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            var rnd = new Random(0);
            foreach (int t0 in mesh.TriangleIndices().Where(t => t % 17 == 0).ToArray()) {
                int tid = t0;
                for (int i = 0; i < 4; i++) {
                    Index3i t = mesh.GetTriangle(tid);
                    Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b);
                    Vector3d onEdge = a + (0.2 + 0.6 * rnd.NextDouble()) * (b - a);
                    if (mesh.PokeTriangle(tid, out DMesh3.PokeTriangleInfo info) != MeshResult.Ok) break;
                    mesh.SetVertex(info.new_vid, onEdge);
                    tid = rnd.Next(3) == 0 ? info.new_t1 : rnd.Next(2) == 0 ? info.new_t2 : tid;
                }
            }
            Matrix3d rot = Matrix3d.AxisAngleD(Vector3d.AxisY, 23);
            foreach (int v in mesh.VertexIndices().ToArray())
                mesh.SetVertex(v, 1e-3 * (rot * mesh.GetVertex(v)) + new Vector3d(632117.25, 5712043.5, 113.75));
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults).code);
            return path;
        }

        [Fact]
        public void Resolve_RoundingTolerances_AtUtmCoordinates()
        {
            string input = UtmCluster();
            // default: the ulp term (256 ulps of the largest coordinate) lets the repair make it clean
            var (exit, r) = Run("resolve", input, m.Path_("utm_default.g3mesh"));
            AssertOk(exit, r);
            Assert.True(B(r["boolean"]["rounding_clean"]), r["boolean"].ToJsonString());
            Assert.Equal(256, D(r["boolean"]["rounding_ulp_factor"]));
            Assert.Equal(5000000, D(r["boolean"]["rounding_max_pair_tests"]));
            Assert.False(B(r["boolean"]["rounding"]["evaluation_limit_reached"]));
            Assert.False(B(r["boolean"]["strict_rounding"]));
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());
            double bound = D(r["boolean"]["rounding"]["max_length"]);

            // strict: the relative bound alone is not enough, so it fails with the reason and writes nothing
            string strictOut = m.Path_("utm_strict.g3mesh");
            (exit, r) = Run("resolve", input, strictOut, "--strict-rounding");
            Assert.Equal(ResultCodes.OperationFailed, S(r["code"]));
            Assert.Contains("RoundingNotClean", S(r["message"]));
            Assert.False(File.Exists(strictOut));
            // strict uses the relative bound only: combined with a tolerance it is refused
            (exit, r) = Run("resolve", input, strictOut, "--strict-rounding", "--rounding-absolute-tolerance", "1e-3");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            (exit, r) = Run("resolve", input, strictOut, "--strict-rounding", "--rounding-ulp-factor", "1000");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            // an ordinary case is clean also when strict; the report gives the effective terms (none)
            (exit, r) = Run("resolve", m.Overlapping, m.Path_("strict_ok.obj"), "--strict-rounding");
            AssertOk(exit, r);
            Assert.True(B(r["boolean"]["strict_rounding"]));
            Assert.Equal(0, D(r["boolean"]["rounding_ulp_factor"]));
            Assert.True(B(r["mesh"]["clean"]));

            // without the ulp term: written, but not clean, within a smaller bound
            (exit, r) = Run("resolve", input, m.Path_("utm_noulp.g3mesh"), "--rounding-ulp-factor", "0");
            AssertOk(exit, r);
            Assert.False(B(r["boolean"]["rounding_clean"]));
            Assert.True(D(r["boolean"]["rounding"]["max_length"]) < bound);

            // an absolute tolerance sets the bound
            (exit, r) = Run("resolve", input, m.Path_("utm_abs.g3mesh"), "--rounding-ulp-factor", "0", "--rounding-absolute-tolerance", "1e-3");
            AssertOk(exit, r);
            Assert.Equal(1e-3, D(r["boolean"]["rounding"]["max_length"]));
            Assert.True(B(r["boolean"]["rounding_clean"]));

            // the effort budget: at a tiny one the repair stops at the limit and writes the result with its defects
            (exit, r) = Run("resolve", input, m.Path_("utm_budget.g3mesh"), "--rounding-max-pair-tests", "10");
            AssertOk(exit, r);
            Assert.Equal(10, D(r["boolean"]["rounding_max_pair_tests"]));
            Assert.Equal(10, D(r["boolean"]["rounding"]["max_pair_tests"]));
            Assert.True(B(r["boolean"]["rounding"]["evaluation_limit_reached"]));
            Assert.False(B(r["boolean"]["rounding_clean"]));
            (exit, r) = Run("resolve", input, m.Path_("utm_budget.g3mesh"), "--rounding-max-pair-tests", "-1");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));

            // the same options on boolean; strict needs safe rounding, tolerances are >= 0
            // (with itself: a cube at the origin would make the diagonal, and so the relative bound, huge)
            (exit, r) = Run("boolean", input, input, m.Path_("utm_b.g3mesh"), "--op", "union", "--strict-rounding");
            Assert.Equal(ResultCodes.OperationFailed, S(r["code"]));
            Assert.Contains("RoundingNotClean", S(r["message"]));
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("b_bad.obj"), "--op", "union", "--strict-rounding", "--no-safe-rounding");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("b_bad.obj"), "--op", "union", "--rounding-ulp-factor", "-1");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            (exit, r) = Run("resolve", m.Overlapping, m.Path_("r_bad.obj"), "--rounding-absolute-tolerance", "-1");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            // the relative bound is limited: larger ones let the repair contract whole components away
            (exit, r) = Run("resolve", m.Overlapping, m.Path_("r_bad.obj"), "--rounding-max-displacement", "0.5");
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            Assert.Contains("1e-2", S(r["message"]));
        }


        // ---- the SafeRounding settings of the robust repair (#294) ----

        // a box [-1, 1]^2 x [-2, 0] whose top is a dent down to (0, 0, -0.5), and a pyramid pointing down into it with its tip on
        // the bottom of the dent, in one mesh: the exact resolve and the repair after the separation of the touching tips both run
        // (MeshRoundingRepairTests.PyramidInADent)
        string PyramidInADent()
        {
            string path = m.Path_("pyramid_dent.g3mesh");
            if (File.Exists(path)) return path;
            var mesh = new DMesh3();
            int[] top = { mesh.AppendVertex(new Vector3d(-1, -1, 0)), mesh.AppendVertex(new Vector3d(1, -1, 0)), mesh.AppendVertex(new Vector3d(1, 1, 0)), mesh.AppendVertex(new Vector3d(-1, 1, 0)) };
            int[] bottom = { mesh.AppendVertex(new Vector3d(-1, -1, -2)), mesh.AppendVertex(new Vector3d(1, -1, -2)), mesh.AppendVertex(new Vector3d(1, 1, -2)), mesh.AppendVertex(new Vector3d(-1, 1, -2)) };
            var dent = new Vector3d(0, 0, -0.5);
            int p = mesh.AppendVertex(dent);
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                mesh.AppendTriangle(p, top[i], top[j]);
                mesh.AppendTriangle(top[i], bottom[i], bottom[j]);
                mesh.AppendTriangle(top[i], bottom[j], top[j]);
            }
            mesh.AppendTriangle(bottom[0], bottom[2], bottom[1]);
            mesh.AppendTriangle(bottom[0], bottom[3], bottom[2]);
            int tip = mesh.AppendVertex(dent);
            int[] b = new int[4];
            for (int i = 0; i < 4; ++i)
                b[i] = mesh.AppendVertex(dent + new Vector3d((i == 0 || i == 3) ? -0.3 : 0.3, (i < 2) ? -0.3 : 0.3, 0.49));
            for (int i = 0; i < 4; ++i)
                mesh.AppendTriangle(tip, b[(i + 1) % 4], b[i]);
            mesh.AppendTriangle(b[0], b[1], b[2]);
            mesh.AppendTriangle(b[0], b[2], b[3]);
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults).code);
            return path;
        }

        // two unit spheres, one turned by 1e-10 and shifted by about 1e-10, in one mesh: rounding leaves slivers
        string NearlyCoincidentSpheres()
        {
            string path = m.Path_("spheres_turned.g3mesh");
            if (File.Exists(path)) return path;
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(new Vector3d(0.6, 0.3, 0.1).Normalized, 1e-10 * MathUtil.Rad2Deg));
            MeshTransforms.Translate(b, 1e-10 * new Vector3d(0.2, 0.9, 0.5));
            new MeshEditor(a).AppendMesh(b);
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, a, WriteOptions.Defaults).code);
            return path;
        }

        [Fact]
        public void Repair_RoundingSettings_ReachBothRepairsOfTheExactStage()
        {
            string input = PyramidInADent();
            var (exit, r) = Run("repair", input, m.Path_("dent_out.g3mesh"), "--no-alpha-wrap",
                "--rounding-max-displacement", "2e-5", "--rounding-ulp-factor", "128", "--rounding-absolute-tolerance", "1e-9",
                "--rounding-max-pair-tests", "12345678");
            AssertOk(exit, r);
            Assert.True(B(r["used_exact_resolve"]), r.ToJsonString());
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());
            foreach (string key in new[] { "exact_resolve_rounding", "separated_rounding" }) {
                JsonNode rr = r[key];
                Assert.NotNull(rr);
                Assert.Equal(2e-5, D(rr["max_displacement"]));
                Assert.Equal(128, D(rr["ulp_factor"]));
                Assert.Equal(1e-9, D(rr["absolute_tolerance"]));
                Assert.Equal(12345678, D(rr["max_pair_tests"]));
            }
            // the defaults: the library's
            (exit, r) = Run("repair", input, m.Path_("dent_default.g3mesh"), "--no-alpha-wrap");
            AssertOk(exit, r);
            Assert.Equal(1e-5, D(r["exact_resolve_rounding"]["max_displacement"]));
            Assert.Equal(256, D(r["separated_rounding"]["ulp_factor"]));
            Assert.Equal(5000000, D(r["separated_rounding"]["max_pair_tests"]));
            Assert.False(B(r["strict_rounding"]));

            // strict: both repairs without the ulp and the absolute term
            (exit, r) = Run("repair", input, m.Path_("dent_strict.g3mesh"), "--no-alpha-wrap", "--strict-rounding");
            AssertOk(exit, r);
            Assert.True(B(r["strict_rounding"]));
            Assert.Equal(0, D(r["exact_resolve_rounding"]["ulp_factor"]));
            Assert.Equal(0, D(r["separated_rounding"]["ulp_factor"]));
            Assert.Equal(0, D(r["separated_rounding"]["absolute_tolerance"]));

            // method auto has no exact stage: the settings do not apply, and there is no rounding report
            (exit, r) = Run("repair", input, m.Path_("dent_auto.g3mesh"), "--method", "auto", "--strict-rounding");
            AssertOk(exit, r);
            Assert.Null(r["exact_resolve_rounding"]);

            // a mesh that is clean already: no exact stage, no rounding reports
            (exit, r) = Run("repair", m.Cube, m.Path_("cube_rep.obj"));
            AssertOk(exit, r);
            Assert.Null(r["exact_resolve_rounding"]);
            Assert.Null(r["separated_rounding"]);
        }

        [Fact]
        public void Repair_StrictRounding_ExactStageFailsWithItsReason()
        {
            // with a bound too small for the slivers, strict: the exact resolve fails with its reason and the other stages run
            var (exit, r) = Run("repair", NearlyCoincidentSpheres(), m.Path_("spheres_strict.g3mesh"), "--no-alpha-wrap",
                "--strict-rounding", "--rounding-max-displacement", "1e-16", "--solidify-resolution", "32");
            Assert.Equal("ok", S(r["code"]));
            Assert.False(B(r["used_exact_resolve"]));
            Assert.Equal("rounding not clean (StrictRounding)", S(r["exact_resolve_reason"]));
            Assert.Equal(0, D(r["exact_resolve_rounding"]["ulp_factor"]));
            Assert.Equal(1e-16, D(r["exact_resolve_rounding"]["max_displacement"]));
            Assert.True(B(r["used_solidify"]));

            // invalid settings
            foreach (var bad in new[] { new[] { "--rounding-max-displacement", "-1" }, new[] { "--rounding-max-displacement", "1" },
                    new[] { "--rounding-max-displacement", "0.5" }, new[] { "--rounding-max-displacement", "0" }, new[] { "--rounding-max-displacement", "1e-300" },
                    new[] { "--rounding-ulp-factor", "-1" }, new[] { "--strict-rounding", "--no-safe-rounding" },
                    new[] { "--strict-rounding", "--rounding-ulp-factor", "100" } }) {
                (exit, r) = Run(new[] { "repair", m.Cube, m.Path_("bad.obj") }.Concat(bad).ToArray());
                Assert.True(S(r["code"]) == ResultCodes.InvalidArguments, string.Join(" ", bad) + " -> " + r.ToJsonString());
            }
        }

        [Fact]
        public void Resolve_RoundingMaxDisplacement_SetsTheRelativeBound()
        {
            // at UTM coordinates the relative bound is the smaller term: raised a hundredfold, the bound follows it
            string input = UtmCluster();
            var (exit, r) = Run("resolve", input, m.Path_("utm_md.g3mesh"), "--rounding-max-displacement", "1e-3", "--rounding-ulp-factor", "0");
            AssertOk(exit, r);
            Assert.Equal(1e-3, D(r["boolean"]["rounding_max_displacement"]));
            Assert.Equal(1e-3, D(r["boolean"]["rounding"]["max_displacement"]));
            double diag = D(r["input_mesh"]["bounds"]["diagonal"]);
            Assert.InRange(D(r["boolean"]["rounding"]["max_length"]), 1e-3 * diag * 0.5, 1e-3 * diag * 2);
            Assert.True(B(r["boolean"]["rounding_clean"]));
        }


        // ---- repair options ----

        [Fact]
        public void Repair_RobustOptions_ChangeTheFallbacks()
        {
            // a limit of one self-intersecting pair skips the exact resolve of the overlapping cubes: the solidify runs
            var (exit, r) = Run("repair", m.Overlapping, m.Path_("rep_pairs.obj"), "--solidify-resolution", "64", "--exact-resolve-max-pairs", "1");
            AssertOk(exit, r);
            Assert.False(B(r["used_exact_resolve"]));
            Assert.False(string.IsNullOrEmpty(S(r["exact_resolve_reason"])));
            Assert.True(B(r["used_solidify"]) || B(r["used_alpha_wrap"]));
            Assert.True(B(r["mesh"]["closed"]));
            Assert.InRange(D(r["mesh"]["volume"]), 11.5, 12.5);
            Assert.False(B(r["used_input"]));

            // the default: resolved exactly, the new report fields present
            (exit, r) = Run("repair", m.Overlapping, m.Path_("rep_default.obj"));
            AssertOk(exit, r);
            Assert.True(B(r["used_exact_resolve"]));
            Assert.False(B(r["used_alpha_wrap"]));
            Assert.Equal(0, I(r["alpha_wrap_triangles"]));
            Assert.Null(r["wrap_reason"]);
            Assert.Null(r["result_reason"]);
            Assert.True(B(r["mesh"]["clean"]));

            (exit, r) = Run("repair", m.Overlapping, m.Path_("rep_err.obj"), "--check-lost-components", "--min-volume-iou", "0");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("repair", m.Overlapping, m.Path_("rep_err.obj"), "--exact-resolve-max-pairs", "-1");
            AssertError(exit, r, ResultCodes.InvalidArguments);
        }


        // ---- query2d ----

        // a 10 x 10 grid of the plane z = 2x + 3y + 1 over [0, 5]^2, as two triangles per cell
        string PlaneTerrain()
        {
            string path = m.Path_("plane_terrain.obj");
            if (File.Exists(path))
                return path;
            var mesh = new DMesh3();
            int n = 10;
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = 0.5 * i, y = 0.5 * j;
                    mesh.AppendVertex(new Vector3d(x, y, 2 * x + 3 * y + 1));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            WriteOptions o = WriteOptions.Defaults;
            o.RealPrecisionDigits = 17;
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, o).code);
            return path;
        }

        [Fact]
        public void Query2d_HeightsRectangleAndNearest_MatchBruteForce()
        {
            string terrain = PlaneTerrain();
            DMesh3 mesh = ReadMesh(terrain);
            var rnd = new Random(7);
            var pts = Enumerable.Range(0, 20).Select(_ => new Vector2d(0.01 + 4.98 * rnd.NextDouble(), 0.01 + 4.98 * rnd.NextDouble())).ToList();
            pts.Add(new Vector2d(9, 9));          // outside
            pts.Add(new Vector2d(5.001, 2));      // outside, within the tolerance
            string text = string.Join(";", pts.Select(p => F(p.x) + "," + F(p.y)));
            var (exit, r) = Run("query2d", terrain, "--points", text, "--tolerance", "0.01", "--nearest", "3", "--rect", "1.2,1.2,1.3,2.6");
            AssertOk(exit, r);
            Assert.Equal(200, I(r["triangles"]));
            Assert.Equal(5, D(r["extent"]["max"][0]), 12);
            var results = r["points"].AsArray();
            Assert.Equal(pts.Count, results.Count);
            Assert.Equal(pts.Count - 1, I(r["points_with_height"]));
            for (int i = 0; i < 20; ++i) {
                Vector2d p = pts[i];
                Assert.Equal(2 * p.x + 3 * p.y + 1, D(results[i]["height"]), 9);
                // the reported triangle contains the point (brute force)
                int tid = I(results[i]["triangle"]);
                Assert.True(Contains(mesh, tid, p), $"triangle {tid} does not contain {p}");
                var nearest = results[i]["nearest"].AsArray().Select(I).ToArray();
                Assert.Equal(3, nearest.Length);
                Assert.Contains(tid, nearest);
            }
            Assert.Null(results[20]["height"]);
            Assert.Null(results[20]["triangle"]);
            Assert.Equal(0, I(results[20]["hits_count"]));
            Assert.Equal(2 * 5 + 3 * 2 + 1, D(results[21]["height"]), 9);   // at the nearest boundary point (5, 2)

            // rectangle: exactly the triangles whose XY bounding boxes meet it
            var box = new AxisAlignedBox2d(1.2, 1.2, 1.3, 2.6);
            var expected = mesh.TriangleIndices().Where(t => {
                var tb = new AxisAlignedBox2d(new Vector2d(mesh.GetTriVertex(t, 0).x, mesh.GetTriVertex(t, 0).y));
                tb.Contain(new Vector2d(mesh.GetTriVertex(t, 1).x, mesh.GetTriVertex(t, 1).y));
                tb.Contain(new Vector2d(mesh.GetTriVertex(t, 2).x, mesh.GetTriVertex(t, 2).y));
                return tb.Intersects(box);
            }).OrderBy(t => t).ToArray();
            Assert.Equal(expected, r["rect"]["triangles"].AsArray().Select(I).ToArray());
            Assert.Equal(expected.Length, I(r["rect"]["triangles_count"]));
            Assert.False(B(r["rect"]["triangles_truncated"]));

            // truncated lists
            (exit, r) = Run("query2d", terrain, "--rect", "0,0,5,5", "--max-results", "10");
            AssertOk(exit, r);
            Assert.Equal(200, I(r["rect"]["triangles_count"]));
            Assert.True(B(r["rect"]["triangles_truncated"]));
            Assert.Equal(10, r["rect"]["triangles"].AsArray().Count);
            Assert.Null(r["points"]);
        }

        [Fact]
        public void Query2d_OnEdgesVerticesAndOutside_MatchesBruteForce()
        {
            // a procedural terrain (z = sin x cos y on a jittered grid, 2 triangles per cell), points at its vertices, on its
            // edges (midpoints, exact in doubles), inside, and outside: hits must be exactly the triangles that contain the
            // point (closed, brute force with exact orientation tests), the height that of the plane of the lowest id
            var mesh = new DMesh3();
            var rnd = new Random(11);
            int n = 8;
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = i + ((i > 0 && i < n) ? 0.25 * (rnd.Next(5) - 2) / 2 : 0), y = j + ((j > 0 && j < n) ? 0.25 * (rnd.Next(5) - 2) / 2 : 0);
                    mesh.AppendVertex(new Vector3d(x, y, Math.Sin(x) * Math.Cos(y)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            string path = m.Path_("jitter_terrain.g3mesh");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults).code);

            var pts = new List<Vector2d>();
            foreach (int vid in new[] { 0, 10, 40, 80 })
                pts.Add(mesh.GetVertex(vid).xy);
            foreach (int eid in new[] { 3, 50, 100, 140 }) {
                Index2i ev = mesh.GetEdgeV(eid);
                pts.Add((mesh.GetVertex(ev.a).xy + mesh.GetVertex(ev.b).xy) * 0.5);
            }
            pts.Add(new Vector2d(3.3, 4.6));
            pts.Add(new Vector2d(-1, 3));
            pts.Add(new Vector2d(9.5, 9.5));
            string text = string.Join(";", pts.Select(p => F(p.x) + "," + F(p.y)));
            var (exit, r) = Run("query2d", path, "--points", text);
            AssertOk(exit, r);
            var results = r["points"].AsArray();
            for (int i = 0; i < pts.Count; ++i) {
                Vector2d p = pts[i];
                int[] expected = mesh.TriangleIndices().Where(t => ContainsExact(mesh, t, p)).OrderBy(t => t).ToArray();
                int[] got = results[i]["hits"].AsArray().Select(h => I(h["triangle"])).ToArray();
                Assert.Equal(expected, got);
                if (expected.Length == 0) {
                    Assert.Null(results[i]["height"]);
                    continue;
                }
                Assert.Equal(expected[0], I(results[i]["triangle"]));
                // the height on the plane of that triangle (barycentric)
                Triangle3d tri = new Triangle3d();
                mesh.GetTriVertices(expected[0], ref tri.V0, ref tri.V1, ref tri.V2);
                Vector3d bary = MathUtil.BarycentricCoords(new Vector2d(p.x, p.y), tri.V0.xy, tri.V1.xy, tri.V2.xy);
                double z = bary.x * tri.V0.z + bary.y * tri.V1.z + bary.z * tri.V2.z;
                Assert.Equal(z, D(results[i]["height"]), 9);
            }
            Assert.True(results.Take(4).All(x => I(x["hits_count"]) >= 1));
            Assert.True(results.Skip(4).Take(4).Count(x => I(x["hits_count"]) == 2) >= 2);    // interior edges: both sides
            Assert.Equal(0, I(results[9]["hits_count"]));
            Assert.Equal(0, I(results[10]["hits_count"]));
        }

        static bool ContainsExact(DMesh3 mesh, int tid, Vector2d p)
        {
            Index3i t = mesh.GetTriangle(tid);
            Vector2d a = mesh.GetVertex(t.a).xy, b = mesh.GetVertex(t.b).xy, c = mesh.GetVertex(t.c).xy;
            int o1 = ExactPredicates.Orient2d(a, b, p), o2 = ExactPredicates.Orient2d(b, c, p), o3 = ExactPredicates.Orient2d(c, a, p);
            return (o1 >= 0 && o2 >= 0 && o3 >= 0) || (o1 <= 0 && o2 <= 0 && o3 <= 0);
        }

        static bool Contains(DMesh3 mesh, int tid, Vector2d p)
        {
            Index3i t = mesh.GetTriangle(tid);
            Vector2d a = mesh.GetVertex(t.a).xy, b = mesh.GetVertex(t.b).xy, c = mesh.GetVertex(t.c).xy;
            double o1 = (b - a).Perp.Dot(p - a), o2 = (c - b).Perp.Dot(p - b), o3 = (a - c).Perp.Dot(p - c);
            return (o1 >= 0 && o2 >= 0 && o3 >= 0) || (o1 <= 0 && o2 <= 0 && o3 <= 0);
        }

        [Fact]
        public void Query2d_Tolerance_TakesTheNearestTriangle()
        {
            // the point (1.5, 0.2) is 0.49 from triangle 0 (z = 100) and 0.1 from triangle 1 (z = 7): the nearest gives the
            // height, although triangle 0 has the lower id; both are hits within the tolerance
            var mesh = new DMesh3();
            foreach (var v in new[] { new Vector3d(0, 0, 100), new Vector3d(1, 0, 100), new Vector3d(0, 1, 100),
                    new Vector3d(1.6, 0, 7), new Vector3d(3, 0, 7), new Vector3d(1.6, 1, 7) })
                mesh.AppendVertex(v);
            mesh.AppendTriangle(0, 1, 2);
            mesh.AppendTriangle(3, 4, 5);
            string path = m.Path_("two_tris.obj");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults).code);
            var (exit, r) = Run("query2d", path, "--points", "1.5,0.2;0.2,0.2;1.55,0.2", "--tolerance", "1", "--max-results", "1");
            AssertOk(exit, r);
            JsonNode p = r["points"][0];
            Assert.Equal(7, D(p["height"]), 12);
            Assert.Equal(1, I(p["triangle"]));
            Assert.False(B(p["exact"]));
            Assert.Equal(2, I(p["hits_count"]));
            Assert.True(B(p["hits_truncated"]));
            // inside triangle 0: exact, whatever the tolerance
            Assert.Equal(100, D(r["points"][1]["height"]), 12);
            Assert.True(B(r["points"][1]["exact"]));
            Assert.Equal(1, I(r["points"][1]["hits_count"]));
            Assert.Equal(7, D(r["points"][2]["height"]), 12);

            // without a tolerance: no height outside
            (exit, r) = Run("query2d", path, "--points", "1.5,0.2");
            AssertOk(exit, r);
            Assert.Null(r["points"][0]["height"]);
            Assert.Null(r["points"][0]["exact"]);
        }

        [Fact]
        public void Query2d_OnAMesh2dResult_SamplesItsHeights()
        {
            // the two commands together: mesh a domain with heights, then sample it
            string output = m.Path_("q_terrain.obj");
            var (exit, r) = Run("mesh2d", "--geometry", "{\"points\": [[0,0],[4,0],[4,4],[0,4]], \"heights\": [0,4,8,4]}", "--output", output, "--max-area", "0.5");
            AssertOk(exit, r);
            (exit, r) = Run("query2d", output, "--points", "1,1;3,2.5;2,2");
            AssertOk(exit, r);
            var pts = r["points"].AsArray();
            Assert.Equal(2, D(pts[0]["height"]), 9);
            Assert.Equal(5.5, D(pts[1]["height"]), 9);
            Assert.Equal(4, D(pts[2]["height"]), 9);
        }

        [Fact]
        public void Query2d_Errors()
        {
            string terrain = PlaneTerrain();
            var (exit, r) = Run("query2d", terrain);
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", terrain, "--rect", "1,1,0,2");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", terrain, "--rect", "1,1,2");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", terrain, "--points", "1,x");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", terrain, "--points", "1,2,3");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", terrain, "--points", "1,1", "--tolerance", "-1");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", terrain, "--points", "1,1", "--nearest", "-1");
            AssertError(exit, r, ResultCodes.InvalidArguments);
            (exit, r) = Run("query2d", m.Path_("missing.obj"), "--points", "1,1");
            AssertError(exit, r, ResultCodes.ReadError);
        }
    }
}
