using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests;

// Sweep of ExactMeshImprint on ulp-shifted near-duplicates (docs/design/IMPRINT_SLIVER_CLEANUP.md, section 5; issue #1489):
// shape x axis x frame x shift (1 to 12 ulps, both signs) x cut direction (the shifted copy into the original and the original into the copy).
// Counts the runs that are not clean (NotCleanReason, RoundingClean) or fail. Public API only, so that it runs on any commit.
//   dotnet run -c Release --project benchmarks/ImprintSliverSweep            the table
//   dotnet run -c Release --project benchmarks/ImprintSliverSweep -- detail  one line per unclean run
static class Program
{
    static readonly double[] BoxCorners = {
        -0.6067795312896138, -0.9680060882596397, -0.58718209569113,
        1.1424598829891777, 0.0016131522330344594, -0.58718209569113,
        0.4933838391796905, 1.1725773320679964, -0.1778617090792985,
        -1.255855575099101, 0.20295809157532227, -0.1778617090792985,
        -0.4933838391796905, -1.1725773320679964, 0.1778617090792985,
        1.255855575099101, -0.20295809157532227, 0.1778617090792985,
        0.6067795312896138, 0.9680060882596397, 0.58718209569113,
        -1.1424598829891777, -0.0016131522330344594, 0.58718209569113 };

    // the same inputs as tests/geometry3Sharp.Tests/ExactMeshImprintSliverTests.cs (libm-free: the box by literals, the cylinder snapped to 2^-24)
    static DMesh3 Shape(string kind, bool capped = true)
    {
        if (kind == "box") {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)) }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToArray())
                m.SetVertex(v, new Vector3d(BoxCorners[3 * v], BoxCorners[3 * v + 1], BoxCorners[3 * v + 2]));
            return m;
        }
        DMesh3 c = new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.6f, Height = 2f, Slices = 12 }.Generate().MakeDMesh();
        const double grid = 1.0 / (1 << 24);
        foreach (int v in c.VertexIndices().ToArray()) {
            Vector3d p = c.GetVertex(v);
            c.SetVertex(v, new Vector3d(Math.Round(p.x / grid) * grid, Math.Round(p.y / grid) * grid, Math.Round(p.z / grid) * grid));
        }
        return c;
    }

    static double NextUp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);

    static DMesh3 Shifted(DMesh3 m, int axis, int ulps)
    {
        double maxAbs = m.Vertices().Max(p => Math.Abs(p[axis]));
        double step = ulps * (NextUp(maxAbs) - maxAbs);
        var b = new DMesh3(m);
        foreach (int v in b.VertexIndices().ToArray()) {
            Vector3d p = b.GetVertex(v);
            p[axis] += step;
            b.SetVertex(v, p);
        }
        return b;
    }

    static Similarity Frame(string name)
    {
        var t = new Vector3d(632117.25, 5712043.5, 113.75);
        switch (name) {
            case "origin": return Similarity.Identity;
            case "utm": return Similarity.Utm;
            case "utm-negated": return new Similarity(name, 1, Matrix3d.Identity, -t);
            case "mixed": return new Similarity(name, 1, Matrix3d.Identity, new Vector3d(t.x, -t.y, t.z));
            default: return new Similarity(name, 1, Matrix3d.Identity, new Vector3d(0, 0, t.z));     // z-only
        }
    }

    // sums over the runs of a cell: the repair's work (initial defects after the rounding, edits, runs where the final stage ran)
    static long sumDegenerate, sumIntersections, sumEdits, stageRuns;

    static string Check(DMesh3 target, DMesh3 cutter)
    {
        var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
        if (imprint.Compute() == false) return "FAILED " + imprint.Error;
        sumDegenerate += imprint.Counts["rounding_degenerate"]; sumIntersections += imprint.Counts["rounding_intersections"];
        sumEdits += imprint.Counts["rounding_edits"]; stageRuns += imprint.Counts["sliver_cleanup_stage"];
        string reason = MeshRobustRepair.NotCleanReason(imprint.Result);
        if (reason != "" || imprint.RoundingClean == false)
            return (reason == "" ? "rounding not clean" : reason) + " (degenerate " + imprint.Rounding.RemainingDegenerate + ", intersections "
                + imprint.Rounding.RemainingIntersections + (imprint.Rounding.EvaluationLimitReached ? ", evaluation limit" : "") + ")";
        return null;
    }

    static void Main(string[] args)
    {
        bool detail = args.Contains("detail");
        bool stats = args.Contains("stats");
        var cases = new (string kind, int axis)[] { ("box", 2), ("cylinder", 2), ("cylinder", 0) };
        var frames = new[] { "origin", "utm", "utm-negated", "mixed", "z-only" };
        Console.WriteLine("unclean runs out of 48 (shifts of 1 to 12 ulps, both signs, both cut directions)");
        if (stats) Console.WriteLine("cell: unclean (mean initial degenerate / mean initial intersections / mean edits / runs with the final stage)");
        Console.WriteLine("shape/axis".PadRight(14) + string.Join("", frames.Select(f => f.PadLeft(stats ? 24 : 13))));
        foreach (var (kind, axis) in cases) {
            var cells = new List<string>();
            foreach (string fr in frames) {
                Similarity s = Frame(fr);
                int bad = 0;
                sumDegenerate = sumIntersections = sumEdits = stageRuns = 0;
                DMesh3 a = Invariance.Transformed(Shape(kind), s);
                for (int ulps = -12; ulps <= 12; ++ulps) {
                    if (ulps == 0) continue;
                    DMesh3 b = Shifted(a, axis, ulps);
                    foreach (var (t, c, dir) in new[] { (a, b, "copy into original"), (b, a, "original into copy") }) {
                        string r = Check(t, c);
                        if (r == null) continue;
                        ++bad;
                        if (detail) Console.WriteLine("  " + kind + " axis " + axis + " " + fr + " shift " + ulps + " " + dir + ": " + r);
                    }
                }
                cells.Add((stats ? bad + " (" + sumDegenerate / 48 + "/" + sumIntersections / 48 + "/" + sumEdits / 48 + "/" + stageRuns + ")" : bad.ToString()).PadLeft(stats ? 24 : 13));
            }
            Console.WriteLine((kind + " " + "xyz"[axis]).PadRight(14) + string.Join("", cells));
        }
    }
}
