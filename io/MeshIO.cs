using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace g3
{

    /// <summary>
    /// Status code returned by mesh readers and writers in <see cref="IOReadResult"/> and <see cref="IOWriteResult"/>.
    /// The mesh I/O classes report most failures through these codes instead of throwing,
    /// so always check that the code is <see cref="Ok"/>.
    /// </summary>
    public enum IOCode
    {
        /// <summary> Success </summary>
        Ok = 0,

        /// <summary> File could not be opened (or a referenced file, eg a glTF .bin buffer, could not be read) </summary>
        FileAccessError = 1,
        /// <summary> No reader/writer is registered for the file extension </summary>
        UnknownFormatError = 2,
        /// <summary> Format is recognized but this variant or feature is not supported (eg glTF 1.0, Draco compression, binary OFF) </summary>
        FormatNotSupportedError = 3,
        /// <summary> Filename has no usable extension </summary>
        InvalidFilenameError = 4,

        // read errors
        /// <summary> File content is malformed or could not be parsed </summary>
        FileParsingError = 100,
        /// <summary> File parsed but contains no usable data (eg OBJ without vertices or triangles) </summary>
        GarbageDataError = 101,
        /// <summary> Other read failure, including exceptions caught by <see cref="StandardMeshReader"/> </summary>
        GenericReaderError = 102,
        /// <summary> Not currently returned by the built-in readers (warnings go to the warningEvent handlers) </summary>
        GenericReaderWarning = 103,

        // write errors
        /// <summary> Write failed, including exceptions caught by <see cref="StandardMeshWriter"/> </summary>
        WriterError = 200,


        // other status
        /// <summary> Not used by the built-in readers/writers </summary>
        ComputingInWorkerThread = 1000
    }



    /// <summary>
    /// Options passed to mesh readers. Use <see cref="Defaults"/> or a new instance.
    /// Most readers ignore these options; format-specific settings are fields on the
    /// format reader classes (eg <see cref="ThreeMFReader.ConvertToMillimeters"/>, <see cref="STLReader.RebuildStrategy"/>).
    /// </summary>
    public class ReadOptions
    {
        /// <summary>
        /// If true, OBJ files load their .mtl material libraries (searched in the directory of the
        /// .obj file when reading by filename). Ignored by the other formats. Default false.
        /// </summary>
		public bool ReadMaterials;

        /// <summary>
        /// Format-specific arguments for readers that have their own settings.
        /// Currently only <see cref="STLReader"/> reads these (<see cref="STLReader.StrategyFlag"/>, <see cref="STLReader.PerTriAttribFlag"/>).
        /// </summary>
        public CommandArgumentSet CustomFlags = new CommandArgumentSet();

        public ReadOptions()
        {
			ReadMaterials = false;
        }

        /// <summary>
        /// Default options (no materials). This is a shared instance, do not modify it.
        /// </summary>
        public static readonly ReadOptions Defaults = new ReadOptions() {
            ReadMaterials = false
        };
    }

    /// <summary>
    /// Result of a mesh read: a status <see cref="code"/> and a human-readable <see cref="message"/>.
    /// Check code == IOCode.Ok; on failure the message describes the error.
    /// </summary>
    public struct IOReadResult
    {
        /// <summary> Status code, <see cref="IOCode.Ok"/> on success </summary>
        public IOCode code { get; set; }
        /// <summary> Description of the error (or "(no message)") </summary>
        public string message { get; set; }
        public IOReadResult(IOCode r, string s) : this()
        {
            code = r;
            message = s;
            if (message == "")
                message = "(no message)";
        }

        /// <summary> Successful result </summary>
		public static readonly IOReadResult Ok = new IOReadResult(IOCode.Ok, "");	
	}



    /// <summary>
    /// Low-level interface for format readers that parse from a TextReader or BinaryReader
    /// and emit meshes into an <see cref="IMeshBuilder"/> (usually a <see cref="DMesh3Builder"/>).
    /// Some readers support only one of the two methods and return an error code or throw for the other.
    /// </summary>
    public interface IMeshReader
    {
        IOReadResult Read(TextReader reader, ReadOptions options, IMeshBuilder builder);
        IOReadResult Read(BinaryReader reader, ReadOptions options, IMeshBuilder builder);
    }



    /// <summary>
    /// Result of a mesh write: a status <see cref="code"/> and a human-readable <see cref="message"/>.
    /// Check code == IOCode.Ok; on failure the message describes the error.
    /// </summary>
    public struct IOWriteResult
    {
        /// <summary> Status code, <see cref="IOCode.Ok"/> on success </summary>
        public IOCode code { get; set; }
        /// <summary> Description of the error (or "(no message)") </summary>
        public string message { get; set; }
        public IOWriteResult( IOCode r, string s ) : this()
        {
            code = r;
            message = s;
            if (message == "")
                message = "(no message)";
        }

        /// <summary> Successful result </summary>
		public static readonly IOWriteResult Ok = new IOWriteResult(IOCode.Ok, "");
    }

    /// <summary>
    /// Options passed to mesh writers. Start from <see cref="Defaults"/> and change fields as needed:
    /// this is a struct, so new WriteOptions() leaves RealPrecisionDigits at 0, which writes
    /// ascii STL/OFF coordinates with no decimals and makes PLY use float.
    /// Each writer uses only the fields that apply to its format; the others are ignored.
    /// </summary>
    public struct WriteOptions
    {
        /// <summary> Write the binary variant of the format: STL (binary STL) and PLY (binary_little_endian). Ignored by other formats. </summary>
		public bool bWriteBinary;

        /// <summary> Write per-vertex normals, if the mesh has them (OBJ, PLY, glTF) </summary>
		public bool bPerVertexNormals;
        /// <summary> Write per-vertex colors, if the mesh has them (OBJ, PLY, glTF) </summary>
		public bool bPerVertexColors;
        /// <summary> Write per-vertex UVs, if the mesh has them (OBJ, PLY, glTF). For OBJ, a separate UV layer in <see cref="WriteMesh.UVs"/> takes precedence. </summary>
		public bool bPerVertexUVs;
        /// <summary> Write triangle groups as OBJ "g" groups (OBJ only) </summary>
		public bool bWriteGroups;

        /// <summary>
        /// Combine all input meshes into a single output object: one solid in ascii STL, one object in 3MF.
        /// Ignored by other formats (OFF, PLY and binary STL always write a single object; OBJ and glTF ignore it).
        /// </summary>
        public bool bCombineMeshes;

        /// <summary>
        /// Digits after the decimal point for ascii STL and OFF coordinates. For PLY, values above 7 write
        /// positions as double, otherwise float. Ignored by other formats. Defaults uses 15.
        /// </summary>
		public int RealPrecisionDigits;

        /// <summary> For OBJ, write the materials in <see cref="WriteMesh.Materials"/> to a .mtl file at <see cref="MaterialFilePath"/> </summary>
		public bool bWriteMaterials;
        /// <summary> Path of the .mtl file to write. Only used if bWriteMaterials = true, and must then be non-null. </summary>
		public string MaterialFilePath;

        /// <summary> Prefix for OBJ group names, followed by the group ID. If null, OBJWriter uses "mmGroup". </summary>
        public string groupNamePrefix;
        /// <summary> If non-null, generates the OBJ group name for a group ID (replaces groupNamePrefix) </summary>
        public Func<int, string> GroupNameF;


        /// <summary> Progress callback, called with (current, total). The meaning of the counts differs per writer. </summary>
        public Action<int, int> ProgressFunc;

        /// <summary>
        /// If set, the returned string is written at the start of the file: as-is as the first line of OBJ files
        /// (so include a leading "#" for a comment), and as comment lines in the PLY header. Ignored by other formats.
        /// </summary>
        public Func<string> AsciiHeaderFunc;

        /// <summary>
        /// Default options: ascii, no normals/colors/UVs/groups/materials, meshes not combined, RealPrecisionDigits = 15.
        /// </summary>
        public static readonly WriteOptions Defaults = new WriteOptions() {
            bWriteBinary = false,
            bPerVertexNormals = false,
            bPerVertexColors = false,
            bWriteGroups = false,
            bPerVertexUVs = false,
            bCombineMeshes = false,
			bWriteMaterials = false,
            ProgressFunc = null,

            RealPrecisionDigits = 15       // double
            //RealPrecisionDigits = 7        // float
        };
    }


    /// <summary>
    /// A mesh to write, plus optional per-mesh data used by some formats.
    /// Writers take a List&lt;WriteMesh&gt;; <see cref="StandardMeshWriter.WriteMesh"/> wraps a single mesh for you.
    /// </summary>
    public struct WriteMesh
    {
        /// <summary> The mesh to write </summary>
        public IMesh Mesh;
        /// <summary> Mesh name, written by ascii STL (solid name), 3MF (object name) and glTF (mesh/node name) </summary>
        public string Name;

        /// <summary> Set of materials (possibly) used in this mesh. Only written by OBJ, see <see cref="WriteOptions.bWriteMaterials"/>. </summary>
		public List<GenericMaterial> Materials;
        /// <summary> Maps triangle index to an index in <see cref="Materials"/> (OBJ only) </summary>
		public IIndexMap TriToMaterialMap;

        /// <summary>
        /// Separate UV layer (just one for now), only written by OBJ. Assumes the UV mesh has the same triangles as Mesh.
        /// </summary>
		public DenseUVMesh UVs;

        public WriteMesh(IMesh mesh, string name = "") {
            Mesh = mesh;
            Name = name;
            UVs = null;
			Materials = null;
			TriToMaterialMap = null;
        }
    }

    
    /// <summary>
    /// Low-level interface for format writers that write a list of meshes to a TextWriter or BinaryWriter.
    /// Some writers support only one of the two methods and return an error code or throw for the other.
    /// Use <see cref="StandardMeshWriter"/> to pick the writer from a file extension.
    /// </summary>
    public interface IMeshWriter
    {
        IOWriteResult Write(TextWriter writer, List<WriteMesh> vMeshes, WriteOptions options);
        IOWriteResult Write(BinaryWriter writer, List<WriteMesh> vMeshes, WriteOptions options);
    }


}
