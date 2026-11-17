using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SpectralConformalParameterization (Mullen et al. 2008): the pin-free opt-in next to LSCM for disc charts
    /// (docs/research/GEOGRAM_MEASUREMENTS_2026-11.md, item 1).
    /// </summary>
    public class SpectralConformalParameterizationTests
    {
        // a grid of nx x ny cells of size h, heights f(x, y); the diagonals are mirrored about x = nx h / 2, so the
        // triangulation is mirror-symmetric too (as in ParameterizationIssueCasesTests.Grid)
        static DMesh3 Grid(int nx, int ny, double h, Func<double, double, double> f, out int[,] ids)
        {
            var m = new DMesh3();
            ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    ids[i, j] = m.AppendVertex(new Vector3d(i * h, j * h, f(i * h, j * h)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                    bool left = 2 * i + 1 < nx;
                    if (left == ((j % 2) == 0)) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                }
            return m;
        }

        // the curved plate of finding P3 without its holes (a disc), mirror-symmetric about x = 1.5
        static DMesh3 Plate(out List<(int, int)> pairs, int nx = 18, int ny = 12)
        {
            DMesh3 m = Grid(nx, ny, 3.0 / nx, (x, y) => 0.3 * Math.Sin(y) + 0.1 * (x - 1.5) * (x - 1.5), out int[,] ids);
            pairs = new List<(int, int)>();
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    pairs.Add((ids[i, j], ids[nx - i, j]));
            return m;
        }

        // a spherical cap of opening angle maxAngle (radians from the pole), on a grid of n x n cells mapped radially
        static DMesh3 Cap(int n, double maxAngle)
        {
            return MapGrid(n, n, (x, y) => {
                double dx = x - 0.5, dy = y - 0.5, t = maxAngle * Math.Sqrt((dx * dx + dy * dy) / 0.5), p = Math.Atan2(dy, dx);
                return new Vector3d(Math.Sin(t) * Math.Cos(p), Math.Sin(t) * Math.Sin(p), Math.Cos(t));
            });
        }

        static DMesh3 MapGrid(int nx, int ny, Func<double, double, Vector3d> f)
        {
            var m = new DMesh3();
            int[,] id = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    id[i, j] = m.AppendVertex(f((double)i / nx, (double)j / ny));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = id[i, j], b = id[i + 1, j], c = id[i, j + 1], d = id[i + 1, j + 1];
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static DMesh3 OpenCylinder(int sides, int rings, double dz)
        {
            var m = new DMesh3();
            for (int j = 0; j <= rings; ++j)
                for (int i = 0; i < sides; ++i) {
                    double a = 2 * Math.PI * i / sides;
                    m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), dz * j));
                }
            for (int j = 0; j < rings; ++j)
                for (int i = 0; i < sides; ++i) {
                    int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            return m;
        }

        // the largest difference of UV distances of mirrored vertex pairs, over the UV diagonal (all pairs of pairs)
        static double Asymmetry(DMesh3 m, Vector2d[] uv, List<(int, int)> pairs)
        {
            var box = new AxisAlignedBox2d(uv[pairs[0].Item1]);
            foreach (int v in m.VertexIndices()) box.Contain(uv[v]);
            double worst = 0;
            for (int p = 0; p < pairs.Count; ++p)
                for (int q = p + 1; q < pairs.Count; ++q) {
                    var (a, ma) = pairs[p];
                    var (b, mb) = pairs[q];
                    worst = Math.Max(worst, Math.Abs(uv[a].Distance(uv[b]) - uv[ma].Distance(uv[mb])));
                }
            return worst / box.DiagonalLength;
        }

        // a valid map: Success, every vertex finite, no flipped, zero-area or degenerate UV triangle, injective (exact test)
        static void AssertValid(DMesh3 m, ParameterizationResult r, string what)
        {
            Assert.True(r.Status == ParameterizationStatus.Success, $"{what}: {r.Status} {r.Error}");
            foreach (int v in m.VertexIndices())
                Assert.True(MathUtil.IsFinite(r.UV[v].x) && MathUtil.IsFinite(r.UV[v].y), $"{what}: vertex {v}");
            Assert.Equal(0, r.Distortion.FlippedTriangles);
            Assert.Equal(0, r.Distortion.DegenerateUVTriangles);
            Assert.Equal(m.TriangleCount, r.Distortion.Triangles);
            Assert.True(UVOverlap.Compute(m, r.UV).IsInjective, $"{what}: not injective");
            Assert.True(r.Converged);
        }

        [Fact]
        public void MirrorSymmetricDisc_SymmetricMap()
        {
            // finding P3 on a disc: LSCM's automatic pins (the two diagonals of the plate are equally far, mirror images of
            // each other) make its map asymmetric beyond rounding; without pins the spectral map is symmetric to rounding,
            // with the same distortion
            DMesh3 m = Plate(out var pairs);
            ParameterizationResult lscm = new LSCMParameterization(m).Compute();
            ParameterizationResult s = new SpectralConformalParameterization(m).Compute();
            AssertValid(m, s, "spectral");
            double asymLscm = Asymmetry(m, lscm.UV, pairs), asymSpectral = Asymmetry(m, s.UV, pairs);
            Assert.True(asymSpectral < 1e-11, $"spectral: mirrored distances differ by {asymSpectral} of the size");
            Assert.True(asymLscm > 1e-6, $"lscm (the reason for the option): {asymLscm}");
            Assert.True(Math.Abs(s.Distortion.MeanConformal - lscm.Distortion.MeanConformal) < 1e-3, $"mean conformal {s.Distortion.MeanConformal} vs {lscm.Distortion.MeanConformal}");
            Assert.True(s.Distortion.MaxConformal <= lscm.Distortion.MaxConformal * (1 + 1e-6), $"max conformal {s.Distortion.MaxConformal} vs {lscm.Distortion.MaxConformal}");
        }

        [Theory]
        [InlineData(0.5, false)]
        [InlineData(0.8, true)]
        public void CurvedCap_NoFold_MaxDistortionAsLscm(double angleOverPi, bool lower)
        {
            // a curved disc (a cap up to 0.5 pi and 0.8 pi from the pole): no flipped or degenerate triangle, injective,
            // the mean distortion as LSCM's; the maximum lower on the strongly curved cap (measured 1.18 against 1.27)
            // and within 0.1 % on the hemisphere (1.0570 against 1.0564: not lower everywhere)
            DMesh3 m = Cap(20, angleOverPi * Math.PI);
            var sc = new SpectralConformalParameterization(m);
            ParameterizationResult s = sc.Compute(), l = new LSCMParameterization(m).Compute();
            AssertValid(m, s, "spectral");
            Assert.False(sc.NearlyDoubleEigenvalue);
            Assert.True(sc.NextEigenvalue > 2 * sc.Eigenvalue, $"gap: {sc.NextEigenvalue} / {sc.Eigenvalue}");
            Assert.True(Math.Abs(s.Distortion.MeanConformal - l.Distortion.MeanConformal) < 0.01 * l.Distortion.MeanConformal);
            double bound = lower ? 0.95 * l.Distortion.MaxConformal : 1.001 * l.Distortion.MaxConformal;
            Assert.True(s.Distortion.MaxConformal <= bound, $"max conformal {s.Distortion.MaxConformal} vs {l.Distortion.MaxConformal}");
            // ScaleToSurfaceArea: the UV area equals the surface area
            Assert.True(Math.Abs(s.Distortion.AreaScale - 1) < 1e-9, $"area scale {s.Distortion.AreaScale}");
        }

        [Fact]
        public void DevelopableDisc_ExactAndStopsEarly()
        {
            // a developable disc (a cut cylinder) maps isometrically; the smallest eigenvalue is at rounding level, and
            // the iteration stops at the rounding floor of the solve instead of running to MaxIterations
            DMesh3 m = MapGrid(30, 30, (x, y) => new Vector3d(Math.Cos(1.9 * Math.PI * x), Math.Sin(1.9 * Math.PI * x), 2 * y));
            var sc = new SpectralConformalParameterization(m);
            ParameterizationResult s = sc.Compute();
            AssertValid(m, s, "spectral");
            Assert.True(s.Distortion.MaxConformal < 1 + 1e-6 && s.Distortion.MaxAreaDistortion < 1e-6, $"{s.Distortion.MaxConformal}, {s.Distortion.MaxAreaDistortion}");
            Assert.True(sc.Iterations < 20, $"{sc.Iterations} iterations");
        }

        [Fact]
        public void NearlyFlatDisc_StopsAtRoundingFloor()
        {
            // the plate at a finer resolution: the smallest eigenvalue is small (1e-10 relative) but not rounding, and the
            // change of the iterate stalls at about 1e-10, above Tolerance: it converges by the stall rule (a mutant
            // without it runs to MaxIterations and refuses)
            DMesh3 m = Plate(out _, 60, 40);
            var sc = new SpectralConformalParameterization(m);
            ParameterizationResult s = sc.Compute();
            AssertValid(m, s, "spectral");
            Assert.True(sc.Iterations < 20, $"{sc.Iterations} iterations");
            Assert.True(sc.Eigenvalue > 1e-12 && sc.Eigenvalue < 1e-8, $"eigenvalue {sc.Eigenvalue}");
        }

        [Theory]
        [InlineData(8, 3, 0.6)]
        [InlineData(48, 12, 0.3)]
        public void Annulus_Refused(int sides, int rings, double dz)
        {
            // an open cylinder (finding P1): the smallest eigenvalue is double (either ring inside) and the computed map
            // can fold; not a disc, so refused with InvalidTopology and no map (the caller uses LSCM)
            DMesh3 m = OpenCylinder(sides, rings, dz);
            var sc = new SpectralConformalParameterization(m);
            ParameterizationResult r = sc.Compute();
            Assert.Equal(ParameterizationStatus.InvalidTopology, r.Status);
            Assert.Null(r.UV);
            Assert.Contains("disc", r.Error);
            Assert.False(r.Succeeded);
        }

        [Fact]
        public void ClosedAndMultiComponentMeshes_Refused()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { EdgeVertices = 4 }.Generate().MakeDMesh();
            Assert.Equal(ParameterizationStatus.InvalidTopology, new SpectralConformalParameterization(sphere).Compute().Status);
            DMesh3 two = Cap(4, 1);
            MeshEditor.Append(two, Cap(4, 1));
            Assert.Equal(ParameterizationStatus.InvalidTopology, new SpectralConformalParameterization(two).Compute().Status);
        }

        [Fact]
        public void SmallGap_Refused()
        {
            // the near-double check on a disc: the cap's next pair is 6.4 times the smallest; with MinEigenvalueGap above
            // that, the map is refused with SolverFailed (NearlyDoubleEigenvalue), no map; below it, accepted
            DMesh3 m = Cap(20, 0.8 * Math.PI);
            var sc = new SpectralConformalParameterization(m) { MinEigenvalueGap = 10 };
            ParameterizationResult r = sc.Compute();
            Assert.Equal(ParameterizationStatus.SolverFailed, r.Status);
            Assert.True(sc.NearlyDoubleEigenvalue);
            Assert.Null(r.UV);
            Assert.Contains("nearly double", r.Error);
            Assert.True(sc.NextEigenvalue / sc.Eigenvalue > 5 && sc.NextEigenvalue / sc.Eigenvalue < 8, $"{sc.NextEigenvalue / sc.Eigenvalue}");
            sc.MinEigenvalueGap = 5;
            Assert.True(sc.Compute().Succeeded);
            Assert.False(sc.NearlyDoubleEigenvalue);
            // an iteration that cannot converge (a small gap) is refused too
            var slow = new SpectralConformalParameterization(m) { MaxIterations = 3 };
            Assert.Equal(ParameterizationStatus.SolverFailed, slow.Compute().Status);
            Assert.True(slow.NearlyDoubleEigenvalue);
        }

        [Fact]
        public void HingedWings_Refused()
        {
            // a disc whose two flat wings meet only through zero-area triangles at a doubled vertex (the shape of tiny
            // corpus charts, eg from duplicated vertices): each wing can take its own similarity, so the smallest
            // eigenvalue is not single and the map would be a mixture. Refused with SolverFailed
            // and no map; LSCM pins one of the maps (Success, max conformal 1)
            var m = new DMesh3();
            Vector3d[] p = { new Vector3d(0, 0, 0), new Vector3d(0, 0, 0), new Vector3d(-2, -2, 0), new Vector3d(0.2, -2, -0.6),
                new Vector3d(0.2, 0.5, -0.6), new Vector3d(0, -2, 0), new Vector3d(0.2, 0.5, 0.6), new Vector3d(0.2, -2, 0.6) };
            foreach (Vector3d q in p) m.AppendVertex(q);
            foreach (var (a, b, c) in new[] { (0, 1, 2), (3, 4, 1), (0, 3, 1), (0, 5, 3), (0, 6, 7), (0, 7, 5) })
                Assert.True(m.AppendTriangle(a, b, c) >= 0);
            var sc = new SpectralConformalParameterization(m);
            ParameterizationResult r = sc.Compute();
            // (refused either by the eigenvalue check or already by the factorization: with more null modes than the
            // translations the shifted matrix need not be positive definite)
            Assert.Equal(ParameterizationStatus.SolverFailed, r.Status);
            Assert.Null(r.UV);
            Assert.True(sc.NearlyDoubleEigenvalue || r.Error.Contains("positive definite"), r.Error);
            ParameterizationResult l = new LSCMParameterization(m).Compute();
            Assert.True(l.Succeeded && l.Distortion.MaxConformal < 1 + 1e-6, $"lscm {l.Status}");
        }

        [Fact]
        public void NearlyDouble_Rule()
        {
            // the refusal rule alone: the next pair must be (1 + gap) times the smallest and above rounding (1e-12)
            Assert.False(SpectralConformalParameterization.NearlyDouble(1e-8, 2e-8, 0.1));
            Assert.True(SpectralConformalParameterization.NearlyDouble(1e-8, 1.05e-8, 0.1));
            Assert.False(SpectralConformalParameterization.NearlyDouble(1e-8, 1.11e-8, 0.1));
            Assert.True(SpectralConformalParameterization.NearlyDouble(1e-8, 0.5e-8, 0.1));
            // developable: the smallest at rounding level is fine if the next is not
            Assert.False(SpectralConformalParameterization.NearlyDouble(-2e-17, 5e-8, 0.1));
            // both at rounding level (a positive next above 1.1 times a negative smallest): refused by the floor
            Assert.True(SpectralConformalParameterization.NearlyDouble(-2e-17, 3e-15, 0.1));
            Assert.True(SpectralConformalParameterization.NearlyDouble(1e-20, 1e-12, 0.1));
            Assert.False(SpectralConformalParameterization.NearlyDouble(1e-20, 2e-12, 0.1));
            Assert.True(SpectralConformalParameterization.NearlyDouble(1e-8, double.NaN, 0.1));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void InvariantUnderSimilarities_ReuseAndRepeat(string name)
        {
            // every similarity gives a valid map with the same distortion; a 2^k scale the same map, scaled, bit for bit
            // (the iterate is normalized by its largest coordinate, not by a square root); the reused instance and a new
            // instance give the same map again
            Similarity sim = Similarity.ByName(name);
            DMesh3 m0 = Cap(16, 0.7 * Math.PI), m = Invariance.Transformed(m0, sim);
            var sc0 = new SpectralConformalParameterization(m0);
            ParameterizationResult r0 = sc0.Compute();
            var sc = new SpectralConformalParameterization(m);
            Vector2d[] first = null;
            foreach (int pass in new[] { 0, 1 }) {
                ParameterizationResult r = sc.Compute();
                AssertValid(m, r, $"{name}, pass {pass}");
                Assert.True(Math.Abs(r.Distortion.MaxConformal - r0.Distortion.MaxConformal) < 1e-6 * r0.Distortion.MaxConformal, $"{name}: {r.Distortion.MaxConformal} vs {r0.Distortion.MaxConformal}");
                Assert.True(Math.Abs(sc.Eigenvalue - sc0.Eigenvalue) < 1e-6 * sc0.Eigenvalue, $"{name}: eigenvalue {sc.Eigenvalue} vs {sc0.Eigenvalue}");
                if (sim.IsExact) {
                    Assert.Equal(sc0.Eigenvalue, sc.Eigenvalue);
                    Assert.Equal(sc0.Iterations, sc.Iterations);
                    foreach (int v in m.VertexIndices())
                        Assert.Equal(sim.Scale * r0.UV[v], r.UV[v]);
                }
                if (first == null) first = r.UV;
                else Assert.Equal(first, r.UV);
            }
            Assert.Equal(first, new SpectralConformalParameterization(m).Compute().UV);
        }

        [Fact]
        public void Deterministic_RepeatedRuns()
        {
            // no parallel path: repeated runs (new instances, also from several threads at once) give the same map bit for bit
            DMesh3 m = Cap(24, 0.8 * Math.PI);
            Vector2d[] reference = new SpectralConformalParameterization(m).Compute().UV;
            var results = new Vector2d[6][];
            System.Threading.Tasks.Parallel.For(0, results.Length, k => results[k] = new SpectralConformalParameterization(m).Compute().UV);
            foreach (Vector2d[] uv in results)
                Assert.Equal(reference, uv);
        }

        [Fact]
        public void Reuse_OtherMeshThenBack_SameAsFresh()
        {
            // one instance on mesh A, then B, then A again: each result equals a fresh instance's, bit for bit (no state
            // from the previous Compute, eg the Cholesky pattern or the eigenvalue estimates)
            DMesh3 a = Cap(16, 0.7 * Math.PI), b = Plate(out _);
            Vector2d[] freshA = new SpectralConformalParameterization(a).Compute().UV;
            var fb = new SpectralConformalParameterization(b);
            Vector2d[] freshB = fb.Compute().UV;
            var sc = new SpectralConformalParameterization(a);
            Assert.Equal(freshA, sc.Compute().UV);
            sc.Mesh = b;
            Assert.Equal(freshB, sc.Compute().UV);
            Assert.Equal(fb.Eigenvalue, sc.Eigenvalue);
            Assert.Equal(fb.NextEigenvalue, sc.NextEigenvalue);
            sc.Mesh = a;
            sc.MinEigenvalueGap = 100;
            Assert.False(sc.Compute().Succeeded);
            sc.MinEigenvalueGap = 0.1;
            Assert.Equal(freshA, sc.Compute().UV);
            Assert.False(sc.NearlyDoubleEigenvalue);
        }

        [Fact]
        public void NextPairNotSettled_Refused()
        {
            // the estimate of the next pair is accepted only when its Rayleigh quotient settled: with a limit of one
            // step it cannot, and the map is refused (an upper bound alone does not prove a large gap)
            DMesh3 m = Cap(20, 0.8 * Math.PI);
            var sc = new SpectralConformalParameterization(m) { NextMaxIterationsLimit = 1 };
            ParameterizationResult r = sc.Compute();
            Assert.Equal(ParameterizationStatus.SolverFailed, r.Status);
            Assert.True(sc.NearlyDoubleEigenvalue);
            Assert.True(double.IsNaN(sc.NextEigenvalue));
            Assert.True(new SpectralConformalParameterization(m).Compute().Succeeded);
        }

        [Fact]
        public void InvalidSettings_Refused()
        {
            DMesh3 m = Cap(8, 1);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new SpectralConformalParameterization(m) { MaxIterations = 0 }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new SpectralConformalParameterization(m) { Tolerance = double.NaN }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new SpectralConformalParameterization(m) { MinEigenvalueGap = -1 }.Compute().Status);
        }

        [Fact]
        public void CurvedDiscWithTJunctions_Success()
        {
            // a curved cap with 40 zero-area triangles (T-junctions: an edge split, then the flip that leaves the split
            // vertex on the old edge). Their mollified weights (about 1e8) must not inflate the shift: the map is a
            // success, with the T-junction vertices on their edges, and the distortion of the other triangles as without
            // them (a shift from trace(A) made the iteration too slow: refused as not converged)
            DMesh3 m = Cap(20, 0.8 * Math.PI);
            ParameterizationResult plain = new SpectralConformalParameterization(m).Compute();
            int junctions = 0;
            foreach (int e in m.EdgeIndices().ToList()) {
                if (junctions == 40) break;
                if (e % 9 != 0 || m.IsBoundaryEdge(e)) continue;
                if (m.SplitEdge(e, out DMesh3.EdgeSplitInfo split) != MeshResult.Ok) continue;
                // flip the edge from the new vertex to one opposite vertex: the triangle (a, new, b) then has zero area
                if (m.FlipEdge(split.eNewCN, out _) == MeshResult.Ok) junctions++;
            }
            Assert.Equal(40, junctions);
            int degenerate = m.TriangleIndices().Count(t => m.GetTriArea(t) < 1e-12);
            Assert.True(degenerate >= 40, $"{degenerate} zero-area triangles");
            var sc = new SpectralConformalParameterization(m);
            ParameterizationResult r = sc.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(0, r.Distortion.FlippedTriangles + r.Distortion.DegenerateUVTriangles);
            Assert.True(Math.Abs(r.Distortion.MeanConformal - plain.Distortion.MeanConformal) < 0.01, $"{r.Distortion.MeanConformal} vs {plain.Distortion.MeanConformal}");
            Assert.True(sc.Iterations < 30, $"{sc.Iterations} iterations");
        }
    }
}
