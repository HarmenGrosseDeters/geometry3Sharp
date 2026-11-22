// Tie-order distribution of the improver of stage 2 (docs/research/TET_TIE_ORDER_2026-11.md; the spike of
// docs/research/CROSS_FIELD_TRANSFER_2026-11.md section 5.2). Per input, stage 1 once (Improve = false), then the improver from that
// result for order 0 (the stage-1 mesh as it is: today's order) and for orders 1 .. K, each a deterministic relabeling of the
// stage-1 mesh: the vertex ids permuted, the tets and the boundary triangles shuffled, the corners of every tet in a random even
// permutation (the orientation stays) and of every boundary triangle rotated. The geometry is bit for bit the same; what changes is every
// choice the improver makes by index or list position: the order of the 6 edges, 4 faces and 4 vertices it tries on a bad tet (the first
// operation that helps is kept), the order of bad tets and of contraction / split edges of equal key, the order of a vertex's
// star (sums over it round differently). No library change: the switch is the relabeling.
// Effort is counted in visits (TetMeshImprover.Visits), never in time. The improver's CancelF is the probe: it is called before every
// visit, never cancels, and samples (visits, tets below the floor, worst dihedral) every <vertices> / 32 visits.
// Usage: ... -- tieorder <mesh|proc> <relativeAlpha> <relativeOffset> <floor> [orders = 8] [trace.csv] [nohdr]
//        (G3_TIE_PARTS = a subset of "vtcb" limits the relabeling to vertex ids / tet order / corner order / boundary triangles)
//        ... -- tieorder check        (self-check on procedural inputs; exit code 0 = passed)
// imp_s is informative only (it includes the probe and the build of the input's tree). The improver runs with the defaults of
// MeshTetrahedralization (no work cap, no MaxDihedralDegrees): order 0 is the library's result only then.
// Rows: TieOrder.Header; trace rows: name,floor,order,visits,below_floor,worst_dihedral,phase. Summary: compare/summarize_tie_order.py.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

