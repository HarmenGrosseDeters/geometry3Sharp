using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// QuadRemesher with its options (hunt 1 ran the defaults on Boolean results only). Documented: deterministic bit for bit,
    /// serial or parallel; 2^k scalings give the same polygon mesh up to the scaling; Compute() can be called again; the input is
    /// not modified; a failed pure-quad attempt leaves the stage 2 mesh, identical to PureQuads = false; a successful one is a
    /// manifold of quads only; SizeAdaptation that is not applied changes nothing. Further relations: the flipped input gives a
    /// consistently oriented mesh of the opposite volume, a far copy gives two independent results.
    /// </summary>
    public static class QuadStage
    {
        const string S = "quad";

        static string Same(DPolyMesh a, DPolyMesh b, double scale = 1)
        {
            if (a == null || b == null) return a == b ? "" : "one is null";
            if (a.VertexCount != b.VertexCount || a.FaceCount != b.FaceCount) return $"counts {a.VertexCount}/{a.FaceCount} vs {b.VertexCount}/{b.FaceCount}";
            for (int v = 0; v < a.VertexCount; ++v) { Vector3d p = a.GetVertex(v) * scale, q = b.GetVertex(v); if (p.x != q.x || p.y != q.y || p.z != q.z) return $"vertex {v}: {p.x:R} {p.y:R} {p.z:R} vs {q.x:R} {q.y:R} {q.z:R}"; }
            for (int f = 0; f < a.FaceCount; ++f) {
                if (a.FaceVertexCount(f) != b.FaceVertexCount(f)) return "face " + f;
                for (int i = 0; i < a.FaceVertexCount(f); ++i) if (a.GetFaceVertex(f, i) != b.GetFaceVertex(f, i)) return "face " + f;
            }
            return "";
        }

        static int[] Sides(DPolyMesh m) { var c = new int[8]; for (int f = 0; f < m.FaceCount; ++f) c[Math.Min(7, m.FaceVertexCount(f))]++; return c; }

        static bool SameBits(DMesh3 a, DMesh3 b)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return false;
            foreach (int v in a.VertexIndices()) if (a.GetVertex(v) != b.GetVertex(v)) return false;
            foreach (int t in a.TriangleIndices()) if (a.GetTriangle(t) != b.GetTriangle(t)) return false;
            return true;
        }

        static (string, DMesh3, double)[] Inputs() => new[] {
            ("sphere", Shapes.Sphere(Vector3d.Zero, 1, 12), 0.2),
            ("torus", Shapes.Torus(Vector3d.Zero, 1, 0.4, 32, 16), 0.2),
            ("grid box", Shapes.GridBox(Vector3d.Zero, new Vector3d(1, 0.75, 0.5), 9), 0.25),
            ("ellipsoid", Shapes.Map(Shapes.Sphere(Vector3d.Zero, 1, 12), p => new Vector3d(1.5 * p.x, p.y, 0.6 * p.z)), 0.2),
            ("cylinder", Remeshed(Shapes.Cylinder(Vector3d.Zero, 0.6, 2, 24), 0.2), 0.2),
            ("open disc", OpenCap(), 0.15),
        };

        static DMesh3 Remeshed(DMesh3 m, double edge)
        {
            var r = new Remesher(m) { SmoothSpeedT = 0.5, EnableSmoothing = false };
            r.SetTargetEdgeLength(edge);
            MeshConstraintUtil.FixAllBoundaryEdges(r);
            r.SetExternalConstraints(new MeshConstraints());
            // split only: a finer mesh of the same solid
            r.EnableCollapses = false; r.EnableFlips = false;
            for (int i = 0; i < 6; ++i) r.BasicRemeshPass();
            return new DMesh3(m, true);
        }

        static DMesh3 OpenCap()
        {
            // the part z > 0.3 of a sphere mesh (open, one boundary loop)
            DMesh3 s = Shapes.Sphere(Vector3d.Zero, 1, 14);
            var m = new DMesh3(); var map = new Dictionary<int, int>();
            foreach (int t in s.TriangleIndices()) {
                Index3i tv = s.GetTriangle(t);
                if (s.GetTriCentroid(t).z < 0.3) continue;
                for (int k = 0; k < 3; ++k) if (!map.ContainsKey(tv[k])) map[tv[k]] = m.AppendVertex(s.GetVertex(tv[k]));
                m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
            }
            return m;
        }

        static QuadRemesher Make(DMesh3 m, double edge, Action<QuadRemesher> opt = null)
        {
            var q = new QuadRemesher(m) { TargetEdgeLength = edge };
            opt?.Invoke(q);
            return q;
        }

        public static void Run()
        {
            foreach (var (name, mesh, edge) in Inputs()) {
                var options = new (string, Action<QuadRemesher>)[] {
                    ("default", q => { }),
                    ("pure quads", q => q.PureQuads = true),
                    ("scale", q => q.SizeAdaptation = SizeAdaptationMode.Scale),
                    ("scale and rotation, pure quads", q => { q.SizeAdaptation = SizeAdaptationMode.ScaleAndRotation; q.PureQuads = true; }),
                    ("curvature", q => q.CurvatureAlignment = 1),
                    ("creases", q => q.PreserveCreases = true),
                    ("no cleanup", q => q.Cleanup = false),
                };
                DPolyMesh stage2 = null;
                foreach (var (on, opt) in options) {
                    string c = name + " | " + on;
                    try {
                        var input = new DMesh3(mesh, true);
                        QuadRemesher q = Make(input, edge, opt);
                        QuadRemeshStatus st = Program.Timed(() => q.Compute(), out long ms);
                        Program.Report(S, c + " | time", "", ms);
                        Program.Report(S, c + " | input not modified", SameBits(input, mesh) ? "" : "the input mesh was changed");
                        if (st != QuadRemeshStatus.Success) { Program.Report(S, c + " | status", $"{st} {q.FieldStatus} {q.Error}"); continue; }
                        DPolyMesh r = q.Result;
                        if (on == "default") stage2 = r;
                        Program.Report(S, c + " | polygon mesh", PolyChecks.Manifold(r, out bool closed, out int euler));
                        bool inputClosed = Checks.ClosedManifold(mesh) == "";
                        int[] sides = Sides(r);
                        // near the input
                        DMesh3 tri = r.ToDMesh3(out int[] _);
                        double worst = 0; foreach (int v in tri.VertexIndices()) worst = Math.Max(worst, Checks.DistanceTo(mesh, tri.GetVertex(v)));
                        Program.Report(S, c + " | vertices near the input", worst <= edge ? "" : $"a vertex {worst:R} from the input (target edge {edge})");
                        if (q.PureQuads) {
                            if (q.PureQuadStatus == PureQuadStatus.Success) {
                                Program.Report(S, c + " | pure: quads only", sides[4] == r.FaceCount ? "" : $"faces by sides: {string.Join(",", sides)}");
                                Program.Report(S, c + " | pure: closed", closed ? "" : "not closed");
                                Program.Report(S, c + " | pure: Euler", euler == Checks.Euler(mesh) ? "" : $"{euler} vs the input {Checks.Euler(mesh)}");
                                if (closed) Program.Report(S, c + " | pure: volume", Program.Rel("volume", Checks.Volume(tri), Checks.Volume(mesh), 0.1));
                                Program.Report(S, c + " | pure: counters", q.InvertedTriangles == 0 && q.FlippedQuads == 0 ? "" : $"InvertedTriangles {q.InvertedTriangles}, FlippedQuads {q.FlippedQuads}");
                            } else {
                                Program.Info(S, c, "pure-quad attempt: " + q.PureQuadStatus);
                                // a failed attempt: identical to the same options without PureQuads
                                QuadRemesher plain = Make(new DMesh3(mesh, true), edge, x => { opt(x); x.PureQuads = false; }); plain.Compute();
                                Program.Report(S, c + " | failed attempt = stage 2", Same(plain.Result, r));
                            }
                            if (!inputClosed) Program.Report(S, c + " | open input: FlowInfeasible", q.PureQuadStatus == PureQuadStatus.FlowInfeasible ? "" : q.PureQuadStatus.ToString());
                        } else Program.Report(S, c + " | PureQuadStatus", q.PureQuadStatus == PureQuadStatus.NotRequested ? "" : q.PureQuadStatus.ToString());
                        if (q.SizeAdaptation != SizeAdaptationMode.Off && !q.SizeAdaptationApplied && !q.PureQuads && stage2 != null) Program.Report(S, c + " | not applied = off", Same(stage2, r));
                        if (closed && inputClosed && !q.PureQuads) Program.Report(S, c + " | volume", Program.Rel("volume", Checks.Volume(tri), Checks.Volume(mesh), 0.15));
                        if (inputClosed && !closed) Program.Info(S, c, $"not closed (DroppedFaces {q.DroppedFaces}; documented)");
                        // reuse
                        var first = r; q.Compute();
                        Program.Report(S, c + " | reuse", Same(first, q.Result));
                        // serial = parallel
                        QuadRemesher ser = Make(new DMesh3(mesh, true), edge, x => { opt(x); x.Parallel = false; }); ser.Compute();
                        Program.Report(S, c + " | serial = parallel", ser.Status == st ? Same(r, ser.Result) : "status " + ser.Status);
                        // 2^k
                        foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                            QuadRemesher qs = Make(Shapes.Map(mesh, p => p * s), edge * s, opt); qs.Compute();
                            Program.Report(S, c + " | scale " + s, qs.Status == st ? Same(r, qs.Result, s) : "status " + qs.Status);
                            if (q.PureQuads) Program.Report(S, c + " | scale " + s + " pure status", qs.PureQuadStatus == q.PureQuadStatus ? "" : $"{qs.PureQuadStatus} vs {q.PureQuadStatus}");
                        }
                        if (on != "default" && on != "pure quads") continue;
                        // flipped input: consistently oriented, the opposite volume
                        {
                            var fl = new DMesh3(mesh, true); fl.ReverseOrientation();
                            QuadRemesher qf = Make(fl, edge, opt); QuadRemeshStatus fs = qf.Compute();
                            if (fs != st) Program.Report(S, c + " | flipped", "status " + fs);
                            else {
                                Program.Report(S, c + " | flipped polygon mesh", PolyChecks.Manifold(qf.Result, out bool fc, out int fe));
                                // (the field and the lattice of the flipped mesh are another solution: only the orientation and the solid are compared)
                                if (qf.Result.FaceCount != r.FaceCount) Program.Info(S, c + " | flipped", $"{qf.Result.VertexCount}/{qf.Result.FaceCount} vertices/faces vs {r.VertexCount}/{r.FaceCount}");
                                if (fc && closed) Program.Report(S, c + " | flipped volume", Program.Rel("volume", Checks.Volume(qf.Result.ToDMesh3(out int[] _)), -Checks.Volume(tri), 0.1));
                                if (q.PureQuads && qf.PureQuadStatus != q.PureQuadStatus) Program.Info(S, c + " | flipped", $"pure status {qf.PureQuadStatus} vs {q.PureQuadStatus}");
                            }
                        }
                        // a far copy: twice the faces
                        {
                            DMesh3 two = Shapes.Combine(mesh, Shapes.Map(mesh, p => p + new Vector3d(64, 0, 0)));
                            QuadRemesher q2 = Make(two, edge, opt); QuadRemeshStatus s2 = q2.Compute();
                            if (s2 != st) Program.Report(S, c + " | far copy", "status " + s2 + " " + q2.Error);
                            else {
                                Program.Report(S, c + " | far copy polygon mesh", PolyChecks.Manifold(q2.Result, out bool _, out int _));
                                string same = q2.Result.FaceCount == 2 * r.FaceCount ? "" : $"{q2.Result.FaceCount} faces vs 2 x {r.FaceCount}";
                                if (same != "") Program.Info(S, c + " | far copy", same + $" (sides {string.Join(",", Sides(q2.Result))} vs {string.Join(",", sides)})"); else Program.Report(S, c + " | far copy faces", "");
                                if (q.PureQuads) Program.Info(S, c + " | far copy", $"pure status {q2.PureQuadStatus} vs {q.PureQuadStatus}");
                                DMesh3 t2 = q2.Result.ToDMesh3(out int[] _);
                                double w2 = 0; foreach (int v in t2.VertexIndices()) w2 = Math.Max(w2, Checks.DistanceTo(two, t2.GetVertex(v)));
                                Program.Report(S, c + " | far copy vertices near the input", w2 <= edge ? "" : $"a vertex {w2:R} from the input");
                            }
                        }
                        // permuted input: valid again, about the same size
                        {
                            QuadRemesher qp = Make(Shapes.Permute(mesh, 9), edge, opt); QuadRemeshStatus ps = qp.Compute();
                            if (ps != st) Program.Report(S, c + " | permuted", "status " + ps);
                            else {
                                Program.Report(S, c + " | permuted polygon mesh", PolyChecks.Manifold(qp.Result, out bool _, out int _));
                                if (Math.Abs(qp.Result.FaceCount - r.FaceCount) > 0.15 * r.FaceCount) Program.Info(S, c + " | permuted", $"{qp.Result.FaceCount} faces vs {r.FaceCount} (O3)");
                                if (q.PureQuads && qp.PureQuadStatus != q.PureQuadStatus) Program.Info(S, c + " | permuted", $"pure status {qp.PureQuadStatus} vs {q.PureQuadStatus}");
                            }
                        }
                    }
                    catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
                }
            }
        }
    }
}
