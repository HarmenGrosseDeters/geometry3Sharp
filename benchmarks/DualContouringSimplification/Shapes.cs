using System;
using g3;

// Signed distance style fields (negative inside) of the benchmark shapes; composed with min / max, so |f| is the distance
// near the surface only where one primitive is nearest.
static class Shapes
{
    public static readonly string[] Names = { "csg", "bracket", "sphere", "torus" };

    /// half extent of the cube [-e, e]^3 that contains the shape with a margin
    public static double Extent(string name) { switch (name) { case "csg": return 1.5; case "bracket": return 1.3; case "sphere": return 1.1; default: return 1.5; } }

    public static Func<Vector3d, double> Get(string name)
    {
        switch (name) {
            case "csg": return Csg;
            case "bracket": return Bracket;
            case "sphere": return (p) => p.Length - 1.0;
            case "torus": return (p) => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1.0; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        }
        throw new ArgumentException(name);
    }

    static double Box(Vector3d p, Vector3d c, Vector3d he)
    {
        Vector3d q = new Vector3d(Math.Abs(p.x - c.x) - he.x, Math.Abs(p.y - c.y) - he.y, Math.Abs(p.z - c.z) - he.z);
        Vector3d o = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
        return o.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
    }
    static double CylZ(double a, double b, double r) { return Math.Sqrt(a * a + b * b) - r; }

    // box half extent 1 intersected with a sphere of radius 1.35, minus three axis cylinders of radius 0.55
    static double Csg(Vector3d p)
    {
        double f = Math.Max(Box(p, Vector3d.Zero, new Vector3d(1, 1, 1)), p.Length - 1.35);
        double cx = CylZ(p.y, p.z, 0.55), cy = CylZ(p.x, p.z, 0.55), cz = CylZ(p.x, p.y, 0.55);
        return Math.Max(f, -Math.Min(cx, Math.Min(cy, cz)));
    }

    // L bracket (two plates) with a flat-ended boss, a through hole, a slot and a chamfered corner
    static double Bracket(Vector3d p)
    {
        double f = Math.Min(Box(p, new Vector3d(0, -0.7, 0), new Vector3d(1, 0.3, 0.5)), Box(p, new Vector3d(-0.7, 0.3, 0), new Vector3d(0.3, 1, 0.5)));
        f = Math.Min(f, Math.Max(CylZ(p.x - 0.5, p.y + 0.7, 0.22), Math.Max(p.z - 0.9, 0.4 - p.z)));
        f = Math.Max(f, -CylZ(p.x, p.y + 0.7, 0.12));
        f = Math.Max(f, -Box(p, new Vector3d(-0.7, 0.6, 0), new Vector3d(0.08, 0.25, 1)));
        f = Math.Max(f, (p.x - p.y - 1.55) / Math.Sqrt(2));
        return f;
    }
}
