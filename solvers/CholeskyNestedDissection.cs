// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Fill-reducing ordering strategy used by <see cref="SparseCholesky"/> and <see cref="SparseHermitianCholesky"/>.
    /// </summary>
    public enum CholeskyOrdering
    {
        /// <summary>
        /// Approximate minimum degree ordering on the quotient graph (default; preserves historical bit-identical factors).
        /// </summary>
        MinimumDegree = 0,

        /// <summary>
        /// Deterministic patch-guided nested dissection with multilevel spectral/FM vertex-separator refinement and
        /// halo-aware local minimum degree on leaves. Typically faster to compute with lower fill-in and a more
        /// balanced elimination tree on medium-to-large 2D/3D mesh systems.
        /// </summary>
        NestedDissection = 1
    }

    /// <summary>
    /// Deterministic patch-guided nested dissection ordering for the symmetric block graph of <see cref="CholeskyAnalysis"/>.
    ///
    /// Written from published papers (no external code ported):
    /// - A. George, "Nested dissection of a regular finite element mesh", SIAM J. Numer. Anal. 1973.
    /// - A. Pothen, H. D. Simon, K.-P. Liou, "Partitioning sparse matrices with eigenvectors of graphs",
    ///   SIAM J. Matrix Anal. Appl. 1990 (Fiedler spectral bisection on the tiny coarsest graph).
    /// - G. Karypis, V. Kumar, "A fast and high quality multilevel scheme for partitioning irregular graphs",
    ///   SIAM J. Sci. Comput. 1998 (heavy-edge matching coarsening, greedy graph growing, FM edge/node refinement).
    /// - C. Ashcraft, J. W. H. Liu, "Robust ordering of sparse matrices using multisection", SIAM J. Matrix Anal. Appl. 1998
    ///   (bipartite minimum vertex cover separator extraction and halo-aware constrained minimum degree on leaves).
    /// - J. E. Hopcroft, R. M. Karp, "An n^(5/2) algorithm for maximum matchings in bipartite graphs", SIAM J. Comput. 1973.
    /// - B. Zarebavani et al., "Fast Sparse Matrix Permutation for Mesh-Based Direct Solvers", SIGGRAPH 2026
    ///   (single-coarsening greedy patch quotient graph for nested dissection + parallel independent subtrees).
    /// </summary>
    internal static class CholeskyNestedDissection
    {
        // Leaf size below which halo-aware local AMD orders the subgraph directly.
        internal const int LeafThreshold = 128;
        // Target patch size for single-coarsening patch quotient graph.
        internal const int PatchSize = 32;
        // Minimum subgraph size to use the precomputed patch quotient graph before falling back to fine multilevel HEM.
        internal const int PatchUseThreshold = 160;

        // Deterministic indexed max-heap on keys (gain desc, id asc), storing vertex ids in [0, capacity).
        struct MaxGainHeap
        {
            int[] heap;
            int[] pos;
            int[] gain;
            public int Count;

            public MaxGainHeap(int capacity)
            {
                heap = new int[capacity];
                pos = new int[capacity];
                gain = new int[capacity];
                for (int i = 0; i < capacity; ++i) pos[i] = -1;
                Count = 0;
            }

            public void Clear()
            {
                for (int i = 0; i < Count; ++i) pos[heap[i]] = -1;
                Count = 0;
            }

            public int TopGain() { return gain[heap[0]]; }
            public int TopVertex() { return heap[0]; }

            bool Better(int a, int b)
            {
                int ga = gain[a], gb = gain[b];
                return ga > gb || (ga == gb && a < b);
            }

            public void PushOrUpdate(int v, int g)
            {
                int p = pos[v];
                if (p < 0) {
                    gain[v] = g;
                    p = Count++;
                    heap[p] = v;
                    pos[v] = p;
                    SiftUp(p);
                } else {
                    int old = gain[v];
                    gain[v] = g;
                    if (g > old) SiftUp(p);
                    else if (g < old) SiftDown(p);
                }
            }

            public void Remove(int v)
            {
                int p = pos[v];
                if (p < 0) return;
                pos[v] = -1;
                int last = heap[--Count];
                if (p < Count) {
                    heap[p] = last;
                    pos[last] = p;
                    SiftUp(p);
                    SiftDown(pos[last]);
                }
            }

            public int Pop()
            {
                int top = heap[0];
                pos[top] = -1;
                int last = heap[--Count];
                if (Count > 0) {
                    heap[0] = last;
                    pos[last] = 0;
                    SiftDown(0);
                }
                return top;
            }

            void SiftUp(int p)
            {
                int v = heap[p];
                while (p > 0) {
                    int parent = (p - 1) >> 1;
                    int pv = heap[parent];
                    if (!Better(v, pv)) break;
                    heap[p] = pv;
                    pos[pv] = p;
                    p = parent;
                }
                heap[p] = v;
                pos[v] = p;
            }

            void SiftDown(int p)
            {
                int v = heap[p];
                while (true) {
                    int c = (p << 1) + 1;
                    if (c >= Count) break;
                    if (c + 1 < Count && Better(heap[c + 1], heap[c])) c++;
                    int cv = heap[c];
                    if (!Better(cv, v)) break;
                    heap[p] = cv;
                    pos[cv] = p;
                    p = c;
                }
                heap[p] = v;
                pos[v] = p;
            }
        }

        // Sparse-key indexed max-heap backed by reusable O(nv) pos[] array (maintained at -1 between uses).
        struct SparseMaxGainHeap
        {
            int[] heap;
            int[] gain;
            int[] pos;
            public int Count;

            public SparseMaxGainHeap(int[] posBacking, int initialCap)
            {
                pos = posBacking;
                heap = new int[Math.Max(32, initialCap)];
                gain = new int[Math.Max(32, initialCap)];
                Count = 0;
            }

            public void Clear()
            {
                for (int i = 0; i < Count; ++i) pos[heap[i]] = -1;
                Count = 0;
            }

            public int TopGain() { return gain[0]; }
            public int TopVertex() { return heap[0]; }

            void EnsureCapacity(int need)
            {
                if (need <= heap.Length) return;
                int nc = Math.Max(need, heap.Length * 2);
                Array.Resize(ref heap, nc);
                Array.Resize(ref gain, nc);
            }

            public void PushOrUpdate(int v, int g)
            {
                int p = pos[v];
                if (p < 0) {
                    p = Count++;
                    EnsureCapacity(Count);
                    heap[p] = v;
                    gain[p] = g;
                    pos[v] = p;
                    SiftUp(p);
                } else {
                    int old = gain[p];
                    gain[p] = g;
                    if (g > old) SiftUp(p);
                    else if (g < old) SiftDown(p);
                }
            }

            public void Remove(int v)
            {
                int p = pos[v];
                if (p < 0) return;
                pos[v] = -1;
                int lastIdx = --Count;
                if (p < lastIdx) {
                    int lv = heap[lastIdx];
                    int lg = gain[lastIdx];
                    heap[p] = lv;
                    gain[p] = lg;
                    pos[lv] = p;
                    SiftUp(p);
                    SiftDown(pos[lv]);
                }
            }

            public int Pop()
            {
                int top = heap[0];
                pos[top] = -1;
                int lastIdx = --Count;
                if (Count > 0) {
                    int lv = heap[lastIdx];
                    heap[0] = lv;
                    gain[0] = gain[lastIdx];
                    pos[lv] = 0;
                    SiftDown(0);
                }
                return top;
            }

            void SiftUp(int p)
            {
                int v = heap[p], gv = gain[p];
                while (p > 0) {
                    int parent = (p - 1) >> 1;
                    int pv = heap[parent], gp = gain[parent];
                    if (gv < gp || (gv == gp && v > pv)) break;
                    heap[p] = pv;
                    gain[p] = gp;
                    pos[pv] = p;
                    p = parent;
                }
                heap[p] = v;
                gain[p] = gv;
                pos[v] = p;
            }

            void SiftDown(int p)
            {
                int v = heap[p], gv = gain[p];
                while (true) {
                    int c = (p << 1) + 1;
                    if (c >= Count) break;
                    if (c + 1 < Count) {
                        int g0 = gain[c], g1 = gain[c + 1];
                        if (g1 > g0 || (g1 == g0 && heap[c + 1] < heap[c])) c++;
                    }
                    int cv = heap[c], gc = gain[c];
                    if (gc < gv || (gc == gv && cv > v)) break;
                    heap[p] = cv;
                    gain[p] = gc;
                    pos[cv] = p;
                    p = c;
                }
                heap[p] = v;
                gain[p] = gv;
                pos[v] = p;
            }
        }

        sealed class Subproblem
        {
            public int Id;
            public int[] Verts;
            public int OutPos;
        }

        sealed class LevelResult
        {
            public List<Subproblem> NextSubproblems;
            public List<Subproblem> NewLeaves;
        }

        // Reusable per-worker workspace for BisectSubproblem to eliminate per-subproblem O(nv) allocations.
        sealed class BisectWorkspace
        {
            public int[] Lxadj = new int[512];
            public int[] Ladj = new int[4096];
            public int[] ExtDeg = new int[512];
            public int[] CompId = new int[512];
            public int[] BfsQ = new int[512];
            public int[] Adj0 = new int[512];
            public int[] Adj1 = new int[512];
            public int[] LockStamp = new int[512];
            public int[] SepMark = new int[512];
            public int[] Pos0 = new int[512];
            public int[] Pos1 = new int[512];
            public int[] BIdx = new int[512];
            public byte[] Where = new byte[512];
            public int[] G2L = new int[0];
            public int[] G2LStamp = new int[0];
            public int G2LCurStamp = 0;
            public int Stamp = 0;

            public BisectWorkspace()
            {
                for (int i = 0; i < Pos0.Length; ++i) Pos0[i] = Pos1[i] = BIdx[i] = -1;
            }

            public void EnsureNv(int nv)
            {
                if (nv + 1 > Lxadj.Length) {
                    int cap = Math.Max(nv + 1, Lxadj.Length * 2);
                    Lxadj = new int[cap];
                }
                if (nv > ExtDeg.Length) {
                    int cap = Math.Max(nv, ExtDeg.Length * 2);
                    ExtDeg = new int[cap];
                    CompId = new int[cap];
                    BfsQ = new int[cap];
                    Adj0 = new int[cap];
                    Adj1 = new int[cap];
                    LockStamp = new int[cap];
                    SepMark = new int[cap];
                    Pos0 = new int[cap];
                    Pos1 = new int[cap];
                    BIdx = new int[cap];
                    Where = new byte[cap];
                    for (int i = 0; i < cap; ++i) Pos0[i] = Pos1[i] = BIdx[i] = -1;
                    Stamp = 0;
                }
            }

            public void EnsureEdges(int ne)
            {
                if (ne > Ladj.Length)
                    Ladj = new int[Math.Max(ne, Ladj.Length * 2)];
            }

            public void EnsureGlobalPatches(int numGlobalPatches)
            {
                if (numGlobalPatches > G2L.Length) {
                    G2L = new int[numGlobalPatches];
                    G2LStamp = new int[numGlobalPatches];
                    G2LCurStamp = 0;
                }
            }

            public int NextStamp()
            {
                if (++Stamp == int.MaxValue) {
                    Array.Clear(LockStamp, 0, LockStamp.Length);
                    Array.Clear(SepMark, 0, SepMark.Length);
                    Stamp = 1;
                }
                return Stamp;
            }
        }

        // Reusable per-worker workspace for flat-array halo-aware local AMD (zero per-leaf List<int> allocations).
        sealed class LeafWorkspace
        {
            public int[] HaloBuf = new int[256];
            public int[] AStart = new int[512];
            public int[] ALen = new int[512];
            public int[] AAdj = new int[4096];
            public int[] EHead = new int[512];
            public int[] EElem = new int[4096];
            public int[] ENext = new int[4096];
            public int[] LStart = new int[512];
            public int[] LLen = new int[512];
            public int[] LData = new int[8192];
            public bool[] Eliminated = new bool[512];
            public bool[] Absorbed = new bool[512];
            public int[] Degree = new int[512];
            public int[] BucketHead = new int[513];
            public int[] BNext = new int[512];
            public int[] BPrev = new int[512];
            public int[] Mark = new int[512];
            public int[] WFlag = new int[512];
            public int[] WVal = new int[512];

            public void EnsureNodeCapacity(int nv, int total)
            {
                if (total > EHead.Length) {
                    int cap = Math.Max(total, EHead.Length * 2);
                    EHead = new int[cap];
                    Eliminated = new bool[cap];
                    Mark = new int[cap];
                }
                if (total + 1 > BucketHead.Length)
                    BucketHead = new int[Math.Max(total + 1, BucketHead.Length * 2)];
                if (nv > AStart.Length) {
                    int cap = Math.Max(nv, AStart.Length * 2);
                    AStart = new int[cap];
                    ALen = new int[cap];
                    LStart = new int[cap];
                    LLen = new int[cap];
                    Absorbed = new bool[cap];
                    Degree = new int[cap];
                    BNext = new int[cap];
                    BPrev = new int[cap];
                    WFlag = new int[cap];
                    WVal = new int[cap];
                }
            }
        }

        /// <summary>
        /// Computes a deterministic nested-dissection block permutation of length n / blockSize.
        /// </summary>
        public static int[] Order(int n, int blockSize, int[] aColPtr, int[] aRow)
        {
            int nb = n / blockSize;
            int[] blockOrder = new int[nb];
            if (nb == 0) return blockOrder;
            if (nb == 1) { blockOrder[0] = 0; return blockOrder; }

            BuildBlockCsr(n, blockSize, aColPtr, aRow, out int[] xadj, out int[] adjncy);

            if (nb <= LeafThreshold) {
                int[] allVerts = new int[nb];
                int[] subId = new int[nb];
                int[] localId = new int[nb];
                for (int i = 0; i < nb; ++i) { allVerts[i] = i; subId[i] = 1; localId[i] = i; }
                OrderLeafHaloAmd(xadj, adjncy, subId, localId, 1, allVerts, blockOrder, 0, new LeafWorkspace());
                return blockOrder;
            }

            // Decompose into connected components in deterministic ascending min-vertex order.
            int[] compVisited = new int[nb];
            int[] queue = new int[nb];
            var frontier = new List<Subproblem>();
            var leaves = new List<Subproblem>();
            int nextSubId = 1;
            int outCursor = 0;

            for (int start = 0; start < nb; ++start) {
                if (compVisited[start] != 0) continue;
                if (xadj[start] == xadj[start + 1]) {
                    compVisited[start] = 1;
                    blockOrder[outCursor++] = start;
                    continue;
                }
                int qh = 0, qt = 0;
                compVisited[start] = 1;
                queue[qt++] = start;
                while (qh < qt) {
                    int u = queue[qh++];
                    for (int e = xadj[u]; e < xadj[u + 1]; ++e) {
                        int v = adjncy[e];
                        if (compVisited[v] == 0) {
                            compVisited[v] = 1;
                            queue[qt++] = v;
                        }
                    }
                }
                int[] verts = new int[qt];
                if (qt == nb) {
                    for (int i = 0; i < nb; ++i) verts[i] = i;
                } else {
                    Array.Copy(queue, 0, verts, 0, qt);
                    Array.Sort(verts);
                }
                var sub = new Subproblem { Id = nextSubId++, Verts = verts, OutPos = outCursor };
                outCursor += qt;
                if (qt <= LeafThreshold) leaves.Add(sub);
                else frontier.Add(sub);
                if (qt == nb) break;
            }

            int[] patchOf = null;
            int numGlobalPatches = 0;
            if (frontier.Count > 0) {
                int maxComp = 0;
                for (int i = 0; i < frontier.Count; ++i)
                    if (frontier[i].Verts.Length > maxComp) maxComp = frontier[i].Verts.Length;
                if (maxComp >= PatchUseThreshold)
                    patchOf = BuildGreedyPatches(nb, xadj, adjncy, PatchSize, out numGlobalPatches);
            }

            int[] subIdMap = new int[nb];
            int[] localIdMap = new int[nb];
            var singleWs = new BisectWorkspace();

            while (frontier.Count > 0) {
                int fCount = frontier.Count;
                if (fCount == 1) {
                    StampSubproblem(frontier[0], subIdMap, localIdMap);
                } else {
                    gParallel.ForRange(fCount, i => StampSubproblem(frontier[i], subIdMap, localIdMap));
                }

                var results = new LevelResult[fCount];
                if (fCount == 1) {
                    results[0] = BisectSubproblem(xadj, adjncy, patchOf, numGlobalPatches, subIdMap, localIdMap, frontier[0], blockOrder, singleWs);
                } else {
                    gParallel.ForRange(fCount, () => new BisectWorkspace(), (i, ws) => {
                        results[i] = BisectSubproblem(xadj, adjncy, patchOf, numGlobalPatches, subIdMap, localIdMap, frontier[i], blockOrder, ws);
                    });
                }

                var nextFrontier = new List<Subproblem>();
                for (int i = 0; i < fCount; ++i) {
                    var res = results[i];
                    if (res.NextSubproblems != null) {
                        for (int k = 0; k < res.NextSubproblems.Count; ++k) {
                            var child = res.NextSubproblems[k];
                            child.Id = nextSubId++;
                            nextFrontier.Add(child);
                        }
                    }
                    if (res.NewLeaves != null) {
                        for (int k = 0; k < res.NewLeaves.Count; ++k) {
                            var leaf = res.NewLeaves[k];
                            leaf.Id = nextSubId++;
                            leaves.Add(leaf);
                        }
                    }
                }
                frontier = nextFrontier;
            }
            singleWs = null;

            if (leaves.Count > 0) {
                int lCount = leaves.Count;
                if (lCount == 1) {
                    StampSubproblem(leaves[0], subIdMap, localIdMap);
                    OrderLeafHaloAmd(xadj, adjncy, subIdMap, localIdMap, leaves[0].Id, leaves[0].Verts, blockOrder, leaves[0].OutPos, new LeafWorkspace());
                } else {
                    gParallel.ForRange(lCount, i => StampSubproblem(leaves[i], subIdMap, localIdMap));
                    gParallel.ForRange(lCount, () => new LeafWorkspace(), (i, ws) => {
                        var leaf = leaves[i];
                        OrderLeafHaloAmd(xadj, adjncy, subIdMap, localIdMap, leaf.Id, leaf.Verts, blockOrder, leaf.OutPos, ws);
                    });
                }
            }

            return blockOrder;
        }

        static void StampSubproblem(Subproblem sub, int[] subIdMap, int[] localIdMap)
        {
            int id = sub.Id;
            int[] verts = sub.Verts;
            for (int k = 0; k < verts.Length; ++k) {
                int v = verts[k];
                subIdMap[v] = id;
                localIdMap[v] = k;
            }
        }

        // Builds symmetric undirected CSR block graph with sorted, duplicate-free neighbor lists and no self-loops.
        static void BuildBlockCsr(int n, int B, int[] aColPtr, int[] aRow, out int[] xadj, out int[] adjncy)
        {
            int nb = n / B;
            xadj = new int[nb + 1];

            if (B == 1) {
                for (int c = 0; c < n; ++c) {
                    for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                        int r = aRow[q];
                        if (r > c) {
                            xadj[c + 1]++;
                            xadj[r + 1]++;
                        }
                    }
                }
                for (int i = 0; i < nb; ++i) xadj[i + 1] += xadj[i];
                adjncy = new int[xadj[nb]];
                int[] next = (int[])xadj.Clone();
                for (int c = 0; c < n; ++c) {
                    for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                        int r = aRow[q];
                        if (r > c) {
                            adjncy[next[c]++] = r;
                            adjncy[next[r]++] = c;
                        }
                    }
                }
                return;
            }

            // General B > 1: since aRow[q] >= c, bi != bj implies bi > bj. Deduplicate per block bj in O(nnz(A)).
            int[] mark = new int[nb];
            for (int i = 0; i < nb; ++i) mark[i] = -1;
            for (int bj = 0; bj < nb; ++bj) {
                int c0 = bj * B, c1 = c0 + B;
                for (int c = c0; c < c1; ++c) {
                    for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                        int bi = aRow[q] / B;
                        if (bi > bj && mark[bi] != bj) {
                            mark[bi] = bj;
                            xadj[bj + 1]++;
                            xadj[bi + 1]++;
                        }
                    }
                }
            }
            for (int i = 0; i < nb; ++i) xadj[i + 1] += xadj[i];
            adjncy = new int[xadj[nb]];
            int[] nextB = (int[])xadj.Clone();
            for (int i = 0; i < nb; ++i) mark[i] = -1;
            for (int bj = 0; bj < nb; ++bj) {
                int c0 = bj * B, c1 = c0 + B;
                int upperStart = nextB[bj];
                for (int c = c0; c < c1; ++c) {
                    for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                        int bi = aRow[q] / B;
                        if (bi > bj && mark[bi] != bj) {
                            mark[bi] = bj;
                            adjncy[nextB[bj]++] = bi;
                            adjncy[nextB[bi]++] = bj;
                        }
                    }
                }
                int upperLen = nextB[bj] - upperStart;
                if (upperLen > 1) {
                    if (upperLen <= 32) {
                        for (int i = upperStart + 1; i < upperStart + upperLen; ++i) {
                            int v = adjncy[i], j = i - 1;
                            while (j >= upperStart && adjncy[j] > v) { adjncy[j + 1] = adjncy[j]; j--; }
                            adjncy[j + 1] = v;
                        }
                    } else {
                        Array.Sort(adjncy, upperStart, upperLen);
                    }
                }
            }
        }

        // Single-coarsening deterministic greedy BFS patching (inspired by Zarebavani et al., SIGGRAPH 2026).
        static int[] BuildGreedyPatches(int nb, int[] xadj, int[] adjncy, int targetSize, out int numPatches)
        {
            int[] patchOf = new int[nb];
            for (int i = 0; i < nb; ++i) patchOf[i] = -1;

            int[] bfsQ = new int[targetSize + 8];
            int[] frontierQ = new int[nb];
            bool[] inFrontier = new bool[nb];
            int fh = 0, ft = 0;
            int nextUnassigned = 0;

            var patchSizes = new List<int>(nb / targetSize + 16);
            var patchMembersPtr = new List<int>(nb / targetSize + 16);
            int[] patchMembers = new int[nb];
            int memberCursor = 0;

            while (true) {
                int seed = -1;
                while (fh < ft) {
                    int cand = frontierQ[fh++];
                    if (patchOf[cand] == -1) { seed = cand; break; }
                }
                if (seed < 0) {
                    while (nextUnassigned < nb && patchOf[nextUnassigned] != -1) nextUnassigned++;
                    if (nextUnassigned >= nb) break;
                    seed = nextUnassigned++;
                }

                int pid = patchSizes.Count;
                patchMembersPtr.Add(memberCursor);
                int qh = 0, qt = 0;
                patchOf[seed] = pid;
                bfsQ[qt++] = seed;
                patchMembers[memberCursor++] = seed;

                while (qh < qt && qt < targetSize) {
                    int u = bfsQ[qh++];
                    for (int e = xadj[u]; e < xadj[u + 1]; ++e) {
                        int v = adjncy[e];
                        if (patchOf[v] == -1) {
                            patchOf[v] = pid;
                            bfsQ[qt++] = v;
                            patchMembers[memberCursor++] = v;
                            if (qt == targetSize) break;
                        }
                    }
                }
                for (int i = 0; i < qt; ++i) {
                    int u = bfsQ[i];
                    for (int e = xadj[u]; e < xadj[u + 1]; ++e) {
                        int v = adjncy[e];
                        if (patchOf[v] == -1 && !inFrontier[v]) {
                            inFrontier[v] = true;
                            frontierQ[ft++] = v;
                        }
                    }
                }
                patchSizes.Add(qt);
            }
            patchMembersPtr.Add(memberCursor);

            int rawPatches = patchSizes.Count;
            int minKeep = Math.Max(4, targetSize / 3);
            int[] nbrCount = new int[rawPatches];
            int[] nbrMark = new int[rawPatches];
            int[] touchedNbrs = new int[64];
            for (int p = 0; p < rawPatches; ++p) nbrMark[p] = -1;

            for (int pid = 0; pid < rawPatches; ++pid) {
                int sz = patchSizes[pid];
                if (sz == 0 || sz >= minKeep) continue;
                int pStart = patchMembersPtr[pid], pEnd = patchMembersPtr[pid + 1];
                int nTouched = 0;
                for (int t = pStart; t < pEnd; ++t) {
                    int u = patchMembers[t];
                    if (patchOf[u] != pid) continue;
                    for (int e = xadj[u]; e < xadj[u + 1]; ++e) {
                        int np = patchOf[adjncy[e]];
                        if (np == pid) continue;
                        if (nbrMark[np] != pid) {
                            nbrMark[np] = pid;
                            nbrCount[np] = 1;
                            if (nTouched == touchedNbrs.Length) Array.Resize(ref touchedNbrs, touchedNbrs.Length * 2);
                            touchedNbrs[nTouched++] = np;
                        } else {
                            nbrCount[np]++;
                        }
                    }
                }
                int bestNbr = -1, bestShared = -1, bestSize = int.MaxValue;
                for (int t = 0; t < nTouched; ++t) {
                    int np = touchedNbrs[t];
                    int sh = nbrCount[np], nsz = patchSizes[np];
                    if (sh > bestShared || (sh == bestShared && (nsz < bestSize || (nsz == bestSize && np < bestNbr)))) {
                        bestShared = sh; bestSize = nsz; bestNbr = np;
                    }
                }
                if (bestNbr >= 0) {
                    for (int t = pStart; t < pEnd; ++t) {
                        int u = patchMembers[t];
                        if (patchOf[u] == pid) patchOf[u] = bestNbr;
                    }
                    patchSizes[bestNbr] += sz;
                    patchSizes[pid] = 0;
                }
            }

            int[] remap = new int[rawPatches];
            numPatches = 0;
            for (int p = 0; p < rawPatches; ++p)
                remap[p] = patchSizes[p] > 0 ? numPatches++ : -1;
            for (int i = 0; i < nb; ++i)
                patchOf[i] = remap[patchOf[i]];
            return patchOf;
        }

        // Processes one ND subproblem using the reusable thread-local BisectWorkspace.
        static LevelResult BisectSubproblem(
            int[] xadj, int[] adjncy, int[] patchOf, int numGlobalPatches,
            int[] subIdMap, int[] localIdMap,
            Subproblem sub, int[] blockOrder, BisectWorkspace ws)
        {
            var result = new LevelResult();
            int id = sub.Id;
            int[] verts = sub.Verts;
            int nv = verts.Length;

            ws.EnsureNv(nv);
            int[] lxadj = ws.Lxadj;
            int[] extDeg = ws.ExtDeg;

            lxadj[0] = 0;
            for (int i = 0; i < nv; ++i) {
                int gv = verts[i];
                int inDeg = 0, outDeg = 0;
                for (int e = xadj[gv]; e < xadj[gv + 1]; ++e) {
                    if (subIdMap[adjncy[e]] == id) inDeg++;
                    else outDeg++;
                }
                lxadj[i + 1] = lxadj[i] + inDeg;
                extDeg[i] = outDeg;
            }
            int ne = lxadj[nv];
            ws.EnsureEdges(ne);
            int[] ladj = ws.Ladj;
            for (int i = 0; i < nv; ++i) {
                int gv = verts[i];
                int w = lxadj[i];
                for (int e = xadj[gv]; e < xadj[gv + 1]; ++e) {
                    int gu = adjncy[e];
                    if (subIdMap[gu] == id) ladj[w++] = localIdMap[gu];
                }
            }

            // Check connectivity: if disconnected, split cleanly into components without adding a separator.
            int[] compId = ws.CompId;
            int[] bfsQ = ws.BfsQ;
            Array.Clear(compId, 0, nv);
            int numComps = 0;
            var compSizes = new List<int>(4);
            for (int i = 0; i < nv; ++i) {
                if (compId[i] != 0) continue;
                numComps++;
                int cid = numComps;
                int qh = 0, qt = 0;
                compId[i] = cid;
                bfsQ[qt++] = i;
                while (qh < qt) {
                    int u = bfsQ[qh++];
                    for (int e = lxadj[u]; e < lxadj[u + 1]; ++e) {
                        int v = ladj[e];
                        if (compId[v] == 0) { compId[v] = cid; bfsQ[qt++] = v; }
                    }
                }
                compSizes.Add(qt);
                if (qt == nv) break;
            }

            if (numComps > 1) {
                int[][] compVerts = new int[numComps][];
                int[] compWrite = new int[numComps];
                for (int c = 0; c < numComps; ++c) compVerts[c] = new int[compSizes[c]];
                for (int i = 0; i < nv; ++i) {
                    int c = compId[i] - 1;
                    compVerts[c][compWrite[c]++] = verts[i];
                }
                int cursor = sub.OutPos;
                for (int c = 0; c < numComps; ++c) {
                    int sz = compSizes[c];
                    AddChild(result, compVerts[c], cursor, blockOrder);
                    cursor += sz;
                }
                return result;
            }

            // Connected subgraph with nv > LeafThreshold: compute edge-cut bipartition into ws.Where[0..nv).
            bool highQuality = nv >= 1024;
            byte[] side = ws.Where;
            int[] bndCandidates = null;
            int bndCandCount = 0;
            bool usedPatches = false;
            if (patchOf != null && nv >= PatchUseThreshold)
                usedPatches = BipartitionViaPatches(nv, verts, lxadj, ladj, patchOf, numGlobalPatches, side, highQuality, ws, out bndCandidates, out bndCandCount);
            if (!usedPatches) {
                int[] unitW = new int[nv];
                for (int i = 0; i < nv; ++i) unitW[i] = 1;
                int[] unitEw = new int[ne];
                for (int e = 0; e < ne; ++e) unitEw[e] = 1;
                byte[] fineSide = MultilevelBipartition(nv, lxadj, ladj, unitW, unitEw, highQuality);
                Array.Copy(fineSide, 0, side, 0, nv);
            }

            // Extract minimum-cardinality vertex separator S from the cut edges via Hopcroft-Karp + Konig's theorem.
            ExtractMinVertexSeparator(nv, lxadj, ladj, side, bndCandidates, bndCandCount, ws,
                out int w0, out int w1, out int wS, out int[] sepInit);

            // Refine S directly on the fine graph via O(|S|)-pass two-sided FM vertex-separator refinement.
            int fmPasses = highQuality ? 4 : 2;
            if (wS > 0 && (w0 > 0 || w1 > 0))
                RefineVertexSeparatorFM(nv, lxadj, ladj, side, sepInit, fmPasses, ws, ref w0, ref w1, ref wS);

            if (wS == 0 || wS == nv || ((w0 == 0 || w1 == 0) && wS >= nv / 2) ||
                (nv <= 512 && wS > (int)(((long)nv * 3) / 5)) || wS > (int)(((long)nv * 3) / 4)) {
                if (result.NewLeaves == null) result.NewLeaves = new List<Subproblem>();
                result.NewLeaves.Add(new Subproblem { Verts = verts, OutPos = sub.OutPos });
                return result;
            }

            int[] vertsA = w0 > 0 ? new int[w0] : null;
            int[] vertsB = w1 > 0 ? new int[w1] : null;
            int[] sepLocal = new int[wS];
            int ia = 0, ib = 0, isep = 0;
            for (int i = 0; i < nv; ++i) {
                byte wh = side[i];
                if (wh == 0) vertsA[ia++] = verts[i];
                else if (wh == 1) vertsB[ib++] = verts[i];
                else sepLocal[isep++] = i;
            }

            if (wS > 1) {
                Array.Sort(sepLocal, (a, b) => {
                    int c = extDeg[a].CompareTo(extDeg[b]);
                    return c != 0 ? c : verts[a].CompareTo(verts[b]);
                });
            }
            int sepPos = sub.OutPos + w0 + w1;
            for (int k = 0; k < wS; ++k)
                blockOrder[sepPos + k] = verts[sepLocal[k]];

            if (w0 > 0) AddChild(result, vertsA, sub.OutPos, blockOrder);
            if (w1 > 0) AddChild(result, vertsB, sub.OutPos + w0, blockOrder);
            return result;
        }

        static void AddChild(LevelResult result, int[] verts, int outPos, int[] blockOrder)
        {
            if (verts.Length == 1) {
                blockOrder[outPos] = verts[0];
            } else if (verts.Length <= LeafThreshold) {
                if (result.NewLeaves == null) result.NewLeaves = new List<Subproblem>();
                result.NewLeaves.Add(new Subproblem { Verts = verts, OutPos = outPos });
            } else {
                if (result.NextSubproblems == null) result.NextSubproblems = new List<Subproblem>();
                result.NextSubproblems.Add(new Subproblem { Verts = verts, OutPos = outPos });
            }
        }

        // Induces the weighted patch quotient graph in O(nv + |E_sub|) using a timestamped O(1) remap table,
        // bipartitions it with multilevel HEM + spectral/GGG + FM, lifts back to fine vertices, and emits only
        // vertices inside boundary patches (skipping ~85-90% of interior vertices for downstream separator steps).
        static bool BipartitionViaPatches(
            int nv, int[] verts, int[] lxadj, int[] ladj, int[] patchOf, int numGlobalPatches,
            byte[] sideOut, bool highQuality, BisectWorkspace ws,
            out int[] bndCandidates, out int bndCandCount)
        {
            bndCandidates = null;
            bndCandCount = 0;

            ws.EnsureGlobalPatches(numGlobalPatches);
            int[] g2l = ws.G2L;
            int[] g2lStamp = ws.G2LStamp;
            if (++ws.G2LCurStamp == int.MaxValue) {
                Array.Clear(g2lStamp, 0, numGlobalPatches);
                ws.G2LCurStamp = 1;
            }
            int curStamp = ws.G2LCurStamp;

            int[] v2p = ws.Adj0; // reuse workspace array (length >= nv)
            int np = 0;
            for (int i = 0; i < nv; ++i) {
                int gp = patchOf[verts[i]];
                int lp;
                if (g2lStamp[gp] != curStamp) {
                    g2lStamp[gp] = curStamp;
                    lp = np++;
                    g2l[gp] = lp;
                } else {
                    lp = g2l[gp];
                }
                v2p[i] = lp;
            }
            if (np < 6) return false;

            int[] pw = new int[np];
            for (int i = 0; i < nv; ++i) pw[v2p[i]]++;

            int[] pOff = new int[np + 1];
            for (int i = 0; i < np; ++i) pOff[i + 1] = pOff[i] + pw[i];
            int[] pNext = (int[])pOff.Clone();
            int[] pVerts = ws.Adj1; // reuse workspace array (length >= nv)
            for (int i = 0; i < nv; ++i) pVerts[pNext[v2p[i]]++] = i;

            int[] pxadj = new int[np + 1];
            var padjList = new List<int>(np * 6);
            var pewList = new List<int>(np * 6);
            int[] mark = new int[np];
            int[] slot = new int[np];
            for (int i = 0; i < np; ++i) mark[i] = -1;

            for (int p = 0; p < np; ++p) {
                pxadj[p] = padjList.Count;
                int start = padjList.Count;
                for (int t = pOff[p]; t < pOff[p + 1]; ++t) {
                    int u = pVerts[t];
                    for (int e = lxadj[u]; e < lxadj[u + 1]; ++e) {
                        int q = v2p[ladj[e]];
                        if (q == p) continue;
                        if (mark[q] != p) {
                            mark[q] = p;
                            slot[q] = padjList.Count;
                            padjList.Add(q);
                            pewList.Add(1);
                        } else {
                            pewList[slot[q]]++;
                        }
                    }
                }
                int len = padjList.Count - start;
                if (len > 1) SortPairedSlice(padjList, pewList, start, len);
            }
            pxadj[np] = padjList.Count;

            byte[] patchSide = MultilevelBipartition(np, pxadj, padjList.ToArray(), pw, pewList.ToArray(), highQuality);

            // Identify boundary patches (patches with at least one opposite-side neighbor patch).
            bool[] isBndPatch = new bool[np];
            int estBnd = 0;
            for (int p = 0; p < np; ++p) {
                byte ps = patchSide[p];
                for (int e = pxadj[p]; e < pxadj[p + 1]; ++e) {
                    if (patchSide[padjList[e]] != ps) {
                        isBndPatch[p] = true;
                        estBnd += pw[p];
                        break;
                    }
                }
            }

            bndCandidates = new int[Math.Max(1, estBnd)];
            for (int i = 0; i < nv; ++i) {
                int lp = v2p[i];
                sideOut[i] = patchSide[lp];
                if (isBndPatch[lp]) bndCandidates[bndCandCount++] = i;
            }
            return true;
        }

        static void SortPairedSlice(List<int> keys, List<int> vals, int start, int len)
        {
            if (len <= 32) {
                for (int i = start + 1; i < start + len; ++i) {
                    int k = keys[i], v = vals[i];
                    int j = i - 1;
                    while (j >= start && keys[j] > k) {
                        keys[j + 1] = keys[j];
                        vals[j + 1] = vals[j];
                        j--;
                    }
                    keys[j + 1] = k;
                    vals[j + 1] = v;
                }
                return;
            }
            int[] ka = new int[len];
            int[] va = new int[len];
            keys.CopyTo(start, ka, 0, len);
            vals.CopyTo(start, va, 0, len);
            Array.Sort(ka, va, 0, len);
            for (int i = 0; i < len; ++i) {
                keys[start + i] = ka[i];
                vals[start + i] = va[i];
            }
        }

        struct CoarseLevel
        {
            public int N;
            public int[] Xadj, Adj, W, Ew;
            public int[] FineToCoarse;
        }

        static byte[] MultilevelBipartition(int n, int[] xadj, int[] adj, int[] w, int[] ew, bool highQuality)
        {
            int totalW = 0;
            for (int i = 0; i < n; ++i) totalW += w[i];
            int maxCoarseW = Math.Max(2, (int)(((long)totalW * 3) / 25));

            var levels = new List<CoarseLevel>();
            levels.Add(new CoarseLevel { N = n, Xadj = xadj, Adj = adj, W = w, Ew = ew });

            int curN = n;
            int[] curX = xadj, curA = adj, curW = w, curEw = ew;
            int depth = 0;

            while (curN > 48 && depth < 16) {
                int nextN = CoarsenHEM(curN, curX, curA, curW, curEw, maxCoarseW, (depth & 1) == 0,
                    out int[] map, out int[] nx, out int[] na, out int[] nw, out int[] newEw);
                if (nextN >= (int)(((long)curN * 85) / 100) || nextN == curN) break;
                var prev = levels[levels.Count - 1];
                prev.FineToCoarse = map;
                levels[levels.Count - 1] = prev;
                levels.Add(new CoarseLevel { N = nextN, Xadj = nx, Adj = na, W = nw, Ew = newEw });
                curN = nextN; curX = nx; curA = na; curW = nw; curEw = newEw;
                depth++;
            }

            var top = levels[levels.Count - 1];
            byte[] part = InitialBipartitionSpectralAndGGG(top.N, top.Xadj, top.Adj, top.W, top.Ew, highQuality);

            int uncoarsePasses = highQuality ? 2 : 1;
            for (int lv = levels.Count - 2; lv >= 0; --lv) {
                var fine = levels[lv];
                byte[] finePart = new byte[fine.N];
                int[] map = fine.FineToCoarse;
                for (int i = 0; i < fine.N; ++i) finePart[i] = part[map[i]];
                RefineEdgeCutFM(fine.N, fine.Xadj, fine.Adj, fine.W, fine.Ew, finePart, uncoarsePasses);
                part = finePart;
            }
            return part;
        }

        static int CoarsenHEM(
            int n, int[] xadj, int[] adj, int[] w, int[] ew, int maxCoarseW, bool forward,
            out int[] map, out int[] cxadj, out int[] cadj, out int[] cw, out int[] cew)
        {
            int[] match = new int[n];
            for (int i = 0; i < n; ++i) match[i] = -1;

            int start = forward ? 0 : n - 1;
            int end = forward ? n : -1;
            int step = forward ? 1 : -1;

            for (int i = start; i != end; i += step) {
                if (match[i] != -1) continue;
                int wi = w[i];
                int bestJ = -1;
                long bestScore = -1;
                int bestW = int.MaxValue;
                if (wi < maxCoarseW) {
                    for (int e = xadj[i]; e < xadj[i + 1]; ++e) {
                        int j = adj[e];
                        if (match[j] != -1 || wi + w[j] > maxCoarseW) continue;
                        long sc = ew[e];
                        if (sc > bestScore || (sc == bestScore && (w[j] < bestW || (w[j] == bestW && j < bestJ)))) {
                            bestScore = sc; bestW = w[j]; bestJ = j;
                        }
                    }
                }
                if (bestJ >= 0) {
                    match[i] = bestJ;
                    match[bestJ] = i;
                } else {
                    match[i] = i;
                }
            }

            map = new int[n];
            for (int i = 0; i < n; ++i) map[i] = -1;
            int cn = 0;
            for (int i = 0; i < n; ++i) {
                if (map[i] != -1) continue;
                int j = match[i];
                map[i] = cn;
                map[j] = cn;
                cn++;
            }

            cw = new int[cn];
            int[] repA = new int[cn];
            int[] repB = new int[cn];
            for (int c = 0; c < cn; ++c) repA[c] = repB[c] = -1;
            for (int i = 0; i < n; ++i) {
                int c = map[i];
                cw[c] += w[i];
                if (repA[c] == -1) repA[c] = i;
                else repB[c] = i;
            }

            cxadj = new int[cn + 1];
            var adjList = new List<int>(adj.Length);
            var ewList = new List<int>(adj.Length);
            int[] mark = new int[cn];
            int[] slot = new int[cn];
            for (int c = 0; c < cn; ++c) mark[c] = -1;

            for (int c = 0; c < cn; ++c) {
                cxadj[c] = adjList.Count;
                int cStart = adjList.Count;
                int u0 = repA[c], u1 = repB[c];
                for (int pass = 0; pass < 2; ++pass) {
                    int u = pass == 0 ? u0 : u1;
                    if (u < 0) continue;
                    for (int e = xadj[u]; e < xadj[u + 1]; ++e) {
                        int nc = map[adj[e]];
                        if (nc == c) continue;
                        if (mark[nc] != c) {
                            mark[nc] = c;
                            slot[nc] = adjList.Count;
                            adjList.Add(nc);
                            ewList.Add(ew[e]);
                        } else {
                            ewList[slot[nc]] += ew[e];
                        }
                    }
                }
                int len = adjList.Count - cStart;
                if (len > 1) SortPairedSlice(adjList, ewList, cStart, len);
            }
            cxadj[cn] = adjList.Count;
            cadj = adjList.ToArray();
            cew = ewList.ToArray();
            return cn;
        }

        // Deterministic initial bipartition on the tiny coarsest graph (<= ~48 nodes):
        // orthogonal spectral Fiedler splits + multi-start GGG, each polished by FM edge-cut refinement.
        static byte[] InitialBipartitionSpectralAndGGG(int n, int[] xadj, int[] adj, int[] w, int[] ew, bool highQuality)
        {
            if (n <= 1) return new byte[n];
            int totalW = 0;
            for (int i = 0; i < n; ++i) totalW += w[i];
            int halfW = totalW / 2;

            int numSeeds = Math.Min(highQuality ? 4 : 3, n);
            int[] seeds = new int[numSeeds];
            int[] dist = new int[n];
            int[] minDist = new int[n];
            int[] bfsQ = new int[n];
            int[] dist0 = new int[n];
            int[] dist2 = new int[n];
            for (int i = 0; i < n; ++i) minDist[i] = int.MaxValue;

            int p0 = FarthestBfsVertex(n, xadj, adj, 0, dist, bfsQ);
            seeds[0] = FarthestBfsVertex(n, xadj, adj, p0, dist, bfsQ);
            for (int s = 0; s < numSeeds; ++s) {
                BfsDistances(n, xadj, adj, seeds[s], dist, bfsQ);
                if (s == 0) Array.Copy(dist, dist0, n);
                else if (s == 2 || (s == 1 && numSeeds == 2)) Array.Copy(dist, dist2, n);
                for (int i = 0; i < n; ++i)
                    if (dist[i] < minDist[i]) minDist[i] = dist[i];
                if (s + 1 < numSeeds) {
                    int bestV = 0, bestD = -1, bestDeg = int.MaxValue;
                    for (int i = 0; i < n; ++i) {
                        int d = minDist[i], dg = xadj[i + 1] - xadj[i];
                        if (d > bestD || (d == bestD && (dg < bestDeg || (dg == bestDeg && i < bestV)))) {
                            bestD = d; bestDeg = dg; bestV = i;
                        }
                    }
                    seeds[s + 1] = bestV;
                }
            }

            byte[] bestPart = null;
            bool bestBalanced = false;
            long bestCut = long.MaxValue;
            int bestImbal = int.MaxValue;
            byte[] part = new byte[n];
            int fmPasses = highQuality ? 3 : 2;

            // 1) Spectral Fiedler candidates (orthogonal starts spanning diagonal and axis-aligned modes).
            double[] degW = new double[n];
            for (int i = 0; i < n; ++i) {
                double s = 0;
                for (int e = xadj[i]; e < xadj[i + 1]; ++e) s += ew[e];
                degW[i] = s;
            }
            double[] x = new double[n];
            double[] nx = new double[n];
            int[] order = new int[n];
            double invTotalW = 1.0 / totalW;

            int numSpectral = numSeeds >= 3 ? (highQuality ? 4 : 3) : 2;
            int spectralIters = highQuality ? 32 : 20;
            for (int init = 0; init < numSpectral; ++init) {
                for (int i = 0; i < n; ++i) {
                    double d0 = dist0[i] == int.MaxValue ? 0.0 : (double)dist0[i];
                    double d2 = dist2[i] == int.MaxValue ? 0.0 : (double)dist2[i];
                    if (init == 0) x[i] = d0;
                    else if (init == 1) x[i] = d0 + d2;
                    else if (init == 2) x[i] = d0 - d2;
                    else x[i] = d2;
                }

                for (int it = 0; it < spectralIters; ++it) {
                    double mean = 0;
                    for (int i = 0; i < n; ++i) mean += w[i] * x[i];
                    mean *= invTotalW;
                    double maxAbs = 0;
                    for (int i = 0; i < n; ++i) {
                        double v = x[i] - mean;
                        x[i] = v;
                        double av = Math.Abs(v);
                        if (av > maxAbs) maxAbs = av;
                    }
                    if (maxAbs == 0.0 || double.IsNaN(maxAbs) || double.IsInfinity(maxAbs)) break;
                    double invMax = 1.0 / maxAbs;
                    for (int i = 0; i < n; ++i) x[i] *= invMax;

                    for (int i = 0; i < n; ++i) {
                        double dw = degW[i];
                        if (dw > 0.0) {
                            double sum = 0;
                            for (int e = xadj[i]; e < xadj[i + 1]; ++e) sum += ew[e] * x[adj[e]];
                            nx[i] = 0.5 * x[i] + 0.5 * (sum / dw);
                        } else {
                            nx[i] = x[i];
                        }
                    }
                    double[] tmp = x; x = nx; nx = tmp;
                }

                for (int i = 0; i < n; ++i) order[i] = i;
                Array.Sort(order, (a, b) => {
                    int c = x[a].CompareTo(x[b]);
                    return c != 0 ? c : a.CompareTo(b);
                });

                for (int i = 0; i < n; ++i) part[i] = 1;
                int w0 = 0;
                for (int k = 0; k < n - 1; ++k) {
                    int v = order[k];
                    if (k > 0 && w0 >= (int)(((long)totalW * 45) / 100) &&
                        Math.Abs((w0 + w[v]) - (totalW - w0 - w[v])) > Math.Abs(w0 - (totalW - w0)))
                        break;
                    part[v] = 0;
                    w0 += w[v];
                    if (w0 >= halfW) break;
                }

                RefineEdgeCutFM(n, xadj, adj, w, ew, part, fmPasses);
                EvaluateCandidate(n, xadj, adj, w, ew, totalW, part, ref bestPart, ref bestBalanced, ref bestCut, ref bestImbal);
            }

            // 2) Multi-start Greedy Graph Growing candidates.
            int numGgg = highQuality ? numSeeds : Math.Min(2, numSeeds);
            var heap = new MaxGainHeap(n);
            int[] gain0 = new int[n];
            for (int s = 0; s < numGgg; ++s) {
                for (int i = 0; i < n; ++i) part[i] = 1;
                heap.Clear();
                int w0 = 0;
                for (int i = 0; i < n; ++i) {
                    int sum = 0;
                    for (int e = xadj[i]; e < xadj[i + 1]; ++e) sum += ew[e];
                    gain0[i] = -sum;
                }

                int cur = seeds[s];
                for (int moved = 0; moved < n - 1; ++moved) {
                    if (moved > 0) {
                        if (heap.Count > 0) cur = heap.Pop();
                        else {
                            cur = -1;
                            for (int i = 0; i < n; ++i) if (part[i] == 1) { cur = i; break; }
                            if (cur < 0) break;
                        }
                    }
                    if (moved > 0 && w0 >= (int)(((long)totalW * 45) / 100) &&
                        Math.Abs((w0 + w[cur]) - (totalW - w0 - w[cur])) > Math.Abs(w0 - (totalW - w0)))
                        break;

                    part[cur] = 0;
                    w0 += w[cur];
                    for (int e = xadj[cur]; e < xadj[cur + 1]; ++e) {
                        int nb = adj[e];
                        if (part[nb] == 1) {
                            gain0[nb] += 2 * ew[e];
                            heap.PushOrUpdate(nb, gain0[nb]);
                        }
                    }
                    if (w0 >= halfW) break;
                }

                RefineEdgeCutFM(n, xadj, adj, w, ew, part, fmPasses);
                EvaluateCandidate(n, xadj, adj, w, ew, totalW, part, ref bestPart, ref bestBalanced, ref bestCut, ref bestImbal);
            }

            if (bestPart == null) {
                bestPart = new byte[n];
                bestPart[0] = 0;
                for (int i = 1; i < n; ++i) bestPart[i] = 1;
            }
            return bestPart;
        }

        static void EvaluateCandidate(
            int n, int[] xadj, int[] adj, int[] w, int[] ew, int totalW, byte[] part,
            ref byte[] bestPart, ref bool bestBalanced, ref long bestCut, ref int bestImbal)
        {
            long cut = 0;
            int pw0 = 0;
            for (int i = 0; i < n; ++i) {
                if (part[i] == 0) {
                    pw0 += w[i];
                    for (int e = xadj[i]; e < xadj[i + 1]; ++e)
                        if (part[adj[e]] == 1) cut += ew[e];
                }
            }
            if (pw0 <= 0 || pw0 >= totalW) return;
            int imbal = Math.Abs(pw0 - (totalW - pw0));
            bool balanced = pw0 >= (int)(((long)totalW * 38) / 100) && pw0 <= (int)(((long)totalW * 62) / 100);
            if ((balanced && !bestBalanced) ||
                (balanced == bestBalanced && (cut < bestCut || (cut == bestCut && imbal < bestImbal)))) {
                bestBalanced = balanced;
                bestCut = cut;
                bestImbal = imbal;
                bestPart = (byte[])part.Clone();
            }
        }

        static int FarthestBfsVertex(int n, int[] xadj, int[] adj, int start, int[] dist, int[] bfsQ)
        {
            BfsDistances(n, xadj, adj, start, dist, bfsQ);
            int bestV = start, bestD = -1, bestDeg = int.MaxValue;
            for (int i = 0; i < n; ++i) {
                int d = dist[i];
                if (d == int.MaxValue) continue;
                int dg = xadj[i + 1] - xadj[i];
                if (d > bestD || (d == bestD && (dg < bestDeg || (dg == bestDeg && i < bestV)))) {
                    bestD = d; bestDeg = dg; bestV = i;
                }
            }
            return bestV;
        }

        static void BfsDistances(int n, int[] xadj, int[] adj, int start, int[] dist, int[] bfsQ)
        {
            for (int i = 0; i < n; ++i) dist[i] = int.MaxValue;
            int qh = 0, qt = 0;
            dist[start] = 0;
            bfsQ[qt++] = start;
            while (qh < qt) {
                int u = bfsQ[qh++];
                int nd = dist[u] + 1;
                for (int e = xadj[u]; e < xadj[u + 1]; ++e) {
                    int v = adj[e];
                    if (dist[v] == int.MaxValue) {
                        dist[v] = nd;
                        bfsQ[qt++] = v;
                    }
                }
            }
        }

        // Boundary-only FM edge-cut refinement (Karypis & Kumar 1998).
        static void RefineEdgeCutFM(int n, int[] xadj, int[] adj, int[] w, int[] ew, byte[] part, int maxPasses)
        {
            int totalW = 0, maxV = 0;
            int w0 = 0, w1 = 0;
            for (int i = 0; i < n; ++i) {
                int wi = w[i];
                totalW += wi;
                if (wi > maxV) maxV = wi;
                if (part[i] == 0) w0 += wi; else w1 += wi;
            }
            if (w0 == 0 || w1 == 0) return;

            int maxPartW = Math.Max((int)(((long)totalW * 53) / 100), totalW / 2 + Math.Min(maxV, totalW / 8));
            int[] intW = new int[n];
            int[] extW = new int[n];
            bool[] locked = new bool[n];
            int[] movedList = new int[n];
            var q0 = new MaxGainHeap(n);
            var q1 = new MaxGainHeap(n);

            for (int pass = 0; pass < maxPasses; ++pass) {
                q0.Clear(); q1.Clear();
                long cut = 0;
                int bndCount = 0;
                for (int i = 0; i < n; ++i) {
                    locked[i] = false;
                    int iw = 0, xw = 0;
                    byte pi = part[i];
                    for (int e = xadj[i]; e < xadj[i + 1]; ++e) {
                        if (part[adj[e]] == pi) iw += ew[e];
                        else xw += ew[e];
                    }
                    intW[i] = iw;
                    extW[i] = xw;
                    if (pi == 0) cut += xw;
                    if (xw > 0) {
                        bndCount++;
                        if (pi == 0) q0.PushOrUpdate(i, xw - iw);
                        else q1.PushOrUpdate(i, xw - iw);
                    }
                }

                long bestCut = cut;
                int bestImbal = Math.Abs(w0 - w1);
                int bestStep = 0;
                int nMoved = 0;
                int noImprove = 0;
                int earlyStop = Math.Max(32, bndCount / 2);

                while (q0.Count > 0 || q1.Count > 0) {
                    bool can0 = q0.Count > 0 && (w0 > w[q0.TopVertex()]) && (w0 > maxPartW || w1 + w[q0.TopVertex()] <= maxPartW);
                    bool can1 = q1.Count > 0 && (w1 > w[q1.TopVertex()]) && (w1 > maxPartW || w0 + w[q1.TopVertex()] <= maxPartW);

                    int moveFrom;
                    if (!can0 && !can1) {
                        if (q0.Count > 0 && (q1.Count == 0 || w0 >= w1)) q0.Pop();
                        else if (q1.Count > 0) q1.Pop();
                        else break;
                        continue;
                    } else if (can0 && !can1) {
                        moveFrom = 0;
                    } else if (!can0 && can1) {
                        moveFrom = 1;
                    } else if (w0 > maxPartW && w1 <= maxPartW) {
                        moveFrom = 0;
                    } else if (w1 > maxPartW && w0 <= maxPartW) {
                        moveFrom = 1;
                    } else {
                        int g0 = q0.TopGain(), g1 = q1.TopGain();
                        if (g0 > g1) moveFrom = 0;
                        else if (g1 > g0) moveFrom = 1;
                        else if (w0 > w1) moveFrom = 0;
                        else if (w1 > w0) moveFrom = 1;
                        else moveFrom = q0.TopVertex() < q1.TopVertex() ? 0 : 1;
                    }

                    int v = moveFrom == 0 ? q0.Pop() : q1.Pop();
                    locked[v] = true;
                    movedList[nMoved++] = v;

                    int gain = extW[v] - intW[v];
                    cut -= gain;
                    if (moveFrom == 0) {
                        part[v] = 1; w0 -= w[v]; w1 += w[v];
                    } else {
                        part[v] = 0; w1 -= w[v]; w0 += w[v];
                    }
                    int tmp = intW[v]; intW[v] = extW[v]; extW[v] = tmp;

                    byte newP = part[v];
                    for (int e = xadj[v]; e < xadj[v + 1]; ++e) {
                        int u = adj[e];
                        int wEdge = ew[e];
                        if (part[u] == newP) { intW[u] += wEdge; extW[u] -= wEdge; }
                        else { intW[u] -= wEdge; extW[u] += wEdge; }
                        if (!locked[u]) {
                            if (extW[u] > 0) {
                                int gu = extW[u] - intW[u];
                                if (part[u] == 0) q0.PushOrUpdate(u, gu);
                                else q1.PushOrUpdate(u, gu);
                            } else {
                                if (part[u] == 0) q0.Remove(u);
                                else q1.Remove(u);
                            }
                        }
                    }

                    int imbal = Math.Abs(w0 - w1);
                    bool balanced = w0 <= maxPartW && w1 <= maxPartW;
                    bool prevBalanced = bestImbal <= Math.Max(0, 2 * maxPartW - totalW);
                    if ((balanced && !prevBalanced) ||
                        (balanced == prevBalanced && (cut < bestCut || (cut == bestCut && imbal < bestImbal)))) {
                        bestCut = cut;
                        bestImbal = imbal;
                        bestStep = nMoved;
                        noImprove = 0;
                    } else {
                        if (++noImprove >= earlyStop) break;
                    }
                }

                for (int k = nMoved - 1; k >= bestStep; --k) {
                    int v = movedList[k];
                    if (part[v] == 0) { part[v] = 1; w0 -= w[v]; w1 += w[v]; }
                    else { part[v] = 0; w1 -= w[v]; w0 += w[v]; }
                }
                if (bestStep == 0) break;
            }
        }

        // Extracts a minimum-cardinality vertex separator S covering all cut edges between side 0 and side 1
        // via Hopcroft-Karp maximum bipartite matching + Konig's theorem (Ashcraft & Liu 1998).
        // When bndCandidates is provided, only those vertices are scanned for cut edges.
        static void ExtractMinVertexSeparator(
            int nv, int[] lxadj, int[] ladj, byte[] where,
            int[] bndCandidates, int bndCandCount, BisectWorkspace ws,
            out int w0, out int w1, out int wS, out int[] sepInit)
        {
            int c0 = 0, c1 = 0;
            for (int i = 0; i < nv; ++i)
                if (where[i] == 0) c0++; else c1++;

            byte leftSide = c0 >= c1 ? (byte)0 : (byte)1;
            byte rightSide = (byte)(1 - leftSide);

            int[] bIdx = ws.BIdx;
            var uLeft = new List<int>();
            var uRight = new List<int>();

            if (bndCandidates != null) {
                for (int t = 0; t < bndCandCount; ++t) {
                    int i = bndCandidates[t];
                    if (bIdx[i] >= 0) continue;
                    byte si = where[i];
                    bool isBnd = false;
                    for (int e = lxadj[i]; e < lxadj[i + 1]; ++e) {
                        if (where[ladj[e]] != si) { isBnd = true; break; }
                    }
                    if (!isBnd) continue;
                    if (si == leftSide) { bIdx[i] = uLeft.Count; uLeft.Add(i); }
                    else { bIdx[i] = uRight.Count; uRight.Add(i); }
                }
            } else {
                for (int i = 0; i < nv; ++i) {
                    byte si = where[i];
                    bool isBnd = false;
                    for (int e = lxadj[i]; e < lxadj[i + 1]; ++e) {
                        if (where[ladj[e]] != si) { isBnd = true; break; }
                    }
                    if (!isBnd) continue;
                    if (si == leftSide) { bIdx[i] = uLeft.Count; uLeft.Add(i); }
                    else { bIdx[i] = uRight.Count; uRight.Add(i); }
                }
            }

            int nL = uLeft.Count, nR = uRight.Count;
            if (nL == 0 || nR == 0) {
                for (int a = 0; a < nL; ++a) bIdx[uLeft[a]] = -1;
                for (int b = 0; b < nR; ++b) bIdx[uRight[b]] = -1;
                w0 = c0; w1 = c1; wS = 0; sepInit = new int[0];
                return;
            }

            int[] bxadj = new int[nL + 1];
            for (int a = 0; a < nL; ++a) {
                int v = uLeft[a];
                int deg = 0;
                for (int e = lxadj[v]; e < lxadj[v + 1]; ++e)
                    if (where[ladj[e]] == rightSide) deg++;
                bxadj[a + 1] = bxadj[a] + deg;
            }
            int[] badj = new int[bxadj[nL]];
            for (int a = 0; a < nL; ++a) {
                int v = uLeft[a];
                int wPos = bxadj[a];
                for (int e = lxadj[v]; e < lxadj[v + 1]; ++e) {
                    int u = ladj[e];
                    if (where[u] == rightSide) badj[wPos++] = bIdx[u];
                }
            }
            // Reset bIdx entries to -1 for workspace reuse.
            for (int a = 0; a < nL; ++a) bIdx[uLeft[a]] = -1;
            for (int b = 0; b < nR; ++b) bIdx[uRight[b]] = -1;

            int[] matchL = new int[nL];
            int[] matchR = new int[nR];
            for (int a = 0; a < nL; ++a) matchL[a] = -1;
            for (int b = 0; b < nR; ++b) matchR[b] = -1;

            for (int a = 0; a < nL; ++a) {
                for (int e = bxadj[a]; e < bxadj[a + 1]; ++e) {
                    int b = badj[e];
                    if (matchR[b] == -1) {
                        matchL[a] = b;
                        matchR[b] = a;
                        break;
                    }
                }
            }

            int[] dist = new int[nL];
            int[] bfsQ = new int[nL];
            int[] curArc = new int[nL];
            int[] dfsStack = new int[nL];
            int[] dfsChosenB = new int[nL];

            while (true) {
                int qh = 0, qt = 0;
                bool foundAug = false;
                for (int a = 0; a < nL; ++a) {
                    if (matchL[a] == -1) { dist[a] = 0; bfsQ[qt++] = a; }
                    else dist[a] = int.MaxValue;
                }
                while (qh < qt) {
                    int a = bfsQ[qh++];
                    int nd = dist[a] + 1;
                    for (int e = bxadj[a]; e < bxadj[a + 1]; ++e) {
                        int b = badj[e];
                        int na = matchR[b];
                        if (na == -1) foundAug = true;
                        else if (dist[na] == int.MaxValue) { dist[na] = nd; bfsQ[qt++] = na; }
                    }
                }
                if (!foundAug) break;

                Array.Copy(bxadj, 0, curArc, 0, nL);
                for (int startA = 0; startA < nL; ++startA) {
                    if (matchL[startA] != -1) continue;
                    int top = 0;
                    dfsStack[0] = startA;
                    while (top >= 0) {
                        int a = dfsStack[top];
                        int endE = bxadj[a + 1];
                        bool advanced = false;
                        while (curArc[a] < endE) {
                            int b = badj[curArc[a]++];
                            int na = matchR[b];
                            if (na == -1) {
                                dfsChosenB[top] = b;
                                for (int k = top; k >= 0; --k) {
                                    int ka = dfsStack[k], kb = dfsChosenB[k];
                                    matchL[ka] = kb;
                                    matchR[kb] = ka;
                                }
                                top = -1;
                                advanced = true;
                                break;
                            } else if (dist[na] == dist[a] + 1) {
                                dfsChosenB[top] = b;
                                dfsStack[++top] = na;
                                advanced = true;
                                break;
                            }
                        }
                        if (!advanced) {
                            dist[a] = int.MaxValue;
                            top--;
                        }
                    }
                }
            }

            bool[] visL = new bool[nL];
            bool[] visR = new bool[nR];
            {
                int qh = 0, qt = 0;
                for (int a = 0; a < nL; ++a)
                    if (matchL[a] == -1) { visL[a] = true; bfsQ[qt++] = a; }
                while (qh < qt) {
                    int a = bfsQ[qh++];
                    for (int e = bxadj[a]; e < bxadj[a + 1]; ++e) {
                        int b = badj[e];
                        if (!visR[b]) {
                            visR[b] = true;
                            int na = matchR[b];
                            if (na != -1 && !visL[na]) { visL[na] = true; bfsQ[qt++] = na; }
                        }
                    }
                }
            }

            w0 = c0; w1 = c1; wS = 0;
            var sep = new List<int>(Math.Min(nL, nR));
            for (int a = 0; a < nL; ++a) {
                if (!visL[a]) {
                    int v = uLeft[a];
                    where[v] = 2;
                    if (leftSide == 0) w0--; else w1--;
                    wS++;
                    sep.Add(v);
                }
            }
            for (int b = 0; b < nR; ++b) {
                if (visR[b]) {
                    int v = uRight[b];
                    where[v] = 2;
                    if (rightSide == 0) w0--; else w1--;
                    wS++;
                    sep.Add(v);
                }
            }
            sepInit = sep.ToArray();
        }

        // Two-sided FM vertex-separator refinement (Karypis & Kumar 1998, Section 3.3), touching only O(|S|)
        // vertices per pass using the reusable BisectWorkspace (zero O(nv) allocations or clears).
        static void RefineVertexSeparatorFM(
            int nv, int[] lxadj, int[] ladj, byte[] where, int[] sepInit, int maxPasses, BisectWorkspace ws,
            ref int w0, ref int w1, ref int wS)
        {
            int[] adj0 = ws.Adj0;
            int[] adj1 = ws.Adj1;
            int[] lockStamp = ws.LockStamp;
            int[] sepMark = ws.SepMark;

            int initUndoCap = Math.Min(nv * 2, Math.Max(256, wS * 4));
            int[] undoVert = new int[initUndoCap];
            byte[] undoOld = new byte[initUndoCap];

            var sepList = new List<int>(wS * 2);
            for (int i = 0; i < sepInit.Length; ++i) sepList.Add(sepInit[i]);

            var q0 = new SparseMaxGainHeap(ws.Pos0, Math.Max(64, wS * 2));
            var q1 = new SparseMaxGainHeap(ws.Pos1, Math.Max(64, wS * 2));

            for (int pass = 0; pass < maxPasses; ++pass) {
                int stamp = ws.NextStamp();
                q0.Clear(); q1.Clear();

                int keep = 0;
                for (int idx = 0; idx < sepList.Count; ++idx) {
                    int i = sepList[idx];
                    if (where[i] != 2 || sepMark[i] == stamp) continue;
                    sepMark[i] = stamp;
                    sepList[keep++] = i;
                    int a0 = 0, a1 = 0;
                    for (int e = lxadj[i]; e < lxadj[i + 1]; ++e) {
                        byte wu = where[ladj[e]];
                        if (wu == 0) a0++;
                        else if (wu == 1) a1++;
                    }
                    adj0[i] = a0;
                    adj1[i] = a1;
                    q0.PushOrUpdate(i, 1 - a1);
                    q1.PushOrUpdate(i, 1 - a0);
                }
                sepList.RemoveRange(keep, sepList.Count - keep);

                int bestWS = wS;
                int bestImbal = Math.Abs(w0 - w1);
                bool bestBothNonEmpty = w0 > 0 && w1 > 0;
                int bestUndoLen = 0;
                int undoLen = 0;
                int noImprove = 0;
                int earlyStop = Math.Max(48, wS / 2);

                while (q0.Count > 0 || q1.Count > 0) {
                    int balLimit = Math.Max((int)(((long)(w0 + w1) * 55) / 100), (w0 + w1) / 2 + 1);
                    bool can0 = q0.Count > 0 && (w0 <= balLimit || w1 > balLimit);
                    bool can1 = q1.Count > 0 && (w1 <= balLimit || w0 > balLimit);

                    int targetSide;
                    if (!can0 && !can1) {
                        break;
                    } else if (can0 && !can1) {
                        targetSide = 0;
                    } else if (!can0 && can1) {
                        targetSide = 1;
                    } else {
                        int g0 = q0.TopGain(), g1 = q1.TopGain();
                        if (g0 > g1) targetSide = 0;
                        else if (g1 > g0) targetSide = 1;
                        else if (w0 < w1) targetSide = 0;
                        else if (w1 < w0) targetSide = 1;
                        else targetSide = q0.TopVertex() < q1.TopVertex() ? 0 : 1;
                    }

                    int v = targetSide == 0 ? q0.Pop() : q1.Pop();
                    if (targetSide == 0) q1.Remove(v); else q0.Remove(v);
                    if (lockStamp[v] == stamp || where[v] != 2) continue;
                    lockStamp[v] = stamp;

                    RecordUndo(ref undoVert, ref undoOld, ref undoLen, v, 2);
                    where[v] = (byte)targetSide;
                    wS--;
                    if (targetSide == 0) w0++; else w1++;

                    byte oppSide = (byte)(1 - targetSide);
                    for (int e = lxadj[v]; e < lxadj[v + 1]; ++e) {
                        int u = ladj[e];
                        byte wu = where[u];
                        if (wu == 2) {
                            if (targetSide == 0) {
                                adj0[u]++;
                                if (lockStamp[u] != stamp) q1.PushOrUpdate(u, 1 - adj0[u]);
                            } else {
                                adj1[u]++;
                                if (lockStamp[u] != stamp) q0.PushOrUpdate(u, 1 - adj1[u]);
                            }
                        } else if (wu == oppSide) {
                            RecordUndo(ref undoVert, ref undoOld, ref undoLen, u, oppSide);
                            where[u] = 2;
                            wS++;
                            if (oppSide == 0) w0--; else w1--;
                            if (sepMark[u] != stamp) { sepMark[u] = stamp; sepList.Add(u); }

                            int ua0 = 0, ua1 = 0;
                            for (int eu = lxadj[u]; eu < lxadj[u + 1]; ++eu) {
                                int z = ladj[eu];
                                byte wz = where[z];
                                if (wz == 0) ua0++;
                                else if (wz == 1) ua1++;
                                else {
                                    if (oppSide == 0) {
                                        adj0[z]--;
                                        if (lockStamp[z] != stamp) q1.PushOrUpdate(z, 1 - adj0[z]);
                                    } else {
                                        adj1[z]--;
                                        if (lockStamp[z] != stamp) q0.PushOrUpdate(z, 1 - adj1[z]);
                                    }
                                }
                            }
                            adj0[u] = ua0;
                            adj1[u] = ua1;
                            if (lockStamp[u] != stamp) {
                                q0.PushOrUpdate(u, 1 - ua1);
                                q1.PushOrUpdate(u, 1 - ua0);
                            }
                        }
                    }

                    bool bothNonEmpty = w0 > 0 && w1 > 0;
                    int imbal = Math.Abs(w0 - w1);
                    int curBalLimit = Math.Max((int)(((long)(w0 + w1) * 55) / 100), (w0 + w1) / 2 + 1);
                    bool curBalanced = w0 <= curBalLimit && w1 <= curBalLimit;
                    bool better = false;
                    if (bothNonEmpty && !bestBothNonEmpty && curBalanced && wS <= bestWS) {
                        better = true;
                    } else if (bothNonEmpty == bestBothNonEmpty && curBalanced) {
                        if (wS < bestWS || (wS == bestWS && imbal < bestImbal))
                            better = true;
                    }

                    if (better) {
                        bestWS = wS;
                        bestImbal = imbal;
                        bestBothNonEmpty = bothNonEmpty;
                        bestUndoLen = undoLen;
                        noImprove = 0;
                    } else {
                        if (++noImprove >= earlyStop) break;
                    }
                }

                for (int k = undoLen - 1; k >= bestUndoLen; --k) {
                    int u = undoVert[k];
                    byte curW = where[u];
                    byte oldW = undoOld[k];
                    if (curW == 0) w0--; else if (curW == 1) w1--; else wS--;
                    where[u] = oldW;
                    if (oldW == 0) w0++; else if (oldW == 1) w1++; else wS++;
                }

                if (bestUndoLen == 0) break;
            }
            q0.Clear(); q1.Clear();
        }

        static void RecordUndo(ref int[] verts, ref byte[] oldWhere, ref int len, int v, byte oldW)
        {
            if (len == verts.Length) {
                int nl = verts.Length * 2;
                Array.Resize(ref verts, nl);
                Array.Resize(ref oldWhere, nl);
            }
            verts[len] = v;
            oldWhere[len] = oldW;
            len++;
        }

        // Flat-array halo-aware local AMD (constrained minimum degree on a leaf subgraph with non-eliminable
        // ancestor-separator halo nodes; Ashcraft & Liu 1998 / Amestoy-Davis-Duff 1996).
        static void OrderLeafHaloAmd(
            int[] xadj, int[] adjncy,
            int[] subIdMap, int[] localIdMap,
            int leafId, int[] verts,
            int[] blockOrder, int outPos,
            LeafWorkspace ws)
        {
            int nv = verts.Length;
            if (nv == 0) return;
            if (nv == 1) { blockOrder[outPos] = verts[0]; return; }

            int haloRawCount = 0;
            int totalAdj = 0;
            for (int i = 0; i < nv; ++i) {
                int gv = verts[i];
                int deg = xadj[gv + 1] - xadj[gv];
                totalAdj += deg;
                for (int e = xadj[gv]; e < xadj[gv + 1]; ++e) {
                    int gu = adjncy[e];
                    if (subIdMap[gu] != leafId) {
                        if (haloRawCount == ws.HaloBuf.Length) Array.Resize(ref ws.HaloBuf, ws.HaloBuf.Length * 2);
                        ws.HaloBuf[haloRawCount++] = gu;
                    }
                }
            }

            int nHalo = 0;
            int[] haloIds = ws.HaloBuf;
            if (haloRawCount > 0) {
                Array.Sort(haloIds, 0, haloRawCount);
                for (int i = 0; i < haloRawCount; ++i) {
                    if (nHalo == 0 || haloIds[i] != haloIds[nHalo - 1])
                        haloIds[nHalo++] = haloIds[i];
                }
            }

            int total = nv + nHalo;
            ws.EnsureNodeCapacity(nv, total);
            if (totalAdj > ws.AAdj.Length) ws.AAdj = new int[Math.Max(totalAdj, ws.AAdj.Length * 2)];

            int[] aStart = ws.AStart, aLen = ws.ALen, aAdj = ws.AAdj;
            int aCursor = 0;
            for (int i = 0; i < nv; ++i) {
                int gv = verts[i];
                aStart[i] = aCursor;
                for (int e = xadj[gv]; e < xadj[gv + 1]; ++e) {
                    int gu = adjncy[e];
                    if (subIdMap[gu] == leafId) aAdj[aCursor++] = localIdMap[gu];
                    else aAdj[aCursor++] = nv + Array.BinarySearch(haloIds, 0, nHalo, gu);
                }
                aLen[i] = aCursor - aStart[i];
            }

            int[] eHead = ws.EHead;
            bool[] eliminated = ws.Eliminated;
            bool[] absorbed = ws.Absorbed;
            int[] degree = ws.Degree;
            int[] bucketHead = ws.BucketHead;
            int[] bnext = ws.BNext;
            int[] bprev = ws.BPrev;
            int[] mark = ws.Mark;
            int[] wFlag = ws.WFlag;
            int[] wVal = ws.WVal;
            int[] lStart = ws.LStart;
            int[] lLen = ws.LLen;

            for (int i = 0; i < total; ++i) { eHead[i] = -1; eliminated[i] = false; mark[i] = -1; }
            for (int d = 0; d <= total; ++d) bucketHead[d] = -1;
            for (int i = 0; i < nv; ++i) { absorbed[i] = false; wFlag[i] = -1; }

            for (int i = nv - 1; i >= 0; --i) {
                int d = aLen[i];
                if (d > total) d = total;
                degree[i] = d;
                bprev[i] = -1;
                bnext[i] = bucketHead[d];
                if (bucketHead[d] >= 0) bprev[bucketHead[d]] = i;
                bucketHead[d] = i;
            }

            int lTop = 0;
            int eTop = 0;
            int minDeg = 0;

            for (int k = 0; k < nv; ++k) {
                while (bucketHead[minDeg] < 0) minDeg++;
                int p = bucketHead[minDeg];
                if (bprev[p] >= 0) bnext[bprev[p]] = bnext[p]; else bucketHead[degree[p]] = bnext[p];
                if (bnext[p] >= 0) bprev[bnext[p]] = bprev[p];

                blockOrder[outPos + k] = verts[p];
                eliminated[p] = true;

                int lpStart = lTop;
                mark[p] = k;

                int ap0 = aStart[p], apEnd = ap0 + aLen[p];
                for (int t = ap0; t < apEnd; ++t) {
                    int a = aAdj[t];
                    if (!eliminated[a] && mark[a] != k) {
                        mark[a] = k;
                        if (lTop == ws.LData.Length) Array.Resize(ref ws.LData, ws.LData.Length * 2);
                        ws.LData[lTop++] = a;
                    }
                }
                for (int node = eHead[p]; node != -1; node = ws.ENext[node]) {
                    int e = ws.EElem[node];
                    if (absorbed[e]) continue;
                    int le0 = lStart[e], leEnd = le0 + lLen[e];
                    for (int q = le0; q < leEnd; ++q) {
                        int a = ws.LData[q];
                        if (!eliminated[a] && mark[a] != k) {
                            mark[a] = k;
                            if (lTop == ws.LData.Length) Array.Resize(ref ws.LData, ws.LData.Length * 2);
                            ws.LData[lTop++] = a;
                        }
                    }
                    absorbed[e] = true;
                }
                eHead[p] = -1;
                int lpLen = lTop - lpStart;
                lStart[p] = lpStart;
                lLen[p] = lpLen;

                int[] lData = ws.LData;
                for (int t = lpStart; t < lTop; ++t) {
                    int i = lData[t];
                    for (int node = eHead[i]; node != -1; node = ws.ENext[node]) {
                        int e = ws.EElem[node];
                        if (absorbed[e]) continue;
                        if (wFlag[e] != k) { wFlag[e] = k; wVal[e] = lLen[e]; }
                        wVal[e]--;
                    }
                }

                int remainingTotal = total - k - 1;
                for (int t = lpStart; t < lTop; ++t) {
                    int i = lData[t];
                    int prev = -1, cur = eHead[i], ext = 0;
                    while (cur != -1) {
                        int next = ws.ENext[cur];
                        int e = ws.EElem[cur];
                        if (absorbed[e]) {
                            if (prev < 0) eHead[i] = next; else ws.ENext[prev] = next;
                        } else if (wVal[e] <= 0) {
                            absorbed[e] = true;
                            if (prev < 0) eHead[i] = next; else ws.ENext[prev] = next;
                        } else {
                            ext += wVal[e];
                            prev = cur;
                        }
                        cur = next;
                    }
                    if (eTop == ws.EElem.Length) {
                        int nc = ws.EElem.Length * 2;
                        Array.Resize(ref ws.EElem, nc);
                        Array.Resize(ref ws.ENext, nc);
                    }
                    ws.EElem[eTop] = p;
                    ws.ENext[eTop] = eHead[i];
                    eHead[i] = eTop++;

                    if (i >= nv) continue;

                    int ai0 = aStart[i], aiEnd = ai0 + aLen[i], wPos = ai0;
                    for (int q = ai0; q < aiEnd; ++q) {
                        int a = aAdj[q];
                        if (!eliminated[a] && mark[a] != k) aAdj[wPos++] = a;
                    }
                    int newAiLen = wPos - ai0;
                    aLen[i] = newAiLen;

                    int d = Math.Min(remainingTotal, Math.Min(degree[i] + lpLen - 1, newAiLen + lpLen - 1 + ext));
                    if (d < 0) d = 0;
                    if (d != degree[i]) {
                        if (bprev[i] >= 0) bnext[bprev[i]] = bnext[i]; else bucketHead[degree[i]] = bnext[i];
                        if (bnext[i] >= 0) bprev[bnext[i]] = bprev[i];
                        degree[i] = d;
                        bprev[i] = -1;
                        bnext[i] = bucketHead[d];
                        if (bucketHead[d] >= 0) bprev[bucketHead[d]] = i;
                        bucketHead[d] = i;
                    }
                    if (d < minDeg) minDeg = d;
                }
            }
        }
    }
}
