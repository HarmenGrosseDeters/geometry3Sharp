// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Near-field gate of the affine log map (docs/design/AFFINE_LOGMAP_GATE.md, "Follow-up: the near-field gate"):
// AffineLocal against Legacy and MeshLocalParam (both modes) inside a patch radius, on procedural fixtures with
// closed-form log maps. The gate (metric, radii, thresholds) is fixed in the design note before the run.
//
//   dotnet run -c Release --project benchmarks/IntrinsicBenchmark -- --local-logmap [points.csv]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

static partial class Program
{
    sealed class LocalFixture
    {
        public string Name;
        public DMesh3 Mesh;
        public Func<Vector3d, Vector3d> Normal;               // smooth surface normal at a surface point
        public Func<Vector3d, Vector3d, Vector3d> Exact;      // log map (3D tangent vector at p0) of p, from p0
        public Vector3d[] Sources;                            // target points, the nearest vertices are used
        public double Boundary;                               // minimum distance of a source to the boundary
    }

    static readonly double[] LocalRadii = { 0.3, 0.6, 1.2 };
    const int LocalSeed = 20261108;

    static bool LocalLogMapGate(string pointPath)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var points = pointPath == null ? TextWriter.Null : new StreamWriter(pointPath);
        points.WriteLine("fixture,source,vertex,radius,legacy,affine,mlp_upwind,mlp_expmap");
        var fixtures = LocalFixtures();
        foreach (LocalFixture f in fixtures) {
            // a gate on a broken fixture says nothing: manifold, no duplicate vertices, closed where it should be,
            // outward orientation of the spheres
            if (!f.Mesh.CheckValidity(false, FailMode.ReturnOnly)) throw new Exception(f.Name + ": invalid mesh");
            bool closed = f.Mesh.IsClosed();
            if (closed != f.Name.StartsWith("sphere")) throw new Exception(f.Name + ": unexpected boundary state");
            if (closed && MeshMeasurements.VolumeArea(f.Mesh, f.Mesh.TriangleIndices(), f.Mesh.GetVertex).x <= 0) throw new Exception(f.Name + ": inward orientation");
            if (f.Mesh.VertexIndices().Select(v => f.Mesh.GetVertex(v)).GroupBy(p => (Math.Round(p.x, 9), Math.Round(p.y, 9), Math.Round(p.z, 9))).Any(g => g.Count() > 1))
                throw new Exception(f.Name + ": duplicate vertices");
        }
        string[] methods = { "affine", "legacy", "mlp-upwind", "mlp-expmap" };
        // errors[fixture][radius][method] = per-vertex errors in units of the mean edge length
        var cells = new List<(string fixture, double radius, double[][] errors)>();
        bool finite = true;
        var times = new List<string>();
        foreach (LocalFixture f in fixtures) {
            DMesh3 mesh = f.Mesh;
            double h = mesh.EdgeIndices().Average(e => { Index2i ev = mesh.GetEdgeV(e); return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); });
            int[] sources = f.Sources.Select(p => Nearest(mesh, p)).ToArray();
            var all = new List<double>[LocalRadii.Length, 4];
            for (int r = 0; r < LocalRadii.Length; ++r) for (int m = 0; m < 4; ++m) all[r, m] = new List<double>();
            var vhm = new VectorHeatMethod(mesh);
            var legacyMs = new List<double>(); var affineMs = new List<double>();
            var upwindMs = new List<double>(); var expMs = new List<double>();
            foreach (int source in sources) {
                Vector3d p0 = mesh.GetVertex(source), n0 = f.Normal(p0);
                Vector3d e1 = vhm.Triangulation.ToWorld(source, Vector2d.AxisX);
                e1 = (e1 - n0 * e1.Dot(n0)).Normalized;
                Vector3d e2 = n0.Cross(e1);
                Vector2d[] legacy = vhm.ComputeLogMap(source), affine = vhm.ComputeLogMap(source, LogMapStrategy.AffineLocal);
                if (!(affine[source].Length == 0)) finite = false;
                var frame = new Frame3f((Vector3f)p0, (Vector3f)e1, (Vector3f)e2, (Vector3f)n0);
                Index3i seedTriangle = mesh.GetTriangle(mesh.VtxTrianglesItr(source).First());
                var uv = new MeshLocalParam[2];
                for (int k = 0; k < 2; ++k) {
                    uv[k] = new MeshLocalParam(mesh.MaxVertexID, v => (Vector3f)mesh.GetVertex(v),
                        v => (Vector3f)f.Normal(mesh.GetVertex(v)), v => mesh.VtxVerticesItr(v));
                    uv[k].UVMode = k == 0 ? MeshLocalParam.UVModes.ExponentialMap_UpwindAvg : MeshLocalParam.UVModes.ExponentialMap;
                    uv[k].ComputeToMaxDistance(frame, seedTriangle, (float)(LocalRadii.Last() * 1.5));
                }
                foreach (int v in mesh.VertexIndices()) {
                    if (v == source) continue;
                    Vector3d exact3 = f.Exact(p0, mesh.GetVertex(v));
                    double d = exact3.Length;
                    if (d > LocalRadii.Last()) continue;
                    Vector2d oracle = new Vector2d(exact3.Dot(e1), exact3.Dot(e2));
                    double[] err = {
                        affine[v].Distance(oracle) / h, legacy[v].Distance(oracle) / h,
                        ((Vector2d)uv[0].GetUV(v)).Distance(oracle) / h, ((Vector2d)uv[1].GetUV(v)).Distance(oracle) / h
                    };
                    if (!double.IsFinite(err[0])) finite = false;
                    // MeshLocalParam leaves vertices it did not reach at InvalidUV: that is an error of the method
                    for (int m = 0; m < 4; ++m) if (!double.IsFinite(err[m]) || err[m] > 1e6) err[m] = double.PositiveInfinity;
                    points.WriteLine($"{f.Name},{source},{v},{d:R},{err[1]:R},{err[0]:R},{err[2]:R},{err[3]:R}");
                    for (int r = 0; r < LocalRadii.Length; ++r)
                        if (d <= LocalRadii[r]) for (int m = 0; m < 4; ++m) all[r, m].Add(err[m]);
                }
                if (source == sources[0]) {
                    legacyMs.Add(LocalMilliseconds(() => vhm.ComputeLogMap(source)));
                    affineMs.Add(LocalMilliseconds(() => vhm.ComputeLogMap(source, LogMapStrategy.AffineLocal)));
                    upwindMs.Add(LocalMilliseconds(() => { var q = new MeshLocalParam(mesh.MaxVertexID, v => (Vector3f)mesh.GetVertex(v),
                        v => (Vector3f)f.Normal(mesh.GetVertex(v)), v => mesh.VtxVerticesItr(v)); q.ComputeToMaxDistance(frame, seedTriangle, (float)LocalRadii.Last()); return q; }));
                    expMs.Add(LocalMilliseconds(() => { var q = new MeshLocalParam(mesh.MaxVertexID, v => (Vector3f)mesh.GetVertex(v),
                        v => (Vector3f)f.Normal(mesh.GetVertex(v)), v => mesh.VtxVerticesItr(v)) { UVMode = MeshLocalParam.UVModes.ExponentialMap };
                        q.ComputeToMaxDistance(frame, seedTriangle, (float)LocalRadii.Last()); return q; }));
                }
            }
            for (int r = 0; r < LocalRadii.Length; ++r)
                cells.Add((f.Name, LocalRadii[r], Enumerable.Range(0, 4).Select(m => all[r, m].ToArray()).ToArray()));
            times.Add($"| {f.Name} | {mesh.VertexCount} | {h:F4} | {legacyMs[0]:F2} | {affineMs[0]:F2} | {upwindMs[0]:F2} | {expMs[0]:F2} |");
        }

        Console.WriteLine("| fixture | radius | vertices | affine mean / max | legacy mean / max | MLP upwind mean / max | MLP exp mean / max | affine / best (mean) | affine / best (max) |");
        Console.WriteLine("|---|---:|---:|---|---|---|---|---:|---:|");
        int g1 = 0; bool g2 = true, g3 = true;
        foreach (var c in cells) {
            double[] mean = c.errors.Select(e => e.Average()).ToArray(), max = c.errors.Select(e => e.Max()).ToArray();
            double bestMean = Math.Min(mean[1], Math.Min(mean[2], mean[3])), bestMax = Math.Min(max[1], Math.Min(max[2], max[3]));
            double rm = mean[0] / bestMean, rx = max[0] / bestMax;
            if (rm <= 0.5) g1++;
            if (rm > 1.25) g2 = false;
            if (rx > 1.5) g3 = false;
            Console.WriteLine($"| {c.fixture} | {c.radius} | {c.errors[0].Length} | {mean[0]:G3} / {max[0]:G3} | {mean[1]:G3} / {max[1]:G3} | {mean[2]:G3} / {max[2]:G3} | {mean[3]:G3} / {max[3]:G3} | {rm:G3} | {rx:G3} |");
        }
        Console.WriteLine();
        Console.WriteLine("Errors in units of the mean edge length of the mesh.");
        Console.WriteLine();
        Console.WriteLine("| fixture | vertices | mean edge | legacy ms | affine ms | MLP upwind ms (patch 1.2) | MLP exp ms (patch 1.2) |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (string t in times) Console.WriteLine(t);
        Console.WriteLine();
        bool pass = g1 * 4 >= cells.Count * 3 && g2 && g3 && finite;
        Console.WriteLine($"G1 (mean <= 0.5 x best competitor): {g1} of {cells.Count} cells (need {(cells.Count * 3 + 3) / 4}); G2 (mean <= 1.25 x best in every cell): {g2}; " +
            $"G3 (max <= 1.5 x best in every cell): {g3}; G4 (finite, source zero): {finite}");
        Console.WriteLine(pass ? "GATE PASS: AffineLocal wins clearly" : "GATE FAIL: AffineLocal does not win clearly");
        return pass;
    }

    static double LocalMilliseconds<T>(Func<T> run)
    {
        run();
        double[] t = new double[5];
        for (int i = 0; i < t.Length; ++i) { var w = Stopwatch.StartNew(); run(); t[i] = w.Elapsed.TotalMilliseconds; }
        Array.Sort(t);
        return t[t.Length / 2];
    }

    static List<LocalFixture> LocalFixtures()
    {
        var list = new List<LocalFixture>();
        Vector3d[] planeTargets = { new(0, 0, 0), new(0.6, 0.3, 0), new(-0.5, 0.7, 0), new(0.15, -0.6, 0), new(-0.7, -0.4, 0), new(0.35, 0.8, 0) };
        Func<Vector3d, Vector3d> up = p => Vector3d.AxisZ;
        Func<Vector3d, Vector3d, Vector3d> planeExact = (p0, p) => p - p0;
        list.Add(new LocalFixture { Name = "plane", Mesh = LocalGrid(4, 41, 41, 0, 0), Normal = up, Exact = planeExact, Sources = planeTargets });
        list.Add(new LocalFixture { Name = "plane-noisy", Mesh = LocalGrid(4, 41, 41, 0.3 * 0.1, LocalSeed), Normal = up, Exact = planeExact, Sources = planeTargets });
        list.Add(new LocalFixture { Name = "plane-aniso", Mesh = LocalGrid(4, 21, 81, 0, 0), Normal = up, Exact = planeExact, Sources = planeTargets });

        Func<Vector3d, Vector3d> radial = p => p.Normalized;
        Func<Vector3d, Vector3d, Vector3d> sphereExact = (p0, p) => {
            Vector3d n = p0.Normalized, q = p.Normalized;
            double c = MathUtil.Clamp(q.Dot(n), -1, 1);
            Vector3d t = q - c * n;
            // the antipode (cut locus) has no single direction: only its distance pi matters, it is outside every patch
            if (t.Length < 1e-12) return c < 0 ? new Vector3d(Math.PI, 0, 0) : Vector3d.Zero;
            return Math.Acos(c) * t.Normalized;
        };
        Vector3d[] sphereTargets = { new(0, 0, 1), new(1, 1, 1), new(1, 0, 0), new(0.3, -1, 0.5), new(-1, 0.4, -0.6), new(0.2, 0.2, -1) };
        DMesh3 sphere = LocalCubeSphere(16);
        list.Add(new LocalFixture { Name = "sphere", Mesh = sphere, Normal = radial, Exact = sphereExact, Sources = sphereTargets });
        DMesh3 noisy = LocalCubeSphere(16);
        double hs = noisy.EdgeIndices().Average(e => { Index2i ev = noisy.GetEdgeV(e); return noisy.GetVertex(ev.a).Distance(noisy.GetVertex(ev.b)); });
        var rng = new Random(LocalSeed);
        foreach (int v in noisy.VertexIndices().ToList()) {
            Vector3d p = noisy.GetVertex(v), n = p.Normalized;
            Vector3d t = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
            t -= n * t.Dot(n);
            if (t.Length > 0) p = (p + t.Normalized * (0.3 * hs * rng.NextDouble())).Normalized;
            noisy.SetVertex(v, p);
        }
        list.Add(new LocalFixture { Name = "sphere-noisy", Mesh = noisy, Normal = radial, Exact = sphereExact, Sources = sphereTargets });
        list.Add(new LocalFixture { Name = "sphere-latlong", Mesh = LocalLatLong(20, 80), Normal = radial, Exact = sphereExact,
            Sources = new Vector3d[] { new(1, 0, 0), new(0.9, 0.4, 0.2), new(0.5, 0.5, 0.7), new(-0.7, 0.3, 0.5), new(0.1, -0.8, -0.4), new(-0.5, -0.5, -0.7) } });

        Vector3d[] cylTargets = { new(1, 0, 0), new(0, 1, 0.5), new(-1, 0, -0.6), new(0, -1, 0.2), new(0.7, 0.7, 0.9), new(-0.7, 0.7, -0.9) };
        Func<Vector3d, Vector3d> cylN = p => new Vector3d(p.x, p.y, 0).Normalized;
        Func<Vector3d, Vector3d, Vector3d> cylExact = (p0, p) => {
            double a = Math.Atan2(p.y, p.x) - Math.Atan2(p0.y, p0.x);
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a * new Vector3d(-p0.y, p0.x, 0) + (p.z - p0.z) * Vector3d.AxisZ;
        };
        list.Add(new LocalFixture { Name = "cylinder", Mesh = LocalCylinder(64, 33), Normal = cylN, Exact = cylExact, Sources = cylTargets });
        return list;
    }

    // nx x ny vertices over [-size/2, size/2]^2, alternating diagonals; interior vertices moved by up to
    // jitter per axis
    static DMesh3 LocalGrid(double size, int nx, int ny, double jitter, int seed)
    {
        var rng = new Random(seed);
        var mesh = new DMesh3();
        int[,] vid = new int[nx, ny];
        for (int j = 0; j < ny; ++j)
            for (int i = 0; i < nx; ++i) {
                double x = -size / 2 + size * i / (nx - 1), y = -size / 2 + size * j / (ny - 1);
                if (jitter > 0 && i > 0 && j > 0 && i < nx - 1 && j < ny - 1) {
                    x += jitter * (2 * rng.NextDouble() - 1);
                    y += jitter * (2 * rng.NextDouble() - 1);
                }
                vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, 0));
            }
        for (int j = 0; j + 1 < ny; ++j)
            for (int i = 0; i + 1 < nx; ++i) {
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

    // unit sphere from a cube with n x n cells per face (welded along the cube edges), outward oriented
    static DMesh3 LocalCubeSphere(int n)
    {
        var mesh = new DMesh3();
        var ids = new Dictionary<(int, int, int), int>();
        int Vid(int x, int y, int z)
        {
            if (!ids.TryGetValue((x, y, z), out int id)) {
                Vector3d p = new Vector3d(2.0 * x / n - 1, 2.0 * y / n - 1, 2.0 * z / n - 1).Normalized;
                ids[(x, y, z)] = id = mesh.AppendVertex(p);
            }
            return id;
        }
        for (int axis = 0; axis < 3; ++axis)
            foreach (int side in new[] { 0, n })
                for (int a = 0; a < n; ++a)
                    for (int b = 0; b < n; ++b) {
                        int[] c = new int[4];
                        // corners (a,b), (a+1,b), (a+1,b+1), (a,b+1) in the face's two other axes
                        var v = new int[4];
                        for (int k = 0; k < 4; ++k) {
                            int ua = a + (k == 1 || k == 2 ? 1 : 0), ub = b + (k >= 2 ? 1 : 0);
                            int[] xyz = new int[3];
                            xyz[axis] = side; xyz[(axis + 1) % 3] = ua; xyz[(axis + 2) % 3] = ub;
                            v[k] = Vid(xyz[0], xyz[1], xyz[2]);
                        }
                        // (axis+1, axis+2) is a right-handed pair about +axis: counterclockwise seen from +axis
                        bool flip = side == 0;
                        if (flip) { (v[1], v[3]) = (v[3], v[1]); }
                        if ((a + b) % 2 == 0) { mesh.AppendTriangle(v[0], v[1], v[2]); mesh.AppendTriangle(v[0], v[2], v[3]); }
                        else { mesh.AppendTriangle(v[0], v[1], v[3]); mesh.AppendTriangle(v[1], v[2], v[3]); }
                    }
        return mesh;
    }

    // unit sphere, rings latitude rings between the two pole vertices, lon vertices per ring
    static DMesh3 LocalLatLong(int rings, int lon)
    {
        var mesh = new DMesh3();
        int north = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int[,] vid = new int[rings - 1, lon];
        for (int j = 0; j < rings - 1; ++j) {
            double th = Math.PI * (j + 1) / rings;
            for (int i = 0; i < lon; ++i) {
                double a = 2 * Math.PI * i / lon;
                vid[j, i] = mesh.AppendVertex(new Vector3d(Math.Sin(th) * Math.Cos(a), Math.Sin(th) * Math.Sin(a), Math.Cos(th)));
            }
        }
        int south = mesh.AppendVertex(new Vector3d(0, 0, -1));
        for (int i = 0; i < lon; ++i) {
            int i1 = (i + 1) % lon;
            mesh.AppendTriangle(north, vid[0, i], vid[0, i1]);
            mesh.AppendTriangle(south, vid[rings - 2, i1], vid[rings - 2, i]);
            for (int j = 0; j + 1 < rings - 1; ++j) {
                mesh.AppendTriangle(vid[j, i], vid[j + 1, i1], vid[j, i1]);
                mesh.AppendTriangle(vid[j, i], vid[j + 1, i], vid[j + 1, i1]);
            }
        }
        return mesh;
    }

    // open cylinder of radius 1 and height 4
    static DMesh3 LocalCylinder(int around, int rows)
    {
        var mesh = new DMesh3();
        for (int j = 0; j < rows; ++j)
            for (int i = 0; i < around; ++i) {
                double a = 2 * Math.PI * i / around;
                mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), -2.0 + 4.0 * j / (rows - 1)));
            }
        for (int j = 0; j + 1 < rows; ++j)
            for (int i = 0; i < around; ++i) {
                int a = j * around + i, b = j * around + (i + 1) % around, c = a + around, d = b + around;
                mesh.AppendTriangle(a, b, d);
                mesh.AppendTriangle(a, d, c);
            }
        return mesh;
    }
}
