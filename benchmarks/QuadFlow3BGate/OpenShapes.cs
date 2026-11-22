// QuadFlow3BGate open=1: open and creased shapes (Phase 4c measurement, docs/design/QUAD_REMESHING.md "4c").
//
// Per shape and variation (scale 1, 0.8, 1.25 at input edge 0.5 s, like the closed set):
//   stage 2     QuadRemesher (PreserveCreases on the creased shapes): the reference mesh and its feature metrics (feature = boundary edge or crease edge above 30 degrees)
//   pure today  QuadRemesher.PureQuads = true as the library does it (an open mesh gives FlowInfeasible: the boundary is rejected; the creases are not kept)
//   raw map     the integer map of the position field as QuadFlowRepair builds it (BuildOffsets, BuildTriangles through reflection, no flow, no library change):
//               triangles, boundary slots, triangles whose three offsets do not sum to 0, inverted triangles, the largest |d|
// With a QuadFlowRepair that has the public field FixFeatureEdges (a prototype on a runs/* branch) the attempt is repeated with it and reported as "proto".
//   dotnet run -c Release --project benchmarks/QuadFlow3BGate -- open=1 [only=<regex>] [adapt=0|1|2]
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

static class OpenShapes
{
    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    // ------------------------------------------------------------------------------------------ shapes (all procedural)

    static DMesh3 Prep(DMesh3 m, double h)
    {
        DMesh3 src = new DMesh3(m);
        var r = new Remesher(m) { SmoothSpeedT = 0.5, PreventNormalFlips = true, MinTriangleQuality = 0.2 };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
        r.SetTargetEdgeLength(h);
        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
        for (int k = 0; k < 15; ++k) r.BasicRemeshPass();
        return new DMesh3(m, true);
    }

    static DMesh3 ExtrudePolygon(Polygon2d poly, double height)
    {
        var gen = new TriangulatedPolygonGenerator { Polygon = new GeneralPolygon2d(poly), Subdivisions = 2 };
        gen.Generate();
        DMesh3 m = gen.MakeDMesh();
        new MeshExtrudeMesh(m) { ExtrudedPositionF = (v, n, id) => v + new Vector3d(0, 0, height), IsPositiveOffset = true }.Extrude();
        return new DMesh3(m, true);
    }

    static DMesh3 Grid(int nu, int nv, Func<double, double, Vector3d> f)
    {
        var m = new DMesh3(); var ids = new int[nu + 1, nv + 1];
        for (int j = 0; j <= nv; ++j) for (int i = 0; i <= nu; ++i) ids[i, j] = m.AppendVertex(f((double)i / nu, (double)j / nv));
        for (int j = 0; j < nv; ++j) for (int i = 0; i < nu; ++i) {
            m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
            m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
        }
        return m;
    }

    static DMesh3 CutTriangles(DMesh3 m, Func<Vector3d, Vector3d, bool> removeF)
    {
        var rm = new List<int>();
        foreach (int t in m.TriangleIndices()) { Vector3d c = m.GetTriCentroid(t); if (removeF(c, m.GetTriNormal(t))) rm.Add(t); }
        foreach (int t in rm) m.RemoveTriangle(t, true);
        return new DMesh3(m, true);
    }

