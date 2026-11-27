using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using g3;
using gs;

// End-to-end comparison of the public ExactMeshBoolean paths, with setup and file I/O outside the timer.
static class EndpointBudgetAudit
{
    sealed record Sample(ExactMeshBoolean Boolean, double Milliseconds, long AllocatedBytes);
    static readonly JsonSerializerOptions EvidenceJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Run(string label, ExactMeshBoolean.Operations operation, string budgetText, int repetitions, string[] sources)
    {
        if (repetitions < 1)
            throw new ArgumentOutOfRangeException(nameof(repetitions));
        var (inputs, allowOpen, files, hashes) = LoadInputs(sources);
        List<DMesh3> warmup = Enumerable.Range(0, 4).Select(RotatedCube).ToList();
        Measure(warmup, ExactMeshBoolean.Operations.Union, 2000, false);
        Measure(warmup, ExactMeshBoolean.Operations.Union, 2000, true);

        long budget;
        if (budgetText == "tight" || budgetText == "near" || budgetText == "exact") {
            var probe = Measure(inputs, operation, 1, false, allowOpen).Boolean;
            if (!probe.Counts.TryGetValue("predicted_pieces", out long prediction) ||
                (probe.Status != ExactMeshBoolean.BooleanStatus.BudgetExceeded && probe.Status != ExactMeshBoolean.BooleanStatus.Success))
                throw new InvalidOperationException($"Could not obtain a reference prediction for {label}: {probe.Error}");
            budget = Math.Max(1, budgetText switch {
                "exact" => prediction,
                "near" => prediction - 1,
                _ => prediction / 2
            });
        } else {
            budget = long.Parse(budgetText);
        }

        for (int repetition = 0; repetition < repetitions; ++repetition) {
            bool earlyFirst = (repetition & 1) != 0;
            Sample first = Measure(inputs, operation, budget, earlyFirst, allowOpen);
            Sample second = Measure(inputs, operation, budget, !earlyFirst, allowOpen);
            Sample baseline = earlyFirst ? second : first, filtered = earlyFirst ? first : second;
            var a = baseline.Boolean;
            var b = filtered.Boolean;
            if (a.Status != b.Status || !a.Counts.TryGetValue("predicted_pieces", out long prediction))
                throw new InvalidOperationException($"The two runs disagree for {label}: {a.Status}, {b.Status}.");
            bool early = b.Counts.TryGetValue("piece_lower_bound", out long bound);
            if (early ? bound <= budget || bound > prediction || b.Result != null || b.Counts.ContainsKey("predicted_pieces") :
                !b.Counts.TryGetValue("predicted_pieces", out long filteredPrediction) || filteredPrediction != prediction)
                throw new InvalidOperationException($"The budget diagnostic disagrees with the exact prediction for {label}.");

            bool? sameMesh = null;
            string defect = null;
            if (a.Status == ExactMeshBoolean.BooleanStatus.Success) {
                sameMesh = a.Result != null && b.Result != null && SameMesh(a.Result, b.Result);
                if (sameMesh != true)
                    throw new InvalidOperationException($"The accepted meshes differ for {label}.");
                defect = MeshRobustRepair.NotCleanReason(b.Result);
            } else if (a.Status != ExactMeshBoolean.BooleanStatus.BudgetExceeded) {
                throw new InvalidOperationException($"Unexpected Boolean status for {label}: {a.Status} {a.Error}");
            }
            Console.WriteLine(JsonSerializer.Serialize(new {
                label, operation = operation.ToString(), budgetText, budget, repetition, earlyFirst,
                sourceFiles = files, hashes,
                inputTriangles = inputs.Select(m => m.TriangleCount).ToArray(), status = a.Status.ToString(),
                prediction, lowerBound = early ? bound : (long?)null, triangles = b.Result?.TriangleCount, sameMesh, defect,
                baselineMs = baseline.Milliseconds, filteredMs = filtered.Milliseconds,
                baselineBytes = baseline.AllocatedBytes, filteredBytes = filtered.AllocatedBytes,
                baselinePhases = a.PhaseMilliseconds, filteredPhases = b.PhaseMilliseconds
            }));
        }
        return 0;
    }

    public static int Evidence(string label, ExactMeshBoolean.Operations operation, string[] sources)
    {
        var (inputs, allowOpen, files, hashes) = LoadInputs(sources);
        var (evidence, _, _) = InspectSnapshot(label, inputs, operation, allowOpen);
        Console.WriteLine(JsonSerializer.Serialize(new {
            label, operation = operation.ToString(), sourceFiles = files, hashes, evidence
        }, EvidenceJson));
        return 0;
    }

