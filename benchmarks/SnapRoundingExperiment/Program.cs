// The snap-rounding experiment (docs/design/SAFE_ROUNDING.md sections 3 and 6): SafeRounding against the prototypes of
// option A (separation by linear programs, MeshRoundingRepair.Separation) and option B (snap to a grid and resolve
// again, SnapFallback.cs), one case per call, so that a driver can run every case in its own process with a time limit
// (run.py; resumable, one JSON line per result).
//   list <set>                  the case names of a set: clusters (the 198 dense and folded clusters of
//                               SafeRoundingDegenerateInputTests), utm (the 114 cases of SafeRoundingBoundTests),
//                               cylinder (the six steps of ExactBooleanIssueCasesTests), booleans (the 198 of
//                               SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans)
//   run <case> <method>...      one JSON line per method: plain (SafeRounding off), safe (the default), again (safe, then
//                               resolve the result once more with the drift left), B (safe, then snap the defects'
//                               vertices to grids from fine to coarse and resolve again, within the drift left), A (safe
//                               with the separation stage), AB (A, then B)
//   A case is a set name and its parameters, eg cluster:FoldedCluster:44:8:1, utm:Cluster:30:4:1, cylinder:2,
//   bool:ShortEdge:44:Union:0, or file:<path> (the self-union of a mesh file, eg Thingi10K).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using geometry3Sharp.Tests;
using F = geometry3Sharp.Tests.DegenerateFamilies.Family;
using Ops = g3.ExactMeshBoolean.Operations;

