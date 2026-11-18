#pragma warning disable CS0618 // the cases compare the deprecated float MeshBoolean with the exact one
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

static class Cases
{
    static Program.Xf[] Scales(params int[] ks) => ks.Select(k => new Program.Xf { Name = "2^" + k, Scale = Math.Pow(2, k) }).ToArray();
    static Program.Xf[] Syms() => Enumerable.Range(1, 47).Where(s => s % 6 == 0 || s % 6 == 1 || s == 7 || s == 3 || s == 13 || s == 29 || s == 47).Select(s => new Program.Xf { Name = "sym" + s, Sym = s }).ToArray();

    static DMesh3 Mesh(double[][] v, int[][] t, double s = 1)
    {
        var m = new DMesh3(); foreach (var p in v) m.AppendVertex(new Vector3d(p[0] * s, p[1] * s, p[2] * s));
        foreach (var x in t) m.AppendTriangle(x[0], x[1], x[2]); return m;
    }

    public static List<Program.Case> All() => new List<Program.Case> { C856(), C745(), C644(), C712(), C719(), C710() };

    // #856 MeshMeshCut: a cut mesh that touches the target; the issue's own repro (2^k scales of the same input).
    static Program.Case C856()
    {
        double[][] av = { new[]{0.0,0,0}, new[]{1.0,0,0}, new[]{1.0,1,0}, new[]{0.0,1,0}, new[]{0.0,0,1}, new[]{1.0,0,1}, new[]{1.0,1,1}, new[]{0.125,1.4375,7.625} };
        int[][] at = { new[]{0,2,1}, new[]{0,3,2}, new[]{4,5,6}, new[]{4,6,7}, new[]{0,1,5}, new[]{0,5,4}, new[]{1,2,6}, new[]{1,6,5}, new[]{2,3,7}, new[]{2,7,6}, new[]{3,0,4}, new[]{3,4,7} };
        double[][] bv = { new[]{0.01806640625,0,0}, new[]{0.01810455322265625,2.47955322265625E-05,1.1444091796875E-05}, new[]{0.018096923828125,3.0517578125E-05,0},
            new[]{0.01806640625,3.0517578125E-05,0}, new[]{0.018133163452148438,1.33514404296875E-05,0.0001239776611328125}, new[]{0.018096923828125,0,3.0517578125E-05},
            new[]{0.018096923828125,3.0517578125E-05,3.0517578125E-05}, new[]{0.01806640625,3.0517578125E-05,3.0517578125E-05} };
        int[][] bt = { new[]{4,5,6}, new[]{4,6,7}, new[]{0,1,5}, new[]{0,5,4}, new[]{1,2,6}, new[]{1,6,5}, new[]{2,3,7}, new[]{2,7,6}, new[]{3,0,4}, new[]{3,4,7} };
        return new Program.Case { Issue = "#856", Name = "MeshMeshCut, cut mesh touching the target (parent of #867)", Scales = Scales(-7, -6, -5, -4, -3, -2, -1, 1, 2, 3, 4, 5), Syms = Syms(),
            Run = x => { DMesh3 a = x.Apply(Mesh(av, at)), b = x.Apply(Mesh(bv, bt));
                new MeshMeshCut { Target = a, CutMesh = b }.Compute(); return Program.Sig(a, x); } };
    }

