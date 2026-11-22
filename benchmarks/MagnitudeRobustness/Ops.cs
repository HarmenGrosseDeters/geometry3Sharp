using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MagnitudeRobustness
{
    /// <summary> What one run of an operation returned, split by physical dimension so that it can be compared with the result of a scaled input </summary>
    public sealed class Sig
    {
        public string Status = "";
        /// <summary> the status is a success (the result is meant to be used) </summary>
        public bool Ok;
        public int[] Ints = Array.Empty<int>();
        /// <summary> dimensionless values (angles, unit vectors): the same bits at every scale </summary>
        public double[] Pure = Array.Empty<double>();
        /// <summary> lengths and coordinates: times the scale </summary>
        public double[] Lengths = Array.Empty<double>();
    }

    /// <summary> An operation on a fixed input; unit is the scale of its parameters that are lengths </summary>
    public sealed class Op
    {
        public string Name;
        public DMesh3 Mesh;
        public Func<DMesh3, double, Sig> Run;
        /// <summary> part of the false-success sweep of #1377 (the others are controls) </summary>
        public string Issue = "#1377";
    }

    public static class Ops
    {
        public static DMesh3 Sphere(Vector3d center, double radius, int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        /// <summary> the sheared 3 x 3 grid of the comment on #1352 </summary>
        public static DMesh3 ShearedGrid(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i + 0.375 * j, j, 0));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
            return m;
        }

        public static DMesh3 Scaled(DMesh3 mesh, double s)
        {
            var c = new DMesh3(mesh);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, c.GetVertex(v) * s);
            return c;
        }

        static double[] Flat(IEnumerable<Vector3d> pts) => pts.SelectMany(p => new[] { p.x, p.y, p.z }).ToArray();
        static double[] Flat(IEnumerable<Vector2d> pts) => pts.SelectMany(p => new[] { p.x, p.y }).ToArray();

        static double[] EdgeLengths(IntrinsicTriangulation t) => Enumerable.Range(0, t.HalfedgeCount).Select(t.Length).ToArray();

        public sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box;
            public double Value(ref Vector3d p) => F(p);
            public AxisAlignedBox3d Bounds() => Box;
        }

        public static readonly AxisAlignedBox3d UnitBox = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
        public static double Sdf(Vector3d p) => (p - new Vector3d(0.03, -0.02, 0.05)).Length - 1;

        public static DualContouring Dc(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, Action<DualContouring> options = null)
        {
            var dc = new DualContouring { Implicit = new Field { F = f, Box = box }, Bounds = box, CellSize = cell, InsideIsLess = true };
            options?.Invoke(dc);
            dc.Generate();
            return dc;
        }

        public static Sig MeshSig(DMesh3 m)
        {
            return new Sig { Status = m.TriangleCount == 0 ? "empty" : "mesh", Ok = true, Lengths = Flat(m.VertexIndices().Select(m.GetVertex)),
                Ints = m.TriangleIndices().SelectMany(t => { Index3i i = m.GetTriangle(t); return new[] { i.a, i.b, i.c }; }).ToArray() };
        }

        public static List<Op> All()
        {
            DMesh3 sphere = Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 6), fine = Sphere(Vector3d.Zero, 1, 12), grid = ShearedGrid(3);
            int last = sphere.MaxVertexID - 1;
            var ops = new List<Op>();
            void Add(string name, DMesh3 mesh, Func<DMesh3, double, Sig> run, string issue = "#1377") => ops.Add(new Op { Name = name, Mesh = mesh, Run = run, Issue = issue });

            Add("IntrinsicTriangulation tangent bases and ToWorld", sphere, (m, u) => {
                var t = new IntrinsicTriangulation(m); t.FlipToDelaunay();
                var vals = new List<double>();
                foreach (int vid in m.VertexIndices()) {
                    if (Environment.GetEnvironmentVariable("BASISDUMP") != null && vid < 6 && u == 1) { t.GetTangentBasis(vid, out Vector3d d1, out Vector3d d2, out Vector3d dn); Console.WriteLine($"DUMP {vid} n {dn.x:R} {dn.y:R} {dn.z:R} e1 {d1.x:R} {d1.y:R} {d1.z:R}"); }
                    t.GetTangentBasis(vid, out Vector3d e1, out Vector3d e2, out Vector3d n);
                    vals.AddRange(new[] { e1.x, e1.y, e1.z, e2.x, e2.y, e2.z, n.x, n.y, n.z });
                    Vector3d w = t.ToWorld(vid, new Vector2d(0.3, 0.7));
                    vals.AddRange(new[] { w.x, w.y, w.z });
                }
                return new Sig { Status = "basis", Ok = true, Pure = vals.ToArray() };
            }, "#1377");
            Add("IntrinsicTriangulation DelaunayRefine edges and layouts", fine, (m, u) => {
                var t = new IntrinsicTriangulation(m); IntrinsicRefinementResult r = t.DelaunayRefine(25);
                var lay = new List<double>();
                for (int k = 0; k < t.TriangleCount; ++k) { t.GetTriangleLayout(k, out Vector2d a, out Vector2d b, out Vector2d c); lay.AddRange(new[] { b.x, c.x, c.y }); }
                return new Sig { Status = r.Status.ToString(), Ok = true, Ints = new[] { r.InsertedVertices, r.Flips }, Lengths = lay.ToArray() };
            }, "#1377");
            Add("MeshGeodesicDistance", sphere, (m, u) => { var g = new MeshGeodesicDistance(m); bool ok = g.Compute(new[] { 0 }); return new Sig { Status = ok ? "true" : "false", Ok = ok, Lengths = ok ? g.Distance : Array.Empty<double>() }; });
            Add("MeshGeodesicDistance (mesh triangles)", sphere, (m, u) => { var g = new MeshGeodesicDistance(m) { IntrinsicDelaunay = false }; bool ok = g.Compute(new[] { 0 }); return new Sig { Status = ok ? "true" : "false", Ok = ok, Lengths = ok ? g.Distance : Array.Empty<double>() }; });
            Add("MeshGeodesicDistance.TracePath", sphere, (m, u) => { var g = new MeshGeodesicDistance(m); bool ok = g.Compute(new[] { 0 }); List<Vector3d> p = ok ? g.TracePath(77) : null; return new Sig { Status = ok ? "true" : "false", Ok = ok, Lengths = ok ? Flat(p) : Array.Empty<double>() }; });
            Add("PolyhedralGeodesicDistance", sphere, (m, u) => { var g = new PolyhedralGeodesicDistance(m); PolyhedralGeodesicStatus st = g.Compute(new[] { 0 }); bool ok = st == PolyhedralGeodesicStatus.Ok; return new Sig { Status = st.ToString(), Ok = ok, Lengths = ok ? g.Distance.Concat(new[] { g.TracePath(77).Length }).ToArray() : Array.Empty<double>() }; });
            Add("MeshGeodesicPath", sphere, (m, u) => { var f = new MeshGeodesicPath(m); GeodesicPathStatus st = f.Compute(0, 77); bool ok = st == GeodesicPathStatus.Success; return new Sig { Status = st.ToString(), Ok = ok, Ints = new[] { f.Flips, f.Iterations }, Lengths = ok ? new[] { f.Length }.Concat(Flat(f.Path)).ToArray() : Array.Empty<double>() }; });
            Add("MeshGeodesicBezier", sphere, (m, u) => { var b = new MeshGeodesicBezier(m); GeodesicPathStatus st = b.Compute(new[] { 0, last, 77 }, 6); bool ok = st == GeodesicPathStatus.Success; return new Sig { Status = st.ToString(), Ok = ok, Lengths = ok ? new[] { b.Length }.Concat(Flat(b.Points)).ToArray() : Array.Empty<double>() }; });
            Add("IntrinsicTriangulation.FlipToDelaunay", grid, (m, u) => { var t = new IntrinsicTriangulation(m); int flips = t.FlipToDelaunay(); return new Sig { Status = "flips", Ok = true, Ints = new[] { flips }, Lengths = EdgeLengths(t) }; }, "#1352");
            Add("IntrinsicTriangulation.DelaunayRefine", sphere, (m, u) => { var t = new IntrinsicTriangulation(m); IntrinsicRefinementResult r = t.DelaunayRefine(25); bool ok = r.Status == IntrinsicRefinementStatus.Success; return new Sig { Status = r.Status.ToString(), Ok = ok, Ints = new[] { r.InsertedVertices, r.Flips }, Pure = new[] { r.MinAngle }, Lengths = ok ? EdgeLengths(t) : Array.Empty<double>() }; });
            Add("ExtractCommonSubdivision", grid, (m, u) => { var t = new IntrinsicTriangulation(m); t.FlipToDelaunay(); IntrinsicCommonSubdivision c = t.ExtractCommonSubdivision(); bool ok = c.Status == CommonSubdivisionStatus.Success; return new Sig { Status = c.Status.ToString(), Ok = ok, Ints = new[] { c.Faces.Count, c.Points.Count }, Lengths = ok ? Flat(c.Points) : Array.Empty<double>() }; }, "#1352");
            Add("VectorHeatMethod.TransportVector", sphere, (m, u) => { Vector2d[] v = new VectorHeatMethod(m).TransportVector(0, new Vector2d(1, 0)); return new Sig { Status = "returned", Ok = true, Pure = Flat(v) }; });
            Add("VectorHeatMethod.ComputeLogMap", sphere, (m, u) => { Vector2d[] v = new VectorHeatMethod(m).ComputeLogMap(0); return new Sig { Status = "returned", Ok = true, Lengths = Flat(v) }; });
            Add("QuadRemesher", fine, (m, u) => { var q = new QuadRemesher(m) { TargetEdgeLength = 0.2 * u }; q.Compute(); bool ok = q.Status == QuadRemeshStatus.Success; return new Sig { Status = q.Status.ToString(), Ok = ok, Ints = ok ? q.Result.FaceIndices.ToArray() : Array.Empty<int>(), Lengths = ok ? q.Result.VertexCoordinates.ToArray() : Array.Empty<double>() }; });
            Add("DualContouring (field and geometry x s)", null, (m, u) => MeshSig(Dc(p => u * Sdf(p / u), new AxisAlignedBox3d(UnitBox.Min * u, UnitBox.Max * u), 0.25 * u).Mesh), "#1376");
            return ops;
        }
    }
}
