using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace g3
{
    //
    // Write PLY (Stanford polygon) mesh format, ascii or binary_little_endian
    // http://paulbourke.net/dataformats/ply/
    //
    // PLY only stores one mesh, so all input meshes are combined.
    // Positions are written as double if options.RealPrecisionDigits > 7, otherwise as float.
    // Normals, colors (uchar) and UVs (s,t) are written if enabled in the options and all meshes have them.
    // Meshes without triangles are written as point sets.
    //
    public class PLYWriter : IMeshWriter
    {
        struct Layout
        {
            public bool bDouble, bNormals, bColors, bUVs;
            public int nVertices, nTriangles;
        }


        public IOWriteResult Write(TextWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Layout layout = make_layout(vMeshes, options);
            writer.Write(make_header(layout, "ascii", options));

            CultureInfo ci = CultureInfo.InvariantCulture;
            const string fmt = "R";     // round-trip format
            StringBuilder line = new StringBuilder();
            for (int mi = 0; mi < vMeshes.Count; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                if (options.ProgressFunc != null)
                    options.ProgressFunc(mi, 2 * vMeshes.Count);
                foreach (int vid in mesh.VertexIndices()) {
                    line.Clear();
                    Vector3d v = mesh.GetVertex(vid);
                    if (layout.bDouble)
                        line.Append(v.x.ToString(fmt, ci)).Append(' ').Append(v.y.ToString(fmt, ci)).Append(' ').Append(v.z.ToString(fmt, ci));
                    else
                        line.Append(((float)v.x).ToString(fmt, ci)).Append(' ').Append(((float)v.y).ToString(fmt, ci)).Append(' ').Append(((float)v.z).ToString(fmt, ci));
                    if (layout.bNormals) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        line.Append(' ').Append(n.x.ToString(fmt, ci)).Append(' ').Append(n.y.ToString(fmt, ci)).Append(' ').Append(n.z.ToString(fmt, ci));
                    }
                    if (layout.bColors) {
                        Vector3f c = mesh.GetVertexColor(vid);
                        line.Append(' ').Append(to_byte(c.x)).Append(' ').Append(to_byte(c.y)).Append(' ').Append(to_byte(c.z));
                    }
                    if (layout.bUVs) {
                        Vector2f uv = mesh.GetVertexUV(vid);
                        line.Append(' ').Append(uv.x.ToString(fmt, ci)).Append(' ').Append(uv.y.ToString(fmt, ci));
                    }
                    writer.WriteLine(line.ToString());
                }
            }

            int[][] mapV = make_vertex_maps(vMeshes);
            for (int mi = 0; mi < vMeshes.Count; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                if (options.ProgressFunc != null)
                    options.ProgressFunc(vMeshes.Count + mi, 2 * vMeshes.Count);
                int[] map = mapV[mi];
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    writer.WriteLine(string.Format(ci, "3 {0} {1} {2}", map[t.a], map[t.b], map[t.c]));
                }
            }
            return IOWriteResult.Ok;
        }


        public IOWriteResult Write(BinaryWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Layout layout = make_layout(vMeshes, options);
            writer.Write(Encoding.ASCII.GetBytes(make_header(layout, "binary_little_endian", options)));

            // BinaryWriter always writes little-endian
            for (int mi = 0; mi < vMeshes.Count; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                if (options.ProgressFunc != null)
                    options.ProgressFunc(mi, 2 * vMeshes.Count);
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    if (layout.bDouble) {
                        writer.Write(v.x); writer.Write(v.y); writer.Write(v.z);
                    } else {
                        writer.Write((float)v.x); writer.Write((float)v.y); writer.Write((float)v.z);
                    }
                    if (layout.bNormals) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        writer.Write(n.x); writer.Write(n.y); writer.Write(n.z);
                    }
                    if (layout.bColors) {
                        Vector3f c = mesh.GetVertexColor(vid);
                        writer.Write(to_byte(c.x)); writer.Write(to_byte(c.y)); writer.Write(to_byte(c.z));
                    }
                    if (layout.bUVs) {
                        Vector2f uv = mesh.GetVertexUV(vid);
                        writer.Write(uv.x); writer.Write(uv.y);
                    }
                }
            }

            int[][] mapV = make_vertex_maps(vMeshes);
            for (int mi = 0; mi < vMeshes.Count; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                if (options.ProgressFunc != null)
                    options.ProgressFunc(vMeshes.Count + mi, 2 * vMeshes.Count);
                int[] map = mapV[mi];
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    writer.Write((byte)3);
                    writer.Write(map[t.a]); writer.Write(map[t.b]); writer.Write(map[t.c]);
                }
            }
            return IOWriteResult.Ok;
        }



        static Layout make_layout(List<WriteMesh> vMeshes, WriteOptions options)
        {
            Layout layout = new Layout() {
                bDouble = options.RealPrecisionDigits > 7,
                bNormals = options.bPerVertexNormals,
                bColors = options.bPerVertexColors,
                bUVs = options.bPerVertexUVs
            };
            foreach (WriteMesh wm in vMeshes) {
                layout.bNormals = layout.bNormals && wm.Mesh.HasVertexNormals;
                layout.bColors = layout.bColors && wm.Mesh.HasVertexColors;
                layout.bUVs = layout.bUVs && wm.Mesh.HasVertexUVs;
                layout.nVertices += wm.Mesh.VertexCount;
                layout.nTriangles += wm.Mesh.TriangleCount;
            }
            return layout;
        }


        static string make_header(Layout layout, string format, WriteOptions options)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("ply\n");
            sb.Append("format ").Append(format).Append(" 1.0\n");
            sb.Append("comment written by geometry3Sharp\n");
            if (options.AsciiHeaderFunc != null) {
                foreach (string line in options.AsciiHeaderFunc().Split('\n')) {
                    if (line.Trim().Length > 0)
                        sb.Append("comment ").Append(line.Trim()).Append('\n');
                }
            }
            string real = layout.bDouble ? "double" : "float";
            sb.Append("element vertex ").Append(layout.nVertices.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("property ").Append(real).Append(" x\n");
            sb.Append("property ").Append(real).Append(" y\n");
            sb.Append("property ").Append(real).Append(" z\n");
            if (layout.bNormals)
                sb.Append("property float nx\nproperty float ny\nproperty float nz\n");
            if (layout.bColors)
                sb.Append("property uchar red\nproperty uchar green\nproperty uchar blue\n");
            if (layout.bUVs)
                sb.Append("property float s\nproperty float t\n");
            if (layout.nTriangles > 0) {
                sb.Append("element face ").Append(layout.nTriangles.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("property list uchar int vertex_indices\n");
            }
            sb.Append("end_header\n");
            return sb.ToString();
        }


        // map from vertex IDs in each mesh to vertex index in the combined file
        static int[][] make_vertex_maps(List<WriteMesh> vMeshes)
        {
            int[][] mapV = new int[vMeshes.Count][];
            int vi = 0;
            for (int mi = 0; mi < vMeshes.Count; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                mapV[mi] = new int[mesh.MaxVertexID];
                foreach (int vid in mesh.VertexIndices())
                    mapV[mi][vid] = vi++;
            }
            return mapV;
        }


        static byte to_byte(float f)
        {
            return (byte)MathUtil.Clamp((int)Math.Round(f * 255.0f), 0, 255);
        }
    }
}
