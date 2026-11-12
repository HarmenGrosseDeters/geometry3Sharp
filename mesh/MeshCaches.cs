using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace g3
{
    /*
     * Basic cache of per-triangle information for a DMesh3
     */
    public class MeshTriInfoCache
    {
        public DVector<Vector3d> Centroids;
        public DVector<Vector3d> Normals;
        public DVector<double> Areas;

        public MeshTriInfoCache(DMesh3 mesh)
        {
            int NT = mesh.MaxTriangleID;     // indexed by triangle id (not compact after removals)
            Centroids = new DVector<Vector3d>(); Centroids.resize(NT);
            Normals = new DVector<Vector3d>(); Normals.resize(NT);
            Areas = new DVector<double>(); Areas.resize(NT);
            gParallel.ForEach(mesh.TriangleIndices(), (tid) => {
                Vector3d c, n; double a;
                mesh.GetTriInfo(tid, out n, out a, out c);
                Centroids[tid] = c;
                Normals[tid] = n;
                Areas[tid] = a;
            });
        }

        /// <summary>
        /// As MeshTriInfoCache(mesh), but scale-free: a triangle has its area and unit normal whenever its cross product
        /// is not zero. DMesh3.GetTriInfo (Vector3d.Normalize with the absolute epsilon 2.2e-16) gives area and normal 0 to
        /// every triangle with a cross product shorter than that, so for a mesh of edges below about 1e-8 (eg a part of
        /// size 1e-7 in a unit frame) all areas were 0. Same arithmetic otherwise, so the same values bit for bit for every
        /// other triangle, and scaling the mesh by 2^k scales areas by 2^2k exactly and keeps the normals.
        /// </summary>
        internal MeshTriInfoCache(DMesh3 mesh, bool scaleFree)
        {
            int NT = mesh.MaxTriangleID;
            Centroids = new DVector<Vector3d>(); Centroids.resize(NT);
            Normals = new DVector<Vector3d>(); Normals.resize(NT);
            Areas = new DVector<double>(); Areas.resize(NT);
            double epsilon = scaleFree ? 0 : MathUtil.Epsilon;
            gParallel.ForEach(mesh.TriangleIndices(), (tid) => {
                Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
                mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
                // (DMesh3.GetTriInfo and MathUtil.FastNormalArea, with the epsilon of Normalize as a parameter)
                Centroids[tid] = (1.0 / 3.0) * (v0 + v1 + v2);
                Vector3d n = (v1 - v0).Cross(v2 - v0);
                Areas[tid] = 0.5 * n.Normalize(epsilon);
                Normals[tid] = n;
            });
        }

        public void GetTriInfo(int tid, ref Vector3d n, ref double a, ref Vector3d c)
        {
            c = Centroids[tid];
            n = Normals[tid];
            a = Areas[tid];
        }
    }
}
