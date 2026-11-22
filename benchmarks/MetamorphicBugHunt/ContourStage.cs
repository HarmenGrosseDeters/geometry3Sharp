using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// MarchingCubes, DualContouring and MeshOffset: closed and manifold, the analytic volume within the cell band, 2^k scale bit
    /// for bit, a shift by whole cells, offset monotony, and offset then inset.
    /// </summary>
    public static class ContourStage
    {
        const string S = "contour";

        sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box; public double Scale = 1; public Vector3d Shift;
            public double Value(ref Vector3d p) => Scale * F((p - Shift) / Scale);
            public AxisAlignedBox3d Bounds() => new AxisAlignedBox3d(Box.Min * Scale + Shift, Box.Max * Scale + Shift);
        }

        static double SdBox(Vector3d p, Vector3d h)
        {
            double dx = Math.Abs(p.x) - h.x, dy = Math.Abs(p.y) - h.y, dz = Math.Abs(p.z) - h.z;
            return new Vector3d(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
        }

        static IEnumerable<(string, Func<Vector3d, double>, double volume, double area)> Fields()
        {
            yield return ("sphere", p => p.Length - 1, 4.0 / 3 * Math.PI, 4 * Math.PI);
            yield return ("torus", p => Math.Sqrt(Math.Pow(Math.Sqrt(p.x * p.x + p.y * p.y) - 0.9, 2) + p.z * p.z) - 0.35, 2 * Math.PI * Math.PI * 0.9 * 0.35 * 0.35, 4 * Math.PI * Math.PI * 0.9 * 0.35);
            yield return ("box", p => SdBox(p, new Vector3d(0.9, 0.7, 0.5)), 8 * 0.9 * 0.7 * 0.5, 8 * (0.9 * 0.7 + 0.9 * 0.5 + 0.7 * 0.5));
            // two spheres that touch in one point (a singular level set), and a thin slab below the cell size
            yield return ("kissing spheres", p => Math.Min((p - new Vector3d(0.5, 0, 0)).Length, (p + new Vector3d(0.5, 0, 0)).Length) - 0.5, 2 * 4.0 / 3 * Math.PI * 0.125, 2 * Math.PI);
            yield return ("slab thinner than a cell", p => SdBox(p, new Vector3d(1, 1, 0.03)), 8 * 0.03, 8 + 16 * 0.03);
        }

        public static void Run()
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
            // (a dyadic cell: the grid nodes, and a shift by whole cells, are exact in double)
            const double cell = 0.125;
            foreach (var (n, f, volume, area) in Fields()) {
                foreach (string method in new[] { "mc", "dc" }) {
                    string tag = $"{n} {method}";
                    DMesh3 Gen(double scale, Vector3d shift, string name)
                    {
                        var field = new Field { F = f, Box = box, Scale = scale, Shift = shift };
                        try {
                            return Program.Timed(() => {
                                if (method == "mc") {
                                    var mc = new MarchingCubes { Implicit = field, Bounds = field.Bounds(), CubeSize = cell * scale };
                                    mc.Generate();
                                    return mc.Mesh;
                                }
                                var dc = new DualContouring { Implicit = field, Bounds = field.Bounds(), CellSize = cell * scale, InsideIsLess = true };
                                dc.Generate();
                                if (dc.NonManifoldTriangles != 0) Program.Info(S, name, $"reports {dc.NonManifoldTriangles} non-manifold triangles");
                                return dc.Mesh;
                            }, out long ms);
                        } catch (Exception ex) { Program.Report(S, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
                    }
                    DMesh3 m = Gen(1, Vector3d.Zero, tag);
                    if (m == null) continue;
                    bool resolved = !n.StartsWith("slab");
                    if (m.TriangleCount == 0) { Program.Report(S, tag + " not empty", resolved ? "empty result" : ""); if (!resolved) Program.Info(S, tag, "empty: the solid is thinner than a cell (lost without a report)"); continue; }
                    Program.Report(S, tag + " closed manifold", Checks.ClosedManifold(m));
                    if (Checks.ClosedManifold(m) == "") {
                        string clean = MeshRobustRepair.NotCleanReason(m);
                        if (clean != "") Program.Info(S, tag, "not clean: " + clean);
                        if (resolved) Program.Report(S, tag + " volume within the cell band", Math.Abs(Checks.Volume(m) - volume) <= area * cell ? "" : $"volume {Checks.Volume(m):R} vs analytic {volume:R}, band {area * cell:R}");
                    }
                    foreach (int k in new[] { -10, 10 }) {
                        double sc = Math.Pow(2, k);
                        DMesh3 ms = Gen(sc, Vector3d.Zero, tag + $" 2^{k}");
                        if (ms != null) Program.Report(S, tag + $" scale 2^{k} bit for bit", BooleanStage.SameBits(m, ms, sc));
                    }
                    // a shift by whole cells (exact in double here): the same mesh, shifted
                    var shift = new Vector3d(64 * cell * 5, -64 * cell * 10, 64 * cell * 15);
                    // (the field is evaluated at p - shift, exact here: the same node values, so the same mesh)
                    DMesh3 mt = Gen(1, shift, tag + " shifted");
                    if (mt != null) {
                        Program.Report(S, tag + " shifted: same counts", mt.TriangleCount == m.TriangleCount && mt.VertexCount == m.VertexCount ? "" : $"{m.VertexCount}/{m.TriangleCount} vs {mt.VertexCount}/{mt.TriangleCount} vertices/triangles");
                        Program.Report(S, tag + " shifted: same volume", Program.Rel("volume", Checks.Volume(m), Checks.Volume(mt), n.StartsWith("kissing") ? 1e-2 : 1e-9));
                        // (up to the rounding of the shifted coordinates; a singular level set under Simplify is the observation O1)
                        double worst = -1;
                        if (mt.VertexCount == m.VertexCount) { worst = 0; foreach (int v in m.VertexIndices()) worst = Math.Max(worst, (m.GetVertex(v) + shift - mt.GetVertex(v)).Length); }
                        if (worst > 1e-9) Program.Info(S, tag + " shifted", $"vertices differ by up to {worst:E2} ({worst / cell:F3} cells)");
                    }
                }
            }
            if (Environment.GetEnvironmentVariable("G3_HUNT_SKIP_OFFSET") == null) Offsets();
        }

        static void Offsets()
        {
            foreach (var (n, input) in new[] {
                ("box", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5))),
                ("sphere", Shapes.Sphere(Vector3d.Zero, 1, 8)),
                ("torus", Shapes.Torus(Vector3d.Zero, 1, 0.35, 24, 12)),
                ("star", Shapes.Prism(Shapes.Star(6, 1.2, 0.6), -0.4, 0.4)),
                ("rotated box", Shapes.Rotate(Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)), new Vector3d(1, 2, 3), 33)) }) {
                MeshOffset Off(DMesh3 m, double d, string name)
                {
                    var o = new MeshOffset(new DMesh3(m), d);
                    bool ok;
                    try { ok = Program.Timed(() => o.Compute(), out long ms); if (ms >= Program.SlowMs) Program.Report(S, name, "", ms); }
                    catch (Exception ex) { Program.Report(S, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
                    if (!ok) { Program.Info(S, name, $"status {o.ResultStatus}"); return null; }
                    return o;
                }
                double v0 = Checks.Volume(input), a0 = Checks.Area(input);
                double last = v0;
                foreach (double d in new[] { 0.05, 0.1, 0.2 }) {
                    string tag = $"offset {n} {d}";
                    MeshOffset o = Off(input, d, tag);
                    if (o == null) continue;
                    Program.Report(S, tag + " closed manifold", Checks.ClosedManifold(o.Result));
                    string clean = MeshRobustRepair.NotCleanReason(o.Result);
                    Program.Report(S, tag + " clean as reported", o.SelfIntersectionFree ? clean : "");
                    Program.Report(S, tag + " input inside", ChainStage.InputInside(input, o.Result));
                    // the band, independently: every result vertex and centroid is within BandDeviation of the distance
                    double worst = 0;
                    foreach (int vid in o.Result.VertexIndices()) worst = Math.Max(worst, Math.Abs(Checks.DistanceTo(input, o.Result.GetVertex(vid)) - d));
                    foreach (int t in o.Result.TriangleIndices()) worst = Math.Max(worst, Math.Abs(Checks.DistanceTo(input, o.Result.GetTriCentroid(t)) - d));
                    Program.Report(S, tag + " band", worst <= o.BandDeviation * (1 + 1e-9) ? "" : $"a sample is {worst:R} off the distance, BandDeviation {o.BandDeviation:R} (certified {o.BandCertified})");
                    double v = Checks.Volume(o.Result);
                    Program.Report(S, tag + " volume grows with the distance", v > last ? "" : $"volume {v:R} after {last:R}");
                    // Steiner-type bounds: v0 + area x d <= volume for a convex body; any body: volume <= v0 + area x d + (edges and corners), checked loosely
                    Program.Report(S, tag + " volume above the input's", v > v0 ? "" : $"{v:R} vs {v0:R}");
                    last = v;
                    // 2^k
                    MeshOffset os = Off(Shapes.Map(input, x => x * 1024), d * 1024, tag + " 2^10");
                    if (os != null) Program.Report(S, tag + " scale 2^10 bit for bit", BooleanStage.SameBits(o.Result, os.Result, 1024));
                    // permutation: the same solid
                    MeshOffset op = Off(Shapes.Permute(input, 4), d, tag + " permuted");
                    if (op != null) Program.Report(S, tag + " permuted volume", Program.Rel("volume", v, Checks.Volume(op.Result), 1e-6));
                    // offset then inset: back to the input up to the band and the rounding of convex edges (inside the input, volume close)
                    if (d == 0.1) {
                        MeshOffset back = Off(o.Result, -d, tag + " then inset");
                        if (back != null) {
                            Program.Report(S, tag + " then inset closed manifold", Checks.ClosedManifold(back.Result));
                            double vb = Checks.Volume(back.Result);
                            Program.Report(S, tag + " then inset volume", Math.Abs(vb - v0) <= a0 * (o.UsedCellSize + back.UsedCellSize) ? "" : $"volume {vb:R} vs input {v0:R}, more than area x cells = {a0 * (o.UsedCellSize + back.UsedCellSize):R} apart");
                        }
                    }
                }
                foreach (double d in new[] { -0.05, -0.1 }) {
                    string tag = $"inset {n} {d}";
                    MeshOffset o = Off(input, d, tag);
                    if (o == null) continue;
                    Program.Report(S, tag + " closed manifold", Checks.ClosedManifold(o.Result));
                    Program.Report(S, tag + " clean as reported", o.SelfIntersectionFree ? MeshRobustRepair.NotCleanReason(o.Result) : "");
                    Program.Report(S, tag + " inside the input", ChainStage.InputInside(o.Result, input));
                    Program.Report(S, tag + " volume below the input's", Checks.Volume(o.Result) < v0 ? "" : $"{Checks.Volume(o.Result):R} vs {v0:R}");
                }
            }
        }
    }
}
