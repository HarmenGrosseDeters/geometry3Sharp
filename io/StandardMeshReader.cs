using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Globalization;

namespace g3
{

    /// <summary>
    /// Callback for non-fatal warnings emitted while parsing a mesh file (eg skipped faces).
    /// extra_data is currently always null in the built-in readers.
    /// </summary>
    public delegate void ParsingMessagesHandler(string message, object extra_data);


    /// <summary>
    /// A format handler registered with <see cref="StandardMeshReader"/>: reads one or more file
    /// extensions from a filename or a Stream into an <see cref="IMeshBuilder"/>.
    /// Implement this and call <see cref="StandardMeshReader.AddFormatHandler"/> to add a custom format.
    /// </summary>
    public interface MeshFormatReader
    {
        /// <summary> File extensions handled by this reader, lowercase and without the dot (eg "obj") </summary>
        List<string> SupportedExtensions { get; }
        IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler warnings);
        IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler warnings);
    }


    /// <summary>
    /// Reads mesh files, choosing the format reader from the file extension (case-insensitive). Supports:
    ///   * .obj : Wavefront OBJ (optionally with .mtl materials, see <see cref="ReadOptions.ReadMaterials"/>)
    ///   * .stl : ascii and binary STL (vertices are welded, see <see cref="STLReader"/>)
    ///   * .off : ascii OFF
    ///   * .g3mesh : internal binary format for DMesh3
    ///   * .ply : ascii and binary PLY
    ///   * .3mf : 3D Manufacturing Format (core spec), converted to millimeters
    ///   * .gltf / .glb : glTF 2.0
    /// 
    /// Easiest use is the static <see cref="ReadMesh(string)"/>, which returns the first mesh or null.
    /// To get all meshes and error details, use <see cref="ReadFile(string, ReadOptions, IMeshBuilder)"/> with a
    /// <see cref="DMesh3Builder"/> and read builder.Meshes afterwards.
    /// Read errors are returned as an <see cref="IOReadResult"/> code, exceptions thrown by format readers are
    /// caught and returned as <see cref="IOCode.GenericReaderError"/>. Coordinates are not converted
    /// (except 3MF units to millimeters).
    /// </summary>
    public class StandardMeshReader
    {
        /// <summary>
        /// If true (default), the current thread culture is set to the invariant culture while reading,
        /// so text formats parse numbers as 1.00 regardless of the OS language (eg not 1,00 in Germany).
        /// </summary>
        public bool ReadInvariantCulture = true;


        /// <summary>
        /// Connect to this to get warning messages from the format readers (eg skipped invalid faces).
        /// Warnings do not change the result code.
        /// </summary>
        public event ParsingMessagesHandler warningEvent;

        /// <summary>
        /// The various format handlers will use this IMeshBuilder to construct meshes
        /// based on the file data and read options.
        /// Default is initialized to DMesh3Builder. Meshes are appended, so after Read() the
        /// result is in eg (MeshBuilder as DMesh3Builder).Meshes.
        /// </summary>
        public IMeshBuilder MeshBuilder { get; set; }

        /// <summary>
        /// Set of format handlers
        /// </summary>
        List<MeshFormatReader> Readers = new List<MeshFormatReader>();


        /// <summary>
        /// Construct a MeshReader, optionally with default format handlers
        /// Initializes MeshBuilder to a DMesh3Builder
        /// </summary>
        public StandardMeshReader(bool bIncludeDefaultReaders = true)
        {
            Readers = new List<MeshFormatReader>();
            MeshBuilder = new DMesh3Builder();

            if ( bIncludeDefaultReaders ) {
                Readers.Add(new OBJFormatReader());
                Readers.Add(new STLFormatReader());
                Readers.Add(new OFFFormatReader());
                Readers.Add(new BinaryG3FormatReader());
                Readers.Add(new PLYFormatReader());
                Readers.Add(new ThreeMFFormatReader());
                Readers.Add(new GLTFFormatReader());
            }
        }


        /// <summary>
        /// Check if extension type is supported. sExtension is without the dot (eg "ply"), case-insensitive.
        /// </summary>
        public bool SupportsFormat(string sExtension)
        {
            foreach (var reader in Readers)
                foreach (string ext in reader.SupportedExtensions)
                    if (ext.Equals(sExtension, StringComparison.OrdinalIgnoreCase))
                        return true;
            return false;
        }


        /// <summary>
        /// Add a handler for a new format. Throws an Exception if one of its extensions is already registered.
        /// </summary>
        public void AddFormatHandler(MeshFormatReader reader)
        {
            List<string> formats = reader.SupportedExtensions;
            foreach (string s in formats)
                if (SupportsFormat(s))
                    throw new Exception("StandardMeshReader.AddFormatHandler: format " + s + " is already registered!");

            Readers.Add(reader);
        }


        /// <summary>
        /// Read mesh file at path, with given Options. Result meshes are appended to the <see cref="MeshBuilder"/> property.
        /// Returns <see cref="IOCode.InvalidFilenameError"/> if the filename has no extension, <see cref="IOCode.UnknownFormatError"/>
        /// if no reader handles it, <see cref="IOCode.FileAccessError"/> if the file cannot be opened, otherwise the format reader's result.
        /// Does not throw: exceptions are returned as <see cref="IOCode.GenericReaderError"/>.
        /// </summary>
        public IOReadResult Read(string sFilename, ReadOptions options)
        {
            if (MeshBuilder == null)
                return new IOReadResult(IOCode.GenericReaderError, "MeshBuilder is null!");

            string sExtension = Path.GetExtension(sFilename);
            if ( sExtension.Length < 2 ) {
                return new IOReadResult(IOCode.InvalidFilenameError, "filename " + sFilename + " does not contain valid extension");
            }
            sExtension = sExtension.Substring(1);

            MeshFormatReader useReader = null;
            foreach (var reader in Readers) {
                foreach (string ext in reader.SupportedExtensions) {
                    if (ext.Equals(sExtension, StringComparison.OrdinalIgnoreCase))
                        useReader = reader;
                }
                if (useReader != null)
                    break;
            }
            if ( useReader == null ) 
                return new IOReadResult(IOCode.UnknownFormatError, "format " + sExtension + " is not supported");

            // save current culture
            var current_culture = Thread.CurrentThread.CurrentCulture;

            try {
                // push invariant culture for write
                if (ReadInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

                var result = useReader.ReadFile(sFilename, MeshBuilder, options, on_warning);

                // restore culture
                if (ReadInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = current_culture;

                return result;

            } catch (Exception e) {
                // restore culture
                if (ReadInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = current_culture;

                return new IOReadResult(IOCode.GenericReaderError, "Unknown error : exception : " + e.Message);
            }
        }


        /// <summary>
        /// Read mesh from a Stream, in the format given by sExtension (without the dot, eg "glb").
        /// Result meshes are appended to the <see cref="MeshBuilder"/> property. The STL reader seeks in the stream,
        /// so use a seekable stream there. External files (OBJ .mtl, glTF .bin buffers) cannot be loaded from a stream.
        /// Does not throw: exceptions are returned as <see cref="IOCode.GenericReaderError"/>.
        /// </summary>
        public IOReadResult Read(Stream stream, string sExtension, ReadOptions options)
        {
            if (MeshBuilder == null)
                return new IOReadResult(IOCode.GenericReaderError, "MeshBuilder is null!");
            MeshFormatReader useReader = null;
            foreach (var reader in Readers) {
                foreach (string ext in reader.SupportedExtensions) {
                    if (ext.Equals(sExtension, StringComparison.OrdinalIgnoreCase))
                        useReader = reader;
                }
                if (useReader != null)
                    break;
            }
            if (useReader == null)
                return new IOReadResult(IOCode.UnknownFormatError, "format " + sExtension + " is not supported");

            // save current culture
            var current_culture = Thread.CurrentThread.CurrentCulture;

            try {
                // push invariant culture for write
                if (ReadInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

                var result = useReader.ReadFile(stream, MeshBuilder, options, on_warning);

                // restore culture
                if (ReadInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = current_culture;

                return result;

            } catch (Exception e) {
                // restore culture
                if (ReadInvariantCulture)
                    Thread.CurrentThread.CurrentCulture = current_culture;

                return new IOReadResult(IOCode.GenericReaderError, "Unknown error : exception : " + e.Message);
            }
        }





        /// <summary>
        /// Open file and call readF on the stream. Only a failure to open the file is reported
        /// as FileAccessError, exceptions thrown by readF are reported as GenericReaderError.
        /// </summary>
        internal static IOReadResult ReadFileStream(string sFilename, FileShare share, Func<Stream, IOReadResult> readF)
        {
            FileStream stream;
            try {
                stream = File.Open(sFilename, FileMode.Open, FileAccess.Read, share);
            } catch (Exception e) {
                return new IOReadResult(IOCode.FileAccessError, "Could not open file " + sFilename + " for reading : " + e.Message);
            }
            using (stream) {
                try {
                    return readF(stream);
                } catch (Exception e) {
                    return new IOReadResult(IOCode.GenericReaderError, "Error reading file " + sFilename + " : " + e.Message);
                }
            }
        }


        /// <summary>
        /// Read mesh file using options and builder, with the default format readers. You must provide your own Builder
        /// here because the reader is not returned. Use a <see cref="DMesh3Builder"/> to get all meshes in the file
        /// (builder.Meshes). See <see cref="Read(string, ReadOptions)"/> for the result codes.
        /// </summary>
        static public IOReadResult ReadFile(string sFilename, ReadOptions options, IMeshBuilder builder)
        {
            StandardMeshReader reader = new StandardMeshReader();
            reader.MeshBuilder = builder;
            return reader.Read(sFilename, options);
        }

        /// <summary>
        /// Read mesh from a Stream using options and builder, in the format given by sExtension (without the dot).
        /// You must provide your own Builder here because the reader is not returned.
        /// See <see cref="Read(Stream, string, ReadOptions)"/>.
        /// </summary>
        static public IOReadResult ReadFile(Stream stream, string sExtension, ReadOptions options, IMeshBuilder builder)
        {
            StandardMeshReader reader = new StandardMeshReader();
            reader.MeshBuilder = builder;
            return reader.Read(stream, sExtension, options);
        }




        /// <summary>
        /// This is basically a utility function, returns first mesh in file, with default options.
        /// Returns null if the read failed (the error code is discarded; use <see cref="ReadFile(string, ReadOptions, IMeshBuilder)"/>
        /// to get it). Throws ArgumentOutOfRangeException if the file was read successfully but contains no mesh.
        /// Other meshes in the file (eg further 3MF build items, glTF nodes, STL solids) are discarded.
        /// </summary>
        static public DMesh3 ReadMesh(string sFilename)
        {
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult result = ReadFile(sFilename, ReadOptions.Defaults, builder);
            return (result.code == IOCode.Ok) ? builder.Meshes[0] : null;
        }


        /// <summary>
        /// This is basically a utility function, returns first mesh in the stream, with default options.
        /// sExtension selects the format (without the dot, eg "ply"). Returns null if the read failed,
        /// throws ArgumentOutOfRangeException if the read succeeded but produced no mesh.
        /// </summary>
        static public DMesh3 ReadMesh(Stream stream, string sExtension)
        {
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult result = ReadFile(stream, sExtension, ReadOptions.Defaults, builder);
            return (result.code == IOCode.Ok) ? builder.Meshes[0] : null;
        }


        private void on_warning(string message, object extra_data)
        {
            if (warningEvent != null)
                warningEvent(message, extra_data);
        }
    }



    /// <summary>
    /// MeshFormatReader impl for OBJ, using <see cref="OBJReader"/>. When reading by filename with
    /// ReadOptions.ReadMaterials, .mtl files are searched in the directory of the .obj file.
    /// </summary>
    public class OBJFormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions { get {
                return new List<string>() { "obj" };
            }
        }


        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.Read, (stream) => {
                OBJReader reader = new OBJReader();
                if (options.ReadMaterials)
                    reader.MTLFileSearchPaths.Add(Path.GetDirectoryName(sFilename));
                reader.warningEvent += messages;

                var result = reader.Read(new StreamReader(stream), options, builder);
                return result;
            });
        }

        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            OBJReader reader = new OBJReader();
            reader.warningEvent += messages;
            var result = reader.Read(new StreamReader(stream), options, builder);
            return result;
        }
    }



    /// <summary>
    /// MeshFormatReader impl for STL, using <see cref="STLReader"/>. Ascii or binary is detected from the
    /// first bytes of the stream (the stream must be seekable).
    /// </summary>
    public class STLFormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions { get {
                return new List<string>() { "stl" };
            }
        }


        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.Read, (stream) => ReadFile(stream, builder, options, messages));
        }

        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            // detect binary STL
            //BinaryReader binReader = new BinaryReader(stream);
            //byte[] header = binReader.ReadBytes(80);
            bool bIsBinary = Util.IsBinaryStream(stream, 500);

            // [RMS] Thingi10k includes some files w/ unicode string in ascii header...
            //   How can we detect this? can we check that each character is a character?
            //string sUTF8 = System.Text.Encoding.UTF8.GetString(header);
            //string sAscii = System.Text.Encoding.ASCII.GetString(header);
            //if (sUTF8.Contains("solid") == false && sAscii.Contains("solid") == false)
            //    bIsBinary = true;

            // if first 80 bytes contain non-text chars, probably a binary file
            //if (Util.IsTextString(header) == false)
            //    bIsBinary = true;
            //// if we don't see "solid" string in first 80 chars, probably binary
            //if (bIsBinary == false) {
            //    string sText = System.Text.Encoding.ASCII.GetString(header);
            //    if (sText.Contains("solid") == false)
            //        bIsBinary = true;
            //}

            stream.Seek(0, SeekOrigin.Begin); // reset stream

            STLReader reader = new STLReader();
            reader.warningEvent += messages;
            IOReadResult result = (bIsBinary) ?
                reader.Read(new BinaryReader(stream), options, builder) :
                reader.Read(new StreamReader(stream), options, builder);

            return result;
        }

    }




    /// <summary>
    /// MeshFormatReader impl for ascii OFF
    /// </summary>
    public class OFFFormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions { get {
                return new List<string>() { "off" };
            }
        }


        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.Read, (stream) => ReadFile(stream, builder, options, messages));
        }

        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            OFFReader reader = new OFFReader();
            reader.warningEvent += messages;
            var result = reader.Read(new StreamReader(stream), options, builder);
            return result;
        }
    }



    /// <summary>
    /// MeshFormatReader impl for g3mesh, using <see cref="BinaryG3Reader"/>
    /// </summary>
    public class BinaryG3FormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions {
            get {
                return new List<string>() { "g3mesh" };
            }
        }

        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.None, (stream) => ReadFile(stream, builder, options, messages));
        }

        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            BinaryG3Reader reader = new BinaryG3Reader();
            //reader.warningEvent += messages;
            IOReadResult result = reader.Read(new BinaryReader(stream), options, builder);
            return result;
        }

    }



    /// <summary>
    /// MeshFormatReader impl for PLY (ascii and binary), using <see cref="PLYReader"/>
    /// </summary>
    public class PLYFormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions { get {
                return new List<string>() { "ply" };
            }
        }

        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.Read, (stream) => ReadFile(stream, builder, options, messages));
        }

        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            PLYReader reader = new PLYReader();
            reader.warningEvent += messages;
            return reader.Read(new BinaryReader(new BufferedStream(stream)), options, builder);
        }
    }



    /// <summary>
    /// MeshFormatReader impl for 3MF, using <see cref="ThreeMFReader"/> with its defaults
    /// (one mesh per build item, converted to millimeters)
    /// </summary>
    public class ThreeMFFormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions { get {
                return new List<string>() { "3mf" };
            }
        }

        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.Read, (stream) => ReadFile(stream, builder, options, messages));
        }

        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            ThreeMFReader reader = new ThreeMFReader();
            reader.warningEvent += messages;
            return reader.Read(stream, options, builder);
        }
    }



    /// <summary>
    /// MeshFormatReader impl for glTF 2.0 (.gltf and .glb), using <see cref="GLTFReader"/> with its defaults
    /// (one mesh per scene node that references a mesh, node transforms applied). When reading by filename,
    /// external buffers are loaded from relative paths in the directory of the .gltf file or below it (not ../).
    /// ReadOptions are ignored.
    /// </summary>
    public class GLTFFormatReader : MeshFormatReader
    {
        public List<string> SupportedExtensions { get {
                return new List<string>() { "gltf", "glb" };
            }
        }

        public IOReadResult ReadFile(string sFilename, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return StandardMeshReader.ReadFileStream(sFilename, FileShare.Read, (stream) => {
                string dir = Path.GetDirectoryName(Path.GetFullPath(sFilename));
                return read(stream, builder, messages, (uri) => read_relative(dir, uri));
            });
        }

        // external buffers (.bin files) cannot be resolved when reading from a stream
        public IOReadResult ReadFile(Stream stream, IMeshBuilder builder, ReadOptions options, ParsingMessagesHandler messages)
        {
            return read(stream, builder, messages, null);
        }

        // buffers must be relative paths to files in the directory of the .gltf file or below it; absolute paths,
        // other URI schemes (http:, file:) and paths that leave the directory (../) are not read, so a file cannot pull
        // in arbitrary local files (eg ../../home/user/.ssh/id_rsa or ../../dev/zero) as buffer data. Symbolic links
        // are not resolved: a linked subdirectory is trusted.
        static byte[] read_relative(string dir, string uri)
        {
            if (Path.IsPathRooted(uri) || uri.Contains(":"))
                throw new IOException("only relative buffer paths are supported: " + uri);
            string root = dir.EndsWith(Path.DirectorySeparatorChar.ToString()) ? dir : dir + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, uri));
            // Windows paths are case-insensitive (../Model/x.bin from inside model stays inside)
            StringComparison cmp = (Path.DirectorySeparatorChar == '\\') ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (path.StartsWith(root, cmp) == false)
                throw new IOException("buffer paths outside the directory of the glTF file are not read: " + uri);
            return File.ReadAllBytes(path);
        }

        IOReadResult read(Stream stream, IMeshBuilder builder, ParsingMessagesHandler messages, Func<string, byte[]> resolve)
        {
            GLTFReader reader = new GLTFReader() { ResolveUri = resolve };
            reader.warningEvent += messages;
            return reader.Read(stream, ReadOptions.Defaults, builder);
        }
    }


}
