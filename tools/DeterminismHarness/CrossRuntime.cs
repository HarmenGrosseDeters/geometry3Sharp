// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Cases of the cross-runtime study (docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md): the paths the other cases do
// not reach (the winding number of open inputs in the exact Boolean, whose bound calls the platform libm; the raw winding
// numbers of DMeshAABBTree3, whose terms end in Math.Atan2 and whose leaves have a Vector256 path on .NET), and probes of
// runtime behaviour that the JIT or the BCL may implement differently per machine.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace DeterminismHarness
{
    static class CrossRuntime
    {
        public static void AddTo(List<Case> list)
        {
            list.Add(new Case("boolean.open-winding", serial => BooleanOpenWinding()));
            list.Add(new Case("winding.values", serial => Winding()));
            list.Add(new Case("probe.libm-constfold", serial => ProbeConstFold()));
            list.Add(new Case("probe.linq-sum", serial => ProbeLinqSum()));
            list.Add(new Case("probe.round-remainder", serial => ProbeRoundRemainder()));
        }

        /// <summary>
        /// What the machine and the JIT offer, for the header of a run (not compared): vector widths, the instruction sets
        /// that would allow a fused multiply-add, and whether the library's Vector256 winding kernel is in use.
        /// </summary>
        public static string Machine()
        {
            var parts = new List<string>();
#if NET
            parts.Add("Vector<double>.Count=" + System.Numerics.Vector<double>.Count);
            parts.Add("Vector128=" + System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated);
            parts.Add("Vector256=" + System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated);
            parts.Add("Vector512=" + System.Runtime.Intrinsics.Vector512.IsHardwareAccelerated);
            parts.Add("x86.Fma=" + System.Runtime.Intrinsics.X86.Fma.IsSupported);
            parts.Add("x86.Avx2=" + System.Runtime.Intrinsics.X86.Avx2.IsSupported);
            parts.Add("Arm.AdvSimd=" + System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported);
#else
            parts.Add("no System.Runtime.Intrinsics (.NET Framework)");
#endif
            Type kernel = typeof(DMesh3).Assembly.GetType("g3.FastWindingLeafKernel");
            PropertyInfo available = kernel == null ? null : kernel.GetProperty("Available", BindingFlags.NonPublic | BindingFlags.Static);
            parts.Add("FastWindingLeafKernel.Available=" + (available == null ? "?" : available.GetValue(null, null).ToString()));
            parts.Add("IntPtr.Size=" + IntPtr.Size);
            return string.Join(" ", parts);
        }

        // ---------------------------------------------------------------- the open-input winding of the exact Boolean
        // A sphere with many small holes (every seventh triangle removed: more than 1000 boundary segments, so the
        // hierarchical evaluation runs) minus a smaller closed sphere through its surface: the input of
        // ExactMeshBooleanTestsAntipodalTree. ExactMeshBoolean.antipodal_tree_value calls Math.Acos, Math.Asin and Math.Cos
        // for the bound that admits a tree value.
        static Trace BooleanOpenWinding()
        {
            DMesh3 a = Inputs.Sphere(Vector3d.Zero, 1, 24);
            int n = 0;
            foreach (int tid in a.TriangleIndices().ToList())
                if (n++ % 7 == 3) a.RemoveTriangle(tid, true, false);
            DMesh3 b = Inputs.Sphere(new Vector3d(0.7, 0.35, 0.2), 0.45, 10);
            var t = new Trace();
            t.Add(a, "input-a"); t.Add(b, "input-b");
            var boolean = new ExactMeshBoolean(a, b, Ops.Difference);
            // (internal: the winding number of every open piece, the value the libm-dependent bound guards)
            FieldInfo record = typeof(ExactMeshBoolean).GetField("RecordOpenWinding", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo perPiece = typeof(ExactMeshBoolean).GetField("OpenWindingPerPiece", BindingFlags.NonPublic | BindingFlags.Instance);
            if (record != null) record.SetValue(boolean, true);
            t.Section("status"); t.Add(boolean.Compute()); t.Add((int)boolean.Status);
            if (boolean.Result != null) t.Add(boolean.Result);
            var windings = perPiece == null ? null : perPiece.GetValue(boolean) as int[][];
            t.Section("open-winding");
            if (windings == null) t.Add(-1);
            else foreach (int[] piece in windings) { t.Add(piece == null ? -1 : piece.Length); if (piece != null) foreach (int w in piece) t.Add(w); }
            return t;
        }

        // ---------------------------------------------------------------- winding numbers
        // The raw values (sections "fast" and "exact-sum") end in Math.Atan2 per triangle, so they are expected to differ
        // where the platform's atan2 does; "inside" (the value compared with 0.5) is what the operations consume.
        static Trace Winding()
        {
            DMesh3 mesh = Inputs.BumpySphere(12);
            var tree = new DMeshAABBTree3(mesh, true);
            var rng = new Rng(4711);
            var points = new List<Vector3d>();
            for (int i = 0; i < 300; ++i) points.Add(new Vector3d(rng.Range(-1.5, 1.5), rng.Range(-1.5, 1.5), rng.Range(-1.5, 1.5)));
            // (and points next to the surface: a vertex moved by 2^-10 and 2^-30 of its length, inwards and outwards)
            foreach (int vid in mesh.VertexIndices().Take(100)) {
                Vector3d v = mesh.GetVertex(vid);
                foreach (double f in new[] { 1 - 1.0 / 1024, 1 + 1.0 / 1024, 1 - 1.0 / 1073741824, 1 + 1.0 / 1073741824 }) points.Add(v * f);
            }
            var t = new Trace();
            t.Add(mesh, "input");
            tree.FastWindingNumber(Vector3d.Zero);          // (builds the cache)
            foreach (Vector3d p in points) {
                double fast = tree.FastWindingNumber(p), exact = tree.WindingNumber(p);
                t.Section("fast"); t.Add(fast);
                t.Section("exact-sum"); t.Add(exact);
                t.Section("inside"); t.Add(fast > 0.5); t.Add(exact > 0.5);
            }
            return t;
        }

        // ---------------------------------------------------------------- probes
        [MethodImpl(MethodImplOptions.NoInlining)]
        static double Opaque(double x) { return x + opaqueZero; }
        static double opaqueZero = 0;

        // A JIT may evaluate a transcendental function of a constant at compile time, with its own library, and the call at
        // run time with the platform's: the same expression could then give two values in one process (and change with the
        // tier). The pairs (the call on a literal, the same call on a value the JIT cannot see) are computed twice: in a
        // method compiled with full optimization at once (AggressiveOptimization, which skips the tiers; the body is inlined
        // into it), where the JIT folds what it can, and in one compiled without optimization. "equal" is 1 where the
        // two values of a pair agree; "optimized-equals-unoptimized" compares the two methods value by value.
        static Trace ProbeConstFold()
        {
            var optimized = new List<double>(); var plain = new List<double>();
            FoldOptimized(optimized); FoldUnoptimized(plain);
            var t = new Trace();
            foreach (List<double> values in new[] { optimized, plain })
                for (int i = 0; i + 1 < values.Count; i += 2) {
                    t.Section("value"); t.Add(values[i]); t.Add(values[i + 1]);
                    t.Section("equal"); t.Add(BitConverter.DoubleToInt64Bits(values[i]) == BitConverter.DoubleToInt64Bits(values[i + 1]));
                }
            t.Section("optimized-equals-unoptimized");
            for (int i = 0; i < optimized.Count; ++i) t.Add(BitConverter.DoubleToInt64Bits(optimized[i]) == BitConverter.DoubleToInt64Bits(plain[i]));
            return t;
        }

        // ((MethodImplOptions)512 is AggressiveOptimization, which the .NET Framework reference assemblies do not name; the
        // .NET Framework JIT ignores it and optimizes as always)
        [MethodImpl((MethodImplOptions)512 | MethodImplOptions.NoInlining)]
        static void FoldOptimized(List<double> v) { FoldBody(v); }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        static void FoldUnoptimized(List<double> v)
        {
            v.Add(Math.Sin(0.7853981633974483)); v.Add(Math.Sin(Opaque(0.7853981633974483)));
            v.Add(Math.Cos(1e22)); v.Add(Math.Cos(Opaque(1e22)));
            v.Add(Math.Tan(1.5707963267948966)); v.Add(Math.Tan(Opaque(1.5707963267948966)));
            v.Add(Math.Exp(0.6931471805599453)); v.Add(Math.Exp(Opaque(0.6931471805599453)));
            v.Add(Math.Log(3.0)); v.Add(Math.Log(Opaque(3.0)));
            v.Add(Math.Pow(10.0, -5.0)); v.Add(Math.Pow(Opaque(10.0), Opaque(-5.0)));
            v.Add(Math.Pow(2.0, 0.5)); v.Add(Math.Pow(Opaque(2.0), Opaque(0.5)));
            v.Add(Math.Atan2(1.0, 3.0)); v.Add(Math.Atan2(Opaque(1.0), Opaque(3.0)));
            v.Add(Math.Acos(0.3)); v.Add(Math.Acos(Opaque(0.3)));
            v.Add(Math.Asin(0.3)); v.Add(Math.Asin(Opaque(0.3)));
            v.Add(Math.Sinh(2.5)); v.Add(Math.Sinh(Opaque(2.5)));
            v.Add(Math.Cosh(2.5)); v.Add(Math.Cosh(Opaque(2.5)));
            v.Add(Math.Log10(7.0)); v.Add(Math.Log10(Opaque(7.0)));
            v.Add(Math.Sqrt(2.0)); v.Add(Math.Sqrt(Opaque(2.0)));
            v.Add(1.0 / 3.0 * 3.0 - 1.0); v.Add(Opaque(1.0) / Opaque(3.0) * Opaque(3.0) - Opaque(1.0));
        }

        // (the same pairs, in the same order, as FoldUnoptimized)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void FoldBody(List<double> v)
        {
            v.Add(Math.Sin(0.7853981633974483)); v.Add(Math.Sin(Opaque(0.7853981633974483)));
            v.Add(Math.Cos(1e22)); v.Add(Math.Cos(Opaque(1e22)));
            v.Add(Math.Tan(1.5707963267948966)); v.Add(Math.Tan(Opaque(1.5707963267948966)));
            v.Add(Math.Exp(0.6931471805599453)); v.Add(Math.Exp(Opaque(0.6931471805599453)));
            v.Add(Math.Log(3.0)); v.Add(Math.Log(Opaque(3.0)));
            v.Add(Math.Pow(10.0, -5.0)); v.Add(Math.Pow(Opaque(10.0), Opaque(-5.0)));
            v.Add(Math.Pow(2.0, 0.5)); v.Add(Math.Pow(Opaque(2.0), Opaque(0.5)));
            v.Add(Math.Atan2(1.0, 3.0)); v.Add(Math.Atan2(Opaque(1.0), Opaque(3.0)));
            v.Add(Math.Acos(0.3)); v.Add(Math.Acos(Opaque(0.3)));
            v.Add(Math.Asin(0.3)); v.Add(Math.Asin(Opaque(0.3)));
            v.Add(Math.Sinh(2.5)); v.Add(Math.Sinh(Opaque(2.5)));
            v.Add(Math.Cosh(2.5)); v.Add(Math.Cosh(Opaque(2.5)));
            v.Add(Math.Log10(7.0)); v.Add(Math.Log10(Opaque(7.0)));
            v.Add(Math.Sqrt(2.0)); v.Add(Math.Sqrt(Opaque(2.0)));
            v.Add(1.0 / 3.0 * 3.0 - 1.0); v.Add(Opaque(1.0) / Opaque(3.0) * Opaque(3.0) - Opaque(1.0));
        }

        // LINQ's Sum / Average / Min / Max of doubles against the plain loop: a BCL that vectorizes them would add in another
        // order (a different sum on a machine with another vector width).
        static Trace ProbeLinqSum()
        {
            var t = new Trace();
            var rng = new Rng(99);
            foreach (int n in new[] { 3, 16, 17, 100, 1000, 4099 }) {
                var xs = new double[n];
                for (int i = 0; i < n; ++i) xs[i] = rng.Range(-1, 1) * Math.Sqrt(rng.Range(1e-12, 1e12));
                double loop = 0; foreach (double x in xs) loop += x;
                var list = new List<double>(xs);
                t.Section("sum"); t.Add(loop); t.Add(xs.Sum()); t.Add(list.Sum()); t.Add(xs.Select(x => x).Sum()); t.Add(xs.Aggregate(0.0, (a, b) => a + b));
                t.Section("sum-equals-loop"); t.Add(BitConverter.DoubleToInt64Bits(loop) == BitConverter.DoubleToInt64Bits(xs.Sum()));
                t.Section("average"); t.Add(xs.Average()); t.Add(list.Average());
                t.Section("minmax"); t.Add(xs.Min()); t.Add(xs.Max()); t.Add(list.Min()); t.Add(list.Max());
            }
            return t;
        }

        // Math.Round with digits (rewritten in .NET Core 3.0), the midpoint modes, the remainder operator and IEEERemainder
        // (exact by definition, computed by the C runtime), Truncate / Floor / Ceiling.
        static Trace ProbeRoundRemainder()
        {
            var t = new Trace();
            var rng = new Rng(7);
            var xs = new List<double> { 0.5, 1.5, 2.5, -0.5, -1.5, 2.675, 1.005, 0.125, 0.285, 1e15 + 0.5, 4503599627370497.5, 5e-324, 1.0000000000000002, 123456.789, -0.0 };
            for (int i = 0; i < 2000; ++i) xs.Add(rng.Range(-1000, 1000));
            for (int i = 0; i < 500; ++i) xs.Add(Math.Floor(rng.Range(-100000, 100000)) / 1000 + 0.0005);
            foreach (double x in xs) {
                t.Section("round"); t.Add(Math.Round(x)); t.Add(Math.Round(x, MidpointRounding.AwayFromZero));
                t.Section("round-digits"); t.Add(Math.Round(x, 2)); t.Add(Math.Round(x, 3)); t.Add(Math.Round(x, 3, MidpointRounding.AwayFromZero)); t.Add(Math.Round(x, 10));
                t.Section("floor-ceiling-truncate"); t.Add(Math.Floor(x)); t.Add(Math.Ceiling(x)); t.Add(Math.Truncate(x));
            }
            for (int i = 0; i < 2000; ++i) {
                double a = rng.Range(-1e6, 1e6), b = rng.Range(-1e3, 1e3) * (i % 3 == 0 ? 1e-9 : 1);
                if (b == 0) b = 1;
                t.Section("remainder"); t.Add(a % b); t.Add(Math.IEEERemainder(a, b));
            }
            return t;
        }
    }
}
