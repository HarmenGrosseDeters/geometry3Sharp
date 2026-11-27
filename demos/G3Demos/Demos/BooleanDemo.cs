using System.Diagnostics;
using g3;
using gs;

namespace G3Demos.Demos
{
    /// <summary>
    /// Exact Boolean of two procedural operands, through SolidBooleanPipeline (soup to solid, the documented customer
    /// entry) or ExactMeshBoolean directly. Operand A is the box [-0.5, 0.5]^3; operand B is a box or a sphere whose -x side
    /// is placed at x = 0.5 + gap (gap &lt; 0 overlaps), then rotated, tilted (near-coplanar top faces) and both are scaled by 2^k.
    /// </summary>
    public sealed class BooleanParams
    {
        public ExactMeshBoolean.Operations Operation = ExactMeshBoolean.Operations.Union;
        public string Engine = "pipeline";   // pipeline (SolidBooleanPipeline) | exact (ExactMeshBoolean)
        public bool UsePipeline => Engine != "exact";
        public string ShapeB = "box";      // box | sphere
        public int Resolution = 6;         // edge vertices of the grid box / sphere
        public double Overlap = 0.3;       // coarse: how far B reaches into A along x (0 = touching)
        public int GapSign = 0;            // -1 overlap, 0 exact contact, +1 gap: added to Overlap as sign * 10^-GapExp
        public int GapExp = 6;
        public double RotZDeg = 0;         // rotation of B about z through its contact point
        public int TiltExp = 0;            // 0 = no tilt; else tilt B about x by 10^-TiltExp rad (near-coplanar top faces)
        public int ScaleExp = 0;           // both operands scaled by 2^ScaleExp
        public double TargetFraction = 0;  // pipeline reduction (0 = none)
        public bool CheckInclusionExclusion = true;
        public bool CheckScaleInvariance = true;
        public bool ShowInputs = true;
    }

