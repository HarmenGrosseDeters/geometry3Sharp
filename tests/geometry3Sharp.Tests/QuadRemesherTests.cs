using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gs.PositionField, gs.QuadRemesher and gs.PolyMeshMetrics (docs/design/QUAD_REMESHING.md stage 2; Jakob et al. 2015): the
    /// polygon mesh of the procedural shapes (a manifold, quad-dominant, faithful mesh), boundaries as lattice constraints, the
    /// cleanup and the face tracing on small graphs, status of bad input, invariance, reuse and determinism (parallel equals serial).
    /// </summary>
    public class QuadRemesherTests
    {
        // the shapes have an edge of 0.1, the quads an edge of 0.2 (the ratio 0.5 of the benchmark)
        const double S = 0.2;

        static readonly Dictionary<string, CrossField> fields = new Dictionary<string, CrossField>();
        static readonly Dictionary<string, DMesh3> meshes = new Dictionary<string, DMesh3>();

        static DMesh3 Shape(string name)
        {
            lock (meshes) {
                DMesh3 m;
                if (!meshes.TryGetValue(name, out m)) {
                    switch (name) {
                        case "sphere": m = CrossFieldShapes.UnitSphere(); break;
                        case "torus": m = CrossFieldShapes.TorusMesh(); break;
                        case "box": m = CrossFieldShapes.FilletedBoxMesh(); break;
                        case "cap": m = CrossFieldShapes.SphereCap(); break;
                        case "patch": m = CrossFieldShapes.OpenPatch(24); break;
                        default: throw new ArgumentException(name);
                    }
                    meshes[name] = m;
                }
                return m;
            }
        }

        static CrossField FieldOf(string name)
        {
            lock (fields) {
                CrossField cf;
                if (!fields.TryGetValue(name, out cf)) {
                    cf = new CrossField(Shape(name));
                    Assert.Equal(CrossFieldStatus.Success, cf.Compute());
                    fields[name] = cf;
                }
                return cf;
            }
        }

        static QuadRemesher Run(string name, double s = -1, Action<QuadRemesher> configure = null)
        {
            if (s < 0) s = name == "patch" ? 0.08 : S;
            var qr = new QuadRemesher(Shape(name)) { Field = FieldOf(name), TargetEdgeLength = s };
            configure?.Invoke(qr);
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        // every success result: finite, valid faces with at most 6 sides, a manifold polygon mesh
        static PolyMeshMetrics CheckResult(DPolyMesh r, string what)
        {
            Assert.True(r.FaceCount > 0, what);
            Assert.True(r.Validate().IsValid, what + " " + r.Validate());
            for (int v = 0; v < r.VertexCount; ++v) Assert.True(r.GetVertex(v).IsFinite, what);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.MaxSides <= 6, what + ": a face with " + m.MaxSides + " sides");
            Assert.True(m.IsManifold, what + ": non-manifold edges " + m.NonManifoldEdges + ", inconsistent " + m.InconsistentEdges + ", vertices " + m.NonManifoldVertices);
            return m;
        }

        static double OneSidedHausdorff(DPolyMesh r, DMesh3 reference)
        {
            int[] map;
            DMesh3 tri = r.ToDMesh3(out map);
            var hd = new MeshHausdorffDistance(tri, reference) { Symmetric = false, RelativeTolerance = 1e-3 };
            hd.Compute();
            return hd.UpperBound;
        }

        // ---------------------------------------------------------------- the result on the procedural shapes

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        [InlineData("box")]
        public void ClosedShapes_GiveAManifoldQuadDominantMesh(string name)
        {
            QuadRemesher qr = Run(name);
            PolyMeshMetrics m = CheckResult(qr.Result, name);
            Assert.True(m.QuadFraction >= 0.88, name + ": quads " + m.QuadFraction);
            Assert.True(m.Quads > 100, name + ": " + m.Quads + " quads");
            double p95 = PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95);
            Assert.True(p95 <= 25, name + ": corner deviation p95 " + p95);
            Assert.True(PolyMeshMetrics.Percentile(m.Aspects, 0.95) <= 1.8, name + ": aspect");
            // faithful: the faces stay within half an edge of the input
            Assert.True(OneSidedHausdorff(qr.Result, Shape(name)) <= 0.5 * S, name + ": Hausdorff");
            // the quads have about the target size: the face count follows the area
            double area = 0;
            DMesh3 input = Shape(name);
            foreach (int t in input.TriangleIndices()) area += input.GetTriArea(t);
            double expected = area / (S * S);
            Assert.InRange(m.Faces, 0.8 * expected, 1.2 * expected);
            // without holes the Euler characteristic is the genus' (a hole is dropped faces: then only the manifold check applies)
            if (qr.DroppedFaces == 0 && m.BoundaryVertices == 0)
                Assert.Equal(name == "torus" ? 0 : 2, m.EulerCharacteristic);
        }

        [Fact]
        public void FilletedBox_HasFewIrregularVertices()
        {
            // the box is the case where the position field is close to its ideal: 8 cone singularities of the field
            QuadRemesher qr = Run("box");
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            int singular = FieldOf("box").SingularFaces.Count;
            Assert.True(m.IrregularVertices <= 2 * singular + 4 + 12, "irregular " + m.IrregularVertices + ", singular faces " + singular);
        }

        [Fact]
        public void Result_IsOrientedLikeTheInput()
        {
            // outward on a closed shape: the faces turn the same way as the input triangles nearest to them
            QuadRemesher qr = Run("sphere");
            DPolyMesh r = qr.Result;
            int outward = 0;
            for (int f = 0; f < r.FaceCount; ++f) {
                int[] fv = r.GetFace(f);
                Vector3d p0 = r.GetVertex(fv[0]), nw = Vector3d.Zero, c = Vector3d.Zero;
                for (int k = 1; k + 1 < fv.Length; ++k) nw += (r.GetVertex(fv[k]) - p0).Cross(r.GetVertex(fv[k + 1]) - p0);
                foreach (int v in fv) c += r.GetVertex(v);
                if (nw.Dot(c) > 0) outward++;
            }
            Assert.Equal(r.FaceCount, outward);
        }

        [Fact]
        public void OpenPatch_BoundaryAndCornersAreLatticeConstraints()
        {
            QuadRemesher qr = Run("patch");
            PolyMeshMetrics m = CheckResult(qr.Result, "patch");
            Assert.True(m.QuadFraction >= 0.85);
            Assert.True(m.BoundaryVertices > 20);
            DPolyMesh r = qr.Result;
            // the four corners of the patch are vertices of the result (a corner is a lattice point)
            foreach (var (x, y) in new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }) {
                Vector3d c = new Vector3d(x, y, 0.15 * PortableMath.Sin(3 * x) * PortableMath.Cos(2 * y));     // (CrossFieldShapes.OpenPatch)
                double best = double.MaxValue;
                for (int v = 0; v < r.VertexCount; ++v) best = Math.Min(best, (r.GetVertex(v) - c).Length);
                Assert.True(best < 1e-9, "corner " + x + "," + y + ": distance " + best);
            }
            // boundary vertices (those of edges that lie in one face): about half of them are exactly on the lattice line of the boundary
            // (the others are where the lattice rows next to the boundary and the boundary row do not meet: ragged, never far)
            var dist = new List<double>();
            var count = new Dictionary<long, int>();
            int nv = r.VertexCount;
            for (int f = 0; f < r.FaceCount; ++f) {
                int[] fv = r.GetFace(f);
                for (int k = 0; k < fv.Length; ++k) { int a = fv[k], b = fv[(k + 1) % fv.Length]; long key = a < b ? (long)a * nv + b : (long)b * nv + a; count.TryGetValue(key, out int c); count[key] = c + 1; }
            }
            int checkedVertices = 0;
            foreach (var kv in count) {
                if (kv.Value != 1) continue;
                foreach (int v in new[] { (int)(kv.Key / nv), (int)(kv.Key % nv) }) {
                    Vector3d p = r.GetVertex(v);
                    double d = Math.Min(Math.Min(Math.Abs(p.x), Math.Abs(1 - p.x)), Math.Min(Math.Abs(p.y), Math.Abs(1 - p.y)));
                    Assert.True(d < 0.5 * 0.08, "boundary vertex " + p + " is " + d + " away from the patch boundary");
                    dist.Add(d);
                    checkedVertices++;
                }
            }
            Assert.True(checkedVertices > 40);
            Assert.True(dist.Count(d => d < 1e-3 * 0.08) >= 0.5 * dist.Count, "on the boundary: " + dist.Count(d => d < 1e-3 * 0.08) + " of " + dist.Count);
            // the boundary edges are lattice lines: about the target length
            Assert.True(OneSidedHausdorff(r, Shape("patch")) <= 0.5 * 0.08);
        }

        [Fact]
        public void SphereCap_KeepsItsBoundaryLoop()
        {
            QuadRemesher qr = Run("cap");
            PolyMeshMetrics m = CheckResult(qr.Result, "cap");
            Assert.True(m.BoundaryVertices > 20, "boundary vertices " + m.BoundaryVertices);
            // the boundary of the cap is the circle z = 0.5: the boundary vertices of the result are near it
            DPolyMesh r = qr.Result;
            var count = new Dictionary<long, int>();
            int nv = r.VertexCount;
            for (int f = 0; f < r.FaceCount; ++f) {
                int[] fv = r.GetFace(f);
                for (int k = 0; k < fv.Length; ++k) { int a = fv[k], b = fv[(k + 1) % fv.Length]; long key = a < b ? (long)a * nv + b : (long)b * nv + a; count.TryGetValue(key, out int c); count[key] = c + 1; }
            }
            int onLoop = 0, total = 0;
            foreach (var kv in count) {
                if (kv.Value != 1) continue;
                foreach (int v in new[] { (int)(kv.Key / nv), (int)(kv.Key % nv) }) {
                    total++;
                    if (Math.Abs(r.GetVertex(v).z - 0.5) < 0.3 * S) onLoop++;
                }
            }
            Assert.True(onLoop >= 0.7 * total, onLoop + " of " + total + " boundary vertices near the boundary of the cap");
        }

        // a long strip 1 x (ny / 48): the boundary vertices of the two long sides face each other across interior edges
        internal static DMesh3 Strip(int ny)
        {
            var m = new DMesh3();
            int nx = 48;
            int[,] ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) ids[i, j] = m.AppendVertex(new Vector3d((double)i / nx, (double)j / nx, 0.1 * PortableMath.Sin(2 * i / (double)nx)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        [Fact]
        public void ThinStrip_BoundarySidesStayApart()
        {
            // boundary vertices only merge with their neighbours along the boundary, never across the strip
            DMesh3 m = Strip(3);
            double s = 0.0625, w = 3.0 / 48;
            var qr = new QuadRemesher(m) { TargetEdgeLength = s };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            PolyMeshMetrics pm = CheckResult(qr.Result, "strip");
            Assert.True(pm.QuadFraction >= 0.85, "quads " + pm.QuadFraction);
            DPolyMesh r = qr.Result;
            int onSide = 0, vertices = 0;
            for (int v = 0; v < r.VertexCount; ++v) {
                Vector3d p = r.GetVertex(v);
                vertices++;
                if (Math.Min(Math.Abs(p.y), Math.Abs(p.y - w)) < 1e-3 * s) onSide++;
            }
            Assert.True(onSide >= 0.4 * vertices, onSide + " of " + vertices + " vertices on the two long sides");
        }

        [Fact]
        public void Hierarchy_MergesBoundaryVerticesOnlyAlongTheBoundary()
        {
            DMesh3 strip = Strip(3);
            var cf = new CrossField(strip); cf.Compute();
            var pf = new PositionField(strip, cf) { TargetEdgeLength = 0.0625 };
            Assert.Equal(PositionFieldStatus.Success, pf.Compute());
            Assert.True(pf.Levels >= 3, "levels " + pf.Levels);
            int pairs = 0;
            for (int l = 0; l + 1 < pf.LevelList.Count; ++l) {
                PositionField.Level L = pf.LevelList[l];
                var members = new Dictionary<int, List<int>>();
                for (int i = 0; i < L.N; ++i) { List<int> list; if (!members.TryGetValue(L.ToCoarse[i], out list)) members[L.ToCoarse[i]] = list = new List<int>(); list.Add(i); }
                foreach (var kv in members) {
                    if (kv.Value.Count != 2) continue;
                    int a = kv.Value[0], b = kv.Value[1];
                    Assert.Equal(L.Kind[a], L.Kind[b]);                 // boundary with boundary, interior with interior
                    Assert.NotEqual(2, (int)L.Kind[a]);                 // corners stay alone
                    if (L.Kind[a] != 1) continue;
                    pairs++;
                    bool along = false;
                    for (int k = L.AS[a]; k < L.AS[a + 1]; ++k) if (L.AJ[k] == b && L.AB[k]) along = true;
                    Assert.True(along, "level " + l + ": boundary vertices " + a + " and " + b + " merged across the strip");
                }
            }
            Assert.True(pairs > 5, "boundary pairs " + pairs);
        }

        [Fact]
        public void MismatchLimit_ExcludesEdgesWhoseLatticesDisagree()
        {
            // no edge agrees to the last bit: nothing votes and nothing is extracted
            var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = S, MismatchLimit = 0 };
            Assert.Equal(QuadRemeshStatus.NoFaces, qr.Compute());
            Assert.Null(qr.Result);
            // a limit of a whole spacing admits the edges at the singularities too, and the result stays valid
            QuadRemesher wide = Run("sphere", S, q => q.MismatchLimit = 1);
            CheckResult(wide.Result, "wide limit");
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("cap")]
        [InlineData("patch")]
        public void WithoutCleanup_TheResultIsStillValid(string name)
        {
            // the dangling edges of the graph are pruned in any case
            QuadRemesher raw = Run(name, -1, q => q.Cleanup = false);
            CheckResult(raw.Result, name + " without cleanup");
            Assert.Equal(0, raw.CollapsedEdges);
            Assert.Equal(0, raw.RemovedValence2);
        }

        // ---------------------------------------------------------------- position field

        [Fact]
        public void PositionField_PointsAreOnTheLatticeNearTheVertices()
        {
            DMesh3 m = Shape("sphere");
            var pf = new PositionField(m, FieldOf("sphere")) { TargetEdgeLength = S };
            Assert.Equal(PositionFieldStatus.Success, pf.Compute());
            Assert.True(pf.Levels >= 3, "levels " + pf.Levels);
            Assert.True(pf.Colors >= 4 && pf.Colors <= 12, "colours " + pf.Colors);
            foreach (int v in m.VertexIndices()) {
                Assert.True(pf.HasPosition(v));
                Vector3d d = pf.Origin(v) - m.GetVertex(v), q = pf.Orientation(v), n = FieldOf("sphere").VertexNormal(v);
                Assert.True(Math.Abs(d.Dot(n)) < 1e-9 * S, "the point is not in the tangent plane");
                Assert.Equal(1, q.Length, 9);
                // the lattice point nearest to the vertex: both coordinates within half a spacing
                Assert.True(Math.Abs(d.Dot(q)) <= 0.5 * S * (1 + 1e-9) && Math.Abs(d.Dot(n.Cross(q))) <= 0.5 * S * (1 + 1e-9));
            }
        }

        // the share of edges whose two lattices agree near the middle point (the roundings differ by less than a quarter of the spacing)
        static double Agreement(PositionField pf)
        {
            PositionField.Level L = pf.L0;
            int ok = 0, all = 0;
            for (int i = 0; i < L.N; ++i)
                for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) {
                    if (L.AJ[k] < i) continue;
                    int dq, dt; double mm;
                    pf.SlotOffset(i, k, out dq, out dt, out mm);
                    all++;
                    if (mm < 0.25) ok++;
                }
            return (double)ok / all;
        }

        [Fact]
        public void PositionField_SmoothingMakesNeighbouringLatticesAgree()
        {
            DMesh3 m = Shape("sphere");
            var raw = new PositionField(m, FieldOf("sphere")) { TargetEdgeLength = S, Sweeps = 0 };
            raw.Compute();
            var smooth = new PositionField(m, FieldOf("sphere")) { TargetEdgeLength = S };
            smooth.Compute();
            double a0 = Agreement(raw), a1 = Agreement(smooth);
            Assert.True(a1 >= 0.95, "agreement " + a0 + " without smoothing, " + a1 + " with");
            Assert.True(a1 >= a0 + 0.1, "agreement " + a0 + " without smoothing, " + a1 + " with");
            Assert.True(smooth.SweepsRun > raw.SweepsRun);
        }

        [Fact]
        public void PositionField_ColouringSeparatesNeighbours()
        {
            var pf = new PositionField(Shape("torus"), FieldOf("torus")) { TargetEdgeLength = S };
            pf.Compute();
            PositionField.Level L = pf.L0;
            int[] color = new int[L.N];
            for (int c = 0; c + 1 < L.ColorStart.Length; ++c)
                for (int p = L.ColorStart[c]; p < L.ColorStart[c + 1]; ++p) color[L.ColorOrder[p]] = c;
            Assert.Equal(L.N, L.ColorOrder.Distinct().Count());
            for (int i = 0; i < L.N; ++i)
                for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) Assert.NotEqual(color[i], color[L.AJ[k]]);
        }

        [Fact]
        public void PositionField_StatusOfBadInput()
        {
            DMesh3 m = Shape("sphere");
            var cf = FieldOf("sphere");
            Assert.Equal(PositionFieldStatus.InvalidInput, new PositionField(m, null) { TargetEdgeLength = S }.Compute());
            Assert.Equal(PositionFieldStatus.InvalidInput, new PositionField(m, new CrossField(m)) { TargetEdgeLength = S }.Compute());     // not computed
            foreach (double bad in new[] { 0, -1, double.NaN, double.PositiveInfinity }) {
                var pf = new PositionField(m, cf) { TargetEdgeLength = bad };
                Assert.Equal(PositionFieldStatus.InvalidInput, pf.Compute());
                Assert.False(pf.Success);
                Assert.NotNull(pf.Error);
                Assert.False(pf.HasPosition(0));
            }
            Assert.Equal(PositionFieldStatus.InvalidInput, new PositionField(m, cf) { TargetEdgeLength = S, MiddleRegularization = double.NaN }.Compute());
        }

        [Fact]
        public void PositionField_VertexWithoutTriangleHasNoPosition()
        {
            DMesh3 m = new DMesh3(Shape("patch"));
            int lone = m.AppendVertex(new Vector3d(5, 5, 5));
            var cf = new CrossField(m); cf.Compute();
            var pf = new PositionField(m, cf) { TargetEdgeLength = 0.08 };
            Assert.Equal(PositionFieldStatus.Success, pf.Compute());
            Assert.False(pf.HasPosition(lone));
            Assert.True(pf.HasPosition(0));
            Assert.False(pf.HasPosition(-1));
            Assert.False(pf.HasPosition(lone + 10));
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = 0.08 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            CheckResult(qr.Result, "patch with a stray vertex");
        }

        // ---------------------------------------------------------------- status

        [Fact]
        public void Remesher_StatusOfBadInput()
        {
            Assert.Equal(QuadRemeshStatus.InvalidInput, new QuadRemesher(null) { TargetEdgeLength = S }.Compute());
            Assert.Equal(QuadRemeshStatus.InvalidInput, new QuadRemesher(new DMesh3()) { TargetEdgeLength = S }.Compute());
            foreach (double bad in new[] { 0, -S, double.NaN, double.PositiveInfinity }) {
                var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = bad };
                Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
                Assert.Null(qr.Result);
                Assert.NotNull(qr.Error);
                Assert.False(qr.Success);
            }
            // a failed cross field
            var failed = new CrossField(new DMesh3());
            failed.Compute();
            var q2 = new QuadRemesher(Shape("sphere")) { Field = failed, TargetEdgeLength = S };
            Assert.Equal(QuadRemeshStatus.FieldFailed, q2.Compute());
            Assert.Equal(failed.Status, q2.FieldStatus);
            // a bowtie vertex: the field is computed internally and reports it
            DMesh3 bow = new DMesh3();
            int c = bow.AppendVertex(new Vector3d(0, 0, 0));
            int[] ring = new int[4];
            for (int k = 0; k < 4; ++k) ring[k] = bow.AppendVertex(new Vector3d(Math.Cos(k * Math.PI / 2 + 0.3), Math.Sin(k * Math.PI / 2 + 0.3), 0));
            bow.AppendTriangle(c, ring[0], ring[1]);
            bow.AppendTriangle(c, ring[2], ring[3]);
            var q3 = new QuadRemesher(bow) { TargetEdgeLength = 0.5 };
            Assert.Equal(QuadRemeshStatus.FieldFailed, q3.Compute());
            Assert.Equal(CrossFieldStatus.NotManifold, q3.FieldStatus);
            // a target far above the size of the shape: nothing to extract, or a status, never an exception
            var q4 = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = 100 };
            QuadRemeshStatus st = q4.Compute();
            Assert.True(st == QuadRemeshStatus.NoFaces || q4.Result != null);
        }

        [Fact]
        public void TinyOrUnregularizedSettings_GiveAStatusNotAnException()
        {
            foreach (double tiny in new[] { 1e-310, 1e-320, double.Epsilon, 1e-12 }) {
                var qr = new QuadRemesher(Shape("patch")) { Field = FieldOf("patch"), TargetEdgeLength = tiny };
                QuadRemeshStatus st = qr.Compute();
                Assert.NotEqual(QuadRemeshStatus.Success, st);
            }
            foreach (double eps in new[] { 0, 1e-17, double.NaN }) {
                var pf = new PositionField(Shape("patch"), FieldOf("patch")) { TargetEdgeLength = 0.08, MiddleRegularization = eps };
                Assert.Equal(PositionFieldStatus.InvalidInput, pf.Compute());
            }
        }

        [Fact]
        public void Remesher_ComputesTheFieldWhenNoneIsGiven()
        {
            var qr = new QuadRemesher(Shape("box")) { TargetEdgeLength = S };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.True(qr.FieldSeconds > 0);
            CheckResult(qr.Result, "box without a given field");
            var given = Run("box");
            Assert.Equal(0, given.FieldSeconds);
            Assert.Equal(given.Result.FaceCount, qr.Result.FaceCount);
        }

        [Fact]
        public void FieldAndCornerOptions_ForwardToCrossFieldAndPositionField()
        {
            // CornerAngleDegrees forwarded to PositionField: on OpenPatch (four 90-degree corners), 30 pins all 4 corners while 100 pins none
            DMesh3 patch = Shape("patch");
            var qr30 = new QuadRemesher(patch) { Field = FieldOf("patch"), TargetEdgeLength = 0.08, CornerAngleDegrees = 30 };
            Assert.Equal(QuadRemeshStatus.Success, qr30.Compute());
            CheckResult(qr30.Result, "patch corner 30");
            Assert.Equal(30, qr30.Positions.CornerAngleDegrees);
            Assert.Equal(4, qr30.Positions.L0.Kind.Count(k => k == 2));

            var qr100 = new QuadRemesher(patch) { Field = FieldOf("patch"), TargetEdgeLength = 0.08, CornerAngleDegrees = 100 };
            Assert.Equal(QuadRemeshStatus.Success, qr100.Compute());
            CheckResult(qr100.Result, "patch corner 100");
            Assert.Equal(100, qr100.Positions.CornerAngleDegrees);
            Assert.Equal(0, qr100.Positions.L0.Kind.Count(k => k == 2));

            // CurvatureAlignment forwarded when Field is null: matches an explicit CrossField bit-for-bit and differs from default 0
            DMesh3 torus = Shape("torus");
            var cfCurv = new CrossField(torus) { CurvatureAlignment = 3 };
            Assert.Equal(CrossFieldStatus.Success, cfCurv.Compute());
            var qrExplicitCurv = new QuadRemesher(torus) { Field = cfCurv, TargetEdgeLength = S };
            Assert.Equal(QuadRemeshStatus.Success, qrExplicitCurv.Compute());

            var qrFwdCurv = new QuadRemesher(torus) { TargetEdgeLength = S, CurvatureAlignment = 3 };
            Assert.Equal(QuadRemeshStatus.Success, qrFwdCurv.Compute());
            CheckResult(qrFwdCurv.Result, "torus curvature aligned");
            AssertSame(qrExplicitCurv.Result, qrFwdCurv.Result);
            Assert.Null(qrFwdCurv.Field);   // internal field must not stick onto Field (reuse)

            var qrSmooth = Run("torus");
            Assert.NotEqual(qrSmooth.Result.VertexCount, qrFwdCurv.Result.VertexCount);

            // Explicit Field takes precedence over CurvatureAlignment
            var qrOverride = new QuadRemesher(torus) { Field = FieldOf("torus"), TargetEdgeLength = S, CurvatureAlignment = 3 };
            Assert.Equal(QuadRemeshStatus.Success, qrOverride.Compute());
            AssertSame(qrSmooth.Result, qrOverride.Result);

            // AlignCreases + CreaseAngleDegrees forwarded when Field is null on SharpBox (90-degree creases)
            DMesh3 sharp = CrossFieldShapes.SharpBox();
            var cfCrease = new CrossField(sharp) { AlignCreases = true, CreaseAngleDegrees = 30 };
            Assert.Equal(CrossFieldStatus.Success, cfCrease.Compute());
            var qrExplicitCrease = new QuadRemesher(sharp) { Field = cfCrease, TargetEdgeLength = 0.25 };
            Assert.Equal(QuadRemeshStatus.Success, qrExplicitCrease.Compute());

            var qrFwdCrease = new QuadRemesher(sharp) { TargetEdgeLength = 0.25, AlignCreases = true, CreaseAngleDegrees = 30 };
            Assert.Equal(QuadRemeshStatus.Success, qrFwdCrease.Compute());
            CheckResult(qrFwdCrease.Result, "sharp box align creases");
            AssertSame(qrExplicitCrease.Result, qrFwdCrease.Result);

            var qrNoCrease = new QuadRemesher(sharp) { TargetEdgeLength = 0.25, AlignCreases = false };
            Assert.Equal(QuadRemeshStatus.Success, qrNoCrease.Compute());
            Assert.NotEqual(qrNoCrease.Result.VertexCount, qrFwdCrease.Result.VertexCount);   // AlignCreases actually changes the result
            var qrHighThreshold = new QuadRemesher(sharp) { TargetEdgeLength = 0.25, AlignCreases = true, CreaseAngleDegrees = 100 };
            Assert.Equal(QuadRemeshStatus.Success, qrHighThreshold.Compute());
            AssertSame(qrNoCrease.Result, qrHighThreshold.Result);

            // Boundary angles 0 and 180 are valid (accepted, not rejected by strict inequalities)
            var qrZero = new QuadRemesher(patch) { Field = FieldOf("patch"), TargetEdgeLength = 0.08, CornerAngleDegrees = 0, CreaseAngleDegrees = 0 };
            Assert.Equal(QuadRemeshStatus.Success, qrZero.Compute());
            CheckResult(qrZero.Result, "patch angle 0");
            var qr180 = new QuadRemesher(patch) { Field = FieldOf("patch"), TargetEdgeLength = 0.08, CornerAngleDegrees = 180, CreaseAngleDegrees = 180 };
            Assert.Equal(QuadRemeshStatus.Success, qr180.Compute());
            CheckResult(qr180.Result, "patch angle 180");
        }

        [Fact]
        public void FieldAndCornerOptions_InvalidSettings_ReturnInvalidInputAndResetTimings()
        {
            // Run with CollapseLength large enough that CollapsedEdges / DroppedFaces are non-zero before testing InvalidInput reset
            DMesh3 m = Shape("sphere");
            var qr = new QuadRemesher(m) { TargetEdgeLength = S, CollapseLength = 0.9 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.True(qr.FieldSeconds > 0);
            Assert.True(qr.CollapsedEdges > 0 || qr.DroppedFaces > 0 || qr.RemovedValence2 > 0);

            foreach (double bad in new[] { -1, 181, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                qr.CornerAngleDegrees = bad;
                Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
                Assert.Null(qr.Result);
                Assert.NotNull(qr.Error);
                Assert.Equal(0, qr.FieldSeconds);
                Assert.Equal(0, qr.PositionSeconds);
                Assert.Equal(0, qr.ExtractSeconds);
                Assert.Equal(0, qr.CollapsedEdges);
                Assert.Equal(0, qr.RemovedValence2);
                Assert.Equal(0, qr.DroppedFaces);
                qr.CornerAngleDegrees = 30;

                qr.CreaseAngleDegrees = bad;
                Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
                Assert.Null(qr.Result);
                Assert.NotNull(qr.Error);
                qr.CreaseAngleDegrees = 30;
            }
            foreach (double bad in new[] { -0.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                qr.CurvatureAlignment = bad;
                Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
                Assert.Null(qr.Result);
                Assert.NotNull(qr.Error);
                qr.CurvatureAlignment = 0;
            }
        }

        [Fact]
        public void FieldAndCornerOptions_ReuseAndDeterminism()
        {
            DMesh3 sharp = CrossFieldShapes.SharpBox();
            var qr = new QuadRemesher(sharp) { TargetEdgeLength = 0.25, CurvatureAlignment = 0, AlignCreases = false };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            DPolyMesh plain = qr.Result;

            qr.AlignCreases = true; qr.CreaseAngleDegrees = 30; qr.CurvatureAlignment = 1;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            DPolyMesh aligned = qr.Result;
            CheckResult(aligned, "sharp box aligned reuse");

            // Fresh serial and parallel instances match bit-for-bit
            var serial = new QuadRemesher(sharp) { TargetEdgeLength = 0.25, AlignCreases = true, CreaseAngleDegrees = 30, CurvatureAlignment = 1, Parallel = false, ChunkSize = 8 };
            Assert.Equal(QuadRemeshStatus.Success, serial.Compute());
            AssertSame(serial.Result, aligned);

            // Resetting options on the same instance reproduces the original result bit-for-bit
            qr.AlignCreases = false; qr.CurvatureAlignment = 0;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            AssertSame(plain, qr.Result);
        }

        [Theory, MemberData(nameof(Frames))]
        public void FieldAndCornerOptions_AreInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = Shape("torus");
            var baseQr = new QuadRemesher(m) { TargetEdgeLength = S, CurvatureAlignment = 3 };
            Assert.Equal(QuadRemeshStatus.Success, baseQr.Compute());

            DMesh3 t = Invariance.Transformed(m, sim);
            var qr = new QuadRemesher(t) { TargetEdgeLength = sim.Scale * S, CurvatureAlignment = 3 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            if (sim.IsExact) {
                AssertSame(baseQr.Result, qr.Result, sim.Scale);
                // also verify AlignCreases + CreaseAngleDegrees and CornerAngleDegrees bit-exact under 2^k scales
                DMesh3 sharp = CrossFieldShapes.SharpBox();
                var baseSharp = new QuadRemesher(sharp) { TargetEdgeLength = 0.25, AlignCreases = true, CreaseAngleDegrees = 30 };
                Assert.Equal(QuadRemeshStatus.Success, baseSharp.Compute());
                DMesh3 tSharp = Invariance.Transformed(sharp, sim);
                var qrSharp = new QuadRemesher(tSharp) { TargetEdgeLength = sim.Scale * 0.25, AlignCreases = true, CreaseAngleDegrees = 30 };
                Assert.Equal(QuadRemeshStatus.Success, qrSharp.Compute());
                AssertSame(baseSharp.Result, qrSharp.Result, sim.Scale);

                DMesh3 patch = Shape("patch");
                var basePatch = new QuadRemesher(patch) { TargetEdgeLength = 0.08, CornerAngleDegrees = 100 };
                Assert.Equal(QuadRemeshStatus.Success, basePatch.Compute());
                DMesh3 tPatch = Invariance.Transformed(patch, sim);
                var qrPatch = new QuadRemesher(tPatch) { TargetEdgeLength = sim.Scale * 0.08, CornerAngleDegrees = 100 };
                Assert.Equal(QuadRemeshStatus.Success, qrPatch.Compute());
                AssertSame(basePatch.Result, qrPatch.Result, sim.Scale);
            } else {
                PolyMeshMetrics a = CheckResult(baseQr.Result, "base torus"), b = CheckResult(qr.Result, name + " torus");
                Assert.InRange(b.Faces, 0.9 * a.Faces, 1.1 * a.Faces);
                Assert.True(Math.Abs(b.QuadFraction - a.QuadFraction) <= 0.05, name + ": quads " + b.QuadFraction + " vs base " + a.QuadFraction);
                Assert.True(OneSidedHausdorff(qr.Result, t) <= 0.5 * sim.Scale * S, name + ": Hausdorff");
            }
        }

        // ---------------------------------------------------------------- reuse, determinism, invariance

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

        [Fact]
        public void Compute_CanBeCalledAgain()
        {
            var qr = new QuadRemesher(Shape("torus")) { Field = FieldOf("torus"), TargetEdgeLength = S };
            qr.Compute();
            DPolyMesh first = qr.Result;
            qr.Compute();
            Assert.NotSame(first, qr.Result);
            AssertSame(first, qr.Result);
            // other settings on the same instance give the result of a fresh one
            qr.TargetEdgeLength = 0.3;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            var fresh = Run("torus", 0.3);
            AssertSame(fresh.Result, qr.Result);
            Assert.True(qr.Result.FaceCount < first.FaceCount);
            // after a failure the old result is gone, after the next success it is back
            qr.TargetEdgeLength = -1;
            Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
            Assert.Null(qr.Result);
            qr.TargetEdgeLength = S;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            AssertSame(first, qr.Result);
            // the position field too
            var pf = new PositionField(Shape("torus"), FieldOf("torus")) { TargetEdgeLength = S };
            pf.Compute();
            Vector3d o0 = pf.Origin(5);
            pf.Compute();
            Assert.Equal(o0, pf.Origin(5));
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("cap")]
        [InlineData("patch")]
        [InlineData("torus")]       // #1674: the default path repairs the crossing edges of the graph (the torus had a hole): the repair is deterministic too
        public void Result_IsTheSameSerialAndParallelAndRepeated(string name)
        {
            // small chunks: every colour is split into many parallel work items
            DPolyMesh serial = Run(name, -1, q => { q.Parallel = false; q.ChunkSize = 8; }).Result;
            DPolyMesh parallel = Run(name, -1, q => { q.Parallel = true; q.ChunkSize = 8; }).Result;
            DPolyMesh parallel2 = Run(name, -1, q => { q.Parallel = true; q.ChunkSize = 3; }).Result;
            DPolyMesh dflt = Run(name).Result;
            AssertSame(serial, parallel);
            AssertSame(serial, parallel2);
            AssertSame(serial, dflt);
            for (int rep = 0; rep < 3; ++rep) AssertSame(serial, Run(name, -1, q => { q.ChunkSize = 8; }).Result);
        }

        [Fact]
        public void PositionField_IsTheSameSerialAndParallel()
        {
            var a = new PositionField(Shape("torus"), FieldOf("torus")) { TargetEdgeLength = S, Parallel = false };
            var b = new PositionField(Shape("torus"), FieldOf("torus")) { TargetEdgeLength = S, Parallel = true, ChunkSize = 16 };
            a.Compute(); b.Compute();
            Assert.Equal(a.SweepsRun, b.SweepsRun);
            foreach (int v in Shape("torus").VertexIndices()) {
                Vector3d p = a.Origin(v), q = b.Origin(v);
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.x), BitConverter.DoubleToInt64Bits(q.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.y), BitConverter.DoubleToInt64Bits(q.y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.z), BitConverter.DoubleToInt64Bits(q.z));
            }
        }

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory, MemberData(nameof(Frames))]
        public void Result_IsInvariantUnderSimilarities(string name) { CheckInvariantUnderSimilarity("box", name, 0.9); }

        // the torus: the closed result of the repair of the crossing edges (#1674): closed (Euler characteristic 0) under every frame too
        [Theory, MemberData(nameof(Frames))]
        public void TorusResult_IsInvariantUnderSimilarities(string name) { CheckInvariantUnderSimilarity("torus", name, 0.85); }

        static void CheckInvariantUnderSimilarity(string shape, string name, double minQuads)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = Shape(shape);
            DPolyMesh baseResult = Run(shape).Result;
            DMesh3 t = Invariance.Transformed(m, sim);
            var cf = new CrossField(t);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(t) { Field = cf, TargetEdgeLength = sim.Scale * S };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            if (shape == "torus") { PolyMeshMetrics bm = CheckResult(baseResult, "base"); Assert.Equal(0, bm.BoundaryVertices); Assert.Equal(0, bm.EulerCharacteristic); }
            if (sim.IsExact) {
                // a power of two scale: the same polygon mesh bit for bit, scaled
                AssertSame(baseResult, qr.Result, sim.Scale);
            } else {
                // rotations and translations change the rounding: the same kind of mesh, the same size
                PolyMeshMetrics a = CheckResult(baseResult, "base"), b = CheckResult(qr.Result, name);
                Assert.InRange(b.Faces, 0.9 * a.Faces, 1.1 * a.Faces);
                Assert.True(b.QuadFraction >= minQuads, name + ": quads " + b.QuadFraction);
                if (shape == "torus") { Assert.Equal(0, b.BoundaryVertices); Assert.Equal(0, b.EulerCharacteristic); }
                double h = OneSidedHausdorff(qr.Result, t);
                Assert.True(h <= 0.5 * sim.Scale * S, name + ": Hausdorff " + h);
            }
        }

        // ---------------------------------------------------------------- cleanup and face tracing on small graphs

        // an n x n grid of lattice points in the plane z = 0 (spacing 1), normals +z
        static QuadRemesher.PolyGraph Grid(int n, byte kind = 0)
        {
            var g = new QuadRemesher.PolyGraph();
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) g.Add(new Vector3d(i, j, 0), Vector3d.AxisZ, kind);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    if (i + 1 < n) g.Link(j * n + i, j * n + i + 1);
                    if (j + 1 < n) g.Link(j * n + i, (j + 1) * n + i);
                }
            return g;
        }

        [Fact]
        public void BuildFaces_TracesTheQuadsAndDropsTheOuterLoop()
        {
            var g = Grid(4);
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 6, out dropped);
            Assert.Equal(9, r.FaceCount);
            Assert.Equal(1, dropped);       // the loop around the grid, 12 sides and facing away
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.IsManifold);
            Assert.Equal(9, m.Quads);
            Assert.Equal(12, m.BoundaryVertices);
            Assert.Equal(1, m.EulerCharacteristic);
            for (int f = 0; f < r.FaceCount; ++f) {
                int[] fv = r.GetFace(f);
                Vector3d nw = (r.GetVertex(fv[1]) - r.GetVertex(fv[0])).Cross(r.GetVertex(fv[2]) - r.GetVertex(fv[0]));
                Assert.True(nw.z > 0, "a face turns away from the normals");
            }
            // flipped normals: the same faces, the other way round
            for (int k = 0; k < g.Nrm.Count; ++k) g.Nrm[k] = -Vector3d.AxisZ;
            DPolyMesh r2 = QuadRemesher.BuildFaces(g, 6, out dropped);
            Assert.Equal(9, r2.FaceCount);
            Assert.True(PolyMeshMetrics.Compute(r2).IsManifold);
            int[] f0 = r2.GetFace(0);
            Assert.True((r2.GetVertex(f0[1]) - r2.GetVertex(f0[0])).Cross(r2.GetVertex(f0[2]) - r2.GetVertex(f0[0])).z < 0);
        }

        [Fact]
        public void BuildFaces_DropsFacesAboveTheLimit()
        {
            // a 3 x 3 grid without its middle vertex has an 8-sided hole
            var g = Grid(3);
            foreach (int x in g.Adj[4].ToArray()) g.Unlink(4, x);
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 6, out dropped);
            Assert.Equal(0, r.FaceCount);          // the 8-gon is dropped; the rest has no face
            Assert.Equal(2, dropped);
            DPolyMesh r8 = QuadRemesher.BuildFaces(g, 8, out dropped);
            Assert.Equal(1, r8.FaceCount);
            Assert.Equal(8, r8.FaceVertexCount(0));
            Assert.Equal(1, dropped);
        }

        [Fact]
        public void Cleanup_CollapsesNearCoincidentPoints()
        {
            // two grid vertices pulled to 0.2 s apart: one point after the cleanup, the mesh is a quad grid again
            var g = Grid(4, 1);
            g.Kind[5] = 0; g.Kind[6] = 0;
            g.Pos[5] = new Vector3d(1, 1, 0); g.Pos[6] = new Vector3d(1.2, 1, 0);
            int collapsed, valence2;
            QuadRemesher.CleanupGraph(g, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(1, collapsed);
            Assert.Equal(0, valence2);
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 6, out dropped);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.IsManifold);
            Assert.Equal(2, m.Triangles);      // the two cells at the collapsed edge
            Assert.Equal(7, m.Quads);
            // the merged point is the middle of the two; the neighbours of both are its neighbours
            Assert.Equal(6, g.Adj[5].Count);
            Assert.Equal(1.1, g.Pos[5].x, 12);
            Assert.Empty(g.Adj[6]);
        }

        [Fact]
        public void Cleanup_KeepsTheCornerPositionAndLongEdges()
        {
            var g = Grid(4, 1);
            g.Kind[5] = 2; g.Pos[6] = new Vector3d(1.2, 1, 0);
            int collapsed, valence2;
            QuadRemesher.CleanupGraph(g, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(1, collapsed);
            Assert.Equal(new Vector3d(1, 1, 0), g.Pos[5]);      // the corner does not move
            // a boundary vertex keeps its place against an interior one
            var k = Grid(4, 1);
            k.Kind[6] = 0; k.Pos[6] = new Vector3d(1.2, 1, 0);
            QuadRemesher.CleanupGraph(k, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(new Vector3d(1, 1, 0), k.Pos[5]);
            Assert.Equal(1, k.Kind[5]);
            // and the other way round: the higher kind is the second vertex of the edge
            var k2 = Grid(4, 1);
            k2.Kind[5] = 0; k2.Pos[5] = new Vector3d(1.8, 1, 0);
            QuadRemesher.CleanupGraph(k2, 1, 0.4, out collapsed, out valence2);
            Assert.Empty(k2.Adj[5]);                       // the vertex of the higher kind is the one that stays
            Assert.Equal(6, k2.Adj[6].Count);
            Assert.Equal(new Vector3d(2, 1, 0), k2.Pos[6]);
            Assert.Equal(1, k2.Kind[6]);
            Assert.Equal(2, g.Kind[5]);
            // two corners are never merged; an edge of 0.5 s stays
            var h = Grid(4, 1);
            h.Kind[5] = 2; h.Kind[6] = 2; h.Pos[6] = new Vector3d(1.2, 1, 0); h.Pos[10] = new Vector3d(2, 1.7, 0);
            QuadRemesher.CleanupGraph(h, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(0, collapsed);
            Assert.True(h.Has(5, 6));
            Assert.True(h.Has(6, 10));      // 0.8 s apart
        }

        [Fact]
        public void Cleanup_RemovesValence2VerticesOffTheBoundary()
        {
            // the corner of a 3 x 3 grid has two edges: off the boundary (kind 0) it goes and its neighbours are joined
            var g = Grid(3, 1);
            g.Kind[0] = 0;
            int collapsed, valence2;
            QuadRemesher.CleanupGraph(g, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(1, valence2);
            Assert.Equal(0, collapsed);
            Assert.Empty(g.Adj[0]);
            Assert.True(g.Has(1, 3));
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 6, out dropped);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.IsManifold);
            Assert.Equal(1, m.Triangles);
            Assert.Equal(3, m.Quads);
            // on the boundary the same vertex is a corner of the mesh and stays
            var h = Grid(3, 1);
            QuadRemesher.CleanupGraph(h, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(0, valence2);
            Assert.Equal(2, h.Adj[0].Count);
        }

        [Fact]
        public void Cleanup_RemovesDanglingEdgesAndWhatTheyLeave()
        {
            // a tail on a boundary vertex: gone after the cleanup, and the vertex it hung on is as before
            var h = Grid(3, 1);
            int t1 = h.Add(new Vector3d(-1, 1, 0), Vector3d.AxisZ, 0), t2 = h.Add(new Vector3d(-2, 1, 0), Vector3d.AxisZ, 0);
            h.Link(3, t1); h.Link(t1, t2);
            int collapsed, valence2;
            QuadRemesher.CleanupGraph(h, 1, 0.4, out collapsed, out valence2);
            Assert.True(h.Adj[t1].Count == 0 && h.Adj[t2].Count == 0, "t1: " + string.Join(",", h.Adj[t1]) + " t2: " + string.Join(",", h.Adj[t2]) + " v3: " + string.Join(",", h.Adj[3]));
            Assert.Equal(3, h.Adj[3].Count);
            // a tail that ends once the vertex it hung on has lost it: that vertex has two edges again and goes too
            var c = Grid(3, 1);
            c.Kind[0] = 0;
            int ct = c.Add(new Vector3d(-1, -1, 0), Vector3d.AxisZ, 0);
            c.Link(0, ct);
            QuadRemesher.CleanupGraph(c, 1, 0.4, out collapsed, out valence2);
            Assert.Equal(1, valence2);
            Assert.Empty(c.Adj[0]);
            // the faces of a graph with a tail inside a face are the faces of the graph without it
            var g = Grid(3, 1);
            int a = g.Add(new Vector3d(0.5, 0.5, 0), Vector3d.AxisZ, 0), b = g.Add(new Vector3d(0.6, 0.6, 0), Vector3d.AxisZ, 0);
            g.Link(0, a); g.Link(a, b);
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 6, out dropped);
            Assert.Equal(4, r.FaceCount);
            Assert.True(PolyMeshMetrics.Compute(r).IsManifold);
            Assert.True(r.Validate().IsValid);
        }

        [Fact]
        public void PruneDangling_RemovesTails()
        {
            var g = Grid(3);
            int tail = g.Add(new Vector3d(-1, 0, 0), Vector3d.AxisZ, 0);
            int tail2 = g.Add(new Vector3d(-2, 0, 0), Vector3d.AxisZ, 0);
            g.Link(0, tail); g.Link(tail, tail2);
            Assert.True(QuadRemesher.PruneDangling(g));
            Assert.False(QuadRemesher.PruneDangling(g));
            Assert.Empty(g.Adj[tail]);
            Assert.Empty(g.Adj[tail2]);
            Assert.Equal(2, g.Adj[0].Count);
        }

        // ---------------------------------------------------------------- PolyMeshMetrics

        static DPolyMesh QuadGrid(int n)
        {
            var r = new DPolyMesh();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) r.AppendVertex(i, j, 0);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) r.AppendQuad(j * (n + 1) + i, j * (n + 1) + i + 1, (j + 1) * (n + 1) + i + 1, (j + 1) * (n + 1) + i);
            return r;
        }

        [Fact]
        public void Metrics_OfAQuadGrid()
        {
            PolyMeshMetrics m = PolyMeshMetrics.Compute(QuadGrid(4));
            Assert.Equal(16, m.Faces); Assert.Equal(16, m.Quads); Assert.Equal(1, m.QuadFraction);
            Assert.Equal(25, m.Vertices); Assert.Equal(40, m.Edges); Assert.Equal(1, m.EulerCharacteristic);
            Assert.Equal(16, m.BoundaryVertices);
            Assert.Equal(4, m.IrregularVertices);       // the four corners have two edges, the other boundary vertices three
            Assert.True(m.IsManifold);
            Assert.Equal(64, m.CornerDeviations.Length);
            Assert.Equal(0, PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), 9);
            Assert.Equal(1, PolyMeshMetrics.Percentile(m.Aspects, 0.5), 9);
            Assert.True(double.IsNaN(PolyMeshMetrics.Percentile(new double[0], 0.5)));
        }

        [Fact]
        public void Metrics_FaceMixAndSkewedCorners()
        {
            var r = new DPolyMesh();
            // a triangle, a pentagon and a hexagon fan around the origin, plus a rhombus (corners 60 and 120 degrees)
            r.AppendVertex(0, 0, 0);
            for (int k = 0; k < 12; ++k) r.AppendVertex(Math.Cos(k * 0.5), Math.Sin(k * 0.5), 0);
            r.AppendFace(new[] { 0, 1, 2 }); r.AppendFace(new[] { 0, 2, 3, 4, 5 }); r.AppendFace(new[] { 0, 5, 6, 7, 8, 9 });
            int a = r.AppendVertex(10, 0, 0), b = r.AppendVertex(11, 0, 0), c = r.AppendVertex(11.5, Math.Sqrt(3) / 2, 0), d = r.AppendVertex(10.5, Math.Sqrt(3) / 2, 0);
            r.AppendQuad(a, b, c, d);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.Equal(4, m.Faces); Assert.Equal(1, m.Pentagons); Assert.Equal(1, m.Hexagons); Assert.Equal(6, m.MaxSides);
            Assert.Equal(1, m.Quads); Assert.Equal(1, m.Triangles);
            Assert.Equal(0.25, m.QuadFraction, 12);
            Assert.Equal(4, m.CornerDeviations.Length);
            Assert.Equal(30, m.CornerDeviations[m.CornerDeviations.Length - 1], 9);     // the 60 and 120 degree corners
            Assert.Equal(1, PolyMeshMetrics.Percentile(m.Aspects, 0.95), 9);
        }

        [Fact]
        public void Metrics_FindNonManifoldInput()
        {
            // three faces on one edge
            var r = new DPolyMesh();
            r.AppendVertex(0, 0, 0); r.AppendVertex(1, 0, 0); r.AppendVertex(0, 1, 0); r.AppendVertex(0, -1, 0); r.AppendVertex(0, 0, 1);
            r.AppendTriangle(0, 1, 2); r.AppendTriangle(1, 0, 3); r.AppendTriangle(0, 1, 4);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.Equal(1, m.NonManifoldEdges);
            Assert.False(m.IsManifold);
            // inconsistent orientation: two triangles that use the shared edge in the same direction
            var o = new DPolyMesh();
            o.AppendVertex(0, 0, 0); o.AppendVertex(1, 0, 0); o.AppendVertex(0, 1, 0); o.AppendVertex(1, 1, 0);
            o.AppendTriangle(0, 1, 2); o.AppendTriangle(0, 1, 3);
            m = PolyMeshMetrics.Compute(o);
            Assert.Equal(1, m.InconsistentEdges);
            Assert.False(m.IsManifold);
            // a bowtie: two triangles that share only a vertex
            var b = new DPolyMesh();
            b.AppendVertex(0, 0, 0); b.AppendVertex(1, 0, 0); b.AppendVertex(0, 1, 0); b.AppendVertex(-1, 0, 0); b.AppendVertex(0, -1, 0);
            b.AppendTriangle(0, 1, 2); b.AppendTriangle(0, 3, 4);
            m = PolyMeshMetrics.Compute(b);
            Assert.Equal(1, m.NonManifoldVertices);
            Assert.False(m.IsManifold);
            // no faces, and faces that are skipped
            Assert.False(PolyMeshMetrics.Compute(new DPolyMesh()).IsManifold);
            var bad = new DPolyMesh();
            bad.AppendVertex(0, 0, 0); bad.AppendVertex(1, 0, 0);
            bad.AppendFace(new[] { 0, 1, 7 });
            Assert.Equal(0, PolyMeshMetrics.Compute(bad).Faces);
        }
    }
}
