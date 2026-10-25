using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Mesh parameterization (LSCM, ARAP, SLIM, Tutte, Boundary First Flattening), distortion measures,
    /// UV packing and the sparse Cholesky solver they use. Known cases: flat disc, cylinder cut open
    /// (developable, so isometric flattening is exact), hemisphere (positive curvature).
    /// </summary>
    public class ParameterizationTests
    {
        // ---------- test meshes ----------

        /// <summary> polar grid: center vertex + rings; pos(r in [0,1], angle) </summary>
        static DMesh3 Polar(int rings, int sectors, Func<double, double, Vector3d> pos, double jitter = 0, int seed = 1)
        {
            var rand = new Random(seed);
            DMesh3 mesh = new DMesh3();
            int center = mesh.AppendVertex(pos(0, 0));
            int[,] vid = new int[rings + 1, sectors];
            for (int i = 1; i <= rings; ++i)
                for (int j = 0; j < sectors; ++j) {
                    double r = (double)i / rings, a = 2 * Math.PI * j / sectors;
                    if (i < rings && jitter > 0) {
                        r += jitter * (rand.NextDouble() - 0.5) / rings;
                        a += jitter * (rand.NextDouble() - 0.5) * 2 * Math.PI / sectors;
                    }
                    vid[i, j] = mesh.AppendVertex(pos(r, a));
                }
            for (int j = 0; j < sectors; ++j)
                mesh.AppendTriangle(center, vid[1, j], vid[1, (j + 1) % sectors]);
            for (int i = 1; i < rings; ++i)
                for (int j = 0; j < sectors; ++j) {
                    int a = vid[i, j], b = vid[i, (j + 1) % sectors], c = vid[i + 1, j], d = vid[i + 1, (j + 1) % sectors];
                    if ((i + j) % 2 == 0) {
                        mesh.AppendTriangle(a, c, d); mesh.AppendTriangle(a, d, b);
                    } else {
                        mesh.AppendTriangle(a, c, b); mesh.AppendTriangle(b, c, d);
                    }
                }
            return mesh;
        }

        static DMesh3 FlatDisc(int rings = 10, int sectors = 32, double jitter = 0)
        {
            return Polar(rings, sectors, (r, a) => new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0), jitter);
        }

        /// <summary> spherical cap of the unit sphere up to polar angle maxPolar (pi/2: hemisphere) </summary>
        static DMesh3 SphereCap(double maxPolar, int rings = 12, int sectors = 40, double jitter = 0)
        {
            return Polar(rings, sectors, (r, a) => {
                double p = r * maxPolar;
                return new Vector3d(Math.Sin(p) * Math.Cos(a), Math.Sin(p) * Math.Sin(a), Math.Cos(p));
            }, jitter);
        }

        static DMesh3 Hemisphere(double jitter = 0) { return SphereCap(Math.PI / 2, 12, 40, jitter); }

        /// <summary> grid over (s,t) in [0,1]^2 </summary>
        static DMesh3 Grid(int ns, int nt, Func<double, double, Vector3d> pos)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[ns + 1, nt + 1];
            for (int j = 0; j <= nt; ++j)
                for (int i = 0; i <= ns; ++i)
                    vid[i, j] = mesh.AppendVertex(pos((double)i / ns, (double)j / nt));
            for (int j = 0; j < nt; ++j)
                for (int i = 0; i < ns; ++i) {
                    if ((i + j) % 2 == 0) {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    } else {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i, j + 1]);
                        mesh.AppendTriangle(vid[i + 1, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    }
                }
            return mesh;
        }

        /// <summary> cylinder of radius 1 and height 2, cut open along a ruling (seam vertices duplicated) </summary>
        static DMesh3 CylinderCutOpen(int nTheta = 48, int nZ = 12)
        {
            return Grid(nTheta, nZ, (s, t) => new Vector3d(Math.Cos(2 * Math.PI * s), Math.Sin(2 * Math.PI * s), 2 * t));
        }

        static DMesh3 FlatAnnulus()
        {
            DMesh3 mesh = new DMesh3();
            int nr = 6, na = 40;
            int[,] vid = new int[nr + 1, na];
            for (int i = 0; i <= nr; ++i)
                for (int j = 0; j < na; ++j) {
                    double r = 0.5 + 0.5 * i / nr, a = 2 * Math.PI * j / na;
                    vid[i, j] = mesh.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0));
                }
            for (int i = 0; i < nr; ++i)
                for (int j = 0; j < na; ++j) {
                    int a = vid[i, j], b = vid[i, (j + 1) % na], c = vid[i + 1, j], d = vid[i + 1, (j + 1) % na];
                    mesh.AppendTriangle(a, c, d); mesh.AppendTriangle(a, d, b);
                }
            return mesh;
        }

        static DMesh3 Scaled(DMesh3 mesh, double s)
        {
            MeshTransforms.Scale(mesh, s);
            return mesh;
        }

        /// <summary>
        /// planar 2x2 square with a T-junction: vertex C = (1,1) lies on the edge A-B of the lower half, which is
        /// closed by the zero-area triangle (A, B, C)
        /// </summary>
        static DMesh3 TJunction()
        {
            DMesh3 m = new DMesh3();
            int P0 = m.AppendVertex(new Vector3d(0, 0, 0)), P1 = m.AppendVertex(new Vector3d(2, 0, 0));
            int A = m.AppendVertex(new Vector3d(0, 1, 0)), B = m.AppendVertex(new Vector3d(2, 1, 0));
            int C = m.AppendVertex(new Vector3d(1, 1, 0));
            int Q0 = m.AppendVertex(new Vector3d(0, 2, 0)), Q1 = m.AppendVertex(new Vector3d(2, 2, 0));
            foreach (Index3i t in new[] { new Index3i(P0, P1, B), new Index3i(P0, B, A), new Index3i(A, B, C),
                                          new Index3i(C, B, Q1), new Index3i(A, C, Q0), new Index3i(C, Q1, Q0) })
                Assert.True(m.AppendTriangle(t) >= 0);
            return m;
        }

        static void AssertNoFlips(DMesh3 mesh, ParameterizationResult r)
        {
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                double a = (r.UV[t.b] - r.UV[t.a]).Cross(r.UV[t.c] - r.UV[t.a]);
                Assert.True(a > 0, "triangle " + tid + " is flipped or degenerate (2*area " + a + ")");
            }
            Assert.Equal(0, r.Distortion.FlippedTriangles);
        }

        /// <summary> max relative change of edge lengths after scaling the UVs by s </summary>
        static double MaxEdgeLengthError(DMesh3 mesh, Vector2d[] uv, double s = 1)
        {
            double err = 0;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                double l3 = mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                double luv = s * uv[ev.a].Distance(uv[ev.b]);
                err = Math.Max(err, Math.Abs(luv / l3 - 1));
            }
            return err;
        }

        static List<int> BoundaryLoop(DMesh3 mesh)
        {
            var loops = new MeshBoundaryLoops(mesh);
            return loops.Loops[0].Vertices.ToList();
        }


        // ---------- sparse Cholesky ----------

        [Fact]
        public void SparseCholesky_MatchesDenseSolve_AndReusesPattern()
        {
            DMesh3 mesh = FlatDisc(12, 30, 0.5);
            int n = mesh.MaxVertexID;
            var rand = new Random(3);
            for (int blockSize = 1; blockSize <= 2; ++blockSize) {
                int N = n * blockSize;
                SparseCholesky chol = new SparseCholesky(N, blockSize);
                // rounds 0 and 1 have the same pattern (analysis reused), round 2 adds long-range couplings
                for (int round = 0; round < 3; ++round) {
                    chol.Clear();
                    DenseMatrix A = new DenseMatrix(N, N);
                    Action<int, int, double> add = (r, c, v) => {
                        chol.Add(r, c, v);
                        A[r, c] += v;
                        if (r != c) A[c, r] += v;
                    };
                    foreach (int eid in mesh.EdgeIndices()) {
                        Index2i ev = mesh.GetEdgeV(eid);
                        for (int s = 0; s < blockSize; ++s) {
                            double w = 0.5 + rand.NextDouble();
                            int va = ev.a * blockSize + s, vb = ev.b * blockSize + s;
                            add(va, va, w); add(vb, vb, w); add(va, vb, -w);
                        }
                    }
                    for (int i = 0; i < N; ++i)
                        add(i, i, 0.01 + 0.1 * rand.NextDouble());
                    if (round == 2)
                        for (int i = 0; i + N / 2 < N; i += 7) {
                            add(i, i, 0.2); add(i + N / 2, i + N / 2, 0.2); add(i, i + N / 2, -0.2);
                        }
                    Assert.True(chol.Factorize(), chol.Error);
                    double[] b = new double[N], x = new double[N];
                    for (int i = 0; i < N; ++i) b[i] = rand.NextDouble() - 0.5;
                    chol.Solve(b, x);
                    double maxRes = 0;
                    for (int i = 0; i < N; ++i) {
                        double s = 0;
                        for (int j = 0; j < N; ++j) s += A[i, j] * x[j];
                        maxRes = Math.Max(maxRes, Math.Abs(s - b[i]));
                    }
                    Assert.True(maxRes < 1e-9, "residual " + maxRes);
                    Assert.True(chol.FactorNonZeros > N);
                }
            }
        }

        [Fact]
        public void SparseCholesky_RejectsIndefiniteAndSingular()
        {
            SparseCholesky chol = new SparseCholesky(3);
            chol.Add(0, 0, 1); chol.Add(1, 1, 1); chol.Add(2, 2, 1); chol.Add(0, 2, 2);
            Assert.False(chol.Factorize());
            Assert.NotNull(chol.Error);
            // graph Laplacian without a fixed vertex: singular
            SparseCholesky lap = new SparseCholesky(3);
            for (int i = 0; i < 3; ++i) {
                int j = (i + 1) % 3;
                lap.Add(i, i, 1); lap.Add(j, j, 1); lap.Add(i, j, -1);
            }
            Assert.False(lap.Factorize());
        }


        // ---------- distortion measures ----------

        [Fact]
        public void Distortion_KnownMaps()
        {
            DMesh3 mesh = FlatDisc(4, 12);
            Vector2d[] uv = new Vector2d[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                uv[vid] = new Vector2d(2 * p.x, 2 * p.y);
            }
            var d = ParameterizationDistortion.Compute(mesh, uv);
            Assert.Equal(mesh.TriangleCount, d.Triangles);
            Assert.Equal(0, d.FlippedTriangles);
            Assert.Equal(4, d.AreaScale, 9);
            Assert.Equal(1, d.MaxConformal, 9);
            Assert.True(d.MaxAreaDistortion < 1e-9);
            Assert.True(d.MaxAngleError < 1e-9);
            Assert.Equal(4 + 4 + 0.25 + 0.25, d.SymmetricDirichlet, 9);
            Assert.Equal(2, d.ARAPEnergy, 9);

            // anisotropic stretch: conformal distortion 3, angles change
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                uv[vid] = new Vector2d(3 * p.x, p.y);
            }
            d = ParameterizationDistortion.Compute(mesh, uv);
            Assert.Equal(3, d.MaxConformal, 9);
            Assert.Equal(3, d.MeanConformal, 9);
            Assert.True(d.MaxAngleError > 0.3);

            // mirror: every triangle flipped
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                uv[vid] = new Vector2d(p.x, -p.y);
            }
            d = ParameterizationDistortion.Compute(mesh, uv);
            Assert.Equal(mesh.TriangleCount, d.FlippedTriangles);
            Assert.True(double.IsPositiveInfinity(d.SymmetricDirichlet));
        }


        // ---------- LSCM ----------

        [Fact]
        public void LSCM_FlatDisc_IsSimilarity()
        {
            DMesh3 mesh = FlatDisc(8, 24, 0.6);
            var r = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6, "conformal " + r.Distortion.MaxConformal);
            Assert.Equal(1, r.Distortion.AreaScale, 6);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6);
            Assert.True(Math.Abs(r.Energy) < 1e-10);
            AssertNoFlips(mesh, r);
        }

        [Fact]
        public void LSCM_CylinderCutOpen_IsIsometricUpToScale()
        {
            DMesh3 mesh = CylinderCutOpen();
            var r = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6, "conformal " + r.Distortion.MaxConformal);
            Assert.True(r.Distortion.MaxAreaDistortion < 1e-6);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6);
            AssertNoFlips(mesh, r);
        }

        [Fact]
        public void LSCM_Hemisphere_NearlyConformal_AndKeepsPins()
        {
            DMesh3 mesh = Hemisphere(0.5);
            var r = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.MeanConformal < 1.08, "mean conformal " + r.Distortion.MeanConformal);
            Assert.True(r.Distortion.MaxConformal < 1.3, "max conformal " + r.Distortion.MaxConformal);
            // a hemisphere cannot be flattened without area distortion
            Assert.True(r.Distortion.MaxAreaDistortion > 0.3);
            AssertNoFlips(mesh, r);

            List<int> loop = BoundaryLoop(mesh);
            var pins = new Dictionary<int, Vector2d>() {
                { loop[0], new Vector2d(1, 2) }, { loop[loop.Count / 2], new Vector2d(4, 6) } };
            var lscm = new LSCMParameterization(mesh) { PinnedVertices = pins };
            r = lscm.Compute();
            Assert.True(r.Succeeded, r.Error);
            foreach (var kv in pins)
                Assert.True(r.UV[kv.Key].Distance(kv.Value) < 1e-12);
            Assert.True(r.Distortion.MeanConformal < 1.08);
        }

        [Fact]
        public void LSCM_ConformalDistortion_DecreasesWithRefinement()
        {
            double prev = double.PositiveInfinity;
            foreach (int rings in new[] { 8, 16, 32 }) {
                DMesh3 mesh = SphereCap(Math.PI / 2, rings, rings * 10 / 3);
                var r = new LSCMParameterization(mesh).Compute();
                Assert.True(r.Succeeded, r.Error);
                double excess = r.Distortion.MeanConformal - 1;
                Assert.True(excess < 0.6 * prev, rings + " rings: " + excess + " vs " + prev);
                prev = excess;
            }
            Assert.True(prev < 0.025, "excess " + prev);
        }

        [Fact]
        public void LSCM_Annulus_TwoBoundaries()
        {
            DMesh3 mesh = FlatAnnulus();
            var r = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6);
        }

        [Fact]
        public void LSCM_TJunction_DegenerateTriangleDoesNotDistort()
        {
            // the planar mesh is its own conformal map. The zero-area triangle has no Dirichlet energy, so its
            // area must not be part of the area term either: else the minimizer opened it up (max conformal 1.8)
            DMesh3 mesh = TJunction();
            var r = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6, "conformal " + r.Distortion.MaxConformal);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6, "edge error " + MaxEdgeLengthError(mesh, r.UV));
            Assert.True(Math.Abs(r.Energy) < 1e-10, "energy " + r.Energy);
            Assert.Equal(0, r.Distortion.FlippedTriangles);     // (the degenerate triangle is not measured)
        }

        [Fact]
        public void LSCM_InvalidInputs()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
            var r = new LSCMParameterization(sphere).Compute();
            Assert.Equal(ParameterizationStatus.InvalidTopology, r.Status);
            Assert.Null(r.UV);

            DMesh3 two = FlatDisc(3, 8);
            DMesh3 other = FlatDisc(3, 8);
            MeshTransforms.Translate(other, new Vector3d(5, 0, 0));
            MeshEditor.Append(two, other);
            Assert.Equal(ParameterizationStatus.InvalidTopology, new LSCMParameterization(two).Compute().Status);

            DMesh3 disc = FlatDisc(3, 8);
            var one = new LSCMParameterization(disc) { PinnedVertices = new Dictionary<int, Vector2d>() { { 1, Vector2d.Zero } } };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, one.Compute().Status);
            var same = new LSCMParameterization(disc) { PinnedVertices = new Dictionary<int, Vector2d>() { { 1, Vector2d.Zero }, { 2, Vector2d.Zero } } };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, same.Compute().Status);
            var bad = new LSCMParameterization(disc) { PinnedVertices = new Dictionary<int, Vector2d>() { { 1, Vector2d.Zero }, { 9999, Vector2d.One } } };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, bad.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidMesh, new LSCMParameterization(new DMesh3()).Compute().Status);
        }


        // ---------- ARAP ----------

        [Fact]
        public void ARAP_CylinderCutOpen_ExactUnfolding()
        {
            DMesh3 mesh = CylinderCutOpen();
            var r = new ARAPParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.ARAPEnergy < 1e-10, "ARAP energy " + r.Distortion.ARAPEnergy);
            Assert.Equal(1, r.Distortion.AreaScale, 6);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-5);
            AssertNoFlips(mesh, r);
            // the unfolded rectangle (in any rotation): width = perimeter of the 48-gon, height 2
            Box2d box = new ContMinBox2(mesh.VertexIndices().Select(v => r.UV[v]).ToList(), 1e-10, QueryNumberType.QT_DOUBLE, false).MinBox;
            double w = 48 * 2 * Math.Sin(Math.PI / 48);
            Assert.Equal(w, 2 * Math.Max(box.Extent.x, box.Extent.y), 6);
            Assert.Equal(2, 2 * Math.Min(box.Extent.x, box.Extent.y), 6);
        }

        [Fact]
        public void ARAP_Hemisphere_LessAreaDistortionThanLSCM()
        {
            DMesh3 mesh = Hemisphere(0.5);
            var lscm = new LSCMParameterization(mesh).Compute();
            var r = new ARAPParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Converged);
            Assert.True(r.Iterations > 1);
            AssertNoFlips(mesh, r);
            Assert.True(r.Distortion.ARAPEnergy < lscm.Distortion.ARAPEnergy,
                r.Distortion.ARAPEnergy + " vs LSCM " + lscm.Distortion.ARAPEnergy);
            Assert.True(r.Distortion.MeanAreaDistortion < lscm.Distortion.MeanAreaDistortion);
            // natural scale: close to area preserving overall
            Assert.InRange(r.Distortion.AreaScale, 0.8, 1.2);
            Assert.True(r.Distortion.MeanConformal < 1.3);
        }


        // ---------- Tutte and SLIM ----------

        [Fact]
        public void Tutte_IsFlipFree_WithCircularBoundary()
        {
            foreach (DMesh3 mesh in new[] { Hemisphere(0.8), SphereCap(0.9 * Math.PI, 14, 40, 0.8), FlatDisc(8, 20, 0.9) }) {
                var tutte = new TutteEmbedding(mesh) { ScaleToSurfaceArea = false };
                var r = tutte.Compute();
                Assert.True(r.Succeeded, r.Error);
                AssertNoFlips(mesh, r);
                foreach (int vid in BoundaryLoop(mesh))
                    Assert.Equal(1, r.UV[vid].Length, 9);
            }
            Assert.Equal(ParameterizationStatus.InvalidTopology, new TutteEmbedding(FlatAnnulus()).Compute().Status);
        }

        [Fact]
        public void SLIM_Hemisphere_FlipFree_LowEnergy()
        {
            DMesh3 mesh = Hemisphere(0.8);
            var tutte = new TutteEmbedding(mesh).Compute();
            var r = new SLIMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            Assert.True(r.Distortion.SymmetricDirichlet < tutte.Distortion.SymmetricDirichlet);
            Assert.True(r.Distortion.SymmetricDirichlet < 4.6, "symmetric Dirichlet " + r.Distortion.SymmetricDirichlet);
            Assert.Equal(r.Energy, r.Distortion.SymmetricDirichlet, 6);
            Assert.True(r.Distortion.MeanConformal < 1.3);
        }

        [Fact]
        public void SLIM_DeepSphereCap_FlipFree()
        {
            // 90% of a sphere: strongly curved, LSCM and ARAP are not guaranteed to be flip-free here
            DMesh3 mesh = SphereCap(0.9 * Math.PI, 14, 40, 0.8);
            var r = new SLIMParameterization(mesh) { MaxIterations = 200 }.Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            Assert.True(MathUtil.IsFinite(r.Energy));
        }

        [Fact]
        public void SLIM_CylinderCutOpen_ConvergesToIsometry()
        {
            DMesh3 mesh = CylinderCutOpen(32, 8);
            var r = new SLIMParameterization(mesh) { MaxIterations = 300, ConvergenceTolerance = 1e-9 }.Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            Assert.True(r.Distortion.SymmetricDirichlet < 4 + 1e-3, "symmetric Dirichlet " + r.Distortion.SymmetricDirichlet);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 0.02);
        }

        [Fact]
        public void SLIM_InitialUV_MustBeFlipFree()
        {
            DMesh3 mesh = FlatDisc(4, 12);
            Vector2d[] uv = new Vector2d[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                uv[vid] = new Vector2d(p.x, -p.y);
            }
            var r = new SLIMParameterization(mesh) { InitialUV = uv }.Compute();
            Assert.Equal(ParameterizationStatus.InvalidConstraints, r.Status);
            // from a valid (stretched) start it relaxes to the isometry
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                uv[vid] = new Vector2d(2 * p.x, p.y);
            }
            r = new SLIMParameterization(mesh) { InitialUV = uv, MaxIterations = 200, ConvergenceTolerance = 1e-10 }.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.SymmetricDirichlet < 4 + 1e-6);
        }


        // ---------- Boundary First Flattening ----------

        [Fact]
        public void BFF_FlatAndDevelopable_AreUnfoldedExactly()
        {
            foreach (DMesh3 mesh in new[] { FlatDisc(8, 24, 0.6), CylinderCutOpen() }) {
                foreach (var ext in new[] { BoundaryFirstFlattening.ExtensionModes.Conformal, BoundaryFirstFlattening.ExtensionModes.Harmonic }) {
                    var bff = new BoundaryFirstFlattening(mesh) { Extension = ext };
                    var r = bff.Compute();
                    Assert.True(r.Succeeded, r.Error);
                    Assert.True(r.Distortion.ARAPEnergy < 1e-10, ext + " ARAP energy " + r.Distortion.ARAPEnergy);
                    Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6);
                    AssertNoFlips(mesh, r);
                }
            }
        }

        [Fact]
        public void BFF_Hemisphere_MinimalAreaDistortion_KeepsBoundaryLengths()
        {
            DMesh3 mesh = Hemisphere(0.5);
            var bff = new BoundaryFirstFlattening(mesh);
            var r = bff.Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            Assert.True(r.Distortion.MeanConformal < 1.08, "mean conformal " + r.Distortion.MeanConformal);
            Assert.True(r.Distortion.MaxConformal < 1.3, "max conformal " + r.Distortion.MaxConformal);
            // boundary lengths kept: the hemisphere (area 2 pi) becomes a disc of radius 1 (area pi)
            Assert.Equal(0.5, r.Distortion.AreaScale, 2);
            foreach (double u in bff.BoundaryScaleFactors.Values)
                Assert.Equal(0, u);
            Assert.Equal(2 * Math.PI, bff.BoundaryAngles.Values.Sum(), 9);

            // harmonic extension reproduces the boundary curve, whose lengths are the surface lengths
            var harm = new BoundaryFirstFlattening(mesh) { Extension = BoundaryFirstFlattening.ExtensionModes.Harmonic }.Compute();
            List<int> loop = BoundaryLoop(mesh);
            for (int k = 0; k < loop.Count; ++k) {
                int a = loop[k], b = loop[(k + 1) % loop.Count];
                double l3 = mesh.GetVertex(a).Distance(mesh.GetVertex(b));
                Assert.Equal(1, harm.UV[a].Distance(harm.UV[b]) / l3, 2);
            }
            // conformal map with least area distortion: less than LSCM with its two arbitrary pins
            var lscm = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Distortion.MeanAreaDistortion <= lscm.Distortion.MeanAreaDistortion * 1.05);
        }

        [Fact]
        public void BFF_Hemisphere_ToDisk()
        {
            DMesh3 mesh = Hemisphere(0.5);
            var r = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.Disk,
                Extension = BoundaryFirstFlattening.ExtensionModes.Harmonic
            }.Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            List<int> loop = BoundaryLoop(mesh);
            Vector2d c = Vector2d.Zero;
            foreach (int v in loop) c += r.UV[v];
            c /= loop.Count;
            double rmin = loop.Min(v => r.UV[v].Distance(c)), rmax = loop.Max(v => r.UV[v].Distance(c));
            Assert.True(rmax / rmin < 1.01, "radius " + rmin + " .. " + rmax);
            Assert.True(r.Distortion.MeanConformal < 1.08);
        }

        [Fact]
        public void BFF_Hemisphere_IsStereographicProjection()
        {
            // the conformal map of the hemisphere with u = 0 on the boundary is the stereographic projection
            // from the opposite pole: a point at polar angle p goes to radius tan(p/2)
            DMesh3 mesh = SphereCap(Math.PI / 2, 24, 80);
            var r = new BoundaryFirstFlattening(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Vector2d center = r.UV[0];   // the pole
            double maxErr = 0;
            foreach (int vid in mesh.VertexIndices()) {
                double p = Math.Acos(MathUtil.Clamp(mesh.GetVertex(vid).z, -1, 1));
                maxErr = Math.Max(maxErr, Math.Abs(r.UV[vid].Distance(center) - Math.Tan(p / 2)));
            }
            Assert.True(maxErr < 5e-3, "max radius error " + maxErr);
        }

        [Fact]
        public void EachMethod_MinimizesItsOwnEnergy()
        {
            DMesh3 mesh = Hemisphere(0.5);
            var lscm = new LSCMParameterization(mesh).Compute();
            var bff = new BoundaryFirstFlattening(mesh).Compute();
            var arap = new ARAPParameterization(mesh).Compute();
            var slim = new SLIMParameterization(mesh).Compute();
            var all = new[] { lscm, bff, arap, slim };
            foreach (var r in all) {
                Assert.True(r.Succeeded, r.Error);
                AssertNoFlips(mesh, r);
            }
            // conformal methods: least angle distortion
            Assert.True(lscm.Distortion.MeanConformal < arap.Distortion.MeanConformal);
            Assert.True(bff.Distortion.MeanConformal < slim.Distortion.MeanConformal);
            // ARAP: least ARAP energy; SLIM: least symmetric Dirichlet energy
            foreach (var r in all) {
                Assert.True(arap.Distortion.ARAPEnergy <= r.Distortion.ARAPEnergy + 1e-12);
                Assert.True(slim.Distortion.SymmetricDirichlet <= r.Distortion.SymmetricDirichlet + 1e-12);
            }
        }

        [Fact]
        public void BFF_TargetAngles_Rectangle()
        {
            DMesh3 mesh = Hemisphere(0.5);
            List<int> loop = BoundaryLoop(mesh);
            int n = loop.Count;
            int[] corners = { loop[0], loop[n / 4], loop[n / 2], loop[3 * n / 4] };
            var angles = corners.ToDictionary(v => v, v => Math.PI / 2);
            var bff = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetAngles, TargetAngles = angles,
                Extension = BoundaryFirstFlattening.ExtensionModes.Harmonic
            };
            var r = bff.Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            // the boundary turns only at the corners: consecutive edges between corners are parallel, corners are right angles
            int start = loop.IndexOf(corners[0]);
            var ordered = Enumerable.Range(0, n).Select(k => loop[(start + k) % n]).ToList();
            for (int k = 0; k < n; ++k) {
                Vector2d e0 = (r.UV[ordered[k]] - r.UV[ordered[(k + n - 1) % n]]).Normalized;
                Vector2d e1 = (r.UV[ordered[(k + 1) % n]] - r.UV[ordered[k]]).Normalized;
                double turn = Math.Atan2(e0.Cross(e1), e0.Dot(e1));
                double expect = corners.Contains(ordered[k]) ? Math.PI / 2 : 0;
                Assert.True(Math.Abs(turn - expect) < 1e-6, "turn " + turn + " at boundary vertex " + k);
            }

            // invalid targets
            var wrong = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetAngles,
                TargetAngles = corners.Take(3).ToDictionary(v => v, v => Math.PI / 2)
            };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, wrong.Compute().Status);
            var interior = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetAngles,
                TargetAngles = new Dictionary<int, double>() { { 0, 2 * Math.PI } }
            };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, interior.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidTopology, new BoundaryFirstFlattening(FlatAnnulus()).Compute().Status);
        }

        [Fact]
        public void BFF_TargetScaleFactors_ScaleTheBoundary()
        {
            DMesh3 mesh = FlatDisc(8, 24);
            List<int> loop = BoundaryLoop(mesh);
            var r = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors,
                TargetScaleFactors = loop.ToDictionary(v => v, v => Math.Log(2)),
                Extension = BoundaryFirstFlattening.ExtensionModes.Harmonic
            }.Compute();
            Assert.True(r.Succeeded, r.Error);
            // constant u = log 2 on a flat disc: uniform scaling by 2
            Assert.Equal(4, r.Distortion.AreaScale, 6);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6);
        }


        // ---------- result and packing ----------

        [Fact]
        public void ApplyToMesh_WritesVertexUVs()
        {
            DMesh3 mesh = Hemisphere();
            var r = new BoundaryFirstFlattening(mesh).Compute();
            r.ApplyToMesh(mesh);
            Assert.True(mesh.HasVertexUVs);
            foreach (int vid in mesh.VertexIndices())
                Assert.True(((Vector2d)mesh.GetVertexUV(vid)).Distance(r.UV[vid]) < 1e-5);
            var d = ParameterizationDistortion.Compute(mesh);
            Assert.Equal(r.Distortion.MeanConformal, d.MeanConformal, 4);
        }

        static DMesh3 ChartsMesh(out double[] areas)
        {
            // several parameterized components of different sizes and shapes
            var parts = new List<DMesh3>() {
                FlatDisc(4, 12), Hemisphere(), CylinderCutOpen(24, 6),
                Scaled(FlatDisc(3, 10), 0.3), Grid(20, 2, (s, t) => new Vector3d(4 * s, 0.4 * t, 0)),
                Scaled(Hemisphere(), 0.5)
            };
            DMesh3 all = new DMesh3();
            all.EnableVertexUVs(Vector2f.Zero);
            areas = new double[parts.Count];
            double offset = 0;
            for (int i = 0; i < parts.Count; ++i) {
                DMesh3 p = parts[i];
                var r = new LSCMParameterization(p).Compute();
                Assert.True(r.Succeeded, r.Error);
                r.ApplyToMesh(p);
                areas[i] = MeshMeasurements.AreaT(p, p.TriangleIndices());
                MeshTransforms.Translate(p, new Vector3d(offset, 0, 0));
                offset += 10;
                MeshEditor.Append(all, p);
            }
            return all;
        }

        [Fact]
        public void UVPacker_NoOverlap_InsideUnitSquare_KeepsRelativeScale()
        {
            double[] areas;
            DMesh3 mesh = ChartsMesh(out areas);
            foreach (bool rot in new[] { true, false }) {
                var packer = new UVPacker(mesh) { Padding = 0.01, AllowRotation = rot };
                var r = packer.Compute();
                Assert.True(r.Succeeded, r.Error);
                Assert.Equal(areas.Length, packer.ChartCount);
                foreach (int vid in mesh.VertexIndices()) {
                    Assert.InRange(r.UV[vid].x, 0.01 - 1e-9, 1 - 0.01 + 1e-9);
                    Assert.InRange(r.UV[vid].y, 0.01 - 1e-9, 1 - 0.01 + 1e-9);
                }
                for (int i = 0; i < packer.ChartCount; ++i)
                    for (int j = i + 1; j < packer.ChartCount; ++j) {
                        AxisAlignedBox2d a = packer.ChartBounds[i], b = packer.ChartBounds[j];
                        double gap = Math.Max(Math.Max(b.Min.x - a.Max.x, a.Min.x - b.Max.x), Math.Max(b.Min.y - a.Max.y, a.Min.y - b.Max.y));
                        Assert.True(gap >= 0.01 - 1e-9, "charts " + i + " and " + j + " gap " + gap);
                    }
                // charts are scaled to their surface area: uniform texel density
                double[] uvArea = new double[packer.ChartCount];
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    uvArea[packer.TriangleChart[tid]] += 0.5 * (r.UV[t.b] - r.UV[t.a]).Cross(r.UV[t.c] - r.UV[t.a]);
                }
                double density = uvArea[0] / areas[0];
                for (int i = 1; i < areas.Length; ++i)
                    Assert.Equal(1, uvArea[i] / areas[i] / density, 6);
                Assert.True(packer.Coverage > 0.35, "coverage " + packer.Coverage);
                Assert.Equal(0, r.Distortion.FlippedTriangles);
                // packing is a similarity per chart: conformal distortion unchanged
                Assert.True(r.Distortion.MeanConformal < 1.05);
            }
        }

        [Fact]
        public void UVPacker_ManyEqualSquares_PackTightly()
        {
            DMesh3 mesh = new DMesh3();
            mesh.EnableVertexUVs(Vector2f.Zero);
            for (int k = 0; k < 16; ++k) {
                DMesh3 sq = Grid(2, 2, (s, t) => new Vector3d(s + 3 * k, t, 0));
                sq.EnableVertexUVs(Vector2f.Zero);
                foreach (int vid in sq.VertexIndices()) {
                    Vector3d p = sq.GetVertex(vid);
                    sq.SetVertexUV(vid, new Vector2f(-p.y, p.x - 3 * k));   // chart-local, rotated by 90 degrees
                }
                MeshEditor.Append(mesh, sq);
            }
            var packer = new UVPacker(mesh) { Padding = 0.002 };
            var r = packer.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(16, packer.ChartCount);
            Assert.True(packer.Coverage > 0.9, "coverage " + packer.Coverage);
            Assert.Equal(0, r.Distortion.FlippedTriangles);
        }

        [Fact]
        public void UVPacker_InvalidInput()
        {
            DMesh3 mesh = FlatDisc(3, 8);
            Assert.Equal(ParameterizationStatus.InvalidMesh, new UVPacker(mesh).Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new UVPacker(mesh) { UV = new Vector2d[2] }.Compute().Status);
        }
    }
}
