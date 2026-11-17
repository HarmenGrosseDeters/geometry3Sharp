using System;
using System.Collections.Generic;
using System.IO;

namespace g3
{
    /// <summary>
    /// SimpleTriangleMesh but for quads. Data packed into buffers, no dynamics.
    /// Supports Per-Vertex Normals, Colors, UV, and Per-Quad Facegroup.
    /// 
    /// use static WriteOBJ() to save. No loading, for now. 
    /// 
    /// </summary>
    public class SimpleQuadMesh
    {
        public DVector<double> Vertices;
        public DVector<float> Normals;
        public DVector<float> Colors;
        public DVector<float> UVs;

        public DVector<int> Quads;
        public DVector<int> FaceGroups;

        public SimpleQuadMesh()
        {
            Initialize();
        }

        public void Initialize(bool bWantNormals = true, bool bWantColors = true, bool bWantUVs = true, bool bWantFaceGroups = true)
        {
            Vertices = new DVector<double>();
            Normals = (bWantNormals) ? new DVector<float>() : null;
            Colors = (bWantColors) ? new DVector<float>() : null;
            UVs = (bWantUVs) ? new DVector<float>() : null;
            Quads = new DVector<int>();
            FaceGroups = (bWantFaceGroups) ? new DVector<int>() : null;
        }

        public MeshComponents Components {
            get {
                MeshComponents c = 0;
                if (Normals != null) c |= MeshComponents.VertexNormals;
                if (Colors != null) c |= MeshComponents.VertexColors;
                if (UVs != null) c |= MeshComponents.VertexUVs;
                if (FaceGroups != null) c |= MeshComponents.FaceGroups;
                return c;
            }
        }


        /*
         * Construction
         */
        public int AppendVertex(double x, double y, double z)
        {
            int i = Vertices.Length / 3;
            if (HasVertexNormals) {
                Normals.Add(0); Normals.Add(1); Normals.Add(0);
            }
            if (HasVertexColors) {
                Colors.Add(1); Colors.Add(1); Colors.Add(1);
            }
            if (HasVertexUVs) {
                UVs.Add(0); UVs.Add(0);
            }
            Vertices.Add(x); Vertices.Add(y); Vertices.Add(z);
            return i;
        }
        public int AppendVertex(Vector3d v) {
            return AppendVertex(v.x, v.y, v.z);
        }

        public int AppendVertex(NewVertexInfo info)
        {
            int i = Vertices.Length / 3;

            if (info.bHaveN && HasVertexNormals) {
                Normals.Add(info.n[0]); Normals.Add(info.n[1]); Normals.Add(info.n[2]);
            } else if (HasVertexNormals) {
                Normals.Add(0); Normals.Add(1); Normals.Add(0);
            }
            if (info.bHaveC && HasVertexColors) {
                Colors.Add(info.c[0]); Colors.Add(info.c[1]); Colors.Add(info.c[2]);
            } else if (HasVertexColors) {
                Colors.Add(1); Colors.Add(1); Colors.Add(1);
            }
            if (info.bHaveUV && HasVertexUVs) {
                UVs.Add(info.uv[0]); UVs.Add(info.uv[1]);
            } else if (HasVertexUVs) {
                UVs.Add(0); UVs.Add(0);
            }

            Vertices.Add(info.v[0]); Vertices.Add(info.v[1]); Vertices.Add(info.v[2]);
            return i;
        }


        public int AppendQuad(int i, int j, int k, int l, int g = -1)
        {
            int qi = Quads.Length / 4;
            if (HasFaceGroups)
                FaceGroups.Add((g == -1) ? 0 : g);
            Quads.Add(i); Quads.Add(j); Quads.Add(k); Quads.Add(l);
            return qi;
        }


        public int VertexCount {
            get { return Vertices.Length / 3; }
        }
        public int QuadCount {
            get { return Quads.Length / 4; }
        }
        public int MaxVertexID {
            get { return VertexCount; }
        }
        public int MaxQuadID {
            get { return QuadCount; }
        }


        public bool IsVertex(int vID)
        {
            return vID * 3 < Vertices.Length;
        }
        public bool IsQuad(int qID)
        {
            return qID * 4 < Quads.Length;
        }

        public bool HasVertexColors {
            get { return Colors != null && Colors.Length == Vertices.Length; }
        }