    public static int Weighted(string label, ExactMeshBoolean.Operations operation, long budget, string[] sources)
    {
        if (budget <= 0)
            throw new ArgumentOutOfRangeException(nameof(budget));
        var (inputs, allowOpen, files, hashes) = LoadInputs(sources);
        List<DMesh3> warmup = Enumerable.Range(0, 4).Select(RotatedCube).ToList();
        InspectSnapshot("warmup", warmup, ExactMeshBoolean.Operations.Union, false);
        Measure(warmup, ExactMeshBoolean.Operations.Union, 10, false);
        var (evidence, snapshotMs, inspectionMs) = InspectSnapshot(label, inputs, operation, allowOpen);
        if (budget < evidence.EndpointBound)
            throw new InvalidOperationException($"The cap must be at or above the unweighted snapshot bound for {label}.");
        var current = Measure(inputs, operation, budget, true, allowOpen);
        var run = current.Boolean;
        if (!run.Counts.TryGetValue("predicted_pieces", out long prediction) || evidence.WeightedBound > prediction ||
            run.Status != (prediction > budget ? ExactMeshBoolean.BooleanStatus.BudgetExceeded : ExactMeshBoolean.BooleanStatus.Success))
            throw new InvalidOperationException($"The full prediction does not validate the weighted bound for {label}: {run.Status} {run.Error}");
        Console.WriteLine(JsonSerializer.Serialize(new {
            label, operation = operation.ToString(), budget, sourceFiles = files, hashes, evidence,
            currentStatus = run.Status.ToString(), prediction, weightedCertifies = evidence.WeightedBound > budget,
            currentMs = current.Milliseconds, currentPhases = run.PhaseMilliseconds, snapshotMs, inspectionMs
        }, EvidenceJson));
        return 0;
    }

    static (EndpointEvidence.Snapshot Evidence, double SnapshotMs, double InspectionMs) InspectSnapshot(
        string label, List<DMesh3> inputs, ExactMeshBoolean.Operations operation, bool allowOpen)
    {
        var sample = Measure(inputs, operation, 1, true, allowOpen);
        var run = sample.Boolean;
        if (run.Status != ExactMeshBoolean.BooleanStatus.BudgetExceeded ||
            !run.Counts.ContainsKey("piece_lower_bound") || run.PhaseMilliseconds.ContainsKey("merge.crossings"))
            throw new InvalidOperationException($"Expected the first-merge snapshot for {label}: {run.Status} {run.Error}");
        var timer = Stopwatch.StartNew();
        var evidence = EndpointEvidence.Read(run);
        return (evidence, sample.Milliseconds, timer.Elapsed.TotalMilliseconds);
    }

    static (List<DMesh3> Inputs, bool AllowOpen, string[] Files, string[] Hashes) LoadInputs(string[] sources)
    {
        bool allowOpen = sources.Contains("--allow-open");
        sources = sources.Where(s => s != "--allow-open").ToArray();
        bool procedural = sources.Length == 1 && int.TryParse(sources[0], out _);
        List<DMesh3> inputs = procedural
            ? Enumerable.Range(0, int.Parse(sources[0])).Select(RotatedCube).ToList()
            : sources.Select(Read).ToList();
        if (!procedural && inputs.Count == 1)
            inputs.Add(Cutter(inputs[0].CachedBounds));
        if (inputs.Count < 2)
            throw new ArgumentException("At least two operands are required.");
        return (inputs, allowOpen, sources.Select(Path.GetFileName).ToArray(),
            procedural ? Array.Empty<string>() : sources.Select(Hash).ToArray());
    }

    static Sample Measure(List<DMesh3> inputs, ExactMeshBoolean.Operations operation, long budget, bool early, bool allowOpen = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var boolean = new ExactMeshBoolean(inputs, operation) {
            MaxPieces = budget, EnableEarlyPieceBudgetCheck = early, Cancellation = timeout.Token, AllowOpenMeshes = allowOpen
        };
        long bytes = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        boolean.Compute();
        return new Sample(boolean, timer.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(true) - bytes);
    }

    static DMesh3 Read(string path)
    {
        var builder = new DMesh3Builder();
        var reader = new StandardMeshReader { MeshBuilder = builder };
        IOReadResult result = reader.Read(path, ReadOptions.Defaults);
        if (result.code != IOCode.Ok || builder.Meshes.Count != 1)
            throw new InvalidOperationException($"Expected one mesh in {path}: {result.message}");
        return builder.Meshes.Single();
    }

    static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static DMesh3 Cutter(AxisAlignedBox3d box)
    {
        Vector3d size = box.Max - box.Min;
        Vector3d lo = box.Min - 0.1 * size, hi = box.Max + 0.1 * size;
        hi.x = box.Min.x + 0.47 * size.x;
        return new TrivialBox3Generator { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
    }

    static DMesh3 RotatedCube(int index)
    {
        // Same 70-operand ThingiCSG schedule as the earlier arrangement probe.
        double angle = index * Math.PI / 140, c = Math.Cos(angle), s = Math.Sin(angle);
        var mesh = new DMesh3();
        foreach (double z in new[] { -0.5, 0.5 })
            foreach (var (x, y) in new[] { (-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5) })
                mesh.AppendVertex(new Vector3d(c * x - s * y, s * x + c * y, z));
        foreach (var (a, b, d, e) in new[] {
            (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
        }) {
            mesh.AppendTriangle(a, b, d);
            mesh.AppendTriangle(a, d, e);
        }
        return mesh;
    }

    static bool SameMesh(DMesh3 a, DMesh3 b) =>
        a.VertexIndices().SequenceEqual(b.VertexIndices()) && a.TriangleIndices().SequenceEqual(b.TriangleIndices()) &&
        a.VertexIndices().All(v => a.GetVertex(v) == b.GetVertex(v)) &&
        a.TriangleIndices().All(t => a.GetTriangle(t) == b.GetTriangle(t));
}
