using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MeshFuzzPilot;

// Exact Boolean 3.0 targets (#1038, phase 2): n-ary entry point, serial against parallel, reuse, open inputs with
// the antipodal paths, and SafeRounding against the plain arrangement. They are appended after trim-loop, so the
// indices of the older operations (and their artifacts) do not change.
public static partial class Program
{
    static bool B30Identical(DMesh3 x, DMesh3 y)
    {
        if (x.VertexCount != y.VertexCount || x.TriangleCount != y.TriangleCount) return false;
        var xv = x.VertexIndices().ToList(); var yv = y.VertexIndices().ToList();
        for (int i = 0; i < xv.Count; i++) if (x.GetVertex(xv[i]) != y.GetVertex(yv[i])) return false;
        var xt = x.TriangleIndices().ToList(); var yt = y.TriangleIndices().ToList();
        for (int i = 0; i < xt.Count; i++) if (x.GetTriangle(xt[i]) != y.GetTriangle(yt[i])) return false;
        return true;
    }

    /// <summary> the judgement of the identity targets: the same bits (a Finding with the message when not) </summary>
    public static void B30CheckIdentical(string what, string message, DMesh3 x, DMesh3 y)
    {
        if (!B30Identical(x, y)) throw new Finding($"{what}: {message}");
    }

    /// <summary> the judgement of the volume targets: two volumes within the tolerance (also false for NaN) </summary>
    public static void B30CheckVolumes(string what, string label, double first, double second, double tol)
    {
        if (!(Math.Abs(first - second) <= tol))
            throw new Finding($"{what}: {label}: {first:R} against {second:R} (tolerance {tol:R})");
    }

    /// <summary> a status that is a finding on finite open inputs, and a success that must be a valid mesh </summary>
    public static void B30CheckOpenStatus(string what, ExactMeshBoolean.Operations op, ExactMeshBoolean.BooleanStatus status, bool ok, bool valid)
    {
        if (status == ExactMeshBoolean.BooleanStatus.InvalidInput || status == ExactMeshBoolean.BooleanStatus.NotComputed)
            throw new Finding($"{what}: {op} status {status} on finite open inputs");
        if (ok && !valid)
            throw new Finding($"{what}: {op} success with an invalid result on open inputs");
    }

