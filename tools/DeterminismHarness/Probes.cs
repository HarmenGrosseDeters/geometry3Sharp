// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Probes of runtime behaviour that the specification leaves open or that changed between .NET Framework and .NET, and
// cases for the candidates of docs/design/DETERMINISM_PROOF.md section 5. No library code except the terrain cases.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using g3;
using gs;

namespace DeterminismHarness
{
    static class Probes
    {
        public static void AddTo(List<Case> list)
        {
            list.Add(new Case("probe.minmax-zero", serial => MinMax()));
            list.Add(new Case("probe.cast-range", serial => Casts()));
            list.Add(new Case("probe.random-seeded", serial => SeededRandom()));
            list.Add(new Case("probe.hash-values", serial => Hashes()));
            list.Add(new Case("probe.hash-seeded", serial => SeededHashes()));
            list.Add(new Case("probe.hash-enumeration", serial => HashEnumeration()));
            list.Add(new Case("probe.sort-ties-large", serial => SortTiesLarge()));
            list.Add(new Case("probe.slope-degrees", serial => SlopeDegrees()));
            list.Add(new Case("terrain.slope-design", serial => SlopeDesign(serial, false)));
            list.Add(new Case("terrain.slope-design.degrees", serial => SlopeDesign(serial, true)));
        }

        // (the values come from an array through a call that is not inlined, so that neither the compiler nor the JIT folds
        // the operation under test)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static double At(double[] a, int i) { return a[i]; }

        // Math.Min / Max of doubles: the result for (+0, -0) and for a NaN operand (changed in .NET Core 3.0)
        static Trace MinMax()
        {
            var t = new Trace();
            double[] v = { 0.0, -0.0, 1.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -double.Epsilon };
            for (int i = 0; i < v.Length; ++i)
                for (int j = 0; j < v.Length; ++j) {
                    double a = At(v, i), b = At(v, j);
                    t.Section("min"); t.Add(Math.Min(a, b));
                    t.Section("max"); t.Add(Math.Max(a, b));
                    // the forms the library writes by hand
                    t.Section("ternary-min"); t.Add(a < b ? a : b);
                    t.Section("ternary-max"); t.Add(a > b ? a : b);
                }
            t.Notes.Add("Math.Min(+0, -0) = 0x" + BitConverter.DoubleToInt64Bits(Math.Min(At(v, 0), At(v, 1))).ToString("x16")
                + ", Math.Min(-0, +0) = 0x" + BitConverter.DoubleToInt64Bits(Math.Min(At(v, 1), At(v, 0))).ToString("x16")
                + ", Math.Max(-0, +0) = 0x" + BitConverter.DoubleToInt64Bits(Math.Max(At(v, 1), At(v, 0))).ToString("x16")
                + ", Math.Max(+0, -0) = 0x" + BitConverter.DoubleToInt64Bits(Math.Max(At(v, 0), At(v, 1))).ToString("x16"));
            return t;
        }

        // double -> integer conversions of NaN and of values out of range (unspecified in C#; saturating since .NET 9)
        static Trace Casts()
        {
            var t = new Trace();
            double[] v = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2147483647.0, 2147483648.0, -2147483648.0, -2147483649.0,
                3e9, -3e9, 4294967295.0, 4294967296.0, 1e10, -1e10, 9223372036854775807.0, -9223372036854775808.0, 1e19, -1e19, 1.8446744073709552e19,
                1e30, -1e30, -1.0, -0.5, 0.5, 2147483647.5, -2147483648.5 };
            for (int i = 0; i < v.Length; ++i) {
                double d = At(v, i);
                unchecked {
                    t.Section("int"); t.Add((long)(int)d);
                    t.Section("uint"); t.Add((long)(uint)d);
                    t.Section("long"); t.Add((long)d);
                    t.Section("ulong"); t.Add((long)(ulong)d);
                    t.Section("short"); t.Add((long)(short)d);
                    t.Section("byte"); t.Add((long)(byte)d);
                }
            }
            unchecked {
                t.Notes.Add("(int)NaN = " + (int)At(v, 0) + ", (int)1e10 = " + (int)At(v, 11) + ", (int)-1e10 = " + (int)At(v, 12)
                    + ", (long)1e19 = " + (long)At(v, 15) + ", (uint)-1.0 = " + (uint)At(v, 20));
            }
            return t;
        }

