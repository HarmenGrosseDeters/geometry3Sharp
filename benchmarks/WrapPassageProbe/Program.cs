// Study probe (docs: benchmarks/WrapPassageProbe/README.md): does MeshAlphaWrap keep a passage (a polyline axis with a
// pin radius) open, and how does that depend on alpha? Procedural parts only, no meshes on disk.
// Usage: WrapPassageProbe [--parts cross,angled,blind] [--radii 1.5,3,6] [--tri 0,1] [--ratios 0.5,0.8,...]
//                         [--offratio 30] [--simplify 1] [--out file.jsonl]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using gs;

static class Program
{
    const double H = 18, Margin = 6;

    static List<Vector3d> LocalAxis(string part)
    {
        double e = H + Margin;
        switch (part) {
            case "cross": return new List<Vector3d> { new Vector3d(0, 0, -e), new Vector3d(0, 0, e) };
            case "angled": return new List<Vector3d> { new Vector3d(0, 0, e), Vector3d.Zero, new Vector3d(e, 0, -0.5 * e) };
            case "blind": return new List<Vector3d> { new Vector3d(0, 0, e), Vector3d.Zero };
            default: throw new ArgumentException(part);
        }
    }

    static double SegDist(Vector3d p, Vector3d a, Vector3d b)
    {
        Vector3d ab = b - a; double t = (p - a).Dot(ab) / ab.LengthSquared; t = Math.Max(0, Math.Min(1, t));
        return (p - (a + t * ab)).Length;
    }

    sealed class PartSdf : ImplicitFunction3d
    {
        public List<Vector3d> Axis; public double R; public Matrix3d Rot; public Vector3d Shift;
        public double Value(ref Vector3d w)
        {
            Vector3d p = Rot.Transpose() * (w - Shift);   // local frame
            double box = Math.Max(Math.Abs(p.x) - H, Math.Max(Math.Abs(p.y) - H, Math.Abs(p.z) - H));
            double tube = double.MaxValue;
            for (int i = 0; i + 1 < Axis.Count; ++i) tube = Math.Min(tube, SegDist(p, Axis[i], Axis[i + 1]) - R);
            return Math.Max(box, -tube);
        }
    }

    // tri 0: axis aligned grid h = R/6; tri 1: rotated and shifted frame, h = R/4.5
    static DMesh3 Build(string part, double R, int tri, out List<Vector3d> worldAxis)
    {
        Matrix3d rot = Matrix3d.Identity; Vector3d shift = Vector3d.Zero; double h = R / 6;
        if (tri == 1) {
            rot = Matrix3d.AxisAngleD(new Vector3d(0.3, 0.5, 0.8).Normalized, 23);
            shift = new Vector3d(0.137, -0.211, 0.093); h = R / 4.5;
        }
        var axisLocal = LocalAxis(part);
        worldAxis = axisLocal.Select(p => rot * p + shift).ToList();
        var f = new PartSdf { Axis = axisLocal, R = R, Rot = rot, Shift = shift };
        double half = H + Margin;
        var mc = new MarchingCubes {
            Implicit = f, CubeSize = h, IsoValue = 0, RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 10,
            Bounds = new AxisAlignedBox3d(shift, 1.8 * half),
        };
        mc.Generate();
        return mc.Mesh;
    }

    // distance of the polyline to the mesh: certified [lo, hi] by branch and bound on the 1-Lipschitz distance function
    // (lower bound of an interval with end values fa, fb and length L: (fa + fb - L) / 2)
    static (double lo, double hi, int queries) Clearance(DMeshAABBTree3 tree, List<Vector3d> axis, double tol)
    {
        int queries = 0;
        double F(Vector3d p) { ++queries; tree.FindNearestTriangle(p, out double dsq); return Math.Sqrt(dsq); }
        var pq = new PriorityQueue<(Vector3d a, Vector3d b, double fa, double fb), double>();
        double U = double.MaxValue;
        for (int i = 0; i + 1 < axis.Count; ++i) {
            double fa = F(axis[i]), fb = F(axis[i + 1]);
            U = Math.Min(U, Math.Min(fa, fb));
            pq.Enqueue((axis[i], axis[i + 1], fa, fb), (fa + fb - (axis[i + 1] - axis[i]).Length) / 2);
        }
        double lo = U;
        while (pq.TryDequeue(out var it, out double lb)) {
            if (lb >= U - tol) { lo = Math.Min(U, lb); break; }
            Vector3d m = 0.5 * (it.a + it.b); double fm = F(m);
            U = Math.Min(U, fm);
            pq.Enqueue((it.a, m, it.fa, fm), (it.fa + fm - (m - it.a).Length) / 2);
            pq.Enqueue((m, it.b, fm, it.fb), (fm + it.fb - (it.b - m).Length) / 2);
        }
        return (Math.Max(0, lo), U, queries);
    }

