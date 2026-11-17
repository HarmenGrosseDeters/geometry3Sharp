// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> Result of QuadRemesher.Compute(). </summary>
    public enum QuadRemeshStatus
    {
        Success = 0,
        /// <summary> no mesh, or a TargetEdgeLength that is not positive and finite </summary>
        InvalidInput = 1,
        /// <summary> the cross field failed (see FieldStatus: InvalidMesh, NotManifold, SolverFailed) </summary>
        FieldFailed = 2,
        /// <summary> the position field failed </summary>
        PositionFieldFailed = 3,
        /// <summary> the extraction found no face (the target edge length is probably far above the size of the shape) </summary>
        NoFaces = 4
    }

    /// <summary>
    /// Quad-dominant remeshing by an instant field-aligned mesh: the cross field (CrossField, Knöppel et al. 2013), the position
    /// field (PositionField, Jakob et al. 2015) and the extraction of the polygon mesh from the integer lattice offsets with a
    /// cleanup, after the same paper (written from the paper; docs/design/QUAD_REMESHING.md stage 2). Mostly quads with a few
    /// triangles, pentagons and hexagons near the singularities.
    ///
    /// The extraction: two neighbouring vertices whose lattice points are the same are merged (union-find over the edges of the
    /// mesh), neighbours with lattice points one step apart become an edge of the polygon graph, and the faces are traced in the
    /// tangent planes. See Cleanup for the repairs that follow.
    ///
    /// Input: a manifold triangle mesh (open boundaries are lattice constraints, see PositionField) and TargetEdgeLength, the
    /// edge length of the quads. Field: a CrossField of the mesh that was computed successfully; if null, the smoothest field
    /// (default settings) is computed. The mesh is not modified. Result: a DPolyMesh (outward as the input's orientation; vertices
    /// are on the tangent planes of the input, not projected). Deterministic: the same input gives the same output bit for bit,
    /// serial or parallel; power of two scalings of the mesh and of TargetEdgeLength give the same polygon mesh up to the scaling.
    /// Compute() can be called again.
    /// </summary>
    public class QuadRemesher
    {
        public DMesh3 Mesh;
        /// <summary> optional: a computed cross field of Mesh </summary>
        public CrossField Field;
        /// <summary> the edge length of the quads </summary>
        public double TargetEdgeLength = 0;
        /// <summary> Gauss-Seidel sweeps per level of the position field </summary>
        public int Sweeps = 20;
        public bool Parallel = true;
        /// <summary> faces with more sides are not part of the result (a hole, normally none) </summary>
        public int MaxFaceSides = 6;
        /// <summary> the repairs after the extraction (collapse of short edges, valence 2 vertices, T-junctions) </summary>
        public bool Cleanup = true;
        /// <summary> edges whose lattice points do not agree (the two roundings are further apart than this, in units of the edge length) do not vote </summary>
        public double MismatchLimit = 0.5;
        /// <summary> see PositionField.MiddleRegularization </summary>
        public double MiddleRegularization = 0.05;

        internal int ChunkSize = 512;

        public QuadRemeshStatus Status = QuadRemeshStatus.Success;
        public string Error;
        public CrossFieldStatus FieldStatus = CrossFieldStatus.Success;
        public DPolyMesh Result;
        /// <summary> seconds of the last Compute(): the cross field (0 if given), the position field, the extraction with its cleanup </summary>
        public double FieldSeconds, PositionSeconds, ExtractSeconds;
        /// <summary> the position field of the last run (for inspection) </summary>
        public PositionField Positions;

        public QuadRemesher(DMesh3 mesh) { Mesh = mesh; }

        public bool Success { get { return Status == QuadRemeshStatus.Success; } }

        public QuadRemeshStatus Compute()
        {
            Status = QuadRemeshStatus.Success; Error = null; FieldStatus = CrossFieldStatus.Success; Result = null; Positions = null;
            if (Mesh == null || Mesh.TriangleCount == 0) return Fail(QuadRemeshStatus.InvalidInput, "no triangles");
            if (!(TargetEdgeLength > 0) || double.IsInfinity(TargetEdgeLength)) return Fail(QuadRemeshStatus.InvalidInput, "TargetEdgeLength must be positive and finite");
            FieldSeconds = PositionSeconds = ExtractSeconds = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CrossField cf = Field;
            if (cf == null) { cf = new CrossField(Mesh); cf.Compute(); FieldSeconds = sw.Elapsed.TotalSeconds; }
            sw.Restart();
            if (!cf.Success) { FieldStatus = cf.Status; return Fail(QuadRemeshStatus.FieldFailed, cf.Error); }
            var pf = new PositionField(Mesh, cf) { TargetEdgeLength = TargetEdgeLength, Sweeps = Sweeps, Parallel = Parallel, MiddleRegularization = MiddleRegularization, ChunkSize = ChunkSize };
            Positions = pf;
            if (pf.Compute() != PositionFieldStatus.Success) return Fail(QuadRemeshStatus.PositionFieldFailed, pf.Error);
            PositionSeconds = sw.Elapsed.TotalSeconds; sw.Restart();
            Result = Extract(pf);
            ExtractSeconds = sw.Elapsed.TotalSeconds;
            if (Result.FaceCount == 0) { Result = null; return Fail(QuadRemeshStatus.NoFaces, "no face was extracted"); }
            return Status;
        }

        QuadRemeshStatus Fail(QuadRemeshStatus s, string error) { Status = s; Error = error; return s; }

        // ------------------------------------------------------------------------------------------------ extraction

        internal sealed class PolyGraph
        {
            public List<Vector3d> Pos = new List<Vector3d>();
            public List<Vector3d> Nrm = new List<Vector3d>();
            public List<List<int>> Adj = new List<List<int>>();
            public List<byte> Kind = new List<byte>();      // 0 interior, 1 boundary, 2 corner of the boundary
            public int Add(Vector3d p, Vector3d n, byte kind) { Pos.Add(p); Nrm.Add(n); Adj.Add(new List<int>()); Kind.Add(kind); return Pos.Count - 1; }
            public bool Has(int a, int b) { return Adj[a].Contains(b); }
            public void Link(int a, int b) { if (a == b || Adj[a].Contains(b)) return; Adj[a].Add(b); Adj[b].Add(a); }
            public void Unlink(int a, int b) { Adj[a].Remove(b); Adj[b].Remove(a); }
        }

        static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

        DPolyMesh Extract(PositionField pf)
        {
            PositionField.Level L = pf.L0;
            int n = L.N;
            int[] uf = new int[n];
            for (int i = 0; i < n; ++i) uf[i] = i;
            var unitA = new List<int>(); var unitB = new List<int>();
            for (int i = 0; i < n; ++i)
                for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) {
                    int j = L.AJ[k];
                    if (j < i) continue;
                    int dq, dt; double mismatch;
                    pf.SlotOffset(i, k, out dq, out dt, out mismatch);
                    if (mismatch > MismatchLimit) continue;
                    if (dq == 0 && dt == 0) { int ri = Find(uf, i), rj = Find(uf, j); if (ri != rj) uf[Math.Max(ri, rj)] = Math.Min(ri, rj); }
                    else if (Math.Abs(dq) + Math.Abs(dt) == 1) { unitA.Add(i); unitB.Add(j); }
                }
            var g = new PolyGraph();
            int[] id = new int[n];
            for (int i = 0; i < n; ++i) id[i] = -1;
            // a cluster is at the mean lattice point of its members; the members of the highest kind decide (a corner of the boundary
            // is a lattice point itself, a boundary vertex is on its lattice line: interior members must not pull them off)
            var sumP = new List<Vector3d>(); var sumN = new List<Vector3d>(); var cnt = new List<int>(); var kinds = new List<byte>();
            for (int pass = 0; pass < 2; ++pass)
                for (int i = 0; i < n; ++i) {
                    int r = Find(uf, i);
                    if (pass == 0) {
                        if (id[r] < 0) { id[r] = sumP.Count; sumP.Add(Vector3d.Zero); sumN.Add(Vector3d.Zero); cnt.Add(0); kinds.Add(0); }
                        int c0 = id[r];
                        sumN[c0] += L.Nrm[i];
                        if (L.Kind[i] > kinds[c0]) kinds[c0] = L.Kind[i];
                    } else {
                        int c = id[r];
                        if (L.Kind[i] != kinds[c]) continue;
                        sumP[c] += pf.Origins[i]; cnt[c]++;
                    }
                }
            for (int c = 0; c < sumP.Count; ++c) {
                Vector3d nn = sumN[c]; double nl = nn.Length;
                g.Add(sumP[c] / cnt[c], nl > 0 ? nn / nl : sumN[c], kinds[c]);
            }
            for (int u = 0; u < unitA.Count; ++u) {
                int a = id[Find(uf, unitA[u])], b = id[Find(uf, unitB[u])];
                if (a != b) g.Link(a, b);
            }
            CollapsedEdges = 0; RemovedValence2 = 0;
            if (Cleanup) CleanupGraph(g, pf.TargetEdgeLength, CollapseLength, out CollapsedEdges, out RemovedValence2);
            int dropped;
            DPolyMesh poly = BuildFaces(g, MaxFaceSides, out dropped);
            DroppedFaces = dropped;
            return poly;
        }

        /// <summary> removes the vertices of valence 1 and the chains they leave; true if there was one </summary>
        internal static bool PruneDangling(PolyGraph g)
        {
            bool any = false;
            var stack = new Stack<int>();
            for (int k = 0; k < g.Adj.Count; ++k) if (g.Adj[k].Count == 1) stack.Push(k);
            while (stack.Count > 0) {
                int k = stack.Pop();
                if (g.Adj[k].Count != 1) continue;
                int o = g.Adj[k][0];
                any = true;
                g.Unlink(k, o);
                if (g.Adj[o].Count == 1) stack.Push(o);
            }
            return any;
        }

        /// <summary> edges shorter than this (in units of the edge length) are collapsed by the cleanup: their end points are one point </summary>
        public double CollapseLength = 0.4;
        /// <summary> short edges collapsed and valence 2 vertices removed by the last Compute() </summary>
        public int CollapsedEdges, RemovedValence2;
        /// <summary> faces of the traced graph that are not in the result (the outer loops of an open mesh, folded faces, faces above MaxFaceSides) </summary>
        public int DroppedFaces;

        // The paper's cleanup: collapse of near-coincident points (edges shorter than CollapseLength s, shortest first; a corner
        // of the boundary keeps its position), removal of valence 2 vertices that are not on the boundary (the two edges become
        // one), and the dangling edges that these leave; repeated while something changes.
        internal static void CleanupGraph(PolyGraph g, double s, double collapseLength, out int collapsedEdges, out int removedValence2)
        {
            collapsedEdges = 0; removedValence2 = 0;
            double lim = collapseLength * s;
            for (int round = 0; round < 8; ++round) {
                bool changed = false;
                var cand = new List<(double, int, int)>();
                for (int a = 0; a < g.Pos.Count; ++a)
                    foreach (int b in g.Adj[a])
                        if (a < b) { double l = (g.Pos[a] - g.Pos[b]).Length; if (l < lim) cand.Add((l, a, b)); }
                cand.Sort();
                foreach (var c in cand) {
                    int a = c.Item2, b = c.Item3;
                    if (!g.Has(a, b) || (g.Pos[a] - g.Pos[b]).Length >= lim) continue;
                    if (g.Kind[a] == 2 && g.Kind[b] == 2) continue;
                    if (g.Kind[b] == 2) { int t = a; a = b; b = t; }
                    Vector3d p = g.Kind[a] == 2 ? g.Pos[a] : 0.5 * (g.Pos[a] + g.Pos[b]);
                    if (g.Kind[a] == 2) { } else g.Kind[a] = Math.Max(g.Kind[a], g.Kind[b]);
                    g.Pos[a] = p;
                    Vector3d nn = g.Nrm[a] + g.Nrm[b]; double nl = nn.Length; if (nl > 0) g.Nrm[a] = nn / nl;
                    foreach (int x in g.Adj[b].ToArray()) { g.Unlink(b, x); if (x != a) g.Link(a, x); }
                    collapsedEdges++; changed = true;
                }
                for (int v = 0; v < g.Pos.Count; ++v)
                    if (g.Adj[v].Count == 2 && g.Kind[v] == 0) {
                        int a = g.Adj[v][0], b = g.Adj[v][1];
                        g.Unlink(v, a); g.Unlink(v, b); g.Link(a, b);
                        removedValence2++; changed = true;
                    }
                if (PruneDangling(g)) changed = true;
                if (!changed) break;
            }
        }

        // ------------------------------------------------------------------------------------------------ faces

        /// <summary>
        /// the faces of the polygon graph: for every vertex the neighbours sorted counter-clockwise in the tangent plane (normal Nrm),
        /// every directed edge is traced to the face on its left; the faces are turned to agree with the normals; faces that
        /// face away from the normals (the outer loops of an open graph, folded faces) and faces with more than maxSides sides are
        /// dropped (counted in dropped). Dangling edges are pruned first. Vertices without a face are not in the result.
        /// </summary>
        internal static DPolyMesh BuildFaces(PolyGraph g, int maxSides, out int dropped)
        {
            PruneDangling(g);
            int nv = g.Pos.Count;
            // neighbours around each vertex, counter-clockwise about the normal, from the first neighbour
            var order = new int[nv][];
            var start = new int[nv + 1];
            for (int k = 0; k < nv; ++k) {
                List<int> nb = g.Adj[k];
                if (nb.Count == 0) { order[k] = new int[0]; continue; }
                Vector3d nk = g.Nrm[k];
                Vector3d d0 = g.Pos[nb[0]] - g.Pos[k]; Vector3d e = d0 - d0.Dot(nk) * nk;
                double el = e.Length; if (el > 0) e /= el;
                Vector3d b = nk.Cross(e);
                var keys = new double[nb.Count]; var ids = nb.ToArray();
                for (int u = 0; u < ids.Length; ++u) { Vector3d d = g.Pos[ids[u]] - g.Pos[k]; keys[u] = Math.Atan2(d.Dot(b), d.Dot(e)); }
                Array.Sort(keys, ids);
                order[k] = ids;
            }
            var used = new bool[nv][];
            for (int k = 0; k < nv; ++k) used[k] = new bool[order[k].Length];
            var faces = new List<int[]>();
            for (int u = 0; u < nv; ++u)
                for (int s0 = 0; s0 < order[u].Length; ++s0) {
                    if (used[u][s0]) continue;
                    var face = new List<int>();
                    int a = u, sa = s0; bool closed = true;
                    while (!used[a][sa]) {
                        used[a][sa] = true; face.Add(a);
                        int b = order[a][sa];
                        int r = Array.IndexOf(order[b], a);
                        int sb = (r - 1 + order[b].Length) % order[b].Length;
                        a = b; sa = sb;
                        if (face.Count > 4 * maxSides + 64) { closed = false; break; }
                    }
                    if (closed && a == u && sa == s0) faces.Add(face.ToArray());
                }
            // orientation: face loops are traced with one winding; make it agree with the normals
            double agree = 0;
            var area = new double[faces.Count];
            for (int f = 0; f < faces.Count; ++f) {
                Vector3d nw = Vector3d.Zero, nsum = Vector3d.Zero; int[] fv = faces[f];
                Vector3d p0 = g.Pos[fv[0]];
                for (int k = 1; k + 1 < fv.Length; ++k) nw += (g.Pos[fv[k]] - p0).Cross(g.Pos[fv[k + 1]] - p0);
                foreach (int v in fv) nsum += g.Nrm[v];
                area[f] = nw.Dot(nsum);
                agree += Math.Sign(area[f]);
            }
            bool flip = agree < 0;
            var mesh = new DPolyMesh();
            dropped = 0;
            int[] remap = new int[nv];
            for (int k = 0; k < nv; ++k) remap[k] = -1;
            var buf = new List<int>();
            for (int f = 0; f < faces.Count; ++f) {
                int[] fv = faces[f];
                double a = flip ? -area[f] : area[f];
                if (!(a > 0) || fv.Length < 3 || fv.Length > maxSides) { dropped++; continue; }     // the outer loop of an open mesh, a folded face, a hole
                buf.Clear();
                for (int k = 0; k < fv.Length; ++k) {
                    int v = fv[flip ? fv.Length - 1 - k : k];
                    if (remap[v] < 0) { remap[v] = mesh.AppendVertex(g.Pos[v].x, g.Pos[v].y, g.Pos[v].z); }
                    buf.Add(remap[v]);
                }
                mesh.AppendFace(buf, 0);
            }
            return mesh;
        }
    }
}
