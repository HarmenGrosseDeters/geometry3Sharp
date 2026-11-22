using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

// Prototype for the study docs/research/OPEN_CUTTING_SURFACE_2026-11.md (#1211): what a Difference whose tool is an open
// cutting surface (a sheet) gives today, and what the candidate semantics would give, on procedural cases. Public API only;
// nothing here is library code. See README.md.
static class Program
{
    sealed class Case
    {
        public string Name, Note;
        public Func<DMesh3> Workpiece, Sheet;
        public double Intent = double.NaN;      // the volume of the cut by the sheet's sides, NaN where the sheet does not define one
        public Func<Vector3d, bool> Removed;    // the same intent as a point set: true where the tool is (null with NaN)
    }

    sealed class Outcome
    {
        public string Status = "";
        public DMesh3 Result;
        public double Ms;
    }

    // ---------------------------------------------------------------- builders

    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var m = new DMesh3();
        for (int i = 0; i < 8; ++i)
            m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
        int[] t = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 7, 3, 2, 6, 7, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
        for (int i = 0; i < 36; i += 3)
            m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
        return m;
    }

    static DMesh3 Cube() { return Box(0, 0, 0, 4, 4, 4); }

    // a height field z = h(x, y) on an nx x ny grid over [x0, x1] x [y0, y1], normals up (or down); cells with keep == false are left out
    static DMesh3 Grid(double x0, double y0, double x1, double y1, int nx, int ny, Func<double, double, double> h, bool up, Func<double, double, bool> keep = null)
    {
        var m = new DMesh3();
        var id = new int[nx + 1, ny + 1];
        for (int i = 0; i <= nx; ++i)
            for (int j = 0; j <= ny; ++j) {
                double x = x0 + (x1 - x0) * i / nx, y = y0 + (y1 - y0) * j / ny;
                id[i, j] = m.AppendVertex(new Vector3d(x, y, h(x, y)));
            }
        for (int i = 0; i < nx; ++i)
            for (int j = 0; j < ny; ++j) {
                double cx = x0 + (x1 - x0) * (i + 0.5) / nx, cy = y0 + (y1 - y0) * (j + 0.5) / ny;
                if (keep != null && !keep(cx, cy))
                    continue;
                int a = id[i, j], b = id[i + 1, j], c = id[i + 1, j + 1], d = id[i, j + 1];
                if (up) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                else { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
            }
        var used = new DMesh3(m, true);     // compact: drop the vertices of the cells left out
        return used;
    }

    static DMesh3 Plane(double z, bool up, double lo = -1, double hi = 5) { return Grid(lo, lo, hi, hi, 1, 1, (x, y) => z, up); }

    // the box [1,3]^2 x [1,5] without its top: a bucket whose opening (z = 5) is above the cube, normals outward
    static DMesh3 Bucket()
    {
        DMesh3 b = Box(1, 1, 1, 3, 3, 5);
        var m = new DMesh3();
        foreach (int v in b.VertexIndices())
            m.AppendVertex(b.GetVertex(v));
        foreach (int t in b.TriangleIndices()) {
            Index3i tv = b.GetTriangle(t);
            if (b.GetVertex(tv.a).z == 5 && b.GetVertex(tv.b).z == 5 && b.GetVertex(tv.c).z == 5)
                continue;
            m.AppendTriangle(tv);
        }
        return m;
    }

    static DMesh3 Reversed(DMesh3 m)
    {
        var r = new DMesh3(m);
        r.ReverseOrientation();
        return r;
    }

    // the bowl z = 1.5 + ((x-2)^2 + (y-2)^2) / 16 on a grid with lines on the cube's faces (step 1/2)
    static double Bowl(double x, double y) { return 1.5 + ((x - 2) * (x - 2) + (y - 2) * (y - 2)) / 16; }

    // the volume under the piecewise linear grid surface over [0,4]^2 (grid lines on x, y = 0 and 4), exact up to rounding
    static double VolumeUnder(DMesh3 sheet)
    {
        double sum = 0;
        foreach (int t in sheet.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            sheet.GetTriVertices(t, ref a, ref b, ref c);
            Vector3d m = (a + b + c) / 3;
            if (m.x < 0 || m.x > 4 || m.y < 0 || m.y > 4)
                continue;
            double area2 = Math.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
            sum += 0.5 * area2 * m.z;
        }
        return sum;
    }

    static List<Case> Cases()
    {
        var list = new List<Case>();
        void add(string name, Func<DMesh3> sheet, double intent, Func<Vector3d, bool> removed, string note)
        {
            list.Add(new Case { Name = name, Workpiece = Cube, Sheet = sheet, Intent = intent, Removed = removed, Note = note });
        }
        // convention (as #1223): the sheet is a part of the tool's outward surface, so the tool is behind it (the side its normals point away from)
        add("plane z=2, normals up", () => Plane(2, true), 32, p => p.z < 2, "the lower half is removed");
        add("plane z=2, normals down", () => Plane(2, false), 32, p => p.z > 2, "the upper half is removed");
        add("tilted plane z = 1 + x/2, up", () => Grid(-1, -1, 5, 5, 1, 1, (x, y) => 1 + x / 2, true), 32, p => p.z < 1 + p.x / 2, "crosses fully, not axis aligned");
        add("bowl (12x12 grid), up", () => Grid(-1, -1, 5, 5, 12, 12, Bowl, true), 64 - VolumeUnder(Grid(-1, -1, 5, 5, 12, 12, Bowl, true)), null, "a curved sheet that crosses fully");
        add("bucket (opening above), outward", Bucket, 64 - 12, p => p.x > 1 && p.x < 3 && p.y > 1 && p.y < 3 && p.z > 1, "a deep pocket: crosses fully, not a height field");
        add("rim on the surface: z=2 on [0,4]^2, up", () => Plane(2, true, 0, 4), 32, p => p.z < 2, "the free edges lie exactly on the cube's faces");
        add("half sheet: z=2, x in [-1,2], up", () => Grid(-1, -1, 2, 5, 1, 1, (x, y) => 2, true), double.NaN, null, "the sheet ends inside the solid");
        add("sheet with a hole [1,3]^2, up", () => Grid(-1, -1, 5, 5, 6, 6, (x, y) => 2, true, (x, y) => !(x > 1 && x < 3 && y > 1 && y < 3)), double.NaN, null, "a hole of the sheet inside the solid");
        add("disc inside: z=2 on [1,3]^2, up", () => Plane(2, true, 1, 3), double.NaN, null, "the whole rim is inside the solid");
        add("on the top face: z=4, up", () => Plane(4, true), 0, p => true, "coplanar with a face; the whole cube is behind");
        add("on the top face: z=4, down", () => Plane(4, false), 64, p => false, "coplanar with a face; the whole cube is in front");
        add("patch on the top face: z=4 on [1,3]^2, up", () => Plane(4, true, 1, 3), double.NaN, null, "touches a face, rim on the surface, cuts nothing");
        add("outside: z=6, up", () => Plane(6, true), double.NaN, null, "no contact: the sheet cuts nothing (behind its plane, but it bounds no cell)");
        return list;
    }

    // ---------------------------------------------------------------- measures

    static double Volume(DMesh3 m)
    {
        double v = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            v += a.Dot(b.Cross(c));
        }
        return v / 6;
    }

    // boundary edges after a weld by position (the boolean duplicates vertices at contacts)
    static int OpenEdges(DMesh3 m)
    {
        var ids = new Dictionary<Vector3d, int>();
        var edges = new Dictionary<long, int>();
        int id(Vector3d p) { if (!ids.TryGetValue(p, out int i)) ids[p] = i = ids.Count; return i; }
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            int[] v = { id(m.GetVertex(tv.a)), id(m.GetVertex(tv.b)), id(m.GetVertex(tv.c)) };
            for (int k = 0; k < 3; ++k) {
                int a = v[k], b = v[(k + 1) % 3];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                edges.TryGetValue(key, out int n);
                edges[key] = n + (a < b ? 1 : -1);
            }
        }
        return edges.Values.Count(n => n != 0);
    }

    static string F(double v) { return double.IsNaN(v) ? "-" : Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture); }

    static string Describe(Outcome o, Case c)
    {
        if (o.Result == null)
            return o.Status;
        double v = Volume(o.Result);
        int open = OpenEdges(o.Result);
        // the volume, and where the intent is a point set also the side: the result's winding number at 6^3 points
        bool right = Math.Abs(v - c.Intent) < 1e-9 * 64;
        if (right && c.Removed != null && open == 0) {
            for (int i = 0; i < 6 && right; ++i)
                for (int j = 0; j < 6 && right; ++j)
                    for (int k = 0; k < 6 && right; ++k) {
                        var p = new Vector3d(4 * (i + 0.37) / 6, 4 * (j + 0.41) / 6, 4 * (k + 0.43) / 6);     // on no face of a case
                        bool inside = o.Result.TriangleCount > 0 && Math.Abs(o.Result.WindingNumber(p)) > 0.5;
                        right = inside == !c.Removed(p);
                    }
        }
        string verdict = double.IsNaN(c.Intent) ? "" : right ? " ok" : " WRONG";
        return o.Status + " " + F(v) + (open > 0 ? " open(" + open + ")" : "") + verdict;
    }

    static Outcome Timed(Func<Outcome> f)
    {
        var sw = Stopwatch.StartNew();
        Outcome o;
        try { o = f(); }
        catch (Exception e) { o = new Outcome { Status = "threw " + e.GetType().Name }; }
        o.Ms = sw.Elapsed.TotalMilliseconds;
        return o;
    }

    // ---------------------------------------------------------------- what master has

    static Outcome BooleanOpen(DMesh3 w, DMesh3 s)
    {
        var op = new ExactMeshBoolean(w, s, ExactMeshBoolean.Operations.Difference);
        op.Compute();
        return new Outcome { Status = op.Status.ToString() + (op.ConflictEdges > 0 ? " conflict(" + op.ConflictEdges + ")" : ""), Result = op.Result };
    }

    // a proxy of the signal for the silent case, from what the boolean reports today: an open tool whose triangles meet the
    // workpiece's (IntersectingPairs) and a result with the workpiece's volume (exact equality: fine for these dyadic cases only)
    static string NoEffectSignal(DMesh3 w, DMesh3 s)
    {
        var op = new ExactMeshBoolean(w, s, ExactMeshBoolean.Operations.Difference);
        if (!op.Compute())
            return op.Status.ToString();
        bool open = !op.ExactWinding[1], unchanged = Volume(op.Result) == Volume(w) && OpenEdges(op.Result) == 0;
        return "pairs " + op.IntersectingPairs + (open && unchanged ? (op.IntersectingPairs > 0 ? ", unchanged: FLAG" : ", unchanged, no contact") : ", changed");
    }

    static Outcome Pipeline(DMesh3 w, DMesh3 s, SolidBooleanPipeline.InputRepairModes mode)
    {
        var p = new SolidBooleanPipeline(w, s, ExactMeshBoolean.Operations.Difference) { InputRepair = mode };
        p.Compute();
        return new Outcome { Status = p.Status.ToString() + (p.Status == SolidBooleanPipeline.PipelineStatus.Success ? (p.Report.Clean ? " clean" : " notclean") : ""), Result = p.Result };
    }

    // MeshRobustRepair of the sheet (it may solidify or wrap), then the closed Difference
    static Outcome RepairThenBoolean(DMesh3 w, DMesh3 s, out string repair)
    {
        var r = new MeshRobustRepair(new DMesh3(s));
        bool ok = r.Apply();
        DMesh3 tool = r.Mesh;
        repair = (ok ? "" : "failed ") + r.ResultPath + (tool != null && tool.TriangleCount > 0 ? " vol " + F(Volume(tool)) + " tris " + tool.TriangleCount : " empty");
        if (tool == null || tool.TriangleCount == 0)
            return new Outcome { Status = "tool empty" };
        var op = new ExactMeshBoolean(w, tool, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = false };
        op.Compute();
        return new Outcome { Status = op.Status.ToString(), Result = op.Result };
    }

    // ExactMeshImprint of the sheet on the workpiece: the regions of the workpiece's surface between the curves
    static string Imprint(DMesh3 w, DMesh3 s)
    {
        var imp = new ExactMeshImprint() { Target = w, Cutter = s };
        if (!imp.Compute())
            return imp.Status.ToString();
        DMesh3 r = imp.Result;
        var seen = new HashSet<int>();
        int regions = 0;
        foreach (int t0 in r.TriangleIndices()) {
            if (!seen.Add(t0))
                continue;
            ++regions;
            var stack = new Stack<int>();
            stack.Push(t0);
            while (stack.Count > 0) {
                int t = stack.Pop();
                Index3i te = r.GetTriEdges(t);
                for (int k = 0; k < 3; ++k) {
                    Index2i ev = r.GetEdgeV(te[k]);
                    if (imp.CurveEdges.Contains(ev.a < ev.b ? ((long)ev.a << 32) | (uint)ev.b : ((long)ev.b << 32) | (uint)ev.a))
                        continue;
                    Index2i et = r.GetEdgeT(te[k]);
                    int o = et.a == t ? et.b : et.a;
                    if (o >= 0 && seen.Add(o))
                        stack.Push(o);
                }
            }
        }
        return imp.CurveEdges.Count + " curve edges, " + regions + " region" + (regions == 1 ? "" : "s");
    }

    // MeshPlaneCut (floating point) with the plane of a planar sheet, holes filled; "-" for a sheet that is not planar
    static Outcome PlaneCut(DMesh3 w, DMesh3 s)
    {
        Vector3d n = Vector3d.Zero, o = Vector3d.Zero;
        bool first = true;
        foreach (int t in s.TriangleIndices()) {
            Vector3d tn = s.GetTriNormal(t);
            if (first) { n = tn; o = s.GetTriCentroid(t); first = false; }
            else if ((tn - n).Length > 1e-12 || Math.Abs((s.GetTriCentroid(t) - o).Dot(n)) > 1e-12)
                return new Outcome { Status = "-" };
        }
        var m = new DMesh3(w);
        // MeshPlaneCut removes the side the normal points to; the tool is behind the sheet
        var cut = new MeshPlaneCut(m, o, -n);
        bool ok = cut.Cut() && cut.FillHoles();
        return new Outcome { Status = ok ? "ok" : "failed", Result = m };
    }

    // ---------------------------------------------------------------- candidate semantics

    // B (#1223): the role Sheet of ExactMeshBoolean, by reflection, so that this project builds without it
    static bool HasSheetRole { get { return typeof(ExactMeshBoolean).GetField("Roles") != null; } }

    static Outcome SheetRole(DMesh3 w, DMesh3 s, string uncutSide = null)
    {
        var field = typeof(ExactMeshBoolean).GetField("Roles");
        if (field == null)
            return new Outcome { Status = "n/a" };
        Type role = typeof(ExactMeshBoolean).GetNestedType("InputRole");
        var roles = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(role));
        roles.Add(Enum.Parse(role, "Solid"));
        roles.Add(Enum.Parse(role, "Sheet"));
        var op = new ExactMeshBoolean(w, s, ExactMeshBoolean.Operations.Difference);
        field.SetValue(op, roles);
        if (uncutSide != null) {
            var side = typeof(ExactMeshBoolean).GetField("SheetUncutSide");
            side.SetValue(op, Enum.Parse(side.FieldType, uncutSide));
        }
        op.Compute();
        string status = op.Status.ToString();
        if (op.Result == null)
            status += " " + typeof(ExactMeshBoolean).GetField("SheetReason").GetValue(op);
        return new Outcome { Status = status, Result = op.Result };
    }

    // C: the point set { p in W : w_S(p) <= tau } by sampling (cell centres of an n^3 grid on the cube), as a volume.
    // tau = 1/2 is today's rule for open inputs, tau = 0 the sign rule (the half-space for a planar sheet)
    static void Winding(Case c, DMesh3 s, int n, out double volHalf, out double volSign, out double wrongHalf, out double wrongSign)
    {
        int keptHalf = 0, keptSign = 0, badHalf = 0, badSign = 0;
        var tris = new List<Vector3d[]>();
        foreach (int t in s.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, cc = Vector3d.Zero;
            s.GetTriVertices(t, ref a, ref b, ref cc);
            tris.Add(new[] { a, b, cc });
        }
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < n; ++j)
                for (int k = 0; k < n; ++k) {
                    var p = new Vector3d(4 * (i + 0.5) / n, 4 * (j + 0.5) / n, 4 * (k + 0.5) / n);
                    double w = 0;
                    foreach (Vector3d[] t in tris)
                        w += MathUtil.TriSolidAngle(t[0], t[1], t[2], ref p);
                    w /= 4 * Math.PI;
                    bool inHalf = w > 0.5, inSign = w > 0;
                    if (!inHalf) ++keptHalf;
                    if (!inSign) ++keptSign;
                    if (c.Removed != null) {
                        bool truth = c.Removed(p);
                        if (truth != inHalf) ++badHalf;
                        if (truth != inSign) ++badSign;
                    }
                }
        double cell = 64.0 / ((double)n * n * n);
        volHalf = keptHalf * cell; volSign = keptSign * cell;
        wrongHalf = c.Removed == null ? double.NaN : 100.0 * badHalf / ((double)n * n * n);
        wrongSign = c.Removed == null ? double.NaN : 100.0 * badSign / ((double)n * n * n);
    }

    // D: the sheet swept against its normals to a closed tool that ends beyond the workpiece's box, or null with a reason
    // where a triangle does not face the sweep. That test is necessary, not sufficient, for an embedded tool: two parts of
    // the sheet stacked along the direction pass it, and the closed boolean then takes the union of their prisms.
    // (MeshExtrudeMesh with ExtrudedPositionF as a translation builds the same shell; this one keeps the prototype self-contained.)
    static DMesh3 SweptTool(DMesh3 w, DMesh3 s, out string reason)
    {
        Vector3d sum = Vector3d.Zero;
        foreach (int t in s.TriangleIndices())
            sum += s.GetTriArea(t) * s.GetTriNormal(t);
        if (sum.Length == 0) { reason = "no direction (the area normals cancel)"; return null; }
        Vector3d d = -sum.Normalized;
        foreach (int t in s.TriangleIndices())
            if (s.GetTriNormal(t).Dot(d) >= 0) { reason = "a triangle does not face the sweep"; return null; }
        AxisAlignedBox3d box = w.GetBounds();
        box.Contain(s.GetBounds());
        double length = Math.Pow(2, Math.Ceiling(Math.Log(2 * box.DiagonalLength, 2)));
        var m = new DMesh3();
        var top = new Dictionary<int, int>();
        var bottom = new Dictionary<int, int>();
        foreach (int v in s.VertexIndices()) {
            top[v] = m.AppendVertex(s.GetVertex(v));
            bottom[v] = m.AppendVertex(s.GetVertex(v) + length * d);
        }
        foreach (int t in s.TriangleIndices()) {
            Index3i tv = s.GetTriangle(t);
            m.AppendTriangle(top[tv.a], top[tv.b], top[tv.c]);
            m.AppendTriangle(bottom[tv.a], bottom[tv.c], bottom[tv.b]);
            Index3i te = s.GetTriEdges(t);
            for (int k = 0; k < 3; ++k) {
                if (!s.IsBoundaryEdge(te[k]))
                    continue;
                int a = tv[k], b = tv[(k + 1) % 3];     // the triangle runs a -> b, the wall b -> a
                m.AppendTriangle(top[b], top[a], bottom[a]);
                m.AppendTriangle(top[b], bottom[a], bottom[b]);
            }
        }
        reason = null;
        return m;
    }

    static Outcome Sweep(DMesh3 w, DMesh3 s)
    {
        DMesh3 tool = SweptTool(w, s, out string reason);
        if (tool == null)
            return new Outcome { Status = "refused: " + reason };
        var op = new ExactMeshBoolean(w, tool, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = false };
        op.Compute();
        return new Outcome { Status = op.Status.ToString(), Result = op.Result };
    }

    // ---------------------------------------------------------------- modes

    static void Table(string title, string[] head, List<string[]> rows)
    {
        Console.WriteLine();
        Console.WriteLine("### " + title);
        Console.WriteLine();
        Console.WriteLine("| " + string.Join(" | ", head) + " |");
        Console.WriteLine("|" + string.Concat(head.Select(h => "---|")));
        foreach (string[] r in rows)
            Console.WriteLine("| " + string.Join(" | ", r) + " |");
    }

    static void Today()
    {
        var rows = new List<string[]>();
        var repairs = new List<string[]>();
        foreach (Case c in Cases()) {
            DMesh3 w = c.Workpiece(), s = c.Sheet();
            Outcome rr = Timed(() => { Outcome o = RepairThenBoolean(w, s, out string rep); repairs.Add(new[] { c.Name, rep }); return o; });
            rows.Add(new[] { c.Name, F(c.Intent),
                Describe(Timed(() => BooleanOpen(w, s)), c),
                Describe(Timed(() => Pipeline(w, s, SolidBooleanPipeline.InputRepairModes.Refuse)), c),
                Describe(Timed(() => Pipeline(w, s, SolidBooleanPipeline.InputRepairModes.AutoRepair)), c),
                Describe(Timed(() => Pipeline(w, s, SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust)), c),
                Describe(rr, c),
                Describe(Timed(() => PlaneCut(w, s)), c),
                Imprint(w, s), NoEffectSignal(w, s) });
        }
        Table("What the existing parts give (volume of the result; the cube has 64)",
            new[] { "case", "intent", "ExactMeshBoolean (open, w > 1/2)", "pipeline Refuse", "pipeline AutoRepair", "pipeline AutoRepairThenRobust", "MeshRobustRepair of the sheet, then closed Difference", "MeshPlaneCut + FillHoles", "ExactMeshImprint", "open tool without effect (signal)" }, rows);
        Table("MeshRobustRepair of the sheet alone", new[] { "case", "result path, volume, triangles" }, repairs);
    }

    static void Semantics(int n)
    {
        var rows = new List<string[]>();
        foreach (Case c in Cases()) {
            DMesh3 w = c.Workpiece(), s = c.Sheet();
            Winding(c, s, n, out double vh, out double vs, out double bh, out double bs);
            string split = "n/a";
            if (HasSheetRole) {
                Outcome front = SheetRole(w, s), back = SheetRole(w, Reversed(s));
                split = front.Result != null && back.Result != null ? F(Volume(front.Result)) + " + " + F(Volume(back.Result)) : "refused";
            }
            rows.Add(new[] { c.Name, F(c.Intent), "refused",
                Describe(SheetRole(w, s), c), Describe(SheetRole(w, s, "Outside"), c),
                F(vh) + (double.IsNaN(bh) ? "" : " (" + F(bh) + " % wrong)"),
                F(vs) + (double.IsNaN(bs) ? "" : " (" + F(bs) + " % wrong)"),
                Describe(Sweep(w, s), c), split });
        }
        Table("The candidate semantics (volume of the result; C by sampling " + n + "^3 points)",
            new[] { "case", "intent", "A refuse", "B sheet role (#1223)", "B, UncutSide = Outside", "C winding > 1/2 (sampled)", "C winding > 0 (sampled)", "D swept tool, closed Difference", "E split (front + back, from B)" }, rows);
    }

    // cost: a sphere (radius 1) cut by a wavy sheet; median of `runs` after a warm-up
    static void Cost(int runs, int[] sizes)
    {
        var rows = new List<string[]>();
        foreach (int n in sizes) {
            DMesh3 w = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            int g = Math.Max(4, n);
            DMesh3 s = Grid(-1.5, -1.5, 1.5, 1.5, g, g, (x, y) => 0.125 * Math.Sin(3 * x) * Math.Cos(3 * y), true);
            DMesh3 closedTool = SweptTool(w, s, out _);
            double median(Func<Outcome> f, out Outcome last)
            {
                Timed(f);
                var ms = new List<double>();
                Outcome o = null;
                for (int i = 0; i < runs; ++i) { o = Timed(f); ms.Add(o.Ms); }
                ms.Sort();
                last = o;
                return ms[ms.Count / 2];
            }
            double tOpen = median(() => BooleanOpen(w, s), out Outcome open);
            double tSweep = median(() => Sweep(w, s), out Outcome sweep);
            double tRole = median(() => SheetRole(w, s), out Outcome role);
            double tImprint = median(() => { var imp = new ExactMeshImprint() { Target = w, Cutter = s }; imp.Compute(); return new Outcome { Status = imp.Status.ToString(), Result = imp.Result }; }, out _);
            string vol(Outcome o) { return o.Result == null ? o.Status : F(Volume(o.Result)) + (OpenEdges(o.Result) > 0 ? " open" : ""); }
            rows.Add(new[] { w.TriangleCount.ToString(), s.TriangleCount.ToString(), closedTool.TriangleCount.ToString(),
                tOpen.ToString("0") + " (" + vol(open) + ")", tSweep.ToString("0") + " (" + vol(sweep) + ")",
                HasSheetRole ? tRole.ToString("0") + " (" + vol(role) + ")" : "n/a", tImprint.ToString("0") });
        }
        Table("Cost, wall ms (median of " + runs + "; result volume in brackets; the sphere has about 4.19)",
            new[] { "sphere triangles", "sheet triangles", "swept tool triangles", "today: open boolean", "D swept tool", "B sheet role", "imprint only" }, rows);
    }

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        string mode = args.Length > 0 ? args[0] : "all";
        Console.WriteLine("sheet role available (ExactMeshBoolean.Roles): " + HasSheetRole);
        if (mode == "today" || mode == "all")
            Today();
        if (mode == "semantics" || mode == "all")
            Semantics(args.Length > 1 && mode == "semantics" ? int.Parse(args[1]) : 48);
        if (mode == "cost" || mode == "all")
            Cost(5, mode == "cost" && args.Length > 1 ? args.Skip(1).Select(int.Parse).ToArray() : new[] { 32, 64, 128 });
        return 0;
    }
}
