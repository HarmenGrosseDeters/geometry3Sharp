using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #621: with Remesher.MinTriangleQuality, procedural cones and tapers ended with more self-intersecting pairs
    /// than without it, all at the bottom rim where the flat cap meets the side. Every such pair involved a fold: a side
    /// triangle with its corners on the rim (two on it and one projected onto the cap at coarse targets) lying in the
    /// cap's plane, facing up, over the cap's triangles, which face down. Flips (a new edge joining two rim vertices
    /// across a third) and collapses created it without flipping a normal, so the normal checks passed it. The remesher
    /// now tests a new triangle with two or more feature vertices exactly against the triangles around its corners.
    /// Which pairs survive the last pass depends on rounding (a small translation changes the count from 0 to 22 on
    /// master, with and without the option), so the cases are also checked under translations.
    /// </summary>
    public class RemesherRimFoldTests
    {
        static DMesh3 Cone(int slices) =>
            new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Taper(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.3f, Height = 1f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();

        static double MeanEdge(DMesh3 m) =>
            m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        static double Q(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            return MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
        }

        // "cone24_x0.3": the input and the target edge length (0.3 x its mean edge), as in benchmarks/RemeshQualityBenchmark
        static (DMesh3 input, double edge) Case(string name)
        {
            bool cone = name.StartsWith("cone");
            string[] parts = name.Substring(cone ? 4 : 5).Split(new[] { "_x" }, StringSplitOptions.None);
            DMesh3 input = cone ? Cone(int.Parse(parts[0])) : Taper(int.Parse(parts[0]));
            return (input, double.Parse(parts[1], CultureInfo.InvariantCulture) * MeanEdge(input));
        }

        // as g3cli remesh with its defaults (20 passes, smoothing 0.5, projection onto the input, 30 degree creases)
        static DMesh3 Remesh(DMesh3 input, double edge, double minQuality, bool parallel = true, bool pro = false, double pinAngle = 0)
        {
            DMesh3 mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = minQuality;
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            if (pinAngle > 0)
                MeshConstraintUtil.PinSharpVertices(r, pinAngle);
            r.SetTargetEdgeLength(edge);
            for (int k = 0; k < 20; ++k) {
                if (pro) ((RemesherPro)r).RemeshIteration();
                else r.BasicRemeshPass();
            }
            return mesh;
        }

        static DMesh3 Translated(DMesh3 m, Vector3d t)
        {
            var copy = new DMesh3(m);
            foreach (int v in copy.VertexIndices())
                copy.SetVertex(v, copy.GetVertex(v) + t);
            return copy;
        }

        // closed, no bowties, oriented consistently, no flat triangles, finite
        static void AssertValid(DMesh3 m, string name)
        {
            Assert.True(m.IsClosed(), name);
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(m));
            Assert.True(m.Vertices().All(p => p.IsFinite), name);
            double qmin = m.TriangleIndices().Min(t => Q(m, t));
            Assert.True(qmin >= 1e-3, $"{name}: a triangle of quality {qmin}");
        }

        // the nine cases of the issue (master: 12 -> 16, 0 -> 12, 0 -> 12, 0 -> 8, 0 -> 10, 0 -> 6, 0 -> 10, 0 -> 31, 0 -> 8
        // pairs without -> with the option) and cone12_x0.15 (22 -> 8; a guard of an earlier attempt gave 503 there)
        [Theory]
        [InlineData("taper10_x0.15")]
        [InlineData("taper16_x0.1")]
        [InlineData("taper32_x0.1")]
        [InlineData("taper32_x0.6")]
        [InlineData("cone16_x0.45")]
        [InlineData("cone24_x0.15")]
        [InlineData("cone24_x0.3")]
        [InlineData("cone24_x0.6")]
        [InlineData("cone32_x0.6")]
        [InlineData("cone12_x0.15")]
        public void Rim_NoNewSelfIntersections(string name)
        {
            var (input, edge) = Case(name);
            DMesh3 before = Remesh(input, edge, 0), after = Remesh(input, edge, 0.2);
            int pairsBefore = MeshRepairMetrics.SelfIntersections(before), pairsAfter = MeshRepairMetrics.SelfIntersections(after);
            Assert.True(pairsAfter == 0, $"{name}: {pairsAfter} self-intersecting pairs with the option, {pairsBefore} without");
            AssertValid(after, name);
            // on the input as closely as without the option (cones: both round off the apex, which is no feature)
            var hd = new MeshHausdorffDistance(input, after) { Tolerance = 0.01 * edge };
            var hd0 = new MeshHausdorffDistance(input, before) { Tolerance = 0.01 * edge };
            Assert.True(hd.Compute() && hd0.Compute());
            Assert.True(hd.UpperBound <= hd0.UpperBound + 0.5 * edge, $"{name}: {hd.UpperBound / edge} edges from the input, {hd0.UpperBound / edge} without the option");
        }

        // which pairs survive the last pass depends on rounding: the same cases moved by small translations (master:
        // 44, 54 and 248 pairs in the 8 runs of each with the option, 20, 0 and 0 without it)
        [Theory]
        [InlineData("cone24_x0.3", false)]
        [InlineData("cone16_x0.45", false)]
        [InlineData("cone24_x0.6", false)]
        [InlineData("cone24_x0.3", true)]
        [InlineData("cone16_x0.45", true)]
        public void Rim_NoSelfIntersections_UnderTranslations(string name, bool pro)
        {
            var (input, edge) = Case(name);
            for (int k = 0; k < 8; ++k) {
                var t = new Vector3d(0.37 * k, -0.61 * k, 0.23 * k);
                DMesh3 moved = Translated(input, t);
                DMesh3 m = Remesh(moved, edge, 0.2, true, pro);
                int pairs = MeshRepairMetrics.SelfIntersections(m);
                Assert.True(pairs == 0, $"{name}, translation {k}{(pro ? ", RemesherPro" : "")}: {pairs} self-intersecting pairs");
                AssertValid(m, name);
            }
        }

        // with PinSharpVertices (30 degrees: the apex and, on cone12, the rim vertices pinned). master: 0, 30 and 24 pairs
        // in the 4 runs of each; cone12_x0.3 ended with 264 pairs when the fold check also rejected the flips on the
        // coarse input (the edges from the rim to the apex)
        [Theory]
        [InlineData("cone12_x0.3")]
        [InlineData("cone24_x0.3")]
        [InlineData("cone16_x0.45")]
        public void Rim_Pinned_NoSelfIntersections_UnderTranslations(string name)
        {
            var (input, edge) = Case(name);
            for (int k = 0; k < 4; ++k) {
                var t = new Vector3d(0.37 * k, -0.61 * k, 0.23 * k);
                DMesh3 m = Remesh(Translated(input, t), edge, 0.2, true, false, 30);
                int pairs = MeshRepairMetrics.SelfIntersections(m);
                Assert.True(pairs == 0, $"{name}, translation {k}, pinned: {pairs} self-intersecting pairs");
                AssertValid(m, name);
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Rim_SameResultUnderSimilarity(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            foreach (string name in new[] { "cone24_x0.3", "taper32_x0.6" }) {
                var (input, edge) = Case(name);
                DMesh3 m0 = Remesh(input, edge, 0.2, false);
                DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(input, s), s.ApplyLength(edge), 0.2, false), s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(m0, back));
                // otherwise rounding tips decisions at the ties of these symmetric inputs, and the passes amplify them
                // (cone24_x0.3 ends about 0.3 edges apart under any translation, with and without the option): the
                // result is checked, not compared vertex by vertex
                int pairs = MeshRepairMetrics.SelfIntersections(back);
                Assert.True(pairs == 0, $"{name}, {s.Name}: {pairs} self-intersecting pairs");
                AssertValid(back, $"{name}, {s.Name}");
                var hd = new MeshHausdorffDistance(input, back) { Tolerance = 0.01 * edge };
                var hd0 = new MeshHausdorffDistance(input, m0) { Tolerance = 0.01 * edge };
                Assert.True(hd.Compute() && hd0.Compute());
                Assert.True(hd.UpperBound <= hd0.UpperBound + 0.5 * edge, $"{name}, {s.Name}: {hd.UpperBound / edge} edges from the input, {hd0.UpperBound / edge} untransformed");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Rim_ParallelEqualsSerial_RepeatedRuns(bool pro)
        {
            var (input, edge) = Case("cone24_x0.3");
            DMesh3 serial = Remesh(input, edge, 0.2, false, pro);
            for (int k = 0; k < 2; ++k)
                Assert.Equal("", Invariance.Difference(serial, Remesh(input, edge, 0.2, true, pro)));
        }
    }
}
