using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Compute per-vertex normals of a mesh as the area-weighted average of the adjacent triangle normals.
    /// Compute() fills Normals (indexed by vertex ID) without modifying the mesh; CopyTo() writes them into a DMesh3
    /// (enabling vertex normals if needed). For the common case use the static QuickCompute(mesh), which does both in place.
    /// Normals follow the triangle orientation (outward for the mesh generators); vertices without triangles get a zero normal.
    /// </summary>
    public class MeshNormals
    {
        /// <summary> input mesh </summary>
        public IMesh Mesh;
        /// <summary> computed unit normals, indexed by vertex ID (size MaxVertexID), available after Compute() </summary>
        public DVector<Vector3d> Normals;

        /// <summary>
        /// By default this is Mesh.GetVertex(). Can override to provide
        /// alternate vertex source.
        /// </summary>
        public Func<int, Vector3d> VertexF;



        /// <summary> type of normal to compute (currently only one) </summary>
        public enum NormalsTypes
        {
            Vertex_OneRingFaceAverage_AreaWeighted
        }
        public NormalsTypes NormalType;


        /// <summary> Set up normal computation for mesh. Call Compute() to compute the normals. </summary>
        public MeshNormals(IMesh mesh, NormalsTypes eType = NormalsTypes.Vertex_OneRingFaceAverage_AreaWeighted)
        {
            Mesh = mesh;
            NormalType = eType;
            Normals = new DVector<Vector3d>();
            VertexF = Mesh.GetVertex;
        }


        /// <summary> Compute the normals into Normals (the mesh is not modified) </summary>
        public void Compute()
        {
            Compute_FaceAvg_AreaWeighted();
        }


        /// <summary> computed normal of vertex vid </summary>
        public Vector3d this[int vid] {
            get { return Normals[vid]; }
        }


        /// <summary>
        /// Write the computed normals into the vertex normals of SetMesh (usually the input mesh), enabling vertex
        /// normals on it if necessary. Throws if SetMesh has fewer vertex IDs than Mesh.
        /// </summary>
        public void CopyTo(DMesh3 SetMesh)
        {
            if (SetMesh.MaxVertexID < Mesh.MaxVertexID)
                throw new Exception("MeshNormals.Set: SetMesh does not have enough vertices!");
            if (!SetMesh.HasVertexNormals)
                SetMesh.EnableVertexNormals(Vector3f.AxisY);
            int NV = Mesh.MaxVertexID;
            for ( int vi = 0; vi < NV; ++vi ) {
                if ( Mesh.IsVertex(vi) && SetMesh.IsVertex(vi) ) {
                    SetMesh.SetVertexNormal(vi, (Vector3f)Normals[vi]);
                }
            }
        }




        // Area-weighted face normals (half cross products) computed in parallel, summed into the vertices serially in
        // triangle order (deterministic: a parallel sum under a lock varied with the thread schedule), then normalized
        // whenever the sum is not zero (scale invariant: upstream left sums with |sum|^2 below 1e-6, ie one-rings with
        // an area below about 1e-3, unnormalized).
        void Compute_FaceAvg_AreaWeighted()
        {
            int NV = Mesh.MaxVertexID;
            if ( NV != Normals.size ) 
                Normals.resize(NV);
            for (int i = 0; i < NV; ++i)
                Normals[i] = Vector3d.Zero;

            int NT = Mesh.MaxTriangleID;
            Vector3d[] triN = new Vector3d[NT];
            gParallel.ForEach(Mesh.TriangleIndices(), (ti) => {
                Index3i tri = Mesh.GetTriangle(ti);
                triN[ti] = AreaNormal(VertexF(tri.a), VertexF(tri.b), VertexF(tri.c));
            });
            foreach (int ti in Mesh.TriangleIndices()) {
                Index3i tri = Mesh.GetTriangle(ti);
                Normals[tri.a] += triN[ti];
                Normals[tri.b] += triN[ti];
                Normals[tri.c] += triN[ti];
            }

            gParallel.BlockStartEnd(0, NV - 1, (vi_start, vi_end) => {
                for (int vi = vi_start; vi <= vi_end; vi++)
                    Normals[vi] = UnitOrZero(Normals[vi]);
            });
        }

        /// <summary> the normal of triangle (a,b,c) times its area </summary>
        static Vector3d AreaNormal(Vector3d a, Vector3d b, Vector3d c) => 0.5 * (b - a).Cross(c - a);

        /// <summary> v normalized if its length is positive and finite, else Zero (no absolute threshold) </summary>
        static Vector3d UnitOrZero(Vector3d v)
        {
            double len = v.Length;
            return (len > 0 && len < double.PositiveInfinity) ? v / len : Vector3d.Zero;
        }



        /// <summary>
        /// Compute area-weighted vertex normals and store them in mesh (in place), enabling vertex normals if needed.
        /// </summary>
        public static void QuickCompute(DMesh3 mesh)
        {
            MeshNormals normals = new MeshNormals(mesh);
            normals.Compute();
            normals.CopyTo(mesh);
        }


        /// <summary>
        /// Compute the area-weighted normal of a single vertex (does not modify the mesh). The type argument is ignored.
        /// </summary>
        public static Vector3d QuickCompute(DMesh3 mesh, int vid, NormalsTypes type = NormalsTypes.Vertex_OneRingFaceAverage_AreaWeighted)
        {
            Vector3d sum = Vector3d.Zero;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach ( int tid in mesh.VtxTrianglesItr(vid)) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                sum += AreaNormal(a, b, c);
            }
            return UnitOrZero(sum);
        }


    }
}