        public bool HasVertexNormals {
            get { return Normals != null && Normals.Length == Vertices.Length; }
        }

        public bool HasVertexUVs {
            get { return UVs != null && UVs.Length / 2 == Vertices.Length / 3; }
        }

        public Vector3d GetVertex(int i)
        {
            return new Vector3d(Vertices[3 * i], Vertices[3 * i + 1], Vertices[3 * i + 2]);
        }

        public Vector3f GetVertexNormal(int i)
        {
            return new Vector3f(Normals[3 * i], Normals[3 * i + 1], Normals[3 * i + 2]);
        }

        public Vector3f GetVertexColor(int i)
        {
            return new Vector3f(Colors[3 * i], Colors[3 * i + 1], Colors[3 * i + 2]);
        }

        public Vector2f GetVertexUV(int i)
        {
            return new Vector2f(UVs[2 * i], UVs[2 * i + 1]);
        }

        public NewVertexInfo GetVertexAll(int i)
        {
            NewVertexInfo vi = new NewVertexInfo();
            vi.v = GetVertex(i);
            if (HasVertexNormals) {
                vi.bHaveN = true;
                vi.n = GetVertexNormal(i);
            } else
                vi.bHaveN = false;
            if (HasVertexColors) {
                vi.bHaveC = true;
                vi.c = GetVertexColor(i);
            } else
                vi.bHaveC = false;
            if (HasVertexUVs) {
                vi.bHaveUV = true;
                vi.uv = GetVertexUV(i);
            } else
                vi.bHaveUV = false;
            return vi;
        }


        public bool HasFaceGroups {
            get { return FaceGroups != null && FaceGroups.Length == Quads.Length / 4; }
        }

        public Index4i GetQuad(int i)
        {
            return new Index4i(Quads[4 * i], Quads[4 * i + 1], Quads[4 * i + 2], Quads[4 * i + 3]);
        }

        public int GetFaceGroup(int i)
        {
            return FaceGroups[i];
        }


        public IEnumerable<Vector3d> VerticesItr()
        {
            int N = VertexCount;
            for (int i = 0; i < N; ++i)
                yield return new Vector3d(Vertices[3 * i], Vertices[3 * i + 1], Vertices[3 * i + 2]);
        }

        public IEnumerable<Vector3f> NormalsItr()
        {
            int N = VertexCount;
            for (int i = 0; i < N; ++i)
                yield return new Vector3f(Normals[3 * i], Normals[3 * i + 1], Normals[3 * i + 2]);
        }

        public IEnumerable<Vector3f> ColorsItr()
        {
            int N = VertexCount;
            for (int i = 0; i < N; ++i)
                yield return new Vector3f(Colors[3 * i], Colors[3 * i + 1], Colors[3 * i + 2]);
        }

        public IEnumerable<Vector2f> UVsItr()
        {
            int N = VertexCount;
            for (int i = 0; i < N; ++i)
                yield return new Vector2f(UVs[2 * i], UVs[2 * i + 1]);
        }

        public IEnumerable<Index4i> QuadsItr()
        {
            int N = QuadCount;
            for (int i = 0; i < N; ++i)
                yield return new Index4i(Quads[4 * i], Quads[4 * i + 1], Quads[4 * i + 2], Quads[4 * i + 3]);
        }

        public IEnumerable<int> FaceGroupsItr()
        {
            int N = QuadCount;
            for (int i = 0; i < N; ++i)
                yield return FaceGroups[i];
        }

        public IEnumerable<int> VertexIndices()
        {
            int N = VertexCount;
            for (int i = 0; i < N; ++i)
                yield return i;
        }
        public IEnumerable<int> QuadIndices()
        {
            int N = QuadCount;
            for (int i = 0; i < N; ++i)
                yield return i;
        }


        // setters

        public void SetVertex(int i, Vector3d v)
        {
            Vertices[3 * i] = v.x;
            Vertices[3 * i + 1] = v.y;
            Vertices[3 * i + 2] = v.z;
        }

        public void SetVertexNormal(int i, Vector3f n)
        {
            Normals[3 * i] = n.x;
            Normals[3 * i + 1] = n.y;
            Normals[3 * i + 2] = n.z;
        }

        public void SetVertexColor(int i, Vector3f c)
        {
            Colors[3 * i] = c.x;
            Colors[3 * i + 1] = c.y;
            Colors[3 * i + 2] = c.z;
        }

