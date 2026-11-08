using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meshing2Acceptance;

// Diagnostic tool only. `phase` invokes the actual private preparation and Sources
// methods, then stops without issuing a verdict. It also runs against the original
// oracle, before any optimization or instrumentation exists. `full` uses Check.
static class Program
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Emit(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
    static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
    static string LensInput(int m)
    {
        if (m < 3) throw new ArgumentOutOfRangeException(nameof(m));
        var x = new List<double>(); var y = new List<double>();
        for (int i = 0; i < m; ++i) { double t = -1 + 2.0 * i / (m - 1); x.Add(t); y.Add(0.25 * (1 - t * t)); }
        for (int i = m - 2; i >= 1; --i) { double t = -1 + 2.0 * i / (m - 1); x.Add(t); y.Add(-0.25 * (1 - t * t)); }
        x.Reverse(); y.Reverse();
        var input = new Mesh2Input { X = x.ToArray(), Y = y.ToArray() };
        input.Rings.Add(new RingInput(Enumerable.Range(0, x.Count).ToArray(), false));
        return TextFormat.WriteInput(input);
    }
    static object Read(object instance, string field) => instance.GetType().GetField(field, Members).GetValue(instance);
    static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Members).SetValue(instance, value);
    static object Call(object instance, string name) => instance.GetType().GetMethod(name, Members).Invoke(instance, null);
    static void Phase(Mesh2Input input, Mesh2Output mesh)
    {
        var type = typeof(Oracle).GetNestedType("Run", BindingFlags.NonPublic);
        var run = Activator.CreateInstance(type, Members, null, new object[] { input, mesh, new OracleExpect { Milestone = Milestone.M1 } }, null);
        var prep = Stopwatch.StartNew();
        if (!(bool)Call(run, "CheckInput") || !(bool)Call(run, "Vertices")) throw new InvalidOperationException("Invalid input or vertices");
        Set(run, "domain", new Domain(input));
        Set(run, "tube", Q.Zero); Set(run, "tube2", Q.Zero); Set(run, "o11Tol2", Q.Zero);
        ((OracleReport)Read(run, "r")).O11Tolerance2 = Q.Zero;
        if (!(bool)Call(run, "Indices") || !(bool)Call(run, "Identity")) throw new InvalidOperationException("Invalid indices or identity");
        Call(run, "Orientation");
        if (!((OracleReport)Read(run, "r")).Ok) throw new InvalidOperationException("Preparation failed: " + Read(run, "r"));
        Call(run, "EdgesAndNeighbours");
        var edgeCount = ((System.Collections.IDictionary)Read(run, "edges")).Count;
        var sources = type.GetMethod("Sources", Members);
        prep.Stop();
        int expectedSources = input.Segments.Count + input.Rings.Sum(r => r.Vertices.Length)
            + input.Regions.Where(r => r.Ring != null).Sum(r => r.Ring.Length);
        Emit(new { phase = "sources-begin", judgment_complete = false, vertices = mesh.VertexCount,
                   edges = edgeCount, input_source_occurrences = expectedSources,
                   pair_upper_bound = (long)edgeCount * expectedSources, preparation_seconds = prep.Elapsed.TotalSeconds });
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        sources.Invoke(run, null);
        watch.Stop(); process.Refresh();
        var keys = ((IEnumerable<long>)Read(run, "constraintEdges")).OrderBy(k => k).ToArray();
        var work = typeof(Oracle).GetProperty("SourceWorkForTests", BindingFlags.Static | BindingFlags.NonPublic);
        Emit(new { phase = "sources-complete", judgment_complete = false, seconds = watch.Elapsed.TotalSeconds,
                   cpu_seconds = (process.TotalProcessorTime - cpu).TotalSeconds,
                   sources = ((System.Collections.ICollection)Read(run, "sources")).Count,
                   constraint_edges = keys.Length, fingerprint = Hash(string.Join("\n", keys)),
                   candidate_visits = work?.GetValue(null) });
    }
    sealed class RationalJson : JsonConverter<Q>
    {
        public override Q Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Q value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
    sealed class IntegerJson : JsonConverter<BigInteger>
    {
        public override BigInteger Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "lens-input") { File.WriteAllText(args[2], LensInput(int.Parse(args[1]))); return 0; }
        if (args.Length < 3 || (args[0] != "phase" && args[0] != "full")) {
            Console.Error.WriteLine("OracleSourcePilot lens-input <m> <input-file> | phase <input> <mesh> | full <input> <mesh> <report-file>");
            return 2;
        }
        var input = TextFormat.ReadInput(File.ReadAllText(args[1]));
        var mesh = TextFormat.ReadOutput(File.ReadAllText(args[2]), out _);
        if (args[0] == "phase") { Phase(input, mesh); return 0; }
        if (args.Length != 4) return 2;
        var report = Oracle.Check(input, mesh, new OracleExpect { Milestone = Milestone.M1 });
        var options = new JsonSerializerOptions { IncludeFields = true };
        options.Converters.Add(new RationalJson()); options.Converters.Add(new IntegerJson());
        File.WriteAllText(args[3], JsonSerializer.Serialize(report, options));
        Emit(new { judgment_complete = true, report.Ok, report.Unique, triangles = mesh.Triangles.Length });
        return report.Ok ? 0 : 1;
    }
}
