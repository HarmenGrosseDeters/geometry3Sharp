using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;

static class Program
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        if (mode == "bound") {
            // bound <out.csv> <mesh.stl|proc> ...
            string outPath = args[1];
            bool header = !File.Exists(outPath);
            foreach (string src in args.Skip(2)) {
                var inputs = src == "proc" ? S.Procedural().ToList()
                    : new List<KeyValuePair<string, DMesh3>> { new KeyValuePair<string, DMesh3>(Path.GetFileName(src), StandardMeshReader.ReadMesh(src)) };
                foreach (var kv in inputs) {
                    var rows = Bound.Run(kv.Key, kv.Value);
                    using (var w = new StreamWriter(outPath, true)) {
                        if (header) { w.WriteLine(Bound.Header); header = false; }
                        foreach (var r in rows) w.WriteLine(r);
                    }
                    Console.WriteLine(kv.Key + ": " + rows.Count + " rows");
                }
            }
            return 0;
        }
        if (mode == "local") {
            // local <out.csv> <mesh.stl|proc> ...
            string outPath = args[1];
            bool header = !File.Exists(outPath);
            foreach (string src in args.Skip(2)) {
                var inputs = src == "proc" ? S.Procedural().ToList()
                    : new List<KeyValuePair<string, DMesh3>> { new KeyValuePair<string, DMesh3>(Path.GetFileName(src), StandardMeshReader.ReadMesh(src)) };
                foreach (var kv in inputs) {
                    var rows = Local.Go(kv.Key, kv.Value);
                    using (var w = new StreamWriter(outPath, true)) {
                        if (header) { w.WriteLine(Local.Header); header = false; }
                        foreach (var r in rows) w.WriteLine(r);
                    }
                    Console.WriteLine(kv.Key + ": " + rows.Count + " rows");
                }
            }
            return 0;
        }
        if (mode == "area") {   // area <name>: surface area and closedness of a procedural input
            foreach (var kv in S.Procedural()) {
                if (args.Length > 1 && kv.Key != args[1]) continue;
                double a = 0; foreach (int t in kv.Value.TriangleIndices()) a += kv.Value.GetTriArea(t);
                Console.WriteLine(kv.Key + " area " + a + " tris " + kv.Value.TriangleCount + " boundary edges " + kv.Value.BoundaryEdgeIndices().Count() + " diag " + kv.Value.CachedBounds.DiagonalLength);
            }
            return 0;
        }
        Console.Error.WriteLine("modes: bound area");
        return 1;
    }
}