        public void SetVertexUV(int i, Vector2f uv)
        {
            UVs[2 * i] = uv.x;
            UVs[2 * i + 1] = uv.y;
        }


        /*
         * Array-based access (allocates arrays automatically)
         */
        public double[] GetVertexArray()
        {
            return this.Vertices.GetBuffer();
        }
        public float[] GetVertexArrayFloat()
        {
            float[] buf = new float[this.Vertices.Length];
            for (int i = 0; i < this.Vertices.Length; ++i)
                buf[i] = (float)this.Vertices[i];
            return buf;
        }

        public float[] GetVertexNormalArray()
        {
            return (this.HasVertexNormals) ? this.Normals.GetBuffer() : null;
        }

        public float[] GetVertexColorArray()
        {
            return (this.HasVertexColors) ? this.Colors.GetBuffer() : null;
        }

        public float[] GetVertexUVArray()
        {
            return (this.HasVertexUVs) ? this.UVs.GetBuffer() : null;
        }

        public int[] GetQuadArray()
        {
            return this.Quads.GetBuffer();
        }

        public int[] GetFaceGroupsArray()
        {
            return (this.HasFaceGroups) ? this.FaceGroups.GetBuffer() : null;
        }





        public static IOWriteResult WriteOBJ(SimpleQuadMesh mesh, string sPath, WriteOptions options)
        {
            StreamWriter writer = new StreamWriter(sPath);
            if (writer.BaseStream == null)
                return new IOWriteResult(IOCode.FileAccessError, "Could not open file " + sPath + " for writing");

            bool bVtxColors = options.bPerVertexColors && mesh.HasVertexColors;
            bool bNormals = options.bPerVertexNormals && mesh.HasVertexNormals;

            // use separate UV set if we have it, otherwise write per-vertex UVs if we have those
            bool bVtxUVs = options.bPerVertexUVs && mesh.HasVertexUVs;
            if (mesh.UVs != null)
                bVtxUVs = false;

            int[] mapV = new int[mesh.MaxVertexID];

            int nAccumCountV = 1;       // OBJ indices always start at 1

            // write vertices for this mesh
            foreach (int vi in mesh.VertexIndices()) {
                mapV[vi] = nAccumCountV++;
                Vector3d v = mesh.GetVertex(vi);
                if (bVtxColors) {
                    Vector3d c = mesh.GetVertexColor(vi);
                    writer.WriteLine("v {0} {1} {2} {3:F8} {4:F8} {5:F8}", v[0], v[1], v[2], c[0], c[1], c[2]);
                } else {
                    writer.WriteLine("v {0} {1} {2}", v[0], v[1], v[2]);
                }

                if (bNormals) {
                    Vector3d n = mesh.GetVertexNormal(vi);
                    writer.WriteLine("vn {0:F10} {1:F10} {2:F10}", n[0], n[1], n[2]);
                }

                if (bVtxUVs) {
                    Vector2f uv = mesh.GetVertexUV(vi);
                    writer.WriteLine("vt {0:F10} {1:F10}", uv.x, uv.y);
                }
            }

            foreach (int ti in mesh.QuadIndices()) {
                Index4i q = mesh.GetQuad(ti);
                q[0] = mapV[q[0]];
                q[1] = mapV[q[1]];
                q[2] = mapV[q[2]];
                q[3] = mapV[q[3]];
                write_quad(writer, ref q, bNormals, bVtxUVs, ref q);
            }

            writer.Close();

            return IOWriteResult.Ok;
        }



        // actually write triangle line, in proper OBJ format
        static void write_quad(TextWriter writer, ref Index4i q, bool bNormals, bool bUVs, ref Index4i tuv)
        {
            if (bNormals == false && bUVs == false) {
                writer.WriteLine("f {0} {1} {2} {3}", q[0], q[1], q[2], q[3]);
            } else if (bNormals == true && bUVs == false) {
                writer.WriteLine("f {0}//{0} {1}//{1} {2}//{2} {3}//{3}", q[0], q[1], q[2], q[3]);
            } else if (bNormals == false && bUVs == true) {
                writer.WriteLine("f {0}/{4} {1}/{5} {2}/{6} {3}/{7}", q[0], q[1], q[2], q[3], tuv[0], tuv[1], tuv[2], tuv[3]);
            } else {
                writer.WriteLine("f {0}/{4}/{0} {1}/{5}/{1} {2}/{6}/{2} {3}/{7}/{3}", q[0], q[1], q[2], q[3], tuv[0], tuv[1], tuv[2], tuv[3]);
            }
        }

    }


