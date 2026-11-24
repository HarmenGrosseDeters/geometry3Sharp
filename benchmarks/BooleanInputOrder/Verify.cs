// The "verify" part of `search --verify` (note docs/research/ROUNDING_DEGENERATE_LEFT_2026-11.md, section 9): is a result of the
// pipeline's default Grid retry the right solid? The volumes of the exact result, the non-strict result, the exact result of the snapped
// inputs and the retry's result are compared with an estimate that does not use the boolean: the winding numbers of the INPUTS along
// vertical lines (columns), integrated over jittered samples of the base square, the expectation of the operation checked per column.
// A measurement harness: no library change; the internal Snapped and DataGrid of the pipeline are called by reflection.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;

    // signed volume about the point c, the terms sorted so that the sum does not depend on the order of the triangles
    static double VolumeAbout(DMesh3 m, Vector3d c, IEnumerable<int> triangles = null)
    {
        if (m == null)
            return double.NaN;
        var terms = new List<double>();
        foreach (int t in triangles ?? m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            Vector3d p = m.GetVertex(f.a) - c, q = m.GetVertex(f.b) - c, r = m.GetVertex(f.c) - c;
            terms.Add(p.Dot(q.Cross(r)));
        }
        terms.Sort();
        double v = 0;
        foreach (double x in terms) v += x;
        return v / 6;
    }

    static double AreaOf(DMesh3 m)
    {
        double a = 0;
        foreach (int t in m.TriangleIndices()) a += m.GetTriArea(t);
        return a;
    }

    // a mesh as triangles in coordinates centred at c, with a bucket grid over the base square for vertical rays
    sealed class ColMesh
    {
        readonly double[] p;
        readonly int n;
        const int G = 40;
        readonly List<int>[] cells = new List<int>[G * G];
        readonly double x0, y0, dx, dy;

        public ColMesh(DMesh3 m, Vector3d c, double xlo, double ylo, double xhi, double yhi)
        {
            n = m.TriangleCount;
            p = new double[9 * n];
            x0 = xlo; y0 = ylo; dx = (xhi - xlo) / G; dy = (yhi - ylo) / G;
            int i = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    Vector3d v = m.GetVertex(f[k]) - c;
                    p[9 * i + 3 * k] = v.x; p[9 * i + 3 * k + 1] = v.y; p[9 * i + 3 * k + 2] = v.z;
                }
                double ax = Math.Min(p[9 * i], Math.Min(p[9 * i + 3], p[9 * i + 6])), bx = Math.Max(p[9 * i], Math.Max(p[9 * i + 3], p[9 * i + 6]));
                double ay = Math.Min(p[9 * i + 1], Math.Min(p[9 * i + 4], p[9 * i + 7])), by = Math.Max(p[9 * i + 1], Math.Max(p[9 * i + 4], p[9 * i + 7]));
                int i0 = Clamp((int)Math.Floor((ax - x0) / dx)), i1 = Clamp((int)Math.Floor((bx - x0) / dx));
                int j0 = Clamp((int)Math.Floor((ay - y0) / dy)), j1 = Clamp((int)Math.Floor((by - y0) / dy));
                for (int a = i0; a <= i1; ++a)
                    for (int b = j0; b <= j1; ++b)
                        (cells[a * G + b] ??= new List<int>()).Add(i);
                ++i;
            }
        }

        static int Clamp(int i) => i < 0 ? 0 : i >= G ? G - 1 : i;

        // the crossings of the vertical line at (x, y): z and the change of the winding number going up (+1 entering, -1 leaving)
        public void Cross(double x, double y, List<(double z, int s)> list, int tag, List<(double z, int s, int tag)> all)
        {
            var cell = cells[Clamp((int)Math.Floor((x - x0) / dx)) * G + Clamp((int)Math.Floor((y - y0) / dy))];
            if (cell == null)
                return;
            foreach (int i in cell) {
                int o = 9 * i;
                double ux = p[o], uy = p[o + 1], vx = p[o + 3], vy = p[o + 4], wx = p[o + 6], wy = p[o + 7];
                double w0 = (vx - x) * (wy - y) - (wx - x) * (vy - y);
                double w1 = (wx - x) * (uy - y) - (ux - x) * (wy - y);
                double w2 = (ux - x) * (vy - y) - (vx - x) * (uy - y);
                double d = w0 + w1 + w2;
                if (d == 0)
                    continue;
                bool inside = d > 0 ? (w0 > 0 && w1 > 0 && w2 > 0) : (w0 < 0 && w1 < 0 && w2 < 0);
                if (!inside)
                    continue;
                double z = (w0 * p[o + 2] + w1 * p[o + 5] + w2 * p[o + 8]) / d;
                int s = d > 0 ? -1 : 1;
                if (list != null) list.Add((z, s));
                if (all != null) all.Add((z, s, tag));
            }
        }
    }

    // the intervals where pred(windings) holds, from the sorted crossings of nMesh meshes (tags 0 .. nMesh - 1)
    static void Walk(List<(double z, int s, int tag)> ev, int nMesh, Func<int[], bool> pred, List<(double lo, double hi)> iv)
    {
        iv.Clear();
        ev.Sort((a, b) => a.z.CompareTo(b.z));
        var w = new int[nMesh];
        bool on = false;
        double start = 0;
        foreach (var e in ev) {
            w[e.tag] += e.s;
            bool now = pred(w);
            if (now && !on) { on = true; start = e.z; }
            else if (!now && on) { on = false; if (e.z > start) iv.Add((start, e.z)); }
        }
    }

    static double Length(List<(double lo, double hi)> iv) { double s = 0; foreach (var x in iv) s += x.hi - x.lo; return s; }

    static double Overlap(List<(double lo, double hi)> a, List<(double lo, double hi)> b)
    {
        double s = 0;
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count) {
            double lo = Math.Max(a[i].lo, b[j].lo), hi = Math.Min(a[i].hi, b[j].hi);
            if (hi > lo) s += hi - lo;
            if (a[i].hi < b[j].hi) i++; else j++;
        }
        return s;
    }

    sealed class ColumnResult
    {
        public double Expected, ExpectedSe;                        // the volume of the operation of the inputs, by columns
        public double[] Positive, Negative, SymDiff, Signed;        // per result mesh: volume where its winding is > 0, < 0, the symmetric difference with the expectation, the integral of the winding
        public double[] SymDiffSe;
        public int WindingChecked, WindingMismatch;                 // columns against DMeshAABBTree3.WindingNumber
    }

    // the expectation of the operation per vertical line against the results; grid x grid jittered columns, reps independent replicas
    static ColumnResult Columns(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, Vector3d c, DMesh3[] results, int grid, int reps, int seed)
    {
        AxisAlignedBox3d box = a.GetBounds();
        box.Contain(b.GetBounds());
        foreach (DMesh3 r in results)
            if (r != null && r.TriangleCount > 0)
                box.Contain(r.GetBounds());
        double xlo = box.Min.x - c.x - 1e-9, xhi = box.Max.x - c.x + 1e-9, ylo = box.Min.y - c.y - 1e-9, yhi = box.Max.y - c.y + 1e-9;
        var ca = new ColMesh(a, c, xlo, ylo, xhi, yhi);
        var cb = new ColMesh(b, c, xlo, ylo, xhi, yhi);
        var cr = results.Select(r => r != null && r.TriangleCount > 0 ? new ColMesh(r, c, xlo, ylo, xhi, yhi) : null).ToArray();
        int nr = results.Length;
        var rnd = new Random(seed);
        double cellArea = (xhi - xlo) * (yhi - ylo) / ((double)grid * grid);
        var exp = new double[reps];
        var pos = new double[nr, reps]; var neg = new double[nr, reps]; var sym = new double[nr, reps]; var sig = new double[nr, reps];
        Func<int[], bool> pe = op == ExactMeshBoolean.Operations.Difference ? (w => w[0] > 0 && w[1] <= 0)
            : op == ExactMeshBoolean.Operations.Intersection ? (Func<int[], bool>)(w => w[0] > 0 && w[1] > 0) : (w => w[0] > 0 || w[1] > 0);
        var evE = new List<(double z, int s, int tag)>();
        var evR = new List<(double z, int s, int tag)>();
        var ivE = new List<(double, double)>(); var ivP = new List<(double, double)>(); var ivN = new List<(double, double)>();
        for (int rep = 0; rep < reps; ++rep)
            for (int i = 0; i < grid; ++i)
                for (int j = 0; j < grid; ++j) {
                    double x = xlo + (i + rnd.NextDouble()) * (xhi - xlo) / grid, y = ylo + (j + rnd.NextDouble()) * (yhi - ylo) / grid;
                    evE.Clear();
                    ca.Cross(x, y, null, 0, evE);
                    cb.Cross(x, y, null, 1, evE);
                    Walk(evE, 2, pe, ivE);
                    double le = Length(ivE);
                    exp[rep] += le * cellArea;
                    for (int k = 0; k < nr; ++k) {
                        if (cr[k] == null) continue;
                        evR.Clear();
                        cr[k].Cross(x, y, null, 0, evR);
                        if (evR.Count == 0 && le == 0) continue;
                        Walk(evR, 1, w => w[0] > 0, ivP);
                        double lp = Length(ivP);
                        pos[k, rep] += lp * cellArea;
                        Walk(evR, 1, w => w[0] < 0, ivN);
                        neg[k, rep] += Length(ivN) * cellArea;
                        sym[k, rep] += (le + lp - 2 * Overlap(ivE, ivP)) * cellArea;
                        // the integral of the winding number along the column
                        double wsum = 0; int wc = 0; double zprev = 0;
                        foreach (var e in evR) { if (wc != 0) wsum += wc * (e.z - zprev); wc += e.s; zprev = e.z; }
                        sig[k, rep] += wsum * cellArea;
                    }
                }
        Func<double[], (double, double)> stat = v => { double m = v.Average(); double s = Math.Sqrt(v.Select(t => (t - m) * (t - m)).Sum() / Math.Max(1, v.Length - 1) / v.Length); return (m, s); };
        var res = new ColumnResult { Positive = new double[nr], Negative = new double[nr], SymDiff = new double[nr], Signed = new double[nr], SymDiffSe = new double[nr] };
        (res.Expected, res.ExpectedSe) = stat(exp);
        for (int k = 0; k < nr; ++k) {
            Func<double[,], double[]> row = m => Enumerable.Range(0, reps).Select(r => m[k, r]).ToArray();
            res.Positive[k] = stat(row(pos)).Item1; res.Negative[k] = stat(row(neg)).Item1; res.Signed[k] = stat(row(sig)).Item1;
            (res.SymDiff[k], res.SymDiffSe[k]) = stat(row(sym));
        }
        // the columns against the winding numbers of the trees (what DMeshAABBTree3 gives), at random points of the box of the inputs
        if (a.TriangleCount == 0 || b.TriangleCount == 0) return res;
        var ta = new DMeshAABBTree3(a, true); var tb = new DMeshAABBTree3(b, true);
        int check = 0, bad = 0;
        var la = new List<(double z, int s)>(); var lb = new List<(double z, int s)>();
        for (int s = 0; s < 1500; ++s) {
            double x = xlo + rnd.NextDouble() * (xhi - xlo), y = ylo + rnd.NextDouble() * (yhi - ylo);
            la.Clear(); lb.Clear();
            ca.Cross(x, y, la, 0, null); cb.Cross(x, y, lb, 1, null);
            if (la.Count == 0 && lb.Count == 0) continue;
            double zlo = Math.Min(la.Concat(lb).Min(e => e.z), 0), zhi = Math.Max(la.Concat(lb).Max(e => e.z), 0);
            zlo = la.Concat(lb).Min(e => e.z); zhi = la.Concat(lb).Max(e => e.z);
            double z = zlo + rnd.NextDouble() * (zhi - zlo);
            int wa = la.Where(e => e.z < z).Sum(e => e.s), wb = lb.Where(e => e.z < z).Sum(e => e.s);
            var q = new Vector3d(x, y, z) + c;
            check++;
            if (Math.Abs(ta.WindingNumber(q) - wa) > 0.5 || Math.Abs(tb.WindingNumber(q) - wb) > 0.5) bad++;
        }
        res.WindingChecked = check; res.WindingMismatch = bad;
        return res;
    }

    static string Components(DMesh3 m, Vector3d c)
    {
        if (m == null) return "none";
        var cc = new MeshConnectedComponents(m);
        cc.FindConnectedT();
        var parts = new List<string>();
        foreach (var comp in cc.Components) {
            AxisAlignedBox3d bb = AxisAlignedBox3d.Empty;
            foreach (int t in comp.Indices) { Index3i f = m.GetTriangle(t); bb.Contain(m.GetVertex(f.a)); bb.Contain(m.GetVertex(f.b)); bb.Contain(m.GetVertex(f.c)); }
            parts.Add(string.Format(CultureInfo.InvariantCulture, "[{0} tris, vol {1:G4}, box {2:G4} x {3:G4} x {4:G4}]", comp.Indices.Length, VolumeAbout(m, c, comp.Indices), bb.Width, bb.Height, bb.Depth));
        }
        return cc.Count + " comp " + string.Join(" ", parts);
    }

    // one case: the five volumes, the independent estimate, the retry's result and the rules
    static void VerifyCase(int index, string name, string group, DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
    {
        var inv = CultureInfo.InvariantCulture;
        Func<double, string> f = x => x.ToString("G6", inv);
        AxisAlignedBox3d bb = a.GetBounds();
        bb.Contain(b.GetBounds());
        Vector3d c = bb.Center;
        var pn = new SolidBooleanPipeline(a, b, op) { AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.None };
        pn.Compute();
        var pd = new SolidBooleanPipeline(a, b, op);
        pd.Compute();
        SolidBooleanRouteStep grid = pd.Report.Route.FirstOrDefault(x => x.Name == "Grid");
        bool retry = grid != null && pd.Status == SolidBooleanPipeline.PipelineStatus.Success;
        bool plain = grid == null && pd.Status == SolidBooleanPipeline.PipelineStatus.Success;
        if (group == "control" ? !plain : (group == "removed" ? !retry : false))
            return;
        var ex = new ExactMeshBoolean(a, b, op) { SafeRounding = false };
        ex.Compute();
        var ns = new ExactMeshBoolean(a, b, op);
        ns.Compute();
        double vExact = VolumeAbout(ex.Result, c), vNon = VolumeAbout(ns.Result, c);
        double vFirstReport = pn.Report.RoundingVolumeBefore;      // (about its own centre; of the failed first run)
        double vIn0 = VolumeAbout(a, c), vIn1 = VolumeAbout(b, c);
        DMesh3 snapExact = null, replica = null;
        MeshRoundingRepair rr = null;
        double cell = 0;
        if (grid != null) {
            cell = grid.Grid;
            int moved = 0;
            var snapped = new List<DMesh3>();
            foreach (DMesh3 m in new[] { a, b }) {
                var args = new object[] { m, cell, 0 };
                snapped.Add((DMesh3)typeof(SolidBooleanPipeline).GetMethod("Snapped", Internal).Invoke(null, args));
            }
            var se = new ExactMeshBoolean(snapped, op) { SafeRounding = false };
            se.Compute();
            snapExact = se.Result;
            var rx = new ExactMeshBoolean(snapped, op) { Orientation = pd.Orientation, CountInsideOutCells = true, AllowOpenMeshes = false, StrictRounding = true };
            rx.Compute();
            rr = rx.Rounding;
            replica = rx.Status == ExactMeshBoolean.BooleanStatus.Success ? rx.Result : rr?.Mesh;
        }
        DMesh3 retryResult = retry ? pd.Result : null;
        DMesh3 mainResult = retry || plain ? pd.Result : null;
        var results = new[] { mainResult, ex.Result, ns.Result, snapExact };
        ColumnResult col = Columns(a, b, op, c, results, 250, 6, 12345 + index);
        double vMc = col.Expected;
        double vRes = mainResult != null ? VolumeAbout(mainResult, c) : double.NaN;
        double vSnapReport = pd.Report.RoundingVolumeBefore, vAfterReport = pd.Report.RoundingVolumeAfter;
        double vSnapExact = VolumeAbout(snapExact, c);
        double dV = Math.Abs(vAfterReport - vSnapReport);
        double smallestClosed = new[] { vIn0, vIn1 }.Where(v => v != 0).Select(Math.Abs).DefaultIfEmpty(0).Min();
        bool ruleA = dV > 1e-3 * Math.Abs(vSnapReport) && dV > 1e-6 * smallestClosed;
        double area = mainResult != null ? AreaOf(mainResult) : 0;
        double diag = bb.Diagonal.Length;
        double vFirst = ex.Result != null ? vExact : vFirstReport;
        double drift = rr != null ? rr.MaxDrift : 0;
        double dFirst = Math.Abs(vRes - vFirst);
        double scaleCell = area * cell, scaleDrift = area * (cell + drift), scaleBound = area * (cell + 1e-5 * diag);
        double err = Math.Abs(vRes - vMc), tol = Math.Max(5 * col.ExpectedSe, 1e-3 * Math.Abs(vMc));
        bool wrong = mainResult != null && err > tol;
        string cls = group == "control" ? "control" : retry ? (wrong ? "WRONG" : "right") : "failed";
        Console.WriteLine(string.Format(inv,
            "VER #{0} {1} | {2} | V_in0 {3} | V_exact {4} (report first run {5}) | V_nonstrict {6} | V_snap_exact {7} (report {8}) | V_retry {9} | V_mc {10} +- {11} | err retry {12:G3} nonstrict {13:G3} exact {14:G3} snapexact {15:G3} | share of in0 {16:G3} | dV(a) {17:G3} ruleA {18} | |Vretry-Vfirst| {19:G3} ratios cell {20:G3} drift {21:G3} bound {22:G3} | cell {23:G3} drift {24:G3} area {25:G3} | wn-check {26}/{27}",
            index, group, cls, f(vIn0), f(vExact), f(vFirstReport), f(vNon), f(vSnapExact), f(vSnapReport), f(vRes), f(vMc), f(col.ExpectedSe),
            err / Math.Abs(vMc), Math.Abs(vNon - vMc) / Math.Abs(vMc), Math.Abs(vExact - vMc) / Math.Abs(vMc), Math.Abs(vSnapExact - vMc) / Math.Abs(vMc),
            Math.Abs(vRes) / Math.Abs(vIn0), dV, ruleA, dFirst, dFirst / scaleCell, dFirst / scaleDrift, dFirst / scaleBound, cell, drift, area, col.WindingMismatch, col.WindingChecked));
        // the volume as section 8 of the note measured it (the triple products about the ORIGIN, Volume()): far from the origin the products of
        // coordinates of 1e4 lose the digits of a volume of 1e-3, so this is the measure that differs, not the solid
        if (mainResult != null)
            Console.WriteLine(string.Format(inv, "  VERORIGIN #{0} about the origin: V_retry {1} V_nonstrict {2} (relative difference {3:G3}); about the centre: V_retry {4} V_nonstrict {5}; V_mc {6}",
                index, f(Volume(mainResult)), f(Volume(ns.Result)), Math.Abs(Volume(mainResult) - Volume(ns.Result)) / Math.Abs(Volume(ns.Result)), f(vRes), f(vNon), f(vMc)));
        Console.WriteLine("VERNAME " + index + " " + name);
        if (cls == "WRONG" || Environment.GetEnvironmentVariable("G3_VERIFY_DETAIL") != null) {
            Console.WriteLine(string.Format(inv, "  VERDETAIL #{0} retry result: {1} | in0 box {2:G4} x {3:G4} x {4:G4}, in1 {5:G4} x {6:G4} x {7:G4}", index, Components(mainResult, c), a.GetBounds().Width, a.GetBounds().Height, a.GetBounds().Depth, b.GetBounds().Width, b.GetBounds().Height, b.GetBounds().Depth));
            Console.WriteLine(string.Format(inv, "  VERDETAIL #{0} NotCleanReason '{1}' Clean {2} OnlyContacts {3} | grid change: {4}", index, mainResult != null ? MeshRobustRepair.NotCleanReason(mainResult) : "", pd.Report.Clean, pd.Report.OnlyContacts, grid?.Change));
            Console.WriteLine(string.Format(inv, "  VERDETAIL #{0} pipeline RoundingRemoved {1}: {2} | RoundingVolume before {3} after {4}", index, pd.Report.RoundingRemoved.Count,
                string.Join("; ", pd.Report.RoundingRemoved.Select(r => r.Reason + " tris " + r.Triangles + " vol " + r.Volume.ToString("G4", inv) + " thick " + r.Thickness.ToString("G3", inv))), f(vSnapReport), f(vAfterReport)));
            if (rr != null)
                Console.WriteLine(string.Format(inv, "  VERDETAIL #{0} replica of the retry's rounding: collapses {1} flips {2} moves {3} zips {4} MaxDrift {5:G3} removed {6} resolvable {7} volume {8} -> {9}; replica result volume {10}; pipeline snapped-run exact {11}",
                    index, rr.Collapses, rr.Flips, rr.Moves, rr.Zips, rr.MaxDrift, rr.RemovedComponents, rr.RemovedResolvable, f(rr.VolumeBefore), f(rr.VolumeAfter), f(VolumeAbout(replica, c)), f(vSnapExact)));
            Console.WriteLine(string.Format(inv, "  VERDETAIL #{0} columns: result winding>0 volume {1:G6}, winding<0 volume {2:G4}, signed {3:G6}; symmetric difference with the expectation {4:G4} +- {5:G2} ({6:G3} of V_mc); first run (None) status {7}/{8}",
                index, col.Positive[0], col.Negative[0], col.Signed[0], col.SymDiff[0], col.SymDiffSe[0], col.SymDiff[0] / Math.Abs(vMc), pn.Status, pn.BooleanStatus));
        }
        // the non-strict and the exact result against the estimate (scope), also where there is no retry
        Console.WriteLine(string.Format(inv, "  VERSCOPE #{0} symdiff/V_mc: retry {1:G3} exact {2:G3} nonstrict {3:G3} snapexact {4:G3}; negative winding volume: retry {5:G3} exact {6:G3} nonstrict {7:G3}",
            index, col.SymDiff[0] / vMc, col.SymDiff[1] / vMc, col.SymDiff[2] / vMc, col.SymDiff[3] / vMc, col.Negative[0], col.Negative[1], col.Negative[2]));
    }

    // families other than plates: the generated degenerate families (one mesh, union with nothing) in three frames, and sphere and box pairs that
    // nearly coincide (a turned copy, a different tessellation), at the origin and at 1e4: how often the retry fires and whether its result is right
    static int Families()
    {
        var inv = CultureInfo.InvariantCulture;
        var U = ExactMeshBoolean.Operations.Union;
        var cases = new List<(string name, DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)>();
        foreach (string frame in new[] { "identity", "rotated, scale 1e-3, utm", "rotated" }) {
            var sim = geometry3Sharp.Tests.Similarity.ByName(frame);
            foreach (var fam in new[] { geometry3Sharp.Tests.DegenerateFamilies.Family.Cluster, geometry3Sharp.Tests.DegenerateFamilies.Family.FoldedCluster,
                                        geometry3Sharp.Tests.DegenerateFamilies.Family.Collinear, geometry3Sharp.Tests.DegenerateFamilies.Family.Sliver })
                foreach (int k in new[] { 0, 30, 36, 40, 44, 48, 52 })
                    foreach (int count in new[] { 2, 4, 8 })
                        for (int seed = 0; seed < 6; seed++) {
                            if (k == 0 && fam == geometry3Sharp.Tests.DegenerateFamilies.Family.Sliver) continue;
                            cases.Add(("family " + fam + " k" + k + " count" + count + " seed" + seed + " " + frame,
                                geometry3Sharp.Tests.Invariance.Transformed(geometry3Sharp.Tests.DegenerateFamilies.Make(fam, k, seed, count), sim), new DMesh3(), U));
                        }
        }
        foreach (double shift in new[] { 0.0, 1e4 })
            foreach (double e in new[] { 1e-9, 1e-8, 1e-7, 1e-6 })
                foreach (var (n1, n2) in new[] { (8, 8), (8, 10), (14, 12) })
                    foreach (var op in new[] { D, I }) {
                        Func<DMesh3, Vector3d, DMesh3> at = (m, t) => { var c = new DMesh3(m); MeshTransforms.Translate(c, t); return c; };
                        var off = new Vector3d(shift, 0.5 * shift, -0.25 * shift);
                        DMesh3 s1 = at(Sphere(n1, Vector3d.Zero), off), s2 = Sphere(n2, Vector3d.Zero);
                        foreach (int v in s2.VertexIndices().ToArray()) s2.SetVertex(v, Rot(s2.GetVertex(v) + C, e, 0.7 * e) + off - C);
                        cases.Add((string.Format(inv, "sphere {0}/{1} turned {2:G2} shift {3} {4}", n1, n2, e, shift, op), s1, s2, op));
                        DMesh3 b1 = at(GridBox(n1 / 2, 1, 1, Vector3d.Zero), off), b2 = GridBox(n2 / 2, 1, 1, Vector3d.Zero);
                        foreach (int v in b2.VertexIndices().ToArray()) b2.SetVertex(v, Rot(b2.GetVertex(v) + C, e, 0.7 * e) + off - C);
                        cases.Add((string.Format(inv, "box {0}/{1} turned {2:G2} shift {3} {4}", n1 / 2, n2 / 2, e, shift, op), b1, b2, op));
                    }
        int noneFail = 0, retryOk = 0, retryFail = 0, wrong = 0, ruleA = 0, other = 0;
        double worst = 0, worstDV = 0;
        foreach (var (name, a, b, op) in cases) {
            var pn = new SolidBooleanPipeline(a, b, op) { AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.None };
            pn.Compute();
            if (pn.Status == SolidBooleanPipeline.PipelineStatus.Success) continue;
            if (!(pn.Status == SolidBooleanPipeline.PipelineStatus.BooleanFailed && pn.BooleanStatus == ExactMeshBoolean.BooleanStatus.RoundingNotClean)) { other++; continue; }
            noneFail++;
            var pd = new SolidBooleanPipeline(a, b, op);
            pd.Compute();
            if (pd.Status != SolidBooleanPipeline.PipelineStatus.Success) { retryFail++; continue; }
            retryOk++;
            AxisAlignedBox3d bb = a.GetBounds(); bb.Contain(b.GetBounds());
            Vector3d c = bb.Center;
            ColumnResult col = Columns(a, b, op, c, new[] { pd.Result }, 200, 4, 7);
            double vRes = VolumeAbout(pd.Result, c), err = Math.Abs(vRes - col.Expected) / Math.Abs(col.Expected);
            double dV = Math.Abs(pd.Report.RoundingVolumeAfter - pd.Report.RoundingVolumeBefore) / Math.Abs(pd.Report.RoundingVolumeBefore);
            worst = Math.Max(worst, err); worstDV = Math.Max(worstDV, dV);
            if (dV > 1e-3) ruleA++;
            bool bad = Math.Abs(vRes - col.Expected) > Math.Max(5 * col.ExpectedSe, 1e-3 * Math.Abs(col.Expected));
            if (bad) wrong++;
            Console.WriteLine(string.Format(inv, "FAM {0} | retry success, V_retry {1:G6} V_mc {2:G6} +- {3:G2} err {4:G3} dV/before {5:G3}{6} | {7}", name, vRes, col.Expected, col.ExpectedSe, err, dV, bad ? " WRONG" : "", pd.Report.Route.FirstOrDefault(x => x.Name == "Grid")?.Change));
        }
        Console.WriteLine($"families: cases {cases.Count}, None fails with RoundingNotClean {noneFail} (other failures {other}), default retry succeeds {retryOk}, retry fails again {retryFail}, wrong {wrong}, largest error {worst:G3}, largest dV/before {worstDV:G3}, rule (a) alarms {ruleA}");
        return 0;
    }
}
