// Standalone procedural integration probe, not a library certificate API.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using g3;

static class ProductionCandidateExport
{
    static long[] Bits(Vector3d v) => new[] { BitConverter.DoubleToInt64Bits(v.x), BitConverter.DoubleToInt64Bits(v.y), BitConverter.DoubleToInt64Bits(v.z) };
    static bool SameBits(Vector3d a, Vector3d b) => BitConverter.DoubleToInt64Bits(a.x) == BitConverter.DoubleToInt64Bits(b.x) &&
        BitConverter.DoubleToInt64Bits(a.y) == BitConverter.DoubleToInt64Bits(b.y) && BitConverter.DoubleToInt64Bits(a.z) == BitConverter.DoubleToInt64Bits(b.z);

    public static int Run(string[] args)
    {
        if (args.Length != 5 || !int.TryParse(args[1], out int n) || n < 3 || n > 50 ||
            (args[2] != "aligned" && args[2] != "rotated") ||
            (args[3] != "normal" && args[3] != "strict")) {
            Console.Error.WriteLine("usage: --certificate <n 3..50> <aligned|rotated> <normal|strict> <output.json>"); return 2;
        }
        if (File.Exists(args[4])) { Console.Error.WriteLine("output already exists; use a fresh path"); return 2; }
        var mesh = new DMesh3(MeshComponents.None);
        var rotation = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 31);
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
            var p = new Vector3d((double)x / (n - 1), (double)y / (n - 1), 0);
            int id = mesh.AppendVertex(args[2] == "rotated" ? rotation * p : p);
            if (id != x + y * n) throw new InvalidOperationException("unexpected vertex IDs");
        }
        for (int y = 0; y < n - 1; y++) for (int x = 0; x < n - 1; x++) {
            int a = x + n * y, b = a + 1, d = a + n, c = d + 1;
            if (mesh.AppendTriangle(a, b, c) < 0 || mesh.AppendTriangle(a, c, d) < 0)
                throw new InvalidOperationException("failed to build fixture");
        }
        var originalPositions = new Vector3d[n*n];
        for (int i = 0; i < originalPositions.Length; i++) originalPositions[i] = mesh.GetVertex(i);
        var originalFaces = new Dictionary<int, Index3i>();
        foreach (int tid in mesh.TriangleIndices()) originalFaces.Add(tid, mesh.GetTriangle(tid));
        var handles = new Dictionary<int, Vector3d>(); var fixedVertices = new bool[n*n];
        var targets = new Vector3d[n*n];
        for (int i = 0; i < targets.Length; i++) targets[i] = mesh.GetVertex(i);
        for (int y = 0; y < n; y++) {
            int left = n*y, right = left+n-1;
            targets[right] += new Vector3d(0.04, -0.03, 0.1);
            fixedVertices[left] = fixedVertices[right] = true;
            handles.Add(left, targets[left]); handles.Add(right, targets[right]);
        }
        var solver = new BiharmonicMeshDeformer(mesh);
        solver.Options.RequireEuclideanAccuracy = args[3] == "strict";
        var preparationTimer = Stopwatch.StartNew();
        var result = solver.Compute(handles); preparationTimer.Stop();
        bool reuseProfile = args[0] == "--certificate-reuse-profile";
        if (reuseProfile && (args[3] != "strict" || n*n <= 16 || n*n > 400 || !result.Converged || result.UsedIntervalFallback)) {
            Console.Error.WriteLine("reuse profile requires a successful sparse strict fixture with 17..400 vertices"); return 1;
        }
        if (reuseProfile) Console.WriteLine($"first_compute_including_jit_ms={preparationTimer.Elapsed.TotalMilliseconds:F3}");
        if (args[0] == "--certificate-profile" || reuseProfile) {
            // Cold: fresh deformer each call. Reuse: same prepared rest proof and
            // handle IDs, new targets; compare to a fresh result outside the timer.
            var times = new List<double>(); var allocations = new List<long>();
            var referenceTimes = new List<double>(); var referenceAllocations = new List<long>();
            for (int run = 0; run < 5; run++) {
                if (reuseProfile) for (int y = 0; y < n; y++) {
                    int right = n*y+n-1;
                    targets[right] = originalPositions[right] + (1 + .01*(run+1))*new Vector3d(.04,-.03,.1);
                    handles[right] = targets[right];
                }
                long allocated = GC.GetTotalAllocatedBytes(true); var timer = Stopwatch.StartNew();
                var instance = reuseProfile ? solver : new BiharmonicMeshDeformer(mesh);
                instance.Options.RequireEuclideanAccuracy = args[3] == "strict";
                var next = instance.Compute(handles); timer.Stop();
                if (reuseProfile && !next.Converged) throw new InvalidOperationException("reuse compute did not succeed");
                allocations.Add(GC.GetTotalAllocatedBytes(true) - allocated); times.Add(timer.Elapsed.TotalMilliseconds);
                var reference = result;
                if (reuseProfile) {
                    long referenceAllocated = GC.GetTotalAllocatedBytes(true); var referenceTimer = Stopwatch.StartNew();
                    var fresh = new BiharmonicMeshDeformer(mesh); fresh.Options.RequireEuclideanAccuracy = args[3] == "strict";
                    reference = fresh.Compute(handles); referenceTimer.Stop();
                    referenceTimes.Add(referenceTimer.Elapsed.TotalMilliseconds);
                    referenceAllocations.Add(GC.GetTotalAllocatedBytes(true) - referenceAllocated);
                }
                if (next.Status != reference.Status || next.Accuracy != reference.Accuracy || next.Iterations != reference.Iterations ||
                    next.UsedIntervalFallback != reference.UsedIntervalFallback ||
                    BitConverter.DoubleToInt64Bits(next.Residual) != BitConverter.DoubleToInt64Bits(reference.Residual) ||
                    BitConverter.DoubleToInt64Bits(next.PositionErrorBound) != BitConverter.DoubleToInt64Bits(reference.PositionErrorBound) ||
                    (next.Mesh == null) != (reference.Mesh == null)) throw new InvalidOperationException("non-deterministic result");
                if (next.Mesh != null) {
                    if (next.Mesh.VertexCount != reference.Mesh.VertexCount || next.Mesh.TriangleCount != reference.Mesh.TriangleCount)
                        throw new InvalidOperationException("result counts changed between runs");
                    for (int i = 0; i < originalPositions.Length; i++)
                        if (!SameBits(next.Mesh.GetVertex(i), reference.Mesh.GetVertex(i))) throw new InvalidOperationException("positions changed between runs");
                    foreach (int id in reference.Mesh.TriangleIndices()) {
                        if (!next.Mesh.IsTriangle(id)) throw new InvalidOperationException("triangle IDs changed between runs");
                        var a = next.Mesh.GetTriangle(id); var b = reference.Mesh.GetTriangle(id);
                        if (a.a != b.a || a.b != b.b || a.c != b.c) throw new InvalidOperationException("topology changed between runs");
                    }
                }
                result = next;
            }
            times.Sort(); allocations.Sort();
            Console.WriteLine($"{(reuseProfile ? "reuse" : "cold")}_compute_runs=5 median_ms={times[2]:F3} min_ms={times[0]:F3} max_ms={times[4]:F3} median_allocated_bytes={allocations[2]} status={result.Status} peak_working_set_bytes={Process.GetCurrentProcess().PeakWorkingSet64}");
            if (reuseProfile) {
                referenceTimes.Sort(); referenceAllocations.Sort();
                Console.WriteLine($"paired_fresh_reference_runs=5 median_ms={referenceTimes[2]:F3} min_ms={referenceTimes[0]:F3} max_ms={referenceTimes[4]:F3} median_allocated_bytes={referenceAllocations[2]}");
            }
        }
        Console.WriteLine($"vertices={mesh.VertexCount} mode={args[3]} orientation={args[2]} status={result.Status} accuracy={result.Accuracy} fallback={result.UsedIntervalFallback}");
        if (mesh.VertexCount != originalPositions.Length || mesh.TriangleCount != originalFaces.Count)
            throw new InvalidOperationException("input counts changed");
        for (int i = 0; i < originalPositions.Length; i++)
            if (!mesh.IsVertex(i) || !SameBits(mesh.GetVertex(i), originalPositions[i]))
                throw new InvalidOperationException("input changed");
        foreach (var entry in originalFaces) {
            if (!mesh.IsTriangle(entry.Key)) throw new InvalidOperationException("input triangle IDs changed");
            var a = entry.Value; var b = mesh.GetTriangle(entry.Key);
            if (a.a != b.a || a.b != b.b || a.c != b.c) throw new InvalidOperationException("input topology changed");
        }
        if (result.Status != BiharmonicMeshDeformer.Status.Success || result.Mesh == null) {
            if (result.Status == BiharmonicMeshDeformer.Status.NumericalRange)
                ProductionAssemblyDiagnostic.Explain(mesh);
            return 1;
        }
        if (mesh.VertexCount != originalPositions.Length || result.Mesh.VertexCount != originalPositions.Length ||
            mesh.TriangleCount != originalFaces.Count || result.Mesh.TriangleCount != originalFaces.Count)
            throw new InvalidOperationException("vertex or triangle count changed");
        for (int i = 0; i < originalPositions.Length; i++)
            if (!mesh.IsVertex(i) || !result.Mesh.IsVertex(i) || !SameBits(mesh.GetVertex(i), originalPositions[i]))
                throw new InvalidOperationException("input changed or vertex IDs changed");
        foreach (var entry in originalFaces) {
            if (!mesh.IsTriangle(entry.Key) || !result.Mesh.IsTriangle(entry.Key)) throw new InvalidOperationException("triangle IDs changed");
            var a = entry.Value; var b = mesh.GetTriangle(entry.Key); var c = result.Mesh.GetTriangle(entry.Key);
            if (a.a != b.a || a.b != b.b || a.c != b.c || a.a != c.a || a.b != c.b || a.c != c.c)
                throw new InvalidOperationException("input or output topology changed");
        }
        int count = mesh.VertexCount; var vertices = new long[count][]; var candidate = new long[count][]; var targetBits = new long[count][];
        Vector3d origin = originalPositions[0]; double scale = 0;
        for (int i = 0; i < count; i++) {
            var original = originalPositions[i]; var d = original - origin;
            scale = Math.Max(scale, Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))));
            vertices[i] = Bits(original); candidate[i] = Bits(result.Mesh.GetVertex(i)); targetBits[i] = Bits(targets[i]);
            if (fixedVertices[i] && !SameBits(result.Mesh.GetVertex(i), targets[i])) throw new InvalidOperationException("handle mismatch");
        }
        var faces = new List<int[]>(); foreach (var entry in originalFaces) {
            var face = entry.Value; faces.Add(new[] { face.a, face.b, face.c });
        }
        int freeCount = count - handles.Count; var order = new int[freeCount];
        for (int i = 0; i < freeCount; i++) order[i] = i;
        // Untrusted trial shift: the receiving verifier must prove it is valid.
        double shift = 0.5;
        var data = new Dictionary<string, object> {
            ["size"] = freeCount, ["vertices_bits"] = vertices, ["faces"] = faces,
            ["fixed"] = fixedVertices, ["order"] = order,
            ["scale_bits"] = BitConverter.DoubleToInt64Bits(scale), ["shift_bits"] = BitConverter.DoubleToInt64Bits(shift),
            ["candidate_bits"] = candidate, ["target_bits"] = targetBits
        };
        // CreateNew prevents stale or previously existing fixture data from being overwritten.
        using (var stream = new FileStream(args[4], FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(stream, data);
        return 0;
    }
}
