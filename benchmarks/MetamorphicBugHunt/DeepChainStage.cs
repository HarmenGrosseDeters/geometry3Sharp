using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Long random chains of Booleans, each step on the rounded result of the step before: every step is checked against the
    /// composition of the original solids' winding numbers (no reference Boolean), the volume laws, and the whole chain against
    /// its 2^k scaled run (bit for bit). Then the SolidBooleanPipeline with repaired inputs against the pipeline on the intact ones.
    /// </summary>
    public static class DeepChainStage
    {
        const string S = "deep";

        static DMesh3 RandomSolid(Random rng, int i)
        {
            Vector3d c = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 1.2;
            Vector3d axis = new Vector3d(rng.NextDouble() + 0.1, rng.NextDouble() + 0.1, rng.NextDouble() + 0.1);
            double deg = rng.NextDouble() * 90, r = 0.5 + 0.5 * rng.NextDouble();
            DMesh3 m;
            switch (i % 5) {
                case 0: m = Shapes.Sphere(Vector3d.Zero, r, 5 + rng.Next(4)); break;
                case 1: m = Shapes.Box(Vector3d.Zero, new Vector3d(r, 0.7 * r, 0.5 * r)); break;
                case 2: m = Shapes.Torus(Vector3d.Zero, r, 0.3 * r, 14, 8); break;
                case 3: m = Shapes.Prism(Shapes.Star(5, r, 0.5 * r), -0.4 * r, 0.4 * r); break;
                default: m = Shapes.Cylinder(Vector3d.Zero, 0.5 * r, 2 * r, 10); break;
            }
            return Shapes.Map(Shapes.Rotate(m, axis, deg), x => x + c);
        }

        public static void Run()
        {
            for (int seed = 1; seed <= 24; ++seed) {
                var rng = new Random(seed);
                var solids = new List<DMesh3>();
                var steps = new List<(Ops op, int solid)>();
                solids.Add(RandomSolid(rng, rng.Next(5)));
                DMesh3 cur = solids[0];
                var scaled = new List<DMesh3> { Shapes.Map(solids[0], x => x * 1024) };
                DMesh3 curScaled = scaled[0];
                bool scaledAlive = true;
                for (int step = 0; step < 6; ++step) {
                    DMesh3 next = RandomSolid(rng, rng.Next(5));
                    Ops op = (Ops)rng.Next(3);
                    solids.Add(next);
                    steps.Add((op, solids.Count - 1));
                    string n = $"seed {seed} step {step} {op}";
                    double vBefore = Checks.Volume(cur), vNext = Checks.Volume(next);
                    ExactMeshBoolean e = BooleanStage.Run(cur, next, op, S, n);
                    if (e == null) { Program.Report(S, n + " status", "the Boolean fails on the result of the chain"); break; }
                    if (e.Result.TriangleCount == 0) { Program.Info(S, n, "empty result, chain ends"); break; }
                    BooleanStage.CheckResult(S, n + " solid", e);
                    double v = Checks.Volume(e.Result), tol = 1e-6 * Math.Max(vBefore, vNext);
                    string law = op == Ops.Union ? (v >= Math.Max(vBefore, vNext) - tol && v <= vBefore + vNext + tol ? "" : "union volume outside [max, sum]")
                        : op == Ops.Intersection ? (v <= Math.Min(vBefore, vNext) + tol ? "" : "intersection volume above the smaller operand")
                        : (v <= vBefore + tol && v >= vBefore - vNext - tol ? "" : "difference volume outside [A - B, A]");
                    Program.Report(S, n + " volume law", law == "" ? "" : $"{law}: {v:R} from {vBefore:R} and {vNext:R}");
                    var st = steps.ToArray(); var all = solids.ToArray();
                    Program.Report(S, n + " winding of the composition", Checks.SameSolid(e.Result, all, s => {
                        bool inside = s[0];
                        foreach (var (o, k) in st) inside = BooleanStage.Inside(o, inside, s[k]);
                        return inside;
                    }, 150, seed * 100 + step, 2e-4));
                    cur = e.Result;
                    if (scaledAlive) {
                        ExactMeshBoolean es = BooleanStage.Run(curScaled, Shapes.Map(next, x => x * 1024), op, S, n + " 2^10");
                        if (es == null) { Program.Report(S, n + " 2^10 status", "fails where the unscaled chain succeeds"); scaledAlive = false; }
                        else { Program.Report(S, n + " 2^10 bit for bit", BooleanStage.SameBits(cur, es.Result, 1024)); curScaled = es.Result; }
                    }
                }
                // the end of the chain through the repair and the tet mesher
                if (cur.TriangleCount > 0 && MeshRobustRepair.NotCleanReason(cur) == "") {
                    var tet = new MeshTetrahedralization(new DMesh3(cur, true));
                    bool ok = false;
                    try { ok = Program.Timed(() => tet.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, $"seed {seed} tet", "", ms); }
                    catch (Exception ex) { Program.Report(S, $"seed {seed} tet", "threw " + ex.GetType().Name + ": " + ex.Message); }
                    if (ok) {
                        Program.Report(S, $"seed {seed} tet valid", TetChecks.Valid(tet.Result, out double vol, out DMesh3 boundary));
                        if (boundary != null) Program.Report(S, $"seed {seed} tet contains the solid", ChainStage.InputInside(cur, boundary));
                    }
                }
            }
            PipelineRepairs();
        }

        static void PipelineRepairs()
        {
            DMesh3 sphere = Shapes.Sphere(Vector3d.Zero, 1, 8), box = Shapes.Box(new Vector3d(0.8, 0.3, 0.2), new Vector3d(0.6, 0.5, 0.7));
            DMesh3 torus = Shapes.Torus(new Vector3d(0, 0, 0.3), 1, 0.3, 20, 10);
            var broken = new (string, DMesh3)[] {
                ("one triangle missing", RepairStage.Holes(sphere, 1, 1)),
                ("unwelded", RepairStage.Unwelded(sphere)),
                ("all flipped", RepairStage.Flipped(sphere, int.MaxValue, 1)),
                ("self-overlapping", Shapes.Combine(sphere, Shapes.Sphere(new Vector3d(0.3, 0, 0), 0.9, 6))),
            };
            foreach (var (bn, a) in broken)
                foreach (Ops op in BooleanStage.AllOps)
                    foreach (DMesh3 b in new[] { box, torus }) {
                        string n = $"pipeline {bn} {op} {(b == box ? "box" : "torus")}";
                        foreach (SolidBooleanPipeline.InputRepairModes mode in Enum.GetValues(typeof(SolidBooleanPipeline.InputRepairModes))) {
                            var pl = new SolidBooleanPipeline(new DMesh3(a), new DMesh3(b), op) { InputRepair = mode };
                            bool ok;
                            try { ok = pl.Compute(); } catch (Exception ex) { Program.Report(S, n + " " + mode, "threw " + ex.GetType().Name + ": " + ex.Message); continue; }
                            if (!ok) { Program.Info(S, n + " " + mode, $"status {pl.Status}: {pl.Error}"); continue; }
                            // the solid meant: the sphere (or the union of the two overlapping spheres) op b
                            DMesh3[] solids = bn == "self-overlapping" ? new[] { sphere, Shapes.Sphere(new Vector3d(0.3, 0, 0), 0.9, 6), b } : new[] { sphere, b };
                            Func<bool[], bool> inside = s => BooleanStage.Inside(op, bn == "self-overlapping" ? (s[0] || s[1]) : s[0], s[s.Length - 1]);
                            Program.Report(S, n + " " + mode + " closed manifold", Checks.ClosedManifold(pl.Result));
                            if (pl.Report.Clean) Program.Report(S, n + " " + mode + " clean as reported", MeshRobustRepair.NotCleanReason(pl.Result));
                            string w = Checks.SameSolid(pl.Result, solids, inside, 200, 31, 0.02);
                            Program.Report(S, n + " " + mode + " winding", w == "" ? "" : w + $" (repaired inputs: {string.Join(", ", pl.Report.RepairedInputs.Select(x => x.Index + " " + x.Method))})");
                        }
                    }
        }
    }
}
