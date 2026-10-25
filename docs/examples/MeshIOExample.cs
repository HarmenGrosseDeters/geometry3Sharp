using System.Collections.Generic;
using System.IO;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Mesh I/O: write a mesh as PLY, 3MF and GLB and read it back. StandardMeshReader / StandardMeshWriter
    /// pick the format from the file extension and report errors as an IOCode (they do not throw).
    /// </summary>
    public static class MeshIOExample
    {
        /// <summary> Writes a sphere in each format to directory, reads each file back, returns the meshes by extension. </summary>
        public static Dictionary<string, DMesh3> Run(string directory)
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sphere, new Vector3d(500000, 5000000, 30));   // eg UTM coordinates

            // always start from WriteOptions.Defaults (a struct: new WriteOptions() has 0 precision digits)
            WriteOptions options = WriteOptions.Defaults;
            options.bWriteBinary = true;          // binary PLY (and STL)
            options.bPerVertexNormals = true;     // PLY, glTF and OBJ write normals

            var result = new Dictionary<string, DMesh3>();
            foreach (string ext in new[] { "ply", "3mf", "glb" }) {
                string path = Path.Combine(directory, "sphere." + ext);
                IOWriteResult written = StandardMeshWriter.WriteMesh(path, sphere, options);
                if (written.code != IOCode.Ok)
                    throw new IOException(written.message);

                // ReadFile returns every mesh of the file (3MF: one per build item, glTF: one per mesh node)
                var builder = new DMesh3Builder();
                IOReadResult read = StandardMeshReader.ReadFile(path, ReadOptions.Defaults, builder);
                if (read.code != IOCode.Ok)
                    throw new IOException(read.message);
                result[ext] = builder.Meshes[0];
                // for the first mesh only: DMesh3 mesh = StandardMeshReader.ReadMesh(path);  (null on error)
            }
            return result;
        }
    }
}
