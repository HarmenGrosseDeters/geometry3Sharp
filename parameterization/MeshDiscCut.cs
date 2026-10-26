// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Cuts a mesh into topological discs (one per connected component) along seams, so that it can be
    /// parameterized: closed surfaces of any genus, surfaces with several boundary loops, bowtie vertices.
    ///
    /// The cut graph is computed by tree-cotree decomposition (Eppstein 2003) with a shortest path tree from a
    /// root vertex and a maximum spanning dual tree weighted by the length of the loop through each edge
    /// (Erickson and Whittlesey 2005): on a closed surface the seams are then the greedy shortest system of
    /// 2g loops through the root, along edges. Edges of the primal tree may be used in the dual tree where the
    /// boundary requires it (paths between boundary loops), and the dangling branches of the cut are removed
    /// again, so the seams end on the boundary or at RequiredVertices (eg cone singularities). Closed
    /// surfaces of genus 0 are cut along a shortest path from the root to the farthest vertex (or between
    /// the required vertices). With FeatureWeight &gt; 0 the seams prefer sharp edges, where they are less
    /// visible. Edge costs are edge lengths, so seams follow edge-path geodesics.
    ///
    /// The cut is done in a new mesh, CutMesh: vertices on the seams are split into one copy per side
    /// (wedge), CutToInputVertex / CutToInputTriangle map back to the input. The input is not changed.
    /// </summary>
    public class MeshDiscCut
    {
        public DMesh3 Mesh;
        /// <summary> &gt; 0: edge cost = length / (1 + FeatureWeight * dihedral angle / pi), seams prefer sharp edges </summary>
        public double FeatureWeight = 0;
        /// <summary> input vertex ids the seams must pass through (eg cones); they may be ends of seams </summary>
        public IList<int> RequiredVertices;

        public ParameterizationStatus Status = ParameterizationStatus.Success;
        public string Error;
        /// <summary> the cut mesh: one disc per connected component (compact ids), null on failure </summary>
        public DMesh3 CutMesh;
        /// <summary> input vertex id for each vertex id of CutMesh </summary>
        public int[] CutToInputVertex;
        /// <summary> input triangle id for each triangle id of CutMesh </summary>
        public int[] CutToInputTriangle;
        /// <summary> seams as pairs of input vertex ids (one per cut edge), boundary edges of the input excluded </summary>
        public List<Index2i> SeamEdges = new List<Index2i>();
        /// <summary> total length of the seams </summary>
        public double SeamLength;
        /// <summary> connected components of the input (after splitting bowtie vertices) </summary>
        public int Components;
        /// <summary> sum of the genus of the components of the input </summary>
        public int Genus;
        /// <summary> boundary loops of the input </summary>
        public int InputBoundaryLoops;

        public MeshDiscCut(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public bool Succeeded { get { return Status == ParameterizationStatus.Success; } }

        bool fail(ParameterizationStatus status, string error)
        {
            Status = status; Error = error; CutMesh = null;
            return false;
        }

        public bool Compute()
        {
            Status = ParameterizationStatus.Success; Error = null; CutMesh = null;
            SeamEdges.Clear(); SeamLength = 0; Components = 0; Genus = 0; InputBoundaryLoops = 0;
            if (Mesh.TriangleCount == 0)
                return fail(ParameterizationStatus.InvalidMesh, "mesh has no triangles");
            foreach (int vid in Mesh.VertexIndices()) {
                Vector3d p = Mesh.GetVertex(vid);
                if (!MathUtil.IsFinite(p.x) || !MathUtil.IsFinite(p.y) || !MathUtil.IsFinite(p.z))
                    return fail(ParameterizationStatus.InvalidMesh, "vertex " + vid + " is not finite");
            }

            // split bowtie vertices first: then every vertex has one fan of triangles
            int[] wToV, wToT;
            DMesh3 work = SplitAlongEdges(Mesh, null, out wToV, out wToT);
            if (work == null)
                return fail(ParameterizationStatus.InvalidMesh, "could not split the bowtie vertices");
            int NV = work.MaxVertexID, NT = work.MaxTriangleID, NE = work.MaxEdgeID;

            bool[] required = new bool[NV];
            var requiredOrder = new List<int>();
            var inputToWork = new List<int>[Mesh.MaxVertexID];
            for (int v = 0; v < NV; ++v) {
                if (inputToWork[wToV[v]] == null) inputToWork[wToV[v]] = new List<int>();
                inputToWork[wToV[v]].Add(v);
            }
            if (RequiredVertices != null) {
                foreach (int r in RequiredVertices) {
                    if (r < 0 || r >= Mesh.MaxVertexID || inputToWork[r] == null)
                        return fail(ParameterizationStatus.InvalidConstraints, "required vertex " + r + " is not a vertex of a triangle");
                    foreach (int w in inputToWork[r])
                        if (!required[w]) { required[w] = true; requiredOrder.Add(w); }
                }
            }

            // components (triangles connected through edges, the same as through vertices after the bowtie split)
            int[] comp = new int[NV];
            int[] tparent = new int[NT];
            for (int t = 0; t < NT; ++t) tparent[t] = t;
            foreach (int eid in work.EdgeIndices()) {
                Index2i et = work.GetEdgeT(eid);
                if (et.b != DMesh3.InvalidID) union(tparent, et.a, et.b);
            }
            var compOfRoot = new Dictionary<int, int>();
            int[] tcomp = new int[NT];
            for (int t = 0; t < NT; ++t) {
                int r = find(tparent, t), c;
                if (!compOfRoot.TryGetValue(r, out c)) { c = compOfRoot.Count; compOfRoot[r] = c; }
                tcomp[t] = c;
                Index3i tri = work.GetTriangle(t);
                comp[tri.a] = comp[tri.b] = comp[tri.c] = c;
            }
            int NC = Components = compOfRoot.Count;

            bool[] isBoundary = new bool[NV];
            bool[] compHasBoundary = new bool[NC];
            foreach (int v in work.VertexIndices())
                if (work.IsBoundaryVertex(v)) { isBoundary[v] = true; compHasBoundary[comp[v]] = true; }

            // edge costs
            double[] cost = new double[NE];
            foreach (int eid in work.EdgeIndices()) {
                Index2i ev = work.GetEdgeV(eid);
                double len = work.GetVertex(ev.a).Distance(work.GetVertex(ev.b));
                if (FeatureWeight > 0) {
                    Index2i et = work.GetEdgeT(eid);
                    if (et.b != DMesh3.InvalidID) {
                        double dihedral = Vector3d.AngleR(work.GetTriNormal(et.a), work.GetTriNormal(et.b));
                        if (MathUtil.IsFinite(dihedral))
                            len /= 1 + FeatureWeight * dihedral / Math.PI;
                    }
                }
                cost[eid] = len;
            }

            // root per component: a boundary vertex, else the first required vertex, else the vertex of largest angle defect
            int[] root = new int[NC];
            for (int c = 0; c < NC; ++c) root[c] = -1;
            foreach (int v in work.VertexIndices())
                if (isBoundary[v] && root[comp[v]] < 0) root[comp[v]] = v;
            foreach (int v in requiredOrder)
                if (root[comp[v]] < 0) root[comp[v]] = v;
            double[] defect = new double[NV];
            foreach (int v in work.VertexIndices())
                defect[v] = 2 * Math.PI - angleSum(work, v);
            foreach (int v in work.VertexIndices()) {
                int c = comp[v];
                if (root[c] < 0 || (!compHasBoundary[c] && !required[root[c]] && defect[v] > defect[root[c]]))
                    root[c] = v;
            }

            // shortest path tree from the roots
            double[] dist = new double[NV];
            int[] parentEdge = new int[NV];
            dijkstra(work, cost, root, dist, parentEdge);
            bool[] inTree = new bool[NE];
            foreach (int v in work.VertexIndices())
                if (parentEdge[v] >= 0) inTree[parentEdge[v]] = true;

            // dual spanning tree: maximum by loop length, edges of the primal tree only where needed
            var interior = new List<int>();
            foreach (int eid in work.EdgeIndices())
                if (!work.IsBoundaryEdge(eid)) interior.Add(eid);
            double[] loopLength = new double[NE];
            foreach (int eid in interior) {
                Index2i ev = work.GetEdgeV(eid);
                loopLength[eid] = dist[ev.a] + dist[ev.b] + cost[eid];
            }
            interior.Sort((x, y) => {
                if (inTree[x] != inTree[y]) return inTree[x] ? 1 : -1;
                int k = loopLength[y].CompareTo(loopLength[x]);
                return k != 0 ? k : x.CompareTo(y);
            });
            for (int t = 0; t < NT; ++t) tparent[t] = t;
            bool[] cut = new bool[NE];
            foreach (int eid in interior) {
                Index2i et = work.GetEdgeT(eid);
                if (find(tparent, et.a) != find(tparent, et.b))
                    union(tparent, et.a, et.b);
                else
                    cut[eid] = true;
            }

            // remove dangling branches: interior, not required vertices with one cut edge
            int[] deg = new int[NV];
            foreach (int eid in interior)
                if (cut[eid]) { Index2i ev = work.GetEdgeV(eid); deg[ev.a]++; deg[ev.b]++; }
            var queue = new Queue<int>();
            foreach (int v in work.VertexIndices())
                if (deg[v] == 1 && !isBoundary[v] && !required[v]) queue.Enqueue(v);
            while (queue.Count > 0) {
                int v = queue.Dequeue();
                if (deg[v] != 1) continue;
                foreach (int eid in work.VtxEdgesItr(v)) {
                    if (!cut[eid]) continue;
                    cut[eid] = false;
                    int w = work.edge_other_v(eid, v);
                    deg[v]--; deg[w]--;
                    if (deg[w] == 1 && !isBoundary[w] && !required[w]) queue.Enqueue(w);
                    break;
                }
            }

            // closed components without a cut (genus 0): seams start at the root; without required
            // vertices, cut to the farthest vertex
            bool[] onCut = new bool[NV];
            foreach (int v in work.VertexIndices())
                onCut[v] = deg[v] > 0 || isBoundary[v];
            bool[] compHasCut = new bool[NC];
            bool[] compHasRequired = new bool[NC];
            foreach (int v in work.VertexIndices())
                if (onCut[v]) compHasCut[comp[v]] = true;
            foreach (int v in requiredOrder) compHasRequired[comp[v]] = true;
            var targets = new List<int>(requiredOrder);
            for (int c = 0; c < NC; ++c) {
                if (compHasCut[c]) continue;
                onCut[root[c]] = true;
                if (!compHasRequired[c] || isOnlyRoot(requiredOrder, comp, c, root[c])) {
                    int far = -1;
                    foreach (int v in work.VertexIndices())
                        if (comp[v] == c && (far < 0 || dist[v] > dist[far])) far = v;
                    if (far != root[c]) targets.Add(far);
                }
            }
            // connect the targets to the cut along the shortest path tree
            foreach (int r in targets) {
                int v = r;
                while (!onCut[v]) {
                    onCut[v] = true;
                    int eid = parentEdge[v];
                    if (eid < 0) break;     // a root is always on the cut
                    cut[eid] = true;
                    int w = work.edge_other_v(eid, v);
                    deg[v]++; deg[w]++;
                    v = w;
                }
            }
            // a closed component cut along a single edge can not be split (both ends stay one vertex):
            // extend the seam by the cheapest edge at one end
            int[] cutCount = new int[NC];
            int[] someCut = new int[NC];
            foreach (int eid in interior)
                if (cut[eid]) { int c = comp[work.GetEdgeV(eid).a]; cutCount[c]++; someCut[c] = eid; }
            for (int c = 0; c < NC; ++c) {
                if (compHasBoundary[c] || cutCount[c] != 1) continue;
                Index2i ev = work.GetEdgeV(someCut[c]);
                int best = -1;
                foreach (int end in new[] { ev.b, ev.a }) {
                    foreach (int eid in work.VtxEdgesItr(end))
                        if (!cut[eid] && (best < 0 || cost[eid] < cost[best])) best = eid;
                    if (best >= 0) break;
                }
                if (best < 0)
                    return fail(ParameterizationStatus.InvalidTopology, "closed component is too small to be cut into a disc");
                cut[best] = true;
            }

            // genus and boundary loops of the input
            int[] cV = new int[NC], cE = new int[NC], cF = new int[NC], cB = new int[NC];
            foreach (int v in work.VertexIndices()) cV[comp[v]]++;
            foreach (int t in work.TriangleIndices()) cF[tcomp[t]]++;
            foreach (int eid in work.EdgeIndices()) cE[comp[work.GetEdgeV(eid).a]]++;
            bool[] seenB = new bool[NV];
            foreach (int v in work.VertexIndices()) {
                if (!isBoundary[v] || seenB[v]) continue;
                cB[comp[v]]++;
                int u = v;
                while (!seenB[u]) { seenB[u] = true; u = nextBoundaryVertex(work, u); if (u < 0) break; }
            }
            for (int c = 0; c < NC; ++c) {
                Genus += (2 - (cV[c] - cE[c] + cF[c]) - cB[c]) / 2;
                InputBoundaryLoops += cB[c];
            }

            // cut the mesh
            int[] cToW, cToWT;
            DMesh3 cutMesh = SplitAlongEdges(work, (eid) => cut[eid], out cToW, out cToWT);
            if (cutMesh == null)
                return fail(ParameterizationStatus.InvalidTopology, "cutting along the seams gave a non-manifold mesh");
            CutToInputVertex = new int[cToW.Length];
            for (int i = 0; i < cToW.Length; ++i) CutToInputVertex[i] = wToV[cToW[i]];
            CutToInputTriangle = new int[cToWT.Length];
            for (int i = 0; i < cToWT.Length; ++i) CutToInputTriangle[i] = wToT[cToWT[i]];
            foreach (int eid in interior) {
                if (!cut[eid]) continue;
                Index2i ev = work.GetEdgeV(eid);
                SeamEdges.Add(new Index2i(wToV[ev.a], wToV[ev.b]));
                SeamLength += work.GetVertex(ev.a).Distance(work.GetVertex(ev.b));
            }
            string why;
            if (!IsDiscs(cutMesh, out why))
                return fail(ParameterizationStatus.InvalidTopology, "the cut mesh is not a set of discs: " + why);
            CutMesh = cutMesh;
            return true;
        }

        static bool isOnlyRoot(List<int> requiredOrder, int[] comp, int c, int root)
        {
            foreach (int v in requiredOrder)
                if (comp[v] == c && v != root) return false;
            return true;
        }

        static double angleSum(DMesh3 mesh, int v)
        {
            double sum = 0;
            foreach (int tid in mesh.VtxTrianglesItr(v)) {
                Index3i t = mesh.GetTriangle(tid);
                int j = t.a == v ? 0 : (t.b == v ? 1 : 2);
                Vector3d p = mesh.GetVertex(v);
                double a = ParamMath.Angle(mesh.GetVertex(t[(j + 1) % 3]) - p, mesh.GetVertex(t[(j + 2) % 3]) - p);
                if (MathUtil.IsFinite(a)) sum += a;
            }
            return sum;
        }

        // the boundary edge leaving v in the direction of the triangles (v must be a boundary vertex of a manifold fan)
        static int nextBoundaryVertex(DMesh3 mesh, int v)
        {
            foreach (int eid in mesh.VtxEdgesItr(v)) {
                if (!mesh.IsBoundaryEdge(eid)) continue;
                Index3i t = mesh.GetTriangle(mesh.GetEdgeT(eid).a);
                int w = mesh.edge_other_v(eid, v);
                for (int j = 0; j < 3; ++j)
                    if (t[j] == v && t[(j + 1) % 3] == w) return w;
            }
            return -1;
        }

        static int indexOf(Index3i t, int v) { return t.a == v ? 0 : (t.b == v ? 1 : 2); }

        static int find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        static void union(int[] parent, int a, int b)
        {
            a = find(parent, a); b = find(parent, b);
            if (a == b) return;
            if (a < b) parent[b] = a; else parent[a] = b;
        }

        static void dijkstra(DMesh3 mesh, double[] cost, int[] roots, double[] dist, int[] parentEdge)
        {
            for (int i = 0; i < dist.Length; ++i) { dist[i] = double.PositiveInfinity; parentEdge[i] = -1; }
            var heap = new SortedSet<KeyValuePair<double, int>>(Comparer<KeyValuePair<double, int>>.Create((x, y) => {
                int k = x.Key.CompareTo(y.Key);
                return k != 0 ? k : x.Value.CompareTo(y.Value);
            }));
            foreach (int r in roots) {
                dist[r] = 0;
                heap.Add(new KeyValuePair<double, int>(0, r));
            }
            while (heap.Count > 0) {
                var top = heap.Min;
                heap.Remove(top);
                int v = top.Value;
                foreach (int eid in mesh.VtxEdgesItr(v)) {
                    int w = mesh.edge_other_v(eid, v);
                    double d = dist[v] + cost[eid];
                    if (d < dist[w]) {
                        if (!double.IsPositiveInfinity(dist[w]))
                            heap.Remove(new KeyValuePair<double, int>(dist[w], w));
                        dist[w] = d;
                        parentEdge[w] = eid;
                        heap.Add(new KeyValuePair<double, int>(d, w));
                    }
                }
            }
        }


        /// <summary>
        /// A new mesh in which the triangles are connected only across edges that are not cut (isCut null: no
        /// edge is cut, which only splits bowtie vertices): each vertex gets one copy per fan of triangles
        /// that stay connected around it. Vertex attributes and triangle groups are copied. Returns null if a
        /// cut edge can not be split (both of its ends stay one vertex, eg a closed mesh cut along a single
        /// edge) or the result would not be edge-manifold.
        /// </summary>
        public static DMesh3 SplitAlongEdges(DMesh3 mesh, Func<int, bool> isCut, out int[] toInputVertex, out int[] toInputTriangle)
        {
            int NT = mesh.MaxTriangleID;
            int[] corner = new int[3 * NT];
            for (int i = 0; i < corner.Length; ++i) corner[i] = i;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID || (isCut != null && isCut(eid)))
                    continue;
                Index2i ev = mesh.GetEdgeV(eid);
                Index3i t0 = mesh.GetTriangle(et.a), t1 = mesh.GetTriangle(et.b);
                union(corner, 3 * et.a + indexOf(t0, ev.a), 3 * et.b + indexOf(t1, ev.a));
                union(corner, 3 * et.a + indexOf(t0, ev.b), 3 * et.b + indexOf(t1, ev.b));
            }
            // a cut edge whose ends both stay one vertex would be glued again
            if (isCut != null) {
                foreach (int eid in mesh.EdgeIndices()) {
                    Index2i et = mesh.GetEdgeT(eid);
                    if (et.b == DMesh3.InvalidID || !isCut(eid))
                        continue;
                    Index2i ev = mesh.GetEdgeV(eid);
                    Index3i t0 = mesh.GetTriangle(et.a), t1 = mesh.GetTriangle(et.b);
                    if (find(corner, 3 * et.a + indexOf(t0, ev.a)) == find(corner, 3 * et.b + indexOf(t1, ev.a))
                        && find(corner, 3 * et.a + indexOf(t0, ev.b)) == find(corner, 3 * et.b + indexOf(t1, ev.b))) {
                        toInputVertex = null; toInputTriangle = null;
                        return null;
                    }
                }
            }
            var result = new DMesh3(mesh.HasVertexNormals, mesh.HasVertexColors, mesh.HasVertexUVs, mesh.HasTriangleGroups);
            int[] newVertex = new int[3 * NT];
            for (int i = 0; i < newVertex.Length; ++i) newVertex[i] = -1;
            var toV = new List<int>();
            var toT = new List<int>();
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Index3i nt = Index3i.Zero;
                for (int j = 0; j < 3; ++j) {
                    int r = find(corner, 3 * tid + j);
                    if (newVertex[r] < 0) {
                        newVertex[r] = result.AppendVertex(mesh, t[j]);
                        toV.Add(t[j]);
                    }
                    nt[j] = newVertex[r];
                }
                int ntid = result.AppendTriangle(nt, mesh.HasTriangleGroups ? mesh.GetTriangleGroup(tid) : -1);
                if (ntid < 0) {
                    toInputVertex = null; toInputTriangle = null;
                    return null;
                }
                toT.Add(tid);
            }
            toInputVertex = toV.ToArray();
            toInputTriangle = toT.ToArray();
            return result;
        }


        /// <summary>
        /// true if every connected component of the mesh is a topological disc: one boundary loop and Euler
        /// characteristic 1 (bowtie vertices are not allowed)
        /// </summary>
        public static bool IsDiscs(DMesh3 mesh, out string why)
        {
            why = null;
            if (mesh.TriangleCount == 0) { why = "no triangles"; return false; }
            int[] tparent = new int[mesh.MaxTriangleID];
            for (int t = 0; t < tparent.Length; ++t) tparent[t] = t;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b != DMesh3.InvalidID) union(tparent, et.a, et.b);
            }
            var compOf = new Dictionary<int, int>();
            int[] vcomp = new int[mesh.MaxVertexID];
            for (int i = 0; i < vcomp.Length; ++i) vcomp[i] = -1;
            foreach (int t in mesh.TriangleIndices()) {
                int r = find(tparent, t), c;
                if (!compOf.TryGetValue(r, out c)) { c = compOf.Count; compOf[r] = c; }
                Index3i tri = mesh.GetTriangle(t);
                for (int j = 0; j < 3; ++j) {
                    if (vcomp[tri[j]] >= 0 && vcomp[tri[j]] != c) { why = "bowtie vertex " + tri[j]; return false; }
                    vcomp[tri[j]] = c;
                }
            }
            int NC = compOf.Count;
            int[] euler = new int[NC], loops = new int[NC];
            foreach (int v in mesh.VertexIndices())
                if (vcomp[v] >= 0) euler[vcomp[v]]++;
            foreach (int t in mesh.TriangleIndices()) euler[vcomp[mesh.GetTriangle(t).a]]++;
            foreach (int eid in mesh.EdgeIndices()) euler[vcomp[mesh.GetEdgeV(eid).a]]--;
            bool[] seen = new bool[mesh.MaxVertexID];
            foreach (int v in mesh.VertexIndices()) {
                if (seen[v] || vcomp[v] < 0 || !mesh.IsBoundaryVertex(v)) continue;
                if (mesh.IsBowtieVertex(v)) { why = "bowtie vertex " + v; return false; }
                loops[vcomp[v]]++;
                int u = v;
                while (u >= 0 && !seen[u]) { seen[u] = true; u = nextBoundaryVertex(mesh, u); }
            }
            for (int c = 0; c < NC; ++c)
                if (euler[c] != 1 || loops[c] != 1) {
                    why = "a component has Euler characteristic " + euler[c] + " and " + loops[c] + " boundary loops";
                    return false;
                }
            return true;
        }
    }
}
