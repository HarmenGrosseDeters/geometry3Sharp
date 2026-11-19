using System;
using System.Collections.Generic;
using System.Linq;
using BoolCmp;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The eval's diagnostic of inside-out components (benchmarks/BooleanComparison/InsideOutDiagnostics.cs, issue #1067): per closed
    /// inside-out component the numbers that the component removal of SafeRounding judges (tiny, thin, exact volume sign, the nesting
    /// verdict of FindInvertedComponents) and what the removal would do, so that the reason why a residue stays can be read off a
    /// corpus row. Procedural meshes, no data.
    /// </summary>
    public class BooleanComparisonInsideOutDiagnosticsTests
    {
        // double cone over an n-gon (radius r, apexes at +-h): area and volume from the formulas, not from the mesh
        static double ConeArea(int n, double r, double h) => 2 * n * 0.5 * (2 * r * Math.Sin(Math.PI / n)) * Math.Sqrt(h * h + Math.Pow(r * Math.Cos(Math.PI / n), 2));
        static double ConeVolume(int n, double r, double h) => 2.0 / 3 * (0.5 * n * r * r * Math.Sin(2 * Math.PI / n)) * h;

        static DMesh3 Sphere() => SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(new DMesh3());

        static DMesh3 With(DMesh3 solid, DMesh3 other)
        {
            var m = new DMesh3(solid);
            new MeshEditor(m).AppendMesh(other);
            return m;
        }

        static DMesh3 Residue(double r = 1e-4, double h = 1e-8) => SafeRoundingInsideOutResidueTests.Bipyramid(17, r, h, new Vector3d(3, 0, 0), true);

        static T Get<T>(Dictionary<string, object> row, string key) => (T)row[key];

        /// <summary> the residue of the safe-rounding test (34 triangles, inverted, tiny, thin) beside a solid: one row, every field as computed from the formulas </summary>
        [Fact]
        public void TinyInvertedResidue_AllFieldsRead()
        {
            var rows = InsideOutDiagnostics.Describe(With(Sphere(), Residue()));
            var r = Assert.Single(rows);
            Assert.Equal(34, Get<int>(r, "tris"));
            Assert.False(Get<bool>(r, "is_whole_result"));
            Assert.Equal(-1, Get<int>(r, "volume_sign"));
            Assert.True(Get<bool>(r, "flagged"));
            Assert.True(Get<bool>(r, "tiny"));
            Assert.True(Get<bool>(r, "thin"));
            Assert.True(Get<bool>(r, "would_remove_if_untouched"));
            Assert.True(Get<bool>(r, "would_remove_if_touched"));
            Assert.Equal(ConeArea(17, 1e-4, 1e-8), Get<double>(r, "area"), 1e-6 * ConeArea(17, 1e-4, 1e-8));
            Assert.Equal(ConeVolume(17, 1e-4, 1e-8), Get<double>(r, "abs_volume"), 1e-6 * ConeVolume(17, 1e-4, 1e-8));
            // the size is the sphere's diagonal (the box of a radius 1 sphere with its needles: 3.4 to 3.5), the bound 1e-8 size^2
            double size = Get<double>(r, "size");
            Assert.InRange(size, 3.4, 3.5);
            Assert.Equal(1e-8 * size * size, Get<double>(r, "tiny_bound"), 1e-9 * 1e-8 * size * size);
            Assert.Equal(1e-5 * size, Get<double>(r, "max_length"), 1e-12 * size);
            Assert.InRange(Get<double>(r, "box_diagonal"), 2.8e-4, 2.9e-4);   // sqrt(2e-4^2 + 2e-4^2 + 2e-8^2)
            Assert.True(Get<double>(r, "min_tri_area") > 0);
        }

        /// <summary> a residue alone is the whole result (the veto of KeepWhenEmptied would apply); its size is its own diagonal, so it is not tiny against itself </summary>
        [Fact]
        public void ResidueAlone_IsTheWholeResult()
        {
            var r = Assert.Single(InsideOutDiagnostics.Describe(Residue()));
            Assert.True(Get<bool>(r, "is_whole_result"));
            Assert.Equal(Get<double>(r, "box_diagonal"), Get<double>(r, "size"));
        }

        /// <summary>
        /// A plate that is not tiny but thin (the case that #1152 leaves): tiny false, thin true, flagged, a small volume ratio; the
        /// removal would take it only if an edit had touched it, never as untouched
        /// </summary>
        [Fact]
        public void ThinInvertedPlateThatIsNotTiny_TinyFalseThinTrue()
        {
            var r = Assert.Single(InsideOutDiagnostics.Describe(With(Sphere(), Residue(0.05, 1e-6))));
            Assert.False(Get<bool>(r, "tiny"));
            Assert.True(Get<bool>(r, "thin"));
            Assert.True(Get<bool>(r, "flagged"));
            Assert.False(Get<bool>(r, "would_remove_if_untouched"));
            Assert.True(Get<bool>(r, "would_remove_if_touched"));
            Assert.True(Get<double>(r, "area") > Get<double>(r, "tiny_bound"));
            // the removal keeps a component with |V| >= 1e-3 area^1.5 (not tiny): the ratio is far below
            Assert.True(Get<double>(r, "volume_ratio") < 1e-3);
            Assert.Equal(2 * ConeVolume(17, 0.05, 1e-6) / ConeArea(17, 0.05, 1e-6), Get<double>(r, "thickness"), 1e-9 * Get<double>(r, "thickness"));
        }

        /// <summary>
        /// Thin by the bound but holding real volume for its area (|V| at least 1e-3 area^1.5, not tiny): the removal keeps such a
        /// component even if an edit touched it (it is counted as ScaleDisparity instead), so would_remove_if_touched is false
        /// </summary>
        [Fact]
        public void ThinByTheBoundWithRealVolume_IsKeptEvenIfTouched()
        {
            var r = Assert.Single(InsideOutDiagnostics.Describe(With(Sphere(), Residue(4e-3, 4e-5))));
            Assert.False(Get<bool>(r, "tiny"));
            Assert.True(Get<bool>(r, "thin"));
            Assert.True(Get<double>(r, "volume_ratio") >= 1e-3);
            Assert.False(Get<bool>(r, "would_remove_if_touched"));
            Assert.False(Get<bool>(r, "would_remove_if_untouched"));
        }

        /// <summary> a thick inverted solid that is not tiny: not thin, so the removal keeps it whether touched or not (it holds real volume) </summary>
        [Fact]
        public void ThickInvertedSolid_NotRemovedEitherWay()
        {
            var r = Assert.Single(InsideOutDiagnostics.Describe(With(Sphere(), SafeRoundingInsideOutResidueTests.Bipyramid(17, 0.1, 0.1, new Vector3d(3, 0, 0), true))));
            Assert.False(Get<bool>(r, "tiny"));
            Assert.False(Get<bool>(r, "thin"));
            Assert.False(Get<bool>(r, "would_remove_if_untouched"));
            Assert.False(Get<bool>(r, "would_remove_if_touched"));
            Assert.True(Get<double>(r, "volume_ratio") > 1e-3);
        }

        /// <summary>
        /// The thin test at its boundary: a plate whose thickness (2 |V| / area) is 1.5 times max_length is not thin, one of 0.7 times
        /// is (the thin test of the removal is 2 |V| below max_length * area)
        /// </summary>
        [Fact]
        public void ThinTest_AtItsBoundary()
        {
            Dictionary<string, object> At(double h) => Assert.Single(InsideOutDiagnostics.Describe(With(Sphere(), Residue(0.05, h))));
            double maxLength = Get<double>(At(1e-4), "max_length"), thickness = 2 * ConeVolume(17, 0.05, 1e-4) / ConeArea(17, 0.05, 1e-4);
            foreach (var (factor, thin) in new[] { (1.5, false), (0.7, true) }) {
                double h = 1e-4 * factor * maxLength / thickness;
                var r = At(h);
                Assert.Equal(factor * maxLength, 2 * ConeVolume(17, 0.05, h) / ConeArea(17, 0.05, h), 1e-3 * maxLength);
                Assert.Equal(thin, Get<bool>(r, "thin"));
            }
        }

        /// <summary> the same shell inside the solid is a cavity: inverted by the sign, but not flagged (the nesting), so not removed even if untouched </summary>
        [Fact]
        public void EnclosedCavity_InvertedButNotFlagged()
        {
            var r = Assert.Single(InsideOutDiagnostics.Describe(With(Sphere(), SafeRoundingInsideOutResidueTests.Bipyramid(17, 1e-4, 1e-8, new Vector3d(0.1, 0.05, 0), true))));
            Assert.Equal(-1, Get<int>(r, "volume_sign"));
            Assert.False(Get<bool>(r, "flagged"));
            Assert.False(Get<bool>(r, "would_remove_if_untouched"));
        }

        /// <summary> an open inverted shell has no volume sign and is not described; a mesh of open components only says so in one row </summary>
        [Fact]
        public void OpenShell_IsNotDescribed()
        {
            DMesh3 residue = Residue();
            residue.RemoveTriangle(residue.TriangleIndices().First());
            Assert.Empty(InsideOutDiagnostics.Describe(With(Sphere(), residue)));
            var only = Assert.Single(InsideOutDiagnostics.Describe(residue));
            Assert.True(Get<bool>(only, "open_components_only"));
        }

        /// <summary> a correctly oriented mesh, with a tiny solid beside it, has no row; neither has an empty mesh or none </summary>
        [Fact]
        public void CorrectlyOrientedAndEmpty_NoRows()
        {
            Assert.Empty(InsideOutDiagnostics.Describe(With(Sphere(), SafeRoundingInsideOutResidueTests.Bipyramid(17, 1e-4, 1e-8, new Vector3d(3, 0, 0), false))));
            Assert.Empty(InsideOutDiagnostics.Describe(new DMesh3()));
            Assert.Empty(InsideOutDiagnostics.Describe(null));
        }

        /// <summary>
        /// The diagnostic against the real removal: for a mesh with a defect that the repair edits, the residue it calls
        /// would_remove_if_untouched is removed by MeshRoundingRepair, and the thin plate it does not is kept
        /// </summary>
        [Fact]
        public void AgreesWithTheRealRemoval()
        {
            foreach (var (residue, removed) in new[] { (Residue(), true), (Residue(0.05, 1e-6), false) }) {
                DMesh3 input = SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(residue);
                var row = Assert.Single(InsideOutDiagnostics.Describe(input));
                var repair = new MeshRoundingRepair(input);
                repair.Apply();
                Assert.Equal(removed, Get<bool>(row, "would_remove_if_untouched"));
                Assert.Equal(removed ? 1 : 0, repair.RemovedComponents);
            }
        }

        /// <summary>
        /// Two lobes that meet at one vertex are two components by edges but one component of the repair (by vertices): the box,
        /// and so the ulp term, is that of both
        /// </summary>
        [Fact]
        public void PinchedLobes_UseTheVertexConnectedBox()
        {
            // the second shell shares the first one's apex vertex
            DMesh3 a = Residue(), b = SafeRoundingInsideOutResidueTests.Bipyramid(17, 1e-4, 1e-8, new Vector3d(3, 0, 5e-4), true);
            var m = new DMesh3();
            var mapA = new Dictionary<int, int>();
            foreach (int v in a.VertexIndices()) mapA[v] = m.AppendVertex(a.GetVertex(v));
            foreach (int t in a.TriangleIndices()) { Index3i f = a.GetTriangle(t); m.AppendTriangle(mapA[f.a], mapA[f.b], mapA[f.c]); }
            int apex = mapA[0];                              // (vertex 0 of a bipyramid is its top apex)
            var mapB = new Dictionary<int, int>();
            foreach (int v in b.VertexIndices()) mapB[v] = v == 1 ? apex : m.AppendVertex(b.GetVertex(v));   // b's bottom apex: moved onto a's top
            foreach (int t in b.TriangleIndices()) { Index3i f = b.GetTriangle(t); m.AppendTriangle(mapB[f.a], mapB[f.b], mapB[f.c]); }
            Assert.Equal(2, MeshRoundingRepair.connected_components(m).Count());
            var rows = InsideOutDiagnostics.Describe(m);
            Assert.Equal(2, rows.Count);
            Assert.Equal(rows[0]["box_diagonal"], rows[1]["box_diagonal"]);
            // both lobes: the diagonal is above that of one shell (2.0e-4 across) by the height they span
            Assert.True(Get<double>(rows[0], "box_diagonal") > 4e-4);
        }

        /// <summary> a value that is not finite is a string in the row (System.Text.Json refuses NaN and infinity in a number), a finite one stays a double </summary>
        [Fact]
        public void NonFiniteNumbers_AreStrings()
        {
            Assert.Equal("Infinity", InsideOutDiagnostics.Num(double.PositiveInfinity));
            Assert.Equal("-Infinity", InsideOutDiagnostics.Num(double.NegativeInfinity));
            Assert.Equal("NaN", InsideOutDiagnostics.Num(double.NaN));
            Assert.IsType<double>(InsideOutDiagnostics.Num(1.5));
            Assert.Equal(1.5, InsideOutDiagnostics.Num(1.5));
        }

        /// <summary> the soup of a result file: a mesh is rebuilt as it is (duplicated vertices stay), an edge with three triangles or a repeated corner is refused </summary>
        [Fact]
        public void FromSoup_BuildsTheMeshAsWritten_AndRefusesWhatIsNotAMesh()
        {
            DMesh3 b = SafeRoundingInsideOutResidueTests.Bipyramid(5, 1, 1, Vector3d.Zero, false);
            var v = b.VertexIndices().Select(b.GetVertex).ToList();
            var t = b.TriangleIndices().Select(b.GetTriangle).ToList();
            DMesh3 m = InsideOutDiagnostics.FromSoup(v, t);
            Assert.NotNull(m);
            Assert.Equal(t.Count, m.TriangleCount);
            Assert.Equal(v.Count, m.VertexCount);
            Assert.Null(InsideOutDiagnostics.FromSoup(v, new List<Index3i>(t) { new Index3i(t[0].a, t[0].b, t[1].c) }));
            Assert.Null(InsideOutDiagnostics.FromSoup(v, new List<Index3i>(t) { new Index3i(0, 0, 1) }));
        }

        /// <summary>
        /// invariance over Similarity.All(): the same verdicts and counts in every frame; in the exact ones (2^k scales) the numbers
        /// are those of the identity frame times the scale, bit for bit
        /// </summary>
        [Fact]
        public void Describe_Invariance()
        {
            DMesh3 input = With(Sphere(), Residue());
            var r0 = Assert.Single(InsideOutDiagnostics.Describe(input));
            foreach (Similarity s in Similarity.All()) {
                var r = Assert.Single(InsideOutDiagnostics.Describe(Invariance.Transformed(input, s)));
                foreach (string k in new[] { "tris", "is_whole_result", "volume_sign", "flagged", "tiny", "thin", "would_remove_if_untouched", "would_remove_if_touched" })
                    Assert.True(r[k].Equals(r0[k]), $"{s.Name}: {k} {r[k]} vs {r0[k]}");
                double sc = s.Scale;
                if (s.IsExact) {
                    Assert.Equal(Get<double>(r0, "area") * sc * sc, Get<double>(r, "area"));
                    Assert.Equal(Get<double>(r0, "abs_volume") * sc * sc * sc, Get<double>(r, "abs_volume"));
                    Assert.Equal(Get<double>(r0, "size") * sc, Get<double>(r, "size"));
                    Assert.Equal(Get<double>(r0, "max_length") * sc, Get<double>(r, "max_length"));
                } else
                    // (a translation far from the origin rounds the coordinates of a 1e-4 shell: far below the margins of the verdicts)
                    Assert.Equal(Get<double>(r0, "area") * sc * sc, Get<double>(r, "area"), 1e-2 * Get<double>(r0, "area") * sc * sc);
            }
        }

        /// <summary> the rows of one mesh twice are the same (no state) </summary>
        [Fact]
        public void Describe_IsRepeatable()
        {
            DMesh3 m = With(With(Sphere(), Residue()), SafeRoundingInsideOutResidueTests.Bipyramid(17, 0.05, 1e-6, new Vector3d(-3, 0, 0), true));
            Func<string> dump = () => string.Join("|", InsideOutDiagnostics.Describe(m).Select(row => string.Join(";", row.Select(kv => kv.Key + "=" + kv.Value))));
            string once = dump();
            Assert.Equal(2, InsideOutDiagnostics.Describe(m).Count);
            Assert.Equal(once, dump());
        }
    }
}