        // System.Random with a seed: the sequence is not promised across versions
        static Trace SeededRandom()
        {
            var t = new Trace();
            foreach (int seed in new[] { 1, 12345, -7, int.MaxValue }) {
                var r = new Random(seed);
                t.Section("next"); for (int i = 0; i < 32; ++i) t.Add(r.Next());
                t.Section("next-max"); for (int i = 0; i < 32; ++i) t.Add(r.Next(i + 1));
                t.Section("next-range"); for (int i = 0; i < 32; ++i) t.Add(r.Next(-1000, 1000000));
                t.Section("next-double"); for (int i = 0; i < 32; ++i) t.Add(r.NextDouble());
                byte[] bytes = new byte[16]; r.NextBytes(bytes);
                t.Section("next-bytes"); foreach (byte b in bytes) t.Add((int)b);
            }
            return t;
        }

        // GetHashCode of key types whose value is a function of the key on every runtime measured
        static Trace Hashes()
        {
            var t = new Trace();
            var rng = new Rng(21);
            t.Section("double");
            foreach (double d in new[] { 0.0, -0.0, 1.0, -1.5, 1e300, double.Epsilon, double.NaN, double.PositiveInfinity })
                t.Add(d.GetHashCode());
            t.Section("long"); for (int i = 0; i < 16; ++i) t.Add(((long)rng.Next()).GetHashCode());
            t.Section("int"); for (int i = 0; i < 16; ++i) t.Add(((int)rng.Next()).GetHashCode());
            t.Section("vector3d"); for (int i = 0; i < 16; ++i) t.Add(new Vector3d(rng.Double(), rng.Double(), rng.Double()).GetHashCode());
            t.Section("index3i"); for (int i = 0; i < 16; ++i) t.Add(new Index3i((int)(rng.Next() % 1000), (int)(rng.Next() % 1000), (int)(rng.Next() % 1000)).GetHashCode());
            return t;
        }

        // GetHashCode of key types that .NET seeds per process: BigInteger (so ExactPoint3, which combines four of them),
        // ValueTuple (HashCode.Combine), string. EXPECTED TO DIFFER between any two runs on .NET, also on one machine; on .NET
        // Framework they are functions of the key. A result must not depend on these values
        static Trace SeededHashes()
        {
            var t = new Trace();
            var rng = new Rng(21);
            t.Section("biginteger");
            BigInteger big = BigInteger.One;
            for (int i = 0; i < 64; ++i) {
                big = big * new BigInteger((long)(rng.Next() >> 1)) + i;
                t.Add(big.GetHashCode()); t.Add((-big).GetHashCode());
            }
            foreach (long x in new long[] { 0, 1, -1, 255, 65536, int.MaxValue, long.MaxValue, long.MinValue })
                t.Add(new BigInteger(x).GetHashCode());
            t.Section("valuetuple"); t.Add((1, 2).GetHashCode()); t.Add((1L, 2L).GetHashCode()); t.Add((0.5, -3).GetHashCode());
            t.Section("tuple"); t.Add(Tuple.Create(1, 2).GetHashCode());
            t.Section("string"); t.Add("abc".GetHashCode()); t.Add("k123".GetHashCode());
            t.Notes.Add("new BigInteger(255).GetHashCode() = " + new BigInteger(255).GetHashCode() + ", (1, 2).GetHashCode() = " + (1, 2).GetHashCode()
                + ", \"abc\".GetHashCode() = " + "abc".GetHashCode());
            return t;
        }

