using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


namespace g3
{
    /// <summary>
    /// <see cref="Remesher"/> for a region (set of triangles) of a mesh: the region is copied into a submesh
    /// (Region.SubMesh, which the remesh passes modify), its border edges are fully constrained and their
    /// vertices pinned, so it still fits the rest of the mesh. Run BasicRemeshPass() as with Remesher, then
    /// BackPropropagate() to replace the region in BaseMesh; BaseMesh is not changed before that.
    /// QuickRemesh() does all steps in one call.
    /// </summary>
    public class RegionRemesher : Remesher
    {
        /// <summary> the mesh that contains the region (modified only by BackPropropagate()) </summary>
        public DMesh3 BaseMesh;
        /// <summary> the region as a submesh; Region.SubMesh is the mesh being remeshed </summary>
        public DSubmesh3 Region;

        /// <summary> this is only valid after BackPropropagate() call!! maps submesh verts to base mesh </summary>
        public IndexMap ReinsertSubToBaseMapV;

        /// <summary> handle a tricky problem...see comments for DuplicateTriBehavior enum </summary>
        public MeshEditor.DuplicateTriBehavior ReinsertDuplicateTriBehavior = MeshEditor.DuplicateTriBehavior.AssertContinue;

        // By default is initialized w/ all boundary constraints
        // You can add more, but don't screw up!
        MeshConstraints bdry_constraints;

        int[] cur_base_tris;

        /// <summary> remesh the triangles regionTris of mesh (the region border is fixed) </summary>
        public RegionRemesher(DMesh3 mesh, int[] regionTris)
        {
            BaseMesh = mesh;
            Region = new DSubmesh3(mesh, regionTris);
            Region.ComputeBoundaryInfo(regionTris);
            base.mesh = Region.SubMesh;

            cur_base_tris = (int[])regionTris.Clone();

            // constrain region-boundary edges
            bdry_constraints = new MeshConstraints();
            MeshConstraintUtil.FixSubmeshBoundaryEdges(bdry_constraints, Region);
            SetExternalConstraints(bdry_constraints);
        }

        /// <summary> remesh the triangles regionTris of mesh (the region border is fixed) </summary>
        public RegionRemesher(DMesh3 mesh, IEnumerable<int> regionTris)
        {
            BaseMesh = mesh;
            Region = new DSubmesh3(mesh, regionTris);
            int count = regionTris.Count();
            Region.ComputeBoundaryInfo(regionTris, count);
            base.mesh = Region.SubMesh;

            cur_base_tris = regionTris.ToArray();

            // constrain region-boundary edges
            bdry_constraints = new MeshConstraints();
            MeshConstraintUtil.FixSubmeshBoundaryEdges(bdry_constraints, Region);
            SetExternalConstraints(bdry_constraints);
        }


        /// <summary>
        /// list of sub-region triangles. This is either the input regionTris,
        /// or the submesh triangles after they are re-inserted.
        /// </summary>
        public int[] CurrentBaseTriangles
        {
            get { return cur_base_tris; }
        }


        /// <summary>
        /// After remeshing we may create an internal edge between two boundary vertices [a,b].
        /// Those vertices will be merged with vertices c and d in the base mesh. If the edge
        /// [c,d] already exists in the base mesh, then after the merge we would have at least
        /// 3 triangles at this edge. Dang.
        ///
        /// A common example is a 'fin' triangle that would duplicate a
        /// 'fin' on the border of the base mesh after removing the submesh, but this situation can
        /// arise anywhere (eg think about one-triangle-wide strips).
        ///
        /// This is very hard to remove, but we can at least avoid creating non-manifold edges (which
        /// with the current DMesh3 will be prevented, hence leaving a hole) by splitting the 
        /// internal edge in the submesh (which presumably we were remeshing anyway, so changes are ok).
        /// Called by BackPropropagate() by default.
        /// </summary>
        public void RepairPossibleNonManifoldEdges()
        {
            // [TODO] do we need to repeat this more than once? I don't think so...

            // repair submesh
            int NE = Region.SubMesh.MaxEdgeID;
            List<int> split_edges = new List<int>();
            for ( int eid = 0; eid < NE; ++eid ) {
                if (Region.SubMesh.IsEdge(eid) == false)
                    continue;
                if (Region.SubMesh.IsBoundaryEdge(eid))
                    continue;
                Index2i edgev = Region.SubMesh.GetEdgeV(eid);
                if (Region.SubMesh.IsBoundaryVertex(edgev.a) && Region.SubMesh.IsBoundaryVertex(edgev.b)) {
                    // ok, we have an internal edge where both verts are on the boundary
                    // now check if it is an edge in the base mesh
                    int base_a = Region.MapVertexToBaseMesh(edgev.a);
                    int base_b = Region.MapVertexToBaseMesh(edgev.b);
                    if (base_a != DMesh3.InvalidID && base_b != DMesh3.InvalidID) {
                        // both vertices in base mesh...right?
                        Debug.Assert(Region.BaseMesh.IsVertex(base_a) && Region.BaseMesh.IsVertex(base_b));
                        int base_eid = Region.BaseMesh.FindEdge(base_a, base_b);
                        if (base_eid != DMesh3.InvalidID)
                            split_edges.Add(eid);
                    }
                }
            }

            // split any problem edges we found and repeat this loop
            for ( int i = 0; i < split_edges.Count; ++i ) {
                DMesh3.EdgeSplitInfo split_info;
                Region.SubMesh.SplitEdge(split_edges[i], out split_info);
            }
        }


