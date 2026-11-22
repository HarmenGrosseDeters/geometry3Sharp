// Part "cert": how many cells does a subdivision isotopy certificate need for a thin sheet?
// The cell tests of Plantinga and Vegter (2004), as restated by Burr and Byrd (2023): a cell is accepted if the interval of
// the function excludes 0 (C0) or the interval of <grad f(x), grad f(y)> over x, y in the cell excludes 0 (C1); otherwise it
// is split into eight. Count only: no balancing, no mesh, plain double intervals without outward rounding.
//   single : the sheet as ONE smooth function, F = g^2 - w^2 (grad F = 2 g grad g vanishes on the mid-surface, so C1 fails
//            in every cell that holds both sides of the wall)
//   pair   : the two sides as TWO level sets of the same g, g = +w and g = -w; a cell is accepted if each level is excluded
//            or C1 holds for g. The two sides cannot meet (levels of one function), so no pairwise test is needed.
//   mid    : the mid-surface g = 0 alone (the cost of the same shape without a wall)
using System;
using g3;

static class CertProbe
{
    struct Iv
    {
        public double Lo, Hi;
        public Iv(double lo, double hi) { Lo = lo; Hi = hi; }
        public bool HasZero { get { return Lo <= 0 && Hi >= 0; } }
        public static Iv operator +(Iv a, Iv b) { return new Iv(a.Lo + b.Lo, a.Hi + b.Hi); }
        public static Iv operator -(Iv a, Iv b) { return new Iv(a.Lo - b.Hi, a.Hi - b.Lo); }
        public static Iv operator -(Iv a, double b) { return new Iv(a.Lo - b, a.Hi - b); }
        public static Iv operator *(Iv a, Iv b)
        {
            double p = a.Lo * b.Lo, q = a.Lo * b.Hi, r = a.Hi * b.Lo, s = a.Hi * b.Hi;
            return new Iv(Math.Min(Math.Min(p, q), Math.Min(r, s)), Math.Max(Math.Max(p, q), Math.Max(r, s)));
        }
        public static Iv Sqr(Iv a) { Iv m = a * a; return a.HasZero ? new Iv(0, m.Hi) : m; }
        public static Iv Sin(Iv a)
        {
            if (a.Hi - a.Lo >= 2 * Math.PI) return new Iv(-1, 1);
            double lo = Math.Min(Math.Sin(a.Lo), Math.Sin(a.Hi)), hi = Math.Max(Math.Sin(a.Lo), Math.Sin(a.Hi));
            if (Math.Floor((a.Hi - Math.PI / 2) / (2 * Math.PI)) > Math.Floor((a.Lo - Math.PI / 2) / (2 * Math.PI))) hi = 1;       // a maximum inside
            if (Math.Floor((a.Hi + Math.PI / 2) / (2 * Math.PI)) > Math.Floor((a.Lo + Math.PI / 2) / (2 * Math.PI))) lo = -1;      // a minimum inside
            return new Iv(lo, hi);
        }
        public static Iv Cos(Iv a) { return Sin(new Iv(a.Lo + Math.PI / 2, a.Hi + Math.PI / 2)); }
    }

    // g and its gradient over a box
    delegate void Field(Iv x, Iv y, Iv z, out Iv g, out Iv gx, out Iv gy, out Iv gz);

    static Field Gyroid(double k)
    {
        return (Iv x, Iv y, Iv z, out Iv g, out Iv gx, out Iv gy, out Iv gz) => {
            Iv kx = new Iv(k * x.Lo, k * x.Hi), ky = new Iv(k * y.Lo, k * y.Hi), kz = new Iv(k * z.Lo, k * z.Hi);
            Iv sx = Iv.Sin(kx), cx = Iv.Cos(kx), sy = Iv.Sin(ky), cy = Iv.Cos(ky), sz = Iv.Sin(kz), cz = Iv.Cos(kz);
            g = sx * cy + sy * cz + sz * cx;          // in units of g; the gradient below is grad g / k
            gx = cx * cy - sz * sx; gy = cy * cz - sx * sy; gz = cz * cx - sy * sz;
        };
    }

