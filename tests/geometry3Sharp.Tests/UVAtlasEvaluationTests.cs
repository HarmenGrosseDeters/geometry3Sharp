using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The evaluation of a UV atlas (benchmarks/Shared/UvEvaluation.cs, used for both UVAtlasGenerator and xatlas in
    /// benchmarks/UVAtlasBenchmark): exact expected values on a cube whose six faces are charts, the seam, gap and
    /// overlap detection, the atlas file layout, and the invariance over the similarities. All meshes are generated.
    /// </summary>
    public class UVAtlasEvaluationTests
    {
        // quads of the unit cube, counter-clockwise seen from outside; each gives the triangles (q0,q1,q2), (q0,q2,q3)
        static readonly int[][] Quads = {
            new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
            new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };

        static readonly Vector3d[] Corners = {
            new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
            new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0, 1, 1) };

        /// <summary>
        /// The cube as input (8 vertices, 12 triangles) and as atlas: every face is one chart with its own vertices, a
        /// square of side(k) at origin(k) in the unit square (mirror: the square is mirrored, so its triangles are flipped).
        /// </summary>
        static UvEvaluation.Atlas CubeAtlas(out DMesh3 input, Func<int, Vector2d> origin, Func<int, double> side,
            int mirror = -1, int overlapWith = -1, bool dropLastFace = false, bool reverseOrder = false)
        {
            input = new DMesh3(MeshComponents.None);
            foreach (Vector3d c in Corners) input.AppendVertex(c);
            var atlas = new DMesh3(MeshComponents.None);
            var uv = new List<Vector2d>();
            var triangles = new List<Index3i>();
            for (int k = 0; k < 6; ++k) {
                int[] q = Quads[k];
                foreach (Index3i t in new[] { new Index3i(q[0], q[1], q[2]), new Index3i(q[0], q[2], q[3]) }) {
                    input.AppendTriangle(t);
                    triangles.Add(t);
                }
                int b = atlas.VertexCount;
                Vector2d o = origin(k);
                double s = side(k);
                // the square's corners in the order of the quad: standard, or mirrored
                Vector2d[] pattern = k == mirror
                    ? new[] { new Vector2d(0, 0), new Vector2d(0, 1), new Vector2d(1, 1), new Vector2d(1, 0) }
                    : new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1) };
                if (k == overlapWith) o = origin(0);
                for (int i = 0; i < 4; ++i) {
                    atlas.AppendVertex(input.GetVertex(q[i]));
                    uv.Add(o + s * pattern[i]);
                }
                if (dropLastFace && k == 5) continue;
                atlas.AppendTriangle(b, b + 1, b + 2);
                atlas.AppendTriangle(b, b + 2, b + 3);
            }
            if (reverseOrder) {
                // the same triangles in reverse order: the faces no longer match the input at their index
                var rev = new DMesh3(MeshComponents.None);
                foreach (Vector3d v in atlas.VertexIndices().Select(v => atlas.GetVertex(v))) rev.AppendVertex(v);
                foreach (int t in atlas.TriangleIndices().Reverse()) rev.AppendTriangle(atlas.GetTriangle(t));
                atlas = rev;
            }
            return new UvEvaluation.Atlas { Mesh = atlas, UV = uv.ToArray() };
        }

        // six squares in a 3 x 2 grid of the unit square, 0.25 wide: 51 pixels apart (more than the gap cap)
        static Vector2d GridOrigin(int k) => new Vector2d(0.05 + 0.3 * (k % 3), 0.1 + 0.45 * (k / 3));
        static double GridSide(int k) => 0.25;

        static double Num(Dictionary<string, object> r, string key) => Convert.ToDouble(r[key]);

        [Fact]
        public void Cube_charts_have_the_expected_metrics()
        {
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide);
            Dictionary<string, object> r = UvEvaluation.Evaluate(input, a);
            Assert.Equal("ok", r["status"]);
            Assert.Equal(12, (int)r["faces"]);
            Assert.Equal(0, (int)r["overlap_pairs"]);
            Assert.Equal(0, (int)r["inverted"]);
            Assert.Equal(6, (int)r["charts"]);
            Assert.Equal(6, (int)r["discs"]);
            // the 12 cube edges separate two faces (each in its own chart); the diagonals lie inside a chart
            Assert.Equal(12 / Math.Sqrt(6), Num(r, "seam_rel"), 12);
            Assert.Equal(12.0, Num(r, "seam_length"), 12);
            // a square of side 0.25 is a similarity of the face: conformal 1, area distortion 0, equal density
            Assert.Equal(1.0, Num(r, "mean_conformal"), 9);
            Assert.Equal(0.0, Num(r, "mean_area"), 9);
            Assert.Equal(0.0, Num(r, "density_logstd"), 9);
            // six squares of area 0.0625 on the raster (pixel centres): about 0.375
            Assert.InRange(Num(r, "coverage"), 0.36, 0.39);
            // the squares are 51 pixels apart, more than the gap cap: reported as the cap + 1
            Assert.Equal(UvEvaluation.GapCap + 1, (int)r["min_gap_px"]);
        }

        [Fact]
        public void Touching_charts_have_gap_1_and_a_3_pixel_gap_has_gap_4()
        {
            // face 0 and face 1 side by side; the other faces are small and far away
            Func<int, Vector2d> origin = k => k == 0 ? new Vector2d(0.1, 0.1) : k == 1 ? new Vector2d(0.3, 0.1)
                : new Vector2d(0.6 + 0.1 * (k % 2), 0.1 + 0.1 * (k / 2));
            Func<int, double> side = k => k <= 1 ? 0.2 : 0.05;
            Dictionary<string, object> touch = UvEvaluation.Evaluate(
                CubeInput(), CubeAtlas(out DMesh3 _, origin, side));
            Assert.Equal(1, (int)touch["min_gap_px"]);

            // a gap of 3 pixels between the squares: the nearest pixel centres are 4 pixels apart
            Func<int, Vector2d> apart = k => k == 1 ? new Vector2d(0.3 + 3.0 / UvEvaluation.RasterSize, 0.1) : origin(k);
            Dictionary<string, object> gap = UvEvaluation.Evaluate(
                CubeInput(), CubeAtlas(out DMesh3 _, apart, side));
            Assert.Equal(4, (int)gap["min_gap_px"]);
        }

        [Fact]
        public void Overlapping_charts_are_reported_exactly()
        {
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide, overlapWith: 1);
            Dictionary<string, object> r = UvEvaluation.Evaluate(input, a);
            Assert.Equal("ok", r["status"]);
            Assert.True((int)r["overlap_pairs"] > 0);
            Assert.Equal(0, (int)r["min_gap_px"]);
        }

        [Fact]
        public void Mirrored_chart_is_reported_as_flipped()
        {
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide, mirror: 2);
            Dictionary<string, object> r = UvEvaluation.Evaluate(input, a);
            Assert.Equal("ok", r["status"]);
            Assert.Equal(2, (int)r["inverted"]);
        }

        [Fact]
        public void Atlas_with_a_missing_face_is_incomplete()
        {
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide, dropLastFace: true);
            Dictionary<string, object> r = UvEvaluation.Evaluate(input, a);
            Assert.Equal("incomplete", r["status"]);
            Assert.Equal(10, (int)r["atlas_faces"]);
        }

        [Fact]
        public void Atlas_whose_faces_are_not_in_input_order_is_a_correspondence_mismatch()
        {
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide, reverseOrder: true);
            Dictionary<string, object> r = UvEvaluation.Evaluate(input, a);
            Assert.Equal("correspondence_mismatch", r["status"]);
            Assert.True((int)r["mismatched_faces"] > 0);
        }

        [Fact]
        public void Atlas_file_round_trips_and_gives_the_same_metrics()
        {
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide);
            string path = Path.GetTempFileName();
            try {
                UvEvaluation.WriteAtlas(path, a);
                UvEvaluation.Atlas back = UvEvaluation.ReadObj(path);
                Assert.Equal(a.Mesh.TriangleCount, back.Mesh.TriangleCount);
                Assert.Equal(a.UV.Length, back.UV.Length);
                Assert.Equal(UvEvaluation.Evaluate(input, a), UvEvaluation.Evaluate(input, back));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Atlas_file_with_a_uv_index_different_from_the_vertex_index_is_rejected()
        {
            string path = Path.GetTempFileName();
            try {
                File.WriteAllLines(path, new[] { "v 0 0 0", "v 1 0 0", "v 0 1 0", "vt 0 0", "vt 1 0", "vt 0 1", "f 1/2 2/2 3/3" });
                Assert.Throws<FormatException>(() => UvEvaluation.ReadObj(path));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Atlas_of_UVAtlasGenerator_on_a_sphere_is_valid_and_evaluates_the_same_after_a_file_round_trip()
        {
            DMesh3 input = UVAtlasTests.Sphere(10);
            var gen = new UVAtlasGenerator(input);
            gen.Compute();
            Assert.True(gen.Succeeded);
            UvEvaluation.Atlas a = UvEvaluation.FromUVAtlas(input, gen);
            Dictionary<string, object> r = UvEvaluation.Evaluate(input, a);
            Assert.Equal("ok", r["status"]);
            Assert.Equal(0, (int)r["overlap_pairs"]);
            Assert.Equal(0, (int)r["inverted"]);
            Assert.Equal((int)r["components"], (int)r["discs"]);
            Assert.InRange(Num(r, "coverage"), 0.05, 1.0);
            Assert.True(double.IsFinite(Num(r, "mean_conformal")));
            Assert.True(double.IsFinite(Num(r, "seam_rel")));

            string path = Path.GetTempFileName();
            try {
                UvEvaluation.WriteAtlas(path, a);
                Assert.Equal(r, UvEvaluation.Evaluate(input, UvEvaluation.ReadObj(path)));
            } finally {
                File.Delete(path);
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Evaluation_is_invariant_under_the_similarities(string name)
        {
            Similarity s = Similarity.All().First(x => x.Name == name);
            UvEvaluation.Atlas a = CubeAtlas(out DMesh3 input, GridOrigin, GridSide);
            Dictionary<string, object> reference = UvEvaluation.Evaluate(input, a);

            // the same cube and atlas, moved by the similarity in 3D (the UVs are the same)
            DMesh3 movedInput = new DMesh3(input, false, MeshComponents.None);
            foreach (int v in input.VertexIndices()) movedInput.SetVertex(v, s.Apply(input.GetVertex(v)));
            DMesh3 movedAtlas = new DMesh3(a.Mesh, false, MeshComponents.None);
            foreach (int v in a.Mesh.VertexIndices()) movedAtlas.SetVertex(v, s.Apply(a.Mesh.GetVertex(v)));
            var moved = new UvEvaluation.Atlas { Mesh = movedAtlas, UV = a.UV };
            Dictionary<string, object> r = UvEvaluation.Evaluate(movedInput, moved);

            Assert.Equal(reference.Keys.OrderBy(k => k), r.Keys.OrderBy(k => k));
            foreach (string key in reference.Keys) {
                // the area and the seam length scale with the mesh (by s^2 and s): not invariant
                if (key == "area" || key == "seam_length") continue;
                if (reference[key] is string || reference[key] is int) {
                    Assert.Equal(reference[key], r[key]);
                    continue;
                }
                double x = Convert.ToDouble(reference[key]), y = Convert.ToDouble(r[key]);
                // a power of two scale and no rotation or translation keeps every value bit for bit; the others
                // only up to the rounding of the 3D positions: the UTM-like offsets of the similarities lose about
                // 1e-7 relative in ParameterizationDistortion's frames (mean conformal at a scale of 1e-3)
                if (s.IsExact) Assert.Equal(x, y);
                else Assert.True(Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Abs(x)), key + ": " + x + " vs " + y);
            }
        }

        static DMesh3 CubeInput()
        {
            var m = new DMesh3(MeshComponents.None);
            foreach (Vector3d c in Corners) m.AppendVertex(c);
            foreach (int[] q in Quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }
    }
}
