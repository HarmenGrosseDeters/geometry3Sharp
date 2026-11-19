using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QuadRemesher.PureQuads (docs/design/QUAD_REMESHING.md stage 3B; QuadFlowRepair after Huang et al., "QuadriFlow", CGF 2018): the
    /// opt-in mesh of quads only: the success path (all quads, manifold, no fold), the fallback contract (every failed check gives
    /// the stage 2 mesh, face for face, and a status that says why), the default (off, unchanged), the exact integer map, the
    /// budgets and the checks one by one, invariance under similarities, reuse and determinism.
    /// </summary>
    public class PureQuadTests
    {
        // (name, edge length of the quads, curvature alignment): on a plateau of edge lengths where the pure-quad attempt succeeds
        // (the attempt on the sphere and the torus is not monotone in s: some lengths fold)
        static readonly Dictionary<string, (double s, double align)> Cases = new Dictionary<string, (double, double)> {
            { "sphere", (0.29, 0) }, { "torus", (0.18, 0) }, { "torus-aligned", (0.2, 10) }, { "box", (0.25, 0) },
        };

        static readonly Dictionary<string, DMesh3> meshes = new Dictionary<string, DMesh3>();
        static readonly Dictionary<string, CrossField> fields = new Dictionary<string, CrossField>();

        static DMesh3 Shape(string name)
        {
            lock (meshes) {
                DMesh3 m;
                if (!meshes.TryGetValue(name, out m)) {
                    switch (name) {
                        case "sphere": m = CrossFieldShapes.UnitSphere(); break;
                        case "torus": case "torus-aligned": m = CrossFieldShapes.TorusMesh(); break;
                        case "box": m = CrossFieldShapes.FilletedBoxMesh(); break;
                        case "cylinder": m = CrossFieldShapes.CappedCylinderMesh(); break;
                        case "cap": m = CrossFieldShapes.SphereCap(); break;
                        case "patch": m = CrossFieldShapes.OpenPatch(24); break;
                        default: throw new ArgumentException(name);
                    }
                    meshes[name] = m;
                }
                return m;
            }
        }

        static CrossField FieldOf(string name, double align = 0)
        {
            lock (fields) {
                string key = name + "/" + align;
                CrossField cf;
                if (!fields.TryGetValue(key, out cf)) {
                    cf = new CrossField(Shape(name)) { CurvatureAlignment = align };
                    Assert.Equal(CrossFieldStatus.Success, cf.Compute());
                    fields[key] = cf;
                }
                return cf;
            }
        }

        static QuadRemesher Run(string name, double s, double align = 0, bool pure = true, Action<QuadRemesher> configure = null)
        {
            var qr = new QuadRemesher(Shape(name)) { Field = FieldOf(name, align), TargetEdgeLength = s, PureQuads = pure };
            configure?.Invoke(qr);
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        static QuadRemesher RunCase(string name, bool pure = true, Action<QuadRemesher> configure = null)
        {
            var c = Cases[name];
            return Run(name, c.s, c.align, pure, configure);
        }

        static void AssertSame(DPolyMesh a, DPolyMesh b, double scale = 1)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.FaceCount, b.FaceCount);
            Assert.Equal(a.FaceOffsets.ToArray(), b.FaceOffsets.ToArray());
            Assert.Equal(a.FaceIndices.ToArray(), b.FaceIndices.ToArray());
            for (int v = 0; v < a.VertexCount; ++v) {
                Vector3d p = a.GetVertex(v), q = b.GetVertex(v);
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.x * scale), BitConverter.DoubleToInt64Bits(q.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.y * scale), BitConverter.DoubleToInt64Bits(q.y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.z * scale), BitConverter.DoubleToInt64Bits(q.z));
            }
        }

        static double OneSidedHausdorff(DPolyMesh r, DMesh3 reference)
        {
            DMesh3 tri = r.ToDMesh3(out int[] map);
            var hd = new MeshHausdorffDistance(tri, reference) { Symmetric = false, RelativeTolerance = 1e-3 };
            hd.Compute();
            return hd.UpperBound;
        }

        // the repaired integer map: sum 0 per triangle, and the exact count of triangles of negative orientation
        static int CheckMap(QuadRemesher qr, bool allSmall)
        {
            int[] d = qr.PureQuadRepair.Offsets();
            Assert.True(d.Length > 0 && d.Length % 6 == 0);
            int inverted = 0;
            for (int f = 0; f < d.Length / 6; ++f) {
                int sx = d[6 * f] + d[6 * f + 2] + d[6 * f + 4], sy = d[6 * f + 1] + d[6 * f + 3] + d[6 * f + 5];
                Assert.True(sx == 0 && sy == 0, "triangle " + f + " does not close");
                int det = d[6 * f] * d[6 * f + 3] - d[6 * f + 1] * d[6 * f + 2];
                if (det < 0) inverted++;
                if (allSmall) for (int k = 0; k < 6; ++k) Assert.InRange(d[6 * f + k], -1, 1);
            }
            return inverted;
        }

        // ---------------------------------------------------------------- the success path

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        [InlineData("torus-aligned")]
        [InlineData("box")]
        public void PureQuads_ClosedShapes_AreAllQuadsAndFoldFree(string name)
        {
            QuadRemesher qr = RunCase(name);
            QuadRemesher stage2 = RunCase(name, false);
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles);
            Assert.Equal(0, qr.FlippedQuads);
            Assert.Equal(0, CheckMap(qr, true));
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            Assert.True(qr.Result.Validate().IsValid);
            Assert.Equal(1.0, m.QuadFraction);
            Assert.True(m.IsManifold, name);
            Assert.Equal(0, m.BoundaryVertices);
            Assert.True(m.Quads >= 40, name + ": " + m.Quads + " quads");
            for (int v = 0; v < qr.Result.VertexCount; ++v) Assert.True(qr.Result.GetVertex(v).IsFinite);
            Assert.Equal(name.StartsWith("torus") ? 0 : 2, m.EulerCharacteristic);
            // few irregular vertices: the singularities of the field and a tenth of those of stage 2 (QUAD_REMESHING.md, stage 3 gate)
            PolyMeshMetrics m2 = PolyMeshMetrics.Compute(stage2.Result);
            int sing = FieldOf(name, Cases[name].align).SingularFaces.Count;
            Assert.True(m.IrregularVertices <= sing + (int)Math.Floor(0.1 * m2.IrregularVertices), name + ": irregular " + m.IrregularVertices + ", singular " + sing + ", stage 2 " + m2.IrregularVertices);
            Assert.True(m.QuadFraction > m2.QuadFraction || m2.QuadFraction == 1.0);
            // faithful (projected onto the input), outward like the input
            double s = Cases[name].s;
            Assert.True(OneSidedHausdorff(qr.Result, Shape(name)) <= 0.5 * s, name + ": Hausdorff");
            DMesh3 tri = qr.Result.ToDMesh3(out int[] map);
            Assert.True(Invariance.Volume(tri) > 0, name + ": inward");
            // the stage times are counted
            Assert.True(qr.FlowSeconds > 0 && qr.RepairSeconds >= 0);
            Assert.Equal(stage2.Status, qr.Status);
        }

        [Fact]
        public void PureQuads_QuadsAreOfTheTargetSize()
        {
            QuadRemesher qr = RunCase("box");
            double area = Shape("box").TriangleIndices().Sum(t => Shape("box").GetTriArea(t));
            double expected = area / (Cases["box"].s * Cases["box"].s);
            Assert.InRange(qr.Result.FaceCount, 0.8 * expected, 1.2 * expected);
        }

        // ---------------------------------------------------------------- the fallback contract

        static void AssertFallback(QuadRemesher qr, QuadRemesher stage2, string what)
        {
            Assert.NotEqual(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.NotEqual(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(QuadRemeshStatus.Success, qr.Status);            // a failed attempt is not a failed remeshing
            Assert.NotNull(qr.Result);
            AssertSame(stage2.Result, qr.Result);                         // the stage 2 mesh, face for face and bit for bit
            Assert.Equal(stage2.DroppedFaces, qr.DroppedFaces);
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        public void ForcedFold_WithoutTheSignPenalty_FallsBackToStage2(string name)
        {
            // the sign-aware flow cost is what keeps these lengths fold free: the plain marginal cost folds
            QuadRemesher good = RunCase(name);
            QuadRemesher bad = RunCase(name, true, q => q.SignPenalty = false);
            QuadRemesher stage2 = RunCase(name, false);
            Assert.Equal(PureQuadStatus.Success, good.PureQuadStatus);
            AssertFallback(bad, stage2, name);
            Assert.True(bad.InvertedTriangles > 0);
            Assert.Equal(bad.InvertedTriangles, CheckMap(bad, true));     // the counter is the exact count of the integer map
            // folds that the repair did not remove: the budget ran out, or it failed within its budget
            Assert.Equal(bad.PureQuadRepair.SatBudgetExhausted ? PureQuadStatus.BudgetExceeded : PureQuadStatus.OrientationFolds, bad.PureQuadStatus);
            Assert.True(bad.PureQuadRepair.SatDecisions > 0);
        }

        [Theory]
        [InlineData(0.18)]
        [InlineData(0.22)]
        [InlineData(0.26)]
        public void CappedCylinder_FoldsNaturallyAndFallsBack(double s)
        {
            QuadRemesher qr = Run("cylinder", s);
            QuadRemesher stage2 = Run("cylinder", s, 0, false);
            AssertFallback(qr, stage2, "cylinder");
            Assert.True(qr.InvertedTriangles > 0);
            Assert.Equal(qr.InvertedTriangles, CheckMap(qr, true));
            Assert.True(qr.PureQuadStatus == PureQuadStatus.OrientationFolds || qr.PureQuadStatus == PureQuadStatus.BudgetExceeded);
        }

        [Fact]
        public void OneFoldLeft_IsOrientationFolds_WhenTheBudgetIsNotExhausted()
        {
            // the sphere at 0.3 ends with one inverted triangle that the local repair cannot remove (no budget problem)
            QuadRemesher qr = Run("sphere", 0.30);
            QuadRemesher stage2 = Run("sphere", 0.30, 0, false);
            AssertFallback(qr, stage2, "sphere 0.3");
            Assert.Equal(PureQuadStatus.OrientationFolds, qr.PureQuadStatus);
            Assert.Equal(1, qr.InvertedTriangles);
            Assert.False(qr.PureQuadRepair.SatBudgetExhausted);
            Assert.True(qr.PureQuadRepair.SatDecisions < 300000);
        }

        [Fact]
        public void Fallback_CountersAndStatus_AreRepeatable()
        {
            QuadRemesher a = Run("cylinder", 0.22), b = Run("cylinder", 0.22, 0, true, q => { q.Parallel = false; q.ChunkSize = 8; });
            Assert.Equal(a.PureQuadStatus, b.PureQuadStatus);
            Assert.Equal(a.InvertedTriangles, b.InvertedTriangles);
            Assert.Equal(a.PureQuadRepair.Offsets(), b.PureQuadRepair.Offsets());
            Assert.Equal(a.PureQuadRepair.SatDecisions, b.PureQuadRepair.SatDecisions);
        }

        // ---------------------------------------------------------------- the default

        [Fact]
        public void PureQuads_IsOffByDefault_AndTheDefaultIsStage2()
        {
            var qr = new QuadRemesher(Shape("box")) { Field = FieldOf("box"), TargetEdgeLength = 0.25 };
            Assert.False(qr.PureQuads);
            Assert.Equal(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles); Assert.Equal(0, qr.FlippedQuads);
            Assert.Equal(0.0, qr.FlowSeconds); Assert.Equal(0.0, qr.RepairSeconds);
            Assert.Null(qr.PureQuadRepair);
            // stage 2 is quad-dominant, not pure: the pure attempt did not run, and asking for it later changes only the pure-quad members
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            DPolyMesh first = qr.Result;
            qr.PureQuads = true;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(1.0, PolyMeshMetrics.Compute(qr.Result).QuadFraction);
            qr.PureQuads = false;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles);
            Assert.Null(qr.PureQuadRepair);
            AssertSame(first, qr.Result);
            Assert.True(m.Faces > 0);
        }

        [Fact]
        public void PureQuads_TheStage2MeshIsTheSameWithAndWithoutTheOption()
        {
            // a fallback result is the default result: the option does not touch the stage 2 pipeline
            foreach (var s in new[] { 0.2, 0.22, 0.3 }) {
                QuadRemesher stage2 = Run("sphere", s, 0, false);
                QuadRemesher pure = Run("sphere", s, 0, true);
                if (pure.PureQuadStatus == PureQuadStatus.Success) { Assert.NotEqual(stage2.Result.FaceCount, 0); continue; }
                AssertSame(stage2.Result, pure.Result);
                Assert.Equal(stage2.CollapsedEdges, pure.CollapsedEdges);
                Assert.Equal(stage2.RemovedValence2, pure.RemovedValence2);
            }
        }

        [Fact]
        public void PureQuads_FailedCompute_LeavesThePureQuadStateEmpty()
        {
            var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = 0.29, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            qr.TargetEdgeLength = -1;
            Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
            Assert.Equal(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles); Assert.Equal(0.0, qr.FlowSeconds); Assert.Null(qr.PureQuadRepair);
            Assert.Null(qr.Result);
        }

        [Theory]
        [InlineData("cap", 0.2)]
        [InlineData("patch", 0.08)]
        public void OpenMeshes_GiveAValidResultOrTheStage2Mesh(string name, double s)
        {
            QuadRemesher qr = Run(name, s);
            QuadRemesher stage2 = Run(name, s, 0, false);
            if (qr.PureQuadStatus != PureQuadStatus.Success) { AssertFallback(qr, stage2, name); return; }
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            Assert.True(m.IsManifold); Assert.Equal(1.0, m.QuadFraction);
            Assert.True(qr.Result.Validate().IsValid);
        }

        // ---------------------------------------------------------------- the repair on its own: budgets and checks

        static QuadFlowRepair Repair(string name, double s, double align = 0, Action<QuadFlowRepair> configure = null)
        {
            var pf = new PositionField(Shape(name), FieldOf(name, align)) { TargetEdgeLength = s };
            Assert.Equal(PositionFieldStatus.Success, pf.Compute());
            var rep = new QuadFlowRepair(Shape(name), FieldOf(name, align), pf, s);
            configure?.Invoke(rep);
            rep.Run();
            return rep;
        }

        [Fact]
        public void Repair_FlowThatDoesNotFitTheCapacity_IsInfeasible()
        {
            QuadFlowRepair ok = Repair("sphere", 0.22);
            Assert.True(ok.FlowCapacity > 2, "the case needs a capacity above 2: " + ok.FlowCapacity);   // 0.22 needs H = 4
            QuadFlowRepair rep = Repair("sphere", 0.22, 0, r => r.MaxFlowCapacity = 2);
            Assert.Equal(PureQuadStatus.FlowInfeasible, rep.Status);
            Assert.Null(rep.Result);
            Assert.True(rep.FlowSeconds > 0);
            Assert.Equal(0.0, rep.RepairSeconds);                                 // nothing after the flow ran
            // and through the remesher: the infeasible status with the stage 2 result is covered by AssertFallback of the other cases
        }

        [Fact]
        public void Repair_SplitBudget_IsBudgetExceeded()
        {
            QuadFlowRepair full = Repair("sphere", 0.29);
            Assert.Equal(PureQuadStatus.Success, full.Status);
            Assert.True(full.Splits > 3);
            QuadFlowRepair rep = Repair("sphere", 0.29, 0, r => r.MaxSplits = full.Splits - 1);
            Assert.Equal(PureQuadStatus.BudgetExceeded, rep.Status);
            Assert.Null(rep.Result);
            Assert.Equal(full.Splits, Repair("sphere", 0.29, 0, r => r.MaxSplits = full.Splits).Splits);
        }

        [Fact]
        public void Repair_SatBudget_IsCountedInDecisionsAndExhaustedIsBudgetExceeded()
        {
            QuadFlowRepair full = Repair("sphere", 0.29);
            Assert.Equal(PureQuadStatus.Success, full.Status);
            Assert.True(full.SatDecisions > 5 && full.SatAttempts > 0, "the case needs the SAT: " + full.SatDecisions);
            Assert.False(full.SatBudgetExhausted);
            // the same repair with a budget below its effort gives up, after the attempt that crossed the budget
            QuadFlowRepair small = Repair("sphere", 0.29, 0, r => r.SatTotalBudget = full.SatDecisions / 2);
            Assert.True(small.SatBudgetExhausted);
            Assert.Equal(PureQuadStatus.BudgetExceeded, small.Status);
            Assert.True(small.SatDecisions >= full.SatDecisions / 2);
            Assert.True(small.InvertedTriangles > 0);
            // a budget of the effort is enough, and the decisions are the same (a counter, not a clock)
            QuadFlowRepair same = Repair("sphere", 0.29, 0, r => r.SatTotalBudget = full.SatDecisions + 1);
            Assert.Equal(PureQuadStatus.Success, same.Status);
            Assert.Equal(full.SatDecisions, same.SatDecisions);
            // the owner's budget
            Assert.Equal(300000, new QuadFlowRepair(Shape("sphere"), FieldOf("sphere"), null, 0.2).SatTotalBudget);
        }

        static DPolyMesh QuadGrid(int n)
        {
            var m = new DPolyMesh();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(i, j, 0);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i;
                    m.AppendFace(new List<int> { a, a + 1, a + n + 2, a + n + 1 }, 0);
                }
            return m;
        }

        [Fact]
        public void CheckExtracted_NamesTheFirstFailedCheck()
        {
            DPolyMesh grid = QuadGrid(3);
            Assert.Equal(PureQuadStatus.Success, QuadFlowRepair.CheckExtracted(grid, 0, 0, 0));
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(null, 0, 0, 0));
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(new DPolyMesh(), 0, 0, 0));
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(grid, 1, 0, 0));      // a triangle that is not part of a quad
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(grid, 0, 2, 0));      // unpaired triangles
            Assert.Equal(PureQuadStatus.FlippedFaces, QuadFlowRepair.CheckExtracted(grid, 0, 0, 1));
            // the order: a mesh that is not a manifold of quads wins over a flipped quad
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(grid, 1, 0, 5));
            // a face that is not a quad
            DPolyMesh withTriangle = QuadGrid(2);
            withTriangle.AppendVertex(5, 5, 0);
            withTriangle.AppendFace(new List<int> { 1, 2, withTriangle.VertexCount - 1 }, 0);
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(withTriangle, 0, 0, 0));
            // three faces at one edge
            DPolyMesh fin = QuadGrid(1);
            int a = fin.AppendVertex(0, 0.5, 1), b = fin.AppendVertex(1, 0.5, 1);
            fin.AppendFace(new List<int> { 0, 1, b, a }, 0);
            int c = fin.AppendVertex(0, 0.5, -1), d = fin.AppendVertex(1, 0.5, -1);
            fin.AppendFace(new List<int> { 1, 0, c, d }, 0);
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(fin, 0, 0, 0));
        }

        [Fact]
        public void PairQuad_PairsOverTheHypotenuseAndCancelsTwoGons()
        {
            int cancelled = 0;
            // two triangles on the two sides of the hypotenuse {3, 4} with different third clusters: the quad
            var l = new List<(int a, int b, int c)> { (3, 4, 7), (4, 3, 8) };
            Assert.True(QuadFlowRepair.PairQuad(l, ref cancelled, out int A, out int B, out int C1, out int C2));
            Assert.Equal((3, 4, 7, 8), (A, B, C1, C2));
            Assert.Equal(0, cancelled);
            // the same two triangles with the same third cluster are a 2-gon: both go, nothing is left
            l = new List<(int a, int b, int c)> { (3, 4, 7), (4, 3, 7) };
            Assert.False(QuadFlowRepair.PairQuad(l, ref cancelled, out A, out B, out C1, out C2));
            Assert.Empty(l); Assert.Equal(1, cancelled);
            // a 2-gon beside a quad: the 2-gon cancels (in any order of the list), the quad remains
            l = new List<(int a, int b, int c)> { (3, 4, 9), (3, 4, 7), (4, 3, 9), (4, 3, 8) };
            Assert.True(QuadFlowRepair.PairQuad(l, ref cancelled, out A, out B, out C1, out C2));
            Assert.Equal(2, cancelled); Assert.Equal(2, l.Count);
            Assert.Equal((3, 4, 7, 8), (A, B, C1, C2));
            // one triangle, two on the same side, three: no quad
            foreach (var bad in new[] {
                new List<(int, int, int)> { (3, 4, 7) },
                new List<(int, int, int)> { (3, 4, 7), (3, 4, 8) },
                new List<(int, int, int)> { (3, 4, 7), (4, 3, 8), (4, 3, 9) } }) {
                var lb = new List<(int a, int b, int c)>(bad);
                Assert.False(QuadFlowRepair.PairQuad(lb, ref cancelled, out A, out B, out C1, out C2));
                Assert.Equal(bad.Count, lb.Count);
            }
        }

        // ---------------------------------------------------------------- invariance, reuse, determinism

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory, MemberData(nameof(Frames))]
        public void PureQuads_AreInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            const string shape = "torus-aligned";
            QuadRemesher baseRun = RunCase(shape);
            Assert.Equal(PureQuadStatus.Success, baseRun.PureQuadStatus);
            DMesh3 t = Invariance.Transformed(Shape(shape), sim);
            var cf = new CrossField(t) { CurvatureAlignment = Cases[shape].align };
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(t) { Field = cf, TargetEdgeLength = sim.Scale * Cases[shape].s, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            if (sim.IsExact) {
                // a power of two scale: the integer offsets are the same numbers, the polygon mesh the same bit for bit, scaled
                Assert.Equal(baseRun.PureQuadStatus, qr.PureQuadStatus);
                Assert.Equal(baseRun.PureQuadRepair.Offsets(), qr.PureQuadRepair.Offsets());
                Assert.Equal(baseRun.InvertedTriangles, qr.InvertedTriangles);
                Assert.Equal(baseRun.FlippedQuads, qr.FlippedQuads);
                AssertSame(baseRun.Result, qr.Result, sim.Scale);
            } else {
                // rotations, translations and other scales change the rounding: the same kind of mesh, the same size
                Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
                PolyMeshMetrics a = PolyMeshMetrics.Compute(baseRun.Result), b = PolyMeshMetrics.Compute(qr.Result);
                Assert.Equal(1.0, b.QuadFraction);
                Assert.True(b.IsManifold);
                Assert.Equal(a.EulerCharacteristic, b.EulerCharacteristic);
                Assert.InRange(b.Faces, 0.9 * a.Faces, 1.1 * a.Faces);
                Assert.Equal(0, qr.InvertedTriangles);
                Assert.True(OneSidedHausdorff(qr.Result, t) <= 0.5 * sim.Scale * Cases[shape].s, name + ": Hausdorff");
            }
        }

        [Fact]
        public void Compute_CanBeCalledAgain_WithPureQuads()
        {
            foreach (string name in new[] { "sphere", "box" }) {
                var c = Cases[name];
                var qr = new QuadRemesher(Shape(name)) { Field = FieldOf(name), TargetEdgeLength = c.s, PureQuads = true };
                qr.Compute();
                DPolyMesh first = qr.Result; PureQuadStatus status = qr.PureQuadStatus; int[] offsets = qr.PureQuadRepair.Offsets(); int inv = qr.InvertedTriangles;
                QuadFlowRepair firstRepair = qr.PureQuadRepair;
                qr.Compute();
                Assert.NotSame(first, qr.Result); Assert.NotSame(firstRepair, qr.PureQuadRepair);
                Assert.Equal(status, qr.PureQuadStatus); Assert.Equal(inv, qr.InvertedTriangles);
                Assert.Equal(offsets, qr.PureQuadRepair.Offsets());
                AssertSame(first, qr.Result);
            }
            // a failing repair on the same instance, then a good one: no state is kept
            var q = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = 0.3, PureQuads = true };
            q.Compute();
            Assert.Equal(PureQuadStatus.OrientationFolds, q.PureQuadStatus);
            q.TargetEdgeLength = 0.29;
            q.Compute();
            Assert.Equal(PureQuadStatus.Success, q.PureQuadStatus);
            Assert.Equal(0, q.InvertedTriangles);
            AssertSame(RunCase("sphere").Result, q.Result);
        }

        [Theory]
        [InlineData("sphere", 0.29)]        // success
        [InlineData("sphere", 0.20)]        // fallback with an exhausted budget
        [InlineData("cylinder", 0.22)]      // fallback
        [InlineData("box", 0.25)]
        public void PureQuads_AreTheSameSerialAndParallelAndRepeated(string name, double s)
        {
            QuadRemesher serial = Run(name, s, 0, true, q => { q.Parallel = false; q.ChunkSize = 8; });
            var runs = new List<QuadRemesher> {
                Run(name, s, 0, true, q => { q.Parallel = true; q.ChunkSize = 8; }),
                Run(name, s, 0, true, q => { q.Parallel = true; q.ChunkSize = 3; }),
                Run(name, s),
                Run(name, s),
                Run(name, s),
            };
            foreach (QuadRemesher r in runs) {
                Assert.Equal(serial.PureQuadStatus, r.PureQuadStatus);
                Assert.Equal(serial.InvertedTriangles, r.InvertedTriangles);
                Assert.Equal(serial.FlippedQuads, r.FlippedQuads);
                Assert.Equal(serial.PureQuadRepair.Offsets(), r.PureQuadRepair.Offsets());
                Assert.Equal(serial.PureQuadRepair.SatDecisions, r.PureQuadRepair.SatDecisions);
                Assert.Equal(serial.PureQuadRepair.Splits, r.PureQuadRepair.Splits);
                AssertSame(serial.Result, r.Result);
            }
        }
    }
}
