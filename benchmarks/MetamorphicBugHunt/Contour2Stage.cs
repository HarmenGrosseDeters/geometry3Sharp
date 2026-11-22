using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Second pass over the contouring (hunt 1 ran closed/manifold, analytic volumes, 2^k and whole-cell shifts): Generate() twice
    /// on one instance, serial = parallel (ids), the iso value against a shifted field, a far second component, a mirror, the
    /// negated field, every root mode; and TpmsLattice: Ok means a solid by the independent check, 2^k bit for bit, the frame
    /// (Origin, Rotation) is an equivariance, serial = parallel, reuse, the two skeletal styles fill the box together, a box
    /// domain mesh against the Box, the realized fraction near the target.
    /// </summary>
    public static class Contour2Stage
    {
        const string S = "contour2";

        sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box;
            public double Value(ref Vector3d p) => F(p);
            public AxisAlignedBox3d Bounds() => Box;
        }

        static string SameIds(DMesh3 a, DMesh3 b, double scale = 1)
        {
            if (a == null || b == null) return a == b ? "" : "one is null";
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"counts {a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount}";
            foreach (int v in a.VertexIndices()) { Vector3d p = a.GetVertex(v) * scale, q = b.GetVertex(v); if (p.x != q.x || p.y != q.y || p.z != q.z) return $"vertex {v}: {p.x:R} {p.y:R} {p.z:R} vs {q.x:R} {q.y:R} {q.z:R}"; }
            foreach (int t in a.TriangleIndices()) { Index3i x = a.GetTriangle(t), y = b.GetTriangle(t); if (x.a != y.a || x.b != y.b || x.c != y.c) return "triangle " + t; }
            return "";
        }

        static string Near(DMesh3 a, DMesh3 b, double tol)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"counts {a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount}";
            foreach (int v in a.VertexIndices()) if ((a.GetVertex(v) - b.GetVertex(v)).Length > tol) return $"vertex {v}: {(a.GetVertex(v) - b.GetVertex(v)).Length:E2} apart";
            foreach (int t in a.TriangleIndices()) { Index3i x = a.GetTriangle(t), y = b.GetTriangle(t); if (x.a != y.a || x.b != y.b || x.c != y.c) return "triangle " + t; }
            return "";
        }

        static IEnumerable<(string, Func<Vector3d, double>)> Fields()
        {
            yield return ("sphere", p => (p - new Vector3d(0.03, -0.02, 0.05)).Length - 1);
            yield return ("torus", p => Math.Sqrt(Math.Pow(Math.Sqrt(p.x * p.x + p.y * p.y) - 0.9, 2) + p.z * p.z) - 0.35);
            yield return ("two blobs", p => Math.Min((p - new Vector3d(0.55, 0.1, 0)).Length - 0.5, (p + new Vector3d(0.6, 0, 0.1)).Length - 0.45));
            yield return ("rounded cube", p => Math.Pow(Math.Pow(p.x, 4) + Math.Pow(p.y, 4) + Math.Pow(p.z, 4), 0.25) - 0.9);
            yield return ("gyroid ball", p => Math.Max(p.Length - 1.2, Math.Abs(Math.Sin(3 * p.x) * Math.Cos(3 * p.y) + Math.Sin(3 * p.y) * Math.Cos(3 * p.z) + Math.Sin(3 * p.z) * Math.Cos(3 * p.x)) / 6 - 0.08));
        }

        static DMesh3 Mc(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, bool parallel = true, double iso = 0, MarchingCubes.RootfindingModes mode = MarchingCubes.RootfindingModes.SingleLerp, MarchingCubes reuse = null)
        {
            var mc = reuse ?? new MarchingCubes();
            mc.Implicit = new Field { F = f, Box = box }; mc.Bounds = box; mc.CubeSize = cell; mc.ParallelCompute = parallel; mc.IsoValue = iso; mc.RootMode = mode;
            mc.Generate();
            return mc.Mesh;
        }

        static DMesh3 Dc(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, bool parallel = true, double iso = 0, bool simplify = true, bool insideLess = true, DualContouring reuse = null)
        {
            var dc = reuse ?? new DualContouring();
            dc.Implicit = new Field { F = f, Box = box }; dc.Bounds = box; dc.CellSize = cell; dc.ParallelCompute = parallel; dc.IsoValue = iso; dc.Simplify = simplify; dc.InsideIsLess = insideLess;
            dc.Generate();
            return dc.Mesh;
        }

        static void Contours()
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
            const double cell = 0.125;
            foreach (var (n, f) in Fields()) {
                foreach (string method in new[] { "mc", "dc", "dc-nosimplify" }) {
                    string c = n + " " + method;
                    try {
                        Func<Func<Vector3d, double>, AxisAlignedBox3d, bool, double, DMesh3> gen = (ff, bb, par, iso) =>
                            method == "mc" ? Mc(ff, bb, cell, par, iso) : Dc(ff, bb, cell, par, iso, method == "dc");
                        DMesh3 m = Program.Timed(() => gen(f, box, true, 0), out long ms);
                        Program.Report(S, c + " | time", "", ms);
                        Program.Report(S, c + " | closed manifold", Checks.ClosedManifold(m));
                        double vol = Checks.Volume(m);
                        Program.Report(S, c + " | outward", vol > 0 ? "" : "volume " + vol);
                        // serial = parallel
                        Program.Report(S, c + " | serial = parallel", SameIds(m, gen(f, box, false, 0)));
                        Program.Report(S, c + " | repeat", SameIds(m, gen(f, box, true, 0)));
                        // reuse of one instance
                        if (method == "mc") { var mc = new MarchingCubes(); DMesh3 a = Mc(f, box, cell, reuse: mc); DMesh3 b = Mc(f, box, cell, reuse: mc); Program.Report(S, c + " | reuse", SameIds(m, b)); Program.Report(S, c + " | reuse first", SameIds(m, a)); }
                        else { var dc = new DualContouring(); DMesh3 a = Dc(f, box, cell, simplify: method == "dc", reuse: dc); a = new DMesh3(a); DMesh3 b = Dc(f, box, cell, simplify: method == "dc", reuse: dc); Program.Report(S, c + " | reuse", SameIds(m, b)); Program.Report(S, c + " | reuse first", SameIds(m, a)); }
                        // iso value c on f + c is the level set of f
                        DMesh3 shifted = gen(p => f(p) + 0.25, box, true, 0.25);
                        // (f + 0.25 is rounded: the same mesh up to rounding of the crossings)
                        Program.Report(S, c + " | iso value 0.25 of f + 0.25", Near(m, shifted, 1e-9));
                        // 2^k
                        foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                            var sb = new AxisAlignedBox3d(box.Min * s, box.Max * s);
                            DMesh3 q = method == "mc" ? Mc(p => s * f(p / s), sb, cell * s) : Dc(p => s * f(p / s), sb, cell * s, simplify: method == "dc");
                            Program.Report(S, c + " | scale " + s, SameIds(m, q, s));
                        }
                        // mirror in x: the field g(p) = f(-x, y, z) on the mirrored box gives the mirrored solid
                        {
                            DMesh3 q = gen(p => f(new Vector3d(-p.x, p.y, p.z)), box, true, 0);
                            Program.Report(S, c + " | mirror closed manifold", Checks.ClosedManifold(q));
                            // (the cube's triangulation table and the QEF are not mirror symmetric: the same solid within the grid's resolution)
                            Program.Report(S, c + " | mirror volume", Program.Rel("volume", Checks.Volume(q), vol, 2e-2));
                            if (method == "mc") Program.Report(S, c + " | mirror counts", q.TriangleCount == m.TriangleCount && q.VertexCount == m.VertexCount ? "" : $"{q.VertexCount}/{q.TriangleCount} vs {m.VertexCount}/{m.TriangleCount}");
                        }
                        // a far second component: a copy of the field 4 units away (32 cells), both in one grid
                        {
                            var wide = new AxisAlignedBox3d(box.Min, box.Max + new Vector3d(4, 0, 0));
                            DMesh3 q = gen(p => Math.Min(f(p), f(p - new Vector3d(4, 0, 0))), wide, true, 0);
                            Program.Report(S, c + " | far copy closed manifold", Checks.ClosedManifold(q));
                            Program.Report(S, c + " | far copy triangles", q.TriangleCount == 2 * m.TriangleCount ? "" : $"{q.TriangleCount} vs {2 * m.TriangleCount}");
                            Program.Report(S, c + " | far copy volume", Program.Rel("volume", Checks.Volume(q), 2 * vol, 1e-9));
                        }
                        // the complement: -f inside a larger box is the box minus the solid; here only: the level set is the same surface
                        if (method != "mc") {
                            DMesh3 q = Dc(p => -f(p), box, cell, simplify: method == "dc", insideLess: false);
                            Program.Report(S, c + " | negated field, inside is greater", SameIds(m, q));
                        }
                        if (method == "mc") foreach (MarchingCubes.RootfindingModes mode in Enum.GetValues(typeof(MarchingCubes.RootfindingModes))) {
                            DMesh3 q;
                            // documented on LinearEdges: a crossing on a grid corner is not repaired, Generate() throws
                            try { q = Mc(f, box, cell, mode: mode); }
                            catch (InvalidOperationException ex) when (mode == MarchingCubes.RootfindingModes.LinearEdges) { Program.Info(S, c + " | " + mode, "throws as documented: " + ex.Message); continue; }
                            Program.Report(S, c + " | " + mode + " closed manifold", Checks.ClosedManifold(q));
                            Program.Report(S, c + " | " + mode + " volume", Program.Rel("volume", Checks.Volume(q), vol, 0.05));
                            Program.Report(S, c + " | " + mode + " serial = parallel", SameIds(q, Mc(f, box, cell, false, mode: mode)));
                        }
                    }
                    catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
                }
            }
        }

        static TpmsLattice Lattice(TpmsType type, TpmsStyle style, double cell, AxisAlignedBox3d box, double fraction = 0.3, int div = 8) =>
            new TpmsLattice { Type = type, Style = style, CellSize = cell, Box = box, VolumeFraction = fraction, Divisions = div };

        static void Tpms()
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1, -0.75, -0.5), new Vector3d(1, 0.75, 1));
            foreach (TpmsType type in Enum.GetValues(typeof(TpmsType)))
            foreach (TpmsStyle style in Enum.GetValues(typeof(TpmsStyle))) {
                string c = $"tpms {type} {style}";
                try {
                    TpmsLattice l = Lattice(type, style, 1, box);
                    Program.Timed(() => { l.Compute(); return 0; }, out long ms);
                    if (l.Status != TpmsStatus.Ok) { Program.Info(S, c, l.Status + " " + l.Message); Program.Report(S, c + " | no mesh on failure", l.Mesh == null ? "" : "mesh set"); continue; }
                    Program.Report(S, c + " | time", "", ms);
                    DMesh3 m = new DMesh3(l.Mesh);
                    Program.Report(S, c + " | solid", Checks.Solid(m));
                    double vol = Checks.Volume(m), bv = box.Volume;
                    Program.Report(S, c + " | realized fraction is the volume", Program.Rel("fraction", l.RealizedVolumeFraction, vol / bv, 1e-9));
                    if (Math.Abs(vol / bv - 0.3) > 0.1) Program.Info(S, c, $"fraction {vol / bv:F3} for a target of 0.3");
                    // inside the box
                    AxisAlignedBox3d b = Checks.Bounds(m);
                    Program.Report(S, c + " | inside the box", b.Min.x >= box.Min.x - 1e-9 && b.Min.y >= box.Min.y - 1e-9 && b.Min.z >= box.Min.z - 1e-9 && b.Max.x <= box.Max.x + 1e-9 && b.Max.y <= box.Max.y + 1e-9 && b.Max.z <= box.Max.z + 1e-9 ? "" : $"bounds {b}");
                    // reuse, serial
                    l.Compute();
                    Program.Report(S, c + " | reuse", l.Status == TpmsStatus.Ok ? SameIds(m, l.Mesh) : "status " + l.Status);
                    TpmsLattice ser = Lattice(type, style, 1, box); ser.ParallelCompute = false; ser.Compute();
                    Program.Report(S, c + " | serial = parallel", ser.Status == TpmsStatus.Ok ? SameIds(m, ser.Mesh) : "status " + ser.Status);
                    // 2^k
                    foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                        TpmsLattice q = Lattice(type, style, s, new AxisAlignedBox3d(box.Min * s, box.Max * s)); q.Compute();
                        Program.Report(S, c + " | scale " + s, q.Status == TpmsStatus.Ok ? SameIds(m, q.Mesh, s) : "status " + q.Status + " " + q.Message);
                    }
                    // the frame: the same lattice in a rotated and translated frame is the moved mesh
                    {
                        Matrix3d rot = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37);
                        var org = new Vector3d(632117.25, 5712043.5, 113.75);
                        TpmsLattice q = Lattice(type, style, 1, box); q.Rotation = rot; q.Origin = org; q.Compute();
                        if (q.Status != TpmsStatus.Ok) Program.Report(S, c + " | frame", "status " + q.Status + " " + q.Message);
                        else {
                            Program.Report(S, c + " | frame counts", q.Mesh.TriangleCount == m.TriangleCount && q.Mesh.VertexCount == m.VertexCount ? "" : $"{q.Mesh.VertexCount}/{q.Mesh.TriangleCount} vs {m.VertexCount}/{m.TriangleCount}");
                            double d = 0; if (q.Mesh.VertexCount == m.VertexCount) foreach (int v in m.VertexIndices()) d = Math.Max(d, (q.Mesh.GetVertex(v) - (rot * m.GetVertex(v) + org)).Length);
                            Program.Report(S, c + " | frame positions", d < 1e-8 ? "" : "max difference " + d.ToString("R"));
                            Program.Report(S, c + " | frame solid", Checks.ClosedManifold(q.Mesh));
                        }
                    }
                    // a box as a domain mesh
                    {
                        TpmsLattice q = Lattice(type, style, 1, box); q.DomainMesh = Shapes.Box(box.Center, box.Extents); q.Compute();
                        if (q.Status != TpmsStatus.Ok) Program.Info(S, c + " | domain mesh", q.Status + " " + q.Message);
                        else { Program.Report(S, c + " | domain mesh solid", Checks.Solid(q.Mesh)); Program.Report(S, c + " | domain mesh volume", Program.Rel("volume", Checks.Volume(q.Mesh), vol, 0.03)); }
                        // the same domain inside out: refused
                        var flipped = Shapes.Box(box.Center, box.Extents); flipped.ReverseOrientation();
                        TpmsLattice fl = Lattice(type, style, 1, box); fl.DomainMesh = flipped; fl.Compute();
                        Program.Report(S, c + " | inside-out domain refused", fl.Status == TpmsStatus.InvalidDomain && fl.Mesh == null ? "" : "status " + fl.Status);
                        // a permuted domain mesh: the same lattice
                        if (q.Status == TpmsStatus.Ok) {
                            TpmsLattice pm = Lattice(type, style, 1, box); pm.DomainMesh = Shapes.Permute(Shapes.Box(box.Center, box.Extents), 5); pm.Compute();
                            // (the distance to the domain takes the nearest triangle in another order: the same lattice up to rounding)
                            Program.Report(S, c + " | permuted domain mesh", pm.Status == TpmsStatus.Ok ? Near(q.Mesh, pm.Mesh, 1e-9) : "status " + pm.Status);
                        }
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
            // the two skeletal networks of a surface: fraction f and 1 - f fill the box together
            foreach (TpmsType type in Enum.GetValues(typeof(TpmsType))) {
                string c = $"tpms {type} skeletal complement";
                try {
                    TpmsLattice lo = Lattice(type, TpmsStyle.SkeletalLow, 1, box, 0.35, 16), hi = Lattice(type, TpmsStyle.SkeletalHigh, 1, box, 0.65, 16);
                    lo.Compute(); hi.Compute();
                    if (lo.Status != TpmsStatus.Ok || hi.Status != TpmsStatus.Ok) { Program.Info(S, c, $"{lo.Status} {lo.Message} / {hi.Status} {hi.Message}"); continue; }
                    Program.Info(S, c, $"low {lo.RealizedVolumeFraction:F4} + high {hi.RealizedVolumeFraction:F4} = {lo.RealizedVolumeFraction + hi.RealizedVolumeFraction:F4}, levels {lo.Level:R} {hi.Level:R}");
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message); }
            }
            // fractions across the range, thin walls included: Ok is a solid, anything else has no mesh
            foreach (double fr in new[] { 0.02, 0.05, 0.1, 0.5, 0.9, 0.98 })
            foreach (int div in new[] { 4, 8, 13 }) {
                string c = $"tpms gyroid sheet fraction {fr} divisions {div}";
                try {
                    TpmsLattice l = Lattice(TpmsType.Gyroid, TpmsStyle.Sheet, 0.7, box, fr, div); l.Compute();
                    if (l.Status != TpmsStatus.Ok) { Program.Info(S, c, l.Status + " " + l.Message); Program.Report(S, c + " | no mesh on failure", l.Mesh == null ? "" : "mesh set"); continue; }
                    Program.Report(S, c + " | solid", Checks.Solid(l.Mesh));
                    Program.Report(S, c + " | positive volume", Checks.Volume(l.Mesh) > 0 ? "" : "volume " + Checks.Volume(l.Mesh));
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        public static void Run()
        {
            Contours();
            Tpms();
        }
    }
}
