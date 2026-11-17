// Exact Boolean 4.0 stage 0 (docs/design/EXACT_BOOLEAN_40_DESIGN.md): the upper bound of keeping exact intermediate results in a
// chain of booleans, measured without a 4.0 build. A chain R1 = X0 op1 X1, R2 = R1 op2 X2, ... is computed two ways:
//   chained:  today, each step one ExactMeshBoolean on the previous step's rounded result (SafeRounding at every step);
//   onepass:  step k as one ExactMeshBoolean over the original operands X0..Xk with the composed winding predicate. Regularized
//             booleans act cell by cell on the arrangement of all operands, so this is the solid an exact chain would reach
//             (exact intermediates, one rounding at the end); its unrounded vertices are the exact chain result's vertices,
//             all constructions over the original doubles.
// Per step one JSON line: time, triangles, vertices and cleanness of both; the exact vertices' bit lengths of the one pass
// against the value bounds of the design note (section 2), from the operands' grid bits B.
//   chain-exact <A.off> <B.off> <C.off> <ucd|dui|iud>      the abc-chained plans (data/abc_chained.py, PLANS)
//   chain-exact-proc <mixed|generic> <seed> <steps>        procedural chains (no data), 1..8 steps
// Benchmark code only: nothing here is part of the library.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using BoolCmp;
using g3;
using gs;

static class ChainExact
{
    // a step: op, left (-1 = the previous result), right (an operand index)
    readonly struct Step
    {
        public readonly ExactMeshBoolean.Operations Op;
        public readonly int Left, Right;
        public Step(ExactMeshBoolean.Operations op, int left, int right) { Op = op; Left = left; Right = right; }
    }

    static readonly Dictionary<string, ExactMeshBoolean.Operations> opOf = new Dictionary<string, ExactMeshBoolean.Operations> {
        ["union"] = ExactMeshBoolean.Operations.Union, ["difference"] = ExactMeshBoolean.Operations.Difference,
        ["intersection"] = ExactMeshBoolean.Operations.Intersection,
    };
    static string Name(ExactMeshBoolean.Operations op) { return opOf.First(kv => kv.Value == op).Key; }

    // the plans of data/abc_chained.py (A = 0, B = 1, C = 2, R = -1)
    static List<Step> AbcPlan(string variant)
    {
        var U = ExactMeshBoolean.Operations.Union; var D = ExactMeshBoolean.Operations.Difference; var I = ExactMeshBoolean.Operations.Intersection;
        switch (variant) {
            case "ucd": return new List<Step> { new Step(U, 0, 1), new Step(D, -1, 2), new Step(I, -1, 0) };
            case "dui": return new List<Step> { new Step(D, 0, 1), new Step(U, -1, 2), new Step(I, -1, 0) };
            case "iud": return new List<Step> { new Step(I, 0, 1), new Step(U, -1, 2), new Step(D, -1, 0) };
        }
        throw new ArgumentException("variant: ucd, dui or iud");
    }

    public static int Abc(string[] args)
    {
        var ops = new List<DMesh3> { Program.Load(args[0]), Program.Load(args[1]), Program.Load(args[2]) };
        if (ops.Any(m => m == null)) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        return RunChain(ops, AbcPlan(args[3]), new Dictionary<string, object> { ["family"] = "abc", ["variant"] = args[3] });
    }

    // ------------------------------------------------------------------ procedural chains

