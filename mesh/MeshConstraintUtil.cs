using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace g3
{
    /// <summary>
    /// Helpers that fill a <see cref="MeshConstraints"/> for common cases, for use with <see cref="Remesher"/>
    /// and <see cref="Reducer"/>. The overloads that take a Remesher create its constraints object if it has
    /// none. Without constraints the refiners move, split and collapse boundary edges freely (boundaries
    /// shrink and lose detail), so constrain them when the boundary must be kept:
    /// FixAllBoundaryEdges keeps the boundary exactly (its edges are not changed at all),
    /// PreserveBoundaryLoops lets boundary vertices slide along the original boundary curve (edges can be split
    /// and collapsed, so the boundary is remeshed too), and PreserveBoundaryAndFeatures additionally keeps sharp
    /// feature edges and pins corners.
    /// </summary>
    public static class MeshConstraintUtil
    {

        /// <summary>
        /// For the given edges, disable flip/split/collapse; pin their vertices in their current position
        /// </summary>
        public static void FixEdges(MeshConstraints cons, DMesh3 mesh, IEnumerable<int> edges)
        {
            foreach ( int ei in edges ) { 
                if (mesh.IsEdge(ei)) {
                    cons.SetOrUpdateEdgeConstraint(ei, EdgeConstraint.FullyConstrained);
                    Index2i ev = mesh.GetEdgeV(ei);
                    cons.SetOrUpdateVertexConstraint(ev.a, VertexConstraint.Pinned);
                    cons.SetOrUpdateVertexConstraint(ev.b, VertexConstraint.Pinned);
                }
            }
        }


        /// <summary>
        /// Constrain boundary loops and sharp feature edges (normals differ by more than creaseAngle degrees)
        /// for remeshing: vertices on them stay on these curves (MeshTopology spans/loops as projection
        /// targets), junctions are pinned, and so are corners along the curves, where the curve turns by more
        /// than cornerAngle degrees (default: creaseAngle). Without pinned corners, collapses and smoothing
        /// slide corner vertices along the curve and round off eg the corners of the outer boundary ring.
        /// </summary>
        public static void PreserveBoundaryAndFeatures(MeshConstraints cons, DMesh3 mesh, double creaseAngle = 30, double cornerAngle = -1)
        {
            if (cornerAngle < 0)
                cornerAngle = creaseAngle;
            gs.MeshTopology topo = new gs.MeshTopology(mesh) { CreaseAngle = creaseAngle };
            topo.AddRemeshConstraints(cons);

            double cornerDot = Math.Cos(cornerAngle * MathUtil.Deg2Rad);
            VertexConstraint pinned = VertexConstraint.Pinned;
            pinned.FixedSetID = -1;
            foreach (EdgeSpan span in topo.Spans)
                pin_curve_corners(cons, mesh, span.Vertices, false, cornerDot, pinned);
            foreach (EdgeLoop loop in topo.Loops)
                pin_curve_corners(cons, mesh, loop.Vertices, true, cornerDot, pinned);
        }
        /// <summary> PreserveBoundaryAndFeatures on the remesher's mesh and constraints (created if null) </summary>
        public static void PreserveBoundaryAndFeatures(Remesher r, double creaseAngle = 30, double cornerAngle = -1)
        {
            if (r.Constraints == null)
                r.SetExternalConstraints(new MeshConstraints());
            PreserveBoundaryAndFeatures(r.Constraints, r.Mesh, creaseAngle, cornerAngle);
        }

        static void pin_curve_corners(MeshConstraints cons, DMesh3 mesh, int[] vertices, bool closed, double cornerDot, VertexConstraint pinned)
        {
            int N = vertices.Length;
            for (int i = 0; i < N; ++i) {
                if (closed == false && (i == 0 || i == N - 1))
                    continue;       // span endpoints are junctions, already pinned
                Vector3d prev = mesh.GetVertex(vertices[(i + N - 1) % N]);
                Vector3d cur = mesh.GetVertex(vertices[i]);
                Vector3d next = mesh.GetVertex(vertices[(i + 1) % N]);
                Vector3d d0 = (cur - prev).Normalized, d1 = (next - cur).Normalized;
                if (d0.Dot(d1) < cornerDot)
                    cons.SetOrUpdateVertexConstraint(vertices[i], pinned);
            }
        }


        /// <summary>
        /// For all mesh boundary edges, disable flip/split/collapse; for all mesh boundary vertices, pin in
        /// current position. The boundary stays exactly as it is.
        /// </summary>
        public static void FixAllBoundaryEdges(MeshConstraints cons, DMesh3 mesh)
        {
            int NE = mesh.MaxEdgeID;
            for ( int ei = 0; ei < NE; ++ei ) {
                if ( mesh.IsEdge(ei) && mesh.IsBoundaryEdge(ei) ) {
                    cons.SetOrUpdateEdgeConstraint(ei, EdgeConstraint.FullyConstrained);

                    Index2i ev = mesh.GetEdgeV(ei);
                    cons.SetOrUpdateVertexConstraint(ev.a, VertexConstraint.Pinned);
                    cons.SetOrUpdateVertexConstraint(ev.b, VertexConstraint.Pinned);
                }
            }
        }
        /// <summary> FixAllBoundaryEdges on the remesher's mesh and constraints (created if null) </summary>
        public static void FixAllBoundaryEdges(Remesher r)
        {
            if (r.Constraints == null)
                r.SetExternalConstraints(new MeshConstraints());
            FixAllBoundaryEdges(r.Constraints, r.Mesh);
        }


        /// <summary>
        /// For all mesh boundary vertices, fix in current position with the given set id, and disable flips and
        /// splits of boundary edges, but allow collapses: with AllowCollapseFixedVertsWithSameSetID (default) two
        /// fixed boundary vertices with the same set id collapse to the edge midpoint, so the boundary gets coarser.
        /// </summary>
        public static void FixAllBoundaryEdges_AllowCollapse(MeshConstraints cons, DMesh3 mesh, int setID)
        {
            EdgeConstraint edgeCons = new EdgeConstraint(EdgeRefineFlags.NoFlip | EdgeRefineFlags.NoSplit);
            VertexConstraint vertCons = new VertexConstraint(true, setID);

            int NE = mesh.MaxEdgeID;
            for ( int ei = 0; ei < NE; ++ei ) {
                if ( mesh.IsEdge(ei) && mesh.IsBoundaryEdge(ei) ) {
                    cons.SetOrUpdateEdgeConstraint(ei, edgeCons);

                    Index2i ev = mesh.GetEdgeV(ei);
                    cons.SetOrUpdateVertexConstraint(ev.a, vertCons);
                    cons.SetOrUpdateVertexConstraint(ev.b, vertCons);
                }
            }
        }



        /// <summary>
        /// For all mesh boundary vertices, fix in current position with the given set id, and disable flips and
        /// collapses of boundary edges, but allow splits (the new vertex is fixed with the same set id, on the
        /// original edge). The boundary shape is kept, it only gets finer.
        /// </summary>
        public static void FixAllBoundaryEdges_AllowSplit(MeshConstraints cons, DMesh3 mesh, int setID)
        {
            EdgeConstraint edgeCons = new EdgeConstraint(EdgeRefineFlags.NoFlip | EdgeRefineFlags.NoCollapse);
            VertexConstraint vertCons = new VertexConstraint(true, setID);

            int NE = mesh.MaxEdgeID;
            for (int ei = 0; ei < NE; ++ei) {
                if (mesh.IsEdge(ei) && mesh.IsBoundaryEdge(ei)) {
                    cons.SetOrUpdateEdgeConstraint(ei, edgeCons);

                    Index2i ev = mesh.GetEdgeV(ei);
                    cons.SetOrUpdateVertexConstraint(ev.a, vertCons);
                    cons.SetOrUpdateVertexConstraint(ev.b, vertCons);
                }
            }
        }


        /// <summary>
        /// Loop through submesh border edges on basemesh, map to submesh, and fully constrain those edges and
        /// pin their vertices (requires a DSubmesh3 that computed BaseBorderE).
        /// </summary>
        public static void FixSubmeshBoundaryEdges(MeshConstraints cons, DSubmesh3 sub)
        {
            Debug.Assert(sub.BaseBorderE != null);
            foreach ( int base_eid in sub.BaseBorderE ) {
                Index2i base_ev = sub.BaseMesh.GetEdgeV(base_eid);
                Index2i sub_ev = sub.MapVerticesToSubmesh(base_ev);
                int sub_eid = sub.SubMesh.FindEdge(sub_ev.a, sub_ev.b);
                Debug.Assert(sub_eid != DMesh3.InvalidID);
                Debug.Assert(sub.SubMesh.IsBoundaryEdge(sub_eid));
            
                cons.SetOrUpdateEdgeConstraint(sub_eid, EdgeConstraint.FullyConstrained);
                cons.SetOrUpdateVertexConstraint(sub_ev.a, VertexConstraint.Pinned);
                cons.SetOrUpdateVertexConstraint(sub_ev.b, VertexConstraint.Pinned);
            }
        }




        /// <summary>
        /// For all triangle group boundary edges (edges between different groups), disable flip/split/collapse;
        /// if bPinVertices, also pin their vertices in current position
        /// </summary>
        public static void FixAllGroupBoundaryEdges(MeshConstraints cons, DMesh3 mesh, bool bPinVertices)
        {
            int NE = mesh.MaxEdgeID;
            for ( int ei = 0; ei < NE; ++ei ) {
                if ( mesh.IsEdge(ei) && mesh.IsGroupBoundaryEdge(ei) ) {
                    cons.SetOrUpdateEdgeConstraint(ei, EdgeConstraint.FullyConstrained);

                    if (bPinVertices) {
                        Index2i ev = mesh.GetEdgeV(ei);
                        cons.SetOrUpdateVertexConstraint(ev.a, VertexConstraint.Pinned);
                        cons.SetOrUpdateVertexConstraint(ev.b, VertexConstraint.Pinned);
                    }
                }
            }
        }
        /// <summary> FixAllGroupBoundaryEdges on the remesher's mesh and constraints (created if null) </summary>
        public static void FixAllGroupBoundaryEdges(Remesher r, bool bPinVertices)
        {
            if (r.Constraints == null)
                r.SetExternalConstraints(new MeshConstraints());
            FixAllGroupBoundaryEdges(r.Constraints, r.Mesh, bPinVertices);
        }



        /// <summary>
        /// For all vertices in the closed loop loopV, constrain to target (projected onto it);
        /// for all edges of the loop, disable flips and set target and tracking set id (splits and collapses stay allowed)
        /// </summary>
        public static void ConstrainVtxLoopTo(MeshConstraints cons, DMesh3 mesh, IList<int> loopV, IProjectionTarget target, int setID = -1)
        {
            VertexConstraint vc = new VertexConstraint(target);
            int N = loopV.Count;
            for (int i = 0; i < N; ++i)
                cons.SetOrUpdateVertexConstraint(loopV[i], vc);

            EdgeConstraint ec = new EdgeConstraint(EdgeRefineFlags.NoFlip, target);
            ec.TrackingSetID = setID;
            for ( int i = 0; i < N; ++i ) {
                int v0 = loopV[i];
                int v1 = loopV[(i + 1) % N];

                int eid = mesh.FindEdge(v0, v1);
                Debug.Assert(eid != DMesh3.InvalidID);
                if ( eid != DMesh3.InvalidID )
                    cons.SetOrUpdateEdgeConstraint(eid, ec);
            }

        }
        /// <summary> ConstrainVtxLoopTo on the remesher's mesh and constraints (created if null) </summary>
        public static void ConstrainVtxLoopTo(Remesher r, int[] loopV, IProjectionTarget target, int setID = -1)
        {
            if (r.Constraints == null)
                r.SetExternalConstraints(new MeshConstraints());
            ConstrainVtxLoopTo(r.Constraints, r.Mesh, loopV, target, setID);
        }





        /// <summary>
        /// For the interior vertices of the open span spanV, constrain to target; pin the two end vertices;
        /// for all edges of the span, disable flips and set target and tracking set id
        /// </summary>
        public static void ConstrainVtxSpanTo(MeshConstraints cons, DMesh3 mesh, IList<int> spanV, IProjectionTarget target, int setID = -1)
        {
            VertexConstraint vc = new VertexConstraint(target);
            int N = spanV.Count;
            for (int i = 1; i < N-1; ++i)
                cons.SetOrUpdateVertexConstraint(spanV[i], vc);
            cons.SetOrUpdateVertexConstraint(spanV[0], VertexConstraint.Pinned);
            cons.SetOrUpdateVertexConstraint(spanV[N-1], VertexConstraint.Pinned);

            EdgeConstraint ec = new EdgeConstraint(EdgeRefineFlags.NoFlip, target);
            ec.TrackingSetID = setID;
            for (int i = 0; i < N-1; ++i) {
                int v0 = spanV[i];
                int v1 = spanV[i + 1];

                int eid = mesh.FindEdge(v0, v1);
                Debug.Assert(eid != DMesh3.InvalidID);
                if (eid != DMesh3.InvalidID)
                    cons.SetOrUpdateEdgeConstraint(eid, ec);
            }

        }
        /// <summary> ConstrainVtxSpanTo on the remesher's mesh and constraints (created if null) </summary>
        public static void ConstrainVtxSpanTo(Remesher r, int[] spanV, IProjectionTarget target, int setID = -1)
        {
            if (r.Constraints == null)
                r.SetExternalConstraints(new MeshConstraints());
            ConstrainVtxSpanTo(r.Constraints, r.Mesh, spanV, target, setID);
        }






        /// <summary>
        /// Constrain each boundary loop to a curve copy of itself (ConstrainVtxLoopTo with a DCurveProjectionTarget):
        /// boundary vertices stay on the original boundary polyline, and boundary edges can be split and collapsed
        /// but not flipped. No vertices are pinned, so corners of the boundary can be rounded off; use
        /// PreserveBoundaryAndFeatures to pin corners, or FixAllBoundaryEdges to keep the boundary unchanged.
        /// </summary>
        public static void PreserveBoundaryLoops(MeshConstraints cons, DMesh3 mesh) {
            MeshBoundaryLoops loops = new MeshBoundaryLoops(mesh);
            foreach ( EdgeLoop loop in loops ) {
                DCurve3 loopC = MeshUtil.ExtractLoopV(mesh, loop.Vertices);
                DCurveProjectionTarget target = new DCurveProjectionTarget(loopC);
                ConstrainVtxLoopTo(cons, mesh, loop.Vertices, target);
            }
        }
        /// <summary> PreserveBoundaryLoops on the remesher's mesh and constraints (created if null) </summary>
        public static void PreserveBoundaryLoops(Remesher r)
        {
            if (r.Constraints == null)
                r.SetExternalConstraints(new MeshConstraints());
            PreserveBoundaryLoops(r.Constraints, r.Mesh);
        }



        /// <summary> tag the edges with a TrackingSetID (no constraint), to find their descendants with FindConstrainedEdgesBySetID </summary>
        public static void AddTrackedEdges(MeshConstraints cons, int[] edges, int setID)
        {
            EdgeConstraint ec = EdgeConstraint.Unconstrained;
            ec.TrackingSetID = setID;
            for ( int i = 0; i < edges.Length; ++i ) {
                cons.SetOrUpdateEdgeConstraint(edges[i], ec);
            }
        }


    }
}
