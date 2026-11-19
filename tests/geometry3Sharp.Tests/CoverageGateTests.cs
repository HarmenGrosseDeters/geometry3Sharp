using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Tests for the lines that the nightly changed-line coverage gate (tools/changed_line_coverage.py, docs/QA.md 2.6) found
    // uncovered in run 44232637032 (2026-11-18): small, deterministic cases of branches that no other test reached. Each test
    // checks the behaviour of its branch, not only that it runs.
    public class CoverageGateTests
    {
        // ---------------------------------------------------------------- mesh/SimpleQuadMesh.cs (DPolyMesh, DPolyMeshBuilder)

        [Fact]
        public void DPolyMesh_Clear_EmptiesEverything_AndTheMeshIsReusable()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            Assert.True(m.VertexCount > 0 && m.FaceCount > 0);
            m.Clear();
            Assert.Equal(0, m.VertexCount);
            Assert.Equal(0, m.FaceCount);
            Assert.Empty(m.FaceIndices);
            Assert.Empty(m.FaceGroups);
            Assert.Equal(new[] { 0 }, m.FaceOffsets.ToArray());     // the offsets start again with their leading 0
            // reused: the first face of the cleared mesh is face 0 again, with its own vertices
            int a = m.AppendVertex(0, 0, 0), b = m.AppendVertex(1, 0, 0), c = m.AppendVertex(0, 1, 0);
            Assert.Equal(0, m.AppendTriangle(a, b, c, 7));
            Assert.Equal(new[] { 0, 3 }, m.FaceOffsets.ToArray());
            Assert.Equal(new[] { 7 }, m.FaceGroups.ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, m.FaceIndices.ToArray());
        }

        [Fact]
        public void DPolyMesh_TriangulateFace_WithoutAnEar_CutsCornerOne()
        {
            // a self-overlapping pentagon (nonzero fan normal) that has no ear at its first step: no convex corner whose
            // triangle is free of the other vertices. The rule then cuts corner 1 anyway, and goes on, so that the face
            // still gives n - 2 triangles over its own vertices (Python replica of the rule: [(0,1,2), (0,2,3), (0,3,4)])
            var m = new DPolyMesh();
            double[,] p = { { 4, 2 }, { 6, 6 }, { 2, 6 }, { 3, 6 }, { 6, 0 } };
            for (int i = 0; i < 5; ++i) m.AppendVertex(p[i, 0], p[i, 1], 0);
            int f = m.AppendFace(new[] { 0, 1, 2, 3, 4 });
            Assert.True(m.IsFaceValid(f));
            var tris = new List<Index3i>();
            Assert.Equal(3, m.TriangulateFace(f, tris));
            Assert.Equal(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3), new Index3i(0, 3, 4) }, tris.ToArray());
        }

        [Fact]
        public void DPolyMeshBuilder_CollectsMeshes_AndDropsTheRest()
        {
            var b = new DPolyMeshBuilder();
            Assert.Null(b.Mesh);
            // a mesh from the reader callbacks: vertices as coordinates and as NewVertexInfo, triangles with a negative group
            Assert.Equal(0, b.AppendNewMesh(true, true, true, true));
            Assert.Equal(0, b.AppendVertex(0, 0, 0));
            Assert.Equal(1, b.AppendVertex(new NewVertexInfo(new Vector3d(1, 0, 0), new Vector3f(0, 0, 1))));
            Assert.Equal(2, b.AppendVertex(0, 1, 0));
            Assert.Equal(0, b.AppendTriangle(0, 1, 2));
            Assert.Equal(1, b.AppendTriangle(0, 1, 2, -5));          // a negative group is group 0
            Assert.Equal(2, b.AppendTriangle(0, 1, 2, 4));
            Assert.Equal(new[] { 0, 0, 4 }, b.Mesh.FaceGroups.ToArray());
            Assert.Equal(new Vector3d(1, 0, 0), b.Mesh.GetVertex(1));
            // a second mesh, from a DMesh3, becomes the active one; SetActiveMesh goes back
            var tri = new DMesh3();
            tri.AppendVertex(new Vector3d(0, 0, 0)); tri.AppendVertex(new Vector3d(2, 0, 0)); tri.AppendVertex(new Vector3d(0, 2, 0));
            tri.AppendTriangle(0, 1, 2);
            Assert.Equal(1, b.AppendNewMesh(tri));
            Assert.Equal(2, b.Meshes.Count);
            Assert.Same(b.Meshes[1], b.Mesh);
            Assert.Equal(1, b.Mesh.FaceCount);
            Assert.Equal(new Vector3d(2, 0, 0), b.Mesh.GetVertex(1));
            b.SetActiveMesh(0);
            Assert.Same(b.Meshes[0], b.Mesh);
            // polygons are kept whole; materials and meta data are not supported
            b.AppendPolygon(new[] { 0, 1, 2 }, 3);
            Assert.Equal(4, b.Mesh.FaceCount);
            Assert.Equal(3, b.Mesh.FaceGroups[3]);
            Assert.Equal(-1, b.BuildMaterial(null));
            b.AssignMaterial(-1, 0);
            Assert.False(b.SupportsMetaData);
            b.AppendMetaData("x", 1);
            Assert.Equal(4, b.Mesh.FaceCount);
        }

        // ---------------------------------------------------------------- io/MeshIOUtil.cs (PolyMeshIO)

        [Fact]
        public void PolyMeshIO_ReadOBJ_VertexLineWithoutThreeNumbers_IsSkippedWithAWarning()
        {
            var warnings = new List<string>();
            DPolyMesh m;
            // line 2 has two coordinates, line 3 a word: both skipped, so the face of line 6 is the only valid one and "f 1 2 4" of line 7 (corner 4 does not exist) is dropped
            string obj = "v 0 0 0\nv 1 0\nv 1 x 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\nf 1 2 4\n";
            IOReadResult r = PolyMeshIO.ReadOBJ(new StringReader(obj), ReadOptions.Defaults, out m, (s, e) => warnings.Add(s));
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Equal(3, m.VertexCount);
            Assert.Equal(1, m.FaceCount);                   // "f 1 2 4": the corner 4 does not exist
            Assert.Contains("[PolyMeshIO] OBJ line 2: vertex skipped", warnings);
            Assert.Contains("[PolyMeshIO] OBJ line 3: vertex skipped", warnings);
            // without a handler the same text reads the same
            Assert.Equal(IOCode.Ok, PolyMeshIO.ReadOBJ(new StringReader(obj), ReadOptions.Defaults, out DPolyMesh m2).code);
            Assert.Equal(3, m2.VertexCount);
        }

        [Fact]
        public void PolyMeshIO_ArraySegmentList_IsAReadOnlyWindowOfTheList()
        {
            // (the window that ReadOBJ hands to DPolyMesh.AppendFace; its mutators must refuse, its reads see only the window)
            Type t = typeof(PolyMeshIO).GetNestedType("ArraySegmentList", BindingFlags.NonPublic);
            Assert.NotNull(t);
            var backing = new List<int> { 9, 8, 7, 6, 5, 4 };
            var w = (IList<int>)Activator.CreateInstance(t, backing, 2, 3);
            Assert.Equal(3, w.Count);
            Assert.True(w.IsReadOnly);
            Assert.Equal(7, w[0]); Assert.Equal(6, w[1]); Assert.Equal(5, w[2]);
            Assert.Equal(new[] { 7, 6, 5 }, w.ToArray());
            var viaNonGeneric = new List<int>();
            foreach (object o in (IEnumerable)w) viaNonGeneric.Add((int)o);
            Assert.Equal(new[] { 7, 6, 5 }, viaNonGeneric.ToArray());
            var copy = new int[5];
            w.CopyTo(copy, 1);
            Assert.Equal(new[] { 0, 7, 6, 5, 0 }, copy);
            Assert.Throws<NotSupportedException>(() => w[0] = 1);
            Assert.Throws<NotSupportedException>(() => w.IndexOf(7));
            Assert.Throws<NotSupportedException>(() => w.Insert(0, 1));
            Assert.Throws<NotSupportedException>(() => w.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => w.Add(1));
            Assert.Throws<NotSupportedException>(() => w.Clear());
            Assert.Throws<NotSupportedException>(() => w.Contains(7));
            Assert.Throws<NotSupportedException>(() => w.Remove(7));
            Assert.Equal(new[] { 9, 8, 7, 6, 5, 4 }, backing.ToArray());    // nothing changed
        }

        // ---------------------------------------------------------------- mesh_ops/LazyExactMesh.cs

        [Fact]
        public void LazyExactMesh_WindingPredicate_RefusesAWrongNumberOfWindingNumbers()
        {
            DMesh3 box = new DMesh3();
            for (int i = 0; i < 8; ++i)
                box.AppendVertex(new Vector3d(i & 1, (i >> 1) & 1, (i >> 2) & 1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) { box.AppendTriangle(q[0], q[1], q[2]); box.AppendTriangle(q[0], q[2], q[3]); }
            ExactMesh e = ExactMesh.FromDMesh(box).Union(ExactMesh.FromDMesh(box));
            Func<int[], bool> pred = e.WindingPredicate();
            Assert.True(pred(new[] { 1, 0 }));
            ArgumentException tooFew = Assert.Throws<ArgumentException>(() => pred(new[] { 1 }));
            Assert.Contains("1 winding numbers for 2 sources", tooFew.Message);
            ArgumentException tooMany = Assert.Throws<ArgumentException>(() => pred(new[] { 1, 0, 0 }));
            Assert.Contains("3 winding numbers for 2 sources", tooMany.Message);
            ArgumentException none = Assert.Throws<ArgumentException>(() => pred(null));
            Assert.Contains("0 winding numbers for 2 sources", none.Message);
            Assert.False(pred(new[] { 0, 0 }));             // the predicate is still usable after the refusals
        }

        // ---------------------------------------------------------------- mesh_ops/TetMeshImprover.cs (Diagnose)

        [Fact]
        public void TetMeshImprover_Diagnose_NamesInteriorVerticesWhoseSmoothingDoesNotHelp()
        {
            // one improvement pass with splits leaves tets that have an interior vertex which the (dry) smoothing cannot improve:
            // the line of such a tet carries "Si" for that vertex, and the count says how often
            var m = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")) {
                RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha, SplitEdges = true, ContractEdges = false, ImprovePasses = 1, RescuePasses = 0, DiagnoseImprovement = true };
            Assert.True(m.Compute());
            int si = m.DiagnosticLines.Sum(l => l.Split(' ').Count(t => t == "Si"));
            Assert.True(si > 0, "no interior vertex without a better position among " + m.DiagnosticLines.Count + " tets");
            Assert.Equal(si, m.Counts["improve_diag_smooth_interior_not_better"]);
            // and the diagnosis changes nothing
            var plain = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")) {
                RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha, SplitEdges = true, ContractEdges = false, ImprovePasses = 1, RescuePasses = 0 };
            Assert.True(plain.Compute());
            Assert.Equal(plain.Result.Vertices, m.Result.Vertices);
            Assert.Equal(plain.Result.Tets, m.Result.Tets);
        }

        // ---------------------------------------------------------------- mesh_ops/CrossField.cs

        [Fact]
        public void CrossField_VertexFrame_IsAnOrthonormalTangentFrame()
        {
            DMesh3 mesh = CrossFieldShapes.UnitSphere();
            var cf = new CrossField(mesh);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            int checkedVertices = 0;
            foreach (int v in mesh.VertexIndices()) {
                if (!cf.HasField(v)) continue;
                Vector3d n = cf.VertexNormal(v), e = cf.VertexTangent(v);
                Assert.Equal(1.0, e.Length, 12);
                Assert.Equal(1.0, n.Length, 12);
                Assert.True(Math.Abs(e.Dot(n)) < 1e-12, "the tangent is not tangent at vertex " + v);
                // on the unit sphere the area weighted normal points along the position (up to the mesh's discretization)
                Assert.True(n.Dot(mesh.GetVertex(v).Normalized) > 0.99);
                ++checkedVertices;
            }
            Assert.Equal(mesh.VertexCount, checkedVertices);
        }

        [Fact]
        public void CrossField_HugeCoordinates_FailWithSolverFailed_AndTheInstanceRecovers()
        {
            // areas of about 1e300 leave the range of the sums that follow: the mass shift of the matrix is NaN, no pivot is positive
            DMesh3 mesh = CrossFieldShapes.UnitSphere();
            var original = new List<Vector3d>();
            foreach (int v in mesh.VertexIndices()) original.Add(mesh.GetVertex(v));
            foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, original[v] * 1e150);
            var cf = new CrossField(mesh);
            Assert.Equal(CrossFieldStatus.SolverFailed, cf.Compute());
            Assert.Contains("the field matrix could not be factorized", cf.Error);
            Assert.False(cf.HasField(0));
            // the same instance computes again once the mesh is back in range (nothing of the failed run is left over)
            foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, original[v]);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            Assert.Null(cf.Error);
            Assert.True(cf.HasField(0));
        }

        [Fact]
        public void CrossField_Gauge_WithoutAMean_FallsBackToTheLargestVertexWithTheLowestId()
        {
            // the phase of a component with no alignment terms is the one that makes the mean of z real; where the mean vanishes
            // (here: half of the vertices carry z, half -z) it is the vertex with the lowest id among those that have at least half
            // the largest magnitude. After the gauge z of that vertex is real positive and the root mean square of |z| is 1.
            var pm = new ParamMesh(CrossFieldShapes.OpenPatch(3));
            int n = pm.N;
            Assert.Equal(0, n % 2);
            double psi = 0.7, a = 3.0;
            var x = new double[2 * n];
            for (int i = 0; i < n; ++i) {
                double s = i % 2 == 0 ? 1 : -1;
                x[2 * i] = s * a * Math.Cos(psi); x[2 * i + 1] = s * a * Math.Sin(psi);
            }
            // one vertex below half the largest magnitude must not take part in the choice of the reference: make vertex 0 small
            // (it has the lowest id, and would win otherwise), and the rest of the pairs stay balanced by scaling its partner alike
            x[0] *= 0.1; x[1] *= 0.1; x[2] *= 0.1; x[3] *= 0.1;
            int reference = -1, lowest = int.MaxValue;
            for (int i = 2; i < n; ++i)
                if (pm.ToVertex[i] < lowest) { lowest = pm.ToVertex[i]; reference = i; }
            var comp = new int[n];
            double[] mass = Enumerable.Repeat(1.0, n).ToArray();
            double[] before = (double[])x.Clone();
            MethodInfo gauge = typeof(CrossField).GetMethod("Gauge", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(gauge);
            gauge.Invoke(null, new object[] { x, comp, mass, new[] { 0 }, new[] { 1.0 }, pm });
            // the reference vertex: real positive; its magnitude is a / rms, with rms from the balanced input
            Assert.True(x[2 * reference] > 0);
            Assert.True(Math.Abs(x[2 * reference + 1]) < 1e-12 * x[2 * reference]);
            double rms = 0;
            for (int i = 0; i < n; ++i) rms += x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1];
            Assert.Equal(1.0, Math.Sqrt(rms / n), 12);
            // the magnitudes keep their ratios (the gauge multiplies the whole component by one factor)
            double rmsBefore = Math.Sqrt(before.Select(v => v * v).Sum() / n);
            for (int i = 0; i < n; ++i) {
                double mb = Math.Sqrt(before[2 * i] * before[2 * i] + before[2 * i + 1] * before[2 * i + 1]);
                double ma = Math.Sqrt(x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1]);
                Assert.Equal(mb / rmsBefore, ma, 10);
            }
        }

        // ---------------------------------------------------------------- mesh_ops/MeshRobustRepair.cs

        static DMesh3 FlatDisc()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            foreach (int v in m.VertexIndices().ToList()) { Vector3d p = m.GetVertex(v); m.SetVertex(v, new Vector3d(p.x, p.y, 0)); }
            return m;
        }

        [Fact]
        public void MeshRobustRepair_ReductionToTheTargetNotCertified_KeepsTheSimplifiedWrap()
        {
            // ReduceToTarget gives null when its result is not clean or not certified within the distance (or on a cancel); Apply()
            // then keeps the simplified wrap and says so. Forced here with a cancel flag that is up only while the reduction runs
            // (after the simplification's phase ended, from the second query on: the first one is the check right after that phase,
            // which a real cancel would take as the end of Apply(); the phase of the target ends with the reduction)
            Func<MeshRobustRepair> make = () => new MeshRobustRepair(FlatDisc()) { SolidifyResolution = 16, WrapRelativeAlpha = 40, WrapRelativeOffset = 400 };
            MeshRobustRepair untargeted = make();
            Assert.True(untargeted.Apply(), untargeted.ResultReason);
            Assert.True(untargeted.UsedAlphaWrap);

            MeshRobustRepair r = make();
            r.WrapTargetTriangles = 300;
            int queriesInTargetPhase = 0;
            r.CancelF = () => {
                if (!r.PhaseMilliseconds.ContainsKey("wrap.simplify") || r.PhaseMilliseconds.ContainsKey("wrap.target")) return false;
                return ++queriesInTargetPhase > 1;
            };
            Assert.True(r.Apply(), r.ResultReason);
            Assert.True(r.UsedAlphaWrap);
            Assert.True(queriesInTargetPhase > 1);
            Assert.Equal(MeshRobustRepair.TargetLimitNotCertified, r.AlphaWrapTargetLimit);
            Assert.Equal(300, r.AlphaWrapTarget);
            // the simplified wrap is the result, unreduced: the very mesh of the run without a target
            Assert.Equal(untargeted.Mesh.TriangleCount, r.Mesh.TriangleCount);
            Assert.Equal(r.Mesh.TriangleCount, r.AlphaWrapTargetReached);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh, true));
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(untargeted.Mesh), AlphaWrapManifoldStepTests.Hash(r.Mesh));
        }

        // ---------------------------------------------------------------- mesh_ops/MeshRoundingRepair.cs

        [Fact]
        public void MeshRoundingRepair_MaxEvaluations_IsTheCapOfTheLastApply()
        {
            // two overlapping spheres (their surfaces intersect along a curve: initial defects): the cap on the evaluations is 10000 plus MaxEvaluationsPerDefect per initial defect, and is set again by every Apply
            DMesh3 sa = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh(), sb = new DMesh3(sa);
            MeshTransforms.Translate(sb, new Vector3d(0.3, 0.2, 0.1));
            DMesh3 both = new DMesh3(sa);
            new MeshEditor(both).AppendMesh(sb);
            var r = new MeshRoundingRepair(both);
            Assert.Equal(0L, r.MaxEvaluations);                          // before any Apply
            r.Apply();
            int defects = r.InitialIntersections + r.InitialDegenerate + r.InitialFalseContacts;
            Assert.True(defects > 0, "the input has no defect");
            Assert.Equal(10000L + 50L * defects, r.MaxEvaluations);
            Assert.True(r.Evaluations <= r.MaxEvaluations + 1);          // (the evaluation that finds the cap reached counts)
            r.MaxEvaluationsPerDefect = 7;
            r.Apply();
            Assert.Equal(10000L + 7L * (r.InitialIntersections + r.InitialDegenerate + r.InitialFalseContacts), r.MaxEvaluations);
        }

        // ---------------------------------------------------------------- mesh_ops/MeshAlphaWrap.cs (pulled_pinch_point)

        // the private pinch point of PullPinchPoints, called on a computed wrap (its triangulation, oracle and offset stay after Compute):
        // the point of the offset projection of a circumcenter cc pulled back until it lies depth inside the circumsphere of cell c
        sealed class PinchProbe
        {
            readonly MeshAlphaWrap wrap;
            public readonly Delaunay3 Dt;
            readonly AlphaWrapOracle oracle;
            readonly MethodInfo project, pull;
            public readonly double Offset;

            public PinchProbe(MeshAlphaWrap w)
            {
                wrap = w;
                BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
                Type t = typeof(MeshAlphaWrap);
                Dt = (Delaunay3)t.GetField("dt", bf).GetValue(w);
                oracle = (AlphaWrapOracle)t.GetField("oracle", bf).GetValue(w);
                Offset = (double)t.GetField("offset", bf).GetValue(w);
                project = t.GetMethod("project_to_offset", bf);
                pull = t.GetMethod("pulled_pinch_point", bf);
                Assert.NotNull(Dt); Assert.NotNull(oracle); Assert.NotNull(project); Assert.NotNull(pull);
            }

            public double R(int c, Vector3d cc)
            {
                double r = double.MaxValue;
                for (int j = 0; j < 4; ++j) r = Math.Min(r, cc.Distance(Dt.GetVertex(Dt.CellVertex(c, j))));
                return r;
            }

            public bool Pull(int c, Vector3d cc, double depth, out Vector3d p)
            {
                object[] a = { c, cc, depth, null };
                bool ok = (bool)pull.Invoke(wrap, a);
                p = (Vector3d)a[3];
                return ok;
            }

            // the stage of the method that decides, from the same quantities
            public string Stage(int c, Vector3d cc, double depth)
            {
                if (!(MathUtil.IsFinite(cc.x) && MathUtil.IsFinite(cc.y) && MathUtil.IsFinite(cc.z))) return "nonfinite";
                double r = R(c, cc);
                if (!(r > depth)) return "no room";
                object[] a = { c, 0, cc, null };
                if (!(bool)project.Invoke(wrap, a)) return "no projection";
                Vector3d q = (Vector3d)a[3];
                double len = cc.Distance(q);
                if (!(len > r - depth)) return "q qualifies";
                Vector3d x = cc + (q - cc) * ((r - depth) / len);
                if (!(Dt.InCircumsphere(c, x) > 0)) return "outside the circumsphere";
                double dx = oracle.Distance(x);
                if (!(dx >= Offset && dx <= MeshAlphaWrap.PullMaxOffsets * Offset)) return "outside the band";
                return "pulled";
            }
        }

        [Fact]
        public void MeshAlphaWrap_PulledPinchPoint_RefusesEveryPointThatBreaksItsRules()
        {
            DMesh3 input = AlphaWrapTests.RandomSoup(42);
            var wrap = new MeshAlphaWrap(input) { RelativeAlpha = 20, RelativeOffset = 200, PullPinchPoints = true };
            Assert.True(wrap.Compute(), wrap.Error);
            var probe = new PinchProbe(wrap);
            Delaunay3 dt = probe.Dt;
            var cells = new List<int>();
            for (int c = 0; c < dt.MaxCellID; ++c)
                if (dt.IsCell(c)) cells.Add(c);
            Assert.True(cells.Count > 100);
            int cell = cells[cells.Count / 2];
            Vector3d centroid = Vector3d.Zero;
            for (int j = 0; j < 4; ++j) centroid += dt.GetVertex(dt.CellVertex(cell, j)) / 4;
            Vector3d p;

            // a circumcenter that is not finite
            Assert.Equal("nonfinite", probe.Stage(cell, new Vector3d(double.NaN, 0, 0), 0));
            Assert.False(probe.Pull(cell, new Vector3d(double.NaN, 0, 0), 0, out p));
            Assert.Equal(Vector3d.Zero, p);
            Assert.False(probe.Pull(cell, new Vector3d(0, double.PositiveInfinity, 0), 0, out p));

            // no room: the nearest cell vertex is not farther from cc than the depth
            Assert.Equal("no room", probe.Stage(cell, centroid, double.MaxValue / 2));
            Assert.False(probe.Pull(cell, centroid, double.MaxValue / 2, out p));
            Assert.False(probe.Pull(cell, centroid, probe.R(cell, centroid), out p));       // r > depth is strict

            // the projection itself qualifies (it lies at most r - depth from cc): that is the first candidate, not this one. The
            // scan over the cells and a few depths: whatever the pulled point would be (beyond the projection), none is returned
            Vector3d cc0 = dt.Circumcenter(cell);
            Assert.Equal("q qualifies", probe.Stage(cell, cc0, -1e9));
            Assert.False(probe.Pull(cell, cc0, -1e9, out p));
            int qualifying = 0;
            foreach (int c in cells) {
                Vector3d cc = dt.Circumcenter(c);
                foreach (double f in new[] { 0.0, 0.1, 0.3 }) {
                    double depth = f * probe.R(c, cc);
                    if (probe.Stage(c, cc, depth) != "q qualifies") continue;
                    Assert.False(probe.Pull(c, cc, depth, out p), "cell " + c + " depth " + depth);
                    ++qualifying;
                }
            }
            Assert.True(qualifying > 100, qualifying + " centers where the projection qualifies");

            // a center far from the cell: the point pulled towards the surface by r - depth is not inside the cell's circumsphere
            // (found by a scan of the cells: a center 100 offsets away from the cell, half the distance to it as the depth)
            int outside = -1; Vector3d outsideCc = Vector3d.Zero; double outsideDepth = 0;
            foreach (int c in cells) {
                Vector3d cn = Vector3d.Zero;
                for (int j = 0; j < 4; ++j) cn += dt.GetVertex(dt.CellVertex(c, j)) / 4;
                Vector3d cc = cn + new Vector3d(100 * probe.Offset, 0, 0);
                double depth = 0.5 * probe.R(c, cc);
                if (probe.Stage(c, cc, depth) == "outside the circumsphere") { outside = c; outsideCc = cc; outsideDepth = depth; break; }
            }
            Assert.True(outside >= 0, "no cell with a center that is refused for lying outside the circumsphere");
            Assert.False(probe.Pull(outside, outsideCc, outsideDepth, out p));
            Assert.Equal(Vector3d.Zero, p);

            // the positive control: a circumcenter whose pulled point qualifies is returned, strictly inside the circumsphere and
            // inside the band of the offset
            int pulled = 0;
            foreach (int c in cells) {
                Vector3d cc = dt.Circumcenter(c);
                double depth = 0.3 * probe.R(c, cc);
                if (probe.Stage(c, cc, depth) != "pulled") continue;
                Assert.True(probe.Pull(c, cc, depth, out p));
                Assert.True(dt.InCircumsphere(c, p) > 0);
                ++pulled;
                if (pulled == 5) break;
            }
            Assert.True(pulled > 0, "no circumcenter of the wrap has a pulled point");
        }

        // ---------------------------------------------------------------- parameterization/SpectralConformalParameterization.cs

        [Fact]
        public void SpectralConformal_HugeCoordinates_AreRefusedAsDegenerate_AndTheInstanceRecovers()
        {
            // the sums over weights and boundary lengths leave the range of double at 1e150 (trace not positive or not finite): no unit for the eigenvalues, so no
            // result (a status, not an exception or a map of NaNs)
            DMesh3 mesh = CrossFieldShapes.OpenPatch(6);
            var original = new List<Vector3d>();
            foreach (int v in mesh.VertexIndices()) original.Add(mesh.GetVertex(v));
            var sp = new SpectralConformalParameterization(mesh);
            ParameterizationResult ok = sp.Compute();
            Assert.Equal(ParameterizationStatus.Success, ok.Status);
            foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, original[v] * 1e150);
            ParameterizationResult r = sp.Compute();
            Assert.Equal(ParameterizationStatus.SolverFailed, r.Status);
            Assert.Contains("degenerate boundary or weights", r.Error);
            Assert.Null(r.UV);
            // the failed run leaves nothing behind: the same instance maps the mesh again once it is back in range
            foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, original[v]);
            ParameterizationResult again = sp.Compute();
            Assert.Equal(ParameterizationStatus.Success, again.Status);
            Assert.True(sp.Eigenvalue >= 0);
        }
    }
}
