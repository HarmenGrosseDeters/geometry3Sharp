using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


namespace g3
{
    /// <summary>
    /// Low-level edits of a DMesh3, in place: add triangle fans and stitches, remove triangles, append meshes,
    /// separate/duplicate/reverse triangles, disconnect or remove bowtie vertices, remove unused vertices and
    /// small components. Most operations have static shortcuts (eg MeshEditor.RemoveTriangles(mesh, tris)).
    /// If the fan and stitch operations fail in-progress, they try to back out what they added and return
    /// null; removal operations cannot be backed out and return false if any triangle could not be removed.
    /// </summary>
    public class MeshEditor
    {
        /// <summary> the mesh that is edited in place </summary>
        public DMesh3 Mesh;


        public MeshEditor(DMesh3 mesh)
        {
            Mesh = mesh;
        }



        /// <summary>
        /// Add a strip of new vertices and triangles: two vertices per frame at Origin + span.a * Y and
        /// Origin + span.b * Y. Returns the new triangles, in group group_id.
        /// </summary>
        public virtual int[] AddTriangleStrip(IList<Frame3f> frames, IList<Interval1d> spans, int group_id = -1)
        {
            int N = frames.Count;
            if (N != spans.Count)
                throw new Exception("MeshEditor.AddTriangleStrip: spans list is not the same size!");
            int[] new_tris = new int[2*(N-1)];

            int prev_a = -1, prev_b = -1;
            int i = 0, ti = 0;
            for (i = 0; i < N; ++i) {
                Frame3f f = frames[i];
                Interval1d span = spans[i];

                Vector3d va = f.Origin + (float)span.a * f.Y;
                Vector3d vb = f.Origin + (float)span.b * f.Y;

                // [TODO] could compute normals here...

                int a = Mesh.AppendVertex(va);
                int b = Mesh.AppendVertex(vb);

                if ( prev_a != -1 ) {
                    new_tris[ti++] = Mesh.AppendTriangle(prev_a, b, prev_b, group_id);
                    new_tris[ti++] = Mesh.AppendTriangle(prev_a, a, b, group_id);

                }
                prev_a = a; prev_b = b;
            }

            return new_tris;
        }





        /// <summary>
        /// Add a fan of triangles (center, loop[i+1], loop[i]) around the existing vertex center. With a boundary
        /// loop in boundary orientation (as from MeshBoundaryLoops) this closes the hole consistently oriented.
        /// Returns the new triangles, or null if a triangle could not be added (the added ones are removed again).
        /// </summary>
        public virtual int[] AddTriangleFan_OrderedVertexLoop(int center, int[] vertex_loop, int group_id = -1)
        {
            int N = vertex_loop.Length;
            int[] new_tris = new int[N];

            int i = 0;
            for ( i = 0; i < N; ++i ) {
                int a = vertex_loop[i];
                int b = vertex_loop[(i + 1) % N];

                Index3i newT = new Index3i(center, b, a);
                int new_tid = Mesh.AppendTriangle(newT, group_id);
                if (new_tid < 0)
                    goto operation_failed;

                new_tris[i] = new_tid;
            }

            return new_tris;


            operation_failed:
                // remove what we added so far
                if (i > 0) {
                    if (remove_triangles(new_tris, i) == false)
                        throw new Exception("MeshEditor.AddTriangleFan_OrderedVertexLoop: failed to add fan, and also falied to back out changes.");
                }
                return null;
        }





        /// <summary>
        /// Add a fan of triangles around vertex center, one per boundary edge of edge_loop, oriented consistently
        /// with the triangle of each edge. Returns the new triangles, or null if an edge is not a boundary edge or
        /// a triangle could not be added (the added ones are removed again).
        /// </summary>
        public virtual int[] AddTriangleFan_OrderedEdgeLoop(int center, int[] edge_loop, int group_id = -1)
        {
            int N = edge_loop.Length;
            int[] new_tris = new int[N];

            int i = 0;
            for ( i = 0; i < N; ++i ) {
                if (Mesh.IsBoundaryEdge(edge_loop[i]) == false)
                    goto operation_failed;

                Index2i ev = Mesh.GetOrientedBoundaryEdgeV(edge_loop[i]);
                int a = ev.a, b = ev.b;

                Index3i newT = new Index3i(center, b, a);
                int new_tid = Mesh.AppendTriangle(newT, group_id);
                if (new_tid < 0)
                    goto operation_failed;

                new_tris[i] = new_tid;
            }

            return new_tris;


            operation_failed:
                // remove what we added so far
                if (i > 0) {
                    if (remove_triangles(new_tris, i) == false)
                        throw new Exception("MeshEditor.AddTriangleFan_OrderedEdgeLoop: failed to add fan, and also failed to back out changes.");
                }
                return null;
        }




