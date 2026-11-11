using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace g3
{
    /// <summary>
    /// Parse ascii XYZ point files into one mesh without triangles (a point cloud). Internal; use StandardMeshReader
    /// with a .xyz file. One point per line, values separated by white space:
    ///   x y z                       (positions only)
    ///   x y z nx ny nz [ignored...] (the second three values are normals; further columns, eg colors or an intensity, are ignored)
    /// Empty lines and lines that start with # or // are skipped. A line with fewer than 3 values, a line with 4 or
    /// 5 values in a file whose first point line has normals, or a value that is not a number fails the read
    /// (FileParsingError); a position with a non-finite coordinate is rejected as in the other readers
    /// (ReadOptions.AllowNonFiniteCoordinates). A file without points is GarbageDataError. Whether the file has normals is
    /// decided by its first point line (6 or more values), and a later line must agree (fewer or more values than the first
    /// is a FileParsingError, except the ignored columns below six); a file "x y z r g b" is read as having normals (the
    /// colors become normals: there is no way to tell them apart). The decimal separator is the point. Normals are read
    /// as float (one rounding), like all DMesh3 normals, and are not normalized or checked (they can be NaN or infinite).
    /// </summary>
    class XYZReader : IMeshReader
    {
        public IOReadResult Read(BinaryReader reader, ReadOptions options, IMeshBuilder builder)
        {
            return new IOReadResult(IOCode.FormatNotSupportedError, "binary read not supported for XYZ format");
        }

        public IOReadResult Read(TextReader reader, ReadOptions options, IMeshBuilder builder)
        {
            bool started = false, haveNormals = false;
            int count = 0, lineNumber = 0;
            string line;
            while ((line = reader.ReadLine()) != null) {
                lineNumber++;
                string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0 || tokens[0].StartsWith("#", StringComparison.Ordinal) || tokens[0].StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (tokens.Length < 3)
                    return new IOReadResult(IOCode.FileParsingError, "XYZ line " + lineNumber + " has fewer than 3 values");
                if (!started) {
                    haveNormals = tokens.Length >= 6;
                    builder.AppendNewMesh(haveNormals, false, false, false);
                    started = true;
                }
                if (haveNormals && tokens.Length < 6)
                    return new IOReadResult(IOCode.FileParsingError, "XYZ line " + lineNumber + " has no normal, the first point has one");
                if (!haveNormals && tokens.Length >= 6)
                    return new IOReadResult(IOCode.FileParsingError, "XYZ line " + lineNumber + " has a normal, the first point has none");
                double x, y, z;
                try {
                    x = MeshIOUtil.ParseDouble(tokens[0]);
                    y = MeshIOUtil.ParseDouble(tokens[1]);
                    z = MeshIOUtil.ParseDouble(tokens[2]);
                } catch (FormatException) {
                    return new IOReadResult(IOCode.FileParsingError, "XYZ line " + lineNumber + ": a position value is not a number");
                }   // (no OverflowException: MeshIOUtil.ParseDouble/ParseFloat read a value beyond the range as infinity, RejectsPosition below decides)
                if (ReadOptions.RejectsPosition(options, x, y, z))
                    return ReadOptions.NonFinitePosition(x, y, z);
                if (haveNormals) {
                    float nx, ny, nz;
                    try {
                        nx = MeshIOUtil.ParseFloat(tokens[3]);
                        ny = MeshIOUtil.ParseFloat(tokens[4]);
                        nz = MeshIOUtil.ParseFloat(tokens[5]);
                    } catch (FormatException) {
                        return new IOReadResult(IOCode.FileParsingError, "XYZ line " + lineNumber + ": a normal value is not a number");
                    }
                    NewVertexInfo info = new NewVertexInfo(new Vector3d(x, y, z), new Vector3f(nx, ny, nz));
                    builder.AppendVertex(info);
                } else
                    builder.AppendVertex(x, y, z);
                count++;
            }
            if (count == 0)
                return new IOReadResult(IOCode.GarbageDataError, "XYZ file has no points");
            return new IOReadResult(IOCode.Ok, "");
        }
    }


    /// <summary>
    /// Write ascii XYZ point files: the vertices of all meshes (triangles are ignored), one line "x y z" or, for a
    /// mesh with vertex normals, "x y z nx ny nz". Values are written in the shortest form that reads back as the
    /// same double (normals: as the same float; MeshIOUtil.FormatDouble / FormatFloat, also on .NET Framework), with the point
    /// as decimal separator. A vertex that is not finite makes the write fail (WriterError): the reader would reject the file.
    /// Normals are written whenever a mesh has them, WriteOptions.bPerVertexNormals is not looked at. If the meshes
    /// differ in having normals, the normals are written for all (zero for a mesh without). The BinaryWriter overload
    /// returns FormatNotSupportedError.
    /// </summary>
    public class XYZWriter : IMeshWriter
    {
        public IOWriteResult Write(BinaryWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            return new IOWriteResult(IOCode.FormatNotSupportedError, "binary write not supported for XYZ format");
        }

        public IOWriteResult Write(TextWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            bool normals = false;
            foreach (WriteMesh wm in vMeshes)
                normals = normals || (wm.Mesh.HasVertexNormals);
            foreach (WriteMesh wm in vMeshes)
                foreach (int vid in wm.Mesh.VertexIndices()) {
                    Vector3d v = wm.Mesh.GetVertex(vid);
                    if (double.IsNaN(v.x + v.y + v.z) || double.IsInfinity(v.x) || double.IsInfinity(v.y) || double.IsInfinity(v.z))
                        return new IOWriteResult(IOCode.WriterError, "vertex " + vid + " is not finite");
                }
            int N = vMeshes.Count;
            for (int mi = 0; mi < N; ++mi) {
                IMesh mesh = vMeshes[mi].Mesh;
                if (options.ProgressFunc != null)
                    options.ProgressFunc(mi, N);
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    string line = R(v.x) + " " + R(v.y) + " " + R(v.z);
                    if (normals) {
                        Vector3f n = mesh.HasVertexNormals ? mesh.GetVertexNormal(vid) : Vector3f.Zero;
                        line += " " + R(n.x) + " " + R(n.y) + " " + R(n.z);
                    }
                    writer.WriteLine(line);
                }
            }
            return new IOWriteResult(IOCode.Ok, "");
        }

        static string R(double d) { return MeshIOUtil.FormatDouble(d); }
        static string R(float f) { return MeshIOUtil.FormatFloat(f); }
    }
}
