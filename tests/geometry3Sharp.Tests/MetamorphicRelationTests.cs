using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Meshing2Acceptance;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The metamorphic relations A1 (anisotropic scale: flatten or stretch one axis by 2^e) and P1 (tiny perturbation: one coordinate
    /// of smallest magnitude set to 2^e) of docs/research/METAMORPHIC_TESTING.md, sections 5 to 7. A1 may change the result of an
    /// operation (a flat input is a different input), but not its cost or its validity: the output counts stay within a factor of the
    /// unflattened reference (or under a floor), and a result that is clean for the unflattened input is clean for the flattened one.
    /// P1 may change a classification or a value by at most the shift, which is far below the features. Each relation runs a few
    /// exponents in the PR tier and the full sweep in the nightly tier (Category=Nightly). The inputs are procedural.
    /// </summary>
    public class MetamorphicRelationTests
    {
        /// <summary>The floor of a count: a few hundred wrap triangles are no cost (the #712 test's threshold is 5000).</summary>
        const long Floor = 5000;

        static DMesh3 Tetrahedron()
        {
            var m = new DMesh3();
            foreach (var p in new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) })
                m.AppendVertex(p);
            m.AppendTriangle(0, 2, 1);
            m.AppendTriangle(0, 1, 3);
            m.AppendTriangle(0, 3, 2);
            m.AppendTriangle(1, 2, 3);
            return m;
        }

        static DMesh3 Sphere(double radius, int edges)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edges }.Generate().MakeDMesh();
            return m;
        }

        // ---------------------------------------------------------------- the helpers

        [Fact]
        public void Stretch_IsExact_AndMovesOnlyOneAxis()
        {
            DMesh3 m = Tetrahedron();
            DMesh3 f = MetamorphicRelations.Stretch(m, 0, -46);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v), q = f.GetVertex(v);
                Assert.Equal(p.x * Math.Pow(2, -46), q.x);
                Assert.Equal(p.y, q.y);
                Assert.Equal(p.z, q.z);
            }
            Assert.Equal(1.0, m.GetVertex(1).x);   // the input is not changed
        }

        [Fact]
        public void SmallestCoordinate_ReplacesOnlyTheSmallestOne()
        {
            DMesh3 m = Tetrahedron();   // the origin has three zero coordinates: the first zero of the first vertex is taken
            DMesh3 p = MetamorphicRelations.SmallestCoordinate(m, -1000);
            Assert.Equal(Math.Pow(2, -1000), p.GetVertex(0).x);
            Assert.Equal(0.0, p.GetVertex(0).y);
            Assert.Equal(0.0, p.GetVertex(0).z);
            for (int v = 1; v < 4; ++v)
                Assert.Equal(m.GetVertex(v), p.GetVertex(v));
        }

        [Fact]
        public void CountBound_AcceptsGrowthUnderTheFloor_AndWithinAFactor_RejectsMore()
        {
            Assert.Equal("", MetamorphicRelations.CountBound("n", 0, 4000, Floor));    // under the floor: no cost
            Assert.Equal("", MetamorphicRelations.CountBound("n", 100, 200, Floor));
            Assert.Equal("", MetamorphicRelations.CountBound("n", 100000, 50, Floor)); // smaller is no explosion
            Assert.NotEqual("", MetamorphicRelations.CountBound("n", 100, 201, 0));
            Assert.NotEqual("", MetamorphicRelations.CountBound("n", 0, 136018, Floor));   // the #712 parent's wrap size
        }

        // ---------------------------------------------------------------- A1: MeshRobustRepair (#712: the flattened tetrahedron)

        struct RepairRun { public bool Ok; public string Reason; public long WrapTriangles, Triangles; public string Clean; }

        static RepairRun Repair(DMesh3 input)
        {
            var r = new MeshRobustRepair(input);
            bool ok = r.Apply();
            return new RepairRun {
                Ok = ok, Reason = r.ResultReason, WrapTriangles = r.AlphaWrapTriangles, Triangles = r.Mesh.TriangleCount,
                Clean = MeshRobustRepair.NotCleanReason(r.Mesh),
            };
        }

        static void A1_Repair(int axis, int e)
        {
            RepairRun reference = Repair(Tetrahedron());
            Assert.True(reference.Ok && reference.Clean == "", "the unflattened input must be repaired clean: " + reference.Reason + reference.Clean);
            RepairRun flat = Repair(MetamorphicRelations.Stretch(Tetrahedron(), axis, e));
            Assert.True(flat.Ok, $"axis {axis} 2^{e}: Apply() false, {flat.Reason}");
            Assert.Equal("", flat.Reason);                          // validity: the result is clean where the reference is
            Assert.Equal("", flat.Clean);
            Assert.Equal("", MetamorphicRelations.CountBound($"axis {axis} 2^{e} wrap triangles", reference.WrapTriangles, flat.WrapTriangles, Floor));
            Assert.Equal("", MetamorphicRelations.CountBound($"axis {axis} 2^{e} result triangles", reference.Triangles, flat.Triangles, Floor));
        }

        [Theory]
        [InlineData(0, -46)]
        [InlineData(0, 46)]
        public void A1_Repair_CostAndValidityDoNotDependOnTheFlattening(int axis, int e) => A1_Repair(axis, e);

        public static IEnumerable<object[]> AllAxisExponents() =>
            Enumerable.Range(0, 3).SelectMany(axis => MetamorphicRelations.AnisotropicAll().Select(e => new object[] { axis, e }));

        [Theory, MemberData(nameof(AllAxisExponents)), Trait("Category", "Nightly")]
        public void A1_Repair_FullSweep(int axis, int e) => A1_Repair(axis, e);

        // ---------------------------------------------------------------- A1: ExactMeshBoolean (the operations on two tetrahedron families)

        public static IEnumerable<object[]> BooleanFamilies() => ExactBooleanMetamorphicTests.TetFamilies.Keys.Select(f => new object[] { f });

        /// <summary>
        /// Known limit after the fix of #985 (SafeRounding no longer empties a non-empty result: SafeRoundingLastComponentTests):
        /// for an exponent of magnitude BooleanKnownDefectFrom or more, a flattened row can be not clean where the unflattened one is
        /// (degenerate triangles: the flattened input itself is degenerate by the relative threshold; or a component the repair's
        /// edits turned inside out), always reported (RoundingClean false). Never open or empty: the census asserts it. Measured:
        /// 301 of the 1656 rows of the sweep (6 families, 3 axes, 92 exponents; 689 rows before the fix, all open). Below the
        /// magnitude every row passes.
        /// </summary>
        public const int BooleanKnownDefectFrom = 18;
        public const int BooleanKnownDefectRows = 301;

        /// <summary> "" if the relation holds for the row, else the violation (for the 3 operations; the first one reported) </summary>
        static string BooleanViolation(string family, int axis, int e)
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies[family];
            DMesh3 a = ta.Mesh(Vector3d.Zero), b = tb.Mesh(Vector3d.Zero);
            DMesh3 fa = MetamorphicRelations.Stretch(a, axis, e), fb = MetamorphicRelations.Stretch(b, axis, e);
            foreach (Ops op in BooleanOracle.AllOps) {
                DMesh3 r = BooleanOracle.Run(a, b, op, out string e1), rf = BooleanOracle.Run(fa, fb, op, out string e2);
                if (e1 != "" || e2 != "") return $"{family} {op} axis {axis} 2^{e}: {e1}{e2}";
                // validity: closed and clean where the unflattened result is (an unclean reference is no claim)
                if (r.IsClosed() && !rf.IsClosed()) return $"{family} {op} axis {axis} 2^{e}: the result is not closed, the unflattened one is";
                if (BooleanOracle.CleanReason(r) == "" && BooleanOracle.CleanReason(rf) != "")
                    return $"{family} {op} axis {axis} 2^{e}: not clean ({BooleanOracle.CleanReason(rf)}), the unflattened one is";
                string c = MetamorphicRelations.CountBound($"{family} {op} axis {axis} 2^{e} triangles", r.TriangleCount, rf.TriangleCount, 0);
                if (c != "") return c;
            }
            return "";
        }

        static void A1_Boolean(string family, int axis, int e)
        {
            // rows of the known defect are pinned by the census (A1_ExactBoolean_KnownDefect_Census), not row by row
            if (Math.Abs(e) >= BooleanKnownDefectFrom) return;
            Assert.Equal("", BooleanViolation(family, axis, e));
        }

        /// <summary> the PR-tier rows below the known defect, both directions just under the boundary </summary>
        public static IEnumerable<object[]> BooleanFamilyNearBoundary() =>
            BooleanFamilies().SelectMany(f => new[] { -17, 17 }.Select(e => new object[] { f[0], e }));

        /// <summary> PR tier: the relation holds below the known defect (both directions, just under the boundary) </summary>
        [Theory, MemberData(nameof(BooleanFamilyNearBoundary))]
        public void A1_ExactBoolean_CostAndValidityDoNotDependOnTheFlattening(string family, int e) => A1_Boolean(family, 0, e);

        /// <summary> the families at both ends of the range, for the pin </summary>
        public static IEnumerable<object[]> BooleanFamilyAtEnd() =>
            BooleanFamilies().SelectMany(f => new[] { -MetamorphicRelations.AnisotropicLimit, MetamorphicRelations.AnisotropicLimit }.Select(e => new object[] { f[0], e }));

        /// <summary>
        /// PR tier: #985 at the ends of the range (2^-46 and 2^+46, flattened along x), strict per row: no result is open where the
        /// unflattened one is closed (the defect, fixed), and a row that still violates the relation does so only as "not clean", the
        /// known limit (BooleanKnownDefectRows)
        /// </summary>
        [Theory, MemberData(nameof(BooleanFamilyAtEnd))]
        public void A1_ExactBoolean_985_EndsAreNotOpen(string family, int e)
        {
            string v = BooleanViolation(family, 0, e);
            Assert.True(v == "" || v.Contains(": not clean ("), $"{family} at 2^{e}: {v}");
        }

        public static IEnumerable<object[]> BooleanFamilyAxisExponents() =>
            BooleanFamilies().SelectMany(f => Enumerable.Range(0, 3).SelectMany(axis => MetamorphicRelations.AnisotropicAll().Select(e => new object[] { f[0], axis, e })));

        [Theory, MemberData(nameof(BooleanFamilyAxisExponents)), Trait("Category", "Nightly")]
        public void A1_ExactBoolean_FullSweep(string family, int axis, int e) => A1_Boolean(family, axis, e);

        /// <summary>
        /// Nightly (Category=Nightly: the sweep is 1656 rows of 3 operations each, about 7 s on net10, so it is not a PR check): the rows at
        /// or above the known limit that violate the relation must number exactly BooleanKnownDefectRows (#985), each one only as "not
        /// clean" (an open or empty result anywhere fails, as does any violation below the boundary). This is a count, not the
        /// set of rows: a fix of one row together with a new violation in another keeps the count. The per-row pins in the PR tier and the
        /// known-defect ends of FullSweep narrow that gap. A row below the boundary is asserted by FullSweep.
        /// </summary>
        [Fact, Trait("Category", "Nightly")]
        public void A1_ExactBoolean_KnownDefect_Census()
        {
            int violating = 0;
            var unexpected = new List<string>();   // below the boundary, or not a "not clean" violation
            foreach (string family in ExactBooleanMetamorphicTests.TetFamilies.Keys)
                for (int axis = 0; axis < 3; ++axis)
                    foreach (int e in MetamorphicRelations.AnisotropicAll()) {
                        string v = BooleanViolation(family, axis, e);
                        if (v == "") continue;
                        if (Math.Abs(e) < BooleanKnownDefectFrom || v.Contains(": not clean (") == false) unexpected.Add(v);
                        else violating++;
                    }
            Assert.True(unexpected.Count == 0, string.Join("\n", unexpected.Take(10)));
            Assert.Equal(BooleanKnownDefectRows, violating);
        }

        // ---------------------------------------------------------------- A1: MeshAlphaWrap

        static MeshAlphaWrap Wrap(DMesh3 input)
        {
            var w = new MeshAlphaWrap(input);
            Assert.True(w.Compute(), "the alpha wrap did not compute");
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            return w;
        }

        static void A1_AlphaWrap(int axis, int e)
        {
            MeshAlphaWrap reference = Wrap(Sphere(1, 6)), flat = Wrap(MetamorphicRelations.Stretch(Sphere(1, 6), axis, e));
            Assert.True(flat.Result.IsClosed(), $"axis {axis} 2^{e}: the wrap is not closed");
            Assert.True(flat.Result.CheckValidity(true, FailMode.ReturnOnly), $"axis {axis} 2^{e}: the wrap is not valid");
            Assert.Equal("", MetamorphicRelations.CountBound($"axis {axis} 2^{e} wrap triangles", reference.Result.TriangleCount, flat.Result.TriangleCount, Floor));
        }

        [Theory]
        [InlineData(0, -46)]
        [InlineData(0, 46)]
        public void A1_AlphaWrap_CostAndValidityDoNotDependOnTheFlattening(int axis, int e) => A1_AlphaWrap(axis, e);

        [Theory, MemberData(nameof(AllAxisExponents)), Trait("Category", "Nightly")]
        public void A1_AlphaWrap_FullSweep(int axis, int e) => A1_AlphaWrap(axis, e);

        // ---------------------------------------------------------------- A1: the 2D quality mesher

        /// <summary>
        /// The unit pentagon, no snapping (the absolute snap tolerance of the fuzz inputs would collapse a flattened input), quality on.
        /// </summary>
        static Mesh2Input Pentagon(double scaleX)
        {
            var input = new Mesh2Input {
                X = new[] { 0, scaleX, scaleX, scaleX * 0.5, 0 }, Y = new[] { 0, 0, 0.6, 1, 1.0 },
                MinAngleDeg = 20, ConstrainedQuality = true, SteinerLimit = PentagonSteinerLimit, SnapTolerance = 0,
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
            return input;
        }

        const int PentagonSteinerLimit = 20000;

        /// <summary>
        /// Known defect #986: the mesher returns Empty (0 triangles) for a valid pentagon at an aspect ratio of 2^41 or more, in both
        /// directions. Below it, the cost of a min-angle mesh is proportional to the aspect ratio (the size law of quality meshing: a sliver
        /// needs points along its length), so the bound of A1 for this operation is the reference's count times 2^|e| (within CountFactor),
        /// not the reference's count. Where the Steiner limit of the input is reached, the output says so (SteinerLimitReached): a documented cap.
        /// </summary>
        public const int MesherKnownDefectFrom = 41;

        static void A1_Mesher(int e)
        {
            var mesher = new QualityMesh2Candidate();
            // the flattening is of the x extent: the pentagon's x coordinates are multiplied by 2^e (exact)
            Mesh2Input reference = Pentagon(1), flat = Pentagon(Math.Pow(2, e));
            Mesh2Output a = mesher.Run(reference), b = mesher.Run(flat);
            Assert.True(a.Status == Mesh2Status.Success, "the unflattened input: " + a.Status + " " + a.FailureKind);
            // rows of the known defect #986 are pinned by A1_QualityMesh2_KnownDefect986_RowsStillEmpty
            if (Math.Abs(e) >= MesherKnownDefectFrom) return;
            Assert.True(b.Status == Mesh2Status.Success, $"2^{e}: {b.Status} {b.FailureKind}");
            long aspect = (long)Math.Pow(2, Math.Abs(e));
            long predicted = a.VertexCount * aspect;
            if (b.SteinerLimitReached) {
                // the documented cap of the input: legitimate only where the size law itself predicts more points than the limit
                Assert.True(predicted * (long)MetamorphicRelations.CountFactor > PentagonSteinerLimit,
                    $"2^{e}: the Steiner limit was reached, but the size law predicts {predicted} points");
                return;
            }
            Assert.Equal("", MetamorphicRelations.CountBound($"2^{e} vertices", predicted, b.VertexCount, Floor));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(2)]
        public void A1_QualityMesh2_CostAndValidityDoNotDependOnTheFlattening(int e) => A1_Mesher(e);

        /// <summary> PR tier: the known defect #986 at the ends of the range, strict: the mesher must still return Empty (a fix removes this pin) </summary>
        [Theory]
        [InlineData(-46)]
        [InlineData(46)]
        public void A1_QualityMesh2_KnownDefect986_EndsStillEmpty(int e)
        {
            Mesh2Output b = new QualityMesh2Candidate().Run(Pentagon(Math.Pow(2, e)));
            Assert.True(b.Status == Mesh2Status.Empty, $"2^{e} gives {b.Status} now: #986 is fixed, remove this pin");
        }

        // The nightly sweep runs every exponent of A1. Rows of the known defect must be Empty (strict, per row).
        public static IEnumerable<object[]> MesherExponents() => MetamorphicRelations.AnisotropicAll().Select(e => new object[] { e });

        [Theory, MemberData(nameof(MesherExponents)), Trait("Category", "Nightly")]
        public void A1_QualityMesh2_FullSweep(int e) => A1_Mesher(e);

        /// <summary> Nightly: the rows of the known defect #986 (|e| 41..46) are all Empty; a row that is not is a fix or a new failure. </summary>
        [Fact, Trait("Category", "Nightly")]
        public void A1_QualityMesh2_KnownDefect986_RowsStillEmpty()
        {
            var bad = new List<string>();
            foreach (int e in MetamorphicRelations.AnisotropicAll().Where(x => Math.Abs(x) >= MesherKnownDefectFrom))
                if (new QualityMesh2Candidate().Run(Pentagon(Math.Pow(2, e))).Status != Mesh2Status.Empty) bad.Add(e.ToString());
            Assert.True(bad.Count == 0, "rows that are no longer Empty (#986 changed): " + string.Join(", ", bad));
        }

        // ---------------------------------------------------------------- P1: the classifiers
        // The winding-number and IsInside samples lie at radius 0.4 and 1.8 of the unit sphere, far from the perturbed vertex: they check
        // that the classification is independent of the span, a regression net. They do not detect the #719 defect, which is in the open-mesh
        // Antipodal union below (validated on the #719 parent, see benchmarks/MetamorphicFalsification/results/relations.txt).

        static DMesh3 OpenSphere(Vector3d c, double r, int ev, Vector3d dir, double cut)
        {
            DMesh3 m = Sphere(r, ev);
            MeshTransforms.Translate(m, c);
            foreach (int t in m.TriangleIndices().ToList())
                if ((m.GetTriCentroid(t) - c).Normalized.Dot(dir.Normalized) > cut)
                    m.RemoveTriangle(t, true, false);
            return m;
        }

        /// <summary> sample points at radius 0.4 (inside) and 1.8 (outside) of the unit sphere, far from its surface </summary>
        static IEnumerable<Vector3d> SamplePoints()
        {
            var rnd = new Random(7);
            for (int i = 0; i < 200; ++i) {
                Vector3d d = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1).Normalized;
                yield return d * (i % 2 == 0 ? 0.4 : 1.8);
            }
        }

        static void P1_WindingNumber(int e)
        {
            DMesh3 m = Sphere(1, 6);
            DMesh3 p = MetamorphicRelations.SmallestCoordinate(m, e);
            var tree = new DMeshAABBTree3(m, true);
            var treeP = new DMeshAABBTree3(p, true);
            foreach (Vector3d q in SamplePoints()) {
                double w = tree.WindingNumber(q), wp = treeP.WindingNumber(q);
                Assert.Equal("", MetamorphicRelations.ValueBound($"winding at {q} under 2^{e}", w, wp, 1e-9));
                Assert.Equal(w > 0.5, wp > 0.5);
            }
        }

        static void P1_IsInside(int e)
        {
            DMesh3 m = Sphere(1, 6);
            DMesh3 p = MetamorphicRelations.SmallestCoordinate(m, e);
            var tree = new DMeshAABBTree3(m, true);
            var treeP = new DMeshAABBTree3(p, true);
            foreach (Vector3d q in SamplePoints())
                Assert.Equal(tree.IsInside(q), treeP.IsInside(q));
        }

        /// <summary> the #719 union: a closed sphere and an open one (the Antipodal classification of open meshes) </summary>
        static void P1_OpenUnion(int e)
        {
            DMesh3 a = Sphere(16, 5), b = OpenSphere(new Vector3d(4.8, 6.4, 8), 11.2, 6, new Vector3d(0, 0, 1), 0.7);
            var unit = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(unit.Compute());
            var tiny = new ExactMeshBoolean(MetamorphicRelations.SmallestCoordinate(a, e), b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(tiny.Compute(), $"2^{e}: the union did not compute");
            Assert.True(tiny.Result.CheckValidity(true, FailMode.ReturnOnly), $"2^{e}: the union is not valid");
            Assert.Equal(unit.Result.TriangleCount, tiny.Result.TriangleCount);   // the classification of whole pieces is unchanged
            Assert.Equal("", MetamorphicRelations.ValueBound($"area under 2^{e}", Invariance.Area(unit.Result), Invariance.Area(tiny.Result), 1e-9));
        }

        [Theory]
        [InlineData(-500)]
        [InlineData(-1000)]
        public void P1_WindingNumber_ClassificationDoesNotDependOnTheSpan(int e) => P1_WindingNumber(e);

        [Theory]
        [InlineData(-500)]
        [InlineData(-1000)]
        public void P1_IsInside_ClassificationDoesNotDependOnTheSpan(int e) => P1_IsInside(e);

        [Theory]
        [InlineData(-500)]
        [InlineData(-600)]
        [InlineData(-1000)]
        public void P1_OpenMeshUnion_DoesNotDependOnTheSpan(int e) => P1_OpenUnion(e);

        public static IEnumerable<object[]> TinyExponents() => MetamorphicRelations.TinyAll().Select(e => new object[] { e });

        [Theory, MemberData(nameof(TinyExponents)), Trait("Category", "Nightly")]
        public void P1_WindingNumber_FullSweep(int e) => P1_WindingNumber(e);

        [Theory, MemberData(nameof(TinyExponents)), Trait("Category", "Nightly")]
        public void P1_IsInside_FullSweep(int e) => P1_IsInside(e);

        [Theory, MemberData(nameof(TinyExponents)), Trait("Category", "Nightly")]
        public void P1_OpenMeshUnion_FullSweep(int e) => P1_OpenUnion(e);
    }
}
