// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using g3;

// Traversal kernels of the prototype. Decisions: a lane the filter certifies as a miss is dropped; every other lane goes
// to the library's public exact test (IntrRay3Triangle3.Intersects), so the hit set equals the library's. The
// filter bound is a copy of ExactPredicates.Orient3d's (without the underflow terms), for timing only.
static class Kernels
{
    const double B3 = (7.0 + 56.0 * 1.1102230246251565e-16) * 1.1102230246251565e-16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Edge(double ux, double uy, double uz, double vx, double vy, double vz, double wx, double wy, double wz)
    {
        double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
        double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
        double perm = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy)) + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
            + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
        double bound = B3 * perm;
        if (det > bound) return 1;
        if (-det > bound) return -1;
        return 0;
    }

    // bit l set: lane l is a certified miss of the line (o, p)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MissMaskScalar(double[] soa, int leaf, int count, Vector3d o, Vector3d p)
    {
        double ux = p.x - o.x, uy = p.y - o.y, uz = p.z - o.z;
        int mask = 0, b = leaf * 36;
        for (int l = 0; l < count; ++l) {
            double ax = soa[b + l] - o.x, ay = soa[b + 4 + l] - o.y, az = soa[b + 8 + l] - o.z;
            double bx = soa[b + 12 + l] - o.x, by = soa[b + 16 + l] - o.y, bz = soa[b + 20 + l] - o.z;
            double cx = soa[b + 24 + l] - o.x, cy = soa[b + 28 + l] - o.y, cz = soa[b + 32 + l] - o.z;
            int s0 = Edge(ux, uy, uz, ax, ay, az, bx, by, bz);
            int s1 = Edge(ux, uy, uz, bx, by, bz, cx, cy, cz);
            if (s0 * s1 < 0) { mask |= 1 << l; continue; }
            int s2 = Edge(ux, uy, uz, cx, cy, cz, ax, ay, az);
            if (s0 * s2 < 0 || s1 * s2 < 0) mask |= 1 << l;
        }
        return mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void EdgeV(Vector256<double> ux, Vector256<double> uy, Vector256<double> uz, Vector256<double> vx, Vector256<double> vy,
        Vector256<double> vz, Vector256<double> wx, Vector256<double> wy, Vector256<double> wz, out Vector256<double> pos, out Vector256<double> neg)
    {
        var uyvz = uy * vz; var uzvy = uz * vy; var uzvx = uz * vx; var uxvz = ux * vz; var uxvy = ux * vy; var uyvx = uy * vx;
        var det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
        var perm = Vector256.Abs(wx) * (Vector256.Abs(uyvz) + Vector256.Abs(uzvy)) + Vector256.Abs(wy) * (Vector256.Abs(uzvx) + Vector256.Abs(uxvz))
            + Vector256.Abs(wz) * (Vector256.Abs(uxvy) + Vector256.Abs(uyvx));
        var bound = Vector256.Create(B3) * perm;
        pos = Vector256.GreaterThan(det, bound);
        neg = Vector256.GreaterThan(-det, bound);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int MissMaskSimd(double[] soa, int leaf, int count, Vector3d o, Vector3d p)
    {
        fixed (double* s = &soa[leaf * 36]) {
            var ox = Vector256.Create(o.x); var oy = Vector256.Create(o.y); var oz = Vector256.Create(o.z);
            var ux = Vector256.Create(p.x - o.x); var uy = Vector256.Create(p.y - o.y); var uz = Vector256.Create(p.z - o.z);
            var ax = Vector256.Load(s) - ox; var ay = Vector256.Load(s + 4) - oy; var az = Vector256.Load(s + 8) - oz;
            var bx = Vector256.Load(s + 12) - ox; var by = Vector256.Load(s + 16) - oy; var bz = Vector256.Load(s + 20) - oz;
            var cx = Vector256.Load(s + 24) - ox; var cy = Vector256.Load(s + 28) - oy; var cz = Vector256.Load(s + 32) - oz;
            EdgeV(ux, uy, uz, ax, ay, az, bx, by, bz, out var p0, out var n0);
            EdgeV(ux, uy, uz, bx, by, bz, cx, cy, cz, out var p1, out var n1);
            EdgeV(ux, uy, uz, cx, cy, cz, ax, ay, az, out var p2, out var n2);
            var miss = (p0 & n1) | (n0 & p1) | (p0 & n2) | (n0 & p2) | (p1 & n2) | (n1 & p2);
            return (int)miss.ExtractMostSignificantBits() & ((1 << count) - 1);
        }
    }

    // slab test; returns entry t (>= 0 clamp) or +inf for a miss
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Slab(ref Bvh.Node n, double ox, double oy, double oz, double ix, double iy, double iz, double tmax)
    {
        double t0 = 0, t1 = tmax, a, b, t;
        a = (n.x0 - ox) * ix; b = (n.x1 - ox) * ix; if (a > b) { t = a; a = b; b = t; }
        if (a > t0) t0 = a; if (b < t1) t1 = b;
        a = (n.y0 - oy) * iy; b = (n.y1 - oy) * iy; if (a > b) { t = a; a = b; b = t; }
        if (a > t0) t0 = a; if (b < t1) t1 = b;
        a = (n.z0 - oz) * iz; b = (n.z1 - oz) * iz; if (a > b) { t = a; a = b; b = t; }
        if (a > t0) t0 = a; if (b < t1) t1 = b;
        return t0 <= t1 ? t0 : double.PositiveInfinity;
    }

    public static int Closest(Bvh bvh, in Ray3d ray0, int[] stack, double[] stackT, bool simd)
    {
        Ray3d ray = ray0;
        Vector3d o = ray.Origin, d = ray.Direction;
        Vector3d p = Second.Point(o, d);
        double ix = 1 / d.x, iy = 1 / d.y, iz = 1 / d.z;
        var nodes = bvh.nodes; var soa = bvh.soa; var leafTri = bvh.leafTri; var mesh = bvh.mesh;
        double nearest = float.MaxValue; int nearId = DMesh3.InvalidID;
        int sp = 0;
        double t0 = Slab(ref nodes[0], o.x, o.y, o.z, ix, iy, iz, nearest);
        if (double.IsPositiveInfinity(t0)) return nearId;
        stack[0] = 0; stackT[0] = t0; sp = 1;
        Triangle3d tri = new Triangle3d();
        while (sp > 0) {
            --sp;
            if (stackT[sp] > nearest) continue;
            int ni = stack[sp];
            ref Bvh.Node nd = ref nodes[ni];
            if (nd.count > 0) {
                int miss = simd ? MissMaskSimd(soa, nd.a, nd.count, o, p) : MissMaskScalar(soa, nd.a, nd.count, o, p);
                for (int l = 0; l < nd.count; ++l) {
                    if ((miss >> l & 1) != 0) continue;
                    int ti = leafTri[nd.a * 4 + l];
                    mesh.GetTriVertices(ti, ref tri.V0, ref tri.V1, ref tri.V2);
                    if (IntrRay3Triangle3.Intersects(ref ray, ref tri.V0, ref tri.V1, ref tri.V2, out double rt)
                        && (rt < nearest || (rt == nearest && ti < nearId))) { nearest = rt; nearId = ti; }
                }
            } else {
                double ta = Slab(ref nodes[nd.a], o.x, o.y, o.z, ix, iy, iz, nearest);
                double tb = Slab(ref nodes[nd.a + 1], o.x, o.y, o.z, ix, iy, iz, nearest);
                if (ta < tb) {         // push far first
                    if (tb <= nearest) { stack[sp] = nd.a + 1; stackT[sp++] = tb; }
                    if (ta <= nearest) { stack[sp] = nd.a; stackT[sp++] = ta; }
                } else {
                    if (ta <= nearest) { stack[sp] = nd.a; stackT[sp++] = ta; }
                    if (tb <= nearest) { stack[sp] = nd.a + 1; stackT[sp++] = tb; }
                }
            }
        }
        return nearId;
    }

    public static readonly Vector3d InsideDir = new Vector3d(0.331960519038825, 0.462531727525156, 0.822111072077288);

    // crossings of the ray from q along InsideDir (generic position only: no symbolic perturbation in this prototype)
    public static int Crossings(Bvh bvh, Vector3d q, int[] stack, bool simd)
    {
        Vector3d d = InsideDir, p = Second.Point(q, d);
        double ix = 1 / d.x, iy = 1 / d.y, iz = 1 / d.z;
        var nodes = bvh.nodes; var soa = bvh.soa; var leafTri = bvh.leafTri; var mesh = bvh.mesh;
        int sp = 0, cnt = 0;
        if (double.IsPositiveInfinity(Slab(ref nodes[0], q.x, q.y, q.z, ix, iy, iz, double.MaxValue))) return 0;
        stack[sp++] = 0;
        Triangle3d tri = new Triangle3d();
        while (sp > 0) {
            ref Bvh.Node nd = ref nodes[stack[--sp]];
            if (nd.count > 0) {
                int miss = simd ? MissMaskSimd(soa, nd.a, nd.count, q, p) : MissMaskScalar(soa, nd.a, nd.count, q, p);
                for (int l = 0; l < nd.count; ++l) {
                    if ((miss >> l & 1) != 0) continue;
                    int ti = leafTri[nd.a * 4 + l];
                    mesh.GetTriVertices(ti, ref tri.V0, ref tri.V1, ref tri.V2);
                    int s0 = ExactPredicates.Orient3d(q, p, tri.V0, tri.V1);
                    int s1 = ExactPredicates.Orient3d(q, p, tri.V1, tri.V2);
                    int s2 = ExactPredicates.Orient3d(q, p, tri.V2, tri.V0);
                    if (s0 != 0 && s0 == s1 && s0 == s2) {
                        int so = ExactPredicates.Orient3d(tri.V0, tri.V1, tri.V2, q);
                        if (so == -s0) cnt++;
                    }
                }
            } else {
                if (!double.IsPositiveInfinity(Slab(ref nodes[nd.a], q.x, q.y, q.z, ix, iy, iz, double.MaxValue))) stack[sp++] = nd.a;
                if (!double.IsPositiveInfinity(Slab(ref nodes[nd.a + 1], q.x, q.y, q.z, ix, iy, iz, double.MaxValue))) stack[sp++] = nd.a + 1;
            }
        }
        return cnt;
    }

    // 6D (rays) / 3D (points) Morton keys for the coherence sort
    public static ulong RayKey(in Ray3d r, double lo, double ext)
    {
        ulong key = 0;
        Span<uint> c = stackalloc uint[6];
        c[0] = Q10((r.Origin.x - lo) / ext); c[1] = Q10((r.Origin.y - lo) / ext); c[2] = Q10((r.Origin.z - lo) / ext);
        c[3] = Q10(r.Direction.x * 0.5 + 0.5); c[4] = Q10(r.Direction.y * 0.5 + 0.5); c[5] = Q10(r.Direction.z * 0.5 + 0.5);
        for (int bit = 9; bit >= 0; --bit)
            for (int k = 0; k < 6; ++k) key = (key << 1) | ((c[k] >> bit) & 1);
        return key;
    }
    static uint Q10(double v) { return (uint)Math.Max(0, Math.Min(1023, (int)(v * 1024))); }
}
