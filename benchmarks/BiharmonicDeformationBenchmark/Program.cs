using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

// BiharmonicDeformationBenchmark <nx> <ny> [warm runs, default 5]
// A procedural nx x ny grid on [0,1]^2; the left column is fixed, the right column is moved along a sine profile.
// Prints the cold first Compute of the process and the median of the warm runs that follow.
class Program
{
    static int Main(string[] args)
    {
        int warm = 5;
        if (args.Length < 2 || args.Length > 3 || !int.TryParse(args[0], out int nx) || !int.TryParse(args[1], out int ny) || nx < 2 || ny < 2 ||
            (args.Length == 3 && (!int.TryParse(args[2], out warm) || warm < 1))) {
            Console.Error.WriteLine("usage: BiharmonicDeformationBenchmark <nx> <ny> [warm runs]"); return 2;
        }
        var mesh = new DMesh3(MeshComponents.None);
        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x)
                mesh.AppendVertex(new Vector3d((double)x / (nx - 1), (double)y / (ny - 1), 0));
        for (int y = 0; y < ny - 1; ++y) for (int x = 0; x < nx - 1; ++x) {
            int a = x + nx * y, b = a + 1, d = a + nx, c = d + 1;
            mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d);
        }
        var handles = new Dictionary<int, Vector3d>();
        for (int y = 0; y < ny; ++y) {
            int left = nx * y, right = left + nx - 1;
            handles.Add(left, mesh.GetVertex(left));
            handles.Add(right, mesh.GetVertex(right) + new Vector3d(0.1, 0, 0.05 * Math.Sin(2 * Math.PI * y / (ny - 1))));
        }
        var solver = new BiharmonicMeshDeformer(mesh);
        var times = new List<double>(); BiharmonicMeshDeformer.ResultInfo result = null; long allocated = 0;
        for (int run = 0; run <= warm; ++run) {
            long before = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            result = solver.Compute(handles);
            watch.Stop();
            if (run == 0) allocated = GC.GetTotalAllocatedBytes(true) - before;
            times.Add(watch.Elapsed.TotalMilliseconds);
            if (!result.Converged) break;
        }
        var warmTimes = times.Skip(1).OrderBy(t => t).ToList();
        string median = warmTimes.Count == 0 ? "n/a" : warmTimes[warmTimes.Count / 2].ToString("F0");
        Console.WriteLine($"vertices={mesh.VertexCount} triangles={mesh.TriangleCount} handles={handles.Count} status={result.Status} residual={result.Residual:E2} " +
            $"cold_ms={times[0]:F0} warm_median_ms={median} warm_runs={warmTimes.Count} allocated_mb={allocated / 1048576.0:F0} peak_working_set_mb={Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F0}");
        if (!result.Converged || result.Mesh == null || result.Mesh.VertexCount != mesh.VertexCount) return 1;
        foreach (var h in handles) if (result.Mesh.GetVertex(h.Key) != h.Value) return 1;
        foreach (int id in result.Mesh.VertexIndices()) if (!result.Mesh.GetVertex(id).IsFinite) return 1;
        return 0;
    }
}
