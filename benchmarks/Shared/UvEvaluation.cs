// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

/// <summary>
/// Evaluation of a UV atlas, the same code for every atlas tool (UVAtlasGenerator and xatlas in
/// benchmarks/UVAtlasBenchmark), so that the comparison is fair. Shared by the benchmark (evaluate mode) and the tests.
///
/// The atlas is an OBJ file with one vertex per atlas vertex (v and vt with the same index, so uv[i] belongs to v i)
/// and one face per input triangle, in the input's triangle order: face i is the atlas of input triangle i. The UVs
/// are in the unit square (for xatlas: divided by the atlas width and height, per axis, which is how a texture
/// is addressed). Charts are the connected components of the atlas faces across shared atlas edges, so a chart
/// boundary is a seam in the atlas, whatever the tool.
///
/// Metrics: exact overlaps and flipped triangles (UVOverlap), distortion (ParameterizationDistortion, area weighted
/// means), charts and discs (MeshTopologySummary), seam length / sqrt(area), coverage of the unit square and the gap
/// between charts on a RasterSize raster (pixel centres), and the spread of the texel density across charts.
/// Seam: an input edge along which the atlas is cut (its two faces meet at different atlas vertices), so a cut inside
/// one chart counts. Gap: Chebyshev distance in pixels between the nearest pixels of two charts: 1 = touching, 0 = a
/// pixel inside two charts, GapCap + 1 = no two charts closer than GapCap pixels.
/// </summary>
public static class UvEvaluation
{
    /// <summary> side of the raster for coverage and the gap between charts (pixels) </summary>
    public const int RasterSize = 1024;
    /// <summary> largest gap measured in pixels; a larger gap is reported as GapCap + 1 </summary>
    public const int GapCap = 8;
    /// <summary> tolerance of the 3D correspondence check, relative to the bounding box diagonal </summary>
    public const double CorrespondenceTolerance = 1e-5;

    /// <summary> an atlas: its mesh (3D positions, faces as atlas triangles) and the UV of every vertex id </summary>
    public sealed class Atlas
    {
        public DMesh3 Mesh;
        public Vector2d[] UV;
    }

    /// <summary>
    /// The atlas of a UVAtlasGenerator run in the layout of this class: a compact copy of its atlas mesh and UVs, faces in
    /// input order (face i = input triangle i). An input triangle that is not in the atlas gets no face (the evaluation
    /// then reports the atlas as incomplete). Triangles of the atlas mesh are the atlas triangles, AtlasToInputTriangle[k]
    /// is the input triangle of atlas triangle k.
    /// </summary>
    public static Atlas FromUVAtlas(DMesh3 input, UVAtlasGenerator gen)
    {
        DMesh3 am = gen.AtlasMesh;
        var atlasOf = new Dictionary<int, int>();
        int k = 0;
        foreach (int t in am.TriangleIndices())
            atlasOf[gen.AtlasToInputTriangle[k++]] = t;
        var index = new Dictionary<int, int>();
        var mesh = new DMesh3(MeshComponents.None);
        var uv = new List<Vector2d>();
        foreach (int v in am.VertexIndices()) {
            index[v] = mesh.AppendVertex(am.GetVertex(v));
            uv.Add(new Vector2d((float)gen.UV[v].x, (float)gen.UV[v].y));   // stored precision: float
        }
        foreach (int t in input.TriangleIndices()) {
            if (!atlasOf.TryGetValue(t, out int at)) continue;
            Index3i f = am.GetTriangle(at);
            mesh.AppendTriangle(index[f.a], index[f.b], index[f.c]);
        }
        return new Atlas { Mesh = mesh, UV = uv.ToArray() };
    }

    /// <summary> the mesh of an input (ids compacted, triangles in id order) as OBJ: the input of an external atlas tool </summary>
    public static void WriteInput(string path, DMesh3 mesh)
    {
        var index = new Dictionary<int, int>();
        var positions = new List<Vector3d>();
        foreach (int v in mesh.VertexIndices()) {
            index[v] = positions.Count;
            positions.Add(mesh.GetVertex(v));
        }
        using (var w = new StreamWriter(path)) {
            var ci = CultureInfo.InvariantCulture;
            foreach (Vector3d p in positions)
                w.WriteLine("v " + p.x.ToString("R", ci) + " " + p.y.ToString("R", ci) + " " + p.z.ToString("R", ci));
            foreach (int t in mesh.TriangleIndices()) {
                Index3i f = mesh.GetTriangle(t);
                w.WriteLine("f " + (index[f.a] + 1) + " " + (index[f.b] + 1) + " " + (index[f.c] + 1));
            }
        }
    }

