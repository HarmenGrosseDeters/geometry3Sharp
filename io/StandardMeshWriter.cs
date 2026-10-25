using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Globalization;

namespace g3
{
    /// <summary>
    /// Writes various mesh file formats. Format is determined from extension. Currently supports:
    ///   * .obj : Wavefront OBJ Format https://en.wikipedia.org/wiki/Wavefront_.obj_file
    ///   * .stl : ascii and binary STL formats https://en.wikipedia.org/wiki/STL_(file_format) 
    ///   * .off : OFF format https://en.wikipedia.org/wiki/OFF_(file_format)
    ///   * .g3mesh : internal binary format for packed DMesh3 objects
    ///   * .ply : ascii and binary (little endian) PLY format http://paulbourke.net/dataformats/ply/
    ///   * .3mf : 3D Manufacturing Format (core spec) https://3mf.io
    ///   * .glb / .gltf : glTF 2.0 binary, or JSON with embedded buffer https://www.khronos.org/gltf/
    ///   
    /// Each of these is implemented in a separate Writer class, eg OBJWriter, STLWriter, etc
    /// 
    /// Easiest use is the static <see cref="WriteMesh"/>, eg WriteMesh("out.ply", mesh, WriteOptions.Defaults).
    /// Start options from <see cref="WriteOptions.Defaults"/>; bWriteBinary selects binary STL / PLY.
    /// Coordinates are written as stored, no unit or axis conversion is done (3MF is labelled millimeter).
    /// Errors are returned as an <see cref="IOWriteResult"/> code; exceptions during writing (including failing to
    /// create the file) are caught and returned as <see cref="IOCode.WriterError"/>.
    /// </summary>
    public class StandardMeshWriter : IDisposable
    {

        /// <summary>
        /// If the mesh format we are writing is text, then the OS will write in the number style
        /// of the current language. So in Germany, numbers are written 1,00 instead of 1.00, for example.
        /// If this flag is true (default), the thread culture is set to the invariant culture while writing.
        /// </summary>
        public bool WriteInvariantCulture = true;



        /// <summary>
        /// By default we write to files, but if you would like to write to some other
        /// Stream type (eg MemoryStream), you can replace this function.  
        /// We also pass this function down into the XYZWriter classes
        /// that need to write additional files (eg OBJ .mtl file).
        /// If it returns null, Write() returns <see cref="IOCode.FileAccessError"/>.
        /// </summary>
        public Func<string, Stream> OpenStreamF = (sFilename) => {
            return File.Open(sFilename, FileMode.Create);
        };

        /// <summary>
        /// called on Streams returned by OpenStreamF when we are done with them.
        /// </summary>
        public Action<Stream> CloseStreamF = (stream) => {
            stream.Close();
            stream.Dispose();
        };




        /// <summary> Does nothing, there are no resources to release </summary>
        public void Dispose()
        {
        }


        /// <summary>
        /// Write a list of DMesh3 to a file, format chosen by the extension. See <see cref="Write"/>.
        /// Whether meshes stay separate depends on the format (see <see cref="WriteOptions.bCombineMeshes"/>).
        /// </summary>
        static public IOWriteResult WriteMeshes(string sFilename, List<DMesh3> vMeshes, WriteOptions options)
        {
            List<WriteMesh> meshes = new List<g3.WriteMesh>();
            foreach (var m in vMeshes)
                meshes.Add(new WriteMesh(m));
            StandardMeshWriter writer = new StandardMeshWriter();
            return writer.Write(sFilename, meshes, options);
        }
        /// <summary>
        /// Write a list of meshes (with optional names, materials, UV layers) to a file, format chosen by the extension. See <see cref="Write"/>.
        /// </summary>
        static public IOWriteResult WriteFile(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            StandardMeshWriter writer = new StandardMeshWriter();
            return writer.Write(sFilename, vMeshes, options);
        }
        /// <summary>
        /// Write a single mesh to a file, format chosen by the extension (eg .obj, .stl, .ply, .3mf, .glb, .gltf).
        /// Use WriteOptions.Defaults, optionally with bWriteBinary = true for binary STL/PLY. See <see cref="Write"/> for result codes.
        /// </summary>
        static public IOWriteResult WriteMesh(string sFilename, IMesh mesh, WriteOptions options)
        {
            StandardMeshWriter writer = new StandardMeshWriter();
            return writer.Write(sFilename, new List<WriteMesh>() { new WriteMesh(mesh) }, options);
        }


