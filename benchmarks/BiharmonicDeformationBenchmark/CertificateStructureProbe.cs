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
        bool growth = args[0] == "--certificate-growth";
        var dimensions = args.Length > 1 ? args[1].Split('x') : Array.Empty<string>();
        int n = 0, height = 0;
        bool dimensionsValid = dimensions.Length == 1 && int.TryParse(dimensions[0], out n);
        if (dimensionsValid) height = n;
        else dimensionsValid = growth && dimensions.Length == 2 && int.TryParse(dimensions[0], out n) && int.TryParse(dimensions[1], out height);
        if (args.Length != 6 || !dimensionsValid || n < 3 || height < 3 ||
            (!growth && (n > 100 || height != n)) || (growth && ((long)n*height > 100000 || args[2] != "solver")) ||
            (args[2] != "natural" && args[2] != "solver") ||
            !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double shift) ||
            !(shift > 0) || !double.IsFinite(shift) || args[4] != "rotated") {
            Console.Error.WriteLine("usage: --certificate-structure <n 3..100> <natural|solver> <positive shift> rotated <fresh output.json>; --certificate-growth <nx>x<ny> solver <positive shift> rotated <fresh output.json> (up to 100000 vertices)"); return 2;
        }
        if (File.Exists(args[5])) { Console.Error.WriteLine("output already exists; use a fresh path"); return 2; }
        var timer = Stopwatch.StartNew();
        int count = n*height, nf = 0;
        var free = new int[count]; var fixedVertices = new bool[count]; var vertices = new long[count][];
        var rows = new HashSet<int>[count]; var faces = new List<int[]>();
        var rotation = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 31);
        double scale = 0;
        for (int i = 0; i < count; i++) {
            var p = rotation * new Vector3d((double)(i%n)/(n-1), (double)(i/n)/(height-1), 0);
            vertices[i] = Bits(p); scale = Math.Max(scale, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
            fixedVertices[i] = i%n == 0 || i%n == n-1; free[i] = fixedVertices[i] ? -1 : nf++;
            rows[i] = new HashSet<int>();
        }
        void Face(int a, int b, int c) {
            faces.Add(new[] {a,b,c});
            // Production keeps every triangle edge key, including zero weights.
            foreach (int i in new[] {a,b,c}) { rows[i].Add(a); rows[i].Add(b); rows[i].Add(c); }
        }
        for (int y = 0; y < height-1; y++) for (int x = 0; x < n-1; x++) {
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
        int[] factorColumnPointers = null;
        SparseCholesky factorForGrowth = null;
        double numericalFactorMilliseconds = 0;
        if (args[2] == "solver") {
            // Strictly diagonally dominant SPD surrogate, with exactly the Q
            // pattern. Only its pattern-dependent ordering is used, never its values.
            var factor = new SparseCholesky(nf); var degree = new int[nf];
            for (int c = 0; c < nf; c++) foreach (int r in pattern[c]) if (r != c) {
                factor.Add(r,c,-1); degree[r]++; degree[c]++;
            }
            for (int i = 0; i < nf; i++) factor.Add(i,i,degree[i]+1);
            var factorTimer = Stopwatch.StartNew();
            if (!factor.Factorize()) throw new InvalidOperationException("ordering surrogate failed: "+factor.Error);
            // Benchmark-only reflection avoids adding a public or friend API.
            var method = typeof(SparseCholesky).GetMethod("CopyPermutation", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("ordering snapshot unavailable");
            factorTimer.Stop(); numericalFactorMilliseconds = factorTimer.Elapsed.TotalMilliseconds;
            order = (int[])method.Invoke(factor, null); numericalNonzeros = factor.FactorNonZeros;
            if (growth) {
                var field = typeof(SparseCholesky).GetField("Lp", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) throw new InvalidOperationException("factor pattern unavailable");
                factorColumnPointers = (int[])field.GetValue(factor);
                factorForGrowth = factor;
            }
        }
        var inverse = new int[nf]; var seen = new bool[nf];
        if (order.Length != nf) throw new InvalidOperationException("ordering size");
        for (int i = 0; i < nf; i++) {
            int old = order[i]; if (old < 0 || old >= nf || seen[old]) throw new InvalidOperationException("ordering bijection");
            seen[old] = true; inverse[old] = i;
        }
        if (growth) {
            // Exact symbolic work from the completed factor column lengths:
            // each column with d strict-lower entries contributes d(d+1)/2
            // Schur updates, without performing those updates diagnostically.
            long schurTotal = 0, initialTotal = 0;
            for (int i = 0; i < nf; i++) {
                int d = factorColumnPointers[i+1]-factorColumnPointers[i]-1;
                if (d < 0) throw new InvalidOperationException("invalid factor column");
                schurTotal = checked(schurTotal+(long)d*(d+1)/2);
                initialTotal += pattern[i].Count;
            }
            var factorRows = (int[])typeof(SparseCholesky).GetField("Li", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(factorForGrowth);
            var precedingCounts = new int[nf];
            for (int column=0; column<nf; column++)
                for (int k=factorColumnPointers[column]+1; k<factorColumnPointers[column+1]; k++) precedingCounts[factorRows[k]]++;
            using (var stream = new FileStream(args[5], FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, new { width=n, height, vertices=count, free=nf, order, preceding_counts=precedingCounts,
                    size=nf, vertices_bits=vertices, faces, @fixed=fixedVertices,
                    scale_bits=BitConverter.DoubleToInt64Bits(scale), shift_bits=BitConverter.DoubleToInt64Bits(shift),
                    assembly_updates=assemblyUpdates, schur_updates=schurTotal, initial_entries=initialTotal,
                    factor_entries=numericalNonzeros, scalar_retained_entries_lower_bound=numericalNonzeros-nf,
                    peak_live_entries_lower_bound=initialTotal, position_certificate="not_checked" });
            Console.WriteLine($"grid={n}x{height} vertices={count} free={nf} ordering=solver structure_complete=True");
            Console.WriteLine($"assembly_updates={assemblyUpdates} schur_updates={schurTotal} total_updates={assemblyUpdates+schurTotal} initial_entries={initialTotal} factor_entries={numericalNonzeros} scalar_retained_entries_lower_bound={numericalNonzeros-nf} peak_live_entries_lower_bound={initialTotal}");
            Console.WriteLine($"surrogate_factor_ms={numericalFactorMilliseconds:F3} diagnostic_total_ms={timer.Elapsed.TotalMilliseconds:F3} allocated_bytes={GC.GetTotalAllocatedBytes(true)} peak_working_set_bytes={Process.GetCurrentProcess().PeakWorkingSet64} position_certificate=not_checked reason=structure_only");
            return 0;
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
        Console.WriteLine($"budget_scope=historical_interval_baseline historical_400_vertex_budget={count<=400} historical_2000_triangle_budget={faces.Count<=2000} historical_1m_update_budget={budget && assemblyUpdates+schur<=1000000} historical_50k_entry_budget={entryBudget} counters={(budget ? "complete" : "partial_lower_bounds")} diagnostic_schur_cap={DiagnosticUpdates}");
        Console.WriteLine($"diagnostic_total_ms={timer.Elapsed.TotalMilliseconds:F3} position_certificate=not_checked reason=structure_only");
        return 0;
    }
}
