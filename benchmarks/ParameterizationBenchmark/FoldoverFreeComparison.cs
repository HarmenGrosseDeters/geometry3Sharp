using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

static class FoldoverFreeComparison
{
    static string F(double x) => x.ToString("R", CultureInfo.InvariantCulture);

    static DMesh3 Grid(int n, bool peaks)
    {
        var m = new DMesh3();
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i) {
                double x = (double)i / n, y = (double)j / n, z = 0;
                if (peaks)
                    foreach (var c in new[] { new Vector2d(0.3, 0.3), new Vector2d(0.7, 0.35), new Vector2d(0.5, 0.75) })
                        z += 0.6 * Math.Exp(-((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y)) / 0.004);
                m.AppendVertex(new Vector3d(x, y, z));
            }
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i;
                m.AppendTriangle(a, a + 1, a + n + 2); m.AppendTriangle(a, a + n + 2, a + n + 1);
            }
        return m;
    }

    static DMesh3 Annulus()
    {
        const int nu = 8, nv = 4;
        var m = new DMesh3();
        for (int j = 0; j < nv; ++j)
            for (int i = 0; i < nu; ++i) {
                double a = 2 * Math.PI * i / nu;
                m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0.6 * j));
            }
        for (int j = 0; j < nv - 1; ++j)
            for (int i = 0; i < nu; ++i) {
                int a = j * nu + i, b = j * nu + (i + 1) % nu, c = ((j + 1) % nv) * nu + i, d = ((j + 1) % nv) * nu + (i + 1) % nu;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
        return m;
    }

    static ParameterizationResult Run(string fixture, string method, string boundary, DMesh3 mesh, Func<ParameterizationResult> compute)
    {
        var clock = Stopwatch.StartNew(); var r = compute(); clock.Stop();
        var overlap = r.UV == null ? null : UVOverlap.Compute(mesh, r.UV, 0);
        Console.WriteLine(string.Join(",", fixture, method, boundary, r.Status, r.Iterations, r.Converged,
            r.Distortion?.FlippedTriangles.ToString() ?? "NA", r.Distortion?.DegenerateUVTriangles.ToString() ?? "NA",
            r.Distortion == null ? "NA" : F(r.Distortion.SymmetricDirichlet), overlap?.OverlappingPairs.ToString() ?? "NA", F(clock.Elapsed.TotalMilliseconds)));
        if (r.Error != null) Console.WriteLine("diagnostic=" + r.Error);
        return r;
    }

    static ParameterizationResult Compare(string name, DMesh3 mesh, Vector2d[] start, ParameterizationResult slim)
    {
        Run(name, "start", "unchanged", mesh, () => {
            var d = ParameterizationDistortion.Compute(mesh, start);
            return new ParameterizationResult { UV = start, Distortion = d, Converged = false,
                Status = d.FlippedTriangles + d.DegenerateUVTriangles == 0 ? ParameterizationStatus.Success : ParameterizationStatus.FlippedTriangles };
        });
        Run(name, "SLIM-same-start", "free", mesh, () => new SLIMParameterization(mesh) { InitialUV = start }.Compute());
        var repaired = Run(name, "FoldoverFree", "fixed", mesh, () => new FoldoverFreeParameterization(mesh, start).Compute());
        bool same = repaired.UV != null;
        foreach (int v in mesh.VertexIndices())
            if (mesh.IsBoundaryVertex(v) && repaired.UV != null)
                same &= BitConverter.DoubleToInt64Bits(start[v].x) == BitConverter.DoubleToInt64Bits(repaired.UV[v].x) &&
                    BitConverter.DoubleToInt64Bits(start[v].y) == BitConverter.DoubleToInt64Bits(repaired.UV[v].y);
        Console.WriteLine("fixed_boundary_bitwise=" + (repaired.UV == null ? "NA" : same.ToString()));
        if (repaired.Succeeded && same) Console.WriteLine("fixed_boundary_feasibility=locally-feasible-witness");
        double ratio = repaired.Distortion != null && slim.Distortion != null ? repaired.Distortion.SymmetricDirichlet / slim.Distortion.SymmetricDirichlet : double.NaN;
        Console.WriteLine("quality_ratio_vs_free_boundary_SLIM=" + F(ratio) + "; target=" +
            (repaired.Succeeded && MathUtil.IsFinite(ratio) && ratio <= 1.1 ? "met" : "UNSATISFIED"));
        return repaired;
    }

    public static void Run()
    {
        Console.WriteLine("fixture,method,boundary,status,iterations,converged,flips,degenerate,symmetric_dirichlet,overlap_pairs,milliseconds");
        var disc = Grid(4, false); var folded = new TutteEmbedding(disc).Compute().UV;
        folded[12] = new Vector2d(2, 0); folded[7] = new Vector2d(-1.5, 1.5);
        Compare("folded-disc", disc, folded, Run("folded-disc", "SLIM-default", "free", disc, () => new SLIMParameterization(disc).Compute()));
        foreach (bool peaks in new[] { false, true }) {
            string name = peaks ? "three-peaks-30" : "annulus-8x3";
            var mesh = peaks ? Grid(30, true) : Annulus();
            var lscm = Run(name, "LSCM", "two-pins", mesh, () => new LSCMParameterization(mesh).Compute());
            var slim = Run(name, "SLIM-default", "free", mesh, () => new SLIMParameterization(mesh).Compute());
            if (lscm.UV != null) {
                if (!peaks) {
                    foreach (var loop in new MeshBoundaryLoops(mesh).Loops) {
                        double minY = loop.Vertices.Min(v => lscm.UV[v].y), maxY = loop.Vertices.Max(v => lscm.UV[v].y);
                        Console.WriteLine("annulus_boundary_vertices=" + string.Join(";", loop.Vertices.Select(v => v + ":" + F(lscm.UV[v].x) + ":" + F(lscm.UV[v].y))));
                        Console.WriteLine("boundary_y_range=" + F(minY) + ":" + F(maxY) + "; feasibility=" +
                            (minY == maxY ? "infeasible-collinear-closed-boundary" : "undetermined-not-certified"));
                    }
                }
                Compare(name, mesh, lscm.UV, slim);
            }
        }
        // Same vertex/triangle order and coordinate evaluation as UVAtlasTests.Torus: cut tie breaks depend on ids.
        var torus = new DMesh3();
        for (int i = 0; i < 48; ++i)
            for (int j = 0; j < 20; ++j) {
                double u = 2 * Math.PI * ((double)i / 48), v = 2 * Math.PI * ((double)j / 20);
                torus.AppendVertex(new Vector3d((2 + 0.7 * Math.Cos(v)) * Math.Cos(u), (2 + 0.7 * Math.Cos(v)) * Math.Sin(u), 0.7 * Math.Sin(v)));
            }
        for (int i = 0; i < 48; ++i)
            for (int j = 0; j < 20; ++j) {
                int a = i * 20 + j, b = ((i + 1) % 48) * 20 + j, c = ((i + 1) % 48) * 20 + (j + 1) % 20, d = i * 20 + (j + 1) % 20;
                torus.AppendTriangle(a, b, c); torus.AppendTriangle(a, c, d);
            }
        var atlas = new UVAtlasGenerator(torus) { Method = UVAtlasGenerator.Methods.Cones };
        atlas.Compute();
        var cone = new ConeFlattening(torus) { FeatureWeight = atlas.FeatureWeight, MaxCones = atlas.MaxCones, MaxScaleFactor = atlas.MaxConeScaleFactor };
        var cr = cone.Compute();
        Console.WriteLine($"torus-stock status={atlas.Status}; components={atlas.ComponentCount}; cone_components={atlas.ConeComponents}; charts={atlas.ChartCount}; fallback_components={atlas.ComponentCount - atlas.ConeComponents}");
        if (cr.UV == null) { Console.WriteLine("torus-cone unavailable=" + cr.Error); return; }
        var repair = Compare("torus-cone-cut", cone.CutMesh, cr.UV,
            Run("torus-cone-cut", "SLIM-default", "free", cone.CutMesh, () => new SLIMParameterization(cone.CutMesh).Compute()));
        bool injective = repair.UV != null && UVOverlap.Compute(cone.CutMesh, repair.UV, 0).IsInjective;
        bool accepted = repair.Succeeded && injective && repair.Distortion.MeanAreaDistortion <= atlas.MaxAreaDistortion &&
            repair.Distortion.MeanConformal <= atlas.MaxConformalDistortion;
        Console.WriteLine($"torus-repair globally_injective={injective}; accepted_by_auto_limits={accepted}; counterfactual_cones_fallback_components={(injective && repair.Succeeded ? 0 : 1)}; production_defaults_unchanged=true");
    }
}
