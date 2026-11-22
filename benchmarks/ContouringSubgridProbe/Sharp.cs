// Part "sharp": what can DualContouring recover from a discrete grid of exact signed distances?
// All variants contour the same sign grid (the trilinear interpolant of the samples decides the crossing edges), so they
// have the same connectivity; only the Hermite data differs:
//   interp : interpolated crossing, gradient of the trilinear interpolant, eigen tolerance 0.1 (the grid-only state before 2026-11)
//   trilin : the defaults (ExactHermite, tolerance 0.02) on the trilinear interpolant (the grid-only state today)
//   plane  : grid-only prototype, one-sided plane fits chosen by the empty-sphere rule (see PlaneHermite)
//   oracle : exact crossing (bisection on the true distance) and the normal of the nearest input triangle: the upper bound
//   oracle-x / oracle-n : only the crossing / only the normal exact, the other one as in interp (which half carries the gain)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

static class SharpProbe
{
    sealed class SdfGrid : ImplicitFunction3d
    {
        public double[] V; public int NX, NY, NZ; public Vector3d Origin; public double H;
        public double At(int i, int j, int k) { return V[i + NX * (j + NY * k)]; }
        public bool Has(int i, int j, int k) { return i >= 0 && j >= 0 && k >= 0 && i < NX && j < NY && k < NZ; }
        public Vector3d Pos(int i, int j, int k) { return new Vector3d(Origin.x + H * i, Origin.y + H * j, Origin.z + H * k); }
        public double Value(ref Vector3d p)
        {
            double fx = (p.x - Origin.x) / H, fy = (p.y - Origin.y) / H, fz = (p.z - Origin.z) / H;
            int i = Math.Max(0, Math.Min(NX - 2, (int)Math.Floor(fx))), j = Math.Max(0, Math.Min(NY - 2, (int)Math.Floor(fy))), k = Math.Max(0, Math.Min(NZ - 2, (int)Math.Floor(fz)));
            double a = fx - i, b = fy - j, c = fz - k;
            double v00 = At(i, j, k) * (1 - a) + At(i + 1, j, k) * a, v10 = At(i, j + 1, k) * (1 - a) + At(i + 1, j + 1, k) * a;
            double v01 = At(i, j, k + 1) * (1 - a) + At(i + 1, j, k + 1) * a, v11 = At(i, j + 1, k + 1) * (1 - a) + At(i + 1, j + 1, k + 1) * a;
            return (v00 * (1 - b) + v10 * b) * (1 - c) + (v01 * (1 - b) + v11 * b) * c;
        }
        public Vector3d Gradient(Vector3d p)
        {
            double e = H / 4;
            Vector3d px = new Vector3d(p.x + e, p.y, p.z), mx = new Vector3d(p.x - e, p.y, p.z), py = new Vector3d(p.x, p.y + e, p.z), my = new Vector3d(p.x, p.y - e, p.z),
                pz = new Vector3d(p.x, p.y, p.z + e), mz = new Vector3d(p.x, p.y, p.z - e);
            return new Vector3d(Value(ref px) - Value(ref mx), Value(ref py) - Value(ref my), Value(ref pz) - Value(ref mz)) / (2 * e);
        }
    }

