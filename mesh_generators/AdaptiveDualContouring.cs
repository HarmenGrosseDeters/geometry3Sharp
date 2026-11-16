// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Dual contouring with adaptive simplification: where one vertex still fits the surface within
    /// <see cref="Tolerance"/>, the vertices of an aligned block of 2^L x 2^L x 2^L grid cells merge into one, so flat
    /// regions get large triangles and fine cells stay at edges, corners and curved parts. Opt-in; DualContouring
    /// itself is unchanged. Design, guarantees and limits: docs/design/DC_ADAPTIVE_SIMPLIFICATION.md.
    ///
    /// The field is contoured on the uniform grid of CellSize (manifold dual contouring with exact Hermite data: the
    /// crossing of each grid edge is found by bisection and the normal is the gradient there), then the vertices are
    /// clustered bottom up by the octree of that grid (Ju et al. 2002; Schaefer, Ju, Warren 2007): a cluster merges
    /// the QEFs of its vertices (union of their Hermite crossings), is placed by the QEF solver inside its block, and
    /// merges if every Hermite plane is within Tolerance finest cells of the vertex.
    /// A block with several sheets gives one cluster per connected piece. After the merge the result is checked:
    /// every edge in two triangles, every vertex link one cycle, and (exactly) no intersecting triangle pair that
    /// involves a merged vertex; clusters involved in a defect lose a level and the check repeats. So the result is
    /// closed and manifold if the uniform contour is, and self-intersection free if the uniform contour is
    /// (BaseSelfIntersectionFree). Tolerance 0 gives the uniform contour.
    ///
    /// The tolerance is relative to the finest cell, so the result does not depend on the scale of the input.
    /// Time and memory scale with the finest grid, not with the output.
    /// </summary>
    public class AdaptiveDualContouring
    {
        public ImplicitFunction3d Implicit;
        public double IsoValue = 0;
        /// <summary> inside is where the field is greater than IsoValue; set for SDFs </summary>
        public bool InsideIsLess = false;
        /// <summary> region to contour, should be large enough that the surface does not touch its border </summary>
        public AxisAlignedBox3d Bounds;
        /// <summary> the finest cell size </summary>
        public double CellSize = 0.1;

        /// <summary>
        /// largest distance (in finest cells) between the vertex of a merged cluster and any Hermite plane (point and
        /// normal on the surface) of its cells. 0 merges nothing. About 0.1 to 0.5 gives a few times fewer triangles on
        /// flat or smooth shapes; the distance to the surface is measured in benchmarks/DualContouringSimplification.
        /// </summary>
        public double Tolerance = 0.25;
        /// <summary> largest merged block is 2^MaxLevel cells wide </summary>
        public int MaxLevel = 6;
        /// <summary> QEF eigenvalues below this fraction of the largest are ignored (as DualContouring.QEFEigenTolerance) </summary>
        public double QEFEigenTolerance = 0.02;
        public bool ParallelCompute = true;
        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Mesh;
        /// <summary> triangles / vertices of the uniform contour before merging </summary>
        public int UniformTriangles, UniformVertices;
        /// <summary> clusters of two or more merged vertices, and the vertices they replaced </summary>
        public int Clusters, MergedVertices;
        /// <summary> clusters by block level (index L: blocks of 2^L cells) </summary>
        public int[] ClustersByLevel = new int[0];
        /// <summary> rounds of the check (1 if the first merge was accepted) and the clusters taken back by it </summary>
        public int CheckRounds, RejectedClusters;
        /// <summary> the uniform contour's own report: polygons left out (holes) and whether it is free of self-intersections </summary>
        public int NonManifoldTriangles;
        public bool BaseSelfIntersectionFree;
        /// <summary> true if the last Generate found no intersecting pair (the base contour was free of them and no merged vertex is in one) </summary>
        public bool SelfIntersectionFree;
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();


        public void Generate()
        {
            Mesh = null;
            Clusters = MergedVertices = RejectedClusters = CheckRounds = 0;
            UniformTriangles = UniformVertices = NonManifoldTriangles = 0;
            BaseSelfIntersectionFree = SelfIntersectionFree = false;
            ClustersByLevel = new int[0];
            PhaseMilliseconds = new Dictionary<string, long>();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var dc = new DualContouring() {
                Implicit = Implicit, IsoValue = IsoValue, InsideIsLess = InsideIsLess, Bounds = Bounds, CellSize = CellSize,
                QEFEigenTolerance = QEFEigenTolerance, ParallelCompute = ParallelCompute, CancelF = CancelF,
                RecordVertexCrossings = true
            };
            dc.HermiteF = HermiteData;
            dc.Generate();
            PhaseMilliseconds["contour"] = timer.ElapsedMilliseconds; timer.Restart();
            if (CancelF() || dc.Mesh == null) return;
            UniformTriangles = dc.Mesh.TriangleCount;
            UniformVertices = dc.Mesh.VertexCount;
            NonManifoldTriangles = dc.NonManifoldTriangles;
            BaseSelfIntersectionFree = dc.SelfIntersectionFree;
            SelfIntersectionFree = dc.SelfIntersectionFree;
            if (Tolerance <= 0 || MaxLevel < 1 || dc.Mesh.TriangleCount == 0) {
                Mesh = dc.Mesh;
                return;
            }
            Mesh = new Simplifier(this, dc).Run();
            PhaseMilliseconds["simplify"] = timer.ElapsedMilliseconds;
        }

        bool is_inside(double f) { return InsideIsLess ? f < IsoValue : f > IsoValue; }

        // exact Hermite data: bisection to the crossing, gradient there
        bool HermiteData(Vector3d a, Vector3d b, Vector3d crossing, out Vector3d point, out Vector3d normal)
        {
            double lo = 0, hi = 1;
            for (int it = 0; it < 50; ++it) {
                double mid = 0.5 * (lo + hi);
                Vector3d m = Vector3d.Lerp(a, b, mid);
                if (is_inside(Implicit.Value(ref m))) lo = mid; else hi = mid;
            }
            point = Vector3d.Lerp(a, b, 0.5 * (lo + hi));
            double h = CellSize / 64;
            Vector3d g = Vector3d.Zero;
            for (int c = 0; c < 3; ++c) {
                Vector3d d = Vector3d.Zero; d[c] = h;
                Vector3d p0 = point + d, p1 = point - d;
                g[c] = Implicit.Value(ref p0) - Implicit.Value(ref p1);
            }
            normal = g / (2 * h);       // (a gradient, not yet a unit vector: DualContouring normalizes; scaled so it does not vanish at tiny scales)
            return g.LengthSquared > 0 && !double.IsNaN(g.LengthSquared) && !double.IsInfinity(g.LengthSquared);
        }


        class Simplifier
        {
            readonly AdaptiveDualContouring owner;
            readonly DualContouring dc;
            readonly DMesh3 M0;
            readonly int NV, Lmax;
            readonly Vector3d[] P0;
            readonly bool[] mergeable, baseBad;
            readonly Vector3i[] cell;
            readonly int[][] root;           // per level: component root (smallest vertex id), -1 if not mergeable
            readonly int[][] roots;          // per level: the roots
            readonly List<int>[][] members;  // per level, by root
            readonly int[][][] children;     // per level, by root: distinct component roots of level-1 (null at level 0)
            readonly sbyte[][] fit;          // 0 unknown, 1 fits, -1 does not
            readonly Vector3d[][] cpos;
            readonly int[][] mlev;           // level at which this member set was merged (inherited if it equals its only child)
            readonly bool[][] coll;
            readonly int[] cap;
            readonly Vector3d origin;
            readonly double tol;

            public Simplifier(AdaptiveDualContouring owner, DualContouring dc)
            {
                this.owner = owner; this.dc = dc; M0 = dc.Mesh;
                NV = M0.MaxVertexID;
                P0 = new Vector3d[NV]; mergeable = new bool[NV]; cell = new Vector3i[NV];
                origin = owner.Bounds.Min;
                tol = owner.Tolerance * owner.CellSize;
                var cross = dc.VertexCrossings;
                for (int v = 0; v < NV; ++v) {
                    if (M0.IsVertex(v) == false) continue;
                    P0[v] = M0.GetVertex(v);
                    if (v < cross.Count && cross[v].Length > 0 && M0.GetVtxEdgeCount(v) > 0) {
                        mergeable[v] = true;
                        cell[v] = dc.VertexCellMin(v);
                    }
                }
                baseBad = new bool[NV];
                for (int v = 0; v < NV; ++v)
                    if (M0.IsVertex(v)) baseBad[v] = vertex_bad(M0, v);
                cap = new int[NV];
                for (int v = 0; v < NV; ++v) cap[v] = owner.MaxLevel;

                // components per level
                int L = Math.Min(owner.MaxLevel, 30);
                root = new int[L + 1][]; roots = new int[L + 1][]; members = new List<int>[L + 1][]; children = new int[L + 1][][];
                fit = new sbyte[L + 1][]; cpos = new Vector3d[L + 1][]; mlev = new int[L + 1][]; coll = new bool[L + 1][];
                for (int l = 0; l <= L; ++l) {
                    int[] r = new int[NV];
                    for (int v = 0; v < NV; ++v) r[v] = mergeable[v] ? v : -1;
                    if (l > 0)
                        foreach (int e in M0.EdgeIndices()) {
                            Index2i ev = M0.GetEdgeV(e);
                            if (mergeable[ev.a] && mergeable[ev.b] && same_block(ev.a, ev.b, l))
                                union(r, ev.a, ev.b);
                        }
                    var rl = new List<int>();
                    var mem = new List<int>[NV];
                    for (int v = 0; v < NV; ++v) {
                        if (!mergeable[v]) continue;
                        r[v] = find(r, v);
                        if (mem[r[v]] == null) { mem[r[v]] = new List<int>(); rl.Add(r[v]); }
                        mem[r[v]].Add(v);
                    }
                    root[l] = r; roots[l] = rl.ToArray(); members[l] = mem;
                    fit[l] = new sbyte[NV]; cpos[l] = new Vector3d[NV]; mlev[l] = new int[NV]; coll[l] = new bool[NV];
                    if (l > 0) {
                        var ch = new int[NV][];
                        foreach (int rt in rl) {
                            var set = new SortedSet<int>();
                            foreach (int v in mem[rt]) set.Add(root[l - 1][v]);
                            var a = new int[set.Count]; set.CopyTo(a); ch[rt] = a;
                        }
                        children[l] = ch;
                    }
                }
                Lmax = L;
                for (int v = 0; v < NV; ++v) if (mergeable[v]) { cpos[0][v] = P0[v]; coll[0][v] = true; mlev[0][v] = 0; }
            }

            bool same_block(int a, int b, int l)
            {
                return (cell[a].x >> l) == (cell[b].x >> l) && (cell[a].y >> l) == (cell[b].y >> l) && (cell[a].z >> l) == (cell[b].z >> l);
            }
            static int find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
            static void union(int[] p, int a, int b)
            {
                a = find(p, a); b = find(p, b);
                if (a == b) return;
                if (a < b) p[b] = a; else p[a] = b;
            }

            // closed manifold vertex: all edges interior, the triangles around it one fan
            static bool vertex_bad(DMesh3 m, int v)
            {
                var tris = new List<int>();
                foreach (int t in m.VtxTrianglesItr(v)) tris.Add(t);
                if (tris.Count == 0) return false;      // isolated: not a defect of a surface
                foreach (int e in m.VtxEdgesItr(v))
                    if (m.IsBoundaryEdge(e)) return true;
                var seen = new HashSet<int> { tris[0] };
                var stack = new Stack<int>(); stack.Push(tris[0]);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    Index3i nb = m.GetTriNeighbourTris(t);
                    for (int k = 0; k < 3; ++k) {
                        int u = nb[k];
                        if (u < 0 || seen.Contains(u)) continue;
                        Index3i tu = m.GetTriangle(u);
                        if (tu.a != v && tu.b != v && tu.c != v) continue;
                        seen.Add(u); stack.Push(u);
                    }
                }
                return seen.Count != tris.Count;
            }

            // QEF vertex of the component (level l, root r) and the fit tests
            void evaluate(int l, int r)
            {
                List<int> mem = members[l][r];
                var cross = dc.VertexCrossings;
                var edges = new List<int>();
                foreach (int v in mem) edges.AddRange(cross[v]);
                edges.Sort();
                int w = 0;
                for (int i = 0; i < edges.Count; ++i)
                    if (i == 0 || edges[i] != edges[i - 1]) edges[w++] = edges[i];
                edges.RemoveRange(w, edges.Count - w);
                Vector3i c0 = cell[mem[0]];
                Vector3d bmin = origin + owner.CellSize * new Vector3d((c0.x >> l) << l, (c0.y >> l) << l, (c0.z >> l) << l);
                double bsize = owner.CellSize * (1 << l);
                Vector3d mass, pn; bool feat;
                Vector3d x = dc.solve_qef(bmin, bsize, edges, dc.HermitePoints, dc.HermiteNormals, out mass, out pn, out feat);
                bool ok = !(double.IsNaN(x.x) || double.IsNaN(x.y) || double.IsNaN(x.z) || double.IsInfinity(x.x) || double.IsInfinity(x.y) || double.IsInfinity(x.z));
                if (ok) {
                    foreach (int ci in edges) {
                        double d = Math.Abs(dc.HermiteNormals[ci].Dot(x - dc.HermitePoints[ci]));
                        if (!(d <= tol)) { ok = false; break; }
                    }
                }
                cpos[l][r] = x;
                fit[l][r] = (sbyte)(ok ? 1 : -1);
            }

            // bottom-up merge decisions for the current caps
            void decide()
            {
                for (int l = 1; l <= Lmax; ++l) {
                    var todo = new List<int>();
                    foreach (int r in roots[l]) {
                        int[] ch = children[l][r];
                        bool allChildren = true;
                        foreach (int c in ch) allChildren &= coll[l - 1][c];
                        if (ch.Length == 1) {
                            int c = ch[0];
                            coll[l][r] = coll[l - 1][c]; mlev[l][r] = mlev[l - 1][c]; cpos[l][r] = cpos[l - 1][c];
                            continue;
                        }
                        bool capOk = true;
                        foreach (int v in members[l][r]) if (cap[v] < l) { capOk = false; break; }
                        mlev[l][r] = l;
                        coll[l][r] = false;
                        if (allChildren && capOk) {
                            if (fit[l][r] == 0) todo.Add(r);
                            else coll[l][r] = fit[l][r] > 0;
                        }
                    }
                    if (todo.Count > 0) {
                        int lv = l;
                        if (owner.ParallelCompute) gParallel.ForEach(Interval1i.Range(todo.Count), (i) => evaluate(lv, todo[i]));
                        else for (int i = 0; i < todo.Count; ++i) evaluate(lv, todo[i]);
                        foreach (int r in todo) coll[l][r] = fit[l][r] > 0;
                    }
                }
            }

            struct Key : IEquatable<Key>
            {
                public int Level, Root;
                public bool Equals(Key o) { return Level == o.Level && Root == o.Root; }
                public override int GetHashCode() { return Level * 7919 + Root; }
            }

            public DMesh3 Run()
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int round = 0; ; ++round) {
                    owner.CheckRounds = round + 1;
                    if (owner.CancelF()) return null;
                    if (round > 200) for (int v = 0; v < NV; ++v) cap[v] = 0;       // (not expected: back to the uniform contour)
                    decide();
                    List<Key> clusterOfOut;
                    var outMesh = assemble(out clusterOfOut, out Dictionary<Key, int> ids, out List<Key> culprits);
                    if (culprits.Count == 0)
                        culprits = intersection_culprits(outMesh, clusterOfOut);
                    if (culprits.Count == 0 || round > 200) {
                        finish(ids);
                        return outMesh;
                    }
                    int rejected = 0;
                    foreach (Key k in culprits) {
                        bool any = false;
                        foreach (int v in members[k.Level][k.Root])
                            if (cap[v] >= mlev[k.Level][k.Root]) { cap[v] = mlev[k.Level][k.Root] - 1; any = true; }
                        if (any) rejected++;
                    }
                    owner.RejectedClusters += rejected;
                    if (rejected == 0) for (int v = 0; v < NV; ++v) cap[v] = 0;
                }
            }

            // top cluster of each mergeable vertex
            Key top_key(int v, out bool merged)
            {
                for (int l = Lmax; l >= 1; --l) {
                    int r = root[l][v];
                    if (coll[l][r]) {
                        merged = mlev[l][r] >= 1;
                        return new Key() { Level = mlev[l][r], Root = r };
                    }
                }
                merged = false;
                return new Key() { Level = 0, Root = v };
            }

            DMesh3 assemble(out List<Key> clusterOfOut, out Dictionary<Key, int> clusterIds, out List<Key> culprits)
            {
                var outMesh = new DMesh3(false, false, false, false);
                var map = new int[NV];
                clusterIds = new Dictionary<Key, int>();
                clusterOfOut = new List<Key>();
                var keyOfVertex = new Key[NV];
                var isCluster = new bool[NV];
                for (int v = 0; v < NV; ++v) {
                    map[v] = -1;
                    if (M0.IsVertex(v) == false) continue;
                    bool merged = false;
                    Key k = default(Key);
                    if (mergeable[v]) k = top_key(v, out merged);
                    if (merged) {
                        isCluster[v] = true; keyOfVertex[v] = k;
                        int id;
                        if (clusterIds.TryGetValue(k, out id) == false) {
                            id = outMesh.AppendVertex(cpos[Math.Max(k.Level, 0)][k.Root]);
                            clusterIds[k] = id;
                            while (clusterOfOut.Count <= id) clusterOfOut.Add(new Key() { Level = -1 });
                            clusterOfOut[id] = k;
                        }
                        map[v] = id;
                    } else {
                        int id = outMesh.AppendVertex(P0[v]);
                        while (clusterOfOut.Count <= id) clusterOfOut.Add(new Key() { Level = -1 });
                        map[v] = id;
                    }
                }
                // cpos of a cluster is stored at its merge level (cpos[l][r] for l = mlev); fix positions
                foreach (var kv in clusterIds) outMesh.SetVertex(kv.Value, cpos[kv.Key.Level][kv.Key.Root]);
                culprits = new List<Key>();
                var bad = new HashSet<Key>();
                // (degenerate area of NotCleanReason: relative to the bounds of the result)
                double diag = outMesh.GetBounds().DiagonalLength, degenerateArea = 1e-12 * diag * diag;
                foreach (int t in M0.TriangleIndices()) {
                    Index3i it = M0.GetTriangle(t);
                    int a = map[it.a], b = map[it.b], c = map[it.c];
                    if (a == b || b == c || a == c) continue;
                    // (area below the degenerate area of MeshRobustRepair.NotCleanReason: three cluster vertices on one crease)
                    bool anyCluster = isCluster[it.a] || isCluster[it.b] || isCluster[it.c];
                    if (anyCluster && 0.5 * (outMesh.GetVertex(b) - outMesh.GetVertex(a)).Cross(outMesh.GetVertex(c) - outMesh.GetVertex(a)).Length < degenerateArea) {
                        foreach (int v in new[] { it.a, it.b, it.c }) if (isCluster[v]) bad.Add(keyOfVertex[v]);
                        continue;
                    }
                    // a triangle that would give an edge a third triangle is left out; the hole is found below (boundary edges at its vertices)
                    outMesh.AppendTriangle(a, b, c);
                }
                // vertices around clusters: fan and boundary test, compared with the uniform contour
                var checkedOut = new HashSet<int>();
                var toCheck = new List<int>();
                foreach (var kv in clusterIds) {
                    if (checkedOut.Add(kv.Value)) toCheck.Add(kv.Value);
                    if (outMesh.IsVertex(kv.Value))
                        foreach (int nb in outMesh.VtxVerticesItr(kv.Value)) if (checkedOut.Add(nb)) toCheck.Add(nb);
                }
                var baseOf = new Dictionary<int, int>();
                for (int v = 0; v < NV; ++v) if (M0.IsVertex(v) && !isCluster[v]) baseOf[map[v]] = v;
                foreach (int o in toCheck) {
                    if (outMesh.IsVertex(o) == false || !vertex_bad(outMesh, o)) continue;
                    if (clusterOfOut[o].Level >= 1) { bad.Add(clusterOfOut[o]); continue; }
                    int bv;
                    if (baseOf.TryGetValue(o, out bv) && baseBad[bv]) continue;
                    foreach (int nb in outMesh.VtxVerticesItr(o))
                        if (clusterOfOut[nb].Level >= 1) bad.Add(clusterOfOut[nb]);
                }
                culprits.AddRange(bad);
                culprits.Sort((p, q) => p.Level != q.Level ? p.Level.CompareTo(q.Level) : p.Root.CompareTo(q.Root));
                return outMesh;
            }

            List<Key> intersection_culprits(DMesh3 m, List<Key> clusterOfOut)
            {
                var result = new List<Key>();
                var cand = new List<int>();
                foreach (int t in m.TriangleIndices()) {
                    Index3i it = m.GetTriangle(t);
                    if (clusterOfOut[it.a].Level >= 1 || clusterOfOut[it.b].Level >= 1 || clusterOfOut[it.c].Level >= 1) cand.Add(t);
                }
                if (cand.Count == 0) return result;
                var candSet = new HashSet<int>(cand);
                var tree = new DMeshAABBTree3(m, true);
                object lockObj = new object();
                var bad = new HashSet<Key>();
                Action<int> check = (k) => {
                    int ta = cand[k];
                    Index3i ia = m.GetTriangle(ta);
                    Triangle3d A = new Triangle3d(m.GetVertex(ia.a), m.GetVertex(ia.b), m.GetVertex(ia.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref A)) return;
                    AxisAlignedBox3d box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                    var hits = new List<int>();
                    tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                        NextBoxDF = (b, depth) => b.Intersects(box),
                        NextTriangleF = (tb) => {
                            if (tb == ta || (tb < ta && candSet.Contains(tb))) return;
                            Index3i ib = m.GetTriangle(tb);
                            Triangle3d B = new Triangle3d(m.GetVertex(ib.a), m.GetVertex(ib.b), m.GetVertex(ib.c));
                            if (ExactTriangleIntersection.IsDegenerate(ref B) == false && ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B))
                                hits.Add(tb);
                        }
                    });
                    if (hits.Count == 0) return;
                    lock (lockObj) {
                        foreach (int v in new[] { ia.a, ia.b, ia.c })
                            if (clusterOfOut[v].Level >= 1) bad.Add(clusterOfOut[v]);
                        foreach (int tb in hits) {
                            Index3i ib = m.GetTriangle(tb);
                            foreach (int v in new[] { ib.a, ib.b, ib.c })
                                if (clusterOfOut[v].Level >= 1) bad.Add(clusterOfOut[v]);
                        }
                    }
                };
                if (owner.ParallelCompute) gParallel.ForEach(Interval1i.Range(cand.Count), check);
                else for (int k = 0; k < cand.Count; ++k) check(k);
                result.AddRange(bad);
                result.Sort((p, q) => p.Level != q.Level ? p.Level.CompareTo(q.Level) : p.Root.CompareTo(q.Root));
                return result;
            }

            void finish(Dictionary<Key, int> ids)
            {
                owner.Clusters = ids.Count;
                int maxL = 0;
                foreach (Key k in ids.Keys) maxL = Math.Max(maxL, k.Level);
                owner.ClustersByLevel = new int[maxL + 1];
                int merged = 0;
                foreach (Key k in ids.Keys) { owner.ClustersByLevel[k.Level]++; merged += members[k.Level][k.Root].Count; }
                owner.MergedVertices = merged;
            }
        }
    }
}
