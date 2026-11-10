// D1 reference rule cohort run: the three operations of one #385 case (A.off, B.off from BooleanComparison export),
// each on its own instance, with SafeRounding's drift fields and a hash of the result. One JSON line per operation.
//   D1Runner <case dir> <id>
// Compiles against both measured builds (master with the apex rule, the PR with the reference rule): only public
// fields that both have.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using g3;
using gs;

static class Program
{
    static DMesh3 Load(string path)
    {
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult r = new StandardMeshReader() { MeshBuilder = builder }.Read(path, ReadOptions.Defaults);
        if (r.code != IOCode.Ok || builder.Meshes.Count == 0)
            throw new IOException("load failed: " + path + " " + r.message);
        DMesh3 m = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i)
            MeshEditor.Append(m, builder.Meshes[i]);
        return m;
    }

    // SHA-256 over the vertices (bit patterns, in id order) and the triangles (in id order): equal hashes mean the
    // same mesh bit for bit
    static string Hash(DMesh3 m)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            w.Write(v); w.Write(BitConverter.DoubleToInt64Bits(p.x)); w.Write(BitConverter.DoubleToInt64Bits(p.y)); w.Write(BitConverter.DoubleToInt64Bits(p.z));
        }
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            w.Write(t); w.Write(tv.a); w.Write(tv.b); w.Write(tv.c);
        }
        w.Flush();
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    // (JSON has no infinity: as a string)
    static object Num(double x) => double.IsFinite(x) ? x : x.ToString(CultureInfo.InvariantCulture);

    static void WarmUp()
    {
        var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = 1 };
        DMesh3 a = g.Generate().MakeDMesh();
        DMesh3 b = new DMesh3(a);
        MeshTransforms.Translate(b, new Vector3d(0.5, 0.3, 0.2));
        foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
            var w = new ExactMeshBoolean(a, b, op);
            w.Compute();
            if (w.Result != null) MeshRobustRepair.NotCleanReason(w.Result);
        }
    }

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = args[0], id = args[1];
        DMesh3 A = Load(Path.Combine(dir, "A.off")), B = Load(Path.Combine(dir, "B.off"));
        WarmUp();
        foreach (var (name, op) in new[] { ("union", ExactMeshBoolean.Operations.Union), ("intersection", ExactMeshBoolean.Operations.Intersection),
            ("difference", ExactMeshBoolean.Operations.Difference) }) {
            var row = new Dictionary<string, object> { ["id"] = id, ["op"] = name, ["cores"] = Environment.ProcessorCount };
            try {
                var b = new ExactMeshBoolean(A, B, op);
                var sw = Stopwatch.StartNew();
                bool ok = b.Compute();
                sw.Stop();
                row["ms"] = sw.Elapsed.TotalMilliseconds;
                row["ok"] = ok;
                row["status"] = b.Status.ToString();
                row["rounding_ms"] = b.PhaseMilliseconds.TryGetValue("rounding", out long rms) ? rms : -1;
                row["rounding_clean"] = b.RoundingClean;
                MeshRoundingRepair r = b.Rounding;
                if (r != null) {
                    row["max_length"] = Num(r.MaxLength);
                    row["max_drift"] = Num(r.MaxDrift);
                    row["drift_ratio"] = r.MaxLength > 0 ? Num(r.MaxDrift / r.MaxLength) : null;
                    row["smallest_rejected_ratio"] = r.MaxLength > 0 ? Num(r.SmallestRejectedDrift / r.MaxLength) : null;
                    row["limit"] = r.EvaluationLimitReached;
                    row["remaining_degenerate"] = r.RemainingDegenerate;
                    row["remaining_intersections"] = r.RemainingIntersections;
                    row["collapses"] = r.Collapses; row["flips"] = r.Flips; row["moves"] = r.Moves; row["zips"] = r.Zips;
                    row["strip_zips"] = r.StripZips; row["vertex_removals"] = r.VertexRemovals;
                }
                if (ok && b.Result != null) {
                    row["triangles"] = b.Result.TriangleCount;
                    row["hash"] = Hash(b.Result);
                    row["reason"] = MeshRobustRepair.NotCleanReason(b.Result);
                }
            } catch (Exception e) {
                row["exception"] = e.GetType().Name + ": " + e.Message;
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }
}
