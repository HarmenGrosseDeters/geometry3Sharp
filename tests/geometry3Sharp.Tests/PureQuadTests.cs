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

        // the forced fold of the first attempt alone (the second attempt, the retry with the freed fixed variables, repairs most of them)
        static void FirstAttemptWithoutSignPenalty(QuadRemesher q) { q.SignPenalty = false; q.PureQuadRetry = false; }

        static void AssertFallback(QuadRemesher qr, QuadRemesher stage2, string what)
        {
            Assert.NotEqual(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.NotEqual(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(QuadRemeshStatus.Success, qr.Status);            // a failed attempt is not a failed remeshing
            Assert.NotNull(qr.Result);
            AssertSame(stage2.Result, qr.Result);                         // the stage 2 mesh, face for face and bit for bit
            Assert.Equal(stage2.DroppedFaces, qr.DroppedFaces);
        }

        [Fact]
        public void ForcedFold_WithoutTheSignPenalty_FallsBackToStage2()
        {
            // the sign-aware flow cost is what keeps the sphere fold free: the plain marginal cost folds
            string name = "sphere";
            QuadRemesher good = RunCase(name);
            QuadRemesher bad = RunCase(name, true, FirstAttemptWithoutSignPenalty);
            QuadRemesher stage2 = RunCase(name, false);
            Assert.Equal(PureQuadStatus.Success, good.PureQuadStatus);
            AssertFallback(bad, stage2, name);
            Assert.True(bad.InvertedTriangles > 0);
            Assert.Equal(bad.InvertedTriangles, CheckMap(bad, true));     // the counter is the exact count of the integer map
            // folds that the repair did not remove: the budget ran out, or it failed within its budget
            Assert.Equal(bad.PureQuadRepair.SatBudgetExhausted ? PureQuadStatus.BudgetExceeded : PureQuadStatus.OrientationFolds, bad.PureQuadStatus);
            Assert.True(bad.PureQuadRepair.SatDecisions > 0);
        }

        [Fact]
        public void ForcedFold_OnTheTorus_WithoutTheSignPenalty_FallsBackToStage2()
        {
            // the torus at 0.3 folds with the plain marginal cost (2 inverted triangles) and is fold free with the sign penalty
            QuadRemesher good = Run("torus", 0.3);
            QuadRemesher bad = Run("torus", 0.3, 0, true, q => q.SignPenalty = false);
            QuadRemesher stage2 = Run("torus", 0.3, 0, false);
            Assert.Equal(PureQuadStatus.Success, good.PureQuadStatus);
            AssertFallback(bad, stage2, "torus");
            Assert.True(bad.InvertedTriangles > 0);
            Assert.Equal(bad.InvertedTriangles, CheckMap(bad, true));
            Assert.Equal(bad.PureQuadRepair.SatBudgetExhausted ? PureQuadStatus.BudgetExceeded : PureQuadStatus.OrientationFolds, bad.PureQuadStatus);
        }

        [Theory]
        [InlineData(0.2)]
        [InlineData(0.29)]
        public void CappedCylinder_FirstAttemptAlone_FallsBackNaturally(double s)
        {
            // a closed mesh whose first pure-quad attempt ends in the geometric gate (no hook but the one that switches the second attempt off): the stage 2 mesh comes back
            QuadRemesher qr = Run("cylinder", s, 0, true, q => q.PureQuadRetry = false);
            Assert.False(qr.PureQuadRetried);
            QuadRemesher stage2 = Run("cylinder", s, 0, false);
            AssertFallback(qr, stage2, "cylinder " + s);
            Assert.Equal(PureQuadStatus.FlippedFaces, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles);
            Assert.True(qr.FlippedQuads > 0);
            Assert.True(qr.Result.Validate().IsValid);
        }

        [Theory]
        [InlineData(0.18)]
        [InlineData(0.22)]
        [InlineData(0.26)]
        public void CappedCylinder_WithImprovedFlow_SucceedsAsPureQuads(double s)
        {
            QuadRemesher qr = Run("cylinder", s);
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles);
            Assert.Equal(0, qr.FlippedQuads);
            Assert.Equal(0, CheckMap(qr, true));
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            Assert.True(qr.Result.Validate().IsValid);
            Assert.Equal(1.0, m.QuadFraction);
            Assert.True(m.IsManifold);
            Assert.Equal(0, m.BoundaryVertices);
            Assert.Equal(2, m.EulerCharacteristic);
            Assert.True(Invariance.Volume(qr.Result.ToDMesh3(out _)) > 0);
        }

        [Fact]
        public void OneFoldLeft_IsOrientationFolds_WhenTheBudgetIsNotExhausted()
        {
            // with SignPenalty disabled and SatMaxRing = 1, folds remain without exhausting SatTotalBudget
            QuadFlowRepair rep = Repair("sphere", 0.29, 0, r => { r.SignPenalty = false; r.SatMaxRing = 1; });
            Assert.Equal(PureQuadStatus.OrientationFolds, rep.Status);
            Assert.Null(rep.Result);
            Assert.True(rep.InvertedTriangles > 0);
            Assert.False(rep.SatBudgetExhausted);
            Assert.True(rep.SatDecisions < rep.SatTotalBudget);
        }

        [Fact]
        public void Fallback_CountersAndStatus_AreRepeatable()
        {
            QuadRemesher a = Run("sphere", 0.29, 0, true, FirstAttemptWithoutSignPenalty);
            QuadRemesher b = Run("sphere", 0.29, 0, true, q => { FirstAttemptWithoutSignPenalty(q); q.Parallel = false; q.ChunkSize = 8; });
            Assert.NotEqual(PureQuadStatus.Success, a.PureQuadStatus);
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
            int fallbacks = 0;
            var cases = new (string name, double s, bool signPenalty)[] {
                ("sphere", 0.2, true),
                ("sphere", 0.27, false),     // SignPenalty=false forces an orientation fold fallback on a closed mesh (0.27: the second attempt fails too)
                ("cap", 0.2, true),          // open mesh falls back with FlowInfeasible
            };
            foreach (var c in cases) {
                QuadRemesher stage2 = Run(c.name, c.s, 0, false, q => q.SignPenalty = c.signPenalty);
                QuadRemesher pure = Run(c.name, c.s, 0, true, q => q.SignPenalty = c.signPenalty);
                if (pure.PureQuadStatus == PureQuadStatus.Success) { Assert.NotEqual(stage2.Result.FaceCount, 0); continue; }
                fallbacks++;
                AssertSame(stage2.Result, pure.Result);
                Assert.Equal(stage2.CollapsedEdges, pure.CollapsedEdges);
                Assert.Equal(stage2.RemovedValence2, pure.RemovedValence2);
            }
            Assert.True(fallbacks >= 2, "expected both closed-mesh and open-mesh fallback cases to exercise Stage 2 identity");
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
        public void OpenMeshes_AreNotSupported_AndGiveTheStage2Mesh(string name, double s)
        {
            QuadRemesher qr = Run(name, s);
            QuadRemesher stage2 = Run(name, s, 0, false);
            AssertFallback(qr, stage2, name);
            Assert.Equal(PureQuadStatus.FlowInfeasible, qr.PureQuadStatus);
            Assert.Equal(0.0, qr.RepairSeconds);                  // stopped at the setup: no flow, no subdivision
            Assert.False(qr.PureQuadRetried);                     // and no second attempt: it would stop at the same place
            Assert.Equal(PureQuadStatus.FlowInfeasible, qr.PureQuadFirstStatus);
        }

        [Fact]
        public void IsolatedVertex_IsIgnored()
        {
            DMesh3 m = new DMesh3(Shape("torus-aligned"));
            m.AppendVertex(new Vector3d(9, 9, 9));
            var cf = new CrossField(m) { CurvatureAlignment = 10 };
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = 0.2, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(1.0, PolyMeshMetrics.Compute(qr.Result).QuadFraction);
        }

        [Fact]
        public void TwoComponents_AreRepairedTogether()
        {
            // two tori in one mesh (translated apart): the equations of each component are rotated from their own root
            DMesh3 a = Shape("torus-aligned"), m = new DMesh3(a);
            var map = new MeshEditor(m).AppendMesh(Invariance.Transformed(a, new Similarity("shift", 1, Matrix3d.Identity, new Vector3d(5, 0, 0))));
            Assert.True(m.IsClosed());
            var cf = new CrossField(m) { CurvatureAlignment = 10 };
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = 0.2, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(0, CheckMap(qr, true));
            PolyMeshMetrics pm = PolyMeshMetrics.Compute(qr.Result);
            Assert.Equal(1.0, pm.QuadFraction); Assert.True(pm.IsManifold);
            Assert.Equal(0, pm.EulerCharacteristic);              // two tori
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
            // small TargetEdgeLength relative to input edge length requires flow capacity H >= 4,
            // so capping MaxFlowCapacity = ok.FlowCapacity - 2 runs MinCostFlow at H >= 2 and fails with flow < supply
            QuadFlowRepair ok = Repair("sphere", 0.05);
            Assert.True(ok.FlowCapacity >= 4, "expected H >= 4 so capacity-capped run executes MinCostFlow, got H=" + ok.FlowCapacity);
            QuadFlowRepair rep = Repair("sphere", 0.05, 0, r => r.MaxFlowCapacity = ok.FlowCapacity - 2);
            Assert.Equal(PureQuadStatus.FlowInfeasible, rep.Status);
            Assert.Null(rep.Result);
            Assert.True(rep.FlowArcs > 0 && rep.FlowSearches > 0 && rep.FlowSeconds > 0);
            Assert.Equal(0.0, rep.RepairSeconds);                                 // nothing after the flow ran
            // and through the remesher: the infeasible status with the stage 2 result is covered by AssertFallback of the other cases
        }

        [Fact]
        public void Repair_RunsOnce_AndTheBalancingHasAWorkLimit()
        {
            QuadFlowRepair ok = Repair("sphere", 0.29);
            Assert.Throws<InvalidOperationException>(() => ok.Run());
            // without the greedy balancing of the fixed variables they are set to 0, and the flow of that map is not the repaired one
            QuadFlowRepair limited = Repair("sphere", 0.29, 0, r => r.GreedyWorkLimit = 0);
            Assert.False(limited.Status == PureQuadStatus.Success && limited.SatDecisions == ok.SatDecisions && limited.Splits == ok.Splits && limited.FlowSettled == ok.FlowSettled,
                "the limit changed nothing: the greedy balancing was not needed on this case");
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
            // torus without SignPenalty introduces folds that SAT cleanly resolves, exercising the decision-budget contract
            QuadFlowRepair full = Repair("torus", 0.18, 0, r => r.SignPenalty = false);
            Assert.Equal(PureQuadStatus.Success, full.Status);
            Assert.True(full.SatDecisions > 5 && full.SatAttempts > 0, "the case needs the SAT: " + full.SatDecisions);
            Assert.False(full.SatBudgetExhausted);
            // the same repair with a budget below its effort gives up, after the attempt that crossed the budget
            QuadFlowRepair small = Repair("torus", 0.18, 0, r => { r.SignPenalty = false; r.SatTotalBudget = full.SatDecisions / 2; });
            Assert.True(small.SatBudgetExhausted);
            Assert.Equal(PureQuadStatus.BudgetExceeded, small.Status);
            Assert.True(small.SatDecisions >= full.SatDecisions / 2);
            Assert.True(small.InvertedTriangles > 0);
            // a budget of the effort is enough, and the decisions are the same (a counter, not a clock)
            QuadFlowRepair same = Repair("torus", 0.18, 0, r => { r.SignPenalty = false; r.SatTotalBudget = full.SatDecisions + 1; });
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
            DPolyMesh cube = new DPolyMesh();
            for (int k = 0; k < 8; ++k) cube.AppendVertex(k & 1, (k >> 1) & 1, (k >> 2) & 1);
            foreach (var q in new[] { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } })
                cube.AppendFace(new List<int>(q), 0);
            Assert.Equal(PureQuadStatus.Success, QuadFlowRepair.CheckExtracted(cube, 0, 0, 0));
            DPolyMesh grid = QuadGrid(3);
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(grid, 0, 0, 0));      // a boundary: closed meshes only
            grid = cube;
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
            // non-finite vertex
            DPolyMesh nanCube = new DPolyMesh();
            for (int k = 0; k < 8; ++k) nanCube.AppendVertex(k == 0 ? double.NaN : (k & 1), (k >> 1) & 1, (k >> 2) & 1);
            foreach (var q in new[] { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } })
                nanCube.AppendFace(new List<int>(q), 0);
            Assert.Equal(PureQuadStatus.NotManifold, QuadFlowRepair.CheckExtracted(nanCube, 0, 0, 0));
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
                Assert.Equal(BitConverter.DoubleToInt64Bits(baseRun.PureQuadAreaRatio), BitConverter.DoubleToInt64Bits(qr.PureQuadAreaRatio));
                Assert.Equal(BitConverter.DoubleToInt64Bits(baseRun.PureQuadAreaRatioAfterFlow), BitConverter.DoubleToInt64Bits(qr.PureQuadAreaRatioAfterFlow));
            } else {
                // rotations, translations and other scales change the rounding: the same kind of mesh, the same size (or the attempt fails
                // on the changed rounding, and then Result is a valid stage 2 mesh)
                if (qr.PureQuadStatus != PureQuadStatus.Success) { Assert.True(qr.Result.Validate().IsValid); Assert.True(qr.InvertedTriangles > 0 || qr.PureQuadStatus != PureQuadStatus.OrientationFolds); return; }
                Assert.InRange(qr.PureQuadAreaRatio, 0.5, 1.5);        // a successful run: a map that covers the surface about once
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
            foreach (string name in new[] { "sphere" }) {   // one shape: the shared fields and a second shape cost about 1.5 s of the 5 s budget
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
            var q = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = 0.29, PureQuads = true, SignPenalty = false, PureQuadRetry = false };
            q.Compute();
            Assert.NotEqual(PureQuadStatus.Success, q.PureQuadStatus);
            Assert.True(q.InvertedTriangles > 0);
            q.SignPenalty = true;
            q.Compute();
            Assert.Equal(PureQuadStatus.Success, q.PureQuadStatus);
            Assert.Equal(0, q.InvertedTriangles);
            AssertSame(RunCase("sphere").Result, q.Result);
        }

        [Theory]
        [InlineData("sphere", 0.29)]        // success
        [InlineData("sphere", 0.20)]        // success
        [InlineData("cylinder", 0.22)]      // success
        [InlineData("cylinder", 0.2)]       // success of the second attempt
        [InlineData("sphere", 0.39)]        // success of the second attempt
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
                Assert.Equal(serial.PureQuadRetried, r.PureQuadRetried);
                Assert.Equal(s == 0.2 && name == "cylinder" || s == 0.39, r.PureQuadRetried);        // the two rows of the second attempt
                Assert.Equal(serial.PureQuadFirstStatus, r.PureQuadFirstStatus);
                Assert.Equal(serial.PureQuadRepair.FrozenEdgesChanged, r.PureQuadRepair.FrozenEdgesChanged);
                AssertSame(serial.Result, r.Result);
            }
        }

        [Fact]
        public void NoisySphere_SucceedsWithImprovedFlow()
        {
            // radial vertex noise on a sphere (previously fell back on 5/6 benchmark variations) now succeeds cleanly
            DMesh3 noisy = new DMesh3(Shape("sphere"));
            MeshNormals.QuickCompute(noisy);
            var rnd = new Random(1);
            double h = 0.14;
            foreach (int v in noisy.VertexIndices()) {
                Vector3d n = (Vector3d)noisy.GetVertexNormal(v);
                noisy.SetVertex(v, noisy.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * 0.15 * h));
            }
            var cf = new CrossField(noisy);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(noisy) { Field = cf, TargetEdgeLength = 0.29, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles);
            Assert.Equal(0, qr.FlippedQuads);
            Assert.True(qr.Result.Validate().IsValid);
            for (int v = 0; v < qr.Result.VertexCount; ++v) Assert.True(qr.Result.GetVertex(v).IsFinite);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            Assert.Equal(1.0, m.QuadFraction);
            Assert.True(m.IsManifold);
            Assert.Equal(0, m.BoundaryVertices);
            Assert.Equal(2, m.EulerCharacteristic);
            Assert.True(Invariance.Volume(qr.Result.ToDMesh3(out _)) > 0);
            Assert.True(OneSidedHausdorff(qr.Result, noisy) <= 0.5 * 0.29);
        }

        static QuadFlowRepair RunSyntheticExtract(DMesh3 input, Vector3d[] vp, Vector3d[] vn, (int a, int c2, int b, int c1)[] quads)
        {
            var repairType = typeof(QuadFlowRepair);
            var any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var offType = repairType.GetNestedType("Off", any);
            var triType = repairType.GetNestedType("Tri", any);
            object NewOff(int x, int y) => Activator.CreateInstance(offType, x, y);
            object NewTri(int[] v, (int x, int y)[] d)
            {
                object t = Activator.CreateInstance(triType);
                triType.GetField("V").SetValue(t, v);
                Array offs = Array.CreateInstance(offType, 3);
                for (int i = 0; i < 3; ++i) offs.SetValue(NewOff(d[i].x, d[i].y), i);
                triType.GetField("D").SetValue(t, offs);
                return t;
            }
            var r = new QuadFlowRepair(input, null, null, 1);
            var tris = (System.Collections.IList)repairType.GetField("tris", any).GetValue(r);
            foreach (var q in quads) {
                // PairQuad pairs (A, B, C1) and (B, A, C2) into quad (A, C2, B, C1)
                tris.Add(NewTri(new[] { q.a, q.b, q.c1 }, new[] { (1, 1), (-1, 0), (0, -1) }));
                tris.Add(NewTri(new[] { q.b, q.a, q.c2 }, new[] { (-1, -1), (1, 0), (0, 1) }));
            }
            var vpList = (List<Vector3d>)repairType.GetField("VP", any).GetValue(r);
            var vnList = (List<Vector3d>)repairType.GetField("VN", any).GetValue(r);
            vpList.AddRange(vp);
            vnList.AddRange(vn);
            repairType.GetMethod("Extract", any).Invoke(r, new object[] { vp });
            return r;
        }

        [Fact]
        public void Extract_ProjectedQuadFacingAwayFromNearestInputTriangle_IsFlippedFaces()
        {
            // closed 6-quad manifold cube [-1, 1]^3 with outward orientation and outward VN:
            // unprojected and projected normals both agree with VN on every face.
            var vp = new Vector3d[8];
            var vn = new Vector3d[8];
            for (int k = 0; k < 8; ++k) {
                vp[k] = new Vector3d((k & 1) == 0 ? -1 : 1, ((k >> 1) & 1) == 0 ? -1 : 1, ((k >> 2) & 1) == 0 ? -1 : 1);
                vn[k] = vp[k].Normalized;
            }
            var quads = new[] {
                (0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)
            };
            DMesh3 outwardBox = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            QuadFlowRepair ok = RunSyntheticExtract(outwardBox, vp, vn, quads);
            Assert.Equal(PureQuadStatus.Success, ok.Status);
            Assert.Equal(6, ok.Quads);
            Assert.Equal(0, ok.FlippedQuads);
            Assert.NotNull(ok.Result);

            // reversing the input box flips all nearest input triangle normals while fn.Dot(sn) > 0 and pfn.Dot(sn) > 0 remain true:
            // ONLY the nearest-input-triangle check detects the flip, and CheckExtracted returns FlippedFaces (closed manifold)
            DMesh3 inwardBox = new DMesh3(outwardBox);
            inwardBox.ReverseOrientation();
            QuadFlowRepair flipped = RunSyntheticExtract(inwardBox, vp, vn, quads);
            Assert.Equal(PureQuadStatus.FlippedFaces, flipped.Status);
            Assert.Equal(6, flipped.Quads);
            Assert.Equal(6, flipped.FlippedQuads);
            Assert.Null(flipped.Result);
        }

        [Fact]
        public void Extract_ProjectionInvertingQuadAgainstClusterNormal_CountsAsFlippedQuad()
        {
            // quad whose unprojected normal fn = (+0.8, 0, -0.16) agrees with sn = (+1, 0, +1) (fn.Dot(sn) = +0.64 > 0),
            // and whose projected normal pfn = (0, 0, -0.16) agrees with the bottom face's input normal tn = (0, 0, -1),
            // so ONLY the projected-against-cluster check !(pfn.Dot(sn) > 0) detects the flip
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            var vp = new[] {
                new Vector3d(0, -0.4, -1.5),   // A  -> projects to (0, -0.4, -1)
                new Vector3d(-0.1, 0, -2.0),   // C2 -> projects to (-0.1, 0, -1)
                new Vector3d(0, 0.4, -1.5),    // B  -> projects to (0, 0.4, -1)
                new Vector3d(0.1, 0, -1.0),    // C1 -> projects to (0.1, 0, -1)
            };
            var vn = new[] {
                new Vector3d(1, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)
            };
            QuadFlowRepair r = RunSyntheticExtract(box, vp, vn, new[] { (0, 1, 2, 3) });
            Assert.Equal(1, r.Quads);
            Assert.Equal(1, r.FlippedQuads);
            Assert.Null(r.Result);
        }

        [Fact]
        public void StepCost_PenalizesWrongGeometricSign_EvenWhenPositionFieldDisagrees_AndIsConvex()
        {
            var r = new QuadFlowRepair(Shape("sphere"), null, null, 0.2) { SignPenalty = true };
            // when r and rGeo disagree with |r| >= 1, MarginalCost(0, -1, -1.2) == 0,
            // yet the geometrically inverted step 0 -> -1 must still carry the full non-vanishing sign penalty
            int wrongStep = r.StepCost(0, -1, -1.2, +0.8);
            int rightStep = r.StepCost(0, +1, -1.2, +0.8);
            Assert.True(wrongStep >= 500 && wrongStep > rightStep, $"wrong-sign step cost {wrongStep} must exceed right-sign cost {rightStep}");
            Assert.True(rightStep <= 200, "right-sign step cost too high: " + rightStep);

            // convexity / monotonicity along both directions (required for MinCostFlow parallel arcs)
            int prevDec = int.MinValue;
            for (int a = 2; a >= -3; --a) {
                int c = r.StepCost(a, a - 1, -1.2, +0.8);
                Assert.True(c >= prevDec, $"decreasing step {a}->{a - 1} not non-decreasing: {c} < {prevDec}");
                prevDec = c;
            }
            int prevInc = int.MinValue;
            for (int a = -2; a <= 3; ++a) {
                int c = r.StepCost(a, a + 1, +1.2, -0.8);
                Assert.True(c >= prevInc, $"increasing step {a}->{a + 1} not non-decreasing: {c} < {prevInc}");
                prevInc = c;
            }
        }

        [Fact]
        public void Extract_TinyInvertedInputTriangle_IsDetected_AndZeroAreaTriangle_IsIgnored()
        {
            DMesh3 MakeCornerSupport()
            {
                DMesh3 m = new DMesh3();
                // 4 outward (+Z) corner support triangles at (±0.5, ±0.5, 1.0), leaving the center (0, 0, 1.0) open
                foreach (var c in new[] { (-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5) }) {
                    int a = m.AppendVertex(new Vector3d(c.Item1, c.Item2, 1.0));
                    int b = m.AppendVertex(new Vector3d(c.Item1 + 0.05, c.Item2, 1.0));
                    int d = m.AppendVertex(new Vector3d(c.Item1, c.Item2 + 0.05, 1.0));
                    m.AppendTriangle(a, b, d);
                }
                return m;
            }
            var vp = new[] {
                new Vector3d(-0.5, -0.5, 1.0),
                new Vector3d( 0.5, -0.5, 1.0),
                new Vector3d( 0.5,  0.5, 1.0),
                new Vector3d(-0.5,  0.5, 1.0),
            };
            var vn = new[] { Vector3d.AxisZ, Vector3d.AxisZ, Vector3d.AxisZ, Vector3d.AxisZ };

            // (1) valid non-degenerate inverted triangle with tiny edge length (1e-170) at the quad midpoint (0, 0, 1.0):
            // power-of-two self-scaling prevents Cross underflow to zero, so the flip is detected
            DMesh3 withTiny = MakeCornerSupport();
            double eps = 1e-170;
            int t0 = withTiny.AppendVertex(new Vector3d(0, 0, 1.0));
            int t1 = withTiny.AppendVertex(new Vector3d(0, eps, 1.0));
            int t2 = withTiny.AppendVertex(new Vector3d(eps, 0, 1.0));
            withTiny.AppendTriangle(t0, t1, t2); // normal -Z
            QuadFlowRepair rTiny = RunSyntheticExtract(withTiny, vp, vn, new[] { (0, 1, 2, 3) });
            Assert.Equal(1, rTiny.Quads);
            Assert.Equal(1, rTiny.FlippedQuads);

            // (2) genuinely zero-area collinear triangle at the quad midpoint (0, 0, 1.0): its normal is undefined,
            // so Extract must not falsely mark the valid outward-facing quad as flipped
            DMesh3 withDegen = MakeCornerSupport();
            int d0 = withDegen.AppendVertex(new Vector3d(-0.1, 0, 1.0));
            int d1 = withDegen.AppendVertex(new Vector3d( 0.0, 0, 1.0));
            int d2 = withDegen.AppendVertex(new Vector3d( 0.1, 0, 1.0));
            withDegen.AppendTriangle(d0, d1, d2);
            QuadFlowRepair rDegen = RunSyntheticExtract(withDegen, vp, vn, new[] { (0, 1, 2, 3) });
            Assert.Equal(1, rDegen.Quads);
            Assert.Equal(0, rDegen.FlippedQuads);
        }

        [Fact]
        public void SolveFlow_BalancingWork_StaysNearLinear()
        {
            // heap-based balancing must stay far below O(E * tot) even on a fine mesh with many unbalanced edges
            QuadFlowRepair fine = Repair("sphere", 0.08);
            Assert.Equal(PureQuadStatus.Success, fine.Status);
            Assert.True(fine.BalancingWork > 0 && fine.BalancingWork < fine.Triangles * 20L,
                $"expected near-linear balancing work, got {fine.BalancingWork} for {fine.Triangles} triangles");
        }

        [Fact]
        public void QuadFlowRepair_IsQuadFlipped_DetectsFlipBeforeAndAfterProjection()
        {
            // Quad (a, c2, b, c1) in the xy plane: (b - a) x (c1 - c2) points along +z when oriented counter-clockwise.
            Vector3d a = new Vector3d(0, 0, 0), c2 = new Vector3d(1, 0, 0), b = new Vector3d(1, 1, 0), c1 = new Vector3d(0, 1, 0);
            Vector3d sn = Vector3d.AxisZ;
            Assert.False(QuadFlowRepair.IsQuadFlipped(a, c2, b, c1, a, c2, b, c1, sn));
            Assert.True(QuadFlowRepair.IsQuadFlipped(a, c1, b, c2, a, c2, b, c1, sn));
            Assert.True(QuadFlowRepair.IsQuadFlipped(a, c2, b, c1, a, c1, b, c2, sn));
            Assert.True(QuadFlowRepair.IsQuadFlipped(a, c2, b, c1, a, a, b, b, sn));
            Assert.True(QuadFlowRepair.IsQuadFlipped(a, c2, b, c1, new Vector3d(double.NaN, 0, 0), c2, b, c1, sn));
            Assert.True(QuadFlowRepair.IsQuadFlipped(a, c2, b, c1, a, c2, b, c1, new Vector3d(0, 0, double.PositiveInfinity)));
            // scale robustness (also near the underflow and overflow range)
            foreach (double k in new[] { 1e-150, 1.0, 1e150 }) {
                Assert.False(QuadFlowRepair.IsQuadFlipped(k * a, k * c2, k * b, k * c1, k * a, k * c2, k * b, k * c1, sn));
                Assert.True(QuadFlowRepair.IsQuadFlipped(k * a, k * c2, k * b, k * c1, k * a, k * c1, k * b, k * c2, sn));
            }
        }

        // ---------------------------------------------------------------- the area ratio of the map (a diagnostic, #1301 option A)

        [Fact]
        public void LatticeAreaRatio_OfKnownMaps()
        {
            // a flat n x n grid of unit cells, every cell two triangles with offsets (1,0) and (0,1): lattice area = geometric area, ratio 1
            int n = 6; double s = 0.25;
            var flat = new List<(long, double)>();
            for (int i = 0; i < 2 * n * n; ++i) flat.Add((1L, s * s));
            double area = n * n * s * s;
            Assert.Equal(1.0, QuadFlowRepair.LatticeAreaRatio(flat, area));
            // every cell counted twice (offsets (2,0), (0,1): det 2): ratio 2
            var twice = flat.Select(x => (2 * x.Item1, x.Item2)).ToList();
            Assert.Equal(2.0, QuadFlowRepair.LatticeAreaRatio(twice, area));
            // a quarter of the triangles folded (det -1): 3/4 - 1/4 = 1/2
            var folded = new List<(long, double)>(flat);
            for (int i = 0; i < folded.Count; i += 4) folded[i] = (-1L, s * s);
            Assert.Equal(0.5, QuadFlowRepair.LatticeAreaRatio(folded, area));
            // a local spacing of 2 s on half of the triangles: the cell area is 4 times as large there
            var local = new List<(long, double)>();
            for (int i = 0; i < 2 * n * n; ++i) local.Add((1L, i < n * n ? s * s : 4 * s * s));
            Assert.Equal(2.5, QuadFlowRepair.LatticeAreaRatio(local, area));
            // no geometric area: NaN
            Assert.True(double.IsNaN(QuadFlowRepair.LatticeAreaRatio(flat, 0)));
            Assert.True(double.IsNaN(QuadFlowRepair.LatticeAreaRatio(flat, double.PositiveInfinity)));
            Assert.Equal(0.0, QuadFlowRepair.LatticeAreaRatio(new List<(long, double)>(), 1.0));
        }

        [Fact]
        public void AreaRatio_OfAPureQuadRun_CoversTheSurfaceOnce_AndChangesNoResult()
        {
            QuadRemesher qr = RunCase("sphere");
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.InRange(qr.PureQuadAreaRatio, 0.8, 1.2);       // 0.89 on this sphere: about 1, not exactly (the cones move cells)
            Assert.InRange(qr.PureQuadAreaRatioAfterFlow, 0.8, 1.2);
            Assert.Equal(qr.PureQuadRepair.AreaRatio, qr.PureQuadAreaRatio);
            Assert.Equal(qr.PureQuadRepair.AreaRatioAfterFlow, qr.PureQuadAreaRatioAfterFlow);
            // a diagnostic: the result is the one of the earlier checks (all quads, manifold, no fold, no flipped quad)
            Assert.Equal(0, qr.InvertedTriangles); Assert.Equal(0, qr.FlippedQuads);
            Assert.Equal(1.0, PolyMeshMetrics.Compute(qr.Result).QuadFraction);
            // the runs that fail after the flow report it too (the ones that end before the flow have NaN), and never as a reason: the status is the old one
            QuadRemesher bad = RunCase("sphere", true, FirstAttemptWithoutSignPenalty);
            Assert.NotEqual(PureQuadStatus.Success, bad.PureQuadStatus);
            Assert.True(!double.IsNaN(bad.PureQuadAreaRatio) && !double.IsNaN(bad.PureQuadAreaRatioAfterFlow));
            AssertSame(RunCase("sphere", false).Result, bad.Result);       // the fallback is still the stage 2 mesh
        }

        [Fact]
        public void AreaRatio_IsNaN_WhenNotComputed_AndResetOnReuse()
        {
            QuadRemesher off = RunCase("sphere", false);
            Assert.True(double.IsNaN(off.PureQuadAreaRatio) && double.IsNaN(off.PureQuadAreaRatioAfterFlow));
            // an open mesh ends before the flow
            var open = new QuadRemesher(Shape("patch")) { Field = FieldOf("patch"), TargetEdgeLength = 0.2, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, open.Compute());
            Assert.Equal(PureQuadStatus.FlowInfeasible, open.PureQuadStatus);
            Assert.True(double.IsNaN(open.PureQuadAreaRatio));
            // one instance: pure quads, then off, then an invalid input, then pure quads again: the same bits, never a stale value
            var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = Cases["sphere"].s, PureQuads = true };
            qr.Compute();
            double first = qr.PureQuadAreaRatio, firstFlow = qr.PureQuadAreaRatioAfterFlow;
            Assert.False(double.IsNaN(first));
            qr.PureQuads = false; qr.Compute();
            Assert.True(double.IsNaN(qr.PureQuadAreaRatio) && double.IsNaN(qr.PureQuadAreaRatioAfterFlow));
            qr.PureQuads = true; qr.TargetEdgeLength = -1;
            Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
            Assert.True(double.IsNaN(qr.PureQuadAreaRatio));
            qr.TargetEdgeLength = Cases["sphere"].s; qr.Compute();
            Assert.Equal(BitConverter.DoubleToInt64Bits(first), BitConverter.DoubleToInt64Bits(qr.PureQuadAreaRatio));
            Assert.Equal(BitConverter.DoubleToInt64Bits(firstFlow), BitConverter.DoubleToInt64Bits(qr.PureQuadAreaRatioAfterFlow));
        }

        [Fact]
        public void AreaRatio_IsTheSameSerialAndParallel()
        {
            QuadRemesher serial = Run("torus-aligned", Cases["torus-aligned"].s, Cases["torus-aligned"].align, true, q => { q.Parallel = false; q.ChunkSize = 8; });
            QuadRemesher parallel = Run("torus-aligned", Cases["torus-aligned"].s, Cases["torus-aligned"].align, true, q => { q.Parallel = true; q.ChunkSize = 3; });
            Assert.Equal(BitConverter.DoubleToInt64Bits(serial.PureQuadAreaRatio), BitConverter.DoubleToInt64Bits(parallel.PureQuadAreaRatio));
            Assert.Equal(BitConverter.DoubleToInt64Bits(serial.PureQuadAreaRatioAfterFlow), BitConverter.DoubleToInt64Bits(parallel.PureQuadAreaRatioAfterFlow));
        }

        // ---------------------------------------------------------------- the second attempt (the fixed variables of the flow freed)

        static void AssertCleanPureQuads(QuadRemesher qr, string name, double s, int euler)
        {
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(0, qr.InvertedTriangles);
            Assert.Equal(0, qr.FlippedQuads);
            Assert.Equal(0, CheckMap(qr, true));
            Assert.True(qr.Result.Validate().IsValid, name);
            for (int v = 0; v < qr.Result.VertexCount; ++v) Assert.True(qr.Result.GetVertex(v).IsFinite);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            Assert.Equal(1.0, m.QuadFraction);
            Assert.True(m.IsManifold, name);
            Assert.Equal(0, m.BoundaryVertices);
            Assert.Equal(euler, m.EulerCharacteristic);
            Assert.True(m.Quads >= 40, name + ": " + m.Quads + " quads");
            Assert.True(Invariance.Volume(qr.Result.ToDMesh3(out _)) > 0, name + ": inward");
            Assert.True(OneSidedHausdorff(qr.Result, Shape(name)) <= 0.5 * s, name + ": Hausdorff");
            Assert.InRange(qr.PureQuadAreaRatio, 0.75, 1.35);      // the map covers the surface about once
        }

        [Theory]
        [InlineData("cylinder", 0.2, PureQuadStatus.FlippedFaces, 2)]
        [InlineData("cylinder", 0.29, PureQuadStatus.FlippedFaces, 2)]
        [InlineData("cylinder", 0.19, PureQuadStatus.OrientationFolds, 2)]
        [InlineData("sphere", 0.39, PureQuadStatus.BudgetExceeded, 2)]
        [InlineData("torus", 0.38, PureQuadStatus.OrientationFolds, 0)]
        public void SecondAttempt_AfterAFallback_GivesCleanPureQuads(string name, double s, PureQuadStatus first, int euler)
        {
            // default options, no hook: the first attempt falls back for the reason named, the second one (the fixed variables of the flow take the
            // rounded values of the relaxation in which they are free) passes every check
            QuadRemesher qr = Run(name, s);
            Assert.True(qr.PureQuadRetried);
            Assert.Equal(first, qr.PureQuadFirstStatus);
            AssertCleanPureQuads(qr, name, s, euler);
            Assert.True(qr.PureQuadRepair.FreeFrozenEdges);
            Assert.True(qr.PureQuadRepair.FrozenEdges > 0);
            Assert.InRange(qr.PureQuadRepair.FrozenEdgesChanged, 1, qr.PureQuadRepair.FrozenEdges);
            Assert.Same(qr.PureQuadRepair.Result, qr.Result);
            Assert.Equal(qr.PureQuadRepair.AreaRatio, qr.PureQuadAreaRatio);
            // few irregular vertices, as for a first attempt that succeeds
            int sing = FieldOf(name).SingularFaces.Count;
            QuadRemesher stage2 = Run(name, s, 0, false);
            Assert.True(PolyMeshMetrics.Compute(qr.Result).IrregularVertices <= sing + (int)Math.Floor(0.1 * PolyMeshMetrics.Compute(stage2.Result).IrregularVertices), name);
            // without the second attempt: the fallback of before, with the same first status
            QuadRemesher old = Run(name, s, 0, true, q => q.PureQuadRetry = false);
            AssertFallback(old, stage2, name);
            Assert.Equal(first, old.PureQuadStatus);
            Assert.False(old.PureQuadRetried);
            Assert.False(old.PureQuadRepair.FreeFrozenEdges);
            Assert.Equal(0, old.PureQuadRepair.FrozenEdgesChanged);
            // the times of both attempts are counted (the first attempt has a flow, and here a repair too, so both sums are larger than the second's part)
            Assert.True(qr.FlowSeconds > qr.PureQuadRepair.FlowSeconds && qr.RepairSeconds > qr.PureQuadRepair.RepairSeconds);
        }

        [Theory]
        [InlineData("sphere", 0.29, 0)]
        [InlineData("sphere", 0.2, 0)]
        [InlineData("torus", 0.18, 0)]
        [InlineData("torus-aligned", 0.2, 10)]
        [InlineData("box", 0.25, 0)]
        [InlineData("cylinder", 0.18, 0)]
        [InlineData("cylinder", 0.22, 0)]
        [InlineData("cylinder", 0.26, 0)]
        public void SecondAttempt_IsNotRun_WhenTheFirstSucceeds_AndTheResultIsTheSameBitForBit(string name, double s, double align)
        {
            QuadRemesher with = Run(name, s, align), without = Run(name, s, align, true, q => q.PureQuadRetry = false);
            Assert.Equal(PureQuadStatus.Success, without.PureQuadStatus);
            Assert.Equal(PureQuadStatus.Success, with.PureQuadStatus);
            Assert.Equal(PureQuadStatus.Success, with.PureQuadFirstStatus);
            Assert.False(with.PureQuadRetried);
            Assert.False(with.PureQuadRepair.FreeFrozenEdges);
            Assert.Equal(0, with.PureQuadRepair.FrozenEdgesChanged);
            Assert.Equal(without.PureQuadRepair.Offsets(), with.PureQuadRepair.Offsets());
            Assert.Equal(without.PureQuadRepair.SatDecisions, with.PureQuadRepair.SatDecisions);
            Assert.Equal(BitConverter.DoubleToInt64Bits(without.PureQuadAreaRatio), BitConverter.DoubleToInt64Bits(with.PureQuadAreaRatio));
            AssertSame(without.Result, with.Result);
        }

        [Theory]
        [InlineData("sphere", 0.27)]
        [InlineData("torus", 0.3)]
        public void SecondAttempt_ThatFailsToo_ReportsTheFirstAttempt_AndGivesTheStage2Mesh(string name, double s)
        {
            // without the sign penalty both attempts keep folds here: everything reported is the first attempt's, as without the second
            QuadRemesher both = Run(name, s, 0, true, q => q.SignPenalty = false);
            QuadRemesher firstOnly = Run(name, s, 0, true, FirstAttemptWithoutSignPenalty);
            QuadRemesher stage2 = Run(name, s, 0, false);
            Assert.True(both.PureQuadRetried);
            AssertFallback(both, stage2, name);
            Assert.Equal(firstOnly.PureQuadStatus, both.PureQuadStatus);
            Assert.Equal(both.PureQuadFirstStatus, both.PureQuadStatus);
            Assert.False(both.PureQuadRepair.FreeFrozenEdges);
            Assert.Equal(firstOnly.PureQuadRepair.Offsets(), both.PureQuadRepair.Offsets());
            Assert.Equal(firstOnly.InvertedTriangles, both.InvertedTriangles);
            Assert.Equal(firstOnly.FlippedQuads, both.FlippedQuads);
            Assert.True(both.InvertedTriangles > 0);
            Assert.Equal(BitConverter.DoubleToInt64Bits(firstOnly.PureQuadAreaRatio), BitConverter.DoubleToInt64Bits(both.PureQuadAreaRatio));
            Assert.Equal(BitConverter.DoubleToInt64Bits(firstOnly.PureQuadAreaRatioAfterFlow), BitConverter.DoubleToInt64Bits(both.PureQuadAreaRatioAfterFlow));
        }

        [Fact]
        public void SecondAttempt_Compute_CanBeCalledAgain()
        {
            // two runs on one instance, then a length where the first attempt succeeds, then back: no state of the second attempt is kept
            var qr = new QuadRemesher(Shape("cylinder")) { Field = FieldOf("cylinder"), TargetEdgeLength = 0.2, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.True(qr.PureQuadRetried);
            AssertCleanPureQuads(qr, "cylinder", 0.2, 2);
            DPolyMesh first = qr.Result; int[] offsets = qr.PureQuadRepair.Offsets(); QuadFlowRepair firstRepair = qr.PureQuadRepair;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.True(qr.PureQuadRetried);
            Assert.NotSame(first, qr.Result); Assert.NotSame(firstRepair, qr.PureQuadRepair);
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadStatus);
            Assert.Equal(PureQuadStatus.FlippedFaces, qr.PureQuadFirstStatus);
            Assert.Equal(offsets, qr.PureQuadRepair.Offsets());
            AssertSame(first, qr.Result);
            AssertSame(Run("cylinder", 0.2).Result, qr.Result);        // and the same as a fresh instance
            qr.TargetEdgeLength = 0.22;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.False(qr.PureQuadRetried);
            Assert.Equal(PureQuadStatus.Success, qr.PureQuadFirstStatus);
            AssertSame(Run("cylinder", 0.22).Result, qr.Result);
            qr.TargetEdgeLength = 0.2;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.True(qr.PureQuadRetried);
            AssertSame(first, qr.Result);
            qr.PureQuads = false;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.False(qr.PureQuadRetried);
            Assert.Equal(PureQuadStatus.NotRequested, qr.PureQuadFirstStatus);
        }

        [Theory, MemberData(nameof(Frames))]
        public void SecondAttempt_IsInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            const string shape = "cylinder"; const double s = 0.2;
            QuadRemesher baseRun = Run(shape, s);
            Assert.True(baseRun.PureQuadRetried);
            Assert.Equal(PureQuadStatus.Success, baseRun.PureQuadStatus);
            DMesh3 t = Invariance.Transformed(Shape(shape), sim);
            var cf = new CrossField(t);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(t) { Field = cf, TargetEdgeLength = sim.Scale * s, PureQuads = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            if (sim.IsExact) {
                // a power of two scale: the same first status, the same relaxation and rounding (integers), the same mesh bit for bit, scaled
                Assert.True(qr.PureQuadRetried);
                Assert.Equal(baseRun.PureQuadFirstStatus, qr.PureQuadFirstStatus);
                Assert.Equal(baseRun.PureQuadStatus, qr.PureQuadStatus);
                Assert.Equal(baseRun.PureQuadRepair.FrozenEdges, qr.PureQuadRepair.FrozenEdges);
                Assert.Equal(baseRun.PureQuadRepair.FrozenEdgesChanged, qr.PureQuadRepair.FrozenEdgesChanged);
                Assert.Equal(baseRun.PureQuadRepair.Offsets(), qr.PureQuadRepair.Offsets());
                AssertSame(baseRun.Result, qr.Result, sim.Scale);
                Assert.Equal(BitConverter.DoubleToInt64Bits(baseRun.PureQuadAreaRatio), BitConverter.DoubleToInt64Bits(qr.PureQuadAreaRatio));
            } else {
                // another rounding: a clean pure-quad mesh of about the same size (by the first or the second attempt), or a valid stage 2
                // mesh after two attempts
                Assert.True(qr.Result.Validate().IsValid);
                if (qr.PureQuadStatus != PureQuadStatus.Success) { Assert.True(qr.PureQuadRetried); Assert.False(qr.PureQuadRepair.FreeFrozenEdges); return; }
                Assert.Equal(qr.PureQuadRetried, qr.PureQuadRepair.FreeFrozenEdges);
                PolyMeshMetrics a = PolyMeshMetrics.Compute(baseRun.Result), b = PolyMeshMetrics.Compute(qr.Result);
                Assert.Equal(1.0, b.QuadFraction);
                Assert.True(b.IsManifold);
                Assert.Equal(0, b.BoundaryVertices);
                Assert.Equal(a.EulerCharacteristic, b.EulerCharacteristic);
                Assert.InRange(b.Faces, 0.7 * a.Faces, 1.4 * a.Faces);
                Assert.Equal(0, qr.InvertedTriangles);
                for (int v = 0; v < qr.Result.VertexCount; ++v) Assert.True(qr.Result.GetVertex(v).IsFinite);
                Assert.True(OneSidedHausdorff(qr.Result, t) <= 0.5 * sim.Scale * s, name + ": Hausdorff");
            }
        }

        [Fact]
        public void Repair_FreeFrozenEdges_ChangesOnlyFixedVariables_AndNothingWithoutThem()
        {
            // the repair alone: the flag makes the capped cylinder clean, and reports what it did
            QuadFlowRepair plain = Repair("cylinder", 0.2), freed = Repair("cylinder", 0.2, 0, r => r.FreeFrozenEdges = true);
            Assert.Equal(PureQuadStatus.FlippedFaces, plain.Status);
            Assert.Equal(PureQuadStatus.Success, freed.Status);
            Assert.Equal(plain.FrozenEdges, freed.FrozenEdges);
            Assert.True(plain.FrozenEdges > 0);
            Assert.Equal(0, plain.FrozenEdgesChanged);
            Assert.InRange(freed.FrozenEdgesChanged, 1, freed.FrozenEdges);
            Assert.True(freed.AreaRatioAfterFlow > plain.AreaRatioAfterFlow);       // the freed map keeps more of the lattice
            // a field without a holonomy on any cycle of the dual tree has no fixed variable: the flag changes nothing
            var c = Cases["torus-aligned"];
            QuadFlowRepair a = Repair("torus-aligned", c.s, c.align), b = Repair("torus-aligned", c.s, c.align, r => r.FreeFrozenEdges = true);
            Assert.Equal(0, a.FrozenEdges); Assert.Equal(0, b.FrozenEdges); Assert.Equal(0, b.FrozenEdgesChanged);
            Assert.Equal(a.Offsets(), b.Offsets());
            Assert.Equal(PureQuadStatus.Success, b.Status);
            // the relaxation runs at the start range of the variables whatever capacity the network flow needs afterwards
            QuadFlowRepair fine = Repair("sphere", 0.05, 0, r => r.FreeFrozenEdges = true);
            Assert.True(fine.FrozenEdgesChanged > 0);
            Assert.NotEqual(PureQuadStatus.FlowInfeasible, fine.Status);
            // its counters: one flow on four nodes per triangle; the searches are counted, never timed
            Assert.Equal(0, plain.RelaxationArcs); Assert.Equal(0L, plain.RelaxationSettled); Assert.Equal(0.0, plain.RelaxationSeconds);
            Assert.True(freed.RelaxationArcs > 0 && freed.RelaxationSearches > 0 && freed.RelaxationSettled > 0 && freed.RelaxationSeconds > 0);
            Assert.True(freed.RelaxationSeconds <= freed.FlowPrepSeconds);
            // the work stays near linear in the triangles (each search stops at the first deficit; 15 to 80 settled nodes per triangle on the gate runs)
            foreach (QuadFlowRepair r in new[] { freed, fine })
                Assert.True(r.RelaxationSettled < 200L * r.Triangles, $"relaxation settled {r.RelaxationSettled} nodes in {r.RelaxationSearches} searches for {r.Triangles} triangles");
        }

        [Fact]
        public void RoundHalfStep_RoundsAHalfTowardsTheRealOffset()
        {
            // even: the exact step
            Assert.Equal(1, QuadFlowRepair.RoundHalfStep(0, 2, -5.0));
            Assert.Equal(-2, QuadFlowRepair.RoundHalfStep(1, -6, 9.0));
            Assert.Equal(1, QuadFlowRepair.RoundHalfStep(1, 0, 0.0));
            // odd: the two neighbours of v + x2 / 2, the one nearer to r
            Assert.Equal(1, QuadFlowRepair.RoundHalfStep(0, 1, 0.9));        // 0.5 -> 1
            Assert.Equal(0, QuadFlowRepair.RoundHalfStep(0, 1, 0.1));        // 0.5 -> 0
            Assert.Equal(-1, QuadFlowRepair.RoundHalfStep(0, -1, -0.9));     // -0.5 -> -1
            Assert.Equal(0, QuadFlowRepair.RoundHalfStep(0, -1, -0.1));      // -0.5 -> 0
            Assert.Equal(2, QuadFlowRepair.RoundHalfStep(0, 3, 1.9));        // 1.5 -> 2
            Assert.Equal(1, QuadFlowRepair.RoundHalfStep(0, 3, 1.2));        // 1.5 -> 1
            Assert.Equal(-2, QuadFlowRepair.RoundHalfStep(0, -3, -1.9));     // -1.5 -> -2
            Assert.Equal(-1, QuadFlowRepair.RoundHalfStep(0, -3, -1.2));     // -1.5 -> -1
            Assert.Equal(-1, QuadFlowRepair.RoundHalfStep(-2, 3, -0.9));     // -0.5 -> -1 from v = -2
            // a tie, or a real offset that is not a number: the neighbour on the side of the start value
            Assert.Equal(0, QuadFlowRepair.RoundHalfStep(0, 1, 0.5));
            Assert.Equal(0, QuadFlowRepair.RoundHalfStep(0, -1, -0.5));
            Assert.Equal(1, QuadFlowRepair.RoundHalfStep(0, 3, 1.5));
            Assert.Equal(-1, QuadFlowRepair.RoundHalfStep(0, -3, double.NaN));
        }
    }
}
