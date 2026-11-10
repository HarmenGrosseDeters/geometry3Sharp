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
        int sampleAll = 0;      // --sample-all k: k pieces sampled uniformly, both paths against the direct sum
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--ops") ops = args[++i].Split(',').ToList();
            else if (args[i] == "--check") maxCheck = int.Parse(args[++i]);
            else if (args[i] == "--only") only = args[++i];
            else if (args[i] == "--sample-all") sampleAll = int.Parse(args[++i]);
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
        if (sampleAll > 0) {
            var fr = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Intersection) { AntipodalMaxBoundarySegments = 0, RecordOpenWinding = true };
            var ar = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Intersection) { AntipodalMaxBoundarySegments = 1000, RecordOpenWinding = true,
                DisableFilters = args.Contains("--nofilter") };
            fr.Compute(); ar.Compute();
            var arr = ar.Arrangement;
            int n = arr.Output.Count, stride = Math.Max(1, n / sampleAll);
            var res = new System.Collections.Concurrent.ConcurrentBag<string>();
            int[] agree = new int[8];
            Parallel.For(0, (n + stride - 1) / stride, (kk) => {
                int i = kk * stride;
                if (ar.OpenWindingPerPiece[i] == null) return;
                var ot = arr.Output[i];
                Vector3d pa = arr.PointToVector(ot.A), pb = arr.PointToVector(ot.B), pc = arr.PointToVector(ot.C);
                var src = arr.Triangles[ot.Source];
                Vector3d nrm = new Vector3d((double)src.NX, (double)src.NY, (double)src.NZ).Normalized;
                double edge = Math.Max((pa - pb).Length, Math.Max((pb - pc).Length, (pc - pa).Length));
                for (int m = 0; m < 2; ++m) {
                    int va = Lookup(ar.OpenWindingPerPiece[i], m), vf = Lookup(fr.OpenWindingPerPiece[i], m);
                    double w7 = Direct(arr.Meshes[m], (pa + pb + pc) / 3 + 1e-7 * edge * nrm);
                    double w4 = Direct(arr.Meshes[m], (pa + pb + pc) / 3 + 1e-4 * edge * nrm);
                    int t7 = (int)Math.Ceiling(w7 - 0.5), t4 = (int)Math.Ceiling(w4 - 0.5);
                    System.Threading.Interlocked.Increment(ref agree[(va == t7 ? 1 : 0) + (vf == t7 ? 2 : 0) + (t4 == t7 ? 4 : 0)]);
                    if (va != t7 && res.Count < 10) {
                        // brute force: the Antipodal formula with a double ray from the front point (all triangles) and the chain
                        Vector3d q = (pa + pb + pc) / 3 + 1e-7 * edge * nrm;
                        var dirs = new[] { new Vector3d(0.6, 0.48, 0.64).Normalized, new Vector3d(-0.3, 0.9, -0.31).Normalized };
                        var bf = new List<string>();
                        Vector3d[] seg = ExactMeshBoolean.BoundarySegments(arr.Meshes[m]);
                        foreach (var x0 in dirs) {
                            int chi = 0;
                            DMesh3 mm = arr.Meshes[m];
                            Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
                            foreach (int t in mm.TriangleIndices()) {
                                mm.GetTriVertices(t, ref v0, ref v1, ref v2);
                                Vector3d e1 = v1 - v0, e2 = v2 - v0, h = x0.Cross(e2);
                                double det = e1.Dot(h);
                                if (Math.Abs(det) < 1e-300) continue;
                                double inv = 1 / det; Vector3d sv = q - v0;
                                double u = sv.Dot(h) * inv; if (u < 0 || u > 1) continue;
                                Vector3d qv = sv.Cross(e1); double vv = x0.Dot(qv) * inv; if (vv < 0 || u + vv > 1) continue;
                                double tt = e2.Dot(qv) * inv; if (tt <= 0) continue;
                                chi += det > 0 ? -1 : 1;    // (sign: leaving through the front side counts +1)
                            }
                            Vector3d x1 = -x0; double sum = 0;
                            for (int k2 = 0; k2 < seg.Length; k2 += 2) {
                                Vector3d A = (seg[k2] - q).Normalized, B = (seg[k2 + 1] - q).Normalized;
                                sum += 2 * Math.Atan2(A.Dot(B.Cross(x1)), 1 + A.Dot(B) + A.Dot(x1) + B.Dot(x1));
                            }
                            bf.Add($"chi {chi} sum {sum / (4 * Math.PI):G6}");
                        }
                        // the nearest triangle of mesh m other than the piece's source, and its distance from the centroid
                        Vector3d cc = (pa + pb + pc) / 3;
                        double best = double.MaxValue; int bestT = -1;
                        DMesh3 mm2 = arr.Meshes[m];
                        foreach (int t in mm2.TriangleIndices()) {
                            if (m == ot.Mesh && t == src.Tid) continue;
                            double d2 = MeshQueries.TriDistanceSqr(mm2, t, cc);
                            if (d2 < best) { best = d2; bestT = t; }
                        }
                        Vector3d tn = mm2.GetTriNormal(bestT);
                        res.Add($"piece {i} srcmesh {ot.Mesh} m {m} ant {va} fwn {vf} w7 {w7:G6} w4 {w4:G6} edge {edge:G3} nearest other {Math.Sqrt(best):G3} (normal dot {tn.Dot(nrm):F3}) brute [{string.Join("; ", bf)}]");
                    }
                }
            });
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')),
                ["agree_bits(ant,fwn,offsets)"] = agree, ["samples"] = res.ToArray() }));
            return 0;
        }
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
                    // (per piece: both again with the open winding numbers recorded; the arrangement is the same)
                    var fr = new ExactMeshBoolean(a, b, opOf[op]) { AntipodalMaxBoundarySegments = 0, RecordOpenWinding = true };
                    var ar = new ExactMeshBoolean(a, b, opOf[op]) { AntipodalMaxBoundarySegments = 1000, RecordOpenWinding = true };
                    fr.Compute(); ar.Compute();
                    int[] cmp = ComparePieces(ar, fr);
                    // ray independence: the Antipodal values with other rays (another seed: other far points and choices) must
                    // be the same at every piece (exact crossing counts; the boundary sum differs only by rounding)
                    var ar2 = new ExactMeshBoolean(a, b, opOf[op]) { AntipodalMaxBoundarySegments = 1000, RecordOpenWinding = true, Seed = 12345 };
                    ar2.Compute();
                    int rayDiff = 0;
                    var rayDiffSamples = new List<string>();
                    var segsByMesh = new Dictionary<int, Vector3d[]>();
                    for (int i = 0; i < ar.OpenWindingPerPiece.Length; ++i)
                        for (int m = 0; m < 2; ++m) {
                            int v1 = Lookup(ar.OpenWindingPerPiece[i], m), v2 = Lookup(ar2.OpenWindingPerPiece[i], m);
                            if (v1 == v2) continue;
                            rayDiff++;
                            if (rayDiffSamples.Count < 8) {
                                var arrA = ar.Arrangement;
                                var ot = arrA.Output[i];
                                Vector3d c = (arrA.PointToVector(ot.A) + arrA.PointToVector(ot.B) + arrA.PointToVector(ot.C)) / 3;
                                if (segsByMesh.ContainsKey(m) == false) segsByMesh[m] = ExactMeshBoolean.BoundarySegments(arrA.Meshes[m]);
                                var sg = segsByMesh[m];
                                double best = double.MaxValue;
                                for (int k = 0; k < sg.Length; k += 2) best = Math.Min(best, new Segment3d(sg[k], sg[k + 1]).DistanceSquared(c));
                                Vector3d pa = arrA.PointToVector(ot.A), pb = arrA.PointToVector(ot.B), pc = arrA.PointToVector(ot.C);
                                double edge = Math.Max((pa - pb).Length, Math.Max((pb - pc).Length, (pc - pa).Length));
                                rayDiffSamples.Add($"piece {i} mesh {m} srcmesh {ot.Mesh} seed1 {v1} seed2 {v2} dist to boundary {Math.Sqrt(best):G3} edge {edge:G3}");
                            }
                        }
                    row["antipodal_other_rays_differing"] = rayDiff;
                    row["ray_samples"] = rayDiffSamples.ToArray();
                    row["pieces_differing"] = cmp[0];
                    row["antipodal_right"] = cmp[1];
                    row["fwn_right"] = cmp[2];
                    row["neither_right"] = cmp[3];
                    row["skipped"] = cmp[4];
                    row["skipped_near_other_sheet"] = cmp[5];
                    row["samples"] = Samples.ToArray();
                    Samples.Clear();
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }


    // Per-piece comparison of the open meshes' thresholded winding numbers of two runs on the same inputs (the same
    // arrangement): for every piece and open mesh where they differ, the direct generalized winding number (the sum over
    // all of the mesh's triangles, no approximation) just in front of the piece (its centroid moved by 1e-7 of its longest
    // edge along its source's normal), thresholded the same way, decides which run is right. Values within 1e-6 of a
    // flip value are skipped.
    static int[] ComparePieces(ExactMeshBoolean x, ExactMeshBoolean y)
    {
        var arr = x.Arrangement;
        int[][] wx = x.OpenWindingPerPiece, wy = y.OpenWindingPerPiece;
        int n = arr.Output.Count;
        if (y.Arrangement.Output.Count != n || wx == null || wy == null)
            throw new InvalidOperationException("ComparePieces: different arrangements or no record");
        int differing = 0, xRight = 0, yRight = 0, neither = 0, skipped = 0;
        var items = new List<(int, int, int, int)>();
        for (int i = 0; i < n; ++i) {
            if (wx[i] == null && wy[i] == null) continue;
            for (int m = 0; m < arr.Meshes.Count; ++m) {
                int vx = Lookup(wx[i], m), vy = Lookup(wy[i], m);
                if (vx != vy) items.Add((i, m, vx, vy));
            }
        }
        differing = items.Count;
        // (pieces whose centroid lies within 1e-3 of their longest edge (or 1e-9 of the coordinates) from another triangle of the evaluated mesh are
        // skipped: there the offset point of the oracle can lie across that triangle, so the direct value does not stand
        // for the exact point in front; slivers along intersection curves are such pieces)
        var near = new bool[items.Count];
        var nearDist = new double[items.Count];
        var trees = new Dictionary<int, DMeshAABBTree3>();
        for (int k = 0; k < items.Count; ++k) {
            var (i, m, vx, vy) = items[k];
            if (trees.TryGetValue(m, out DMeshAABBTree3 tree) == false)
                trees[m] = tree = new DMeshAABBTree3(arr.Meshes[m], true);
            var ot = arr.Output[i];
            var src = arr.Triangles[ot.Source];
            Vector3d a = arr.PointToVector(ot.A), b = arr.PointToVector(ot.B), c = arr.PointToVector(ot.C);
            double edge = Math.Max((a - b).Length, Math.Max((b - c).Length, (c - a).Length));
            int skipTid = (src.Mesh == m) ? src.Tid : -1;
            var stackT = new HashSet<int>();
            if (src.Mesh == m) stackT.Add(src.Tid);
            foreach (int u in src.Coplanar) if (arr.Triangles[u].Mesh == m) stackT.Add(arr.Triangles[u].Tid);
            tree.TriangleFilterF = (t) => stackT.Contains(t) == false;
            double dist;
            int nt = tree.FindNearestTriangle((a + b + c) / 3, out dist);
            tree.TriangleFilterF = null;
            dist = Math.Sqrt(dist);
            nearDist[k] = dist;
            // (also degenerate pieces, whose corners round to nearly one point: no direction in front)
            double scale = Math.Abs(a.x) + Math.Abs(a.y) + Math.Abs(a.z) + 1;
            near[k] = (nt >= 0 && dist <= Math.Max(1e-3 * edge, 1e-9 * scale)) || edge <= 1e-12 * scale;
        }
        var verdict = new int[items.Count];
        System.Threading.Tasks.Parallel.For(0, items.Count, (k) => {
            var (i, m, vx, vy) = items[k];
            if (near[k]) { verdict[k] = 0; return; }
            var ot = arr.Output[i];
            Vector3d a = arr.PointToVector(ot.A), b = arr.PointToVector(ot.B), c = arr.PointToVector(ot.C);
            var src = arr.Triangles[ot.Source];
            Vector3d nrm = new Vector3d((double)src.NX, (double)src.NY, (double)src.NZ).Normalized;
            double edge = Math.Max((a - b).Length, Math.Max((b - c).Length, (c - a).Length));
            Vector3d q = (a + b + c) / 3 + 1e-7 * edge * nrm;
            DMesh3 mesh = arr.Meshes[m];
            double w = 0;
            Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero, p2 = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref p0, ref p1, ref p2);
                w += MathUtil.TriSolidAngle(p0, p1, p2, ref q) / MathUtil.FourPI;
            }
            double frac = w - 0.5 - Math.Floor(w - 0.5);
            if (frac < 1e-6 || frac > 1 - 1e-6) { verdict[k] = 0; return; }
            int t0 = (int)Math.Ceiling(w - 0.5);
            verdict[k] = t0 == vx ? 1 : t0 == vy ? 2 : 3;
            if (verdict[k] != 1 && Samples.Count < 12) lock (Samples) Samples.Add($"piece {i} src mesh {ot.Mesh} eval mesh {m} ant {vx} fwn {vy} direct {w:R} stack {src.Coplanar.Count} edge {edge:G3} nearest other sheet {nearDist[k]:G3}");
        });
        foreach (int v in verdict) {
            if (v == 0) skipped++; else if (v == 1) xRight++; else if (v == 2) yRight++; else neither++;
        }
        return new[] { differing, xRight, yRight, neither, skipped, near.Count(v => v) };
    }

    static readonly List<string> Samples = new List<string>();

    static int Lookup(int[] w, int m)
    {
        if (w == null) return 0;
        for (int k = 0; k < w.Length; k += 2)
            if (w[k] == m) return w[k + 1];
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

    static string KeyReversed(DMesh3 m, int t)
    {
        Index3i f = m.GetTriangle(t);
        var v = new[] { m.GetVertex(f.a), m.GetVertex(f.c), m.GetVertex(f.b) };
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