        /// <summary>
        /// set group ID for entire submesh
        /// </summary>
        public void SetSubmeshGroupID(int gid)
        {
            FaceGroupUtil.SetGroupID(Region.SubMesh, gid);
        }


        /// <summary>
        /// Remove the original submesh region from BaseMesh and merge in the remeshed version.
        /// You can call this multiple times as the base-triangle-set is updated (CurrentBaseTriangles).
        ///
        /// By default, we allow the submesh to be modified to prevent creation of
        /// non-manifold edges (RepairPossibleNonManifoldEdges). You can disable this, however then some of
        /// the submesh triangles may be discarded.
        ///
        /// Returns false if there were errors in insertion, ie if some triangles
        /// failed to insert. Does not revert changes that were successful.
        /// </summary>
        public bool BackPropropagate(bool bAllowSubmeshRepairs = true)
        {
            if (bAllowSubmeshRepairs) {
                RepairPossibleNonManifoldEdges();
            }

            // remove existing submesh triangles
            MeshEditor editor = new MeshEditor(BaseMesh);
            editor.RemoveTriangles(cur_base_tris, true);

            // insert new submesh
            int[] new_tris = new int[Region.SubMesh.TriangleCount];
            ReinsertSubToBaseMapV = null;
            bool bOK = editor.ReinsertSubmesh(Region, ref new_tris, out ReinsertSubToBaseMapV, ReinsertDuplicateTriBehavior);

            // assert that new triangles are all valid (goes wrong sometimes??)
            Debug.Assert(IndexUtil.IndicesCheck(new_tris, BaseMesh.IsTriangle));

            cur_base_tris = new_tris;
            return bOK;
        }




        /// <summary> options of QuickRemesh() </summary>
        [Flags]
        public enum QuickRemeshFlags
        {
            NoFlags = 0,
            PreventNormalFlips = 1
        }


        /// <summary>
        /// Remesh triangles tris of mesh with the given edge lengths, smoothing speed and number of
        /// BasicRemeshPass() rounds, projecting onto target if not null, then BackPropropagate() into mesh
        /// (modified in place). Returns the remesher (CurrentBaseTriangles are the new region triangles).
        /// </summary>
        public static RegionRemesher QuickRemesh(DMesh3 mesh, int[] tris, 
            double minEdgeLen, double maxEdgeLen, double smoothSpeed, 
            int rounds, 
            IProjectionTarget target,
            QuickRemeshFlags flags = QuickRemeshFlags.PreventNormalFlips )
        {
            RegionRemesher remesh = new RegionRemesher(mesh, tris);
            if ( target != null )
                remesh.SetProjectionTarget(target);
            remesh.MinEdgeLength = minEdgeLen;
            remesh.MaxEdgeLength = maxEdgeLen;
            remesh.SmoothSpeedT = smoothSpeed;
            if ((flags & QuickRemeshFlags.PreventNormalFlips) != 0)
                remesh.PreventNormalFlips = true;
            for (int k = 0; k < rounds; ++k) {
                remesh.BasicRemeshPass();
            }
            remesh.BackPropropagate();
            return remesh;
        }
        /// <summary> QuickRemesh() with SetTargetEdgeLength(targetEdgeLen) </summary>
        public static RegionRemesher QuickRemesh(DMesh3 mesh, int[] tris, 
            double targetEdgeLen, double smoothSpeed, 
            int rounds, 
            IProjectionTarget target,
            QuickRemeshFlags flags = QuickRemeshFlags.PreventNormalFlips )
        {
            RegionRemesher remesh = new RegionRemesher(mesh, tris);
            if ( target != null )
                remesh.SetProjectionTarget(target);
            remesh.SetTargetEdgeLength(targetEdgeLen);
            remesh.SmoothSpeedT = smoothSpeed;
            if ( (flags & QuickRemeshFlags.PreventNormalFlips) != 0 )
                remesh.PreventNormalFlips = true;
            for (int k = 0; k < rounds; ++k) {
                remesh.BasicRemeshPass();
            }
            remesh.BackPropropagate();
            return remesh;
        }


    }
}
