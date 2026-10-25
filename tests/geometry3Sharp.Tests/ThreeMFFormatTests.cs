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
    }
}
