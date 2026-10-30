using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;
using Xunit.Sdk;

namespace geometry3Sharp.Tests
{
    public class SerializationRoundTripTests
    {
        static readonly float NegativeZeroF = BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)0x80000000)), 0);
        static readonly double NegativeZeroD = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));

        static void Require(bool condition, string context)
        {
            if (!condition) throw new XunitException(context);
        }

        static void Equal<T>(T expected, T actual, string context)
        {
            Require(EqualityComparer<T>.Default.Equals(expected, actual),
                context + "; expected=" + expected + "; actual=" + actual);
        }

        static void Bits(double expected, double actual, string context)
        {
            Require(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
                context + "; expected=" + expected.ToString("R", CultureInfo.InvariantCulture)
                + "; actual=" + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Bits(float expected, float actual, string context)
        {
            Require(BitConverter.ToInt32(BitConverter.GetBytes(expected), 0) == BitConverter.ToInt32(BitConverter.GetBytes(actual), 0),
                context + "; expected=" + expected.ToString("R", CultureInfo.InvariantCulture)
                + "; actual=" + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Same(Vector2d a, Vector2d b, string context)
        {
            Bits(a.x, b.x, context + ".x"); Bits(a.y, b.y, context + ".y");
        }
        static void Same(Vector3d a, Vector3d b, string context)
        {
            Bits(a.x, b.x, context + ".x"); Bits(a.y, b.y, context + ".y"); Bits(a.z, b.z, context + ".z");
        }
        static void Same(Vector2f a, Vector2f b, string context)
        {
            Bits(a.x, b.x, context + ".x"); Bits(a.y, b.y, context + ".y");
        }
        static void Same(Vector3f a, Vector3f b, string context)
        {
            Bits(a.x, b.x, context + ".x"); Bits(a.y, b.y, context + ".y"); Bits(a.z, b.z, context + ".z");
        }
        static void Same(Quaternionf a, Quaternionf b, string context)
        {
            Bits(a.x, b.x, context + ".x"); Bits(a.y, b.y, context + ".y");
            Bits(a.z, b.z, context + ".z"); Bits(a.w, b.w, context + ".w");
        }

        static byte[] Bytes(Action<BinaryWriter> store)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                store(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        static T Read<T>(byte[] bytes, Func<BinaryReader, T> restore)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) {
                T value = restore(reader);
                Equal(stream.Length, stream.Position, "exact stream consumption");
                return value;
            }
        }

        // The expected object is compared directly; equality never comes from comparing serialized bytes.
        static T RoundTrip<T>(T source, Action<T, BinaryWriter> store, Func<BinaryReader, T> restore,
            Action<T, T, string> compare, string context)
        {
            T first = Read(Bytes(w => store(source, w)), restore);
            compare(source, first, context + "; restore=1");
            T second = Read(Bytes(w => store(first, w)), restore);
            compare(source, second, context + "; restore=2");
            byte[] pair = Bytes(w => { store(source, w); store(first, w); });
            using (var stream = new MemoryStream(pair, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) {
                compare(source, restore(reader), context + "; pair=0");
                Equal((long)Bytes(w => store(source, w)).Length, stream.Position, context + "; first boundary");
                compare(source, restore(reader), context + "; pair=1");
                Equal(stream.Length, stream.Position, context + "; final boundary");
            }
            return first;
        }

        static DMesh3 ReadMesh(BinaryReader reader)
        {
            var mesh = new DMesh3();
            gSerialization.Restore(mesh, reader);
            return mesh;
        }

        static MeshComponents Components(int mask)
        {
            MeshComponents flags = (MeshComponents)0;
            if ((mask & 1) != 0) flags |= MeshComponents.VertexNormals;
            if ((mask & 2) != 0) flags |= MeshComponents.VertexColors;
            if ((mask & 4) != 0) flags |= MeshComponents.VertexUVs;
            if ((mask & 8) != 0) flags |= MeshComponents.FaceGroups;
            return flags;
        }

        static int Vertex(DMesh3 mesh, double x, double y, double z)
        {
            int id = mesh.AppendVertex(new Vector3d(x, y, z));
            Require(id >= 0, "fixture AppendVertex");
            return id;
        }

        static int Triangle(DMesh3 mesh, int a, int b, int c)
        {
            int id = mesh.AppendTriangle(a, b, c);
            Require(id >= 0, "fixture AppendTriangle(" + a + "," + b + "," + c + ")");
            return id;
        }

        static void Attributes(DMesh3 mesh, int salt = 0)
        {
            foreach (int id in mesh.VertexIndices().OrderBy(v => v)) {
                float f = (id + salt + 1) / 16.0f;
                if (mesh.HasVertexNormals) mesh.SetVertexNormal(id, new Vector3f(f, -f * 2, f * 4));
                if (mesh.HasVertexColors) mesh.SetVertexColor(id, new Vector3f(f / 2, f / 4, -f / 8));
                if (mesh.HasVertexUVs) mesh.SetVertexUV(id, new Vector2f(-f * 8, f * 16));
            }
            if (mesh.HasTriangleGroups)
                foreach (int id in mesh.TriangleIndices().OrderBy(t => t)) mesh.SetTriangleGroup(id, 7 + 3 * id + salt);
        }

        static DMesh3 Fixture(string name, int mask)
        {
            var mesh = new DMesh3(Components(mask));
            if (name == "empty") return mesh;
            if (name == "point-cloud") {
                Vertex(mesh, -1.25, 2.5, 3.75); Vertex(mesh, 4.5, -5.25, 6.125); Vertex(mesh, 7.75, 8.5, -9.25);
            } else if (name == "closed") {
                int a = Vertex(mesh, 0, 0, 0), b = Vertex(mesh, 2, 0, 0);
                int c = Vertex(mesh, 0, 4, 0), d = Vertex(mesh, 0, 0, 8);
                Triangle(mesh, a, c, b); Triangle(mesh, a, b, d);
                Triangle(mesh, b, c, d); Triangle(mesh, c, a, d);
                Require(mesh.IsClosed(), "closed tetrahedron fixture");
            } else {
                int count = (name == "disconnected" || name == "sparse") ? 3 : 1;
                for (int patch = 0; patch < count; ++patch) {
                    int a = Vertex(mesh, patch * 8, 0.25, 0.5), b = Vertex(mesh, patch * 8 + 2, 0.25, 0.5);
                    int c = Vertex(mesh, patch * 8, 4.25, 0.5);
                    Triangle(mesh, a, b, c);
                }
                if (name == "sparse") {
                    Equal(MeshResult.Ok, mesh.RemoveTriangle(1, true, true), "remove middle island");
                    Require(!mesh.IsVertex(3) && !mesh.IsTriangle(1) && !mesh.IsEdge(3), "sparse fixture gaps");
                    Require(mesh.MaxVertexID > mesh.VertexCount && mesh.MaxTriangleID > mesh.TriangleCount
                        && mesh.MaxEdgeID > mesh.EdgeCount, "all three ID families are sparse");
                }
                if (name == "isolated" || name == "sparse") Vertex(mesh, -2.5, 6.25, 9.5);
                Require(!mesh.IsClosed(), name + "; intentionally open");
            }
            Attributes(mesh);
            Validate(mesh, name + "; source");
            return mesh;
        }

        static string Dump(DMesh3 mesh)
        {
            var text = new StringBuilder("; components=" + mesh.Components);
            foreach (int id in mesh.VertexIndices().OrderBy(v => v)) {
                Vector3d p = mesh.GetVertex(id);
                text.AppendFormat(CultureInfo.InvariantCulture, "; v{0}=({1:R},{2:R},{3:R})", id, p.x, p.y, p.z);
                if (mesh.HasVertexNormals) {
                    Vector3f n = mesh.GetVertexNormal(id);
                    text.AppendFormat(CultureInfo.InvariantCulture, "; n=({0:R},{1:R},{2:R})", n.x, n.y, n.z);
                }
                if (mesh.HasVertexColors) {
                    Vector3f c = mesh.GetVertexColor(id);
                    text.AppendFormat(CultureInfo.InvariantCulture, "; c=({0:R},{1:R},{2:R})", c.x, c.y, c.z);
                }
                if (mesh.HasVertexUVs) {
                    Vector2f uv = mesh.GetVertexUV(id);
                    text.AppendFormat(CultureInfo.InvariantCulture, "; uv=({0:R},{1:R})", uv.x, uv.y);
                }
            }
            foreach (int id in mesh.TriangleIndices().OrderBy(t => t)) {
                Index3i t = mesh.GetTriangle(id);
                text.AppendFormat(CultureInfo.InvariantCulture, "; t{0}=({1},{2},{3})", id, t.a, t.b, t.c);
                if (mesh.HasTriangleGroups) text.Append("; group=" + mesh.GetTriangleGroup(id));
            }
            foreach (int id in mesh.EdgeIndices().OrderBy(e => e)) text.Append("; e" + id + "=" + mesh.GetEdge(id));
            return text.ToString();
        }

        static void Validate(DMesh3 mesh, string context)
        {
            Require(mesh.CheckValidity(false, FailMode.ReturnOnly), context + "; CheckValidity");
            var incidence = new Dictionary<Tuple<int, int>, List<int>>();
            foreach (int tid in mesh.TriangleIndices().OrderBy(t => t)) {
                Index3i t = mesh.GetTriangle(tid);
                Require(t.a != t.b && t.b != t.c && t.c != t.a, context + "; distinct triangle vertices=" + tid);
                for (int j = 0; j < 3; ++j) {
                    Require(mesh.IsVertex(t[j]), context + "; live triangle vertex");
                    var pair = Tuple.Create(Math.Min(t[j], t[(j + 1) % 3]), Math.Max(t[j], t[(j + 1) % 3]));
                    if (!incidence.TryGetValue(pair, out List<int> triangles)) {
                        triangles = new List<int>(); incidence.Add(pair, triangles);
                    }
                    triangles.Add(tid);
                }
            }
            Equal(incidence.Count, mesh.EdgeCount, context + "; independent edge count");
            var pairs = new HashSet<Tuple<int, int>>();
            foreach (int eid in mesh.EdgeIndices().OrderBy(e => e)) {
                Index2i v = mesh.GetEdgeV(eid), t = mesh.GetEdgeT(eid);
                var pair = Tuple.Create(v.a, v.b);
                Require(v.a < v.b && pairs.Add(pair), context + "; unique sorted edge=" + eid);
                Require(incidence.TryGetValue(pair, out List<int> triangles), context + "; edge in triangle scan=" + eid);
                int[] actual = t.b == DMesh3.InvalidID ? new[] { t.a } : new[] { t.a, t.b }.OrderBy(x => x).ToArray();
                Require(triangles.OrderBy(x => x).SequenceEqual(actual), context + "; incidence=" + eid);
                Equal(triangles.Count == 1, mesh.IsBoundaryEdge(eid), context + "; boundary=" + eid);
            }
            foreach (int vid in mesh.VertexIndices().OrderBy(v => v)) {
                Require(mesh.GetVertex(vid).IsFinite, context + "; finite vertex=" + vid);
                int[] edgeScan = mesh.EdgeIndices().Where(e => mesh.GetEdgeV(e).a == vid || mesh.GetEdgeV(e).b == vid).OrderBy(e => e).ToArray();
                Require(edgeScan.SequenceEqual(mesh.VtxEdgesItr(vid).OrderBy(e => e)), context + "; vertex edge adjacency=" + vid);
                int[] triScan = mesh.TriangleIndices().Where(t => {
                    Index3i v = mesh.GetTriangle(t); return v.a == vid || v.b == vid || v.c == vid;
                }).OrderBy(t => t).ToArray();
                Require(triScan.SequenceEqual(mesh.VtxTrianglesItr(vid).OrderBy(t => t)), context + "; vertex triangle adjacency=" + vid);
            }
        }

        static void SameMesh(DMesh3 a, DMesh3 b, string context)
        {
            Equal(a.Components, b.Components, context + "; components");
            Equal(a.HasVertexNormals, b.HasVertexNormals, context + "; normals presence");
            Equal(a.HasVertexColors, b.HasVertexColors, context + "; colors presence");
            Equal(a.HasVertexUVs, b.HasVertexUVs, context + "; UV presence");
            Equal(a.HasTriangleGroups, b.HasTriangleGroups, context + "; groups presence");
            Require(a.VertexIndices().OrderBy(v => v).SequenceEqual(b.VertexIndices().OrderBy(v => v)), context + "; live vertex IDs");
            Require(a.TriangleIndices().OrderBy(t => t).SequenceEqual(b.TriangleIndices().OrderBy(t => t)), context + "; live triangle IDs");
            Require(a.EdgeIndices().OrderBy(e => e).SequenceEqual(b.EdgeIndices().OrderBy(e => e)), context + "; live edge IDs");
            Equal(a.IsClosed(), b.IsClosed(), context + "; closedness");
            foreach (int id in a.VertexIndices().OrderBy(v => v)) {
                Same(a.GetVertex(id), b.GetVertex(id), context + "; vertex=" + id);
                if (a.HasVertexNormals) Same(a.GetVertexNormal(id), b.GetVertexNormal(id), context + "; normal=" + id);
                if (a.HasVertexColors) Same(a.GetVertexColor(id), b.GetVertexColor(id), context + "; color=" + id);
                if (a.HasVertexUVs) Same(a.GetVertexUV(id), b.GetVertexUV(id), context + "; UV=" + id);
            }
            foreach (int id in a.TriangleIndices().OrderBy(t => t)) {
                Equal(a.GetTriangle(id), b.GetTriangle(id), context + "; oriented triangle=" + id);
                Equal(a.GetTriEdges(id), b.GetTriEdges(id), context + "; triangle edges=" + id);
                if (a.HasTriangleGroups) Equal(a.GetTriangleGroup(id), b.GetTriangleGroup(id), context + "; group=" + id);
            }
            foreach (int id in a.EdgeIndices().OrderBy(e => e)) Equal(a.GetEdge(id), b.GetEdge(id), context + "; edge=" + id);
            Validate(b, context + "; restored");
        }

        static void ExerciseRebuiltBookkeeping(DMesh3 mesh, string context)
        {
            int[] before = mesh.VertexIndices().OrderBy(v => v).ToArray();
            int a = Vertex(mesh, 32.25, 33.5, 34.75), b = Vertex(mesh, 36.25, 33.5, 34.75);
            int c = Vertex(mesh, 32.25, 37.5, 34.75);
            int tid = Triangle(mesh, a, b, c);
            Validate(mesh, context + "; appended island");
            Equal(MeshResult.Ok, mesh.RemoveTriangle(tid, true, true), context + "; remove appended island");
            Require(before.SequenceEqual(mesh.VertexIndices().OrderBy(v => v)), context + "; original live vertices after remove");
            Validate(mesh, context + "; removed island");
            if (mesh.EdgeCount != 0) {
                int eid = mesh.EdgeIndices().OrderBy(e => e).First();
                Index2i ev = mesh.GetEdgeV(eid);
                Vector3d midpoint = (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b)) * 0.5;
                int vertices = mesh.VertexCount, triangles = mesh.TriangleCount;
                bool boundary = mesh.IsBoundaryEdge(eid);
                Equal(MeshResult.Ok, mesh.SplitEdge(eid, out DMesh3.EdgeSplitInfo split), context + "; split edge=" + eid);
                Equal(vertices + 1, mesh.VertexCount, context + "; split vertex count");
                Equal(triangles + (boundary ? 1 : 2), mesh.TriangleCount, context + "; split triangle count");
                Same(midpoint, mesh.GetVertex(split.vNew), context + "; split midpoint");
                Validate(mesh, context + "; split result");
            }
        }

        public static IEnumerable<object[]> MeshCases()
        {
            foreach (string name in new[] { "empty", "point-cloud", "open", "closed", "disconnected", "isolated", "sparse" })
                foreach (int mask in new[] { 0, 15 }) yield return new object[] { name, mask };
            for (int mask = 1; mask < 15; ++mask) yield return new object[] { "sparse", mask };
        }

        [Theory]
        [MemberData(nameof(MeshCases))]
        public void MeshMatrix_PreservesLiveTopologyAndEveryAttribute(string name, int mask)
        {
            DMesh3 source = Fixture(name, mask);
            string context = "fixture=" + name + "; mask=" + mask + Dump(source);
            DMesh3 restored = RoundTrip(source, gSerialization.Store, ReadMesh, SameMesh, context);
            ExerciseRebuiltBookkeeping(restored, context);
        }

        [Fact]
        public void MeshRestore_ReusedDestination_RichSparseRichMatchesFresh()
        {
            DMesh3 rich = Fixture("closed", 15), sparse = Fixture("sparse", 0);
            var reused = new DMesh3();
            foreach (DMesh3 source in new[] { rich, sparse, rich }) {
                byte[] data = Bytes(w => gSerialization.Store(source, w));
                DMesh3 fresh = Read(data, ReadMesh);
                Read(data, r => { gSerialization.Restore(reused, r); return reused; });
                SameMesh(source, reused, "reuse" + Dump(source));
                SameMesh(fresh, reused, "fresh vs reused" + Dump(source));
            }
        }

        [Fact]
        public void MeshRestore_ReusedDestination_SingleTriangleThenDeletedMatchesFresh()
        {
            DMesh3 source = Fixture("open", 0);
            var reused = new DMesh3((MeshComponents)0);
            byte[] occupied = Bytes(w => gSerialization.Store(source, w));
            Read(occupied, r => { gSerialization.Restore(reused, r); return reused; });
            SameMesh(source, reused, "minimal reuse; first restore; one triangle");

            Equal(MeshResult.Ok, source.RemoveTriangle(source.TriangleIndices().Single(), true, true),
                "minimal reuse; remove the only triangle and its isolated vertices");
            Equal(0, source.VertexCount, "minimal reuse; empty source vertices");
            Equal(0, source.TriangleCount, "minimal reuse; empty source triangles");
            Equal(0, source.EdgeCount, "minimal reuse; empty source edges");
            Validate(source, "minimal reuse; empty source after deletion");
            byte[] deleted = Bytes(w => gSerialization.Store(source, w));
            DMesh3 fresh = Read(deleted, ReadMesh);
            SameMesh(source, fresh, "minimal reuse; deleted triangle restores into fresh destination");

            // Deletion retains buffer slots; none of them may remain live in the reused destination.
            Read(deleted, r => { gSerialization.Restore(reused, r); return reused; });
            SameMesh(source, reused, "minimal reuse; second restore; deleted triangle");
            SameMesh(fresh, reused, "minimal reuse; fresh vs reused after deletion");
        }

        [Fact]
        public void MeshRoundTrip_AllSimilarities_StoredBitsThenInverseGeometry()
        {
            foreach (string name in new[] { "closed", "sparse", "point-cloud" }) {
                DMesh3 source = Fixture(name, 15);
                foreach (Similarity similarity in Similarity.All()) {
                    var transformed = new DMesh3(source);
                    foreach (int vid in transformed.VertexIndices().OrderBy(v => v)) {
                        transformed.SetVertex(vid, similarity.Apply(source.GetVertex(vid)));
                        transformed.SetVertexNormal(vid, (Vector3f)similarity.ApplyDirection(source.GetVertexNormal(vid)));
                    }
                    string context = "fixture=" + name + "; similarity=" + similarity.Name + Dump(transformed);
                    DMesh3 restored = RoundTrip(transformed, gSerialization.Store, ReadMesh, SameMesh, context);
                    foreach (int vid in source.VertexIndices().OrderBy(v => v)) {
                        Vector3d expected = source.GetVertex(vid), inverse = similarity.Inverse(restored.GetVertex(vid));
                        if (similarity.IsExact) Same(expected, inverse, context + "; inverse vertex=" + vid);
                        else Require(expected.Distance(inverse) <= 16 * similarity.Resolution(16), context + "; inverse vertex=" + vid);
                    }
                }
            }
        }

        [Theory]
        [InlineData(17)] [InlineData(101)] [InlineData(307)] [InlineData(809)]
        [InlineData(1237)] [InlineData(4093)] [InlineData(7919)] [InlineData(104729)]
        public void SeededAttributedMeshes_RoundTrip(int seed)
        {
            var random = new Random(seed);
            for (int sample = 0; sample < 32; ++sample) {
                int mask = random.Next(16), patches = random.Next(3, 13);
                var mesh = new DMesh3(Components(mask));
                var log = new StringBuilder("seed=" + seed + "; sample=" + sample + "; patches=" + patches + "; mask=" + mask);
                for (int patch = 0; patch < patches; ++patch) {
                    double x = patch * 8 + random.Next(-8, 9) / 16.0;
                    double y = random.Next(-16, 17) / 8.0, z = random.Next(-32, 33) / 16.0;
                    double width = random.Next(1, 9) / 4.0, height = random.Next(1, 9) / 4.0;
                    int a = Vertex(mesh, x, y, z), b = Vertex(mesh, x + width, y, z);
                    int c = Vertex(mesh, x + width, y + height, z), d = Vertex(mesh, x, y + height, z);
                    Triangle(mesh, a, b, c); Triangle(mesh, a, c, d);
                }
                // Sorted selections are recorded, and each edit retains manifold patches.
                int[] tids = mesh.TriangleIndices().OrderBy(t => t).ToArray();
                int remove = tids[random.Next(tids.Length)];
                Equal(MeshResult.Ok, mesh.RemoveTriangle(remove, true, true), log + "; RemoveTriangle=" + remove);
                log.Append("; RemoveTriangle(" + remove + ",true,true)");
                int temporary = Vertex(mesh, -16.25, 17.5, -18.75);
                Vertex(mesh, -19.5, 20.25, 21.75);
                Equal(MeshResult.Ok, mesh.RemoveVertex(temporary, false, true), log + "; RemoveVertex=" + temporary);
                log.Append("; RemoveVertex(" + temporary + ",false,true)");
                Attributes(mesh, sample);
                Require(mesh.TriangleCount <= 128, log + "; triangle budget");
                string context = log + Dump(mesh);
                Validate(mesh, context + "; source");
                DMesh3 restored = RoundTrip(mesh, gSerialization.Store, ReadMesh, SameMesh, context);
                ExerciseRebuiltBookkeeping(restored, context);
            }
        }

        static byte[] Version1(DMesh3 mesh)
        {
            // Version 1 has no vertex-refcount suffix; never change the global version switch.
            return Bytes(w => {
                w.Write(1); w.Write((int)mesh.Components);
                gSerialization.Store(mesh.VerticesBuffer, w); gSerialization.Store(mesh.TrianglesBuffer, w);
                gSerialization.Store(mesh.EdgesBuffer, w); gSerialization.Store(mesh.EdgesRefCounts.RawRefCounts, w);
                if (mesh.HasVertexNormals) gSerialization.Store(mesh.NormalsBuffer, w);
                if (mesh.HasVertexColors) gSerialization.Store(mesh.ColorsBuffer, w);
                if (mesh.HasVertexUVs) gSerialization.Store(mesh.UVBuffer, w);
                if (mesh.HasTriangleGroups) gSerialization.Store(mesh.GroupsBuffer, w);
            });
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
        [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
        [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
        public void ExplicitVersion1_PreservesOnlyRepresentableSubset(int mask)
        {
            DMesh3 source = Fixture("sparse", mask);
            DMesh3 representable = new DMesh3(source);
            foreach (int id in representable.VertexIndices().OrderBy(v => v).ToArray())
                if (representable.GetVtxEdgeCount(id) == 0)
                    Equal(MeshResult.Ok, representable.RemoveVertex(id, false, true), "v1 remove unrepresentable isolated vertex=" + id);
            byte[] bytes = Version1(source);
            DMesh3 restored = Read(bytes, ReadMesh);
            SameMesh(representable, restored, "v1; mask=" + mask + Dump(source));
            RoundTrip(restored, gSerialization.Store, ReadMesh, SameMesh, "v1 migrated to current format");
            ExerciseRebuiltBookkeeping(restored, "v1 bookkeeping");
            DMesh3 cloud = Read(Version1(Fixture("point-cloud", mask)), ReadMesh);
            Require(cloud.VertexCount == 0 && cloud.TriangleCount == 0 && cloud.EdgeCount == 0, "v1 point cloud is not representable");
            Equal(Components(mask), cloud.Components, "v1 cloud component flags");
            Validate(cloud, "v1 cloud");
            SameMesh(Fixture("empty", mask), Read(Version1(Fixture("empty", mask)), ReadMesh), "v1 empty");
        }

        static void SameVector<T>(DVector<T> a, DVector<T> b, Action<T, T, string> compare, string context)
        {
            Equal(a.Length, b.Length, context + "; length");
            for (int i = 0; i < a.Length; ++i) compare(a[i], b[i], context + "; index=" + i);
        }

        static void VectorMatrix<T>(Func<int, T> value, Action<DVector<T>, BinaryWriter> store,
            Action<DVector<T>, BinaryReader> restore, Action<T, T, string> compare, string type)
        {
            int block = new DVector<T>().BlockCount;
            foreach (int length in new[] { 0, 1, block - 1, block, block + 1, 3 * block, 3 * block + 7 }) {
                var source = new DVector<T>();
                for (int i = 0; i < length + block; ++i) source.Add(value(i));
                source.resize(length);
                Func<BinaryReader, DVector<T>> read = r => { var v = new DVector<T>(); restore(v, r); return v; };
                RoundTrip(source, store, read, (a, b, c) => SameVector(a, b, compare, c), type + "; shortened length=" + length);
            }
            var aValue = new DVector<T>(Enumerable.Range(0, block + 1).Select(value));
            var bValue = new DVector<T>(new[] { value(7) });
            var reused = new DVector<T>();
            foreach (DVector<T> source in new[] { aValue, bValue, new DVector<T>(), aValue }) {
                byte[] data = Bytes(w => store(source, w));
                Read(data, r => { restore(reused, r); return reused; });
                SameVector(source, reused, compare, type + "; reused length=" + source.Length);
            }
        }

        [Fact]
        public void DVectorBuffers_AllTypes_BlockBoundariesAndReplacement()
        {
            VectorMatrix(i => i * 31 - 100003, gSerialization.Store, gSerialization.Restore, Equal, "int");
            VectorMatrix(i => (short)(i % 30001 - 15000), gSerialization.Store, gSerialization.Restore, Equal, "short");
            VectorMatrix(i => i == 0 ? NegativeZeroF : (i * 3 - 10001) / 16.0f, gSerialization.Store, gSerialization.Restore, Bits, "float");
            VectorMatrix(i => i == 0 ? NegativeZeroD : (i * 7 - 20003) / 32.0, gSerialization.Store, gSerialization.Restore, Bits, "double");
        }

        static Vector2f ReadV2f(BinaryReader r) { var v = new Vector2f(); gSerialization.Restore(ref v, r); return v; }
        static Vector2d ReadV2d(BinaryReader r) { var v = new Vector2d(); gSerialization.Restore(ref v, r); return v; }
        static Vector3f ReadV3f(BinaryReader r) { var v = new Vector3f(); gSerialization.Restore(ref v, r); return v; }
        static Vector3d ReadV3d(BinaryReader r) { var v = new Vector3d(); gSerialization.Restore(ref v, r); return v; }
        static Quaternionf ReadQuaternion(BinaryReader r) { var v = new Quaternionf(); gSerialization.Restore(ref v, r); return v; }
        static Frame3f ReadFrame(BinaryReader r) { var v = new Frame3f(); gSerialization.Restore(ref v, r); return v; }
        static AxisAlignedBox2d ReadBox(BinaryReader r) { var v = new AxisAlignedBox2d(); gSerialization.Restore(ref v, r); return v; }
        static Segment2d ReadSegment(BinaryReader r) { var v = new Segment2d(); gSerialization.Restore(ref v, r); return v; }
        static Circle2d ReadCircle(BinaryReader r) { var v = new Circle2d(1); gSerialization.Restore(ref v, r); return v; }
        static Arc2d ReadArc(BinaryReader r) { var v = new Arc2d(Vector2d.Zero, 1, 0, 1); gSerialization.Restore(ref v, r); return v; }

        static void Same(Frame3f a, Frame3f b, string c) { Same(a.Origin, b.Origin, c + "; origin"); Same(a.Rotation, b.Rotation, c + "; rotation"); }
        static void Same(AxisAlignedBox2d a, AxisAlignedBox2d b, string c) { Same(a.Min, b.Min, c + "; min"); Same(a.Max, b.Max, c + "; max"); }
        static void Same(Segment2d a, Segment2d b, string c) { Same(a.Center, b.Center, c + "; center"); Same(a.Direction, b.Direction, c + "; direction"); Bits(a.Extent, b.Extent, c + "; extent"); }
        static void Same(Circle2d a, Circle2d b, string c) { Same(a.Center, b.Center, c + "; center"); Bits(a.Radius, b.Radius, c + "; radius"); Equal(a.IsReversed, b.IsReversed, c + "; reversed"); }
        static void Same(Arc2d a, Arc2d b, string c) { Same(a.Center, b.Center, c + "; center"); Bits(a.Radius, b.Radius, c + "; radius"); Bits(a.AngleStartDeg, b.AngleStartDeg, c + "; start"); Bits(a.AngleEndDeg, b.AngleEndDeg, c + "; end"); Equal(a.IsReversed, b.IsReversed, c + "; reversed"); }

        [Fact]
        public void PrimitiveFields_AllSupportedTypes_DistinctChannelsAndSignedZero()
        {
            RoundTrip(new Vector2f(NegativeZeroF, 2.25f), gSerialization.Store, ReadV2f, Same, "Vector2f");
            RoundTrip(new Vector2d(NegativeZeroD, -3.5), gSerialization.Store, ReadV2d, Same, "Vector2d");
            RoundTrip(new Vector3f(1.25f, NegativeZeroF, -3.75f), gSerialization.Store, ReadV3f, Same, "Vector3f");
            RoundTrip(new Vector3d(4.125, -5.25, NegativeZeroD), gSerialization.Store, ReadV3d, Same, "Vector3d");
            var q = new Quaternionf(0.125f, -0.25f, 0.5f, NegativeZeroF);
            RoundTrip(q, gSerialization.Store, ReadQuaternion, Same, "Quaternionf stored representation");
            RoundTrip(new Frame3f(new Vector3f(7.5f, NegativeZeroF, -9.25f), q), gSerialization.Store, ReadFrame, Same, "Frame3f");
            RoundTrip(new AxisAlignedBox2d { Min = new Vector2d(-7.25, NegativeZeroD), Max = new Vector2d(11.5, 13.75) }, gSerialization.Store, ReadBox, Same, "box");
            RoundTrip(new Segment2d(new Vector2d(NegativeZeroD, 2.25), new Vector2d(0.6, 0.8), 3.75), gSerialization.Store, ReadSegment, Same, "segment explicit fields");
            RoundTrip(new Segment2d(new Vector2d(-2.25, 1.5), new Vector2d(5.75, 7.5)), gSerialization.Store, ReadSegment, Same, "segment constructor-normalized fields");
            foreach (bool reversed in new[] { false, true }) {
                RoundTrip(new Circle2d(new Vector2d(NegativeZeroD, -4.25), 5.5) { IsReversed = reversed }, gSerialization.Store, ReadCircle, Same, "circle; reversed=" + reversed);
                RoundTrip(new Arc2d(new Vector2d(6.75, NegativeZeroD), 8.125, 310.25, 21.5) { IsReversed = reversed }, gSerialization.Store, ReadArc, Same, "arc wrapped constructor angles; reversed=" + reversed);
            }
        }

        [Fact]
        public void PrimitiveStorageOrder_IsIndependentOfRestore()
        {
            byte[] bytes = Bytes(w => {
                gSerialization.Store(new Vector3d(1.25, -2.5, 3.75), w);
                gSerialization.Store(new Frame3f(new Vector3f(4.5f, -5.25f, 6.75f), new Quaternionf(0.125f, -0.25f, 0.5f, 0.75f)), w);
                gSerialization.Store(new AxisAlignedBox2d { Min = new Vector2d(-7.5, -8.25), Max = new Vector2d(9.5, 10.75) }, w);
                gSerialization.Store(new Segment2d(new Vector2d(11.25, 12.5), new Vector2d(0.6, 0.8), 13.75), w);
                gSerialization.Store(new Circle2d(new Vector2d(14.25, 15.5), 16.75) { IsReversed = true }, w);
                gSerialization.Store(new Arc2d(new Vector2d(17.25, 18.5), 19.75, 20.25, 21.5) { IsReversed = true }, w);
            });
            Read(bytes, r => {
                foreach (double v in new[] { 1.25, -2.5, 3.75 }) Bits(v, r.ReadDouble(), "Vector3d xyz wire order");
                foreach (float v in new[] { 4.5f, -5.25f, 6.75f, 0.125f, -0.25f, 0.5f, 0.75f }) Bits(v, r.ReadSingle(), "frame origin then quaternion xyzw wire order");
                foreach (double v in new[] { -7.5, -8.25, 9.5, 10.75, 11.25, 12.5, 0.6, 0.8, 13.75, 14.25, 15.5, 16.75 }) Bits(v, r.ReadDouble(), "box min/max, segment center/direction/extent, circle wire order");
                Require(r.ReadBoolean(), "circle reversal wire order");
                foreach (double v in new[] { 17.25, 18.5, 19.75, 20.25, 21.5 }) Bits(v, r.ReadDouble(), "arc center/radius/angles wire order");
                Require(r.ReadBoolean(), "arc reversal wire order");
                return true;
            });
        }

        static void SameList<T>(IList<T> a, IList<T> b, Action<T, T, string> compare, string context)
        {
            Equal(a.Count, b.Count, context + "; count");
            for (int i = 0; i < a.Count; ++i) compare(a[i], b[i], context + "; index=" + i);
        }

        static void ListMatrix<T>(T[] values, T prefix, Action<List<T>, BinaryWriter> store,
            Action<List<T>, BinaryReader> restore, Action<T, T, string> compare, string type)
        {
            foreach (T[] input in new[] { new T[0], values }) {
                var source = new List<T>(input);
                RoundTrip(source, store, r => { var v = new List<T>(); restore(v, r); return v; },
                    (a, b, c) => SameList(a, b, compare, c), type);
                var appended = new List<T> { prefix };
                Read(Bytes(w => store(source, w)), r => { restore(appended, r); return appended; });
                SameList(new[] { prefix }.Concat(input).ToArray(), appended, compare, type + "; append prefix");
            }
        }

        static string ReadString(BinaryReader r) { string s = null; gSerialization.Restore(ref s, r); return s; }
        static string[] ReadStrings(BinaryReader r) { string[] s = null; gSerialization.Restore(ref s, r); return s; }

        [Fact]
        public void NumericListsAndText_EmptyNonemptyOrderAppendAndUnicode()
        {
            ListMatrix(new[] { int.MinValue, 17, -29, int.MaxValue }, 31, gSerialization.Store, gSerialization.Restore, Equal, "List<int>");
            ListMatrix(new[] { NegativeZeroF, 1.25f, -3.5f, 7.75f }, -9.125f, gSerialization.Store, gSerialization.Restore, Bits, "List<float>");
            ListMatrix(new[] { NegativeZeroD, 2.25, -5.5, 11.75 }, -13.125, gSerialization.Store, gSerialization.Restore, Bits, "List<double>");
            foreach (string text in new[] { "", "ASCII\0suffix", "Grüße — 東京 — 🧭", "e\u0301 ≠ é\r\n" })
                RoundTrip(text, gSerialization.Store, ReadString, Equal, "UTF-8 text=" + text);
            foreach (string[] array in new[] { new string[0], new[] { "", "zwei", "東京", "Grüße 🧭", "\0" } })
                RoundTrip(array, gSerialization.Store, ReadStrings, (a, b, c) => SameList(a, b, Equal, c), "string array");
            string[] reused = { "stale", "array", "suffix" };
            foreach (string[] array in new[] { new[] { "rich", "東京" }, new string[0], new[] { "rich", "東京" } }) {
                Read(Bytes(w => gSerialization.Store(array, w)), r => { gSerialization.Restore(ref reused, r); return reused; });
                SameList(array, reused, Equal, "string array replacement");
            }
        }

        static Vector2d[] Points2() => new[] { new Vector2d(-1.25, 2.5), new Vector2d(3.75, 4.125), new Vector2d(5.5, -6.25) };
        static Vector3d[] Points3() => new[] { new Vector3d(-1.25, 2.5, 3.75), new Vector3d(4.125, -5.5, 6.25), new Vector3d(7.75, 8.5, -9.125) };
        static DCurve3 ReadCurve(BinaryReader r) { var c = new DCurve3(); gSerialization.Restore(c, r); return c; }
        static PolyLine2d ReadPolyline(BinaryReader r) { var p = new PolyLine2d(); gSerialization.Restore(p, r); return p; }
        static Polygon2d ReadPolygon(BinaryReader r) { var p = new Polygon2d(); gSerialization.Restore(p, r); return p; }
        static GeneralPolygon2d ReadRegion(BinaryReader r) { var p = new GeneralPolygon2d(); gSerialization.Restore(p, r); return p; }
        static ParametricCurveSequence2 ReadSequence(BinaryReader r) { var s = new ParametricCurveSequence2(); gSerialization.Restore(ref s, r); return s; }
        static IParametricCurve2d ReadParametric(BinaryReader r) { gSerialization.Restore(out IParametricCurve2d c, r); return c; }
        static PlanarSolid2d ReadSolid(BinaryReader r) { var s = new PlanarSolid2d(); gSerialization.Restore(s, r); return s; }

        static void Same(DCurve3 a, DCurve3 b, string c) { Equal(a.Closed, b.Closed, c + "; closed"); SameList(a.Vertices.ToArray(), b.Vertices.ToArray(), Same, c); }
        static void Same(PolyLine2d a, PolyLine2d b, string c) { SameList(a.Vertices, b.Vertices, Same, c); }
        static void Same(Polygon2d a, Polygon2d b, string c) { SameList(a.Vertices, b.Vertices, Same, c); }
        static void Same(GeneralPolygon2d a, GeneralPolygon2d b, string c)
        {
            Same(a.Outer, b.Outer, c + "; outer"); Equal(a.Holes.Count, b.Holes.Count, c + "; holes");
            for (int i = 0; i < a.Holes.Count; ++i) Same(a.Holes[i], b.Holes[i], c + "; hole=" + i);
        }
        static void Same(ParametricCurveSequence2 a, ParametricCurveSequence2 b, string c)
        {
            Equal(a.IsClosed, b.IsClosed, c + "; closed"); Equal(a.Count, b.Count, c + "; count");
            for (int i = 0; i < a.Count; ++i) SameParametric(a.Curves[i], b.Curves[i], c + "; curve=" + i);
        }
        static void SameParametric(IParametricCurve2d a, IParametricCurve2d b, string c)
        {
            Equal(a.GetType(), b.GetType(), c + "; concrete type");
            if (a is Segment2d segment) Same(segment, (Segment2d)b, c);
            else if (a is Circle2d circle) Same(circle, (Circle2d)b, c);
            else if (a is Arc2d arc) Same(arc, (Arc2d)b, c);
            else if (a is ParametricCurveSequence2 sequence) Same(sequence, (ParametricCurveSequence2)b, c);
            else throw new XunitException(c + "; unsupported test fixture type");
        }
        static void Same(PlanarSolid2d a, PlanarSolid2d b, string c)
        {
            SameParametric(a.Outer, b.Outer, c + "; outer"); Equal(a.Holes.Count, b.Holes.Count, c + "; holes");
            for (int i = 0; i < a.Holes.Count; ++i) SameParametric(a.Holes[i], b.Holes[i], c + "; hole=" + i);
        }

        static ParametricCurveSequence2 MixedSequence(bool closed)
        {
            var inner = new ParametricCurveSequence2(new IParametricCurve2d[] {
                new Arc2d(new Vector2d(-2.25, 3.5), 4.75, 37.5, 128.25) { IsReversed = true },
                new Circle2d(new Vector2d(5.25, -6.5), 7.75) { IsReversed = true }
            }, true);
            return new ParametricCurveSequence2(new IParametricCurve2d[] {
                new Segment2d(new Vector2d(-8.25, 9.5), new Vector2d(10.75, -11.125)),
                new Circle2d(new Vector2d(12.25, 13.5), 14.75), inner,
                new ParametricCurveSequence2(new IParametricCurve2d[] { inner.Clone() }, false)
            }, closed);
        }

        static GeneralPolygon2d Region(int holes)
        {
            var outer = new Polygon2d(new[] { new Vector2d(-8, -8), new Vector2d(8, -8), new Vector2d(8, 8), new Vector2d(-8, 8) });
            var region = new GeneralPolygon2d(outer);
            if (holes > 0) region.AddHole(new Polygon2d(new[] { new Vector2d(-6, -3), new Vector2d(-6, 1), new Vector2d(-2, 1), new Vector2d(-2, -3) }));
            if (holes > 1) region.AddHole(new Polygon2d(new[] { new Vector2d(1, 2), new Vector2d(2, 5), new Vector2d(5, 2) }));
            return region;
        }

        [Fact]
        public void CurvesAndRegions_RoundTripsAndOverloadSpecificAppend()
        {
            foreach (bool closed in new[] { false, true }) {
                foreach (Vector3d[] vertices in new[] { new Vector3d[0], Points3() }) {
                    var source = new DCurve3(vertices, closed);
                    RoundTrip(source, gSerialization.Store, ReadCurve, Same, "DCurve3; closed=" + closed + "; vertices=" + vertices.Length);
                    var prefix = new Vector3d(31.25, -32.5, 33.75);
                    var appended = new DCurve3(new[] { prefix }, !closed);
                    Read(Bytes(w => gSerialization.Store(source, w)), r => { gSerialization.Restore(appended, r); return appended; });
                    Same(new DCurve3(new[] { prefix }.Concat(vertices), closed), appended, "DCurve3 prefix, closure replaced");
                }
                foreach (ParametricCurveSequence2 source in new[] { new ParametricCurveSequence2 { IsClosed = closed }, MixedSequence(closed) }) {
                    RoundTrip(source, gSerialization.Store, ReadSequence, Same, "sequence; closed=" + closed + "; count=" + source.Count);
                    IParametricCurve2d prefix = new Circle2d(new Vector2d(-34.25, 35.5), 36.75);
                    var appended = new ParametricCurveSequence2(new[] { prefix }, !closed);
                    Read(Bytes(w => gSerialization.Store(source, w)), r => { gSerialization.Restore(ref appended, r); return appended; });
                    Same(new ParametricCurveSequence2(new[] { prefix }.Concat(source.Curves), closed), appended, "sequence prefix, closure replaced");
                }
            }
            foreach (Vector2d[] vertices in new[] { new Vector2d[0], Points2() }) {
                var polyline = new PolyLine2d(vertices); var polygon = new Polygon2d(vertices);
                RoundTrip(polyline, gSerialization.Store, ReadPolyline, Same, "polyline; vertices=" + vertices.Length);
                RoundTrip(polygon, gSerialization.Store, ReadPolygon, Same, "polygon; vertices=" + vertices.Length);
                var prefix = new Vector2d(-37.25, 38.5);
                var appendedLine = new PolyLine2d(new[] { prefix }); var appendedPolygon = new Polygon2d(new[] { prefix });
                Read(Bytes(w => gSerialization.Store(polyline, w)), r => { gSerialization.Restore(appendedLine, r); return appendedLine; });
                Read(Bytes(w => gSerialization.Store(polygon, w)), r => { gSerialization.Restore(appendedPolygon, r); return appendedPolygon; });
                Same(new PolyLine2d(new[] { prefix }.Concat(vertices)), appendedLine, "polyline append prefix");
                Same(new Polygon2d(new[] { prefix }.Concat(vertices)), appendedPolygon, "polygon append prefix");
            }
            foreach (int holes in new[] { 0, 1, 2 }) RoundTrip(Region(holes), gSerialization.Store, ReadRegion, Same, "region; holes=" + holes);
            foreach (IParametricCurve2d curve in new IParametricCurve2d[] {
                new Segment2d(new Vector2d(1.25, 2.5), new Vector2d(3.75, 4.125)),
                new Circle2d(new Vector2d(5.25, -6.5), 7.75) { IsReversed = true },
                new Arc2d(new Vector2d(-8.25, 9.5), 10.75, 11.125, 12.25) { IsReversed = true }, MixedSequence(true)
            }) RoundTrip(curve, gSerialization.Store, ReadParametric, SameParametric, "tagged curve=" + curve.GetType().Name);
            foreach (int holes in new[] { 0, 2 }) {
                var solid = new PlanarSolid2d();
                solid.SetOuter(new Circle2d(new Vector2d(-1.25, 2.5), 32) { IsReversed = true }, true);
                if (holes > 0) {
                    solid.AddHole(new Circle2d(new Vector2d(-5.25, 6.5), 2.75));
                    var loop = new ParametricCurveSequence2(new IParametricCurve2d[] {
                        new Segment2d(new Vector2d(4, 4), new Vector2d(6, 4)),
                        new Segment2d(new Vector2d(6, 4), new Vector2d(5, 6)),
                        new Segment2d(new Vector2d(5, 6), new Vector2d(4, 4))
                    }, true);
                    solid.AddHole(loop);
                }
                RoundTrip(solid, gSerialization.Store, ReadSolid, Same, "PlanarSolid2d; holes=" + holes);
            }
        }

        static void RejectCounted<T>(Action<T, BinaryReader> restore, Func<T> create, int elementSize, string context)
        {
            foreach (int count in new[] { -1, int.MinValue, int.MaxValue }) {
                T value = create();
                Exception error = Assert.Throws<Exception>(() => Read(Bytes(w => w.Write(count)), r => { restore(value, r); return value; }));
                Require(error.Message.Contains(count < 0 ? "negative element count" : (elementSize == 1 ? "beyond the end" : "too large")), context + "; count=" + count + "; " + error.Message);
            }
            foreach (int bytes in new[] { 0, elementSize * 3 - 1 }) {
                T value = create();
                Assert.Throws<EndOfStreamException>(() => Read(Bytes(w => { w.Write(3); w.Write(new byte[bytes]); }), r => { restore(value, r); return value; }));
            }
            T missing = create();
            Assert.Throws<EndOfStreamException>(() => Read(new byte[0], r => { restore(missing, r); return missing; }));
        }

        [Fact]
        public void MalformedCountedBuffersAndText_RejectNegativeOverflowTruncation()
        {
            RejectCounted(gSerialization.Restore, () => new DVector<int>(), sizeof(int), "DVector<int>");
            RejectCounted(gSerialization.Restore, () => new DVector<short>(), sizeof(short), "DVector<short>");
            RejectCounted(gSerialization.Restore, () => new DVector<float>(), sizeof(float), "DVector<float>");
            RejectCounted(gSerialization.Restore, () => new DVector<double>(), sizeof(double), "DVector<double>");
            foreach (int count in new[] { -1, int.MinValue }) {
                Assert.Throws<Exception>(() => Read(Bytes(w => w.Write(count)), ReadString));
                Assert.Throws<Exception>(() => Read(Bytes(w => w.Write(count)), ReadStrings));
            }
            Assert.Throws<EndOfStreamException>(() => Read(Bytes(w => w.Write(int.MaxValue)), ReadString));
            Assert.Throws<Exception>(() => Read(Bytes(w => w.Write(int.MaxValue)), ReadStrings));
            byte[] text = Bytes(w => gSerialization.Store("Grüße 🧭", w));
            Assert.Throws<EndOfStreamException>(() => Read(text.Take(text.Length - 1).ToArray(), ReadString));
            byte[] strings = Bytes(w => gSerialization.Store(new[] { "prefix", "東京" }, w));
            Assert.Throws<EndOfStreamException>(() => Read(strings.Take(strings.Length - 1).ToArray(), ReadStrings));
            Assert.Throws<EndOfStreamException>(() => Read(new byte[0], ReadString));
            Assert.Throws<EndOfStreamException>(() => Read(new byte[0], ReadStrings));
            Exception tag = Assert.Throws<Exception>(() => Read(Bytes(w => w.Write(99)), ReadParametric));
            Require(tag.Message.Contains("unknown curve type 99"), "unsupported curve discriminator");
        }

        [Fact]
        public void MalformedMesh_UnsupportedVersionTruncatedAndInvalidBuffers()
        {
            foreach (int version in new[] { -1, 0, 3, int.MaxValue }) {
                Exception error = Assert.Throws<Exception>(() => Read(Bytes(w => w.Write(version)), ReadMesh));
                Require(error.Message.Contains("Incorrect DMesh3Version"), "unsupported version=" + version);
            }
            byte[] valid = Bytes(w => gSerialization.Store(Fixture("sparse", 15), w));
            foreach (int length in new[] { 0, 3, 7, valid.Length - 1 })
                Assert.Throws<EndOfStreamException>(() => Read(valid.Take(length).ToArray(), ReadMesh));
            foreach (string fault in new[] { "vertices-size", "triangles-size", "edges-size", "edge-refs-size", "normal-size", "color-size", "UV-size", "groups-size", "vertex-refs-size", "edge-vertex", "edge-triangle", "edge-refcount" }) {
                var mesh = Fixture("open", 15);
                switch (fault) {
                    case "vertices-size": mesh.VerticesBuffer.Add(42); break;
                    case "triangles-size": mesh.TrianglesBuffer.Add(42); break;
                    case "edges-size": mesh.EdgesBuffer.Add(42); break;
                    case "edge-refs-size": mesh.EdgesRefCounts.RawRefCounts.Add(1); break;
                    case "normal-size": mesh.NormalsBuffer.pop_back(); break;
                    case "color-size": mesh.ColorsBuffer.pop_back(); break;
                    case "UV-size": mesh.UVBuffer.pop_back(); break;
                    case "groups-size": mesh.GroupsBuffer.pop_back(); break;
                    case "vertex-refs-size": mesh.VerticesRefCounts.RawRefCounts.Add(1); break;
                    case "edge-vertex": mesh.EdgesBuffer[1] = mesh.MaxVertexID; break;
                    case "edge-triangle": mesh.EdgesBuffer[2] = mesh.MaxTriangleID; break;
                    case "edge-refcount": mesh.EdgesRefCounts.RawRefCounts[0] = 2; break;
                }
                byte[] corrupt = Bytes(w => gSerialization.Store(mesh, w));
                Exception error = Assert.Throws<Exception>(() => Read(corrupt, ReadMesh));
                Require(error.Message.Contains("invalid mesh data"), "fault=" + fault + "; " + error.Message);
            }
        }

        [Fact]
        public void ComparisonBoundaryFaults_DetectOmittedChangedAndRetainedComponentsAndLastElement()
        {
            DMesh3 source = Fixture("sparse", 15);
            DMesh3 restored = Read(Bytes(w => gSerialization.Store(source, w)), ReadMesh);
            SameMesh(source, restored, "sensitivity baseline");
            restored.DiscardVertexUVs();
            Assert.Throws<XunitException>(() => SameMesh(source, restored, "temporary omitted UV component"));
            restored = Read(Bytes(w => gSerialization.Store(source, w)), ReadMesh);
            int vid = restored.VertexIndices().OrderBy(v => v).First();
            restored.SetVertexColor(vid, new Vector3f(99, 98, 97));
            Assert.Throws<XunitException>(() => SameMesh(source, restored, "temporary changed component values"));
            restored = Read(Bytes(w => gSerialization.Store(source, w)), ReadMesh);
            SameMesh(source, restored, "sensitivity restored baseline");
            DMesh3 plain = Fixture("sparse", 0), stale = Read(Bytes(w => gSerialization.Store(plain, w)), ReadMesh);
            SameMesh(plain, stale, "plain baseline");
            stale.EnableVertexNormals(Vector3f.AxisY);
            Assert.Throws<XunitException>(() => SameMesh(plain, stale, "temporary retained destination normals"));
            stale.DiscardVertexNormals();
            SameMesh(plain, stale, "plain restored baseline");
            int block = new DVector<double>().BlockCount;
            var vector = new DVector<double>(Enumerable.Range(0, block + 1).Select(i => i / 16.0));
            DVector<double> read = Read(Bytes(w => gSerialization.Store(vector, w)), r => { var v = new DVector<double>(); gSerialization.Restore(v, r); return v; });
            SameVector(vector, read, Bits, "last block baseline");
            read.resize(block);
            Assert.Throws<XunitException>(() => SameVector(vector, read, Bits, "temporary truncated final block"));
            read.Add(vector[block] + 1);
            Assert.Throws<XunitException>(() => SameVector(vector, read, Bits, "temporary wrong final boundary value"));
            read[block] = vector[block];
            SameVector(vector, read, Bits, "last block restored baseline");
        }
    }
}
