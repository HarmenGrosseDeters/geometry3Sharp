using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Simplest hole filler, in place: a 3-vertex hole gets one triangle, larger holes a triangle fan around a
    /// new vertex at the centroid of the loop vertices. Fast and topologically simple, but the fan can be
    /// badly shaped or self-intersecting for non-convex or non-planar holes; prefer MinimalHoleFill,
    /// SmoothedHoleFill (both start from this fill) or PlanarHoleFiller for better geometry.
    /// </summary>
    public class SimpleHoleFiller
    {
        /// <summary> the mesh to fill, modified in place </summary>
        public DMesh3 Mesh;
        /// <summary> the boundary loop to fill, oriented as from MeshBoundaryLoops (see Validate) </summary>
        public EdgeLoop Loop;

        /// <summary> the added centroid vertex, or InvalidID (3-vertex holes, or failure) </summary>
        public int NewVertex;
        /// <summary> the added triangles after a successful Fill(), otherwise null </summary>
        public int[] NewTriangles;


        public SimpleHoleFiller(DMesh3 mesh, EdgeLoop loop)
        {
            Mesh = mesh;
            Loop = loop;

            NewVertex = DMesh3.InvalidID;
            NewTriangles = null;
        }



        /// <summary> check that Loop is a correctly oriented boundary loop of Mesh (MeshValidation.IsBoundaryLoop) </summary>
        public virtual ValidationStatus Validate()
        {
            ValidationStatus loopStatus = MeshValidation.IsBoundaryLoop(Mesh, Loop);
            return loopStatus;
        }


        /// <summary>
        /// Fill the hole; the new triangles get group_id. Fill() does not validate the loop.
        /// </summary>
        /// <returns>
        /// false if the loop has fewer than 3 vertices or a triangle could not be added (eg it would create a
        /// non-manifold edge); the changes are then backed out. true otherwise.
        /// </returns>
        public virtual bool Fill(int group_id = -1)
        {
            if (Loop.Vertices.Length < 3)
                return false;

            // this just needs one triangle
            if ( Loop.Vertices.Length == 3 ) {
                Index3i tri = new Index3i(Loop.Vertices[0], Loop.Vertices[2], Loop.Vertices[1]);
                int new_tid = Mesh.AppendTriangle(tri, group_id);
                if (new_tid < 0)
                    return false;
                NewTriangles = new int[1] { new_tid };
                NewVertex = DMesh3.InvalidID;
                return true;
            }

            // [TODO] 4-case? could check nbr normals to figure out best internal edge...


            // compute centroid
            Vector3d c = Vector3d.Zero;
            for (int i = 0; i < Loop.Vertices.Length; ++i)
                c += Mesh.GetVertex(Loop.Vertices[i]);
            c *= 1.0 / Loop.Vertices.Length;

            // add centroid vtx
            NewVertex = Mesh.AppendVertex(c);

            // stitch triangles
            MeshEditor editor = new MeshEditor(Mesh);
            try {
                NewTriangles = editor.AddTriangleFan_OrderedVertexLoop(NewVertex, Loop.Vertices, group_id);
            } catch {
                NewTriangles = null;
            }

            // if fill failed, back out vertex-add
            if ( NewTriangles == null ) {
                Mesh.RemoveVertex(NewVertex, true, false);
                NewVertex = DMesh3.InvalidID;
				return false;
            } else 
            	return true;

        }


    }
}
