using System.Diagnostics;
using g3;
using gs;

namespace G3Demos.Demos
{
    /// <summary> contouring with analytic references: TpmsLattice, MeshOffset of a box, DualContouring / MarchingCubes of a box and a sphere </summary>
    public sealed class ContourParams
    {
        public string Mode = "tpms";            // tpms | offset | dc-box | dc-sphere | mc-sphere
        // TpmsLattice
        public TpmsType Type = TpmsType.Gyroid;
        public TpmsStyle Style = TpmsStyle.Sheet;
        public int Cells = 2;                   // periods per box side
        public int Divisions = 16;
        public double VolumeFraction = 0.3;
        public bool Enclosure = false;
        // MeshOffset
        public double Distance = 0.1;           // offset distance (negative: inset) of the box 1 x 0.7 x 0.5
        public double RelativeCellSize = 0.25;
        public double RotateDeg = 0;            // the box rotated about z (exact rotation by Shapes.Circle, about this angle)
        // contouring
        public double CellSize = 0.1;
        public double ShiftExp = 0;             // 0: grid aligned; else the field moved by 10^-k (features close to grid planes)
        public int ScaleExp = 0;
        public bool CheckScaleInvariance = false;
    }

    public static class ContourDemo
    {
        const double A = 0.5, B = 0.35, C = 0.25;   // half extents of the box

        sealed class BoxSdf : BoundedImplicitFunction3d
        {
            public Vector3d H, O; public double S;
            public AxisAlignedBox3d Bounds() { return new AxisAlignedBox3d(O, (H.MaxAbs + 0.3) * S); }
            public double Value(ref Vector3d p)
            {
                double dx = Math.Abs(p.x - O.x) - H.x * S, dy = Math.Abs(p.y - O.y) - H.y * S, dz = Math.Abs(p.z - O.z) - H.z * S;
                double outside = new Vector3d(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length;
                return outside + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
            }
        }

        sealed class Run
        {
            public DMesh3 Mesh;
            public bool Ok;
            public string Status = "", Error = "";
            public long Ms;
            public StatusGroup Group;
            public double Expected = double.NaN, Tolerance = 1e-9;
            public string Exception;
        }

