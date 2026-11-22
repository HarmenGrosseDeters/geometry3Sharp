// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Research spike (docs/research/ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md): a whole reduction driven by the exact
// polyhedral envelope of Wang et al. 2020 (PolyhedralEnvelope.ContainsTriangle, internal) as the collapse check, against
// the current Reducer (MaxDeviation: TriangleDeviationBounds.IsWithin). A prototype in a Reducer subclass, not library
// code: the check runs before base.CollapseEdge, so the control "hook + IsWithin" shows what the hook's position costs
// (its result must equal the built-in MaxDeviation reduction bit for bit).
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope [bracket|full|full0|psi] [case filter]
//     bracket: only the upper-bound measurement (the built-in Reducer at eps and at 0.9 eps / sqrt 3, which bracket what
//              the envelope can reach), no envelope run
//     full:    bracket, the envelope reduction, the control, the per-query replay (rows also on stderr as they finish);
//              full0 adds the envelope reduction without the point samples
//     psi:     the guaranteed pipeline (PreventSelfIntersections on), closed meshes at 0.003
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static class EnvelopeSimplification
{
    /// <summary> a Reducer whose deviation check is a callback, asked before base.CollapseEdge for the triangles the collapse creates </summary>
    sealed class HookReducer : Reducer
    {
        public Func<Vector3d, bool> PointCheck;                         // the new vertex first (cheap reject)
        public Func<Vector3d, Vector3d, Vector3d, bool> SampleCheck;    // optional cheap reject per triangle
        public Func<Vector3d, Vector3d, Vector3d, bool> Check;          // the certificate per triangle
        public long Collapses, Asked, Calls, Moved;
        public List<Triangle3d> Log = new List<Triangle3d>();
        public int LogEvery = 1;

        public HookReducer(DMesh3 m) : base(m) { }

        protected override ProcessResult CollapseEdge(int edgeID, Vector3d vNewPos, out int collapseToV)
        {
            collapseToV = DMesh3.InvalidID;
            int a = 0, b = 0, t0 = 0, t1 = 0;
            if (mesh.GetEdge(edgeID, ref a, ref b, ref t0, ref t1) == false)
                return ProcessResult.Failed_NotAnEdge;
            // the position base.CollapseEdge will use (no constraints, no target: only the boundary rule moves it)
            Vector3d pos = vNewPos, vA = mesh.GetVertex(a), vB = mesh.GetVertex(b);
            if (PreserveBoundaryShape && HaveBoundary) {
                bool ba = IsBoundaryV(a), bb = IsBoundaryV(b);
                if (ba && bb) pos = (vNewPos.DistanceSquared(vA) < vNewPos.DistanceSquared(vB)) ? vA : vB;
                else if (bb) pos = vB;
                else if (ba) pos = vA;
            }
            Asked++;
            if (within(a, b, pos) == false)
                return ProcessResult.Ignored_ExceedsDeviation;
            ProcessResult r = base.CollapseEdge(edgeID, vNewPos, out collapseToV);
            if (r == ProcessResult.Ok_Collapsed) {
                Collapses++;
                if (mesh.GetVertex(collapseToV) != pos) Moved++;       // the hook checked another position: reported
            }
            return r;
        }

        bool within(int a, int b, Vector3d newPos)
        {
            if (PointCheck(newPos) == false) return false;
            var tris = new List<Triangle3d>();
            foreach (int v in new int[] { a, b }) {
                int other = (v == a) ? b : a;
                foreach (int tid in mesh.VtxTrianglesItr(v)) {
                    Index3i t = mesh.GetTriangle(tid);
                    if (t.a == other || t.b == other || t.c == other) continue;      // removed by the collapse
                    tris.Add(new Triangle3d((t.a == v) ? newPos : mesh.GetVertex(t.a),
                        (t.b == v) ? newPos : mesh.GetVertex(t.b), (t.c == v) ? newPos : mesh.GetVertex(t.c)));
                }
            }
            if (SampleCheck != null)
                foreach (Triangle3d t in tris)
                    if (SampleCheck(t.V0, t.V1, t.V2) == false) return false;
            if ((Asked % LogEvery) == 0) Log.AddRange(tris);
            Calls += tris.Count;
            bool ok = true;
            gParallel.ForRange(tris.Count, (k) => {     // as Reducer.collapse_within_deviation: the triangles of a collapse in parallel
                if (ok && Check(tris[k].V0, tris[k].V1, tris[k].V2) == false) ok = false;
            });
            return ok;
        }
    }

    // a welded subdivided cube: flat faces, sharp edges (the CAD-like case)
    static DMesh3 Cube(int edgeVertices)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            m.SetVertex(vid, p / Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
        }
        return m;
    }

    static (double up, double low) Hausdorff(DMesh3 from, DMesh3 to, double eps)
    {
        var h = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = 0.01 * eps };
        h.Compute();
        return (h.UpperBound / eps, h.LowerBound / eps);
    }

    static int InEnvelope(PolyhedralEnvelope env, DMesh3 m)
    {
        int n = 0;
        Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
        foreach (int t in m.TriangleIndices()) {
            m.GetTriVertices(t, ref a, ref b, ref c);
            if (env.ContainsTriangle(a, b, c)) n++;
        }
        return n;
    }

    static HookReducer EnvelopeReducer(DMesh3 m, PolyhedralEnvelope env, bool samples)
    {
        var r = new HookReducer(m) { PointCheck = env.Contains, Check = env.ContainsTriangle };
        if (samples)
            r.SampleCheck = (a, b, c) => env.Contains((a + b + c) / 3) && env.Contains(0.5 * (a + b)) && env.Contains(0.5 * (b + c)) && env.Contains(0.5 * (c + a));
        return r;
    }

    // the control: the built-in check's steps (Reducer.collapse_within_deviation) behind the hook
    static HookReducer BoundsReducer(DMesh3 m, DMesh3 input, double eps)
    {
        var tree = new DMeshAABBTree3(new DMesh3(input), true);
        var bounds = new TriangleDeviationBounds(tree.Mesh);
        return new HookReducer(m) {
            PointCheck = p => tree.FindNearestTriangle(p, eps) >= 0,
            SampleCheck = (a, b, c) => tree.FindNearestTriangle((a + b + c) / 3, eps) >= 0 && tree.FindNearestTriangle(0.5 * (a + b), eps) >= 0
                && tree.FindNearestTriangle(0.5 * (b + c), eps) >= 0 && tree.FindNearestTriangle(0.5 * (c + a), eps) >= 0,
            Check = (a, b, c) => bounds.IsWithin(tree, a, b, c, eps, 10)
        };
    }

    static string Topology(DMesh3 m, bool closedInput, bool intersections)
    {
        if (closedInput == false) return m.CheckValidity(true, FailMode.ReturnOnly) ? "valid (open)" : "INVALID";
        string s = MeshRobustRepair.NotCleanReason(m, intersections);
        return string.IsNullOrEmpty(s) == false ? s : (intersections ? "clean" : "closed, manifold");
    }

    internal static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain, string[] args)
    {
        string mode = args.Length > 1 ? args[1] : "full";
        string filter = args.Length > 2 ? args[2] : "";
        var cases = new List<(string name, DMesh3 mesh)> {
            ("bumpy sphere 20", bumpy(20)), ("terrain 40", terrain(40)), ("cube 10", Cube(10)),
            ("bumpy sphere 40", bumpy(40)), ("terrain 80", terrain(80)), ("cube 20", Cube(20)) };
        cases = cases.Where(c => c.name.Contains(filter)).ToList();
        bool noSamples = mode == "full0";      // also the envelope without the point samples (slower: a second envelope run)
        if (noSamples) mode = "full";
        Console.WriteLine($"mode: {mode}, processors: {Environment.ProcessorCount}");
        if (mode == "psi") { Psi(cases); return; }

        var rows = new List<string>(); var replay = new List<string>();
        Console.WriteLine("| mesh | triangles | eps / diag | A: MaxDeviation eps: triangles, ms | C: MaxDeviation 0.9 eps / sqrt 3: triangles, ms | C / A triangles |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var (name, input) in cases)
            foreach (double rel in new[] { 1e-3, 3e-3, 1e-2 }) {
                double eps = rel * input.CachedBounds.DiagonalLength, inner = 0.9 * eps / Math.Sqrt(3);
                bool closed = input.IsClosed();
                new Reducer(new DMesh3(input)) { MaxDeviation = eps }.ReduceToTriangleCount(input.TriangleCount / 2);    // warm-up
                var mA = new DMesh3(input);
                var sw = Stopwatch.StartNew();
                new Reducer(mA) { MaxDeviation = eps }.ReduceToTriangleCount(4);
                double tA = sw.Elapsed.TotalMilliseconds;
                var mC = new DMesh3(input);
                sw.Restart();
                new Reducer(mC) { MaxDeviation = inner }.ReduceToTriangleCount(4);
                double tC = sw.Elapsed.TotalMilliseconds;
                Console.WriteLine($"| {name} | {input.TriangleCount} | {rel} | {mA.TriangleCount}, {tA:F0} | {mC.TriangleCount}, {tC:F0} | {(double)mC.TriangleCount / mA.TriangleCount:F2} |");
                if (mode != "full") continue;

                // the control: hook + IsWithin must reproduce A
                var mH = new DMesh3(input);
                sw.Restart();
                var rH = BoundsReducer(mH, input, eps);
                rH.ReduceToTriangleCount(4);
                double tH = sw.Elapsed.TotalMilliseconds;
                bool same = ReducerHashes.Hash(mH) == ReducerHashes.Hash(mA);

                // B: the paper's envelope (construction included in the time); with and without point samples
                var mB = new DMesh3(input);
                sw.Restart();
                var env = new PolyhedralEnvelope(new DMesh3(input), eps);
                double tBuild = sw.Elapsed.TotalMilliseconds;
                if (env.Status != EnvelopeStatus.Ready) throw new Exception("envelope: " + env.Status);
                var rB = EnvelopeReducer(mB, env, true);
                rB.LogEvery = 4;
                rB.ReduceToTriangleCount(4);
                double tB = sw.Elapsed.TotalMilliseconds;
                string b0 = "-";
                if (noSamples) {
                    var mB0 = new DMesh3(input);
                    sw.Restart();
                    EnvelopeReducer(mB0, env, false).ReduceToTriangleCount(4);
                    b0 = $"{mB0.TriangleCount}, {sw.Elapsed.TotalMilliseconds + tBuild:F0}";
                }

                var hA = Hausdorff(mA, input, eps); var hAr = Hausdorff(input, mA, eps);
                var hB = Hausdorff(mB, input, eps); var hBr = Hausdorff(input, mB, eps);
                int inA = InEnvelope(env, mA), inB = InEnvelope(env, mB);
                if (inB != mB.TriangleCount) throw new Exception("the envelope reduction left the envelope");
                Console.Error.WriteLine($"  B: {mB.TriangleCount} triangles, {tB:F0} ms; control {tH:F0} ms");
                rows.Add($"| {name} | {rel} | {mA.TriangleCount}, {tA:F0} | {mC.TriangleCount} | {mB.TriangleCount}, {tB:F0} (build {tBuild:F0}) | {b0} | {(double)mB.TriangleCount / mA.TriangleCount:F2} | {tB / tA:F1} | {tH:F0}, {(same ? "same" : "DIFFERENT")} | {hA.up:F2} / {hAr.up:F2} | {hB.up:F2} / {hBr.up:F2} | {inA} of {mA.TriangleCount} | {rB.Calls} / {rH.Calls} | {rB.Moved + rH.Moved} | {Topology(mA, closed, false)} / {Topology(mB, closed, false)} |");
                Console.Error.WriteLine(rows[rows.Count - 1]);
                replay.Add(Replay(name, rel, input, env, eps, rB.Log));
                Console.Error.WriteLine(replay[replay.Count - 1]);
            }
        if (mode != "full") return;
        Console.WriteLine();
        Console.WriteLine("| mesh | eps / diag | A: triangles, ms | C: triangles | B (envelope, with samples): triangles, ms | B without samples: triangles, ms | B / A triangles | B / A time | control (hook + IsWithin): ms, result vs A | A: h(R,M) / h(M,R) in eps | B: h(R,M) / h(M,R) in eps | A's triangles in the envelope | triangle checks B / control | checked position differs | topology A / B |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string s in rows) Console.WriteLine(s);
        Console.WriteLine();
        Console.WriteLine("| mesh | eps / diag | queries replayed | envelope µs (mean) | box scan µs (mean) | IsWithin µs (mean) | rejected at a vertex | one polyhedron holds it | full test: in | full test: out | envelope µs by class (vertex / one / in / out) | IsWithin accepts | envelope accepts |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string s in replay) Console.WriteLine(s);
    }

    // the logged queries of the envelope reduction again, one by one and serial: time per query of the envelope and of
    // IsWithin, the share of the linear box scan (PolyhedralEnvelope.candidates_box; a tree would remove it), and how
    // the query is decided (private members by reflection: classification only, not timed)
    static string Replay(string name, double rel, DMesh3 input, PolyhedralEnvelope env, double eps, List<Triangle3d> log)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        var inside = (Func<int, Vector3d, bool>)Delegate.CreateDelegate(typeof(Func<int, Vector3d, bool>), env, typeof(PolyhedralEnvelope).GetMethod("inside", F));
        var candidates = (Func<Vector3d, Vector3d, Vector3d, List<int>>)Delegate.CreateDelegate(typeof(Func<Vector3d, Vector3d, Vector3d, List<int>>), env, typeof(PolyhedralEnvelope).GetMethod("candidates_box", F));
        var tree = new DMeshAABBTree3(input, true);
        var bounds = new TriangleDeviationBounds(input);
        int step = Math.Max(1, log.Count / 3000);
        var q = new List<Triangle3d>();
        for (int i = 0; i < log.Count; i += step) q.Add(log[i]);
        int[] cls = new int[q.Count]; bool[] res = new bool[q.Count];
        double[] us = new double[q.Count];
        for (int i = 0; i < q.Count; ++i) us[i] = double.MaxValue;
        var sw = new Stopwatch();
        for (int round = 0; round < 3; ++round)
            for (int i = 0; i < q.Count; ++i) {
                sw.Restart();
                res[i] = env.ContainsTriangle(q[i].V0, q[i].V1, q[i].V2);
                us[i] = Math.Min(us[i], sw.Elapsed.TotalMilliseconds * 1000);
            }
        sw.Restart();
        foreach (Triangle3d t in q) candidates(t.V0, t.V1, t.V2);
        double usScan = sw.Elapsed.TotalMilliseconds * 1000 / q.Count;
        int inW = 0;
        sw.Restart();
        foreach (Triangle3d t in q) if (bounds.IsWithin(tree, t.V0, t.V1, t.V2, eps)) inW++;
        double usW = sw.Elapsed.TotalMilliseconds * 1000 / q.Count;
        for (int i = 0; i < q.Count; ++i) {
            bool ia = false, ib = false, ic = false, one = false;
            foreach (int p in candidates(q[i].V0, q[i].V1, q[i].V2)) {
                bool xa = inside(p, q[i].V0), xb = inside(p, q[i].V1), xc = inside(p, q[i].V2);
                one |= xa && xb && xc; ia |= xa; ib |= xb; ic |= xc;
            }
            cls[i] = !(ia && ib && ic) ? 0 : one ? 1 : res[i] ? 2 : 3;
        }
        Func<int, string> share = k => $"{100.0 * cls.Count(x => x == k) / q.Count:F0} %";
        Func<int, string> mean = k => cls.Any(x => x == k) ? Enumerable.Range(0, q.Count).Where(i => cls[i] == k).Average(i => us[i]).ToString("F0") : "-";
        return $"| {name} | {rel} | {q.Count} | {us.Average():F0} | {usScan:F0} | {usW:F0} | {share(0)} | {share(1)} | {share(2)} | {share(3)} | {mean(0)} / {mean(1)} / {mean(2)} / {mean(3)} | {100.0 * inW / q.Count:F0} % | {100.0 * res.Count(x => x) / q.Count:F0} % |";
    }

    // the guaranteed pipeline: envelope (or MaxDeviation) plus PreventSelfIntersections, checked with NotCleanReason
    static void Psi(List<(string name, DMesh3 mesh)> cases)
    {
        Console.WriteLine("| mesh | eps / diag | A + PSI: triangles, ms, NotCleanReason | B + PSI: triangles, ms, NotCleanReason | B: in the envelope | B / A triangles | B / A time |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var (name, input) in cases) {
            if (input.IsClosed() == false) continue;
            double rel = 3e-3, eps = rel * input.CachedBounds.DiagonalLength;
            new Reducer(new DMesh3(input)) { MaxDeviation = eps, PreventSelfIntersections = true }.ReduceToTriangleCount(input.TriangleCount / 2);    // warm-up
            var mA = new DMesh3(input);
            var sw = Stopwatch.StartNew();
            new Reducer(mA) { MaxDeviation = eps, PreventSelfIntersections = true }.ReduceToTriangleCount(4);
            double tA = sw.Elapsed.TotalMilliseconds;
            var mB = new DMesh3(input);
            sw.Restart();
            var env = new PolyhedralEnvelope(new DMesh3(input), eps);
            var rB = EnvelopeReducer(mB, env, true);
            rB.PreventSelfIntersections = true;
            rB.ReduceToTriangleCount(4);
            double tB = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"| {name} | {rel} | {mA.TriangleCount}, {tA:F0}, {Topology(mA, true, true)} | {mB.TriangleCount}, {tB:F0}, {Topology(mB, true, true)} | {InEnvelope(env, mB)} of {mB.TriangleCount} | {(double)mB.TriangleCount / mA.TriangleCount:F2} | {tB / tA:F1} |");
        }
    }
}
