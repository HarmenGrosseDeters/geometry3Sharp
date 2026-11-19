// Spike (docs/design/TET_MESHING.md section 17, P4(a)): shared helpers. Not library code.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class S
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string F(double x) => x.ToString("G5", Inv);

    // procedural inputs with concave detail (exact unions of boxes) and plain references
    static DMesh3 Box(Vector3d min, Vector3d max) =>
        new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
    // exact union: no buried internal faces (a soup of touching boxes has them, and their distance to the boundary is not detail)
    static DMesh3 Soup(params DMesh3[] parts) => ExactMeshBoolean.Union(parts) ?? throw new Exception("union failed");
    public static IEnumerable<KeyValuePair<string, DMesh3>> Procedural()
    {
        // comb: a base plate and 12 teeth, the gaps 0.03 wide (about diagonal / 40 at diagonal 1.2)
        var comb = new List<DMesh3> { Box(new Vector3d(0, 0, 0), new Vector3d(1.0, 0.2, 0.2)) };
        for (int i = 0; i < 12; ++i) {
            double x0 = 0.01 + i * 0.0825;
            comb.Add(Box(new Vector3d(x0, 0.15, 0), new Vector3d(x0 + 0.0525, 0.6, 0.2)));
        }
        yield return new KeyValuePair<string, DMesh3>("comb", Soup(comb.ToArray()));
        // U channel: two walls and a floor, slot 0.05 wide and 0.4 deep
        yield return new KeyValuePair<string, DMesh3>("u_channel", Soup(
            Box(new Vector3d(0, 0, 0), new Vector3d(0.5, 0.1, 0.5)),
            Box(new Vector3d(0, 0.05, 0), new Vector3d(0.225, 0.5, 0.5)),
            Box(new Vector3d(0.275, 0.05, 0), new Vector3d(0.5, 0.5, 0.5))));
        // L bracket: one concave 90 degree crease, nothing narrow
        yield return new KeyValuePair<string, DMesh3>("l_bracket", Soup(
            Box(new Vector3d(0, 0, 0), new Vector3d(1, 0.3, 0.5)),
            Box(new Vector3d(0, 0.2, 0), new Vector3d(0.3, 1, 0.5))));
        // two plates with a thin gap (0.025) between them, joined at one end
        yield return new KeyValuePair<string, DMesh3>("plates_gap", Soup(
            Box(new Vector3d(0, 0, 0), new Vector3d(1, 0.2, 0.6)),
            Box(new Vector3d(0, 0.225, 0), new Vector3d(1, 0.425, 0.6)),
            Box(new Vector3d(0, 0.15, 0), new Vector3d(0.15, 0.275, 0.6))));
        yield return new KeyValuePair<string, DMesh3>("sphere", new Sphere3Generator_NormalizedCube() { Radius = 0.5, EdgeVertices = 24 }.Generate().MakeDMesh());
        yield return new KeyValuePair<string, DMesh3>("box", Box(new Vector3d(0, 0, 0), new Vector3d(1, 0.6, 0.4)));
        yield return new KeyValuePair<string, DMesh3>("thin_plate", Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0.02)));
    }

    // area-weighted samples of the input surface, deterministic
    public static void Samples(DMesh3 m, int n, out Vector3d[] pos, out Vector3d[] nrm, out int[] tid)
    {
        var tris = m.TriangleIndices().ToArray();
        var cum = new double[tris.Length];
        double acc = 0;
        for (int k = 0; k < tris.Length; ++k) { acc += m.GetTriArea(tris[k]); cum[k] = acc; }
        pos = new Vector3d[n]; nrm = new Vector3d[n]; tid = new int[n];
        ulong s = 0x9E3779B97F4A7C15UL;
        Func<double> rnd = () => { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); };
        for (int i = 0; i < n; ++i) {
            double u = rnd() * acc;
            int lo = 0, hi = tris.Length - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < u) lo = mid + 1; else hi = mid; }
            int t = tris[lo];
            m.GetTriVertices(t, ref a, ref b, ref c);
            double r1 = Math.Sqrt(rnd()), r2 = rnd();
            pos[i] = (1 - r1) * a + r1 * (1 - r2) * b + r1 * r2 * c;
            nrm[i] = m.GetTriNormal(t);
            tid[i] = t;
        }
    }
    static Vector3d a, b, c;

    // distance of the samples to a surface (exact, AABB tree)
    public static double[] Dist(DMesh3 surface, Vector3d[] pos)
    {
        var tree = new DMeshAABBTree3(surface, true);
        var d = new double[pos.Length];
        System.Threading.Tasks.Parallel.For(0, pos.Length, i => {
            int t = tree.FindNearestTriangle(pos[i], out double d2);
            d[i] = t < 0 ? double.PositiveInfinity : Math.Sqrt(d2);
        });
        return d;
    }

    public static double Quantile(double[] v, double q)
    {
        var s = (double[])v.Clone(); Array.Sort(s);
        if (s.Length == 0) return double.NaN;
        double x = q * (s.Length - 1); int i = (int)x; double f = x - i;
        return i + 1 < s.Length ? s[i] * (1 - f) + s[i + 1] * f : s[i];
    }

    // uniform grid of points for ball queries
    public sealed class Grid
    {
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly Vector3d[] p; readonly double h;
        public Grid(Vector3d[] pts, double cell) { p = pts; h = cell; for (int i = 0; i < pts.Length; ++i) { var k = Key(pts[i]); if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>(); l.Add(i); } }
        static long Pack(int x, int y, int z) => ((long)(x + 1048576) << 42) | ((long)(y + 1048576) << 21) | (long)(z + 1048576);
        long Key(Vector3d q) => Pack((int)Math.Floor(q.x / h), (int)Math.Floor(q.y / h), (int)Math.Floor(q.z / h));
        public void InBall(Vector3d q, double r, List<int> res)
        {
            res.Clear();
            int x0 = (int)Math.Floor((q.x - r) / h), x1 = (int)Math.Floor((q.x + r) / h), y0 = (int)Math.Floor((q.y - r) / h), y1 = (int)Math.Floor((q.y + r) / h), z0 = (int)Math.Floor((q.z - r) / h), z1 = (int)Math.Floor((q.z + r) / h);
            for (int x = x0; x <= x1; ++x) for (int y = y0; y <= y1; ++y) for (int z = z0; z <= z1; ++z)
                if (cells.TryGetValue(Pack(x, y, z), out var l)) foreach (int i in l) if ((p[i] - q).LengthSquared <= r * r) res.Add(i);
        }
        public int Nearest(Vector3d q, double maxR)
        {
            var l = new List<int>(); double r = h;
            while (true) {
                InBall(q, r, l);
                if (l.Count > 0) { int best = -1; double bd = double.MaxValue; foreach (int i in l) { double d = (p[i] - q).LengthSquared; if (d < bd) { bd = d; best = i; } } return best; }
                if (r > maxR) return -1; r *= 2;
            }
        }
    }
}
