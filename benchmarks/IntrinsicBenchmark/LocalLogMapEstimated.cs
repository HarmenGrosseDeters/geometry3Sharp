// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Gate 2 of the affine log map (docs/design/AFFINE_LOGMAP_GATE.md, "Follow-up 2"): MeshLocalParam with normals estimated
// from the mesh, and the far field up to 0.9 pi on the spheres. Criteria are fixed in the design note before the run.
//
//   dotnet run -c Release --project benchmarks/IntrinsicBenchmark -- --local-logmap-est [points.csv]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

static partial class Program
{
    // (lo, hi] bands of geodesic distance; the first three are the near-field patches (lo = 0)
    static readonly (double lo, double hi)[] EstBands = { (0, 0.3), (0, 0.6), (0, 1.2), (1.2, 1.8), (1.8, 2.4), (2.4, 2.85) };
    const double EstMaxDistance = 3.0;
    const double EstFloor = 1e-4;

    // angle-weighted vertex normals (Thuermer and Wuethrich 1998; Max 1999), from the mesh alone
    static Vector3d[] AngleWeightedNormals(DMesh3 mesh)
    {
        var n = new Vector3d[mesh.MaxVertexID];
        foreach (int t in mesh.TriangleIndices()) {
            Index3i tri = mesh.GetTriangle(t);
            Vector3d a = mesh.GetVertex(tri.a), b = mesh.GetVertex(tri.b), c = mesh.GetVertex(tri.c);
            Vector3d fn = (b - a).Cross(c - a).Normalized;
            Vector3d[] p = { a, b, c };
            for (int k = 0; k < 3; ++k) {
                Vector3d u = (p[(k + 1) % 3] - p[k]).Normalized, w = (p[(k + 2) % 3] - p[k]).Normalized;
                n[tri[k]] += fn * Math.Acos(MathUtil.Clamp(u.Dot(w), -1, 1));
            }
        }
        for (int i = 0; i < n.Length; ++i) if (n[i].LengthSquared > 0) n[i] = n[i].Normalized;
        return n;
    }

