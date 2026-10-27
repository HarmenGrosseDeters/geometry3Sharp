using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace geometry3Sharp.Tests
{
    /// <summary> options of MeshMinimizer and DeltaDebug.MinimizeBytes (docs/QA.md 2.9) </summary>
    public sealed class MinimizeOptions
    {
        /// <summary>
        /// The whole run stops after this and returns the smallest failing input found so far. It is checked before each
        /// predicate call, so a run can overshoot by one call (at most PredicateTimeout).
        /// </summary>
        public TimeSpan TimeBudget = TimeSpan.FromSeconds(60);
        /// <summary> one predicate call; zero or less runs the predicate on the calling thread without a timeout </summary>
        public TimeSpan PredicateTimeout = TimeSpan.FromSeconds(10);
        /// <summary>
        /// A predicate that throws is a different error, not the one being minimized (false), unless this is set. A
        /// predicate that reproduces a crash should catch it and compare it with the original (see FailureMinimizer).
        /// </summary>
        public bool ExceptionMeansFailure = false;
        /// <summary> a predicate call that does not finish within PredicateTimeout is a different error, unless this is set </summary>
        public bool TimeoutMeansFailure = false;
        /// <summary> a call that times out keeps running (it cannot be cancelled): stop after this many </summary>
        public int MaxTimeouts = 3;
        /// <summary> 0 keeps the input order; another value shuffles the order in which subsets are tried (reproducibly) </summary>
        public int Seed = 0;
        /// <summary> MeshMinimizer: round the coordinates to coarser grids while the error persists </summary>
        public bool SimplifyCoordinates = true;
    }

    /// <summary>
    /// A predicate ("the error is still present") with the policies of MinimizeOptions: time budget, timeout,
    /// exceptions. Counts the calls.
    /// </summary>
    public sealed class Probe<T>
    {
        readonly Func<T, bool> predicate;
        readonly MinimizeOptions options;
        readonly Stopwatch clock = Stopwatch.StartNew();

        public int Evaluations, Exceptions, Timeouts;
        /// <summary> the first exception a predicate threw (for the report) </summary>
        public Exception FirstException;

        public Probe(Func<T, bool> predicate, MinimizeOptions options)
        {
            this.predicate = predicate;
            this.options = options;
        }

        public TimeSpan Elapsed => clock.Elapsed;
        public bool BudgetExhausted => clock.Elapsed >= options.TimeBudget;
        public bool TimeoutLimitReached => Timeouts >= options.MaxTimeouts;
        public bool Stopped => BudgetExhausted || TimeoutLimitReached;

        /// <summary> true if the error is present in input </summary>
        public bool Fails(T input)
        {
            ++Evaluations;
            bool result = false;
            Exception error = null;
            Action run = () => {
                try { result = predicate(input); } catch (Exception e) { error = e; }
            };
            if (options.PredicateTimeout <= TimeSpan.Zero)
                run();
            else if (Timed.Finishes(run, options.PredicateTimeout) == false) {
                ++Timeouts;
                return options.TimeoutMeansFailure;
            }
            if (error != null) {
                ++Exceptions;
                if (FirstException == null) FirstException = error;
                return options.ExceptionMeansFailure;
            }
            return result;
        }
    }

    /// <summary>
    /// Delta debugging: ddmin of Zeller and Hildebrandt, "Simplifying and Isolating Failure-Inducing Input" (IEEE TSE
    /// 28(2), 2002). Implemented from the paper.
    /// </summary>
    public static class DeltaDebug
    {
        /// <summary>
        /// A 1-minimal failing subset of items (removing any single item makes the error disappear), unless stop() ends
        /// the search early: then the smallest failing subset found so far. items itself must fail (not checked). fails
        /// gets the subsets in the order of items. tryEmpty tests the empty subset first.
        /// </summary>
        public static List<T> Minimize<T>(IReadOnlyList<T> items, Func<List<T>, bool> fails, Func<bool> stop, int seed = 0, bool tryEmpty = true)
        {
            var c = Enumerable.Range(0, items.Count).ToList();
            if (seed != 0) {
                var rand = new Random(seed);
                c = c.OrderBy(i => rand.Next()).ToList();
            }
            // every subset is tested once: a failing one becomes c, and later candidates are strict subsets of it, so a
            // subset seen before did not fail. (Keyed by a 64-bit hash, not the index list, which needs O(n^2) memory for
            // byte inputs; a collision only skips a test, the result still fails.)
            var tested = new HashSet<ulong>();
            bool Fails(List<int> s)
            {
                List<int> sorted = s.OrderBy(i => i).ToList();
                if (tested.Add(Hash(sorted)) == false) return false;
                return fails(sorted.Select(i => items[i]).ToList());
            }
            List<T> Result() => c.OrderBy(i => i).Select(i => items[i]).ToList();

            if (tryEmpty && c.Count > 0 && stop() == false && Fails(new List<int>()))
                return new List<T>();
            int n = 2;
            while (c.Count >= 2 && stop() == false) {
                List<List<int>> chunks = Split(c, n);
                bool reduced = false;
                for (int i = 0; i < chunks.Count && reduced == false && stop() == false; ++i)
                    if (Fails(chunks[i])) { c = chunks[i]; n = 2; reduced = true; }
                // (with two chunks the complements are the chunks)
                if (chunks.Count > 2)
                    for (int i = 0; i < chunks.Count && reduced == false && stop() == false; ++i) {
                        List<int> complement = chunks.Where((_, j) => j != i).SelectMany(x => x).ToList();
                        if (Fails(complement)) { c = complement; n = Math.Max(n - 1, 2); reduced = true; }
                    }
                if (reduced == false) {
                    if (n >= c.Count) break;
                    n = Math.Min(2 * n, c.Count);
                }
            }
            return Result();
        }

        // FNV-1a over the indices and the count
        static ulong Hash(List<int> s)
        {
            ulong h = 14695981039346656037UL;
            foreach (int i in s) { h ^= (uint)i; h *= 1099511628211UL; }
            h ^= (uint)s.Count; h *= 1099511628211UL;
            return h;
        }

        /// <summary> c in n contiguous chunks of almost equal size </summary>
        static List<List<int>> Split(List<int> c, int n)
        {
            var chunks = new List<List<int>>();
            int start = 0;
            for (int i = 0; i < n; ++i) {
                int end = (int)((long)c.Count * (i + 1) / n);
                if (end > start) chunks.Add(c.GetRange(start, end - start));
                start = end;
            }
            return chunks;
        }

        /// <summary> the result of MinimizeBytes </summary>
        public sealed class BytesResult
        {
            public bool Reproduced;
            public byte[] Data;
            public int Evaluations, Exceptions, Timeouts;
            public bool BudgetExhausted;
            public TimeSpan Elapsed;
        }

        /// <summary>
        /// A small input for which fails is still true: for text, ddmin over lines first, then over bytes; for binary
        /// input over bytes. fails gets a fresh array on every call.
        /// </summary>
        public static BytesResult MinimizeBytes(byte[] data, Func<byte[], bool> fails, bool text, MinimizeOptions options = null)
        {
            options = options ?? new MinimizeOptions();
            var probe = new Probe<byte[]>(fails, options);
            var result = new BytesResult();
            byte[] current = data;
            if (probe.Fails((byte[])data.Clone())) {
                result.Reproduced = true;
                if (text) {
                    List<byte[]> lines = Lines(data);
                    current = Minimize(lines, s => probe.Fails(s.SelectMany(x => x).ToArray()), () => probe.Stopped, options.Seed)
                        .SelectMany(x => x).ToArray();
                }
                current = Minimize(current, s => probe.Fails(s.ToArray()), () => probe.Stopped, options.Seed).ToArray();
            }
            result.Data = current;
            result.Evaluations = probe.Evaluations;
            result.Exceptions = probe.Exceptions;
            result.Timeouts = probe.Timeouts;
            result.BudgetExhausted = probe.Stopped;
            result.Elapsed = probe.Elapsed;
            return result;
        }

        /// <summary> the lines of data, each with its '\n' </summary>
        static List<byte[]> Lines(byte[] data)
        {
            var lines = new List<byte[]>();
            int start = 0;
            for (int i = 0; i < data.Length; ++i)
                if (data[i] == (byte)'\n') { lines.Add(data.Skip(start).Take(i + 1 - start).ToArray()); start = i + 1; }
            if (start < data.Length) lines.Add(data.Skip(start).ToArray());
            return lines;
        }

        /// <summary> data as a C# expression: a string literal for printable ASCII text, else a byte array </summary>
        public static string ToCSharp(byte[] data)
        {
            bool printable = data.All(b => b == '\n' || b == '\r' || b == '\t' || (b >= 32 && b < 127));
            var sb = new StringBuilder();
            if (printable) {
                sb.Append("System.Text.Encoding.ASCII.GetBytes(\"");
                foreach (byte b in data) {
                    switch ((char)b) {
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        default: sb.Append((char)b); break;
                    }
                }
                sb.Append("\")");
            } else {
                sb.Append("new byte[] {");
                for (int i = 0; i < data.Length; ++i) {
                    if (i % 16 == 0) sb.Append("\n    ");
                    sb.Append("0x").Append(data[i].ToString("X2")).Append(i + 1 < data.Length ? ", " : "");
                }
                sb.Append("\n}");
            }
            return sb.ToString();
        }
    }
}