        // the enumeration order of Dictionary and HashSet: after inserts only, after removals, after refills, after set operations
        static Trace HashEnumeration()
        {
            var t = new Trace();
            var rng = new Rng(33);
            foreach (int n in new[] { 5, 40, 300, 5000 }) {
                var keys = new List<long>();
                for (int i = 0; i < n; ++i) keys.Add((long)(rng.Next() % (ulong)(4 * n)));
                var d = new Dictionary<long, int>();
                var h = new HashSet<long>();
                var dt = new Dictionary<(int, int), int>();     // (a key whose hash is seeded per process on .NET)
                var ds = new Dictionary<string, int>();
                for (int i = 0; i < n; ++i) {
                    d[keys[i]] = i; h.Add(keys[i]);
                    dt[((int)keys[i], i % 3)] = i; ds["k" + keys[i]] = i;
                }
                t.Section("dictionary-inserted-" + n); foreach (var kv in d) { t.Add(kv.Key); t.Add(kv.Value); }
                t.Section("dictionary-values-" + n); foreach (int v in d.Values) t.Add(v);
                t.Section("hashset-inserted-" + n); foreach (long k in h) t.Add(k);
                t.Section("tuple-keys-inserted-" + n); foreach (var kv in dt) t.Add(kv.Value);
                t.Section("string-keys-inserted-" + n); foreach (var kv in ds) t.Add(kv.Value);
                // remove every third key, then enumerate, then add new keys (they take the freed slots)
                for (int i = 0; i < n; i += 3) { d.Remove(keys[i]); h.Remove(keys[i]); dt.Remove(((int)keys[i], i % 3)); ds.Remove("k" + keys[i]); }
                t.Section("dictionary-removed-" + n); foreach (var kv in d) t.Add(kv.Key);
                t.Section("hashset-removed-" + n); foreach (long k in h) t.Add(k);
                for (int i = 0; i < n / 2; ++i) {
                    long k = 4 * n + (long)(rng.Next() % (ulong)n);
                    d[k] = -i; h.Add(k); dt[((int)k, 7)] = -i; ds["n" + k] = -i;
                }
                t.Section("dictionary-refilled-" + n); foreach (var kv in d) t.Add(kv.Key);
                t.Section("hashset-refilled-" + n); foreach (long k in h) t.Add(k);
                t.Section("tuple-keys-refilled-" + n); foreach (var kv in dt) t.Add(kv.Value);
                t.Section("string-keys-refilled-" + n); foreach (var kv in ds) t.Add(kv.Value);
                // set operations and emptying
                var h2 = new HashSet<long>(keys.Take(n / 2));
                h2.UnionWith(keys.Skip(n / 4)); h2.ExceptWith(keys.Where((k, i) => i % 5 == 0)); h2.RemoveWhere(k => k % 7 == 0);
                t.Section("hashset-setops-" + n); foreach (long k in h2) t.Add(k);
                h2.TrimExcess();
                t.Section("hashset-trimmed-" + n); foreach (long k in h2) t.Add(k);
                foreach (long k in h2.ToList()) h2.Remove(k);
                for (int i = 0; i < n / 3; ++i) h2.Add(keys[i]);
                t.Section("hashset-emptied-refilled-" + n); foreach (long k in h2) t.Add(k);
                d.Clear(); for (int i = n - 1; i >= 0; --i) d[keys[i]] = i;
                t.Section("dictionary-cleared-refilled-" + n); foreach (var kv in d) t.Add(kv.Key);
                t.Section("linq-distinct-" + n); foreach (long k in keys.Distinct()) t.Add(k);
                t.Section("linq-groupby-" + n); foreach (var g in keys.GroupBy(k => k % 11)) { t.Add(g.Key); t.Add(g.Count()); }
            }
            return t;
        }

