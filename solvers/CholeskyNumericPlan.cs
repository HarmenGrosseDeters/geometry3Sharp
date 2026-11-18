// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The structure of the numeric factorization of SparseCholesky and SparseHermitianCholesky as data: for every supernode J the
    /// supernodes K that update it, with the row range [p0, p1) of K that falls in the columns of J, in exactly the order in
    /// which the serial left-looking loop (linked lists head / link / pos of E. Ng and B. Peyton's algorithm) applies them, and the
    /// split of the supernode tree into tasks. With the updates as a table, the supernode J only reads supernodes of its own
    /// subtree, so the tasks (subtrees) run in parallel, and every entry of L gets the same sequence of operations as in the
    /// serial loop, hence the same bits. The supernodes above the tasks (the top) run one after the other in ascending order;
    /// the large ones run their dense kernels in parallel over independent rows (see the classes).
    /// </summary>
    internal sealed class CholeskyNumericPlan
    {
        /// <summary> total work (about multiplications) below which the factorization stays serial </summary>
        internal const long DefaultMinWork = 6000000;
        internal const int DefaultDivisor = 48;

        /// <summary> task t = supernodes [TaskFirst[t], TaskEnd[t]) </summary>
        public int[] TaskFirst, TaskEnd;
        /// <summary> the supernodes outside the tasks, ascending </summary>
        public int[] Top;
        /// <summary> the updates of supernode J are the entries UpdPtr[J] ... UpdPtr[J + 1] - 1 </summary>
        public int[] UpdPtr, UpdK, UpdP0, UpdP1;

        /// <summary> the plan, or null if the work is below minWork or fewer than two tasks come out (the factorization runs serially) </summary>
        public static CholeskyNumericPlan Build(int snCount, int[] snFirst, int[] snRowPtr, int[] snRows, int[] colSnode, long minWork, int divisor)
        {
            if (snCount < 2 || divisor < 2) return null;
            var head = new int[snCount]; var link = new int[snCount]; var pos = new int[snCount];
            for (int J = 0; J < snCount; ++J) head[J] = -1;
            var updPtr = new int[snCount + 1];
            var uk = new List<int>(); var up0 = new List<int>(); var up1 = new List<int>();
            var own = new long[snCount]; var parent = new int[snCount];
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, lJ = f + w;
                long work = (long)w * w * h / 2 + 64;
                int K = head[J];
                while (K != -1) {
                    int knext = link[K];
                    int krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp, kw = snFirst[K + 1] - snFirst[K];
                    int p0 = pos[K], p1 = p0;
                    while (p1 < kh && snRows[krp + p1] < lJ) p1++;
                    uk.Add(K); up0.Add(p0); up1.Add(p1);
                    work += (long)(kh - p0) * (p1 - p0) * kw;
                    pos[K] = p1;
                    if (p1 < kh) {
                        int JS = colSnode[snRows[krp + p1]];
                        link[K] = head[JS]; head[JS] = K;
                    }
                    K = knext;
                }
                updPtr[J + 1] = uk.Count;
                pos[J] = w;
                parent[J] = -1;
                if (w < h) {
                    int JS = colSnode[snRows[rp + w]];
                    link[J] = head[JS]; head[JS] = J;
                    parent[J] = JS;
                }
                own[J] = work;
            }
            List<int> first, end;
            if (!CholeskySolveSchedule.Partition(snCount, own, parent, minWork, divisor, out first, out end)) return null;
            var plan = new CholeskyNumericPlan();
            plan.TaskFirst = first.ToArray(); plan.TaskEnd = end.ToArray();
            plan.UpdPtr = updPtr; plan.UpdK = uk.ToArray(); plan.UpdP0 = up0.ToArray(); plan.UpdP1 = up1.ToArray();
            var top = new List<int>();
            int t = 0;
            for (int J = 0; J < snCount; ++J) {
                if (t < first.Count && J == first[t]) { J = end[t] - 1; ++t; continue; }
                top.Add(J);
            }
            plan.Top = top.ToArray();
            return plan;
        }
    }
}
