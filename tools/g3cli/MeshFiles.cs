// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;

namespace g3cli
{
    /// <summary> Reading and writing mesh files, with failures as CommandException result codes. </summary>
    public static class MeshFiles
    {
        public static readonly string[] ReadFormats = { "stl", "obj", "off", "ply", "3mf", "gltf", "glb", "g3mesh" };
        public static readonly string[] WriteFormats = { "stl", "obj", "off", "ply", "3mf", "gltf", "glb", "g3mesh" };

        static string Extension(string path)
        {
            string ext = Path.GetExtension(path ?? "");
            return ext.Length > 1 ? ext.Substring(1).ToLowerInvariant() : "";
        }

        /// <summary> Fails with unknown_format unless the extension of path can be written. </summary>
        public static void CheckWritable(string path)
        {
            if (WriteFormats.Contains(Extension(path)) == false)
                throw new CommandException(ResultCodes.UnknownFormat,
                    $"cannot write '{path}': supported output formats are {string.Join(", ", WriteFormats)}");
        }

        /// <summary>
        /// Reads all meshes of the file into one mesh. Fails with read_error, unknown_format or (no
        /// triangles) empty_mesh. Reader warnings are added to warnings.
        /// </summary>
        public static DMesh3 Read(string path, List<string> warnings, bool allowEmpty = false)
        {
            string ext = Extension(path);
            if (ReadFormats.Contains(ext) == false)
                throw new CommandException(ResultCodes.UnknownFormat,
                    $"cannot read '{path}': supported input formats are {string.Join(", ", ReadFormats)}");
            if (File.Exists(path) == false)
                throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");

            var builder = new DMesh3Builder();
            var reader = new StandardMeshReader() { MeshBuilder = builder };
            reader.warningEvent += (message, extra) => warnings?.Add(message);
            IOReadResult result = reader.Read(path, ReadOptions.Defaults);
            if (result.code == IOCode.UnknownFormatError || result.code == IOCode.FormatNotSupportedError)
                throw new CommandException(ResultCodes.UnknownFormat, $"cannot read '{path}': {result.message}");
            if (result.code != IOCode.Ok)
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {result.code}: {result.message}");

            DMesh3 mesh;
            if (builder.Meshes.Count == 1) {
                mesh = builder.Meshes[0];
            } else {
                mesh = new DMesh3();
                var editor = new MeshEditor(mesh);
                foreach (DMesh3 m in builder.Meshes)
                    editor.AppendMesh(m);
            }
            if (mesh.TriangleCount == 0 && allowEmpty == false)
                throw new CommandException(ResultCodes.EmptyMesh, $"'{path}' contains no triangles");
            return mesh;
        }

        /// <summary> Writes the mesh (compacted); fails with unknown_format or write_error. </summary>
        public static void Write(string path, DMesh3 mesh, bool binary = true)
        {
            CheckWritable(path);
            DMesh3 compact = mesh.IsCompact ? mesh : new DMesh3(mesh, true);
            WriteOptions options = WriteOptions.Defaults;
            options.bWriteBinary = binary;
            IOWriteResult result;
            try {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (Directory.Exists(dir) == false)
                    throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': directory does not exist");
                result = StandardMeshWriter.WriteMesh(path, compact, options);
            } catch (CommandException) {
                throw;
            } catch (Exception e) {
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
            if (result.code != IOCode.Ok)
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {result.code}: {result.message}");
        }
    }
}