    static string J(double v) { return double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture); }

    // measures of one wrap (raw or simplified) for the pin: clearance interval, winding numbers at the axis ends
    static string Measure(DMesh3 wrap, List<Vector3d> axis, double R, string tag)
    {
        var tree = new DMeshAABBTree3(wrap, true);
        var (lo, hi, q) = Clearance(tree, axis, 1e-3 * R);
        double w0 = tree.WindingNumber(axis[0]), w1 = tree.WindingNumber(axis[axis.Count - 1]);
        return $"\"{tag}_tris\":{wrap.TriangleCount},\"{tag}_clr_lo\":{J(lo)},\"{tag}_clr_hi\":{J(hi)},\"{tag}_w0\":{J(w0)},\"{tag}_w1\":{J(w1)}";
    }

    static void Main(string[] args)
    {
        var o = new Dictionary<string, string> {
            ["parts"] = "cross,angled,blind", ["radii"] = "1.5,3,6", ["tri"] = "0,1", ["ratios"] = "0.5,0.8,0.95,1.1,1.25,1.5,2,3",
            ["offratio"] = "30", ["simplify"] = "1", ["out"] = "wrap_passage.jsonl" };
        for (int i = 0; i + 1 < args.Length; i += 2) o[args[i].TrimStart('-')] = args[i + 1];
        double[] ParseD(string s) => s.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        double offRatio = double.Parse(o["offratio"], CultureInfo.InvariantCulture);
        bool simplify = o["simplify"] == "1";
        using var outFile = new StreamWriter(o["out"], true);
        foreach (string part in o["parts"].Split(','))
        foreach (double R in ParseD(o["radii"]))
        foreach (int tri in o["tri"].Split(',').Select(int.Parse)) {
            var sw = Stopwatch.StartNew();
            DMesh3 input = Build(part, R, tri, out var axis);
            double tBuild = sw.Elapsed.TotalSeconds;
            var inTree = new DMeshAABBTree3(input, true);
            var (rlo, rhi, _) = Clearance(inTree, axis, 1e-4 * R);
            Console.Error.WriteLine($"{part} R={R} tri={tri}: input {input.TriangleCount} tris, build {tBuild:F1}s, input clearance [{rlo:F4},{rhi:F4}]");
            foreach (double k in ParseD(o["ratios"])) {
                double alpha0 = rlo / (1 + 1 / offRatio);     // r - offset = alpha
                double alpha = k * alpha0, offset = alpha / offRatio;
                var w = new MeshAlphaWrap(input) { Alpha = alpha, Offset = offset, MaxThreads = 0 };
                sw.Restart();
                bool ok = w.Compute();
                double tWrap = sw.Elapsed.TotalSeconds;
                var sb = new StringBuilder();
                sb.Append($"{{\"part\":\"{part}\",\"R\":{J(R)},\"tri\":{tri},\"in_tris\":{input.TriangleCount},\"r_lo\":{J(rlo)},\"r_hi\":{J(rhi)},\"offratio\":{J(offRatio)}," +
                          $"\"ratio\":{J(k)},\"alpha\":{J(alpha)},\"offset\":{J(offset)},\"status\":\"{w.Status}\",\"raw_s\":{J(tWrap)},");
                double bound = rlo - offset > alpha ? Math.Sqrt((rlo - offset) * (rlo - offset) - alpha * alpha) : 0;
                sb.Append($"\"bound\":{J(bound)},");
                if (w.Result != null) {
                    sb.Append(Measure(w.Result, axis, R, "raw"));
                    if (simplify && ok) {
                        sw.Restart();
                        DMesh3 s = null; try {
                            s = MeshRobustRepair.SimplifyWithin(w.Result, 0.5 * offset, 0.5 * offset, true, () => false, out int _,
                                MeshRobustRepair.WrapSimplifyMinQuality, true, MeshRobustRepair.WrapSimplifySlabs, 0, null, input);
                        } catch (Exception e) { sb.Append($",\"simp_error\":\"{e.GetType().Name}\""); }
                        sb.Append($",\"simp_s\":{J(sw.Elapsed.TotalSeconds)}");
                        if (s != null) sb.Append("," + Measure(s, axis, R, "simp")); else sb.Append(",\"simp_tris\":null");
                    }
                }
                sb.Append("}");
                outFile.WriteLine(sb.ToString()); outFile.Flush();
                Console.Error.WriteLine($"  k={k} alpha={alpha:F3} {w.Status} {tWrap:F1}s  {sb.ToString().Substring(Math.Max(0, sb.Length - 220))}");
            }
        }
    }
}