    // (name, creased, s0, builder(h))
    static List<(string name, bool creased, double s0, Func<double, DMesh3> build)> Shapes()
    {
        Func<double, DMesh3> sphere = h => {
            var g = new Sphere3Generator_NormalizedCube { Radius = 1.0, EdgeVertices = Math.Max(8, (int)Math.Round(2.0 / h)) }; g.Generate(); return g.MakeDMesh(); };
        return new List<(string, bool, double, Func<double, DMesh3>)> {
            ("sphere-closed", false, 0.1, h => sphere(h)),
            ("open-patch", false, 0.1, h => { int n = Math.Max(8, (int)Math.Round(1.0 / h)); return Grid(n, n, (x, y) => new Vector3d(x, y, 0.15 * Math.Sin(3 * x) * Math.Cos(2 * y))); }),
            ("sphere-cap", false, 0.1, h => CutTriangles(sphere(h), (c, n) => c.z > 0.35)),
            ("open-strip", false, 0.1, h => { int nu = Math.Max(12, (int)Math.Round(3.0 / h)), nv = Math.Max(3, (int)Math.Round(0.3 / h)); return Grid(nu, nv, (x, y) => new Vector3d(3 * x, 0.3 * y, 0.2 * Math.Sin(6 * x))); }),
            ("open-tube", false, 0.1, h => { int nu = Math.Max(16, (int)Math.Round(2 * Math.PI * 0.5 / h)), nv = Math.Max(4, (int)Math.Round(1.2 / h)); return Grid(nu, nv, (x, y) => new Vector3d(0.5 * Math.Cos(2 * Math.PI * x), 0.5 * Math.Sin(2 * Math.PI * x), 1.2 * y)); }),
            ("sharp-box", true, 0.1, h => { var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 0.7, 0.5)), EdgeVertices = 5 }; g.Generate(); return Prep(g.MakeDMesh(), h); }),
            ("open-box", true, 0.1, h => { var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 0.7, 0.5)), EdgeVertices = 5 }; g.Generate(); return Prep(CutTriangles(g.MakeDMesh(), (c, n) => n.z > 0.9), h); }),
            ("sharp-cylinder", true, 0.1, h => { var g = new CappedCylinderGenerator { BaseRadius = 0.6f, TopRadius = 0.6f, Height = 1.6f, Slices = 40, NoSharedVertices = false }; g.Generate(); return Prep(g.MakeDMesh(), h); }),
            ("l-profile", true, 0.08, h => Prep(ExtrudePolygon(new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(1.2, 0), new Vector2d(1.2, 0.4), new Vector2d(0.4, 0.4), new Vector2d(0.4, 1.2), new Vector2d(0, 1.2) }), 0.6), h)),
            ("gear", true, 0.06, h => {
                int teeth = 8; double r0 = 0.6, r1 = 0.85; var pts = new List<Vector2d>();
                for (int k = 0; k < teeth; ++k) {
                    double a0 = 2 * Math.PI * k / teeth, step = 2 * Math.PI / (teeth * 4);
                    pts.Add(new Vector2d(r0 * Math.Cos(a0), r0 * Math.Sin(a0))); pts.Add(new Vector2d(r0 * Math.Cos(a0 + step), r0 * Math.Sin(a0 + step)));
                    pts.Add(new Vector2d(r1 * Math.Cos(a0 + 1.4 * step), r1 * Math.Sin(a0 + 1.4 * step))); pts.Add(new Vector2d(r1 * Math.Cos(a0 + 2.6 * step), r1 * Math.Sin(a0 + 2.6 * step)));
                }
                return Prep(ExtrudePolygon(new Polygon2d(pts.ToArray()), 0.4), h); }),
        };
    }

    // ------------------------------------------------------------------------------------------ feature metrics (boundary edges and crease edges above 30 degrees)

    static double PointSegmentDist(Vector3d p, Vector3d a, Vector3d b)
    {
        Vector3d ab = b - a; double l2 = ab.LengthSquared;
        if (!(l2 > 0)) return p.Distance(a);
        double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / l2));
        return p.Distance(a + t * ab);
    }

    static void Features(DMesh3 input, DPolyMesh result, double s, out int count, out double coverage, out double angleP95, out double haus)
    {
        var topo = new MeshTopology(input) { CreaseAngle = 30 }; topo.Compute();
        var feat = new List<int>(topo.CreaseEdges);
        foreach (int e in input.BoundaryEdgeIndices()) feat.Add(e);
        count = feat.Count; coverage = 1; angleP95 = 0; haus = 0;
        if (result == null || result.FaceCount == 0 || feat.Count == 0) { if (result == null || result.FaceCount == 0) { coverage = 0; haus = double.NaN; } return; }
        var outEdges = new List<(Vector3d a, Vector3d b, Vector3d dir)>(); var seen = new HashSet<long>(); int nv = result.VertexCount;
        for (int f = 0; f < result.FaceCount; ++f) {
            int[] fv = result.GetFace(f);
            for (int k = 0; k < fv.Length; ++k) {
                int a = fv[k], b = fv[(k + 1) % fv.Length]; long key = a < b ? (long)a * nv + b : (long)b * nv + a;
                if (!seen.Add(key)) continue;
                Vector3d pa = result.GetVertex(a), pb = result.GetVertex(b), d = pb - pa; double l = d.Length;
                if (l > 0) outEdges.Add((pa, pb, d / l));
            }
        }
        double total = 0, covered = 0, maxDist = 0; var angles = new List<double>();
        foreach (int eid in feat) {
            Index2i ev = input.GetEdgeV(eid); Vector3d u = input.GetVertex(ev.a), v = input.GetVertex(ev.b), d = v - u; double len = d.Length;
            if (!(len > 0)) continue; Vector3d dir = d / len; total += len;
            double dEnd = 0, ang = 90;
            foreach (double t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }) {
                Vector3d q = (1 - t) * u + t * v; double best = double.MaxValue, bestAng = 90;
                foreach (var oe in outEdges) {
                    double dist = PointSegmentDist(q, oe.a, oe.b);
                    if (dist < best) { best = dist; bestAng = Math.Acos(Math.Max(0, Math.Min(1, Math.Abs(dir.Dot(oe.dir))))) * 180 / Math.PI; }
                }
                maxDist = Math.Max(maxDist, best); dEnd = Math.Max(dEnd, best); if (t == 0.5) ang = bestAng;
            }
            angles.Add(ang); if (dEnd <= 0.1 * s && ang <= 10) covered += len;
        }
        coverage = total > 0 ? covered / total : 1; angleP95 = angles.Count > 0 ? PolyMeshMetrics.Percentile(angles.ToArray(), 0.95) : 0; haus = maxDist / s;
    }

    // ------------------------------------------------------------------------------------------ the raw integer map (reflection, no library change)

    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static string RawMap(DMesh3 mesh, CrossField cf, PositionField pf, double s, out int tris, out int boundarySlots, out int notClosed, out int inverted, out int maxD)
    {
        tris = boundarySlots = notClosed = inverted = maxD = 0;
        var rep = new QuadFlowRepair(mesh, cf, pf, s); Type t = typeof(QuadFlowRepair);
        t.GetMethod("BuildOffsets", NP).Invoke(rep, null);
        if ((bool)t.GetField("setupFailed", NP).GetValue(rep)) return "setup failed";
        t.GetMethod("BuildTriangles", NP).Invoke(rep, null);
        var list = (IList)t.GetField("tris", NP).GetValue(rep); tris = list.Count;
        foreach (object tr in list) {
            Type tt = tr.GetType(); var D = (Array)tt.GetField("D").GetValue(tr); var Nb = (int[])tt.GetField("Nb").GetValue(tr);
            int[] x = new int[3], y = new int[3];
            for (int k = 0; k < 3; ++k) {
                object o = D.GetValue(k); Type ot = o.GetType(); x[k] = (int)ot.GetField("X").GetValue(o); y[k] = (int)ot.GetField("Y").GetValue(o);
                maxD = Math.Max(maxD, Math.Max(Math.Abs(x[k]), Math.Abs(y[k]))); if (Nb[k] < 0) boundarySlots++;
            }
            if (x[0] + x[1] + x[2] != 0 || y[0] + y[1] + y[2] != 0) notClosed++;
            long det = (long)x[0] * y[1] - (long)y[0] * x[1]; if (det < 0) inverted++;
        }
        return "ok";
    }

    // ------------------------------------------------------------------------------------------ run

    public static int Run(Dictionary<string, string> A)
    {
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        int adaptArg = A.ContainsKey("adapt") ? int.Parse(A["adapt"], CultureInfo.InvariantCulture) : 0;
        if (adaptArg < 0 || adaptArg > 2) throw new ArgumentOutOfRangeException("adapt", "adapt must be 0, 1 or 2");
        var adaptMode = adaptArg == 1 ? SizeAdaptationMode.Scale : adaptArg == 2 ? SizeAdaptationMode.ScaleAndRotation : SizeAdaptationMode.Off;
        var variations = new (double scale, double ratio)[] { (1, 0.5), (0.8, 0.5), (1.25, 0.5) };
        bool hasProto = typeof(QuadFlowRepair).GetField("FixFeatureEdges") != null;
        Console.WriteLine("| shape | scale | input V | feat. edges | stage 2: quads % / irr. / feat cov % / ang p95 / feat Haus (s) / t (s) | pure today: status / feat cov % / feat Haus (s) | raw map: tris / bnd slots / not closed / inverted / max abs d | " + (hasProto ? "proto: status / inv. / t (s) |" : "proto |"));
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var (name, creased, s0, build) in Shapes()) {
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in variations) {
                double s = s0 * scale;
                DMesh3 input = build(s * ratio);
                var cf = new CrossField(input) { AlignCreases = creased, CreaseAngleDegrees = 30 };
                cf.Compute();
                if (!cf.Success) { Console.WriteLine($"| {name} | {scale} | cross field failed {cf.Status} |"); continue; }
                var a = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PreserveCreases = creased, SizeAdaptation = adaptMode };
                var sw = Stopwatch.StartNew(); a.Compute(); double t2 = sw.Elapsed.TotalSeconds;
                var b = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true, SizeAdaptation = adaptMode };
                b.Compute();
                string st2 = "-"; double cov2 = double.NaN, ang2 = double.NaN, hf2 = double.NaN; int fc = 0;
                if (a.Status == QuadRemeshStatus.Success) {
                    PolyMeshMetrics m2 = PolyMeshMetrics.Compute(a.Result); Features(input, a.Result, s, out fc, out cov2, out ang2, out hf2);
                    st2 = $"{F(100 * m2.QuadFraction, "F1")} / {m2.IrregularVertices} / {F(100 * cov2, "F1")} / {F(ang2, "F1")} / {F(hf2, "F2")} / {F(t2, "F2")}";
                } else st2 = a.Status.ToString();
                string stb = b.PureQuadStatus.ToString();
                if (b.PureQuadStatus == PureQuadStatus.Success) { Features(input, b.Result, s, out _, out double covb, out _, out double hfb); stb += $" / {F(100 * covb, "F1")} / {F(hfb, "F2")}"; }
                else if (b.Status != QuadRemeshStatus.Success) stb = b.Status.ToString();
                else stb += " / (stage 2 mesh)";
                var pf = new PositionField(input, cf) { TargetEdgeLength = s, PreserveCreases = creased, CreaseAngleDegrees = 30 };
                string raw;
                if (pf.Compute() != PositionFieldStatus.Success) raw = "position field failed";
                else { string r = RawMap(input, cf, pf, s, out int nt, out int bs, out int nc, out int inv, out int md); raw = r == "ok" ? $"{nt} / {bs} / {nc} / {inv} / {md}" : r; }
                string proto = "-";
                if (hasProto) {
                    var c = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true, PreserveCreases = creased, SizeAdaptation = adaptMode };
                    proto = "(prototype hook not wired)";
                }
                Console.WriteLine($"| {name} | {F(scale, "G3")} | {input.VertexCount} | {fc} | {st2} | {stb} | {raw} | {proto} |");
            }
        }
        return 0;
    }
}
