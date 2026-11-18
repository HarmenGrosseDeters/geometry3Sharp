// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Markers of a tetrahedral mesh for output (docs/design/TET_MESHING.md section 17.3, stage P2), computed from the mesh and the
    /// input it was made from:
    ///   - per boundary triangle (index of TetMesh3.BoundaryTriangles): the nearest input triangle to the boundary triangle's
    ///     centroid (MeshBatchQueries.NearestPointBatch; on an exact tie the tree's own order decides, the same for every call), its marker (the nearest triangle's group id if the input has triangle groups, else its
    ///     triangle id) and the distance of the centroid to it. The marker is a label for loads and supports (a group of the input
    ///     becomes one boundary patch), not a proof that the boundary triangle "lies on" the input: the boundary is the offset
    ///     wrap, at about the offset (diagonal / 600 by default) up to alpha from the input, and the distance is the evidence;
    ///   - per tetrahedron: a region id, the connected component of the tets by shared facets (0, 1, ... in the order of the lowest
    ///     tet index of each component, so the numbering is deterministic).
    /// Both are functions of the exact tets and of the input's topology (ids and groups), so a similarity that keeps the mesh
    /// (scale by 2^k) gives the same markers bit for bit, and the distances scale with it.
    /// Without any input triangle (null or empty input) the marker and the nearest triangle are -1 and the distance is +infinity.
    /// </summary>
    public sealed class TetMeshMarkers
    {
        /// <summary> per boundary triangle: id of the nearest input triangle, -1 if there is none </summary>
        public int[] NearestInputTriangle;
        /// <summary> per boundary triangle: its marker (group id of the nearest input triangle if the input has groups, else its id), -1 if none </summary>
        public int[] BoundaryMarker;
        /// <summary> per boundary triangle: distance from its centroid to the nearest input triangle (mesh units), +infinity if none </summary>
        public double[] BoundaryDistance;
        /// <summary> true if BoundaryMarker holds group ids, false if it holds input triangle ids </summary>
        public bool MarkersAreGroups;
        /// <summary> per tet: id of its connected component (0 .. RegionCount - 1) </summary>
        public int[] TetRegion;
        public int RegionCount;

        /// <summary> the largest and the mean of BoundaryDistance (the finite ones); 0 for no boundary triangle </summary>
        public double MaxBoundaryDistance, MeanBoundaryDistance;

        public static TetMeshMarkers Compute(TetMesh3 mesh, DMesh3 input)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            var r = new TetMeshMarkers();
            int nb = mesh.BoundaryTriangles.Count;
            r.NearestInputTriangle = new int[nb];
            r.BoundaryMarker = new int[nb];
            r.BoundaryDistance = new double[nb];
            for (int i = 0; i < nb; ++i) { r.NearestInputTriangle[i] = -1; r.BoundaryMarker[i] = -1; r.BoundaryDistance[i] = double.PositiveInfinity; }
            if (input != null && input.TriangleCount > 0 && nb > 0) {
                r.MarkersAreGroups = input.HasTriangleGroups;
                var tree = new DMeshAABBTree3(input, true);
                var points = new Vector3d[nb];
                for (int i = 0; i < nb; ++i) {
                    Index3i t = mesh.BoundaryTriangles[i];
                    points[i] = mesh.Vertices[t.a] / 3.0 + mesh.Vertices[t.b] / 3.0 + mesh.Vertices[t.c] / 3.0;   // no overflow of the sum
                }
                var res = new NearestPointResult[nb];
                BatchStatus st = MeshBatchQueries.NearestPointBatch(tree, points, res);
                if (st != BatchStatus.Ok) throw new InvalidOperationException("nearest point batch failed: " + st);
                for (int i = 0; i < nb; ++i) {
                    int tid = res[i].Triangle;
                    if (tid == DMesh3.InvalidID) continue;
                    r.NearestInputTriangle[i] = tid;
                    r.BoundaryMarker[i] = r.MarkersAreGroups ? input.GetTriangleGroup(tid) : tid;
                    r.BoundaryDistance[i] = res[i].Distance;
                }
            }
            double sum = 0; int cnt = 0;
            foreach (double d in r.BoundaryDistance)
                if (!double.IsInfinity(d)) { r.MaxBoundaryDistance = Math.Max(r.MaxBoundaryDistance, d); sum += d; cnt++; }
            r.MeanBoundaryDistance = cnt > 0 ? sum / cnt : 0;
            r.ComputeRegions(mesh);
            return r;
        }

        void ComputeRegions(TetMesh3 mesh)
        {
            int n = mesh.Tets.Count;
            var parent = new int[n];
            for (int i = 0; i < n; ++i) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            var owner = new Dictionary<(int, int, int), int>(4 * n);
            for (int c = 0; c < n; ++c) {
                Index4i t = mesh.Tets[c];
                for (int i = 0; i < 4; ++i) {
                    // a facet is shared by the two tets that contain its three vertices, whatever the orientation
                    int a = t[Delaunay3.FacetIndices[i, 0]], b = t[Delaunay3.FacetIndices[i, 1]], d = t[Delaunay3.FacetIndices[i, 2]];
                    var key = Sorted(a, b, d);
                    if (owner.TryGetValue(key, out int other)) {
                        int x = find(c), y = find(other);
                        if (x != y) parent[y] = x;
                    } else owner[key] = c;
                }
            }
            TetRegion = new int[n];
            var ids = new Dictionary<int, int>();
            for (int c = 0; c < n; ++c) {
                int root = find(c);                  // ids in the order the components first appear in the tet list: the lowest tet index decides
                if (!ids.TryGetValue(root, out int id)) { id = ids.Count; ids[root] = id; }
                TetRegion[c] = id;
            }
            RegionCount = ids.Count;
        }

        static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return (a, b, c);
        }
    }


    /// <summary>
    /// Writers of a TetMesh3 as text, for solvers and viewers (docs/design/TET_MESHING.md stage P2). Formats, from their published
    /// descriptions (SOURCES.md): Medit / libMeshb ".mesh" (ASCII, version 2), Gmsh ".msh" 4.1 and 2.2 (ASCII), VTK legacy ".vtk"
    /// (ASCII, unstructured grid). Readers are not provided.
    ///
    /// Every tet is written as in the TetMesh3 (exactly positively oriented, which is the positive orientation of all four formats:
    /// the normal of the triangle (v0, v1, v2) by the right-hand rule points to v3), vertex order unchanged. Boundary triangles are
    /// written as in TetMesh3.BoundaryTriangles (outward by the right-hand rule). Vertex coordinates are written in the shortest
    /// round-trip form (MeshIOUtil.FormatDouble, invariant), so reading them back gives the same doubles.
    ///
    /// References (Medit "ref", Gmsh physical and elementary tag, VTK cell data "ref"): the ref of a tet is its region id + 1, the
    /// ref of a boundary triangle is its marker + 1 (TetMeshMarkers), so refs start at 1 and 0 stays "unmarked" (also for a marker of
    /// -1 or a negative group id). Without markers every ref is 0 and Gmsh 4.1 has one volume entity. Vertices carry ref 0.
    /// Gmsh 4.1 gets one volume entity per region (one empty one if there are vertices but no tets) (tag = ref) and one surface entity per distinct boundary ref (tags in increasing
    /// order of the ref, a ref of 0 last), each with the ref as its physical tag (none for a ref of 0); the entities carry no
    /// bounding topology (their bounding lists are empty), and all nodes are in one block of the first volume entity.
    /// Indices are 1-based in Medit and Gmsh, 0-based in VTK, as the formats say.
    /// </summary>
    public static class TetMeshWriter
    {
        public enum Format { Medit, Msh41, Msh22, Vtk }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary> the ref of a marker or region id: id + 1, or 0 for no id (negative) </summary>
        public static int Ref(int id) => id >= 0 && id < int.MaxValue ? id + 1 : 0;

        /// <summary> the format of a file name by its extension: .mesh Medit, .msh Gmsh 4.1, .vtk VTK; null for anything else </summary>
        public static Format? FormatOfExtension(string path)
        {
            switch ((Path.GetExtension(path ?? "") ?? "").ToLowerInvariant()) {
                case ".mesh": return Format.Medit;
                case ".msh": return Format.Msh41;
                case ".vtk": return Format.Vtk;
                default: return null;
            }
        }

        /// <summary> writes the file; the format by the extension unless given (.msh is 4.1 then); markers may be null </summary>
        public static void Write(string path, TetMesh3 mesh, TetMeshMarkers markers = null, Format? format = null)
        {
            Format f = format ?? FormatOfExtension(path) ?? throw new ArgumentException("unknown tet mesh file extension (.mesh, .msh, .vtk): " + path);
            using (var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false))) Write(w, mesh, markers, f);
        }

        public static void Write(TextWriter w, TetMesh3 mesh, TetMeshMarkers markers, Format format)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (markers != null && (markers.TetRegion.Length != mesh.Tets.Count || markers.BoundaryMarker.Length != mesh.BoundaryTriangles.Count))
                throw new ArgumentException("the markers are not those of this mesh (different tet or boundary triangle counts)");
            switch (format) {
                case Format.Medit: WriteMedit(w, mesh, markers); break;
                case Format.Msh41: WriteMsh41(w, mesh, markers); break;
                case Format.Msh22: WriteMsh22(w, mesh, markers); break;
                default: WriteVtk(w, mesh, markers); break;
            }
        }

        static string R(double x) => MeshIOUtil.FormatDouble(x);
        static string I(int x) => x.ToString(Inv);
        static void Line(TextWriter w, string s) { w.Write(s); w.Write('\n'); }

        static int TetRef(TetMeshMarkers m, int i) => m == null ? 0 : Ref(m.TetRegion[i]);
        static int TriRef(TetMeshMarkers m, int i) => m == null ? 0 : Ref(m.BoundaryMarker[i]);

        static void WriteMedit(TextWriter w, TetMesh3 t, TetMeshMarkers m)
        {
            Line(w, "MeshVersionFormatted 2");
            Line(w, "Dimension 3");
            Line(w, "Vertices");
            Line(w, I(t.Vertices.Count));
            foreach (Vector3d v in t.Vertices) Line(w, R(v.x) + " " + R(v.y) + " " + R(v.z) + " 0");
            if (t.BoundaryTriangles.Count > 0) {
                Line(w, "Triangles");
                Line(w, I(t.BoundaryTriangles.Count));
                for (int i = 0; i < t.BoundaryTriangles.Count; ++i) {
                    Index3i f = t.BoundaryTriangles[i];
                    Line(w, I(f.a + 1) + " " + I(f.b + 1) + " " + I(f.c + 1) + " " + I(TriRef(m, i)));
                }
            }
            Line(w, "Tetrahedra");
            Line(w, I(t.Tets.Count));
            for (int i = 0; i < t.Tets.Count; ++i) {
                Index4i q = t.Tets[i];
                Line(w, I(q.a + 1) + " " + I(q.b + 1) + " " + I(q.c + 1) + " " + I(q.d + 1) + " " + I(TetRef(m, i)));
            }
            Line(w, "End");
        }

        static void WriteMsh22(TextWriter w, TetMesh3 t, TetMeshMarkers m)
        {
            Line(w, "$MeshFormat");
            Line(w, "2.2 0 8");
            Line(w, "$EndMeshFormat");
            Line(w, "$Nodes");
            Line(w, I(t.Vertices.Count));
            for (int i = 0; i < t.Vertices.Count; ++i) Line(w, I(i + 1) + " " + R(t.Vertices[i].x) + " " + R(t.Vertices[i].y) + " " + R(t.Vertices[i].z));
            Line(w, "$EndNodes");
            Line(w, "$Elements");
            Line(w, I(t.Tets.Count + t.BoundaryTriangles.Count));
            // element: id type numTags physical elementary nodes (type 4 = 4-node tetrahedron, 2 = 3-node triangle)
            for (int i = 0; i < t.Tets.Count; ++i) {
                Index4i q = t.Tets[i];
                int r = TetRef(m, i);
                Line(w, I(i + 1) + " 4 2 " + I(r) + " " + I(r) + " " + I(q.a + 1) + " " + I(q.b + 1) + " " + I(q.c + 1) + " " + I(q.d + 1));
            }
            for (int i = 0; i < t.BoundaryTriangles.Count; ++i) {
                Index3i f = t.BoundaryTriangles[i];
                int r = TriRef(m, i);
                Line(w, I(t.Tets.Count + i + 1) + " 2 2 " + I(r) + " " + I(r) + " " + I(f.a + 1) + " " + I(f.b + 1) + " " + I(f.c + 1));
            }
            Line(w, "$EndElements");
        }

        static void WriteMsh41(TextWriter w, TetMesh3 t, TetMeshMarkers m)
        {
            int nv = t.Vertices.Count, nt = t.Tets.Count, nb = t.BoundaryTriangles.Count;
            // volume entities: one per tet ref (tag = ref, or 1 for the unmarked ref 0); surface entities: one per distinct boundary ref
            var volRefs = new SortedSet<int>();
            for (int i = 0; i < nt; ++i) volRefs.Add(TetRef(m, i));
            var surfRefs = new SortedSet<int>(Comparer<int>.Create((x, y) => x == 0 ? (y == 0 ? 0 : 1) : y == 0 ? -1 : x.CompareTo(y)));
            for (int i = 0; i < nb; ++i) surfRefs.Add(TriRef(m, i));
            var volTag = new Dictionary<int, int>(); var surfTag = new Dictionary<int, int>();
            if (nt == 0 && nv > 0) volRefs.Add(0);                 // the node block needs an entity to belong to
            int usedMax = volRefs.Count > 0 ? volRefs.Max() : 0;
            foreach (int r in volRefs) volTag[r] = r > 0 ? r : ++usedMax;      // ref 0 gets a free tag above the others
            int k = 0;
            foreach (int r in surfRefs) surfTag[r] = ++k;

            Line(w, "$MeshFormat");
            Line(w, "4.1 0 8");
            Line(w, "$EndMeshFormat");

            // bounding boxes of the entities' nodes
            var volBox = new Dictionary<int, AxisAlignedBox3d>(); var surfBox = new Dictionary<int, AxisAlignedBox3d>();
            for (int i = 0; i < nt; ++i) Grow(volBox, TetRef(m, i), t, t.Tets[i].a, t.Tets[i].b, t.Tets[i].c, t.Tets[i].d);
            for (int i = 0; i < nb; ++i) Grow(surfBox, TriRef(m, i), t, t.BoundaryTriangles[i].a, t.BoundaryTriangles[i].b, t.BoundaryTriangles[i].c, -1);
            Line(w, "$Entities");
            Line(w, "0 0 " + I(surfRefs.Count) + " " + I(volRefs.Count));
            foreach (int r in surfRefs) Line(w, I(surfTag[r]) + " " + Box(surfBox[r]) + (r > 0 ? " 1 " + I(r) : " 0") + " 0");
            foreach (int r in volRefs) Line(w, I(volTag[r]) + " " + Box(volBox.TryGetValue(r, out AxisAlignedBox3d vb) ? vb : VertexBox(t)) + (r > 0 ? " 1 " + I(r) : " 0") + " 0");
            Line(w, "$EndEntities");

            Line(w, "$Nodes");
            if (nv == 0) Line(w, "0 0 0 0");
            else {
                Line(w, "1 " + I(nv) + " 1 " + I(nv));
                Line(w, "3 " + I(volRefs.Count > 0 ? volTag[volRefs.First()] : 1) + " 0 " + I(nv));
                for (int i = 0; i < nv; ++i) Line(w, I(i + 1));
                foreach (Vector3d v in t.Vertices) Line(w, R(v.x) + " " + R(v.y) + " " + R(v.z));
            }
            Line(w, "$EndNodes");

            Line(w, "$Elements");
            // element rows per entity, in one pass over the elements
            var volRows = new Dictionary<int, List<string>>(); var surfRows = new Dictionary<int, List<string>>();
            for (int i = 0; i < nt; ++i) {
                Index4i q = t.Tets[i];
                Rows(volRows, TetRef(m, i)).Add(I(i + 1) + " " + I(q.a + 1) + " " + I(q.b + 1) + " " + I(q.c + 1) + " " + I(q.d + 1));
            }
            for (int i = 0; i < nb; ++i) {
                Index3i f = t.BoundaryTriangles[i];
                Rows(surfRows, TriRef(m, i)).Add(I(nt + i + 1) + " " + I(f.a + 1) + " " + I(f.b + 1) + " " + I(f.c + 1));
            }
            int blocks = volRows.Count + surfRows.Count, total = nt + nb;
            Line(w, I(blocks) + " " + I(total) + (total > 0 ? " 1 " + I(total) : " 0 0"));
            foreach (int r in volRefs) {
                if (!volRows.TryGetValue(r, out List<string> rows)) continue;
                Line(w, "3 " + I(volTag[r]) + " 4 " + I(rows.Count));
                foreach (string row in rows) Line(w, row);
            }
            foreach (int r in surfRefs) {
                List<string> rows = surfRows[r];
                Line(w, "2 " + I(surfTag[r]) + " 2 " + I(rows.Count));
                foreach (string row in rows) Line(w, row);
            }
            Line(w, "$EndElements");
        }

        static AxisAlignedBox3d VertexBox(TetMesh3 t)
        {
            var b = AxisAlignedBox3d.Empty;
            foreach (Vector3d v in t.Vertices) b.Contain(v);
            return b;
        }

        static List<string> Rows(Dictionary<int, List<string>> d, int r)
        {
            if (!d.TryGetValue(r, out List<string> l)) d[r] = l = new List<string>();
            return l;
        }

        static void Grow(Dictionary<int, AxisAlignedBox3d> boxes, int r, TetMesh3 t, int a, int b, int c, int d)
        {
            if (!boxes.TryGetValue(r, out AxisAlignedBox3d box)) box = AxisAlignedBox3d.Empty;
            box.Contain(t.Vertices[a]); box.Contain(t.Vertices[b]); box.Contain(t.Vertices[c]);
            if (d >= 0) box.Contain(t.Vertices[d]);
            boxes[r] = box;
        }

        static string Box(AxisAlignedBox3d b) => R(b.Min.x) + " " + R(b.Min.y) + " " + R(b.Min.z) + " " + R(b.Max.x) + " " + R(b.Max.y) + " " + R(b.Max.z);

        static void WriteVtk(TextWriter w, TetMesh3 t, TetMeshMarkers m)
        {
            int nt = t.Tets.Count, nb = t.BoundaryTriangles.Count;
            Line(w, "# vtk DataFile Version 3.0");
            Line(w, "geometry3Sharp tetrahedral mesh");
            Line(w, "ASCII");
            Line(w, "DATASET UNSTRUCTURED_GRID");
            Line(w, "POINTS " + I(t.Vertices.Count) + " double");
            foreach (Vector3d v in t.Vertices) Line(w, R(v.x) + " " + R(v.y) + " " + R(v.z));
            Line(w, "CELLS " + I(nt + nb) + " " + I(5 * nt + 4 * nb));
            foreach (Index4i q in t.Tets) Line(w, "4 " + I(q.a) + " " + I(q.b) + " " + I(q.c) + " " + I(q.d));
            foreach (Index3i f in t.BoundaryTriangles) Line(w, "3 " + I(f.a) + " " + I(f.b) + " " + I(f.c));
            Line(w, "CELL_TYPES " + I(nt + nb));
            for (int i = 0; i < nt; ++i) Line(w, "10");              // VTK_TETRA
            for (int i = 0; i < nb; ++i) Line(w, "5");               // VTK_TRIANGLE
            Line(w, "CELL_DATA " + I(nt + nb));
            Line(w, "SCALARS ref int 1");
            Line(w, "LOOKUP_TABLE default");
            for (int i = 0; i < nt; ++i) Line(w, I(TetRef(m, i)));
            for (int i = 0; i < nb; ++i) Line(w, I(TriRef(m, i)));
        }
    }
}