    static DMesh3 Shape(int k, Random r)
    {
        DMesh3 m;
        switch (k % 3) {
            case 0: m = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh(); break;
            case 1: m = new CappedCylinderGenerator { BaseRadius = 1, TopRadius = 0.7f, Height = 2, Slices = 24 }.Generate().MakeDMesh(); break;
            default: m = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.4)), EdgeVertices = 4 }.Generate().MakeDMesh(); break;
        }
        Vector3d axis = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5).Normalized;
        MeshTransforms.Rotate(m, Vector3d.Zero, new Quaterniond(axis, 360 * r.NextDouble()));
        // float32 per coordinate (as the CAD models of abc-chained): many coordinates share low bits, ties are likelier
        foreach (int vid in m.VertexIndices()) {
            Vector3d v = m.GetVertex(vid);
            m.SetVertex(vid, new Vector3d((float)v.x, (float)v.y, (float)v.z));
        }
        return m;
    }

    // mixed: exact contact placements against X0 (ContactTransforms), cycling partial-overlap, ulp-shift (a copy of X0 one ulp
    //   off), face-contact and mirror; ops cycle union, difference, union, intersection (the near-coincident inputs of RC4).
    // generic: shapes at random rotations and offsets (no exact contacts), ops cycle union, union, difference, intersection.
    public static int Procedural(string[] args)
    {
        string family = args[0];
        int seed = int.Parse(args[1]), steps = int.Parse(args[2]);
        if ((family != "mixed" && family != "generic") || steps < 1 || steps > 8)
            throw new ArgumentException("chain-exact-proc <mixed|generic> <seed> <steps 1..8>");
        var r = new Random(seed);
        var ops = new List<DMesh3>();
        var plan = new List<Step>();
        var info = new Dictionary<string, object> { ["family"] = family, ["seed"] = seed };
        var U = ExactMeshBoolean.Operations.Union; var D = ExactMeshBoolean.Operations.Difference; var I = ExactMeshBoolean.Operations.Intersection;
        DMesh3 x0 = Shape(seed, r);
        if (!ContactTransforms.NormalizeExact(x0))
            throw new Exception("X0 cannot be normalized exactly");
        ops.Add(x0);
        var placements = new List<string>();
        for (int i = 1; i <= steps; ++i) {
            DMesh3 xi;
            ExactMeshBoolean.Operations op;
            if (family == "mixed") {
                var variants = new[] { ContactVariant.PartialOverlap, ContactVariant.UlpShift, ContactVariant.FaceContact, ContactVariant.Mirror };
                ContactVariant v = variants[(i - 1) % 4];
                ContactResult c = ContactTransforms.Build(x0, Shape(seed + i, r), v, r.Next(3), r.Next(2) == 0 ? 1 : -1, 1);
                if (c.Error != null)
                    throw new Exception($"placement {i}: {c.Error}");
                if (!ContactTransforms.SameVertices(c.A, x0))
                    throw new Exception("the frame of X0 changed");
                xi = c.B;
                placements.Add(ContactTransforms.VariantNames[(int)v]);
                op = new[] { U, D, U, I }[(i - 1) % 4];
            } else {
                xi = Shape(seed + i, r);
                MeshTransforms.Scale(xi, 0.5 + 0.5 * r.NextDouble());
                MeshTransforms.Translate(xi, new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * 0.8);
                placements.Add("random");
                op = new[] { U, U, D, I }[(i - 1) % 4];
            }
            ops.Add(xi);
            plan.Add(new Step(op, i == 1 ? 0 : -1, i));
        }
        info["placements"] = placements;
        return RunChain(ops, plan, info);
    }

    // ------------------------------------------------------------------ the two chains

    static Func<int[], bool> Predicate(List<Step> plan, int k, Dictionary<int, int> slot)
    {
        // the indicator of R_k over the winding numbers of the operands (slot: operand -> entry of the winding vector)
        Func<int[], bool> leaf(int x) { int s = slot[x]; return w => w[s] > 0; }
        Func<int[], bool> f = null;
        for (int i = 0; i <= k; ++i) {
            Func<int[], bool> l = plan[i].Left < 0 ? f : leaf(plan[i].Left), rr = leaf(plan[i].Right);
            switch (plan[i].Op) {
                case ExactMeshBoolean.Operations.Union: { var a = l; var b = rr; f = w => a(w) || b(w); break; }
                case ExactMeshBoolean.Operations.Intersection: { var a = l; var b = rr; f = w => a(w) && b(w); break; }
                default: { var a = l; var b = rr; f = w => a(w) && !b(w); break; }
            }
        }
        return f;
    }

    static int RunChain(List<DMesh3> ops, List<Step> plan, Dictionary<string, object> info)
    {
        int B = GridBits(ops, out int scaleExp);
        DMesh3 prev = null;
        bool chainAlive = true;
        for (int k = 0; k < plan.Count; ++k) {
            var row = new Dictionary<string, object>(info) { ["step"] = k + 1, ["op"] = Name(plan[k].Op), ["grid_bits"] = B, ["grid_exp"] = scaleExp };
            // chained (today): the previous rounded result op the operand
            if (chainAlive) {
                DMesh3 left = plan[k].Left < 0 ? prev : ops[plan[k].Left];
                var b = new ExactMeshBoolean(left, ops[plan[k].Right], plan[k].Op);
                var sw = Stopwatch.StartNew();
                bool ok = b.Compute();
                row["chained_ms"] = sw.ElapsedMilliseconds;
                Outcome(b, ok, "chained", row);
                if (ok && b.Result.TriangleCount > 0) prev = b.Result;
                else chainAlive = false;
            }
            // one pass over the original operands X0..Xk: exact intermediates, one rounding
            var used = new SortedSet<int>();
            for (int i = 0; i <= k; ++i) { if (plan[i].Left >= 0) used.Add(plan[i].Left); used.Add(plan[i].Right); }
            var slot = new Dictionary<int, int>();
            var meshes = new List<DMesh3>();
            foreach (int x in used) { slot[x] = meshes.Count; meshes.Add(ops[x]); }
            Func<int[], bool> pred = Predicate(plan, k, slot);
            {
                var b = new ExactMeshBoolean(meshes, pred);
                var sw = Stopwatch.StartNew();
                bool ok = b.Compute();
                row["onepass_ms"] = sw.ElapsedMilliseconds;
                Outcome(b, ok, "onepass", row);
            }
            {
                // the exact chain result: unrounded, with its exact vertices
                var b = new ExactMeshBoolean(meshes, pred) { SafeRounding = false, ComputeExactVertices = true };
                var sw = Stopwatch.StartNew();
                bool ok = b.Compute();
                row["exact_ms"] = sw.ElapsedMilliseconds;
                row["exact_ok"] = ok;
                if (ok) {
                    row["exact_tris"] = b.Result.TriangleCount;
                    row["exact_verts"] = b.Result.VertexCount;
                    row["exact_rounded_to_nearest_reason"] = MeshRobustRepair.NotCleanReason(b.Result, false);
                    Bits(b.ResultExactVertices, B, scaleExp, row);
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }

    static void Outcome(ExactMeshBoolean b, bool ok, string pre, Dictionary<string, object> row)
    {
        row[pre + "_ok"] = ok;
        if (!ok) { row[pre + "_error"] = b.Error; return; }
        DMesh3 m = b.Result;
        row[pre + "_tris"] = m.TriangleCount;
        row[pre + "_verts"] = m.VertexCount;
        row[pre + "_rounding_ms"] = b.PhaseMilliseconds.TryGetValue("rounding", out long rms) ? rms : 0;
        row[pre + "_rounding_clean"] = b.RoundingClean;
        row[pre + "_removed_components"] = b.Rounding?.RemovedComponents ?? 0;
        row[pre + "_evaluation_limit"] = b.Rounding?.EvaluationLimitReached ?? false;
        row[pre + "_max_drift"] = b.Rounding?.MaxDrift ?? 0;
        row[pre + "_contacts"] = b.Contacts;
        // NotCleanReason without its self-intersection test (as the eval; split pinches touch at duplicated positions) and with it
        row[pre + "_reason"] = MeshRobustRepair.NotCleanReason(m, false);
        row[pre + "_reason_full"] = MeshRobustRepair.NotCleanReason(m, true);
        row[pre + "_volume"] = m.TriangleCount > 0 ? MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x : 0.0;
    }

    // ------------------------------------------------------------------ bit lengths against the bounds

    // B: the bits of the largest |coordinate| on the operands' common grid (coordinate = integer * 2^s, s the lowest set bit of all
    // coordinates), uncentered: the value bounds of the note hold for these integers, whatever grid offset the arrangement uses.
    static int GridBits(List<DMesh3> ops, out int s)
    {
        s = int.MaxValue;
        double maxAbs = 0;
        foreach (DMesh3 m in ops)
            foreach (Vector3d v in m.Vertices())
                for (int k = 0; k < 3; ++k) {
                    if (v[k] == 0) continue;
                    s = Math.Min(s, LowestExponent(v[k]));
                    maxAbs = Math.Max(maxAbs, Math.Abs(v[k]));
                }
        if (s == int.MaxValue) { s = 0; return 0; }
        // |x| / 2^s < 2^B
        return ContactTransforms.ILog2(maxAbs) + 1 - s;
    }

    static int LowestExponent(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        int e = (int)((bits >> 52) & 0x7FF);
        long mant = bits & 0xFFFFFFFFFFFFFL;
        if (e != 0) mant |= 1L << 52; else e = 1;
        int tz = 0;
        while ((mant & 1) == 0) { mant >>= 1; tz++; }
        return e - 1075 + tz;
    }

    static int BitLen(BigInteger v) { return v.IsZero ? 0 : (int)BigInteger.Abs(v).GetBitLength(); }

    static bool IsDouble(BigInteger x, BigInteger w)
    {
        if (x.IsZero) return true;
        if ((w & (w - 1)).IsZero == false) return false;
        BigInteger ax = BigInteger.Abs(x);
        while (ax.IsEven) ax >>= 1;
        return ax.GetBitLength() <= 53;
    }

    // The canonical (reduced) value of a point on the grid has at most the bits of any representation of it, so a vertex
    // of value class c obeys num <= numBound(c) + max(0, s), W <= wBound(c) + max(0, -s) in input coordinates (x = grid * 2^s).
    // Classes (design note, section 2, B the grid bits): input vertex; LPI of an input edge and a face plane (4B+8 / 3B+6);
    // TPI of three face planes (7B+14 / 6B+12); TPI with edge planes (up to 12B+22 / 9B+18).
    static void Bits(ExactPoint3[] v, int B, int s, Dictionary<string, object> row)
    {
        int maxNum = 0, maxW = 0, doubles = 0, inLpi = 0, inTpi = 0, inEdgeTpi = 0, beyond = 0;
        long sumBits = 0;
        int ns = Math.Max(0, s), ws = Math.Max(0, -s);
        foreach (ExactPoint3 p in v) {
            int nb = Math.Max(BitLen(p.X), Math.Max(BitLen(p.Y), BitLen(p.Z))), wb = BitLen(p.W);
            maxNum = Math.Max(maxNum, nb); maxW = Math.Max(maxW, wb);
            sumBits += BitLen(p.X) + BitLen(p.Y) + BitLen(p.Z) + wb;
            if (IsDouble(p.X, p.W) && IsDouble(p.Y, p.W) && IsDouble(p.Z, p.W)) doubles++;
            else if (nb <= 4 * B + 8 + ns && wb <= 3 * B + 6 + ws) inLpi++;
            else if (nb <= 7 * B + 14 + ns && wb <= 6 * B + 12 + ws) inTpi++;
            else if (nb <= 12 * B + 22 + ns && wb <= 9 * B + 18 + ws) inEdgeTpi++;
            else beyond++;
        }
        row["exact_double_vertices"] = doubles;
        row["exact_within_lpi_bound"] = inLpi;
        row["exact_within_tpi_bound"] = inTpi;
        row["exact_within_edge_tpi_bound"] = inEdgeTpi;
        row["exact_beyond_bounds"] = beyond;
        row["exact_max_num_bits"] = maxNum;
        row["exact_max_w_bits"] = maxW;
        row["exact_mean_bits_per_vertex"] = v.Length > 0 ? Math.Round(sumBits / (double)v.Length, 1) : 0;
    }
}