        static Run Execute(ContourParams p, int scaleExp)
        {
            double s = Math.Pow(2, scaleExp);
            var r = new Run();
            var sw = Stopwatch.StartNew();
            try {
                switch (p.Mode) {
                    case "offset": {
                        DMesh3 box = Shapes.Box(Vector3d.Zero, new Vector3d(A, B, C), 2);
                        if (p.RotateDeg != 0) {
                            Vector2d c = Shapes.Circle(p.RotateDeg / 360);
                            Shapes.Transform(box, v => new Vector3d(v.x * c.x - v.y * c.y, v.x * c.y + v.y * c.x, v.z));
                        }
                        Shapes.Transform(box, v => v * s);
                        var op = new MeshOffset(box, p.Distance * s) { RelativeCellSize = p.RelativeCellSize };
                        r.Ok = op.Compute();
                        r.Mesh = op.Result; r.Status = op.ResultStatus.ToString();
                        double d = p.Distance, a = 2 * A, b = 2 * B, cc = 2 * C;
                        // the exact offset of a box: Minkowski sum with a ball (V + A d + pi/4 E d^2 + 4/3 pi d^3), or the inset box
                        r.Expected = (d >= 0
                            ? a * b * cc + 2 * (a * b + b * cc + a * cc) * d + Math.PI * (a + b + cc) * d * d + 4.0 / 3 * Math.PI * d * d * d
                            : Math.Max(0, a + 2 * d) * Math.Max(0, b + 2 * d) * Math.Max(0, cc + 2 * d)) * s * s * s;
                        r.Tolerance = d >= 0 ? 0.02 : 1e-9;   // the rounded offset is resolved at the cell size; the inset is exact
                        r.Group = new StatusGroup("MeshOffset")
                            .Add("Status", op.ResultStatus.ToString(), r.Ok ? RowState.Good : RowState.Bad)
                            .Add("UsedCellSize", MeshCheck.F(op.UsedCellSize))
                            .Add("BandCertified / BandDeviation", $"{op.BandCertified} / {MeshCheck.F(op.BandDeviation)}", op.BandCertified ? RowState.Good : RowState.Warn)
                            .Add("UncertifiedTriangles", op.UncertifiedTriangles.ToString(), op.UncertifiedTriangles > 0 ? RowState.Warn : RowState.Info)
                            .Check("SelfIntersectionFree", op.SelfIntersectionFree)
                            .Add("NonManifoldTriangles", op.NonManifoldTriangles.ToString(), op.NonManifoldTriangles > 0 ? RowState.Bad : RowState.Info);
                        break;
                    }
                    case "dc-box":
                    case "dc-sphere":
                    case "mc-sphere": {
                        double shift = p.ShiftExp > 0 ? Math.Pow(10, -p.ShiftExp) : 0;
                        Vector3d o = new Vector3d(shift, shift * 0.5, shift * 0.25) * s;
                        BoundedImplicitFunction3d f = p.Mode == "dc-box"
                            ? new BoxSdf { H = new Vector3d(A, B, C), O = o, S = s }
                            : new ImplicitSphere3d { Origin = o, Radius = 0.5 * s };
                        AxisAlignedBox3d bounds = f.Bounds();
                        bounds.Expand(2 * p.CellSize * s);
                        if (p.Mode == "mc-sphere") {
                            var mc = new MarchingCubes { Implicit = f, Bounds = bounds, CubeSize = p.CellSize * s, RootMode = MarchingCubes.RootfindingModes.Bisection };
                            mc.Generate();
                            r.Mesh = mc.Mesh; r.Ok = mc.Mesh != null && mc.Mesh.TriangleCount > 0; r.Status = r.Ok ? "generated" : "empty";
                            r.Group = new StatusGroup("MarchingCubes").Add("cells", mc.CellDimensions.ToString());
                        } else {
                            var dc = new DualContouring { Implicit = f, Bounds = bounds, CellSize = p.CellSize * s, InsideIsLess = true, ExactHermite = true };
                            dc.Generate();
                            r.Mesh = dc.Mesh; r.Ok = dc.Mesh != null && dc.Mesh.TriangleCount > 0 && dc.NonManifoldTriangles == 0; r.Status = r.Ok ? "generated" : "empty or non-manifold";
                            r.Group = new StatusGroup("DualContouring")
                                .Add("NonManifoldTriangles", dc.NonManifoldTriangles.ToString(), dc.NonManifoldTriangles > 0 ? RowState.Bad : RowState.Info)
                                .Add("TransitionErrors", dc.TransitionErrors.ToString(), dc.TransitionErrors > 0 ? RowState.Bad : RowState.Info);
                        }
                        r.Expected = (p.Mode == "dc-box" ? 8 * A * B * C : 4.0 / 3 * Math.PI * 0.125) * s * s * s;
                        r.Tolerance = p.Mode == "dc-box" ? 1e-9 : 0.02;   // sharp features exact for the box; the sphere is polyhedral
                        break;
                    }
                    default: {
                        double L = 1.0 / p.Cells * s;
                        var t = new TpmsLattice {
                            Type = p.Type, Style = p.Style, CellSize = 2 * L, Divisions = p.Divisions, VolumeFraction = p.VolumeFraction,
                            Box = new AxisAlignedBox3d(Vector3d.Zero, s), Enclosure = p.Enclosure
                        };
                        t.Compute();
                        r.Mesh = t.Mesh; r.Ok = t.Status == TpmsStatus.Ok; r.Status = t.Status.ToString(); r.Error = t.Message;
                        double boxVolume = 8 * s * s * s;
                        r.Expected = t.RealizedVolumeFraction * boxVolume;
                        r.Tolerance = 0.05;
                        r.Group = new StatusGroup("TpmsLattice")
                            .Add("Status / Message", t.Status + (string.IsNullOrEmpty(t.Message) ? "" : ": " + t.Message), r.Ok ? RowState.Good : RowState.Bad)
                            .Add("VolumeFraction requested / realized", $"{MeshCheck.F(p.VolumeFraction)} / {MeshCheck.F(t.RealizedVolumeFraction)}")
                            .Add("measured fraction (volume / box)", r.Mesh != null ? MeshCheck.F(Shapes.Volume(r.Mesh) / boxVolume) : "-")
                            .Add("MeanWallThickness", MeshCheck.F(t.MeanWallThickness))
                            .Add("NotCleanReason / SelfIntersectionsFound", $"\"{t.NotCleanReason}\" / {t.SelfIntersectionsFound}", t.NotCleanReason == "" ? RowState.Good : RowState.Bad)
                            .Add("cells evaluated / grid shifts", $"{t.CellsEvaluated} / {t.GridShiftsUsed}");
                        break;
                    }
                }
            } catch (Exception e) {
                r.Exception = e.GetType().Name + ": " + e.Message;
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }

        public static DemoResult Compute(ContourParams p)
        {
            var res = new DemoResult { FitKey = "contour:" + p.Mode };
            res.InputHash = p.Mode + "/" + p.Type + "/" + p.Style;
            Run run = Execute(p, p.ScaleExp);
            double frame = Math.Pow(2, p.ScaleExp);
            StatusGroup g = run.Group ?? new StatusGroup(p.Mode);
            g.Check("succeeded", run.Ok, "yes", "NO");
            if (!string.IsNullOrEmpty(run.Error)) g.Add("Error", run.Error, RowState.Bad);
            if (run.Exception != null) g.Add("exception", run.Exception, RowState.Bad);
            g.Add("time", $"{run.Ms} ms");
            res.Groups.Add(g);

            MeshCheck rc = null;
            if (run.Mesh != null && run.Mesh.TriangleCount > 0) {
                rc = MeshCheck.Of(run.Mesh);
                res.Groups.Add(rc.ToGroup("Result checks", run.Expected, run.Tolerance));
            }
            if (p.CheckScaleInvariance && p.ScaleExp != 0 && run.Mesh != null) {
                Run r0 = Execute(p, 0);
                bool same = r0.Mesh != null && r0.Mesh.VertexCount == run.Mesh.VertexCount && r0.Mesh.TriangleCount == run.Mesh.TriangleCount;
                int diff = 0;
                if (same) {
                    var a = r0.Mesh.VertexIndices().Select(r0.Mesh.GetVertex).ToArray();
                    var b = run.Mesh.VertexIndices().Select(run.Mesh.GetVertex).ToArray();
                    for (int i = 0; i < a.Length; ++i) if (a[i] * frame != b[i]) diff++;
                }
                var si = new StatusGroup("2^k invariance (vs. the unscaled run)");
                si.Add("unscaled", $"{r0.Status}, {r0.Mesh?.TriangleCount ?? 0} tris");
                si.Check("bit-identical after scaling", same && diff == 0, "yes", same ? $"NO ({diff} vertices differ)" : "NO (different counts)");
                res.Groups.Add(si);
            }
            res.OutputHash = run.Mesh != null ? Shapes.Hash(run.Mesh) : "none";
            if (run.Mesh != null)
                res.Meshes.Add(RenderMesh.Of(run.Mesh, "#0ea5e9", 1, frame, edges: true));

            double rel = rc != null && !double.IsNaN(run.Expected) ? Math.Abs(rc.Volume - run.Expected) / Math.Abs(run.Expected) : double.NaN;
            bool bad = !run.Ok || run.Exception != null || rc == null || !rc.Clean;
            if (bad)
                res.Headline = $"Problem: {run.Status} {run.Error} {run.Exception} {(rc != null && !rc.Clean ? "not clean: " + rc.NotCleanReasons : "")}".Trim();
            else
                res.Headline = $"{run.Status}: {rc.Triangles} triangles, volume {(double.IsNaN(rel) ? "" : $"within {rel:P3} of the reference")}, {run.Ms} ms";
            res.HeadlineState = bad ? RowState.Bad : rel > run.Tolerance ? RowState.Warn : RowState.Good;
            return res;
        }
    }
}
