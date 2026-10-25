using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace g3
{
    //
    // Parse PLY (Stanford polygon) mesh format, ascii and binary (little or big endian)
    // http://paulbourke.net/dataformats/ply/
    //
    // Reads the vertex element (x,y,z and optionally normals, colors and UVs) and
    // the face element (vertex_indices or vertex_index list). Polygons are fan-triangulated.
    // Files without faces are read as point sets (a mesh with only vertices).
    // Other elements (edges, materials, ...) are skipped.
    //
    public class PLYReader : IMeshReader
    {
        // connect to this to get warning messages
        public event ParsingMessagesHandler warningEvent;

        Dictionary<string, int> warningCount = new Dictionary<string, int>();


        enum PlyFormat { Ascii, BinaryLittleEndian, BinaryBigEndian }

        enum PlyType { Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64 }

        class PlyProperty
        {
            public string Name;
            public PlyType Type;
            public bool IsList;
            public PlyType CountType;
        }

        class PlyElement
        {
            public string Name;
            public int Count;
            public List<PlyProperty> Properties = new List<PlyProperty>();

            public int Find(params string[] names)
            {
                foreach (string name in names) {
                    for (int i = 0; i < Properties.Count; ++i) {
                        if (Properties[i].IsList == false && Properties[i].Name == name)
                            return i;
                    }
                }
                return -1;
            }
        }


        /// <summary>
        /// Read ascii PLY from a TextReader. Use the BinaryReader version for binary PLY.
        /// </summary>
        public IOReadResult Read(TextReader reader, ReadOptions options, IMeshBuilder builder)
        {
            List<string> header = new List<string>();
            while (true) {
                string line = reader.ReadLine();
                if (line == null)
                    return new IOReadResult(IOCode.FileParsingError, "PLY header has no end_header line");
                line = line.Trim();
                header.Add(line);
                if (line == "end_header")
                    break;
            }

            List<PlyElement> elements;
            PlyFormat format;
            IOReadResult result = parse_header(header, out elements, out format);
            if (result.code != IOCode.Ok)
                return result;
            if (format != PlyFormat.Ascii)
                return new IOReadResult(IOCode.FormatNotSupportedError, "binary PLY must be read with a BinaryReader");

            AsciiTokenizer tokens = new AsciiTokenizer(reader);
            return read_body(elements, (type) => tokens.NextDouble(), builder);
        }


        /// <summary>
        /// Read ascii or binary PLY. The header is parsed from the stream bytes, so this works for all formats.
        /// </summary>
        public IOReadResult Read(BinaryReader reader, ReadOptions options, IMeshBuilder builder)
        {
            List<string> header = new List<string>();
            StringBuilder line = new StringBuilder();
            while (true) {
                int b = reader.BaseStream.ReadByte();
                if (b < 0)
                    return new IOReadResult(IOCode.FileParsingError, "PLY header has no end_header line");
                if (b == '\n') {
                    string s = line.ToString().Trim();
                    header.Add(s);
                    line.Clear();
                    if (s == "end_header")
                        break;
                    if (header.Count == 1 && s != "ply")
                        return new IOReadResult(IOCode.FileParsingError, "PLY file must start with ply");
                } else {
                    line.Append((char)b);
                    if (line.Length > 100000)
                        return new IOReadResult(IOCode.FileParsingError, "PLY header line is too long, file is probably not PLY");
                }
            }

            List<PlyElement> elements;
            PlyFormat format;
            IOReadResult result = parse_header(header, out elements, out format);
            if (result.code != IOCode.Ok)
                return result;

            if (format == PlyFormat.Ascii) {
                AsciiTokenizer tokens = new AsciiTokenizer(new StreamReader(reader.BaseStream, Encoding.ASCII));
                return read_body(elements, (type) => tokens.NextDouble(), builder);
            }

            BinaryValueReader values = new BinaryValueReader(reader, format == PlyFormat.BinaryBigEndian);
            return read_body(elements, (type) => values.Read(type), builder);
        }



        IOReadResult parse_header(List<string> lines, out List<PlyElement> elements, out PlyFormat format)
        {
            elements = new List<PlyElement>();
            format = PlyFormat.Ascii;
            if (lines.Count == 0 || lines[0] != "ply")
                return new IOReadResult(IOCode.FileParsingError, "PLY file must start with ply");

            bool bHaveFormat = false;
            PlyElement cur = null;
            for (int li = 1; li < lines.Count; ++li) {
                string[] tokens = lines[li].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;
                switch (tokens[0]) {
                    case "format":
                        if (tokens.Length < 2)
                            return new IOReadResult(IOCode.FileParsingError, "invalid PLY format line: " + lines[li]);
                        if (tokens[1] == "ascii")
                            format = PlyFormat.Ascii;
                        else if (tokens[1] == "binary_little_endian")
                            format = PlyFormat.BinaryLittleEndian;
                        else if (tokens[1] == "binary_big_endian")
                            format = PlyFormat.BinaryBigEndian;
                        else
                            return new IOReadResult(IOCode.FormatNotSupportedError, "unknown PLY format " + tokens[1]);
                        bHaveFormat = true;
                        break;

                    case "element":
                        int count;
                        if (tokens.Length < 3 || int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) == false || count < 0)
                            return new IOReadResult(IOCode.FileParsingError, "invalid PLY element line: " + lines[li]);
                        cur = new PlyElement() { Name = tokens[1], Count = count };
                        elements.Add(cur);
                        break;

                    case "property":
                        if (cur == null)
                            return new IOReadResult(IOCode.FileParsingError, "PLY property before first element: " + lines[li]);
                        PlyProperty prop = new PlyProperty();
                        bool bTypesOk;
                        if (tokens.Length >= 5 && tokens[1] == "list") {
                            prop.IsList = true;
                            bTypesOk = parse_type(tokens[2], out prop.CountType) & parse_type(tokens[3], out prop.Type);
                            prop.Name = tokens[4];
                        } else if (tokens.Length >= 3) {
                            bTypesOk = parse_type(tokens[1], out prop.Type);
                            prop.Name = tokens[2];
                        } else
                            return new IOReadResult(IOCode.FileParsingError, "invalid PLY property line: " + lines[li]);
                        if (bTypesOk == false)
                            return new IOReadResult(IOCode.FileParsingError, "unknown PLY property type: " + lines[li]);
                        cur.Properties.Add(prop);
                        break;

                    case "comment":
                    case "obj_info":
                    case "end_header":
                        break;

                    default:
                        emit_warning("unknown PLY header line: " + lines[li]);
                        break;
                }
            }
            if (bHaveFormat == false)
                return new IOReadResult(IOCode.FileParsingError, "PLY header has no format line");
            return IOReadResult.Ok;
        }


        static bool parse_type(string s, out PlyType type)
        {
            switch (s) {
                case "char": case "int8": type = PlyType.Int8; return true;
                case "uchar": case "uint8": type = PlyType.UInt8; return true;
                case "short": case "int16": type = PlyType.Int16; return true;
                case "ushort": case "uint16": type = PlyType.UInt16; return true;
                case "int": case "int32": type = PlyType.Int32; return true;
                case "uint": case "uint32": type = PlyType.UInt32; return true;
                case "float": case "float32": type = PlyType.Float32; return true;
                case "double": case "float64": type = PlyType.Float64; return true;
            }
            type = PlyType.Float32;
            return false;
        }


        // readValue reads the next value of the given type (ascii files ignore the type)
        IOReadResult read_body(List<PlyElement> elements, Func<PlyType, double> readValue, IMeshBuilder builder)
        {
            try {
                return read_elements(elements, readValue, builder);
            } catch (Exception e) {
                return new IOReadResult(IOCode.FileParsingError, "error reading PLY data: " + e.Message);
            }
        }

        IOReadResult read_elements(List<PlyElement> elements, Func<PlyType, double> readValue, IMeshBuilder builder)
        {
            PlyElement vertexElem = elements.Find((e) => e.Name == "vertex");
            if (vertexElem == null)
                return new IOReadResult(IOCode.FileParsingError, "PLY file has no vertex element");

            int ix = vertexElem.Find("x"), iy = vertexElem.Find("y"), iz = vertexElem.Find("z");
            if (ix < 0 || iy < 0 || iz < 0)
                return new IOReadResult(IOCode.FileParsingError, "PLY vertex element must have x, y and z properties");
            int inx = vertexElem.Find("nx"), iny = vertexElem.Find("ny"), inz = vertexElem.Find("nz");
            int ir = vertexElem.Find("red", "diffuse_red", "r"), ig = vertexElem.Find("green", "diffuse_green", "g"), ib = vertexElem.Find("blue", "diffuse_blue", "b");
            int iu = vertexElem.Find("s", "u", "texture_u", "texture_s"), iv = vertexElem.Find("t", "v", "texture_v", "texture_t");
            bool bNormals = inx >= 0 && iny >= 0 && inz >= 0;
            bool bColors = ir >= 0 && ig >= 0 && ib >= 0;
            bool bUVs = iu >= 0 && iv >= 0;

            builder.AppendNewMesh(bNormals, bColors, bUVs, false);

            int[] vertexMap = new int[vertexElem.Count];
            double[] values = new double[vertexElem.Properties.Count];
            List<int> polygon = new List<int>();
            int nSkippedFaces = 0;

            foreach (PlyElement elem in elements) {
                if (elem == vertexElem) {
                    foreach (PlyProperty p in elem.Properties) {
                        if (p.IsList)
                            return new IOReadResult(IOCode.FormatNotSupportedError, "list properties in PLY vertex element are not supported");
                    }
                    for (int vi = 0; vi < elem.Count; ++vi) {
                        for (int pi = 0; pi < elem.Properties.Count; ++pi)
                            values[pi] = readValue(elem.Properties[pi].Type);

                        NewVertexInfo info = new NewVertexInfo(new Vector3d(values[ix], values[iy], values[iz]));
                        if (bNormals) {
                            info.bHaveN = true;
                            info.n = new Vector3f(values[inx], values[iny], values[inz]);
                        }
                        if (bColors) {
                            info.bHaveC = true;
                            info.c = new Vector3f(color_value(elem.Properties[ir].Type, values[ir]),
                                color_value(elem.Properties[ig].Type, values[ig]), color_value(elem.Properties[ib].Type, values[ib]));
                        }
                        if (bUVs) {
                            info.bHaveUV = true;
                            info.uv = new Vector2f(values[iu], values[iv]);
                        }
                        vertexMap[vi] = builder.AppendVertex(info);
                    }

                } else if (elem.Name == "face") {
                    int iIndices = elem.Properties.FindIndex((p) => p.IsList && (p.Name == "vertex_indices" || p.Name == "vertex_index"));
                    if (iIndices < 0)
                        return new IOReadResult(IOCode.FileParsingError, "PLY face element has no vertex_indices list");
                    for (int fi = 0; fi < elem.Count; ++fi) {
                        for (int pi = 0; pi < elem.Properties.Count; ++pi) {
                            PlyProperty p = elem.Properties[pi];
                            if (p.IsList == false) {
                                readValue(p.Type);
                                continue;
                            }
                            int n = (int)readValue(p.CountType);
                            if (n < 0)
                                return new IOReadResult(IOCode.FileParsingError, "negative PLY list length");
                            if (pi == iIndices)
                                polygon.Clear();
                            for (int k = 0; k < n; ++k) {
                                double value = readValue(p.Type);
                                if (pi == iIndices)
                                    polygon.Add((int)value);
                            }
                        }
                        if (append_polygon(polygon, vertexMap, builder) == false)
                            nSkippedFaces++;
                    }

                } else {
                    // skip unknown element
                    for (int i = 0; i < elem.Count; ++i) {
                        foreach (PlyProperty p in elem.Properties) {
                            if (p.IsList) {
                                int n = (int)readValue(p.CountType);
                                for (int k = 0; k < n; ++k)
                                    readValue(p.Type);
                            } else
                                readValue(p.Type);
                        }
                    }
                }
            }

            if (nSkippedFaces > 0)
                emit_warning(string.Format("skipped {0} PLY faces with fewer than 3 or invalid vertex indices", nSkippedFaces));
            return IOReadResult.Ok;
        }


        static float color_value(PlyType type, double value)
        {
            switch (type) {
                case PlyType.UInt8: return (float)(value / 255.0);
                case PlyType.UInt16: return (float)(value / 65535.0);
                default: return (float)value;
            }
        }


        // fan-triangulate polygon, returns false if polygon was invalid
        static bool append_polygon(List<int> polygon, int[] vertexMap, IMeshBuilder builder)
        {
            if (polygon.Count < 3)
                return false;
            foreach (int idx in polygon) {
                if (idx < 0 || idx >= vertexMap.Length)
                    return false;
            }
            int a = vertexMap[polygon[0]];
            for (int k = 1; k + 1 < polygon.Count; ++k) {
                int b = vertexMap[polygon[k]], c = vertexMap[polygon[k + 1]];
                if (a != b && b != c && a != c)
                    builder.AppendTriangle(a, b, c);
            }
            return true;
        }



        class AsciiTokenizer
        {
            TextReader reader;
            StringBuilder token = new StringBuilder();

            public AsciiTokenizer(TextReader reader)
            {
                this.reader = reader;
            }

            public double NextDouble()
            {
                token.Clear();
                int ch;
                while ((ch = reader.Read()) >= 0 && char.IsWhiteSpace((char)ch))
                    ;
                while (ch >= 0 && char.IsWhiteSpace((char)ch) == false) {
                    token.Append((char)ch);
                    ch = reader.Read();
                }
                if (token.Length == 0)
                    throw new EndOfStreamException("unexpected end of ascii PLY data");
                return double.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }


        class BinaryValueReader
        {
            BinaryReader reader;
            bool swap;      // file byte order differs from machine byte order
            byte[] buf = new byte[8];

            public BinaryValueReader(BinaryReader reader, bool bigEndian)
            {
                this.reader = reader;
                swap = bigEndian == BitConverter.IsLittleEndian;
            }

            void fill(int n)
            {
                int read = 0;
                while (read < n) {
                    int k = reader.Read(buf, read, n - read);
                    if (k <= 0)
                        throw new EndOfStreamException("unexpected end of binary PLY data");
                    read += k;
                }
                if (swap)
                    Array.Reverse(buf, 0, n);
            }

            public double Read(PlyType type)
            {
                switch (type) {
                    case PlyType.Int8: fill(1); return (sbyte)buf[0];
                    case PlyType.UInt8: fill(1); return buf[0];
                    case PlyType.Int16: fill(2); return BitConverter.ToInt16(buf, 0);
                    case PlyType.UInt16: fill(2); return BitConverter.ToUInt16(buf, 0);
                    case PlyType.Int32: fill(4); return BitConverter.ToInt32(buf, 0);
                    case PlyType.UInt32: fill(4); return BitConverter.ToUInt32(buf, 0);
                    case PlyType.Float32: fill(4); return BitConverter.ToSingle(buf, 0);
                    default: fill(8); return BitConverter.ToDouble(buf, 0);
                }
            }
        }


        private void emit_warning(string sMessage)
        {
            string sPrefix = sMessage.Substring(0, Math.Min(15, sMessage.Length));
            int nCount = warningCount.ContainsKey(sPrefix) ? warningCount[sPrefix] : 0;
            nCount++; warningCount[sPrefix] = nCount;
            if (nCount > 10)
                return;
            else if (nCount == 10)
                sMessage += " (additional message surpressed)";

            var e = warningEvent;
            if (e != null)
                e(sMessage, null);
        }
    }
}
