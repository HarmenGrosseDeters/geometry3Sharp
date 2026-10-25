// Comparison of mesh boolean implementations on the same inputs (see README.md).
//   export <mesh> <dir>                  A = the mesh, B = a rotated, shifted copy (as in MeshRepairBenchmark),
//                                        written as A.off / B.off with round-trip precision; info.json
//   pair <meshA> <meshB> <dir>           A and B as given (no transformation); A.off / B.off, info.json
//   synth <dir>                          synthetic hard cases (touching, coplanar, thin, large coordinates,
//                                        scale differences) and iterated sequences; writes <dir>/cases.jsonl
//   run <A.off> <B.off> <op> <out.off>   ExactMeshBoolean; prints {"ms":..} (boolean only, without I/O)
//   obb --request <r.json> --result <r.json> [--write-last-only]
//                                        runner for the Open Boolean Benchmark protocol (obb-runner/README.md)
//   eval <dir>                           metrics of all results <method>_<op>.(off|obj) in dir, one JSON line per method
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using g3;
using gs;

static class Program
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    static int Main(string[] args)
    {
        switch (args[0]) {
            case "export": return Export(args[1], args[2]);
            case "pair": return Pair(args[1], args[2], args[3]);
            case "synth": return Synthetic.Write(args[1]);
            case "run": return Run(args[1], args[2], args[3], args[4]);
            case "obb": return ObbRunner.Run(args.Skip(1).ToArray());
            case "eval": return Eval(args[1]);
        }
        Console.Error.WriteLine("unknown mode " + args[0]);
        return 1;
    }

    static DMesh3 Load(string meshFile)
    {
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(meshFile, ReadOptions.Defaults, builder);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0)
            return null;
        DMesh3 a = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i)
            MeshEditor.Append(a, builder.Meshes[i]);
        return a;
    }

    static int Export(string meshFile, string dir)
    {
        DMesh3 a = Load(meshFile);
        if (a == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        WriteCase(a, b, dir, Path.GetFileName(meshFile));
        return 0;
    }

    static int Pair(string fa, string fb, string dir)
    {
        DMesh3 a = Load(fa), b = Load(fb);
        if (a == null || b == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        WriteCase(a, b, dir, Path.GetFileName(fa) + "+" + Path.GetFileName(fb));
        return 0;
    }

    // A.off, B.off and info.json with the input properties of both meshes and the reference volumes
    internal static Dictionary<string, object> WriteCase(DMesh3 a, DMesh3 b, string dir, string name, Dictionary<string, object> extra = null)
    {
        Directory.CreateDirectory(dir);
        WriteOFF(a, Path.Combine(dir, "A.off"));
        WriteOFF(b, Path.Combine(dir, "B.off"));
        // reference volumes of the solids (self-intersections resolved exactly)
        DMesh3 ra = ExactMeshBoolean.Resolve(a), rb = ExactMeshBoolean.Resolve(b);
        int sa = SelfIntersections(a), sb = SelfIntersections(b);
        // volumes are measured relative to this point (eval uses it too): with coordinates far from the origin
        // (UTM), the sum of a·(b×c) over the triangles would cancel catastrophically
        Vector3d center = a.CachedBounds.Center;
        var info = new Dictionary<string, object> {
            ["file"] = name, ["tris"] = a.TriangleCount + b.TriangleCount,
            // properties of both inputs: the case is valid for CGAL only if both are closed and self-intersection free
            ["closed"] = a.IsClosed() && b.IsClosed(),
            ["wclosed"] = ExactMeshBoolean.IsWindingClosed(a) && ExactMeshBoolean.IsWindingClosed(b),
            ["selfint"] = sa < 0 || sb < 0 ? -1 : sa + sb,
            ["vA"] = ra != null ? Volume(ra, center) : double.NaN, ["vB"] = rb != null ? Volume(rb, center) : double.NaN,
            ["center"] = new[] { center.x, center.y, center.z },
        };
        foreach (var kv in extra ?? new Dictionary<string, object>())
            info[kv.Key] = kv.Value;
        File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(info));
        Console.WriteLine(JsonSerializer.Serialize(info));
        return info;
    }

    static int SelfIntersections(DMesh3 m)
    {
        return m.TriangleCount <= 300000 ? MeshRepairMetrics.SelfIntersections(m) : -1;
    }

    static int Run(string fa, string fb, string op, string fout)
    {
        DMesh3 a = StandardMeshReader.ReadMesh(fa), b = StandardMeshReader.ReadMesh(fb);
        var ops = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };
        // warm-up on a small problem, so that the time does not include the JIT compilation (the other
        // implementations are compiled ahead of time)
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        foreach (var o in ops.Values)
            new ExactMeshBoolean(w1, w2, o).Compute();
        int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
        TimeSpan pause0 = GC.GetTotalPauseDuration();
        var sw = Stopwatch.StartNew();
        var boolean = new ExactMeshBoolean(a, b, ops[op]);
        bool ok = boolean.Compute();
        long ms = sw.ElapsedMilliseconds;
        var gc = new Dictionary<string, object> { ["gen0"] = GC.CollectionCount(0) - gc0, ["gen2"] = GC.CollectionCount(2) - gc2,
            ["pause_ms"] = (long)(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds };
        if (ok)
            WriteOFF(boolean.Result, fout);
        var r = new Dictionary<string, object> { ["ms"] = ms, ["ok"] = ok, ["phases"] = boolean.PhaseMilliseconds, ["counts"] = boolean.Counts, ["gc"] = gc };
        if (!ok) r["error"] = boolean.Error;
        Console.WriteLine(JsonSerializer.Serialize(r));
        return 0;
    }

    internal static void WriteOFF(DMesh3 mesh, string path)
    {
        var sb = new StringBuilder();
        sb.Append("OFF\n").Append(mesh.VertexCount).Append(' ').Append(mesh.TriangleCount).Append(" 0\n");
        var map = new Dictionary<int, int>();
        foreach (int vid in mesh.VertexIndices()) {
            map[vid] = map.Count;
            Vector3d v = mesh.GetVertex(vid);
            sb.Append(v.x.ToString("R", ci)).Append(' ').Append(v.y.ToString("R", ci)).Append(' ').Append(v.z.ToString("R", ci)).Append('\n');
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            sb.Append("3 ").Append(map[t.a]).Append(' ').Append(map[t.b]).Append(' ').Append(map[t.c]).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    // triangle soup from OFF or OBJ (no welding beyond the file's own vertex indices)
    static bool ReadSoup(string path, out List<Vector3d> v, out List<Index3i> t)
    {
        v = new List<Vector3d>(); t = new List<Index3i>();
        if (File.Exists(path) == false)
            return false;
        string[] lines = File.ReadAllLines(path);
        if (path.EndsWith(".off")) {
            int k = 0;
            while (lines[k].Trim().Length == 0 || lines[k].StartsWith("#") || lines[k].Trim() == "OFF") k++;
            string[] counts = lines[k++].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int nv = int.Parse(counts[0]), nt = int.Parse(counts[1]);
            for (int i = 0; i < nv; ++i, ++k) {
                string[] s = lines[k].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                v.Add(new Vector3d(double.Parse(s[0], ci), double.Parse(s[1], ci), double.Parse(s[2], ci)));
            }
            for (int i = 0; i < nt; ++i, ++k) {
                string[] s = lines[k].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                int n = int.Parse(s[0]);
                for (int j = 2; j < n; ++j)
                    t.Add(new Index3i(int.Parse(s[1]), int.Parse(s[j]), int.Parse(s[j + 1])));
            }
        } else {
            foreach (string line in lines) {
                string[] s = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (s.Length == 0) continue;
                if (s[0] == "v")
                    v.Add(new Vector3d(double.Parse(s[1], ci), double.Parse(s[2], ci), double.Parse(s[3], ci)));
                else if (s[0] == "f") {
                    int[] f = s.Skip(1).Select(x => int.Parse(x.Split('/')[0]) - 1).ToArray();
                    for (int j = 1; j + 1 < f.Length; ++j)
                        t.Add(new Index3i(f[0], f[j], f[j + 1]));
                }
            }
        }
        return true;
    }

    internal static double Volume(DMesh3 m, Vector3d center)
    {
        double volume = 0;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a) - center, b = m.GetVertex(t.b) - center, c = m.GetVertex(t.c) - center;
            volume += a.Dot(b.Cross(c)) / 6.0;
        }
        return volume;
    }

    static int Eval(string dir)
    {
        var info = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(dir, "info.json")));
        double vA = info["vA"].GetDouble(), vB = info["vB"].GetDouble();
        Vector3d center = info.TryGetValue("center", out JsonElement ce)
            ? new Vector3d(ce[0].GetDouble(), ce[1].GetDouble(), ce[2].GetDouble()) : Vector3d.Zero;
        // the results to evaluate: the three operations, or the final result of a sequence
        string[] ops = info.TryGetValue("ops", out JsonElement o) ? o.EnumerateArray().Select(x => x.GetString()).ToArray()
            : new[] { "union", "intersection", "difference" };
        // analytically known volumes of the results (synthetic cases)
        var expected = info.TryGetValue("expected", out JsonElement ex)
            ? ex.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()) : null;
        string first = "_" + ops[0] + ".";
        var methods = Directory.GetFiles(dir).Select(Path.GetFileName)
            .Where(f => f.Contains(first)).Select(f => f.Substring(0, f.IndexOf(first))).Distinct();
        foreach (string method in methods) {
            var r = new Dictionary<string, object> { ["file"] = info["file"].GetString(), ["method"] = method, ["tris_in"] = info["tris"].GetInt32() };
            var vol = new Dictionary<string, double>();
            bool all = true;
            foreach (string op in ops) {
                string path = new[] { ".off", ".obj" }.Select(e => Path.Combine(dir, method + "_" + op + e)).FirstOrDefault(File.Exists);
                List<Vector3d> v; List<Index3i> t;
                if (path == null || ReadSoup(path, out v, out t) == false) { all = false; continue; }
                // weld by exact position, so that all methods are measured the same way
                var ids = new Dictionary<Vector3d, int>();
                int[] map = v.Select(p => { if (p.x == 0) p.x = 0; if (p.y == 0) p.y = 0; if (p.z == 0) p.z = 0;
                    if (ids.TryGetValue(p, out int id) == false) { id = ids.Count; ids[p] = id; } return id; }).ToArray();
                DMesh3 soup = new DMesh3();
                foreach (var kv in ids.OrderBy(kv => kv.Value)) soup.AppendVertex(kv.Key);
                var edgeUse = new Dictionary<long, int>();
                int degenerate = 0;
                var tris = new List<Index3i>();
                foreach (Index3i f in t) {
                    Index3i g = new Index3i(map[f.a], map[f.b], map[f.c]);
                    if (g.a == g.b || g.b == g.c || g.a == g.c) { degenerate++; continue; }
                    tris.Add(g);
                    for (int k = 0; k < 3; ++k) {
                        int x = g[k], y = g[(k + 1) % 3];
                        long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                        edgeUse[key] = edgeUse.TryGetValue(key, out int c) ? c + (x < y ? 1 : 1 << 16) : (x < y ? 1 : 1 << 16);
                    }
                }
                bool wclosed = edgeUse.Values.All(c => (c & 0xFFFF) == (c >> 16));
                bool manifold = edgeUse.Values.All(c => (c & 0xFFFF) == 1 && (c >> 16) == 1);
                DMesh3 mesh = new DMesh3();
                foreach (var kv in ids.OrderBy(kv => kv.Value)) mesh.AppendVertex(kv.Key);
                int dup = 0;
                foreach (Index3i g in tris) {
                    if (mesh.AppendTriangle(g) < 0) {
                        dup++;
                        mesh.AppendTriangle(mesh.AppendVertex(mesh.GetVertex(g.a)), mesh.AppendVertex(mesh.GetVertex(g.b)), mesh.AppendVertex(mesh.GetVertex(g.c)));
                    }
                }
                double volume = 0;
                int zeroArea = 0;
                foreach (Index3i g in tris) {
                    Vector3d a = soup.GetVertex(g.a) - center, b = soup.GetVertex(g.b) - center, c = soup.GetVertex(g.c) - center;
                    volume += a.Dot(b.Cross(c)) / 6.0;
                    Vector3d pa = soup.GetVertex(g.a);
                    if ((soup.GetVertex(g.b) - pa).Cross(soup.GetVertex(g.c) - pa).LengthSquared == 0) zeroArea++;
                }
                vol[op] = volume;
                r[op + "_tris"] = tris.Count;
                r[op + "_wclosed"] = wclosed;
                r[op + "_manifold_closed"] = manifold;
                r[op + "_degenerate"] = degenerate;
                r[op + "_zero_area"] = zeroArea;
                r[op + "_selfint"] = mesh.TriangleCount <= 400000 ? MeshRepairMetrics.SelfIntersections(mesh) : -1;
                r[op + "_volume"] = volume;
            }
            r["all_results"] = all;
            if (all && expected != null) {
                // relative to the input volume, so that empty expected results (touching solids) are measured too
                double scale = Math.Max(Math.Max(Math.Abs(vA) + Math.Abs(vB), expected.Values.Max(Math.Abs)), 1e-300);
                r["expected_error"] = expected.Max(kv => Math.Abs(vol[kv.Key] - kv.Value)) / scale;
            }
            if (all && ops.Length == 3 && !double.IsNaN(vA) && vA + vB > 0) {
                r["ie_error"] = Math.Abs(vol["union"] + vol["intersection"] - vA - vB) / (vA + vB);
                r["diff_error"] = Math.Abs(vol["difference"] - (vA - vol["intersection"])) / Math.Max(vA, 1e-300);
            }
            Console.WriteLine(JsonSerializer.Serialize(r));
        }
        return 0;
    }
}
