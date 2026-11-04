using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using g3;

internal static class Program
{
    sealed class SkewedSphere : ImplicitFunction3d
    {
        public double Value(ref Vector3d p)
        {
            int steps = p.z > 0.4 && p.z < 0.8 ? 64 : 1;
            double perturbation = 0;
            for (int i = 1; i <= steps; ++i)
                perturbation += Math.Sin(i * p.x) * Math.Cos(i * p.y);
            return p.Length - 1 + 1e-6 * perturbation;
        }
    }

    static string PointKey(Vector3d p) => string.Join(":", new[] { p.x, p.y, p.z }
        .Select(x => BitConverter.DoubleToInt64Bits(x).ToString("X16", CultureInfo.InvariantCulture)));

    static string GeometryDigest(DMesh3 mesh)
    {
        var rows = mesh.TriangleIndices().Select(t => {
            Index3i tri = mesh.GetTriangle(t);
            string[] p = { PointKey(mesh.GetVertex(tri.a)), PointKey(mesh.GetVertex(tri.b)), PointKey(mesh.GetVertex(tri.c)) };
            int first = 0;
            for (int i = 1; i < 3; ++i)
                if (string.CompareOrdinal(p[i], p[first]) < 0) first = i;
            return p[first] + "/" + p[(first + 1) % 3] + "/" + p[(first + 2) % 3];
        }).OrderBy(s => s, StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }

    static bool SameIds(DMesh3 a, DMesh3 b)
    {
        if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return false;
        return a.VertexIndices().All(v => a.GetVertex(v) == b.GetVertex(v))
            && a.TriangleIndices().All(t => a.GetTriangle(t) == b.GetTriangle(t));
    }

    static void Validate(DMesh3 mesh, string digest)
    {
        if (mesh.TriangleCount == 0 || !mesh.IsClosed() || !mesh.CheckValidity()
            || mesh.Vertices().Any(v => !v.IsFinite) || GeometryDigest(mesh) != digest)
            throw new InvalidOperationException("Generated geometry differs from the serial reference or is invalid");
    }

    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        int cells = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 48;
        int repeats = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        if (cells < 8 || cells > 256 || repeats < 1 || repeats > 20)
            throw new ArgumentOutOfRangeException(nameof(args), "Usage: MarchingCubesBenchmark [cells:8..256] [repeats:1..20]");
        Console.Error.WriteLine($"runtime={Environment.Version}; processors={Environment.ProcessorCount}; OS={Environment.OSVersion}; tiered_compilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}");
        Console.WriteLine("field,cells,parallel,repeat,elapsed_ms,allocated_bytes,vertices,triangles,geometry_sha256,ids_equal_serial");
        foreach (bool skewed in new[] { false, true }) {
            ImplicitFunction3d field = skewed ? (ImplicitFunction3d)new SkewedSphere() : new ImplicitSphere3d { Radius = 1 };
            var mc = new MarchingCubes {
                Implicit = field, Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.25),
                CubeSize = 2.5 / cells, ParallelCompute = false
            };
            mc.Generate();
            DMesh3 reference = mc.Mesh;
            string digest = GeometryDigest(reference);
            foreach (bool parallel in new[] { false, true }) {
                mc.ParallelCompute = parallel;
                mc.Generate();
                Validate(mc.Mesh, digest);
                for (int run = 0; run < repeats; ++run) {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    long before = GC.GetTotalAllocatedBytes(true);
                    var timer = Stopwatch.StartNew();
                    mc.Generate();
                    timer.Stop();
                    long allocated = GC.GetTotalAllocatedBytes(true) - before;
                    Validate(mc.Mesh, digest);
                    Console.WriteLine($"{(skewed ? "skewed" : "sphere")},{cells},{parallel},{run},{timer.Elapsed.TotalMilliseconds:F3},{allocated},{mc.Mesh.VertexCount},{mc.Mesh.TriangleCount},{digest},{SameIds(reference, mc.Mesh)}");
                }
            }
        }
    }
}