        // the order of equal keys after an unstable sort, at the sizes where the runtimes' algorithms switch (insertion sort, heap
        // sort, introsort depth), for the overloads the library uses
        static Trace SortTiesLarge()
        {
            var t = new Trace();
            var rng = new Rng(55);
            foreach (int n in new[] { 16, 17, 32, 33, 64, 65, 1000, 20000 })
                foreach (int distinct in new[] { 2, 7, n / 4 + 1 }) {
                    int[] keys = new int[n], ids = new int[n];
                    double[] dkeys = new double[n]; long[] lkeys = new long[n];
                    for (int i = 0; i < n; ++i) {
                        keys[i] = (int)(rng.Next() % (ulong)distinct); ids[i] = i;
                        dkeys[i] = keys[i] * 0.5 - 1; lkeys[i] = keys[i] * 1000003L;
                    }
                    string tag = "-" + n + "-" + distinct;
                    int[] i1 = (int[])ids.Clone();
                    Array.Sort((int[])keys.Clone(), i1);
                    t.Section("int-keys-items" + tag); foreach (int x in i1) t.Add(x);
                    i1 = (int[])ids.Clone();
                    Array.Sort((double[])dkeys.Clone(), i1);
                    t.Section("double-keys-items" + tag); foreach (int x in i1) t.Add(x);
                    i1 = (int[])ids.Clone();
                    Array.Sort((long[])lkeys.Clone(), i1);
                    t.Section("long-keys-items" + tag); foreach (int x in i1) t.Add(x);
                    i1 = (int[])ids.Clone();
                    Array.Sort((long[])lkeys.Clone(), i1, n / 3, n - n / 3);
                    t.Section("long-keys-items-range" + tag); foreach (int x in i1) t.Add(x);
                    var list = new List<int>(ids);
                    list.Sort((x, y) => keys[x].CompareTo(keys[y]));
                    t.Section("list-comparison" + tag); foreach (int x in list) t.Add(x);
                    i1 = (int[])ids.Clone();
                    Array.Sort(i1, Comparer<int>.Create((x, y) => dkeys[x].CompareTo(dkeys[y])));
                    t.Section("array-comparer" + tag); foreach (int x in i1) t.Add(x);
                    // BinarySearch among equal elements: which of them
                    int[] sorted = (int[])keys.Clone(); Array.Sort(sorted);
                    t.Section("binary-search" + tag);
                    for (int k = 0; k < Math.Min(distinct, 8); ++k) { t.Add(Array.BinarySearch(sorted, k)); t.Add(new List<int>(sorted).BinarySearch(k)); }
                }
            return t;
        }

        // SlopeSpec.Degrees: S is tan of the angle by the platform's libm (TerrainSlope.cs, in the exact scope)
        static Trace SlopeDegrees()
        {
            var t = new Trace();
            for (int k = 1; k < 4096; ++k) {
                double a = 90.0 * k / 4096;
                t.Add(SlopeSpec.Degrees(a).S);
            }
            t.Notes.Add("SlopeSpec.Degrees(33.7).S = 0x" + BitConverter.DoubleToInt64Bits(SlopeSpec.Degrees(33.7).S).ToString("x16"));
            return t;
        }

        // a pit with a constant slope in a tilted, slightly warped terrain (only + - * / in the input). With a Ratio slope the
        // input is the same on every platform, and the result shows whether the libm calls inside TerrainSlope* reach it; with
        // Degrees the slope itself is a libm value
        static Trace SlopeDesign(bool serial, bool degrees)
        {
            var terrain = new DMesh3();
            int nx = 23, ny = 25;
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    double x = -60 + 120.0 * i / nx, y = -60 + 120.0 * j / ny;
                    terrain.AppendVertex(new Vector3d(x, y, 0.15 * (0.8 * x + 0.6 * y) + 0.3 + 0.0004 * x * y - 0.0002 * y * y));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
                    if ((i + j) % 2 == 0) { terrain.AppendTriangle(a, b, c); terrain.AppendTriangle(a, c, d); }
                    else { terrain.AppendTriangle(a, b, d); terrain.AppendTriangle(b, c, d); }
                }
            // an irregular 7-gon, counter-clockwise, at z 0
            var toePoints = new List<Vector3d>() {
                new Vector3d(11, 1, 0), new Vector3d(7, 8.5, 0), new Vector3d(-2, 10.75, 0), new Vector3d(-10, 5, 0),
                new Vector3d(-9.5, -5.25, 0), new Vector3d(-2.5, -11, 0), new Vector3d(6.75, -8, 0) };
            SlopeSpec slope = degrees ? SlopeSpec.Degrees(33.7) : SlopeSpec.Ratio(1.5);
            var design = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(toePoints, true, SlopeToe.Sides.Outside, slope) }, Terrain = terrain, Tolerance = 0.05,
                BuildComposite = true, Direction = TerrainSlopeDesign.Directions.Cut
            };
            var t = new Trace();
            t.Add(terrain, "input");
            t.Section("status");
            t.Add(design.Compute()); t.Add((int)design.Status);
            if (design.SlopeSurface != null) t.Add(design.SlopeSurface, "slope-surface");
            if (design.Composite != null) t.Add(design.Composite, "composite");
            t.Notes.Add("status " + design.Status + (design.Composite != null ? ", composite " + design.Composite.TriangleCount + " triangles" : ""));
            return t;
        }
    }
}
