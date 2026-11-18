using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

// SolidBooleanPipeline on procedural unwelded triangle soups: the triangle counts after the exact boolean and after the
// reduction to a fraction of the boolean result, the time per stage, the share of clean results (NotCleanReason == ""),
// the stop reason, and MeshRobustRepair on the same inputs (the prophylactic route) for the cost.
// Usage: SolidBooleanBenchmark [fractions, eg 0.25,0.1] | plain | repeated | open | limit | pair
static class Program
{
    // ---- procedural inputs (all unwelded: every triangle has its own three vertices, like an STL read) ----
    static DMesh3 Soup(DMesh3 m)
    {
        var s = new DMesh3();
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            int a = s.AppendVertex(m.GetVertex(tv.a)), b = s.AppendVertex(m.GetVertex(tv.b)), c = s.AppendVertex(m.GetVertex(tv.c));
            s.AppendTriangle(a, b, c);
        }
        return s;
    }

    static DMesh3 Box(Vector3d c, Vector3d e) { return new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh(); }
    static DMesh3 FineBox(Vector3d c, Vector3d e, int n) { return new GridBox3Generator() { Box = new Box3d(c, e), EdgeVertices = n }.Generate().MakeDMesh(); }
    static DMesh3 Sphere(Vector3d c, double r, int n)
    {
        var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }
    static DMesh3 Cylinder(Vector3d c, double r, double h, int slices)
    {
        var m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = slices }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }
    static DMesh3 Torus(Vector3d c, double R, double r, int nu, int nv)
    {
        var m = new DMesh3();
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                m.AppendVertex(c + new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), r * Math.Sin(v), (R + r * Math.Cos(v)) * Math.Sin(u)));
            }
        Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                int a = id(i, j), b = id(i + 1, j), cc = id(i + 1, j + 1), d = id(i, j + 1);
                m.AppendTriangle(a, d, cc); m.AppendTriangle(a, cc, b);
            }
        return m;
    }

    class Case { public string Name; public List<DMesh3> Inputs; public ExactMeshBoolean.Operations Op; }

    static List<Case> Cases()
    {
        var L = new List<Case>();
        Action<string, ExactMeshBoolean.Operations, Func<DMesh3[]>> add = (n, op, f) => L.Add(new Case { Name = n, Op = op, Inputs = f().Select(Soup).ToList() });
        var U = ExactMeshBoolean.Operations.Union; var I = ExactMeshBoolean.Operations.Intersection; var D = ExactMeshBoolean.Operations.Difference;
        add("box+box overlap U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(0.7, 0.4, 0.2), new Vector3d(1, 1, 1)) });
        add("box+box face-touch U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(2, 0, 0), new Vector3d(1, 1, 1)) });
        add("box+box edge-touch U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(2, 2, 0), new Vector3d(1, 1, 1)) });
        add("sphere+sphere overlap U", U, () => new[] { Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 24) });
        add("sphere&sphere overlap I", I, () => new[] { Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 24) });
        add("sphere-sphere D", D, () => new[] { Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(1.0, 0.1, 0.05), 0.6, 24) });
        add("sphere+sphere tangent-ish U", U, () => new[] { Sphere(Vector3d.Zero, 1, 16), Sphere(new Vector3d(2.0, 0, 0), 1, 16) });
        add("box-cylinder D", D, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 48) });
        add("torus+cylinder U", U, () => new[] { Torus(Vector3d.Zero, 1, 0.3, 64, 24), Cylinder(new Vector3d(1, -0.8, 0), 0.25, 1.6, 48) });
        add("torus+torus linked U", U, () => new[] { Torus(Vector3d.Zero, 1, 0.3, 64, 24), Torus(new Vector3d(1, 0, 0), 1, 0.3, 64, 24) });
        add("fine box edge-touch U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), FineBox(new Vector3d(2, 2, 0), new Vector3d(1, 1, 1), 16) });
        add("fine box vertex-touch U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), FineBox(new Vector3d(2, 2, 2), new Vector3d(1, 1, 1), 16) });
        add("sphere on box face (pole touch) U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), Sphere(new Vector3d(0, 2, 0), 1, 25) });
        add("two cylinders line-touch U", U, () => new[] { Cylinder(new Vector3d(-0.5, 0, 0), 0.5, 2, 64), Cylinder(new Vector3d(0.5, 0, 0), 0.5, 2, 64) });
        add("cylinder on box, partial face-touch U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), Cylinder(new Vector3d(0.5, 1, 0), 0.5, 1, 64) });
        add("3 spheres U", U, () => new[] { Sphere(Vector3d.Zero, 1, 20), Sphere(new Vector3d(1, 0.2, 0), 1, 20), Sphere(new Vector3d(0.5, 1, 0.1), 1, 20) });
        add("box+sphere+cyl U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Sphere(new Vector3d(1, 1, 1), 0.8, 24), Cylinder(new Vector3d(-0.5, -1.5, 0), 0.3, 3, 32) });
        return L;
    }

    // positions with more than one vertex
    static int CountContacts(DMesh3 m)
    {
        var d = new Dictionary<Vector3d, int>();
        foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); d[p] = d.TryGetValue(p, out int n) ? n + 1 : 1; }
        return d.Values.Count(n => n > 1);
    }

    // the contacts of a mesh by position: vertices at a contact, edges with an end at one, and of those the edges whose two ends
    // each have exactly one twin vertex and the twin edge exists (a pair a paired collapse can take), the groups of three or more
    static (int vertices, int edges, int paired, int bigGroups) TwinStats(DMesh3 m)
    {
        var groups = new Dictionary<Vector3d, List<int>>();
        foreach (int v in m.VertexIndices()) {
            var p = m.GetVertex(v); if (p.x == 0) p.x = 0; if (p.y == 0) p.y = 0; if (p.z == 0) p.z = 0;
            if (!groups.TryGetValue(p, out var l)) groups[p] = l = new List<int>();
            l.Add(v);
        }
        var twin = new Dictionary<int, int>(); int cv = 0, big = 0;
        var inGroup = new HashSet<int>();
        foreach (var l in groups.Values) {
            if (l.Count < 2) continue;
            cv += l.Count; foreach (int v in l) inGroup.Add(v);
            if (l.Count == 2) { twin[l[0]] = l[1]; twin[l[1]] = l[0]; } else big++;
        }
        int ce = 0, paired = 0;
        foreach (int e in m.EdgeIndices()) {
            var ev = m.GetEdgeV(e);
            if (!inGroup.Contains(ev.a) && !inGroup.Contains(ev.b)) continue;
            ce++;
            if (twin.TryGetValue(ev.a, out int ta) && twin.TryGetValue(ev.b, out int tb) && m.FindEdge(ta, tb) != DMesh3.InvalidID) paired++;
        }
        return (cv, ce, paired, big);
    }

    static void PlainReduce()
    {
        // the Reducer alone on flat-faced and round closed meshes: the cost of MinTriangleQuality = 0
        foreach (var (name, mesh) in new[] { ("fine box 16", FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16)), ("sphere 25", Sphere(Vector3d.Zero, 1, 25)) })
            foreach (double q in new[] { 0.0, 0.02 })
                foreach (double f in new[] { 0.25, 0.1 }) {
                    var m = new DMesh3(mesh);
                    var r = new Reducer(m) { PreventSelfIntersections = true, MinCollapseTriangleQuality = q };
                    r.ReduceToTriangleCount((int)(mesh.TriangleCount * f));
                    Console.WriteLine($"plain {name} quality={q} f={f}: {m.TriangleCount} of {mesh.TriangleCount} tris, {(MeshRobustRepair.NotCleanReason(m) is "" ? "clean" : MeshRobustRepair.NotCleanReasons(m))}");
                }
    }

    static long Time(Action a) { var sw = Stopwatch.StartNew(); a(); return sw.ElapsedMilliseconds; }

    static string Short(string reason) { return reason.Length == 0 ? "clean" : reason; }

    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length > 0 && args[0] == "plain") { PlainReduce(); return; }
        if (args.Length > 0 && args[0] == "repeated") { Repeated(); return; }
        if (args.Length > 0 && args[0] == "open") { OpenInput(); return; }
        if (args.Length > 0 && args[0] == "limit") { Limit(); return; }
        if (args.Length > 0 && args[0] == "pair") { Pair(); return; }
        double[] fractions = args.Length > 0 ? args[0].Split(',').Select(double.Parse).ToArray() : new[] { 0.25, 0.1 };
        Console.WriteLine("| case | in tris | boolean tris | boolean ms | contacts | " + string.Join(" | ", fractions.Select(f => $"{f:P0}: tris, reduce ms, contacts, result, stop")) + " | RobustRepair of the soup: ms, tris, result |");
        Console.WriteLine("|---|---|---|---|---|" + string.Concat(fractions.Select(f => "---|")) + "---|");
        int total = 0, cleanBool = 0, cleanRed = 0, cleanRedNoContacts = 0, withContacts = 0;
        foreach (var c in Cases()) {
            int inTris = c.Inputs.Sum(m => m.TriangleCount);
            string line = $"| {c.Name} | {inTris} | ";
            bool first = true;
            foreach (double f in fractions) {
                var p = new SolidBooleanPipeline(c.Inputs, c.Op) { TargetFraction = f };
                p.Compute();
                var r = p.Report;
                if (p.Status != SolidBooleanPipeline.PipelineStatus.Success) { line += p.Status + " " + p.Error + " | "; continue; }
                if (first) { line += $"{r.BooleanTriangles} | {r.BooleanMilliseconds} | {r.ContactsBoolean} | "; first = false; }
                line += $"{r.ResultTriangles} (target {r.TargetTriangles}), {r.ReductionMilliseconds}, {r.ContactsResult}, {Short(r.NotCleanReason)}, {r.StopReason} | ";
                total++;
                if (r.Clean) cleanRed++;
                if (r.ContactsBoolean > 0) withContacts++; else if (r.Clean) cleanRedNoContacts++;
            }
            var cat = new DMesh3();
            foreach (var mm in c.Inputs) MeshEditor.Append(cat, mm);
            var rob = new MeshRobustRepair(cat);
            long mrs = Time(() => rob.Apply());
            line += $"{mrs}, {rob.Mesh.TriangleCount}, {Short(MeshRobustRepair.NotCleanReason(rob.Mesh))} |";
            Console.WriteLine(line);
        }
        Console.WriteLine($"\nreduced results: {total}, clean {cleanRed} ({100.0 * cleanRed / Math.Max(1, total):F0} %); of the {total - withContacts} without contacts in the boolean result, clean {cleanRedNoContacts}");
    }

    // repeated booleans: the result of each step is the next step's input (welded, closed), union with a shifted sphere
    static void Repeated()
    {
        Console.WriteLine("| step | input tris | boolean tris | boolean ms | reduced tris (50 %) | reduce ms | result |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        DMesh3 acc = Sphere(Vector3d.Zero, 1, 20);
        DMesh3 accReduced = acc;
        for (int k = 1; k <= 6; ++k) {
            DMesh3 next = Soup(Sphere(new Vector3d(0.9 * k, 0.15 * k, 0.05 * k), 1, 20));
            var p = new SolidBooleanPipeline(accReduced, next, ExactMeshBoolean.Operations.Union) { TargetFraction = 0.5 };
            p.Compute();
            var r = p.Report;
            Console.WriteLine($"| {k} | {r.InputTriangles[0]} + {r.InputTriangles[1]} | {r.BooleanTriangles} | {r.BooleanMilliseconds} | {r.ResultTriangles} | {r.ReductionMilliseconds} | {Short(r.NotCleanReason)}, {r.StopReason} |");
            accReduced = p.Result;
        }
        // the same chain without reductions: the triangle count grows
        DMesh3 plain = Sphere(Vector3d.Zero, 1, 20);
        for (int k = 1; k <= 6; ++k) {
            var p = new SolidBooleanPipeline(plain, Soup(Sphere(new Vector3d(0.9 * k, 0.15 * k, 0.05 * k), 1, 20)), ExactMeshBoolean.Operations.Union);
            p.Compute();
            Console.WriteLine($"no reduction, step {k}: {p.Report.InputTriangles[0]} + {p.Report.InputTriangles[1]} -> {p.Report.BooleanTriangles} tris, {Short(p.Report.NotCleanReason)}");
            plain = p.Result;
        }
    }

    // how far contacts let the reduction go: two subdivided boxes apart, touching at a vertex and along an edge, asked for 4 triangles
    static void Limit()
    {
        Console.WriteLine("| boxes (EdgeVertices) | placement | boolean tris | result tris (target 4) | contacts left | stop reason | blocked edges: guard / quality, at contacts | with PairContacts: result tris, contacts left, stop reason, paired collapses, result |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (int n in new[] { 4, 8, 12 })
            foreach (var (name, off) in new[] { ("apart", new Vector3d(2.5, 2.5, 0)), ("vertex touch", new Vector3d(2, 2, 2)), ("edge touch", new Vector3d(2, 2, 0)) }) {
                var p = new SolidBooleanPipeline(new[] { Soup(FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), n)), Soup(FineBox(off, new Vector3d(1, 1, 1), n)) }, ExactMeshBoolean.Operations.Union) { TargetTriangleCount = 4 };
                p.Compute();
                var r = p.Report;
                var pp = new SolidBooleanPipeline(p.Inputs, ExactMeshBoolean.Operations.Union) { TargetTriangleCount = 4, PairContacts = true };
                pp.Compute();
                var q = pp.Report;
                Console.WriteLine($"| {n} | {name} | {r.BooleanTriangles} | {r.ResultTriangles} | {r.ContactsResult} | {r.StopReason} | {r.BlockedByGuard} / {r.BlockedByQuality}, {r.BlockedAtContacts} | {q.ResultTriangles}, {q.ContactsResult}, {q.StopReason}, {q.PairedCollapses}, {Short(q.NotCleanReason)} |");
            }
        Console.WriteLine();
        Console.WriteLine("Edges at contacts in the boolean result (the upper bound of what a paired collapse can take): vertices at contacts, edges with an end at one, of those the paired ones (both ends with one twin, twin edge exists), contact positions with 3 or more vertices");
        foreach (var c in Cases().Where(c => c.Name.Contains("touch") || c.Name.Contains("tangent"))) {
            var p = new SolidBooleanPipeline(c.Inputs, c.Op);
            p.Compute();
            var (v, e, pr, big) = TwinStats(p.Result);
            Console.WriteLine($"{c.Name}: boolean {p.Result.TriangleCount} tris, {v} vertices at contacts, {e} edges at them, {pr} paired, {big} positions with 3+ vertices");
        }
    }

    // the cases that touch: the pipeline without and with PairContacts at two targets (the table of the README)
    static void Pair()
    {
        Console.WriteLine("| case | boolean tris | contacts | target | tris off | contacts off | result off | stop off | ms off | tris PairContacts | contacts | result | stop | paired collapses | ms | closed |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var c in Cases().Where(c => c.Name.Contains("touch") || c.Name.Contains("tangent")))
            foreach (double f in new[] { 0.25, 0.1 }) {
                var off = new SolidBooleanPipeline(c.Inputs, c.Op) { TargetFraction = f };
                off.Compute();
                var on = new SolidBooleanPipeline(c.Inputs, c.Op) { TargetFraction = f, PairContacts = true };
                on.Compute();
                var a = off.Report; var b = on.Report;
                if (a.ContactsBoolean == 0 && f == 0.25) { Console.WriteLine($"| {c.Name} | {a.BooleanTriangles} | 0 | (no contacts: the same result, {a.ResultTriangles} = {b.ResultTriangles} tris: {(a.ResultTriangles == b.ResultTriangles)}) |"); }
                if (a.ContactsBoolean == 0) continue;
                Console.WriteLine($"| {c.Name} | {a.BooleanTriangles} | {a.ContactsBoolean} | {a.TargetTriangles} | {a.ResultTriangles} | {a.ContactsResult} | {Short(a.NotCleanReason)} | {a.StopReason} | {a.ReductionMilliseconds} | {b.ResultTriangles} | {b.ContactsResult} | {Short(b.NotCleanReason)} | {b.StopReason} | {b.PairedCollapses} | {b.ReductionMilliseconds} | {on.Result.IsClosed()} |");
            }
    }

    // inputs that are not closed: a sphere with 3 triangles removed, a box soup with a crack
    static void OpenInput()
    {
        DMesh3 holed = Sphere(Vector3d.Zero, 1, 20);
        foreach (int t in holed.TriangleIndices().Take(3).ToArray()) holed.RemoveTriangle(t);
        DMesh3 other = Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 20));
        foreach (var mode in new[] { SolidBooleanPipeline.InputRepairModes.Refuse, SolidBooleanPipeline.InputRepairModes.AutoRepair, SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust }) {
            var p = new SolidBooleanPipeline(holed, other, ExactMeshBoolean.Operations.Union) { InputRepair = mode, TargetFraction = 0.25 };
            bool ok = p.Compute();
            var r = p.Report;
            Console.WriteLine($"{mode}: {p.Status} {p.Error} | repaired: {string.Join(", ", r.RepairedInputs.Select(x => $"input {x.Index} by {x.Method} {x.TrianglesBefore}->{x.TrianglesAfter} in {x.Milliseconds} ms"))} | tris {r.BooleanTriangles} -> {r.ResultTriangles}, {Short(r.NotCleanReason)}, total {r.TotalMilliseconds} ms");
        }
    }
}
