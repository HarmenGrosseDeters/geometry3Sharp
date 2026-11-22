using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Chains: Boolean result -> repair -> remesh / reduce / quad / tet, and the SolidBooleanPipeline. Every stage's success is
    /// checked independently and against the stage before it (closed, manifold, Euler characteristic, volume).
    /// </summary>
    public static class ChainStage
    {
        const string S = "chain";

        public static void Run()
        {
            foreach (Pair p in BooleanStage.Pairs()) {
                foreach (Ops op in BooleanStage.AllOps) {
                    string n = $"{p.Name} {op}";
                    ExactMeshBoolean e = BooleanStage.Run(p.A, p.B, op, S, n);
                    if (e == null || e.Result.TriangleCount == 0) continue;
                    DMesh3 r = e.Result;
                    bool clean = MeshRobustRepair.NotCleanReason(r) == "";
                    double v0 = Checks.Volume(r), diag = Checks.Bounds(r).Diagonal.Length;
                    int euler = Checks.Euler(r);

                    DMesh3 repaired = Repair(n, r, clean, v0);
                    if (repaired == null) continue;
                    Remesh(n, repaired, diag, false);
                    Remesh(n, repaired, diag, true);
                    Reduce(n, repaired, diag);
                    Quad(n, repaired, diag);
                    Tet(n, repaired, diag);
                    Pipeline(n, p, op, v0, clean);
                }
            }
        }

        static DMesh3 Repair(string n, DMesh3 r, bool clean, double v0)
        {
            var rep = new MeshRobustRepair(new DMesh3(r));
            bool ok;
            try { ok = Program.Timed(() => rep.Apply(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, n + " repair", "", ms); }
            catch (Exception ex) { Program.Report(S, n + " repair", "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
            if (!ok) {
                Program.Report(S, n + " repair status", clean ? "Apply() false on a clean Boolean result: " + rep.ResultReason : "");
                if (!clean) Program.Info(S, n + " repair", "Apply() false on a not-clean Boolean result: " + rep.ResultReason);
                return null;
            }
            Program.Report(S, n + " repair solid", Checks.Solid(rep.Mesh));
            Program.Report(S, n + " repair volume", Program.Rel("volume", Checks.Volume(rep.Mesh), v0, clean ? 1e-5 : 2e-2));
            if (clean) {
                // a clean mesh: the repair has nothing to do. Compared as a set (the result is compacted), and with the same
                // mesh next to a far clean part, which the repair passes through untouched (PassThroughCleanParts)
                bool same = rep.Mesh.TriangleCount == r.TriangleCount && rep.Mesh.VertexCount == r.VertexCount && Math.Abs(Checks.Volume(rep.Mesh) - v0) <= 1e-12 * Math.Abs(v0);
                AxisAlignedBox3d b = Checks.Bounds(r);
                DMesh3 far = Shapes.Box(b.Center + new Vector3d(4 * b.Width, 0, 0), b.Extents * 0.25);
                var rep3 = new MeshRobustRepair(Shapes.Combine(r, far));
                bool ok3 = rep3.Apply();
                bool sameWithFar = ok3 && rep3.Mesh.TriangleCount == r.TriangleCount + far.TriangleCount && rep3.Mesh.VertexCount == r.VertexCount + far.VertexCount;
                Program.Report(S, n + " repair of a clean mesh is the identity", same ? "" : $"path {rep.ResultPath}, {r.VertexCount}/{r.TriangleCount} -> {rep.Mesh.VertexCount}/{rep.Mesh.TriangleCount} vertices/triangles, volume {v0:R} -> {Checks.Volume(rep.Mesh):R}, moved {OneSided(rep.Mesh, r):R}");
                Program.Report(S, n + " repair of a clean mesh: alone as next to a far part", same == sameWithFar ? "" :
                    $"alone: path {rep.ResultPath}, {r.VertexCount}/{r.TriangleCount} -> {rep.Mesh.VertexCount}/{rep.Mesh.TriangleCount} vertices/triangles, volume {v0:R} -> {Checks.Volume(rep.Mesh):R}, moved {OneSided(rep.Mesh, r):R}; next to a far box: path {rep3.ResultPath}, unchanged {sameWithFar}");
            }
            // idempotence
            var rep2 = new MeshRobustRepair(new DMesh3(rep.Mesh));
            if (!rep2.Apply()) Program.Report(S, n + " repair twice", "the second Apply() fails: " + rep2.ResultReason);
            else Program.Report(S, n + " repair twice is the identity", BooleanStage.SameBits(Compact(rep.Mesh), Compact(rep2.Mesh), 1) == "" ? "" : $"path {rep.ResultPath} then {rep2.ResultPath}: " + BooleanStage.SameBits(Compact(rep.Mesh), Compact(rep2.Mesh), 1));
            return rep.Mesh;
        }

        static DMesh3 Compact(DMesh3 m) => new DMesh3(m, true);

        static void Remesh(string n, DMesh3 input, double diag, bool prevent)
        {
            string tag = n + (prevent ? " remesh(prevent)" : " remesh");
            var m = new DMesh3(input);
            var rm = new Remesher(m) { PreventSelfIntersections = prevent };
            rm.SetTargetEdgeLength(diag / 25);
            rm.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            try {
                Program.Timed(() => { for (int i = 0; i < 10; ++i) rm.BasicRemeshPass(); return 0; }, out long ms);
                if (ms >= Program.SlowMs) Program.Report(S, tag, "", ms);
            } catch (Exception ex) { Program.Report(S, tag, "threw " + ex.GetType().Name + ": " + ex.Message); return; }
            Program.Report(S, tag + " closed manifold", Checks.ClosedManifold(m));
            Program.Report(S, tag + " validity", m.CheckValidity(false, FailMode.ReturnOnly) ? "" : "CheckValidity false");
            Program.Report(S, tag + " euler", Checks.Euler(m) == Checks.Euler(input) ? "" : $"{Checks.Euler(m)} vs {Checks.Euler(input)}");
            // (no bound: the remesher keeps no sharp edges without constraints; reported, not failed)
            if (Program.Rel("volume", Checks.Volume(m), Checks.Volume(input), 5e-2) != "") Program.Info(S, tag, "volume changed by more than 5%: " + Program.Rel("volume", Checks.Volume(m), Checks.Volume(input), 5e-2));
            string clean = MeshRobustRepair.NotCleanReason(m);
            if (prevent) Program.Report(S, tag + " clean", clean);
            else if (clean != "") Program.Info(S, tag, "not clean (no guarantee without PreventSelfIntersections): " + clean);
        }

        static void Reduce(string n, DMesh3 input, double diag)
        {
            foreach (bool dev in new[] { false, true }) {
                string tag = n + (dev ? " reduce(deviation)" : " reduce(count)");
                var m = new DMesh3(input);
                // (MinCollapseTriangleQuality: without it collapses may create near-degenerate triangles, as documented)
                var red = new Reducer(m) { PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02 };
                if (dev) red.MaxDeviation = diag * 1e-3;
                int target = dev ? 4 : Math.Max(4, input.TriangleCount / 3);
                try {
                    Program.Timed(() => { red.ReduceToTriangleCount(target); return 0; }, out long ms);
                    if (ms >= Program.SlowMs) Program.Report(S, tag, "", ms);
                } catch (Exception ex) { Program.Report(S, tag, "threw " + ex.GetType().Name + ": " + ex.Message); continue; }
                Program.Report(S, tag + " closed manifold", Checks.ClosedManifold(m));
                Program.Report(S, tag + " euler", Checks.Euler(m) == Checks.Euler(input) ? "" : $"{Checks.Euler(m)} vs {Checks.Euler(input)}");
                Program.Report(S, tag + " clean", MeshRobustRepair.NotCleanReason(m));
                if (dev) {
                    Program.Report(S, tag + " hausdorff", OneSided(m, input) <= diag * 1e-3 * (1 + 1e-9) ? "" : $"result vertices up to {OneSided(m, input):R} from the input, bound {diag * 1e-3:R}");
                    double areaBound = Checks.Area(input) * diag * 1e-3 * 2;
                    Program.Report(S, tag + " volume", Math.Abs(Checks.Volume(m) - Checks.Volume(input)) <= areaBound ? "" : $"volume {Checks.Volume(m):R} vs {Checks.Volume(input):R}, more than area x deviation x 2 = {areaBound:R}");
                }
            }
        }

        /// <summary> the largest distance of a vertex or triangle centroid of x from the surface y </summary>
        public static double OneSided(DMesh3 x, DMesh3 y)
        {
            double worst = 0;
            foreach (int v in x.VertexIndices()) worst = Math.Max(worst, Checks.DistanceTo(y, x.GetVertex(v)));
            foreach (int t in x.TriangleIndices()) worst = Math.Max(worst, Checks.DistanceTo(y, x.GetTriCentroid(t)));
            return worst;
        }

        static void Quad(string n, DMesh3 input, double diag)
        {
            string tag = n + " quad";
            var qr = new QuadRemesher(new DMesh3(input, true)) { TargetEdgeLength = diag / 20 };
            QuadRemeshStatus st;
            try { st = Program.Timed(() => qr.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, tag, "", ms); }
            catch (Exception ex) { Program.Report(S, tag, "threw " + ex.GetType().Name + ": " + ex.Message); return; }
            if (st != QuadRemeshStatus.Success) { Program.Info(S, tag, $"status {st} {qr.FieldStatus} {qr.Error}"); return; }
            Program.Report(S, tag + " polygon mesh", PolyChecks.Manifold(qr.Result, out bool closed, out int eulerQ));
            if (!closed) Program.Info(S, tag, "result is not closed (closed input)");
            else if (eulerQ != Checks.Euler(input)) Program.Info(S, tag, $"closed, Euler {eulerQ} vs input {Checks.Euler(input)}");
            DMesh3 tri = qr.Result.ToDMesh3(out int[] _);
            double worst = 0;
            foreach (int v in tri.VertexIndices()) worst = Math.Max(worst, Checks.DistanceTo(input, tri.GetVertex(v)));
            Program.Report(S, tag + " vertices near the input", worst <= diag / 20 ? "" : $"a vertex {worst:R} from the input (target edge {diag / 20:R})");
            if (closed) Program.Report(S, tag + " volume within 15%", Program.Rel("volume", Checks.Volume(tri), Checks.Volume(input), 0.15));
        }

        static void Tet(string n, DMesh3 input, double diag)
        {
            string tag = n + " tet";
            var tet = new MeshTetrahedralization(new DMesh3(input, true));
            bool ok;
            try { ok = Program.Timed(() => tet.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, tag, "", ms); Program.Info(S, tag, $"{ms} ms"); }
            catch (Exception ex) { Program.Report(S, tag, "threw " + ex.GetType().Name + ": " + ex.Message); return; }
            if (!ok) { Program.Info(S, tag, $"status {tet.Status} wrap {tet.WrapStatus}"); return; }
            Program.Report(S, tag + " valid", TetChecks.Valid(tet.Result, out double vol, out DMesh3 boundary));
            if (boundary == null) return;
            Program.Report(S, tag + " boundary solid", Checks.Solid(boundary));
            // the wrap is outside the input, within the offset and alpha: volume above the input's and below input + area x (offset + alpha)
            double v = Checks.Volume(input), slack = Checks.Area(input) * (tet.UsedOffset + tet.UsedAlpha);
            Program.Report(S, tag + " volume", vol >= v * (1 - 1e-9) && vol <= v + slack ? "" : $"tet volume {vol:R}, input {v:R}, allowed up to {v + slack:R} (offset {tet.UsedOffset:R}, alpha {tet.UsedAlpha:R})");
            Program.Report(S, tag + " input inside", InputInside(input, boundary));
            Program.Report(S, tag + " euler", Checks.Euler(boundary) == Checks.Euler(input) ? "" : "", -1);
            if (Checks.Euler(boundary) != Checks.Euler(input)) Program.Info(S, tag, $"boundary Euler {Checks.Euler(boundary)} vs input {Checks.Euler(input)}");
        }

        public static string InputInside(DMesh3 input, DMesh3 wrap)
        {
            int bad = 0; string first = "";
            foreach (int v in input.VertexIndices()) {
                double w = Checks.Winding(wrap, input.GetVertex(v));
                if (w > 0.5) continue;
                if (bad++ == 0) first = $"input vertex {v} at {input.GetVertex(v)} has winding {w:R} in the result";
            }
            return bad == 0 ? "" : $"{bad} input vertices outside: {first}";
        }

        static void Pipeline(string n, Pair p, Ops op, double v0, bool clean)
        {
            double diag = Checks.Bounds(p.A, p.B).Diagonal.Length, dev = 2e-3 * diag;
            foreach (double frac in new[] { 0.0, 0.3 }) {
                string tag = n + $" pipeline({frac})";
                // (a reduction without a deviation bound may change the solid at will: bounded here)
                var pl = new SolidBooleanPipeline(new DMesh3(p.A), new DMesh3(p.B), op) { TargetFraction = frac, MaxDeviation = frac > 0 ? dev : 0, MaxDeviationTwoSided = frac > 0 };
                bool ok;
                try { ok = Program.Timed(() => pl.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, tag, "", ms); }
                catch (Exception ex) { Program.Report(S, tag, "threw " + ex.GetType().Name + ": " + ex.Message); continue; }
                if (!ok) { Program.Info(S, tag, $"status {pl.Status} {pl.Error}"); continue; }
                Program.Report(S, tag + " closed manifold", Checks.ClosedManifold(pl.Result));
                string reason = MeshRobustRepair.NotCleanReason(pl.Result);
                if (pl.Report.Clean) Program.Report(S, tag + " clean as reported", reason);
                else {
                    Program.Report(S, tag + " not clean as reported", reason == "" ? "Report.NotCleanReason '" + pl.Report.NotCleanReason + "' but the result is clean" : "");
                    Program.Info(S, tag, $"reported not clean: {pl.Report.NotCleanReason}, only contacts {pl.Report.OnlyContacts}");
                }
                if (frac == 0) Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(pl.Result), v0, 1e-9));
                else {
                    DMesh3 exact = BooleanStage.Run(p.A, p.B, op, S, tag).Result;
                    double bound = Checks.Area(exact) * dev * 2;
                    Program.Report(S, tag + " volume", Math.Abs(Checks.Volume(pl.Result) - v0) <= bound ? "" : $"volume {Checks.Volume(pl.Result):R} vs {v0:R}, more than area x deviation x 2 = {bound:R}");
                    double h1 = OneSided(pl.Result, exact), h2 = OneSided(exact, pl.Result);
                    Program.Report(S, tag + " deviation", Math.Max(h1, h2) <= dev * (1 + 1e-6) ? "" : $"result to Boolean {h1:R}, Boolean to result {h2:R}, MaxDeviation {dev:R} (two-sided)");
                }
                Program.Report(S, tag + " winding", Checks.SameSolid(pl.Result, new[] { p.A, p.B }, s => BooleanStage.Inside(op, s[0], s[1]), 200, 23, frac == 0 ? 1e-4 : 2.5e-3));
                // run twice on one instance
                DMesh3 first = pl.Result;
                if (pl.Compute()) Program.Report(S, tag + " reuse", BooleanStage.SameBits(first, pl.Result, 1));
            }
        }
    }

    public static class PolyChecks
    {
        /// <summary> every directed edge at most once, each undirected edge in at most two faces; closed if in exactly two </summary>
        public static string Manifold(DPolyMesh m, out bool closed, out int euler)
        {
            closed = false; euler = 0;
            var dir = new Dictionary<(int, int), int>();
            var used = new HashSet<int>();
            for (int f = 0; f < m.FaceCount; ++f) {
                int k = m.FaceVertexCount(f);
                if (k < 3) return $"face {f} has {k} vertices";
                for (int i = 0; i < k; ++i) {
                    int a = m.GetFaceVertex(f, i), b = m.GetFaceVertex(f, (i + 1) % k);
                    if (a == b) return $"face {f} repeats vertex {a}";
                    if (!m.GetVertex(a).IsFinite) return $"non-finite vertex {a}";
                    used.Add(a);
                    if (dir.ContainsKey((a, b))) return $"directed edge ({a}, {b}) in two faces (non-manifold or inconsistent orientation)";
                    dir[(a, b)] = f;
                }
            }
            int open = 0;
            var und = new HashSet<(int, int)>();
            foreach (var kv in dir) {
                if (!dir.ContainsKey((kv.Key.Item2, kv.Key.Item1))) ++open;
                und.Add(kv.Key.Item1 < kv.Key.Item2 ? kv.Key : (kv.Key.Item2, kv.Key.Item1));
            }
            closed = open == 0;
            euler = used.Count - und.Count + m.FaceCount;
            return "";
        }
    }

    public static class TetChecks
    {
        /// <summary> positive tets, every facet matched or on the boundary, the boundary equal to BoundaryTriangles; the volume and the boundary mesh </summary>
        public static string Valid(TetMesh3 t, out double volume, out DMesh3 boundary)
        {
            volume = 0; boundary = null;
            var facets = new Dictionary<(int, int, int), int>();
            int[][] F = { new[] { 1, 2, 3 }, new[] { 0, 3, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 1 } };
            foreach (Vector3d v in t.Vertices) if (!v.IsFinite) return "non-finite vertex";
            for (int i = 0; i < t.Tets.Count; ++i) {
                Index4i q = t.Tets[i];
                Vector3d a = t.Vertices[q.a], b = t.Vertices[q.b], c = t.Vertices[q.c], d = t.Vertices[q.d];
                double vol = (b - a).Cross(c - a).Dot(d - a) / 6;
                if (!(vol > 0)) return $"tet {i} has volume {vol:R}";
                volume += vol;
                foreach (int[] f in F) {
                    var key = Canon(q[f[0]], q[f[1]], q[f[2]]);
                    facets.TryGetValue(key, out int c0);
                    facets[key] = c0 + 1;
                }
            }
            // outward boundary facets: those whose reverse is not present
            var b3 = new DMesh3();
            foreach (Vector3d v in t.Vertices) b3.AppendVertex(v);
            int nb = 0;
            foreach (var kv in facets) {
                if (kv.Value != 1) return $"facet {kv.Key} used {kv.Value} times with one orientation";
                var rev = Canon(kv.Key.Item1, kv.Key.Item3, kv.Key.Item2);
                if (facets.ContainsKey(rev)) continue;
                ++nb;
                if (b3.AppendTriangle(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3) < 0) return $"boundary facet {kv.Key} is non-manifold";
            }
            if (nb != t.BoundaryTriangles.Count) return $"{nb} boundary facets, BoundaryTriangles has {t.BoundaryTriangles.Count}";
            var used = new HashSet<int>();
            foreach (int tid in b3.TriangleIndices()) { Index3i tv = b3.GetTriangle(tid); used.Add(tv.a); used.Add(tv.b); used.Add(tv.c); }
            foreach (int v in b3.VertexIndices().ToArray()) if (!used.Contains(v)) b3.RemoveVertex(v, false, false);
            boundary = b3;
            return "";
        }

        static (int, int, int) Canon(int a, int b, int c)
        {
            if (a < b && a < c) return (a, b, c);
            if (b < a && b < c) return (b, c, a);
            return (c, a, b);
        }
    }
}
