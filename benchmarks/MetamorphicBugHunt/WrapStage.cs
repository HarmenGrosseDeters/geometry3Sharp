using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// MeshAlphaWrap and MeshTetrahedralization on clean and broken inputs: the documented guarantees checked independently
    /// (closed, manifold, clean, input inside, vertices at the offset), monotony in the offset, 2^k scale bit for bit, a rotation,
    /// a permutation, and two far parts wrapped together against each alone.
    /// </summary>
    public static class WrapStage
    {
        const string S = "wrap";

        static IEnumerable<(string, DMesh3)> Inputs()
        {
            DMesh3 sphere = Shapes.Sphere(Vector3d.Zero, 1, 8);
            yield return ("sphere", sphere);
            yield return ("box", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)));
            yield return ("torus", Shapes.Torus(Vector3d.Zero, 1, 0.35, 24, 12));
            yield return ("star", Shapes.Prism(Shapes.Star(6, 1.2, 0.6), -0.4, 0.4));
            yield return ("sphere with holes", RepairStage.Holes(sphere, 20, 4));
            yield return ("overlap soup", Shapes.Combine(sphere, Shapes.Box(new Vector3d(0.8, 0.3, 0.2), new Vector3d(0.6, 0.5, 0.7))));
            yield return ("thin plate", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 0.002)));
            yield return ("needle", Shapes.Box(Vector3d.Zero, new Vector3d(2, 0.01, 0.01)));
            yield return ("tilted plate", Shapes.Rotate(Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 0.01)), new Vector3d(1, 2, 3), 33));
            yield return ("single triangle", Triangle());
            yield return ("tet", Shapes.Tet(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1)));
        }

        static DMesh3 Triangle()
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0.2, 0.9, 0.3)));
            return m;
        }

        static MeshAlphaWrap Wrap(string name, DMesh3 input, double relAlpha, double relOffset, double alpha = 0, double offset = 0)
        {
            var w = new MeshAlphaWrap(new DMesh3(input)) { RelativeAlpha = relAlpha, RelativeOffset = relOffset, Alpha = alpha, Offset = offset };
            bool ok;
            try { ok = Program.Timed(() => w.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, name, "", ms); }
            catch (Exception ex) { Program.Report(S, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
            if (!ok || w.Status != MeshAlphaWrap.WrapStatus.Success) { Program.Info(S, name, $"status {w.Status} {w.Error}"); return null; }
            return w;
        }

        static void Guarantees(string name, DMesh3 input, MeshAlphaWrap w)
        {
            Program.Report(S, name + " solid", Checks.Solid(w.Result));
            Program.Report(S, name + " input inside", ChainStage.InputInside(input, w.Result));
            double near = double.MaxValue, far = 0;
            foreach (int v in w.Result.VertexIndices()) { double d = Checks.DistanceTo(input, w.Result.GetVertex(v)); near = Math.Min(near, d); far = Math.Max(far, d); }
            Program.Report(S, name + " vertices keep the offset", near >= w.UsedOffset * (1 - 1e-6) ? "" : $"a vertex at {near:R} from the input, offset {w.UsedOffset:R}");
            // (documented: within about Offset + Alpha, farther where the manifold step moved cells: reported above 2 x)
            if (far > 2 * (w.UsedOffset + w.UsedAlpha)) Program.Info(S, name, $"a vertex {far:R} from the input, {far / (w.UsedOffset + w.UsedAlpha):F2} (offset + alpha)");
        }

        public static void Run()
        {
            foreach (var (n, input) in Inputs()) {
                MeshAlphaWrap w = Wrap(n, input, 20, 600);
                if (w == null) continue;
                Guarantees(n, input, w);
                double v = Checks.Volume(w.Result);
                // monotone in the offset
                MeshAlphaWrap w2 = Wrap(n + " offset x4", input, 20, 150);
                if (w2 != null) {
                    Guarantees(n + " offset x4", input, w2);
                    Program.Report(S, n + " larger offset, larger volume", Checks.Volume(w2.Result) > v ? "" : $"volume {Checks.Volume(w2.Result):R} at 4 x offset, {v:R} at 1 x");
                }
                // 2^k
                foreach (int k in new[] { -10, 10 }) {
                    double f = Math.Pow(2, k);
                    MeshAlphaWrap ws = Wrap(n + $" 2^{k}", Shapes.Map(input, x => x * f), 20, 600);
                    Program.Report(S, n + $" scale 2^{k} bit for bit", ws == null ? "fails" : BooleanStage.SameBits(w.Result, ws.Result, f));
                }
                // absolute parameters: rotation, permutation, utm; the volume within the alpha band
                Matrix3d rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);
                var far = new Vector3d(632117.25, 5712043.5, 113.75);
                double band = Checks.Area(w.Result) * (w.UsedAlpha + w.UsedOffset);
                foreach (var (tn, ti) in new[] { ("permuted", Shapes.Permute(input, 9)), ("rotated", Shapes.Map(input, x => rot * x)), ("utm", Shapes.Map(input, x => x + far)) }) {
                    MeshAlphaWrap wt = Wrap(n + " " + tn, ti, 0, 0, w.UsedAlpha, w.UsedOffset);
                    if (wt == null) { Program.Report(S, n + " " + tn + " status", "fails where the identity run succeeds"); continue; }
                    Guarantees(n + " " + tn, ti, wt);
                    Program.Report(S, n + " " + tn + " volume", Math.Abs(Checks.Volume(wt.Result) - v) <= band ? "" : $"volume {Checks.Volume(wt.Result):R} vs {v:R}, more than area x (alpha + offset) = {band:R} apart");
                    Program.Report(S, n + " " + tn + " euler", Checks.Euler(wt.Result) == Checks.Euler(w.Result) ? "" : $"Euler {Checks.Euler(wt.Result)} vs {Checks.Euler(w.Result)}");
                }
                // two far parts: the wrap of both is the two wraps
                AxisAlignedBox3d b = Checks.Bounds(input);
                Vector3d shift = new Vector3d(8 * Math.Max(b.Width, Math.Max(b.Height, b.Depth)), 0, 0);
                DMesh3 moved = Shapes.Map(input, x => x + shift);
                MeshAlphaWrap both = Wrap(n + " two parts", Shapes.Combine(input, moved), 0, 0, w.UsedAlpha, w.UsedOffset);
                if (both == null) Program.Report(S, n + " two parts status", "fails where one part succeeds");
                else {
                    Guarantees(n + " two parts", Shapes.Combine(input, moved), both);
                    Program.Report(S, n + " two parts volume", Math.Abs(Checks.Volume(both.Result) - 2 * v) <= 2 * band ? "" : $"volume {Checks.Volume(both.Result):R} vs 2 x {v:R}");
                    Program.Report(S, n + " two parts euler", Checks.Euler(both.Result) == 2 * Checks.Euler(w.Result) ? "" : $"Euler {Checks.Euler(both.Result)} vs 2 x {Checks.Euler(w.Result)}");
                }
                // wrap of the wrap contains the wrap
                MeshAlphaWrap ww = Wrap(n + " wrap of wrap", w.Result, 0, 0, w.UsedAlpha, w.UsedOffset);
                if (ww != null) {
                    Guarantees(n + " wrap of wrap", w.Result, ww);
                    Program.Report(S, n + " wrap of wrap is larger", Checks.Volume(ww.Result) > v ? "" : $"volume {Checks.Volume(ww.Result):R} vs {v:R}");
                }
                Tet(n, input);
            }
        }

        static void Tet(string n, DMesh3 input)
        {
            string tag = n + " tet";
            MeshTetrahedralization Run(DMesh3 m, string name, int threads = 0)
            {
                var t = new MeshTetrahedralization(new DMesh3(m)) { MaxThreads = threads };
                bool ok;
                try { ok = Program.Timed(() => t.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, name, "", ms); }
                catch (Exception ex) { Program.Report(S, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
                if (!ok) { Program.Info(S, name, $"status {t.Status} wrap {t.WrapStatus}"); return null; }
                return t;
            }
            MeshTetrahedralization tet = Run(input, tag);
            if (tet == null) return;
            Program.Report(S, tag + " valid", TetChecks.Valid(tet.Result, out double vol, out DMesh3 boundary));
            string lib = TetMeshQuality.NotValidReason(tet.Result);
            Program.Report(S, tag + " valid (library check)", lib ?? "");
            if (boundary == null) return;
            Program.Report(S, tag + " boundary solid", Checks.Solid(boundary));
            Program.Report(S, tag + " input inside", ChainStage.InputInside(input, boundary));
            Program.Report(S, tag + " tet volume = boundary volume", Program.Rel("volume", vol, Checks.Volume(boundary), 1e-9));
            if (!tet.Targets.Met) Program.Info(S, tag, $"targets not met: min dihedral {tet.Targets.WorstMinDihedral:F2} ({tet.Targets.TetsBelowMinDihedral} below), radius-edge {tet.Targets.WorstRadiusEdge:F2} ({tet.Targets.TetsAboveRadiusEdge} above), {tet.Result.Tets.Count} tets");
            // reuse, serial = parallel, 2^k
            int tets = tet.Result.Tets.Count; var verts = tet.Result.Vertices.ToArray();
            if (tet.Compute()) Program.Report(S, tag + " reuse", tet.Result.Tets.Count == tets && tet.Result.Vertices.SequenceEqual(verts) ? "" : $"{tets} tets, then {tet.Result.Tets.Count}");
            MeshTetrahedralization serial = Run(input, tag + " serial", 1);
            if (serial != null) Program.Report(S, tag + " serial = parallel", serial.Result.Tets.Count == tets && serial.Result.Vertices.SequenceEqual(verts) ? "" : $"{tets} tets parallel, {serial.Result.Tets.Count} serial");
            double f = 1024;
            MeshTetrahedralization scaled = Run(Shapes.Map(input, x => x * f), tag + " 2^10");
            if (scaled != null) Program.Report(S, tag + " scale 2^10 bit for bit", scaled.Result.Tets.Count == tets && scaled.Result.Vertices.SequenceEqual(verts.Select(x => x * f)) ? "" : $"{tets} tets, scaled {scaled.Result.Tets.Count}");
        }
    }
}
