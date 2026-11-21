// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Quad remeshing stage 2 benchmark (docs/design/QUAD_REMESHING.md section 4): gs.CrossField, gs.PositionField and gs.QuadRemesher on
// procedural shapes (MarchingCubes, then 20 Remesher passes at edge h = ratio * s) and open shapes. One Markdown table row per shape
// with the stage 2 gate numbers: quads %, faces above 6 sides (always 0: MaxFaceSides), irregular vertices against the field's
// singularities, corner deviation p95, one-sided Hausdorff (output to input) over s, manifold, and the times of the stages.
//
//   dotnet run -c Release --project benchmarks/QuadRemeshBenchmark -- [only=<regex>] [ratio=0.5] [sweeps=20] [scale=1] [serial=1] [cleanup=0] [big=<n>] [align=<s>] [adapt=0|1|2] [sharp=1] [creases=1] [creaseangle=30] [digest=1]
//
// align is CrossField.CurvatureAlignment (default 0: the smoothest field); scale multiplies every s; serial=1 runs the position field without parallel loops; big=<n> adds a sphere of about n input vertices
// (the speed gate: at most 5 s of position field and extraction per 100k input vertices on 4 cores).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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

    class Shape { public string Name; public Func<Vector3d, double> F; public double S; public double Noise; public Func<DMesh3, DMesh3> Post; public double Radius = 1; }

    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double ratio = A.ContainsKey("ratio") ? double.Parse(A["ratio"], CultureInfo.InvariantCulture) : 0.5;
        int sweeps = A.ContainsKey("sweeps") ? int.Parse(A["sweeps"]) : 20;
        double scaleMul = A.ContainsKey("scale") ? double.Parse(A["scale"], CultureInfo.InvariantCulture) : 1.0;
        bool serial = A.ContainsKey("serial") && A["serial"] == "1";
        bool cleanup = !(A.ContainsKey("cleanup") && A["cleanup"] == "0");
        double mm = A.ContainsKey("mm") ? double.Parse(A["mm"], CultureInfo.InvariantCulture) : 0.5;
        double align = A.ContainsKey("align") ? double.Parse(A["align"], CultureInfo.InvariantCulture) : 0;
        double collapse = A.ContainsKey("collapse") ? double.Parse(A["collapse"], CultureInfo.InvariantCulture) : -1;
        int adapt = A.ContainsKey("adapt") ? int.Parse(A["adapt"]) : 0;
        double eps = A.ContainsKey("eps") ? double.Parse(A["eps"], CultureInfo.InvariantCulture) : 0.05;
        bool sharp = A.ContainsKey("sharp") && A["sharp"] == "1";
        bool creases = A.ContainsKey("creases") && A["creases"] == "1";
        double creaseAngle = A.ContainsKey("creaseangle") ? double.Parse(A["creaseangle"], CultureInfo.InvariantCulture) : 30.0;

        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        Func<DMesh3, DMesh3> cap = m => {
            var remove = new List<int>();
            foreach (int t in m.TriangleIndices()) if (m.GetTriCentroid(t).z > 0.5) remove.Add(t);
            MeshEditor.RemoveTriangles(m, remove, true);
            return m;
        };
        var shapes = new List<Shape>();
        if (sharp) {
            shapes.Add(new Shape { Name = "sharp-box", S = 0.1 });
            shapes.Add(new Shape { Name = "sharp-cylinder", S = 0.1 });
            shapes.Add(new Shape { Name = "l-profile", S = 0.08 });
            shapes.Add(new Shape { Name = "gear", S = 0.06 });
        } else {
            shapes.AddRange(new[] {
                new Shape { Name = "sphere", F = sphere, S = 0.1 },
                new Shape { Name = "torus", F = torus, S = 0.1 },
                new Shape { Name = "bumpy", F = bumpy, S = 0.06 },
                new Shape { Name = "filleted-box", F = rbox, S = 0.08 },
                new Shape { Name = "noisy-sphere", F = sphere, S = 0.1, Noise = 0.2 },
                new Shape { Name = "sphere-cap", F = sphere, S = 0.1, Post = cap },
                new Shape { Name = "open-patch", F = null, S = 0.04 },
            });
            if (A.ContainsKey("big")) {
                // about n vertices: a sphere with edge h gives 4 pi / (0.866 h^2) / 2 vertices
                double n = double.Parse(A["big"], CultureInfo.InvariantCulture);
                double h = Math.Sqrt(2 * Math.PI / (0.866 * n) * 2) ;
                shapes.Add(new Shape { Name = "big-sphere", F = sphere, S = 2 * h });
            }
        }

        if (sharp) {
            Console.WriteLine("| shape | input V | s | crease edges | faces | quads % | irreg. V | manifold | feat cov % | feat ang p95 (deg) | feat Haus / s | surf Haus / s | corner p95 (deg) | t field (s) | t pos (s) | t ext (s) |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        } else {
            Console.WriteLine("| shape | input V | s | singular faces | faces | quads % | tris | 5-6 | irreg. V | irreg / sing. | boundary V | manifold | corner dev p50 / p95 (deg) | aspect p95 | Hausdorff out->in / s | t field (s) | t position (s) | t extract (s) | levels / colours / sweeps | collapsed / valence 2 / dropped |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        }
        {   // warm-up
            var w = MakeInput(new Shape { Name = "sphere", F = sphere, S = 0.1 }, 0.2, 0);
            var wf = new CrossField(w); wf.Compute();
            new QuadRemesher(w) { Field = wf, TargetEdgeLength = 0.4 }.Compute();
        }
        foreach (var sh in shapes) {
            if (only != null && !only.IsMatch(sh.Name)) continue;
            double s = sh.S * scaleMul;
            DMesh3 input;
            if (sharp) input = MakeSharpInput(sh.Name, s * ratio, creaseAngle);
            else input = sh.Name == "open-patch" ? OpenPatch((int)Math.Round(1.0 / (s * ratio))) : MakeInput(sh, s * ratio, sh.Noise);

            var sw = Stopwatch.StartNew();
            var cf = new CrossField(input) { CurvatureAlignment = align, AlignCreases = creases, CreaseAngleDegrees = creaseAngle }; cf.Compute();
            double tField = sw.Elapsed.TotalSeconds;
            if (!cf.Success) { Console.WriteLine("| " + sh.Name + " | " + input.VertexCount + " | cross field " + cf.Status + " |"); continue; }

            var qr = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, Sweeps = sweeps, Parallel = !serial, Cleanup = cleanup, MismatchLimit = mm, MiddleRegularization = eps, SizeAdaptation = (SizeAdaptationMode)adapt, PreserveCreases = creases, CreaseAngleDegrees = creaseAngle };
            if (collapse >= 0) qr.CollapseLength = collapse;
            if (adapt != 0 && (A.ContainsKey("iters") || A.ContainsKey("tol") || A.ContainsKey("maxratio") || A.ContainsKey("damp"))) {
                var frames = new IntegrableFrames(null, null);
                if (A.ContainsKey("iters")) frames.Iterations = int.Parse(A["iters"]);
                if (A.ContainsKey("tol")) frames.StopTolerance = double.Parse(A["tol"], CultureInfo.InvariantCulture);
                if (A.ContainsKey("maxratio")) frames.MaxSpacingRatio = double.Parse(A["maxratio"], CultureInfo.InvariantCulture);
                if (A.ContainsKey("damp")) frames.Damping = double.Parse(A["damp"], CultureInfo.InvariantCulture);
                qr.Frames = frames;
            }
            qr.Compute();
            double tPos = qr.PositionSeconds, tExt = qr.ExtractSeconds;
            if (A.ContainsKey("digest") && qr.Success) Console.Error.WriteLine($"  {sh.Name}: digest {Digest(qr.Result):x16}");
            if (adapt != 0) Console.Error.WriteLine($"  {sh.Name}: size adaptation {(qr.SizeAdaptationApplied ? "applied" : "NOT applied")}, curl {qr.CurlBefore:F1} -> {qr.CurlAfter:F1}, spacing {qr.MinSpacing:F2}..{qr.MaxSpacing:F2}, solve {qr.SizeSeconds:F2} s");
            if (!qr.Success) { Console.WriteLine("| " + sh.Name + " | " + input.VertexCount + " | quad remesh " + qr.Status + " " + qr.Error + " |"); continue; }
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            int[] map; var skipped = new List<int>();
            DMesh3 tri = qr.Result.ToDMesh3(out map, skipped);
            double haus = double.NaN;
            if (tri.TriangleCount > 0) {
                var hd = new MeshHausdorffDistance(tri, input) { Symmetric = false, RelativeTolerance = 1e-3 };
                hd.Compute();
                haus = hd.UpperBound / s;
            }
            if (A.ContainsKey("diag")) Diag(qr.Result, s);
            if (sharp) {
                FeatureMetrics(input, qr.Result, s, creaseAngle, out int creaseCount, out double featCov, out double featAng95, out double featHaus, A.ContainsKey("diag") ? qr : null);
                Console.WriteLine($"| {sh.Name} | {input.VertexCount} | {F(s, "G3")} | {creaseCount} | {m.Faces} | {F(100.0 * m.QuadFraction, "F1")} | {m.IrregularVertices} | {(m.IsManifold ? "yes" : "NO (" + m.NonManifoldEdges + "e " + m.InconsistentEdges + "i " + m.NonManifoldVertices + "v)")} | {F(100.0 * featCov, "F1")} | {F(featAng95, "F1")} | {F(featHaus, "F3")} | {F(haus, "F2")} | {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), "F1")} | {F(tField, "F2")} | {F(tPos, "F2")} | {F(tExt, "F2")} |");
            } else {
                string sing = cf.SingularFaces.Count.ToString(CultureInfo.InvariantCulture);
                Console.WriteLine($"| {sh.Name} | {input.VertexCount} | {F(s, "G3")} | {sing} | {m.Faces} | {F(100.0 * m.QuadFraction, "F1")} | {m.Triangles} | {m.Pentagons + m.Hexagons} | {m.IrregularVertices} | {(cf.SingularFaces.Count == 0 ? "-" : F((double)m.IrregularVertices / cf.SingularFaces.Count, "F1"))} | {m.BoundaryVertices} | {(m.IsManifold ? "yes" : "NO (" + m.NonManifoldEdges + "e " + m.InconsistentEdges + "i " + m.NonManifoldVertices + "v)")} | {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.5), "F1")} / {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), "F1")} | {F(PolyMeshMetrics.Percentile(m.Aspects, 0.95), "F2")} | {F(haus, "F2")} | {F(tField, "F2")} | {F(tPos, "F2")} | {F(tExt, "F2")} | {qr.Positions.Levels} / {qr.Positions.Colors} / {qr.Positions.SweepsRun} | {qr.CollapsedEdges} / {qr.RemovedValence2} / {qr.DroppedFaces} |");
            }
        }
        return 0;
    }

    // FNV-1a over the bits of the vertex positions and the face indices: equal digests mean the same mesh bit for bit
    static ulong Digest(DPolyMesh r)
    {
        ulong h = 14695981039346656037UL;
        for (int v = 0; v < r.VertexCount; ++v) {
            Vector3d p = r.GetVertex(v);
            h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(p.x)) * 1099511628211UL;
            h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(p.y)) * 1099511628211UL;
            h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(p.z)) * 1099511628211UL;
        }
        for (int f = 0; f < r.FaceCount; ++f) { h = (h ^ 0xffffffffUL) * 1099511628211UL; foreach (int v in r.GetFace(f)) h = (h ^ (ulong)v) * 1099511628211UL; }
        return h;
    }

    static void Diag(DPolyMesh r, double s)
    {
        var val = new Dictionary<int, int>(); var edges = new HashSet<long>(); var len = new List<double>();
        int nv = r.VertexCount;
        for (int f = 0; f < r.FaceCount; ++f) {
            int[] fv = r.GetFace(f);
            for (int k = 0; k < fv.Length; ++k) {
                int a = fv[k], b = fv[(k + 1) % fv.Length];
                long key = a < b ? (long)a * nv + b : (long)b * nv + a;
                if (edges.Add(key)) { len.Add((r.GetVertex(a) - r.GetVertex(b)).Length / s); val.TryGetValue(a, out int ca); val[a] = ca + 1; val.TryGetValue(b, out int cb); val[b] = cb + 1; }
            }
        }
        len.Sort();
        Console.WriteLine("  valence: " + string.Join(" ", val.Values.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine("  edge/s p1 p5 p50 p95 p99 max: " + string.Join(" ", new[] { 0.01, 0.05, 0.5, 0.95, 0.99, 1.0 }.Select(q => F(len[Math.Min(len.Count - 1, (int)(q * len.Count))], "F2"))));
    }

    static DMesh3 OpenPatch(int n)
    {
        var m = new DMesh3();
        int[,] ids = new int[n + 1, n + 1];
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i) {
                double x = (double)i / n, y = (double)j / n;
                ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.15 * Math.Sin(3 * x) * Math.Cos(2 * y)));
            }
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
            }
        return m;
    }

    static DMesh3 MakeInput(Shape sh, double h, double noise)
    {
        var B = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
        var mc = new MarchingCubes { Implicit = new Fn { F = sh.F, B = B }, Bounds = B, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
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
            foreach (int v in m.VertexIndices()) {
                Vector3d n = (Vector3d)m.GetVertexNormal(v);
                m.SetVertex(v, m.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * noise * h));
            }
        }
        if (sh.Post != null) m = sh.Post(m);
        return m;
    }

    static DMesh3 MakeSharpInput(string name, double h, double creaseAngle)
    {
        DMesh3 m;
        switch (name) {
            case "sharp-box": {
                var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 0.7, 0.5)), EdgeVertices = 5 };
                g.Generate();
                m = g.MakeDMesh();
                break;
            }
            case "sharp-cylinder": {
                var g = new CappedCylinderGenerator { BaseRadius = 0.6f, TopRadius = 0.6f, Height = 1.6f, Slices = 48, NoSharedVertices = false };
                g.Generate();
                m = g.MakeDMesh();
                break;
            }
            case "l-profile": {
                var poly = new Polygon2d(new[] {
                    new Vector2d(0, 0), new Vector2d(1.2, 0), new Vector2d(1.2, 0.4),
                    new Vector2d(0.4, 0.4), new Vector2d(0.4, 1.2), new Vector2d(0, 1.2)
                });
                m = ExtrudePolygon(poly, 0.6);
                break;
            }
            case "gear": {
                int teeth = 8;
                double r0 = 0.6, r1 = 0.85;
                var pts = new List<Vector2d>();
                for (int k = 0; k < teeth; ++k) {
                    double a0 = 2 * Math.PI * k / teeth;
                    double step = 2 * Math.PI / (teeth * 4);
                    pts.Add(new Vector2d(r0 * Math.Cos(a0), r0 * Math.Sin(a0)));
                    pts.Add(new Vector2d(r0 * Math.Cos(a0 + step), r0 * Math.Sin(a0 + step)));
                    pts.Add(new Vector2d(r1 * Math.Cos(a0 + 1.4 * step), r1 * Math.Sin(a0 + 1.4 * step)));
                    pts.Add(new Vector2d(r1 * Math.Cos(a0 + 2.6 * step), r1 * Math.Sin(a0 + 2.6 * step)));
                }
                m = ExtrudePolygon(new Polygon2d(pts.ToArray()), 0.4);
                break;
            }
            default:
                throw new ArgumentException(name);
        }
        DMesh3 src = new DMesh3(m);
        var r = new Remesher(m) { SmoothSpeedT = 0.5, PreventNormalFlips = true, MinTriangleQuality = 0.2 };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
        r.SetTargetEdgeLength(h);
        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, creaseAngle);
        for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
        return new DMesh3(m, true);
    }

    static DMesh3 ExtrudePolygon(Polygon2d poly, double height)
    {
        var gen = new TriangulatedPolygonGenerator { Polygon = new GeneralPolygon2d(poly), Subdivisions = 2 };
        gen.Generate();
        DMesh3 m = gen.MakeDMesh();
        var ext = new MeshExtrudeMesh(m) {
            ExtrudedPositionF = (v, n, id) => v + new Vector3d(0, 0, height),
            IsPositiveOffset = true
        };
        ext.Extrude();
        return new DMesh3(m, true);
    }

    static double PointSegmentDist(Vector3d p, Vector3d a, Vector3d b)
    {
        Vector3d ab = b - a;
        double l2 = ab.LengthSquared;
        if (!(l2 > 0)) return p.Distance(a);
        double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / l2));
        return p.Distance(a + t * ab);
    }

    static void FeatureMetrics(DMesh3 input, DPolyMesh result, double s, double creaseAngle, out int creaseCount, out double coverage, out double angleP95, out double featHausdorffOverS, QuadRemesher diagQr = null)
    {
        var topo = new MeshTopology(input) { CreaseAngle = creaseAngle };
        topo.Compute();
        creaseCount = topo.CreaseEdges.Count;
        var outEdges = new List<(Vector3d a, Vector3d b, Vector3d dir)>();
        var seen = new HashSet<long>();
        int nv = result.VertexCount;
        for (int f = 0; f < result.FaceCount; ++f) {
            int[] fv = result.GetFace(f);
            for (int k = 0; k < fv.Length; ++k) {
                int a = fv[k], b = fv[(k + 1) % fv.Length];
                long key = a < b ? (long)a * nv + b : (long)b * nv + a;
                if (!seen.Add(key)) continue;
                Vector3d pa = result.GetVertex(a), pb = result.GetVertex(b);
                Vector3d d = pb - pa; double l = d.Length;
                if (l > 0) outEdges.Add((pa, pb, d / l));
            }
        }
        double totalLen = 0, coveredLen = 0, maxDist = 0;
        Vector3d worstPt = Vector3d.Zero; int worstEid = -1;
        var angles = new List<double>();
        foreach (int eid in topo.CreaseEdges) {
            Index2i ev = input.GetEdgeV(eid);
            Vector3d u = input.GetVertex(ev.a), v = input.GetVertex(ev.b);
            Vector3d d = v - u; double len = d.Length;
            if (!(len > 0)) continue;
            Vector3d dir = d / len;
            totalLen += len;
            double dEndMax = 0, ang = 90;
            foreach (double t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }) {
                Vector3d q = (1 - t) * u + t * v;
                double best = double.MaxValue;
                double bestAng = 90;
                for (int i = 0; i < outEdges.Count; ++i) {
                    double dist = PointSegmentDist(q, outEdges[i].a, outEdges[i].b);
                    if (dist < best - 1e-9 * s) {
                        best = dist;
                        if (t == 0.5) {
                            double dot = Math.Max(0, Math.Min(1, Math.Abs(dir.Dot(outEdges[i].dir))));
                            bestAng = Math.Acos(dot) * 180.0 / Math.PI;
                        }
                    } else if (t == 0.5 && dist <= best + 1e-9 * s) {
                        double dot = Math.Max(0, Math.Min(1, Math.Abs(dir.Dot(outEdges[i].dir))));
                        double candAng = Math.Acos(dot) * 180.0 / Math.PI;
                        if (candAng < bestAng) bestAng = candAng;
                    }
                }
                if (best > maxDist) { maxDist = best; worstPt = q; worstEid = eid; }
                if (best > dEndMax) dEndMax = best;
                if (t == 0.5) ang = bestAng;
            }
            angles.Add(ang);
            if (dEndMax <= 0.1 * s && ang <= 10.0) coveredLen += len;
        }
        if (diagQr != null && worstEid >= 0) {
            Index2i ev = input.GetEdgeV(worstEid);
            Index2i et = input.GetEdgeT(worstEid);
            double dih = Math.Acos(Math.Max(-1, Math.Min(1, input.GetTriNormal(et.a).Dot(input.GetTriNormal(et.b))))) * 180 / Math.PI;
            Vector3d va = input.GetVertex(ev.a), vb = input.GetVertex(ev.b);
            Vector3d oa = diagQr.Positions.Origin(ev.a), ob = diagQr.Positions.Origin(ev.b);
            Vector3d qa = diagQr.Positions.Orientation(ev.a), qb = diagQr.Positions.Orientation(ev.b);
            Console.Error.WriteLine($"  diag: dropped={diagQr.DroppedFaces} collapsed={diagQr.CollapsedEdges} val2={diagQr.RemovedValence2} worstEid={worstEid} dih={dih:F1}deg len={va.Distance(vb)/s:F2}s pt=({worstPt.x:F3},{worstPt.y:F3},{worstPt.z:F3}) dist={maxDist/s:F3}s");
            Console.Error.WriteLine($"    va=({va.x:F3},{va.y:F3},{va.z:F3}) oa=({oa.x:F3},{oa.y:F3},{oa.z:F3}) qa=({qa.x:F2},{qa.y:F2},{qa.z:F2}) | vb=({vb.x:F3},{vb.y:F3},{vb.z:F3}) ob=({ob.x:F3},{ob.y:F3},{ob.z:F3}) qb=({qb.x:F2},{qb.y:F2},{qb.z:F2}) | |oa-ob|/s={oa.Distance(ob)/s:F2}");
        }
        coverage = totalLen > 0 ? coveredLen / totalLen : 1.0;
        angleP95 = angles.Count > 0 ? PolyMeshMetrics.Percentile(angles.ToArray(), 0.95) : 0;
        featHausdorffOverS = maxDist / s;
    }
}
