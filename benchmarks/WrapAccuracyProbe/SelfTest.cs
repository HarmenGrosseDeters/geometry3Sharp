// Self-test of the helpers the numbers depend on (the validity check of the snap back, the metrics). Exit code 0 = all pass.
//   1. the ring check: a harmless move passes; a flip, a collapse onto a neighbour and a move through the opposite layer are rejected
//      for the right reason; each check switched off lets its case through (mutation check of the test itself)
//   2. the independent verdict agrees: Safe on a battery of wraps is clean by MeshRobustRepair.NotCleanReason and 0 self-intersections
//      (MeshRepairMetrics, not the incremental check), while the same run with the checks off is not clean on at least one input
//   3. metrics: the volume of a box, the Hausdorff interval of a shifted copy contains the shift, FarShare of a mesh against itself is 0
//      and against a shifted copy is 1
using System;
using System.Collections.Generic;
using g3;
using gs;

static class SelfTest
{
    static int fails = 0;
    static void Expect(bool ok, string what) { Console.WriteLine((ok ? "pass  " : "FAIL  ") + what); if (!ok) fails++; }

    public static int Run()
    {
        // 1. ring check on a closed slab 6 x 6 x 1 (unit cells), interior vertex on the top at (3,3,1)
        DMesh3 slab = Procedural.PolyCube(6, 6, 1, (x, y, z) => true);
        int vid = -1;
        foreach (int v in slab.VertexIndices()) if (slab.GetVertex(v).Distance(new Vector3d(3, 3, 1)) == 0) vid = v;
        int nb = -1;
        foreach (int v in slab.VertexIndices()) if (slab.GetVertex(v).Distance(new Vector3d(4, 3, 1)) == 0) nb = v;
        double diag = 9;
        var on = new SnapBack.Options();
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(3.1, 3.05, 1), diag, on) == 0, "harmless in-plane move accepted");
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(3, 3, 1.3), diag, on) == 0, "harmless move up accepted");
        int flip = SnapBack.TestMove(slab, vid, new Vector3d(7.5, 3, 1), diag, on);
        Expect(flip == 1 || flip == 3, "move far in plane (folds the fan) rejected, reason " + flip);
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(4, 3, 1), diag, on) == 2, "collapse onto a neighbour rejected as degenerate");
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(3, 3, -0.5), diag, on) == 3, "move through the bottom layer rejected as self-intersection");
        var noSelf = new SnapBack.Options { CheckSelf = false };
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(3, 3, -0.5), diag, noSelf) == 0, "mutant without the self check lets the move through the layer pass");
        var noDeg = new SnapBack.Options { CheckDegenerate = false, CheckSelf = false };
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(4, 3, 1), diag, noDeg) != 2, "mutant without the degenerate check lets the collapse pass");
        var noFlip = new SnapBack.Options { CheckFlip = false, CheckSelf = false };
        Expect(SnapBack.TestMove(slab, vid, new Vector3d(7.5, 3, 1), diag, noFlip) == 0, "mutant without flip and self checks lets the fold pass");
        // a flip without any intersection: a vertex of a flat fan moved across the ring's other side is a fold; a pure flip is the vertex moved
        // through its own ring plane from the top of a pyramid: the apex of a square pyramid pushed below the base flips every ring triangle
        var pyr = new DMesh3();
        int a0 = pyr.AppendVertex(new Vector3d(0, 0, 0)), a1 = pyr.AppendVertex(new Vector3d(1, 0, 0)), a2 = pyr.AppendVertex(new Vector3d(1, 1, 0)), a3 = pyr.AppendVertex(new Vector3d(0, 1, 0));
        int apex = pyr.AppendVertex(new Vector3d(0.5, 0.5, 1));
        pyr.AppendTriangle(a0, a1, apex); pyr.AppendTriangle(a1, a2, apex); pyr.AppendTriangle(a2, a3, apex); pyr.AppendTriangle(a3, a0, apex);
        pyr.AppendTriangle(a0, a2, a1); pyr.AppendTriangle(a0, a3, a2);
        Expect(SnapBack.TestMove(pyr, apex, new Vector3d(0.5, 0.5, -1), 2, on) == 3 || SnapBack.TestMove(pyr, apex, new Vector3d(0.5, 0.5, -1), 2, on) == 1, "apex through the base rejected");
        Expect(SnapBack.TestMove(pyr, apex, new Vector3d(0.5, 0.5, 0.5), 2, on) == 0, "apex lowered but above the base accepted");

        // 2. battery: independent verdict
        bool mutantCaught = false;
        foreach (string name in new[] { "box", "slot0.20", "plate1e-3", "sphere", "cylinder" }) {
            DMesh3 input = Inputs.Solid(name);
            double d = Inputs.Diag(input);
            var w = new MeshAlphaWrap(input) { Alpha = d / 20, Offset = d * 1e-3 };
            w.Compute();
            var tree = new DMeshAABBTree3(input, true);
            var st = new BackStats();
            DMesh3 safe = SnapBack.Safe(w.Result, input, tree, d, on, st);
            string v = MeshRobustRepair.NotCleanReason(safe);
            int si = MeshRepairMetrics.SelfIntersections(safe);
            Expect(v == "" && si == 0, name + ": safe result clean (" + (v == "" ? "clean" : v) + "), independent self-intersections " + si);
            var off = new SnapBack.Options { CheckFlip = false, CheckDegenerate = false, CheckSelf = false };
            DMesh3 bad = SnapBack.Safe(w.Result, input, tree, d, off, new BackStats());
            string vb = MeshRobustRepair.NotCleanReason(bad);
            Console.WriteLine("      (all checks off on " + name + ": " + (vb == "" ? "clean" : vb) + ")");
            if (vb != "") mutantCaught = true;
            // one check off at a time (information: which check the battery needs)
            foreach (var m in new[] { ("flip", new SnapBack.Options { CheckFlip = false }), ("degenerate", new SnapBack.Options { CheckDegenerate = false }), ("self", new SnapBack.Options { CheckSelf = false }) }) {
                DMesh3 r1 = SnapBack.Safe(w.Result, input, tree, d, m.Item2, new BackStats());
                string v1 = MeshRobustRepair.NotCleanReason(r1);
                Console.WriteLine("      (" + m.Item1 + " check off on " + name + ": " + (v1 == "" ? "clean" : v1) + ")");
            }
        }
        Expect(mutantCaught, "with the checks off at least one input of the battery is not clean (the battery can see the checks)");

        // 3. metrics
        DMesh3 box = Procedural.Prism(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(0, 6) }, 4);
        Expect(Math.Abs(Metrics.SignedVolume(box, new Vector3d(100, -50, 7)) - 240) < 1e-9, "signed volume of the 10 x 6 x 4 box is 240 from any origin");
        DMesh3 moved = new DMesh3(box, true);
        Inputs.Translate(moved, new Vector3d(0, 0, 0.25));
        var tb = new DMeshAABBTree3(box, true); var tm = new DMeshAABBTree3(moved, true);
        // box shifted along z: the farthest point of the shifted box from the box is a corner of the top at distance 0.25
        Metrics.Hd h = Metrics.OneSided(moved, box, tb, 1e-3, 20);
        Expect(h.Converged && h.Lower <= 0.25 + 1e-9 && h.Upper >= 0.25 - 1e-9, "Hausdorff interval [" + h.Lower.ToString("G6") + ", " + h.Upper.ToString("G6") + "] contains the shift 0.25");
        Expect(Metrics.FarShare(box, tb, 1e-6, 5000, out double mx0) == 0, "FarShare of a mesh against itself is 0");
        Inputs.Translate(moved, new Vector3d(0, 0, 4.75));
        tm = new DMeshAABBTree3(moved, true);
        Expect(Metrics.FarShare(box, tm, 0.5, 5000, out double mx1) > 0.99, "FarShare against a copy shifted by 5 is 1 (max dist " + mx1.ToString("G4") + ")");
        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
