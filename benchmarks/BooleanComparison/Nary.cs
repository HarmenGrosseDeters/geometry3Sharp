// Booleans of many meshes: one ExactMeshBoolean over all inputs (one arrangement, a winding number predicate)
// against the chain of pairwise booleans, on the iterated cube grid of the sequence class and on random spheres.
//   nary grid <n> [chained|onepass|twostep]...    n^3 cubes united, minus a void in each (2 n^3 - 1 booleans)
//   nary spheres <k> [chained|onepass]...          union of k random overlapping spheres (k - 1 booleans)
// One JSON line per variant: time (booleans only), triangles, vertices, volume error, closed, self-intersections.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class Nary
{
    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var m = new DMesh3();
        for (int i = 0; i < 8; ++i)
            m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
        int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
        foreach (int[] q in quads) {
            m.AppendTriangle(q[0], q[1], q[2]);
            m.AppendTriangle(q[0], q[2], q[3]);
        }
        return m;
    }

    static double Volume(DMesh3 m)
    {
        return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;
    }

    static DMesh3 Pairwise(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
    {
        var boolean = new ExactMeshBoolean(a, b, op);
        if (boolean.Compute() == false)
            throw new Exception(boolean.Error);
        return boolean.Result;
    }

    static DMesh3 OnePass(List<DMesh3> meshes, Func<int[], bool> predicate, Dictionary<string, object> info)
    {
        var boolean = new ExactMeshBoolean(meshes, predicate);
        if (boolean.Compute() == false)
            throw new Exception(boolean.Error);
        info["phases_ms"] = boolean.PhaseMilliseconds;
        info["patches"] = boolean.Patches;
        info["intersecting_pairs"] = boolean.IntersectingPairs;
        return boolean.Result;
    }

    public static int Run(string[] args)
    {
        string kind = args[0];
        int n = int.Parse(args[1]);
        var variants = args.Skip(2).ToList();
        if (variants.Count == 0)
            variants = (kind == "grid") ? new List<string> { "chained", "onepass", "twostep" } : new List<string> { "chained", "onepass" };
        if ((kind != "grid" && kind != "spheres") || n < 1) {
            Console.Error.WriteLine("usage: nary grid|spheres <n >= 1> [variant]...");
            return 1;
        }
        foreach (string v in variants)
            if (v != "chained" && v != "onepass" && v != "twostep") {
                Console.Error.WriteLine("unknown variant " + v);
                return 1;
            }
        // Warm-up, not timed: the requested variants on a small case of the same kind, repeated for 3 s. A cold
        // run pays about 1 s, mostly for JIT compilation (tiered: optimized code only after enough calls or loop
        // iterations), partly for growing the GC heap (grid 5 onepass: 2.8-3.6 s cold, 1.6-1.9 s warm); before,
        // the time of a variant depended on whether another variant ran before it in the same process.
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 3000) {
            int code = Measure(kind, Math.Min(n, kind == "grid" ? 3 : 5), variants, false);
            if (code != 0)
                return code;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Measure(kind, n, variants, true);
    }

    // the inputs of a case: cubes (or spheres), voids (empty for spheres), the predicate of the one-pass boolean and
    // the expected volume (NaN if not known)
    internal static void Inputs(string kind, int n, out List<DMesh3> cubes, out List<DMesh3> voids, out Func<int[], bool> predicate, out double expected)
    {
        cubes = new List<DMesh3>();
        voids = new List<DMesh3>();
        if (kind == "grid") {
            for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) for (int k = 0; k < n; ++k) {
                cubes.Add(Box(0.9 * i, 0.9 * j, 0.9 * k, 0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1));
                voids.Add(Box(0.9 * i + 0.25, 0.9 * j + 0.25, 0.9 * k + 0.25, 0.9 * i + 0.75, 0.9 * j + 0.75, 0.9 * k + 0.75));
            }
            double side = 0.9 * (n - 1) + 1;
            expected = side * side * side - cubes.Count * 0.125;
        } else {
            var rand = new Random(1);
            for (int k = 0; k < n; ++k) {
                DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 0.5 + 0.5 * rand.NextDouble(), EdgeVertices = 16 }.Generate().MakeDMesh();
                MeshTransforms.Translate(s, new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * Math.Cbrt(n));
                cubes.Add(s);
            }
            expected = double.NaN;
        }
        int nc = cubes.Count;
        predicate = (w) => {
            bool inCube = false;
            for (int m = 0; m < nc && inCube == false; ++m)
                inCube = w[m] > 0;
            if (inCube == false)
                return false;
            for (int m = nc; m < w.Length; ++m)
                if (w[m] > 0)
                    return false;
            return true;
        };
    }

    static int Measure(string kind, int n, List<string> variants, bool print)
    {
        List<DMesh3> cubes, voids;
        Func<int[], bool> predicate;
        double expected;
        Inputs(kind, n, out cubes, out voids, out predicate, out expected);
        int nc = cubes.Count;
        var all = cubes.Concat(voids).ToList();

        foreach (string variant in variants) {
            var info = new Dictionary<string, object> { ["case"] = $"{kind}_{n}", ["variant"] = variant, ["inputs"] = all.Count };
            var sw = Stopwatch.StartNew();
            DMesh3 r;
            if (variant == "chained") {
                r = cubes[0];
                for (int c = 1; c < nc; ++c)
                    r = Pairwise(r, cubes[c], ExactMeshBoolean.Operations.Union);
                foreach (DMesh3 v in voids)
                    r = Pairwise(r, v, ExactMeshBoolean.Operations.Difference);
                info["booleans"] = all.Count - 1;
            } else if (variant == "onepass") {
                r = OnePass(all, predicate, info);
                info["booleans"] = 1;
            } else if (variant == "twostep") {
                // n-ary union of the cubes, then the union minus all voids (n-ary difference)
                var union = new ExactMeshBoolean(cubes, ExactMeshBoolean.Operations.Union);
                if (union.Compute() == false) throw new Exception(union.Error);
                var diff = new ExactMeshBoolean(new[] { union.Result }.Concat(voids), ExactMeshBoolean.Operations.Difference);
                if (diff.Compute() == false) throw new Exception(diff.Error);
                r = diff.Result;
                info["booleans"] = 2;
            } else {
                Console.Error.WriteLine("unknown variant " + variant);
                return 1;
            }
            info["ms"] = sw.ElapsedMilliseconds;
            if (print == false)
                continue;
            info["triangles"] = r.TriangleCount;
            info["vertices"] = r.VertexCount;
            double vol = Volume(r);
            info["volume"] = vol;
            if (double.IsNaN(expected) == false)
                info["volume_error"] = Math.Abs(vol - expected) / expected;
            info["closed"] = r.IsClosed();
            info["self_intersections"] = MeshRepairMetrics.SelfIntersections(r);
            Console.WriteLine(JsonSerializer.Serialize(info));
        }
        return 0;
    }
}