    /// <summary>
    /// Problems <see cref="DPolyMesh.Validate"/> reports for one face (flags, a face can have several).
    /// </summary>
    [Flags]
    public enum PolyFaceIssues
    {
        None = 0,
        /// <summary> the face has fewer than 3 vertices </summary>
        TooFewVertices = 1,
        /// <summary> a vertex index is negative or not smaller than the vertex count </summary>
        IndexOutOfRange = 2,
        /// <summary> a vertex index occurs more than once in the face </summary>
        RepeatedVertex = 4
    }


    /// <summary>
    /// Result of <see cref="DPolyMesh.Validate"/>: the faces with problems (in face order), and the vertices with a
    /// non-finite coordinate. Nothing is thrown or repaired.
    /// </summary>
    public sealed class PolyMeshReport
    {
        /// <summary> the invalid faces, ascending </summary>
        public readonly List<int> InvalidFaces = new List<int>();
        /// <summary> the issues of InvalidFaces[i] </summary>
        public readonly List<PolyFaceIssues> Issues = new List<PolyFaceIssues>();
        /// <summary> the vertices with a NaN or infinite coordinate, ascending </summary>
        public readonly List<int> NonFiniteVertices = new List<int>();

        public bool IsValid { get { return InvalidFaces.Count == 0 && NonFiniteVertices.Count == 0; } }
    }


    /// <summary>
    /// Polygon mesh: faces with 3 or more vertices (quads, n-gons), in compact arrays. A face is a range of
    /// <see cref="FaceIndices"/> given by <see cref="FaceOffsets"/>; every face has an integer group (default 0).
    /// Vertex positions only, no normals, colors or UVs. No topology (no edge or neighbor queries): this is an
    /// output and exchange type, the geometry operations run on <see cref="DMesh3"/> (see <see cref="ToDMesh3"/>).
    ///
    /// Construction is cheap: <see cref="AppendVertices"/> and <see cref="AppendFaces"/> append whole arrays
    /// (amortized O(1) per entry, no per-face allocation). Invalid faces (fewer than 3 vertices, repeated vertices,
    /// out-of-range indices) can be appended; they are reported by <see cref="Validate"/> and skipped by the
    /// conversions, never thrown for.
    ///
    /// Triangulation rule (<see cref="TriangulateFace"/>, used by <see cref="ToDMesh3"/>): a triangle face is
    /// itself. A larger face is cut by ear clipping in the plane of its Newell normal, scanning the corners
    /// 1, 2, ..., n-1, 0 (cyclically, restarting at 1 after each cut) and cutting the first corner that is convex
    /// (the cross product of its two edges points along the normal) and whose triangle contains no other vertex of
    /// the remaining ring (on the boundary counts as inside; vertices at the same position as a corner of the ear
    /// are ignored). Triangles keep the orientation of the face ring. A convex planar face therefore becomes a fan
    /// from its vertex 0. If no ear exists (self-intersecting or fully degenerate ring), corner 1 is cut anyway,
    /// and a face with a zero Newell normal is a fan from vertex 0. The rule uses only comparisons of sums of
    /// products of coordinate differences, so a power-of-two scale of the vertices gives the same triangles.
    /// </summary>
    public class DPolyMesh
    {
        readonly List<double> vertices = new List<double>();
        readonly List<int> offsets = new List<int>() { 0 };
        readonly List<int> indices = new List<int>();
        readonly List<int> groups = new List<int>();

        public DPolyMesh() { }

        /// <summary> capacity hints, for a producer that knows the sizes (no effect on the result) </summary>
        public DPolyMesh(int vertexCapacity, int faceCapacity, int indexCapacity)
        {
            vertices.Capacity = Math.Max(0, vertexCapacity) * 3;
            offsets.Capacity = Math.Max(0, faceCapacity) + 1;
            groups.Capacity = Math.Max(0, faceCapacity);
            indices.Capacity = Math.Max(0, indexCapacity);
        }

        public int VertexCount { get { return vertices.Count / 3; } }
        public int FaceCount { get { return groups.Count; } }

