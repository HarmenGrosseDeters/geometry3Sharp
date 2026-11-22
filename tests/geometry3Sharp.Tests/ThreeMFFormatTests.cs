using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using g3;
using Xunit;
using static geometry3Sharp.Tests.MeshFormatTestUtil;

namespace geometry3Sharp.Tests
{
    public class ThreeMFFormatTests
    {
        const string Rels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Target=\"/3D/model.model\" Id=\"r0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/>" +
            "</Relationships>";

        // unit tetrahedron with corner at origin, as 3MF mesh element
        const string TetMesh =
            "<mesh><vertices>" +
            "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"1\"/>" +
            "</vertices><triangles>" +
            "<triangle v1=\"0\" v2=\"2\" v3=\"1\"/><triangle v1=\"0\" v2=\"1\" v3=\"3\"/><triangle v1=\"0\" v2=\"3\" v3=\"2\"/><triangle v1=\"1\" v2=\"2\" v3=\"3\"/>" +
            "</triangles></mesh>";

        static byte[] MakePackage(string modelXml, string rels = Rels, string modelPath = "3D/model.model")
        {
            MemoryStream ms = new MemoryStream();
            using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
                if (rels != null)
                    write_entry(zip, "_rels/.rels", rels);
                write_entry(zip, modelPath, modelXml);
            }
            return ms.ToArray();
        }

        static void write_entry(ZipArchive zip, string name, string text)
        {
            using (Stream s = zip.CreateEntry(name).Open()) {
                byte[] b = Encoding.UTF8.GetBytes(text);
                s.Write(b, 0, b.Length);
            }
        }

