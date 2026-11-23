// Comparison of mesh boolean implementations on the same inputs (see README.md).
//   export <mesh> <dir>                  A = the mesh, B = a rotated, shifted copy (as in MeshRepairBenchmark),
//                                        written as A.off / B.off with round-trip precision; info.json
//   pair <meshA> <meshB> <dir>           A and B as given (no transformation); A.off / B.off, info.json
//   thingi-pair <meshA> <meshB> <dir> <qx qy qz qw tx ty tz>
//                                        a pair of the thingi-pairs class (ThingiPairs.cs): both meshes normalized to the unit
//                                        box, B rotated by the quaternion and translated; A.off / B.off, info.json
//   thingi-contact <meshA> <meshB> <dir> <variant> <axis> <sign> <param>
//                                        a pair of the thingi-contact class (ThingiContact.cs, ContactTransforms.cs): B placed
//                                        against A by an exact translation or reflection (face contact, ulp shift, mirror)
//   abc-chain-inputs <A> <B> <C> <dir> <variantB> <axisB> <signB> <paramB> <variantC> <axisC> <signC> <paramC>
//                                        the inputs of a chain of the abc-chained class (AbcChained.cs): B and C placed against A
//                                        by exact placements; A.off, B.off, C.off, chain.json
//   chain-step <A.off> <B.off> <dir> <name>
//                                        one step of a chain: A.off, B.off and info.json of the step's inputs (results of earlier steps)
//   thingi-filter <out.csv> <mesh>...    id, triangles and MeshRobustRepair.NotCleanReason of each mesh (appends, skips ids
//                                        that are in out.csv already)
//   synth <dir>                          synthetic hard cases (touching, coplanar, thin, large coordinates,
//                                        scale differences) and iterated sequences; writes <dir>/cases.jsonl
//   run <A.off> <B.off> <op> <out.off>   ExactMeshBoolean; prints {"ms":..} (boolean only, without I/O)
//   imprint <A.off> <B.off> <method> <op>
//                                        the imprint comparison (ImprintCompare.cs): exact_imprint / float_cut with op a_by_b or b_by_a,
//                                        exact_union / float_union with op union; prints one JSON line with the status, ms and checks
//                                        (run and obb: BOOLCMP_SAFE_ROUNDING=0|1 sets ExactMeshBoolean.SafeRounding)
//   obb --request <r.json> --result <r.json> [--write-last-only]
//                                        runner for the Open Boolean Benchmark protocol (obb-runner/README.md)
//   eval <dir>                           metrics of all results <method>_<op>.(off|obj) in dir, one JSON line per method
//   nary grid|spheres <n> [variant]...   many meshes in one ExactMeshBoolean against chained pairwise booleans (Nary.cs)
//   components <mesh> <dir>              the connected components of the mesh, one input each (N000.off, ...): the
//                                        n-ary union of overlapping shells (an STL merged before printing)
//   array <mesh> <dir> <n>               n rotated, shifted copies of the mesh that overlap their neighbours (N000.off, ...)
//   profile [options] <case>...          phase times, allocations, GC, predicate filter failures and result hashes
//                                        of ExactMeshBoolean (Profile.cs)
//   seam-levers [options]                upper-bound probes on procedural operands with a small intersection seam
//                                        (SeamLevers.cs; docs/research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using g3;
using gs;

