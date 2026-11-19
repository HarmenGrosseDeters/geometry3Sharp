// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Which supernodes of a factor the triangular solves of SparseCholesky and SparseHermitianCholesky run as parallel
    /// tasks, and the buffers that keep every entry's order of operations the serial one (the solutions do not depend on
    /// the schedule, on the thread count or on whether the schedule is used at all).
    ///
    /// The supernodes are in postorder of the elimination tree, so the subtree of a supernode is a contiguous range, and a
    /// supernode only writes (forward solve) to the rows of its ancestors. A task is a range closed under descendants
    /// (one or several adjacent subtrees); every other supernode is "top". Forward solve: the tasks run in parallel, a
    /// supernode adds its updates to the rows inside its task in place (those rows only have contributors in the task, in
    /// the serial order) and stores the updates for the rows above the task in a buffer; then one serial pass in supernode
    /// order applies the buffers of the task supernodes and does the work of the top supernodes, so each row above the
    /// tasks gets its subtractions in the order of the serial loop. Backward solve: the top supernodes run first
    /// (descending), then the tasks in parallel; a task only reads rows of its own range and of the top.
    /// </summary>
    internal sealed class CholeskySolveSchedule
    {
        /// <summary> total work (about multiplications) below which the solves stay serial </summary>
        internal const long DefaultMinWork = 400000;
        /// <summary> a task holds at most 1/divisor of the total work (and more adjacent subtrees are joined up to that) </summary>
        internal const int DefaultDivisor = 32;

        /// <summary> task t = supernodes [TaskFirst[t], TaskEnd[t]) </summary>
        public int[] TaskFirst, TaskEnd;
        /// <summary> task of each supernode, -1 for a top supernode </summary>
        public int[] TaskOf;
        /// <summary> rows [0, Split[J]) of supernode J (all of them for a top supernode) lie in its task, rows [Split[J], h) above it </summary>
        public int[] Split;
        /// <summary> the updates for the rows [Split[J], h) of J are at BufOff[J] ... in the buffer </summary>
        public int[] BufOff;
        public int BufSize;

        /// <summary> the schedule, or null if the work is below minWork or fewer than two tasks come out (the solve runs serially) </summary>
        public static CholeskySolveSchedule Build(int snCount, int[] snFirst, int[] snRowPtr, int[] snRows, int[] colSnode, long minWork, int divisor)
        {
            if (snCount < 2 || divisor < 2) return null;
            var own = new long[snCount]; var parent = new int[snCount];
            for (int J = 0; J < snCount; ++J) {
                int w = snFirst[J + 1] - snFirst[J], h = snRowPtr[J + 1] - snRowPtr[J];
                own[J] = (long)w * h + 32;
                parent[J] = h > w ? colSnode[snRows[snRowPtr[J] + w]] : -1;
            }
            List<int> first, end;
            if (!Partition(snCount, own, parent, minWork, divisor, out first, out end)) return null;
            var s = new CholeskySolveSchedule();
            s.TaskFirst = first.ToArray(); s.TaskEnd = end.ToArray();
            s.TaskOf = new int[snCount]; s.Split = new int[snCount]; s.BufOff = new int[snCount];
            for (int J = 0; J < snCount; ++J) {
                s.TaskOf[J] = -1; s.Split[J] = snRowPtr[J + 1] - snRowPtr[J];
            }
            int buf = 0;
            for (int t = 0; t < first.Count; ++t) {
                int c1 = snFirst[end[t]];
                for (int J = first[t]; J < end[t]; ++J) {
                    s.TaskOf[J] = t;
                    int rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, i = snFirst[J + 1] - snFirst[J];
                    while (i < h && snRows[rp + i] < c1) ++i;
                    s.Split[J] = i; s.BufOff[J] = buf; buf += h - i;
                }
            }
            s.BufSize = buf;
            return s;
        }

        /// <summary>
        /// Split the supernode tree (postorder, parent[J] > J or -1, own[J] the work of J) into tasks: maximal subtrees of at most
        /// total/divisor work, joined while adjacent (no other supernode between them) and within that bound. False if the total
        /// work is below minWork or fewer than two tasks come out.
        /// </summary>
        internal static bool Partition(int snCount, long[] own, int[] parent, long minWork, int divisor, out List<int> first, out List<int> end)
        {
            first = new List<int>(); end = new List<int>();
            var sub = (long[])own.Clone(); var fd = new int[snCount];
            long total = 0;
            for (int J = 0; J < snCount; ++J) { fd[J] = J; total += own[J]; }
            if (total < minWork) return false;
            for (int J = 0; J < snCount; ++J) {
                int p = parent[J];
                if (p >= 0) { sub[p] += sub[J]; if (fd[J] < fd[p]) fd[p] = fd[J]; }
            }
            long target = Math.Max(total / divisor, 1), curW = 0;
            for (int J = 0; J < snCount; ++J) {
                if (sub[J] > target || (parent[J] >= 0 && sub[parent[J]] <= target)) continue;
                if (end.Count > 0 && end[end.Count - 1] == fd[J] && curW + sub[J] <= target) {
                    end[end.Count - 1] = J + 1; curW += sub[J];
                } else {
                    first.Add(fd[J]); end.Add(J + 1); curW = sub[J];
                }
            }
            return first.Count >= 2;
        }
    }
}