        /// <summary> coordinates x0 y0 z0 x1 y1 z1 ... (live view, do not modify through casts) </summary>
        public IReadOnlyList<double> VertexCoordinates { get { return vertices; } }
        /// <summary> FaceCount + 1 entries: face f has the vertex indices FaceIndices[FaceOffsets[f] .. FaceOffsets[f+1]) </summary>
        public IReadOnlyList<int> FaceOffsets { get { return offsets; } }
        /// <summary> the vertex indices of all faces, in face order </summary>
        public IReadOnlyList<int> FaceIndices { get { return indices; } }
        /// <summary> the group of every face </summary>
        public IReadOnlyList<int> FaceGroups { get { return groups; } }

        public Vector3d GetVertex(int vid)
        {
            int i = 3 * vid;
            return new Vector3d(vertices[i], vertices[i + 1], vertices[i + 2]);
        }
        public void SetVertex(int vid, Vector3d v)
        {
            int i = 3 * vid;
            vertices[i] = v.x; vertices[i + 1] = v.y; vertices[i + 2] = v.z;
        }

        public int FaceVertexCount(int f) { return offsets[f + 1] - offsets[f]; }
        public int GetFaceVertex(int f, int k) { return indices[offsets[f] + k]; }
        public int GetFaceGroup(int f) { return groups[f]; }
        public void SetFaceGroup(int f, int group) { groups[f] = group; }

        /// <summary> the vertex indices of face f (a new array) </summary>
        public int[] GetFace(int f)
        {
            int a = offsets[f], n = offsets[f + 1] - a;
            int[] r = new int[n];
            indices.CopyTo(a, r, 0, n);
            return r;
        }

        public void Clear()
        {
            vertices.Clear(); indices.Clear(); groups.Clear();
            offsets.Clear(); offsets.Add(0);
        }


        // ---- construction ----

        public int AppendVertex(double x, double y, double z)
        {
            int vid = vertices.Count / 3;
            vertices.Add(x); vertices.Add(y); vertices.Add(z);
            return vid;
        }
        public int AppendVertex(Vector3d v) { return AppendVertex(v.x, v.y, v.z); }

        /// <summary>
        /// Append many vertices at once, as x0 y0 z0 x1 y1 z1 ... Returns the index of the first new vertex.
        /// Throws ArgumentException if the length is not a multiple of 3.
        /// </summary>
        public int AppendVertices(IList<double> xyz)
        {
            if (xyz == null) throw new ArgumentNullException("xyz");
            if (xyz.Count % 3 != 0) throw new ArgumentException("length must be a multiple of 3", "xyz");
            int first = vertices.Count / 3;
            vertices.AddRange(xyz);
            return first;
        }

        /// <summary> Append a face (any vertex count, validity is not checked, see <see cref="Validate"/>). Returns its index. </summary>
        public int AppendFace(IList<int> face, int group = 0)
        {
            if (face == null) throw new ArgumentNullException("face");
            indices.AddRange(face);
            offsets.Add(indices.Count);
            groups.Add(group);
            return groups.Count - 1;
        }
        public int AppendTriangle(int a, int b, int c, int group = 0)
        {
            indices.Add(a); indices.Add(b); indices.Add(c);
            offsets.Add(indices.Count); groups.Add(group);
            return groups.Count - 1;
        }
        public int AppendQuad(int a, int b, int c, int d, int group = 0)
        {
            indices.Add(a); indices.Add(b); indices.Add(c); indices.Add(d);
            offsets.Add(indices.Count); groups.Add(group);
            return groups.Count - 1;
        }

