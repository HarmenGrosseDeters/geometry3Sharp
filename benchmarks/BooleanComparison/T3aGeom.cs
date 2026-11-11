// T3a acceptance on real models: Difference and Intersection of a case directory (A.off, B.off; `export`, the #385
// pairing) with and without the early-out (ExactMeshBoolean.CullOutsideA): the results must be the same geometry (the
// same oriented triangles at the same exact coordinates, ids ignored: the test harness's Invariance.GeometricDifference)
// and the same volume to the bit.
//   t3a-geom <case dir> [--ops difference,intersection]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

static class T3aGeom
{
    public static int Run(string[] args)
    {
        string dir = args[0];
        var ops = new List<string> { "difference", "intersection" };
        for (int i = 1; i < args.Length; ++i)
            if (args[i] == "--ops") ops = args[++i].Split(',').ToList();
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        foreach (string opName in ops) {
            var op = opName == "intersection" ? ExactMeshBoolean.Operations.Intersection : ExactMeshBoolean.Operations.Difference;
            var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["op"] = opName };
            var res = new Dictionary<bool, ExactMeshBoolean>();
            foreach (bool cull in new[] { false, true }) {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var o = new ExactMeshBoolean(a, b, op) { CullOutsideA = cull };
                var sw = Stopwatch.StartNew();
                o.Compute();
                string k = cull ? "cull" : "base";
                row[k + "_ms"] = sw.ElapsedMilliseconds;
                row[k + "_status"] = o.Status.ToString();
                if (cull) row["culled"] = o.Counts["t3a_culled"];
                res[cull] = o;
            }
            DMesh3 x = res[false].Result, y = res[true].Result;
            row["status_equal"] = res[false].Status == res[true].Status;
            if (x != null && y != null) {
                row["tris"] = new[] { x.TriangleCount, y.TriangleCount };
                row["bit_identical"] = BitIdentical(x, y);
                row["geometric_difference"] = GeometricDifference(x, y);
                double vx = Volume(x), vy = Volume(y);
                row["volume_equal"] = BitConverter.DoubleToInt64Bits(vx) == BitConverter.DoubleToInt64Bits(vy);
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }

    static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

    static bool BitIdentical(DMesh3 x, DMesh3 y)
    {
        if (x.MaxVertexID != y.MaxVertexID || x.MaxTriangleID != y.MaxTriangleID) return false;
        for (int v = 0; v < x.MaxVertexID; ++v)
            if (x.IsVertex(v) != y.IsVertex(v) || (x.IsVertex(v) && x.GetVertex(v) != y.GetVertex(v))) return false;
        for (int t = 0; t < x.MaxTriangleID; ++t)
            if (x.IsTriangle(t) != y.IsTriangle(t) || (x.IsTriangle(t) && x.GetTriangle(t) != y.GetTriangle(t))) return false;
        return true;
    }

    // "" if both are the same set of oriented triangles at the same exact coordinates (ids and the starting corner of
    // each triangle ignored), else the first difference (as Invariance.GeometricDifference of the tests)
    static string GeometricDifference(DMesh3 x, DMesh3 y)
    {
        List<string> p = Canonical(x), q = Canonical(y);
        if (p.Count != q.Count) return $"triangle count {p.Count} vs {q.Count}";
        for (int i = 0; i < p.Count; ++i)
            if (p[i] != q[i]) return $"triangle {p[i]} vs {q[i]}";
        return "";
    }

    static List<string> Canonical(DMesh3 m)
    {
        var list = new List<string>();
        var c = new Vector3d[3];
        foreach (int t in m.TriangleIndices()) {
            Index3i tri = m.GetTriangle(t);
            for (int k = 0; k < 3; ++k) c[k] = m.GetVertex(tri[k]);
            int first = 0;
            for (int k = 1; k < 3; ++k)
                if (Compare(c[k], c[first]) < 0) first = k;
            list.Add(string.Join(";", Enumerable.Range(0, 3).Select(k => Key(c[(first + k) % 3]))));
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    static int Compare(Vector3d u, Vector3d v)
    {
        int r = u.x.CompareTo(v.x);
        if (r != 0) return r;
        r = u.y.CompareTo(v.y);
        return r != 0 ? r : u.z.CompareTo(v.z);
    }

    static string Key(Vector3d v)
    {
        return $"{BitConverter.DoubleToInt64Bits(v.x):X16},{BitConverter.DoubleToInt64Bits(v.y):X16},{BitConverter.DoubleToInt64Bits(v.z):X16}";
    }
}
