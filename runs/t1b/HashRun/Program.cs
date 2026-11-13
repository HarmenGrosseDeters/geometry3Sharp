// per op: rounding ms, op ms, result hash (SHA-256 over vertex bits and triangles in id order); args: case dirs
using System; using System.Collections.Generic; using System.Diagnostics; using System.IO; using System.Security.Cryptography; using System.Text.Json; using g3; using gs;
static class P {
  static DMesh3 Load(string path) { var b = new DMesh3Builder(); new StandardMeshReader() { MeshBuilder = b }.Read(path, ReadOptions.Defaults); var m = b.Meshes[0]; for (int i = 1; i < b.Meshes.Count; ++i) MeshEditor.Append(m, b.Meshes[i]); return m; }
  static string Hash(DMesh3 m) { using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
    foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); w.Write(v); w.Write(BitConverter.DoubleToInt64Bits(p.x)); w.Write(BitConverter.DoubleToInt64Bits(p.y)); w.Write(BitConverter.DoubleToInt64Bits(p.z)); }
    foreach (int t in m.TriangleIndices()) { var tv = m.GetTriangle(t); w.Write(t); w.Write(tv.a); w.Write(tv.b); w.Write(tv.c); }
    w.Flush(); return Convert.ToHexString(SHA256.HashData(ms.ToArray())); }
  static void Main(string[] a) {
    var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = 1 }; DMesh3 wa = g.Generate().MakeDMesh(); DMesh3 wb = new DMesh3(wa); MeshTransforms.Translate(wb, new Vector3d(0.5, 0.3, 0.2));
    foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Difference }) new ExactMeshBoolean(wa, wb, op).Compute();
    int reps = int.Parse(Environment.GetEnvironmentVariable("REPS") ?? "1");
    foreach (string dir in a) {
      string id = Path.GetFileName(dir.TrimEnd('/'));
      DMesh3 A = Load(Path.Combine(dir, "A.off")), B = Load(Path.Combine(dir, "B.off"));
      foreach (var (name, op) in new[] { ("union", ExactMeshBoolean.Operations.Union), ("intersection", ExactMeshBoolean.Operations.Intersection), ("difference", ExactMeshBoolean.Operations.Difference) }) {
        var rms = new List<long>(); var oms = new List<double>(); string h = null;
        for (int r = 0; r < reps; ++r) {
          var bo = new ExactMeshBoolean(A, B, op); var sw = Stopwatch.StartNew(); bool ok = bo.Compute(); sw.Stop();
          oms.Add(sw.Elapsed.TotalMilliseconds); rms.Add(bo.PhaseMilliseconds.TryGetValue("rounding", out long x) ? x : -1);
          string hh = ok && bo.Result != null ? Hash(bo.Result) : "none";
          if (h != null && h != hh) hh = "NONDETERMINISTIC"; h = hh;
        }
        rms.Sort(); oms.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["id"] = id, ["op"] = name, ["cores"] = Environment.ProcessorCount, ["rounding_ms"] = rms[rms.Count / 2], ["ms"] = oms[oms.Count / 2], ["hash"] = h }));
      }
    }
  }
}