    // g = |p| - R as r^2 would change the wall's meaning; the distance to a sphere, away from the centre
    static Field Sphere(double R)
    {
        return (Iv x, Iv y, Iv z, out Iv g, out Iv gx, out Iv gy, out Iv gz) => {
            Iv q = Iv.Sqr(x) + Iv.Sqr(y) + Iv.Sqr(z);
            Iv r = new Iv(Math.Sqrt(q.Lo), Math.Sqrt(q.Hi));
            g = r - R;
            if (r.Lo <= 0) { gx = gy = gz = new Iv(-1, 1); return; }
            Iv inv = new Iv(1 / r.Hi, 1 / r.Lo);
            gx = x * inv; gy = y * inv; gz = z * inv;
        };
    }

    sealed class Counter
    {
        public Field F; public double W; public string Mode; public int MaxDepth;
        public long Leaves, ByC0, ByC1, Unresolved; public int Deepest;
        public long[] C1AtDepth = new long[32];

        bool Accept(Iv x, Iv y, Iv z, out bool byC0)
        {
            Iv g, gx, gy, gz;
            F(x, y, z, out g, out gx, out gy, out gz);
            byC0 = false;
            if (Mode == "single") {
                Iv f = Iv.Sqr(g) - W * W;
                if (!f.HasZero) { byC0 = true; return true; }
                Iv fx = g * gx, fy = g * gy, fz = g * gz;       // grad F / 2
                return !(fx * fx + fy * fy + fz * fz).HasZero;
            }
            bool c0 = Mode == "mid" ? !g.HasZero : !(g - W).HasZero && !(g - (-W)).HasZero;
            if (c0) { byC0 = true; return true; }
            return !(gx * gx + gy * gy + gz * gz).HasZero;
        }

        public void Visit(Iv x, Iv y, Iv z, int depth)
        {
            bool byC0;
            if (Accept(x, y, z, out byC0)) {
                ++Leaves; if (byC0) ++ByC0; else { ++ByC1; ++C1AtDepth[depth]; }
                Deepest = Math.Max(Deepest, depth);
                return;
            }
            if (depth == MaxDepth) { ++Leaves; ++Unresolved; Deepest = depth; return; }
            double mx = 0.5 * (x.Lo + x.Hi), my = 0.5 * (y.Lo + y.Hi), mz = 0.5 * (z.Lo + z.Hi);
            for (int c = 0; c < 8; ++c)
                Visit((c & 1) == 0 ? new Iv(x.Lo, mx) : new Iv(mx, x.Hi), (c & 2) == 0 ? new Iv(y.Lo, my) : new Iv(my, y.Hi), (c & 4) == 0 ? new Iv(z.Lo, mz) : new Iv(mz, z.Hi), depth + 1);
        }
    }

    public static void Run()
    {
        const int res = 32, maxDepth = 10;
        double h = 2.0 / res;
        Console.WriteLine("# cert: octree over [-1, 1]^3 (depth 5 = the 32^3 grid of part thin, cell 1 at depth 5), split until C0 or C1 holds, depth cap " + maxDepth);
        Console.WriteLine("shape, k, mode, leaves, by C0, by C1, unresolved at cap, deepest, finest C1 cell (in grid cells), modal C1 depth");
        foreach (string shape in new[] { "gyroid", "shell" })
            foreach (double k in new[] { 0.25, 0.5, 1.0, 2.0 })
                foreach (string mode in new[] { "mid", "single", "pair" }) {
                    if (mode == "mid" && k != 0.25) continue;
                    double kappa = 2 * Math.PI / (16 * h);
                    // gyroid: g in its own units, wall |g| / kappa <= t / 2 as in part thin; shell: |r - 0.7| <= t / 2
                    var c = new Counter { F = shape == "gyroid" ? Gyroid(kappa) : Sphere(0.7), W = shape == "gyroid" ? kappa * k * h / 2 : k * h / 2, Mode = mode, MaxDepth = maxDepth };
                    // a slightly off-centre root box, so no cell face lies in a symmetry plane of the field
                    c.Visit(new Iv(-1.013, 0.987), new Iv(-1.007, 0.993), new Iv(-1.003, 0.997), 0);
                    int modal = 0;
                    for (int d = 0; d < 32; ++d) if (c.C1AtDepth[d] > c.C1AtDepth[modal]) modal = d;
                    Console.WriteLine(string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}", shape, k, mode, c.Leaves, c.ByC0, c.ByC1, c.Unresolved, c.Deepest,
                        Math.Pow(2, 5 - c.Deepest), modal));
                }
    }
}
