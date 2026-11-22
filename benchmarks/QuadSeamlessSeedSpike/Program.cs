// QuadSeamlessSeedSpike: the upper-bound measurement of docs/research/QUAD_SEAMLESS_SEED_2026-11.md. A throwaway prototype, not library code.
//
// Question: how far would a seamless parametrization (Corman, Crane, "Rectangular Surface Parameterization", 2025; Capouellez et al.,
// "Feature-Aligned Parametrization in Penner Coordinates", 2025) cut the irregular vertices of gs.QuadRemesher if its frames seeded the
// position field, with the expensive parts idealised?
//
//   table 1  the curl mass (QUAD_REMESHING.md 4.5: the dislocations a lattice needs, in lattice units) that each frame model leaves:
//            uniform, IntegrableFrames Scale and ScaleAndRotation (the library), two scales per vertex with fixed directions and with a
//            rotation (the unknowns of the rectangular model, by a damped Gauss-Newton of this file), and the conformal oracle
//   table 2  the stage 2 mesh of the library's PositionField and extraction, seeded with the oracle's spacing and rotation (and with
//            the spacing clamped from below, minsp)
//   table 3  the oracle itself: Newton steps, residual, the seam defect of the parallel field, spacing and rotation ranges
//   table 4  exploratory: the pure-quad repair of stage 3B on the seeded lattices, judged by independent checks
//   gate=1   the 54 runs of benchmarks/QuadFlow3BGate: PureQuadStatus against the curl mass of the run
//
// The conformal oracle: the cones of the cross field (singular face of index k/4 -> the angle 2 pi - k pi / 2 at its vertex of smallest
// field magnitude), a discrete conformal metric with these cone angles (Springborn, Schroeder, Pinkall, "Conformal Equivalence of Triangle
// Meshes", 2008: Newton on the convex energy, fixed connectivity, no flips), the cross field that is parallel in that flat metric (breadth
// first over the faces, one global rotation fitted to the input field) and from both a spacing exp(-u) and a rotation per vertex. Its
// frames close up exactly in the flat metric, so what the position field still gets wrong with them is rounding, not integrability.
//
//   dotnet run -c Release --project benchmarks/QuadSeamlessSeedSpike -- [only=<regex>] [scale=1] [ratio=0.5] [minsp=0.7] [aniso=0] [gate=1]
using System;
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
    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    // the input of benchmarks/QuadRemeshBenchmark and QuadFlow3BGate
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

    static double Haus(DPolyMesh pm, DMesh3 input, double s)
    {
        if (pm == null || pm.FaceCount == 0) return double.NaN;
        DMesh3 tri = pm.ToDMesh3(out int[] map, new List<int>());
        if (tri.TriangleCount == 0) return double.NaN;
        var hd = new MeshHausdorffDistance(tri, input) { Symmetric = false, RelativeTolerance = 1e-3 };
        hd.Compute(); return hd.UpperBound / s;
    }

    static bool SameMesh(DPolyMesh a, DPolyMesh b)
    {
        if (a == null || b == null) return a == b;
        if (a.VertexCount != b.VertexCount || a.FaceCount != b.FaceCount) return false;
        if (!a.FaceOffsets.ToArray().SequenceEqual(b.FaceOffsets.ToArray()) || !a.FaceIndices.ToArray().SequenceEqual(b.FaceIndices.ToArray())) return false;
        for (int v = 0; v < a.VertexCount; ++v) if (a.GetVertex(v) != b.GetVertex(v)) return false;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ curl mass of a frame model

    // the representative of vertex 1's cross nearest to q0 (PositionField.CompatOrient) and whether it is the second axis (a quarter turn)
    static Vector3d Rep(Vector3d q0, Vector3d q1, Vector3d n1, out bool swapped)
    {
        Vector3d t1 = n1.Cross(q1);
        double dq = q0.Dot(q1), dt = q0.Dot(t1);
        swapped = !(Math.Abs(dq) >= Math.Abs(dt));
        return swapped ? (dt >= 0 ? t1 : -t1) : (dq >= 0 ? q1 : -q1);
    }

    /// <summary>
    /// The sum over the triangles (not skipped) of |w|, w the sum of the three edge vectors in the averaged, aligned frame of the edge, in
    /// lattice units: the formula of IntegrableFrames (CurlBefore; with a rotation the frames are turned first, which equals CurlAfter to first order only), with a spacing factor along the first axis (sa) and along
    /// the second axis (sb) of every vertex and a rotation of the frame (rot); sa = sb is the isotropic model of the library.
    /// </summary>
    static double Curl(DMesh3 m, CrossField cf, double s, double[] sa, double[] sb, double[] rot, bool[] skip)
    {
        int n = m.MaxVertexID;
        var Q = new Vector3d[n]; var N = new Vector3d[n];
        for (int v = 0; v < n; ++v) {
            N[v] = cf.VertexNormal(v); Vector3d q = cf.VertexDirection(v, 0);
            if (rot != null && rot[v] != 0) q = Math.Cos(rot[v]) * q + Math.Sin(rot[v]) * N[v].Cross(q);
            Q[v] = q;
        }
        double curl = 0;
        foreach (int t in m.TriangleIndices()) {
            if (skip[t]) continue;
            Index3i tv = m.GetTriangle(t);
            Vector3d qa = Q[tv.a], na = N[tv.a]; Vector2d w = Vector2d.Zero;
            for (int e = 0; e < 3; ++e) {
                int i = tv[e], j = tv[(e + 1) % 3];
                Vector3d qi = Rep(qa, Q[i], N[i], out bool swi), qj = Rep(qa, Q[j], N[j], out bool swj);
                Vector3d qe = qi + qj; qe /= qe.Length;
                Vector3d ev = (m.GetVertex(j) - m.GetVertex(i)) / s;
                double xi = sa == null ? 1 : (swi ? sb[i] : sa[i]), xj = sa == null ? 1 : (swj ? sb[j] : sa[j]);
                double yi = sa == null ? 1 : (swi ? sa[i] : sb[i]), yj = sa == null ? 1 : (swj ? sa[j] : sb[j]);
                w.x += ev.Dot(qe) / Math.Sqrt(xi * xj); w.y += ev.Dot(na.Cross(qe)) / Math.Sqrt(yi * yj);
            }
            curl += w.Length;
        }
        return curl;
    }

    // ------------------------------------------------------------------------------------------------ two scales per vertex (the unknowns of the rectangular model)

    /// <summary>
    /// Damped Gauss-Newton on the sum of |w|^2 with a log spacing along each of the two axes of every vertex (a, b) and optionally a
    /// rotation (IntegrableFrames with one more scale; the same damping, backtracking and gauge: mean of a and of b zero). This is the
    /// freedom of a rectangular parametrization (axis scales exp(u + v), exp(u - v), a frame rotation), solved as a least squares
    /// problem and not as the paper's constrained optimization: a capacity measurement.
    /// </summary>
    static int TwoScales(DMesh3 m, CrossField cf, double s, bool useRot, bool tied, bool[] skip, out double[] sa, out double[] sb, out double[] rot)
    {
        int n = m.MaxVertexID;
        var tri = new List<int[]>(); var av = new List<Vector2d[]>(); var sw = new List<bool[]>();
        foreach (int t in m.TriangleIndices()) {
            if (skip[t]) continue;
            Index3i tv = m.GetTriangle(t);
            Vector3d qa = cf.VertexDirection(tv.a, 0), na = cf.VertexNormal(tv.a);
            var a = new Vector2d[3]; var f = new bool[3];
            for (int k = 0; k < 3; ++k) Rep(qa, cf.VertexDirection(tv[k], 0), cf.VertexNormal(tv[k]), out f[k]);
            if (tied) f = null;        // the control: one spacing per vertex (slot 0 for both axes), the same solver, clamp and triangles
            for (int e = 0; e < 3; ++e) {
                int i = tv[e], j = tv[(e + 1) % 3];
                Vector3d qe = Rep(qa, cf.VertexDirection(i, 0), cf.VertexNormal(i), out bool d0) + Rep(qa, cf.VertexDirection(j, 0), cf.VertexNormal(j), out bool d1);
                qe /= qe.Length;
                Vector3d ev = (m.GetVertex(j) - m.GetVertex(i)) / s;
                a[e] = new Vector2d(ev.Dot(qe), ev.Dot(na.Cross(qe)));
            }
            tri.Add(new[] { tv.a, tv.b, tv.c }); av.Add(a); sw.Add(f);
        }
        int mt = tri.Count;
        var x = new double[3 * n];      // per vertex: log spacing along axis 0, along axis 1, rotation
        double maxLog = Math.Log(4.0), maxPhi = Math.PI / 4, damping = 0.01;
        Func<double[], double> cost = xx => {
            double c = 0;
            for (int t = 0; t < mt; ++t) {
                Vector2d w = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) { Term(tri[t], av[t], sw[t], xx, e, out Vector2d term, out Vector2d d); w += term; }
                c += 0.5 * w.LengthSquared;
            }
            return c;
        };
        double cur = cost(x);
        var used = new bool[n]; int nUsed = 0;
        foreach (int[] c in tri) foreach (int i in c) if (!used[i]) { used[i] = true; nUsed++; }
        int steps = 0;
        for (int step = 0; step < 40; ++step) {
            var chol = new SparseCholesky(3 * n, 3);
            var rhs = new double[3 * n];
            var J = new double[3][];        // per corner: [dwx / d(a, b, phi), dwy / d(a, b, phi)]
            for (int t = 0; t < mt; ++t) {
                int[] c = tri[t]; var term = new Vector2d[3]; var dphi = new Vector2d[3]; Vector2d w = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) { Term(c, av[t], sw[t], x, e, out term[e], out dphi[e]); w += term[e]; }
                for (int k = 0; k < 3; ++k) {
                    int e1 = k, e2 = (k + 2) % 3; int px = sw[t] == null ? 0 : (sw[t][k] ? 1 : 0), py = sw[t] == null ? 0 : 1 - px;
                    var j = new double[6];
                    j[px] = -0.5 * (term[e1].x + term[e2].x);
                    j[3 + py] = -0.5 * (term[e1].y + term[e2].y);
                    if (useRot) { j[2] = 0.5 * (dphi[e1].x + dphi[e2].x); j[5] = 0.5 * (dphi[e1].y + dphi[e2].y); }
                    J[k] = j;
                    for (int ca = 0; ca < 3; ++ca) rhs[3 * c[k] + ca] -= j[ca] * w.x + j[3 + ca] * w.y;
                }
                for (int k = 0; k < 3; ++k) for (int l = 0; l < 3; ++l) for (int ca = 0; ca < 3; ++ca) for (int cb = 0; cb < 3; ++cb) {
                    int r = 3 * c[k] + ca, q = 3 * c[l] + cb;
                    if (r < q) continue;
                    chol.Add(r, q, J[k][ca] * J[l][cb] + J[k][3 + ca] * J[l][3 + cb]);
                }
            }
            for (int i = 0; i < 3 * n; ++i) chol.Add(i, i, damping);
            if (!chol.Factorize()) break;
            var d = new double[3 * n];
            chol.Solve(rhs, d);
            for (int comp = 0; comp < 2; ++comp) {
                double mean = 0; for (int i = 0; i < n; ++i) if (used[i]) mean += d[3 * i + comp]; mean /= nUsed;
                for (int i = 0; i < n; ++i) d[3 * i + comp] = used[i] ? d[3 * i + comp] - mean : 0;
            }
            double alpha = 1, nc = cur; bool ok = false; var x2 = new double[3 * n];
            for (int tries = 0; tries < 8; ++tries) {
                for (int i = 0; i < n; ++i) {
                    x2[3 * i] = Math.Max(-maxLog, Math.Min(maxLog, x[3 * i] + alpha * d[3 * i]));
                    x2[3 * i + 1] = Math.Max(-maxLog, Math.Min(maxLog, x[3 * i + 1] + alpha * d[3 * i + 1]));
                    x2[3 * i + 2] = useRot ? Math.Max(-maxPhi, Math.Min(maxPhi, x[3 * i + 2] + alpha * d[3 * i + 2])) : 0;
                }
                nc = cost(x2);
                if (nc < cur) { ok = true; break; }
                alpha *= 0.5;
            }
            if (!ok) break;
            double gain = (cur - nc) / cur;
            x = x2; cur = nc; steps++;
            if (gain < 1e-3) break;
        }
        sa = new double[n]; sb = new double[n]; rot = new double[n];
        for (int i = 0; i < n; ++i) { sa[i] = Math.Exp(x[3 * i]); sb[i] = Math.Exp(x[3 * i + (tied ? 0 : 1)]); rot[i] = x[3 * i + 2]; }
        return steps;
    }

    // edge e of a triangle: the edge vector turned by -phi_e, then scaled per axis; d = its derivative by phi_e
    static void Term(int[] c, Vector2d[] a, bool[] sw, double[] x, int e, out Vector2d term, out Vector2d dphi)
    {
        int k0 = e, k1 = (e + 1) % 3, i = c[k0], j = c[k1];
        double pe = 0.5 * (x[3 * i + 2] + x[3 * j + 2]), cs = Math.Cos(pe), sn = Math.Sin(pe);
        double xr = cs * a[e].x + sn * a[e].y, yr = -sn * a[e].x + cs * a[e].y;
        int xi = sw == null ? 0 : (sw[k0] ? 1 : 0), xj = sw == null ? 0 : (sw[k1] ? 1 : 0), yi = sw == null ? 0 : 1 - xi, yj = sw == null ? 0 : 1 - xj;
        double gx = Math.Exp(-0.5 * (x[3 * i + xi] + x[3 * j + xj]));
        double gy = Math.Exp(-0.5 * (x[3 * i + yi] + x[3 * j + yj]));
        term = new Vector2d(gx * xr, gy * yr);
        dphi = new Vector2d(gx * yr, -gy * xr);
    }

    // ------------------------------------------------------------------------------------------------ the conformal oracle

    class Oracle
    {
        public double[] Spacing, Rot, U;
        public List<int> ConeVertices = new List<int>();
        public int Iterations, BrokenTriangles;
        public double Residual, SeamDefectDeg, GaussBonnetError, Seconds;
        public bool Converged;
    }

    // corner angles and cotangents of a triangle with the edge lengths l[k] opposite corner k; a broken triangle inequality gives (pi, 0, 0)
    static bool TriAngles(double l0, double l1, double l2, double[] ang, double[] cot)
    {
        if (l0 >= l1 + l2) { ang[0] = Math.PI; ang[1] = ang[2] = 0; cot[0] = cot[1] = cot[2] = 0; return false; }
        if (l1 >= l0 + l2) { ang[1] = Math.PI; ang[0] = ang[2] = 0; cot[0] = cot[1] = cot[2] = 0; return false; }
        if (l2 >= l0 + l1) { ang[2] = Math.PI; ang[0] = ang[1] = 0; cot[0] = cot[1] = cot[2] = 0; return false; }
        double sp = 0.5 * (l0 + l1 + l2);
        double a4 = 4 * Math.Sqrt(sp * (sp - l0) * (sp - l1) * (sp - l2));
        ang[0] = 2 * Math.Atan2(Math.Sqrt((sp - l1) * (sp - l2)), Math.Sqrt(sp * (sp - l0)));
        ang[1] = 2 * Math.Atan2(Math.Sqrt((sp - l0) * (sp - l2)), Math.Sqrt(sp * (sp - l1)));
        ang[2] = Math.PI - ang[0] - ang[1];
        cot[0] = (l1 * l1 + l2 * l2 - l0 * l0) / a4; cot[1] = (l0 * l0 + l2 * l2 - l1 * l1) / a4; cot[2] = (l0 * l0 + l1 * l1 - l2 * l2) / a4;
        return true;
    }

    static void ScaledLengths(DMesh3 m, Index3i tv, double[] u, double[] l)
    {
        Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
        l[0] = (b - c).Length * Math.Exp(0.5 * (u[tv.b] + u[tv.c]));
        l[1] = (a - c).Length * Math.Exp(0.5 * (u[tv.a] + u[tv.c]));
        l[2] = (a - b).Length * Math.Exp(0.5 * (u[tv.a] + u[tv.b]));
    }

    static double AngleDefects(DMesh3 m, double[] u, double[] target, double[] g, out int broken)
    {
        int n = m.MaxVertexID; broken = 0;
        for (int v = 0; v < n; ++v) g[v] = target[v];
        var l = new double[3]; var ang = new double[3]; var cot = new double[3];
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            ScaledLengths(m, tv, u, l);
            if (!TriAngles(l[0], l[1], l[2], ang, cot)) broken++;
            g[tv.a] -= ang[0]; g[tv.b] -= ang[1]; g[tv.c] -= ang[2];
        }
        double res = 0; for (int v = 0; v < n; ++v) res = Math.Max(res, Math.Abs(g[v]));
        return res;
    }

    static double Wrap(double a, double period) { a -= period * Math.Round(a / period); return a; }

    static Oracle Conformal(DMesh3 m, CrossField cf)
    {
        var sw = Stopwatch.StartNew();
        var o = new Oracle();
        int n = m.MaxVertexID;
        var target = new double[n];
        for (int v = 0; v < n; ++v) target[v] = 2 * Math.PI;
        foreach (int f in cf.SingularFaces) {
            Index3i tv = m.GetTriangle(f); int best = tv.a;
            for (int k = 1; k < 3; ++k) if (cf.VertexMagnitude(tv[k]) < cf.VertexMagnitude(best)) best = tv[k];
            target[best] -= cf.FaceIndex(f) * (Math.PI / 2);
            if (!o.ConeVertices.Contains(best)) o.ConeVertices.Add(best);
        }
        double gb = 0; for (int v = 0; v < n; ++v) gb += 2 * Math.PI - target[v];
        int chi = m.VertexCount - m.EdgeCount + m.TriangleCount;
        o.GaussBonnetError = Math.Abs(gb - 2 * Math.PI * chi);

        // Newton on the convex energy: gradient target - angle sum, Hessian the cotangent Laplacian of the scaled metric
        var u = new double[n]; var g = new double[n];
        double res = AngleDefects(m, u, target, g, out o.BrokenTriangles);
        var l = new double[3]; var ang = new double[3]; var cot = new double[3];
        for (int it = 0; it < 60 && res > 1e-11; ++it) {
            var chol = new SparseCholesky(n);
            double diagSum = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                ScaledLengths(m, tv, u, l);
                TriAngles(l[0], l[1], l[2], ang, cot);
                for (int k = 0; k < 3; ++k) {
                    int i = tv[(k + 1) % 3], j = tv[(k + 2) % 3]; double w = 0.5 * cot[k];
                    chol.Add(i, i, w); chol.Add(j, j, w); chol.Add(i, j, -w); diagSum += 2 * w;
                }
            }
            double eps = 1e-10 * diagSum / n;
            for (int v = 0; v < n; ++v) chol.Add(v, v, eps);
            if (!chol.Factorize()) break;
            var rhs = new double[n]; var d = new double[n];
            for (int v = 0; v < n; ++v) rhs[v] = -g[v];
            chol.Solve(rhs, d);
            double mean = d.Sum() / n; for (int v = 0; v < n; ++v) d[v] -= mean;
            double alpha = 1; var u2 = new double[n]; var g2 = new double[n]; bool ok = false;
            for (int tries = 0; tries < 30; ++tries) {
                for (int v = 0; v < n; ++v) u2[v] = u[v] + alpha * d[v];
                double r2 = AngleDefects(m, u2, target, g2, out int br);
                if (r2 < res) { u = u2; g = g2; res = r2; o.BrokenTriangles = br; ok = true; break; }
                alpha *= 0.5;
            }
            o.Iterations = it + 1;
            if (!ok) break;
        }
        o.Residual = res; o.Converged = res <= 1e-8; o.U = u;

        // the layout of every face in the flat metric (corner a at the origin, b on the x axis), then the parallel cross field by a
        // breadth-first walk over the faces: across an edge the direction keeps its angle to the edge
        int nt = m.MaxTriangleID;
        var P = new Vector2d[nt][]; var theta = new double[nt]; var seen = new bool[nt];
        foreach (int t in m.TriangleIndices()) {
            ScaledLengths(m, m.GetTriangle(t), u, l);
            double cx = (l[1] * l[1] + l[2] * l[2] - l[0] * l[0]) / (2 * l[2]);
            P[t] = new[] { Vector2d.Zero, new Vector2d(l[2], 0), new Vector2d(cx, Math.Sqrt(Math.Max(1e-30, l[1] * l[1] - cx * cx))) };
        }
        Func<int, int, int, double> edgeAngle = (t, va, vb) => {
            Index3i tv = m.GetTriangle(t); int ia = tv.a == va ? 0 : (tv.b == va ? 1 : 2), ib = tv.a == vb ? 0 : (tv.b == vb ? 1 : 2);
            Vector2d dd = P[t][ib] - P[t][ia]; return Math.Atan2(dd.y, dd.x);
        };
        double seam = 0;
        foreach (int root in m.TriangleIndices()) {
            if (seen[root]) continue;
            var queue = new Queue<int>(); queue.Enqueue(root); seen[root] = true;
            while (queue.Count > 0) {
                int f = queue.Dequeue(); Index3i te = m.GetTriEdges(f);
                for (int k = 0; k < 3; ++k) {
                    Index2i et = m.GetEdgeT(te[k]); int gq = et.a == f ? et.b : et.a;
                    if (gq < 0) continue;
                    Index2i ev = m.GetEdgeV(te[k]);
                    double tg = theta[f] - edgeAngle(f, ev.a, ev.b) + edgeAngle(gq, ev.a, ev.b);
                    if (!seen[gq]) { seen[gq] = true; theta[gq] = tg; queue.Enqueue(gq); }
                    else seam = Math.Max(seam, Math.Abs(Wrap(theta[gq] - tg, Math.PI / 2)));
                }
            }
        }
        o.SeamDefectDeg = seam * 180 / Math.PI;

        // one global rotation: the parallel field nearest to the input field (area weighted, as 4-RoSy fields)
        double sx = 0, sy = 0;
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            Vector3d A = m.GetVertex(tv.a), E1 = m.GetVertex(tv.b) - A, E2 = m.GetVertex(tv.c) - A, dir = cf.FaceDirection(t);
            if (dir.LengthSquared == 0) continue;
            double a11 = E1.Dot(E1), a12 = E1.Dot(E2), a22 = E2.Dot(E2), b1 = dir.Dot(E1), b2 = dir.Dot(E2), det = a11 * a22 - a12 * a12;
            double al = (b1 * a22 - b2 * a12) / det, be = (a11 * b2 - a12 * b1) / det;
            Vector2d d2 = al * P[t][1] + be * P[t][2];
            double tf = Math.Atan2(d2.y, d2.x), area = 0.5 * E1.Cross(E2).Length;
            sx += area * Math.Cos(4 * (tf - theta[t])); sy += area * Math.Sin(4 * (tf - theta[t]));
        }
        double delta = Math.Atan2(sy, sx) / 4;

        // per vertex: the rotation from the input field to the parallel field (4-RoSy mean over the faces, weighted by the corner angle)
        // and the spacing exp(-u), normalized to the same number of cells as the uniform spacing
        var zx = new double[n]; var zy = new double[n]; var varea = new double[n];
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            Vector3d A = m.GetVertex(tv.a), E1 = m.GetVertex(tv.b) - A, E2 = m.GetVertex(tv.c) - A;
            double th = theta[t] + delta, be = Math.Sin(th) / P[t][2].y, al = (Math.Cos(th) - be * P[t][2].x) / P[t][1].x;
            Vector3d D = al * E1 + be * E2;
            double area = 0.5 * E1.Cross(E2).Length;
            for (int k = 0; k < 3; ++k) {
                int v = tv[k]; Vector3d N = cf.VertexNormal(v), Q = cf.VertexDirection(v, 0);
                double a = Math.Atan2(D.Dot(N.Cross(Q)), D.Dot(Q));
                double wgt = m.GetTriInternalAngleR(t, k);
                zx[v] += wgt * Math.Cos(4 * a); zy[v] += wgt * Math.Sin(4 * a); varea[v] += area / 3;
            }
        }
        o.Rot = new double[n]; o.Spacing = new double[n];
        double num = 0, den = 0;
        for (int v = 0; v < n; ++v) { o.Rot[v] = Math.Atan2(zy[v], zx[v]) / 4; num += varea[v] * Math.Exp(2 * u[v]); den += varea[v]; }
        double c0 = Math.Sqrt(num / den);
        for (int v = 0; v < n; ++v) o.Spacing[v] = c0 * Math.Exp(-u[v]);
        o.Seconds = sw.Elapsed.TotalSeconds;
        return o;
    }

    // ------------------------------------------------------------------------------------------------ the library's stage 2 with given frames

    // PositionField with a spacing and a rotation per vertex, then QuadRemesher's extraction and cleanup (a private method: by reflection,
    // so that the spike changes no library file); checked against QuadRemesher.SizeAdaptation in Main
    static DPolyMesh Seeded(DMesh3 m, CrossField cf, double s, double[] spacing, double[] rot, bool pure, out string pureStatus)
    {
        pureStatus = "-";
        var pf = new PositionField(m, cf) { TargetEdgeLength = s, SpacingFactors = spacing, RotationAngles = rot };
        if (pf.Compute() != PositionFieldStatus.Success) return null;
        var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = s };
        MethodInfo extract = typeof(QuadRemesher).GetMethod("Extract", BindingFlags.NonPublic | BindingFlags.Instance);
        var stage2 = (DPolyMesh)extract.Invoke(qr, new object[] { pf });
        if (pure) {
            // exploratory: the pure-quad repair of stage 3B (an internal class, written for a uniform spacing) on the seeded lattice. Its own
            // status is not trusted here: "clean" needs the independent checks of benchmarks/QuadFlow3BGate (manifold, quads only, finite,
            // no quad facing away from the input) and a one-sided Hausdorff distance of at most 0.5 s
            Type repType = typeof(QuadRemesher).Assembly.GetType("gs.QuadFlowRepair");
            object rep = Activator.CreateInstance(repType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { m, cf, pf, s }, null);
            var status = (PureQuadStatus)repType.GetMethod("Run", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(rep, null);
            var res = (DPolyMesh)repType.GetField("Result", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(rep);
            pureStatus = status.ToString();
            if (status == PureQuadStatus.Success && res != null) {
                PolyMeshMetrics mm = PolyMeshMetrics.Compute(res);
                var tree = new DMeshAABBTree3(m, true); int away = 0;
                for (int f = 0; f < res.FaceCount; ++f) {
                    var fv = res.GetFace(f);
                    if (fv.Length != 4) { away++; continue; }
                    Vector3d p0 = res.GetVertex(fv[0]), p1 = res.GetVertex(fv[1]), p2 = res.GetVertex(fv[2]), p3 = res.GetVertex(fv[3]);
                    int t = tree.FindNearestTriangle(0.25 * (p0 + p1 + p2 + p3));
                    if (t < 0 || (p2 - p0).Cross(p3 - p1).Dot(m.GetTriNormal(t)) <= 0) away++;
                }
                bool finite = Enumerable.Range(0, res.VertexCount).All(v => res.GetVertex(v).IsFinite);
                double hd = Haus(res, m, s);
                bool clean = mm.IsManifold && mm.QuadFraction == 1 && away == 0 && finite && res.Validate().IsValid && mm.BoundaryVertices == 0;
                double p95 = PolyMeshMetrics.Percentile(mm.CornerDeviations, 0.95);
                pureStatus = (clean ? (hd <= 0.5 ? (p95 <= 15 ? "clean" : "clean, corner p95 above 15") : "clean but Hausdorff " + F(hd, "F2") + " s") : "Success, checks FAIL") + $": {mm.IrregularVertices} irregular, {mm.Faces} quads, corner p95 {F(PolyMeshMetrics.Percentile(mm.CornerDeviations, 0.95), "F1")}";
            }
        }
        return stage2;
    }

    static string Row(DPolyMesh r, DMesh3 input, double s)
    {
        if (r == null || r.FaceCount == 0) return "failed";
        PolyMeshMetrics mm = PolyMeshMetrics.Compute(r);
        return $"{mm.IrregularVertices} / {F(100 * mm.QuadFraction, "F1")} / {F(PolyMeshMetrics.Percentile(mm.CornerDeviations, 0.95), "F1")} / {mm.Faces} / {F(Haus(r, input, s), "F2")}{(mm.IsManifold ? "" : " (not manifold)")}{(mm.BoundaryVertices == 0 ? "" : " (" + mm.BoundaryVertices + " boundary V, area " + F(100 * Area(r) / MeshMeasurements.AreaT(input, input.TriangleIndices()), "F1") + " %)")}";
    }

    static double Area(DPolyMesh r) { DMesh3 tri = r.ToDMesh3(out int[] map, new List<int>()); return MeshMeasurements.AreaT(tri, tri.TriangleIndices()); }

    static string Range(double[] x) { var c = x.OrderBy(v => v).ToArray(); return F(c[0], "F2") + "-" + F(c[c.Length - 1], "F2"); }
    static double Pct(IEnumerable<double> x, double q) { var c = x.OrderBy(v => v).ToArray(); return c[Math.Min(c.Length - 1, (int)(q * c.Length))]; }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double scale = A.ContainsKey("scale") ? double.Parse(A["scale"], CultureInfo.InvariantCulture) : 1.0;
        double ratio = A.ContainsKey("ratio") ? double.Parse(A["ratio"], CultureInfo.InvariantCulture) : 0.5;
        bool gate = A.ContainsKey("gate") && A["gate"] == "1";
        double minSpacing = A.ContainsKey("minsp") ? double.Parse(A["minsp"], CultureInfo.InvariantCulture) : 0.7;
        bool aniso = !(A.ContainsKey("aniso") && A["aniso"] == "0");
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
        var shapes = new List<(string, Func<Vector3d, double>, double, double, double)> {
            ("sphere", sphere, 0.1, 0.0, 0.0), ("torus", torus, 0.1, 0.0, 0.0), ("torus-align10", torus, 0.1, 10.0, 0.0),
            ("bumpy", bumpy, 0.06, 0.0, 0.0), ("filleted-box", rbox, 0.08, 0.0, 0.0), ("noisy-sphere", sphere, 0.1, 0.0, 0.2),
            ("capsule", capsule, 0.08, 0.0, 0.0), ("genus2", genus2, 0.06, 0.0, 0.0), ("capped-cylinder", cylinder, 0.08, 0.0, 0.0),
        };
        if (gate) return Gate(shapes, only);

        var t1 = new List<string>(); var t2 = new List<string>(); var t3 = new List<string>(); var t4 = new List<string>();
        foreach (var (name, fn, s0, align, noise) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            double s = s0 * scale;
            DMesh3 input = MakeInput(fn, s * ratio, noise);
            var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
            if (!cf.Success) { Console.WriteLine($"{name}: cross field {cf.Status}"); continue; }
            int S = cf.SingularFaces.Count;

            // the library: uniform, Scale, ScaleAndRotation, and the pure-quad attempt on the uniform lattice
            var q0 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s }; q0.Compute();
            var f1 = new IntegrableFrames(null, null); var q1 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, SizeAdaptation = SizeAdaptationMode.Scale, Frames = f1 }; q1.Compute();
            var f2 = new IntegrableFrames(null, null); var q2 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, SizeAdaptation = SizeAdaptationMode.ScaleAndRotation, Frames = f2 }; q2.Compute();
            var qp = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true }; qp.Compute();
            // the reflection path against the library: the same frames must give the same mesh
            string pureUniform, pureSR = "not applied (= uniform)", pureOracle;
            bool pathOk = SameMesh(Seeded(input, cf, s, null, null, true, out pureUniform), q0.Result);
            if (q2.SizeAdaptationApplied) pathOk &= SameMesh(Seeded(input, cf, s, f2.Spacing, f2.Rotation, true, out pureSR), q2.Result);
            pathOk &= pureUniform.StartsWith("clean") == (qp.PureQuadStatus == PureQuadStatus.Success);

            Oracle o = Conformal(input, cf);
            // the same oracle with the spacing clamped from below: a lattice finer than the input triangles cannot be represented by the position field
            var clamped = (double[])o.Spacing.Clone();
            {
                // the clamp removes cells, so the scale is found again: the same expected number of cells as the uniform spacing
                var va = new double[input.MaxVertexID]; double total = 0;
                foreach (int t in input.TriangleIndices()) { Index3i tv = input.GetTriangle(t); double a3 = input.GetTriArea(t) / 3; va[tv.a] += a3; va[tv.b] += a3; va[tv.c] += a3; total += 3 * a3; }
                double c = 1;
                for (int it = 0; it < 50; ++it) {
                    double cells = 0;
                    foreach (int v in input.VertexIndices()) { double sp = Math.Max(c * o.Spacing[v], minSpacing); cells += va[v] / (sp * sp); }
                    c *= Math.Sqrt(cells / total);
                }
                foreach (int v in input.VertexIndices()) clamped[v] = Math.Max(c * o.Spacing[v], minSpacing);
            }
            var skip = new bool[input.MaxTriangleID];
            foreach (int f in cf.SingularFaces) skip[f] = true;
            foreach (int v in o.ConeVertices) foreach (int t in input.VtxTrianglesItr(v)) skip[t] = true;
            double cU = Curl(input, cf, s, null, null, null, skip);
            double cS = Curl(input, cf, s, f1.Spacing, f1.Spacing, null, skip), cSR = Curl(input, cf, s, f2.Spacing, f2.Spacing, f2.Rotation, skip);
            double cO = Curl(input, cf, s, o.Spacing, o.Spacing, o.Rot, skip);
            string c1 = "-", c1R = "-", cA = "-", cAR = "-", anis = "-", anisR = "-", gnSteps = "-";
            if (aniso) {
                Func<double[], double[], string> ratioOf = (pa, pb) => F(Pct(input.VertexIndices().Select(v => Math.Max(pa[v] / pb[v], pb[v] / pa[v])), 0.95), "F2") + " / " + F(input.VertexIndices().Max(v => Math.Max(pa[v] / pb[v], pb[v] / pa[v])), "F2");
                int n1 = TwoScales(input, cf, s, false, true, skip, out double[] sa, out double[] sb, out double[] rr);
                c1 = F(Curl(input, cf, s, sa, sb, null, skip), "F1");
                int n2 = TwoScales(input, cf, s, true, true, skip, out sa, out sb, out rr);
                c1R = F(Curl(input, cf, s, sa, sb, rr, skip), "F1");
                int n3 = TwoScales(input, cf, s, false, false, skip, out sa, out sb, out rr);
                cA = F(Curl(input, cf, s, sa, sb, null, skip), "F1"); anis = ratioOf(sa, sb);
                int n4 = TwoScales(input, cf, s, true, false, skip, out sa, out sb, out rr);
                cAR = F(Curl(input, cf, s, sa, sb, rr, skip), "F1"); anisR = ratioOf(sa, sb);
                gnSteps = $"{n1} / {n2} / {n3} / {n4}";
            }
            DPolyMesh ro = Seeded(input, cf, s, o.Spacing, o.Rot, true, out pureOracle);
            DPolyMesh rc = Seeded(input, cf, s, clamped, o.Rot, true, out string pureClamped);
            var rotDeg = input.VertexIndices().Select(v => Math.Abs(o.Rot[v]) * 180 / Math.PI).ToArray();

            t1.Add($"| {name} | {input.VertexCount} | {S} | {F(f1.CurlBefore, "F1")} | {F(cU, "F1")} | {F(cS, "F1")} | {F(cSR, "F1")} ({F(f2.CurlAfter, "F1")}) | {c1} | {c1R} | {cA} | {cAR} | {F(cO, "F1")} | {anis} | {anisR} | {gnSteps} |");
            t2.Add($"| {name} | {S} | {Row(q0.Result, input, s)} | {Row(q1.Result, input, s)}{(q1.SizeAdaptationApplied ? "" : " (not applied)")} | {Row(q2.Result, input, s)}{(q2.SizeAdaptationApplied ? "" : " (not applied)")} | {Row(ro, input, s)} | {Row(rc, input, s)} | {(pathOk ? "yes" : "NO")} |");
            t4.Add($"| {name} | {S} | {qp.PureQuadStatus} | {pureUniform} | {pureSR} | {pureOracle} | {pureClamped} |");
            t3.Add($"| {name} | {o.ConeVertices.Count} | {o.Iterations} | {o.Residual:E1} | {o.BrokenTriangles} | {F(o.SeamDefectDeg, "F2")} | {Range(input.VertexIndices().Select(v => o.Spacing[v]).ToArray())} | {F(Pct(input.VertexIndices().Select(v => o.Spacing[v]), 0.05), "F2")}-{F(Pct(input.VertexIndices().Select(v => o.Spacing[v]), 0.95), "F2")} | {F(Pct(rotDeg, 0.5), "F1")} / {F(Pct(rotDeg, 0.95), "F1")} / {F(rotDeg.Max(), "F1")} | {F(o.Seconds, "F2")} | {F(f1.Seconds, "F2")} / {F(f2.Seconds, "F2")} | {o.GaussBonnetError:E1} |");
            Console.Error.WriteLine($"  done {name}");
        }
        Console.WriteLine("Table 1: curl mass (lattice units) left by each frame model");
        Console.WriteLine();
        Console.WriteLine("| shape | input V | S | library CurlBefore | uniform | Scale | ScaleAndRotation (library CurlAfter, all non-singular triangles) | control: one scale | control: one scale + rotation | two scales | two scales + rotation | conformal oracle | axis ratio p95 / max (two scales) | (with rotation) | Gauss-Newton steps (the four solves) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string r in t1) Console.WriteLine(r);
        Console.WriteLine();
        Console.WriteLine("Table 2: stage 2 mesh, irregular V / quads % / corner p95 (deg) / faces / Hausdorff out->in (s)");
        Console.WriteLine();
        Console.WriteLine("| shape | S | uniform | Scale | ScaleAndRotation | conformal oracle seed | oracle, spacing >= " + F(minSpacing, "G3") + " | reflection path = library |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (string r in t2) Console.WriteLine(r);
        Console.WriteLine();
        Console.WriteLine("Table 3: the conformal oracle");
        Console.WriteLine();
        Console.WriteLine("| shape | cone vertices | Newton steps | residual (rad) | broken triangles | seam defect (deg) | spacing min-max | spacing p5-p95 | rotation p50 / p95 / max (deg) | oracle seconds | IntegrableFrames seconds, Scale / ScaleAndRotation | Gauss-Bonnet error of the cone angles (rad) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string r in t3) Console.WriteLine(r);
        Console.WriteLine();
        Console.WriteLine("Table 4: the pure-quad repair (stage 3B) on each lattice, exploratory for the two size fields");
        Console.WriteLine();
        Console.WriteLine("| shape | S | library PureQuads (uniform) | repair by reflection, uniform | on ScaleAndRotation | on the conformal oracle | on the clamped oracle |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (string r in t4) Console.WriteLine(r);
        return 0;
    }

    // the 54 runs of QuadFlow3BGate: does the pure-quad attempt succeed where the curl is small?
    static int Gate(List<(string, Func<Vector3d, double>, double, double, double)> shapes, Regex only)
    {
        var variations = new (double scale, double ratio)[] { (1, 0.5), (0.8, 0.5), (1.25, 0.5), (1.5, 0.5), (1, 0.35), (1, 0.7) };
        Console.WriteLine("| shape | scale / ratio | S | faces (stage 2) | irregular (stage 2) | curl mass C | C per 1000 faces | PureQuadStatus |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        var ok = new List<double>(); var bad = new List<double>();
        foreach (var (name, fn, s0, align, noise) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in variations) {
                double s = s0 * scale;
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
                if (!cf.Success) continue;
                var q = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true }; q.Compute();
                var q2 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s }; q2.Compute();
                var fr = new IntegrableFrames(input, cf) { TargetEdgeLength = s, Iterations = 1 }; fr.Compute();
                PolyMeshMetrics mm = PolyMeshMetrics.Compute(q2.Result);
                double per = 1000 * fr.CurlBefore / mm.Faces;
                (q.PureQuadStatus == PureQuadStatus.Success ? ok : bad).Add(per);
                Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {cf.SingularFaces.Count} | {mm.Faces} | {mm.IrregularVertices} | {F(fr.CurlBefore, "F1")} | {F(per, "F1")} | {q.PureQuadStatus} |");
            }
        }
        Console.WriteLine();
        if (ok.Count > 0 && bad.Count > 0) {
            Console.WriteLine($"curl mass per 1000 faces: Success ({ok.Count} runs) median {F(Pct(ok, 0.5), "F1")}, range {F(ok.Min(), "F1")}-{F(ok.Max(), "F1")}; other statuses ({bad.Count} runs) median {F(Pct(bad, 0.5), "F1")}, range {F(bad.Min(), "F1")}-{F(bad.Max(), "F1")}");
            var all = ok.Select(x => (x, true)).Concat(bad.Select(x => (x, false))).OrderBy(p => p.Item1).ToList();
            int half = all.Count / 2;
            Console.WriteLine($"lower half by curl per face: {all.Take(half).Count(p => p.Item2)} of {half} Success; upper half: {all.Skip(half).Count(p => p.Item2)} of {all.Count - half}");
        }
        return 0;
    }
}
