using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace SheetExtensionProbe
{
    enum Kind { Extended, Unchanged, Refused, Any }

    // what a case builds: the workpiece, the sheets (all with the role Sheet; the one at ShortIndex is the short one), the analytic kept volume
    sealed class CaseData
    {
        public DMesh3 W;
        public List<DMesh3> Sheets = new List<DMesh3>();
        public int ShortIndex;
        public ExactMeshBoolean.SheetUncutSides Uncut = ExactMeshBoolean.SheetUncutSides.Refuse;
        /// <summary> the kept volume by hand (NaN: from AnalyticFromExtended, computed once in the first frame) </summary>
        public double Analytic = double.NaN;
        public Func<DMesh3, double> AnalyticFromExtended;
        /// <summary> the volume of the smooth surface the sheet stands for (NaN: not applicable), for the comparison with U </summary>
        public double Intent = double.NaN;
        public string Note = "";
    }

    sealed class Case
    {
        public string Name, Group;
        public Kind Expect;
        public ExtensionRefusal[] Reasons = new ExtensionRefusal[0];
        /// <summary> run also in the translated and rotated frames </summary>
        public bool Frames = true;
        public Func<CaseData> Build;
        public double CachedAnalytic = double.NaN;
        public string Description = "";
    }

    static class Cases
    {
        static Case Add(List<Case> list, string group, string name, Kind expect, Func<CaseData> build, string description, bool frames = true, params ExtensionRefusal[] reasons)
        {
            var c = new Case { Group = group, Name = name, Expect = expect, Build = build, Description = description, Frames = frames, Reasons = reasons };
            list.Add(c);
            return c;
        }

        const double Tall = 10;

        static DMesh3 Cube() { return Geometry.Box(0, 0, 0, 10, 10, 10); }

        static readonly double[] YsWide = { -2.3, -0.5, 1.3, 3.1, 4.9, 6.7, 8.5, 10.3, 12.1 };

        // the end x of a sheet that ends pct of its own diagonal short of x = 10
        static double EndX(double pct, Func<double, DMesh3> make)
        {
            double xe = 10 - pct * 18;
            for (int i = 0; i < 8; ++i) {
                double d = make(xe).GetBounds().Diagonal.Length;
                xe = 10 - pct * d;
            }
            return xe;
        }

        static DMesh3 PlanarSheet(double xe)
        {
            return Geometry.Grid(new[] { -2.3, 1.1, 5.0, (5.0 + xe) / 2, xe }, YsWide, (x, y) => 5, true);
        }

        static double BentHeight(double x) { return 5 + 0.3 * Math.Max(0, x - 5); }

        static DMesh3 BentSheet(double xe)
        {
            return Geometry.Grid(new[] { -2.3, 1.1, 5.0, (5.0 + xe) / 2, xe }, YsWide, (x, y) => BentHeight(x), true);
        }

        public static List<Case> All()
        {
            var list = new List<Case>();

            // ---- the box and a planar / bent sheet that ends pct of its diagonal short
            foreach (double pct in new[] { 0.001, 0.005, 0.02, 0.08 }) {
                double p = pct;
                bool ok = p <= 0.05;
                Add(list, "box", "planar " + (p * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %", ok ? Kind.Extended : Kind.Refused, () => {
                    double xe = EndX(p, PlanarSheet);
                    return new CaseData { W = Cube(), Sheets = { PlanarSheet(xe) }, Analytic = 500 };
                }, "cube [0,10]^3, sheet z = 5 (normals up), rim " + (p * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " % of its diagonal short of x = 10; the part above is kept (500)", true,
                    ExtensionRefusal.DepthAboveLimit);
                Add(list, "box", "bent " + (p * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %", ok ? Kind.Extended : Kind.Refused, () => {
                    double xe = EndX(p, BentSheet);
                    return new CaseData { W = Cube(), Sheets = { BentSheet(xe) }, Analytic = 1000 - 10 * (50 + 0.3 * 12.5) };
                }, "cube, sheet z = 5 up to x = 5 and rising with slope 0.3 beyond, rim " + (p * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " % short; the part above is kept (462.5)", true,
                    ExtensionRefusal.DepthAboveLimit);
            }
            Add(list, "box", "slit ends in the middle", Kind.Refused, () => new CaseData {
                W = Cube(), Sheets = { Geometry.Grid(new[] { -2.3, 1.1, 3.0, 5.0 }, YsWide, (x, y) => 5, true) }
            }, "cube, planar sheet ending at x = 5: no exit within the limit (depth 5 is 35 % of the diagonal)", true, ExtensionRefusal.DepthAboveLimit);

            // ---- the slot and the fin; the free pin
            Func<DMesh3> slotFin = () => Geometry.PrismXZ(new[] {
                new Vector2d(0, 0), new Vector2d(9.85, 0), new Vector2d(9.85, 10), new Vector2d(9.55, 10),
                new Vector2d(9.55, 2), new Vector2d(9.5, 2), new Vector2d(9.5, 10), new Vector2d(0, 10) }, 0, 10);
            Func<DMesh3> slotSheet = () => Geometry.Grid(new[] { -2.3, 1.1, 5.0, 9.4 }, new[] { 0.3, 2.0, 4.0, 6.0, 8.0, 9.7 }, (x, y) => 5, false);
            Add(list, "adversarial", "slot and fin", Kind.Extended, () => new CaseData {
                W = slotFin(), Sheets = { slotSheet() }, Analytic = 506
            }, "block [0,9.5] x [0,10] x [0,10], slot 0.05 wide, fin 0.3 thick at x 9.55..9.85, common base below z = 2; sheet z = 5 (normals down) 0.1 short of the slot wall; the strip stops at the wall, the fin is untouched: kept 506");
            Add(list, "adversarial", "free pin in the slot", Kind.Refused, () => new CaseData {
                W = Geometry.Union(slotFin(), Geometry.Box(9.51, 4.5, 1.0, 9.515, 5.5, 8.0)), Sheets = { slotSheet() }
            }, "the slot case with a thin pin standing in the slot (x 9.51..9.515, y 4.5..5.5, z 1..8), between rim vertices whose rays both exit cleanly: the strip pierces the pin; refused by the curve rule", true, ExtensionRefusal.CurveRule);
            Add(list, "adversarial", "wide pocket (concave, re-entry)", Kind.Extended, () => new CaseData {
                W = Geometry.PrismXZ(new[] {
                    new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(9, 10),
                    new Vector2d(9, 2), new Vector2d(8, 2), new Vector2d(8, 10), new Vector2d(0, 10) }, 0, 10),
                Sheets = { Geometry.Grid(new[] { -2.3, 1.1, 5.0, 7.9 }, new[] { 0.3, 2.0, 4.0, 6.0, 8.0, 9.7 }, (x, y) => 5, true) },
                Analytic = 400
            }, "block [0,8], a pocket 1 wide, then a wall [9,10] on a common base; sheet z = 5 (up) 0.1 short of the pocket: the first exit is used, the wall beyond is never reached; kept = the block above z = 5 (400)");

            // ---- the notch, the U, the bowtie
            Add(list, "adversarial", "notch narrower than the depth", Kind.Refused, () => {
                var xs = new[] { -2.3, 1.1, 5.0, 9.5, 9.9 };
                var ys = new[] { -2.3, -0.5, 1.3, 3.1, 4.9, 5.1, 6.7, 8.5, 10.3, 12.1 };
                DMesh3 s = Geometry.Grid(xs, ys, (x, y) => 5, true, (cx, cy) => !(cx > 9.5 && cy > 4.9 && cy < 5.1));
                return new CaseData { W = Cube(), Sheets = { s } };
            }, "cube, sheet z = 5 ending 0.1 short with a notch 0.2 wide and 0.4 deep in its end (the rim there is 0.5 from the surface): the rays of the notch's bottom corners cross, the outer segment runs backwards", true,
                ExtensionRefusal.InvertedQuad);
            Add(list, "adversarial", "rim outside, next to another part (U)", Kind.Unchanged, () => new CaseData {
                W = Geometry.PrismXZ(new[] {
                    new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(7, 10),
                    new Vector2d(7, 2), new Vector2d(3, 2), new Vector2d(3, 10), new Vector2d(0, 10) }, 0, 10),
                Sheets = { Geometry.Grid(new[] { -2.3, 1.1, 3.0, 5.0 }, YsWide, (x, y) => 5, false) },
                Analytic = 530
            }, "U-shaped prism (arms [0,3] and [7,10], base below z = 2); sheet z = 5 (down) crosses the left arm and ends in the notch at x = 5, outside the workpiece: the rim is not continued, the strip cannot reach the right arm; kept 530");
            Add(list, "adversarial", "bowtie vertex on the rim", Kind.Refused, () => {
                Vector3d[] v = { new Vector3d(9.4, 5, 5), new Vector3d(-2.3, -2.3, 5), new Vector3d(-2.3, 4.5, 5), new Vector3d(-2.3, 12.1, 5), new Vector3d(-2.3, 5.5, 5) };
                return new CaseData { W = Cube(), Sheets = { Geometry.Triangles(v, new[] { 0, 2, 1, 0, 3, 4 }) } };
            }, "cube; two triangles of a sheet at z = 5 that share only the vertex (9.4, 5, 5) inside the cube: a bowtie vertex of the rim; refused (no per-wedge points)", true, ExtensionRefusal.BowtieVertex);

            // ---- the chord, the vertex on the surface, the exit angle, the ray through an edge / a vertex
            Add(list, "adversarial", "chord across a sphere", Kind.Extended, () => {
                DMesh3 sphere = Geometry.Sphere(Vector3d.Zero, 5, 14);
                DMesh3 s = Geometry.Grid(new[] { -8, -4, 4, 8.0 }, new[] { -8, -3, 0, 2, 4.5 }, (x, y) => 0.5, true);
                double kept = SheetExtension.Volume(Geometry.Intersect(sphere, Geometry.Box(-9, -9, 0.5, 9, 9, 9)));
                return new CaseData { W = sphere, Sheets = { s }, Analytic = kept };
            }, "sphere (radius 5, 14 vertices per cube edge), sheet z = 0.5 (up) with its far edge y = 4.5 across the sphere as ONE edge from x = -4 to 4: both ends outside, the middle inside, no vertex of the rim inside; continued by edge, the outer edge subdivided; kept = the sphere above z = 0.5 by an independent closed intersection");
            Add(list, "adversarial", "rim vertex exactly on the surface", Kind.Extended, () => {
                Vector3d[] v = { new Vector3d(-2.3, -2.3, 5), new Vector3d(9.8, -2.3, 5), new Vector3d(9.8, 3, 5), new Vector3d(10, 5, 5), new Vector3d(9.8, 7, 5), new Vector3d(9.8, 12.1, 5), new Vector3d(-2.3, 12.1, 5) };
                return new CaseData { W = Cube(), Sheets = { Geometry.Triangles(v, new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6 }) }, Analytic = 500 };
            }, "cube, sheet z = 5 (up) ending 0.2 short with a tip at (10, 5, 5) exactly on the face: g = 0 there, no degenerate triangle; kept 500", false);
            Func<double, DMesh3> wedge = x => Geometry.PrismXZ(new[] { new Vector2d(0, 0), new Vector2d(x, 0), new Vector2d(0, 1) }, 0, 10);
            Add(list, "adversarial", "exit at 3 degrees", Kind.Refused, () => new CaseData {
                W = wedge(19.0), Sheets = { Geometry.Grid(new[] { -2.3, 1.1, 5.0, 9.0 }, YsWide, (x, y) => 0.5, true) }
            }, "wedge prism (0,0)-(19,0)-(0,1), sheet z = 0.5 ending 0.5 short of the slanted face, which the rays meet at 3 degrees: alpha is below delta_min plus the margin", true, ExtensionRefusal.GrazingExit);
            Add(list, "adversarial", "exit at 20 degrees", Kind.Extended, () => {
                double x = 1.0 / Math.Tan(20 * Math.PI / 180);
                return new CaseData {
                    W = wedge(x), Sheets = { Geometry.Grid(new[] { -2.3, -0.6, 1.0 }, YsWide, (x2, y) => 0.5, true) }, Analytic = x * 10 / 8
                };
            }, "the same wedge with a face at 20 degrees to the rays: continued; kept = X * 10 / 8");
            Add(list, "adversarial", "short on four sides, rays through box edges", Kind.Extended, () => new CaseData {
                W = Cube(), Sheets = { Geometry.Grid(new[] { 0.3, 3, 6, 9.7 }, new[] { 0.3, 3, 6, 9.7 }, (x, y) => 5, true) }, Analytic = 500
            }, "cube, sheet z = 5 over [0.3, 9.7]^2 (up): every corner ray runs along a diagonal through a vertical edge of the cube exactly; kept 500");
            Add(list, "adversarial", "octahedron, rays through its vertices and edges", Kind.Any, () => {
                Vector3d c = Vector3d.Zero;
                Vector3d[] v = { c, new Vector3d(9.5, 0, 0), new Vector3d(0, 9.5, 0), new Vector3d(-9.5, 0, 0), new Vector3d(0, -9.5, 0) };
                return new CaseData { W = Geometry.Octahedron(10), Sheets = { Geometry.Triangles(v, new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1 }) }, Analytic = 4.0 / 3 * 1000 / 2 };
            }, "octahedron |x| + |y| + |z| <= 10, sheet = the diamond of half-diagonal 9.5 in the plane z = 0 (up): the corner rays run through its vertices, all other rays through its equator edges; continued or refused, never wrong; kept 666.67", false);

            // ---- curved sheets
            Func<double, DMesh3> cylinder = xe => {
                double th0 = -Math.Asin(7.3 / 8), th1 = Math.Asin((xe - 5) / 8);
                var xs = new double[41];
                for (int i = 0; i <= 40; ++i) xs[i] = 5 + 8 * Math.Sin(th0 + (th1 - th0) * i / 40);
                return Geometry.Grid(xs, new[] { -2.3, 2.5, 7.3, 12.1 }, (x, y) => 13 - Math.Sqrt(Math.Max(0, 64 - (x - 5) * (x - 5))), true);
            };
            Add(list, "curved", "cylinder segment 0.5 %", Kind.Extended, () => {
                double xe = EndX(0.005, cylinder);
                DMesh3 s = cylinder(xe);
                // what the smooth surface would give where the sheet is (its own facets, exactly) and beyond its rim (the smooth cylinder)
                double intent = 1000 - Geometry.VolumeUnder(s, 0, 0, 10, 10) - 10 * Integrate(x => 13 - Math.Sqrt(64 - (x - 5) * (x - 5)), xe, 10);
                return new CaseData {
                    W = Cube(), Sheets = { s }, Intent = intent,
                    AnalyticFromExtended = ext => 1000 - Geometry.VolumeUnder(ext, 0, 0, 10, 10)
                };
            }, "cube, sheet = an arc of radius 8 (40 facets, axis along y, bowl up) ending 0.5 % short of x = 10; kept = above the extended sheet by an independent height-field integral; the smooth cylinder beyond the rim, with the sheet's own facets, is the intent");
            Func<DMesh3> dome = () => Geometry.Grid(Geometry.Lin(0.2, 9.8, 12), Geometry.Lin(0.2, 9.8, 12), (x, y) => -2 + Math.Sqrt(64 - (x - 5) * (x - 5) - (y - 5) * (y - 5)), true);
            Add(list, "curved", "spherical dome (stated sheet)", Kind.Extended, () => new CaseData {
                W = Cube(), Sheets = { dome() }, Intent = 1000 - Geometry.VolumeUnder(dome(), 0, 0, 10, 10) - IntegrateBand((x, y) => -2 + Math.Sqrt(64 - (x - 5) * (x - 5) - (y - 5) * (y - 5)), 0.2, 9.8),
                AnalyticFromExtended = ext => 1000 - Geometry.VolumeUnder(ext, 0, 0, 10, 10)
            }, "cube, sheet = a sphere (radius 8) as a height field over [0.2, 9.8]^2 (12 x 12 cells), rim 0.2 from every side; continued all round, corner rays included; kept above");
            Func<double, double, double> tray = (x, y) => 2 + 0.5 * Math.Max(0, Math.Max(Math.Abs(x - 5) - 2, Math.Abs(y - 5) - 2));
            Add(list, "curved", "shallow tray (stated sheet)", Kind.Extended, () => new CaseData {
                W = Cube(),
                Sheets = { Geometry.Grid(new[] { 0.2, 3, 7, 9.8 }, new[] { 0.2, 3, 7, 9.8 }, tray, true) },
                Intent = 1000 - Geometry.VolumeUnder(Geometry.Grid(new[] { 0.2, 3, 7, 9.8 }, new[] { 0.2, 3, 7, 9.8 }, tray, true), 0, 0, 10, 10) - IntegrateBand(tray, 0.2, 9.8),
                AnalyticFromExtended = ext => 1000 - Geometry.VolumeUnder(ext, 0, 0, 10, 10)
            }, "cube, sheet = a tray (flat bottom z = 2 on [3,7]^2, walls of slope 0.5 up to the rim 0.2 from the sides); continued all round; kept above");

            // ---- cases that give single rules their teeth
            Add(list, "teeth", "ray grazes a reflex edge", Kind.Refused, () => new CaseData {
                W = Geometry.PrismXZ(new[] { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 5), new Vector2d(5, 5), new Vector2d(5, 10), new Vector2d(0, 10) }, 0, 10),
                Sheets = { Geometry.Grid(new[] { -2.3, 1.1, 3.0, 4.5 }, YsWide, (x, y) => 5 - 0.5 * (x - 5), true) }
            }, "L-shaped prism, sheet z = 5 - (x - 5) / 2 (up) ending at x = 4.5 whose rays pass exactly through the reflex edge (5, y, 5) and go on inside the material: the first hit is no exit; the new point is inside", true, ExtensionRefusal.NewPointNotOutside);
            Add(list, "teeth", "thick pin touching the outer edge (routed round)", Kind.Extended, () => new CaseData {
                W = Geometry.Union(slotFin(), Geometry.Box(9.51, 4.5, 1.0, 9.54, 5.5, 8.0)), Sheets = { slotSheet() }, Analytic = 506 + 0.03 * 1.0 * 6
            }, "the slot case with a thicker free pin (x 9.51..9.54, y 4.5..5.5, z 1..8): it reaches the outer edge at x = 9.525, so the subdivision routes the outer edge between the wall and the pin and the strip does not touch the pin; kept 506 + the pin (0.18)");
            Add(list, "teeth", "free pin whose top lies in the strip's plane", Kind.Refused, () => new CaseData {
                W = Geometry.Union(slotFin(), Geometry.Box(9.51, 4.5, 1.0, 9.515, 5.5, 5.0)), Sheets = { slotSheet() }
            }, "the free pin of the slot case, but ending exactly at z = 5: its top face lies in the plane of the strip, between the rays of the rim vertices, and is overlapped by the strip", true,
                ExtensionRefusal.CoplanarContact, ExtensionRefusal.CurveRule);
            Add(list, "teeth", "rib on the slot wall (no way round)", Kind.Refused, () => new CaseData {
                W = Geometry.Union(slotFin(), Geometry.Box(9.49, 4.5, 2.0, 9.54, 5.5, 8.0)), Sheets = { slotSheet() }
            }, "the slot case with a rib on the wall (x 9.49..9.54, y 4.5..5.5, z 2..8): for y in the rib the first exit is 0.04 farther; the outer edge would have to step round the rib, which a monotone strip cannot do", true,
                ExtensionRefusal.OuterEdgeInside, ExtensionRefusal.InvertedQuad, ExtensionRefusal.CurveRule);
            Add(list, "teeth", "sheet in the plane of a step face", Kind.Refused, () => new CaseData {
                W = Geometry.PrismXZ(new[] { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 5), new Vector2d(5, 5), new Vector2d(5, 10), new Vector2d(0, 10) }, 0, 10),
                Sheets = { Geometry.Grid(new[] { -2.3, 1.1, 4.9 }, YsWide, (x, y) => 5, true) }
            }, "L-shaped prism whose step face (x 5..10) lies in the plane z = 5 of the sheet; the sheet ends 0.1 short of the wall x = 5: past the wall the ray runs on the step face, so the new point is on the surface, not outside", true,
                ExtensionRefusal.NewPointNotOutside);
            Add(list, "teeth", "strip crosses another sheet", Kind.Refused, () => {
                DMesh3 wall = Geometry.Grid(new[] { -2.3, 5.0, 12.1 }, new[] { -2.0, 5.0, 12.0 }, (x, y) => 0, true);
                // the plane x = 9.95 (normals +x): the grid's (x, y) become (y, z)
                DMesh3 vert = Geometry.Transformed(wall, p => new Vector3d(9.95, p.x, p.y));
                vert.ReverseOrientation();
                return new CaseData { W = Cube(), Sheets = { PlanarSheet(9.9), vert }, ShortIndex = 0 };
            }, "cube, the 0.1-short sheet z = 5 and a second sheet, the plane x = 9.95, that fully crosses the cube: the strip of the first crosses the second inside the cube", false, ExtensionRefusal.MeetsOtherTool);
            Add(list, "teeth", "folded sheet, strips converge", Kind.Refused, () => {
                var xs = new[] { -2.3, 4.0, 9.9 };
                var ys = new[] { -2.3, 5.0, 12.1 };
                DMesh3 lower = Geometry.Grid(xs, ys, (x, y) => 5, true);
                DMesh3 upper = Geometry.Grid(xs, ys, (x, y) => 5.01 + 0.3 * (9.9 - x), false);
                double zTop = 5.01 + 0.3 * 12.2;
                var wallV = new List<Vector3d>(); var wallT = new List<int>();
                for (int j = 0; j < ys.Length; ++j) { wallV.Add(new Vector3d(-2.3, ys[j], 5)); wallV.Add(new Vector3d(-2.3, ys[j], zTop)); }
                for (int j = 0; j + 1 < ys.Length; ++j) { int a = 2 * j, b = 2 * j + 1, c = 2 * j + 3, d = 2 * j + 2; wallT.AddRange(new[] { a, c, b, a, d, c }); }
                DMesh3 wall = Geometry.Triangles(wallV.ToArray(), wallT.ToArray());
                DMesh3 fold = Geometry.Weld(lower, upper, wall);
                return new CaseData { W = Cube(), Sheets = { fold } };
            }, "cube, a sheet folded like a ribbon at x = -2.3 (lower arm z = 5, upper arm rising 0.3 per unit towards the fold), both arms ending at x = 9.9: the upper rim is 0.01 above the lower one, so the strips of the two rims converge and cross each other inside the cube at x = 9.93", false,
                ExtensionRefusal.MeetsItself, ExtensionRefusal.MeetsSheet, ExtensionRefusal.SheetRoleRefused);

            // ---- several sheets, UncutSide
            Add(list, "adversarial", "several sheets, one short", Kind.Extended, () => {
                DMesh3 full = Geometry.Grid(new[] { -2.3, 5.0, 12.3 }, new[] { -2.3, 5.0, 12.1 }, (x, y) => 2, true);
                DMesh3 shortOne = Geometry.Grid(new[] { -2.3, 1.1, 5.0, 9.9 }, YsWide, (x, y) => 7, false);
                return new CaseData { W = Cube(), Sheets = { shortOne, full }, ShortIndex = 0, Analytic = 500 };
            }, "cube, sheet z = 2 (up) crossing fully and sheet z = 7 (down) 0.1 short: only the short one is continued; kept the slab 2 < z < 7 (500)");
            foreach (var side in new[] { ExactMeshBoolean.SheetUncutSides.Outside, ExactMeshBoolean.SheetUncutSides.Inside }) {
                var sd = side;
                Add(list, "adversarial", "UncutSide " + sd + " set (no uncut part)", Kind.Extended, () => new CaseData {
                    W = Cube(), Sheets = { PlanarSheet(9.9) }, Uncut = sd, Analytic = 500
                }, "the 0.5 % planar case with UncutSide " + sd + " passed through to the cut: no part is uncut, so the result is the same (500)");
            }
            Add(list, "adversarial", "UncutSide Outside, two shells", Kind.Refused, () => {
                DMesh3 two = Geometry.Merge(Cube(), Geometry.Box(20, 0, 0, 30, 10, 10));
                return new CaseData { W = two, Sheets = { PlanarSheet(9.9) }, Uncut = ExactMeshBoolean.SheetUncutSides.Outside };
            }, "two cubes (disjoint boxes) as the workpiece, sheet z = 5 short in the first: the second cube is a part no piece touches; with several shells the sheet role refuses it whatever UncutSide says (a refusal of the cut, after the extension)", true, ExtensionRefusal.SheetRoleRefused);
            return list;
        }

        static double Integrate(Func<double, double> f, double a, double b)
        {
            int n = 20000; double s = 0;
            for (int i = 0; i < n; ++i) s += f(a + (b - a) * (i + 0.5) / n);
            return s * (b - a) / n;
        }

        // the integral of f over [0, 10]^2 without [lo, hi]^2 (the strip's footprint)
        static double IntegrateBand(Func<double, double, double> f, double lo, double hi)
        {
            int n = 800; double s = 0;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    double x = 10 * (i + 0.5) / n, y = 10 * (j + 0.5) / n;
                    if (x > lo && x < hi && y > lo && y < hi) continue;
                    s += f(x, y);
                }
            return s * 100.0 / (n * n);
        }

        static double IntegrateXY(Func<double, double, double> f, double a, double b)
        {
            int n = 400; double s = 0;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    s += f(a + (b - a) * (i + 0.5) / n, a + (b - a) * (j + 0.5) / n);
            return s * (b - a) * (b - a) / (n * n);
        }
    }
}
