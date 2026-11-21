using System.IO;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Volumetric tetrahedral meshing (MeshTetrahedralization), boundary/region markers (TetMeshMarkers),
    /// and export to Medit, Gmsh, and VTK formats (TetMeshWriter).
    /// </summary>
    public static class TetMeshExample
    {
        /// <summary>
        /// Builds a valid tetrahedral mesh of a box, computes boundary patch and region markers,
        /// and formats the mesh in Gmsh 4.1 ASCII format.
        /// </summary>
        public static MeshTetrahedralization Run(out TetMeshMarkers markers, out string mshText)
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();

            var tet = new MeshTetrahedralization(box) {
                RelativeAlpha = 10,
                RelativeOffset = 200
            };
            if (!tet.Compute()) {
                markers = null;
                mshText = null;
                return tet;
            }

            markers = TetMeshMarkers.Compute(tet.Result, box);
            using (var sw = new StringWriter()) {
                TetMeshWriter.Write(sw, tet.Result, markers, TetMeshWriter.Format.Msh41);
                mshText = sw.ToString();
            }
            return tet;
        }
    }
}
