using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace g3
{
    /// <summary>
    /// Write 3MF (3D Manufacturing Format) files, core specification
    /// https://github.com/3MFConsortium/spec_core
    /// 
    /// Each mesh is written as one object (named by WriteMesh.Name) with one build item, without transform.
    /// If options.bCombineMeshes is set, all meshes are written as a single object instead.
    /// Vertex values are written as given (no scaling), and the model is labelled with Unit (default millimeter).
    /// Only geometry is written: no normals, colors, UVs or materials.
    /// StandardMeshWriter always uses the default Unit; create a ThreeMFWriter to use another one.
    /// </summary>
    public class ThreeMFWriter
    {
        /// <summary>
        /// 3MF unit: micron, millimeter, centimeter, inch, foot or meter
        /// </summary>
        public string Unit = "millimeter";


        /// <summary>
        /// Write the meshes as a 3MF zip package to stream. The stream is left open.
        /// Returns Ok; exceptions (eg from a non-writable stream) are not caught here.
        /// </summary>
        public IOWriteResult Write(Stream stream, List<WriteMesh> vMeshes, WriteOptions options)
        {
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
                write_text_entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n" +
                    "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n" +
                    "  <Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/>\n" +
                    "</Types>\n");
                write_text_entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
                    "  <Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/>\n" +
                    "</Relationships>\n");

                ZipArchiveEntry model = zip.CreateEntry("3D/3dmodel.model", CompressionLevel.Optimal);
                using (Stream s = model.Open()) {
                    XmlWriterSettings settings = new XmlWriterSettings() { Indent = true, Encoding = new UTF8Encoding(false) };
                    using (XmlWriter xml = XmlWriter.Create(s, settings))
                        write_model(xml, vMeshes, options);
                }
            }
            return IOWriteResult.Ok;
        }


        static void write_text_entry(ZipArchive zip, string name, string text)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = entry.Open()) {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            }
        }


        void write_model(XmlWriter xml, List<WriteMesh> vMeshes, WriteOptions options)
        {
            string ns = ThreeMFReader.CoreNamespace;
            CultureInfo ci = CultureInfo.InvariantCulture;

            xml.WriteStartDocument();
            xml.WriteStartElement("model", ns);
            xml.WriteAttributeString("unit", Unit);
            xml.WriteAttributeString("xml", "lang", null, "en-US");
            xml.WriteStartElement("metadata", ns);
            xml.WriteAttributeString("name", "Application");
            xml.WriteString("geometry3Sharp");
            xml.WriteEndElement();

            int nObjects = options.bCombineMeshes ? Math.Min(1, vMeshes.Count) : vMeshes.Count;
            xml.WriteStartElement("resources", ns);
            for (int oi = 0; oi < nObjects; ++oi) {
                xml.WriteStartElement("object", ns);
                xml.WriteAttributeString("id", (oi + 1).ToString(ci));
                xml.WriteAttributeString("type", "model");
                if (options.bCombineMeshes == false && string.IsNullOrEmpty(vMeshes[oi].Name) == false)
                    xml.WriteAttributeString("name", vMeshes[oi].Name);
                xml.WriteStartElement("mesh", ns);

                List<WriteMesh> meshes = options.bCombineMeshes ? vMeshes : new List<WriteMesh>() { vMeshes[oi] };
                xml.WriteStartElement("vertices", ns);
                List<int[]> maps = new List<int[]>();
                int vi = 0;
                for (int mi = 0; mi < meshes.Count; ++mi) {
                    IMesh mesh = meshes[mi].Mesh;
                    if (options.ProgressFunc != null)
                        options.ProgressFunc(oi, nObjects);
                    int[] map = new int[mesh.MaxVertexID];
                    foreach (int vid in mesh.VertexIndices()) {
                        Vector3d v = mesh.GetVertex(vid);
                        xml.WriteStartElement("vertex", ns);
                        xml.WriteAttributeString("x", v.x.ToString("R", ci));
                        xml.WriteAttributeString("y", v.y.ToString("R", ci));
                        xml.WriteAttributeString("z", v.z.ToString("R", ci));
                        xml.WriteEndElement();
                        map[vid] = vi++;
                    }
                    maps.Add(map);
                }
                xml.WriteEndElement();

                xml.WriteStartElement("triangles", ns);
                for (int mi = 0; mi < meshes.Count; ++mi) {
                    IMesh mesh = meshes[mi].Mesh;
                    int[] map = maps[mi];
                    foreach (int tid in mesh.TriangleIndices()) {
                        Index3i t = mesh.GetTriangle(tid);
                        xml.WriteStartElement("triangle", ns);
                        xml.WriteAttributeString("v1", map[t.a].ToString(ci));
                        xml.WriteAttributeString("v2", map[t.b].ToString(ci));
                        xml.WriteAttributeString("v3", map[t.c].ToString(ci));
                        xml.WriteEndElement();
                    }
                }
                xml.WriteEndElement();

                xml.WriteEndElement();  // mesh
                xml.WriteEndElement();  // object
            }
            xml.WriteEndElement();  // resources

            xml.WriteStartElement("build", ns);
            for (int oi = 0; oi < nObjects; ++oi) {
                xml.WriteStartElement("item", ns);
                xml.WriteAttributeString("objectid", (oi + 1).ToString(ci));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();  // build

            xml.WriteEndElement();  // model
            xml.WriteEndDocument();
        }
    }
}
