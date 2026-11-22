using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using g3;

// The oracle of the audit. It shares no code with SolidBooleanPipeline or the stages it chains: it reads triangles out of a
// DMesh3 and does everything else with its own arithmetic on raw doubles (ray casting, signed crossings, volume, the
// closedness of a result) or on its own exact dyadic numbers (Dyadic, for distances of a few ulps).
//
// What a case means is given by its "intent" meshes: the clean, closed, outward-oriented operands before the corpus damages
// them. The truth at a point p is op(inside_1(p), ..., inside_n(p)), inside_i(p) = the signed crossing number of a ray from p
// with intent mesh i is positive. The result of the pipeline is judged at the same points by its own signed crossing number.
static class Oracle
{
    public sealed class Tris
    {
        public double[] P;      // 9 doubles per triangle, relative to Origin
        public int Count;
        public double Ox, Oy, Oz;
    }

    public static Tris Of(DMesh3 m, Vector3d origin)
    {
        var t = new Tris { P = new double[9 * m.TriangleCount], Ox = origin.x, Oy = origin.y, Oz = origin.z };
        foreach (int tid in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(tid);
            for (int j = 0; j < 3; ++j) {
                Vector3d v = m.GetVertex(tv[j]);
                t.P[9 * t.Count + 3 * j] = v.x - origin.x;
                t.P[9 * t.Count + 3 * j + 1] = v.y - origin.y;
                t.P[9 * t.Count + 3 * j + 2] = v.z - origin.z;
            }
            t.Count++;
        }
        return t;
    }

