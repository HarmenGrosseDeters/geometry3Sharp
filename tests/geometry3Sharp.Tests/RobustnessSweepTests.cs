using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Robustness sweep over the public operations, mostly upstream code (docs/QA.md 2.10, findings table in
    /// docs/ROBUSTNESS_SWEEP.md). Each operation runs
    /// - under every Similarity of Similarity.All() plus two extreme power-of-two scales: the result, mapped back to the
    ///   input's units, must equal the result on the original bit for bit for power-of-two scales (absolute thresholds
    ///   break this) and within a tolerance for the others (float code breaks this at UTM coordinates), and
    /// - with a NaN or infinite vertex and a NaN or infinite parameter: it must finish, must not throw from deep inside
    ///   (an ArgumentException from the operation's own type is accepted), must not spread non-finite coordinates to
    ///   vertices that were finite, and a non-finite parameter must give a failure status or a finite result.
    /// A case listed in KnownFailures must still fail (strict expected failure): a fix removes its entry. Known hangs
    /// are not run (not strict), and on .NET Framework the list is not strict (it is taken on net10). Set
    /// G3_SWEEP_REPORT=1 to print the outcome of every case instead of asserting.
    /// </summary>
    public class RobustnessSweepTests
    {
        readonly ITestOutputHelper output;
        public RobustnessSweepTests(ITestOutputHelper output) { this.output = output; }

        // ---- the signature of a result: values in the input's units ----

        public sealed class Sig
        {
            public readonly List<(string name, double value, int dim, bool count)> Values = new List<(string, double, int, bool)>();
            /// <summary> the operation reported failure (a status, false, or a documented empty result) </summary>
            public bool Failed;
            /// <summary> the mesh the operation produced or edited, if any (checked for spreading non-finite values) </summary>
            public DMesh3 Output;
            /// <summary> per-vertex values the operation produced without an output mesh (normals, UVs, distances), checked for spreading like Output </summary>
            public int NonFiniteValues = -1;
            public Sig PerVertex(IEnumerable<double> values) { NonFiniteValues = values.Count(x => double.IsNaN(x) || double.IsInfinity(x)); return this; }
            readonly Similarity s;
            public Sig(Similarity s) { this.s = s; }

            public Sig Count(string name, double n) { Values.Add((name, n, 0, true)); return this; }
            public Sig Scalar(string name, double v) { Values.Add((name, v, 0, false)); return this; }
            public Sig Length(string name, double v) { Values.Add((name, v / s.Scale, 1, false)); return this; }
            public Sig Area(string name, double v) { Values.Add((name, v / (s.Scale * s.Scale), 2, false)); return this; }
            public Sig Volume(string name, double v) { Values.Add((name, v / (s.Scale * s.Scale * s.Scale), 3, false)); return this; }
            public Sig Point(string name, Vector3d p)
            {
                Vector3d q = s.Inverse(p);
                for (int k = 0; k < 3; ++k) Values.Add((name + "." + "xyz"[k], q[k], 1, false));
                return this;
            }
            public Sig Direction(string name, Vector3d d)
            {
                Vector3d q = s.Rotation.Transpose() * d;
                for (int k = 0; k < 3; ++k) Values.Add((name + "." + "xyz"[k], q[k], 0, false));
                return this;
            }
            /// <summary>
            /// triangle count, area and, if closed, volume of a mesh (about the bounds center, accurate far from the
            /// origin; the volume of an open mesh depends on that reference point, which moves under rotation)
            /// </summary>
            public Sig Mesh(string name, DMesh3 m)
            {
                Output = m;
                Count(name + ".triangles", m.TriangleCount);
                Area(name + ".area", Invariance.Area(m));
                return m.IsClosed() ? Volume(name + ".volume", Invariance.Volume(m)) : this;
            }
        }

        public sealed class Op
        {
            public string Name;
            /// <summary> the input, of size about 1 around the origin (a fresh copy per call) </summary>
            public Func<DMesh3> Input;
            /// <summary> runs on the transformed input; param is the operation's scalar parameter, already in the transformed units </summary>
            public Func<DMesh3, Similarity, double, Sig> Run;
            /// <summary> the scalar parameter in the input's units (a length, mapped with Similarity.ApplyLength), NaN for none </summary>
            public double Param = double.NaN;
            public string ParamName = "";
            /// <summary> tolerance of non-exact similarities, in the input's units (the inputs have size about 1) </summary>
            public double Tol = 1e-6;
            /// <summary> relative tolerance of counts under non-exact similarities (rotation changes ties) </summary>
            public double CountTol = 0;
            /// <summary> false: power-of-two scales are compared with Tol too (the result varies in the last bits from run to run) </summary>
            public bool BitExact = true;
            /// <summary> the operation's own type: an ArgumentException thrown by its members is a documented rejection </summary>
            public Type Type;
            public override string ToString() => Name;
        }

        static DMesh3 Sphere(double r = 1, int n = 6) => Defects.Sphere(Vector3d.Zero, r, n);

        /// <summary> a sphere with the one-ring of a vertex removed: one hole </summary>
        static DMesh3 HoledSphere()
        {
            DMesh3 m = Sphere();
            int v = m.VertexIndices().OrderByDescending(vid => m.GetVertex(vid).z).ThenBy(vid => vid).First();
            foreach (int t in m.VtxTrianglesItr(v).ToList()) m.RemoveTriangle(t, true, false);
            return m;
        }

        /// <summary> the upper part of a sphere: a disc, for the parameterizations </summary>
        static DMesh3 Cap()
        {
            DMesh3 m = Sphere(1, 8);
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(t).z < 0.2) m.RemoveTriangle(t, true, false);
            return new DMesh3(m, true);
        }

        static EdgeLoop Hole(DMesh3 m) => new MeshBoundaryLoops(m).Loops[0];

        static readonly Vector3d Query = new Vector3d(0.3, 0.2, 2);
        static readonly Vector3d CutNormal = new Vector3d(1, 2, 3).Normalized;

        public static readonly Op[] Ops = new Op[] {
            new Op { Name = "MeshNormals", Type = typeof(MeshNormals), Input = () => Sphere(), Tol = 1e-6,
                Run = (m, s, p) => {
                    var n = new MeshNormals(m); n.Compute();
                    var sig = new Sig(s);
                    foreach (int v in m.VertexIndices()) sig.Direction("n" + v, n.Normals[v]);
                    return sig.PerVertex(m.VertexIndices().Select(v => n.Normals[v].x + n.Normals[v].y + n.Normals[v].z));
                } },
            new Op { Name = "MeshMeasurements", Type = typeof(MeshMeasurements), Input = () => Sphere(),
                Run = (m, s, p) => {
                    Vector2d va = MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex);
                    MeshQueries.EdgeLengthStats(m, out double min, out double max, out double avg);
                    var g = MeshMeasurements.Genus(m);
                    return new Sig(s).Area("area", va.y).Length("minEdge", min).Length("maxEdge", max).Length("avgEdge", avg)
                        .Count("genus", g.Valid ? g.Genus : -1);
                } },
            new Op { Name = "MeshPlaneCut", Type = typeof(MeshPlaneCut), Input = () => Sphere(), Param = 0.1, ParamName = "origin.x",
                Run = (m, s, p) => {
                    var cut = new MeshPlaneCut(m, s.Apply(new Vector3d(0, 0.05, 0.02)) + s.ApplyDirection(Vector3d.AxisX) * p, s.ApplyDirection(CutNormal));
                    var sig = new Sig(s) { Failed = cut.Cut() == false };
                    sig.Failed |= cut.FillHoles() == false;
                    return sig.Count("loops", cut.CutLoops.Count).Mesh("mesh", m);
                } },
            new Op { Name = "MinimalHoleFill", Type = typeof(MinimalHoleFill), Input = HoledSphere,
                Run = (m, s, p) => new Sig(s) { Failed = new MinimalHoleFill(m, Hole(m)).Apply() == false }.Mesh("mesh", m) },
            new Op { Name = "SimpleHoleFiller", Type = typeof(SimpleHoleFiller), Input = HoledSphere,
                Run = (m, s, p) => new Sig(s) { Failed = new SimpleHoleFiller(m, Hole(m)).Fill() == false }.Mesh("mesh", m) },
            new Op { Name = "AutoHoleFill", Type = typeof(AutoHoleFill), Input = HoledSphere,
                Run = (m, s, p) => new Sig(s) { Failed = new AutoHoleFill(m, Hole(m)).Apply() == false }.Mesh("mesh", m) },
            new Op { Name = "SmoothedHoleFill", Type = typeof(SmoothedHoleFill), Input = HoledSphere, Param = 0.15, ParamName = "TargetEdgeLength", Tol = 1e-5, CountTol = 0.02,
                Run = (m, s, p) => new Sig(s) { Failed = new SmoothedHoleFill(m, Hole(m)) { TargetEdgeLength = p }.Apply() == false }.Mesh("mesh", m) },
            new Op { Name = "Reducer.TriangleCount", Type = typeof(Reducer), Input = () => Sphere(),
                Run = (m, s, p) => { new Reducer(m).ReduceToTriangleCount(m.TriangleCount / 3); return new Sig(s).Mesh("mesh", m); } },
            new Op { Name = "Reducer.MaxDeviation", Type = typeof(Reducer), Input = () => Sphere(), Param = 0.02, ParamName = "MaxDeviation",
                Run = (m, s, p) => { new Reducer(m) { MaxDeviation = p }.ReduceToTriangleCount(4); return new Sig(s).Mesh("mesh", m); } },
            new Op { Name = "Remesher", Type = typeof(Remesher), Input = () => Sphere(), Param = 0.2, ParamName = "TargetEdgeLength", Tol = 1e-5, CountTol = 0.02,
                Run = (m, s, p) => {
                    var r = new Remesher(m); r.SetTargetEdgeLength(p);
                    for (int k = 0; k < 4; ++k) r.BasicRemeshPass();
                    return new Sig(s).Mesh("mesh", m);
                } },
            new Op { Name = "MeshIterativeSmooth", Type = typeof(MeshIterativeSmooth), Input = HoledSphere, Tol = 1e-5,
                Run = (m, s, p) => {
                    new MeshIterativeSmooth(m, m.VertexIndices().Where(v => !m.IsBoundaryVertex(v)).ToArray()) { Alpha = 0.5, Rounds = 5 }.Smooth();
                    return new Sig(s).Mesh("mesh", m);
                } },
            new Op { Name = "LaplacianMeshSmoother", Type = typeof(LaplacianMeshSmoother), Input = HoledSphere, Tol = 1e-5,
                Run = (m, s, p) => {
                    var sm = new LaplacianMeshSmoother(m);
                    foreach (int v in m.VertexIndices()) sm.SetConstraint(v, m.GetVertex(v), m.IsBoundaryVertex(v) ? 100 : 0.5, m.IsBoundaryVertex(v));
                    return new Sig(s) { Failed = sm.SolveAndUpdateMesh() == false }.Mesh("mesh", m);
                } },
            new Op { Name = "MeshAutoRepair", Type = typeof(MeshAutoRepair), Input = HoledSphere,
                Run = (m, s, p) => new Sig(s) { Failed = new MeshAutoRepair(m).Apply() == false }.Mesh("mesh", m) },
            new Op { Name = "MeshPostRepairCleanup", Type = typeof(MeshPostRepairCleanup), Input = () => Sphere(),
                Run = (m, s, p) => { new MeshPostRepairCleanup(m).Apply(); return new Sig(s).Mesh("mesh", m); } },
            new Op { Name = "DMeshAABBTree3", Type = typeof(DMeshAABBTree3), Input = () => Sphere(), Param = 0.3, ParamName = "query.x",
                Run = (m, s, p) => {
                    var tree = new DMeshAABBTree3(m, true);
                    Vector3d q = s.Apply(new Vector3d(0, Query.y, Query.z)) + s.ApplyDirection(Vector3d.AxisX) * p;
                    int tid = tree.FindNearestTriangle(q);
                    var sig = new Sig(s) { Failed = tid < 0 };
                    if (tid < 0) return sig;
                    DistPoint3Triangle3 d = MeshQueries.TriangleDistance(m, tid, q);
                    sig.Length("distance", d.Get()).Point("nearest", d.TriangleClosest);
                    var ray = new Ray3d(q, -s.ApplyDirection(Vector3d.AxisZ));
                    int hit = tree.FindNearestHitTriangle(ray);
                    sig.Count("hit", hit < 0 ? 0 : 1);
                    if (hit >= 0) sig.Length("rayT", MeshQueries.TriangleIntersection(m, hit, ray).RayParameter);
                    return sig.Count("inside", tree.IsInside(s.Apply(Vector3d.Zero)) ? 1 : 0).Scalar("winding", tree.WindingNumber(q));
                } },
            new Op { Name = "PointHashGrid3d", Type = typeof(PointHashGrid3d<int>), Input = () => Sphere(), Param = 0.3, ParamName = "radius",
                Run = (m, s, p) => {
                    var grid = new PointHashGrid3d<int>(p, -1);
                    foreach (int v in m.VertexIndices()) grid.InsertPoint(v, m.GetVertex(v));
                    Vector3d q = s.Apply(new Vector3d(0.1, 0.2, 0.95));
                    var nearest = grid.FindNearestInRadius(q, p, v => m.GetVertex(v).Distance(q));
                    return new Sig(s) { Failed = nearest.Key < 0 }.Count("nearest", nearest.Key).Length("distance", nearest.Value);
                } },
            new Op { Name = "MeshSignedDistanceGrid+MarchingCubes", Type = typeof(MeshSignedDistanceGrid), Input = () => Sphere(), Param = 0.1, ParamName = "CellSize", Tol = 5e-3, CountTol = 0.05,
                Run = (m, s, p) => {
                    var sdf = new MeshSignedDistanceGrid(m, p) { ExactBandWidth = 2 };
                    sdf.Compute();
                    // (cubes on the grid samples, as in the documented usage; serial: the parallel version appends the
                    // triangles in thread order, S31)
                    var iso = new DenseGridTrilinearImplicit(sdf);
                    var mc = new MarchingCubes() { Implicit = iso, IsoValue = 0, Bounds = iso.Bounds(), CubeSize = sdf.CellSize, ParallelCompute = false };
                    mc.Generate();
                    // (the grid is axis aligned: under rotation only the volume is comparable)
                    var sig = new Sig(s);
                    sig.Output = mc.Mesh;
                    return sig.Volume("volume", Invariance.Volume(mc.Mesh));
                } },
            new Op { Name = "MeshBoolean", Type = typeof(MeshBoolean), Input = () => Sphere(), Tol = 1e-8,
                Run = (m, s, p) => {
                    DMesh3 tool = Defects.Sphere(Vector3d.Zero, 0.7, 5);
                    foreach (int v in tool.VertexIndices()) tool.SetVertex(v, s.Apply(tool.GetVertex(v) + new Vector3d(0.6, 0.1, 0.05)));
                    var b = new MeshBoolean() { Target = m, Tool = tool };
                    var sig = new Sig(s) { Failed = b.Compute() == false };
                    return b.Result == null ? sig : sig.Mesh("result", b.Result);
                } },
            new Op { Name = "MeshIsoCurves", Type = typeof(MeshIsoCurves), Input = () => Sphere(), Param = 0.1, ParamName = "iso",
                Run = (m, s, p) => {
                    Vector3d o = s.Apply(Vector3d.Zero), n = s.ApplyDirection(CutNormal);
                    var iso = new MeshIsoCurves(m, x => (x - o).Dot(n) - p);
                    iso.Compute();
                    double len = iso.Graph.Edges().Sum(e => iso.Graph.GetVertex(e.a).Distance(iso.Graph.GetVertex(e.b)));
                    return new Sig(s).Count("vertices", iso.Graph.VertexCount).Length("length", len);
                } },
            new Op { Name = "MeshGeodesicDistance", Type = typeof(MeshGeodesicDistance), Input = () => Sphere(), Tol = 1e-6,
                Run = (m, s, p) => {
                    var g = new MeshGeodesicDistance(m);
                    var sig = new Sig(s) { Failed = g.Compute(new[] { 0 }) == false };
                    return sig.Failed ? sig : sig.Length("max", m.VertexIndices().Max(v => g.Distance[v])).PerVertex(m.VertexIndices().Select(v => g.Distance[v]));
                } },
            new Op { Name = "MeshHausdorffDistance", Type = typeof(MeshHausdorffDistance), Input = () => Sphere(), Tol = 1e-5,
                Run = (m, s, p) => {
                    DMesh3 b = Defects.Sphere(Vector3d.Zero, 1.05, 5);
                    foreach (int v in b.VertexIndices()) b.SetVertex(v, s.Apply(b.GetVertex(v) + new Vector3d(0.02, 0, 0)));
                    var h = new MeshHausdorffDistance(m, b);
                    var sig = new Sig(s) { Failed = h.Compute() == false };
                    return sig.Length("lower", h.LowerBound).Length("upper", h.UpperBound);
                } },
            new Op { Name = "MeshICP", Type = typeof(MeshICP), Input = () => Sphere(), Tol = 1e-5,
                Run = (m, s, p) => {
                    DMesh3 src = new DMesh3(m);
                    MeshTransforms.Translate(src, s.ApplyDirection(new Vector3d(0.03, -0.02, 0.01)) * s.Scale);
                    var icp = new MeshICP(src, new DMeshAABBTree3(m, true));
                    icp.Solve();
                    return new Sig(s) { Failed = icp.Converged == false }.Length("error", icp.Error);
                } },
            new Op { Name = "LSCMParameterization", Type = typeof(LSCMParameterization), Input = Cap, Tol = 1e-6,
                Run = (m, s, p) => {
                    ParameterizationResult r = new LSCMParameterization(m).Compute();
                    var sig = new Sig(s) { Failed = r.Status != ParameterizationStatus.Success };
                    if (sig.Failed) return sig;
                    var d = ParameterizationDistortion.Compute(m, r.UV);
                    return sig.Count("flipped", d.FlippedTriangles).Scalar("meanConformal", d.MeanConformal).PerVertex(m.VertexIndices().Select(v => r.UV[v].x + r.UV[v].y));
                } },
            new Op { Name = "BoundaryFirstFlattening", Type = typeof(BoundaryFirstFlattening), Input = Cap, Tol = 1e-6,
                Run = (m, s, p) => {
                    ParameterizationResult r = new BoundaryFirstFlattening(m).Compute();
                    var sig = new Sig(s) { Failed = r.Status != ParameterizationStatus.Success };
                    if (sig.Failed) return sig;
                    var d = ParameterizationDistortion.Compute(m, r.UV);
                    return sig.Count("flipped", d.FlippedTriangles).Scalar("meanConformal", d.MeanConformal).PerVertex(m.VertexIndices().Select(v => r.UV[v].x + r.UV[v].y));
                } },
        };

        public static IEnumerable<object[]> OpNames() => Ops.Select(o => new object[] { o.Name });
        static Op ByName(string name) => Ops.First(o => o.Name == name);

        /// <summary> Similarity.All() and two extreme powers of two (exact; features of 1e-7 and 1e+6 units) </summary>
        public static IEnumerable<Similarity> Similarities()
        {
            foreach (Similarity s in Similarity.All()) yield return s;
            yield return new Similarity("scale 2^-24", Math.Pow(2, -24), Matrix3d.Identity, Vector3d.Zero);
            yield return new Similarity("scale 2^20", Math.Pow(2, 20), Matrix3d.Identity, Vector3d.Zero);
        }

        public static IEnumerable<object[]> SimilarityCases() =>
            Ops.SelectMany(o => Similarities().Where(s => s.Name != "identity").Select(s => new object[] { o.Name, s.Name }));

        public static IEnumerable<object[]> NonFiniteCases()
        {
            foreach (Op o in Ops) {
                foreach (string c in new[] { "NaN vertex", "+Inf vertex" })
                    yield return new object[] { o.Name, c };
                if (double.IsNaN(o.Param) == false)
                    foreach (string c in new[] { "NaN param", "+Inf param", "-Inf param" })
                        yield return new object[] { o.Name, c };
            }
        }

        // ---- expected failures: "operation|case" -> finding id in docs/ROBUSTNESS_SWEEP.md ("hang R..": not run) ----

        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
            { "DMeshAABBTree3|scale 2^-24", "R17" },
            { "LaplacianMeshSmoother|rotated", "R21" },
            { "LaplacianMeshSmoother|rotated, scale 1e-3, utm", "R21" },
            { "MeshBoolean|rotated", "R6" },
            { "MeshBoolean|rotated, scale 1e-3, utm", "hang R6" },
            { "MeshBoolean|scale 1e-3", "R6" },
            { "MeshBoolean|scale 2^-10", "R6" },
            { "MeshBoolean|scale 2^-24", "R6" },
            { "MeshGeodesicDistance|rotated, scale 1e-3, utm", "R16" },
            { "MeshICP|scale 2^-24", "R8" },
            { "MeshIsoCurves|NaN param", "R15" },
            { "MeshIsoCurves|scale 2^-24", "R15" },
            { "MeshIterativeSmooth|+Inf vertex", "R13" },
            { "MeshIterativeSmooth|NaN vertex", "R13" },
            { "Reducer.MaxDeviation|scale 2^-24", "R14" },
            { "Reducer.TriangleCount|+Inf vertex", "R13" },
            { "Reducer.TriangleCount|NaN vertex", "R13" },
            { "Reducer.TriangleCount|rotated, scale 1e-3, utm", "R7" },
            { "Reducer.TriangleCount|scale 2^-24", "R14" },
            { "Reducer.TriangleCount|utm", "R7" },
        };

        static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

        static Sig RunOp(Op op, DMesh3 input, Similarity s, double param)
        {
            Sig sig = null;
            if (Timed.Finishes(() => sig = op.Run(input, s, param), Budget) == false)
                throw new TimeoutException($"did not finish within {Budget.TotalSeconds} s");
            return sig;
        }

        /// <summary> "" if the results agree, else the first difference </summary>
        static string Compare(Op op, Similarity s, Sig a, Sig b)
        {
            if (a.Failed != b.Failed) return $"status: failed={a.Failed} on the original, {b.Failed} here";
            if (a.Values.Count != b.Values.Count) return $"{a.Values.Count} values vs {b.Values.Count}";
            bool exact = s.IsExact && op.BitExact;
            for (int i = 0; i < a.Values.Count; ++i) {
                var x = a.Values[i]; var y = b.Values[i];
                if (exact) {
                    if (x.value.Equals(y.value) == false) return $"{x.name}: {x.value:R} vs {y.value:R} (not bit identical under an exact scale)";
                } else if (x.count) {
                    if ((Math.Abs(x.value - y.value) <= op.CountTol * Math.Max(1, Math.Abs(x.value))) == false) return $"{x.name}: {x.value} vs {y.value}";
                } else if ((Math.Abs(x.value - y.value) <= op.Tol) == false) {
                    return $"{x.name}: {x.value:R} vs {y.value:R} (difference {Math.Abs(x.value - y.value):E2} > {op.Tol:E0})";
                }
            }
            return "";
        }

        string SimilarityOutcome(Op op, Similarity s)
        {
            try {
                Sig a = RunOp(op, op.Input(), Similarity.Identity, op.Param);
                double param = double.IsNaN(op.Param) ? double.NaN : s.ApplyLength(op.Param);
                Sig b = RunOp(op, Invariance.Transformed(op.Input(), s), s, param);
                return Compare(op, s, a, b);
            } catch (Exception e) {
                return Describe(e);
            }
        }

        static string Describe(Exception e)
        {
            if (e is TimeoutException) return "hang: " + e.Message;
            var frame = new System.Diagnostics.StackTrace(e).GetFrames()?.FirstOrDefault(f => f.GetMethod() != null);
            string where = frame == null ? "?" : frame.GetMethod().DeclaringType?.Name + "." + frame.GetMethod().Name;
            return $"{e.GetType().Name} in {where}: {e.Message.Split('\n')[0]}";
        }

        /// <summary> t is the operation's type, one of its base types, or a type nested in one of them (closures) </summary>
        static bool IsOwnType(Type op, Type t)
        {
            for (; t != null; t = t.DeclaringType)
                for (Type b = op; b != null && b != typeof(object); b = b.BaseType)
                    if (t == b) return true;
            return false;
        }

        static int NonFiniteVertices(DMesh3 m) => m == null ? 0 : m.VertexIndices().Count(v => m.GetVertex(v).IsFinite == false);

        string NonFiniteOutcome(Op op, string c)
        {
            DMesh3 input = op.Input();
            double param = op.Param;
            if (c.EndsWith("vertex")) {
                // a vertex of a triangle (not the first one: some operations start there)
                int v = input.VertexIndices().Skip(input.VertexCount / 3).First();
                double bad = c.StartsWith("NaN") ? double.NaN : double.PositiveInfinity;
                input.SetVertex(v, new Vector3d(bad, 0.5, 0.5));
            } else {
                param = c.StartsWith("NaN") ? double.NaN : c.StartsWith("+") ? double.PositiveInfinity : double.NegativeInfinity;
            }
            int before = NonFiniteVertices(input);
            Sig sig;
            try {
                sig = RunOp(op, input, Similarity.Identity, param);
            } catch (Exception e) {
                bool rejection = e is ArgumentException && IsOwnType(op.Type, e.TargetSite?.DeclaringType);
                return rejection ? "" : Describe(e);
            }
            // (an output mesh may keep the bad input vertex; more is spreading)
            if (sig.Output != null) {
                int after = NonFiniteVertices(sig.Output);
                if (after > before) return $"silent garbage: {after} non-finite vertices in the output, {before} in the input";
            }
            if (sig.NonFiniteValues > before && sig.Failed == false)
                return $"silent garbage: {sig.NonFiniteValues} non-finite per-vertex values with success, {before} non-finite vertices in the input";
            if (c.EndsWith("param") && sig.Failed == false) {
                var bad = sig.Values.FirstOrDefault(x => double.IsNaN(x.value) || double.IsInfinity(x.value));
                if (bad.name != null) return $"silent garbage: success with {bad.name} = {bad.value}";
            }
            return "";
        }

        /// <summary>
        /// a known hang is not run in the normal suite: it would cost the whole budget and leave a thread spinning for
        /// the rest of the run (G3_SWEEP_REPORT=1 runs it)
        /// </summary>
        static bool SkipKnownHang(string key) =>
            Environment.GetEnvironmentVariable("G3_SWEEP_REPORT") != "1" && KnownFailures.TryGetValue(key, out string f) && f.StartsWith("hang");

        void Expect(string key, string outcome)
        {
            if (Environment.GetEnvironmentVariable("G3_SWEEP_REPORT") == "1") {
                output.WriteLine($"SWEEP {key} => {(outcome == "" ? "ok" : outcome)}");
                Console.WriteLine($"SWEEP {key} => {(outcome == "" ? "ok" : outcome)}");
                return;
            }
            if (KnownFailures.TryGetValue(key, out string finding)) {
#if NETFRAMEWORK
                // (the list is taken on net10, where double -> int casts saturate; .NET Framework casts NaN and out of
                // range values to int.MinValue, so a listed case may fail differently or not at all: not strict here)
                return;
#endif
                Assert.True(outcome != "", $"{key} passes now: remove its entry ({finding}) from KnownFailures and update docs/ROBUSTNESS_SWEEP.md");
            } else
                Assert.True(outcome == "", $"{key}: {outcome}");
        }

        [Theory]
        [MemberData(nameof(SimilarityCases))]
        public void Sweep_SameResultUnderSimilarity(string op, string similarity)
        {
            Similarity s = Similarities().First(x => x.Name == similarity);
            if (SkipKnownHang(op + "|" + similarity)) return;
            Expect(op + "|" + similarity, SimilarityOutcome(ByName(op), s));
        }

        [Theory]
        [MemberData(nameof(NonFiniteCases))]
        public void Sweep_NonFiniteInput(string op, string c)
        {
            if (SkipKnownHang(op + "|" + c)) return;
            Expect(op + "|" + c, NonFiniteOutcome(ByName(op), c));
        }

        [Fact]
        public void Sweep_KnownFailuresNameExistingCases()
        {
            var keys = new HashSet<string>(SimilarityCases().Concat(NonFiniteCases()).Select(x => x[0] + "|" + x[1]));
            foreach (string key in KnownFailures.Keys)
                Assert.True(keys.Contains(key), "unknown case " + key);
            // every finding id has a row in docs/ROBUSTNESS_SWEEP.md (found from the test directory upwards)
            string dir = AppContext.BaseDirectory, doc = null;
            for (; dir != null && doc == null; dir = System.IO.Path.GetDirectoryName(dir)) {
                string f = System.IO.Path.Combine(dir, "docs", "ROBUSTNESS_SWEEP.md");
                if (System.IO.File.Exists(f)) doc = System.IO.File.ReadAllText(f);
            }
            Assert.True(doc != null, "docs/ROBUSTNESS_SWEEP.md not found");
            foreach (string id in KnownFailures.Values.Select(f => f.Split(' ').Last()).Distinct())
                Assert.True(doc.Contains("| " + id + " |"), $"finding {id} has no row in docs/ROBUSTNESS_SWEEP.md");
        }

        // ---- self-test of the harness: operations with a known defect must fail, an invariant one must pass ----

        static Op SelfTestOp(string name, Func<DMesh3, Similarity, Sig> run) =>
            new Op { Name = name, Type = typeof(SelfTestThrower), Input = () => Sphere(), Run = (m, s, p) => run(m, s) };

        sealed class SelfTestThrower
        {
            public static void Reject(double x) { if (double.IsNaN(x)) throw new ArgumentException("rejected"); }
        }

        [Fact]
        public void Harness_InvariantOperation_PassesEverySimilarity()
        {
            Op op = SelfTestOp("invariant", (m, s) => {
                Vector3d c = m.VertexIndices().Aggregate(Vector3d.Zero, (a, v) => a + m.GetVertex(v)) / m.VertexCount;
                return new Sig(s).Mesh("mesh", m).Point("centroid", c).Length("edge", m.GetVertex(0).Distance(m.GetVertex(1)))
                    .Direction("normal", m.GetTriNormal(0)).Count("vertices", m.VertexCount);
            });
            op.Tol = 1e-5;     // (rotated, 1e-3, utm: the coordinates themselves resolve about 7e-7)
            foreach (Similarity s in Similarities())
                Assert.Equal("", SimilarityOutcome(op, s));
        }

        [Fact]
        public void Harness_AbsoluteThresholdAndFloat_AreFound()
        {
            // an absolute threshold changes a count under power-of-two scales; a float position moves at UTM
            Op threshold = SelfTestOp("threshold", (m, s) => new Sig(s).Count("long edges",
                m.EdgeIndices().Count(e => m.GetVertex(m.GetEdgeV(e).a).Distance(m.GetVertex(m.GetEdgeV(e).b)) > 0.3)));
            Assert.NotEqual("", SimilarityOutcome(threshold, Similarity.Pow2Small));
            Assert.NotEqual("", SimilarityOutcome(threshold, Similarity.Pow2Large));
            Op single = SelfTestOp("float", (m, s) => new Sig(s).Point("p", (Vector3d)(Vector3f)m.GetVertex(3)));
            Assert.Equal("", SimilarityOutcome(single, Similarity.Pow2Small));
            Assert.NotEqual("", SimilarityOutcome(single, Similarity.Utm));
            // a value with the wrong dimension (a length reported as an area) fails exact scales
            Op unit = SelfTestOp("unit", (m, s) => new Sig(s).Area("edge", m.GetVertex(0).Distance(m.GetVertex(1))));
            Assert.NotEqual("", SimilarityOutcome(unit, Similarity.Pow2Large));
        }

        [Fact]
        public void Harness_NonFiniteOutcomes_AreClassified()
        {
            // spreading to a neighbour, a deep exception, a hang-free rejection, and passing the bad vertex through
            Op spread = SelfTestOp("spread", (m, s) => {
                foreach (int v in m.VertexIndices().ToList())
                    if (m.GetVertex(v).IsFinite == false)
                        foreach (int w in m.VtxVerticesItr(v).ToList()) m.SetVertex(w, m.GetVertex(v));
                return new Sig(s).Mesh("mesh", m);
            });
            Assert.StartsWith("silent garbage", NonFiniteOutcome(spread, "NaN vertex"));
            Op values = SelfTestOp("values", (m, s) => new Sig(s).PerVertex(m.VertexIndices().Select(v => m.GetVertex(v).x + 0 * m.Vertices().Sum(p => p.x))));
            Assert.StartsWith("silent garbage", NonFiniteOutcome(values, "+Inf vertex"));
            Op deep = SelfTestOp("deep", (m, s) => { int[] a = new int[1]; return new Sig(s).Count("x", a[m.VertexCount]); });
            Assert.StartsWith("IndexOutOfRangeException", NonFiniteOutcome(deep, "NaN vertex"));
            Op reject = SelfTestOp("reject", (m, s) => { foreach (int v in m.VertexIndices()) SelfTestThrower.Reject(m.GetVertex(v).x); return new Sig(s); });
            Assert.Equal("", NonFiniteOutcome(reject, "NaN vertex"));
            Op passThrough = SelfTestOp("pass through", (m, s) => new Sig(s).Mesh("mesh", m));
            Assert.Equal("", NonFiniteOutcome(passThrough, "NaN vertex"));
        }

        [Theory]
        [MemberData(nameof(OpNames))]
        public void Sweep_OriginalInput_Succeeds(string op)
        {
            // the baseline: on the original input every operation reports success with finite values
            Sig sig = RunOp(ByName(op), ByName(op).Input(), Similarity.Identity, ByName(op).Param);
            Assert.False(sig.Failed, op + " failed on the original input");
            foreach (var v in sig.Values) Assert.True(double.IsNaN(v.value) == false && double.IsInfinity(v.value) == false, $"{op}: {v.name} = {v.value}");
            if (sig.Output != null) ResultChecks.AssertValid(sig.Output, op);
        }
    }
}
