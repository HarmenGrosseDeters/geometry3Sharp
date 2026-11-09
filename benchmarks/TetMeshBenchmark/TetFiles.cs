// Tetrahedral mesh files of other meshers, for the comparison with fTetWild (docs/design/TET_MESHING.md 10.1): read MEDIT .mesh and
// Gmsh MSH 2.x ASCII (public format descriptions), write both, and evaluate a tet mesh with the library's measures.
// Benchmark tool code, not part of the library.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

static class TetFiles
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static TetMesh3 Read(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".mesh") return ReadMedit(path);
        if (ext == ".msh") return ReadMsh2(path);
        throw new ArgumentException("unsupported tet file extension " + ext + " (.mesh or .msh)");
    }

    static TetMesh3 ReadMedit(string path)
    {
        var t = new TetMesh3();
        var tok = File.ReadAllText(path).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        int p = 0;
        while (p < tok.Length) {
            string k = tok[p++];
            if (k == "Vertices") {
                int n = int.Parse(tok[p++], Inv);
                for (int i = 0; i < n; ++i) { t.Vertices.Add(new Vector3d(D(tok[p]), D(tok[p + 1]), D(tok[p + 2]))); p += 4; }
            } else if (k == "Tetrahedra") {
                int n = int.Parse(tok[p++], Inv);
                for (int i = 0; i < n; ++i) { t.Tets.Add(new Index4i(I(tok[p]) - 1, I(tok[p + 1]) - 1, I(tok[p + 2]) - 1, I(tok[p + 3]) - 1)); p += 5; }
            }
        }
        return t;
    }

    static TetMesh3 ReadMsh2(string path)
    {
        var t = new TetMesh3();
        var lines = File.ReadAllLines(path);
        var idMap = new Dictionary<int, int>();
        for (int i = 0; i < lines.Length; ++i) {
            string l = lines[i].Trim();
            if (l == "$MeshFormat" && !lines[i + 1].TrimStart().StartsWith("2")) throw new NotSupportedException("only MSH 2.x ASCII: " + lines[i + 1]);
            if (l == "$Nodes") {
                int n = int.Parse(lines[++i].Trim(), Inv);
                for (int k = 0; k < n; ++k) {
                    var f = lines[++i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    idMap[int.Parse(f[0], Inv)] = t.Vertices.Count;
                    t.Vertices.Add(new Vector3d(D(f[1]), D(f[2]), D(f[3])));
                }
            } else if (l == "$Elements") {
                int n = int.Parse(lines[++i].Trim(), Inv);
                for (int k = 0; k < n; ++k) {
                    var f = lines[++i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (f[1] != "4") continue;                                  // tetrahedra only
                    int first = 3 + int.Parse(f[2], Inv);
                    t.Tets.Add(new Index4i(idMap[I(f[first])], idMap[I(f[first + 1])], idMap[I(f[first + 2])], idMap[I(f[first + 3])]));
                }
            }
        }
        return t;
    }

    static double D(string s) => double.Parse(s, Inv);
    static int I(string s) => int.Parse(s, Inv);

    public static void Write(TetMesh3 t, string path)
    {
        using (var w = new StreamWriter(path)) {
            string R(double x) => x.ToString("R", Inv);
            if (Path.GetExtension(path).ToLowerInvariant() == ".mesh") {
                w.WriteLine("MeshVersionFormatted 2\nDimension 3\nVertices\n" + t.Vertices.Count);
                foreach (var v in t.Vertices) w.WriteLine(R(v.x) + " " + R(v.y) + " " + R(v.z) + " 0");
                w.WriteLine("Tetrahedra\n" + t.Tets.Count);
                foreach (var q in t.Tets) w.WriteLine((q.a + 1) + " " + (q.b + 1) + " " + (q.c + 1) + " " + (q.d + 1) + " 0");
                w.WriteLine("End");
            } else {
                w.WriteLine("$MeshFormat\n2.2 0 8\n$EndMeshFormat\n$Nodes\n" + t.Vertices.Count);
                for (int i = 0; i < t.Vertices.Count; ++i) w.WriteLine((i + 1) + " " + R(t.Vertices[i].x) + " " + R(t.Vertices[i].y) + " " + R(t.Vertices[i].z));
                w.WriteLine("$EndNodes\n$Elements\n" + t.Tets.Count);
                for (int i = 0; i < t.Tets.Count; ++i) { var q = t.Tets[i]; w.WriteLine((i + 1) + " 4 2 0 0 " + (q.a + 1) + " " + (q.b + 1) + " " + (q.c + 1) + " " + (q.d + 1)); }
                w.WriteLine("$EndElements");
            }
        }
    }

    public const string Header = "name,time_s,threads,tets,vertices,inverted_fixed,valid,re_worst,re_p5,re_median,re_gt2_share,mindih_worst,mindih_p1,mindih_p5,mindih_median,dih_lt10_share,dih_ge10_all";

    /// <summary> one CSV row: tets of another mesher re-oriented to positive (counted), validity of the whole complex (boundary derived from the unmatched facets), quality </summary>
    public static string Evaluate(string name, string tetFile, string timeS, string threads)
    {
        TetMesh3 t = Read(tetFile);
        int fixedCount = 0;
        for (int i = 0; i < t.Tets.Count; ++i) {
            Index4i q = t.Tets[i];
            if (ExactPredicates.Orient3d(t.Vertices[q.a], t.Vertices[q.b], t.Vertices[q.c], t.Vertices[q.d]) < 0) { t.Tets[i] = new Index4i(q.b, q.a, q.c, q.d); fixedCount++; }
        }
        var faces = new HashSet<(int, int, int)>();
        Func<int, int, int, (int, int, int)> canon = (a, b, c) => a <= b && a <= c ? (a, b, c) : b <= a && b <= c ? (b, c, a) : (c, a, b);
        foreach (Index4i q in t.Tets)
            for (int i = 0; i < 4; ++i)
                faces.Add(canon(q[Delaunay3.FacetIndices[i, 0]], q[Delaunay3.FacetIndices[i, 1]], q[Delaunay3.FacetIndices[i, 2]]));
        foreach (var f in faces)
            if (!faces.Contains(canon(f.Item1, f.Item3, f.Item2))) t.BoundaryTriangles.Add(new Index3i(f.Item1, f.Item2, f.Item3));
        // vertices no tet uses (surface-only points of the file) are left alone: the check is on the tets
        string bad = TetMeshQuality.NotValidReason(t);
        TetQualityStatistics q0 = TetMeshQuality.Measure(t);
        int n = Math.Max(1, t.Tets.Count);
        string F(double x) => x.ToString("G6", Inv);
        return string.Join(",", name, timeS, threads, t.Tets.Count, t.Vertices.Count, fixedCount, bad == null ? "yes" : bad.Replace(",", ";"),
            F(q0.RadiusEdge.Worst), F(q0.RadiusEdge.Worst5Percent), F(q0.RadiusEdge.Median), F(q0.RadiusEdgeAbove2 / (double)n),
            F(q0.MinDihedral.Worst), F(q0.MinDihedral.Worst1Percent), F(q0.MinDihedral.Worst5Percent), F(q0.MinDihedral.Median),
            F(q0.MinDihedralBelow10 / (double)n), q0.MinDihedral.Worst >= 10 ? "yes" : "no");
    }
}
