using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace g3 
{
	/// <summary>
	/// Helper functions shared by the mesh writers
	/// </summary>
	public static class MeshIOUtil 
	{
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.NumberStyles Flt = System.Globalization.NumberStyles.Float;

        // Is the platform's double.Parse correctly rounded and does it keep the sign of "-0"? (.NET Core 3.0 and later: yes;
        // .NET Framework: no.) If not, ExactDecimal parses.
        static readonly bool PlatformDoubleParseIsExact = ProbeDoubleParse();
        static readonly bool PlatformFloatParseIsExact = ProbeFloatParse();

        /// <summary> (tests) use ExactDecimal on every framework, to test the readers' use of it where the platform parser is exact </summary>
        internal static volatile bool ForceExactDecimal;

        static bool ProbeDoubleParse()
        {
            try {
                return BitConverter.DoubleToInt64Bits(double.Parse("5.6348333975312060066e-8", Flt, Inv)) == 0x3e6e4074bfc9cfd2L
                    && BitConverter.DoubleToInt64Bits(double.Parse("-0", Flt, Inv)) == unchecked((long)0x8000000000000000UL)
                    && double.Parse("9007199254740993", Flt, Inv) == 9007199254740992.0                       // halfway: to even
                    && double.Parse("9007199254740993.00000000000000000001", Flt, Inv) == 9007199254740994.0;  // just above halfway
            } catch (Exception) {
                return false;
            }
        }

        static bool ProbeFloatParse()
        {
            try {
                return BitConverter.ToInt32(BitConverter.GetBytes(float.Parse("-0", Flt, Inv)), 0) == int.MinValue
                    && float.Parse("16777217", Flt, Inv) == 16777216f
                    && float.Parse("16777217.00000000000000000001", Flt, Inv) == 16777218f;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>
        /// A coordinate in invariant culture (NumberStyles.Float), with a value beyond the double range read as
        /// infinity of its sign on every framework: .NET Framework throws OverflowException for eg "1e400", .NET Core
        /// returns infinity. (ReadOptions.AllowNonFiniteCoordinates then decides.) Correctly rounded on every framework
        /// (.NET Framework's own parser is not, and reads "-0" as +0: ExactDecimal).
        /// </summary>
        internal static double ParseDouble(string s)
        {
            double v;
            if ((!PlatformDoubleParseIsExact || ForceExactDecimal) && ExactDecimal.TryParseDouble(s, out v)) return v;
            try {
                return double.Parse(s, Flt, Inv);
            } catch (OverflowException) {
                return s.TrimStart().StartsWith("-") ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        /// <summary>
        /// The text of a double that reads back to the same value under a correctly rounded parser, in invariant culture: the
        /// shortest such digit string (<see cref="ShortestDecimal"/>), the same on every framework and in the layout of .NET Core
        /// 3.0 and later "R" ("0.1", "1E-05", "1.5E+20", "-0" for a negative zero). Not a number is "NaN", "Infinity",
        /// "-Infinity".
        /// </summary>
        public static string FormatDouble(double v)
        {
            return ShortestDecimal.Format(v);
        }

        // The name of the former .NET Framework path (first of G15, G16, G17 that the platform parser reads back), kept so that the
        // tests of that path run on every framework: it is the shortest text now, as FormatDouble.
        internal static string FormatDoubleByDigits(double v) { return FormatDouble(v); }
        internal static string FormatFloatByDigits(float v) { return FormatFloat(v); }

        /// <summary> as <see cref="FormatDouble"/>, for float (at most 9 significant digits) </summary>
        public static string FormatFloat(float v)
        {
            return ShortestDecimal.Format(v);
        }

        /// <summary> as <see cref="ParseDouble"/>, for float </summary>
        internal static float ParseFloat(string s)
        {
            float v;
            if ((!PlatformFloatParseIsExact || ForceExactDecimal) && ExactDecimal.TryParseFloat(s, out v)) return v;
            try {
                return float.Parse(s, Flt, Inv);
            } catch (OverflowException) {
                return s.TrimStart().StartsWith("-") ? float.NegativeInfinity : float.PositiveInfinity;
            }
        }

        /// <summary> as <see cref="ParseDouble"/>, false instead of an exception for text that is not a number </summary>
        internal static bool TryParseDouble(string s, out double value)
        {
            if ((!PlatformDoubleParseIsExact || ForceExactDecimal) && ExactDecimal.TryParseDouble(s, out value)) return true;
            if (double.TryParse(s, Flt, Inv, out value)) return true;
            value = 0;
            return false;
        }

		
		/// <summary>
		/// Returns the distinct materials of all WriteMesh.Materials lists, in first-seen order
		/// </summary>
		public static List<GenericMaterial> FindUniqueMaterialList(List<WriteMesh> meshes) 
		{
			List<GenericMaterial> unique = new List<GenericMaterial>();
			foreach (WriteMesh mesh in meshes) {
				if (mesh.Materials == null )
					continue;
				foreach ( GenericMaterial mat in mesh.Materials ) {
					if ( unique.Contains(mat) == false )
						unique.Add(mat);
				}
			}
			return unique;
		}
	}


	/// <summary>
	/// OBJ and PLY reading and writing of <see cref="DPolyMesh"/> with the faces kept as written (quads, n-gons).
	/// The standard readers and writers (<see cref="StandardMeshReader"/>, <see cref="OBJWriter"/>, <see cref="PLYWriter"/>)
	/// are not changed: they triangulate on read and write triangle meshes only, byte for byte as before.
	///
	/// Writers: positions as shortest round-trip text (OBJ always, PLY as double unless doublePrecision is false), faces
	/// with their vertex lists in face order, face groups if any is non-zero. Faces that cannot be written (fewer
	/// than 3 vertices, an out-of-range index) are left out; repeated vertices are written as they are. The result is
	/// always Ok (exceptions of the stream are not caught). OBJ: "f i j k l" with 1-based indices and "g mmGroup{id}"
	/// whenever the group changes. PLY ("ascii" or "binary_little_endian"): "property list uchar int vertex_indices"
	/// ("int" as count type if a face has more than 255 vertices) and, if groups are used, "property int face_group".
	///
	/// Readers: <see cref="ReadPLY"/> uses <see cref="PLYReader"/> (all its variants) with a <see cref="DPolyMeshBuilder"/>, so
	/// polygons stay whole and "face_group" is the group. <see cref="ReadOBJ"/> is a small separate parser: "v" (first
	/// three numbers; a line that does not parse is skipped, as in <see cref="OBJReader"/>) and "f" lines (v, v/vt,
	/// v//vn, v/vt/vn; negative indices count back from the last vertex so far) and "g" lines; normals, UVs, colors,
	/// materials and objects are ignored. A face with fewer than 3 corners or an index that is zero, unparseable or
	/// beyond the vertices is skipped with a warning (<c>warnings</c>); repeated vertices are kept (see
	/// <see cref="DPolyMesh.Validate"/>). A group named "mmGroup{k}" is group k, any other name gets the next free
	/// group number starting at 1 (in order of appearance); faces before the first "g" line are in group 0.
	/// A file without vertices or faces returns <see cref="IOCode.GarbageDataError"/>.
	/// </summary>
	public static class PolyMeshIO
	{
		// faces that a writer can write
		static bool Writable(DPolyMesh mesh, int f)
		{
			int a = mesh.FaceOffsets[f], b = mesh.FaceOffsets[f + 1], nv = mesh.VertexCount;
			if (b - a < 3) return false;
			for (int k = a; k < b; ++k) {
				int v = mesh.FaceIndices[k];
				if (v < 0 || v >= nv) return false;
			}
			return true;
		}

		static string Real(double v, bool doublePrecision)
		{
			return doublePrecision ? MeshIOUtil.FormatDouble(v) : MeshIOUtil.FormatFloat((float)v);
		}

		/// <summary> Write OBJ text (positions, faces, groups), see the class remarks. </summary>
		public static IOWriteResult WriteOBJ(TextWriter writer, DPolyMesh mesh)
		{
			if (writer == null) throw new ArgumentNullException("writer");
			if (mesh == null) throw new ArgumentNullException("mesh");
			bool groups = false;
			for (int f = 0; f < mesh.FaceCount && !groups; ++f)
				groups = mesh.FaceGroups[f] != 0;

			StringBuilder line = new StringBuilder();
			for (int i = 0; i < mesh.VertexCount; ++i) {
				Vector3d v = mesh.GetVertex(i);
				line.Clear();
				line.Append("v ").Append(Real(v.x, true)).Append(' ').Append(Real(v.y, true)).Append(' ').Append(Real(v.z, true));
				writer.WriteLine(line.ToString());
			}
			int group = int.MinValue;
			for (int f = 0; f < mesh.FaceCount; ++f) {
				if (!Writable(mesh, f)) continue;
				if (groups && mesh.FaceGroups[f] != group) {
					group = mesh.FaceGroups[f];
					writer.WriteLine("g mmGroup" + group.ToString(CultureInfo.InvariantCulture));
				}
				line.Clear();
				line.Append('f');
				for (int k = mesh.FaceOffsets[f]; k < mesh.FaceOffsets[f + 1]; ++k)
					line.Append(' ').Append((mesh.FaceIndices[k] + 1).ToString(CultureInfo.InvariantCulture));
				writer.WriteLine(line.ToString());
			}
			return IOWriteResult.Ok;
		}

		struct PlyLayout { public int nFaces, maxFace; public bool groups; }

		static PlyLayout MakeLayout(DPolyMesh mesh)
		{
			PlyLayout l = new PlyLayout();
			for (int f = 0; f < mesh.FaceCount; ++f) {
				if (!Writable(mesh, f)) continue;
				l.nFaces++;
				l.maxFace = Math.Max(l.maxFace, mesh.FaceVertexCount(f));
				if (mesh.FaceGroups[f] != 0) l.groups = true;
			}
			return l;
		}

		static string PlyHeader(DPolyMesh mesh, PlyLayout l, string format, bool doublePrecision)
		{
			CultureInfo ci = CultureInfo.InvariantCulture;
			string real = doublePrecision ? "double" : "float";
			StringBuilder sb = new StringBuilder();
			sb.Append("ply\nformat ").Append(format).Append(" 1.0\ncomment written by geometry3Sharp\n");
			sb.Append("element vertex ").Append(mesh.VertexCount.ToString(ci)).Append('\n');
			sb.Append("property ").Append(real).Append(" x\nproperty ").Append(real).Append(" y\nproperty ").Append(real).Append(" z\n");
			if (l.nFaces > 0) {
				sb.Append("element face ").Append(l.nFaces.ToString(ci)).Append('\n');
				sb.Append("property list ").Append(l.maxFace > 255 ? "int" : "uchar").Append(" int vertex_indices\n");
				if (l.groups)
					sb.Append("property int face_group\n");
			}
			sb.Append("end_header\n");
			return sb.ToString();
		}

		/// <summary> Write ascii PLY, see the class remarks. </summary>
		public static IOWriteResult WritePLY(TextWriter writer, DPolyMesh mesh, bool doublePrecision = true)
		{
			if (writer == null) throw new ArgumentNullException("writer");
			if (mesh == null) throw new ArgumentNullException("mesh");
			PlyLayout l = MakeLayout(mesh);
			writer.Write(PlyHeader(mesh, l, "ascii", doublePrecision));
			StringBuilder line = new StringBuilder();
			for (int i = 0; i < mesh.VertexCount; ++i) {
				Vector3d v = mesh.GetVertex(i);
				line.Clear();
				line.Append(Real(v.x, doublePrecision)).Append(' ').Append(Real(v.y, doublePrecision)).Append(' ').Append(Real(v.z, doublePrecision));
				writer.WriteLine(line.ToString());
			}
			for (int f = 0; f < mesh.FaceCount; ++f) {
				if (!Writable(mesh, f)) continue;
				line.Clear();
				line.Append(mesh.FaceVertexCount(f).ToString(CultureInfo.InvariantCulture));
				for (int k = mesh.FaceOffsets[f]; k < mesh.FaceOffsets[f + 1]; ++k)
					line.Append(' ').Append(mesh.FaceIndices[k].ToString(CultureInfo.InvariantCulture));
				if (l.groups)
					line.Append(' ').Append(mesh.FaceGroups[f].ToString(CultureInfo.InvariantCulture));
				writer.WriteLine(line.ToString());
			}
			return IOWriteResult.Ok;
		}

		/// <summary> Write binary_little_endian PLY, see the class remarks. </summary>
		public static IOWriteResult WritePLY(BinaryWriter writer, DPolyMesh mesh, bool doublePrecision = true)
		{
			if (writer == null) throw new ArgumentNullException("writer");
			if (mesh == null) throw new ArgumentNullException("mesh");
			PlyLayout l = MakeLayout(mesh);
			writer.Write(Encoding.ASCII.GetBytes(PlyHeader(mesh, l, "binary_little_endian", doublePrecision)));
			for (int i = 0; i < mesh.VertexCount; ++i) {
				Vector3d v = mesh.GetVertex(i);
				if (doublePrecision) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
				else { writer.Write((float)v.x); writer.Write((float)v.y); writer.Write((float)v.z); }
			}
			for (int f = 0; f < mesh.FaceCount; ++f) {
				if (!Writable(mesh, f)) continue;
				int n = mesh.FaceVertexCount(f);
				if (l.maxFace > 255) writer.Write(n); else writer.Write((byte)n);
				for (int k = mesh.FaceOffsets[f]; k < mesh.FaceOffsets[f + 1]; ++k)
					writer.Write(mesh.FaceIndices[k]);
				if (l.groups)
					writer.Write(mesh.FaceGroups[f]);
			}
			return IOWriteResult.Ok;
		}

		/// <summary>
		/// Write a file by its extension: ".obj", or ".ply" (binary_little_endian if binaryPly, else ascii).
		/// Returns FormatNotSupportedError for other extensions.
		/// </summary>
		public static IOWriteResult WriteFile(string path, DPolyMesh mesh, bool binaryPly = false)
		{
			string ext = Path.GetExtension(path).ToLowerInvariant();
			if (ext == ".obj") {
				using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
					return WriteOBJ(w, mesh);
			} else if (ext == ".ply") {
				if (binaryPly) {
					using (BinaryWriter w = new BinaryWriter(File.Open(path, FileMode.Create)))
						return WritePLY(w, mesh);
				}
				using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
					return WritePLY(w, mesh);
			}
			return new IOWriteResult(IOCode.FormatNotSupportedError, "unsupported extension " + ext);
		}


		/// <summary>
		/// Read ascii or binary PLY with the faces kept whole, see the class remarks. mesh is null if the result is not Ok.
		/// A PLY file without a face element gives a mesh with vertices only.
		/// </summary>
		public static IOReadResult ReadPLY(BinaryReader reader, ReadOptions options, out DPolyMesh mesh, ParsingMessagesHandler warnings = null)
		{
			PLYReader plyReader = new PLYReader();
			if (warnings != null) plyReader.warningEvent += warnings;
			DPolyMeshBuilder builder = new DPolyMeshBuilder();
			IOReadResult r = plyReader.Read(reader, options, builder);
			mesh = (r.code == IOCode.Ok) ? builder.Mesh : null;
			return r;
		}

		/// <summary> Read OBJ text with the faces kept whole, see the class remarks. mesh is null if the result is not Ok. </summary>
		public static IOReadResult ReadOBJ(TextReader reader, ReadOptions options, out DPolyMesh mesh, ParsingMessagesHandler warnings = null)
		{
			mesh = null;
			List<double> xyz = new List<double>();
			List<int> counts = new List<int>(), idx = new List<int>(), faceGroups = new List<int>();
			Dictionary<string, int> names = new Dictionary<string, int>();
			HashSet<int> used = new HashSet<int>();
			int nextGroup = 1, group = 0, lineNo = 0, skipped = 0;
			List<int> face = new List<int>();
			char[] ws = null;
			string line;
			while ((line = reader.ReadLine()) != null) {
				lineNo++;
				string[] tokens = line.Split(ws, StringSplitOptions.RemoveEmptyEntries);
				if (tokens.Length == 0) continue;
				string key = tokens[0];
				if (key == "v") {
					double x, y, z;
					if (tokens.Length >= 4 && MeshIOUtil.TryParseDouble(tokens[1], out x) && MeshIOUtil.TryParseDouble(tokens[2], out y) && MeshIOUtil.TryParseDouble(tokens[3], out z)) {
						if (ReadOptions.RejectsPosition(options, x, y, z))
							return ReadOptions.NonFinitePosition(x, y, z);
						xyz.Add(x); xyz.Add(y); xyz.Add(z);
					} else if (warnings != null)
						warnings("[PolyMeshIO] OBJ line " + lineNo + ": vertex skipped", null);
				} else if (key == "f") {
					face.Clear();
					bool ok = tokens.Length >= 4;
					for (int t = 1; ok && t < tokens.Length; ++t) {
						int slash = tokens[t].IndexOf('/');
						string s = slash < 0 ? tokens[t] : tokens[t].Substring(0, slash);
						int v;
						if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v) || v == 0) { ok = false; break; }
						face.Add(v > 0 ? v - 1 : xyz.Count / 3 + v);
					}
					if (!ok) { skipped++; continue; }
					counts.Add(face.Count); idx.AddRange(face); faceGroups.Add(group);
				} else if (key == "g") {
					string name = tokens.Length == 1 ? "default" : (tokens.Length == 2 ? tokens[1] : line.Substring(line.IndexOf(tokens[1], StringComparison.Ordinal)));
					int id;
					if (!names.TryGetValue(name, out id)) {
						int k;
						if (name.StartsWith("mmGroup", StringComparison.Ordinal) && int.TryParse(name.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out k))
							id = k;
						else {
							while (used.Contains(nextGroup)) nextGroup++;
							id = nextGroup;
						}
						names[name] = id; used.Add(id);
					}
					group = id;
				}
			}

			// faces beyond the vertices are dropped
			int nv = xyz.Count / 3;
			DPolyMesh m = new DPolyMesh(nv, counts.Count, idx.Count);
			m.AppendVertices(xyz);
			int at = 0;
			for (int f = 0; f < counts.Count; ++f) {
				bool ok = true;
				for (int k = 0; k < counts[f]; ++k)
					if (idx[at + k] < 0 || idx[at + k] >= nv) { ok = false; break; }
				if (ok) m.AppendFace(new ArraySegmentList(idx, at, counts[f]), faceGroups[f]);
				else skipped++;
				at += counts[f];
			}
			if (skipped > 0 && warnings != null)
				warnings("[PolyMeshIO] skipped " + skipped + " OBJ faces with fewer than 3 corners or invalid indices", null);
			if (nv == 0 || m.FaceCount == 0)
				return new IOReadResult(IOCode.GarbageDataError, "no vertices or no faces in OBJ");
			mesh = m;
			return IOReadResult.Ok;
		}

		// a window of a list as IList<int>, so that a face is appended without a copy
		sealed class ArraySegmentList : IList<int>
		{
			readonly List<int> list; readonly int start, count;
			public ArraySegmentList(List<int> list, int start, int count) { this.list = list; this.start = start; this.count = count; }
			public int this[int i] { get { return list[start + i]; } set { throw new NotSupportedException(); } }
			public int Count { get { return count; } }
			public bool IsReadOnly { get { return true; } }
			public void CopyTo(int[] array, int arrayIndex) { list.CopyTo(start, array, arrayIndex, count); }
			public IEnumerator<int> GetEnumerator() { for (int i = 0; i < count; ++i) yield return list[start + i]; }
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
			public int IndexOf(int item) { throw new NotSupportedException(); }
			public void Insert(int index, int item) { throw new NotSupportedException(); }
			public void RemoveAt(int index) { throw new NotSupportedException(); }
			public void Add(int item) { throw new NotSupportedException(); }
			public void Clear() { throw new NotSupportedException(); }
			public bool Contains(int item) { throw new NotSupportedException(); }
			public bool Remove(int item) { throw new NotSupportedException(); }
		}

		/// <summary>
		/// Read a file by its extension: ".obj" or ".ply". Returns FormatNotSupportedError for other extensions and
		/// FileAccessError if the file cannot be opened.
		/// </summary>
		public static IOReadResult ReadFile(string path, ReadOptions options, out DPolyMesh mesh, ParsingMessagesHandler warnings = null)
		{
			mesh = null;
			string ext = Path.GetExtension(path).ToLowerInvariant();
			if (ext != ".obj" && ext != ".ply")
				return new IOReadResult(IOCode.FormatNotSupportedError, "unsupported extension " + ext);
			Stream stream;
			try {
				stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			} catch (Exception e) {
				return new IOReadResult(IOCode.FileAccessError, e.Message);
			}
			using (stream) {
				if (ext == ".ply")
					return ReadPLY(new BinaryReader(stream), options, out mesh, warnings);
				return ReadOBJ(new StreamReader(stream, Encoding.UTF8), options, out mesh, warnings);
			}
		}
	}
}
