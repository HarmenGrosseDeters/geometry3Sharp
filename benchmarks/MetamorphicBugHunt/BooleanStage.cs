using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace MetamorphicBugHunt
{
    public sealed class Pair
    {
        public string Name; public DMesh3 A, B;
        /// <summary> the pair has coplanar or touching faces: SafeRounding and tie decisions are in play </summary>
        public bool Contact;
        public Pair(string name, DMesh3 a, DMesh3 b, bool contact = false) { Name = name; A = a; B = b; Contact = contact; }
    }

    public static class BooleanStage
    {
        public static readonly Ops[] AllOps = { Ops.Union, Ops.Intersection, Ops.Difference };

        public static bool Inside(Ops op, bool a, bool b) => op == Ops.Union ? (a || b) : op == Ops.Intersection ? (a && b) : (a && !b);

        public static List<Pair> Pairs()
        {
            var l = new List<Pair>();
            l.Add(new Pair("sphere-box", Shapes.Sphere(new Vector3d(0.3, 0.2, 0.1), 1, 8), Shapes.Box(new Vector3d(0.9, 0.5, 0.3), new Vector3d(0.7, 0.6, 0.8))));
            l.Add(new Pair("sphere-sphere", Shapes.Sphere(Vector3d.Zero, 1, 10), Shapes.Sphere(new Vector3d(0.7, 0.3, 0.2), 0.9, 7)));
            l.Add(new Pair("torus-cylinder", Shapes.Torus(Vector3d.Zero, 1, 0.35, 24, 12), Shapes.Rotate(Shapes.Cylinder(new Vector3d(0, 0, 0), 0.3, 3, 16), new Vector3d(1, 0.2, 0), 90)));
            l.Add(new Pair("star-sphere", Shapes.Prism(Shapes.Star(7, 1.2, 0.5), -0.4, 0.4), Shapes.Sphere(new Vector3d(0.4, 0.1, 0.2), 0.7, 8)));
            l.Add(new Pair("rotated-boxes", Shapes.Rotate(Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.6, 0.4)), new Vector3d(1, 2, 3), 37), Shapes.Rotate(Shapes.Box(new Vector3d(0.3, 0.1, 0), new Vector3d(0.5, 1, 0.5)), new Vector3d(3, 1, 2), 71)));
            l.Add(new Pair("nested", Shapes.Sphere(Vector3d.Zero, 1, 8), Shapes.Box(new Vector3d(0.1, 0, 0), new Vector3d(0.2, 0.3, 0.2))));
            l.Add(new Pair("disjoint", Shapes.Sphere(Vector3d.Zero, 1, 6), Shapes.Box(new Vector3d(3, 0, 0), new Vector3d(0.5, 0.5, 0.5))));
            l.Add(new Pair("torus-torus", Shapes.Torus(Vector3d.Zero, 1, 0.3, 20, 10), Shapes.Rotate(Shapes.Torus(new Vector3d(1, 0, 0), 1, 0.3, 20, 10), Vector3d.AxisX, 90)));
            l.Add(new Pair("tet-tet", Shapes.Tet(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0), new Vector3d(0, 0, 2)), Shapes.Tet(new Vector3d(1.3, 1.1, 1.2), new Vector3d(-0.7, 0.4, 0.3), new Vector3d(0.5, -0.8, 0.2), new Vector3d(0.3, 0.2, -0.9))));
            // contact families
            l.Add(new Pair("boxes-coplanar-face", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Shapes.Box(new Vector3d(1.5, 0.25, 0), new Vector3d(0.5, 0.5, 1)), true));
            l.Add(new Pair("boxes-shared-face", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Shapes.Box(new Vector3d(2, 0, 0), new Vector3d(1, 1, 1)), true));
            l.Add(new Pair("gridboxes-overlap-coplanar", Shapes.GridBox(Vector3d.Zero, new Vector3d(1, 1, 1), 5), Shapes.GridBox(new Vector3d(1, 0, 0), new Vector3d(1, 1, 1), 4), true));
            l.Add(new Pair("cylinder-on-box", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 0.5)), Shapes.Cylinder(new Vector3d(0, 0, 1), 0.5, 1, 12), true));
            l.Add(new Pair("prism-in-prism-coaxial", Shapes.Prism(Shapes.Ngon(12, 1), -1, 1), Shapes.Prism(Shapes.Ngon(12, 0.5), -1, 1), true));
            l.Add(new Pair("box-edge-touch", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Shapes.Box(new Vector3d(2, 2, 0), new Vector3d(1, 1, 1)), true));
            l.Add(new Pair("box-vertex-touch", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Shapes.Box(new Vector3d(2, 2, 2), new Vector3d(1, 1, 1)), true));
            l.Add(new Pair("rotated-coplanar", Shapes.Rotate(Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1)), new Vector3d(1, 2, 3), 37), Shapes.Rotate(Shapes.Box(new Vector3d(1.5, 0.25, 0), new Vector3d(0.5, 0.5, 1)), new Vector3d(1, 2, 3), 37), true));
            return l;
        }

        public static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, string stage, string name)
        {
            var e = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            bool ok;
            try { ok = Program.Timed(() => e.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(stage, name, "", ms); }
            catch (Exception ex) { Program.Report(stage, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
            if (!ok) { Program.Info(stage, name, $"status {e.Status}: {e.Error}"); return null; }
            if (e.Status != ExactMeshBoolean.BooleanStatus.Success) Program.Report(stage, name, "Compute() true with status " + e.Status);
            return e;
        }

        /// <summary> the result as a solid: "" for an empty result is allowed where empty is expected </summary>
        public static string SolidOrEmpty(DMesh3 m) => m.TriangleCount == 0 ? "" : Checks.Solid(m);

        public static int NotCleanReported;

        /// <summary>
        /// A Boolean result: closed and manifold always (independent check); clean (no self-intersections) whenever the Boolean
        /// reports it clean (RoundingClean and no Contacts). A result the Boolean itself reports as not clean is counted, not failed.
        /// </summary>
        public static void CheckResult(string stage, string name, ExactMeshBoolean e)
        {
            if (e.Result.TriangleCount == 0) { Program.Report(stage, name, ""); return; }
            string cm = Checks.ClosedManifold(e.Result);
            if (cm != "") { Program.Report(stage, name, cm); return; }
            string clean = MeshRobustRepair.NotCleanReason(e.Result);
            if (clean == "") { Program.Report(stage, name, e.RoundingClean ? "" : "clean by NotCleanReason, but RoundingClean is false"); return; }
            if (e.RoundingClean && e.Contacts == 0) { Program.Report(stage, name, "reported clean (RoundingClean, no contacts) but: " + clean); return; }
            ++NotCleanReported;
            Program.Info(stage, name, $"not clean, and reported: {clean}; RoundingClean {e.RoundingClean}, contacts {e.Contacts}, remaining intersections {e.Rounding?.RemainingIntersections}");
        }

        public static void Run()
        {
            const string S = "boolean";
            foreach (Pair p in Pairs()) {
                double va = Checks.Volume(p.A), vb = Checks.Volume(p.B);
                var res = new Dictionary<Ops, DMesh3>();
                foreach (Ops op in AllOps) {
                    string n = $"{p.Name} {op}";
                    ExactMeshBoolean e = Run(p.A, p.B, op, S, n);
                    if (e == null) continue;
                    res[op] = e.Result;
                    CheckResult(S, n + " solid", e);
                    Program.Report(S, n + " winding", Checks.SameSolid(e.Result, new[] { p.A, p.B }, s => Inside(op, s[0], s[1]), 400, 7));
                    // commutativity
                    if (op != Ops.Difference) {
                        ExactMeshBoolean sw = Run(p.B, p.A, op, S, n + " swapped");
                        if (sw != null) Program.Report(S, n + " commutes (volume)", Program.Rel("volume", Checks.Volume(e.Result), Checks.Volume(sw.Result), 1e-9));
                    }
                    // permutation of both operands: the same solid
                    ExactMeshBoolean q = Run(Shapes.Permute(p.A, 3), Shapes.Permute(p.B, 5), op, S, n + " permuted");
                    if (q != null) {
                        CheckResult(S, n + " permuted solid", q);
                        Program.Report(S, n + " permuted volume", Program.Rel("volume", Checks.Volume(e.Result), Checks.Volume(q.Result), 1e-9));
                    }
                    // 2^k scale: bit for bit
                    foreach (int k in new[] { -10, 10 }) {
                        double f = Math.Pow(2, k);
                        ExactMeshBoolean s = Run(Shapes.Map(p.A, x => x * f), Shapes.Map(p.B, x => x * f), op, S, n + $" 2^{k}");
                        if (s != null) Program.Report(S, n + $" scale 2^{k} bit for bit", SameBits(e.Result, s.Result, f));
                    }
                    // other similarities: the same volume up to rounding, and a solid
                    foreach (var (sn, map, scale) in Similarities()) {
                        ExactMeshBoolean s = Run(Shapes.Map(p.A, map), Shapes.Map(p.B, map), op, S, n + " " + sn);
                        if (s == null) { Program.Report(S, n + " " + sn + " status", "fails where the identity run succeeds"); continue; }
                        CheckResult(S, n + " " + sn + " solid", s);
                        Program.Report(S, n + " " + sn + " volume", Program.Rel("volume", Checks.Volume(e.Result) * scale * scale * scale, Checks.Volume(s.Result), 1e-7));
                    }
                }
                if (res.Count == 3) {
                    double vu = Checks.Volume(res[Ops.Union]), vi = Checks.Volume(res[Ops.Intersection]), vd = Checks.Volume(res[Ops.Difference]);
                    Program.Report(S, p.Name + " inclusion-exclusion", Program.Rel("vol(AuB)+vol(AnB) vs vol A+vol B", vu + vi, va + vb, 1e-9));
                    Program.Report(S, p.Name + " vol(A-B) = vol A - vol(AnB)", Program.Rel("volume", vd + vi, va, 1e-9));
                    // (A u B) - B = A - B
                    ExactMeshBoolean r = Run(res[Ops.Union], p.B, Ops.Difference, S, p.Name + " (AuB)-B");
                    if (r == null) Program.Report(S, p.Name + " (AuB)-B status", "fails on a result of the Boolean");
                    else {
                        CheckResult(S, p.Name + " (AuB)-B solid", r);
                        Program.Report(S, p.Name + " (AuB)-B volume", Program.Rel("volume", Checks.Volume(r.Result), vd, 1e-6));
                        Program.Report(S, p.Name + " (AuB)-B winding", Checks.SameSolid(r.Result, new[] { p.A, p.B }, s => s[0] && !s[1], 300, 11));
                    }
                    // (A - B) u (A n B) = A
                    if (res[Ops.Difference].TriangleCount > 0 && res[Ops.Intersection].TriangleCount > 0) {
                        r = Run(res[Ops.Difference], res[Ops.Intersection], Ops.Union, S, p.Name + " (A-B)u(AnB)");
                        if (r == null) Program.Report(S, p.Name + " (A-B)u(AnB) status", "fails on results of the Boolean");
                        else {
                            CheckResult(S, p.Name + " (A-B)u(AnB) solid", r);
                            Program.Report(S, p.Name + " (A-B)u(AnB) volume", Program.Rel("volume", Checks.Volume(r.Result), va, 1e-6));
                            Program.Report(S, p.Name + " (A-B)u(AnB) winding", Checks.SameSolid(r.Result, new[] { p.A }, s => s[0], 300, 13));
                        }
                    }
                    // (A u B) n A = A (absorption)
                    r = Run(res[Ops.Union], p.A, Ops.Intersection, S, p.Name + " (AuB)nA");
                    if (r == null) Program.Report(S, p.Name + " (AuB)nA status", "fails on a result of the Boolean");
                    else {
                        CheckResult(S, p.Name + " (AuB)nA solid", r);
                        Program.Report(S, p.Name + " (AuB)nA volume", Program.Rel("volume", Checks.Volume(r.Result), va, 1e-6));
                    }
                }
                // idempotence on a separately built, permuted copy
                foreach (Ops op in AllOps) {
                    ExactMeshBoolean e = Run(p.A, Shapes.Permute(p.A, 17), op, S, $"{p.Name} A {op} A");
                    if (e == null) { Program.Report(S, $"{p.Name} A {op} A status", "fails"); continue; }
                    CheckResult(S, $"{p.Name} A {op} A solid", e);
                    Program.Report(S, $"{p.Name} A {op} A volume", op == Ops.Difference
                        ? (e.Result.TriangleCount == 0 ? "" : $"A - A is not empty: {e.Result.TriangleCount} triangles, volume {Checks.Volume(e.Result):R}")
                        : Program.Rel("volume", Checks.Volume(e.Result), va, 1e-12));
                }
                // split by a half space and join
                AxisAlignedBox3d bounds = Checks.Bounds(p.A);
                Vector3d c = bounds.Center, h = bounds.Extents * 1.5;
                foreach (var (hn, half) in new[] {
                    ("axis", Shapes.Box(c + new Vector3d(h.x + 0.137 * bounds.Extents.x, 0, 0), h)),
                    ("tilted", Shapes.Map(Shapes.Rotate(Shapes.Box(new Vector3d(h.Length, 0, 0), new Vector3d(h.Length, h.Length, h.Length)), new Vector3d(0.3, 1, 0.5), 23), x => x + c + 0.05 * bounds.Extents)) }) {
                    ExactMeshBoolean i1 = Run(p.A, half, Ops.Intersection, S, $"{p.Name} split {hn} in"), i2 = Run(p.A, half, Ops.Difference, S, $"{p.Name} split {hn} out");
                    if (i1 == null || i2 == null || i1.Result.TriangleCount == 0 || i2.Result.TriangleCount == 0) continue;
                    Program.Report(S, $"{p.Name} split {hn} volumes", Program.Rel("vol(AnH)+vol(A-H) vs vol A", Checks.Volume(i1.Result) + Checks.Volume(i2.Result), va, 1e-9));
                    ExactMeshBoolean j = Run(i1.Result, i2.Result, Ops.Union, S, $"{p.Name} join {hn}");
                    if (j == null) { Program.Report(S, $"{p.Name} join {hn} status", "the union of the two halves fails"); continue; }
                    CheckResult(S, $"{p.Name} join {hn} solid", j);
                    Program.Report(S, $"{p.Name} join {hn} volume", Program.Rel("volume", Checks.Volume(j.Result), va, 1e-6));
                    Program.Report(S, $"{p.Name} join {hn} winding", Checks.SameSolid(j.Result, new[] { p.A }, s => s[0], 300, 17));
                    Program.Report(S, $"{p.Name} join {hn} euler", Checks.Euler(j.Result) == Checks.Euler(p.A) ? "" : $"Euler characteristic {Checks.Euler(j.Result)} vs {Checks.Euler(p.A)} of the input");
                }
            }
        }

        public static IEnumerable<(string, Func<Vector3d, Vector3d>, double)> Similarities()
        {
            Matrix3d r = Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);
            var utm = new Vector3d(632117.25, 5712043.5, 113.75);
            yield return ("rotated", p => r * p, 1);
            yield return ("utm", p => p + utm, 1);
            yield return ("scale 1e-3", p => p * 1e-3, 1e-3);
            yield return ("scale 1e3", p => p * 1e3, 1e3);
            // (2^20: the inputs keep 2^-32 of their size; farther away the transformed input is itself a different solid)
            yield return ("far 2^20", p => p + new Vector3d(Math.Pow(2, 20), -Math.Pow(2, 20), Math.Pow(2, 19)), 1);
            yield return ("scale 3", p => p * 3.0, 3);
        }

        /// <summary> the same vertex list and triangle list after scaling by f (a power of two) </summary>
        public static string SameBits(DMesh3 x, DMesh3 y, double f)
        {
            if (x.TriangleCount != y.TriangleCount || x.VertexCount != y.VertexCount) return $"{x.VertexCount} vertices / {x.TriangleCount} triangles vs {y.VertexCount} / {y.TriangleCount}";
            var xi = x.VertexIndices().GetEnumerator(); var yi = y.VertexIndices().GetEnumerator();
            while (xi.MoveNext() && yi.MoveNext()) {
                if (xi.Current != yi.Current) return "vertex ids differ";
                if (x.GetVertex(xi.Current) * f != y.GetVertex(yi.Current)) return $"vertex {xi.Current} differs: {x.GetVertex(xi.Current) * f} vs {y.GetVertex(yi.Current)}";
            }
            var xt = x.TriangleIndices().GetEnumerator(); var yt = y.TriangleIndices().GetEnumerator();
            while (xt.MoveNext() && yt.MoveNext())
                if (xt.Current != yt.Current || x.GetTriangle(xt.Current) != y.GetTriangle(yt.Current)) return $"triangle {xt.Current} differs";
            return "";
        }
    }
}