        /// <summary>
        /// Append many faces at once: face i has faceVertexCounts[i] vertices, taken in order from vertexIndices
        /// (which must hold exactly the sum of the counts); groups (optional, one per face) default to 0.
        /// Returns the index of the first new face. Throws ArgumentException for inconsistent array sizes or a negative
        /// count (nothing is appended then). Indices themselves are not checked, see <see cref="Validate"/>.
        /// </summary>
        public int AppendFaces(IList<int> faceVertexCounts, IList<int> vertexIndices, IList<int> faceGroups = null)
        {
            if (faceVertexCounts == null) throw new ArgumentNullException("faceVertexCounts");
            if (vertexIndices == null) throw new ArgumentNullException("vertexIndices");
            long sum = 0;
            for (int i = 0; i < faceVertexCounts.Count; ++i) {
                if (faceVertexCounts[i] < 0) throw new ArgumentException("negative face vertex count", "faceVertexCounts");
                sum += faceVertexCounts[i];
            }
            if (sum != vertexIndices.Count) throw new ArgumentException("the counts do not add up to the number of indices", "vertexIndices");
            if (faceGroups != null && faceGroups.Count != faceVertexCounts.Count) throw new ArgumentException("one group per face is needed", "faceGroups");

            int firstFace = groups.Count;
            indices.AddRange(vertexIndices);
            int at = offsets[offsets.Count - 1];
            offsets.Capacity = Math.Max(offsets.Capacity, offsets.Count + faceVertexCounts.Count);
            for (int i = 0; i < faceVertexCounts.Count; ++i) {
                at += faceVertexCounts[i];
                offsets.Add(at);
            }
            if (faceGroups != null)
                groups.AddRange(faceGroups);
            else {
                for (int i = 0; i < faceVertexCounts.Count; ++i)
                    groups.Add(0);
            }
            return firstFace;
        }


        // ---- validation ----

        /// <summary>
        /// Report faces with fewer than 3 vertices, with out-of-range or repeated vertex indices, and vertices with
        /// non-finite coordinates. Never throws. O(total face size) (repeats are found with a stamp array).
        /// </summary>
        public PolyMeshReport Validate()
        {
            PolyMeshReport report = new PolyMeshReport();
            int nv = VertexCount;
            for (int i = 0; i < nv; ++i) {
                if (!IsFinite(vertices[3 * i]) || !IsFinite(vertices[3 * i + 1]) || !IsFinite(vertices[3 * i + 2]))
                    report.NonFiniteVertices.Add(i);
            }
            int[] stamp = new int[nv];      // face + 1 of the last face that used the vertex
            for (int f = 0; f < FaceCount; ++f) {
                PolyFaceIssues issues = PolyFaceIssues.None;
                int a = offsets[f], b = offsets[f + 1];
                if (b - a < 3) issues |= PolyFaceIssues.TooFewVertices;
                for (int k = a; k < b; ++k) {
                    int v = indices[k];
                    if (v < 0 || v >= nv) { issues |= PolyFaceIssues.IndexOutOfRange; continue; }
                    if (stamp[v] == f + 1) issues |= PolyFaceIssues.RepeatedVertex;
                    stamp[v] = f + 1;
                }
                if (issues != PolyFaceIssues.None) {
                    report.InvalidFaces.Add(f);
                    report.Issues.Add(issues);
                }
            }
            return report;
        }

        static bool IsFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        /// <summary> true if face f has at least 3 vertices, all in range and distinct (the vertex positions are not looked at) </summary>
        public bool IsFaceValid(int f)
        {
            int a = offsets[f], b = offsets[f + 1], nv = VertexCount;
            if (b - a < 3) return false;
            for (int k = a; k < b; ++k) {
                int v = indices[k];
                if (v < 0 || v >= nv) return false;
                for (int j = a; j < k; ++j)
                    if (indices[j] == v) return false;
            }
            return true;
        }

        /// <summary> true if there is at least one face and every face has exactly 4 vertices </summary>
        public bool IsAllQuads()
        {
            if (FaceCount == 0) return false;
            for (int f = 0; f < FaceCount; ++f)
                if (offsets[f + 1] - offsets[f] != 4) return false;
            return true;
        }


        // ---- conversion ----

        /// <summary>
        /// Triangulate face f (see the class remarks for the rule) and append its triangles, as vertex indices of this
        /// mesh, to triangles. Returns the number of triangles appended (n - 2 for a valid face with n vertices);
        /// an invalid face (see <see cref="IsFaceValid"/>) appends nothing and returns 0.
        /// </summary>
        public int TriangulateFace(int f, List<Index3i> triangles)
        {
            if (triangles == null) throw new ArgumentNullException("triangles");
            if (IsFaceValid(f) == false)
                return 0;
            int n = FaceVertexCount(f), start = offsets[f];
            if (n == 3) {
                triangles.Add(new Index3i(indices[start], indices[start + 1], indices[start + 2]));
                return 1;
            }
            List<int> ring = new List<int>(n);
            for (int k = 0; k < n; ++k) ring.Add(indices[start + k]);
            return EarClip(ring, triangles);
        }

