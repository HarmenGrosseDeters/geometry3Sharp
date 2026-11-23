using System; using System.Collections.Generic; using System.Linq; using g3; using gs;
// Diagnosis of the three relations that fire on master (docs/research/METAMORPHIC_TESTING.md, section 3.1): #712 repair under
// symmetry / permutation, #710 Boolean under 2^k scale and symmetry, each on the input of the issue and on an in-range variant.
static class Diagnose
{
    static DMesh3 Mesh(double[][] v, int[][] t) { var m = new DMesh3(); foreach (var p in v) m.AppendVertex(new Vector3d(p[0], p[1], p[2])); foreach (var x in t) m.AppendTriangle(x[0], x[1], x[2]); return m; }
    public static void Scan()
    {
        double a0 = 3.6741945026526215E-223, a1 = 4.538710856217944E-223;
        int[][] t = { new[]{0,2,1}, new[]{0,3,2}, new[]{4,5,6}, new[]{4,6,7}, new[]{0,1,5}, new[]{0,5,4}, new[]{1,2,6}, new[]{1,6,5}, new[]{2,3,7}, new[]{2,7,6}, new[]{3,0,4}, new[]{3,4,7} };
        double[] corner = { -a0 * Math.Pow(2, -30), a0 * Math.Pow(2, -30), -a0 * Math.Pow(2, -30) };
        double[][] av = { new[]{a0,a0,a0}, new[]{a1,a0,a0}, new[]{a1,a1,a0}, new[]{a0,a1,a0}, new[]{a0,a0,a1}, new[]{a1,a0,a1}, new[]{a1,a1,a1}, new[]{a0,a1,a1} };
        double[][] bv = { new[]{a0,a0,a0}, new[]{a1,a0,a0}, corner, new[]{3.944355863141785E-223, 4.8088722167071076E-223, a0}, new[]{a0,a0,a1}, new[]{a1,a0,a1}, new[]{a1, 4.700807672511442E-223, 4.700807672511442E-223}, new[]{3.944355863141785E-223, 4.8088722167071076E-223, 4.8088722167071076E-223} };
        string refSig = null; int n = 0, same = 0;
        for (int k = 600; k <= 800; k += 4) {
            var x = new Program.Xf { Scale = Math.Pow(2, k) };
            var s = new ExactMeshBoolean(x.Apply(Mesh(av, t)), x.Apply(Mesh(bv, t)), ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true }; bool ok = s.Compute();
            string sg = ok ? Program.Sig(s.Result, x) : "failed"; refSig ??= sg; ++n; if (sg == refSig) ++same;
        }
        Console.WriteLine($"#710 in-range variant (corner 2^-30): scales 2^600 .. 2^800 step 4 (cube from 2^-139 to 2^61): {same} of {n} bit-identical to 2^600");
    }
    public static void Run()
    {
        Scan();
        // ---- #712 repair on master: flattened and unflattened tetrahedron, per transform
        double a = -1.1210387714598537E-44, b = 7.888609052210118E-31;
        int[][] tt = { new[]{0,2,1}, new[]{0,1,3}, new[]{0,3,2}, new[]{1,2,3} };
        foreach (var (name, ax) in new[] { ("flat", a), ("unflat", -b) }) {
            double[][] v = { new[]{ax,0.0,0.0}, new[]{0.0,0,0}, new[]{ax,b,0.0}, new[]{ax,0.0,b} };
            string idSig = null;
            foreach (var x in new[] { new Program.Xf{Name="id"}, new Program.Xf{Name="perm",Perm=true}, new Program.Xf{Name="sym1",Sym=1}, new Program.Xf{Name="sym3",Sym=3}, new Program.Xf{Name="sym6",Sym=6}, new Program.Xf{Name="sym7",Sym=7} }) {
                var r = new MeshRobustRepair(x.Apply(Mesh(v, tt))); bool ok = r.Apply();
                string sg = ok && r.Mesh != null ? Program.Sig(r.Mesh, x) : "none"; if (x.Name == "id") idSig = sg;
                Console.WriteLine($"#712 {name} {x.Name,-5} ok={ok} tris={r.Mesh?.TriangleCount} clean='{(r.Mesh == null ? "" : MeshRobustRepair.NotCleanReasons(r.Mesh))}' wrap={r.UsedAlphaWrap} solid={r.UsedSolidify} exact={r.UsedExactResolve} wrapTris={r.AlphaWrapTriangles} fallback='{r.FallbackReason}' sameAsId={sg == idSig} vol={(r.Mesh == null ? 0 : r.Mesh.TriangleIndices().Sum(q => { var tr = r.Mesh.GetTriangle(q); return r.Mesh.GetVertex(tr.a).Dot(r.Mesh.GetVertex(tr.b).Cross(r.Mesh.GetVertex(tr.c))) / 6; })):R}");
            }
        }
        // ---- #710 on master: scale family and symmetry, original input and an in-range variant (corner at 2^-30 of the cube instead of 2^-200)
        double a0 = 3.6741945026526215E-223, a1 = 4.538710856217944E-223;
        int[][] t = { new[]{0,2,1}, new[]{0,3,2}, new[]{4,5,6}, new[]{4,6,7}, new[]{0,1,5}, new[]{0,5,4}, new[]{1,2,6}, new[]{1,6,5}, new[]{2,3,7}, new[]{2,7,6}, new[]{3,0,4}, new[]{3,4,7} };
        foreach (var (name, corner) in new[] { ("orig corner 1.8e-284", new[]{-1.7669274406332015E-284, 1.766927440712025E-284, -1.767003118109299E-284}), ("variant corner 2^-30 of a0", new[]{-a0*Math.Pow(2,-30), a0*Math.Pow(2,-30), -a0*Math.Pow(2,-30)}) }) {
            double[][] av = { new[]{a0,a0,a0}, new[]{a1,a0,a0}, new[]{a1,a1,a0}, new[]{a0,a1,a0}, new[]{a0,a0,a1}, new[]{a1,a0,a1}, new[]{a1,a1,a1}, new[]{a0,a1,a1} };
            double[][] bv = { new[]{a0,a0,a0}, new[]{a1,a0,a0}, corner, new[]{3.944355863141785E-223, 4.8088722167071076E-223, a0}, new[]{a0,a0,a1}, new[]{a1,a0,a1}, new[]{a1, 4.700807672511442E-223, 4.700807672511442E-223}, new[]{3.944355863141785E-223, 4.8088722167071076E-223, 4.8088722167071076E-223} };
            var sigs = new Dictionary<string, string>();
            { var x0 = new Program.Xf { Scale = Math.Pow(2, 740) }; var s0 = new ExactMeshBoolean(x0.Apply(Mesh(av, t)), x0.Apply(Mesh(bv, t)), ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true }; s0.Compute(); sigs["2^740 sym0"] = Program.Sig(s0.Result, x0); }
            foreach (int k in new[] { 0, 700, 740, 760 })
                foreach (var sy in new[] { 0, 1, 7, 3 }) {
                    var x = new Program.Xf { Name = $"2^{k} sym{sy}", Scale = Math.Pow(2, k), Sym = sy };
                    var s = new ExactMeshBoolean(x.Apply(Mesh(av, t)), x.Apply(Mesh(bv, t)), ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true }; bool ok = s.Compute();
                    string sg = ok ? Program.Sig(s.Result, x) : "failed"; sigs[x.Name] = sg;
                    Console.WriteLine($"#710 {name,-27} {x.Name,-14} ok={ok} tris={s.Result?.TriangleCount} outsideRange={(s.Counts.TryGetValue("rounding_outside_range", out var o) ? o : -1)} roundingClean={s.RoundingClean} sameAs[2^740 sym0]={sg == sigs.GetValueOrDefault("2^740 sym0")}");
                }
        }
    }
}
