using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using gs;

// Correctness only: for each mesh file, the verdict of MeshRobustRepair.NotCleanReason (with and without the
// self-intersection check), the full defect list (NotCleanReasons), and the number and a hash of the self-intersection
// pairs, one tab-separated line per mesh. Run with two builds of the library and diff the outputs (compare.sh).
// Usage: NotCleanVerdict <mesh-list-file> <mesh-dir>   (the list has one file name per line, relative to the directory)
static class Program
{
    static string PairHash(List<Index2i> pairs)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(pairs.Count);
        foreach (var p in pairs) { w.Write(p.a); w.Write(p.b); }
        w.Flush();
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").Substring(0, 16);
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: NotCleanVerdict <mesh-list-file> <mesh-dir>"); return 2; }
        foreach (string name in File.ReadAllLines(args[0]).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"))) {
            string path = Path.Combine(args[1], name);
            string line;
            try {
                var builder = new DMesh3Builder();
                IOReadResult rr = StandardMeshReader.ReadFile(path, new ReadOptions(), builder);
                if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) {
                    line = "load_failed";
                } else {
                    DMesh3 mesh = builder.Meshes[0];
                    for (int i = 1; i < builder.Meshes.Count; ++i)
                        MeshEditor.Append(mesh, builder.Meshes[i]);
                    string withSi = MeshRobustRepair.NotCleanReason(mesh, true);
                    string withoutSi = MeshRobustRepair.NotCleanReason(mesh, false);
                    string all = MeshRobustRepair.NotCleanReasons(mesh, true);
                    var pairs = MeshRepairMetrics.SelfIntersectionPairs(mesh);
                    line = $"tris={mesh.TriangleCount}\treason=\"{withSi}\"\tno_selfint=\"{withoutSi}\"\tall=\"{all}\"\tpairs={pairs.Count}\tpairhash={PairHash(pairs)}";
                }
            } catch (Exception e) {
                line = "exception=" + e.GetType().Name;
            }
            Console.WriteLine(name + "\t" + line);
        }
        return 0;
    }
}