static class Program
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // BOOLCMP_SAFE_ROUNDING=0|1: SafeRounding off or on; unset: the library's default
    internal static ExactMeshBoolean Configure(ExactMeshBoolean boolean)
    {
        string s = Environment.GetEnvironmentVariable("BOOLCMP_SAFE_ROUNDING");
        if (s == "0" || s == "1")
            boolean.SafeRounding = s == "1";
        // BOOLCMP_ROUNDING_MAX_PAIR_TESTS=<n>: SafeRounding's effort budget (RC4a measurements)
        string b = Environment.GetEnvironmentVariable("BOOLCMP_ROUNDING_MAX_PAIR_TESTS");
        if (long.TryParse(b, NumberStyles.Integer, ci, out long budget) && budget >= 0)
            boolean.RoundingMaxPairTests = budget;
        // BOOLCMP_ROUNDING_DEFECT_SCALE=<x>: MeshRoundingRepair.PairTestsDefectScale (0: the fixed budget of before)
        string ds = Environment.GetEnvironmentVariable("BOOLCMP_ROUNDING_DEFECT_SCALE");
        if (double.TryParse(ds, NumberStyles.Float, ci, out double scale))
            boolean.RoundingPairTestsDefectScale = scale;
        return boolean;
    }

    static int Main(string[] args)
    {
        switch (args[0]) {
            case "export": return Export(args[1], args[2]);
            case "pair": return Pair(args[1], args[2], args[3]);
            case "components": return Components(args[1], args[2]);
            case "array": return ArrayCase(args[1], args[2], int.Parse(args[3]));
            case "thingi-pair": return ThingiPairs.Pair(args.Skip(1).ToArray());
            case "thingi-filter": return ThingiPairs.Filter(args.Skip(1).ToArray());
            case "thingi-contact": return ThingiContact.Pair(args.Skip(1).ToArray());
            case "imprint": return ImprintCompare.Run(args.Skip(1).ToArray());
            case "abc-chain-inputs": return AbcChained.Inputs(args.Skip(1).ToArray());
            case "chain-step": return AbcChained.Step(args.Skip(1).ToArray());
            case "chain-exact": return ChainExact.Abc(args.Skip(1).ToArray());
            case "chain-exact-proc": return ChainExact.Procedural(args.Skip(1).ToArray());
            case "chain-exact-diag": return ChainExact.Abc(args.Skip(1).ToArray(), "diag");
            case "chain-exact-diag-proc": return ChainExact.Procedural(args.Skip(1).ToArray(), "diag");
            case "chain-exact-time": return ChainExact.Abc(args.Skip(1).ToArray(), "time");
            case "chain-exact-time-proc": return ChainExact.Procedural(args.Skip(1).ToArray(), "time");
            case "chain-ab": return ChainExact.Abc(args.Skip(1).ToArray(), "ab");
            case "chain-ab-proc": return ChainExact.Procedural(args.Skip(1).ToArray(), "ab");
            case "synth": return Synthetic.Write(args[1]);
            case "knot": return Knot.Run(args.Skip(1).ToArray());
            case "run": return Run(args[1], args[2], args[3], args[4]);
            case "run-pipeline": return RunPipeline(args[1], args[2], args[3], args[4], args.Length > 5 ? args[5] : "default");
            case "obb": return ObbRunner.Run(args.Skip(1).ToArray());
            case "eval": return Eval(args[1]);
            case "nary": return Nary.Run(args.Skip(1).ToArray());
            case "certify": return Certify.Run(args.Skip(1).ToArray());
            case "coplanar-clip": return CoplanarClip.Run(args.Skip(1).ToArray());
            case "profile": return Profile.Run(args.Skip(1).ToArray());
            case "seam-levers": return SeamLevers.Run(args.Skip(1).ToArray());
            case "indirect": return IndirectBench.Run(args.Skip(1).ToArray());
            case "envelope": return EnvelopeBench.Run(args.Skip(1).ToArray());
            case "prestudy-fwn": return PrestudyFwn.Run(args.Skip(1).ToArray());
            case "antipodal-ab": return AntipodalAB.Run(args.Skip(1).ToArray());
            case "prestudy-antipodal": return PrestudyAntipodal.Run(args.Skip(1).ToArray());
            case "prestudy-many-a": return PrestudyMany.RunA(args.Skip(1).ToArray());
            case "t3a-cull-share": return PrestudyMany.RunCullShare(args.Skip(1).ToArray());
            case "t3a-ab": return PrestudyMany.RunCullAB(args.Skip(1).ToArray());
            case "t3a-geom": return T3aGeom.Run(args.Skip(1).ToArray());
            case "t3b-bound": return PrestudyMany.RunEachBound(args.Skip(1).ToArray());
            case "t3b-each": return PrestudyMany.RunEach(args.Skip(1).ToArray());
            case "t3b-grid": return PrestudyMany.RunEachGrid(args.Skip(1).ToArray());
            case "prestudy-many-b": return PrestudyMany.RunB(args.Skip(1).ToArray());
            case "prestudy-memory": case "prestudy-exact": case "prestudy-csg": case "prestudy-csg-thingi": return Prestudy.Run(args[0], args.Skip(1).ToArray());
        }
        Console.Error.WriteLine("unknown mode " + args[0]);
        return 1;
    }

    internal static DMesh3 Load(string meshFile)
    {
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(meshFile, ReadOptions.Defaults, builder);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0)
            return null;
        DMesh3 a = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i)
            MeshEditor.Append(a, builder.Meshes[i]);
        return a;
    }

    static int Export(string meshFile, string dir)
    {
        DMesh3 a = Load(meshFile);
        if (a == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        WriteCase(a, b, dir, Path.GetFileName(meshFile));
        return 0;
    }

    // the n-ary cases (profile runs the one-pass union of all N*.off of a case directory)
    static int Components(string meshFile, string dir)
    {
        DMesh3 a = Load(meshFile);
        if (a == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        DMesh3[] parts = MeshConnectedComponents.Separate(a);
        WriteInputs(parts.ToList(), dir, Path.GetFileName(meshFile));
        return 0;
    }

    static int ArrayCase(string meshFile, string dir, int n)
    {
        DMesh3 a = Load(meshFile);
        if (a == null || n < 1) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        // copies on a ring, each about 0.6 of the mesh size from its neighbours and rotated about a random axis:
        // every copy overlaps the next ones (deterministic: fixed seed)
        AxisAlignedBox3d bounds = a.CachedBounds;
        double size = bounds.MaxDim, radius = 0.6 * size * n / (2 * Math.PI);
        var rand = new Random(n);
        var parts = new List<DMesh3>();
        for (int k = 0; k < n; ++k) {
            DMesh3 m = new DMesh3(a);
            Vector3d axis = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5).Normalized;
            MeshTransforms.Rotate(m, bounds.Center, Quaterniond.AxisAngleD(axis, 360 * rand.NextDouble()));
            double t = 2 * Math.PI * k / n;
            MeshTransforms.Translate(m, new Vector3d(radius * Math.Cos(t), radius * Math.Sin(t), 0.1 * size * rand.NextDouble()));
            parts.Add(m);
        }
        WriteInputs(parts, dir, Path.GetFileName(meshFile));
        return 0;
    }

    static void WriteInputs(List<DMesh3> parts, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        for (int k = 0; k < parts.Count; ++k)
            WriteOFF(parts[k], Path.Combine(dir, $"N{k:D3}.off"));
        var info = new Dictionary<string, object> { ["file"] = name, ["inputs"] = parts.Count, ["tris"] = parts.Sum(m => m.TriangleCount) };
        File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(info));
        Console.WriteLine(JsonSerializer.Serialize(info));
    }

    static int Pair(string fa, string fb, string dir)
    {
        DMesh3 a = Load(fa), b = Load(fb);
        if (a == null || b == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        WriteCase(a, b, dir, Path.GetFileName(fa) + "+" + Path.GetFileName(fb));
        return 0;
    }

    // A.off, B.off and info.json with the input properties of both meshes and the reference volumes
    internal static Dictionary<string, object> WriteCase(DMesh3 a, DMesh3 b, string dir, string name, Dictionary<string, object> extra = null, bool inputsClean = false)
    {
        Directory.CreateDirectory(dir);
        WriteOFF(a, Path.Combine(dir, "A.off"));
        WriteOFF(b, Path.Combine(dir, "B.off"));
        // reference volumes of the solids (self-intersections resolved exactly)
        // inputsClean: the caller has checked NotCleanReason == "" (closed, manifold, no self-intersections, positive
        // volume), so the volumes are those of the meshes themselves and the checks are skipped
        DMesh3 ra = inputsClean ? a : ExactMeshBoolean.Resolve(a), rb = inputsClean ? b : ExactMeshBoolean.Resolve(b);
        int sa = inputsClean ? 0 : SelfIntersections(a), sb = inputsClean ? 0 : SelfIntersections(b);
        // volumes are measured relative to this point (eval uses it too): with coordinates far from the origin
        // (UTM), the sum of a·(b×c) over the triangles would cancel catastrophically
        Vector3d center = a.CachedBounds.Center;
        var info = new Dictionary<string, object> {
            ["file"] = name, ["tris"] = a.TriangleCount + b.TriangleCount,
            // properties of both inputs: the case is valid for CGAL only if both are closed and self-intersection free
            ["closed"] = inputsClean || a.IsClosed() && b.IsClosed(),
            ["wclosed"] = inputsClean || ExactMeshBoolean.IsWindingClosed(a) && ExactMeshBoolean.IsWindingClosed(b),
            ["selfint"] = sa < 0 || sb < 0 ? -1 : sa + sb,
            ["vA"] = ra != null ? Volume(ra, center) : double.NaN, ["vB"] = rb != null ? Volume(rb, center) : double.NaN,
            ["center"] = new[] { center.x, center.y, center.z },
        };
        foreach (var kv in extra ?? new Dictionary<string, object>())
            info[kv.Key] = kv.Value;
        File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(info));
        Console.WriteLine(JsonSerializer.Serialize(info));
        return info;
    }

    static int SelfIntersections(DMesh3 m)
    {
        return m.TriangleCount <= 300000 ? MeshRepairMetrics.SelfIntersections(m) : -1;
    }

    static int Run(string fa, string fb, string op, string fout)
    {
        DMesh3 a = StandardMeshReader.ReadMesh(fa), b = StandardMeshReader.ReadMesh(fb);
        var ops = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };
        // warm-up on a small problem, so that the time does not include the JIT compilation (the other
        // implementations are compiled ahead of time)
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        foreach (var o in ops.Values)
            Configure(new ExactMeshBoolean(w1, w2, o) { AllowOpenMeshes = true }).Compute();
        int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
        TimeSpan pause0 = NetCompat.GcPause();
        var sw = Stopwatch.StartNew();
        var boolean = Configure(new ExactMeshBoolean(a, b, ops[op]) { AllowOpenMeshes = true });
        bool ok = boolean.Compute();
        long ms = sw.ElapsedMilliseconds;
        var gc = new Dictionary<string, object> { ["gen0"] = GC.CollectionCount(0) - gc0, ["gen2"] = GC.CollectionCount(2) - gc2,
            ["pause_ms"] = (long)(NetCompat.GcPause() - pause0).TotalMilliseconds };
        if (ok)
            WriteOFF(boolean.Result, fout);
        var r = new Dictionary<string, object> { ["ms"] = ms, ["ok"] = ok, ["phases"] = boolean.PhaseMilliseconds, ["counts"] = boolean.Counts, ["gc"] = gc };
        if (!ok) r["error"] = boolean.Error;
        if (boolean.Rounding != null) {
            r["rounding_clean"] = boolean.RoundingClean;
            // what the repair left and why it stopped (RC4 of docs/research/BOOLEAN_TRIAGE_2026-11.md)
            MeshRoundingRepair rr = boolean.Rounding;
            r["rounding"] = new Dictionary<string, object> {
                ["remaining_intersections"] = rr.RemainingIntersections, ["remaining_degenerate"] = rr.RemainingDegenerate,
                ["remaining_false_contacts"] = rr.RemainingFalseContacts, ["evaluation_limit_reached"] = rr.EvaluationLimitReached,
                ["smallest_rejected_drift"] = Finite(rr.SmallestRejectedDrift),
                ["max_drift"] = Finite(rr.MaxDrift), ["max_length"] = Finite(rr.MaxLength), ["scale_disparity"] = rr.ScaleDisparity,
                ["removed_components"] = rr.RemovedComponents, ["edits"] = rr.Collapses + rr.Flips + rr.Moves + rr.Zips + rr.VertexRemovals,
                ["pair_tests"] = rr.PairTests, ["evaluations"] = rr.Evaluations, ["max_evaluations"] = rr.MaxEvaluations,
                ["max_pair_tests"] = rr.MaxPairTests, ["pair_budget"] = rr.PairBudget, ["stopped_by_pair_tests"] = rr.StoppedByPairTests,
                ["triangles_in"] = boolean.Counts.TryGetValue("pieces", out long pc) ? pc : -1,
                // the two candidate verdicts on what the removal took (#1216): the library's (RemovedResolvable: 256 ulps and
                // 1e-3 of the thin bound) and the one by ulps alone; and the removed components themselves (the first 50)
                ["removed_resolvable"] = rr.RemovedResolvable,
                ["removed_defective_256ulps"] = rr.RemovedComponentInfos.Count(c => c.ThicknessUlps >= 256
                    && (c.Reason == MeshRoundingRepair.RemovalReasons.StillDefective || c.Reason == MeshRoundingRepair.RemovalReasons.VolumeChanged)),
                ["removed"] = rr.RemovedComponentInfos.Take(50).Select(c => new Dictionary<string, object> {
                    ["reason"] = c.Reason.ToString(), ["triangles"] = c.Triangles, ["thickness"] = Finite(c.Thickness),
                    ["thickness_ulps"] = Finite(c.ThicknessUlps), ["resolvable"] = c.Resolvable }).ToList(),
            };
        }
        Console.WriteLine(JsonSerializer.Serialize(r));
        return 0;
    }

    // run-pipeline <A> <B> <op> <out.off> [default|cad]
    // The operation through SolidBooleanPipeline (input checks, the exact boolean, the reduction to its defaults,
    // the final check) instead of the bare ExactMeshBoolean of `run`. Variant default: the pipeline's defaults. Variant cad:
    // the settings for faceted CAD inputs, InputGrid = 2^(floor(log2(largest box dimension of the inputs)) - 20), a grid
    // relative to the size of the model (the exact snap of the inputs). The result is the pipeline's Result (closed,
    // reduced); "ok" is Status Success, an empty result included.
    static int RunPipeline(string fa, string fb, string op, string fout, string variant)
    {
        DMesh3 a = StandardMeshReader.ReadMesh(fa), b = StandardMeshReader.ReadMesh(fb);
        var ops = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };
        if (variant != "default" && variant != "cad")
            throw new ArgumentException("variant must be default or cad: " + variant);
        // warm-up on a small problem (the time does not include the JIT compilation), as in run
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        foreach (var o in ops.Values)
            new SolidBooleanPipeline(new List<DMesh3> { w1, w2 }, o).Compute();
        var pipeline = new SolidBooleanPipeline(new List<DMesh3> { a, b }, ops[op]) { ConfigureBoolean = bo => Configure(bo) };
        if (variant == "cad") {
            AxisAlignedBox3d box = a.GetBounds();
            box.Contain(b.GetBounds());
            double size = Math.Max(box.MaxDim, double.Epsilon);
            pipeline.InputGrid = Math.Pow(2, Math.Floor(Math.Log(size, 2)) - 20);
        }
        var sw = Stopwatch.StartNew();
        bool ok = pipeline.Compute();
        long ms = sw.ElapsedMilliseconds;
        if (ok)
            WriteOFF(pipeline.Result, fout);
        SolidBooleanReport rep = pipeline.Report;
        var r = new Dictionary<string, object> {
            ["ms"] = ms, ["ok"] = ok, ["pipeline_status"] = pipeline.Status.ToString(), ["variant"] = variant,
            ["input_grid"] = pipeline.InputGrid,
            ["pipeline"] = new Dictionary<string, object> {
                ["not_clean"] = rep.NotCleanReason, ["boolean_tris"] = rep.BooleanTriangles, ["result_tris"] = rep.ResultTriangles,
                ["contacts_boolean"] = rep.ContactsBoolean, ["contacts_result"] = rep.ContactsResult,
                ["thin_components_kept"] = rep.ThinComponentsKept, ["input_snapped_vertices"] = rep.InputSnappedVertices,
                ["repaired_inputs"] = rep.RepairedInputs.Count,
                ["boolean_ms"] = rep.BooleanMilliseconds, ["reduction_ms"] = rep.ReductionMilliseconds, ["total_ms"] = rep.TotalMilliseconds,
            },
        };
        if (!ok)
            r["error"] = pipeline.Error;
        Console.WriteLine(JsonSerializer.Serialize(r));
        return 0;
    }

    // (JSON has no infinity or NaN: null)
    static object Finite(double x) => double.IsNaN(x) || double.IsInfinity(x) ? null : (object)x;

    internal static void WriteOFF(DMesh3 mesh, string path)
    {
        var sb = new StringBuilder();
        sb.Append("OFF\n").Append(mesh.VertexCount).Append(' ').Append(mesh.TriangleCount).Append(" 0\n");
        var map = new Dictionary<int, int>();
        foreach (int vid in mesh.VertexIndices()) {
            map[vid] = map.Count;
            Vector3d v = mesh.GetVertex(vid);
            sb.Append(v.x.ToString("R", ci)).Append(' ').Append(v.y.ToString("R", ci)).Append(' ').Append(v.z.ToString("R", ci)).Append('\n');
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            sb.Append("3 ").Append(map[t.a]).Append(' ').Append(map[t.b]).Append(' ').Append(map[t.c]).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    // triangle soup from OFF or OBJ (no welding beyond the file's own vertex indices)
    static bool ReadSoup(string path, out List<Vector3d> v, out List<Index3i> t)
    {
        v = new List<Vector3d>(); t = new List<Index3i>();
        if (File.Exists(path) == false)
            return false;
        string[] lines = File.ReadAllLines(path);
        if (path.EndsWith(".off")) {
            int k = 0;
            while (lines[k].Trim().Length == 0 || lines[k].StartsWith("#") || lines[k].Trim() == "OFF") k++;
            string[] counts = lines[k++].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int nv = int.Parse(counts[0]), nt = int.Parse(counts[1]);
            for (int i = 0; i < nv; ++i, ++k) {
                string[] s = lines[k].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                v.Add(new Vector3d(double.Parse(s[0], ci), double.Parse(s[1], ci), double.Parse(s[2], ci)));
            }
            for (int i = 0; i < nt; ++i, ++k) {
                string[] s = lines[k].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                int n = int.Parse(s[0]);
                for (int j = 2; j < n; ++j)
                    t.Add(new Index3i(int.Parse(s[1]), int.Parse(s[j]), int.Parse(s[j + 1])));
            }
        } else {
            foreach (string line in lines) {
                string[] s = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (s.Length == 0) continue;
                if (s[0] == "v")
                    v.Add(new Vector3d(double.Parse(s[1], ci), double.Parse(s[2], ci), double.Parse(s[3], ci)));
                else if (s[0] == "f") {
                    int[] f = s.Skip(1).Select(x => int.Parse(x.Split('/')[0]) - 1).ToArray();
                    for (int j = 1; j + 1 < f.Length; ++j)
                        t.Add(new Index3i(f[0], f[j], f[j + 1]));
                }
            }
        }
        return true;
    }

    internal static double Volume(DMesh3 m, Vector3d center)
    {
        double volume = 0;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a) - center, b = m.GetVertex(t.b) - center, c = m.GetVertex(t.c) - center;
            volume += a.Dot(b.Cross(c)) / 6.0;
        }
        return volume;
    }

    // the default hash of a long is lo ^ hi = x ^ y for the key of an edge (x, y): neighbouring vertices have close
    // indices, so very few distinct hashes, and a result of 1M triangles took more than 30 minutes to evaluate
    sealed class EdgeKeyComparer : IEqualityComparer<long>
    {
        public bool Equals(long a, long b) => a == b;
        public int GetHashCode(long k) => (int)(((ulong)k * 0x9E3779B97F4A7C15UL) >> 32);
    }

    static int Eval(string dir)
    {
        var info = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(dir, "info.json")));
        double vA = info["vA"].GetDouble(), vB = info["vB"].GetDouble();
        Vector3d center = info.TryGetValue("center", out JsonElement ce)
            ? new Vector3d(ce[0].GetDouble(), ce[1].GetDouble(), ce[2].GetDouble()) : Vector3d.Zero;
        // the results to evaluate: the three operations, or the final result of a sequence
        string[] ops = info.TryGetValue("ops", out JsonElement o) ? o.EnumerateArray().Select(x => x.GetString()).ToArray()
            : new[] { "union", "intersection", "difference" };
        // analytically known volumes of the results (synthetic cases)
        var expected = info.TryGetValue("expected", out JsonElement ex)
            ? ex.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()) : null;
        // oracle of the thingi-pairs class: MeshRobustRepair.NotCleanReason of every result, self-intersections of any size
        bool notClean = info.TryGetValue("notclean", out JsonElement nc) && nc.GetBoolean();
        string first = "_" + ops[0] + ".";
        var methods = Directory.GetFiles(dir).Select(Path.GetFileName)
            .Where(f => f.Contains(first)).Select(f => f.Substring(0, f.IndexOf(first))).Distinct();
        foreach (string method in methods) {
            var r = new Dictionary<string, object> { ["file"] = info["file"].GetString(), ["method"] = method, ["tris_in"] = info["tris"].GetInt32() };
            var vol = new Dictionary<string, double>();
            bool all = true;
            foreach (string op in ops) {
                string path = new[] { ".off", ".obj" }.Select(e => Path.Combine(dir, method + "_" + op + e)).FirstOrDefault(File.Exists);
                List<Vector3d> v; List<Index3i> t;
                if (path == null || ReadSoup(path, out v, out t) == false) { all = false; continue; }
                // weld by exact position, so that all methods are measured the same way
                var ids = new Dictionary<Vector3d, int>();
                int[] map = v.Select(p => { if (p.x == 0) p.x = 0; if (p.y == 0) p.y = 0; if (p.z == 0) p.z = 0;
                    if (ids.TryGetValue(p, out int id) == false) { id = ids.Count; ids[p] = id; } return id; }).ToArray();
                DMesh3 soup = new DMesh3();
                foreach (var kv in ids.OrderBy(kv => kv.Value)) soup.AppendVertex(kv.Key);
                var edgeUse = new Dictionary<long, int>(new EdgeKeyComparer());
                int degenerate = 0;
                var tris = new List<Index3i>();
                foreach (Index3i f in t) {
                    Index3i g = new Index3i(map[f.a], map[f.b], map[f.c]);
                    if (g.a == g.b || g.b == g.c || g.a == g.c) { degenerate++; continue; }
                    tris.Add(g);
                    for (int k = 0; k < 3; ++k) {
                        int x = g[k], y = g[(k + 1) % 3];
                        long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                        edgeUse[key] = edgeUse.TryGetValue(key, out int c) ? c + (x < y ? 1 : 1 << 16) : (x < y ? 1 : 1 << 16);
                    }
                }
                bool wclosed = edgeUse.Values.All(c => (c & 0xFFFF) == (c >> 16));
                bool manifold = edgeUse.Values.All(c => (c & 0xFFFF) == 1 && (c >> 16) == 1);
                // the welded result split at its pinches (edges and vertices where the solid touches itself) into a manifold
                // mesh with duplicated vertices, the representation of ExactMeshBoolean and Manifold (BoolCmp.Pinches; before,
                // the triangles that did not fit at a pinch edge were appended on their own vertices: "not closed")
                var welded = ids.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
                BoolCmp.Pinches.Split split = BoolCmp.Pinches.Build(welded, tris);
                DMesh3 mesh = split.Mesh;
                double volume = 0;
                int zeroArea = 0;
                foreach (Index3i g in tris) {
                    Vector3d a = soup.GetVertex(g.a) - center, b = soup.GetVertex(g.b) - center, c = soup.GetVertex(g.c) - center;
                    volume += a.Dot(b.Cross(c)) / 6.0;
                    Vector3d pa = soup.GetVertex(g.a);
                    if ((soup.GetVertex(g.b) - pa).Cross(soup.GetVertex(g.c) - pa).LengthSquared == 0) zeroArea++;
                }
                vol[op] = volume;
                r[op + "_tris"] = tris.Count;
                r[op + "_wclosed"] = wclosed;
                r[op + "_manifold_closed"] = manifold;
                r[op + "_degenerate"] = degenerate;
                r[op + "_zero_area"] = zeroArea;
                r[op + "_pinch_edges"] = split.PinchEdges;
                r[op + "_pinch_vertices"] = split.PinchVertices;
                r[op + "_unpaired_edges"] = split.UnpairedEdges;
                // intersecting pairs beyond the pinches: triangles that touch only at a position where both have a vertex
                // (the duplicated vertices of a pinch) do not count (before: every pair touching at a pinch did)
                int selfint = mesh.TriangleCount <= 400000 || notClean ? BoolCmp.Pinches.SelfIntersectionsBeyondContacts(mesh) : -1;
                r[op + "_selfint"] = selfint;
                if (notClean) {
                    string reason = MeshRobustRepair.NotCleanReason(mesh, false);
                    if (reason == "" && selfint != 0) reason = "self-intersections";
                    // clean except where the solid touches itself: a class of its own (owner 2026-11-17; NotCleanReason
                    // itself is unchanged and calls the split mesh self-intersecting there)
                    if (reason == "" && (split.PinchVertices > 0 || split.PinchEdges > 0)) reason = "pinches";
                    r[op + "_clean_reason"] = reason;
                    // inside-out components (#1067): what the component removal sees of them, on the evaluated (pinch-split) mesh
                    // and on the result as the method wrote it (unwelded: the library's own mesh; absent if it is not a mesh)
                    if (reason.Contains("inside-out")) {
                        // (cost: the nesting test and the exact volume signs on the whole mesh, twice; not for results above 400k triangles;
                        // an exception of the diagnostic must not lose the row)
                        try {
                            if (mesh.TriangleCount > 400000)
                                r[op + "_inside_out"] = "skipped: more than 400000 triangles";
                            else {
                                r[op + "_inside_out"] = BoolCmp.InsideOutDiagnostics.Describe(mesh);
                                DMesh3 raw = BoolCmp.InsideOutDiagnostics.FromSoup(v, t);
                                r[op + "_inside_out_raw"] = raw != null ? BoolCmp.InsideOutDiagnostics.Describe(raw) : (object)"not a mesh as written";
                            }
                        } catch (Exception e) {
                            r[op + "_inside_out"] = "error: " + e.GetType().Name + " " + e.Message;
                        }
                    }
                }
                r[op + "_volume"] = volume;
            }
            r["all_results"] = all;
            if (all && expected != null) {
                // relative to the input volume, so that empty expected results (touching solids) are measured too
                double scale = Math.Max(Math.Max(Math.Abs(vA) + Math.Abs(vB), expected.Values.Max(Math.Abs)), 1e-300);
                r["expected_error"] = expected.Max(kv => Math.Abs(vol[kv.Key] - kv.Value)) / scale;
            }
            if (all && ops.Length == 3 && !double.IsNaN(vA) && vA + vB > 0) {
                r["ie_error"] = Math.Abs(vol["union"] + vol["intersection"] - vA - vB) / (vA + vB);
                r["diff_error"] = Math.Abs(vol["difference"] - (vA - vol["intersection"])) / Math.Max(vA, 1e-300);
            }
            Console.WriteLine(JsonSerializer.Serialize(r));
        }
        return 0;
    }
}