static class Program
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "list") {
            foreach (string c in Cases(args[1]))
                Console.WriteLine(c);
            return 0;
        }
        if (args.Length >= 3 && args[0] == "run") {
            foreach (string method in args.Skip(2))
                Console.WriteLine(Run(args[1], method));
            return 0;
        }
        Console.Error.WriteLine("usage: list <set> | run <case> <method>...");
        return 1;
    }

    static IEnumerable<string> Cases(string set)
    {
        switch (set) {
            case "clusters":
                foreach (F f in new[] { F.Cluster, F.FoldedCluster })
                    foreach (int k in new[] { 0, 30, 36, 40, 44, 52 })
                        foreach (int count in new[] { 2, 4, 8 })
                            for (int seed = 0; seed < 6; seed++)
                                if (f != F.FoldedCluster || k != 0)
                                    yield return $"cluster:{f}:{k}:{count}:{seed}";
                break;
            case "utm":
                foreach (F f in new[] { F.Cluster, F.FoldedCluster, F.Collinear, F.Sliver })
                    foreach (int k in new[] { 0, 30, 36, 44, 52 })
                        foreach (int count in new[] { 2, 4 })
                            for (int seed = 0; seed < 3; seed++)
                                if (k != 0 || f != F.Sliver)
                                    yield return $"utm:{f}:{k}:{count}:{seed}";
                break;
            case "cylinder":
                for (int step = 0; step < 6; step++)
                    yield return $"cylinder:{step}";
                break;
            case "booleans":
                foreach (F f in new[] { F.Collinear, F.Sliver, F.ShortEdge, F.Coincident, F.Unwelded, F.Cluster })
                    foreach (int k in new[] { 0, 36, 44, 52 })
                        foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                            for (int seed = 0; seed < 3; seed++)
                                if (k != 0 || (f != F.Sliver && f != F.ShortEdge))
                                    yield return $"bool:{f}:{k}:{op}:{seed}";
                break;
            default:
                throw new ArgumentException("unknown set " + set);
        }
    }

    // the operands of a case (B is empty for the self-unions)
    static (DMesh3 a, DMesh3 b, Ops op) Input(string name)
    {
        string[] p = name.Split(':');
        switch (p[0]) {
            case "cluster":
                return (DegenerateFamilies.Make(Enum.Parse<F>(p[1]), int.Parse(p[2]), int.Parse(p[4]), int.Parse(p[3])), new DMesh3(), Ops.Union);
            case "utm": {
                // the similarity "rotated, scale 1e-3, utm" of tests/geometry3Sharp.Tests/Harness/Invariance.cs
                DMesh3 m = DegenerateFamilies.Make(Enum.Parse<F>(p[1]), int.Parse(p[2]), int.Parse(p[4]), int.Parse(p[3]));
                Matrix3d rot = Matrix3d.AxisAngleD(Vector3d.AxisY, 23);
                var t = new Vector3d(632117.25, 5712043.5, 113.75);
                foreach (int vid in m.VertexIndices().ToArray())
                    m.SetVertex(vid, 1e-3 * (rot * m.GetVertex(vid)) + t);
                return (m, new DMesh3(), Ops.Union);
            }
            case "cylinder": {
                // ExactBooleanIssueCasesTests.CylinderToolSteppedDown_RepeatedDifference: the work piece of each step is
                // the previous step's result with the default SafeRounding
                int steps = int.Parse(p[1]);
                DMesh3 work = Box(-3, -3, -10, 3, 3, 0);
                DMesh3 tool = new CappedCylinderGenerator() { BaseRadius = 1.3f, TopRadius = 1.3f, Height = 2.5f, Slices = 24 }.Generate().MakeDMesh();
                MeshTransforms.Rotate(tool, Vector3d.Zero, Quaterniond.AxisAngleD(Vector3d.AxisX, 90));
                MeshTransforms.Translate(tool, new Vector3d(0.17, -0.11, -1.5));
                for (int step = 0; step < steps; step++) {
                    var d = new ExactMeshBoolean(work, tool, Ops.Difference);
                    if (!d.Compute()) throw new InvalidOperationException(d.Error);
                    work = d.Result;
                    MeshTransforms.Translate(tool, new Vector3d(0, 0, -1));
                }
                return (work, tool, Ops.Difference);
            }
            case "bool": {
                int seed = int.Parse(p[4]);
                F f = Enum.Parse<F>(p[1]);
                DMesh3 a = DegenerateFamilies.Make(f, int.Parse(p[2]), seed, f == F.Cluster ? 4 : 3);
                DMesh3 b = DegenerateFamilies.Box(new Vector3d(-0.3 + 0.1 * seed, -2, -2), new Vector3d(2, 2, 2));
                return (a, b, Enum.Parse<Ops>(p[3]));
            }
            case "file": {
                DMesh3 m = StandardMeshReader.ReadMesh(name.Substring(5));
                if (m == null) throw new IOException("cannot read " + name);
                return (m, new DMesh3(), Ops.Union);
            }
        }
        throw new ArgumentException("unknown case " + name);
    }

    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
        DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
        foreach (int vid in mesh.VertexIndices().ToArray()) {
            Vector3d v = mesh.GetVertex(vid);
            mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
        }
        return mesh;
    }

    static string Run(string name, string method)
    {
        var (a, b, op) = Input(name);
        var sb = new StringBuilder();
        sb.Append("{\"case\":\"").Append(name.Replace("\\", "/").Replace("\"", "'")).Append("\",\"method\":\"").Append(method).Append('"');
        var clock = Stopwatch.StartNew();
        var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { SafeRounding = method != "plain" };
        bool ok;
        try { ok = boolean.Compute(); } catch (Exception ex) { return sb.Append(",\"ok\":false,\"error\":\"").Append(ex.GetType().Name).Append("\"}").ToString(); }
        if (!ok)
            return sb.Append(",\"ok\":false,\"error\":\"").Append(boolean.Error?.Replace("\"", "'")).Append("\"}").ToString();
        DMesh3 result = boolean.Result;
        MeshRoundingRepair r = boolean.Rounding;
        // A: the separation stage on SafeRounding's repair, in place on the result (MeshRoundingRepair.Separation.cs)
        if ((method == "A" || method == "AB") && r != null && r.IsClean == false)
        {
            if (Environment.GetEnvironmentVariable("SEP_DEBUG") == "1")
                r.SeparationLog = (msg) => Console.Error.WriteLine(msg);
            r.Separate();
        }
        double drift = r?.MaxDrift ?? 0, maxLength = r?.MaxLength ?? 0;
        SnapFallback fb = null;
        if (method == "again" || method == "B" || method == "AB") {
            fb = new SnapFallback(result, r) { SnapGrids = method != "again" };
            fb.Apply();
            result = fb.Result;
            drift = fb.Drift;
        }
        double ms = clock.Elapsed.TotalMilliseconds;
        string reasons = gs.MeshRobustRepair.NotCleanReasons(result);
        bool roundingClean = method == "plain" ? reasons == "" : (fb != null ? fb.Clean : r.IsClean);
        double largest = Largest(result);
        sb.Append(",\"ok\":true");
        sb.Append(",\"clean\":").Append(roundingClean && reasons == "" ? "true" : "false");
        sb.Append(",\"rounding_clean\":").Append(roundingClean ? "true" : "false");
        sb.Append(",\"reasons\":\"").Append(reasons).Append('"');
        sb.Append(",\"tris\":").Append(result.TriangleCount);
        sb.Append(",\"ms\":").Append(ms.ToString("F1", ci));
        sb.Append(",\"max_drift\":").Append(drift.ToString("R", ci));
        sb.Append(",\"max_length\":").Append(maxLength.ToString("R", ci));
        sb.Append(",\"drift_ulps\":").Append((drift / Ulp(largest)).ToString("G4", ci));
        sb.Append(",\"diag\":").Append(gs.MeshRepairMetrics.TriangleBounds(result).DiagonalLength.ToString("R", ci));
        if (r != null) {
            sb.Append(",\"removed_components\":").Append(r.RemovedComponents + (fb?.RemovedComponents ?? 0));
            sb.Append(",\"edits\":").Append(r.Collapses + r.Flips + r.Moves + r.Zips + r.VertexRemovals);
            sb.Append(",\"separations\":").Append(r.Separations).Append(",\"programs\":").Append(r.SeparationPrograms).Append(",\"targets\":").Append(r.SeparationTargetsReached);
        }
        if (fb != null)
            sb.Append(",\"b_rounds\":").Append(fb.Rounds).Append(",\"b_grid_ulps\":").Append(fb.GridUlps.ToString("G4", ci));
        if (method != "plain" && Environment.GetEnvironmentVariable("SNAP_CERT") != "0") {
            // the d-separation certificate (tests/geometry3Sharp.Tests/Harness/SeparationCertificate.cs): disjoint features farther apart than 2e
            if (Environment.GetEnvironmentVariable("SEP_DEBUG") == "1") SeparationCertificate.Log = (msg) => Console.Error.WriteLine(msg);
            var cert = SeparationCertificate.Check(result);
            sb.Append(",\"cert_pairs\":").Append(cert.Pairs).Append(",\"uncertified\":").Append(cert.Uncertified);
            if (cert.Capped) sb.Append(",\"cert_capped\":true");
        }
        if (method != "plain") {
            // Hausdorff distance to the exact result, rounded (SafeRounding off): an upper bound of the distance to the
            // exact one, up to the rounding of its new vertices (half an ulp per coordinate)
            var raw = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { SafeRounding = false };
            if (raw.Compute() && raw.Result.TriangleCount > 0 && result.TriangleCount > 0) {
                var hd = new gs.MeshHausdorffDistance(result, raw.Result) { Tolerance = Math.Max(1e-2 * maxLength, 1e-300), MaxSubdivisions = 2000000 };
                if (hd.Compute())
                    sb.Append(",\"hausdorff\":").Append(hd.UpperBound.ToString("R", ci));
            }
        }
        return sb.Append('}').ToString();
    }

    internal static double Largest(DMesh3 m)
    {
        AxisAlignedBox3d bounds = gs.MeshRepairMetrics.TriangleBounds(m);
        return Math.Max(bounds.Min.MaxAbs, bounds.Max.MaxAbs);
    }

    // a unit in the last place of x (a power of two)
    internal static double Ulp(double x)
    {
        if (!(x > 0) || double.IsInfinity(x)) return double.Epsilon;
        long bits = BitConverter.DoubleToInt64Bits(x) & 0x7FF0000000000000L;
        return Math.Max(BitConverter.Int64BitsToDouble(bits) * 2.220446049250313e-16, double.Epsilon);
    }
}
