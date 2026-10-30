using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace g3
{
    public static class gSerialization
    {

        public static void Store(Vector2f v, BinaryWriter writer)
        {
            writer.Write(v.x);
            writer.Write(v.y);
        }
        public static void Restore(ref Vector2f v, BinaryReader reader)
        {
            v.x = reader.ReadSingle();
            v.y = reader.ReadSingle();
        }

        public static void Store(Vector2d v, BinaryWriter writer)
        {
            writer.Write(v.x);
            writer.Write(v.y);
        }
        public static void Restore(ref Vector2d v, BinaryReader reader)
        {
            v.x = reader.ReadDouble();
            v.y = reader.ReadDouble();
        }

        public static void Store(Vector3f v, BinaryWriter writer)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }
        public static void Restore(ref Vector3f v, BinaryReader reader)
        {
            v.x = reader.ReadSingle();
            v.y = reader.ReadSingle();
            v.z = reader.ReadSingle();
        }
        public static void Store(Vector3d v, BinaryWriter writer)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }
        public static void Restore(ref Vector3d v, BinaryReader reader)
        {
            v.x = reader.ReadDouble();
            v.y = reader.ReadDouble();
            v.z = reader.ReadDouble();
        }


        public static void Store(Quaternionf q, BinaryWriter writer)
        {
            writer.Write(q.x);
            writer.Write(q.y);
            writer.Write(q.z);
            writer.Write(q.w);
        }
        public static void Restore(ref Quaternionf q, BinaryReader reader)
        {
            q.x = reader.ReadSingle();
            q.y = reader.ReadSingle();
            q.z = reader.ReadSingle();
            q.w = reader.ReadSingle();
        }



        public static void Store(Frame3f vFrame, BinaryWriter writer)
        {
            Store(vFrame.Origin, writer);
            Store(vFrame.Rotation, writer);
        }
        public static void Restore(ref Frame3f vFrame, BinaryReader reader)
        {
            Vector3f origin = Vector3f.Zero;
            Quaternionf orientation = Quaternionf.Identity;
            Restore(ref origin, reader);
            Restore(ref orientation, reader);
            vFrame = new Frame3f(origin, orientation);
        }


        public static void Store(AxisAlignedBox2d b, BinaryWriter writer)
        {
            Store(b.Min, writer);
            Store(b.Max, writer);
        }
        public static void Restore(ref AxisAlignedBox2d b, BinaryReader reader)
        {
            Restore(ref b.Min, reader);
            Restore(ref b.Max, reader);
        }





        public static void Store(List<int> values, BinaryWriter writer)
        {
            writer.Write(values.Count);
            for (int i = 0; i < values.Count; ++i)
                writer.Write(values[i]);
        }
        public static void Restore(List<int> values, BinaryReader reader)
        {
            int N = reader.ReadInt32();
            for (int i = 0; i < N; ++i)
                values.Add(reader.ReadInt32());
        }


        public static void Store(List<float> values, BinaryWriter writer)
        {
            writer.Write(values.Count);
            for (int i = 0; i < values.Count; ++i)
                writer.Write(values[i]);
        }
        public static void Restore(List<float> values, BinaryReader reader)
        {
            int N = reader.ReadInt32();
            for (int i = 0; i < N; ++i)
                values.Add(reader.ReadSingle());
        }



        public static void Store(List<double> values, BinaryWriter writer)
        {
            writer.Write(values.Count);
            for (int i = 0; i < values.Count; ++i)
                writer.Write(values[i]);
        }
        public static void Restore(List<double> values, BinaryReader reader)
        {
            int N = reader.ReadInt32();
            for (int i = 0; i < N; ++i)
                values.Add(reader.ReadDouble());
        }


        public static void Store(DCurve3 curve, BinaryWriter writer)
        {
            writer.Write(curve.Closed);
            writer.Write(curve.VertexCount);
            for (int i = 0; i < curve.VertexCount; ++i) {
                writer.Write(curve[i].x);
                writer.Write(curve[i].y);
                writer.Write(curve[i].z);
            }
        }
        public static void Restore(DCurve3 curve, BinaryReader reader)
        {
            curve.Closed = reader.ReadBoolean();
            int count = reader.ReadInt32();
            for ( int i = 0; i < count; ++i ) {
                double x = reader.ReadDouble();
                double y = reader.ReadDouble();
                double z = reader.ReadDouble();
                curve.AppendVertex(new Vector3d(x, y, z));
            }
        }




		public static void Store(PolyLine2d polyline, BinaryWriter writer)
		{
			writer.Write(polyline.VertexCount);
			for (int i = 0; i < polyline.VertexCount; ++i) {
				writer.Write(polyline[i].x);
				writer.Write(polyline[i].y);
			}
		}

		public static void Restore(PolyLine2d polyline, BinaryReader reader)
		{
			int count = reader.ReadInt32();
			for (int i = 0; i < count; ++i) {
				double x = reader.ReadDouble();
				double y = reader.ReadDouble();
				polyline.AppendVertex(new Vector2d(x, y));
			}
		}



        public static void Store(Polygon2d polygon, BinaryWriter writer)
        {
            writer.Write(polygon.VertexCount);
            for (int i = 0; i < polygon.VertexCount; ++i) {
                writer.Write(polygon[i].x);
                writer.Write(polygon[i].y);
            }
        }

        public static void Restore(Polygon2d polygon, BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for ( int i = 0; i < count; ++i ) {
                double x = reader.ReadDouble();
                double y = reader.ReadDouble();
                polygon.AppendVertex(new Vector2d(x, y));
            }
        }




        public static void Store(GeneralPolygon2d polygon, BinaryWriter writer)
        {
            Store(polygon.Outer, writer);
            writer.Write(polygon.Holes.Count);
            for ( int i = 0; i < polygon.Holes.Count; ++i )
                Store(polygon.Holes[i], writer);
        }

        public static void Restore(GeneralPolygon2d polygon, BinaryReader reader)
        {
            Polygon2d outer = new Polygon2d();
            Restore(outer, reader);
            polygon.Outer = outer;

            int hole_count = reader.ReadInt32();
            for ( int i = 0; i < hole_count; ++i ) {
                Polygon2d holepoly = new Polygon2d();
                Restore(holepoly, reader);
                polygon.AddHole(holepoly, false);
            }
        }






        public static void Store(Segment2d segment, BinaryWriter writer)
        {
            writer.Write(segment.Center.x);
            writer.Write(segment.Center.y);
            writer.Write(segment.Direction.x);
            writer.Write(segment.Direction.y);
            writer.Write(segment.Extent);
        }
        public static void Restore(ref Segment2d segment, BinaryReader reader)
        {
            segment.Center.x = reader.ReadDouble();
            segment.Center.y = reader.ReadDouble();
            segment.Direction.x = reader.ReadDouble();
            segment.Direction.y = reader.ReadDouble();
            segment.Extent = reader.ReadDouble();
        }


        public static void Store(Arc2d arc, BinaryWriter writer)
        {
            writer.Write(arc.Center.x);
            writer.Write(arc.Center.y);
            writer.Write(arc.Radius);
            writer.Write(arc.AngleStartDeg);
            writer.Write(arc.AngleEndDeg);
            writer.Write(arc.IsReversed);
        }
        public static void Restore(ref Arc2d arc, BinaryReader reader)
        {
            arc.Center.x = reader.ReadDouble();
            arc.Center.y = reader.ReadDouble();
            arc.Radius = reader.ReadDouble();
            arc.AngleStartDeg = reader.ReadDouble();
            arc.AngleEndDeg = reader.ReadDouble();
            arc.IsReversed = reader.ReadBoolean();
        }


        public static void Store(Circle2d circle, BinaryWriter writer)
        {
            writer.Write(circle.Center.x);
            writer.Write(circle.Center.y);
            writer.Write(circle.Radius);
            writer.Write(circle.IsReversed);
        }
        public static void Restore(ref Circle2d circle, BinaryReader reader)
        {
            circle.Center.x = reader.ReadDouble();
            circle.Center.y = reader.ReadDouble();
            circle.Radius = reader.ReadDouble();
            circle.IsReversed = reader.ReadBoolean();
        }


        public static void Store(ParametricCurveSequence2 sequence, BinaryWriter writer)
        {
            writer.Write(sequence.IsClosed);
            writer.Write((int)sequence.Count);
            foreach (IParametricCurve2d c in sequence.Curves)
                Store(c, writer);
        }

        public static void Restore(ref ParametricCurveSequence2 sequence, BinaryReader reader)
        {
            sequence.IsClosed = reader.ReadBoolean();
            int N = reader.ReadInt32();
            for ( int i = 0; i < N; ++i ) {
                IParametricCurve2d c;
                Restore(out c, reader);
                sequence.Append(c);
            }
        }



        public static void Store(IParametricCurve2d curve, BinaryWriter writer)
        {
            if (curve is Segment2d) {
                writer.Write((int)1);
                Store((Segment2d)curve, writer);
            } else if (curve is Circle2d) {
                writer.Write((int)2);
                Store((Circle2d)curve, writer);
            } else if (curve is Arc2d) {
                writer.Write((int)3);
                Store((Arc2d)curve, writer);
            } else if ( curve is ParametricCurveSequence2 ) {
                writer.Write((int)100);
                Store(curve as ParametricCurveSequence2, writer);
            }
        }

        public static void Restore(out IParametricCurve2d curve, BinaryReader reader)
        {
            curve = null;
            int nType = reader.ReadInt32();
            if ( nType == 1 ) {
                Segment2d segment = new Segment2d();
                Restore(ref segment, reader);
                curve = segment;
            } else if ( nType == 2 ) {
                Circle2d circle = new Circle2d(Vector2d.Zero, 1.0);
                Restore(ref circle, reader);
                curve = circle;
            } else if ( nType == 3 ) {
                Arc2d arc = new Arc2d(Vector2d.Zero, 1.0, 0, 1);
                Restore(ref arc, reader);
                curve = arc;
            } else if ( nType == 100 ) {
                ParametricCurveSequence2 seq = new ParametricCurveSequence2();
                Restore(ref seq, reader);
                curve = seq;
            } else {
                throw new Exception("gSerialization.Restore: IParametricCurve2D : unknown curve type " + nType.ToString());
            }
        }


        public static void Store(PlanarSolid2d solid, BinaryWriter writer)
        {
            Store(solid.Outer, writer);
            writer.Write(solid.Holes.Count);
            for ( int i = 0; i < solid.Holes.Count; ++i )
                Store(solid.Holes[i], writer);
        }

        public static void Restore(PlanarSolid2d solid, BinaryReader reader)
        {
            IParametricCurve2d outer;
            Restore(out outer, reader);
            solid.SetOuter(outer, true); // !! currently CW/CCW is ignored!

            int hole_count = reader.ReadInt32();
            for ( int i = 0; i < hole_count; ++i ) {
                IParametricCurve2d hole;
                Restore(out hole, reader);
                solid.AddHole(hole);
            }
        }







        // version 2 adds the vertex refcounts, so that isolated vertices (and point clouds) are kept
        public static int DMesh3Version = 2;

        public static void Store(DMesh3 mesh, BinaryWriter writer)
        {
            writer.Write(DMesh3Version);

            int nComponents = (int)mesh.Components;
            writer.Write(nComponents);

            Store(mesh.VerticesBuffer, writer);
            Store(mesh.TrianglesBuffer, writer);
            Store(mesh.EdgesBuffer, writer);
            Store(mesh.EdgesRefCounts.RawRefCounts, writer);

            if ((mesh.Components & MeshComponents.VertexNormals) != 0)
                Store(mesh.NormalsBuffer, writer);
            if ((mesh.Components & MeshComponents.VertexColors) != 0)
                Store(mesh.ColorsBuffer, writer);
            if ((mesh.Components & MeshComponents.VertexUVs) != 0)
                Store(mesh.UVBuffer, writer);
            if ((mesh.Components & MeshComponents.FaceGroups) != 0)
                Store(mesh.GroupsBuffer, writer);

            Store(mesh.VerticesRefCounts.RawRefCounts, writer);
        }



        public static void Restore(DMesh3 mesh, BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != 1 && version != DMesh3Version)
                throw new Exception("gSerialization.Restore: Incorrect DMesh3Version!");

            MeshComponents components = (MeshComponents)reader.ReadInt32();

            Restore(mesh.VerticesBuffer, reader);
            Restore(mesh.TrianglesBuffer, reader);
            Restore(mesh.EdgesBuffer, reader);
            Restore(mesh.EdgesRefCounts.RawRefCounts, reader);

            if ((components & MeshComponents.VertexNormals) != 0) {
                mesh.EnableVertexNormals(Vector3f.AxisY);
                Restore(mesh.NormalsBuffer, reader);
            } else
                mesh.DiscardVertexNormals();

            if ((components & MeshComponents.VertexColors) != 0) {
                mesh.EnableVertexColors(Vector3f.One);
                Restore(mesh.ColorsBuffer, reader);
            } else
                mesh.DiscardVertexColors();

            if ((components & MeshComponents.VertexUVs) != 0) {
                mesh.EnableVertexUVs(Vector2f.Zero);
                Restore(mesh.UVBuffer, reader);
            } else
                mesh.DiscardVertexUVs();

            if ((components & MeshComponents.FaceGroups) != 0) {
                mesh.EnableTriangleGroups(0);
                Restore(mesh.GroupsBuffer, reader);
            } else
                mesh.DiscardTriangleGroups();

            DVector<short> vertexRefCounts = null;
            if (version >= 2) {
                vertexRefCounts = new DVector<short>();
                Restore(vertexRefCounts, reader);
            }

            // the stored buffers are taken over as they are: check them before the rebuild trusts them
            check_buffers(mesh, vertexRefCounts);
            // the vertices of a reused destination are not part of the stream
            DVector<short> oldVertexRefCounts = mesh.VerticesRefCounts.RawRefCounts;
            oldVertexRefCounts.resize(mesh.VerticesBuffer.Length / 3);
            for (int vid = 0; vid < oldVertexRefCounts.size; ++vid)
                oldVertexRefCounts[vid] = RefCountVector.invalid;
            mesh.RebuildFromEdgeRefcounts();
            check_rebuilt(mesh);

            // vertices without edges are not restored by RebuildFromEdgeRefcounts()
            if (vertexRefCounts != null) {
                DVector<double> v = mesh.VerticesBuffer;
                for (int vid = 0; vid < vertexRefCounts.size; ++vid) {
                    if (vertexRefCounts[vid] <= 0 || mesh.IsVertex(vid))
                        continue;
                    NewVertexInfo vinfo = new NewVertexInfo(new Vector3d(v[3 * vid], v[3 * vid + 1], v[3 * vid + 2]));
                    if (mesh.HasVertexNormals) {
                        vinfo.bHaveN = true;
                        vinfo.n = new Vector3f(mesh.NormalsBuffer[3 * vid], mesh.NormalsBuffer[3 * vid + 1], mesh.NormalsBuffer[3 * vid + 2]);
                    }
                    if (mesh.HasVertexColors) {
                        vinfo.bHaveC = true;
                        vinfo.c = new Vector3f(mesh.ColorsBuffer[3 * vid], mesh.ColorsBuffer[3 * vid + 1], mesh.ColorsBuffer[3 * vid + 2]);
                    }
                    if (mesh.HasVertexUVs) {
                        vinfo.bHaveUV = true;
                        vinfo.uv = new Vector2f(mesh.UVBuffer[2 * vid], mesh.UVBuffer[2 * vid + 1]);
                    }
                    mesh.InsertVertex(vid, ref vinfo, true);
                }
                mesh.EndUnsafeVerticesInsert();
            }
        }


        static void check(bool ok, string what)
        {
            if (ok == false)
                throw new Exception("gSerialization.Restore: invalid mesh data (" + what + ")");
        }

        // sizes of the stored buffers, and every live edge's vertices and triangles
        static void check_buffers(DMesh3 mesh, DVector<short> vertexRefCounts)
        {
            DVector<double> vertices = mesh.VerticesBuffer;
            DVector<int> triangles = mesh.TrianglesBuffer, edges = mesh.EdgesBuffer;
            DVector<short> edgeRefs = mesh.EdgesRefCounts.RawRefCounts;
            check(vertices.size % 3 == 0 && triangles.size % 3 == 0 && edges.size % 4 == 0, "buffer sizes");
            int nV = vertices.size / 3, nT = triangles.size / 3, nE = edges.size / 4;
            check(edgeRefs.size == nE, "edge reference counts");
            if (mesh.HasVertexNormals) check(mesh.NormalsBuffer.size == 3 * nV, "normals");
            if (mesh.HasVertexColors) check(mesh.ColorsBuffer.size == 3 * nV, "colors");
            if (mesh.HasVertexUVs) check(mesh.UVBuffer.size == 2 * nV, "uvs");
            if (mesh.HasTriangleGroups) check(mesh.GroupsBuffer.size == nT, "groups");
            if (vertexRefCounts != null) check(vertexRefCounts.size <= nV, "vertex reference counts");
            for (int eid = 0; eid < nE; ++eid) {
                short r = edgeRefs[eid];
                if (r <= 0) {
                    edgeRefs[eid] = RefCountVector.invalid;     // a free slot (the rebuild skips it too)
                    continue;
                }
                check(r == 1, "edge reference count");
                int va = edges[4 * eid], vb = edges[4 * eid + 1], t0 = edges[4 * eid + 2], t1 = edges[4 * eid + 3];
                check(va >= 0 && va < nV && vb >= 0 && vb < nV && va < vb, "edge vertices");
                check(t0 >= 0 && t0 < nT && (t1 == DMesh3.InvalidID || (t1 >= 0 && t1 < nT && t1 != t0)), "edge triangles");
                Index3i tri0 = new Index3i(triangles[3 * t0], triangles[3 * t0 + 1], triangles[3 * t0 + 2]);
                check(IndexUtil.find_edge_index_in_tri(va, vb, ref tri0) != DMesh3.InvalidID, "edge not in its triangle");
                if (t1 != DMesh3.InvalidID) {
                    Index3i tri1 = new Index3i(triangles[3 * t1], triangles[3 * t1 + 1], triangles[3 * t1 + 2]);
                    check(IndexUtil.find_edge_index_in_tri(va, vb, ref tri1) != DMesh3.InvalidID, "edge not in its triangle");
                }
            }
        }

        // after the rebuild: every triangle has three distinct vertices and the three edges that reference it, and
        // each vertex pair has one edge
        static void check_rebuilt(DMesh3 mesh)
        {
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(tid);
                check(mesh.IsVertex(tv.a) && mesh.IsVertex(tv.b) && mesh.IsVertex(tv.c), "triangle vertices");
                check(tv.a != tv.b && tv.b != tv.c && tv.c != tv.a, "repeated triangle vertex");
                Index3i te = mesh.GetTriEdges(tid);
                for (int j = 0; j < 3; ++j) {
                    int eid = te[j];
                    check(mesh.IsEdge(eid), "triangle edges");
                    Index2i ev = mesh.GetEdgeV(eid), et = mesh.GetEdgeT(eid);
                    check(IndexUtil.same_pair_unordered(tv[j], tv[(j + 1) % 3], ev.a, ev.b) && (et.a == tid || et.b == tid), "triangle edges");
                }
            }
            // (per vertex, its edges' other ends marked with the vertex id: linear, also around high-valence vertices)
            int[] mark = new int[mesh.MaxVertexID];
            for (int k = 0; k < mark.Length; ++k) mark[k] = -1;
            foreach (int vid in mesh.VertexIndices()) {
                foreach (int eid in mesh.VtxEdgesItr(vid)) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    int other = (ev.a == vid) ? ev.b : ev.a;
                    check(mark[other] != vid, "duplicate edge");
                    mark[other] = vid;
                }
            }
        }


        // [TODO] these could be a lot faster if DVector had a block-iterator...
        public static void Store(DVector<double> vec, BinaryWriter writer)
        {
            byte[] buffer = new byte[vec.BlockCount * sizeof(double)];
            int N = vec.Length;
            writer.Write(N);
            foreach ( DVector<double>.DBlock block in vec.BlockIterator() ) {
                Buffer.BlockCopy(block.data, 0, buffer, 0, block.usedCount*sizeof(double));
                writer.Write(buffer, 0, block.usedCount*sizeof(double));
            }
        }
        public static void Restore(DVector<double> vec, BinaryReader reader)
        {
            int N = read_count(reader, sizeof(double));
            byte[] bytes = read_bytes(reader, N * sizeof(double));
            double[] buffer = new double[N];
            Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
            vec.Initialize(buffer);
        }

        public static void Store(DVector<float> vec, BinaryWriter writer)
        {
            byte[] buffer = new byte[vec.BlockCount * sizeof(float)];
            int N = vec.Length;
            writer.Write(N);
            foreach ( DVector<float>.DBlock block in vec.BlockIterator() ) {
                Buffer.BlockCopy(block.data, 0, buffer, 0, block.usedCount*sizeof(float));
                writer.Write(buffer, 0, block.usedCount*sizeof(float));
            }
        }
        public static void Restore(DVector<float> vec, BinaryReader reader)
        {
            int N = read_count(reader, sizeof(float));
            byte[] bytes = read_bytes(reader, N * sizeof(float));
            float[] buffer = new float[N];
            Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
            vec.Initialize(buffer);
        }

        public static void Store(DVector<int> vec, BinaryWriter writer)
        {
            byte[] buffer = new byte[vec.BlockCount * sizeof(int)];
            int N = vec.Length;
            writer.Write(N);
            foreach ( DVector<int>.DBlock block in vec.BlockIterator() ) {
                Buffer.BlockCopy(block.data, 0, buffer, 0, block.usedCount*sizeof(int));
                writer.Write(buffer, 0, block.usedCount*sizeof(int));
            }
        }
        public static void Restore(DVector<int> vec, BinaryReader reader)
        {
            int N = read_count(reader, sizeof(int));
            byte[] bytes = read_bytes(reader, N * sizeof(int));
            int[] buffer = new int[N];
            Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
            vec.Initialize(buffer);
        }

        public static void Store(DVector<short> vec, BinaryWriter writer)
        {
            byte[] buffer = new byte[vec.BlockCount * sizeof(short)];
            int N = vec.Length;
            writer.Write(N);
            foreach ( DVector<short>.DBlock block in vec.BlockIterator() ) {
                Buffer.BlockCopy(block.data, 0, buffer, 0, block.usedCount*sizeof(short));
                writer.Write(buffer, 0, block.usedCount*sizeof(short));
            }
        }
        public static void Restore(DVector<short> vec, BinaryReader reader)
        {
            int N = read_count(reader, sizeof(short));
            byte[] bytes = read_bytes(reader, N * sizeof(short));
            short[] buffer = new short[N];
            Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
            vec.Initialize(buffer);
        }



        // An element count stored in the stream: not negative, and (for a seekable stream) not more elements of
        // elementSize bytes than the stream has left, so that a damaged or hostile file cannot make us allocate
        // gigabytes before the read fails.
        static int read_count(BinaryReader reader, int elementSize)
        {
            int N = reader.ReadInt32();
            if (N < 0)
                throw new Exception("gSerialization.Restore: negative element count " + N);
            long bytes = (long)N * elementSize;
            if (bytes > int.MaxValue)
                throw new Exception("gSerialization.Restore: element count " + N + " too large");
            Stream stream = reader.BaseStream;
            if (stream.CanSeek && bytes > stream.Length - stream.Position)
                throw new EndOfStreamException("gSerialization.Restore: element count " + N + " beyond the end of the stream");
            return N;
        }

        static byte[] read_bytes(BinaryReader reader, int count)
        {
            byte[] bytes;
            if (reader.BaseStream.CanSeek || count <= ReadChunk) {
                bytes = reader.ReadBytes(count);
            } else {
                // (a stream without a length: read in chunks, so that memory follows the data that is there, not the
                // count the stream claims)
                var stream = new MemoryStream();
                while (stream.Length < count) {
                    byte[] chunk = reader.ReadBytes((int)Math.Min(ReadChunk, count - stream.Length));
                    if (chunk.Length == 0)
                        break;
                    stream.Write(chunk, 0, chunk.Length);
                }
                bytes = stream.ToArray();
            }
            if (bytes.Length != count)
                throw new EndOfStreamException("gSerialization.Restore: stream ends inside an array");
            return bytes;
        }
        const int ReadChunk = 1 << 20;



        public static void Store(string s, BinaryWriter writer)
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(s);
            writer.Write(buffer.Length);
            writer.Write(buffer);
        }
        public static void Restore(ref string s, BinaryReader reader)
        {
            int N = read_count(reader, 1);
            byte[] bytes = read_bytes(reader, N);
            s = System.Text.Encoding.UTF8.GetString(bytes);
        }


        public static void Store(string[] s, BinaryWriter writer)
        {
            writer.Write(s.Length);
            for (int i = 0; i < s.Length; ++i)
                Store(s[i], writer);
        }
        public static void Restore(ref string[] s, BinaryReader reader)
        {
            int N = read_count(reader, sizeof(int));
            s = new string[N];
            for (int i = 0; i < N; ++i)
                Restore(ref s[i], reader);
        }
    }



    /// <summary>
    /// Utility class that is intended to support things like writing and reading
    /// test cases, etc. You can write out a test case in a single line, eg
    ///    SimpleStore.Store(path, new object[] { TestMesh, VertexList, PlaneNormal, ... })
    /// The object list will be binned into the relevant sublists automatically.
    /// Then you can load this data via:
    ///    SimpleStore s = SimpleStore.Restore(path)
    /// </summary>
    public class SimpleStore
    {
        // only ever append to this list!
        public List<DMesh3> Meshes = new List<DMesh3>();
        public List<Vector3d> Points = new List<Vector3d>();
        public List<string> Strings = new List<string>();
        public List<List<int>> IntLists = new List<List<int>>();

        public SimpleStore()
        {
        }

        public SimpleStore(object[] objs)
        {
            Add(objs);
        }

        public void Add(object[] objs)
        {
            foreach (object o in objs) {
                if (o is DMesh3)
                    Meshes.Add(o as DMesh3);
                else if (o is string)
                    Strings.Add(o as String);
                else if (o is List<int>)
                    IntLists.Add(o as List<int>);
                else if (o is IEnumerable<int>)
                    IntLists.Add(new List<int>(o as IEnumerable<int>));
                else if (o is Vector3d)
                    Points.Add((Vector3d)o);
                else
                    throw new Exception("SimpleStore: unknown type " + o.GetType().ToString());
            }
        }


        public static void Store(string sPath, object[] objs)
        {
            SimpleStore s = new SimpleStore(objs);
            Store(sPath, s);
        }

        public static void Store(string sPath, SimpleStore s)
        {
            using (FileStream stream = new FileStream(sPath, FileMode.Create)) {
                using (BinaryWriter w = new BinaryWriter(stream)) {
                    w.Write(s.Meshes.Count);
                    for (int k = 0; k < s.Meshes.Count; ++k)
                        gSerialization.Store(s.Meshes[k], w);
                    w.Write(s.Points.Count);
                    for (int k = 0; k < s.Points.Count; ++k)
                        gSerialization.Store(s.Points[k], w);
                    w.Write(s.Strings.Count);
                    for (int k = 0; k < s.Strings.Count; ++k)
                        gSerialization.Store(s.Strings[k], w);

                    w.Write(s.IntLists.Count);
                    for (int k = 0; k < s.IntLists.Count; ++k)
                        gSerialization.Store(s.IntLists[k], w);
                }
            }
        }


        public static SimpleStore Restore(string sPath)
        {
            SimpleStore s = new SimpleStore();
            using (FileStream stream = new FileStream(sPath, FileMode.Open)) {
                using (BinaryReader r = new BinaryReader(stream)) {
                    int nMeshes = r.ReadInt32();
                    for ( int k = 0; k < nMeshes; ++k ) {
                        DMesh3 m = new DMesh3(); gSerialization.Restore(m, r); s.Meshes.Add(m);
                    }
                    int nPoints = r.ReadInt32();
                    for (int k = 0; k < nPoints; ++k) {
                        Vector3d v = Vector3d.Zero; gSerialization.Restore(ref v, r); s.Points.Add(v);
                    }
                    int nStrings = r.ReadInt32();
                    for (int k = 0; k < nStrings; ++k) {
                        string str = null; gSerialization.Restore(ref str, r); s.Strings.Add(str);
                    }
                    int nIntLists = r.ReadInt32();
                    for (int k = 0; k < nIntLists; ++k) {
                        List<int> l = new List<int>(); gSerialization.Restore(l, r); s.IntLists.Add(l);
                    }
                }
            }
            return s;
        }

    }


}
