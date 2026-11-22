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

        /// <summary>
        /// float32 output (binary STL; glTF/GLB and 3MF when a consumer reads floats) loses precision at UTM-sized
        /// coordinates: a float32 has 24 mantissa bits, so at 5,000,000 m the spacing is 0.5 m. Recentre before
        /// writing, keep the offset, and add it back after reading. Returns the vertex error of a binary STL
        /// round trip, written directly and written recentred.
        /// </summary>
        public static (double direct, double recentred) Float32RoundTrip(string directory)
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sphere, new Vector3d(500000, 5000000, 30));

            WriteOptions options = WriteOptions.Defaults;
            options.bWriteBinary = true;

            // direct: every coordinate is rounded to float32 at its full size
            DMesh3 back = WriteAndRead(Path.Combine(directory, "direct.stl"), sphere, options);
            double direct = VertexError(sphere, back, Vector3d.Zero);

            // recentred: write a copy moved to the bounding box centre, add the centre back after reading
            Vector3d center = sphere.GetBounds().Center;
            DMesh3 local = new DMesh3(sphere);
            MeshTransforms.Translate(local, -center);
            back = WriteAndRead(Path.Combine(directory, "local.stl"), local, options);
            double recentred = VertexError(sphere, back, center);
            return (direct, recentred);
        }

        static DMesh3 WriteAndRead(string path, DMesh3 mesh, WriteOptions options)
        {
            IOWriteResult written = StandardMeshWriter.WriteMesh(path, mesh, options);
            if (written.code != IOCode.Ok)
                throw new IOException(written.message);
            DMesh3 back = StandardMeshReader.ReadMesh(path);
            if (back == null)
                throw new IOException("read failed: " + path);
            return back;
        }

        // largest distance of a read-back vertex (plus offset) from the surface of the expected mesh
        static double VertexError(DMesh3 expected, DMesh3 actual, Vector3d offset)
        {
            DMeshAABBTree3 tree = new DMeshAABBTree3(expected, true);
            double maxSqr = 0;
            foreach (int vid in actual.VertexIndices()) {
                double distSqr;
                tree.FindNearestTriangle(actual.GetVertex(vid) + offset, out distSqr);
                maxSqr = System.Math.Max(maxSqr, distSqr);
            }
            return System.Math.Sqrt(maxSqr);
        }
    }
}
