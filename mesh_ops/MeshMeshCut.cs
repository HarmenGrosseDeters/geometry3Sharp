// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace g3
{
    /// <summary>
    /// Cuts the curves where CutMesh intersects Target into Target, in place (floating-point): Compute()
    /// splits the triangles of Target so that the intersection curves run along its edges. CutMesh is not
    /// modified. Afterwards RemoveContained() deletes the triangles of Target that are inside CutMesh (winding
    /// number above 0.9 at the centroid), which leaves Target open along the cut.
    ///
    /// Intersection points within VertexSnapTol are merged with existing vertices. Not robust: segments that
    /// cannot be inserted are counted in FailedSegmentInsertions (the cut then has gaps), and only
    /// intersection segments are used, so coplanar overlaps (intersection polygons) are not handled. Used by <see cref="MeshBoolean"/>; for robust booleans use <see cref="ExactMeshBoolean"/>.
    /// VertexSnapTol is an absolute 1e-5: scale it to the model (millimetre models and UTM coordinates fail with the
    /// default, docs/ROBUSTNESS_SWEEP.md R6).
    ///
    /// Contact is out of scope: a cut that is not transversal is refused. With RefuseContact (the default), Compute()
    /// first looks, with exact predicates, for a pair of triangles (one of Target, one of CutMesh) whose closed
    /// triangles intersect although at least one of their six vertex-against-plane orientations is zero (a vertex on the
    /// other triangle's plane: a vertex on a face, an edge on a face, two edges that touch, coplanar triangles overlapping
    /// or touching, a vertex on the plane with a proper crossing). That is conservative: a pair that is also
    /// crossed properly is refused too. If there is one, Compute() leaves Target unchanged, sets Status to
    /// ContactDetected and returns (do not call RemoveContained() then). Only pairs whose six orientations are all
    /// nonzero (a transversal cut) are cut. The decision is made by exact predicates, so it is the same under every
    /// scaling by a power of two (and any similarity that is exact). The cut itself is not scale invariant (VertexSnapTol
    /// is absolute). <see cref="MeshBoolean"/> sets RefuseContact to false and so keeps its behaviour on touching inputs.
    ///
    /// TODO:
    ///    - track descendant triangles of each input face
    ///    - for missing segments, can resolve in 2D in plane of face
    /// </summary>
    public class MeshMeshCut
    {
        /// <summary> outcome of <see cref="MeshMeshCut.Compute"/> </summary>
        public enum CutStatus
        {
            /// <summary> Compute() ran (the cut may still have gaps, see FailedSegmentInsertions) </summary>
            Ok = 0,
            /// <summary> CutMesh touches Target (see the class remarks); Target was not changed </summary>
            ContactDetected = 1
        }

        /// <summary> outcome of the last Compute() </summary>
        public CutStatus Status { get; private set; } = CutStatus.Ok;

        /// <summary>
        /// If true (the default), Compute() refuses a CutMesh that touches Target (Status ContactDetected, Target unchanged).
        /// If false, Compute() runs on touching input as before (not robust: the cut can have gaps or throw).
        /// </summary>
        public bool RefuseContact = true;

        /// <summary> mesh that is cut (modified in place) </summary>
        public DMesh3 Target;
        /// <summary> mesh whose intersection with Target is cut into Target (not modified) </summary>
        public DMesh3 CutMesh;

        PointHashGrid3d<int> PointHash;

        // points within this tolerance are merged
        public double VertexSnapTol = 0.00001;

        // List of vertices in output Target that are on the
        // cut path, after calling RemoveContained. 
        // TODO: still missing some vertices??
        public List<int> CutVertices;

        /// <summary>
        /// Number of intersection segments that could not be inserted into Target by Compute().
        /// If this is not zero, the cut path has gaps.
        /// </summary>
        public int FailedSegmentInsertions = 0;


        public void Compute()
        {
            FailedSegmentInsertions = 0;
            Status = CutStatus.Ok;
            initialize();
            if (RefuseContact && has_contact()) {
                Status = CutStatus.ContactDetected;
                return;
            }
            double cellSize = Target.CachedBounds.MaxDim / 64;
            PointHash = new PointHashGrid3d<int>(cellSize, -1);

            // insert target vertices into hash
            foreach ( int vid in Target.VertexIndices()) {
                Vector3d v = Target.GetVertex(vid);
                // [TODO] duplicate (coincident) target vertices are not handled specially
                PointHash.InsertPointUnsafe(vid, v);
            }

            find_segments();
            insert_face_vertices();
            insert_edge_vertices();
            connect_edges();

            // SegmentInsertVertices was constructed by planar polygon
            // insertions in MeshInsertUVPolyCurve calls, but we also
            // need to the segment vertices
            foreach (SegmentVtx sv in SegVertices)
                SegmentInsertVertices.Add(sv.vtx_id);
        }


        public void RemoveContained()
        {
            DMeshAABBTree3 spatial = new DMeshAABBTree3(CutMesh, true);
            spatial.WindingNumber(Vector3d.Zero);
            SafeListBuilder<int> removeT = new SafeListBuilder<int>();
            gParallel.ForEach(Target.TriangleIndices(), (tid) => {
                Vector3d v = Target.GetTriCentroid(tid);
                if (spatial.WindingNumber(v) > 0.9)
                    removeT.SafeAdd(tid);
            });
            // (in id order, not the thread order of the parallel loop: the removal order sets the free lists and so the
            // ids of later insertions)
            List<int> removeSorted = new List<int>(removeT.Result);
            removeSorted.Sort();
            MeshEditor.RemoveTriangles(Target, removeSorted);

            // [RMS] construct set of on-cut vertices? This is not
            // necessarily all boundary vertices...
            CutVertices = new List<int>();
            foreach (int vid in SegmentInsertVertices) {
                if (Target.IsVertex(vid))
                    CutVertices.Add(vid);
            }
        }

        public void AppendSegments(double r)
        {
            foreach ( var seg in Segments ) {
                Segment3d s = new Segment3d(seg.v0.v, seg.v1.v);
                if ( Target.FindEdge(seg.v0.vtx_id, seg.v1.vtx_id) == DMesh3.InvalidID )
                    MeshEditor.AppendLine(Target, s, (float)r);
            }
        }

        public void ColorFaces()
        {
            int counter = 1;
            Dictionary<int, int> gidmap = new Dictionary<int, int>();
            foreach (var key in SubFaces.Keys)
                gidmap[key] = counter++;
            Target.EnableTriangleGroups(0);
            foreach ( int tid in Target.TriangleIndices() ) {
                if (ParentFaces.ContainsKey(tid))
                    Target.SetTriangleGroup(tid, gidmap[ParentFaces[tid]]);
                else if (SubFaces.ContainsKey(tid))
                    Target.SetTriangleGroup(tid, gidmap[tid]);
            }
        }


        class SegmentVtx
        {
            public Vector3d v;
            public int type = -1;
            public int initial_type = -1;
            public int vtx_id = DMesh3.InvalidID;
            public int elem_id = DMesh3.InvalidID;
        }
        List<SegmentVtx> SegVertices;
        Dictionary<int, SegmentVtx> VIDToSegVtxMap;


        // segment vertices in each triangle that we still have to insert
        Dictionary<int, List<SegmentVtx>> FaceVertices;

        // segment vertices in each edge that we still have to insert
        Dictionary<int, List<SegmentVtx>> EdgeVertices;


        class IntersectSegment
        {
            public int base_tid;
            public SegmentVtx v0;
            public SegmentVtx v1;
            public SegmentVtx this[int key] {
                get { return (key == 0) ? v0 : v1; }
                set { if (key == 0) v0 = value; else v1 = value; }
            }
        }
        IntersectSegment[] Segments;

        Vector3d[] BaseFaceCentroids;
        Vector3d[] BaseFaceNormals;
        Dictionary<int, HashSet<int>> SubFaces;
        Dictionary<int, int> ParentFaces;

        HashSet<int> SegmentInsertVertices;

        /// <summary> true if a triangle of Target and one of CutMesh touch (see the class remarks), by exact predicates </summary>
        bool has_contact()
        {
            if (Target.TriangleCount == 0 || CutMesh.TriangleCount == 0)
                return false;
            DMeshAABBTree3 cutTree = new DMeshAABBTree3(CutMesh, true);
            bool found = false;
            foreach (int tid in Target.TriangleIndices()) {
                Triangle3d a = new Triangle3d();
                Target.GetTriVertices(tid, ref a.V0, ref a.V1, ref a.V2);
                AxisAlignedBox3d box = new AxisAlignedBox3d(a.V0); box.Contain(a.V1); box.Contain(a.V2);
                // the candidate boxes are padded by a relative amount (the tree's boxes are float boxes; the exact test decides)
                double mag = Math.Max(Math.Max(Math.Abs(box.Min.x), Math.Abs(box.Max.x)), Math.Max(Math.Max(Math.Abs(box.Min.y), Math.Abs(box.Max.y)),
                    Math.Max(Math.Abs(box.Min.z), Math.Abs(box.Max.z))));
                double pad = 1e-5 * Math.Max(mag, box.MaxDim);
                box = new AxisAlignedBox3d(box.Min - pad * Vector3d.One, box.Max + pad * Vector3d.One);
                var traversal = new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (bb, depth) => !found && bb.Intersects(box),
                    NextTriangleF = (uid) => {
                        if (found) return;
                        Triangle3d b = new Triangle3d();
                        CutMesh.GetTriVertices(uid, ref b.V0, ref b.V1, ref b.V2);
                        if (ContactTest.TrianglesTouch(a, b))
                            found = true;
                    }
                };
                cutTree.DoTraversal(traversal);
                if (found)
                    return true;
            }
            return false;
        }

        void initialize()
        {
            BaseFaceCentroids = new Vector3d[Target.MaxTriangleID];
            BaseFaceNormals = new Vector3d[Target.MaxTriangleID];
            double area = 0;
            foreach (int tid in Target.TriangleIndices())
                Target.GetTriInfo(tid, out BaseFaceNormals[tid], out area, out BaseFaceCentroids[tid]);

            // allocate internals
            SegVertices = new List<SegmentVtx>();
            EdgeVertices = new Dictionary<int, List<SegmentVtx>>();
            FaceVertices = new Dictionary<int, List<SegmentVtx>>();
            SubFaces = new Dictionary<int, HashSet<int>>();
            ParentFaces = new Dictionary<int, int>();
            SegmentInsertVertices = new HashSet<int>();
            VIDToSegVtxMap = new Dictionary<int, SegmentVtx>();
        }



        /// <summary>
        /// 1) Find intersection segments
        /// 2) sort onto existing input mesh vtx/edge/face
        /// </summary>
        void find_segments()
        {
            Dictionary<Vector3d, SegmentVtx> SegVtxMap = new Dictionary<Vector3d, SegmentVtx>();

            // find intersection segments
            // TODO: intersection polygons
            // TODO: do we need to care about intersection vertices?
            DMeshAABBTree3 targetSpatial = new DMeshAABBTree3(Target, true);
            DMeshAABBTree3 cutSpatial = new DMeshAABBTree3(CutMesh, true);
            var intersections = targetSpatial.FindAllIntersections(cutSpatial);

            // for each segment, for each vtx, determine if it is 
            // at an existing vertex, on-edge, or in-face
            Segments = new IntersectSegment[intersections.Segments.Count];
            for ( int i = 0; i < Segments.Length; ++i ) {
                var isect = intersections.Segments[i];
                Vector3dTuple2 points = new Vector3dTuple2(isect.point0, isect.point1);
                IntersectSegment iseg = new IntersectSegment() {
                    base_tid = isect.t0
                };
                Segments[i] = iseg;
                for (int j = 0; j < 2; ++j) {
                    Vector3d v = points[j];

                    // if this exact vtx coord has been seen, use same vtx
                    SegmentVtx sv;
                    if (SegVtxMap.TryGetValue(v, out sv)) {
                        iseg[j] = sv;
                        continue;
                    }
                    sv = new SegmentVtx() { v = v };
                    SegVertices.Add(sv);
                    SegVtxMap[v] = sv;
                    iseg[j] = sv;

                    // this vtx is tol-equal to input mesh vtx
                    int existing_v = find_existing_vertex(v);
                    if (existing_v >= 0) {
                        sv.initial_type = sv.type = 0;
                        sv.elem_id = existing_v;
                        sv.vtx_id = existing_v;
                        VIDToSegVtxMap[sv.vtx_id] = sv;
                        continue;
                    }

                    Triangle3d tri = new Triangle3d();
                    Target.GetTriVertices(isect.t0, ref tri.V0, ref tri.V1, ref tri.V2);
                    Index3i tv = Target.GetTriangle(isect.t0);

                    // this vtx is tol-on input mesh edge
                    int on_edge_i = on_edge(ref tri, ref v);
                    if ( on_edge_i >= 0 ) {
                        sv.initial_type = sv.type = 1;
                        sv.elem_id = Target.FindEdge(tv[on_edge_i], tv[(on_edge_i+1)%3]);
                        Util.gDevAssert(sv.elem_id != DMesh3.InvalidID);
                        add_edge_vtx(sv.elem_id, sv);
                        continue;
                    }

                    // otherwise contained in input mesh face
                    sv.initial_type = sv.type = 2;
                    sv.elem_id = isect.t0;
                    add_face_vtx(sv.elem_id, sv);
                }

            }

        }




        /// <summary>
        /// For each on-face vtx, we poke the face, and re-sort 
        /// the remaining vertices on that face onto new faces/edges
        /// </summary>
        void insert_face_vertices()
        {
            while ( FaceVertices.Count > 0 ) {
                var pair = FaceVertices.First();
                int tid = pair.Key;
                List<SegmentVtx> triVerts = pair.Value;
                SegmentVtx v = triVerts[triVerts.Count-1];
                triVerts.RemoveAt(triVerts.Count-1);

                DMesh3.PokeTriangleInfo pokeInfo;
                MeshResult result = Target.PokeTriangle(tid, out pokeInfo);
                if (result != MeshResult.Ok)
                    throw new Exception("MeshMeshCut.insert_face_vertices: poke triangle failed - " + result.ToString());
                int new_v = pokeInfo.new_vid;

                Target.SetVertex(new_v, v.v);
                v.vtx_id = new_v;
                VIDToSegVtxMap[v.vtx_id] = v;
                PointHash.InsertPoint(v.vtx_id, v.v);

                // remove this triangles vtx list because it is no longer valid
                FaceVertices.Remove(tid);

                // update remaining verts
                Index3i pokeEdges = pokeInfo.new_edges;
                Index3i pokeTris = new Index3i(tid, pokeInfo.new_t1, pokeInfo.new_t2);
                foreach ( SegmentVtx sv in triVerts ) {
                    update_from_poke(sv, pokeEdges, pokeTris);
                    if (sv.type == 1)
                        add_edge_vtx(sv.elem_id, sv);
                    else if (sv.type == 2)
                        add_face_vtx(sv.elem_id, sv);
                }

                // track poke subfaces
                add_poke_subfaces(tid, ref pokeInfo);
            }
        }



        /// <summary>
        /// figure out which vtx/edge/face the input vtx is on
        /// </summary>
        void update_from_poke(SegmentVtx sv, Index3i pokeEdges, Index3i pokeTris)
        {
            // check if within tolerance of existing vtx, because we did not 
            // sort that out before...
            int existing_v = find_existing_vertex(sv.v);
            if (existing_v >= 0) {
                sv.type = 0;
                sv.elem_id = existing_v;
                sv.vtx_id = existing_v;
                VIDToSegVtxMap[sv.vtx_id] = sv;
                return;
            }

            for ( int j = 0; j < 3; ++j ) {
                if ( is_on_edge(pokeEdges[j], sv.v) ) {
                    sv.type = 1;
                    sv.elem_id = pokeEdges[j];
                    return;
                }
            }

            // [TODO] should use PrimalQuery2d for this!
            for ( int j = 0; j < 3; ++j ) {
                if ( is_in_triangle(pokeTris[j], sv.v) ) {
                    sv.type = 2;
                    sv.elem_id = pokeTris[j];
                    return;
                }
            }

            // could not classify the vertex (numerical problem), keep it on the first poke triangle
            sv.elem_id = pokeTris.a;
        }




        /// <summary>
        /// for each on-edge vtx, we split the edge and then
        /// re-sort any of the vertices on that edge onto new edges
        /// </summary>
        void insert_edge_vertices()
        {
            while (EdgeVertices.Count > 0) {
                var pair = EdgeVertices.First();
                int eid = pair.Key;
                List<SegmentVtx> edgeVerts = pair.Value;
                SegmentVtx v = edgeVerts[edgeVerts.Count - 1];
                edgeVerts.RemoveAt(edgeVerts.Count - 1);

                Index2i splitTris = Target.GetEdgeT(eid);

                DMesh3.EdgeSplitInfo splitInfo;
                MeshResult result = Target.SplitEdge(eid, out splitInfo);
                if (result != MeshResult.Ok)
                    throw new Exception("insert_edge_vertices: split failed!");
                int new_v = splitInfo.vNew;
                Index2i splitEdges = new Index2i(eid, splitInfo.eNewBN);

                Target.SetVertex(new_v, v.v);
                v.vtx_id = new_v;
                VIDToSegVtxMap[v.vtx_id] = v;
                PointHash.InsertPoint(v.vtx_id, v.v);

                // remove this triangles vtx list because it is no longer valid
                EdgeVertices.Remove(eid);

                // update remaining verts
                foreach (SegmentVtx sv in edgeVerts) {
                    update_from_split(sv, splitEdges);
                    if (sv.type == 1)
                        add_edge_vtx(sv.elem_id, sv);
                }

                // track subfaces
                add_split_subfaces(splitTris, ref splitInfo);

            }
        }



        /// <summary>
        /// figure out which vtx/edge the input vtx is on
        /// </summary>
        void update_from_split(SegmentVtx sv, Index2i splitEdges)
        {
            // check if within tolerance of existing vtx, because we did not 
            // sort that out before...
            int existing_v = find_existing_vertex(sv.v);
            if (existing_v >= 0) {
                sv.type = 0;
                sv.elem_id = existing_v;
                sv.vtx_id = existing_v;
                VIDToSegVtxMap[sv.vtx_id] = sv;
                return;
            }

            for (int j = 0; j < 2; ++j) {
                if (is_on_edge(splitEdges[j], sv.v)) {
                    sv.type = 1;
                    sv.elem_id = splitEdges[j];
                    return;
                }
            }

            throw new Exception("update_from_split: unsortable vertex?");
        }







        /// <summary>
        /// Make sure that all intersection segments are represented by
        /// a connected chain of edges.
        /// </summary>
        void connect_edges()
        {
            int NS = Segments.Length;
            for ( int si = 0; si < NS; ++si ) {
                IntersectSegment seg = Segments[si];
                if (seg.v0 == seg.v1)
                    continue;       // degenerate!
                if (seg.v0.vtx_id == seg.v1.vtx_id)
                    continue;       // also degenerate and how does this happen?

                int a = seg.v0.vtx_id, b = seg.v1.vtx_id;

                if (a == DMesh3.InvalidID || b == DMesh3.InvalidID)
                    throw new Exception("segment vertex is not defined?");
                int eid = Target.FindEdge(a, b);
                if (eid != DMesh3.InvalidID)
                    continue;       // already connected

                // TODO: in many cases there is an edge we added during a
                // poke or split that we could flip to get edge AB. 
                // this is much faster and we should do it where possible!
                // HOWEVER we need to know which edges we can and cannot flip
                // is_inserted_free_edge() should do this but not implemented yet
                // possibly also requires that we do all these flips before any
                // calls to insert_segment() !

                try {
                    insert_segment(seg);
                } catch (Exception) {
                    // the segment is missing from the cut, report it instead of failing the whole cut
                    FailedSegmentInsertions++;
                }
            }
        }


        void insert_segment(IntersectSegment seg)
        {
            List<int> subfaces = get_all_baseface_tris(seg.base_tid);

            RegionOperator op = new RegionOperator(Target, subfaces);

            Vector3d n = BaseFaceNormals[seg.base_tid];
            Vector3d c = BaseFaceCentroids[seg.base_tid];
            Vector3d e0, e1;
            Vector3d.MakePerpVectors(ref n, out e0, out e1);

            DMesh3 mesh = op.Region.SubMesh;
            MeshTransforms.PerVertexTransform(mesh, (v) => {
                v -= c;
                return new Vector3d(v.Dot(e0), v.Dot(e1), 0);
            });

            Vector3d end0 = seg.v0.v, end1 = seg.v1.v;
            end0 -= c; end1 -= c;
            Vector2d p0 = new Vector2d(end0.Dot(e0), end0.Dot(e1));
            Vector2d p1 = new Vector2d(end1.Dot(e0), end1.Dot(e1));
            PolyLine2d path = new PolyLine2d();
            path.AppendVertex(p0); path.AppendVertex(p1);

            MeshInsertUVPolyCurve insert = new MeshInsertUVPolyCurve(mesh, path);
            insert.Apply();

            MeshVertexSelection cutVerts = new MeshVertexSelection(mesh);
            cutVerts.SelectEdgeVertices(insert.OnCutEdges);

            MeshTransforms.PerVertexTransform(mesh, (v) => {
                return c + v.x * e0 + v.y * e1;
            });

            op.BackPropropagate();

            // add new cut vertices to cut list
            foreach (int vid in cutVerts)
                SegmentInsertVertices.Add(op.ReinsertSubToBaseMapV[vid]);

            add_regionop_subfaces(seg.base_tid, op);
        }





        void add_edge_vtx(int eid, SegmentVtx vtx)
        {
            List<SegmentVtx> l;
            if (EdgeVertices.TryGetValue(eid, out l)) {
                l.Add(vtx);
            } else {
                l = new List<SegmentVtx>() { vtx };
                EdgeVertices[eid] = l;
            }
        }

        void add_face_vtx(int tid, SegmentVtx vtx)
        {
            List<SegmentVtx> l;
            if (FaceVertices.TryGetValue(tid, out l)) {
                l.Add(vtx);
            } else {
                l = new List<SegmentVtx>() { vtx };
                FaceVertices[tid] = l;
            }
        }



        void add_poke_subfaces(int tid, ref DMesh3.PokeTriangleInfo pokeInfo)
        {
            int parent = get_parent(tid);
            HashSet<int> subfaces = get_subfaces(parent);
            if (tid != parent)
                add_subface(subfaces, parent, tid);
            add_subface(subfaces, parent, pokeInfo.new_t1);
            add_subface(subfaces, parent, pokeInfo.new_t2);
        }
        void add_split_subfaces(Index2i origTris, ref DMesh3.EdgeSplitInfo splitInfo)
        {
            int parent_1 = get_parent(origTris.a);
            HashSet<int> subfaces_1 = get_subfaces(parent_1);
            if (origTris.a != parent_1)
                add_subface(subfaces_1, parent_1, origTris.a);
            add_subface(subfaces_1, parent_1, splitInfo.eNewT2);

            if ( origTris.b != DMesh3.InvalidID ) {
                int parent_2 = get_parent(origTris.b);
                HashSet<int> subfaces_2 = get_subfaces(parent_2);
                if (origTris.b != parent_2)
                    add_subface(subfaces_2, parent_2, origTris.b);
                add_subface(subfaces_2, parent_2, splitInfo.eNewT3);
            }
        }
        void add_regionop_subfaces(int parent, RegionOperator op)
        {
            HashSet<int> subfaces = get_subfaces(parent);
            foreach (int tid in op.CurrentBaseTriangles) {
                if (tid != parent)
                    add_subface(subfaces, parent, tid);
            }
        }


        int get_parent(int tid)
        {
            int parent;
            if (ParentFaces.TryGetValue(tid, out parent) == false)
                parent = tid;
            return parent;
        }
        HashSet<int> get_subfaces(int parent)
        {
            HashSet<int> subfaces;
            if (SubFaces.TryGetValue(parent, out subfaces) == false) {
                subfaces = new HashSet<int>();
                SubFaces[parent] = subfaces;
            }
            return subfaces;
        }
        void add_subface(HashSet<int> subfaces, int parent, int tid)
        {
            subfaces.Add(tid);
            ParentFaces[tid] = parent;
        }
        List<int> get_all_baseface_tris(int base_tid)
        {
            List<int> faces = new List<int>(get_subfaces(base_tid));
            faces.Add(base_tid);
            return faces;
        }

        bool is_inserted_free_edge(int eid)
        {
            Index2i et = Target.GetEdgeT(eid);
            if (get_parent(et.a) != get_parent(et.b))
                return false;
            // TODO need to check if we need to save edge AB to connect vertices!
            throw new Exception("not done yet!");
            return true;
        }




        protected int on_edge(ref Triangle3d tri, ref Vector3d v)
        {
            Segment3d s01 = new Segment3d(tri.V0, tri.V1);
            if (s01.DistanceSquared(v) < VertexSnapTol * VertexSnapTol)
                return 0;
            Segment3d s12 = new Segment3d(tri.V1, tri.V2);
            if (s12.DistanceSquared(v) < VertexSnapTol * VertexSnapTol)
                return 1;
            Segment3d s20 = new Segment3d(tri.V2, tri.V0);
            if (s20.DistanceSquared(v) < VertexSnapTol * VertexSnapTol)
                return 2;
            return -1;
        }
        protected int on_edge_eid(int tid, Vector3d v)
        {
            Index3i tv = Target.GetTriangle(tid);
            Triangle3d tri = new Triangle3d();
            Target.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            int eidx = on_edge(ref tri, ref v);
            if (eidx < 0)
                return DMesh3.InvalidID;
            int eid = Target.FindEdge(tv[eidx], tv[(eidx+1)%3]);
            Util.gDevAssert(eid != DMesh3.InvalidID);
            return eid;            
        }
        protected bool is_on_edge(int eid, Vector3d v)
        {
            Index2i ev = Target.GetEdgeV(eid);
            Segment3d seg = new Segment3d(Target.GetVertex(ev.a), Target.GetVertex(ev.b));
            return seg.DistanceSquared(v) < VertexSnapTol * VertexSnapTol;
        }

        protected bool is_in_triangle(int tid, Vector3d v)
        {
            Triangle3d tri = new Triangle3d();
            Target.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            Vector3d bary = tri.BarycentricCoords(v);
            return (bary.x >= 0 && bary.y >= 0 && bary.z >= 0
                  && bary.x < 1 && bary.y <= 1 && bary.z <= 1);
                
        }



        /// <summary>
        /// find existing vertex at point, if it exists
        /// </summary>
        protected int find_existing_vertex(Vector3d pt)
        {
            return find_nearest_vertex(pt, VertexSnapTol);
        }
        /// <summary>
        /// find closest vertex, within searchRadius
        /// </summary>
        protected int find_nearest_vertex(Vector3d pt, double searchRadius, int ignore_vid = -1)
        {
            KeyValuePair<int, double> found = (ignore_vid == -1) ?
                PointHash.FindNearestInRadius(pt, searchRadius,
                            (b) => { return pt.Distance(Target.GetVertex(b)); })
                            :
                PointHash.FindNearestInRadius(pt, searchRadius,
                            (b) => { return pt.Distance(Target.GetVertex(b)); },
                            (vid) => { return vid == ignore_vid; });
            if (found.Key == PointHash.InvalidValue)
                return -1;
            return found.Key;
        }



    }

    /// <summary>
    /// Exact test for the contact of two triangles (MeshMeshCut): the six orientations of the vertices of each against the plane of the
    /// other (ExactPredicates.Orient3d); contact if one of them is zero and the closed triangles intersect. The intersection test
    /// uses that two closed triangles intersect exactly if an edge of one meets the closed other triangle.
    /// </summary>
    static class ContactTest
    {
        public static bool TrianglesTouch(Triangle3d t, Triangle3d u)
        {
            int[] s = new int[6];
            for (int i = 0; i < 3; ++i) {
                s[i] = ExactPredicates.Orient3d(t.V0, t.V1, t.V2, u[i]);
                s[3 + i] = ExactPredicates.Orient3d(u.V0, u.V1, u.V2, t[i]);
            }
            if (s[0] != 0 && s[1] != 0 && s[2] != 0 && s[3] != 0 && s[4] != 0 && s[5] != 0)
                return false;       // a transversal cut or disjoint
            // separated by a plane (all on one strict side)? then they do not meet
            if ((s[0] > 0 && s[1] > 0 && s[2] > 0) || (s[0] < 0 && s[1] < 0 && s[2] < 0))
                return false;
            if ((s[3] > 0 && s[4] > 0 && s[5] > 0) || (s[3] < 0 && s[4] < 0 && s[5] < 0))
                return false;
            for (int i = 0; i < 3; ++i)
                if (SegmentMeetsTriangle(u[i], u[(i + 1) % 3], t) || SegmentMeetsTriangle(t[i], t[(i + 1) % 3], u))
                    return true;
            return false;
        }

        // the closed segment pq meets the closed triangle abc
        static bool SegmentMeetsTriangle(Vector3d p, Vector3d q, Triangle3d tri)
        {
            int sp = ExactPredicates.Orient3d(tri.V0, tri.V1, tri.V2, p), sq = ExactPredicates.Orient3d(tri.V0, tri.V1, tri.V2, q);
            if ((sp > 0 && sq > 0) || (sp < 0 && sq < 0))
                return false;
            if (sp == 0 && sq == 0)
                return CoplanarSegmentMeetsTriangle(p, q, tri);
            // the segment meets the plane in one point x (p, q or between): x is in the closed triangle if the three edge orientations
            // of the line pq are not of both strict signs
            int e0 = ExactPredicates.Orient3d(p, q, tri.V0, tri.V1), e1 = ExactPredicates.Orient3d(p, q, tri.V1, tri.V2),
                e2 = ExactPredicates.Orient3d(p, q, tri.V2, tri.V0);
            bool pos = e0 > 0 || e1 > 0 || e2 > 0, neg = e0 < 0 || e1 < 0 || e2 < 0;
            return !(pos && neg);
        }

        // p, q and the triangle are in one plane; the plane is projected along the first axis on which the triangle has an area
        static bool CoplanarSegmentMeetsTriangle(Vector3d p, Vector3d q, Triangle3d tri)
        {
            for (int axis = 0; axis < 3; ++axis) {
                Vector2d a = Project(tri.V0, axis), b = Project(tri.V1, axis), c = Project(tri.V2, axis);
                int o = ExactPredicates.Orient2d(a, b, c);
                if (o == 0)
                    continue;
                if (o < 0) { Vector2d tmp = b; b = c; c = tmp; }
                Vector2d pp = Project(p, axis), qq = Project(q, axis);
                if (InClosedTriangle(pp, a, b, c) || InClosedTriangle(qq, a, b, c))
                    return true;
                return SegmentsMeet(pp, qq, a, b) || SegmentsMeet(pp, qq, b, c) || SegmentsMeet(pp, qq, c, a);
            }
            return true;    // a triangle without an area: refused (the conservative answer)
        }

        static Vector2d Project(Vector3d v, int axis)
        {
            return axis == 0 ? new Vector2d(v.y, v.z) : axis == 1 ? new Vector2d(v.x, v.z) : new Vector2d(v.x, v.y);
        }

        // a, b, c counter-clockwise
        static bool InClosedTriangle(Vector2d p, Vector2d a, Vector2d b, Vector2d c)
        {
            return ExactPredicates.Orient2d(a, b, p) >= 0 && ExactPredicates.Orient2d(b, c, p) >= 0 && ExactPredicates.Orient2d(c, a, p) >= 0;
        }

        // the closed segments pq and ab meet
        static bool SegmentsMeet(Vector2d p, Vector2d q, Vector2d a, Vector2d b)
        {
            int o1 = ExactPredicates.Orient2d(p, q, a), o2 = ExactPredicates.Orient2d(p, q, b);
            int o3 = ExactPredicates.Orient2d(a, b, p), o4 = ExactPredicates.Orient2d(a, b, q);
            if (o1 * o2 < 0 && o3 * o4 < 0)
                return true;
            return (o1 == 0 && OnSegment(p, q, a)) || (o2 == 0 && OnSegment(p, q, b)) || (o3 == 0 && OnSegment(a, b, p)) || (o4 == 0 && OnSegment(a, b, q));
        }

        // r is collinear with pq: r is between p and q (closed)
        static bool OnSegment(Vector2d p, Vector2d q, Vector2d r)
        {
            return Math.Min(p.x, q.x) <= r.x && r.x <= Math.Max(p.x, q.x) && Math.Min(p.y, q.y) <= r.y && r.y <= Math.Max(p.y, q.y);
        }
    }
}
