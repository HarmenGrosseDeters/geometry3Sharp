using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace MagnitudeRobustness
{
    /// <summary>
    /// DualContouring under a scale of the field values (c f has the level set of f, #1376), under a uniform scale of field and
    /// geometry, and far above the float range (the 2^300 and 2^400 lines of #1377).
    /// </summary>
    public static class DcStage
    {
        static readonly (string name, Action<DualContouring> set)[] Configs = {
            ("default", null),
            ("Simplify off", dc => dc.Simplify = false),
            ("ExactHermite off", dc => dc.ExactHermite = false),
        };

        static IEnumerable<int> ValueExponents()
        {
            for (int e = -1070; e <= 1020; ++e)
                if (e % 20 == 0 || (e >= -160 && e <= -20) || (e >= 100 && e <= 140)) yield return e;
        }

        static Sig Guarded(Func<Sig> run)
        {
            try { return run(); } catch (Exception ex) { return new Sig { Status = "threw " + ex.GetType().Name }; }
        }

        // the volume of the mesh times 2^-e (the plain volume overflows above 2^341)
        static double VolumeScaled(DMesh3 m, int e)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) { Vector3d p = c.GetVertex(v); c.SetVertex(v, new Vector3d(Math.ScaleB(p.x, -e), Math.ScaleB(p.y, -e), Math.ScaleB(p.z, -e))); }
            return Volume(c);
        }

        static double Volume(DMesh3 m) => m.TriangleCount == 0 ? 0 : MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        /// <summary>
        /// Option (a) for a field, as a wrapper: the geometry (Bounds, CellSize, the argument of the field) by the power of two of
        /// the largest coordinate of Bounds, the values (and IsoValue) by the power of two of the largest |f - IsoValue| on the
        /// grid nodes (one more evaluation per node here; inside the class the grid is sampled anyway).
        /// </summary>
        public static DMesh3 Prescaled(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, double iso, Action<DualContouring> options, bool scaleValues = true)
        {
            double max = 0;
            for (int k = 0; k < 3; ++k) max = Math.Max(max, Math.Max(Math.Abs(box.Min[k]), Math.Abs(box.Max[k])));
            int kg = Math.ILogB(max);
            Vector3d Down(Vector3d p) => new Vector3d(Math.ScaleB(p.x, -kg), Math.ScaleB(p.y, -kg), Math.ScaleB(p.z, -kg));
            Vector3d Up(Vector3d p) => new Vector3d(Math.ScaleB(p.x, kg), Math.ScaleB(p.y, kg), Math.ScaleB(p.z, kg));
            var b = new AxisAlignedBox3d(Down(box.Min), Down(box.Max));
            double c = Math.ScaleB(cell, -kg);
            int kv = 0;
            if (scaleValues) {
                double vmax = 0;
                int ni = (int)Math.Ceiling((b.Max.x - b.Min.x) / c), nj = (int)Math.Ceiling((b.Max.y - b.Min.y) / c), nk = (int)Math.Ceiling((b.Max.z - b.Min.z) / c);
                for (int z = 0; z <= nk; ++z) for (int y = 0; y <= nj; ++y) for (int x = 0; x <= ni; ++x) {
                    double v = Math.Abs(f(Up(b.Min + c * new Vector3d(x, y, z))) - iso);
                    if (v > vmax && !double.IsInfinity(v)) vmax = v;
                }
                if (vmax > 0) kv = Math.ILogB(vmax);
            }
            var dc = new DualContouring { Implicit = new Ops.Field { F = q => Math.ScaleB(f(Up(q)), -kv), Box = b }, Bounds = b, CellSize = c, InsideIsLess = true, IsoValue = Math.ScaleB(iso, -kv) };
            options?.Invoke(dc);
            dc.Generate();
            DMesh3 m = dc.Mesh;
            foreach (int v in m.VertexIndices()) m.SetVertex(v, Up(m.GetVertex(v)));
            return m;
        }

        public static void Run()
        {
            // 1. the values times 2^e, the geometry fixed
            foreach (var (name, set) in Configs) {
                Sig unit = Ops.MeshSig(Ops.Dc(Ops.Sdf, Ops.UnitBox, 0.125, set).Mesh);
                var plain = new List<(int, string)>(); var pre = new List<(int, string)>();
                foreach (int e in ValueExponents()) {
                    double c = Math.ScaleB(1.0, e);
                    plain.Add((e, Program.Compare(unit, Guarded(() => Ops.MeshSig(Ops.Dc(p => c * Ops.Sdf(p), Ops.UnitBox, 0.125, set).Mesh)), 0)));
                    pre.Add((e, Program.Compare(unit, Guarded(() => Ops.MeshSig(Prescaled(p => c * Ops.Sdf(p), Ops.UnitBox, 0.125, 0, set))), 0)));
                }
                Console.WriteLine($"PLAIN DualContouring, values x 2^e, {name} ({unit.Ints.Length / 3} triangles at e = 0)\n    {Program.Runs(plain)}");
                Console.WriteLine($"PRESCALED DualContouring, values x 2^e, {name}\n    {Program.Runs(pre)}");
            }
            // 2. field and geometry times 2^e, prescaled (the plain sweep is the line of the stage sweep)
            {
                Sig unit = Ops.MeshSig(Ops.Dc(Ops.Sdf, Ops.UnitBox, 0.25).Mesh);
                var pre = new List<(int, string)>();
                foreach (int e in Program.Exponents()) {
                    if (e % 20 != 0) continue;
                    double s = Math.ScaleB(1.0, e);
                    if (s == 0 || double.IsInfinity(s * 2)) continue;
                    pre.Add((e, Program.Compare(unit, Guarded(() => Ops.MeshSig(Prescaled(p => s * Ops.Sdf(p / s), new AxisAlignedBox3d(Ops.UnitBox.Min * s, Ops.UnitBox.Max * s), 0.25 * s, 0, null))), e)));
                }
                Console.WriteLine($"PRESCALED DualContouring, field and geometry x 2^e\n    {Program.Runs(pre)}");
            }
            // 3. a field whose small values are not a common factor: the level sets of exp(-40 r^2)
            var big = new AxisAlignedBox3d(new Vector3d(-2.5, -2.5, -2.5), new Vector3d(2.5, 2.5, 2.5));
            Func<Vector3d, double> gauss = p => -Math.Exp(-40 * p.LengthSquared);
            foreach (double level in new[] { 1e-5, 1e-10, 1e-14, 1e-16, 1e-20, 1e-30, 1e-40, 1e-50 }) {
                double r = Math.Sqrt(-Math.Log(level) / 40), volume = 4.0 / 3 * Math.PI * r * r * r;
                DMesh3 a = Ops.Dc(gauss, big, 0.125, dc => dc.IsoValue = -level).Mesh, b = Prescaled(gauss, big, 0.125, -level, null);
                var mc = new MarchingCubes { Implicit = new Ops.Field { F = gauss, Box = big }, Bounds = big, CubeSize = 0.125, IsoValue = -level, RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 20 };
                mc.Generate();
                Console.WriteLine($"level {level:E0} of exp(-40 r^2): radius {r:F4}, |gradient| at the surface {80 * r * level:E1}, volume {volume:F4}; MarchingCubes {Volume(mc.Mesh):F4}; DualContouring {a.TriangleCount} triangles, volume {Volume(a):F4}; with the values prescaled by their maximum {b.TriangleCount} triangles, volume {Volume(b):F4}");
            }
            // 4. does prescaling the values change an ordinary result? (the same field, plain against the wrapper)
            {
                var fields = new (string, Func<Vector3d, double>, AxisAlignedBox3d, double)[] {
                    ("sphere distance", Ops.Sdf, Ops.UnitBox, 0.125),
                    ("1000 x sphere distance", p => 1000 * Ops.Sdf(p), Ops.UnitBox, 0.125),
                    ("box distance", p => Math.Max(Math.Abs(p.x) - 0.7, Math.Max(Math.Abs(p.y) - 0.9, Math.Abs(p.z) - 1.1)), Ops.UnitBox, 0.125),
                    ("torus distance", p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.9; return Math.Sqrt(q * q + p.z * p.z) - 0.35; }, Ops.UnitBox, 0.0625),
                    ("quartic blob x^4 + y^4 + z^4 - 1", p => p.x * p.x * p.x * p.x + p.y * p.y * p.y * p.y + p.z * p.z * p.z * p.z - 1, Ops.UnitBox, 0.125),
                };
                foreach (var (name, f, box, cell) in fields)
                    foreach (var (cn, set) in Configs) {
                        Sig a = Ops.MeshSig(Ops.Dc(f, box, cell, set).Mesh), b = Ops.MeshSig(Prescaled(f, box, cell, 0, set));
                        Console.WriteLine($"ordinary scale, {name}, {cn}: plain {a.Ints.Length / 3} triangles; the wrapper: {Program.Compare(a, b, 0)}");
                    }
            }
            // 5. the cost of the wrapper (one more evaluation of the field per grid node)
            foreach (double cell in new[] { 0.0625, 0.03125 }) {
                double T(Func<DMesh3> run) { var t = new List<double>(); for (int i = 0; i < 5; ++i) { var w = Stopwatch.StartNew(); run(); t.Add(w.Elapsed.TotalMilliseconds); } t.Sort(); return t[2]; }
                Ops.Dc(Ops.Sdf, Ops.UnitBox, cell); Prescaled(Ops.Sdf, Ops.UnitBox, cell, 0, null);
                double plain = T(() => Ops.Dc(Ops.Sdf, Ops.UnitBox, cell).Mesh), pre = T(() => Prescaled(Ops.Sdf, Ops.UnitBox, cell, 0, null)), geo = T(() => Prescaled(Ops.Sdf, Ops.UnitBox, cell, 0, null, false));
                Console.WriteLine($"cost, sphere distance, cell {cell}: plain {plain:F1} ms, geometry prescaled {geo:F1} ms, geometry and values prescaled {pre:F1} ms ({(pre - plain) / plain * 100:F1} %)");
            }
        }

        // ---------------------------------------------------------------- far above the float range

        static string Diff(DMesh3 a, DMesh3 b, int e)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"{b.VertexCount} vertices / {b.TriangleCount} triangles (unit: {a.VertexCount} / {a.TriangleCount})";
            int moved = 0, tris = 0; double worst = 0;
            foreach (int v in a.VertexIndices()) {
                Vector3d p = a.GetVertex(v), q = b.GetVertex(v);
                q = new Vector3d(Math.ScaleB(q.x, -e), Math.ScaleB(q.y, -e), Math.ScaleB(q.z, -e));
                if (p != q) { ++moved; worst = Math.Max(worst, (p - q).Length); }
            }
            foreach (int t in a.TriangleIndices()) if (a.GetTriangle(t) != b.GetTriangle(t)) ++tris;
            return moved == 0 && tris == 0 ? "exact" : $"{moved} of {a.VertexCount} vertices moved (at most {worst:E1} in unit lengths), {tris} of {a.TriangleCount} triangles with other indices";
        }

        public static void Big()
        {
            var settings = new (string name, Action<DualContouring> set)[] {
                ("default", null),
                ("Simplify off", dc => dc.Simplify = false),
                ("Simplify off, ResolveSelfIntersections off", dc => { dc.Simplify = false; dc.ResolveSelfIntersections = false; }),
                ("ExactHermite off", dc => dc.ExactHermite = false),
                ("ParallelCompute off", dc => dc.ParallelCompute = false),
            };
            // the simplification (Simplify, the default) changes at 2^262: its counters around that scale
            foreach (int e in new[] { 0, 255, 256, 257, 261, 262, 300, 400 }) {
                double s = Math.ScaleB(1.0, e);
                var box = new AxisAlignedBox3d(Ops.UnitBox.Min * s, Ops.UnitBox.Max * s);
                var ad = new AdaptiveDualContouring { Implicit = new Ops.Field { F = p => s * Ops.Sdf(p / s), Box = box }, Bounds = box, CellSize = 0.25 * s, InsideIsLess = true };
                ad.Generate();
                Console.WriteLine($"AdaptiveDualContouring at 2^{e}: uniform {ad.UniformVertices} vertices / {ad.UniformTriangles} triangles, clusters {ad.Clusters}, merged vertices {ad.MergedVertices}, check rounds {ad.CheckRounds}, rejected clusters {ad.RejectedClusters}, base repairs {ad.BaseRepairs}, result {ad.Mesh.VertexCount} / {ad.Mesh.TriangleCount}, closed {ad.Mesh.IsClosed()}, volume / s^3 {VolumeScaled(ad.Mesh, e):F6}");
            }
            foreach (var (name, set) in settings) {
                DMesh3 unit = Ops.Dc(Ops.Sdf, Ops.UnitBox, 0.25, set).Mesh;
                // (1) field and geometry, (2) the geometry only (values of order 1), (3) the values only
                var both = new List<(int, string)>(); var geo = new List<(int, string)>(); var val = new List<(int, string)>();
                for (int e = 100; e <= 1010; e += (e >= 120 && e < 135) || (e >= 250 && e < 345) || (e >= 500 && e < 520) ? 1 : 10) {
                    double s = Math.ScaleB(1.0, e);
                    var box = new AxisAlignedBox3d(Ops.UnitBox.Min * s, Ops.UnitBox.Max * s);
                    string R(Func<DMesh3> run, int scale) { try { return Diff(unit, run(), scale); } catch (Exception ex) { return "threw " + ex.GetType().Name; } }
                    both.Add((e, R(() => Ops.Dc(p => s * Ops.Sdf(p / s), box, 0.25 * s, set).Mesh, e)));
                    geo.Add((e, R(() => Ops.Dc(p => Ops.Sdf(p / s), box, 0.25 * s, set).Mesh, e)));
                    val.Add((e, R(() => Ops.Dc(p => s * Ops.Sdf(p), Ops.UnitBox, 0.25, set).Mesh, 0)));
                }
                Console.WriteLine($"DualContouring above 2^100, {name}\n  field and geometry x 2^e\n    {Program.Runs(both)}\n  geometry x 2^e, values of order 1\n    {Program.Runs(geo)}\n  values x 2^e, geometry of order 1\n    {Program.Runs(val)}");
            }
        }
    }
}
