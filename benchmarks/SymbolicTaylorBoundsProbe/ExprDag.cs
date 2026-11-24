// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary>
/// Outward-rounded IEEE-754 binary64 interval arithmetic (matching math/ExactIntervals.cs semantics:
/// round-to-nearest followed by one-ulp outward widening on both ends, sound for normals and subnormals).
/// </summary>
internal readonly struct Interval64
{
    public readonly double Lo;
    public readonly double Hi;

    public Interval64(double lo, double hi) { Lo = lo; Hi = hi; }

    public static Interval64 Point(double x) => new Interval64(x, x);
    public static readonly Interval64 Whole = new Interval64(double.NegativeInfinity, double.PositiveInfinity);

    public double MaxAbs => Math.Max(Math.Abs(Lo), Math.Abs(Hi));
    public double Radius => 0.5 * (Hi - Lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Up(double x)
    {
        if (double.IsNaN(x) || x == double.PositiveInfinity) return x;
        if (x == 0.0) return double.Epsilon;
        long b = BitConverter.DoubleToInt64Bits(x);
        return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Down(double x) => -Up(-x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Interval64 Make(double lo, double hi)
    {
        if (double.IsNaN(lo) || double.IsNaN(hi)) return Whole;
        return new Interval64(Down(lo), Up(hi));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Interval64 operator +(Interval64 a, Interval64 b) => Make(a.Lo + b.Lo, a.Hi + b.Hi);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Interval64 operator -(Interval64 a, Interval64 b) => Make(a.Lo - b.Hi, a.Hi - b.Lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Interval64 operator -(Interval64 a) => new Interval64(-a.Hi, -a.Lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Interval64 operator *(Interval64 a, Interval64 b)
    {
        double p1 = a.Lo * b.Lo, p2 = a.Lo * b.Hi, p3 = a.Hi * b.Lo, p4 = a.Hi * b.Hi;
        if (double.IsNaN(p1) || double.IsNaN(p2) || double.IsNaN(p3) || double.IsNaN(p4)) return Whole;
        return Make(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4)), Math.Max(Math.Max(p1, p2), Math.Max(p3, p4)));
    }

    public int Sign()
    {
        if (Lo > 0) return 1;
        if (Hi < 0) return -1;
        if (Lo == 0 && Hi == 0) return 0;
        return 2; // undecided
    }
}

/// <summary>
/// Exact dyadic / binary rational number m * 2^e for exact ground-truth evaluation of binary64 polynomials.
/// </summary>
internal readonly struct ExactBin
{
    public readonly BigInteger M;
    public readonly int Exp;

    public ExactBin(BigInteger m, int exp)
    {
        if (m.IsZero) { M = BigInteger.Zero; Exp = 0; }
        else { M = m; Exp = exp; }
    }

    public static ExactBin FromDouble(double x)
    {
        if (x == 0.0) return new ExactBin(BigInteger.Zero, 0);
        if (!double.IsFinite(x)) throw new ArgumentException($"Non-finite double: {x}");
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool neg = bits < 0;
        int be = (int)((bits >> 52) & 0x7FFL);
        long frac = bits & ((1L << 52) - 1);
        BigInteger m;
        int e;
        if (be == 0) { m = new BigInteger(frac); e = -1074; }
        else { m = new BigInteger((1L << 52) | frac); e = be - 1075; }
        return new ExactBin(neg ? -m : m, e);
    }

    public static ExactBin operator +(ExactBin a, ExactBin b)
    {
        if (a.M.IsZero) return b;
        if (b.M.IsZero) return a;
        if (a.Exp >= b.Exp) return new ExactBin((a.M << (a.Exp - b.Exp)) + b.M, b.Exp);
        return new ExactBin(a.M + (b.M << (b.Exp - a.Exp)), a.Exp);
    }

    public static ExactBin operator -(ExactBin a, ExactBin b) => a + new ExactBin(-b.M, b.Exp);

    public static ExactBin operator *(ExactBin a, ExactBin b) => new ExactBin(a.M * b.M, a.Exp + b.Exp);

    public int Sign => M.Sign;

    public double ToDoubleAbs()
    {
        if (M.IsZero) return 0.0;
        BigInteger am = BigInteger.Abs(M);
        int bl = (int)am.GetBitLength();
        if (bl <= 53) return Math.ScaleB((double)am, Exp);
        int shift = bl - 53;
        return Math.ScaleB((double)(am >> shift), Exp + shift);
    }
}

internal enum DagOp : byte { Input, Add, Sub, Mul }

internal readonly struct DagNode
{
    public readonly DagOp Op;
    public readonly int A;
    public readonly int B;
    public readonly string Name;

    public DagNode(DagOp op, int a, int b, string name)
    {
        Op = op; A = a; B = b; Name = name;
    }
}

/// <summary>
/// Arithmetic expression DAG over binary64 inputs with (+, -, *) operations.
/// Supports exact evaluation, IEEE-754 double evaluation, PredicateCompiler forward permanent bounds,
/// outward-rounded Interval64 evaluation, and Symbolic Taylor Round-off Expansion bounds.
/// </summary>
internal sealed class ExprDag
{
    public const double U = 1.1102230246251565404236316680908203125e-16; // 2^-53

    public readonly string Name;
    public readonly List<DagNode> Nodes = new List<DagNode>();
    public readonly List<int> InputIds = new List<int>();
    public readonly Dictionary<string, int> InputByName = new Dictionary<string, int>();
    public int Root = -1;

    // Precomputed static PredicateCompiler forward constants c_k (|v~_k - v_k| <= c_k * m~_k).
    public double[] ForwardC = Array.Empty<double>();
    // Precomputed static reverse-pass forward-error constants cbar_k on the adjoint DAG.
    public double[] AdjointC = Array.Empty<double>();
    public bool[] IsLeafDiff = Array.Empty<bool>();
    public int[] Degree = Array.Empty<int>();

    public ExprDag(string name) { Name = name; }

    public int Input(string name)
    {
        if (InputByName.TryGetValue(name, out int id)) return id;
        id = Nodes.Count;
        Nodes.Add(new DagNode(DagOp.Input, InputIds.Count, -1, name));
        InputIds.Add(id);
        InputByName[name] = id;
        return id;
    }

    public int Add(int a, int b, string name = "") { int id = Nodes.Count; Nodes.Add(new DagNode(DagOp.Add, a, b, name)); return id; }
    public int Sub(int a, int b, string name = "") { int id = Nodes.Count; Nodes.Add(new DagNode(DagOp.Sub, a, b, name)); return id; }
    public int Mul(int a, int b, string name = "") { int id = Nodes.Count; Nodes.Add(new DagNode(DagOp.Mul, a, b, name)); return id; }

    public void FinalizeRoot(int root)
    {
        Root = root;
        int n = Nodes.Count;
        ForwardC = new double[n];
        IsLeafDiff = new bool[n];
        Degree = new int[n];

        // Exact PredicateCompiler filter_bounds rule (Compiler.cs:270-280), rounded upward for soundness.
        double inv1mU = Interval64.Up(1.0 / (1.0 - U));
        for (int i = 0; i < n; ++i) {
            var nd = Nodes[i];
            switch (nd.Op) {
                case DagOp.Input:
                    ForwardC[i] = 0.0;
                    Degree[i] = 1;
                    break;
                case DagOp.Add:
                case DagOp.Sub:
                    IsLeafDiff[i] = Nodes[nd.A].Op == DagOp.Input && Nodes[nd.B].Op == DagOp.Input;
                    Degree[i] = Math.Max(Degree[nd.A], Degree[nd.B]);
                    if (IsLeafDiff[i])
                        ForwardC[i] = U;
                    else
                        ForwardC[i] = Interval64.Up((U + Math.Max(ForwardC[nd.A], ForwardC[nd.B])) * inv1mU);
                    break;
                case DagOp.Mul:
                    Degree[i] = Degree[nd.A] + Degree[nd.B];
                    double ca = ForwardC[nd.A], cb = ForwardC[nd.B];
                    ForwardC[i] = Interval64.Up((U + ca + cb + ca * cb) * inv1mU);
                    break;
            }
        }

        // Compute conservative reverse-pass error factor AdjointC[i]:
        // During reverse sweep, each adjoint vbar[i] is a sum of fanout terms (each either +/- vbar[p]
        // or vbar[p] * v~[sibling]). Track relative error against reverse permanent mbar[i].
        AdjointC = new double[n];
        int[] fanout = new int[n];
        for (int i = 0; i < n; ++i) {
            if (Nodes[i].Op != DagOp.Input) {
                fanout[Nodes[i].A]++;
                fanout[Nodes[i].B]++;
            }
        }
        // In reverse topological order from Root down to 0:
        double[] maxIncoming = new double[n];
        for (int i = Root; i >= 0; --i) {
            if (i == Root) {
                AdjointC[i] = 0.0;
            } else {
                // Sum of fanout[i] terms introduces at most (fanout[i] - 1) additions.
                double c = maxIncoming[i];
                for (int s = 1; s < fanout[i]; ++s)
                    c = Interval64.Up((U + c) * inv1mU);
                AdjointC[i] = c;
            }

            var nd = Nodes[i];
            if (nd.Op == DagOp.Add || nd.Op == DagOp.Sub) {
                maxIncoming[nd.A] = Math.Max(maxIncoming[nd.A], AdjointC[i]);
                maxIncoming[nd.B] = Math.Max(maxIncoming[nd.B], AdjointC[i]);
            } else if (nd.Op == DagOp.Mul) {
                // Term is vbar[i] * v~[sibling]: one rounding (U) on top of AdjointC[i]
                // (we linearize around exact computed doubles v~, so v~[sibling] itself has 0 error here!).
                double termC = Interval64.Up((U + AdjointC[i]) * inv1mU);
                maxIncoming[nd.A] = Math.Max(maxIncoming[nd.A], termC);
                maxIncoming[nd.B] = Math.Max(maxIncoming[nd.B], termC);
            }
        }
    }

    public double StaticCompilerK => Interval64.Up(ForwardC[Root] / (1.0 - U));

    public ExactBin EvalExact(double[] inputs)
    {
        var vals = new ExactBin[Nodes.Count];
        for (int i = 0; i <= Root; ++i) {
            var nd = Nodes[i];
            vals[i] = nd.Op switch {
                DagOp.Input => ExactBin.FromDouble(inputs[nd.A]),
                DagOp.Add => vals[nd.A] + vals[nd.B],
                DagOp.Sub => vals[nd.A] - vals[nd.B],
                DagOp.Mul => vals[nd.A] * vals[nd.B],
                _ => throw new InvalidOperationException()
            };
        }
        return vals[Root];
    }

    public Interval64 EvalInterval(double[] inputs)
    {
        var iv = new Interval64[Nodes.Count];
        for (int i = 0; i <= Root; ++i) {
            var nd = Nodes[i];
            iv[i] = nd.Op switch {
                DagOp.Input => Interval64.Point(inputs[nd.A]),
                DagOp.Add => iv[nd.A] + iv[nd.B],
                DagOp.Sub => iv[nd.A] - iv[nd.B],
                DagOp.Mul => iv[nd.A] * iv[nd.B],
                _ => throw new InvalidOperationException()
            };
        }
        return iv[Root];
    }

    /// <summary>
    /// Evaluates PredicateCompiler's Stage-A permanent filter in pure double.
    /// Returns (val, permBound, decidedSign) where decidedSign is -1/0/+1 if decided, 2 if undecided.
    /// </summary>
    public (double Val, double Bound, int Sign) EvalStageAPermanent(double[] inputs, double[] vBuf, double[] mBuf)
    {
        for (int i = 0; i <= Root; ++i) {
            var nd = Nodes[i];
            switch (nd.Op) {
                case DagOp.Input:
                    vBuf[i] = inputs[nd.A];
                    mBuf[i] = Math.Abs(vBuf[i]);
                    break;
                case DagOp.Add:
                    vBuf[i] = vBuf[nd.A] + vBuf[nd.B];
                    mBuf[i] = IsLeafDiff[i] ? Math.Abs(vBuf[i]) : mBuf[nd.A] + mBuf[nd.B];
                    break;
                case DagOp.Sub:
                    vBuf[i] = vBuf[nd.A] - vBuf[nd.B];
                    mBuf[i] = IsLeafDiff[i] ? Math.Abs(vBuf[i]) : mBuf[nd.A] + mBuf[nd.B];
                    break;
                case DagOp.Mul:
                    vBuf[i] = vBuf[nd.A] * vBuf[nd.B];
                    mBuf[i] = mBuf[nd.A] * mBuf[nd.B];
                    break;
            }
        }
        double val = vBuf[Root];
        double m = mBuf[Root];
        if (m == 0.0) return (0.0, 0.0, 0);
        double bound = StaticCompilerK * m;
        int sign = val > bound ? 1 : (val < -bound ? -1 : 2);
        return (val, bound, sign);
    }

    /// <summary>
    /// Rigorous Runtime Symbolic Taylor Filter (Stage 1.5, Solovyev et al. 2018 + exact telescoping identity):
    /// Assumes vBuf and mBuf were populated by EvalStageAPermanent (normal range, no underflow/overflow).
    /// Computes reverse-mode adjoints vbar[k] on the signed computed values vBuf[k], plus a rigorous O(u^2)
    /// upper bound on adjoint rounding and the exact quadratic Mul remainder sum |vbar[k]| * c_a*m_a * c_b*m_b.
    /// For sign decisions (signFilter = true), the final root rounding cannot flip sign and is omitted.
    /// </summary>
    public (double Bound, double FirstOrderTerm, double SecondOrderTerm, int Sign) EvalRuntimeSymbolicTaylor(
        double[] vBuf,
        double[] mBuf,
        double[] vBar,
        double[] mBar,
        bool signFilter = true)
    {
        int r = Root;
        Array.Clear(vBar, 0, r + 1);
        Array.Clear(mBar, 0, r + 1);
        vBar[r] = 1.0;
        mBar[r] = 1.0;

        for (int i = r; i >= 0; --i) {
            var nd = Nodes[i];
            if (nd.Op == DagOp.Input) continue;
            double vb = vBar[i];
            double mb = mBar[i];
            switch (nd.Op) {
                case DagOp.Add:
                    vBar[nd.A] += vb;
                    vBar[nd.B] += vb;
                    mBar[nd.A] += mb;
                    mBar[nd.B] += mb;
                    break;
                case DagOp.Sub:
                    vBar[nd.A] += vb;
                    vBar[nd.B] -= vb;
                    mBar[nd.A] += mb;
                    mBar[nd.B] += mb;
                    break;
                case DagOp.Mul:
                    vBar[nd.A] += vb * vBuf[nd.B];
                    vBar[nd.B] += vb * vBuf[nd.A];
                    double ma = Math.Abs(vBuf[nd.A]), mbv = Math.Abs(vBuf[nd.B]);
                    mBar[nd.A] += mb * mbv;
                    mBar[nd.B] += mb * ma;
                    break;
            }
        }

        // Accumulate exact 1st-order sensitivity sum and exact 2nd-order remainder.
        // Local rounding at node k is |l_k| <= u * |vBuf[k]| (Knuth / Jeannerod-Rump 2018; exact on normal ops).
        double firstOrder = 0.0;
        double adjointErr = 0.0;
        double quadRem = 0.0;
        int opCount = 0;

        for (int i = 0; i <= r; ++i) {
            var nd = Nodes[i];
            if (nd.Op == DagOp.Input) continue;
            double av = Math.Abs(vBuf[i]);
            if ((av > 0.0 && av < 1e-290) || av > 1e150 || !double.IsFinite(av))
                return (double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, 2);
            ++opCount;

            // Rigorous upper bound on |exact_vbar[i] - vBar[i]|: AdjointC[i] * mBar[i].
            double vbarErr = AdjointC[i] * mBar[i];
            double absVbarUpper = Math.Abs(vBar[i]) + vbarErr;

            // Omit root rounding for sign filters (fl(z) has exact same sign as z).
            if (!(signFilter && i == r)) {
                firstOrder += Math.Abs(vBar[i]) * Math.Abs(vBuf[i]);
                adjointErr += vbarErr * Math.Abs(vBuf[i]);
            }

            if (nd.Op == DagOp.Mul) {
                // Exact quadratic remainder of Mul: |vbar_exact| * |eps_a| * |eps_b|
                // with |eps_a| <= ForwardC[A] * mBuf[A], |eps_b| <= ForwardC[B] * mBuf[B].
                double ea = ForwardC[nd.A] * mBuf[nd.A];
                double eb = ForwardC[nd.B] * mBuf[nd.B];
                quadRem += absVbarUpper * (ea * eb);
            }
        }

        // Cover floating-point rounding in the accumulation itself via generous upward factor (1 + (2*opCount + 8)*u).
        double sumSafety = Interval64.Up(1.0 + (2 * opCount + 8) * U);
        double firstTerm = Interval64.Up(U * firstOrder * sumSafety);
        double secondTerm = Interval64.Up((U * adjointErr + quadRem) * sumSafety);
        double totalBound = Interval64.Up((firstTerm + secondTerm) * Interval64.Up(1.0 + 4.0 * U));

        if (!double.IsFinite(totalBound)) return (double.PositiveInfinity, firstTerm, secondTerm, 2);
        double val = vBuf[r];
        int sign = val > totalBound ? 1 : (val < -totalBound ? -1 : 2);
        return (totalBound, firstTerm, secondTerm, sign);
    }
}
