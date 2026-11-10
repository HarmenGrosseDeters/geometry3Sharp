using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

// Time and result hash of MeshWindingNumberSolidify (adaptive dual contouring) on procedural inputs, so a
// speedup can be checked to give the same mesh bit for bit. Usage: SolidifyBenchmark [repeats]
// (the first run of each case is cold and reported separately from the median of the others).
static class Program
{
    static DMesh3 ThinPlates(int seed)
    {
        var rnd = new Random(seed);
        DMesh3 m = new DMesh3();
        for (int i = 0; i < 12; ++i) {
            var c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
            var e = new Vector3d(0.3 + 0.2 * rnd.NextDouble(), 0.02 + 0.03 * rnd.NextDouble(), 0.3);
            var b = new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh();
            var axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
            MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(axis, 360 * rnd.NextDouble()));
            MeshEditor.Append(m, b);
        }
        return m;
    }

    static DMesh3 Box(Vector3d min, Vector3d max)
    {
        return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
    }

    // two boxes with a gap (as HiddenGapRefinementTests), rotated off the grid axes
    static DMesh3 BoxesWithGap(double gap, double tiltDeg)
    {
        DMesh3 a = Box(new Vector3d(-2.5, -1.25, -2.5), new Vector3d(2.5, 1.25, 2.5));
        DMesh3 b = Box(new Vector3d(-2.5, 1.25 + gap, -2.5), new Vector3d(2.5, 3.75 + gap, 2.5));
        MeshTransforms.Rotate(b, new Vector3d(0, 1.25 + gap, 0), Quaterniond.AxisAngleD(Vector3d.AxisX, tiltDeg));
        MeshEditor.Append(a, b);
        MeshTransforms.Rotate(a, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27));
        return a;
    }

    static string Hash(DMesh3 m)
    {
        if (m == null) return "null";
        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        w.Write(m.VertexCount); w.Write(m.TriangleCount);
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            w.Write(BitConverter.DoubleToInt64Bits(p.x)); w.Write(BitConverter.DoubleToInt64Bits(p.y)); w.Write(BitConverter.DoubleToInt64Bits(p.z));
        }
        foreach (int t in m.TriangleIndices()) { Index3i q = m.GetTriangle(t); w.Write(q.a); w.Write(q.b); w.Write(q.c); }
        w.Flush();
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").Substring(0, 16);
    }

    static int Main(string[] args)
    {
        int repeats = args.Length > 0 ? int.Parse(args[0]) : 4;
        DMesh3 plates = ThinPlates(6), plates3 = ThinPlates(3);
        DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
        var cases = new List<KeyValuePair<string, Func<MeshWindingNumberSolidify>>> {
            new("thin plates, res 64", () => new MeshWindingNumberSolidify(plates) { Adaptive = true, GridResolution = 64 }),
            new("thin plates, res 64, no reduce", () => new MeshWindingNumberSolidify(plates) { Adaptive = true, GridResolution = 64, ReduceTolerance = 0 }),
            new("thin plates, res 64, stop on self-int.", () => new MeshWindingNumberSolidify(plates) { Adaptive = true, GridResolution = 64, StopIfSelfIntersecting = true }),
            new("thin plates (seed 3), res 48", () => new MeshWindingNumberSolidify(plates3) { Adaptive = true, GridResolution = 48 }),
            new("gap 0.3 cells, 3 levels", () => new MeshWindingNumberSolidify(BoxesWithGap(0.3 * 0.25, 0)) { CellSize = 0.25, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 3 }),
            new("gap 0.45 cells, tilted 0.5 deg", () => new MeshWindingNumberSolidify(BoxesWithGap(0.45 * 0.25, 0.5)) { CellSize = 0.25, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 3 }),
            new("sphere, res 40", () => new MeshWindingNumberSolidify(sphere) { Adaptive = true, GridResolution = 40 }),
        };
        Console.WriteLine("| case | result triangles | result hash | cold ms | median warm ms | octree.fine ms (median) |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var c in cases) {
            var warm = new List<long>(); var fine = new List<long>();
            long cold = 0; string hash = null; int tris = 0;
            for (int r = 0; r < repeats; ++r) {
                var s = c.Value();
                var sw = Stopwatch.StartNew();
                bool ok = s.Compute();
                long ms = sw.ElapsedMilliseconds;
                string h = ok ? Hash(s.Result) : "failed";
                if (hash != null && h != hash) { Console.Error.WriteLine("RESULT DIFFERS between repeats in " + c.Key); return 1; }
                hash = h; tris = s.Result == null ? 0 : s.Result.TriangleCount;
                if (r == 0) cold = ms; else {
                    warm.Add(ms);
                    long f; fine.Add(s.PhaseMilliseconds.TryGetValue("contour.octree.fine", out f) ? f : 0);
                }
            }
            warm.Sort(); fine.Sort();
            Console.WriteLine($"| {c.Key} | {tris} | `{hash}` | {cold} | {(warm.Count > 0 ? warm[warm.Count / 2] : 0)} | {(fine.Count > 0 ? fine[fine.Count / 2] : 0)} |");
        }
        return 0;
    }
}
