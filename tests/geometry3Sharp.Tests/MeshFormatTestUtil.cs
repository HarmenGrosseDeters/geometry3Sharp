using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    static class MeshFormatTestUtil
    {
        /// <summary>
        /// Sphere with normals, UVs and per-vertex colors, centered at center
        /// </summary>
        public static DMesh3 AttributeSphere(Vector3d center, double radius = 1.0)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = 5 }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            mesh.EnableVertexColors(Vector3f.One);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d n = mesh.GetVertexNormal(vid);
                mesh.SetVertexColor(vid, new Vector3f(0.5 + 0.5 * n.x, 0.5 + 0.5 * n.y, 0.5 + 0.5 * n.z));
            }
            return mesh;
        }

        public static WriteOptions AllAttributes(bool binary)
        {
            WriteOptions options = WriteOptions.Defaults;
            options.bWriteBinary = binary;
            options.bPerVertexNormals = true;
            options.bPerVertexColors = true;
            options.bPerVertexUVs = true;
            return options;
        }

        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "g3tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static List<DMesh3> ReadAll(string path, out IOReadResult result)
        {
            DMesh3Builder builder = new DMesh3Builder();
            result = StandardMeshReader.ReadFile(path, ReadOptions.Defaults, builder);
            return builder.Meshes;
        }

        public static DMesh3 WriteRead(DMesh3 mesh, string extension, WriteOptions options)
        {
            string dir = TempDir();
            try {
                string path = Path.Combine(dir, "mesh." + extension);
                IOWriteResult w = StandardMeshWriter.WriteMesh(path, mesh, options);
                Assert.Equal(IOCode.Ok, w.code);
                IOReadResult r;
                List<DMesh3> meshes = ReadAll(path, out r);
                Assert.True(r.code == IOCode.Ok, r.message);
                Assert.Single(meshes);
                return meshes[0];
            } finally {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// Readers write vertices in the order of VertexIndices(), so a compact mesh maps 1:1
        /// </summary>
        public static void AssertSameTopology(DMesh3 expected, DMesh3 actual)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            Assert.Equal(expected.TriangleCount, actual.TriangleCount);
            int[] ev = expected.VertexIndices().ToArray(), av = actual.VertexIndices().ToArray();
            Dictionary<int, int> map = new Dictionary<int, int>();
            for (int i = 0; i < ev.Length; ++i)
                map[ev[i]] = av[i];
            int[] et = expected.TriangleIndices().ToArray(), at = actual.TriangleIndices().ToArray();
            for (int i = 0; i < et.Length; ++i) {
                Index3i a = expected.GetTriangle(et[i]), b = actual.GetTriangle(at[i]);
                Assert.Equal(new Index3i(map[a.a], map[a.b], map[a.c]), b);
            }
        }

        public static double MaxPositionError(DMesh3 expected, DMesh3 actual)
        {
            int[] ev = expected.VertexIndices().ToArray(), av = actual.VertexIndices().ToArray();
            double err = 0;
            for (int i = 0; i < ev.Length; ++i)
                err = Math.Max(err, expected.GetVertex(ev[i]).Distance(actual.GetVertex(av[i])));
            return err;
        }

        public static double MaxAttributeError(DMesh3 expected, DMesh3 actual, Func<DMesh3, int, Vector3d> attribF)
        {
            int[] ev = expected.VertexIndices().ToArray(), av = actual.VertexIndices().ToArray();
            double err = 0;
            for (int i = 0; i < ev.Length; ++i)
                err = Math.Max(err, attribF(expected, ev[i]).Distance(attribF(actual, av[i])));
            return err;
        }

        public static double MaxUVError(DMesh3 expected, DMesh3 actual)
        {
            int[] ev = expected.VertexIndices().ToArray(), av = actual.VertexIndices().ToArray();
            double err = 0;
            for (int i = 0; i < ev.Length; ++i)
                err = Math.Max(err, expected.GetVertexUV(ev[i]).Distance(actual.GetVertexUV(av[i])));
            return err;
        }

        public static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }
    }
}