    public static void Run(int seeds, int[] resolutions)
    {
        Console.WriteLine("# sharp: DualContouring from a discrete grid of exact signed distances; distances in cells");
        Console.WriteLine("shape, res, seed, variant, triangles, clean, featMean, featMax, cornerMax, edgeChamfer, hausdorff, ms");
        string[] variants = { "interp", "trilin", "plane", "oracle-x", "oracle-n", "oracle" };
        var sum = new Dictionary<string, double[]>();
        foreach (string name in Procedural.Names)
            foreach (int res in resolutions)
                for (int seed = 0; seed < seeds; ++seed) {
                    DMesh3 truth = Procedural.Make(name);
                    Vector3d c = truth.CachedBounds.Center;
                    Matrix3d R = Program.Rotation(seed);
                    for (int vid = 0; vid < truth.MaxVertexID; ++vid) if (truth.IsVertex(vid)) truth.SetVertex(vid, R * (truth.GetVertex(vid) - c));
                    var tree = new DMeshAABBTree3(truth, true);
                    tree.WindingNumber(Vector3d.Zero);
                    Func<Vector3d, double> sd = p => {
                        double d2; tree.FindNearestTriangle(p, out d2);
                        return Math.Sqrt(d2) * (tree.WindingNumber(p) > 0.5 ? -1 : 1);
                    };
                    AxisAlignedBox3d b = truth.CachedBounds;
                    double h = b.MaxDim / res;
                    Vector3d org = b.Min - h * (2 * Vector3d.One + Program.Shift(seed));
                    var bounds = new AxisAlignedBox3d(org, b.Max + 3 * h * Vector3d.One);
                    var grid = new SdfGrid { Origin = org, H = h, NX = (int)Math.Ceiling(bounds.Width / h) + 2, NY = (int)Math.Ceiling(bounds.Height / h) + 2, NZ = (int)Math.Ceiling(bounds.Depth / h) + 2 };
                    grid.V = new double[grid.NX * grid.NY * grid.NZ];
                    gParallel.ForEach(Interval1i.Range(grid.NZ), k => {
                        for (int j = 0; j < grid.NY; ++j) for (int i = 0; i < grid.NX; ++i) grid.V[i + grid.NX * (j + grid.NY * k)] = sd(grid.Pos(i, j, k));
                    });
                    List<Vector3d> featurePts, corners;
                    FeatureSamples(truth, h / 4, out featurePts, out corners);

                    foreach (string variant in variants) {
                        var dc = new DualContouring { Implicit = grid, InsideIsLess = true, Bounds = bounds, CellSize = h, Simplify = false };
                        if (variant == "interp") { dc.ExactHermite = false; dc.QEFEigenTolerance = 0.1; }
                        else if (variant == "plane") dc.HermiteF = (Vector3d a, Vector3d bb, Vector3d x, out Vector3d p, out Vector3d n) => PlaneHermite(grid, a, bb, x, out p, out n);
                        else if (variant.StartsWith("oracle")) dc.HermiteF = (Vector3d a, Vector3d bb, Vector3d x, out Vector3d p, out Vector3d n) => {
                            Vector3d lo = a, hi = bb;      // a is inside
                            for (int it = 0; it < 50; ++it) { Vector3d mid = 0.5 * (lo + hi); if (sd(mid) < 0) lo = mid; else hi = mid; }
                            Vector3d exact = 0.5 * (lo + hi);
                            p = variant == "oracle-n" ? x : exact;
                            n = variant == "oracle-x" ? grid.Gradient(exact).Normalized : truth.GetTriNormal(tree.FindNearestTriangle(exact));
                            return true;
                        };
                        var sw = Stopwatch.StartNew();
                        dc.Generate();
                        double ms = sw.Elapsed.TotalMilliseconds;
                        DMesh3 m = dc.Mesh;
                        string clean = MeshRobustRepair.NotCleanReason(m);
                        var mtree = new DMeshAABBTree3(m, true);
                        double fsum = 0, fmax = 0, cmax = 0;
                        foreach (Vector3d p in featurePts) { double d2; mtree.FindNearestTriangle(p, out d2); double d = Math.Sqrt(d2) / h; fsum += d; fmax = Math.Max(fmax, d); }
                        foreach (Vector3d p in corners) { double d2; mtree.FindNearestTriangle(p, out d2); cmax = Math.Max(cmax, Math.Sqrt(d2) / h); }
                        var fid = new MeshFidelityMetrics(truth, m) { Curvature = false, VolumeSamples = 0, OuterSurface = false, SurfaceSamples = 20000 };
                        fid.Compute();
                        double toCells = fid.Size / h;
                        double haus = Math.Max(fid.MaxDistanceMeshToReference, fid.MaxDistanceReferenceToMesh) * toCells;
                        double[] row = { fsum / featurePts.Count, fmax, cmax, fid.EdgeChamfer * toCells, haus, ms, clean == "" ? 1 : 0, 1 };
                        Console.WriteLine(string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6:F4}, {7:F4}, {8:F4}, {9:F4}, {10:F4}, {11:F0}",
                            name, res, seed, variant, m.TriangleCount, clean == "" ? "yes" : clean, row[0], row[1], row[2], row[3], row[4], ms));
                        string key = res + ", " + variant;
                        double[] acc;
                        if (!sum.TryGetValue(key, out acc)) sum[key] = acc = new double[row.Length];
                        for (int q = 0; q < row.Length; ++q) acc[q] += row[q];
                    }
                }
        Console.WriteLine("# sharp, means over shapes and seeds");
        Console.WriteLine("res, variant, runs, clean, featMean, featMax, cornerMax, edgeChamfer, hausdorff, ms");
        foreach (var kv in sum) {
            double[] a = kv.Value; double n = a[7];
            Console.WriteLine(string.Format("{0}, {1}, {2}, {3:F4}, {4:F4}, {5:F4}, {6:F4}, {7:F4}, {8:F0}", kv.Key, n, a[6], a[0] / n, a[1] / n, a[2] / n, a[3] / n, a[4] / n, a[5] / n));
        }
    }

    // points on the edges of the truth with a dihedral angle above 30 degrees (spacing step), and the vertices where three or more meet
    static void FeatureSamples(DMesh3 mesh, double step, out List<Vector3d> pts, out List<Vector3d> corners)
    {
        pts = new List<Vector3d>(); corners = new List<Vector3d>();
        var count = new Dictionary<int, int>();
        foreach (int eid in mesh.EdgeIndices()) {
            Index2i t = mesh.GetEdgeT(eid);
            if (t.b < 0 || mesh.GetTriNormal(t.a).Dot(mesh.GetTriNormal(t.b)) > Math.Cos(30 * Math.PI / 180)) continue;
            Index2i v = mesh.GetEdgeV(eid);
            Vector3d a = mesh.GetVertex(v.a), b = mesh.GetVertex(v.b);
            int n = Math.Max(1, (int)Math.Ceiling(a.Distance(b) / step));
            for (int i = 0; i <= n; ++i) pts.Add(Vector3d.Lerp(a, b, (double)i / n));
            int cnt;
            count[v.a] = count.TryGetValue(v.a, out cnt) ? cnt + 1 : 1;
            count[v.b] = count.TryGetValue(v.b, out cnt) ? cnt + 1 : 1;
        }
        foreach (var kv in count) if (kv.Value >= 3) corners.Add(mesh.GetVertex(kv.Key));
    }

    // Grid-only Hermite data, local and without iteration. A sample (u, s) of an exact distance field says that the ball of
    // radius |s| around u is empty and touches the surface (the constraint of Kohlbrenner and Alexa 2025 and of Carrera et
    // al. 2026). Candidates: the planes s0 + g . (x - p0) of the one-sided differences g at both ends of the edge and their axis neighbours (8 octants
    // each; exact where the four nodes see one face), and the interpolated datum. A candidate must cross the edge. Score: how far
    // the plane enters the balls of the two ends and their axis neighbours, plus the eikonal defect (| |g| - 1 |).
    static readonly int[] Sgn = { -1, 1 };
    static bool PlaneHermite(SdfGrid g, Vector3d a, Vector3d b, Vector3d crossing, out Vector3d point, out Vector3d normal)
    {
        double h = g.H;
        var ends = new Vector3i[2];
        ends[0] = new Vector3i((int)Math.Round((a.x - g.Origin.x) / h), (int)Math.Round((a.y - g.Origin.y) / h), (int)Math.Round((a.z - g.Origin.z) / h));
        ends[1] = new Vector3i((int)Math.Round((b.x - g.Origin.x) / h), (int)Math.Round((b.y - g.Origin.y) / h), (int)Math.Round((b.z - g.Origin.z) / h));
        var ballC = new List<Vector3d>(14); var ballR = new List<double>(14);
        var stencilNodes = new List<Vector3i>(14);
        for (int e = 0; e < 2; ++e) {
            Vector3i q = ends[e];
            stencilNodes.Add(q);
            ballC.Add(g.Pos(q.x, q.y, q.z)); ballR.Add(Math.Abs(g.At(q.x, q.y, q.z)));
            for (int ax = 0; ax < 3; ++ax) foreach (int s in Sgn) {
                Vector3i r = q; r[ax] += s;
                if (!g.Has(r.x, r.y, r.z)) continue;
                stencilNodes.Add(r);
                ballC.Add(g.Pos(r.x, r.y, r.z)); ballR.Add(Math.Abs(g.At(r.x, r.y, r.z)));
            }
        }
        Vector3d gi = g.Gradient(crossing);
        int secondEnd = ballC.IndexOf(g.Pos(ends[1].x, ends[1].y, ends[1].z));
        double best = double.MaxValue; point = crossing; normal = gi.Normalized;
        if (gi.Length > 0.25) best = Score(crossing, normal, Math.Abs(gi.Length - 1), ballC, ballR, h, secondEnd);
        foreach (Vector3i q in stencilNodes) {
            Vector3d p0 = g.Pos(q.x, q.y, q.z); double s0 = g.At(q.x, q.y, q.z);
            foreach (int sx in Sgn) foreach (int sy in Sgn) foreach (int sz in Sgn) {
                if (!g.Has(q.x + sx, q.y + sy, q.z + sz)) continue;
                Vector3d grad = new Vector3d((g.At(q.x + sx, q.y, q.z) - s0) * sx, (g.At(q.x, q.y + sy, q.z) - s0) * sy, (g.At(q.x, q.y, q.z + sz) - s0) * sz) / h;
                double len = grad.Length;
                if (len < 0.5 || len > 1.5) continue;
                double fa = s0 + grad.Dot(a - p0), fb = s0 + grad.Dot(b - p0);
                if (!(fa < 0 && fb > 0)) continue;                       // the plane must cross the edge, inside to outside
                Vector3d x = a + (b - a) * (fa / (fa - fb));
                Vector3d n = grad / len;
                double sc = Score(x, n, Math.Abs(len - 1), ballC, ballR, h, secondEnd);
                if (sc < best) { best = sc; point = x; normal = n; }
            }
        }
        return best < double.MaxValue;
    }

    static double Score(Vector3d x, Vector3d n, double defect, List<Vector3d> c, List<double> r, double h, int secondEnd)
    {
        // the plane touches the ball of at least one end of the edge (entries 0 and secondEnd) ...
        double t0 = Math.Abs(r[0] - Math.Abs(n.Dot(c[0] - x))) / h, t1 = Math.Abs(r[secondEnd] - Math.Abs(n.Dot(c[secondEnd] - x))) / h;
        double sc = defect * defect + Math.Min(t0, t1) * Math.Min(t0, t1);
        // ... and enters no ball by more than a crease allows (a face that ends at a convex crease leaves the ball of a node
        // beyond the crease empty although its plane enters it, so this term is weak)
        for (int i = 0; i < c.Count; ++i) {
            double enter = (r[i] - Math.Abs(n.Dot(c[i] - x))) / h;
            if (enter > 0) sc += 0.1 * enter * enter;
        }
        return sc;
    }
}
