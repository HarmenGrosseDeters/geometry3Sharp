// The "oracle" mode (research note docs/research/ROUNDING_Z3_ORACLE_2026-11.md, #1465): the state the rounding repair leaves, written out
// exactly (every coordinate as the bits of its double) for the Z3 model of tools/Z3Pilot/rounding_oracle.py, and the check of a witness
// that the model finds, with the repair's own tests (is_bad, intersects, the drift bound of triangle_bound / ghost_bound, try_move_pair).
// A measurement harness: no library change; the repair's private members are read by reflection.
//
//   oracle dump --case <spec> --out <file.json> [--nojoint]
//   oracle verify --case <spec> --witness <file.json> [--nojoint]
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using g3;
using gs;

static partial class Program
{
    const BindingFlags AnyInstance = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static string Hex(double d) => BitConverter.DoubleToInt64Bits(d).ToString("x16");
    static double FromHex(string s) => BitConverter.Int64BitsToDouble(long.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    static string HexV(Vector3d p) => $"[\"{Hex(p.x)}\",\"{Hex(p.y)}\",\"{Hex(p.z)}\"]";

    static MeshRoundingRepair OracleState(string spec, bool nojoint)
    {
        string[] f = spec.Split(',');
        var (hn, h) = Surfaces.First(x => x.Item1 == f[0]);
        int n = int.Parse(f[1], CultureInfo.InvariantCulture), dn = int.Parse(f[2], CultureInfo.InvariantCulture);
        double t = double.Parse(f[3], CultureInfo.InvariantCulture), e = double.Parse(f[4], CultureInfo.InvariantCulture);
        double ratio = double.Parse(f[5], CultureInfo.InvariantCulture), shift = double.Parse(f[6], CultureInfo.InvariantCulture);
        bool diff = f[7] == "D";
        Func<Vector3d, Vector3d> sh = p => p + new Vector3d(shift, shift * 0.5, -shift * 0.25);
        DMesh3 coarse = Plate(n, t, h, sh), fine = Plate(n + dn, t, h, p => sh(Rot(p, e, ratio * e)));
        DMesh3 a = diff ? coarse : fine, b = diff ? fine : coarse;
        FieldInfo nj = typeof(MeshRoundingRepair).GetField("NoJointSearchOnThisThread", BindingFlags.NonPublic | BindingFlags.Static);
        nj.SetValue(null, nojoint);
        var plain = new ExactMeshBoolean(a, b, D);
        plain.Compute();
        nj.SetValue(null, false);
        return plain.Rounding;
    }

    static int Oracle(string[] args)
    {
        string mode = args.Length > 1 ? args[1] : "";
        string spec = null, outPath = null, witness = null;
        var witnesses = new List<string>();
        bool nojoint = false;
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--case") spec = args[++i];
            else if (args[i] == "--out") outPath = args[++i];
            else if (args[i] == "--witness") { witness = args[++i]; witnesses.Add(witness); }
            else if (args[i] == "--nojoint") nojoint = true;
            else { Console.WriteLine("unknown argument " + args[i]); return 2; }
        }
        if (spec == null) { Console.WriteLine("--case is required"); return 2; }
        MeshRoundingRepair r = OracleState(spec, nojoint);
        if (mode == "dump") {
            // (--witness: joint moves made first through try_move_pair, one after the other; the state after them is dumped)
            if (witnesses.Count > 0 && OracleApply(r, witnesses) != 0)
                return 1;
            File.WriteAllText(outPath, OracleDump(r, spec, nojoint));
            Console.WriteLine($"dumped {spec}: triangles {r.Mesh.TriangleCount}, left degenerate {r.RemainingDegenerate}, intersections {r.RemainingIntersections}");
            return 0;
        }
        if (mode == "verify")
            return OracleVerify(r, witness);
        if (mode == "apply")
            return OracleApply(r, witnesses);
        Console.WriteLine("oracle dump|verify");
        return 2;
    }