    // One ray from p (already relative to the origin) in direction d: the signed crossings (exits minus entries) and their
    // number. False if a crossing is too close to an edge, a vertex or the start to be trusted (the caller takes another ray).
    static bool cast(Tris T, double px, double py, double pz, double dx, double dy, double dz, out int signed, out int count)
    {
        signed = 0; count = 0;
        double[] P = T.P;
        const double tol = 1e-9;
        for (int i = 0; i < T.Count; ++i) {
            int o = 9 * i;
            double ax = P[o], ay = P[o + 1], az = P[o + 2];
            double e1x = P[o + 3] - ax, e1y = P[o + 4] - ay, e1z = P[o + 5] - az;
            double e2x = P[o + 6] - ax, e2y = P[o + 7] - ay, e2z = P[o + 8] - az;
            double hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x;
            double det = e1x * hx + e1y * hy + e1z * hz;
            double scale = Math.Sqrt((e1x * e1x + e1y * e1y + e1z * e1z) * (e2x * e2x + e2y * e2y + e2z * e2z));
            if (scale == 0)
                continue;       // (a triangle with a zero edge has no area)
            double sx = px - ax, sy = py - ay, sz = pz - az;
            if (Math.Abs(det) < 1e-10 * scale) {
                // nearly parallel: a miss unless the ray runs in the triangle's plane (then: not to be trusted)
                double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                double nn = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nn == 0)
                    continue;
                double dist = Math.Abs(sx * nx + sy * ny + sz * nz) / nn;
                if (dist < 1e-9 * Math.Sqrt(scale))
                    return false;
                continue;
            }
            double inv = 1 / det;
            double u = (sx * hx + sy * hy + sz * hz) * inv;
            if (u < -tol || u > 1 + tol)
                continue;
            double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
            double v = (dx * qx + dy * qy + dz * qz) * inv;
            if (v < -tol || u + v > 1 + tol)
                continue;
            double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            if (t < -tol)
                continue;
            if (u < tol || v < tol || u + v > 1 - tol || t < tol)
                return false;
            count++;
            signed += det < 0 ? 1 : -1;     // det = -(d . normal): det < 0 is an exit through a front face
        }
        return true;
    }

    // the signed crossing number of the surface around p (the winding number of a closed surface); int.MinValue if no ray
    // could be trusted (p on or within 1e-9 of the surface)
    public static int Winding(Tris T, Vector3d p, Random rng)
    {
        int parity;
        return Winding(T, p, rng, out parity);
    }

    public static int Winding(Tris T, Vector3d p, Random rng, out int parity)
    {
        parity = 0;
        if (T.Count == 0)
            return 0;
        double px = p.x - T.Ox, py = p.y - T.Oy, pz = p.z - T.Oz;
        for (int attempt = 0; attempt < 24; ++attempt) {
            double dx, dy, dz, n;
            do {
                dx = 2 * rng.NextDouble() - 1; dy = 2 * rng.NextDouble() - 1; dz = 2 * rng.NextDouble() - 1;
                n = dx * dx + dy * dy + dz * dz;
            } while (n < 0.05 || n > 1);
            n = Math.Sqrt(n);
            int s, c;
            if (cast(T, px, py, pz, dx / n, dy / n, dz / n, out s, out c)) {
                parity = c & 1;
                return s;
            }
        }
        return int.MinValue;
    }

    // the volume by the divergence theorem, relative to the origin of the Tris (a local one)
    public static double Volume(Tris T)
    {
        double v6 = 0;
        double[] P = T.P;
        for (int i = 0; i < T.Count; ++i) {
            int o = 9 * i;
            v6 += P[o] * (P[o + 4] * P[o + 8] - P[o + 5] * P[o + 7])
                - P[o + 1] * (P[o + 3] * P[o + 8] - P[o + 5] * P[o + 6])
                + P[o + 2] * (P[o + 3] * P[o + 7] - P[o + 4] * P[o + 6]);
        }
        return v6 / 6;
    }

    // closed as a set of oriented triangles: every directed edge, by vertex position, is used as often as its reverse
    public static bool Closed(DMesh3 m)
    {
        var balance = new Dictionary<(Vector3d, Vector3d), int>();
        foreach (int tid in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(tid);
            for (int j = 0; j < 3; ++j) {
                Vector3d a = m.GetVertex(tv[j]), b = m.GetVertex(tv[(j + 1) % 3]);
                if (a.x == b.x && a.y == b.y && a.z == b.z)
                    continue;
                bool fwd = less(a, b);
                var key = fwd ? (a, b) : (b, a);
                int c;
                balance.TryGetValue(key, out c);
                balance[key] = c + (fwd ? 1 : -1);
            }
        }
        foreach (int c in balance.Values)
            if (c != 0)
                return false;
        return true;
    }

    // the volumes of the connected components (triangles joined through vertices at one position), each relative to one
    // of its own vertices, so that a small component far from the origin keeps its digits
    public static List<double> ComponentVolumes(DMesh3 m)
    {
        var id = new Dictionary<Vector3d, int>();
        var parent = new List<int>();
        Func<int, int> find = null;
        find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
        var tris = new List<(int, int, int)>();
        var where = new List<Vector3d>();
        foreach (int tid in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(tid);
            var k = new int[3];
            for (int j = 0; j < 3; ++j) {
                Vector3d v = m.GetVertex(tv[j]);
                if (!id.TryGetValue(v, out k[j])) {
                    k[j] = parent.Count; id[v] = k[j]; parent.Add(k[j]); where.Add(v);
                }
            }
            parent[find(k[0])] = find(k[1]);
            parent[find(k[1])] = find(k[2]);
            tris.Add((k[0], k[1], k[2]));
        }
        var v6 = new Dictionary<int, double>();
        foreach (var (a, b, c) in tris) {
            int r = find(a);
            Vector3d o = where[r], x = where[a] - o, y = where[b] - o, z = where[c] - o;
            double d = x.x * (y.y * z.z - y.z * z.y) - x.y * (y.x * z.z - y.z * z.x) + x.z * (y.x * z.y - y.y * z.x);
            v6.TryGetValue(r, out double sum);
            v6[r] = sum + d;
        }
        var vols = new List<double>();
        foreach (double d in v6.Values)
            vols.Add(d / 6);
        vols.Sort();
        return vols;
    }

    static bool less(Vector3d a, Vector3d b)
    {
        if (a.x != b.x) return a.x < b.x;
        if (a.y != b.y) return a.y < b.y;
        return a.z < b.z;
    }

    public sealed class Judgement
    {
        public int Samples, Ambiguous, Mismatch;
        public int FalseSolid, FalseEmpty;      // the result is solid where the truth is empty, and the reverse
        public double Fraction { get { return Samples > 0 ? (double)Mismatch / Samples : 0; } }
    }

    // sample points: uniform in the (inflated) box of all meshes, and near the surfaces of the given meshes
    public static Vector3d[] SamplePoints(IList<DMesh3> meshes, int n, int seed)
    {
        var rng = new Random(seed);
        AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
        var tris = new List<(Vector3d, Vector3d, Vector3d)>();
        foreach (DMesh3 m in meshes)
            foreach (int tid in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(tid);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                box.Contain(a); box.Contain(b); box.Contain(c);
                tris.Add((a, b, c));
            }
        var pts = new Vector3d[n];
        if (tris.Count == 0)
            return pts;
        double diag = box.DiagonalLength;
        Vector3d lo = box.Min - 0.05 * diag * Vector3d.One, ext = box.Diagonal + 0.1 * diag * Vector3d.One;
        for (int i = 0; i < n; ++i) {
            if (i % 2 == 0) {
                pts[i] = lo + new Vector3d(rng.NextDouble() * ext.x, rng.NextDouble() * ext.y, rng.NextDouble() * ext.z);
            } else {
                var t = tris[rng.Next(tris.Count)];
                double u = rng.NextDouble(), v = rng.NextDouble();
                if (u + v > 1) { u = 1 - u; v = 1 - v; }
                Vector3d q = t.Item1 + u * (t.Item2 - t.Item1) + v * (t.Item3 - t.Item1);
                double r = diag * Math.Pow(10, -1 - 3 * rng.NextDouble());
                pts[i] = q + r * new Vector3d(2 * rng.NextDouble() - 1, 2 * rng.NextDouble() - 1, 2 * rng.NextDouble() - 1);
            }
        }
        return pts;
    }

    // truth[i] at the points: 1 solid, 0 empty, -1 not decided (a point on a surface)
    public static sbyte[] Truth(IList<DMesh3> intent, Func<bool[], bool> op, Vector3d[] pts, Vector3d origin, int seed)
    {
        var T = new Tris[intent.Count];
        for (int k = 0; k < T.Length; ++k)
            T[k] = Of(intent[k], origin);
        var truth = new sbyte[pts.Length];
        Parallel.For(0, pts.Length, i => {
            var rng = new Random(seed * 7919 + i);
            var inside = new bool[T.Length];
            for (int k = 0; k < T.Length; ++k) {
                int w = Winding(T[k], pts[i], rng);
                if (w == int.MinValue) { truth[i] = -1; return; }
                inside[k] = w > 0;
            }
            truth[i] = (sbyte)(op(inside) ? 1 : 0);
        });
        return truth;
    }

    public static Judgement Judge(DMesh3 result, sbyte[] truth, Vector3d[] pts, Vector3d origin, int seed)
    {
        Tris R = Of(result, origin);
        var verdict = new sbyte[pts.Length];
        Parallel.For(0, pts.Length, i => {
            if (truth[i] < 0) { verdict[i] = -1; return; }
            var rng = new Random(seed * 104729 + i);
            int w = Winding(R, pts[i], rng);
            if (w == int.MinValue) { verdict[i] = -1; return; }
            verdict[i] = (sbyte)(w > 0 ? 1 : 0);
        });
        var j = new Judgement();
        for (int i = 0; i < pts.Length; ++i) {
            if (verdict[i] < 0) { j.Ambiguous++; continue; }
            j.Samples++;
            if (verdict[i] != truth[i]) {
                j.Mismatch++;
                if (verdict[i] == 1) j.FalseSolid++; else j.FalseEmpty++;
            }
        }
        return j;
    }
}

