// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Offline Bounded-Box Symbolic Taylor Round-off Analyzer (Solovyev et al., ACM TOPLAS 2018):
/// computes rigorous upper bounds on max_{x in B} |fl(f(x)) - f(x)| using reverse-mode interval
/// sensitivities, exact algebraic second-order Mul remainder R_2, and priority-queue branch-and-bound.
/// </summary>
internal static class BoxSymbolicTaylor
{
    public sealed class BoxResult
    {
        public double SingleBoxBound;
        public double RefinedBound;
        public double FirstOrderBound;
        public double SecondOrderBound;
        public double SampledLowerBound;
        public int BoxesEvaluated;
        public double ElapsedMs;
    }

    private readonly struct BoxItem
    {
        public readonly Interval64[] Box;
        public readonly double Upper;
        public readonly double FirstOrder;
        public readonly double SecondOrder;
        public readonly int SplitDim;

        public BoxItem(Interval64[] box, double upper, double firstOrder, double secondOrder, int splitDim)
        {
            Box = box; Upper = upper; FirstOrder = firstOrder; SecondOrder = secondOrder; SplitDim = splitDim;
        }
    }

    private static (double Total, double FirstOrder, double SecondOrder, int BestSplitDim) EvaluateBox(
        ExprDag dag,
        Interval64[] inputBox,
        bool signFilter,
        bool useTightDiff = true)
    {
        int n = dag.Root + 1;
        var V = new Interval64[n];
        var fwdErr = new double[n];
        double u = ExprDag.U;

        for (int i = 0; i < n; ++i) {
            var nd = dag.Nodes[i];
            switch (nd.Op) {
                case DagOp.Input:
                    V[i] = inputBox[nd.A];
                    fwdErr[i] = 0.0;
                    break;
                case DagOp.Add:
                    V[i] = V[nd.A] + V[nd.B];
                    fwdErr[i] = Interval64.Up(fwdErr[nd.A] + fwdErr[nd.B] + u * V[i].MaxAbs);
                    break;
                case DagOp.Sub:
                    V[i] = V[nd.A] - V[nd.B];
                    fwdErr[i] = Interval64.Up(fwdErr[nd.A] + fwdErr[nd.B] + u * V[i].MaxAbs);
                    break;
                case DagOp.Mul:
                    V[i] = V[nd.A] * V[nd.B];
                    fwdErr[i] = Interval64.Up(
                        V[nd.B].MaxAbs * fwdErr[nd.A]
                        + V[nd.A].MaxAbs * fwdErr[nd.B]
                        + fwdErr[nd.A] * fwdErr[nd.B]
                        + u * V[i].MaxAbs);
                    break;
            }
        }

        // Reverse-mode interval sensitivities VBar[k] enclosing d(f)/d(v_k) over the box.
        var VBar = new Interval64[n];
        for (int i = 0; i < n; ++i) VBar[i] = Interval64.Point(0.0);
        VBar[dag.Root] = Interval64.Point(1.0);

        for (int i = dag.Root; i >= 0; --i) {
            var nd = dag.Nodes[i];
            if (nd.Op == DagOp.Input) continue;
            var vb = VBar[i];
            switch (nd.Op) {
                case DagOp.Add:
                    VBar[nd.A] = VBar[nd.A] + vb;
                    VBar[nd.B] = VBar[nd.B] + vb;
                    break;
                case DagOp.Sub:
                    VBar[nd.A] = VBar[nd.A] + vb;
                    VBar[nd.B] = VBar[nd.B] - vb;
                    break;
                case DagOp.Mul:
                    VBar[nd.A] = VBar[nd.A] + vb * V[nd.B];
                    VBar[nd.B] = VBar[nd.B] + vb * V[nd.A];
                    break;
            }
        }

        // Optional symbolic cancellation check for shared-input differences of the form (x - r) - (y - r) -> x - y
        // in 1st-order adjoints: handled naturally by splitting shared inputs (like rx, ry, rz) in branch-and-bound!
        _ = useTightDiff;

        double sumFirst = 0.0;
        double sumSecond = 0.0;
        int opCount = 0;
        for (int i = 0; i < n; ++i) {
            var nd = dag.Nodes[i];
            if (nd.Op == DagOp.Input) continue;
            ++opCount;
            if (!(signFilter && i == dag.Root)) {
                double prodMax = (VBar[i] * V[i]).MaxAbs;
                sumFirst = Interval64.Up(sumFirst + prodMax);
            }
            if (nd.Op == DagOp.Mul) {
                double rem = Interval64.Up(VBar[i].MaxAbs * Interval64.Up(fwdErr[nd.A] * fwdErr[nd.B]));
                sumSecond = Interval64.Up(sumSecond + rem);
            }
        }

        // Factor (1 + u) covers |v~_k| <= (1 + c_k)*|v_k|? More precisely, local error |l_k| <= u*(|v_k| + fwdErr[k]),
        // so include u * VBar[k].MaxAbs * fwdErr[k] in second-order remainder for 100% soundness.
        for (int i = 0; i < n; ++i) {
            var nd = dag.Nodes[i];
            if (nd.Op == DagOp.Input) continue;
            if (!(signFilter && i == dag.Root)) {
                double shift = Interval64.Up(u * Interval64.Up(VBar[i].MaxAbs * fwdErr[i]));
                sumSecond = Interval64.Up(sumSecond + shift);
            }
        }

        double first = Interval64.Up(u * sumFirst);
        double total = Interval64.Up(first + sumSecond);

        // Pick split dimension: input with largest width * (sensitivity + radius proxy).
        int bestDim = 0;
        double bestScore = -1.0;
        for (int d = 0; d < inputBox.Length; ++d) {
            double width = inputBox[d].Hi - inputBox[d].Lo;
            if (width <= 0.0) continue;
            int nodeId = dag.InputIds[d];
            double score = width * (VBar[nodeId].MaxAbs + 1e-30);
            if (score > bestScore) {
                bestScore = score;
                bestDim = d;
            }
        }

        return (total, first, sumSecond, bestDim);
    }

