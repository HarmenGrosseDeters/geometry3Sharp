// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace DeterminismHarness
{
    sealed class Case
    {
        public string Name;
        public Func<bool, Trace> Run;       // serial?
        public Case(string name, Func<bool, Trace> run) { Name = name; Run = run; }
    }

    static class Cases
    {
        public static List<Case> All()
        {
            var list = new List<Case>();
            // ---- math library: each function over a fixed set of arguments
            list.Add(Libm("sin", Math.Sin, -20, 20));
            list.Add(Libm("cos", Math.Cos, -20, 20));
            list.Add(Libm("tan", Math.Tan, -1.5, 1.5));
            list.Add(Libm("asin", Math.Asin, -1, 1));
            list.Add(Libm("acos", Math.Acos, -1, 1));
            list.Add(Libm("atan", Math.Atan, -50, 50));
            list.Add(Libm("exp", Math.Exp, -30, 30));
            list.Add(Libm("log", Math.Log, 1e-6, 1e6));
            list.Add(Libm("log10", Math.Log10, 1e-6, 1e6));
            list.Add(Libm("sqrt", Math.Sqrt, 0, 1e6));
            list.Add(Libm("sinh", Math.Sinh, -8, 8));
            list.Add(Libm("cosh", Math.Cosh, -8, 8));
            list.Add(Libm("tanh", Math.Tanh, -8, 8));
            list.Add(Libm2("atan2", Math.Atan2, -10, 10, -10, 10));
            list.Add(Libm2("pow", Math.Pow, 0.1, 10, -5, 5));
            list.Add(Libm2("pow-int", Math.Pow, 0.5, 4, 1, 40, true));
            // ---- runtime probes (no library code): arithmetic, sorting with ties, number parsing and formatting
            list.Add(new Case("probe.arith-chain", serial => ProbeArith()));
            list.Add(new Case("probe.sort-ties", serial => ProbeSort()));
            list.Add(new Case("probe.double-parse", serial => ProbeParse()));
            list.Add(new Case("probe.double-format", serial => ProbeFormat()));
            list.Add(new Case("probe.float-cast", serial => ProbeFloat()));
            list.Add(new Case("io.obj-text", serial => IoObj()));
            list.Add(new Case("io.stl-text", serial => IoStl()));
            // ---- exact Boolean
            foreach (bool safe in new[] { true, false })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    bool s = safe; Ops o = op;
                    list.Add(new Case("boolean." + op.ToString().ToLowerInvariant() + (safe ? ".saferounding" : ".exact"), serial => Boolean(o, s, serial)));
                }
            list.Add(new Case("boolean.union.libm-input", serial => Boolean(Ops.Union, true, serial, true)));
            list.Add(new Case("boolean.box-sphere", serial => BooleanBoxSphere(serial)));
            // ---- repair, wrap, solidify
            list.Add(new Case("repair.robust", serial => Repair(serial)));
            list.Add(new Case("alphawrap", serial => AlphaWrap(serial)));
            list.Add(new Case("solidify.winding", serial => Solidify(serial)));
            // ---- remeshing, simplification
            list.Add(new Case("remesher", serial => Remesh(serial)));
            list.Add(new Case("remesher.libm-input", serial => Remesh(serial, true)));
            list.Add(new Case("reducer", serial => Reduce(serial)));
            // ---- contouring
            list.Add(new Case("dualcontouring.blob", serial => DualContour(serial)));
            list.Add(new Case("dualcontouring.blob.libm-input", serial => DualContour(serial, true)));
            list.Add(new Case("marchingcubes.blob", serial => MarchingCube(serial)));
            // ---- parameterization
            foreach (string m in new[] { "lscm", "arap", "slim", "bff", "tutte" }) {
                string method = m;
                list.Add(new Case("param." + method, serial => Param(method, serial)));
            }
            // ---- geodesics
            list.Add(new Case("geodesic.heat", serial => GeodesicHeat(serial)));
            list.Add(new Case("geodesic.heat.intrinsic", serial => GeodesicHeatIntrinsic(serial)));
            list.Add(new Case("geodesic.vectorheat.logmap", serial => LogMap(serial)));
            list.Add(new Case("geodesic.path", serial => GeodesicPath(serial)));
            return list;
        }

        // ---------------------------------------------------------------- runtime probes

        // + - * / and sqrt in long dependent chains (a*b+c patterns where a JIT could fuse, long chains where extended precision would show)
        static Trace ProbeArith()
        {
            var t = new Trace();
            var rng = new Rng(11);
            for (int rep = 0; rep < 256; ++rep) {
                double a = rng.Range(0.5, 2), b = rng.Range(0.5, 2), c = rng.Range(-1, 1), d = rng.Range(0.5, 2), acc = 0;
                for (int i = 0; i < 64; ++i) {
                    acc = acc * a + b * c;          // a * b + c
                    acc = (acc + d) / (1 + acc * acc);
                    acc = Math.Sqrt(Math.Abs(acc) + c * c) - b * d;
                }
                t.Add(acc);
                Vector3d u = new Vector3d(a, b, c), v = new Vector3d(d, c, a);
                t.Add(u.Dot(v)); t.Add(u.Cross(v)); t.Add(u.Normalized); t.Add((u - v).Length);
            }
            return t;
        }

        // List.Sort / Array.Sort are unstable: the order of equal keys is whatever the runtime's algorithm gives
        static Trace ProbeSort()
        {
            var t = new Trace();
            var rng = new Rng(5);
            foreach (int n in new[] { 20, 100, 1000 }) {
                int[] keys = new int[n], ids = new int[n];
                for (int i = 0; i < n; ++i) { keys[i] = (int)(rng.Next() % 7); ids[i] = i; }
                int[] k1 = (int[])keys.Clone(), i1 = (int[])ids.Clone();
                Array.Sort(k1, i1);
                t.Section("array-sort-keys-items-" + n); foreach (int x in i1) t.Add(x);
                var list = new List<int>(ids);
                list.Sort((x, y) => keys[x].CompareTo(keys[y]));
                t.Section("list-sort-comparison-" + n); foreach (int x in list) t.Add(x);
                int[] arr = (int[])ids.Clone();
                Array.Sort(arr, (x, y) => keys[x].CompareTo(keys[y]));
                t.Section("array-sort-comparison-" + n); foreach (int x in arr) t.Add(x);
                var ord = ids.OrderBy(x => keys[x]).ToArray();
                t.Section("linq-orderby-" + n); foreach (int x in ord) t.Add(x);
            }
            return t;
        }

        static Trace ProbeParse()
        {
            var t = new Trace();
            var rng = new Rng(9);
            for (int i = 0; i < 4096; ++i) {
                // decimal strings of 17 to 20 significant digits
                int digits = 17 + (i % 4);
                var sb = new System.Text.StringBuilder();
                sb.Append(rng.Next() % 9 + 1).Append('.');
                for (int k = 1; k < digits; ++k) sb.Append(rng.Next() % 10);
                sb.Append('e').Append((int)(rng.Next() % 40) - 20);
                string s = sb.ToString();
                double parsed = double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                t.Add(parsed);
                t.Add((double)float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                if (i >= 190 && i <= 192) t.Notes.Add("parse \"" + s + "\" -> 0x" + BitConverter.DoubleToInt64Bits(parsed).ToString("x16"));
            }
            return t;
        }

        static Trace ProbeFormat()
        {
            var t = new Trace();
            var rng = new Rng(3);
            for (int i = 0; i < 4096; ++i) {
                double x = rng.Range(-1000, 1000) * (i % 3 == 0 ? 1e-7 : 1);
                foreach (string f in new[] { "R", "G17", "F6", "E10" }) {
                    string s = x.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
                    foreach (char ch in s) t.Add((int)ch);
                    t.Add(-1);
                }
                if (i < 2) t.Notes.Add("format " + BitConverter.DoubleToInt64Bits(x).ToString("x16") + ": R=" + x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " G17=" + x.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + " default=" + x.ToString(System.Globalization.CultureInfo.InvariantCulture));
                // does "R" round-trip?
                t.Add(double.Parse(x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture) == x);
            }
            return t;
        }

        static Trace ProbeFloat()
        {
            var t = new Trace();
            var rng = new Rng(1);
            for (int i = 0; i < 4096; ++i) {
                double x = rng.Range(-1e5, 1e5);
                float f = (float)x;
                t.Add((double)f); t.Add((double)(f * f + f)); t.Add((double)(float)Math.Sqrt(x < 0 ? -x : x));
            }
            return t;
        }

        // the text formats: the characters the writer produces (hashed as their code points) and what the reader makes of them
        static Trace IoText(Func<System.IO.TextWriter, DMesh3, bool> write, Func<string, DMesh3> read)
        {
            DMesh3 m = Inputs.BumpySphere(6);
            var sw = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            var t = new Trace();
            t.Add(m, "input");
            t.Add(write(sw, m));
            string text = sw.ToString().Replace("\r\n", "\n");
            t.Section("text");
            foreach (char c in text) t.Add((int)c);
            DMesh3 back = read(text);
            t.Add(back, "readback");
            // does it come back exactly?
            t.Section("roundtrip-exact");
            bool exact = back != null && back.VertexCount == m.VertexCount;
            if (exact) { var a = m.VertexIndices().ToArray(); var b = back.VertexIndices().ToArray(); for (int i = 0; i < a.Length; ++i) exact &= m.GetVertex(a[i]) == back.GetVertex(b[i]); }
            t.Add(exact);
            t.Notes.Add("roundtrip exact: " + exact + "; text length " + text.Length + "; first vertex line: " + text.Split('\n').FirstOrDefault(l => l.StartsWith("v ") || l.Contains("vertex")));
            return t;
        }

        static Trace IoObj()
        {
            return IoText((w, m) => new OBJWriter().Write(w, new List<WriteMesh> { new WriteMesh(m) }, new WriteOptions()).code == IOCode.Ok,
                text => {
                    var builder = new DMesh3Builder();
                    new OBJReader().Read(new System.IO.StringReader(text), new ReadOptions(), builder);
                    return builder.Meshes.Count > 0 ? builder.Meshes[0] : null;
                });
        }

        static Trace IoStl()
        {
            return IoText((w, m) => new STLWriter().Write(w, new List<WriteMesh> { new WriteMesh(m) }, new WriteOptions()).code == IOCode.Ok,
                text => {
                    var builder = new DMesh3Builder();
                    new STLReader().Read(new System.IO.StringReader(text), new ReadOptions(), builder);
                    return builder.Meshes.Count > 0 ? builder.Meshes[0] : null;
                });
        }

        // ---------------------------------------------------------------- math library

        static Case Libm(string name, Func<double, double> f, double lo, double hi)
        {
            return new Case("libm." + name, serial => {
                var t = new Trace();
                var rng = new Rng(0xC0FFEEUL ^ (ulong)name.GetHashCodeStable());
                for (int i = 0; i < 4096; ++i) { double x = rng.Range(lo, hi); t.Add(x); t.Add(f(x)); }
                return t;
            });
        }

        static Case Libm2(string name, Func<double, double, double> f, double lo, double hi, double lo2, double hi2, bool integerSecond = false)
        {
            return new Case("libm." + name, serial => {
                var t = new Trace();
                var rng = new Rng(0xBEEFUL ^ (ulong)name.GetHashCodeStable());
                for (int i = 0; i < 4096; ++i) {
                    double x = rng.Range(lo, hi), y = rng.Range(lo2, hi2);
                    if (integerSecond) y = Math.Floor(y);
                    t.Add(x); t.Add(y); t.Add(f(x, y));
                }
                return t;
            });
        }

        static long GetHashCodeStable(this string s)
        {
            long h = 1469598103934665603L;
            foreach (char c in s) h = (h ^ c) * 1099511628211L;
            return h;
        }

        // ---------------------------------------------------------------- Boolean

        static Trace Boolean(Ops op, bool safe, bool serial, bool libm = false)
        {
            DMesh3 a = Inputs.BumpySphere(10, libm), b = Inputs.Sphere(new Vector3d(0.7, 0.2, 0.1), 0.9, 9);
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = safe };
            var t = new Trace();
            t.Add(a, "input-a"); t.Add(b, "input-b");
            t.Add(boolean.Compute());
            if (boolean.Result != null) t.Add(boolean.Result);
            return t;
        }

        static Trace BooleanBoxSphere(bool serial)
        {
            DMesh3 a = Inputs.Box(Vector3d.Zero, new Vector3d(0.8, 0.7, 0.9)), b = Inputs.Sphere(new Vector3d(0.3, 0.1, 0.2), 0.85, 10);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            var t = new Trace();
            t.Add(a, "input-a"); t.Add(b, "input-b");
            t.Add(boolean.Compute());
            if (boolean.Result != null) t.Add(boolean.Result);
            return t;
        }

        // ---------------------------------------------------------------- repair, wrap, solidify

        static DMesh3 BrokenInput()
        {
            DMesh3 broken = Inputs.BumpySphere(8);
            DMesh3 second = Inputs.Sphere(new Vector3d(0.9, 0.1, 0), 0.8, 8);
            MeshEditor.Append(broken, second);
            for (int tid = 0; tid < 6; ++tid) broken.RemoveTriangle(tid);
            return broken;
        }

        static Trace Repair(bool serial)
        {
            DMesh3 input = BrokenInput();
            var r = new MeshRobustRepair(input);
            var t = new Trace();
            t.Add(input, "input");
            t.Add(r.Apply());
            t.Add(r.UsedExactResolve); t.Add(r.UsedSolidify); t.Add(r.UsedAlphaWrap);
            if (r.Mesh != null) t.Add(r.Mesh);
            return t;
        }

        static Trace AlphaWrap(bool serial)
        {
            DMesh3 input = Inputs.BumpySphere(7);
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 12, RelativeOffset = 120 };
            var t = new Trace();
            t.Add(input, "input");
            t.Add(w.Compute());
            if (w.Result != null) t.Add(w.Result);
            return t;
        }

        static Trace Solidify(bool serial)
        {
            DMesh3 input = BrokenInput();
            var s = new MeshWindingNumberSolidify(input) { GridResolution = 48, Adaptive = true };
            var t = new Trace();
            t.Add(input, "input");
            t.Add(s.Compute());
            if (s.Result != null) t.Add(s.Result);
            return t;
        }

        // ---------------------------------------------------------------- remeshing

        static Trace Remesh(bool serial, bool libm = false)
        {
            DMesh3 m = Inputs.BumpySphere(10, libm);
            var t = new Trace();
            t.Add(m, "input");
            var remesher = new Remesher(m) { EnableParallelProjection = !serial, EnableParallelSmooth = !serial };
            remesher.SetTargetEdgeLength(0.18);
            remesher.SetProjectionTarget(MeshProjectionTarget.Auto(m));
            for (int pass = 0; pass < 6; ++pass) remesher.BasicRemeshPass();
            t.Add(m);
            return t;
        }

        static Trace Reduce(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(14);
            var t = new Trace();
            t.Add(m, "input");
            var reducer = new Reducer(m);
            reducer.ReduceToTriangleCount(300);
            t.Add(m);
            return t;
        }

        // ---------------------------------------------------------------- contouring

        static Trace DualContour(bool serial, bool libm = false)
        {
            var dc = new DualContouring() {
                Implicit = new Inputs.BlobSDF() { Libm = libm }, IsoValue = 0, InsideIsLess = true, CellSize = 0.12,
                Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.6), ParallelCompute = !serial
            };
            dc.Generate();
            var t = new Trace();
            t.Add(dc.Mesh);
            return t;
        }

        static Trace MarchingCube(bool serial)
        {
            var mc = new MarchingCubes() {
                Implicit = new Inputs.BlobSDF(), IsoValue = 0, CubeSize = 0.12,
                Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.6), ParallelCompute = !serial
            };
            mc.Generate();
            var t = new Trace();
            t.Add(mc.Mesh);
            return t;
        }

        // ---------------------------------------------------------------- parameterization

        static Trace Param(string method, bool serial)
        {
            DMesh3 m = Inputs.Grid(10, 8, 0.2, 77, true);
            var t = new Trace();
            t.Add(m, "input");
            ParameterizationResult r;
            switch (method) {
                case "lscm": r = new LSCMParameterization(m).Compute(); break;
                case "arap": r = new ARAPParameterization(m).Compute(); break;
                case "slim": r = new SLIMParameterization(m).Compute(); break;
                case "bff": r = new BoundaryFirstFlattening(m).Compute(); break;
                default: r = new TutteEmbedding(m).Compute(); break;
            }
            t.Section("uv");
            t.Add((int)r.Status);
            if (r.UV != null) foreach (Vector2d uv in r.UV) t.Add(uv);
            return t;
        }

        // ---------------------------------------------------------------- geodesics

        static Trace GeodesicHeat(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(12);
            var g = new MeshGeodesicDistance(m);
            var t = new Trace();
            t.Add(m, "input");
            t.Add(g.Compute(new[] { Inputs.Nearest(m, new Vector3d(1, 0, 0)) }));
            if (g.Distance != null) t.Add(g.Distance);
            return t;
        }

        static Trace GeodesicHeatIntrinsic(bool serial)
        {
            DMesh3 m = Inputs.Grid(14, 12, 0.15, 5, true);
            var g = new MeshGeodesicDistance(m) { IntrinsicRefine = true };
            var t = new Trace();
            t.Add(m, "input");
            t.Add(g.Compute(new[] { Inputs.Nearest(m, new Vector3d(1, 1, 0)) }));
            if (g.Distance != null) t.Add(g.Distance);
            return t;
        }

        static Trace LogMap(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(10);
            var vhm = new VectorHeatMethod(m);
            var t = new Trace();
            t.Add(m, "input");
            foreach (Vector2d v in vhm.ComputeLogMap(Inputs.Nearest(m, new Vector3d(0, 1, 0)))) t.Add(v);
            return t;
        }

        static Trace GeodesicPath(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(12);
            var g = new MeshGeodesicPath(m);
            var t = new Trace();
            t.Add(m, "input");
            t.Add((int)g.Compute(Inputs.Nearest(m, new Vector3d(1, 0, 0)), Inputs.Nearest(m, new Vector3d(-0.5, 0.7, -0.6))));
            t.Add(g.Length);
            foreach (Vector3d p in g.Path) t.Add(p);
            return t;
        }
    }
}
