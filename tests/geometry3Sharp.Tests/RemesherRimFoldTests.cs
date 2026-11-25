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
        // cos and sin (x and z of the ring vertex k of Cone(24)) as the correctly rounded value of Math.Cos / Math.Sin at the
        // generator's float angle. The generator calls the C runtime's sin and cos, which are not correctly rounded: this
        // Windows runtime (net10.0, UCRT) is 1 ulp off at the cos of k = 8 and 22 and the sin of k = 9, other runtimes may
        // differ elsewhere. These cases are decided by single bits (cone24_x0.3 under the "rotated" similarity ends with 3
        // self-intersecting pairs with the runtime's values and 0 with these), so the test pins the bits.
        static readonly (ulong cos, ulong sin)[] Cone24Ring = {
            (0x3FF0000000000000, 0x0000000000000000), (0x3FEEE8DD464598F4, 0x3FD0907DC92156F4),
            (0x3FEBB67AE46F065C, 0x3FE0000006C6422D), (0x3FE6A09E5E333598, 0x3FE6A09E6ECB41FE),
            (0x3FDFFFFFE4E6F746, 0x3FEBB67AF02AD942), (0x3FD0907DD99D2F2B, 0x3FEEE8DD44103ED6),
            (0xBE6777A5CF72CECC, 0x3FEFFFFFFFFFFFF7), (0xBFD0907E3448F3BD, 0x3FEEE8DD37EA7518),
            (0xBFE000001B1908AF, 0x3FEBB67AD8B33368), (0xBFE6A09E68C2AFFE, 0x3FE6A09E643BC79B),
            (0xBFEBB67ADBE6AC24, 0x3FE00000158DAAE8), (0xBFEEE8DD5AB3A10B, 0x3FD0907D30A3D78B),
            (0xBFEFFFFFFFFFFFDE, 0xBE7777A5CF72CEC6), (0xBFEEE8DD42680E44, 0xBFD0907DE5FB6110),
            (0xBFEBB67AACF75FCF, 0xBFE0000066D8C4A1), (0xBFE6A09E26627E0D, 0xBFE6A09EA69BF8D6),
            (0xBFDFFFFF939BDCEA, 0xBFEBB67B07A27EDF), (0xBFD0907D7EF16A76, 0xBFEEE8DD50360852),
            (0x3E499BC5B961B1AD, 0xBFEFFFFFFFFFFFFF), (0x3FD0907D97ADCE48, 0xBFEEE8DD4CE5A73E),
            (0x3FDFFFFFA9C95425, 0xBFEBB67B013B8D85), (0x3FE6A09EE4754105, 0xBFE6A09DE88933D7),
            (0x3FEBB67B335E50CB, 0xBFDFFFFEFC1C6315), (0x3FEEE8DD66D96A0B, 0xBFD0907CD5F81294),
        };

        static DMesh3 Cone(int slices)
        {
            DMesh3 m = new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
            if (slices == 24)
                for (int k = 0; k < 24; ++k)   // BaseRadius is 1, so the ring is (cos, 0, sin)
                    m.SetVertex(k, new Vector3d(BitConverter.Int64BitsToDouble(unchecked((long)Cone24Ring[k].cos)), 0,
                                                BitConverter.Int64BitsToDouble(unchecked((long)Cone24Ring[k].sin))));
            return m;
        }
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
            DMesh3 after = Remesh(input, edge, 0.2);
            int pairs = MeshRepairMetrics.SelfIntersections(after);
            Assert.True(pairs == 0, $"{name}: {pairs} self-intersecting pairs with the option");
            AssertValid(after, name);
            var hd = new MeshHausdorffDistance(input, after) { Tolerance = 0.01 * edge };
            Assert.True(hd.Compute());
            if (name.StartsWith("taper")) {
                // on the input: 0.06..0.23 target in these cases (and without the option)
                Assert.True(hd.UpperBound < 0.3 * edge, $"{name}: {hd.UpperBound / edge} edges from the input");
            } else {
                // a cone's apex is no feature: both variants round it off by 1.4..3.4 target, and which pass ends where
                // is chance (over 8 small translations of the 42 cones of benchmarks/RemeshQualityBenchmark the mean
                // is the same with and without the fold check, single cases differ by up to 0.5 target either way)
                var hd0 = new MeshHausdorffDistance(input, Remesh(input, edge, 0)) { Tolerance = 0.01 * edge };
                Assert.True(hd0.Compute());
                Assert.True(hd.UpperBound <= hd0.UpperBound + 0.5 * edge, $"{name}: {hd.UpperBound / edge} edges from the input, {hd0.UpperBound / edge} without the option");
            }
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

        // with PinSharpVertices (30 degrees: the apex and, on cone12, the rim vertices pinned). master: 0, 0, 0, 30 and 24
        // pairs in the 4 runs of each; cone12_x0.3 ended with 264 pairs when the fold check also rejected the flips on
        // the coarse input (the edges from the rim to the apex), cone12_x0.15 / x0.2 with 6 / 7 when the old triangles
        // were counted at all their corners (an intersection at a corner off the features hid new folds)
        [Theory]
        [InlineData("cone12_x0.15")]
        [InlineData("cone12_x0.2")]
        [InlineData("cone12_x0.3")]
        [InlineData("cone24_x0.3")]
        [InlineData("cone16_x0.45")]
        public void Rim_Pinned_NoSelfIntersections_UnderTranslations(string name)
        {
            var (input, edge) = Case(name);
            for (int k = 0; k < 4; ++k) {
                var t = new Vector3d(0.37 * k, -0.61 * k, 0.23 * k);
                DMesh3 m = Remesh(Translated(input, t), edge, 0.2, true, false, 30);
                // The pairs at the rim (a triangle with a vertex in the cap's plane, y = 0 of the input): the subject of
                // this test. Since the ring check of issue #1441 a cone can end with a few pairs near its pinned apex
                // instead (owner 2026-11-23; cone12_x0.15 untranslated: 2 pairs one edge below the apex; master had
                // pairs in other pinned cases, cone24_x0.15 and cone24_x0.2: docs/roadmap/alpha-wrap-and-repair.md, "Feature
                // detection misses vertex features"). Those must lie within two target edges of the apex (y = 2).
                int rimPairs = 0;
                foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(m)) {
                    bool atRim = false;
                    double lowest = double.MaxValue;
                    foreach (int tid in new[] { pair.a, pair.b }) {
                        Index3i tv = m.GetTriangle(tid);
                        for (int i = 0; i < 3; ++i) {
                            double y = m.GetVertex(tv[i]).y - t.y;
                            if (Math.Abs(y) < 1e-9) atRim = true;
                            lowest = Math.Min(lowest, y);
                        }
                    }
                    if (atRim) rimPairs++;
                    else Assert.True(lowest > 2 - 2 * edge, $"{name}, translation {k}, pinned: a self-intersecting pair {2 - lowest} below the apex, away from the rim");
                }
                Assert.True(rimPairs == 0, $"{name}, translation {k}, pinned: {rimPairs} self-intersecting pairs at the rim");
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


        // a flip of edge B-U at a rim A-B-C (feature vertices) would fold the side onto the cap: the new triangle B-A-C
        // overlaps the cap triangles O-A-B, O-B-C. Fins at U (a corner off the features) already cross the old side
        // triangles; the old triangles are counted at the same (feature) corners as the new ones, so these crossings,
        // which the flip does not change, do not hide the fold
        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public void FoldFlip_Rejected_NotHiddenByCrossingsElsewhere(int fins)
        {
            var m = new DMesh3();
            double c30 = Math.Cos(Math.PI / 6);
            int O = m.AppendVertex(new Vector3d(0, 0, 0)), A = m.AppendVertex(new Vector3d(1, 0, 0)), B = m.AppendVertex(new Vector3d(c30, 0, 0.5)),
                C = m.AppendVertex(new Vector3d(0.5, 0, c30)), U = m.AppendVertex(new Vector3d(0.7, 0.5, 0.4));
            m.AppendTriangle(O, A, B); m.AppendTriangle(O, B, C);
            int tBAU = m.AppendTriangle(B, A, U), tCBU = m.AppendTriangle(C, B, U);
            Vector3d u = m.GetVertex(U), nb = m.GetTriNormal(tBAU), nc = m.GetTriNormal(tCBU);
            Vector3d mid1 = (m.GetVertex(A) + m.GetVertex(B)) / 2, mid2 = (m.GetVertex(B) + m.GetVertex(C)) / 2;
            for (int k = 0; k < fins; ++k) {
                // a thin triangle at U through the inside of a side triangle, facing partly against it
                bool first = k % 2 == 0;
                Vector3d n = first ? nb : nc, p = Vector3d.Lerp(u, first ? mid1 : mid2, 0.5 + 0.1 * (k / 2));
                Vector3d side = (p - u).Cross(n).Normalized;
                Vector3d p1 = p + 0.3 * n + 0.2 * side, p2 = p - 0.3 * n - 0.2 * side;
                int i1 = m.AppendVertex(p1), i2 = m.AppendVertex(p2);
                if (MathUtil.Normal(u, p1, p2).Dot(n) < 0) m.AppendTriangle(U, i1, i2); else m.AppendTriangle(U, i2, i1);
            }
            Assert.Equal(fins == 0 ? 0 : 7, MeshRepairMetrics.SelfIntersections(m));
            var r = new Remesher(m) { MinTriangleQuality = 0.2 };
            var cons = new MeshConstraints();
            var rim = MeshProjectionTarget.Auto(new DMesh3(m));
            foreach (int v in new[] { A, B, C })
                cons.SetOrUpdateVertexConstraint(v, new VertexConstraint(rim));
            r.SetExternalConstraints(cons);
            int e = m.FindEdge(B, U);
            Index2i ev = m.GetEdgeV(e), ov = m.GetEdgeOpposingV(e), et = m.GetEdgeT(e);
            Assert.True(r.flip_creates_feature_fold(ev.a, ev.b, ov.a, ov.b, et.a, et.b, 10));
        }

        // a collapse of the side vertex S onto the rim vertex B close to it, with a fin at S that crosses the side triangle
        // S-A-U: after the collapse the fin crosses B-A-U the same way. The collapse adds no crossing, so it is not
        // rejected: the triangles before it are counted with S as a feature corner, as B is after it (counted without
        // S, the crossing would look new and block the collapse)
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Collapse_ExistingCrossingMoved_NotRejected(bool fin)
        {
            var m = new DMesh3();
            double c30 = Math.Cos(Math.PI / 6);
            int O = m.AppendVertex(new Vector3d(0, 0, 0)), A = m.AppendVertex(new Vector3d(1, 0, 0)), B = m.AppendVertex(new Vector3d(c30, 0, 0.5)),
                C = m.AppendVertex(new Vector3d(0.5, 0, c30)), U = m.AppendVertex(new Vector3d(0.6, 0.6, 0.4)),
                S = m.AppendVertex(new Vector3d(0.98 * c30, 0.03, 0.98 * 0.5));
            m.AppendTriangle(O, A, B); m.AppendTriangle(O, B, C);
            m.AppendTriangle(B, A, S); m.AppendTriangle(C, B, S);
            int tSAU = m.AppendTriangle(S, A, U);
            m.AppendTriangle(S, U, C);
            if (fin) {
                Vector3d s = m.GetVertex(S), n = m.GetTriNormal(tSAU), p = Vector3d.Lerp(s, (m.GetVertex(A) + m.GetVertex(U)) / 2, 0.5);
                Vector3d side = (p - s).Cross(n).Normalized, p1 = p + 0.3 * n + 0.2 * side, p2 = p - 0.3 * n - 0.2 * side;
                int i1 = m.AppendVertex(p1), i2 = m.AppendVertex(p2);
                if (MathUtil.Normal(s, p1, p2).Dot(n) < 0) m.AppendTriangle(S, i1, i2); else m.AppendTriangle(S, i2, i1);
            }
            Assert.Equal(fin ? 1 : 0, MeshRepairMetrics.SelfIntersections(m));
            var r = new Remesher(m) { MinTriangleQuality = 0.2 };
            var cons = new MeshConstraints();
            var rim = MeshProjectionTarget.Auto(new DMesh3(m));
            foreach (int v in new[] { A, B, C })
                cons.SetOrUpdateVertexConstraint(v, new VertexConstraint(rim));
            r.SetExternalConstraints(cons);
            Index2i et = m.GetEdgeT(m.FindEdge(S, B));
            Assert.False(r.collapse_creates_feature_fold(S, B, B, m.GetVertex(B), et.a, et.b));
        }

        // one Remesher instance used twice (the mesh reset to the input, the constraints set again): the same result
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Rim_ReusedRemesher_SameResult(bool pro)
        {
            var (input, edge) = Case("cone24_x0.3");
            DMesh3 mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = 0.2;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            var results = new List<DMesh3>();
            for (int run = 0; run < 2; ++run) {
                mesh.Copy(input);
                r.SetExternalConstraints(new MeshConstraints());
                MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
                r.SetTargetEdgeLength(edge);
                if (pro) ((RemesherPro)r).ResetQueue();
                for (int k = 0; k < 20; ++k) {
                    if (pro) ((RemesherPro)r).RemeshIteration();
                    else r.BasicRemeshPass();
                }
                results.Add(new DMesh3(mesh));
            }
            Assert.Equal("", Invariance.Difference(results[0], results[1]));
            Assert.Equal("", Invariance.Difference(results[0], Remesh(input, edge, 0.2, true, pro)));
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
