// Classification and seeded stratified selection of Thingi10K models (read from a local copy; no mesh is copied or committed).
//   classify <raw_meshes dir> --seed 2611 --per-class 8 --max-tris 100000 --out results/thingi
// Classes (by the library): C closed and clean (MeshRobustRepair.NotCleanReason == ""), S closed (winding-closed) but not clean
// (the reason is recorded), H open, boundary edges at most 2 % of the edges (small holes), O open with more (sheets, soups).
// The candidates are the files of the directory except the T1 ids, in a seeded random order; the scan stops when every class has its quota.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using g3;
using gs;

static class Classify
{
    public static readonly string[] T1 = { "96675", "96773", "64983", "43551", "1582399", "57084", "104640", "94664", "73088", "1582404", "37731", "57714",
        "116871", "109459", "472011", "37111", "113418", "79961", "1038438", "66112" };

    public static string ClassOf(DMesh3 m, out string reason, out double boundaryShare)
    {
        int edges = 0, boundary = 0;
        foreach (int eid in m.EdgeIndices()) { edges++; if (m.IsBoundaryEdge(eid)) boundary++; }
        boundaryShare = edges == 0 ? 1 : (double)boundary / edges;
        reason = MeshRobustRepair.NotCleanReason(m);
        if (reason == "") return "C";
        if (boundary == 0 && ExactMeshBoolean.IsWindingClosed(m)) return "S";
        if (m.IsClosed()) return "S";
        return boundaryShare <= 0.02 ? "H" : "O";
    }

    public static int Run(string[] args)
    {
        string dir = args[1]; int seed = 2611, perClass = 8, maxTris = 100000, maxScan = 800; string outPrefix = null;
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--seed") seed = int.Parse(args[++i]);
            else if (args[i] == "--per-class") perClass = int.Parse(args[++i]);
            else if (args[i] == "--max-tris") maxTris = int.Parse(args[++i]);
            else if (args[i] == "--max-scan") maxScan = int.Parse(args[++i]);
            else if (args[i] == "--out") outPrefix = args[++i];
        }
        var lines = new List<string> { "id,class,tris,boundary_share,reason" };
        // T1
        foreach (string id in T1) {
            string path = Path.Combine(dir, id + ".stl");
            if (!File.Exists(path)) { lines.Add(id + ",missing,,,"); continue; }
            DMesh3 m = Inputs.LoadFile(path);
            string c = ClassOf(m, out string reason, out double bs);
            lines.Add(id + ",T1:" + c + "," + m.TriangleCount + "," + bs.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + "," + reason);
            Console.WriteLine("T1 " + lines[lines.Count - 1]);
        }
        var files = Directory.GetFiles(dir, "*.stl").Select(Path.GetFileNameWithoutExtension).Where(f => !T1.Contains(f)).OrderBy(f => int.Parse(f)).ToList();
        var rnd = new Random(seed);
        for (int i = files.Count - 1; i > 0; --i) { int j = rnd.Next(i + 1); (files[i], files[j]) = (files[j], files[i]); }
        var picked = new Dictionary<string, List<string>> { ["C"] = new List<string>(), ["S"] = new List<string>(), ["H"] = new List<string>(), ["O"] = new List<string>() };
        int scanned = 0, skipped = 0;
        foreach (string id in files) {
            if (picked.Values.All(l => l.Count >= perClass) || scanned >= maxScan) break;
            string path = Path.Combine(dir, id + ".stl");
            if (new FileInfo(path).Length > 50L * maxTris + 84 + 1000) { skipped++; continue; }      // binary STL: 50 bytes per triangle
            DMesh3 m = null;
            try { m = Inputs.LoadFile(path); } catch { }
            scanned++;
            if (m == null || m.TriangleCount == 0 || m.TriangleCount > maxTris) { skipped++; continue; }
            string c;
            string reason; double bs;
            try { c = ClassOf(m, out reason, out bs); } catch (Exception e) { skipped++; continue; }
            lines.Add(id + "," + c + "," + m.TriangleCount + "," + bs.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + "," + reason);
            if (picked[c].Count < perClass) { picked[c].Add(id); Console.WriteLine("pick " + lines[lines.Count - 1]); }
        }
        Console.WriteLine("scanned " + scanned + ", skipped " + skipped);
        if (outPrefix != null) {
            File.WriteAllLines(outPrefix + "_scan.csv", lines);
            foreach (var kv in picked) File.WriteAllLines(outPrefix + "_" + kv.Key + ".txt", kv.Value.Select(id => Path.Combine(dir, id + ".stl")));
            File.WriteAllLines(outPrefix + "_T1.txt", T1.Select(id => Path.Combine(dir, id + ".stl")));
        }
        return 0;
    }
}