static class TieOrder
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string Header = "name,alpha,floor,order,status,vertices_in,tets_in,below_floor_in,visits_total,visits_at_rescue,passes,rescue_passes,tets_out,below_floor,worst_dihedral,valid,digest,imp_s";

    public sealed class Outcome
    {
        public TetMesh3 Result;
        public long Visits, VisitsAtRescue = -1;
        public Dictionary<string, long> Counts;
        public int Below; public double Worst;
        public List<(long visits, int below, double worst, string phase)> Trace = new List<(long, int, double, string)>();
        public double Seconds;
    }

    public static int Run(string[] args, IEnumerable<KeyValuePair<string, DMesh3>> procedural)
    {
        if (args.Length > 1 && args[1] == "check") return Check(procedural);
        if (args.Length < 5) { Console.Error.WriteLine("usage: tieorder <mesh|proc> <relativeAlpha> <relativeOffset> <floor> [orders] [trace.csv] [nohdr]"); return 1; }
        string src = args[1];
        double alpha = double.Parse(args[2], Inv), offset = double.Parse(args[3], Inv), floor = double.Parse(args[4], Inv);
        int orders = args.Length > 5 && args[5] != "nohdr" ? int.Parse(args[5], Inv) : 8;
        string tracePath = args.Length > 6 && args[6] != "nohdr" ? args[6] : null;
        if (Parts.Length == 0 || Parts.Any(c => "vtcb".IndexOf(c) < 0)) { Console.Error.WriteLine("G3_TIE_PARTS must be a non-empty subset of vtcb (it is \"" + Parts + "\")"); return 1; }
        var inputs = src == "proc" ? procedural.ToList()
            : new List<KeyValuePair<string, DMesh3>> { new KeyValuePair<string, DMesh3>(Path.GetFileName(src), StandardMeshReader.ReadMesh(src)) };
        if (args[args.Length - 1] != "nohdr") Console.WriteLine(Header);
        foreach (var kv in inputs) {
            string head = kv.Key + "," + alpha.ToString("G5", Inv) + "," + floor.ToString("G5", Inv);
            var tm = Stage1(kv.Value, alpha, offset, floor);
            if (tm.Result == null) { Console.WriteLine(head + ",-1," + tm.Status); continue; }
            int belowIn = Measure(tm.Result, floor, out _);
            for (int order = 0; order <= orders; ++order) {
                Outcome o = Improve(tm, kv.Value, floor, order, true);
                string valid = TetMeshQuality.NotValidReason(o.Result) == null ? "yes" : "NO";
                Console.WriteLine(string.Join(",", head, order, tm.Status, tm.Result.Vertices.Count, tm.Result.Tets.Count, belowIn, o.Visits, o.VisitsAtRescue,
                    o.Counts["passes"], o.Counts.TryGetValue("rescue_passes", out long rp) ? rp : 0, o.Result.Tets.Count, o.Below, o.Worst.ToString("R", Inv), valid,
                    Digest(o.Result).ToString("x16", Inv), o.Seconds.ToString("G4", Inv)));
                Console.Out.Flush();
                if (tracePath != null)
                    File.AppendAllLines(tracePath, o.Trace.Select(s => string.Join(",", kv.Key, floor.ToString("G5", Inv), order, s.visits, s.below, s.worst.ToString("R", Inv), s.phase)));
            }
        }
        return 0;
    }

    public static MeshTetrahedralization Stage1(DMesh3 input, double alpha, double offset, double floor)
    {
        var tm = new MeshTetrahedralization(input) { RelativeAlpha = alpha, RelativeOffset = offset, SliverDihedralDegrees = floor, Improve = false };
        tm.Compute();
        return tm;
    }

    /// <summary> the improver as MeshTetrahedralization.Compute sets it up (its defaults: no work cap, no MaxDihedralDegrees), on order's relabeling of the stage-1 mesh </summary>
    public static Outcome Improve(MeshTetrahedralization tm, DMesh3 input, double floor, int order, bool trace)
    {
        TetMesh3 start = order == 0 ? tm.Result : Relabel(tm.Result, order);
        var o = new Outcome();
        var sw = Stopwatch.StartNew();
        var imp = new TetMeshImprover(start) { TargetDegrees = floor };
        if (tm.BoundarySlideFactor > 0 && input.TriangleCount > 0) {
            imp.InputTree = new DMeshAABBTree3(input, true);
            imp.BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset;
            imp.TangentOnly = tm.BoundaryMovesTangentOnly;
        }
        imp.MaxPasses = tm.ImprovePasses; imp.RescuePasses = tm.RescuePasses;
        imp.Contraction = tm.ContractEdges; imp.Insertion = tm.SplitEdges;
        long step = Math.Max(1, start.Vertices.Count / 32), next = 0;
        imp.CancelF = () => {
            long v = imp.Visits;
            if (imp.ActiveSet && o.VisitsAtRescue < 0) o.VisitsAtRescue = v;
            if (trace && v >= next) {
                int b = Measure(imp.Result(), floor, out double w);
                o.Trace.Add((v, b, w, imp.ActiveSet ? "rescue" : "main"));
                next = v + step;
            }
            return false;
        };
        imp.Run();
        o.Seconds = sw.Elapsed.TotalSeconds;
        o.Result = imp.Result();
        o.Visits = imp.Visits;
        o.Counts = new Dictionary<string, long>(imp.Counts);
        o.Below = Measure(o.Result, floor, out o.Worst);
        o.Trace.Add((o.Visits, o.Below, o.Worst, "end"));
        return o;
    }

    /// <summary> the tets below the floor and the worst smallest dihedral angle, by the improver's own quality </summary>
    public static int Measure(TetMesh3 m, double floor, out double worst)
    {
        int below = 0; worst = 180;
        foreach (Index4i t in m.Tets) {
            double q = TetMeshImprover.Quality(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c], m.Vertices[t.d]);
            if (q < floor) below++;
            if (q < worst) worst = q;
        }
        return below;
    }

    // SplitMix64 (Steele, Lea, Flood 2014; the public-domain constants): the same sequence on every runtime
    sealed class Rng
    {
        ulong s;
        public Rng(ulong seed) { s = seed; }
        public ulong Next() { ulong z = (s += 0x9E3779B97F4A7C15UL); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
        /// <summary> uniform in [0, n) (rejection: no modulo bias) </summary>
        public int Below(int n) { ulong lim = ulong.MaxValue - ulong.MaxValue % (ulong)n; ulong x; do x = Next(); while (x >= lim); return (int)(x % (ulong)n); }
        public void Shuffle<T>(IList<T> a) { for (int i = a.Count - 1; i > 0; --i) { int j = Below(i + 1); (a[i], a[j]) = (a[j], a[i]); } }
    }

    // the 12 even permutations of 4 corners
    static readonly int[][] Even = {
        new[] { 0, 1, 2, 3 }, new[] { 0, 2, 3, 1 }, new[] { 0, 3, 1, 2 }, new[] { 1, 0, 3, 2 }, new[] { 1, 2, 0, 3 }, new[] { 1, 3, 2, 0 },
        new[] { 2, 0, 1, 3 }, new[] { 2, 1, 3, 0 }, new[] { 2, 3, 0, 1 }, new[] { 3, 0, 2, 1 }, new[] { 3, 1, 0, 2 }, new[] { 3, 2, 1, 0 } };

    /// <summary> the same tet mesh with other labels: vertex ids permuted, tets and boundary triangles shuffled, corners in an even permutation / rotated. order >= 1 </summary>
    public static TetMesh3 Relabel(TetMesh3 m, int order) { return Relabel(m, order, Parts); }

    /// <summary>
    /// the parts of the relabeling that are on, from G3_TIE_PARTS (default "vtcb" = all): v the vertex ids, t the order of the tets,
    /// c the corner order within a tet, b the boundary triangles (order and rotation). For the diagnosis of which part acts
    /// </summary>
    static readonly string Parts = Environment.GetEnvironmentVariable("G3_TIE_PARTS") ?? "vtcb";

    public static TetMesh3 Relabel(TetMesh3 m, int order, string parts)
    {
        var rng = new Rng(0x7E7011E0D0000000UL + (ulong)order);
        bool pv = parts.Contains('v'), pt = parts.Contains('t'), pc = parts.Contains('c'), pb = parts.Contains('b');
        int nv = m.Vertices.Count;
        var perm = new int[nv];
        for (int i = 0; i < nv; ++i) perm[i] = i;
        if (pv) rng.Shuffle(perm);                          // old id i -> new id perm[i]
        var r = new TetMesh3();
        var verts = new Vector3d[nv];
        for (int i = 0; i < nv; ++i) verts[perm[i]] = m.Vertices[i];
        r.Vertices.AddRange(verts);
        foreach (Index4i t in m.Tets) {
            int[] e = Even[pc ? rng.Below(12) : 0];
            r.Tets.Add(new Index4i(perm[t[e[0]]], perm[t[e[1]]], perm[t[e[2]]], perm[t[e[3]]]));
        }
        if (pt) rng.Shuffle(r.Tets);
        foreach (Index3i f in m.BoundaryTriangles) {
            int k = pb ? rng.Below(3) : 0;
            r.BoundaryTriangles.Add(new Index3i(perm[f[k]], perm[f[(k + 1) % 3]], perm[f[(k + 2) % 3]]));
        }
        if (pb) rng.Shuffle(r.BoundaryTriangles);
        return r;
    }

    /// <summary> FNV-1a over the result as it is labelled (positions bit for bit, tets, boundary triangles) </summary>
    public static ulong Digest(TetMesh3 m)
    {
        ulong h = 14695981039346656037UL;
        void add(ulong x) { for (int i = 0; i < 8; ++i) { h ^= (x >> (8 * i)) & 0xff; h *= 1099511628211UL; } }
        foreach (Vector3d v in m.Vertices) { add((ulong)BitConverter.DoubleToInt64Bits(v.x)); add((ulong)BitConverter.DoubleToInt64Bits(v.y)); add((ulong)BitConverter.DoubleToInt64Bits(v.z)); }
        foreach (Index4i t in m.Tets) { add((ulong)t.a); add((ulong)t.b); add((ulong)t.c); add((ulong)t.d); }
        foreach (Index3i f in m.BoundaryTriangles) { add((ulong)f.a); add((ulong)f.b); add((ulong)f.c); }
        return h;
    }

    // a label-free form of a tet mesh: every tet and boundary triangle as its corner positions, corners and items sorted
    static List<string> Canonical(TetMesh3 m)
    {
        string p(int v) => BitConverter.DoubleToInt64Bits(m.Vertices[v].x).ToString("x16", Inv) + BitConverter.DoubleToInt64Bits(m.Vertices[v].y).ToString("x16", Inv) + BitConverter.DoubleToInt64Bits(m.Vertices[v].z).ToString("x16", Inv);
        var l = new List<string>();
        foreach (Index4i t in m.Tets) { var c = new[] { p(t.a), p(t.b), p(t.c), p(t.d) }; Array.Sort(c, string.CompareOrdinal); l.Add("T" + string.Join("|", c)); }
        foreach (Index3i f in m.BoundaryTriangles) { var c = new[] { p(f.a), p(f.b), p(f.c) }; Array.Sort(c, string.CompareOrdinal); l.Add("F" + string.Join("|", c)); }
        l.Sort(string.CompareOrdinal);
        return l;
    }

    /// <summary>
    /// self-check on procedural inputs (alpha / 20, floor 20: the torus and the cylinder give the improver work; none misses the floor,
    /// so the rescue phase is not covered here): order 0 of the harness is the library
    /// (MeshTetrahedralization with Improve = true gives the same result bit for bit and the same counts); a relabeling keeps the mesh
    /// (the same tets and boundary triangles by position, every tet exactly positive, and it is not the identity); an order is
    /// deterministic (twice the same digest and visits); the probe does not change the run (with and without the trace); every result is valid
    /// </summary>
    static int Check(IEnumerable<KeyValuePair<string, DMesh3>> procedural)
    {
        int failed = 0, differ = 0, runs = 0;
        void expect(bool ok, string what) { if (!ok) { failed++; Console.WriteLine("FAILED: " + what); } }
        foreach (var kv in procedural.Where(k => k.Key == "sphere n=16" || k.Key == "torus" || k.Key == "cylinder")) {
            const double alpha = 20, floor = 20;
            var tm = Stage1(kv.Value, alpha, 600, floor);
            expect(tm.Result != null, kv.Key + ": stage 1 has a result");
            if (tm.Result == null) continue;
            var lib = new MeshTetrahedralization(kv.Value) { RelativeAlpha = alpha, RelativeOffset = 600, SliverDihedralDegrees = floor };
            lib.Compute();
            Outcome o0 = Improve(tm, kv.Value, floor, 0, true);
            expect(lib.Result != null && Digest(lib.Result) == Digest(o0.Result), kv.Key + ": order 0 is the library's result bit for bit");
            foreach (var c in o0.Counts) expect(lib.Counts.TryGetValue("improve_" + c.Key, out long lc) && lc == c.Value, kv.Key + ": count " + c.Key + " as in the library");
            expect(o0.Visits > 0, kv.Key + ": the improver has work (visits > 0)");
            expect(Digest(Improve(tm, kv.Value, floor, 0, false).Result) == Digest(o0.Result), kv.Key + ": the probe does not change the run");
            List<string> canon = Canonical(tm.Result);
            for (int order = 1; order <= 3; ++order) {
                TetMesh3 rl = Relabel(tm.Result, order, "vtcb");
                expect(Digest(Relabel(tm.Result, order, "")) == Digest(tm.Result), kv.Key + " order " + order + ": with every part off the relabeling is the identity");
                foreach (string part in new[] { "v", "t", "c", "b" }) {
                    TetMesh3 one = Relabel(tm.Result, order, part);
                    expect(Canonical(one).SequenceEqual(canon) && Digest(one) != Digest(tm.Result), kv.Key + " order " + order + ": part " + part + " alone keeps the mesh and changes the labels");
                }
                expect(Canonical(rl).SequenceEqual(canon), kv.Key + " order " + order + ": the relabeling keeps the tets and boundary triangles");
                expect(Digest(rl) != Digest(tm.Result), kv.Key + " order " + order + ": the relabeling is not the identity");
                expect(TetMeshQuality.NotValidReason(rl) == null, kv.Key + " order " + order + ": the relabelled mesh is valid");
                Outcome a = Improve(tm, kv.Value, floor, order, true), b = Improve(tm, kv.Value, floor, order, false);
                expect(Digest(a.Result) == Digest(b.Result) && a.Visits == b.Visits, kv.Key + " order " + order + ": deterministic, with and without the probe");
                expect(TetMeshQuality.NotValidReason(a.Result) == null, kv.Key + " order " + order + ": the result is valid");
                expect(a.Trace.Count > 1 && a.Trace[a.Trace.Count - 1].visits == a.Visits && a.Trace[a.Trace.Count - 1].below == a.Below, kv.Key + " order " + order + ": the trace ends at the result");
                for (int i = 1; i < a.Trace.Count; ++i) expect(a.Trace[i].visits >= a.Trace[i - 1].visits, kv.Key + " order " + order + ": visits do not decrease along the trace");
                runs++;
                if (a.Visits != o0.Visits || a.Below != o0.Below || a.Worst != o0.Worst) differ++;
                Console.WriteLine(string.Join(",", kv.Key, order, "visits=" + a.Visits + " (order 0: " + o0.Visits + ")", "below=" + a.Below + " (" + o0.Below + ")", "worst=" + a.Worst.ToString("F3", Inv) + " (" + o0.Worst.ToString("F3", Inv) + ")"));
            }
        }
        // the switch must act: a relabeling that never changed the improver's run would measure nothing
        expect(differ > 0, "at least one relabeling changes the improver's run (" + differ + " of " + runs + ")");
        Console.WriteLine(failed == 0 ? "tieorder check: passed" : "tieorder check: " + failed + " FAILED");
        return failed == 0 ? 0 : 3;
    }
}
