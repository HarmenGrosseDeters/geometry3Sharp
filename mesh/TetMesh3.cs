// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// A tetrahedral mesh: vertices, tetrahedra and the triangles of its boundary. Every tetrahedron is positively
    /// oriented (ExactPredicates.Orient3d(v0, v1, v2, v3) > 0); boundary triangles are oriented outwards (counter-clockwise
    /// seen from outside), their vertices are vertices of the tetrahedra. See TetMeshQuality for the validity check
    /// and the quality measures (docs/design/TET_MESHING.md).
    /// </summary>
    public class TetMesh3
    {
        public List<Vector3d> Vertices = new List<Vector3d>();
        public List<Index4i> Tets = new List<Index4i>();
        public List<Index3i> BoundaryTriangles = new List<Index3i>();

        /// <summary> the boundary as a DMesh3 (vertices of the tet mesh that no boundary triangle uses are left out) </summary>
        public DMesh3 ToSurfaceMesh()
        {
            var mesh = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (Index3i t in BoundaryTriangles) {
                var m = Index3i.Zero;
                for (int j = 0; j < 3; ++j) {
                    if (!map.TryGetValue(t[j], out int vid)) {
                        vid = mesh.AppendVertex(Vertices[t[j]]);
                        map[t[j]] = vid;
                    }
                    m[j] = vid;
                }
                mesh.AppendTriangle(m);
            }
            return mesh;
        }
    }
}