    static string OracleDump(MeshRoundingRepair r, string spec, bool nojoint)
    {
        Type T = typeof(MeshRoundingRepair);
        DMesh3 m = r.Mesh;
        var P = new Probe(r);
        var refAnchor = (Vector3d[])T.GetField("refAnchor", AnyInstance).GetValue(r);
        var refCarry = (double[])T.GetField("refCarry", AnyInstance).GetValue(r);
        var componentOf = (int[])T.GetField("componentOf", AnyInstance).GetValue(r);
        var areaEpsC = (double[])T.GetField("areaEpsC", AnyInstance).GetValue(r);
        var sizeAreaC = (double[])T.GetField("sizeAreaC", AnyInstance).GetValue(r);
        var maxLengthC = (double[])T.GetField("maxLengthC", AnyInstance).GetValue(r);
        var ghostsAt = (IDictionary)T.GetField("ghostsAt", AnyInstance).GetValue(r);
        var canonical = T.GetMethod("canonical", AnyInstance, null, new[] { typeof(int) }, null);
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"\"case\": \"{spec}\", \"nojoint\": {(nojoint ? "true" : "false")},\n");
        sb.Append($"\"remaining_degenerate\": {r.RemainingDegenerate}, \"remaining_intersections\": {r.RemainingIntersections}, \"remaining_contacts\": {r.RemainingFalseContacts}, \"joint_moves\": {r.JointMoves},\n");
        sb.Append($"\"max_length\": \"{Hex(r.MaxLength)}\",\n");
        sb.Append($"\"area_eps\": [{string.Join(",", areaEpsC.Select(x => "\"" + Hex(x) + "\""))}],\n");
        sb.Append($"\"size_area\": [{string.Join(",", sizeAreaC.Select(x => "\"" + Hex(x) + "\""))}],\n");
        sb.Append($"\"max_length_c\": [{string.Join(",", maxLengthC.Select(x => "\"" + Hex(x) + "\""))}],\n");
        sb.Append($"\"bad\": [{string.Join(",", P.Bad())}],\n");
        sb.Append("\"vertices\": [\n");
        bool first = true;
        foreach (int v in m.VertexIndices()) {
            if (!first) sb.Append(",\n");
            first = false;
            int canon = (int)canonical.Invoke(r, new object[] { v });
            sb.Append($"{{\"id\": {v}, \"p\": {HexV(m.GetVertex(v))}, \"fixed\": {(P.IsFixed(v) ? "true" : "false")}, \"boundary\": {(m.IsBoundaryVertex(v) ? "true" : "false")}, \"bowtie\": {(m.IsBowtieVertex(v) ? "true" : "false")}, \"component\": {componentOf[v]}, \"canonical\": {canon}, \"room\": \"{Hex(P.Room(v))}\"}}");
        }
        sb.Append("\n],\n\"triangles\": [\n");
        first = true;
        foreach (int t in m.TriangleIndices()) {
            if (!first) sb.Append(",\n");
            first = false;
            Index3i tv = m.GetTriangle(t);
            sb.Append($"{{\"id\": {t}, \"v\": [{tv.a},{tv.b},{tv.c}], \"anchors\": [{HexV(refAnchor[3 * t])},{HexV(refAnchor[3 * t + 1])},{HexV(refAnchor[3 * t + 2])}], \"carry\": \"{Hex(refCarry[t])}\", \"bad\": {(P.IsBad(t) ? "true" : "false")}}}");
        }
        sb.Append("\n],\n\"ghosts\": [\n");
        // each ghost once (it is listed at each vertex it lies on)
        var seen = new Dictionary<object, List<int>>(ReferenceEqualityComparer.Instance);
        var order = new List<object>();
        foreach (DictionaryEntry kv in ghostsAt)
            foreach (object g in (IEnumerable)kv.Value) {
                if (!seen.TryGetValue(g, out List<int> at)) { seen[g] = at = new List<int>(); order.Add(g); }
                at.Add((int)kv.Key);
            }
        first = true;
        foreach (object g in order) {
            Type G = g.GetType();
            var at = (Index3i)G.GetField("at").GetValue(g);
            var a0 = (Vector3d)G.GetField("a0").GetValue(g);
            var a1 = (Vector3d)G.GetField("a1").GetValue(g);
            var a2 = (Vector3d)G.GetField("a2").GetValue(g);
            double carry = (double)G.GetField("carry").GetValue(g);
            bool lost = (bool)G.GetField("lost").GetValue(g);
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append($"{{\"at\": [{at.a},{at.b},{at.c}], \"anchors\": [{HexV(a0)},{HexV(a1)},{HexV(a2)}], \"carry\": \"{Hex(carry)}\", \"lost\": {(lost ? "true" : "false")}, \"listed_at\": [{string.Join(",", seen[g].OrderBy(x => x))}]}}");
        }
        sb.Append("\n]\n}\n");
        return sb.ToString();
    }

    // the witnesses of several triangles of one state, one after the other through the library's own joint edit (try_move_pair:
    // improves with its counts, orientation rule, drift and shared positions); an accepted one is committed; then the state from scratch
    static int OracleApply(MeshRoundingRepair r, List<string> paths)
    {
        Type T = typeof(MeshRoundingRepair);
        var P = new Probe(r);
        MethodInfo pair = T.GetMethod("try_move_pair", AnyInstance);
        int before = P.Bad().Count;
        foreach (string path in paths) {
            var moves = new List<(int, Vector3d)>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                foreach (JsonElement e in doc.RootElement.GetProperty("moves").EnumerateArray()) {
                    var p = e.GetProperty("p").EnumerateArray().Select(x => FromHex(x.GetString())).ToArray();
                    moves.Add((e.GetProperty("v").GetInt32(), new Vector3d(p[0], p[1], p[2])));
                }
            if (moves.Count != 2) { Console.WriteLine($"{path}: {moves.Count} moves, only pairs are applied"); continue; }
            bool ok = (bool)pair.Invoke(r, new object[] { moves[0].Item1, moves[0].Item2, moves[1].Item1, moves[1].Item2 });
            Console.WriteLine($"{Path.GetFileName(path)}: try_move_pair accepted {ok}");
        }
        var boxes = new AxisAlignedBox3d[r.Mesh.MaxTriangleID];
        foreach (int tid in r.Mesh.TriangleIndices()) boxes[tid] = P.Box(tid);
        var tids = r.Mesh.TriangleIndices().ToList();
        int pairs = 0;
        for (int i = 0; i < tids.Count; ++i)
            for (int j = i + 1; j < tids.Count; ++j)
                if (boxes[tids[i]].Intersects(boxes[tids[j]]) && P.Intersects(tids[i], tids[j]))
                    pairs++;
        Console.WriteLine($"APPLY degenerate {before} -> {P.Bad().Count}, intersecting pairs {pairs}, MaxDrift {r.MaxDrift:G4} <= MaxLength {r.MaxLength:G4}: {r.MaxDrift <= r.MaxLength}, NotCleanReason '{MeshRobustRepair.NotCleanReason(r.Mesh)}'");
        return 0;
    }

    // the witness: {"moves": [{"v": id, "p": [hex, hex, hex]}, ...]}
    static int OracleVerify(MeshRoundingRepair r, string witnessPath)
    {
        Type T = typeof(MeshRoundingRepair);
        DMesh3 m = r.Mesh;
        var P = new Probe(r);
        var moves = new List<(int, Vector3d)>();
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(witnessPath)))
            foreach (JsonElement e in doc.RootElement.GetProperty("moves").EnumerateArray()) {
                var p = e.GetProperty("p").EnumerateArray().Select(x => FromHex(x.GetString())).ToArray();
                moves.Add((e.GetProperty("v").GetInt32(), new Vector3d(p[0], p[1], p[2])));
            }
        int badBefore = P.Bad().Count;
        MethodInfo triangleBound = T.GetMethod("triangle_bound", AnyInstance);
        MethodInfo ghostBound = T.GetMethod("ghost_bound", AnyInstance);
        MethodInfo maxLengthOf = T.GetMethod("maxLength_of", AnyInstance);
        var ghostsAt = (IDictionary)T.GetField("ghostsAt", AnyInstance).GetValue(r);
        // (2) the configuration itself, all moves at once: the drift bound of every triangle and ghost (the library's formulas, at the
        // new positions), the degenerate triangles and the intersecting pairs of the whole mesh (brute force over boxes, the library's tests)
        MethodInfo setVertex = T.GetMethod("set_vertex", AnyInstance);
        var saved = moves.Select(x => m.GetVertex(x.Item1)).ToArray();
        foreach (var (v, p) in moves) setVertex.Invoke(r, new object[] { v, p });
        double worst = double.NegativeInfinity;
        bool driftOk = true;
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            double b = (double)triangleBound.Invoke(r, new object[] { t, m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) });
            double lim = (double)maxLengthOf.Invoke(r, new object[] { tv.a });
            worst = Math.Max(worst, b - lim);
            if (!(b <= lim)) driftOk = false;
        }
        foreach (DictionaryEntry kv in ghostsAt)
            foreach (object g in (IEnumerable)kv.Value) {
                if ((bool)g.GetType().GetField("lost").GetValue(g)) continue;
                double b = (double)ghostBound.Invoke(r, new object[] { g, -1, -1, Vector3d.Zero });
                double lim = (double)maxLengthOf.Invoke(r, new object[] { (int)kv.Key });
                worst = Math.Max(worst, b - lim);
                if (!(b <= lim)) driftOk = false;
            }
        int bad = P.Bad().Count;
        var boxes = new AxisAlignedBox3d[m.MaxTriangleID];
        foreach (int tid in m.TriangleIndices()) boxes[tid] = P.Box(tid);
        int pairs = 0;
        var tids = m.TriangleIndices().ToList();
        for (int i = 0; i < tids.Count; ++i)
            for (int j = i + 1; j < tids.Count; ++j)
                if (boxes[tids[i]].Intersects(boxes[tids[j]]) && P.Intersects(tids[i], tids[j])) {
                    if (pairs < 5) Console.WriteLine($"  intersecting pair {tids[i]} {tids[j]}");
                    pairs++;
                }
        // (the orientation rule of improves, for information: triangles of the moved stars above OrientedArea times the size area that turn over)
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "VERIFY moves {0}: degenerate {1} -> {2}, intersecting pairs {3}, drift within the bound {4} (largest bound minus limit {5:G4})",
            moves.Count, badBefore, bad, pairs, driftOk, worst));
        // (restore the state the repair left; the drift bookkeeping was not touched)
        for (int k = moves.Count - 1; k >= 0; --k) setVertex.Invoke(r, new object[] { moves[k].Item1, saved[k] });
        Console.WriteLine($"evaluation limit reached {r.EvaluationLimitReached}");
        // (1) for two moved vertices: the library's own joint edit (improves: counts, orientation rule, drift, shared positions)
        if (moves.Count == 2) {
            MethodInfo pair = T.GetMethod("try_move_pair", AnyInstance);
            var save = moves.Select(x => m.GetVertex(x.Item1)).ToArray();
            bool ok = (bool)pair.Invoke(r, new object[] { moves[0].Item1, moves[0].Item2, moves[1].Item1, moves[1].Item2 });
            Console.WriteLine($"try_move_pair accepted {ok}");
            if (ok) {
                // undo (the check below starts from the state the repair left)
                MethodInfo setV = T.GetMethod("set_vertex", AnyInstance);
                for (int k = 0; k < 2; ++k) setV.Invoke(r, new object[] { moves[k].Item1, save[k] });
            }
        }
        if (moves.Count == 1) {
            MethodInfo one = T.GetMethod("try_move", AnyInstance);
            Vector3d save = m.GetVertex(moves[0].Item1);
            bool ok = (bool)one.Invoke(r, new object[] { moves[0].Item1, moves[0].Item2, false });
            Console.WriteLine($"try_move accepted {ok}");
            if (ok) T.GetMethod("set_vertex", AnyInstance).Invoke(r, new object[] { moves[0].Item1, save });
        }
        return 0;
    }
}