        /// <summary>
        /// Write meshes to sFilename, choosing the writer from the extension (case-insensitive).
        /// Returns <see cref="IOCode.UnknownFormatError"/> for an unsupported extension, <see cref="IOCode.FileAccessError"/>
        /// if OpenStreamF returns null, <see cref="IOCode.WriterError"/> if an exception is thrown (eg the file
        /// cannot be created), otherwise the result of the format writer. Does not throw.
        /// </summary>
        public IOWriteResult Write(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Func<string, List<WriteMesh>, WriteOptions, IOWriteResult> writeFunc = null;

            string sExtension = Path.GetExtension(sFilename);
            if (sExtension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_OBJ;
            else if (sExtension.Equals(".stl", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_STL;
            else if (sExtension.Equals(".off", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_OFF;
            else if (sExtension.Equals(".g3mesh", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_G3Mesh;
            else if (sExtension.Equals(".ply", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_PLY;
            else if (sExtension.Equals(".3mf", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_3MF;
            else if (sExtension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_GLB;
            else if (sExtension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
                writeFunc = Write_GLTF;

            if (writeFunc == null)
                return new IOWriteResult(IOCode.UnknownFormatError, "format " + sExtension + " is not supported");

            // save current culture
            var current_culture = Thread.CurrentThread.CurrentCulture;

            try {
                // push invariant culture for write
                if (WriteInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

                var result = writeFunc(sFilename, vMeshes, options);

                // restore culture
                if (WriteInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = current_culture;

                return result;

            } catch (Exception e) {
                // restore culture
                if (WriteInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = current_culture;

                return new IOWriteResult(IOCode.WriterError, "Unknown error : exception : " + e.Message);
            }
        }




        IOWriteResult Write_OBJ(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if ( stream == null )
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                StreamWriter w = new StreamWriter(stream);
                OBJWriter writer = new OBJWriter() {
                    OpenStreamF = this.OpenStreamF,
                    CloseStreamF = this.CloseStreamF
                };
                var result = writer.Write(w, vMeshes, options);
                w.Flush();
                return result;
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_OFF(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                StreamWriter w = new StreamWriter(stream);
                OFFWriter writer = new OFFWriter();
                var result = writer.Write(w, vMeshes, options);
                w.Flush();
                return result;
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_STL(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try { 
                if (options.bWriteBinary) {
                    BinaryWriter w = new BinaryWriter(stream);
                    STLWriter writer = new STLWriter();
                    var result = writer.Write(w, vMeshes, options);
                    w.Flush();
                    return result;
                } else {
                    StreamWriter w = new StreamWriter(stream);
                    STLWriter writer = new STLWriter();
                    var result = writer.Write(w, vMeshes, options);
                    w.Flush();
                    return result;
                }
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_G3Mesh(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                BinaryWriter w = new BinaryWriter(stream);
                BinaryG3Writer writer = new BinaryG3Writer();
                var result = writer.Write(w, vMeshes, options);
                w.Flush();
                return result;
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_PLY(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                PLYWriter writer = new PLYWriter();
                if (options.bWriteBinary) {
                    BinaryWriter w = new BinaryWriter(stream);
                    var result = writer.Write(w, vMeshes, options);
                    w.Flush();
                    return result;
                } else {
                    StreamWriter w = new StreamWriter(stream);
                    var result = writer.Write(w, vMeshes, options);
                    w.Flush();
                    return result;
                }
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_3MF(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                return new ThreeMFWriter().Write(stream, vMeshes, options);
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_GLB(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                return new GLTFWriter().WriteGLB(stream, vMeshes, options);
            } finally {
                CloseStreamF(stream);
            }
        }


        IOWriteResult Write_GLTF(string sFilename, List<WriteMesh> vMeshes, WriteOptions options)
        {
            Stream stream = OpenStreamF(sFilename);
            if (stream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for writing");

            try {
                StreamWriter w = new StreamWriter(stream);
                var result = new GLTFWriter().WriteGLTF(w, vMeshes, options);
                w.Flush();
                return result;
            } finally {
                CloseStreamF(stream);
            }
        }




    }


}
