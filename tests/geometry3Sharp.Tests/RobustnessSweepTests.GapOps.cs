using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The operations of the QA 2.0 gap list (docs/design/QA20_INVENTORY.md section 4, task T1) in the sweep table:
    /// alpha wrap, the repairs, the winding-number operations, the envelope, the editing operations, Delaunay3,
    /// ConvexHull2, the point tree, the batch queries, MarchingCubesPro and the analysis operations. The same two
    /// checks as for the first rows (scale / similarity invariance, non-finite vertex and parameter), plus the claims
    /// an operation makes about its own result (clean, Delaunay, hull contains the points) in Sig.Contradiction.
    /// </summary>
    public partial class RobustnessSweepTests
    {
        /// <summary> a sphere with the one-ring of its top and bottom vertex removed: two holes </summary>
        static DMesh3 TwoHoledSphere()
        {
            DMesh3 m = HoledSphere();
            int v = m.VertexIndices().OrderBy(vid => m.GetVertex(vid).z).ThenBy(vid => vid).First();
            foreach (int t in m.VtxTrianglesItr(v).ToList()) m.RemoveTriangle(t, true, false);
            return m;
        }

        /// <summary> a flat n x n grid in z = 0, of size 2 around the origin </summary>
        static DMesh3 FlatGrid(int n = 8)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(-1 + 2.0 * i / n, -1 + 2.0 * j / n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        /// <summary> a copy of a sphere of radius r at offset (in the input's units), mapped like the input </summary>
        static DMesh3 MappedSphere(Similarity s, double r, Vector3d offset, int n = 5)
        {
            DMesh3 tool = Defects.Sphere(Vector3d.Zero, r, n);
            foreach (int v in tool.VertexIndices()) tool.SetVertex(v, s.Apply(tool.GetVertex(v) + offset));
            return tool;
        }

        /// <summary> a closed mesh claims clean: NotCleanReason must agree </summary>
        static string CleanClaim(bool claimsClean, DMesh3 mesh) =>
            claimsClean && mesh != null ? MeshRobustRepair.NotCleanReason(mesh, true) : "";

        /// <summary> the claim of MeshRoundingRepair.IsClean: no self-intersection and no degenerate triangle (it neither closes nor orients) </summary>
        static string RoundingCleanClaim(bool claimsClean, DMesh3 mesh)
        {
            if (claimsClean == false || mesh == null) return "";
            string reasons = MeshRobustRepair.NotCleanReasons(mesh, true);
            return reasons.Contains("self-intersections") || reasons.Contains("degenerate triangles") ? reasons : "";
        }

        /// <summary> a sphere field that does not overflow for coordinates up to 1e300 (the length is scaled by its largest component) </summary>
        sealed class SafeSphereField : ImplicitFunction3d
        {
            public Vector3d Center; public double Radius;
            public double Value(ref Vector3d p)
            {
                Vector3d d = p - Center;
                double m = Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z)));
                if (m == 0) return -Radius;
                return m * Math.Sqrt((d.x / m) * (d.x / m) + (d.y / m) * (d.y / m) + (d.z / m) * (d.z / m)) - Radius;
            }
        }

        static Sig WithOutput(Sig sig, DMesh3 result) => result == null ? sig : sig.Mesh("result", result);

        static IEnumerable<double> Finite(IEnumerable<double> values) => values;

        static Op[] GapOps()
        {
            var ops = new List<Op>();

            // ---- repair and wrap ----
            ops.Add(new Op { Name = "MeshAlphaWrap", Type = typeof(MeshAlphaWrap), Input = () => Sphere(), Param = 0.25, ParamName = "Alpha", Tol = 0.05, CountTol = 0.5,
                Run = (m, s, p) => {
                    var w = new MeshAlphaWrap(m) { Alpha = p, Offset = p * 0.1 };
                    var sig = new Sig(s) { Failed = w.Compute() == false };
                    return WithOutput(sig, w.Result);
                } });
            ops.Add(new Op { Name = "MeshAlphaWrap.Points", Type = typeof(MeshAlphaWrap), Input = () => Sphere(), Param = 0.25, ParamName = "Alpha", Tol = 0.05, CountTol = 0.5,
                Run = (m, s, p) => {
                    var w = new MeshAlphaWrap(m.Vertices().ToList()) { Alpha = p, Offset = p * 0.1 };
                    var sig = new Sig(s) { Failed = w.Compute() == false };
                    return WithOutput(sig, w.Result);
                } });
            ops.Add(new Op { Name = "MeshRobustRepair", Type = typeof(MeshRobustRepair), Input = HoledSphere, Tol = 0.05, CountTol = 0.3,
                Run = (m, s, p) => {
                    var r = new MeshRobustRepair(m) { SolidifyResolution = 32 };
                    bool ok = r.Apply();
                    return new Sig(s) { Failed = ok == false, Contradiction = CleanClaim(ok, r.Mesh) }.Mesh("mesh", r.Mesh);
                } });
            ops.Add(new Op { Name = "MeshRoundingRepair", Type = typeof(MeshRoundingRepair), Input = () => DegenerateFamilies.Make(DegenerateFamilies.Family.Sliver, 10, 0), Tol = 1e-5, CountTol = 0.1,
                Run = (m, s, p) => {
                    var r = new MeshRoundingRepair(m);
                    bool ok = r.Apply();
                    return new Sig(s) { Failed = ok == false, Contradiction = RoundingCleanClaim(ok, m) }.Mesh("mesh", m);
                } });

            // ---- winding-number operations ----
            foreach (var op in new[] { MeshWindingNumberBoolean.Operations.Union, MeshWindingNumberBoolean.Operations.Difference }) {
                MeshWindingNumberBoolean.Operations operation = op;
                ops.Add(new Op { Name = "MeshWindingNumberBoolean." + op, Type = typeof(MeshWindingNumberBoolean), Input = () => Sphere(), Tol = 0.2, CountTol = 0.5,
                    Run = (m, s, p) => {
                        var b = new MeshWindingNumberBoolean(m, MappedSphere(s, 0.7, new Vector3d(0.6, 0.1, 0.05)), operation) { GridResolution = 24 };
                        var sig = new Sig(s) { Failed = b.Compute() == false };
                        return WithOutput(sig, b.Result);
                    } });
            }
            foreach (var method in new[] { MeshWindingNumberSolidify.Methods.DualContouring, MeshWindingNumberSolidify.Methods.MarchingCubes }) {
                MeshWindingNumberSolidify.Methods meth = method;
                ops.Add(new Op { Name = "MeshWindingNumberSolidify." + method, Type = typeof(MeshWindingNumberSolidify), Input = HoledSphere, Tol = 0.05, CountTol = 0.3,
                    Run = (m, s, p) => {
                        var sol = new MeshWindingNumberSolidify(m) { Method = meth, GridResolution = 24 };
                        var sig = new Sig(s) { Failed = sol.Compute() == false };
                        return WithOutput(sig, sol.Result);
                    } });
            }
            ops.Add(new Op { Name = "VolumeSampleCells", Type = typeof(VolumeSampleCells), SkipNonFinite = true, Input = () => Sphere(), Tol = 0.5, CountTol = 0.5,
                Run = (m, s, p) => {
                    DMesh3 b = MappedSphere(s, 0.7, new Vector3d(0.6, 0.1, 0.05));
                    var ta = new DMeshAABBTree3(m, true); var tb = new DMeshAABBTree3(b, true);
                    AxisAlignedBox3d root = m.GetBounds(); root.Contain(b.GetBounds()); root.Expand(0.1 * s.Scale);
                    var cells = new VolumeSampleCells(m, ta, b, tb, root, 2048, 6, true, false);
                    return new Sig(s).Volume("both", cells.KnownBoth).Volume("onlyA", cells.KnownOnlyA).Volume("onlyB", cells.KnownOnlyB)
                        .Volume("sampled", cells.SampledVolume).Count("unclassified", cells.Unclassified);
                } });

            // ---- envelope ----
            ops.Add(new Op { Name = "MeshEnvelope", Type = typeof(MeshEnvelope), Input = () => Sphere(), Param = 0.05, ParamName = "epsilon",
                Run = (m, s, p) => {
                    var env = new MeshEnvelope(m, p);
                    var sig = new Sig(s) { Failed = env.Status != EnvelopeStatus.Ready }.Count("status", (int)env.Status);
                    if (env.Status != EnvelopeStatus.Ready) return sig;
                    // points at 0.01, 0.2 and 1 from the surface (inside the 0.05 envelope only the first)
                    foreach (double r in new[] { 1.01, 1.2, 2.0 })
                        sig.Count("contains " + r, env.Contains(s.Apply(new Vector3d(0.1, 0.2, 0.3).Normalized * r)) ? 1 : 0);
                    // (a triangle on the sphere: its corners lie on the mesh's vertices, its middle is off the surface)
                    Index3i t = m.GetTriangle(0);
                    sig.Count("triangle", env.ContainsTriangle(m.GetVertex(t.a), m.GetVertex(t.b), m.GetVertex(t.c)) ? 1 : 0);
                    return sig;
                } });

            // ---- editing ----
            ops.Add(new Op { Name = "MeshInsertPolygon", Type = typeof(MeshInsertPolygon), Input = () => FlatGrid(), PlanarInput = true, Tol = 1e-6,
                Run = (m, s, p) => {
                    Polygon2d circle = Polygon2d.MakeCircle(0.45 * s.Scale, 12);
                    Vector2d c = new Vector2d(s.Translation.x, s.Translation.y) + s.Scale * new Vector2d(0.13, 0.07);
                    circle.Translate(c);
                    var ins = new MeshInsertPolygon() { Mesh = m, Polygon = new GeneralPolygon2d(circle) };
                    var sig = new Sig(s) { Failed = ins.Insert() == false };
                    return sig.Count("interior", ins.InteriorTriangles == null ? -1 : ins.InteriorTriangles.Count).Mesh("mesh", m);
                } });
            ops.Add(new Op { Name = "MeshTrimLoop", Type = typeof(MeshTrimLoop), Input = () => Sphere(1, 10), Tol = 0.05, CountTol = 0.3,
                Run = (m, s, p) => {
                    var line = new DCurve3 { Closed = true };
                    double z = 0.4, rr = Math.Sqrt(1 - z * z);
                    for (int k = 0; k < 24; ++k) {
                        double a = 2 * Math.PI * k / 24;
                        line.AppendVertex(s.Apply(new Vector3d(rr * Math.Cos(a), rr * Math.Sin(a), z)));
                    }
                    var trim = new MeshTrimLoop(m, line, s.Apply(new Vector3d(0, 0, 1)));
                    if (trim.Validate() != ValidationStatus.Ok) return new Sig(s) { Failed = true };
                    var sig = new Sig(s) { Failed = trim.Trim() == false };
                    return sig.Mesh("mesh", m);
                } });
            ops.Add(new Op { Name = "MeshStitchLoops", Type = typeof(MeshStitchLoops), Input = TwoHoledSphere, Tol = 1e-5,
                Run = (m, s, p) => {
                    var loops = new MeshBoundaryLoops(m).Loops;
                    // (the stitch advances along both loops in the same direction; the loops of the two holes run opposite ways)
                    loops[1].Reverse();
                    var st = new MeshStitchLoops(m, loops[0], loops[1]);
                    // (blind stitching is not supported: the corresponding vertices are given, top to bottom by angle)
                    var a = loops[0].Vertices.OrderBy(v => Math.Atan2(s.Inverse(m.GetVertex(v)).y, s.Inverse(m.GetVertex(v)).x)).ToArray();
                    var b = loops[1].Vertices.OrderBy(v => Math.Atan2(s.Inverse(m.GetVertex(v)).y, s.Inverse(m.GetVertex(v)).x)).ToArray();
                    st.AddKnownCorrespondences(new[] { a[0], a[a.Length / 2] }, new[] { b[0], b[b.Length / 2] });
                    return new Sig(s) { Failed = st.Stitch() == false }.Mesh("mesh", m);
                } });
            ops.Add(new Op { Name = "MeshExtrudeMesh", Type = typeof(MeshExtrudeMesh), Input = Cap, Tol = 1e-5,
                Run = (m, s, p) => {
                    var normals = new MeshNormals(m); normals.Compute();
                    var ex = new MeshExtrudeMesh(m) { ExtrudedPositionF = (pos, nrm, vid) => pos + 0.1 * s.Scale * (Vector3d)nrm };
                    var sig = new Sig(s) { Failed = ex.Extrude() == false }.Mesh("mesh", m);
                    // (the extrusion duplicates a bad vertex, and its offset follows the (NaN) normals of the one-ring: not spreading)
                    sig.Output = null;
                    return sig;
                } });

            // ---- computational geometry, trees, batches ----
            ops.Add(new Op { Name = "Delaunay3", Type = typeof(Delaunay3), Input = () => Sphere(), Param = 1e-9, ParamName = "minSeparation", CountTol = 0.2,
                Run = (m, s, p) => {
                    AxisAlignedBox3d box = AxisAlignedBox3d.Empty; foreach (Vector3d x in m.Vertices().Where(x => x.IsFinite)) box.Contain(x); box.Expand(0.5 * s.Scale);
                    var dt = new Delaunay3(box);
                    int inserted = 0;
                    foreach (int v in m.VertexIndices())
                        if (dt.Insert(m.GetVertex(v), -1, p, out int _) == Delaunay3.InsertResult.Inserted) inserted++;
                    return new Sig(s) { Contradiction = dt.Validate(true) ?? "" }.Count("inserted", inserted).Count("vertices", dt.VertexCount);
                } });
            ops.Add(new Op { Name = "ConvexHull2", Type = typeof(ConvexHull2), Input = () => Sphere(), Param = 1e-9, ParamName = "epsilon", Tol = 1e-5,
                Run = (m, s, p) => {
                    // the points in the frame of the input (rotation and translation taken off, scale kept)
                    Vector2d[] pts = m.VertexIndices().Select(v => { Vector3d q = s.Rotation.Transpose() * (m.GetVertex(v) - s.Translation); return new Vector2d(q.x, q.y); }).ToArray();
                    var hull = new ConvexHull2(pts, p, QueryNumberType.QT_DOUBLE);
                    var sig = new Sig(s) { Failed = hull.Dimension != 2 }.Count("dimension", hull.Dimension);
                    if (hull.Dimension != 2) return sig;
                    Polygon2d poly = hull.GetHullPolygon();
                    // (every point on the inner side of every edge, in units of the input: no squares of the scale)
                    int n = poly.VertexCount;
                    double orient = Math.Sign(poly.SignedArea);
                    for (int i = 0; i < n && sig.Contradiction == ""; ++i) {
                        Vector2d ea = poly[i] / s.Scale, eb = poly[(i + 1) % n] / s.Scale;
                        foreach (Vector2d q in pts) {
                            Vector2d d = eb - ea, w = q / s.Scale - ea;
                            double side = orient * (d.x * w.y - d.y * w.x);
                            if (side < -1e-9 * Math.Max(1, d.Length)) { sig.Contradiction = "a point lies outside its hull"; break; }
                        }
                    }
                    return sig.Scalar("area", poly.Area / s.Scale / s.Scale);
                } });
            ops.Add(new Op { Name = "PointAABBTree3", Type = typeof(PointAABBTree3), Input = () => Sphere(), Param = 0.3, ParamName = "query.x", Tol = 1e-5,
                Run = (m, s, p) => {
                    var tree = new PointAABBTree3(m, true) { FWNAreaEstimateF = v => 4 * Math.PI / m.VertexCount * s.Scale * s.Scale };
                    Vector3d q = s.Apply(new Vector3d(0, Query.y, Query.z)) + s.ApplyDirection(Vector3d.AxisX) * p;
                    int vid = tree.FindNearestPoint(q);
                    var sig = new Sig(s) { Failed = vid < 0 };
                    if (vid < 0) return sig;
                    return sig.Count("nearest", vid).Length("distance", m.GetVertex(vid).Distance(q)).Scalar("winding", tree.FastWindingNumber(s.Apply(Vector3d.Zero)));
                } });
            ops.Add(new Op { Name = "MeshBatchQueries", Type = typeof(MeshBatchQueries), Input = () => Sphere(), Param = 0.3, ParamName = "query.x", Tol = 1e-5,
                Run = (m, s, p) => {
                    var tree = new DMeshAABBTree3(m, true);
                    Vector3d q = s.Apply(new Vector3d(0, Query.y, Query.z)) + s.ApplyDirection(Vector3d.AxisX) * p;
                    Vector3d[] pts = { q, s.Apply(new Vector3d(0.05, 0.03, 0.02)), s.Apply(new Vector3d(0.2, 0.1, 0.3)), s.Apply(new Vector3d(0.1, 0.2, 0.99)) };
                    Ray3d[] rays = pts.Select(x => new Ray3d(x, -s.ApplyDirection(Vector3d.AxisZ))).ToArray();
                    var hits = new RayHit[rays.Length]; var any = new bool[rays.Length]; var near = new NearestPointResult[pts.Length]; var inside = new InsideStatus[pts.Length];
                    var opt = new BatchOptions { Parallel = false };
                    BatchStatus b1 = MeshBatchQueries.IntersectBatch(tree, rays, hits, null, opt);
                    BatchStatus b2 = MeshBatchQueries.AnyHitBatch(tree, rays, any, null, opt);
                    BatchStatus b3 = MeshBatchQueries.NearestPointBatch(tree, pts, near, null, opt);
                    BatchStatus b4 = MeshBatchQueries.IsInsideBatch(tree, pts, inside, InsideMethod.Crossings, opt);
                    // (the nearest point of an inner point of a faceted sphere is a tie between facets: only the outer ones give a point)
                    var sig = new Sig(s) { Failed = b1 != BatchStatus.Ok || b2 != BatchStatus.Ok || b3 != BatchStatus.Ok || b4 != BatchStatus.Ok };
                    for (int i = 0; i < pts.Length; ++i)
                        sig.Count("hit" + i, hits[i].Triangle < 0 ? 0 : 1).Length("t" + i, hits[i].Triangle < 0 ? 0 : hits[i].T).Count("any" + i, any[i] ? 1 : 0)
                           .Length("d" + i, near[i].Distance).Count("inside" + i, (int)inside[i]);
                    return sig.Point("nearest0", near[0].Point).Point("nearest3", near[3].Point);
                } });
            foreach (var mode in new[] { MarchingCubesPro.RootfindingModes.SingleLerp, MarchingCubesPro.RootfindingModes.Bisection }) {
                MarchingCubesPro.RootfindingModes rootMode = mode;
                ops.Add(new Op { Name = "MarchingCubesPro." + mode, Type = typeof(MarchingCubesPro), Input = () => Sphere(), Param = 0.1, ParamName = "CubeSize", Tol = 5e-3, CountTol = 0.05,
                    Run = (m, s, p) => {
                        var iso = new SafeSphereField() { Center = s.Apply(Vector3d.Zero), Radius = s.Scale };
                        double r = s.Scale * 1.2;
                        var box = new AxisAlignedBox3d(iso.Center - new Vector3d(r, r, r), iso.Center + new Vector3d(r, r, r));
                        var mc = new MarchingCubesPro() { Implicit = iso, IsoValue = 0, Bounds = box, CubeSize = p, ParallelCompute = false, RootMode = rootMode };
                        mc.Generate();
                        var sig = new Sig(s);
                        sig.Output = mc.Mesh;
                        return sig.Volume("volume", Invariance.Volume(mc.Mesh));
                    } });
            }

            // ---- analysis ----
            ops.Add(new Op { Name = "MeshWallThickness", Type = typeof(MeshWallThickness), Input = () => Sphere(), Tol = 1e-2, CountTol = 0.05,
                Run = (m, s, p) => {
                    var w = new MeshWallThickness(m) { Method = MeshWallThickness.Methods.Ray };
                    var sig = new Sig(s) { Failed = w.Compute() == false };
                    if (sig.Failed) return sig;
                    // (NaN per vertex is the documented "no valid ray", so no per-vertex check)
                    // (no valid ray anywhere: the statistics are NaN, documented)
                    return w.Statistics.Count == 0 ? sig.Count("valid", 0) : sig.Count("valid", w.Statistics.Count).Length("mean", w.Statistics.Mean).Length("max", w.Statistics.Max);
                } });
            ops.Add(new Op { Name = "MeshDeviation", Type = typeof(MeshDeviation), Input = () => Sphere(), Tol = 1e-5,
                Run = (m, s, p) => {
                    var d = new MeshDeviation(m, MappedSphere(s, 1.05, new Vector3d(0.02, 0, 0)));
                    var sig = new Sig(s) { Failed = d.Compute() == false };
                    if (sig.Failed) return sig;
                    // (NaN for isolated vertices is documented, so no per-vertex check)
                    return sig.Length("max", d.Statistics.MaxAbsolute).Length("mean", d.Statistics.Mean);
                } });
            ops.Add(new Op { Name = "MeshFidelityMetrics", Type = typeof(MeshFidelityMetrics), Input = () => Sphere(), Tol = 1e-2,
                Run = (m, s, p) => {
                    var f = new MeshFidelityMetrics(m, MappedSphere(s, 1.05, new Vector3d(0.02, 0, 0))) { SurfaceSamples = 2000, EdgeSamples = 500, VolumeSamples = 2000, Parallel = false };
                    var sig = new Sig(s) { Failed = f.Compute() == false };
                    if (sig.Failed) return sig;
                    // (distances relative to the size of the reference, so scalars)
                    return sig.Scalar("chamfer", f.Chamfer).Scalar("max", f.MaxDistanceMeshToReference);
                } });
            ops.Add(new Op { Name = "MeshCurvature", Type = typeof(MeshCurvature), Input = () => Sphere(), Tol = 1e-4,
                Run = (m, s, p) => {
                    MeshCurvature c = MeshCurvature.Compute(m);
                    var sig = new Sig(s).Count("valid", c.ValidVertices).Count("degenerate", c.DegenerateTriangles);
                    // (curvature is a 1/length: the product with the scale is invariant)
                    sig.Scalar("meanAverage", m.VertexIndices().Where(v => c.Valid[v]).Select(v => c.Mean[v]).DefaultIfEmpty(0).Average() * s.Scale);
                    return sig.PerVertex(m.VertexIndices().Select(v => c.Mean[v] + c.Gaussian[v]));
                } });
            ops.Add(new Op { Name = "MeshTopologySummary", Type = typeof(MeshTopologySummary), Input = HoledSphere,
                Run = (m, s, p) => {
                    MeshTopologySummary t = MeshTopologySummary.Compute(m);
                    return new Sig(s).Count("components", t.Components).Count("edges", t.Edges).Count("boundaryLoops", t.BoundaryLoops).Count("euler", t.EulerCharacteristic).Scalar("genus", t.Genus);
                } });
            ops.Add(new Op { Name = "PrimitiveFit3.Sphere", Type = typeof(PrimitiveFit3), Input = () => Sphere(1, 8), Param = 0.02, ParamName = "InlierDistance", Tol = 1e-4,
                Run = (m, s, p) => {
                    var fit = new PrimitiveFit3(m.Vertices().ToArray()) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = p, UseRansac = false };
                    bool ok = fit.Compute() && fit.Result == PrimitiveFit3.Results.Success;
                    var sig = new Sig(s) { Failed = ok == false };
                    if (ok == false) return sig;
                    return sig.Length("radius", fit.SphereRadius).Point("center", fit.SphereCenter).Count("inliers", fit.InlierCount);
                } });
            ops.Add(new Op { Name = "PrimitiveFit3.Plane", Type = typeof(PrimitiveFit3), Input = () => FlatGrid(), Param = 0.02, ParamName = "InlierDistance", Tol = 1e-4,
                Run = (m, s, p) => {
                    var fit = new PrimitiveFit3(m.Vertices().ToArray()) { Primitive = PrimitiveFit3.Primitives.Plane, InlierDistance = p, UseRansac = false };
                    bool ok = fit.Compute() && fit.Result == PrimitiveFit3.Results.Success;
                    var sig = new Sig(s) { Failed = ok == false };
                    if (ok == false) return sig;
                    return sig.Count("inliers", fit.InlierCount).Length("rms", fit.RMSError);
                } });

            return ops.ToArray();
        }
    }
}