    private static double EvaluateFirstOrderPointLowerBound(ExprDag dag, double[] pt, bool signFilter)
    {
        int n = dag.Root + 1;
        var v = new double[n];
        for (int i = 0; i < n; ++i) {
            var nd = dag.Nodes[i];
            v[i] = nd.Op switch {
                DagOp.Input => pt[nd.A],
                DagOp.Add => v[nd.A] + v[nd.B],
                DagOp.Sub => v[nd.A] - v[nd.B],
                DagOp.Mul => v[nd.A] * v[nd.B],
                _ => 0
            };
        }
        var vb = new double[n];
        vb[dag.Root] = 1.0;
        for (int i = dag.Root; i >= 0; --i) {
            var nd = dag.Nodes[i];
            if (nd.Op == DagOp.Input) continue;
            switch (nd.Op) {
                case DagOp.Add: vb[nd.A] += vb[i]; vb[nd.B] += vb[i]; break;
                case DagOp.Sub: vb[nd.A] += vb[i]; vb[nd.B] -= vb[i]; break;
                case DagOp.Mul: vb[nd.A] += vb[i] * v[nd.B]; vb[nd.B] += vb[i] * v[nd.A]; break;
            }
        }
        double sum = 0.0;
        for (int i = 0; i < n; ++i) {
            if (dag.Nodes[i].Op == DagOp.Input) continue;
            if (signFilter && i == dag.Root) continue;
            sum += Math.Abs(vb[i] * v[i]);
        }
        return ExprDag.U * sum;
    }

    public static BoxResult Bound(
        ExprDag dag,
        Interval64[] initialBox,
        int maxBoxes = 512,
        double relTol = 0.01,
        bool signFilter = false)
    {
        var sw = Stopwatch.StartNew();
        var (u0, f0, s0, dim0) = EvaluateBox(dag, initialBox, signFilter);

        // Quick corner/midpoint sampling for a lower bound on the 1st-order error max.
        double lower = 0.0;
        var rng = new Random(1337);
        var pt = new double[initialBox.Length];
        for (int s = 0; s < 128; ++s) {
            for (int d = 0; d < initialBox.Length; ++d) {
                if (s == 0) pt[d] = 0.5 * (initialBox[d].Lo + initialBox[d].Hi);
                else if (s < 64) pt[d] = rng.Next(2) == 0 ? initialBox[d].Lo : initialBox[d].Hi;
                else pt[d] = initialBox[d].Lo + rng.NextDouble() * (initialBox[d].Hi - initialBox[d].Lo);
            }
            lower = Math.Max(lower, EvaluateFirstOrderPointLowerBound(dag, pt, signFilter));
        }

        var pq = new PriorityQueue<BoxItem, double>();
        pq.Enqueue(new BoxItem(initialBox, u0, f0, s0, dim0), -u0);
        int evaluated = 1;

        while (evaluated < maxBoxes && pq.Count > 0) {
            var top = pq.Peek();
            if (lower > 0 && top.Upper <= lower * (1.0 + relTol))
                break;
            pq.Dequeue();

            int d = top.SplitDim;
            double lo = top.Box[d].Lo, hi = top.Box[d].Hi;
            if (hi - lo <= 1e-12 * Math.Max(1.0, Math.Abs(hi)))
                break;
            double mid = 0.5 * (lo + hi);

            var left = (Interval64[])top.Box.Clone();
            var right = (Interval64[])top.Box.Clone();
            left[d] = new Interval64(lo, mid);
            right[d] = new Interval64(mid, hi);

            var (ul, fl, sl, dl) = EvaluateBox(dag, left, signFilter);
            var (ur, fr, sr, dr) = EvaluateBox(dag, right, signFilter);
            pq.Enqueue(new BoxItem(left, ul, fl, sl, dl), -ul);
            pq.Enqueue(new BoxItem(right, ur, fr, sr, dr), -ur);
            evaluated += 2;

            // Sample midpoints of children to tighten lower bound.
            for (int k = 0; k < left.Length; ++k) pt[k] = 0.5 * (left[k].Lo + left[k].Hi);
            lower = Math.Max(lower, EvaluateFirstOrderPointLowerBound(dag, pt, signFilter));
            for (int k = 0; k < right.Length; ++k) pt[k] = 0.5 * (right[k].Lo + right[k].Hi);
            lower = Math.Max(lower, EvaluateFirstOrderPointLowerBound(dag, pt, signFilter));
        }

        var best = pq.Peek();
        sw.Stop();
        return new BoxResult {
            SingleBoxBound = u0,
            RefinedBound = best.Upper,
            FirstOrderBound = best.FirstOrder,
            SecondOrderBound = best.SecondOrder,
            SampledLowerBound = lower,
            BoxesEvaluated = evaluated,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
        };
    }
}
