// QuadSheafObstruction: research spike (docs/research/QUAD_SHEAF_OBSTRUCTION_2026-11.md). Does a cohomology computation on the lattice
// offsets of the position field (a 1-cochain of the Z^2 local system with quarter-turn transitions, the "cellular sheaf" of the quad
// pipeline) predict, before the flow and the repair of stage 3B, whether and where QuadRemesher.PureQuads falls back?
//   main pass    the 54 runs of benchmarks/QuadFlow3BGate; per run the sheaf quantities (curl mass, integer defect mass, the filling
//                energy of the sheaf Laplacian, the folds and the area of the relaxed real map), the outcome of the library, the area
//                invariant of the library's map, the ranking power (AUC) of every quantity for "fallback", and where the folds lie
//   levers=1     the oracle over the levers that exist (CurvatureAlignment 0 / 10, SizeAdaptation where the library allows it with
//                PureQuads): how many inputs are clean with at least one configuration
// The sheaf complex is rebuilt here from the public accessors of PositionField / CrossField (the formulas of QuadFlowRepair.BuildOffsets
// and BuildTriangles); the map of the library is read by reflection (QuadRemesher.PureQuadRepair is internal), so no library file changes.
//   dotnet run -c Release --project benchmarks/QuadSheafObstruction -- [only=<regex>] [levers=1] [radius=<x>]
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }
    static string F(double x, string fmt) { return double.IsNaN(x) ? "-" : x.ToString(fmt, CultureInfo.InvariantCulture); }

    // the inputs of QuadFlow3BGate (same construction, so the runs are the same)
    static DMesh3 MakeInput(Func<Vector3d, double> fn, double h, double noise)
    {
        var B = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
        var mc = new MarchingCubes { Implicit = new Fn { F = fn, B = B }, Bounds = B, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
        mc.Generate();
        DMesh3 m = mc.Mesh;
        DMesh3 src = new DMesh3(m);
        var r = new Remesher(m) { SmoothSpeedT = 0.5 };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
        r.SetTargetEdgeLength(h);
        for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
        m = new DMesh3(m, true);
        if (noise > 0) {
            var rnd = new Random(1);
            MeshNormals.QuickCompute(m);
            foreach (int v in m.VertexIndices()) { Vector3d n = (Vector3d)m.GetVertexNormal(v); m.SetVertex(v, m.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * noise * h)); }
        }
        return m;
    }

    static int FacingAway(DPolyMesh r, DMesh3 input, DMeshAABBTree3 tree)
    {
        int bad = 0;
        for (int f = 0; f < r.FaceCount; ++f) {
            var fv = r.GetFace(f);
            if (fv.Length != 4) { bad++; continue; }
            Vector3d p0 = r.GetVertex(fv[0]), p1 = r.GetVertex(fv[1]), p2 = r.GetVertex(fv[2]), p3 = r.GetVertex(fv[3]);
            Vector3d n = (p2 - p0).Cross(p3 - p1), c = 0.25 * (p0 + p1 + p2 + p3);
            int t = tree.FindNearestTriangle(c);
            if (t < 0 || n.Dot(input.GetTriNormal(t)) <= 0) bad++;
        }
        return bad;
    }

    // the independent check of QuadFlow3BGate: Success, a manifold of quads only, finite, no quad facing away from the input
    static bool IsClean(QuadRemesher q, DMesh3 input)
    {
        if (q.PureQuadStatus != PureQuadStatus.Success || q.Result == null) return false;
        PolyMeshMetrics m = PolyMeshMetrics.Compute(q.Result);
        var tree = new DMeshAABBTree3(input, true);
        bool finite = Enumerable.Range(0, q.Result.VertexCount).All(v => q.Result.GetVertex(v).IsFinite);
        return m.IsManifold && m.QuadFraction >= 1 && finite && q.Result.Validate().IsValid && FacingAway(q.Result, input, tree) == 0;
    }

    // ---------------------------------------------------------------------------------------------------- the sheaf complex

    static void Rot(int k, double x, double y, out double rx, out double ry)
    {
        k = ((k % 4) + 4) % 4;
        switch (k) { case 0: rx = x; ry = y; break; case 1: rx = -y; ry = x; break; case 2: rx = -x; ry = -y; break; default: rx = y; ry = -x; break; }
    }

    /// <summary>
    /// The 1-cochains of the position field on the triangles of the input: per triangle slot (edge V[s] -> V[s+1]) the integer offset D
    /// and the real offset R (the edge vector in the cross frame, in lattice units), both in the lattice frame of the triangle's first vertex; Nb / NbS the neighbour across the slot and its
    /// slot, Rt the quarter turns with D_neighbour = -R^Rt D_self. The same quantities as QuadFlowRepair.BuildOffsets / BuildTriangles.
    /// </summary>
    sealed class Sheaf
    {
        public int NT; public bool Ok = true, Closed = true;
        public int[] Tid, V, Nb, NbS, Rt, Dx, Dy; public double[] Rx, Ry;
        public Vector3d[] Centroid; public double[] Cells;      // per triangle: centroid, area in lattice cells (area / spacing^2)
        public double GeoCells; public int AntisymmetryViolations;

        static Vector3d MiddlePoint(Vector3d p0, Vector3d n0, Vector3d p1, Vector3d n1, double eps)
        {
            Vector3d m = 0.5 * (p0 + p1);
            double c = n0.Dot(n1), r0 = n0.Dot(p0 - m), r1 = n1.Dot(p1 - m);
            double d0 = 1 + eps, det = d0 * d0 - c * c;
            double alpha = (d0 * r0 - c * r1) / det, beta = (d0 * r1 - c * r0) / det;
            return m + alpha * n0 + beta * n1;
        }

        public Sheaf(DMesh3 mesh, CrossField cf, PositionField pf, double s)
        {
            int maxV = mesh.MaxVertexID, maxE = mesh.MaxEdgeID;
            var P = new Vector3d[maxV]; var N = new Vector3d[maxV]; var Q = new Vector3d[maxV]; var T = new Vector3d[maxV];
            foreach (int v in mesh.VertexIndices()) {
                if (!pf.HasPosition(v)) { Ok = false; return; }
                P[v] = mesh.GetVertex(v); N[v] = cf.VertexNormal(v); Q[v] = pf.Orientation(v); T[v] = N[v].Cross(Q[v]);
            }
            double[] spc = pf.SpacingFactors;
            var g = new int[maxE]; var dx = new int[maxE]; var dy = new int[maxE]; var rx = new double[maxE]; var ry = new double[maxE];
            foreach (int e in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                int u = Math.Min(ev.a, ev.b), v = Math.Max(ev.a, ev.b);
                double su = spc == null ? s : s * spc[u], sv = spc == null ? s : s * spc[v];
                Vector3d x = MiddlePoint(P[u], N[u], P[v], N[v], pf.MiddleRegularization);
                double dq = Q[u].Dot(Q[v]), dt = Q[u].Dot(T[v]);
                Vector3d b; int m;
                if (Math.Abs(dq) >= Math.Abs(dt)) { if (dq >= 0) { b = Q[v]; m = 0; } else { b = -Q[v]; m = 2; } }
                else { if (dt >= 0) { b = T[v]; m = 1; } else { b = -T[v]; m = 3; } }
                Vector3d tb = N[v].Cross(b), oi = pf.Origin(u), oj = pf.Origin(v), d = x - oi;
                double cq0 = Math.Round(Q[u].Dot(d) / su), ct0 = Math.Round(T[u].Dot(d) / su);
                Vector3d a = oi + su * (cq0 * Q[u] + ct0 * T[u]), d2 = a - oj;
                double cq1 = Math.Round(b.Dot(d2) / sv), ct1 = Math.Round(tb.Dot(d2) / sv);
                if (!(Math.Abs(cq0) < 1e6) || !(Math.Abs(ct0) < 1e6) || !(Math.Abs(cq1) < 1e6) || !(Math.Abs(ct1) < 1e6)) { Ok = false; return; }
                g[e] = (4 - m) % 4; dx[e] = (int)cq0 - (int)cq1; dy[e] = (int)ct0 - (int)ct1;
                // the real cochain: the edge vector in the averaged compatible cross frame, in lattice units (the coframe of the field on the
                // edge; the offsets of the origins, x - o_u against x - o_v, are the integers plus smoothing noise and carry no geometry)
                Vector3d ev3 = P[v] - P[u];
                rx[e] = 0.5 * (Q[u].Dot(ev3) / su + b.Dot(ev3) / sv); ry[e] = 0.5 * (T[u].Dot(ev3) / su + tb.Dot(ev3) / sv);
            }
            int Ge(int u, int w, int e) { if (u == w) return 0; return u < w ? g[e] : (4 - g[e]) % 4; }
            void Dir(int u, int w, int e, out double ix, out double iy, out double fx, out double fy)
            {
                if (u < w) { ix = dx[e]; iy = dy[e]; fx = rx[e]; fy = ry[e]; return; }
                Rot(-g[e], dx[e], dy[e], out ix, out iy); ix = -ix; iy = -iy;
                Rot(-g[e], rx[e], ry[e], out fx, out fy); fx = -fx; fy = -fy;
            }
            NT = mesh.TriangleCount;
            Tid = new int[NT]; V = new int[3 * NT]; Nb = new int[3 * NT]; NbS = new int[3 * NT]; Rt = new int[3 * NT];
            Dx = new int[3 * NT]; Dy = new int[3 * NT]; Rx = new double[3 * NT]; Ry = new double[3 * NT];
            Centroid = new Vector3d[NT]; Cells = new double[NT];
            var E = new int[3 * NT]; var toF = new int[mesh.MaxTriangleID];
            int f = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t), te = mesh.GetTriEdges(t);
                int a = tv.a, b = tv.b, c = tv.c;
                Tid[f] = t; toF[t] = f; V[3 * f] = a; V[3 * f + 1] = b; V[3 * f + 2] = c; E[3 * f] = te.a; E[3 * f + 1] = te.b; E[3 * f + 2] = te.c;
                int gab = Ge(a, b, te.a), gac = Ge(a, c, te.c);
                Dir(a, b, te.a, out double i0x, out double i0y, out double f0x, out double f0y);
                Dir(b, c, te.b, out double i1x, out double i1y, out double f1x, out double f1y);
                Dir(c, a, te.c, out double i2x, out double i2y, out double f2x, out double f2y);
                Rot(gab, i1x, i1y, out i1x, out i1y); Rot(gab, f1x, f1y, out f1x, out f1y);
                Rot(gac, i2x, i2y, out i2x, out i2y); Rot(gac, f2x, f2y, out f2x, out f2y);
                Dx[3 * f] = (int)i0x; Dy[3 * f] = (int)i0y; Dx[3 * f + 1] = (int)i1x; Dy[3 * f + 1] = (int)i1y; Dx[3 * f + 2] = (int)i2x; Dy[3 * f + 2] = (int)i2y;
                Rx[3 * f] = f0x; Ry[3 * f] = f0y; Rx[3 * f + 1] = f1x; Ry[3 * f + 1] = f1y; Rx[3 * f + 2] = f2x; Ry[3 * f + 2] = f2y;
                Centroid[f] = (P[a] + P[b] + P[c]) / 3.0;
                double sp = spc == null ? s : s * (spc[a] + spc[b] + spc[c]) / 3.0;
                Cells[f] = mesh.GetTriArea(t) / (sp * sp); GeoCells += Cells[f];
                f++;
            }
            int GT(int ff, int u, int w)
            {
                if (u == w) return 0;
                int iu = V[3 * ff] == u ? 0 : V[3 * ff + 1] == u ? 1 : 2, iw = V[3 * ff] == w ? 0 : V[3 * ff + 1] == w ? 1 : 2;
                int sl = (iu + 1) % 3 == iw ? iu : iw;
                return Ge(u, w, E[3 * ff + sl]);
            }
            for (f = 0; f < NT; ++f)
                for (int sl = 0; sl < 3; ++sl) {
                    int e = E[3 * f + sl]; Index2i et = mesh.GetEdgeT(e);
                    if (et.b == DMesh3.InvalidID) { Closed = false; Nb[3 * f + sl] = -1; continue; }
                    int nb = toF[et.a] == f ? toF[et.b] : toF[et.a];
                    int ns = E[3 * nb] == e ? 0 : E[3 * nb + 1] == e ? 1 : 2;
                    int p = V[3 * f + sl], q = V[3 * f + (sl + 1) % 3];
                    Nb[3 * f + sl] = nb; NbS[3 * f + sl] = ns;
                    Rt[3 * f + sl] = (((GT(nb, V[3 * nb], q) - Ge(p, q, e) - GT(f, V[3 * f], p)) % 4) + 4) % 4;
                }
            if (!Closed) return;
            // self check of the reconstruction: the two sides of every edge agree, D_nb = -R^Rt D
            for (f = 0; f < NT; ++f)
                for (int sl = 0; sl < 3; ++sl) {
                    int k = 3 * f + sl, kn = 3 * Nb[k] + NbS[k];
                    Rot(Rt[k], Dx[k], Dy[k], out double ex, out double ey);
                    Rot(Rt[k], Rx[k], Ry[k], out double fx, out double fy);
                    if (Dx[kn] != -(int)ex || Dy[kn] != -(int)ey || Math.Abs(Rx[kn] + fx) > 1e-9 || Math.Abs(Ry[kn] + fy) > 1e-9) AntisymmetryViolations++;
                }
        }

        // ------------------------------------------------------------------------------------------------ the cohomology quantities

        public int DefectTriangles; public double DefectMass, CurlMass;       // sum |delta D|_1 (integer) and sum |delta R|_1 (real) over the triangles
        public double EnergyCurl, EnergyDefect;                                // filling energies w^T L^-1 w and b^T L^-1 b of the sheaf Laplacian
        public double CorrMax; public int HotEdges, Edges;                     // of the minimal correction of R: largest |c|inf, edges with |c|inf >= 0.5
        public int RelaxedFolds, RelaxedThin; public double RelaxedCells;      // of the relaxed (closed, real) map: det <= 0, det < 1/4 of the geometric area, total area
        public int GeoFolds;                                                   // triangles whose raw real offsets already have det <= 0
        public bool[] HotDefect, HotCorr, HotFold, HotThin;                    // per triangle
        public double SolveSeconds; public bool Solved;

        /// <summary>
        /// delta of a 1-cochain on a triangle is the sum of its three offsets. L = delta delta^T on the triangles is the connection
        /// Laplacian of the dual graph, 3 I on the diagonal and -R^Rt between neighbours (2 x 2 blocks). The minimal correction of the
        /// real offsets that closes every triangle is c = delta^T psi with L psi = delta R (the coexact part of R).
        /// </summary>
        public void Analyse()
        {
            var sw = Stopwatch.StartNew();
            int n = 2 * NT;
            var w = new double[n]; var b = new double[n];
            HotDefect = new bool[NT]; HotCorr = new bool[NT]; HotFold = new bool[NT]; HotThin = new bool[NT];
            for (int f = 0; f < NT; ++f) {
                int ix = Dx[3 * f] + Dx[3 * f + 1] + Dx[3 * f + 2], iy = Dy[3 * f] + Dy[3 * f + 1] + Dy[3 * f + 2];
                double fx = Rx[3 * f] + Rx[3 * f + 1] + Rx[3 * f + 2], fy = Ry[3 * f] + Ry[3 * f + 1] + Ry[3 * f + 2];
                b[2 * f] = ix; b[2 * f + 1] = iy; w[2 * f] = fx; w[2 * f + 1] = fy;
                if (ix != 0 || iy != 0) { DefectTriangles++; HotDefect[f] = true; }
                DefectMass += Math.Abs(ix) + Math.Abs(iy); CurlMass += Math.Abs(fx) + Math.Abs(fy);
                if (Rx[3 * f] * Ry[3 * f + 1] - Ry[3 * f] * Rx[3 * f + 1] <= 0) GeoFolds++;
            }
            // a flat bundle without holonomy (the aligned torus) has the two constant sections in the kernel: the shift makes L definite
            var ch = new SparseCholesky(n, 2);
            for (int f = 0; f < NT; ++f) {
                ch.Add(2 * f, 2 * f, 3 + 1e-9); ch.Add(2 * f + 1, 2 * f + 1, 3 + 1e-9);
                for (int sl = 0; sl < 3; ++sl) {
                    int nb = Nb[3 * f + sl]; if (nb <= f) continue;
                    // block (nb, f) = -R^Rt
                    Rot(Rt[3 * f + sl], 1, 0, out double c0x, out double c0y); Rot(Rt[3 * f + sl], 0, 1, out double c1x, out double c1y);
                    if (c0x != 0) ch.Add(2 * nb, 2 * f, -c0x); if (c0y != 0) ch.Add(2 * nb + 1, 2 * f, -c0y);
                    if (c1x != 0) ch.Add(2 * nb, 2 * f + 1, -c1x); if (c1y != 0) ch.Add(2 * nb + 1, 2 * f + 1, -c1y);
                }
            }
            Solved = ch.Factorize();
            if (!Solved) { SolveSeconds = sw.Elapsed.TotalSeconds; return; }
            var psi = new double[n]; var psiB = new double[n];
            ch.Solve(w, psi); ch.Solve(b, psiB);
            for (int i = 0; i < n; ++i) { EnergyCurl += w[i] * psi[i]; EnergyDefect += b[i] * psiB[i]; }
            var cx = new double[3 * NT]; var cy = new double[3 * NT];
            for (int f = 0; f < NT; ++f)
                for (int sl = 0; sl < 3; ++sl) {
                    int k = 3 * f + sl, nb = Nb[k];
                    Rot(-Rt[k], psi[2 * nb], psi[2 * nb + 1], out double px, out double py);
                    cx[k] = psi[2 * f] - px; cy[k] = psi[2 * f + 1] - py;
                    double inf = Math.Max(Math.Abs(cx[k]), Math.Abs(cy[k]));
                    if (nb > f) { Edges++; if (inf >= 0.5) HotEdges++; }
                    if (inf > CorrMax) CorrMax = inf;
                    if (inf >= 0.5) HotCorr[f] = true;
                }
            for (int f = 0; f < NT; ++f) {
                double ax = Rx[3 * f] - cx[3 * f], ay = Ry[3 * f] - cy[3 * f], bx = Rx[3 * f + 1] - cx[3 * f + 1], by = Ry[3 * f + 1] - cy[3 * f + 1];
                double det = ax * by - ay * bx;
                RelaxedCells += 0.5 * det;
                if (det <= 0) { RelaxedFolds++; HotFold[f] = true; }
                if (det < 0.25 * 2 * Cells[f]) { RelaxedThin++; HotThin[f] = true; }
            }
            SolveSeconds = sw.Elapsed.TotalSeconds;
        }
    }

    // ---------------------------------------------------------------------------------------------------- the map of the library

    /// <summary>
    /// the final offsets of QuadFlowRepair (after subdivision, greedy moves and SAT), by reflection: the lattice area of the map,
    /// (1/2) sum det, and the centroids of the triangles with det &lt; 0. The sum is the same right after the flow: the subdivision moves
    /// area between the two sides of a split edge, a vertex move and a SAT region change the map inside a fixed boundary.
    /// </summary>
    static bool LibraryMap(QuadRemesher q, out double cells, out int folds, List<Vector3d> foldCentroids)
    {
        cells = double.NaN; folds = 0;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object rep = typeof(QuadRemesher).GetField("PureQuadRepair", any)?.GetValue(q);
        if (rep == null) return false;
        var offsets = rep.GetType().GetMethod("Offsets", any)?.Invoke(rep, null) as int[];
        if (offsets == null || offsets.Length == 0) return false;
        var tris = rep.GetType().GetField("tris", any)?.GetValue(rep) as IList;
        var vp = rep.GetType().GetField("VP", any)?.GetValue(rep) as List<Vector3d>;
        FieldInfo vField = tris != null && tris.Count > 0 ? tris[0].GetType().GetField("V", any) : null;
        long sum = 0;
        for (int f = 0; 6 * f < offsets.Length; ++f) {
            long det = (long)offsets[6 * f] * offsets[6 * f + 3] - (long)offsets[6 * f + 1] * offsets[6 * f + 2];
            sum += det;
            if (det >= 0) continue;
            folds++;
            if (vField != null && vp != null && vp.Count > 0) {
                var v = (int[])vField.GetValue(tris[f]);
                foldCentroids.Add((vp[v[0]] + vp[v[1]] + vp[v[2]]) / 3.0);
            }
        }
        cells = 0.5 * sum;
        return true;
    }

    // ---------------------------------------------------------------------------------------------------- statistics

    /// <summary> the probability that a fallback row has a larger value than a clean row (ties count half): 0.5 is chance, 1 a perfect ranking </summary>
    static double Auc(List<double> fallback, List<double> clean)
    {
        if (fallback.Count == 0 || clean.Count == 0) return double.NaN;
        double sum = 0;
        foreach (double a in fallback) foreach (double b in clean) sum += a > b ? 1 : a == b ? 0.5 : 0;
        return sum / ((double)fallback.Count * clean.Count);
    }

    /// <summary> the largest number of rows that one threshold on the value classifies correctly (in sample) </summary>
    static int BestThreshold(List<double> fallback, List<double> clean)
    {
        var cuts = fallback.Concat(clean).Distinct().OrderBy(x => x).ToList();
        int best = Math.Max(fallback.Count, clean.Count);
        foreach (double c in cuts) {
            int hi = fallback.Count(x => x >= c) + clean.Count(x => x < c), lo = fallback.Count(x => x < c) + clean.Count(x => x >= c);
            best = Math.Max(best, Math.Max(hi, lo));
        }
        return best;
    }

    sealed class Grid
    {
        readonly Dictionary<(int, int, int), List<Vector3d>> cells = new Dictionary<(int, int, int), List<Vector3d>>(); readonly double h;
        public Grid(double radius) { h = radius; }
        (int, int, int) Key(Vector3d p) { return ((int)Math.Floor(p.x / h), (int)Math.Floor(p.y / h), (int)Math.Floor(p.z / h)); }
        public void Add(Vector3d p) { var k = Key(p); if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<Vector3d>(); l.Add(p); }
        public bool Near(Vector3d p)
        {
            var (kx, ky, kz) = Key(p);
            for (int dx = -1; dx <= 1; ++dx) for (int dy = -1; dy <= 1; ++dy) for (int dz = -1; dz <= 1; ++dz)
                if (cells.TryGetValue((kx + dx, ky + dy, kz + dz), out var l)) foreach (var q in l) if (q.DistanceSquared(p) <= h * h) return true;
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------------------- main

    static List<(string name, Func<Vector3d, double> fn, double s0, double align, double noise)> Shapes()
    {
        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        Func<Vector3d, double> capsule = p => { double x = Math.Max(-0.6, Math.Min(0.6, p.x)); return new Vector3d(p.x - x, p.y, p.z).Length - 0.5; };
        Func<Vector3d, double> genus2 = p => {
            Func<double, double> tor = cx => { double q = Math.Sqrt((p.x - cx) * (p.x - cx) + p.y * p.y) - 0.6; return Math.Sqrt(q * q + p.z * p.z) - 0.22; };
            double a = tor(-0.62), b = tor(0.62), k = 0.1, h = Math.Max(k - Math.Abs(a - b), 0) / k;
            return Math.Min(a, b) - h * h * k * 0.25;
        };
        Func<Vector3d, double> cylinder = p => { double dx = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.6, dz = Math.Abs(p.z) - 0.8; return Math.Min(Math.Max(dx, dz), 0) + new Vector2d(Math.Max(dx, 0), Math.Max(dz, 0)).Length; };
        return new List<(string, Func<Vector3d, double>, double, double, double)> {
            ("sphere", sphere, 0.1, 0.0, 0.0), ("torus", torus, 0.1, 0.0, 0.0), ("torus-align10", torus, 0.1, 10.0, 0.0),
            ("bumpy", bumpy, 0.06, 0.0, 0.0), ("filleted-box", rbox, 0.08, 0.0, 0.0), ("noisy-sphere", sphere, 0.1, 0.0, 0.2),
            ("capsule", capsule, 0.08, 0.0, 0.0), ("genus2", genus2, 0.06, 0.0, 0.0), ("capped-cylinder", cylinder, 0.08, 0.0, 0.0),
        };
    }
    static readonly (double scale, double ratio)[] Variations = { (1, 0.5), (0.8, 0.5), (1.25, 0.5), (1.5, 0.5), (1, 0.35), (1, 0.7) };

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double radius = A.ContainsKey("radius") ? double.Parse(A["radius"], CultureInfo.InvariantCulture) : 1.5;
        { var w = MakeInput(p => p.Length - 1, 0.1, 0); var wc = new CrossField(w); wc.Compute(); new QuadRemesher(w) { Field = wc, TargetEdgeLength = 0.2, PureQuads = true }.Compute(); }
        if (A.ContainsKey("levers") && A["levers"] == "1") return Levers(only);
        return MainPass(only, radius);
    }

    static int MainPass(Regex only, double radius)
    {
        Console.WriteLine("| shape | scale / ratio | tris | sing. | status | folds (library) | cells: geometric | library map / geometric | relaxed map / geometric | defect triangles | defect mass | curl mass | energy (curl) | energy (defect) | max corr. | hot edges % | relaxed folds | relaxed thin % | t pure (s) | t sheaf (s) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        string[] names = { "defect mass / cell", "curl mass / cell", "energy (curl) / cell", "energy (defect) / cell", "max correction", "hot edges share", "relaxed folds share", "relaxed thin share", "1 - relaxed area ratio", "singular faces", "triangles", "triangles per cell (input resolution)" };
        var fb = names.Select(_ => new List<double>()).ToArray(); var cl = names.Select(_ => new List<double>()).ToArray();
        var hotNames = new[] { "singular faces", "defect triangles", "correction >= 0.5", "relaxed folds", "relaxed thin" };
        var hotHit = new double[hotNames.Length]; var hotCover = new double[hotNames.Length]; var hotRows = new int[hotNames.Length]; long foldTotal = 0;
        var perShapeHit = new SortedDictionary<string, double[]>(); var perShapeCover = new SortedDictionary<string, double[]>(); var perShapeFolds = new SortedDictionary<string, long>();
        int total = 0, clean = 0, fallback = 0, viol = 0; double tFallbackAttempt = 0, tFallbackStage2 = 0, tSheaf = 0, tCleanAttempt = 0;
        var libRatioClean = new List<double>(); var libRatioFb = new List<double>();
        var statusCount = new SortedDictionary<string, int>();
        foreach (var (name, fn, s0, align, noise) in Shapes()) {
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in Variations) {
                double s = s0 * scale;
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
                if (!cf.Success) { Console.WriteLine($"| {name} | {scale}/{ratio} | cross field failed {cf.Status} |"); continue; }
                var q2 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s };
                var sw = Stopwatch.StartNew(); q2.Compute(); double t2 = sw.Elapsed.TotalSeconds;
                var q3 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true };
                sw.Restart(); q3.Compute(); double t3 = sw.Elapsed.TotalSeconds;
                bool isClean = IsClean(q3, input);
                total++; if (isClean) { clean++; tCleanAttempt += t3; } else { fallback++; tFallbackAttempt += t3; tFallbackStage2 += t2; }
                statusCount.TryGetValue(q3.PureQuadStatus.ToString(), out int sc); statusCount[q3.PureQuadStatus.ToString()] = sc + 1;
                sw.Restart();
                var sh = new Sheaf(input, cf, q3.Positions, s);
                if (!sh.Ok || !sh.Closed) { Console.WriteLine($"| {name} | {scale}/{ratio} | no sheaf complex (open mesh or no position) |"); continue; }
                sh.Analyse(); double ts = sw.Elapsed.TotalSeconds; tSheaf += ts; viol += sh.AntisymmetryViolations;
                var foldPts = new List<Vector3d>();
                bool hasMap = LibraryMap(q3, out double libCells, out int libFolds, foldPts);
                double libRatio = hasMap ? libCells / sh.GeoCells : double.NaN;
                if (hasMap) (isClean ? libRatioClean : libRatioFb).Add(libRatio);
                double g = sh.GeoCells;
                double[] feat = { sh.DefectMass / g, sh.CurlMass / g, sh.EnergyCurl / g, sh.EnergyDefect / g, sh.CorrMax, (double)sh.HotEdges / sh.Edges, (double)sh.RelaxedFolds / sh.NT, (double)sh.RelaxedThin / sh.NT, 1 - sh.RelaxedCells / g, cf.SingularFaces.Count, sh.NT, sh.NT / g };
                for (int i = 0; i < feat.Length; ++i) (isClean ? cl[i] : fb[i]).Add(feat[i]);
                Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {sh.NT} | {cf.SingularFaces.Count} | {q3.PureQuadStatus}{(isClean ? "" : q3.PureQuadStatus == PureQuadStatus.Success ? " (not clean)" : "")} | {(hasMap ? libFolds.ToString() : "-")} | {F(g, "F0")} | {F(libRatio, "F3")} | {F(sh.RelaxedCells / g, "F3")} | {sh.DefectTriangles} | {F(sh.DefectMass, "F0")} | {F(sh.CurlMass, "F1")} | {F(sh.EnergyCurl, "F1")} | {F(sh.EnergyDefect, "F1")} | {F(sh.CorrMax, "F2")} | {F(100.0 * sh.HotEdges / sh.Edges, "F2")} | {sh.RelaxedFolds} | {F(100.0 * sh.RelaxedThin / sh.NT, "F2")} | {F(t3, "F2")} | {F(ts, "F2")} |");
                // where: the folds of the library's map against the places the sheaf quantities mark, within radius x s
                if (foldPts.Count > 0) {
                    var sing = new bool[sh.NT]; var singSet = new HashSet<int>(cf.SingularFaces); for (int f = 0; f < sh.NT; ++f) sing[f] = singSet.Contains(sh.Tid[f]);
                    var sets = new[] { sing, sh.HotDefect, sh.HotCorr, sh.HotFold, sh.HotThin };
                    foldTotal += foldPts.Count;
                    if (!perShapeHit.ContainsKey(name)) { perShapeHit[name] = new double[sets.Length]; perShapeCover[name] = new double[sets.Length]; perShapeFolds[name] = 0; }
                    perShapeFolds[name] += foldPts.Count;
                    for (int k = 0; k < sets.Length; ++k) {
                        var grid = new Grid(radius * s); int members = 0;
                        for (int f = 0; f < sh.NT; ++f) if (sets[k][f]) { grid.Add(sh.Centroid[f]); members++; }
                        int hit = foldPts.Count(p => grid.Near(p));
                        double cover = 0; for (int f = 0; f < sh.NT; ++f) if (grid.Near(sh.Centroid[f])) cover += sh.Cells[f];
                        cover /= g;
                        hotHit[k] += hit; hotCover[k] += cover * foldPts.Count; hotRows[k]++;
                        perShapeHit[name][k] += hit; perShapeCover[name][k] += cover * foldPts.Count;
                    }
                }
            }
        }
        Console.WriteLine();
        Console.WriteLine($"runs {total}: clean {clean}, fallback {fallback}; status counts: {string.Join(", ", statusCount.Select(kv => kv.Key + " " + kv.Value))}; antisymmetry violations of the rebuilt complex: {viol}");
        Console.WriteLine($"time: pure-quad attempts of the fallback rows {F(tFallbackAttempt, "F2")} s (their stage 2 alone {F(tFallbackStage2, "F2")} s), of the clean rows {F(tCleanAttempt, "F2")} s; the sheaf analysis of all rows {F(tSheaf, "F2")} s");
        double Med(List<double> l) { if (l.Count == 0) return double.NaN; var c = l.OrderBy(x => x).ToList(); return c[c.Count / 2]; }
        Console.WriteLine($"library map area / geometric area: clean rows min {F(libRatioClean.DefaultIfEmpty(double.NaN).Min(), "F3")} median {F(Med(libRatioClean), "F3")} max {F(libRatioClean.DefaultIfEmpty(double.NaN).Max(), "F3")}; fallback rows min {F(libRatioFb.DefaultIfEmpty(double.NaN).Min(), "F3")} median {F(Med(libRatioFb), "F3")} max {F(libRatioFb.DefaultIfEmpty(double.NaN).Max(), "F3")}");
        Console.WriteLine();
        Console.WriteLine("## Ranking power for 'fallback' (AUC: 0.5 chance, 1 perfect; in sample)");
        Console.WriteLine("| quantity (before the flow) | AUC | rows right with the best threshold | median clean | median fallback |");
        Console.WriteLine("|---|---|---|---|---|");
        for (int i = 0; i < names.Length; ++i)
            Console.WriteLine($"| {names[i]} | {F(Auc(fb[i], cl[i]), "F2")} | {BestThreshold(fb[i], cl[i])} / {fb[i].Count + cl[i].Count} | {F(Med(cl[i]), "G3")} | {F(Med(fb[i]), "G3")} |");
        Console.WriteLine($"| (always the majority class) | 0.50 | {Math.Max(clean, fallback)} / {total} | | |");
        Console.WriteLine();
        Console.WriteLine($"## Where the folds of the library's final map lie ({foldTotal} fold triangles of the fallback rows; radius {F(radius, "G3")} s)");
        Console.WriteLine("| marked set (input triangles) | folds within the radius of the set | share of the surface within the radius (weighted by the folds of the row) | enrichment |");
        Console.WriteLine("|---|---|---|---|");
        for (int k = 0; k < hotNames.Length; ++k)
            Console.WriteLine($"| {hotNames[k]} | {F(100 * hotHit[k] / Math.Max(1, foldTotal), "F1")} % | {F(100 * hotCover[k] / Math.Max(1, foldTotal), "F1")} % | {F(hotHit[k] / Math.Max(1e-12, hotCover[k]), "F2")} |");
        Console.WriteLine();
        Console.WriteLine("per shape (folds; then per set: folds within the radius % / surface within the radius %):");
        foreach (var kv in perShapeHit)
            Console.WriteLine($"- {kv.Key}: {perShapeFolds[kv.Key]} folds; " + string.Join("; ", Enumerable.Range(0, hotNames.Length).Select(k => $"{hotNames[k]} {F(100 * kv.Value[k] / perShapeFolds[kv.Key], "F0")} / {F(100 * perShapeCover[kv.Key][k] / perShapeFolds[kv.Key], "F0")}")));
        return viol == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------------------- the levers that exist

    static int Levers(Regex only)
    {
        // which SizeAdaptation modes the library accepts together with PureQuads (master of 2026-11-22: none; with PR #1293: all)
        var modes = new List<SizeAdaptationMode> { SizeAdaptationMode.Off };
        {
            var w = MakeInput(p => p.Length - 1, 0.1, 0); var wc = new CrossField(w); wc.Compute();
            foreach (var m in new[] { SizeAdaptationMode.Scale, SizeAdaptationMode.ScaleAndRotation }) {
                var q = new QuadRemesher(w) { Field = wc, TargetEdgeLength = 0.2, PureQuads = true, SizeAdaptation = m }; q.Compute();
                if (q.Status == QuadRemeshStatus.Success) modes.Add(m);
            }
        }
        var aligns = new[] { 0.0, 10.0 };
        var configs = new List<(double align, SizeAdaptationMode mode)>();
        foreach (var m in modes) foreach (double a in aligns) configs.Add((a, m));
        Console.WriteLine("configurations: " + string.Join(", ", configs.Select((c, i) => $"{i}: align {c.align} adapt {c.mode}")));
        Console.WriteLine("| shape | scale / ratio | " + string.Join(" | ", configs.Select(c => $"align {c.align} {c.mode}")) + " | clean with any | curl mass / cell (align 0, Off) |");
        Console.WriteLine("|---|---|" + string.Join("", configs.Select(_ => "---|")) + "---|---|");
        var cleanPer = new int[configs.Count]; var secPer = new double[configs.Count]; int inputs = 0, any = 0;
        var perShapeAny = new SortedDictionary<string, int>(); var rows = new List<(bool[] ok, double curl)>();
        foreach (var (name, fn, s0, align0, noise) in Shapes()) {
            if (align0 != 0) continue;                        // torus-align10 is the torus with the lever on
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in Variations) {
                double s = s0 * scale;
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var fields = new Dictionary<double, CrossField>(); var fieldSec = new Dictionary<double, double>();
                foreach (double a in aligns) { var sw0 = Stopwatch.StartNew(); var cf = new CrossField(input) { CurvatureAlignment = a }; cf.Compute(); fields[a] = cf; fieldSec[a] = sw0.Elapsed.TotalSeconds; }
                var ok = new bool[configs.Count]; var cells = new string[configs.Count]; double curl = double.NaN;
                for (int i = 0; i < configs.Count; ++i) {
                    var cf = fields[configs[i].align];
                    if (!cf.Success) { cells[i] = "field failed"; continue; }
                    var q = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true, SizeAdaptation = configs[i].mode };
                    var sw = Stopwatch.StartNew(); q.Compute(); double t = sw.Elapsed.TotalSeconds;
                    ok[i] = IsClean(q, input); secPer[i] += t + fieldSec[configs[i].align];
                    if (ok[i]) cleanPer[i]++;
                    PolyMeshMetrics m = ok[i] ? PolyMeshMetrics.Compute(q.Result) : null;
                    cells[i] = ok[i] ? $"clean (irr. {m.IrregularVertices}, p95 {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), "F1")})" : q.PureQuadStatus.ToString();
                    if (i == 0 && q.Positions != null) { var sh = new Sheaf(input, cf, q.Positions, s); if (sh.Ok && sh.Closed) { sh.Analyse(); curl = sh.CurlMass / sh.GeoCells; } }
                }
                inputs++; bool anyOk = ok.Any(x => x); if (anyOk) { any++; perShapeAny.TryGetValue(name, out int c); perShapeAny[name] = c + 1; }
                rows.Add((ok, curl));
                Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {string.Join(" | ", cells)} | {(anyOk ? "yes" : "no")} | {F(curl, "F4")} |");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"inputs {inputs}; clean per configuration: " + string.Join(", ", configs.Select((c, i) => $"[align {c.align} {c.mode}] {cleanPer[i]} ({F(secPer[i], "F1")} s incl. field)")));
        Console.WriteLine($"oracle (clean with at least one configuration): {any} / {inputs}; per shape: " + string.Join(", ", perShapeAny.Select(kv => kv.Key + " " + kv.Value)));
        // the best pair "default, and the second configuration where a threshold on the curl mass says so" (in sample)
        int bestPair = 0; string bestPairText = "";
        for (int i = 0; i < configs.Count; ++i)
            for (int j = 0; j < configs.Count; ++j) {
                if (i == j) continue;
                foreach (double cut in rows.Select(r => r.curl).Where(x => !double.IsNaN(x)).Distinct().Append(double.PositiveInfinity)) {
                    int n = rows.Count(r => (r.curl >= cut ? r.ok[j] : r.ok[i]));
                    if (n > bestPair) { bestPair = n; bestPairText = $"[{i}] below, [{j}] from curl mass / cell {F(cut, "F4")}"; }
                }
            }
        Console.WriteLine($"best one-threshold switch between two configurations on the curl mass (in sample): {bestPair} / {inputs} ({bestPairText})");
        // the first-clean order: try configurations in a fixed order until one is clean
        var order = Enumerable.Range(0, configs.Count).OrderByDescending(i => cleanPer[i]).ToList();
        var covered = new bool[rows.Count]; var steps = new List<string>(); int cum = 0;
        foreach (int i in order) { int add = 0; for (int r = 0; r < rows.Count; ++r) if (!covered[r] && rows[r].ok[i]) { covered[r] = true; add++; } cum += add; steps.Add($"[{i}] +{add} = {cum}"); }
        Console.WriteLine("trying the configurations one after the other (most successful first): " + string.Join(", ", steps));
        return 0;
    }
}
