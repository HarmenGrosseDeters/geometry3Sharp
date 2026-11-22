using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// PassageWrap, the sharp-feature pass of the alpha wrap (AlphaWrapFeaturePass, through reflection: the class is internal) and
    /// the options of MeshAlphaWrap that hunt 1 did not run. A Success of PassageWrap is checked independently: the result is a
    /// solid that contains the input, every pin axis is outside it (winding number) and keeps its radius (sampled distances).
    /// The feature pass promises: closed, manifold, no self-intersections, the input strictly inside, moved vertices at least
    /// 0.9 offsets from the input, deterministic, 2^k bit for bit. Relations: 2^k, reuse, serial = parallel, a reversed and a
    /// duplicated pin, a permuted and a flipped input, a far copy.
    /// </summary>
    public static class Wrap2Stage
    {
        const string S = "wrap2";

        /// <summary> a tube along z: outer and inner n-gon, z in [-h, h], outward oriented (a through hole of radius ri) </summary>
        public static DMesh3 Tube(int n, double ro, double ri, double h)
        {
            var m = new DMesh3();
            for (int k = 0; k < 4; ++k) for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n, r = (k % 2 == 0) ? ro : ri, z = k < 2 ? -h : h;
                m.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), z));
            }
            int O0 = 0, I0 = n, O1 = 2 * n, I1 = 3 * n;
            void Quad(int a, int b, int c, int d) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                Quad(O0 + i, O0 + j, O1 + j, O1 + i);     // outer wall
                Quad(I0 + j, I0 + i, I1 + i, I1 + j);     // inner wall
                Quad(O1 + i, O1 + j, I1 + j, I1 + i);     // top
                Quad(O0 + j, O0 + i, I0 + i, I0 + j);     // bottom
            }
            return m;
        }

        static string PinsIndependent(DMesh3 result, IList<PassagePin> pins)
        {
            foreach (PassagePin pin in pins) {
                for (int i = 0; i + 1 < pin.Axis.Count || i == 0; ++i) {
                    Vector3d a = pin.Axis[i], b = pin.Axis[Math.Min(i + 1, pin.Axis.Count - 1)];
                    for (int s = 0; s <= 40; ++s) {
                        Vector3d q = a + (b - a) * (s / 40.0);
                        double w = Checks.Winding(result, q);
                        if (Math.Abs(w) > 0.5) return $"an axis point is inside the wrap (winding {w:F3})";
                        double d = Checks.DistanceTo(result, q);
                        if (d < pin.Radius * (1 - 1e-9)) return $"an axis point is {d:R} from the wrap, radius {pin.Radius:R}";
                    }
                }
            }
            return "";
        }

        static PassageWrap Passage(DMesh3 mesh, IEnumerable<PassagePin> pins, Action<PassageWrap> opt = null)
        {
            var p = new PassageWrap(mesh);
            p.Pins.AddRange(pins);
            opt?.Invoke(p);
            return p;
        }

        static void Passages()
        {
            Matrix3d rot = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 33);
            var cases = new (string, DMesh3, PassagePin[])[] {
                ("tube", Tube(16, 1.0, 0.4, 0.5), new[] { new PassagePin(new[] { new Vector3d(0, 0, -0.9), new Vector3d(0, 0, 0.9) }, 0.2) }),
                ("rotated tube", Shapes.Map(Tube(12, 1.0, 0.5, 0.3), p => rot * p), new[] { new PassagePin(new[] { rot * new Vector3d(0, 0, -0.7), rot * new Vector3d(0, 0, 0.7) }, 0.3) }),
                ("torus", Shapes.Torus(Vector3d.Zero, 1.0, 0.3, 24, 12), new[] { new PassagePin(new[] { new Vector3d(0, 0, -0.8), new Vector3d(0, 0, 0.8) }, 0.35) }),
                ("two tubes", Shapes.Combine(Tube(12, 1.0, 0.4, 0.5), Shapes.Map(Tube(12, 0.8, 0.3, 0.4), p => p + new Vector3d(3, 0, 0))), new[] {
                    new PassagePin(new[] { new Vector3d(0, 0, -0.9), new Vector3d(0, 0, 0.9) }, 0.2), new PassagePin(new[] { new Vector3d(3, 0, -0.8), new Vector3d(3, 0, 0.8) }, 0.15) }),
                ("tube, bent pin", Tube(16, 1.0, 0.45, 0.5), new[] { new PassagePin(new[] { new Vector3d(1.6, 0, -1.0), new Vector3d(0, 0, -0.8), new Vector3d(0, 0, 0.8), new Vector3d(-1.6, 0, 1.0) }, 0.15) }),
            };
            foreach (var (name, mesh, pins) in cases)
            foreach (bool simplify in new[] { true, false }) {
                string c = "passage " + name + (simplify ? "" : " raw");
                try {
                    Action<PassageWrap> opt = p => p.Simplify = simplify;
                    PassageWrap pw = Passage(new DMesh3(mesh), pins, opt);
                    bool ok = Program.Timed(() => pw.Compute(), out long ms);
                    Program.Report(S, c + " | time", "", ms);
                    if (pw.Status != PassageWrap.PassageStatus.Success) { Program.Info(S, c, $"{pw.Status} {pw.Error} wrap {pw.WrapStatus}"); Program.Report(S, c + " | no claimed pass", pw.AllPassed && pw.Status != PassageWrap.PassageStatus.Success ? "AllPassed with " + pw.Status : ""); continue; }
                    DMesh3 r = pw.Result;
                    Program.Report(S, c + " | AllPassed", pw.AllPassed ? "" : "Success without AllPassed");
                    Program.Report(S, c + " | solid", Checks.Solid(r));
                    Program.Report(S, c + " | input inside", ChainStage.InputInside(mesh, r));
                    Program.Report(S, c + " | pins clear (independent)", PinsIndependent(r, pins));
                    string iv = ""; foreach (PassagePinResult pr in pw.PinResults) if (!(pr.ClearanceLow <= pr.ClearanceHigh) || !pr.AxisOutside || !(pr.ClearanceLow >= 0)) iv = $"interval [{pr.ClearanceLow:R}, {pr.ClearanceHigh:R}] outside {pr.AxisOutside}";
                    Program.Report(S, c + " | pin results", iv);
                    // the reported interval contains the sampled clearance
                    for (int k = 0; k < pins.Length; ++k) {
                        double min = double.MaxValue;
                        for (int i = 0; i + 1 < pins[k].Axis.Count; ++i) for (int s = 0; s <= 200; ++s) min = Math.Min(min, Checks.DistanceTo(r, pins[k].Axis[i] + (pins[k].Axis[i + 1] - pins[k].Axis[i]) * (s / 200.0)));
                        Program.Report(S, c + $" | pin {k} sampled clearance not below ClearanceLow", min >= pw.PinResults[k].ClearanceLow * (1 - 1e-9) ? "" : $"sampled {min:R}, ClearanceLow {pw.PinResults[k].ClearanceLow:R}");
                    }
                    Program.Report(S, c + " | alpha within the bound", pw.Alpha <= pw.BoundAlpha * (1 + 1e-12) || pw.BoundAlpha == 0 ? "" : $"Alpha {pw.Alpha:R} above BoundAlpha {pw.BoundAlpha:R}");
                    // reuse
                    DMesh3 first = new DMesh3(r);
                    pw.Compute();
                    Program.Report(S, c + " | reuse", pw.Status == PassageWrap.PassageStatus.Success ? BooleanStage.SameBits(first, pw.Result, 1) : "status " + pw.Status);
                    // one thread
                    PassageWrap one = Passage(new DMesh3(mesh), pins, p => { opt(p); p.MaxThreads = 1; }); one.Compute();
                    Program.Report(S, c + " | one thread", one.Status == pw.Status ? BooleanStage.SameBits(first, one.Result, 1) : "status " + one.Status);
                    // 2^k
                    foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                        PassageWrap ps = Passage(Shapes.Map(mesh, q => q * s), pins.Select(p => new PassagePin(p.Axis.Select(q => q * s), p.Radius * s)), opt); ps.Compute();
                        Program.Report(S, c + " | scale " + s, ps.Status == pw.Status ? BooleanStage.SameBits(first, ps.Result, s) : "status " + ps.Status);
                    }
                    // reversed axes, a pin given twice: the same wrap
                    {
                        PassageWrap rv = Passage(new DMesh3(mesh), pins.Select(p => new PassagePin(p.Axis.AsEnumerable().Reverse(), p.Radius)), opt); rv.Compute();
                        Program.Report(S, c + " | reversed axes", rv.Status == pw.Status ? BooleanStage.SameBits(first, rv.Result, 1) : "status " + rv.Status);
                        PassageWrap tw = Passage(new DMesh3(mesh), pins.Concat(pins), opt); tw.Compute();
                        Program.Report(S, c + " | every pin twice", tw.Status == pw.Status ? BooleanStage.SameBits(first, tw.Result, 1) : "status " + tw.Status);
                        PassageWrap ro = Passage(new DMesh3(mesh), pins.Reverse(), opt); ro.Compute();
                        Program.Report(S, c + " | pins in reverse order", ro.Status == pw.Status ? BooleanStage.SameBits(first, ro.Result, 1) : "status " + ro.Status);
                    }
                    // a smaller pin never needs a finer wrap
                    {
                        PassageWrap sm = Passage(new DMesh3(mesh), pins.Select(p => new PassagePin(p.Axis, p.Radius * 0.5)), opt); sm.Compute();
                        Program.Report(S, c + " | half radius status", sm.Status == PassageWrap.PassageStatus.Success ? "" : sm.Status.ToString());
                        if (sm.Status == PassageWrap.PassageStatus.Success) Program.Report(S, c + " | half radius: alpha not smaller", sm.BoundAlpha >= pw.BoundAlpha * (1 - 1e-12) ? "" : $"BoundAlpha {sm.BoundAlpha:R} vs {pw.BoundAlpha:R}");
                    }
                    // permuted, flipped input and similarities: a valid passage again
                    var variants = new List<(string, DMesh3, Func<Vector3d, Vector3d>, double)> { ("permuted", Shapes.Permute(mesh, 4), q => q, 1.0) };
                    var fl = new DMesh3(mesh); fl.ReverseOrientation(); variants.Add(("flipped", fl, q => q, 1.0));
                    foreach (var (sn, f, sc) in LoftStage.Sims()) variants.Add((sn, Shapes.Map(mesh, f), f, sc));
                    foreach (var (vn, vm, f, sc) in variants) {
                        var vp = pins.Select(p => new PassagePin(p.Axis.Select(f), p.Radius * sc)).ToArray();
                        PassageWrap pv = Passage(vm, vp, opt); pv.Compute();
                        string tag = c + " | " + vn;
                        if (pv.Status != pw.Status) { Program.Report(S, tag, "status " + pv.Status + " " + pv.Error); continue; }
                        Program.Report(S, tag + " solid", Checks.Solid(pv.Result));
                        Program.Report(S, tag + " pins clear", PinsIndependent(pv.Result, vp));
                        Program.Report(S, tag + " input inside", ChainStage.InputInside(vm, pv.Result));
                        // (the input clearance is a certified lower bound within 1e-3 of the pin radius, so BoundAlpha moves by that much)
                        Program.Report(S, tag + " bound alpha", Program.Rel("BoundAlpha", pv.BoundAlpha, pw.BoundAlpha * sc, 2e-3));
                        // (the wrap's distance queries take ties and rounding in the order of the input's triangles: another wrap of the same band)
                        if (vn == "permuted" || vn == "flipped") Program.Report(S, tag + " same band", Program.Rel("volume", Checks.Volume(pv.Result), Checks.Volume(first), 0.05));
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
            // a pin the input blocks, and a pin that only touches: never a Success
            {
                DMesh3 tube = Tube(16, 1.0, 0.4, 0.5);
                foreach (var (what, radius) in new[] { ("pin wider than the hole", 0.45), ("pin of the hole's inradius", 0.4 * Math.Cos(Math.PI / 16)), ("pin just below the inradius", 0.4 * Math.Cos(Math.PI / 16) * 0.98) }) {
                    PassageWrap pw = Passage(tube, new[] { new PassagePin(new[] { new Vector3d(0, 0, -0.9), new Vector3d(0, 0, 0.9) }, radius) }); pw.Compute();
                    if (pw.Status == PassageWrap.PassageStatus.Success) Program.Report(S, "passage " + what + " | pins clear (independent)", radius >= 0.4 * Math.Cos(Math.PI / 16) ? "Success for a pin that the input blocks" : PinsIndependent(pw.Result, pw.Pins));
                    else { Program.Info(S, "passage " + what, pw.Status + " " + pw.Error); Program.Report(S, "passage " + what + " | no claimed pass", pw.AllPassed ? "AllPassed with " + pw.Status : ""); }
                }
            }
        }

        // ---- the sharp-feature pass, through reflection ----

        static readonly Type FeatureType = typeof(MeshAlphaWrap).Assembly.GetType("g3.AlphaWrapFeaturePass") ?? typeof(MeshAlphaWrap).Assembly.GetType("gs.AlphaWrapFeaturePass");

        sealed class Feature
        {
            public DMesh3 Mesh; public bool Completed; public int PlaneMoves, CreaseSnaps, Splits, Flips, Rejected, InputCreases;
        }

        static Feature RunFeature(DMesh3 wrap, DMesh3 input, double alpha, double offset)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object pass = Activator.CreateInstance(FeatureType, bf, null, new object[] { new DMesh3(wrap, true), input, alpha, offset }, null);
            bool done = (bool)FeatureType.GetMethod("Compute", bf).Invoke(pass, null);
            int I(string n) => (int)FeatureType.GetField(n, bf).GetValue(pass);
            return new Feature { Mesh = (DMesh3)FeatureType.GetField("Mesh", bf).GetValue(pass), Completed = done, PlaneMoves = I("PlaneMoves"), CreaseSnaps = I("CreaseSnaps"), Splits = I("Splits"), Flips = I("Flips"), Rejected = I("Rejected"), InputCreases = I("InputCreases") };
        }

        static DMesh3 LShape()
        {
            var poly = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(2, 1), new Vector2d(1, 1), new Vector2d(1, 2), new Vector2d(0, 2) };
            // a prism over a non-star polygon: fan from a point that sees every vertex
            var m = new DMesh3(); int n = poly.Count; var c = new Vector2d(0.5, 0.5);
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, -0.4));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 0.4));
            int cb = m.AppendVertex(new Vector3d(c.x, c.y, -0.4)), ct = m.AppendVertex(new Vector3d(c.x, c.y, 0.4));
            for (int i = 0; i < n; ++i) { int j = (i + 1) % n; m.AppendTriangle(cb, j, i); m.AppendTriangle(ct, n + i, n + j); m.AppendTriangle(i, j, n + j); m.AppendTriangle(i, n + j, n + i); }
            return m;
        }

        static void Features()
        {
            if (FeatureType == null) { Program.Report(S, "feature pass", "the type AlphaWrapFeaturePass was not found"); return; }
            var inputs = new (string, DMesh3)[] {
                ("box", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5))),
                ("rotated box", Shapes.Rotate(Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)), new Vector3d(1, 2, 3), 33)),
                ("star prism", Shapes.Prism(Shapes.Star(5, 1.2, 0.6), -0.4, 0.4)),
                ("L shape", LShape()),
                ("tube", Tube(8, 1.0, 0.5, 0.5)),
                ("tet", Shapes.Tet(new Vector3d(0, 0, 0), new Vector3d(1.5, 0, 0), new Vector3d(0, 1.3, 0), new Vector3d(0.2, 0.3, 1.1))),
                ("two boxes", Shapes.Combine(Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)), Shapes.Box(new Vector3d(0.9, 0.5, 0.4), new Vector3d(0.5, 0.6, 0.3)))),
                ("open box", RepairStage.Holes(Shapes.GridBox(Vector3d.Zero, new Vector3d(1, 0.7, 0.5), 5), 6, 3)),
                ("cylinder", Shapes.Cylinder(Vector3d.Zero, 0.7, 1.5, 20)),
                ("thin plate", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 0.02))),
            };
            foreach (var (name, input) in inputs)
            foreach (var (ra, ro) in new[] { (20.0, 600.0), (30.0, 300.0), (12.0, 120.0) }) {
                string c = $"feature {name} alpha 1/{ra} offset 1/{ro}";
                try {
                    var w = new MeshAlphaWrap(new DMesh3(input)) { RelativeAlpha = ra, RelativeOffset = ro };
                    if (!w.Compute() || w.Status != MeshAlphaWrap.WrapStatus.Success) { Program.Info(S, c, "wrap " + w.Status); continue; }
                    Feature f = Program.Timed(() => RunFeature(w.Result, input, w.UsedAlpha, w.UsedOffset), out long ms);
                    Program.Report(S, c + " | time", "", ms);
                    Program.Report(S, c + " | completed", f.Completed ? "" : "Compute() false without a cancel");
                    int ops = f.PlaneMoves + f.CreaseSnaps + f.Splits + f.Flips;
                    if (ops == 0) Program.Info(S, c, $"no operation (creases {f.InputCreases}, rejected {f.Rejected})");
                    DMesh3 r = f.Mesh;
                    Program.Report(S, c + " | solid", Checks.Solid(r));
                    Program.Report(S, c + " | input inside", ChainStage.InputInside(input, r));
                    Program.Report(S, c + " | Euler", Checks.Euler(r) == Checks.Euler(w.Result) ? "" : $"{Checks.Euler(r)} vs the wrap {Checks.Euler(w.Result)}");
                    double near = double.MaxValue; foreach (int v in r.VertexIndices()) near = Math.Min(near, Checks.DistanceTo(input, r.GetVertex(v)));
                    Program.Report(S, c + " | vertices keep 0.9 offset", near >= 0.9 * w.UsedOffset * (1 - 1e-6) ? "" : $"a vertex {near / w.UsedOffset:F4} offsets from the input");
                    // no triangle of the result touches the input: sampled (centroids and edge midpoints stay off the input)
                    double tn = double.MaxValue; foreach (int t in r.TriangleIndices()) tn = Math.Min(tn, Checks.DistanceTo(input, r.GetTriCentroid(t)));
                    Program.Report(S, c + " | centroids off the input", tn > 0 ? "" : "a centroid on the input");
                    Program.Report(S, c + " | volume not below the input's hull part", Checks.ClosedManifold(input) != "" || Checks.Volume(r) > Checks.Volume(input) * (name == "two boxes" ? 0.5 : 1) ? "" : $"volume {Checks.Volume(r):R} below the input's {Checks.Volume(input):R}");
                    // repeat: deterministic
                    Feature again = RunFeature(w.Result, input, w.UsedAlpha, w.UsedOffset);
                    Program.Report(S, c + " | repeat", BooleanStage.SameBits(r, again.Mesh, 1));
                    // 2^k
                    foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                        DMesh3 si = Shapes.Map(input, q => q * s);
                        var ws = new MeshAlphaWrap(new DMesh3(si)) { RelativeAlpha = ra, RelativeOffset = ro }; ws.Compute();
                        Feature fs = RunFeature(ws.Result, si, ws.UsedAlpha, ws.UsedOffset);
                        Program.Report(S, c + " | scale " + s, BooleanStage.SameBits(r, fs.Mesh, s));
                    }
                    // a second pass on the result keeps every guarantee
                    Feature second = RunFeature(r, input, w.UsedAlpha, w.UsedOffset);
                    Program.Report(S, c + " | second pass solid", Checks.Solid(second.Mesh));
                    Program.Report(S, c + " | second pass input inside", ChainStage.InputInside(input, second.Mesh));
                    // the input permuted and flipped: the pass reads planes and creases of the input only
                    foreach (var (vn, vi) in new[] { ("permuted input", Shapes.Permute(input, 6)), ("flipped input", Flipped(input)) }) {
                        Feature fv = RunFeature(w.Result, vi, w.UsedAlpha, w.UsedOffset);
                        Program.Report(S, c + " | " + vn + " solid", Checks.Solid(fv.Mesh));
                        Program.Report(S, c + " | " + vn + " input inside", ChainStage.InputInside(input, fv.Mesh));
                        string same = BooleanStage.SameBits(r, fv.Mesh, 1);
                        if (same != "") Program.Info(S, c + " | " + vn, $"another result ({fv.PlaneMoves}/{fv.CreaseSnaps}/{fv.Splits}/{fv.Flips} vs {f.PlaneMoves}/{f.CreaseSnaps}/{f.Splits}/{f.Flips} moves/snaps/splits/flips)");
                    }
                }
                catch (Exception e) { Exception x = e.InnerException ?? e; Program.Report(S, c, "threw " + x.GetType().Name + ": " + x.Message + " at " + (x.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        static DMesh3 Flipped(DMesh3 m) { var f = new DMesh3(m); f.ReverseOrientation(); return f; }

        // ---- options of MeshAlphaWrap ----

        static void Options()
        {
            var inputs = new (string, DMesh3)[] {
                ("sphere", Shapes.Sphere(Vector3d.Zero, 1, 8)),
                ("tube", Tube(12, 1.0, 0.5, 0.5)),
                ("star", Shapes.Prism(Shapes.Star(6, 1.2, 0.6), -0.4, 0.4)),
                ("soup", Shapes.Combine(Shapes.Sphere(Vector3d.Zero, 1, 6), Shapes.Box(new Vector3d(0.8, 0.3, 0.2), new Vector3d(0.6, 0.5, 0.7)))),
                ("plate", Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 0.002))),
            };
            var options = new (string, Action<MeshAlphaWrap>)[] {
                ("PullPinchPoints", w => w.PullPinchPoints = true),
                ("no RefineNonManifold", w => w.RefineNonManifold = false),
                ("no angle refinement", w => w.RefineMinAngleDegrees = 0),
                ("angle 25", w => w.RefineMinAngleDegrees = 25),
                ("budget 2000", w => w.MaxEstimatedTriangles = 2000),
                ("budget 2000, coarsening 1.5", w => { w.MaxEstimatedTriangles = 2000; w.MaxAlphaCoarsening = 1.5; }),
                ("point limit 300", w => w.MaxSteinerPoints = 300),
                ("alpha field", w => w.AlphaField = p => 0.08 + 0.1 * Math.Abs(p.x)),
            };
            foreach (var (name, input) in inputs)
            foreach (var (on, opt) in options) {
                string c = $"option {name} | {on}";
                try {
                    Func<DMesh3, double, MeshAlphaWrap> run = (m, s) => { var w = new MeshAlphaWrap(new DMesh3(m)) { RelativeAlpha = 20, RelativeOffset = 300 }; opt(w); if (s != 1 && w.AlphaField != null) w.AlphaField = p => s * (0.08 + 0.1 * Math.Abs(p.x / s)); w.Compute(); return w; };
                    MeshAlphaWrap w0 = Program.Timed(() => run(input, 1), out long ms);
                    Program.Report(S, c + " | time", "", ms);
                    bool limit = w0.Status == MeshAlphaWrap.WrapStatus.PointLimitReached;
                    if (w0.Status != MeshAlphaWrap.WrapStatus.Success && !limit) { Program.Info(S, c, $"{w0.Status} {w0.Error}"); continue; }
                    if (limit) Program.Info(S, c, "PointLimitReached");
                    Program.Report(S, c + " | solid", Checks.Solid(w0.Result));
                    Program.Report(S, c + " | input inside", ChainStage.InputInside(input, w0.Result));
                    double near = double.MaxValue; foreach (int v in w0.Result.VertexIndices()) near = Math.Min(near, Checks.DistanceTo(input, w0.Result.GetVertex(v)));
                    if (!limit) Program.Report(S, c + " | vertices keep the offset", near >= w0.UsedOffset * (1 - 1e-6) ? "" : $"a vertex at {near / w0.UsedOffset:F4} offsets");
                    if (w0.MaxEstimatedTriangles > 0) Program.Report(S, c + " | coarsening", w0.AlphaCoarsening >= 1 && (w0.MaxAlphaCoarsening <= 0 || w0.AlphaCoarsening <= Math.Max(1, w0.MaxAlphaCoarsening) * (1 + 1e-12)) ? "" : $"AlphaCoarsening {w0.AlphaCoarsening:R}");
                    DMesh3 first = new DMesh3(w0.Result);
                    w0.Compute();
                    Program.Report(S, c + " | reuse", BooleanStage.SameBits(first, w0.Result, 1));
                    var one = new MeshAlphaWrap(new DMesh3(input)) { RelativeAlpha = 20, RelativeOffset = 300, MaxThreads = 1 }; opt(one); one.Compute();
                    Program.Report(S, c + " | one thread", one.Status == w0.Status ? BooleanStage.SameBits(first, one.Result, 1) : "status " + one.Status);
                    foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                        MeshAlphaWrap ws = run(Shapes.Map(input, q => q * s), s);
                        Program.Report(S, c + " | scale " + s, ws.Status == w0.Status ? BooleanStage.SameBits(first, ws.Result, s) : "status " + ws.Status);
                    }
                    // a permuted or flipped input: the guarantees again, the same band (not the same mesh: ties and rounding of the distance
                    // queries follow the order of the input's triangles)
                    foreach (var (vn, vm) in new[] { ("permuted", Shapes.Permute(input, 8)), ("flipped input", Flipped(input)) }) {
                        MeshAlphaWrap wv = run(vm, 1);
                        if (wv.Status != w0.Status) { Program.Report(S, c + " | " + vn, "status " + wv.Status); continue; }
                        Program.Report(S, c + " | " + vn + " solid", Checks.Solid(wv.Result));
                        Program.Report(S, c + " | " + vn + " input inside", ChainStage.InputInside(input, wv.Result));
                        Program.Report(S, c + " | " + vn + " same band", Program.Rel("volume", Checks.Volume(wv.Result), Checks.Volume(first), limit ? 0.5 : 0.05));
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        public static void Run()
        {
            Passages();
            Features();
            Options();
        }
    }
}
