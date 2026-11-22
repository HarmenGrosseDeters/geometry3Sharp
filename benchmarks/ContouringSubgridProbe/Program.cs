// Upper-bound probe for the SIGGRAPH 2026 contouring upgrades (docs/research/CONTOURING_SDF_SUBGRID_2026-11.md).
// Measurement only, public API only, no library change. Three parts:
//   sharp : DualContouring from a discrete SDF grid; grid-only Hermite data against an oracle (exact crossing and normal)
//   thin  : walls thinner than a cell; MarchingCubes / DualContouring against a restricted subgrid marching tetrahedra
//   cert  : cells of a Plantinga-Vegter subdivision for a thin sheet, as one field and as a pair of level sets
//   cull  : evaluations of a coarse-first octree build from a Lipschitz bound against the dense grid (plan #1276)
using System;
using System.Globalization;
using g3;

static class Program
{
    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = args.Length > 0 ? args[0] : "all";
        int seeds = args.Length > 1 ? int.Parse(args[1]) : 3;
        if (mode == "sharp" || mode == "all") SharpProbe.Run(seeds, new[] { 24, 48 });
        if (mode == "thin" || mode == "all") ThinProbe.Run(seeds, 32);
        if (mode == "cert" || mode == "all") CertProbe.Run();
        if (mode == "cull") CullProbe.Run(seeds);      // the second argument is the largest N of the dense DualContouring run
        return 0;
    }

    // a rotation and a grid shift per seed (seed 0: rotated as well, no input is axis-aligned)
    public static Matrix3d Rotation(int seed)
    {
        var rnd = new Random(1000 + seed);
        Vector3d axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
        return Matrix3d.AxisAngleD(axis, 20 + 50 * rnd.NextDouble());
    }
    public static Vector3d Shift(int seed)
    {
        var rnd = new Random(2000 + seed);
        return new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
    }
}

sealed class FuncImplicit : ImplicitFunction3d
{
    readonly Func<Vector3d, double> f;
    public long Evaluations;
    public FuncImplicit(Func<Vector3d, double> f) { this.f = f; }
    public double Value(ref Vector3d p) { System.Threading.Interlocked.Increment(ref Evaluations); return f(p); }
}
