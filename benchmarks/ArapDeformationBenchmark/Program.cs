using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

class Program
{
    static int Main(string[] args)
    {
        int depth = new ArapMeshDeformer.Settings().AccelerationDepth;
        if ((args.Length < 3 || args.Length > 6 || (args.Length >= 5 && args[4] != "drag")) || !int.TryParse(args[0], out int nx) || !int.TryParse(args[1], out int ny) ||
            !int.TryParse(args[2], out int iterations) || nx < 2 || ny < 2 || iterations < 1 ||
            (args.Length >= 4 && !int.TryParse(args[3], out depth))) {
            Console.Error.WriteLine("usage: ArapDeformationBenchmark <nx> <ny> <iterations> [accelerationDepth; 0 = plain local/global] [drag [steps = 30]]"); return 2;
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
        if (args.Length >= 5) return Drag(mesh, nx, ny, iterations, depth, args.Length == 6 && int.TryParse(args[5], out int dragSteps) && dragSteps > 0 ? dragSteps : 30);
        var solver = new ArapMeshDeformer(mesh); solver.Options.MaxIterations = iterations; solver.Options.AccelerationDepth = depth;
        long allocated = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var result = solver.Compute(handles);
        watch.Stop();
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        ulong hash = 14695981039346656037UL;
        void AddHash(long bits) { unchecked { hash ^= (ulong)bits; hash *= 1099511628211UL; } }
        if (result.Mesh != null) {
            foreach (int id in result.Mesh.VertexIndices()) {
                var v = result.Mesh.GetVertex(id); AddHash(id);
                AddHash(BitConverter.DoubleToInt64Bits(v.x)); AddHash(BitConverter.DoubleToInt64Bits(v.y)); AddHash(BitConverter.DoubleToInt64Bits(v.z));
            }
            foreach (int id in result.Mesh.TriangleIndices()) {
                var t = result.Mesh.GetTriangle(id); AddHash(id); AddHash(t.a); AddHash(t.b); AddHash(t.c);
            }
        }
        Console.WriteLine($"vertices={mesh.VertexCount} triangles={mesh.TriangleCount} handles={handles.Count} budget={iterations} depth={depth} status={result.Status} iterations={result.Iterations} residual={result.Residual:R} energy={result.Energy:R} elapsed_ms={watch.Elapsed.TotalMilliseconds:F3} allocated_bytes={allocated} peak_working_set_bytes={Process.GetCurrentProcess().PeakWorkingSet64} mesh_hash={hash:x16}");
        if (result.Mesh == null || result.Mesh.VertexCount != mesh.VertexCount) return 1;
        foreach (var h in handles) if (result.Mesh.GetVertex(h.Key) != h.Value) return 1;
        foreach (int id in result.Mesh.VertexIndices()) if (!result.Mesh.GetVertex(id).IsFinite) return 1;
        return 0;
    }

    // A drag: the right boundary moves to its final place in `steps` equal steps; every step is a solve with the same handle
    // ids. Three ways to do it: a fresh Compute per step, one Prepare and a cold Solve per step, and one Prepare and a Solve
    // per step started from the previous result (InitialPositions).
    static int Drag(DMesh3 mesh, int nx, int ny, int iterations, int depth, int steps)
    {
        Dictionary<int, Vector3d> Handles(double t)
        {
            var h = new Dictionary<int, Vector3d>();
            for (int y = 0; y < ny; ++y) {
                int left = nx * y, right = left + nx - 1;
                h.Add(left, mesh.GetVertex(left));
                h.Add(right, mesh.GetVertex(right) + t * new Vector3d(0.1, 0, 0.05 * Math.Sin(2 * Math.PI * y / (ny - 1))));
            }
            return h;
        }
        ArapMeshDeformer Make() { var d = new ArapMeshDeformer(mesh); d.Options.MaxIterations = iterations; d.Options.AccelerationDepth = depth; return d; }
        int bad = 0;
        string Line(string name, Stopwatch watch, long totalIterations, ArapMeshDeformer.ResultInfo last, int notSuccess, double prepareMs)
        {
            if (last.Mesh == null) ++bad;
            return $"vertices={mesh.VertexCount} steps={steps} budget={iterations} depth={depth} variant={name} total_ms={watch.Elapsed.TotalMilliseconds:F1} prepare_ms={prepareMs:F1} total_iterations={totalIterations} not_success={notSuccess} last_energy={last.Energy:R}";
        }
        var results = new List<string>();
        {
            var w = Stopwatch.StartNew(); long it = 0; int ns = 0; ArapMeshDeformer.ResultInfo last = null;
            for (int k = 1; k <= steps; ++k) { last = Make().Compute(Handles((double)k / steps)); it += last.Iterations; if (last.Status != ArapMeshDeformer.Status.Success) ++ns; }
            w.Stop(); results.Add(Line("fresh-compute", w, it, last, ns, 0));
        }
        foreach (bool warm in new[] { false, true }) {
            var w = Stopwatch.StartNew(); long it = 0; int ns = 0; ArapMeshDeformer.ResultInfo last = null;
            var d = Make(); var p0 = Stopwatch.StartNew(); var prep = d.Prepare(Handles(1).Keys); p0.Stop();
            Dictionary<int, Vector3d> previous = null;
            for (int k = 1; k <= steps; ++k) {
                last = d.Solve(prep, Handles((double)k / steps), warm ? previous : null); it += last.Iterations; if (last.Status != ArapMeshDeformer.Status.Success) ++ns;
                if (warm && last.Mesh != null) { previous = new Dictionary<int, Vector3d>(); foreach (int v in last.Mesh.VertexIndices()) previous[v] = last.Mesh.GetVertex(v); }
            }
            w.Stop(); results.Add(Line(warm ? "prepare-solve-warm" : "prepare-solve", w, it, last, ns, p0.Elapsed.TotalMilliseconds));
        }
        foreach (var r in results) Console.WriteLine(r);
        return bad == 0 ? 0 : 1;
    }
}
