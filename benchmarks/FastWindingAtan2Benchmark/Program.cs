// Cost of the correctly rounded Atan2 in MathUtil.TriSolidAngle (#1577) on the operations that use the floating-point winding number.
// Run the same program on the base commit and on the candidate and compare the best-of-5 times (README.md).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

static DMesh3 Soup(DMesh3 clean, double jitterRel, int flipEvery, int holeEvery, int dupEvery, int seed)
{
    var rnd = new Random(seed);
    double size = clean.GetBounds().DiagonalLength;
    var soup = new DMesh3();
    int k = 0;
    foreach (int t in clean.TriangleIndices()) {
        ++k;
        if (holeEvery > 0 && k % holeEvery == 0) continue;
        Index3i tri = clean.GetTriangle(t);
        Vector3d[] v = new Vector3d[3];
        for (int i = 0; i < 3; ++i) {
            v[i] = clean.GetVertex(tri[i]);
            if (jitterRel > 0) v[i] += jitterRel * size * new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
        }
        bool flip = flipEvery > 0 && k % flipEvery == 0;
        for (int copy = 0; copy < (dupEvery > 0 && k % dupEvery == 0 ? 2 : 1); ++copy) {
            int a = soup.AppendVertex(v[0]), b = soup.AppendVertex(v[1]), c = soup.AppendVertex(v[2]);
            soup.AppendTriangle(a, flip ? c : b, flip ? b : c);
        }
    }
    return soup;
}
static DMesh3 Sphere(int edge, double r = 0.5) => new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = edge }.Generate().MakeDMesh();
static DMesh3 Box(int edge) => new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.4, 0.3)), EdgeVertices = edge }.Generate().MakeDMesh();

static Func<string> RepairCase(DMesh3 input)
{
    return delegate {
        MeshRobustRepair repair = new MeshRobustRepair(new DMesh3(input));
        repair.Apply();
        return repair.ResultPath.ToString() + " " + (repair.Mesh != null ? repair.Mesh.TriangleCount : 0) + " tris";
    };
}

static double Best(Func<string> run, int repeats, out string note)
{
    double best = double.MaxValue; note = "";
    for (int r = 0; r < repeats; ++r) {
        var sw = Stopwatch.StartNew(); note = run(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    return best;
}

int repeats = args.Length > 0 ? int.Parse(args[0]) : 5;
var cases = new List<(string, Func<string>)>();
DMesh3 s1 = Soup(Sphere(10), 1e-3, 0, 0, 0, 1);
DMesh3 s2 = Soup(Sphere(10), 0, 3, 11, 7, 2);
DMesh3 s3 = Soup(Box(6), 0, 3, 13, 5, 3);
foreach (var (n, m) in new[] { ("MeshRobustRepair sphere soup jitter 1e-3", s1), ("MeshRobustRepair sphere flip/holes/dups", s2), ("MeshRobustRepair box flip/holes/dups", s3) })
    cases.Add((n, RepairCase(m)));
DMesh3 big = Soup(Sphere(65, 1.0), 1e-4, 0, 0, 0, 4);       // ~50k triangles
cases.Add(("MeshWindingNumberSolidify 50k soup (" + big.TriangleCount + " tris)", () => { var s = new MeshWindingNumberSolidify(new DMesh3(big)) { Adaptive = true, GridResolution = 64 }; s.Compute(); return (s.Result?.TriangleCount ?? 0) + " tris"; }));
DMesh3 sa = Sphere(24, 0.5), sb = Sphere(24, 0.5); MeshTransforms.Translate(sb, new Vector3d(0.4, 0.1, 0.05));
foreach (var (n, op) in new[] { ("MeshWindingNumberBoolean union, two overlapping spheres", MeshWindingNumberBoolean.Operations.Union), ("MeshWindingNumberBoolean intersection, two overlapping spheres", MeshWindingNumberBoolean.Operations.Intersection) })
    cases.Add((n, () => { var b = new MeshWindingNumberBoolean(new DMesh3(sa), new DMesh3(sb), op) { GridResolution = 64 }; b.Compute(); return (b.Result?.TriangleCount ?? 0) + " tris"; }));

Console.WriteLine("best of " + repeats + " (ms)");
foreach (var (name, run) in cases) {
    run();   // warm up
    double ms = Best(run, repeats, out string note);
    Console.WriteLine($"{name,-70} {ms,9:F1}  {note}");
}