        /// <summary>
        /// Trivial back-and-forth stitch between two vertex loops with same length. 
        /// Loops must have appropriate orientation (which is...??)
        /// [TODO] check and fail on bad orientation
        /// </summary>
        public virtual int[] StitchLoop(int[] vloop1, int[] vloop2, int group_id = -1)
        {
            int N = vloop1.Length;
            if (N != vloop2.Length)
                throw new Exception("MeshEditor.StitchLoop: loops are not the same length!!");

            int[] new_tris = new int[N * 2];

            int i = 0;
            for ( ; i < N; ++i ) {
                int a = vloop1[i];
                int b = vloop1[(i + 1) % N];
                int c = vloop2[i];
                int d = vloop2[(i + 1) % N];

                Index3i t1 = new Index3i(b, a, d);
                Index3i t2 = new Index3i(a, c, d);

                int tid1 = Mesh.AppendTriangle(t1, group_id);
                int tid2 = Mesh.AppendTriangle(t2, group_id);
                new_tris[2 * i] = tid1;
                new_tris[2 * i + 1] = tid2;

                if (tid1 < 0 || tid2 < 0)
                    goto operation_failed;
            }

            return new_tris;


            operation_failed:
                // remove what we added so far, including the one valid triangle of the failed step
                // (invalid ids are skipped)
                if (remove_triangles(new_tris, 2 * i + 2) == false)
                    throw new Exception("MeshEditor.StitchLoop: failed to add all triangles, and also failed to back out changes.");
                return null;
        }







        /// <summary>
        /// Trivial back-and-forth stitch between two vertex loops with same length. 
        /// If nearest vertices of input loops would not be matched, cycles loops so
        /// that this is the case. 
        /// Loops must have appropriate orientation.
        /// </summary>
        public virtual int[] StitchVertexLoops_NearestV(int[] loop0, int[] loop1, int group_id = -1)
        {
            int N = loop0.Length;
            Index2i iBestPair = Index2i.Zero;
            double best_dist = double.MaxValue;
            for (int i = 0; i < N; ++i) {
                Vector3d v0 = Mesh.GetVertex(loop0[i]);
                for (int j = 0; j < N; ++j) {
                    double dist_sqr = v0.DistanceSquared(Mesh.GetVertex(loop1[j]));
                    if (dist_sqr < best_dist) {
                        best_dist = dist_sqr;
                        iBestPair = new Index2i(i, j);
                    }
                }
            }
            if (iBestPair.a != iBestPair.b) {
                int[] newLoop0 = new int[N];
                int[] newLoop1 = new int[N];
                for (int i = 0; i < N; ++i) {
                    newLoop0[i] = loop0[(iBestPair.a + i) % N];
                    newLoop1[i] = loop1[(iBestPair.b + i) % N];
                }
                return StitchLoop(newLoop0, newLoop1, group_id);
            } else {
                return StitchLoop(loop0, loop1, group_id);
            }

        }






        /// <summary>
        /// Stitch two sets of boundary edges that are provided as unordered pairs of edges, by
        /// adding triangulated quads between each edge pair. 
        /// If bAbortOnFailure==true and a failure is encountered during stitching, the triangles added up to that point are removed.
        /// If bAbortOnFailure==false, failures are ignored and the returned triangle list may contain invalid values!
        /// </summary>
        public virtual int[] StitchUnorderedEdges(List<Index2i> EdgePairs, int group_id, bool bAbortOnFailure, out bool stitch_incomplete)
        {
            int N = EdgePairs.Count;
            int[] new_tris = new int[N * 2];
            for (int k = 0; k < new_tris.Length; ++k)
                new_tris[k] = DMesh3.InvalidID;
            stitch_incomplete = false;

            int i = 0;
            for (; i < N; ++i) {
                Index2i edges = EdgePairs[i];

                // look up and orient the first edge
                Index4i edge_a = Mesh.GetEdge(edges.a);
                if (edge_a.d != DMesh3.InvalidID) {
                    if (bAbortOnFailure) goto operation_failed;
                    else { stitch_incomplete = true; continue; }
                }
                Index3i edge_a_tri = Mesh.GetTriangle(edge_a.c);
                int a = edge_a.a, b = edge_a.b;
                IndexUtil.orient_tri_edge(ref a, ref b, edge_a_tri);

                // look up and orient the second edge
                Index4i edge_b = Mesh.GetEdge(edges.b);
                if (edge_b.d != DMesh3.InvalidID) {
                    if (bAbortOnFailure) goto operation_failed;
                    else { stitch_incomplete = true; continue; }
                }
                Index3i edge_b_tri = Mesh.GetTriangle(edge_b.c);
                int c = edge_b.a, d = edge_b.b;
                IndexUtil.orient_tri_edge(ref c, ref d, edge_b_tri);

                // swap second edge (right? should this be a parameter?)
                int tmp = c; c = d; d = tmp;

                Index3i t1 = new Index3i(b, a, d);
                Index3i t2 = new Index3i(a, c, d);

                int tid1 = Mesh.AppendTriangle(t1, group_id);
                int tid2 = Mesh.AppendTriangle(t2, group_id);
                new_tris[2 * i] = tid1;
                new_tris[2 * i + 1] = tid2;

                if (tid1 < 0 || tid2 < 0) {
                    if (bAbortOnFailure) goto operation_failed;
                    else { stitch_incomplete = true; continue; }
                }
            }

            return new_tris;

            operation_failed:
            // remove what we added so far (unset entries are InvalidID and skipped)
            if (remove_triangles(new_tris, new_tris.Length) == false)
                throw new Exception("MeshEditor.StitchUnorderedEdges: failed to add all triangles, and also failed to back out changes.");
            return null;
        }
        public virtual int[] StitchUnorderedEdges(List<Index2i> EdgePairs, int group_id = -1, bool bAbortOnFailure = true)
        {
            bool incomplete = false;
            return StitchUnorderedEdges(EdgePairs, group_id, bAbortOnFailure, out incomplete);
        }






