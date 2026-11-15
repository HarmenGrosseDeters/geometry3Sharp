// Procedural structural diagnosis only. This does not run a deformation solve.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using g3;

static class CertificateStructureProbe
{
    const long DiagnosticUpdates = 50000000;
    static long[] Bits(Vector3d p) => new[] { BitConverter.DoubleToInt64Bits(p.x), BitConverter.DoubleToInt64Bits(p.y), BitConverter.DoubleToInt64Bits(p.z) };

    public static int Run(string[] args)
    {
        if (args.Length != 6 || !int.TryParse(args[1], out int n) || n < 3 || n > 100 ||
            (args[2] != "natural" && args[2] != "solver") ||
            !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double shift) ||
            !(shift > 0) || !double.IsFinite(shift) || args[4] != "rotated") {
            Console.Error.WriteLine("usage: --certificate-structure <n 3..100> <natural|solver> <positive shift> rotated <fresh output.json>"); return 2;
        }
        if (File.Exists(args[5])) { Console.Error.WriteLine("output already exists; use a fresh path"); return 2; }
        var timer = Stopwatch.StartNew();
        int count = n*n, nf = 0;
        var free = new int[count]; var fixedVertices = new bool[count]; var vertices = new long[count][];
        var rows = new HashSet<int>[count]; var faces = new List<int[]>();
        var rotation = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 31);
        double scale = 0;
        for (int i = 0; i < count; i++) {
            var p = rotation * new Vector3d((double)(i%n)/(n-1), (double)(i/n)/(n-1), 0);
            vertices[i] = Bits(p); scale = Math.Max(scale, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
            fixedVertices[i] = i%n == 0 || i%n == n-1; free[i] = fixedVertices[i] ? -1 : nf++;
            rows[i] = new HashSet<int>();
        }
        void Face(int a, int b, int c) {
            faces.Add(new[] {a,b,c});
            // Production keeps every triangle edge key, including zero weights.
            foreach (int i in new[] {a,b,c}) { rows[i].Add(a); rows[i].Add(b); rows[i].Add(c); }
        }
        for (int y = 0; y < n-1; y++) for (int x = 0; x < n-1; x++) {
            int a = x+n*y, b = a+1, d = a+n, c = d+1; Face(a,b,c); Face(a,c,d);
        }
        var pattern = new HashSet<int>[nf]; for (int i = 0; i < nf; i++) pattern[i] = new HashSet<int>();
        long assemblyUpdates = 0;
        foreach (var row in rows) {
            var entries = new List<int>(); foreach (int v in row) if (free[v] >= 0) entries.Add(free[v]);
            for (int i = 0; i < entries.Count; i++) for (int j = 0; j <= i; j++) {
                int a = entries[i], b = entries[j]; pattern[Math.Min(a,b)].Add(Math.Max(a,b)); assemblyUpdates++;
            }
        }
        var order = new int[nf]; for (int i = 0; i < nf; i++) order[i] = i;
        int numericalNonzeros = 0;
        if (args[2] == "solver") {
            // Strictly diagonally dominant SPD surrogate, with exactly the Q
            // pattern. Only its pattern-dependent ordering is used, never its values.
            var factor = new SparseCholesky(nf); var degree = new int[nf];
            for (int c = 0; c < nf; c++) foreach (int r in pattern[c]) if (r != c) {
                factor.Add(r,c,-1); degree[r]++; degree[c]++;
            }
            for (int i = 0; i < nf; i++) factor.Add(i,i,degree[i]+1);
            if (!factor.Factorize()) throw new InvalidOperationException("ordering surrogate failed: "+factor.Error);
            // Benchmark-only reflection avoids adding a public or friend API.
            var method = typeof(SparseCholesky).GetMethod("CopyPermutation", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("ordering snapshot unavailable");
            order = (int[])method.Invoke(factor, null); numericalNonzeros = factor.FactorNonZeros;
        }
        var inverse = new int[nf]; var seen = new bool[nf];
        if (order.Length != nf) throw new InvalidOperationException("ordering size");
        for (int i = 0; i < nf; i++) {
            int old = order[i]; if (old < 0 || old >= nf || seen[old]) throw new InvalidOperationException("ordering bijection");
            seen[old] = true; inverse[old] = i;
        }
        var columns = new HashSet<int>[nf]; for (int i = 0; i < nf; i++) columns[i] = new HashSet<int>();
        long initial = 0;
        for (int c = 0; c < nf; c++) foreach (int r in pattern[c]) {
            int a = inverse[r], b = inverse[c]; if (columns[Math.Min(a,b)].Add(Math.Max(a,b))) initial++;
        }
        long live = initial, peak = initial, schur = 0, factorEntries = 0; int completed = 0;
        bool budget = true;
        for (int k = 0; k < nf; k++) {
            var neighbors = new List<int>(columns[k]); neighbors.Remove(k); neighbors.Sort();
            long work = (long)neighbors.Count*(neighbors.Count+1)/2;
            if (work > DiagnosticUpdates-schur) { budget = false; break; }
            schur += work; factorEntries += neighbors.Count+1;
            for (int i = 0; i < neighbors.Count; i++) for (int j = 0; j <= i; j++)
                if (columns[neighbors[j]].Add(neighbors[i])) { live++; peak = Math.Max(peak,live); }
            live -= columns[k].Count; columns[k].Clear(); completed++;
        }
        if (budget && args[2] == "solver" && factorEntries != numericalNonzeros)
            throw new InvalidOperationException("symbolic pattern disagrees with SparseCholesky");
        using (var stream = new FileStream(args[5], FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(stream, new Dictionary<string,object> {
                ["size"] = nf, ["vertices_bits"] = vertices, ["faces"] = faces, ["fixed"] = fixedVertices, ["order"] = order,
                ["scale_bits"] = BitConverter.DoubleToInt64Bits(scale), ["shift_bits"] = BitConverter.DoubleToInt64Bits(shift),
                ["structure"] = new { assembly_updates = assemblyUpdates, schur_updates = schur, initial_entries = initial,
                    peak_live_entries = peak, factor_entries = factorEntries, completed_columns = completed, complete = budget }
            });
        Console.WriteLine($"grid={n} vertices={count} triangles={faces.Count} free={nf} ordering={args[2]} structure_complete={budget} completed_columns={completed}");
        Console.WriteLine($"assembly_updates={assemblyUpdates} schur_updates={schur} total_updates={assemblyUpdates+schur} initial_entries={initial} peak_live_entries={peak} factor_entries={factorEntries} numerical_factor_nonzeros={numericalNonzeros}");
        string entryBudget = peak > 50000 ? "False" : budget ? "True" : "unknown";
        Console.WriteLine($"production_vertex_budget={count<=400} triangle_budget={faces.Count<=2000} update_budget={budget && assemblyUpdates+schur<=1000000} entry_budget={entryBudget} counters={(budget ? "complete" : "partial_lower_bounds")} diagnostic_schur_cap={DiagnosticUpdates}");
        Console.WriteLine($"diagnostic_total_ms={timer.Elapsed.TotalMilliseconds:F3} position_certificate=not_checked reason=structure_only");
        return 0;
    }
}
