// The "cost" mode: the time and the effort of the joint stage (#1465) on given inputs, with the stage on and off (the internal switch
// MeshRoundingRepair.NoJointSearchOnThisThread, set by reflection). Cases: --family Cluster --k 30 --count 8 --seed 1 [--frame identity]
// (the generated degenerate families, Union with an empty mesh), or --plate "surface,n,dn,t,e,ratio,shift,D|Drev" (a case of the search mode).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    static int Cost(string[] args)
    {
        var specs = new List<(string, Func<(DMesh3, DMesh3, ExactMeshBoolean.Operations)>)>();
        int repeats = 5;
        string family = null, frame = "identity";
        int k = 30, count = 8, seed = 1, sphere = 6;
        long pairTests = 5000000;
        int onlyMode = -1;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--family") family = args[++i];
            else if (args[i] == "--k") k = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--count") count = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--seed") seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--mode") onlyMode = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--sphere") sphere = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--pairtests") pairTests = long.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--frame") frame = args[++i];
            else if (args[i] == "--repeats") repeats = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--plate") {
                string spec = args[++i];
                string[] f = spec.Split(',');
                specs.Add(("plate " + spec, () => {
                    var (hn, h) = Surfaces.First(x => x.Item1 == f[0]);
                    int n = int.Parse(f[1], CultureInfo.InvariantCulture), dn = int.Parse(f[2], CultureInfo.InvariantCulture);
                    double t = double.Parse(f[3], CultureInfo.InvariantCulture), e = double.Parse(f[4], CultureInfo.InvariantCulture);
                    double ratio = double.Parse(f[5], CultureInfo.InvariantCulture), shift = double.Parse(f[6], CultureInfo.InvariantCulture);
                    bool diff = f[7] == "D";
                    Func<Vector3d, Vector3d> sh = p => p + new Vector3d(shift, shift * 0.5, -shift * 0.25);
                    DMesh3 coarse = Plate(n, t, h, sh), fine = Plate(n + dn, t, h, p => sh(Rot(p, e, ratio * e)));
                    return (diff ? coarse : fine, diff ? fine : coarse, D);
                }));
            }
            else { Console.WriteLine("unknown argument " + args[i]); return 2; }
        }
        if (family != null) {
            var fam = (geometry3Sharp.Tests.DegenerateFamilies.Family)Enum.Parse(typeof(geometry3Sharp.Tests.DegenerateFamilies.Family), family);
            specs.Add(($"{family} k{k} count{count} seed{seed} sphere{sphere} {frame}", () => {
                DMesh3 mesh = geometry3Sharp.Tests.Invariance.Transformed(geometry3Sharp.Tests.DegenerateFamilies.Make(fam, k, seed, count, sphere), geometry3Sharp.Tests.Similarity.ByName(frame));
                return (mesh, new DMesh3(), ExactMeshBoolean.Operations.Union);
            }));
        }
        FieldInfo off = typeof(MeshRoundingRepair).GetField("NoJointSearchOnThisThread", BindingFlags.NonPublic | BindingFlags.Static);
        PropertyInfo evals = typeof(MeshRoundingRepair).GetProperty("Evaluations", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo noExt = typeof(MeshRoundingRepair).GetField("SkipJointRoundsOnThisThread", BindingFlags.NonPublic | BindingFlags.Static);
        // (the stage off, the free pairs of before the extension, the extension)
        foreach (var (name, make) in specs)
            foreach (int mode in new[] { 0, 1, 2 }) {
                if (onlyMode >= 0 && mode != onlyMode)
                    continue;
                bool joint = mode > 0;
                noExt.SetValue(null, mode == 1 ? 6 : 0);
                var times = new List<double>();
                string last = "";
                for (int r = 0; r < repeats + 1; ++r) {
                    var (a, b, op) = make();
                    off.SetValue(null, joint == false);
                    var sw = Stopwatch.StartNew();
                    var bo = new ExactMeshBoolean(a, b, op) { RoundingMaxPairTests = pairTests };
                    bool ok = bo.Compute();
                    sw.Stop();
                    off.SetValue(null, false);
                    if (r > 0) times.Add(sw.Elapsed.TotalMilliseconds);
                    MeshRoundingRepair rr = bo.Rounding;
                    last = rr == null ? "no repair" : $"degenerate {rr.RemainingDegenerate} intersections {rr.RemainingIntersections} limit {rr.EvaluationLimitReached} jointMoves {rr.JointMoves} withFixed {rr.JointMovesWithFixedCorner} evaluations {evals.GetValue(rr)} proposals {typeof(MeshRoundingRepair).GetField("JointSearchProposals", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rr)}";
                }
                times.Sort();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} | joint {1} | median {2:F0} ms | {3}", name, mode == 0 ? "off" : mode == 1 ? "free" : "ext ", times[times.Count / 2], last));
            }
        return 0;
    }
}
