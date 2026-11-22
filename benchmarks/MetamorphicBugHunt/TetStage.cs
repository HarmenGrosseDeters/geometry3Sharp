using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// MeshTetrahedralization with its options (hunt 1 ran the defaults): size control (TargetEdgeLength, SizeFunction,
    /// GradingFactor), MaxSurfaceDeviation, CreaseAngleDegrees, Improve off, no peeling. A result is checked independently:
    /// positive tets, matched facets, the boundary a solid that contains the input, the volume between the input's and the
    /// wrap band. Relations: 2^k bit for bit, a second Compute(), one thread, the input permuted and flipped (the same checks),
    /// a far copy, the Targets report against a recount of the dihedral angles.
    /// </summary>
    public static class TetStage
    {
        const string S = "tet";

        static string SameTets(TetMesh3 a, TetMesh3 b, double scale = 1)
        {
            if (a.Vertices.Count != b.Vertices.Count || a.Tets.Count != b.Tets.Count) return $"counts {a.Vertices.Count}/{a.Tets.Count} vs {b.Vertices.Count}/{b.Tets.Count}";
            for (int i = 0; i < a.Vertices.Count; ++i) { Vector3d p = a.Vertices[i] * scale, q = b.Vertices[i]; if (p.x != q.x || p.y != q.y || p.z != q.z) return $"vertex {i}: {p.x:R} {p.y:R} {p.z:R} vs {q.x:R} {q.y:R} {q.z:R}"; }
            for (int i = 0; i < a.Tets.Count; ++i) { Index4i x = a.Tets[i], y = b.Tets[i]; if (x.a != y.a || x.b != y.b || x.c != y.c || x.d != y.d) return "tet " + i; }
            return "";
        }

        static double MinDihedral(TetMesh3 t)
        {
            double min = 180;
            foreach (Index4i q in t.Tets) {
                Vector3d[] p = { t.Vertices[q.a], t.Vertices[q.b], t.Vertices[q.c], t.Vertices[q.d] };
                for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) {
                    int k = Enumerable.Range(0, 4).First(x => x != i && x != j), l = 6 - i - j - k;
                    Vector3d e = (p[j] - p[i]).Normalized;
                    Vector3d u = p[k] - p[i], v = p[l] - p[i];
                    u -= u.Dot(e) * e; v -= v.Dot(e) * e;
                    double ang = Math.Atan2(u.Cross(v).Length, u.Dot(v)) * 180 / Math.PI;
                    min = Math.Min(min, ang);
                }
            }
            return min;
        }

        public static void Run()
        {
            var inputs = new (string, DMesh3)[] {
                ("sphere", Shapes.Sphere(Vector3d.Zero, 1, 6)),
                ("box", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5))),
                ("star prism", Shapes.Prism(Shapes.Star(5, 1.2, 0.6), -0.4, 0.4)),
                ("tube", Wrap2Stage.Tube(10, 1.0, 0.5, 0.4)),
                ("soup", Shapes.Combine(Shapes.Sphere(Vector3d.Zero, 1, 5), Shapes.Box(new Vector3d(0.8, 0.3, 0.2), new Vector3d(0.6, 0.5, 0.7)))),
            };
            var options = new (string, Action<MeshTetrahedralization, double>)[] {
                ("coarse default", (t, s) => { }),
                ("target edge", (t, s) => t.TargetEdgeLength = 0.25 * s),
                ("size function", (t, s) => t.SizeFunction = p => s * (0.15 + 0.2 * Math.Abs(p.x / s))),
                ("grading", (t, s) => t.GradingFactor = 0.5),
                ("surface deviation", (t, s) => t.MaxSurfaceDeviation = 0.02 * s),
                ("creases", (t, s) => t.CreaseAngleDegrees = 30),
                ("no improve", (t, s) => t.Improve = false),
                ("no peeling", (t, s) => t.PeelBoundarySlivers = false),
                ("invalid size function", (t, s) => t.SizeFunction = p => p.x > 0 ? double.NaN : -1),
            };
            foreach (var (name, input) in inputs)
            foreach (var (on, opt) in options) {
                string c = name + " | " + on;
                try {
                    Func<DMesh3, double, MeshTetrahedralization> run = (m, s) => { var t = new MeshTetrahedralization(new DMesh3(m, true)) { RelativeAlpha = 20, RelativeOffset = 300 }; opt(t, s); t.Compute(); return t; };
                    MeshTetrahedralization tet = Program.Timed(() => run(input, 1), out long ms);
                    Program.Report(S, c + " | time", "", ms);
                    if (tet.Status != MeshTetrahedralization.TetStatus.Success) { Program.Report(S, c + " | status", $"{tet.Status} wrap {tet.WrapStatus}"); if (tet.Result == null) continue; }
                    string valid = TetChecks.Valid(tet.Result, out double vol, out DMesh3 boundary);
                    Program.Report(S, c + " | valid", valid);
                    if (boundary == null) continue;
                    Program.Report(S, c + " | boundary solid", Checks.Solid(boundary));
                    Program.Report(S, c + " | input inside", ChainStage.InputInside(input, boundary));
                    if (Checks.ClosedManifold(input) == "" && name != "soup") {
                        double v = Checks.Volume(input), slack = Checks.Area(input) * (tet.UsedOffset + tet.UsedAlpha);
                        Program.Report(S, c + " | volume", vol >= v * (1 - 1e-9) && vol <= v + slack ? "" : $"tet volume {vol:R}, input {v:R}, allowed up to {v + slack:R}");
                    }
                    Program.Report(S, c + " | volume = boundary volume", Program.Rel("volume", vol, Checks.Volume(boundary), 1e-9));
                    // the report against a recount
                    if (tet.Targets != null) {
                        double min = MinDihedral(tet.Result);
                        Program.Report(S, c + " | Targets.MinDihedral", Math.Abs(tet.Targets.WorstMinDihedral - min) < 1e-6 ? "" : $"reported {tet.Targets.WorstMinDihedral:R}, recounted {min:R}");
                        Program.Report(S, c + " | Targets.DihedralMet", tet.Targets.MinDihedralMet == (min >= tet.Targets.MinDihedralDegrees - 1e-9) ? "" : $"MinDihedralMet {tet.Targets.MinDihedralMet} with a minimum of {min:R} for {tet.Targets.MinDihedralDegrees}");
                        if (!tet.Targets.MinDihedralMet) Program.Info(S, c, $"dihedral floor not met: {min:F2} degrees (reported)");
                    } else Program.Report(S, c + " | Targets", "null with a Result");
                    if (on == "invalid size function") {
                        Program.Report(S, c + " | invalid samples counted", tet.InvalidSizeSamples > 0 ? "" : "InvalidSizeSamples is 0");
                        MeshTetrahedralization plain = run(input, 1); plain.SizeFunction = null; plain.Compute();
                        Program.Report(S, c + " | = no size function", SameTets(plain.Result, tet.Result));
                    }
                    // reuse, one thread, 2^k
                    TetMesh3 first = tet.Result;
                    tet.Compute();
                    Program.Report(S, c + " | reuse", SameTets(first, tet.Result));
                    var one = new MeshTetrahedralization(new DMesh3(input, true)) { RelativeAlpha = 20, RelativeOffset = 300, MaxThreads = 1 }; opt(one, 1); one.Compute();
                    Program.Report(S, c + " | one thread", SameTets(first, one.Result));
                    foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                        MeshTetrahedralization ts = run(Shapes.Map(input, p => p * s), s);
                        Program.Report(S, c + " | scale " + s, ts.Status == tet.Status ? SameTets(first, ts.Result, s) : "status " + ts.Status);
                    }
                    if (on != "coarse default" && on != "creases" && on != "target edge") continue;
                    foreach (var (vn, vi) in new[] { ("permuted", Shapes.Permute(input, 3)), ("flipped", Flip(input)) }) {
                        MeshTetrahedralization tv = run(vi, 1);
                        if (tv.Result == null) { Program.Report(S, c + " | " + vn, "status " + tv.Status); continue; }
                        Program.Report(S, c + " | " + vn + " valid", TetChecks.Valid(tv.Result, out double vv, out DMesh3 vb));
                        if (vb == null) continue;
                        Program.Report(S, c + " | " + vn + " boundary solid", Checks.Solid(vb));
                        Program.Report(S, c + " | " + vn + " input inside", ChainStage.InputInside(input, vb));
                        Program.Report(S, c + " | " + vn + " volume", Program.Rel("volume", vv, vol, 0.02));
                    }
                    {
                        DMesh3 two = Shapes.Combine(input, Shapes.Map(input, p => p + new Vector3d(8, 0, 0)));
                        var t2 = new MeshTetrahedralization(two) { RelativeAlpha = 20 * Checks.Bounds(two).Diagonal.Length / Checks.Bounds(input).Diagonal.Length, RelativeOffset = 300 * Checks.Bounds(two).Diagonal.Length / Checks.Bounds(input).Diagonal.Length };
                        opt(t2, 1); t2.Compute();
                        if (t2.Result == null) { Program.Report(S, c + " | far copy", "status " + t2.Status); continue; }
                        Program.Report(S, c + " | far copy valid", TetChecks.Valid(t2.Result, out double v2, out DMesh3 b2));
                        if (b2 != null) {
                            Program.Report(S, c + " | far copy boundary solid", Checks.Solid(b2));
                            Program.Report(S, c + " | far copy input inside", ChainStage.InputInside(two, b2));
                            Program.Report(S, c + " | far copy volume", Program.Rel("volume", v2, 2 * vol, 0.02));
                        }
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        static DMesh3 Flip(DMesh3 m) { var f = new DMesh3(m); f.ReverseOrientation(); return f; }
    }
}