    static bool LocalLogMapEstimatedGate(string pointPath)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var points = pointPath == null ? TextWriter.Null : new StreamWriter(pointPath);
        points.WriteLine("fixture,source,vertex,dist,legacy,affine,mlp_upwind_est,mlp_exp_est,mlp_upwind_exact,mlp_exp_exact");
        string[] methods = { "affine", "legacy", "mlp-upwind(est)", "mlp-exp(est)", "mlp-upwind(exact)", "mlp-exp(exact)" };
        var cells = new List<(string fixture, (double lo, double hi) band, double[][] errors)>();
        bool finite = true;
        double normalDev = 0;
        foreach (LocalFixture f in LocalFixtures()) {
            DMesh3 mesh = f.Mesh;
            double h = mesh.EdgeIndices().Average(e => { Index2i ev = mesh.GetEdgeV(e); return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); });
            Vector3d[] est = AngleWeightedNormals(mesh);
            double dev = mesh.VertexIndices().Max(v => Math.Acos(MathUtil.Clamp(est[v].Dot(f.Normal(mesh.GetVertex(v))), -1, 1)));
            double devMean = mesh.VertexIndices().Average(v => Math.Acos(MathUtil.Clamp(est[v].Dot(f.Normal(mesh.GetVertex(v))), -1, 1)));
            normalDev = Math.Max(normalDev, dev);
            Console.WriteLine($"{f.Name}: estimated normal deviation from the smooth normal, mean {devMean * 180 / Math.PI:F2} deg, max {dev * 180 / Math.PI:F2} deg");
            int[] sources = f.Sources.Select(p => Nearest(mesh, p)).ToArray();
            var all = new List<double>[EstBands.Length, methods.Length];
            for (int b = 0; b < EstBands.Length; ++b) for (int m = 0; m < methods.Length; ++m) all[b, m] = new List<double>();
            var vhm = new VectorHeatMethod(mesh);
            foreach (int source in sources) {
                Vector3d p0 = mesh.GetVertex(source), nExact = f.Normal(p0), nEst = est[source];
                Vector3d zero = vhm.Triangulation.ToWorld(source, Vector2d.AxisX);
                Vector3d e1 = (zero - nEst * zero.Dot(nEst)).Normalized, e2 = nEst.Cross(e1);       // estimated-normal frame
                Vector3d x1 = (zero - nExact * zero.Dot(nExact)).Normalized, x2 = nExact.Cross(x1); // exact frame (informational)
                Vector2d[] legacy = vhm.ComputeLogMap(source), affine = vhm.ComputeLogMap(source, LogMapStrategy.AffineLocal);
                if (affine[source].Length != 0) finite = false;
                Index3i seed = mesh.GetTriangle(mesh.VtxTrianglesItr(source).First());
                MeshLocalParam Make(Func<int, Vector3f> normal, Frame3f frame, bool upwind)
                {
                    var q = new MeshLocalParam(mesh.MaxVertexID, v => (Vector3f)mesh.GetVertex(v), normal, v => mesh.VtxVerticesItr(v));
                    q.UVMode = upwind ? MeshLocalParam.UVModes.ExponentialMap_UpwindAvg : MeshLocalParam.UVModes.ExponentialMap;
                    q.ComputeToMaxDistance(frame, seed, (float)EstMaxDistance);
                    return q;
                }
                Func<int, Vector3f> estN = v => (Vector3f)est[v], exactN = v => (Vector3f)f.Normal(mesh.GetVertex(v));
                var fe = new Frame3f((Vector3f)p0, (Vector3f)e1, (Vector3f)e2, (Vector3f)nEst);
                var fx = new Frame3f((Vector3f)p0, (Vector3f)x1, (Vector3f)x2, (Vector3f)nExact);
                MeshLocalParam[] uv = { Make(estN, fe, true), Make(estN, fe, false), Make(exactN, fx, true), Make(exactN, fx, false) };
                foreach (int v in mesh.VertexIndices()) {
                    if (v == source) continue;
                    Vector3d exact3 = f.Exact(p0, mesh.GetVertex(v));
                    double d = exact3.Length;
                    if (d > EstBands.Last().hi) continue;
                    double[] err = new double[methods.Length];
                    err[0] = (affine[v].x * e1 + affine[v].y * e2).Distance(exact3) / h;
                    err[1] = (legacy[v].x * e1 + legacy[v].y * e2).Distance(exact3) / h;
                    for (int k = 0; k < 4; ++k) {
                        Vector2d u = (Vector2d)uv[k].GetUV(v);
                        Vector3d a = k < 2 ? e1 : x1, b = k < 2 ? e2 : x2;
                        err[2 + k] = (u.x * a + u.y * b).Distance(exact3) / h;
                    }
                    if (!double.IsFinite(err[0]) || !double.IsFinite(err[1])) finite = false;
                    for (int m = 0; m < err.Length; ++m) if (!double.IsFinite(err[m]) || err[m] > 1e6) err[m] = double.PositiveInfinity;
                    points.WriteLine($"{f.Name},{source},{v},{d:R},{err[1]:R},{err[0]:R},{err[2]:R},{err[3]:R},{err[4]:R},{err[5]:R}");
                    for (int b = 0; b < EstBands.Length; ++b)
                        if (d > EstBands[b].lo && d <= EstBands[b].hi) for (int m = 0; m < methods.Length; ++m) all[b, m].Add(err[m]);
                }
            }
            for (int b = 0; b < EstBands.Length; ++b)
                if (all[b, 0].Count > 0) cells.Add((f.Name, EstBands[b], Enumerable.Range(0, methods.Length).Select(m => all[b, m].ToArray()).ToArray()));
        }

        Console.WriteLine();
        Console.WriteLine("Mean / max error in mean edge lengths. Competitors of the gate: legacy, mlp-upwind(est), mlp-exp(est).");
        Console.WriteLine("| fixture | band | n | affine | legacy | mlp-upwind(est) | mlp-exp(est) | mlp-upwind(exact) | mlp-exp(exact) | affine / best (mean) | affine / best (max) | gate |");
        Console.WriteLine("|---|---|---:|---|---|---|---|---|---|---:|---:|---|");
        int g1 = 0, gateCells = 0; bool g2 = true, g3 = true;
        foreach (var c in cells) {
            double[] mean = c.errors.Select(e => e.Average()).ToArray(), max = c.errors.Select(e => e.Max()).ToArray();
            double bestMean = Math.Min(mean[1], Math.Min(mean[2], mean[3])), bestMax = Math.Min(max[1], Math.Min(max[2], max[3]));
            double rm = Math.Max(mean[0], EstFloor) / Math.Max(bestMean, EstFloor), rx = Math.Max(max[0], EstFloor) / Math.Max(bestMax, EstFloor);
            bool inGate = !c.fixture.StartsWith("plane") && (c.band.lo > 0 ? c.fixture.StartsWith("sphere") : true);
            if (inGate) {
                gateCells++;
                if (rm <= 0.5) g1++;
                if (rm > 1.25) g2 = false;
                if (rx > 1.5) g3 = false;
            }
            string bandName = c.band.lo > 0 ? $"({c.band.lo},{c.band.hi}]" : $"<= {c.band.hi}";
            Console.WriteLine($"| {c.fixture} | {bandName} | {c.errors[0].Length} | " +
                string.Join(" | ", Enumerable.Range(0, methods.Length).Select(m => $"{mean[m]:G3} / {max[m]:G3}")) +
                $" | {rm:G3} | {rx:G3} | {(inGate ? "yes" : "no")} |");
        }
        Console.WriteLine();
        bool pass = gateCells == 21 && g1 * 4 >= gateCells * 3 && g2 && g3 && finite;
        Console.WriteLine($"gate cells: {gateCells}; G1 (mean <= 0.5 x best competitor): {g1} of {gateCells} (need 16); G2 (mean <= 1.25 x best in every cell): {g2}; " +
            $"G3 (max <= 1.5 x best in every cell): {g3}; G4 (finite, source zero): {finite}");
        Console.WriteLine(pass ? "GATE 2 PASS: AffineLocal wins clearly" : "GATE 2 FAIL: AffineLocal does not win clearly");
        return pass;
    }
}