        int EarClip(List<int> ring, List<Index3i> triangles)
        {
            int added = 0;
            Vector3d p0 = GetVertex(ring[0]);
            Vector3d normal = Vector3d.Zero;
            for (int k = 1; k + 1 < ring.Count; ++k)
                normal += (GetVertex(ring[k]) - p0).Cross(GetVertex(ring[k + 1]) - p0);
            if (normal.x == 0 && normal.y == 0 && normal.z == 0) {
                for (int k = 1; k + 1 < ring.Count; ++k)
                    triangles.Add(new Index3i(ring[0], ring[k], ring[k + 1]));
                return ring.Count - 2;
            }

            while (ring.Count > 3) {
                int n = ring.Count, cut = -1;
                for (int s = 0; s < n && cut < 0; ++s) {
                    int i = (1 + s) % n;
                    if (IsEar(ring, i, normal))
                        cut = i;
                }
                if (cut < 0)
                    cut = 1;        // no ear: cut corner 1 anyway
                int c = ring.Count;
                triangles.Add(new Index3i(ring[(cut + c - 1) % c], ring[cut], ring[(cut + 1) % c]));
                added++;
                ring.RemoveAt(cut);
            }
            triangles.Add(new Index3i(ring[0], ring[1], ring[2]));
            return added + 1;
        }