        /// <summary>
        /// Trivial back-and-forth stitch between two vertex spans with same length. 
        /// vertex ordering must reslut in appropriate orientation (which is...??)
        /// [TODO] check and fail on bad orientation
        /// </summary>
        public virtual int[] StitchSpan(IList<int> vspan1, IList<int> vspan2, int group_id = -1)
        {
            int N = vspan1.Count;
            if (N != vspan2.Count)
                throw new Exception("MeshEditor.StitchSpan: spans are not the same length!!");
            N--;

            int[] new_tris = new int[N * 2];

            int i = 0;
            for ( ; i < N; ++i ) {
                int a = vspan1[i];
                int b = vspan1[i + 1];
                int c = vspan2[i];
                int d = vspan2[i + 1];

                Index3i t1 = new Index3i(b, a, d);
                Index3i t2 = new Index3i(a, c, d);

                int tid1 = Mesh.AppendTriangle(t1, group_id);
                int tid2 = Mesh.AppendTriangle(t2, group_id);
                new_tris[2 * i] = tid1;
                new_tris[2 * i + 1] = tid2;

                if (tid1 < 0 || tid2 < 0)
                    goto operation_failed;
            }

            return new_tris;


            operation_failed:
                // remove what we added so far, including the one valid triangle of the failed step
                // (invalid ids are skipped)
                if (remove_triangles(new_tris, 2 * i + 2) == false)
                    throw new Exception("MeshEditor.StitchSpan: failed to add all triangles, and also failed to back out changes.");
                return null;
        }







        // [TODO] cannot back-out this operation right now
        /// <summary>
        /// Remove list of triangles. Values of triangles[] set to InvalidID are ignored.
        /// Returns false if any removal failed (the others are still removed).
        /// </summary>
        public bool RemoveTriangles(IList<int> triangles, bool bRemoveIsolatedVerts)
        {
            bool bAllOK = true;
            for (int i = 0; i < triangles.Count; ++i ) {
                if (triangles[i] == DMesh3.InvalidID)
                    continue;

                MeshResult result = Mesh.RemoveTriangle(triangles[i], bRemoveIsolatedVerts, false);
                if (result != MeshResult.Ok)
                    bAllOK = false;
            }
            return bAllOK;
        }

        // [TODO] cannot back-out this operation right now
        /// <summary>
        /// Remove the triangles. Returns false if any id is not a triangle (including InvalidID) or a removal
        /// failed (the others are still removed).
        /// </summary>
        public bool RemoveTriangles(IEnumerable<int> triangles, bool bRemoveIsolatedVerts)
        {
            bool bAllOK = true;
            foreach ( int tid in triangles ) {
                if (! Mesh.IsTriangle(tid) ) {
                    bAllOK = false;
                    continue;
                }
                MeshResult result = Mesh.RemoveTriangle(tid, bRemoveIsolatedVerts, false);
                if (result != MeshResult.Ok)
                    bAllOK = false;
            }
            return bAllOK;
        }

        // [TODO] cannot back-out this operation right now
        /// <summary>
        /// Remove all triangles identified by selectorF returning true. Returns false if any removal failed.
        /// </summary>
        public bool RemoveTriangles(Func<int,bool> selectorF, bool bRemoveIsolatedVerts)
        {
            bool bAllOK = true;
            int NT = Mesh.MaxTriangleID;
            for ( int ti = 0; ti < NT; ++ti ) {
                if (Mesh.IsTriangle(ti) == false || selectorF(ti) == false)
                    continue;
                MeshResult result = Mesh.RemoveTriangle(ti, bRemoveIsolatedVerts, false);
                if (result != MeshResult.Ok)
                    bAllOK = false;
            }
            return bAllOK;
        }

        /// <summary> static shortcut for RemoveTriangles(IList, bool); removes unused vertices by default </summary>
        public static bool RemoveTriangles(DMesh3 Mesh, IList<int> triangles, bool bRemoveIsolatedVerts = true) {
            MeshEditor editor = new MeshEditor(Mesh);
            return editor.RemoveTriangles(triangles, bRemoveIsolatedVerts);
        }
        /// <summary> static shortcut for RemoveTriangles(IEnumerable, bool); removes unused vertices by default </summary>
        public static bool RemoveTriangles(DMesh3 Mesh, IEnumerable<int> triangles, bool bRemoveIsolatedVerts = true) {
            MeshEditor editor = new MeshEditor(Mesh);
            return editor.RemoveTriangles(triangles, bRemoveIsolatedVerts);
        }


        /// <summary>
        /// Remove 'loner' triangles that have no connected neighbours (and their vertices, if unused).
        /// Returns false if any removal failed.
        /// </summary>
        public static bool RemoveIsolatedTriangles(DMesh3 mesh)
        {
            MeshEditor editor = new MeshEditor(mesh);
            return editor.RemoveTriangles((tid) => {
                Index3i tnbrs = mesh.GetTriNeighbourTris(tid);
                return (tnbrs.a == DMesh3.InvalidID && tnbrs.b == DMesh3.InvalidID && tnbrs.c == DMesh3.InvalidID);
            }, true);
        }



