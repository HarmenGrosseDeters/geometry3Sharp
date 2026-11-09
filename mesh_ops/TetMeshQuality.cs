// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Shape quality of the tetrahedra of a mesh: radius-edge ratio (circumradius over shortest edge; sqrt(6)/4 = 0.612
    /// for the regular tetrahedron) and the dihedral angles in degrees (70.5 for the regular one), the measures of
    /// Delaunay refinement theory (Shewchuk 1998; Cheng, Dey, Shewchuk 2012) and of sliver detection. Distributions
    /// read from the bad side (QualityDistribution); all measures are scale invariant. Tetrahedra whose exact orientation
    /// is not positive are counted as Degenerate (a positive one that is flat in floating point is measured as radius-edge infinity,
    /// dihedral 0 and 180 and counts as a sliver) (radius-edge infinity, dihedral min 0, max 180).
    /// </summary>
    public class TetQualityStatistics
    {
        public int Tets;
        public int Degenerate;
        public double TotalVolume;
        public QualityDistribution RadiusEdge, MinDihedral, MaxDihedral;
        /// <summary> tetrahedra with a smallest dihedral angle below 10, 5, 1 degrees (slivers and worse) </summary>
        public int MinDihedralBelow10, MinDihedralBelow5, MinDihedralBelow1;
        /// <summary> tetrahedra with a radius-edge ratio above 2 </summary>
        public int RadiusEdgeAbove2;
        public int MaxDihedralAbove170;
    }

    /// <summary> Validity check and quality measures for TetMesh3 (docs/design/TET_MESHING.md section 7). </summary>
    public static class TetMeshQuality
    {
        /// <summary>
        /// Null if the mesh is a valid tetrahedral mesh of its boundary, else the first violation found: indices in range,
        /// every tetrahedron exactly positively oriented, every facet shared by two tetrahedra with opposite
        /// orientation or on the boundary, the boundary facets equal to BoundaryTriangles, the boundary a closed
        /// 2-manifold (every vertex has one fan). The volume of the tets equals the volume enclosed by the boundary then
        /// automatically (the sum telescopes over matched facets), and with a boundary that is embedded (the wrap's) the
        /// positive tets cover every enclosed point exactly once (degree argument), so no overlap check is needed.
        /// </summary>
        public static string NotValidReason(TetMesh3 m)
        {
            int nv = m.Vertices.Count;
            foreach (Vector3d v in m.Vertices)
                if (!(Math.Abs(v.x) <= double.MaxValue && Math.Abs(v.y) <= double.MaxValue && Math.Abs(v.z) <= double.MaxValue))
                    return "a vertex is not finite";
            var faces = new Dictionary<(int, int, int), int>();
            for (int c = 0; c < m.Tets.Count; ++c) {
                Index4i t = m.Tets[c];
                for (int i = 0; i < 4; ++i)
                    if (t[i] < 0 || t[i] >= nv) return "tet " + c + " has a vertex index out of range";
                if (t.a == t.b || t.a == t.c || t.a == t.d || t.b == t.c || t.b == t.d || t.c == t.d)
                    return "tet " + c + " repeats a vertex";
                if (ExactPredicates.Orient3d(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c], m.Vertices[t.d]) <= 0)
                    return "tet " + c + " is not positively oriented";
                for (int i = 0; i < 4; ++i) {
                    var key = canonical(t[Delaunay3.FacetIndices[i, 0]], t[Delaunay3.FacetIndices[i, 1]], t[Delaunay3.FacetIndices[i, 2]]);
                    if (faces.ContainsKey(key)) return "a facet occurs twice with the same orientation (tet " + c + ")";
                    faces[key] = c;
                }
            }
            var boundary = new HashSet<(int, int, int)>();
            foreach (var kv in faces) {
                var rev = canonical(kv.Key.Item1, kv.Key.Item3, kv.Key.Item2);
                if (!faces.ContainsKey(rev)) boundary.Add(kv.Key);
            }
            if (boundary.Count != m.BoundaryTriangles.Count) return "the facets without a partner (" + boundary.Count + ") differ in number from the boundary triangles (" + m.BoundaryTriangles.Count + ")";
            foreach (Index3i t in m.BoundaryTriangles)
                if (!boundary.Contains(canonical(t.a, t.b, t.c))) return "a boundary triangle is not a facet of one tet without a partner";
            string manifold = boundary_manifold_reason(m.BoundaryTriangles);
            if (manifold != null) return manifold;
            return null;
        }

        static double Volume(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            return (b - a).Cross(c - a).Dot(d - a) / 6.0;
        }

        static (int, int, int) canonical(int a, int b, int c)
        {
            if (a <= b && a <= c) return (a, b, c);
            if (b <= a && b <= c) return (b, c, a);
            return (c, a, b);
        }

        static string boundary_manifold_reason(List<Index3i> tris)
        {
            var edge = new Dictionary<(int, int), int>();
            for (int k = 0; k < tris.Count; ++k)
                for (int j = 0; j < 3; ++j) {
                    var e = (tris[k][j], tris[k][(j + 1) % 3]);
                    if (edge.ContainsKey(e)) return "a boundary edge occurs twice with the same direction";
                    edge[e] = k;
                }
            var fan = new Dictionary<int, List<int>>();
            for (int k = 0; k < tris.Count; ++k)
                for (int j = 0; j < 3; ++j) {
                    var e = (tris[k][j], tris[k][(j + 1) % 3]);
                    if (!edge.ContainsKey((e.Item2, e.Item1))) return "the boundary is not closed";
                    if (!fan.TryGetValue(e.Item1, out var l)) fan[e.Item1] = l = new List<int>();
                    l.Add(k);
                }
            foreach (var kv in fan) {
                // triangles around vertex v: walk next = the triangle across the edge (v, w) leaving v; one cycle covers all of them
                int v = kv.Key, start = kv.Value[0], cur = start, count = 0;
                do {
                    Index3i t = tris[cur];
                    int j = t.a == v ? 0 : t.b == v ? 1 : 2;
                    int w = t[(j + 2) % 3];            // the edge (w, v) enters v; the triangle across it
                    if (!edge.TryGetValue((v, w), out cur)) return "the boundary is not closed";
                    ++count;
                } while (cur != start && count <= kv.Value.Count);
                if (count != kv.Value.Count) return "the boundary is not a manifold at vertex " + v;
            }
            return null;
        }

        /// <summary> radius-edge ratio and the six dihedral angles (degrees) of one tetrahedron; false if it is degenerate </summary>
        public static bool Measure(Vector3d a, Vector3d b, Vector3d c, Vector3d d, out double radiusEdge, out double minDihedral, out double maxDihedral)
        {
            radiusEdge = double.PositiveInfinity; minDihedral = 0; maxDihedral = 180;
            var p = new[] { a, b, c, d };
            // outward normals (unnormalised) of the facets opposite each vertex
            var n = new Vector3d[4];
            for (int i = 0; i < 4; ++i) {
                Vector3d f0 = p[Delaunay3.FacetIndices[i, 0]], f1 = p[Delaunay3.FacetIndices[i, 1]], f2 = p[Delaunay3.FacetIndices[i, 2]];
                n[i] = (f1 - f0).Cross(f2 - f0);
            }
            if (ExactPredicates.Orient3d(a, b, c, d) <= 0) return false;      // exact: a positive but nearly flat tet is a sliver, not degenerate
            if (!(Volume(a, b, c, d) > 0)) { radiusEdge = double.PositiveInfinity; minDihedral = 0; maxDihedral = 180; return true; }   // flat in floating point
            double mn = double.MaxValue, mx = 0;
            for (int i = 0; i < 4; ++i)
                for (int j = i + 1; j < 4; ++j) {
                    // the dihedral angle at the edge shared by the facets opposite i and j is pi - angle(n_i, n_j)
                    double angle = Math.Atan2(n[i].Cross(n[j]).Length, n[i].Dot(n[j]));
                    double dih = 180 - angle * 180 / Math.PI;
                    if (dih < mn) mn = dih;
                    if (dih > mx) mx = dih;
                }
            minDihedral = mn; maxDihedral = mx;
            double shortest = double.MaxValue;
            for (int i = 0; i < 4; ++i)
                for (int j = i + 1; j < 4; ++j)
                    shortest = Math.Min(shortest, p[i].Distance(p[j]));
            Vector3d cc = Delaunay3.Circumcenter(a, b, c, d);
            double R = cc.Distance(a);
            radiusEdge = shortest > 0 && Math.Abs(R) <= double.MaxValue ? R / shortest : double.PositiveInfinity;
            return true;
        }

        public static TetQualityStatistics Measure(TetMesh3 m)
        {
            int n = m.Tets.Count;
            var re = new double[n]; var mind = new double[n]; var maxd = new double[n];
            var r = new TetQualityStatistics() { Tets = n };
            for (int k = 0; k < n; ++k) {
                Index4i t = m.Tets[k];
                Vector3d a = m.Vertices[t.a], b = m.Vertices[t.b], c = m.Vertices[t.c], d = m.Vertices[t.d];
                if (!Measure(a, b, c, d, out re[k], out mind[k], out maxd[k])) r.Degenerate++;
                else r.TotalVolume += Volume(a, b, c, d);
                if (mind[k] < 10) r.MinDihedralBelow10++;
                if (mind[k] < 5) r.MinDihedralBelow5++;
                if (mind[k] < 1) r.MinDihedralBelow1++;
                if (maxd[k] > 170) r.MaxDihedralAbove170++;
                if (re[k] > 2) r.RadiusEdgeAbove2++;
            }
            r.RadiusEdge = QualityDistribution.Compute(re, true);
            r.MinDihedral = QualityDistribution.Compute(mind, false);
            r.MaxDihedral = QualityDistribution.Compute(maxd, true);
            return r;
        }
    }
}
