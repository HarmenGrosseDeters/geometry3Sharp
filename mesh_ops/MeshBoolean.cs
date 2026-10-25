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
    /// Experimental floating-point union of two closed meshes: each mesh is cut along its intersection with
    /// the other (<see cref="MeshMeshCut"/>), the triangles inside the other mesh (winding number above 0.9 at
    /// the triangle centroid) are removed, and the two remaining parts are appended into Result.
    ///
    /// Only union is supported (there is no operation setting). The parts are not welded: Result has open
    /// boundaries along the intersection curve, with duplicate vertices on both sides (missing cut vertices
    /// are inserted by edge splits within VertexSnapTol, so they can be merged afterwards, for example with
    /// MergeCoincidentEdges). Not robust for coplanar or touching faces. Both inputs must be closed: this is
    /// checked with Util.gDevAssert, which throws (or breaks into the debugger) otherwise. Compute() always
    /// returns true; the inputs are not modified (copies are cut).
    ///
    /// Prefer <see cref="ExactMeshBoolean"/> (exact, robust, all operations, closed result), or
    /// <see cref="gs.MeshWindingNumberBoolean"/> for a volumetric result from broken input.
    /// </summary>
    public class MeshBoolean
    {
        /// <summary> first input mesh (not modified) </summary>
        public DMesh3 Target;
        /// <summary> second input mesh (not modified) </summary>
        public DMesh3 Tool;

        /// <summary> points within this tolerance are merged </summary>
        public double VertexSnapTol = 0.00001;

        /// <summary> the union computed by Compute(): a new mesh, the two cut parts appended (not welded) </summary>
        public DMesh3 Result;

        // Each MeshMeshCut snaps its cut points to nearby vertices of its own mesh, by up to VertexSnapTol,
        // so the two copies of a cut point can be up to twice that apart
        double MatchTol { get { return 2 * VertexSnapTol; } }

        MeshMeshCut cutTargetOp;
        MeshMeshCut cutToolOp;

        DMesh3 cutTargetMesh;
        DMesh3 cutToolMesh;

        /// <summary> compute the union into Result; always returns true </summary>
        public bool Compute()
        {
            // Alternate strategy:
            //   - don't do RemoveContained
            //   - match embedded vertices, split where possible
            //   - find min-cut path through shared edges
            //   - remove contiguous patches that are inside both/etc (use MWN)
            //   ** no good for coplanar regions...


            cutTargetOp = new MeshMeshCut() {
                Target = new DMesh3(Target),
                CutMesh = Tool,
                VertexSnapTol = VertexSnapTol
            };
            cutTargetOp.Compute();
            cutTargetOp.RemoveContained();
            cutTargetMesh = cutTargetOp.Target;

            cutToolOp = new MeshMeshCut() {
                Target = new DMesh3(Tool),
                CutMesh = Target,
                VertexSnapTol = VertexSnapTol
            };
            cutToolOp.Compute();
            cutToolOp.RemoveContained();
            cutToolMesh = cutToolOp.Target;

            resolve_vtx_pairs();

            Result = cutToolMesh;
            MeshEditor.Append(Result, cutTargetMesh);

            // the two halves meet along the cut curve, where resolve_vtx_pairs() made their boundary
            // vertices coincide: weld them into one mesh. Returns false if the result is not closed,
            // i.e. the cut curves could not be matched everywhere.
            weld_seam();

            return Result.IsClosed();
        }


        void weld_seam()
        {
            merge_seam_edges();

            // Two seam vertices less than MatchTol apart can stay joined by a tiny boundary edge (a cut point
            // that was matched on one side and split into an edge on the other). The long seam edges next to
            // it then nearly coincide but share a vertex, so they cannot be merged. Collapse the tiny edges
            // and weld again.
            bool collapsed = false;
            foreach (int eid in Result.BoundaryEdgeIndices().ToList()) {
                if (Result.IsEdge(eid) == false || Result.IsBoundaryEdge(eid) == false)
                    continue;
                Index2i ev = Result.GetEdgeV(eid);
                if (Result.GetVertex(ev.a).Distance(Result.GetVertex(ev.b)) >= MatchTol)
                    continue;
                DMesh3.EdgeCollapseInfo collapseInfo;
                if (Result.CollapseEdge(ev.a, ev.b, out collapseInfo) == MeshResult.Ok)
                    collapsed = true;
            }
            if (collapsed)
                merge_seam_edges();

            // what remains of the seam can be a single missing triangle (a hole with 3 edges): add it.
            // Larger holes are left open, and Compute() returns false.
            if (Result.IsClosed() == false) {
                try {
                    MeshBoundaryLoops loops = new MeshBoundaryLoops(Result);
                    foreach (EdgeLoop loop in loops.Loops) {
                        if (loop.VertexCount == 3)
                            new SimpleHoleFiller(Result, loop).Fill();
                    }
                } catch (Exception) {
                    // boundary loops could not be extracted (e.g. bowties); leave the holes open
                }
            }
        }

        void merge_seam_edges()
        {
            gs.MergeCoincidentEdges merge = new gs.MergeCoincidentEdges(Result) { MergeDistance = MatchTol };
            merge.Apply();
        }







        void resolve_vtx_pairs()
        {
            //HashSet<int> targetVerts = new HashSet<int>(cutTargetOp.CutVertices);
            //HashSet<int> toolVerts = new HashSet<int>(cutToolOp.CutVertices);

            // tracking on-cut vertices is not working yet...
            Util.gDevAssert(Target.IsClosed() && Tool.IsClosed());

            HashSet<int> targetVerts = new HashSet<int>(MeshIterators.BoundaryVertices(cutTargetMesh));
            HashSet<int> toolVerts = new HashSet<int>(MeshIterators.BoundaryVertices(cutToolMesh));

            split_missing(cutTargetOp, cutToolOp, cutTargetMesh, cutToolMesh, targetVerts, toolVerts);
            split_missing(cutToolOp, cutTargetOp, cutToolMesh, cutTargetMesh, toolVerts, targetVerts);
        }


        void split_missing(MeshMeshCut fromOp, MeshMeshCut toOp, 
                           DMesh3 fromMesh, DMesh3 toMesh,
                           HashSet<int> fromVerts, HashSet<int> toVerts)
        {
            List<int> missing = new List<int>();
            foreach (int vid in fromVerts) {
                Vector3d v = fromMesh.GetVertex(vid);
                int near_vid = find_nearest_vertex(toMesh, v, toVerts);
                if (near_vid == DMesh3.InvalidID )
                    missing.Add(vid);
            }

            foreach (int vid in missing) {
                Vector3d v = fromMesh.GetVertex(vid);
                int near_eid = find_nearest_edge(toMesh, v, toVerts);
                if ( near_eid == DMesh3.InvalidID)
                    continue;       // no boundary edge near this vertex, it stays unmatched

                DMesh3.EdgeSplitInfo splitInfo;
                MeshResult result = toMesh.SplitEdge(near_eid, out splitInfo);
                if ( result != MeshResult.Ok )
                    continue;       // vertex stays unmatched

                toMesh.SetVertex(splitInfo.vNew, v);
                toVerts.Add(splitInfo.vNew);
            }
        }



        int find_nearest_vertex(DMesh3 mesh, Vector3d v, HashSet<int> vertices)
        {
            int near_vid = DMesh3.InvalidID;
            double nearSqr = MatchTol * MatchTol;
            foreach ( int vid in vertices ) {
                double dSqr = mesh.GetVertex(vid).DistanceSquared(ref v);
                if ( dSqr < nearSqr ) {
                    near_vid = vid;
                    nearSqr = dSqr;
                }
            }
            return near_vid;
        }

        int find_nearest_edge(DMesh3 mesh, Vector3d v, HashSet<int> vertices)
        {
            int near_eid = DMesh3.InvalidID;
            double nearSqr = MatchTol * MatchTol;
            foreach ( int eid in mesh.BoundaryEdgeIndices() ) {
                Index2i ev = mesh.GetEdgeV(eid);
                if (vertices.Contains(ev.a) == false || vertices.Contains(ev.b) == false)
                    continue;
                Segment3d seg = new Segment3d(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b));
                double dSqr = seg.DistanceSquared(v);
                if (dSqr < nearSqr) {
                    near_eid = eid;
                    nearSqr = dSqr;
                }
            }
            return near_eid;
        }

    }
}
