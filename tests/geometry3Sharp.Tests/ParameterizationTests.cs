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
        /// closed by the zero-area triangle (A, B, C) = vertices (2, 3, 4). liftQ0 moves the corner (0,2) up.
        /// </summary>
        static DMesh3 TJunction(double liftQ0 = 0)
        {
            DMesh3 m = new DMesh3();
            int P0 = m.AppendVertex(new Vector3d(0, 0, 0)), P1 = m.AppendVertex(new Vector3d(2, 0, 0));
            int A = m.AppendVertex(new Vector3d(0, 1, 0)), B = m.AppendVertex(new Vector3d(2, 1, 0));
            int C = m.AppendVertex(new Vector3d(1, 1, 0));
            int Q0 = m.AppendVertex(new Vector3d(0, 2, liftQ0)), Q1 = m.AppendVertex(new Vector3d(2, 2, 0));
            foreach (Index3i t in new[] { new Index3i(P0, P1, B), new Index3i(P0, B, A), new Index3i(A, B, C),
                                          new Index3i(C, B, Q1), new Index3i(A, C, Q0), new Index3i(C, Q1, Q0) })
                Assert.True(m.AppendTriangle(t) >= 0);
            return m;
        }

        /// <summary>
        /// planar 2x2 square cut along y = 1 (A, B below; A2, B2 at the same positions above) and zipped by the
        /// zero-area triangles (A, B, B2) and (A, B2, A2), which have two coincident vertices each
        /// </summary>
        static DMesh3 ZippedSquare()
        {
            DMesh3 m = new DMesh3();
            int P0 = m.AppendVertex(new Vector3d(0, 0, 0)), P1 = m.AppendVertex(new Vector3d(2, 0, 0));
            int A = m.AppendVertex(new Vector3d(0, 1, 0)), B = m.AppendVertex(new Vector3d(2, 1, 0));
            int A2 = m.AppendVertex(new Vector3d(0, 1, 0)), B2 = m.AppendVertex(new Vector3d(2, 1, 0));
            int Q0 = m.AppendVertex(new Vector3d(0, 2, 0)), Q1 = m.AppendVertex(new Vector3d(2, 2, 0));
            foreach (Index3i t in new[] { new Index3i(P0, P1, B), new Index3i(P0, B, A), new Index3i(A, B, B2),
                                          new Index3i(A, B2, A2), new Index3i(A2, B2, Q1), new Index3i(A2, Q1, Q0) })
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

        /// <summary> MaxEdgeLengthError over the edges of nonzero length, and the UV length of the zero-length ones </summary>
        static double MaxEdgeLengthErrorZipped(DMesh3 mesh, Vector2d[] uv)
        {
            double err = 0;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                double l3 = mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)), luv = uv[ev.a].Distance(uv[ev.b]);
                err = Math.Max(err, l3 > 0 ? Math.Abs(luv / l3 - 1) : luv);
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
        public void SparseCholesky_RejectsIndefiniteAndExactlySingular()
        {
            SparseCholesky chol = new SparseCholesky(3);
            chol.Add(0, 0, 1); chol.Add(1, 1, 1); chol.Add(2, 2, 1); chol.Add(0, 2, 2);
            Assert.False(chol.Factorize());
            Assert.NotNull(chol.Error);
            // graph Laplacian without a fixed vertex, small integers: the last pivot is exactly 0
            SparseCholesky lap = new SparseCholesky(3);
            for (int i = 0; i < 3; ++i) {
                int j = (i + 1) % 3;
                lap.Add(i, i, 1); lap.Add(j, j, 1); lap.Add(i, j, -1);
            }
            Assert.False(lap.Factorize());
        }

        [Fact]
        public void SparseCholesky_IsValidDoesNotDetectSingularity()
        {
            // the documented limit of IsValid: the Laplacian of a ring (edge weights 1..3, one heavy edge) without a
            // fixed vertex is singular, but its last pivot is rounding noise and often positive. Which rings are
            // accepted depends on the ordering and the summation order, so several are tried: at least one of
            // them is accepted, and every accepted one gives a solution dominated by the null vector.
            Func<int, double, bool, SparseCholesky> ring = (n, heavy, pin) => {
                var c = new SparseCholesky(n);
                for (int i = 0; i < n; ++i) {
                    int j = (i + 1) % n;
                    double w = j == 0 ? heavy : 1 + i % 3;
                    c.Add(i, i, w); c.Add(j, j, w); c.Add(i, j, -w);
                }
                if (pin)
                    c.Add(0, 0, 1);
                c.Factorize();
                return c;
            };
            int accepted = 0;
            foreach (double heavy in new[] { 10.0, 100, 1000, 1e4 })
                for (int n = 20; n <= 72; n += 4) {
                    SparseCholesky singular = ring(n, heavy, false);
                    if (!singular.IsValid)
                        continue;
                    accepted++;
                    // L x = e_0 has no solution (the rows of L sum to zero, e_0 does not), yet Solve() returns
                    // finite values with a huge mean
                    double[] e0 = new double[n], y = new double[n];
                    e0[0] = 1;
                    singular.Solve(e0, y);
                    double sum = 0;
                    for (int i = 0; i < n; ++i) {
                        Assert.False(double.IsNaN(y[i]) || double.IsInfinity(y[i]));
                        sum += y[i];
                    }
                    Assert.True(Math.Abs(sum / n) > 1e6, "n " + n + " heavy " + heavy + " mean " + sum / n);
                }
            Assert.True(accepted > 0);

            // a mass term on vertex 0 makes the matrix definite: (L + e_0 e_0^T) x = b is solved accurately
            int m = 60;
            SparseCholesky pinned = ring(m, 1000, true);
            Assert.True(pinned.IsValid);
            double[] b = new double[m], x = new double[m];
            b[0] = 1; b[m / 2] = -1;
            pinned.Solve(b, x);
            for (int i = 0; i < m; ++i) {
                int p = (i + m - 1) % m, q = (i + 1) % m;
                double wp = p == m - 1 ? 1000 : 1 + p % 3, wq = q == 0 ? 1000 : 1 + i % 3;
                double r = (wp + wq) * x[i] - wp * x[p] - wq * x[q] + (i == 0 ? x[0] : 0) - b[i];
                Assert.True(Math.Abs(r) < 1e-9, "residual " + r + " at " + i);
            }
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
            // the planar mesh is its own conformal map. The zero-area triangle had no Dirichlet energy but was
            // part of the area term, so the minimizer opened it up (max conformal 1.8)
            DMesh3 mesh = TJunction();
            var r = new LSCMParameterization(mesh).Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6, "conformal " + r.Distortion.MaxConformal);
            Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6, "edge error " + MaxEdgeLengthError(mesh, r.UV));
            Assert.True(Math.Abs(r.Energy) < 1e-9, "energy " + r.Energy);
            Assert.Equal(0, r.Distortion.FlippedTriangles);     // (the degenerate triangle is not measured)

            // curved: the T-junction vertex must still stay on its edge (no crack or overlap in the UVs)
            foreach (double lift in new[] { 0.3, 0.6 }) {
                DMesh3 curved = TJunction(lift);
                r = new LSCMParameterization(curved).Compute();
                Assert.True(r.Succeeded, r.Error);
                Index3i t = curved.GetTriangle(2);
                double crack = Math.Abs((r.UV[t.b] - r.UV[t.a]).Cross(r.UV[t.c] - r.UV[t.a])) / r.UV[t.a].DistanceSquared(r.UV[t.b]);
                Assert.True(crack < 1e-5, "UV area of the zero-area triangle / |AB|^2: " + crack);
            }
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

        [Fact]
        public void InteriorBowtieVertex_IsInvalidMesh()
        {
            // two hexagon fans that only share their center vertex: the vertex has no boundary edges, but its
            // triangles form two fans. LSCM pinned the first fan and collapsed the second one to the shared
            // vertex (zero UV area), and reported success.
            DMesh3 mesh = new DMesh3();
            int c = mesh.AppendVertex(Vector3d.Zero);
            for (int d = 0; d < 2; ++d) {
                var rim = new List<int>();
                for (int k = 0; k < 6; ++k) {
                    double a = k * Math.PI / 3;
                    rim.Add(mesh.AppendVertex(d == 0 ? new Vector3d(Math.Cos(a), Math.Sin(a), 0) : new Vector3d(Math.Cos(a), 0.3, Math.Sin(a))));
                }
                for (int k = 0; k < 6; ++k)
                    Assert.True(mesh.AppendTriangle(c, rim[k], rim[(k + 1) % 6]) >= 0);
            }
            Assert.True(mesh.IsBowtieVertex(c));
            Assert.False(mesh.IsBoundaryVertex(c));
            var results = new[] {
                new LSCMParameterization(mesh).Compute(), new ARAPParameterization(mesh).Compute(),
                new SLIMParameterization(mesh) { InitialUV = new Vector2d[mesh.MaxVertexID] }.Compute(),
                new TutteEmbedding(mesh).Compute(), new BoundaryFirstFlattening(mesh).Compute() };
            foreach (var r in results) {
                Assert.Equal(ParameterizationStatus.InvalidMesh, r.Status);
                Assert.Contains("bowtie", r.Error);
                Assert.Null(r.UV);
            }
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
            // stops at the minimum (the tolerance is below what the line search can resolve), well before the limit
            Assert.True(r.Converged);
            Assert.True(r.Iterations < 300);
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
        public void BFF_TJunction_DegenerateTriangleKeepsGaussBonnet()
        {
            // the zero-area triangle has the angles 0, 0, pi. Without them the angle sums were pi short at the
            // T-junction vertex: curvature pi there, boundary angles summing to 3 pi, edge lengths off by 76%
            // (and flipped triangles in Disk mode) on a planar mesh. And without its (mollified) cotan weights the
            // harmonic extension did not keep the T-junction vertex on its edge (edge lengths off by 33%)
            DMesh3 mesh = TJunction();
            foreach (var ext in new[] { BoundaryFirstFlattening.ExtensionModes.Conformal, BoundaryFirstFlattening.ExtensionModes.Harmonic }) {
                var bff = new BoundaryFirstFlattening(mesh) { Extension = ext };
                var r = bff.Compute();
                Assert.True(r.Succeeded, r.Error);
                Assert.Equal(2 * Math.PI, bff.BoundaryAngles.Values.Sum(), 9);
                foreach (double u in bff.BoundaryScaleFactors.Values)
                    Assert.True(Math.Abs(u) < 1e-9, "scale factor " + u);
                Assert.Equal(0, r.Distortion.FlippedTriangles);
                Assert.True(r.Distortion.MaxConformal < 1 + 1e-6, ext + " conformal " + r.Distortion.MaxConformal);
                Assert.True(MaxEdgeLengthError(mesh, r.UV) < 1e-6, ext + " edge error " + MaxEdgeLengthError(mesh, r.UV));

            }
            // zero-area triangles with coincident vertices (the worst conditioned mollified weights)
            DMesh3 zipped = ZippedSquare();
            foreach (var ext in new[] { BoundaryFirstFlattening.ExtensionModes.Conformal, BoundaryFirstFlattening.ExtensionModes.Harmonic }) {
                var r = new BoundaryFirstFlattening(zipped) { Extension = ext }.Compute();
                Assert.True(r.Succeeded, r.Error);
                Assert.True(r.Distortion.MaxConformal < 1 + 1e-6, ext + " zipped conformal " + r.Distortion.MaxConformal);
                Assert.True(MaxEdgeLengthErrorZipped(zipped, r.UV) < 1e-6, ext + " zipped edge error " + MaxEdgeLengthErrorZipped(zipped, r.UV));
            }
            var lz = new LSCMParameterization(zipped).Compute();
            Assert.True(lz.Succeeded, lz.Error);
            Assert.True(MaxEdgeLengthErrorZipped(zipped, lz.UV) < 1e-6, "LSCM zipped edge error " + MaxEdgeLengthErrorZipped(zipped, lz.UV));

            var disk = new BoundaryFirstFlattening(mesh) { BoundaryMode = BoundaryFirstFlattening.BoundaryModes.Disk }.Compute();
            Assert.True(disk.Succeeded, disk.Error);
            Assert.Equal(0, disk.Distortion.FlippedTriangles);
        }

        [Fact]
        public void BFF_DegenerateTriangleAngles_SumToPi()
        {
            double[] a = new double[3];
            // flat: pi at the middle point
            BoundaryFirstFlattening.degenerateAngles(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0.5, 0, 0), a);
            Assert.Equal(Math.PI, a.Sum(), 12);
            Assert.Equal(Math.PI, a[2], 12);
            // two coincident vertices: pi/2 at each of them (the limit of mollified lengths), 0 at the third
            BoundaryFirstFlattening.degenerateAngles(new Vector3d(1, 1, 0), new Vector3d(1, 1, 0), new Vector3d(3, 1, 0), a);
            Assert.Equal(Math.PI / 2, a[0], 12);
            Assert.Equal(Math.PI / 2, a[1], 12);
            Assert.Equal(0, a[2], 12);
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
        public void UVPacker_PaddingThatCannotBeMet_IsReported()
        {
            // 100 equal squares: with a gap of 0.1 of the side, 10 squares per row need more than the whole side
            // for the gaps alone (11 * 0.1), so no packing has the padding
            DMesh3 mesh = new DMesh3();
            mesh.EnableVertexUVs(Vector2f.Zero);
            for (int k = 0; k < 100; ++k) {
                DMesh3 sq = Grid(2, 2, (s, t) => new Vector3d(s + 3 * k, t, 0));
                sq.EnableVertexUVs(Vector2f.Zero);
                foreach (int vid in sq.VertexIndices()) {
                    Vector3d p = sq.GetVertex(vid);
                    sq.SetVertexUV(vid, new Vector2f(p.x - 3 * k, p.y));
                }
                MeshEditor.Append(mesh, sq);
            }
            foreach (double padding in new[] { 0.1, 0.01 }) {
                var packer = new UVPacker(mesh) { Padding = padding };
                var r = packer.Compute();
                Assert.True(r.Succeeded, r.Error);
                bool feasible = padding < 0.05;
                Assert.Equal(feasible, r.Converged);
                if (feasible)
                    Assert.True(packer.AchievedPadding >= padding * (1 - 1e-9), "padding " + packer.AchievedPadding);
                else
                    Assert.True(packer.AchievedPadding < padding && packer.AchievedPadding > 0, "padding " + packer.AchievedPadding);
                // whatever the padding, the layout is consistent with AchievedPadding: inside the square, gaps kept
                double g = packer.AchievedPadding * (1 - 1e-9);
                foreach (int vid in mesh.VertexIndices()) {
                    Assert.InRange(r.UV[vid].x, g, 1 - g);
                    Assert.InRange(r.UV[vid].y, g, 1 - g);
                }
                for (int i = 0; i < packer.ChartCount; ++i)
                    for (int j = i + 1; j < packer.ChartCount; ++j) {
                        AxisAlignedBox2d a = packer.ChartBounds[i], b = packer.ChartBounds[j];
                        double gap = Math.Max(Math.Max(b.Min.x - a.Max.x, a.Min.x - b.Max.x), Math.Max(b.Min.y - a.Max.y, a.Min.y - b.Max.y));
                        Assert.True(gap >= g, "charts " + i + " and " + j + " gap " + gap);
                    }
            }
        }

        // a flat chart (UV = xy) like a CAD face with T-junctions: a triangle over the base (0,0)-(len,0), below it a
        // strip whose vertices lie on that base (not shared with the triangle), and a long thin triangle that
        // tilts the minimum-area rectangle; dir rotates everything (exact for dir = (1,0))
        static DMesh3 TJunctionChart(int n, double len, Vector2d dir, Vector2d far)
        {
            var mesh = new DMesh3();
            mesh.EnableVertexUVs(Vector2f.Zero);
            Func<double, double, int> vert = (x, y) => {
                Vector2d p = new Vector2d(x * dir.x - y * dir.y, x * dir.y + y * dir.x);
                return mesh.AppendVertex(new Vector3d(p.x, p.y, 0));
            };
            int a = vert(0, 0), b = vert(len, 0), c = vert(0.5 * len, 1);
            mesh.AppendTriangle(a, b, c);
            int[] top = new int[n + 1], bottom = new int[n];
            top[0] = a; top[n] = b;
            Vector2d pa = mesh.GetVertex(a).xy, pb = mesh.GetVertex(b).xy;
            for (int i = 1; i < n; ++i) {
                top[i] = vert(i * len / n, 0);
                // rotated, the vertex is only nearly on the base: keep it off the triangle above (exactly)
                Vector3d p = mesh.GetVertex(top[i]);
                for (double h = 1e-15; ExactPredicates.Orient2d(pa, pb, p.xy) > 0; h *= 2)
                    p = new Vector3d(p.x + h * dir.y, p.y - h * dir.x, 0);
                mesh.SetVertex(top[i], p);
            }
            for (int i = 0; i < n; ++i) bottom[i] = vert((i + 0.5) * len / n, -1);
            for (int i = 0; i < n; ++i) {
                mesh.AppendTriangle(top[i], bottom[i], top[i + 1]);
                if (i + 1 < n) mesh.AppendTriangle(bottom[i], bottom[i + 1], top[i + 1]);
            }
            mesh.AppendTriangle(b, vert(far.x, far.y), vert(far.x, far.y + 1));
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                mesh.SetVertexUV(v, new Vector2f((float)p.x, (float)p.y));
            }
            return mesh;
        }

        [Fact]
        public void UVPacker_PlacedChartsStayOverlapFree()
        {
            // the old packer rotated these charts to their (tilted) minimum-area rectangle; rounding moved vertices
            // of the T-junctions off the base line, into the triangle above: overlapping UV triangles in a chart
            // that is overlap-free in its input UVs. Without rotation, scaling and translating still round, which
            // moves vertices nearly on a slanted line (dir) to its other side.
            int checkedCharts = 0, unrotated = 0, repaired = 0;
            foreach (bool mirror in new[] { false, true })
            foreach (bool allowRotation in new[] { true, false })
                for (int k = 0; k < 6; ++k) {
                    double ang = allowRotation ? 0 : 0.3 + 0.17 * k;
                    Vector2d dir = new Vector2d(Math.Cos(ang), Math.Sin(ang));
                    DMesh3 mesh = TJunctionChart(24 + 7 * k, 10 + 3.1 * k, dir, new Vector2d(30 + 5 * k, 19 - 2 * k));
                    var uv = new Vector2d[mesh.MaxVertexID];
                    // mirrored: a valid chart with clockwise triangles, which must be checked as well
                    foreach (int v in mesh.VertexIndices()) { Vector3d p = mesh.GetVertex(v); uv[v] = new Vector2d(mirror ? -p.x : p.x, p.y); }
                    var ov0 = UVOverlap.Compute(mesh, uv);
                    Assert.True(ov0.OverlappingPairs == 0 && ov0.DegenerateTriangles == 0, "input " + k);
                    var packer = new UVPacker(mesh) { UV = uv, Padding = 0.01, AllowRotation = allowRotation };
                    var r = packer.Compute();
                    Assert.True(r.Succeeded, r.Error);
                    var ov = UVOverlap.Compute(mesh, r.UV);
                    Assert.True(ov.OverlappingPairs == 0 && ov.DegenerateTriangles == 0 && ov.InvertedTriangles == ov0.InvertedTriangles,
                        "mirror " + mirror + " rotation " + allowRotation + " case " + k + ": " + ov.OverlappingPairs + " pairs, "
                        + ov.InvertedTriangles + " inverted, " + ov.DegenerateTriangles + " degenerate");
                    Assert.Equal(0, packer.RemainingDefects);
                    // every triangle keeps its input orientation
                    foreach (int tid in mesh.TriangleIndices()) {
                        Index3i t = mesh.GetTriangle(tid);
                        Assert.Equal(ExactPredicates.Orient2d(uv[t.a], uv[t.b], uv[t.c]), ExactPredicates.Orient2d(r.UV[t.a], r.UV[t.b], r.UV[t.c]));
                    }
                    checkedCharts++;
                    unrotated += packer.UnrotatedCharts;
                    repaired += packer.RepairedVertices;
                }
            Assert.Equal(24, checkedCharts);
            // both fallbacks were needed somewhere
            Assert.True(unrotated > 0 && repaired > 0, "unrotated " + unrotated + ", repaired " + repaired);
        }

        [Fact]
        public void UVPacker_DefectsOfTheInputAreNotRepaired()
        {
            // a strip whose UVs fold back and forth (every triangle overlaps many others in the input), turned so
            // that it is rotated to its minimum-area rectangle: its overlaps are the input's, not the packer's;
            // nothing is moved or unrotated, the layout is that of the packer without the check
            List<Vector2d> uv;
            DMesh3 mesh = Accordion(400, 0.4, out uv);
            Assert.True(UVOverlap.Compute(mesh, uv).OverlappingPairs > 100000);
            var packer = new UVPacker(mesh) { UV = uv, Padding = 0.01 };
            var r = packer.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(0, packer.RepairedVertices);
            Assert.Equal(0, packer.UnrotatedCharts);
            Assert.Equal(0, packer.RemainingDefects);
            var plain = new UVPacker(mesh) { UV = uv, Padding = 0.01, ExactCheck = false }.Compute();
            foreach (int v in mesh.VertexIndices())
                Assert.Equal(plain.UV[v], r.UV[v]);

            // with T-junction charts (one mirrored) next to it: more than MaxCheckedPairs overlaps in the input
            // do not hide the defects of the other charts
            foreach (bool mirror in new[] { false, true }) {
                DMesh3 both = Accordion(400, 0.4, out uv);
                int firstTJ = both.MaxTriangleID;
                DMesh3 tj = TJunctionChart(24, 10, new Vector2d(1, 0), new Vector2d(30, 19));
                MeshEditor.Append(both, tj);
                foreach (int v in tj.VertexIndices()) { Vector3d p = tj.GetVertex(v); uv.Add(new Vector2d(mirror ? -p.x : p.x, p.y)); }
                var pk = new UVPacker(both) { UV = uv, Padding = 0.01 };
                var rb = pk.Compute();
                Assert.True(rb.Succeeded, rb.Error);
                var ov = UVOverlap.Compute(both, rb.UV, int.MaxValue);
                Assert.DoesNotContain(ov.Pairs, pr => pr.a >= firstTJ || pr.b >= firstTJ);
                Assert.DoesNotContain(ov.DegenerateTriangleIds, t => t >= firstTJ);
                foreach (int tid in both.TriangleIndices()) {
                    if (tid < firstTJ) continue;
                    Index3i t = both.GetTriangle(tid);
                    Assert.Equal(ExactPredicates.Orient2d(uv[t.a], uv[t.b], uv[t.c]), ExactPredicates.Orient2d(rb.UV[t.a], rb.UV[t.b], rb.UV[t.c]));
                }
                Assert.True(pk.UnrotatedCharts + pk.RepairedVertices > 0);
                Assert.Equal(0, pk.RemainingDefects);
            }
        }

        // a strip of 2n triangles whose UVs fold back and forth (x = 0, 1, 0, 1, ...), turned by angle
        static DMesh3 Accordion(int n, double angle, out List<Vector2d> uv)
        {
            DMesh3 mesh = new DMesh3();
            uv = new List<Vector2d>();
            double c = Math.Cos(angle), sn = Math.Sin(angle);
            int[] lo = new int[n + 1], hi = new int[n + 1];
            for (int i = 0; i <= n; ++i) {
                double x = (i % 2) * 1.0 + 0.001 * i;
                lo[i] = mesh.AppendVertex(new Vector3d(i, 0, 0)); uv.Add(new Vector2d(c * x, sn * x));
                hi[i] = mesh.AppendVertex(new Vector3d(i, 1, 0)); uv.Add(new Vector2d(c * x - sn, sn * x + c));
            }
            for (int i = 0; i < n; ++i) {
                mesh.AppendTriangle(lo[i], lo[i + 1], hi[i + 1]);
                mesh.AppendTriangle(lo[i], hi[i + 1], hi[i]);
            }
            return mesh;
        }

        [Fact]
        public void UVPacker_NeedleTrianglesKeepTheirOrientation()
        {
            // needles whose apex is 1e-20 (relative) above the base: translating them into the unit square rounds
            // the apex onto the base line, a degenerate UV triangle that is not degenerate in the input; the apex is
            // moved back up by a few ulps
            DMesh3 mesh = new DMesh3();
            var uv = new List<Vector2d>();
            for (int k = 0; k < 12; ++k) {
                double h = (1 + k) * 1e-20;
                int a = mesh.AppendVertex(new Vector3d(0, 3 * k, 0)), b = mesh.AppendVertex(new Vector3d(1, 3 * k, 0)),
                    c = mesh.AppendVertex(new Vector3d(0.37, 3 * k + h, 0));
                uv.Add(new Vector2d(2 * k, 0)); uv.Add(new Vector2d(2 * k + 1, 0)); uv.Add(new Vector2d(2 * k + 0.37, h));
                mesh.AppendTriangle(a, b, c);
            }
            Assert.True(UVOverlap.Compute(mesh, uv).IsInjective, "input");
            var packer = new UVPacker(mesh) { UV = uv, Padding = 0.01, AllowRotation = false, ScaleChartsToSurfaceArea = false };
            var r = packer.Compute();
            Assert.True(r.Succeeded, r.Error);
            var ov = UVOverlap.Compute(mesh, r.UV);
            Assert.True(ov.IsInjective, ov.DegenerateTriangles + " degenerate, " + ov.InvertedTriangles + " inverted");
            Assert.True(packer.RepairedVertices > 0, "repaired " + packer.RepairedVertices);
            foreach (int v in mesh.VertexIndices()) {
                Assert.InRange(r.UV[v].x, 0, 1);
                Assert.InRange(r.UV[v].y, 0, 1);
                // ChartBounds include the moved vertices
                AxisAlignedBox2d box = packer.ChartBounds[packer.TriangleChart[v / 3]];
                Assert.True(r.UV[v].x >= box.Min.x && r.UV[v].x <= box.Max.x && r.UV[v].y >= box.Min.y && r.UV[v].y <= box.Max.y);
            }
        }

        [Fact]
        public void UVPacker_InvalidInput()
        {
            DMesh3 mesh = FlatDisc(3, 8);
            Assert.Equal(ParameterizationStatus.InvalidMesh, new UVPacker(mesh).Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new UVPacker(mesh) { UV = new Vector2d[2] }.Compute().Status);
        }


        // ---------- review fixes ----------

        /// <summary>
        /// flat disc with an extra zero-area "ear" on a boundary edge: its apex is the midpoint of the edge and
        /// belongs to no other triangle, so it only appears in a triangle that is degenerate on the surface
        /// </summary>
        static DMesh3 DiscWithDegenerateEar(out int apex)
        {
            DMesh3 mesh = FlatDisc(6, 16);
            int eid = MeshIterators.BoundaryEdges(mesh).First();
            Index2i ev = mesh.GetOrientedBoundaryEdgeV(eid);
            apex = mesh.AppendVertex(0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b)));
            Assert.True(mesh.AppendTriangle(ev.b, ev.a, apex) >= 0);
            return mesh;
        }

        [Fact]
        public void VertexOnlyInDegenerateTriangles_AllMethodsSucceed()
        {
            int apex;
            DMesh3 mesh = DiscWithDegenerateEar(out apex);
            var results = new Dictionary<string, ParameterizationResult>() {
                { "LSCM", new LSCMParameterization(mesh).Compute() },
                { "BFF", new BoundaryFirstFlattening(mesh).Compute() },
                { "ARAP", new ARAPParameterization(mesh).Compute() },
                { "SLIM", new SLIMParameterization(mesh).Compute() },
                { "Tutte", new TutteEmbedding(mesh).Compute() },
            };
            foreach (var kv in results) {
                Assert.True(kv.Value.Succeeded, kv.Key + ": " + kv.Value.Error);
                Assert.True(MathUtil.IsFinite(kv.Value.UV[apex].x) && MathUtil.IsFinite(kv.Value.UV[apex].y), kv.Key);
            }
            // the planar part stays an isometry (up to scale for the conformal maps)
            Assert.True(results["ARAP"].Distortion.ARAPEnergy < 1e-6, "ARAP energy " + results["ARAP"].Distortion.ARAPEnergy);
            // the reported ARAP energy includes the stiff, mollified degenerate triangle; its term is negligible, so it
            // agrees with the distortion measure, which skips it
            Assert.Equal(results["ARAP"].Distortion.ARAPEnergy, results["ARAP"].Energy, 9);
            foreach (double lift in new[] { 0.3, 0.6 }) {
                DMesh3 curved = TJunction(lift);
                var ra = new ARAPParameterization(curved).Compute();
                Assert.True(ra.Succeeded, ra.Error);
                Assert.Equal(ra.Distortion.ARAPEnergy, ra.Energy, 9);
            }
                        // SLIM keeps every UV triangle positive, also the one that is degenerate on the surface
            AssertNoFlips(mesh, results["SLIM"]);
            Assert.True(results["SLIM"].Distortion.SymmetricDirichlet < 4 + 1e-3);
        }

        [Fact]
        public void InitialUV_TooShort_IsInvalidConstraints()
        {
            DMesh3 mesh = FlatDisc(4, 12);
            var shortUV = new Vector2d[mesh.MaxVertexID - 1];
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ARAPParameterization(mesh) { InitialUV = shortUV }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new SLIMParameterization(mesh) { InitialUV = shortUV }.Compute().Status);
            var nan = new Vector2d[mesh.MaxVertexID];
            nan[1] = new Vector2d(double.NaN, 0);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ARAPParameterization(mesh) { InitialUV = nan }.Compute().Status);
            // trailing vertex ids without triangles need no entry
            var good = new Vector2d[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices()) good[vid] = new Vector2d(mesh.GetVertex(vid).x, mesh.GetVertex(vid).y);
            mesh.AppendVertex(new Vector3d(5, 5, 0));
            var r = new ARAPParameterization(mesh) { InitialUV = good }.Compute();
            Assert.True(r.Succeeded, r.Error);
            // a deleted vertex id at the end (a gap: MaxVertexID stays larger than the list)
            DMesh3 gap = FlatDisc(4, 12);
            var gapUV = new Vector2d[gap.MaxVertexID];
            foreach (int vid in gap.VertexIndices()) gapUV[vid] = new Vector2d(gap.GetVertex(vid).x, gap.GetVertex(vid).y);
            int last = gap.AppendVertex(new Vector3d(5, 5, 0));
            Assert.Equal(MeshResult.Ok, gap.RemoveVertex(last, true, false));
            Assert.True(gap.MaxVertexID > gapUV.Length);
            Assert.True(new ARAPParameterization(gap) { InitialUV = gapUV }.Compute().Succeeded);
            Assert.True(new SLIMParameterization(gap) { InitialUV = gapUV }.Compute().Succeeded);
        }

        [Fact]
        public void BFF_TargetScaleFactors_AreValidated()
        {
            DMesh3 mesh = FlatDisc(4, 12);
            var none = new BoundaryFirstFlattening(mesh) { BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, none.Compute().Status);
            var unknown = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors,
                TargetScaleFactors = new Dictionary<int, double>() { { 9999, 0.5 } }
            };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, unknown.Compute().Status);
            var nan = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors,
                TargetScaleFactors = BoundaryLoop(mesh).ToDictionary(v => v, v => double.NaN)
            };
            Assert.Equal(ParameterizationStatus.InvalidConstraints, nan.Compute().Status);
            // a per-vertex field: the interior entries are ignored, the result equals the boundary-only one
            var field = mesh.VertexIndices().ToDictionary(v => v, v => 0.1 * mesh.GetVertex(v).x);
            var all = new BoundaryFirstFlattening(mesh) { BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors, TargetScaleFactors = field }.Compute();
            var boundaryOnly = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors,
                TargetScaleFactors = BoundaryLoop(mesh).ToDictionary(v => v, v => field[v])
            }.Compute();
            Assert.True(all.Succeeded, all.Error);
            foreach (int v in mesh.VertexIndices())
                Assert.True(all.UV[v].Distance(boundaryOnly.UV[v]) < 1e-12);
        }

        [Fact]
        public void BFF_Disk_DeepSphereCap_BoundaryIsCircle()
        {
            // strongly curved: the boundary scale factors change a lot, the target angles must match the final lengths
            DMesh3 mesh = SphereCap(0.8 * Math.PI, 16, 48, 0.5);
            MeshTransforms.Scale(mesh, 2, 1, 1);   // ellipsoid: the scale factors vary along the boundary
            var r = new BoundaryFirstFlattening(mesh) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.Disk,
                Extension = BoundaryFirstFlattening.ExtensionModes.Harmonic
            }.Compute();
            Assert.True(r.Succeeded, r.Error);
            AssertNoFlips(mesh, r);
            // the boundary vertices are not evenly spaced, so measure from the circumcenter of three of them
            List<int> loop = BoundaryLoop(mesh);
            int n = loop.Count;
            Vector2d a = r.UV[loop[0]], b = r.UV[loop[n / 3]], d = r.UV[loop[2 * n / 3]];
            double D = 2 * (a.x * (b.y - d.y) + b.x * (d.y - a.y) + d.x * (a.y - b.y));
            Vector2d c = new Vector2d(
                (a.LengthSquared * (b.y - d.y) + b.LengthSquared * (d.y - a.y) + d.LengthSquared * (a.y - b.y)) / D,
                (a.LengthSquared * (d.x - b.x) + b.LengthSquared * (a.x - d.x) + d.LengthSquared * (b.x - a.x)) / D);
            double rmin = loop.Min(v => r.UV[v].Distance(c)), rmax = loop.Max(v => r.UV[v].Distance(c));
            Assert.True(rmax / rmin < 1 + 1e-9, "radius ratio - 1 = " + (rmax / rmin - 1));
        }

        [Fact]
        public void SLIM_ConvergedOnlyAtTheMinimum()
        {
            // with tolerance 0 the iteration ends when no step lowers the energy any more: at the minimum
            // (Converged), not at the iteration limit
            DMesh3 mesh = Hemisphere(0.5);
            var r = new SLIMParameterization(mesh) { ConvergenceTolerance = 0, MaxIterations = 1000 }.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.True(r.Iterations < 1000, "iterations " + r.Iterations);
            Assert.True(r.Converged);
            // a single iteration is far from the minimum
            var one = new SLIMParameterization(mesh) { MaxIterations = 1 }.Compute();
            Assert.False(one.Converged);
            Assert.True(one.Energy > r.Energy);
        }

        // 8x8 grid with random heights, InitialUV the grid jittered by up to 0.4999 cells (as in the search that
        // found the early "converged" stop of the old criterion)
        static DMesh3 BumpyGrid(int seed, out List<Vector2d> uv)
        {
            var rnd = new Random(seed);
            int n = 8;
            var mesh = new DMesh3();
            int[,] id = new int[n + 1, n + 1];
            uv = new List<Vector2d>();
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j) {
                    bool inner = i > 0 && i < n && j > 0 && j < n;
                    id[i, j] = mesh.AppendVertex(new Vector3d(i, j, inner ? rnd.NextDouble() * 3 : 0));
                    double ju = inner ? 0.4999 : 0;
                    uv.Add(new Vector2d(i + ju * (rnd.NextDouble() - 0.5) * 2, j + ju * (rnd.NextDouble() - 0.5) * 2));
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    mesh.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]);
                    mesh.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
                }
            return mesh;
        }

        [Fact]
        public void SLIM_ConvergedMeansNoFurtherDecrease()
        {
            // the old criterion (one step decreased the energy by less than the tolerance) stopped this case after 7
            // iterations as converged, 1% above what a restart from its result reached. Converged now means the
            // proxy predicts no decrease, so a restart from a converged result does not move.
            List<Vector2d> uv;
            DMesh3 mesh = BumpyGrid(126, out uv);
            foreach (bool reuse in new[] { true, false }) {
                var r = new SLIMParameterization(mesh) { InitialUV = uv, ReuseFactorization = reuse }.Compute();
                Assert.True(r.Succeeded, r.Error);
                Assert.True(r.Converged);
                AssertNoFlips(mesh, r);
                var again = new SLIMParameterization(mesh) { InitialUV = r.UV }.Compute();
                Assert.True(again.Converged);
                Assert.True(again.Energy >= r.Energy * (1 - 1e-6), "restart lowers the energy from " + r.Energy + " to " + again.Energy);
            }
        }

        [Fact]
        public void SLIM_GradientMatchesEnergy_LineSearchNeedsStrictDecrease()
        {
            DMesh3 mesh = SphereCap(0.4 * Math.PI, 5, 16, 0.4);   // projects injectively onto the xy plane
            ParamMesh pm = new ParamMesh(mesh);
            int N = pm.N;
            double[] u = new double[N], v = new double[N];
            for (int i = 0; i < N; ++i) { u[i] = 1.3 * pm.Pos[i].x; v[i] = 0.8 * pm.Pos[i].y + 0.1 * pm.Pos[i].x; }
            double e0 = SLIMParameterization.Energy(pm, u, v);
            Assert.True(MathUtil.IsFinite(e0));
            // half the gradient of Sum A_T f = E * SurfaceArea, against central differences
            double[] g = new double[2 * N];
            SLIMParameterization.HalfGradient(pm, u, v, g);
            double h = 1e-6;
            for (int i = 0; i < N; i += 7)
                for (int c = 0; c < 2; ++c) {
                    double[] w = c == 0 ? u : v;
                    double keep = w[i];
                    w[i] = keep + h; double ep = SLIMParameterization.Energy(pm, u, v);
                    w[i] = keep - h; double em = SLIMParameterization.Energy(pm, u, v);
                    w[i] = keep;
                    double fd = (ep - em) / (2 * h) * pm.SurfaceArea;
                    Assert.True(Math.Abs(2 * g[2 * i + c] - fd) < 1e-5 * (1 + Math.Abs(fd)), "vertex " + i + " gradient " + 2 * g[2 * i + c] + " differences " + fd);
                }
            // the line search accepts only a strict decrease: along the gradient (ascent) it fails, against it it succeeds
            double[] du = new double[N], dv = new double[N], tu = new double[N], tv = new double[N];
            for (int sign = -1; sign <= 1; sign += 2) {
                for (int i = 0; i < N; ++i) { du[i] = sign * g[2 * i]; dv[i] = sign * g[2 * i + 1]; }
                double e1 = SLIMParameterization.LineSearch(pm, u, v, du, dv, e0, tu, tv);
                if (sign > 0)
                    Assert.False(e1 < e0);
                else {
                    Assert.True(e1 < e0);
                    Assert.Equal(e1, SLIMParameterization.Energy(pm, tu, tv));
                }
            }
            // the zero direction cannot decrease the energy either (the old line search accepted e1 == e0)
            Array.Clear(du, 0, N); Array.Clear(dv, 0, N);
            Assert.False(SLIMParameterization.LineSearch(pm, u, v, du, dv, e0, tu, tv) < e0);
        }

        [Fact]
        public void SLIM_ReusedFactorization_SameMinimum_FewerFactorizations()
        {
            DMesh3 mesh = SphereCap(0.8 * Math.PI, 14, 48, 0.5);
            var fresh = new SLIMParameterization(mesh) { ReuseFactorization = false, StartMap = SLIMParameterization.StartMaps.Tutte };
            var reuse = new SLIMParameterization(mesh) { StartMap = SLIMParameterization.StartMaps.Tutte };
            var rf = fresh.Compute();
            var rr = reuse.Compute();
            Assert.True(rf.Converged && rr.Converged);
            AssertNoFlips(mesh, rr);
            Assert.Equal(rf.Iterations, fresh.Factorizations);
            Assert.True(reuse.Factorizations < fresh.Factorizations, "factorizations " + reuse.Factorizations + " vs " + fresh.Factorizations);
            Assert.True(reuse.Factorizations < rr.Iterations);
            Assert.True(Math.Abs(rr.Energy - rf.Energy) < 1e-4 * rf.Energy, "energy " + rr.Energy + " vs " + rf.Energy);
        }

        [Fact]
        public void SLIM_StartsFromLSCMOnlyIfItIsInjective()
        {
            // a hemisphere: LSCM is injective and much closer to the minimum than Tutte
            DMesh3 hemi = SphereCap(Math.PI / 2, 20, 64, 0.5);
            var auto = new SLIMParameterization(hemi);
            var tutte = new SLIMParameterization(hemi) { StartMap = SLIMParameterization.StartMaps.Tutte };
            var ra = auto.Compute();
            var rt = tutte.Compute();
            Assert.True(auto.StartedFromLSCM);
            Assert.False(tutte.StartedFromLSCM);
            Assert.True(ra.Converged && rt.Converged);
            Assert.True(ra.Iterations < rt.Iterations, "iterations " + ra.Iterations + " vs " + rt.Iterations);
            Assert.True(Math.Abs(ra.Energy - rt.Energy) < 1e-4 * rt.Energy);

            // a ramp winding 1.5 times around an axis: LSCM unrolls it nearly isometrically into a sector of
            // 3 pi, which overlaps itself, so SLIM starts from the (injective) Tutte embedding
            DMesh3 ramp = Grid(60, 4, (s, t) => {
                double a = 3 * Math.PI * s, r = 1 + t;
                return new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0.1 * a);
            });
            Assert.False(UVOverlap.Compute(ramp, new LSCMParameterization(ramp).Compute().UV).IsInjective);
            var rampSlim = new SLIMParameterization(ramp);
            var rr = rampSlim.Compute();
            Assert.True(rr.Succeeded, rr.Error);
            Assert.False(rampSlim.StartedFromLSCM);
            AssertNoFlips(ramp, rr);
        }

        [Fact]
        public void SLIM_AutoStart_CurvedCapWithZeroAreaEar_MatchesTutte()
        {
            // a sphere cap with a triangle of zero surface area on a boundary edge (apex at 0.9 of the edge): LSCM
            // keeps the apex on the edge, a UV sliver that blocked every SLIM step when started from it (it stopped
            // after a few iterations, not converged, far above the minimum). Auto must reach Tutte's minimum.
            foreach (double cap in new[] { 0.4, 0.8 }) {
                DMesh3 mesh = SphereCap(cap * Math.PI, 8, 24, 0.3);
                int eid = MeshIterators.BoundaryEdges(mesh).First();
                Index2i ev = mesh.GetOrientedBoundaryEdgeV(eid);
                int apex = mesh.AppendVertex(Vector3d.Lerp(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), 0.9));
                Assert.True(mesh.AppendTriangle(ev.b, ev.a, apex) >= 0);
                var auto = new SLIMParameterization(mesh);
                var ra = auto.Compute();
                var rt = new SLIMParameterization(mesh) { StartMap = SLIMParameterization.StartMaps.Tutte }.Compute();
                Assert.True(ra.Succeeded && rt.Succeeded, ra.Error + rt.Error);
                Assert.True(ra.Converged, "cap " + cap + " not converged, energy " + ra.Energy);
                AssertNoFlips(mesh, ra);
                Assert.True(Math.Abs(ra.Energy - rt.Energy) < 1e-3 * rt.Energy, "cap " + cap + ": " + ra.Energy + " vs Tutte " + rt.Energy);
                // the sliver is seen before SLIM starts (not only by the restart after getting stuck): the same run as Tutte
                Assert.False(auto.StartedFromLSCM);
                Assert.Equal(rt.Iterations, ra.Iterations);
            }
        }

        [Fact]
        public void SLIM_StuckFromLSCM_GoesOnFromTutte()
        {
            // without the sliver check the LSCM start is taken and SLIM gets stuck at the sliver of the ear; it goes
            // on from Tutte within the same iteration budget and keeps the lower energy
            DMesh3 mesh = SphereCap(0.8 * Math.PI, 8, 24, 0.3);
            int eid = MeshIterators.BoundaryEdges(mesh).First();
            Index2i ev = mesh.GetOrientedBoundaryEdgeV(eid);
            int apex = mesh.AppendVertex(Vector3d.Lerp(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), 0.9));
            Assert.True(mesh.AppendTriangle(ev.b, ev.a, apex) >= 0);
            var rt = new SLIMParameterization(mesh) { StartMap = SLIMParameterization.StartMaps.Tutte }.Compute();
            var steps = new List<int>();
            var slim = new SLIMParameterization(mesh) { SliverTolerance = 0 };
            slim.IterationCompleted = (it, e) => steps.Add(it);
            var r = slim.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.False(slim.StartedFromLSCM);
            Assert.True(r.Converged);
            AssertNoFlips(mesh, r);
            Assert.True(Math.Abs(r.Energy - rt.Energy) < 1e-3 * rt.Energy, r.Energy + " vs Tutte " + rt.Energy);
            // the iterations from LSCM count, and the numbers go on across the restart
            Assert.True(r.Iterations > rt.Iterations);
            for (int k = 1; k < steps.Count; ++k)
                Assert.True(steps[k] > steps[k - 1]);
            // the budget is shared: with fewer iterations than the LSCM part plus the Tutte part it ends unconverged
            var shortRun = new SLIMParameterization(mesh) { SliverTolerance = 0, MaxIterations = rt.Iterations };
            var rs = shortRun.Compute();
            Assert.True(rs.Iterations <= rt.Iterations);
            Assert.False(rs.Converged);
            AssertNoFlips(mesh, rs);
        }

        [Fact]
        public void SLIM_IterationCompleted_ReportsEachDecrease()
        {
            DMesh3 mesh = Hemisphere(0.5);
            var energies = new List<double>();
            var iterations = new List<int>();
            var slim = new SLIMParameterization(mesh) { StartMap = SLIMParameterization.StartMaps.Tutte };
            slim.IterationCompleted = (it, e) => { iterations.Add(it); energies.Add(e); };
            var r = slim.Compute();
            Assert.True(r.Converged);
            Assert.NotEmpty(energies);
            for (int k = 1; k < energies.Count; ++k) {
                Assert.True(energies[k] < energies[k - 1]);
                Assert.True(iterations[k] > iterations[k - 1]);
            }
            Assert.Equal(r.Energy, energies[energies.Count - 1]);
            // one step per iteration, plus the final pass that finds convergence; passes that only refactorize
            // (here: after a kept factorization predicted no decrease) are not counted
            Assert.True(slim.Factorizations > 1 && slim.Factorizations < r.Iterations);
            Assert.Equal(energies.Count + 1, r.Iterations);
            Assert.Equal(r.Iterations - 1, iterations[iterations.Count - 1]);
        }

        [Fact]
        public void BFF_InscribedAngles_ClosePolygonOnCircle()
        {
            // regular, irregular, and with one long edge whose circle center lies outside the polygon
            foreach (double[] lt in new[] {
                new double[] { 1, 1, 1, 1, 1, 1 },
                new double[] { 0.3, 1.2, 0.7, 0.9, 0.2, 1.1, 0.5 },
                new double[] { 1, 1, 1.9 },
                new double[] { 0.2, 0.3, 0.25, 0.7 } }) {
                int m = lt.Length;
                double[] kt = new double[m];
                BoundaryFirstFlattening.inscribedAngles(lt, kt);
                Assert.Equal(2 * Math.PI, kt.Sum(), 9);
                // integrate the polygon: it closes, and all vertices lie on one circle
                var p = new List<Vector2d>() { Vector2d.Zero };
                double phi = 0;
                for (int k = 0; k < m; ++k) {
                    if (k > 0) phi += kt[k];
                    p.Add(p[k] + lt[k] * new Vector2d(Math.Cos(phi), Math.Sin(phi)));
                }
                Assert.True(p[m].Length < 1e-9, "polygon does not close: " + p[m].Length);
                Vector2d a = p[0], b = p[1], d = p[2];
                double D = 2 * (a.x * (b.y - d.y) + b.x * (d.y - a.y) + d.x * (a.y - b.y));
                Vector2d c = new Vector2d(
                    (a.LengthSquared * (b.y - d.y) + b.LengthSquared * (d.y - a.y) + d.LengthSquared * (a.y - b.y)) / D,
                    (a.LengthSquared * (d.x - b.x) + b.LengthSquared * (a.x - d.x) + d.LengthSquared * (b.x - a.x)) / D);
                double R = a.Distance(c);
                for (int k = 0; k < m; ++k)
                    Assert.Equal(R, p[k].Distance(c), 9);
            }
        }
    }
}
