using System;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// The reduced cases of the findings (docs/research/METAMORPHIC_BUG_HUNT_2026-11.md, section 4): each FAIL line here is one
    /// issue, each INFO line one observation that is not a defect. The stage is expected to fail until the issues are fixed.
    /// </summary>
    public static class FindingsStage
    {
        const string S = "findings";

        /// <summary> the box [-1, 1]^3 (12 triangles) with one of its edges split at the fraction t from a corner: the same solid, 14 triangles </summary>
        public static DMesh3 BoxWithSplitEdge(double t)
        {
            DMesh3 b = Shapes.Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            int eid = b.EdgeIndices().First(x => { Index2i ev = b.GetEdgeV(x); return Math.Abs((b.GetVertex(ev.a) - b.GetVertex(ev.b)).Length - 2) < 1e-12; });
            Index2i evs = b.GetEdgeV(eid);
            b.SplitEdge(eid, out DMesh3.EdgeSplitInfo info);
            b.SetVertex(info.vNew, Vector3d.Lerp(b.GetVertex(evs.a), b.GetVertex(evs.b), t));
            return b;
        }

        public static void Run()
        {
            // F1: MeshRobustRepair changes a clean one-part mesh (the needle collapse of MeshPostRepairCleanup moves a box corner);
            // next to a far clean part the same mesh passes through untouched
            foreach (double t in new[] { 1e-2, 1e-3, 1e-4, 1e-6 }) {
                DMesh3 box = BoxWithSplitEdge(t);
                string clean = MeshRobustRepair.NotCleanReasons(box);
                var alone = new MeshRobustRepair(new DMesh3(box));
                bool ok = alone.Apply();
                DMesh3 far = Shapes.Box(new Vector3d(10, 0, 0), new Vector3d(1, 1, 1));
                var withFar = new MeshRobustRepair(Shapes.Combine(box, far));
                bool okFar = withFar.Apply();
                double v = Checks.Volume(alone.Mesh), moved = ChainStage.OneSided(box, alone.Mesh);
                Program.Info(S, $"F1 box, edge split at {t}", $"input clean '{clean}', volume {Checks.Volume(box):R}; alone: {ok}, path {alone.ResultPath}, reason '{alone.ResultReason}', {alone.Mesh.VertexCount}/{alone.Mesh.TriangleCount} vertices/triangles, volume {v:R}, input up to {moved:R} from the result; with a far box: {okFar}, path {withFar.ResultPath}, volume {Checks.Volume(withFar.Mesh) - 8:R}");
                Program.Report(S, $"F1 box, edge split at {t}: the repair of a clean mesh keeps its solid", Program.Rel("volume", v, 8, 1e-12));
                Program.Report(S, $"F1 box, edge split at {t}: alone as next to a far part", Program.Rel("volume", v, Checks.Volume(withFar.Mesh) - 8, 1e-12));
            }

            // O1 (observation): DualContouring with Simplify on a singular level set (two spheres that touch) is not the same mesh
            // after a shift by whole cells; the vertices move by up to 0.15 cells, inside SimplifyTolerance
            Func<Vector3d, double> kiss = p => Math.Min((p - new Vector3d(0.5, 0, 0)).Length, (p + new Vector3d(0.5, 0, 0)).Length) - 0.5;
            foreach (bool simplify in new[] { true, false }) {
                DMesh3 Gen(Vector3d shift)
                {
                    var f = new ShiftedField { F = kiss, Shift = shift };
                    var dc = new DualContouring { Implicit = f, Bounds = f.Bounds(), CellSize = 0.125, InsideIsLess = true, Simplify = simplify };
                    dc.Generate();
                    return dc.Mesh;
                }
                DMesh3 a = Gen(Vector3d.Zero);
                foreach (Vector3d shift in new[] { new Vector3d(0.125, 0, 0), new Vector3d(8, 0, 0), new Vector3d(40, -80, 120) }) {
                    DMesh3 b = Gen(shift);
                    double worst = -1;
                    if (a.VertexCount == b.VertexCount) { worst = 0; foreach (int v in a.VertexIndices()) worst = Math.Max(worst, (a.GetVertex(v) + shift - b.GetVertex(v)).Length); }
                    Program.Info(S, $"O1 dual contouring, touching spheres, Simplify {simplify}, shift {shift}", $"volume {Checks.Volume(a):R} vs {Checks.Volume(b):R}, largest vertex difference {worst:E2} ({worst / 0.125:F3} cells)");
                }
            }
        }

        sealed class ShiftedField : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public Vector3d Shift;
            public double Value(ref Vector3d p) => F(p - Shift);
            public AxisAlignedBox3d Bounds() => new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5) + Shift, new Vector3d(1.5, 1.5, 1.5) + Shift);
        }
    }
}
