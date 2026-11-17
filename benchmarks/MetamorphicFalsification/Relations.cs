// The relations A1 (anisotropic scale) and P1 (tiny perturbation) of docs/research/METAMORPHIC_TESTING.md, run on the repros of
// #712 (A1) and #719 (P1): `dotnet run --project benchmarks/MetamorphicFalsification -c Release -- relations`. The helpers are the
// test file tests/geometry3Sharp.Tests/Harness/MetamorphicRelations.cs (linked, base API only), so the same code runs on any
// commit: the validation of the relations is that A1 fires on the parent of the #712 fix, P1 on the parent of #719, and neither on
// master (results/relations.txt).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using geometry3Sharp.Tests;
using gs;

static class Relations
{
    static DMesh3 Mesh(double[][] v, int[][] t)
    {
        var m = new DMesh3();
        foreach (var p in v) m.AppendVertex(new Vector3d(p[0], p[1], p[2]));
        foreach (var x in t) m.AppendTriangle(x[0], x[1], x[2]);
        return m;
    }

    static void Report(string name, string violation, string detail)
    {
        Console.WriteLine($"  {name,-44} {(violation == "" ? "not caught" : "CAUGHT")} {detail}{(violation == "" ? "" : " | " + violation)}");
    }

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "THROWS " + e.GetType().Name + ": " + (e.Message.Length > 60 ? e.Message.Substring(0, 60) : e.Message); }
    }

    public static int Run()
    {
        Console.WriteLine($"== relations on {Environment.Version} (A1 and P1 of docs/research/METAMORPHIC_TESTING.md, section 5)");

        // ---- A1 on #712: the repro (the tetrahedron flattened to 2^-46 of its other extents) against the same tetrahedron unflattened
        // (its x extent equal to the other extents, the reference of falsification run section 3). Cost: the wrap triangles of the repair.
        double a = -1.1210387714598537E-44, b = 7.888609052210118E-31;
        int[][] t712 = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 0, 3, 2 }, new[] { 1, 2, 3 } };
        double[][] flat712 = { new[] { a, 0.0, 0.0 }, new[] { 0.0, 0, 0 }, new[] { a, b, 0.0 }, new[] { a, 0.0, b } };
        double[][] unflat712 = { new[] { -b, 0.0, 0.0 }, new[] { 0.0, 0, 0 }, new[] { -b, b, 0.0 }, new[] { -b, 0.0, b } };
        Func<double[][], (int wrap, int tris, string clean)> repair = v => {
            var r = new MeshRobustRepair(Mesh(v, t712));
            bool ok = r.Apply();
            return (r.AlphaWrapTriangles, r.Mesh.TriangleCount, ok ? MeshRobustRepair.NotCleanReason(r.Mesh) : "Apply() false: " + r.ResultReason);
        };
        string a1Issue = Try(() => {
            var ref0 = repair(unflat712); var fl = repair(flat712);
            string v = MetamorphicRelations.CountBound("wrap triangles", ref0.wrap, fl.wrap, 5000) + (fl.clean == "" ? "" : "unclean: " + fl.clean);
            return v == "" ? "" : v;
        });
        string a1IssueDetail = Try(() => { var ref0 = repair(unflat712); var fl = repair(flat712); return $"wrap {ref0.wrap} vs {fl.wrap}, result {ref0.tris} vs {fl.tris} triangles"; });
        Report("A1 #712 repair (flattened vs unflattened)", a1Issue, a1IssueDetail);

        // ---- A1 generic: the unit tetrahedron, one axis flattened by 2^-46 (exact)
        int[][] t = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 0, 3, 2 }, new[] { 1, 2, 3 } };
        double[][] unit = { new[] { 0.0, 0, 0 }, new[] { 1.0, 0, 0 }, new[] { 0.0, 1, 0 }, new[] { 0.0, 0, 1 } };
        Func<DMesh3, (int wrap, int tris, string clean)> repairM = m => {
            var r = new MeshRobustRepair(m);
            bool ok = r.Apply();
            return (r.AlphaWrapTriangles, r.Mesh.TriangleCount, ok ? MeshRobustRepair.NotCleanReason(r.Mesh) : "Apply() false: " + r.ResultReason);
        };
        foreach (int e in new[] { -MetamorphicRelations.AnisotropicLimit, MetamorphicRelations.AnisotropicLimit }) {
            int ee = e;
            string v = Try(() => {
                var ref0 = repairM(Mesh(unit, t)); var fl = repairM(MetamorphicRelations.Stretch(Mesh(unit, t), 0, ee));
                return MetamorphicRelations.CountBound("wrap triangles", ref0.wrap, fl.wrap, 5000) + (fl.clean == "" ? "" : "unclean: " + fl.clean);
            });
            string d = Try(() => { var ref0 = repairM(Mesh(unit, t)); var fl = repairM(MetamorphicRelations.Stretch(Mesh(unit, t), 0, ee)); return $"wrap {ref0.wrap} vs {fl.wrap}"; });
            Report($"A1 unit tetrahedron, x by 2^{e}", v, d);
        }

        // ---- P1 on #719: the open-mesh Antipodal union, the smallest coordinate of the closed sphere set to 2^e
        DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 16, EdgeVertices = 5 }.Generate().MakeDMesh();
        DMesh3 open = new Sphere3Generator_NormalizedCube() { Radius = 11.2, EdgeVertices = 6 }.Generate().MakeDMesh();
        Vector3d c = new Vector3d(4.8, 6.4, 8);
        MeshTransforms.Translate(open, c);
        foreach (int tr in open.TriangleIndices().ToList())
            if ((open.GetTriCentroid(tr) - c).Normalized.Dot(new Vector3d(0, 0, 1)) > 0.7) open.RemoveTriangle(tr, true, false);
        Func<DMesh3, (int tris, double area)> union = s => {
            var u = new ExactMeshBoolean(s, open, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true };
            if (!u.Compute()) throw new InvalidOperationException("the union did not compute");
            return (u.Result.TriangleCount, MeshMeasurements.AreaT(u.Result, u.Result.TriangleIndices()));
        };
        var unitU = Try(() => { var r = union(sphere); return r.tris + ":" + r.area.ToString("R"); });
        foreach (int e in new[] { -500, -1000 }) {
            int ee = e;
            string v = Try(() => {
                var u0 = union(sphere);
                var u1 = union(MetamorphicRelations.SmallestCoordinate(sphere, ee));
                string c1 = u0.tris == u1.tris ? "" : $"triangles {u0.tris} vs {u1.tris}";
                string c2 = MetamorphicRelations.ValueBound("area", u0.area, u1.area, 1e-9);
                return c1 + (c1 != "" && c2 != "" ? "; " : "") + c2;
            });
            string d = Try(() => { var u0 = union(sphere); var u1 = union(MetamorphicRelations.SmallestCoordinate(sphere, ee)); return $"unit {u0.tris}:{u0.area:R} vs 2^{e} {u1.tris}:{u1.area:R}"; });
            Report($"P1 #719 open union, smallest coord 2^{e}", v, d);
        }

        // ---- P1 on the classifiers (FastWindingNumber, IsInside) of the closed sphere, the same shift
        DMesh3 s0 = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
        var rnd = new Random(7);
        var pts = Enumerable.Range(0, 200).Select(i => new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1).Normalized * (i % 2 == 0 ? 0.4 : 1.8)).ToList();
        foreach (int e in new[] { -500, -1000 }) {
            int ee = e;
            string w = Try(() => {
                var tree = new DMeshAABBTree3(s0, true); var treeP = new DMeshAABBTree3(MetamorphicRelations.SmallestCoordinate(s0, ee), true);
                foreach (var q in pts) {
                    string v = MetamorphicRelations.ValueBound("winding", tree.WindingNumber(q), treeP.WindingNumber(q), 1e-9);
                    if (v != "") return v;
                }
                return "";
            });
            Report($"P1 WindingNumber, smallest coord 2^{e}", w, "200 points");
            string i = Try(() => {
                var tree = new DMeshAABBTree3(s0, true); var treeP = new DMeshAABBTree3(MetamorphicRelations.SmallestCoordinate(s0, ee), true);
                return string.Join(",", pts.Where(q => tree.IsInside(q) != treeP.IsInside(q)).Select(q => q.ToString()));
            });
            Report($"P1 IsInside, smallest coord 2^{e}", i, "200 points");
        }
        Console.WriteLine($"  (unit #719 union: {unitU})");
        return 0;
    }
}
