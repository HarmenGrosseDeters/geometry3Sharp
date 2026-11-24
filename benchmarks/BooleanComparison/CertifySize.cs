// Research prototype for docs/research/CERTIFYING_BOOLEANS_2026-11.md (not library code): the size of a Boolean
// certificate in two formats, and the time of the existing test-only checker (C0-C5) against the boolean, on the
// procedural cases of `certify cost` and on the closed families of the Boolean fuzzer.
//
//   dotnet run -c Release --project benchmarks/BooleanComparison -- certify size [reps] [seeds]
//
// Format R (rational): per piece three point ids and its source triangle; per corner point that is not an input vertex
// its exact homogeneous coordinates (X, Y, Z, W on the arrangement's grid, as the stages 1-2 checker reads them).
// Format S (symbolic): the same pieces, but per corner point only its construction from input ids (the arrangement's
// point key: an input edge crossing an input plane, two coplanar input edges, three input planes), as the indirect
// predicates of Attene 2020 evaluate them on the input doubles. Both carry the same labels: per classified piece the
// front winding numbers (one small integer per mesh) and the keep / flip bits.
// Sizes are counted, not serialized: ids 4 bytes, a BigInteger its bit length rounded up to bytes plus a 1-byte length,
// a winding number 1 byte (|w| < 128 in every case here; checked), keep / flip 2 bits.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using g3;
using gs;
using geometry3Sharp.Tests;
using Ops = g3.ExactMeshBoolean.Operations;