        /// <summary>
        /// Remove 'fin' triangles that have only one connected triangle.
        /// Removing one fin can create another, by default will keep iterating
        /// until all fins removed (in a not very efficient way!).
        /// Pass bRepeatToConvergence=false to only do one pass.
        /// [TODO] if we are repeating, construct face selection from nbrs of first list and iterate over that on future passes!
        /// </summary>
        public static int RemoveFinTriangles(DMesh3 mesh, Func<DMesh3, int, bool> removeF = null, bool bRepeatToConvergence = true)
        {
            MeshEditor editor = new MeshEditor(mesh);

            int nRemoved = 0;
            List<int> to_remove = new List<int>();
            repeat:
            foreach ( int tid in mesh.TriangleIndices()) {
                Index3i nbrs = mesh.GetTriNeighbourTris(tid);
                int c = ((nbrs.a != DMesh3.InvalidID)?1:0) + ((nbrs.b != DMesh3.InvalidID)?1:0) + ((nbrs.c != DMesh3.InvalidID)?1:0);
                if (c <= 1) {
                    if (removeF == null || removeF(mesh, tid) == true )
                        to_remove.Add(tid);
                }
            }
            if (to_remove.Count == 0)
                return nRemoved;
            nRemoved += to_remove.Count;
            RemoveTriangles(mesh, to_remove, true);
            to_remove.Clear();
            if (bRepeatToConvergence)
                goto repeat;
            return nRemoved;
        }



        /// <summary>
        /// Disconnect the given triangles from their neighbours, by duplicating "boundary" vertices, ie
        /// vertices on edges for which one triangle is in-set and the other is not. 
        /// If bComputeEdgePairs is true, we return list of old/new edge pairs (useful for stitching)
        /// [TODO] currently boundary-edge behaviour is to *not* duplicate boundary verts
        /// </summary>
        public bool SeparateTriangles(IEnumerable<int> triangles, bool bComputeEdgePairs, out List<Index2i> EdgePairs)
        {
            HashSet<int> in_set = new HashSet<int>(triangles);
            Dictionary<int, int> VertexMap = new Dictionary<int, int>();
            EdgePairs = null;
            HashSet<int> edges = null;
            List<Index2i> OldEdgeVerts = null;
            if (bComputeEdgePairs) {
                EdgePairs = new List<Index2i>();
                edges = new HashSet<int>();
                OldEdgeVerts = new List<Index2i>();
            }

            // duplicate vertices on edges that are on boundary of triangles roi
            foreach ( int tid in triangles ) {
                Index3i te = Mesh.GetTriEdges(tid);

                for ( int j = 0; j < 3; ++j ) {
                    Index2i et = Mesh.GetEdgeT(te[j]);
                    // [TODO] what about behavior where we want to also duplicate boundary verts??
                    if (et.b == DMesh3.InvalidID ||  (et.a == tid && in_set.Contains(et.b)) || (et.b == tid && in_set.Contains(et.a)))
                        te[j] = -1;
                }

                for ( int j = 0; j < 3; ++j ) {
                    if (te[j] == -1)
                        continue;
                    Index2i ev = Mesh.GetEdgeV(te[j]);
                    if (VertexMap.ContainsKey(ev.a) == false)
                        VertexMap[ev.a] = Mesh.AppendVertex(Mesh, ev.a);
                    if (VertexMap.ContainsKey(ev.b) == false)
                        VertexMap[ev.b] = Mesh.AppendVertex(Mesh, ev.b);

                    if (bComputeEdgePairs && edges.Contains(te[j]) == false) {
                        edges.Add(te[j]);
                        OldEdgeVerts.Add(ev);
                        EdgePairs.Add(new Index2i(te[j], -1));
                    }
                }
            }

            // update triangles
            foreach ( int tid in triangles ) {
                Index3i tv = Mesh.GetTriangle(tid);
                Index3i tv_new = tv;
                for ( int j = 0; j < 3; ++j ) {
                    int newv;
                    if (VertexMap.TryGetValue(tv[j], out newv)) 
                        tv_new[j] = newv;
                }
                if ( tv_new != tv ) {
                    Mesh.SetTriangle(tid, tv_new);
                }
            }

            if ( bComputeEdgePairs ) {
                for ( int k = 0; k < EdgePairs.Count; ++k ) {
                    Index2i old_ev = OldEdgeVerts[k];
                    int new_a = VertexMap[old_ev.a];
                    int new_b = VertexMap[old_ev.b];
                    int new_eid = Mesh.FindEdge(new_a, new_b);
                    Util.gDevAssert(new_eid != DMesh3.InvalidID);
                    EdgePairs[k] = new Index2i(EdgePairs[k].a, new_eid);
                }
            }

            return true;
        }



        /// <summary>
        /// Make a copy of provided triangles, with new vertices. You provide MapV because
        /// you know if you are doing a small subset or a full-mesh-copy.
        /// </summary>
        public List<int> DuplicateTriangles(IEnumerable<int> triangles, ref IndexMap MapV, int group_id = -1)
        {
            List<int> new_triangles = new List<int>();
            foreach ( int tid in triangles ) {
                Index3i tri = Mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j) {
                    int vid = tri[j];
                    if (MapV.Contains(vid) == false) {
                        int new_vid = Mesh.AppendVertex(Mesh, vid);
                        MapV[vid] = new_vid;
                        tri[j] = new_vid;
                    } else {
                        tri[j] = MapV[vid];
                    }
                }
                int new_tid = Mesh.AppendTriangle(tri, group_id);
                new_triangles.Add(new_tid);
            }
            return new_triangles;
        }



