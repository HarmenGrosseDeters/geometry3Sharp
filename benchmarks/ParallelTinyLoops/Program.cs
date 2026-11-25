using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;

// Crossover of gParallel.ForRange against a plain loop, per item count and per-item cost, alone and with a busy thread pool.
// Usage: ParallelTinyLoops crossover | busy | e2e [busy]
// All inputs are procedural; the numbers are times per call (median of the repetitions).
static class Program
{
    static volatile int sink;

    // a body of about `spins` * 1 ns of dependent integer work
    static int Work(int i, int spins)
    {
        uint x = (uint)i * 2654435761u + 1;
        for (int k = 0; k < spins; ++k)
            x = x * 1664525u + 1013904223u;
        return (int)x;
    }

    static double MedianUs(Action run, int reps)
    {
        var t = new double[reps];
        var sw = new Stopwatch();
        for (int r = 0; r < reps; ++r) {
            sw.Restart();
            run();
            t[r] = sw.Elapsed.TotalMilliseconds * 1000.0;
        }
        Array.Sort(t);
        return t[reps / 2];
    }

    // the load of the issue: four items that each loop Parallel.For of 64 items x ~10 ms
    sealed class BusyPool : IDisposable
    {
        volatile bool stop;
        Task[] tasks;
        public BusyPool(int loads = 4)
        {
            tasks = new Task[loads];
            for (int i = 0; i < loads; ++i)
                tasks[i] = Task.Run(() => {
                    while (!stop)
                        Parallel.For(0, 64, (j) => { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < 10) { } });
                });
        }
        public void Dispose() { stop = true; Task.WaitAll(tasks); }
    }

    static void Crossover(bool busy)
    {
        int[] counts = { 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 4096, 16384, 65536, 262144 };
        int[] costs = { 0, 20, 200, 2000 };    // spins per item: ~1 ns, ~20 ns, ~200 ns, ~2 us
        Console.WriteLine("# crossover " + (busy ? "BUSY pool" : "alone") + ", " + Environment.ProcessorCount + " cores; time per call in us (median)");
        Console.WriteLine("| items | spins/item | serial us | ForRange us | ForRangeSmall us | ForEach(Range) us | ForEachSmall us | ForRange / serial | Small / min(serial, ForRange) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        using (busy ? new BusyPool() : null) {
            if (busy) Thread.Sleep(500);
            foreach (int spins in costs)
                foreach (int n in counts) {
                    if ((long)n * Math.Max(spins, 1) > 40_000_000L) continue;
                    var res = new int[n];
                    int reps = busy ? (n <= 1024 ? 15 : 7) : (n * Math.Max(spins, 1) < 100000 ? 301 : 31);
                    double ser = MedianUs(() => { for (int i = 0; i < n; ++i) res[i] = Work(i, spins); }, reps);
                    double par = MedianUs(() => gParallel.ForRange(n, (i) => res[i] = Work(i, spins)), reps);
#if BASELINE
                    double sm = 0, fes = 0;     // (the base commit has no Small loops)
#else
                    double sm = MedianUs(() => gParallel.ForRangeSmall(n, (i) => res[i] = Work(i, spins)), reps);
                    double fes = MedianUs(() => gParallel.ForEachSmall(Interval1i.Range(n), (i) => res[i] = Work(i, spins)), reps);
#endif
                    double fe = MedianUs(() => gParallel.ForEach(Interval1i.Range(n), (i) => res[i] = Work(i, spins)), reps);
                    sink = res[n / 2];
                    Console.WriteLine("| " + n + " | " + spins + " | " + F(ser) + " | " + F(par) + " | " + F(sm) + " | " + F(fe) + " | " + F(fes) + " | " + (par / Math.Max(ser, 0.01)).ToString("F2", CultureInfo.InvariantCulture) + " | " + (sm / Math.Max(Math.Min(ser, par), 0.01)).ToString("F2", CultureInfo.InvariantCulture) + " |");
                }
        }
    }

    static string F(double v) { return v.ToString(v < 10 ? "F2" : "F0", CultureInfo.InvariantCulture); }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "crossover";
        bool busy = args.Contains("busy");
        string b = args.FirstOrDefault(a => a.StartsWith("budget="));
#if !BASELINE
        if (b != null)
            gParallel.SmallLoopBudgetTicksOnThisThread = (long)(double.Parse(b.Substring(7), CultureInfo.InvariantCulture) * 1e-6 * Stopwatch.Frequency);   // microseconds
#endif
        if (mode == "crossover") Crossover(busy);
        else if (mode == "e2e") EndToEnd.Run(args.Skip(1).ToArray());
        else Console.WriteLine("usage: ParallelTinyLoops crossover [busy] | e2e [busy] [serial]");
    }
}