// An exact dyadic number m * 2^e (every finite double is one): sums and products are exact. Used for the distance of a point
// from a plane where that distance is a few units of rounding of the coordinates.
readonly struct Dyadic
{
    public readonly BigInteger M;
    public readonly int E;

    public Dyadic(BigInteger m, int e) { M = m; E = e; }

    public static Dyadic Of(double x)
    {
        if (x == 0)
            return new Dyadic(BigInteger.Zero, 0);
        long bits = BitConverter.DoubleToInt64Bits(x);
        int exp = (int)((bits >> 52) & 0x7FF);
        long man = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp = 1; else man |= 1L << 52;
        return new Dyadic(bits < 0 ? -man : man, exp - 1075);
    }

    public static Dyadic operator +(Dyadic a, Dyadic b)
    {
        if (a.E >= b.E)
            return new Dyadic((a.M << (a.E - b.E)) + b.M, b.E);
        return new Dyadic(a.M + (b.M << (b.E - a.E)), a.E);
    }

    public static Dyadic operator -(Dyadic a, Dyadic b) { return a + new Dyadic(-b.M, b.E); }
    public static Dyadic operator *(Dyadic a, Dyadic b) { return new Dyadic(a.M * b.M, a.E + b.E); }

    public double ToDouble()
    {
        if (M.IsZero)
            return 0;
        // keep 64 significant bits, then scale
        BigInteger m = BigInteger.Abs(M);
        int e = E;
        int extra = (int)m.GetBitLength() - 64;
        if (extra > 0) { m >>= extra; e += extra; }
        double r = Math.ScaleB((double)m, e);
        return M.Sign < 0 ? -r : r;
    }

    // the exact determinant (b - a) . ((c - a) x (d - a)): 6 times the signed volume of the tetrahedron
    public static Dyadic Orient(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
    {
        Dyadic ax = Of(a.x), ay = Of(a.y), az = Of(a.z);
        Dyadic bx = Of(b.x) - ax, by = Of(b.y) - ay, bz = Of(b.z) - az;
        Dyadic cx = Of(c.x) - ax, cy = Of(c.y) - ay, cz = Of(c.z) - az;
        Dyadic dx = Of(d.x) - ax, dy = Of(d.y) - ay, dz = Of(d.z) - az;
        return bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx);
    }
}
