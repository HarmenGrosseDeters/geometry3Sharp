using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gSerialization: a restored mesh equals its source, also in a destination that held another mesh, and the stored
    /// form of the small types stays what it is. Damaged .g3mesh input is covered by G3MeshReaderTests and ReaderFuzzTests.
    /// </summary>
    public class SerializationRoundTripTests
    {
        static void Bits(double expected, double actual, string context) =>
            Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual), context + ": " + expected + " vs " + actual);
        static void Bits(float expected, float actual, string context) =>
            Assert.True(BitConverter.ToInt32(BitConverter.GetBytes(expected), 0) == BitConverter.ToInt32(BitConverter.GetBytes(actual), 0), context + ": " + expected + " vs " + actual);

        static void Same(Vector2d a, Vector2d b, string c) { Bits(a.x, b.x, c + ".x"); Bits(a.y, b.y, c + ".y"); }
        static void Same(Vector3d a, Vector3d b, string c) { Bits(a.x, b.x, c + ".x"); Bits(a.y, b.y, c + ".y"); Bits(a.z, b.z, c + ".z"); }
        static void Same(Vector2f a, Vector2f b, string c) { Bits(a.x, b.x, c + ".x"); Bits(a.y, b.y, c + ".y"); }
        static void Same(Vector3f a, Vector3f b, string c) { Bits(a.x, b.x, c + ".x"); Bits(a.y, b.y, c + ".y"); Bits(a.z, b.z, c + ".z"); }

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
                Assert.Equal(stream.Length, stream.Position);
                return value;
            }
        }

        static DMesh3 ReadMesh(BinaryReader reader)
        {
            var mesh = new DMesh3();
            gSerialization.Restore(mesh, reader);
            return mesh;
        }

        static byte[] MeshBytes(DMesh3 mesh) => Bytes(w => gSerialization.Store(mesh, w));
        static void RestoreInto(DMesh3 mesh, byte[] bytes) => Read(bytes, r => { gSerialization.Restore(mesh, r); return mesh; });

        static MeshComponents Components(int mask)
        {
            MeshComponents flags = (MeshComponents)0;
            if ((mask & 1) != 0) flags |= MeshComponents.VertexNormals;
            if ((mask & 2) != 0) flags |= MeshComponents.VertexColors;
            if ((mask & 4) != 0) flags |= MeshComponents.VertexUVs;
            if ((mask & 8) != 0) flags |= MeshComponents.FaceGroups;
            return flags;
        }

        static void Attributes(DMesh3 mesh)
        {
            foreach (int id in mesh.VertexIndices()) {
                float f = (id + 1) / 16.0f;
                if (mesh.HasVertexNormals) mesh.SetVertexNormal(id, new Vector3f(f, -f * 2, f * 4));
                if (mesh.HasVertexColors) mesh.SetVertexColor(id, new Vector3f(f / 2, f / 4, -f / 8));
                if (mesh.HasVertexUVs) mesh.SetVertexUV(id, new Vector2f(-f * 8, f * 16));
            }
            if (mesh.HasTriangleGroups)
                foreach (int id in mesh.TriangleIndices()) mesh.SetTriangleGroup(id, 7 + 3 * id);
        }

        static DMesh3 Fixture(string name, int mask)
        {
            var mesh = new DMesh3(Components(mask));
            if (name == "large") {
                // long enough that every stored buffer spans several DVector blocks
                DMesh3 sphere = new Sphere3Generator_NormalizedCube { EdgeVertices = 30 }.Generate().MakeDMesh();
                foreach (int v in sphere.VertexIndices()) mesh.AppendVertex(sphere.GetVertex(v));
                foreach (int t in sphere.TriangleIndices()) Assert.True(mesh.AppendTriangle(sphere.GetTriangle(t)) >= 0);
                Assert.True(mesh.VertexCount > new DVector<short>().BlockCount, "more vertices than a block");
            } else if (name == "point-cloud") {
                mesh.AppendVertex(new Vector3d(-1.25, 2.5, 3.75)); mesh.AppendVertex(new Vector3d(4.5, -5.25, 6.125));
                mesh.AppendVertex(new Vector3d(7.75, 8.5, -9.25));
            } else if (name == "closed") {
                int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(new Vector3d(2, 0, 0));
                int c = mesh.AppendVertex(new Vector3d(0, 4, 0)), d = mesh.AppendVertex(new Vector3d(0, 0, 8));
                mesh.AppendTriangle(a, c, b); mesh.AppendTriangle(a, b, d);
                mesh.AppendTriangle(b, c, d); mesh.AppendTriangle(c, a, d);
                Assert.True(mesh.IsClosed(), "closed fixture");
            } else if (name != "empty") {
                int count = (name == "disconnected" || name == "sparse") ? 3 : 1;
                for (int patch = 0; patch < count; ++patch) {
                    int a = mesh.AppendVertex(new Vector3d(patch * 8, 0.25, 0.5)), b = mesh.AppendVertex(new Vector3d(patch * 8 + 2, 0.25, 0.5));
                    Assert.True(mesh.AppendTriangle(a, b, mesh.AppendVertex(new Vector3d(patch * 8, 4.25, 0.5))) >= 0);
                }
                if (name == "sparse") {
                    Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(1, true, true));
                    Assert.True(mesh.MaxVertexID > mesh.VertexCount && mesh.MaxTriangleID > mesh.TriangleCount && mesh.MaxEdgeID > mesh.EdgeCount, "sparse");
                }
                if (name == "isolated" || name == "sparse") mesh.AppendVertex(new Vector3d(-2.5, 6.25, 9.5));
            }
            Attributes(mesh);
            return mesh;
        }

        static void SameMesh(DMesh3 a, DMesh3 b, string context)
        {
            Assert.Equal(a.Components, b.Components);
            Assert.Equal(a.VertexIndices().OrderBy(v => v), b.VertexIndices().OrderBy(v => v));
            Assert.Equal(a.TriangleIndices().OrderBy(t => t), b.TriangleIndices().OrderBy(t => t));
            Assert.Equal(a.EdgeIndices().OrderBy(e => e), b.EdgeIndices().OrderBy(e => e));
            foreach (int id in a.VertexIndices()) {
                Same(a.GetVertex(id), b.GetVertex(id), context + " vertex " + id);
                if (a.HasVertexNormals) Same(a.GetVertexNormal(id), b.GetVertexNormal(id), context + " normal " + id);
                if (a.HasVertexColors) Same(a.GetVertexColor(id), b.GetVertexColor(id), context + " color " + id);
                if (a.HasVertexUVs) Same(a.GetVertexUV(id), b.GetVertexUV(id), context + " UV " + id);
            }
            foreach (int id in a.TriangleIndices()) {
                Assert.Equal(a.GetTriangle(id), b.GetTriangle(id));
                Assert.Equal(a.GetTriEdges(id), b.GetTriEdges(id));
                if (a.HasTriangleGroups) Assert.Equal(a.GetTriangleGroup(id), b.GetTriangleGroup(id));
            }
            foreach (int id in a.EdgeIndices()) Assert.Equal(a.GetEdge(id), b.GetEdge(id));
            DMesh3PropertyTests.CheckTopology(b);
        }

        // The rebuilt free lists, refcounts and adjacency work: edits after the restore give a valid mesh.
        static void EditAfterRestore(DMesh3 mesh)
        {
            int[] before = mesh.VertexIndices().OrderBy(v => v).ToArray();
            int a = mesh.AppendVertex(new Vector3d(32.25, 33.5, 34.75)), b = mesh.AppendVertex(new Vector3d(36.25, 33.5, 34.75));
            int tid = mesh.AppendTriangle(a, b, mesh.AppendVertex(new Vector3d(32.25, 37.5, 34.75)));
            Assert.True(tid >= 0, "appended triangle");
            DMesh3PropertyTests.CheckTopology(mesh);
            Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(tid, true, true));
            Assert.Equal(before, mesh.VertexIndices().OrderBy(v => v));
            if (mesh.EdgeCount != 0)
                Assert.Equal(MeshResult.Ok, mesh.SplitEdge(mesh.EdgeIndices().Min(), out DMesh3.EdgeSplitInfo _));
            DMesh3PropertyTests.CheckTopology(mesh);
        }

        public static IEnumerable<object[]> MeshCases()
        {
            foreach (string name in new[] { "empty", "point-cloud", "open", "closed", "disconnected", "isolated", "sparse", "large" })
                foreach (int mask in new[] { 0, 15 }) yield return new object[] { name, mask };
            for (int mask = 1; mask < 15; ++mask) yield return new object[] { "sparse", mask };
        }

        [Theory]
        [MemberData(nameof(MeshCases))]
        public void Mesh_RoundTrip_KeepsLiveIDsAndEveryChannel_AndStaysEditable(string name, int mask)
        {
            DMesh3 source = Fixture(name, mask);
            DMesh3 first = Read(MeshBytes(source), ReadMesh);
            SameMesh(source, first, name + " first");
            SameMesh(source, Read(MeshBytes(first), ReadMesh), name + " second");
            EditAfterRestore(first);
        }

        [Fact]
        public void MeshRestore_ReusedDestination_RichSparseRichMatchesFresh()
        {
            DMesh3 rich = Fixture("closed", 15), sparse = Fixture("sparse", 0);
            var reused = new DMesh3();
            foreach (DMesh3 source in new[] { rich, sparse, rich }) {
                byte[] data = MeshBytes(source);
                RestoreInto(reused, data);
                SameMesh(source, reused, "reused");
                SameMesh(Read(data, ReadMesh), reused, "fresh");
            }
            EditAfterRestore(reused);
        }

        [Fact]
        public void MeshRestore_ReusedDestination_DropsOldVertices_AndCompacts()
        {
            // the first mesh has more vertices than the second, and vertex 0 is deleted in the second
            DMesh3 first = new Sphere3Generator_NormalizedCube { EdgeVertices = 4 }.Generate().MakeDMesh();
            DMesh3 second = new Sphere3Generator_NormalizedCube { EdgeVertices = 2 }.Generate().MakeDMesh();
            foreach (int t in second.VtxTrianglesItr(0).ToArray())
                Assert.Equal(MeshResult.Ok, second.RemoveTriangle(t, true, false));
            Assert.False(second.IsVertex(0), "vertex 0 is deleted");

            var reused = new DMesh3();
            RestoreInto(reused, MeshBytes(first));
            RestoreInto(reused, MeshBytes(second));
            SameMesh(second, reused, "reused destination holds only the second mesh");

            reused.CompactInPlace();
            DMesh3PropertyTests.CheckTopology(reused);
            Assert.Equal(second.VertexCount, reused.VertexCount);
            Assert.Equal(second.TriangleCount, reused.TriangleCount);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void Strings_NegativeCount_AreRejected(int count)
        {
            byte[] data = Bytes(w => w.Write(count));
            Assert.Contains("negative element count", Assert.Throws<Exception>(() => Read(data, r => { string s = null; gSerialization.Restore(ref s, r); return s; })).Message);
            Assert.Contains("negative element count", Assert.Throws<Exception>(() => Read(data, r => { string[] s = null; gSerialization.Restore(ref s, r); return s; })).Message);
        }

        [Fact]
        public void Strings_CountBeyondTheStream_AreRejected()
        {
            byte[] data = Bytes(w => w.Write(int.MaxValue));
            Assert.Throws<EndOfStreamException>(() => Read(data, r => { string s = null; gSerialization.Restore(ref s, r); return s; }));
            Assert.Throws<Exception>(() => Read(data, r => { string[] s = null; gSerialization.Restore(ref s, r); return s; }));
        }

        [Fact]
        public void Strings_Truncated_AreRejected_AndWholeOnesRoundTrip()
        {
            byte[] text = Bytes(w => gSerialization.Store("Gr\u00fc\u00dfe", w));
            byte[] texts = Bytes(w => gSerialization.Store(new[] { "prefix", "\u6771\u4eac" }, w));
            Assert.Equal("Gr\u00fc\u00dfe", Read(text, r => { string s = null; gSerialization.Restore(ref s, r); return s; }));
            Assert.Equal(new[] { "prefix", "\u6771\u4eac" }, Read(texts, r => { string[] s = null; gSerialization.Restore(ref s, r); return s; }));
            Assert.Throws<EndOfStreamException>(() => Read(text.Take(text.Length - 1).ToArray(), r => { string s = null; gSerialization.Restore(ref s, r); return s; }));
            Assert.Throws<EndOfStreamException>(() => Read(texts.Take(texts.Length - 1).ToArray(), r => { string[] s = null; gSerialization.Restore(ref s, r); return s; }));
        }

        [Fact]
        public void Curve_UnknownTag_IsRejected()
        {
            byte[] data = Bytes(w => w.Write(99));
            Exception error = Assert.Throws<Exception>(() => Read(data, r => { gSerialization.Restore(out IParametricCurve2d c, r); return c; }));
            Assert.Contains("unknown curve type 99", error.Message);
        }

        [Fact]
        public void MeshRestore_ReusedDestination_SingleTriangleThenDeletedIsEmpty()
        {
            DMesh3 source = Fixture("open", 0);
            var reused = new DMesh3((MeshComponents)0);
            RestoreInto(reused, MeshBytes(source));
            SameMesh(source, reused, "first restore");

            // deletion keeps the buffer slots, and none of them may stay live in the destination
            Assert.Equal(MeshResult.Ok, source.RemoveTriangle(source.TriangleIndices().Single(), true, true));
            RestoreInto(reused, MeshBytes(source));
            Assert.Equal(0, reused.VertexCount);
            Assert.Equal(0, reused.TriangleCount);
            Assert.Equal(0, reused.EdgeCount);
            SameMesh(source, reused, "second restore");
        }

        [Fact]
        public void MeshRestore_ReusedDestination_Version1DropsOldVertices()
        {
            // version 1 has no vertex refcounts, so nothing in the stream says that the old vertices are gone
            DMesh3 source = Fixture("sparse", 0);
            byte[] version1 = Bytes(w => {
                w.Write(1); w.Write((int)source.Components);
                gSerialization.Store(source.VerticesBuffer, w); gSerialization.Store(source.TrianglesBuffer, w);
                gSerialization.Store(source.EdgesBuffer, w); gSerialization.Store(source.EdgesRefCounts.RawRefCounts, w);
            });
            var expected = new DMesh3(source);      // version 1 cannot hold vertices without edges
            foreach (int v in expected.VertexIndices().ToArray())
                if (expected.GetVtxEdgeCount(v) == 0) Assert.Equal(MeshResult.Ok, expected.RemoveVertex(v, false, true));
            var reused = new DMesh3((MeshComponents)0);
            RestoreInto(reused, MeshBytes(Fixture("disconnected", 0)));
            Assert.Equal(9, reused.VertexCount);
            RestoreInto(reused, version1);
            SameMesh(expected, reused, "version 1");
        }

        [Fact]
        public void MeshRoundTrip_AllSimilarities_StoredBitsAndMappedBackPositions()
        {
            foreach (string name in new[] { "closed", "sparse", "point-cloud" }) {
                DMesh3 source = Fixture(name, 15);
                foreach (Similarity similarity in Similarity.All()) {
                    var transformed = new DMesh3(source);
                    foreach (int vid in source.VertexIndices()) {
                        transformed.SetVertex(vid, similarity.Apply(source.GetVertex(vid)));
                        transformed.SetVertexNormal(vid, (Vector3f)similarity.ApplyDirection(source.GetVertexNormal(vid)));
                    }
                    byte[] bytes = MeshBytes(transformed);
                    DMesh3 restored = Read(bytes, ReadMesh);
                    SameMesh(transformed, restored, name + " " + similarity.Name);
                    Assert.Equal("", Invariance.Difference(restored, Read(bytes, ReadMesh)));
                    foreach (int vid in source.VertexIndices()) {
                        Vector3d expected = source.GetVertex(vid), inverse = similarity.Inverse(restored.GetVertex(vid));
                        if (similarity.IsExact) Same(expected, inverse, name + " " + similarity.Name + " " + vid);
                        else Assert.True(expected.Distance(inverse) <= 16 * similarity.Resolution(16), similarity.Name + " " + vid);
                    }
                }
            }
        }

        /// <summary>The stored field order is a file format; a round trip alone would not notice a change on both sides.</summary>
        [Fact]
        public void SmallTypes_StoredFieldOrder_AndRoundTrip()
        {
            var v2f = new Vector2f(1.25f, 2.25f); var v2d = new Vector2d(3.25, 4.25);
            var v3f = new Vector3f(5.25f, 6.25f, 7.25f); var v3d = new Vector3d(8.25, 9.25, 10.25);
            var q = new Quaternionf(0.125f, -0.25f, 0.5f, 0.75f);
            var frame = new Frame3f(new Vector3f(11.25f, 12.25f, 13.25f), q);
            var box = new AxisAlignedBox2d { Min = new Vector2d(-7.5, -8.25), Max = new Vector2d(9.5, 10.75) };
            var segment = new Segment2d(new Vector2d(11.25, 12.5), new Vector2d(0.6, 0.8), 13.75);
            var circle = new Circle2d(new Vector2d(14.25, 15.5), 16.75) { IsReversed = true };
            var arc = new Arc2d(new Vector2d(17.25, 18.5), 19.75, 20.25, 21.5) { IsReversed = true };
            byte[] bytes = Bytes(w => {
                gSerialization.Store(v2f, w); gSerialization.Store(v2d, w); gSerialization.Store(v3f, w); gSerialization.Store(v3d, w);
                gSerialization.Store(q, w); gSerialization.Store(frame, w); gSerialization.Store(box, w);
                gSerialization.Store(segment, w); gSerialization.Store(circle, w); gSerialization.Store(arc, w);
            });
            Read(bytes, r => {
                foreach (float x in new[] { 1.25f, 2.25f }) Bits(x, r.ReadSingle(), "Vector2f");
                foreach (double x in new[] { 3.25, 4.25 }) Bits(x, r.ReadDouble(), "Vector2d");
                foreach (float x in new[] { 5.25f, 6.25f, 7.25f }) Bits(x, r.ReadSingle(), "Vector3f");
                foreach (double x in new[] { 8.25, 9.25, 10.25 }) Bits(x, r.ReadDouble(), "Vector3d");
                foreach (float x in new[] { 0.125f, -0.25f, 0.5f, 0.75f }) Bits(x, r.ReadSingle(), "Quaternionf");
                foreach (float x in new[] { 11.25f, 12.25f, 13.25f, 0.125f, -0.25f, 0.5f, 0.75f }) Bits(x, r.ReadSingle(), "Frame3f");
                foreach (double x in new[] { -7.5, -8.25, 9.5, 10.75, 11.25, 12.5, 0.6, 0.8, 13.75, 14.25, 15.5, 16.75 }) Bits(x, r.ReadDouble(), "box, segment, circle");
                Assert.True(r.ReadBoolean(), "circle reversed");
                foreach (double x in new[] { 17.25, 18.5, 19.75, 20.25, 21.5 }) Bits(x, r.ReadDouble(), "arc");
                Assert.True(r.ReadBoolean(), "arc reversed");
                return true;
            });
            Read(bytes, r => {
                var a2f = new Vector2f(); gSerialization.Restore(ref a2f, r); Same(v2f, a2f, "Vector2f");
                var a2d = new Vector2d(); gSerialization.Restore(ref a2d, r); Same(v2d, a2d, "Vector2d");
                var a3f = new Vector3f(); gSerialization.Restore(ref a3f, r); Same(v3f, a3f, "Vector3f");
                var a3d = new Vector3d(); gSerialization.Restore(ref a3d, r); Same(v3d, a3d, "Vector3d");
                var aq = new Quaternionf(); gSerialization.Restore(ref aq, r); Same(q, aq, "Quaternionf");
                var af = new Frame3f(); gSerialization.Restore(ref af, r); Same(frame.Origin, af.Origin, "Frame3f"); Same(frame.Rotation, af.Rotation, "Frame3f");
                var ab = new AxisAlignedBox2d(); gSerialization.Restore(ref ab, r); Same(box.Min, ab.Min, "box"); Same(box.Max, ab.Max, "box");
                var ase = new Segment2d(); gSerialization.Restore(ref ase, r); Same(segment, ase, "segment");
                var ac = new Circle2d(1); gSerialization.Restore(ref ac, r); Same(circle, ac, "circle");
                var aa = new Arc2d(Vector2d.Zero, 1, 0, 1); gSerialization.Restore(ref aa, r); Same(arc, aa, "arc");
                return true;
            });
        }

        [Fact]
        public void ListsAndText_RoundTrip()
        {
            var ints = new List<int> { int.MinValue, 17, -29, int.MaxValue }; var floats = new List<float> { 1.25f, -3.5f }; var doubles = new List<double> { 2.25, -5.5 };
            string[] strings = { "", "zwei", "東京", "Grüße 🧭", "\0" };
            byte[] bytes = Bytes(w => {
                gSerialization.Store(ints, w); gSerialization.Store(floats, w); gSerialization.Store(doubles, w);
                gSerialization.Store("Grüße — 東京 — 🧭", w); gSerialization.Store(strings, w);
            });
            Read(bytes, r => {
                var i = new List<int>(); gSerialization.Restore(i, r); Assert.Equal(ints, i);
                var f = new List<float>(); gSerialization.Restore(f, r); Assert.Equal(floats, f);
                var d = new List<double>(); gSerialization.Restore(d, r); Assert.Equal(doubles, d);
                string s = null; gSerialization.Restore(ref s, r); Assert.Equal("Grüße — 東京 — 🧭", s);
                string[] a = null; gSerialization.Restore(ref a, r); Assert.Equal(strings, a);
                return true;
            });
        }

        static void Same(Quaternionf a, Quaternionf b, string c) { Bits(a.x, b.x, c + ".x"); Bits(a.y, b.y, c + ".y"); Bits(a.z, b.z, c + ".z"); Bits(a.w, b.w, c + ".w"); }
        static void Same(Segment2d a, Segment2d b, string c) { Same(a.Center, b.Center, c); Same(a.Direction, b.Direction, c); Bits(a.Extent, b.Extent, c); }
        static void Same(Circle2d a, Circle2d b, string c) { Same(a.Center, b.Center, c); Bits(a.Radius, b.Radius, c); Assert.Equal(a.IsReversed, b.IsReversed); }
        static void Same(Arc2d a, Arc2d b, string c) { Same(a.Center, b.Center, c); Bits(a.Radius, b.Radius, c); Bits(a.AngleStartDeg, b.AngleStartDeg, c); Bits(a.AngleEndDeg, b.AngleEndDeg, c); Assert.Equal(a.IsReversed, b.IsReversed); }
        static void SamePoints(IReadOnlyList<Vector2d> a, IReadOnlyList<Vector2d> b, string c)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; ++i) Same(a[i], b[i], c + " " + i);
        }
        static void SameCurve(IParametricCurve2d a, IParametricCurve2d b, string c)
        {
            Assert.Equal(a.GetType(), b.GetType());
            if (a is Segment2d segment) Same(segment, (Segment2d)b, c);
            else if (a is Circle2d circle) Same(circle, (Circle2d)b, c);
            else if (a is Arc2d arc) Same(arc, (Arc2d)b, c);
            else {
                var x = (ParametricCurveSequence2)a; var y = (ParametricCurveSequence2)b;
                Assert.Equal(x.IsClosed, y.IsClosed);
                Assert.Equal(x.Count, y.Count);
                for (int i = 0; i < x.Count; ++i) SameCurve(x.Curves[i], y.Curves[i], c + " " + i);
            }
        }

        [Fact]
        public void CurvesAndRegions_RoundTrip_KeepOrderClosureAndConcreteTypes()
        {
            Vector3d[] points3 = { new Vector3d(-1.25, 2.5, 3.75), new Vector3d(4.125, -5.5, 6.25), new Vector3d(7.75, 8.5, -9.125) };
            foreach (bool closed in new[] { false, true }) {
                var curve = new DCurve3(points3, closed);
                DCurve3 read = Read(Bytes(w => gSerialization.Store(curve, w)), r => { var v = new DCurve3(); gSerialization.Restore(v, r); return v; });
                Assert.Equal(closed, read.Closed);
                Assert.Equal(3, read.VertexCount);
                for (int i = 0; i < 3; ++i) Same(points3[i], read.GetVertex(i), "DCurve3");
            }

            Vector2d[] points2 = { new Vector2d(-1.25, 2.5), new Vector2d(3.75, 4.125), new Vector2d(5.5, -6.25) };
            SamePoints(points2, Read(Bytes(w => gSerialization.Store(new PolyLine2d(points2), w)), r => { var v = new PolyLine2d(); gSerialization.Restore(v, r); return v; }).Vertices.ToArray(), "polyline");
            SamePoints(points2, Read(Bytes(w => gSerialization.Store(new Polygon2d(points2), w)), r => { var v = new Polygon2d(); gSerialization.Restore(v, r); return v; }).Vertices.ToArray(), "polygon");

            var outer = new Polygon2d(new[] { new Vector2d(-8, -8), new Vector2d(8, -8), new Vector2d(8, 8), new Vector2d(-8, 8) });
            Vector2d[][] holes = { new[] { new Vector2d(-6, -3), new Vector2d(-6, 1), new Vector2d(-2, 1), new Vector2d(-2, -3) },
                new[] { new Vector2d(1, 2), new Vector2d(2, 5), new Vector2d(5, 2) } };
            var region = new GeneralPolygon2d(outer);
            foreach (Vector2d[] hole in holes) region.AddHole(new Polygon2d(hole));
            GeneralPolygon2d readRegion = Read(Bytes(w => gSerialization.Store(region, w)), r => { var v = new GeneralPolygon2d(); gSerialization.Restore(v, r); return v; });
            SamePoints(outer.Vertices.ToArray(), readRegion.Outer.Vertices.ToArray(), "outer");
            Assert.Equal(2, readRegion.Holes.Count);
            for (int i = 0; i < 2; ++i) SamePoints(holes[i], readRegion.Holes[i].Vertices.ToArray(), "hole");

            var inner = new ParametricCurveSequence2(new IParametricCurve2d[] {
                new Arc2d(new Vector2d(-2.25, 3.5), 4.75, 37.5, 128.25) { IsReversed = true },
                new Circle2d(new Vector2d(5.25, -6.5), 7.75) { IsReversed = true } }, true);
            var sequence = new ParametricCurveSequence2(new IParametricCurve2d[] {
                new Segment2d(new Vector2d(-8.25, 9.5), new Vector2d(10.75, -11.125)), new Circle2d(new Vector2d(12.25, 13.5), 14.75), inner }, false);
            foreach (IParametricCurve2d c in sequence.Curves.Concat(new IParametricCurve2d[] { sequence }))
                SameCurve(c, Read(Bytes(w => gSerialization.Store(c, w)), r => { gSerialization.Restore(out IParametricCurve2d v, r); return v; }), c.GetType().Name);

            var solid = new PlanarSolid2d();
            solid.SetOuter(new Circle2d(new Vector2d(-1.25, 2.5), 32) { IsReversed = true }, true);
            solid.AddHole(new Circle2d(new Vector2d(-5.25, 6.5), 2.75));
            solid.AddHole(new ParametricCurveSequence2(new IParametricCurve2d[] {
                new Segment2d(new Vector2d(4, 4), new Vector2d(6, 4)), new Segment2d(new Vector2d(6, 4), new Vector2d(5, 6)),
                new Segment2d(new Vector2d(5, 6), new Vector2d(4, 4)) }, true));
            PlanarSolid2d readSolid = Read(Bytes(w => gSerialization.Store(solid, w)), r => { var v = new PlanarSolid2d(); gSerialization.Restore(v, r); return v; });
            SameCurve(solid.Outer, readSolid.Outer, "solid outer");
            Assert.Equal(2, readSolid.Holes.Count);
            for (int i = 0; i < 2; ++i) SameCurve(solid.Holes[i], readSolid.Holes[i], "solid hole");
        }
    }
}
