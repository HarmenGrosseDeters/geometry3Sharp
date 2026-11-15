// probe for #747: wrap (300/3000) + the first simplification pass only (no retries); one jsonl line per mesh, resumable
using System; using System.Collections.Generic; using System.Diagnostics; using System.IO; using System.Linq; using System.Text.Json; using g3; using gs;
class P {
  static DMesh3 Load(string f) {
    var o = new ReadOptions(); o.CustomFlags.Integers[STLReader.StrategyFlag] = (int)STLReader.Strategy.NoProcessing;
    var b = new DMesh3Builder(); StandardMeshReader.ReadFile(f, o, b); var m = b.Meshes[0];
    for (int i = 1; i < b.Meshes.Count; ++i) MeshEditor.Append(m, b.Meshes[i]); return m; }
  static void Main(string[] a) {
    string dir = a[0], list = a[1], outp = a[2]; double budget = double.Parse(a[3]); string saveDir = a.Length > 4 ? a[4] : null;
    var done = new HashSet<string>(File.Exists(outp) ? File.ReadAllLines(outp).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("mesh").GetString()) : new string[0]);
    var total = Stopwatch.StartNew(); Func<bool> never = () => false;
    foreach (string m in File.ReadAllLines(list)) {
      if (done.Contains(m)) continue; if (total.Elapsed.TotalSeconds > budget) break;
      var r = new Dictionary<string, object> { ["mesh"] = m };
      var input = Load(Path.Combine(dir, m + ".stl"));
      var sw = Stopwatch.StartNew();
      var w = new MeshAlphaWrap(input) { RelativeAlpha = 300, RelativeOffset = 3000 }; w.Compute();
      r["wrap_ms"] = sw.ElapsedMilliseconds; r["status"] = w.Status.ToString();
      if (w.Result != null && w.Result.TriangleCount > 0) {
        var raw = w.Result; r["raw"] = raw.TriangleCount;
        var ph = new Dictionary<string, long>(); sw.Restart();
        var res = MeshRobustRepair.SimplifyWithin(raw, 0.5 * w.UsedOffset, 0.5 * w.UsedOffset, true, never, out int sub,
          MeshRobustRepair.WrapSimplifyMinQuality, true, MeshRobustRepair.WrapSimplifySlabs, 0, ph, input, false, false);
        r["simp_ms"] = sw.ElapsedMilliseconds; r["certified"] = res != null; r["fin"] = res?.TriangleCount ?? 0;
        foreach (var kv in ph) r[kv.Key] = kv.Value;
        if (saveDir != null && res == null) StandardMeshWriter.WriteMesh(Path.Combine(saveDir, m + "_raw.obj"), raw, WriteOptions.Defaults);
      }
      File.AppendAllText(outp, JsonSerializer.Serialize(r) + "\n");
    } } }
