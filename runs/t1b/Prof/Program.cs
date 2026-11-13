using System; using System.Collections.Generic; using System.Diagnostics; using System.IO; using System.Linq; using System.Text.Json; using g3; using gs;
static class P {
  static DMesh3 Load(string path) { var b = new DMesh3Builder(); var r = new StandardMeshReader() { MeshBuilder = b }.Read(path, ReadOptions.Defaults); var m = b.Meshes[0]; for (int i = 1; i < b.Meshes.Count; ++i) MeshEditor.Append(m, b.Meshes[i]); return m; }
  static void Main(string[] a) {
    var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = 1 }; DMesh3 wa = g.Generate().MakeDMesh(); DMesh3 wb = new DMesh3(wa); MeshTransforms.Translate(wb, new Vector3d(0.5, 0.3, 0.2));
    foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Difference }) new ExactMeshBoolean(wa, wb, op).Compute();
    for (int ai = 0; ai < a.Length; ai++) {
      string dir = a[ai]; string id = Path.GetFileName(dir);
      DMesh3 A = Load(Path.Combine(dir, "A.off")), B = Load(Path.Combine(dir, "B.off"));
      foreach (var (name, op) in new[] { ("union", ExactMeshBoolean.Operations.Union), ("intersection", ExactMeshBoolean.Operations.Intersection), ("difference", ExactMeshBoolean.Operations.Difference) }) {
        RRProf.T.Clear();
        var bo = new ExactMeshBoolean(A, B, op); var sw = Stopwatch.StartNew(); bo.Compute(); sw.Stop();
        var row = new Dictionary<string, object> { ["id"] = id, ["op"] = name, ["cores"] = Environment.ProcessorCount, ["ms"] = sw.Elapsed.TotalMilliseconds };
        foreach (var kv in bo.PhaseMilliseconds) row["ph." + kv.Key] = kv.Value;
        foreach (var kv in RRProf.T.OrderBy(k => k.Key)) row["rr." + kv.Key] = kv.Key.StartsWith("n.") ? kv.Value : kv.Value * 1000.0 / Stopwatch.Frequency;
        var r = bo.Rounding; if (r != null) { row["edits"] = r.Collapses + r.Flips + r.Moves + r.Zips + r.StripZips + r.VertexRemovals; row["tris"] = bo.Result?.TriangleCount ?? 0; row["init_pairs"] = r.InitialIntersections; row["init_bad"] = r.InitialDegenerate; row["clean"] = bo.RoundingClean; }
        Console.WriteLine(JsonSerializer.Serialize(row));
      }
    }
  }
}
