using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace g3
{
    /// <summary>
    /// Read 3MF (3D Manufacturing Format) files, core specification
    /// https://github.com/3MFConsortium/spec_core
    /// 
    /// Each build item becomes one mesh (with the object name as "name" metadata, if the builder supports it).
    /// Components and item/component transforms are applied to the vertices, and objects in other model
    /// parts (production extension "path" attribute) are followed. Vertices are converted to millimeters unless
    /// ConvertToMillimeters is false. Invalid triangles are skipped with a warning.
    /// Materials, colors and extensions (beam lattices, slices, ...) are ignored. ReadOptions are ignored.
    /// Errors (not a zip package, missing model part, bad XML or numbers, result larger than <see cref="MaxElements"/>) are returned as
    /// <see cref="IOCode.FileParsingError"/>, exceptions are caught. A file without build items reads Ok with no meshes.
    /// The stream is left open.
    /// </summary>
    public class ThreeMFReader
    {
        /// <summary> XML namespace of the 3MF core specification </summary>
        public const string CoreNamespace = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
        const string RelsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string StartPartType = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";

        /// <summary> connect to this to get warning messages </summary>
        public event ParsingMessagesHandler warningEvent;

        /// <summary>
        /// If true (default), vertices are scaled from the file unit to millimeters
        /// </summary>
        public bool ConvertToMillimeters = true;

        /// <summary>
        /// The unit attribute of the last model that was read (3MF default is millimeter): micron, millimeter,
        /// centimeter, inch, foot or meter. Unknown units are not scaled.
        /// </summary>
        public string FileUnit = "millimeter";

        /// <summary>
        /// Limit on the size of the file and of the result (default 50 million, ie a DMesh3 of a few GB): the vertices
        /// and triangles read from the model parts may not exceed it, and neither may the vertices, triangles and
        /// object instances appended after expanding build items and components.
        /// Components may reference the same object several times, so a small file can describe an exponentially
        /// large mesh, and a zip bomb can hold gigabytes of XML. If the limit is exceeded, Read returns
        /// <see cref="IOCode.FileParsingError"/>.
        /// </summary>
        public long MaxElements = 50000000;

        long nParsed, nAppended;


        class Mesh3MF
        {
            public List<Vector3d> Vertices = new List<Vector3d>();
            public List<Index3i> Triangles = new List<Index3i>();
        }

        class Component3MF
        {
            public string Path;     // model part that holds the object
            public int ObjectID;
            public Matrix4x3 Transform;
        }

        class Object3MF
        {
            public string Name;
            public Mesh3MF Mesh;
            public List<Component3MF> Components = new List<Component3MF>();
        }

        // affine transform x' = R*x + T, converted from the 3MF row-vector form
        struct Matrix4x3
        {
            public Matrix3d R;
            public Vector3d T;
            public static readonly Matrix4x3 Identity = new Matrix4x3() { R = Matrix3d.Identity, T = Vector3d.Zero };

            public Vector3d Apply(Vector3d v)
            {
                return R * v + T;
            }

            // this applied first, then other
            public Matrix4x3 Then(Matrix4x3 other)
            {
                return new Matrix4x3() { R = other.R * R, T = other.R * T + other.T };
            }
        }


        /// <summary>
        /// Read a 3MF package from stream and append one mesh per build item to builder. Does not throw.
        /// </summary>
        public IOReadResult Read(Stream stream, ReadOptions options, IMeshBuilder builder)
        {
            readOptions = options;
            try {
                return read(stream, builder);
            } catch (Exception e) {
                return new IOReadResult(IOCode.FileParsingError, "error reading 3MF: " + e.Message);
            }
        }


        ReadOptions readOptions;

        IOReadResult read(Stream stream, IMeshBuilder builder)
        {
            nParsed = nAppended = 0;
            ZipArchive zip;
            try {
                zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
            } catch (Exception e) {
                return new IOReadResult(IOCode.FileParsingError, "3MF file is not a valid zip package: " + e.Message);
            }

            using (zip) {
                string startPart = find_start_part(zip);
                if (startPart == null)
                    return new IOReadResult(IOCode.FileParsingError, "3MF package has no 3D model part");

                Dictionary<string, Dictionary<int, Object3MF>> parts = new Dictionary<string, Dictionary<int, Object3MF>>();
                List<Component3MF> buildItems = new List<Component3MF>();
                string unit;
                IOReadResult result = read_model_part(zip, startPart, parts, buildItems, out unit);
                if (result.code != IOCode.Ok)
                    return result;
                FileUnit = unit;
                double scale = ConvertToMillimeters ? unit_to_mm(unit) : 1.0;

                int nMeshes = 0;
                foreach (Component3MF item in buildItems) {
                    Object3MF obj = find_object(zip, parts, item.Path, item.ObjectID, out result);
                    if (obj == null)
                        return result;

                    builder.AppendNewMesh(false, false, false, false);
                    if (obj.Name != null && builder.SupportsMetaData)
                        builder.AppendMetaData("name", obj.Name);
                    result = append_object(zip, parts, obj, item.Path, item.Transform, scale, builder, 0);
                    if (result.code != IOCode.Ok)
                        return result;
                    nMeshes++;
                }
                if (nMeshes == 0)
                    emit_warning("3MF file has no build items");
            }
            return IOReadResult.Ok;
        }


        static double unit_to_mm(string unit)
        {
            switch (unit) {
                case "micron": return 0.001;
                case "centimeter": return 10.0;
                case "inch": return 25.4;
                case "foot": return 304.8;
                case "meter": return 1000.0;
                default: return 1.0;
            }
        }


        static string normalize_path(string path)
        {
            return path.TrimStart('/');
        }


        static string find_start_part(ZipArchive zip)
        {
            ZipArchiveEntry rels = zip.GetEntry("_rels/.rels");
            if (rels != null) {
                // OPC parts must not contain DTDs; prohibiting them also rules out entity expansion bombs
                XmlDocument doc = new XmlDocument() { XmlResolver = null };
                XmlReaderSettings settings = new XmlReaderSettings() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (Stream s = rels.Open())
                using (XmlReader xml = XmlReader.Create(s, settings))
                    doc.Load(xml);
                foreach (XmlNode node in doc.GetElementsByTagName("Relationship", RelsNamespace)) {
                    XmlElement elem = node as XmlElement;
                    if (elem != null && elem.GetAttribute("Type") == StartPartType)
                        return normalize_path(elem.GetAttribute("Target"));
                }
            }
            // fallback for packages without relationships
            if (zip.GetEntry("3D/3dmodel.model") != null)
                return "3D/3dmodel.model";
            return null;
        }


        IOReadResult read_model_part(ZipArchive zip, string path, Dictionary<string, Dictionary<int, Object3MF>> parts,
            List<Component3MF> buildItems, out string unit)
        {
            unit = "millimeter";
            ZipArchiveEntry entry = zip.GetEntry(path);
            if (entry == null)
                return new IOReadResult(IOCode.FileParsingError, "3MF package has no part " + path);

            Dictionary<int, Object3MF> objects = new Dictionary<int, Object3MF>();
            parts[path] = objects;

            XmlReaderSettings settings = new XmlReaderSettings() { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit };
            using (Stream s = entry.Open())
            using (XmlReader xml = XmlReader.Create(s, settings)) {
                Object3MF cur = null;
                while (xml.Read()) {
                    if (xml.NodeType != XmlNodeType.Element || xml.NamespaceURI != CoreNamespace)
                        continue;
                    switch (xml.LocalName) {
                        case "model":
                            string u = xml.GetAttribute("unit");
                            if (string.IsNullOrEmpty(u) == false)
                                unit = u;
                            break;

                        case "object":
                            int id;
                            if (int.TryParse(xml.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) == false)
                                return new IOReadResult(IOCode.FileParsingError, "3MF object without valid id");
                            cur = new Object3MF() { Name = xml.GetAttribute("name") };
                            objects[id] = cur;
                            break;

                        case "mesh":
                            if (cur != null)
                                cur.Mesh = new Mesh3MF();
                            break;

                        case "vertex":
                            if (cur != null && cur.Mesh != null && add_parsed() == false)
                                return too_large();
                            if (cur != null && cur.Mesh != null)
                                cur.Mesh.Vertices.Add(new Vector3d(parse_double(xml, "x"), parse_double(xml, "y"), parse_double(xml, "z")));
                            break;

                        case "triangle":
                            if (cur != null && cur.Mesh != null && add_parsed() == false)
                                return too_large();
                            if (cur != null && cur.Mesh != null)
                                cur.Mesh.Triangles.Add(new Index3i(parse_int(xml, "v1"), parse_int(xml, "v2"), parse_int(xml, "v3")));
                            break;

                        case "component":
                            if (cur != null)
                                cur.Components.Add(parse_reference(xml, path));
                            break;

                        case "item":
                            if (buildItems != null)
                                buildItems.Add(parse_reference(xml, path));
                            break;
                    }
                }
            }
            return IOReadResult.Ok;
        }


        Component3MF parse_reference(XmlReader xml, string currentPath)
        {
            Component3MF c = new Component3MF() { Path = currentPath, Transform = Matrix4x3.Identity };
            c.ObjectID = parse_int(xml, "objectid");
            string transform = xml.GetAttribute("transform");
            if (string.IsNullOrEmpty(transform) == false)
                c.Transform = parse_transform(transform);
            // production extension: object can be in another model part
            for (int i = 0; i < xml.AttributeCount; ++i) {
                xml.MoveToAttribute(i);
                if (xml.LocalName == "path" && xml.NamespaceURI != CoreNamespace && xml.NamespaceURI.Length > 0)
                    c.Path = normalize_path(xml.Value);
            }
            xml.MoveToElement();
            return c;
        }


        static Matrix4x3 parse_transform(string s)
        {
            string[] tokens = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 12)
                throw new FormatException("3MF transform must have 12 values: " + s);
            double[] m = new double[12];
            for (int i = 0; i < 12; ++i)
                m[i] = double.Parse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture);
            // 3MF: [x y z 1] * [m00 m01 m02; m10 m11 m12; m20 m21 m22; m30 m31 m32]
            // so x' = m00*x + m10*y + m20*z + m30, ie the column-vector matrix is the transpose
            return new Matrix4x3() {
                R = new Matrix3d(m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]),
                T = new Vector3d(m[9], m[10], m[11])
            };
        }


        Object3MF find_object(ZipArchive zip, Dictionary<string, Dictionary<int, Object3MF>> parts, string path, int id, out IOReadResult result)
        {
            result = IOReadResult.Ok;
            if (parts.ContainsKey(path) == false) {
                string unit;
                result = read_model_part(zip, path, parts, null, out unit);
                if (result.code != IOCode.Ok)
                    return null;
            }
            Object3MF obj;
            if (parts[path].TryGetValue(id, out obj))
                return obj;
            result = new IOReadResult(IOCode.FileParsingError, string.Format("3MF object {0} not found in {1}", id, path));
            return null;
        }


        IOReadResult append_object(ZipArchive zip, Dictionary<string, Dictionary<int, Object3MF>> parts, Object3MF obj,
            string path, Matrix4x3 transform, double scale, IMeshBuilder builder, int depth)
        {
            if (depth > 64)
                return new IOReadResult(IOCode.FileParsingError, "3MF components are nested too deeply (cyclic reference?)");
            if (add_appended(1 + (obj.Mesh != null ? obj.Mesh.Vertices.Count + obj.Mesh.Triangles.Count : 0)) == false)
                return too_large();

            if (obj.Mesh != null) {
                Mesh3MF mesh = obj.Mesh;
                int[] map = new int[mesh.Vertices.Count];
                for (int i = 0; i < mesh.Vertices.Count; ++i) {
                    Vector3d p = scale * transform.Apply(mesh.Vertices[i]);
                    if (ReadOptions.RejectsPosition(readOptions, p.x, p.y, p.z))
                        return ReadOptions.NonFinitePosition(p.x, p.y, p.z);
                    map[i] = builder.AppendVertex(new NewVertexInfo(p));
                }
                int nInvalid = 0;
                foreach (Index3i t in mesh.Triangles) {
                    if (t.a < 0 || t.b < 0 || t.c < 0 || t.a >= map.Length || t.b >= map.Length || t.c >= map.Length || t.a == t.b || t.b == t.c || t.a == t.c) {
                        nInvalid++;
                        continue;
                    }
                    builder.AppendTriangle(map[t.a], map[t.b], map[t.c]);
                }
                if (nInvalid > 0)
                    emit_warning(string.Format("skipped {0} invalid 3MF triangles", nInvalid));
            }

            foreach (Component3MF c in obj.Components) {
                IOReadResult result;
                Object3MF child = find_object(zip, parts, c.Path, c.ObjectID, out result);
                if (child == null)
                    return result;
                result = append_object(zip, parts, child, c.Path, c.Transform.Then(transform), scale, builder, depth + 1);
                if (result.code != IOCode.Ok)
                    return result;
            }
            return IOReadResult.Ok;
        }


        // count one parsed vertex or triangle against MaxElements, false if the limit is exceeded
        bool add_parsed()
        {
            return ++nParsed <= MaxElements;
        }

        // count n appended elements against MaxElements, false if the limit is exceeded
        bool add_appended(long n)
        {
            nAppended += n;
            return nAppended <= MaxElements;
        }

        IOReadResult too_large()
        {
            return new IOReadResult(IOCode.FileParsingError,
                string.Format("3MF file expands to more than {0} vertices, triangles and object instances (MaxElements)", MaxElements));
        }


        static double parse_double(XmlReader xml, string name)
        {
            string value = xml.GetAttribute(name);
            if (value == null)
                throw new FormatException("3MF: attribute " + name + " is missing");
            return MeshIOUtil.ParseDouble(value);
        }

        static int parse_int(XmlReader xml, string name)
        {
            return int.Parse(xml.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }


        private void emit_warning(string sMessage)
        {
            var e = warningEvent;
            if (e != null)
                e(sMessage, null);
        }
    }
}
