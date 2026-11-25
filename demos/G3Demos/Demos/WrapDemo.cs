using System.Diagnostics;
using g3;
using gs;

namespace G3Demos.Demos
{
    /// <summary> MeshAlphaWrap (and PassageWrap for the bored block) on procedural inputs that stress its documented limits </summary>
    public sealed class WrapParams
    {
        public string Shape = "sphere-holes";   // sphere-holes | sheet | points | thin-plate | two-boxes | bored-block
        public double RelativeAlpha = 20;       // alpha = diagonal / RelativeAlpha
        public double RelativeOffset = 600;     // offset = diagonal / RelativeOffset
        public int Budget = 0;                  // MaxEstimatedTriangles (0 = off)
        public double MaxCoarsening = 0;        // MaxAlphaCoarsening (0 = no limit)
        public bool PullPinchPoints = false;
        public int Holes = 6;                   // sphere-holes: triangles removed
        public int Resolution = 8;              // sphere / sheet / plate resolution
        public int Points = 2000;               // points: number of samples
        public int NoiseExp = 0;                // points: 0 = none, else noise 10^-k of the size
        public int ThinExp = 3;                 // thin-plate: thickness 10^-k; two-boxes: gap 10^-k
        public double BoreRadius = 0.3;         // bored-block: radius of the bore (block is 2 x 2 x 2)
        public double PinRadius = 0.2;          // bored-block: radius of the passage pin (PassageWrap)
        public int ScaleExp = 0;
        public bool CheckScaleInvariance = false;
    }

