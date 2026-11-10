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
            // ---- exact Boolean
            foreach (bool safe in new[] { true, false })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    bool s = safe; Ops o = op;
                    list.Add(new Case("boolean." + op.ToString().ToLowerInvariant() + (safe ? ".saferounding" : ".exact"), serial => Boolean(o, s, serial)));
                }
            list.Add(new Case("boolean.box-sphere", serial => BooleanBoxSphere(serial)));
            // ---- repair, wrap, solidify
            list.Add(new Case("repair.robust", serial => Repair(serial)));
            list.Add(new Case("alphawrap", serial => AlphaWrap(serial)));
            list.Add(new Case("solidify.winding", serial => Solidify(serial)));
            // ---- remeshing, simplification
            list.Add(new Case("remesher", serial => Remesh(serial)));
            list.Add(new Case("reducer", serial => Reduce(serial)));
            // ---- contouring
            list.Add(new Case("dualcontouring.blob", serial => DualContour(serial)));
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

        static Trace Boolean(Ops op, bool safe, bool serial)
        {
            DMesh3 a = Inputs.BumpySphere(10), b = Inputs.Sphere(new Vector3d(0.7, 0.2, 0.1), 0.9, 9);
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = safe };
            var t = new Trace();
            t.Add(boolean.Compute());
            if (boolean.Result != null) t.Add(boolean.Result);
            return t;
        }

        static Trace BooleanBoxSphere(bool serial)
        {
            DMesh3 a = Inputs.Box(Vector3d.Zero, new Vector3d(0.8, 0.7, 0.9)), b = Inputs.Sphere(new Vector3d(0.3, 0.1, 0.2), 0.85, 10);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            var t = new Trace();
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
            var r = new MeshRobustRepair(BrokenInput());
            var t = new Trace();
            t.Add(r.Apply());
            t.Add(r.UsedExactResolve); t.Add(r.UsedSolidify); t.Add(r.UsedAlphaWrap);
            if (r.Mesh != null) t.Add(r.Mesh);
            return t;
        }

        static Trace AlphaWrap(bool serial)
        {
            var w = new MeshAlphaWrap(Inputs.BumpySphere(7)) { RelativeAlpha = 12, RelativeOffset = 120 };
            var t = new Trace();
            t.Add(w.Compute());
            if (w.Result != null) t.Add(w.Result);
            return t;
        }

        static Trace Solidify(bool serial)
        {
            var s = new MeshWindingNumberSolidify(BrokenInput()) { GridResolution = 48, Adaptive = true };
            var t = new Trace();
            t.Add(s.Compute());
            if (s.Result != null) t.Add(s.Result);
            return t;
        }

        // ---------------------------------------------------------------- remeshing

        static Trace Remesh(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(10);
            var remesher = new Remesher(m) { EnableParallelProjection = !serial, EnableParallelSmooth = !serial };
            remesher.SetTargetEdgeLength(0.18);
            remesher.SetProjectionTarget(MeshProjectionTarget.Auto(m));
            for (int pass = 0; pass < 6; ++pass) remesher.BasicRemeshPass();
            var t = new Trace();
            t.Add(m);
            return t;
        }

        static Trace Reduce(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(14);
            var reducer = new Reducer(m);
            reducer.ReduceToTriangleCount(300);
            var t = new Trace();
            t.Add(m);
            return t;
        }

        // ---------------------------------------------------------------- contouring

        static Trace DualContour(bool serial)
        {
            var dc = new DualContouring() {
                Implicit = new Inputs.BlobSDF(), IsoValue = 0, InsideIsLess = true, CellSize = 0.12,
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
            ParameterizationResult r;
            switch (method) {
                case "lscm": r = new LSCMParameterization(m).Compute(); break;
                case "arap": r = new ARAPParameterization(m).Compute(); break;
                case "slim": r = new SLIMParameterization(m).Compute(); break;
                case "bff": r = new BoundaryFirstFlattening(m).Compute(); break;
                default: r = new TutteEmbedding(m).Compute(); break;
            }
            var t = new Trace();
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
            t.Add(g.Compute(new[] { Inputs.Nearest(m, new Vector3d(1, 0, 0)) }));
            if (g.Distance != null) t.Add(g.Distance);
            return t;
        }

        static Trace GeodesicHeatIntrinsic(bool serial)
        {
            DMesh3 m = Inputs.Grid(14, 12, 0.15, 5, true);
            var g = new MeshGeodesicDistance(m) { IntrinsicRefine = true };
            var t = new Trace();
            t.Add(g.Compute(new[] { Inputs.Nearest(m, new Vector3d(1, 1, 0)) }));
            if (g.Distance != null) t.Add(g.Distance);
            return t;
        }

        static Trace LogMap(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(10);
            var vhm = new VectorHeatMethod(m);
            var t = new Trace();
            foreach (Vector2d v in vhm.ComputeLogMap(Inputs.Nearest(m, new Vector3d(0, 1, 0)))) t.Add(v);
            return t;
        }

        static Trace GeodesicPath(bool serial)
        {
            DMesh3 m = Inputs.BumpySphere(12);
            var g = new MeshGeodesicPath(m);
            var t = new Trace();
            t.Add((int)g.Compute(Inputs.Nearest(m, new Vector3d(1, 0, 0)), Inputs.Nearest(m, new Vector3d(-0.5, 0.7, -0.6))));
            t.Add(g.Length);
            foreach (Vector3d p in g.Path) t.Add(p);
            return t;
        }
    }
}
