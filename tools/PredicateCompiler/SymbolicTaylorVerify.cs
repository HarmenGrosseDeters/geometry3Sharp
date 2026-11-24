// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Offline Symbolic Taylor round-off error verifier for PredicateCompiler's floating-point filters (Solovyev et al.,
// "Rigorous Estimation of Floating-Point Round-Off Errors with Symbolic Taylor Expansions", ACM TOPLAS 40(4), 2018).
//
// Operates directly on PredicateCompiler's parsed Predicate / Node DAG with zero external dependencies:
//   - Directed-rounding IEEE-754 binary64 interval arithmetic (Interval64, via Math.BitDecrement / Math.BitIncrement).
//   - Exact IEEE-754 binary64 binade local rounding bound HalfUlpMax(m) (handling exact powers of two and subnormals).
//   - Reverse-mode interval sensitivities VBar[k] enclosing d(f)/d(v_k) over each box.
//   - Exact algebraic telescoping identity on +, -, * DAGs:
//       v~_root - v_root = sum_{k not Input} vBar_k * l_k + sum_{k in Mul} vBar_k * (err_{a_k} * err_{b_k}),
//     where l_k = fl(s~_k) - s~_k is bounded by HalfUlpMax(Up(|v_k| + preRoundErr_k)) and err_k = v~_k - v_k.
//   - Best-first branch-and-bound box subdivision to tighten first-order symbolic Taylor bounds.
//   - Exact rational (BigInteger) spot-checks on corners and interior points of each box.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace G3PredicateCompiler
{
    /// <summary> Outwardly rounded IEEE-754 binary64 interval [Lo, Hi]. </summary>
    public readonly struct Interval64
    {
        public readonly double Lo, Hi;

        public Interval64(double lo, double hi)
        {
            if (double.IsNaN(lo) || double.IsNaN(hi)) {
                Lo = double.NegativeInfinity;
                Hi = double.PositiveInfinity;
            } else {
                Lo = lo;
                Hi = hi;
            }
        }

        public static Interval64 Point(double v) => new Interval64(v, v);

        public double MaxAbs => Math.Max(Math.Abs(Lo), Math.Abs(Hi));
        public bool IsFinite => double.IsFinite(Lo) && double.IsFinite(Hi);
        public bool IsZero => Lo == 0.0 && Hi == 0.0;

        public static double Down(double x)
        {
            if (double.IsNaN(x)) return double.NegativeInfinity;
            if (x == double.NegativeInfinity) return x;
            return Math.BitDecrement(x);
        }

        public static double Up(double x)
        {
            if (double.IsNaN(x)) return double.PositiveInfinity;
            if (x == double.PositiveInfinity) return x;
            return Math.BitIncrement(x);
        }

        // Exact IEEE-754 directed-rounding addition via Knuth's TwoSum (exact even in subnormals; falls back near overflow).
        public static double AddDown(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NegativeInfinity;
            if (a == 0.0) return b;
            if (b == 0.0) return a;
            double s = a + b;
            if (!double.IsFinite(s)) return double.IsNaN(s) ? double.NegativeInfinity : s;
            if (Math.Abs(a) <= 1e300 && Math.Abs(b) <= 1e300) {
                double bv = s - a;
                double err = (a - (s - bv)) + (b - bv);
                return err < 0.0 ? Math.BitDecrement(s) : s;
            }
            return Math.BitDecrement(s);
        }

        public static double AddUp(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.PositiveInfinity;
            if (a == 0.0) return b;
            if (b == 0.0) return a;
            double s = a + b;
            if (!double.IsFinite(s)) return double.IsNaN(s) ? double.PositiveInfinity : s;
            if (Math.Abs(a) <= 1e300 && Math.Abs(b) <= 1e300) {
                double bv = s - a;
                double err = (a - (s - bv)) + (b - bv);
                return err > 0.0 ? Math.BitIncrement(s) : s;
            }
            return Math.BitIncrement(s);
        }

        public static double SubDown(double a, double b) => AddDown(a, -b);
        public static double SubUp(double a, double b) => AddUp(a, -b);

        // Exact IEEE-754 directed-rounding multiplication via Dekker's TwoProd within [1e-140, 1e140], else BitDecrement/BitIncrement.
        static void Split(double a, out double hi, out double lo)
        {
            double c = 134217729.0 * a; // (2^27 + 1) * a
            double big = c - a;
            hi = c - big;
            lo = a - hi;
        }

        public static double MulDown(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NegativeInfinity;
            double p = a * b;
            if (!double.IsFinite(p)) return double.IsNaN(p) ? double.NegativeInfinity : p;
            if (a == 0.0 || b == 0.0) return 0.0;
            double aa = Math.Abs(a), ab = Math.Abs(b);
            if (aa >= 1e-140 && aa <= 1e140 && ab >= 1e-140 && ab <= 1e140) {
                Split(a, out double ah, out double al);
                Split(b, out double bh, out double bl);
                double err = ((ah * bh - p) + ah * bl + al * bh) + al * bl;
                return err < 0.0 ? Math.BitDecrement(p) : p;
            }
            return Math.BitDecrement(p);
        }

        public static double MulUp(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.PositiveInfinity;
            double p = a * b;
            if (!double.IsFinite(p)) return double.IsNaN(p) ? double.PositiveInfinity : p;
            if (a == 0.0 || b == 0.0) return 0.0;
            double aa = Math.Abs(a), ab = Math.Abs(b);
            if (aa >= 1e-140 && aa <= 1e140 && ab >= 1e-140 && ab <= 1e140) {
                Split(a, out double ah, out double al);
                Split(b, out double bh, out double bl);
                double err = ((ah * bh - p) + ah * bl + al * bh) + al * bl;
                return err > 0.0 ? Math.BitIncrement(p) : p;
            }
            return Math.BitIncrement(p);
        }

        public static Interval64 operator +(Interval64 a, Interval64 b) =>
            new Interval64(AddDown(a.Lo, b.Lo), AddUp(a.Hi, b.Hi));

        public static Interval64 operator -(Interval64 a, Interval64 b) =>
            new Interval64(SubDown(a.Lo, b.Hi), SubUp(a.Hi, b.Lo));

        public static Interval64 operator *(Interval64 a, Interval64 b)
        {
            if (a.IsZero || b.IsZero) {
                if (a.IsFinite && b.IsFinite) return Point(0.0);
                return new Interval64(double.NegativeInfinity, double.PositiveInfinity);
            }
            double lo = Math.Min(Math.Min(MulDown(a.Lo, b.Lo), MulDown(a.Lo, b.Hi)),
                                 Math.Min(MulDown(a.Hi, b.Lo), MulDown(a.Hi, b.Hi)));
            double hi = Math.Max(Math.Max(MulUp(a.Lo, b.Lo), MulUp(a.Lo, b.Hi)),
                                 Math.Max(MulUp(a.Hi, b.Lo), MulUp(a.Hi, b.Hi)));
            return new Interval64(lo, hi);
        }
    }

    public sealed class FilterVerificationRow
    {
        public string PredicateName;
        public string NodeLabel;
        public string DomainLabel;
        public int Degree;
        public int OpCount;
        public double SymbolicTaylorBound;
        public double FirstOrderBound;
        public double SecondOrderBound;
        public double GeneratorBound;
        public double Ratio;                // GeneratorBound / SymbolicTaylorBound (>= 1 means generator dominates)
        public double MaxSampledExactErr;
        public int BoxesEvaluated;
        public double ElapsedMs;
        public bool Dominates;              // SymbolicTaylorBound <= GeneratorBound (finite)
        public bool SamplesValid;           // every exact rational spot-check stayed within both bounds
        public bool Passed => Dominates && SamplesValid;
    }

    public sealed class FilterVerificationReport
    {
        public List<FilterVerificationRow> Rows = new List<FilterVerificationRow>();
        public double TotalElapsedMs;
        public bool AllPassed => Rows.Count > 0 && Rows.All(r => r.Passed);
    }

    public static class SymbolicTaylorVerify
    {
        /// <summary>
        /// Exact upper bound on |fl(z) - z| in IEEE-754 binary64 round-to-nearest for any real z with |z| &lt;= m.
        /// For m = 2^e (exact power of two, e &gt;= -1021), |z| = 2^e is exact and |z| &lt; 2^e lies in [0, 2^e),
        /// so the largest binade reached is [2^(e-1), 2^e) with half-ulp 2^(e - 54). For 2^e &lt; m &lt; 2^(e+1),
        /// half-ulp is 2^(e - 53). For 0 &lt; m &lt;= 2^-1021, returns double.Epsilon (2^-1074 &gt; 2^-1075).
        /// </summary>
        public static double HalfUlpMax(double m)
        {
            if (double.IsNaN(m) || double.IsInfinity(m)) return double.PositiveInfinity;
            if (m <= 0.0) return 0.0;
            long bits = BitConverter.DoubleToInt64Bits(m);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0 || (exp == 1 && mant == 0))
                return double.Epsilon;
            int halfExp = mant == 0 ? exp - 1077 : exp - 1076;
            if (halfExp < -1074)
                return double.Epsilon;
            return Math.ScaleB(1.0, halfExp);
        }

        // Sub-DAG restricted to the ancestors of a target node, indexed 0..N-1 in topological order.
        sealed class SubDag
        {
            public Node[] Nodes;
            public int[] A;
            public int[] B;
            public int[] InputSubIds;
            public int Root;
            public int OpCount;

            public static SubDag Extract(Predicate p, Node target)
            {
                var reachable = new HashSet<int>();
                var stack = new Stack<Node>();
                stack.Push(target);
                while (stack.Count > 0) {
                    var cur = stack.Pop();
                    if (!reachable.Add(cur.Id)) continue;
                    if (cur.Op != Op.Input) {
                        stack.Push(cur.A);
                        stack.Push(cur.B);
                    }
                }
                var list = p.Nodes.Where(n => reachable.Contains(n.Id)).ToList();
                var map = new Dictionary<int, int>(list.Count);
                for (int i = 0; i < list.Count; ++i) map[list[i].Id] = i;

                int n = list.Count;
                var a = new int[n];
                var b = new int[n];
                var inputs = new List<int>();
                int ops = 0;
                for (int i = 0; i < n; ++i) {
                    var nd = list[i];
                    if (nd.Op == Op.Input) {
                        a[i] = inputs.Count;
                        b[i] = -1;
                        inputs.Add(i);
                    } else {
                        a[i] = map[nd.A.Id];
                        b[i] = map[nd.B.Id];
                        ++ops;
                    }
                }
                return new SubDag {
                    Nodes = list.ToArray(),
                    A = a,
                    B = b,
                    InputSubIds = inputs.ToArray(),
                    Root = map[target.Id],
                    OpCount = ops
                };
            }
        }

        readonly struct BoxEval
        {
            public readonly double Total, First, Second;
            public readonly int SplitDim;
            public BoxEval(double total, double first, double second, int splitDim)
            {
                Total = total; First = first; Second = second; SplitDim = splitDim;
            }
        }

        static BoxEval EvaluateBox(SubDag dag, Interval64[] inputBox)
        {
            int n = dag.Nodes.Length;
            var V = new Interval64[n];
            var preRoundErr = new double[n];
            var fwdErr = new double[n];

            for (int i = 0; i < n; ++i) {
                var nd = dag.Nodes[i];
                switch (nd.Op) {
                    case Op.Input:
                        V[i] = inputBox[dag.A[i]];
                        preRoundErr[i] = 0.0;
                        fwdErr[i] = 0.0;
                        break;
                    case Op.Add:
                    case Op.Sub: {
                        int ia = dag.A[i], ib = dag.B[i];
                        V[i] = nd.Op == Op.Add ? V[ia] + V[ib] : V[ia] - V[ib];
                        double pre = Interval64.AddUp(fwdErr[ia], fwdErr[ib]);
                        preRoundErr[i] = pre;
                        double preMax = Interval64.AddUp(V[i].MaxAbs, pre);
                        double loc = HalfUlpMax(preMax);
                        fwdErr[i] = Interval64.AddUp(pre, loc);
                        break;
                    }
                    case Op.Mul: {
                        int ia = dag.A[i], ib = dag.B[i];
                        V[i] = V[ia] * V[ib];
                        double t1 = Interval64.MulUp(V[ib].MaxAbs, fwdErr[ia]);
                        double t2 = Interval64.MulUp(V[ia].MaxAbs, fwdErr[ib]);
                        double t3 = Interval64.MulUp(fwdErr[ia], fwdErr[ib]);
                        double pre = Interval64.AddUp(Interval64.AddUp(t1, t2), t3);
                        preRoundErr[i] = pre;
                        double preMax = Interval64.AddUp(V[i].MaxAbs, pre);
                        double loc = HalfUlpMax(preMax);
                        fwdErr[i] = Interval64.AddUp(pre, loc);
                        break;
                    }
                }
            }

            // Reverse-mode interval sensitivities VBar[k] enclosing d(f)/d(v_k) at exact values v over the box.
            var VBar = new Interval64[n];
            for (int i = 0; i < n; ++i) VBar[i] = Interval64.Point(0.0);
            VBar[dag.Root] = Interval64.Point(1.0);

            for (int i = dag.Root; i >= 0; --i) {
                var nd = dag.Nodes[i];
                if (nd.Op == Op.Input) continue;
                var vb = VBar[i];
                if (vb.IsZero) continue;
                int ia = dag.A[i], ib = dag.B[i];
                switch (nd.Op) {
                    case Op.Add:
                        VBar[ia] = VBar[ia] + vb;
                        VBar[ib] = VBar[ib] + vb;
                        break;
                    case Op.Sub:
                        VBar[ia] = VBar[ia] + vb;
                        VBar[ib] = VBar[ib] - vb;
                        break;
                    case Op.Mul:
                        VBar[ia] = VBar[ia] + vb * V[ib];
                        VBar[ib] = VBar[ib] + vb * V[ia];
                        break;
                }
            }

            // Exact telescoping identity:
            //   |v~_root - v_root| <= sum_{k not Input} |vBar_k| * HalfUlpMax(|v_k| + preRoundErr_k)
            //                       + sum_{k in Mul}    |vBar_k| * fwdErr_{a_k} * fwdErr_{b_k}
            double sumFirst = 0.0;
            double sumSecond = 0.0;
            for (int i = 0; i < n; ++i) {
                var nd = dag.Nodes[i];
                if (nd.Op == Op.Input) continue;
                double vbMax = VBar[i].MaxAbs;
                if (vbMax == 0.0) continue;

                double preMax = Interval64.AddUp(V[i].MaxAbs, preRoundErr[i]);
                double loc = HalfUlpMax(preMax);
                if (loc > 0.0) {
                    double term = Interval64.MulUp(vbMax, loc);
                    sumFirst = Interval64.AddUp(sumFirst, term);
                }
                if (nd.Op == Op.Mul) {
                    double ea = fwdErr[dag.A[i]], eb = fwdErr[dag.B[i]];
                    if (ea > 0.0 && eb > 0.0) {
                        double rem = Interval64.MulUp(vbMax, Interval64.MulUp(ea, eb));
                        sumSecond = Interval64.AddUp(sumSecond, rem);
                    }
                }
            }

            double total = Interval64.AddUp(sumFirst, sumSecond);

            // Split input dimension with largest width * sensitivity.
            int bestDim = 0;
            double bestScore = -1.0;
            for (int d = 0; d < inputBox.Length; ++d) {
                double width = inputBox[d].Hi - inputBox[d].Lo;
                if (!(width > 0.0)) continue;
                int subId = dag.InputSubIds[d];
                double score = width * (VBar[subId].MaxAbs + 1e-30);
                if (score > bestScore) {
                    bestScore = score;
                    bestDim = d;
                }
            }

            return new BoxEval(total, sumFirst, sumSecond, bestDim);
        }

        // First-order point proxy for branch-and-bound lower bound stopping criterion (never affects soundness).
        static double PointFirstOrderProxy(SubDag dag, double[] pt)
        {
            int n = dag.Nodes.Length;
            var v = new double[n];
            for (int i = 0; i < n; ++i) {
                var nd = dag.Nodes[i];
                v[i] = nd.Op switch {
                    Op.Input => pt[dag.A[i]],
                    Op.Add => v[dag.A[i]] + v[dag.B[i]],
                    Op.Sub => v[dag.A[i]] - v[dag.B[i]],
                    Op.Mul => v[dag.A[i]] * v[dag.B[i]],
                    _ => 0.0
                };
            }
            var vb = new double[n];
            vb[dag.Root] = 1.0;
            for (int i = dag.Root; i >= 0; --i) {
                var nd = dag.Nodes[i];
                if (nd.Op == Op.Input) continue;
                double vbi = vb[i];
                switch (nd.Op) {
                    case Op.Add: vb[dag.A[i]] += vbi; vb[dag.B[i]] += vbi; break;
                    case Op.Sub: vb[dag.A[i]] += vbi; vb[dag.B[i]] -= vbi; break;
                    case Op.Mul: vb[dag.A[i]] += vbi * v[dag.B[i]]; vb[dag.B[i]] += vbi * v[dag.A[i]]; break;
                }
            }
            double sum = 0.0;
            for (int i = 0; i < n; ++i) {
                if (dag.Nodes[i].Op == Op.Input) continue;
                sum += Math.Abs(vb[i]) * HalfUlpMax(Math.Abs(v[i]));
            }
            return sum;
        }

        // Generator's worst-case box bound: target.C * M_max(box), where M_max follows Compiler.cs's m~ recurrence.
        // For sign factors, uses FilterK (which is (C / (1 - u)).CeilDouble()); for internal nodes, C.CeilDouble().
        static double GeneratorBoxBound(Predicate p, SubDag dag, Node target, Interval64[] inputBox)
        {
            int n = dag.Nodes.Length;
            var V = new Interval64[n];
            var mMax = new double[n];
            for (int i = 0; i < n; ++i) {
                var nd = dag.Nodes[i];
                switch (nd.Op) {
                    case Op.Input:
                        V[i] = inputBox[dag.A[i]];
                        mMax[i] = V[i].MaxAbs;
                        break;
                    case Op.Add:
                    case Op.Sub: {
                        int ia = dag.A[i], ib = dag.B[i];
                        V[i] = nd.Op == Op.Add ? V[ia] + V[ib] : V[ia] - V[ib];
                        mMax[i] = nd.Leaf ? V[i].MaxAbs : Interval64.AddUp(mMax[ia], mMax[ib]);
                        break;
                    }
                    case Op.Mul: {
                        int ia = dag.A[i], ib = dag.B[i];
                        V[i] = V[ia] * V[ib];
                        mMax[i] = Interval64.MulUp(mMax[ia], mMax[ib]);
                        break;
                    }
                }
            }
            int signIdx = p.Signs.IndexOf(target);
            double coeff = signIdx >= 0 ? p.FilterK[signIdx] : target.C.CeilDouble();
            return Interval64.MulUp(coeff, mMax[dag.Root]);
        }

        // Exact rational evaluation on concrete double points inside the box to cross-check both bounds.
        static (bool allOk, double maxExactErr) SpotCheckExact(
            Predicate p, SubDag dag, Node target, Interval64[] inputBox, double symTaylorBound, int samples)
        {
            int d = inputBox.Length;
            int n = dag.Nodes.Length;
            int signIdx = p.Signs.IndexOf(target);
            double filterK = signIdx >= 0 ? p.FilterK[signIdx] : target.C.CeilDouble();
            double gateLo = Math.ScaleB(1.0, -p.GateLow);
            double gateHi = Math.ScaleB(1.0, p.GateHigh);

            var rng = new Random(202611);
            var pt = new double[d];
            var vFp = new double[n];
            var mFp = new double[n];
            var vEx = new Rational[n];

            double maxErr = 0.0;
            bool ok = true;

            for (int s = 0; s < samples; ++s) {
                bool inGate = true;
                for (int k = 0; k < d; ++k) {
                    double lo = inputBox[k].Lo, hi = inputBox[k].Hi;
                    if (s == 0) pt[k] = lo;
                    else if (s == 1) pt[k] = hi;
                    else if (s == 2) pt[k] = 0.5 * (lo + hi);
                    else if (s < samples / 2) pt[k] = rng.Next(2) == 0 ? lo : hi;
                    else pt[k] = lo + rng.NextDouble() * (hi - lo);
                    double ap = Math.Abs(pt[k]);
                    if (pt[k] != 0.0 && !(ap >= gateLo && ap <= gateHi))
                        inGate = false;
                }

                for (int i = 0; i < n; ++i) {
                    var nd = dag.Nodes[i];
                    switch (nd.Op) {
                        case Op.Input:
                            vFp[i] = pt[dag.A[i]];
                            mFp[i] = Math.Abs(vFp[i]);
                            vEx[i] = Rational.FromDouble(vFp[i]);
                            break;
                        case Op.Add: {
                            int ia = dag.A[i], ib = dag.B[i];
                            vFp[i] = vFp[ia] + vFp[ib];
                            mFp[i] = nd.Leaf ? Math.Abs(vFp[i]) : mFp[ia] + mFp[ib];
                            vEx[i] = vEx[ia] + vEx[ib];
                            break;
                        }
                        case Op.Sub: {
                            int ia = dag.A[i], ib = dag.B[i];
                            vFp[i] = vFp[ia] - vFp[ib];
                            mFp[i] = nd.Leaf ? Math.Abs(vFp[i]) : mFp[ia] + mFp[ib];
                            vEx[i] = vEx[ia] - vEx[ib];
                            break;
                        }
                        case Op.Mul: {
                            int ia = dag.A[i], ib = dag.B[i];
                            vFp[i] = vFp[ia] * vFp[ib];
                            mFp[i] = mFp[ia] * mFp[ib];
                            vEx[i] = vEx[ia] * vEx[ib];
                            break;
                        }
                    }
                }

                int r = dag.Root;
                Rational diff = Rational.FromDouble(vFp[r]) - vEx[r];
                Rational absDiff = diff.Num.Sign < 0 ? new Rational(-diff.Num, diff.Den) : diff;
                if (!(absDiff <= Rational.FromDouble(symTaylorBound)))
                    ok = false;

                // Pointwise check of Compiler.cs's runtime invariant |e~ - e| <= C * m~ (valid inside the range gate).
                if (inGate) {
                    Rational genPtBound = target.C * Rational.FromDouble(mFp[r]);
                    if (!(absDiff <= genPtBound))
                        ok = false;

                    // Sign filter check: if |e~| > fl(K * m~), sign(e~) must equal sign(e)
                    if (signIdx >= 0 && Math.Abs(vFp[r]) > filterK * mFp[r]) {
                        if (Math.Sign(vFp[r]) != vEx[r].Num.Sign)
                            ok = false;
                    }
                }

                if (!absDiff.Num.IsZero) {
                    double approxErr = absDiff.CeilDouble();
                    if (approxErr > maxErr) maxErr = approxErr;
                }
            }

            return (ok, maxErr);
        }

        readonly struct QueueItem
        {
            public readonly Interval64[] Box;
            public readonly BoxEval Eval;
            public QueueItem(Interval64[] box, BoxEval eval) { Box = box; Eval = eval; }
        }

        /// <summary>
        /// Verifies a single node of an analyzed `inputs xyz` predicate on the given per-point coordinate ranges.
        /// Any point absent from `pointRanges` uses `defaultRange`.
        /// </summary>
        public static FilterVerificationRow VerifyNode(
            Predicate p,
            Node target,
            string nodeLabel,
            string domainLabel,
            (double Lo, double Hi) defaultRange,
            Dictionary<string, (double Lo, double Hi)> pointRanges = null,
            int maxBoxes = 512,
            double relTol = 0.01,
            int exactSamples = 128)
        {
            if (!p.Xyz)
                throw new NotSupportedException($"{p.Name}: --verify-filters applies only to 'inputs xyz' predicates");

            var sw = Stopwatch.StartNew();
            var dag = SubDag.Extract(p, target);

            var initialBox = new Interval64[dag.InputSubIds.Length];
            for (int d = 0; d < initialBox.Length; ++d) {
                Node inp = dag.Nodes[dag.InputSubIds[d]];
                var range = defaultRange;
                if (pointRanges != null && pointRanges.TryGetValue(inp.Point, out var custom))
                    range = custom;
                initialBox[d] = new Interval64(range.Lo, range.Hi);
            }

            var eval0 = EvaluateBox(dag, initialBox);
            double lower = 0.0;
            var rng = new Random(1337);
            var pt = new double[initialBox.Length];
            for (int s = 0; s < 64; ++s) {
                for (int d = 0; d < initialBox.Length; ++d) {
                    if (s == 0) pt[d] = 0.5 * (initialBox[d].Lo + initialBox[d].Hi);
                    else if (s < 32) pt[d] = rng.Next(2) == 0 ? initialBox[d].Lo : initialBox[d].Hi;
                    else pt[d] = initialBox[d].Lo + rng.NextDouble() * (initialBox[d].Hi - initialBox[d].Lo);
                }
                lower = Math.Max(lower, PointFirstOrderProxy(dag, pt));
            }

            var pq = new PriorityQueue<QueueItem, double>();
            pq.Enqueue(new QueueItem(initialBox, eval0), -eval0.Total);
            int evaluated = 1;

            while (evaluated < maxBoxes && pq.Count > 0) {
                var top = pq.Peek();
                if (!double.IsFinite(top.Eval.Total)) break;
                if (lower > 0.0 && top.Eval.Total <= lower * (1.0 + relTol)) break;
                pq.Dequeue();

                int dim = top.Eval.SplitDim;
                double lo = top.Box[dim].Lo, hi = top.Box[dim].Hi;
                if (hi - lo <= 1e-12 * Math.Max(1.0, Math.Abs(hi))) {
                    pq.Enqueue(top, -top.Eval.Total);
                    break;
                }
                double mid = 0.5 * (lo + hi);

                var left = (Interval64[])top.Box.Clone();
                var right = (Interval64[])top.Box.Clone();
                left[dim] = new Interval64(lo, mid);
                right[dim] = new Interval64(mid, hi);

                var el = EvaluateBox(dag, left);
                var er = EvaluateBox(dag, right);
                pq.Enqueue(new QueueItem(left, el), -el.Total);
                pq.Enqueue(new QueueItem(right, er), -er.Total);
                evaluated += 2;

                for (int k = 0; k < left.Length; ++k) pt[k] = 0.5 * (left[k].Lo + left[k].Hi);
                lower = Math.Max(lower, PointFirstOrderProxy(dag, pt));
                for (int k = 0; k < right.Length; ++k) pt[k] = 0.5 * (right[k].Lo + right[k].Hi);
                lower = Math.Max(lower, PointFirstOrderProxy(dag, pt));
            }

            var best = pq.Peek().Eval;
            double genBound = GeneratorBoxBound(p, dag, target, initialBox);
            var (samplesOk, maxExactErr) = SpotCheckExact(p, dag, target, initialBox, best.Total, exactSamples);
            sw.Stop();

            bool dominates = double.IsFinite(best.Total) && double.IsFinite(genBound) && best.Total <= genBound;
            double ratio = best.Total > 0.0 ? genBound / best.Total : double.PositiveInfinity;

            return new FilterVerificationRow {
                PredicateName = p.Name,
                NodeLabel = nodeLabel,
                DomainLabel = domainLabel,
                Degree = target.Degree,
                OpCount = dag.OpCount,
                SymbolicTaylorBound = best.Total,
                FirstOrderBound = best.First,
                SecondOrderBound = best.Second,
                GeneratorBound = genBound,
                Ratio = ratio,
                MaxSampledExactErr = maxExactErr,
                BoxesEvaluated = evaluated,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                Dominates = dominates,
                SamplesValid = samplesOk
            };
        }

        /// <summary>
        /// Verifies every sign factor of every `inputs xyz` predicate in the spec across standard bounded domains
        /// (`[-1, 1]` symmetric unit box and `[1, 2]` shifted positive box).
        /// </summary>
        public static FilterVerificationReport VerifySpec(string spec, int maxBoxes = 512, int exactSamples = 128)
        {
            var preds = PredicateCompiler.Analyze(spec).Where(p => p.Xyz).ToList();
            return VerifyPredicates(preds, maxBoxes, exactSamples);
        }

        public static FilterVerificationReport VerifyPredicates(IEnumerable<Predicate> preds, int maxBoxes = 512, int exactSamples = 128)
        {
            var sw = Stopwatch.StartNew();
            var report = new FilterVerificationReport();
            var domains = new[] {
                ("[-1, 1]", (-1.0, 1.0)),
                ("[1, 2]", (1.0, 2.0)),
            };

            foreach (var p in preds) {
                if (!p.Xyz) continue;
                for (int f = 0; f < p.Signs.Count; ++f) {
                    var target = p.Signs[f];
                    string label = target.Name ?? (f == 0 ? "det" : $"sign_{f}");
                    foreach (var (domName, range) in domains) {
                        report.Rows.Add(VerifyNode(p, target, label, domName, range, null, maxBoxes, 0.01, exactSamples));
                    }
                }
            }
            sw.Stop();
            report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return report;
        }

        public static string FormatReport(FilterVerificationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| predicate | factor | domain | deg | ops | SymTaylor bound | generator bound | ratio (gen/ST) | max sampled err | boxes | status |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var r in report.Rows) {
                string status = r.Passed ? "PASS" : (!r.Dominates ? "FAIL (bound)" : "FAIL (sample)");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| `{0}` | `{1}` | `{2}` | {3} | {4} | {5:E4} | {6:E4} | {7:F2}x | {8:E3} | {9} | {10} |",
                    r.PredicateName, r.NodeLabel, r.DomainLabel, r.Degree, r.OpCount,
                    r.SymbolicTaylorBound, r.GeneratorBound, r.Ratio, r.MaxSampledExactErr, r.BoxesEvaluated, status));
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Verified {0} filter checks in {1:F1} ms: {2}",
                report.Rows.Count, report.TotalElapsedMs, report.AllPassed ? "ALL PASSED" : "FAILED"));
            return sb.ToString();
        }
    }
}