    public static class WrapDemo
    {
        /// <summary> the input: a mesh (possibly with no triangles) or points, and a reference volume of the solid it stands for (NaN: none) </summary>
        public static void MakeInput(WrapParams p, int scaleExp, out DMesh3 mesh, out List<Vector3d> points, out double refVolume, out string what)
        {
            points = null; refVolume = double.NaN;
            switch (p.Shape) {
                case "sheet": {
                    // an open curved sheet: the wrap has to go around both sides (volume about area * 2 * offset)
                    int n = Math.Max(2, p.Resolution * 2);
                    mesh = new DMesh3();
                    for (int i = 0; i <= n; ++i)
                        for (int j = 0; j <= n; ++j) {
                            double x = -0.5 + (double)i / n, y = -0.5 + (double)j / n;
                            mesh.AppendVertex(new Vector3d(x, y, 0.3 * (x * x - y * y) + 0.6 * x * y * (x + y)));   // polynomial: same bits everywhere
                        }
                    for (int i = 0; i < n; ++i)
                        for (int j = 0; j < n; ++j) {
                            int a = i * (n + 1) + j, b = a + n + 1;
                            mesh.AppendTriangle(a, b, b + 1);
                            mesh.AppendTriangle(a, b + 1, a + 1);
                        }
                    what = $"open wavy sheet, {mesh.TriangleCount} triangles";
                    break;
                }
                case "points": {
                    // points on a torus (major 0.6, minor 0.25), deterministic, optional normal noise
                    var rnd = new Random(7);
                    points = new List<Vector3d>();
                    double noise = p.NoiseExp > 0 ? Math.Pow(10, -p.NoiseExp) : 0;
                    for (int i = 0; i < p.Points; ++i) {
                        Vector2d cu = Shapes.Circle(rnd.NextDouble()), cv = Shapes.Circle(rnd.NextDouble());
                        double r = 0.25 + noise * (2 * rnd.NextDouble() - 1);
                        double rr = 0.6 + r * cv.x;
                        points.Add(new Vector3d(rr * cu.x, r * cv.y, rr * cu.y));
                    }
                    mesh = null;
                    refVolume = 2 * Math.PI * Math.PI * 0.6 * 0.25 * 0.25;
                    what = $"{points.Count} points on a torus" + (noise > 0 ? $", noise {noise:G2}" : "");
                    break;
                }
                case "thin-plate": {
                    double t = Math.Pow(10, -p.ThinExp);
                    mesh = Shapes.Box(Vector3d.Zero, new Vector3d(0.5, 0.5, t / 2), Math.Max(2, p.Resolution));
                    // no volume reference: thinner than the offset, the wrap is a shell of about area x 2 offset by design
                    what = $"plate 1 x 1 x {t:G3}";
                    break;
                }
                case "two-boxes": {
                    // two cubes with a gap of 10^-k between them: does the wrap bridge it?
                    double g = Math.Pow(10, -p.ThinExp);
                    mesh = Shapes.Box(new Vector3d(-0.25 - g / 2, 0, 0), new Vector3d(0.25, 0.25, 0.25), 2);
                    new MeshEditor(mesh).AppendMesh(Shapes.Box(new Vector3d(0.25 + g / 2, 0, 0), new Vector3d(0.25, 0.25, 0.25), 2));
                    refVolume = 2 * 0.125;
                    what = $"two cubes, gap {g:G3}";
                    break;
                }
                case "bored-block": {
                    // block [-1,1]^3 minus a 32-gon prism of radius BoreRadius along z (exact boolean)
                    DMesh3 block = Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1), 2);
                    DMesh3 bore = Prism(p.BoreRadius, 32, -1.5, 1.5);
                    var diff = new ExactMeshBoolean(block, bore, ExactMeshBoolean.Operations.Difference);
                    if (!diff.Compute() || diff.Result == null)
                        throw new InvalidOperationException($"the bored block could not be built: ExactMeshBoolean {diff.Status}: {diff.Error}");
                    mesh = diff.Result;
                    refVolume = Shapes.Volume(mesh);
                    what = $"block with a bore of radius {p.BoreRadius:G3} ({mesh.TriangleCount} triangles)";
                    break;
                }
                default: {
                    // a closed sphere with single triangles dropped: each hole is about one triangle wide
                    mesh = Shapes.Sphere(Vector3d.Zero, 0.5, p.Resolution);
                    refVolume = Shapes.Volume(mesh);
                    var rnd = new Random(3);
                    var tris = mesh.TriangleIndices().ToList();
                    var drop = new HashSet<int>();
                    while (drop.Count < Math.Min(p.Holes, tris.Count / 4))
                        drop.Add(tris[rnd.Next(tris.Count)]);
                    foreach (int tid in drop)
                        mesh.RemoveTriangle(tid, false, false);
                    double edge = 0;
                    foreach (int eid in mesh.EdgeIndices()) { Index2i ev = mesh.GetEdgeV(eid); edge += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); }
                    edge /= Math.Max(1, mesh.EdgeCount);
                    what = $"sphere, {mesh.TriangleCount} triangles, {drop.Count} single-triangle holes (mean edge {edge:G3})";
                    break;
                }
            }
            if (scaleExp != 0) {
                double s = Math.Pow(2, scaleExp);
                if (mesh != null) Shapes.Transform(mesh, v => v * s);
                if (points != null) points = points.Select(v => v * s).ToList();
                refVolume *= s * s * s;
            }
        }

        static DMesh3 Prism(double r, int n, double z0, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < n; ++i) {
                Vector2d c = Shapes.Circle((double)i / n);
                m.AppendVertex(new Vector3d(r * c.x, r * c.y, z0));
                m.AppendVertex(new Vector3d(r * c.x, r * c.y, z1));
            }
            int c0 = m.AppendVertex(new Vector3d(0, 0, z0)), c1 = m.AppendVertex(new Vector3d(0, 0, z1));
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                int a0 = 2 * i, a1 = 2 * i + 1, b0 = 2 * j, b1 = 2 * j + 1;
                m.AppendTriangle(a0, b0, b1); m.AppendTriangle(a0, b1, a1);
                m.AppendTriangle(c0, b0, a0); m.AppendTriangle(c1, a1, b1);
            }
            if (Shapes.Volume(m) < 0) m.ReverseOrientation();
            return m;
        }

        sealed class Run
        {
            public DMesh3 Result;
            public bool Ok;
            public string Status, Error;
            public long Ms;
            public MeshAlphaWrap Wrap;
            public PassageWrap Passage;
            public string Exception;
        }

        static Run RunWrap(WrapParams p, DMesh3 mesh, List<Vector3d> points)
        {
            var r = new Run();
            var sw = Stopwatch.StartNew();
            try {
                if (p.Shape == "bored-block") {
                    double s = mesh.GetBounds().DiagonalLength / Math.Sqrt(12);   // the block's half size (1 unscaled)
                    var pin = new PassagePin(new[] { new Vector3d(0, 0, -1.5 * s), new Vector3d(0, 0, 1.5 * s) }, p.PinRadius * s);
                    var pw = new PassageWrap(mesh) { Pins = { pin }, MaxTriangles = p.Budget, PullPinchPoints = p.PullPinchPoints };
                    r.Ok = pw.Compute();
                    r.Result = pw.Result; r.Status = pw.Status.ToString(); r.Error = pw.Error; r.Passage = pw;
                } else {
                    var w = points != null ? new MeshAlphaWrap(points) : new MeshAlphaWrap(mesh);
                    w.RelativeAlpha = p.RelativeAlpha; w.RelativeOffset = p.RelativeOffset;
                    w.MaxEstimatedTriangles = p.Budget; w.MaxAlphaCoarsening = p.MaxCoarsening; w.PullPinchPoints = p.PullPinchPoints;
                    r.Ok = w.Compute();
                    r.Result = w.Result; r.Status = w.Status.ToString(); r.Error = w.Error; r.Wrap = w;
                }
            } catch (Exception e) {
                r.Exception = e.GetType().Name + ": " + e.Message;
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary>
        /// what the wrap promises, checked on the result: every input point inside (winding number &gt; 1/2), the distance of the wrap's
        /// vertices to the input (about the offset at least), and nested shells (a component inside another: the wrap went around both
        /// sides of a sheet or into the input through a hole)
        /// </summary>
        public static void Enclosure(DMesh3 result, DMesh3 mesh, List<Vector3d> points, out int outside, out int tested,
            out double minDist, out double maxDist, out int shells, out int nested)
        {
            var tree = new DMeshAABBTree3(result, true);
            var inputPts = points ?? mesh.VertexIndices().Select(mesh.GetVertex).ToList();
            outside = 0; tested = 0;
            int step = Math.Max(1, inputPts.Count / 3000);
            for (int i = 0; i < inputPts.Count; i += step) {
                tested++;
                if (!(tree.WindingNumber(inputPts[i]) > 0.5)) outside++;
            }
            minDist = double.MaxValue; maxDist = 0;
            var wv = result.VertexIndices().ToList();
            int wstep = Math.Max(1, wv.Count / 3000);
            DMeshAABBTree3 inTree = mesh != null && mesh.TriangleCount > 0 ? new DMeshAABBTree3(mesh, true) : null;
            var cloud = points;
            for (int i = 0; i < wv.Count; i += wstep) {
                Vector3d v = result.GetVertex(wv[i]);
                double d;
                if (inTree != null) {
                    int t = inTree.FindNearestTriangle(v);
                    d = Math.Sqrt(MeshQueries.TriangleDistance(mesh, t, v).DistanceSquared);
                } else {
                    double best = double.MaxValue;
                    foreach (Vector3d q in cloud) best = Math.Min(best, v.DistanceSquared(q));
                    d = Math.Sqrt(best);
                }
                minDist = Math.Min(minDist, d); maxDist = Math.Max(maxDist, d);
            }
            var mcc = new MeshConnectedComponents(result);
            mcc.FindConnectedT();
            shells = mcc.Count; nested = 0;
            if (shells > 1 && shells <= MaxShellsForNesting) {
                // one tree per component, built once; one vertex of each component tested against the other components
                var trees = mcc.Components.Select(c => new DMeshAABBTree3(MeshSubset(result, c.Indices), true)).ToList();
                for (int i = 0; i < shells; ++i) {
                    Vector3d v = result.GetVertex(result.GetTriangle(mcc.Components[i].Indices[0]).a);
                    for (int j = 0; j < shells; ++j)
                        if (j != i && trees[j].WindingNumber(v) > 0.5) { nested++; break; }
                }
            } else if (shells > MaxShellsForNesting)
                nested = -1;   // not checked
        }

        /// <summary> above this many shells the nesting is not checked (a sparse cloud with a small alpha gives one shell per point) </summary>
        const int MaxShellsForNesting = 50;

        static DMesh3 MeshSubset(DMesh3 m, int[] tris)
        {
            var sub = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int t in tris) {
                Index3i tv = m.GetTriangle(t);
                int[] nv = new int[3];
                for (int j = 0; j < 3; ++j)
                    if (!map.TryGetValue(tv[j], out nv[j])) map[tv[j]] = nv[j] = sub.AppendVertex(m.GetVertex(tv[j]));
                sub.AppendTriangle(nv[0], nv[1], nv[2]);
            }
            return sub;
        }

        /// <summary>
        /// volume / (area x offset) below this: the wrap is a thin shell (a double-sided wrap of a surface has about 1, as its two
        /// sides are each about one offset from it; a wrap of a solid of size D has about D / (6 offset), hundreds at the defaults)
        /// </summary>
        const double ThinShellRatio = 4;

        public static DemoResult Compute(WrapParams p)
        {
            var res = new DemoResult { FitKey = "wrap:" + p.Shape };
            DMesh3 mesh; List<Vector3d> points; double refVolume; string what;
            try {
                MakeInput(p, p.ScaleExp, out mesh, out points, out refVolume, out what);
            } catch (Exception ex) {
                res.Groups.Add(new StatusGroup("Input").Add("input", ex.Message, RowState.Bad));
                res.Headline = "Input not built: " + ex.Message;
                res.HeadlineState = RowState.Bad;
                return res;
            }
            double frame = Math.Pow(2, p.ScaleExp);
            res.InputHash = mesh != null ? Shapes.Hash(mesh) : Shapes.Hash(PointsMesh(points));
            var ig = new StatusGroup("Input").Add("input", what);
            if (mesh != null) {
                MeshCheck ic = MeshCheck.Of(mesh, false);
                ig.Add("closed / boundary edges", ic.Closed ? "closed" : $"open, {ic.BoundaryEdges} boundary edges");
            }
            res.Groups.Add(ig);

            Run run = RunWrap(p, mesh, points);
            var g = new StatusGroup(run.Passage != null ? "PassageWrap" : "MeshAlphaWrap");
            g.Check("Compute() returned", run.Ok, "true", "false");
            g.Add("Status", run.Status ?? "-", run.Ok ? RowState.Good : RowState.Bad);
            if (!string.IsNullOrEmpty(run.Error)) g.Add("Error", run.Error, RowState.Bad);
            if (run.Exception != null) g.Add("exception", run.Exception, RowState.Bad);
            g.Add("time", $"{run.Ms} ms");
            double offset = double.NaN, alpha = double.NaN;
            if (run.Wrap != null) {
                MeshAlphaWrap w = run.Wrap;
                alpha = w.UsedAlpha; offset = w.UsedOffset;
                g.Add("UsedAlpha / UsedOffset", $"{MeshCheck.F(w.UsedAlpha)} / {MeshCheck.F(w.UsedOffset)}");
                g.Add("AlphaCoarsening", MeshCheck.F(w.AlphaCoarsening), w.AlphaCoarsening > 1 ? RowState.Warn : RowState.Info);
                int tri = w.Result?.TriangleCount ?? 0;
                g.Add("EstimatedTriangles / result", $"{w.EstimatedTriangles:F0} / {tri}" + (w.EstimatedTriangles > 0 ? $" ({tri / w.EstimatedTriangles:F2}x)" : ""));
                g.Add("phases (ms)", string.Join(", ", w.PhaseMilliseconds.OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key + " " + kv.Value)));
            }
            if (run.Passage != null) {
                PassageWrap pw = run.Passage;
                alpha = pw.Alpha; offset = pw.Offset;
                g.Add("Alpha / BoundAlpha / Offset", $"{MeshCheck.F(pw.Alpha)} / {MeshCheck.F(pw.BoundAlpha)} / {MeshCheck.F(pw.Offset)}");
                g.Add("WrapStatus", pw.WrapStatus.ToString());
                g.Add("FallbackSteps / BoundPremisesHeld", $"{pw.FallbackSteps} / {pw.BoundPremisesHeld}", pw.FallbackSteps > 0 ? RowState.Warn : RowState.Info);
                g.Add("EstimatedTriangles", $"{pw.EstimatedTriangles:F0}");
                foreach (PassagePinResult pr in pw.PinResults)
                    g.Add("pin", $"passed {pr.Passed}, clearance [{MeshCheck.F(pr.ClearanceLow)}, {MeshCheck.F(pr.ClearanceHigh)}], input clearance {MeshCheck.F(pr.InputClearance)}, axis outside {pr.AxisOutside}",
                        pr.Passed ? RowState.Good : RowState.Bad);
            }
            res.Groups.Add(g);

            MeshCheck rc = null;
            int outside = 0, nested = 0, shells = 0, tested = 0;
            double shellRatio = double.NaN;
            double minD = double.NaN, maxD = double.NaN;
            if (run.Result != null && run.Result.TriangleCount > 0) {
                rc = MeshCheck.Of(run.Result);
                res.Groups.Add(rc.ToGroup("Result checks", refVolume, 0.05));
                Enclosure(run.Result, mesh, points, out outside, out tested, out minD, out maxD, out shells, out nested);
                var e = new StatusGroup("Wrap promises (checked on the result)");
                e.Check("input points inside (winding > 1/2)", outside == 0, $"all {tested} tested (a subsample of at most 3000)", $"{outside} of {tested} tested outside");
                if (!double.IsNaN(offset) && offset > 0) {
                    e.Add("min distance to input / offset", MeshCheck.F(minD / offset), minD > 0 ? RowState.Good : RowState.Bad);
                    e.Add("max distance to input / offset", MeshCheck.F(maxD / offset));
                }
                e.Add("shells / nested shells", $"{shells} / {(nested < 0 ? $"not checked (more than {MaxShellsForNesting} shells)" : nested.ToString())}", nested > 0 ? RowState.Warn : RowState.Info);
                if (rc.Area > 0 && offset > 0) {
                    shellRatio = rc.Volume / (rc.Area * offset);
                    e.Add("volume / (area x offset)", MeshCheck.F(shellRatio) + (shellRatio < ThinShellRatio ? " (a thin shell: the wrap lies on both sides of the surface)" : ""),
                        shellRatio < ThinShellRatio ? RowState.Warn : RowState.Info);
                }
                res.Groups.Add(e);
            }

            if (p.CheckScaleInvariance && p.ScaleExp != 0 && run.Result != null) {
                MakeInput(p, 0, out DMesh3 m0, out List<Vector3d> p0, out _, out _);
                Run r0 = RunWrap(p, m0, p0);
                bool same = r0.Result != null && r0.Result.VertexCount == run.Result.VertexCount && r0.Result.TriangleCount == run.Result.TriangleCount;
                int diff = 0;
                if (same) {
                    var a = r0.Result.VertexIndices().Select(r0.Result.GetVertex).ToArray();
                    var b = run.Result.VertexIndices().Select(run.Result.GetVertex).ToArray();
                    for (int i = 0; i < a.Length; ++i) if (a[i] * frame != b[i]) diff++;
                }
                var si = new StatusGroup("2^k invariance (vs. the unscaled run)");
                si.Add("unscaled", $"{r0.Status}, {r0.Result?.TriangleCount ?? 0} tris");
                si.Check("bit-identical after scaling", same && diff == 0, "yes", same ? $"NO ({diff} vertices differ)" : "NO (different counts)");
                res.Groups.Add(si);
            }

            res.OutputHash = run.Result != null ? Shapes.Hash(run.Result) : "none";
            if (run.Result != null)
                res.Meshes.Add(RenderMesh.Of(run.Result, "#8b5cf6", 0.45, frame, edges: true));
            if (mesh != null && mesh.TriangleCount > 0) {
                res.Meshes.Add(RenderMesh.Of(mesh, "#f59e0b", 1, frame));
                res.Meshes.Add(RenderMesh.BoundaryEdges(mesh, frame));
            }
            if (points != null)
                res.Meshes.Add(PointMarks(points, frame));

            bool bad = !run.Ok || run.Exception != null || rc == null || !rc.Clean || outside > 0;
            if (bad)
                res.Headline = $"Problem: {run.Status} {run.Error} {run.Exception} {(rc != null && !rc.Clean ? "not clean: " + rc.NotCleanReasons : "")} {(outside > 0 ? outside + " input points outside" : "")}".Trim();
            else if (shellRatio < ThinShellRatio)
                res.Headline = $"{run.Status}, but the wrap is a thin shell around both sides of the surface (volume / (area x offset) = {shellRatio:F2})"
                    + (refVolume > 0 ? " where the input is a solid: hollow result" : ": expected for a sheet, a thin part or sparse points") + $", {run.Ms} ms";
            else
                res.Headline = $"{run.Status}: {rc.Triangles} triangles, {shells} shell(s){(nested > 0 ? $", {nested} nested" : "")}, {run.Ms} ms";
            bool hollowSolid = shellRatio < ThinShellRatio && refVolume > 0;
            res.HeadlineState = bad || hollowSolid ? RowState.Bad : nested > 0 || shellRatio < ThinShellRatio ? RowState.Warn : RowState.Good;
            return res;
        }

        static DMesh3 PointsMesh(List<Vector3d> pts)
        {
            var m = new DMesh3();
            foreach (var v in pts) m.AppendVertex(v);
            return m;
        }

        /// <summary> the points as short crosses (line segments), so they show without a point shader </summary>
        static RenderMesh PointMarks(List<Vector3d> pts, double frame)
        {
            var pos = new List<float>();
            double s = 0.004 * MeshMeasurements.BoundsV(PointsMesh(pts), Enumerable.Range(0, pts.Count)).DiagonalLength;
            foreach (Vector3d q in pts)
                foreach (Vector3d d in new[] { Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ }) {
                    Vector3d a = (q - d * s) / frame, b = (q + d * s) / frame;
                    pos.Add((float)a.x); pos.Add((float)a.y); pos.Add((float)a.z);
                    pos.Add((float)b.x); pos.Add((float)b.y); pos.Add((float)b.z);
                }
            return new RenderMesh { positions = pos.ToArray(), indices = new int[0], color = "#b45309", lines = true };
        }
    }
}
