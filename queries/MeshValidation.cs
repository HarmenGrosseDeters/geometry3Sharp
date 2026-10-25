using System;
using System.Collections.Generic;


namespace g3
{
    /// <summary>
    /// Result of a validity check (MeshValidation, hole fillers, curve insertion): Ok or the first problem found.
    /// </summary>
    public enum ValidationStatus
    {
        Ok,

        NotAVertex,
        NotBoundaryVertex,
        NotBoundaryEdge,

        NotATriangle,

        VerticesNotConnectedByEdge,
        IncorrectLoopOrientation,

        DuplicateTriangles,

        NearDegenerateMeshEdges,
        NearDenegerateInputGeometry,
    }


    /// <summary>
    /// Read-only topology checks for edge loops and duplicate triangles. Each returns ValidationStatus.Ok or
    /// the first problem found. For a full consistency check of the mesh data structure use
    /// DMesh3.CheckValidity(); for repair metrics see MeshRepairMetrics.
    /// </summary>
    public static class MeshValidation
    {

        /// <summary>
        /// Check that the loop vertices are vertices of mesh and consecutive ones (cyclically) are connected by
        /// an edge. Edges, orientation and boundary status are not checked.
        /// </summary>
        public static ValidationStatus IsEdgeLoop(DMesh3 mesh, EdgeLoop loop)
        {
           int N = loop.Vertices.Length;
            for ( int i = 0; i < N; ++i ) {
                if ( ! mesh.IsVertex(loop.Vertices[i]) )
                    return ValidationStatus.NotAVertex;
            }
            for (int i = 0; i < N; ++i) {
                int a = loop.Vertices[i];
                int b = loop.Vertices[(i + 1) % N];

                int eid = mesh.FindEdge(a, b);
                if (eid == DMesh3.InvalidID)
                    return ValidationStatus.VerticesNotConnectedByEdge;
            }
            return ValidationStatus.Ok;
        }



        /// <summary>
        /// Check that the loop is a boundary loop of mesh in boundary orientation (each consecutive vertex pair
        /// a to b is a boundary edge whose triangle also runs a to b), as produced by MeshBoundaryLoops and
        /// expected by the hole fillers.
        /// </summary>
        public static ValidationStatus IsBoundaryLoop(DMesh3 mesh, EdgeLoop loop)
        {
            int N = loop.Vertices.Length;

            for ( int i = 0; i < N; ++i ) {
                if ( ! mesh.IsBoundaryVertex(loop.Vertices[i]) )
                    return ValidationStatus.NotBoundaryVertex;
            }

            for ( int i = 0; i < N; ++i ) {
                int a = loop.Vertices[i];
                int b = loop.Vertices[(i + 1) % N];

                int eid = mesh.FindEdge(a, b);
                if (eid == DMesh3.InvalidID)
                    return ValidationStatus.VerticesNotConnectedByEdge;

                if (mesh.IsBoundaryEdge(eid) == false)
                    return ValidationStatus.NotBoundaryEdge;

                Index2i ev = mesh.GetOrientedBoundaryEdgeV(eid);
                if (!(ev.a == a && ev.b == b))
                    return ValidationStatus.IncorrectLoopOrientation;
            }

            return ValidationStatus.Ok;
        }



        /// <summary>
        /// Only detects the simplest case: a triangle whose three neighbours are all the same triangle (an
        /// isolated two-sided pair on the same three vertices). Duplicates that are attached to other
        /// triangles or use different (unwelded) vertices are not found; see RemoveDuplicateTriangles.
        /// </summary>
        public static ValidationStatus HasDuplicateTriangles(DMesh3 mesh)
        {
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i nbrs = mesh.GetTriNeighbourTris(tid);
                if (nbrs.a == nbrs.b && nbrs.b == nbrs.c && nbrs.a != DMesh3.InvalidID)
                    return ValidationStatus.DuplicateTriangles;
            }

            return ValidationStatus.Ok;
        }


    }
}
