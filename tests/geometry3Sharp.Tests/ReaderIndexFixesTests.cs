using System;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Cases found by the reader fuzzing (docs/QA.md): out-of-range indices that crashed the OBJ and OFF readers, and a
    /// PLY header count that made the reader allocate for the claimed size before reading anything.
    /// </summary>
    public class ReaderIndexFixesTests
    {
        static IOReadResult Read(MeshFormatReader reader, string text, out DMesh3Builder builder)
        {
            builder = new DMesh3Builder();
            return reader.ReadFile(new MemoryStream(Encoding.ASCII.GetBytes(text)), builder, ReadOptions.Defaults, null);
        }

        [Fact]
        public void OBJ_NormalAndUVIndicesBeyondTheIntRangeOfTheirProducts_AreIgnored()
        {
            // 3 * 800000000 overflows int: the normal index passed the bounds check as a negative offset and threw
            string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nvt 0 0\nvt 1 0\nvt 0 1\n"
                + "f 1/1/800000000 2/2/800000000 3/3/1\n"
                + "f 1/1100000000/1 3/1100000000/1 2/3/1\n";
            DMesh3Builder builder;
            IOReadResult r = Read(new OBJFormatReader(), obj, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 mesh = builder.Meshes.Single();
            Assert.Equal(2, mesh.TriangleCount);
            Assert.True(mesh.Vertices().All(v => v.IsFinite));
        }

        [Fact]
        public void OFF_PolygonWithAnInvalidVertexIndex_IsSkipped()
        {
            // a negative index threw ArgumentOutOfRange inside DMesh3 (FindTriangle on vertex -1); an index past the
            // vertices was dropped silently by DMesh3.AppendTriangle
            string off = "OFF\n4 4 0\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n"
                + "3 0 2 1\n3 -1 0 1\n3 0 3 7\n3 1 2 3\n";
            DMesh3Builder builder;
            IOReadResult r = Read(new OFFFormatReader(), off, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(2, builder.Meshes.Single().TriangleCount);
        }

        [Fact]
        public void OFF_PolygonSizeOfIntMax_IsSkipped()
        {
            // nV + 1 overflows for nV = int.MaxValue, so the "enough tokens" check passed
            string off = "OFF\n3 2 0\n0 0 0\n1 0 0\n0 1 0\n2147483647 0 1 2\n3 0 1 2\n";
            DMesh3Builder builder;
            IOReadResult r = Read(new OFFFormatReader(), off, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(1, builder.Meshes.Single().TriangleCount);
        }

        [Fact]
        public void PLY_HeaderCountFarBeyondTheData_DoesNotAllocateIt()
        {
            string ply = "ply\nformat ascii 1.0\nelement vertex 400000000\nproperty float x\nproperty float y\nproperty float z\n"
                + "element face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n";
#if NETCOREAPP
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            DMesh3Builder builder;
            IOReadResult r = Read(new PLYFormatReader(), ply, out builder);
            Assert.NotEqual(IOCode.Ok, r.code);         // the file ends after 3 of the 400 million vertices
#if NETCOREAPP
            // (before: an int[400000000] vertex map, 1.5 GB)
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 16 * 1024 * 1024);
#endif
        }

        // (passed before too: guards the vertex map that now grows per vertex read)
        [Fact]
        public void PLY_FaceIndexBeyondTheVerticesRead_IsSkipped()
        {
            string ply = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n"
                + "element face 2\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n3 0 1 5\n";
            DMesh3Builder builder;
            IOReadResult r = Read(new PLYFormatReader(), ply, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(1, builder.Meshes.Single().TriangleCount);
        }

        [Fact]
        public void OFF_PolygonWithAnIndexPastTheVertices_IsSkippedAsAWhole()
        {
            // before, the fan triangles with valid indices were kept (0 1 2) and the others dropped
            string off = "OFF\n4 2 0\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n4 0 1 2 9\n3 1 2 3\n";
            DMesh3Builder builder;
            IOReadResult r = Read(new OFFFormatReader(), off, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 mesh = builder.Meshes.Single();
            Assert.Equal(1, mesh.TriangleCount);
            Assert.Equal(new Index3i(1, 2, 3), mesh.GetTriangle(mesh.TriangleIndices().First()));
        }

        [Fact]
        public void PLY_ElementWithoutPropertiesAndAHugeCount_IsSkippedAtOnce()
        {
            // a 250-byte file took 25 s: the loop ran over 2^31 elements that have nothing to read
            string ply = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n"
                + "element foo 2147483647\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n"
                + "0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n";
            var timer = System.Diagnostics.Stopwatch.StartNew();
            DMesh3Builder builder;
            IOReadResult r = Read(new PLYFormatReader(), ply, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(1, builder.Meshes.Single().TriangleCount);
            Assert.True(timer.ElapsedMilliseconds < 2000, timer.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void PLY_NonIntegerListLengthsAndIndices_AreRejected()
        {
            string header = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n"
                + "element face 1\nproperty list float float vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n";
            DMesh3Builder builder;
            // a NaN or fractional list length is an error (the cast gave int.MinValue, 0 or a truncated length,
            // depending on the runtime)
            Assert.Equal(IOCode.FileParsingError, Read(new PLYFormatReader(), header + "nan 0 1 2\n", out builder).code);
            Assert.Equal(IOCode.FileParsingError, Read(new PLYFormatReader(), header + "2.5 0 1 2\n", out builder).code);
            // a fractional index makes the face invalid (it was truncated: -0.5 1 2.9 read as 0 1 2)
            IOReadResult r = Read(new PLYFormatReader(), header + "3 -0.5 1 2.9\n", out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(0, builder.Meshes.Single().TriangleCount);
        }
    }
}
