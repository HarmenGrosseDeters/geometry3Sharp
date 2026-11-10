using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

// Time and result hash of Remesher with PreventSelfIntersections (20 passes as g3cli remesh) on procedural thin
// tubes, so a speedup can be checked to give the same mesh bit for bit. Usage: RemeshPreventBenchmark [repeats]
static class Program
{
    // a tube of radius R, wall thickness w, height H (as RemeshSizingTests.Tube)
    static DMesh3 Tube(int slices, int rings, double R, double w, double H)
    {
        var m = new DMesh3();
        int[,] o = new int[rings + 1, slices], i = new int[rings + 1, slices];
        for (int r = 0; r <= rings; ++r)
            for (int s = 0; s < slices; ++s) {
                double a = 2 * Math.PI * s / slices, z = H * r / rings;
                o[r, s] = m.AppendVertex(new Vector3d(R * Math.Cos(a), R * Math.Sin(a), z));
                i[r, s] = m.AppendVertex(new Vector3d((R - w) * Math.Cos(a), (R - w) * Math.Sin(a), z));
            }
        for (int s = 0; s < slices; ++s) {
            int s1 = (s + 1) % slices;
            for (int r = 0; r < rings; ++r) {
                m.AppendTriangle(o[r, s], o[r, s1], o[r + 1, s1]); m.AppendTriangle(o[r, s], o[r + 1, s1], o[r + 1, s]);
                m.AppendTriangle(i[r, s], i[r + 1, s1], i[r, s1]); m.AppendTriangle(i[r, s], i[r + 1, s], i[r + 1, s1]);
            }
            m.AppendTriangle(o[rings, s], o[rings, s1], i[rings, s1]); m.AppendTriangle(o[rings, s], i[rings, s1], i[rings, s]);
            m.AppendTriangle(o[0, s], i[0, s1], o[0, s1]); m.AppendTriangle(o[0, s], i[0, s], i[0, s1]);
        }
        return m;
    }

    static string Hash(DMesh3 m)
    {
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

    static DMesh3 Remesh(DMesh3 input, double target, Func<Vector3d, double> field, bool prevent)
    {
        DMesh3 mesh = new DMesh3(input);
        Remesher r = new Remesher(mesh);
        r.SmoothSpeedT = 0.5; r.MinTriangleQuality = 0.2; r.PreventSelfIntersections = prevent;
        r.EnableParallelSmooth = r.EnableParallelProjection = true;
        r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
        r.SetTargetEdgeLength(target);
        r.TargetEdgeLengthF = field;
        for (int k = 0; k < 20; ++k)
            r.BasicRemeshPass();
        return mesh;
    }

    static int Main(string[] args)
    {
        int repeats = args.Length > 0 ? int.Parse(args[0]) : 4;
        DMesh3 thin = Tube(16, 4, 1, 0.1, 3), fat = Tube(24, 6, 1, 0.5, 3);
        var cases = new List<KeyValuePair<string, Func<DMesh3>>> {
            new("thin tube (wall 0.1), wall-thickness sizing, target 0.8", () => Remesh(thin, 0.8, RemeshSizing.FromWallThickness(thin, 1.0), true)),
            new("thin tube (wall 0.1), uniform target 0.2", () => Remesh(thin, 0.2, null, true)),
            new("thick tube (wall 0.5), uniform target 0.3", () => Remesh(fat, 0.3, null, true)),
            new("thick tube (wall 0.5), uniform target 1.5 (coarsening)", () => Remesh(fat, 1.5, null, true)),
            new("thin tube, uniform target 0.2, check off", () => Remesh(thin, 0.2, null, false)),
        };
        Console.WriteLine("| case | result triangles | result hash | cold ms | median warm ms |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var c in cases) {
            var warm = new List<long>();
            long cold = 0; string hash = null; int tris = 0;
            for (int r = 0; r < repeats; ++r) {
                var sw = Stopwatch.StartNew();
                DMesh3 m = c.Value();
                long ms = sw.ElapsedMilliseconds;
                string h = Hash(m);
                if (hash != null && h != hash) { Console.Error.WriteLine("RESULT DIFFERS between repeats in " + c.Key); return 1; }
                hash = h; tris = m.TriangleCount;
                if (r == 0) cold = ms; else warm.Add(ms);
            }
            warm.Sort();
            Console.WriteLine($"| {c.Key} | {tris} | `{hash}` | {cold} | {(warm.Count > 0 ? warm[warm.Count / 2] : 0)} |");
        }
        return 0;
    }
}
