// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using g3;

// Prototype of a batch-query BVH for the upper-bound measurement (public API of g3 only). NOT library code: the ray-box
// padding and the filter bound below are estimates for timing, not certified. Layout: triangles in Morton order, leaves
// of at most 4 triangles packed as 9 vectors of 4 doubles (SoA, one lane per triangle) so that the scalar and the
// Vector256 filter read the same data.
sealed class Bvh
{
    public const int Lanes = 4;
    public struct Node { public double x0, y0, z0, x1, y1, z1; public int a, count; }   // count 0: inner, children a, a+1
    public Node[] nodes; public int nodeCount;
    public int[] leafTri;           // leaf li: ids of its triangles (4 per leaf, -1 pad)
    public double[] soa;            // leaf li: 9 * 4 doubles: ax ay az bx by bz cx cy cz, each 4 lanes
    public readonly DMesh3 mesh;

    public Bvh(DMesh3 m)
    {
        mesh = m;
        int n = m.TriangleCount;     // compact mesh assumed (procedural)
        var key = new ulong[n]; var ids = new int[n];
        var bb = m.GetBounds(); double ex = Math.Max(bb.Width, Math.Max(bb.Height, bb.Depth));
        for (int t = 0; t < n; ++t) {
            Vector3d a = m.GetVertex(m.GetTriangle(t).a), b = m.GetVertex(m.GetTriangle(t).b), c = m.GetVertex(m.GetTriangle(t).c);
            Vector3d ce = (a + b + c) / 3;
            key[t] = Morton((ce.x - bb.Min.x) / ex, (ce.y - bb.Min.y) / ex, (ce.z - bb.Min.z) / ex); ids[t] = t;
        }
        Array.Sort(key, ids);
        int leaves = 0;
        nodes = new Node[4 * (n / 2 + 4)];
        leafTri = new int[Lanes * (n / 2 + 4) * 2]; soa = new double[9 * Lanes * (n / 2 + 4) * 2];
        nodeCount = 1; BuildRange(0, 0, n, ids, ref leaves);
        Array.Resize(ref nodes, nodeCount);
    }

    public static ulong Morton(double x, double y, double z)
    {
        return Spread(Q(x)) | (Spread(Q(y)) << 1) | (Spread(Q(z)) << 2);
    }
    static ulong Q(double v) { return (ulong)Math.Max(0, Math.Min(2097151, (long)(v * 2097152))); }
    static ulong Spread(ulong v)
    {
        v &= 0x1fffff; v = (v | v << 32) & 0x1f00000000ffff; v = (v | v << 16) & 0x1f0000ff0000ff;
        v = (v | v << 8) & 0x100f00f00f00f00f; v = (v | v << 4) & 0x10c30c30c30c30c3; v = (v | v << 2) & 0x1249249249249249;
        return v;
    }

    void BuildRange(int node, int s, int e, int[] ids, ref int leaves)
    {
        if (e - s <= Lanes) {
            int li = leaves++;
            nodes[node].count = e - s; nodes[node].a = li;
            double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = -x0, y1 = -x0, z1 = -x0;
            for (int l = 0; l < Lanes; ++l) {
                int t = l < e - s ? ids[s + l] : -1; leafTri[li * Lanes + l] = t;
                Vector3d a, b, c;
                if (t >= 0) {
                    var tr = mesh.GetTriangle(t); a = mesh.GetVertex(tr.a); b = mesh.GetVertex(tr.b); c = mesh.GetVertex(tr.c);
                    foreach (var v in new[] { a, b, c }) {
                        x0 = Math.Min(x0, v.x); y0 = Math.Min(y0, v.y); z0 = Math.Min(z0, v.z);
                        x1 = Math.Max(x1, v.x); y1 = Math.Max(y1, v.y); z1 = Math.Max(z1, v.z);
                    }
                } else a = b = c = new Vector3d(double.NaN, double.NaN, double.NaN);
                int o = li * 9 * Lanes + l;
                soa[o] = a.x; soa[o + 4] = a.y; soa[o + 8] = a.z; soa[o + 12] = b.x; soa[o + 16] = b.y; soa[o + 20] = b.z;
                soa[o + 24] = c.x; soa[o + 28] = c.y; soa[o + 32] = c.z;
            }
            Pad(ref nodes[node], x0, y0, z0, x1, y1, z1);
            return;
        }
        int m = s + (e - s) / 2;
        int c0 = nodeCount; nodeCount += 2; nodes[node].a = c0; nodes[node].count = 0;
        BuildRange(c0, s, m, ids, ref leaves); BuildRange(c0 + 1, m, e, ids, ref leaves);
        ref Node p = ref nodes[node]; ref Node l0 = ref nodes[c0]; ref Node l1 = ref nodes[c0 + 1];
        p.x0 = Math.Min(l0.x0, l1.x0); p.y0 = Math.Min(l0.y0, l1.y0); p.z0 = Math.Min(l0.z0, l1.z0);
        p.x1 = Math.Max(l0.x1, l1.x1); p.y1 = Math.Max(l0.y1, l1.y1); p.z1 = Math.Max(l0.z1, l1.z1);
    }

    static void Pad(ref Node n, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        double pad = 1e-9 * Math.Max(x1 - x0, Math.Max(y1 - y0, z1 - z0)) + 1e-300;     // estimate, not certified
        n.x0 = x0 - pad; n.y0 = y0 - pad; n.z0 = z0 - pad; n.x1 = x1 + pad; n.y1 = y1 + pad; n.z1 = z1 + pad;
    }
}

// The ray's second point, the same as the library's (internal) ExactRayTriangle.SecondPoint
static class Second
{
    public static Vector3d Point(Vector3d origin, Vector3d direction)
    {
        double mo = Math.Max(Math.Abs(origin.x), Math.Max(Math.Abs(origin.y), Math.Abs(origin.z)));
        double md = Math.Max(Math.Abs(direction.x), Math.Max(Math.Abs(direction.y), Math.Abs(direction.z)));
        if (!(md > 0 && md <= double.MaxValue && mo <= double.MaxValue)) return origin;
        int m = 0;
        if (mo > 0) { m = Exp(mo) - Exp(md) + 1; m = Math.Max(-1000, Math.Min(1000, m)); }
        double s = BitConverter.Int64BitsToDouble((long)(m + 1023) << 52);
        return origin + new Vector3d(direction.x * s, direction.y * s, direction.z * s);
    }
    static int Exp(double x) { return (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023; }
}
