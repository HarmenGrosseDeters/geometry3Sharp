// The probe of docs/research/SNAP_ROUNDING_EMPTY_BALL_GUARANTEE_2026-11.md: the certified empty ball as a guarantee
// (a clearance per triangle of the exact result against the displacement of its corners), not as a filter.
// `ball <case>` prints one JSON line with what ball.py needs to decide, in exact rational arithmetic, which triangles a
// clearance certificate covers under plain rounding and where the leftovers of SafeRounding sit:
//   - every vertex of the exact result (SafeRounding off, ComputeExactVertices): its rounded double position and its
//     exact rational coordinates, and its position in the SafeRounding result where the vertex is still there;
//   - every triangle, and the pairs of triangles with no common vertex whose distance in doubles (on the rounded
//     positions) is below 16 ulps of the largest coordinate: the only pairs whose exact distance can be below the
//     displacement of plain rounding (at most sqrt(3)/2 ulp per corner, so sqrt(3) per pair), with a wide margin for
//     the error of the double distance;
//   - the defects of plain rounding and those SafeRounding leaves (as vertex ids of the exact result).
// Nothing here decides in doubles: ball.py recomputes the distances of the listed pairs with fractions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

static class EmptyBall
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    internal static string Run(string name, DMesh3 a, DMesh3 b, Ops op)
    {
        var sb = new StringBuilder();
        sb.Append("{\"case\":").Append(System.Text.Json.JsonSerializer.Serialize(name));
        var plain = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { AllowOpenMeshes = true, SafeRounding = false, ComputeExactVertices = true };
        var safe = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { AllowOpenMeshes = true, ComputeExactVertices = true };
        if (!plain.Compute() || !safe.Compute())
            return sb.Append(",\"ok\":false}").ToString();
        DMesh3 raw = plain.Result, res = safe.Result;
        MeshRoundingRepair r = safe.Rounding;
        AxisAlignedBox3d box = gs.MeshRepairMetrics.TriangleBounds(raw);
        double largest = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs), ulp = Program.Ulp(largest);
        sb.Append(",\"ok\":true,\"ulp\":").Append(ulp.ToString("R", ci)).Append(",\"diag\":").Append(box.DiagonalLength.ToString("R", ci));
        sb.Append(",\"max_length\":").Append(r.MaxLength.ToString("R", ci)).Append(",\"max_drift\":").Append(r.MaxDrift.ToString("R", ci));
        sb.Append(",\"rounding_clean\":").Append(r.IsClean ? "true" : "false");

        // the exact point of a SafeRounding vertex names the vertex of the exact result it came from
        var byPoint = new Dictionary<ExactPoint3, int>();
        foreach (int v in raw.VertexIndices()) byPoint[plain.ResultExactVertices[v]] = v;
        var safePos = new Dictionary<int, Vector3d>();
        foreach (int v in res.VertexIndices())
            if (v < safe.ResultExactVertices.Length && byPoint.TryGetValue(safe.ResultExactVertices[v], out int pv)) safePos[pv] = res.GetVertex(v);

        sb.Append(",\"verts\":{");
        bool first = true;
        foreach (int v in raw.VertexIndices()) {
            ExactPoint3 e = plain.ResultExactVertices[v];
            Vector3d p = raw.GetVertex(v);
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(v).Append("\":[").Append(D(p.x)).Append(',').Append(D(p.y)).Append(',').Append(D(p.z));
            sb.Append(",\"").Append(e.X).Append("\",\"").Append(e.Y).Append("\",\"").Append(e.Z).Append("\",\"").Append(e.W).Append('"');
            if (safePos.TryGetValue(v, out Vector3d s)) sb.Append(',').Append(D(s.x)).Append(',').Append(D(s.y)).Append(',').Append(D(s.z));
            sb.Append(']');
        }
        sb.Append('}');
        int[] tids = raw.TriangleIndices().ToArray();
        sb.Append(",\"tris\":{").Append(string.Join(",", tids.Select(t => { Index3i tv = raw.GetTriangle(t); return $"\"{t}\":[{tv.a},{tv.b},{tv.c}]"; }))).Append('}');

        // the close pairs with no common vertex (all pairs: the results of the generated sets are small)
        double limit = 16 * ulp;
        var tri = new Triangle3d[tids.Length];
        var tb = new AxisAlignedBox3d[tids.Length];
        for (int i = 0; i < tids.Length; i++) {
            Index3i tv = raw.GetTriangle(tids[i]);
            tri[i] = new Triangle3d(raw.GetVertex(tv.a), raw.GetVertex(tv.b), raw.GetVertex(tv.c));
            tb[i] = new AxisAlignedBox3d(tri[i].V0, tri[i].V0);
            tb[i].Contain(tri[i].V1); tb[i].Contain(tri[i].V2);
            tb[i].Expand(limit);
        }
        var pairs = new List<string>();
        for (int i = 0; i < tids.Length; i++)
            for (int j = i + 1; j < tids.Length; j++) {
                if (!tb[i].Intersects(tb[j])) continue;
                Index3i ti = raw.GetTriangle(tids[i]), tj = raw.GetTriangle(tids[j]);
                bool share = false;
                for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) if (ti[x] == tj[y]) share = true;
                if (share) continue;
                double d = Math.Sqrt(new DistTriangle3Triangle3(tri[i], tri[j]).GetSquared());
                if (d < limit) pairs.Add($"[{tids[i]},{tids[j]},{D(d)}]");
            }
        sb.Append(",\"pairs\":[").Append(string.Join(",", pairs)).Append(']');

        // defects: of plain rounding (triangle ids of the exact result) and of SafeRounding (vertex ids of the exact result)
        sb.Append(",\"plain_degenerate\":[").Append(string.Join(",", Degenerate(raw))).Append(']');
        sb.Append(",\"plain_pairs\":[").Append(string.Join(",", gs.MeshRepairMetrics.SelfIntersectionPairs(raw).Select(p => $"[{p.a},{p.b}]"))).Append(']');
        var toPlain = new Func<int, int>(v => v < safe.ResultExactVertices.Length && byPoint.TryGetValue(safe.ResultExactVertices[v], out int pv) ? pv : -1);
        sb.Append(",\"left_degenerate\":[").Append(string.Join(",", Degenerate(res).Select(t => Corners(res, t, toPlain)))).Append(']');
        sb.Append(",\"left_pairs\":[").Append(string.Join(",", gs.MeshRepairMetrics.SelfIntersectionPairs(res).Select(p => "[" + Corners(res, p.a, toPlain) + "," + Corners(res, p.b, toPlain) + "]"))).Append(']');
        return sb.Append('}').ToString();
    }

    static IEnumerable<int> Degenerate(DMesh3 m)
    {
        double diag = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
        double eps = 1e-12 * diag * diag;
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            var T = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
            if (m.GetTriArea(t) < eps || ExactTriangleIntersection.IsDegenerate(ref T)) yield return t;
        }
    }

    static string Corners(DMesh3 m, int t, Func<int, int> toPlain)
    {
        Index3i tv = m.GetTriangle(t);
        return $"[{toPlain(tv.a)},{toPlain(tv.b)},{toPlain(tv.c)}]";
    }

    static string D(double x) => x.ToString("R", ci);
}
