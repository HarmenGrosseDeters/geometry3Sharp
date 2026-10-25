using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace g3
{
    /// <summary>
    /// Read glTF 2.0 files, as .gltf (JSON) or .glb (binary container); the variant is detected from the data.
    /// https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
    /// 
    /// Every node of the default scene that references a mesh becomes one mesh (if there is no scene, all root
    /// nodes are used; if there are no nodes, every glTF mesh is read once). Node transforms (matrix or TRS,
    /// including parents) are applied to positions and normals unless ApplyNodeTransforms is false.
    /// All primitives of a glTF mesh are combined; if there is more than one primitive,
    /// the primitive index is stored as face group. The glTF mesh name is stored as "name" metadata if the
    /// builder supports it. Reads POSITION, NORMAL, COLOR_0 (RGB) and TEXCOORD_0, including sparse and
    /// normalized-integer accessors. Triangle lists, strips and fans are read; point and line primitives are skipped.
    /// Materials, textures, skins and animations are ignored. Coordinates are not converted
    /// (glTF is Y-up, in meters). Draco and meshopt compression are not supported (FormatNotSupportedError).
    /// Buffers can be the GLB BIN chunk, base64 data URIs, or external files loaded through ResolveUri.
    /// Errors are returned as an <see cref="IOReadResult"/> code, exceptions are caught.
    /// </summary>
    public class GLTFReader
    {
        /// <summary> connect to this to get warning messages </summary>
        public event ParsingMessagesHandler warningEvent;

        /// <summary>
        /// Resolves external buffer URIs (relative paths, already URI-unescaped) to bytes. When reading from a file,
        /// StandardMeshReader sets this to load files next to the .gltf file. If null (default), files with
        /// external buffers fail with <see cref="IOCode.FileAccessError"/>.
        /// </summary>
        public Func<string, byte[]> ResolveUri = null;

        /// <summary>
        /// If true (default), node transforms are applied to the vertices
        /// </summary>
        public bool ApplyNodeTransforms = true;


        const uint GLBMagic = 0x46546C67;     // "glTF"
        const uint ChunkJSON = 0x4E4F534A;
        const uint ChunkBIN = 0x004E4942;

        Dictionary<string, object> root;
        List<byte[]> buffers;

        // affine transform x' = Linear*x + T
        struct Affine3d
        {
            public Matrix3d Linear;
            public Vector3d T;
            public static readonly Affine3d Identity = new Affine3d(Matrix3d.Identity, Vector3d.Zero);

            public Affine3d(Matrix3d linear, Vector3d t)
            {
                Linear = linear; T = t;
            }

            // column-major 4x4 matrix as stored in glTF
            public static Affine3d FromColumnMajor(double[] m)
            {
                return new Affine3d(new Matrix3d(m[0], m[4], m[8], m[1], m[5], m[9], m[2], m[6], m[10]), new Vector3d(m[12], m[13], m[14]));
            }

            public Vector3d TransformPoint(Vector3d v)
            {
                return Linear * v + T;
            }

            // apply b first, then a
            public static Affine3d operator *(Affine3d a, Affine3d b)
            {
                return new Affine3d(a.Linear * b.Linear, a.Linear * b.T + a.T);
            }
        }

        class Primitive
        {
            public Vector3d[] Positions;
            public Vector3f[] Normals, Colors;
            public Vector2f[] UVs;
            public List<Index3i> Triangles;
        }


        /// <summary>
        /// Read a .gltf or .glb file from stream (read to the end) and append one mesh per mesh instance to builder.
        /// options are ignored. Does not throw.
        /// </summary>
        public IOReadResult Read(Stream stream, ReadOptions options, IMeshBuilder builder)
        {
            try {
                return read(stream, builder);
            } catch (Exception e) {
                return new IOReadResult(IOCode.FileParsingError, "error reading glTF: " + e.Message);
            }
        }


        IOReadResult read(Stream stream, IMeshBuilder builder)
        {
            byte[] data = read_all(stream);
            byte[] glbBin = null;
            string json;
            if (data.Length >= 12 && BitConverter.ToUInt32(data, 0) == GLBMagic) {
                IOReadResult r = parse_glb(data, out json, out glbBin);
                if (r.code != IOCode.Ok)
                    return r;
            } else {
                json = Encoding.UTF8.GetString(data);
                if (json.Length > 0 && json[0] == '﻿')
                    json = json.Substring(1);
            }

            try {
                root = MiniJson.Parse(json) as Dictionary<string, object>;
            } catch (FormatException e) {
                return new IOReadResult(IOCode.FileParsingError, "invalid glTF JSON: " + e.Message);
            }
            if (root == null)
                return new IOReadResult(IOCode.FileParsingError, "glTF JSON root is not an object");

            Dictionary<string, object> asset = get_obj(root, "asset");
            string version = asset != null ? get_string(asset, "version", "") : "";
            if (version.StartsWith("2") == false)
                return new IOReadResult(IOCode.FormatNotSupportedError, "only glTF 2.0 is supported, file has version " + version);

            // extensions that change how geometry is stored; others (materials, KHR_mesh_quantization, ...) do not matter here
            foreach (object ext in get_list(root, "extensionsRequired")) {
                string name = ext as string;
                if (name == "KHR_draco_mesh_compression" || name == "EXT_meshopt_compression" || name == "KHR_meshopt_compression")
                    return new IOReadResult(IOCode.FormatNotSupportedError, "glTF file requires unsupported extension " + name);
            }

            IOReadResult result = load_buffers(glbBin);
            if (result.code != IOCode.Ok)
                return result;

            List<object> meshes = get_list(root, "meshes");
            List<object> nodes = get_list(root, "nodes");
            List<object> scenes = get_list(root, "scenes");

            // collect (mesh, transform) instances
            List<KeyValuePair<int, Affine3d>> instances = new List<KeyValuePair<int, Affine3d>>();
            if (scenes.Count > 0) {
                int sceneIndex = get_int(root, "scene", 0);
                if (sceneIndex < 0 || sceneIndex >= scenes.Count)
                    return new IOReadResult(IOCode.FileParsingError, "glTF default scene index is invalid");
                Dictionary<string, object> scene = scenes[sceneIndex] as Dictionary<string, object>;
                foreach (object n in get_list(scene, "nodes"))
                    collect_instances(nodes, to_int(n), Affine3d.Identity, instances, 0);
            } else if (nodes.Count > 0) {
                // no scene: use all root nodes
                bool[] isChild = new bool[nodes.Count];
                foreach (object n in nodes)
                    foreach (object c in get_list(n as Dictionary<string, object>, "children"))
                        if (to_int(c) >= 0 && to_int(c) < nodes.Count)
                            isChild[to_int(c)] = true;
                for (int i = 0; i < nodes.Count; ++i)
                    if (isChild[i] == false)
                        collect_instances(nodes, i, Affine3d.Identity, instances, 0);
            } else {
                for (int i = 0; i < meshes.Count; ++i)
                    instances.Add(new KeyValuePair<int, Affine3d>(i, Affine3d.Identity));
            }

            Dictionary<int, List<Primitive>> cache = new Dictionary<int, List<Primitive>>();
            foreach (var instance in instances) {
                int mi = instance.Key;
                if (mi < 0 || mi >= meshes.Count)
                    return new IOReadResult(IOCode.FileParsingError, "glTF node references invalid mesh " + mi);
                List<Primitive> prims;
                if (cache.TryGetValue(mi, out prims) == false) {
                    try {
                        prims = read_mesh(meshes[mi] as Dictionary<string, object>);
                    } catch (Exception e) {
                        return new IOReadResult(IOCode.FileParsingError, "error reading glTF mesh " + mi + ": " + e.Message);
                    }
                    cache[mi] = prims;
                }
                append_mesh(prims, ApplyNodeTransforms ? instance.Value : Affine3d.Identity,
                    get_string(meshes[mi] as Dictionary<string, object>, "name", null), builder);
            }
            return IOReadResult.Ok;
        }



        static byte[] read_all(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream()) {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }


        static IOReadResult parse_glb(byte[] data, out string json, out byte[] bin)
        {
            json = null; bin = null;
            uint version = BitConverter.ToUInt32(data, 4);
            if (version != 2)
                return new IOReadResult(IOCode.FormatNotSupportedError, "only GLB version 2 is supported");
            long length = Math.Min(BitConverter.ToUInt32(data, 8), data.Length);
            long pos = 12;
            while (pos + 8 <= length) {
                uint chunkLength = BitConverter.ToUInt32(data, (int)pos);
                uint chunkType = BitConverter.ToUInt32(data, (int)pos + 4);
                pos += 8;
                if (pos + chunkLength > length)
                    return new IOReadResult(IOCode.FileParsingError, "GLB chunk extends past end of file");
                if (chunkType == ChunkJSON && json == null)
                    json = Encoding.UTF8.GetString(data, (int)pos, (int)chunkLength);
                else if (chunkType == ChunkBIN && bin == null) {
                    bin = new byte[chunkLength];
                    Buffer.BlockCopy(data, (int)pos, bin, 0, (int)chunkLength);
                }
                pos += chunkLength;
            }
            if (json == null)
                return new IOReadResult(IOCode.FileParsingError, "GLB file has no JSON chunk");
            return IOReadResult.Ok;
        }


        IOReadResult load_buffers(byte[] glbBin)
        {
            buffers = new List<byte[]>();
            List<object> list = get_list(root, "buffers");
            for (int i = 0; i < list.Count; ++i) {
                Dictionary<string, object> b = list[i] as Dictionary<string, object>;
                int byteLength = get_int(b, "byteLength", 0);
                string uri = get_string(b, "uri", null);
                byte[] bytes;
                if (uri == null) {
                    if (i != 0 || glbBin == null)
                        return new IOReadResult(IOCode.FileParsingError, "glTF buffer " + i + " has no uri and no GLB BIN chunk");
                    bytes = glbBin;
                } else if (uri.StartsWith("data:")) {
                    int comma = uri.IndexOf(',');
                    if (comma < 0 || uri.Substring(0, comma).EndsWith(";base64") == false)
                        return new IOReadResult(IOCode.FormatNotSupportedError, "glTF buffer " + i + " has a data uri that is not base64");
                    bytes = Convert.FromBase64String(uri.Substring(comma + 1));
                } else {
                    if (ResolveUri == null)
                        return new IOReadResult(IOCode.FileAccessError, "glTF buffer " + i + " references external file " + uri + ", but no ResolveUri function is set");
                    try {
                        bytes = ResolveUri(Uri.UnescapeDataString(uri));
                    } catch (Exception e) {
                        return new IOReadResult(IOCode.FileAccessError, "could not read glTF buffer " + uri + ": " + e.Message);
                    }
                    if (bytes == null)
                        return new IOReadResult(IOCode.FileAccessError, "could not read glTF buffer " + uri);
                }
                if (bytes.Length < byteLength)
                    return new IOReadResult(IOCode.FileParsingError, "glTF buffer " + i + " is shorter than its byteLength");
                buffers.Add(bytes);
            }
            return IOReadResult.Ok;
        }


        void collect_instances(List<object> nodes, int ni, Affine3d parent, List<KeyValuePair<int, Affine3d>> instances, int depth)
        {
            if (ni < 0 || ni >= nodes.Count || depth > 256)
                throw new FormatException("glTF node hierarchy is invalid");
            Dictionary<string, object> node = nodes[ni] as Dictionary<string, object>;
            Affine3d xform = parent * node_matrix(node);
            int mesh = get_int(node, "mesh", -1);
            if (mesh >= 0)
                instances.Add(new KeyValuePair<int, Affine3d>(mesh, xform));
            foreach (object c in get_list(node, "children"))
                collect_instances(nodes, to_int(c), xform, instances, depth + 1);
        }


        static Affine3d node_matrix(Dictionary<string, object> node)
        {
            List<object> m = get_list(node, "matrix");
            if (m.Count == 16) {
                // column-major
                double[] a = new double[16];
                for (int i = 0; i < 16; ++i)
                    a[i] = to_double(m[i]);
                return Affine3d.FromColumnMajor(a);
            }
            List<object> t = get_list(node, "translation"), r = get_list(node, "rotation"), s = get_list(node, "scale");
            Vector3d T = (t.Count == 3) ? new Vector3d(to_double(t[0]), to_double(t[1]), to_double(t[2])) : Vector3d.Zero;
            Vector3d S = (s.Count == 3) ? new Vector3d(to_double(s[0]), to_double(s[1]), to_double(s[2])) : Vector3d.One;
            Matrix3d R = Matrix3d.Identity;
            if (r.Count == 4) {
                double x = to_double(r[0]), y = to_double(r[1]), z = to_double(r[2]), w = to_double(r[3]);
                double len = Math.Sqrt(x * x + y * y + z * z + w * w);
                if (len > 0) { x /= len; y /= len; z /= len; w /= len; }
                R = new Matrix3d(
                    1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
            }
            Matrix3d RS = R * new Matrix3d(S.x, S.y, S.z);
            return new Affine3d(RS, T);
        }



        List<Primitive> read_mesh(Dictionary<string, object> mesh)
        {
            List<Primitive> result = new List<Primitive>();
            int nSkipped = 0;
            foreach (object po in get_list(mesh, "primitives")) {
                Dictionary<string, object> p = po as Dictionary<string, object>;
                int mode = get_int(p, "mode", 4);
                if (mode < 4 || mode > 6) {
                    nSkipped++;
                    continue;
                }
                Dictionary<string, object> attribs = get_obj(p, "attributes");
                if (attribs == null || attribs.ContainsKey("POSITION") == false)
                    throw new FormatException("primitive has no POSITION attribute");

                Primitive prim = new Primitive();
                double[] pos = read_accessor(to_int(attribs["POSITION"]), 3);
                prim.Positions = new Vector3d[pos.Length / 3];
                for (int i = 0; i < prim.Positions.Length; ++i)
                    prim.Positions[i] = new Vector3d(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]);
                int nv = prim.Positions.Length;

                if (attribs.ContainsKey("NORMAL")) {
                    double[] n = read_accessor(to_int(attribs["NORMAL"]), 3);
                    if (n.Length == 3 * nv) {
                        prim.Normals = new Vector3f[nv];
                        for (int i = 0; i < nv; ++i)
                            prim.Normals[i] = new Vector3f(n[3 * i], n[3 * i + 1], n[3 * i + 2]);
                    }
                }
                if (attribs.ContainsKey("COLOR_0")) {
                    int comps;
                    double[] c = read_accessor(to_int(attribs["COLOR_0"]), -1, out comps);
                    if (comps >= 3 && c.Length == comps * nv) {
                        prim.Colors = new Vector3f[nv];
                        for (int i = 0; i < nv; ++i)
                            prim.Colors[i] = new Vector3f(c[comps * i], c[comps * i + 1], c[comps * i + 2]);
                    }
                }
                if (attribs.ContainsKey("TEXCOORD_0")) {
                    double[] uv = read_accessor(to_int(attribs["TEXCOORD_0"]), 2);
                    if (uv.Length == 2 * nv) {
                        prim.UVs = new Vector2f[nv];
                        for (int i = 0; i < nv; ++i)
                            prim.UVs[i] = new Vector2f(uv[2 * i], uv[2 * i + 1]);
                    }
                }

                int[] indices;
                if (p.ContainsKey("indices")) {
                    double[] idx = read_accessor(to_int(p["indices"]), 1);
                    indices = new int[idx.Length];
                    for (int i = 0; i < idx.Length; ++i)
                        indices[i] = (int)idx[i];
                } else {
                    indices = new int[nv];
                    for (int i = 0; i < nv; ++i)
                        indices[i] = i;
                }

                prim.Triangles = new List<Index3i>();
                if (mode == 4) {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                        prim.Triangles.Add(new Index3i(indices[i], indices[i + 1], indices[i + 2]));
                } else if (mode == 5) {
                    for (int i = 0; i + 2 < indices.Length; ++i) {
                        if (i % 2 == 0)
                            prim.Triangles.Add(new Index3i(indices[i], indices[i + 1], indices[i + 2]));
                        else
                            prim.Triangles.Add(new Index3i(indices[i + 1], indices[i], indices[i + 2]));
                    }
                } else {
                    for (int i = 1; i + 1 < indices.Length; ++i)
                        prim.Triangles.Add(new Index3i(indices[0], indices[i], indices[i + 1]));
                }
                result.Add(prim);
            }
            if (nSkipped > 0)
                emit_warning(string.Format("skipped {0} glTF point/line primitives", nSkipped));
            return result;
        }


        double[] read_accessor(int index, int expectComponents)
        {
            int comps;
            return read_accessor(index, expectComponents, out comps);
        }

        // read accessor as flat array of doubles (normalized integers are mapped to [0,1] / [-1,1])
        double[] read_accessor(int index, int expectComponents, out int nComponents)
        {
            List<object> accessors = get_list(root, "accessors");
            if (index < 0 || index >= accessors.Count)
                throw new FormatException("invalid accessor index " + index);
            Dictionary<string, object> acc = accessors[index] as Dictionary<string, object>;
            int count = get_int(acc, "count", 0);
            int componentType = get_int(acc, "componentType", 5126);
            bool normalized = get_bool(acc, "normalized", false);
            nComponents = type_components(get_string(acc, "type", "SCALAR"));
            if (expectComponents > 0 && nComponents != expectComponents)
                throw new FormatException(string.Format("accessor {0} has {1} components, expected {2}", index, nComponents, expectComponents));
            int compSize = component_size(componentType);

            double[] values = new double[(long)count * nComponents];
            if (acc.ContainsKey("bufferView"))
                read_view(to_int(acc["bufferView"]), get_int(acc, "byteOffset", 0), count, nComponents, componentType, normalized, compSize, values, null);

            Dictionary<string, object> sparse = get_obj(acc, "sparse");
            if (sparse != null) {
                int sparseCount = get_int(sparse, "count", 0);
                Dictionary<string, object> sIdx = get_obj(sparse, "indices"), sVal = get_obj(sparse, "values");
                double[] idx = new double[sparseCount];
                int idxType = get_int(sIdx, "componentType", 5125);
                read_view(get_int(sIdx, "bufferView", -1), get_int(sIdx, "byteOffset", 0), sparseCount, 1, idxType, false, component_size(idxType), idx, -1);
                double[] val = new double[(long)sparseCount * nComponents];
                read_view(get_int(sVal, "bufferView", -1), get_int(sVal, "byteOffset", 0), sparseCount, nComponents, componentType, normalized, compSize, val, -1);
                for (int i = 0; i < sparseCount; ++i) {
                    int k = (int)idx[i];
                    if (k < 0 || k >= count)
                        throw new FormatException("sparse accessor index out of range");
                    for (int j = 0; j < nComponents; ++j)
                        values[k * nComponents + j] = val[i * nComponents + j];
                }
            }
            return values;
        }


        // forceStride: null = use bufferView byteStride, -1 = tightly packed (sparse data)
        void read_view(int viewIndex, int accessorOffset, int count, int nComponents, int componentType, bool normalized, int compSize, double[] values, int? forceStride)
        {
            List<object> views = get_list(root, "bufferViews");
            if (viewIndex < 0 || viewIndex >= views.Count)
                throw new FormatException("invalid bufferView index " + viewIndex);
            Dictionary<string, object> view = views[viewIndex] as Dictionary<string, object>;
            int bufferIndex = get_int(view, "buffer", 0);
            if (bufferIndex < 0 || bufferIndex >= buffers.Count)
                throw new FormatException("invalid buffer index " + bufferIndex);
            byte[] buffer = buffers[bufferIndex];
            long viewOffset = get_int(view, "byteOffset", 0);
            long viewLength = get_int(view, "byteLength", 0);
            int elemSize = compSize * nComponents;
            int stride = (forceStride == null) ? get_int(view, "byteStride", 0) : 0;
            if (stride == 0)
                stride = elemSize;

            if (count == 0)
                return;
            long start = viewOffset + accessorOffset;
            long end = start + (long)(count - 1) * stride + elemSize;
            if (accessorOffset < 0 || end > viewOffset + viewLength || viewOffset + viewLength > buffer.Length)
                throw new FormatException("accessor data extends past its bufferView or buffer");

            for (int i = 0; i < count; ++i) {
                long elemStart = start + (long)i * stride;
                for (int j = 0; j < nComponents; ++j)
                    values[(long)i * nComponents + j] = read_component(buffer, (int)(elemStart + j * compSize), componentType, normalized);
            }
        }


        static double read_component(byte[] b, int offset, int componentType, bool normalized)
        {
            switch (componentType) {
                case 5120: { double v = (sbyte)b[offset]; return normalized ? Math.Max(v / 127.0, -1.0) : v; }
                case 5121: { double v = b[offset]; return normalized ? v / 255.0 : v; }
                case 5122: { double v = BitConverter.ToInt16(b, offset); return normalized ? Math.Max(v / 32767.0, -1.0) : v; }
                case 5123: { double v = BitConverter.ToUInt16(b, offset); return normalized ? v / 65535.0 : v; }
                case 5125: return BitConverter.ToUInt32(b, offset);
                case 5126: return BitConverter.ToSingle(b, offset);
            }
            throw new FormatException("unknown componentType " + componentType);
        }

        static int component_size(int componentType)
        {
            switch (componentType) {
                case 5120: case 5121: return 1;
                case 5122: case 5123: return 2;
                case 5125: case 5126: return 4;
            }
            throw new FormatException("unknown componentType " + componentType);
        }

        static int type_components(string type)
        {
            switch (type) {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
            }
            throw new FormatException("unknown accessor type " + type);
        }



        void append_mesh(List<Primitive> prims, Affine3d xform, string name, IMeshBuilder builder)
        {
            bool bNormals = false, bColors = false, bUVs = false;
            foreach (Primitive p in prims) {
                bNormals |= p.Normals != null;
                bColors |= p.Colors != null;
                bUVs |= p.UVs != null;
            }
            bool bGroups = prims.Count > 1;
            builder.AppendNewMesh(bNormals, bColors, bUVs, bGroups);
            if (name != null && builder.SupportsMetaData)
                builder.AppendMetaData("name", name);

            Matrix3d normalXform = Matrix3d.Identity;
            Matrix3d linear = xform.Linear;
            bool bFlip = linear.Determinant < 0;
            if (Math.Abs(linear.Determinant) > MathUtil.ZeroTolerance)
                normalXform = linear.Inverse().Transpose();

            int nInvalid = 0;
            for (int pi = 0; pi < prims.Count; ++pi) {
                Primitive p = prims[pi];
                int[] map = new int[p.Positions.Length];
                for (int i = 0; i < p.Positions.Length; ++i) {
                    NewVertexInfo info = new NewVertexInfo(xform.TransformPoint(p.Positions[i]));
                    if (bNormals) {
                        info.bHaveN = true;
                        info.n = (p.Normals != null) ? (Vector3f)(normalXform * (Vector3d)p.Normals[i]).Normalized : Vector3f.AxisY;
                    }
                    if (bColors) {
                        info.bHaveC = true;
                        info.c = (p.Colors != null) ? p.Colors[i] : Vector3f.One;
                    }
                    if (bUVs) {
                        info.bHaveUV = true;
                        info.uv = (p.UVs != null) ? p.UVs[i] : Vector2f.Zero;
                    }
                    map[i] = builder.AppendVertex(info);
                }
                foreach (Index3i t in p.Triangles) {
                    if (t.a < 0 || t.b < 0 || t.c < 0 || t.a >= map.Length || t.b >= map.Length || t.c >= map.Length
                        || t.a == t.b || t.b == t.c || t.a == t.c) {
                        nInvalid++;
                        continue;
                    }
                    int a = map[t.a], b = bFlip ? map[t.c] : map[t.b], c = bFlip ? map[t.b] : map[t.c];
                    if (bGroups)
                        builder.AppendTriangle(a, b, c, pi);
                    else
                        builder.AppendTriangle(a, b, c);
                }
            }
            if (nInvalid > 0)
                emit_warning(string.Format("skipped {0} degenerate or invalid glTF triangles", nInvalid));
        }



        static Dictionary<string, object> get_obj(Dictionary<string, object> d, string key)
        {
            object o;
            return (d != null && d.TryGetValue(key, out o)) ? o as Dictionary<string, object> : null;
        }
        static readonly List<object> EmptyList = new List<object>();
        static List<object> get_list(Dictionary<string, object> d, string key)
        {
            object o;
            return (d != null && d.TryGetValue(key, out o) && o is List<object>) ? (List<object>)o : EmptyList;
        }
        static int get_int(Dictionary<string, object> d, string key, int def)
        {
            object o;
            return (d != null && d.TryGetValue(key, out o) && o is double) ? to_int(o) : def;
        }
        static bool get_bool(Dictionary<string, object> d, string key, bool def)
        {
            object o;
            return (d != null && d.TryGetValue(key, out o) && o is bool) ? (bool)o : def;
        }
        static string get_string(Dictionary<string, object> d, string key, string def)
        {
            object o;
            return (d != null && d.TryGetValue(key, out o) && o is string) ? (string)o : def;
        }
        static int to_int(object o)
        {
            if (o is double == false)
                throw new FormatException("expected a number");
            double d = (double)o;
            if (d < int.MinValue || d > int.MaxValue)
                throw new FormatException("number out of range");
            return (int)d;
        }
        static double to_double(object o)
        {
            if (o is double == false)
                throw new FormatException("expected a number");
            return (double)o;
        }


        private void emit_warning(string sMessage)
        {
            var e = warningEvent;
            if (e != null)
                e(sMessage, null);
        }
    }
}
