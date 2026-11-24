// Probe of docs/research/TET_RADIUS_EDGE_AT_SURFACE_2026-11.md: where the tets above the radius-edge bound sit and why the
// refinement leaves them.
//   ... -- surfprobe [relativeAlpha] [surfaceMinAngle] [improve 0|1] [slide] [inputs: all | sphere,torus,boxhole,twist] [relH: TargetEdgeLength = diagonal / relH, 0 = off]
// For each procedural input one MeshTetrahedralization run, then per tet of Result with radius-edge > MaxRadiusEdgeRatio:
//   - its boundary contact (boundary vertices, boundary facets), the distance of its centroid to the boundary (in alpha);
//   - whether its circumcentre lies outside the boundary (the classical "beyond a subfacet" case);
//   - the facet bound: for a boundary facet f of the tet, every tet on f has radius-edge >= r_f / l_min(f) (R >= r_f, shortest
//     edge <= l_min(f)); a bad tet whose facet bound is above the target cannot be repaired while f is kept;
//   - encroachment: a vertex of the tet strictly inside the diametral ball (centre the circumcentre of f in its plane, radius r_f)
//     of one of the boundary facets the tet has or touches.
// The surface itself: the share of boundary triangles whose own r / l_min is above the target.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class SurfaceProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    sealed class Fn : ImplicitFunction3d
    {
        public Func<Vector3d, double> F;
        public double Value(ref Vector3d p) => F(p);
    }

    static DMesh3 Implicit(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell)
    {
        var mc = new MarchingCubes() { Implicit = new Fn { F = f }, Bounds = box, CubeSize = cell, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
        mc.Generate();
        return mc.Mesh;
    }

    internal static DMesh3 Input(string name)
    {
        switch (name) {
            case "sphere":
                return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            case "torus": {
                    var m = new DMesh3();
                    int major = 64, minor = 32; double R = 1, r = 0.35;
                    for (int i = 0; i < major; ++i)
                        for (int j = 0; j < minor; ++j) {
                            double u = 2 * Math.PI * i / major, v = 2 * Math.PI * j / minor;
                            m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                        }
                    for (int i = 0; i < major; ++i)
                        for (int j = 0; j < minor; ++j) {
                            int a = i * minor + j, b = ((i + 1) % major) * minor + j, c = ((i + 1) % major) * minor + (j + 1) % minor, d = i * minor + (j + 1) % minor;
                            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                        }
                    return m;
                }
            case "boxhole":   // box 2 x 2 x 1 minus a cylinder of radius 0.5 along z (sharp edges, a concave curved face)
                return Implicit(p => Math.Max(Math.Max(Math.Abs(p.x) - 1, Math.Abs(p.y) - 1), Math.Max(Math.Abs(p.z) - 0.5, 0.5 - Math.Sqrt(p.x * p.x + p.y * p.y))),
                    new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -0.6), new Vector3d(1.1, 1.1, 0.6)), 0.025);
            case "twist": {   // a 0.5 x 0.5 x 3 bar twisted by 180 degrees along its length
                    DMesh3 m = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.25, 0.25, 1.5)), EdgeVertices = 25 }.Generate().MakeDMesh();
                    foreach (int v in m.VertexIndices()) {
                        Vector3d p = m.GetVertex(v);
                        double a = Math.PI * (p.z + 1.5) / 3, c = Math.Cos(a), s = Math.Sin(a);
                        m.SetVertex(v, new Vector3d(c * p.x - s * p.y, s * p.x + c * p.y, p.z));
                    }
                    return m;
                }
        }
        throw new ArgumentException(name);
    }

    static double TriCircumradius(Vector3d a, Vector3d b, Vector3d c, out Vector3d centre)
    {
        Vector3d ab = b - a, ac = c - a, n = ab.Cross(ac);
        double n2 = n.LengthSquared;
        Vector3d off = (ac.LengthSquared * n.Cross(ab) + ab.LengthSquared * ac.Cross(n)) / (2 * n2);
        centre = a + off;
        return off.Length;
    }

    static double MinEdge(Vector3d a, Vector3d b, Vector3d c) => Math.Min(a.Distance(b), Math.Min(b.Distance(c), c.Distance(a)));

    internal sealed class Row
    {
        public string Name; public int Tets, Bad; public double Seconds, WorstRe, MinDih;
        public int[] ByBoundaryVertices = new int[5]; public int[] ByBoundaryFacets = new int[5];
        public int CcOutside, FacetBoundAbove, Encroached, NoExplanation;
        public double MaxCentroidDistAlpha;
        public int SurfaceTris, SurfaceTrisAbove; public double SurfaceWorstRatio, SurfaceMinAngle = 180;
        public long Unrefined, RefusedSurface, RefusedFloor, Peeled;
    }

    internal static Row Probe(string name, DMesh3 input, double ra, double surfAngle, bool improve, double slide, double relH = 0)
    {
        var sw = Stopwatch.StartNew();
        var tm = new MeshTetrahedralization(input) { RelativeAlpha = ra, SurfaceMinAngleDegrees = surfAngle, Improve = improve, BoundarySlideFactor = slide, MaxThreads = 1 };
        if (relH > 0) tm.TargetEdgeLength = input.CachedBounds.DiagonalLength / relH;
        tm.Compute();
        var row = new Row { Name = name, Seconds = sw.Elapsed.TotalSeconds };
        TetMesh3 t = tm.Result;
        double rho = tm.MaxRadiusEdgeRatio, alpha = tm.UsedAlpha;
        row.Tets = t.Tets.Count;
        row.Unrefined = tm.Counts["unrefined_bad_tets"];
        row.RefusedSurface = tm.Counts["refused_surface"]; row.RefusedFloor = tm.Counts["refused_floor"]; row.Peeled = tm.Counts["peeled"];
        // boundary facets as sorted keys; boundary vertices
        var bfacets = new HashSet<(int, int, int)>();
        var bverts = new HashSet<int>();
        var trisAtVertex = new Dictionary<int, List<Index3i>>();
        foreach (Index3i f in t.BoundaryTriangles) {
            bfacets.Add(Key(f.a, f.b, f.c));
            for (int j = 0; j < 3; ++j) {
                bverts.Add(f[j]);
                if (!trisAtVertex.TryGetValue(f[j], out var l)) trisAtVertex[f[j]] = l = new List<Index3i>();
                l.Add(f);
            }
            Vector3d a = t.Vertices[f.a], b = t.Vertices[f.b], c = t.Vertices[f.c];
            double ratio = TriCircumradius(a, b, c, out _) / MinEdge(a, b, c);
            row.SurfaceTris++;
            if (ratio > rho) row.SurfaceTrisAbove++;
            row.SurfaceWorstRatio = Math.Max(row.SurfaceWorstRatio, ratio);
            row.SurfaceMinAngle = Math.Min(row.SurfaceMinAngle, MinAngle(a, b, c));
        }
        DMesh3 surf = t.ToSurfaceMesh();
        var tree = new DMeshAABBTree3(surf, true);
        row.MinDih = 180;
        foreach (Index4i q in t.Tets) {
            Vector3d a = t.Vertices[q.a], b = t.Vertices[q.b], c = t.Vertices[q.c], d = t.Vertices[q.d];
            TetMeshQuality.Measure(a, b, c, d, out double re, out double mind, out _);
            row.MinDih = Math.Min(row.MinDih, mind);
            row.WorstRe = Math.Max(row.WorstRe, re);
            if (re <= rho) continue;
            row.Bad++;
            int[] v = { q.a, q.b, q.c, q.d };
            int nbv = v.Count(bverts.Contains);
            row.ByBoundaryVertices[nbv]++;
            var faces = new List<Index3i>();
            int[][] fi = { new[] { 1, 2, 3 }, new[] { 0, 2, 3 }, new[] { 0, 1, 3 }, new[] { 0, 1, 2 } };
            foreach (var k in fi) if (bfacets.Contains(Key(v[k[0]], v[k[1]], v[k[2]]))) faces.Add(new Index3i(v[k[0]], v[k[1]], v[k[2]]));
            row.ByBoundaryFacets[faces.Count]++;
            Vector3d cen = (a + b + c + d) * 0.25;
            double dist = MeshQueries.TriangleDistance(surf, tree.FindNearestTriangle(cen), cen).DistanceSquared;
            row.MaxCentroidDistAlpha = Math.Max(row.MaxCentroidDistAlpha, Math.Sqrt(dist) / alpha);
            Vector3d cc = Delaunay3.Circumcenter(a, b, c, d);
            bool outside = !tree.IsInside(cc);
            if (outside) row.CcOutside++;
            double shortest = Math.Min(Math.Min(Math.Min(a.Distance(b), a.Distance(c)), Math.Min(a.Distance(d), b.Distance(c))), Math.Min(b.Distance(d), c.Distance(d)));
            bool facetBound = false;
            foreach (Index3i f in faces) {
                Vector3d fa = t.Vertices[f.a], fb = t.Vertices[f.b], fc = t.Vertices[f.c];
                if (TriCircumradius(fa, fb, fc, out _) / MinEdge(fa, fb, fc) > rho) facetBound = true;
            }
            if (facetBound) row.FacetBoundAbove++;
            // encroachment of the boundary triangles at the tet's boundary vertices by a vertex of the tet
            bool enc = false;
            foreach (int w in v) {
                if (!trisAtVertex.TryGetValue(w, out var l)) continue;
                foreach (Index3i f in l) {
                    double rf = TriCircumradius(t.Vertices[f.a], t.Vertices[f.b], t.Vertices[f.c], out Vector3d fcen);
                    foreach (int u in v) if (u != f.a && u != f.b && u != f.c && t.Vertices[u].Distance(fcen) < rf * (1 - 1e-12)) enc = true;
                }
            }
            if (enc) row.Encroached++;
            if (!outside && !facetBound && !enc) row.NoExplanation++;
        }
        return row;
    }

    static double MinAngle(Vector3d a, Vector3d b, Vector3d c)
    {
        double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
        return Math.Min(Math.Min(A, B), 180 - A - B);
    }

    static (int, int, int) Key(int a, int b, int c)
    {
        if (a > b) { int x = a; a = b; b = x; }
        if (b > c) { int x = b; b = c; c = x; }
        if (a > b) { int x = a; a = b; b = x; }
        return (a, b, c);
    }

    internal static int Run(string[] args)
    {
        double ra = args.Length > 1 ? double.Parse(args[1], Inv) : 40, ang = args.Length > 2 ? double.Parse(args[2], Inv) : 15;
        bool improve = args.Length > 3 ? args[3] == "1" : true;
        double slide = args.Length > 4 ? double.Parse(args[4], Inv) : 0.5;
        string[] names = args.Length > 5 && args[5] != "all" ? args[5].Split(',') : new[] { "sphere", "torus", "boxhole", "twist" };
        double relH = args.Length > 6 ? double.Parse(args[6], Inv) : 0;
        Console.WriteLine("input,alpha,surf_angle,improve,slide,rel_h,tets,s,bad,worst_re,min_dih,unrefined_s1,refused_surface,refused_floor,peeled,"
            + "bad_bv0,bad_bv1,bad_bv2,bad_bv3,bad_bv4,bad_bf0,bad_bf1,bad_bf2,bad_bf3,cc_outside,facet_bound_above,encroached,unexplained,max_centroid_dist_alpha,"
            + "surf_tris,surf_tris_above,surf_worst_ratio,surf_min_angle");
        foreach (string n in names) {
            Row r = Probe(n, Input(n), ra, ang, improve, slide, relH);
            Console.WriteLine(string.Join(",", new object[] { r.Name, ra, ang, improve ? 1 : 0, slide, relH, r.Tets, r.Seconds.ToString("F2", Inv), r.Bad, r.WorstRe.ToString("F2", Inv), r.MinDih.ToString("F2", Inv),
                r.Unrefined, r.RefusedSurface, r.RefusedFloor, r.Peeled,
                r.ByBoundaryVertices[0], r.ByBoundaryVertices[1], r.ByBoundaryVertices[2], r.ByBoundaryVertices[3], r.ByBoundaryVertices[4],
                r.ByBoundaryFacets[0], r.ByBoundaryFacets[1], r.ByBoundaryFacets[2], r.ByBoundaryFacets[3],
                r.CcOutside, r.FacetBoundAbove, r.Encroached, r.NoExplanation, r.MaxCentroidDistAlpha.ToString("F2", Inv),
                r.SurfaceTris, r.SurfaceTrisAbove, r.SurfaceWorstRatio.ToString("F2", Inv), r.SurfaceMinAngle.ToString("F1", Inv) }.Select(o => Convert.ToString(o, Inv))));
        }
        return 0;
    }
}
