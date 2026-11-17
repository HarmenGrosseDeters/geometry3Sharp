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

    /// <summary> signed volume by the divergence theorem over the triangles </summary>
    static double B30Volume(DMesh3 m)
    {
        double v = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            v += a.Dot(b.Cross(c)) / 6.0;
        }
        return v;
    }

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
        var nary = new ExactMeshBoolean(new[] { c.A, c.B, third }, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        if (!nary.Compute()) throw new Finding($"{what}: n-ary union Compute() false: {nary.Error}");
        var first = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        if (!first.Compute()) return;
        var chain = new ExactMeshBoolean(first.Result, third, ExactMeshBoolean.Operations.Union) { SafeRounding = false };
        if (!chain.Compute()) throw new Finding($"{what}: pairwise chain Compute() false: {chain.Error}");
        double tol = 1e-9 * Math.Pow(B30Scale(c.A, c.B, third), 3);
        double vn = B30Volume(nary.Result), vc = B30Volume(chain.Result);
        if (Math.Abs(vn - vc) > tol)
            throw new Finding($"{what}: n-ary volume {vn:R} against the chain {vc:R} (tolerance {tol:R})");
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
            if (!B30Identical(sr, p.Result)) throw new Finding($"{what}: serial and parallel union differ");
            var q = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Union);
            q.Compute();
            if (!B30Identical(p.Result, q.Result)) throw new Finding($"{what}: two parallel runs differ");
        } finally { gParallel.SerialOnThisThread = serial; }
    }

    /// <summary> reuse: one instance computed twice gives the same bits </summary>
    static void RunReuse(Case c, string what)
    {
        var op = new ExactMeshBoolean(c.A, c.B, ExactMeshBoolean.Operations.Difference);
        if (!op.Compute()) return;
        DMesh3 first = new DMesh3(op.Result);
        if (!op.Compute()) throw new Finding($"{what}: second Compute() false where the first succeeded: {op.Error}");
        if (!B30Identical(first, op.Result)) throw new Finding($"{what}: second Compute() of the same instance differs");
    }

    /// <summary> open inputs (one triangle of each operand removed) through all three operations: no failed status, and a
    /// success is a valid mesh; the boundary of an open input is not promised to be closed </summary>
    static void RunOpenAntipodal(Case c, string what)
    {
        DMesh3 a = new DMesh3(c.A), b = new DMesh3(c.B);
        if (a.TriangleCount > 1) a.RemoveTriangle(a.TriangleIndices().First());
        if (b.TriangleCount > 1) b.RemoveTriangle(b.TriangleIndices().First());
        foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
            var x = new ExactMeshBoolean(a, b, op);
            bool ok = x.Compute();
            if (x.Status == ExactMeshBoolean.BooleanStatus.InvalidInput || x.Status == ExactMeshBoolean.BooleanStatus.NotComputed)
                throw new Finding($"{what}: {op} status {x.Status} on finite open inputs: {x.Error}");
            if (ok && x.Result != null && x.Result.TriangleCount > 0 && !x.Result.CheckValidity(true, FailMode.ReturnOnly))
                throw new Finding($"{what}: {op} success with an invalid result on open inputs");
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
        double tol = 1e-4 * Math.Pow(B30Scale(c.A, c.B), 3);
        double vp = B30Volume(plain.Result), vs = B30Volume(safe.Result);
        if (Math.Abs(vp - vs) > tol)
            throw new Finding($"{what}: plain volume {vp:R} against SafeRounding {vs:R} (tolerance {tol:R})");
    }
}