        /// <summary>
        /// Reverse face orientation on a subset of triangles
        /// </summary>
        public void ReverseTriangles(IEnumerable<int> triangles, bool bFlipVtxNormals = true)
        {
            if ( bFlipVtxNormals == false ) { 
                foreach (int tid in triangles) {
                    Mesh.ReverseTriOrientation(tid);
                }

            } else {
                BitArray donev = new BitArray(Mesh.MaxVertexID);

                foreach (int tid in triangles) {
                    Mesh.ReverseTriOrientation(tid);

                    Index3i tri = Mesh.GetTriangle(tid);
                    for (int j = 0; j < 3; ++j) {
                        int vid = tri[j];
                        if (donev[vid] == false) {
                            Mesh.SetVertexNormal(vid, -Mesh.GetVertexNormal(vid));
                            donev[vid] = true;
                        }
                    }
                }
            }

        }



        /// <summary>
        /// separate triangle one-ring at vertex into connected components, and
        /// then duplicate vertex once for each component
        /// </summary>
        public void DisconnectBowtie(int vid)
        {
            // grouping triangles in a single pass (add to first set containing a neighbour)
            //   depends on iteration order and can split one fan into several sets, which cuts a
            //   crack into the mesh. So union the two triangles of each edge at vid instead.
            Dictionary<int, int> parent = new Dictionary<int, int>();
            foreach (int tid in Mesh.VtxTrianglesItr(vid))
                parent[tid] = tid;
            foreach (int eid in Mesh.VtxEdgesItr(vid)) {
                Index2i et = Mesh.GetEdgeT(eid);
                if (et.b != DMesh3.InvalidID)
                    parent[find_root(parent, et.a)] = find_root(parent, et.b);
            }
            Dictionary<int, List<int>> setMap = new Dictionary<int, List<int>>();
            foreach (int tid in Mesh.VtxTrianglesItr(vid)) {
                int root = find_root(parent, tid);
                List<int> set;
                if (setMap.TryGetValue(root, out set) == false) {
                    set = new List<int>();
                    setMap[root] = set;
                }
                set.Add(tid);
            }
            List<List<int>> sets = new List<List<int>>(setMap.Values);
            if (sets.Count == 1)
                return;  // not a bowtie!
            sets.Sort(bowtie_sorter);
            for ( int k = 1; k < sets.Count; ++k ) {
                int copy_vid = Mesh.AppendVertex(Mesh, vid);
                List<int> tris = sets[k];
                foreach ( int tid in tris ) {
                    Index3i t = Mesh.GetTriangle(tid);
                    if (t.a == vid) t.a = copy_vid;
                    else if (t.b == vid) t.b = copy_vid;
                    else t.c = copy_vid;
                    Mesh.SetTriangle(tid, t, false);
                }
            }
        }
        /// <summary>
        /// Cut the mesh open along the given interior edges. At every endpoint the triangle fan is split where it
        /// crosses a cut edge, and each piece after the first (largest) gets its own copy of the vertex; a cut edge
        /// becomes two boundary edges where this separates its two triangles. It does not where the fan of
        /// neither end is split: eg an edge whose ends are both inside the mesh with no other cut edge at them
        /// (a slit needs a hole or a boundary to open into), or a fan with a single cut edge. Fans at the endpoints
        /// that were already separate (bowties) are split as well. Returns the number of vertices added.
        /// </summary>
        public int CutAlongEdges(IEnumerable<int> edges)
        {
            HashSet<int> cut = new HashSet<int>();
            HashSet<int> verts = new HashSet<int>();
            foreach (int eid in edges) {
                if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid))
                    continue;
                cut.Add(eid);
                Index2i ev = Mesh.GetEdgeV(eid);
                verts.Add(ev.a);
                verts.Add(ev.b);
            }
            if (cut.Count == 0)
                return 0;
            // the cut edges are identified by their vertices: splitting one endpoint replaces the edge ids
            HashSet<Index2i> cutV = new HashSet<Index2i>();
            foreach (int eid in cut) {
                Index2i ev = Mesh.GetEdgeV(eid);
                cutV.Add(new Index2i(Math.Min(ev.a, ev.b), Math.Max(ev.a, ev.b)));
            }
            int added = 0;
            foreach (int vid in verts) {
                Dictionary<int, int> parent = new Dictionary<int, int>();
                foreach (int tid in Mesh.VtxTrianglesItr(vid))
                    parent[tid] = tid;
                foreach (int eid in Mesh.VtxEdgesItr(vid)) {
                    Index2i et = Mesh.GetEdgeT(eid);
                    if (et.b == DMesh3.InvalidID)
                        continue;
                    Index2i ev = Mesh.GetEdgeV(eid);
                    if (cutV.Contains(new Index2i(Math.Min(ev.a, ev.b), Math.Max(ev.a, ev.b))))
                        continue;
                    parent[find_root(parent, et.a)] = find_root(parent, et.b);
                }
                Dictionary<int, List<int>> setMap = new Dictionary<int, List<int>>();
                foreach (int tid in Mesh.VtxTrianglesItr(vid)) {
                    int root = find_root(parent, tid);
                    List<int> set;
                    if (setMap.TryGetValue(root, out set) == false)
                        setMap[root] = set = new List<int>();
                    set.Add(tid);
                }
                if (setMap.Count < 2)
                    continue;
                List<List<int>> sets = new List<List<int>>(setMap.Values);
                sets.Sort(bowtie_sorter);
                for (int k = 1; k < sets.Count; ++k) {
                    int copy_vid = Mesh.AppendVertex(Mesh, vid);
                    added++;
                    foreach (int tid in sets[k]) {
                        Index3i t = Mesh.GetTriangle(tid);
                        if (t.a == vid) t.a = copy_vid;
                        else if (t.b == vid) t.b = copy_vid;
                        else t.c = copy_vid;
                        Mesh.SetTriangle(tid, t, false);
                    }
                    // (the cut edges at vid now have one triangle each, at vid and at the copy: boundary edges,
                    // which the fans of their other ends do not join over)
                }
            }
            return added;
        }


        static int find_root(Dictionary<int, int> parent, int tid) {
            int root = tid;
            while (parent[root] != root)
                root = parent[root];
            while (parent[tid] != root) {
                int next = parent[tid];
                parent[tid] = root;
                tid = next;
            }
            return root;
        }
        static int bowtie_sorter(List<int> l1, List<int> l2) {
            if (l1.Count == l2.Count) return 0;
            return (l1.Count > l2.Count) ? -1 : 1;
        }



        /// <summary>
        /// Disconnect all bowtie vertices in mesh. Iterates in case disconnecting
		/// a bowtie creates new bowties.
		/// Returns number of remaining bowties after iterations.
        /// </summary>
        public int DisconnectAllBowties(int nMaxIters = 10)
        {
            List<int> bowties = new List<int>(MeshIterators.BowtieVertices(Mesh));
            int iter = 0;
            while (bowties.Count > 0 && iter++ < nMaxIters) {
                foreach (int vid in bowties) 
                    DisconnectBowtie(vid);
                bowties = new List<int>(MeshIterators.BowtieVertices(Mesh));
            }
			return bowties.Count;
        }




        // in ReinsertSubmesh, a problem can arise where the mesh we are inserting has duplicate triangles of
        // the base mesh. This can lead to problematic behavior later. We can do various things, like delete
        // and replace that existing triangle, or just use it instead of adding a new one. Or fail, or ignore it.
        // This enum/argument controls the behavior. 
        // However, fundamentally this kind of problem should be handled upstream!! For example by not trying
        // to remesh areas that contain nonmanifold geometry...
        public enum DuplicateTriBehavior
        {
            AssertContinue,         // check will not be done in Release!
            AssertAbort, UseExisting, Replace
        }


        // Assumption here is that Submesh has been modified, but boundary loop has
        // been preserved, and that old submesh has already been removed from this mesh.
        // So, we just have to append new vertices and then rewrite triangles
        // If new_tris or new_verts is non-null, we will return this info.
        // new_tris should be set to TriangleCount (ie it is not necessarily a map)
        // For new_verts, if we used an existing bdry vtx instead, we set the value to -(existing_index+1),
        // otherwise the value is new_index (+1 is to handle 0)
        //
        // Returns true if submesh successfully inserted, false if any triangles failed
        // (which happens if triangle would result in non-manifold mesh)
        public bool ReinsertSubmesh(DSubmesh3 sub, ref int[] new_tris, out IndexMap SubToNewV,
            DuplicateTriBehavior eDuplicateBehavior = DuplicateTriBehavior.AssertAbort)
        {
            if (sub.BaseBorderV == null)
                throw new Exception("MeshEditor.ReinsertSubmesh: Submesh does not have required boundary info. Call ComputeBoundaryInfo()!");

            DMesh3 submesh = sub.SubMesh;
            bool bAllOK = true;

            IndexFlagSet done_v = new IndexFlagSet(submesh.MaxVertexID, submesh.TriangleCount/2);
            SubToNewV = new IndexMap(submesh.MaxVertexID, submesh.VertexCount);

            int nti = 0;
            int NT = submesh.MaxTriangleID;
            for (int ti = 0; ti < NT; ++ti ) {
                if (submesh.IsTriangle(ti) == false)
                    continue;

                Index3i sub_t = submesh.GetTriangle(ti);
                int gid = submesh.GetTriangleGroup(ti);

                Index3i new_t = Index3i.Zero;
                for ( int j = 0; j < 3; ++j ) {
                    int sub_v = sub_t[j];
                    int new_v = -1;
                    if (done_v[sub_v] == false) {

                        // first check if this is a boundary vtx on submesh and maps to a bdry vtx on base mesh
                        if (submesh.IsBoundaryVertex(sub_v)) {
                            int base_v = (sub_v < sub.SubToBaseV.size) ? sub.SubToBaseV[sub_v] : -1;
                            if ( base_v >= 0 && Mesh.IsVertex(base_v) && sub.BaseBorderV[base_v] == true ) { 
                                // [RMS] this should always be true, but assert in tests to find out
                                Debug.Assert(Mesh.IsBoundaryVertex(base_v));
                                if (Mesh.IsBoundaryVertex(base_v)) {
                                    new_v = base_v;
                                }
                            }
                        }

                        // if that didn't happen, append new vtx
                        if ( new_v == -1 ) {
                            new_v = Mesh.AppendVertex(submesh, sub_v);
                        }

                        SubToNewV[sub_v] = new_v;
                        done_v[sub_v] = true;

                    } else 
                        new_v = SubToNewV[sub_v];

                    new_t[j] = new_v;
                }

                // try to handle duplicate-tri case
                if (eDuplicateBehavior == DuplicateTriBehavior.AssertContinue) {
                    Debug.Assert(Mesh.FindTriangle(new_t.a, new_t.b, new_t.c) == DMesh3.InvalidID);
                } else {
                    int existing_tid = Mesh.FindTriangle(new_t.a, new_t.b, new_t.c);
                    if (existing_tid != DMesh3.InvalidID) {
                        if (eDuplicateBehavior == DuplicateTriBehavior.AssertAbort) {
                            Debug.Assert(existing_tid == DMesh3.InvalidID);
                            return false;
                        } else if (eDuplicateBehavior == DuplicateTriBehavior.UseExisting) {
                            if (new_tris != null)
                                new_tris[nti++] = existing_tid;
                            continue;
                        } else if (eDuplicateBehavior == DuplicateTriBehavior.Replace) {
                            Mesh.RemoveTriangle(existing_tid, false);
                        }
                    }
                }


                int new_tid = Mesh.AppendTriangle(new_t, gid);
                Debug.Assert(new_tid != DMesh3.InvalidID && new_tid != DMesh3.NonManifoldID);
                if ( ! Mesh.IsTriangle(new_tid) )
                    bAllOK = false;

                if (new_tris != null)
                    new_tris[nti++] = new_tid;
            }

            return bAllOK;
        }







        /// <summary>
        /// Append all vertices and triangles of appendMesh (with its vertex attributes) as a separate part, not
        /// welded. Triangles get appendGID if it is &gt;= 0, otherwise their own group. Always returns true.
        /// </summary>
        public bool AppendMesh(IMesh appendMesh, int appendGID = -1)
        {
            int[] mapV;
            return AppendMesh(appendMesh, out mapV, appendGID);
        }
        /// <summary> as AppendMesh(appendMesh, appendGID); mapV maps appendMesh vertex ids to new ids in Mesh </summary>
        public bool AppendMesh(IMesh appendMesh, out int[] mapV, int appendGID = -1)
        {
            mapV = new int[appendMesh.MaxVertexID];
            foreach (int vid in appendMesh.VertexIndices() ) {
                
                NewVertexInfo vinfo = appendMesh.GetVertexAll(vid);
                int newvid = Mesh.AppendVertex(vinfo);
                mapV[vid] = newvid;
            }

            foreach (int tid in appendMesh.TriangleIndices()) {
                Index3i t = appendMesh.GetTriangle(tid);
                t.a = mapV[t.a];
                t.b = mapV[t.b];
                t.c = mapV[t.c];
                int gid = appendMesh.GetTriangleGroup(tid);
                if (appendGID >= 0)
                    gid = appendGID;
                Mesh.AppendTriangle(t, gid);
            }

            return true;
        }
        /// <summary> new mesh containing all the given meshes (not welded), each as a new triangle group </summary>
        public static DMesh3 Combine(params IMesh[] appendMeshes )
        {
            DMesh3 m = new DMesh3();
            MeshEditor editor = new MeshEditor(m);
            foreach ( var mesh in appendMeshes ) {
                editor.AppendMesh(mesh, m.AllocateTriangleGroup());
            }
            return m;
        }
        /// <summary> append a copy of append to appendTo (not welded), as a new triangle group </summary>
        public static void Append(DMesh3 appendTo, DMesh3 append)
        {
            MeshEditor editor = new MeshEditor(appendTo);
            editor.AppendMesh(append, appendTo.AllocateTriangleGroup());
        }


        /// <summary>
        /// As AppendMesh, but vertices of appendMesh that are in mergeMapV are not appended: their triangles use
        /// the mapped existing vertex of Mesh instead (welds the appended mesh along those vertices).
        /// Triangles that cannot be added (eg non-manifold) are skipped silently. Always returns true.
        /// </summary>
        public bool AppendMesh(IMesh appendMesh, IndexMap mergeMapV, out int[] mapV, int appendGID = -1)
        {
            mapV = new int[appendMesh.MaxVertexID];
            foreach (int vid in appendMesh.VertexIndices()) {
                if (mergeMapV.Contains(vid)) {
                    mapV[vid] = mergeMapV[vid];
                } else {
                    NewVertexInfo vinfo = appendMesh.GetVertexAll(vid);
                    int newvid = Mesh.AppendVertex(vinfo);
                    mapV[vid] = newvid;
                }
            }

            foreach (int tid in appendMesh.TriangleIndices()) {
                Index3i t = appendMesh.GetTriangle(tid);
                t.a = mapV[t.a];
                t.b = mapV[t.b];
                t.c = mapV[t.c];
                int gid = appendMesh.GetTriangleGroup(tid);
                if (appendGID >= 0)
                    gid = appendGID;
                Mesh.AppendTriangle(t, gid);
            }

            return true;
        }




        public void AppendBox(Frame3f frame, float size)
        {
            AppendBox(frame, size * Vector3f.One);
        }
        public void AppendBox(Frame3f frame, Vector3f size)
        {
            AppendBox(frame, size, Colorf.White);
        }
        public void AppendBox(Frame3f frame, Vector3f size, Colorf color)
        {
            TrivialBox3Generator boxgen = new TrivialBox3Generator() {
                Box = new Box3d(frame, size),
                NoSharedVertices = false
            };
            boxgen.Generate();
            DMesh3 mesh = new DMesh3();
            boxgen.MakeMesh(mesh);
            if (Mesh.HasVertexColors)
                mesh.EnableVertexColors(color);
            AppendMesh(mesh, Mesh.AllocateTriangleGroup());
        }
        public void AppendLine(Segment3d seg, float size)
        {
            Frame3f f = new Frame3f(seg.Center);
            f.AlignAxis(2, (Vector3f)seg.Direction);
            AppendBox(f, new Vector3f(size, size, seg.Extent));
        }
        public void AppendLine(Segment3d seg, float size, Colorf color)
        {
            Frame3f f = new Frame3f(seg.Center);
            f.AlignAxis(2, (Vector3f)seg.Direction);
            AppendBox(f, new Vector3f(size, size, seg.Extent), color);
        }
        public static void AppendBox(DMesh3 mesh, Vector3d pos, float size)
        {
            MeshEditor editor = new MeshEditor(mesh);
            editor.AppendBox(new Frame3f(pos), size);
        }
        public static void AppendBox(DMesh3 mesh, Vector3d pos, float size, Colorf color)
        {
            MeshEditor editor = new MeshEditor(mesh);
            editor.AppendBox(new Frame3f(pos), size*Vector3f.One, color);
        }
        public static void AppendBox(DMesh3 mesh, Vector3d pos, Vector3d normal, float size)
        {
            MeshEditor editor = new MeshEditor(mesh);
            editor.AppendBox(new Frame3f(pos, normal), size);
        }
        public static void AppendBox(DMesh3 mesh, Vector3d pos, Vector3d normal, float size, Colorf color)
        {
            MeshEditor editor = new MeshEditor(mesh);
            editor.AppendBox(new Frame3f(pos, normal), size*Vector3f.One, color);
        }
        public static void AppendBox(DMesh3 mesh, Frame3f frame, Vector3f size, Colorf color)
        {
            MeshEditor editor = new MeshEditor(mesh);
            editor.AppendBox(frame, size, color);
        }

        public static void AppendLine(DMesh3 mesh, Segment3d seg, float size)
        {
            Frame3f f = new Frame3f(seg.Center);
            f.AlignAxis(2, (Vector3f)seg.Direction);
            MeshEditor editor = new MeshEditor(mesh);
            editor.AppendBox(f, new Vector3f(size, size, seg.Extent));
        }




        public void AppendPathSolid(IEnumerable<Vector3d> vertices, double radius, Colorf color)
        {
            TubeGenerator tubegen = new TubeGenerator() {
                Vertices = new List<Vector3d>(vertices),
                Polygon = Polygon2d.MakeCircle(radius, 6),
                NoSharedVertices = false
            };
            DMesh3 mesh = tubegen.Generate().MakeDMesh();
            if (Mesh.HasVertexColors)
                mesh.EnableVertexColors(color);
            AppendMesh(mesh, Mesh.AllocateTriangleGroup());
        }




        /// <summary>
        /// Remove all bowtie vertices in mesh. Makes one pass unless
        ///   bRepeatUntilClean = true, in which case repeats until no more bowties found
        /// Returns true if any vertices were removed
        /// </summary>
        public bool RemoveAllBowtieVertices(bool bRepeatUntilClean)
        {
            int nRemoved = 0;

            while (true) {
                List<int> bowties = new List<int>();
                foreach (int vID in Mesh.VertexIndices()) {
                    if (Mesh.IsBowtieVertex(vID))
                        bowties.Add(vID);
                }
                if (bowties.Count == 0)
                    break;

                foreach (int vID in bowties) {
                    MeshResult result = Mesh.RemoveVertex(vID, true, false);
                    Debug.Assert(result == MeshResult.Ok);
                    nRemoved++;
                }
                if (bRepeatUntilClean == false)
                    break;
            }
            return (nRemoved > 0);
        }







        /// <summary>
        /// Remove any unused vertices in mesh, ie vertices with no edges.
        /// Returns number of removed vertices.
        /// </summary>
        public int RemoveUnusedVertices()
        {
            int nRemoved = 0;
            int NV = Mesh.MaxVertexID;
            for ( int vid = 0; vid < NV; ++vid) {
                if (Mesh.IsVertex(vid) && Mesh.GetVtxEdgeCount(vid) == 0) {
                    Mesh.RemoveVertex(vid);
                    ++nRemoved;
                }
            }
            return nRemoved;
        }
        public static int RemoveUnusedVertices(DMesh3 mesh) {
            MeshEditor e = new MeshEditor(mesh); return e.RemoveUnusedVertices();
        }






        /// <summary>
        /// Remove any connected components with volume &lt; min_volume area lt; min_area
        /// </summary>
        public int RemoveSmallComponents(double min_volume, double min_area)
        {
            MeshConnectedComponents C = new MeshConnectedComponents(Mesh);
            C.FindConnectedT();
            if (C.Count == 1)
                return 0;
            int nRemoved = 0;
            foreach (var comp in C.Components) {
                Vector2d vol_area = MeshMeasurements.VolumeArea(Mesh, comp.Indices, Mesh.GetVertex);
                if (vol_area.x < min_volume || vol_area.y < min_area) {
                    MeshEditor.RemoveTriangles(Mesh, comp.Indices);
                    nRemoved++;
                }
            }
            return nRemoved;
        }
        public static int RemoveSmallComponents(DMesh3 mesh, double min_volume, double min_area) {
            MeshEditor e = new MeshEditor(mesh); return e.RemoveSmallComponents(min_volume, min_area);
        }







        // this is for backing out changes we have made...
        bool remove_triangles(int[] tri_list, int count)
        {
            for (int i = 0; i < count; ++i) {
                if (Mesh.IsTriangle(tri_list[i]) == false)
                    continue;
                MeshResult result = Mesh.RemoveTriangle(tri_list[i], false, false);
                if (result != MeshResult.Ok)
                    return false;
            }
            return true;
        }


    }
}