        bool IsEar(List<int> ring, int i, Vector3d normal)
        {
            int n = ring.Count;
            int ia = ring[(i + n - 1) % n], ib = ring[i], ic = ring[(i + 1) % n];
            Vector3d a = GetVertex(ia), b = GetVertex(ib), c = GetVertex(ic);
            if ((b - a).Cross(c - b).Dot(normal) <= 0)
                return false;
            for (int k = 0; k < n; ++k) {
                int iq = ring[k];
                if (iq == ia || iq == ib || iq == ic) continue;
                Vector3d q = GetVertex(iq);
                if (q == a || q == b || q == c) continue;
                if ((b - a).Cross(q - a).Dot(normal) >= 0 && (c - b).Cross(q - b).Dot(normal) >= 0 && (a - c).Cross(q - c).Dot(normal) >= 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Convert to a <see cref="DMesh3"/> with triangle groups: all vertices (in order, so DMesh3 vertex id == vertex
        /// index here, also unreferenced ones), and the triangles of every valid face (<see cref="TriangulateFace"/>), in face
        /// order, each with the group of its face. triangleToFace[t] is the face of triangle t. Faces that give no
        /// triangle are listed in skippedFaces (if not null): invalid faces, and faces with a triangle that DMesh3
        /// refuses (an edge that would get more than two triangles,);
        /// the other triangles of such a face stay in the mesh. Never throws for invalid input.
        /// </summary>
        public DMesh3 ToDMesh3(out int[] triangleToFace, List<int> skippedFaces = null)
        {
            DMesh3 mesh = new DMesh3(false, false, false, true);
            int nv = VertexCount;
            for (int i = 0; i < nv; ++i)
                mesh.AppendVertex(new Vector3d(vertices[3 * i], vertices[3 * i + 1], vertices[3 * i + 2]));

            List<int> map = new List<int>(FaceCount);
            List<Index3i> tris = new List<Index3i>();
            for (int f = 0; f < FaceCount; ++f) {
                tris.Clear();
                bool ok = TriangulateFace(f, tris) > 0;
                for (int k = 0; k < tris.Count; ++k) {
                    int tid = mesh.AppendTriangle(tris[k], groups[f]);
                    if (tid < 0) { ok = false; continue; }
                    while (map.Count <= tid) map.Add(-1);
                    map[tid] = f;
                }
                if (!ok && skippedFaces != null)
                    skippedFaces.Add(f);
            }
            triangleToFace = map.ToArray();
            return mesh;
        }

        /// <summary>
        /// A polygon mesh from a triangle mesh: vertices compacted in vertex-id order, one 3-vertex face per
        /// triangle in triangle-id order, with the triangle group (0 if the mesh has no groups).
        /// </summary>
        public static DPolyMesh FromDMesh3(DMesh3 mesh)
        {
            if (mesh == null) throw new ArgumentNullException("mesh");
            DPolyMesh poly = new DPolyMesh(mesh.VertexCount, mesh.TriangleCount, 3 * mesh.TriangleCount);
            int[] map = new int[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices())
                map[vid] = poly.AppendVertex(mesh.GetVertex(vid));
            bool groupsOn = mesh.HasTriangleGroups;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                poly.AppendTriangle(map[t.a], map[t.b], map[t.c], groupsOn ? mesh.GetTriangleGroup(tid) : 0);
            }
            return poly;
        }

        /// <summary>
        /// Build a <see cref="SimpleQuadMesh"/> (positions and face groups) from an all-quad mesh. Returns false and
        /// null if <see cref="IsAllQuads"/> is false or a face has an out-of-range index (repeated vertices are copied
        /// as they are, see <see cref="Validate"/>).
        /// </summary>
        public bool TryToSimpleQuadMesh(out SimpleQuadMesh quads)
        {
            quads = null;
            if (IsAllQuads() == false)
                return false;
            int nv = VertexCount;
            for (int i = 0; i < indices.Count; ++i)
                if (indices[i] < 0 || indices[i] >= nv) return false;
            SimpleQuadMesh q = new SimpleQuadMesh();
            q.Initialize(false, false, false, true);
            for (int i = 0; i < nv; ++i)
                q.AppendVertex(vertices[3 * i], vertices[3 * i + 1], vertices[3 * i + 2]);
            for (int f = 0; f < FaceCount; ++f) {
                int a = offsets[f];
                q.AppendQuad(indices[a], indices[a + 1], indices[a + 2], indices[a + 3], groups[f]);
            }
            quads = q;
            return true;
        }
    }


    /// <summary>
    /// A reader (<see cref="PLYReader"/>) that finds an <see cref="IMeshBuilder"/> implementing this interface hands it
    /// every face as read, instead of fan-triangulating it.
    /// </summary>
    public interface IPolygonMeshBuilder
    {
        /// <summary>
        /// A face of the active mesh: vertexIds as returned by AppendVertex (at least 3, all valid), group as read from the file (0 if none).
        /// </summary>
        void AppendPolygon(IList<int> vertexIds, int group);
    }


    /// <summary>
    /// <see cref="IMeshBuilder"/> that collects the read meshes as <see cref="DPolyMesh"/> (positions and groups only;
    /// normals, colors, UVs and materials are dropped). Triangles from readers that only know triangles become
    /// 3-vertex faces; readers that support <see cref="IPolygonMeshBuilder"/> (PLY) give whole polygons.
    /// </summary>
    public class DPolyMeshBuilder : IMeshBuilder, IPolygonMeshBuilder
    {
        public readonly List<DPolyMesh> Meshes = new List<DPolyMesh>();
        int active = -1;

        /// <summary> the active mesh (the last one appended unless SetActiveMesh was called), null if none </summary>
        public DPolyMesh Mesh { get { return active >= 0 ? Meshes[active] : null; } }

        public int AppendNewMesh(bool bHaveVtxNormals, bool bHaveVtxColors, bool bHaveVtxUVs, bool bHaveFaceGroups)
        {
            Meshes.Add(new DPolyMesh());
            active = Meshes.Count - 1;
            return active;
        }
        public int AppendNewMesh(DMesh3 existingMesh)
        {
            Meshes.Add(DPolyMesh.FromDMesh3(existingMesh));
            active = Meshes.Count - 1;
            return active;
        }
        public void SetActiveMesh(int id) { active = id; }

        public int AppendVertex(double x, double y, double z) { return Mesh.AppendVertex(x, y, z); }
        public int AppendVertex(NewVertexInfo info) { return Mesh.AppendVertex(info.v.x, info.v.y, info.v.z); }
        public int AppendTriangle(int i, int j, int k) { return Mesh.AppendTriangle(i, j, k); }
        public int AppendTriangle(int i, int j, int k, int g) { return Mesh.AppendTriangle(i, j, k, Math.Max(g, 0)); }

        public void AppendPolygon(IList<int> vertexIds, int group) { Mesh.AppendFace(vertexIds, group); }

        public int BuildMaterial(GenericMaterial m) { return -1; }
        public void AssignMaterial(int materialID, int meshID) { }
        public bool SupportsMetaData { get { return false; } }
        public void AppendMetaData(string identifier, object data) { }
    }
}
