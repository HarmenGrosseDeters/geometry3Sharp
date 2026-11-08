// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
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
    static bool AffineLogMapProbe(LogMapStrategy strategy, string pointPath, double affineTime)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var points = pointPath == null ? TextWriter.Null : new StreamWriter(pointPath);
        points.WriteLine("mesh,vertex,radius,band,legacy_error,affine_error,regression");
        Console.WriteLine($"{strategy} gate: affine TimeFactor={affineTime:R}, legacy TimeFactor=1; otherwise default options; smooth unit sphere/cylinder, exact planar offsets.");
        Console.WriteLine("Gauge: projected signpost-zero direction, right-handed smooth tangent frame; no fitted rotation.");
        Console.WriteLine("All vertices including boundaries; only source and exactly undefined cut-locus directions omitted.");
        Console.WriteLine("Regressions count affine_error > legacy_error; significant uses absolute 1e-10 roundoff allowance.");
        Console.WriteLine("mesh,band_radius,count,legacy_mean,legacy_max,affine_mean,affine_max,regressions,significant,worst_vertex,worst_delta");
        bool passed = true;
        foreach (string name in new[] { "plane", "sphere", "cylinder" }) {
            DMesh3 mesh = name == "plane" ? Grid(4, 21) : name == "sphere"
                ? new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 17 }.Generate().MakeDMesh()
                : AffineCylinder(32, 17);
            Vector3d target = name == "plane" ? new Vector3d(2, 2, 0) : name == "sphere" ? Vector3d.AxisZ : Vector3d.AxisX;
            int source = Nearest(mesh, target);
            Vector3d p0 = mesh.GetVertex(source), normal = name == "plane" ? Vector3d.AxisZ : name == "sphere" ? p0.Normalized : new Vector3d(p0.x, p0.y, 0).Normalized;
            var baseline = new VectorHeatMethod(mesh);
            var method = new VectorHeatMethod(mesh) { TimeFactor = affineTime };
            Vector2d[] before = baseline.ComputeLogMap(source);
            Vector2d[] after = method.ComputeLogMap(source, strategy);
            Vector3d e1 = method.Triangulation.ToWorld(source, Vector2d.AxisX);
            e1 = (e1 - normal * e1.Dot(normal)).Normalized;
            Vector3d e2 = normal.Cross(e1);
            double maxRadius = name == "plane" ? Math.Sqrt(8) : name == "sphere" ? Math.PI : Math.Sqrt(Math.PI * Math.PI + 4);
            var bands = Enumerable.Range(0, 4).Select(_ => new List<(int id, double oldError, double newError)>()).ToArray();
            int undefined = 0, nonfinite = 0;
            foreach (int v in mesh.VertexIndices()) {
                if (v == source) continue;
                Vector3d p = mesh.GetVertex(v), exact;
                if (name == "sphere") {
                    double cosine = MathUtil.Clamp(p.Normalized.Dot(normal), -1, 1);
                    Vector3d tangent = p.Normalized - cosine * normal;
                    if (cosine < 0 && tangent.Length < 1e-14) { undefined++; continue; }
                    exact = Math.Acos(cosine) * tangent.Normalized;
                } else if (name == "cylinder") {
                    double angle = Math.Atan2(p.y, p.x) - Math.Atan2(p0.y, p0.x);
                    while (angle > Math.PI) angle -= 2 * Math.PI;
                    while (angle < -Math.PI) angle += 2 * Math.PI;
                    if (Math.Abs(Math.Abs(angle) - Math.PI) < 1e-14) { undefined++; continue; }
                    exact = angle * new Vector3d(-p0.y, p0.x, 0) + (p.z - p0.z) * Vector3d.AxisZ;
                } else exact = p - p0;
                Vector2d oracle = new Vector2d(exact.Dot(e1), exact.Dot(e2));
                double oldError = before[v].Distance(oracle), newError = after[v].Distance(oracle);
                if (!double.IsFinite(oldError) || !double.IsFinite(newError)) { nonfinite++; passed = false; continue; }
                int band = Math.Min(3, (int)(4 * exact.Length / maxRadius));
                bands[band].Add((v, oldError, newError));
                points.WriteLine($"{name},{v},{exact.Length:R},{band},{oldError:R},{newError:R},{newError > oldError}");
            }
            for (int band = 0; band < bands.Length; ++band) {
                var values = bands[band];
                int regressions = values.Count(v => v.newError > v.oldError);
                int significant = values.Count(v => v.newError > v.oldError + 1e-10);
                var worst = values.OrderByDescending(v => v.newError - v.oldError).First();
                Console.WriteLine($"{name},[{band * maxRadius / 4:R};{(band + 1) * maxRadius / 4:R}],{values.Count},{values.Average(v => v.oldError):R},{values.Max(v => v.oldError):R},{values.Average(v => v.newError):R},{values.Max(v => v.newError):R},{regressions},{significant},{worst.id},{worst.newError - worst.oldError:R}");
                if (significant > 0) passed = false;
                if (name == "sphere" && band == 3 && (values.Average(v => v.newError) >= values.Average(v => v.oldError)
                    || values.Max(v => v.newError) >= values.Max(v => v.oldError))) passed = false;
            }
            Console.WriteLine($"{name}: undefined={undefined}, nonfinite={nonfinite}; warm query median ms: legacy={AffineMilliseconds(() => baseline.ComputeLogMap(source)):R}, affine={AffineMilliseconds(() => method.ComputeLogMap(source, strategy)):R}");
        }
        Console.WriteLine(passed ? "ACCEPTANCE PASS" : "ACCEPTANCE FAIL: stop; do not expand scope or weaken acceptance without owner approval.");
        return passed;
    }

    static double AffineMilliseconds(Func<Vector2d[]> run)
    {
        run();
        double[] times = new double[5];
        for (int i = 0; i < times.Length; ++i) {
            var watch = Stopwatch.StartNew();
            run();
            times[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        return times[times.Length / 2];
    }

    // Procedural meshes are exported only to the caller's scratch directory, never committed as assets.
    static void AffineReferenceExport(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string name in new[] { "plane", "sphere", "cylinder" }) {
            DMesh3 mesh = name == "plane" ? Grid(4, 21) : name == "sphere"
                ? new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 17 }.Generate().MakeDMesh()
                : AffineCylinder(32, 17);
            Vector3d target = name == "plane" ? new Vector3d(2, 2, 0) : name == "sphere" ? Vector3d.AxisZ : Vector3d.AxisX;
            int source = Nearest(mesh, target);
            foreach (bool intrinsic in new[] { false, true }) {
                var solver = new VectorHeatMethod(mesh) { IntrinsicDelaunay = intrinsic };
                var maps = new Dictionary<string, double[][]>();
                foreach (LogMapStrategy strategy in Enum.GetValues<LogMapStrategy>())
                    maps[strategy.ToString()] = solver.ComputeLogMap(source, strategy).Select(v => new[] { v.x, v.y }).ToArray();
                Vector3d gauge = solver.Triangulation.ToWorld(source, Vector2d.AxisX);
                var data = new {
                    name, source, intrinsic,
                    vertices = mesh.VertexIndices().Select(v => { Vector3d p = mesh.GetVertex(v); return new[] { p.x, p.y, p.z }; }).ToArray(),
                    faces = mesh.TriangleIndices().Select(t => { Index3i f = mesh.GetTriangle(t); return new[] { f.a, f.b, f.c }; }).ToArray(),
                    gauge = new[] { gauge.x, gauge.y, gauge.z },
                    diffusionTime = solver.DiffusionTime,
                    maps
                };
                string path = Path.Combine(directory, $"{name}-{(intrinsic ? "intrinsic" : "input")}.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions {
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                }));
                Console.WriteLine(path);
            }
        }
    }

    static DMesh3 AffineCylinder(int around, int rows)
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