        static string Model(string resources, string build, string unit = null)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"" + (unit != null ? " unit=\"" + unit + "\"" : "") + ">" +
                "<resources>" + resources + "</resources><build>" + build + "</build></model>";
        }

        static List<DMesh3> Read(byte[] package, out IOReadResult result)
        {
            DMesh3Builder builder = new DMesh3Builder();
            result = StandardMeshReader.ReadFile(new MemoryStream(package), "3mf", ReadOptions.Defaults, builder);
            return builder.Meshes;
        }


        [Fact]
        public void RoundTrip_IsExact()
        {
            DMesh3 mesh = AttributeSphere(new Vector3d(1.0 / 3.0, 100.5, -7e-9));
            DMesh3 read = WriteRead(mesh, "3mf", WriteOptions.Defaults);
            AssertSameTopology(mesh, read);
            Assert.Equal(0.0, MaxPositionError(mesh, read));
            Assert.True(read.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void Write_MultipleMeshes_SeparateOrCombined()
        {
            DMesh3 a = AttributeSphere(Vector3d.Zero), b = AttributeSphere(new Vector3d(5, 0, 0));
            List<WriteMesh> meshes = new List<WriteMesh>() { new WriteMesh(a, "first"), new WriteMesh(b, "second & <more>") };
            string dir = TempDir();
            try {
                string path = Path.Combine(dir, "two.3mf");
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteFile(path, meshes, WriteOptions.Defaults).code);
                DMesh3Builder builder = new DMesh3Builder();
                Assert.Equal(IOCode.Ok, StandardMeshReader.ReadFile(path, ReadOptions.Defaults, builder).code);
                Assert.Equal(2, builder.Meshes.Count);
                Assert.Equal(0.0, MaxPositionError(b, builder.Meshes[1]));
                Assert.Equal("first", builder.Metadata[0]["name"]);
                Assert.Equal("second & <more>", builder.Metadata[1]["name"]);

                WriteOptions combine = WriteOptions.Defaults;
                combine.bCombineMeshes = true;
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteFile(path, meshes, combine).code);
                IOReadResult r;
                List<DMesh3> combined = ReadAll(path, out r);
                Assert.Single(combined);
                Assert.Equal(a.TriangleCount + b.TriangleCount, combined[0].TriangleCount);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Write_PackageStructure()
        {
            DMesh3 mesh = AttributeSphere(Vector3d.Zero);
            MemoryStream ms = new MemoryStream();
            Assert.Equal(IOCode.Ok, new ThreeMFWriter().Write(ms, new List<WriteMesh>() { new WriteMesh(mesh) }, WriteOptions.Defaults).code);
            ms.Position = 0;
            using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read)) {
                Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                Assert.NotNull(zip.GetEntry("_rels/.rels"));
                ZipArchiveEntry model = zip.GetEntry("3D/3dmodel.model");
                Assert.NotNull(model);
                XmlDocument doc = new XmlDocument();
                using (Stream s = model.Open())
                    doc.Load(s);
                Assert.Equal(ThreeMFReader.CoreNamespace, doc.DocumentElement.NamespaceURI);
                Assert.Equal("millimeter", doc.DocumentElement.GetAttribute("unit"));
                Assert.Equal(mesh.TriangleCount, doc.GetElementsByTagName("triangle", ThreeMFReader.CoreNamespace).Count);
            }
        }

        [Fact]
        public void Read_ItemTransform_And_Units()
        {
            // item transform: rotate 90 degrees about z, then translate (10,0,0); unit is centimeter
            string model = Model(
                "<object id=\"1\" type=\"model\">" + TetMesh + "</object>",
                "<item objectid=\"1\" transform=\"0 1 0 -1 0 0 0 0 1 10 0 0\"/>", "centimeter");
            IOReadResult r;
            List<DMesh3> meshes = Read(MakePackage(model), out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 m = Assert.Single(meshes);
            // (1,0,0) -> row vector * M = (0,1,0) + (10,0,0), in cm -> mm
            Assert.True(m.GetVertex(1).Distance(new Vector3d(100, 10, 0)) < 1e-12, m.GetVertex(1).ToString());
            Assert.True(m.GetVertex(2).Distance(new Vector3d(90, 0, 0)) < 1e-12, m.GetVertex(2).ToString());
            Assert.Equal(1000.0 / 6.0, Volume(m), 9);

            ThreeMFReader reader = new ThreeMFReader() { ConvertToMillimeters = false };
            DMesh3Builder builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, reader.Read(new MemoryStream(MakePackage(model)), ReadOptions.Defaults, builder).code);
            Assert.Equal("centimeter", reader.FileUnit);
            Assert.True(builder.Meshes[0].GetVertex(1).Distance(new Vector3d(10, 1, 0)) < 1e-12);
        }

        [Fact]
        public void Read_Components_NestedTransforms()
        {
            // object 2 = two translated instances of object 1; object 3 = object 2 scaled by 2
            string model = Model(
                "<object id=\"1\" type=\"model\">" + TetMesh + "</object>" +
                "<object id=\"2\" type=\"model\"><components>" +
                "<component objectid=\"1\"/>" +
                "<component objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 5 0 0\"/>" +
                "</components></object>" +
                "<object id=\"3\" type=\"model\"><components>" +
                "<component objectid=\"2\" transform=\"2 0 0 0 2 0 0 0 2 0 0 0\"/>" +
                "</components></object>",
                "<item objectid=\"3\"/><item objectid=\"1\"/>");
            IOReadResult r;
            List<DMesh3> meshes = Read(MakePackage(model), out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(2, meshes.Count);
            DMesh3 m = meshes[0];
            Assert.Equal(8, m.VertexCount);
            Assert.Equal(8, m.TriangleCount);
            // second instance: (1,0,0) -> (6,0,0) -> scaled (12,0,0)
            Assert.True(m.GetVertex(5).Distance(new Vector3d(12, 0, 0)) < 1e-12, m.GetVertex(5).ToString());
            Assert.Equal(2 * 8.0 / 6.0, Volume(m), 9);
            Assert.Equal(4, meshes[1].TriangleCount);
        }

        [Fact]
        public void Read_WithoutRelationships_UsesDefaultModelPath()
        {
            string model = Model("<object id=\"1\" type=\"model\">" + TetMesh + "</object>", "<item objectid=\"1\"/>");
            IOReadResult r;
            List<DMesh3> meshes = Read(MakePackage(model, null, "3D/3dmodel.model"), out r);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Equal(4, meshes[0].TriangleCount);
        }

        [Fact]
        public void Read_SkipsInvalidTriangles()
        {
            string mesh = TetMesh.Replace("<triangle v1=\"1\" v2=\"2\" v3=\"3\"/>",
                "<triangle v1=\"1\" v2=\"2\" v3=\"3\"/><triangle v1=\"1\" v2=\"1\" v3=\"3\"/><triangle v1=\"1\" v2=\"2\" v3=\"99\"/>");
            string model = Model("<object id=\"1\" type=\"model\">" + mesh + "</object>", "<item objectid=\"1\"/>");
            ThreeMFReader reader = new ThreeMFReader();
            List<string> warnings = new List<string>();
            reader.warningEvent += (msg, data) => warnings.Add(msg);
            DMesh3Builder builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, reader.Read(new MemoryStream(MakePackage(model)), ReadOptions.Defaults, builder).code);
            Assert.Equal(4, builder.Meshes[0].TriangleCount);
            Assert.Contains(warnings, w => w.Contains("skipped 2"));
        }

        [Fact]
        public void Read_CyclicComponents_ReturnsError()
        {
            string model = Model(
                "<object id=\"1\" type=\"model\"><components><component objectid=\"2\"/></components></object>" +
                "<object id=\"2\" type=\"model\"><components><component objectid=\"1\"/></components></object>",
                "<item objectid=\"1\"/>");
            IOReadResult r;
            Read(MakePackage(model), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        // object 1..depth each reference the next object twice, object depth+1 is the tetrahedron: 2^depth instances
        static string DoublingComponents(int depth)
        {
            StringBuilder res = new StringBuilder();
            for (int i = 1; i <= depth; ++i)
                res.Append("<object id=\"" + i + "\" type=\"model\"><components><component objectid=\"" + (i + 1) +
                    "\"/><component objectid=\"" + (i + 1) + "\" transform=\"1 0 0 0 1 0 0 0 1 2 0 0\"/></components></object>");
            res.Append("<object id=\"" + (depth + 1) + "\" type=\"model\">" + TetMesh + "</object>");
            return Model(res.ToString(), "<item objectid=\"1\"/>");
        }

        [Fact]
        public void Read_ExponentialComponents_ReturnError()
        {
            // < 2 KB of XML that describes 2^40 tetrahedra: stopped by MaxElements instead of running out of time or memory
            // (a lower limit than the default keeps the test fast)
            byte[] package = MakePackage(DoublingComponents(40));
            Assert.True(package.Length < 2000);
            Assert.True(new ThreeMFReader().MaxElements < (1L << 40));
            IOReadResult r = default(IOReadResult);
            ThreeMFReader reader = new ThreeMFReader() { MaxElements = 1000000 };
            Assert.True(Timed.Finishes(() => { r = reader.Read(new MemoryStream(package), ReadOptions.Defaults, new DMesh3Builder()); }, TimeSpan.FromSeconds(60)), "reading did not finish");
            Assert.Equal(IOCode.FileParsingError, r.code);
            Assert.Contains("MaxElements", r.message);
        }

        [Fact]
        public void Read_SharedComponents_BelowLimit_AreExpanded()
        {
            // shared components are valid 3MF: 2^4 instances of the tetrahedron
            IOReadResult r;
            List<DMesh3> meshes = Read(MakePackage(DoublingComponents(4)), out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 m = Assert.Single(meshes);
            Assert.Equal(16 * 4, m.VertexCount);
            Assert.Equal(16 * 4, m.TriangleCount);
        }

        [Fact]
        public void Read_MaxElements_LimitsVerticesInTheModelPart()
        {
            // the limit also counts the vertices and triangles while the XML is parsed (zip bombs that expand to gigabytes of XML)
            StringBuilder mesh = new StringBuilder("<mesh><vertices>");
            for (int i = 0; i < 3000; ++i)
                mesh.Append("<vertex x=\"0\" y=\"0\" z=\"0\"/>");
            mesh.Append("</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh>");
            // object 2 is not used by a build item, so only the count during parsing can catch it
            byte[] package = MakePackage(Model("<object id=\"1\" type=\"model\">" + TetMesh + "</object>" +
                "<object id=\"2\" type=\"model\">" + mesh + "</object>", "<item objectid=\"1\"/>"));

            ThreeMFReader reader = new ThreeMFReader() { MaxElements = 2000 };
            IOReadResult r = reader.Read(new MemoryStream(package), ReadOptions.Defaults, new DMesh3Builder());
            Assert.Equal(IOCode.FileParsingError, r.code);
            Assert.Contains("MaxElements", r.message);

            reader = new ThreeMFReader();
            DMesh3Builder builder = new DMesh3Builder();
            r = reader.Read(new MemoryStream(package), ReadOptions.Defaults, builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(4, builder.Meshes[0].VertexCount);
        }

        [Fact]
        public void Read_MaxElements_FileAndResultAreCountedSeparately()
        {
            // one tetrahedron: 8 elements parsed, 1 + 8 appended; each count must fit MaxElements on its own
            byte[] package = MakePackage(Model("<object id=\"1\" type=\"model\">" + TetMesh + "</object>", "<item objectid=\"1\"/>"));
            IOReadResult r = new ThreeMFReader() { MaxElements = 9 }.Read(new MemoryStream(package), ReadOptions.Defaults, new DMesh3Builder());
            Assert.True(r.code == IOCode.Ok, r.message);
            r = new ThreeMFReader() { MaxElements = 8 }.Read(new MemoryStream(package), ReadOptions.Defaults, new DMesh3Builder());
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        [Fact]
        public void Read_RelationshipsWithDtd_ReturnsError()
        {
            // OPC parts must not contain DTDs (and entity expansion in them could be an XML bomb)
            string rels = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY t \"/3D/model.model\">]>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Target=\"&t;\" Id=\"r0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>";
            IOReadResult r;
            Read(MakePackage(Model("<object id=\"1\" type=\"model\">" + TetMesh + "</object>", "<item objectid=\"1\"/>"), rels), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        [Fact]
        public void Read_MissingObject_ReturnsError()
        {
            string model = Model("<object id=\"1\" type=\"model\">" + TetMesh + "</object>", "<item objectid=\"7\"/>");
            IOReadResult r;
            Read(MakePackage(model), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        [Fact]
        public void Read_NotAZip_ReturnsError()
        {
            IOReadResult r;
            Read(Encoding.ASCII.GetBytes("solid nope\nendsolid\n"), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        [Fact]
        public void Read_MalformedXml_ReturnsError()
        {
            IOReadResult r;
            Read(MakePackage("<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources>"), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
            Read(MakePackage(Model("<object id=\"1\" type=\"model\"><mesh><vertices><vertex x=\"a\" y=\"0\" z=\"0\"/></vertices></mesh></object>", "")), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        // ---------------------------------------------------------------- byte-reproducible packages (owner 2026-11-22)

        static byte[] WritePackage(DMesh3 mesh, bool combine = false)
        {
            var ms = new MemoryStream();
            WriteOptions options = WriteOptions.Defaults;
            options.bCombineMeshes = combine;
            IOWriteResult r = new ThreeMFWriter().Write(ms, new List<WriteMesh> { new WriteMesh(mesh, "part"), new WriteMesh(mesh, "copy") }, options);
            Assert.Equal(IOCode.Ok, r.code);
            return ms.ToArray();
        }

        /// <summary> the (time, date) fields of every local file header (PK 03 04: offsets 10, 12) and central directory entry (PK 01 02: offsets 12, 14) </summary>
        static List<(ushort time, ushort date)> ZipTimeStamps(byte[] zip)
        {
            var stamps = new List<(ushort, ushort)>();
            // the central directory lists every entry; its offset and size are in the end record (PK 05 06), the last 22 bytes without a comment
            int end = zip.Length - 22;
            Assert.True(zip[end] == 0x50 && zip[end + 1] == 0x4B && zip[end + 2] == 5 && zip[end + 3] == 6, "no end of central directory record");
            int count = BitConverter.ToUInt16(zip, end + 10), pos = BitConverter.ToInt32(zip, end + 16);
            for (int i = 0; i < count; ++i) {
                Assert.True(zip[pos] == 0x50 && zip[pos + 1] == 0x4B && zip[pos + 2] == 1 && zip[pos + 3] == 2, "no central directory entry");
                stamps.Add((BitConverter.ToUInt16(zip, pos + 12), BitConverter.ToUInt16(zip, pos + 14)));
                int local = BitConverter.ToInt32(zip, pos + 42);
                Assert.True(zip[local] == 0x50 && zip[local + 1] == 0x4B && zip[local + 2] == 3 && zip[local + 3] == 4, "no local file header");
                stamps.Add((BitConverter.ToUInt16(zip, local + 10), BitConverter.ToUInt16(zip, local + 12)));
                pos += 46 + BitConverter.ToUInt16(zip, pos + 28) + BitConverter.ToUInt16(zip, pos + 30) + BitConverter.ToUInt16(zip, pos + 32);
            }
            return stamps;
        }

        /// <summary>
        /// Every zip entry of a written package carries 1980-01-01 00:00:00 (DOS date 0x0021, time 0), not the time of the write: the
        /// time stamp fields are read from the raw headers, so the test does not depend on the clock, and through ZipArchive.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Write_EveryZipEntry_HasTheFixedTime(bool combine)
        {
            byte[] package = WritePackage(AttributeSphere(new Vector3d(1, 2, 3)), combine);
            List<(ushort time, ushort date)> stamps = ZipTimeStamps(package);
            Assert.Equal(6, stamps.Count);      // [Content_Types].xml, _rels/.rels, 3D/3dmodel.model: a central and a local header each
            foreach (var (time, date) in stamps) {
                Assert.Equal(0, time);
                Assert.Equal(0x0021, date);     // ((1980 - 1980) << 9) | (1 << 5) | 1
            }
            using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read)) {
                Assert.Equal(3, zip.Entries.Count);
                foreach (ZipArchiveEntry e in zip.Entries)
                    Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 0), e.LastWriteTime.DateTime);
            }
        }

        /// <summary>
        /// Two writes of the same meshes give the same bytes, the package still reads back as the same mesh bit for bit, and the read
        /// meshes write the same package again, for every member of Similarity.All(). (Two writes within one 2 s tick of the zip
        /// time stamp were the same bytes before the fixed time too: Write_EveryZipEntry_HasTheFixedTime is the test of the change.)
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Write_Twice_SameBytes_AndTheSameMeshBack(string similarity)
        {
            DMesh3 mesh = Invariance.Transformed(AttributeSphere(Vector3d.Zero), Similarity.ByName(similarity));
            byte[] first = WritePackage(mesh), second = WritePackage(mesh);
            Assert.True(first.SequenceEqual(second), "two writes of one mesh differ");
            IOReadResult r;
            List<DMesh3> back = Read(first, out r);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Equal(2, back.Count);
            foreach (DMesh3 b in back) {
                AssertSameTopology(mesh, b);
                Assert.Equal(0, MaxPositionError(mesh, b));
                Assert.True(b.IsClosed());
            }
            // a second generation: the read meshes write the same package again
            var again = new MemoryStream();
            Assert.Equal(IOCode.Ok, new ThreeMFWriter().Write(again, new List<WriteMesh> { new WriteMesh(back[0], "part"), new WriteMesh(back[1], "copy") }, WriteOptions.Defaults).code);
            Assert.True(first.SequenceEqual(again.ToArray()), "the package of the read meshes differs from the first");
        }
    }
}