    // #745 PointAABBTree3.FastWindingNumber: values differ and are slower at 2^k scales (issue's repro).
    static Program.Case C745()
    {
        return new Program.Case { Issue = "#745", Name = "PointAABBTree3.FastWindingNumber under 2^k", Scales = Scales(-40, -46, -50), Syms = null, NoPerm = true,
            Run = x => { DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 40 }.Generate().MakeDMesh(); MeshNormals.QuickCompute(m);
                var area = new double[m.MaxVertexID]; foreach (int t in m.TriangleIndices()) { var tv = m.GetTriangle(t); double a = m.GetTriArea(t) / 3; area[tv.a] += a; area[tv.b] += a; area[tv.c] += a; }
                double s = x.Scale; MeshTransforms.Scale(m, s);
                var tree = new PointAABBTree3(m, true) { FWNAreaEstimateF = v => area[v] * s * s };
                var rnd = new Random(1); ulong h = 1469598103934665603UL;
                for (int i = 0; i < 20000; ++i) { var q = new Vector3d((rnd.NextDouble() * 2 - 1) * 1.2, (rnd.NextDouble() * 2 - 1) * 1.2, (rnd.NextDouble() * 2 - 1) * 1.2) * s;
                    h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(tree.FastWindingNumber(q))) * 1099511628211UL; }
                return h.ToString("X"); } };
    }

    // #644 ConvexHull2: extent <= 2^-52 reported as Dimension 1 / wrong ring (absolute threshold). Relation: 2^k scale of a normal set.
    static Program.Case C644()
    {
        var rnd = new Random(7); var pts = Enumerable.Range(0, 24).Select(i => new Vector2d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1)).ToArray();
        return new Program.Case { Issue = "#644", Name = "ConvexHull2 dimension / ring at tiny extent", Scales = Scales(-52, -60, -80, -200, -500), Syms = null, NoPerm = true,
            Run = x => { var q = pts.Select(p => { var u = x.Map(new Vector3d(p.x, p.y, 0)); return new Vector2d(u.x, u.y); }).ToList();
                var h = new ConvexHull2(q, 1e-9 * x.Scale, QueryNumberType.QT_DOUBLE);
                return h.Dimension + ":" + (h.HullIndices == null ? "no ring" : string.Join(",", h.HullIndices.OrderBy(i => i))); } };
    }

    // #712 MeshRobustRepair: a tetrahedron flattened to 2^-46 of its other extents took 6.5 s, 136 018 wrap triangles. Relations: 2^k scale (size, cost).
    static Program.Case C712()
    {
        double a = -1.1210387714598537E-44, b = 7.888609052210118E-31;
        double[][] v = { new[]{a,0.0,0.0}, new[]{0.0,0,0}, new[]{a,b,0.0}, new[]{a,0.0,b} };
        int[][] t = { new[]{0,2,1}, new[]{0,1,3}, new[]{0,3,2}, new[]{1,2,3} };
        var c = new Program.Case { Issue = "#712", Name = "MeshRobustRepair on a flattened tetrahedron (cost and result)", Scales = Scales(40, 80, 100, 140, 200, 300), Syms = Syms().Take(4).ToArray(),
            Run = x => { var r = new MeshRobustRepair(x.Apply(Mesh(v, t))); bool ok = r.Apply(); return ok + ":" + (r.Mesh == null ? "null" : Program.Sig(r.Mesh, x)); } };
        Func<double, long> timeIt = ax => { var sw = System.Diagnostics.Stopwatch.StartNew(); new MeshRobustRepair(Mesh(new[] { new[]{ax,0.0,0.0}, new[]{0.0,0,0}, new[]{ax,b,0.0}, new[]{ax,0.0,b} }, t)).Apply(); return sw.ElapsedMilliseconds; };
        c.Info.Add(("aniso times ms (flattened, unflattened)", () => { timeIt(-b); return timeIt(a) + ", " + timeIt(-b); }));
        c.Extra.Add(("anisotropic", () => { timeIt(-b); long flat = timeIt(a), full = timeIt(-b); return flat > 300 && flat > 5 * Math.Max(1, full) ? $"flattened {flat} ms vs unflattened {full} ms" : ""; }));
        return c;
    }

    // #719 Exact boolean, Antipodal open-mesh union depends on the coordinate span: one 0 coordinate replaced by 2^-1000.
    static Program.Case C719()
    {
        Func<Vector3d, double, int, DMesh3> sphere = (c, r, ev) => { var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh(); MeshTransforms.Translate(m, c); return m; };
        Func<DMesh3> open = () => { var c = new Vector3d(4.8, 6.4, 8); var m = sphere(c, 11.2, 6); foreach (int t in m.TriangleIndices().ToList()) if ((m.GetTriCentroid(t) - c).Normalized.Dot(new Vector3d(0, 0, 1)) > 0.7) m.RemoveTriangle(t, true, false); return m; };
        Func<DMesh3, int, DMesh3> tiny = (m0, e) => { var m = new DMesh3(m0); int best = -1, ax = 0; double sm = double.MaxValue;
            foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); for (int k = 0; k < 3; ++k) if (Math.Abs(p[k]) < sm) { sm = Math.Abs(p[k]); best = v; ax = k; } }
            var q = m.GetVertex(best); q[ax] = Math.Pow(2, e); m.SetVertex(best, q); return m; };
        Func<DMesh3, DMesh3, string> union = (a, b) => { var s = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true }; if (!s.Compute()) return "failed"; return s.Result.TriangleCount + ":" + MeshMeasurements.AreaT(s.Result, s.Result.TriangleIndices()).ToString("R"); };
        var c719 = new Program.Case { Issue = "#719", Name = "ExactMeshBoolean Antipodal union vs coordinate span", Scales = Scales(-10, 10, 20), Syms = Syms().Take(5).ToArray(),
            Run = x => { var a = x.Apply(sphere(Vector3d.Zero, 16, 5)); var b = x.Apply(open()); var s = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true };
                if (!s.Compute()) return "failed"; return s.Result.TriangleCount + ":" + Program.Sig(s.Result, x).GetHashCode(); } };
        c719.Info.Add(("syms (count:area)", () => string.Join(" ", Syms().Take(5).Prepend(new Program.Xf { Name = "id" }).Select(x => { var s = new ExactMeshBoolean(x.Apply(sphere(Vector3d.Zero, 16, 5)), x.Apply(open()), ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true }; s.Compute();
            return x.Name + "=" + s.Result.TriangleCount + ":" + MeshMeasurements.AreaT(s.Result, s.Result.TriangleIndices()).ToString("R"); }))));
        c719.Extra.Add(("tiny coordinate", () => { string u = union(sphere(Vector3d.Zero, 16, 5), open()), w = union(tiny(sphere(Vector3d.Zero, 16, 5), -1000), open()); return u == w ? "" : $"unit {u} vs 2^-1000 {w}"; }));
        return c719;
    }

    // #710 Exact boolean Difference throws (InCircle filter overflow) on two cubes near 3.7e-223 (MeshFuzzPilot dump). Relation: 2^k scale into the unit range.
    static Program.Case C710()
    {
        double a0 = 3.6741945026526215E-223, a1 = 4.538710856217944E-223;
        double[][] av = { new[]{a0,a0,a0}, new[]{a1,a0,a0}, new[]{a1,a1,a0}, new[]{a0,a1,a0}, new[]{a0,a0,a1}, new[]{a1,a0,a1}, new[]{a1,a1,a1}, new[]{a0,a1,a1} };
        double[][] bv = { new[]{a0,a0,a0}, new[]{a1,a0,a0}, new[]{-1.7669274406332015E-284, 1.766927440712025E-284, -1.767003118109299E-284}, new[]{3.944355863141785E-223, 4.8088722167071076E-223, a0},
            new[]{a0,a0,a1}, new[]{a1,a0,a1}, new[]{a1, 4.700807672511442E-223, 4.700807672511442E-223}, new[]{3.944355863141785E-223, 4.8088722167071076E-223, 4.8088722167071076E-223} };
        int[][] t = { new[]{0,2,1}, new[]{0,3,2}, new[]{4,5,6}, new[]{4,6,7}, new[]{0,1,5}, new[]{0,5,4}, new[]{1,2,6}, new[]{1,6,5}, new[]{2,3,7}, new[]{2,7,6}, new[]{3,0,4}, new[]{3,4,7} };
        var c710 = new Program.Case { Issue = "#710", Name = "ExactMeshBoolean Difference of two cubes at 3.7e-223", Scales = Scales(700, 720, 740, 760), Syms = Syms().Take(6).ToArray(),
            Run = x => { var a = x.Apply(Mesh(av, t)); var b = x.Apply(Mesh(bv, t)); var s = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference);
                if (!s.Compute()) return "failed"; return s.Result.TriangleCount + ":" + Program.Sig(s.Result, x).GetHashCode(); } };
        c710.Info.Add(("triangles per scale", () => string.Join(" ", new[] { 0, 700, 740, 760 }.Select(k => { var x = new Program.Xf { Scale = Math.Pow(2, k) }; try { var s = new ExactMeshBoolean(x.Apply(Mesh(av, t)), x.Apply(Mesh(bv, t)), ExactMeshBoolean.Operations.Difference); return "2^" + k + "=" + (s.Compute() ? s.Result.TriangleCount.ToString() : "failed"); } catch (Exception e) { return "2^" + k + "=throws"; } }))));
        return c710;
    }
}
