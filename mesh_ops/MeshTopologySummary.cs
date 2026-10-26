// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Topology of a mesh per connected component (triangles connected across edges): vertices V, edges E and
    /// triangles F used by the component, Euler characteristic chi = V - E + F, boundary loops b and genus
    /// g = (2 - chi - b) / 2 (chi = 2 - 2g - b for an orientable surface with boundary). The genus is an integer
    /// for orientable manifold components; bowtie vertices are counted once (as in the mesh), so a component
    /// with bowties can get a genus that is not an integer or not its genus when split there (BowtieVertices
    /// says when). Boundary loops are the connected parts of the boundary edges (two loops through one bowtie
    /// vertex count as one). Purely combinatorial: invariant under any transform.
    /// </summary>
    public class MeshTopologySummary
    {
        public int Components, Vertices, Edges, Triangles;
        public int BoundaryEdges, BoundaryLoops, BowtieVertices;
        /// <summary> components without boundary edges </summary>
        public int ClosedComponents;
        /// <summary> sum over the components </summary>
        public int EulerCharacteristic;
        /// <summary> sum of the genus of the components (handles) </summary>
        public double Genus;
        /// <summary> the components, largest (most triangles) first, ties in the order found </summary>
        public List<ComponentTopology> ComponentList = new List<ComponentTopology>();

        public struct ComponentTopology
        {
            public int Vertices, Edges, Triangles, BoundaryLoops, EulerCharacteristic;
            public double Genus;
        }

        /// <summary>
        /// what changed from one mesh (eg the input) to another (eg the repair result), by the totals; a handle closed
        /// on one component and one created on another (same total genus, other genera of the components) sets both
        /// HandlesClosed and HandlesCreated. Weld triangle soups first (each unwelded triangle is a component).
        /// </summary>
        [Flags]
        public enum Changes
        {
            None = 0,
            /// <summary> fewer components (parts lost or merged) </summary>
            ComponentsLost = 1,
            /// <summary> more components (parts split off or added) </summary>
            ComponentsAdded = 2,
            /// <summary> smaller total genus: handles closed </summary>
            HandlesClosed = 4,
            /// <summary> larger total genus: handles created </summary>
            HandlesCreated = 8,
            /// <summary> fewer boundary loops (holes filled) </summary>
            BoundaryLoopsRemoved = 16,
            /// <summary> more boundary loops (holes opened) </summary>
            BoundaryLoopsAdded = 32
        }

        public static Changes Compare(MeshTopologySummary from, MeshTopologySummary to)
        {
            Changes c = Changes.None;
            if (to.Components < from.Components) c |= Changes.ComponentsLost;
            if (to.Components > from.Components) c |= Changes.ComponentsAdded;
            if (to.Genus < from.Genus) c |= Changes.HandlesClosed;
            if (to.Genus > from.Genus) c |= Changes.HandlesCreated;
            if (to.Genus == from.Genus && same_genera(from, to) == false)
                c |= Changes.HandlesClosed | Changes.HandlesCreated;    // moved between components
            if (to.BoundaryLoops < from.BoundaryLoops) c |= Changes.BoundaryLoopsRemoved;
            if (to.BoundaryLoops > from.BoundaryLoops) c |= Changes.BoundaryLoopsAdded;
            return c;
        }

        // the components of positive genus have the same genera (sorted), so no handle moved between components
        static bool same_genera(MeshTopologySummary a, MeshTopologySummary b)
        {
            var ga = new List<double>(); var gb = new List<double>();
            foreach (var c in a.ComponentList) if (c.Genus != 0) ga.Add(c.Genus);
            foreach (var c in b.ComponentList) if (c.Genus != 0) gb.Add(c.Genus);
            ga.Sort(); gb.Sort();
            if (ga.Count != gb.Count) return false;
            for (int i = 0; i < ga.Count; ++i)
                if (ga[i] != gb[i]) return false;
            return true;
        }

        public static MeshTopologySummary Compute(DMesh3 mesh)
        {
            var r = new MeshTopologySummary();
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            // marks per component (stamp = component index + 1), so every element is counted once per component
            int[] vstamp = new int[mesh.MaxVertexID], estamp = new int[mesh.MaxEdgeID];
            int[] parent = new int[mesh.MaxVertexID];
            var boundaryVerts = new List<int>();
            var roots = new HashSet<int>();
            var list = new List<ComponentTopology>();
            int stamp = 0;
            foreach (var comp in comps.Components) {
                stamp++;
                var ct = new ComponentTopology() { Triangles = comp.Indices.Length };
                int boundaryEdges = 0;
                boundaryVerts.Clear();
                foreach (int tid in comp.Indices) {
                    Index3i tv = mesh.GetTriangle(tid);
                    for (int j = 0; j < 3; ++j) {
                        if (vstamp[tv[j]] != stamp) {
                            vstamp[tv[j]] = stamp;
                            parent[tv[j]] = tv[j];
                            ct.Vertices++;
                        }
                    }
                }
                foreach (int tid in comp.Indices) {
                    Index3i te = mesh.GetTriEdges(tid);
                    for (int j = 0; j < 3; ++j) {
                        int eid = te[j];
                        if (estamp[eid] == stamp) continue;
                        estamp[eid] = stamp;
                        ct.Edges++;
                        if (mesh.IsBoundaryEdge(eid)) {
                            boundaryEdges++;
                            Index2i ev = mesh.GetEdgeV(eid);
                            union(parent, ev.a, ev.b);
                            boundaryVerts.Add(ev.a); boundaryVerts.Add(ev.b);
                        }
                    }
                }
                roots.Clear();
                foreach (int v in boundaryVerts)
                    roots.Add(find(parent, v));
                ct.BoundaryLoops = roots.Count;
                ct.EulerCharacteristic = ct.Vertices - ct.Edges + ct.Triangles;
                ct.Genus = 0.5 * (2 - ct.EulerCharacteristic - ct.BoundaryLoops);
                list.Add(ct);
                r.Vertices += ct.Vertices; r.Edges += ct.Edges; r.Triangles += ct.Triangles;
                r.BoundaryEdges += boundaryEdges; r.BoundaryLoops += ct.BoundaryLoops;
                r.EulerCharacteristic += ct.EulerCharacteristic;
                r.Genus += ct.Genus;
                if (boundaryEdges == 0) r.ClosedComponents++;
            }
            r.Components = list.Count;
            // stable: ties keep the order found
            int[] idx = new int[list.Count];
            for (int i = 0; i < idx.Length; ++i) idx[i] = i;
            Array.Sort(idx, (x, y) => list[x].Triangles != list[y].Triangles ? list[y].Triangles.CompareTo(list[x].Triangles) : x.CompareTo(y));
            foreach (int i in idx) r.ComponentList.Add(list[i]);
            foreach (int vid in mesh.VertexIndices())
                if (mesh.IsBowtieVertex(vid)) r.BowtieVertices++;
            return r;
        }

        static int find(int[] parent, int v)
        {
            while (parent[v] != v) {
                parent[v] = parent[parent[v]];      // path halving
                v = parent[v];
            }
            return v;
        }

        static void union(int[] parent, int a, int b)
        {
            int ra = find(parent, a), rb = find(parent, b);
            if (ra != rb) {
                if (ra < rb) parent[rb] = ra; else parent[ra] = rb;
            }
        }
    }
}