static partial class CertifySize
{
    public static int Run(List<(string name, List<DMesh3> inputs, Ops op)> cases, int reps)
    {
        Console.WriteLine($"# {Environment.ProcessorCount} logical cores, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; times in ms (boolean: median of {reps} warm runs; checker parts: one run after one warm-up)");
        Console.WriteLine("case | input tris | input KB | result tris | result KB | pieces | classified | corner points (input / edge-plane / segments / planes) | max bits per coordinate | R: KB | S: KB | labels KB | boolean | boolean+record | C0 | C1 | C2 | C3+C4 | C5 | checker sum (x boolean) | anchors | NotCleanReason(R) | verdict");
        FieldInfo keysField = typeof(ExactMeshArrangement).GetField("keys", BindingFlags.NonPublic | BindingFlags.Instance);
        int failed = 0;
        foreach (var (name, inputs, op) in cases) {
            Func<bool, ExactMeshBoolean> run = record => {
                var b = new ExactMeshBoolean(inputs, op) { AllowOpenMeshes = true };
                if (record) { b.RecordCertificate = true; b.ComputeExactVertices = true; if (op == Ops.Intersection) b.CullOutsideA = false; }
                if (b.Compute() == false) throw new InvalidOperationException(name + ": " + b.Error);
                return b;
            };
            double tBool = Median(reps, () => run(false));
            double tRec = Median(reps, () => run(true));
            ExactMeshBoolean bb = run(true);
            ExactMeshArrangement arr = bb.Arrangement;
            var keys = (ChunkedList<ExactMeshArrangement.PointKey>)keysField.GetValue(arr);

            int n = arr.Output.Count;
            var used = new HashSet<int>();
            foreach (var t in arr.Output) { used.Add(t.A); used.Add(t.B); used.Add(t.C); }
            var kinds = new int[5];
            long rationalBytes = 0, symbolicBytes = 0, maxBits = 0;
            foreach (int v in used) {
                var k = keys[v].Kind;
                kinds[(int)k]++;
                if (k == ExactMeshArrangement.PointKind.Input) continue;            // an input vertex: its id is enough in both formats
                ExactPoint3 x = arr.Exact(v);
                foreach (BigInteger c in new[] { x.X, x.Y, x.Z, x.W }) {
                    long bits = ExactPoint3.BitLength(c);
                    maxBits = Math.Max(maxBits, bits);
                    rationalBytes += (bits + 7) / 8 + 1;
                }
                // the construction: edge (two vertex ids) + plane (one triangle id); two edges + an axis; three planes
                symbolicBytes += 1 + (k == ExactMeshArrangement.PointKind.EdgePlane ? 12 : k == ExactMeshArrangement.PointKind.Segments ? 17 : 12);
            }
            long pieceBytes = 16L * n;                                                // three point ids and the source
            int classified = bb.CertificateFront == null ? 0 : bb.CertificateFront.Count(f => f != null);
            int meshes = arr.Meshes.Count;
            if (bb.CertificateFront != null && bb.CertificateFront.Any(f => f != null && f.Any(w => Math.Abs(w) > 127)))
                Console.WriteLine("# a winding number beyond one byte in " + name);
            long labelBytes = (long)classified * meshes + (2L * n + 7) / 8;
            long inBytes = inputs.Sum(m => 24L * m.VertexCount + 12L * m.TriangleCount);
            long outBytes = 24L * bb.Result.VertexCount + 12L * bb.Result.TriangleCount;

            BooleanCertificate cert = BooleanCertificate.From(bb);
            string verdict; double t0 = 0, t1 = 0, t2 = 0, t3 = 0, t5 = 0;
            if (cert.NotApplicableReason != null) verdict = "n/a: " + cert.NotApplicableReason;
            else {
                cert.Check();                                                         // warm-up (and the stacks of C3)
                cert = BooleanCertificate.From(bb);
                var sw = Stopwatch.StartNew();
                string r0 = cert.CheckInputs(); t0 = Lap(sw);
                string r1 = cert.CheckPieces(); t1 = Lap(sw);
                string r2 = cert.CheckEmbedding(); t2 = Lap(sw);
                string r3 = cert.CheckClassification(); t3 = Lap(sw);
                string r5 = cert.CheckResult(); t5 = Lap(sw);
                verdict = r0 + r1 + r2 + r3 + r5 == "" ? (cert.Uncovered == 0 ? (cert.RemovedParts.Count == 0 ? "certified" : "removed parts") : cert.Uncovered + " uncovered")
                    : $"FAIL '{r0}' '{r1}' '{r2}' '{r3}' '{r5}'";
                if (verdict.StartsWith("FAIL")) failed++;
            }
            string notClean = "";
            var swc = Stopwatch.StartNew();
            notClean = MeshRobustRepair.NotCleanReason(bb.Result);
            double tClean = swc.Elapsed.TotalMilliseconds;
            double sum = t0 + t1 + t2 + t3 + t5;
            Console.WriteLine($"{name} | {inputs.Sum(m => m.TriangleCount)} | {K(inBytes)} | {bb.Result.TriangleCount} | {K(outBytes)} | {n} | {classified} | {kinds[0]} / {kinds[2]} / {kinds[3]} / {kinds[4]} | {maxBits} | "
                + $"{K(pieceBytes + rationalBytes)} | {K(pieceBytes + symbolicBytes)} | {K(labelBytes)} | {F(tBool)} | {F(tRec)} | {F(t0)} | {F(t1)} | {F(t2)} | {F(t3)} | {F(t5)} | {F(sum)} ({(tBool > 0 ? sum / tBool : 0).ToString("0.0", CultureInfo.InvariantCulture)}) | {cert.Anchors} | {F(tClean)}{(notClean == "" ? "" : " (" + notClean + ")")} | {verdict}");
        }
        return failed == 0 ? 0 : 1;
    }

    /// <summary> the closed families of the Boolean fuzzer (tests/.../BooleanFuzzCases.cs), seeds 0..seeds-1, all three operations </summary>
    public static List<(string, List<DMesh3>, Ops)> FuzzCases(int seeds)
    {
        var L = new List<(string, List<DMesh3>, Ops)>();
        foreach (string f in BooleanFuzzCases.ClosedFamilies)
            for (int s = 0; s < seeds; ++s) {
                FuzzCase c = BooleanFuzzCases.Make(f, s);
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    L.Add(($"fuzz {f} #{s} {op.ToString()[0]}", new List<DMesh3> { c.A, c.B }, op));
            }
        return L;
    }

    static double Lap(Stopwatch sw) { double t = sw.Elapsed.TotalMilliseconds; sw.Restart(); return t; }

    static double Median(int reps, Action a)
    {
        a();
        var t = new List<double>();
        for (int i = 0; i < reps; ++i) { var sw = Stopwatch.StartNew(); a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        t.Sort();
        return t[t.Count / 2];
    }

    static string K(long bytes) => (bytes / 1024.0).ToString(bytes < 10240 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
    static string F(double ms) => ms.ToString(ms < 10 ? "0.00" : ms < 100 ? "0.0" : "0", CultureInfo.InvariantCulture);
}
