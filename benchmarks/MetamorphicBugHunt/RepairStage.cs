using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// MeshRobustRepair on procedural defects of known solids: the result is a solid, it is the expected solid (volume, winding
    /// samples), the repair is idempotent, and the result is the same under 2^k scale, a rotation, a far translation and a permutation.
    /// </summary>
    public static class RepairStage
    {
        const string S = "repair";

        sealed class Case
        {
            public string Name; public DMesh3 Input; public DMesh3[] Solids; public Func<bool[], bool> Inside; public double Volume;
            /// <summary> surface distance (relative to the diagonal) inside which the samples are not judged </summary>
            public double Margin = 0.02;
        }

        static IEnumerable<Case> Cases()
        {
            DMesh3 sphere = Shapes.Sphere(Vector3d.Zero, 1, 10), box = Shapes.Box(new Vector3d(0.8, 0.3, 0.2), new Vector3d(0.6, 0.5, 0.7));
            DMesh3 torus = Shapes.Torus(Vector3d.Zero, 1, 0.35, 24, 12), star = Shapes.Prism(Shapes.Star(6, 1.2, 0.6), -0.4, 0.4);
            Func<bool[], bool> any = s => s.Any(x => x), first = s => s[0];

            yield return Make("overlap sphere+box", Shapes.Combine(sphere, box), new[] { sphere, box }, any);
            yield return Make("overlap torus+star", Shapes.Combine(torus, star), new[] { torus, star }, any);
            DMesh3 s2 = Shapes.Sphere(new Vector3d(0.5, 0, 0), 1, 10);
            yield return Make("overlap three spheres", Shapes.Combine(sphere, s2, Shapes.Sphere(new Vector3d(0.2, 0.6, 0.1), 0.8, 8)), new[] { sphere, s2, Shapes.Sphere(new Vector3d(0.2, 0.6, 0.1), 0.8, 8) }, any);
            foreach (var (n, m) in new[] { ("sphere", sphere), ("torus", torus), ("star", star), ("gridbox", Shapes.GridBox(Vector3d.Zero, new Vector3d(1, 0.7, 0.5), 6)) }) {
                yield return Make(n + " one triangle missing", Holes(m, 1, 1), new[] { m }, first);
                yield return Make(n + " 5% missing", Holes(m, Math.Max(2, m.TriangleCount / 20), 2), new[] { m }, first);
                yield return Make(n + " 10% flipped", Flipped(m, m.TriangleCount / 10, 3), new[] { m }, first);
                yield return Make(n + " all flipped", Flipped(m, m.TriangleCount, 3), new[] { m }, first);
                yield return Make(n + " unwelded", Unwelded(m), new[] { m }, first);
                yield return Make(n + " duplicated triangles", Shapes.Combine(m, m), new[] { m }, first);
            }
            // a cavity: a shell with an inner, inward-facing sphere
            DMesh3 inner = Shapes.Sphere(new Vector3d(0.1, 0, 0), 0.5, 6);
            DMesh3 innerRev = new DMesh3(inner); innerRev.ReverseOrientation();
            yield return Make("hollow sphere (clean)", Shapes.Combine(sphere, innerRev), new[] { sphere, inner }, s => s[0] && !s[1]);
            yield return Make("hollow sphere, outer shell one triangle missing", Shapes.Combine(Holes(sphere, 1, 1), innerRev), new[] { sphere, inner }, s => s[0] && !s[1]);
            yield return Make("hollow sphere, cavity facing outward", Shapes.Combine(sphere, inner), new[] { sphere, inner }, s => s[0] && !s[1]);
            // parts of very different size
            DMesh3 small = Shapes.Sphere(new Vector3d(3, 0, 0), 0.05, 4);
            yield return Make("sphere with hole + small clean sphere", Shapes.Combine(Holes(sphere, 1, 1), small), new[] { sphere, small }, any);
            yield return Make("overlap + small clean sphere", Shapes.Combine(sphere, box, small), new[] { sphere, box, small }, any);
            // a thin plate crossing a sphere
            DMesh3 plate = Shapes.Box(new Vector3d(0, 0, 0.2), new Vector3d(1.5, 0.4, 0.01));
            yield return Make("sphere + thin plate through it", Shapes.Combine(sphere, plate), new[] { sphere, plate }, any, 0.004);
        }

        static Case Make(string name, DMesh3 input, DMesh3[] solids, Func<bool[], bool> inside, double margin = 0.02)
        {
            // the reference volume: the exact Boolean of the solids with the winding predicate
            double volume = double.NaN;
            try {
                if (solids.Length == 1) volume = Checks.Volume(solids[0]);
                else {
                    var e = new ExactMeshBoolean(solids.Select(x => new DMesh3(x)), w => inside(w.Select(x => x != 0).ToArray()));
                    if (e.Compute()) volume = Checks.Volume(e.Result);
                }
            } catch (Exception) { }
            return new Case { Name = name, Input = input, Solids = solids, Inside = inside, Volume = volume, Margin = margin };
        }

        public static DMesh3 Holes(DMesh3 m, int count, int seed)
        {
            var c = new DMesh3(m);
            var rng = new Random(seed);
            var tris = c.TriangleIndices().ToList();
            for (int i = 0; i < count; ++i) { int t = tris[rng.Next(tris.Count)]; if (c.IsTriangle(t)) c.RemoveTriangle(t, true, false); }
            return new DMesh3(c, true);
        }

        public static DMesh3 Flipped(DMesh3 m, int count, int seed)
        {
            // as a soup: every triangle with its own vertices, some reversed, welded by position where the orientation allows
            var rng = new Random(seed);
            var flip = new HashSet<int>();
            var tris = m.TriangleIndices().ToList();
            if (count >= tris.Count) foreach (int t in tris) flip.Add(t);
            else while (flip.Count < count) flip.Add(tris[rng.Next(tris.Count)]);
            if (count >= tris.Count) { var all = new DMesh3(m); all.ReverseOrientation(); return all; }
            var c = new DMesh3();
            foreach (int t in tris) {
                Index3i tv = m.GetTriangle(t);
                int a = c.AppendVertex(m.GetVertex(tv.a)), b = c.AppendVertex(m.GetVertex(tv.b)), d = c.AppendVertex(m.GetVertex(tv.c));
                if (flip.Contains(t)) c.AppendTriangle(a, d, b); else c.AppendTriangle(a, b, d);
            }
            return c;
        }

        public static DMesh3 Unwelded(DMesh3 m)
        {
            var c = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                c.AppendTriangle(c.AppendVertex(m.GetVertex(tv.a)), c.AppendVertex(m.GetVertex(tv.b)), c.AppendVertex(m.GetVertex(tv.c)));
            }
            return c;
        }

        static MeshRobustRepair Apply(string name, DMesh3 input, out bool ok)
        {
            var rep = new MeshRobustRepair(new DMesh3(input));
            ok = false;
            try { ok = Program.Timed(() => rep.Apply(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, name, "", ms); }
            catch (Exception ex) { Program.Report(S, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
            return rep;
        }

        static string Path(MeshRobustRepair r) => $"{r.ResultPath}{(r.FallbackReason != "" ? " (" + r.FallbackReason + ")" : "")}";

        public static void Run()
        {
            foreach (Case c in Cases()) {
                MeshRobustRepair rep = Apply(c.Name, c.Input, out bool ok);
                if (rep == null) continue;
                if (!ok) { Program.Info(S, c.Name, $"Apply() false: {rep.ResultReason}"); continue; }
                if (rep.ResultReason != "") { Program.Info(S, c.Name, $"Apply() true, not clean as reported: {rep.ResultReason}"); continue; }
                DMesh3 r = rep.Mesh;
                double v = Checks.Volume(r);
                Program.Info(S, c.Name, $"path {Path(rep)}, {r.TriangleCount} triangles, volume {v:R} (expected {c.Volume:R})");
                Program.Report(S, c.Name + " solid", Checks.Solid(r));
                // the wrap and solidify paths grow the solid by their offset / a cell: 5%; the exact paths: 1e-4
                bool approximate = rep.ResultPath == MeshRobustRepair.ResultPaths.AlphaWrap || rep.ResultPath == MeshRobustRepair.ResultPaths.Solidify;
                Program.Report(S, c.Name + " volume", Program.Rel("volume", v, c.Volume, approximate ? 0.05 : 1e-4));
                Program.Report(S, c.Name + " winding", Checks.SameSolid(r, c.Solids, c.Inside, 300, 5, approximate ? c.Margin : 1e-3));
                // idempotence
                MeshRobustRepair again = Apply(c.Name + " twice", r, out bool ok2);
                if (again != null) Program.Report(S, c.Name + " idempotent", !ok2 ? "second Apply() false: " + again.ResultReason
                    : (again.Mesh.TriangleCount == r.TriangleCount && Math.Abs(Checks.Volume(again.Mesh) - v) <= 1e-12 * Math.Abs(v)) ? ""
                    : $"path {Path(rep)} then {Path(again)}: {r.TriangleCount} -> {again.Mesh.TriangleCount} triangles, volume {v:R} -> {Checks.Volume(again.Mesh):R}");
                // 2^k: bit for bit
                foreach (int k in new[] { -10, 10 }) {
                    double f = Math.Pow(2, k);
                    MeshRobustRepair sc = Apply(c.Name + $" 2^{k}", Shapes.Map(c.Input, x => x * f), out bool ok3);
                    if (sc != null) Program.Report(S, c.Name + $" scale 2^{k} bit for bit", !ok3 ? "Apply() false: " + sc.ResultReason : BooleanStage.SameBits(r, sc.Mesh, f));
                }
                // permutation, rotation, far translation: the same path, a solid, the same volume up to the path's tolerance
                Matrix3d rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);
                var far = new Vector3d(632117.25, 5712043.5, 113.75);
                foreach (var (tn, input, back) in new (string, DMesh3, Func<Vector3d, Vector3d>)[] {
                    ("permuted", Shapes.Permute(c.Input, 9), x => x),
                    ("rotated", Shapes.Map(c.Input, x => rot * x), x => rot.Transpose() * x),
                    ("utm", Shapes.Map(c.Input, x => x + far), x => x - far) }) {
                    MeshRobustRepair t = Apply(c.Name + " " + tn, input, out bool ok4);
                    if (t == null) continue;
                    if (!ok4 || t.ResultReason != "") { Program.Report(S, c.Name + " " + tn + " status", $"fails ({t.ResultReason}) where the identity run is clean"); continue; }
                    Program.Report(S, c.Name + " " + tn + " solid", Checks.Solid(t.Mesh));
                    if (t.ResultPath != rep.ResultPath) Program.Info(S, c.Name + " " + tn, $"path {Path(t)} instead of {Path(rep)}");
                    bool approx2 = approximate || t.ResultPath == MeshRobustRepair.ResultPaths.AlphaWrap || t.ResultPath == MeshRobustRepair.ResultPaths.Solidify;
                    Program.Report(S, c.Name + " " + tn + " volume", Program.Rel("volume", Checks.Volume(t.Mesh), v, approx2 ? 0.05 : 1e-4));
                    Program.Report(S, c.Name + " " + tn + " winding", Checks.SameSolid(Shapes.Map(t.Mesh, back), c.Solids, c.Inside, 150, 5, approx2 ? c.Margin : 1e-3));
                }
            }
        }
    }
}