    public static class BooleanDemo
    {
        public static void MakeOperands(BooleanParams p, int scaleExp, out DMesh3 a, out DMesh3 b, out double gap)
        {
            gap = -p.Overlap + p.GapSign * Math.Pow(10, -p.GapExp);
            a = Shapes.Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5), p.Resolution);
            // B: its -x extreme at x = 0.5 + gap; the box has the same top face z = 0.5 as A (coplanar before the tilt)
            if (p.ShapeB == "sphere")
                b = Shapes.Sphere(new Vector3d(0.5 + gap + 0.4, 0.15, 0.1), 0.4, p.Resolution + 2);
            else
                b = Shapes.Box(new Vector3d(0.5 + gap + 0.35, 0.1, 0.2), new Vector3d(0.35, 0.3, 0.3), p.Resolution);
            Vector3d pivot = new Vector3d(0.5 + gap, 0.1, 0.5);
            Quaterniond rz = new Quaterniond(Vector3d.AxisZ, p.RotZDeg);
            double tilt = p.TiltExp > 0 ? Math.Pow(10, -p.TiltExp) * 180 / Math.PI : 0;
            Quaterniond rx = new Quaterniond(Vector3d.AxisX, tilt);
            if (p.RotZDeg != 0 || tilt != 0)
                Shapes.Transform(b, v => pivot + rz * (rx * (v - pivot)));
            if (scaleExp != 0) {
                double s = Math.Pow(2, scaleExp);
                Shapes.Transform(a, v => v * s);
                Shapes.Transform(b, v => v * s);
            }
        }

        sealed class Run
        {
            public DMesh3 Result;
            public bool Ok;
            public string Status, Error;
            public long Ms;
            public SolidBooleanPipeline Pipeline;
            public ExactMeshBoolean Boolean;
        }

        static Run RunOp(BooleanParams p, DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, bool usePipeline)
        {
            var sw = Stopwatch.StartNew();
            var r = new Run();
            if (usePipeline) {
                var pipe = new SolidBooleanPipeline(a, b, op);
                if (p.TargetFraction > 0)
                    pipe.TargetFraction = p.TargetFraction;
                r.Ok = pipe.Compute();
                r.Result = pipe.Result;
                r.Status = pipe.Status.ToString() + (pipe.BooleanStatus != ExactMeshBoolean.BooleanStatus.Success ? " / boolean " + pipe.BooleanStatus : "");
                r.Error = pipe.Error;
                r.Pipeline = pipe;
            } else {
                var eb = new ExactMeshBoolean(a, b, op);
                r.Ok = eb.Compute();
                r.Result = eb.Result;
                r.Status = eb.Status.ToString();
                r.Error = eb.Error;
                r.Boolean = eb;
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary> exact volume of the boolean for axis-aligned boxes (no rotation, no tilt), else NaN </summary>
        static double ExpectedBoxVolume(BooleanParams p, DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
        {
            if (p.ShapeB != "box" || p.RotZDeg != 0 || p.TiltExp != 0)
                return double.NaN;
            AxisAlignedBox3d ba = a.GetBounds(), bb = b.GetBounds();
            AxisAlignedBox3d i = ba.Intersect(bb);
            double vi = (i.Max.x > i.Min.x && i.Max.y > i.Min.y && i.Max.z > i.Min.z) ? i.Volume : 0;
            double va = ba.Volume, vb = bb.Volume;
            switch (op) {
                case ExactMeshBoolean.Operations.Union: return va + vb - vi;
                case ExactMeshBoolean.Operations.Intersection: return vi;
                default: return va - vi;
            }
        }

        public static DemoResult Compute(BooleanParams p)
        {
            var res = new DemoResult { FitKey = "boolean:" + p.ShapeB };
            MakeOperands(p, p.ScaleExp, out DMesh3 a, out DMesh3 b, out double gap);
            double frame = Math.Pow(2, p.ScaleExp);
            res.InputHash = Shapes.Hash(a, b);

            var inputs = new StatusGroup("Inputs")
                .Add("operands", $"A box {a.TriangleCount} tris, B {p.ShapeB} {b.TriangleCount} tris")
                .Add("gap along x (unscaled)", MeshCheck.F(gap) + (gap == 0 ? " (exact contact)" : gap > 0 ? " (apart)" : " (overlap)"))
                .Add("scale", $"2^{p.ScaleExp}");
            res.Groups.Add(inputs);

            Run main = RunOp(p, a, b, p.Operation, p.UsePipeline);
            var op = new StatusGroup((p.UsePipeline ? "SolidBooleanPipeline" : "ExactMeshBoolean") + " " + p.Operation);
            op.Check("Compute() returned", main.Ok, "true", "false");
            op.Add("Status", main.Status, main.Ok ? RowState.Good : RowState.Bad);
            if (!string.IsNullOrEmpty(main.Error))
                op.Add("Error", main.Error, RowState.Bad);
            op.Add("time", $"{main.Ms} ms");
            if (main.Pipeline != null) {
                SolidBooleanReport rep = main.Pipeline.Report;
                op.Add("Report.Clean / NotCleanReason", rep.Clean ? "true" : "false: " + rep.NotCleanReason, rep.Clean ? RowState.Good : RowState.Bad);
                op.Add("boolean / result triangles", $"{rep.BooleanTriangles} / {rep.ResultTriangles}");
                op.Add("contacts (boolean / result)", $"{rep.ContactsBoolean} / {rep.ContactsResult}");
                op.Add("StopReason", rep.StopReason + (string.IsNullOrEmpty(rep.StopDetail) ? "" : ": " + rep.StopDetail));
                op.Add("rounding volume before / after", $"{MeshCheck.F(rep.RoundingVolumeBefore)} / {MeshCheck.F(rep.RoundingVolumeAfter)}");
                if (rep.RoundingRemoved.Count > 0)
                    op.Add("rounding removed components", rep.RoundingRemoved.Count.ToString(), RowState.Warn);
                if (rep.RemovedThinSolids.Count > 0)
                    op.Add("removed thin solids", rep.RemovedThinSolids.Count.ToString(), RowState.Warn);
                if (rep.Route.Count > 0)
                    op.Add("route", string.Join(" > ", rep.Route.Select(s => s.Name)));
                op.Add("stage ms (check/bool/reduce/final)", $"{rep.InputCheckMilliseconds}/{rep.BooleanMilliseconds}/{rep.ReductionMilliseconds}/{rep.FinalCheckMilliseconds}");
            }
            if (main.Boolean != null) {
                ExactMeshBoolean eb = main.Boolean;
                op.Add("RoundingClean", eb.RoundingClean.ToString(), eb.RoundingClean ? RowState.Good : RowState.Warn);
                op.Add("intersecting pairs / contacts", $"{eb.IntersectingPairs} / {eb.Contacts}");
                if (eb.Rounding != null && eb.Rounding.RemovedComponents > 0)
                    op.Add("rounding removed components", eb.Rounding.RemovedComponents.ToString(), RowState.Warn);
            }
            res.Groups.Add(op);

            double expected = main.Result != null ? ExpectedBoxVolume(p, a, b, p.Operation) : double.NaN;
            MeshCheck rc = null;
            if (main.Result != null) {
                rc = MeshCheck.Of(main.Result);
                res.Groups.Add(rc.ToGroup("Result checks (MeshRobustRepair.NotCleanReasons)", expected, 1e-12));
            }

            if (p.CheckInclusionExclusion && main.Result != null) {
                // V(A u B) + V(A n B) = V(A) + V(B) and V(A - B) = V(A) - V(A n B), with the same engine
                var ie = new StatusGroup("Inclusion-exclusion (same engine, all three operations)");
                double va = Shapes.Volume(a), vb = Shapes.Volume(b);
                var vols = new Dictionary<ExactMeshBoolean.Operations, double>();
                bool allOk = true;
                foreach (ExactMeshBoolean.Operations o in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
                    Run r = o == p.Operation ? main : RunOp(p, a, b, o, p.UsePipeline);
                    allOk &= r.Ok;
                    vols[o] = r.Result != null ? Shapes.Volume(r.Result) : double.NaN;
                    ie.Add($"V({o})", (r.Ok ? "" : r.Status + ": ") + MeshCheck.F(vols[o]) + $" ({r.Ms} ms)", r.Ok ? RowState.Info : RowState.Bad);
                }
                double scaleV = Math.Pow(frame, 3);
                double e1 = (vols[ExactMeshBoolean.Operations.Union] + vols[ExactMeshBoolean.Operations.Intersection] - va - vb) / scaleV;
                double e2 = (vols[ExactMeshBoolean.Operations.Difference] - va + vols[ExactMeshBoolean.Operations.Intersection]) / scaleV;
                ie.Add("V(AuB)+V(AnB)-V(A)-V(B)", MeshCheck.F(e1), Math.Abs(e1) < 1e-9 ? RowState.Good : RowState.Bad);
                ie.Add("V(A-B)-V(A)+V(AnB)", MeshCheck.F(e2), Math.Abs(e2) < 1e-9 ? RowState.Good : RowState.Bad);
                res.Groups.Add(ie);
            }

            if (p.CheckScaleInvariance && p.ScaleExp != 0 && main.Result != null) {
                // the 2^k scaled run must equal the unscaled one bit for bit (vertices times 2^k)
                MakeOperands(p, 0, out DMesh3 a0, out DMesh3 b0, out _);
                Run r0 = RunOp(p, a0, b0, p.Operation, p.UsePipeline);
                var si = new StatusGroup("2^k invariance (vs. the unscaled run)");
                bool same = r0.Result != null && r0.Result.TriangleCount == main.Result.TriangleCount && r0.Result.VertexCount == main.Result.VertexCount;
                int differing = 0;
                if (same) {
                    var v0 = r0.Result.VertexIndices().Select(r0.Result.GetVertex).ToArray();
                    var v1 = main.Result.VertexIndices().Select(main.Result.GetVertex).ToArray();
                    for (int i = 0; i < v0.Length; ++i)
                        if (v0[i] * frame != v1[i])
                            differing++;
                }
                si.Add("unscaled", $"{r0.Status}, {r0.Result?.TriangleCount ?? 0} tris");
                si.Check("bit-identical after scaling", same && differing == 0, "yes", same ? $"NO ({differing} vertices differ)" : "NO (different counts)");
                res.Groups.Add(si);
            }

            res.OutputHash = main.Result != null ? Shapes.Hash(main.Result) : "none";
            if (main.Result != null)
                res.Meshes.Add(RenderMesh.Of(main.Result, "#3b82f6", 1, frame, edges: true));
            if (p.ShowInputs || main.Result == null) {
                res.Meshes.Add(RenderMesh.Of(a, "#94a3b8", 0.18, frame));
                res.Meshes.Add(RenderMesh.Of(b, "#f59e0b", 0.18, frame));
            }

            if (!main.Ok) {
                res.Headline = $"Refused / failed: {main.Status}. {main.Error}";
                res.HeadlineState = RowState.Bad;
            } else if (rc != null && !rc.Clean) {
                res.Headline = $"Success reported, but the result is not clean: {rc.NotCleanReasons}";
                res.HeadlineState = RowState.Bad;
            } else {
                res.Headline = $"Success, clean result, {main.Result.TriangleCount} triangles in {main.Ms} ms";
                res.HeadlineState = RowState.Good;
            }
            return res;
        }
    }
}
