using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace g3
{
    /// <summary>
    /// Write glTF 2.0 files, as .glb (binary container, <see cref="WriteGLB"/>) or .gltf
    /// (JSON with embedded base64 buffer, <see cref="WriteGLTF"/>). No external .bin files are written.
    /// https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
    /// 
    /// Each mesh becomes one node with one mesh and one primitive, named by WriteMesh.Name. Writes POSITION and, if enabled
    /// in the options (bPerVertexNormals/Colors/UVs) and present in the mesh, NORMAL, COLOR_0 (RGB, clamped to [0,1])
    /// and TEXCOORD_0. Meshes without triangles are written as point primitives, empty meshes as nodes without mesh.
    /// No materials are written.
    /// 
    /// glTF stores positions as 32-bit floats. With CenterNodes = true (default), each mesh is
    /// written relative to the center of its bounding box and the center is stored as node
    /// translation (in double precision), so large coordinates (eg UTM) keep their precision.
    /// GLTFReader applies the translation again when reading. Coordinates are not converted (glTF is Y-up, in meters).
    /// The write methods return Ok; exceptions are not caught here.
    /// </summary>
    public class GLTFWriter
    {
        /// <summary>
        /// If true (default), vertices are written relative to the bounding box center and the center is the node translation
        /// </summary>
        public bool CenterNodes = true;


        /// <summary>
        /// Write meshes as binary .glb to stream (the stream is flushed, not closed)
        /// </summary>
        public IOWriteResult WriteGLB(Stream stream, List<WriteMesh> vMeshes, WriteOptions options)
        {
            byte[] bin;
            string json = build(vMeshes, options, false, out bin);
            byte[] jsonBytes = pad(new UTF8Encoding(false).GetBytes(json), (byte)' ');
            byte[] binBytes = pad(bin, 0);

            BinaryWriter w = new BinaryWriter(stream);
            int total = 12 + 8 + jsonBytes.Length + (binBytes.Length > 0 ? 8 + binBytes.Length : 0);
            w.Write(0x46546C67u);   // "glTF"
            w.Write(2u);
            w.Write((uint)total);
            w.Write((uint)jsonBytes.Length);
            w.Write(0x4E4F534Au);   // JSON
            w.Write(jsonBytes);
            if (binBytes.Length > 0) {
                w.Write((uint)binBytes.Length);
                w.Write(0x004E4942u);   // BIN
                w.Write(binBytes);
            }
            w.Flush();
            return IOWriteResult.Ok;
        }


        /// <summary>
        /// Write meshes as .gltf JSON to writer, with the buffer embedded as a base64 data URI
        /// </summary>
        public IOWriteResult WriteGLTF(TextWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            byte[] bin;
            string json = build(vMeshes, options, true, out bin);
            writer.Write(json);
            return IOWriteResult.Ok;
        }


        static byte[] pad(byte[] data, byte value)
        {
            int n = (data.Length + 3) & ~3;
            if (n == data.Length)
                return data;
            byte[] result = new byte[n];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            for (int i = data.Length; i < n; ++i)
                result[i] = value;
            return result;
        }


        class JsonList
        {
            public List<string> Items = new List<string>();
            public int Add(string json) { Items.Add(json); return Items.Count - 1; }
            public override string ToString() { return "[" + string.Join(",", Items.ToArray()) + "]"; }
        }


        string build(List<WriteMesh> vMeshes, WriteOptions options, bool bEmbedBuffer, out byte[] bin)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            MemoryStream data = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(data);
            JsonList views = new JsonList(), accessors = new JsonList(), meshes = new JsonList(), nodes = new JsonList();

            Func<int, int, string> addView = (offset, length) =>
                views.Add(string.Format(ci, "{{\"buffer\":0,\"byteOffset\":{0},\"byteLength\":{1}}}", offset, length)).ToString(ci);

            for (int mi = 0; mi < vMeshes.Count; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                if (options.ProgressFunc != null)
                    options.ProgressFunc(mi, vMeshes.Count);
                string name = string.IsNullOrEmpty(vMeshes[mi].Name) ? null : MiniJson.Quote(vMeshes[mi].Name);

                // glTF accessors must not be empty, so an empty mesh becomes a node without mesh
                if (mesh.VertexCount == 0) {
                    nodes.Add("{" + (name != null ? "\"name\":" + name : "") + "}");
                    continue;
                }

                AxisAlignedBox3d bounds = AxisAlignedBox3d.Empty;
                foreach (int vid in mesh.VertexIndices())
                    bounds.Contain(mesh.GetVertex(vid));
                Vector3d origin = CenterNodes ? bounds.Center : Vector3d.Zero;

                bool bNormals = options.bPerVertexNormals && mesh.HasVertexNormals;
                bool bColors = options.bPerVertexColors && mesh.HasVertexColors;
                bool bUVs = options.bPerVertexUVs && mesh.HasVertexUVs;

                int[] map = new int[mesh.MaxVertexID];
                int nv = 0;
                foreach (int vid in mesh.VertexIndices())
                    map[vid] = nv++;

                StringBuilder attribs = new StringBuilder();

                // positions, with float min/max as required by the spec
                int offset = (int)data.Length;
                Vector3f min = new Vector3f(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3f max = new Vector3f(float.MinValue, float.MinValue, float.MinValue);
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid) - origin;
                    Vector3f f = new Vector3f((float)v.x, (float)v.y, (float)v.z);
                    bw.Write(f.x); bw.Write(f.y); bw.Write(f.z);
                    min = new Vector3f(Math.Min(min.x, f.x), Math.Min(min.y, f.y), Math.Min(min.z, f.z));
                    max = new Vector3f(Math.Max(max.x, f.x), Math.Max(max.y, f.y), Math.Max(max.z, f.z));
                }
                string view = addView(offset, (int)data.Length - offset);
                int posAccessor = accessors.Add(string.Format(ci,
                    "{{\"bufferView\":{0},\"componentType\":5126,\"count\":{1},\"type\":\"VEC3\",\"min\":[{2},{3},{4}],\"max\":[{5},{6},{7}]}}",
                    view, nv, f2s(min.x), f2s(min.y), f2s(min.z), f2s(max.x), f2s(max.y), f2s(max.z)));
                attribs.Append(string.Format(ci, "\"POSITION\":{0}", posAccessor));

                if (bNormals) {
                    offset = (int)data.Length;
                    foreach (int vid in mesh.VertexIndices()) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        if (n.Length > 0)
                            n.Normalize();
                        else
                            n = Vector3f.AxisZ;
                        bw.Write(n.x); bw.Write(n.y); bw.Write(n.z);
                    }
                    int acc = accessors.Add(string.Format(ci, "{{\"bufferView\":{0},\"componentType\":5126,\"count\":{1},\"type\":\"VEC3\"}}",
                        addView(offset, (int)data.Length - offset), nv));
                    attribs.Append(string.Format(ci, ",\"NORMAL\":{0}", acc));
                }
                if (bColors) {
                    offset = (int)data.Length;
                    foreach (int vid in mesh.VertexIndices()) {
                        Vector3f c = mesh.GetVertexColor(vid);
                        bw.Write(MathUtil.Clamp(c.x, 0.0f, 1.0f)); bw.Write(MathUtil.Clamp(c.y, 0.0f, 1.0f)); bw.Write(MathUtil.Clamp(c.z, 0.0f, 1.0f));
                    }
                    int acc = accessors.Add(string.Format(ci, "{{\"bufferView\":{0},\"componentType\":5126,\"count\":{1},\"type\":\"VEC3\"}}",
                        addView(offset, (int)data.Length - offset), nv));
                    attribs.Append(string.Format(ci, ",\"COLOR_0\":{0}", acc));
                }
                if (bUVs) {
                    offset = (int)data.Length;
                    foreach (int vid in mesh.VertexIndices()) {
                        Vector2f uv = mesh.GetVertexUV(vid);
                        bw.Write(uv.x); bw.Write(uv.y);
                    }
                    int acc = accessors.Add(string.Format(ci, "{{\"bufferView\":{0},\"componentType\":5126,\"count\":{1},\"type\":\"VEC2\"}}",
                        addView(offset, (int)data.Length - offset), nv));
                    attribs.Append(string.Format(ci, ",\"TEXCOORD_0\":{0}", acc));
                }

                string primitive;
                if (mesh.TriangleCount > 0) {
                    offset = (int)data.Length;
                    foreach (int tid in mesh.TriangleIndices()) {
                        Index3i t = mesh.GetTriangle(tid);
                        bw.Write((uint)map[t.a]); bw.Write((uint)map[t.b]); bw.Write((uint)map[t.c]);
                    }
                    int idxAccessor = accessors.Add(string.Format(ci, "{{\"bufferView\":{0},\"componentType\":5125,\"count\":{1},\"type\":\"SCALAR\"}}",
                        addView(offset, (int)data.Length - offset), 3 * mesh.TriangleCount));
                    primitive = string.Format(ci, "{{\"attributes\":{{{0}}},\"indices\":{1},\"mode\":4}}", attribs, idxAccessor);
                } else
                    primitive = string.Format(ci, "{{\"attributes\":{{{0}}},\"mode\":0}}", attribs);

                int meshIndex = meshes.Add("{" + (name != null ? "\"name\":" + name + "," : "") + "\"primitives\":[" + primitive + "]}");
                string translation = (origin != Vector3d.Zero) ?
                    string.Format(ci, ",\"translation\":[{0},{1},{2}]", origin.x.ToString("R", ci), origin.y.ToString("R", ci), origin.z.ToString("R", ci)) : "";
                nodes.Add("{" + (name != null ? "\"name\":" + name + "," : "") + string.Format(ci, "\"mesh\":{0}", meshIndex) + translation + "}");
            }

            bw.Flush();
            bin = data.ToArray();

            StringBuilder sceneNodes = new StringBuilder();
            for (int i = 0; i < nodes.Items.Count; ++i)
                sceneNodes.Append(i == 0 ? "" : ",").Append(i.ToString(ci));

            StringBuilder json = new StringBuilder();
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"geometry3Sharp\"}");
            json.Append(",\"scene\":0,\"scenes\":[{\"nodes\":[").Append(sceneNodes).Append("]}]");
            json.Append(",\"nodes\":").Append(nodes);
            json.Append(",\"meshes\":").Append(meshes);
            if (accessors.Items.Count > 0) {
                json.Append(",\"accessors\":").Append(accessors);
                json.Append(",\"bufferViews\":").Append(views);
            }
            if (bin.Length > 0) {
                json.Append(",\"buffers\":[{\"byteLength\":").Append(bin.Length.ToString(ci));
                if (bEmbedBuffer)
                    json.Append(",\"uri\":\"data:application/octet-stream;base64,").Append(Convert.ToBase64String(bin)).Append('"');
                json.Append("}]");
            }
            json.Append('}');
            return json.ToString();
        }


        static string f2s(float f)
        {
            return f.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
