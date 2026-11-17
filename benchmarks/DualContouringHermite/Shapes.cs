using System;
using g3;

/// <summary> Procedural test shapes as signed fields (negative inside, InsideIsLess). Max/min of exact SDFs: a true SDF only near the faces, which is all the contouring reads. </summary>
public sealed class Shape : ImplicitFunction3d
{
    public string Name;
    public Func<Vector3d, double> F;
    public AxisAlignedBox3d Bounds;
    public double Value(ref Vector3d p) { return F(p); }

    static double Box(Vector3d p, Vector3d c, Vector3d half)
    {
        Vector3d q = new Vector3d(Math.Abs(p.x - c.x) - half.x, Math.Abs(p.y - c.y) - half.y, Math.Abs(p.z - c.z) - half.z);
        Vector3d o = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
        return o.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
    }
    // infinite cylinder around an axis through c
    static double Cyl(Vector3d p, Vector3d c, int axis, double r)
    {
        double a = p[(axis + 1) % 3] - c[(axis + 1) % 3], b = p[(axis + 2) % 3] - c[(axis + 2) % 3];
        return Math.Sqrt(a * a + b * b) - r;
    }

    /// <summary> box ∩ sphere minus three orthogonal cylinders </summary>
    public static Shape Csg()
    {
        return new Shape() {
            Name = "csg",
            Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1), new Vector3d(1.1, 1.1, 1.1)),
            F = p => {
                double d = Math.Max(Box(p, Vector3d.Zero, new Vector3d(0.8, 0.8, 0.8)), p.Length - 1.0);
                d = Math.Max(d, -Cyl(p, Vector3d.Zero, 0, 0.3));
                d = Math.Max(d, -Cyl(p, Vector3d.Zero, 1, 0.3));
                d = Math.Max(d, -Cyl(p, Vector3d.Zero, 2, 0.3));
                return d;
            }
        };
    }

    /// <summary> base plate and upright plate (boxes), a boss, a hole, a slot and a 45 degree chamfer </summary>
    public static Shape Bracket()
    {
        return new Shape() {
            Name = "bracket",
            Bounds = new AxisAlignedBox3d(new Vector3d(-1.2, -0.8, -0.5), new Vector3d(1.2, 0.8, 1.1)),
            F = p => {
                double d = Box(p, new Vector3d(0, 0, 0), new Vector3d(1.0, 0.6, 0.15));
                d = Math.Min(d, Box(p, new Vector3d(-0.85, 0, 0.55), new Vector3d(0.15, 0.6, 0.55)));
                // boss on the base plate
                d = Math.Min(d, Math.Max(Cyl(p, new Vector3d(0.45, 0, 0), 2, 0.3), Math.Abs(p.z - 0.2) - 0.2));
                // hole through the boss and the plate
                d = Math.Max(d, -Cyl(p, new Vector3d(0.45, 0, 0), 2, 0.13));
                // slot in the base plate
                d = Math.Max(d, -Box(p, new Vector3d(-0.2, 0, 0), new Vector3d(0.1, 0.35, 1)));
                // 45 degree chamfer of the top edge of the upright (half-space z - x < 1.85)
                d = Math.Max(d, (p.z - p.x - 1.85) / Math.Sqrt(2) * 1.0);
                return d;
            }
        };
    }

    public static Shape Sphere()
    {
        return new Shape() {
            Name = "sphere", Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1), new Vector3d(1.1, 1.1, 1.1)),
            F = p => p.Length - 0.9
        };
    }

    public static Shape Torus()
    {
        return new Shape() {
            Name = "torus", Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -0.5), new Vector3d(1.1, 1.1, 0.5)),
            F = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.7; return Math.Sqrt(q * q + p.z * p.z) - 0.25; }
        };
    }

    public static Shape[] All() { return new[] { Csg(), Bracket(), Sphere(), Torus() }; }
}