    /// <summary> signed volume by the divergence theorem over the triangles, summed relative to the bounds center (a sum from the origin loses
    /// the volume of a mesh far from it to cancellation: two cubes of edge 16 at an offset of 2^40 gave 6.0815e18 against 6.0818e18). absolute: the
    /// sum of the absolute terms, the scale of the summation error </summary>
    static double B30Volume(DMesh3 m, out double absolute)
    {
        absolute = 0;
        if (m.TriangleCount == 0) return 0;
        Vector3d center = m.GetBounds().Center;
        double v = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double term = (a - center).Dot((b - center).Cross(c - center)) / 6.0;
            v += term;
            absolute += Math.Abs(term);
        }
        return v;
    }

    static double B30Volume(DMesh3 m) => B30Volume(m, out _);

    /// <summary> the tolerance of a volume comparison: a relative part of the cube of the scale, and the summation error of both volumes
    /// (a thousand units of the last place of the sum of the absolute terms) </summary>
    static double B30VolumeTolerance(double relative, double scale, double absoluteFirst, double absoluteSecond) =>
        relative * scale * scale * scale + 1e3 * 2.220446049250313e-16 * (absoluteFirst + absoluteSecond);

    static double B30Scale(params DMesh3[] meshes)
    {
        AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
        foreach (DMesh3 m in meshes) box.Contain(m.GetBounds());
        return Math.Max(Diagonal(box), 1e-300);
    }

    /// <summary> n-ary union of three operands against the chain of pairwise unions: same volume. Without SafeRounding: the
    /// repair may move the surface by up to RoundingMaxDisplacement (1e-5) of the diagonal and contracts short edges, so a chain
    /// (which repairs each intermediate result) and the n-ary union (one repair) legitimately differ by that much; the
    /// arrangement itself is exact, and there they agree (crash 096845c5 of campaign b30-n1-v2: 1.9e-6, within the contract) </summary>
    static void RunNary(Case c, string what)
    {
        // volumes are compared, which is only a contract for closed operands: an open operand is a generalized winding
        // number, and the chain feeds an intermediate result into the next union (crash 04511314 of campaign b30-n1: two
        // open boxes, one folded, gave -1.7e-71 against -3.4e-71; with a closed bottom the two agree to 15 digits)
        if (!c.A.IsClosed() || !c.B.IsClosed()) return;
        DMesh3 third = new DMesh3(c.B);
        double d = B30Scale(c.A, c.B);
        foreach (int v in third.VertexIndices()) third.SetVertex(v, third.GetVertex(v) + new Vector3d(2 * d, 0, 0));
        // (an operand with a coordinate near double.MaxValue: the diagonal overflows and the shifted copy is not finite, which
        // the boolean rightly refuses (crash 1377d9ea of campaign b30-n1-v3), so there is nothing to compare)
        if (!AllFinite(third)) return;
        var nary = new ExactMeshBoolean(new[] { c.A, c.B, third }, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        if (!nary.Compute()) throw new Finding($"{what}: n-ary union Compute() false: {nary.Error}");
        var first = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        if (!first.Compute()) throw new Finding($"{what}: pairwise union Compute() false where the n-ary union succeeded: {first.Error}");
        var chain = new ExactMeshBoolean(first.Result, third, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        if (!chain.Compute()) throw new Finding($"{what}: pairwise chain Compute() false: {chain.Error}");
        double vn = B30Volume(nary.Result, out double an), vc = B30Volume(chain.Result, out double ac);
        double tol = B30VolumeTolerance(1e-9, B30Scale(c.A, c.B, third), an, ac);
        // (a volume or tolerance that overflows is a representation limit, not a finding: nothing to compare)
        if (!double.IsFinite(vn) || !double.IsFinite(vc) || !double.IsFinite(tol)) return;
        B30CheckVolumes(what, "n-ary volume against the chain", vn, vc, tol);
    }

    /// <summary> determinism: one union serial and parallel, and a second parallel run, give the same bits (#774) </summary>
    static void RunDeterminism(Case c, string what)
    {
        bool serial = gParallel.SerialOnThisThread;
        try {
            gParallel.SerialOnThisThread = true;
            var s = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union);
            if (!s.Compute()) return;
            DMesh3 sr = new DMesh3(s.Result);
            gParallel.SerialOnThisThread = false;
            var p = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union);
            if (!p.Compute()) throw new Finding($"{what}: parallel Compute() false where serial succeeded: {p.Error}");
            B30CheckIdentical(what, "serial and parallel union differ", sr, p.Result);
            var q = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union);
            if (!q.Compute()) throw new Finding($"{what}: second parallel Compute() false where the first succeeded: {q.Error}");
            B30CheckIdentical(what, "two parallel runs differ", p.Result, q.Result);
        } finally { gParallel.SerialOnThisThread = serial; }
    }

    /// <summary> reuse: one instance computed twice gives the same bits, and with new inputs and another operation it gives what a fresh instance
    /// gives (state kept from the first call would show only there: the same inputs twice give the same result either way) </summary>
    static void RunReuse(Case c, string what)
    {
        var op = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Difference);
        if (!op.Compute()) return;
        DMesh3 first = new DMesh3(op.Result);
        if (!op.Compute()) throw new Finding($"{what}: second Compute() false where the first succeeded: {op.Error}");
        B30CheckIdentical(what, "second Compute() of the same instance differs", first, op.Result);
        op.MeshA = c.B; op.MeshB = c.A; op.Operation = ExactMeshBoolean.Operations.Intersection;
        var fresh = new ExactMeshBoolean(c.B, c.A, ExactMeshBoolean.Operations.Intersection);
        bool okFresh = fresh.Compute(), okReused = op.Compute();
        if (okFresh != okReused) throw new Finding($"{what}: new inputs on a reused instance: Compute() {okReused} ({op.Status}), a fresh instance {okFresh} ({fresh.Status})");
        if (okFresh) B30CheckIdentical(what, "new inputs on a reused instance differ from a fresh instance", fresh.Result, op.Result);
    }

    /// <summary> the time one input may take over its operations before the rest of them is skipped: with 600 orders of magnitude between
    /// the coordinates every filter fails and the open-input classification runs on exact numbers, 6 to 7 s per operation on a pair of
    /// 24 and 10 triangles (the timeout artifact f873b37a of campaign b30-n2-v3: 21 s for the three operations, above libFuzzer's 30 s under load).
    /// A cost limit of the target, not of the correctness checks: the operations that ran are judged in full </summary>
    public static bool B30OverBudget(TimeSpan elapsed) => elapsed > TimeSpan.FromSeconds(5);

    /// <summary> open inputs (one triangle of each operand removed) through all three operations: no failed status, and a
    /// success is a valid mesh; the boundary of an open input is not promised to be closed </summary>
    static void RunOpenAntipodal(Case c, string what)
    {
        DMesh3 a = new DMesh3(c.A), b = new DMesh3(c.B);
        if (a.TriangleCount > 1) a.RemoveTriangle(a.TriangleIndices().First());
        if (b.TriangleCount > 1) b.RemoveTriangle(b.TriangleIndices().First());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
            if (B30OverBudget(clock.Elapsed)) return;
            var x = new ExactMeshBoolean(a, b, op);
            bool ok = x.Compute();
            B30CheckOpenStatus(what, op, x.Status, ok, x.Result != null && (x.Result.TriangleCount == 0 || x.Result.CheckValidity(true, FailMode.ReturnOnly)));
        }
    }

    /// <summary> SafeRounding against the plain arrangement: when both succeed and the repair is clean, the volumes agree within
    /// the repair's documented bound (the surface moves by at most RoundingMaxDisplacement = 1e-5 of the diagonal; 1e-4 of the cube of
    /// the diagonal covers it for the area of these shapes) </summary>
    static void RunRoundingEquivalence(Case c, string what)
    {
        if (!c.A.IsClosed() || !c.B.IsClosed()) return;   // volumes are judged on closed operands only (see RunNary)
        var plain = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        var safe = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union) { SafeRounding = true };
        bool okP = plain.Compute(), okS = safe.Compute();
        if (!okP || !okS || plain.Result == null || safe.Result == null || !safe.RoundingClean) return;
        double vp = B30Volume(plain.Result, out double ap), vs = B30Volume(safe.Result, out double asf);
        double tol = B30VolumeTolerance(1e-4, B30Scale(c.A, c.B), ap, asf);
        if (!double.IsFinite(vp) || !double.IsFinite(vs) || !double.IsFinite(tol)) return;
        B30CheckVolumes(what, "plain volume against SafeRounding", vp, vs, tol);
    }
}
