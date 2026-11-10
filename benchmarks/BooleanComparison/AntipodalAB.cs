// A/B of the open-operand classification: the Antipodal Method with the boundary-segment switch (the default) against
// fast winding numbers everywhere (AntipodalMaxBoundarySegments = 0, the classification of master before), on a case
// directory (A.off, B.off; `export`, the #385 pairing). docs/design/EXACT_BOOLEAN_30_PRESTUDY.md section 5.1.
//   antipodal-ab <case dir> [--ops union,intersection,difference] [--check <max triangles>]
// One JSON line per operation: times and classification times of both, whether the results are equal (hash), the
// boundary segments and the meshes on the Antipodal path, and the differing result triangles checked against the
// direct generalized winding number (the sum over all triangles, no approximation) of the input they do not lie on:
// a triangle only in the Antipodal result must be kept by the direct value, one only in the FWN result not (rule of
// ExactBooleanOpenPerPieceTests.AssertPieces; triangles on both inputs or within 1e-6 of 1/2 are skipped).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using g3;

static class AntipodalAB
{
    public static int Run(string[] args)
    {
        string dir = args[0];
        var ops = new List<string> { "union", "intersection", "difference" };
        int maxCheck = 400;
        string only = null;     // --only fwn|antipodal: one variant (for traces), no comparison
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--ops") ops = args[++i].Split(',').ToList();
            else if (args[i] == "--check") maxCheck = int.Parse(args[++i]);
            else if (args[i] == "--only") only = args[++i];
        }
        var opOf = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        // warm-up
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        foreach (int t in w1.TriangleIndices().ToArray()) if (w1.GetTriCentroid(t).z > 0.6) w1.RemoveTriangle(t, true, false);
        DMesh3 w2 = new DMesh3(w1); MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        for (int k = 0; k < 2; ++k)
            foreach (int m in new[] { 0, 1000 })
                new ExactMeshBoolean(w1, w2, ExactMeshBoolean.Operations.Union) { AntipodalMaxBoundarySegments = m }.Compute();
        var ta = new DMeshAABBTree3(a, true);
        var tb = new DMeshAABBTree3(b, true);
        foreach (string op in ops) {
            var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["op"] = op };
            var results = new Dictionary<string, ExactMeshBoolean>();
            foreach (var (name, max) in new[] { ("fwn", 0), ("antipodal", 1000) }) {
                if (only != null && only != name)
                    continue;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var r = new ExactMeshBoolean(a, b, opOf[op]) { AntipodalMaxBoundarySegments = max };
                var sw = Stopwatch.StartNew();
                bool ok = r.Compute();
                row[name + "_ms"] = sw.ElapsedMilliseconds;
                row[name + "_ok"] = ok;
                row[name + "_classification_ms"] = r.PhaseMilliseconds.TryGetValue("classification", out long c) ? c : -1;
                row[name + "_input_ms"] = r.PhaseMilliseconds.TryGetValue("input", out long inp) ? inp : -1;
                row[name + "_undecided"] = r.Counts.TryGetValue("antipodal_undecided", out long und) ? und : 0;
                row[name + "_classification_alloc_mb"] = r.PhaseAllocatedBytes.TryGetValue("classification", out long cb) ? cb / 1048576 : -1;
                if (name == "antipodal") {
                    row["boundary_segments"] = r.OpenBoundarySegments;
                    row["antipodal_meshes"] = r.Counts.TryGetValue("antipodal_meshes", out long am) ? am : 0;
                }
                results[name] = r;
            }
            ExactMeshBoolean f, an;
            results.TryGetValue("fwn", out f);
            results.TryGetValue("antipodal", out an);
            if (f != null && an != null && f.Result != null && an.Result != null) {
                row["equal"] = Hash(f.Result) == Hash(an.Result);
                row["tris"] = new[] { f.Result.TriangleCount, an.Result.TriangleCount };
                if ((bool)row["equal"] == false) {
                    // (the comparison on the pieces: both again without SimplifyFlatRegions and SafeRounding, which
                    // retriangulate around changed pieces; then every result triangle is an arrangement piece)
                    var fr = new ExactMeshBoolean(a, b, opOf[op]) { AntipodalMaxBoundarySegments = 0, SimplifyFlatRegions = false, SafeRounding = false };
                    var ar = new ExactMeshBoolean(a, b, opOf[op]) { AntipodalMaxBoundarySegments = 1000, SimplifyFlatRegions = false, SafeRounding = false };
                    fr.Compute(); ar.Compute();
                    f = fr; an = ar;
                    var onlyAnt = Missing(an.Result, f.Result);
                    var onlyFwn = Missing(f.Result, an.Result);
                    row["only_antipodal"] = onlyAnt.Count;
                    row["only_fwn"] = onlyFwn.Count;
                    // the direct value per differing triangle (a fixed subsample when there are many), in parallel
                    var items = onlyAnt.Select(t => (an.Result, t, true)).Concat(onlyFwn.Select(t => (f.Result, t, false))).ToList();
                    int stride = Math.Max(1, items.Count / maxCheck);
                    var sample = items.Where((x, k) => k % stride == 0).ToList();
                    var verdict = new int[sample.Count];   // 0 skipped, 1 Antipodal right, 2 Antipodal wrong
                    Parallel.For(0, sample.Count, (k) => {
                        var (m, t, inAnt) = sample[k];
                        Vector3d c = m.GetTriCentroid(t);
                        double da = Math.Sqrt(MeshQueries.TriDistanceSqr(a, ta.FindNearestTriangle(c), c));
                        double db = Math.Sqrt(MeshQueries.TriDistanceSqr(b, tb.FindNearestTriangle(c), c));
                        double res = 1e-14 * (Math.Max(Math.Abs(c.x), Math.Max(Math.Abs(c.y), Math.Abs(c.z))) + 1);
                        if (Math.Abs(da - db) <= Math.Max(1e-3 * (da + db), res)) return;
                        bool onA = da < db;
                        double w = Direct(onA ? b : a, c);
                        if (Math.Abs(w - 0.5) < 1e-6) return;
                        bool inOther = w > 0.5;
                        bool keep = op == "intersection" ? inOther : op == "union" ? !inOther : (onA ? !inOther : inOther);
                        verdict[k] = keep == inAnt ? 1 : 2;
                    });
                    row["checked"] = sample.Count;
                    row["antipodal_right"] = verdict.Count(v => v == 1);
                    row["antipodal_wrong"] = verdict.Count(v => v == 2);
                    row["skipped"] = verdict.Count(v => v == 0);
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }

    static double Direct(DMesh3 m, Vector3d p)
    {
        double w = 0;
        Vector3d x = Vector3d.Zero, y = Vector3d.Zero, z = Vector3d.Zero;
        foreach (int t in m.TriangleIndices()) {
            m.GetTriVertices(t, ref x, ref y, ref z);
            w += MathUtil.TriSolidAngle(x, y, z, ref p) / MathUtil.FourPI;
        }
        return w;
    }

    static string Key(DMesh3 m, int t)
    {
        Index3i f = m.GetTriangle(t);
        var v = new[] { m.GetVertex(f.a), m.GetVertex(f.b), m.GetVertex(f.c) };
        int k = 0;
        for (int i = 1; i < 3; ++i) if (v[i].CompareTo(v[k]) < 0) k = i;
        return string.Join(";", Enumerable.Range(0, 3).Select(i => v[(k + i) % 3].ToString("R")));
    }

    static List<int> Missing(DMesh3 x, DMesh3 y)
    {
        var have = new HashSet<string>(y.TriangleIndices().Select(t => Key(y, t)));
        return x.TriangleIndices().Where(t => have.Contains(Key(x, t)) == false).ToList();
    }

    static string Hash(DMesh3 m)
    {
        ulong h = 14695981039346656037UL;
        void add(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            add(v); add(BitConverter.DoubleToInt64Bits(p.x)); add(BitConverter.DoubleToInt64Bits(p.y)); add(BitConverter.DoubleToInt64Bits(p.z));
        }
        foreach (int t in m.TriangleIndices()) { Index3i f = m.GetTriangle(t); add(t); add(f.a); add(f.b); add(f.c); }
        return h.ToString("X16");
    }
}
