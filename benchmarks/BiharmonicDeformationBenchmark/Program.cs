using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--certificate" || args[0] == "--certificate-profile")) return ProductionCandidateExport.Run(args);
        if (args.Length != 2 || !int.TryParse(args[0], out int nx) || !int.TryParse(args[1], out int ny) || nx < 2 || ny < 2) {
            Console.Error.WriteLine("usage: BiharmonicDeformationBenchmark <nx> <ny>"); return 2;
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
        Console.WriteLine($"vertices={mesh.VertexCount} triangles={mesh.TriangleCount} handles={handles.Count} status={result.Status} iterations={result.Iterations} residual={result.Residual:R} elapsed_ms={watch.Elapsed.TotalMilliseconds:F3} allocated_bytes={allocated} peak_working_set_bytes={Process.GetCurrentProcess().PeakWorkingSet64} mesh_hash={hash:x16}");
        if (result.Mesh == null || result.Mesh.VertexCount != mesh.VertexCount) return 1;
        foreach (var h in handles) if (result.Mesh.GetVertex(h.Key) != h.Value) return 1;
        foreach (int id in result.Mesh.VertexIndices()) if (!result.Mesh.GetVertex(id).IsFinite) return 1;
        return 0;
    }
}
