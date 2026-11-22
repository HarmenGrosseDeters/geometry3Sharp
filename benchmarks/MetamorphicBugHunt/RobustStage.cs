using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Three relations across the operations of the second hunt, and the reduced cases of its findings.
    /// Stage robust: (1) a mesh with gaps in its ids is the same input as its compact copy; (2) a field multiplied by a positive
    /// power of two has the same level set; (3) the input scaled by 2^e far beyond 2^10: the result is the scaled result bit
    /// for bit, or a failure status (an INFO line names the first exponents where a module stops being exact; a success with
    /// another result is a finding, see findings2).
    /// Stage findings2: H2-F1 (TpmsLattice, a domain mesh with id gaps), H2-F2 (DualContouring, a field of small values),
    /// H2-F3 (success with a wrong result outside an undocumented range). Its FAIL lines stay until the issues are fixed.
    /// </summary>
    public static class RobustStage
    {
        const string S = "robust";

        /// <summary> the same triangles with unused vertex ids before and between the vertices, and one removed triangle id </summary>
        public static DMesh3 Gaps(DMesh3 m, out Dictionary<int, int> map, Vector3d? stale = null)
        {
            var g = new DMesh3();
            map = new Dictionary<int, int>();
            var junk = new List<int>();
            int k = 0;
            foreach (int v in m.VertexIndices()) {
                if (k++ % 3 == 0) junk.Add(g.AppendVertex(stale ?? new Vector3d(1e3 + k, -7, 5)));
                map[v] = g.AppendVertex(m.GetVertex(v));
            }
            int e0 = g.AppendVertex(new Vector3d(50, 0, 0)), e1 = g.AppendVertex(new Vector3d(51, 0, 0)), e2 = g.AppendVertex(new Vector3d(50, 1, 0));
            int first = g.AppendTriangle(e0, e1, e2);
            foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); g.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]); }
            g.RemoveTriangle(first, true, false);
            foreach (int v in junk) g.RemoveVertex(v, true, false);
            return g;
        }

        sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box;
            public double Value(ref Vector3d p) => F(p);
            public AxisAlignedBox3d Bounds() => Box;
        }

        static readonly AxisAlignedBox3d UnitBox = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
        static double Sdf(Vector3d p) => (p - new Vector3d(0.03, -0.02, 0.05)).Length - 1;

        static DMesh3 Mc(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, double iso = 0)
        {
            var mc = new MarchingCubes { Implicit = new Field { F = f, Box = box }, Bounds = box, CubeSize = cell, IsoValue = iso };
            mc.Generate();
            return mc.Mesh;
        }

        static DMesh3 Dc(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, bool simplify = true, bool insideIsLess = true, double iso = 0)
        {
            var dc = new DualContouring { Implicit = new Field { F = f, Box = box }, Bounds = box, CellSize = cell, InsideIsLess = insideIsLess, Simplify = simplify, IsoValue = iso };
            dc.Generate();
            return dc.Mesh;
        }

        static void IdGaps()
        {
            DMesh3 sphere = Shapes.Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 10), tube = Wrap2Stage.Tube(10, 1, 0.5, 0.4);
            foreach (var (name, m) in new[] { ("sphere", sphere), ("tube", tube) }) {
                string c = "id gaps " + name;
                try {
                    DMesh3 g = Gaps(m, out Dictionary<int, int> map);
                    Program.Report(S, c + " | the gap mesh is the same clean solid", !g.IsCompact && Checks.Solid(g) == "" && Checks.Volume(g) == Checks.Volume(m) ? "" : "the helper is wrong");
                    var wa = new MeshAlphaWrap(new DMesh3(m)); wa.Compute(); var wb = new MeshAlphaWrap(g); wb.Compute();
                    Program.Report(S, c + " | MeshAlphaWrap", wa.Status == wb.Status ? BooleanStage.SameBits(wa.Result, wb.Result, 1) : $"{wa.Status} vs {wb.Status}");
                    var ta = new MeshTetrahedralization(new DMesh3(m)) { RelativeAlpha = 20 }; ta.Compute(); var tb = new MeshTetrahedralization(g) { RelativeAlpha = 20 }; tb.Compute();
                    Program.Report(S, c + " | MeshTetrahedralization", ta.Status == tb.Status && tb.Result != null && ta.Result.Tets.Count == tb.Result.Tets.Count && ta.Result.Vertices.SequenceEqual(tb.Result.Vertices) ? "" : $"{ta.Status} vs {tb.Status}, or other tets");
                    var qa = new QuadRemesher(new DMesh3(m)) { TargetEdgeLength = 0.25 }; qa.Compute(); var qb = new QuadRemesher(g) { TargetEdgeLength = 0.25 }; qb.Compute();
                    Program.Report(S, c + " | QuadRemesher", qa.Status == qb.Status ? (qb.Result == null ? "" : PolyChecks.Manifold(qb.Result, out _, out _)) : $"{qa.Status} vs {qb.Status}");
                    if (qa.Status == QuadRemeshStatus.Success && qa.Result.FaceCount != qb.Result.FaceCount) Program.Info(S, c + " | QuadRemesher", $"{qb.Result.FaceCount} faces vs {qa.Result.FaceCount} (another vertex order, O3)");
                    var pin = new PassagePin(new[] { new Vector3d(0, 0, -0.8), new Vector3d(0, 0, 0.8) }, 0.2);
                    if (name == "tube") {
                        var pa = new PassageWrap(new DMesh3(m)); pa.Pins.Add(pin); pa.Compute(); var pb = new PassageWrap(g); pb.Pins.Add(pin); pb.Compute();
                        Program.Report(S, c + " | PassageWrap", pa.Status == pb.Status ? BooleanStage.SameBits(pa.Result, pb.Result, 1) : $"{pa.Status} vs {pb.Status}");
                    }
                    var ra = new MeshRobustRepair(new DMesh3(m)) { AlphaWrapSharpFeatures = true }; var rb = new MeshRobustRepair(g) { AlphaWrapSharpFeatures = true };
                    bool oa = ra.Apply(), ob = rb.Apply();
                    Program.Report(S, c + " | MeshRobustRepair", oa == ob && ra.ResultPath == rb.ResultPath ? Program.Rel("volume", Checks.Volume(rb.Mesh), Checks.Volume(ra.Mesh), 1e-12) : $"{oa} {ra.ResultPath} vs {ob} {rb.ResultPath}");
                    int s = m.VertexIndices().First(), t = m.VertexIndices().ElementAt(m.VertexCount / 2);
                    var ga = new PolyhedralGeodesicDistance(m); ga.Compute(new[] { s }); var gb = new PolyhedralGeodesicDistance(g); var gs = gb.Compute(new[] { map[s] });
                    string same = gs == PolyhedralGeodesicStatus.Ok ? "" : gs.ToString();
                    foreach (int v in m.VertexIndices()) if (same == "" && ga.Distance[v] != gb.Distance[map[v]]) same = "vertex " + v;
                    Program.Report(S, c + " | PolyhedralGeodesicDistance", same);
                    Program.Report(S, c + " | TracePath", Program.Rel("length", gb.TracePath(map[t]).Length, ga.TracePath(t).Length, 0));
                    var ha = new MeshGeodesicDistance(m); ha.Compute(new[] { s }); var hb = new MeshGeodesicDistance(g); bool hok = hb.Compute(new[] { map[s] });
                    same = hok ? "" : "Compute() false";
                    foreach (int v in m.VertexIndices()) if (same == "" && Math.Abs(ha.Distance[v] - hb.Distance[map[v]]) > 1e-12) same = $"vertex {v}: {ha.Distance[v]:R} vs {hb.Distance[map[v]]:R}";
                    Program.Report(S, c + " | MeshGeodesicDistance", same);
                    var fa = new MeshGeodesicPath(m); fa.Compute(s, t); var fb = new MeshGeodesicPath(g); fb.Compute(map[s], map[t]);
                    Program.Report(S, c + " | MeshGeodesicPath", fa.Status == fb.Status ? Program.Rel("length", fb.Length, fa.Length, 0) : $"{fa.Status} vs {fb.Status}");
                    var ba = new MeshGeodesicBezier(m); ba.Compute(new[] { s, m.VertexIndices().Last(), t }, 6); var bb = new MeshGeodesicBezier(g); bb.Compute(new[] { map[s], map[m.VertexIndices().Last()], map[t] }, 6);
                    Program.Report(S, c + " | MeshGeodesicBezier", ba.Status == bb.Status ? Program.Rel("length", bb.Length, ba.Length, 0) : $"{ba.Status} vs {bb.Status}");
                    var va = new VectorHeatMethod(m); Vector2d[] xa = va.TransportVector(s, new Vector2d(1, 0)); var vb = new VectorHeatMethod(g); Vector2d[] xb = vb.TransportVector(map[s], new Vector2d(1, 0));
                    double w = 0; foreach (int v in m.VertexIndices()) w = Math.Max(w, (va.Triangulation.ToWorld(v, xa[v]) - vb.Triangulation.ToWorld(map[v], xb[map[v]])).Length);
                    Program.Report(S, c + " | VectorHeatMethod", w < 1e-9 ? "" : $"transported vectors {w:E2} apart");
                    var ia = new IntrinsicTriangulation(m); var ib = new IntrinsicTriangulation(g);
                    Program.Report(S, c + " | IntrinsicTriangulation", ia.FlipToDelaunay() == ib.FlipToDelaunay() && ia.VertexCount == ib.VertexCount ? "" : "other flips");
                    IntrinsicRefinementResult rfa = new IntrinsicTriangulation(m).DelaunayRefine(25), rfb = new IntrinsicTriangulation(g).DelaunayRefine(25);
                    Program.Report(S, c + " | DelaunayRefine", rfa.Status == rfb.Status && rfa.InsertedVertices == rfb.InsertedVertices ? "" : $"{rfa} vs {rfb}");
                    // (TpmsLattice.DomainMesh: finding H2-F1, stage findings2)
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        static void ValueScale()
        {
            // c f has the level set of f: MarchingCubes for every c, DualContouring while the gradient stays above 2.2e-16 (H2-F2)
            DMesh3 mc0 = Mc(Sdf, UnitBox, 0.125), dc0 = Dc(Sdf, UnitBox, 0.125), dn0 = Dc(Sdf, UnitBox, 0.125, false);
            foreach (int e in new[] { -300, -150, -100, -52, -40, -20, 20, 100, 300 }) {
                double c = Math.Pow(2, e);
                Program.Report(S, $"field x 2^{e} | MarchingCubes", BooleanStage.SameBits(mc0, Mc(p => c * Sdf(p), UnitBox, 0.125), 1));
                if (e < -48 || e > 120) continue;
                Program.Report(S, $"field x 2^{e} | DualContouring", BooleanStage.SameBits(dc0, Dc(p => c * Sdf(p), UnitBox, 0.125), 1));
                Program.Report(S, $"field x 2^{e} | DualContouring, Simplify off", BooleanStage.SameBits(dn0, Dc(p => c * Sdf(p), UnitBox, 0.125, false), 1));
            }
        }

        /// <summary> what an operation gives for the input scaled by 2^e: "exact" (the scaled reference bit for bit), a failure status, or something else </summary>
        public static IEnumerable<(string name, Func<double, string> run, string failure)> ScaleProbes()
        {
            DMesh3 sphere = Shapes.Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 6), fine = Shapes.Sphere(Vector3d.Zero, 1, 12), tube = Wrap2Stage.Tube(10, 1, 0.5, 0.4);
            var w0 = new MeshAlphaWrap(new DMesh3(sphere)); w0.Compute();
            yield return ("MeshAlphaWrap", s => { var w = new MeshAlphaWrap(Shapes.Map(sphere, p => p * s)); w.Compute(); return w.Status != MeshAlphaWrap.WrapStatus.Success ? "status " + w.Status : BooleanStage.SameBits(w0.Result, w.Result, s) == "" ? "exact" : "another result"; }, "status");
            var t0 = new MeshTetrahedralization(new DMesh3(sphere)) { RelativeAlpha = 20 }; t0.Compute();
            yield return ("MeshTetrahedralization", s => { var t = new MeshTetrahedralization(Shapes.Map(sphere, p => p * s)) { RelativeAlpha = 20 }; t.Compute(); return t.Status != MeshTetrahedralization.TetStatus.Success ? "status " + t.Status : t.Result.Vertices.Select(p => p / s).SequenceEqual(t0.Result.Vertices) ? "exact" : "another result"; }, "status");
            var pw0 = new PassageWrap(new DMesh3(tube)); pw0.Pins.Add(new PassagePin(new[] { new Vector3d(0, 0, -0.8), new Vector3d(0, 0, 0.8) }, 0.2)); pw0.Compute();
            yield return ("PassageWrap", s => { var p = new PassageWrap(Shapes.Map(tube, q => q * s)); p.Pins.Add(new PassagePin(new[] { new Vector3d(0, 0, -0.8) * s, new Vector3d(0, 0, 0.8) * s }, 0.2 * s)); p.Compute(); return p.Status != PassageWrap.PassageStatus.Success ? "status " + p.Status : BooleanStage.SameBits(pw0.Result, p.Result, s) == "" ? "exact" : "another result"; }, "status");
            var q0 = new QuadRemesher(new DMesh3(fine)) { TargetEdgeLength = 0.2 }; q0.Compute();
            yield return ("QuadRemesher", s => { var q = new QuadRemesher(Shapes.Map(fine, p => p * s)) { TargetEdgeLength = 0.2 * s }; q.Compute(); return q.Status != QuadRemeshStatus.Success ? "status " + q.Status : q.Result.FaceCount == q0.Result.FaceCount && Enumerable.Range(0, q.Result.VertexCount).All(v => q.Result.GetVertex(v) == q0.Result.GetVertex(v) * s) ? "exact" : "another result"; }, "status");
            var g0 = new PolyhedralGeodesicDistance(sphere); g0.Compute(new[] { 0 });
            yield return ("PolyhedralGeodesicDistance", s => { var g = new PolyhedralGeodesicDistance(Shapes.Map(sphere, p => p * s)); var st = g.Compute(new[] { 0 }); return st != PolyhedralGeodesicStatus.Ok ? "status " + st : g.Distance.Select(d => d / s).SequenceEqual(g0.Distance) ? "exact" : $"another result ({(g.Distance[5] / s - g0.Distance[5]) / g0.Distance[5]:E1} relative)"; }, "status");
            var h0 = new MeshGeodesicDistance(sphere); h0.Compute(new[] { 0 });
            yield return ("MeshGeodesicDistance", s => { var h = new MeshGeodesicDistance(Shapes.Map(sphere, p => p * s)); bool ok = h.Compute(new[] { 0 }); return !ok ? "status false" : h.Distance.Select(d => d / s).SequenceEqual(h0.Distance) ? "exact" : double.IsNaN(h.Distance[5]) ? "another result (NaN)" : "another result"; }, "status");
            var f0 = new MeshGeodesicPath(sphere); f0.Compute(0, 77);
            yield return ("MeshGeodesicPath", s => { var f = new MeshGeodesicPath(Shapes.Map(sphere, p => p * s)); var st = f.Compute(0, 77); return st != GeodesicPathStatus.Success ? "status " + st : f.Length == f0.Length * s ? "exact" : "another result"; }, "status");
            IntrinsicRefinementResult r0 = new IntrinsicTriangulation(sphere).DelaunayRefine(25);
            yield return ("IntrinsicTriangulation.DelaunayRefine", s => { IntrinsicRefinementResult r = new IntrinsicTriangulation(Shapes.Map(sphere, p => p * s)).DelaunayRefine(25); return r.Status != IntrinsicRefinementStatus.Success ? "status " + r.Status : r.MinAngle == r0.MinAngle && r.InsertedVertices == r0.InsertedVertices ? "exact" : $"another result (MinAngle {r.MinAngle:F1})"; }, "status");
            Vector2d[] v0 = new VectorHeatMethod(sphere).TransportVector(0, new Vector2d(1, 0));
            yield return ("VectorHeatMethod", s => { Vector2d[] v; try { v = new VectorHeatMethod(Shapes.Map(sphere, p => p * s)).TransportVector(0, new Vector2d(1, 0)); } catch (InvalidOperationException) { return "status InvalidOperationException"; } return v.SequenceEqual(v0) ? "exact" : Enumerable.Range(0, v.Length).All(i => (v[i] - v0[i]).Length < 1e-6) ? "exact up to rounding" : $"another result (length {v[5].Length:F3})"; }, "status");
            DMesh3 mc0 = Mc(Sdf, UnitBox, 0.25), dc0 = Dc(Sdf, UnitBox, 0.25);
            yield return ("MarchingCubes", s => BooleanStage.SameBits(mc0, Mc(p => s * Sdf(p / s), new AxisAlignedBox3d(UnitBox.Min * s, UnitBox.Max * s), 0.25 * s), s) == "" ? "exact" : "another result", "status");
            yield return ("DualContouring", s => { DMesh3 m = Dc(p => s * Sdf(p / s), new AxisAlignedBox3d(UnitBox.Min * s, UnitBox.Max * s), 0.25 * s); return BooleanStage.SameBits(dc0, m, s) == "" ? "exact" : m.TriangleCount == 0 ? "another result (empty)" : "another result"; }, "status");
            var l0 = new TpmsLattice { Divisions = 8, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }; l0.Compute();
            yield return ("TpmsLattice", s => { var l = new TpmsLattice { CellSize = s, Divisions = 8, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(s, s, s)) }; l.Compute(); return l.Status != TpmsStatus.Ok ? "status " + l.Status : BooleanStage.SameBits(l0.Mesh, l.Mesh, s) == "" ? "exact" : "another result"; }, "status");
            List<Vector3d> ca = Shapes.Ngon(8, 1, 0.1).Select(p => new Vector3d(p.x, p.y, 0)).ToList(), cb = Shapes.Ngon(6, 0.8, 0.3).Select(p => new Vector3d(p.x, p.y, 1)).ToList();
            ContourLoftResult c0 = new ContourLoft { Caps = true }.Compute(ca, cb);
            yield return ("ContourLoft", s => { ContourLoftResult r = new ContourLoft { Caps = true }.Compute(ca.Select(p => p * s).ToList(), cb.Select(p => p * s).ToList()); return r.Status != ContourLoftStatus.Success ? "status " + r.Status : BooleanStage.SameBits(c0.Mesh, r.Mesh, s) == "" ? "exact" : "another result"; }, "status");
            ConstrainedMesh2Input In(double s) { var i = new ConstrainedMesh2Input(); foreach (Vector2d p in Shapes.Star(5, 1, 0.5)) i.Points.Add(p * s); i.Rings.Add(new Mesh2Ring(Enumerable.Range(0, 10).ToArray())); return i; }
            ConstrainedMesh2 m0 = new QualityMesh2().Compute(In(1)).Mesh;
            yield return ("QualityMesh2", s => { ConstrainedTriangulation2Result r; try { r = new QualityMesh2().Compute(In(s)); } catch (ArgumentException) { return "status ArgumentException"; } return r.Status != Triangulation2Status.Success ? "status " + r.Status : r.Mesh.Triangles.SequenceEqual(m0.Triangles) && r.Mesh.Vertices.Select(p => p / s).SequenceEqual(m0.Vertices) ? "exact" : "another result"; }, "status");
        }

        public static readonly int[] Exponents = { -320, -280, -260, -240, -200, -150, -140, -100, -60, -48, -30, 30, 60, 100, 200, 250, 260, 300, 400 };

        static void Scales()
        {
            foreach (var (name, run, _) in ScaleProbes()) {
                var line = new List<string>(); int lo = int.MaxValue, hi = int.MinValue; bool falseSuccess = false;
                foreach (int e in Exponents) {
                    string r;
                    try { r = Program.Timed(() => run(Math.Pow(2, e)), out long ms); if (ms >= Program.SlowMs) Program.Report(S, $"{name} at 2^{e}", "", ms); }
                    catch (Exception ex) { r = "threw " + ex.GetType().Name; }
                    if (r.StartsWith("exact")) { lo = Math.Min(lo, e); hi = Math.Max(hi, e); }
                    else { line.Add($"2^{e}: {r}"); if (r.StartsWith("another") || r.StartsWith("threw")) falseSuccess = true; }
                }
                // from 2^-100 to 2^100 every module of the hunt gives the scaled result bit for bit; DualContouring only from 2^-44
                // (H2-F2: below it the same vertices with other quad diagonals, then an empty mesh), PassageWrap from 2^-60 on this
                // input (the wrap's documented range: a nonzero coordinate of the tube, sin(pi) * 2^-100, is below 2^-128)
                int from = name == "DualContouring" ? -30 : name == "PassageWrap" ? -60 : -100;
                bool inside = true;
                foreach (int e in Exponents) if (e >= from && e <= 100 && (e < lo || e > hi)) inside = false;
                Program.Report(S, $"scale 2^e | {name} | exact from 2^{from} to 2^100", inside ? "" : $"exact only from 2^{lo} to 2^{hi}");
                Program.Info(S, $"scale 2^e | {name}", $"exact from 2^{lo} to 2^{hi}" + (line.Count > 0 ? "; outside: " + string.Join(", ", line) : "") + (falseSuccess ? "  [a success with another result: H2-F3]" : ""));
            }
        }

        public static void Run()
        {
            IdGaps();
            ValueScale();
            Scales();
        }

        // ---------------------------------------------------------------- the findings of the second hunt

        public static void Findings()
        {
            const string F = "findings2";
            // H2-F1: TpmsLattice reads the domain mesh with the ids of its compact copy
            DMesh3 box = Shapes.Box(Vector3d.Zero, Vector3d.One);
            var reference = new TpmsLattice { DomainMesh = box, CellSize = 0.5, Divisions = 8, VolumeFraction = 0.5, Style = TpmsStyle.SkeletalLow };
            reference.Compute();
            foreach (Vector3d stale in new[] { Vector3d.Zero, new Vector3d(3, 0, 0) }) {
                var gaps = new DMesh3();
                int junk = gaps.AppendVertex(stale);
                foreach (int v in box.VertexIndices()) gaps.AppendVertex(box.GetVertex(v));
                foreach (int t in box.TriangleIndices()) { Index3i tv = box.GetTriangle(t); gaps.AppendTriangle(tv.a + 1, tv.b + 1, tv.c + 1); }
                gaps.RemoveVertex(junk, true, false);
                var l = new TpmsLattice { DomainMesh = gaps, CellSize = 0.5, Divisions = 8, VolumeFraction = 0.5, Style = TpmsStyle.SkeletalLow };
                l.Compute();
                Program.Info(F, $"H2-F1 box domain with vertex ids 1..8, removed vertex at {stale}", $"domain clean '{Checks.Solid(gaps)}', volume {Checks.Volume(gaps):R}; lattice {l.Status}, {l.Mesh?.TriangleCount} triangles, volume {(l.Mesh == null ? 0 : Checks.Volume(l.Mesh)):R}, bounds {l.Mesh?.GetBounds()}; compact domain: {reference.Mesh.TriangleCount} triangles, volume {Checks.Volume(reference.Mesh):R}");
                Program.Report(F, $"H2-F1 TpmsLattice: a domain mesh with an id gap (removed vertex at {stale}) gives the lattice of its compact copy", l.Status == reference.Status && l.Mesh != null ? BooleanStage.SameBits(reference.Mesh, l.Mesh, 1) : "status " + l.Status);
            }
            // H2-F2: DualContouring loses the surface of a field whose values are small
            DMesh3 dc0 = Dc(Sdf, UnitBox, 0.125), dn0 = Dc(Sdf, UnitBox, 0.125, false), mc0 = Mc(Sdf, UnitBox, 0.125);
            foreach (int e in new[] { -50, -52, -60, -150 }) {
                double c = Math.Pow(2, e);
                DMesh3 dc = Dc(p => c * Sdf(p), UnitBox, 0.125), dn = Dc(p => c * Sdf(p), UnitBox, 0.125, false), mc = Mc(p => c * Sdf(p), UnitBox, 0.125);
                Program.Info(F, $"H2-F2 field 2^{e} x (|p| - 1)", $"MarchingCubes {mc.TriangleCount} triangles, volume {Checks.Volume(mc):R}; DualContouring {dc.TriangleCount} triangles{(dc.TriangleCount > 0 ? $", volume {Checks.Volume(dc):R}" : "")}; Simplify off {dn.TriangleCount} triangles{(dn.TriangleCount > 0 ? $", volume {Checks.Volume(dn):R}" : "")}");
                Program.Report(F, $"H2-F2 MarchingCubes: field x 2^{e} has the same level set", BooleanStage.SameBits(mc0, mc, 1));
                Program.Report(F, $"H2-F2 DualContouring: field x 2^{e} has the same level set", BooleanStage.SameBits(dc0, dc, 1));
                Program.Report(F, $"H2-F2 DualContouring, Simplify off: field x 2^{e} has the same level set", BooleanStage.SameBits(dn0, dn, 1));
            }
            foreach (double level in new[] { 1e-10, 1e-30 }) {
                var big = new AxisAlignedBox3d(new Vector3d(-2.5, -2.5, -2.5), new Vector3d(2.5, 2.5, 2.5));
                Func<Vector3d, double> gauss = p => Math.Exp(-40 * p.LengthSquared);
                double r = Math.Sqrt(-Math.Log(level) / 40), volume = 4.0 / 3 * Math.PI * r * r * r;
                DMesh3 dc = Dc(gauss, big, 0.125, true, false, level);
                Program.Report(F, $"H2-F2 DualContouring: the level {level:E0} of exp(-40 r^2) (a sphere of radius {r:F4})", dc.TriangleCount == 0 ? "empty mesh" : Program.Rel("volume", Checks.Volume(dc), volume, 0.05));
            }
            // H2-F3: a success with another result outside the range a module supports (no documented range)
            foreach (var (name, run, _) in ScaleProbes()) {
                foreach (int e in Exponents) {
                    string r;
                    try { r = run(Math.Pow(2, e)); } catch (Exception ex) { r = "threw " + ex.GetType().Name; }
                    if (r.StartsWith("another") || r.StartsWith("threw")) Program.Report(F, $"H2-F3 {name} at 2^{e}: the scaled result or a failure status", r);
                }
            }
            // O2: a geodesic Bezier curve of two control points on each other's cut locus mixes two geodesics of the same length
            {
                DMesh3 sphere = Shapes.Sphere(Vector3d.Zero, 1, 6);
                int a = 0, b = sphere.MaxVertexID / 3;
                var path = new MeshGeodesicPath(sphere); path.Compute(a, b);
                foreach (int segments in new[] { 1, 2, 4, 8 }) {
                    var bz = new MeshGeodesicBezier(sphere); bz.Compute(new[] { a, b }, segments);
                    Program.Info(F, $"O2 geodesic Bezier, two control points on the symmetry plane of the sphere mesh, {segments} segments", $"Length {bz.Length:R}, the geodesic {path.Length:R} ({(bz.Length - path.Length) / path.Length:E1} longer)");
                }
            }
            // O3: the quad remesher's face count for the same solid and target edge depends on the vertex order
            {
                DMesh3 grid = Shapes.GridBox(Vector3d.Zero, new Vector3d(1, 0.75, 0.5), 9);
                double expected = Checks.Area(grid) / (0.25 * 0.25);
                foreach (var (what, m) in new[] { ("as generated", grid), ("permuted (seed 9)", Shapes.Permute(grid, 9)), ("permuted (seed 10)", Shapes.Permute(grid, 10)), ("flipped", Flip(grid)) }) {
                    var q = new QuadRemesher(m) { TargetEdgeLength = 0.25 }; q.Compute();
                    Program.Info(F, $"O3 quad remesher, gridded box, target edge 0.25, {what}", $"{q.Status}, {q.Result?.FaceCount} faces (area / edge^2 = {expected:F0}), DroppedFaces {q.DroppedFaces}");
                }
            }
        }

        static DMesh3 Flip(DMesh3 m) { var f = new DMesh3(m); f.ReverseOrientation(); return f; }
    }
}