    /// <summary> writes an atlas as OBJ in the layout of this class (vertices compacted in id order) </summary>
    public static void WriteAtlas(string path, Atlas atlas)
    {
        DMesh3 m = atlas.Mesh;
        var index = new Dictionary<int, int>();
        var positions = new List<Vector3d>();
        var uv = new List<Vector2d>();
        foreach (int v in m.VertexIndices()) {
            index[v] = positions.Count;
            positions.Add(m.GetVertex(v));
            uv.Add(atlas.UV[v]);
        }
        using (var w = new StreamWriter(path)) {
            var ci = CultureInfo.InvariantCulture;
            foreach (Vector3d p in positions)
                w.WriteLine("v " + p.x.ToString("R", ci) + " " + p.y.ToString("R", ci) + " " + p.z.ToString("R", ci));
            foreach (Vector2d q in uv)
                w.WriteLine("vt " + ((float)q.x).ToString("R", ci) + " " + ((float)q.y).ToString("R", ci));
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                int a = index[f.a] + 1, b = index[f.b] + 1, c = index[f.c] + 1;
                w.WriteLine("f " + a + "/" + a + " " + b + "/" + b + " " + c + "/" + c);
            }
        }
    }

    /// <summary>
    /// Reads an atlas OBJ written by WriteObj (or by xatlas_driver, which writes the same layout). Faces may be
    /// polygons (fan triangulated). Throws FormatException when the file is not an atlas of this layout.
    /// </summary>
    public static Atlas ReadObj(string path)
    {
        var ci = CultureInfo.InvariantCulture;
        var pos = new List<Vector3d>();
        var vt = new List<Vector2d>();
        var faces = new List<Index3i>();
        foreach (string raw in File.ReadLines(path)) {
            string line = raw.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal)) {
                string[] s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (s.Length < 4) throw new FormatException("vertex with fewer than 3 coordinates");
                pos.Add(new Vector3d(Num(s[1], ci), Num(s[2], ci), Num(s[3], ci)));
            } else if (line.StartsWith("vt ", StringComparison.Ordinal)) {
                string[] s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (s.Length < 3) throw new FormatException("texture coordinate with fewer than 2 values");
                vt.Add(new Vector2d((float)Num(s[1], ci), (float)Num(s[2], ci)));   // float, as stored
            } else if (line.StartsWith("f ", StringComparison.Ordinal)) {
                string[] s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (s.Length < 4) throw new FormatException("face with fewer than 3 corners");
                int[] idx = new int[s.Length - 1];
                for (int k = 1; k < s.Length; ++k) {
                    string[] parts = s[k].Split('/');
                    idx[k - 1] = Index(parts[0], pos.Count);
                    // the UV index must be the vertex index: one UV per vertex
                    if (parts.Length > 1 && parts[1].Length > 0 && Index(parts[1], vt.Count) != idx[k - 1])
                        throw new FormatException("UV index differs from the vertex index");
                }
                for (int k = 1; k + 1 < idx.Length; ++k)
                    faces.Add(new Index3i(idx[0], idx[k], idx[k + 1]));
            }
        }
        if (vt.Count != pos.Count) throw new FormatException("one texture coordinate per vertex is needed");
        var mesh = new DMesh3(MeshComponents.None);
        foreach (Vector3d p in pos) mesh.AppendVertex(p);
        foreach (Index3i f in faces)
            if (mesh.AppendTriangle(f) < 0) throw new FormatException("triangle rejected by the mesh (degenerate or non-manifold edge)");
        return new Atlas { Mesh = mesh, UV = vt.ToArray() };
    }

    static double Num(string s, IFormatProvider ci)
    {
        if (!double.TryParse(s, NumberStyles.Float, ci, out double x) || double.IsNaN(x) || double.IsInfinity(x))
            throw new FormatException("bad number '" + s + "'");
        return x;
    }

    static int Index(string s, int count)
    {
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int i) || i < 1 || i > count)
            throw new FormatException("bad index '" + s + "'");
        return i - 1;
    }

    /// <summary>
    /// Evaluates an atlas of the input mesh. Status: "ok" (metrics filled in), "incomplete" (not one face per input
    /// triangle), "correspondence_mismatch" (an atlas face is not the input face at the same index).
    /// </summary>
    public static Dictionary<string, object> Evaluate(DMesh3 input, Atlas atlas)
    {
        var r = new Dictionary<string, object>();
        DMesh3 m = atlas.Mesh;
        r["faces"] = input.TriangleCount;
        r["atlas_faces"] = m.TriangleCount;
        if (m.TriangleCount != input.TriangleCount) {
            r["status"] = "incomplete";
            return r;
        }
        var inTids = input.TriangleIndices().ToArray();

        // face i of the atlas must be input triangle i (same corners in 3D)
        Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
        double maxAbs = 0;
        foreach (int t in inTids) {
            Index3i f = input.GetTriangle(t);
            foreach (int v in new[] { f.a, f.b, f.c }) {
                Vector3d p = input.GetVertex(v);
                lo = new Vector3d(Math.Min(lo.x, p.x), Math.Min(lo.y, p.y), Math.Min(lo.z, p.z));
                hi = new Vector3d(Math.Max(hi.x, p.x), Math.Max(hi.y, p.y), Math.Max(hi.z, p.z));
                maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
            }
        }
        // the positions of an atlas are stored in float by xatlas: a rounding of about 6e-8 times the magnitude
        double tol = inTids.Length > 0 ? CorrespondenceTolerance * lo.Distance(hi) + 1e-6 * maxAbs : 0;
        int mismatched = 0;
        for (int i = 0; i < inTids.Length; ++i) {
            Index3i ti = input.GetTriangle(inTids[i]);
            Index3i to = m.GetTriangle(i);
            var p = new[] { input.GetVertex(ti.a), input.GetVertex(ti.b), input.GetVertex(ti.c) };
            var q = new[] { m.GetVertex(to.a), m.GetVertex(to.b), m.GetVertex(to.c) };
            if (!SameCorners(p, q, tol)) mismatched++;
        }
        if (mismatched > 0) {
            r["status"] = "correspondence_mismatch";
            r["mismatched_faces"] = mismatched;
            return r;
        }

        // the UVs are in the unit square; a chart outside it would be invisible to the raster, coverage and gap
        foreach (int v in m.VertexIndices()) {
            Vector2d q = atlas.UV[v];
            if (!(q.x >= -1e-9 && q.x <= 1 + 1e-9 && q.y >= -1e-9 && q.y <= 1 + 1e-9)) {
                r["status"] = "uv_out_of_range";
                return r;
            }
        }

        // exact overlaps and flipped triangles, and the distortion of the atlas
        UVOverlap ov = UVOverlap.Compute(m, atlas.UV);
        r["overlap_pairs"] = ov.OverlappingPairs;
        r["inverted"] = ov.InvertedTriangles;
        r["degenerate_uv"] = ov.DegenerateTriangles;
        ParameterizationDistortion d = ParameterizationDistortion.Compute(m, atlas.UV);
        r["mean_conformal"] = d.MeanConformal;
        r["max_conformal"] = d.MaxConformal;
        r["mean_area"] = d.MeanAreaDistortion;
        r["max_area"] = d.MaxAreaDistortion;

        // charts: components of the atlas faces across shared atlas edges
        int n = m.MaxTriangleID;
        int[] parent = new int[n];
        for (int t = 0; t < n; ++t) parent[t] = t;
        foreach (int e in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(e);
            if (et.a >= 0 && et.b >= 0) Union(parent, et.a, et.b);
        }
        var chartOfRoot = new Dictionary<int, int>();
        int[] chart = new int[m.TriangleCount];
        int ti2 = 0;
        foreach (int t in m.TriangleIndices()) {
            int root = Find(parent, t);
            if (!chartOfRoot.TryGetValue(root, out int c)) { c = chartOfRoot.Count; chartOfRoot[root] = c; }
            chart[ti2++] = c;
        }
        int charts = chartOfRoot.Count;
        r["charts"] = charts;
        MeshTopologySummary topo = MeshTopologySummary.Compute(m);
        r["components"] = topo.ComponentList.Count;
        r["discs"] = topo.ComponentList.Count(c => c.BoundaryLoops == 1 && c.EulerCharacteristic == 1);

        // the atlas vertex of each input corner: (face i, input vertex) -> atlas vertex, by the corner matching above
        var atlasVertexOf = new Dictionary<long, int>();
        int[] faceOfInput = new int[input.MaxTriangleID];
        for (int i = 0; i < inTids.Length; ++i) {
            faceOfInput[inTids[i]] = i;
            Index3i ti = input.GetTriangle(inTids[i]);
            Index3i to = m.GetTriangle(i);
            foreach (int iv in new[] { ti.a, ti.b, ti.c }) {
                Vector3d p = input.GetVertex(iv);
                foreach (int av in new[] { to.a, to.b, to.c })
                    if (m.GetVertex(av).Distance(p) <= tol) { atlasVertexOf[(long)i * input.MaxVertexID + iv] = av; break; }
            }
        }
        // a seam: an input edge along which the atlas is cut, ie its two faces meet at different atlas vertices
        double area = 0, seam = 0;
        foreach (int t in inTids) area += input.GetTriArea(t);
        foreach (int e in input.EdgeIndices()) {
            Index2i et = input.GetEdgeT(e);
            if (et.a < 0 || et.b < 0) continue;
            Index2i ev = input.GetEdgeV(e);
            long fa = (long)faceOfInput[et.a] * input.MaxVertexID, fb = (long)faceOfInput[et.b] * input.MaxVertexID;
            bool cut = atlasVertexOf[fa + ev.a] != atlasVertexOf[fb + ev.a] || atlasVertexOf[fa + ev.b] != atlasVertexOf[fb + ev.b];
            if (!cut) continue;
            seam += input.GetVertex(ev.a).Distance(input.GetVertex(ev.b));
        }
        r["area"] = area;
        r["seam_length"] = seam;
        r["seam_rel"] = area > 0 ? seam / Math.Sqrt(area) : 0.0;

        // texel density per chart: sqrt(UV area / surface area); its spread (std of the log, over charts)
        double[] area3 = new double[charts], areaUV = new double[charts];
        int k = 0;
        foreach (int t in m.TriangleIndices()) {
            Index3i tr = m.GetTriangle(t);
            Vector2d a = atlas.UV[tr.a], b = atlas.UV[tr.b], c = atlas.UV[tr.c];
            area3[chart[k]] += m.GetTriArea(t);
            areaUV[chart[k]] += Math.Abs(Cross(b - a, c - a)) / 2;
            k++;
        }
        var logs = new List<double>();
        for (int c = 0; c < charts; ++c)
            if (area3[c] > 0 && areaUV[c] > 0) logs.Add(0.5 * Math.Log(areaUV[c] / area3[c]));
        r["density_logstd"] = logs.Count > 1 ? StdDev(logs) : 0.0;

        // coverage and the gap between charts on the raster (pixel centres)
        RasterGap(m, atlas.UV, chart, out double coverage, out int gap);
        r["coverage"] = coverage;
        r["min_gap_px"] = gap;
        r["status"] = "ok";
        return r;
    }

    static bool SameCorners(Vector3d[] p, Vector3d[] q, double tol)
    {
        return p.All(x => q.Any(y => y.Distance(x) <= tol)) && q.All(y => p.Any(x => x.Distance(y) <= tol));
    }

    static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a); b = Find(parent, b);
        if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
    }

    static int Find(int[] parent, int a)
    {
        while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
        return a;
    }

    static double Cross(Vector2d u, Vector2d v) => u.x * v.y - u.y * v.x;

    static double StdDev(List<double> xs)
    {
        double mean = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Count);
    }

    /// <summary>
    /// Coverage: fraction of the RasterSize^2 pixel centres inside some atlas face. Gap: the smallest Chebyshev distance
    /// (in pixels) between two pixels of different charts, 0 if a pixel is inside two charts, GapCap + 1 if no two
    /// charts come closer than GapCap pixels.
    /// </summary>
    static void RasterGap(DMesh3 m, Vector2d[] uv, int[] chart, out double coverage, out int gap)
    {
        const int N = RasterSize;
        int[] id = new int[N * N];
        for (int i = 0; i < id.Length; ++i) id[i] = -1;
        int k = 0;
        foreach (int t in m.TriangleIndices()) {
            Index3i tr = m.GetTriangle(t);
            Vector2d a = uv[tr.a] * N, b = uv[tr.b] * N, c = uv[tr.c] * N;
            double area2 = Cross(b - a, c - a);
            int c0 = chart[k++];
            if (area2 == 0 || double.IsNaN(area2)) continue;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x))));
            int x1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y))));
            int y1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(a.y, Math.Max(b.y, c.y))));
            double sgn = Math.Sign(area2);
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    Vector2d p = new Vector2d(x + 0.5, y + 0.5);
                    if (sgn * Cross(b - a, p - a) <= 0 || sgn * Cross(c - b, p - b) <= 0 || sgn * Cross(a - c, p - c) <= 0)
                        continue;
                    int idx = y * N + x;
                    if (id[idx] == -1) id[idx] = c0;
                    else if (id[idx] != c0) id[idx] = -2;   // inside two charts
                }
        }
        int covered = 0;
        bool shared = false;
        foreach (int v in id) {
            if (v != -1) covered++;
            if (v == -2) shared = true;
        }
        coverage = covered / (double)(N * N);
        gap = GapCap + 1;
        if (shared) { gap = 0; return; }
        for (int rad = 1; rad <= GapCap; ++rad) {
            for (int y = 0; y < N; ++y)
                for (int x = 0; x < N; ++x) {
                    int a = id[y * N + x];
                    if (a < 0) continue;
                    for (int dy = -rad; dy <= rad; ++dy)
                        for (int dx = -rad; dx <= rad; ++dx) {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != rad) continue;
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue;
                            int b = id[yy * N + xx];
                            if (b >= 0 && b != a) { gap = rad; return; }
                        }
                }
        }
    }
}
