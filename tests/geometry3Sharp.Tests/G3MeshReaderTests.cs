using System;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The .g3mesh reader (gSerialization.Restore) takes the stored DMesh3 buffers over as they are. Damaged or hostile
    /// files must give a read error, not a mesh with broken internal references, and must not make the reader allocate
    /// the sizes the file claims (found by the reader fuzzing of docs/QA.md).
    /// </summary>
    public class G3MeshReaderTests
    {
        static DMesh3 TestMesh()
        {
            DMesh3 mesh = MeshFormatTestUtil.AttributeSphere(Vector3d.Zero);
            mesh.EnableTriangleGroups(3);
            // a hole (not compact), a flipped triangle (inconsistent orientation is valid) and an isolated vertex
            mesh.RemoveTriangle(mesh.TriangleIndices().First());
            mesh.ReverseTriOrientation(mesh.TriangleIndices().Skip(30).First());
            mesh.AppendVertex(new Vector3d(5, 5, 5));
            return mesh;
        }

        static byte[] Store(DMesh3 mesh)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(1);        // number of meshes
            gSerialization.Store(mesh, writer);
            writer.Flush();
            return stream.ToArray();
        }

        static IOReadResult Read(byte[] data, out DMesh3Builder builder)
        {
            builder = new DMesh3Builder();
            return StandardMeshReader.ReadFile(new MemoryStream(data, false), "g3mesh", ReadOptions.Defaults, builder);
        }

        [Fact]
        public void ValidFile_ReadsBack()
        {
            DMesh3 mesh = TestMesh();
            DMesh3Builder builder;
            IOReadResult r = Read(Store(mesh), out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 read = builder.Meshes.Single();
            Assert.Equal(mesh.VertexCount, read.VertexCount);
            Assert.Equal(mesh.TriangleCount, read.TriangleCount);
            Assert.Equal(mesh.EdgeCount, read.EdgeCount);
            foreach (int t in mesh.TriangleIndices()) {
                Assert.Equal(mesh.GetTriangle(t), read.GetTriangle(t));
                Assert.Equal(mesh.GetTriangleGroup(t), read.GetTriangleGroup(t));
            }
            foreach (int v in mesh.VertexIndices()) {
                Assert.Equal(mesh.GetVertex(v), read.GetVertex(v));
                Assert.Equal(mesh.GetVertexUV(v), read.GetVertexUV(v));
            }
        }

        [Fact]
        public void EdgeReferencingAnotherTriangle_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            int eid = mesh.EdgeIndices().First(e => mesh.IsBoundaryEdge(e) == false);
            Index2i et = mesh.GetEdgeT(eid);
            int other = mesh.TriangleIndices().First(t => t != et.a && t != et.b
                && IndexUtil.find_edge_index_in_tri(mesh.GetEdgeV(eid).a, mesh.GetEdgeV(eid).b, mesh.GetTriangle(t).array) == DMesh3.InvalidID);
            mesh.EdgesBuffer[4 * eid + 2] = other;
            DMesh3Builder builder;
            IOReadResult r = Read(Store(mesh), out builder);
            Assert.Equal(IOCode.GenericReaderError, r.code);
            Assert.Contains("invalid mesh data", r.message);
        }

        [Fact]
        public void EdgeVertexOutOfRange_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            mesh.EdgesBuffer[4 * mesh.EdgeIndices().First() + 1] = mesh.MaxVertexID + 100;
            DMesh3Builder builder;
            Assert.Equal(IOCode.GenericReaderError, Read(Store(mesh), out builder).code);
        }

        [Fact]
        public void AttributeBufferOfTheWrongSize_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            mesh.UVBuffer.resize(mesh.UVBuffer.size - 2);
            DMesh3Builder builder;
            IOReadResult r = Read(Store(mesh), out builder);
            Assert.Equal(IOCode.GenericReaderError, r.code);
            Assert.Contains("uvs", r.message);
        }

        [Fact]
        public void TriangleWithoutItsEdges_IsAReadError()
        {
            // an edge removed from the stored edges: its triangles have no edge on that side
            DMesh3 mesh = TestMesh();
            int eid = mesh.EdgeIndices().First(e => mesh.IsBoundaryEdge(e) == false);
            mesh.EdgesRefCounts.RawRefCounts[eid] = RefCountVector.invalid;
            DMesh3Builder builder;
            IOReadResult r = Read(Store(mesh), out builder);
            Assert.Equal(IOCode.GenericReaderError, r.code);
            Assert.Contains("invalid mesh data", r.message);
        }

        [Fact]
        public void HugeCount_FailsWithoutAllocatingIt()
        {
            // one mesh, version 2, no components, then a vertex buffer that claims 16 million doubles (128 MB) in a
            // stream of 64 more bytes
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(1);
            writer.Write(gSerialization.DMesh3Version);
            writer.Write(0);
            writer.Write(1 << 24);
            writer.Write(new byte[64]);
            writer.Flush();
#if NETCOREAPP
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            DMesh3Builder builder;
            IOReadResult r = Read(stream.ToArray(), out builder);
            Assert.Equal(IOCode.GenericReaderError, r.code);
#if NETCOREAPP
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 16 * 1024 * 1024);
#endif
        }

        // (these failed before too, with exceptions from the allocation; kept as a guard for the count check)
        [Fact]
        public void NegativeAndOverflowingCounts_AreReadErrors()
        {
            foreach (int n in new[] { -1, int.MinValue, int.MaxValue }) {
                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream);
                writer.Write(1);
                writer.Write(gSerialization.DMesh3Version);
                writer.Write(0);
                writer.Write(n);
                writer.Write(new byte[64]);
                writer.Flush();
                DMesh3Builder builder;
                Assert.Equal(IOCode.GenericReaderError, Read(stream.ToArray(), out builder).code);
            }
        }

        static void AssertReadError(byte[] data, string messagePart = "invalid mesh data")
        {
            DMesh3Builder builder;
            IOReadResult r = Read(data, out builder);
            Assert.Equal(IOCode.GenericReaderError, r.code);
            Assert.Contains(messagePart, r.message);
        }

        [Fact]
        public void DuplicateEdge_IsAReadError()
        {
            // an interior edge split into two stored edges with the same vertex pair, one triangle each
            DMesh3 mesh = TestMesh();
            int eid = mesh.EdgeIndices().First(e => mesh.IsBoundaryEdge(e) == false);
            Index2i ev = mesh.GetEdgeV(eid), et = mesh.GetEdgeT(eid);
            mesh.EdgesBuffer[4 * eid + 3] = DMesh3.InvalidID;
            mesh.EdgesBuffer.Add(ev.a); mesh.EdgesBuffer.Add(ev.b); mesh.EdgesBuffer.Add(et.b); mesh.EdgesBuffer.Add(DMesh3.InvalidID);
            mesh.EdgesRefCounts.RawRefCounts.Add(1);
            AssertReadError(Store(mesh), "duplicate edge");
        }

        [Fact]
        public void UnsortedEdge_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            int eid = mesh.EdgeIndices().First();
            int a = mesh.EdgesBuffer[4 * eid];
            mesh.EdgesBuffer[4 * eid] = mesh.EdgesBuffer[4 * eid + 1];
            mesh.EdgesBuffer[4 * eid + 1] = a;
            AssertReadError(Store(mesh), "edge vertices");
        }

        [Fact]
        public void TriangleWithARepeatedVertex_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            int tid = mesh.TriangleIndices().First();
            mesh.TrianglesBuffer[3 * tid + 2] = mesh.TrianglesBuffer[3 * tid];
            AssertReadError(Store(mesh));
        }

        [Fact]
        public void EdgeReferenceCountOtherThanOne_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            mesh.EdgesRefCounts.RawRefCounts[mesh.EdgeIndices().First()] = 2;
            AssertReadError(Store(mesh), "edge reference count");
        }

        [Fact]
        public void GroupBufferOfTheWrongSize_IsAReadError()
        {
            DMesh3 mesh = TestMesh();
            mesh.GroupsBuffer.resize(mesh.GroupsBuffer.size - 1);
            AssertReadError(Store(mesh), "groups");
        }

        [Fact]
        public void VertexReferenceCountsLongerThanTheVertices_AreAReadError()
        {
            DMesh3 mesh = TestMesh();
            mesh.VerticesRefCounts.RawRefCounts.Add(1);
            AssertReadError(Store(mesh), "vertex reference counts");
        }

        [Fact]
        public void TruncatedFile_IsAReadError()
        {
            byte[] data = Store(TestMesh());
            // inside the vertex buffer (after the mesh count, version, components and the buffer length)
            AssertReadError(data.Take(100).ToArray(), "");
            AssertReadError(data.Take(data.Length - 10).ToArray(), "");
        }

        [Fact]
        public void Version1File_ReadsBack()
        {
            // version 1 has no vertex reference counts (the last array): isolated vertices are not restored
            DMesh3 mesh = TestMesh();
            byte[] data = Store(mesh);
            int n = mesh.VerticesRefCounts.RawRefCounts.size;
            byte[] v1 = data.Take(data.Length - 4 - 2 * n).ToArray();
            BitConverter.GetBytes(1).CopyTo(v1, 4);
            DMesh3Builder builder;
            IOReadResult r = Read(v1, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 read = builder.Meshes.Single();
            Assert.Equal(mesh.TriangleCount, read.TriangleCount);
            Assert.Equal(mesh.VertexCount - 1, read.VertexCount);
        }

        [Fact]
        public void PointCloud_ReadsBack()
        {
            var mesh = new DMesh3();
            for (int k = 0; k < 10; ++k)
                mesh.AppendVertex(new Vector3d(k, 2 * k, 0));
            mesh.RemoveVertex(9);
            DMesh3Builder builder;
            IOReadResult r = Read(Store(mesh), out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(9, builder.Meshes.Single().VertexCount);
        }

        /// <summary>A stream without a length (CanSeek false), like a network or zip stream.</summary>
        class ForwardOnlyStream : Stream
        {
            readonly Stream inner;
            public ForwardOnlyStream(byte[] data) { inner = new MemoryStream(data, false); }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Fact]
        public void ForwardOnlyStream_ValidFileReads_HugeCountFailsWithoutAllocatingIt()
        {
            DMesh3 mesh = TestMesh();
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult r = StandardMeshReader.ReadFile(new ForwardOnlyStream(Store(mesh)), "g3mesh", ReadOptions.Defaults, builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(mesh.TriangleCount, builder.Meshes.Single().TriangleCount);

            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(1);
            writer.Write(gSerialization.DMesh3Version);
            writer.Write(0);
            writer.Write(1 << 27);          // 1 GB of doubles claimed, 64 bytes there
            writer.Write(new byte[64]);
            writer.Flush();
#if NETCOREAPP
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            builder = new DMesh3Builder();
            r = StandardMeshReader.ReadFile(new ForwardOnlyStream(stream.ToArray()), "g3mesh", ReadOptions.Defaults, builder);
            Assert.Equal(IOCode.GenericReaderError, r.code);
#if NETCOREAPP
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 16 * 1024 * 1024);
#endif
        }

        [Fact]
        public void HighValenceVertex_ReadsInLinearTime()
        {
            // a fan of 30000 triangles around one vertex: the duplicate-edge check must not be quadratic in the valence
            var mesh = new DMesh3();
            int center = mesh.AppendVertex(Vector3d.Zero);
            int n = 30000;
            int first = mesh.AppendVertex(new Vector3d(1, 0, 0)), prev = first;
            for (int k = 1; k < n; ++k) {
                double a = 2 * Math.PI * k / n;
                int v = mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0));
                mesh.AppendTriangle(center, prev, v);
                prev = v;
            }
            byte[] data = Store(mesh);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            DMesh3Builder builder;
            IOReadResult r = Read(data, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(n - 1, builder.Meshes.Single().TriangleCount);
            // (quadratic: several seconds; linear: tens of milliseconds)
            Assert.True(timer.ElapsedMilliseconds < 2000, timer.ElapsedMilliseconds + " ms");
        }
    }
}
